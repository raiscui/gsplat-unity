## Context

当前 RadarScan 粒子渲染主链已经稳定:

- `Runtime/Lidar/GsplatLidarScan.cs` 在 vertex 阶段按 `LidarPointRadiusPixels` 扩出屏幕空间点片.
- `Runtime/Shaders/GsplatLidar.shader` 在 fragment 阶段用固定 `kSquareFeather` 做 soft-edge coverage.
- pass 当前保持:
  - `ZWrite On`
  - `Blend SrcAlpha OneMinusSrcAlpha`

这条链路已经能稳定表达 LiDAR 的深度、颜色、show/hide、external hit 与扫描前沿. 但也正因为它是“小方片 + 固定 feather”的本地 shader 覆盖方式,所以当点半径很小,或者背景对比强、扫描头持续移动时,边缘会出现比较明显的锯齿和闪烁.

用户现在要的不是“把 RadarScan 接到全局后处理 AA 上”,而是给这个粒子 pass 本身增加可选抗锯齿模式. 同时项目还有两个关键约束:

- 不能为了加 AA 就改坏现在的 RadarScan 颜色、show/hide 或深度语义.
- 不能把平台/管线不稳定的方案伪装成默认能力.

## Goals / Non-Goals

**Goals:**

- 为 RadarScan 粒子增加明确可选的 AA 模式枚举.
- 让 `GsplatRenderer` 与 `GsplatSequenceRenderer` 暴露一致的 LiDAR 粒子 AA API 与 Inspector 入口.
- 提供一个不依赖 MSAA 的稳定本地 shader AA 路线.
- 提供一个依赖 MSAA 的更高质量可选路线.
- 在 `AlphaToCoverage` 不可用时提供稳定、可预期的 fallback.
- 保持当前 RadarScan 其它视觉语义不变.
- 保持老场景升级后的稳定性,避免无感升级后视觉突然变化.

**Non-Goals:**

- 不把 FXAA / SMAA / TAA 做成这个组件的直接选项.
- 不接管相机、URP/HDRP renderer feature 或后处理栈.
- 不改变 RadarScan 点形状语义.
  - 当前仍然是“屏幕空间方片 + soft edge”,不是顺带改成圆点.
- 不改变 LiDAR external hit、颜色模式、show/hide 或扫描频率逻辑.

## Decisions

### 1) 新增 LiDAR 粒子 AA 模式枚举,并保持两条 runtime API 一致

**Decision:**

- 在 runtime 公共表面新增一个 LiDAR 粒子 AA 模式枚举.
- `GsplatRenderer` 与 `GsplatSequenceRenderer` 都要暴露同名配置字段,保持序列化和 Inspector 体验一致.
- 模式集合固定为:
  - `LegacySoftEdge`
  - `AnalyticCoverage`
  - `AlphaToCoverage`
  - `AnalyticCoveragePlusAlphaToCoverage`

**Alternatives considered:**

- A) 不加枚举,只加一个“开启 AA”布尔值
  - 问题: 无法表达不同质量档和 MSAA 依赖关系.
- B) 让 `GsplatRenderer` 和 `GsplatSequenceRenderer` 各自独立命名
  - 问题: 会造成 API 分裂和 Inspector 心智负担.

**Why:**

- 这是最直观的用户模型.
- 也方便测试与文档把模式语义逐个锁死.

### 2) 兼容优先: 默认保持 `LegacySoftEdge`,推荐值写成 `AnalyticCoverage`

**Decision:**

- 新增字段的兼容默认值使用 `LegacySoftEdge`.
- Inspector 与 README 明确标注 `AnalyticCoverage` 是推荐模式.
- 旧场景如果没有显式切换新模式,应继续保持当前固定 feather 的视觉语义.

**Alternatives considered:**

- A) 直接把默认值切成 `AnalyticCoverage`
  - 问题: 老场景升级后可能出现边缘观感变化,不利于稳定升级.
- B) 直接废弃当前 fixed feather 路线
  - 问题: 无法做对照,也失去最低风险回退路径.

**Why:**

- 这个 change 的目标是“增加能力”,不是偷偷替用户改视觉基线.
- 先保住兼容,再给出推荐值,是风险更低的 rollout 方式.

### 3) `AnalyticCoverage` 作为不依赖 MSAA 的主 AA 路线

**Decision:**

- 在现有 `GsplatLidar.shader` 里引入基于屏幕导数的 coverage AA.
- 核心做法:
  - 保留当前点形状距离场定义.
  - 用 `fwidth(...)` 或等价屏幕导数驱动边缘过渡宽度.
  - 不再只依赖固定 `kSquareFeather`.

**Alternatives considered:**

- A) 继续只调大固定 feather
  - 问题: 大点和小点无法同时兼顾,容易变糊.
- B) 直接改成后处理型 AA
  - 问题: 超出这个组件职责边界,也会牵连 camera/pipeline.

**Why:**

- 这是最贴合当前 LiDAR 粒子 shader 结构的方案.
- 不要求 MSAA,也不依赖外部渲染管线设施.
- 对小点半径的稳定性最好,适合作为推荐模式.

### 4) `AlphaToCoverage` 通过独立 shader shell / material 路线实现

**Decision:**

