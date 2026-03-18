## Context

当前 `show-hide-switch-高斯` 已经暴露出一个结构性问题:
用户要的是“旧视觉退场仍在继续,新视觉入场已经开始”的 overlap 切换.
但现有实现的核心状态只有一套共享显隐轨:

- `m_visibilityState`
- `m_visibilityProgress01`
- `m_visibilitySourceMaskMode`
- `m_visibilitySourceMaskProgress01`

这套状态既要表达“雷达粒子正在 hide”,又要表达“Gaussian 正在 show”.
一旦半程触发把共享状态切到 `Showing`,雷达那条 hide 轨就会被覆盖.

与此同时,当前 splat 提交门禁也不适合 overlap:

- `EnableLidarScan && HideSplatsWhenLidarEnabled`
- 会直接挡住 Gaussian splat 的 sort/draw

这就是用户为什么会不断看到:

- 雷达粒子做一半就像被断电
- 高斯 show 虽然开始了,但并不稳定
- overlap 阶段无法可靠同时看到两者

本 change 的目标,不是引入一个庞大的通用时间线系统.
而是为 RadarScan -> Gaussian 这条专用切换补一条最小双轨模型,让用户要的时间线可以被真实表达出来.

## Goals / Non-Goals

**Goals:**

- 让 `show-hide-switch-高斯` 满足明确的双轨语义:
  - 雷达 hide 轨完整执行
  - Gaussian show 在 hide 过半时启动
  - overlap 阶段两者同屏
  - LiDAR 只在 hide 轨真正完成后才关闭
- 尽量复用现有 burn reveal / LiDAR fade / RenderStyle 切换体系,避免为这一个按钮引入整套新框架.
- 保持 `GsplatRenderer` 与 `GsplatSequenceRenderer` 的实现与测试口径一致.
- 用回归测试把最容易回退的时序锁死,尤其是:
  - 首帧延迟
  - overlap 期间高斯提交门禁
  - hide 完成前不得提前关 LiDAR

**Non-Goals:**

- 不把整个项目的 show/hide 都改造成通用多轨时间线系统.
- 不改变普通 `PlayShow()` / `PlayHide()` 的语义.
- 不引入新的 shader 视觉语言.
  本 change 复用现有 `visibility hide` 的 noise / burn 效果.
- 不在本期把 overlap 起点做成任意可配比例.
  本期固定为 hide 进度 `0.5`.

## Decisions

### 1) 为 RadarScan -> Gaussian 增加一条独立的 LiDAR hide overlay 轨,而不是继续复用共享显隐轨

**Decision:**

- 保留共享 `m_visibilityState` 给 Gaussian show 使用.
- 另外增加一条 Radar->Gaussian 专用的 LiDAR hide overlay 轨,用于保存:
  - hide 是否仍在进行
  - hide progress
  - source mask snapshot
  - lastAdvanceRealtime
- overlap 阶段 LiDAR overlay 优先消费这条独立轨,而不是继续读取共享 `Showing`.

**Why:**

- 用户要的不是“看起来像并行”,而是“雷达 hide 轨真的继续跑到终点”.
- 只要还把两条轨塞进同一套共享显隐状态,半程启动 Gaussian show 时就会把 hide 轨覆盖掉.

**Alternatives considered:**

- 继续沿用共享 `m_visibilityState`,再叠更多 bypass / if 分支
  - 优点: 改动面看起来更小
  - 缺点: 本质上仍然只有一条轨,无法真实表达 overlap
- 引入完整的通用 timeline/clip 系统
  - 优点: 泛化最强
  - 缺点: 对当前需求明显过重,违背“改良胜过新增”

### 2) 半程事件只负责启动 Gaussian show,不负责终止雷达 hide 轨

**Decision:**

- `AdvancePendingRadarToGaussianShowSwitchIfNeeded()` 在 LiDAR hide 进度过半时:
  - 启动 Gaussian show
  - 切换 `RenderStyle` 到 `Gaussian`
  - 允许 Gaussian 开始提交
- 但此时不立即结束 LiDAR hide 轨.
- `EnableLidarScan` 的关闭动作延后到独立 hide 轨完整结束时执行.

**Why:**

- 这是用户时间线的核心:
  - “show 从中段开始”
  - 不是“hide 到中段就结束”

**Alternatives considered:**

- 半程时直接 `EnableLidarScan=false`
  - 已被用户多次证伪
  - 会导致雷达粒子做一半就消失

### 3) overlap 阶段必须显式放开 Gaussian splat 提交门禁

**Decision:**

- 为 `ShouldSubmitSplatsThisFrame()` 增加一条专用放行条件:
  - 当 Radar->Gaussian dual-track switch 正在 overlap 阶段时
  - 即使 `EnableLidarScan=true && HideSplatsWhenLidarEnabled=true`
  - 仍要允许 Gaussian splat sort/draw 提交

