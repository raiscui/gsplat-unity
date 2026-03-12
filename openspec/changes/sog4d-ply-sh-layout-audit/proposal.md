## Why

这次 `.splat4d` 单帧 SH3 颜色偏彩问题已经被证实是 `f_rest_*` 字段排列解释错误: 真实 PLY 契约是 `RRR... GGG... BBB...`,而不是 `RGBRGB...`. 当前仓库里 `Tools~/Sog4D/ply_sequence_to_sog4d.py` 也存在同样的 `reshape(..., coeff, 3)` 读取方式,因此它很可能复制了同一类色偏风险,但本轮还没有做动态验证。

用户已经明确要求“先记录,现在不改”。因此需要把这个风险正式沉淀成一个 OpenSpec change,明确后续审计范围、验收证据和不在本轮落地的边界,避免它继续只停留在临时笔记里。

## What Changes

- 新增一个专门描述 `Sog4D` PLY SH 字段布局契约的 capability,覆盖 `f_rest_*` 的读取顺序与导出语义。
- 要求后续实现阶段先做最小可证伪实验:
  - 同一份 PLY 分别按 `channel-major` 与 `interleaved` 两种方式解释
  - 比较导出后 `.sog4d` 解码结果更贴近哪一种源解释
- 要求后续修复只在证据成立时进行,不能把当前 `Splat4D` 的结论直接照搬到 `Sog4D`。
- 要求后续补齐真实资产验收:
  - 使用真实 `PLY` 样例导出 `.sog4d`
  - 在 Unity 中导入并确认颜色没有出现同类偏彩
- 本 change 当前只建立记录与验收合同,不在本轮实施代码修改。

## Capabilities

### New Capabilities
- `sog4d-ply-sh-layout`: 定义 `Tools~/Sog4D/ply_sequence_to_sog4d.py` 在读取 PLY `f_rest_*` 时必须遵守的 SH 通道布局契约,以及后续审计/修复的验证要求。

### Modified Capabilities
- None.

## Impact

- Tools:
  - `Tools~/Sog4D/ply_sequence_to_sog4d.py`
  - `Tools~/Sog4D/tests/`
  - `Tools~/Sog4D/README.md`
- Validation:
  - 需要增加离线反解对照实验
  - 需要增加真实 `.sog4d` 资产导入后的 Unity 颜色验收
- Process:
  - 后续实现必须遵守“先验证,后修复”的排查纪律
