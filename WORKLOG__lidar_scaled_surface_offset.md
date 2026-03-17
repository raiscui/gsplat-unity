# WORKLOG: LiDAR 缩放组件 external target 表面偏移

## [2026-03-17 23:04:20] [Session ID: lidar_scaled_surface_offset_20260317_225305] 任务名称: 修复缩放节点下 LiDAR external target 点云偏离 mesh 表面

### 任务内容
- 修复 `GsplatRenderer` 与 `GsplatSequenceRenderer` 在 LiDAR 运行时上下文里直接使用带缩放传感器矩阵的问题。
- 新增刚体 LiDAR 传感器矩阵辅助函数,并补充两组回归测试锁定语义。
- 在隔离 Unity 工程中完成真实 EditMode 测试验证。

### 完成过程
- 先沿着 `TryGetEffectiveLidarRuntimeContext -> TryRebuildRangeImage -> RenderPointCloud -> GsplatLidarPassCore.hlsl` 把矩阵链路串了一遍。
- 再对比 CPU external helper,确认它发射 ray 时只使用 `position + rotation`,没有把 scale 带进方向。
- 由此建立主假设:
  - gsplat 自身命中会在 `worldToLidar * transform.localToWorldMatrix` 中抵消缩放
  - external target 的世界距离不会抵消,所以最终重建时会被多乘一次 scale
- 修复方式:
  - 新增 `GsplatUtils.BuildRigidTransformMatrices(...)`
  - 两个 renderer 统一改为使用“忽略 scale 的 LiDAR 传感器矩阵”
- 验证方式:
  - 由于主工程被另一 Unity 实例占用,改用 `/tmp/gsplat_editmode_project.yvWxpk` 隔离工程
  - 跑通 3 条定向 EditMode 测试,全部 `Exit code 0 (Ok)`

### 总结感悟
- 这次最容易误判的点是: “缩放看起来像命中阶段的问题”, 但真正出错的是最终世界点位重建阶段。
- LiDAR 这类“距离语义先算出来,之后再重建世界点位”的系统,传感器 frame 必须是刚体矩阵,不能偷懒直接拿 `localToWorldMatrix`。

## [2026-03-17 23:38:46] [Session ID: lidar_scaled_surface_offset_followup_20260317_233246] 任务名称: 修复 CameraFrustum 模式在缩放父节点下的近平平面弯折

### 任务内容
- 修复 `GsplatLidarScan.TryComputeFrustumAngleBounds(...)` 仍然使用带 scale 相机局部坐标的问题。
- 保证 `CameraFrustum` layout、LiDAR runtime context、最终点云重建三者统一使用 rigid sensor frame。
- 在隔离 Unity 工程里完成 4 条定向 EditMode 测试验证。

### 完成过程
- 先补最小失败测试 `TryGetEffectiveLidarLayout_UsesRigidCameraFrameForAngles_WhenParentScaled`。
- 用隔离工程 `/tmp/gsplat_editmode_project.yvWxpk` 跑测试,确认它真实失败,而不是 CLI 假阳性。
- 失败后回看 `GsplatLidarScan.cs`,定位到 `frustumCamera.transform.InverseTransformPoint(worldPoint)` 仍然把父级 scale 带进角域。
- 最终改成 rigid `worldToSensor` 变换,再重跑 frustum 回归测试和上一轮 3 条缩放语义测试,全部通过。

### 总结感悟
- LiDAR 相关修复不能只盯“最终传感器矩阵”,还要检查 layout LUT 是不是在同一套坐标语义里生成。
- `CameraFrustum` 这种“相机投影 + LiDAR 重建”混合链路,最怕局部 frame 和 runtime frame 语义分叉。
