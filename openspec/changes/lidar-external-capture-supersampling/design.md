## Context

当前 frustum external GPU capture 已经具备完整的 depth / surfaceColor capture 与 GPU resolve 链路,并且 runtime API 里已经暴露了:

- `LidarExternalCaptureResolutionMode`
- `LidarExternalCaptureResolutionScale`
- `LidarExternalCaptureResolution`

但从现有实现可以确认,external depth resolve 仍然是“先渲染离屏 depth RT,再在 `Gsplat.compute` 里按整数 texel 直接 `Load(...)`”的路径. 这条路径在 external target 的轮廓、斜面和远处细结构上,会把屏幕空间 depth 的像素离散性直接带进 LiDAR external hit,表现成明显的台阶、断续细缝和边缘不稳定。

这次 change 不准备直接引入更激进的 depth 过渡算法. 原因也很明确:

- 普通 blur 会把前后表面混掉
- naive bilinear 深度混合会破坏最近表面语义
- edge-aware resolve 复杂度更高,也更适合单独作为后续 change

因此,方案1选择风险最低、语义最稳定的路线: 保留 point resolve 与 nearest-surface / nearest-hit 契约,先通过提高 external capture 的离屏分辨率来降低台阶感。

## Goals / Non-Goals

**Goals:**

- 把 external capture supersampling 正式定义为 frustum external GPU capture 的质量控制路径。
- 在不改变 current point resolve 语义的前提下,通过更高分辨率的 depth / surfaceColor capture 减少 depth 台阶。
- 明确 `Auto / Scale / Explicit` 三种 capture resolution mode 的角色,尤其把 `Scale` 定义为首选 supersampling 入口。
- 让 static / dynamic 两组 external capture 在同一套 capture-size 规则下工作,避免两组结果精度不一致。
- 明确硬件上限 clamp、非法输入 sanitize 与用户可预测的尺寸推导规则。
- 在 Inspector / 文档里把 supersampling 明确成 external capture depth stair-stepping 的推荐缓解手段。

**Non-Goals:**

- 不在本 change 中引入 blur、naive bilinear 深度混合或 edge-aware resolve。
- 不改变 external hit 与 gsplat hit 的 nearest-hit 竞争规则。
- 不把普通 camera hardware depth 直接改造成新的连续几何求交模型。
- 不把“规则 LiDAR 栅格自身产生的 moire / 拍频”一并定义成这个 change 的解决范围。
- 不要求所有用户默认承担更高显存和带宽开销; 默认行为应尽量保持现状。

## Decisions

### 1) 保留 point resolve 语义,只提高 capture fidelity

**Decision:**

- external depth / surfaceColor capture 仍然保持“capture -> resolve -> per-cell nearest-hit competition”的现有架构。
- supersampling 只提高 capture RT 的空间采样密度,不改 `Gsplat.compute` 中“每个 LiDAR cell 读取一个 texel 并重建 external hit”的基本语义。
- 不在方案1里对 depth 做 blur、bilinear 混合或邻域投票。

**Alternatives considered:**

- A) 直接做 depth blur
  - 问题: 会把前景与背景深度混成中间值,破坏 first visible surface 语义。
- B) 直接做 bilinear depth sampling
  - 问题: 跨边界混合时仍会把两个表面错误平均。
- C) 直接做 edge-aware resolve
  - 问题: 方向正确,但它是更高风险、更高复杂度的后续方案,不适合当作方案1。

**Why:**

- 用户当前最关心的是“消除台阶感”,不是重写整个 external depth resolve 模型。
- supersampling 能先降低 texel 量化误差,同时保留最重要的最近表面语义。

### 2) 继续沿用现有 resolution mode API,把 `Scale` 定义为首选 supersampling 入口

**Decision:**

- 保留现有三种模式:
  - `Auto`: 使用当前 frustum capture 的默认基准尺寸
  - `Scale`: 在 Auto 基准上乘以倍率
  - `Explicit`: 用户直接指定 capture 宽高
- 其中 `Scale` 被正式定义为 external capture supersampling / downsampling 的主入口。
- `Explicit` 保留给高级用户做精确控制,但不作为解决台阶问题的首选推荐。

**Alternatives considered:**

- A) 新增一个独立的 `SupersampleFactor` 字段
  - 问题: 会与现有 `Scale` 语义重叠,增加 API 冗余。
- B) 把 `Explicit` 作为唯一质量入口
  - 问题: 用户需要自己推宽高,也更容易配出错误 aspect。
- C) 让 `Auto` 默认自动放大到更高分辨率
  - 问题: 会把额外开销变成隐式默认行为,不利于兼容现有场景。

**Why:**

- 当前 API 已经具备表达 supersampling 的能力,更好的做法是把语义定义清楚,而不是继续新增参数。
- `Scale` 保留了 Auto 的基准来源,又把质量和性能权衡清晰交给用户。

