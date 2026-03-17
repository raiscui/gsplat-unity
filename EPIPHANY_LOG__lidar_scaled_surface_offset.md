# EPIPHANY_LOG: LiDAR 缩放组件 external target 表面偏移

## [2026-03-17 23:04:20] [Session ID: lidar_scaled_surface_offset_20260317_225305] 主题: LiDAR 传感器 frame 必须是刚体矩阵,不能吸收节点缩放

### 发现来源
- 在排查“缩放后的高斯组件上,external target 点云偏离 mesh 表面”时发现。
- 具体链路:
  - `TryGetEffectiveLidarRuntimeContext(...)`
  - `GsplatLidarScan.RenderPointCloud(...)`
  - `Runtime/Shaders/GsplatLidarPassCore.hlsl`

### 核心问题
- 只要系统是“先算射线距离,再用传感器矩阵重建世界点位”的两阶段架构,
  传感器矩阵就不能夹带 scale。

### 为什么重要
- 这种 bug 很隐蔽:
  - 对 gsplat 自身命中,scale 可能在中间链路里被抵消
  - 对 external target 世界距离命中,却不会抵消
- 最终表现就是“只有某一类目标偏离表面”,非常容易误判成 capture / depth / bias 问题。

### 未来风险
- 以后如果有别的功能继续复用 LiDAR 传感器 frame:
  - 例如 debug gizmo
  - secondary scan overlay
  - screen-space resolve
  只要又有人直接拿 `localToWorldMatrix`,就可能把同类 bug 带回来。

### 当前结论
- 当前已知事实:
  - 刚体矩阵修复后,`GsplatRenderer` 与 `GsplatSequenceRenderer` 的相关定向测试都通过了
  - util 级测试也证明“直接使用带缩放矩阵”会把世界点位推远
- 未确认部分:
  - 其它未来新模块是否也会重复犯同类错误

### 后续讨论入口
- 下次再遇到“缩放后才出现的 LiDAR / 射线重建偏移”,应先检查:
  - 距离语义是世界距离还是局部距离
  - 重建矩阵是不是 rigid transform

## [2026-03-17 23:38:46] [Session ID: lidar_scaled_surface_offset_followup_20260317_233246] 主题: LiDAR 的 layout LUT 也必须和 runtime frame 共用同一套 rigid 语义

### 发现来源
- 在修掉 external target 表面偏移后,用户继续反馈 `LidarFrustumCamera` 附近平面地面向下弯折。
- 通过 `TryGetEffectiveLidarLayout_UsesRigidCameraFrameForAngles_WhenParentScaled` 定向测试定位到 frustum angle bounds。

### 核心问题
- 仅仅把 runtime sensor matrix 改成 rigid 还不够。
- 只要 layout LUT 仍然在带 scale 的 local frame 里生成,系统内部就会同时存在两套 LiDAR 坐标语义。

### 为什么重要
- 这类 bug 不一定表现成“整体偏移”。
- 它也可能表现成:
  - 近平几何弯折
  - frustum 边缘密度异常
  - 同一条 ray 在 capture / reconstruct 两端角度不一致

### 未来风险
- 以后任何基于 camera sample 生成 LiDAR 方向 LUT 的新功能,
  只要直接使用 `transform.InverseTransformPoint/Direction` 而没先剥离缩放,
  都可能把同类回归重新带回来。

### 当前结论
- layout 生成、runtime context、点云重建,三者必须共用同一套 rigid sensor frame。
- 只修其中一处,其余两处不跟上,仍然会出现几何撕裂。