- `AlphaToCoverage` 不在现有 LiDAR pass 里靠 uniform 动态切换.
- 改为引入 A2C 专用 shader shell 或专用材质资源,由 runtime 在 draw 前选择.
- 该 shell 与当前 LiDAR shader 共享同一份核心 HLSL 逻辑,避免复制两套主体实现.

**Alternatives considered:**

- A) 试图在同一个 pass 里动态切 `AlphaToMask`
  - 问题: 这属于 ShaderLab pass 状态,不是普通 MPB uniform.
- B) 复制整份 `GsplatLidar.shader` 做 A2C 版本
  - 问题: 维护成本高,后续 show/hide 或 external 逻辑容易双份漂移.

**Why:**

- 这是最符合 Unity pass 状态约束,又能控制维护成本的实现路径.

### 5) A2C 相关模式必须有保守的可用性检测与 fallback

**Decision:**

- 当用户选择:
  - `AlphaToCoverage`
  - `AnalyticCoveragePlusAlphaToCoverage`
- 系统必须先判断当前相机/目标是否具备有效 MSAA 条件.
- 若 MSAA 条件不满足:
  - `AlphaToCoverage` 回退到 `AnalyticCoverage`
  - `AnalyticCoveragePlusAlphaToCoverage` 也回退到 `AnalyticCoverage`
- Inspector 应明确提示:
  - A2C 需要 MSAA
  - 未满足时当前只会运行 `AnalyticCoverage`

**Alternatives considered:**

- A) 无条件打开 A2C
  - 问题: 在没有 MSAA 的情况下结果不可预测.
- B) 没有 MSAA 时回退到 `LegacySoftEdge`
  - 问题: 用户明明主动选择了更强 AA,却回退到更旧的路径,体验不直观.
- C) 没有 MSAA 时直接静默失效
  - 问题: 最难排查,用户会觉得“选项没作用”.

**Why:**

- `AnalyticCoverage` 是最稳定的底线.
- 这样既能保住视觉质量,也不会让用户踩到 A2C 的平台坑.

### 6) 明确不把全屏后处理型 AA 纳入本 change

**Decision:**

- 本次不把 FXAA / SMAA / TAA 做成 LiDAR 组件内模式.
- 文档里会明确说明:
  - 这些方案属于相机或渲染管线级能力.
  - 如果项目本身已经有全局 post AA,它可以继续叠加在 RadarScan 之上,但不由本组件负责接线.

**Alternatives considered:**

- A) 顺手把 FXAA / SMAA / TAA 也塞进枚举
  - 问题: 会形成误导,像是这个组件能单独控制整机 post stack.

**Why:**

- 这能把 change 的边界保持清楚.
- 也避免把一个本地 shader 质量问题扩大成跨 camera/pipeline 的大改.

### 7) AA 只影响边缘 coverage,不得改变 RadarScan 的其它语义

**Decision:**

- 无论模式如何切换,都只允许改变粒子边缘 coverage / alpha 行为.
- 以下语义不得被 AA 模式改变:
  - `Depth` / `SplatColorSH0`
  - show/hide
  - glow
  - `external hit vs gsplat hit` 最近命中竞争
  - 扫描前沿与 trail 强度

**Alternatives considered:**

- A) 在 AA 模式里顺便改亮度、点大小或形状
  - 问题: 用户无法判断变化到底来自 AA 还是来自别的视觉逻辑.

**Why:**

- 保持单一职责.
- 测试也更容易做稳定回归.

## Risks / Trade-offs

- [风险] `AnalyticCoverage` 会让边缘更平滑,也可能让极小点看起来略微更软
  - 缓解: 保留 `LegacySoftEdge` 作为稳定对照与回退.
- [风险] A2C 的效果会受 pipeline / target MSAA 条件影响
  - 缓解: 做保守检测,未满足时统一回退到 `AnalyticCoverage`,并在 Inspector 明示.
- [风险] 为了支持 A2C 引入第二个 shader shell,会增加一层资源维护
  - 缓解: 让主体 HLSL 共用,只把 pass 状态差异留在 shell 层.
- [风险] 若把 AA 和点形状、强度等逻辑混在一起,很容易引入观感回归
  - 缓解: 设计上把 AA 明确限制为边缘 coverage 变化.

## Migration Plan

1. 新增 LiDAR 粒子 AA 枚举与 runtime 字段,兼容默认值设为 `LegacySoftEdge`.
2. 新增 Inspector 文案与推荐说明.
3. 在 shader 侧补 `AnalyticCoverage` 路线.
4. 增加 A2C shell / material 与 runtime 选择逻辑.
5. 为无 MSAA 时的 fallback 增加自动化测试与 Inspector 提示.
6. README / CHANGELOG 同步模式说明与适用前提.

回滚策略:

- 若实现出现回归,可把模式切回 `LegacySoftEdge`,立即恢复到当前路径.
- 若 A2C 路线出现平台兼容问题,仍可先只保留 `AnalyticCoverage` 和 `LegacySoftEdge`.

## Open Questions

- 当前没有阻塞性 open question.
- 若后续用户明确要求把 FXAA / SMAA / TAA 也纳入组件选项,应作为独立 change 处理,而不是在本 change 内继续扩 scope.
