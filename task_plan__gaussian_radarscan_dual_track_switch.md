# 任务计划: gaussian-radarscan-dual-track-switch OpenSpec 新建

## 目标

- 为“`show-hide-switch-雷达` 反向按钮”建立一条新的 OpenSpec change,让后续规格起草有清晰入口。
- 明确它和既有 `radarscan-gaussian-dual-track-switch` 的边界,避免把反向行为误塞回旧 change。

## 阶段

- [ ] 阶段1: 复核现有 OpenSpec 与命名边界
- [ ] 阶段2: 创建新的 change 脚手架
- [ ] 阶段3: 查询 artifact 状态与首个可写 artifact
- [ ] 阶段4: 整理输出并停在“等待用户决定是否继续起草”

## 关键问题

1. 这次需求是不是已有 `radarscan-gaussian-dual-track-switch` 的重复,还是应该作为对称的新 change?
2. 反向按钮的 change 名称怎样命名最清楚,又能和现有 change 形成对称关系?
3. 默认 workflow 下,第一个 ready artifact 是什么,模板要求是什么?

## 做出的决定

- 方案A(最佳方案): 新建 `gaussian-radarscan-dual-track-switch`,把“高斯退场 + 雷达进场”的反向双轨契约独立建模。理由: 语义边界清楚,以后归档和追溯都更干净。
- 方案B(先能用方案): 继续扩写既有 `radarscan-gaussian-dual-track-switch`。理由: 相关背景相近,但缺点是一个 change 同时承担正反两个方向,后续规格会更容易缠在一起。
- 当前采用方案A,因为用户这次明确是用 `$openspec-new-change` 发起“新建 change”,而且反向语义与现有 change 不同。

## 遇到错误

- 暂无。

## [2026-03-18 15:41:23 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 阶段进展

- [x] 阶段1: 复核现有 OpenSpec 与命名边界
  - 已确认既有 `radarscan-gaussian-dual-track-switch` 只覆盖 `RadarScan -> Gaussian`。
  - 已决定本轮采用独立 change `gaussian-radarscan-dual-track-switch`。
- [ ] 阶段2: 创建新的 change 脚手架
- [ ] 阶段3: 查询 artifact 状态与首个可写 artifact
- [ ] 阶段4: 整理输出并停在“等待用户决定是否继续起草”

## 状态

**目前在阶段2**
- 正在检查 `gaussian-radarscan-dual-track-switch` 是否已存在。
- 若不存在,下一步立即创建 change 并读取其 artifact 状态。

## [2026-03-18 15:43:18 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 阶段进展

- [x] 阶段2: 创建新的 change 脚手架
  - 已执行 `openspec new change "gaussian-radarscan-dual-track-switch"`。
  - 已在 `openspec/changes/gaussian-radarscan-dual-track-switch/` 生成默认 `spec-driven` 工作流骨架。
- [ ] 阶段3: 查询 artifact 状态与首个可写 artifact
- [ ] 阶段4: 整理输出并停在“等待用户决定是否继续起草”

## 状态

**目前在阶段3**
- 正在读取 change 的 artifact 状态。
- 下一步确定第一个 ready artifact,并提取它的 instructions 模板。

## [2026-03-18 15:43:56 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 阶段进展

- [x] 阶段3: 查询 artifact 状态与首个可写 artifact
  - `openspec status --change "gaussian-radarscan-dual-track-switch"` 返回 `0/4 artifacts complete`。
  - 第一个 ready artifact 为 `proposal`。
  - 已提取 `openspec instructions proposal --change "gaussian-radarscan-dual-track-switch"` 的完整模板与要求。
- [ ] 阶段4: 整理输出并停在“等待用户决定是否继续起草”

## 状态

**目前在阶段4**
- 已具备对用户的完整回报材料。
- 下一步只做结果整理,不继续创建任何 artifact。

## [2026-03-18 15:43:56 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 阶段进展

