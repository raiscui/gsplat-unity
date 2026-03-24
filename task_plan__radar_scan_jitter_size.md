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

## [2026-03-23 21:02:50 +0800] [Session ID: 20260323_9] 阶段进展: 继续执行 openspec-ff-change

- [x] 阶段1: 计划和设置
  - 已确认本轮继续沿用已创建的 OpenSpec change: `lidar-external-capture-supersampling`。
  - 已确认当前 artifact 状态:
    - `proposal = done`
    - `design = ready`
    - `specs = ready`
    - `tasks = blocked by design/specs`
- [ ] 阶段2: 研究/收集信息
  - 已读取 `openspec instructions design/specs` 的官方约束。
  - 已读取相近 change:
    - `lidar-camera-frustum-external-gpu-scan`
    - `lidar-external-targets`
  - 下一步先把 research 结论落到 `notes__radar_scan_jitter_size.md`,再一次性生成 `design.md` 与 `spec.md`。
- [ ] 阶段3: 执行/构建
  - 在 `design/specs` 完成后,继续生成 `tasks.md`。
- [ ] 阶段4: 审查和交付
  - 最后重新跑 `openspec status`,确认该 change 进入 apply-ready。

## 状态

**目前在阶段2**
- 正在把方案1的 OpenSpec 研究结论写入支线上下文。
- 下一步是正式落 `design.md` 和 capability `spec.md`。

## [2026-03-23 21:14:02 +0800] [Session ID: 20260323_9] 阶段完成: OpenSpec change 已快进到 apply-ready

- [x] 阶段2: 研究/收集信息
  - 已完成 `design/specs/tasks` 的官方模板与相近 change 参考阅读。
  - 已把 external capture supersampling 的静态证据和设计边界追加到 `notes__radar_scan_jitter_size.md`。
- [x] 阶段3: 执行/构建
  - 已创建 `openspec/changes/lidar-external-capture-supersampling/design.md`。
  - 已创建 `openspec/changes/lidar-external-capture-supersampling/specs/gsplat-lidar-external-capture-quality/spec.md`。
  - 已创建 `openspec/changes/lidar-external-capture-supersampling/tasks.md`。
- [x] 阶段4: 审查和交付
  - 已执行 `openspec status --change "lidar-external-capture-supersampling"`。
  - 结果为 `4/4 artifacts complete`, change 已进入 apply-ready。

## 状态

**目前在阶段4(本轮 openspec-ff-change 已完成)**
- 方案1对应的 OpenSpec change 已经具备 `proposal/design/specs/tasks` 全套 artifact。
- 下一步如继续,就可以直接进入实现阶段,例如 `/opsx:apply` 或继续让我按 tasks 落代码。

## [2026-03-23 22:07:39 +0800] [Session ID: 20260323_10] 阶段进展: 开始实施 `lidar-external-capture-supersampling`

- [ ] 阶段3: 执行/构建
  - 已按 `openspec instructions apply` 确认当前 schema=`spec-driven`, apply state=`ready`。
  - 当前进度为 `0/13 tasks complete`。
  - 下一步先核对 runtime / editor / tests / docs 的现状,分辨哪些任务已被现有代码部分覆盖,哪些仍需要真正补实现。
- [ ] 阶段4: 运行验证,记录结论与后续事项
  - 本轮需要在代码落地后更新 `openspec` task checkbox,并跑相关 EditMode 验证。

## 状态

**目前在阶段3**

## [2026-03-24 12:26:48 +0800] [Session ID: 20260324_8] 阶段进展: 继续收口 `lidar-external-hybrid-resolve` 的 4.4 验证

- [ ] 阶段4: 运行验证,记录结论与后续事项
  - 当前继续沿用 OpenSpec change: `lidar-external-hybrid-resolve`。
  - 代码与 `dotnet build ../../Gsplat.Tests.Editor.csproj -v minimal` 的静态编译验证已经通过,但 `tasks.md` 的 `4.4` 仍未完成。
  - 当前主阻塞不是代码编译错误,而是 Unity Editor 侧持续返回 `tests_running`,需要先确认这是已有测试未结束,还是状态卡住未释放。
  - 下一步先读取 Unity 当前 editor/test 状态,若已可运行,立即执行 `Gsplat.Tests.Editor` 下的相关 EditMode 测试并据结果更新 OpenSpec task。

