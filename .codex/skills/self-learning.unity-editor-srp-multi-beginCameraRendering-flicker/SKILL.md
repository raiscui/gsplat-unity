---
name: self-learning.unity-editor-srp-beginCameraRendering-flicker
description: |
  修复 Unity Editor(SRP) 下 SceneView/GameView 偶发“整体闪烁/消失”,且日志不明显的场景.
  典型特征: 同一 Time.frameCount 内出现多次 RenderPipelineManager.beginCameraRendering,
  但 draw 只在 ExecuteAlways.Update 中提交一次,导致部分 render invocation 没有 draw.
  解决思路: 把 draw 提交对齐到 beginCameraRendering(或等价的相机渲染回调)链路,并避免 EditMode 双重渲染.
author: Claude Code
version: 1.0.0
date: 2026-02-24
---

# Unity Editor(SRP) 同帧多次 BeginCameraRendering 导致“整体闪烁/消失”排障与修复

## 现象 / 触发条件
满足任意一条就值得用这个 skill:

- Unity Editor 非 Play 模式下,SceneView/GameView 里渲染物会“显示/不显示”来回闪.
- 闪烁更容易在 SceneView 的 UI(overlay/toolbar/tab)区域移动鼠标、滚轮缩放、拖拽视角时出现.
- Console 里没有明确 error/warning,看起来像“随机”.
- 你在 SRP(URP/HDRP)下工作,并且使用了 `RenderPipelineManager.beginCameraRendering` 或类似回调做 per-camera 逻辑.

## 关键认知: Editor 下 “1 帧”不等于 “1 次相机渲染”
在 Unity Editor(SRP)里,同一个 `Time.frameCount` 内可能发生多次相机渲染调用:

- 同一个 camera 多次 `beginCameraRendering`
- 多个内部阶段/overlay/repaint 触发的额外 render invocation

因此你会遇到这种组合拳:

1. 你的渲染提交点在 `ExecuteAlways.Update()`(每次 Update 提交一次 draw).
2. 但同一 `Time.frameCount` 内 camera 实际渲染了多次.
3. 最终会出现: `render invocation 次数 > draw 提交次数`.
4. 某些 render invocation 没有 draw,最终显示出来的那一次恰好没 draw 时,体感就是“整体闪烁/消失”.

## Phase 1: 先把黑盒变成证据
在修复前,优先采集这 2 类证据:

### 1) 相机渲染回调计数
记录每个 camera 的:

- `renderCount`(本帧触发了多少次 beginCameraRendering)
- `drawCount`(本帧对该 camera 提交了多少次 draw)

当你看到 `renderCount > drawCount` 时,基本就可以锁定为“提交点时序不一致”的问题.

### 2) render serial(把 draw 与某次渲染调用关联起来)
做一个全局递增的序号:

- 每次 `beginCameraRendering` 时 `rs++`,并把 `[CAM_RENDER] rs=...` 打出来.
- 每次提交 draw 时也打印 `rs`.
  - 如果 draw 是在相机回调链路里提交的,你应该能看到 `[DRAW] rs=...` 与最近的 `[CAM_RENDER] rs=...` 对齐.
  - 如果 draw 是在 Update 里提交的,通常只能标成 `rs=-1`(或 unknown).

## Phase 2: 根因判断(典型)
当证据满足:

- 同一帧同一 camera 出现多次 `[CAM_RENDER]`
- 但只有一次 `[DRAW]`(或 `renderCount > drawCount`)

则根因通常是:

- draw 只在 Update 中提交,无法覆盖 Editor 的每次 render invocation.

这不是 UI 信号(focusedWindow/mouseOverWindow)能彻底解决的问题.
它属于“渲染提交点与相机渲染回调链路不一致”的架构问题.

## Phase 3: 修复方案(正确性优先)

### 方案A(推荐): 把 draw 提交对齐到 beginCameraRendering 链路
做法(概念):

1. 在 `beginCameraRendering(context, camera)` 里完成 per-camera 的准备工作(例如排序/compute).
2. 紧接着在同一个回调链路里,针对当前 camera 提交 draw:
   - 使用 `Graphics.RenderMeshPrimitives` 时,显式设置 `RenderParams.camera = camera`.
   - 或使用 `CommandBuffer`/SRP pass 做更严格的注入点控制.
3. 这样每一次 render invocation 都会在对应的 camera 渲染中看到 draw.

### 方案B(不推荐): 继续赌 Update + Repaint 的时序
这类方案常见表现:

- 让 Update 更频繁执行.
- 或在 UI 交互时强行 `QueuePlayerLoopUpdate/RepaintAll`.

它通常只能“缓解”,无法保证覆盖 Editor 的每一次内部 render invocation,后续很容易复现.

## 非常重要: 避免 EditMode 双重渲染(draw twice)
如果你把 draw 提交搬到了 `beginCameraRendering`,同时又保留了 Update 里的 draw 提交,会产生:

- 同一 render invocation 叠加两次 draw(亮度翻倍/性能翻倍)

稳妥做法之一:

- EditMode + SRP + 你需要修复的相机模式下:
  - Update 仍负责数据上传/解码/参数缓存.
  - 但 Update 不再提交 draw.
  - draw 只由相机回调提交一次.

## 常见坑: 把“排序门禁”误用于“渲染门禁”
很多系统会有一个 gather 函数,里面包含 “每帧只排序一次” 的 guard(例如 `lastSortedFrame == Time.frameCount`).

如果你直接用它的 bool 返回值来决定“要不要渲染”,就会出现:

- 该帧第二次/第三次 render invocation 被判断为 “不需要 sort -> return false”
- 结果渲染也被一起跳过
- 闪烁依旧

正确做法:

- sort guard 只影响 “是否需要重新排序”.
- render 仍应在每次 render invocation 提交(至少在 Editor 的闪烁场景里).

## 验证标准
修复后,你应该能在日志里看到:

- `renderCount == drawCount`(至少对你关注的 SceneView/GameView camera)
- 每条 `[CAM_RENDER] rs=...` 都能在附近找到对应的 `[DRAW] rs=...`
- 主观体验: SceneView UI 上滑动/滚轮/拖拽视角时不再整体闪烁

## 备注: -batchmode -nographics
在 `-nographics` 下:

- `SystemInfo.graphicsDeviceType` 可能是 `Null`.
- 不要在这种环境里强行提交 draw(有可能产生 error log 并导致单测失败).
- 可以用 “排序器/渲染器 Valid=false 时直接 no-op” 的方式做稳态兼容.
