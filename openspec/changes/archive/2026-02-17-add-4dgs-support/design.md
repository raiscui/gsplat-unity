## Context

本仓库的 3DGS 渲染流程是:
- Editor 导入 PLY,生成 `GsplatAsset`(positions/scales/rotations/colors/SHs).
- Runtime 把数据上传为 GPU `GraphicsBuffer`.
- 每帧对每个相机做两步:
  1) Compute shader 计算深度 key 并做 GPU radix sort,得到 `OrderBuffer`.
  2) 使用自定义 shader 取排序后的 splat id,计算屏幕空间椭圆高斯 footprint,做透明混合.

约束与现实因素:
- GPU 排序依赖 wave/subgroup,需要 D3D12/Metal/Vulkan.
- HDRP 只能跑线性色彩空间,而 3DGS 数据常见是 gamma 训练.
  - 因此本次设计默认不改变色彩空间规则,只是保证 4D 行为正确.
- 目标输入包含两类:
  - canonical 4D PLY: 每个高斯携带 `pos0`,`vel`,`time0`,`duration`
  - `.splat4d` 二进制: 基于 `SplatVFX` 的 `.splat` 思路扩展,用于更快导入与更顺滑的 VFX 工作流
  - 其中 `time0/duration` 归一化到 `[0,1]`.
  - 运动模型: `pos(t) = pos0 + vel * (t - time0)`.
  - 可见性: `t in [time0, time0 + duration]`.
- 性能与规模: 用户预期可能 >10M splats,且目标显存 16-24GB.

## Goals / Non-Goals

**Goals:**
- 在不破坏现有 3DGS 的前提下,扩展到 4DGS(位置/速度/时间/持续时间).
- 在同一帧内,排序与渲染对同一 `t` 保持一致,保证透明混合正确.
- 提供可脚本/Timeline 驱动的 `TimeNormalized` 控制接口.
- 提供一个按 `SplatVFX` 风格改进的 VFX Graph 工作流后端,用于更方便地驱动与对照渲染.
- 在大规模数据下给出明确的资源预算告警,并支持可配置的自动降级策略.

**Non-Goals:**
- 不实现训练/优化流程,仅实现 Unity 侧的导入、播放与渲染.
- 不实现时间高斯核(temporal gaussian)或更复杂的时间调制函数.
- 一期不把 VFX Graph 后端作为超大规模渲染主路径.
  - 系统会设置明确的硬上限,并在超过上限时强制回退到 Gsplat 主后端.
- 不在第一阶段实现完整的流式加载/分块 LOD/FP16 全量压缩(这些属于后续优化阶段).

## Decisions

### 1) 资产模型: 扩展 `GsplatAsset`,而不是新增新资产类型
**选择:** 在 `GsplatAsset` 增加可选字段 `Velocities/Times/Durations`.
当 PLY 缺失 4D 字段时,用安全默认值表现为静态.

**理由:**
- 兼容旧 PLY 与旧场景,避免用户迁移成本.
- Runtime/GPU buffer 管线可以按 "字段是否存在" 分支创建,实现影响面可控.

**备选:**
- 新增 `Gsplat4DAsset`: 结构更干净,但会导致 Editor/Runtime 分叉,并引入大量重复代码.

### 2) 导入格式: PLY + `.splat4d` 双入口
**选择:**
- 扩展现有 PLY importer,支持 `vx/vy/vz,time,duration` 以及若干别名映射.
- 同时引入 `.splat4d` 二进制格式与对应 importer(参考 `SplatVFX` 的 `.splat` 设计),用于更快的导入与更顺滑的 VFX Graph 工作流.

`.splat4d` 的一期范围(刻意收敛):
- 固定 record size,以 "无 header + 记录数组" 的方式解析.
- 一期仅承载 SH0(DC) 颜色与 opacity,不承载高阶 SH 系数.
  - 需要高阶 SH 时继续使用 PLY.

**理由:**
- 当前仓库已经以 PLY 为入口,扩展最自然.
- 上游 FreeTimeGS/4DGS 生态也常见 PLY/点云格式.
- `.splat4d` 能显著降低导入解析成本,并更贴近 VFX Graph 的工作流(参考 `SplatVFX` 的 "导入即生成可用 prefab + binder" 思路).

**备选:**
- 仅做 PLY: 导入开销与托管内存峰值较大,不利于大数据与迭代工作流.

### 3) GPU 数据布局: 第一阶段使用 float32,后续再做打包压缩
**选择:** velocity/time/duration 使用 `float3/float/float` 的结构化 buffer.

**理由:**
- 先保证正确性与可调试性.
- 便于和现有 shader/compute 逻辑对齐.

**备选:**
- FP16/打包压缩: 对 >10M + SH 很关键,但会显著增加实现与验证成本.
  - 作为后续优化任务进入 `4dgs-resource-budgeting` 的延伸任务列表.

