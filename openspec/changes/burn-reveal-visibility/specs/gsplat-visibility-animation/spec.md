## ADDED Requirements

### Requirement: Visibility animation is optional and non-breaking
当 `EnableVisibilityAnimation=false` 时,系统 MUST 保持旧行为:

- 不引入额外的显隐动画效果.
- show/hide 的可见性由既有逻辑决定(例如组件启用/禁用,或后端开关等).

#### Scenario: Default off keeps legacy rendering
- **WHEN** 用户没有开启 `EnableVisibilityAnimation`
- **THEN** 渲染结果与性能表现 MUST 与改动前一致

### Requirement: Show animation reveals splats as an expanding burn ring
当 show 动画被触发时,系统 MUST 以“中心 -> 向外扩散”的方式逐步显示:

- 起始时刻点云 MUST 完全不可见.
- 随着进度推进,从中心出现一圈“燃烧发光环”.
- 燃烧环 MUST 随时间向外扩散,并使被扫过区域逐步稳定可见.

#### Scenario: PlayShow starts from invisible and expands outward
- **WHEN** 用户调用 `PlayShow()`(或 `SetVisible(true, animated:true)`)
- **THEN** 点云 MUST 从“完全不可见”开始,并出现从中心向外扩散的燃烧环显示效果

### Requirement: Hide animation burns outward and ends fully hidden
当 hide 动画被触发时,系统 MUST 以“中心 -> 向外扩散”的方式逐步隐藏:

- 起始阶段 MUST 出现更强的燃烧发光环(相对 show 更亮).
- 燃烧环 MUST 随时间向外扩散.
- 被燃烧环扫过的区域 MUST 逐渐透明并最终消失.
- hide 动画结束后,对象 MUST 处于“完全不可见”的隐藏态.

#### Scenario: PlayHide burns outward and ends invisible
- **WHEN** 用户调用 `PlayHide()`(或 `SetVisible(false, animated:true)`)
- **THEN** 点云 MUST 逐步消失并在动画结束后完全不可见

### Requirement: Hidden state MUST stop sorting and draw submission
当对象处于隐藏态(即 hide 动画已结束)时,系统 MUST 满足:

- 系统 MUST 不再对该对象提交 draw.
- 系统 MUST 不再为该对象执行排序(即 sorter gather/dispatch 跳过该对象).

#### Scenario: Hidden object has no render cost
- **WHEN** hide 动画结束并进入隐藏态
- **THEN** 该对象 MUST 不再产生排序与渲染开销

### Requirement: Center is driven by a user-specified Transform with a safe fallback
系统 MUST 支持以用户指定中心来驱动环形扩散:

- 如果 `VisibilityCenter` 不为空,中心 MUST 以该 Transform 的位置为准.
- 如果 `VisibilityCenter` 为空,中心 MUST 回退为点云 bounds.center.

#### Scenario: Center transform takes precedence
- **WHEN** 用户设置了 `VisibilityCenter`
- **THEN** 环形扩散中心 MUST 与该 Transform 对齐

### Requirement: Noise behavior changes across show vs hide
系统 MUST 在显隐动画期间引入噪波(noise),并满足阶段性语义:

- show: 噪波强度 MUST 随“稳定显示”而减弱.
- hide: 噪波强度 MUST 随“燃烧消失”而增强,并呈现碎屑/灰烬感.
- 系统 SHOULD 使用空间上更连续的噪声场(例如 value noise / domain warp),让观感更像“烟雾的扭曲与波动”,避免白噪声式的混乱闪烁.

#### Scenario: Noise decreases during show and increases during hide
- **WHEN** show 动画推进到后半段
- **THEN** 噪波效果 MUST 更弱且更稳定
- **WHEN** hide 动画推进到后半段
- **THEN** 噪波效果 MUST 更强且更碎屑化

### Requirement: Show/Hide ring and trail widths MUST be configurable independently
系统 MUST 支持分别调节 show 与 hide 的环形宽度与拖尾宽度:

- show:
  - `ShowRingWidthNormalized`
  - `ShowTrailWidthNormalized`
- hide:
  - `HideRingWidthNormalized`
  - `HideTrailWidthNormalized`

#### Scenario: Different widths affect show vs hide independently
- **WHEN** 用户把 show 的 ring/trail 与 hide 的 ring/trail 设为不同值
- **THEN** show 与 hide 的燃烧环/拖尾观感 MUST 能分别变化,互不耦合

### Requirement: Show/Hide MUST also animate splat size (tiny <-> normal)
系统 MUST 在显隐动画期间让高斯基元的屏幕尺寸发生变化,以强化“燃烧显现/燃烧成灰”的动感:

