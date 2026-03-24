# Notes: LiDAR 缩放组件 external target 表面偏移

## [2026-03-17 22:59:30] [Session ID: lidar_scaled_surface_offset_20260317_225305] 笔记: 首轮静态证据整理

## 来源

### 来源1: `Runtime/GsplatRenderer.cs`

- `TryGetEffectiveLidarRuntimeContext(...)` 当前直接返回:
  - `lidarLocalToWorld = sensorTransform.localToWorldMatrix`
  - `worldToLidar = sensorTransform.worldToLocalMatrix`
- `TickLidarRangeImageIfNeeded()` 中:
  - `modelToLidar = worldToLidar * transform.localToWorldMatrix`
- `RenderLidarInUpdateIfNeeded()` / `RenderLidarForCamera()` 中:
  - 最终把 `lidarLocalToWorld` 交给 `GsplatLidarScan.RenderPointCloud(...)`

### 来源2: `Runtime/GsplatSequenceRenderer.cs`

- 与 `GsplatRenderer` 使用同一套 LiDAR 传感器矩阵构造方式。
- 说明 Sequence 路径也可能带同样回退风险。

### 来源3: `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs`

- CPU external helper 发射 ray 时只用:
  - `origin = lidarSensorTransform.position`
  - `rotation = lidarSensorTransform.rotation`
  - `direction = rotation * m_localDirections[cellId]`
- 这里没有把缩放带进 ray 方向。

### 来源4: `Runtime/Shaders/GsplatLidarPassCore.hlsl`

- external hit 渲染阶段:
  - `renderRange = max(range - max(_LidarExternalHitBiasMeters, 0.0), 0.0);`
  - `worldPos = mul(_LidarMatrixL2W, float4(dirLocal * renderRange, 1.0)).xyz;`
- 说明 external hit 的最终世界点位一定受 `_LidarMatrixL2W` 影响。

## 综合发现

### 现象

- 代码上,external hit 的距离语义是“世界距离 / 射线距离”。
- 但最终重建世界点位时,使用的是完整 `lidarLocalToWorld` 矩阵。

### 当前主假设

- 如果 `lidarLocalToWorld` 包含节点缩放:
  - `dirLocal * range` 会先得到一个“传感器局部空间中的距离向量”
  - 再乘 `lidarLocalToWorld` 时,这个距离向量会被缩放再次放大
  - external hit 由于输入 `range` 已经是世界距离,因此会出现“多乘一次缩放”的偏移

### 备选解释

- GPU frustum capture 路径也可能在 `linearDepth -> ray distance` 还原时有缩放相关误差。
- 但静态上看,它使用的是 frustum camera 的投影矩阵,更像“相机自身缩放不为 1”才会触发的问题。

### 当前结论状态

- 以上仍然只是候选假设。
- 还没有动态证据证明“带缩放传感器矩阵”真的进入了失败路径。
- 下一步需要用测试把“重建点位被放大”这个行为直接测出来。

## [2026-03-17 23:04:20] [Session ID: lidar_scaled_surface_offset_20260317_225305] 笔记: 动态验证结果

## 验证命令

- 在原工程直接运行:
  - `Unity ... -projectPath /Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel -runTests ...`
  - 结果: 失败,原因不是代码错误,而是“另一个 Unity 实例正在打开该项目”。
- 因此改用隔离工程:
  - 临时工程: `/tmp/gsplat_editmode_project.yvWxpk`
  - 关键依赖:
    - `com.unity.feature.development`
    - `wu.yize.gsplat -> file:/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat`
    - `testables = [\"wu.yize.gsplat\"]`
- 实际通过的测试命令:
  - `Unity ... -projectPath /tmp/gsplat_editmode_project.yvWxpk -runTests -testPlatform EditMode -testFilter Gsplat.Tests.GsplatUtilsTests.BuildRigidTransformMatrices_IgnoresScaleButPreservesPose`
  - `Unity ... -projectPath /tmp/gsplat_editmode_project.yvWxpk -runTests -testPlatform EditMode -testFilter Gsplat.Tests.GsplatLidarScanTests.TryGetEffectiveLidarRuntimeContext_IgnoresSensorScale_GsplatRenderer`
  - `Unity ... -projectPath /tmp/gsplat_editmode_project.yvWxpk -runTests -testPlatform EditMode -testFilter Gsplat.Tests.GsplatLidarScanTests.TryGetEffectiveLidarRuntimeContext_IgnoresSensorScale_GsplatSequenceRenderer`

