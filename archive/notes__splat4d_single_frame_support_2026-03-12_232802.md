# 笔记: 单帧普通 3DGS `.ply -> .splat4d` OpenSpec 立项

## 2026-03-11 22:22:27 +0800 来源: 历史上下文与仓库静态检查

### 已观察到的事实

- 仓库已有相近 change:
  - `openspec/changes/sog4d-single-frame-ply-support`
- 仓库已有目标脚本:
  - `Tools~/Splat4D/ply_sequence_to_splat4d.py`
- 当前 `openspec/changes/` 下尚未发现:
  - `splat4d-single-frame-ply-support`
- 当前活跃支线 `__sog4d_display_issue` 的尾部记录表明:
  - 前一条任务聚焦 `.sog4d` 数据语义与显示异常
  - 本次用户请求则是新的 `.splat4d` OpenSpec 立项

### 初步判断

- 这次更像“沿用既有 `.sog4d` 单帧立项经验,为 `.splat4d` 平行补一条正式能力”。
- 当前还没有动态证据证明 `GsplatRenderer` 单帧 `.splat4d` 路径一定缺失。
- 因此在 proposal 阶段更适合把它表述为:
  - 需要正式化并验证的工作流
  - 而不是已经确认存在缺陷的 bug

## 2026-03-11 22:22:27 +0800 来源: OpenSpec CLI

### 状态结果

- `openspec new change "splat4d-single-frame-ply-support"`
  - 已创建:
    - `openspec/changes/splat4d-single-frame-ply-support/`
  - schema:
    - `spec-driven`
- `openspec status --change "splat4d-single-frame-ply-support"`
  - `Progress: 0/4 artifacts complete`
  - `proposal` 为唯一 ready artifact
  - `design/specs/tasks` 仍被阻塞

### 首个 artifact instructions 摘要

- artifact:
  - `proposal`
- 输出路径:
  - `openspec/changes/splat4d-single-frame-ply-support/proposal.md`
- 目标:
  - 说明这次 change 为什么需要做
- 模板主段落:
  - `Why`
  - `What Changes`
  - `Capabilities`
  - `Impact`
- 关键提醒:
  - `Capabilities` 是 proposal 和 specs 之间的契约
  - 需要先研究已有 `openspec/specs/` 再填写 modified capabilities

## 2026-03-11 22:22:27 +0800 来源: 用户澄清 + 现有 specs / 代码阅读

### 新证据

- 用户明确收窄了边界:
  - “普通 3DGS 单帧 `.ply -> .splat4d`” 是工具职责
  - “`.splat4d -> GsplatRenderer`” 现有链路已经存在
  - 这次重点是保证单帧 `.splat4d` 在 Unity 中正常工作
- `openspec/specs/4dgs-core/spec.md`
  - 已经覆盖 `.splat4d` 导入与 4D 字段语义
  - 适合承接“单帧静态 `.splat4d` 必须作为合法资产导入”的 requirement
- `openspec/specs/4dgs-playback-api/spec.md`
  - 已经覆盖 `TimeNormalized / AutoPlay / Loop`
  - 适合承接“静态单帧 `.splat4d` 在播放控制下应稳定退化”的 requirement
- `Tools~/Splat4D/ply_sequence_to_splat4d.py`
  - 当前 CLI 仍然只接受:
    - `--input-dir`
- `README.md`
  - 已经出现 `--input-ply` 示例
  - 说明文档和真实 CLI 入口已经发生漂移

### 由此形成的 proposal 取舍

- New Capability:
  - `splat4d-ply-conversion`
- Modified Capabilities:
  - `4dgs-core`
  - `4dgs-playback-api`
- 没有新增新的 Unity renderer capability
  - 因为用户已经明确:
    - `.splat4d -> GsplatRenderer` 本身不是从零新增

## 2026-03-11 22:22:27 +0800 来源: `design` 起草前的静态证据补读

### 已观察到的事实

- `Editor/GsplatSplat4DImporter.cs`
  - `.splat4d` 已经导入为现有 `GsplatAsset`
  - importer 注释和 prefab 生成路径都说明它本来就是给 `GsplatRenderer` 用的
- `Runtime/GsplatRenderer.cs`
  - `Has4D` 由 `Velocities / Times / Durations` 是否存在决定
  - `TimeNormalized / AutoPlay / Loop` 已经是现有播放控制接口
- `Tools~/Splat4D/ply_sequence_to_splat4d.py`
  - `average` 路径在 `len(ply_files) == 1` 时已经会写:
    - `velocity = 0`
    - `time0 = 0`
    - `duration = 1`
  - `keyframe` 路径对单帧输入会报错

### 对 design 的直接影响

- 单帧支持不是“编码格式能力缺失”,更像“入口没有正式暴露 + 契约没写清 + 验证没锁住”。
- 这次 design 最自然的路线是:
  - 扩展现有脚本入口
  - 复用现有静态默认值
  - 用测试和 spec 把 Unity 侧单帧工作语义固定下来

## 2026-03-11 22:22:27 +0800 来源: `specs` 编写过程

### 这轮关键取舍

- `splat4d-ply-conversion`
  - 作为新 capability,单独定义:
    - 单文件输入
    - 静态默认 4D 字段
    - Gaussian 字段缺失时的明确失败语义
- `4dgs-core`
  - 没有去 `MODIFY` 那个很长的 “Import canonical 4D fields from `.splat4d` binary”
  - 改为补一个新的 requirement:
    - 单帧静态 `.splat4d` 仍是合法 canonical 4D asset
- `4dgs-playback-api`
  - 也没有硬改现有 3D-only requirement
  - 改为新增:
    - 静态单帧 `.splat4d` 在所有播放控制下都必须视觉稳定

### 为什么这样做

- 这次不是重写 `.splat4d` 格式本体。
- 重点是补“单帧普通 3DGS 输入”这条正式支持路径。
- 用 `ADDED Requirements` 比整段 `MODIFIED` 旧 requirement 更稳,归档时也更不容易丢失原始细节。

## 2026-03-11 22:22:27 +0800 来源: `tasks` 编写过程

### 任务拆分原则

- 先把工具入口与默认导出语义放在最前面
  - 因为它是整个 change 的真正新增入口
- 再处理 Unity importer / runtime 语义
  - 因为这部分是“既有链路的正式保证”
- 测试与真实夹具单独成组
  - 避免实现完成后才发现没有证据闭环
- 文档与最终验证放最后
  - 用来收口 README 漂移和 apply-ready 证据

### 结果

- `tasks.md` 已被 OpenSpec 接受
- 当前 change 状态:
  - `4/4 artifacts complete`
  - `All artifacts complete!`

## 2026-03-11 22:47:06 +0800 来源: apply 阶段实现前补充证据

### OpenSpec apply 状态

- `openspec status --change "splat4d-single-frame-ply-support" --json`
  - `schemaName: spec-driven`
  - `isComplete: true`
