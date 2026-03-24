## [2026-03-24 15:02:40 +0800] [Session ID: 20260324_21] 问题: `delta_updatecount_bad.sog4d` 预期错误残留在 Console

### 现象

- 运行 `GsplatSog4DImporterTests.Import_DeltaUpdateCountOverflow_FailsFast` 后
- Unity Console 会留下:
  - `Assets/__GsplatSog4DImporterTests/delta_updatecount_bad.sog4d import error: delta-v1 invalid updateCount...`
- 这条日志虽然来自预期失败测试,但对日常使用者看起来像真实导入故障

### 原因

- 测试本身故意构造坏样本,并用 `LogAssert.Expect(...)` 断言这条错误必须出现
- 但测试收尾只删除临时资产,没有清理 Console
- 因此“预期错误”会在测试通过后继续留在编辑器界面中

### 修复

- 在 `Tests/Editor/GsplatSog4DImporterTests.cs` 的 `TearDown()` 中增加 `TryClearEditorConsole()`
- 通过反射兼容调用 `UnityEditor.LogEntries` / `UnityEditorInternal.LogEntries` 的 `Clear()`
- 保持 importer 的 fail-fast 与错误日志输出逻辑不变

### 验证

- Unity MCP 定向测试:
  - `Gsplat.Tests.GsplatSog4DImporterTests.Import_DeltaUpdateCountOverflow_FailsFast`
  - 结果: `Passed`
- 测试 output:
  - 仍包含预期 import error,证明负向断言仍然有效
- 测试后读取 Console:
  - `0 log entries`
  - 说明残留噪声已被清理
