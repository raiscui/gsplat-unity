# notes__show_hide_scale_tuning

## 2026-03-15 22:12:00 +0800 证据汇总: show/hide 尺度差异

### 现象

- 用户反馈:
  - `ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest` 的 show/hide 效果正常。
  - `s1_point_cloud_v2_sh3_full_k8192_f32_20260312` 在 show/hide 时会出现更大、更快的球形扩张。

### 静态证据

1. `Runtime/GsplatRenderer.cs`
   - `PushVisibilityUniformsForThisFrame(...)` 会先计算:
     - `centerModel = CalcVisibilityCenterModel(localBounds)`
     - `maxRadius = CalcVisibilityMaxRadius(localBounds, centerModel)`
   - 然后把以下参数直接按 `maxRadius` 缩放:
     - `ringWidth = maxRadius * Show/HideRingWidthNormalized`
     - `trailWidth = maxRadius * Show/HideTrailWidthNormalized`
2. `Runtime/GsplatRenderer.cs`
   - `CalcVisibilityCenterModel(...)`:
     - 若 `VisibilityCenter` 非空,使用该 Transform 的 world position 转成 model space。
     - 否则回退到 `localBounds.center`。
   - `CalcVisibilityMaxRadius(...)`:
     - 使用 bounds 8 个角点到 `centerModel` 的最大距离作为 `maxRadius`。
3. `Runtime/Shaders/Gsplat.shader`
   - reveal/burn 半径使用公式:
     - `radius = EaseInOutQuad(progress) * (maxRadius + trailWidth)`
   - 这意味着世界空间里的扩张速度与 `maxRadius` 成正比。
4. `Runtime/GsplatRenderer.cs`
   - progress 推进逻辑是 `progress += dt / ShowDuration` 或 `dt / HideDuration`。
   - 当两个对象 `ShowDuration` / `HideDuration` 相同,但 `maxRadius` 不同,更大的对象会在同样秒数内跨越更多空间距离。
5. `Runtime/GsplatRenderer.cs`
   - `ResolveVisibilityLocalBoundsForThisFrame()` 直接使用 `GsplatAsset.Bounds` 作为 show/hide 的 local bounds 来源。
6. `Editor/GsplatSplat4DImporter.cs`
   - `.splat4d` 导入时,`GsplatAsset.Bounds` 是根据 record 的 position 包围得到的 bounds。
   - 也就是说 show/hide 半径首先由资产本身的空间范围决定,不是由 glow/noise 参数决定。

### 动态证据

#### 1) 资源本身 bounds 差异

用 Python 直接解析两个 `.splat4d v2` 文件中的 `RECS` section,得到:

- `ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest.splat4d`
  - `size = [10.743664, 8.101825, 11.953199]`
  - `extents = [5.371832, 4.050912, 5.976600]`
  - 若以 `bounds.center` 为中心,`maxRadius = 8.999234`
- `s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d`
  - `size = [289.024170, 793.980225, 478.734924]`
  - `extents = [144.512085, 396.990112, 239.367462]`
  - 若以 `bounds.center` 为中心,`maxRadius = 485.573578`

结论:
- 仅从资产自身局部 bounds 看,`s1` 的 reveal 半径约是 `ckpt` 的 `53.957x`。

#### 2) 场景里的 show/hide 参数基本相同

从 `Assets/OutdoorsScene.unity` 对比两者的场景配置:

- `ShowDuration = 4`
- `HideDuration = 6`
- `WarpStrength = 1.425`
- `NoiseStrength = 0.354`
- `ShowRingWidthNormalized = 0.174`
- `ShowTrailWidthNormalized = 0.05`
- `HideRingWidthNormalized = 0.165`
- `HideTrailWidthNormalized = 0.273`
- `EnableVisibilityAnimation = 1`
- `VisibilityCenter = {fileID: 1538456303}`

结论:
- 两个对象不是因为动画参数不同才产生明显差异。
- 它们吃到的是几乎同一套参数,但作用在不同尺度的 bounds 上。

#### 3) 当前场景共用 `VisibilityCenter`

- 两个对象都引用同一个 `VisibilityCenter` (`3dgsLoc`, fileID `1538456303`)。
- `3dgsLoc` 位于 `3DGS` 父节点下,局部位置约 `[-18.063, 1.113, -17.815]`。
- `s1` 在场景中的 transform override 主要是:
  - `m_LocalPosition = [-22.12, 1.95, -19.95]`
  - `m_LocalScale.y = -1`
- `s1` 没有额外的 uniform 放大缩小,只有 Y 轴镜像翻转。

补充估算:
- 若按当前场景的共享 `VisibilityCenter` 估算 reveal 半径:
  - `ckpt maxRadius ≈ 39.652`
  - `s1 maxRadius ≈ 554.162`
  - 比值约 `13.976x`

说明:
- 共享 `VisibilityCenter` 会继续放大“不同尺寸资产”的差异。
- 即便把共享中心因素算进去,`s1` 的 reveal 半径依然远大于 `ckpt`。

#### 4) 当前参数下的 show 前沿空间速度

使用当前场景参数:
- `ShowDuration = 4`
- `ShowTrailWidthNormalized = 0.05`
- shader 前沿半径近似终值: `maxRadius + trailWidth = maxRadius * 1.05`

估算得到:
- `ckpt`
  - `frontSpeed ≈ 10.409 units/s`
- `s1`
  - `frontSpeed ≈ 145.468 units/s`
- 比值约 `13.976x`

这和用户观察到的“更大、更快的球形扩大开来”一致。

