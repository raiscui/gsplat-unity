## Context

当前包的 4DGS 数据入口主要是 `.ply` 和 `.splat4d`.

- `.ply` 是训练输出与通用交换格式.
  - 优点是信息完整(可含高阶 SH),生态兼容.
  - 缺点是体积大,导入耗时,且序列化为 Unity 资产时会有明显内存峰值.
- `.splat4d` 是本包为 4DGS 工作流引入的二进制格式.
  - 它的 4D 语义是"线性 velocity + 时间窗(time/duration)".
  - 离线 keyframe 导出也只是把相邻帧 position 差分成 velocity.
    - scale/rotation/opacity/SH 等仍然是分段阶梯值.

你已经确认要走路线 B: 真逐帧 keyframe,并且 position/scale/rotation/opacity/SH 全属性都要可插值.
同时你还要支持两种 time mapping:

- uniform: `t_i = i/(frameCount-1)`
- explicit: `frameTimesNormalized[]`

因此我们需要一个新的"序列 4DGS 格式",其目标更偏向:

- 分发友好(单文件,易缓存,易版本管理).
- 导入友好(不需要把巨大 PLY 序列完整展开成 float 再进 Unity).
- 运行时可扩展(未来可把压缩优势延伸到运行时,而不是只停留在磁盘).

本 change 选择对标 PlayCanvas SOG 的总体思路:

- 以 `meta.json` 描述 schema 与文件引用.
- 以"数据图"(例如 lossless WebP)承载量化后的属性流.
- 允许 bundle 打包成单文件 ZIP.

## Goals / Non-Goals

**Goals:**

- 定义 `.sog4d`(ZIP bundle)的最小可落地实现路径,并与本包现有渲染/排序链路对齐.
- 在 Unity 中导入 `.sog4d`,产出一个可播放的序列资产,并能被运行时组件驱动.
- 支持两种 time mapping(uniform + explicit),并严格定义 `TimeNormalized -> (i0,i1,a)` 的评估方式.
- 支持 position/scale/rotation/opacity/SH 的逐帧插值,并保证"同一帧内排序与渲染"使用同一套评估结果.
- 在资源预算上给出可行动的 warning 与降级策略(例如降低 SH,关闭插值).

**Non-Goals:**

- 不替换现有 `.ply` / `.splat4d` 工作流,不做 breaking changes.
- 不在本 change 内实现完整的 streaming LOD/分块在线加载体系(可在后续 change 做).
- 不支持每帧 splatCount 变化的拓扑动画(本 change 明确要求 splat identity 稳定).

## Decisions

### 1) Unity 侧资产模型: 新增序列资产类型,而不是扩展 `GsplatAsset`

**决定:**

- 为 `.sog4d` 新增一个 ScriptableObject(暂定 `GsplatSequenceAsset`),用于承载:
  - `SplatCount`, `FrameCount`, `SHBands`, `TimeMapping`
  - per-frame streams 的 GPU-ready 数据(见后续决策)
  - bounds(UnionBounds,可选 PerFrameBounds)

**理由:**

- `GsplatAsset` 当前语义是"单帧静态 splats(+可选 4D velocity/time/duration)".
- 逐帧 keyframe 序列需要 `frameCount` 维度,并且在运行时需要访问相邻两帧.
- 直接把所有帧展开塞进 `GsplatAsset`(例如 `Positions` 长度变成 `frameCount*splatCount`)会导致:
  - CPU 内存爆炸.
  - 上传开销与生命周期管理复杂化.
  - public API 语义变模糊.

**替代方案:**

- 扩展 `GsplatAsset` 增加 frame 维度字段: 拒绝,原因如上.
- 导入为多个 `GsplatAsset`(每帧一个): 拒绝.
  - 无法自然表达插值与 time mapping.
  - 资产碎片化严重,也不满足"单文件分发"的目标.

### 2) 运行时评估路径: "解码+插值"先落到 float buffers,复用现有排序与渲染

**决定:**

