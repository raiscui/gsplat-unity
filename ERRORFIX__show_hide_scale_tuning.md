# ERRORFIX__show_hide_scale_tuning

## 2026-03-15 23:35:00 +0800 问题: WorldSpeed 首轮实现没有真正对齐 reveal 前沿世界速度

### 问题现象
- 新增的 `AdvanceVisibilityState_WorldSpeedMode_KeepsRevealFrontDistanceComparableAcrossBoundsScales` 测试失败。
- 小 bounds 与大 bounds 在相同 `worldSpeed` 下,show 前沿距离相差明显。

### 原因分析
- 首轮实现采用:
  - `progressStep = dt * worldSpeed / totalRange`
- 但 shader 的 reveal 半径实际使用:
  - `radius = EaseInOutQuad(progress01) * totalRange`
- 因此“线性推进 progress01”并不等于“线性推进 reveal 半径”。
- 在动画起始阶段,`EaseInOutQuad` 为二次曲线,大对象因为 `progress01` 更小而被进一步减速。

### 修复方法
- 新增 `EaseInOutQuad` 和 `InverseEaseInOutQuad` 工具函数。
- WorldSpeed 模式下改为:
  1. 先把 `currentProgress01` 映射到 `easedProgress`。
  2. 在线性世界距离空间推进 `easedProgress += dt * worldSpeed / totalRange`。
  3. 再用 `InverseEaseInOutQuad` 反推出新的 `progress01`。
- LegacyDuration 模式保持原有按时长线性推进语义不变。

### 验证
- `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过。
- `Gsplat.Tests.GsplatVisibilityAnimationTests.AdvanceVisibilityState_WorldSpeedMode_KeepsRevealFrontDistanceComparableAcrossBoundsScales` 通过。
- `Gsplat.Tests.GsplatVisibilityAnimationTests.AdvanceVisibilityState_LegacyDurationMode_ProgressRemainsBoundsIndependent` 通过。
- `Gsplat.Tests.GsplatVisibilityAnimationTests.BuildLidarShowHideOverlay_VisibilityRadiusScale_ScalesMaxRadius` 通过。

## [2026-03-18 01:07:19 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 问题: show-hide-switch-高斯 把半程触发绑在墙钟时间,导致高斯抢跑

### 问题现象
- 用户反馈点击 `show-hide-switch-高斯` 后:
  - 雷达粒子很快就退掉
  - 高斯 show 也提前开始
- 和预期的“LiDAR hide 真正过半后,高斯再进场”不一致。

### 原因分析
- 首轮实现使用的是:
  - `Time.realtimeSinceStartup - armTime >= halfDuration`
- 但 LiDAR hide 的实际推进发生在:
  - `AdvanceLidarAnimationStateIfNeeded()`
  - 也就是 `Update / SubmitDrawForCamera / Editor ticker` 真正跑到时,状态机才会前进
- 这就产生了时间轴脱节:
  - 如果第一次 tick 来得比较晚
  - wall-clock 已经过了 halfDuration
  - 但 `m_lidarVisibilityAnimProgress01` 仍然还没推进到 0.5
  - 高斯 show 就会被提前触发

### 动态证据
- 新增复现实验:
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelayedFirstTick_DoesNotStartShowBeforeLidarHalfway`
- 该测试先故意让第一次 `Update` 晚于 halfDuration 再到来。
- 修复前它会失败,关键输出是:
  - `Expected: ParticleDots`
  - `But was: Gaussian`
- 这证明“高斯提前 show”不是主观观感,而是状态机确实在错误时间轴上触发。

### 修复方法
- 不再用 wall-clock 作为半程判定。
- 改为直接绑定 LiDAR hide 动画状态机:
  1. 若 `m_lidarVisibilityAnimating=true` 且目标是 fade-out
  2. 只有当 `m_lidarVisibilityAnimProgress01 >= 0.5f` 时
  3. 才允许切到 `Gaussian` 并启动高斯 `show`
- 对于没有实际 fade-out 的场景(例如时长为 0 或雷达本来就不可见),保留立即切换的兜底路径。