## 关键输出

- 三条命令都以 `Exit code 0 (Ok)` 结束。
- 结果文件:
  - `/tmp/gsplat_test_utils.xml`
  - `/tmp/gsplat_test_lidar_renderer.xml`
  - `/tmp/gsplat_test_lidar_sequence.xml`
- 运行中出现过 `UnityConnect` 网络超时日志,但不影响测试结果:
  - 测试框架仍明确输出 `Test run completed. Exiting with code 0 (Ok).`

## 已验证结论

- 上一条主假设成立:
  - LiDAR 传感器矩阵一旦吸收节点缩放,就会破坏 external hit 的世界距离重建语义。
- 证据链:
  - 静态证据:
    - `RenderPointCloud(...)` 最终使用 `lidarLocalToWorld * (dir * range)` 重建世界点位。
    - external hit 的 `range` 语义本身就是世界射线距离。
  - 动态证据:
    - `BuildRigidTransformMatrices_IgnoresScaleButPreservesPose` 证明“刚体矩阵”能保住预期世界点位,而直接用带缩放的 `localToWorldMatrix` 会把点推远。
    - 两条 `TryGetEffectiveLidarRuntimeContext_IgnoresSensorScale_*` 证明两个 renderer 都已经返回无缩放的 LiDAR 传感器矩阵。

## 最终结论

- 这次不是 external mesh capture 自己算错了距离。
- 真正的问题在最终点云重建坐标系:
  - 传感器矩阵不该带 scale。
  - 它应该是一个严格的刚体 frame(position + rotation)。

## [2026-03-17 23:08:10] [Session ID: lidar_scaled_surface_offset_20260317_225305] 笔记: 新回归现象与首轮判断

## 现象

- 用户现场新反馈:
  - `LidarFrustumCamera` 附近,原本平整的地面被 LiDAR 粒子画成了“向下拐弯”的形状。
- 这个现象和“external target 离开 mesh 表面”不同:
  - 它更像射线模型 / 相机几何 / 深度重建的一致性出了问题。

## 当前主假设

- `CameraFrustum` 模式可能不能直接复用“忽略 scale 的 rigid 传感器矩阵”修法。
- 原因是这条链路里至少有两套相机几何来源:
  - LiDAR 自身 range image 使用 `worldToLidar` + layout LUT
  - external GPU capture 使用 `frustumCamera.worldToCameraMatrix` + `projectionMatrix`
- 如果这两套矩阵语义不完全一致,近平处平面最容易先暴露成弯曲。

## 最强备选解释

- 不是 frustum 相机矩阵失配。
- 而是 `modelToLidar = worldToLidar * transform.localToWorldMatrix` 在缩放场景下丢掉了某个此前“碰巧抵消”的关系,导致 gsplat 自身 range image 先变形。

## 下一步需要的最小证据

- 比较:
  - `frustumCamera.transform.localToWorldMatrix`
  - `Matrix4x4.TRS(camera.position, camera.rotation, Vector3.one)`
  - `frustumCamera.worldToCameraMatrix.inverse`
- 重点看带父级缩放时,这三者是否仍然等价。

## [2026-03-17 23:33:40] [Session ID: lidar_scaled_surface_offset_followup_20260317_233246] 笔记: 首次 CLI 验证属于无效结果

## 来源

### 来源1: `/tmp/gsplat_test_lidar_frustum_layout.log`

- 命令参数已进入 Unity:
  - `-runTests`
  - `-testFilter Gsplat.Tests.GsplatLidarScanTests.TryGetEffectiveLidarLayout_UsesRigidCameraFrameForAngles_WhenParentScaled`
  - `-testResults /tmp/gsplat_test_lidar_frustum_layout.xml`
