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
