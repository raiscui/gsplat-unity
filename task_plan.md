# 任务计划: 收尾 `sog4d-sequence-format`(验证 + 归档)

## 目标
完成 `.sog4d`/`.sog4d.zip` 的实现收尾验证.
把本次 change 正式归档,让仓库回到"可继续下一件事"的干净状态.

## 阶段
- [x] 阶段1: 状态确认与收尾规划
- [x] 阶段2: 运行可用验证(非 Unity)
- [x] 阶段3: 四文件收尾(WORKLOG/notes/LATER_PLANS/ERRORFIX)
- [x] 阶段4: 归档 OpenSpec change

## 关键问题
1. 在当前环境不启动 Unity 的前提下,我们还能做哪些"证据型"验证.
2. Python 导出工具的 validate 子命令,是否能在未安装训练依赖(sklearn 等)时独立运行.

## 做出的决定
- [决定] 因为 `task_plan.md` 已超过 1000 行,按规范先续档并新开一份.
  - 理由: 减少注意力污染,也符合"文件系统作为外部内存"的维护成本控制.

## 状态
**已完成**.
时间: 2026-02-18 22:23:00 +0800.
我已完成 specs 同步与 change 归档,并通过 strict 校验验证仓库 specs 一致性.

## 进展记录
### 2026-02-18 22:08:25 +0800
- 已确认 change 状态:
  - `openspec list --json` 显示 `sog4d-sequence-format` 为 complete(38/38).
  - `openspec validate sog4d-sequence-format --strict` 通过.
- 已完成可执行验证(不启动 Unity):
  - Python exporter 自检通过:
    - `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py validate --input Tests/Editor/Sog4DTestData/minimal_valid_delta_v1.sog4d.zip`
  - Python 语法编译检查通过:
    - `python3 -m compileall Tools~/Sog4D -q`
- 下一步进入阶段3:
  - 收尾更新 `WORKLOG.md/notes.md/LATER_PLANS.md`(以及如有必要的 `ERRORFIX.md`).
  - 然后归档 OpenSpec change.

### 2026-02-18 22:15:06 +0800
- 已完成阶段3(四文件收尾):
  - `notes.md`: 追加 continuous-learning 四文件摘要.
  - `WORKLOG.md`: 追加收尾验证记录与下一步(同步 specs + 归档 change).
  - `LATER_PLANS.md`: 清理已完成的 "序列 4DGS 新格式" 候选项(避免备忘污染).
  - `AGENTS.md`: 追加 "Samples copy 不自动更新" 的长期提醒(项目级约定).
  - 历史版本归档:
    - `archive/task_plan_2026-02-18_220625.md`
- 下一步进入阶段4:
  - 先把 `openspec/changes/sog4d-sequence-format/specs/**` 同步到 `openspec/specs/**`.
  - 然后把 change 归档到 `openspec/changes/archive/`.

### 2026-02-18 22:23:00 +0800
- 已完成阶段4(specs 同步 + 归档):
  - specs 同步:
    - 新增 `openspec/specs/sog4d-*/spec.md` 与 `openspec/specs/4dgs-keyframe-motion/spec.md`
    - 更新 `openspec/specs/4dgs-resource-budgeting/spec.md`(纳入 `.sog4d` 路径的预算与降级项)
  - strict 校验:
    - `openspec validate --specs --strict` 通过
    - `openspec validate --all --strict` 通过
  - 归档:
    - `openspec/changes/archive/2026-02-18-sog4d-sequence-format/`
    - `openspec list --json` 显示无 active changes

---

# 任务计划: 修复 Unity 编译歧义引用(CS0104)

## 目标
修复 `.sog4d` importer 与其 EditMode tests 在 Unity 下的编译错误:
- CS0104: `CompressionLevel` 歧义引用
- CS0104: `Object` 歧义引用

使 `Gsplat.Tests.Editor` 能正常编译并继续执行回归测试.

## 阶段
- [x] 阶段1: 定位歧义来源与影响范围
- [x] 阶段2: 以最小侵入方式消除歧义
- [x] 阶段3: Unity 侧重新编译与测试验证

## 状态
**已完成**.
时间: 2026-02-19 10:34:20 +0800.
Unity 已重新编译并执行 `Gsplat.Tests.Editor`(EditMode),未再出现 CS0104.

## 进展记录
### 2026-02-19 10:11:17 +0800
- 现象(来自 Unity Console):
  - `Tests/Editor/GsplatSog4DImporterTests.cs`: `CompressionLevel` 在 `System.IO.Compression` 与 `UnityEngine` 之间歧义.
  - `Editor/GsplatSog4DImporter.cs`: `Object` 在 `UnityEngine.Object` 与 `object`(System.Object)之间歧义.
- 根因:
  - 文件同时 `using System.IO.Compression;` 与 `using UnityEngine;`,且两侧都暴露同名类型.
