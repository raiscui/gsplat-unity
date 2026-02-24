## Context

当前 Gsplat 的渲染链路里,排序(sort)属于“按相机触发”的计算:

- BiRP: `Camera.onPreCull` 回调里触发 `GsplatSorter.DispatchSort`.
- SRP(URP/HDRP): `RenderPipelineManager.beginCameraRendering` 回调里触发 `GsplatSorter.DispatchSort`.

这条 SRP 回调路径的优点是:
- SceneView 的隐藏相机也会进入回调,因此 SceneView 强旋转、转到背后等场景能稳定触发排序,显示正确.

但它也引入了一个现实的性能问题:
- 场景里每多一个相机(反射/探针/多视口/辅助 camera),同一帧就可能多触发一次 GPU sort.
- 在 >1M 且 <10M splats 规模下,GPU sort 的成本足够大,多相机重复排序会导致性能线性恶化.

本 change 的核心思路是:
- 不去“更快地排序”(算法不变),而是把“需要排序+渲染的相机数量”收敛为 1.
- 用一个明确的契约告诉用户:
  - 你要兼容多相机(例如反射/探针也要看到)就用 `AllCameras`.
  - 你要性能优先就用 `ActiveCameraOnly`,只保证一个视角正确.

## Goals / Non-Goals

**Goals:**
- 提供一个相机模式 `ActiveCameraOnly`,把每帧 sort 次数压到 1 次(只对 ActiveCamera).
- 在 `ActiveCameraOnly` 下:
  - Play 模式/Player: 渲染只对 ActiveCamera 提交,避免“某相机渲染了但没排序”的错误组合.
  - Editor 非 Play: SceneView 必须稳定可见(避免 UI 交互导致整体“显示/不显示”闪烁),必要时允许 SceneView 相机驱动排序/渲染而不强依赖某个 Camera 实例相等.
- ActiveCamera 的选择规则清晰、可预测,并覆盖:
  - Editor 非 Play: 默认 SceneView(不做自动切换),GameView 需显式 override 或切到 AllCameras.
  - Play/Player build: 始终选择 Game/VR 相机,优先 `Camera.main`,支持显式 override.
- 不破坏兼容性:
  - 用户可以切回 `AllCameras`,恢复“所有相机都能看到 Gsplat”的旧行为.
- 代码实现不引入明显 GC,并避免在每个相机回调里重复做昂贵枚举.

**Non-Goals:**
- 不改变 GPU 排序算法(仍使用现有 radix sort 管线).
- 不实现“可见子集排序/分块 LOD/近似透明”等更激进方案.
- 不保证 `ActiveCameraOnly` 下反射探针/辅助相机也能正确看到 Gsplat(这是 `AllCameras` 的责任).

## Decisions

### 1) 相机模式以 settings 驱动,而不是“自动猜测”

**选择:**
- 在 `GsplatSettings` 增加 `CameraMode`(AllCameras/ActiveCameraOnly),并在 Project Settings 提供 UI.
- 默认值设为 `ActiveCameraOnly`(性能优先).

**理由:**
- 这是一个用户可感知的行为变化,必须显式可配置,不能靠隐式规则让用户“猜不透”.
- 当项目确实需要多相机渲染时,切回 `AllCameras` 是最清晰的恢复路径.

**备选:**
- 自动检测“相机数量 > 1 就启用单相机模式”.
  - 缺点: 行为不稳定,且会导致调试阶段“突然看不到了”的困惑.

### 2) ActiveCamera 的解析逻辑集中放在 `GsplatSorter`

**选择:**
- 把 ActiveCamera 的解析与缓存放在 `GsplatSorter` 内部,并提供一个小而清晰的接口(例如 `TryGetActiveCamera(out cam)`).
- sorter 与 renderer 都通过同一份解析结果做门禁,保证 sort/render 一致.

**理由:**
- sort 与 render 必须对同一个相机成立,否则会出现排序错误或“渲染了但没排序”.
- `GsplatSorter` 目前已经承担“按相机收集 active gsplats”的职责,是最自然的放置点.

**备选:**
- 在每个 renderer 各自解析 ActiveCamera.
  - 缺点: 容易出现实现漂移,以及同帧不同对象选择了不同相机的隐性 bug.

### 3) 以“每帧缓存”避免每个相机回调重复枚举

**选择:**
- ActiveCamera 的解析结果按 `Time.frameCount` 缓存:
  - 同一帧内,无论 `beginCameraRendering` 被调用多少次,都只解析一次.

**理由:**
- 多相机场景下,回调次数本身就多,必须避免把“相机枚举”也放大成 N 倍成本.

