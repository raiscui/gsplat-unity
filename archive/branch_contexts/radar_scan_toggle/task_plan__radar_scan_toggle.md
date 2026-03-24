# 任务计划: RadarScan 扫描动作开关

## 目标

- 增加一个可配置开关,关闭 RadarScan 的扫描动作效果时,仍然保留雷达粒子显示。

## 阶段

- [x] 阶段1: 计划和设置
- [ ] 阶段2: 研究/收集信息
- [ ] 阶段3: 执行/构建
- [ ] 阶段4: 审查和交付

## 关键问题

1. 当前“扫描动作效果”具体由 shader 参数、CPU 动画状态,还是 show/hide 逻辑驱动?
2. 粒子可见性与扫描动作是否已经耦合在同一开关上?
3. 应该把开关放在现有 RadarScan 配置里,还是挂到渲染器/材质路径上更稳定?

## 做出的决定

- 先建立现象 -> 假设 -> 验证计划,再改代码,避免把粒子和扫描动作一起关掉。
- 优先复用现有 RadarScan 配置入口,只有在现有结构无法清晰表达时,才考虑新增更深层参数。

## 遇到错误

- 暂无。

## 状态

**目前已完成阶段4**
- `LidarEnableScanMotion` 已落地到运行时、shader、Inspector、测试和文档。
- 已拿到定向测试通过证据,并记录了与本次改动无关的现存整包失败项。

## [2026-03-18 14:00:00 +0800] [Session ID: a5122445-83f8-4367-a55f-188f1411a83d] 行动记录: 启动代码定位

- 目的: 先确认“扫描动作效果”和“雷达粒子”在实现上是否分层,避免后续错误地在单一布尔值上做硬切。
- 当前行动:
  - 搜索 `RadarScan`、`scan`、`particle`、`show/hide` 相关实现与测试
  - 追踪 Inspector 暴露项、运行时状态字段、shader uniform 的连线关系

## [2026-03-18 14:18:00] [Session ID: a5122445-83f8-4367-a55f-188f1411a83d] 研究结论: 锁定扫描动作层

- [x] 阶段2: 研究/收集信息
  - 已确认持续扫描动作来自 `GsplatLidarPassCore.hlsl` 中 `_LidarTime * _LidarRotationHz` 推进的扫描头位置,以及 `trail01` 对亮度的调制。
  - 已确认 `EnableLidarScan` 控制的是 LiDAR runtime 本体,不能直接拿来关闭扫描动作。
- [ ] 阶段3: 执行/构建
  - 为 `GsplatRenderer` / `GsplatSequenceRenderer` 增加 `LidarEnableScanMotion`
  - 打通 C# -> `GsplatLidarScan` -> shader -> Inspector -> tests -> docs

## [2026-03-18 14:46:00] [Session ID: a5122445-83f8-4367-a55f-188f1411a83d] 收尾记录: 实现与验证完成

- [x] 阶段3: 执行/构建
  - 已新增 `LidarEnableScanMotion`,默认 `true`
  - 已打通运行时 -> shader -> Inspector -> README / CHANGELOG
- [x] 阶段4: 审查和交付
  - 已通过定向 EditMode 测试:
    - `Gsplat.Tests.GsplatLidarShaderPropertyTests.LidarShader_ContainsShowHideOverlayProperties`
    - `Gsplat.Tests.GsplatLidarShaderPropertyTests.LidarShader_UsesAnalyticCoverageAndExternalHitCompetition`
    - `Gsplat.Tests.GsplatLidarShaderPropertyTests.LidarAlphaToCoverageShader_DeclaresAlphaToMaskOn`
    - `Gsplat.Tests.GsplatLidarScanTests.NewGsplatRenderer_DefaultsLidarEnableScanMotionToTrue`
    - `Gsplat.Tests.GsplatLidarScanTests.NewGsplatSequenceRenderer_DefaultsLidarEnableScanMotionToTrue`
  - 已记录整包 `Gsplat.Tests.Editor` 的现存失败,当前证据显示它们与本次改动无关