- 修复:
  - `Tests/Editor/GsplatSog4DImporterTests.cs`:
    - 增加别名 `using ZipCompressionLevel = System.IO.Compression.CompressionLevel;`
    - `CreateEntry(..., CompressionLevel.NoCompression)` 改为 `CreateEntry(..., ZipCompressionLevel.NoCompression)`
  - `Editor/GsplatSog4DImporter.cs`:
    - 增加别名 `using Object = UnityEngine.Object;`,统一 `Object.DestroyImmediate(...)` 的含义
- 下一步:
  - 在 Unity Test Runner 里重新编译并运行 `Gsplat.Tests.Editor`.

### 2026-02-19 10:34:20 +0800
- Unity EditMode tests 已实际执行并产出测试报告:
  - `TestResults.xml`: `/Users/cuiluming/Library/Application Support/DefaultCompany/st-dongfeng-worldmodel/TestResults.xml`
  - 汇总: total=17, passed=11, failed=0, skipped=6
- Console 中看到的多条 `import error:` 为测试用例刻意导入坏数据以验证"可操作的报错信息",属于预期现象.
- skipped 的 6 个用例原因:
  - `当前 Unity 版本不支持 WebP 解码,跳过需要解码的测试.`

---

# 任务计划: 让 WebP 解码可用(跑满 `.sog4d` importer tests)

## 目标
让 `Gsplat.Tests.Editor` 中被 Ignore 的 6 个用例跑起来.
核心是: 在 Unity `ImageConversion.LoadImage` 不支持 WebP 的环境下,依然能解码 `.webp` 数据图.

## 阶段
- [x] 阶段1: 规划与风险评估(两条路线)
- [x] 阶段2: 研究 Unity/平台可用的 WebP 解码方案
- [x] 阶段3: 落地实现(Importer + Tests)
- [x] 阶段4: Unity 侧回归验证(不再 skipped)

