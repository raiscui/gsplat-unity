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
