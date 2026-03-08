## Why

当前 RadarScan(LiDAR) 粒子虽然已经有固定 soft-edge,但在小半径、高对比背景和运动扫描头场景里,边缘仍然容易出现锯齿、闪烁和发硬的观感. 用户现在希望把 RadarScan 粒子的抗锯齿能力正式做成可选项,并能在几种常见 AA 路线之间按画质与性能取舍.

## What Changes

- 为 `GsplatRenderer` 与 `GsplatSequenceRenderer` 增加 RadarScan 粒子抗锯齿模式选项.
- 为 RadarScan 粒子定义一组明确的可选模式:
  - `LegacySoftEdge`
  - `AnalyticCoverage`
  - `AlphaToCoverage`
  - `AnalyticCoveragePlusAlphaToCoverage`
- 保持现有场景升级后的稳定性:
  - 未显式切换新模式时,旧项目应继续保持当前固定 soft-edge 的视觉语义.
- 在 `AlphaToCoverage` 相关模式下补齐可用性与 fallback 语义:
  - 仅在 MSAA 可用时启用 A2C.
  - 当 MSAA 不可用时,系统必须回退到稳定且可预期的本地 shader AA 路线,而不是进入不可预测状态.
- 更新 Inspector、README、CHANGELOG 与自动化测试,覆盖 AA 模式枚举、fallback 语义和 shader 契约.
- 明确本 change 的边界:
  - 不把 FXAA / SMAA / TAA 这类全屏后处理型 AA 伪装成 RadarScan 组件内选项.
  - 不改变当前 RadarScan 的颜色、深度、show/hide 和 external-hit 语义.

## Capabilities

### New Capabilities
- `gsplat-lidar-particle-antialiasing`: 定义 RadarScan 粒子的抗锯齿模式、模式选择语义、MSAA 相关 fallback 规则,以及它们与现有 LiDAR 显示语义的兼容边界.

### Modified Capabilities
- (无)

## Impact

- Affected runtime:
  - `Runtime/GsplatUtils.cs`
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
  - `Runtime/Lidar/GsplatLidarScan.cs`
  - `Runtime/GsplatSettings.cs`
- Affected shaders:
  - `Runtime/Shaders/GsplatLidar.shader`
  - 可能新增 LiDAR A2C 专用 shader shell 或材质资源
- Affected editor:
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`
- Affected tests/docs:
  - `Tests/Editor/GsplatLidarScanTests.cs`
  - `Tests/Editor/GsplatLidarShaderPropertyTests.cs`
  - `README.md`
  - `CHANGELOG.md`
