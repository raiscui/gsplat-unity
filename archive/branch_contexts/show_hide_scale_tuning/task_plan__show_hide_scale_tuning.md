# 任务计划: 2026-03-15 分析 3DGS show/hide 动画尺度差异

## 目标

- 分析两个 3DGS 资源在 show/hide 动画上球形扩张大小与速度不一致的原因。
- 明确区分现象、候选假设、验证计划与当前结论。
- 给出可执行的调校思路,让不同尺度的 3DGS 都能得到接近的 show/hide 动画效果。

## 阶段

- [ ] 阶段1: 读取已有上下文与相关代码入口
- [ ] 阶段2: 建立现象 -> 假设 -> 验证计划
- [ ] 阶段3: 查证 show/hide 半径或尺度计算链路
- [ ] 阶段4: 输出结论与调校建议

## 关键问题

1. show/hide 球形扩张的半径,是否直接依赖对象的 bounds / extent / object scale?
2. 两个资源的差异,是数据本身的空间尺度不同,还是导入后某个归一化步骤不一致?
3. 当前动画速度快慢,是视觉上因为尺度更大,还是实际参数使用了和尺寸耦合的插值步长?

## 做出的决定

- 先按 systematic-debugging 的流程做证据收集,不先入为主认定是尺度问题。
- 先找 show/hide 动画的半径计算与 shader/renderer 入口,再对照资产数据特征做判断。
- 本轮优先给出可证伪分析与调校方案,如需进一步落地代码修改,再单开实施阶段。

## 状态

**目前在阶段1**
- 正在创建支线上下文并准备读取 show/hide 相关实现。
- 下一步会先回读相关历史记录,再用结构化搜索定位动画参数来源。

## 2026-03-15 21:48:00 +0800 阶段进展

- 正式进入代码入口定位。
- 目标关键词优先级:
  1. `show` / `hide`
  2. `radius` / `sphere` / `bounds` / `extent`
  3. 与 reveal / dissolve / clip / transition 相关的 shader 参数
- 本轮先搜索 Runtime / Editor / Samples 中和显示裁剪动画有关的实现,再决定是否需要进一步看场景或材质配置。

## 2026-03-15 22:15:00 +0800 阶段进展

- [x] 阶段1: 读取已有上下文与相关代码入口
  - 已确认 `GsplatRenderer` / shader / importer 的 show/hide 半径计算链路。
- [x] 阶段2: 建立现象 -> 假设 -> 验证计划
  - 已形成主假设: reveal 半径直接依赖资产 bounds 与 VisibilityCenter。
  - 已形成备选解释: 共享 VisibilityCenter 进一步放大差异,但不是唯一原因。
- [x] 阶段3: 查证 show/hide 半径或尺度计算链路
  - 已用代码阅读 + 场景参数对比 + `.splat4d` 二进制统计三类证据完成验证。
- [ ] 阶段4: 输出结论与调校建议
  - 下一步整理成面向用户的结论、调校公式与建议顺序。

## 状态

**目前在阶段4**
- 已确认当前问题主要由资产空间尺度差异驱动。
- 正在整理“何时该调场景 scale、何时该调 duration、何时需要代码层 radius override”的建议。

## 2026-03-15 22:18:00 +0800 阶段进展

- [x] 阶段4: 输出结论与调校建议
  - 已整理现象、主假设、备选解释、验证证据与调校建议。
  - 已把后续可能的代码改良方向追加到 `LATER_PLANS__show_hide_scale_tuning.md`。

## 状态

**目前已完成阶段4**
- 本轮分析已完成。
- 若用户希望继续落地代码层改造,下一步可直接基于本支线进入实现阶段。

## 2026-03-15 22:24:00 +0800 用户新约束

- 用户确认: 本轮不把 `VisibilityCenter` 作为主因处理方向。
- 用户希望的新语义:
  1. show/hide 先改成“按目标世界速度驱动”,不同大小的 3DGS 在场景里的 reveal 形态尽量接近。
  2. 该目标世界速度先以当前 `ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest` 的效果为基准。
  3. 在此基础上,再额外提供 `VisibilityRadiusScale` 做半径控制。
- 决策:
  - 支线从“分析与调校建议”继续推进到“代码实现 + 测试验证”。
  - 先计算当前 `ckpt` 的等效世界前沿速度,再把它抽象成新驱动模式的默认基准。

## 2026-03-15 22:46:00 +0800 阶段进展

- 已完成首轮代码改造:
  - 新增 `GsplatVisibilityProgressMode` 与共享的 reveal 数学工具。
  - `GsplatRenderer` / `GsplatSequenceRenderer` 已接入 WorldSpeed 驱动与 `VisibilityRadiusScale`。
  - 已补 `GsplatVisibilityAnimationTests` 的回归测试,覆盖:
    - `VisibilityRadiusScale` 对 maxRadius 的缩放
    - WorldSpeed 模式下不同 bounds 的 reveal 前沿世界距离一致性
    - LegacyDuration 模式的兼容语义
- 下一步进入编译与定向测试验证。

## 2026-03-15 23:01:00 +0800 阶段进展

- 正在进入验证阶段前的静态复核。
- 本轮先检查首轮改动的关键代码上下文,重点确认:
  1. `GsplatVisibilityAnimationUtil` 的接口是否与两个 renderer 的调用一致。
  2. `GsplatVisibilityAnimationTests` 的新反射字段、tuple 解构与断言是否与当前私有实现匹配。
  3. 若静态复核无明显问题,下一步立刻执行 `dotnet build` 与定向 EditMode 测试。

## 状态

