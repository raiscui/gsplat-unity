## ADDED Requirements

### Requirement: Conversion tool MUST accept either a single `.ply` file or a directory of `.ply` frames
系统 MUST 为 `.ply -> .sog4d` 提供正式的离线转换入口,并支持以下两类输入形态:

- 单帧输入: 一个明确指定的 `.ply` 文件
- 序列输入: 一个包含一组 `.ply` 文件的目录

系统 MUST 要求用户在这两类输入形态中二选一,不得让“单帧文件”和“序列目录”同时作为同一次打包的输入来源.

当输入为目录时:
- 系统 MUST 收集目录中的 `.ply` 文件
- 系统 MUST 以确定性顺序组织帧序列
- 若文件名中含数字帧号,系统 MUST 优先按数字顺序排列

当输入为单帧文件时:
- 系统 MUST 将该文件视为一个 `frameCount = 1` 的合法输入序列
- 系统 MUST 复用与多帧路径相同的编码与打包主流程,而不是走另一套平行输出格式

#### Scenario: Single PLY file is accepted as a one-frame sequence
- **WHEN** 用户使用单个 `.ply` 文件作为输入执行 `.sog4d` 打包
- **THEN** 系统 MUST 接受该输入并继续生成 `.sog4d` bundle
- **AND** 该输入 MUST 被解释为 `frameCount = 1` 的序列

#### Scenario: Conflicting input modes are rejected
- **WHEN** 用户在同一次打包命令中同时提供单帧文件输入和序列目录输入
- **THEN** 系统 MUST 失败
- **AND** 错误信息 MUST 明确指出这两种输入模式只能二选一

### Requirement: Single-frame conversion MUST emit a valid one-frame `.sog4d` bundle
当输入仅包含一帧 `.ply` 时,系统 MUST 生成一个合法的 `.sog4d` bundle,并满足:

- `meta.json.frameCount` MUST 等于 `1`
- `meta.json.splatCount` MUST 等于该 `.ply` 的顶点数
- bundle 内 MUST 继续包含当前 `.sog4d` 路径所需的 streams 与引用文件
- 输出 bundle MUST 继续满足 `sog4d-container` 与 `sog4d-sequence-encoding` 的要求

当 `timeMapping.type = "uniform"` 时:
- 系统 MUST 令唯一帧的归一化时间为 `0.0`

系统 MUST NOT 通过复制同一帧或伪造第二帧的方式把单帧输入强行扩展成多帧 bundle.

#### Scenario: Single-frame bundle keeps frameCount at one
- **WHEN** 用户把一个单帧 `.ply` 成功打包为 `.sog4d`
- **THEN** 生成的 `meta.json.frameCount` MUST 为 `1`
- **AND** 系统 MUST NOT 额外生成一个伪造的第二帧

### Requirement: Single-frame and multi-frame inputs MUST share the same output contract
系统 MUST 让单帧输入与多帧输入共享同一套 `.sog4d` 输出契约,包括:

- 相同的 bundle 容器语义
- 相同的 stream 命名规则
- 相同的校验/自检命令
- 相同的下游 Unity importer 入口

系统 MAY 因 `frameCount` 不同而在元数据或 layer 数量上产生自然差异,但 MUST NOT 让单帧输出变成另一种只被单独工具识别的特殊格式.

#### Scenario: One-frame bundle remains importable through the normal `.sog4d` path
- **WHEN** 系统输出一个 `frameCount = 1` 的 `.sog4d` bundle
- **THEN** 该 bundle MUST 继续走正常 `.sog4d` Unity 导入路径
- **AND** 不得要求额外的单帧专用 importer 或后处理步骤

### Requirement: Validation and self-check MUST treat one-frame bundles as first-class supported output
系统 MUST 让 `.sog4d` 的 validate / self-check 流程把 `frameCount = 1` 视为合法情况.

对于单帧 bundle,系统 MUST 继续校验:

- `meta.json` 顶层字段合法性
- 所有被引用文件存在
- layout 尺寸能够覆盖 `splatCount`
- 所有量化索引与 labels 在合法范围内

系统 MUST NOT 仅因 `frameCount = 1` 就把 bundle 判为无效.

#### Scenario: Self-check accepts a valid one-frame bundle
- **WHEN** 用户对一个合法的单帧 `.sog4d` bundle 执行 validate 或 self-check
- **THEN** 系统 MUST 报告校验通过
- **AND** 不得输出“至少需要两帧”之类的错误
