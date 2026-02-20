# ERRORFIX

## 2026-02-17 21:08:36 +0800: 修复 VFXPropertyBinding 缺失导致的编译失败

### 现象
- Unity 编译报错:
  - `Runtime/VFX/GsplatVfxBinder.cs(...): error CS0246: The type or namespace name 'VFXPropertyBinding' could not be found`
  - `Runtime/VFX/GsplatVfxBinder.cs(...): error CS0246: The type or namespace name 'VFXPropertyBindingAttribute' could not be found`

### 本质(根因)
- `GsplatVfxBinder` 参考 SplatVFX 的写法,在 `ExposedProperty` 字段上使用了 `[VFXPropertyBinding(...)]` 标注.
- 但在部分 Unity / Visual Effect Graph 版本中:
  - `VFXPropertyBinding` 这个 attribute 并不存在,或不在 Runtime asmdef 可引用的程序集里.
  - 导致只要启用 VFX 后端,就会直接编译失败.
- 同时,该 attribute 只影响 Inspector 的绑定 UI 体验.
  - 对运行时实际绑定并非必需,因为 binder 内部已经显式调用 `Has*/Set*` 完成校验与写入.

### 修复
- 修改 `Runtime/VFX/GsplatVfxBinder.cs`:
  - 移除所有 `[VFXPropertyBinding(...)]` 标注.
  - 保留 `[SerializeField]` 以维持可序列化与 Inspector 可编辑.
  - 增加注释解释跨版本兼容原因.

### 验证
- 代码侧验证:
  - 仓库内已无 `VFXPropertyBinding` 的类型引用(仅在注释中提及).
  - binder 运行时绑定逻辑仍走 `IsValid/UpdateBinding` 的 `Has*/Set*` 路径,不依赖该 attribute.
- 运行时验证(需要 Unity 环境):
  - 在 Unity 中重新导入包后应不再出现上述 CS0246 错误.

## 2026-02-18 14:05:09 +0800: 修复 `GsplatVfxBinder` 漏配 `VfxComputeShader` 导致的运行时报错(Inspector 自动回填)

### 现象
- Unity Console 输出 error(通常在 Play 后由 `VFXPropertyBinder` 驱动触发):
  - `[Gsplat][VFX] GsplatVfxBinder 缺少 VfxComputeShader. 请在 Inspector 中指定为 Packages/wu.yize.gsplat/Runtime/Shaders/GsplatVfx.compute`

### 本质(根因)
- `GsplatVfxBinder.UpdateBinding` 依赖辅助 compute shader 生成:
  - `AxisBuffer`(由 rotation+scale 得到 3 个轴向向量).
  - 4D 动态 `VfxPosition/VfxColor` buffers(把 time/duration + TimeNormalized 语义烘焙进结果).
- 当用户手工搭建 VFX Graph 工作流时,`VfxComputeShader` 字段默认是空引用.
  - 这属于“可自愈的配置缺失”,不应该等到运行时才报错.

### 修复
- 修改 `Runtime/VFX/GsplatVfxBinder.cs`:
  - 增加 Editor-only 自动回填:
    - 在 `Reset/OnValidate/OnEnable` 中检测 `VfxComputeShader==null` 时,
      自动从 `Packages/wu.yize.gsplat/Runtime/Shaders/GsplatVfx.compute` 加载并赋值.
    - 严格使用 `#if UNITY_EDITOR` 包裹,避免 Player 构建依赖 `UnityEditor`.
  - 缺失报错提示改为复用同一路径常量,避免文案与实际默认路径漂移.

### 验证
- 代码侧验证:
  - 新增 EditMode 回归测试 `Tests/Editor/GsplatVfxBinderTests.cs`.
  - `Samples~/VFXGraphSample/README.md` 的手工搭建说明已同步为“默认会自动填充,为空再手动指定”.

## 2026-02-18 14:21:29 +0800: 修复 `GsplatVfxBinderTests` 编译失败(CS0012/CS1061,避免硬依赖 VFX Runtime)

