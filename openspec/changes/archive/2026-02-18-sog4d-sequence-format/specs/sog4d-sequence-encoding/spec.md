# Capability: sog4d-sequence-encoding

## ADDED Requirements

### Requirement: Frame-to-frame splat identity MUST be stable
系统 MUST 保证 frame-to-frame 的 splat identity 稳定,以支持逐帧 keyframe 插值.
因此系统 MUST 满足以下恒等约束:
- `splatId` 的定义在所有帧中 MUST 一致.
- `splatCount` 在所有帧中 MUST 一致,且等于 `meta.json.splatCount`.
- 每帧的属性图(layout) MUST 使用相同的 `layout` 参数,并遵循相同的 `splatId -> pixel` 映射.

#### Scenario: frameCount 变化导致无法插值
- **WHEN** 某一帧的属性图像素数量不足以覆盖 `splatCount`
- **THEN** 导入器 MUST 失败,并输出明确的 error(包含 frameIndex 与 `splatCount` 信息)

### Requirement: `timeMapping` MUST support both uniform and explicit modes
`meta.json.timeMapping.type` MUST 为以下之一:
- `"uniform"`: 帧时间均匀分布在 `[0,1]`
- `"explicit"`: 帧时间由 `frameTimesNormalized[]` 显式给出

当 `type="uniform"` 时:
- 当 `frameCount > 1` 时,系统 MUST 将第 `i` 帧的时间定义为 `t_i = i / (frameCount - 1)`
- 当 `frameCount == 1` 时,系统 MUST 将唯一帧时间定义为 `t_0 = 0.0`

当 `type="explicit"` 时:
- `meta.json.timeMapping.frameTimesNormalized` MUST 存在
- `frameTimesNormalized` 长度 MUST 等于 `frameCount`
- `frameTimesNormalized[i]` MUST 位于 `[0,1]`
- `frameTimesNormalized` MUST 单调非递减

#### Scenario: uniform time mapping 的帧时间定义
- **WHEN** `frameCount=5` 且 `timeMapping.type="uniform"`
- **THEN** 系统 MUST 解释帧时间为 `[0.0, 0.25, 0.5, 0.75, 1.0]`

#### Scenario: explicit time mapping 的合法性校验
- **WHEN** `timeMapping.type="explicit"` 且 `frameTimesNormalized` 存在越界值(例如 `-0.1`)
- **THEN** 导入器 MUST 失败,并输出明确的 error(包含越界值与索引)

### Requirement: `layout` MUST define a deterministic splatId-to-pixel mapping
系统 MUST 通过 `layout` 定义确定性的 splatId-to-pixel 映射.
系统 MUST 以"数据图"的方式读取像素,而不是以"颜色图"的方式读取.

`meta.json.layout` MUST 包含:
- `type`: MUST 为 `"row-major"`
- `width`: MUST 为正整数
- `height`: MUST 为正整数

并且 MUST 定义映射:
- `pixelIndex = splatId`
- `x = pixelIndex % width`
- `y = floor(pixelIndex / width)`
- 仅当 `splatId < splatCount` 时该像素有效

#### Scenario: layout 的尺寸不足
- **WHEN** `width * height < splatCount`
- **THEN** 导入器 MUST 失败,并输出明确的 error(包含 `width`, `height`, `splatCount`)

### Requirement: Per-frame path templates MUST interpret `{frame}` as a zero-padded decimal frame index
系统 MUST 以确定性的方式把 per-frame 路径模板中的 `{frame}` 替换为 frameIndex 字符串.

对于任何 per-frame 路径模板(例如 `position.hiPath`, `scale.indicesPath`, `rotation.path`, `sh.sh0Path`, `sh.shNLabelsPath`):
- 模板字符串 MUST 包含 `{frame}`
- 系统 MUST 将 `{frame}` 替换为十进制 frameIndex,并左侧补零到至少 5 位
  - 例如 frameIndex=7 时,替换结果 MUST 为 `"00007"`
  - 当 frameIndex>=100000 时,替换结果 MUST 允许自然扩展为更多位数(不截断)

#### Scenario: `{frame}` 的零填充替换
- **WHEN** `frameIndex=3`,且某个模板为 `"frames/{frame}/sh0.webp"`
- **THEN** 系统 MUST 解析到实际路径 `"frames/00003/sh0.webp"`

### Requirement: Streams MUST be per-frame and interpolable for position, scale, rotation, and SH
用户已确认: position, scale, rotation, opacity, SH MUST 逐帧可插值.
因此 `meta.json.streams` MUST 声明并提供以下 streams,且它们 MUST 为 per-frame:
- `position`
- `scale`
- `rotation`
- `sh`

