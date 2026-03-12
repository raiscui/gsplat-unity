# EPIPHANY_LOG: 单帧普通 3DGS `.ply -> .splat4d` OpenSpec 立项

## 2026-03-11 23:18:00 +0800 主题: 单帧 `.splat4d` 的动态 SH 初始化与播放 no-op 存在不对称

### 发现来源
- 只读探索 `Runtime/GsplatRenderer.cs` 的单帧 `.splat4d` 播放路径时发现

### 核心问题
- `TryInitShDeltaRuntime()` 目前在 `ShFrameCount <= 0` 时才跳过初始化
- 但 `TryApplyShDeltaForTime()` 在 `frameCount <= 1` 时就直接 no-op
- 这意味着:
  - 若未来出现 `ShFrameCount == 1` 且仍带 delta 元数据的 `.splat4d v2`
  - runtime 会先初始化“动态 SH”资源
  - 后续播放又永远不会真正推进

### 为什么重要
- 这条路径和“单帧资产应稳态退化成静态资产”的语义不完全对齐
- 即使当前未必会造成功能错误,也容易让后续维护者误判:
  - 为什么单帧资产还在初始化动态 SH runtime

### 未来风险
- 若后续有人新增单帧 v2 exporter 或夹具,可能无意命中这条路径
- 那时最先出现的症状未必是画面错误,也可能是:
  - 多余 buffer 初始化
  - 多余 warning
  - “看起来像动态资产”的误解

### 当前结论
- 这是静态阅读得到的候选风险,不是已验证 bug
- 若未来需要补最小 runtime guard,首选检查点就是:
  - `Runtime/GsplatRenderer.cs` 的 `TryInitShDeltaRuntime()`

### 后续讨论入口
- `Runtime/GsplatRenderer.cs`
- `Tests/Editor/GsplatSplat4DImporterDeltaV1Tests.cs`
- `Tests/Editor/GsplatVisibilityAnimationTests.cs`

## 2026-03-12 00:05:00 +0800 主题: Unity 6000.3.8f1 的 `-runTests` 在当前项目里去掉 `-quit` 才稳定产出 XML

### 发现来源
- 对 `splat4d-single-frame-ply-support` 做 Unity CLI EditMode 回归验证时发现

### 核心问题
- 同一条 `-runTests` 命令:
  - 带 `-quit`:
    - `exit code 0`
    - 但 `-testResults` XML 不生成
    - 日志里也看不到 TestRunner 真正开始执行
  - 去掉 `-quit`:
    - XML 正常生成
    - 日志明确出现:
      - `Test run completed. Exiting with code 0 (Ok).`

### 为什么重要
- 这类现象很容易被误判成:
  - 测试程序集坏了
  - 或者 test filter 写错了
- 但真实问题其实是 CLI 退出时机

### 未来风险
- 后续再做 batch EditMode 回归时,如果忘了这一点,会反复浪费时间在“为什么没有 xml”上

### 当前结论
- 对当前项目 + 当前 Unity 版本:
  - 优先使用“不带 `-quit` 的 `-runTests`”做 CLI 测试
- 这个结论是当前项目的动态验证结果,不是纯静态猜测

### 后续讨论入口
- `/tmp/gsplat_single_frame_editmode.log`
- `/tmp/gsplat_single_frame_editmode_noquit.log`
- `/tmp/gsplat_single_frame_editmode_noquit.xml`

## 2026-03-12 12:24:25 +0800 主题: 单帧 `.splat4d v2 + delta-v1` 的动态 SH 初始化路径已被动态证据证实可达

### 发现来源
- 在 Unity 当前会话中,对临时构造的 `frameCount=1` `.splat4d v2 + delta-v1` 资产做最小 Editor probe

### 核心问题
- 旧结论里那条“不对称路径”现在已经不是静态猜测了:
  - `TryInitShDeltaRuntime()` 会真实初始化动态 SH runtime
  - `TryApplyShDeltaForTime()` 又会因为 `frameCount <= 1` 永远 no-op

