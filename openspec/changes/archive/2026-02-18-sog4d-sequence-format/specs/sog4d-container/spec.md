# Capability: sog4d-container

## ADDED Requirements

### Requirement: `.sog4d` MUST be a single-file ZIP bundle
系统 MUST 将 `.sog4d` 视为一个 ZIP bundle.
bundle 根目录 MUST 包含 `meta.json`.
bundle 内的所有路径 MUST 为相对路径,并使用 "/" 作为分隔符.

#### Scenario: 输入文件不是 ZIP
- **WHEN** 用户导入一个扩展名为 `.sog4d` 但实际不是 ZIP 的文件
- **THEN** 导入器 MUST 失败,并输出明确的 error(包含资产路径与原因)

#### Scenario: bundle 缺少 meta.json
- **WHEN** `.sog4d` bundle 内不存在 `meta.json`
- **THEN** 导入器 MUST 失败,并输出明确的 error(包含缺失文件名)

### Requirement: `meta.json` MUST be valid UTF-8 JSON with required top-level fields
`meta.json` MUST 为 UTF-8 编码的 JSON 文本.
`meta.json` MUST 包含以下顶层字段:
- `format`: MUST 为字符串 `"sog4d"`
- `version`: MUST 为整数,并用于 forward/backward compatibility
- `splatCount`: MUST 为正整数
- `frameCount`: MUST 为正整数
- `timeMapping`: MUST 为对象(细节由 `sog4d-sequence-encoding` 定义)
- `layout`: MUST 为对象(细节由 `sog4d-sequence-encoding` 定义)
- `streams`: MUST 为对象(细节由 `sog4d-sequence-encoding` 定义)

#### Scenario: meta.json 不是合法 JSON
- **WHEN** `meta.json` 无法被解析为 JSON
- **THEN** 导入器 MUST 失败,并输出明确的 error(包含解析失败原因)

#### Scenario: meta.json 缺少必需字段
- **WHEN** `meta.json` 缺少任意一个必需的顶层字段(例如缺少 `streams`)
- **THEN** 导入器 MUST 失败,并输出明确的 error(包含缺失字段名)

### Requirement: Bundle MUST contain all files referenced by `meta.json`
对 `meta.json` 中声明的每个 stream,其引用的文件路径 MUST 在 bundle 内存在.
导入器 MUST 在导入期做完整性校验,避免运行期才发现缺失资源.

#### Scenario: meta 引用的文件不存在
- **WHEN** `meta.json` 引用了 `frames/00000/position_hi.webp`,但 bundle 内缺少该文件
- **THEN** 导入器 MUST 失败,并输出明确的 error(包含缺失文件路径)

### Requirement: Forward compatibility for unknown fields and extra files
导入器 MUST 忽略 `meta.json` 中未知的字段(不应因未知字段而失败).
导入器 MUST 忽略 bundle 内未被 `meta.json` 引用的额外文件.

#### Scenario: meta.json 包含未知字段
- **WHEN** `meta.json` 包含未来版本新增的字段(例如 `streams.newFeatureX`)
- **THEN** 当前版本导入器仍 MUST 能完成导入(在不使用该字段的前提下)

#### Scenario: bundle 内包含多余文件
- **WHEN** bundle 内包含未被 `meta.json` 引用的文件(例如 `debug_dump.txt`)
- **THEN** 导入器 MUST 仍能完成导入,且不应产生 error

