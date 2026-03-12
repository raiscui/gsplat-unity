## ADDED Requirements

### Requirement: Conversion tool MUST accept either a single ordinary 3DGS `.ply` file or a directory of PLY frames
系统 MUST 为 `.ply -> .splat4d` 提供正式的离线输入入口,并支持以下两类输入形态:

- 单帧输入: 一个明确指定的普通 3DGS `.ply` 文件
- 序列输入: 一个包含一组 `.ply` 文件的目录

系统 MUST 要求用户在这两类输入形态中二选一.
系统 MUST NOT 允许“单帧文件输入”和“序列目录输入”同时作为同一次导出的输入来源.

当输入为单帧文件时:
- 系统 MUST 将该文件视为合法的单帧输入
- 系统 MUST 继续复用现有 `.splat4d` 编码主流程
- 系统 MUST NOT 因为输入只有一帧就切换到另一种平行输出格式

当输入为目录时:
- 系统 MUST 收集目录中的 `.ply` 文件
- 系统 MUST 以确定性顺序组织输入
- 若文件名中含数字帧号,系统 MUST 优先按数字顺序排列

#### Scenario: Single-file input is accepted as a formal one-frame path
- **WHEN** 用户使用一个普通 3DGS `.ply` 文件执行正式 `.splat4d` 导出
- **THEN** 系统 MUST 接受该输入
- **AND** 该输入 MUST 被解释为单帧 `.splat4d` 导出路径

#### Scenario: Conflicting input modes are rejected
- **WHEN** 用户在同一次导出命令里同时提供单帧文件输入和序列目录输入
- **THEN** 系统 MUST 失败
- **AND** 错误信息 MUST 明确指出两种输入模式只能二选一

### Requirement: Single ordinary 3DGS `.ply` input MUST emit a valid static one-frame `.splat4d`
当输入仅包含一个普通 3DGS `.ply` 文件时,系统 MUST 生成一个合法的单帧静态 `.splat4d`,并满足:

- 每个输入 gaussian MUST 对应 1 条标准 `.splat4d` record
- 输出 MUST 继续使用现有 `.splat4d` 64 bytes/record 契约
- 输出 MUST 继续使用现有 baseRgb / opacity / scale / quaternion 量化口径
- 输出记录中的 4D 默认字段 MUST 为:
  - `vx = 0`
  - `vy = 0`
  - `vz = 0`
  - `time = 0`
  - `duration = 1`

系统 MUST 将这组默认值视为“静态单帧 `.splat4d`”的正式导出语义.
系统 MUST NOT 通过伪造第二帧、复制额外记录或注入虚假运动来模拟动态数据.

#### Scenario: Average path writes the canonical static defaults for a one-frame input
- **WHEN** 用户使用一个普通 3DGS `.ply` 文件并以单帧正式路径导出 `.splat4d`
- **THEN** 输出的每条 record MUST 写入 `vx/vy/vz = 0`
- **AND** 输出的每条 record MUST 写入 `time = 0` 与 `duration = 1`

#### Scenario: Keyframe mode rejects one-frame input instead of inventing fake motion
- **WHEN** 用户请求 `keyframe` 语义,但输入实际上只有 1 个 `.ply`
- **THEN** 系统 MUST 失败
- **AND** 错误信息 MUST 明确指出 `keyframe` 路径至少需要 2 帧真实输入

### Requirement: Single-file path MUST reject non-Gaussian PLYs with clear field errors
单帧 `.ply -> .splat4d` 正式路径 MUST 继续要求 Gaussian Splatting 风格的顶点字段.
至少包括:

- `x/y/z`
- `f_dc_0/f_dc_1/f_dc_2`
- `opacity`
- `scale_0/scale_1/scale_2`
- `rot_0/rot_1/rot_2/rot_3`

当输入缺少这些必要字段中的任意一项时,系统 MUST 失败.
系统 MUST 在错误信息中指出缺失字段,而不是静默生成不完整或错误的 `.splat4d`.

#### Scenario: Missing required Gaussian field fails clearly
- **WHEN** 用户提供的单帧 `.ply` 缺少 `opacity` 或任一必需 Gaussian 字段
- **THEN** 系统 MUST 失败
- **AND** 错误信息 MUST 明确指出缺少的字段名称
