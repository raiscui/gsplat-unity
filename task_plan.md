# 任务计划: 2026-03-12 continuous-learning 续档与知识沉淀

## 目标

- 完成六文件检索总结,把可复用知识沉淀到合适载体,并完成超长上下文文件续档。

## 阶段

- [x] 阶段1: 检索默认组六文件与支线上下文集
- [x] 阶段2: 阅读并汇总最近任务的关键事实
- [ ] 阶段3: 同步长期载体(`AGENTS.md` / `README.md` / `CHANGELOG.md` / skill)
- [ ] 阶段4: 收尾记录与结果核对

## 关键问题

1. 哪条知识最适合跨项目复用,值得抽成 `self-learning.*` skill?
2. 哪些仓库文档已经落后于当前实现,必须同步?
3. 哪些上下文文件已经超过 1000 行,需要在本轮完成续档?

## 做出的决定

- 先完成六文件事实归纳,再做文档与 skill 分流,避免把一时印象写成长期规则。
- 对超过 1000 行的活跃上下文文件先续档,再把本轮总结落到新的当前文件。

## 状态

**目前在阶段3**
- 已完成六文件分组阅读与外部资料补证。
- 正在同步 `AGENTS.md` / `README.md` / `CHANGELOG.md` 和新的 `self-learning.*` skill。

## 2026-03-12 23:40:00 +0800 阶段进展

- [x] 阶段3: 同步长期载体(`AGENTS.md` / `README.md` / `CHANGELOG.md` / skill)
  - 已更新 `AGENTS.md`
  - 已同步 `README.md`
  - 已同步 `CHANGELOG.md`
  - 已新增 `~/.codex/skills/self-learning.unity-scriptedimporter-version-bump/SKILL.md`
- [x] 阶段4: 收尾记录与结果核对
  - 已完成六文件摘要
  - 已完成超长上下文续档与归档
  - 已回查是否需要新增 `EPIPHANY_LOG.md`,当前结论为不需要

## 状态

**目前已完成阶段4**
- 本轮 continuous-learning 已完成。
- 后续如果继续某条业务线,应直接基于新的当前文件继续追加,不要再回到已归档的大文件里写入。

## 2026-03-15 21:40:00 +0800 新任务: 修复 GsplatLidarScanTests 过时 API 警告与 Inspector MissingReferenceException

## 目标

- 消除 `GsplatLidarScanTests.cs` 中对已废弃 `LidarExternalTargets` 的使用警告。
- 定位并修复测试运行后触发的 `GameObjectInspector` / `TransformInspector` 空引用异常。
- 用可复现的测试或命令验证修复,避免只改表面现象。

## 阶段

- [ ] 阶段1: 读取相关代码并复现问题
- [ ] 阶段2: 建立现象 -> 假设 -> 验证计划
- [ ] 阶段3: 实施修复并补齐必要测试
- [ ] 阶段4: 运行验证并收尾记录

## 关键问题

1. 过时 API 警告只是测试调用点没跟上新接口,还是还有兼容层行为需要一起核对?
2. Inspector 异常是测试销毁顺序问题,还是 Selection/EditorWindow/serialized target 残留导致?
3. 能否用单个或少量定向测试稳定复现并证明修复有效?

## 做出的决定

- 先按 systematic-debugging 方式做根因调查,不直接改代码。
- 先锁定最小复现路径,再统一处理 warning 和异常,避免补丁式清理。

## 状态

**目前在阶段1**
- 正在读取 `GsplatLidarScanTests.cs` 与相关 runtime/editor 代码。
- 下一步先用结构化搜索确认过时 API 调用点与潜在 Selection 残留路径。

## 2026-03-15 21:45:00 +0800 支线索引: __show_hide_scale_tuning

- 启用原因: 用户反馈两个 3DGS 资产在 show/hide 动画上的球形扩张尺度和速度明显不一致,与当前主线的 Inspector 修复无关,需独立分析。
- 支线主题: 分析 `ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest` 与 `s1_point_cloud_v2_sh3_full_k8192_f32_20260312` 的 show/hide 动画差异,判断是否由整体尺度、包围体或动画半径计算方式导致,并给出调校建议。
- 对应上下文集:
  - `task_plan__show_hide_scale_tuning.md`
  - `notes__show_hide_scale_tuning.md`
  - `WORKLOG__show_hide_scale_tuning.md`
  - `LATER_PLANS__show_hide_scale_tuning.md`
  - `EPIPHANY_LOG__show_hide_scale_tuning.md`
  - `ERRORFIX__show_hide_scale_tuning.md`

