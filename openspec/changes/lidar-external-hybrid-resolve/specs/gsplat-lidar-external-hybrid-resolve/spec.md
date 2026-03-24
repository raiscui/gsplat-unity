## ADDED Requirements

### Requirement: Renderer SHALL expose independent hybrid resolve modes
系统 MUST 为 `GsplatRenderer` 与 `GsplatSequenceRenderer` 提供两组独立的 frustum external GPU capture hybrid resolve 配置:

- `LidarExternalEdgeAwareResolveMode`
- `LidarExternalSubpixelResolveMode`

系统 MUST 支持以下 edge-aware mode:

- `Off`
- `Kernel2x2`
- `Kernel3x3`

系统 MUST 支持以下 subpixel mode:

- `Off`
- `Quad4`

两组 mode MUST 能独立设置,并 MUST 支持以下组合:

- `Off + Off`
- `Kernel2x2/Kernel3x3 + Off`
- `Off + Quad4`
- `Kernel2x2/Kernel3x3 + Quad4`

默认配置 MUST 为 `Off + Off`。

#### Scenario: Independent hybrid resolve controls are available on both renderer types
- **WHEN** 用户在 `GsplatRenderer` 或 `GsplatSequenceRenderer` 上启用 frustum external GPU capture
- **THEN** 系统 MUST 提供 edge-aware 与 subpixel 两组独立 mode 配置
- **AND** 用户 MUST 能分别关闭或开启它们

#### Scenario: Default configuration preserves the scheme from supersampling-only behavior
- **WHEN** 用户没有显式开启 edge-aware 或 subpixel resolve
- **THEN** 系统 MUST 使用 `Off + Off` 作为默认值
- **AND** 默认行为 MUST 保持与方案1一致

### Requirement: Edge-aware nearest resolve SHALL support kernel-based neighborhood filtering with center fallback
当 `LidarExternalEdgeAwareResolveMode` 为 `Kernel2x2` 或 `Kernel3x3` 时,系统 MUST 围绕当前 candidate uv 读取对应大小的 depth 邻域。
系统 MUST 先根据深度差、ray-distance 一致性或等价的保边条件过滤候选,只保留与当前主样本足够接近的候选。
在通过过滤的候选中,系统 MUST 选择最近且可信的样本作为该 candidate 的 resolved result。
如果过滤后没有可信候选,系统 MUST 回退到该 candidate 的中心 point sample。

#### Scenario: Edge-aware resolve selects a trusted nearest sample inside the kernel
- **WHEN** edge-aware resolve 已开启,且邻域中存在多个深度样本
- **THEN** 系统 MUST 先过滤掉与主样本不一致的跨边界候选
- **AND** 系统 MUST 从剩余可信候选中选择最近样本

#### Scenario: Edge-aware resolve falls back to the center sample when filtering rejects the neighborhood
- **WHEN** edge-aware resolve 已开启,但邻域候选全部未通过一致性过滤
- **THEN** 系统 MUST 回退到当前 candidate 的中心 point sample
- **AND** 不得因为过滤失败返回未定义结果或制造新的空洞路径

### Requirement: Subpixel resolve SHALL support deterministic Quad4 candidate sampling
当 `LidarExternalSubpixelResolveMode` 为 `Quad4` 时,系统 MUST 基于当前 uv 生成固定的 4 个亚像素 candidate offset。
这些 offset MUST 是 deterministic 的,不得依赖 frame-random jitter、temporal noise 或其他不稳定随机源。
当 subpixel resolve 关闭时,系统 MUST 只评估中心 uv。

#### Scenario: Quad4 mode evaluates four deterministic subpixel candidates
- **WHEN** 用户把 `LidarExternalSubpixelResolveMode` 设为 `Quad4`
- **THEN** 系统 MUST 为当前 external resolve 评估 4 个固定亚像素 candidate
- **AND** 同一输入条件下,这些 candidate 的位置 MUST 保持可复现

#### Scenario: Off mode keeps the candidate set at the center sample only
- **WHEN** 用户把 `LidarExternalSubpixelResolveMode` 设为 `Off`
- **THEN** 系统 MUST 只评估中心 uv 对应的 candidate
- **AND** 不得隐式引入随机 subpixel 扰动

### Requirement: Hybrid resolve SHALL define a fixed combined evaluation order
当 edge-aware resolve 与 subpixel resolve 同时开启时,系统 MUST 使用固定顺序:

1. 先生成 subpixel candidate uv
2. 再对每个 candidate 执行 edge-aware neighborhood resolve
3. 最后在所有 candidate 结果中统一选择最近且可信的 winner

系统 MUST NOT 反转这条顺序,也 MUST NOT 在不同 renderer 路径上使用不同顺序。

#### Scenario: Combined mode evaluates candidates before neighborhood filtering and winner selection
- **WHEN** 用户同时开启 edge-aware resolve 与 `Quad4`
- **THEN** 系统 MUST 先生成 subpixel candidate 集合
- **AND** 系统 MUST 在每个 candidate 完成 edge-aware resolve 后再统一选出 final winner

### Requirement: Final external color SHALL follow the resolved depth winner
系统 MUST 把 final external hit 视为一条统一的 resolved winner 记录。
无论 hybrid resolve 采用哪种组合,final depth 与 final surfaceColor MUST 来自同一个 resolved winner。
系统 MUST NOT 对 color 独立做 average、blur、单独 bilinear resolve 或其他会让颜色与深度脱钩的处理。

#### Scenario: Depth and color are sourced from the same resolved winner
- **WHEN** hybrid resolve 在多个 candidate 或多个邻域样本中选出了 final winner
- **THEN** 系统 MUST 使用该 winner 对应的 depth 作为 final external hit distance
- **AND** 系统 MUST 使用同一 winner 对应的 color 作为 final surfaceColor

### Requirement: Hybrid resolve SHALL preserve nearest-surface semantics without cross-surface averaging
系统 MUST 将 hybrid resolve 定义为“保 nearest-surface 语义的邻域选样 resolve”。
系统 MUST NOT 使用普通 blur、naive bilinear depth mixing 或其他会把前后表面深度直接混成中间值的方式来实现 hybrid resolve。
在 silhouette 边界同时存在前后两层表面时,系统 MUST 继续遵守最近表面语义。

#### Scenario: Silhouette edges do not average front and back surfaces into an intermediate hit
- **WHEN** external target 的轮廓边界附近同时存在前景与背景两个深度层
- **THEN** 系统 MUST 保持 nearest-surface 语义
- **AND** 不得把前后表面直接混成一个中间深度结果

### Requirement: The system SHALL preserve current point texel behavior when both modes are Off
当 `LidarExternalEdgeAwareResolveMode = Off` 且 `LidarExternalSubpixelResolveMode = Off` 时,系统 MUST 保持方案1的 external resolve 行为。
也就是说,系统 MUST 继续使用中心 uv 对应的 point texel read 语义,并 MUST 保持与现有 supersampling-only 路径一致的最近命中契约。

#### Scenario: Off plus Off keeps the supersampling-only path unchanged
- **WHEN** 两组 hybrid resolve mode 都为 `Off`
- **THEN** 系统 MUST 继续走当前中心 uv 的 point texel read 路径
- **AND** 不得为旧场景引入新的邻域混样或额外随机行为
