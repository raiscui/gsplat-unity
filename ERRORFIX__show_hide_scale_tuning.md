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