其中:
- opacity MUST 作为 `sh0.webp` 的 alpha 被承载(见下文 `streams.sh`).

并且系统 MUST 能对上述属性在相邻两帧之间进行插值(插值语义由 `4dgs-keyframe-motion` 定义).

#### Scenario: 缺少必需 stream
- **WHEN** `meta.json.streams` 缺少 `sh`
- **THEN** 导入器 MUST 失败,并输出明确的 error(包含缺失 stream 名)

### Requirement: Position stream MUST use u16 quantization with per-frame range
`streams.position` MUST 使用 16-bit 量化表示 position.
系统 MUST 支持 per-frame range,以减少动态序列的量化误差.

`streams.position` MUST 包含:
- `rangeMin`: float3 数组,长度 MUST 等于 `frameCount`
- `rangeMax`: float3 数组,长度 MUST 等于 `frameCount`
- `hiPath`: 字符串模板,例如 `"frames/{frame}/position_hi.webp"`
- `loPath`: 字符串模板,例如 `"frames/{frame}/position_lo.webp"`

并且每帧 MUST 存在两张 WebP 图:
- `position_hi.webp`: 每个像素的 RGB 为 x,y,z 的高 8-bit
- `position_lo.webp`: 每个像素的 RGB 为 x,y,z 的低 8-bit

对每个分量,解码规则 MUST 为:
- `q = hi * 256 + lo` (范围 `[0, 65535]`)
- `x = rangeMin.x + (q / 65535) * (rangeMax.x - rangeMin.x)` (y,z 同理)

#### Scenario: position range 数组长度不匹配
- **WHEN** `streams.position.rangeMin` 长度不等于 `frameCount`
- **THEN** 导入器 MUST 失败,并输出明确的 error(包含 `frameCount` 与实际长度)

### Requirement: Scale stream MUST use a codebook plus per-frame indices
`streams.scale` MUST 使用 codebook + index map 的编码方式.
`streams.scale` MUST 包含:
- `codebook`: float3 数组,每个元素表示一个候选 scale(对象空间)
- `indicesPath`: 字符串模板,例如 `"frames/{frame}/scale_indices.webp"`

每帧 MUST 存在一张 `indicesPath` 指向的 WebP 图像作为 index map(例如 `scale_indices.webp`):
- 每个像素的 RG 表示一个 u16 index(小端): `index = r + (g << 8)`
- 解码时 `scale = codebook[index]`

#### Scenario: scale index 越界
- **WHEN** `scale_indices.webp` 中出现 `index >= len(codebook)`
- **THEN** 导入器 MUST 失败,并输出明确的 error(包含 frameIndex 与 index 值)

### Requirement: Rotation stream MUST use per-frame quantized quaternions
`streams.rotation` MUST 以 per-frame 的量化 quaternion 表示旋转.
`streams.rotation` MUST 包含:
- `path`: 字符串模板,例如 `"frames/{frame}/rotation.webp"`

每帧 MUST 存在一张 `path` 指向的 WebP 图像(例如 `rotation.webp`).
每个像素的 RGBA MUST 表示 `(w, x, y, z)` 四元数的 8-bit 量化分量.

解码规则 MUST 与 `.splat4d` 保持一致:
- `v = (byte - 128) / 128`,得到 `[-1, 1]` 的近似值
- 解码后 MUST 归一化 quaternion
- 为了稳定插值路径,系统 MUST 对 quaternion 做半球规范化: `if w < 0 then q = -q`

#### Scenario: rotation 解码后归一化
- **WHEN** 某个像素解码得到的 quaternion 长度不为 1
- **THEN** 系统 MUST 在归一化后再进入后续插值与渲染流程

### Requirement: SH stream MUST be per-frame and support interpolation
`streams.sh` MUST 支持 SH0 与高阶 SH(rest coefficients)的逐帧表达,并可插值.

`streams.sh` MUST 包含:
- `bands`: 整数,范围 MUST 为 `[0, 3]`
- `sh0Path`: 字符串模板,例如 `"frames/{frame}/sh0.webp"`
- `sh0Codebook`: float 数组,长度 MUST 为 `256`

每帧 MUST 存在一张 `sh0Path` 指向的 WebP 图像(例如 `sh0.webp`).
该图像 MUST 为 RGBA8 数据图.
对每个像素:
- `f_dc.r = sh0Codebook[R]`
- `f_dc.g = sh0Codebook[G]`
- `f_dc.b = sh0Codebook[B]`
- `opacity = A / 255`

当 `bands > 0` 时,`streams.sh` 还 MUST 包含:
- `shNCount`: 整数,范围 MUST 为 `[1, 65535]`
- `shNCentroidsType`: 字符串,值 MUST 为 `"f16"` 或 `"f32"`
- `shNCentroidsPath`: 字符串,例如 `"shN_centroids.bin"`
- `shNLabelsEncoding`: 字符串,值 MUST 为 `"full"` 或 `"delta-v1"`