### 3) `Scale` 必须基于统一的 Auto 基准做可预测推导

**Decision:**

- `Scale` 模式下的 capture 宽高必须先从 Auto 基准尺寸推导,再逐维乘以 scale。
- 逐维缩放后的结果必须做稳定舍入与硬件上限 clamp,避免不同调用点或不同分组得出不一致尺寸。
- static / dynamic 两组 external capture 必须共享同一套 capture-size 规则,只允许内容更新频率不同,不允许采样布局语义不同。

**Alternatives considered:**

- A) static / dynamic 各自推自己的 capture 尺寸
  - 问题: 同一相机下两组结果的深度精度会飘,合并时更难解释。
- B) 允许 scale 在不同平台或不同路径下使用不同舍入规则
  - 问题: 用户无法预测最终尺寸,测试也无法稳定锁定契约。

**Why:**

- 这是一个质量控制能力,它的第一要求就是“可预测”。
- 尺寸推导规则一旦不稳定,不仅文档难写,测试也无法形成长期护栏。

### 4) supersampling 必须同时作用于 depth 与 surfaceColor capture

**Decision:**

- external capture 只要启用 supersampling,就必须让 depth RT 与 surfaceColor RT 一起使用相同的 supersampled capture layout。
- 不允许 depth 放大而 color 仍保留旧分辨率,也不允许 static / dynamic 的 color/depth 在布局上失配。

**Alternatives considered:**

- A) 只给 depth 做 supersampling
  - 问题: 边界命中位置与颜色来源会因为布局失配而更难解释。
- B) color 仍沿用旧尺寸,只在 resolve 后做补偿
  - 问题: 等于又引入一次额外重采样,而且仍无法保证和 depth 命中严格对齐。

**Why:**

- external hit 最终由“距离 + 颜色”共同组成。
- capture layout 不一致会制造新的伪影和新的解释成本。

### 5) 默认行为保持不变,通过 Inspector / 文档明确推荐 supersampling

**Decision:**

- 默认 `Auto + scale=1` 的行为保持现状,不隐式提高开销。
- Inspector 文案要明确说明:
  - external capture 的台阶感来自离屏 depth / color capture 的像素离散性
  - 如果看到明显 stair-stepping,优先尝试 `Scale > 1`
- README / CHANGELOG / OpenSpec capability 统一使用相同口径,避免 UI 和文档各说各话。

**Alternatives considered:**

- A) 靠用户自己猜应该调哪个参数
  - 问题: 现有字段虽然已经存在,但缺少明确的“为什么调它”解释。
- B) 默认直接把 scale 提高到 1.5 或 2
  - 问题: 会把更多显存和带宽成本悄悄施加给所有场景。

**Why:**

- 这个 change 的一个核心目标,就是把“已有参数”变成“真正可理解、可操作的质量开关”。

## Risks / Trade-offs

- [风险] supersampling 会直接提高 external capture RT 的显存、带宽和渲染成本
  - 缓解: 保持默认 `Auto + scale=1`,只在用户显式提高 scale 时承担额外开销
- [风险] supersampling 只能减轻 texel 台阶,不能彻底消除规则 LiDAR 栅格自身的 moire
  - 缓解: 在文档里明确边界,避免把它宣传成“一键消除所有波纹”
- [风险] 如果 `Scale` 的舍入、clamp 与 UI 展示不一致,用户会很难理解最终实际尺寸
  - 缓解: 在 design/spec/tests 中统一写死尺寸推导与硬件上限规则
- [风险] 如果后续实现偷偷改成 bilinear 或 blur,会在看起来更顺滑的同时破坏 nearest-surface 语义
  - 缓解: 在 spec 中明确 supersampling 不得改变 nearest-hit 契约,并增加相应测试
- [取舍] 方案1优先保守,不处理更高级的 depth 过渡
  - 缓解: 把 edge-aware resolve 明确保留为后续可独立演进的 change

## Migration Plan

- 旧场景默认保持现有 external capture 行为:
  - `Auto` 仍然使用当前基准尺寸
  - `Scale=1` 仍然等价于“不额外 supersample”
- 用户若要降低台阶感,优先把 `LidarExternalCaptureResolutionMode` 切到 `Scale`,并逐步提高 `LidarExternalCaptureResolutionScale`。
- 若 supersampling 带来不可接受的性能或显存开销,可以直接回退到:
  - `Scale=1`
  - 或 `Auto`
  - 或使用 `Explicit` 指定更保守的固定尺寸
- 本 change 不引入破坏性数据迁移,也不改变现有 serialized 字段名。

## Open Questions

- 暂无阻塞本 change 的开放问题。
- 后续若要继续改善 external depth 的边界平滑,可以单独评估:
  - edge-aware resolve
  - hit neighborhood voting
  - 面向 silhouette 的保边重建