- [x] 阶段4: 整理输出并停在“等待用户决定是否继续起草”
  - 已整理 change 名称、路径、schema、artifact 顺序与首个模板。
  - 已按 OpenSpec skill 要求停在“只展示 instructions,不代写 artifact”的边界。

## 状态

**目前已完成阶段4**
- 本轮 OpenSpec 新建流程已完成。
- 如果用户继续,下一步应进入 `proposal.md` 起草,而不是重新创建 change。

## [2026-03-18 15:54:36 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 继续任务: 起草 proposal artifact

## 目标

- 根据用户刚补充的完整需求,为 `gaussian-radarscan-dual-track-switch` 创建 `proposal.md`。
- 让 proposal 明确反向按钮、`0.35` 触发阈值、双向配对按钮语义,并解锁后续 `design` / `specs`。

## 阶段

- [ ] 阶段1: 读取当前 change 状态与 proposal 指令
- [ ] 阶段2: 起草并写入 `proposal.md`
- [ ] 阶段3: 验证 artifact 已创建并检查解锁状态

## 关键问题

1. proposal 应该把这次需求建模为“新增 capability”,还是“修改已有 capability”?
2. `0.35` 需要在 proposal 层先被写成明确行为契约,还是留到 design 再细化?
3. “和 `show-hide-switch-高斯` 按钮对应” 应如何写成 OpenSpec 可追踪的能力边界?

## 做出的决定

- 将本轮建模为新增 capability `gsplat-gaussian-radarscan-switch`,因为主 specs 中不存在可直接复用的既有 capability 名称。
- 在 proposal 层先把 `0.35` 写成明确启动阈值,避免后续 `specs` 和 `design` 再次偏离用户口径。
- 把“按钮对应关系”写成用户可见行为要求,而不是仅写成 Inspector 文案问题。

## 状态

**目前在阶段1**
- 已拿到 `proposal` 的 JSON instructions。
- 下一步开始写入 `openspec/changes/gaussian-radarscan-dual-track-switch/proposal.md`。

## [2026-03-18 15:56:50 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 阶段进展

- [x] 阶段1: 读取当前 change 状态与 proposal 指令
  - 已确认 `proposal` 是当前唯一 ready artifact。
  - 已读取 JSON instructions,输出路径为 `openspec/changes/gaussian-radarscan-dual-track-switch/proposal.md`。
- [x] 阶段2: 起草并写入 `proposal.md`
  - 已把反向按钮、`0.35` 启动阈值、双向切换语义写入 proposal。
- [x] 阶段3: 验证 artifact 已创建并检查解锁状态
  - `openspec status --change "gaussian-radarscan-dual-track-switch"` 已显示 `Progress: 1/4 artifacts complete`。
  - `design` 与 `specs` 已从 blocked 解锁为 ready。

## 状态

**目前已完成 proposal artifact**
- 当前 change 进度为 `1/4`。
- 如果用户继续,下一步应只创建一个 artifact: `design` 或 `specs` 中的第一个 ready 项。

## [2026-03-18 15:56:50 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 继续任务: 基于既有实现创建 design artifact

## 目标

- 读取现有 `show-hide-switch-高斯` 的真实实现过程,把已经验证过的 overlap 机制沉淀到新 change 的 `design.md`。
- 把用户新增口径“中段双效果并存”和“`0.35` 可调”纳入 design 决策,避免后续实现再次走弯路。

## 阶段

- [ ] 阶段1: 读取 design 指令与现有实现证据
- [ ] 阶段2: 形成新 change 的设计决策
- [ ] 阶段3: 写入并验证 `design.md`

## 关键问题

1. 现有 `show-hide-switch-高斯` 的 overlap 是靠哪几条 runtime 轨道和门禁一起成立的?
2. 反向切换是否应该镜像旧方案,还是需要抽象成“可调启动阈值”的更通用形态?
3. `0.35` 可调应落在 design 的哪一层: 固定字段、可序列化参数,还是仅内部常量?

