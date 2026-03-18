## 1. Shared trigger configuration

- [x] 1.1 在 `Runtime/GsplatRenderer.cs` 与 `Runtime/GsplatSequenceRenderer.cs` 引入共享的 `DualTrackSwitchTriggerProgress01` 序列化字段,默认值为 `0.35`,并补齐 Clamp/NaN/Inf 合法化逻辑.
- [x] 1.2 让现有 `show-hide-switch-高斯` 路径改为读取共享阈值,替换当前内部写死的 `0.35` 常量,确保旧路径行为不回退.

## 2. Reverse switch runtime state model

- [x] 2.1 在 `Runtime/GsplatRenderer.cs` 为 `Gaussian -> RadarScan` 切换补齐专用 pending 状态、LiDAR show overlay 轨状态与对应的 cancel / finalize helper.
- [x] 2.2 在 `Runtime/GsplatSequenceRenderer.cs` 镜像实现同样的反向 dual-track 状态与 helper,保持静态版和序列版 runtime API 一致.

## 3. Reverse switch orchestration and gating

- [x] 3.1 实现 `show-hide-switch-雷达` 的主编排链路: 先启动 Gaussian hide,达到共享阈值后启动 Radar show,Gaussian hide 完成后再落到稳定 RadarScan 终态.
- [x] 3.2 调整 `BuildLidarShowHideOverlayForThisFrame(...)`,让反向 overlap 阶段优先输出 `Gaussian -> Radar` 专用 LiDAR show overlay,而不是误读共享 `Hiding` 状态.
- [x] 3.3 调整 `ShouldSubmitSplatsThisFrame()` 与相关 LiDAR runtime 门禁,确保反向 overlap 阶段 Gaussian splat 继续提交,但稳定 RadarScan 语义不回退.
- [x] 3.4 明确正反两个 dual-track 按钮与其它 `SetRenderStyle*` / `SetRadarScanEnabled` / `PlayShow` / `PlayHide` 入口之间的 cancel / re-entry 规则,避免连点或中断后留下半残状态.

## 4. Editor controls and user-facing semantics

- [x] 4.1 在 `Editor/GsplatRendererEditor.cs` 中加入 `show-hide-switch-雷达` 按钮,并同步更新帮助文案,明确它与 `show-hide-switch-高斯` 共同组成双向 overlap 切换.
- [x] 4.2 在 `Editor/GsplatSequenceRendererEditor.cs` 中加入同款 `show-hide-switch-雷达` 按钮与说明,保持两种 renderer 的操作入口一致.
- [x] 4.3 在 Inspector 暴露共享触发阈值字段,让用户可以调整默认 `0.35` 的双轨切换起点,并确保按钮触发前 `serializedObject.ApplyModifiedProperties()` 后读取到最新值.

## 5. Tests and verification

- [x] 5.1 扩展 `Tests/Editor/GsplatVisibilityAnimationTests.cs`,锁定 `show-hide-switch-雷达` 的前半段、触发阈值、overlap 阶段、最终稳态与门禁恢复时序.
- [x] 5.2 扩展 `Tests/Editor/GsplatVisibilityAnimationTests.cs`,锁定共享触发阈值对 `show-hide-switch-高斯` 与 `show-hide-switch-雷达` 两条路径都会生效.
- [x] 5.3 运行 `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 与相关定向 EditMode 测试,确认正反两个 dual-track 按钮与共享阈值都没有引入新的编译或时序回退.
