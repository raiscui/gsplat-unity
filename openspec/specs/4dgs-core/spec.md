# Capability: 4dgs-core

## Purpose
定义 4D Gaussian Splatting 的核心字段导入、运动模型、可见性裁剪、排序与 bounds 约束.

## Requirements

### Requirement: Import canonical 4D fields from PLY
当 PLY vertex 属性包含 4D 字段时,导入器 MUST 读取并保存到资产中.
4D 字段包括:
- 速度: `(vx, vy, vz)` 或等价别名.
- 时间: `time` 或等价别名.
- 持续时间: `duration` 或等价别名.

速度 MUST 与 `x/y/z` 使用同一坐标系(对象空间).
速度单位 MUST 表示 "每 1.0 归一化时间的位移".
时间与持续时间 MUST 被视为归一化值,目标范围为 `[0, 1]`.

#### Scenario: PLY 包含标准字段(vx,vy,vz,time,duration)
- **WHEN** 导入一个同时包含 `x/y/z` 与 `vx/vy/vz,time,duration` 的 PLY
- **THEN** 系统生成的 `GsplatAsset` MUST 具有与 splatCount 等长的 `Velocities/Times/Durations` 数据

#### Scenario: PLY 使用可识别的别名字段
- **WHEN** 导入 PLY,其中速度字段名为 `velocity_x/velocity_y/velocity_z`,时间字段名为 `t`,持续时间字段名为 `dt`
- **THEN** 导入器 MUST 正确映射别名到 4D 字段,且渲染行为与标准字段一致

### Requirement: Import canonical 4D fields from `.splat4d` binary
系统 MUST 支持导入 `.splat4d` 二进制格式,用于更快的导入与更顺滑的 VFX Graph 工作流.

`.splat4d` 支持两个版本:
- v1: 无 header 的 64B record 数组(一期定义,兼容旧工具链).
- v2: 有 header + section table 的可扩展格式(用于承载 SH rest 与更准确的时间核).

#### v1: legacy record array(无 header)
`.splat4d` 的 v1 定义如下:
- 文件为 **无 header** 的 record 数组.
- 文件大小 MUST 为 recordSize 的整数倍,否则导入器 MUST 以明确 error 失败.
- recordSize MUST 为 `64` bytes.
- 数值端序 MUST 为 little-endian.

单条 record 的字段布局(偏移以 byte 为单位):
- `0..11`: `px/py/pz`(float32 * 3)
- `12..23`: `sx/sy/sz`(float32 * 3),表示 **线性尺度**(不是对数尺度)
- `24..27`: `r/g/b/a`(uint8 * 4)
  - `r/g/b` 表示 **基础颜色(baseRgb)**,范围 `[0,1]`,等价于 `f_dc * SH_C0 + 0.5` 的量化结果
  - `a` 表示 opacity,范围 `[0,1]`
- `28..31`: `rw/rx/ry/rz`(uint8 * 4),量化四元数,还原规则与 `SplatVFX` 一致:
  - `v = (byte - 128) / 128`,得到 `[-1,1]` 近似
  - 四元数在系统内 MUST 表示为 `float4(w, x, y, z)`
- `32..43`: `vx/vy/vz`(float32 * 3)
- `44..47`: `time`(float32, 归一化 `[0,1]`)
- `48..51`: `duration`(float32, 归一化 `[0,1]`)
- `52..63`: padding(保留,读取时忽略)

`.splat4d` 与现有 PLY 导入行为的一致性约束:
- `vx/vy/vz` MUST 与 `px/py/pz` 使用同一坐标系(对象空间).
- 系统 MUST **不** 在导入期对坐标轴做隐式翻转.
  - 若数据来源存在坐标系差异,应在导出阶段处理.
- `.splat4d` 一期不承载高阶 SH,因此导入后资产 MUST 视为 `SHBands=0`.

#### v2: header + section table(可扩展,支持 SH rest 与时间核选择)
当文件以 magic `SPL4DV02` 开头时,导入器 MUST 按 v2 格式解析.

v2 的目标:
- 在保持 base record 与 v1 对齐的同时,额外承载:
  - SH rest 的 per-band codebook(`sh1/sh2/sh3`)与 labels.
  - labels 的 `deltaSegments`(分段),用于随机访问/流式基础设施.
  - 明确的时间核语义: window(time0+duration)或 gaussian(mu+sigma).

v2 文件总体结构:
- `[Header(64B)]`
- `[Section Data ...]`(顺序任意)
- `[SectionTable]`(位于 header 指定的 offset)

