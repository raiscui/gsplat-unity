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

## 2026-02-22 15:20:43 +0800: 修复 FreeTimeGsVanilla `.sog4d` 导入失败(legacy meta.json) + 改良 `.splat4d` VFX 默认资产查找

### 现象

1) FreeTimeGsVanilla 导出的 `.sog4d` 在离线自检阶段失败:
- `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py validate --input <file>.sog4d`
- 输出: `[sog4d][error] meta.json.format 非法: None`

2) Unity Console 出现 VFX warning:
- `[Gsplat][VFX] 未找到默认 VFX Graph asset: Packages/.../Samples~/VFXGraphSample/VFX/SplatSorted.vfx 或 .../Splat.vfx ...`
- 堆栈定位: `Editor/GsplatSplat4DImporter.cs`(OnImportAsset)

### 本质(根因)

1) `.sog4d`:
- legacy `meta.json` 缺少顶层 `"format": "sog4d"`.
- 且把 Vector3 字段写成 `[[x,y,z]]`(list-of-3),与 Unity `JsonUtility` 解析 `Vector3[]` 的 `{x,y,z}` 形态不兼容.
- 另外,本包在构建 ZIP entry map 时曾“同名取第一个”,导致无法通过追加更新 `meta.json` 的方式修复大文件 bundle.

2) `.splat4d` VFX:
- Unity 的 Sample import 会把 `Samples~` 下的资源拷贝到 `Assets/Samples/...`.
- 但 importer 只尝试从 `Packages/.../Samples~` 直接加载,因此即使用户已导入 sample,也可能仍找不到默认 `.vfx`.

### 修复

1) `.sog4d` ZIP 兼容与快速修复路径:
- `Editor/GsplatSog4DImporter.cs` / `Runtime/GsplatSog4DRuntimeBundle.cs`:
  - ZIP entry map 改为“同名取最后一个”,符合 zip update 语义.
- `Tools~/Sog4D/ply_sequence_to_sog4d.py`:
  - 新增 `normalize-meta` 子命令:
    - 自动补齐 `meta.format="sog4d"`.
    - 把 `streams.position.rangeMin/rangeMax` 与 `streams.scale.codebook` 从 `[[x,y,z]]` 规范化为 `{x,y,z}`.
    - 通过追加新的 `meta.json` entry 修复,避免重写 1GB+ 的 bundle.

2) `.splat4d` 默认 VFX Graph 资产查找:
- `Editor/GsplatSplat4DImporter.cs`:
  - 先尝试包内路径,失败后在 `Assets/Samples/**/VFX/` 下搜索 `SplatSorted.vfx`/`Splat.vfx`.

### 验证

- `.sog4d` 修复验证(真实文件):
  - `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py normalize-meta --input <file>.sog4d --validate`
  - 输出: `[sog4d] validate ok (v1 delta-v1).`
- `.splat4d` VFX 自动绑定:
  - 需要在 Unity 中导入 sample 后验证:
    - 导入 `.splat4d` 不再输出“未找到默认 VFX Graph asset”的 warning.
    - prefab 自动挂上 `VisualEffect` + `GsplatVfxBinder`,并把 `EnableGsplatBackend=false`.

## 2026-02-23: 修复 Editor SceneView 相机下 Gsplat 排序不更新(背面显示错误) + 拖动 TimeNormalized 不刷新

### 现象

1) SceneView(隐藏相机)观察 Gsplat 时:
- 相机强烈旋转,尤其转到背后时,高斯基元显示不正确,像是没有排序.

2) 编辑态拖动 `GsplatRenderer.TimeNormalized` 时:
- SceneView 画面会乱/不稳定.
- 需要切到 GameView 再切回 SceneView 才会“刷新一下”恢复正确.

### 本质(根因)

1) 排序(sort)的触发点此前依赖“管线注入点”:
- HDRP: `GsplatHDRPPass(CustomPass)` 触发 `DispatchSort`.
- URP: `GsplatURPFeature(RendererFeature)` 触发 `DispatchSort`.
- BiRP: `Camera.onPreCull` 触发 `DispatchSort`.

但 SceneView 相机并非必然覆盖 HDRP CustomPass/URP Feature,因此 SceneView 可能渲染了 Gsplat,却使用了旧的 `_OrderBuffer`(排序结果过期),在背面/强旋转时特别明显.

