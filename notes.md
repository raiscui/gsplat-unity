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

---

## 2026-02-24 15:32:37 +0800: 四文件摘要(continuous-learning)

## 四文件摘要(用于决定是否提取/更新 skill)
- 任务目标(task_plan.md):
  - 修复 Editor(EditMode) 下 ActiveCameraOnly 的“整体闪烁/消失”.
  - 修复 EditMode 在 GameView 下拖动 `TimeNormalized` 时“全消失”.
  - 优化 PlayMode `TimeNormalized` 拖动/`AutoPlay` 的卡顿(尤其是 keyframe `.splat4d(window)`).
- 关键决定(task_plan.md):
  - 采用“相机回调驱动 draw 提交”,不再赌 Update 单次 draw 能覆盖 Editor 的多次 render invocation.
  - ActiveCameraOnly(EditMode) 采用 viewport hint(最近交互视窗)做“粘性”决策,避免 Inspector 抢焦点导致 ActiveCamera 切走.
  - keyframe `.splat4d(window)` 走“子范围 sort+draw”优化: baseIndex + segment 检测,只处理当前 segment.
- 关键发现(notes.md):
  - Unity Editor(SRP) 下同一 `Time.frameCount` 内可能出现多次 `beginCameraRendering`,Update 只提交一次 draw 会导致 `renderCount > drawCount` 的闪烁模式.
  - SceneView 内部 camera 可能 `enabled=false` / `isActiveAndEnabled=false` 但仍参与 SRP 回调,因此不能用 `isActiveAndEnabled` 做硬门禁.
  - keyframe `.splat4d(window)` 常见为多 segment records 叠加,同一时刻只有 1 个 segment 可见,全量 radix sort 是纯浪费.
- 实际变更(WORKLOG.md):
  - 新增 `GsplatActiveCameraOverride` 组件,用于显式指定 ActiveCamera(跨窗口稳态).
  - 新增 `GsplatEditorDiagnostics` 捕获 Metal 跳绘制 warning 并自动 dump ring-buffer 证据.
  - sorter/render 支持 `SplatBaseIndex` 子范围,compute+shader+渲染侧完整打通.
  - `GsplatRenderer` 检测 non-overlap segments,window model 下按 segment 做 sort+draw.
  - `OnValidate` 使用 `InternalEditorUtility.RepaintAllViews()` 同刷 SceneView/GameView.
- 错误与根因(ERRORFIX.md):
  - 闪烁本质是“相机渲染调用次数 > draw 提交次数”(时序架构问题),不是简单 UI 信号 bug.
  - GameView 全消失本质是 EditMode 相机选择依赖 focusedWindow,Inspector 抢焦点后 ActiveCamera 切走导致 gate.
  - PlayMode 卡顿本质是 keyframe 多 segment 数据仍全量排序,成本随 segment 数线性膨胀.
- 可复用点候选(1-3 条):
  1. Editor(SRP) 里不要假设 1 帧只渲染一次相机; draw 要对齐到 camera callback 链路,并避免用 sort guard 去 gate draw.
  2. ActiveCameraOnly(EditMode) 不要只用 focusedWindow; 用“最近交互视窗”的 sticky hint 才能稳态支持 Inspector 拖动/编辑.
  3. “多段 records 叠加但同刻只显一段”的数据形态,应尽量把 O(totalRecords) 降到 O(recordsPerSegment)(子范围 sort+draw).
- 是否需要固化到 docs/specs: 是.

  - `AGENTS.md`: 补充 Metal 的 "requires a ComputeBuffer ... Skipping draw calls" 识别与处理要点,以及 Editor(SRP) 多次 beginCameraRendering 的注意事项.
  - `Documentation~/Implementation Details.md`: 补充 keyframe `.splat4d(window)` segment 子范围 sort/draw 的设计与条件.
- 是否提取/更新 skill: 是.
  - 更新现有 skill: `self-learning.unity-editor-srp-beginCameraRendering-flicker`(补充 SceneView camera enabled 状态坑位).
  - 新增 skill 候选:
    - `self-learning.unity-metal-skip-draw-missing-buffer`: Metal 因 StructuredBuffer 未绑定导致跳绘制的排障与修复.