- `openspec instructions apply --change "splat4d-single-frame-ply-support" --json`
  - `progress.total = 13`
  - `progress.complete = 0`
  - `progress.remaining = 13`
  - `state = ready`

### Python `argparse` 官方参考

- 来源:
  - Context7 `/python/cpython`
  - `Doc/library/argparse.rst`
- 已确认:
  - `add_mutually_exclusive_group(required=True)` 可以直接约束两种正式入口二选一
  - 当未提供任一参数时,`argparse` 会给出标准 usage + required 错误
  - 当同时提供两个参数时,`argparse` 会给出 mutually exclusive 冲突错误

### 对实现的直接影响

- `Tools~/Splat4D/ply_sequence_to_splat4d.py` 最自然的改法是:
  - 新增 `_die(...)`
  - 新增 `_resolve_input_ply_files(args)`
  - 在 parser 中使用 `mutually_exclusive_group(required=True)`
  - 后续主流程统一只接 `ply_files`
- 这样可以最大化复用现有 average / keyframe 编码逻辑
  - 避免为了单帧再新造第二条编码路线

## 2026-03-11 23:46:18 +0800 来源: apply 阶段实现与验证

### 工具侧已验证事实

- `Tools~/Splat4D/ply_sequence_to_splat4d.py`
  - 已新增:
    - `--input-ply`
    - `--input-ply` / `--input-dir` required mutually exclusive group
    - `_resolve_input_ply_files(args)`
    - 统一 CLI 错误前缀:
      - `[splat4d][error] ...`
- `python3 -m unittest Tools~/Splat4D/tests/test_single_frame_cli.py`
  - `Ran 4 tests`
  - `OK`
- 仓库内夹具:
  - `Tools~/Splat4D/tests/data/single_frame_valid_3dgs.ply`
- 用夹具做真实导出:
  - 命令:
    - `python3 Tools~/Splat4D/ply_sequence_to_splat4d.py --input-ply Tools~/Splat4D/tests/data/single_frame_valid_3dgs.ply --output /tmp/single_frame_valid_3dgs.splat4d --mode average --opacity-mode linear --scale-mode linear`
  - 导出结果:
    - `1` 条 record
    - `vx/vy/vz = 0/0/0`
    - `time = 0`
    - `duration = 1`

### Unity 侧静态审计结论

- `Editor/GsplatSplat4DImporter.cs`
  - v1 raw `.splat4d` 会继续创建:
    - `GsplatAsset.Velocities`
    - `GsplatAsset.Times`
    - `GsplatAsset.Durations`
  - window 语义下单帧 `time=0,duration=1` 会自然成为全时域可见的固定帧
- `Runtime/GsplatRenderer.cs`
  - 当前播放控制只是维护一个 `m_timeNormalizedThisFrame`
  - 单帧 `.splat4d` 不存在“第二帧插值”概念
  - 对 keyframe 子范围优化而言:
    - 单帧静态 4D 资产不会命中多 segment 优化
    - `UpdateSortRangeForTime(...)` 会稳定回到全量单帧范围

### Unity 侧代码改动与编译证据

- 最小 guard:
  - `Runtime/GsplatRenderer.cs`
    - `Has4DFields(...)` 已从“只看非 null”补成“非 null 且长度至少覆盖 `SplatCount`”
- 新增测试:
  - `Tests/Editor/GsplatSplat4DImporterDeltaV1Tests.cs`
    - 已补单帧 raw v1 `.splat4d` importer test
  - `Tests/Editor/GsplatVisibilityAnimationTests.cs`
    - 已补静态单帧 4D runtime 逻辑测试
- `dotnet build Gsplat.Tests.Editor.csproj`
  - `0 errors`
  - 4 个 warning 都来自既有无关文件:
    - `Tests/Editor/GsplatLidarScanTests.cs`
    - `Tests/Editor/GsplatSog4DImporterTests.cs`
- `dotnet build Gsplat.Editor.csproj`
  - `0 warnings`
  - `0 errors`

### 当前阻塞与证据

- 现象:
  - 运行 Unity batch EditMode tests 时,CLI 在启动前失败
- 动态证据:
  - 报错:
    - `似乎有另一个正在运行的 Unity 实例打开了此项目`
  - `ps aux | rg -i "Unity|Unity Hub"`
    - 存在项目主进程:
      - `PID 7588`
  - `lsof +D <project>/Temp`
    - `PID 7588` 持有:
      - `Temp/UnityLockfile`
- 结论:
  - 当前不是“测试失败”
  - 而是“真实 Unity 实例占用项目,导致无法在同项目上启动 batchmode 做最终验证”

### 候选但未落地的额外 guard

- 子智能体只读探索给出的候选方向:
  - `Runtime/GsplatRenderer.cs`
    - `TryInitShDeltaRuntime()`
- 当前口径:
  - 这只是静态候选假设
  - 还没有动态证据证明 `ShFrameCount == 1` 的 `.splat4d v2` 资产会在现网路径触发问题
  - 因此本轮没有贸然修改

## 2026-03-11 23:18:00 +0800 来源: 只读探索 - Unity EditMode 单帧 `.splat4d` 测试落点

### 已观察到的事实

- `Tests/Editor/GsplatSplat4DImporterDeltaV1Tests.cs`
  - 已有稳定的 AssetDatabase 临时目录脚手架:
    - `SetUp/TearDown`
    - `GetProjectRootPath`
    - `WriteBytesToAssetPath`
  - 已有最贴题的最小夹具构造器:
    - `BuildMinimalStaticSingleFrameV1()`
    - 其语义明确写死:
      - `velocity = 0`
      - `time = 0`
      - `duration = 1`
  - 已有现成测试:
    - `ImportV1_StaticSingleFrame4D_PreservesCanonicalArrays()`
    - 已锁定:
      - 这是合法 `.splat4d`
      - importer 会保留 canonical 4D arrays
      - `TimeModel = 1`
- `Tests/Editor/GsplatVisibilityAnimationTests.cs`
  - 已有成熟的 `GsplatRenderer` EditMode 测试套路:
    - 用 `GameObject + GsplatRenderer + yield return null`
    - 用反射推进私有状态机
    - 用 `((IGsplat)renderer).SplatCount` 观测“当前帧是否仍提交 splats”
  - 这套模式已经在现有测试里被反复使用,说明它是本仓库认可的稳定写法
- `Tests/Editor/GsplatSequenceAssetTests.cs`
  - 已锁定“单帧 + 多个 TimeNormalized 样本 -> 固定帧”的断言模式
  - 这可以直接借来作为 `.splat4d` runtime 测试的数据点设计参考