##### v2 Header(64 bytes, little-endian)
- `magic`: 8 bytes ASCII,值 MUST 为 `"SPL4DV02"`
- `version`: u32,值 MUST 为 `2`
- `headerSizeBytes`: u32,值 MUST 为 `64`
- `sectionCount`: u32,SectionEntry 数量
- `recordSizeBytes`: u32,值 MUST 为 `64`
- `splatCount`: u32
- `shBands`: u32,范围 MUST 为 `[0,3]`
- `timeModel`: u32
  - `1`: window(time0+duration),与 v1 语义一致
  - `2`: gaussian(time=mu,duration=sigma),用于表达时间高斯核
  - 兼容与约束:
    - 当 `timeModel=1(window)` 时,`time/duration` 目标范围为 `[0,1]`,导入器 MUST clamp 到 `[0,1]`.
    - 当 `timeModel=2(gaussian)` 时:
      - `time` 表示 `mu`,MAY 超出 `[0,1]`(例如来自优化后的 FreeTimeGS checkpoint).
      - `duration` 表示 `sigma`,MUST > 0,导入器 MUST clamp 到 `>= epsilon`(例如 `1e-6`).
- `frameCount`: u32
  - 当 `labelsEncoding="delta-v1"` 时 MUST > 0
  - 其它情况 MAY 为 0(未知)
- `sectionTableOffset`: u64,指向 SectionTable 的起始位置
- `reserved0/reserved1`: u64 * 2,保留为 0

##### SectionTable
SectionTable 由两部分组成:
1) TableHeader(16 bytes):
   - `magic`: 4 bytes ASCII,值 MUST 为 `"SECT"`
   - `version`: u32,值 MUST 为 `1`
   - `sectionCount`: u32,值 MUST 等于 header.sectionCount
   - `reserved`: u32,保留为 0
2) `sectionCount` 个 SectionEntry(每个 32 bytes):
   - `kindFourCC`: u32,4 字节 ASCII 的 fourcc(例如 `"RECS"`)
   - `band`: u32
     - 对于 SH 相关 section,取值为 `1/2/3`(对应 sh1/sh2/sh3)
     - 对于非 SH section,值 MUST 为 0
   - `startFrame`: u32
     - 仅对 labels/delta 的 segment section 有意义
   - `frameCount`: u32
     - 仅对 labels/delta 的 segment section 有意义
   - `offset`: u64,section 数据起始偏移
   - `length`: u64,section 数据字节长度

##### 必需的 section
- `RECS`: base record 数组
  - `length` MUST 等于 `splatCount * recordSizeBytes`
  - record layout 与 v1 完全一致(见上文 v1 record layout)
- `META`: v2 meta block(用于声明 SH codebook/labelsEncoding 等)

##### v2 META(section kind=`META`, little-endian)
`META` section 的最小实现为固定大小结构 `MetaV1(64B)`:
- `metaVersion`: u32,值 MUST 为 `1`
- `temporalGaussianCutoff`: float32
  - 仅在 `timeModel=2(gaussian)` 时使用
  - 用于把非常小的 temporal weight 视为不可见(例如 0.01)
- `deltaSegmentLength`: u32
  - 当 `labelsEncoding="delta-v1"` 时用于描述 exporter 的分段长度
  - `0` 表示“单 segment 覆盖全帧”
- `reserved0`: u32,保留为 0
- `sh1/sh2/sh3` 各 1 组 `BandInfo(16B)`:
  - `codebookCount`: u32(=labels 的最大值+1),当该 band 不存在时 MUST 为 0
  - `centroidsType`: u32
    - `1`: f16
    - `2`: f32
  - `labelsEncoding`: u32
    - `1`: full(单份 labels,不分段)
    - `2`: delta-v1(按 segment 写 base labels + delta)
  - `reserved`: u32,保留为 0

##### SH rest sections(per-band)
当 `shBands > 0` 时:
- 导入器 MUST 读取并生成资产的 SH rest 系数,并设置:
  - `GsplatAsset.SHBands = shBands`
  - `GsplatAsset.SHs != null`

每个启用的 band 必须包含:
- `SHCT`(centroids): `kind="SHCT", band={1,2,3}`
  - 数据为 little-endian 的 float16 或 float32,由 `META.BandInfo.centroidsType` 决定
  - 向量维度:
    - sh1: 3 coeff * 3 channels = 9 scalars
    - sh2: 5 coeff * 3 channels = 15 scalars
    - sh3: 7 coeff * 3 channels = 21 scalars
  - centroids 数量为 `codebookCount`