### 当前结论

- 已验证结论:
  1. 这不是单纯的 `ShowDuration/HideDuration` 参数写错。
  2. 这也不是场景里把某个 GameObject 做了明显 uniform scale 放大导致。
  3. 核心原因是: 当前 show/hide 动画半径直接依赖 `GsplatAsset.Bounds` 与 `VisibilityCenter`。而 `s1` 的资产空间范围远大于 `ckpt`,所以在同样时长下 reveal 球体会显得更大、更快。
- 候选次要因素:
  - 共享 `VisibilityCenter` 不是唯一原因,但会进一步拉大不同资产之间的 reveal 差异。

### 可证伪点

- 如果把 `s1` 的 `VisibilityCenter` 清空或换成它自己的局部中心后,球形扩张依然同样夸张,则说明主因仍然是资产 bounds 尺度本身,而不是中心点偏置。
- 如果把 `s1` 整体缩放到与 `ckpt` 接近的世界尺寸后,show/hide 观感明显接近,则进一步支持“世界空间尺度驱动 reveal 效果”的判断。

## 2026-03-15 23:07:00 +0800 运行时包路径验证

### 现象
- Unity 工程根目录 `../../Packages/manifest.json` 中,`wu.yize.gsplat` 依赖项指向 `file:/Users/cuiluming/local_doc/l_dev/my/unity/gsplat-unity`。
- 当前工作目录却是工程内的 `Packages/wu.yize.gsplat`。

### 候选假设
- 假设A: 当前目录只是仓库内副本,而 Unity 实际加载的是 manifest 指向的外部包目录。
- 假设B: 两个路径实际上是同一目录的软链接或等价映射,因此当前改动仍会被 Unity 使用。

### 验证计划
- 比对两个路径的 `realpath`、`stat inode` 与 git 根路径。
- 只有确认它们是同一份目录后,才继续用当前目录对应的代码做 Unity 验证。
- 若不是同一份,后续编译/测试必须切到 Unity 实际使用的包上。

## 2026-03-15 23:12:00 +0800 编译入口进一步验证

### 新问题
- `manifest.json` 指向外部 file package,但工程内同时还存在一份同名 embedded package 目录 `Packages/wu.yize.gsplat`。
- 两份代码差异极大,不能只靠 `manifest` 推断最终编译源。

### 下一步验证点
- 检查 `Packages/packages-lock.json` 中 `wu.yize.gsplat` 的 source 信息。
- 检查生成的 `Gsplat.csproj` / `Gsplat.Tests.Editor.csproj` 实际包含的是哪一份源码路径。

### 目的
- 直接以编译器视角确认当前 Unity 工程到底在用哪份 Gsplat 源码。

## 2026-03-15 23:16:00 +0800 编译失败: 新增工具文件未进入编译单元

### 现象
- `dotnet build ../../Gsplat.Tests.Editor.csproj` 报:
  - `GsplatRenderer.cs`: `CS0246 GsplatVisibilityProgressMode` not found
  - `GsplatSequenceRenderer.cs`: `CS0246 GsplatVisibilityProgressMode` not found

### 当前主假设
- 新增的 `Runtime/GsplatVisibilityAnimationUtil.cs` 尚未被 Unity 生成的 `.csproj` 纳入。

### 备选解释
- 文件可能已存在于磁盘,但缺少 `.meta` 或 Unity 尚未 refresh,导致工程文件未更新。
- 也不排除 asmdef / compile order 级别的问题,但因为该类型与使用点同属 `Gsplat` runtime,当前优先级较低。

### 最小验证
- 检查 `GsplatVisibilityAnimationUtil.cs.meta` 是否存在。
- 检查 `../../Gsplat.csproj` 是否包含该新文件。
- 若缺失,先 refresh Unity 并重新生成工程后再编译。

## 2026-03-15 23:19:00 +0800 refresh 后状态

### 已验证
- Unity refresh 后已自动生成 `Runtime/GsplatVisibilityAnimationUtil.cs.meta`。
- `../../Gsplat.csproj` 已包含 `Packages/wu.yize.gsplat/Runtime/GsplatVisibilityAnimationUtil.cs`。

### 结论
- 刚才的 `CS0246` 并不是实现接口设计错误,而是新脚本尚未进入工程。
- 现在可以继续重跑编译,再看是否还有真正的代码层问题。

## 2026-03-15 23:23:00 +0800 WorldSpeed 首轮实现失败分析

### 现象
- 定向失败用例: `AdvanceVisibilityState_WorldSpeedMode_KeepsRevealFrontDistanceComparableAcrossBoundsScales`
- 失败数据:
  - 期望 reveal 前沿距离约 `1.4188`
  - 实际大 bounds 对象只有约 `0.1505`

### 当前主假设
- 现在的实现把 `worldSpeed` 施加在 `progress01` 上:
  - `progressStep = dt * worldSpeed / totalRange`
- 但 shader 真正用于计算 reveal 半径的是 `EaseInOutQuad(progress01) * totalRange`。
- 这意味着“线性推进 progress”并不等于“线性推进世界半径”。
- 在起始阶段 `EaseInOutQuad(t)` 为二次曲线,大对象因为 `progress` 更小,会被额外压得更慢。

### 证据方向
- 静态证据: 回读 shader 的 reveal 半径公式与 renderer 当前 progress 推进公式。
- 动态证据: 用失败测试的参数直接复算 small/large 两个 bounds 的 `progress` 与 eased radius。

