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