**Why:**

- 否则会出现“show 状态已经开始,但高斯实际没画出来”的假成功.
- 这类问题肉眼很像 easing 不对,本质上其实是门禁没放开.

**Alternatives considered:**

- 半程时先把 `EnableLidarScan` 提前关掉来绕过门禁
  - 会再次破坏“hide 轨完整执行”的目标

### 4) LiDAR overlay 构建阶段使用“专用轨优先,共享轨兜底”的优先级

**Decision:**

- `BuildLidarShowHideOverlayForThisFrame(...)` 的优先级调整为:
  1. 若 Radar->Gaussian 专用 hide overlay 轨处于 active,优先输出这条轨的 hide mode / progress / source mask
  2. 否则再使用现有共享 `m_visibilityState`
  3. 非动画态保持当前默认 gate 语义

**Why:**

- overlap 阶段共享轨已经会进入 `Showing`.
- 若不提升专用轨优先级,LiDAR overlay 只能看到 Gaussian show 的状态.

**Alternatives considered:**

- overlap 期间简单 `bypass overlay`
  - 只能做到“不被 Gaussian show 裁掉”
  - 做不到“继续播放完整的 hide overlay”

### 5) 中断与重入策略保持“显式取消旧 pending,再建立当前 switch”

**Decision:**

- 保留现有 cancel/arm 风格.
- 新按钮再次被触发,或用户调用其他风格/可见性 API 时:
  - 显式取消当前 Radar->Gaussian dual-track pending 状态
  - 清理专用 hide overlay 轨
  - 由新的操作重新建立目标状态

**Why:**

- overlap 切换比普通按钮更依赖明确的优先级.
- 如果不先定义中断语义,用户在 Inspector 连点时很容易留下半残状态.

**Alternatives considered:**

- 让旧轨自然跑完,新请求排队
  - 逻辑更复杂
  - 也不符合编辑器按钮“当前点击应立即接管”的体感

### 6) 测试以“显式推进状态机 + 定向门禁断言”为主

**Decision:**

- 扩展 `GsplatVisibilityAnimationTests`:
  - 显式推进 `Update()` / `AdvanceVisibilityStateIfNeeded()` / `AdvanceLidarAnimationStateIfNeeded()`
  - 锁定半程前、overlap 中、最终完成后的 3 个关键状态点
- 把验证重点放在:
  - `EnableLidarScan`
  - `RenderStyle`
  - `VisibilityState`
  - LiDAR overlay 输出
  - splat 提交门禁

**Why:**

- 这条功能的风险不在 shader 数学,而在时序和门禁.
- 测试应该直接卡住“什么时候该继续画,什么时候才允许关”.

**Alternatives considered:**

- 只做编译验证或只看单一状态字段
  - 很容易漏掉 overlap 阶段的真实回退

## Risks / Trade-offs

- [风险] 双轨状态在 `GsplatRenderer` 与 `GsplatSequenceRenderer` 之间出现实现漂移
  - 缓解: 两边保持同名 helper / 同名阶段语义,并同步更新测试与注释
- [风险] overlap 阶段会短时间同时提交 LiDAR 与 Gaussian,引入额外开销
  - 缓解: overlap 持续时间有限,且只发生在专用切换按钮路径
- [风险] 中断语义处理不全时,可能出现“连点后状态卡住”
  - 缓解: 所有入口统一先 cancel 当前 pending dual-track 状态
- [风险] 若只放开 splat 提交,但忘记独立 hide overlay 轨,现场仍会看起来像雷达突然断掉
  - 缓解: 把 overlay 优先级调整列为独立实现任务与测试点

## Migration Plan

- 本 change 不涉及资产格式或序列化迁移.
- 默认行为保持不变,仅影响 `show-hide-switch-高斯` 这条专用切换路径.
- 落地顺序:
  1. 先改 runtime 双轨状态与门禁
  2. 再同步 editor 按钮说明
  3. 最后补定向测试并做编译验证
- 若需要回滚:
  - 可以仅回退 Radar->Gaussian 专用 helper 与门禁放行逻辑
  - 普通 `PlayShow()` / `PlayHide()` / RadarScan 基础能力不受影响

## Open Questions

- 是否要在后续把“过半启动”抽成可配比率,还是继续固定 `0.5`?
- 是否要把这条 dual-track 编排抽成更通用的 “A 退场 + B 入场 overlap” 机制,供其他 render style 切换复用?
- 是否要在 README/CHANGELOG 中把这个按钮的时间线语义对外写明,避免后续再次被误读成“普通渐变切换”?
