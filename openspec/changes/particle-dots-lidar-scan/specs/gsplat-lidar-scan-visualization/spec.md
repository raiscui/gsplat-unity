## ADDED Requirements

### Requirement: LiDAR 采集显示默认关闭且不影响现有渲染
系统 SHALL 提供一个可选的"LiDAR 采集显示"模式,并且默认 MUST 为关闭状态.
当该模式关闭时,系统 MUST 保持现有 Gaussian/ParticleDots 渲染行为与性能不变.

#### Scenario: 默认关闭
- **WHEN** 用户新添加/启用组件但未开启 LiDAR 开关
- **THEN** 视图中不出现 LiDAR 点云,且现有 splat 渲染路径不发生变化

### Requirement: LiDAR 坐标系以 LidarOrigin Transform 为准
系统 MUST 以 `LidarOrigin` 的 world position 与 rotation 作为 LiDAR 的原点与朝向.
当 `EnableLidarScan=true` 且 `LidarOrigin` 为空时,系统 MUST 不渲染 LiDAR 点云,并给出可行动的诊断日志(至少包含"需要指定 LidarOrigin").

#### Scenario: 未指定 LidarOrigin
- **WHEN** `EnableLidarScan=true` 且 `LidarOrigin=null`
- **THEN** LiDAR 点云不被渲染,并输出一次警告/错误日志提示需要设置 `LidarOrigin`

### Requirement: 采样网格支持 128x2048 且参数可配置
系统 SHALL 支持以 `BeamCount=128` 和 `AzimuthBins=2048` 作为默认采样网格.
系统 SHALL 允许用户配置 `AzimuthBins`.
系统 MAY 允许用户配置 `BeamCount`,但当 `BeamCount` 被配置时系统 MUST 确保其与竖直线束分布参数一致(例如 `BeamCount=UpBeams+DownBeams`).

#### Scenario: 采样网格为 128x2048
- **WHEN** 用户使用默认参数启用 LiDAR
- **THEN** 系统生成的采样网格为 128 条线束 x 2048 个方位角 bin

### Requirement: 竖直线束分布支持"上少下多"
系统 MUST 提供"上少下多"的竖直线束分布参数,至少包含:

- `UpFovDeg`(例如 +10 度)
- `DownFovDeg`(例如 -30 度)
- `UpBeams`(例如 16)
- `DownBeams`(例如 112)

系统 MUST 以这些参数将点的 `elevation` 映射到一个确定的 `beamIndex`,并且对超出 `[DownFovDeg, UpFovDeg]` 的点 MUST 直接剔除(不参与 first return).

#### Scenario: 超出竖直视场的点被剔除
- **WHEN** 某个 splat 的 elevation 小于 `DownFovDeg` 或大于 `UpFovDeg`
- **THEN** 该 splat 不参与 first return 归约,也不会影响任何 LiDAR 输出点

### Requirement: first return 语义为"每个(beam,azBin)仅保留最近距离"
系统 MUST 对每个 `(beamIndex, azimuthBin)` 方向仅保留最近距离的回波(第一回波).
当同一 cell 内存在多个候选回波时,系统 MUST 选择距离最小者.

#### Scenario: 同一 cell 内近点遮挡远点
- **WHEN** 同一 `(beamIndex, azimuthBin)` 内同时存在一个近点和一个远点
- **THEN** 该 cell 的输出距离为近点距离,远点不应在 LiDAR 点云中出现

### Requirement: 深度范围门禁使用 DepthNear/DepthFar
系统 MUST 以 `DepthNear` 与 `DepthFar`(默认 1m..200m)作为有效距离门禁:

- 小于 `DepthNear` 的回波 MUST 被忽略.
- 大于 `DepthFar` 的回波 MUST 被忽略.

#### Scenario: 超出深度范围的点被忽略
- **WHEN** 某个候选回波距离小于 `DepthNear` 或大于 `DepthFar`
- **THEN** 该回波不参与 first return,不会生成对应的 LiDAR 输出点

### Requirement: LiDAR 更新频率遵循 UpdateHz(方案 X 全量更新)
系统 MUST 支持 `UpdateHz` 作为 range image 的重建频率控制.
当 `UpdateHz=10` 时,系统 MUST 以约 0.1 秒为间隔全量重建一次 360 range image.
当 `UpdateHz` 为非正或非法值(NaN/Inf)时,系统 MUST 回退到一个安全默认值(例如 10Hz).

#### Scenario: UpdateHz 非法回退
- **WHEN** 用户将 `UpdateHz` 设置为 NaN/Inf/负数/0
- **THEN** 系统使用默认 UpdateHz(例如 10Hz)进行更新,并避免出现卡死或崩溃

### Requirement: 360 扫描前沿与余辉亮度由 RotationHz 与 azBin 年龄决定
系统 MUST 提供 `RotationHz`(默认 5Hz)控制扫描头的角速度.
系统 MUST 在渲染阶段根据扫描头位置与每个 `azimuthBin` 的相对年龄计算亮度,以表现:

- 扫描前沿更亮
- 后方逐渐变暗(余辉)
- 保留 1 圈数据的观感

#### Scenario: 扫描头更亮
- **WHEN** 当前扫描头位于某个 `azimuthBin`
- **THEN** 该 bin 的输出点亮度高于落后于它的一段 bin(余辉衰减)

### Requirement: LiDAR 点云渲染为规则点,点大小语义为屏幕像素半径
系统 MUST 将每个有效 `(beamIndex, azimuthBin)` cell 渲染为一个规则点,其世界坐标 MUST 满足:
`P = LidarOrigin.position + dir(beamIndex, azimuthBin) * range`.

系统 MUST 提供 `LidarPointRadiusPixels` 控制点大小,其语义 MUST 为屏幕像素半径(px radius),默认值 SHALL 为 2px.

#### Scenario: 点大小可调
- **WHEN** 用户将 `LidarPointRadiusPixels` 从 2 调整到 4
- **THEN** LiDAR 点在屏幕上的半径增大,但其世界坐标位置不变

### Requirement: 颜色模式支持 Depth 与 SplatColor(SH0)
系统 MUST 提供颜色模式切换,至少包含:

- Depth: 基于 `DepthNear/DepthFar` 对 `range` 做颜色映射.
- SplatColor(SH0): 取 first return 对应 splat 的基础颜色(SH0)作为输出点颜色.

当启用 SplatColor(SH0) 时,系统 MUST 保证输出点颜色与 first return 的距离来自同一个 splat(不得偶发错配).

#### Scenario: 颜色模式切换
- **WHEN** 用户从 Depth 模式切换到 SplatColor(SH0) 模式
- **THEN** LiDAR 点的颜色从深度色变为采样自 first return splat 的基础色,且同一时刻点的位置不变

### Requirement: 可隐藏 splat 渲染但保留 LiDAR 采样
系统 SHALL 提供一个选项用于"隐藏 splat 渲染"(不提交 splat 的 sort/draw),同时 LiDAR 仍然可以读取 splat 的 Position/Color buffers 进行采样.

#### Scenario: 仅显示 LiDAR 点云
- **WHEN** 用户启用 LiDAR 并开启"隐藏 splat 渲染"
- **THEN** 视图中只显示 LiDAR 点云,不显示 splat,且 LiDAR 采样仍然正常工作
