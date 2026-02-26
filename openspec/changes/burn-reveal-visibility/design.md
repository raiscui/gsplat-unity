## Context

当前 Gsplat 的可见性控制主要依赖:

- 直接禁用组件(`enabled=false`)或禁用物体(`SetActive(false)`).
- 或者关闭后端渲染开关(例如 `EnableGsplatBackend=false`).

这些方式都是“硬切”。
对于大规模点云/高斯基元,硬切会显得突兀,也缺少叙事化的“出场/退场”效果。

本 change 要实现一个可选的显隐动画:

- show: 初始完全不可见,从中心出现燃烧发光环并向外扩散,扫过区域逐步稳定显示.
- hide: 从中心起燃更强的发光环向外扩散,扫过区域慢慢透明消失,并且噪波越来越大像碎屑.

额外约束:

- hide 不能依赖 `SetActive(false)`/`enabled=false`,否则动画无法播放.
- 动画结束后必须能“真正停止”排序与渲染开销(不能只把 alpha 乘成 0).
- 包作为 UPM package,不应强依赖项目侧的 vInspector,因此 Inspector 按钮要用包内 Editor 脚本实现.

## Goals / Non-Goals

**Goals:**

- 在主后端(`Gsplat.shader`)实现燃烧环 reveal/burn 效果:
  - 径向阈值(mask)控制“已显示区域”.
  - 环形 glow 提供明显的燃烧边缘.
  - 噪波同时作用于“边界抖动”与“灰烬颗粒”,并且随 show/hide 阶段变化:
    - show: 越稳定噪波越弱.
    - hide: 越烧掉噪波越强.
- 在 `GsplatRenderer`/`GsplatSequenceRenderer` 增加最小但完整的状态机与 API:
  - `PlayShow()`/`PlayHide()`/`SetVisible(...)`.
  - 动画结束后进入 Hidden 状态并让 `Valid=false`,从根源停止 sorter gather 与 draw 提交.
- 在 Inspector 增加 "Show" / "Hide" 两个按钮,便于验证与调参.
- 增加最小回归测试,锁定状态机的关键行为(例如 hide 播完后进入 Hidden 并停开销).

**Non-Goals:**

- 不实现 VFX Graph 后端的同款显隐动画(后续可作为二期扩展).
- 不引入噪声贴图资产(本期使用轻量 hash noise,避免新增资源与依赖).
- 不改变默认渲染表现与性能(默认 `EnableVisibilityAnimation=false` 时保持旧行为).

## Decisions

### 1) Shader 侧用“uniform 控制的分支”而不是新增 shader variants

**Decision:**

- 在 `Gsplat.shader` 增加 `_VisibilityMode` 等 uniforms.
- 仅当 `_VisibilityMode!=0` 时执行 reveal/burn 计算.

**Why:**

- uniform 分支对 GPU 来说是同一 draw 内一致的,通常代价可控.
- 避免引入 keyword 组合导致材质 variants 膨胀.
- 默认 mode=0 时,不会改变现有路径,也便于回归.

### 2) 中心坐标以“用户指定 Transform”为主,并转换到 model space

**Decision:**

- 在 C# 每帧把 `VisibilityCenter.position` 通过 `transform.InverseTransformPoint` 转到 model space.
- 若中心为空,回退使用 bounds.center.

**Why:**

- 点云数据与 bounds 都在对象空间,shader 读取的 `_PositionBuffer` 也在对象空间.
- 用 model space 计算 `dist=length(pos-center)` 最直观且不受相机影响.

### 3) 最大半径由 bounds 8 个角点到中心的最大距离推导

**Decision:**

- 用 local bounds 的 8 个 corner 计算 `maxRadius`,保证环最终能覆盖整个对象.
- show/hide 的 ring/trail 宽度都用 normalized 相对比例(例如 `ShowRingWidthNormalized`/`HideTrailWidthNormalized`),再换算成 model space 的实际长度.

**Why:**

- 这样用户调参是“相对尺度”,不同资产大小也能得到相似观感.
- 中心可能不在 bounds.center,用 max corner distance 更稳.

### 4) 噪波使用更平滑的 value noise + 轻量 domain warp,并随 show/hide 改变强度

**Decision:**

- 在 HLSL 里实现不依赖贴图/`sin` 的 3D value noise:
  - 8 个格点 hash + trilinear 插值,空间上更平滑,更像烟雾/流体的连续变化.
  - 用轻量 domain warp(用一个噪声扭曲采样域),让形态更像“烟雾团簇 + 流动”.