### 验证
- 编译通过:
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`
- Unity 定向测试通过:
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelayedFirstTick_DoesNotStartShowBeforeLidarHalfway`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.SetRenderStyleAndRadarScan_Animated_DelayHideSplatsUntilRadarVisible`

## [2026-03-18 01:26:25 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 问题: show-hide-switch-高斯 在按钮点击瞬间就把雷达粒子直接杀掉

### 问题现象
- 用户反馈当前版本虽然修掉了“高斯抢跑”,但视觉仍然不对:
  - 不能在点按钮时就关闭 `EnableLidarScan`
  - 现在看起来像“雷达粒子立刻消失”

### 原因分析
- 这轮确认不是单一根因,而是两个动作同时过硬:
  1. `PlayRadarScanToGaussianShowHideSwitch(...)` 起点调用了 `SetVisible(false, animated: false)`
     - 共享显隐状态立刻进入 `Hidden`
     - `BuildLidarShowHideOverlayForThisFrame(...)` 会因此输出 `gate = 0.0f`
     - LiDAR 也被一起裁没
  2. 按钮点击当下就调用 `SetRadarScanEnabled(false, animated: true, ...)`
     - `EnableLidarScan` 被立刻写成 `false`
     - 某些 runtime 链路会提前停掉或不再更新
- 现有测试还把“`EnableLidarScan` 必须立即变成 false”写成了正确行为,导致错误语义被回归锁死

### 修复方法
- 把按钮编排改成真正的两阶段:
  1. 起点只启动 LiDAR hide,不再调用 `SetVisible(false, animated: false)`
  2. 半程触发时才把 `EnableLidarScan` 置为 `false`
  3. 同时切到 `Gaussian`,并从 `FullHidden` 起点启动高斯 show
  4. 在重叠阶段让 LiDAR render bypass 共享 show/hide overlay,只受自己的 `m_lidarVisibility01` 控制
- 同步把 `TickLidarRangeImageIfNeeded()` 从“只认 `EnableLidarScan`”改成“认 `IsLidarRuntimeActive()`”,保证 keepalive fade-out 期间仍能更新
- 修改测试断言,改为锁定:
  - 前半段 `EnableLidarScan` 仍为 true
  - 半程后才切 Gaussian show
  - 重叠阶段 LiDAR overlay gate 仍保持打开

### 验证
- 编译通过:
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`
- Unity MCP `run_tests` 本轮未获得有效动态结果:
  - 返回 `summary.total = 0`
  - 当前工程 `manifest.json` 的 `wu.yize.gsplat` 指向外部 `file:` 包路径,Test Runner 很可能没有真正加载当前工作区代码
  - 临时 `testables` 已恢复,未残留到 `manifest.json`

## [2026-03-18 01:44:10 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 问题: show-hide-switch-高斯 前半段没有走 visibility hide 的 noise 燃烧过程

### 问题现象
- 用户反馈:
  - 雷达粒子前半段 hide 应该执行 `visibility hide` 按钮的处理过程
  - 应该有 noise / 燃烧效果
  - 当前却“什么都没有”

### 原因分析
- 上一轮为了解决“雷达被一按就裁掉”,把前半段改成了:
  - 只做 LiDAR 自己的 visibility fade-out
- 但 `visibility hide` 的 noise / radial / glow 语言并不来自这条 LiDAR fade-out
- 它来自共享显隐状态机:
  - `PlayHide()`
  - `m_visibilityState = Hiding`
  - `BuildLidarShowHideOverlayForThisFrame(...)` 输出 `mode = 2`
- 也就是说,前半段根本没有进入用户真正想要的那条 hide 处理链

### 修复方法
- 在 `PlayRadarScanToGaussianShowHideSwitch(...)` 中:
  1. 先启动 LiDAR hide
  2. 若 hide 真正启动成功,再补走 `PlayHide()`
  3. 最后重新 `ArmRadarToGaussianShowSwitch()`
- 这样可以同时满足:
  - 前半段拥有 `visibility hide` 的 noise 燃烧效果
  - 半程后仍然能按计划切到 Gaussian show
- 关键点在于顺序:
  - `PlayHide()` 会 `CancelPendingRadarToGaussianShowSwitch()`
  - 所以必须在它之后重新 arm

### 验证
- 编译通过:
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`
- 回归测试语义已同步改为:
  - 前半段 `VisibilityState == Hiding`
  - 前半段 `BuildLidarShowHideOverlay(...).mode == 2`