### 现象
- Unity 编译报错:
  - `Tests/Editor/GsplatVfxBinderTests.cs`: error CS1061: `GsplatVfxBinder` 不包含 `enabled`
  - `Tests/Editor/GsplatVfxBinderTests.cs`: error CS0012: `VFXBinderBase` 所在程序集未被引用(`Unity.VisualEffectGraph.Runtime`)

### 本质(根因)
- `Tests/Editor/GsplatVfxBinderTests.cs` 之前直接强类型引用 `Gsplat.VFX.GsplatVfxBinder`.
- Unity asmdef 不会自动带上传递依赖.
  - 当测试程序集没有显式引用 `Unity.VisualEffectGraph.Runtime` 时,
    编译器无法解析 `GsplatVfxBinder` 的基类 `VFXBinderBase`,
    进而导致 `Behaviour.enabled` 解析失败,出现 CS1061/CS0012 连锁报错.

### 修复
- 修改 `Tests/Editor/GsplatVfxBinderTests.cs`:
  - 改为反射获取 binder 类型 + `Behaviour.enabled` 触发生命周期,
    避免对 `Unity.VisualEffectGraph.Runtime` 的编译期硬依赖.
  - 当 binder 类型不存在(未安装/未启用 VFX Graph)时,测试直接 `Assert.Ignore`.

### 验证
- 使用 Unity MCP 触发一次 `refresh_unity` 编译并读取 Console:
  - 当前未再出现该组 CS0012/CS1061 编译错误.

## 2026-02-18 15:15:11 +0800: 修复 VFX 后端无法随 `TimeNormalized` 播放 4DGS 动画(只在按 Play 时更新)

### 现象
- VFX Graph 后端:
  - 能显示某一帧时刻的 4DGS.
  - 但 `GsplatRenderer.TimeNormalized` 连续变化时,画面不更新.
  - 只有手动触发 `VisualEffect.Play()`/重新播放时,画面才会刷新到当前时刻.

### 本质(根因)
- `VFXPropertyBinder` 本身每帧都会调用 binder 的 `UpdateBinding`.
  - 因此不是 binder “没更新”.
- 根因在 VFX Graph 资产结构:
  - `Samples~/VFXGraphSample/VFX/Splat.vfx` 之前缺失 Update Context.
  - `PositionBuffer/AxisBuffer/ColorBuffer` 只在 Initialize 阶段采样并写入粒子属性.
  - 粒子初始化后属性冻结,后续 buffer 更新无法影响已存在粒子,表现为“只有按 Play 才更新”.

### 修复
- 修改 `Samples~/VFXGraphSample/VFX/InitializeSplat.vfxblock`:
  - `m_SuitableContexts: 2 -> 10`(Init|Update),允许该 subgraph block 在 Update 阶段复用.
- 修改 `Samples~/VFXGraphSample/VFX/Splat.vfx`:
  - 新增 `VFXBasicUpdate` Context(Spawn -> Init -> Update -> Output).
  - 在 Update Context 中复用 `InitializeSplat` block,每帧把 position/color/alpha/axis 从 buffer 写回粒子属性.

### 验证
- 使用 Unity MCP 触发刷新编译并读取 Console:
  - 未出现 VFX Graph 资产导入/编译错误.
- 运行时验证(需要 Unity 场景):
  - 播放后拖动 `GsplatRenderer.TimeNormalized` 应实时更新.
  - 启用 `AutoPlay` 应能看到 4DGS 动画持续播放.

## 2026-02-18 15:35:59 +0800: 修复 Unity 工程内 Sample copy 不自动更新导致的持续复现

### 现象
- 我们已经修复了包内 sample:
  - `Packages/wu.yize.gsplat/Samples~/VFXGraphSample/VFX/Splat.vfx`
- 但在你的 Unity 工程里仍可能继续看到旧现象:
  - “拖动 TimeNormalized 不更新,只有按 `VisualEffect.Play()` 才刷新一帧”.

### 本质(根因)
- Unity 的 Package Samples 会被复制到工程的 `Assets/Samples/...`.
  - 这份 copy **不会**随着 `Packages/.../Samples~` 自动更新.
