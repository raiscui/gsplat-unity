# Capability: 4dgs-resource-budgeting

## ADDED Requirements

### Requirement: Estimate GPU memory footprint before allocation
在创建 `GraphicsBuffer` 之前,系统 MUST 估算 GPU 侧内存占用.
估算 MUST 至少考虑:
- Position/Scale/Rotation/Color/SH/Order 等现有 buffers
- Velocity/Time/Duration 等新增 buffers
- `SplatCount` 与每个 buffer 的 stride

系统 MUST 输出一个可读的估算结果(例如 MB),便于用户判断是否可行.

#### Scenario: 创建资源前打印估算
- **WHEN** 某个 GsplatRenderer 首次创建 GPU buffers
- **THEN** 日志中 MUST 包含该资产的显存估算值与关键参数(SplatCount,SHBands)

### Requirement: Warn when projected VRAM risk is high
系统 MUST 在高风险情况下给出明确告警.
高风险条件 SHOULD 包括:
- 估算显存超过 `SystemInfo.graphicsMemorySize` 的某个比例阈值(例如 60%).
- 或者 `SplatCount` 超过某个硬阈值且启用高阶 SH.

#### Scenario: 大规模 + SH3 触发告警
- **WHEN** `SplatCount` 很大且 SHBands=3,导致估算显存超过阈值
- **THEN** 系统 MUST 输出 warning,并说明可能的后果(性能下降、OOM)与建议的降级选项

### Requirement: Provide configurable auto-degrade policies
系统 MUST 提供可配置的自动降级策略,用于在资源不足时避免直接失败.
策略 MUST 至少包含以下选项:
- 降低 SH 阶数(例如强制只用 DC/SH0).
- 限制最大 splat 数(例如只上传前 N 个).

当自动降级生效时:
- 系统 MUST 输出明确 warning,并包含降级前后的关键参数.

#### Scenario: 自动降低 SH 阶数
- **WHEN** 用户启用 "AutoDegrade=ReduceSH",且估算显存超过阈值
- **THEN** 系统 MUST 自动降低 SH 渲染阶数,并输出 warning

#### Scenario: 自动限制最大 splat 数
- **WHEN** 用户启用 "AutoDegrade=CapSplatCount",且估算显存超过阈值
- **THEN** 系统 MUST 仅创建/上传前 N 个 splats,并输出 warning

### Requirement: Fail fast with actionable error if allocation fails
如果 `GraphicsBuffer` 创建失败或发生运行期异常,系统 MUST:
- 输出 `Error` 级别日志,包含失败的 buffer 类型、count、stride.
- 禁用当前 renderer 的渲染,避免持续报错或产生不确定行为.
- 给出可执行的恢复建议(例如降低 SH 阶数、减少 splat 数、关闭 VFX 后端).

#### Scenario: GraphicsBuffer 创建失败
- **WHEN** GPU buffer 分配失败(例如 OOM)
- **THEN** 系统 MUST 以可行动的错误信息失败,而不是静默忽略或崩溃

