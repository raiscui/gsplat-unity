## ADDED Requirements

### Requirement: Renderer SHALL expose supersampled external capture resolution controls
系统 MUST 为 `GsplatRenderer` 与 `GsplatSequenceRenderer` 提供 frustum external GPU capture 的分辨率控制入口,至少包括:

- `LidarExternalCaptureResolutionMode`
- `LidarExternalCaptureResolutionScale`
- `LidarExternalCaptureResolution`

系统 MUST 支持以下三种模式:

- `Auto`
- `Scale`
- `Explicit`

当用户未启用 frustum external GPU capture 时,这些参数 MUST NOT 改变现有非相关路径的行为。

#### Scenario: Resolution controls are available for frustum external capture
- **WHEN** 用户在 `GsplatRenderer` 或 `GsplatSequenceRenderer` 上启用 camera-frustum external GPU capture
- **THEN** 系统 MUST 提供 `Auto`、`Scale`、`Explicit` 三种 capture resolution mode
- **AND** 用户 MUST 能通过 `scale` 或显式宽高控制 external capture 的离屏分辨率

### Requirement: Scale mode SHALL derive predictable supersampled capture size from the Auto baseline
系统 MUST 将 `Scale` 模式定义为基于 Auto 基准尺寸的 supersampling / downsampling 入口。
在 `Scale` 模式下,系统 MUST 先确定 Auto 基准 capture 宽高,再对每个维度乘以 `LidarExternalCaptureResolutionScale`。
最终 capture 宽高 MUST 使用稳定、可预测的舍入规则,并 MUST 遵守硬件纹理尺寸上限。

#### Scenario: Scale mode increases capture size above the Auto baseline
- **WHEN** 用户把 resolution mode 设为 `Scale`,且 `LidarExternalCaptureResolutionScale > 1`
- **THEN** 系统 MUST 生成比 Auto 基准更高的 external capture 宽高
- **AND** 最终宽高 MUST 仍然遵守硬件支持上限

#### Scenario: Invalid scale input falls back to a safe predictable size
- **WHEN** 用户提供非法的 scale 值,例如 `NaN`、`Infinity` 或 `<= 0`
- **THEN** 系统 MUST 回退到安全且可预测的 capture 尺寸决策
- **AND** 不得因为非法 scale 进入未定义尺寸或资源申请失败状态

### Requirement: Supersampling SHALL preserve nearest-surface and nearest-hit semantics
系统 MUST 将 supersampling 视为“提高 external capture fidelity”的手段,而不是改变 external hit 语义的手段。
启用 supersampling 后,系统 MUST 继续保持 external capture 的最近表面选择语义,并 MUST 继续让 external hit 与 gsplat hit 按逐 cell 最近距离竞争。
系统 MUST NOT 通过普通 blur、naive bilinear 深度混合或其他会跨边界混合前后表面的方式来实现这项能力。

#### Scenario: Supersampling changes fidelity without changing hit-selection semantics
- **WHEN** 用户把 external capture 从 `Auto` 提高到 `Scale > 1`
- **THEN** 系统 MAY 减轻 external target 边缘的 depth stair-stepping
- **AND** 系统 MUST 继续保持 nearest-surface / nearest-hit 的选择语义不变

#### Scenario: Front and back surfaces are not averaged across a silhouette edge
- **WHEN** external target 的轮廓边界同时覆盖前后两个深度层
- **THEN** 系统 MUST 继续选择符合最近表面语义的 external hit
- **AND** 不得把前后表面深度直接混成一个中间距离

### Requirement: Supersampling SHALL apply consistently to external depth and color capture inputs
当 external capture 启用 supersampling 时,系统 MUST 让 external depth capture 与 external surfaceColor capture 使用一致的 capture layout 与分辨率决策。
static 与 dynamic 两组 external capture 在同一 frustum sensor frame 下,也 MUST 共享一致的 capture-size 规则。

#### Scenario: Depth and color captures scale together
- **WHEN** 用户提高 external capture 的 supersampling scale
- **THEN** external depth 与 external surfaceColor capture MUST 同步使用放大后的 capture layout
- **AND** 不得出现 depth 与 color 使用不同离屏分辨率而导致命中与颜色对不齐的状态

### Requirement: The system SHALL present supersampling as the recommended mitigation for external capture stair-stepping
系统 MUST 在 Inspector、文档或其他用户可见说明中,把 supersampling 明确描述为 frustum external GPU capture depth stair-stepping 的首选缓解手段。
系统 MUST 明确说明:

- `Scale > 1` 用于提高 external depth / color capture 精度
- 更高分辨率会带来额外显存、带宽或渲染成本
- supersampling 不能等价替代更高级的 edge-aware resolve

#### Scenario: User looks for a quality fix for stair-stepped external depth
- **WHEN** 用户在 frustum external GPU capture 中观察到明显的台阶感,并查看 Inspector 或文档说明
- **THEN** 系统 MUST 把 supersampling 标记为首选质量调节方向
- **AND** 系统 MUST 同时提示更高 scale 会带来额外性能与显存开销
