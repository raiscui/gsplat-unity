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

## [2026-03-24 13:09:06 +0800] [Session ID: 20260324_8] 任务名称: Metal 下 `Gsplat.compute` 的 struct ternary 编译错误修复

### 问题现象
- Unity/Metal 编译报错:
  - `conditional operator only supports results with numeric scalar, vector, or matrix types`
- 报错文件:
  - `Runtime/Shaders/Gsplat.compute`
- 对应逻辑位于 `ResolveExternalCandidate(...)` 的返回处

### 原因分析
- `ExternalResolveSample` 是自定义 struct
- 代码使用了:
  - `return bestNeighborhoodSample.Valid != 0 ? bestNeighborhoodSample : centerSample;`
- 这在通用 HLSL 语义上看似自然,但 Metal/HLSLcc 不接受“struct 作为 `?:` 结果”

### 修复动作
- 把 struct ternary 改成显式分支:
  - `if (bestNeighborhoodSample.Valid != 0) return bestNeighborhoodSample;`
  - `return centerSample;`
- 同步更新 `Tests/Editor/GsplatLidarExternalGpuCaptureTests.cs`
  - 不再锁旧的三元表达式
  - 改为锁显式 `if/return` 回退语义

### 验证结果
- `dotnet build ../../Gsplat.Tests.Editor.csproj -v minimal`
  - 结果: `0 warning / 0 error`
- Unity reload 后 Console 检查:
  - `filter_text = Gsplat.compute` -> `0` 条
  - `filter_text = conditional operator` -> `0` 条

## [2026-03-24 14:12:25 +0800] [Session ID: 20260324_8] 任务名称: LiDAR compute 单轴 dispatch 超出 `65535` group limit 修复

### 问题现象
- Unity 运行时报错:
  - `Thread group count is above the maximum allowed limit. Maximum allowed thread group count is 65535.`
- 调用栈落到:
  - `Gsplat.GsplatLidarScan:TryRebuildRangeImage(...)`
  - `UnityEngine.Graphics:ExecuteCommandBuffer(...)`

### 原因分析
- 当前 LiDAR compute kernel 采用线性一维 dispatch:
  - `DispatchCompute(groupsX, 1, 1)`
- 单次 dispatch 的单个维度 group count 不能超过 `65535`
- 旧实现把全部 `cellCount/splatCount` 直接折算成单次 `groupsX`
- 当 item 数超过 `65535 * 256 = 16776960` 时,就会直接撞上平台上限

### 修复动作
- 在 `Runtime/GsplatUtils.cs` 中新增线性 compute dispatch chunk 规划 helper
- 在 `Runtime/Lidar/GsplatLidarScan.cs` 中把:
  - `ClearRangeImage`
  - `ReduceMinRangeSq`
  - `ResolveMinSplatId`
  改成分批 dispatch
- 在 `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs` 中把:
  - `ResolveExternalFrustumHits`
  也改成分批 dispatch
- 在 `Runtime/Shaders/Gsplat.compute` 中新增:
  - `_LidarDispatchBaseIndex`
  并让相关 kernel 使用 `dispatchBaseIndex + SV_DispatchThreadID.x`
- 在 `Tests/Editor/GsplatUtilsTests.cs` 中补边界单测

### 验证结果
- `dotnet build ../../Gsplat.Tests.Editor.csproj -v minimal`
  - 结果: `0 warning / 0 error`
- Unity Console 清空后 refresh/compile
  - 再查 `Thread group count is above the maximum allowed limit`
  - 结果: `0` 条
- Unity EditMode 整程序集:
  - job: `fa6d81bb09744bbe8966695fc555f4a9`
  - 结果: 无新增 `GsplatUtilsTests` / LiDAR 相关失败

## [2026-03-24 14:29:56 +0800] [Session ID: 20260324_9] 任务名称: `LidarBeamCount` 被 runtime 压回 `512` 的限制修复

### 问题现象
- 用户在 Inspector 调大 `LidarBeamCount` 后,运行时实际值会被限制回 `512`
- 这会让更高线束配置无法落到底层 LiDAR range image / render path

### 原因分析
- 真正生效的限制不在字段声明,而在:
  - `GsplatRenderer.ValidateLidarSerializedFields()`
  - `GsplatSequenceRenderer.ValidateLidarSerializedFields()`
- 其中有历史代码:
  - `if (LidarBeamCount > 512) LidarBeamCount = 512;`
- 继续回读后确认:
  - layout / buffer / shader 参数链没有写死 `512`
  - 之前为了规避单次 compute dispatch 上限的风险,现在也已经通过分批 dispatch 修掉
- 因此这个 `512` 已经只剩“保守防呆”含义,不是当前实现约束

### 修复动作
- 删除 `Runtime/GsplatRenderer.cs` 中的 `LidarBeamCount > 512` 钳制
- 删除 `Runtime/GsplatSequenceRenderer.cs` 中的同类钳制
- 同步更新两个组件上 `LidarBeamCount` 的 tooltip / 注释,明确:
  - 当前不再设置硬上限
  - 实际成本会随 `beamCount * azimuthBins` 上升
- 在 `Tests/Editor/GsplatLidarScanTests.cs` 中新增两条回归测试:
  - `ValidateLidarSerializedFields_DoesNotClampBeamCountMax_GsplatRenderer`
  - `ValidateLidarSerializedFields_DoesNotClampBeamCountMax_GsplatSequenceRenderer`

### 验证结果
- 源码检索:
  - 已确认 runtime 里的 `LidarBeamCount > 512` / `LidarBeamCount = 512` clamp 被移除
- `dotnet build ../../Gsplat.Tests.Editor.csproj -v minimal`
  - 结果: `0 warning / 0 error`
- Unity EditMode 整程序集:
  - job: `a135cf447dc74e7b9fa6d7449c5b8126`
  - 结果: `135/135`
  - 失败仍是既有 importer / visibility 老问题,没有新增 LiDAR 相关失败
- 额外说明:
  - 单独 `test_names` 过滤的 job `81632a589684421490a5ad08169771ec` 返回 `summary.total = 0`
  - 该结果不算有效通过证据,因此本轮正式采用整程序集结果作为动态验证
