## [2026-03-24 16:32:00] [Session ID: 20260324_27] 任务名称: continuous-learning 六文件检索、归档与知识沉淀

### 任务内容

- 回读默认组六文件与全部支线组,完成活跃度判定与“六文件摘要”
- 将未轮转旧支线从根目录迁入新的 `archive/branch_contexts/` 分层结构
- 把本轮新增的长期知识沉淀到 `EXPERIENCE.md`、`AGENTS.md` 和跨项目 skill

### 完成过程

- 先枚举了根目录下所有默认组六文件与 `__suffix` 支线文件,按支线分组读取最新记录
- 确认今天仍活跃的只有:
  - 默认组
  - `__radar_scan_jitter_size`
  - `__sog4d_updatecount_bad`
- 将其余 9 组无日期后缀旧支线整体归档到 `archive/branch_contexts/<topic>/`
- 同时把旧的平铺 `archive/*.md` 历史文件迁入:
  - `archive/default_history/`
  - `archive/branch_contexts/<topic>/snapshots/<timestamp>/`
- 对超过 1000 行的 `notes__radar_scan_jitter_size.md` 做了续档,保留活跃分支但清空当前写入压力
- 最后把今天真正新增的经验落到:
  - `EXPERIENCE.md`
  - `AGENTS.md`
  - `~/.codex/skills/self-learning.unity-compute-single-axis-dispatch-limit/SKILL.md`

### 总结感悟

- continuous-learning 真正有价值的地方,不是“再写一份总结”,而是把旧支线从根目录移走,同时把还会反复遇到的知识搬进长期入口。
- 今天最值得记住的新增经验有两条:
  - Unity MCP 的有效验证不能只盯 `editor/state`
  - 大规模线性 compute kernel 的问题,本质往往在 dispatch 模型,不是 shader 数学