- 定义 `noiseWeight`:
  - show: `noiseWeight = 1 - passed`(越稳定越干净).
  - hide: `noiseWeight = passed`(越烧掉越碎).
- 噪波同时作用于:
  - 边界扰动(`edgeDistNoisy`),让环边缘像燃烧抖动.
  - 灰烬颗粒(`ashMul`),让透明度呈现碎屑感.

**Why:**

- 不引入贴图资产,对包来说更易维护.
- 用同一套 noise 参数同时控制两类效果,更容易调参和讲清楚行为.

### 5) “真正隐藏并停开销”通过 `Valid=false` 实现,而不是只把 alpha 乘 0

**Decision:**

- hide 结束后进入 Hidden 状态.
- 在 Hidden 状态下让组件 `Valid=false`,使 sorter 的 gather 直接跳过该对象,从根源停止 sort/draw.

**Why:**

- 只把 alpha 乘 0 仍会做 GPU sort 与 draw,对大规模点云不划算.
- `Valid` 是现有系统的关键门禁,复用它最稳,也最符合“改良胜过新增”.

### 6) Inspector 按钮用包内 CustomEditor(IMGUI),不依赖 vInspector attributes

**Decision:**

- 保留 `Editor/GsplatRendererEditor.cs` 的自定义 Inspector,在底部追加 "Show" / "Hide" 按钮.
- 新增 `Editor/GsplatSequenceRendererEditor.cs`,提供同样按钮.

**Why:**

- UPM package 不能假设项目一定安装 vInspector,直接引用会导致编译失败.
- 现有 `GsplatRenderer` 已有 CustomEditor,vInspector 的 `[Button]` 机制不一定能接管,按钮也不一定会显示.
- IMGUI 按钮实现简单且确定.

### 7) 显隐期间同时做“高斯基元尺寸变化”(极小 <-> 正常)

**Decision:**

- 在 `Gsplat.shader` 的显隐分支中,基于 `passed`(燃烧环扫过的局部进度)计算 `visibilitySizeMul`.
  - show: `visibilitySizeMul` 从极小平滑增大到 1.
  - hide: `visibilitySizeMul` 从 1 平滑缩小到极小.
- 在 `ClipCorner` 之后把 `corner.offset *= visibilitySizeMul`,让几何真正变小/变大.

**Why:**

- 单靠 alphaMask 会让 splat “一下子就是正常大小”,缺少“从无到有”的动感.
- 把进度绑定到 `passed` 可以让每个 splat 在被扫到时自然长大/缩小,观感更像燃烧扩散.
- 在 `ClipCorner` 之后缩放,且 `ClipCorner` 仍只看 baseAlpha,可以避免 alphaMask 导致几何尺寸抖动.

### 8) 噪波增加“空间扭曲位移”,并配套 bounds 扩展避免 CPU culling

**Decision:**

- 在显隐分支中对 `modelCenter` 增加基于连续噪声场的位移扰动(扭曲粒子效果).
- 增加一个独立的位移强度倍率 `WarpStrength`,并通过新 uniform `_VisibilityWarpStrength` 下发,用于在不必拉高 `NoiseStrength` 的情况下获得更明显的 pos 位移.
- 在 shader 中引入 per-splat phase offset + 各轴不同的时间推进 + globalWarp 权重,让扭曲位移更明显且更像“扭曲空间”的粒子效果,同时避免整片区域同步抖动.
- 为了稳定阈值,`passed/ring` 的判定仍基于 `modelCenterBase`(未位移的中心)计算.
- 在 C# 侧,当处于 Showing/Hiding 时保守扩展 render bounds,避免位移后的 splats 被 Unity CPU culling 裁掉.

**Why:**

- 用户需要的“扭曲空间一样”的噪波,核心是 position 发生明显变化,而不是仅 alpha 抖动.
- 阈值判定如果使用位移后的 pos,会产生“阈值抖动”,体感像闪烁.
- Unity 的 `RenderParams.worldBounds` 会参与 CPU culling,不扩 bounds 会在边缘产生“突然消失”的不连续.

## Risks / Trade-offs

- [风险] reveal/burn 计算会增加 vertex shader 计算量.
  - 缓解: 默认 `_VisibilityMode=0` 时跳过全部额外计算.
- [风险] alpha 很小时 `ClipCorner` 内部 `log(alpha)` 可能产生 NaN.
  - 缓解: 调用 `ClipCorner` 前把 alpha clamp 到一个很小的正数(例如 `max(alpha, 1e-6)`),并对不可见 splat 提前 discard.
