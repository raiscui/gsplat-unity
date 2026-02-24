## 1. Settings 与公共 API

- [x] 1.1 在 `Runtime/GsplatSettings.cs` 增加 `GsplatCameraMode`(AllCameras/ActiveCameraOnly)与 `CameraMode` 字段,并设置默认值为 `ActiveCameraOnly`
- [x] 1.2 在 `Editor/GsplatSettingsProvider.cs` 的 `Project Settings/Gsplat` UI 中暴露 `CameraMode` 配置项

## 2. ActiveCamera 解析与缓存

- [x] 2.1 在 `Runtime/GsplatSorter.cs` 增加 ActiveCamera override 接口(例如 `ActiveGameCameraOverride`),用于脚本显式指定主相机
- [x] 2.2 在 `Runtime/GsplatSorter.cs` 实现 `TryGetActiveCamera(out Camera cam)` 并按 `Time.frameCount` 缓存解析结果(同一帧只解析一次)
- [x] 2.3 实现 Play/Player build 下的 ActiveCamera 自动选择规则:
  - 候选为启用的 Game/VR 相机
  - 优先 override,否则优先 `Camera.main`,否则选 depth 最大的相机
- [x] 2.4 实现 Editor 非 Play 下的 ActiveCamera 焦点切换规则:
  - 鼠标悬停/聚焦 SceneView -> SceneView 相机(优先取鼠标所在的 SceneView)
  - 鼠标悬停/聚焦 GameView -> 按 Play 规则选 Game/VR 相机
  - 在 Inspector/Hierarchy 等非视口窗口交互时 -> 沿用上一帧的“视口 hint”,避免抖动导致闪烁
- [x] 2.5 提供一个可选的 override 组件 `Runtime/GsplatActiveCameraOverride.cs`:
  - 挂在 Game/VR Camera 上时自动写入 `GsplatSorter.ActiveGameCameraOverride`
  - 支持 `Priority`,同优先级下“最后启用者 wins”

## 3. 排序(sort)门禁

- [x] 3.1 在 `Runtime/GsplatSorter.cs` 的 `GatherGsplatsForCamera(Camera cam)` 增加门禁:
  - `AllCameras`: 保持现有行为
  - `ActiveCameraOnly`: 非 ActiveCamera 直接 `return false`
- [x] 3.2 在 SRP/BiRP 回调路径(`OnBeginCameraRendering`/`OnPreCullCamera`)验证门禁生效,确保同一帧不会对多相机重复 dispatch sort

## 4. 渲染(render)门禁与编辑器刷新

- [x] 4.1 在 `Runtime/GsplatRendererImpl.cs` 的 `Render(...)` 中实现 `ActiveCameraOnly` 渲染门禁:
  - 只对 ActiveCamera 提交 `Graphics.RenderMeshPrimitives`
  - 其它相机不渲染,避免“渲染了但没按该相机排序”的错误显示
- [x] 4.2 在 `Runtime/GsplatRenderer.cs` 与 `Runtime/GsplatSequenceRenderer.cs` 的 `OnValidate()` 中,在 `SceneView.RepaintAll()` 之外补充对当前聚焦窗口的 Repaint(配合“焦点切换”体验)
- [x] 4.3 复查并更新已有的 Editor Play Mode 开关语义(`SkipSceneViewSortingInPlayMode/SkipSceneViewRenderingInPlayMode`):
  - 在 `ActiveCameraOnly` 下它们应当仍然行为一致且不产生互相打架的特殊情况
- [x] 4.4 Metal 稳态: 在每次 draw 前重新绑定所有 StructuredBuffers,避免 buffer 绑定丢失导致 Unity 跳过 draw call

## 5. 回归测试(EditMode)

- [x] 5.1 新增 `Tests/Editor/GsplatActiveCameraOnlyTests.cs`,覆盖 `TryGetActiveCamera` 的核心规则(override 优先,单相机默认,`Camera.main` 优先)
- [x] 5.2 在测试中覆盖 `GatherGsplatsForCamera` 的门禁行为:
  - `ActiveCameraOnly` 下 ActiveCamera 返回 true,非 ActiveCamera 返回 false
- [x] 5.3 确保测试在 `-batchmode -nographics` 下不刷 error log 且可稳定运行
- [x] 5.4 新增 `Tests/Editor/GsplatActiveCameraOverrideComponentTests.cs`,锁定 override 组件的优先级与同优先级 tie-break 行为

## 6. 文档与版本

- [x] 6.1 更新 `README.md`,说明 `CameraMode` 的用途、默认值、以及需要多相机渲染时如何切回 `AllCameras`
- [x] 6.2 更新 `Documentation~/Implementation Details.md`,补充 ActiveCameraOnly 下 sort/render 的触发链路与门禁规则
- [x] 6.3 更新 `CHANGELOG.md` 记录用户可见行为变化,并 bump `package.json` 版本号

## 7. 验证清单(手工)

- [ ] 7.1 Editor 非 Play: 点 SceneView 强旋转到背后,确认排序正确; 点 GameView 确认 Main Camera 视角正确且 SceneView 不再额外消耗
- [ ] 7.2 Play 模式: GameView 始终正确; SceneView 聚焦不应劫持 ActiveCamera(性能不应出现双相机双排序)
- [ ] 7.3 多相机压力场景(反射/探针/辅助 camera): Profiler 中确认每帧 sort dispatch 次数约为 1