## 做出的决定

- 先从现有代码实现找静态证据,再写 design,不直接照搬旧 OpenSpec 文本。
- 新 design 会继承“两个效果必须 overlap 并存”的事实,同时把原本固定 `0.5` 的启动点升级为可调阈值。
- 这一轮只创建一个 artifact: `design.md`。

## 状态

**目前在阶段1**
- 正在读取既有 `show-hide-switch-高斯` 的 design/tasks 与 runtime 实现位置。
- 下一步提取代码层证据,再下笔写新 design。

## [2026-03-18 16:08:23 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 阶段进展

- [x] 阶段1: 读取 design 指令与现有实现证据
  - 已确认现有正向切换的 overlap 依赖“专用 overlay 轨 + 提交门禁放行 + 延后关 LiDAR”。
  - 已确认代码与测试中的默认触发点实际为 `0.35`。
- [x] 阶段2: 形成新 change 的设计决策
  - 已决定保留旧路径结构,只把 `0.35` 提升为共享可调阈值。
  - 已决定反向路径采用“Gaussian hide 共享轨 + LiDAR show 专用轨 + 最终再落 RadarScan 稳态”的三阶段编排。
- [x] 阶段3: 写入并验证 `design.md`
  - 已创建 `openspec/changes/gaussian-radarscan-dual-track-switch/design.md`。
  - `openspec status --change "gaussian-radarscan-dual-track-switch"` 已显示 `Progress: 2/4 artifacts complete`。
  - 当前只剩 `specs` 未完成,`tasks` 仍等待 `specs`。

## 状态

**目前已完成 design artifact**
- 当前 change 进度为 `2/4`。
- 下一步如果继续,应创建 `specs` artifact,把 requirement 级别的双向 overlap 语义写实。

## [2026-03-18 16:08:23 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 继续任务: Fast-forward 完成剩余 artifacts

## 目标

- 按 `openspec-ff-change` 流程一次性补齐当前 change 剩余的 `specs` 与 `tasks`。
- 保持 proposal / design / specs / tasks 四件套口径一致,让 change 直接进入可实现状态。

## 阶段

- [ ] 阶段1: 读取 `specs` instructions 并创建 spec artifact
- [ ] 阶段2: 验证 `specs` 完成并解锁 `tasks`
- [ ] 阶段3: 读取 `tasks` instructions 并创建 tasks artifact
- [ ] 阶段4: 最终状态校验与收尾记录

## 关键问题

1. `specs` 应如何把“默认 0.35 但可调”写成 requirement,同时保证正反按钮共享同一阈值?
2. `tasks` 应如何拆解,才能既保护旧的 `show-hide-switch-高斯`,又能落下新按钮?
3. 是否需要在 `specs` 中单独要求 editor 文案与按钮成对出现?

## 做出的决定

- `specs` 先锁用户可见契约,包括 overlap、可调阈值、双向按钮对称语义。
- `tasks` 按“共享参数 -> runtime 状态 -> editor -> tests/verification”顺序拆分。
- 当前这轮直接做完到 `tasks` 为止,不再停在中间等确认。

## 状态

**目前在阶段1**
- 正在读取 `specs` instructions 与现有相邻 spec 风格。
- 下一步直接创建 spec 文件。

## [2026-03-18 16:31:46 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 阶段进展

- [x] 阶段1: 读取 `specs` instructions 并创建 spec artifact
  - 已创建 `openspec/changes/gaussian-radarscan-dual-track-switch/specs/gsplat-gaussian-radarscan-switch/spec.md`。
  - 已把双向共享阈值、反向 overlap、专用 LiDAR show overlay、最终稳态收敛等 requirement 写入 spec。
- [x] 阶段2: 验证 `specs` 完成并解锁 `tasks`
  - `openspec status --change "gaussian-radarscan-dual-track-switch"` 已显示 `Progress: 3/4 artifacts complete`。
  - `tasks` 已由 blocked 变为 ready。
