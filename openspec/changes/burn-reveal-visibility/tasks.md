## 1. Shader: burn reveal + noise

- [x] 1.1 在 `Runtime/Shaders/Gsplat.shader` 增加显隐动画 uniforms(`_VisibilityMode/_VisibilityProgress/_VisibilityCenterModel/...`).
- [x] 1.2 在 `Runtime/Shaders/Gsplat.hlsl` 增加轻量 hash noise 函数(无贴图,无 sin),输出 `noise01/noiseSigned`.
- [x] 1.3 在 `Runtime/Shaders/Gsplat.shader` 的 `vert()` 集成 reveal/burn 逻辑: 径向 mask + 环形 glow + 噪波边界扰动 + 灰烬颗粒,并做好 NaN/alpha clamp 防御.

## 2. Runtime: uniforms 下发与状态机

- [x] 2.1 在 `Runtime/GsplatRendererImpl.cs` 增加一组 `Shader.PropertyToID` 与 `SetVisibilityUniforms(...)`,集中写入 `MaterialPropertyBlock`.
- [x] 2.2 在 `Runtime/GsplatRenderer.cs` 增加显隐动画配置字段与公开 API(`SetVisible/PlayShow/PlayHide`),并实现 show/hide 状态机(含 Hidden 时 `Valid=false` 的门禁).
- [x] 2.3 在 `Runtime/GsplatSequenceRenderer.cs` 镜像实现同样字段/API/状态机,并复用 `GsplatRendererImpl.SetVisibilityUniforms(...)`.
- [x] 2.4 在 draw 提交链路中(含 EditMode SRP 相机回调路径),确保每次渲染前都会把本帧显隐 uniforms 推到 shader(避免 Update/CameraCallback 行为漂移).

## 3. Editor: Inspector 按钮

- [x] 3.1 修改 `Editor/GsplatRendererEditor.cs`,在 Inspector 底部增加 "Show" / "Hide" 两个按钮,分别调用 `PlayShow()`/`PlayHide()`.
- [x] 3.2 新增 `Editor/GsplatSequenceRendererEditor.cs`,为 `GsplatSequenceRenderer` 提供同样的 Show/Hide 按钮.

## 4. Tests: 最小回归

- [x] 4.1 新增 EditMode 单测: 调用 `PlayHide()` 后在短 duration 内进入 Hidden,并使 `Valid=false`(从根源停止 sorter gather).
- [x] 4.2 新增 EditMode 单测: 调用 `PlayShow()` 会从 Hidden/不可见态进入 Showing,并恢复 `Valid=true`(在资源有效时).

## 5. Docs/Changelog

- [x] 5.1 更新 `CHANGELOG.md`,记录新增的显隐动画能力与公开 API/Inspector 按钮(默认关闭,不影响旧行为).

## 6. Enhancements: size scaling + warp distortion

- [x] 6.1 在 `Runtime/Shaders/Gsplat.shader` 增加 show/hide 期间的 splat size 缩放(show: 极小->正常; hide: 正常->极小),并应用到 `corner.offset`.
- [x] 6.2 在 `Runtime/Shaders/Gsplat.shader` 增加基于 noise 的 modelCenter 扭曲位移,让位置变化明显,同时保持 ring/visible 判定稳定.
- [x] 6.3 在 `Runtime/GsplatRenderer.cs`/`Runtime/GsplatSequenceRenderer.cs` 在 Showing/Hiding 期间保守扩展 bounds,避免 CPU culling 裁掉扭曲位移后的 splats.
- [x] 6.4 更新 `CHANGELOG.md` 说明 size/warp 的行为改进.

## 7. Tuning: stronger warp particles + slower size easing

- [x] 7.1 在 `Runtime/GsplatRenderer.cs`/`Runtime/GsplatSequenceRenderer.cs` 增加 `WarpStrength` 参数,并在 shader 增加新 uniform `_VisibilityWarpStrength` 用于放大位移扭曲(不必通过拉高 NoiseStrength 才能看到明显位移).
- [x] 7.2 调整 `Runtime/Shaders/Gsplat.shader` 的 warp 逻辑: 引入 per-splat phase offset + 各轴不同的时间推进 + globalWarp 权重,让 show/hide 期间的 pos 位移更明显且更像“扭曲空间”的粒子效果.
- [x] 7.3 调整 `Runtime/Shaders/Gsplat.shader` 的 size easing(例如 pow+smoothstep),让 show 的 grow 更慢更明显,hide 的 shrink 更容易被肉眼感知.
- [x] 7.4 更新 `CHANGELOG.md` 与 OpenSpec spec/design,说明新增可调参数与调优后的默认观感.

## 8. Tuning: hide shrink stronger + smoke-like noise field