2) 编辑态拖动 `TimeNormalized` 时,SceneView 不一定立即 Repaint,导致“排序/渲染”和“时间参数缓存”在体感上不同步.

### 修复

1) SRP(URP/HDRP)统一改为按相机回调驱动排序:
- `Runtime/GsplatSorter.cs`:
  - 在 `RenderPipelineManager.beginCameraRendering` 中对每个 SRP 相机调用 `DispatchSort`.
  - BiRP 下仍走 `Camera.onPreCull`,并用 `GraphicsSettings.currentRenderPipeline` 做门禁避免互相干扰.

2) 避免重复排序:
- `Runtime/SRP/GsplatHDRPPass.cs` / `Runtime/SRP/GsplatURPFeature.cs`:
  - 当 SRP 回调驱动排序时自动 no-op,避免同一相机重复 dispatch sort.

3) Play 模式智能策略(性能与正确性平衡):
- `Runtime/GsplatSettings.cs` 新增 `AllowSceneViewSortingWhenFocusedInPlayMode`.
  - 当 `SkipSceneViewSortingInPlayMode=true` 时,仅在 SceneView 聚焦时才允许 SceneView 相机排序.

4) 编辑态拖动 TimeNormalized 立刻刷新:
- `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs` 新增 `OnValidate`:
  - `QueuePlayerLoopUpdate` + `SceneView.RepaintAll`.

### 验证(手工)

- HDRP 项目中删除/禁用 `CustomPassVolume` 后:
  - SceneView 仍应正确显示(不再依赖 CustomPass 才排序).
  - 强旋转/转背面应稳定正确(排序更新).
- 编辑态拖动 `TimeNormalized`:
  - SceneView 应立即更新,不需要切 GameView 刷新.
- Play 模式:
  - SceneView 未聚焦时仍会跳过排序(性能优先).
  - 聚焦 SceneView 并交互时显示应正确(允许排序).

## 2026-02-23 18:53:24 +0800: 修复 ActiveCameraOnly 在 batchmode 下解析 ActiveCamera 失败 + 回归测试不稳定

### 现象

- Unity `-batchmode -nographics` 跑 `Gsplat.Tests.GsplatActiveCameraOnlyTests` 时:
  - `TryGetActiveCamera_*` 用例失败.
  - 初期表现为 `TryGetActiveCamera` 返回 false(无法解析 ActiveCamera).
  - 修复后又表现为返回项目默认场景的 `Main Camera`,导致断言期望不一致.

### 本质(根因)

1) batchmode/nographics 下的相机枚举不稳定:
- 在少数环境下,`Camera.allCamerasCount` 可能返回 0,即使场景里确实存在 Camera.
- 这会导致 `ResolveActiveGameOrVrCamera` 得到空集合,从而 ActiveCamera 解析失败.

2) tests 环境不干净:
- 测试项目可能默认打开一个包含 `Main Camera` 的场景.
- 用例里的“单相机/多相机”假设会被污染,导致断言不稳定.

### 修复

- `Runtime/GsplatSorter.cs`:
  - 当 `Camera.allCamerasCount==0` 时,用 FindObjects 系列做一次兜底枚举.
  - batchmode 下禁用“按帧缓存”,并避免命中 `null` 缓存导致后续一直返回 false.
- `Tests/Editor/GsplatActiveCameraOnlyTests.cs`:
  - `[SetUp]` 时创建 `EmptyScene`,让相机集合完全由用例控制.

### 验证

- Unity 6000.3.8f1,`-batchmode -nographics`:
  - `-testFilter Gsplat.Tests.GsplatActiveCameraOnlyTests`: passed(4/4)
  - `-testFilter Gsplat.Tests`: passed(21), skipped(1), failed(0)

## 2026-02-23 19:34:58 +0800: 修复 Editor UI 交互导致 SceneView 中 Gsplat 闪烁(ActiveCameraOnly)

### 现象
- Editor 非 Play 模式下,当你在 Inspector/Hierarchy 等 UI 中交互时:
  - SceneView 里的 Gsplat 整体会出现“显示/不显示”闪烁.
  - 该现象在引入 `CameraMode=ActiveCameraOnly` 之前不存在.

