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

- 在显隐分支中对 `modelCenter` 增加基于 hash noise 的位移扰动(扭曲粒子效果).
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
- shader 侧对 hide 做语义校正:
  - ring 更像“燃烧前沿”,主要出现在外侧(未燃烧侧).
  - trail(渐隐)更自然地落在内侧(已燃烧区域),避免体感“trail 在外”.

**Why:**

- show/hide 的最佳观感参数通常不同(例如 hide 想要更厚的拖尾与更强的灰烬感).
- hide 的 ring 如果在边界两侧都发光,很容易与内侧的渐隐叠在一起,产生“拖尾好像跑到外面”的错觉.

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
