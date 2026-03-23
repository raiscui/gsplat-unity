## [2026-03-23 17:24:59] [Session ID: 20260323_8] 问题: RadarScan 小于 1px 的粒子容易消失

### 现象
- 用户反馈: `jitter` 已经可以接受,但 `size < 1` 的粒子会消失或显示不稳定。
- 默认 `LegacySoftEdge` 配置下更容易出现。

### 原因
- 真实 subpixel 半径虽然不再被 fragment 强行钳到 `1px`,但 billboard 几何仍按真实 `<1px` 尺寸直接缩小。
- 在默认 `LegacySoftEdge` 路径下,额外 AA fringe 不生效,导致几何 footprint 与 coverage 支撑都过小。
- 标准像素中心采样下可能完全打不到片元,于是视觉上表现为“粒子消失”。

### 修复
- 在 `Runtime/Shaders/GsplatLidarPassCore.hlsl` 中新增:
  - `ResolveLidarSubpixelCoverageSupportPx`
  - `ResolveLidarCoveragePadPx`
- 对 `0 < radius < 1px` 的点额外保留 `1px` coverage support,但不修改真实 `pointRadius` 语义。
- 顶点与片元两侧统一使用该 support 宽度,让 subpixel 点在 Legacy / Analytic / A2C 路径下都能保留最基本的 raster / coverage 支撑。
- 同步更新 `Tests/Editor/GsplatLidarShaderPropertyTests.cs` 的 shader 契约断言。

### 验证
- `dotnet build ../../Gsplat.Tests.Editor.csproj -v minimal` -> 成功, `0 warning / 0 error`
- Unity EditMode 包级测试:
  - `passed=117 failed=3 skipped=3`
  - 本次相关 LiDAR 测试全部 Passed
  - 剩余 3 个失败集中在 `GsplatVisibilityAnimationTests`,与本修复无直接对应关系
