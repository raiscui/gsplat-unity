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

## 2026-03-03

### 现象

- LiDAR(RadarScan) 扫描头扫过后的区域会变黑.
- 下一次扫描前,老点会提前变暗或消失.

### 根因

- `Runtime/Shaders/GsplatLidar.shader` 旧逻辑:
  - `brightness = LidarIntensity * trail`.
  - alpha 不随 trail 变化.
- 在 alpha blend + ZWrite On 场景下:
  - 当 brightness 很小但仍未 discard 时,点会表现为"黑点/黑片".
  - trail 会在 1 圈内衰减到接近 0,因此老点在下一次扫到前会提前变暗或消失.

### 修复

- 引入"未扫到区域底色强度"(亮度下限)与 Keep 开关:
  - 新增字段:
    - `LidarKeepUnscannedPoints`(是否在下一次扫描前保留未扫到区域)
    - `LidarUnscannedIntensity`(未扫到区域底色强度)
  - Shader 新增 `_LidarUnscannedIntensity`,并把每点强度改为:
    - `lerp(unscannedIntensity, scanIntensity, trail)`
  - Runtime 下发 `_LidarUnscannedIntensity`:
    - Keep 关闭时强制下发 0,保持旧行为.
    - Keep 开启时下发 `LidarUnscannedIntensity`,避免"扫过后变黑".

### 验证(证据)

- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`): total=54, passed=52, failed=0, skipped=2
- XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_unscanned_intensity_2026-03-03_154114_noquit.xml`

## 2026-03-08

### 现象

- `radarscan-particle-antialiasing-modes` 首轮 Unity EditMode 回归里,两个 A2C fallback 测试失败.
- 失败不是断言错,而是 Unity 记录了 error log:
  - `Releasing render texture that is set as Camera.targetTexture!`

### 根因

- `Tests/Editor/GsplatLidarScanTests.cs` 里的两个测试在 `finally` 中直接销毁了 `targetTexture`.
- 但此时 `Camera.targetTexture` 仍然指向该 RT.
- Unity 会把这种销毁顺序记成错误日志,从而导致整组测试失败.

### 修复

- 把测试清理顺序改为:
  - 先 `camera.targetTexture = null`
  - 再 `DestroyImmediate(targetTexture)`
  - 最后 `DestroyImmediate(cameraGo)`
- 同时把 `camera` 提升到 `try/finally` 外层变量,确保 `finally` 能安全解绑.

### 验证(证据)

- 目标测试:
  - `Gsplat.Tests.GsplatLidarScanTests.ResolveEffectiveLidarParticleAntialiasingMode_FallsBackToAnalyticCoverageWithoutMsaa`
  - `Gsplat.Tests.GsplatLidarScanTests.ResolveEffectiveLidarParticleAntialiasingMode_KeepsA2CWhenMsaaIsAvailable`
  - 在 XML 中均为 `Passed`
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`82`, passed=`80`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_particle_aa_2026-03-08_noquit.xml`
