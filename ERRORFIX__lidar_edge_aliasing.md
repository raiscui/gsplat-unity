# ERRORFIX: LiDAR 高密度扫描边缘锯齿成因分析

## [2026-03-16 22:51:03] [Session ID: 99992] 问题: CameraFrustum external mesh 的 depth capture 精度不可控

### 问题现象
- 用户把 `LidarAzimuthBins` / `LidarBeamCount` 调高后,external mesh 轮廓仍表现为明显的大块台阶和乐高式边缘
- 当前主路径是 `CameraFrustum + external mesh GPU depth capture`
- 用户需要一个"最佳方案",并且希望精度可控

### 原因
- external mesh frustum 路径会先做离屏 depth/color capture
- resolve 时每个 LiDAR cell 只读取一个 capture pixel 的 depth
- 旧实现里 capture RT 尺寸来源基本是默认自动推导,用户无法显式把 external capture 分辨率提高到更细

### 修复
- 新增 `GsplatLidarExternalCaptureResolutionMode`
  - `Auto`
  - `Scale`
  - `Explicit`
- 新增:
  - `LidarExternalCaptureResolutionScale`
  - `LidarExternalCaptureResolution`
- 在 external GPU capture 侧正式消费这些参数
- 对最终 capture 宽高增加 `SystemInfo.maxTextureSize` clamp
- 在 Inspector 和 README 中补齐使用说明
- 用自动化测试锁住 `Auto / Scale / Explicit / clamp` 行为

### 验证
- `dotnet build ../../Gsplat.csproj -nologo`
  - 结果: 成功,0 error,0 warning
- `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`
  - 结果: 成功,0 error
  - 备注: 存在 3 个旧 warning,不是本轮引入
- Unity CLI 首轮直接对主工程跑 EditMode tests
  - 结果: 失败
  - 原因: 另一个 Unity 实例占用了该项目
- 改用临时克隆工程后再跑:
  - 命令: `Unity -batchmode -projectPath /tmp/gsplat-lidar-test-HmRatm -runTests -testPlatform editmode -assemblyNames Gsplat.Tests.Editor -testFilter Gsplat.Tests.GsplatLidarExternalGpuCaptureTests -testResults /tmp/gsplat-lidar-test-HmRatm/Logs/codex_lidar_external_capture_tests_retry.xml`
  - 结果: 11/11 Passed

### 经验
- 当主工程被 Unity Editor 占用时,不要急着把 batchmode 失败误判成代码问题
- 如果第一次 `-runTests` 没产出 XML,优先检查项目文档建议,尝试去掉 `-quit` 再跑一遍