### 如果主假设成立,修正方向
- 不再直接让 `progress01` 线性增长。
- 改为让 `easedProgress = EaseInOutQuad(progress01)` 按 `dt * worldSpeed / totalRange` 线性增长。
- 然后通过 `InverseEaseInOutQuad` 反推出新的 `progress01`,以保持 shader 现有曲线和 ckpt 观感基线不变。

## 2026-03-15 23:31:00 +0800 旧回归用例新失败: hide 中断后 source mask 丢失

### 现象
- 定向用例 `PlayShow_DuringHiding_RestartsShowFromZero` 当前失败。
- 失败并非前沿距离不一致,而是 source mask 语义不符:
  - 期望 `HideSnapshot`
  - 实际 `FullHidden`

### 当前假设
- 需要先确认 `PlayShow()` 被调用时,对象状态是否仍然真的是 `Hiding`。
- 若状态已被其他逻辑提前收束到 `Hidden`,测试期望就会与实际分支不一致。
- 另一种可能是 `PlayShow()` 分支本身没有保留 hide snapshot。

### 下一步
- 静态回读 `PlayShow_DuringHiding_RestartsShowFromZero` 与 `PlayShow()` 实现。

## [2026-03-18 12:33:45 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 笔记: 高斯 -> 雷达 方向里“像突然消失”的第二层排查

## 现象

- 用户最新反馈:
  - 进入雷达形态时,高斯粒子的 alpha 退场“像没有做完整”,视觉上仍然偏突然。
- 已知前情:
  - 上一轮已经修过一层运行时门禁问题:
    - `ShouldDelayHideSplatsForLidarFadeIn()` 不再只看 LiDAR fade-in
    - 也会等 `Gaussian -> ParticleDots` 的 render-style 动画结束
  - 这意味着“splat 提交在 style 动画明显中途就被立刻切掉”这一层,已有测试和代码证据兜住。

## 当前主假设

- 主假设:
  - 现在残留的不自然感,更像是 render-style 自身的 alpha 交接曲线太陡。
  - 也就是:
    1. 几何 morph 还在继续
    2. 但高斯 alpha 的主观可见度在后半段掉得太快
    3. 肉眼会把它感知成“还没演完就没了”

## 最强备选解释

- 备选解释:
  - 运行时仍存在另一条没有覆盖到的提前关断链路。
  - 比如某处不是靠 `ShouldSubmitSplatsThisFrame()` 门禁,而是别的提交条件或 shader 早退导致观感被提前掐掉。

## 静态证据

1. `Runtime/GsplatRenderer.cs`
   - `AdvanceRenderStyleStateIfNeeded()` 里,几何/风格的主 blend 仍然使用:
     - `GsplatUtils.EaseInOutQuart(m_renderStyleAnimProgress01)`
2. `Runtime/Shaders/Gsplat.shader`
   - fragment 最终 alpha 交接目前仍是:
     - `alpha = lerp(alphaGauss, alphaDot, styleBlend);`
   - 也就是说,alpha 交接直接跟着 quart 曲线走,没有单独的“更柔和 alpha 过渡”。
3. 这套结构意味着:
   - 后半段只要 `styleBlend` 很快逼近 1,高斯权重 `1 - styleBlend` 就会非常快地掉光。

## 动态证据

- 用当前 `EaseInOutQuart` 曲线直接计算:
  - 当动画时间走到 `t=0.70` 时:
    - `blend=0.935200`
    - `gaussian_weight=0.064800`
  - 当动画时间走到 `t=0.75` 时:
    - `blend=0.968750`
    - `gaussian_weight=0.031250`
  - 当动画时间走到 `t=0.80` 时:
    - `blend=0.987200`
    - `gaussian_weight=0.012800`
  - 当动画时间走到 `t=0.90` 时:
    - `blend=0.999200`
    - `gaussian_weight=0.000800`
- 解释:
  - 虽然“动画标志位”还显示 render-style 在继续播放,
  - 但对肉眼来说,高斯分量在后半程已经衰减得非常狠了。
  - 这和用户口中的“像没做完就不显示了”是吻合的。

## 当前结论

- 已验证结论:
  - 上一轮修掉的“提交门禁过早关闭”不是这轮唯一问题。
  - 当前还存在一层更细的观感问题:
    - render-style 的 alpha 交接曲线本身过于前倾
- 仍待验证的部分:
  - 是否只放缓 alpha handoff 就足够自然
  - 还是还需要再补额外的关断保护

## 下一步验证计划

1. 保持几何 morph 仍使用现有 quart 节奏
2. 单独给 render-style 的 alpha 交接引入更柔和的曲线
3. 补一个定向测试,锁定“alpha handoff 比几何 blend 更慢”的语义
4. 再编译并跑定向 EditMode 测试,确认没有把既有切换语义打坏

## [2026-03-18 19:30:00 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 笔记: dual-track overlap 被 `SetRenderStyle` 意外清空

## 来源

### 来源1: `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`

- 要点:
  - `TriggerRadarToGaussianShowSwitchNow()` 先 `CaptureRadarToGaussianLidarHideOverlayFromCurrentHideState()`
  - 紧接着又调用公开 API `SetRenderStyle(...)`
  - 而 `SetRenderStyle(...)` 的第一句是 `CancelPendingRadarToGaussianShowSwitch()`

### 来源2: Unity EditMode 动态测试

- 用例:
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway`
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DisablesLidarOnlyAfterDedicatedHideOverlayCompletes`
- 失败现象:
  - overlap 阶段预期 `overlay.mode == 2`
  - 实际却得到 `overlay.mode == 1`

