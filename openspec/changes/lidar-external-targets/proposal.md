## Why

当前 RadarScan(LiDAR) 只能扫描 gsplat 自己的点数据,无法把场景里额外摆放的三维模型一起纳入 first return 结果. 这会让“雷达扫描结果”和真实场景结构脱节: 用户明明在场景里放了车、路牌、角色或其它 mesh,雷达粒子却完全打不到它们。

现在需要把 `GameObject[]` 外部目标正式纳入 RadarScan,让 gsplat 与外部三维模型共享同一套扫描语义. 这样用户才能用一个组件同时扫描“高斯环境 + 常规 Unity 模型”,并得到更接近真实传感器的遮挡与命中结果。

## What Changes

- 为 `GsplatRenderer` 和 `GsplatSequenceRenderer` 新增 LiDAR 外部目标数组配置:
  - `LidarExternalTargets : GameObject[]`
  - 数组中的对象及其子层级 mesh 将参与 RadarScan.
- 为外部目标新增普通 mesh 可见性模式:
  - 默认让 external targets 以“只参与扫描,不显示普通 mesh”的方式工作.
  - 同时保留显式切回“继续显示普通 mesh”的能力.
  - 新增“只在 Play 模式隐藏,编辑器平时仍显示”的折中模式.
- 新增“外部模型参与 first return”能力:
  - 外部模型与 gsplat 命中结果按每个 `(beam, azBin)` 竞争最近距离.
  - 谁更近,最终雷达粒子就显示谁.
- 新增“真实 mesh 参与扫描”语义:
  - 静态模型使用真实 mesh.
  - `SkinnedMeshRenderer` 使用烘焙后的真实 mesh 快照.
  - 不使用球体/胶囊等近似碰撞体替代真实外形.
- 扩展 RadarScan 颜色语义:
  - `Depth` 模式下,外部模型命中与 gsplat 一样使用深度色.
  - `SplatColorSH0` 模式下,gsplat 命中继续用 SH0 颜色,外部模型命中使用材质主色.
- 扩展 RadarScan 的显隐覆盖范围:
  - show/hide 的 bounds、最大半径与可见性覆盖需要同时考虑 gsplat 与外部模型的联合范围.
- 补充 Inspector、文档与测试:
  - 面板增加 `LidarExternalTargets`
  - README/CHANGELOG/OpenSpec 同步更新
  - 增加外部命中、材质颜色与 bounds 联合行为的自动化回归

## Capabilities

### New Capabilities
- `gsplat-lidar-external-targets`: 定义 RadarScan 如何接纳 `GameObject[]` 外部模型,包括外部目标收集、真实 mesh 命中、与 gsplat 的 first return 竞争、外部命中着色与联合 bounds 语义.

### Modified Capabilities
- (无)

## Impact

- Affected runtime:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
  - `Runtime/Lidar/*`
  - `Runtime/Shaders/GsplatLidar.shader`
- Affected editor:
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`
- Affected tests/docs:
  - `Tests/Editor/*`
  - `README.md`
  - `CHANGELOG.md`
- Public surface:
  - 两个 Renderer 会新增 `LidarExternalTargets : GameObject[]` 序列化字段
  - 两个 Renderer 会新增 external target 普通 mesh 可见性模式字段
  - RadarScan 的 `SplatColorSH0` 模式将新增“外部模型命中使用材质主色”的可观察行为
