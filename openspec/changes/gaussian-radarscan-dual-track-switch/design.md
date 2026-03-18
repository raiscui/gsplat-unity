## Context

当前仓库已经有一条被反复调过、也被测试锁住的 `show-hide-switch-高斯` 路径。它之所以最后能跑对,不是因为“简单半程切一下”,而是因为 runtime 同时满足了三件事:

- `RadarScan -> Gaussian` 在半程触发后,会把 LiDAR 的 hide 视觉继续保存在专用 overlay 轨里,而不是继续复用共享显隐状态。
- overlap 阶段会显式放开 Gaussian splat 的提交门禁,否则虽然 show 启动了,画面上依然看不到高斯。
- `EnableLidarScan` 会延后到专用 hide overlay 真正结束时才关闭,避免雷达效果像被突然断电。

这些事实不只是旧 OpenSpec 的描述,而是当前代码和测试已经共同锁定的行为:

- `Runtime/GsplatRenderer.cs` 与 `Runtime/GsplatSequenceRenderer.cs` 中存在 `m_pendingRadarToGaussianShowSwitch`、`m_pendingRadarToGaussianDisableLidar`、`m_radarToGaussianLidarHideOverlayActive` 等专用状态。
- `BuildLidarShowHideOverlayForThisFrame(...)` 会优先读取专用 hide overlay 轨。
- `ShouldSubmitSplatsThisFrame()` 会在 overlap 阶段绕过 `EnableLidarScan && HideSplatsWhenLidarEnabled` 的常规门禁。
- `Tests/Editor/GsplatVisibilityAnimationTests.cs` 已把触发点锁为 `0.35f`,并验证 overlap 期间必须同时存在 `Gaussian + LiDAR`。

用户现在要补的是反向按钮 `show-hide-switch-雷达`。新需求虽然和旧路径看起来对称,但结构上并不完全镜像:

- `Radar -> Gaussian` 的第二效果是主渲染层的 Gaussian,所以半程时必须开始切主 render style。
- `Gaussian -> Radar` 的第二效果主要是 LiDAR 叠加层,如果半程就机械切到 `ParticleDots`,反而会把 Gaussian hide 的主视觉提前切掉。

另一个新约束是: 当前代码里写死的 `0.35` 需要保留为默认值,但要变成可调参数,而不是继续埋在私有常量里。

## Goals / Non-Goals

**Goals:**

- 为 `show-hide-switch-雷达` 提供一条与 `show-hide-switch-高斯` 成对的双轨切换路径。
- 保留中段双效果并存的体验:
  - Gaussian hide 继续推进
  - Radar show 已经开始
  - overlap 阶段两者同时可见
- 把当前已被验证有效的 `0.35` 提升为共享的可调触发阈值,默认值不变。
- 让 `GsplatRenderer` 与 `GsplatSequenceRenderer` 使用同一套按钮语义和同一套阈值配置。
- 尽量保住已经跑对的 `show-hide-switch-高斯` 实现,避免为了“看起来更抽象”再次打乱现有正确行为。

**Non-Goals:**

- 不把整个项目重构成通用 timeline / clip 系统。
- 不改写普通 `PlayShow()` / `PlayHide()` / `SetRenderStyleAndRadarScan(...)` 的默认语义。
- 不重新设计 LiDAR show/hide 的 shader 视觉语言,继续复用现有 show/hide 视觉体系。
- 不在本轮做“每个方向一套完全独立的大型配置面板”; 本轮优先引入一条共享阈值。

## Decisions

### 1) 保留当前 `Radar -> Gaussian` 的已验证结构,只做最小必要的抽取与补强

**Decision:**

- 不推倒现有 `show-hide-switch-高斯` 实现。
- 新增 `Gaussian -> Radar` 的专用编排状态,但保留现有 `Radar -> Gaussian` helper 的核心结构与执行顺序。
- 只抽取真正需要共享的部分,例如触发阈值配置和 editor 说明,不抽成一个过度泛化的统一状态机框架。

**Why:**

- 现有正向路径“实现了好几次才弄对”,说明它最危险的不是代码量,而是时序细节。
- 现在最稳的策略是承认这条路径已经被代码和测试证实有效,不要再为了抽象美观把它重新洗牌。

**Alternatives considered:**

- 方案A: 直接重构成通用 `A-hide + B-show` timeline 引擎
  - 优点: 形式上更统一
  - 缺点: 侵入面太大,很容易把已经正确的正向路径一起弄回退
- 方案B: 直接复制一整套反向实现,完全不共享任何参数
  - 优点: 改动边界直观
  - 缺点: 很快会在阈值、文案、测试口径上漂移

### 2) 把当前写死的 `0.35` 提升为共享的可调阈值,默认值继续保持 `0.35`

**Decision:**

