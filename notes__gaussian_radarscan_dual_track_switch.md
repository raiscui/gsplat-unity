## [2026-03-18 15:43:56 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 笔记: Gaussian -> RadarScan 反向切换 OpenSpec 建档事实

## 来源

### 来源1: 既有 change `radarscan-gaussian-dual-track-switch`

- 路径: `openspec/changes/radarscan-gaussian-dual-track-switch/proposal.md`
- 要点:
  - 该 change 明确只覆盖 `show-hide-switch-高斯`,也就是 `RadarScan -> Gaussian` 的双轨切换。
  - 既有语义是“雷达 hide 过半时启动 Gaussian show”,不是本轮所需的反向按钮。

### 来源2: 新建 change 命令输出

- 命令: `openspec new change "gaussian-radarscan-dual-track-switch"`
- 要点:
  - change 已成功创建。
  - 默认 schema 为 `spec-driven`。

### 来源3: OpenSpec 状态与指令

- 命令: `openspec status --change "gaussian-radarscan-dual-track-switch"`
- 命令: `openspec instructions proposal --change "gaussian-radarscan-dual-track-switch"`
- 要点:
  - 当前进度为 `0/4 artifacts complete`。
  - `proposal` 是第一个 ready artifact。
  - `design` 与 `specs` 依赖 `proposal`, `tasks` 依赖 `design` 与 `specs`。
  - `proposal.md` 需要包含 `Why`、`What Changes`、`Capabilities`、`Impact` 四部分。

## 综合发现

### 命名与边界

- 使用 `gaussian-radarscan-dual-track-switch` 能和既有 `radarscan-gaussian-dual-track-switch` 形成清晰对称。
- 这次建档适合独立成新 change,不适合继续塞进旧 change。

### 当前可继续动作

- 下一步如果继续,应先起草 `openspec/changes/gaussian-radarscan-dual-track-switch/proposal.md`。
- proposal 需要先界定:
  - 高斯 hide 到 `0.35` 时是否定义为明确的启动阈值。
  - 雷达 show 是否要求沿用已有 `visibility show` 视觉语言。
  - 是否需要同时修改已有 capability,还是只新增反向 capability。

## [2026-03-18 15:56:50 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 笔记: proposal 起草后的结论

## 来源

### 来源1: 新建的 proposal artifact

- 路径: `openspec/changes/gaussian-radarscan-dual-track-switch/proposal.md`
- 要点:
  - proposal 已明确这是 `show-hide-switch-高斯` 的反向按钮。
  - 已把 Gaussian hide 到 `0.35` 时启动 Radar show 写成显式行为契约。
  - capability 名称采用 `gsplat-gaussian-radarscan-switch`。

### 来源2: proposal 创建后的 OpenSpec 状态

- 命令: `openspec status --change "gaussian-radarscan-dual-track-switch"`
- 要点:
  - 当前进度变为 `1/4 artifacts complete`。
  - `design` 与 `specs` 均已解锁。

## 综合发现

### proposal 已锁定的边界

- 这次 change 的核心不是“新增一个普通切换按钮”,而是“补齐 Gaussian -> RadarScan 的反向双轨契约”。
- proposal 层已经把 `0.35` 作为明确启动阈值写死,后续 `design` 应围绕它解释实现方式,而不是重新定义它。
- proposal 层已经把“两颗按钮形成双向切换入口”定义成用户可见行为,后续 editor/runtime/tests 都需要围绕这一点对齐。

## [2026-03-18 16:05:00 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 笔记: 既有 `show-hide-switch-高斯` 的实现证据

## 来源

### 来源1: `Runtime/GsplatRenderer.cs`

- 路径: `Runtime/GsplatRenderer.cs`
- 要点:
  - `k_radarToGaussianShowTriggerProgress01 = 0.35f`,说明当前正向切换的触发点已经落在 `0.35`,不是旧设计里写过的 `0.5`。
  - `AdvancePendingRadarToGaussianShowSwitchIfNeeded()` 会先推进专用 hide overlay,再在达到触发阈值后调用 `TriggerRadarToGaussianShowSwitchNow()`。
  - `BuildLidarShowHideOverlayForThisFrame(...)` 会优先读取 `m_radarToGaussianLidarHideOverlayActive`,保证 overlap 阶段 LiDAR 继续走 hide 语义。
  - `ShouldSubmitSplatsThisFrame()` 在 `m_pendingRadarToGaussianDisableLidar` 存在时放开 Gaussian splat 提交门禁。