- [ ] 阶段3: 读取 `tasks` instructions 并创建 tasks artifact
- [ ] 阶段4: 最终状态校验与收尾记录

## 状态

**目前在阶段3**
- `specs` 已完成,正在读取 `tasks` instructions。
- 下一步直接创建 `tasks.md`。

## [2026-03-18 16:33:06 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 阶段进展

- [x] 阶段3: 读取 `tasks` instructions 并创建 tasks artifact
  - 已创建 `openspec/changes/gaussian-radarscan-dual-track-switch/tasks.md`。
  - 任务按“共享阈值 -> runtime 状态 -> runtime 门禁 -> editor -> tests/verification”顺序拆解完成。
- [x] 阶段4: 最终状态校验与收尾记录
  - `openspec status --change "gaussian-radarscan-dual-track-switch" --json` 已返回 `isComplete=true`。
  - `proposal` / `design` / `specs` / `tasks` 四个 artifacts 均为 `done`。

## 状态

**目前已完成 OpenSpec 全部 artifacts**
- 当前 change 已进入可实现状态。
- 下一步如果用户继续,应进入 apply / implementation 阶段,而不是再补 OpenSpec artifact。

## [2026-03-18 17:05:00 +0800] [Session ID: 7a566a6f-bc80-4706-a356-298650a81244] 继续任务: 进入 implementation 阶段并先抽无副作用 helper

## 目标

- 按 `gaussian-radarscan-dual-track-switch` 的 tasks 开始真正落代码。
- 先把会误触 `Cancel*/Reset*` 的公开入口内部动作拆成无副作用 helper,再接 `Gaussian -> RadarScan` 的 overlap 编排。
- 保证现有 `show-hide-switch-高斯` 读取共享阈值后行为不回退。

## 阶段

- [ ] 阶段1: 梳理现有正向 dual-track 与会抹中间态的公开 API
- [ ] 阶段2: 在 `GsplatRenderer` / `GsplatSequenceRenderer` 抽取无副作用 helper 与共享阈值
- [ ] 阶段3: 接入 `show-hide-switch-雷达` 的反向 dual-track 编排、overlay 与门禁
- [ ] 阶段4: 更新 editor 按钮与文案
- [ ] 阶段5: 扩测试并完成编译/定向验证

## 关键问题

1. 哪些现有公开 API 在内部编排时不能再直接调用,否则会把 overlap 中间态 cancel 掉?
2. 共享阈值应如何落到两个 renderer 上,既保留默认 `0.35`,又能让旧正向路径无缝切过去?
3. 反向路径的稳态收敛点应放在哪一刻,才能保住 Gaussian hide 的主视觉而不让 RadarScan 终态回退?

## 做出的决定

- 先不碰 editor 和 tests,优先把 runtime 内部 helper 的边界拆干净,避免后面一边接反向链路一边被公开 API 清状态。
- 先在 `GsplatRenderer` 完成结构抽取和反向链路,再镜像到 `GsplatSequenceRenderer`,避免两边同时开工把时序问题翻倍。
- 共享阈值先按 design 采用 `DualTrackSwitchTriggerProgress01`,默认值保持 `0.35`。

## 状态

**目前在阶段1**
- 已重新读取支线 `task_plan` / `notes` / `tasks` / `design`。
- 正在比对 `GsplatRenderer` 与 `GsplatSequenceRenderer` 中所有会触发 `CancelPendingRadarToGaussianShowSwitch()` 的公开入口。
- 下一步先抽无副作用 visibility / radar / render-style helper,再把正向路径改成走共享阈值。

## [2026-03-18 17:42:00 +0800] [Session ID: 7a566a6f-bc80-4706-a356-298650a81244] 阶段进展