---

## 2026-02-24 21:31:02 +0800: 显隐燃烧环动画(burn reveal)落地笔记

### 状态机设计要点
- `Visible/Showing/Hiding/Hidden` 四态即可覆盖需求.
- 关键门禁: `Hidden => Valid=false`.
  - 这是“真正停排序与渲染开销”的根本保证(不是只把 alpha 乘 0).
- Showing/Hiding 期间保持 `Valid=true`,保证 sorter/draw 能跑,动画才可见.

### uniforms 推送要点
- Update 渲染入口与 EditMode SRP 相机回调渲染入口,都必须在 draw 前推一次本帧 uniforms.
  - 否则会出现 Update 路径与 CameraCallback 路径的动画进度不一致,体感像“偶尔跳一下/卡一下”.

### Unity batchmode 测试踩坑
- 运行 `-runTests` 时不要附带 `-quit`.
  - 观察到 `-quit` 会导致 Unity 在完成导入/编译后直接退出,测试不会执行,也不会生成 testResults.
- EditMode tests 中,ExecuteAlways.Update 的触发与 `Time.frameCount` 行为不一定稳定.
  - 因此回归用例里用反射直接调用 `AdvanceVisibilityStateIfNeeded` 推进状态机,
    让测试不依赖 Editor PlayerLoop 细节.

---

## 2026-02-25 00:06:30 +0800: 显隐燃烧环动画(burn reveal)调优: 更慢的 size easing + 更明显的 warp 粒子

### 用户反馈
- 位置扭曲(pos warp)不够明显,看起来更像 alpha 在抖,而不是“扭曲空间”的粒子位移.
- show/hide 的 size 变化希望更慢更容易看出来.