- `Runtime/GsplatRenderer.cs`
  - `Update()` 的播放控制只会推进 `TimeNormalized`
  - 随后统一进入:
    - `UpdateSortRangeForTime(m_timeNormalizedThisFrame)`
    - `TryApplyShDeltaForTime(m_timeNormalizedThisFrame)`
  - `TryApplyShDeltaForTime` 对 `frameCount <= 1` 直接 no-op
  - 但 `TryInitShDeltaRuntime` 当前只在 `ShFrameCount <= 0` 时跳过初始化

### 结论

- 若只选一个“最适合继续补测试”的现有文件:
  - 首选 `Tests/Editor/GsplatSplat4DImporterDeltaV1Tests.cs`
  - 原因不是它名字最好看,而是它已经拥有:
    - 最小 `.splat4d` 二进制夹具
    - 导入脚手架
    - 单帧静态 canonical 4D 现成断言
  - 在这个文件里继续补“播放控制下仍固定画面”的测试,改动面最小
- 若只看 runtime 测试手法:
  - 最适合借模式的是 `Tests/Editor/GsplatVisibilityAnimationTests.cs`
  - 它是现成的 `GsplatRenderer` EditMode 反射测试模板

### 候选假设

- 候选假设A:
  - 对于未来可能出现的“`ShFrameCount == 1` 但仍带 delta 元数据”的 `.splat4d v2`
  - runtime 目前会走“初始化动态 SH runtime”,但后续播放时又因 `frameCount <= 1` 永不推进
  - 这是一条不对称路径
- 最强备选解释B:
  - 当前项目真正会落地的静态单帧资产主要是 raw v1 `.splat4d`
  - 它根本不会带 delta metadata
  - 因此上面的不对称路径暂时不会在用户资产上触发

### 当前还缺的证据

- 还没有动态复现证据证明:
  - `ShFrameCount == 1` 的 `.splat4d v2` 已经在现网资产中出现
  - 它一定会造成可见伪动态或失败
- 所以上述 runtime guard 判断,目前只能表述为:
  - “最值当的候选 guard 点”
  - 不能表述为“已确认根因”

## 2026-03-12 00:02:00 +0800 来源: 收尾验证前的 VFX headless error 分析

### 已观察到的现象

- `Unity -batchmode -nographics -executeMethod Gsplat.Editor.BatchVerifySplat4DImport.VerifyStaticSingleFrameFixture`
  - 真实闭环已经成功:
    - exporter 成功
    - importer 成功
    - runtime sample `(0, 0.35, 1)` 全部保持整帧 sort range
- 但 batch log 里仍有:
  - `Kernel 'BuildAxes' not found`

### 静态证据

- `Runtime/VFX/GsplatVfxBinder.cs`
  - `OnEnable()` 会调用 `EnsureKernelsReady()`
  - `EnsureKernelsReady()` 内部会触发:
    - `VfxComputeShader.FindKernel("BuildAxes")`
    - `VfxComputeShader.FindKernel("BuildDynamic")`
- 这说明 error 来源是 VFX 预览链路的 kernel 查询,不是 `.splat4d` importer / renderer 主链路失败

### 外部文档证据

- Unity `GraphicsDeviceType.Null`
  - 文档说明:
    - 没有 graphics API
    - 这通常发生在显式请求 null graphics API 时
    - 例子就包括 game server 或 editor batch mode
- Unity `ComputeShader.FindKernel`
  - 文档说明:
    - 找不到 kernel 时,会先记录 `"FindKernel failed"` 一类错误
    - 然后抛 `ArgumentException`
- Unity `ComputeShader.HasKernel`
  - 文档说明:
    - 只返回 bool,适合先做存在性探测

### 当前结论

- 当前主假设已经有两类证据支撑:
  - 静态证据:
    - `OnEnable -> EnsureKernelsReady -> FindKernel`
  - 动态证据:
    - 真实闭环功能通过,只剩 VFX kernel error
- 因此更正确的修法不是继续依赖 `try/catch`
- 而是:
  - 在 `GraphicsDeviceType.Null` 下直接短路
  - 普通图形环境下先用 `HasKernel(...)` 再 `FindKernel(...)`

## 2026-03-12 00:05:00 +0800 来源: 收尾验证结果

### 命令与结果

- `python3 -m unittest Tools~/Splat4D/tests/test_single_frame_cli.py`
  - 结果:
    - `Ran 4 tests`
    - `OK`
- `dotnet build Gsplat.Editor.csproj`
  - 结果:
    - `0 warnings`
    - `0 errors`
- `dotnet build Gsplat.Tests.Editor.csproj`
  - 结果:
    - `0 errors`
    - 3 个既有 warning,均来自无关旧文件
- `Unity ... -executeMethod Gsplat.Editor.BatchVerifySplat4DImport.VerifyStaticSingleFrameFixture -quit`
  - 结果:
    - exporter/importer/runtime 闭环全部成功
    - `BuildAxes` / `FindKernel` 相关 error 已消失
- `Unity ... -runTests ... -quit`
  - 结果:
    - `exit code 0`
    - 未生成 `testResults xml`
- `Unity ... -runTests ...`(不带 `-quit`)
  - 结果:
    - 成功生成 `testResults xml`
    - `total=19 passed=19 failed=0`

### 结论

- `GsplatVfxBinder` 的 headless guard 修复是有效的
- 本 change 现在同时拥有:
  - 工具侧自动化测试
  - Unity 编译验证
  - 真实 `.ply -> .splat4d -> Unity` batch verifier 证据
  - Unity EditMode XML 测试结果

## 2026-03-12 09:38:00 +0800 来源: 用户真实资产导入验证

### 资产路径

- 输入 `.ply`:
  - `/Users/cuiluming/Downloads/jimeng-sd2-point_cloud.ply`
- 导出 `.splat4d`:
  - `/Users/cuiluming/Downloads/jimeng-sd2-point_cloud.splat4d`
- 导入到项目:
  - `Assets/Gsplat/splat/jimeng-sd2-point_cloud.splat4d`

### 动态证据

- Python 导出:
  - `[OK] wrote 63,132 splats`
- Unity batch 导入日志:
  - `[Codex][UserImport] Imported Assets/Gsplat/splat/jimeng-sd2-point_cloud.splat4d: SplatCount=63132, SHBands=0, TimeModel=1, Velocities=63132, Times=63132, Durations=63132.`

### 结论

- 这份用户资产已经完成:
  - `.ply -> 单帧 .splat4d`
  - `.splat4d -> 当前 Unity 项目 importer`
- 当前 importer 口径识别为:
  - `TimeModel=1`
  - `SHBands=0`

## 2026-03-12 12:16:35 +0800 来源: 单帧 delta-v1 不对称的最小动态实验设计

### 现象

- 静态代码显示:
  - `TryInitShDeltaRuntime()` 只拦 `ShFrameCount <= 0`
  - `TryApplyShDeltaForTime()` 却在 `frameCount <= 1` 直接返回
- importer 同时允许:
  - `.splat4d v2`
  - `labelsEncoding=delta-v1`
  - `header.frameCount == 1`