### 4) 时间参数传播: 每个 renderer 维护自己的 `TimeNormalized`
**选择:** `TimeNormalized` 作为 per-renderer 状态.
- 排序 pass: 在 `DispatchSort` 为每个 gsplat 设置 `_TimeNormalized`.
- 渲染 pass: 通过 `MaterialPropertyBlock` 为该对象设置 `_TimeNormalized`.

**理由:**
- 支持同一场景中多个 4D 资产以不同时间播放.
- 不依赖全局 shader 变量,更符合组件化使用方式.

**备选:**
- 全局时间: 实现更简单,但不支持多序列,后续扩展会更痛苦.

### 5) 可见性: shader 为最终裁决,compute 仅做排序友好处理
**选择:**
- shader 顶点阶段 MUST 基于时间窗裁剪(不可见直接输出 discardVec).
- compute 的 `CalcDistance` 可以对不可见 splat 写入一个极端 key,把它们推到排序序列的尾部.

**理由:**
- correctness 必须由渲染路径保证,避免因排序分支导致 "可见但没画出来".
- compute 的处理是优化与稳定性手段,不是功能正确性的唯一来源.

**备选:**
- 仅在 compute 裁剪: 需要额外 compact/stream compaction,复杂且可能影响现有排序实现.

### 6) Bounds: 使用保守 padding,避免相机剔除错误
**选择:** 在导入期统计 `maxSpeed` 与 `maxDuration`,运行时按 `padding = maxSpeed * maxDuration` 扩展 bounds.

**理由:**
- 运行时扫描全量 splat 计算 bounds 代价过高.
- 保守 bounds 会增加透明队列负担,但能保证不漏渲染.

**备选:**
- 分块 bounds/层级 bounds: 更精确,但属于后续优化阶段.

### 7) VFX Graph 后端: 按 `SplatVFX` 风格改进工作流交付
**选择:**
- 通过 asmdef 的 version define 或编译宏隔离 `UnityEngine.VFX` 依赖.
- 提供 `GsplatVfxBinder`(参考 `SplatVFX` 的 `VFXSplatDataBinder`),把现有 GPU buffers 绑定到 `VisualEffect`.
- 优先让 `.splat4d` 导入产物可以"一键"驱动 VFX:
  - 导入器在 VFX Graph 可用时,可生成一个带 `VisualEffect + binder` 的 prefab(类似 `SplatVFX` 的导入体验).
  - VFX Graph 内用粒子索引采样 buffers,并在 output 侧复刻 gaussian 投影与混合(一期优先 SH0,高阶 SH 后续再补).
- 默认设置 `MaxSplatsForVfx`,超过上限则禁用 VFX 后端并输出清晰告警,引导用户回退到 Gsplat 主后端.

**理由:**
- 不让 VFX Graph 成为硬依赖,避免破坏现有用户.
- `SplatVFX` 证明了 "GraphicsBuffer + binder + VFX Graph" 是一条可用的工作流路线.
- 但对超大规模,强行 VFX 化风险仍然很高,因此必须硬限制并提供回退.

**备选:**
- 完全改为 VFX Graph: 会放大排序/透明混合/一致性风险,不符合 "尽量接近现有质量" 的目标.

### 8) 资源预算与降级: 先做估算与告警,再做可配置降级
**选择:**
- 在创建 buffer 前做显存估算,按阈值输出 warning.
- 提供可配置策略:
  - 降低 SH 渲染阶数(例如强制 DC/SH0)
  - 限制最大 splat 数

**理由:**
- 对 16-24GB VRAM,>10M + SH3 很可能不可行.
- 需要让失败是可解释的,并提供可执行的恢复路径.

## Risks / Trade-offs

- [Risk] >10M + SH3 显存爆炸,创建 buffer 直接失败 → Mitigation: 显存估算 + 告警 + 自动降级策略.
- [Risk] PLY 导入产生巨大的托管数组,导致导入慢/内存峰值 → Mitigation: 第一阶段先告警,第二阶段规划分块/流式导入.
- [Risk] VFX Graph 与 Gsplat 的透明排序存在细微差异 → Mitigation: 明确 VFX 后端规模上限,并提供对照测试场景.
- [Risk] Bounds 过保守导致透明排序/剔除效率下降 → Mitigation: 后续优化为分块 bounds 或基于时间段的 bounds.

## Migration Plan

- 本次为新增能力,对旧 PLY 与旧场景默认保持兼容.
- 用户若提供 4D 字段,即可启用动态渲染.
- VFX Graph 后端为可选启用项,不启用时不影响项目依赖.

## Open Questions

- 4D PLY 的字段命名是否需要进一步兼容更多上游导出器(除 vx/vy/vz,time,duration 外).
- `.splat4d` 的最终 record layout 是否需要预留版本字段或 attribute flags(以便未来可选承载高阶 SH/压缩数据).
- 默认的 `MaxSplatsForVfx` 取值是多少更合理(例如 200k,500k,1M).
- 自动降级策略的默认值与优先级如何设计,才能既安全又不让用户困惑.
- 是否需要引入 FP16/打包压缩作为第一阶段的强制项(取决于目标数据的真实规模与 VRAM).