### 调整要点
- 新增独立参数 `WarpStrength`(C# 字段 + shader uniform `_VisibilityWarpStrength`):
  - 目的: 不必通过拉高 `NoiseStrength` 才能获得明显位移.
- warp 观感增强(仍保持 passed/ring 判定基于 basePos 的稳定性):
  - per-splat phase offset: 通过 splatId 生成相位偏移,避免整片区域同步抖动.
  - 各轴不同的时间推进: 避免噪波场只沿对角线平移导致运动不够“活”.
  - globalWarp 权重: show 早期/ hide 后期更不稳定,让位移在视觉上更明显.
- size easing 调整:
  - 用 `pow + smoothstep` 让 grow 更慢,shrink 更快更明显(相对 alphaMask 更容易被肉眼感知).

### 顺手修复: d3d11 shader 编译错误
- 问题:
  - `signed/unsigned mismatch`(uint vs int 比较).
  - `out` 参数未在所有路径初始化(InitCenter).
- 修复:
  - 显式 cast 消除 signed/unsigned 歧义.
  - 在 `InitCenter` 入口初始化 `center` 的所有字段,再走早退分支.

### 回归(证据)
- Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
- 结果文件:
  - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_warp_tuning_2026-02-25_000517.xml`
- 汇总: total=28, passed=26, failed=0, skipped=2
- shadercompiler logs: 本次不再出现 `error:` 记录.

---

## 2026-02-25 00:31:30 +0800: 显隐燃烧环动画调优: hide shrink 更强 + 噪波更像烟雾

### 用户反馈
- hide 期间 splat 尺寸仍然很大,缺少“从正常逐渐变小”的过程.
- noise 仍然很混乱,不像烟雾.

### 处理思路
- hide size:
  - 仅靠 passed 的局部 shrink 会让“外圈未扫过区域”一直保持大尺寸.
  - 因此对 hide 增加:
    - 更强的 shrink 曲线(指数更大).
    - global progress 的整体 shrink(让未扫到区域也会逐渐变小).
- smoke noise:
  - 之前的 hash 白噪声更像随机抖动,缺少烟雾的空间连续性.
  - 升级为 3D value noise(8-corner hash + trilinear),并加入轻量 domain warp,让噪波形态更像烟雾的扭曲与波动.

### 回归(证据)
- Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
- 结果文件:
  - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_smoke_tuning_2026-02-25_003000.xml`
- 汇总: total=28, passed=26, failed=0, skipped=2
- shadercompiler logs: 未出现 `error:`.

---

## 2026-02-25 01:22:20 +0800: smoke noise 进一步稳态化(去掉 per-splat 大相位偏移)

### 动机
- 用户希望噪波更像烟雾(空间连续的扭曲与波动),而不是每个 splat 独立抖动导致的混乱.

### 调整
- `Runtime/Shaders/Gsplat.shader`:
  - 移除 per-splat 大幅 `idPhase` 偏移(它会破坏空间连续性).
  - 改为:
    - 先用 value noise 得到 base01/baseSigned.
    - 用 base 生成 domain warp(降低强度到 0.65).
    - 再用 warp 后的噪声(warp01a/warpSignedA)作为 jitter/ash/warpVec 的统一噪声源.
    - jitter 额外乘 0.75,避免边界抖动过碎.

### 回归(证据)
- Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
- 结果文件:
  - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_smoke_tuning_2026-02-25_012102.xml`
- 汇总: total=28, passed=26, failed=0, skipped=2

---

## 2026-02-25 01:26:10 +0800: show/hide 宽度拆分 + hide 前沿方位校正

### 用户反馈
- show 的 ring/trail 宽度希望与 hide 分开设置.
- show 在 ring 阶段 size grow 过慢,看到的都是小点点; 但仍希望从极小开始.
- hide 的 ring/trail 方位希望“反过来”: 体感现在像 trail 在外,希望 trail 在内.

### 实现要点
- 参数拆分(保持兼容):
  - 将旧的 `RingWidthNormalized/TrailWidthNormalized` 迁移为 show 专用(`ShowRingWidthNormalized/ShowTrailWidthNormalized`).
  - 新增 hide 专用(`HideRingWidthNormalized/HideTrailWidthNormalized`).
  - 用 `FormerlySerializedAs("RingWidthNormalized")` 保证旧 Prefab/Scene 自动迁移.
- show size:
  - 将 show 的 grow 曲线从“慢”改为“更快”(指数从 2.0 改为 0.5),避免 ring 阶段全是小点点.
  - 仍保持 passed=0 时从极小开始.
- hide 方位:
  - hide 的 ring 改为主要出现在外侧(edgeDist>=0)的“前沿”,避免 ring 在边界两侧发光与内侧渐隐叠加导致方位错觉.
  - trail(渐隐)仍基于 passed,自然落在内侧(已燃烧区域).

### 回归(证据)
- Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
- 结果文件:
  - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_smoke_tuning_2026-02-25_012102.xml`
- 汇总: total=28, passed=26, failed=0, skipped=2

补充(证据更新):
- 上述“show/hide 宽度拆分 + hide 前沿方位校正”的回归结果文件是:
  - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_widthsplit_2026-02-25_105753.xml`
- 汇总: total=28, passed=26, failed=0, skipped=2

---

## 2026-02-25 15:55:00 +0800: burn reveal 显隐动画在 EditMode 下“鼠标不动不播放”的根因与修复

### 现象
- 在 Unity Editor 非 Play 模式下触发 `PlayShow()` / `PlayHide()`:
  - 如果鼠标不动,SceneView/GameView 不 repaint.
  - 体感像动画不播放,晃动鼠标才会“跳一下”更新.

### 根因(本质)
- EditMode 下的 SceneView/GameView 往往是“事件驱动 repaint”:
  - 没有鼠标/键盘事件时,视口不会持续刷新.
- 我们的 burn reveal 是 shader/uniform 驱动的时间动画:
  - `AdvanceVisibilityStateIfNeeded()` 需要被调用推进 progress.
  - 更关键的是,即使 progress 推进了,也必须有视口 repaint 才会提交 draw,肉眼才能看到连续帧.

### 修复策略(改良胜过新增)
- 只在 Showing/Hiding 期间(动画进行中)主动请求 Editor 刷新:
  - `EditorApplication.QueuePlayerLoopUpdate()`
  - `InternalEditorUtility.RepaintAllViews()`
- 加轻量节流(60fps 上限),避免空闲耗电与刷屏.
- `Application.isBatchMode` 下跳过,避免命令行 tests/CI 的无意义调用与潜在不稳定因素.
- 动画刚结束时额外补 1 次强制刷新,避免停在“最后一帧之前”的错觉.

### 验证(证据)
- Unity 6000.3.8f1 EditMode tests:
  - total=28, passed=26, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_editor_repaint_2026-02-25.xml`

---

## 2026-02-25 16:35:00 +0800: 仅靠“请求 Repaint”仍不足时,用 EditorApplication.update ticker 彻底驱动 show/hide

### 用户反馈
- 上述“只在状态机里请求 Repaint”的优化,用户实测仍会出现:
  - 鼠标不动,画面还是不动.

### 推断
- 可能存在某些 Editor 状态下:
  - `ExecuteAlways.Update`/相机回调链路不会持续触发.
  - 导致我们的 repaint 请求只发生一次,之后缺少持续 tick,又回到“鸡生蛋”循环.

### 最终做法(更稳态)
- 在 `GsplatRenderer`/`GsplatSequenceRenderer` 内增加 EditorApplication.update 驱动的 ticker:
  - 只在 Showing/Hiding 期间注册.
  - 每 tick 主动调用 `AdvanceVisibilityStateIfNeeded()`,并通过内部的 `RequestEditorRepaintForVisibilityAnimation()` 持续触发:
    - `QueuePlayerLoopUpdate()`
    - `RepaintAllViews()`
  - 动画结束后自动注销.
- 诊断增强(可控):
  - `Runtime/GsplatEditorDiagnostics.cs` 新增 `[VIS_STATE]/[VIS_REPAINT]` 事件写入 ring buffer.
  - 当 `GsplatSettings.EnableEditorDiagnostics=true` 时,可通过 `Tools/Gsplat/Dump Editor Diagnostics` dump 出完整时序证据.

### 回归(证据)
- Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_editor_ticker_2026-02-25.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2

---

## 2026-02-25 17:10:00 +0800: 燃烧环扩散速度曲线改为 easeInOutQuart

### 用户需求
- show/hide 的燃烧环扩散希望不是匀速.
- 期望曲线: `easeInOutQuart`.

### 落地方式
- `Runtime/Shaders/Gsplat.shader`:
  - 增加 `EaseInOutQuart(float t)` 函数(避免 pow,用乘法实现).
  - 用 `progressExpand = EaseInOutQuart(progress)` 替代线性 progress,用于:
    - `radius = progressExpand * (maxRadius + trailWidth)`
    - hide glow 衰减 lerp
    - hide globalShrink
    - globalWarp

### 回归(证据)
- Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_easeInOutQuart_2026-02-25.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2

---

## 2026-02-25 17:35:00 +0800: 燃烧环扩散速度曲线切换为 easeOutCirc

### 用户需求
- 试一下 `easeOutCirc`.

### 调整点
- `Runtime/Shaders/Gsplat.shader`:
  - 将扩散半径的推进曲线从 `easeInOutQuart` 切换为 `easeOutCirc`.
  - `easeOutCirc(t)=sqrt(1-(t-1)^2)`,并对 sqrt 输入做 `max(0,x)` 防御浮点误差.
  - 与扩散节奏强相关的全局效果继续使用同一套 `progressExpand`,避免节奏脱钩.

### 回归(证据)
- Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_easeOutCirc_2026-02-25.xml`
  - 汇总: total=28, passed=26, failed=0, skipped=2

---

## 2026-02-25 17:25:00 +0800: 燃烧环扩散速度曲线改为 easeInOutQuad

### 用户需求
- 将 show/hide 的燃烧环扩散速度曲线改为 `easeInOutQuad`.

### 落地方式
- `Runtime/Shaders/Gsplat.shader`:
  - 使用 `EaseInOutQuad(progress)` 生成 `progressExpand`,用于:
    - `radius = progressExpand * (maxRadius + trailWidth)`
    - hide glow 衰减、globalWarp、globalShrink 与扩散节奏保持一致(避免脱钩).
- `openspec/changes/burn-reveal-visibility/`:
  - 修正 spec/tasks 中仍残留的 `easeOutCirc` 文案,与当前实现对齐.

### 回归(证据)
- Unity 6000.3.8f1 EditMode tests:
  - total=28, passed=26, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_easeInOutQuad_rerun_2026-02-25.xml`

### 额外发现(本机/本版本)
- Unity `-runTests` CLI 在本机 Unity 6000.3.8f1 下实测:
  - 当 `-quit` 放在参数前部时,可能出现“进程正常退出,但没有真正跑测试,也不生成 results.xml”的情况.
  - 更稳态的做法:
    - 不传 `-quit`(tests 完成后仍会自动退出),或
    - 把 `-quit` 放到参数末尾.

---

## 2026-02-25 17:55:00 +0800: continuous-learning 四文件摘要(归档 WORKLOG 超 1000 行)

## 四文件摘要(用于决定是否提取 skill)
- 任务目标(task_plan.md):
  - 将 burn reveal(show/hide)的燃烧环扩散速度曲线切换为 `easeInOutQuad`,并保证 OpenSpec artifacts 与实现一致.
  - 在 Unity 命令行下跑 EditMode tests 作为回归证据.
- 关键决定(task_plan.md):
  - 以 shader 内的 `progressExpand` 作为单一节奏源,扩散半径与全局效果共用同一 easing.
  - OpenSpec 的 spec/tasks 若出现漂移,优先同步修正,避免“实现对了但规格旧了”.
- 关键发现(notes.md):
  - Unity `-runTests` 在本机/本版本下,`-quit` 放在参数前部可能导致不跑测试且不生成 `-testResults`.
  - 更稳态的跑法是移除 `-quit` 或把 `-quit` 放到参数末尾.
- 实际变更(WORKLOG.md):
  - 归档旧 `WORKLOG.md` 到 `archive/WORKLOG_2026-02-25_174051.md`(因超过 1000 行),并新建当前 `WORKLOG.md`.
  - 同步修正 OpenSpec change `burn-reveal-visibility` 的 spec/tasks,与实现对齐.
- 错误与根因(ERRORFIX.md,本次无新增):
  - 本次属于调参与文档同步,无新增 bugfix 条目.
- 可复用点候选(1-3 条):
  1. Unity CLI 跑 tests 时,若出现“不生成 results.xml”,优先怀疑 `-quit` 参数位置/使用方式.
  2. OpenSpec artifacts 需要持续对齐实现,否则会出现“规格/实现漂移”的维护风险.
- 是否需要固化到 docs/specs: 是.
  - 已把 Unity CLI tests 的 `-quit` 小坑补充到 `AGENTS.md` 的 Testing Guidelines.
- 是否提取/更新 skill: 否.
  - 理由: 该踩坑点已在历史 notes 中出现过,本次仅做再次确认与固化到项目文档.

---

## 2026-02-25 18:30:00 +0800: 显隐动画 warp 噪声升级(ValueSmoke/CurlSmoke)与可切换下拉

### 用户目标
- warp 的位移噪声更平滑,更像烟雾/流体的连续流动.
- 需要一个下拉选项,能在“当前效果”和“新效果”之间快速切换对照.

### 设计要点
- 默认值必须保持现有观感,避免升级后旧项目被意外改动.
- CurlSmoke 允许更贵一点,因为它只在 show/hide 动画期间启用(平时 `_VisibilityMode=0` 直接走旧路径).

### 实现策略(核心)
1. 增加 `VisibilityNoiseMode` 枚举字段(Inspector 下拉).
   - `ValueSmoke`(默认): 平滑 value noise + domain warp,更像烟雾波动.
   - `CurlSmoke`: curl-like 向量场,更像旋涡/流动(主要用于 position warp).
   - `HashLegacy`: 旧版对照,更碎更抖(调试/性能基线).
2. shader 新增 `_VisibilityNoiseMode` uniform,每帧由 `GsplatRendererImpl.SetVisibilityUniforms(...)` 写入 MPB.
3. Curl-like 计算方式:
   - 在 HLSL 中实现 value noise 的梯度计算(仍然只做 8 个 corner hash,同时求 trilinear+fade 的偏导).
   - 用 3 份独立 noise 作为 vector potential A(p)=(Ax,Ay,Az),计算 `curl(A)=∇×A` 得到更连续的旋涡向量场.
   - 在 vertex shader 中把 curlVec 投影到切向(去掉 radial 分量),再叠加少量 radial 分量保持“空间被拉扯”的感觉.

### 风险与取舍
- CurlSmoke 计算量更大(需要额外的 noise+gradient 采样).
  - 通过“只在 show/hide 动画期间启用 + 默认 ValueSmoke”来控制整体成本与回归风险.

---

## 2026-02-25 19:20:00 +0800: glow/size 调优(hide 更早变小 + show 增加 StartBoost + hide glow 尾巴朝内)

### 用户反馈要点
1. hide: glow 阶段 splat 仍偏大,希望进入 glow 时已经更小.
2. show: 也需要 `GlowStartBoost`,让“点燃瞬间”更亮.
3. hide: 当前 glow 的“逐渐变弱”体感朝外,导致外围突兀;期望朝内衰减,更符合“中心先烧掉”的语义.

### 对应实现策略
- show:
  - 增加 `ShowGlowStartBoost`,并在 shader 中用 eased progress 做轻量衰减,避免全程过曝.
- hide:
  - glow 改为两段:
    - 前沿 ring 使用 `HideGlowStartBoost` 作为 boost,且不随扩散向外整体变暗(减少外围突兀).
    - 内侧增加 afterglow tail,并让其朝内逐渐衰减(中心方向),衰减方向更符合语义.
  - size shrink 提前:
    - 为 size 单独引入 `passedForSize`(向外提前半个 ringWidth),
      让 ring(glow)出现时 splat 已明显变小.

---

## 2026-02-25 20:15:00 +0800: hide size 节奏调优(先快后慢,easeOutCirc-like)

### 用户反馈要点
- hide 燃烧阶段粒子大小仍偏大,希望迅速先变小.
- 现状体感: size 变得太小导致“看起来消失太快”.

### 调整思路
- 把 hide 的 shrink 从“强依赖 passed 并压到接近 0”改为两点:
  1) 在燃烧前沿附近快速 shrink 到一个非 0 的 minScaleHide(较小但仍可见).
  2) 后续主要依赖 alpha trail 慢慢消失,避免 size 过早接近 0.
