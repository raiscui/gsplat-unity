## ADDED Requirements

### Requirement: CameraMode configuration
系统 MUST 提供一个可配置的相机模式 `CameraMode`,用于控制 Gsplat 是否对所有相机排序+渲染.

`CameraMode` MUST 支持:
- `AllCameras`: 任意相机都可以触发排序并渲染 Gsplat(兼容模式).
- `ActiveCameraOnly`: 只保证一个 ActiveCamera 的正确性与性能(性能模式).

系统 MUST 将 `ActiveCameraOnly` 作为默认值.

#### Scenario: Default is ActiveCameraOnly
- **WHEN** 用户首次创建/加载 `GsplatSettings` 且未显式修改 `CameraMode`
- **THEN** `CameraMode` MUST 为 `ActiveCameraOnly`

#### Scenario: User can switch back to AllCameras
- **WHEN** 用户将 `CameraMode` 设置为 `AllCameras`
- **THEN** 系统 MUST 恢复“所有相机都能看到 Gsplat”的兼容行为

### Requirement: ActiveCameraOnly gates GPU sorting
当 `CameraMode=ActiveCameraOnly` 时,系统 MUST 只对 ActiveCamera 触发 GPU sort.
对于非 ActiveCamera 的相机,系统 MUST 跳过 GPU sort,以避免多相机重复排序成本.

#### Scenario: Only the active camera triggers sorting
- **WHEN** 同一帧内存在多个相机会渲染该对象(cullingMask 包含其 layer),且 `CameraMode=ActiveCameraOnly`
- **THEN** 系统 MUST 只对 ActiveCamera 执行排序,并且对其它相机不执行排序

### Requirement: ActiveCameraOnly gates rendering (Play Mode / Player)
当 `CameraMode=ActiveCameraOnly` 且处于 Play 模式或 Player build 时,系统 MUST 只对 ActiveCamera 提交 Gsplat 的 draw call.
对于非 ActiveCamera 的相机,系统 MUST 不渲染 Gsplat,以避免“渲染了但排序不是基于该相机”的错误显示.

#### Scenario: Non-active camera does not render Gsplat
- **WHEN** Play 模式或 Player build 下,`CameraMode=ActiveCameraOnly` 且某个相机不是 ActiveCamera
- **THEN** 该相机 MUST 不渲染 Gsplat

### Requirement: Editor Edit Mode keeps SceneView stable
当处于 Unity Editor 且 `Application.isPlaying==false` 时,系统 MUST 保证 SceneView 体验稳定:

- 系统 MUST 允许 SceneView 相机触发排序与渲染,并且在你与 Inspector/Hierarchy 等 Editor UI 交互时 SceneView MUST 不出现整体“显示/不显示”的闪烁.
- 系统 MUST NOT 依赖 `focusedWindow` 等噪声信号来决定“SceneView 这一帧是否渲染”.
- 系统 SHOULD 避免在 EditMode 因内部 camera 实例抖动导致:
  - SceneView 不排序(旋转到背后像没 sort)
  - SceneView 不渲染(整体消失)

#### Scenario: SceneView does not flicker while interacting with Editor UI
- **WHEN** Editor 非 Play 模式,用户正在操作 SceneView(例如旋转视角),并且鼠标划过或交互 Inspector/Hierarchy 等 UI
- **THEN** SceneView MUST 仍然渲染 Gsplat(不会整体消失/闪烁),并且排序 MUST 能够随 SceneView 相机更新

### Requirement: ActiveCamera selection in Play Mode
当 `Application.isPlaying==true` 时,系统 MUST 以 Game/VR 相机作为 ActiveCamera 候选集合.
系统 MUST 忽略 Editor SceneView 相机对 ActiveCamera 的影响(即使 SceneView 获得焦点).

若存在显式 override(见下一个 Requirement),系统 MUST 优先使用 override.

否则系统 MUST 按以下优先级选择 ActiveCamera:
1. 若候选集合中只有 1 个相机,ActiveCamera MUST 为该相机.
2. 若 `Camera.main` 在候选集合中,ActiveCamera MUST 为 `Camera.main`.
3. 否则,ActiveCamera MUST 选择 depth 最大的那个候选相机.

#### Scenario: Single main camera is selected
- **WHEN** Play 模式下场景中只有 1 个启用的 Game/VR 相机
- **THEN** ActiveCamera MUST 为该相机

#### Scenario: Camera.main is preferred when multiple cameras exist
- **WHEN** Play 模式下存在多个启用的 Game/VR 相机,且其中一个为 `Camera.main`
- **THEN** ActiveCamera MUST 为 `Camera.main`

### Requirement: ActiveCamera selection in Editor Edit Mode
当处于 Unity Editor 且 `Application.isPlaying==false` 时,系统 MUST 以 SceneView 的稳定体验为优先目标选择 ActiveCamera.

系统 MUST NOT 基于 `focusedWindow` / `mouseOverWindow` 等 Editor UI 信号在 SceneView/GameView 之间自动切换 ActiveCamera,
因为这些信号在 SceneView overlay/UIElements 区域可能失真,并导致 ActiveCameraOnly 出现“整体显示/不显示”的闪烁.

- 若存在一个有效的显式 override 相机(见下一个 Requirement),ActiveCamera MUST 为 override 相机.
- 否则:
  - 若存在 SceneView 相机,ActiveCamera MUST 为一个 SceneView 相机.
    - 并且系统 MUST 满足上面的 "Editor Edit Mode keeps SceneView stable" 要求.
  - 若当前环境不存在 SceneView(例如 batchmode/headless),ActiveCamera MAY 回退到 "ActiveCamera selection in Play Mode" 的选择规则,
    或沿用上一帧的解析结果.

#### Scenario: Edit Mode defaults to SceneView
- **WHEN** Editor 非 Play 模式,且没有显式 override 相机
- **THEN** ActiveCamera MUST 为一个 SceneView 相机(若存在),并且 SceneView MUST 不出现整体“显示/不显示”的闪烁

#### Scenario: GameView can be targeted via explicit override in Edit Mode
- **WHEN** Editor 非 Play 模式,用户通过显式 override 指定了一个有效的 Game/VR 相机
- **THEN** ActiveCamera MUST 为该 Game/VR 相机,并且该相机 MUST 能持续渲染 Gsplat

### Requirement: Explicit active camera override
系统 MUST 提供一个显式 override 机制,允许用户/脚本指定 ActiveCamera(至少覆盖 Play Mode 的选择规则).

当 override 相机存在且有效(非空且可用)时:
- ActiveCamera MUST 为 override 相机.

#### Scenario: Override camera takes precedence
- **WHEN** 用户/脚本设置了一个有效的 override 相机
- **THEN** ActiveCamera MUST 为该 override 相机,并忽略自动选择规则