## 综合发现

### 现象

- overlap 阶段一旦开始 Gaussian show,LiDAR overlay 立即退回共享 `Showing` 轨。
- 这会让用户看到:
  - hide burn/noise 语义丢失
  - 专用 hide 轨像没存在过

### 当前假设

- 专用 hide overlay 不是“没抓到”,而是“抓到后立刻又被公共 API 的 cancel 逻辑抹掉”。

### 验证

- 静态证据:
  - `TriggerRadarToGaussianShowSwitchNow()` 的调用顺序就是 `Capture -> SetRenderStyle`
  - `SetRenderStyle(...)` 内部固定先 cancel
- 动态证据:
  - 修复前,2 个 dual-track 用例都稳定报 `overlay.mode == 1`
  - 修复后,3 个 dual-track 相关用例单独运行均通过

### 结论

- overlap 这种“先抓一段中间态,再继续编排”的逻辑,不能直接复用会自动 cancel 当前 switch 的公开 API。
- 更稳的做法是:
  - 保留公开 API 继续承担普通用户入口
  - 但在 overlap 编排内部,拆一个不带 cancel 副作用的内部 helper

## [2026-03-18 20:11:00 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 笔记: 把 Gaussian show 触发点从 0.5 微调到 0.42

## 来源

### 来源1: 用户最新体验反馈

- 用户反馈:
  - 当前 dual-track 已经对了
  - 但希望“衔接再紧密一点”
  - 也就是 Gaussian show 希望再稍微提前

### 来源2: 当前 dual-track 触发实现

- `GsplatRenderer` / `GsplatSequenceRenderer` 当前触发条件都是:
  - `m_visibilityProgress01 >= 0.5f`

## 综合发现

### 处理策略

- 这轮不改整体编排,只改触发阈值。
- 为了避免继续散落魔法数字,把阈值提成常量:
  - `k_radarToGaussianShowTriggerProgress01 = 0.42f`

### 结论

- `0.42` 属于“比过半稍早一点”的档位:
  - 足够让衔接更紧
  - 又不会提前到破坏“先 hide 再 overlap”的体感
- 后续如果还要继续往前或往后调,现在只需要改一处常量即可。

## [2026-03-18 20:21:00 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 笔记: 触发点继续从 0.42 收紧到 0.35

## 来源

### 来源1: 用户最新指定值

- 用户直接给出目标值:
  - `0.35`

## 综合发现

### 处理

- 将以下常量统一从 `0.42f` 改为 `0.35f`:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
  - `Tests/Editor/GsplatVisibilityAnimationTests.cs`

### 动态验证

- 以下 3 个定向 EditMode 用例逐个通过:
  1. `PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway`
  2. `PlayRadarScanToGaussianShowHideSwitch_DelayedFirstTick_DoesNotStartShowBeforeLidarHalfway`
  3. `PlayRadarScanToGaussianShowHideSwitch_DisablesLidarOnlyAfterDedicatedHideOverlayCompletes`

### 当前结论

- `0.35` 这档已经明显比 `0.42` 更早接入。
- 但从现有动态证据看,它还没有破坏:
  - delayed first tick 不抢跑
  - overlap 期间专用 hide overlay 继续存在
  - `EnableLidarScan` 延后到 hide 轨结束后再关闭

## [2026-03-18 20:38:00 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 笔记: Gaussian -> RadarScan 的高斯 alpha 退场要与 splat 提交门禁对齐

## 来源

### 来源1: 用户现场反馈

- 用户反馈:
  - 从高斯粒子形态切到雷达粒子形态时
  - 高斯本来应该有一段 alpha 消失过程
  - 但现在像没做完就突然消失

### 来源2: 新增最小红测

- 用例:
  - `SetRenderStyleAndRadarScan_Animated_KeepsSplatsUntilGaussianAlphaFadeFinishes`
- 构造:
  - `LidarShowDuration = 0.05`
  - `RenderStyleSwitchDurationSeconds = 0.2`
- 修复前失败:
  - `Expected splats to remain submitted while Gaussian alpha fade is still finishing.`
  - `Expected: greater than 0`
  - `But was: 0`

## 综合发现

### 现象

- 入雷达时,高斯 alpha 退场与 LiDAR 淡入不是同一条动画轨。
- 当 LiDAR 淡入更快时,旧门禁会误以为“已经可以停掉 splat 提交”。

### 已验证根因

- `ShouldDelayHideSplatsForLidarFadeIn()` 修复前只考虑:
  - LiDAR fade-in 是否还没完成
- 没考虑:
  - `m_renderStyleAnimating && m_renderStyleAnimTargetBlend01 >= 0.999f`
  - 也就是 Gaussian -> ParticleDots 的 render-style 退场是否还没结束

### 修复结论

- 入雷达阶段延迟隐藏 splats 的条件必须是“任一退场轨未完成”:
  1. LiDAR fade-in 还没完成
  2. Gaussian alpha / render-style 退场还没完成
- 两条条件都结束后,才允许真正停掉 splat 提交。

## [2026-03-18 00:31:11 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 笔记: RadarScan -> Gaussian 新按钮的时序分析

## 来源

### 来源1: `Editor/GsplatRendererEditor.cs` 与 `Editor/GsplatSequenceRendererEditor.cs`