## 状态

**目前在阶段4**
- 正在处理 `lidar-external-hybrid-resolve` 的最终验证收尾。
- 下一步先确认 Unity Editor 是否还处于 `tests_running` 占用状态。

## [2026-03-24 11:50:19 +0800] [Session ID: unknown] 阶段进展: 一次性补齐方案2 hybrid resolve artifacts

- [x] 阶段1: 计划和设置
  - 已确认继续沿用支线上下文 `__radar_scan_jitter_size`。
  - 已确认当前 OpenSpec change 为 `lidar-external-hybrid-resolve`。
  - 已重新读取 `proposal.md`、`openspec status` 与 `openspec instructions design/specs/tasks`。
- [ ] 阶段2: 研究/收集信息
  - 已收敛本轮必须写死的设计边界:
    - `LidarExternalEdgeAwareResolveMode = Off / Kernel2x2 / Kernel3x3`
    - `LidarExternalSubpixelResolveMode = Off / Quad4`
    - 两者都开时顺序固定为“subpixel candidate -> edge-aware neighborhood resolve -> final nearest winner”
    - edge-aware 过滤失败时回退中心 point sample
    - color 跟随最终 depth winner,不做独立平均
  - 下一步直接完成 `design.md` 与 capability `spec.md`。
- [ ] 阶段3: 执行/构建
  - 在 `design/specs` 完成后,继续生成 `tasks.md`,把 API、compute resolve、Inspector、测试与文档拆成可执行 checklist。
- [ ] 阶段4: 审查和交付
  - 最后执行 `openspec status --change "lidar-external-hybrid-resolve"`。
  - 目标是让 change 进入 `apply-ready`。

## 状态

**目前在阶段2**
- 方案2的口径已经收敛,不再需要继续 explore。
- 正在正式落 `design.md`、`spec.md` 与 `tasks.md`。

## [2026-03-24 11:53:09 +0800] [Session ID: unknown] 阶段完成: 方案2 OpenSpec artifacts 已一次性补齐

- [x] 阶段2: 研究/收集信息
  - 已把方案2的能力边界、组合顺序、回退语义和公开 API 形态写入 `design.md` 与 `spec.md`。
- [x] 阶段3: 执行/构建
  - 已创建:
    - `openspec/changes/lidar-external-hybrid-resolve/design.md`
    - `openspec/changes/lidar-external-hybrid-resolve/specs/gsplat-lidar-external-hybrid-resolve/spec.md`
    - `openspec/changes/lidar-external-hybrid-resolve/tasks.md`
- [x] 阶段4: 审查和交付
  - 已执行 `openspec status --change "lidar-external-hybrid-resolve" --json`。
  - 验证结果:
    - `proposal = done`
    - `design = done`
    - `specs = done`
    - `tasks = done`
  - 当前 change 已进入 apply-ready。

## 状态

**目前在阶段4(本轮 OpenSpec artifact 快进已完成)**
- `lidar-external-hybrid-resolve` 已具备完整的 `proposal/design/specs/tasks`。
- 下一步可以直接进入实现阶段,按 `tasks.md` 落代码与测试。

## [2026-03-24 11:59:27 +0800] [Session ID: unknown] 阶段进展: 开始实施 `lidar-external-hybrid-resolve`

- [ ] 阶段3: 执行/构建
  - 已确认本轮正式进入 `openspec-apply-change` 流程。
  - 当前 change: `lidar-external-hybrid-resolve`
  - 下一步先读取 `openspec instructions apply`、proposal/design/specs/tasks 和当前 runtime/editor/tests 代码入口。
  - 目标是先确认现有代码结构能否直接承接:
    - 新增两个 resolve mode 的 public API
    - `Gsplat.compute` 的 hybrid candidate resolve 流程
    - Inspector、README、CHANGELOG 与测试矩阵
- [ ] 阶段4: 运行验证,记录结论与后续事项
  - 代码落地后需要更新 `tasks.md` checkbox,并跑编译与相关 EditMode 测试。

## 状态