- [x] 8.1 在 `Runtime/Shaders/Gsplat.shader` 增强 hide 的 shrink: 使用更强的指数曲线,并叠加 global progress shrink,让未被扫到区域也会逐渐变小(避免体感一直很大).
- [x] 8.2 在 `Runtime/Shaders/Gsplat.hlsl` 增加 3D value noise(8-corner hash + trilinear),并在 `Runtime/Shaders/Gsplat.shader` 用它替换白噪声式 hash noise,同时加入轻量 domain warp,让噪波更像烟雾的扭曲与波动.
- [x] 8.3 更新 OpenSpec spec/design 记录“烟雾感噪波”的设计决定,并回归测试验证 shader 编译与单测不回退.

## 9. Tuning: show/hide widths split + hide ring outside / trail inside

- [x] 9.1 在 `Runtime/GsplatRenderer.cs`/`Runtime/GsplatSequenceRenderer.cs` 将 `RingWidthNormalized/TrailWidthNormalized` 拆分为 show/hide 两组配置(例如 `ShowRingWidthNormalized/.../HideTrailWidthNormalized`),并用 `FormerlySerializedAs` 保持旧场景/Prefab 的序列化兼容.
- [x] 9.2 在 `Runtime/GsplatRenderer.cs`/`Runtime/GsplatSequenceRenderer.cs` 下发 uniforms 时按 mode(show/hide)选择对应的 ring/trail 宽度.
- [x] 9.3 在 `Runtime/Shaders/Gsplat.shader` 调整:\n  - show: size grow 更快一些,避免 ring 阶段全是很小的点点,但仍从极小开始.\n  - hide: ring 更像“前沿”,主要出现在外侧,让 trail(渐隐)更自然地落在内侧(避免体感“trail 在外”).\n- [x] 9.4 更新 `CHANGELOG.md` 与 OpenSpec spec/design,记录 show/hide 宽度拆分与 hide ring/trail 语义调整.

## 10. Editor: animation should play without mouse movement (force repaint while animating)

- [x] 10.1 在 `Runtime/GsplatRenderer.cs`/`Runtime/GsplatSequenceRenderer.cs` 中,当处于 Showing/Hiding 且为 Editor 非 Play 模式时,主动请求 `QueuePlayerLoopUpdate + RepaintAllViews`,保证 show/hide 动画无需鼠标交互也能连续播放到结束.
- [x] 10.2 增加节流与 batchmode 门禁,避免空闲耗电与测试环境干扰.
- [x] 10.3 更新 `CHANGELOG.md` 与 OpenSpec spec/design,记录该 Editor 行为改进,并跑 EditMode tests 回归.
- [x] 10.4 增加可控诊断事件(复用 `GsplatSettings.EnableEditorDiagnostics` 的 ring buffer),记录 show/hide 的 state/ticker/repaint,用于定位“为何看起来不播放”.

## 11. Tuning: ring expansion uses easeInOutQuad

- [x] 11.1 在 `Runtime/Shaders/Gsplat.shader` 中,将 burn reveal 的扩散半径从线性 `radius=progress*(...)` 改为 `radius=easeInOutQuad(progress)*(...)`,并让与扩散强相关的全局效果(glow/globalWarp/globalShrink)使用同一套 eased progress,保证观感一致.

## 12. Noise: warp noise mode dropdown + curl-like field

- [x] 12.1 在 `Runtime/GsplatRenderer.cs`/`Runtime/GsplatSequenceRenderer.cs` 增加 `VisibilityNoiseMode` 下拉枚举(默认 ValueSmoke,保持当前行为不变).
- [x] 12.2 在 `Runtime/GsplatRendererImpl.cs` 增加 `_VisibilityNoiseMode` uniform 的下发(每帧写入 MPB),并在 `Runtime/Shaders/Gsplat.shader` 声明该 uniform.
- [x] 12.3 在 `Runtime/Shaders/Gsplat.hlsl` 实现 value noise 的梯度计算,并基于 curl(A)=∇×A 构造 curl-like 向量场.
- [x] 12.4 在 `Runtime/Shaders/Gsplat.shader` 中根据 `_VisibilityNoiseMode` 切换:
  - ValueSmoke/HashLegacy: 维持现有 tangent/bitangent 混合 warp.
  - CurlSmoke: 使用 curl-like 向量场生成更连续的“旋涡/流动”扭曲方向.

## 13. Tuning: glow semantics + show glow boost + earlier hide shrink

- [x] 13.1 在 `Runtime/GsplatRenderer.cs`/`Runtime/GsplatSequenceRenderer.cs` 增加 `ShowGlowStartBoost` 参数,并在 shader 增加 `_VisibilityShowGlowStartBoost` uniform 下发.
- [x] 13.2 调整 hide 的 glow 语义:
  - 前沿 ring 使用 `HideGlowStartBoost` 进行更亮的 boost,且不再随扩散向外变弱(避免外围突兀).
  - 增加内侧 afterglow tail,并随“向内”衰减(中心方向),让衰减方向朝内而不是朝外.
- [x] 13.3 调整 hide 的 size shrink 提前开始:
  - 让 ring(glow)出现时 splat 已明显变小,更符合“燃烧前沿”观感.

## 14. Tuning: hide size quickly shrinks then lingers (easeOutCirc)