- 你的工程内存在旧版 sample copy:
  - `Assets/Samples/Gsplat/1.1.2/VFX Graph Sample(SplatVFX style)/VFX/Splat.vfx`
  - 该版本缺失 Update Context,因此会继续复现旧问题.

### 修复
- 直接把包内 sample 的关键修复同步覆盖到工程内的 sample copy(仅 3 个文件):
  - `Assets/Samples/Gsplat/1.1.2/VFX Graph Sample(SplatVFX style)/VFX/Splat.vfx`
  - `Assets/Samples/Gsplat/1.1.2/VFX Graph Sample(SplatVFX style)/VFX/InitializeSplat.vfxblock`
  - `Assets/Samples/Gsplat/1.1.2/VFX Graph Sample(SplatVFX style)/README.md`

### 验证
- 使用 Unity MCP 触发 `refresh_unity` 并读取 Console:
  - 未出现新的导入/编译错误.

## 2026-02-18 15:48:48 +0800: 修复外部包目录 `gsplat-unity` 仍旧缺失关键修复(避免未来“重新导入 Sample 又坏了”)

### 现象
- Unity 工程的 `Packages/manifest.json` 可能引用外部包目录:
  - `wu.yize.gsplat`: `file:/Users/cuiluming/local_doc/l_dev/my/unity/gsplat-unity`
- 即使我们修复了工程内 `Assets/Samples/...` copy,
  外部包本身如果还是旧实现,后续重新导入 Sample 或复用包内 sample 时仍会把问题带回来.

### 本质(根因)
- 同一份包在你磁盘上存在两份来源:
  - 工程内 `Packages/wu.yize.gsplat`(本次修复的主要落点)
  - 外部目录 `/Users/cuiluming/local_doc/l_dev/my/unity/gsplat-unity`
- Unity 工程引用哪一份,最终取决于 manifest 的 `file:` 指向.
  - 这会导致“我改了A,但工程跑的是B”的典型分叉问题.

### 修复
- 已把关键修复同步到外部包 `/Users/cuiluming/local_doc/l_dev/my/unity/gsplat-unity`:
  - `Runtime/VFX/GsplatVfxBinder.cs`:
    - Editor 下自动回填默认 `VfxComputeShader`.
    - 缺失报错提示复用默认路径常量.
  - `Samples~/VFXGraphSample`:
    - 增加 Update Context,让动画真正每帧更新.

### 验证
- 代码与资产层面已经完成同步.
- 如果要在 Unity 里做端到端验证:
  - 需要打开“引用外部包”的 Unity 工程后再 `refresh/compile`.

## 2026-02-19 10:11:17 +0800: 修复 `.sog4d` importer/tests 的 CS0104 歧义引用

### 现象
- Unity 编译报错:
  - `Tests/Editor/GsplatSog4DImporterTests.cs`: CS0104,`CompressionLevel` 在 `System.IO.Compression.CompressionLevel` 与 `UnityEngine.CompressionLevel` 之间歧义.
  - `Editor/GsplatSog4DImporter.cs`: CS0104,`Object` 在 `UnityEngine.Object` 与 `object`(System.Object)之间歧义.

### 本质(根因)
- C# 文件同时 `using System.IO.Compression;` 与 `using UnityEngine;`.
  - `ZipArchive.CreateEntry(..., CompressionLevel)` 需要的是 `System.IO.Compression.CompressionLevel`.
  - 但 Unity 也定义了同名 `UnityEngine.CompressionLevel`,导致未限定名称时编译器无法选择.
