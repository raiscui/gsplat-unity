# 笔记: 单帧 `.sog4d` 显示异常

## 2026-03-11 23:09:00 +0800 初始现象

- 用户反馈:
  - `Assets/Gsplat/sog4d/s1_point_cloud_final_check_20260311.sog4d` 在 Unity 中显示很不正常
  - 主观感受更像高斯叠加/混合不正常
- 对照组:
  - `Assets/Gsplat/splat/v2/ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest.splat4d` 显示正常

## 调试约束

- 当前不能把“看起来像叠加问题”直接当根因
- 需要优先区分:
  - 数据打包语义错
  - 渲染路径错
  - 还是只是 `sh-bands=0` 造成的观感差异

## 2026-03-11 23:18:00 +0800 当前代码路径线索

- `.splat4d` 走:
  - `Editor/GsplatSplat4DImporter.cs`
  - `Runtime/GsplatRenderer.cs`
- `.sog4d` 走:
  - `Editor/GsplatSog4DImporter.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
  - `Runtime/Shaders/GsplatSequenceDecode.compute`
- 当前最重要的最小证伪实验:
  - 用同一份 `s1-point_cloud.ply` 生成单帧 `.splat4d`
  - 再生成 `sh-bands=3` 的 `.sog4d`
  - 若 `.splat4d` 正常而 `.sog4d` 异常,优先怀疑 `.sog4d` 打包或 sequence 解码路径

## 2026-03-11 23:22:00 +0800 同源转换实验结果

- 同源 `.splat4d` 已生成:
  - `/tmp/s1_point_cloud_same_source_20260311.splat4d`
  - 日志: `wrote 169,133 splats`
- `.sog4d(sh3)` 变体已生成并通过脚本自检:
  - `/tmp/s1_point_cloud_sh3_20260311.sog4d`
  - 日志要点:
    - `frames: 1`
    - `splats: 169133, shBands: 3`
    - `layout: 412x411`
    - `validate ok (v1 delta-v1)`
- 当前结论:
  - 单从脚本侧看,同一份真实 PLY 可以稳定产出 `.splat4d` 和 `.sog4d`
  - 但这还不能证明 Unity 里的显示路径也正确

## 2026-03-11 23:45:00 +0800 关键数值证据

- 当前异常 `.sog4d` 的离线重建结果:
  - `position / rotation / opacity` 与原始 PLY 基本一致
  - `scale` 仍有少量大离群误差
  - `sh0/base color` 误差非常大
- 更关键的是:
  - 原始 `f_dc` 约有 `39.3%` 为负值
  - 旧 `.sog4d` 的 `sh0Codebook` 却是 `0%` 负值,范围只有 `[0.5196, 1.7599]`
  - 这会把大量暗色/负值基色系统性抬亮
- 已完成修复:
  - `Tools~/Sog4D/ply_sequence_to_sog4d.py`
  - 新增 `sh0-codebook-method=base-rgb`
  - 并改为默认值
  - `base-rgb` 直接对齐 `.splat4d` 的 `baseRgb 8bit` 语义
- 修复后再用真实 `s1-point_cloud.ply` 生成 `.sog4d`:
  - `sh0/alpha` 与同源 `.splat4d` 数值完全一致
  - 当前剩余的主要差异只在 `scale codebook`
