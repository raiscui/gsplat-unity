## ADDED Requirements

### Requirement: Renderer SHALL expose selectable RadarScan particle antialiasing modes
系统 MUST 为 `GsplatRenderer` 与 `GsplatSequenceRenderer` 提供 RadarScan 粒子抗锯齿模式选项.
系统 MUST 至少支持以下模式:

- `LegacySoftEdge`
- `AnalyticCoverage`
- `AlphaToCoverage`
- `AnalyticCoveragePlusAlphaToCoverage`

#### Scenario: Both runtime components expose the same LiDAR AA modes
- **WHEN** 用户在 `GsplatRenderer` 或 `GsplatSequenceRenderer` 上启用 RadarScan
- **THEN** 两个组件都 MUST 暴露同一组 LiDAR 粒子 AA 模式
- **AND** Inspector 文案 MUST 使用一致的模式命名和说明

### Requirement: Legacy mode SHALL preserve current RadarScan edge behavior
系统 MUST 保留当前 RadarScan 粒子的固定 soft-edge 路线,作为 `LegacySoftEdge` 模式.
当用户未显式切换到新模式时,系统 MUST 尽量保持旧项目当前的边缘观感不变.

#### Scenario: Existing scene keeps current LiDAR edge look
- **WHEN** 旧场景升级到包含本 change 的版本,但用户没有显式切换 LiDAR 粒子 AA 模式
- **THEN** RadarScan 粒子 MUST 继续保持当前固定 soft-edge 的边缘语义
- **AND** 不得无提示地自动切到新的 coverage AA 路线

### Requirement: Analytic coverage mode SHALL use derivative-driven edge coverage
当用户选择 `AnalyticCoverage` 时,系统 MUST 使用基于屏幕导数的边缘 coverage 计算来替代纯固定 feather 路线.
该模式 MUST 仍然保持当前 RadarScan 粒子的点形状、颜色、深度和 show/hide 语义.

#### Scenario: Analytic coverage stabilizes small LiDAR point edges
- **WHEN** 用户把 RadarScan 粒子 AA 模式切到 `AnalyticCoverage`,且 `LidarPointRadiusPixels` 较小
- **THEN** 系统 MUST 使用基于屏幕导数的 coverage 过渡来处理点边缘
- **AND** 不得继续只依赖固定常量 feather 作为唯一边缘过渡宽度

### Requirement: Alpha-to-coverage modes SHALL only activate when MSAA is effectively available
当用户选择 `AlphaToCoverage` 或 `AnalyticCoveragePlusAlphaToCoverage` 时,系统 MUST 先判断当前相机或目标是否具备有效 MSAA 条件.
若 MSAA 条件不满足,系统 MUST 回退到稳定且可预期的本地 shader AA 路线.

#### Scenario: A2C mode falls back safely when MSAA is unavailable
- **WHEN** 用户选择 `AlphaToCoverage`,但当前相机或目标没有有效 MSAA
- **THEN** 系统 MUST 回退到 `AnalyticCoverage`
- **AND** 不得继续以不可预测状态运行 Alpha-to-Coverage

#### Scenario: Hybrid mode falls back to analytic coverage without MSAA
- **WHEN** 用户选择 `AnalyticCoveragePlusAlphaToCoverage`,但当前相机或目标没有有效 MSAA
- **THEN** 系统 MUST 回退到 `AnalyticCoverage`
- **AND** Inspector 或运行时诊断 MUST 明确说明 A2C 当前未生效

### Requirement: Alpha-to-coverage implementation SHALL remain local to the LiDAR particle draw path
系统 MUST 把 `AlphaToCoverage` 相关实现限制在 RadarScan 粒子 draw 路径内.
系统 MUST NOT 因为启用该模式而自动修改全局相机 AA、URP/HDRP 后处理或项目质量设置.

#### Scenario: Selecting LiDAR AA mode does not reconfigure camera post AA
- **WHEN** 用户切换 RadarScan 粒子 AA 模式
- **THEN** 系统 MUST 只影响 LiDAR 粒子自己的 draw 路径
- **AND** 不得自动改写 camera post-processing、renderer feature 或 QualitySettings 的全局 AA 配置

### Requirement: AA modes SHALL preserve existing RadarScan visual semantics
无论用户选择哪一种 LiDAR 粒子 AA 模式,系统 MUST 保持以下 RadarScan 语义不变:

- `Depth` / `SplatColorSH0` 颜色模式
- show/hide 与 glow
- 扫描前沿与 trail 强度
- external hit 与 gsplat hit 的最近命中竞争

AA 模式只允许影响粒子边缘 coverage / alpha 行为,不得顺带修改其它视觉语义.

#### Scenario: Changing LiDAR AA mode does not change hit/color logic
- **WHEN** 用户在同一个 RadarScan 场景里切换不同 LiDAR 粒子 AA 模式
- **THEN** 系统 MUST 保持同一条 LiDAR 命中结果、颜色模式和 show/hide 逻辑
- **AND** 变化范围 MUST 限制在粒子边缘 coverage / alpha 表现

### Requirement: Inspector SHALL communicate recommended mode and MSAA prerequisite clearly
Inspector MUST 明确告诉用户:

- `AnalyticCoverage` 是推荐的一般模式
- `AlphaToCoverage` 相关模式依赖有效 MSAA
- 当 A2C 条件不满足时,当前运行结果会回退到 `AnalyticCoverage`

#### Scenario: User sees AA prerequisites in Inspector
- **WHEN** 用户在 Inspector 中查看 RadarScan 粒子 AA 模式
- **THEN** Inspector MUST 展示 `AnalyticCoverage` 的推荐说明
- **AND** MUST 展示 `AlphaToCoverage` 相关模式对 MSAA 的依赖与 fallback 语义