### 为什么重要
- 这意味着未来只要有人导入或导出单帧 delta-v1 资产,就会真实命中:
  - 多余的 centroids buffer / segment runtime 初始化
  - “看起来像动态 SH 资产,但实际上永远不播放”的语义混乱

### 未来风险
- 若后续补单帧 SH exporter 或接入外部 `.splat4d v2` 资产,这个不对称会从“边角”变成稳定契约问题
- 后续维护者可能误以为单帧 delta-v1 已被完整支持,从而在别处叠加错误假设

### 当前结论
- 这已经是动态证据支撑的可触发路径
- 但当前还不能直接表述成“用户已遭遇的可见渲染 bug”
- 更准确的说法是:
  - 真实可达的 runtime 语义不对齐
  - 首选候选修复点仍是 `Runtime/GsplatRenderer.cs` 的 `TryInitShDeltaRuntime()`
  - 候选 guard 方向是把 `ShFrameCount <= 1` 直接视为无需初始化动态 SH runtime

### 后续讨论入口
- `Runtime/GsplatRenderer.cs`
- `Editor/GsplatSplat4DImporter.cs`
- `Tests/Editor/GsplatSplat4DImporterDeltaV1Tests.cs`
- `notes__splat4d_single_frame_support.md`

## 2026-03-12 18:00:00 +0800 主题: 单帧 `.splat4d v2` 最干净的 SH 语义不是 delta-v1,而是 full labels

### 发现来源
- 为 `s1-point_cloud.ply` 落地真实 `.splat4d v2 + SH3` exporter 时发现

### 核心问题
- 单帧资产如果硬写成 `delta-v1`,会天然落入:
  - 初始化了动态 SH runtime
  - 但播放阶段 `frameCount <= 1` 永远 no-op
- 这会把“静态资产”包装成“看起来像动态,其实永远不动”的奇怪语义

### 为什么重要
- 对单帧高质量资产而言,真正需要的是“把高阶 SH 保留下来”,不是强行构造一套动态标签演化机制
- `full labels` 更接近资产本意:
  - 只有一套 base labels
  - 没有 delta segment
  - importer/runtime 侧也更容易理解和维护

### 当前结论
- 单帧 `.ply -> .splat4d v2 + SH3` 的首选做法是:
  - `labelsEncoding = full`
  - `frameCount = 1`
  - `ShFrameCount = 0`
- 这不是对多帧 `.splat4d v2` 的最终结论
- 但对“单帧高质量”这条路径,它已经是更干净的契约

### 后续讨论入口
- `Tools~/Splat4D/ply_sequence_to_splat4d.py`
- `Editor/GsplatSplat4DImporter.cs`
- `Runtime/GsplatRenderer.cs`

## 2026-03-12 18:52:00 +0800 主题: Unity 验证口径必须和 runtime 口径一致,否则验证器自己会制造假阳性

### 发现来源
- 对 `s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d` 做 Unity batch verify 时发现

### 核心问题
- runtime 在动态 SH 初始化里把 `null` 和 `Length==0` 都视为“没有 delta-v1 数据”
- 但 batch verify 之前只要看到 `Sh*DeltaSegments != null` 就直接报错

### 为什么重要
- 这会把“序列化层可能把 null 归一为 empty array”的无害情况,误报成 importer/runtime bug
- 长期看,这类验证口径不一致会严重污染排查过程

### 当前结论
- 对 `Sh*DeltaSegments` 这类可选数组字段,验证层应采用和 runtime 一致的“可用元素数”语义
- 更准确的判断是:
  - `segmentCount > 0` 才算真的带了 delta-v1 数据

### 后续讨论入口
- `Editor/BatchVerifySplat4DImport.cs`
- `Runtime/GsplatRenderer.cs`
- `Tests/Editor/GsplatSplat4DImporterDeltaV1Tests.cs`