## [2026-03-16 21:54:40] [Session ID: 99992] 支线索引: __lidar_edge_aliasing

- 启用原因: 用户在提高 `LidarAzimuthBins` 与 `LidarBeamCount` 后,观察到 RadarScan/LiDAR 扫描出来的 mesh 轮廓仍然呈现巨大锯齿,需要独立判断这是采样离散化、深度重建方式,还是渲染/精度优化路径造成的现象。
- 支线主题: 追踪 `LidarAzimuthBins`、`LidarBeamCount` 与扫描结果 mesh 边缘形成过程,按"现象 -> 假设 -> 验证计划 -> 结论"给出原因解释,必要时提出后续改进方向。
- 对应上下文集:
  - `task_plan__lidar_edge_aliasing.md`
  - `notes__lidar_edge_aliasing.md`
  - `WORKLOG__lidar_edge_aliasing.md`
  - `LATER_PLANS__lidar_edge_aliasing.md`
  - `EPIPHANY_LOG__lidar_edge_aliasing.md`
  - `ERRORFIX__lidar_edge_aliasing.md`

## [2026-03-17 22:53:05] [Session ID: lidar_scaled_surface_offset_20260317_225305] 支线索引: __lidar_scaled_surface_offset

- 启用原因: 用户反馈 RadarScan/LiDAR 效果在“被缩放过的高斯组件”上,`LidarExternalStaticTargets` 生成的外部目标点云会偏离 mesh 表面。该问题与既有 `__lidar_edge_aliasing` 的锯齿分析不同,需要独立追踪缩放矩阵在 LiDAR 传感器坐标系中的传播方式。
- 支线主题: 追踪缩放后的 `GsplatRenderer` / `GsplatSequenceRenderer` 在 LiDAR range image、external target 命中与最终点云重建中的矩阵链路,验证是否存在“传感器缩放污染 LiDAR 坐标系,导致 external hit 被按缩放倍数推远”的问题,并完成修复与回归测试。
- 对应上下文集:
  - `task_plan__lidar_scaled_surface_offset.md`
  - `notes__lidar_scaled_surface_offset.md`
  - `WORKLOG__lidar_scaled_surface_offset.md`
  - `LATER_PLANS__lidar_scaled_surface_offset.md`
  - `EPIPHANY_LOG__lidar_scaled_surface_offset.md`
  - `ERRORFIX__lidar_scaled_surface_offset.md`
## [2026-03-18 14:00:00 +0800] [Session ID: a5122445-83f8-4367-a55f-188f1411a83d] 支线索引: __radar_scan_toggle

- 启用原因: 用户希望增加一个开关,可以关闭 RadarScan 的扫描动作效果,但继续保留雷达粒子显示。该需求与当前主线历史任务不同,为了避免混写,启用独立支线上下文集。
- 支线主题: 追踪 RadarScan 中“扫描动作效果”和“粒子呈现”的职责边界,实现一个只关闭扫描动作、但不影响粒子可见性的配置开关,并完成必要验证。
- 对应上下文集:
  - `task_plan__radar_scan_toggle.md`
  - `notes__radar_scan_toggle.md`
  - `WORKLOG__radar_scan_toggle.md`
  - `LATER_PLANS__radar_scan_toggle.md`
  - `EPIPHANY_LOG__radar_scan_toggle.md`
  - `ERRORFIX__radar_scan_toggle.md`

## [2026-03-18 15:41:23 +0800] [Session ID: 019cffe2-155b-7932-8e5a-744b6ecdc177] 支线索引: __gaussian_radarscan_dual_track_switch

- 启用原因: 用户要求为现有 `show-hide-switch-高斯` 增加一个反向按钮 `show-hide-switch-雷达`,语义是“高斯先 hide 到 0.35,同时开始雷达 show”。该任务属于新的 OpenSpec 建档工作,和当前主线/其它支线不是同一条任务链,需要独立记录。
- 支线主题: 为 Gaussian -> RadarScan 的反向 dual-track 切换创建独立 OpenSpec change,明确命名、流程、首个 artifact 模板,并避免与既有 `radarscan-gaussian-dual-track-switch` 混淆。
- 对应上下文集:
  - `task_plan__gaussian_radarscan_dual_track_switch.md`
  - `notes__gaussian_radarscan_dual_track_switch.md`
  - `WORKLOG__gaussian_radarscan_dual_track_switch.md`
  - `LATER_PLANS__gaussian_radarscan_dual_track_switch.md`
  - `EPIPHANY_LOG__gaussian_radarscan_dual_track_switch.md`
  - `ERRORFIX__gaussian_radarscan_dual_track_switch.md`

