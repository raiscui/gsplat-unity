# Capability: 4dgs-core

## ADDED Requirements

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

`.splat4d` 的一期定义如下:
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

#### Scenario: `.splat4d` 基本导入成功
- **WHEN** 导入一个 recordSize=64 且包含 `vx/vy/vz,time,duration` 的 `.splat4d`
- **THEN** 系统生成的 `GsplatAsset` MUST 具有与 splatCount 等长的 `Velocities/Times/Durations` 数据

#### Scenario: `.splat4d` 文件长度非法
- **WHEN** `.splat4d` 文件大小不是 64 的整数倍
- **THEN** 导入器 MUST 输出 error 并失败(不生成部分资产)

#### Scenario: `.splat4d` time/duration 越界时 clamp
- **WHEN** 导入的 `.splat4d` 中存在 `time=-0.2` 或 `duration=1.7`
- **THEN** 资产中的对应值 MUST 被 clamp 到 `[0, 1]`,并输出 warning 日志

### Requirement: Safe defaults when 4D fields are missing
当 PLY 不包含任何 4D 字段时,系统 MUST 保持旧的 3DGS 渲染行为.
此时:
- `Velocities` MUST 被视为全零.
- `Times` MUST 被视为全零.
- `Durations` MUST 被视为全 1.0(等价于全时可见).

#### Scenario: PLY 不包含任何 4D 字段
- **WHEN** 导入一个仅包含传统 3DGS 字段的 PLY
- **THEN** 渲染结果 MUST 与引入 4DGS 功能前一致(忽略浮点误差)

### Requirement: Clamp invalid time and duration values
导入器 MUST 对超出范围的 `time/duration` 做安全处理,避免运行期出现不可解释的结果.
- `time` MUST clamp 到 `[0, 1]`.
- `duration` MUST clamp 到 `[0, 1]`.
- 若发生 clamp,系统 MUST 输出一次明确的 warning,并包含资产路径与统计信息(最小/最大值).

#### Scenario: time/duration 超出归一化范围
- **WHEN** 导入的 PLY 中存在 `time=-0.2` 或 `duration=1.7`
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

### Requirement: Time-window visibility gating
系统 MUST 以时间窗控制可见性.
当且仅当 `t` 满足 `time0 <= t <= time0 + duration` 时,该 splat 可见.
当 splat 不可见时,它 MUST 不对最终颜色输出产生任何贡献.

#### Scenario: 时间窗外完全不可见
- **WHEN** `t < time0` 或 `t > time0 + duration`
- **THEN** 该 splat MUST 被剔除,不会写入任何片元颜色

#### Scenario: 时间窗内可见
- **WHEN** `t` 落在 `[time0, time0 + duration]` 内
- **THEN** 该 splat MUST 按现有 3DGS shader 逻辑参与投影与混合

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

#### Scenario: 高斯在时间窗内移动到静态 bounds 外
- **WHEN** 某个 splat 在 `t` 下的 `pos(t)` 超出静态 bounds
- **THEN** 系统仍 MUST 正确渲染该 splat,不应被相机剔除掉
