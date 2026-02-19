## 1. Spec Finalization

- [x] 1.1 决定 SH rest palette 的最终承载方式(centroids.webp vs centroids.bin),并把选择同步回 `sog4d-sequence-encoding`
- [x] 1.2 明确 `{frame}` 路径模板的格式(是否零填充),并把规则同步回 `sog4d-sequence-encoding`
- [x] 1.3 决定 opacity 是否并入 `sh0.webp` alpha(对齐 SOG),并相应更新 `sog4d-sequence-encoding` 与 `4dgs-keyframe-motion`

## 2. Data Model (Unity Runtime)

- [x] 2.1 新增 `GsplatSequenceAsset` ScriptableObject(包含 SplatCount/FrameCount/SHBands/TimeMapping/Bounds,以及 per-frame streams 引用)
- [x] 2.2 新增 `GsplatSequenceTimeMapping` 数据结构(uniform/explicit),并提供 CPU 侧 `(i0,i1,a)` 评估函数
- [x] 2.3 新增 `GsplatInterpolationMode`(Nearest/Linear),并与 specs 的插值语义对齐

## 3. Importer (Unity Editor)

- [x] 3.1 新增 `.sog4d` ScriptedImporter 骨架(失败时输出 actionable errors,并忽略未知字段)
- [x] 3.2 实现 ZIP 读取与 `meta.json` 解析,并按 `sog4d-container` 做完整性校验
- [x] 3.3 实现 streams 校验(必需 streams,layout 尺寸,frameCount/splatCount 一致性)
- [x] 3.4 选型并接入 WebP 解码依赖(至少 Editor 可用),并确保按"数据图"读取 byte(禁用 sRGB/压缩/mipmap)
- [x] 3.5 将 per-frame streams 打包为 `Texture2DArray`(或 chunked arrays),并作为 sub-assets 写入导入结果
- [x] 3.6 计算 `UnionBounds`(覆盖所有帧),并可选输出 `PerFrameBounds` 便于调试
- [x] 3.7 导入后自动生成可播放 prefab(挂 `GsplatSequenceRenderer`),并把该 prefab 设为 main object
- [x] 3.8 支持 `streams.sh.shNLabelsEncoding="delta-v1"`:
  - 校验 `shNDeltaSegments` 覆盖连续性(startFrame/FrameCount)
  - 校验 `shNLabelDeltaV1` header 与 meta 一致(splatCount/shNCount/segmentStartFrame)
  - 导入期把 delta 还原为 per-frame labels 纹理,以保持运行时随机访问与双帧采样简单性

## 4. Runtime Playback (Decode + Interpolate)

- [x] 4.1 新增 `GsplatSequenceRenderer` 组件(字段尽量对齐 `GsplatRenderer`: TimeNormalized/AutoPlay/Speed/Loop)
- [x] 4.2 让 `GsplatSequenceRenderer` 实现 `IGsplat`,并持有一个 `GsplatRendererImpl`(复用现有排序与渲染)
- [x] 4.3 新增 compute shader: 从 quantized textures 解码两帧,按 `(i0,i1,a)` 插值,写入 float structured buffers
- [x] 4.4 确保同一帧内 decode/sort/render 共享同一个 `TimeNormalized`(使用 this-frame 缓存值)
- [x] 4.5 支持 explicit time mapping 的二分查找,并覆盖重复时间点(`t[i1]==t[i0]`)的 `a=0` 分支
- [x] 4.6 支持 Nearest/Linear 两种插值模式,并提供可配置的默认值

## 5. SH Support

- [x] 5.1 实现 SH0(f_dc+opacity) 的解码与插值,并写入 `ColorBuffer`(f_dc+opacity)
- [x] 5.2 实现高阶 SH(rest) 的 palette+labels 解码与逐系数插值,并写入 `SHBuffer`
- [x] 5.3 当资源预算过高时,支持降级到 SH0-only(对齐 `4dgs-resource-budgeting`)

## 6. Resource Budgeting & Auto-Degrade

- [x] 6.1 扩展 GPU 资源估算: 把 `.sog4d` 的量化纹理,双帧访问窗口,解码中间 buffers 纳入计算
- [x] 6.2 新增自动降级策略: 关闭插值(回退 Nearest),并输出降级前后关键参数
- [x] 6.3 分配失败时 fail-fast: 记录资源类型/尺寸/format,并禁用渲染组件避免持续报错

## 7. Tooling (Exporter)

- [x] 7.1 新增/扩展离线工具: `time_*.ply` 序列 -> `.sog4d`(写 meta.json + 写属性图 + ZIP 打包)
- [x] 7.2 实现 position/scale/rotation/opacity/SH 的量化与编码,并可配置压缩质量(例如 codebook/palette size)
- [x] 7.3 增加一个 bundle 自检命令(校验 splatCount,frameCount,layout,range,labels 越界)
- [x] 7.4 实现 `sh0Codebook` 的生成(1D VQ/K-means/quantile),并支持 importance weighting(例如 `opacity * volume`)
- [x] 7.5 实现 `shN_centroids.bin` 的生成(高维 VQ/K-means),并支持 importance weighting
- [x] 7.6 支持 `shNLabelsEncoding="delta-v1"` 的输出:
  - 按 segment 生成 `baseLabelsPath`(WebP)与 `deltaPath`(二进制)
  - delta block 仅保存 changed `(splatId,label)` 列表,并强制 `splatId` 严格递增
  - segment 长度暴露为 exporter 参数(默认 50 帧/segment)
  - exporter 默认启用 `delta-v1`,并默认 `shNCount=8192`(可通过参数切换为 `"full"` 或降低 `shNCount`)

## 8. Tests, Docs, Samples

- [x] 8.1 新增 EditMode tests: meta.json 校验失败时的错误语义(缺字段/缺文件/layout 不足/labels 越界)
- [x] 8.2 新增最小 `.sog4d` 测试样例(少量 splats,少量帧),用于 importer 回归
- [x] 8.3 补充文档: `.sog4d` 结构,时间轴语义,插值规则,以及常见失败原因与修复建议
- [x] 8.4 新增 delta-v1 覆盖的 tests(至少包含):
  - segments 不连续/不覆盖(frameCount)时 fail
  - delta header mismatch(splatCount/shNCount/segmentStartFrame)时 fail
  - delta block 的 `splatId` 非递增/重复时 fail
  - `updateCount` 越界导致读溢出时 fail-fast

## 9. Future (Target A: Keep Runtime Compression Advantage)

- [x] 9.1 让 player build 可以直接加载 `.sog4d` bundle(运行时解包 ZIP + 解码 WebP),而不依赖 Editor 预处理产物
- [x] 9.2 设计并实现 frame chunk 的按需加载/释放,降低显存峰值并支持长序列