- [风险] 显隐期间的位移扭曲不参与 sorter 的排序 key(排序仍基于原始 PositionBuffer).
  - 影响: 极端情况下可能出现短暂的半透明排序误差.
  - 缓解: 位移只在动画期间启用,并将位移幅度限制在“观感明显但不破坏整体”范围内.
- [取舍] 本期不做 VFX Graph 后端的同款显隐动画.
  - 缓解: 在 tasks 里记录为后续扩展候选(需要改 shadergraph/VFX 输出链路).

### 9) show/hide 的 ring/trail 宽度分别可调,并校正 hide 的“前沿/拖尾”方位

**Decision:**

- 将 `RingWidthNormalized/TrailWidthNormalized` 拆为两组参数:
  - show: `ShowRingWidthNormalized` / `ShowTrailWidthNormalized`
  - hide: `HideRingWidthNormalized` / `HideTrailWidthNormalized`
- 通过 `FormerlySerializedAs` 保持旧场景/Prefab 的序列化兼容(旧字段值迁移到 show 侧).
- shader 侧统一 show/hide 的语义:
  - ring 更像“燃烧前沿”,show/hide 都主要出现在外侧(未燃烧侧,edgeDist>=0).
  - trail(渐隐)与 afterglow(余辉)更自然地落在内侧(已燃烧侧,edgeDist<=0),并朝内衰减,
    让“内部更亮,外围不突兀”,避免体感“trail 在外”.

**Why:**

- show/hide 的最佳观感参数通常不同(例如 hide 想要更厚的拖尾与更强的灰烬感).
- ring 如果在边界两侧都发光,很容易与内侧的渐隐/余辉叠在一起,产生“拖尾好像跑到外面”的错觉.
  同时对 show 来说,也更容易出现“前沿亮但内部不够亮”的体感落差.

### 10) Editor 下 show/hide 动画期间主动 Repaint,避免“鼠标不动就不播放”

**Decision:**

- 在 `GsplatRenderer`/`GsplatSequenceRenderer` 的显隐状态机推进(`AdvanceVisibilityStateIfNeeded`)中:
  - 当处于 Showing/Hiding 且为 Editor 非 Play 模式时,主动请求:
    - `EditorApplication.QueuePlayerLoopUpdate()`
    - `InternalEditorUtility.RepaintAllViews()`
  - 做轻量节流(例如 60fps 上限),避免 Editor update 频率过高时刷屏/耗电.
  - 在 batchmode/tests 环境下跳过 Repaint,避免无意义调用与潜在不稳定因素.
  - 当动画刚结束(Showing/Hiding -> Visible/Hidden)时,额外补 1 次强制刷新,避免停在“最后一帧之前”的错觉.
- 增加一个 EditorApplication.update 驱动的 ticker(只在 Showing/Hiding 期间注册):
  - 目的: 打破“必须先 repaint 才能 tick,但必须先 tick 才会继续请求 repaint”的鸡生蛋循环.
  - ticker 每次 update 会主动调用 `AdvanceVisibilityStateIfNeeded()`,从而持续驱动 show/hide 动画推进与 repaint 请求.
  - 动画结束后自动注销,避免空闲耗电与 Editor 常驻开销.
- 诊断增强(可控):
  - 当 `GsplatSettings.EnableEditorDiagnostics=true` 时,记录 show/hide 的 `[VIS_STATE]/[VIS_REPAINT]` 事件到 ring buffer,
    便于通过 `Tools/Gsplat/Dump Editor Diagnostics` 一次性 dump 出证据,判断“是否在持续 tick / 是否在持续请求 repaint”.

**Why:**

- Unity Editor 的 SceneView/GameView 在 EditMode 下往往是“事件驱动 repaint”.
  - 用户鼠标不动时,视口不会持续刷新,体感像“动画卡住/不播放”.
- show/hide 是纯 shader/uniform 动画,不主动 repaint 就无法看到连续帧.
- 把 Repaint 限定在动画期间,能在体验与性能之间取得更稳态的平衡.

### 11) 燃烧环扩散速度使用 easeInOutQuad(非匀速)

**Decision:**

- 在 `Runtime/Shaders/Gsplat.shader` 中,扩散半径不再使用线性速度:
  - from: `radius = progress * (maxRadius + trailWidth)`
  - to: `radius = easeInOutQuad(progress) * (maxRadius + trailWidth)`
