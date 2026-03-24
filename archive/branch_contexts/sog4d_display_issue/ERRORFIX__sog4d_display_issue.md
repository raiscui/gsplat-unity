# ERRORFIX: 单帧 `.sog4d` 显示异常

## 2026-03-11 23:58:00 +0800 问题: 单帧 `.sog4d` 显示发白,主观上像高斯叠加不正常

### 现象
- 用户反馈:
  - `Assets/Gsplat/sog4d/s1_point_cloud_final_check_20260311.sog4d` 显示很不正常
  - 对照 `GsplatRenderer` 的 `.splat4d` 资产显示正常
- 离线数值证据:
  - 旧 `.sog4d` 的 `position / rotation / opacity` 与原始 PLY 基本一致
  - 但 `sh0/base color` 与同源 `.splat4d` 差异很大
  - 旧 `.sog4d` 的 `sh0Codebook` 范围是 `[0.5196, 1.7599]`,没有任何负值
  - 原始 `f_dc` 约 `39.3%` 是负值

### 原因
- `Tools~/Sog4D/ply_sequence_to_sog4d.py` 旧逻辑默认用 importance-weighted learned `sh0Codebook`
- 对这份真实 `s1-point_cloud.ply`,该策略把大量负值/暗色 `f_dc` 压没了
- 结果是 `sh0.webp` 解码后的基色整体偏亮,视觉上会很像“叠加发白/混合异常”

### 修复
- 新增 `sh0-codebook-method=base-rgb`
- 并改成默认值
- `base-rgb` 直接复用 `.splat4d` 的 `baseRgb 8bit` 语义:
  - `sh0.webp` 的 RGB byte 直接写 `baseRgb`
  - `sh0Codebook[0..255]` 变成与 byte 一一对应的固定 `f_dc` 反解表

### 验证
- 命令:
  - `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py pack --input-ply .../Assets/Gsplat/ply/s1-point_cloud.ply --output /tmp/s1_point_cloud_fixed_sh0_20260311.sog4d --sh-bands 0 --self-check`
- 关键结果:
  - `validate ok (bands=0)`
  - 修复后 `.sog4d` 的 `fdc` 对同源 `.splat4d`:
    - `mae = [0, 0, 0]`
    - `max = [0, 0, 0]`
  - `alpha` 对同源 `.splat4d`:
    - `mae = 0`
    - `max = 0`

### 结论
- 旧资产的主要问题已经定位到 `sh0` 颜色编码策略
- 当前修复已经把 `sh0/alpha` 路径严格对齐到同源 `.splat4d`
- 若 Unity 里仍有残余观感问题,下一优先级是 `scale codebook` 的少量大离群误差
