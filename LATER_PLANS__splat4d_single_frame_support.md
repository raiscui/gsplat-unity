# LATER_PLANS: 单帧普通 3DGS `.ply -> .splat4d` OpenSpec 立项

## 2026-03-12 12:24:25 +0800 后续候选: 为单帧 delta-v1 资产补最小 runtime guard 与回归测试

- 候选修复点:
  - `Runtime/GsplatRenderer.cs` 的 `TryInitShDeltaRuntime()`
- 候选策略:
  - 当 `GsplatAsset.ShFrameCount <= 1` 时,直接跳过动态 SH runtime 初始化
- 配套回归建议:
  - 增加一个针对 `frameCount=1` `.splat4d v2 + delta-v1` 的 importer/runtime 测试
  - 目标不是锁定当前不对称行为
  - 而是锁定“单帧资产退化为静态语义,不分配动态 SH runtime”
- 触发时机:
  - 若后续决定正式修这个问题
  - 或开始支持单帧 SH / 单帧 delta-v1 exporter

## 2026-03-12 17:57:00 +0800 后续候选: `.splat4d v2` 多帧 exporter 与 Unity 在线验证恢复

- 当前已落地的是:
  - 单帧 `.ply -> .splat4d v2 + SH(full)`
- 还没有做的是:
  - 多帧序列直接导出 `.splat4d v2`
  - Unity MCP 在线读取这份真实资产的 imported prefab / console 结果
- 若后续继续,推荐顺序:
  - 先恢复 Unity MCP session 或让当前 Editor 完成 refresh
  - 跑 `Gsplat.Editor.BatchVerifySplat4DImport.VerifyS1PointCloudV2Sh3`
  - 再讨论是否需要把多帧 `.splat4d v2` 做成正式 exporter 路径

## 2026-03-12 18:52:00 +0800 后续候选: 审计 `Tools~/Sog4D/ply_sequence_to_sog4d.py` 是否复用了同样的 SH 排列错误

- 发现来源:
  - ast-grep 搜到 `Tools~/Sog4D/ply_sequence_to_sog4d.py` 里也有同样的 `flat.reshape(flat.shape[0], rest_coeff_count, 3)`
- 为什么值得后续处理:
  - 如果 `Sog4D` 的 PLY `f_rest_*` 也遵循同一份 `RRR... GGG... BBB...` 契约,它很可能存在相同的潜在色偏问题
- 当前状态:
  - 本轮聚焦 `.splat4d` 用户资产,还没有对 `Sog4D` 做动态验证

## 2026-03-12 19:55:00 +0800 后续候选: 为 `Gsplat` 做 SuperSplat 对照用的纯观察模式 + `aaFactor` A/B 开关

- 目标:
  - 把"场景后处理差异"与"高斯算法差异"拆开验证
- 建议分两步:
  1. 纯观察模式:
     - 临时关闭 HDRP Volume 中的 `Exposure / Tonemapping / FilmGrain / Bloom / ColorCurves / LiftGammaGain`
     - 或提供一个不受场景 Volume 污染的最小对照场景
  2. shader A/B:
     - 仅补 `aaFactor` compensation
     - 不改 `normExp` 主核
- 价值:
  - 可以把"我感觉更脏"变成可截图、可复现、可归因的对照实验