- show:
  - 当 splat 刚被燃烧环扫到时,其尺寸 MUST 从“极小”开始.
  - 随着燃烧环继续推进(局部进度增加),splat 尺寸 MUST 平滑变为正常大小.
- hide:
  - 当 splat 被燃烧环扫过时,其尺寸 MUST 从正常大小平滑缩小到“极小”.

#### Scenario: Splats grow during show and shrink during hide
- **WHEN** show 动画推进,且某个 splat 从“刚出现”进入“稳定显示”
- **THEN** 该 splat 的屏幕尺寸 MUST 从极小平滑变到正常
- **WHEN** hide 动画推进,且某个 splat 从“未燃烧”进入“将消失”
- **THEN** 该 splat 的屏幕尺寸 MUST 从正常平滑缩小到极小

### Requirement: Noise MUST visibly warp splat positions (space distortion particles)
系统 MUST 支持一种“扭曲空间一样”的噪波效果,其关键特征是:

- 在 show/hide 期间,噪波 MUST 能够造成 splat 的中心位置出现明显位移(不仅仅是 alpha 抖动).
- show: 位移强度 MUST 随“稳定显示”而减弱.
- hide: 位移强度 MUST 随“燃烧消失”而增强.
- 系统 SHOULD 提供一个独立的位移强度倍率(例如 `WarpStrength`),用于在不必拉高 `NoiseStrength` 的情况下获得更明显的 pos 位移.
- 为了保证 reveal/burn 阈值稳定,位移 MUST 不应参与“燃烧环是否扫过(passed/ring)”的判定,避免阈值抖动导致闪烁.

#### Scenario: Warp is visible and phase-consistent
- **WHEN** show 动画推进到后半段
- **THEN** splat 的位移扭曲 MUST 明显变弱且趋于稳定
- **WHEN** hide 动画推进到后半段
- **THEN** splat 的位移扭曲 MUST 更明显且更碎屑化

### Requirement: Inspector provides Show/Hide controls for rapid iteration
系统 MUST 在 Unity Editor 的 Inspector 中提供快捷触发:

- 至少提供 "Show" 与 "Hide" 两个按钮.
- 按钮 MUST 分别触发 show/hide 动画(等价调用公开 API).

#### Scenario: Clicking inspector buttons triggers animation
- **WHEN** 用户在 Inspector 点击 "Show"
- **THEN** 系统 MUST 播放 show 动画
- **WHEN** 用户在 Inspector 点击 "Hide"
- **THEN** 系统 MUST 播放 hide 动画

### Requirement: In Editor EditMode, show/hide animation MUST advance without mouse movement
系统 MUST 在 Unity Editor 非 Play 模式下,当对象处于 Showing/Hiding 时主动请求 Editor 视图刷新,使 show/hide 动画无需鼠标交互也能连续播放到结束.

在 Unity Editor 非 Play 模式下,SceneView/GameView 往往是“事件驱动 repaint”:

- 当用户鼠标不动时,视口可能不会持续刷新.
- 如果显隐动画只依赖 shader/uniform 的时间推进,就会出现“看起来不播放,晃一下鼠标才更新”的体验问题.

因此系统 MUST 满足:

- **WHEN** 处于 Editor 非 Play 模式且对象正在 Showing/Hiding
- **THEN** 系统 MUST 主动请求 Editor 视图刷新,使动画能够连续播放到结束(即使没有鼠标交互)
- 动画结束后系统 SHOULD 停止持续刷新请求,避免空闲耗电与不必要的 Editor 开销

#### Scenario: Animation plays to completion even when the viewport is idle
- **GIVEN** 用户在 Editor 非 Play 模式点击 "Show"(或调用 `PlayShow()`)
- **WHEN** 在 show duration 内没有鼠标交互
- **THEN** 动画 MUST 仍能持续推进并在时长结束后进入 Visible
- **GIVEN** 用户在 Editor 非 Play 模式点击 "Hide"(或调用 `PlayHide()`)
- **WHEN** 在 hide duration 内没有鼠标交互
- **THEN** 动画 MUST 仍能持续推进并在时长结束后进入 Hidden

### Requirement: Burn ring expansion MUST use easeOutCirc timing
系统 MUST 使用 `easeOutCirc` 作为燃烧环扩散的时间曲线,而不是匀速线性推进:

- 前半段扩散 MUST 更快(更有冲击力).
- 后半段扩散 MUST 逐渐减速(收尾更自然,减少最后一瞬间的突兀感).

#### Scenario: Ring radius advances with easeOutCirc
- **GIVEN** show/hide 动画正在播放
- **WHEN** `_VisibilityProgress` 从 0 递增到 1
- **THEN** 燃烧环半径的推进 MUST 呈现 `easeOutCirc` 的非线性速度曲线