- [x] 阶段1: 梳理现有正向 dual-track 与会抹中间态的公开 API
  - 已确认 `SetVisible` / `PlayShow` / `PlayHide` / `SetRenderStyle*` / `SetRadarScanEnabled` 的 public 层都会先触发 cancel。
  - 已确认反向路径若继续复用这些 public 入口,会把 overlap 中间态清掉。
- [x] 阶段2: 在 `GsplatRenderer` / `GsplatSequenceRenderer` 抽取无副作用 helper 与共享阈值
  - 已引入共享字段 `DualTrackSwitchTriggerProgress01`,默认 `0.35`,并补齐 Clamp/NaN/Inf 合法化。
  - 已把正向 dual-track 改为读取共享阈值,并把 visibility / radar / render-style 的 public 包装层拆成无副作用 helper。
- [x] 阶段3: 接入 `show-hide-switch-雷达` 的反向 dual-track 编排、overlay 与门禁
  - 已新增 `PlayGaussianToRadarScanShowHideSwitch(...)`。
  - 已新增 `Gaussian -> Radar` 专用 LiDAR show overlay、pending finalize 门禁与稳态收敛。
- [x] 阶段4: 更新 editor 按钮与文案
  - 两个 inspector 都已加入 `show-hide-switch-雷达` 按钮。
  - 帮助文案已补充双向按钮关系与共享阈值说明。
- [x] 阶段5: 扩测试并完成编译/定向验证
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 已通过,0 warning / 0 error。
  - 相关 dual-track 定向 EditMode 测试已通过:
    - `PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway`
    - `PlayRadarScanToGaussianShowHideSwitch_DelayedFirstTick_DoesNotStartShowBeforeLidarHalfway`
    - `PlayRadarScanToGaussianShowHideSwitch_DisablesLidarOnlyAfterDedicatedHideOverlayCompletes`
    - `PlayGaussianToRadarScanShowHideSwitch_DelaysRadarShowUntilSharedTrigger_AndRestoresStableRadarMode`
    - `DualTrackSwitchTriggerProgress01_AffectsBothDirections`
  - 额外 spot-check:
    - `PlayShow_DuringHiding_RestartsShowFromZero` 通过。
  - 当前 Unity 全量包测试仍存在与本次 change 无直接关系的历史问题/环境问题,例如:
    - `ImportV1_StaticSingleFrame4D_RealFixturePlyThroughExporterAndImporter` 依赖 `python3 numpy`,当前环境缺失
    - 若干旧动画用例在当前编辑器环境下仍有独立失败,未纳入本 change 修复范围

## 状态

**目前已完成 implementation 与定向验证**
- OpenSpec `tasks.md` 已全部勾选完成。
- 下一步可以进入人工验收 / 归档前复核,或根据你的需要继续补充更大范围的回归验证。

## [2026-03-18 18:02:00 +0800] [Session ID: 7a566a6f-bc80-4706-a356-298650a81244] 继续任务: 修复反向 dual-track 后半段雷达粒子闪断

## 目标

- 修复 `show-hide-switch-雷达` 在后半段出现“雷达粒子先出来,随后整批消失一次,然后又回来”的时序问题。
- 补上能够稳定锁住这个现象的定向测试,避免后续回归。

## 阶段

- [ ] 阶段1: 复盘现象并定位反向 overlap 到稳态之间的候选断点
- [ ] 阶段2: 做最小验证,确认是 overlay / gate / finalize 哪一段把雷达可见性打断
- [ ] 阶段3: 修复 runtime 时序并补测试
- [ ] 阶段4: 编译与定向验证

## 关键问题

1. 闪断发生在“反向 show overlay 已开始但尚未落稳态”的哪一小段?
2. 是专用 show overlay 自己结束得太早,还是结束后错误回落到了共享 `Hiding/Hidden` 语义?
3. 修复后怎样保证雷达已经 show 出来后,直到稳定 RadarScan 前都不会再被共享 hide 轨重新裁掉?

## 做出的决定