**目前在验证前静态复核阶段**
- 已回读支线计划和 diff。
- 下一步检查关键代码片段完整上下文,随后编译与跑测试。

## 2026-03-15 23:04:00 +0800 阶段进展

- 已确认当前会话已连接 Unity MCP 实例 `st-dongfeng-worldmodel@717de14bb89cb7b5`。
- 额外验证发现: 当前包目录下没有可直接用于 `dotnet build` 的 `.csproj`,因此本轮验证改为以 Unity EditMode 测试为主。
- 下一步:
  1. 激活 testing 工具组。
  2. 运行 `Gsplat.Tests.Editor` 的定向测试。
  3. 若失败,按失败栈回到代码/测试修正。

## 2026-03-15 23:09:00 +0800 新证据与计划调整

- 已验证: 当前工作目录与 Unity manifest 实际引用的 `wu.yize.gsplat` 不是同一份目录。
- 这意味着:
  - 当前目录里的首轮改动,还不能直接视为 Unity 运行时已采用。
  - 后续所有“是否生效”的判断,必须基于 manifest 指向的外部包目录。
- 计划调整:
  1. 先比对当前改动文件与外部包对应文件的差异。
  2. 将本次 show/hide world-speed 改动安全同步到 Unity 实际使用的包。
  3. 在实际使用的包上运行 EditMode 测试与必要的编译验证。

## 2026-03-15 23:14:00 +0800 假设回滚与修正

- 上一轮关于“Unity 实际使用外部 `gsplat-unity` 包”的判断不成立。
- 推翻该判断的证据:
  1. `../../Packages/packages-lock.json` 中 `wu.yize.gsplat` 的 `source` 为 `embedded`。
  2. 生成的 `../../Gsplat.csproj` 与 `../../Gsplat.Tests.Editor.csproj` 都直接编译 `Packages/wu.yize.gsplat/...`。
- 修正后的结论:
  - 当前工作目录就是 Unity 当前实际编译与测试使用的包源码。
  - 因此可以直接在当前改动基础上继续做 `dotnet build` 与 Unity EditMode 测试验证。

## 2026-03-15 23:18:00 +0800 阶段进展

- 编译首轮失败,但已确认失败原因是新增 `GsplatVisibilityAnimationUtil.cs` 尚未被工程识别。
- 证据:
  1. 文件当前无 `.meta`。
  2. `../../Gsplat.csproj` 尚未包含该文件。
- 当前处理:
  - 先强制 refresh Unity,让新脚本导入并重生成工程。
  - refresh 完成后再次检查 `.meta` 与 `.csproj`,然后重跑 `dotnet build`。

## 2026-03-15 23:21:00 +0800 阶段进展

- `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 已成功通过。
- 当前静态证据:
  - 新增 world-speed / radius-scale 改动已进入编译链。
  - 当前无编译错误。
- 说明:
  - build 过程中出现 3 条既有 warning,分别来自 `GsplatLidarScanTests` 与 `GsplatSog4DImporterTests`,与本次显隐动画改动无直接耦合,本轮先不扩散修改。
- 下一步: 运行 Unity EditMode 测试,验证显隐动画新旧语义。

## 2026-03-15 23:27:00 +0800 修正方案落地

- 已按失败证据修正 WorldSpeed 推进公式。
- 修正内容:
  1. 新增 `EaseInOutQuad` / `InverseEaseInOutQuad` 工具函数。
  2. WorldSpeed 模式下不再直接线性推进 `progress01`。
  3. 改为线性推进 `easedProgress`,再反解回 `progress01`。
- 目的:
  - 让 shader 现有 `EaseInOutQuad(progress)` 视觉曲线保持不变。
  - 同时让不同 totalRange 的 reveal 前沿世界空间推进更接近。
- 下一步: 重新编译并跑定向可见性测试。

## 2026-03-15 23:37:00 +0800 阶段收尾

- [x] 阶段1: 读取已有上下文与相关代码入口
- [x] 阶段2: 建立现象 -> 假设 -> 验证计划
- [x] 阶段3: 查证 show/hide 半径或尺度计算链路
- [x] 阶段4: 输出结论与调校建议
- [x] 阶段5: 落地 WorldSpeed / VisibilityRadiusScale 改造
- [x] 阶段6: 编译与定向测试验证

## 状态

**目前本支线实现与验证已完成**
- 已完成 show/hide 的 WorldSpeed 驱动改造。
- 已保留 LegacyDuration 兼容模式。
- 已完成编译验证与关键定向测试。
- 剩余未处理项已转入 `LATER_PLANS__show_hide_scale_tuning.md`。

## 2026-03-16 00:05:00 +0800 用户新需求

- 用户希望把 hide 粒子预收缩阶段使用的 `EaseOutCirc` 改为 `easeInSine`。
- 目标:
  1. 不改动 hide 前沿世界空间扩张的 WorldSpeed 语义。
  2. 只调整 hide 的粒子缩放节奏。
- 下一步:
  - 定位 `EaseOutCirc` 的定义与使用点。
  - 修改为 `EaseInSine` 并同步更新注释。
  - 重新编译并跑与 visibility 相关的定向测试。

## 2026-03-16 00:10:00 +0800 本轮完成

- 已完成 `EaseOutCirc -> EaseInSine` 替换。
- 已验证:
  1. `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过。
  2. Unity Console 未出现新的 shader 编译错误。
  3. `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayHide_EndsHidden_ValidBecomesFalse` 通过。

## 状态

**当前 hide 预收缩曲线改造已完成**
- hide 前沿外扩逻辑未改。
- hide 预收缩节奏已改为 `easeInSine`。