- 要点:
  - 当前 Inspector 已有三个 Render Style 快捷按钮:
    - `Gaussian(动画)`
    - `ParticleDots(动画)`
    - `RadarScan(动画)`
  - `Gaussian(动画)` 目前调用的是 `SetRenderStyleAndRadarScan(GsplatRenderStyle.Gaussian, enableRadarScan: false, animated: true, durationSeconds: -1.0f)`。
  - 这意味着当前“雷达 -> 高斯”是并行切换:
    - LiDAR 可见性开始 fade-out
    - RenderStyle 同时从 `ParticleDots` morph 到 `Gaussian`

### 来源2: `Runtime/GsplatRenderer.cs`

- 要点:
  - `SetRadarScanEnabled(false, animated: true, ...)` 会立刻把 `EnableLidarScan` 设为 `false`,只靠 `m_lidarKeepAliveDuringFadeOut` 让 LiDAR 自己继续淡出。
  - `ShouldSubmitSplatsThisFrame()` 只在 `EnableLidarScan && HideSplatsWhenLidarEnabled` 时阻止 splat sort/draw。
  - 这意味着:
    - 一旦关掉 `EnableLidarScan`,高斯 splat 会重新具备提交资格。
    - 如果不额外控制可见性,高斯会比“过半 show”更早露出来。
  - `SetVisible(false, animated: false)` 可以把 splat 立刻置为 `Hidden`,从根源停掉 sort/draw。
  - `PlayShow()` 可以在后续重新启动高斯的 reveal show 动画。

### 来源3: `Runtime/GsplatSequenceRenderer.cs`

- 要点:
  - 序列后端与静态后端在 `SetRenderStyleAndRadarScan`、`SetRadarScanEnabled`、`SetVisible`、`PlayShow/PlayHide` 上保持同构语义。
  - 因此新按钮若只改 `GsplatRendererEditor` 会造成两个 Inspector 行为不一致。

## 综合发现

### 现象

- 用户要的是一个新按钮,不是替换现有 `Gaussian(动画)`。
- 目标语义也不是“把渐变曲线换一种 easing”,而是把并行切换改成分段编排:
  1. 雷达粒子先 hide
  2. 到切换过程过半
  3. 高斯基元再开始 show

### 当前主假设

- 最稳的实现方式是:
  - 在运行时层新增一个专用切换入口。
  - 按下按钮后先立即把 splat 强制隐藏,避免提前露出。
  - 同时启动 LiDAR fade-out。
  - 到 `雷达 hide 时长的一半` 时,再硬切 `RenderStyle=Gaussian` 并调用 `PlayShow()`。

### 备选解释

- 也可以只在 Editor 按钮层做 `EditorApplication.update` 延迟调度。
- 但那样:
  - 逻辑会散在 Inspector 脚本里,不利于复用和测试。
  - PlayMode / 其他调用方若以后也想要这个切换语义,还得再复制一份。

### 当前结论

- 目前只有静态代码证据,但它已经足够说明:
  - “只串两个现有 API”不能满足新时序。
  - 需要一个能跨半程延迟触发高斯 show 的状态机或调度点。
- 下一步落地方向:
  1. 在 `GsplatRenderer` / `GsplatSequenceRenderer` 增加专用切换 API。
  2. 在 `Update` / Editor ticker 推进链路里检查“半程是否到达”。
  3. 在两个 Inspector 中新增 `show-hide-switch-高斯` 按钮。
  4. 补一个 EditMode 测试,锁定“高斯 show 不会早于半程开始”。

## [2026-03-18 00:51:48 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 笔记: Unity 定向测试收集条件

## 来源

### 来源1: Unity MCP `run_tests` 返回结果

- 要点:
  - 在当前工程里,如果 package 没进 `testables`,`run_tests` 可能会返回:
    - `summary.total = 0`
    - `resultState = Passed`
  - 这不是“测试通过”,而是“根本没有收集到目标测试”。

### 来源2: 临时验证实验