## [2026-03-23 16:37:02 +0800] [Session ID: 20260323_6] 支线索引: __radar_scan_jitter_size

- 启用原因: 用户反馈 RadarScan 粒子在增加位置抖动后,密度调高时出现波纹,并且粒子大小小于 `1` 时显示异常。该问题与既有按钮切换、扫描开关支线不同,需要独立追踪点位扰动、屏幕空间粒径和 shader 覆盖率之间的关系。
- 支线主题: 按“现象 -> 假设 -> 验证计划 -> 结论”追踪 RadarScan 粒子抖动与小粒径渲染异常,修复高密度波纹和 `size < 1` 的显示问题,并补齐必要验证。
- 对应上下文集:
  - `task_plan__radar_scan_jitter_size.md`
  - `notes__radar_scan_jitter_size.md`
  - `WORKLOG__radar_scan_jitter_size.md`
  - `LATER_PLANS__radar_scan_jitter_size.md`
  - `EPIPHANY_LOG__radar_scan_jitter_size.md`
  - `ERRORFIX__radar_scan_jitter_size.md`

## [2026-03-24 14:47:10 +0800] [Session ID: 20260324_21] 支线索引: __sog4d_updatecount_bad

- 启用原因: 用户反馈 `.sog4d` 导入时出现 `delta-v1 invalid updateCount` 报错,希望先做一个简单可用的处理。该问题与现有主线和其它支线不同,需要独立追踪这是测试数据故意失败、导入器校验过严,还是应该增加兼容策略。
- 支线主题: 按“现象 -> 假设 -> 验证计划 -> 结论”追踪 `GsplatSog4DImporter` 的 `updateCount` 溢出报错链路,判断是否应放宽、截断或保留失败,并在必要时补齐测试与记录。
- 对应上下文集:
  - `task_plan__sog4d_updatecount_bad.md`
  - `notes__sog4d_updatecount_bad.md`
  - `WORKLOG__sog4d_updatecount_bad.md`
  - `LATER_PLANS__sog4d_updatecount_bad.md`
  - `EPIPHANY_LOG__sog4d_updatecount_bad.md`
  - `ERRORFIX__sog4d_updatecount_bad.md`

## [2026-03-24 16:05:00] [Session ID: 20260324_27] 支线索引: __continuous_learning_20260324

- 启用原因: 用户明确触发 `continuous-learning` skill。该任务需要系统回读默认组六文件与多条历史支线,并执行摘要、归档、经验沉淀与索引更新。为了避免把持续学习过程混写进 2026-03-15 尚未完成的主线任务,启用独立支线上下文集。
- 支线主题: 按 `continuous-learning` 流程完成六文件检索总结、活跃度判定、归档、经验分流、`AGENTS.md` 索引检查,并给出当前项目后续建议。
- 对应上下文集:
  - `task_plan__continuous_learning_20260324.md`
  - `notes__continuous_learning_20260324.md`
  - `WORKLOG__continuous_learning_20260324.md`
  - `LATER_PLANS__continuous_learning_20260324.md`
  - `EPIPHANY_LOG__continuous_learning_20260324.md`
  - `ERRORFIX__continuous_learning_20260324.md`

## [2026-03-28 12:48:52] [Session ID: 019d32c5-3334-71e2-84bc-b7a60390dc20] 支线索引: __lidar_distance_color_panel

- 启用原因: 用户希望把雷达点云按距离着色的开始颜色和结束颜色直接暴露到 Inspector 面板中可编辑。该任务同时涉及运行时序列化字段、LiDAR draw 参数传递、shader 颜色映射,与当前默认主线历史任务不同,需要独立记录。
- 支线主题: 按“现象 -> 假设 -> 验证计划 -> 结论”追踪当前 RadarScan Depth 颜色映射的硬编码位置,把距离颜色起止色接入 `GsplatRenderer` / `GsplatSequenceRenderer` 与对应自定义 Inspector,并完成必要验证。
- 对应上下文集:
  - `task_plan__lidar_distance_color_panel.md`
  - `notes__lidar_distance_color_panel.md`
  - `WORKLOG__lidar_distance_color_panel.md`
  - `LATER_PLANS__lidar_distance_color_panel.md`
  - `EPIPHANY_LOG__lidar_distance_color_panel.md`
  - `ERRORFIX__lidar_distance_color_panel.md`