### 来源2: `Tests/Editor/GsplatVisibilityAnimationTests.cs`

- 路径: `Tests/Editor/GsplatVisibilityAnimationTests.cs`
- 要点:
  - `kRadarToGaussianShowTriggerProgress01 = 0.35f` 被测试常量直接锁定。
  - `PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway()` 证明 overlap 阶段必须同时满足:
    - `RenderStyle == Gaussian`
    - `EnableLidarScan == true`
    - LiDAR overlay 仍为 hide mode
    - splat 提交重新放开
  - `PlayRadarScanToGaussianShowHideSwitch_DisablesLidarOnlyAfterDedicatedHideOverlayCompletes()` 证明 LiDAR 关闭必须晚于 overlap 结束。

### 来源3: `Editor/GsplatRendererEditor.cs` / `Editor/GsplatSequenceRendererEditor.cs`

- 路径:
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`
- 要点:
  - 现有帮助文案已经向用户承诺 `show-hide-switch-高斯` 是“双轨 overlap 切换”。
  - 目前还没有与之配对的 `show-hide-switch-雷达` 按钮。

## 综合发现

### 已验证结论

- overlap 能跑对,依赖的是三件事同时成立:
  - 独立 hide overlay 轨继续推进
  - 主效果的第二轨在触发点启动
  - 对应提交门禁在 overlap 阶段被显式放开
- 当前 `0.35` 已经是代码和测试共同锁定的默认值。

### 候选设计方向

- 新 change 不应重写旧的 `Radar -> Gaussian` 路径,而应尽量保留其已验证结构。
- `0.35` 更适合提升为“这组双向切换共享的可调参数”,默认值继续保持 `0.35`。
- `Gaussian -> Radar` 不能机械镜像成“半程立刻切 `RenderStyle=ParticleDots`”,否则中段会丢掉 Gaussian hide 的主视觉。

## [2026-03-18 16:08:23 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 笔记: design 已采用的关键决策

## 来源

### 来源1: 新建 design artifact

- 路径: `openspec/changes/gaussian-radarscan-dual-track-switch/design.md`
- 要点:
  - 不重写旧的 `show-hide-switch-高斯`,只做最小共享抽取。
  - `0.35` 升级为默认值不变、但可调的共享阈值。
  - 反向路径采用三阶段:
    - Gaussian hide 共享轨
    - LiDAR show 专用轨
    - Gaussian hide 完成后再落到 RadarScan 稳态

### 来源2: proposal 修订

- 路径: `openspec/changes/gaussian-radarscan-dual-track-switch/proposal.md`
- 要点:
  - 已同步改成“默认 `0.35`,但触发点可调”,避免与 design 口径冲突。

## 综合发现

### 已验证结论

- 这次最重要的不是“再造一个反向按钮”,而是把现有被验证过的 overlap 机制稳稳镜像到反向路径。
- `Gaussian -> Radar` 和 `Radar -> Gaussian` 在用户体验上是对称的,但在 runtime 结构上不是完全镜像的。
- 新设计已经明确避免“半程立即切主 render style”这一条高风险路线。

## [2026-03-18 16:33:06 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 笔记: OpenSpec 四件套已齐

## 来源

### 来源1: `tasks.md`

- 路径: `openspec/changes/gaussian-radarscan-dual-track-switch/tasks.md`
- 要点:
  - 实现任务已按共享阈值、反向 runtime 状态、overlay/gating、editor、tests 五组拆解。
  - 任务明确要求先保护旧按钮,再增加新按钮和共享阈值。

### 来源2: 最终状态检查

- 命令: `openspec status --change "gaussian-radarscan-dual-track-switch" --json`
- 要点:
  - `isComplete=true`
  - `proposal` / `design` / `specs` / `tasks` 全部为 `done`

## 综合发现

### 当前 change 已具备的完成度

- 这条 change 已从“需求口述”推进到“可直接开始实现”的状态。
- artifact 之间的口径已经统一到:
  - 默认 `0.35`,但可调
  - 中段必须双效果并存
  - 新按钮不是机械镜像,而是反向 dual-track

## [2026-03-18 17:08:00 +0800] [Session ID: 7a566a6f-bc80-4706-a356-298650a81244] 笔记: implementation 前的静态证据补充

## 来源

### 来源1: `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs` 的公开 API 入口

- 路径:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
- 要点:
  - `SetVisible(...)`、`PlayShow()`、`PlayHide()`、`SetRenderStyle(...)`、`SetRadarScanEnabled(...)`、`SetRenderStyleAndRadarScan(...)` 开头都会先 `CancelPendingRadarToGaussianShowSwitch()`。
  - 这说明只要反向实现还继续串这些公开入口,中间刚抓到的 overlay / pending 状态就会被立即清掉。

### 来源2: `BuildLidarShowHideOverlayForThisFrame(...)`

- 路径:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
- 要点:
  - 当前优先级只有:
    1. `Radar -> Gaussian` 专用 hide overlay
    2. 共享 `m_visibilityState`
  - 共享状态若处于 `Hiding`,返回的一定是 `mode=2`。
  - 因此反向路径若想在 Gaussian hide 期间让 LiDAR 读到 `show`,必须新增 `Gaussian -> Radar` 专用 overlay,不能复用共享状态。

### 来源3: `ShouldSubmitSplatsThisFrame()`

- 路径:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
- 要点:
  - 当前只有 `m_pendingRadarToGaussianDisableLidar` 会在 overlap 期间放开 splat 提交门禁。
  - 一旦反向路径在 overlap 中打开 `EnableLidarScan` 且 `HideSplatsWhenLidarEnabled=true`,若没有新的 pending 放行条件,Gaussian splat 会立刻被门禁掐掉。

## 综合发现

### 现象

- 旧正向路径之所以能成立,靠的是“专用 overlay + 专用 pending 门禁 + 避开公开 API cancel”这三个点一起成立。
- 反向路径目前三者都还没有。

### 当前主假设

## [2026-03-18 21:49:17 +0800] [Session ID: 505DEC94-AB29-42D5-B880-88BC2E74BFE0] 笔记: reverse 尾段闪断的证据收敛

## 来源

### 来源1: Unity 场景中的真实 `GsplatRenderer` 组件快照

- 资源: `mcpforunity://scene/gameobject/-7795406/components`
- 要点:
  - 当前人工验收对象是 `ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest`。
  - 关键参数:
    - `VisibilityProgressMode = WorldSpeed`
    - `ShowDuration = 4.0`
    - `HideDuration = 6.0`
    - `DualTrackSwitchTriggerProgress01 = 0.35`
    - `HideSplatsWhenLidarEnabled = true`
    - `LidarKeepUnscannedPoints = true`
  - 这说明现场不是测试里那种短时长 `LegacyDuration` 场景,而是一个较长尾、世界速度驱动的真实切换。