### 当前假设

- 若资产满足:
  - `frameCount=1`
  - `shBands=1`
  - `labelsEncoding=delta-v1`
  - `startFrame=0` 的 base labels segment 存在
- 那么 `GsplatRenderer` 可能会:
  - 初始化 `m_sh1Segments`
  - 创建 `m_sh1CentroidsBuffer`
  - 置 `m_shDeltaInitialized=true`
  - 同时把 `m_shDeltaFrameCount` 设为 1
- 之后播放侧因为 `frameCount <= 1` 永远不进入真正的 delta apply

### 最强备选解释

- 运行时也许还有额外门禁,例如:
  - `m_renderer.SHBands <= 0`
  - compute shader 不可用
  - segment / centroid 校验提前失败
- 如果如此,那条“不对称路径”虽然存在于源码,但未必对真实资产可达

### 验证计划

1. 复制现有 `BuildMinimalSplat4DV2DeltaV1()` 的套路.
2. 构造一份 `frameCount=1` 的最小 `.splat4d v2 + delta-v1`.
3. 导入后创建 `GsplatRenderer`.
4. 用反射读取:
   - `m_shDeltaInitialized`
   - `m_shDeltaDisabled`
   - `m_shDeltaFrameCount`
   - `m_shDeltaCurrentFrame`
   - `m_sh1Segments`
   - `m_sh1CentroidsBuffer`
5. 如需进一步确认 no-op,再尝试推动一次 `TryApplyShDeltaForTime(1.0f)` 并观察状态是否仍停在 frame 0.

### 预期判定口径

- 若初始化字段都被建立,且 `m_shDeltaFrameCount == 1`:
  - 结论应写成:
    - “动态证据确认这条不对称路径可触发”
  - 但仍需区分:
    - 是否只是资源语义不对齐
    - 是否已经造成用户可见错误
- 若初始化没有发生:
  - 必须回滚旧假设
  - 明确说明是哪条动态证据推翻了“可触发”的判断

## 2026-03-12 12:24:25 +0800 来源: Unity 当前会话中的最小动态探针结果

### 验证方法

- 先在项目 `Assets/__CodexSingleFrameDeltaRuntimeProbe/` 下临时生成一份最小资产:
  - `.splat4d v2`
  - `shBands=1`
  - `labelsEncoding=delta-v1`
  - `frameCount=1`
  - `SHLB(startFrame=0, frameCount=1)`
  - `SHDL(startFrame=0, frameCount=1)`
- 再在 Unity 当前会话里创建一次性 Editor menu probe:
  - 强制导入该资产
  - `PrefabUtility.InstantiatePrefab(...)`
  - 通过反射读取 `GsplatRenderer` 私有字段
  - 再手动调用一次 `TryApplyShDeltaForTime(1.0f)`
  - 观察 `m_shDeltaCurrentFrame` 是否变化

### 动态证据

- Unity Console 关键输出:

```text
[Codex][SingleFrameDeltaProbe] Asset.ShFrameCount=1; Asset.Sh1DeltaSegments=1; Runtime.Initialized=True; Runtime.Disabled=False; Runtime.FrameCount=1; Runtime.CurrentFrameBefore=0; Runtime.CurrentFrameAfter=0; Runtime.Segments=1; Runtime.CentroidsBufferNull=False; Runtime.CentroidsBufferValid=True
```

### 结论

- 主假设成立:
  - `frameCount=1` 的 `.splat4d v2 + delta-v1` 在当前 runtime 中确实会进入动态 SH 初始化路径
- 被推翻的备选解释:
  - “运行时还有额外门禁,导致这条路径实际上不可达”
  - 这条说法已被动态证据否定
- 进一步确认的现象:
  - `m_shDeltaInitialized=True`
  - `m_shDeltaFrameCount=1`
  - `m_sh1Segments` 已建立
  - `m_sh1CentroidsBuffer` 已创建且 `IsValid()==true`
  - 但显式调用 `TryApplyShDeltaForTime(1.0f)` 后,`m_shDeltaCurrentFrame` 仍是 `0`
- 当前口径应更新为:
  - 这不再只是“静态候选风险”
  - 而是“当前代码中真实可触发的语义不对称路径”

### 还需要继续区分的边界

- 已确认的,是“初始化发生了,播放 no-op 也发生了”
- 还没确认的,是“这是否已经对当前用户资产造成可见渲染错误”
- 结合既有事实,当前更像:
  - 语义不对齐
  - 多余动态 SH 资源初始化
  - 对未来维护和未来 exporter 的契约风险
- 但不是当前单帧 raw v1 exporter 路径下的已证实用户可见故障

## 2026-03-12 16:32:00 +0800 来源: `s1-point_cloud.ply` 的 v2 + SH3 正式转换验证

### 现象

- 用户当前明确要的不是继续猜显示质量。
- 先要把 `Assets/Gsplat/ply/s1-point_cloud.ply` 转成“v2 + SH3”,不要现有的 v1 产物。

### 已验证事实

- `Assets/Gsplat/ply/s1-point_cloud.ply`
  - `vertex_count = 169133`
  - `f_rest_*` 字段数 = `45`
  - `restCoeffCount = 15`
  - 可严格推导为 `SH bands = 3`
- 现有旧产物:
  - `Assets/Gsplat/sog4d/s1_point_cloud_fixed_auto_20260311.sog4d`
  - `meta.version = 1`
  - `bands = 3`
  - `shNCentroidsType = f16`
  - `shNLabelsEncoding = delta-v1`
  - 说明它确实还是 v1 单 palette 方案,不是用户要的 v2 four-codebooks.
- 本轮新产物:
  - `Assets/Gsplat/sog4d/s1_point_cloud_v2_sh3_20260312.sog4d`
  - 打包命令使用:
    - `--sh-split-by-band`
    - `--shN-centroids-type f32`
    - `--sh0-codebook-method kmeans`
    - `--shN-labels-encoding delta-v1`
    - `--self-check`
- 新产物离线验证:
  - `meta.version = 2`
  - `bands = 3`
  - `streams.sh` 下存在 `sh1/sh2/sh3`
  - zip 内存在:
    - `sh1_centroids.bin`
    - `sh2_centroids.bin`
    - `sh3_centroids.bin`
  - `sh1/sh2/sh3 count` 均为 `8192`
  - `centroidsType = f32`
  - `labelsEncoding = delta-v1`
  - `validate ok (v2)`
- 体积对比:
  - 旧 v1 SH3: `3.1 MiB`
  - 新 v2 SH3: `3.8 MiB`
- Unity 侧导入验证:
  - `manage_asset search` 返回:
    - `Assets/Gsplat/sog4d/s1_point_cloud_v2_sh3_20260312.sog4d`
    - `assetType = UnityEngine.GameObject`
  - Console 中未检出与该资产导入相关的 error/warning