- 在 `GsplatRenderer` 与 `GsplatSequenceRenderer` 上引入一个共享的序列化配置字段,用于控制这组双向切换的触发进度。
- 该字段默认值保持 `0.35f`,并在运行前统一做 `Clamp01` 与 NaN/Inf 防御。
- 现有 `Radar -> Gaussian` 路径改为读取该共享字段,新 `Gaussian -> Radar` 路径也读取同一个字段。

建议命名方向:

- `DualTrackSwitchTriggerProgress01`

它表达的是:

- `0` = 第二效果立即开始
- `1` = 等第一效果完全结束才开始
- `0.35` = 当前已被验证过的默认 overlap 起点

**Why:**

- `0.35` 已经被现有代码和测试共同锁定,它不是临时拍脑袋值,适合成为默认值。
- 用户明确要求“`0.35` 可调”,说明这个值应该成为正式参数,而不是只活在私有常量里。
- 用一条共享字段可以保证两颗按钮的手感一致,也避免以后一个调了一个没调。

**Alternatives considered:**

- 继续保留硬编码常量
  - 优点: 改动最少
  - 缺点: 与用户最新要求冲突,也不利于后续调校
- 为正反两个方向各自做一套阈值字段
  - 优点: 灵活
  - 缺点: 当前没有证据表明两者必须分家,会先把配置面复杂化

### 3) `Gaussian -> Radar` 采用“Gaussian hide 共享轨 + LiDAR show 专用轨 + 末尾再落稳态”的三阶段编排

**Decision:**

- 点击 `show-hide-switch-雷达` 后,先让 Gaussian 进入共享显隐状态机的 `Hiding`。
- 当共享 hide 进度达到 `DualTrackSwitchTriggerProgress01` 时:
  - 启动 LiDAR show
  - 建立一条 `Gaussian -> Radar` 专用的 LiDAR show overlay 轨
  - 保持主 render style 仍为 Gaussian,让 Gaussian hide 继续作为主视觉推进
- 当 Gaussian hide 真正结束后:
  - 再把稳定状态落到 RadarScan 目标语义
  - 包括 `RenderStyle=ParticleDots` 的最终状态收敛
  - 以及恢复普通 Radar 模式自己的提交门禁

**Why:**

- 反向路径的第二效果主要是 LiDAR 叠加层,不是主 render style 本身。
- 如果半程就把主 render style 机械切到 `ParticleDots`,中段就不再是“Gaussian hide + Radar show”,而会变成另一种并不等价的画面。
- 把“Radar show 开始”和“最终落稳到 RadarScan”拆开,更符合当前视觉结构。

**Alternatives considered:**

- 半程立即切 `RenderStyle=ParticleDots`
  - 优点: 看起来和正向路径更“对称”
  - 缺点: 会提前抹掉 Gaussian hide 主视觉,破坏用户强调的 overlap
- 等 Gaussian 完全 hide 后才开始 Radar show
  - 优点: 状态简单
  - 缺点: 直接失去中段双效果并存

### 4) 为反向路径增加专用 LiDAR show overlay 轨,不要复用共享 `Hiding` 状态

**Decision:**

- 新增 `Gaussian -> Radar` 专用的 LiDAR show overlay 状态,其职责与现有 `Radar -> Gaussian` 的专用 hide overlay 类似,但语义相反。
- `BuildLidarShowHideOverlayForThisFrame(...)` 的优先级调整为:
  1. `Gaussian -> Radar` 专用 LiDAR show overlay
  2. `Radar -> Gaussian` 专用 LiDAR hide overlay
  3. 共享 `m_visibilityState`

**Why:**

- 在反向 overlap 阶段,共享状态仍然是 Gaussian 的 `Hiding`。
- 如果 LiDAR 继续读取共享状态,它拿到的会是 hide mode,而不是 show mode,雷达效果的入场语义就错了。
- 这与现有正向路径为什么要加专用 hide overlay 是同一个问题,只是方向反过来了。

**Alternatives considered:**

- 让 LiDAR show 直接复用共享 `Showing/Hiding`
  - 优点: 少一组字段
  - 缺点: 共享状态本来服务的是 Gaussian hide,语义冲突
- 完全不走 show overlay,只靠 `m_lidarVisibility01` 淡入
  - 优点: 改动小
  - 缺点: 很可能丢掉现有 show/hide 视觉语言,无法保证和按钮语义一致

### 5) 反向 overlap 期间需要一条专用提交门禁放行,不能只依赖现有 LiDAR fade-in 延迟逻辑

**Decision:**

- 新增 `Gaussian -> Radar` 反向切换的 pending/finalize 状态,用于表示“雷达已经开始 show,但 Gaussian hide 还没结束”。
- 在这个状态期间,`ShouldSubmitSplatsThisFrame()` 必须继续允许 Gaussian splat 提交。
- 当 Gaussian hide 结束并切到稳定 RadarScan 后,再恢复普通 `EnableLidarScan && HideSplatsWhenLidarEnabled` 的默认门禁。

