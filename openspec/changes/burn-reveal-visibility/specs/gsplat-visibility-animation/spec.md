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

### Requirement: Hide fade MUST NOT linger due to outward edge noise
系统 MUST 避免 hide 动画末尾出现“少量 splats 长时间残留”的现象(即便开启了边界噪声与扭曲):

- 边界噪声 MAY 影响 ring/glow 的抖动质感.
- 但边界噪声 MUST NOT 以“往外推边界”的方式长期阻止 fade/shrink 完成,
  否则会出现局部 passed 无法到达 1 的 lingering.

#### Scenario: Hide completes without lingering splats at the end
- **GIVEN** 用户启用了显隐动画并播放 hide
- **WHEN** hide 进度接近结束(progress→1)
- **THEN** splats MUST 能稳定淡出并在动画结束时不残留(不会因为正向噪声把边界推外而长期半透明可见)

### Requirement: Glow boost is configurable for show and hide
系统 MUST 支持对 show/hide 的燃烧发光进行额外 boost 调节,以便实现更“点燃瞬间”的冲击力:

- show:
  - 系统 MUST 提供 `ShowGlowStartBoost`.
  - 当 `ShowGlowStartBoost > 1` 时,show 的燃烧环在起始阶段 SHOULD 更亮.
- hide:
  - 系统 MUST 保留并支持 `HideGlowStartBoost`.
  - 当 `HideGlowStartBoost > 1` 时,hide 的燃烧前沿(ring) SHOULD 更亮.

#### Scenario: Boost values affect ring glow intensity
- **GIVEN** 用户启用了显隐动画
- **WHEN** 用户调大 `ShowGlowStartBoost`
- **THEN** show 的燃烧环 glow 在起始阶段 SHOULD 更亮
- **WHEN** 用户调大 `HideGlowStartBoost`
- **THEN** hide 的燃烧前沿 glow SHOULD 更亮

### Requirement: Hide glow tail SHOULD decay inward (avoid abrupt outer rim)
系统 SHOULD 在 hide 期间让 glow 的衰减方向更符合“中心先烧掉”的语义:

- hide 的燃烧前沿(ring) SHOULD 保持明显亮度,不应因为扩散到外围就整体变暗导致外围突兀.
- 系统 SHOULD 在燃烧前沿内侧提供一个 afterglow tail,并使其朝内(中心方向)逐渐衰减.

#### Scenario: Hide glow has bright front and inward-decaying tail
- **GIVEN** hide 动画正在播放
- **WHEN** 燃烧前沿向外推进
- **THEN** 前沿 glow SHOULD 仍然明显,且内侧 tail SHOULD 随向内距离衰减

### Requirement: Show glow SHOULD also have a bright front and an inward afterglow tail
系统 SHOULD 在 show 期间也保证/强化“前沿更亮 + 内侧余辉朝内衰减”的语义,避免出现“外侧很亮但内部发暗”的体感:

- show 的燃烧前沿(ring) SHOULD 更像“燃烧前沿”,主要出现在外侧(edgeDist>=0).
- 前沿 ring SHOULD 保持更亮的观感(可由 `ShowGlowStartBoost` 增强).
- 系统 SHOULD 在燃烧前沿内侧提供一个 afterglow tail,并使其朝内(中心方向)逐渐衰减,用于补足“内部不够亮”.
- 系统 SHOULD 确保该内侧 afterglow 在实际渲染中肉眼可见(例如不要被 premul alpha 完全压没).

#### Scenario: Show glow has bright front and visible inward afterglow
- **GIVEN** show 动画正在播放
- **WHEN** 燃烧前沿向外推进
- **THEN** 前沿 ring SHOULD 更亮且位于外侧,内侧 afterglow SHOULD 朝内衰减且可被清晰看到

### Requirement: Show ring glow MAY sparkle (curl noise twinkle) when enabled
系统 MAY 在 show 阶段为燃烧前沿 ring 的 glow 提供“星火闪烁”效果,并且可调:

- 系统 MAY 提供 `ShowGlowSparkleStrength` 参数:
  - `0` 表示关闭闪烁(默认值 SHOULD 为 0,避免升级后意外改变观感/性能).
  - 值越大,ring glow 的局部亮点与闪烁 SHOULD 更明显.
- 闪烁 SHOULD 通过更平滑的噪声场实现(例如 curl-like 噪声场),
  使其更像火星/星星闪闪,而不是白噪声式的随机抖动.
- 闪烁 SHOULD 主要作用在 ring 前沿,不应把整段 tail 都变成噪点墙.

#### Scenario: Sparkle strength changes ring glow flicker
- **GIVEN** 用户启用了显隐动画并播放 show
- **WHEN** 用户把 `ShowGlowSparkleStrength` 从 0 调大
- **THEN** ring 前沿 glow SHOULD 出现更明显的“星火闪烁”亮度变化

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
  - hide 的 shrink SHOULD 呈现“先快后慢”的节奏:
    - 在燃烧前沿(glow)阶段快速缩到较小但仍可见的尺寸.
    - 随后更多由 alpha trail 慢慢消失,避免因尺寸过早接近 0 而显得消失太快.

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

### Requirement: Visibility noise mode MUST be selectable (dropdown)
系统 MUST 提供一个可切换的噪声模式,用于对比与调参:

- 系统 MUST 在 `GsplatRenderer` 与 `GsplatSequenceRenderer` 上提供 `VisibilityNoiseMode` 下拉选项.
- 默认值 MUST 为 `ValueSmoke`,以保持当前项目在升级后观感不被意外改变.
- 系统 MUST 至少提供两种可选模式:
  - `ValueSmoke`: 平滑 value noise + 轻量 domain warp(当前默认,更像烟雾波动).
  - `CurlSmoke`: curl-like 向量场(更像旋涡/流动,主要用于 position warp).
- 系统 MAY 提供 `HashLegacy` 作为旧版对照模式(更碎更抖),用于调试或性能基线对比.

#### Scenario: Switching noise mode changes warp field without breaking semantics
- **GIVEN** 用户启用了显隐动画
- **WHEN** 用户把 `VisibilityNoiseMode` 从 `ValueSmoke` 切换为 `CurlSmoke`
- **THEN** show/hide 期间的 position warp 方向 MUST 更像连续的流动/旋涡,且 reveal/burn 的 passed/ring 判定 MUST 仍保持稳定(不因 warp 抖动阈值)

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

### Requirement: Burn ring expansion MUST use easeInOutQuad timing
系统 MUST 使用 `easeInOutQuad` 作为燃烧环扩散的时间曲线,而不是匀速线性推进:

- 起始阶段扩散 MUST 相对更慢(更像“点燃/聚能”).
- 中段扩散 MUST 相对更快(扩散更明显).
- 末尾阶段扩散 MUST 逐渐减速(收尾更自然,减少最后一瞬间的突兀感).

#### Scenario: Ring radius advances with easeInOutQuad
- **GIVEN** show/hide 动画正在播放
- **WHEN** `_VisibilityProgress` 从 0 递增到 1
- **THEN** 燃烧环半径的推进 MUST 呈现 `easeInOutQuad` 的非线性速度曲线
