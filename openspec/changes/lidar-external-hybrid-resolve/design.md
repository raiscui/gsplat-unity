## Context

方案1 `lidar-external-capture-supersampling` 已经把 frustum external GPU capture 的质量路径收敛到“提高 capture fidelity,但继续保持 point texel read + nearest-surface 语义”。这条路线能显著减轻 depth 台阶,但它仍然保留了一个核心限制: `Gsplat.compute` 中的 external resolve 还是围绕“中心 uv 对应的单 texel 决策”展开。

当 external target 的 silhouette 很细、斜面很多,或者 capture texel 与 LiDAR ray 的相位关系不理想时,单 texel 决策会继续留下两类问题:

- 中心 uv 刚好踩在边界附近时,会看到边缘阶梯和断续感。
- 即使 capture 分辨率更高,中心 uv 仍可能落在不理想 texel 上,表现成局部空隙或细缝。

这次 change 的目标不是把 external depth 做成连续几何重建,也不是重新定义 external hit 语义。相反,它要在保住方案1边界的前提下,给 external resolve 增加更稳的“候选选择”能力:

- `edge-aware nearest resolve`
- `subpixel jitter resolve`

当前实现约束也很明确:

- 输入主要来自 external depth 与 surfaceColor capture,没有稳定可依赖的 normal / object id / semantic mask。
- external hit 最终仍要与 gsplat hit 做逐 cell 最近距离竞争。
- static / dynamic external capture 共享同一套 sensor frame 和 resolve 语义,不能分叉成两条不同真相源。

## Goals / Non-Goals

**Goals:**

- 为 frustum external GPU capture 引入两条可独立开关、也可组合使用的 hybrid resolve 路线。
- 在不破坏 nearest-surface / nearest-hit 语义的前提下,改善 external silhouette 的阶梯、断续感和局部空隙。
- 用清晰的公开 API 表达质量档位,让 Inspector、文档和测试都能稳定描述四种组合状态。
- 明确两条 resolve 路线同时开启时的固定执行顺序、回退语义和颜色绑定规则。
- 保持默认行为与方案1一致,避免对现有场景引入隐式质量或性能变化。

**Non-Goals:**

- 不在本 change 中引入普通 blur、naive bilinear depth mixing 或跨表面插值。
- 不做 plane fitting、法线引导重建或新的几何求交模型。
- 不把 temporal random jitter 引入 external resolve。
- 不要求用户一开始就面对大量 threshold slider 或细碎高级调参项。
- 不改变 external hit 与 gsplat hit 的最终最近距离竞争契约。

## Decisions

### 1) 公开 API 使用两个独立 mode,默认都为 `Off`

**Decision:**

- 系统公开两个独立模式字段:
  - `LidarExternalEdgeAwareResolveMode`
    - `Off`
    - `Kernel2x2`
    - `Kernel3x3`
  - `LidarExternalSubpixelResolveMode`
    - `Off`
    - `Quad4`
- 默认值为 `Off + Off`,保持方案1行为不变。
- 这两个 mode 同时暴露在 `GsplatRenderer` 与 `GsplatSequenceRenderer` 上。

**Alternatives considered:**

- A) 用多个独立 bool 拼组合
  - 问题: Inspector 语义分散,文档和测试都不易表达 `2x2` 与 `3x3` 的质量差异。
- B) 用一个“大而全”的质量枚举同时表达两条路径
  - 问题: 会把“edge-aware”和“subpixel”两条职责耦死,不利于独立开关和后续演进。
- C) 直接公开大量底层参数
  - 问题: 当前仍在第一轮 capability 定义阶段,过早公开细节阈值只会增加噪声。

**Why:**

- 两个独立 mode 能最清楚地表达四种组合状态。
- `Off` 作为显式值,天然让默认兼容与回归验证更简单。

### 2) hybrid resolve 的执行顺序固定为“先 subpixel,后 edge-aware,最后统一选 winner”

**Decision:**

- 当两条路径都开启时,执行顺序固定为:
  1. 先生成 subpixel candidate uv
  2. 再对每个 candidate 执行 edge-aware neighborhood resolve
  3. 最后在所有 candidate 结果中统一选择最近且可信的 winner
- 不允许先对中心 uv 做 edge-aware,再把结果拿去派生 subpixel candidate。

**Alternatives considered:**

- A) 先 edge-aware 再 subpixel
  - 问题: 这样本质上仍是围绕中心 uv 展开,会损失 subpixel 扩充候选的价值。
- B) 两条路径完全并行,最后再随意合并
  - 问题: 难以定义稳定的 winner 规则,测试也不容易锁定。

**Why:**

- subpixel resolve 的职责是“增加候选”,edge-aware resolve 的职责是“保边选样”。
- 先扩候选、再做保边过滤,最符合这次 change 的目标边界。

### 3) edge-aware nearest resolve 只做保边选样,不过表面平均

**Decision:**

- `Kernel2x2` 与 `Kernel3x3` 都以 candidate uv 为中心读取小邻域 depth。
- 邻域候选必须先经过深度差 / ray-distance 一致性过滤,只保留与当前主样本深度足够接近的 texel。
- 过滤后的候选中,选择最近且可信的样本作为该 candidate 的 resolved result。
- 如果过滤后没有可信候选,必须回退到该 candidate 的中心 point sample。