### 来源2: `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`

- 路径:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
- 要点:
  - `AdvancePendingGaussianToRadarShowSwitchIfNeeded()` 会先推进 reverse 专用 show overlay,再等 `FinalizeGaussianToRadarRadarModeIfNeeded()` 把状态落到稳定 RadarScan。
  - 旧实现里,一旦 `m_gaussianToRadarLidarShowOverlayActive` 变成 false,`BuildLidarShowHideOverlayForThisFrame(...)` 就会继续走共享 `m_visibilityState`。
  - 当此时 `m_pendingGaussianToRadarFinalizeRadarMode` 仍为 true 时,共享状态很可能还是 `Hiding` 或刚到 `Hidden`。

### 来源3: 定向验证

- 命令:
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`
  - Unity EditMode:
    - `BuildLidarShowHideOverlay_GaussianToRadarFinalizeTail_DoesNotFallBackToHide`
    - `PlayGaussianToRadarScanShowHideSwitch_DoesNotFlashRadarBetweenOverlapAndStableMode`
    - `PlayGaussianToRadarScanShowHideSwitch_DelaysRadarShowUntilSharedTrigger_AndRestoresStableRadarMode`
    - `DualTrackSwitchTriggerProgress01_AffectsBothDirections`
    - `PlayRadarScanToGaussianShowHideSwitch_DisablesLidarOnlyAfterDedicatedHideOverlayCompletes`
- 要点:
  - 编译通过,0 warning / 0 error。
  - reverse 尾段状态级 guard 通过。
  - reverse 主流程、共享阈值、forward 旧流程回归都通过。

## 综合发现

### 现象

- 用户人工验收看到的是: `show-hide-switch-雷达` 后半段,雷达粒子已经显示出来后,会突然整批消失一次,然后再回来。

### 假设

- 当前主假设是:
  - reverse 专用 show overlay 在 finalize 前已经结束。
  - 旧逻辑随后回落到共享 `Hiding/Hidden`。
  - 结果 LiDAR 在尾段被错误读成 hide 或 gate=0,造成“闪一下”。
- 备选解释:
  - 现场参数使用 `WorldSpeed` 且时长较长,让这个尾段窗口比测试场景更容易暴露。
  - 即便专用 show overlay 本身没有明显提前结束,只要它和 finalize 的交接太贴边,也可能在一两帧里出现空窗。

### 已验证结论

- 这次修复不需要改公开 API 的 cancel/reset 语义。
- 真正要补的是 `BuildLidarShowHideOverlayForThisFrame(...)` 的 reverse finalize 尾段优先级:
  - 当 `m_pendingGaussianToRadarFinalizeRadarMode` 仍为 true 时,
  - 若专用 show overlay 已经结束,LiDAR 也必须继续保持稳定可见,
  - 不能再回落到共享 `Hiding/Hidden`。

- 最稳的实现方式是:
  - 先把公开 API 的真实执行体拆成无副作用 helper
  - 再新增 `Gaussian -> Radar` 的专用 show overlay 和 pending finalize 门禁
  - 最后让两个 dual-track 按钮都只在 public 层做 cancel,内部编排全部走 helper

### 最强备选解释

- 也可能完全不需要无副作用 helper,只要在反向路径里避免调用会 cancel 的公开 API 即可。
- 但从静态阅读看,反向路径至少需要安全地设置 render style / visible / lidar runtime 稳态,没有 helper 后代码会被迫重复拷贝大量公开 API 体内逻辑。

### 当前结论

- 这一步已经具备静态证据,可以开始正式改 runtime。
- 动态证据还要靠后面的单测和编译验证补上,目前还不能把“实现正确”当成已验证结论。

## [2026-03-18 17:44:00 +0800] [Session ID: 7a566a6f-bc80-4706-a356-298650a81244] 笔记: implementation 与验证结果

## 来源

### 来源1: runtime / editor 实装

- 路径:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`
- 要点:
  - 两个 renderer 都新增了共享字段 `DualTrackSwitchTriggerProgress01`。
  - public API 外层现在统一先做 dual-track cancel,真正的内部编排改走无副作用 helper。
  - 新增了 `PlayGaussianToRadarScanShowHideSwitch(...)` 与反向专用 LiDAR show overlay / finalize 门禁。
  - inspector 已加入 `show-hide-switch-雷达` 按钮,并明确正反按钮共享同一阈值。

