# 任务计划: LiDAR 高密度扫描边缘锯齿成因分析

## [2026-03-16 21:54:40] [Session ID: 99992] 新任务: LiDAR 高密度扫描边缘锯齿成因分析

## 目标

- 弄清楚提高 `LidarAzimuthBins` 与 `LidarBeamCount` 后,为何扫描结果 mesh 边缘仍出现明显锯齿。
- 区分这是采样离散化、mesh 重建拓扑、命中精度限制,还是某个优化开关导致的结果。
- 给出基于代码证据的解释,避免把未验证的猜测说成结论。

## 阶段

- [ ] 阶段1: 读取 LiDAR / RadarScan 参数与输出链路
- [ ] 阶段2: 建立现象 -> 假设 -> 验证计划
- [ ] 阶段3: 用静态证据与必要命令验证假设
- [ ] 阶段4: 汇总结论与后续建议

## 关键问题

1. `LidarAzimuthBins` 与 `LidarBeamCount` 实际控制的是射线数量、角分辨率,还是后续 mesh 顶点密度?
2. 扫描结果的边缘形状,是由 hit point 的离散采样决定,还是由三角拼接规则放大出来的?
3. 当前实现里是否存在为性能做的量化、降采样、分辨率限制或深度精度压缩?

## 做出的决定

- 先读代码与已有测试,再下判断,不直接把"采样精度优化"当成根因。
- 优先找出 LiDAR 命中数据如何变成 mesh,因为边缘锯齿通常出现在这个环节。

## 状态

**目前在阶段1**
- 正在读取 `LidarAzimuthBins`、`LidarBeamCount` 的定义与使用点。
- 下一步会用结构化搜索把参数流向和 mesh 生成路径串起来。

## [2026-03-16 21:56:20] [Session ID: 99992] 阶段进展: 开始第一轮证据收集

- 当前动作:
  - 搜索 `LidarAzimuthBins`、`LidarBeamCount`、`RadarScan`、`Lidar` 相关实现与测试。
  - 优先确认参数定义位置、调度 compute / CPU 构网位置、以及 mesh 拓扑生成点。
- 为什么先做这个:
  - 用户问的是"为什么高密度下边缘仍锯齿",这通常不是单一参数问题,而是参数 -> 命中点 -> mesh 重建三段链路共同决定。

## [2026-03-16 22:02:10] [Session ID: 99992] 阶段进展: 结论已建立

## 阶段

- [x] 阶段1: 读取 LiDAR / RadarScan 参数与输出链路
- [x] 阶段2: 建立现象 -> 假设 -> 验证计划
- [x] 阶段3: 用静态证据与必要命令验证假设
- [ ] 阶段4: 汇总结论与后续建议

## 做出的决定

- 已确认当前主假设成立:
  - 该模式本质是规则角度栅格的 LiDAR 点云,不是连续曲面重建。
  - 锯齿主因是离散 cell 采样 + 屏幕空间方点绘制,不是某个隐藏的"采样精度优化"把边缘做坏。
- 需要在交付里额外提醒两个容易被忽略的事实:
  - `LidarBeamCount` 运行时会 clamp 到 `512`
  - `CameraFrustum` 模式下 active bins / beams 会按视场缩放

## 状态

**目前在阶段4**
- 已完成代码链路阅读与量化换算。
- 正在整理面向用户的最终解释,并区分"已确认结论"和"还需现场证据的部分"。

## [2026-03-16 22:03:40] [Session ID: 99992] 阶段进展: 收尾完成

## 阶段

- [x] 阶段1: 读取 LiDAR / RadarScan 参数与输出链路
- [x] 阶段2: 建立现象 -> 假设 -> 验证计划
- [x] 阶段3: 用静态证据与必要命令验证假设
- [x] 阶段4: 汇总结论与后续建议

## 状态

**目前已完成阶段4**
- 本次分析已完成,未进行代码修改。
- 已把量化证据写入 `notes__lidar_edge_aliasing.md`,并把候选改进方向写入 `LATER_PLANS__lidar_edge_aliasing.md`。
- 本轮未发现需要单独升级到 `EPIPHANY_LOG__lidar_edge_aliasing.md` 的架构级灾难项。

## [2026-03-16 22:11:50] [Session ID: 99992] 新增跟进: 核对 `beam=512` 上限与 `CameraFrustum` active 分辨率缩放

## 目标

- 把用户选中的第 2 条继续分析做实:
  - `LidarBeamCount` 的 `512` 上限是不是一定会生效
  - `CameraFrustum` 模式会怎样缩放 active bins / active beams
- 给出一个可以直接拿来判断现场配置的口径

## 阶段

- [x] 阶段5: 读取 active count 缩放与 clamp 细节
- [x] 阶段6: 形成现场判定规则并回复用户

## 状态

**目前在阶段5**
- 正在追 `ScaleCountKeepingDensity(...)` 与 frustum active count 的具体公式。

## [2026-03-16 22:15:10] [Session ID: 99992] 阶段进展: 判定公式已确认

- 已确认:
  - `LidarBeamCount > 512` 的 clamp 只作用于基础输入值
  - `CameraFrustum` 下最终 active beam 仍可因 `camera.fieldOfView` 相对 baseline span 更大而上升
  - `CameraFrustum` 下 `activeAzimuthBins` 一定小于基础 `LidarAzimuthBins`