- `using System;` + `using UnityEngine;` 同时存在时,`Object` 也会同时指向:
  - `UnityEngine.Object`
  - `System.Object`(C# 关键字 `object` 的别名)

### 修复
- `Tests/Editor/GsplatSog4DImporterTests.cs`:
  - 增加别名 `using ZipCompressionLevel = System.IO.Compression.CompressionLevel;`
  - 统一把 `CompressionLevel.NoCompression` 替换为 `ZipCompressionLevel.NoCompression`
- `Editor/GsplatSog4DImporter.cs`:
  - 增加别名 `using Object = UnityEngine.Object;`
  - 让 `Object.DestroyImmediate(...)` 的意图始终明确,不再触发歧义

### 验证
- 代码层面已消除歧义引用(不再依赖编译器推断).
- 需要在 Unity 中重新编译并运行 `Gsplat.Tests.Editor` 做最终确认(因为本环境无法直接驱动 Unity 编译器).

## 2026-02-19 10:34:20 +0800: 验证 CS0104 修复已通过 Unity EditMode tests

### 验证
- Unity TestRunner 已实际执行 `Gsplat.Tests.Editor`(EditMode):
  - 报告: `/Users/cuiluming/Library/Application Support/DefaultCompany/st-dongfeng-worldmodel/TestResults.xml`
  - 汇总: total=17, passed=11, failed=0, skipped=6
- skipped 的 6 个用例原因为:
  - `当前 Unity 版本不支持 WebP 解码,跳过需要解码的测试.`

## 2026-02-19 11:55:25 +0800: 跑满 `.sog4d` importer 的 WebP 相关用例(不再 skipped)

### 现象
- Unity EditMode tests 汇总里出现 `skipped=6`.
  - skipped 原因是 tests 内部认为“当前 Unity 版本不支持 WebP 解码”,于是 `Assert.Ignore`.
- 在命令行/CI 常用的 `-batchmode -nographics` 下,还可能出现额外噪声:
  - Unity 输出 error log: `Kernel 'InitPayload' not found`.
  - Unity Test Framework 会把未处理的 error log 当作失败,导致用例误失败.

### 本质(根因)
- `.sog4d` 的 WebP 是“数据图”,必须能 lossless 解码为 RGBA8.
- Unity 的 `ImageConversion.LoadImage` 在很多版本/环境对 WebP 会返回 false.
- native plugin 缺 `.meta` 时,Unity 对它的导入/兼容性判定可能不稳定.
- `-nographics` 下图形设备为 Null 时,ComputeShader 的 kernel 反射/编译行为可能不完整,
  触发 sorter 初始化阶段的 error log.

### 修复
- WebP 解码 fallback:
  - `Editor/Plugins/macOS/libGsplatWebpDecoder.dylib` + `Editor/GsplatWebpNative.cs`.
  - `Editor/GsplatSog4DImporter.cs`: `LoadImage` 失败后 fallback 到 `GsplatWebpNative.TryDecodeRgba32`.
- tests 能力探测与 importer 对齐:
  - `Tests/Editor/GsplatSog4DImporterTests.cs`: 先尝试 `ImageConversion.LoadImage`,失败再反射调用 `GsplatWebpNative.SupportsWebpDecoding`.
- 稳态补强(native plugin meta):
  - 新增:
    - `Editor/Plugins.meta`
    - `Editor/Plugins/macOS.meta`
    - `Editor/Plugins/macOS/libGsplatWebpDecoder.dylib.meta`
- CI/命令行 batchmode 兼容:
  - `Runtime/GsplatSettings.cs`: `SystemInfo.graphicsDeviceType==Null` 时跳过 sorter 初始化,
    避免无图形设备时的 compute kernel error log 让 tests 误失败.

### 验证
- Unity 6000.3.8f1 `-batchmode -nographics` 临时工程:
  - `/private/tmp/gsplat_webp_test_project_02_results.xml`
  - `Gsplat.Tests.GsplatSog4DImporterTests`: passed=10, failed=0, skipped=0

### 追加验证(真实工程)
- `/Users/cuiluming/Library/Application Support/DefaultCompany/st-dongfeng-worldmodel/TestResults.xml`
  - total=17, passed=17, failed=0, skipped=0

## 2026-02-19 16:02:27 +0800: 修复 `GsplatSequenceDecode.compute` kernel invalid(运行时序列播放)

### 现象
- Unity Console 报错:
  - `GsplatSequenceDecode.compute: Kernel at index (1) is invalid`
- 调用栈(用户贴出):
  - `GsplatSequenceRenderer.TryDecodeThisFrame -> ComputeShader.Dispatch`

### 本质(根因)
- `.sog4d` 的数据图纹理由 importer/runtime bundle 生成:
  - `Texture2DArray(TextureFormat.RGBA32, linear=true)`(也就是 UNorm 的 RGBA8).
- decode compute shader 旧实现用 `Texture2DArray<uint4>` 读取这些纹理:
  - 在部分 Graphics API(尤其 Metal)上,这类 "integer view 读取 UNorm 纹理" 可能导致 kernel 编译失败.
  - 表现为: kernel 名字存在,`FindKernel` 能拿到 index,但 `Dispatch` 时报 "Kernel ... invalid".
- 另一个坑:
  - Unity 的 `ComputeShader.Dispatch` 在这种情况下可能只输出 error log,不会抛异常.
  - 结果是运行期变成“持续报错但逻辑还在往下走”的黑盒.

### 修复
- shader 侧(根因修复):
  - `Runtime/Shaders/GsplatSequenceDecode.compute`:
    - 把所有数据图输入从 `Texture2DArray<uint4>` 改为 `Texture2DArray<float4>`.
    - 增加 `Float4ToU8` 把 UNorm 的 float(0..1)还原为 byte(0..255),再按原逻辑解码 u8/u16.
- C# 侧(fail-fast + 可操作报错):
  - `Runtime/GsplatSequenceRenderer.cs`:
    - 无图形设备(`GraphicsDeviceType.Null`)直接禁用并给 warning,避免 `-nographics` 刷屏.
    - 增加 `TryValidateDecodeKernel`:
      - 用 `GetKernelThreadGroupSizes` 做一次反射验证.
      - 让 kernel 编译失败/不支持时尽早报错并禁用,避免 `Dispatch` 只刷 error log.

### 验证
- 本环境无法直接驱动 Unity 编译/运行来做端到端验证.
- 我已做的证据型验证:
  - `.sog4d` 离线工具自检通过:
    - `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py validate --input Tests/Editor/Sog4DTestData/minimal_valid_delta_v1.sog4d.zip`

## 2026-02-19 17:54:42 +0800: 修复 Metal 下 decode compute shader 编译失败(GetDimensions 不支持)

### 现象
- Unity Console 报错(用户日志):
  - `HLSLcc: Metal shading language does not support buffer size query from shader. Pass the size to shader as const instead.`
  - kernel: `DecodeKeyframesSH0/DecodeKeyframesSH`
- 随后表现为:
  - kernel 编译失败 -> `Dispatch` 时 kernel invalid -> 序列无法播放.

### 本质(根因)
- `Runtime/Shaders/GsplatSequenceDecode.compute` 内部用了:
  - `_ScaleCodebook.GetDimensions(...)`
  - `_Sh0Codebook.GetDimensions(...)`
  - `_ShNCentroids.GetDimensions(...)`
- Metal 的 MSL 不支持这种 shader 内查询 buffer size 的能力,因此编译直接失败.

### 修复
- `Runtime/Shaders/GsplatSequenceDecode.compute`:
  - 移除所有 `GetDimensions` 调用.
  - 新增 `_ScaleCodebookCount/_Sh0CodebookCount/_ShNCentroidsCount`,由 C# 侧传入.
  - shader 内只用这些 count 做 clamp 与越界防御.
- `Runtime/GsplatSequenceRenderer.cs`:
  - 在 dispatch 前显式 `SetInt(...)` 传入各 buffer 的 `count`.

### 验证
- 本环境无法直接驱动 Unity/Metal 做端到端验证.
- 需要在 Unity 中确认:
  - 不再出现上述 MSL 编译错误.
  - `GsplatSequenceRenderer` 能正常播放序列.

## 2026-02-19 19:12:31 +0800: 修复 Metal 下 `GetKernelThreadGroupSizes` 误报导致 decode kernel 被判无效

### 现象
- Unity Console 报错(用户日志):
  - `[Gsplat][Sequence] DecodeComputeShader kernel 无效: DecodeKeyframesSH0`
  - `IndexOutOfRangeException: Invalid kernelIndex (0) passed, must be non-negative less than 2.`
  - 堆栈定位到: `ComputeShader.GetKernelThreadGroupSizes -> TryValidateDecodeKernel`
- 同时 shader 侧只看到 warning(例如 `isnan/isinf`),没有新的编译 error,但播放仍被阻塞.

### 本质(根因)
这是两件事叠加导致的“误杀”:

1) 我们在初始化时会验证两个 kernels(`DecodeKeyframesSH0` 和 `DecodeKeyframesSH`),
   但当 `shBands>0` 时实际只会用 `DecodeKeyframesSH`.
   此时即便 `DecodeKeyframesSH0` 的反射/缓存出问题,也不应该阻塞播放.

