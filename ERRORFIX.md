# ERRORFIX: RadarScan show/hide noise 不可见

## 2026-03-02

### 现象

- 用户反馈 RadarScan(LiDAR) 模式下,Show/Hide 期间“完全看不到 noise/变化”。

### 初步判断

- 由于已经多轮“增强 shader 噪声”,但用户仍完全无变化。
- 必须先排除: Unity 根本没跑到我们改的 shader 或参数链路有断点。

### 证据采集(已做)

- 在 `Runtime/Lidar/GsplatLidarScan.cs` 添加 Editor 下节流诊断日志:
  - `[Gsplat][LiDAR][ShowHideDiag]`
  - 打印 settings/shader 的 AssetDatabase path,show/hide 参数,noise 参数,以及材质是否存在 `_LidarShowHideNoise*` 属性。

### 下一步

- 依据诊断日志定位断点:
  - 若 shaderPath 不指向 `Packages/wu.yize.gsplat/Runtime/Shaders/GsplatLidar.shader`,优先修正引用/包来源。
  - 若 noise 参数为 0,回溯 RenderPointCloud 调用参数来源。
  - 若两者都正确但仍不可见,再调整 shader 观感(只改根因点)。