- shrink 曲线使用 `easeOutCirc` 风格,实现“先快后慢”的节奏.

### 回归(证据)
- Unity 6000.3.8f1 EditMode tests:
  - total=28, passed=26, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_hide_size_easeOutCirc_2026-02-25.xml`

---

## 2026-02-25 20:10:00 +0800: show/hide 的 ring/tail 语义统一(前沿在外侧,内侧更亮)

### 现象(用户反馈)
- 怀疑 show 的 `ShowTrailWidthNormalized` 体感像跑到外侧,导致“内部不够亮”.
- 期望明确语义:
  - 前沿 ring 永远更亮(Boost),并且先到.
  - 内侧 afterglow/tail 朝内(中心方向)衰减.

### 本质(根因判断)
- show 原先 ring 在边界两侧都发光时,很容易在视觉上把“前沿”和“内侧余辉/拖尾”混在一起,
  产生“trail 跑到外面”的错觉.
- 同时本 shader 使用 premul alpha 输出,如果内侧余辉区域 alpha 很低,即使加了 glow 也会被 alpha 乘没,
  体感就会变成“外侧亮,内部发暗”.

### 处理(落地策略)
- ring 统一为“只在外侧(edgeDist>=0)出现”的燃烧前沿,show/hide 共用这条语义.
- afterglow/tail 统一为“只在内侧(edgeDist<=0),并朝内衰减”,且被 (1-ring) 抑制以保证前沿永远更亮.
- show: 为了避免 premul alpha 把内侧 afterglow 吃掉,允许 tail 提供一个受限的 alpha 下限,确保内部余辉肉眼可见.

### 回归(证据)
- Unity 6000.3.8f1 EditMode tests:
  - total=28, passed=26, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_show_trail_glow_semantics_2026-02-25_200342.xml`

