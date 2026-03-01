## Context

本 change 要在 `ParticleDots`(粒子圆点)能力基础上,增加一个"车载 LiDAR 采集显示"模式.
它的目标不是把 splat 当作 LiDAR 点直接画出来,而是:

1. splat 仅作为"环境采样点"(可完全不显示).
2. GPU compute 生成规则的 range image(beam x azimuthBin),并具备第一回波(first return)遮挡语义.
3. 渲染阶段从 range image 重建出规则点云(最多 128x2048=262,144 点),形成整齐的 128 线束环线.

已收敛的默认参数:

- 360 度扫描.
- `RotationHz=5`(1 圈 0.2s).
- `UpdateHz=10`(每 0.1s 全量重建 range image,方案 X).
- `BeamCount=128`,`AzimuthBins=2048`.
- 保留 1 圈数据,且有扫描前沿+余辉(扫描头更亮,后方逐渐变暗).
- 颜色模式:
  - Depth: `DepthNear=1m`,`DepthFar=200m`.
  - SplatColor: 取 splat 的基础颜色(SH0).
- 点大小: 默认 2px,可调.
- LiDAR 安装姿态: 由 `LidarOrigin Transform` 的真实摆放决定(位置+朝向).

约束与现状:

- 当前 splat 渲染主链路为: `Gsplat.compute` 计算排序 key -> radix sort -> `Gsplat.shader` 绘制.
- 数据源为 GPU `GraphicsBuffer`(Position/Color/SH...),其坐标通常在 renderer 的 model space.
- 包是 UPM package,需要保持默认行为不变,且不引入外部依赖.
- 我们在 macOS/Metal 上已经对"StructuredBuffer 必绑"做过稳态修复,新增渲染路径也必须遵循同样的绑定原则.

## Goals / Non-Goals

**Goals:**

- 提供一个可选的 LiDAR 采集显示模式(默认关闭),不改变现有 Gaussian/ParticleDots 的渲染表现与性能.
- 第一回波遮挡:
  - 对每个 `(beamIndex, azimuthBin)` 方向只保留最近距离的回波.
- 规则点云渲染:
  - 不直接渲染命中的 splat,而是渲染规则网格重建出来的点,保证扫描线整齐.
- 支持 2 种颜色模式:
  - Depth(1m..200m).
  - SplatColor(SH0),并保证颜色与 first return 的距离严格对应(避免偶发跳色).
- 支持 1 圈保留 + 扫描前沿/余辉的采集感:
  - 余辉由当前扫描头与 azimuthBin 的相对"年龄"决定,无需额外历史 buffer.
- 允许"隐藏 splat 渲染但保留 LiDAR":
  - 用户可完全不显示 splat,只显示 LiDAR 点云,同时保留采样所需的 GPU buffers.

**Non-Goals:**

- 不实现真实物理 `Physics.Raycast` 与 mesh/collider 碰撞.
- 不实现多回波(multi-return)、强度回波模型、噪声/掉点/雨雾等高级传感器仿真.
- 不要求在单元测试中真实跑 GPU compute 并验证图像结果(测试只锁定参数校验与时间语义).
- 不实现 VFX Graph 后端的同款 LiDAR(可作为二期扩展).

## Decisions

### 1) 用"range image + first return 归约"而不是"纯视觉扫描带"

**Decision:**

- 用 compute 生成 `minRangeSq` 表格(128x2048),作为第一回波(遮挡)的事实来源.

**Alternatives considered:**

- A) shader 纯程序化扫描带(无状态):
  - 优点: 实现快.
  - 缺点: 同方向远近点无法遮挡,不满足第一回波语义.
- B) CPU 侧 raycast/BVH:
  - 优点: 语义直观.
  - 缺点: 需要构建加速结构/大量 CPU 开销,且 splat 并非真实表面,命中定义困难.

**Why:**

- first return 本质是"按方向做最小距离归约",非常适合 GPU 并行归约.
- 生成 range image 后,扫描线的整齐度与密度可控(与 splat 分布解耦).

### 2) 更新策略选方案 X: 10Hz 全量重建,扫描前沿用亮度表达

**Decision:**

- `UpdateHz=10` 时全量重建一次 360 range image.
- 采集感(扫描前沿+余辉)在渲染阶段用亮度调制实现:
  - `scanHeadBin = floor(frac(time*RotationHz) * AzimuthBins)`
  - `deltaBins = (scanHeadBin - azBin + AzimuthBins) % AzimuthBins`
  - `trail = pow(1 - deltaBins/AzimuthBins, TrailGamma)`

**Alternatives considered:**

- 按扫描头逐列更新 azBin:
  - 优点: 更"采集式".
  - 缺点: 若仍需遍历全部 splat,收益有限且复杂度更高.

**Why:**

- 10Hz 全量更新可控且实现简单,足以满足"采集观感".
- 渲染阶段的亮度余辉不需要额外历史 buffer,也能保持 1 圈保留的直觉语义.

### 3) first return 归约使用 `rangeSq` 的 uint bit 表示,用 atomic min 保证跨平台

**Decision:**