- 当前先把主假设聚焦在 `BuildLidarShowHideOverlayForThisFrame(...)` 与反向 finalize 之间的衔接,不先扩散到 shader 或 sensor 侧。
- 先用单测和代码路径证据验证“是否回落到共享 hide/hidden”,验证成立再做正式修复。

## 状态

**目前在阶段1**
- 已收到用户的人工验收现象: `show-hide-switch-雷达` 后半段雷达粒子会闪断一次。
- 下一步先把反向 overlap 后半段的 overlay/gate 时序重新验证一遍。

## [2026-03-18 21:23:50 +0800] [Session ID: 505DEC94-AB29-42D5-B880-88BC2E74BFE0] 阶段进展

- [ ] 阶段1: 复盘现象并定位反向 overlap 到稳态之间的候选断点
- [ ] 阶段2: 做最小验证,确认是 overlay / gate / finalize 哪一段把雷达可见性打断
- [ ] 阶段3: 修复 runtime 时序并补测试
- [ ] 阶段4: 编译与定向验证

## 状态

**目前在阶段1**
- 当前会先读取 `BuildLidarShowHideOverlayForThisFrame(...)`、反向 finalize 和反向定向测试。
- 这一步只做“现象 -> 假设 -> 最小验证计划”的收敛,暂不直接改 runtime。

## [2026-03-18 21:49:17 +0800] [Session ID: 505DEC94-AB29-42D5-B880-88BC2E74BFE0] 阶段进展

- [x] 阶段1: 复盘现象并定位反向 overlap 到稳态之间的候选断点
  - 已确认现场对象 `ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest` 使用 `WorldSpeed` 模式,`ShowDuration=4`,`HideDuration=6`,`DualTrackSwitchTriggerProgress01=0.35`。
  - 已确认反向路径在 `m_pendingGaussianToRadarFinalizeRadarMode` 为 true 时,`BuildLidarShowHideOverlayForThisFrame(...)` 仍可能回落到共享 `Hiding/Hidden` 语义。
- [x] 阶段2: 做最小验证,确认是 overlay / gate / finalize 哪一段把雷达可见性打断
  - 已补状态级回归测试 `BuildLidarShowHideOverlay_GaussianToRadarFinalizeTail_DoesNotFallBackToHide`。
  - 已把端到端 sanity 用例 `PlayGaussianToRadarScanShowHideSwitch_DoesNotFlashRadarBetweenOverlapAndStableMode` 保留下来,继续验证整条 reverse dual-track。
- [x] 阶段3: 修复 runtime 时序并补测试
  - 已在 `GsplatRenderer` / `GsplatSequenceRenderer` 的 `BuildLidarShowHideOverlayForThisFrame(...)` 增加 reverse finalize 尾段优先分支。
  - 当 `m_pendingGaussianToRadarFinalizeRadarMode` 仍为 true 且专用 show overlay 已结束时,LiDAR 现在保持稳定可见,不再回落到共享 hide/hidden。
- [x] 阶段4: 编译与定向验证
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过,0 warning / 0 error。
  - Unity EditMode 定向测试通过:
    - `BuildLidarShowHideOverlay_GaussianToRadarFinalizeTail_DoesNotFallBackToHide`
    - `PlayGaussianToRadarScanShowHideSwitch_DoesNotFlashRadarBetweenOverlapAndStableMode`
    - `PlayGaussianToRadarScanShowHideSwitch_DelaysRadarShowUntilSharedTrigger_AndRestoresStableRadarMode`
    - `DualTrackSwitchTriggerProgress01_AffectsBothDirections`
    - `PlayRadarScanToGaussianShowHideSwitch_DisablesLidarOnlyAfterDedicatedHideOverlayCompletes`

## 状态

**目前已完成本轮“雷达粒子闪断”修复与验证**
- 这次补丁聚焦在 reverse finalize 尾段的 LiDAR overlay 连续性,没有改公开 API 语义。
- 下一步可以让用户在真实场景里再次点 `show-hide-switch-雷达` 做人工复验。