## 2026-03-16 00:15:00 +0800 用户确认继续

- 用户已同意继续把 hide 内侧继续烧尽阶段的曲线也统一成同一风格。
- 当前目标:
  1. 保持 hide 前沿外扩逻辑不变。
  2. 保持预收缩阶段已改成 `EaseInSine`。
  3. 将 hide 尾部 `passed * passed` 的继续烧尽节奏,也改为 `EaseInSine` 驱动。
- 下一步:
  - 回读 hide 的 `passedForFade / passedForTail / insideScale` 代码块。
  - 做最小改动。
  - 重新 refresh Unity、编译并跑 hide 相关回归测试。

## 2026-03-16 00:22:00 +0800 本轮收尾

- 已完成 hide 尾部继续烧尽曲线统一。
- 已验证:
  1. `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过。
  2. `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayHide_EndsHidden_ValidBecomesFalse` 通过。
  3. `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayHide_DuringShowing_RestartsHideFromZero` 通过。

## 状态

**当前 hide 缩放语言已统一**
- 预收缩: `EaseInSine`
- 扫过后继续烧尽: `EaseInSine`
- 前沿外扩: 保持原有 WorldSpeed / LegacyDuration 逻辑

## [2026-03-18 00:31:11 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 新增 show-hide-switch-高斯 按钮与分段切换时序

### 当前目标

- 新增一个 `show-hide-switch-高斯` 按钮。
- 按下后,从“雷达粒子”切到“高斯基元”时,不再是两边一起渐变切换。
- 新时序要求:
  1. 先触发雷达粒子的 hide 动画。
  2. 在整个切换过程过半时,再触发高斯基元的 show 动画。

### 当前阶段拆分

- [ ] 阶段A: 定位按钮、模式切换入口与现有渐变逻辑
- [ ] 阶段B: 设计新的分段时序与状态保护
- [ ] 阶段C: 落地代码改造与必要注释
- [ ] 阶段D: 编译或测试验证,确认没有回归

### 关键问题

1. 现有“雷达扫描 -> 高斯”按钮是在 UI 层直接切材质/开关,还是通过统一的 show/hide 播放接口触发?
2. “过半”应该绑定总切换时长的一半,还是绑定雷达 hide 动画进度到 50% 时再开始高斯 show?

## [2026-03-18 13:58:00 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 按 OpenSpec change 继续落地 dual-track 切换

### 当前目标

- 使用 `openspec-apply-change` 继续实现 `radarscan-gaussian-dual-track-switch`。
- 本轮不再重做方案讨论,直接补完缺失实现与验证。

### 当前阶段拆分

- [x] 阶段A: 定位按钮、模式切换入口与现有渐变逻辑
- [x] 阶段B: 设计新的分段时序与状态保护
- [ ] 阶段C: 落地 `GsplatSequenceRenderer` 对称 dual-track runtime
- [ ] 阶段D: 更新 editor 文案与测试口径
- [ ] 阶段E: 编译验证并回写 OpenSpec 任务状态

### 本轮执行顺序

1. 先复读 `GsplatRenderer.cs` 已改区域与 `GsplatSequenceRenderer.cs` 对应区域。
2. 以 `GsplatRenderer` 为参照,补齐 `GsplatSequenceRenderer` 的独立 LiDAR hide overlay 轨。
3. 再更新 `GsplatRendererEditor.cs`、`GsplatSequenceRendererEditor.cs` 与 `GsplatVisibilityAnimationTests.cs`。
4. 最后执行 `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`,根据结果再勾选 `openspec/.../tasks.md`。

## 状态

**目前在阶段C**
- 已确认前一轮 `GsplatRenderer` 已有首轮 dual-track patch,但尚未编译验证。
- 下一步先逐段核对两个 runtime 的差异,避免把未验证的逻辑直接复制扩散。

## [2026-03-18 19:28:00 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: OpenSpec dual-track change 已完成实现与验证

### 阶段进展

- [x] 阶段C: 落地 `GsplatSequenceRenderer` 对称 dual-track runtime
  - 已补齐专用 LiDAR hide overlay 状态、halfway 判定、overlap 门禁与 cancel/reset 逻辑。
- [x] 阶段D: 更新 editor 文案与测试口径
  - 已把两个 Inspector 的按钮说明改成“双轨 overlap 切换”。
  - 已把 `GsplatVisibilityAnimationTests` 改为锁定真正的 dual-track 语义。
- [x] 阶段E: 编译验证并回写 OpenSpec 任务状态
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过。
  - 定向 EditMode 测试通过:
    1. `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway`
    2. `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelayedFirstTick_DoesNotStartShowBeforeLidarHalfway`
    3. `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DisablesLidarOnlyAfterDedicatedHideOverlayCompletes`

### 新验证结论

- 已验证:
  1. 序列版 runtime 之前确实停留在单轨旧语义,现在已与静态版对齐。
  2. overlap 阶段专用 hide overlay 曾被 `SetRenderStyle(...)` 内部的 `CancelPendingRadarToGaussianShowSwitch()` 清空。
  3. 将 render-style 硬切拆成“不自动 cancel 的内部 helper”后,专用 hide overlay 可以持续到真正结束。
- 额外说明:
  - 整个 `Gsplat.Tests.Editor` 程序集复跑后,当前只剩一个与本次改动无关的既有失败:
    - `GsplatSplat4DImporterDeltaV1Tests.ImportV1_StaticSingleFrame4D_RealFixturePlyThroughExporterAndImporter`
    - 原因是本机 Python 环境缺 `numpy`

## 状态

**目前本轮 OpenSpec apply 已完成**
- `radarscan-gaussian-dual-track-switch` 的实现、说明文案和定向验证都已完成。
- 下一步只剩整理交付说明与后续建议。

## [2026-03-18 19:42:00 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 用户要求把 Gaussian show 触发点再提前一点

### 当前目标

- 在不破坏 dual-track 语义的前提下,让高斯 show 再更早一点接上。
- 保持:
  1. 雷达 `visibility hide` 仍然完整跑完
  2. overlap 阶段仍然是 `Gaussian + 雷达粒子` 同屏
  3. `EnableLidarScan` 仍然只在专用 hide overlay 结束后关闭

### 当前处理策略

- 不做大改,只把触发阈值从固定 `0.5` 调成“略早于过半”的常量。
- 同步更新注释、Inspector 文案和定向测试口径。
- 改完后重新做编译与 3 个 dual-track 定向测试验证。

## 状态

**目前进入 dual-track 触发点微调阶段**
- 下一步直接改 `GsplatRenderer` / `GsplatSequenceRenderer` 的触发阈值常量,然后重新验证。

## [2026-03-18 20:18:00 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 用户把 dual-track 触发点继续收紧到 0.35

### 当前目标

- 将 `show-hide-switch-高斯` 的 Gaussian show 触发点从 `0.42` 调整到 `0.35`。
- 保持其它语义不变:
  1. 雷达 hide 仍然完整执行
  2. overlap 期间两者仍可同屏
  3. `EnableLidarScan` 仍然只在专用 hide overlay 结束后关闭

### 当前执行步骤

1. 同步修改 `GsplatRenderer` / `GsplatSequenceRenderer` 的触发阈值常量
2. 同步修改 `GsplatVisibilityAnimationTests` 的阈值常量
3. 重新编译
4. 重新跑 3 个 dual-track 定向 EditMode 测试

## 状态

**目前正在执行 0.35 微调**
- 下一步直接修改常量并开始验证。

## [2026-03-18 20:28:00 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 用户反馈 Gaussian -> RadarScan 方向的高斯 alpha 退场被提前掐断

### 现象

- 用户观察到:
  - 从高斯粒子形态切到雷达粒子形态时
  - 高斯粒子原本有一段 alpha 消失过程
  - 但现在这段过程没有做完就不显示了
  - 体感像瞬间消失,不自然

### 当前主假设

- `HideSplatsWhenLidarEnabled` 的 splat 提交门禁,可能在 render-style / alpha 退场动画完成前就把 Gaussian 提交停掉了。

### 备选解释

- 也不排除是 shader 内部 alpha/morph 曲线仍在走,但某个 runtime uniform 或 render-style blend 被更早收敛,导致看起来像瞬间消失。

### 当前验证计划

1. 回读 `SetRenderStyleAndRadarScan(...)`、`ShouldDelayHideSplatsForLidarFadeIn()`、`ShouldSubmitSplatsThisFrame()`
2. 找出现有测试是否只锁了“避免黑帧”,但没锁“Gaussian alpha 退场要跑完”
3. 先补一个最小回归测试,用动态证据确认究竟是哪条门禁提前掐掉了高斯提交

## 状态

**目前进入反向切换 bug 的根因调查阶段**
- 下一步先回读进入 RadarScan 的 runtime 路径和现有测试口径。

## [2026-03-18 20:36:00 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 已修复 Gaussian -> RadarScan 时高斯 alpha 退场被提前掐断

### 阶段进展

- [x] 根因调查
  - 已用最小红测确认: splat 提交门禁会在 Gaussian alpha 退场完成前提前关闭。
- [x] runtime 修复
  - 已让 `ShouldDelayHideSplatsForLidarFadeIn()` 同时考虑:
    1. LiDAR fade-in 是否完成
    2. Gaussian -> ParticleDots render-style 动画是否完成
- [x] 验证
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过
  - 相关定向 EditMode 测试通过

### 已验证结论

- 现象:
  - 高斯 -> 雷达时,高斯 alpha 退场还没做完就突然不显示
- 根因:
  - `HideSplatsWhenLidarEnabled` 的 splat 提交门禁之前只看 LiDAR 淡入完成与否
  - 没看 Gaussian -> ParticleDots 的 render-style 退场是否仍在进行
  - 所以会出现“雷达已经亮起来了,但高斯 alpha 还没退完,提交却先被停掉”

## 状态

**目前这条反向切换 bug 已完成修复与验证**
- 下一步可直接交付结果,或继续做现场观感微调。

## [2026-03-18 20:46:00 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 用户确认现场仍有“突然消失”,上一轮修复不足以覆盖真实路径

### 现象

- 用户刚刚再次确认:
  - 高斯 -> 雷达时
  - 现场看起来还是会突然消失

### 当前判断

- 上一轮“提交门禁过早关闭”的修复和定向测试是成立的。
- 但这说明真实现场里至少还有第二个截断点:
  1. 可能是 shader 内 render-style 的实际 alpha/morph 退场轨和测试假设不一致
  2. 也可能是 runtime 还有别的 gate 在更早时机把 Gaussian 观感切没了

### 新验证计划

1. 读取 render-style 相关 shader 路径,确认高斯 alpha 实际是如何随 blend 退场的
2. 检查 `PushRenderStyleUniformsForThisFrame(...)` 与 `ShouldSubmitSplatsThisFrame()` 之外是否还有别的早退路径
3. 补一个更贴近现场的红测:
   - 不只测“是否还在提交”
   - 还要测“render-style 动画未完成时,运行态是否已经进入纯雷达 gate”

## 状态

**目前回到根因调查阶段**
- 下一步先读 shader 和 render-style 提交链路,不先继续补丁。
3. 当前系统里是否已经有可复用的延迟 show / 串行动画机制,避免再额外堆一个并行协程分支?

### 当前决策

- 先复用现有 show/hide 播放链路,优先做“改良胜过新增”的改造,避免再造一套独立动画系统。
- 在看到动态证据前,暂不假设现有渐变就是某一个协程或某一个 shader 参数造成的。
- 先建立“现象 -> 假设 -> 验证计划”,再真正改代码。

### 状态

**目前在阶段A**
- 正在定位 `show-hide-switch-高斯` 应该落到哪一层。
- 下一步先查按钮定义、点击处理函数、以及雷达粒子和高斯基元各自的 show/hide 入口。

## [2026-03-18 00:31:11 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 阶段A完成并进入实现设计

### 已验证事实

- 现有 `Gaussian(动画)` 按钮属于“并行切换”:
  - LiDAR fade-out 和 RenderStyle morph 同时开始。
- 一旦 `SetRadarScanEnabled(false, ...)` 执行,`EnableLidarScan` 会立即变成 `false`。
- 在当前门禁下,如果不额外控制 `m_visibilityState`,高斯 splat 会过早重新参与提交。

### 阶段状态

- [x] 阶段A: 定位按钮、模式切换入口与现有渐变逻辑
- [x] 阶段B: 设计新的分段时序与状态保护
- [ ] 阶段C: 落地代码改造与必要注释
- [ ] 阶段D: 编译或测试验证,确认没有回归

### 当前决策

- 采用运行时专用切换 API + Inspector 新按钮的方案。
- 新按钮不替换现有 `Gaussian(动画)`。
- 新按钮的目标语义:
  1. 立即开始 LiDAR hide。
  2. 同时把 splat 强制置为 Hidden,避免高斯抢跑。
  3. 到 LiDAR hide 时长过半时,再切到 `Gaussian` 并启动 `PlayShow()`。

### 状态

**目前在阶段C**
- 正在给 `GsplatRenderer` / `GsplatSequenceRenderer` 补半程触发调度。
- 下一步会同步把两个 Inspector 都接上新按钮,然后补测试。

## [2026-03-18 00:51:48 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 阶段C/阶段D完成

### 已完成内容

- [x] 阶段C: 落地代码改造与必要注释
  - `GsplatRenderer` / `GsplatSequenceRenderer` 已新增 `PlayRadarScanToGaussianShowHideSwitch(...)`。
  - 两个 renderer 都已接入“半程触发高斯 show”的待执行状态推进。
  - `GsplatRendererEditor` / `GsplatSequenceRendererEditor` 都已新增 `show-hide-switch-高斯` 按钮。
- [x] 阶段D: 编译或测试验证,确认没有回归
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过,`0 warning / 0 error`。
  - Unity 定向测试通过:
    1. `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway`
    2. `Gsplat.Tests.GsplatVisibilityAnimationTests.SetRenderStyleAndRadarScan_Animated_DelayHideSplatsUntilRadarVisible`

### 验证补充

- Unity `run_tests` 在当前工程里最开始返回 `total=0`,原因不是测试通过,而是:
  1. 工程默认没有把包测试放进 `testables`
  2. `test_names` 需要传完整限定名,短名不会命中
- 为了拿到真实证据,本轮做了临时验证动作:
  1. 暂时把 `wu.yize.gsplat` 加入工程 `testables`
  2. 重新 resolve package 后跑全名测试
  3. 验证结束后已把 `manifest.json` 恢复原状

### 状态

**当前本轮任务已完成**
- 新按钮已经可用。
- 现有 `Gaussian(动画)` 按钮保留不变。
- 新按钮语义已经被定向测试锁定。

## [2026-03-18 00:51:48 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 用户反馈新按钮时序不对,重新进入排障

### 现象

- 用户反馈:
  - 点击 `show-hide-switch-高斯` 后,雷达粒子“直接迅速隐藏”。
  - 高斯的 show 也很快就开始了。
- 这与预期的“雷达先走一半 hide,再开始高斯 show”不一致。

### 当前主假设

- 目前 `PlayRadarScanToGaussianShowHideSwitch(...)` 用的是“墙钟时间延迟”:
  - `Time.realtimeSinceStartup - armTime >= halfDuration`
- 但 LiDAR hide 的真正推进是在 `Update / Editor ticker` 里完成的。
- 如果第一次 tick 来得比 halfDuration 晚,就会出现:
  1. 雷达 hide 实际还没推进到一半
  2. 高斯 show 却已经被 wall-clock 条件提前触发

### 备选解释

- 也可能是当前场景对象的 `LidarHideDuration` / `RenderStyleSwitchDurationSeconds` 本身过小或为 0,导致看起来像“立刻切”。

### 最小验证计划

1. 静态检查当前实现是否确实按 wall-clock 判定半程。
2. 动态做一个“故意延后第一次 Update”的定向测试,看是否会复现“雷达未过半,高斯已 show”。
3. 若验证成立,再把触发条件从“时间过半”改成“LiDAR hide 动画进度过半”。

### 状态

**目前重新回到排障阶段**
- 先做最小复现实验,不直接补丁。

## [2026-03-18 01:07:19 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 抢跑问题已修正并完成验证

### 验证结论

- 主假设成立:
  - 旧实现把“半程”绑定到了 wall-clock
  - 第一次 tick 晚到时,高斯 show 会抢跑
- 关键动态证据:
  - `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelayedFirstTick_DoesNotStartShowBeforeLidarHalfway`
  - 修复前失败,修复后通过

### 已完成修正

- `GsplatRenderer` / `GsplatSequenceRenderer`
  - 不再用 `elapsed >= halfDuration` 判定
  - 改为使用 `m_lidarVisibilityAnimProgress01 >= 0.5f`
- 测试:
  - 保留原有“半程后能进入 Gaussian show”的回归
  - 新增“第一次 tick 晚到也不能抢跑”的复现实验

### 最终验证

- `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过
- Unity 定向测试通过:
  1. `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelayedFirstTick_DoesNotStartShowBeforeLidarHalfway`
  2. `Gsplat.Tests.GsplatVisibilityAnimationTests.PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway`
  3. `Gsplat.Tests.GsplatVisibilityAnimationTests.SetRenderStyleAndRadarScan_Animated_DelayHideSplatsUntilRadarVisible`

