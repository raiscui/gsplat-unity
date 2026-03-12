## Context

当前仓库已经发现并修复了一条与 PLY SH 布局有关的真实问题:

- `Tools~/Splat4D/ply_sequence_to_splat4d.py` 之前把 `f_rest_*` 直接解释成 `RGBRGB...`
- 但仓库现有 PLY importer 契约实际是 `RRR... GGG... BBB...`
- 这个错位会把原本接近中性的材质染成明显彩色

在静态搜索中,`Tools~/Sog4D/ply_sequence_to_sog4d.py` 也出现了相同形态的 `flat.reshape(flat.shape[0], rest_coeff_count, 3)` 读取方式。它是一个高优先级候选风险,但当前还没有完成下面两类证据:

- 动态证据 1: 真实 PLY 在 `Sog4D` 导出路径里,到底更贴近哪种 `f_rest_*` 解释
- 动态证据 2: Unity 导入 `.sog4d` 后,是否真的出现和 `.splat4d` 同类的偏彩观感

因此,这次 change 的定位不是“直接修 `Sog4D`”,而是先把后续审计与修复需要满足的合同写清楚。

## Goals / Non-Goals

**Goals:**
- 把 `Sog4D` 的 PLY SH 布局风险变成正式的后续变更,而不是零散备注。
- 为后续实现定义一条最小可证伪路径,避免把 `Splat4D` 的结论直接照搬过去。
- 明确未来修复必须同时拿到静态和动态证据,并补真实资产验收。

**Non-Goals:**
- 本轮不修改 `Tools~/Sog4D/ply_sequence_to_sog4d.py`。
- 本轮不改 Unity 的 `Sog4D` importer / runtime。
- 本轮不把这个 change 扩大成通用的 `.ply -> .sog4d` 全面重构。

## Decisions

### 1. 把问题建模成“新 capability + 审计合同”,而不是直接开修复任务

**决定**
- 新增 `sog4d-ply-sh-layout` capability。
- 它只定义未来必须如何验证和解释 `f_rest_*`,不预设这次一定会改代码。

**理由**
- 当前只有静态高风险线索,还缺 `Sog4D` 自己的动态证据。
- 先把合同写清楚,可以避免未来实现时跳过验证步骤。

**备选方案**
- 直接修改 `sog4d-sequence-encoding`
  - 放弃。因为这个风险目前发生在 PLY 输入解释层,不在 `.sog4d` 容器规范本身。

### 2. 后续实现必须复用 `.splat4d` 那套“双解释 + 反解误差”最小实验

**决定**
- 后续审计先跑两种解释方式:
  - `channel-major`: `RRR... GGG... BBB...`
  - `interleaved`: `RGBRGB...`
- 再反解导出的 `.sog4d`,比较更贴近哪一种源解释。

**理由**
- 这是已经在 `.splat4d` 路径上证明有效的最小可证伪实验。
- 它可以把“布局错误”和“量化有损”分开。

**备选方案**
- 只做静态代码阅读
  - 放弃。因为这无法回答 `Sog4D` 当前真实用户资产到底有没有同类观感问题。

### 3. 后续验收必须包含 Unity 实际导入与观感检查

**决定**
- 后续实现完成后,必须用真实 `.sog4d` 资产在 Unity 中做导入与显示验证。
- 不能只停留在离线脚本统计正确。

**理由**
- 用户真正关心的是 Unity 画面是否变脏、是否偏彩。
- 只有离线误差没有 Unity 显示闭环,无法证明用户问题被解决。

**备选方案**
- 只补 Python test
  - 放弃。因为这无法覆盖 Unity importer/runtime 的最终表现。

## Risks / Trade-offs

- [风险] `Sog4D` 的 SH 路径和 `.splat4d v2` 不完全相同,不能机械复用 `.splat4d` 的修法
  - 缓解: 先做最小实验,确认动态证据再动代码

- [风险] 即使布局没错,用户看到的“脏感”也可能来自 `Sog4D` 的量化或 WebP 数据图路径
  - 缓解: 把“布局错位”和“量化有损”明确分成两个候选方向

- [风险] 如果只在笔记里记录,后续很容易遗忘
  - 缓解: 用 OpenSpec change 固化 proposal/design/spec/tasks

## Migration Plan

- 本轮无代码迁移,无资产迁移。
- 后续若决定实施:
  1. 先完成双解释 + 反解误差实验
  2. 若证据支持,再修改 `Tools~/Sog4D/ply_sequence_to_sog4d.py`
  3. 重导出真实 `.sog4d` 资产
  4. 做 Unity 导入与显示回归
- 回滚策略:
  - 若实验推翻“同类布局错误”假设,则关闭这条修复方向,转去排查量化或渲染链差异

## Open Questions

- `Sog4D` 当前真实用户资产中,是否已经有和 `.splat4d` 同类的偏彩现象?
- `Sog4D` 的 WebP / palette / labels 路径会不会放大布局或量化误差?
- 是否需要为 `Sog4D` 增加一个和 `.splat4d` 对等的 batch verify 入口?
