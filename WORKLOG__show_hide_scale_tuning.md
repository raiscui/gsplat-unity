# WORKLOG__show_hide_scale_tuning

## 2026-03-15 22:18:00 +0800 任务名称: 分析 3DGS show/hide 动画尺度差异

### 任务内容
- 分析 `ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest` 与 `s1_point_cloud_v2_sh3_full_k8192_f32_20260312` 的 show/hide 动画差异。
- 核对包内 reveal/burn 动画的半径、中心点、进度推进与 shader 公式。
- 对比场景里的 renderer 参数和两个 `.splat4d` 资产本身的空间尺度。

### 完成过程
- 先读取 `GsplatRenderer.cs`、`Gsplat.shader`、`GsplatSplat4DImporter.cs`,确认 show/hide 半径直接依赖 `GsplatAsset.Bounds` 与 `VisibilityCenter`。
- 再对 `Assets/OutdoorsScene.unity` 做场景参数比对,确认两者 `ShowDuration`、`HideDuration`、ring/trail 宽度、Noise/Warp 等参数基本相同。
- 最后直接解析两个 `.splat4d v2` 的 `RECS` section,量化其 bounds 与 reveal 半径差异,并结合当前场景共享 `VisibilityCenter` 估算 show 前沿世界空间速度。

### 总结感悟
- 当前 reveal 动画本质上是“按资产 bounds 做空间扫掠”,不是“按统一世界尺度播放同一特效”。
- 当不同 3DGS 的世界尺寸差很多时,只复用同一套 reveal 参数通常不够,需要同时考虑资产尺度、中心点和半径归一化策略。

## 2026-03-15 23:35:00 +0800 任务名称: 落地 show/hide 的 WorldSpeed 驱动与验证

### 任务内容
- 将 3DGS show/hide 从“按总时长线性推进 progress”扩展为“按目标世界前沿速度驱动”的新模式。
- 以 `ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest` 当前观感反推出默认基准速度。
- 增加 `VisibilityRadiusScale` 作为 reveal 半径的独立缩放参数。
- 为 `GsplatRenderer` / `GsplatSequenceRenderer` / `GsplatVisibilityAnimationTests` 补齐实现与验证。

### 完成过程
- 首轮实现先按 `progressStep = dt * speed / totalRange` 推进,随后通过定向测试发现这并不能真正保持 reveal 前沿世界距离一致。
- 回读 shader 后确认 reveal 半径使用的是 `EaseInOutQuad(progress01) * totalRange`,因此把修正点下沉到 eased 空间:
  - 新增 `GsplatVisibilityAnimationUtil.EaseInOutQuad`。
  - 新增 `GsplatVisibilityAnimationUtil.InverseEaseInOutQuad`。
  - WorldSpeed 模式改为线性推进 `easedProgress`,再反解回 `progress01`。
- 同时保留 `LegacyDuration` 模式,确保旧场景仍可继续按时长驱动。
- 过程中还验证了当前 Unity 工程实际使用的是 embedded package `Packages/wu.yize.gsplat`,不是外部 `file:` 依赖对应源码。

### 验证结果
- 编译验证:
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过。
- 定向测试通过:
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.BuildLidarShowHideOverlay_VisibilityRadiusScale_ScalesMaxRadius`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.AdvanceVisibilityState_WorldSpeedMode_KeepsRevealFrontDistanceComparableAcrossBoundsScales`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.AdvanceVisibilityState_LegacyDurationMode_ProgressRemainsBoundsIndependent`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayHide_EndsHidden_ValidBecomesFalse`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayHide_DuringShowing_RestartsHideFromZero`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayShow_DuringHiding_RestartsShowFromZero`(重跑后通过,表现出时间敏感抖动)
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayShow_FromHidden_ValidBecomesTrue`

### 总结感悟
- 对带 easing 的 reveal 动画,若目标是“世界空间前沿速度一致”,就不能把速度直接施加在原始 progress 上,而要施加在真正驱动空间半径的 eased 量上。
- Unity 包来源判断不能只看 `manifest.json`,还要结合 `packages-lock.json` 与生成的 `.csproj` 交叉确认。

## 2026-03-16 00:10:00 +0800 任务名称: 将 hide 预收缩曲线从 EaseOutCirc 改为 EaseInSine

### 任务内容
- 调整 hide 粒子预收缩阶段的 easing。
- 保持 hide 前沿扩张和 WorldSpeed 逻辑不变。

### 完成过程
- 定位到 `Runtime/Shaders/Gsplat.shader` 中 `EaseOutCirc` 的唯一 hide 使用点。
- 将函数定义改为 `EaseInSine`。
- 将 `tApproach = EaseOutCirc(tApproach)` 改为 `tApproach = EaseInSine(tApproach)`。
- 同步更新了函数注释,明确新的节奏语义是“起手更克制,靠近前沿时再更明显进入 afterglow 缩放”。

### 验证结果
- `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过。
- Unity Console 未出现新的 shader 编译错误。
- `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayHide_EndsHidden_ValidBecomesFalse` 通过。

