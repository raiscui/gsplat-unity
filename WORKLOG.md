# WORKLOG: 2026-03-12 continuous-learning

## 2026-03-12 23:42:00 +0800 任务名称: continuous-learning 六文件续档与知识沉淀

### 任务内容
- 回读默认组六文件与 4 套支线上下文集,提炼可复用知识
- 判断长期知识的最佳落点: `AGENTS.md` / `README.md` / `CHANGELOG.md` / `self-learning.*` skill
- 处理超过 1000 行的上下文文件续档与归档

### 完成过程
- 先列出并分组读取了:
  - 默认组六文件
  - `__imgui_layout_error`
  - `__sog4d_display_issue`
  - `__splat4d_edge_opacity`
  - `__splat4d_single_frame_support`
- 从这些记录里筛出了最值得长期沉淀的一条规则:
  - Unity `ScriptedImporter` 只要改变输出资产形态,就要同步 bump `[ScriptedImporter(version,...)]`
- 用 Unity 官方文档补证后,完成三路沉淀:
  - `AGENTS.md`: 增加 importer 输出形态变化时的 version bump 规则
  - `README.md`: 同步 `.ply` / `.splat4d` 导入行为与 `EnableFootprintAACompensation` 使用边界
  - `CHANGELOG.md`: 补记 `.ply` main object 修复与 footprint AA 开关
  - `~/.codex/skills/self-learning.unity-scriptedimporter-version-bump/SKILL.md`: 新增跨项目 skill
- 对超长上下文文件做了续档与归档:
  - `task_plan.md`
  - `notes.md`
  - `WORKLOG.md`
  - `notes__splat4d_edge_opacity.md`
  - `notes__splat4d_single_frame_support.md`

### 总结感悟
- 最有价值的 continuous-learning,往往不是再发明一份新总结,而是把“已经在支线里被证实的规律”送到真正长期可检索的位置。
- `ScriptedImporter` 这类工作流问题很容易伪装成“代码修复失败”,所以它特别适合沉淀成 skill + repo 规则双保险。