- 与扩散强相关的“全局随时间变化”的效果(例如 hide 的 glow 衰减,globalWarp/globalShrink)也使用同一套 eased progress,
  避免 ring 的运动与全局效果节奏脱钩.

**Why:**

- 用户希望 show/hide 的燃烧扩散更“像动画”,而不是匀速推进.
- `easeInOutQuad` 的特征是:
  - 起始更慢(更像“点燃/聚能”)
  - 中段更快(扩散更明显)
  - 末尾逐渐减速(收尾更自然,减少最后一瞬间的突兀感)

### 12) 噪声模式可切换: ValueSmoke / CurlSmoke / HashLegacy

**Decision:**

- 在 `GsplatRenderer`/`GsplatSequenceRenderer` 增加 `VisibilityNoiseMode` 下拉枚举(默认 `ValueSmoke`).
- shader 增加 `_VisibilityNoiseMode` uniform,由 `GsplatRendererImpl.SetVisibilityUniforms(...)` 每帧下发.
- 在 shader 中根据 `_VisibilityNoiseMode` 切换 warp 噪声场:
  - `ValueSmoke`: 沿用现有 value noise + domain warp,并用两个标量噪声混合 tangent/bitangent 生成 warp 方向.
  - `CurlSmoke`: 基于 value noise 的梯度/旋度构造 curl-like 向量场,生成更连续的“旋涡/流动”warp 方向.
  - `HashLegacy`: 旧版对照(更碎更抖),用于调试或性能基线.

**Why:**

- 用户需要“更平滑/更像烟雾流动”的扭曲,但也希望能快速切回当前效果做对照,避免调参时迷失.
- 默认值保持 `ValueSmoke`,可以保证升级后旧项目观感不被意外改变.
- `CurlSmoke` 会更贵,但它只在 show/hide 动画期间启用,因此整体成本可控.

### 13) Glow 语义调优: show 也有 StartBoost,hide 增加“朝内衰减”的 tail

**Decision:**

- show 增加 `ShowGlowStartBoost`:
  - 用于在 show 起始阶段增强燃烧环的“点燃瞬间更亮”的冲击力.
  - `ShowGlowIntensity` 仍作为全局强度,StartBoost 只用于起始阶段的额外放大.
- show 的 glow 也遵循“前沿更亮 + 内侧余辉”的语义:
  - 前沿 ring(外侧)始终更亮(可被 StartBoost 放大).
  - 内侧 afterglow/tail 朝内衰减,用于补足“内部不够亮”.
  - 由于本 shader 使用 premul alpha 输出,为了让内侧 afterglow 肉眼可见,
    show 允许 tail 提供一个受限的 alpha 下限(只在前沿内侧,且被 ring 抑制),避免 glow 被 alpha 吃掉.
- hide 的 glow 改为“前沿更亮 + 内侧衰减的尾巴”:
  - 前沿 ring 使用 `HideGlowStartBoost` 做 boost,并避免因扩散到外围就整体变暗导致外围突兀.
  - 在 ring 的内侧增加 afterglow tail,并使其朝内(中心方向)逐渐衰减,更符合“中心先烧掉”的语义.

**Why:**

- 用户反馈的核心不是“亮不亮”,而是“衰减方向不对”:
  - 若 glow 随扩散向外整体变弱,会导致外围燃烧前沿缺乏存在感,体感突兀.
  - 更符合语义的是: 前沿一直明显,而内侧因为更早燃烧而更早冷却,因此朝内变弱.

### 14) hide 在 glow 前提前 shrink(进入 glow 时已明显变小)

**Decision:**

- hide 的 splat 尺寸 shrink 不再严格绑定 `passed`(边界处为 0),
  而是使用一个“向外提前”的 passedForSize:
  - 让 ring(glow)出现时,对应 splat 已经开始缩小,避免 glow 阶段仍像“正常大小点云在发光”.

**Why:**

- 用户期望“燃烧前沿”出现时,高斯基元已经呈现“燃烧收缩”的形态.
- 通过提前 shrink,可以不必整体后移 glow/noise 的时序,也更容易保持 reveal/burn 的阈值判定稳定.

### 15) hide size 节奏: 先迅速变小(easeOutCirc),再慢慢消失(避免 size 过早接近 0)

**Decision:**

- hide 的 size shrink 不再强依赖 `passed` 把尺寸一路压到接近 0.
- 改为在燃烧前沿附近快速 shrink 到一个非 0 的 `minScaleHide`,
  后续主要依赖 alpha trail 让它慢慢消失.