### 总结感悟
- 这次变更只影响 hide 的预收缩节奏,不影响球形前沿的世界空间运动曲线。
- 从观感上说,新曲线会让粒子在前段更稳,更靠近燃烧前沿时才明显缩小。

## 2026-03-16 00:22:00 +0800 任务名称: 统一 hide 两段缩放曲线为 EaseInSine

### 任务内容
- 在已完成 hide 预收缩改造的基础上,继续把前沿扫过后的烧尽曲线也统一为 `EaseInSine`。

### 完成过程
- 回读 `Runtime/Shaders/Gsplat.shader` 中 hide 的 `passedForFade / passedForTail / insideScale` 路径。
- 将 `passedForFade = passed * passed` 改为 `passedForFade = EaseInSine(passed)`。
- `passedForTail` 保持与 `passedForFade` 一致,从而让 alpha / tail / insideScale 使用同一条 hide ease-in 曲线。

### 验证结果
- `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过。
- `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayHide_EndsHidden_ValidBecomesFalse` 通过。
- `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayHide_DuringShowing_RestartsHideFromZero` 通过。

### 总结感悟
- 现在 hide 的前后两段缩放不再是“前半段一种 ease,后半段另一种 ease”的拼接感。
- 观感上会更统一,更像同一股收缩趋势逐渐接管整个 hide 过程。

## [2026-03-18 00:51:48 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 任务名称: 新增 RadarScan -> Gaussian 的 show-hide-switch-高斯 按钮

### 任务内容
- 新增一个独立于现有 `Gaussian(动画)` 的新按钮 `show-hide-switch-高斯`。
- 目标语义不是并行渐变,而是分段切换:
  - 雷达粒子先 hide
  - 到 hide 过程过半
  - 高斯基元再开始 show

### 完成过程
- 先回读 `GsplatRendererEditor` / `GsplatSequenceRendererEditor` / `GsplatRenderer` / `GsplatSequenceRenderer`,确认现有 `Gaussian(动画)` 是 LiDAR fade-out 与 RenderStyle morph 并行执行。
- 静态验证后确认: 只调用现有 API 会让高斯过早重新提交,无法满足“半程后再 show”的时序。
- 在两个 renderer 内新增 `PlayRadarScanToGaussianShowHideSwitch(...)`:
  - 前半段先 `SetVisible(false, animated:false)` 压住 splat
  - 同时 `SetRadarScanEnabled(false, animated:true, ...)` 让雷达先淡出
  - 到 radar hide 时长一半时,硬切到 `Gaussian` 并触发 `SetVisible(true, animated:true)`
- 在两个 Inspector 中新增同名按钮,并补充帮助文案。
- 增加 `GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway` 回归测试,锁定“半程前不抢跑,半程后才开始高斯 show”的语义。

### 验证结果
- 编译通过:
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`
- Unity 定向测试通过:
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.SetRenderStyleAndRadarScan_Animated_DelayHideSplatsUntilRadarVisible`
- 验证过程中为收集包测试,临时把 `wu.yize.gsplat` 加到工程 `testables`,跑完后已恢复 `manifest.json` 原状。

### 总结感悟
- 这次的关键不是“换一个更花的 easing”,而是把并行切换改造成分段编排。
- 对这类 Unity Inspector 按钮功能,如果只把延迟写在 Editor 层,后续很难复用和测试;把编排下沉到 runtime API,明显更稳。

## [2026-03-18 01:07:19 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 任务名称: 修正 show-hide-switch-高斯 的高斯抢跑问题

### 任务内容
- 修正新按钮 `show-hide-switch-高斯` 的实际观感与预期不一致的问题。
- 目标是保证:
  - 雷达 hide 真的过半后
  - 高斯才开始 show

### 完成过程
- 先不直接补丁,而是补了一个最小复现实验:
  - `PlayRadarScanToGaussianShowHideSwitch_DelayedFirstTick_DoesNotStartShowBeforeLidarHalfway`
- 该实验故意让第一次 `Update` 晚到,成功复现旧实现的抢跑:
  - 旧逻辑会在第一帧直接变成 `Gaussian`
- 在此基础上把运行时判定从“墙钟时间过半”改成“LiDAR hide 动画进度过半”:
  - `m_lidarVisibilityAnimProgress01 >= 0.5f`
- 同步修正了脆弱测试断言,避免把“刚触发时 show 进度必须极小”这种依赖 Unity 自动 Update 时序的条件继续锁死。

### 验证结果
- `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过。
- Unity 定向测试通过:
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelayedFirstTick_DoesNotStartShowBeforeLidarHalfway`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.SetRenderStyleAndRadarScan_Animated_DelayHideSplatsUntilRadarVisible`