- 在运行时增加一个"序列评估阶段",把 `(i0,i1,a)` 评估后的属性写入现有的 float `GraphicsBuffer`:
  - `PositionBuffer`, `ScaleBuffer`, `RotationBuffer`, `ColorBuffer`, `SHBuffer`
- 后续排序与渲染继续复用现有链路:
  - `GsplatSorter` 读取 `PositionBuffer` 排序.
  - `Gsplat.shader` 从各属性 buffer 渲染.

**理由:**

- 这能最大化复用当前已稳定的 compute-sort + shader 渲染架构.
- "排序与渲染一致"可以通过一次性写入评估结果来天然保证.
- 未来要优化压缩/解码路径时,只需要替换"序列评估阶段",而不是推翻整个渲染系统.

**替代方案:**

- 在 shader 中直接从 per-frame quantized textures 采样并插值: 风险高.
  - 需要重写渲染 shader 的数据访问模式,与现有 structured buffer 体系冲突较大.
- CPU 解码+插值,每帧上传 float buffers: 可以作为最小兜底,但不作为主路线.
  - 对大 splatCount 场景,带宽与 CPU 开销不可控.

### 3) per-frame streams 的 GPU 侧形态: `Texture2DArray` 优先,必要时 chunk

**决定:**

- 导入器输出 per-frame streams 为 `Texture2DArray`(layer=frameIndex).
- 当 `FrameCount` 很大导致单个 array 不可行时,按 chunk 输出多个 `Texture2DArray` 并记录映射.

**理由:**

- `Texture2DArray` 在 compute shader 中访问方便,也便于"同一 dispatch 读两帧".
- chunk 是为了把"帧数"与"单个 GPU 资源大小"解耦,避免单资源过大导致导入失败或运行时 OOM.

**替代方案:**

- 每帧单独一个 `Texture2D`: 拒绝.
  - 资产碎片化严重,导入与引用管理成本高.
- 全部帧展开为 float `GraphicsBuffer`: 拒绝.
  - 直接失去压缩带来的带宽优势,也会极大放大显存占用.

### 4) 导入期 vs 运行时解码边界: 先跑通 MVP,再升级到"运行时也保持压缩优势"

**决定:**

- 分两条实现路径,并在 tasks 中明确优先级:
  - MVP(B): 导入期解包 ZIP 并解码 WebP,产出 Unity `Texture2DArray` 子资产.
    - 运行时只做 GPU-side 解码+插值到 float buffers.
    - 优点: Player 不需要 zip/WebP 解码依赖,工程落地更稳.
    - 缺点: build 内资产可能膨胀(取决于 Unity 对这些 data textures 的序列化方式).
  - Target(A): 保持 `.sog4d` 在构建产物中仍为压缩 bundle,运行时加载并解码.
    - 优点: 真正把"分发压缩优势"带到最终产物.
    - 风险: 引入跨平台 zip/WebP runtime 依赖,并需要更严格的性能/内存治理.

**理由:**

- 你要的是"逐帧可插值"与"两种 time mapping"的正确语义,先用 MVP 跑通可大幅降低讨论成本.
- 压缩边界与依赖选型是系统性问题,需要在有可工作的 playback 原型后再做更精准的取舍.

### 5) `TimeNormalized` 评估放在 CPU,并缓存到 "this frame"

**决定:**

- 在组件 Update 中把 `TimeNormalized` 处理为 `m_timeNormalizedThisFrame`.
- CPU 侧根据 `timeMapping` 计算 `(i0,i1,a)`:
  - uniform: O(1)
  - explicit: 二分查找 O(log frameCount)
- 把 `(i0,i1,a)` 作为参数传入解码/插值 compute.

**理由:**

- 显式时间轴的查找更适合 CPU.
- 复用现有 `GsplatRenderer` 的"同一帧缓存 time"模式,更容易保证 sort/render/sequence-eval 一致.

### 6) SH 的落地形态需要收敛,并优先选择性能更好的承载方式

