## Why

当前 4DGS 数据入口主要是 `.ply` 和 `.splat4d`.
当我们想要承载"真逐帧 keyframe 的 4D 序列"(用户已确认选择 B)时,现有方案会出现明显的分发与导入瓶颈.

- `.ply` 序列文件体积大,字段多(SH/opacity/scale/rot 等),导入耗时和内存峰值都很高.
- 现有 `.splat4d` 更偏"线性运动 + 时间窗"的 4D 表达,并且一期是 SH0-only.
  - 对逐帧 keyframe 来说,如果用 record 重复写入每一帧的属性,文件会膨胀得很快.
- 我们需要一个面向"分发 + 快速导入 + 可扩展属性流"的序列格式.
  - 目标是让"序列 4DGS 资产"能像普通资源一样易于复制/缓存/版本管理.
  - 同时为后续做 GPU-native 的量化解码留下空间(把压缩优势延伸到运行时).

因此,本 change 选择路线B:
参考 PlayCanvas 的 SOG 思路(属性图 + meta.json),并打包成单文件,用于新的"序列 4DGS 格式".

## What Changes

- 新增一个 SOG 风格的"序列 4DGS"文件格式(暂定扩展名 `.sog4d`):
  - 单文件 bundle(例如 zip),内部包含 `meta.json` 与一组属性图(例如 WebP).
  - 重点面向"逐帧 keyframe"的时间维度表达,而不是 velocity/time/duration 的线性拟合.
- 新增 Unity 侧导入与运行时支持:
  - ScriptedImporter 导入 `.sog4d`,生成可播放的资产(并能被现有 `GsplatRenderer` 的播放 API 驱动).
  - 支持按 `TimeNormalized` 选择当前帧(以及可选的帧间插值策略,由 specs 定义).
- 新增离线转换/打包工具:
  - 把常见来源(例如 `time_*.ply` 序列)转换为 `.sog4d` bundle.
- 引入并接受必要的依赖:
  - zip 读取/解包.
  - WebP 解码(纯 C# 或原生插件,按平台约束在设计中给出选择与降级策略).

本 change 以"新增能力"为主,不计划破坏现有 `.ply`/`.splat4d` 工作流.
若后续发现需要变更现有能力的强约束,会在 design/specs 阶段再明确标注 **BREAKING**.

## Capabilities

### New Capabilities

- `sog4d-container`: 定义 `.sog4d` 单文件 bundle 的容器布局与版本化策略.
  - 约束包含: 必需文件集合,路径命名规则,`meta.json` 的 schema,以及 forward/backward compatibility 规则.
- `sog4d-sequence-encoding`: 定义"逐帧 keyframe 序列"的编码方式.
  - 约束包含: 时间轴与帧索引的定义,每帧包含哪些属性流,属性的量化/压缩策略(codebook/palette 等),以及 SH 的承载范围.
- `sog4d-unity-importer`: 定义 Unity 侧的导入行为与运行时数据形态.
  - 约束包含: 导入失败的错误语义,峰值内存控制策略,缓存策略,以及导入后如何与 `GsplatRenderer`/`TimeNormalized` 对齐.
- `4dgs-keyframe-motion`: 定义 keyframe 资产在时间 `t` 下的空间评估,可见性,排序与 bounds 语义.
  - 目标是把"逐帧序列"的时序语义说清楚,避免实现中出现多套不一致的解释.

### Modified Capabilities

- `4dgs-resource-budgeting`: 需要扩展资源预算的要求,以覆盖 `.sog4d` 可能引入的"量化纹理/中间缓冲/解码开销".

## Impact

- Editor:
  - 新增 `.sog4d` 的 ScriptedImporter.
  - 可能需要新增或引入 zip/WebP 解码依赖(并处理不同 Unity/平台的兼容性).
- Runtime:
  - 增加 keyframe 序列的播放路径(与现有 `TimeNormalized` 保持一致).
  - 可能新增解码 compute/shader(如果选择 GPU-side 解码以保留运行时压缩优势).
- Tools:
  - 新增/扩展离线工具,支持从 PLY 序列打包生成 `.sog4d`.
- Docs/Samples/Tests:
  - 补充格式说明,导入说明,以及最小可复现样例与回归测试入口.