- shrink 的时间曲线使用 `easeOutCirc` 风格,实现“先快后慢”的观感.

**Why:**

- 用户反馈“hide 燃烧时粒子太大”与“看起来消失太快”的根因往往是同一个:
  - 尺寸在 glow 阶段没有足够早变小,但一旦进入 shrink 又被压到接近 0,导致肉眼感觉“瞬间消失”.
- 保持一个非 0 的 minScaleHide,能让粒子在燃烧尾巴阶段仍可见,
  再由 alpha trail 渐隐完成“慢慢消失”的节奏.

### 16) show/hide 的 ring/tail 语义统一: 前沿在外侧,余辉在内侧

**Decision:**

- ring 统一为“前沿在外侧(edgeDist>=0)”.
  - 前沿 ring 永远在最外侧先到,更符合“燃烧扩散”的直觉.
- afterglow/tail 统一为“只在内侧(edgeDist<=0),并朝内衰减”.
  - 让内部更亮,同时避免外围突兀.
- show 在 premul alpha 的约束下,给内侧 afterglow 一个受限 alpha 下限(只在前沿附近且被 ring 抑制),
  确保余辉不会被 alpha 吃掉,能稳定地被肉眼看到.

**Why:**

- 用户反馈的核心是“语义一致性”:
  - 前沿应该永远是最亮、最先到的边界.
  - 余辉应该落在已经被扫过的一侧,并且越靠近中心越冷却.
- 在 premul alpha 输出下,如果内侧余辉区域的 alpha 太低,即使加了 glow 也会被 alpha 乘没,
  视觉上就会出现“内部不够亮”的错觉.

### 17) hide 末尾残留修复: fade/shrink 不允许“边界噪声往外推”

**Decision:**

- hide 的边界噪声拆分为两条用途:
  - ring/glow: 仍使用完整的 `edgeDistNoisy`,保留燃烧边界抖动质感.
  - fade/shrink: 改用更稳态的 `edgeDistForFade`:
    - 仅允许噪声往内咬(`min(noiseSigned,0)`),不允许往外推.
    - 目的: 避免局部 passed 被“正向噪声”长期压在 <1,导致动画末尾 lingering.

**Why:**

- 用户反馈的“hide 最最后残留一些高斯基元很久才消失”本质上是:
  - `passed`/`visible` 直接跟随 `edgeDistNoisy` 时,当噪声把边界往外推,
    就相当于局部 burn front 被“拖住”,最后一圈 splats 会持续半透明可见.
- 通过只在 fade/shrink 上禁止外推,我们既保留了 ring 的抖动质感,
  也保证了 hide 在末尾能稳定烧尽并进入 Hidden.

### 18) show 的 ring glow 星火闪烁: curl noise 调制亮度

**Decision:**

- 为 show 增加一个可调参数 `ShowGlowSparkleStrength`(0=关闭).
- show 的 ring(前沿) glow 亮度使用 curl-like 噪声场做调制,形成“稀疏亮点 + 时间闪烁”的火星感:
  - 稀疏亮点(sparkMask): 由 curl 向量场的幅度经过幂次增强得到,多数区域较暗,少数区域会更亮.
  - 时间闪烁(twinkle): 使用随时间变化的噪声相位(复用已有 noise 采样),让亮点闪烁而不是静态斑点.
- 该效果只作用于 ring 前沿,不影响 tail,避免内部余辉变成噪点墙.

**Why:**

- 用户希望 show 的环状 glow 不要“纯均匀的光带”,而是像火星/星星一样闪闪.
- curl-like 噪声场在空间上连续且带旋涡感,用它做亮度调制更像“火星在流动的气流里跳动”,
  比纯 hash 抖动更自然,也更接近“星火闪烁”的观感.

### 19) (撤回) 默认参数微调: show 的 ring 更厚,trail 更短

**Decision:**

- (已撤回) 调整 show 的默认参数(仅影响“新加组件/Reset”,不对已有序列化对象做自动迁移):
  - `ShowRingWidthNormalized`: `0.06 -> 0.066`(+10%).
  - `ShowTrailWidthNormalized`: `0.12 -> 0.048`(×0.4).

**Why:**

- ring 稍微更厚,能让前沿的存在感更强,更像“燃烧前沿”.
- trail 更短会让 reveal 在前沿扫过后更快稳定为“完全可见”,从而:
  - 内部更快变亮/变实(减少“外侧亮但内部发暗”的体感).
  - 降低长拖尾带来的“半透明区域过宽”的混浊感.