**决定:**

- 系统 MUST 不把 SH palette 塞进 JSON.
- 系统 MUST 采用以下承载方式(与 `sog4d-sequence-encoding` 一致):
  - `sh0.webp`(RGBA8,per-frame):
    - RGB 为 DC 系数(`f_dc`)的 codebook 索引
    - A 为 opacity(u8)
  - 当 `bands>0` 时:
    - `shN_labels.webp`(per-frame): u16 labels(小端)
    - `shN_centroids.bin`(global): palette,默认 `float16` little-endian(允许 `float32`)

**理由:**

- 这是性能优先的选择:
  - `labels.webp` 仍保持 SOG 风格的 u16 label,便于互操作.
  - `centroids.bin` 避免了“读取一个 palette entry 需要大量纹理采样”的模式,更适合 compute 解码吞吐.
- 同时也对齐了 SOG v2 的关键约定:
  - opacity 位于 `sh0.webp` alpha
  - u16 label 的打包端序为 little-endian

#### 6.1) `sh0Codebook` 的生成策略(建议,Exporter 侧)

这里的 `sh0Codebook` 是一个 1D 的量化表.
它把 8-bit 的索引(byte)映射回 DC 系数 `f_dc` 的 float 值.

建议的生成流程:

- 训练数据: 收集序列里所有帧的 `f_dc`(三个通道)样本.
  - 数据量太大时,允许随机采样(例如每帧采样 N 个 splat).
- 重要性权重(可选,但强烈建议):
  - 用 `importance = opacity * volume` 作为样本权重.
  - `volume` 可以用 `scale.x * scale.y * scale.z` 的近似(或 clamp 后的近似).
  - 目的: 让 codebook 把更多分辨率留给"更可见"的 splats,避免被大量透明/极小 splats 拉偏.
- 拟合方法(二选一):
  - mini-batch k-means(1D,K=256): 误差更小.
  - quantile/等频分箱(256 桶): 实现简单,且对长尾更稳.
- 输出规范:
  - 输出 `sh0Codebook[256]`(float32)写入 `meta.json`.
  - 允许按值排序(从小到大),但排序后需要同步重映射索引.

#### 6.2) `shNCount`/`shN_centroids.bin` 的生成策略(建议,Exporter 侧)

当 `bands>0` 时,我们需要把高阶 SH(rest coefficients)从 float 域压到 "palette + labels".

- `shNCount` 的含义:
  - palette entry 的数量(也就是 centroid 的数量).
  - 每个 entry 存一整套 rest 系数向量.
    - `bands=3` 时,`restCoeffCount=(3+1)^2-1=15`.
    - 因此每个 entry 是 `15 * float3(RGB)` 的系数块.

建议的生成流程:

- 训练数据:
  - 对每个 `(frameIndex,splatId)` 取出该 splat 的 rest 系数向量,拼成一个高维向量进行聚类.
- 重要性权重(强烈建议):
  - 同 `sh0Codebook`,用 `opacity * volume` 作为权重.
  - 目的: 把 palette 的容量分配给"画面更重要"的系数组合.
- 拟合方法:
  - 推荐使用 "EMA Vector Quantization"(DualGS/DynGsplat 的做法)或 mini-batch k-means.
  - 直觉上,这是一个典型的"数据量巨大,但离线可训练"问题.
- 输出规范:
  - centroids 写入 `shN_centroids.bin`,默认 `float16` little-endian.
  - labels 为每帧一张 `shN_labels.webp`(或 delta,见下节),每像素一个 u16 label.

#### 6.3) `shNLabelsEncoding`: `"full"` vs `"delta-v1"`

`shNLabelsEncoding` 的目标是把 "逐帧 labels" 这个最容易膨胀的数据项压下去.
它不改变渲染语义,只改变存储形态.

- `"full"`(格式兼容默认):
  - 每帧都有一张 `shN_labels.webp`.
  - 优点: 随机访问极好,导入实现也最直观.
  - 缺点: 当 `frameCount*splatCount` 很大时,labels 会成为主要体积来源.