**目前在阶段3**
- 已从 artifact 创建切到真正实现阶段。
- 正在读取 apply 指引与代码现状,准备拆出第一批可直接落地的任务。

## [2026-03-24 12:00:00 +0800] [Session ID: unknown] 阶段进展: hybrid resolve 主体实现已落地,验证被现有 Unity 实例占用阻塞

- [x] 阶段3: 执行/构建
  - 已完成 public API:
    - `GsplatLidarExternalEdgeAwareResolveMode`
    - `GsplatLidarExternalSubpixelResolveMode`
  - 已完成 `GsplatRenderer` / `GsplatSequenceRenderer` 的默认值、sanitize 与 Inspector 接线。
  - 已完成 `GsplatLidarExternalGpuCapture` 的参数链与 mode-change re-resolve 触发。
  - 已完成 `Gsplat.compute` 的 hybrid resolve 主体:
    - deterministic `Quad4`
    - `Kernel2x2 / Kernel3x3` edge-aware neighborhood resolve
    - final nearest winner
    - color follows winner
  - 已完成 `README.md` / `CHANGELOG.md` 与相关 EditMode 测试补充。
- [ ] 阶段4: 运行验证,记录结论与后续事项
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -v minimal` 已通过,`0 warning / 0 error`。
  - Unity CLI `-runTests` 被现有打开的 Unity 实例占用阻塞,不是代码报错。
  - Unity MCP 已连接到实例 `st-dongfeng-worldmodel@717de14b`,但 `run_tests` 持续返回 `tests_running`,当前无法拿到新的 EditMode 执行结果。

## 状态

**目前在阶段4(实现已完成,最终 Unity 跑测被阻塞)**
- 代码、文档和测试已补齐。
- 当前唯一未闭环项是拿到一次新的 Unity EditMode 执行结果。

## [2026-03-24 12:25:36 +0800] [Session ID: unknown] 阶段进展: 继续处理 `4.4` Unity 跑测阻塞

- [ ] 阶段4: 运行验证,记录结论与后续事项
  - 当前唯一未完成项仍是 `openspec tasks 4.4`。
  - 已知现象:
    - Unity CLI 直接跑 `-runTests` 时,报“另一个 Unity 实例正在打开该项目”。
    - Unity MCP 已连到现有实例,但 `run_tests` 连续返回 `tests_running`。
  - 下一步先确认这是不是“现有测试任务仍在跑 / 卡住”,还是“编辑器刚 recompile 完仍未释放测试状态”。
  - 若能接管现有测试任务或等到空闲,就继续跑目标测试组并收证据。
  - 若确认短时间内无法解锁,则要把阻塞原因、已完成验证和剩余风险明确写回记录。

## 状态

**目前仍在阶段4**
- 正在专门处理 Unity 测试占用问题。
- 目标是不留模糊口径,而是拿到明确的“可跑 / 不可跑”证据。
- 正在实施 OpenSpec change: `lidar-external-capture-supersampling`。
- 当前先做静态差距核对,然后按 tasks 顺序落代码与文档。

## [2026-03-23 22:07:39 +0800] [Session ID: 20260323_10] 阶段完成: supersampling 方案1已完成落地与验证

- [x] 阶段3: 执行/构建
  - 已补 runtime tooltip、Inspector help box、README / CHANGELOG 文案。
  - 已补 `GsplatLidarExternalGpuCapture` / `Gsplat.compute` 的语义注释。
  - 已补 external capture supersampling 的回归测试:
    - invalid scale sanitize
    - downsample
    - point texel read 不退化成 bilinear
    - depth / surfaceColor / depthStencil 同尺寸
  - 已把 `openspec/changes/lidar-external-capture-supersampling/tasks.md` 的 13 个任务全部勾完。
- [x] 阶段4: 运行验证,记录结论与后续事项
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -v minimal` 通过,`0 warning / 0 error`。
  - Unity EditMode 定向测试 `Gsplat.Tests.GsplatLidarExternalGpuCaptureTests` 运行结果:
    - total=11
    - passed=11
    - failed=0
    - skipped=0
  - `openspec status --change "lidar-external-capture-supersampling"` 保持 complete。

## 状态

