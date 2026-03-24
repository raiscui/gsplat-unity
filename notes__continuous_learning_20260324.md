# 笔记: 2026-03-24 continuous-learning 六文件摘要

## [2026-03-24 16:18:00] [Session ID: 20260324_27] 笔记: 六文件摘要与沉淀决策

## 来源

### 来源1: 默认组六文件

- 文件:
  - `task_plan.md`
  - `notes.md`
  - `WORKLOG.md`
  - `LATER_PLANS.md`
  - `ERRORFIX.md`
- 要点:
  - 默认组当前没有新的主线实现收尾,更多是在承载支线索引与 2026-03-12 那轮 continuous-learning 的结果。
  - 2026-03-12 那轮已把 `ScriptedImporter version bump`、rigid sensor frame、overlap helper 等较强经验分别沉淀到了 `AGENTS.md` 或 skill。

### 来源2: 2026-03-11 到 2026-03-18 的旧支线组

- 涉及:
  - `__imgui_layout_error`
  - `__sog4d_display_issue`
  - `__splat4d_edge_opacity`
  - `__splat4d_single_frame_support`
  - `__show_hide_scale_tuning`
  - `__radar_scan_toggle`
  - `__lidar_edge_aliasing`
  - `__lidar_scaled_surface_offset`
  - `__gaussian_radarscan_dual_track_switch`
- 要点:
  - 这些支线都已经形成明确结论或完成实现验证。
  - 其中最值得长期复用的几条,已经被同步到现有载体:
    - `ScriptedImporter` 输出形态变化要 bump version
    - camera 驱动的 LiDAR layout / runtime / reconstruct 要共用 rigid sensor frame
    - overlap 时序不要直接复用带 cancel/reset 副作用的 public API
  - 因此这些支线本轮不再需要继续留在根目录占位,更适合整体归档。

### 来源3: 今天仍活跃的支线组

- 涉及:
  - `__radar_scan_jitter_size`
  - `__sog4d_updatecount_bad`
- 要点:
  - `__radar_scan_jitter_size` 今天新增了 3 条高价值结论:
    - Unity MCP 跑测不能只看 `editor/state`
    - `summary.total = 0` 不是有效通过证据
    - 大规模线性 compute kernel 需要 `dispatchBaseIndex + chunked dispatch`
  - `__sog4d_updatecount_bad` 今天确认:
    - `delta-v1 invalid updateCount` 这条红日志来自负向测试
    - 正确修复是清理测试收尾的 Console 噪声,而不是放宽 importer fail-fast

## 六文件摘要(用于决定如何沉淀知识)

- 涉及的上下文集:
  - 默认组
  - `__imgui_layout_error`
  - `__sog4d_display_issue`
  - `__splat4d_edge_opacity`
  - `__splat4d_single_frame_support`
  - `__show_hide_scale_tuning`
  - `__radar_scan_toggle`
  - `__lidar_edge_aliasing`
  - `__lidar_scaled_surface_offset`
  - `__gaussian_radarscan_dual_track_switch`
  - `__radar_scan_jitter_size`
  - `__sog4d_updatecount_bad`
- 任务目标(`task_plan*.md`):
  - 对根目录里的默认组六文件与全部支线文件做回读、活跃度判定、摘要、归档与长期知识分流。
- 关键决定(`task_plan*.md`):
  - 把今天仍在推进的支线和“无日期但其实已结束”的旧支线分开处理。
  - 不重复提炼已经进过 `AGENTS.md` / 既有 skill 的知识,把注意力放在今天新增且未沉淀的模式上。
- 关键发现(`notes*.md`):
  - 今天最有价值的新知识集中在 `__radar_scan_jitter_size`:
    - Unity MCP refresh / domain reload 后,动作通道与资源通道可能脱钩,必须交叉验证。
    - `summary.total = 0` 的 test job 不能算通过。
    - 大规模线性 compute kernel 不能默认塞进一次 `DispatchCompute(x,1,1)`。
  - `__sog4d_updatecount_bad` 证明了另一类经验:
    - 某些“像真实故障”的红日志,其实来自负向测试的预期输出,真正该修的是测试副作用。
- 实际变更(`WORKLOG*.md`):
  - 多条旧支线已在 3 月 11 日到 3 月 18 日之间完成实现和验证,但文件仍留在根目录。
  - `__radar_scan_jitter_size` 今天已经补齐 compute 分批 dispatch 与 `LidarBeamCount` 去 clamp 的实现验证。
  - `__sog4d_updatecount_bad` 今天已完成测试侧 Console 清理。
