## [2026-03-18 15:43:56 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 任务名称: gaussian-radarscan-dual-track-switch OpenSpec 新建

### 任务内容
- 为“`show-hide-switch-雷达` 反向按钮”建立独立 OpenSpec change。
- 核对其与既有 `radarscan-gaussian-dual-track-switch` 的边界,避免新需求误落到旧 change。
- 获取当前工作流的 artifact 进度与首个模板,并停在可继续起草的位置。

### 完成过程
- 阅读既有 `radarscan-gaussian-dual-track-switch` proposal/tasks,确认它只覆盖 `RadarScan -> Gaussian`。
- 新建 `openspec/changes/gaussian-radarscan-dual-track-switch/`,默认 schema 为 `spec-driven`。
- 运行 `openspec status --change "gaussian-radarscan-dual-track-switch"` 获取当前状态。
- 运行 `openspec instructions proposal --change "gaussian-radarscan-dual-track-switch"` 获取第一个 artifact 的官方模板。

### 总结感悟
- 正反两个方向的 dual-track 切换虽然相似,但用户语义、时间线起点和 capability 边界不同,独立建档更干净。
- 在 OpenSpec 初始建档阶段,先把“是否应复用旧 change”判断清楚,比直接生成 proposal 更重要,这样后续设计不会一开始就混线。

## [2026-03-18 15:56:50 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 任务名称: gaussian-radarscan-dual-track-switch proposal 起草

### 任务内容
- 根据用户补充的正式需求,创建 `proposal.md`。
- 把反向按钮、`0.35` 启动阈值、与 `show-hide-switch-高斯` 成对的切换语义固化到 OpenSpec。

### 完成过程
- 读取 `openspec status --change "gaussian-radarscan-dual-track-switch" --json` 与 `openspec instructions proposal --change "gaussian-radarscan-dual-track-switch" --json`。
- 对照既有 `radarscan-gaussian-dual-track-switch` proposal/spec 命名风格,确定新 capability 命名为 `gsplat-gaussian-radarscan-switch`。
- 写入 `openspec/changes/gaussian-radarscan-dual-track-switch/proposal.md`。
- 运行 `openspec status --change "gaussian-radarscan-dual-track-switch"` 验证 proposal 已完成并解锁后续 artifacts。

### 总结感悟
- proposal 阶段最重要的是把“用户真正要的时间线”写成契约,尤其是 `0.35` 这种门槛值,不要留到 design 才补。
- 当一组按钮存在明确的正反配对关系时,proposal 里就应该把它写成用户行为模型,这样后面 tests 才有稳定锚点。

## [2026-03-18 16:08:23 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 任务名称: gaussian-radarscan-dual-track-switch design 起草

### 任务内容
- 读取现有 `show-hide-switch-高斯` 的真实实现与测试,为反向按钮撰写 `design.md`。
- 把“中段双效果并存”和“默认 `0.35` 但可调”沉淀成技术决策。

### 完成过程
- 阅读 `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs` 中正向 dual-track 的状态字段、overlay 构建与提交门禁逻辑。
- 阅读 `Tests/Editor/GsplatVisibilityAnimationTests.cs`,确认当前默认触发点已被测试锁为 `0.35f`。
- 撰写 `openspec/changes/gaussian-radarscan-dual-track-switch/design.md`,明确:
  - 保留旧路径结构
  - 引入共享可调阈值
  - 反向路径使用专用 LiDAR show overlay 和专用 overlap 放行门禁
- 同步修订 `proposal.md`,把“固定 `0.35`”改成“默认 `0.35`,但可调”。
- 运行 `openspec status --change "gaussian-radarscan-dual-track-switch"` 验证 `design` 已完成并解锁 `specs`。

### 总结感悟
- 这类 UI 按钮的“对称”很容易让人误以为 runtime 也可以机械镜像,但真正决定成败的是可见层结构是否对称。
- 先尊重已经被多轮验证过的旧路径,再小范围抽取共享参数,通常比大重构更稳。

## [2026-03-18 16:33:06 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 任务名称: gaussian-radarscan-dual-track-switch specs/tasks 收尾

### 任务内容
- 创建 `specs` artifact,把反向 dual-track 的 requirement 级契约写实。
- 创建 `tasks` artifact,把实现拆成可执行清单。
- 用 OpenSpec 状态检查确认四件套已全部完成。

### 完成过程
- 创建 `openspec/changes/gaussian-radarscan-dual-track-switch/specs/gsplat-gaussian-radarscan-switch/spec.md`。
- 在 spec 中锁定:
  - `show-hide-switch-雷达` 的起始、overlap、最终稳态
  - 默认 `0.35` 且可调的共享阈值
  - 正反按钮共享同一阈值语义
  - editor 中成对按钮暴露