**Why:**

- 当前 `ShouldDelayHideSplatsForLidarFadeIn()` 只负责解决“雷达入场黑场”问题。
- 它不能表达“即使 LiDAR fade-in 已完成,只要 Gaussian hide 还在 overlap,Gaussian 也必须继续提交”。
- 如果只靠现有 fade-in 延迟逻辑,Gaussian 很可能在 LiDAR visibility 接近完成时被过早掐掉。

**Alternatives considered:**

- 完全依赖 `ShouldDelayHideSplatsForLidarFadeIn()`
  - 优点: 少改一处门禁
  - 缺点: 它只能覆盖 fade-in 窗口,覆盖不了整个 overlap
- 完全关闭 `HideSplatsWhenLidarEnabled`
  - 优点: 实现最简单
  - 缺点: 会破坏普通 Radar 模式的既有语义

### 6) 测试重点继续放在“时序 + 门禁 + 可调阈值”,而不是只看最终字段值

**Decision:**

- 扩展 `Tests/Editor/GsplatVisibilityAnimationTests.cs`,至少覆盖以下断言:
  - 反向按钮触发前半段: 只有 Gaussian hide,LiDAR 还未 show
  - 到达自定义触发阈值后: LiDAR show 开始,Gaussian hide 继续,两者 overlap
  - overlap 阶段: Gaussian splat 仍提交,LiDAR overlay 为 show mode
  - Gaussian hide 完成后: 稳态落到 RadarScan,普通门禁恢复
  - 调整触发阈值字段后: 正向与反向按钮都按新阈值工作
- 保持 `GsplatRenderer` 与 `GsplatSequenceRenderer` 的 helper、字段命名和流程一致,并至少用编译/定向测试防止漂移。

**Why:**

- 这类功能最容易错在“看起来差不多,但时间线已经歪了”。
- 只测最终状态会漏掉 overlap 这段最关键、也最脆弱的中间过程。

**Alternatives considered:**

- 只验证最终 `RenderStyle` / `EnableLidarScan`
  - 优点: 断言简单
  - 缺点: 基本抓不住 overlap 退化

## Risks / Trade-offs

- [风险] 新增第二套专用轨后,状态字段数量继续上升
  - 缓解: 保持“方向名 + 职责名”明确命名,并把 cancel/finalize helper 做成对称结构
- [风险] 共享可调阈值若未同步到旧按钮,会出现“两个按钮说明一致,手感不一致”
  - 缓解: 先改旧按钮读取共享字段,再做新按钮
- [风险] 反向路径若把 `RenderStyle=ParticleDots` 提前切得太早,会重新破坏 Gaussian hide 主视觉
  - 缓解: design 明确规定“Radar show 开始”和“最终落到 RadarScan 稳态”是两个阶段
- [风险] 仅靠 LiDAR fade-in 延迟可能不足以覆盖整个 overlap,导致高斯提前消失
  - 缓解: 为反向 overlap 增加专用 pending/finalize 门禁放行
- [风险] 现有正向路径已经稳定,任何重构都可能引入回退
  - 缓解: 限制抽象范围,优先保留旧 helper 的结构,并让旧测试继续跑

## Migration Plan

- 本 change 不涉及资产格式迁移。
- 推荐实现顺序:
  1. 引入共享的 `DualTrackSwitchTriggerProgress01` 字段,默认 `0.35`,补齐 sanitize
  2. 让现有 `show-hide-switch-高斯` 改为读取共享阈值,确保旧路径不回退
  3. 为 `Gaussian -> Radar` 增加专用 pending 状态、LiDAR show overlay 轨和 finalize 逻辑
  4. 调整 `BuildLidarShowHideOverlayForThisFrame(...)` 与 `ShouldSubmitSplatsThisFrame()` 的优先级
  5. 在两个 editor inspector 中加入 `show-hide-switch-雷达` 按钮,并更新说明文案
  6. 补定向测试与编译验证
- 若需要回滚:
  - 可以先回退新的 `Gaussian -> Radar` 专用 helper 与 editor 按钮
  - 共享阈值字段可以暂时保留,默认值仍为 `0.35`,不会破坏旧按钮行为

## Open Questions

- 当前先用一条共享阈值覆盖两个方向。若后续用户明确觉得正反两个方向手感需要分开调,再拆成两条方向独立参数是否更合适?
- 最终落到 RadarScan 稳态时,是直接硬切 `RenderStyle=ParticleDots`,还是需要保留一个额外的不可见 morph 过程? 当前倾向前者,因为 Gaussian 已经 hide 完成,画面上不会看到这次切换。
- 是否要把这组双向按钮的触发阈值与语义写入 README / CHANGELOG,避免以后再次被误读成“普通 show/hide 按钮”?