### 当前结论

- `s1-point_cloud.ply` 已经完成正式的 `SOG4D v2 + SH3` 转换。
- 这次不是“把 v1 修好”,而是新增了一份真正的 v2 four-codebooks 产物。
- 如果接下来还要继续比显示质量,应该优先拿这份新 bundle 做对照,而不是继续拿旧的 v1 产物当基线.

## 2026-03-12 16:55:00 +0800 来源: `.splat4d v2` 静态审计 + `s1-point_cloud.ply` 夹具检查

### 现象

- 用户最新目标已经收敛成:
  - 把 `Assets/Gsplat/ply/s1-point_cloud.ply` 转成 `.splat4d` 的 `v2 + SH3`
- 当前 `Tools~/Splat4D/ply_sequence_to_splat4d.py`
  - 仍只会写 raw v1 record 流
  - 不写 `SPL4DV02` header
  - 不写 `META/SHCT/SHLB` section
  - 不读取 `f_rest_*`
- 当前 importer/runtime
  - 已经能读 `.splat4d v2`
  - 已经能读 `shBands=0..3`
  - 已经能从 `SHCT + SHLB/SHDL` 还原 SH rest
- `Assets/Gsplat/ply/s1-point_cloud.ply`
  - `vertex_count = 169133`
  - `f_rest_*` 字段数 = `45`
  - 这等价于 `restCoeffCount = 15`
  - 满足 `SH bands = 3`

### 当前主假设

- 当前“没有 supersplat 那种质量感”的一个重要候选原因,是现有 `.splat4d` 导出路径还停留在 `v1 + SH0-only`.
- 如果把这份 PLY 按 `.splat4d v2 + SH3` 导出,至少能把高阶 SH 信息补回来,这是最值得先验证的一步.

### 当前最强备选解释

- 即使补了 `v2 + SH3`,观感仍可能和 supersplat 不完全一致.
- 那时剩余差异才更可能来自:
  - temporal / alpha kernel
  - tone mapping / post process
  - 排序与屏幕 footprint 处理
  - 或 SH 解码/量化策略差异

### 对实现范围的直接影响

- 这轮不应该继续碰 `.sog4d`.
- 最小正确目标是:
  - 给 `ply_sequence_to_splat4d.py` 增加一个显式 `v2` 导出路径
  - 支持从单帧普通 3DGS PLY 读取 `f_rest_*`
  - 写出 `RECS + META + SHCT + SHLB`
- 对单帧资产来说,优先使用 `labelsEncoding=full` 更自然:
  - 它不需要人为制造 `frameCount=1` 的 delta-v1 动态 SH 语义
  - 也能避开“单帧动态 SH 初始化但播放永远 no-op”的不对称路径

## 2026-03-12 17:52:00 +0800 来源: `s1-point_cloud.ply` 真实导出与 Unity 验证尝试

### 现象

- 已用真实源文件成功导出:
  - `Assets/Gsplat/splat/v2/s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d`
- 导出命令使用:
  - `--splat4d-version 2`
  - `--sh-bands 3`
  - `--sh-codebook-count 8192`
  - `--sh-centroids-type f32`
  - `--scale-mode log`
  - `--opacity-mode logit`
  - `--self-check`
- 离线解析结果:
  - `size = 13,314,270 bytes`
  - `magic = SPL4DV02`
  - `sectionCount = 8`
  - `splatCount = 169133`
  - `shBands = 3`
  - `timeModel = 1`
  - `frameCount = 1`
  - `META.sh1/sh2/sh3.count = 8192`
  - `META.sh1/sh2/sh3.centroidsType = 2(f32)`
  - `META.sh1/sh2/sh3.labelsEncoding = 1(full)`
- section table:
  - `RECS`
  - `META`
  - `SHCT/SHLB` for band1/2/3
- Python 回归:
  - `python3 -m unittest Tools~/Splat4D/tests/test_single_frame_cli.py`
  - `Ran 6 tests`
  - `OK`
- C# 编译:
  - `dotnet build Gsplat.Editor.csproj`
  - `0 warnings`
  - `0 errors`

### Unity 侧现象

- 当前机器上 Unity Editor 进程仍然持有同一项目锁:
  - `PID 85179`
  - `/Applications/Unity/Hub/Editor/6000.3.8f1/Unity.app/... -projectpath .../st-dongfeng-worldmodel`
- Unity MCP 当前不可直接读 Console / Asset:
  - `read_console` 返回 `Unity session not available`
  - `manage_asset` 返回 `no_unity_session`
- `refresh_unity(mode=force, scope=all, wait_for_ready=true)`
  - 触发了 refresh
  - 但等待 60s 后仍超时, editor readiness 没恢复
- `Editor.log` 中目前还没有这份新资产名的导入记录
- 输出文件旁也还没有生成 `.meta`

### 当前主假设

- 真实导出结果已经正确落盘.
- 当前没拿到 Unity 导入证据,更像是“打开中的 Editor 会话当前没有处于 MCP 可交互状态,且自动导入/刷新没有走到可观测完成态”.

### 当前最强备选解释

- 也不排除当前项目里 Auto Refresh 被临时关闭,或 Unity 正卡在别的编译/刷新任务上.
- 但在拿到 Console / AssetDatabase 动态证据前,不能把它说成 importer bug.

### 已补的验证入口

- `Editor/BatchVerifySplat4DImport.cs`
  - 新增 `VerifyS1PointCloudV2Sh3()`
  - 一旦 Unity 会话可用,就可以直接做 batchmode / 菜单验证:
    - `SplatCount`
    - `SHBands = 3`
    - `SHs.Length = splatCount * 15`
    - `Sh1/2/3Centroids` 长度
    - `ShFrameCount = 0`
    - `delta segments = null`

## 2026-03-12 18:26:00 +0800 来源: `s1-point_cloud.ply` SH 排列最小可证伪实验

### 现象

- 用户指定异常资产:
  - `Assets/Gsplat/splat/v2/s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d`
- 颜色观感:
  - 原本应偏灰色金属,现在明显偏彩
- 静态代码对照:
  - `Editor/GsplatImporter.cs` 把 PLY `f_rest_*` 当作 `RRR... GGG... BBB...` 读取
  - 当前 `Tools~/Splat4D/ply_sequence_to_splat4d.py::_read_ply_frame()` 直接 `reshape(N, coeff, 3)`
  - 这等价于把同一份数据按 `RGBRGB...` 交织解释

### 当前主假设

- exporter 在读取 PLY `f_rest_*` 时用了错误的通道排列解释
- 当前 `.splat4d v2` 资产因此把错位后的 SH 压缩进 `SHCT/SHLB`

### 当前最强备选解释

- 仍要保留“量化过强会染偏”的备选解释
- 但它只能解释“在正确布局上再有损”,不能解释“为什么当前资产更像另一种布局”