若 `shNLabelsEncoding` 缺失,系统 MUST 将其视为 `"full"`.

当 `bands > 0` 时,系统 MUST 定义:
- `restCoeffCount = (bands + 1)^2 - 1`

当 `shNLabelsEncoding="full"` 时,`streams.sh` 还 MUST 包含:
- `shNLabelsPath`: 字符串模板,例如 `"frames/{frame}/shN_labels.webp"`

并且每帧 MUST 存在一张 `shNLabelsPath` 指向的 WebP 图像(例如 `shN_labels.webp`).
该图像的每个像素 RG MUST 表示一个 u16 label(小端):
- `label = r + (g << 8)`
- `label` MUST 满足 `label < shNCount`

当 `shNLabelsEncoding="delta-v1"` 时,`streams.sh` 还 MUST 包含:
- `shNDeltaSegments`: 数组,按 `startFrame` 升序排列,覆盖 `[0, frameCount)` 的所有帧

并且:
- `streams.sh` MUST NOT 包含 `shNLabelsPath`(避免 "full" 与 "delta" 的路径定义发生歧义).
- `shNDeltaSegments` 的每个 segment MUST 包含:
  - `startFrame`: 整数,范围 MUST 为 `[0, frameCount-1]`
  - `frameCount`: 正整数
  - `baseLabelsPath`: 字符串,指向该 segment 首帧的 labels WebP(格式同 `shN_labels.webp`)
  - `deltaPath`: 字符串,指向该 segment 的 delta 二进制文件
- segments MUST 满足:
  - 第 0 个 segment 的 `startFrame` MUST 为 `0`
  - 相邻 segments MUST 连续:
    - `segments[i+1].startFrame == segments[i].startFrame + segments[i].frameCount`
  - 所有 segments 的 `frameCount` 总和 MUST 等于 `frameCount`

`baseLabelsPath` 指向的 WebP 图像 MUST 满足:
- 每个像素 RG 表示一个 u16 label(小端): `label = r + (g << 8)`
- `label` MUST 满足 `label < shNCount`

`deltaPath` 指向的二进制文件 MUST 为 little-endian,并使用格式 `shNLabelDeltaV1`:
- Header:
  - `magic`: 8 bytes ASCII,值 MUST 为 `"SOG4DLB1"`
  - `version`: u32,值 MUST 为 `1`
  - `segmentStartFrame`: u32,值 MUST 等于该 segment 的 `startFrame`
  - `segmentFrameCount`: u32,值 MUST 等于该 segment 的 `frameCount`
  - `splatCount`: u32,值 MUST 等于 `meta.json.splatCount`
  - `shNCount`: u32,值 MUST 等于 `streams.sh.shNCount`
- Body:
  - 对于该 segment 的每个后续帧(共 `segmentFrameCount - 1` 个),按时间顺序存储一个 delta block:
    - `updateCount`: u32
    - 重复 `updateCount` 次:
      - `splatId`: u32,且 MUST 满足 `splatId < splatCount`
      - `label`: u16,且 MUST 满足 `label < shNCount`
      - `reserved`: u16,值 MUST 为 `0`
    - 同一个 delta block 内,`splatId` MUST 严格递增(不允许重复),便于快速校验与解码

`shN_centroids.bin` MUST 为 little-endian 的二进制文件.
它 MUST 存储 `shNCount * restCoeffCount` 个连续的 `float3` 系数项(按 entry-major 顺序):
- 对 palette entry `n` 与系数 `k`,该系数 MUST 位于索引 `(n * restCoeffCount + k)` 的位置
- 标量类型由 `shNCentroidsType` 决定:
  - `"f16"`: IEEE 754 half,每个标量 2 bytes
  - `"f32"`: IEEE 754 float,每个标量 4 bytes

#### Scenario: SH bands=3 时 restCoeffCount 校验
- **WHEN** `bands=3`
- **THEN** `restCoeffCount` MUST 为 `15`

#### Scenario: delta-v1 segments 覆盖所有帧
- **WHEN** `shNLabelsEncoding="delta-v1"`,且 `frameCount=120`
- **THEN** `shNDeltaSegments` 的 segments MUST 以连续方式覆盖 frame `[0..119]`,不允许缺帧或重叠

#### Scenario: delta-v1 header 与 meta 不一致
- **WHEN** 某个 `deltaPath` 文件的 `segmentFrameCount` 与其所属 segment 的 `frameCount` 不一致
- **THEN** 导入器 MUST 失败,并输出明确的 error(包含 segmentIndex 与字段名)
