## [2026-03-18 14:46:00] [Session ID: a5122445-83f8-4367-a55f-188f1411a83d] 任务名称: RadarScan 扫描动作开关

### 任务内容
- 为 `GsplatRenderer` / `GsplatSequenceRenderer` 增加一个可以关闭 RadarScan 扫描动作效果,但继续保留 LiDAR 粒子显示的开关
- 同步更新 LiDAR 渲染桥接、shader、Inspector、测试与文档

### 完成过程
- 先按"现象 -> 假设 -> 验证计划"梳理了 RadarScan 动作层:
  - 确认持续扫描动作来自 `GsplatLidarPassCore.hlsl` 中的扫描头旋转(`_LidarTime * _LidarRotationHz`)和 `trail01` 亮度调制
  - 确认 `EnableLidarScan` 不能直接拿来关闭扫描动作,否则会把 LiDAR 点云本体一起关掉
- 实现了 `LidarEnableScanMotion`:
  - 运行时组件新增序列化字段,默认 `true`
  - `RenderPointCloud(...)` 新增参数并通过 MPB 下发 `_LidarEnableScanMotion`
  - shader 关闭该开关时,直接把 `trail01` 固定为 `1.0`,从而显示稳定点云
- Inspector 同步:
  - 新开关放入 Timing 区
  - 在关闭扫描动作时,把 `LidarRotationHz`、`LidarTrailGamma` 与未扫到底色相关字段置为禁用态,减少误解
- 文档同步:
  - 更新 `README.md` 的 LiDAR 说明、示例代码和人工验证清单
  - 更新 `CHANGELOG.md`

### 总结感悟
- 这类"看起来像一个效果"的需求,落地前最好先拆清楚"runtime 开关"和"shader 表现层"是不是同一件事。
- 如果用户想保留结果,只关掉表现动作,最稳的切口往往是 shader 里的表现门控,不是上层总开关。