### 动态验证 1: 同一份 PLY 用两种排列解释的统计差异

- 实验对象:
  - `../../Assets/Gsplat/ply/s1-point_cloud.ply`
- 对比方式:
  - `interleaved`: 当前 exporter 的 `reshape(N, coeff, 3)`
  - `channel-major`: 与 `Editor/GsplatImporter.cs` 一致的 `RRR... GGG... BBB...`
- 关键输出:
  - `interleaved chroma mean/p95/max = 0.045657 / 0.148269 / 1.005215`
  - `channel-major chroma mean/p95/max = 0.014275 / 0.058608 / 0.828289`
  - `interleaved grayness fraction chroma<0.01 = 0.2267`
  - `channel-major grayness fraction chroma<0.01 = 0.6524`
- 解释:
  - 同一份 SH 原始数据,按 `channel-major` 解释时明显更接近灰色中性材质
  - `interleaved` 会显著放大 RGB 通道差异

### 动态验证 2: 直接反解当前 `.splat4d` 资产

- 反解对象:
  - `../../Assets/Gsplat/splat/v2/s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d`
- 反解结果:
  - `header.magic = SPL4DV02`
  - `band_infos = {1:(8192,2,1), 2:(8192,2,1), 3:(8192,2,1)}`
- 重建 SH 后与两种源解释的误差:
  - `asset vs interleaved: mae=0.008158, rmse=0.013644`
  - `asset vs channel-major: mae=0.028865, rmse=0.048649`
- 解释:
  - 当前资产压缩结果明显贴近 `interleaved`
  - 说明量化器是在忠实压缩“错误排列后的 SH”,而不是在“正确排列上稍微染偏”

### 结论

- 这轮已经有两类证据:
  - 静态证据: `Editor/GsplatImporter.cs` 的 PLY SH 读取契约
  - 动态证据: PLY 双解释统计 + 当前 `.splat4d` 反解误差
- 因此当前可以把主结论升级为:
  - `Tools~/Splat4D/ply_sequence_to_splat4d.py::_read_ply_frame()` 的 `f_rest_*` 排列解释与现有仓库契约不一致
  - 这就是当前 `s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d` 颜色偏彩的直接原因
- 量化仍然有损,但目前不是主要矛盾

## 2026-03-12 18:52:00 +0800 来源: SH 排列修复后的重导出 + Unity 闭环

### 现象

- 修复 `_read_ply_frame()` 后,已重导出:
  - `Assets/Gsplat/splat/v2/s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d`
- 离线反解新资产后:
  - `asset vs interleaved: mae=0.029000, rmse=0.048913`
  - `asset vs channel-major: mae=0.007287, rmse=0.012755`
- 这说明新资产已经明显贴近正确的 `channel-major` 解释,不再贴近旧的错误 `interleaved` 解释.

### Unity 侧新现象与验证

- 第一次重跑 batch verify 时,报错:
  - `Single-frame full-label asset should not carry delta-v1 segments.`
- 静态审计发现:
  - importer 在进入 v2 解码前,已经先把 `ShFrameCount` 与 `Sh*DeltaSegments` 清空
  - runtime 在 `TryInitShDeltaRuntime()` 中也把 `null` 与 `Length==0` 都视为“无 delta-v1”
- 因此新的主假设变成:
  - 不是 importer 又把 full-label 误解成 delta-v1
  - 而是 batch verify 对“无 delta”判定过严,把 empty array 也当成了异常

### 动态验证

- 修改 batch verify 后重新执行 Unity 菜单:
  - `Tools/Gsplat/Batch Verify/Verify s1_point_cloud_v2_sh3`
- Console 成功输出:
  - `[Gsplat][BatchVerify] Imported Assets/Gsplat/splat/v2/s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d: SplatCount=169133, SHBands=3, TimeModel=1, SHs=2536995, Sh1Centroids=24576, Sh2Centroids=40960, Sh3Centroids=57344`
- 新增 Editor 测试并运行:
  - `Gsplat.Tests.GsplatSplat4DImporterDeltaV1Tests.ImportV2_FullLabels_DoesNotExposeUsableDeltaSegments`
  - `Passed (1/1)`

### 结论

- 当前用户指定资产的“灰金属变彩色”主因已经修复:
  - 直接原因是 exporter 错把 `f_rest_*` 当成 `RGBRGB...`
  - 修复后新资产已重新贴近正确的 `RRR... GGG... BBB...` 契约
- Unity 侧后来暴露的异常不是新资产格式又错了
  - 而是 batch verify 对“无 delta”语义判断过严
  - 已改成和 runtime 一致的“segmentCount > 0 才算真的带 delta”

## 2026-03-12 19:55:00 +0800 来源: Unity `Gsplat` vs SuperSplat 脏感/局部颜色更明显 的对照分析

### 现象

- 用户反馈:
  - 同一份 `PLY` 在 SuperSplat 在线版里看起来更干净.
  - Unity 当前 `GsplatRenderer` 里某些局部颜色更明显,整体更"脏".
- 当前场景里的目标对象已确认是:
  - `s1_point_cloud_v2_sh3_full_k8192_f32_20260312`
- 当前对象的动态状态已确认:
  - `RenderStyle = Gaussian`
  - `EnableVisibilityAnimation = false`
  - `SHDegree = 3`
  - `GammaToLinear = true`
- 因此本轮观感差异不是由 `ParticleDots` 过渡逻辑,也不是由可见性动画造成的.

### 当前主假设

- Unity 当前高斯路径相比 PlayCanvas / SuperSplat,缺少那条 splat footprint 的 AA compensation.
- 这会让很多小 footprint splat 的 alpha 能量偏高.
- 在高阶 SH 和局部高对比区域里,这种额外能量会把颜色差异显得更脏、更碎.

### 当前最强备选解释

- 当前 Unity 场景不是纯 shader 对照环境.
- 它处在 HDRP Volume 后处理链下.
- `Tonemapping / Exposure / ColorCurves / FilmGrain / LiftGammaGain / Bloom` 都处于 active.
- 因此用户看到的"脏感 / 颜色更显"里,至少有一部分可能来自场景后处理,而不是高斯核算法本身.

### 静态证据 1: 主核本身基本相同

- Unity 当前 Gaussian 主核:
  - `Runtime/Shaders/Gsplat.shader:916`
  - `Runtime/Shaders/Gsplat.shader:942`
- PlayCanvas / SuperSplat 当前主核:
  - `/tmp/playcanvas-engine.V7a3ug/src/scene/shader-lib/glsl/chunks/gsplat/frag/gsplat.js:32`
  - `/tmp/supersplat.vkbHFh/src/shaders/splat-shader.ts:163`
- 结论:
  - 两边现在都在用同一类 `normExp` 归一化高斯核.
  - 所以当前"更脏"现象,不能先怪到 fragment 里的高斯衰减公式不同.

