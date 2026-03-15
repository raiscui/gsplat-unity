# LATER_PLANS__show_hide_scale_tuning

## 2026-03-15 22:15:00 +0800 候选改进: 显隐动画半径脱钩资产 bounds

- 背景:
  - 当前 `GsplatRenderer` 的 show/hide 动画半径直接取 `CalcVisibilityMaxRadius(localBounds, centerModel)`。
  - 对不同世界尺度的 3DGS,即便动画参数完全相同,也会出现 reveal 球体大小和世界空间扩张速度差异极大的情况。
- 候选方向:
  1. 增加 `VisibilityRadiusScale` 或 `VisibilityMaxRadiusOverride`。
  2. 增加“按目标世界空间前沿速度”驱动的 reveal 模式,而不是只按 `progress01` 线性扫完整个 bounds。
  3. 增加 `UseBoundsCenterWhenVisibilityCenterMissing/Off` 之外的 artist-friendly 选项,支持每个资产单独配置 reveal anchor。
- 价值:
  - 可以让不同尺度的 3DGS 复用更一致的显隐动画语言,不用靠极端 duration 补偿。

## 2026-03-15 23:35:00 +0800 候选后续: 稳定化既有 EditMode 测试环境

- 在本轮定向验证之外,整组 `Gsplat.Tests.Editor` 仍暴露出几类与当前任务无直接耦合的问题:
  1. `GsplatSplat4DImporterDeltaV1Tests.ImportV1_StaticSingleFrame4D_RealFixturePlyThroughExporterAndImporter` 依赖 python `numpy`,当前环境缺失导致失败。
  2. `GsplatVisibilityAnimationTests` 中若干旧动画测试存在时间敏感抖动,单次跑可能因 `Time.realtimeSinceStartup` / `yield` 时序波动失败,重跑后恢复。
- 若后续要把包级测试恢复到稳定全绿,建议单开支线处理:
  - 为 Python exporter fixture 补环境依赖检查或测试内更明确的失败说明。
  - 将时间敏感的 visibility / render style / lidar blend 测试改造成更确定的手动时间推进夹具。