2) 在少数 Unity/Metal 组合下,`ComputeShader.GetKernelThreadGroupSizes` 可能抛 `IndexOutOfRangeException`,
   这更像是 Unity 内部反射/缓存问题,不一定代表 kernel 真无法 `Dispatch`.
   直接把它当成“硬失败”会误判,导致播放被禁用.

### 修复
- `Runtime/GsplatSequenceRenderer.cs`:
  - 仅验证“当前会实际使用”的 kernel:
    - `shBands==0`: 只验证 `DecodeKeyframesSH0`
    - `shBands>0`: 只验证 `DecodeKeyframesSH`
  - 验证顺序调整:
    - 先用 `ComputeShader.IsSupported(kernel)` 做基础能力探测.
    - `GetKernelThreadGroupSizes` 改为“加分项检查”:
      - 成功则校验 `numthreads` 是否与 `k_decodeThreads` 一致.
      - 抛 `IndexOutOfRangeException` 时降级为 warning,不再判定为 fatal.
  - 稳态补强:
    - `OnEnable` 在 renderer 创建成功后即写入 `m_prevAsset`,避免 decode 失败时 Update 每帧重建 renderer 刷屏.
    - SequenceAsset 变化时先 `DisposeDecodeResources`,避免复用旧 codebook/centroids buffer 导致解码错误.