- [x] 14.1 在 `Runtime/Shaders/Gsplat.shader` 为 hide 的 size shrink 改为 `easeOutCirc` 风格:
  - 燃烧前沿附近先迅速 shrink 到“较小但仍可见”的 minScale.
  - 后续更多依赖 alpha trail 慢慢消失,避免 size 过早接近 0 导致“看起来消失太快”.

## 15. Tuning: show has inner afterglow tail (brighter interior)

- [x] 15.1 在 `Runtime/Shaders/Gsplat.shader` 为 show 增加内侧 afterglow tail:
  - tail 只出现在燃烧前沿内侧(edgeDist<=0),并朝内衰减.
  - tail 从 ring 内侧开始(避免与 ring 叠加过曝),用于补足用户反馈的“内部不够亮”.

## 16. Tuning: show/hide 统一“前沿在外侧”+ show 内侧更亮

- [x] 16.1 在 `Runtime/Shaders/Gsplat.shader` 进一步统一与强化语义:
  - ring 作为“燃烧前沿”,在 show/hide 期间都主要出现在外侧(edgeDist>=0).
  - 内侧 afterglow/tail 只出现在内侧(edgeDist<=0),并朝内衰减,保证“内部更亮,外围不突兀”.
  - show: 为了避免 premul alpha 把内侧 afterglow 吃掉,允许 tail 提供一个受限的 alpha 下限,让内部余辉肉眼可见.

## 17. Tuning: fix hide end lingering splats (fade/shrink ignore outward jitter)

- [x] 17.1 修复 hide 末尾“残留一些高斯基元很久才消失”的问题:
  - 原因: 当边界噪声为正时,会把 edgeDist 往外推,导致局部 passed 永远达不到 1,从而在末尾 lingering.
  - 策略:
    - ring/glow 仍使用完整的 `edgeDistNoisy`,保留燃烧边界抖动质感.
    - 但 hide 的 alpha fade 与 size shrink 使用更稳态的 `edgeDistForFade`:
      仅允许噪声往内咬(`min(noiseSigned,0)`),不允许往外推,确保最终一定能烧尽.

## 18. Tuning: show ring glow sparkle (curl noise twinkle)

- [x] 18.1 show 的 ring glow 增加“火星/星星闪烁”亮度变化:
  - 使用 curl-like 噪声场生成稀疏亮点(sparkMask),并用随时间变化的噪声相位生成 twinkle.
  - 提供可调参数 `ShowGlowSparkleStrength`(0=关闭).

## 19. Tuning: adjust default show widths (ring +10%, trail *40%)

- [x] 19.1 调整 show 的默认宽度参数(仅影响新加组件/Reset 的默认值,不强制迁移旧场景):
  - `ShowRingWidthNormalized` 默认值扩大 10%: `0.06 -> 0.066`
  - `ShowTrailWidthNormalized` 默认值乘以 40%: `0.12 -> 0.048`

## 20. Retraction: "+10%/*40%" refers to splat size (not ring/trail spatial width)

- [x] 20.1 撤回 show 的默认宽度微调,恢复 `ShowRingWidthNormalized=0.06` 与 `ShowTrailWidthNormalized=0.12`.
- [x] 20.2 增加“粒子大小(高斯基元尺寸)”调参项,并由 shader 在 ring/tail 阶段使用它,避免 ring 前沿全是很小的点点:
  - 新增 runtime 字段: `ShowSplatMinScale/ShowRingSplatMinScale/ShowTrailSplatMinScale/HideSplatMinScale`
  - 新增 shader uniforms: `_VisibilityShowMinScale/_VisibilityShowRingMinScale/_VisibilityShowTrailMinScale/_VisibilityHideMinScale`
- [x] 20.3 更新 OpenSpec design/spec 与 `CHANGELOG.md`,并跑 Unity EditMode tests 回归.

## 21. Tuning: hide afterglow should linger longer (alpha + size)

- [x] 21.1 调整 hide 的 afterglow 余辉:
  - alpha fade: 对 hide 的 passed 做轻量 easing(先慢后快),让余辉存在时间更长.
  - size shrink: 拆成两段:
    - 前沿到来前预收缩到一个 afterglow size.
    - 前沿扫过后在 tail 内再慢慢 shrink 到最终 `HideSplatMinScale`,避免 glow 一过就直接变到极小.
- [x] 21.2 更新 OpenSpec/CHANGELOG/四文件记录,并跑 Unity EditMode tests 回归.

## 22. Tuning: hide warp MUST NOT push trail to the outer rim

- [x] 22.1 在 `Runtime/Shaders/Gsplat.shader` 限制 hide 阶段 warp 的径向外推分量:
  - 允许切向扭曲与径向内咬(更像烟雾流动/被吸入燃烧中心).
  - 禁止径向外推,避免把“内侧拖尾(afterglow)”视觉上推到外圈,造成 "trail 在外" 的错觉.
- [x] 22.2 更新 OpenSpec/CHANGELOG/四文件记录,并跑 Unity EditMode tests 回归.
