# 任务计划: 单帧 `.sog4d` 显示异常根因排查

## 目标

定位 `Assets/Gsplat/sog4d/s1_point_cloud_final_check_20260311.sog4d` 在 Unity 中显示异常的真实原因。
如果问题在转换参数或 package 代码,完成最小修复并验证。

## 阶段

- [x] 阶段1: 现象与对照确认
- [x] 阶段2: 数据语义排查
- [x] 阶段3: 运行时路径排查
- [x] 阶段4: 最小修复与验证

## 关键问题

1. 当前异常更像“数据被打包错了”,还是“Renderer/SequenceRenderer 显示路径不同”?
2. `s1-point_cloud.ply` 的 `opacity` / `scale_*` 是否符合当前 `.sog4d` 工具默认假设?
3. `splat4d` 工具与 `sog4d` 工具对同一份 PLY 的解释规则是否一致?

## 现象

- 用户观察:
  - `Assets/Gsplat/sog4d/s1_point_cloud_final_check_20260311.sog4d` 显示很不正常
  - 感觉像“高斯基元叠加方式不正常”
- 对照:
  - `Assets/Gsplat/splat/v2/ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest.splat4d`
  - `GsplatRenderer` 显示正常

## 当前主假设与备选解释

- 主假设:
  - 这次 `s1-point_cloud.ply -> .sog4d` 使用的转换口径有一项不匹配真实 PLY 语义,导致 opacity/scale/sh 数据被错误解释
- 备选解释:
  - 数据本身没错,问题在 `GsplatSequenceRenderer` 的单帧显示路径

## 做出的决定

- 先做“同一源 PLY 的数据语义核对”,不先改代码.
  - 理由: 用户描述的“叠加不正常”非常像 opacity/scale 语义错读,这比先改 renderer 更容易被证伪.

## 遇到的错误

- (待补)

## 状态

**目前已完成阶段4**
- 脚本侧修复已经完成,并且真实 `s1-point_cloud.ply` 的重打包产物已经覆盖回 Unity 工程中的目标 `.sog4d` 路径.
- 当前剩余未完全拿到的是“编辑器内截图证据”,但数值对照已经把 `sh0/alpha` 路径严格对齐到同源 `.splat4d`.

## 2026-03-11 23:17:00 +0800 追加行动

- 准备执行“同源对照实验”:
  - 用同一份 `Assets/Gsplat/ply/s1-point_cloud.ply` 生成单帧 `.splat4d`
  - 再生成一份 `sh-bands=3` 的 `.sog4d` 变体
  - 将两份资产都导入 Unity 工程
- 目的:
  - 先证伪“是不是当前 `.sog4d` 打包参数有问题”
  - 再判断要不要深入 `GsplatSequenceRenderer` / importer / shader 代码路径

## 2026-03-11 23:23:00 +0800 阶段进展

- “同源转换实验”已完成:
  - `/tmp/s1_point_cloud_same_source_20260311.splat4d`
  - `/tmp/s1_point_cloud_sh3_20260311.sog4d`
- 下一步:
  - 离线核对 `.sog4d` 重建后的 alpha/scale/color 与原始 PLY 是否偏离
  - 把实验资产导入 Unity,抓取导入日志与显示证据

## 2026-03-11 23:34:00 +0800 追加行动

- 当前离线证据显示:
  - `position / rotation / opacity` 误差很小
  - `scale` 有少量大离群误差
  - `sh0/base color` 量化误差明显偏大
- 准备做可视化最小验证:
  - 在 Unity 工程根目录临时增加一个 Editor 批处理探针
  - 同场景实例化同源 `.splat4d` 与 `.sog4d`
  - 用同一相机输出截图,避免靠主观描述猜问题

## 2026-03-11 23:46:00 +0800 修复进展

- 已完成脚本修复:
  - `Tools~/Sog4D/ply_sequence_to_sog4d.py`
  - `sh0-codebook-method` 默认从 learned codebook 改为 `base-rgb`
- 已验证:
  - 修复后的 `.sog4d` 在 `sh0/alpha` 上与同源 `.splat4d` 完全对齐
- 当前待验证:
  - Unity 实际显示是否已经恢复正常
  - 若仍异常,优先继续查 `scale codebook` 的少量大离群误差

## 2026-03-11 22:22:27 +0800 支线索引: 启用 `__splat4d_single_frame_support` 上下文集

- 启用原因:
  - 用户当前提出的是新的 OpenSpec 立项任务:
    - 为 `Tools~/Splat4D/ply_sequence_to_splat4d.py` 增加“单个普通 3DGS `.ply` -> 单帧 `.splat4d`”正式支持
    - 并明确 Unity `GsplatRenderer` 支持这条显示路径
  - 这和当前 `.sog4d` 显示异常排查是不同目标,需要单独记录 artifact 与决策
- 支线上下文后缀:
  - `__splat4d_single_frame_support`
- 支线主题:
  - 创建新的 OpenSpec change,为单帧普通 3DGS `.ply -> .splat4d -> GsplatRenderer` 工作流建立 proposal 起点
