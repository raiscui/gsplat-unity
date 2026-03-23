## [2026-03-23 17:24:59] [Session ID: 20260323_8] 任务名称: RadarScan subpixel 粒子消失修复

### 任务内容
- 修复 RadarScan / LiDAR 粒子在 `size < 1px` 时容易消失的问题。
- 保留已经可用的 stable jitter 方向,不再把问题误判成 `warp`。
- 同步更新 shader 契约测试,避免旧断言把错误行为重新锁回去。

### 完成过程
- 先通过 shader 静态阅读确认: 当前默认 `LegacySoftEdge` 下,`<1px` 点会真的生成 `<1px` billboard 几何。
- 再用最小像素中心采样脚本验证: subpixel footprint 确实可能完全打不到任何片元。
- 然后在 `Runtime/Shaders/GsplatLidarPassCore.hlsl` 中把“真实半径”和“coverage 支撑宽度”拆开。
- 最后更新 `Tests/Editor/GsplatLidarShaderPropertyTests.cs`,并完成 C# 编译与 Unity 包级测试验证。

### 总结感悟
- subpixel 点的正确修法,不是把真实半径重新钳大,而是分离“视觉半径”和“raster / coverage 支撑半径”。
- 这类问题如果只盯 fragment alpha,很容易漏掉更早的光栅阶段退化。

## [2026-03-23 21:14:02 +0800] [Session ID: 20260323_9] 任务名称: OpenSpec 快进方案1 external capture supersampling

### 任务内容
- 继续用户指定的 `$openspec-ff-change` 流程。
- 为方案1 `lidar-external-capture-supersampling` 补齐 `design`、`specs`、`tasks` 三个 artifact。
- 把 change 推进到可直接进入实现的 apply-ready 状态。

### 完成过程
- 先读取 `openspec instructions design/specs/tasks` 的官方约束。
- 再读取 `lidar-camera-frustum-external-gpu-scan` 与 `lidar-external-targets` 两个相近 change 的 design / tasks / spec 写法。
- 同时回看 `GsplatLidarExternalGpuCapture.cs`、`Gsplat.compute` 与现有 EditMode tests,确认 `Auto / Scale / Explicit` 现在的真实语义与 point depth resolve 证据。
- 然后完成以下文件:
  - `openspec/changes/lidar-external-capture-supersampling/design.md`
  - `openspec/changes/lidar-external-capture-supersampling/specs/gsplat-lidar-external-capture-quality/spec.md`
  - `openspec/changes/lidar-external-capture-supersampling/tasks.md`
- 最后执行 `openspec status --change "lidar-external-capture-supersampling"`,确认 `4/4 artifacts complete`。

### 总结感悟
- 这次方案1最重要的设计边界,不是“怎么把边缘弄顺”,而是“先不破坏 nearest-surface 语义”。
- 当前仓库其实已经有 `Scale` 这套 API 了,更好的做法不是继续堆新参数,而是把它正式定义为 supersampling 质量入口。
