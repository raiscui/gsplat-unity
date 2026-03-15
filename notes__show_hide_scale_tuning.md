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
