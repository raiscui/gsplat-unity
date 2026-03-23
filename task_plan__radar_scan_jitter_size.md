# 任务计划: RadarScan 抖动波纹与小粒径异常

## 目标

- 修复 RadarScan 粒子在启用位置抖动后,高密度场景出现的波纹伪影。
- 修复粒子大小小于 `1` 时的显示异常,让小粒径也能稳定呈现。

## 阶段

- [ ] 阶段1: 读取现有 RadarScan 运行时、shader 和测试代码
- [ ] 阶段2: 建立“现象 -> 假设 -> 验证计划”并找最小可证伪实验
- [ ] 阶段3: 实施修复并补齐回归测试
- [ ] 阶段4: 运行验证,记录结论与后续事项

## 关键问题

1. 波纹是由 CPU 侧抖动分布、shader 中的屏幕空间覆盖率,还是密度叠加后的采样拍频引起?
2. `size < 1` 的异常是点精灵尺寸被 clamp、覆盖率公式不连续,还是 alpha/mask 路径在亚像素区间退化?
3. 能否用同一套修复同时覆盖 `GsplatRenderer` 和 `GsplatSequenceRenderer`,避免两条渲染路径再次分叉?

## 做出的决定

- 方案A(最佳方案): 找到波纹与小粒径异常的共同根因,把粒径映射、抖动分布和 shader 覆盖率统一到连续数学模型上。优点是后续密度、size、抖动强度一起调时更稳。代价是需要同时改 runtime 参数和 shader 逻辑。
- 方案B(先能用方案): 只对当前异常区间做钳制,例如限制抖动幅度或把 `size < 1` 直接提升到安全下限。优点是快。缺点是会牺牲参数真实可调范围,而且高概率只是掩盖症状。
- 当前先按方案A调查,只有证据表明共同根因不存在时,才退回方案B。

## 遇到错误

- 暂无。

## 状态

**目前在阶段2**
- 已完成 RadarScan 运行时、shader、测试与历史支线的首轮静态阅读。
- 下一步先做最小验证,确认“小粒径折叠到 1px”和“warp 与点半径脱钩”是否真的足以解释用户看到的异常。

## [2026-03-23 16:37:02 +0800] [Session ID: 20260323_6] 阶段进展

- [x] 阶段1: 读取现有 RadarScan 运行时、shader 和测试代码
  - 已定位 `Runtime/Shaders/GsplatLidarPassCore.hlsl` 中的粒径覆盖率、show/hide jitter 与屏幕空间 warp 计算。
  - 已定位 `Runtime/Lidar/GsplatLidarScan.cs` 中的 LiDAR 参数下发与输入清洗逻辑。
  - 已定位 `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs` 的默认参数与序列化约束。
  - 已读取 `Tests/Editor/GsplatLidarShaderPropertyTests.cs` 和相关 LiDAR tests,确认当前测试把 `pointRadiusPx = max(_LidarPointRadiusPixels, 1.0)` 视为既有契约。
- [ ] 阶段2: 建立“现象 -> 假设 -> 验证计划”并找最小可证伪实验
  - 当前正在做公式级最小验证,确认用户反馈是否由现有数学模型直接导出。

## [2026-03-23 16:56:29 +0800] [Session ID: 20260323_7] 阶段进展: 根据用户澄清回滚错误假设

- [ ] 阶段2: 建立“现象 -> 假设 -> 验证计划”并找最小可证伪实验
  - 用户已明确说明当前问题不是 `warp` 主导的 show/hide 形变,而是“点分布过于均匀,密度提高后出现摩尔纹”。
  - 上一轮把 `warp` 当成主因的分析不再成立,现在回到 LiDAR 规则栅格本身,重新验证“bin center 过于均匀 -> 高密度拍频/摩尔纹”的候选假设。
  - 下一步先检查当前工作区半成品改动,确认哪些变更应保留,哪些需要撤回,再实施“稳定 cell 内抖动 + subpixel 粒径连续覆盖率”方案。

## 状态

**目前仍在阶段2**
- 已根据用户澄清撤回“`warp` 是主因”的口径。
- 正在核对现有工作区差异,准备把修复收敛到“打散规则栅格 + 修复 `size < 1`”这条线上。

## [2026-03-23 16:56:29 +0800] [Session ID: 20260323_7] 阶段进展: 继续执行下一未完成步骤

- [ ] 阶段3: 实施修复并补齐回归测试
  - 先补 `GsplatLidarScan.cs` 的参数签名、property ID 和 MPB 下发,把 `LidarPointJitterCellFraction` / beam 角域范围真正送到 shader。
  - 再补 `Editor` 与 shader shell 的缺口,让静态/序列两条路径都能一致调参。
  - 最后把 `Tests/Editor/GsplatLidarShaderPropertyTests.cs` 从错误的 `warp cap` 约束改回 “stable in-cell jitter + subpixel radius” 契约。

## [2026-03-23 17:24:59] [Session ID: 20260323_8] 阶段进展: 用户确认 jitter 已可用, 当前聚焦 subpixel 消失问题

- [ ] 阶段2: 建立“现象 -> 假设 -> 验证计划”并找最小可证伪实验
  - 用户已确认 `jitter` 方向可以接受, 当前剩余核心问题收敛为“粒子大小小于 1 时会消失”。
  - 下一步只围绕 subpixel 显示链路做最小验证: 先核对顶点阶段外扩、fragment 覆盖率、clip / alpha-to-coverage 三处是否存在不连续或提前裁剪。