**Alternatives considered:**

- A) 直接在邻域里做深度平均
  - 问题: 会把前后表面混成中间层,破坏 nearest-surface 语义。
- B) 无条件选择邻域最小深度
  - 问题: 容易跨 silhouette 串到前景边缘,造成错误“吸边”。
- C) 上 object id / normal 引导
  - 问题: 当前 external capture 没有稳定提供这些额外缓冲,会扩大 change 范围。

**Why:**

- 这次 design 的本质不是“把边缘抹平”,而是“在保边前提下找更合适的 nearest sample”。

### 4) subpixel resolve 使用 deterministic `Quad4`,不使用随机 jitter

**Decision:**

- `Quad4` 为固定的 4 个亚像素偏移模式。
- 这些 offset 相对当前 uv 的定义必须稳定、可复现,不能依赖 frame-random 或 temporal noise。
- 当 subpixel resolve 关闭时,系统只评估中心 uv。

**Alternatives considered:**

- A) 使用 frame-random jitter
  - 问题: temporal stability 差,现场会闪,测试也难锁定。
- B) 使用更大的固定 pattern,如 8 点或 9 点
  - 问题: 当前先做第一版 capability,4 点更容易控制成本与收益。
- C) 完全不做 subpixel candidate,只靠 edge-aware
  - 问题: 中心 uv 一旦踩偏,即使有 edge-aware 也可能没有好候选可选。

**Why:**

- `Quad4` 已经足以打散“只看中心 uv”的局限,同时仍便于理解、验证和控制成本。

### 5) final winner 统一按 nearest-hit 规则选择,且 color 跟随 depth winner

**Decision:**

- 不论是 edge-only、subpixel-only,还是两者都开启,最终 external hit 都必须通过统一 winner 选择阶段确定。
- winner 选择仍遵循 nearest-hit 语义。
- surfaceColor 必须跟随最终 depth winner 的同一候选结果,不能独立 average,也不能从另一条候选路径单独补采。

**Alternatives considered:**

- A) color 单独做 bilinear 或平均
  - 问题: 会让“距离命中”和“颜色命中”来自不同样本,语义断裂。
- B) 先选最近 depth,再重新对 color 做单独重采样
  - 问题: 同样会制造命中与颜色脱钩。

**Why:**

- external hit 不是两套独立结果,而是一条命中记录。
- depth 和 color 必须来自同一个 resolved winner。

### 6) 阈值先保持为受控实现细节,避免第一版 API 过度外露

**Decision:**

- edge-aware 的深度一致性阈值、candidate 比较容差等,第一版先作为受控实现细节或少量内部常量管理。
- OpenSpec 只要求这些阈值必须让 `Kernel2x2` / `Kernel3x3` 具备保边过滤与中心回退能力,不要求第一版就公开成一组通用 slider。

**Alternatives considered:**

- A) 立即公开完整阈值矩阵
  - 问题: 参数一旦公开就是长期 API 负担,当前证据还不足以确定最稳的外露形式。
- B) 完全不定义阈值行为
  - 问题: 会让实现者随意发挥,难以保证“保边而非混样”的结果。

**Why:**

- 先把能力边界写清楚,再根据实现验证决定是否值得开放更细调参,风险更低。

## Risks / Trade-offs

- [风险] 阈值太松会跨 silhouette 串边
  - 缓解: 要求 edge-aware 先做一致性过滤,并通过 spec/test 锁定“不得跨表面平均”的语义。
- [风险] 阈值太紧会频繁退化回中心 sample
  - 缓解: 明确中心回退是安全退路; 即使收益不足,也不应制造新的错误空洞。
- [风险] `Kernel3x3 + Quad4` 的成本明显高于方案1
  - 缓解: 默认保持 `Off + Off`,并在 Inspector / 文档中明确质量档位与成本关系。
- [风险] 如果 subpixel pattern 引入随机性,会造成 temporal instability
  - 缓解: 第一版只允许 deterministic `Quad4`。
- [风险] 如果 color 不跟随最终 depth winner,会出现距离和颜色脱钩
  - 缓解: 把“color follows winner”写入 spec 与测试。
- [取舍] 这次 design 优先定义可验证的第一版 resolve 能力,不追求一步到位的最强重建效果
  - 缓解: 后续若仍需更强质量,可以在此基础上继续做更高级的 resolve change。

## Migration Plan

- 默认配置保持 `LidarExternalEdgeAwareResolveMode = Off`、`LidarExternalSubpixelResolveMode = Off`。
- 旧场景在不改配置时,必须继续保持方案1的 point texel read 行为。
- 若现场需要更稳的 silhouette:
  - 可先开启 `Kernel2x2`
  - 再视效果与成本决定是否叠加 `Quad4`
  - `Kernel3x3 + Quad4` 作为更高成本档位保留
- 如果 hybrid resolve 带来不可接受的性能成本或质量副作用,可以直接回退到 `Off + Off`,无需数据迁移。

## Open Questions

- 第一版是否需要公开 edge-aware 阈值参数,当前不阻塞实现,但需要在落地验证后再决定。
- `Kernel2x2` 是否足以覆盖大多数场景,还是需要在文档里直接把 `Kernel3x3` 描述为推荐高质量档位,当前也不阻塞 artifact 完成。
