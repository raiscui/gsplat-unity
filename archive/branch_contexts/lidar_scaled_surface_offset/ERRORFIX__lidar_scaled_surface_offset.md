# ERRORFIX: LiDAR 缩放组件 external target 表面偏移

## [2026-03-17 23:38:46] [Session ID: lidar_scaled_surface_offset_followup_20260317_233246] 问题: CameraFrustum 模式在缩放父节点下出现近平平面弯折

### 问题现象
- 用户现场反馈:
  - 接近 `LidarFrustumCamera` 的位置,原本应当平整的地面被 LiDAR 粒子画成向下弯折。

### 原因
- 上一轮已经把 LiDAR runtime context 改成 rigid frame。
- 但 `GsplatLidarScan.TryComputeFrustumAngleBounds(...)` 仍然用 `frustumCamera.transform.InverseTransformPoint(worldPoint)` 求角域。
- 当相机父节点带缩放时:
  - frustum layout 角域落在“带 scale 的 camera local frame”
  - runtime ray/reconstruct 落在“无 scale 的 rigid frame”
  - 两套语义不一致,最终在近平处表现成几何弯折。

### 修复
- 在 `Runtime/Lidar/GsplatLidarScan.cs` 中:
  - 先用 `GsplatUtils.BuildRigidTransformMatrices(...)` 构造 rigid `worldToSensor`
  - 再用 `rigidWorldToSensor.MultiplyPoint3x4(worldPoint)` 计算 frustum sample 的局部方向

### 验证
- 失败验证:
  - `Gsplat.Tests.GsplatLidarScanTests.TryGetEffectiveLidarLayout_UsesRigidCameraFrameForAngles_WhenParentScaled`
  - 修复前在 `/tmp/gsplat_test_lidar_frustum_layout.xml` 中明确失败
- 修复后通过:
  - `Gsplat.Tests.GsplatLidarScanTests.TryGetEffectiveLidarLayout_UsesRigidCameraFrameForAngles_WhenParentScaled`
  - `Gsplat.Tests.GsplatLidarScanTests.TryGetEffectiveLidarRuntimeContext_IgnoresSensorScale_GsplatRenderer`
  - `Gsplat.Tests.GsplatLidarScanTests.TryGetEffectiveLidarRuntimeContext_IgnoresSensorScale_GsplatSequenceRenderer`
  - `Gsplat.Tests.GsplatUtilsTests.BuildRigidTransformMatrices_IgnoresScaleButPreservesPose`

## [2026-03-17 23:04:20] [Session ID: lidar_scaled_surface_offset_20260317_225305] 问题: 缩放后的高斯组件让 external target 点云偏离 mesh 表面

### 现象
- 用户反馈:
  - 在被缩放过的高斯组件上,`LidarExternalStaticTargets` 呈现出的雷达点云离开了 mesh 表面。
- 代码观察:
  - `TryGetEffectiveLidarRuntimeContext(...)` 直接使用 `sensorTransform.localToWorldMatrix/worldToLocalMatrix`
  - `RenderPointCloud(...)` 又用 `lidarLocalToWorld * (dir * range)` 重建世界点位

### 假设
- 主假设:
  - 传感器矩阵吸收了节点缩放
  - external hit 的 `range` 已经是世界射线距离
  - 所以在最终乘 `lidarLocalToWorld` 时被额外放大
- 备选解释:
  - external mesh capture / resolve 本身在缩放场景下给了错误距离

### 验证
- 静态验证:
  - CPU external helper 发射 ray 只使用 `position + rotation`
  - 说明 external hit 语义本身是世界空间射线距离,不是“带缩放的传感器局部距离”
- 动态验证:
  - `Gsplat.Tests.GsplatUtilsTests.BuildRigidTransformMatrices_IgnoresScaleButPreservesPose`
  - `Gsplat.Tests.GsplatLidarScanTests.TryGetEffectiveLidarRuntimeContext_IgnoresSensorScale_GsplatRenderer`
  - `Gsplat.Tests.GsplatLidarScanTests.TryGetEffectiveLidarRuntimeContext_IgnoresSensorScale_GsplatSequenceRenderer`
  - 三条测试在隔离工程 `/tmp/gsplat_editmode_project.yvWxpk` 中全部通过,结果文件在 `/tmp/gsplat_test_*.xml`

### 根因
- 根因已确认:
  - LiDAR 传感器 frame 不该包含节点缩放
  - 但旧实现直接使用 `localToWorldMatrix/worldToLocalMatrix`,把 scale 带进了 LiDAR 坐标系
  - 这会让 external target 的世界距离在最终重建阶段被再次按 scale 放大

### 修复
- 新增 `GsplatUtils.BuildRigidTransformMatrices(...)`
  - 只保留 `position + rotation`
  - 明确排除 `scale`
- `GsplatRenderer` / `GsplatSequenceRenderer`
  - 改为统一使用刚体 LiDAR 传感器矩阵

### 回归保护
- 新增 util 级测试,锁住“刚体矩阵忽略 scale”的数学语义
- 新增 renderer 级测试,锁住两个后端都不会再把 scale 带进 LiDAR 运行时上下文