- 创建 `openspec/changes/gaussian-radarscan-dual-track-switch/tasks.md`。
- 运行 `openspec status --change "gaussian-radarscan-dual-track-switch" --json`,确认 `isComplete=true`。

### 总结感悟
- 真正让 OpenSpec 变得可实现的,往往不是 proposal 里的动机,而是 spec 和 tasks 里把最容易歪掉的时间线钉死。
- 当一个需求已经踩过多轮试错,把“为什么之前会做错”提前写进 design/spec/tasks,能显著降低下一轮实现再次走偏的概率。

## [2026-03-18 17:45:00 +0800] [Session ID: 7a566a6f-bc80-4706-a356-298650a81244] 任务名称: gaussian-radarscan-dual-track-switch implementation 落地

### 任务内容
- 实现 `show-hide-switch-雷达` 的反向 dual-track overlap 切换。
- 先抽无副作用内部 helper,再接入共享阈值、反向 overlay、editor 按钮与定向测试。

### 完成过程
- 在 `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs` 中新增共享字段 `DualTrackSwitchTriggerProgress01`,默认值保持 `0.35`。
- 将 `SetVisible` / `PlayShow` / `PlayHide` / `SetRenderStyle*` / `SetRadarScanEnabled` 的 public 包装层与无副作用 helper 分离,避免内部编排时被 cancel 语义清空中间态。
- 为 `Gaussian -> RadarScan` 新增:
  - `PlayGaussianToRadarScanShowHideSwitch(...)`
  - 专用 LiDAR show overlay 轨
  - overlap 期间的 splat 提交放行门禁
  - Gaussian hide 完成后的 RadarScan 稳态收敛
- 更新 `Editor/GsplatRendererEditor.cs` / `Editor/GsplatSequenceRendererEditor.cs`:
  - 新增 `show-hide-switch-雷达` 按钮
  - 文案明确正反按钮共用 `DualTrackSwitchTriggerProgress01`
- 扩展 `Tests/Editor/GsplatVisibilityAnimationTests.cs`,增加:
  - 反向按钮的前半段 / overlap / 稳态测试
  - 共享阈值同时作用于正反按钮的测试
- 验证:
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过
  - 5 条 dual-track 相关定向 EditMode 测试通过

### 总结感悟
- 这次真正避免重踩旧坑的关键,不是“再写一个反向按钮”,而是先把 public cancel 语义和内部编排 helper 拆开。
- 对 dual-track 这种时序功能来说,“是否 overlap”必须靠测试锁中间态,只看最终状态几乎一定会漏掉退化。

## [2026-03-18 21:49:17 +0800] [Session ID: 505DEC94-AB29-42D5-B880-88BC2E74BFE0] 任务名称: reverse dual-track 雷达闪断修复

### 任务内容
- 修复 `show-hide-switch-雷达` 后半段雷达粒子“闪灭一次再回来”的问题。
- 为 reverse finalize 尾段补回归测试,并确认不影响正向旧按钮和共享阈值行为。

### 完成过程
- 先从 Unity 当前场景的真实 `GsplatRenderer` 组件读取实际参数,确认人工验收对象使用 `WorldSpeed`、长尾 hide 和共享阈值 `0.35`。
- 在 `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs` 的 `BuildLidarShowHideOverlayForThisFrame(...)` 中新增 reverse finalize 尾段优先分支:
  - 若 `m_pendingGaussianToRadarFinalizeRadarMode` 仍为 true 且专用 show overlay 已结束,
  - 则 LiDAR 保持稳定可见,不再回退到共享 hide/hidden。
- 扩展 `Tests/Editor/GsplatVisibilityAnimationTests.cs`:
  - 新增 `BuildLidarShowHideOverlay_GaussianToRadarFinalizeTail_DoesNotFallBackToHide`
  - 保留并复跑 `PlayGaussianToRadarScanShowHideSwitch_DoesNotFlashRadarBetweenOverlapAndStableMode`
- 验证:
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`
  - Unity EditMode 定向测试 5 条通过

### 总结感悟
- 这类“已经显示出来后又闪没一下”的问题,常常不是入口没触发,而是 finalize 尾段缺少一个看似不起眼的连续性保护分支。
- 对 dual-track 时序来说,除了入口和终态测试,还需要专门加“中间态尾段 guard”,否则真实场景参数一拉长就容易露馅。