### 状态

**当前这一轮 bug fix 已完成**
- 新按钮仍保留
- 抢跑问题已修正
- 临时 `testables` 验证改动已恢复

## [2026-03-18 01:18:30 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 用户反馈雷达粒子仍被一按即杀,进入第二轮时序修正

### 现象

- 用户反馈当前版本仍然不对:
  - 不能在按钮点击当下就把 `EnableLidarScan` 关掉
  - 现在的视觉效果是“雷达粒子立刻消失”
- 静态复核还发现另一条同样危险的路径:
  - `PlayRadarScanToGaussianShowHideSwitch(...)` 在起点调用了 `SetVisible(false, animated: false)`
  - 这会把共享显隐状态直接压成 `Hidden`

### 当前主假设

- 当前问题不止一个触发点:
  1. `EnableLidarScan=false` 太早写入,导致 RadarScan runtime 链路过早退出或停止更新
  2. `SetVisible(false, animated: false)` 会让共享 show/hide overlay 直接把 LiDAR 也裁成 0
- 也就是说,上一轮“只修半程触发条件”还不够,按钮编排本身仍然是错误语义

### 备选解释

- 如果当前场景高度依赖 range image 实时刷新,那么即便有 `m_lidarKeepAliveDuringFadeOut`,只要 `TickLidarRangeImageIfNeeded()` 仍然直接看 `EnableLidarScan`,视觉上也可能提前塌掉