### 总结感悟
- 对这类编辑器动画编排,真正可靠的“过半”应该绑定状态机进度,不能绑定按钮按下后的墙钟时间。
- 这次新增的红测价值很高,它正好卡住了“第一次 tick 来晚”这种肉眼最容易看到、但普通逐帧测试不容易覆盖的真实场景。

## [2026-03-18 01:26:10 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 任务名称: 修正 show-hide-switch-高斯 一按即杀雷达的问题

### 任务内容
- 修正 `show-hide-switch-高斯` 仍然会在按钮点击瞬间把雷达粒子直接干掉的问题。
- 目标语义改成:
  - 按钮起点只启动雷达 hide
  - 半程后才启动 Gaussian show
  - 重叠阶段 LiDAR 继续按自己的 fade-out 退场

### 完成过程
- 静态复核确认了两个直接问题:
  1. `SetVisible(false, animated: false)` 会把共享显隐状态直接打成 `Hidden`,LiDAR overlay 也跟着归零
  2. `EnableLidarScan=false` 写得太早,会让 RadarScan runtime 链路提前退场
- 在 `GsplatRenderer` 和 `GsplatSequenceRenderer` 中把这条按钮编排改成真正的两阶段:
  - 起点只调用 LiDAR hide 的内部过渡,不再立刻关总开关
  - 半程触发时才关闭 `EnableLidarScan`,切到 `Gaussian`,并从 `FullHidden` 起点启动高斯 show
  - 重叠阶段给 LiDAR render 增加 bypass,避免它被 Gaussian 的共享 show overlay 一起裁掉
  - `TickLidarRangeImageIfNeeded()` 也同步改成认 `IsLidarRuntimeActive()`,让 keepalive fade-out 期间继续更新
- 同步修改回归测试:
  - 把“立即关闭 `EnableLidarScan`”这种错误断言去掉
  - 改成验证“前半段雷达仍启用,半程才切 Gaussian,重叠期 LiDAR gate 仍打开”

### 验证结果
- 编译通过:
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`
- Unity MCP `run_tests` 本轮未形成有效动态证据:
  - 返回 `summary.total = 0`
  - 当前工程 `manifest.json` 中 `wu.yize.gsplat` 指向外部 `file:` 包路径,因此 Test Runner 很可能没有真正加载当前工作区代码
  - 临时 `testables` 改动已在验证后恢复

### 总结感悟
- 这次不是单纯调阈值,而是把“状态机语义”纠正回来了。
- 对共享 show/hide 系统来说,最危险的不是 easing 不对,而是把本该分阶段的编排提前硬写进一个起点动作里。

## [2026-03-18 01:43:55 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 任务名称: 补回 show-hide-switch-高斯 前半段的 noise 燃烧 hide 过程

### 任务内容
- 根据用户新补充的目标,修正 `show-hide-switch-高斯` 前半段缺少 `visibility hide` 燃烧效果的问题。
- 目标是让雷达粒子前半段真正复用 `Hide` 按钮那套处理过程。

### 完成过程
- 先重新比对 `PlayRadarScanToGaussianShowHideSwitch(...)` 与 `PlayHide()`:
  - 确认上一轮前半段只做了 LiDAR visibility fade-out
  - 没进入共享 `Hiding` 状态机
- 在两个 renderer 中补了 `BeginVisibilityHideForRadarToGaussianShowSwitch()`:
  - 仅在雷达 hide 真正启动成功后才调用
  - 调用顺序放在 `ArmRadarToGaussianShowSwitch()` 之前
  - 避免 `PlayHide()` 内部的 `CancelPending...` 把半程切换计划清掉
- 同步修改回归测试:
  - 前半段从校验 `Visible` 改为校验 `Hiding`
  - 补充 `BuildLidarShowHideOverlay(...).mode == 2` 的证据

### 验证结果
- `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过