### 本质(根因)
- `ActiveCameraOnly` 把 sort/render 都门禁到 ActiveCamera.
- 旧 EditMode ActiveCamera 选择策略过度依赖 `SceneView.hasFocus`:
  - SceneView 失焦时(例如你在 Inspector 拖滑条),ActiveCamera 会被解析为 Game/VR 相机或上一帧缓存.
- 结果:
  - SceneView repaint 时不一定是 ActiveCamera.
  - 因为渲染只提交给 ActiveCamera,所以 SceneView 就会出现整体闪烁.

### 修复
- `Runtime/GsplatSorter.cs`:
  - 新增 `TryGetAnySceneViewCamera()`:
    - 不要求 SceneView.hasFocus,只要能拿到一个 SceneView.camera 就返回.
  - EditMode 下的 ActiveCamera 规则调整为:
    - GameView 聚焦 -> 选 Game/VR 相机.
    - 否则只要 SceneView 存在 -> 选 SceneView 相机.
    - 若无 SceneView(例如 batchmode) -> 兜底为上一帧或 Game/VR 规则.
- 同步文档与 OpenSpec:
  - `README.md`
  - `Documentation~/Implementation Details.md`
  - `openspec/changes/active-camera-only/design.md`
  - `openspec/changes/active-camera-only/specs/gsplat-camera-selection/spec.md`

### 验证
- `openspec validate --all --strict`: 9 passed, 0 failed
- Unity 6000.3.8f1,`-batchmode -nographics`:
  - `-testFilter Gsplat.Tests.GsplatActiveCameraOnlyTests`: passed(4/4)
  - `-testFilter Gsplat.Tests`: passed(21), skipped(1), failed(0)

## 2026-02-23 21:17:00 +0800: 修复 ActiveCameraOnly 在 Editor UI 交互时仍闪烁(视口信号不稳态)

### 现象
- 用户确认: 只有在 `GsplatSettings.CameraMode=ActiveCameraOnly` 时,与 Editor UI(Inspector/Hierarchy 等)交互会导致视口中的 Gsplat 整体“显示/不显示”闪烁.
- 切到 `CameraMode=AllCameras` 后不闪烁.

### 本质(根因)
- `ActiveCameraOnly` 会把 sort/render 都门禁到 ActiveCamera.
- EditMode 下如果用 `EditorWindow.focusedWindow` 这类“键盘焦点信号”去判断当前在看哪个视口:
  - 在拖动 Inspector 控件或其它 UI 交互时,`focusedWindow` 可能保持在 GameView/SceneView,或在多个窗口间抖动.
  - ActiveCamera 因此会在 SceneView/GameView 间来回切换.
  - 因为渲染只提交给 ActiveCamera,所以你在某个视口里看到的就是整体“显示/不显示”的闪烁.

### 修复
- `Runtime/GsplatSorter.cs`:
  - EditMode 引入更稳态的“视口 hint”(SceneView/GameView)缓存:
    - 优先读取 `EditorWindow.mouseOverWindow`:
      - 鼠标悬停在 SceneView -> hint=SceneView,并缓存该 SceneView.camera.
      - 鼠标悬停在 GameView -> hint=GameView.
    - 鼠标悬停在 Inspector/Hierarchy 等非视口窗口 -> 保持上一帧 hint 不变,避免抖动导致闪烁.
    - 仅在 `mouseOverWindow==null` 时才回退使用 `focusedWindow`.
  - 明确 override 优先级:
    - 当 `ActiveGameCameraOverride` 有效时,`TryGetActiveCamera` 直接返回 override(符合 spec 的 "override MUST win").
- 同步文档与 OpenSpec:
  - `README.md` / `Documentation~/Implementation Details.md` / `CHANGELOG.md`
  - `openspec/changes/active-camera-only/design.md`
  - `openspec/changes/active-camera-only/specs/gsplat-camera-selection/spec.md`

### 验证(证据型)
- OpenSpec:
  - `openspec validate --all --strict`: 9 passed, 0 failed
- Unity 6000.3.8f1(运行中实例,通过 Unity MCP):
  - `Gsplat.Tests.Editor`: passed=22, failed=0, skipped=0

## 2026-02-24 06:18:43 +0800: 修复 SceneView UI 滑动仍闪烁 + Metal 缓冲区未绑定导致跳绘制