### 最小修正计划

1. 把按钮起点改成“只启动雷达 hide”,不再立即 `SetVisible(false, animated: false)`
2. 不再在按钮点击瞬间写 `EnableLidarScan=false`,而是延后到高斯 show 真正启动的那一刻
3. 让 Gaussian show 从“隐藏起点”单独启动,但不把 LiDAR 一起拖进共享 overlay
4. 更新测试,移除“立即关 `EnableLidarScan` 才算正确”的错误断言

### 状态

**目前重新进入执行修复阶段**
- 下一步直接改运行时编排和测试

## [2026-03-18 01:25:40 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 第二轮“按钮一按即杀雷达”修复完成

### 已验证结论

- 当前轮用户反馈成立:
  - 问题不只是“半程触发时机”
  - 还包括按钮起点把共享显隐状态和雷达开关都动得太早
- 静态证据已确认两条直接错误路径:
  1. `SetVisible(false, animated: false)` 会把 LiDAR overlay 一起压成 `gate=0`
  2. 按钮点击当下就关闭 `EnableLidarScan` 会让 RadarScan runtime 过早退出或停止更新

### 已完成修正

- `GsplatRenderer` / `GsplatSequenceRenderer`
  - 起点改为“只启动 LiDAR hide”,不再立刻 `SetVisible(false, animated: false)`
  - `EnableLidarScan=false` 延后到 Gaussian show 真正启动时
  - 新增“从 FullHidden 启动 Gaussian show”的内部 helper
  - 在 Radar -> Gaussian 重叠阶段,LiDAR render 不再吃 Gaussian 的共享 show overlay
  - `TickLidarRangeImageIfNeeded()` 改为认 `IsLidarRuntimeActive()`,让 fade-out keepalive 期间也能继续更新