### 总结感悟
- 这次真正缺的不是参数,而是状态机入口。
- 当用户说“要执行某个按钮的处理过程”时,最稳的做法通常不是手工模仿结果,而是把调用链真正接回那个过程本身。

## [2026-03-18 09:37:07 +0800] [Session ID: unknown] 任务名称: 重新诊断 Radar->Gaussian 切换需求并整理双轨实施方案

### 任务内容
- 根据用户最新补充,重新校正 `show-hide-switch-高斯` 的目标语义。
- 重点确认当前实现为什么无法做到:
  - 雷达 hide 完整执行
  - Gaussian 在中段开始 show
  - overlap 阶段两者同屏

### 完成过程
- 重新回读了支线 `task_plan__show_hide_scale_tuning.md`、`notes__show_hide_scale_tuning.md`、`WORKLOG__show_hide_scale_tuning.md`、`ERRORFIX__show_hide_scale_tuning.md`。

## [2026-03-18 19:32:00 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 任务名称: 完成 RadarScan -> Gaussian 的 dual-track overlap 切换

### 任务内容
- 按 OpenSpec change `radarscan-gaussian-dual-track-switch` 完成 runtime、editor 文案、测试与验证。
- 让 `show-hide-switch-高斯` 满足:
  - 雷达 `visibility hide` 完整执行
  - Gaussian 在 hide 过半时开始 show
  - overlap 阶段两者同屏
  - hide 结束后才关闭 `EnableLidarScan`

### 完成过程
- 在 `Runtime/GsplatSequenceRenderer.cs` 镜像补齐 dual-track 状态:
  - `m_pendingRadarToGaussianDisableLidar`
  - 专用 LiDAR hide overlay 的 active/progress/source-mask/lastAdvance
  - overlay 构建优先级、overlap splat 提交门禁、cancel/reset 逻辑
- 在 `Runtime/GsplatRenderer.cs` 和 `Runtime/GsplatSequenceRenderer.cs` 都补了 `ApplyRenderStyleTransition(...)` 内部 helper:
  - 避免 overlap 触发点调用公开 `SetRenderStyle(...)` 时,把刚抓到的专用 hide overlay 一起 cancel 掉
- 更新两个 Inspector help box,把按钮语义写成“双轨 overlap 切换”
- 重写 `Tests/Editor/GsplatVisibilityAnimationTests.cs` 的 switch 相关断言:
  - 半程前只在 hide
  - overlap 阶段 `overlay.mode == 2`
  - overlap 阶段 splat 提交重新放开
  - `EnableLidarScan` 只在专用 hide overlay 完整结束后才关闭

### 验证结果
- 编译通过:
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`
- 定向 EditMode 测试通过:
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelayedFirstTick_DoesNotStartShowBeforeLidarHalfway`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DisablesLidarOnlyAfterDedicatedHideOverlayCompletes`
- 额外回归:
  - 整个 `Gsplat.Tests.Editor` 程序集复跑后,本轮只剩一个与本次改动无关的既有失败:
    - `GsplatSplat4DImporterDeltaV1Tests.ImportV1_StaticSingleFrame4D_RealFixturePlyThroughExporterAndImporter`
    - 失败原因是 Python 环境缺少 `numpy`

### 总结感悟
- overlap 功能最容易踩坑的,不是 easing 本身,而是“公共 API 里的副作用”会不会把中间态清掉。
- 对这类“旧轨未结束,新轨已开始”的功能,最好把“用户入口 API”与“编排内部 helper”拆开,避免互相打架。

## [2026-03-18 20:13:00 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 任务名称: 将 Gaussian show 触发点微调为更紧的衔接

### 任务内容
- 根据用户反馈,把 `show-hide-switch-高斯` 的 Gaussian show 起点再提前一点。
- 保持 dual-track 的其它核心语义不变。

### 完成过程
- 在 `GsplatRenderer` / `GsplatSequenceRenderer` 中把固定 `0.5f` 改成常量:
  - `k_radarToGaussianShowTriggerProgress01 = 0.42f`
- 同步更新了:
  - 注释描述
  - Inspector help box 文案
  - 定向测试里的断言口径

### 验证结果
- 编译通过:
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`
- 定向 EditMode 测试逐个通过:
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelayedFirstTick_DoesNotStartShowBeforeLidarHalfway`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DisablesLidarOnlyAfterDedicatedHideOverlayCompletes`

