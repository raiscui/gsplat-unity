## [2026-03-24 14:58:30 +0800] [Session ID: 20260324_21] 笔记: `delta_updatecount_bad.sog4d` 报错边界

## 来源

### 来源1: `Tests/Editor/GsplatSog4DImporterTests.cs`

- 路径: `Tests/Editor/GsplatSog4DImporterTests.cs`
- 要点:
  - `Import_DeltaUpdateCountOverflow_FailsFast` 明确写着“`updateCount > splatCount` 时必须 fail-fast”
  - 测试会构造 `Assets/__GsplatSog4DImporterTests/delta_updatecount_bad.sog4d`
  - `ImportAssetExpectError(...)` 使用 `LogAssert.Expect(LogType.Error, ...)` 接住这条错误

### 来源2: `Editor/GsplatSog4DImporter.cs`

- 路径: `Editor/GsplatSog4DImporter.cs`
- 要点:
  - importer 在 `updateCount > splatCount` 时会直接 `FailAndDestroyTexture2DArray(...)`
  - `Fail(...)` 最终会在 `GsplatSettings.Instance.ShowImportErrors` 为 `true` 时 `Debug.LogError(...)`

### 来源3: Unity MCP 定向测试

- 测试: `Gsplat.Tests.GsplatSog4DImporterTests.Import_DeltaUpdateCountOverflow_FailsFast`
- 结果:
  - `Passed`
  - output 中包含:
    - `Assets/__GsplatSog4DImporterTests/delta_updatecount_bad.sog4d import error: delta-v1 invalid updateCount: sh/delta_00000.bin: updateCount=5 > splatCount=4 at frame 1`

## 综合发现

### 现象

- 你看到的红日志确实会出现在 Unity Console 里
- 但它来自一个专门验证“坏 delta 必须 fail-fast”的负向测试

### 当前结论

- “导入器错误是否是 bug” 这一点已经有静态 + 动态证据:
  - 静态: 测试名称、注释、构造坏样本逻辑都明确说明这是故意失败
  - 动态: 定向测试 `Passed`,并把该错误作为预期输出记录
- 因此更合理的简单修复方向是:
  - 不改 importer 的 fail-fast 逻辑
  - 只处理测试后的 Console 残留

### 备选解释与证伪条件

- 备选解释:
  - 坏样本在测试结束后又被 Unity 自动重导入,所以即使测试通过,错误仍会再次出现
- 证伪方式:
  - 在测试收尾里清理临时资产并清空 Console
  - 再次运行定向测试后读取 Console
  - 如果 Console 为空,说明残留噪声确实只是测试收尾问题

## [2026-03-24 15:02:40 +0800] [Session ID: 20260324_21] 笔记: 修复后验证结果

## 来源

### 来源1: `Tests/Editor/GsplatSog4DImporterTests.cs`

- 路径: `Tests/Editor/GsplatSog4DImporterTests.cs`
- 要点:
  - 在 `TearDown()` 中新增 `TryClearEditorConsole()`
  - 通过反射清理 Unity Editor Console,避免绑定 internal API 编译风险

### 来源2: Unity MCP 定向验证

- 测试:
  - `Gsplat.Tests.GsplatSog4DImporterTests.Import_DeltaUpdateCountOverflow_FailsFast`
- 结果:
  - `Passed`
  - test output 仍保留同一条 import error
  - 随后 `read_console` 返回 `0 log entries`

## 综合发现

### 已验证结论

- 负向测试的 fail-fast 语义没有变
- 用户看到的“红日志困扰”已经通过测试收尾清理解决
- 这个修复属于测试体验修复,不是导入协议变更

### 影响范围

- 仅影响 `GsplatSog4DImporterTests` 这组测试结束后的 Console 状态
- 不改 `GsplatSog4DImporter.Fail(...)`
- 不改 `.sog4d` 坏数据遇到 `updateCount > splatCount` 时的失败逻辑
