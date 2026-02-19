# Capability: sog4d-unity-importer

## Purpose
定义 Unity 侧 `.sog4d` ScriptedImporter 的导入行为与产物形态.
目标是让 `.sog4d` 导入后可直接播放,并在导入期提供明确的可行动错误.

## Requirements

### Requirement: Unity importer MUST create a playable sequence asset from `.sog4d`
系统 MUST 提供一个 Unity ScriptedImporter 来导入 `.sog4d`.
导入成功后,导入器 MUST 生成一个可被播放的资产对象,并且该资产 MUST 能被运行时渲染组件引用.

该资产 MUST 暴露以下基础元数据:
- `SplatCount`
- `FrameCount`
- `SHBands`
- `TimeMapping`(uniform 或 explicit)

#### Scenario: 导入成功后资产可被引用
- **WHEN** 用户把 `.sog4d` 拖入 `Assets/`
- **THEN** Unity 中 MUST 生成一个可被组件字段引用的资产对象

### Requirement: Importer MUST validate `meta.json` and all required streams before creating assets
导入器 MUST 在创建任何可播放资产之前完成校验:
- `meta.json` 的顶层字段校验(见 `sog4d-container`)
- `streams` 的必需项校验(见 `sog4d-sequence-encoding`)
- 所有被引用文件路径存在性校验

校验失败时,导入器 MUST 失败,并输出明确的 error.

#### Scenario: 缺少某帧的 position_hi.webp
- **WHEN** `streams.position.hiPath` 指向的某一帧文件缺失
- **THEN** 导入器 MUST 失败,且不应生成半成品资产

### Requirement: Importer MUST treat WebP images as data, not color
导入器 MUST 将 `.sog4d` 的 WebP 图像视为"数据图",而不是颜色图.
导入器 MUST 确保解码得到的 byte 值与文件内容一致.
导入器 MUST 禁止以下会改变 byte 的处理:
- sRGB/gamma 颜色空间转换
- 纹理压缩导致的有损变形
- 平台特定的重采样或 mipmap 生成

#### Scenario: 数据图不经过 sRGB 变换
- **WHEN** 导入器把 `rotation.webp` 解码为 Unity `Texture2D`
- **THEN** 解码得到的 RGBA byte MUST 与文件解码输出一致,不应因 sRGB 设置发生变化

### Requirement: Importer MUST store per-frame streams in a form suitable for interpolation at runtime
由于用户要求所有核心属性逐帧可插值,导入器 MUST 输出一种运行时可高效采样的资源形态.
该形态 MUST 支持在任意 `TimeNormalized` 下同时访问相邻两帧的数据(用于插值).

导入器至少 MUST 支持以下一种输出策略:
- 把每个 stream 打包为 `Texture2DArray`,其中 layer 对应 frameIndex
- 或把 frames 分块为多个 `Texture2DArray` chunk,并在资产中记录 chunk 映射

#### Scenario: 运行时可读取相邻两帧
- **WHEN** 运行时 `TimeNormalized` 落在 frame 10 与 frame 11 之间
- **THEN** 系统 MUST 能在同一帧内读取 frame 10 与 frame 11 的 stream 数据(不应要求重新导入或重建资源)

### Requirement: Importer MUST provide bounds data for correct culling
导入器 MUST 提供用于剔除的 bounds 数据,避免序列播放时出现剔除闪烁.
导入器 MUST 输出:
- `UnionBounds`: 覆盖所有帧的保守 bounds
- (可选) `PerFrameBounds[frameIndex]`: 便于调试与更精细的剔除策略

#### Scenario: UnionBounds 覆盖所有帧
- **WHEN** 某个 splat 在 frame 0 与 frame 19 之间移动到静态 bounds 外
- **THEN** 系统仍 MUST 正确渲染,不应因剔除导致闪烁或消失