**Retraction:**

- 用户后来澄清: 这里的 “+10%/*40%” 需求指的是“高斯基元(粒子)大小”,不是 ring/trail 的径向空间宽度.
- 因此最终实现选择:
  - 恢复 show 的默认宽度为 `0.06/0.12`,避免混淆.
  - 新增粒子大小参数,用 size floor 去解决“ring 阶段全是小点点”的问题(见下一节).

### 20) 粒子大小微调: ring/tail 的最小尺寸分离(避免小点点)

**Decision:**

- 恢复 show 的默认空间宽度:
  - `ShowRingWidthNormalized=0.06`
  - `ShowTrailWidthNormalized=0.12`
- 新增/下发 4 个粒子大小参数(相对正常尺寸,0..1):
  - `ShowSplatMinScale`: show 的基础最小尺寸(用于“从极小开始”).
  - `ShowRingSplatMinScale`: show 的 ring 前沿最小尺寸(用于 ring 更可读).
  - `ShowTrailSplatMinScale`: show 的 tail/afterglow 最小尺寸(用于控制拖尾粗细).
  - `HideSplatMinScale`: hide 的最小尺寸(用于控制燃烧阶段缩到多小).
- shader 侧:
  - show: 在原有 `passed -> grow` 的基础上,对 ring/tail 额外施加 size floor:
    - `ringSizeFloor`: 由 `ring` 权重把 size 拉到 `ShowRingSplatMinScale`.
    - `tailSizeFloor`: 由 `tailInside` 权重把 size 拉到 `ShowTrailSplatMinScale`.
  - hide: 继续使用现有 `easeOutCirc` shrink,并把最小值改为 `HideSplatMinScale` 可调.

**Why:**

- 用户反馈的核心不是 “ring 在空间里太薄/太厚”,而是:
  - ring 前沿可见时,由于 `passed≈0` 导致 size 仍贴近极小,视觉上变成“很小的点点”.
- 通过把“空间宽度”(ring/trail width)与“粒子大小”(splat min scale)拆开:
  - 参数语义更清楚,不再容易误解.
  - ring 前沿能保持更可读的粒子大小,同时仍保留“从极小开始长大”的动态过程.

### 21) hide 余辉增强: afterglow 更久 + 尺寸不要立刻极小

**Decision:**

- 调整 hide 的 afterglow(余辉)体验,目标是让 glow 扫过后“余辉仍能存在一段时间”,而不是一过就没:
  1) alpha fade: 对 hide 的 passed 做轻量 ease-in(平方),让衰减前段更慢,尾段更快.
     - 直观效果: 余辉存在时间更长,但不改变 passed=1 时必须完全烧尽的约束.
  2) size shrink: 拆为两段:
     - 前沿到来前: 预收缩到一个 afterglow size(让进入 glow 时已明显变小).
     - 前沿扫过后: 在 tail 内再慢慢 shrink 到最终 `HideSplatMinScale`,避免 glow 一过就直接变到极小.
- afterglow size 先使用 `HideSplatMinScale` 的派生规则(×2)作为默认,减少新增参数数量.
  - 若后续需要更精细控制(例如 ring 更小但余辉更大),再拆成独立参数.

**Why:**

- 用户反馈的关键体验是: hide 的 glow 前沿扫过之后,余辉粒子“几乎立刻全没了”.
- 通过对 hide 的 alpha/size 都做“先慢后快 + 分段收敛”的节奏,可以同时满足:
  - 进入 glow 前就变小(燃烧感更强).
  - glow 扫过后仍有余辉拖尾(更像燃烧后余烬,不会突兀断掉).

### 22) hide warp 防外推: 拖尾保持在 ring 内侧

**Decision:**

- 在 hide 阶段,限制 position warp 的“径向外推”分量:
  - 允许切向扭曲(旋涡/烟雾流动).
  - 允许径向内咬(更像被吸入燃烧中心).
  - 但禁止径向外推(不把 splat 往外圈推).

**Why:**

- reveal/burn 的 passed/ring 判定刻意不受 warp 影响,以避免阈值抖动带来的 flicker.
- 但当 warpStrength 较大时,如果 warp 把“内侧拖尾(afterglow)”的 splat 往径向外侧推,
  肉眼会产生 "HideTrail 在外圈" 的错觉(看起来像拖尾跑到了前沿 ring 外侧).
- 通过在 hide 阶段只禁止“径向外推”,可以保留烟雾式的切向流动,同时让拖尾位置更稳态地保持在 ring 的内侧.
