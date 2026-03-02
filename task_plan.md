# 任务计划: 修复 Unity Editor 下 Gsplat 仍然闪烁(多次 BeginCameraRendering / Draw 丢失)

## 目标
在 Unity Editor 非 Play 模式下:
- 鼠标在 SceneView 的 UI(overlay/toolbar/tab)区域滑动,画面不再出现“显示/不显示”的整体闪烁.
- GameView/SceneView 不再出现“偶发整帧消失”.
- `EnableEditorDiagnostics` 开启后,日志里每次 `BeginCameraRendering` 都能看到对应的 draw 提交证据(或明确的 skip 原因),不再黑盒.

## 阶段
- [x] 阶段1: 现象复盘与证据收集
- [x] 阶段2: 根因定位(时序/门禁/生命周期)
- [x] 阶段3: 修复实现(改渲染提交策略)
- [x] 阶段4: 回归验证(单测 + Unity 命令行)
- [x] 阶段5: 收尾归档(WORKLOG/notes/LATER_PLANS/ERRORFIX)

## 两条路线(先定方向再动代码)
### 路线A(正确性优先): EditMode 渲染与 SRP 相机回调对齐
- 核心思想:
  - Editor 下同一 `Time.frameCount` 可能触发多次 `BeginCameraRendering`.
  - 如果 draw 只在 `ExecuteAlways.Update` 里提交,就可能只覆盖其中一次 render invocation,表现为闪烁.
  - 因此把“提交 draw”移动到(或补充到) `beginCameraRendering` 的同一链路里,确保每次相机渲染都能看到 draw.
- 风险:
  - 需要避免 Play 模式双重提交(draw twice)与性能退化.

### 路线B(先能用): 放宽 sort 门禁/增加重复提交兜底
- 核心思想:
  - 继续沿用 Update 提交 draw,但通过放宽/调整门禁,尽量让 Update 更频繁触发并覆盖到更多 repaint.
- 风险:
  - 本质上仍在赌 Editor 内部 repaint 时序,容易反复返工.

## 做出的决定
- [决定] 优先采用路线A.
  - 理由: 用户已提供 `[GsplatDiag]` 证据显示同一帧存在多次 `BeginCameraRendering`,而 draw 只有一次.
    这是“时序不一致”问题,靠继续补 UI 信号很难彻底稳态.

## 关键问题
1. 我们当前 draw 的提交点在哪里? 是否只在 Update 内提交,从而无法覆盖每次相机渲染调用?
2. `GatherGsplatsForCamera` 的 false 具体是哪一种原因(每帧 guard vs 无对象 vs 非 ActiveCamera)? 需要更细粒度 reason.
3. 如何做到:
   - EditMode: 每次 camera render 都能 draw(避免闪烁)
   - PlayMode: 不重复 draw,不引入额外排序/渲染开销

## 状态
**已完成** - 用户已按原复现步骤确认“不闪了”. 根因已被证据闭环,并通过代码门禁修复.

## 进展记录
### 2026-02-24 11:15:00 +0800
- 已从用户提供的 `[GsplatDiag]` ring-buffer 证据确认:
  - 同一 `Time.frameCount` 内,SceneView camera 会触发多次 `BeginCameraRendering`.
  - 但 draw 提交主要发生在 `ExecuteAlways.Update`,因此通常每帧只提交一次 draw.
- 根因判断:
  - Editor 的“多次相机渲染调用”与“Update 单次提交 draw”的时序不一致,会导致部分 render invocation 没有 splats,体感为闪烁.
- 修复落地(核心思路: 对齐 SRP 相机回调):
  - `GsplatSorter` 在 `RenderPipelineManager.beginCameraRendering` 内,完成排序后补交一次“针对当前 camera 的 draw”.
  - `GsplatRenderer/GsplatSequenceRenderer` 在 EditMode + SRP + ActiveCameraOnly 下,不再从 Update 提交 draw,避免重复渲染.
  - `GsplatEditorDiagnostics` 增强为按相机统计 `renderCount/drawCount`,并用 `rs(render serial)` 关联 render invocation 与 draw.