### 现象(用户反馈)
- 仍然闪烁:
  - 鼠标在 "场景(SceneView)" 窗口的 UI 上滑动时,仍会出现整体“显示/不显示”闪烁.
  - 同时 GameView 也可能出现“突然消失/又出现”(体感像随机).
- Console warning(Metal):
  - `Metal: Vertex or Fragment Shader "Gsplat/Standard" requires a ComputeBuffer at index ... to be bound, but none provided. Skipping draw calls to avoid crashing.`

### 本质(根因)
1) Metal 的 warning 是硬错误:
   - shader 需要的 StructuredBuffer 未绑定时,Unity 为避免崩溃会直接跳过 draw call.
   - 这会让任何视口(GameView/SceneView)都可能“偶发整体消失”,并把问题伪装成相机切换闪烁.
2) EditMode 的视口 hint 更新不足:
   - 在某些交互链路里,`EditorWindow.focusedWindow` 可能不是 SceneView,
     但 `mouseOverWindow` 仍然是 SceneView(或事件只在 SceneView GUI 中可见).
   - 如果 hint 不及时切回 SceneView,ActiveCameraOnly 会把渲染提交给 Game/VR 相机,导致 SceneView “看起来闪烁”.

### 修复
- `Runtime/GsplatRendererImpl.cs`:
  - 在每次 `Render()` 提交 draw call 前重新 `SetBuffer` 绑定所有 StructuredBuffers,提升 Metal 绑定稳态.
  - `Valid` 逻辑把 `OrderBuffer` 与 4D buffers(Velocity/Time/Duration,含 dummy)视为必需资源,避免漏绑.
  - EditMode 下渲染相机选择跟随 `TryGetActiveCamera`:
    - ActiveCamera=SceneView 时,对所有 SceneView cameras 提交 draw(规避内部 camera 实例抖动).
    - ActiveCamera=Game/VR 时,只对 ActiveCamera 提交 draw(保证 GameView 稳定).
- `Runtime/GsplatSorter.cs`:
  - `SceneView.duringSceneGui` 增加 `MouseMove` 作为“SceneView 交互锁定”信号,让鼠标在 SceneView UI 上滑动时也能稳定 hint=SceneView.
  - 当鼠标悬停在 SceneView 时,无条件更新 hint=SceneView(不再要求 `over==focused`).
  - SceneView 排序门禁补强:
    - 如果 ActiveCamera 被 override 到 Game/VR,SceneView 不再抢排序,避免把 OrderBuffer 刷成 SceneView 视角.
- 新增 `Runtime/GsplatActiveCameraOverride.cs`:
  - 挂在 Game/VR Camera 上自动写入 `GsplatSorter.ActiveGameCameraOverride`.
  - 支持 `Priority`,同优先级下“最后启用者 wins”.

### 验证(证据型)
- OpenSpec:
  - `openspec validate active-camera-only --type change --strict`: passed
  - `openspec validate --all --strict`: 9 passed, 0 failed
