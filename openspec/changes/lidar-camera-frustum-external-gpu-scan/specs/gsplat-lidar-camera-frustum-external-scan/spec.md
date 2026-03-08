## ADDED Requirements

### Requirement: Renderer SHALL support camera-frustum LiDAR aperture mode
系统 MUST 为 `GsplatRenderer` 与 `GsplatSequenceRenderer` 提供基于 camera frustum 的 LiDAR 扫描口径模式.
当该模式启用时,系统 MUST 使用指定 camera 的投影与视口角度作为 active scan cells 的来源,而不是继续按固定 360 度水平口径生成全部扫描方向.

#### Scenario: Frustum mode uses camera aperture
- **WHEN** 用户启用 frustum 扫描模式并指定一个 camera
- **THEN** 系统 MUST 只为该 camera frustum 覆盖的角度范围生成 active scan cells
- **AND** 不得继续为视口之外的 360 度方向生成完整 external scan 工作量

### Requirement: Frustum mode SHALL preserve an explicit LiDAR sensor-frame contract
在 frustum 模式下,系统 MUST 直接把指定 frustum camera 视为 authoritative LiDAR sensor pose.
系统 MUST 将该 camera 同时作为以下语义来源:

- sensor origin / translation
- aperture 朝向基准( forward / up / right )
- projection / FOV / aspect / pixelRect 语义

系统 MUST 使用 frustum camera 的完整 pose/projection 来驱动 external GPU capture:

- translation = `frustumCamera.transform.position`
- rotation = `frustumCamera.transform.rotation`
- projection / aspect / pixelRect semantics = `frustumCamera`

系统 SHOULD 让 `LidarOrigin` 在 frustum 模式下退回非必填或被明确忽略.
系统 MUST NOT 在 frustum 模式下继续要求用户再额外指定一个独立的 beam origin.

#### Scenario: Frustum mode directly uses camera pose
- **WHEN** 用户启用 frustum 模式并指定 frustum camera
- **THEN** 系统 MUST 直接使用该 camera 的位置和朝向作为 LiDAR sensor pose
- **AND** 不得再要求另一个独立的 `LidarOrigin` 来决定 frustum 模式的 beam origin

### Requirement: Frustum mode SHALL preserve current density semantics as closely as practical
系统 MUST 将现有 `LidarAzimuthBins` 与 `LidarBeamCount` 继续视为密度基线,并在 frustum 模式下根据 camera 水平/垂直角度推导 active cell 数.
系统 MUST 以“尽量保持当前屏幕内点密度观感”为目标,而不是把完整基线 cell 数强行压缩进更窄的 frustum 角度内.

#### Scenario: Narrow frustum does not over-densify visible points
- **WHEN** 用户在保持 `LidarAzimuthBins` 与 `LidarBeamCount` 不变的前提下启用较窄的 frustum camera
- **THEN** 系统 MUST 让屏幕内的 LiDAR 点密度观感尽量接近旧的 360 度路径
- **AND** 不得因为 frustum 变窄就把完整基线 cell 数全部塞进该窄角度区域导致点云明显变密

### Requirement: Renderer SHALL expose separate static and dynamic external target groups
系统 MUST 将 external target 输入拆分为 static 与 dynamic 两组,而不是只保留单一 external target 数组.
系统 MUST 至少提供:

- `LidarExternalStaticTargets`
- `LidarExternalDynamicTargets`
- dynamic 组的独立更新频率配置

#### Scenario: Static and dynamic targets are configured separately
- **WHEN** 用户同时配置 static external targets 与 dynamic external targets
- **THEN** 系统 MUST 将两组目标视为不同的扫描输入组
- **AND** dynamic 组 MUST 支持独立于 static 组的更新频率控制

### Requirement: Static external capture SHALL be reusable only while camera and static targets remain unchanged
系统 MUST 只在以下条件同时成立时复用 static external capture 结果:

- frustum camera 的位置、朝向、投影、FOV、aspect 与 pixelRect 未变化
- capture RT layout 与 cell-to-RT mapping 规则未变化
- static external target 的 transform / mesh 状态未变化
- static renderer 的 enabled / active 状态未变化
- external surface main-color 语义所依赖的材质状态未变化
  - 至少包括 `_BaseColor` / `_Color` 与 material slot 映射

当上述任一条件变化时,系统 MUST 使 static capture 失效并重新构建.

#### Scenario: Camera move invalidates static capture
- **WHEN** 用户启用 frustum 模式,且 static external targets 本身没有变化,但 frustum camera 的位置、朝向或投影发生变化
- **THEN** 系统 MUST 重新构建 static external capture
- **AND** 不得继续复用旧 camera 视角下的 static capture 结果

#### Scenario: Material main color change invalidates static capture
- **WHEN** static external target 的几何没有变化,但 `_BaseColor` / `_Color` 或 material slot 映射发生变化
- **THEN** 系统 MUST 使 static capture 失效并重新构建
- **AND** 不得继续复用旧的 external surface color 结果

### Requirement: Dynamic external targets SHALL support budgeted update frequency
系统 MUST 允许 dynamic external targets 按独立配置的更新频率刷新结果.
当尚未到达 dynamic 组的下一次更新时间时,系统 MAY 继续复用上一轮 dynamic capture 结果,以换取性能收益.