## 2026-03-12 19:55:00 +0800 主题: 比对 SuperSplat 观感前,必须先隔离 Unity 场景级后处理,否则很容易把场景问题误判成高斯算法问题

### 发现来源
- 对 `s1_point_cloud_v2_sh3_full_k8192_f32_20260312` 做 SuperSplat 对照分析时发现

### 核心问题
- 当前 Unity 场景是 HDRP,且全局 Volume 开着:
  - `Exposure`
  - `Tonemapping`
  - `ColorCurves`
  - `FilmGrain`
  - `LiftGammaGain`
  - `Bloom`
- 如果直接拿它和 SuperSplat 默认观察环境比,很容易把 scene post FX 的影响误记到 splat shader 头上

### 为什么重要
- 这类误判会把后续优化方向带偏:
  - 明明应该先做纯观察对照
  - 却可能一头扎进高斯核或 SH 公式里反复调参

### 当前结论
- 当前已经确认两件事同时存在:
  - Unity 缺少 PlayCanvas 的 `aaFactor` AA compensation 链路
  - 当前对照环境又混入了 HDRP scene post FX
- 因此后续若要继续,必须先拆分变量再比较

### 后续讨论入口
- `Runtime/Shaders/Gsplat.hlsl`
- `Runtime/Shaders/Gsplat.shader`
- `Runtime/GsplatSettings.cs`
- `Assets/OutdoorsScene.unity`

## 2026-03-12 20:34:00 +0800 主题: `aaFactor` 链路存在不等于应该默认开启,参数层默认值和适用条件同样属于算法契约的一部分

### 发现来源
- 对 `GSPLAT_AA` 做第一版实现后,动态截图出现整体显著变暗
- 随后回查 PlayCanvas `gsplat-params.js`

### 核心问题
- 如果只看 shader chunk,很容易得到"Unity 少了一条链路,所以应该补上并默认开启"的结论
- 但参数层源码明确写了:
  - `antiAlias` 默认 `false`
  - 仅适用于带 anti-aliasing 训练/导出的 splat 数据

### 为什么重要
- 这说明算法契约不能只读 shader 数学本身
- 还要把参数层默认值、适用数据条件、运行时开关一起看
- 否则很容易把"可选补偿"误改成"全局默认行为"

### 当前结论
- `GSPLAT_AA` 已在 Unity 中补成 renderer 级开关
- 不应默认全局开启
- 后续如果继续做质量对照,应优先记录当前资产是否属于 AA-trained / AA-exported 数据

### 后续讨论入口
- `Runtime/Shaders/Gsplat.shader`
- `Runtime/GsplatRenderer.cs`
- `Runtime/GsplatSequenceRenderer.cs`
- `/tmp/playcanvas-engine.V7a3ug/src/scene/gsplat-unified/gsplat-params.js`

## 2026-03-12 19:55:00 +0800 主题: 做视觉对照时,旧的 scene 观察前提会过期,必须重新读取现场状态

### 发现来源
- 对 `EnableFootprintAACompensation` 第二轮复核时,重新读取了 `mcpforunity://scene/volumes`

### 核心问题
- 上一轮分析时,当前 HDRP scene 里确实读到过多个 Volume effect
- 但本轮重新读取后,scene 已经变成 `Found 0 volume(s)`
- 如果继续沿用旧前提,就会把已经被用户关闭的 scene 变量继续算进当前结论

### 为什么重要
- 视觉问题非常容易受现场状态影响
- 而这类状态会被用户在 Editor 里随时修改
- 如果不重新读取现场,就会把过期证据误当当前事实,导致后续排查方向跑偏

### 当前结论
- 以后每次做 Unity 观感对照前,都应重新读取 scene / camera / volume / target component 的当前状态
- 不要复用上一轮的现场假设,除非这轮重新验证过

### 后续讨论入口
- `mcpforunity://scene/volumes`
- `mcpforunity://scene/cameras`
- `mcpforunity://scene/gameobject/-193818/component/GsplatRenderer`
