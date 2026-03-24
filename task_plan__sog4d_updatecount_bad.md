# 任务计划: `.sog4d` delta updateCount 导入报错排查

## 目标

- 判断 `delta-v1 invalid updateCount` 是“应当失败的坏数据”,还是“应该兼容处理的输入”,并完成最合适的简单修复。

## 阶段

- [ ] 阶段1: 读取 importer、测试和测试数据生成路径,确认现象边界
- [ ] 阶段2: 建立“现象 -> 假设 -> 验证计划”,做最小可证伪验证
- [ ] 阶段3: 实施修复或测试调整,补齐必要验证
- [ ] 阶段4: 运行定向验证并收尾记录

## 关键问题

1. 当前报错来自真实用户输入,还是测试里专门构造的坏样本?
2. `updateCount > splatCount` 时,正确语义应该是 fail-fast, clamp, 还是跳过该帧?
3. 如果用户只想“先简单解决”,最小但正确的处理边界是什么?

## 做出的决定

- 先按 `systematic-debugging` 做根因调查,不直接把校验改松。
- 先确认这条路径对应的是哪份资产和哪条测试,再决定是改数据、改测试,还是改 importer。

## 状态

**目前已完成阶段4**
- 已确认这条报错来自负向测试 `Import_DeltaUpdateCountOverflow_FailsFast`, 不是普通资源异常。
- 已在测试收尾中增加 Console 清理, 预期错误不再残留到用户界面。

## [2026-03-24 14:58:30 +0800] [Session ID: 20260324_21] 阶段进展: 完成现象与最小验证

- [x] 阶段1: 读取 importer、测试和测试数据生成路径,确认现象边界
- [x] 阶段2: 建立“现象 -> 假设 -> 验证计划”,做最小可证伪验证
- [ ] 阶段3: 实施修复或测试调整,补齐必要验证
- [ ] 阶段4: 运行定向验证并收尾记录

- 已确认的现象:
  - 错误路径命中 `Tests/Editor/GsplatSog4DImporterTests.cs` 中的 `Import_DeltaUpdateCountOverflow_FailsFast`
  - 测试专门构造了 `delta_updatecount_bad.sog4d`
  - `LogAssert.Expect(...)` 明确把这条 `LogType.Error` 视为预期输出
- 当前主假设:
  - 真正需要“解决”的不是 importer 校验,而是负向测试执行后把预期错误留在 Console,影响日常使用体验
- 备选解释:
  - Console 残留并不来自测试,而是测试结束后该坏资产被再次自动导入
- 已做最小验证:
  - 用 Unity MCP 定向运行 `Gsplat.Tests.GsplatSog4DImporterTests.Import_DeltaUpdateCountOverflow_FailsFast`
  - 结果: `Passed`
  - 测试输出里包含同一条 import error
  - 因此“这条错误是测试预期输出”已被动态证据支撑

## [2026-03-24 15:02:40 +0800] [Session ID: 20260324_21] 阶段进展: 测试层清理完成并验证通过

- [x] 阶段1: 读取 importer、测试和测试数据生成路径,确认现象边界
- [x] 阶段2: 建立“现象 -> 假设 -> 验证计划”,做最小可证伪验证
- [x] 阶段3: 实施修复或测试调整,补齐必要验证
- [x] 阶段4: 运行定向验证并收尾记录

- 实际修改:
  - 在 `Tests/Editor/GsplatSog4DImporterTests.cs` 的 `TearDown()` 中新增 `TryClearEditorConsole()`
  - 通过反射兼容调用 `UnityEditor.LogEntries` / `UnityEditorInternal.LogEntries` 的 `Clear()`
- 验证结果:
  - 定向测试 `Gsplat.Tests.GsplatSog4DImporterTests.Import_DeltaUpdateCountOverflow_FailsFast` 再次 `Passed`
  - 测试 output 仍包含预期的 import error,说明 fail-fast 行为没有被削弱
  - 随后读取 Unity Console,结果为 `0 log entries`
- 结论:
  - 需要修的不是 importer 校验逻辑
  - 更合适的简单修复是清理负向测试产生的残留 Console 噪声
