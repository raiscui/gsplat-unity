## 1. 审计准备

- [ ] 1.1 选定真实 `PLY` / `.sog4d` 样例,作为后续 SH 布局审计的统一输入
- [ ] 1.2 盘点 `Tools~/Sog4D/ply_sequence_to_sog4d.py` 中所有 `f_rest_*` 读取与 SH 写入路径

## 2. 最小可证伪实验

- [ ] 2.1 对同一份真实 PLY 分别按 `channel-major` 与 `interleaved` 两种方式解释 `f_rest_*`
- [ ] 2.2 导出 `.sog4d` 并做反解或等价还原,比较结果更贴近哪一种源解释
- [ ] 2.3 判断主矛盾是“布局错误”还是“量化/编码链导致的脏感”

## 3. 若证据成立的后续实现

- [ ] 3.1 仅在实验确认布局错位后,修复 `Tools~/Sog4D/ply_sequence_to_sog4d.py`
- [ ] 3.2 增加 Python / Editor / Unity 验收回归,锁定正确的 SH 布局契约
- [ ] 3.3 用真实 `.sog4d` 资产完成 Unity 画面验证,确认没有同类偏彩问题
