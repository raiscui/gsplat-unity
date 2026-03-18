## ADDED Requirements

### Requirement: RadarScan-to-Gaussian switch SHALL start with radar hide and preserve visibility-hide visuals
系统 MUST 为专用的 RadarScan -> Gaussian 切换提供一个明确起点:

- 按钮触发后,系统 MUST 立即启动雷达粒子的 hide 过程.
- 在 Gaussian show 尚未启动前,系统 MUST 保持 `RenderStyle=ParticleDots`.
- 在 Gaussian show 尚未启动前,系统 MUST 保持 `EnableLidarScan=true`.
- 雷达粒子的前半段 hide MUST 复用现有 `visibility hide` 的视觉语言,包括 radial burn / noise / glow 处理.

#### Scenario: Trigger starts radar hide without early Gaussian show
- **WHEN** 用户触发 `show-hide-switch-高斯`
- **THEN** 系统 MUST 立即进入雷达粒子的 hide 过程
- **AND** 在过半前 `RenderStyle` MUST 仍为 `ParticleDots`
- **AND** 在过半前 `EnableLidarScan` MUST 仍为 `true`
- **AND** LiDAR overlay MUST 仍输出 hide 模式而不是 Gaussian show 模式

### Requirement: RadarScan-to-Gaussian switch SHALL start Gaussian show at radar-hide halfway and preserve overlap
系统 MUST 在雷达 hide 轨推进到过半时启动 Gaussian show.
该启动点 MUST 表示“第二条轨开始”,而不是“第一条轨结束”.

在 overlap 阶段,系统 MUST 同时满足:

- Gaussian show 已经开始
- 雷达粒子的 hide 轨仍在继续推进
- 用户肉眼可同时看到 `高斯 + 雷达粒子`

#### Scenario: Halfway starts Gaussian show while radar hide continues
- **WHEN** 雷达 hide 轨推进到过半
- **THEN** 系统 MUST 启动 Gaussian 的 show 动画
- **AND** 雷达粒子的 hide 轨 MUST 继续推进而不是被提前终止
- **AND** overlap 阶段 MUST 允许两种视觉同时存在

### Requirement: RadarScan-to-Gaussian switch SHALL defer LiDAR disable until radar hide track completes
系统 MUST 将 `EnableLidarScan` 的关闭时机绑定到雷达 hide 轨的真正完成,而不是绑定到 Gaussian show 的启动时刻.

#### Scenario: LiDAR main enable stays on until hide track finishes
- **WHEN** Gaussian show 已经启动但雷达 hide 轨尚未完成
- **THEN** 系统 MUST 保持 `EnableLidarScan=true` 或等价的 LiDAR runtime keepalive 语义继续有效
- **AND** 不得因为 Gaussian show 已启动就让雷达粒子立刻消失
- **WHEN** 雷达 hide 轨真正完成
- **THEN** 系统 MUST 关闭 `EnableLidarScan` 或释放等价的 LiDAR runtime 保活状态

### Requirement: Overlap phase SHALL submit Gaussian splats even when HideSplatsWhenLidarEnabled is active
在 RadarScan -> Gaussian dual-track overlap 阶段,系统 MUST 放开 Gaussian splat 的 sort/draw 提交门禁.

这条要求 MUST 优先于普通纯 Radar 语义下的:

- `EnableLidarScan=true`
- `HideSplatsWhenLidarEnabled=true`

因为 overlap 阶段的目标本身就是“Gaussian show 与雷达 hide 同屏”.

#### Scenario: Gaussian splats remain submit-enabled during overlap
- **GIVEN** `HideSplatsWhenLidarEnabled=true`
- **WHEN** RadarScan -> Gaussian dual-track 切换已经进入 overlap 阶段
- **THEN** Gaussian splat 的 sort/draw MUST 继续提交
- **AND** 纯 Radar 模式下的普通“隐藏 splats”门禁 MUST 不得把 overlap 阶段的 Gaussian show 整体挡掉

### Requirement: LiDAR hide overlay SHALL continue on its own track after Gaussian show begins
系统 MUST 允许 LiDAR 的 hide overlay 在 Gaussian show 启动后继续独立推进.

这意味着:

- LiDAR overlay MUST NOT 在 overlap 阶段被共享 `Showing` 状态覆盖
- LiDAR overlay MUST 继续保留 hide 的 progress / source mask / burn semantics
- overlap 阶段如果需要优先级,系统 MUST 优先使用 Radar->Gaussian 专用 hide overlay 轨

#### Scenario: LiDAR overlay keeps hide semantics during overlap
- **WHEN** Gaussian show 已经启动且雷达 hide 轨尚未完成
- **THEN** LiDAR overlay MUST 仍输出 hide 语义
- **AND** LiDAR overlay MUST 继续沿 hide progress 推进
- **AND** LiDAR overlay MUST NOT 退化成“无 overlay”或被 Gaussian show 的 overlay 覆盖

### Requirement: Static and sequence renderers SHALL expose the same RadarScan-to-Gaussian switch contract
系统 MUST 让 `GsplatRenderer` 与 `GsplatSequenceRenderer` 对这条专用切换保持一致的行为契约.

#### Scenario: Both renderer types follow the same dual-track switch semantics
- **WHEN** 用户在 `GsplatRenderer` 或 `GsplatSequenceRenderer` 上触发 `show-hide-switch-高斯`
- **THEN** 两种 renderer MUST 都遵守相同的 dual-track 时间线
- **AND** 两者都 MUST 满足:
  - hide 过半时启动 Gaussian show
  - overlap 阶段两者同屏
  - hide 完成后才关闭 LiDAR
