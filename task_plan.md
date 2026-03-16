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