- [ ] 阶段4: 运行验证,记录结论与后续事项
  - 修复后需要重新跑包级编译与相关测试,确认没有把已解决的 jitter 链路打坏。

## 状态

**目前在阶段2**
- 已根据用户最新反馈把问题范围收敛到 `size < 1` 粒子消失。
- 正在读取 shader / render path, 准备做最小证伪实验并定点修复。

## [2026-03-23 17:24:59] [Session ID: 20260323_8] 遇到的验证问题

- `dotnet build Gsplat.Tests.Editor.csproj -v minimal` 在当前包目录下失败,错误为 `MSBUILD : error MSB1009: 项目文件不存在。`
- 该错误属于验证命令路径错误,不是当前代码编译结果。
- 下一步先定位实际 `.csproj` 路径,再继续编译验证,不能把这次失败当作代码已通过。

## [2026-03-23 17:24:59] [Session ID: 20260323_8] 阶段完成: subpixel 消失问题已完成实现与验证

- [x] 阶段2: 建立“现象 -> 假设 -> 验证计划”并找最小可证伪实验
  - 已用最小像素中心采样脚本验证“`<1px` 几何 footprint 在默认 Legacy 路径下可能完全打不到片元”。
  - 已确认真正需要修的是“真实半径”和“raster / coverage 支撑宽度”的分离,而不是把真实半径重新钳回 `1px`。
- [x] 阶段3: 实施修复并补齐回归测试
  - 已在 `Runtime/Shaders/GsplatLidarPassCore.hlsl` 增加 subpixel coverage support 逻辑。
  - 已更新 `Tests/Editor/GsplatLidarShaderPropertyTests.cs` 锁定新契约。
- [x] 阶段4: 运行验证,记录结论与后续事项
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -v minimal` 已通过,`0 warning / 0 error`。
  - Unity 包级 EditMode 测试中,与本次相关的 LiDAR 测试全部 Passed。
  - 仍有 3 个既有失败留在 `GsplatVisibilityAnimationTests`,未见本次 LiDAR 修复扩大失败面。

## 状态

**目前在阶段4(已完成本轮修复验证)**
- `jitter` 相关需求已保留。
- `size < 1` 粒子消失问题已完成修复与验证。
- 下一步如用户需要,可以继续做一轮 Unity 现场视觉 smoke test,专门看 `0.25 / 0.5 / 0.75 / 1.0px` 的实际观感梯度。

## [2026-03-23 17:24:59] [Session ID: 20260323_8] 阶段进展: 解释高密度下空隙与波纹细缝的原因

- [ ] 阶段2: 建立“现象 -> 假设 -> 验证计划”并找最小可证伪实验
  - 用户进一步追问: 为什么粒子密度打高以后,反而会出现大量空隙和波纹细缝。
  - 下一步做一个最小规则点阵 vs 像素中心采样实验,确认这是否属于规则采样与屏幕像素采样之间的拍频 / 摩尔纹问题。

## 状态

**目前在阶段2**
- 正在补充“高密度反而出现细缝”的解释证据。

## [2026-03-23 17:24:59] [Session ID: 20260323_8] 阶段完成: 已补充高密度空隙 / 波纹细缝的原因说明

- [x] 阶段2: 建立“现象 -> 假设 -> 验证计划”并找最小可证伪实验
  - 已补充规则点阵 vs 像素中心采样的最小实验。
  - 已确认“密度更高反而出现细缝”更符合采样拍频 / 摩尔纹解释,不是数据物理上变稀。

## 状态

**目前在阶段4(说明已补齐)**
- 已能解释为什么规则高密度点阵会出现空隙和波纹细缝。

## [2026-03-23 17:24:59] [Session ID: 20260323_8] 阶段进展: 排查是否由 camera depth 采样阶梯导致

- [ ] 阶段2: 建立“现象 -> 假设 -> 验证计划”并找最小可证伪实验
  - 用户提出新假设: 是否因为使用 camera 深度图采样,深度基于 pixel 取样而出现阶梯 / 细缝,并希望在深度采样时做过渡。
  - 下一步先核对当前 external target / camera capture 的真实实现链路,确认是否真的存在“逐像素深度图取样 -> 直接重建 LiDAR 点”的路径。

## 状态

**目前在阶段2**
- 正在排查 camera depth texture 是否参与了当前细缝问题。

## [2026-03-23 17:24:59] [Session ID: 20260323_8] 阶段进展: 为方案1创建 OpenSpec change

- [ ] 阶段1: 计划和设置
  - 用户要求创建 OpenSpec change,目标是“方案1: external capture 超采样降低 depth 台阶”。
  - 下一步按 OpenSpec new-change 流程创建 change,查看 artifact 状态,再给出第一个 artifact 模板。

## 状态

**目前在阶段1**
- 正在创建方案1对应的 OpenSpec change。

## [2026-03-23 17:24:59] [Session ID: 20260323_8] 阶段完成: OpenSpec change 已创建并确认首个 artifact

- [x] 阶段1: 计划和设置
  - 已创建 OpenSpec change: `lidar-external-capture-supersampling`
  - workflow/schema: `spec-driven`
  - 当前进度: `0/4 artifacts complete`
  - 首个 ready artifact: `proposal`

## 状态

**目前在阶段1(已完成 change 创建)**
- 正在读取 proposal 的官方模板与说明。
