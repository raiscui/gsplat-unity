# LATER_PLANS

## 2026-02-16 17:40:21 +0800
- (暂无)如果后续发现不适合立刻做的优化项,会记录在这里.

## 2026-02-17 00:18:02 +0800
- 候选优化: 让 VFX Graph 后端进一步接近主后端的“透明排序一致性”.
  - 思路: 在 VFX Graph 侧引入/复用 `OrderBuffer`,按主后端的 back-to-front 顺序进行绘制或采样.
  - 价值: 在中等规模下,对照渲染更接近,更少“半透明穿插”差异.

## 2026-02-17 10:45:02 +0800
- 候选优化: 提供“直接从训练 checkpoint 导出 `.splat4d`”的导出器(避免先落 PLY 再转换).
  - 目标仓库优先级:
    - `hustvl/4DGaussians`: 从 deformation network 直接采样多个 t,并用 keyframe 分段生成 time/duration/velocity.
    - `FreeTimeGsVanilla`: 直接读取训练后 splats 参数(means/scales/quats/opacities/sh0/times/durations/velocities),无须推导.
  - 价值:
    - 少一次磁盘中间文件,速度更快,也能保留更准确的 time 列表.

## 2026-02-17 11:25:45 +0800
- 更新: 已完成 FreeTimeGsVanilla checkpoint -> `.splat4d` 的直出导出器.
  - 落地点: `../FreeTimeGsVanilla/src/export_splat4d.py`
  - 仍待做: 4DGaussians 这类 deformation 方法的“从 checkpoint 直接导出 `.splat4d`”(目前只能走 PLY 序列采样路线).

## 2026-02-17 21:08:36 +0800
- 候选改进: 恢复 VFX binder 在 Inspector 里的“类型化绑定 UI”.
  - 背景: 我们为了兼容部分 Unity/VFX Graph 版本,移除了 `GsplatVfxBinder` 里的 `[VFXPropertyBinding(...)]`.
  - 方向:
    - 方案A: 额外做一个 Editor-only 的 asmdef,只在存在对应 API 时为字段提供更友好的自定义 inspector/PropertyDrawer.
    - 方案B: 做一层可选的编译宏,在确认 `VFXPropertyBinding` 可用的版本中再启用该 attribute(其它版本保持无 attribute 的兼容实现).

## 2026-02-18 12:48:57 +0800
- 候选改进: `Tools~/Splat4D/ply_sequence_to_splat4d.py` 支持直接输入单个 PLY 文件(例如 `--input-ply`).
  - 背景: 目前导出单帧 `.splat4d`(静态快照)需要创建临时目录并放入 1 个 `.ply`.
  - 价值: 用法更直观,也更适合写成批处理脚本.
  - 设计要点:
    - 继续保留现有 `--input-dir` 行为不变.
    - README 补一段 "导出单帧快照(velocity=0,time=0,duration=1)" 的示例命令.

## 2026-02-18 18:20:48 +0800
- 候选扩展(先记录,不在本轮实现): 更贴近 DualGS 的 "four codebooks" 拆分
  - 动机:
    - DualGS/DynGsplat 会把 SH 按阶拆成 `rgb/sh_1/sh_2/sh_3` 分别做 VQ(codebook).
    - 这样每个 codebook 的维度更低,训练更容易,也可能提升在固定码率下的质量.
  - 代价:
    - labels 可能从 "1 个 u16/gaussian/frame" 变成 "3 个 u16/gaussian/frame"(SH1/2/3 各一份),体积会明显变大.
  - 如果要做:
    - 需要扩展 `.sog4d` streams,为 sh1/sh2/sh3 分别定义 centroids+labels(以及各自的 delta).
    - 并补一套 importer/runtime decode 的实现与资源预算估算.

## 2026-02-19 11:55:25 +0800
- 候选扩展: 为 Windows/Linux Editor 也内置 WebP 解码器(跑满跨平台回归测试).
  - 背景:
    - 当前我们在 macOS Editor 通过 `libGsplatWebpDecoder.dylib` 解决了 WebP 数据图解码.
    - 在其它平台,如果宿主 Unity 的 `ImageConversion.LoadImage` 仍不支持 WebP,对应 tests 仍会被跳过.
  - 方向:
    - 提供 `Editor/Plugins/Windows/*.dll` 与 `Editor/Plugins/Linux/*.so` 版本的 `GsplatWebpDecoder`.
    - 统一由 `Editor/GsplatWebpNative.cs` 做 P/Invoke(按平台分发二进制即可).

## 2026-02-21 15:34:59 +0800
- 候选补强: 增加 `.sog4d` v2 的最小测试数据与 importer 回归用例.
  - 背景:
    - 当前 `Tests/Editor/Sog4DTestData/` 只有 v1(delta-v1) 的最小 bundle.
    - 但实现已经支持 `meta.json.version=2` 的 sh1/sh2/sh3 per-band 编码与 per-band deltaSegments.
  - 目标:
    - 提供一份 `minimal_valid_v2_*.sog4d.zip` 测试数据.
    - 覆盖 per-band centroids size 校验,labels(full/delta-v1) 两条路径.
  - 价值:
    - 防止未来改动导致 v2 路径 silent break.

## 2026-02-22 15:20:43 +0800
- 候选改进: 同步修复 FreeTimeGsVanilla 的 `.sog4d` exporter(meta.json schema 对齐本包).
  - 背景:
    - 已发现某些 exporter 版本缺少 `meta.format`,并把 Vector3 写成 `[[x,y,z]]`,导致 Unity importer 解析失败.
    - 目前可以用本包 `Tools~/Sog4D/ply_sequence_to_sog4d.py normalize-meta` 救火修复既有 bundle.
  - 目标:
    - exporter 直接输出规范形态:
      - `meta.format="sog4d"`
      - `streams.position.rangeMin/rangeMax` 与 `streams.scale.codebook` 输出为 `{x,y,z}` 数组.
  - 价值:
    - 新产物开箱即用,避免在团队里传播“先导出再修 meta”的隐性流程.

## 2026-02-23
- 候选改进: 在 SRP(URP/HDRP) 下增加一个高级开关,允许禁用 `beginCameraRendering` 驱动排序.
  - 动机:
    - 目前我们把排序统一搬到了 SRP 相机回调,默认不再需要 CustomPass/RendererFeature.
    - 但某些项目可能希望精确控制“排序发生的注入点”(例如严格卡在 Transparent 前,或与其它自定义 pass 协调).
  - 方向:
    - 增加 `GsplatSettings.UseLegacySrpInjector`(或类似命名)并配套文档,开启时让 `GsplatHDRPPass`/`GsplatURPFeature` 恢复生效.

- 候选改进: Play 模式 SceneView 聚焦判断当前使用 `SceneView.lastActiveSceneView`.
  - 风险:
    - 多 SceneView 窗口/多显示器时,`lastActiveSceneView` 可能不是正在绘制的那个 camera.
  - 方向:
    - 遍历 `SceneView.sceneViews` 找到 `hasFocus` 且 `sceneView.camera == cam` 的那一个,提高判断准确性.