**目前在阶段4(本轮 implementation 已完成)**
- `lidar-external-capture-supersampling` 已完成代码、文档、测试和 OpenSpec task 勾选。
- 下一步如果继续,可以考虑:
  - 归档这个 OpenSpec change
  - 或继续做方案2/后续 edge-aware resolve 方向的 explore

## [2026-03-24 10:53:30 +0800] [Session ID: 20260324_1] 阶段进展: 继续方案2 explore

- [ ] 阶段2: 建立“现象 -> 假设 -> 验证计划”并找最小可证伪实验
  - 用户明确要求“继续开方案2 explore”。
  - 已确认当前相关 OpenSpec change `lidar-external-capture-supersampling` 已 complete。
  - 下一步不做实现,只围绕“在不破坏 nearest-surface 语义的前提下,方案2还能怎么做 depth 过渡”展开技术探索。

## 状态

**目前在阶段2(explore)**
- 正在梳理方案2的候选路线、收益、风险和推荐顺序。

## [2026-03-24 10:53:30 +0800] [Session ID: 20260324_1] 阶段进展: 用户已选定方案2的组合方向

- [ ] 阶段2: 建立“现象 -> 假设 -> 验证计划”并找最小可证伪实验
  - 用户已明确要求方案2同时包含:
    - edge-aware nearest resolve
    - subpixel jitter resolve
  - 同时需要两者都能单独开 / 关,也能一起开。
  - 下一步给出推荐的参数形态、执行顺序和最小验证方案,确认这组组合不会产生语义歧义。

## 状态

**目前仍在阶段2(explore)**
- 方案2的方向已经从“选哪条路”收敛到“如何把两条路组合得可控且可验证”。

## [2026-03-24 11:31:47 +0800] [Session ID: 20260324_1] 阶段完成: 方案2已形成独立 proposal 草案

- [x] 阶段2: 建立“现象 -> 假设 -> 验证计划”并找最小可证伪实验
  - 已新建 OpenSpec change: `lidar-external-hybrid-resolve`。
  - 已完成 `proposal.md` 草案。
  - 当前 artifact 状态:
    - `proposal = done`
    - `design = ready`
    - `specs = ready`
    - `tasks = blocked by design/specs`
  - proposal 已明确:
    - edge-aware nearest resolve
    - subpixel jitter resolve
    - 两者独立开关
    - 两者组合顺序与回退语义

## 状态

**目前在阶段4(本轮 explore 产出已落盘)**
- 方案2已经从口头讨论推进成新的 OpenSpec proposal。
- 下一步如果继续,最自然的是:
  - 继续把 `design.md` 和 `specs` 补出来
  - 或先停在 proposal 阶段审口径

## [2026-03-24 12:34:31 +0800] [Session ID: 20260324_8] 阶段完成: `lidar-external-hybrid-resolve` 验证已收口

- [x] 阶段4: 运行验证,记录结论与后续事项
  - 已执行 `openspec status --change "lidar-external-hybrid-resolve" --json`,结果为 `proposal/design/specs/tasks` 全部 `done`。
  - 已执行 `dotnet build ../../Gsplat.Tests.Editor.csproj -v minimal`,结果 `0 warning / 0 error`。
  - 已验证 Unity MCP 的旧 job `16b3d306df314707b4353231203bd602` 实际是“初始化超时后自动失败”,不能继续当作运行中 job。
  - 已确认 `test_names` 精确过滤返回 `summary.total = 0` 时不能作为有效通过证据,因此本轮改用整程序集失败列表归因。
  - 已重新跑 `Gsplat.Tests.Editor` 整程序集 EditMode 验证。最终失败列表里不再包含任何 `GsplatLidarScanTests` 或 `GsplatLidarExternalGpuCaptureTests`。
  - 剩余失败来自既有 `GsplatVisibilityAnimationTests` 和环境缺失 `numpy` 的 importer fixture,未见本轮 hybrid resolve 扩大失败面。
  - 已把 `openspec/changes/lidar-external-hybrid-resolve/tasks.md` 的 `4.4` 勾选完成。

## 状态

**目前在阶段4(已完成)**
- `lidar-external-hybrid-resolve` 的实现与相关验证已经完成。
- 下一步更适合归档该 change,或者单独处理现有的 `GsplatVisibilityAnimationTests` / `numpy` 环境问题。