### 来源2: 定向验证

- 命令: `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`
- 结果:
  - `0 个警告`
  - `0 个错误`
- Unity EditMode 定向测试:
  - `PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway` -> Passed
  - `PlayRadarScanToGaussianShowHideSwitch_DelayedFirstTick_DoesNotStartShowBeforeLidarHalfway` -> Passed
  - `PlayRadarScanToGaussianShowHideSwitch_DisablesLidarOnlyAfterDedicatedHideOverlayCompletes` -> Passed
  - `PlayGaussianToRadarScanShowHideSwitch_DelaysRadarShowUntilSharedTrigger_AndRestoresStableRadarMode` -> Passed
  - `DualTrackSwitchTriggerProgress01_AffectsBothDirections` -> Passed
  - `PlayShow_DuringHiding_RestartsShowFromZero` -> Passed

### 来源3: 全量包测试的背景噪声

- Unity 全量 `Gsplat.Tests.Editor` 在当前环境下仍有历史问题/环境问题:
  - `ImportV1_StaticSingleFrame4D_RealFixturePlyThroughExporterAndImporter` 缺少 `python3 numpy`
  - 若干旧动画用例在当前编辑器环境下仍会独立失败
- 这些失败没有指向本次新增的 dual-track 改动,因此本轮只把和本 change 直接相关的时序验证作为交付证据。

## 综合发现

### 已验证结论

- 反向按钮现在已经具备真正的 overlap:
  - 前半段只做 Gaussian hide
  - 到共享阈值后启动 Radar show
  - overlap 阶段保持 Gaussian splat 提交
  - Gaussian hide 完成后再落到稳定 RadarScan
- 正向按钮没有继续吃硬编码 `0.35`,而是和反向按钮一起读取共享阈值字段。
- public API cancel 语义与内部 helper 已经分层,这次不会再因为中途调用 `SetRenderStyle*` / `SetRadarScanEnabled` 把刚抓到的中间态抹掉。

### 仍需记住的风险

- 当前全量包测试里还有和本 change 无关的历史噪声,后续若要做 release 级验收,最好单独清理 `numpy` 依赖和旧动画测试稳定性。
- `GsplatSequenceRenderer` 目前主要依赖编译对齐而不是独立时序单测,后续如果序列版的使用频率提高,值得再补一组专门的 dual-track 时序测试。
