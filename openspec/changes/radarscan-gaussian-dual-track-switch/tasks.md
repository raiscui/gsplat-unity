## 1. Runtime state model

- [x] 1.1 在 `Runtime/GsplatRenderer.cs` 为 RadarScan -> Gaussian 切换补齐独立的 LiDAR hide overlay 轨状态与清理 helper.
- [x] 1.2 在 `Runtime/GsplatSequenceRenderer.cs` 镜像实现同样的 dual-track 状态与 helper,保持两条 runtime API 一致.

## 2. Runtime orchestration and gating

- [x] 2.1 重构 `PlayRadarScanToGaussianShowHideSwitch(...)` 与半程触发链路,让半程事件只启动 Gaussian show,不提前终止雷达 hide 轨.
- [x] 2.2 调整 `BuildLidarShowHideOverlayForThisFrame(...)`,让 overlap 阶段优先使用 Radar->Gaussian 专用 hide overlay 轨,继续输出完整的 hide 语义.
- [x] 2.3 调整 `ShouldSubmitSplatsThisFrame()` 与相关 LiDAR runtime 门禁,确保 overlap 阶段 Gaussian splat 可以提交,但普通纯 Radar 语义不回退.
- [x] 2.4 明确 dual-track 切换的 cancel / re-entry 规则,避免 Inspector 连点或其他 API 中断后留下半残状态.

## 3. Editor and user-facing controls

- [x] 3.1 更新 `Editor/GsplatRendererEditor.cs` 中 `show-hide-switch-高斯` 的说明文案,明确它是“双轨 overlap 切换”,不是普通渐变.
- [x] 3.2 更新 `Editor/GsplatSequenceRendererEditor.cs` 的同款按钮与说明,保持静态版和序列版一致.

## 4. Tests and verification

- [x] 4.1 扩展 `Tests/Editor/GsplatVisibilityAnimationTests.cs`,锁定“半程前只在 hide,不提前 show”的时序.
- [x] 4.2 扩展 `Tests/Editor/GsplatVisibilityAnimationTests.cs`,锁定 overlap 阶段 `Gaussian + 雷达粒子` 同屏时的 overlay 与 splat 提交门禁.
- [x] 4.3 扩展 `Tests/Editor/GsplatVisibilityAnimationTests.cs`,锁定 `EnableLidarScan` 只有在雷达 hide 轨真正完成后才关闭.
- [x] 4.4 运行 `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 与相关定向 EditMode 测试,确认双轨切换未引入新的编译或时序回退.