**备选:**
- 每次回调都遍历相机.
  - 缺点: 在 Editor 下还可能引入额外 GC 与不稳定性能抖动.

### 4) Editor 非 Play 的焦点切换策略

**选择:**
- Editor 非 Play 模式下,`ActiveCameraOnly` 默认始终选择 SceneView(若存在)作为 ActiveCamera.
- 系统不再基于 `focusedWindow` / `mouseOverWindow` 等 Editor UI 信号在 SceneView/GameView 之间自动切换 ActiveCamera.
- 若你需要在 EditMode 以 GameView 作为主视角:
  - 方案A: 切换 `CameraMode=AllCameras`(兼容模式,但会增加多相机排序成本).
  - 方案B: 使用显式 override(`GsplatActiveCameraOverride`)把某个 Game/VR 相机指定为 ActiveCamera(性能模式且确定).

**理由:**
- 我们在前几轮实现中已经尝试过 “视口 hint + 缓存 + SceneView 锁定” 等方案.
- 但用户仍可在 SceneView overlay/UIElements 的交互链路里复现闪烁.
- 这说明:
  - Editor UI 信号在不同 Unity 版本/不同 UI 形态下本质不可靠.
  - 继续追加特殊情况只会产生更脆弱的分支地狱,并且仍然无法穷尽所有交互路径.
- 因此这里选择更确定性的契约:
  - EditMode 下默认只保证 SceneView 稳定.
  - GameView 需求通过“显式 override 或 AllCameras”解决.

**补充(新发现):**
- 在真实编辑器交互里,即使有了 viewport hint,仍然可能遇到 Unity 内部的 SceneView camera 实例抖动:
  - SceneView 这一帧参与渲染的 Camera 可能与 `SceneView.lastActiveSceneView.camera` 不是同一个实例.
  - 如果 `ActiveCameraOnly` 对排序/渲染都要求 “activeCam == currentCam”,就会出现:
    - SceneView 不排序(旋转到背后像没 sort)
    - SceneView 不渲染(整体消失/闪烁)
- 因此最终实现需要把“SceneView 稳定体验”作为硬约束:
  - EditMode 下允许 SceneView 相机触发排序(不强依赖 ActiveCamera 的实例相等).
  - EditMode 下把渲染提交给 SceneView 相机,保证你在 UI 交互时 SceneView 仍然可见.

**备选:**
- 继续做基于 Editor UI 信号的 SceneView/GameView 自动切换.
  - 缺点: SceneView overlay/UIElements 的信号链路难以可靠覆盖,仍可能闪烁.

### 5) Play 模式策略: 始终保证 Game/VR 相机

**选择:**
- Editor Play 模式下,即使 SceneView 获得焦点,ActiveCamera 也不切换到 SceneView.

**理由:**
- Play 模式下用户通常以 GameView 为主,SceneView 是辅助.
- 切换会导致“主视角性能/正确性”被辅助窗口影响,违背预期.

## Risks / Trade-offs

- [Risk] 默认启用 `ActiveCameraOnly` 会改变某些项目对“多相机可见性”的预期 → Mitigation: 提供 `AllCameras` 回退选项,并在文档里明确说明影响范围.
- [Risk] 多 SceneView 窗口时 `SceneView.lastActiveSceneView` 可能不是你想要的那个视图 → Mitigation: 实现里允许遍历 `SceneView.sceneViews` 做兜底选择,并在需要时再增强为“按当前渲染相机(camera callback)精确匹配”.
- [Risk] ActiveCamera 解析在 `-batchmode -nographics` 下可能不存在 Game/Scene 相机 → Mitigation: 当解析失败时保持 `AllCameras` 行为或直接 no-op(不排序不渲染),避免刷 error log.

## Migration Plan

- 默认值变化的迁移路径:
  - 需要多相机渲染的项目,在 `Project Settings/Gsplat` 把模式切回 `AllCameras`.
- 对已有场景/Prefab:
  - 不要求用户改动组件挂载方式.
  - 若项目存在自定义“主相机切换系统”,可通过提供的 override 接口把当前主相机显式设置为 ActiveCamera.
  - 已提供一个可选的轻量组件 `GsplatActiveCameraOverride`(挂在 Camera 上)来自动设置 override,用于镜头切换/过场/多相机系统.
    - 支持 `Priority` 与“最后启用者 wins”的同优先级 tie-break.

## Open Questions

- `ActiveCameraOnly` 模式下,是否需要对反射探针相机给出更明确的 warning(避免用户误以为 bug)?