- 要点:
  - 临时把 `wu.yize.gsplat` 加入工程 `testables` 并做 package resolve 之后,包测试才开始真正被 Unity 收集。
  - `run_tests.test_names` 这次必须传完整限定名,例如:
    - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway`
  - 只传短名时,依旧会出现 `total=0` 的“空跑通过”结果。

## 综合发现

### 已验证结论

- 当前 Unity MCP 测试入口下:
  1. 包测试收集依赖 `testables`
  2. 定向过滤依赖完整限定名

### 对本轮任务的意义

- 如果不先识别这个验证环境条件,很容易把 `total=0` 误说成“测试已经通过”。
- 本轮最终采用的验证证据,必须以“完整限定名命中的单测结果”为准。

## [2026-03-18 00:51:48 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 笔记: 新按钮提前触发的候选原因

## 来源

### 来源1: `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`

- 要点:
  - 当前半程触发逻辑使用:
    - `m_pendingRadarToGaussianShowStartRealtime`
    - `Time.realtimeSinceStartup`
    - `elapsed >= delaySeconds`
  - LiDAR hide 本身的推进却发生在:
    - `AdvanceLidarAnimationStateIfNeeded()`
    - 并且它只会在 `Update` / `SubmitDrawForCamera` / Editor ticker tick 时真正推进 `m_lidarVisibilityAnimProgress01`

## 综合发现

### 当前主假设

- 当前实现存在一个“墙钟时间”和“动画状态机时间”脱节的窗口:
  - 如果按钮按下后,第一次真正推进状态机的 tick 已经晚于 `halfDuration`
  - 那么高斯 show 会在 LiDAR hide 还没真正走到一半时就提前开始

### 证伪方式

- 做一个最小动态实验:
  1. 点击新按钮
  2. 故意等到超过 `halfDuration`
  3. 在第一次 `Update` 时检查:
     - `m_lidarVisibilityAnimProgress01` 是否仍接近 0
     - `RenderStyle` / `VisibilityState` 是否已经切到 `Gaussian + Showing`
- 若成立,则说明当前问题不在“用户观感误解”,而在“触发条件绑定错了时间轴”。

## [2026-03-18 01:07:19 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 笔记: 抢跑假设已被动态证据确认

## 来源

### 来源1: 定向测试 `PlayRadarScanToGaussianShowHideSwitch_DelayedFirstTick_DoesNotStartShowBeforeLidarHalfway`

- 要点:
  - 修复前,测试失败:
    - `Expected: ParticleDots`
    - `But was: Gaussian`
  - 复现实验步骤是:
    1. 在 inactive 对象上触发 `PlayRadarScanToGaussianShowHideSwitch()`
    2. 故意让墙钟时间先流过 0.08s
    3. 在第一次 `Update` 才手动推进状态机

### 来源2: 当前实现路径

- 要点:
  - 原实现把 show 触发条件绑定在 arm 后经过的 wall-clock 上。
  - LiDAR hide 动画进度却要等 `AdvanceLidarAnimationStateIfNeeded()` 执行后才会前进。

## 综合发现

### 已验证结论

- 这次问题的直接原因已经确认:
  - 高斯 show 的“半程判定”绑定错了时间轴
  - 应该绑定 LiDAR hide 状态机进度
  - 不应该绑定按钮按下后的墙钟时间

### 修正后的判定原则

- 当前正确语义应为:
  - `m_lidarVisibilityAnimating == true`
  - 并且这是一个 fade-out(`target01 -> 0`)
  - 并且 `m_lidarVisibilityAnimProgress01 >= 0.5`
  - 这时才允许高斯 show 开始
- 再决定这是测试夹具问题、既有脆弱性,还是本次改动引出的行为变化。

## 2026-03-15 23:37:00 +0800 最终结论

### 已验证结论
- 不同尺度 3DGS 的 show/hide 若仍复用同一条 `EaseInOutQuad(progress01)` reveal 曲线,就不能把目标世界速度直接施加在原始 `progress01` 上。
- 正确做法是:
  - 先在 eased reveal 空间推进世界距离。
  - 再反解回 shader 继续消费的 `progress01`。
- 这样可以同时满足:
  1. `ckpt` 当前的观感基线仍可保留。
  2. 更大或更小的 3DGS 在 show/hide 时得到更接近的世界空间扩散形态。
  3. `VisibilityRadiusScale` 仍可作为额外的 reveal 空间范围控制旋钮。

### 当前仍未纳入本轮处理的内容
- 整组 `Gsplat.Tests.Editor` 中与 Python `numpy` 依赖相关的 importer 测试问题。
- 若干既有时间敏感动画测试的稳定性问题。

## 2026-03-16 00:07:00 +0800 hide 预收缩曲线改造

### 变更内容
- 将 `Runtime/Shaders/Gsplat.shader` 中 hide 预收缩阶段使用的 `EaseOutCirc` 改为 `EaseInSine`。
- 修改点仅限于:
  1. easing 函数定义与注释。
  2. `tApproach` 的 easing 调用。

### 设计意图
- 保持 hide 前沿外扩(WorldSpeed / LegacyDuration)逻辑不变。
- 只调整粒子在前沿到来前的预收缩节奏:
  - 从“前面缩得更快”改为“前面更克制,靠近前沿时再更明显缩小”。

## 2026-03-16 00:17:00 +0800 hide 尾部继续烧尽曲线统一

### 变更内容
- 将 hide 阶段的 `passedForFade = passed * passed` 改为 `passedForFade = EaseInSine(passed)`。
- `passedForTail` 继续与 `passedForFade` 保持一致。

### 设计意图
- 统一 hide 两段缩放语言:
  1. 前沿到来前的预收缩: `EaseInSine`
  2. 前沿扫过后的继续烧尽: `EaseInSine`
- 这样前后不再是“两种不同的 ease-in 逻辑”拼起来的感觉。

## [2026-03-18 01:18:50 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 笔记: 雷达粒子被一按即杀的第二轮静态证据

## 来源

### 来源1: `Runtime/GsplatRenderer.cs` 与 `Runtime/GsplatSequenceRenderer.cs`

- 要点:
  - 当前 `PlayRadarScanToGaussianShowHideSwitch(...)` 起点仍调用了 `SetVisible(false, animated: false)`
  - 它不是“只隐藏高斯”,而是把共享显隐状态直接打成 `Hidden`
  - `BuildLidarShowHideOverlayForThisFrame(...)` 在 `m_visibilityState == Hidden` 时会把 `gate = 0.0f`
  - 这意味着 LiDAR 也会被同一套 overlay 直接裁没

### 来源2: 当前测试 `PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway`

- 要点:
  - 现有断言把 `EnableLidarScan` 立即变成 `false` 当成了正确行为
  - 这与用户刚刚补充的真实目标相矛盾:
    - “不能点了按钮就立即 EnableLidarScan 关闭”

## 综合发现

### 当前结论

- 当前按钮问题不是“半程阈值还要再调”
- 而是编排语义本身仍然错误:
  1. 共享显隐状态被过早压成 `Hidden`
  2. `EnableLidarScan` 被过早写成 `false`
- 下一轮修正必须变成真正的两阶段:
  1. 起点只启动雷达 hide
  2. 半程时再启动 Gaussian show
  3. LiDAR 在重叠阶段继续靠自己的 fade-out 退场

## [2026-03-18 01:25:55 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 笔记: 第二轮修复后的实现落点与验证边界

## 来源

### 来源1: 当前运行时代码

- 要点:
  - `PlayRadarScanToGaussianShowHideSwitch(...)` 入口不再调用 `SetVisible(false, animated: false)`
  - 改成只启动 LiDAR 自己的 hide,直到半程才真正切换到 Gaussian show
  - `TriggerRadarToGaussianShowSwitchNow()` 里才会把 `EnableLidarScan` 置为 `false`
  - `BuildLidarShowHideOverlayForThisFrame(...)` 在 Radar -> Gaussian 重叠期会直接 bypass 共享 overlay
  - `TickLidarRangeImageIfNeeded()` 改成认 `IsLidarRuntimeActive()`,不再只认 `EnableLidarScan`

### 来源2: 本轮验证

- 要点:
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过
  - Unity MCP `run_tests` 返回:
    - `summary.total = 0`
    - `resultState = Passed`
  - 这不是有效通过,而是“空跑”

## 综合发现

### 已确认事实

- 这轮代码语义已经从“按钮一按就硬关”改成“真正两阶段编排”
- 但 Unity 动态测试环境当前存在装载路径歧义:
  - `manifest.json` 中 `wu.yize.gsplat` 指向的是外部 `file:` 包路径
  - 因此 Test Runner 很可能没有加载当前工作区里的这份修改

### 对后续验证的影响

- 当前可以确认:
  - 代码能编译
  - 测试断言已经与用户真实目标重新对齐
- 当前还不能确认:
  - Unity 当前实际运行的包副本,是否就是这份工作区代码
  - 用户现场 GameView 的最终观感是否已完全吻合预期

## [2026-03-18 01:28:55 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 笔记: “跑错包副本”假设已被本轮静态证据推翻

## 来源

### 来源1: `../../Gsplat.csproj` 与 `../../Gsplat.Tests.Editor.csproj`

- 要点:
  - `Gsplat.csproj` 直接编译 `Packages/wu.yize.gsplat/Runtime/...`
  - `Gsplat.Tests.Editor.csproj` 直接编译 `Packages/wu.yize.gsplat/Tests/Editor/...`

### 来源2: 目录 inode 对比

- 要点:
  - 当前工作目录与 `../../Packages/wu.yize.gsplat` 的 inode 一致
  - 外部 `/Users/cuiluming/local_doc/l_dev/my/unity/gsplat-unity` 是另一份不同目录

## 综合发现

### 已验证结论

- 本轮不能再把 Unity MCP `run_tests` 的空跑结果解释成“当前工作区不是实际生效代码”
- 当前工作区代码就是 `.csproj` 的编译目标
- 因此这次 `summary.total = 0` 更像是:
  - 包测试收集链路没有真正命中
  - 或 Unity MCP 的 `run_tests` 对包测试仍有额外限制

### 口径回滚

- 上一条笔记里“当前还不能确认 Unity 当前实际运行的包副本,是否就是这份工作区代码”这句,在本轮静态证据下不再成立
- 修正后应写成:
  - 当前工作区就是工程编译目标
  - 但 Unity MCP 的动态测试收集仍然没有形成有效运行证据

## [2026-03-18 01:38:45 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 笔记: 雷达前半段缺少 noise 燃烧效果的静态判断

## 来源

### 来源1: 当前 `PlayRadarScanToGaussianShowHideSwitch(...)`

- 要点:
  - 当前前半段只调用了 `BeginRadarHideForRadarToGaussianShowSwitch(...)`
  - 它只驱动 LiDAR 自己的可见度淡出
  - 没有进入 `PlayHide()` 这条共享显隐状态机

### 来源2: `PlayHide()` 与 `BuildLidarShowHideOverlayForThisFrame(...)`

- 要点:
  - `PlayHide()` 会把 `m_visibilityState` 置为 `Hiding`
  - `BuildLidarShowHideOverlayForThisFrame(...)` 在 `Hiding` 时输出:
    - `mode = 2`
    - `progress = m_visibilityProgress01`
    - 对应 hide 的 radial/noise/glow 处理

## 综合发现

### 当前主判断

- 用户这次说“雷达 hide 要执行 visibility hide 按钮的处理过程”,在代码层面几乎就是指:
  - 前半段必须真实进入 `PlayHide()` 驱动的 `Hiding`
- 上一轮为了避免雷达被立刻裁掉,把共享 hide 整段绕开了
- 这样虽然避免了“秒没”,但也把真正的 noise 燃烧 hide 语言一起绕掉了

## [2026-03-18 01:43:40 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] 笔记: 补回雷达前半段 visibility hide 后的结论

## 来源

### 来源1: 修正后的 `PlayRadarScanToGaussianShowHideSwitch(...)`

- 要点:
  - 先执行 `BeginRadarHideForRadarToGaussianShowSwitch(...)`
  - 若雷达 hide 真正启动成功,则补走 `PlayHide()`
  - 最后再 `ArmRadarToGaussianShowSwitch()`

### 来源2: 修正后的回归断言

- 要点:
  - 前半段状态改为验证 `GetVisibilityStateName(r) == "Hiding"`
  - 同时验证 `BuildLidarShowHideOverlay(...).mode == 2`

## 综合发现

### 已验证结论

- 这轮用户要的“noise 燃烧 hide 过程”,本质上就是让前半段重新进入共享 `visibility hide` 状态机
- 正确顺序不是:
  - 先 arm,再调用会取消 pending 的 API
- 而是:
  1. 先启动 LiDAR hide
  2. 再启动 `PlayHide()`
  3. 最后重新 arm 半程切换
- 这样既能保留前半段的 noise 燃烧效果
- 也不会把“半程后切 Gaussian show”的编排弄丢

## [2026-03-18 09:37:07 +0800] [Session ID: unknown] 笔记: 双轨并行需求下,现有单轨状态机为什么不够用

## 来源

### 来源1: `Runtime/GsplatRenderer.cs` 的半程触发链路

- 要点:
  - `PlayRadarScanToGaussianShowHideSwitch(...)` 先启动 LiDAR hide,再补走 `PlayHide()`
  - `AdvancePendingRadarToGaussianShowSwitchIfNeeded()` 在 LiDAR fade-out 过半时调用 `TriggerRadarToGaussianShowSwitchNow()`
  - `TriggerRadarToGaussianShowSwitchNow()` 会:
    - `EnableLidarScan = false`
    - `SetRenderStyle(Gaussian, animated: false, ...)`
    - `BeginGaussianShowFromHiddenForRadarSwitch()`

### 来源2: `BuildLidarShowHideOverlayForThisFrame(...)`

- 要点:
  - 当前 LiDAR overlay 只读取共享显隐状态:
    - `m_visibilityState`
    - `m_visibilityProgress01`
    - `m_visibilitySourceMaskMode`
    - `m_visibilitySourceMaskProgress01`
  - 当共享状态变成 `Showing` 时,LiDAR overlay 也只能看到高斯 show 这条轨
  - 现有 `ShouldBypassVisibilityOverlayForRadarToGaussianOverlap()` 只能选择“忽略 overlay”
  - 它不能让“上一条 hide 轨”继续独立推进

### 来源3: `ShouldSubmitSplatsThisFrame()`

- 要点:
  - 当前门禁有一条硬条件:
    - `EnableLidarScan && HideSplatsWhenLidarEnabled && !ShouldDelayHideSplatsForLidarFadeIn() => return false`
  - 这意味着:
    - 只要雷达还保持启用
    - 且处在纯 Radar 语义
    - 高斯 splat 的 sort/draw 就会被整体挡住
  - 因此若用户要“hide 跑完整,show 在中段启动”,overlap 阶段必须额外放行高斯 splat

## 综合发现

### 现象

- 用户要的时间线是两条并行轨:
  - 上轨: 雷达粒子的 `visibility hide` 完整跑完
  - 下轨: Gaussian show 在上轨过半时启动
- 中段必须同时看到:
  - 雷达粒子还在做 hide
  - 高斯已经开始 show

### 当前主假设

- 现有实现的问题已经不是“半程判定取 0.5 还是 0.6”
- 真正的问题是:
  - 当前只有一套共享显隐轨
  - 无法同时承载“LiDAR hide 继续推进”和“Gaussian show 已经开始”
- 因此需要最小双轨方案:
  1. 共享 `m_visibilityState` 继续给 Gaussian show 用
  2. 再给 Radar->Gaussian overlap 增加一条独立的 LiDAR hide overlay 轨
  3. `EnableLidarScan` 延后到独立 hide 轨真正结束后再关

### 最强备选解释

- 继续沿用单轨状态机,靠更多 bypass 和延后关开关,也许能做出“看起来像重叠”的短期效果
- 但这条路的上限很明显:
  - hide 轨一旦被 `Showing` 覆盖,就不再是真正的“完整执行”
  - 后面很容易继续出现:
    - 粒子中途断掉
    - 高斯虽然 show 了,但 splat 实际没提交
    - 某些帧 overlay 参数突然跳回默认值

### 当前结论

- 现在已经有足够静态证据支持这个判断:
  - “单轨状态机表达不了用户最新的双轨语义”
- 但这仍然只是设计结论
- 还缺的动态证据是:
  - 新测试去锁定“hide 完整跑完前,`EnableLidarScan` 不得关闭”
  - 新测试去锁定 overlap 阶段 `Gaussian + 雷达粒子` 的并存门禁

## [2026-03-18 09:49:26 +0800] [Session ID: unknown] 笔记: 方案A 对应的 OpenSpec change 已成型

## 来源

### 来源1: 新建 change `radarscan-gaussian-dual-track-switch`

- 要点:
  - change 名称没有直接绑定 Inspector 中文按钮名
  - 采用语义化命名,便于后续把实现从单个按钮推广到同类切换机制

### 来源2: proposal / design / tasks / spec delta

- 要点:
  - proposal 把问题固定为“单轨状态机表达不了 overlap 切换”
  - design 明确采用“共享 show 轨 + 独立 LiDAR hide overlay 轨”的最小双轨方案
  - spec 新增 capability:
    - `gsplat-radarscan-gaussian-switch`
  - tasks 把实施拆成:
    - runtime 状态
    - orchestration / gating
    - editor 文案
    - tests / verification

### 来源3: OpenSpec 校验

- 要点:
  - `openspec validate radarscan-gaussian-dual-track-switch`
  - 结果为 valid
  - `openspec status --change ...` 显示 4/4 artifacts complete

## 综合发现

### 已验证结论

- 方案A 现在不再只是口头计划,已经被固化成可执行的 OpenSpec change
- 后续实现时,最重要的约束已经写死:
  - 不是“半程切过去”
  - 而是“hide 完整跑完,show 中段启动,中间同屏”
