# ARCHIVE MANIFEST: 2026-03-24 continuous-learning

## [2026-03-24 16:25:00] [Session ID: 20260324_27] 归档批次说明

### 归档原因

- 本轮按 `continuous-learning` 流程回读了默认组六文件与全部支线组。
- 其中多条无日期后缀支线已经完成实现或形成明确结论,但仍滞留在根目录。
- 同时,旧 `archive/` 仍是平铺结构,不利于后续检索。
- 另有活跃支线 `__radar_scan_jitter_size` 的 `notes` 已超过 1000 行,需要续档。

### 本批次动作

- 将旧的平铺 `archive/*.md` 历史文件迁入:
  - `archive/default_history/`
  - `archive/branch_contexts/<topic>/snapshots/<timestamp>/`
- 将以下未轮转旧支线整组迁入 `archive/branch_contexts/<topic>/`:
  - `imgui_layout_error`
  - `sog4d_display_issue`
  - `splat4d_edge_opacity`
  - `splat4d_single_frame_support`
  - `show_hide_scale_tuning`
  - `radar_scan_toggle`
  - `lidar_edge_aliasing`
  - `lidar_scaled_surface_offset`
  - `gaussian_radarscan_dual_track_switch`
- 将活跃支线 `__radar_scan_jitter_size` 的旧 notes 续档到:
  - `archive/branch_contexts/radar_scan_jitter_size/snapshots/2026-03-24_162500/`
- 保留根目录活跃上下文:
  - 默认组
  - `__radar_scan_jitter_size`
  - `__sog4d_updatecount_bad`
  - 本轮 `__continuous_learning_20260324`

### 归档前已完成的检索总结

- 已完成 `notes__continuous_learning_20260324.md` 六文件摘要。
- 已确认旧支线里最重要的长期知识去向:
  - `AGENTS.md`
  - `EXPERIENCE.md`
  - `~/.codex/skills/self-learning.unity-compute-single-axis-dispatch-limit/SKILL.md`

### 备注

- 2026-03-24 之前 `__lidar_edge_aliasing` 中关于 `LidarBeamCount=512` 的描述仅代表历史状态。
- 旧支线现已转入 archive,后续再引用时应结合 `EXPERIENCE.md` 的更正口径一起阅读。