## 两条路线(需要做选择)
### 路线A(不惜代价,最佳方案): 内置 WebP 解码器
- 做法:
  - 在包内提供一个可用的 WebP 解码器(优先: `libwebp` 原生库 + C# P/Invoke 包装).
  - Importer 先尝试 `ImageConversion.LoadImage`,失败后自动走内置解码器.
  - Tests 改为基于“与 importer 同源的 decoder 探测”,不再被 `ImageConversion.LoadImage==false` 误伤而 Ignore.
- 优点:
  - 行为稳定,与 Unity 版本解耦.
  - 满足 spec 对“数据图 byte 必须一致”的要求(使用 `libwebp` lossless 解码).
- 代价:
  - 引入原生插件(需要考虑 macOS/Windows/Linux Editor 的二进制分发与 .meta).

### 路线B(先能用,后面再优雅): 借用 Unity 资产导入链路解码
- 做法:
  - 把 zip 内 `.webp` 临时写到 `Assets/__Temp/xxx.webp`,用 `AssetDatabase.ImportAsset` 让 Unity 纹理导入器解码,
    再读回像素做数据图处理.
- 优点:
  - 不引入第三方二进制.
- 风险/缺点:
  - 依赖 Unity 是否“能导入 WebP 纹理”(不确定性很高).
  - 需要频繁磁盘 IO,并且更容易在测试/并发导入场景出幺蛾子.

## 做出的决定
- [决定] 选择路线A: 内置 `libwebp` 解码器,彻底消除 Unity 版本差异.
  - 理由: `.sog4d` 的 WebP 是“数据图”,必须 lossless 且 byte 精确; 依赖 Unity 导入链路属于黑盒且不稳定.

## 关键问题
1. 我们要覆盖哪些平台: 仅 macOS Editor 先跑通,还是一次性补齐 Win/Linux Editor?
2. 原生库的落点与命名: 如何避免与其它包的 `libwebp` 冲突,并保持可维护性.

## 状态
**已完成**.
时间: 2026-02-19 11:17:50 +0800.
我已为 macOS Editor 内置 `libwebp` 解码器,并让 `.sog4d` importer tests 不再 skipped.

## 进展记录
### 2026-02-19 11:17:50 +0800
- 根因确认:
  - `ImageConversion.LoadImage` 在当前 Unity 版本对 WebP 返回 false,导致 tests 触发 `Assert.Ignore`.
- 落地实现(路线A):
  - 新增 macOS Editor 原生库: `Editor/Plugins/macOS/libGsplatWebpDecoder.dylib`(universal: arm64+x86_64).
  - 新增 C# P/Invoke 包装: `Editor/GsplatWebpNative.cs`,提供 `TryDecodeRgba32/SupportsWebpDecoding`.
  - Importer fallback:
    - `Editor/GsplatSog4DImporter.cs`: `LoadImage` 失败后自动调用 `GsplatWebpNative.TryDecodeRgba32`,
      并用 `Texture2D.LoadRawTextureData` 填充数据图.
  - Tests 能力探测改造:
    - `Tests/Editor/GsplatSog4DImporterTests.cs`: `SupportsWebpDecoding` 改为反射调用 `GsplatWebpNative.SupportsWebpDecoding`,
      避免对 `Gsplat.Editor` 产生编译期依赖.
- 回归验证(证据):
  - 使用 Unity 6000.3.8f1 在临时工程中跑测试:
    - `Gsplat.Tests.GsplatSog4DImporterTests`: passed=10, failed=0, skipped=0

---

# 任务计划: 让那 6 个 skipped 用例也能跑起来(WebP 解码稳态)

## 目标
让 `Gsplat.Tests.Editor` 里原本 skipped 的 6 个 `.sog4d` importer 用例不再跳过.
结束状态是: 同一套 EditMode tests 在当前工程中实际执行,并且 `skipped=0`.

## 阶段
- [x] 阶段1: 复盘 skipped 的来源与时间线
- [x] 阶段2: 核对 WebP 解码 fallback 是否已落盘
- [x] 阶段3: 补齐 native plugin 的 `.meta`(避免 Unity 识别/加载不稳定)
- [x] 阶段4: Unity 侧重新跑测试并存证

## 关键问题
1. skipped 是否来自旧的 `TestResults.xml`,还是“重新跑依然 skip”.
2. `.dylib` 是否被 Unity 当作 native plugin 导入(缺 `.meta` 时可能不稳定).
3. tests 的“能力探测”是否与 importer 的解码路径一致.

## 做出的决定
- [决定] 先把 skipped 的时间线搞清楚,再补齐 `.meta` 做稳态.
  - 理由: 你现在贴出来的 `TestResults.xml` 时间是 2026-02-19 10:34:12 +0800,
    但 WebP decoder 相关文件是在 11:00+ 才新增.
    这很可能是“修复已落盘,但还没重新跑测试”的典型情况.

## 状态
**已完成**
时间: 2026-02-19 11:55:25 +0800.
我已补齐 native plugin 的 `.meta`,并确保 tests 的 WebP 探测逻辑与 importer 的解码路径一致.
同时补上 batchmode/nographics 的 guard,避免无图形设备时的 compute kernel error log 让单测误失败.

## 进展记录
### 2026-02-19 11:46:23 +0800
- 你这次看到的 skipped=6 来自测试报告:
  - `/Users/cuiluming/Library/Application Support/DefaultCompany/st-dongfeng-worldmodel/TestResults.xml`
  - mtime: 2026-02-19 10:34:12 +0800
  - skipped 原因: "当前 Unity 版本不支持 WebP 解码..."
- 但包内 WebP fallback 已经在稍后落盘(文件 mtime):
  - `Editor/Plugins/macOS/libGsplatWebpDecoder.dylib`: 2026-02-19 11:00:52 +0800
  - `Editor/GsplatWebpNative.cs`: 2026-02-19 11:01:37 +0800
  - `Tests/Editor/GsplatSog4DImporterTests.cs`: 2026-02-19 11:02:48 +0800
- 下一步进入阶段3:
  - 为 `Editor/Plugins/**` 补齐 `.meta`,降低 Unity 导入期自动生成 meta 的不确定性.

### 2026-02-19 11:55:25 +0800
- 已补齐 native plugin 的 `.meta`(稳态):
  - `Editor/Plugins.meta`
  - `Editor/Plugins/macOS.meta`
  - `Editor/Plugins/macOS/libGsplatWebpDecoder.dylib.meta`
- tests 的 WebP 能力探测改造:
  - `Tests/Editor/GsplatSog4DImporterTests.cs`: 先尝试 `ImageConversion.LoadImage`,失败再反射调用 `Gsplat.Editor.GsplatWebpNative` 的 native fallback.
- 额外修复(让 CI/命令行也能跑测试):
  - `Runtime/GsplatSettings.cs`: 在 `SystemInfo.graphicsDeviceType==Null` 时跳过 sorter 初始化,避免 "Kernel 'InitPayload' not found" 的 error log 让测试失败.
- 回归验证(证据型):
  - Unity 6000.3.8f1 `-batchmode -nographics` 临时工程结果: `/private/tmp/gsplat_webp_test_project_02_results.xml`
  - `Gsplat.Tests.GsplatSog4DImporterTests`: passed=10, failed=0, skipped=0

### 2026-02-19 12:41:19 +0800
- 你在真实工程里重新跑了 EditMode tests 后,`.sog4d` 那 6 个原 skipped 用例已实际执行:
  - 报告文件: `/Users/cuiluming/Library/Application Support/DefaultCompany/st-dongfeng-worldmodel/TestResults.xml`
  - 汇总: total=17, passed=17, failed=0, skipped=0
- 你 Console 里看到的多条 `import error:` 日志属于预期:
  - 这些用例本来就是“导入坏数据,断言 importer 输出可操作的错误信息”.

---

# 任务计划: 修复 `GsplatSequenceDecode.compute` kernel invalid + 跑通 PLY->SOG4D->Unity 工作流

## 目标
1. 在 Unity 运行/编辑模式下,`GsplatSequenceRenderer` 不再输出 "Kernel at index (...) is invalid".
2. 给出从 PLY 序列生成 `.sog4d` 并在 Unity 播放显示的可执行步骤(含常见坑位排查).

## 阶段
- [x] 阶段1: 复现与定位(设备/API/导入产物核对)
- [x] 阶段2: 修复 decode compute shader 兼容性
- [x] 阶段3: 增加 fail-fast 检测与可操作报错
- [x] 阶段4: 工具链验证 + 文档补充

## 关键问题
1. kernel invalid 是 "kernel 缺失" 还是 "kernel 编译失败/不支持当前 Graphics API"?
2. importer 生成的数据图纹理格式(`TextureFormat.RGBA32`)是否与 compute shader 的 `Texture2DArray<uint4>` 匹配?
3. 在 `ExecuteAlways` 下,编辑模式或无图形设备时是否需要额外 guard?

## 做出的决定
- [决定] 优先把 decode compute shader 的输入改为 `Texture2DArray<float4>`,并在 shader 内把 0..1 还原为 0..255 的 `uint4`.
  - 理由: importer 生成的数据图是 `TextureFormat.RGBA32`(UNorm),跨平台最稳的读法是 float4.
  - 预期收益: 避免因 "整数视图/纹理格式不匹配" 导致 kernel 变成 invalid.

## 状态
**已完成**
时间: 2026-02-19 16:11:14 +0800.
我已定位并修复 `.sog4d` 序列播放时的 "Kernel at index (...) is invalid".
我把 decode compute shader 的数据图读取改为 `float4` + byte 还原,以匹配 importer 生成的 `TextureFormat.RGBA32`(UNorm).
同时我增加了 kernel 的 fail-fast 检测.
这样能避免 `Dispatch` 只刷 error log 的黑盒.

## 进展记录
### 2026-02-19 16:02:27 +0800
- 用户现象:
  - `GsplatSequenceDecode.compute: Kernel at index (1) is invalid`
  - 调用栈: `GsplatSequenceRenderer.TryDecodeThisFrame -> ComputeShader.Dispatch`
- 初步判断:
  - `GsplatSequenceDecode.compute` 使用 `Texture2DArray<uint4>` 读取数据图.
  - `.sog4d` importer 创建的是 `TextureFormat.RGBA32`(UNorm) 的 `Texture2DArray`.
  - 在部分 Graphics API(尤其 Metal)上,"用 uint 读 UNorm 纹理" 可能导致 kernel 编译失败,进而 dispatch 报 invalid.

### 2026-02-19 16:11:14 +0800
- 修复(根因修复 + 稳态):
  - `Runtime/Shaders/GsplatSequenceDecode.compute`:
    - 数据图读取从 `Texture2DArray<uint4>` 改为 `Texture2DArray<float4>`.
    - 增加 `Float4ToU8`,把 UNorm 的 float(0..1)还原为 byte(0..255),再按 u8/u16 规则解码.
  - `Runtime/GsplatSequenceRenderer.cs`:
    - 增加无图形设备 guard(`GraphicsDeviceType.Null`),避免 `-nographics` 刷屏.
    - 增加 `TryValidateDecodeKernel`(`GetKernelThreadGroupSizes`)做 fail-fast,避免 `Dispatch` 只刷 error log 的黑盒.
- 工具链验证(证据型):
  - `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py validate --input Tests/Editor/Sog4DTestData/minimal_valid_delta_v1.sog4d.zip`
- 文档补充:
  - `README.md` 增加 "Kernel at index (...) is invalid" 的排查提示.

---

# 任务计划: 输出 `.sog4d` pack 命令手册(多配置菜谱)

## 目标
把 `Tools~/Sog4D/ply_sequence_to_sog4d.py pack` 的常用配置组合整理成一份“可复制粘贴”的命令手册.
让你在不同数据/质量/体积/速度需求下,可以直接选一条命令跑.

## 阶段
- [x] 阶段1: 梳理参数与默认值(以 `--help` 和脚本实现为准)
- [x] 阶段2: 写入文档(优先改良现有 README)
- [x] 阶段3: 用 `--help/validate` 做证据型校验
- [x] 阶段4: 收尾记录到 WORKLOG

## 做出的决定
- [决定] 不新建额外文档文件,直接扩写 `Tools~/Sog4D/README.md`.
  - 理由: "改良胜过新增". 同一入口更好找,也更不容易过期.

## 状态
**已完成**
时间: 2026-02-19 17:49:46 +0800.
我已把 pack 的常用配置整理成“命令菜谱手册”.
我把它直接扩写进 `Tools~/Sog4D/README.md`,作为唯一入口.
这样你后续只要打开这一个文件,就能按场景复制粘贴命令.

## 进展记录
### 2026-02-19 17:49:46 +0800
- 参数梳理:
  - 以 `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py pack --help` 与脚本实现为准确认默认值与依赖项.
- 手册落盘:
  - 扩写 `Tools~/Sog4D/README.md`:
    - 增加依赖说明(WebP/Pillow,scipy,sklearn).
    - 增加输入数据前提(字段/帧一致性/命名排序).
    - 增加多套可复制粘贴的 pack 配置命令(菜谱).
    - 增加参数速查与 Unity 播放常见报错提示.
- 证据型校验:
  - `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py validate --input Tests/Editor/Sog4DTestData/minimal_valid_delta_v1.sog4d.zip`

### 2026-02-19 17:51:13 +0800
- 追加补强:
  - `Tools~/Sog4D/README.md`: 增加 `unzip -l out.sog4d` 的 bundle 内容查看命令,并强调扩展名使用 `.sog4d`.

---

# 任务计划: 修复 Metal 下 `.sog4d` 序列 decode compute shader 编译失败(GetDimensions 不支持)

## 目标
在 macOS/Metal 下,`GsplatSequenceDecode.compute` 能正常编译与运行.
Unity Console 不再出现:
- `HLSLcc: Metal shading language does not support buffer size query from shader`
- `Kernel at index (...) is invalid`

## 阶段
- [x] 阶段1: 收集错误日志并定位根因
- [x] 阶段2: 修复 shader,移除 buffer size query
- [x] 阶段3: C# 侧传入必要的 buffer count 常量
- [ ] 阶段4: Unity 侧验证与回归

## 根因
`Runtime/Shaders/GsplatSequenceDecode.compute` 里使用了:
- `_ScaleCodebook.GetDimensions(...)`
- `_Sh0Codebook.GetDimensions(...)`
- `_ShNCentroids.GetDimensions(...)`

但 Metal 的 MSL 不支持这种 "buffer size query from shader".
因此 compute kernel 编译失败,最终导致 `Dispatch` 时 kernel invalid.

## 修复
- shader 侧:
  - 移除所有 `GetDimensions` 调用.
  - 新增 `_ScaleCodebookCount/_Sh0CodebookCount/_ShNCentroidsCount`,由 C# 侧传入.
  - shader 内只用这些 count 做 clamp 与越界防御.
- C# 侧:
  - `Runtime/GsplatSequenceRenderer.cs` 在 dispatch 前 `SetInt(...)` 传入各 buffer 的 `count`.

## 状态
**等待 Unity 验证**
时间: 2026-02-19 17:54:42 +0800.
修复已落盘.
我需要你在 Unity 里重新触发一次脚本/compute shader 重新编译(通常自动发生).
然后确认不再出现 Metal 的 GetDimensions 编译错误.

## 进展记录
### 2026-02-19 17:54:42 +0800
- 用户日志(关键错误):
  - `Shader error in 'GsplatSequenceDecode': HLSLcc: Metal shading language does not support buffer size query from shader. Pass the size to shader as const instead.`
  - kernel: `DecodeKeyframesSH0/DecodeKeyframesSH`
- 代码修复已完成:
  - `Runtime/Shaders/GsplatSequenceDecode.compute`: 移除 `GetDimensions`,改用 C# 传入的 count.
  - `Runtime/GsplatSequenceRenderer.cs`: dispatch 前传入 `_ScaleCodebookCount/_Sh0CodebookCount/_ShNCentroidsCount`.

---

# 任务计划: continuous-learning 提取 Unity/Metal compute 排障 skill

## 目标
把本次 Unity/macOS/Metal 下 compute shader 的排障经验固化成可复用知识:
- `Kernel at index (...) is invalid` 的真实含义与 fail-fast 检测方式.
- Metal 不支持 `StructuredBuffer.GetDimensions`(buffer size query)的规避写法.
- RGBA8 UNorm 数据图纹理在 shader 内的更稳读取方式(`float4` + byte 还原).

## 阶段
- [x] 阶段1: 回读四文件并做摘要
- [x] 阶段2: 去重并补齐参考资料
- [x] 阶段3: 创建/更新 skill
- [x] 阶段4: 同步 AGENTS.md 与四文件记录

## 状态
**已完成**
时间: 2026-02-19 18:53:37 +0800.
我已完成 continuous-learning 复盘:
- 新增 skill: `~/.codex/skills/self-learning.unity-metal-compute-kernel-invalid/SKILL.md`
- 更新项目协作约定: `AGENTS.md` 增加 Metal compute 注意事项,避免未来回归.

---

# 任务计划: 修复 decode kernel 验证误判(避免 `GetKernelThreadGroupSizes` 误报阻塞播放)

## 目标
在 macOS/Metal 下,`.sog4d` 序列播放不再因为 kernel 验证阶段的误判而被禁用.
典型现象包括:
- `IndexOutOfRangeException: Invalid kernelIndex ...` 出现在 `ComputeShader.GetKernelThreadGroupSizes`.
- 但 shader 侧没有新的编译 error,播放仍被阻塞.

## 阶段
- [x] 阶段1: 收集用户日志并定位触发点
- [x] 阶段2: 调整验证策略(只验证需要的 kernel)
- [x] 阶段3: `GetKernelThreadGroupSizes` 降级为非强制检查
- [ ] 阶段4: Unity 侧验证与回归

## 修复摘要
- `Runtime/GsplatSequenceRenderer.cs`:
  - 当 `shBands>0` 时只验证 `DecodeKeyframesSH`,不再因为 `DecodeKeyframesSH0` 的反射异常阻塞播放.
  - 先用 `ComputeShader.IsSupported` 做能力探测.
  - `GetKernelThreadGroupSizes` 抛 `IndexOutOfRangeException` 时降级为 warning,继续尝试运行.
  - decode 失败不再导致 Update 每帧重建 renderer 刷屏,并在 asset 变化时先 `DisposeDecodeResources`.

## 状态
**等待 Unity 验证**
时间: 2026-02-19 19:12:31 +0800.
请在 Unity 中清空 Console 后重新启用对象,确认不再出现:
- `[Gsplat][Sequence] DecodeComputeShader kernel 无效: ...`
- `Invalid kernelIndex ... GetKernelThreadGroupSizes ...`

---

# 任务计划: `gaussian_pertimestamp` 输出高质量 `.sog4d`

## 目标
把 `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp` 下的 `time_*.ply` 序列,
打包生成一个“质量优先”的 `.sog4d`,
用于 Unity 中通过 `GsplatSequenceRenderer` 播放显示.

## 阶段
- [x] 阶段1: 输入确认与约束核对
- [x] 阶段2: 选择打包配置(质量/体积/速度权衡)
- [x] 阶段3: 执行 `pack` + `validate`(self-check)
- [ ] 阶段4: Unity 导入与播放验证(回归)

## 关键问题
1. 输入 PLY 的 `opacity/scale` 语义是什么(是否需要 sigmoid/exp)?
2. “高质量”的定义更偏向:
   - 更大 codebook/palette(量化误差更小)?
   - 更高 SH(bands=3)?
   - 还是仅在保证可播的前提下尽量压缩体积?

## 做出的决定
- [决定] 采用“质量优先”配置作为默认输出,同时保留一条“先能用”的平衡配置备选.
  - 理由: 你明确要求输出高质量 `.sog4d`,并且该序列帧数只有 20,做高质量拟合的成本可控.

## 状态
**目前在阶段4(等待 Unity 验证)**
时间: 2026-02-19 20:13:39 +0800.
我已生成并验证“质量优先”的 `.sog4d` 输出.
下一步是在 Unity 中导入并播放,确认不再出现 Metal/ComputeShader 相关报错.

## 进展记录
### 2026-02-19 19:14:49 +0800
- 输入确认(阶段1):
  - 输入目录: `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp`
  - PLY 文件: `time_00000.ply`..`time_00019.ply`(共 20 帧)
  - splatCount: 27673
  - 检测到 `f_rest_0..44`(45 个标量),因此 `shBands=3`
  - `opacity` 为 logit(范围约 [-8,9]),因此需要 `sigmoid`
  - `scale_0/1/2` 为 log(scale)(大量负数),因此需要 `exp`

- 配置选择(阶段2):
  - 质量优先(默认执行):
    - `--opacity-mode sigmoid --scale-mode exp`
    - `--sh-bands 3`
    - `--scale-codebook-size 8192 --scale-sample-count 400000`
    - `--sh0-codebook-method kmeans --sh0-sample-count 4000000`
    - `--shN-count 8192 --shN-sample-count 400000 --shN-centroids-type f32`
    - `--shN-labels-encoding delta-v1 --delta-segment-length 50`
    - `--zip-compression deflated --self-check`
  - 平衡配置(备选,更快更省内存,但量化误差更大):
    - 基本保持默认参数,只强制 `--sh-bands 3` + `--self-check`

### 2026-02-19 20:13:39 +0800
- 输出文件(阶段3):
  - `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp/gaussian_pertimestamp_quality_sh3.sog4d`
  - meta.json 关键字段:
    - `frameCount=20`
    - `layout=167x166`
    - `shNCount=8192`
    - `shNCentroidsType=f32`
    - `shNLabelsEncoding=delta-v1`
- 自检/校验(阶段3):
  - `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py validate --input /Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp/gaussian_pertimestamp_quality_sh3.sog4d`
  - 输出: `[sog4d] validate ok (delta-v1).`
- 下一步(阶段4):
  - 导入 Unity 并挂 `GsplatSequenceRenderer` 播放.
  - 重点确认 Console 不再出现:
    - `Kernel at index (...) is invalid`
    - `HLSLcc: Metal shading language does not support buffer size query from shader`

---

# 任务计划: 多档质量输出 `gaussian_pertimestamp` `.sog4d`(pack 脚本稳态验证)

## 目标
从同一套输入 PLY 序列输出多份不同质量/体积/复杂度的 `.sog4d`,
并对每一份输出执行 `validate`,
用“证据型”结果确认 `pack` 脚本在多配置下都没有隐藏问题.

## 阶段
- [x] 阶段1: 选定输出矩阵与命名
- [x] 阶段2: 执行 `pack` 生成多份 `.sog4d`
- [x] 阶段3: 对每份输出执行 `validate` + 记录文件尺寸
- [x] 阶段4: 汇总到四文件 + 给出 Unity 导入验证建议

## 输出矩阵(计划)
输入:
- `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp/time_*.ply`

计划输出(会逐个 pack + validate):
1. SH0-only(最快,先跑通最小路径):
   - `gaussian_pertimestamp_fast_sh0.sog4d`
2. SH3 默认(平衡,覆盖默认分支):
   - `gaussian_pertimestamp_balanced_sh3_delta_f16.sog4d`
3. SH3 体积优先(更小 palette,更容易暴露越界/边界 bug):
   - `gaussian_pertimestamp_small_sh3_delta_f16_4096.sog4d`
4. SH3 质量优先(已完成):
   - `gaussian_pertimestamp_quality_sh3.sog4d`

## 状态
**已完成**
时间: 2026-02-19 21:14:10 +0800.
我已完成多配置输出与逐一 validate 自检,并已把结果汇总回四文件.
接下来只需要在 Unity 里按建议导入其中任意一个 `.sog4d` 做端到端播放验证即可.

## 进展记录
### 2026-02-19 21:12:30 +0800
- 输出目录(避免把输出混在 input-dir 里):
  - `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp_out`
- 已生成并自检通过(`--self-check`):
  1. SH0-only(最小路径,最快):
     - `gaussian_pertimestamp_out/gaussian_pertimestamp_fast_sh0.sog4d`
     - `validate ok (bands=0)`, size=7.3MiB
  2. SH3 + delta-v1(多 segment,覆盖边界逻辑):
     - `gaussian_pertimestamp_out/gaussian_pertimestamp_balanced_sh3_delta_seg5_shN4096_f16.sog4d`
     - `validate ok (delta-v1)`, size=7.8MiB
     - meta: `shNCount=4096`, `shNCentroidsType=f16`,并且 `shNDeltaSegments` 存在多段(由 `--delta-segment-length 5` 产生)
  3. SH3 + full labels(覆盖 full 路径):
     - `gaussian_pertimestamp_out/gaussian_pertimestamp_balanced_sh3_full_shN8192_f16.sog4d`
     - `validate ok (full labels)`, size=8.7MiB
     - meta: `shNCount=8192`, `shNCentroidsType=f16`, `shNLabelsEncoding=full`

- 已有的质量优先(参考对照,更大采样量 + f32 centroids + ZIP deflated):
  - `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp_quality_sh3.sog4d`
  - 同时已复制到输出目录:
    - `gaussian_pertimestamp_out/gaussian_pertimestamp_quality_sh3.sog4d`
  - `validate ok (delta-v1)`, size=8.3MiB

---

# 任务计划: 输出 `.sog4d` 详细准确规格(用于 FreeTimeGsVanilla 改版导出器)

## 目标
输出一份可以直接用来实现 exporter 的 `.sog4d` 文件格式规格.
重点是: 结构清晰,字段类型准确,二进制布局不含糊.

## 阶段
- [x] 阶段1: 回读现有 specs/实现(Importer + Tools)
- [x] 阶段2: 对齐 v1/v2 与 delta-v1 的真实细节
- [x] 阶段3: 产出对外可用的规格说明(回复 + 可选落盘文档)
- [x] 阶段4: 四文件收尾(WORKLOG/notes/LATER_PLANS)

## 关键问题
1. `.sog4d` 的容器与 `meta.json` schema 的强约束有哪些(哪些必须 fail-fast).
2. delta-v1 的二进制布局里 update entry 是否包含 reserved 字段(需要按实现对齐).
3. v2 的 SH rest 按 band 拆分(sh1/sh2/sh3)对 exporter 的影响是什么.

## 做出的决定
- [决定] 以当前仓库实现为准来输出规格:
  - `Editor/GsplatSog4DImporter.cs` 代表 Unity 侧"读者"的真实期望.
  - `Tools~/Sog4D/ply_sequence_to_sog4d.py` 代表一份可运行的"写者"参考实现.
  - 如发现 OpenSpec 文档与实现不一致,以实现为准,并在回复中明确指出差异.

## 状态
**已完成**
- 时间: 2026-02-21
- 我已输出 `.sog4d` 的统一规格说明,并完成四文件收尾.

## 进展记录
### 2026-02-21 15:34:59 +0800
- 已完成回读与对齐:
  - 以 `Editor/GsplatSog4DImporter.cs` 与 `Tools~/Sog4D/ply_sequence_to_sog4d.py` 为准核对所有字段与二进制布局.
  - 已更新 `openspec/specs/sog4d-sequence-encoding/spec.md`:
    - 补齐 `meta.json.version=2` 的 sh1/sh2/sh3 per-band 编码.
    - 补齐 delta-v1 的 update entry: `(splatId u32, newLabel u16, reserved u16=0)`,并要求 block 内 splatId 严格递增.
  - `openspec validate --specs --strict` 已通过.
- 下一步:
  - 输出 `.sog4d` 的统一规格说明(用于 FreeTimeGsVanilla 改版 exporter).
  - 然后完成四文件收尾.

### 2026-02-21 15:38:10 +0800
- 已输出统一规格说明到对话(用于实现 exporter).
- 已完成四文件收尾:
  - `task_plan.md`/`notes.md`/`WORKLOG.md`/`LATER_PLANS.md` 均已追加本次任务记录.

---

# 任务计划: 修复 FreeTimeGsVanilla 输出 `.sog4d` 导入失败 + 改良 `.splat4d` 默认 VFX 资产查找

## 目标
1. 让 FreeTimeGsVanilla 导出的 `.sog4d` 能在本包 Unity importer 中成功导入并播放.
2. 当用户已导入 `Samples~/VFXGraphSample` 时,`.splat4d` 一键导入能自动找到 `SplatSorted.vfx`/`Splat.vfx`,减少误导性 warning.

## 现象
- Unity Console 出现 warning:
  - `[Gsplat][VFX] 未找到默认 VFX Graph asset: Packages/wu.yize.gsplat/Samples~/VFXGraphSample/VFX/SplatSorted.vfx 或 Packages/wu.yize.gsplat/Samples~/VFXGraphSample/VFX/Splat.vfx ...`
  - 堆栈: `Gsplat.Editor.GsplatSplat4DImporter:OnImportAsset`(约 `Editor/GsplatSplat4DImporter.cs:449`)
- 对 FreeTimeGsVanilla 的 `.sog4d` 做离线自检时失败:
  - `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py validate --input <file>.sog4d`
  - 输出: `[sog4d][error] meta.json.format 非法: None`
- 进一步检查 `.sog4d` 内的 `meta.json`:
  - 缺少顶层字段 `"format": "sog4d"`
  - `streams.position.rangeMin/rangeMax` 与 `streams.scale.codebook` 使用 `[[x,y,z]]` 形式,与 Unity `JsonUtility` 期望的 `{x,y,z}` 不一致.

## 方向(二选一)
- 方向A(推荐,最佳): 兼容与修复并行.
  - 在 importer/runtime 的 ZIP entry map 中对同名条目采用“保留最后一个”(更符合 zip update 语义).
  - 在离线工具中提供一个“规范化 meta.json”的命令,把 legacy `.sog4d` 一键修到符合本包 spec.
  - 优点: 不必重新导出巨大的 `.sog4d`,可快速救火.
- 方向B(先能用): 只修 exporter 并要求重新导出.
  - 让 FreeTimeGsVanilla exporter 输出完全符合 spec 的 meta.json.
  - 优点: 规范更干净; 缺点: 对大文件需要重新导出,成本高.

## 阶段
- [x] 阶段1: 证据与复现落盘
- [x] 阶段2: 修复 `.sog4d` ZIP 同名 entry 策略(保留最后一个)
- [x] 阶段3: 增加 `.sog4d` meta.json 规范化工具
- [x] 阶段4: 改良 `.splat4d` 默认 VFX 资产查找(支持 `Assets/Samples/...`)
- [ ] 阶段5: 验证 + 四文件收尾(ERRORFIX/WORKLOG/LATER_PLANS 回溯清理)

## 状态
**目前在阶段5(等待 Unity 验证)**
- 时间: 2026-02-22 15:20:43 +0800
- 已完成:
  - `.sog4d` importer/runtime: ZIP 同名条目改为“取最后一个”,符合 zip update 语义.
  - 离线工具新增 `normalize-meta`,可一键补 `meta.format` 并规范化 Vector3 JSON.
  - `.splat4d` importer: 增加在 `Assets/Samples/**/VFX/` 下搜索默认 `.vfx` 的回退逻辑.
- 已做离线验证:
  - `normalize-meta` 在真实 `.sog4d` 上执行后 `validate ok`.
- 待 Unity 侧确认:
  - 重新导入该 `.sog4d` 后不再报 meta.json 错误,并能正常播放.
  - 在已导入 sample 的项目里导入 `.splat4d` 不再报“未找到默认 VFX Graph asset”,并能自动挂 VFX 组件.