- `"delta-v1"`(推荐默认,受 DualGS 启发):
  - 把时间轴切成多个 segment.
  - 每个 segment:
    - 首帧存完整 labels(`baseLabelsPath`)
    - 后续帧只存"相对前一帧改变的位置与新 label"(`deltaPath`)
  - 优点: 对"相邻帧只有少量 label 变化"的序列,压缩率会非常显著.
  - 缺点: 存储层不再天然支持 O(1) 随机跳帧.
    - 但对 Unity 来说,我们推荐在 importer 导入期把 delta 还原成 per-frame labels 纹理.
    - 这样运行时仍保持随机访问与双帧采样的简单性.

#### 6.4) 推荐默认值(性能优先,并可被 exporter 参数覆盖)

这里的 "默认值" 主要影响的是文件大小与画质,对运行时解码吞吐影响较小.
(运行时主要成本来自 "每帧写入 float buffers",而不是 palette 的大小.)

你的偏好是:
- 默认性能优先.
- 若性能无明显差异,则质量优先.
- 默认启用 delta.

因此我建议把 exporter 的默认值定为(并允许参数覆盖):

- `streams.sh.sh0Codebook`: 固定 256(按 spec).
- `streams.sh.shNCentroidsType`: 默认 `"f16"`(按 spec 允许 `"f32"`).
- `streams.sh.shNLabelsEncoding`: 默认 `"delta-v1"`(但会显式写入字段,不依赖 spec 的缺省值).
- `streams.sh.shNCount`: 默认 8192.
  - 解释: 对 `bands=3` 时,`shN_centroids.bin` 的体积大约是:
    - `8192 * 15 * 3 * 2 bytes ≈ 0.7 MB`(float16)
  - 这相比 per-frame 的 position/rotation/sh0/labels 纹理通常是小头.
  - 因此更大的 palette 一般更倾向于"画质提升",而不是"明显拖慢运行时".
  - 真实是否存在 cache/局部性差异,需要在实现阶段用基准测试确认.
- segment 长度: 默认 50 帧/segment(对齐 DualGS 论文叙述),并暴露为 exporter 参数.
  - 对 Unity 的推荐落地是: importer 导入期把 delta 展开为 per-frame labels 纹理.
  - 这样运行时仍保持 O(1) 的随机访问与双帧采样逻辑.

## Risks / Trade-offs

- [Risk] per-frame 解码+插值 compute 成本随 `splatCount` 线性增长,可能成为帧时间瓶颈.
  - Mitigation: 提供 AutoDegrade 选项(关闭插值/降低 SH),并允许限制 splatCount.
- [Risk] Unity 对 data textures 的导入/序列化可能隐式改写 byte(例如 sRGB,压缩,mipmap).
  - Mitigation: importer 强制关闭 sRGB/压缩/mipmap,并做字节一致性校验(至少抽样校验).
- [Risk] 显存峰值: 同时常驻多帧量化纹理 + 解码后的 float buffers.
  - Mitigation: chunk,以及在资源预算中提前估算与告警.
- [Risk] 显式时间轴存在重复时间点,导致 `t[i1]-t[i0]==0` 产生 NaN.
  - Mitigation: spec 已要求实现定义 `a=0.0`,实现中也必须覆盖该分支.

## Migration Plan

- 新增 `.sog4d` 的 importer 与运行时组件,不影响现有 `.ply` / `.splat4d`.
- 默认保持现有 `GsplatRenderer` 行为不变.
- `.sog4d` 使用单独的资产/组件路径,以避免把 3D-only 工作流复杂化.

## Open Questions

1. `.sog4d` 的 exporter 工具链选型:
   - Python 侧是否直接写 WebP(lossless),还是先写 PNG 再由外部工具转 WebP.
2. 导入期展开 delta 的缓存策略:
   - 我们是否需要在 importer 里同时保留 delta 原始数据,用于调试与二次导出?