- 测试:
  - 把“立即关闭 `EnableLidarScan` 才算正确”的错误断言改掉
  - 改为锁定“前半段雷达仍活着,半程才启动 Gaussian show,重叠期 LiDAR gate 仍打开”

### 验证结果

- `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过
- Unity MCP `run_tests` 本轮未拿到有效动态结果:
  - 返回 `summary.total = 0`
  - 当前工程 `manifest.json` 中 `wu.yize.gsplat` 指向外部 `file:` 包路径,高度怀疑 Test Runner 没有真正加载当前工作区这份代码
  - 临时 `testables` 改动已恢复,未留在 `manifest.json`

### 状态

**当前代码修正已完成,编译通过**
- 还缺“当前工作区真实挂到 Unity 后的动态观感确认”
- 若用户继续反馈观感,下一步优先处理真实挂载路径下的 on-screen 验证

## [2026-03-18 01:28:40 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 回滚“外部 file 包导致本轮空跑”的假设

### 被推翻的旧假设

- 刚才一度怀疑 Unity MCP `run_tests` 返回 `summary.total = 0` 是因为工程实际加载的是外部 `file:` 包副本,不是当前工作区

### 推翻证据

- `../../Gsplat.csproj` 明确编译:
  - `Packages/wu.yize.gsplat/Runtime/GsplatRenderer.cs`
  - `Packages/wu.yize.gsplat/Runtime/GsplatSequenceRenderer.cs`
- `../../Gsplat.Tests.Editor.csproj` 明确编译:
  - `Packages/wu.yize.gsplat/Tests/Editor/GsplatVisibilityAnimationTests.cs`
- 当前工作目录与 `../../Packages/wu.yize.gsplat` 的 inode 一致,说明就是同一份目录

### 修正后的结论

- 这轮 `run_tests` 的 `summary.total = 0` 不能归因于“跑错包副本”
- 更合理的解释是:
  - Unity MCP 当前没有真正把包测试收集进来
  - 或 `run_tests` 的包测试收集链路仍存在额外门槛

### 状态

**当前最终口径**
- 当前工作区代码就是工程编译目标
- 编译验证可信
- Unity MCP 动态测试仍缺有效证据,但原因应归到测试收集链路,不是包副本错误

## [2026-03-18 01:38:20 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 用户反馈雷达 hide 缺少 visibility hide 的 noise 燃烧过程

### 现象

- 用户补充了新的真实目标:
  - `show-hide-switch-高斯` 的雷达粒子 hide 过程
  - 应该执行 `visibility hide` 按钮那条处理过程
  - 要有 noise / 燃烧效果
- 当前现象是:
  - 雷达虽然不再“秒没”
  - 但前半段没有 `visibility hide` 的燃烧观感

### 当前主假设

- 上一轮为了避免雷达被立刻裁掉,把前半段改成了“只做 LiDAR 可见度淡出”
- 但没有进入 `PlayHide()` 驱动的共享 hide 状态机
- 结果是:
  - `BuildLidarShowHideOverlayForThisFrame(...)` 前半段没有得到 `Hiding / mode=2`
  - 因而雷达粒子缺少 noise 燃烧 hide 效果

### 备选解释

- 也可能是前半段虽然进入了 hide 状态,但在半程切到 Gaussian show 前太快被别的状态覆盖,导致肉眼看起来像“没有”

### 最小验证计划

1. 把按钮前半段接回真正的 `PlayHide()` 路径,但在它之后再重新 arm 半程切换,避免被 `CancelPending...` 抵消
2. 更新测试,把“前半段 VisibilityState=Visible”的断言改成“前半段应为 Hiding, overlay mode=2”
3. 编译验证,确认这次不是引入新的状态机冲突

### 状态

**目前重新进入定向修正阶段**
- 这轮先补回正确的 hide 过程,不盲调 shader 参数

## [2026-03-18 01:43:20 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 雷达前半段 noise 燃烧过程已补回

### 验证结论

- 当前主假设成立:
  - 问题不是 shader 噪声参数没传
  - 而是前半段根本没有进入 `PlayHide()` 驱动的 `Hiding`
- 修正后:
  - 按钮前半段现在会进入与 `Hide` 按钮一致的 `visibility hide` 处理过程
  - 半程切换仍保留

### 已完成修正

- `GsplatRenderer` / `GsplatSequenceRenderer`
  - 在 `BeginRadarHideForRadarToGaussianShowSwitch(...)` 成功启动后,补走 `PlayHide()` 路径
  - 但把 `ArmRadarToGaussianShowSwitch()` 继续放在后面,避免被 `CancelPending...` 抵消
- 测试:
  - 把前半段断言从 `Visible` 改成 `Hiding`
  - 新增检查 `BuildLidarShowHideOverlay(...).mode == 2`

### 最终验证

- `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo` 通过

### 状态

**当前这轮修正已完成**
- 代码已编译通过
- 前半段 hide 语义已与用户新补充的目标对齐

## [2026-03-18 09:37:07 +0800] [Session ID: unknown] [记录类型]: 用户将目标进一步收紧为“双轨并行且 hide 跑完整”

### 现象

- 用户最新明确语义不是:
  - hide 做到一半就切到 show
- 而是:
  1. 雷达粒子的 hide 过程要完整执行完 `visibility hide` 那套 noise / 燃烧处理
  2. Gaussian 的 show 动画在这条 hide 轨“过半时”启动
  3. 中段必须出现 `高斯 + 雷达粒子` 同时呈现
  4. 只有当雷达 hide 轨真正跑完后,才能关闭 `EnableLidarScan`

### 当前主假设

- 现有实现的核心限制已经不是阈值快慢,而是状态表达能力不够:
  - `PlayRadarScanToGaussianShowHideSwitch(...)` 在半程触发后会把共享显隐状态从 `Hiding` 切到 `Showing`
  - `BuildLidarShowHideOverlayForThisFrame(...)` 只认这套共享 `m_visibilityState`
- 因此只要 Gaussian show 一启动,雷达那条 hide 轨就会在结构上被覆盖
- 如果不做最小双轨化,就无法同时满足:
  - 雷达 hide 跑完整
  - Gaussian 中段开始 show
  - overlap 期间两者同屏

### 备选解释

- 理论上的备选方向是继续用单轨状态机,再叠更多 bypass / 延迟开关
- 但当前静态证据显示:
  - 这最多能修“看起来别太快切”
  - 不能保证 hide 轨在高斯 show 启动后仍然继续独立推进到终点

### 最小验证计划

1. 先把当前实现按“共享显隐轨”和“LiDAR 自身 fade 轨”拆开复盘,确认最小双轨需要改哪些门禁
2. 检查 `ShouldSubmitSplatsThisFrame()` 是否必须在 overlap 阶段放行高斯 splat
3. 检查 `BuildLidarShowHideOverlayForThisFrame(...)` 是否需要独立消费一条 Radar->Gaussian 专用 hide overlay 轨
4. 在不继续堆补丁前,先给出:
   - 最佳方案
   - 先能用但不够优雅的保守方案

### 状态

**目前进入重新设计阶段**
- 本轮先不继续盲改代码
- 先输出一份针对“双轨并行”语义的周密实施计划

## [2026-03-18 09:41:12 +0800] [Session ID: unknown] [记录类型]: 用户要求先按方案A产出 OpenSpec change

### 行动目的

- 用户已经确认按方案A推进。
- 本轮先不落 runtime 补丁,改为先把 OpenSpec change 方案建立完整。

### 当前计划

1. 选定 change 名称,优先体现 RadarScan -> Gaussian overlap switch 的核心语义
2. 回读已有相近 OpenSpec change,复用本仓库 proposal / design / tasks / spec delta 的风格
3. 用 OpenSpec CLI 创建新 change 骨架
4. 产出 proposal / design / tasks 与必要 spec delta

### 状态

**目前进入 OpenSpec 建模阶段**
- 下一步开始读取现有 change 模板与相关 specs

## [2026-03-18 09:49:26 +0800] [Session ID: unknown] [记录类型]: 方案A 的 OpenSpec change 已建立并校验通过

### 已完成事项

- 已新建 change:
  - `openspec/changes/radarscan-gaussian-dual-track-switch/`
- 已完成 artifacts:
  1. `proposal.md`
  2. `design.md`
  3. `tasks.md`
  4. `specs/gsplat-radarscan-gaussian-switch/spec.md`
- 已执行校验:
  - `openspec validate radarscan-gaussian-dual-track-switch`
  - 结果: valid

### 本轮结论

- 这次 change 已把方案A 的核心口径固定下来:
  - Radar hide 轨完整执行
  - Gaussian show 在 hide 过半时启动
  - overlap 阶段 `高斯 + 雷达粒子` 同屏
  - hide 完成后才关闭 LiDAR
- 现在已经具备进入 apply / implementation 的前置规格条件

### 状态

**当前 OpenSpec 方案阶段已完成**
- 下一步可以按该 change 进入实现

## [2026-03-18 10:01:12 +0800] [Session ID: unknown] [记录类型]: 按 openspec-apply-change 开始实现 `radarscan-gaussian-dual-track-switch`

### 行动目的

- 用户已明确要求进入 apply 阶段。
- 本轮目标不是继续讨论方案,而是按 OpenSpec tasks 落代码并逐项勾选。

### 当前上下文

- Using change: `radarscan-gaussian-dual-track-switch`
- Schema: `spec-driven`
- 当前 apply 进度:
  - `0/12 tasks complete`
- OpenSpec apply 指令已确认 state=`ready`

### 实施顺序

1. 先回读 contextFiles 与当前相关代码,确认现有半成品状态
2. 先做 runtime 双轨状态与 overlap 门禁
3. 再更新 editor 文案
4. 最后补 tests 与编译验证,并同步勾选 tasks

### 状态

**目前进入 apply 实施阶段**
- 下一步开始逐段阅读 `Runtime/*`、`Editor/*`、`Tests/*` 的相关实现

## [2026-03-18 12:33:45 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 继续排查“高斯 -> 雷达”方向仍有突然消失

### 行动目的

- 用户最新反馈是: 高斯退场 alpha 还没自然做完,现场看起来仍像瞬间消失。
- 上一轮已经修过“splat 提交门禁过早关闭”这一层,本轮不能重复拍脑袋继续改同一处。
- 需要重新区分:
  1. 运行时是否还有别的提前关断
  2. 还是 shader 里的高斯退场曲线本身过陡

### 当前计划

1. 回读支线上下文,把上一轮已验证结论和本轮待验证范围对齐
2. 聚焦 `GsplatRenderer` / `GsplatSequenceRenderer` / `Gsplat.shader`
3. 先建立新的现象 -> 假设 -> 验证计划
4. 只有在证据指向明确后才落代码和测试

### 状态

**目前进入新一轮根因排查阶段**
- 已确认当前 Session ID,后续记录沿用本链路
- 下一步重点查看 render-style alpha 退场与 dual-track 切换衔接

## [2026-03-18 12:48:29 +0800] [Session ID: 019cfc9f-fe46-7e83-89ae-e49289473ee6] [记录类型]: 高斯 -> 雷达 的 alpha handoff 放缓改造已落地

### 已完成事项

1. 已在 `GsplatRenderer` / `GsplatSequenceRenderer` 中把 render-style 的几何 blend 和 alpha blend 拆开
2. 几何 morph 继续沿用 `EaseInOutQuart`
3. alpha handoff 改为 `EaseInOutSine`
4. shader 已新增 `_RenderStyleAlphaBlend`,不再让 fragment alpha 直接跟着几何 blend 走
5. `GsplatVisibilityAnimationTests` 已补充断言:
   - 入雷达时 alpha handoff 必须慢于几何 morph

### 验证状态

- 已完成:
  - `dotnet build ../../Gsplat.Tests.Editor.csproj -nologo`
  - 结果: `0 warning / 0 error`
- 动态验证阻塞:
  - Unity MCP 当前没有连接到 Editor 实例
  - 直接用 Unity CLI 跑定向测试时,项目被另一份 Unity 实例锁住:
    - `似乎有另一个正在运行的 Unity 实例打开了此项目`
- 已清理:
  - 临时加入 `manifest.json` 的 `testables` 已恢复干净

### 状态

**目前本轮代码修复已完成,编译验证已完成**
- 动态 EditMode 测试尚缺一轮可运行的 Unity 实例环境
- 若后续 Unity Editor 可连接 MCP 或释放项目锁,下一步优先补跑这两个定向用例