- labels:
  - `labelsEncoding=full`:
    - 必须存在 1 个 `SHLB` section: `kind="SHLB", band=..., startFrame=0, frameCount=0`
    - 数据为 little-endian 的 `u16 labels[splatCount]`
  - `labelsEncoding=delta-v1`:
    - 必须存在覆盖 `[0, frameCount)` 的多个 segment section 对:
      - `SHLB`: `kind="SHLB", band=..., startFrame=segStart, frameCount=segFrameCount`
        - 数据为 little-endian 的 `u16 baseLabels[splatCount]`
      - `SHDL`: `kind="SHDL", band=..., startFrame=segStart, frameCount=segFrameCount`
        - 数据为 `labelDeltaV1` 二进制块(见下文)
    - segments MUST 连续覆盖 frame:
      - 第 0 段 `startFrame=0`
      - 相邻段满足:
        - `next.startFrame = prev.startFrame + prev.frameCount`
      - 所有段 frameCount 之和 MUST 等于 header.frameCount

##### labelDeltaV1(SH delta)格式
`SHDL` section 的 payload MUST 为 little-endian,并使用格式 `labelDeltaV1`:
- Header:
  - `magic`: 8 bytes ASCII,值 MUST 为 `"SOG4DLB1"` 或 `"SPL4DLB1"`
  - `version`: u32,值 MUST 为 `1`
  - `segmentStartFrame`: u32,值 MUST 等于该 segment 的 `startFrame`
  - `segmentFrameCount`: u32,值 MUST 等于该 segment 的 `frameCount`
  - `splatCount`: u32,值 MUST 等于 header.splatCount
  - `labelCount`: u32,值 MUST 等于 `BandInfo.codebookCount`

delta body MUST 为一组按 frame 顺序的 block.
对于 segment 内每个 frame(从 `startFrame+1` 到 `startFrame+frameCount-1`):
- block MUST 以 `updateCount`(u32)开头
- 随后 MUST 紧跟 `updateCount` 个 update 条目
- 每个 update MUST 为 `(splatId, newLabel, reserved)`:
  - `splatId`: u32,范围 MUST 为 `[0, splatCount-1]`
  - `newLabel`: u16,范围 MUST 为 `[0, labelCount-1]`
  - `reserved`: u16,值 MUST 为 `0`
- 同一个 block 内的 update 条目 MUST 按 `splatId` 严格递增.

#### Scenario: `.splat4d` 基本导入成功
- **WHEN** 导入一个 recordSize=64 且包含 `vx/vy/vz,time,duration` 的 `.splat4d`
- **THEN** 系统生成的 `GsplatAsset` MUST 具有与 splatCount 等长的 `Velocities/Times/Durations` 数据

#### Scenario: `.splat4d` 文件长度非法
- **WHEN** `.splat4d` 文件大小不是 64 的整数倍
- **THEN** 导入器 MUST 输出 error 并失败(不生成部分资产)

#### Scenario: `.splat4d` window(timeModel=1)的 time/duration 越界时 clamp
- **WHEN** 导入的 `.splat4d` 为 v1 或 v2 且 `timeModel=1`,并且存在 `time=-0.2` 或 `duration=1.7`
- **THEN** 资产中的对应值 MUST 被 clamp 到 `[0, 1]`,并输出 warning 日志

#### Scenario: `.splat4d` gaussian(timeModel=2)的 time(mu) 越界时不 clamp
- **WHEN** 导入的 `.splat4d` 为 v2 且 `timeModel=2`,并且存在 `time=-0.2` 或 `time=1.7`
- **THEN** 资产中的 `time(mu)` MUST 保持原值(仅修复 NaN/Inf),系统 MUST NOT 把它 clamp 到 `[0,1]`

### Requirement: Safe defaults when 4D fields are missing
当 PLY 不包含任何 4D 字段时,系统 MUST 保持旧的 3DGS 渲染行为.
此时:
- `Velocities` MUST 被视为全零.
- `Times` MUST 被视为全零.
- `Durations` MUST 被视为全 1.0(等价于全时可见).

#### Scenario: PLY 不包含任何 4D 字段
- **WHEN** 导入一个仅包含传统 3DGS 字段的 PLY
- **THEN** 渲染结果 MUST 与引入 4DGS 功能前一致(忽略浮点误差)

### Requirement: Sanitize time and duration values safely
导入器 MUST 对 `time/duration` 做安全处理,避免运行期出现不可解释的结果.

- 当 `timeModel=1(window)` 或数据源不支持 `timeModel`(例如 PLY / `.splat4d v1`):
  - `time` MUST clamp 到 `[0, 1]`.
  - `duration` MUST clamp 到 `[0, 1]`.
- 当 `timeModel=2(gaussian)`:
  - `time(mu)` MUST 只做 NaN/Inf -> 0 的修复,并 MUST NOT clamp 到 `[0,1]`.
  - `duration(sigma)` MUST 只做 NaN/Inf -> 0 的修复,并 MUST clamp 到 `>= epsilon`(例如 `1e-6`).

- 若发生 clamp/修复,系统 MUST 输出一次明确的 warning,并包含资产路径与统计信息(最小/最大值).

