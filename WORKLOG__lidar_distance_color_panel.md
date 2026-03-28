## [2026-03-28 13:00:50] [Session ID: 019d32c5-3334-71e2-84bc-b7a60390dc20] 任务名称: 雷达点云距离颜色面板可编辑

### 任务内容
- 为 RadarScan / LiDAR 的距离着色新增近处颜色与远处颜色字段
- 同步更新静态与序列两套 Inspector
- 把颜色参数接入 LiDAR draw 参数链、shader 属性和测试

### 完成过程
- 先确认当前现象不是单纯 Inspector 漏画, 而是 shader 里的 Depth 颜色本来就写死在 `DepthToCyanRed(...)`
- 在 `GsplatRenderer` / `GsplatSequenceRenderer` 中增加 `LidarDepthNearColor` / `LidarDepthFarColor`
- 在 `GsplatLidarScan.RenderPointCloud(...)` 中新增颜色参数与 shader property 下发
- 在 `GsplatLidarPassCore.hlsl` 中增加 `DepthToConfiguredGradient(...)`
- 同步修改 `GsplatLidar.shader` / `GsplatLidarAlphaToCoverage.shader` 的隐藏属性声明
- 同步修改 `GsplatRendererEditor` / `GsplatSequenceRendererEditor`
- 补齐 `GsplatLidarScanTests` 与 `GsplatLidarShaderPropertyTests`
- 更新 `README.md` / `CHANGELOG.md`

### 总结感悟
- 这类“只是想在面板上露个字段”的需求, 很容易实际牵出序列化、材质属性和 shader 三层链路, 不能只停在 Inspector。
- 对已有视觉路径做参数化时, 兼容老默认值往往比“一刀切换成新算法”更稳。