### 静态证据 2: Unity 保留了 `aaFactor` 数学,但主渲染链没有真正用起来

- Unity 公共 HLSL 里仍保留了 PlayCanvas 同源的 `aaFactor` 结构与计算:
  - `Runtime/Shaders/Gsplat.hlsl:33`
  - `Runtime/Shaders/Gsplat.hlsl:106`
- 但 Unity 主 shader 在顶点阶段构建 `color/baseAlpha/finalAlpha` 时,没有任何地方把 `corner.aaFactor` 乘回 alpha:
  - `Runtime/Shaders/Gsplat.shader:832`
  - `Runtime/Shaders/Gsplat.shader:887`
- 同时材质创建时只启用了 `SH_BANDS_x` keyword,没有任何 `GSPLAT_AA` keyword 路径:
  - `Runtime/GsplatSettings.cs:240`
- 对照 PlayCanvas engine,它在 vertex 阶段明确执行:
  - `clr.a *= corner.aaFactor`
  - 位置:
    - `/tmp/playcanvas-engine.V7a3ug/src/scene/shader-lib/glsl/chunks/gsplat/vert/gsplat.js:57`
    - `/tmp/playcanvas-engine.V7a3ug/src/scene/shader-lib/glsl/chunks/gsplat/vert/gsplatCorner.js:44`
- 当前结论:
  - Unity 这边确实存在一条已确认的算法差异.
  - 而且不是"也许漏了",而是代码链路上确实断开了.

### 动态证据 1: `aaFactor` 的量级不是小修小补

- 用 `detOrig / detBlur` 公式做了一个最小数值实验.
- 当 splat footprint 偏小时,`aaFactor` 会明显小于 1:
  - `(d1,d2,off)=(0.01,0.01,0.0) -> 0.032258`
  - `(0.03,0.02,0.005) -> 0.073799`
  - `(0.10,0.06,0.015) -> 0.200417`
  - `(0.20,0.12,0.02) -> 0.335552`
  - `(0.40,0.20,0.03) -> 0.476007`
- 解释:
  - 如果缺少这条补偿,很多小高斯的 alpha 会比 PlayCanvas 路径更大.
  - 这不是边角级别差异,而是可能到数倍量级的能量差.
- 但注意:
  - 这还是通用数学量级实验.
  - 还不是对当前 `s1` 视角下的逐 splat 实测分布.

### 静态 + 动态证据 3: 当前 Unity 场景存在明显后处理混入

- 当前活跃 scene:
  - `Assets/OutdoorsScene.unity`
- 当前目标对象动态状态:
  - `GammaToLinear = true`
- 当前 scene 的 HDRP 全局 Volume 动态读取结果显示:
  - `Exposure` active
  - `Tonemapping` active
  - `ColorCurves` active
  - `FilmGrain` active
  - `LiftGammaGain` active
  - `Bloom` active
- 另外,定向截图时画面明显受到强曝光/后处理影响,不是一个干净的 neutral render.
- 对照 SuperSplat 默认配置:
  - 默认 camera 是 `exposure = 1.0`, `toneMapping = linear`
    - `/tmp/supersplat.vkbHFh/src/scene-config.ts:18`
  - 默认 post effects 里 `bloom / grading / vignette / fringing` 都是 disabled
    - `/tmp/supersplat.vkbHFh/src/splat-serialize.ts:111`
- 当前结论:
  - 目前不能把所有观感差异都归因到高斯算法.
  - 至少已经确认: 当前 Unity 场景级后处理与 SuperSplat 默认观察环境不对齐.

### 颜色输出路径对照

- SuperSplat / PlayCanvas 会在 vertex 阶段把 gamma-space 颜色送进 `prepareOutputFromGamma(...)`:
  - `/tmp/supersplat.vkbHFh/src/shaders/splat-shader.ts:134`
  - `/tmp/playcanvas-engine.V7a3ug/src/scene/shader-lib/glsl/chunks/gsplat/vert/gsplatOutput.js:8`
- Unity 当前路径是:
  - 片元阶段根据 `_GammaToLinear` 决定是否 `GammaToLinearSpace(i.color.rgb)`
  - `Runtime/Shaders/Gsplat.shader:957`
  - C# 侧每帧把这个开关传进 shader
  - `Runtime/GsplatRendererImpl.cs:502`
- 当前判断:
  - 这是一个真实差异点.
  - 但仅从现有证据还不能证明它就是"脏感"主因.
  - 因为场景级 HDRP 后处理变量目前更强,优先级更高.

### 验证计划

- 如果后续要继续,最小可证伪实验建议按这个顺序:
  1. 在 Unity 里做一组"纯对照"截图:
     - 同一对象
     - 关闭 HDRP Volume 的 `Exposure / Tonemapping / FilmGrain / Bloom / ColorCurves / LiftGammaGain`
     - 保持其它条件不变
  2. 再做一组 shader A/B:
     - 只补 `aaFactor` compensation
     - 不动高斯主核和 SH 路径
  3. 用同一 camera pose 对比 SuperSplat
     - 先对齐后处理
     - 再看高斯本身是否仍然偏脏

### 目前结论

- 已确认结论:
  1. Unity 当前和 PlayCanvas / SuperSplat 确实存在算法差异.
     - 差异点不是 `normExp` 主核.
     - 而是 PlayCanvas 那条 `aaFactor` AA compensation 在 Unity 当前主渲染链里没有生效.
  2. 当前 Unity 场景也确实混入了比 SuperSplat 默认更重的 HDRP 后处理.
- 还不能直接下的结论:
  - 还不能把用户眼前全部"脏感 / 局部颜色更显"都单独归因到缺 AA compensation.
- 当前最稳妥的判断是:
  - 这是"至少两个因素叠加"的问题:
    - 因素A: Unity 当前缺少 PlayCanvas 的 footprint AA compensation
    - 因素B: Unity 当前 HDRP 后处理环境与 SuperSplat 默认观察环境不一致
  - 若只让我按目前证据排优先级:
    - 先隔离场景后处理
    - 再验证 `aaFactor` 差异是否解释剩余的质量落差

## 2026-03-12 20:34:00 +0800 来源: `GSPLAT_AA` 不能默认全局开启,应改成 renderer 级对照开关

### 新现象

- 第一版实现把 `GSPLAT_AA` 直接默认启用在所有 `Gsplat` 材质上.
- 动态截图结果不是"轻微变干净",而是整体显著变暗、变稀.
- 这说明之前那个"直接默认开启就更接近 SuperSplat"的做法不成立.

### 上一假设为什么不成立

- 新证据来自 PlayCanvas engine 源码参数层:
  - `gsplat-unified/gsplat-params.js` 明确写着:
    - `antiAlias` 默认 `false`
    - 仅建议给"带 anti-aliasing 训练/导出"的 splat 数据使用
