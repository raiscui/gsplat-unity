# LATER_PLANS: 单帧 `.sog4d` 显示异常

## [2026-03-12 00:00:00 +0800] 后续关注: `scale codebook` 离群误差

- 当前 `sh0/alpha` 已经和同源 `.splat4d` 对齐
- 但离线统计仍显示 `scale codebook` 有少量大离群误差
- 如果用户在 Unity 刷新后仍感觉有残余“体积/叠加”异常,下一步优先做:
  - 同源 `.splat4d` vs 修复后 `.sog4d` 的 Unity 实拍对照
  - 针对大尺度离群点做保留极值/增大 codebook/混合采样策略验证
