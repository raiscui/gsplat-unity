# Capability: 4dgs-resource-budgeting

## MODIFIED Requirements

### Requirement: Estimate GPU memory footprint before allocation
在创建 GPU 资源(例如 `GraphicsBuffer`, `Texture2DArray`)之前,系统 MUST 估算 GPU 侧内存占用.
估算 MUST 至少考虑:
- 现有 Gsplat 后端的 buffers:
  - Position/Scale/Rotation/Color/SH/Order
- 现有 4DGS(linear) 的新增 buffers:
  - Velocity/Time/Duration
- 新增 `.sog4d`(keyframe) 路径可能引入的资源:
  - per-frame 的量化数据纹理(例如 position_hi/lo, rotation, scale_indices, sh0(alpha 含 opacity), shN_labels)
  - SH 的常驻 palette 资源(例如 sh0Codebook, shN_centroids)
  - 用于插值的双帧访问开销(至少 2 帧的可访问窗口,或等价的双缓冲策略)
  - 解码 compute 输出的中间 buffers(若采用 GPU-side decode)
- `SplatCount`, `FrameCount`, `SHBands`,以及每个资源的 stride/format/层数

系统 MUST 输出一个可读的估算结果(例如 MB),便于用户判断是否可行.

#### Scenario: 创建资源前打印估算
- **WHEN** 某个渲染组件首次为资产创建 GPU 资源
- **THEN** 日志中 MUST 包含该资产的显存估算值与关键参数(SplatCount,FrameCount,SHBands)

### Requirement: Warn when projected VRAM risk is high
系统 MUST 在高风险情况下给出明确告警.
高风险条件 MUST 至少包括:
- 估算显存超过 `SystemInfo.graphicsMemorySize` 的某个比例阈值(例如 60%)
- 或者 `SplatCount` 很大且启用高阶 SH(例如 SHBands=3)
- 或者 `.sog4d` 需要同时常驻多帧量化纹理/解码缓存,导致显存峰值明显上升

#### Scenario: 大规模 + SH3 触发告警
- **WHEN** `SplatCount` 很大且 SHBands=3,导致估算显存超过阈值
- **THEN** 系统 MUST 输出 warning,并说明可能后果(性能下降,OOM)与建议的降级选项

### Requirement: Provide configurable auto-degrade policies
系统 MUST 提供可配置的自动降级策略,用于在资源不足时避免直接失败.
策略 MUST 至少包含以下选项:
- 降低 SH 阶数(例如强制只用 DC/SH0)
- 限制最大 splat 数(例如只上传前 N 个)
- 关闭 keyframe 插值,回退到 `Nearest` 采样
- 降低 keyframe 缓存窗口(例如只缓存 1 个 chunk,或减小预取帧数)

当自动降级生效时:
- 系统 MUST 输出明确 warning,并包含降级前后的关键参数.

#### Scenario: 自动降低 SH 阶数
- **WHEN** 用户启用 "AutoDegrade=ReduceSH",且估算显存超过阈值
- **THEN** 系统 MUST 自动降低 SH 渲染阶数,并输出 warning

#### Scenario: 自动关闭插值
- **WHEN** 用户启用 "AutoDegrade=DisableInterpolation",且估算显存超过阈值
- **THEN** 系统 MUST 回退到 `Nearest` 采样,并输出 warning

### Requirement: Fail fast with actionable error if allocation fails
如果 GPU 资源创建失败或发生运行期异常,系统 MUST:
- 输出 `Error` 级别日志,包含失败的资源类型,count,stride/format
- 禁用当前渲染组件的渲染,避免持续报错或产生不确定行为
- 给出可执行的恢复建议(例如降低 SH 阶数,减少 splat 数,关闭插值,减小缓存窗口)

#### Scenario: GPU 资源创建失败
- **WHEN** GPU 资源分配失败(例如 OOM)
- **THEN** 系统 MUST 以可行动的错误信息失败,而不是静默忽略或崩溃