- Unity 命令行(独立最小工程,避免主项目被 Unity 锁):
  - project: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests`
  - `Gsplat.Tests`(EditMode): total=22, passed=21, failed=0, skipped=1(VFX Graph 未安装导致 Ignore)

### 补充验证(回归锁定 override 组件行为)
- 新增 tests:
  - `Tests/Editor/GsplatActiveCameraOverrideComponentTests.cs`
- Unity 命令行(同一最小工程):
  - `Gsplat.Tests`(EditMode): total=25, passed=24, failed=0, skipped=1(VFX Graph 未安装导致 Ignore)

## 2026-02-24: 修复 SceneView UI(overlay/UIElements)区域 `mouseOverWindow` 不可靠导致的闪烁

### 现象
- 用户反馈: 鼠标滑动在 "场景(SceneView)" 窗口的 UI 上,仍会出现整体“显示/不显示”闪烁.

### 本质(根因)
- 在部分 Unity/Editor UI 组合下:
  - `UnityEditor.EditorWindow.mouseOverWindow` 在 SceneView 的 overlay/UIElements 区域可能返回 `null`,
    或返回“非 SceneView 的内部窗口”.
- 我们的 ActiveCameraOnly(EditMode) 在 `mouseOverWindow==null` 时会沿用缓存 hint.
  - 当缓存 hint 恰好是 GameView 时,ActiveCamera 会被解析为 Game/VR 相机,
    SceneView 那一帧就不会提交 draw,表现为闪烁.

### 修复
- `Runtime/GsplatSorter.cs`:
  - 通过反射可选读取 `UnityEditor.SceneView.mouseOverWindow`(若存在),作为 `EditorWindow.mouseOverWindow` 的补强信号.
  - 并把该补强判定放在 “over==null -> 沿用缓存 hint” 之前,优先确保 SceneView 的 hint 稳定.
- `Runtime/GsplatRendererImpl.cs`:
  - EditMode + ActiveCamera=SceneView 时,渲染目标相机改为遍历 `UnityEditor.SceneView.sceneViews` 的 cameras.
  - 避免 `Camera.GetAllCameras` 在 Editor 下枚举不到隐藏 SceneView camera 时,退化为“只画某一个实例”导致闪烁.

### 验证(证据型)
- Unity 6000.3.8f1,`-batchmode -nographics`(最小工程 `_tmp_gsplat_pkgtests`):
  - `Gsplat.Tests`(EditMode): total=25, passed=24, failed=0, skipped=1
  - 结果文件: `_tmp_gsplat_pkgtests/Logs/TestResults_gsplat.xml`

## 2026-02-24: 架构级修复 - EditMode 停止 SceneView/GameView 自动切换(消灭闪烁根源路径)

### 现象
- 用户反馈: 即使补强 `mouseOverWindow` 与 SceneView 交互锁定,在 SceneView 的 overlay/UIElements 区域仍可能出现闪烁.

### 本质(根因)
- EditMode 下 “ActiveCameraOnly 依赖 Editor UI 信号做 SceneView/GameView 自动切换” 这个架构本身不可靠:
  - UI 信号在不同 Unity 版本/不同 UI 形态(overlay/UIElements)下不可控.
  - 任何一次误判都会让 ActiveCameraOnly 当帧把 draw 提交给错误相机,从而出现整体“显示/不显示”闪烁.

### 修复(策略变更)
- `Runtime/GsplatSorter.cs`:
  - 当 `Application.isPlaying==false` 且 `CameraMode=ActiveCameraOnly` 时:
    - 默认始终选择 SceneView(若存在)作为 ActiveCamera.
    - 不再基于 Editor UI 信号自动切换到 GameView.
    - 若需要在 EditMode 以 GameView 为主,通过显式 override(`GsplatActiveCameraOverride`)或切到 `AllCameras`.
- 同步更新 OpenSpec 文档,保证“契约与实现一致”:
  - `openspec/changes/active-camera-only/specs/gsplat-camera-selection/spec.md`
  - `openspec/changes/active-camera-only/design.md`

### 验证(证据型)
- OpenSpec:
  - `openspec validate --all --strict`: passed
- Unity 6000.3.8f1,`-batchmode -nographics`(最小工程 `_tmp_gsplat_pkgtests`):
  - `Gsplat.Tests`(EditMode): total=25, passed=24, failed=0, skipped=1
  - 结果文件: `_tmp_gsplat_pkgtests/Logs/TestResults_gsplat2.xml`

## 2026-02-24: 增加可控的诊断采集链路(为定位“仍闪烁”提供证据)

### 现象
- 用户反馈: 仍然闪烁,并且“没有任何相关 log”,无法判断是:
  - draw 根本没提交,
  - 提交到了错误相机实例,
  - 还是提交了但被 Metal 跳绘制.

### 决策(先证据,再修复)
- 遵循 systematic-debugging:
  - 在没有根因证据之前,不再继续追加“猜测式修复”.
  - 先把观测点打通,让闪烁发生时可以自动产出可分析的证据块.

### 实现
- `Runtime/GsplatSettings.cs`:
  - 新增 `EnableEditorDiagnostics`(默认 false).
- `Editor/GsplatSettingsProvider.cs`:
  - 在 Project Settings/Gsplat 增加 `Diagnostics` UI,无需改宏即可开关.
- `Runtime/GsplatEditorDiagnostics.cs`:
  - 环形缓冲记录近 512 条事件.
  - 当检测到 “SceneView 相机触发渲染回调,但当帧没有提交 draw” 时自动 dump(`[GsplatDiag]`),用于定位时序/相机实例不一致问题.
- 接入点:
  - `Runtime/GsplatSorter.cs`: 记录相机渲染回调 + sort skip/dispatch.
  - `Runtime/GsplatRendererImpl.cs`: 记录 render skip 原因 + 每次提交 draw 的 camera 目标.

### 验证(证据型)
- Unity 6000.3.8f1,`-batchmode -nographics`(最小工程 `_tmp_gsplat_pkgtests`):
  - `Gsplat.Tests`(EditMode): total=25, passed=24, failed=0, skipped=1
  - 结果文件: `_tmp_gsplat_pkgtests/Logs/TestResults_gsplat3.xml`

## 2026-02-24: Metal 下因 StructuredBuffer 绑定不稳导致的跳绘制(闪烁/消失)

### 现象
- macOS/Metal 下,视口(SceneView/GameView)出现整体"消失/闪烁".
- Console/Editor.log 里出现(或只出现一次后静默):
  - `Metal: Vertex or Fragment Shader "Gsplat/Standard" requires a ComputeBuffer at index 3 to be bound, but none provided. Skipping draw calls to avoid crashing.`

### 根因
- Metal 下只要任意一个 shader 需要的 StructuredBuffer 未绑定,Unity 会直接跳过 draw call 防止崩溃.
- 该 warning 可能只打印一次,导致用户体感为"没 log"但仍然闪烁.
- 仅依赖 `MaterialPropertyBlock.SetBuffer` 在某些 Editor/Metal 场景下可能不够稳态.

### 修复
- `Runtime/GsplatRendererImpl.cs`
  - 为每个 renderer 创建一个 per-renderer `Material` 实例.
  - 每帧 draw 前把所有 StructuredBuffers 同时绑定到:
    - `MaterialPropertyBlock`(原有路径)
    - per-renderer `Material` 实例(稳态兜底)
  - `Valid` 强化: 使用 `GraphicsBuffer.IsValid()` 识别已失效 buffer.
- `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`
  - 检测到 `!m_renderer.Valid` 时,节流(1s)自动重建 renderer(以及 sequence decoder),避免长期黑屏.
- `Runtime/GsplatEditorDiagnostics.cs`
  - 捕获 Metal 跳绘制 warning 时自动 dump,并输出 "ComputeBuffer index -> shader 属性名" 映射.
- 新增回归测试:
  - `Tests/Editor/GsplatMetalBufferBindingTests.cs`

### 验证(证据型)
- Unity 命令行(有图形设备,Metal),最小工程 `_tmp_gsplat_pkgtests`:
  - `GsplatMetalBufferBindingTests`: passed(1/1)
  - `Gsplat.Tests`: total=26, passed=25, failed=0, skipped=1
  - 关键点: 日志中未再出现 "requires a ComputeBuffer ... Skipping draw calls".

## 2026-02-24 11:15:00 +0800: 修复 Editor 下“SceneView UI 上滑动仍闪烁”(SRP 多次 BeginCameraRendering vs Update 单次 draw)

### 现象
- Unity Editor 非 Play 模式:
  - 鼠标在 SceneView 的 UI(overlay/toolbar/tab)区域滑动时,Gsplat 会整体“显示/不显示”闪烁.
  - 有时 GameView 也会出现“偶发整帧消失”.
- Console 未必有可用 error/warning,导致排查困难.

### 本质(根因)
- 证据来自 `[GsplatDiag]` ring-buffer:
  - 同一 `Time.frameCount` 内,同一个 SceneView camera 会多次触发:
    - `RenderPipelineManager.beginCameraRendering`(即 `[CAM_RENDER] phase=BeginCameraRendering`)
  - 但 draw 的提交主要来自 `ExecuteAlways.Update()` 内的 `m_renderer.Render(...)`:
    - 每次 Update 通常只提交一次 draw.
- 因此在 Editor 的“同帧多次渲染调用”场景下会出现:
  - `render invocation 次数 > draw 提交次数`
  - 部分 render invocation 没有 splats,最终显示出来的那一次恰好没 draw 时,体感就是闪烁/消失.

### 修复
1) EditMode 下把 draw 提交对齐到 SRP 相机回调链路
- `Runtime/GsplatSorter.cs`
  - 在 `OnBeginCameraRendering` 排序逻辑之后调用 `SubmitEditModeDrawForCamera(camera)`.
  - 仅在 `CameraMode=ActiveCameraOnly` 且 `!Application.isPlaying` 时启用,避免 Play 模式重复渲染.
  - 渲染门禁(不包含 sort 的 per-frame guard):
    - Active=SceneView: 允许所有 SceneView camera 渲染(规避实例抖动/多窗口).
    - Active=Game/VR: 只允许 ActiveCamera 渲染(override MUST win).

2) renderer 增加“按指定 camera 渲染”的入口
- `Runtime/GsplatRendererImpl.cs`
  - 新增 `RenderForCamera(Camera camera, ...)`.
  - 新增 `TryPrepareRender(...)` 统一渲染准备:
    - 校验 settings/sorter/buffers.
    - 填充 property block(含 time model).
    - 重新绑定 StructuredBuffers(Metal 稳态).
    - 构建 `RenderParams` + `instanceCount`.

3) 避免 EditMode 双重提交 draw
- `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`
  - 在 `!Application.isPlaying && SortDrivenBySrpCallback && CameraMode==ActiveCameraOnly` 时:
    - Update 仍负责 upload/decode/time 缓存等状态更新.
    - 但不再从 Update 调用 `m_renderer.Render(...)`.
    - draw 由 sorter 的相机回调统一提交.

4) 诊断增强(更贴合本根因)
- `Runtime/GsplatEditorDiagnostics.cs`
  - 增加 `sceneView.renderCounts/drawCounts`(按 camera instanceId 计数).
  - 增加 `rs(render serial)` 关联每次 `[CAM_RENDER]` 与 `[DRAW]`.
  - 自动 dump 触发条件从 “rendered but no draw” 扩展为:
    - `renderCount > drawCount`.

5) 排序 skip reason 细分
- `Runtime/GsplatSorter.cs`
  - 新增 overload: `GatherGsplatsForCamera(Camera cam, out string skipReason)`.
  - 在 `SORT_SKIP` 日志中输出更具体的 skipReason(例如 per-frame guard 命中).

### 验证(证据)
- Unity 6000.3.8f1,最小工程 `_tmp_gsplat_pkgtests`:
  - `-batchmode -nographics`
  - `-testFilter Gsplat.Tests`
  - 结果文件: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_flickerfix.xml`
  - 汇总: total=26, passed=24, failed=0, skipped=2