- 命令行验证(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics` 最小工程 `_tmp_gsplat_pkgtests`
  - `-testFilter Gsplat.Tests`: total=26, passed=24, failed=0, skipped=2
  - 结果文件: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_flickerfix.xml`

### 2026-02-24 11:22:00 +0800
- 已完成阶段5收尾:
  - `notes.md`/`WORKLOG.md`/`ERRORFIX.md`/`LATER_PLANS.md` 已追加本任务结论与证据.
  - continuous-learning:
    - 新增 skill: `~/.codex/skills/self-learning.unity-editor-srp-beginCameraRendering-flicker/SKILL.md`
    - 已把历史超长文件归档到 `archive/`:
      - `archive/task_plan_2026-02-24_105820.md`
      - `archive/notes_2026-02-24_105820.md`

### 2026-02-24 12:40:00 +0800
- 用户反馈: 仍然闪烁.
- 新证据(来自最新 `[GsplatDiag]` dump):
  - `SceneView renderCount > drawCount`.
  - 且出现 `RenderForCamera` 被跳过,提示 camera 处于 "null/disabled" 一类状态.
- 推断:
  - 某些 Unity/Editor 状态下,SceneView 的内部 camera 可能出现 `enabled=false` / `isActiveAndEnabled=false`,
    但它依然会参与 SRP 渲染回调(beginCameraRendering).
  - 因此任何以 `isActiveAndEnabled` 作为渲染门禁的地方,都会导致“相机在渲染,但我们不提交 draw”,体感为闪烁.
- 采取措施(补强):
  - `Runtime/GsplatRendererImpl.cs`: 遍历 `SceneView.sceneViews` 时不再用 `cam.isActiveAndEnabled` 过滤,只做 null/destroyed 防御.
  - `Runtime/GsplatEditorDiagnostics.cs`: `DescribeCamera` 输出 `en/act`,让 dump 可以直接看出该 camera 的 enabled 状态.
- 待验证:
  - 在用户复现步骤下,`renderCount/drawCount` 不再失配,且闪烁消失.

### 2026-02-24 13:02:53 +0800
- 用户确认: 不闪了.
- 结论:
  - 闪烁的最后一刀根因是: SceneView 内部 camera 可能 `enabled=false`/`isActiveAndEnabled=false` 但仍参与 SRP 渲染回调.
  - 因此渲染门禁不能用 `isActiveAndEnabled` 过滤 SceneView cameras,应以“回调链路的 camera”作为事实来源,仅做 null/destroyed 防御.

### 2026-02-24 13:10:00 +0800
- 用户问题: 切换到 GameView 后不显示高斯基元.
- 根因:
  - 为了修复 overlay/UIElements 信号噪声导致的闪烁,我们曾在 EditMode 把 ActiveCameraOnly 固定为 SceneView.
  - 这会让 Game/VR 相机永远不是 ActiveCamera,导致:
    - `GatherGsplatsForCamera` 把 Game camera 判为 "camera is not ActiveCamera".
    - SRP 相机回调里的 `SubmitEditModeDrawForCamera` 也会拒绝对 Game camera 提交 draw.
- 修复(兼顾稳态):
  - `Runtime/GsplatSorter.cs`: EditMode 下改为:
    - 当 GameView 窗口聚焦时,ActiveCamera 解析为 Game/VR 相机(用于预览 GameView).
    - 否则仍默认选择 SceneView(保持 overlay 场景稳态,避免闪烁回归).
  - `Runtime/GsplatSettings.cs`: 更新 CameraMode Tooltip,把上述规则写清楚.
- 命令行验证(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics` 最小工程 `_tmp_gsplat_pkgtests`
  - `-testFilter Gsplat.Tests`: total=26, passed=24, failed=0, skipped=2
  - 结果文件: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_2026-02-24_1310.xml`

### 2026-02-24 14:32:11 +0800
- 用户新反馈:
  - EditMode: 在 GameView 下拖动 `TimeNormalized` 会让高斯基元“全消失”.
  - PlayMode: 拖动 `TimeNormalized` 可以显示,但非常卡; `AutoPlay` 也非常卡.
- 初步判断(根因假设):
  1. (消失) ActiveCameraOnly(EditMode) 目前只在 "GameView 聚焦" 时选择 Game/VR 相机.
     但用户拖动 Inspector 滑条时,焦点会落到 Inspector,导致 ActiveCamera 切回 SceneView.
     于是 `GatherGsplatsForCamera`/`SubmitEditModeDrawForCamera` 对 Game camera 直接 gate,体感为“全消失”.
  2. (卡顿) PlayMode 下每帧都要做 GPU radix sort(以及可能的序列 decode).
     对于 keyframe `.splat4d`(多 segment 叠在一个 asset 里)的典型数据,当前会对“全量 records”排序,
     但同一时刻只有一个 segment 真的可见,其余 records 的 sort 属于纯浪费,会把播放拖成 PPT.

## 两条路线(先定方向再动代码)
### 路线A(正确性+性能): 稳态 ActiveCamera + 子范围排序/渲染
- ActiveCameraOnly(EditMode) 改为使用 "viewport hint(最近交互视窗)" 而不是仅靠 `focusedWindow`.
  - 目标: 你点击过 GameView 之后,就算去 Inspector 拖动滑条,GameView 也能持续预览而不消失.
- 为 sorter/render 增加 `baseIndex` 支持:
  - 排序阶段仅对 [baseIndex, baseIndex+count) 这段 records 计算 depth key 并排序.
  - 渲染阶段在 shader 侧把 `_OrderBuffer` 的 local index 转换为 absolute id.
- 对 `.splat4d(window)` 检测 keyframe segment(连续区间内 `time/duration` 常量且 segments 不重叠),
  并在播放时只对“当前 segment”做 sort+draw,把成本从 O(totalRecords) 降到 O(recordsPerSegment).

### 路线B(先能用): 只修消失 + 给出设置建议
- 仅修 ActiveCameraOnly(EditMode) 的 hint 逻辑,让 GameView 拖动滑条不再消失.
- 性能先通过配置与工作流规避:
  - 保持 `CameraMode=ActiveCameraOnly`.
  - PlayMode 下跳过 SceneView 排序/渲染(已默认开启).
  - 若使用序列后端,必要时先降 SH/关闭插值.

## 本轮选择
- [决定] 优先采用路线A.
  - 理由: 同时解决“消失(正确性)”与 “PlayMode 卡顿(性能)”两类问题,并把优化做成自动稳态,减少用户手工调参.

## 本轮待办
- [x] 修复 EditMode 下 GameView 拖动滑条消失(ActiveCamera hint).
- [x] 为 sorter/render 增加 baseIndex 支持(Compute + Shader + C#).
- [x] GsplatRenderer 增加 keyframe segment 检测,window model 下按 segment sort/draw.
- [x] 回归: tests 编译与通过(必要时补测试).
- [x] 收尾: notes/WORKLOG/ERRORFIX/LATER_PLANS 追加记录与结论.

### 2026-02-24 14:54:19 +0800
- 已完成:
  - EditMode: ActiveCameraOnly 改用 viewport hint,并在 OnValidate 触发 RepaintAllViews.
    - 目标: GameView 预览不再因拖动 Inspector 滑条而“全消失”.
  - 性能: 为 sort/render 引入 baseIndex 子范围,并对 `.splat4d(window)` 检测 keyframe segments,
    播放时仅对当前 segment 做 sort+draw(避免对全量 records 排序的线性浪费).
- 回归(证据型):
  - Unity 6000.3.8f1,`-batchmode -nographics`,最小工程 `_tmp_gsplat_pkgtests`
  - `-testFilter Gsplat.Tests`: total=26, passed=24, failed=0, skipped=2
  - 结果文件: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_timeNormalized_fix_2026-02-24_1453.xml`
  - 日志: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_timeNormalized_fix_2026-02-24_1453.log`
- 状态: 已完成代码修复与回归,等待用户在实际场景验证体验(重点验证 PlayMode 的流畅度).

---

# 任务计划: Gsplat 显隐燃烧环动画(show/hide) + Inspector Show/Hide 按钮

## 目标
- 实现一个“初始显示动画”:
  - 一开始不显示任何 splats.
  - 从中心开始出现发光(类似燃烧)的环形边缘,并向外扩散,逐步显示完整点云.
- 实现一个“隐藏动画”(反向语义但更像燃烧成灰):
  - 从中心开始更强的燃烧高亮环.
  - 环向外扩散,被扫过区域慢慢透明并消失.
  - 同时噪波(noise)越来越大,像烧成碎屑.
- 提供可控 API,并在 Inspector 上提供 "Show" / "Hide" 两个按钮用于触发动画.
- 默认关闭该效果,确保不影响旧项目的渲染表现与性能.

## 阶段
- [x] 阶段1: OpenSpec change 生成与 artifacts(proposal/design/specs/tasks)
- [x] 阶段2: Shader 与 Runtime 状态机落地(show/hide + noise)
- [x] 阶段3: Editor Inspector 按钮落地 + 回归测试补齐
- [x] 阶段4: 回归验证 + 文档/日志同步

## 关键决定
1. 中心以用户指定的 `Transform` 为准(可为空,为空时回退 bounds.center).
2. 噪波先用轻量 hash noise(不引入噪声贴图,降低依赖与资产新增).
3. hide/show 通过 API 控制,不依赖 `SetActive(false)` 或 `enabled=false`(否则隐藏动画无法播放).
4. Inspector 按钮不依赖 vInspector attribute 系统,而是通过包内自定义 Inspector(IMGUI)保证一定可用.

## 状态
**已完成** - 已按 OpenSpec tasks 全部落地并通过 Unity EditMode tests 回归.

### 2026-02-24 19:29:16 +0800
- 用户需求:
  - hide->show 时播放“中心燃烧发光环扩散显示”的初始动画.
  - show->hide 时播放“中心起燃,环扩散,噪波变大像碎屑,逐渐透明消失”的动画.
  - 希望在 Inspector 上有 show/hide 两个按钮快速触发验证.
- 本轮行动:
  - 先生成 OpenSpec change(带 proposal/design/specs/tasks).
  - 再按 tasks 逐项落地 shader + runtime 状态机 + inspector 按钮 + tests.

### 2026-02-24 19:40:00 +0800
- 已完成阶段1:
  - OpenSpec change: `openspec/changes/burn-reveal-visibility/`
  - artifacts: proposal/design/specs/tasks 已全部生成并通过 `openspec status` 验证.
- 下一步(阶段2):
  - 先落地 shader uniforms + hash noise + burn ring reveal.
  - 再落地 `GsplatRendererImpl` uniforms 下发与两个 renderer 的 show/hide 状态机.

### 2026-02-24 21:04:44 +0800
- 现状盘点:
  - OpenSpec apply 指令显示 12 个任务均未勾选,但其中 1.1/1.2/1.3/2.1 已经实际落地到代码.
  - 目前欠缺的是: 两个 renderer 的 show/hide 状态机与 API,以及确保在 Update 与 SRP 相机回调两条 draw 路径都能推本帧 uniforms.
- 本轮行动(继续阶段2->阶段4):
  - 补齐 `GsplatRenderer`/`GsplatSequenceRenderer` 的显隐状态机与 `SetVisible/PlayShow/PlayHide`.
  - 在所有 draw 提交入口前统一推送显隐 uniforms(包含 Editor 相机回调路径).
  - Editor inspector 增加 Show/Hide 按钮,并补齐最小回归测试与 CHANGELOG.
  - 最后更新 `openspec/changes/burn-reveal-visibility/tasks.md` 勾选状态,并按四文件模式收尾(WORKLOG/notes/LATER_PLANS).

### 2026-02-24 21:31:02 +0800
- 已完成阶段2/3/4 的全部落地:
  - Shader: reveal/burn uniforms + hash noise + show 起始(progress=0)强制全不可见.
  - Runtime: `GsplatRenderer`/`GsplatSequenceRenderer` 增加配置字段 + `SetVisible/PlayShow/PlayHide` + 状态机.
    - Hidden 状态下 `Valid=false`,从根源停止 sorter gather 与 draw 提交(停开销).
    - Update 与 EditMode SRP 相机回调两条路径渲染前都会推送显隐 uniforms.
  - Editor: Inspector 增加 Show/Hide 按钮(两种 renderer 都有).
  - Tests: 新增 `GsplatVisibilityAnimationTests`,锁定 hide->Hidden(Valid=false)与 show->Valid=true 的关键语义.
  - Changelog: Unreleased Added 补充该能力说明.
- 回归验证(证据型):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - 结果文件: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_2026-02-24_212906.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2
- OpenSpec 状态:
  - `openspec instructions apply --change burn-reveal-visibility --json` 显示 12/12 tasks 已完成,可进入 archive.

### 2026-02-24 23:41:13 +0800
- 用户新增改进需求:
  - show: 高斯基元从“无限小/极小”慢慢变成正常大小.
  - hide: 从正常大小慢慢缩到“无限小/极小”.
  - noise: 希望有“扭曲空间一样”的扭曲粒子效果,即 position 要有明显位移变化(目前不明显).
- 两条路线(先定方向再动代码):
  - 路线A(先能用,风险低): shader 内基于 passed/noise 做:
    - per-splat size 缩放(极小<->正常)
    - modelCenter 扭曲位移(阈值判定仍基于 basePos,避免闪烁)
    - C# 侧在动画期间保守扩 bounds,避免 CPU culling 裁掉位移后的 splats
  - 路线B(不惜代价,最佳稳态): compute 侧生成“扭曲后的临时 position buffer”并参与排序与 bounds:
    - 优点: 排序与渲染一致,扭曲幅度可更大,不会出现短暂排序误差
    - 代价: 需要额外 GPU buffer 与 compute pass,复杂度与显存成本更高
- 本轮选择:
  - [决定] 先实现路线A,把观感先做出来,同时把路线B 记录到 LATER_PLANS 作为二期候选.
- 结果:
  - shader 已增加 size scaling + 位置扭曲,并在 Showing/Hiding 期间扩 bounds.
  - 已跑 Unity EditMode tests 回归通过(见 WORKLOG 证据).

### 2026-02-24 23:55:30 +0800
- 用户新增改进需求(显隐燃烧环动画):
  - show: 高斯基元从“极小”更慢更明显地长到正常大小.
  - hide: 高斯基元从正常更明显地缩到“极小”.
  - noise: 希望是“扭曲空间”的扭曲粒子效果,需要位置(pos)有更明显的位移(当前不够明显).
- 本轮计划:
  - [x] 调整 shader 的 size easing,让 grow/shrink 更容易被肉眼感知.
  - [x] 调整 shader 的 warp 逻辑(幅度/权重/每-splat 相位),让位移更明显且更像空间扭曲.
  - [x] 视情况增加一个可调参数(例如 WarpStrength),让用户无需拉高 NoiseStrength 也能得到明显位移.
  - [x] 更新 OpenSpec change(burn-reveal-visibility)的 tasks/spec/changelog 记录.
  - [x] 跑 Unity EditMode tests 回归,确保编译与逻辑不回退.

### 2026-02-25 00:10:30 +0800
- 用户继续反馈(显隐燃烧环动画):
  - hide 时: splat 没有“从正常逐渐变小”,体感尺寸仍然很大.
    - show 的 size 变化基本满意.
  - noise: 仍然很混乱,不像烟雾.
    - 希望更像烟雾的“扭曲 + 波动”,建议基于 xyz + time 做更平滑的场.
- 本轮计划:
  - [x] hide size: 在 shader 中对 hide 增加更强的 shrink 曲线,并叠加一个 global progress 的整体缩放(让未被扫过区域也会逐渐变小).
  - [x] smoke noise: 把当前的 hash 白噪声替换/升级为更平滑的 value noise(基于 xyz + time),并用轻量 domain warp 让形态更像烟雾.
  - [x] 更新 OpenSpec change(burn-reveal-visibility)的 design/tasks 记录.
  - [x] 跑 Unity EditMode tests 回归,确保编译与逻辑不回退.

### 2026-02-25 01:25:00 +0800
- 用户继续反馈(显隐燃烧环动画):
  - show: ring/trail 的宽度希望与 hide 分开设置.
  - show: ring 阶段 size grow 过慢,导致看到的都是很小的点点; 但仍希望从极小开始.
  - hide: ring/trail 的方位希望反过来,当前体感像“trail 在外”,希望“trail 在内”.
- 本轮计划:
  - [x] 拆分 show/hide 的 RingWidthNormalized/TrailWidthNormalized 为两组参数,并保持序列化兼容.
  - [x] shader 调整 show size grow 曲线,让 ring 阶段更快变大但仍从极小开始.
  - [x] shader 调整 hide 的 ring 定义,让 ring 更像外侧前沿,trail(渐隐)落在内侧.
  - [x] 更新 OpenSpec spec/design/tasks 与 CHANGELOG.
  - [x] 跑 Unity EditMode tests 回归验证.

### 2026-02-25 15:40:00 +0800
- 用户新反馈(显隐燃烧环动画):
  - show/hide 播放时,如果鼠标不动(视口不触发 repaint),画面就不动,动画不会连续播放.
  - 需要在播放时间段内,即使没有鼠标交互,也能让动画持续推进并播放到结束.
- 本轮计划:
  - [x] 在 Editor 非 Play 模式下,当 Showing/Hiding 进行中时,主动触发 Editor 的渲染刷新(QueuePlayerLoopUpdate + RepaintAllViews).
  - [x] 增加节流与 batchmode 门禁,避免无意义的高频 Repaint 与测试环境干扰.
  - [x] 更新 OpenSpec change(burn-reveal-visibility)的 tasks/design/changelog 记录.
  - [x] 跑 Unity EditMode tests 回归,确保编译与逻辑不回退.

- 验证(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_editor_repaint_2026-02-25.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2

### 2026-02-25 16:25:00 +0800
- 用户反馈(继续): 依然存在“鼠标不动就不播放”.
  - 刚才用户实测: 鼠标不动,画面还是不动.
  - 用户建议: 增加 log/dump,判断到底哪条链路没跑起来.
- 初步判断(根因假设):
  - 仅靠在 `AdvanceVisibilityStateIfNeeded()` 内请求 Repaint 仍可能不足:
    - 如果 EditMode 下 `ExecuteAlways.Update`/相机回调没有被持续触发,就会出现“请求只发生一次,后续不再 tick”.
    - 在 Built-in Render Pipeline(BiRP)下,draw 仍主要由 Update 提交,更依赖 PlayerLoop.
- 本轮计划:
  - [x] 增加一个 EditorApplication.update 驱动的“显隐动画 ticker”,只在 Showing/Hiding 期间保持 tick+repaint,彻底打破鸡生蛋死循环.
  - [x] 增加可控的诊断日志(复用 `GsplatSettings.EnableEditorDiagnostics` 的 ring buffer),记录: start/finish, tick, repaint request.
  - [x] 更新 OpenSpec change(burn-reveal-visibility)记录,并跑 Unity EditMode tests 回归.

- 验证(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_editor_ticker_2026-02-25.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2

### 2026-02-25 17:05:00 +0800
- 用户新需求(显隐燃烧环动画):
  - show/hide 的“燃烧环扩散速度曲线”希望是 `easeInOutQuart`.
- 本轮计划:
  - [x] 在 shader 中引入 `EaseInOutQuart(progress)` 并用于计算扩散半径 `radius`.
  - [x] 与扩散强相关的全局效果(glow/globalWarp/globalShrink)也使用同一套 eased progress,保持节奏一致.
  - [x] 更新 OpenSpec change(burn-reveal-visibility)的 tasks/design/spec/changelog 记录.
  - [x] 跑 Unity EditMode tests 回归.
- 验证(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_easeInOutQuart_2026-02-25.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2

### 2026-02-25 17:30:00 +0800
- 用户新需求(显隐燃烧环动画):
  - 将燃烧环扩散速度曲线改为 `easeOutCirc`.
- 本轮计划:
  - [x] shader 将 `progressExpand` 从 `EaseInOutQuart` 切换为 `EaseOutCirc`.
  - [x] 与扩散节奏强相关的全局效果(glow/globalWarp/globalShrink)继续使用同一套 eased progress,保持节奏一致.
  - [x] 更新 OpenSpec change(burn-reveal-visibility)的 tasks/design/spec/changelog 记录.
  - [x] 跑 Unity EditMode tests 回归.
- 验证(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_easeOutCirc_2026-02-25.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2

### 2026-02-25 17:45:00 +0800
- 用户新需求(显隐燃烧环动画):
  - 将燃烧环扩散速度曲线改为 `easeInOutQuad`.
- 本轮计划:
  - [x] shader 将 `progressExpand` 从 `EaseOutCirc` 切换为 `EaseInOutQuad`.
  - [x] 同步更新 OpenSpec change(burn-reveal-visibility)的 tasks/design/spec/changelog 记录,避免规格与实现不一致.
  - [x] 跑 Unity EditMode tests 回归(若可用),至少确保没有编译错误与 tests 回退.
- 验证(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_easeInOutQuad_rerun_2026-02-25.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2

### 2026-02-25 18:20:00 +0800
- 用户新需求(显隐燃烧环动画):
  - 将 show/hide 期间的 warp 噪声进一步升级为更平滑的 noise(例如 value noise / curl-like).
  - 并提供一个下拉选项,可以在“当前效果”和“新 noise 效果”之间切换对比.
- 本轮计划:
  - [x] 增加 `VisibilityNoiseMode` 下拉枚举(默认保持当前行为),并在两个 renderer(GsplatRenderer/GsplatSequenceRenderer)暴露到 Inspector.
  - [x] shader 增加 `_VisibilityNoiseMode` uniform,并在 `GsplatRendererImpl.SetVisibilityUniforms(...)` 每帧写入.
  - [x] 实现更平滑的 curl-like 噪声场(基于 value noise 的梯度/旋度构造),用于 position warp 方向生成,让扭曲更像“连续的烟雾流动”.
  - [x] 更新 OpenSpec change(burn-reveal-visibility)的 tasks/design/spec,并补充 `CHANGELOG.md`.
  - [x] 跑 Unity EditMode tests 回归,确认编译与行为不回退.
- 验证(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_noise_mode_noquit_2026-02-25.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2

### 2026-02-25 19:10:00 +0800
- 用户新反馈(显隐燃烧环动画,继续调优):
  1) hide 时 glow 阶段 splat 仍偏大,希望进入 glow 时已经更小.
     - 候选: shrink 更早开始(在 glow 前预收缩),或把 glow/noise 效果整体后移.
  2) show 也需要一个 `GlowStartBoost`(类似 hide).
  3) hide 的 `HideGlowIntensity` / `HideGlowStartBoost` 体感“反了”:
     - 消失从中心向外.
     - 希望: 前沿突然更亮(Boost),并且衰减的尾巴应该“朝内”(中心方向),而不是随着扩散向外越来越弱导致外围突兀.
- 本轮计划:
  - [x] shader: 调整 hide 的 glow 构成:
    - 前沿 ring 使用 Boost(更亮).
    - 增加一个位于内侧的 afterglow tail,并随“向内”衰减(避免外围突兀).
  - [x] shader: hide 的 size shrink 提前开始(在 glow ring 出现时已明显变小).
  - [x] runtime: 增加 `ShowGlowStartBoost` 字段并下发到 shader.
  - [x] 更新 OpenSpec(change burn-reveal-visibility)的 tasks/spec/design 与 `CHANGELOG.md`.
  - [x] 跑 Unity EditMode tests 回归.
- 验证(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_glow_tuning_2026-02-25.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2

### 2026-02-25 20:05:00 +0800
- 用户新反馈(显隐燃烧环动画,hide size 节奏):
  - hide 燃烧时高斯基元粒子大小仍偏大.
  - 期望: 先迅速降低到比较小的 size,再慢慢消失.
  - 现状体感: 变得太小导致“看起来消失太快”.
  - 候选: hide 的 size 曲线尝试类似 `easeOutCirc` 的节奏(先快后慢).
- 本轮计划:
  - [x] shader: 调整 hide 的 size 曲线:
    - 在燃烧前沿附近快速 shrink 到“较小但仍可见”的 minScale.
    - 后续更多依赖 alpha trail 慢慢消失,避免 size 过早接近 0.
  - [ ] (如仍偏快) hide 的 alpha fade 对 passed 施加 easing,让 fade 更“前沿利落,尾巴更慢”.
  - [x] 更新 OpenSpec/CHANGELOG/四文件记录.
  - [x] 跑 Unity EditMode tests 回归.
- 验证(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_hide_size_easeOutCirc_2026-02-25.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2

### 2026-02-25 20:35:00 +0800
- 用户新反馈(show/hide 的 trail/glow 语义):
  - 质疑: `ShowTrailWidthNormalized` 是否在燃烧外部先到? 体感内部不够亮.
  - 需求确认: 希望保证/强化:
    - 前沿 ring 始终更亮(Boost).
    - 内侧有 afterglow tail,并朝内衰减(中心方向),这样内部更亮,外围不突兀.
- 本轮计划:
  - [ ] shader: 为 show 增加内侧 afterglow tail(朝内衰减),补足“内部不够亮”.
  - [ ] 检查 hide 是否仍满足“前沿 ring 更亮 + 内侧 tail 朝内衰减”的语义,必要时微调.
  - [ ] 更新 OpenSpec/CHANGELOG/四文件记录.
  - [ ] 跑 Unity EditMode tests 回归.

### 2026-02-25 20:10:00 +0800
- 用户继续反馈:
  - 怀疑 show 的 trail/glow 语义不对: `ShowTrailWidthNormalized` 体感像跑到外侧,内部不够亮.
  - 期望: 前沿 ring 永远更亮(可被 boost 放大),并且内侧 afterglow/tail 朝内衰减.
- 本轮落地:
  - [x] shader: 统一 show/hide 的 ring/tail 语义为:
    - ring 作为“燃烧前沿”,show/hide 都主要出现在外侧(edgeDist>=0),保证前沿永远在最外侧先到.
    - 内侧 afterglow/tail 只出现在内侧(edgeDist<=0),并朝内衰减,补足“内部不够亮”并避免外围突兀.
    - show: 由于 premul alpha 会把低 alpha 区域的 glow 吃掉,因此给 tail 一个受限的 alpha 下限,确保余辉肉眼可见.
  - [x] 更新 OpenSpec(change burn-reveal-visibility)的 tasks/spec/design 与 `CHANGELOG.md`.
  - [x] 跑 Unity EditMode tests 回归.
- 验证(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_show_trail_glow_semantics_2026-02-25_200342.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2
- 备注:
  - 运行 Unity 命令行 tests 时不要加 `-quit`,否则可能在 TestRunner 启动前提前退出,导致不生成 XML.

### 2026-02-25 20:20:00 +0800
- 用户新反馈:
  - hide 最最后会残留一些高斯基元很久才消失.
- 根因判断:
  - hide 的 passed/visible 若完全跟随 `edgeDistNoisy`,当噪声为正时会把边界往外推,
    导致局部 passed 长时间达不到 1,于是出现 lingering(末尾残留).
- 本轮落地:
  - [x] shader: hide 的 fade/shrink 使用 `edgeDistForFade`(仅允许噪声往内咬),禁止外推导致的 lingering.
  - [x] 保留 ring/glow 使用 `edgeDistNoisy`,不牺牲边界抖动质感.
  - [x] 更新 OpenSpec/CHANGELOG/四文件记录.
  - [x] 跑 Unity EditMode tests 回归.
- 验证(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_hide_lingering_fix_2026-02-25_201817.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2

### 2026-02-25 20:35:00 +0800
- 用户新需求:
  - show 的环状 glow 亮度希望有变化,像火星/星星闪闪.
  - 希望使用 curl noise,并提供强度可调.
- 本轮落地:
  - [x] runtime: 增加 `ShowGlowSparkleStrength` 参数(0=关闭),并作为 uniform 下发到 shader.
  - [x] shader: show 的 ringGlow 使用 curl-like 噪声场生成稀疏亮点 + 随时间 twinkle,形成“星火闪烁”观感.
  - [x] 更新 OpenSpec/CHANGELOG/四文件记录.
  - [x] 跑 Unity EditMode tests 回归.
- 验证(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_show_sparkle_2026-02-25_202927.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2

### 2026-02-25 20:45:00 +0800
- 用户调参需求:
  - Show 的默认参数希望微调:
    - `ShowRingWidthNormalized` 大 10%.
    - `ShowTrailWidthNormalized` 乘以 40%(缩短 trail).
- 本轮落地:
  - [x] 调整 `GsplatRenderer`/`GsplatSequenceRenderer` 的默认值:
    - `ShowRingWidthNormalized`: `0.06 -> 0.066`
    - `ShowTrailWidthNormalized`: `0.12 -> 0.048`
  - [x] 更新 OpenSpec/CHANGELOG/四文件记录.
  - [ ](可选) 若你希望“自动迁移已有 Prefab/场景里仍是旧默认值的对象”,再做一次显式 migration(当前未做,避免升级后意外改动旧场景观感).

### 2026-02-25 23:10:00 +0800
- 用户撤回说明:
  - 上一轮提到的 “ShowRingWidthNormalized 大 10% / ShowTrailWidthNormalized *40%” 实际指的是“高斯基元(粒子)大小”,不是 ring/trail 的径向空间宽度.
- 本轮计划:
  - [x] 撤回 show 的默认宽度微调(恢复 `0.06/0.12`),避免把“空间宽度”和“粒子大小”混淆.
  - [x] 为 show/hide 增加“粒子大小”调参项,并让 shader 在 ring/tail 阶段使用它,避免 ring 前沿全是很小的点点.
  - [x] 更新 OpenSpec/CHANGELOG/notes/WORKLOG 记录,并跑 Unity EditMode tests 回归.
- 验证(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_particle_size_tuning_2026-02-25_2317.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2

### 2026-02-25 23:30:00 +0800
- 用户新反馈(hide 余辉/afterglow):
  - hide 的 glow(前沿)扫过后,余辉粒子存在时间偏短/尺寸偏小.
  - 体感: glow 一过,后面的余辉几乎就全没了.
- 本轮计划:
  - [x] shader: hide 的 alpha fade 对 passed 做轻量 easing(先慢后快),让余辉更“拖尾”.
  - [x] shader: hide 的 size shrink 拆成“到达前沿时先 shrink 到 afterglow size,随后在 tail 内再慢慢 shrink 到最终 min”,避免一过前沿就直接变到极小.
  - [x] 更新 OpenSpec/CHANGELOG/notes/WORKLOG 记录,并跑 Unity EditMode tests 回归.
- 验证(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_hide_afterglow_linger_2026-02-25_2336.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2

### 2026-02-26 00:25:00 +0800
- 用户新反馈(hide ring/trail 相对位置):
  - 体感: `HideTrailWidthNormalized` 对应的拖尾区域看起来仍在外圈.
  - 期望: hide 的 trail 应该在 `HideRingWidthNormalized` 的内侧(前沿 ring 在外,拖尾在内).
- 本轮计划:
  - [x] shader: 限制 hide 阶段的 warp 不允许把 splat 往径向外侧推(避免位移把“内侧拖尾”推到外圈,造成错觉).
  - [ ](如仍不稳) hide 的 tailInside 判定改用更稳态的 `edgeDistForFade`(与 fade/shrink 同源),减少 ring 抖动导致的相对位置错觉.
  - [x] 更新 OpenSpec/CHANGELOG/notes/WORKLOG 记录,并跑 Unity EditMode tests 回归.
- 验证(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_hide_trail_inside_ring_warp_clamp_2026-02-26_0029.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2

### 2026-02-26 11:48:36 +0800
- 用户需求: 增加一种新的显示效果,并且可以通过 API 在“高斯基元常规显示效果”和“粒子圆片/圆点显示”之间切换.
  - 新效果: 类似粒子的圆片/圆点,大小可调.
  - 切换要求: 有动画效果,默认 `easeInOutQuart`,时长 1.5 秒.
- 本轮选择(已确认):
  - 大小语义: 屏幕像素半径(px radius).
  - 点外观: 实心 + 柔边(soft edge).
  - 切换动画: 形态渐变(morph),尽量保持单次 draw(避免双绘制 crossfade 带来的性能与叠加差异).
- 注意(文件上下文维护):
  - `WORKLOG.md` 已超过 1000 行,需要先按规则续档(加日期重命名 + 新建空白 WORKLOG),并执行一次 continuous-learning 总结,避免后续记录继续膨胀.
- 本轮计划:
  - [x] 续档 `WORKLOG.md`(超过 1000 行),并做 continuous-learning 复盘沉淀.
  - [x] Runtime: 新增 `GsplatRenderStyle(Gaussian/ParticleDots)` 与 API `SetRenderStyle(...)`,支持 1.5s easeInOutQuart 动画切换.
  - [x] Runtime: 新增 dot 参数 `ParticleDotRadiusPixels`,并通过 MPB 下发 `_RenderStyleBlend/_ParticleDotRadiusPixels`.
  - [x] Shader: `Gsplat.shader` 增加 dots 渲染与 morph 逻辑,`blend=0` 时保持旧行为不变.
  - [x] Tests: 增加 easing 单测,增加 render style 动画状态机单测(反射推进,避免依赖 Editor PlayerLoop).
  - [x] Docs: 更新 `README.md` 与 `CHANGELOG.md`.
  - [x] 四文件: `notes.md` 记录关键决策与实现要点,`WORKLOG.md` 记录最终结果,`LATER_PLANS.md` 如有二期项再追加.

### 2026-02-26 12:26:00 +0800
- 已完成: RenderStyle(ParticleDots) 与动画切换.
- 回归(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - total=30, passed=28, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_renderstyle_2026-02-26_noquit.xml`

### 2026-02-26 12:30:00 +0800
- 用户反馈:
  - Inspector 的 RenderStyle 下拉切换没有看到动画.
  - 期望: 在面板上提供按钮,可以触发“动画切换”.
- 说明(根因):
  - 下拉框修改的是序列化字段,会触发 OnValidate 做“参数同步”,默认是硬切,不会走 `SetRenderStyle(..., animated:true)` 的动画状态机.
- 本轮计划:
  - [x] Editor: `GsplatRenderer` Inspector 增加两个按钮(`Gaussian(动画)` / `ParticleDots(动画)`),通过 `SetRenderStyle(..., animated:true)` 触发切换动画.
  - [x] Editor: `GsplatSequenceRenderer` Inspector 同步增加两个按钮.
  - [x] Editor: 在按钮型 API 调用前先 `ApplyModifiedProperties`,避免按钮读取到旧的 duration/参数导致“改了但没生效”的错觉.
  - [x] 回归: 跑 Unity EditMode tests 确保无编译错误与回归.

- 回归(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - total=30, passed=28, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_renderstyle_inspector_buttons_2026-02-26.xml`

### 2026-02-26 13:33:00 +0800
- 用户新反馈(RenderStyle 切换 pop):
  - Gaussian -> ParticleDots: 动画尾部有部分远处/靠屏幕外缘的 splat 突然消失.
  - ParticleDots -> Gaussian: 动画开头同一批 splat 突然出现.
- 根因:
  - shader 在 `styleBlend==1` 时跳过 Gaussian corner 计算.
  - 当 dotCorner 因 frustum cull 不可用时,vertex 会被直接 discard,导致动画头尾 pop.
- 本轮计划:
  - [x] Shader: 调整 RenderStyle corner 计算顺序:
    - 先算 dotCorner.
    - `styleBlend<1` 或 dotCorner 不可用时才算 gaussCorner 作为兜底几何.
  - [x] Shader: fragment 增加 `uvDot`(屏幕像素半径归一化),并改为“两种核都不贡献才 discard”,让 pop 变成平滑淡出/淡入.
  - [x] 回归: 跑 Unity EditMode tests.
- 回归(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - total=30, passed=28, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_renderstyle_popfix_2026-02-26.xml`

### 2026-03-01 15:39:48 +0800
- 用户新需求(讨论已收敛): ParticleDots(粒子模式)增加"车载 LiDAR 采集"显示效果.
  - 目标: 高斯基元(splat)可完全不显示,仅作为"环境采样点";最终渲染一套新生成的规则点云,让扫描线更整齐.
  - 关键语义: 第一回波(遮挡更准确).
  - 扫描参数:
    - 360 度.
    - 5 Hz 旋转(1 圈 0.2s).
    - 1 圈保留,且有扫描前沿+余辉(扫描头更亮,后方逐渐变暗).
    - UpdateHz=10Hz(每 0.1s 全量重建一次 range image).
  - 采样分辨率:
    - BeamCount=128,竖直分层环,上少下多(竖直视场由我先推荐默认值,后续可调或改 LUT).
    - AzimuthBins=2048.
  - 颜色模式:
    - Depth: DepthNear=1m,DepthFar=200m.
    - SplatColor: 采集自高斯基元的基础颜色(SH0).
  - 点大小: 默认 2px,可调.
  - LiDAR 安装姿态: 由 Transform 真实摆放(位置+朝向).

- 本轮动作(只做规格工单,不落地代码):
  - [x] 生成一个新的 OpenSpec change,用于承载后续实现与验收.
  - [x] 显示该 change 的 artifact 状态,并打印第一个 artifact 的 instructions 模板.

### 2026-03-01 15:58:54 +0800
- 用户指令: `$openspec-ff-change`,希望一次性生成所有 artifacts,快速进入实现阶段.
- 本轮落地(规格工单完结,仍未开始写实现代码):
  - [x] 完成 OpenSpec change `particle-dots-lidar-scan` 的全部 artifacts(4/4):
    - `openspec/changes/particle-dots-lidar-scan/proposal.md`
    - `openspec/changes/particle-dots-lidar-scan/design.md`
    - `openspec/changes/particle-dots-lidar-scan/specs/gsplat-lidar-scan-visualization/spec.md`
    - `openspec/changes/particle-dots-lidar-scan/tasks.md`
  - [x] 为新增 change 目录与 md 文件补齐 Unity `.meta`(保持与现有 openspec change 一致).
- 当前状态:
  - OpenSpec: artifacts 已 complete,可进入 apply/implementation 阶段.

### 2026-03-01 17:06:00 +0800
- 开始实现 OpenSpec change: `particle-dots-lidar-scan`(schema: spec-driven).
- 目标:
  - 增加可选的"车载 LiDAR 采集显示"(默认关闭),不影响现有 Gaussian/ParticleDots.
  - first return(第一回波): 每个 (beam,azBin) 只保留最近距离.
  - 默认分辨率: 128 beams x 2048 azimuth bins.
  - 时间语义: RotationHz=5(扫描前沿),UpdateHz=10(0.1s 全量重建),保留 1 圈余辉.
  - 颜色模式: Depth(1m..200m) / SplatColor(SH0).
  - 点大小: 2px radius 默认,可调.
  - 可选: 隐藏 splat 渲染但保留 LiDAR 采样.
- 本轮计划:
  - [ ] 1) Runtime: 增加 LiDAR 字段与 clamp.
  - [ ] 2) Runtime: range image/LUT 资源生命周期.
  - [ ] 3) Compute: Clear/Reduce/Resolve kernels + UpdateHz 调度.
  - [ ] 4) Render: LiDAR 点云 shader + draw 提交.
  - [ ] 5) Editor: Inspector 调参区 + EditMode repaint 驱动.
  - [ ] 6) Tests/Docs: 最小回归 + README/CHANGELOG.
  - [ ] 7) 回归验证 + git commit.
- 当前状态:
  - apply 0/24 tasks.

### 2026-03-01 20:31:32 +0800
- 我正在继续推进 OpenSpec change: `particle-dots-lidar-scan` 的 apply 阶段.
- 当前进展(已落地但尚未提交):
  - apply tasks 1-4 已完成(14/24).
  - Runtime 已新增 LiDAR 采集显示的 compute+shader+资源管理,并已在 `GsplatRenderer`/`GsplatSequenceRenderer` 接入:
    - `Runtime/Lidar/GsplatLidarScan.cs`: range image/LUT/UpdateHz 门禁/compute dispatch/draw 提交.
    - `Runtime/Shaders/Gsplat.compute`: 新增 Clear/Reduce/Resolve kernels.
    - `Runtime/Shaders/GsplatLidar.shader`: LiDAR 规则点云圆点渲染(深度色/SH0 采样色,扫描前沿+余辉).
    - `Runtime/GsplatRenderer.cs`/`Runtime/GsplatSequenceRenderer.cs`: 新增序列化字段,clamp,UpdateHz 调度与渲染提交.
    - `Runtime/GsplatSettings.cs`: 新增 LiDAR shader/material 引用与自动创建.
- 之所以还要继续做:
  - 目前 tasks 5-8 还未完成,包括“隐藏 splat 但 LiDAR 仍可采样”的完整门禁,Inspector 调参区,EditMode 连续刷新,单测与文档.
- 接下来计划(继续按 tasks.md 逐项完成并勾选):
  - [ ] 5) splat 隐藏与资源复用: 彻底解耦"保持 splat buffers"与"提交 splat sort/draw",补齐 `GsplatSequenceRenderer` 的门禁.
  - [ ] 6) Editor: 为两个 Renderer 的 Inspector 增加 LiDAR 调参区,并在 EditMode 下仅在 EnableLidarScan 时驱动受控 Repaint.
  - [ ] 7) Tests: 增加 EditMode 单测(参数 clamp + UpdateHz 门禁,不跑 GPU).
  - [ ] 8) Docs: 更新 README/CHANGELOG,并补一份手动验证清单.
  - [ ] 9) 回归: 跑 Unity EditMode tests,确保无编译错误与行为回归.
  - [ ] 10) git commit: 提交本次变更(含 `.meta`).
- 当前状态:
  - apply 14/24 tasks,remaining 10.

### 2026-03-01 20:47:37 +0800
- 我已经完成 OpenSpec change `particle-dots-lidar-scan` 的全部实现任务:
  - apply 24/24 tasks.
- 关键补齐点(相较于 14/24 时的状态):
  - `GsplatSequenceRenderer` 补齐 splat sort/draw 与 LiDAR 的解耦门禁,HideSplatsWhenLidarEnabled 时不提交 splat sort/draw,但 LiDAR 正常.
  - Editor: 两个 Inspector 增加 LiDAR 调参区 + 有效网格提示 + Origin 缺失提示.
  - EditMode: 启用 LiDAR 时受控驱动 Repaint,保证扫描前沿/余辉连续播放.
  - Tests: 增加 clamp 与 UpdateHz 门禁回归.
  - Docs: README/CHANGELOG 更新,补手动验证清单.
- 回归(证据):
  - 注意: 主项目当前被另一个 Unity 实例占用(项目锁),因此测试在独立临时项目 `.../_tmp_gsplat_pkgtests` 上执行.
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - total=33, passed=31, failed=0, skipped=2
  - XML:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_particle_dots_lidar_scan_2026-03-01_204400.xml`
  - log:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_particle_dots_lidar_scan_2026-03-01_204400.log`
- 下一步(马上做):
  - [ ] git commit: 提交本次变更(含 `.meta`).

### 2026-03-01 20:49:39 +0800
- 已完成 git 提交:
  - `25de60a` `feat: LiDAR scan visualization`
- 当前状态:
  - OpenSpec change `particle-dots-lidar-scan` apply 24/24 已完成,且已提交到 git.
  - 若你希望收尾归档 OpenSpec change,下一步可以执行 `$openspec-archive-change particle-dots-lidar-scan`.

### 2026-03-01 21:47:41 +0800
- 用户新反馈与新需求:
  1) LiDAR 的竖直线束分布不要再拆成 `LidarUpBeams/LidarDownBeams`,希望上下统一,只保留一个 BeamCount.
  2) LiDAR 点云看起来离高斯点云太远,像在外面包了一层厚壳,需要解释原因并修正.
- 初步诊断(why):
  - 当前 range image 存的是 `|pos|`(到 LiDAR 原点的欧氏距离),但渲染时用的是"bin center 的离散射线方向".
  - 当点不在射线中心方向上时,把 `|pos|` 强行放到射线上会把点推到更远的位置,视觉上就像外面包了一层壳.
- 本轮计划(准备落地修正):
  - [x] 1) API/Inspector: 用 `LidarBeamCount` 替代 `LidarUpBeams/LidarDownBeams`,竖直分布按 `[DownFovDeg..UpFovDeg]` 匀角度采样生成 LUT.
  - [x] 2) first return range 语义修正: range image 存"沿离散射线方向的 depth"(projection),而不是 `|pos|`,消除厚壳偏移.
  - [x] 3) 更新 compute/LUT/Editor/Tests/Docs,跑 EditMode tests 回归.

### 2026-03-01 22:12:00 +0800
- 已完成本轮修正:
  - API/Inspector: 移除 Up/Down beams,统一为 `LidarBeamCount`.
  - LUT: 竖直方向改为在 `[LidarDownFovDeg..LidarUpFovDeg]` 做匀角度采样(上下统一).
  - compute: range image 由存 `|pos|^2` 改为存 `depth^2`,其中 `depth=dot(posLidar,dirBinCenter)`(消除“厚壳”外推).
  - Tests/Docs: 已同步更新 EditMode tests 与 README 文案.
- 回归(证据):
  - Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - total=33, passed=31, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_beamcount_shellfix_2026-03-01_221012.xml`
- 下一步:
  - git commit: 等你确认后,我再提交这一轮改动.

### 2026-03-01 22:52:00 +0800
- 用户反馈: LiDAR 点云仍然有“包一层”的观感.
- 进一步根因分析:
  - 仅修正 `|pos|` 外推还不够.
  - LiDAR first return 采样如果不对齐渲染可见性,会命中一些:
    - 4D 时间窗外(渲染会 discard)的 splats
    - 或者 opacity 极低(渲染几乎不可见)的透明噪声 splats
  - 这些 splats 在渲染里不明显,但在 LiDAR 里会被当作“最近障碍”,于是形成一层“透明外壳”.
- 修正策略:
  - compute 侧采样对齐渲染:
    - 4D: window/gaussian 时间核裁剪 + 速度位移(与主 shader 一致)
    - opacity: 增加 `LidarMinSplatOpacity` 阈值过滤(默认 1/255)
  - Inspector: 暴露 `LidarMinSplatOpacity`,便于按资产噪声水平调参.
- 回归(证据):
  - Unity 6000.3.8f1,EditMode tests: total=33, passed=31, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_opacity_filter_2026-03-01_225104.xml`

### 2026-03-01 23:56:00 +0800
- 用户新需求(观感):
  - LiDAR 点云不要“透明发灰”,亮度要更正常.
  - 点形改为正方形.
  - Depth 颜色用常见深度渐变: cyan -> red.
- 落地:
  - `GsplatLidar.shader` 改为 additive blend(且不污染 alpha 通道),并把点形从圆改为正方形(软边).
  - Depth 配色改为 HSV 渐变(hue 180°->0°),即 cyan -> red.
- 回归(证据):
  - Unity 6000.3.8f1,EditMode tests: total=33, passed=31, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_shader_square_2026-03-01_235423.xml`

### 2026-03-02 00:25:57 +0800
- 用户反馈: LiDAR 的 Depth 渐变虽然也是 cyan -> red,但路径变成了 cyan -> blue -> purple -> red,不符合“惯用深度色带”的直觉.
- 根因分析:
  - 我们之前用 HSV 做 hue 插值时,red 端被当成了 hue=1.0(360°),而不是 hue=0.0.
  - 于是 hue 会沿着 0.5(cyan) -> 1.0(red) 这条路径走,自然经过 blue/purple.
- 修正计划:
  - [x] Shader: Depth 色带改为可控的分段色带(cyan -> green -> yellow -> red),避免蓝/紫过渡.
  - [x] Docs: README/CHANGELOG 同步文案,让预期与实现一致.
  - [x] 回归: Unity EditMode tests(确保编译与用例都稳定).
    - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_depth_colormap_2026-03-02_002839_noquit.xml`
  - [x] git commit: `278742e`(fix: LiDAR depth colormap).

### 2026-03-02 00:37:38 +0800
- 用户澄清需求: 你要的 Depth 色带就是 "青 -> 蓝 -> 紫 -> 红".
  - 也就是 HSV hue 走 0.5(cyan) -> 1.0(red/360°) 的那条路径,而不是走 0.5 -> 0.0(经过绿/黄)的路径.
- 调整策略:
  - [x] Shader: Depth 色带恢复为 HSV 渐变(0.5 -> 1.0),确保路径经过 blue/purple.
  - [x] Docs: README/CHANGELOG 同步文案描述.
  - [x] 回归: Unity EditMode tests.
    - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_depth_colormap2_2026-03-02_003813_noquit.xml`
  - [x] git commit: `5299fab`(fix: restore LiDAR HSV depth gradient).

### 2026-03-02 11:18:15 +0800
- 用户需求: LiDAR 点云在 `LidarColorMode=Depth` 时,需要一个可调的“透明度/可见性”设置.
  - 目的: Depth 色彩模式下,可以按场景/底图亮度调整点云的覆盖感,而不必只能靠 `LidarIntensity` 硬顶亮度.
- 修正计划:
  - [x] Runtime: 新增序列化字段 `LidarDepthOpacity(0..1)` 并 clamp.
  - [x] Shader: 增加 `_LidarDepthOpacity`,仅在 Depth 模式下参与强度计算.
  - [x] Editor: Inspector 面板增加该字段的调参入口(并标注仅 Depth 生效).
  - [x] Tests: 补充 clamp 单测覆盖.
  - [x] Docs: README/CHANGELOG 补充该参数说明.
  - [x] 回归: Unity EditMode tests.
    - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_depth_opacity_2026-03-02_112159_noquit.xml`
  - [x] git commit: `1362d14`(feat: LiDAR depth opacity).

### 2026-03-02 11:57:33 +0800
- 用户澄清: 你要的是“真正的不透明”,也就是:
  - `LidarDepthOpacity=1` 时,点云颜色应当覆盖背景(不再受底图混色影响).
  - 而不是 additive blend 那种“亮度叠加/像发光”的观感.
- 根因:
  - 之前 LiDAR shader 采用 additive blend,因此即便暴露了 `LidarDepthOpacity`,它也只能表现为亮度/覆盖率缩放,无法得到“真正的 alpha 不透明”.
- 修正策略:
  - [x] Shader: LiDAR 点云改为 alpha blend,并用 `ColorMask RGB` 保持不写入 alpha 通道.
  - [x] Shader: `LidarDepthOpacity` 改为直接参与 alpha(仅 Depth 模式),从而得到真实不透明/透明控制.
  - [x] Docs: README/CHANGELOG 同步渲染方式描述(从 additive 改为 alpha blend).
  - [x] 回归: Unity EditMode tests.
    - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_depth_opacity_alpha_2026-03-02_115702_noquit.xml`
  - [x] git commit: `e6e5615`(fix: LiDAR depth points opaque).

### 2026-03-02 12:32:15 +0800
- 用户反馈: 改成 alpha blend 后,点云/粒子之间遮挡关系看起来不正常(远近穿插).
- 根因:
  - alpha blend + `ZWrite Off` 时,透明物体的正确遮挡需要严格的 back-to-front 排序.
  - 我们的点云是单次 instanced draw,没有 per-point 排序,因此会出现遮挡乱序.
- 修复策略:
  - [x] Shader: 开启 `ZWrite On`,让更近的点写入深度并稳定遮挡更远的点.
  - [x] 回归: Unity EditMode tests.
    - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_zwrite_2026-03-02_123150_noquit.xml`
  - [x] git commit: `2bf675c`(fix: LiDAR point occlusion).

---

## 2026-03-02 14:56:03 +0800
# 任务计划: RenderStyle 增加 "雷达 scan" 按钮与双向切换

## 目标
- 在 `GsplatRenderer` 和 `GsplatSequenceRenderer` 的 Inspector 中新增 "RadarScan(动画)" 按钮.
- 支持从 `Gaussian` 或 `ParticleDots` 一键切换到雷达 scan 观感.
- 支持从雷达 scan 观感再切回 `Gaussian` / `ParticleDots`.
- 不破坏现有渲染风格动画与 LiDAR 参数行为.

## 两条路线(先明确)
### 路线A(不惜代价,最佳方案)
- 新增统一的高层 API(例如 `SetRadarScanEnabled(bool, animated)` + 恢复前样式记忆),
- 把 "RenderStyle" 与 "EnableLidarScan" 的协同状态机沉淀到 Runtime,
- Inspector 只调用统一 API.
- 优点: 语义最清晰,后续扩展成本低.
- 代价: 变更面稍大,需要补更多测试.

### 路线B(先能用,后续再优雅)
- 保持现有 Runtime 字段不大改,
- 在 Inspector 按钮层完成组合动作:
  - RadarScan 按钮: 开启 `EnableLidarScan`,并切到 `ParticleDots`.
  - Gaussian/ParticleDots 按钮: 关闭 `EnableLidarScan`,再走现有风格动画 API.
- 优点: 低风险,可以快速满足交互需求.
- 代价: 状态编排分散在 Editor 侧,后续可能再收敛 API.

## 本轮决策
- [决定] 先落地路线B.
- [理由] 你这次需求是 "增加按钮并完成双向切换",路线B能最小影响现有 runtime,并且与当前架构一致.

## 阶段
- [ ] 阶段1: 定位现有 RenderStyle/LiDAR 按钮与状态入口
- [ ] 阶段2: 实现 RadarScan 按钮与双向切换逻辑
- [ ] 阶段3: 增加/更新测试并跑回归
- [ ] 阶段4: 文档与四文件收尾

## 状态
**目前在阶段1**
- 正在定位 `Editor/*RendererEditor.cs` 与 Runtime API,确认最稳妥改造点.

### 2026-03-02 15:03:12 +0800
- 阶段1完成: 已定位并确认改造入口.
  - Editor: `Editor/GsplatRendererEditor.cs`、`Editor/GsplatSequenceRendererEditor.cs` 已存在 RenderStyle 动画按钮区.
  - Runtime: `SetRenderStyle(...)` 与 `EnableLidarScan/HideSplatsWhenLidarEnabled` 语义清晰,可安全组合.
- 下一步进入阶段2:
  - 在 Runtime 增加统一组合 API(风格+雷达开关).
  - Editor 新增 `RadarScan(动画)` 按钮,并让 Gaussian/ParticleDots 按钮自动关闭雷达模式.

## 阶段
- [x] 阶段1: 定位现有 RenderStyle/LiDAR 按钮与状态入口
- [ ] 阶段2: 实现 RadarScan 按钮与双向切换逻辑
- [ ] 阶段3: 增加/更新测试并跑回归
- [ ] 阶段4: 文档与四文件收尾

## 状态
**目前在阶段2**
- 正在实现 Runtime 组合 API 与 Inspector 新按钮联动.

### 2026-03-02 15:10:44 +0800
- 阶段2已完成代码实现:
  - Runtime: `GsplatRenderer` / `GsplatSequenceRenderer` 新增 `SetRenderStyleAndRadarScan(...)`.
  - Editor: Render Style 区新增 `RadarScan(动画)` 按钮.
  - 联动语义:
    - RadarScan 按钮 -> 启用 `EnableLidarScan`,默认 `HideSplatsWhenLidarEnabled=true`,并强制切到 `ParticleDots`.
    - Gaussian / ParticleDots 按钮 -> 关闭 `EnableLidarScan`,并播放对应风格动画.
- 下一步进入阶段3:
  - 新增/更新测试覆盖双向切换语义.
  - 执行 EditMode tests 回归.

## 阶段
- [x] 阶段1: 定位现有 RenderStyle/LiDAR 按钮与状态入口
- [x] 阶段2: 实现 RadarScan 按钮与双向切换逻辑
- [ ] 阶段3: 增加/更新测试并跑回归
- [ ] 阶段4: 文档与四文件收尾

## 状态
**目前在阶段3**
- 正在执行测试并验证无回归.

### 2026-03-02 15:19:06 +0800
- 阶段3完成: 测试回归通过.
  - 执行环境: Unity 6000.3.8f1 (`/Applications/Unity/Hub/Editor/6000.3.8f1/Unity.app/Contents/MacOS/Unity`).
  - 命令: `-runTests -testPlatform EditMode -testFilter Gsplat.Tests`.
  - 结果: total=35, passed=33, failed=0, skipped=2.
  - 新增用例通过:
    - `SetRenderStyleAndRadarScan_SupportsRendererBidirectionalSwitch`
    - `SetRenderStyleAndRadarScan_SupportsSequenceBidirectionalSwitch`
- 下一步进入阶段4:
  - 更新 changelog 与四文件收尾.

## 阶段
- [x] 阶段1: 定位现有 RenderStyle/LiDAR 按钮与状态入口
- [x] 阶段2: 实现 RadarScan 按钮与双向切换逻辑
- [x] 阶段3: 增加/更新测试并跑回归
- [ ] 阶段4: 文档与四文件收尾

## 状态
**目前在阶段4**
- 正在整理最终变更说明并更新 WORKLOG/LATER_PLANS/notes.

### 2026-03-02 15:23:15 +0800
- 阶段4完成:
  - `CHANGELOG.md` 已同步本次新增行为.
  - `notes.md` / `WORKLOG.md` / `LATER_PLANS.md` 已追加本轮记录.
  - 回归证据已归档到 `_tmp_gsplat_pkgtests/Logs`.

## 阶段
- [x] 阶段1: 定位现有 RenderStyle/LiDAR 按钮与状态入口
- [x] 阶段2: 实现 RadarScan 按钮与双向切换逻辑
- [x] 阶段3: 增加/更新测试并跑回归
- [x] 阶段4: 文档与四文件收尾

## 状态
**已完成**
- RenderStyle 动画区已支持 RadarScan 按钮与双向切换,并已通过回归测试.

### 2026-03-02 15:33:18 +0800
- 新反馈处理(增量修复):
  - 问题1: 雷达点云 `Depth -> SplatColor` 为硬切,缺少颜色过渡动画.
  - 问题2: `RadarScan -> Gaussian/ParticleDots` 为硬切,缺少雷达效果淡出动画.
- 增量修复计划:
  - [ ] Runtime: 增加 LiDAR 颜色混合动画状态(`colorBlend01`)与雷达可见性动画状态(`visibility01`).
  - [ ] Shader: 增加 `_LidarColorBlend` / `_LidarVisibility` uniform,实现颜色与可见性平滑过渡.
  - [ ] Editor: 增加 LiDAR 颜色模式动画按钮(`Depth(动画)`/`SplatColor(动画)`).
  - [ ] Tests: 新增动画状态推进回归用例.
  - [ ] 回归: 重新跑 `Gsplat.Tests`.

## 状态
**增量修复进行中**
- 正在实现 Runtime + Shader 动画状态机.

### 2026-03-02 16:10:00 +0800
- 继续处理你最新反馈的动画缺口闭环:
  - 先逐项自检 Runtime/Shader/Editor/Test 的 diff,确认 `Depth<->SplatColor` 和 `RadarScan<->其他风格` 都是平滑过渡而非硬切.
  - 再执行 Unity EditMode tests(`Gsplat.Tests`)做一次完整回归.
  - 最后补齐 `notes.md`/`WORKLOG.md`/`LATER_PLANS.md`/`ERRORFIX.md` 的尾部记录并将阶段状态改为完成.

## 状态
**增量修复验证中**
- 正在执行最终代码自检与回归测试.

### 2026-03-02 16:21:00 +0800
- 增量修复已完成并通过回归:
  - [x] Runtime: 增加 LiDAR 颜色混合动画(`m_lidarColorBlend01`)与雷达可见性动画(`m_lidarVisibility01`),并在关闭雷达时通过 `m_lidarKeepAliveDuringFadeOut` 播放淡出.
  - [x] Shader: 增加 `_LidarColorBlend` / `_LidarVisibility`,实现 `Depth <-> SplatColor` 渐变与 `RadarScan` 淡入淡出.
  - [x] Editor: LiDAR Visual 区新增 `Depth(动画)` / `SplatColor(动画)` 按钮.
  - [x] Tests: 新增 `SetLidarColorMode_Animated_ReachesTargetBlend` 与 `SetRadarScanEnabled_Animated_FadesOutVisibility`.
  - [x] 回归: Unity EditMode `Gsplat.Tests` 通过.
    - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_radar_anim_fix_2026-03-02_1612_noquit.xml`

## 状态
**增量修复已完成**
- 你反馈的两个硬切点已改为平滑动画,并有自动化测试覆盖.

### 2026-03-02 16:35:00 +0800
- 新增细节修复任务(你最新反馈):
  - 现象: `Gaussian/ParticleDots -> RadarScan` 时出现黑场,随后雷达粒子再渐显.
  - 初步判断: `HideSplatsWhenLidarEnabled=true` 在切换起点立即生效,导致 splat 被先停掉,而雷达还在 fade-in.
- 本轮计划:
  - [ ] 定位 splat 提交门禁与 `HideSplatsWhenLidarEnabled` 的生效时序.
  - [ ] 改为对称过渡: 入雷达期间延迟隐藏 splat,直到雷达可见性达到阈值(或动画结束).
  - [ ] 增加回归测试覆盖“入雷达不黑场”语义.
  - [ ] 运行 Unity EditMode tests 并更新四文件.

## 状态
**新一轮增量修复进行中**
- 正在定位 black frame 的具体门禁触发点.

### 2026-03-02 16:30:00 +0800
- 本轮 black frame 细节修复完成:
  - [x] 已定位门禁: `HideSplatsWhenLidarEnabled` 在入雷达起点立即生效,导致 splat 先停掉.
  - [x] Runtime 修复: 入雷达 fade-in 期间延迟隐藏 splat,待雷达可见性接近完成后再隐藏.
  - [x] 覆盖 `GsplatRenderer` 与 `GsplatSequenceRenderer` 两个后端.
  - [x] 新增回归测试: `SetRenderStyleAndRadarScan_Animated_DelayHideSplatsUntilRadarVisible`.
  - [x] 回归通过: `Gsplat.Tests` total=38, passed=36, failed=0, skipped=2.
    - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_radar_enter_no_black_2026-03-02_1640_noquit.xml`

## 状态
**新一轮增量修复已完成**
- 入雷达不再出现“先黑场再渐显雷达”的断层.

### 2026-03-02 16:41:00 +0800
- 进入交付动作: 按你的要求执行 git 提交.
- 提交范围: 本轮已验证通过的 RadarScan 切换动画修复(含 black frame 细节修复)与对应测试/文档/工作记录.