- 证据来源:
  - `Runtime/Lidar/GsplatLidarScan.cs`
  - `Tests/Editor/GsplatLidarScanTests.cs`
- 下一步:
  - 将这些公式翻译成用户可直接套用的现场判断规则

## 状态

**目前在阶段6**
- 正在组织最终答复。

## [2026-03-16 22:16:05] [Session ID: 99992] 阶段进展: 本轮跟进完成

## 状态

**目前已完成阶段6**
- 已补齐 `beam=512` 与 `CameraFrustum active count` 的判定规则。
- 已写入 `notes__lidar_edge_aliasing.md` 与 `WORKLOG__lidar_edge_aliasing.md`。
- 本轮无新增代码修改,无新增 `EPIPHANY` 项。

## [2026-03-16 22:16:37] [Session ID: 99992] 新增跟进: 基于用户截图做现场现象核对

## 阶段

- [x] 阶段7: 基于现场截图核对“横向 active bins 不够”与“方点扫描线观感”的主次

## [2026-03-16 22:23:40] [Session ID: 99992] 新增跟进: 回答“采样精度能否提高”

## 阶段

- [x] 阶段8: 评估当前实现可提升的精度层级与对应代价
- [ ] 阶段9: 给出最佳方案与先能用方案

## 状态

**目前在阶段9**
- 已锁定 `CameraFrustum + external mesh GPU depth capture` 为当前主路径。
- 正在整理最终建议,区分“继续沿用当前语义做增强”和“换成更接近 mesh 的表示”。

## [2026-03-16 22:31:40] [Session ID: 99992] 阶段进展: 开始实现“最佳方案”

- 决策:
  - 不只做一个单一倍率字段。
  - 直接实现 `Auto / Scale / Explicit` 三态 external capture 分辨率控制。
- 预期改动面:
  - `GsplatUtils.cs`: 新增分辨率模式枚举
  - `GsplatRenderer.cs` / `GsplatSequenceRenderer.cs`: 新增序列化字段与防御性 clamp
  - `GsplatLidarExternalGpuCapture.cs`: 接入新的 capture 尺寸解析逻辑
  - `GsplatRendererEditor.cs` / `GsplatSequenceRendererEditor.cs`: 暴露 Inspector
  - `Tests/Editor/GsplatLidarExternalGpuCaptureTests.cs`: 增加 capture size 解析测试
  - `README.md` / `CHANGELOG.md`: 同步文档

## 状态

**目前在实现阶段**
- 正在执行代码修改。

## [2026-03-16 22:36:40] [Session ID: 99992] 阶段进展: 继续实现 external capture 分辨率控制

- 当前动作:
  - 回读已改 runtime 字段与待改 capture / editor / tests / docs 文件。
  - 先补 `GsplatLidarExternalGpuCapture.cs` 的尺寸解析与签名,再补 Inspector、测试和文档。
- 为什么先做这个:
  - 用户要的是"最佳方案"且要"精度可控"。
  - 只有 capture 侧真正支持 `Auto / Scale / Explicit`,前面的字段暴露才不是空壳。

## 状态

**目前继续处于实现阶段**
- 当前重点是把 external GPU capture 分辨率控制真正打通到运行链路。

## [2026-03-16 22:43:34] [Session ID: 99992] 阶段进展: 验证遇到 Unity 项目锁,切换到临时克隆工程

- 已验证到的现象:
  - `dotnet build ../../Gsplat.csproj -nologo` 成功,0 error.
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 成功,0 error.
  - 直接跑 Unity EditMode tests 时, batchmode 报"另一个 Unity 实例正在打开此项目".
- 当前决策:
  - 不中断验证.
  - 改为复制 `Assets / Packages / ProjectSettings` 到临时工程,在克隆工程继续跑同一组测试,避免项目锁影响结论。

## 状态

**目前在验证阶段**
- 正在准备临时克隆工程,继续获取运行时测试证据。

## [2026-03-16 22:51:03] [Session ID: 99992] 阶段进展: 最佳方案实现并验证完成

## 阶段

- [x] 阶段8: 评估当前实现可提升的精度层级与对应代价
- [x] 阶段9: 给出最佳方案与先能用方案

## 做出的决定

- 已正式落地最佳方案:
  - 对 `CameraFrustum + external GPU capture` 增加 `Auto / Scale / Explicit` 三态 capture 分辨率控制。
- 保持默认兼容:
  - 默认 `Auto` 不改变旧项目行为。
  - 需要更高精度时,再切到 `Scale` 或 `Explicit`。

## 状态

**目前已完成本轮实现与验证**
- 已完成 runtime、Inspector、测试、README、CHANGELOG 同步。
- `dotnet build ../../Gsplat.csproj -nologo` 成功。
- `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 成功。
- 在临时克隆工程中, `Gsplat.Tests.GsplatLidarExternalGpuCaptureTests` 共 11 项全部通过。

## [2026-03-16 22:53:03] [Session ID: 99992] 收尾: 清理测试工程旧 warning

- 额外完成:
  - 清理 `Gsplat.Tests.Editor.csproj` 中 3 个旧 warning。
  - 重新执行 `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`,结果为 0 warning / 0 error。