## 2026-02-24 12:40:00 +0800: 仍闪烁(补强) - SceneView camera 可能 `enabled/isActiveAndEnabled=false`

### 现象
- 用户反馈仍然闪烁.
- `[GsplatDiag]` auto-detect 触发:
  - `DETECTED: SceneView renderCount > drawCount`.
  - ring-buffer 中可见 `RenderForCamera` 被跳过,原因指向 camera "null/disabled".

### 根因(补充)
- Unity Editor 下的 SceneView 内部 camera 在某些时序/交互链路中可能:
  - `enabled=false` 或 `isActiveAndEnabled=false`,
  但仍然会参与 SRP 的渲染回调(beginCameraRendering).
- 因此,任何把 `isActiveAndEnabled` 当作 SceneView 渲染门禁的代码,都会出现:
  - “相机在渲染,但我们不提交 draw” -> `renderCount > drawCount` -> 体感为“整帧闪”.

### 修复
- `Runtime/GsplatRendererImpl.cs`
  - 遍历 `SceneView.sceneViews` 时,移除 `cam.isActiveAndEnabled` 过滤(仅保留 null/destroyed 防御).
- `Runtime/GsplatEditorDiagnostics.cs`
  - `DescribeCamera` 输出 `en/act`,在 dump 中直接看到 camera enabled 状态,避免再猜.

