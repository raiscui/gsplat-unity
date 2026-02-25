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

## 11. Tuning: ring expansion uses easeOutCirc

- [x] 11.1 在 `Runtime/Shaders/Gsplat.shader` 中,将 burn reveal 的扩散半径从线性 `radius=progress*(...)` 改为 `radius=easeOutCirc(progress)*(...)`,并让与扩散强相关的全局效果(glow/globalWarp/globalShrink)使用同一套 eased progress,保证观感一致.
