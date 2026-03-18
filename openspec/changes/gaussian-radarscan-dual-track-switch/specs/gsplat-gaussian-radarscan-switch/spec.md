## ADDED Requirements

### Requirement: Gaussian-to-RadarScan switch SHALL start with Gaussian hide and delay radar show until the trigger threshold
系统 MUST 为专用的 `Gaussian -> RadarScan` 切换提供一个明确起点:

- 用户触发 `show-hide-switch-雷达` 后,系统 MUST 立即启动 Gaussian 的 hide 过程。
- 在触发阈值到达前,系统 MUST 继续保持 Gaussian hide 作为主视觉过程。
- 在触发阈值到达前,系统 MUST NOT 提前进入稳定的 RadarScan 终态。

#### Scenario: Trigger starts Gaussian hide before radar show
- **WHEN** 用户触发 `show-hide-switch-雷达`
- **THEN** 系统 MUST 立即进入 Gaussian hide 过程
- **AND** 在触发阈值到达前雷达 show MUST 尚未开始
- **AND** 在触发阈值到达前系统 MUST NOT 提前落到 `RenderStyle=ParticleDots` 的稳定 RadarScan 终态

### Requirement: Dual-track switch pair SHALL share a configurable trigger threshold with default value 0.35
系统 MUST 为 `show-hide-switch-高斯` 与 `show-hide-switch-雷达` 这组双向切换提供同一条可调触发阈值配置.

这条配置 MUST 满足:

- 默认值为 `0.35`
- 运行时读取前会做合法化处理,避免 NaN / Inf / 越界值污染时间线
- 正向按钮与反向按钮 MUST 都读取同一条阈值配置

#### Scenario: Default threshold starts the second effect at 0.35
- **WHEN** 用户未修改该阈值配置并触发任一双轨切换按钮
- **THEN** 系统 MUST 使用默认 `0.35` 作为第二效果的启动点
- **AND** 该默认值 MUST 同时作用于 `show-hide-switch-高斯` 与 `show-hide-switch-雷达`

#### Scenario: Customized threshold affects both paired buttons
- **WHEN** 用户把双轨切换阈值调整为一个新的合法值
- **THEN** `show-hide-switch-高斯` MUST 在新阈值触发第二效果
- **AND** `show-hide-switch-雷达` MUST 在同一新阈值触发第二效果

### Requirement: Gaussian-to-RadarScan switch SHALL preserve overlap between Gaussian hide and radar show
系统 MUST 在 `Gaussian -> RadarScan` 切换中保留 overlap 阶段.

在 overlap 阶段,系统 MUST 同时满足:

- Gaussian hide 已经开始且尚未完成
- Radar show 已经开始
- 用户肉眼可同时看到 `高斯 + 雷达效果`

#### Scenario: Trigger threshold starts radar show while Gaussian hide continues
- **WHEN** Gaussian hide 进度到达配置的触发阈值
- **THEN** 系统 MUST 启动 Radar show
- **AND** Gaussian hide MUST 继续推进而不是被提前终止
- **AND** overlap 阶段 MUST 允许两种效果同时存在

### Requirement: Gaussian-to-RadarScan switch SHALL drive LiDAR show on its own overlay track during overlap
系统 MUST 允许反向切换里的 LiDAR show 在 overlap 阶段走一条独立于共享 Gaussian hide 状态的专用 overlay 轨.

这意味着:

- LiDAR overlay MUST 在 overlap 阶段输出 show 语义,而不是误读成 Gaussian hide 的 hide 语义
- LiDAR overlay MUST 继续保留 show 的 progress / source mask / visual semantics
- 反向切换专用 show overlay 的优先级 MUST 高于共享 `Hiding` 状态

#### Scenario: LiDAR overlay keeps show semantics during overlap
- **WHEN** `show-hide-switch-雷达` 已进入 overlap 阶段
- **THEN** LiDAR overlay MUST 输出 show 语义
- **AND** LiDAR overlay MUST NOT 因为共享状态仍处于 `Hiding` 而退化成 hide overlay
- **AND** Gaussian hide 与 LiDAR show MUST 可同时被感知

### Requirement: Overlap phase SHALL keep Gaussian splats submitted until Gaussian hide completes
在 `Gaussian -> RadarScan` 的 overlap 阶段,系统 MUST 继续允许 Gaussian splat 的 sort/draw 提交.

这条要求 MUST 优先于普通稳定 RadarScan 语义下的:

- `EnableLidarScan=true`
- `HideSplatsWhenLidarEnabled=true`

因为 overlap 阶段的目标本身就是“Gaussian hide 与 Radar show 同屏”.

#### Scenario: Gaussian splats remain submit-enabled during reverse overlap
- **GIVEN** `HideSplatsWhenLidarEnabled=true`
- **WHEN** `show-hide-switch-雷达` 已进入 overlap 阶段且 Gaussian hide 尚未完成
- **THEN** Gaussian splat 的 sort/draw MUST 继续提交
- **AND** 普通 Radar 模式下的隐藏门禁 MUST 不得把 overlap 阶段的 Gaussian hide 整体挡掉

### Requirement: Gaussian-to-RadarScan switch SHALL enter stable RadarScan only after Gaussian hide completes
系统 MUST 把反向切换的稳定终态绑定到 Gaussian hide 的真正完成,而不是绑定到 Radar show 的启动时刻.

稳定终态 MUST 表示:

- Gaussian hide 已完成
- 系统已进入 RadarScan 目标模式
- 普通 Radar 模式的门禁与稳态语义重新生效

#### Scenario: Stable RadarScan state starts only after Gaussian hide finishes
- **WHEN** Radar show 已经启动但 Gaussian hide 尚未完成
- **THEN** 系统 MUST 仍处于 overlap 阶段而不是稳定 RadarScan 终态
- **WHEN** Gaussian hide 真正完成
- **THEN** 系统 MUST 进入稳定的 RadarScan 呈现
- **AND** 稳态 RadarScan 的默认门禁 MUST 恢复生效

### Requirement: Static and sequence renderers SHALL expose paired bidirectional switch controls
系统 MUST 让 `GsplatRenderer` 与 `GsplatSequenceRenderer` 都对外暴露成对的双向切换入口.

这组入口 MUST 满足:

- 同时存在 `show-hide-switch-高斯` 与 `show-hide-switch-雷达`
- 两者的说明文案 MUST 明确它们不是普通 show/hide,而是双轨 overlap 切换
- 两种 renderer MUST 共享同一套按钮语义与触发阈值语义

#### Scenario: Both renderer inspectors expose the paired switch buttons
- **WHEN** 用户打开 `GsplatRenderer` 或 `GsplatSequenceRenderer` 的 Inspector
- **THEN** Inspector MUST 同时提供 `show-hide-switch-高斯` 与 `show-hide-switch-雷达`
- **AND** Inspector 文案 MUST 明确双向切换与 overlap 语义
- **AND** 两种 renderer MUST 遵守相同的按钮行为契约
