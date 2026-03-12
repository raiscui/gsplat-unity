## ADDED Requirements

### Requirement: Single-frame static `.splat4d` MUST remain a valid canonical 4D asset
系统 MUST 将由普通单帧 3DGS `.ply` 导出的静态 `.splat4d` 视为合法的现有 `.splat4d` 资产.

这里的“静态 `.splat4d`”指其 record 使用规范默认值:
- `vx/vy/vz = 0`
- `time = 0`
- `duration = 1`

对于这类资产:
- 导入器 MUST 继续走现有 `.splat4d` 导入路径
- 系统 MUST 生成正常的 `GsplatAsset`
- `Velocities / Times / Durations` MUST 具有与 `splatCount` 等长的数据
- 导入后的 4D 数据 MUST 保持与导出值语义一致:
  - 零速度表示无时间位移
  - `time = 0, duration = 1` 表示在整个归一化时间范围内可见

系统 MUST NOT 因为这类资产“看起来是静态的”就要求改走 `.ply` importer、降级成另一种资产类型,或依赖伪造的第二帧来源.

#### Scenario: One-frame static `.splat4d` imports with canonical 4D arrays
- **WHEN** 用户导入一个由普通单帧 3DGS `.ply` 导出的静态 `.splat4d`
- **THEN** 系统 MUST 生成一个正常的 `GsplatAsset`
- **AND** 该资产的 `Velocities / Times / Durations` MUST 与 `splatCount` 等长
- **AND** 对应默认值 MUST 保持 `vx/vy/vz = 0`, `time = 0`, `duration = 1`

#### Scenario: Canonical static defaults imply no motion and full normalized-time visibility
- **WHEN** 系统对这类静态 `.splat4d` 在 `t = 0.0` 与 `t = 1.0` 下分别按现有 4D 运动与可见性语义进行评估
- **THEN** splat 的瞬时位置 MUST 保持不变
- **AND** splat MUST 在这两个时间点都保持可见
