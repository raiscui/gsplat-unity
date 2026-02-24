# 笔记: Unity Editor 下 Gsplat 闪烁(2026-02-24)

## 现象(用户反馈)
- SceneView 内: 鼠标在 SceneView 的 UI(overlay/toolbar/tab)上滑动,画面仍会“显示/不显示”闪烁.
- GameView 内: 也会出现“偶发整帧消失”.
- Console 不一定有可用报错/警告,因此需要靠诊断日志做证据采集.

## 已有证据(来自 `[GsplatDiag]` dump)
- 同一 `Time.frameCount` 内,同一个 SceneView camera 会触发多次:
  - `[CAM_RENDER] phase=BeginCameraRendering ...`
  - 并交替出现:
    - `[SORT] ...`
    - `[SORT_SKIP] ... reason=GatherGsplatsForCamera returned false`
- 同一帧内的 draw 证据通常只有一条:
  - `[DRAW] tag=EditMode.SceneView.FallbackActive ...`

## 初步推断(时序层面)
- Unity Editor 下,SceneView 可能在同一帧序号内触发多次渲染调用(repaint/overlay 等).
- 我们当前 draw 的提交点主要在 `ExecuteAlways.Update`:
  - 这会导致: 每次 Update 只提交一次 draw.
  - 当相机在同一帧内渲染多次时,其中部分 render invocation 可能没有 draw,从而表现为闪烁.

## 下一步需要验证的点
1. `GatherGsplatsForCamera` 返回 false 的具体原因需要细分(区分“每帧只排序一次”的 guard vs 其它门禁).
2. 需要用诊断把 “每次 BeginCameraRendering 是否都有对应 draw” 关联起来(按 render invocation 计数/序号).
3. 若确认 draw 确实无法覆盖多次 render invocation,则将 EditMode 的 draw 提交策略对齐到相机回调链路.

---

## 2026-02-24 11:15:00 +0800: 根因确认与修复落地(相机回调驱动 EditMode draw)

### 根因(更具体)
- `GsplatRenderer/GsplatSequenceRenderer` 的主渲染提交点在 `ExecuteAlways.Update()`:
  - 每次 Update 只会调用一次 `m_renderer.Render(...)`.
- 但在 Unity Editor(SRP)中,同一 `Time.frameCount` 里可能出现多次 `RenderPipelineManager.beginCameraRendering`:
  - 这些额外的 render invocation 常由 SceneView 的 overlay/UIElements、repaint、内部渲染阶段触发.
- 结果就是:
  - “相机渲染调用次数 > draw 提交次数”.
  - 部分 render invocation 没有 splats,最终显示出来的那一次恰好没 draw 时,体感就是“闪一下/消失”.

### 修复策略(路线A落地)
目标是把“draw 提交”与“相机渲染回调”对齐,让每次相机渲染都能拿到 splats.

#### 1) `GsplatSorter` 在 beginCameraRendering 内补交 draw
- 落点: `Runtime/GsplatSorter.cs`
- 做法:
  - 在 `OnBeginCameraRendering` 的排序逻辑之后,调用 `SubmitEditModeDrawForCamera(camera)`.
  - 仅在 EditMode + `CameraMode=ActiveCameraOnly` 下启用.
  - 渲染门禁(不包含 sort 的 per-frame guard):
    - Active=SceneView: 允许所有 SceneView camera 渲染(规避实例抖动/多窗口).
    - Active=Game/VR: 只允许 ActiveCamera 渲染(override MUST win).

#### 2) renderer 增加 “按指定 camera 渲染” 的新入口
- 落点: `Runtime/GsplatRendererImpl.cs`
- 新增:
  - `RenderForCamera(Camera camera, ...)`
  - 内部复用 `TryPrepareRender(...)` 做参数/资源校验、property block 填充、buffers 重新绑定、RenderParams 构建.
- 目的:
  - 让 sorter 在相机回调里能针对“当前正在渲染的 camera”提交 draw.
  - 避免再次走原 `Render()` 里的相机选择逻辑(避免重复枚举/重复提交到其它 camera).