### 小坑记录(避免重复踩)
- Unity 命令行跑 tests 时,不要额外加 `-quit`.
  - 观察到加了 `-quit` 会在 TestRunner 启动前提前退出,导致不生成 `-testResults` XML.

---

## 2026-02-25 20:20:00 +0800: hide 末尾 lingering 修复(禁止 fade/shrink 被正向边界噪声拖住)

### 现象(用户反馈)
- hide 最最后会残留一些高斯基元很久才消失.

### 根因(本质)
- hide 的 passed/visible 如果直接用 `edgeDistNoisy`:
  - 当噪声为正时,会把边界往外推.
  - 局部 passed 会长期达不到 1.
  - 于是末尾会出现少量 splats lingering(半透明残留).

### 处理(落地策略)
- ring/glow: 仍使用完整的 `edgeDistNoisy`,保留燃烧边界抖动质感.
- fade/shrink: 改用 `edgeDistForFade`:
  - 仅允许噪声往内咬(`min(noiseSigned,0)`),不允许往外推.
  - 这样可以保证最终一定能烧尽,不会在末尾被拖住.

### 回归(证据)
- Unity 6000.3.8f1 EditMode tests:
  - total=28, passed=26, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_hide_lingering_fix_2026-02-25_201817.xml`

---

## 2026-02-25 20:35:00 +0800: show ring glow 星火闪烁(curl noise),并提供强度可调

### 需求(用户期望)
- show 的环状 glow 不要均匀,要像火星/星星闪闪.
- 希望使用 curl-like noise,并且强度可调.

### 设计要点
- 只让 ring(前沿)闪烁,不要把 tail 也变成噪点墙.
- 闪烁由两部分组成:
  - 稀疏亮点(sparkMask): 用 curl noise 的幅度经过幂次增强得到.
  - 时间 twinkle: 用随时间变化的噪声相位(复用已有 noise 采样)让亮点闪烁.

### 落地实现
- C#:
  - `GsplatRenderer`/`GsplatSequenceRenderer` 新增 `ShowGlowSparkleStrength`(0=关闭).
  - `GsplatRendererImpl` 新增 `_VisibilityShowGlowSparkleStrength` uniform 下发,并做 NaN/Inf/范围 clamp.
- Shader:
  - show 分支对 ringGlow 做乘性调制:
    - ringGlow *= 1 + Strength * Sparkle * Scale
  - Sparkle 来自 curl noise + twinkle 相位,且只有 ring>0 时才计算(避免无意义开销).

### 回归(证据)
- Unity 6000.3.8f1 EditMode tests:
  - total=28, passed=26, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_burn_reveal_visibility_show_sparkle_2026-02-25_202927.xml`

---

## 2026-02-25 20:45:00 +0800: 默认参数微调(show ring 更厚,trail 更短)

### 需求(用户调参)
- `ShowRingWidthNormalized` 大 10%.
- `ShowTrailWidthNormalized` 乘以 40%(缩短 trail).

### 落地
- 默认值调整(仅影响新加组件/Reset,不会自动迁移已有 Prefab/场景):
  - `ShowRingWidthNormalized`: `0.06 -> 0.066`
  - `ShowTrailWidthNormalized`: `0.12 -> 0.048`

### 预期观感变化
- ring 更厚: 前沿存在感更强.
- trail 更短: 前沿扫过后更快稳定为完全可见,内部更快变亮,减少“半透明带太宽”的混浊感.
