## Why

目前我们已经有 `ParticleDots`(粒子圆点)这种更适合做"点云调试视图"的 RenderStyle,但它仍然是在"直接显示 splat".
当我们希望模拟车载激光雷达(LiDAR)的采集观感时,直接显示 splat 会带来两个问题:

- 扫描线不整齐: splat 空间分布不规则,环线会断续且锯齿.
- 遮挡语义不准: 同一方向上的远近点会同时出现,缺少"第一回波(first return)"的遮挡特征.

我们需要一个可控的 LiDAR 采集显示模式: splat 可以完全不显示,仅作为"环境采样点";最终渲染一套规则的 beam x azimuth 点云,从而获得整齐的 128 线束环线,并具备第一回波遮挡与扫描前沿+余辉的采集感.

## What Changes

- 新增一个可选的"车载 LiDAR 采集显示"(默认关闭,不影响现有渲染与性能):
  - 以某个 `Transform` 作为 LiDAR 原点与朝向(安装高度/俯仰由项目真实摆放决定).
  - 360 度旋转扫描,旋转频率默认 5Hz(1 圈 0.2s).
  - 扫描保留 1 圈数据,并通过亮度调制表现"扫描前沿更亮,后方逐渐变暗(余辉)".
  - 第一回波(遮挡更准确): 对每个 (beam, azimuthBin) 方向仅保留最近距离回波.
  - 最终渲染为"新生成的规则点云"(beam x azimuthBins),而不是直接渲染命中的 splat,以获得更整齐的扫描线.
- 采样分辨率与更新策略(默认值按讨论收敛):
  - `BeamCount=128`,竖直分层环,上少下多(使用可调的上下视场与线束分配,后续可升级为 LUT).
  - `AzimuthBins=2048`.
  - `UpdateHz=10Hz`: 每 0.1s 全量重建一次 range image(方案 X),运行时用扫描头位置做亮度余辉.
- 新增可控参数与 API(方向性,细节在 design/specs 定义):
  - 开关: `EnableLidarScan`,以及"是否隐藏原始 splat 渲染".
  - LiDAR: `LidarOrigin`,`RotationHz`,`UpdateHz`.
  - 分辨率: `AzimuthBins`,`BeamCount`(固定 128 也可,但参数化更利于复用).
  - 竖直视场分布: `UpFovDeg`,`DownFovDeg`,`UpBeams`(上少下多).
  - 点大小: `LidarPointRadiusPixels`(默认 2px,可调).
  - 颜色模式切换:
    - Depth: `DepthNear=1m`,`DepthFar=200m`.
    - SplatColor(SH0): 采集自高斯基元的基础颜色(不评估方向 SH).
  - 扫描余辉: `TrailGamma`/`Intensity` 等可调项.
- Shader/Compute:
  - 增加 range image 计算所需的 compute kernel(第一回波 min 归约).
  - 为 SplatColor 模式提供"minSplatId 与 minRange 对齐"的稳态策略(例如两阶段).
  - 增加 LiDAR 点云的渲染路径(圆点/圆片),并与现有 RenderStyle 保持隔离,避免影响 Gaussian/ParticleDots 的既有行为.
- Editor:
  - 在 Inspector 增加 LiDAR 调参区与颜色模式切换,用于快速验证与调参.
- Tests:
  - 增加最小回归用例,锁定参数校验与 UpdateHz/RotationHz 的时间推进语义(不要求在测试中跑真实 GPU compute).

## Capabilities

### New Capabilities
- `gsplat-lidar-scan-visualization`: 定义 LiDAR 采集显示的行为契约,包括:
  - 第一回波语义(遮挡规则).
  - beam/azimuth 的网格定义与默认参数(128 x 2048).
  - 360 扫描 + 5Hz 旋转 + 10Hz 全量更新的时间语义.
  - 1 圈保留 + 扫描前沿/余辉的亮度调制规则.
  - 颜色模式(Depth / SplatColor(SH0))与默认深度范围(1m..200m).
  - splat 是否可隐藏,以及点大小(px)语义.

### Modified Capabilities
- (无)

## Impact

- 影响的代码区域(预计):
  - `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`: 新增 LiDAR 参数、更新调度(UpdateHz)与渲染门禁(可隐藏 splat).
  - `Runtime/GsplatRendererImpl.cs`: 新增 LiDAR 相关 GPU 资源管理与 draw 提交.
  - `Runtime/Shaders/Gsplat.compute`(或新增 compute 文件): 新增 range image 的归约 kernel.
  - `Runtime/Shaders/*.shader`: 新增 LiDAR 点云渲染 shader(或复用现有 dot 逻辑,但需隔离资源绑定).
  - `Editor/*`: Inspector 增加 LiDAR 参数面板.
  - `Tests/Editor/*`: 最小回归用例.
- 资源与性能:
  - 新增 GPU buffer(例如 128*2048 的 range/id 表),以及点渲染的固定开销(最多 262,144 点).
  - compute 更新频率为 10Hz,单次更新的工作量与 splat 数量近似线性,需要在 design 中给出保守预算与降级策略(例如降低 azBins 或关闭 SplatColor 模式).