- range 表存 `rangeSqBits(uint)`:
  - compute 写入/归约使用 `InterlockedMin`.
  - shader 读取后再 `asfloat` 还原.

**Why:**

- 原子 min 对 float 的支持在不同平台/Unity 版本中差异较大.
- `rangeSq` 为非负数,其 float bit 序与数值大小一致(可用于 min 归约).
- 省掉 `sqrt` 的归约阶段开销,只在渲染阶段还原 `range = sqrt(rangeSq)`.

### 4) SplatColor(SH0) 采用两阶段,保证 `minRange` 与 `minSplatId` 稳态对应

**Decision:**

- Pass1: 只归约 `minRangeSqBits`.
- Pass2: 再遍历 splat,只有 `rangeSqBits == minRangeSqBits[cell]` 的候选才写 `minSplatId[cell]`.
  - 多候选时取最小 id(确定性).

**Why:**

- 单 pass 同时写 minRange 与 id,在并行竞争下容易出现"距离更新了但 id 没跟上"的偶发错配.
- 两阶段虽然多一次遍历,但 UpdateHz=10 的预算下更稳,也更容易调试.

### 5) 竖直线束分布采用"上少下多"的参数化映射,并提供后续 LUT 升级点

**Decision:**

- v1 使用参数化分布:
  - `DownFovDeg=-30`,`UpFovDeg=+10`
  - `DownBeams=112`,`UpBeams=16`
- elevation -> beamIndex 采用直接分段映射(无搜索),并剔除超出 FOV 的点.

**Why:**

- 参数化分布足够像车载,且易于调参.
- 保留 LUT 升级点(未来可直接用 128 条固定俯仰角表).

### 6) 渲染路径选择: 以"每个 cell 一个实例"绘制,方向使用 LUT 避免大量 sin/cos

**Decision:**

- 渲染点数固定为 `BeamCount*AzimuthBins`.
- 使用两个小 LUT buffer:
  - `azSinCos[AzimuthBins] = (sin(az), cos(az))`
  - `beamSinCos[BeamCount] = (sin(el), cos(el))`
- vertex shader 基于 `(beamIndex, azBin)` + range 重建 `dirLocal`,再由 `LidarLocalToWorld` 转到世界坐标绘制圆点.

**Alternatives considered:**

- 每点在 shader 内直接调用 `sin/cos`:
  - 简单但会在 262k 点规模下带来明显 ALU 压力.
- compute 预生成完整 `posWorld` buffer:
  - 会增加显存与带宽,且对颜色模式切换不够灵活.

**Why:**

- LUT 占用极小(2048 个 float2 + 128 个 float2),但能显著降低每点的 trig 计算.
- 不生成 pos buffer,只存 range/id,资源更紧凑.

### 7) "隐藏 splat 但保留 LiDAR"通过解耦资源创建与 draw/sort 门禁实现

**Decision:**

- 将"需要创建/保持 splat GPU buffers"的条件扩展为:
  - `NeedSplatBuffers = EnableGsplatBackend || EnableLidarScan || (其它依赖这些 buffers 的功能)`
- draw/sort 门禁仍由 `EnableGsplatBackend` 决定,并新增一个显式选项:
  - `HideSplatsWhenLidarEnabled`(或等价语义),允许 LiDAR 打开时不提交 splat 的 sort/draw.

**Why:**

- 用户明确希望 splat 可以完全不显示.
- 但 LiDAR 仍需要读取 Position/Color 等 buffers,不能简单把后端关死.
- 复用现有 buffers 能避免重复占用显存与重复上传.

## Risks / Trade-offs

- [风险] first return 的 atomic min 归约在 splat 数量很大时仍可能吃满 GPU.
  - 缓解: 通过 `UpdateHz` 节流(10Hz),并提供降级策略(降低 `AzimuthBins`,或只启用 Depth 模式跳过 Pass2).
- [风险] 新增 LiDAR 渲染路径需要额外 buffers 与材质,可能在 Metal 下触发"未绑定 StructuredBuffer 跳绘制".
  - 缓解: 参照现有 `GsplatRendererImpl` 的"必绑资源"策略,把 LiDAR shader 声明的 buffers 视为必绑,每次 draw 前统一绑定.
- [风险] EditMode 下如果不主动 Repaint,扫描前沿动画会"鼠标不动就不动".
  - 缓解: 仅在 `EnableLidarScan` 时注册 Editor ticker,以受控频率请求 `RepaintAllViews`.
- [取舍] v1 不做真实厂商 beam LUT,而用参数化分布近似.
  - 缓解: 保留 LUT 扩展点,不锁死数据结构.

## Migration Plan

- 默认 `EnableLidarScan=false`,不影响现有项目.
- 若未来需要调整默认参数(例如 FOV/UpBeams),仅影响新创建的组件默认值,不主动迁移旧场景序列化数据.

## Open Questions

- 是否需要为 `GsplatSequenceRenderer` 提供同款 LiDAR(它的 buffers 生命周期与 decode 链路不同),还是先只覆盖 `GsplatRenderer`.
- Depth colormap 采用简单渐变还是内置 Turbo/Jet,以及是否需要用户可自定义渐变色带.
