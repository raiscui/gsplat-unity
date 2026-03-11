## MODIFIED Requirements

### Requirement: Unity importer MUST create a playable sequence asset from `.sog4d`
系统 MUST 提供一个 Unity ScriptedImporter 来导入 `.sog4d`.
导入成功后,导入器 MUST 生成一个可被播放的资产对象,并且该资产 MUST 能被运行时渲染组件引用.

该资产 MUST 暴露以下基础元数据:
- `SplatCount`
- `FrameCount`
- `SHBands`
- `TimeMapping`(uniform 或 explicit)

对于 `frameCount = 1` 的 bundle:
- 导入器 MUST 继续生成序列资产,而不是回退成其它资产类型
- 该资产 MUST 仍可被现有 `GsplatSequenceRenderer` 等序列运行时组件直接引用

#### Scenario: 导入成功后资产可被引用
- **WHEN** 用户把 `.sog4d` 拖入 `Assets/`
- **THEN** Unity 中 MUST 生成一个可被组件字段引用的资产对象

#### Scenario: 单帧 bundle 导入后仍是可播放序列资产
- **WHEN** 用户导入一个 `frameCount = 1` 的 `.sog4d` bundle
- **THEN** Unity 中 MUST 生成一个可被 `GsplatSequenceRenderer` 引用的序列资产
- **AND** 导入器 MUST NOT 把它降级为静态 `GsplatAsset`

### Requirement: Importer MUST store per-frame streams in a form suitable for interpolation at runtime
由于用户要求所有核心属性逐帧可插值,导入器 MUST 输出一种运行时可高效采样的资源形态.
该形态 MUST 支持在任意 `TimeNormalized` 下同时访问相邻两帧的数据(用于插值).

导入器至少 MUST 支持以下一种输出策略:
- 把每个 stream 打包为 `Texture2DArray`,其中 layer 对应 frameIndex
- 或把 frames 分块为多个 `Texture2DArray` chunk,并在资产中记录 chunk 映射

对于 `frameCount = 1` 的 bundle:
- 导入器 MUST 允许每个 per-frame stream 仅生成 1 个 layer 的资源形态
- 导入器 MUST NOT 因缺少“第二帧 layer”而失败
- 运行时所需的单帧退化语义由 `4dgs-keyframe-motion` 定义

#### Scenario: 运行时可读取相邻两帧
- **WHEN** 运行时 `TimeNormalized` 落在 frame 10 与 frame 11 之间
- **THEN** 系统 MUST 能在同一帧内读取 frame 10 与 frame 11 的 stream 数据(不应要求重新导入或重建资源)

#### Scenario: 单帧 bundle 不需要合成第二个 layer
- **WHEN** 导入器处理一个 `frameCount = 1` 的 `.sog4d` bundle
- **THEN** 每个 per-frame stream MAY 只包含 1 个 layer
- **AND** 导入器 MUST NOT 通过伪造第二个 layer 才允许导入成功

## ADDED Requirements

### Requirement: Importer MUST keep one-frame bundles on the normal `.sog4d` sequence path
导入器 MUST 把单帧 `.sog4d` 视为 `.sog4d` 序列能力的一种合法边界情况.
因此导入器 MUST:

- 继续执行正常的 `meta.json` / stream / bounds 校验
- 继续生成 `.sog4d` 对应的序列资产与相关子资产
- 保持与多帧 bundle 相同的错误语义和资源命名口径

导入器 MUST NOT 因为 `frameCount = 1` 而要求用户改走 `.ply` importer 或其它替代工作流.

#### Scenario: 单帧 `.sog4d` 不要求改走其它 importer
- **WHEN** 用户把一个合法的单帧 `.sog4d` 拖入 Unity `Assets/`
- **THEN** 当前 `.sog4d` importer MUST 直接完成导入
- **AND** 系统 MUST NOT 提示用户改用 `.ply` importer 才能正常显示

### Requirement: The real repository single-frame fixture MUST be importable and display-verifiable in the current Unity project
系统 MUST 把由 `Assets/Gsplat/ply/s1-point_cloud.ply` 转出的单帧 `.sog4d` 视为本 change 的真实 Unity 验收样例.

这里的前置条件是:

- `.ply -> .sog4d` 转换已经由离线脚本工具完成
- Unity 只负责导入这个 `.sog4d` 产物并验证显示

对于这份样例对应的 `.sog4d` bundle:

- 导入器 MUST 在当前 Unity 工程中完成正常导入
- 系统 MUST 生成正常的序列资产与可实例化主对象
- 该主对象 MUST 可被放入场景并完成显示验证

系统 MUST NOT 只在最小测试夹具上满足“可导入”,却让这份真实样例停留在“导入成功但无法实际显示”的状态.

#### Scenario: The real repository one-frame fixture imports and can be display-verified
- **WHEN** 用户把脚本工具由 `Assets/Gsplat/ply/s1-point_cloud.ply` 转出的合法单帧 `.sog4d` 放入当前 Unity 工程的 `Assets/`
- **THEN** 当前 `.sog4d` importer MUST 成功导入该 bundle
- **AND** 系统 MUST 生成可被 `GsplatSequenceRenderer` 引用与实例化的主对象/序列资产
- **AND** 该对象 MUST 能在场景中完成实际显示验证
