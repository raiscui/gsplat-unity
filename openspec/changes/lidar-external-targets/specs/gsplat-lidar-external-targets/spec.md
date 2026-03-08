## ADDED Requirements

### Requirement: Renderer SHALL expose LiDAR external target roots
系统 SHALL 为 `GsplatRenderer` 与 `GsplatSequenceRenderer` 提供 `LidarExternalTargets : GameObject[]` 配置项.
当该数组为空或未配置时,系统 MUST 保持当前纯 gsplat RadarScan 行为不变.

#### Scenario: External targets left empty
- **WHEN** 用户启用 RadarScan 但 `LidarExternalTargets` 为空
- **THEN** 系统只扫描 gsplat 命中,并保持现有 LiDAR 行为与结果不变

### Requirement: External targets SHALL be collected recursively from root GameObjects
系统 MUST 将 `LidarExternalTargets` 中的每个元素视为根对象,并递归收集其子层级中的可扫描 Renderer.
系统 MUST 至少支持以下类型:

- `MeshRenderer` 搭配 `MeshFilter`
- `SkinnedMeshRenderer`

不支持的 Renderer 类型 MUST 被忽略,并提供可行动诊断而不是中断整个 LiDAR 扫描.

#### Scenario: Root object contains nested mesh renderers
- **WHEN** 用户把一个 prefab 根对象放入 `LidarExternalTargets`,且其子节点包含多个 mesh renderer
- **THEN** 系统会递归纳入这些子节点 mesh 参与扫描,用户不需要逐个手工列出子节点

### Requirement: External targets SHALL use real mesh geometry for LiDAR hits
系统 MUST 使用真实 mesh 参与外部目标扫描,不得使用球体、胶囊或盒体近似碰撞体替代.
静态 mesh MUST 使用原始 shared mesh 几何.
`SkinnedMeshRenderer` MUST 使用与当前扫描时刻一致的 baked mesh 快照参与命中.

#### Scenario: Skinned mesh changes pose before scan update
- **WHEN** 一个 `SkinnedMeshRenderer` 在两次 LiDAR 更新之间发生姿态变化
- **THEN** 下一次按 `LidarUpdateHz` 触发的外部目标扫描 MUST 使用更新后的 baked mesh 形状进行命中

### Requirement: External target hits SHALL compete with gsplat hits per cell
系统 MUST 让外部目标命中与 gsplat 命中在每个 `(beamIndex, azimuthBin)` 上比较最近距离.
当 external hit 比 gsplat hit 更近时,该 cell 的最终 LiDAR 输出 MUST 采用 external hit.
当 gsplat hit 更近时,该 cell 的最终 LiDAR 输出 MUST 保持 gsplat hit.

#### Scenario: External target occludes gsplat
- **WHEN** 同一个 `(beamIndex, azimuthBin)` 上存在一个更近的外部模型命中和一个更远的 gsplat 命中
- **THEN** 最终 LiDAR 点 MUST 显示外部模型命中结果,更远的 gsplat 命中不得出现在该 cell

### Requirement: External hits SHALL follow LiDAR color mode semantics
系统 MUST 让 external hit 遵循当前 LiDAR 颜色模式:

- `Depth`: external hit MUST 使用与 gsplat hit 一致的深度着色规则
- `SplatColorSH0`: external hit MUST 使用命中材质的主色,而不是 SH0 颜色

当命中的 mesh 具有多个 submesh / materials 时,系统 MUST 根据实际命中的三角面解析正确的材质主色.

#### Scenario: External target hit in SplatColorSH0 mode
- **WHEN** 当前颜色模式为 `SplatColorSH0`,且某个 cell 最终命中的是外部模型
- **THEN** 该 LiDAR 点的颜色 MUST 来自命中材质的主色,而不是深度色或任意固定默认色

### Requirement: RadarScan visibility coverage SHALL include external target bounds
系统 MUST 在计算 RadarScan 的 show/hide 覆盖范围时,同时考虑 gsplat bounds 与 external targets 的联合范围.
外部目标即使位于 gsplat 原始 bounds 之外,也 MUST 受同一套 LiDAR show/hide 半径与可见性动画控制.

#### Scenario: External target extends beyond gsplat bounds
- **WHEN** 一个外部目标位于 gsplat 原始局部 bounds 之外,且用户触发 RadarScan show 或 hide
- **THEN** 可见性动画 MUST 仍完整覆盖该外部目标,不得因为只使用 gsplat bounds 而提前裁掉

### Requirement: HideSplatsWhenLidarEnabled SHALL not disable external target scanning
当 `HideSplatsWhenLidarEnabled=true` 时,系统 MAY 停止 splat 的 sort/draw 提交,但 MUST 保留:

- gsplat LiDAR 采样
- external target LiDAR 扫描
- 最终规则点云渲染

#### Scenario: Pure radar view with external targets
- **WHEN** 用户启用 RadarScan,设置 `HideSplatsWhenLidarEnabled=true`,并配置了 `LidarExternalTargets`
- **THEN** 视图中 MUST 只显示最终 LiDAR 粒子结果,同时外部目标与 gsplat 都继续参与扫描命中

### Requirement: External targets SHALL support scan-only visibility mode
系统 MUST 为 `GsplatRenderer` 与 `GsplatSequenceRenderer` 提供 external target 普通 mesh 可见性模式配置.
系统 MUST 至少支持以下模式:

- `KeepVisible`: 外部目标继续显示普通 mesh
- `ForceRenderingOff`: 外部目标继续参与 LiDAR 扫描,但不显示普通 mesh shader 效果
- `ForceRenderingOffInPlayMode`: Play 模式下不显示普通 mesh,非 Play 的编辑器模式下继续显示普通 mesh

当用户未显式修改该字段时,默认模式 SHOULD 为 `ForceRenderingOff`,以符合“external target 主要作为 RadarScan 命中源”的主语义.

#### Scenario: ForceRenderingOff hides ordinary mesh but keeps LiDAR scanning
- **WHEN** 用户把某个 mesh 目标加入 `LidarExternalTargets`,并使用 `ForceRenderingOff`
- **THEN** 该目标 MUST 继续参与 external hit 扫描
- **AND** 该目标 MUST 不再以普通 mesh 渲染显示在画面中

#### Scenario: ForceRenderingOffInPlayMode keeps mesh visible in EditMode
- **WHEN** 用户把某个 mesh 目标加入 `LidarExternalTargets`,并使用 `ForceRenderingOffInPlayMode`
- **AND** 当前不在 Play 模式
- **THEN** 该目标 MUST 继续以普通 mesh 显示
- **AND** 仍可继续参与 LiDAR 扫描

#### Scenario: ForceRenderingOffInPlayMode hides mesh during Play mode
- **WHEN** 用户把某个 mesh 目标加入 `LidarExternalTargets`,并使用 `ForceRenderingOffInPlayMode`
- **AND** 当前处于 Play 模式
- **THEN** 该目标 MUST 不再以普通 mesh 显示
- **AND** 仍 MUST 继续参与 LiDAR 扫描

#### Scenario: Helper cleanup restores original renderer state
- **WHEN** external target 被移出 `LidarExternalTargets`,或 helper 因组件关闭/销毁而释放
- **THEN** 系统 MUST 恢复该 source renderer 原始的 `forceRenderingOff` 状态
- **AND** 不得把“scan-only 隐藏”状态永久污染到用户原场景对象上
