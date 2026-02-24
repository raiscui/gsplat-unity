## Why

当前 Gsplat 的排序触发是“按相机触发”的.
在 SRP(URP/HDRP) 下,我们使用 `RenderPipelineManager.beginCameraRendering` 来保证 SceneView(隐藏相机)也能稳定触发 sort.

但在真实项目里,经常会同时存在很多相机(反射/探针/多视口/辅助 camera).
当 splat 数量在 >1M 且 <10M 时,同一帧内对多个相机重复做 GPU sort,会导致性能线性恶化,编辑与运行体验都会被击穿.

## What Changes

- 增加一个“相机模式”设置,用来决定 Gsplat 是否对所有相机排序+渲染:
  - `AllCameras`: 保持兼容行为,任意相机都可以看到 Gsplat.
  - `ActiveCameraOnly`: 只保证 1 个 ActiveCamera 的正确性与性能.
- 默认启用 `ActiveCameraOnly`(性能优先):
  - 只对 ActiveCamera 执行 GPU sort.
  - 也只对 ActiveCamera 提交 draw call,避免出现“某相机渲染了但没 sort”的错误组合.
- ActiveCamera 的选择规则(高层行为,细节在 design/specs):
  - Editor 非 Play: 按窗口焦点在 SceneView/GameView 之间切换.
  - Play/Player build: 始终选择 Game/VR 相机,优先 `Camera.main`,并支持显式 override.
- 增加回归测试与文档说明,让该模式的行为可验证、可预期.

## Capabilities

### New Capabilities
- `gsplat-camera-selection`: 定义 ActiveCameraOnly/AllCameras 的行为契约,以及 Editor/Play 模式下 ActiveCamera 的选择规则与 override 机制.

### Modified Capabilities
- (无)

## Impact

- 影响的代码区域(预计):
  - `Runtime/GsplatSettings.cs`: 增加相机模式设置.
  - `Runtime/GsplatSorter.cs`: 增加 ActiveCamera 解析/缓存,并在 SRP/BiRP 回调里对非 ActiveCamera 直接跳过排序.
  - `Runtime/GsplatRendererImpl.cs`: 在 ActiveCameraOnly 下只对 ActiveCamera 提交 draw call.
  - `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`: Editor 拖动/重绘时按焦点窗口触发刷新(配合 ActiveCamera).
  - `Editor/GsplatSettingsProvider.cs`: 在 Project Settings 里暴露新设置.
  - `Tests/Editor/*`: 增加 ActiveCamera 选择规则的 EditMode 回归测试.
- 用户可见影响:
  - 当默认启用 `ActiveCameraOnly` 时,非 ActiveCamera(例如某些辅助 camera/探针)将不再渲染 Gsplat.
  - 若项目依赖“多相机都要看到 Gsplat”,需要把模式切回 `AllCameras`.
