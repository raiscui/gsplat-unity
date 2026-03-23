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
