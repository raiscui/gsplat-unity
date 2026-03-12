## ADDED Requirements

### Requirement: Sog4D PLY conversion MUST define a deterministic `f_rest_*` channel layout contract
`Tools~/Sog4D/ply_sequence_to_sog4d.py` 在读取 PLY `f_rest_*` 时 MUST 明确定义通道布局契约,不得依赖隐式 `reshape(..., coeff, 3)` 推断顺序。

当输入 PLY 的 `f_rest_*` 使用仓库现有 importer 约定时,系统 MUST 把它解释为:
- 先全部 R coefficients
- 再全部 G coefficients
- 最后全部 B coefficients

如果后续实现决定支持别的来源约定,系统 MUST 通过显式配置或明文文档区分来源,不能把不同约定混成同一条默认路径。

#### Scenario: channel-major PLY 被按既定契约解释
- **WHEN** 输入 PLY 的 `f_rest_*` 实际顺序为 `RRR... GGG... BBB...`
- **THEN** `Sog4D` exporter MUST 按该顺序重建 `[coeff, rgb]` 的 SH 数据,不得把它错解释成 `RGBRGB...`

### Requirement: Sog4D SH layout investigation MUST use a minimal falsifiable experiment before code changes
后续任何针对 `Sog4D` PLY SH 布局的修复,在改代码前 MUST 先完成最小可证伪实验。

该实验 MUST 至少包含:
- 同一份真实 PLY 同时按 `channel-major` 与 `interleaved` 两种方式解释
- 对导出结果做反解或等价还原
- 比较导出结果更贴近哪一种源解释

如果当前只有静态阅读结论,系统 MUST 把它明确标记为候选假设,不得直接当成已验证根因。

#### Scenario: 只有静态线索时不允许直接修复
- **WHEN** 维护者只发现 `reshape(..., coeff, 3)` 这类静态高风险线索,但还没有导出后反解或显示证据
- **THEN** 该问题 MUST 被记录为候选假设,而不是直接认定为已验证根因

### Requirement: Sog4D SH layout fixes MUST be validated with real Unity import evidence
如果后续实现阶段对 `Sog4D` 的 PLY SH 布局做了修改,系统 MUST 用真实资产完成 Unity 侧验收,不能只依赖离线脚本测试。

验收 MUST 至少包括:
- 使用真实 PLY 生成 `.sog4d`
- Unity 成功导入该 `.sog4d`
- 检查显示结果没有出现同类的明显偏彩或异常局部颜色

#### Scenario: 修复后必须经过 Unity 画面验收
- **WHEN** `Sog4D` exporter 的 SH 布局逻辑被修改
- **THEN** 变更在完成前 MUST 提供一条真实 `.sog4d -> Unity` 的导入与显示验证证据