#### Scenario: Dynamic targets reuse last capture between updates
- **WHEN** dynamic external targets 已有上一轮有效 capture,且当前时刻尚未达到 `LidarExternalDynamicUpdateHz` 的下一次更新阈值
- **THEN** 系统 MAY 继续使用上一轮 dynamic capture 结果参与 external hit 竞争
- **AND** 不得因此阻塞 static external capture 或最终 LiDAR draw

### Requirement: External targets SHALL support GPU depth/color capture path in frustum mode
在 frustum 模式下,系统 MUST 提供 external target 的 GPU depth/color capture 主路径.
该路径 MUST 生成能够参与 LiDAR external hit 竞争的 external 深度与颜色结果,而不是继续默认依赖 CPU `RaycastCommand` 全量扫描.

#### Scenario: Frustum external scan uses GPU capture
- **WHEN** 用户启用 frustum 模式并配置 external targets
- **THEN** 系统 MUST 优先使用 GPU depth/color capture 生成 external hit 数据
- **AND** CPU `RaycastCommand` 不得继续作为 frustum 模式下的默认主路径

### Requirement: External GPU capture SHALL resolve depth into LiDAR ray-distance semantics
系统 MUST NOT 直接把 hardware depth 或普通 `view-space z` 写入 external hit buffer.
系统 MUST 在 GPU resolve pass 中完成以下语义对齐:

- depth RT -> linear hit position
- hit position -> LiDAR local / sensor space
- LiDAR local hit position -> 按 active cell center ray 投影得到 `depth` / `depthSq`

系统写入 external hit buffer 的距离语义 MUST 与当前 gsplat LiDAR compute 路线保持一致,从而允许 external hit 与 gsplat hit 做逐 cell 最近距离竞争.

#### Scenario: GPU depth is converted before nearest-hit competition
- **WHEN** external GPU capture 产出某个像素的 depth 命中
- **THEN** 系统 MUST 先把该 depth 转换为 LiDAR ray-distance 语义后再写入 external hit buffer
- **AND** 不得把原始 depth texture 值直接拿来与 gsplat 的 `depthSq` 结果比较

### Requirement: External GPU capture SHALL preserve nearest-hit competition with gsplat
系统 MUST 保持 external hit 与 gsplat hit 的逐 cell 最近距离竞争语义.
当 external depth 更近时,最终 LiDAR 点 MUST 采用 external 命中结果.
当 gsplat 命中更近时,最终 LiDAR 点 MUST 保持 gsplat 结果.

#### Scenario: External GPU hit occludes gsplat hit
- **WHEN** 某个 active scan cell 上 external GPU capture 产出的距离比 gsplat 命中更近
- **THEN** 最终 LiDAR 点 MUST 显示 external 命中结果
- **AND** 更远的 gsplat 命中不得继续出现在该 cell

### Requirement: External GPU capture SHALL preserve existing color semantics
系统 MUST 保持当前 LiDAR 颜色语义不变:

- `Depth` 模式下,external 命中 MUST 继续使用深度着色
- `SplatColorSH0` 模式下,external 命中 MUST 继续使用 external surface 的材质主色语义

性能优化不得主动改变用户当前的 RadarScan RGB 视觉语言.

系统在 GPU 路线下 MUST 将 external color capture 限制为 “surface main color” 语义.
该 capture MUST:

- 等价于当前 CPU external 路线读取 `_BaseColor` / `_Color`
- 不依赖实时光照
- 不依赖贴图采样结果
- 不依赖后处理或 tonemap 后的最终 scene color

#### Scenario: Frustum mode keeps existing RGB behavior
- **WHEN** 用户把现有 RadarScan 场景切换到 frustum + external GPU scan 路线
- **THEN** `Depth` 与 `SplatColorSH0` 的颜色语义 MUST 继续成立
- **AND** 不得因为性能优化而把 external 命中统一降级成单一固定颜色

#### Scenario: GPU route uses material main color rather than lit scene color
- **WHEN** external target 使用带贴图、灯光或后处理会改变最终画面颜色的材质
- **THEN** `SplatColorSH0` 模式下的 external 命中仍 MUST 对齐 `_BaseColor` / `_Color` 主色语义
- **AND** 不得直接复用 lit scene color 作为 external LiDAR 颜色

### Requirement: Existing visibility semantics SHALL remain available in frustum mode
系统 MUST 让 frustum 模式继续支持现有 external target 可见性模式与 LiDAR show/hide 语义,包括:

- `KeepVisible`
- `ForceRenderingOff`
- `ForceRenderingOffInPlayMode`
- show/hide bounds 与 external target 联合覆盖

#### Scenario: Frustum mode keeps scan-only and play-only visibility controls
- **WHEN** 用户在 frustum 模式下使用 `ForceRenderingOff` 或 `ForceRenderingOffInPlayMode`
- **THEN** external target 的普通 mesh 可见性语义 MUST 与现有行为保持一致
- **AND** external target 仍 MUST 继续参与 external hit 扫描

#### Scenario: Scan-only hidden mesh still participates in GPU capture
- **WHEN** external target 被 `ForceRenderingOff` 或 `ForceRenderingOffInPlayMode` 隐藏了普通 mesh 显示
- **THEN** frustum GPU capture 仍 MUST 继续捕获该 target 的深度与 surface main color
- **AND** capture 路径不得因为 source renderer 在 scene 中被强制隐藏而漏掉该 target