#### 3) EditMode 下避免“双重提交 draw”
- 落点:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
- 做法:
  - 当 `!Application.isPlaying && SortDrivenBySrpCallback && CameraMode==ActiveCameraOnly` 时:
    - Update 仍负责 upload/decode/time 缓存等状态更新.
    - 但不再从 Update 调用 `m_renderer.Render(...)`.
    - draw 由 sorter 的相机回调统一提交,避免同一 render invocation 叠加 draw(亮度翻倍/性能翻倍).

### 诊断增强(让证据更“对症”)
- 落点: `Runtime/GsplatEditorDiagnostics.cs`
- 新增:
  - `sceneView.renderCounts` / `sceneView.drawCounts`(按 camera instanceId 计数).
  - `rs(render serial)`: 给每次 `[CAM_RENDER]` 一个递增序号,并在 `[DRAW]` 中输出关联序号.
- 新的自动 dump 触发条件:
  - 从 “SceneView rendered but no draw” 扩展为:
    - `renderCount > drawCount`(更贴合“同一帧多次渲染但 draw 只提交一次”的闪烁根因).

---

## 2026-02-24 11:20:00 +0800: continuous-learning 四文件摘要(决定是否提取 skill)
- 任务目标(task_plan.md):
  - 修复 Unity Editor(SRP)下的“SceneView UI 上滑动仍闪烁”,并把证据采集链路补齐到可解释.
- 关键决定(task_plan.md):
  - 采用“相机回调驱动 EditMode draw 提交”,不再继续赌 focusedWindow/mouseOverWindow 这类 UI 信号.
- 关键发现(notes.md):
  - Editor 下同一 `Time.frameCount` 内可能出现多次 `BeginCameraRendering`.
  - Update 单次提交 draw 不一定覆盖每次 render invocation,导致 `renderCount > drawCount` 的闪烁模式.
- 实际变更(WORKLOG.md):
  - `GsplatSorter` 在 beginCameraRendering 内补交 draw.
  - `GsplatRendererImpl` 新增 `RenderForCamera` 并抽 `TryPrepareRender`.
  - renderer 在 EditMode + SRP + ActiveCameraOnly 下不再从 Update 提交 draw,避免双重渲染.
  - `GsplatEditorDiagnostics` 增强为 renderCounts/drawCounts + render serial(rs).
- 错误与根因(ERRORFIX.md):
  - 根因是“渲染调用次数与 draw 提交次数不一致”,属于时序架构问题,不是单点 UI 信号 bug.
- 可复用点候选(1-3 条):
  1. Unity Editor(SRP)里不要假设 `BeginCameraRendering` 每帧只触发一次; 需要按 render invocation 思维设计门禁与提交点.
  2. 若 draw 是由 Update 提交,但相机在同帧多次渲染,很容易出现“部分 invocation 没 draw”的闪烁; 应把 draw 提交对齐到相机回调链路.
  3. 诊断上用 `renderCount/drawCount` 与 `render serial` 比“本帧是否提交过一次 draw”更对症.
- 是否需要固化到 docs/specs: 否(更像排障模式,不必写进 specs).
- 是否提取/更新 skill: 是(跨项目可复用的 Unity Editor/SRP 闪烁排查与修复模式).

---

## 2026-02-24 12:40:00 +0800: 新证据 - SceneView camera 可能 `enabled/isActiveAndEnabled` 为 false

### 现象(用户 dump)
- `[GsplatDiag] DETECTED: SceneView renderCount > drawCount`.
- ring-buffer 中出现 `RenderForCamera` 被跳过,原因指向 camera 处于 "null/disabled".

### 推断
- SceneView 的内部 camera 在某些 Editor 状态下可能:
  - `enabled=false`
  - `isActiveAndEnabled=false`
  但仍然会触发 SRP 的 `beginCameraRendering`.
- 因此渲染链路里任何依赖 `isActiveAndEnabled` 的门禁都会导致:
  - 该 render invocation 没有 draw,
  - 进而 `renderCount > drawCount`,
  - 体感就是“整体闪一下/整帧消失”.

