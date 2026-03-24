## [2026-03-24 12:34:31 +0800] [Session ID: 20260324_8] 任务名称: `ExternalGpuResolve_UsesLinearDepthTextureBeforeRayDistanceConversion` 测试契约漂移修复

### 问题现象
- Unity EditMode 整程序集验证时, `Gsplat.Tests.GsplatLidarExternalGpuCaptureTests.ExternalGpuResolve_UsesLinearDepthTextureBeforeRayDistanceConversion` 失败。
- 失败断言仍要求 `Gsplat.compute` 直接保留:
  - `_LidarExternalStaticLinearDepthTex.Load(...)`
  - `_LidarExternalDynamicLinearDepthTex.Load(...)`
- 但当前 compute 已把 static / dynamic 共用逻辑收敛到 `LoadExternalPointSample(...)`。

### 原因分析
- 这不是 hybrid resolve 实现本身回归。
- 真正原因是测试契约没有跟上 helper 重构:
  - 旧测试锁的是“展开后的源码形态”
  - 新实现保留的是“统一 helper 下的 point load 语义”

### 修复动作
- 更新 `Tests/Editor/GsplatLidarExternalGpuCaptureTests.cs`
- 改为锁定以下仍应成立的语义:
  - `float linearDepth = linearDepthTex.Load(int3(pixel, 0)).x;`
  - `float rayDepth = linearDepth / rayForwardDot;`
  - static / dynamic 仍通过 `ResolveExternalCaptureSource(...)` 传入各自的 linear depth texture
  - 不允许退回 `Sample` / `SampleLevel` / encoded depth decode

### 验证结果
- `dotnet build ../../Gsplat.Tests.Editor.csproj -v minimal`
  - 结果: `0 warning / 0 error`
- Unity EditMode 整程序集:
  - job: `9bfd63c810704827aacb7c94bbfae734`
  - 结果: hybrid resolve 相关 `GsplatLidar*` 测试不再出现在失败列表
  - 剩余失败属于既有 `GsplatVisibilityAnimationTests` 与 `numpy` 缺失的 importer fixture
