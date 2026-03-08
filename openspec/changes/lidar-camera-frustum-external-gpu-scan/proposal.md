## Why

当前 RadarScan 的 external mesh 路径仍基于 `beamCount * azimuthBins` 的 360 度 CPU `RaycastCommand` 扫描. 当用户加入 external mesh,尤其是 skinned mesh 后,性能会立刻明显下降,但用户又不希望通过降低粒子数量、扫描频率或颜色表现来换性能.

现在需要把 external mesh 的扫描语义改成“基于 camera frustum 的口径扫描”,并把 external 路径从 CPU raycast 迁移到 GPU depth/color 采样主导的路线. 这样可以把计算集中在真正可见的视口区域,同时尽量保持当前 RadarScan 的视觉密度、RGB 语义和节奏不变.

## What Changes

- 新增基于 camera frustum 的 RadarScan 口径模式:
  - 扫描方向不再默认固定为 360 度水平覆盖.
  - 系统改为按指定 camera 的视口/FOV 生成 active scan cells.
  - 屏幕内的采样密度应尽量对齐当前 `LidarAzimuthBins` / `LidarBeamCount` 的视觉观感,而不是简单粗暴减少点数.
- external target 输入从单一 `LidarExternalTargets` 扩展为两组:
  - `LidarExternalStaticTargets`
  - `LidarExternalDynamicTargets`
  - 并为 dynamic 组提供独立更新频率设置.
- external mesh 命中主路径改为 GPU depth/color:
  - 用 camera 对 external targets 渲染 depth/color 结果.
  - LiDAR 最终点云从 GPU depth/color 中读取 external hit.
  - 保留与 gsplat first return 的最近距离竞争语义.
- external target 的 static / dynamic 更新策略改为脏标记与分频:
  - static 组不再按每个 LiDAR tick 全量重建.
  - dynamic 组按独立频率或显式脏标记更新.
- 保持既有视觉语义:
  - 不因性能优化而主动降低用户当前屏幕内的点密度观感.
  - `Depth` / `SplatColorSH0` 的颜色语义继续成立.
  - show/hide、scan-only、Play 模式专用隐藏等现有行为继续可用.
- 更新 Inspector、README、CHANGELOG 与自动化测试,覆盖新口径模式、static/dynamic 分组和 GPU external scan 行为.

## Capabilities

### New Capabilities
- `gsplat-lidar-camera-frustum-external-scan`: 定义 RadarScan 如何使用 camera frustum 作为扫描口径,如何把 external targets 拆成 static / dynamic 两组,以及如何通过 GPU depth/color 路径参与 external hit 与最终 LiDAR 粒子显示.

### Modified Capabilities
- (无)

## Impact

- Affected runtime:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
  - `Runtime/Lidar/*`
  - `Runtime/Shaders/GsplatLidar.shader`
  - 可能新增 external depth/color 相关 shader 或 render texture 管理代码
- Affected editor:
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`
- Affected tests/docs:
  - `Tests/Editor/*`
  - `README.md`
  - `CHANGELOG.md`
- Public surface:
  - LiDAR external target 输入将从单一数组扩展为 static / dynamic 两组
  - LiDAR 将新增基于 camera frustum 的扫描口径配置
  - dynamic external targets 将新增独立更新频率配置