### 对策
- 移除 SceneView camera 枚举处的 `isActiveAndEnabled` 过滤(只做 null/destroyed 防御).
- 诊断输出 `en/act`,让 dump 直接显示该 camera 的 enabled 状态,避免再猜.

### 验证
- 2026-02-24 13:02:53 +0800: 用户确认“不闪了”.

---

## 2026-02-24 13:10:00 +0800: GameView 不显示的根因与修复策略

### 现象
- EditMode 下切换/聚焦到 GameView,高斯基元不显示.

### 根因
- `ActiveCameraOnly` 为了稳态曾在 EditMode 固定选择 SceneView 作为 ActiveCamera.
- 结果:
  - Game camera 永远不是 ActiveCamera.
  - 排序门禁与 SRP 回调 draw 门禁都会把 Game camera 跳过.

### 修复策略
- EditMode 下使用更可靠的强信号:
  - 当 `focusedWindow` 是 GameView 时,ActiveCamera 选择 Game/VR 相机,用于 GameView 预览.
  - 其它窗口聚焦时仍默认 SceneView,避免 overlay/UIElements 的噪声信号导致闪烁回归.

---

## 2026-02-24 14:32:11 +0800: GameView 拖动 TimeNormalized 消失 + PlayMode 播放卡顿

### 现象(用户反馈)
- EditMode:
  - 在 SceneView 下拖动 `TimeNormalized`,性能很好.
  - 切到 GameView 后拖动 `TimeNormalized`,高斯基元会“全消失”.
- PlayMode:
  - 拖动 `TimeNormalized` 可以显示,但非常卡.
  - `AutoPlay` 也非常卡.

### 根因假设 1(正确性): ActiveCameraOnly(EditMode) 对 GameView 的选择不够"粘"
- `GsplatSorter.TryGetActiveCamera` 在 EditMode 里目前是:
  - 只有 `focusedWindow` 是 GameView 才选 Game/VR camera.
  - 否则默认选 SceneView camera.
- 用户拖动 Inspector 滑条时,焦点会落到 Inspector.
  - 这会把 ActiveCamera 从 Game/VR 切回 SceneView.
  - 于是对 Game camera 的排序与 draw 提交都被 gate,体感就是 GameView “全消失”.
- 代码侧已经有 `GetEditorFocusHintNonAlloc()` 的 viewport hint 机制(并且能在鼠标在 Inspector 时保持上一帧 hint),
  但当前 EditMode ActiveCamera 选择没有真正使用这个 hint(只用于 cache/debug).

### 根因假设 2(性能): PlayMode 全量 records 排序的浪费
- 对 `.splat4d(window)` 的 keyframe 数据,常见生成方式是:
  - 把每个时间段(segment)的 splats 依次追加到同一个 asset.
  - 每个 segment 内 `time/duration` 基本是常量,segment 之间不重叠.
- 现状:
  - `Gsplat.compute/CalcDistance` 会把时间窗外的 splat 写入极端 key(1e30)推到排序尾部.
  - 但排序仍然对“全量 records”执行 GPU radix sort,成本是 O(totalRecords).
  - 实际上同一时刻只有一个 segment 可见,真正有价值的排序规模应该是 O(recordsPerSegment).

### 计划落点(路线A)
1. ActiveCameraOnly(EditMode) 改用 viewport hint:
   - hint=GameView 时,即便当前在 Inspector 拖动滑条,仍保持 Game/VR camera 为 ActiveCamera.
2. 为排序与渲染引入 `baseIndex`:
   - Sort: CalcDistance 读取 buffers 时用 `baseIndex + localId` 偏移,而 payload 仍保持 localId.
   - Render: shader 侧用 `_SplatBaseIndex + localId` 得到 absolute splatId.
3. 在 `GsplatRenderer` 侧检测 non-overlap keyframe segments:
   - 满足条件时,每帧只对当前 segment 做 sort+draw.
   - 不满足条件时保持旧行为(全量排序+shader 硬裁剪).
