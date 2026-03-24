## [2026-03-24 15:02:40 +0800] [Session ID: 20260324_21] 任务名称: 清理 `.sog4d` 负向 importer 测试残留错误日志

### 任务内容
- 追踪 `delta_updatecount_bad.sog4d` 报错来源,确认它是否属于真实导入故障
- 对 `GsplatSog4DImporterTests` 做测试层清理,避免预期错误残留在 Unity Console
- 保留 importer 对坏 delta 数据的 fail-fast 校验

### 完成过程
- 先读取 `GsplatSog4DImporter.cs` 与 `GsplatSog4DImporterTests.cs`,确认 `updateCount > splatCount` 是被显式测试的负向路径
- 用 Unity MCP 定向运行 `Gsplat.Tests.GsplatSog4DImporterTests.Import_DeltaUpdateCountOverflow_FailsFast`
- 验证测试 `Passed`,并确认测试 output 本身包含该 import error
- 在 `Tests/Editor/GsplatSog4DImporterTests.cs` 的 `TearDown()` 中增加 `TryClearEditorConsole()`
- 再次运行同一条测试并读取 Console,确认测试仍通过且 Console 已无残留错误

### 总结感悟
- 这类问题很容易误判成“导入器坏了”,但真正该修的往往是测试副作用
- 对负向测试来说,验证失败语义和避免污染用户界面,这两件事都要一起照顾