- 但日志缺少测试真正启动/结束的关键标志:
  - 没有 `Test run completed`
  - 没有 `Exiting with code 0 (Ok)`
  - 没有 NUnit 结果摘要

### 来源2: `/tmp/gsplat_test_lidar_frustum_layout.xml`

- 文件不存在。

## 综合发现

### 现象

- 这次 `exit 0` 不能当成测试通过。
- 当前只能确认 Unity 启动过并完成了导入,不能确认 TestRunner 真正执行过。

### 当前判断

- 这更像是 Unity CLI 的提前退出问题。
- 根据仓库里的已知经验,下一步应去掉 `-quit` 再跑一次。

## [2026-03-17 23:35:40] [Session ID: lidar_scaled_surface_offset_followup_20260317_233246] 笔记: frustum 角域 scale 污染已被动态证据验证

## 来源

### 来源1: `/tmp/gsplat_test_lidar_frustum_layout.xml`

- 目标测试:
  - `Gsplat.Tests.GsplatLidarScanTests.TryGetEffectiveLidarLayout_UsesRigidCameraFrameForAngles_WhenParentScaled`
- 结果:
  - `result="Failed"`
- 关键断言:
  - `Expected: -0.798425972f`
  - `But was: -1.08279777f`
- 失败位置:
  - `Tests/Editor/GsplatLidarScanTests.cs:823`

### 来源2: `/tmp/gsplat_test_lidar_frustum_layout.log`

- 已出现有效结束标志:
  - `Test run completed. Exiting with code 2 (Failed). One or more tests failed.`

### 来源3: `Runtime/Lidar/GsplatLidarScan.cs`

- 当前实现仍是:
  - `var localDirection = frustumCamera.transform.InverseTransformPoint(worldPoint);`

## 综合发现

### 现象

- 带父级缩放时,frustum layout 生成出来的角域和 rigid camera frame 预期不一致。

### 当前主假设

- `TryComputeFrustumAngleBounds(...)` 用了带 scale 的 camera local frame。
- 运行时 LiDAR frame 却已经改成 rigid。
- 这就让 layout LUT 和运行时 ray/reconstruct 语义发生撕裂。

### 当前结论状态

- 这次已经不是候选假设。
- 静态证据 + 动态证据共同支持:
  - 回归点就在 frustum angle bounds 计算链路。

### 最直接修复点

- 把 `TryComputeFrustumAngleBounds(...)` 的 sample worldPoint 转换,
  从 `transform.InverseTransformPoint(...)`
  改成 rigid `worldToSensor.MultiplyPoint3x4(...)`。

## [2026-03-17 23:38:46] [Session ID: lidar_scaled_surface_offset_followup_20260317_233246] 笔记: 修复后验证结果

## 来源

### 来源1: `Runtime/Lidar/GsplatLidarScan.cs`

- 已改成:
  - 先 `GsplatUtils.BuildRigidTransformMatrices(frustumCamera.transform, out _, out var rigidWorldToSensor)`
  - 再 `rigidWorldToSensor.MultiplyPoint3x4(worldPoint)`

### 来源2: 定向测试结果

- `/tmp/gsplat_test_lidar_frustum_layout.xml`
  - `TryGetEffectiveLidarLayout_UsesRigidCameraFrameForAngles_WhenParentScaled -> Passed`
- `/tmp/gsplat_test_lidar_renderer.xml`
  - `TryGetEffectiveLidarRuntimeContext_IgnoresSensorScale_GsplatRenderer -> Passed`
- `/tmp/gsplat_test_lidar_sequence.xml`
  - `TryGetEffectiveLidarRuntimeContext_IgnoresSensorScale_GsplatSequenceRenderer -> Passed`
- `/tmp/gsplat_test_utils.xml`
  - `BuildRigidTransformMatrices_IgnoresScaleButPreservesPose -> Passed`

## 综合发现

### 已验证结论

- 这次回归不是 `modelToLidar` 链路造成的。
- 真正出错的是 frustum layout 角域计算仍然吸收父级 scale。
- 当 layout 和 runtime frame 不共用同一套 rigid 语义时,
  近平处最容易先表现成平面弯折。
