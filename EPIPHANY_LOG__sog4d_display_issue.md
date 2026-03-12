# EPIPHANY_LOG: 单帧 `.sog4d` 显示异常

## [2026-03-12 00:01:00 +0800] 主题: learned `sh0Codebook` 会把真实单帧 PLY 的暗色基元整体抬亮

### 发现来源
- 用真实 `s1-point_cloud.ply` 对旧 `.sog4d` 产物做离线重建
- 再与同源 `.splat4d` 做逐字段对照

### 核心问题
- `sh0` 使用 importance-weighted learned scalar codebook 时
- 对真实单帧 PLY,codebook 可能完全失去负值半区
- 这会把大量 `f_dc < 0` 的基色整体抬亮

### 为什么重要
- 这类问题看起来像“渲染器叠加不正常”
- 但真正根因在离线编码策略
- 如果不记录,后续很容易误把锅甩给 runtime shader 或 blend

### 未来风险
- 只要继续沿用默认 learned `sh0Codebook`,其他真实单帧 PLY 也可能重复踩坑
- 特别是亮度分布长尾明显,且暗色 splat 数量多的场景

### 当前结论
- `sh0` 对齐 `.splat4d` 的 `baseRgb 8bit` 语义更稳
- 当前修复已经验证这条路径可以把 `sh0/alpha` 完全对齐到同源 `.splat4d`

### 后续讨论入口
- 若后续仍有残余观感问题,优先转查 `scale codebook` 离群误差