### 总结感悟
- 这次不是“修错”,更像体验微调。
- 把触发点收敛成单一常量后,后续继续试 `0.40`、`0.44` 这类小步调校会轻松很多。

## [2026-03-18 20:22:00 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 任务名称: 将 dual-track 触发点继续收紧到 0.35

### 任务内容
- 按用户指定值,把 `show-hide-switch-高斯` 的 Gaussian show 触发点继续提前到 `0.35`。

### 完成过程
- 将以下常量统一改成 `0.35f`:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
  - `Tests/Editor/GsplatVisibilityAnimationTests.cs`
- 重新编译并逐个复跑 3 个 dual-track 定向 EditMode 用例。

### 验证结果
- 编译通过:
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`
- 定向 EditMode 测试通过:
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelayedFirstTick_DoesNotStartShowBeforeLidarHalfway`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DisablesLidarOnlyAfterDedicatedHideOverlayCompletes`

### 总结感悟
- 现在这条按钮的 show 接入会更早、更贴近 hide 前沿。
- 但从测试证据看,它仍然保持在“更早衔接”而不是“高斯抢跑”的范围内。
- 逐段检查了:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
  - `Tests/Editor/GsplatVisibilityAnimationTests.cs`
- 静态证据确认了 3 个关键门槛:
  1. `TriggerRadarToGaussianShowSwitchNow()` 一旦启动 Gaussian show,共享显隐状态就不再保留雷达 hide 轨。
  2. `BuildLidarShowHideOverlayForThisFrame(...)` 只消费共享 `m_visibilityState` / `m_visibilityProgress01`,无法让 hide 与 show 并行存在。
  3. `ShouldSubmitSplatsThisFrame()` 会在 `EnableLidarScan && HideSplatsWhenLidarEnabled` 时直接挡掉 Gaussian splat 提交,不适合 overlap 阶段。
- 在此基础上形成了推荐方案:
  - 共享显隐轨继续给 Gaussian show 用
  - 单独补一条 Radar->Gaussian 专用 LiDAR hide overlay 轨
  - 只有在这条独立 hide 轨完成后才关闭 `EnableLidarScan`

### 总结感悟
- 这轮真正被用户纠正的不是参数,而是状态表达模型。
- 当需求变成“出场动画和退场动画要在一段时间里同时成立”时,单一显隐状态机往往天然不够用,应该优先考虑拆轨,而不是继续堆 bypass。

## [2026-03-18 09:49:26 +0800] [Session ID: unknown] 任务名称: 为方案A 建立 OpenSpec change

### 任务内容
- 将 `show-hide-switch-高斯` 的双轨切换方案固化成 OpenSpec change.
- 输出 proposal / design / tasks / spec delta,为后续实现提供约束。

### 完成过程
- 先回读了 `burn-reveal-visibility`、`particle-dots-lidar-scan` 等现有 change 的 proposal / design / tasks / spec 风格。
- 新建 change:
  - `openspec/changes/radarscan-gaussian-dual-track-switch/`
- 产出了以下 artifacts:
  - `proposal.md`
  - `design.md`
  - `tasks.md`
  - `specs/gsplat-radarscan-gaussian-switch/spec.md`
- 其中核心设计口径固定为:
  - 共享 `show` 轨继续给 Gaussian 用
  - 单独补一条 LiDAR hide overlay 轨
  - overlap 阶段放开 Gaussian splat 提交
  - hide 完成后才关闭 LiDAR
- 最后执行 OpenSpec 校验并确认通过。

### 总结感悟
- 这一步最大的价值不是“多写了几份 md”,而是把用户反复纠正的真实语义固定成了可执行契约。
- 后面实现时如果再偏向“半程切过去”的老思路,测试和 spec 都会第一时间把它拦下来。