### 验证
- 需要在 Unity 中确认:
  - Console 不再出现 `DecodeComputeShader kernel 无效: DecodeKeyframesSH0` 的 error.
  - `.sog4d` 序列可正常播放(仅保留非致命的 shader warnings 属于可接受范围).

## 2026-02-19 20:13:39 +0800: 修复 `ply_sequence_to_sog4d.py pack` 权重采样崩溃(ValueError: Fewer non-zero entries in p than size)

### 现象
- 运行 `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py pack ...` 时,在采样阶段抛异常:
  - `ValueError: Fewer non-zero entries in p than size`
- 报错点来自 numpy:
  - `rng.choice(n, size=size, replace=False, p=p)`

### 本质(根因)
- numpy 的硬约束:
  - 当 `replace=False` 时,概率分布 `p` 里非零项数量必须 >= `size`.
- 本工具里 `weights` 可能来自:
  - `importance = opacity * volume`
  - `volume = scale.x * scale.y * scale.z`
- 在 float32 下,`volume` 很容易下溢为 0.
  - 这会导致 `importance` 出现大量 0 权重.
  - 当 `size` 很大时,就会触发“非零权重不足”并抛 ValueError.

### 修复
- `Tools~/Sog4D/ply_sequence_to_sog4d.py`:
  - 新增 `_weighted_choice_no_replace(...)` 做稳态采样:
    - 权重总和无效/为 0 时,回退到均匀采样.
    - 非零权重不足时,自动把 `size` clamp 到非零项数量.
  - `volume/importance` 改为在 float64 下计算后再转换,减少下溢导致的 0 权重.
  - sh0/scale/shN 的采样统一走 `_weighted_choice_no_replace`,避免边界分叉.

### 验证
- 已对输出文件执行 validate,确认 bundle 完整性与越界检查通过:
  - `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py validate --input /Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp/gaussian_pertimestamp_quality_sh3.sog4d`
  - 输出: `[sog4d] validate ok (delta-v1).`

### 补充说明
- 为了避免把输出写在 `time_*.ply` 的输入目录里导致后续被上游导出脚本清理,
  当前推荐把 `.sog4d` 输出放在独立目录,例如:
  - `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp_out/`
