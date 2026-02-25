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
  - [ ] shader 将 `progressExpand` 从 `EaseOutCirc` 切换为 `EaseInOutQuad`.
  - [ ] 同步更新 OpenSpec change(burn-reveal-visibility)的 tasks/design/spec/changelog 记录,避免规格与实现不一致.
  - [ ] 跑 Unity EditMode tests 回归(若可用),至少确保没有编译错误与 tests 回退.