- 支线组摘要:
  - `__imgui_layout_error`: 结论指向工程级 `vInspector / vHierarchy` 的 `try/finally` 缺失,不属于本包长期规则,可归档。
  - `__sog4d_display_issue`: `sh0` 默认 learned codebook 会把暗色基元抬亮,已修成 `base-rgb`,当前无继续活跃证据,可归档。
  - `__splat4d_edge_opacity`: 最强长期知识已经抽成 importer version bump 规则; 其余是阶段性视觉排查流水,可归档。
  - `__splat4d_single_frame_support`: AA 开关的适用前提与单帧 `.splat4d` 路线已成结论,旧 notes 也已续档,可归档。
  - `__show_hide_scale_tuning`: dual-track overlap 的公开 API 副作用问题已落入 `AGENTS.md`,支线本体可归档。
  - `__radar_scan_toggle`: 功能已完成并验证,无延期事项,可归档。
  - `__lidar_edge_aliasing`: 这组分析里仍有“`LidarBeamCount` 会 clamp 到 512”的历史结论,需要在长期经验里标明这已过期,然后归档。
  - `__lidar_scaled_surface_offset`: rigid sensor frame 结论已写入 `AGENTS.md`,可归档。
  - `__gaussian_radarscan_dual_track_switch`: 反向 dual-track 与闪断修复已完成,可归档。
  - `__radar_scan_jitter_size`: 仍活跃; 需要保留在根目录,但 `notes` 已超过 1000 行,必须续档。
  - `__sog4d_updatecount_bad`: 仍活跃; 今天刚完成测试体验修复,保留在根目录。
- 支线组活跃度判定:
  - 活跃:
    - 默认组
    - `__radar_scan_jitter_size`
    - `__sog4d_updatecount_bad`
  - 未轮转旧支线:
    - `__imgui_layout_error`
    - `__sog4d_display_issue`
    - `__splat4d_edge_opacity`
    - `__splat4d_single_frame_support`
    - `__show_hide_scale_tuning`
    - `__radar_scan_toggle`
    - `__lidar_edge_aliasing`
    - `__lidar_scaled_surface_offset`
    - `__gaussian_radarscan_dual_track_switch`
- 暂缓事项 / 后续方向(`LATER_PLANS*.md`):
  - `LATER_PLANS.md` 仍保留若干历史候选优化,但本轮 continuous-learning 没新增新的项目级延期项。
  - `__lidar_edge_aliasing` 仍保留“更连续的 LiDAR 轮廓模式”作为未来方向。
- 错误与根因(`ERRORFIX*.md`):
  - `__radar_scan_jitter_size`: 已验证 compute dispatch 单维上限问题与 `LidarBeamCount` 旧 clamp 已被修正。
  - `__sog4d_updatecount_bad`: 已验证 fail-fast 逻辑不应改松,真正要处理的是测试后的 Console 残留。
- 重大风险 / 灾难点 / 重要规律(`EPIPHANY_LOG*.md`):
  - Unity MCP 的“资源可读”不代表“动作可用”。
  - 大规模线性 compute kernel 如果继续单次 `DispatchCompute(x,1,1)`,会重复撞上平台 group limit。
  - 旧支线里的 `LidarBeamCount=512` 结论已过期,不能再被当成当前事实。
- 可复用点候选:
  1. Unity MCP 有效验证纪律: `editor/state` + status 文件 + listener + 非 0 test total
  2. 线性 compute kernel 的 `dispatchBaseIndex + chunked dispatch` 模式
  3. 旧约束回收后,要在长期经验里显式标记“哪些旧笔记已过期”
- 最适合写到哪里:
  - repo-specific:
    - `EXPERIENCE.md`
    - `AGENTS.md`
  - cross-project:
    - `~/.codex/skills/self-learning.unity-compute-single-axis-dispatch-limit/SKILL.md`
- 需要同步的现有 `docs/` / `specs/` / plan 文档:
  - `AGENTS.md`
  - `README.md`
  - `CHANGELOG.md`
- 是否需要新增或更新 `docs/` / `specs/` / plan 文档:
  - 是
  - 新增:
    - `EXPERIENCE.md`
  - 更新:
    - `AGENTS.md`
  - 不需要额外更新 `README.md` / `CHANGELOG.md`:
    - `LidarBeamCount` 去 clamp 已在 `CHANGELOG.md` 中
    - 其它今天新增知识更偏开发流程与验证纪律,不属于 README 用户手册
- 是否提取/更新 skill:
  - 是
  - 理由:
    - “单次 `DispatchCompute(x,1,1)` 撞上 group limit,正确修法是 chunk + base index” 具有跨 Unity 项目复用价值,且今天已有完整动态证据