### 验证(待补)
- 需要在用户真实复现步骤下确认:
  - dump 不再出现 `renderCount > drawCount`,
  - SceneView 不再出现“显示/不显示”的整体闪烁.

### 验证(补充)
- 2026-02-24 13:02:53 +0800: 用户已确认“不闪了”.

## 2026-02-24 13:10:00 +0800: EditMode 切到 GameView 不显示(回归修复)

### 现象
- 非 Play 模式,切换/聚焦到 GameView 后,高斯基元不显示.

### 根因
- 为了避免 overlay/UIElements 噪声导致闪烁,EditMode 下 `ActiveCameraOnly` 曾固定选择 SceneView 作为 ActiveCamera.
- 这会让 Game camera 永远不是 ActiveCamera,从而:
  - 排序门禁跳过 Game camera.
  - SRP 相机回调驱动的 draw 门禁也拒绝对 Game camera 提交 draw.

### 修复
- `Runtime/GsplatSorter.cs`
  - EditMode 下: 当 GameView 窗口聚焦时,ActiveCamera 解析为 Game/VR 相机.
  - 其它窗口聚焦时仍默认 SceneView,保持闪烁修复不回归.
- `Runtime/GsplatSettings.cs`
  - tooltip 同步更新,避免用户误解“为何 GameView 看不到”.

