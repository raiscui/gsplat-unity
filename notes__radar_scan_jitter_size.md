# 笔记: RadarScan 抖动波纹与小粒径异常

## [2026-03-24 16:25:00] [Session ID: 20260324_27] 上下文续档

- 旧 `notes__radar_scan_jitter_size.md` 已因超过 1000 行转入:
  - `archive/branch_contexts/radar_scan_jitter_size/snapshots/2026-03-24_162500/notes__radar_scan_jitter_size_2026-03-24_162500.md`
- 后续若继续这条线,优先回看:
  - `task_plan__radar_scan_jitter_size.md`
  - `WORKLOG__radar_scan_jitter_size.md`
  - `ERRORFIX__radar_scan_jitter_size.md`
  - `EPIPHANY_LOG__radar_scan_jitter_size.md`
  - 上面的 snapshot notes

## [2026-03-24 16:25:00] [Session ID: 20260324_27] 当前有效结论

- Unity MCP 跑测链路里:
  - `editor/state` 可读不等于动作通道可用
  - `summary.total = 0` 不能算有效通过证据
- LiDAR 相关线性 compute kernel 已改成:
  - CPU 侧分批 dispatch
  - shader 侧 `dispatchBaseIndex` 偏移
- `LidarBeamCount` 的 runtime `512` clamp 已移除:
  - 旧 notes 里把 `512` 当作当前硬上限的段落,只能按历史背景理解
- 如果后续继续这条任务,下一步更值得做的是:
  - 真实场景 smoke test
  - 性能与显存成本边界测量
  - 而不是再回到“为什么会被 clamp 到 512”这条已关闭问题
