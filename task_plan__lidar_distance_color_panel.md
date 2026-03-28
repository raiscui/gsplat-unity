# 任务计划: 雷达点云距离颜色面板可编辑

## 目标

- 让 RadarScan / LiDAR 的距离着色起始颜色和结束颜色可以在 Inspector 面板里直接看到并编辑, 且运行时渲染正确生效。

## 阶段

- [x] 阶段1: 读取历史经验与相关代码入口
- [x] 阶段2: 建立现象 -> 假设 -> 验证计划
- [ ] 阶段3: 实施 runtime / shader / inspector 改动
- [ ] 阶段4: 编译验证与收尾记录

## 关键问题

1. 当前缺的是 Inspector 绘制, 还是 runtime/shader 本身没有可配置的颜色字段?
2. `GsplatRenderer` 和 `GsplatSequenceRenderer` 是否都需要补齐, 才不会出现一边能调一边不能调?
3. 参数链是否能从序列化字段稳定传到 `GsplatLidarScan.RenderPointCloud(...)` 和 `GsplatLidarPassCore.hlsl`?

## 做出的决定

- 先做静态证据确认, 再修改代码, 避免把“看起来像漏画字段”误判成真实根因。
- 两套 Inspector 一起处理, 保持静态资产和序列资产的调参体验一致。
- 颜色映射改成显式的 start/end color 线性插值, 不保留 shader 侧硬编码渐变作为唯一真相源。

## 状态

**目前在阶段3**
- 已确认当前 Depth 颜色映射在 shader 中硬编码, 不是单纯的 Inspector 漏画。
- 已确认 `GsplatRendererEditor` 与 `GsplatSequenceRendererEditor` 都需要同步补齐。
- 下一步开始改 `GsplatRenderer` / `GsplatSequenceRenderer` / `GsplatLidarScan` / `GsplatLidarPassCore.hlsl` 与两个 Editor。

## [2026-03-28 13:00:50] [Session ID: 019d32c5-3334-71e2-84bc-b7a60390dc20] 阶段进展: runtime / shader / inspector / docs 已落地

- [x] 阶段3: 实施 runtime / shader / inspector 改动
  - 已为 `GsplatRenderer` / `GsplatSequenceRenderer` 新增 `LidarDepthNearColor` / `LidarDepthFarColor`
  - 已把颜色参数接到 `GsplatLidarScan.RenderPointCloud(...)`
  - 已在 `GsplatLidarPassCore.hlsl` 中加入可配置颜色路径
  - 已同步 `GsplatRendererEditor` / `GsplatSequenceRendererEditor`
  - 已更新 `README.md` / `CHANGELOG.md`
- [x] 阶段4: 编译验证与收尾记录
  - 已看到 Unity 项目对当前包路径做了重新解析与 domain reload
  - 已在 `Logs/shadercompiler-UnityShaderCompiler.exe-0.log` 中确认
    - `Packages/wu.yize.gsplat/Runtime/Shaders/GsplatLidar.shader` `preprocess ... ok=1`
    - `Packages/wu.yize.gsplat/Runtime/Shaders/GsplatLidarAlphaToCoverage.shader` `preprocess ... ok=1`
  - 已用日志搜索确认当前 `Logs/` 下未出现新的 `error CS` / `Shader error`
  - 已补齐 `Tests/Editor/GsplatLidarScanTests.cs` 与 `Tests/Editor/GsplatLidarShaderPropertyTests.cs`

## 做出的决定

- 为了避免老场景在“默认值不变”的前提下视觉突然回退, Depth 颜色路径采用兼容策略:
  - 默认 `LidarDepthNearColor=cyan` + `LidarDepthFarColor=red` 时,继续保留历史青 -> 蓝 -> 紫 -> 红色带
  - 用户一旦改动任一颜色,就切到直观的 near -> far 直接插值

## 状态

**目前已完成阶段4**
- 代码、Inspector、shader、测试和文档都已同步。
- 当前未能独立跑 Unity Test Runner:
  - 原因1: 目标工程当前已有打开中的 Unity Editor 进程
  - 原因2: 本机缺少可直接复用的现代 `dotnet` SDK, 且现成 `.csproj` 仍指向旧的 macOS Unity 引用路径
- 但已取得静态证据 + Unity 现场日志证据, 可支持本轮改动已被 Editor 接收且 shader 至少通过预处理。
