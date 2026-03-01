## 1. API & 参数面板数据模型

- [x] 1.1 为 `GsplatRenderer` 增加 LiDAR 参数字段(Enable, Origin, RotationHz, UpdateHz, AzimuthBins, Beam 分布, DepthNear/Far, 点大小, 颜色模式, TrailGamma, 强度, HideSplats).
- [x] 1.2 为 `GsplatSequenceRenderer` 增加同款 LiDAR 参数字段,并明确"支持/不支持"的差异点(若暂不支持需在 Inspector 显示提示).
- [x] 1.3 增加参数校验与 clamp 规则(NaN/Inf/负数/非法组合回退到安全默认值),并保证默认关闭时不改变旧行为.

## 2. GPU 资源与生命周期

- [x] 2.1 定义 LiDAR range image buffers: `minRangeSqBits(uint)` 与 `minSplatId(uint)` 的尺寸规则(BeamCount*AzimuthBins),并实现创建/释放/重建.
- [x] 2.2 定义并生成 LUT buffers: `azSinCos[AzimuthBins]` 与 `beamSinCos[BeamCount]`,并在参数变化时重建.
- [x] 2.3 增加 LiDAR 渲染所需的材质/MPB 与必绑 buffer 列表,遵循 Metal "必绑资源"稳态原则(每次 draw 前统一绑定).

## 3. Compute: first return range image 生成

- [x] 3.1 在 compute shader 中新增 kernel: ClearRangeImage(填充 INF/默认值).
- [x] 3.2 新增 kernel: ReduceMinRangeSq(遍历 splat,计算 `(beam,azBin)` 并对 `minRangeSqBits` 做 atomic min).
- [x] 3.3 新增 kernel: ResolveMinSplatId(二阶段保证 `minSplatId` 与 `minRangeSqBits` 严格对应,并定义确定性 tie-breaker).
- [x] 3.4 在 Runtime 侧实现 UpdateHz=10 的调度(按 realtime 计时),并确保在 EditMode/PlayMode 下行为一致且可预期.

## 4. 渲染: 规则点云绘制

- [x] 4.1 新增 LiDAR 点云 shader(或新增 pass),支持圆点/圆片绘制,点大小语义为 px radius(默认 2px,可调).
- [x] 4.2 实现每 cell 一个实例的绘制方式(BeamCount*AzimuthBins),vertex 阶段从 range image + LUT 重建世界坐标.
- [x] 4.3 实现颜色模式:
  - Depth: 使用 DepthNear=1m,DepthFar=200m 做 colormap 映射.
  - SplatColor(SH0): 用 `minSplatId` 采样 splat 基础色并做与主 shader 一致的解码.
- [x] 4.4 实现扫描前沿+余辉亮度调制(基于 RotationHz 与 `azBin` 年龄,保留 1 圈观感),并提供 TrailGamma/强度参数.

## 5. splat 隐藏与资源复用

- [x] 5.1 解耦"创建/保持 splat buffers"与"提交 splat sort/draw": `EnableLidarScan` 打开时即使隐藏 splat 仍能采样 Position/Color buffers.
- [x] 5.2 当 `HideSplatsWhenLidarEnabled=true` 时,确保 splat 的 sort/draw 不被提交(避免无意义开销),但 LiDAR 的 compute/draw 正常工作.

## 6. Editor: Inspector 调参与可视化验证

- [x] 6.1 `GsplatRendererEditor` 增加 LiDAR 调参区(开关、Origin、分辨率、FOV 分布、UpdateHz/RotationHz、颜色模式、点大小、余辉参数、隐藏 splat).
- [x] 6.2 `GsplatSequenceRendererEditor` 同步增加 LiDAR 调参区或明确标注暂不支持的原因与下一步.
- [x] 6.3 EditMode 下 LiDAR 扫描前沿需要连续刷新时,增加受控的 Editor repaint 驱动(仅在 EnableLidarScan 时启用).

## 7. Tests & 验证

- [x] 7.1 增加 EditMode tests: 参数校验与默认值(DepthNear/Far,RotationHz/UpdateHz clamp,点大小语义).
- [x] 7.2 增加 EditMode tests: UpdateHz 的调度逻辑(不跑 GPU compute,只验证"是否触发更新"的时间门禁).
- [x] 7.3 手动验证清单: 360 扫描、5Hz 前沿、1 圈余辉、Depth/SplatColor 切换、隐藏 splat 后仍能显示 LiDAR 点云.

## 8. 文档与变更记录

- [x] 8.1 更新 `README.md` 增加 LiDAR 采集显示的用法、参数说明与示例代码.
- [x] 8.2 更新 `CHANGELOG.md` 记录新增 LiDAR 采集显示能力(默认关闭).
