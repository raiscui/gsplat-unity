# WORKLOG: 单帧 `.sog4d` 显示异常

## [2026-03-11 23:59:00 +0800] 任务名称: 单帧 `.sog4d` 显示异常定位与修复

### 任务内容
- 针对真实样例 `Assets/Gsplat/ply/s1-point_cloud.ply` 排查 `.sog4d` 显示异常
- 修复 `Tools~/Sog4D/ply_sequence_to_sog4d.py` 的 `sh0` 编码策略
- 用真实产物重新覆盖 `Assets/Gsplat/sog4d/s1_point_cloud_final_check_20260311.sog4d`

### 完成过程
- 先做同源对照:
  - 生成单帧 `.splat4d`
  - 生成旧逻辑 `.sog4d`
  - 离线重建两者并比较 `position / scale / rotation / f_dc / opacity`
- 锁定关键差异:
  - `position / rotation / opacity` 正常
  - 旧 `.sog4d` 的 `sh0Codebook` 没有负值,而原始 `f_dc` 有大量负值
- 实施修复:
  - 新增 `sh0-codebook-method=base-rgb`
  - 默认改为 `base-rgb`
  - `sh0.webp` RGB 直接对齐 `.splat4d` 的 `baseRgb` 字节语义
- 重新生成与验证:
  - 修复后 `.sog4d` 的 `sh0/alpha` 与同源 `.splat4d` 完全一致
  - 再把修复后的产物覆盖到 Unity 工程中的实际资产路径

### 总结感悟
- `sh0` 这种“最终直接决定观感”的通道,优先对齐成熟量化语义,比学一个 data-dependent codebook 更稳
- 这类显示异常不能只看最终 shader,先做“同源产物数值对照”能更快缩小根因