- 也就是说:
  - `aaFactor` 这条链路本身确实存在
  - 但它不是 PlayCanvas 默认对所有 splat 一刀切启用的路径

### 新结论

- 之前的口径需要回滚:
  - 不能把"Unity 少了 `aaFactor` 链路"直接等价成"应该默认全局开启 `GSPLAT_AA`"
- 更准确的结论是:
  - `aaFactor` 应作为可切换的对照能力存在
  - 默认值应保持 `false`,与 PlayCanvas / SuperSplat 默认一致
  - 需要 A/B 时,在具体 renderer 上单独打开

### 本轮落地

- 保留 shader 内的 `GSPLAT_AA` 路径
- 但把开关从全局材质默认值,收回到 renderer 级布尔字段:
  - `GsplatRenderer.EnableFootprintAACompensation`
  - `GsplatSequenceRenderer.EnableFootprintAACompensation`
- 当前激活对象 `s1_point_cloud_v2_sh3_full_k8192_f32_20260312` 已被设置为 `true`,用于现场 A/B

## 2026-03-12 20:48:00 +0800 来源: `PLY` 导入基本解码公式与 PlayCanvas 静态一致, 应转查低 alpha 处理与细节保留策略

### 现象

- 用户反馈再次确认:
  - `EnableFootprintAACompensation=false` 时观感更正常
  - `true` 时明显丢细节
- 这直接推翻了上一轮把 `aaFactor` 当作主要修复方向的判断.

### 静态对照结果

- Unity `Editor/GsplatImporter.cs` 的基础公式:
  - `opacity -> sigmoid`
  - `scale -> exp`
  - `rotation -> normalize`
  - `f_rest_* -> channel-major`
- PlayCanvas `GSplatData` 的基础读取公式也同样是:
  - `opacity -> sigmoid`
  - `scale -> exp`
  - `f_dc -> 0.5 + SH_C0 * dc`
  - `rotation` 读取后再 normalize
- 因此当前还没有证据支持"Unity 的 PLY 基础解码公式写错了".

### 结论更新

- 上一主假设不成立:
  - `aaFactor` 不是当前这份数据的正确修复方向
- 当前也没有证据支持:
  - `PLY importer` 的 `opacity/scale/rotation/f_dc/f_rest` 基础解释本身错误
- 下一优先级候选:
  - 低 alpha splat 的保留策略
  - 例如 PlayCanvas / SuperSplat 的 dither / discard 语义,是否比 Unity 当前更能保住细节

## 2026-03-12 19:55:00 +0800 第二轮: `EnableFootprintAACompensation` 为什么一开就掉细节

### 现象

- 用户再次确认:
  - `EnableFootprintAACompensation=false` 时观感还好
  - `true` 时细节明显变少
- 用 Unity MCP 重新读取当前 scene:
  - `mcpforunity://scene/volumes` 返回 `Found 0 volume(s)`
  - 当前 HDRP scene 已经没有 Volume 组件
- 这推翻了上一轮一个旧前提:
  - 不能再把当前这次观感问题继续归到 scene Volume / post FX 混入

### 静态对照

#### Unity 当前实现

- `Runtime/Shaders/Gsplat.hlsl`
  - `InitCorner(...)` 内的 `aaFactor` 公式与 PlayCanvas 一致
  - 小 footprint 早退条件也是 `l1 < 2.0 && l2 < 2.0`
  - `ClipCorner(...)` 数学式也与 PlayCanvas 一致
- `Runtime/Shaders/Gsplat.shader`
  - 开启 `GSPLAT_AA` 时:
    - vertex 里 `gaussAlphaMul = gaussCorner.aaFactor`
    - `ClipCorner(gaussCorner, max(baseAlpha * gaussAlphaMul, 1.0 / 255.0))`
    - fragment 里 `alphaGauss = EvalNormalizedGaussian(A) * baseAlpha * gaussAlphaMul`

#### PlayCanvas / SuperSplat 当前实现

- `playcanvas-engine/src/scene/shader-lib/glsl/chunks/gsplat/vert/gsplat.js`
  - `clr.a *= corner.aaFactor`
  - 然后 `clipCorner(corner, clr.w)`
- `playcanvas-engine/src/scene/shader-lib/glsl/chunks/gsplat/frag/gsplat.js`
  - forward pass 里 `alpha = normExp(A) * gaussianColor.a`
- `playcanvas-engine/src/scene/gsplat-unified/gsplat-params.js`
  - `antiAlias` 默认 `false`
  - 注释明确说明: 仅适用于启用 anti-aliasing 训练/导出的 splat 数据
  - 若源数据不是这种形态,开启后可能 `soften the image or alter opacity`

### 动态证据

- 用 Unity MCP 在目标对象 `s1_point_cloud_v2_sh3_full_k8192_f32_20260312` 上顺序切换并回读:
  - 打开后回读 `EnableFootprintAACompensation=true`
  - 截图后恢复并回读 `EnableFootprintAACompensation=false`
- 离线比较 `Assets/Screenshots/aa_off_seq.png` 与 `Assets/Screenshots/aa_on_seq.png`:
  - `mean_rgba = [0.4485, 0.4325, 0.4375, 0.0]`
  - `changed_rgb = 1,398,500 px = 67.44%`
  - `max_channel_delta >= 2` 的像素约 `4.34%`
  - `mean_luma_delta = -0.000238`
- 解释:
  - 变化不是“整张图显著变暗”
  - 更像是大量细微像素都被轻度削弱/重分布
  - 这与用户描述的“细节被吃掉,不是整体风格大变”一致

### 当前主假设

- 当前最可信的解释不是“Unity 算法写错了”
- 而是:
  - 当前这份 splat 数据不满足 PlayCanvas `antiAlias=true` 那条数据契约
  - 因此 `aaFactor < 1` 会同时降低 `ClipCorner` 的有效半径和最终 alpha
  - 最先消失的是小 footprint / 低 alpha / 高频边缘 splat

### 最强备选解释

- Unity 与 SuperSplat 在颜色输出链仍有差异
  - Unity 目前在 fragment 末尾按 `_GammaToLinear` 做 `GammaToLinearSpace`
  - SuperSplat / PlayCanvas 则走 `prepareOutputFromGamma(...)`
- 这条差异更像是“颜色脏感/层次感”问题
- 但它不能直接解释“为什么一开 AA 补偿就少细节”

### 当前结论

- 目前已有足够证据支持下面这条结论:
  - 对当前这份 `s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d` 数据, `EnableFootprintAACompensation` 不应默认开启
- 更准确地说:
  - 这不是一个“应该全局打开但 Unity 实现错了”的问题
  - 而是这个选项本来就只适合 AA-trained / AA-exported 数据
- 后续如果继续追“为什么 Unity 仍不如 SuperSplat 干净”,优先级应转到:
  1. 颜色输出链 / gamma-linear 路径
  2. 是否存在 SuperSplat 编辑器级的观察参数差异