#### Scenario: window(timeModel=1)的 time/duration 超出归一化范围
- **WHEN** 导入的 PLY 或 `.splat4d v1` 中存在 `time=-0.2` 或 `duration=1.7`
- **THEN** 资产中的对应值 MUST 被 clamp 到 `[0, 1]`,并输出 warning 日志

### Requirement: Evaluate 4D motion model consistently
在时间 `t` 下,系统 MUST 使用以下运动模型计算瞬时位置:
`pos(t) = pos0 + vel * (t - time0)`.

此规则 MUST 同时用于:
- GPU 排序 pass 的深度 key 计算.
- 渲染 shader 的投影与裁剪计算.

#### Scenario: 同一帧内排序与渲染使用同一 pos(t)
- **WHEN** 在某一帧,同一个 `GsplatRenderer` 先执行排序 pass 再执行绘制
- **THEN** 这两步 MUST 使用同一 `t` 值,且均按 `pos(t)` 计算空间位置

### Requirement: Time-window visibility gating(timeModel=1)
当 `timeModel=1(window)` 时,系统 MUST 以时间窗控制可见性.
当且仅当 `t` 满足 `time0 <= t <= time0 + duration` 时,该 splat 可见.
当 splat 不可见时,它 MUST 不对最终颜色输出产生任何贡献.

#### Scenario: 时间窗外完全不可见
- **WHEN** `t < time0` 或 `t > time0 + duration`
- **THEN** 该 splat MUST 被剔除,不会写入任何片元颜色

#### Scenario: 时间窗内可见
- **WHEN** `t` 落在 `[time0, time0 + duration]` 内
- **THEN** 该 splat MUST 按现有 3DGS shader 逻辑参与投影与混合

### Requirement: Gaussian temporal visibility gating(timeModel=2)
当 `timeModel=2(gaussian)` 时,系统 MUST 以时间高斯核控制可见性与平滑淡入淡出.

系统 MUST 定义 temporal weight:
- `temporalWeight(t) = exp(-0.5 * ((t - mu) / sigma)^2)`
  - 其中 `mu=time`,`sigma=duration`

并且:
- 系统 MUST 使用 `TemporalGaussianCutoff` 作为不可见阈值:
  - 当 `temporalWeight(t) < TemporalGaussianCutoff` 时,该 splat MUST 被剔除,不产生任何颜色贡献.
- 系统 MUST 把 `temporalWeight(t)` 乘到 opacity 上,用于实现平滑淡入淡出.

#### Scenario: 高斯核 cutoff 外不可见
- **WHEN** `timeModel=2` 且 `temporalWeight(t) < TemporalGaussianCutoff`
- **THEN** 该 splat MUST 被剔除,不会写入任何片元颜色

#### Scenario: 高斯核中心处权重为 1
- **WHEN** `timeModel=2` 且 `t == mu`
- **THEN** `temporalWeight(t)` MUST 为 1,opacity 不应被衰减(忽略浮点误差)

### Requirement: Time-aware depth sorting for correct blending
当存在多个可见 splat 且相互遮挡时,系统 MUST 基于时间 `t` 下的 `pos(t)` 深度进行 back-to-front 排序.
排序结果 MUST 通过现有的 `OrderBuffer` 驱动绘制顺序,以确保透明混合的正确性.

不可见 splat MAY 仍存在于排序输入中.
但它 MUST 在渲染阶段被丢弃,从而不影响最终画面.

#### Scenario: 两个 splat 在运动中发生遮挡关系变化
- **WHEN** 两个 splat 的 `pos(t)` 深度顺序随 `t` 变化而互换
- **THEN** 画面中的遮挡关系 MUST 随时间一致地变化,不应出现明显的排序错误闪烁

### Requirement: Motion-aware bounds to prevent culling artifacts
系统 MUST 使用保守的运动包围盒,避免因 motion 导致的相机剔除错误.
当资产包含 4D 字段时:
- 渲染用 `worldBounds` MUST 在静态 bounds 的基础上扩展一个 motion padding.
- motion padding MUST 至少覆盖 `maxSpeed * maxDuration` 形成的最大位移量(按对象空间估算).
  - 当 `timeModel=1(window)` 时,`maxDuration` SHOULD 为资产内 `duration` 的最大值.
  - 当 `timeModel=2(gaussian)` 时,`maxDuration` SHOULD 为:
    - `halfWidthFactor * sigma` 的最大值
    - 其中 `halfWidthFactor = sqrt(-2 * ln(TemporalGaussianCutoff))`

#### Scenario: 高斯在时间窗内移动到静态 bounds 外
- **WHEN** 某个 splat 在 `t` 下的 `pos(t)` 超出静态 bounds
- **THEN** 系统仍 MUST 正确渲染该 splat,不应被相机剔除掉