### 验证(证据)
- Unity 6000.3.8f1,最小工程 `_tmp_gsplat_pkgtests`:
  - `-batchmode -nographics -runTests -testFilter Gsplat.Tests`
  - 结果文件: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_2026-02-24_1310.xml`
  - 汇总: total=26, passed=24, failed=0, skipped=2

## 2026-02-24 13:12:00 +0800: Codex skill 加载失败(名称超长)

### 现象
- Codex 提示:
  - `Skipped loading 1 skill(s) due to invalid SKILL.md files.`
  - `invalid name: exceeds maximum length of 64 characters`

### 根因
- `.codex/skills/**/SKILL.md` 的 front-matter 里 `name` 字段超过 64 字符,被校验器拒绝加载.

### 修复
- 缩短 skill name:
  - from: `self-learning.unity-editor-srp-multi-beginCameraRendering-flicker`
  - to: `self-learning.unity-editor-srp-beginCameraRendering-flicker`

## 2026-02-24 14:54:19 +0800: GameView 拖动 TimeNormalized 消失 + PlayMode 播放卡顿(自动优化)

### 现象
- EditMode:
  - SceneView 下拖动 `TimeNormalized`,正常且很顺.
  - GameView 下拖动 `TimeNormalized`,高斯基元会“全消失”.
- PlayMode:
  - `TimeNormalized` 拖动/`AutoPlay` 都非常卡.

### 根因
1. (消失) ActiveCameraOnly(EditMode) 之前只在 `focusedWindow==GameView` 时选择 Game/VR camera.
   - 用户一拖动 Inspector 滑条,焦点落到 Inspector,ActiveCamera 立刻切回 SceneView.
   - 于是 Game camera 的 sort/draw 直接被 gate.

2. (卡顿) keyframe `.splat4d(window)` 常见是“多 segment records 堆在同一个 asset/buffers 中”.
   - 同一时刻只有 1 个 segment 真正可见.
   - 旧版仍对全量 records 做 GPU radix sort,成本随 segment 数线性膨胀.

### 修复
- ActiveCameraOnly(EditMode):
  - `Runtime/GsplatSorter.cs`: ActiveCamera 决策改用 viewport hint(最近交互视窗),而不是仅靠 focusedWindow.
  - `Runtime/GsplatRenderer.cs`/`Runtime/GsplatSequenceRenderer.cs`: OnValidate 改为 `InternalEditorUtility.RepaintAllViews()`,
    确保拖动 Inspector 滑条时 SceneView/GameView 都会立即刷新.
  - `Runtime/GsplatSettings.cs`: tooltip 文案同步更新.

- keyframe segment 性能优化(自动启用,不改格式):
  - `Runtime/GsplatSorter.cs`: `IGsplat.SplatBaseIndex` + sortCount 变化时重置 payload,支持对子范围排序.
  - `Runtime/GsplatSortPass.cs`/`Runtime/Shaders/Gsplat.compute`: 新增 `e_baseIndex`,排序只读子范围数据.
  - `Runtime/GsplatRendererImpl.cs`/`Runtime/Shaders/Gsplat.shader`: 新增 `_SplatBaseIndex`,渲染把 local index 映射为 absolute splatId.
  - `Runtime/GsplatRenderer.cs`: 检测 "time/duration 常量且 segments 不重叠" 的 keyframe 资产形态,
    播放时仅对当前 segment 做 sort+draw.

### 验证(证据)
- Unity 6000.3.8f1,最小工程 `_tmp_gsplat_pkgtests`:
  - `-batchmode -nographics -runTests -testFilter Gsplat.Tests`
  - 结果文件: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_timeNormalized_fix_2026-02-24_1453.xml`
  - 汇总: total=26, passed=24, failed=0, skipped=2
