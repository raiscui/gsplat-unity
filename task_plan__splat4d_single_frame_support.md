# 任务计划: 单帧普通 3DGS `.ply -> .splat4d` OpenSpec 立项

## 目标

创建一条新的 OpenSpec change。
它用于规划:
- `Tools~/Splat4D/ply_sequence_to_splat4d.py` 支持单个普通 3DGS `.ply` 生成单帧 `.splat4d`
- Unity `GsplatRenderer` 支持这份单帧 `.splat4d` 正常显示

## 阶段

- [x] 阶段1: 读取历史上下文与现有变更
- [x] 阶段2: 建立本次支线文件上下文
- [x] 阶段3: 创建 OpenSpec change
- [x] 阶段4: 读取首个 artifact instructions 并向用户汇报

## 关键问题

1. 这次 change 的边界是否应明确为“普通 3DGS `.ply` 单帧输入”,而不是 4D PLY?
2. `GsplatRenderer` 当前对 `.splat4d` 的单帧显示语义,是已经具备基础能力,还是需要正式补规格?
3. 这次 change 名应否延续既有命名风格,采用 `splat4d-single-frame-ply-support`?

## 候选方向

- 方向A: 最佳方案
  - 把“单个 `.ply` 文件输入”补成 `ply_sequence_to_splat4d.py` 的正式入口
  - 同时把 importer / runtime / README / tests / OpenSpec 一起补齐
  - 让 `GsplatRenderer` 对单帧 `.splat4d` 的显示语义成为正式契约
- 方向B: 先能用方案
  - 仅在脚本层接受单个 `.ply`
  - 其余路径尽量复用现有 `.splat4d` 导入与显示逻辑
  - 后续再看是否需要细化更强的显示语义与回归测试

## 做出的决定

- 当前先不进入实现。
- 先按 `openspec-new-change` workflow 建立 change 脚手架,停在第一个 artifact 模板。
- change 名先采用:
  - `splat4d-single-frame-ply-support`
  - 理由: 与现有 `sog4d-single-frame-ply-support` 风格一致,也最贴近用户意图
- 根据用户最新澄清,这次 proposal 的职责边界调整为:
  - “普通 3DGS 单帧 `.ply -> .splat4d`” 是工具侧职责
  - “`.splat4d -> GsplatRenderer`” 不是从零新增能力
  - 本次更准确的目标是:
    - 增加或确保单帧 `.splat4d` 在 Unity 中正常工作

## 遇到的错误

- (待补)

## 状态

**目前已完成本轮 OpenSpec 四件套创建**
- 已确认仓库里不存在同名 change。
- `openspec new change "splat4d-single-frame-ply-support"` 已执行成功。
- `proposal.md` 已创建完成。
- `openspec status --change "splat4d-single-frame-ply-support"` 当前已确认:
  - `Progress: 4/4 artifacts complete`
  - `proposal` done
  - `design` done
  - `specs` done
  - `tasks` done
  - `All artifacts complete!`
- 当前这条 change 已进入 apply-ready 状态。

## 2026-03-11 22:22:27 +0800 追加行动

- 当前行动目的:
  - 为这次新的 `splat4d` 支线立项建立独立工作记忆
  - 避免和前一条 `.sog4d` 单帧任务混淆
- 下一步行动:
  - [x] 创建 OpenSpec change `splat4d-single-frame-ply-support`
  - [ ] 运行 `openspec status --change "splat4d-single-frame-ply-support"`
  - [ ] 读取首个 ready artifact 的 instructions

## 2026-03-11 22:22:27 +0800 阶段进展

- 已创建:
  - `openspec/changes/splat4d-single-frame-ply-support/`
- workflow:
  - `spec-driven`
- 当前待做:
  - [x] 读取 `openspec status --change "splat4d-single-frame-ply-support"`
  - [x] 识别第一个 ready artifact
  - [x] 读取对应 instructions

## 2026-03-11 22:22:27 +0800 最终结果

- change:
  - `splat4d-single-frame-ply-support`
- location:
  - `openspec/changes/splat4d-single-frame-ply-support/`
- schema:
  - `spec-driven`
- artifact sequence:
  - `proposal -> design -> specs -> tasks`
- current progress:
  - `0/4`
- first ready artifact:
  - `proposal`
- proposal 输出路径:
  - `openspec/changes/splat4d-single-frame-ply-support/proposal.md`

## 2026-03-11 22:22:27 +0800 用户澄清后的下一步

- 新的边界确认:
  - 普通 3DGS 单帧 `.ply -> .splat4d` 属于工具能力
  - `.splat4d -> GsplatRenderer` 现有链路已存在
  - 本次要补的是“单帧 `.splat4d`”在 Unity 中的正式保证与验收
- 下一步行动:
  - [x] 读取相关现有 specs,避免 proposal 把既有能力误写成全新能力
  - [x] 起草 `proposal.md`
  - [x] 运行 `openspec status --change "splat4d-single-frame-ply-support"` 刷新进度

## 2026-03-11 22:22:27 +0800 `proposal` 完成结果

- 已创建:
  - `openspec/changes/splat4d-single-frame-ply-support/proposal.md`
- 已写明的边界:
  - 工具侧新增:
    - 普通单帧 `.ply -> .splat4d`
  - Unity 侧修改:
    - 现有 `.splat4d -> GsplatRenderer` 路径对单帧资产的正式保证
- capabilities:
  - New:
    - `splat4d-ply-conversion`
  - Modified:
    - `4dgs-core`
    - `4dgs-playback-api`
- 当前解锁:
  - `design`
  - `specs`

## 2026-03-11 22:22:27 +0800 继续步骤: 创建 `design`

- 当前行动目的:
  - 为 `proposal` 里已经确定的 capability 边界补齐技术设计
  - 说明“为什么复用现有 `.splat4d` / `GsplatRenderer` 主链路”以及“工具侧单帧入口怎么补”
- 新增静态证据:
  - `Tools~/Splat4D/ply_sequence_to_splat4d.py`
    - 现有 `average` 模式在 `len(ply_files)==1` 时会自然写出:
      - `velocity = 0`
      - `time0 = 0`
      - `duration = 1`
  - `README.md`
    - 已经有 `--input-ply` 文案
  - 真实 CLI
    - 还没有 `--input-ply`
- 下一步行动:
  - [x] 起草 `openspec/changes/splat4d-single-frame-ply-support/design.md`
  - [x] 运行 `openspec status --change "splat4d-single-frame-ply-support"` 刷新状态

## 2026-03-11 22:22:27 +0800 `design` 完成结果

- 已创建:
  - `openspec/changes/splat4d-single-frame-ply-support/design.md`
- 设计核心取舍:
  - 继续复用现有 `.splat4d -> GsplatAsset -> GsplatRenderer` 主链路
  - 工具侧新增 `--input-ply`,不额外发明单帧包装脚本
  - 单帧导出直接复用现有 `average` 语义:
    - `velocity = 0`
    - `time = 0`
    - `duration = 1`
  - Unity 侧优先用契约、测试、最小 guard 硬化现有行为,不新增单帧专用 renderer 分支
- 当前解锁:
  - `specs`

## 2026-03-11 22:22:27 +0800 继续步骤: 创建 `specs`

- 当前行动目的:
  - 把 `proposal` 和 `design` 里的边界收敛成正式 requirement
  - 一次性创建 3 份对应 capability 的 spec 文件
- 当前取舍:
  - `splat4d-ply-conversion`
    - 新 capability,使用 `ADDED Requirements`
  - `4dgs-core`
    - 已有 capability,本次通过 `ADDED Requirements` 补“单帧静态 `.splat4d`”语义
  - `4dgs-playback-api`
    - 已有 capability,本次通过 `ADDED Requirements` 补“播放控制下的固定帧退化”语义
- 下一步行动:
  - [x] 创建 `openspec/changes/splat4d-single-frame-ply-support/specs/splat4d-ply-conversion/spec.md`
  - [x] 创建 `openspec/changes/splat4d-single-frame-ply-support/specs/4dgs-core/spec.md`
  - [x] 创建 `openspec/changes/splat4d-single-frame-ply-support/specs/4dgs-playback-api/spec.md`
  - [x] 运行 `openspec status --change "splat4d-single-frame-ply-support"` 刷新状态

## 2026-03-11 22:22:27 +0800 `specs` 完成结果

- 已创建:
  - `openspec/changes/splat4d-single-frame-ply-support/specs/splat4d-ply-conversion/spec.md`
  - `openspec/changes/splat4d-single-frame-ply-support/specs/4dgs-core/spec.md`
  - `openspec/changes/splat4d-single-frame-ply-support/specs/4dgs-playback-api/spec.md`
- 这轮采用的 delta 策略:
  - 新 capability:
    - `splat4d-ply-conversion` 使用 `ADDED Requirements`
  - 已有 capability:
    - `4dgs-core` 使用 `ADDED Requirements`
    - `4dgs-playback-api` 使用 `ADDED Requirements`
- 当前解锁:
  - `tasks`

## 2026-03-11 22:22:27 +0800 继续步骤: 创建 `tasks`

- 当前行动目的:
  - 把 `proposal / design / specs` 收敛成可直接执行的实现清单
  - 让这条 change 进入 apply-ready 状态
- 当前拆分原则:
  - 工具入口与默认导出语义单独成组
  - Unity importer / runtime 的单帧保证单独成组
  - 测试与真实夹具单独成组
  - 文档与最终验证单独成组
- 下一步行动:
  - [x] 起草 `openspec/changes/splat4d-single-frame-ply-support/tasks.md`
  - [x] 运行 `openspec status --change "splat4d-single-frame-ply-support"` 刷新状态

## 2026-03-11 22:22:27 +0800 `tasks` 完成结果

- 已创建:
  - `openspec/changes/splat4d-single-frame-ply-support/tasks.md`
- 任务分组:
  - `Exporter And CLI`

## 2026-03-11 22:56:00 +0800 只读探索: EditMode 单帧 `.splat4d` 测试方案摸底

- 当前行动目的:
  - 只读定位“最小、最稳”的 Unity EditMode 测试落点
  - 回答现有测试文件、可复用 helper / 反射入口、最值当的 runtime guard 位置
- 当前约束:
  - 不改任何生产代码
  - 只给静态证据与建议
- 下一步行动:
  - [x] 盘点 `Tests/Editor/` 现有测试文件与夹具
  - [x] 盘点 `.splat4d` importer / runtime / playback 代码链路
  - [x] 汇总最适合的测试落点与最小 guard 候选点
- 当前状态:
  - 已完成只读探索
  - 已确认:
    - 最适合继续补测试的现有文件是 `Tests/Editor/GsplatSplat4DImporterDeltaV1Tests.cs`
    - 最适合借反射测试手法的现有文件是 `Tests/Editor/GsplatVisibilityAnimationTests.cs`
    - 最值当的最小 runtime guard 候选点是 `Runtime/GsplatRenderer.cs` 的 `TryInitShDeltaRuntime()`

## 2026-03-11 22:47:06 +0800 进入 apply 阶段

- 当前行动目的:
  - 从 `tasks.md` 的第一个未完成项开始正式实现
  - 先补 `Tools~/Splat4D/ply_sequence_to_splat4d.py` 的单文件入口,再补测试
- 当前静态证据:
  - `openspec instructions apply --change "splat4d-single-frame-ply-support" --json`
    - `progress: 0/13`
    - 当前状态: `ready`
  - `Tools~/Splat4D/ply_sequence_to_splat4d.py`
    - 仍只接受 `--input-dir`
    - `average` 模式在单帧时已天然写出 `vx/vy/vz = 0`, `time = 0`, `duration = 1`
  - Python `argparse` 官方文档
    - 已确认 `add_mutually_exclusive_group(required=True)` 适合约束 `--input-ply` / `--input-dir`
- 当前主假设:
  - 缺口主要在 CLI 正式入口、错误语义与回归测试
  - Unity 侧更可能只需要最小 guard 和测试锁定
- 当前最强备选解释:
  - `GsplatRenderer` 可能对异常 4D 数组长度缺少防御,导致单帧 `.splat4d` 在边界资产下有隐藏风险
- 推翻当前主假设的证据:
  - 如果单帧 `.splat4d` 在 importer / runtime 现有测试夹具里表现出播放控制异常
  - 或者 `Has4DFields` / `SetData` 路径出现数组长度不一致导致的错误
- 下一步行动:
  - [ ] 先修改 `Tools~/Splat4D/ply_sequence_to_splat4d.py`
  - [ ] 新增 `Tools~/Splat4D/tests/test_single_frame_cli.py`
  - [ ] 运行 Python 回归测试

## 2026-03-11 23:46:18 +0800 apply 阶段进展

- 已完成的实现项:
  - `Tools~/Splat4D/ply_sequence_to_splat4d.py`
    - 新增 `--input-ply`
    - `--input-ply` / `--input-dir` 改为 required mutually exclusive group
    - 新增统一输入归一 helper
    - 新增统一 CLI 错误前缀,把 traceback 收敛为明确失败语义
  - `Tools~/Splat4D/tests/test_single_frame_cli.py`
    - 已覆盖:
      - 互斥参数失败
      - 单帧 `.ply -> .splat4d`
      - `vx/vy/vz = 0`
      - `time = 0`
      - `duration = 1`
      - 缺少 Gaussian 字段时失败
      - 单帧误用 `keyframe` 模式时失败
  - `Tools~/Splat4D/tests/data/single_frame_valid_3dgs.ply`
    - 已作为仓库内正式单帧 3DGS 夹具落盘
  - `Runtime/GsplatRenderer.cs`
    - `Has4DFields(...)` 已补长度 guard
  - Unity EditMode 测试
    - `Tests/Editor/GsplatSplat4DImporterDeltaV1Tests.cs`
      - 已补单帧 raw v1 `.splat4d` importer test
    - `Tests/Editor/GsplatVisibilityAnimationTests.cs`
      - 已补静态单帧 4D runtime 逻辑测试
  - 文档
    - `README.md`
    - `Tools~/Splat4D/README.md`
    - 已补 `--input-ply`、单帧默认值和固定帧语义
- 当前验证证据:
  - `python3 -m unittest Tools~/Splat4D/tests/test_single_frame_cli.py`
    - `Ran 4 tests`
    - `OK`
  - `python3 Tools~/Splat4D/ply_sequence_to_splat4d.py --input-ply Tools~/Splat4D/tests/data/single_frame_valid_3dgs.ply --output /tmp/single_frame_valid_3dgs.splat4d --mode average --opacity-mode linear --scale-mode linear`
    - 导出成功
  - 对 `/tmp/single_frame_valid_3dgs.splat4d` 解包检查:
    - `records = 1`
    - `vx/vy/vz = 0/0/0`
    - `time = 0`
    - `duration = 1`
  - `dotnet build Gsplat.Tests.Editor.csproj`
    - `0 errors`
    - 有 4 个既有 warning,来自无关旧文件:
      - `Tests/Editor/GsplatLidarScanTests.cs`
      - `Tests/Editor/GsplatSog4DImporterTests.cs`
  - `dotnet build Gsplat.Editor.csproj`
    - `0 warnings`
    - `0 errors`
- 当前剩余 OpenSpec tasks:
  - `3.4`
  - `4.2`
- 当前阻塞:
  - 尝试运行 Unity EditMode CLI 时,不是测试失败,而是启动前被项目锁挡住
  - 现象:
    - `Unity` 进程 `PID 7588` 正在打开同一项目
    - `lsof` 已确认它持有 `Temp/UnityLockfile`
  - 因此当前不能安全完成:
    - `.ply -> .splat4d -> Unity` 的真实闭环验证
    - 新增 EditMode 测试的实际执行验证
- 下一步行动:
  - 等待用户确认是否可以关闭当前 Unity 实例
  - 一旦项目锁释放:
    - 先跑定向 EditMode tests
    - 再完成真实闭环验证
    - 最后勾掉 `3.4` / `4.2`
  - `Unity Importer And Runtime Semantics`
  - `Tests And Fixtures`
  - `Docs And Final Verification`
- 最终状态:
  - `Progress: 4/4 artifacts complete`
  - `All artifacts complete!`
- 当前结论:
  - `splat4d-single-frame-ply-support` 已完成 OpenSpec 四件套
  - 下一步可直接进入实现阶段

## 2026-03-11 23:59:00 +0800 收尾阶段继续推进

- 当前行动目的:
  - 在真实 `.ply -> .splat4d -> Unity` 闭环已通过的前提下,清掉 batchmode / `-nographics` 下新增的 `Kernel 'BuildAxes' not found` error
  - 让 `3.4` 与 `4.2` 的验证结果变成“功能通过且日志干净”
- 已观察到的现象:
  - `Gsplat.Editor.BatchVerifySplat4DImport.VerifyStaticSingleFrameFixture`
    - 已经输出:
      - `[Gsplat][BatchVerify] Exporter ok.`
      - `[Gsplat][BatchVerify] Imported ... Runtime samples(0,0.35,1) all kept full sort range.`
  - 但同一轮 batch log 中仍出现:
    - `Kernel 'BuildAxes' not found`
- 当前主假设:
  - 在 `-batchmode -nographics` 下,`Runtime/VFX/GsplatVfxBinder.cs` 的 `EnsureKernelsReady()` 仍会触发 `ComputeShader.FindKernel(...)`
  - 即使调用点被 `try/catch` 包住,Unity 也会先把 kernel missing 记为 error
- 最强备选解释:
  - 不是 `FindKernel` 时机问题,而是 importer 在 headless 验证链路里根本不该创建或激活 VFX 绑定组件
- 最小验证计划:
  - 先静态确认 `GsplatVfxBinder` 的 `OnEnable -> EnsureKernelsReady` 调用链
  - 再做最小修复: 对 `GraphicsDeviceType.Null` / batch 环境提前 no-op
  - 串行重跑:
    - `dotnet build Gsplat.Editor.csproj`
    - `dotnet build Gsplat.Tests.Editor.csproj`
    - `Unity -batchmode -nographics -executeMethod Gsplat.Editor.BatchVerifySplat4DImport.VerifyStaticSingleFrameFixture ...`
- 当前状态:
  - 正在定位 `GsplatVfxBinder` 的 headless kernel 探测路径

## 2026-03-12 00:05:00 +0800 收尾验证完成

- 已完成的动作:
  - 修改 `Runtime/VFX/GsplatVfxBinder.cs`
    - 对 `GraphicsDeviceType.Null` 提前 no-op
    - 普通图形环境下改为 `HasKernel(...)` 先探测,再 `FindKernel(...)`
  - 串行运行:
    - `python3 -m unittest Tools~/Splat4D/tests/test_single_frame_cli.py`
    - `dotnet build Gsplat.Editor.csproj`
    - `dotnet build Gsplat.Tests.Editor.csproj`
    - `Unity -batchmode -nographics -executeMethod Gsplat.Editor.BatchVerifySplat4DImport.VerifyStaticSingleFrameFixture -quit`
    - `Unity -batchmode -nographics -runTests ...`(先带 `-quit`,后去掉 `-quit` 重新验证)
- 关键动态证据:
  - Python 工具测试:
    - `Ran 4 tests`
    - `OK`
  - `dotnet build Gsplat.Editor.csproj`
    - `0 warnings`
    - `0 errors`
  - `dotnet build Gsplat.Tests.Editor.csproj`
    - `0 errors`
    - 保留 3 个既有无关 warning:
      - `Tests/Editor/GsplatLidarScanTests.cs` 的 2 个 obsolete warning
      - `Tests/Editor/GsplatSog4DImporterTests.cs` 的 1 个 CS0649
  - batch verifier:
    - 仍输出:
      - `[Gsplat][BatchVerify] Exporter ok.`
      - `[Gsplat][BatchVerify] Imported ... Runtime samples(0,0.35,1) all kept full sort range.`
    - 已确认:
      - 日志中不再出现 `Kernel 'BuildAxes' not found`
      - 日志中不再出现 `FindKernel` 相关 error
  - Unity EditMode CLI:
    - 带 `-quit` 时:
      - `exit code 0`
      - 但不产出 `testResults xml`
    - 去掉 `-quit` 后:
      - `/tmp/gsplat_single_frame_editmode_noquit.xml` 成功生成
      - `result="Passed" total="19" passed="19" failed="0"`
      - 包含:
        - `Gsplat.Tests.GsplatSplat4DImporterDeltaV1Tests` 3/3 passed
        - `Gsplat.Tests.GsplatVisibilityAnimationTests` 16/16 passed
- 对剩余日志的判断:
  - `Curl error 42: Callback aborted`
    - 出现在 batch verifier 即将退出阶段
    - 当前没有调用栈指向 `wu.yize.gsplat`
    - 本轮不把它判成这次改动引入的问题
  - `MCP-FOR-UNITY: No process found listening on port 8082`
    - 来自 `Library/PackageCache/com.coplaydev.unity-mcp`
    - 属于退出阶段的外部包日志
- 当前状态:
  - `tasks.md` 的 `3.4` 与 `4.2` 已满足验收条件
  - 本 change 的实现与验证均已闭环

## 2026-03-12 09:36:00 +0800 用户资产导入验证

- 当前行动目的:
  - 将 `/Users/cuiluming/Downloads/jimeng-sd2-point_cloud.ply` 已导出的单帧 `.splat4d` 放入当前 Unity 项目
  - 用最小 batch 验证确认 importer 确实生成了可用的 `GsplatRenderer/GsplatAsset`
- 已观察到的事实:
  - `.ply -> .splat4d` 已成功:
    - `/Users/cuiluming/Downloads/jimeng-sd2-point_cloud.splat4d`
    - `63,132 splats`
  - 项目内已有目标目录:
    - `Assets/Gsplat/splat`
- 当前主假设:
  - 直接复制到 `Assets/Gsplat/splat` 后,现有 `.splat4d` importer 会正常生成 prefab 主资源
- 最强备选解释:
  - 资产本身可以导入,但还需要额外 batch 读回确认 `GsplatRenderer` 与 `GsplatAsset` 是否都存在
- 下一步行动:
  - [x] 复制 `.splat4d` 到项目 `Assets/Gsplat/splat`
  - [x] 用临时 Editor batch 入口做一次导入与结构验证
  - [x] 清理临时入口,只保留用户资产与验证记录

## 2026-03-12 09:38:00 +0800 用户资产导入验证完成

- 已完成的动作:
  - 复制:
    - `/Users/cuiluming/Downloads/jimeng-sd2-point_cloud.splat4d`
    - `-> Assets/Gsplat/splat/jimeng-sd2-point_cloud.splat4d`
  - 用临时 batch 入口导入并读回 prefab / renderer / asset
  - 删除临时入口:
    - `Assets/Editor/__CodexImportJimengSplat4D.cs`
    - `Assets/Editor/__CodexImportJimengSplat4D.cs.meta`
- 关键动态证据:
  - Unity batch log:
    - `[Codex][UserImport] Imported Assets/Gsplat/splat/jimeng-sd2-point_cloud.splat4d: SplatCount=63132, SHBands=0, TimeModel=1, Velocities=63132, Times=63132, Durations=63132.`
- 当前结论:
  - 用户这份真实 `.ply` 已成功转成单帧 `.splat4d`
  - 并已成功导入当前 Unity 项目
  - importer 结果可被 `GsplatRenderer` 正常引用

## 2026-03-12 10:20:00 +0800 支线索引: 启用 `__splat4d_edge_opacity` 上下文集

- 启用原因:
  - 用户当前提出的是新的运行时显示调查:
    - `.splat4d -> GsplatRenderer` 的高斯边缘看起来有“边界感”
    - 怀疑是高斯核 / alpha / blend / temporal gaussian 呈现算法与成熟实现存在差异
  - 这和前一条“单帧 `.ply -> .splat4d` 支持”的 OpenSpec 立项不同,需要独立记录现象、假设、对照证据与可能修复
- 支线上下文后缀:
  - `__splat4d_edge_opacity`
- 支线主题:
  - 对比本仓库 `GsplatRenderer` 与 `playcanvas/supersplat` 的高斯 splat 呈现差异,判断当前边缘不透明感是否来自算法路径

## 2026-03-12 11:52:00 +0800 继续调查: 单帧 `.splat4d v2` 动态 SH 初始化与播放 no-op 不对称

- 当前行动目的:
  - 把 2026-03-11 23:18:00 那条 EPIPHANY 从“静态候选风险”推进到“现状确认”
  - 回答三个问题:
    - 当前代码里这条不对称是否仍然存在?
    - `.splat4d v2 frameCount=1 + delta-v1` 是否真的允许导入?
    - runtime 是否真的会对这类资产初始化动态 SH 资源?
- 当前已确认的静态证据:
  - `TryInitShDeltaRuntime()` 仍只在 `ShFrameCount <= 0` 时跳过
  - `TryApplyShDeltaForTime()` 仍在 `frameCount <= 1` 时直接 no-op
  - importer 对 `labelsEncoding=delta-v1` 只要求 `header.frameCount > 0`,未禁止 `frameCount=1`
- 下一步行动:
  - [ ] 做最小动态实验,构造 `frameCount=1` 的 `.splat4d v2 delta-v1` 资产
  - [ ] 在 Unity 中导入并观察 `GsplatRenderer` 的动态 SH runtime 状态
  - [ ] 根据动态证据判断这是“真实 bug”还是“可触发但目前仅是语义不对齐”

## 2026-03-12 12:16:35 +0800 调查推进: 进入最小动态实验设计

- 当前行动目的:
  - 不再停留在静态阅读
  - 先用最小可证伪实验确认 `frameCount=1` 的 `.splat4d v2 + delta-v1` 资产,在当前 runtime 里是否真的会初始化动态 SH 资源
- 当前已重新确认的事实:
  - `Runtime/GsplatRenderer.cs`
    - `TryInitShDeltaRuntime()` 仍在 `ShFrameCount <= 0` 才跳过
    - `TryApplyShDeltaForTime()` 仍在 `frameCount <= 1` 直接 no-op
  - `Editor/GsplatSplat4DImporter.cs`
    - importer 允许 `header.frameCount == 1` 的 delta-v1 v2 资产进入资产化路径
  - 现有测试里还没有直接覆盖这条组合路径
- 当前主假设:
  - 若导入一份 `frameCount=1` 的 `.splat4d v2 + delta-v1`, `GsplatRenderer` 会完成动态 SH runtime 初始化,但后续时间推进永远不会真正应用 delta
- 最强备选解释:
  - 即便 importer 允许该格式,运行时别的门禁也可能让这条路径根本不会初始化,此前的怀疑只是静态假象
- 下一步行动:
  - [ ] 先把实验设计写入 `notes__splat4d_single_frame_support.md`
  - [ ] 以最小测试或临时验证脚本复用现有二进制夹具套路,构造 `frameCount=1` 的 delta-v1 v2 资产
  - [ ] 在 Unity 中读取 `m_shDeltaInitialized` / `m_shDeltaDisabled` / `m_shDeltaFrameCount` / `m_sh1Segments` / `m_sh1CentroidsBuffer` 的实际状态

## 2026-03-12 12:24:25 +0800 最小动态实验结果

- 已完成的动作:
  - [x] 构造临时 `frameCount=1` 的 `.splat4d v2 + delta-v1` 资产
  - [x] 在 Unity 当前会话中导入并实例化该资产
  - [x] 通过反射读取 `GsplatRenderer` 动态 SH 私有状态
  - [x] 显式调用一次 `TryApplyShDeltaForTime(1.0f)` 观察 no-op 结果
- 关键动态证据:
  - `Asset.ShFrameCount=1`
  - `Asset.Sh1DeltaSegments=1`
  - `Runtime.Initialized=True`
  - `Runtime.Disabled=False`
  - `Runtime.FrameCount=1`
  - `Runtime.CurrentFrameBefore=0`
  - `Runtime.CurrentFrameAfter=0`
  - `Runtime.Segments=1`
  - `Runtime.CentroidsBufferNull=False`
  - `Runtime.CentroidsBufferValid=True`
- 结论更新:
  - 这条路径不是纯静态猜测
  - 它在当前 Unity runtime 中真实可达
  - 当前更准确的定性是:
    - 单帧 delta-v1 资产会初始化动态 SH runtime
    - 但播放逻辑又因 `frameCount <= 1` 永远不推进
- 下一步行动:
  - [ ] 结合当前 exporter / 真实用户资产路径,判断这是不是“当前用户可见 bug”还是“未来契约风险 + 资源冗余”
  - [ ] 输出正式调查结论与建议修复点

## 2026-03-12 16:24:00 +0800 新行动: 将 `Assets/Gsplat/ply/s1-point_cloud.ply` 转成 v2 + SH3 产物

- 当前行动目的:
  - 响应用户“先做 v2 的 SH3 转换,不要现在的 v1”
  - 不再停留在显示观感猜测,先把输入资产的 SH 信息量提升到更高版本
- 为什么现在做这个:
  - 当前 `.splat4d` 单帧工具链默认仍是 `SHBands=0`
  - 用户明确要先看更高信息量格式对显示质量的影响
- 下一步行动:
  - [ ] 核对仓库里 `v1 / v2 / SH3` 分别对应哪条格式链路
  - [ ] 确认 `s1-point_cloud.ply` 应该走 `Splat4D` 还是 `Sog4D`
  - [ ] 对目标资产执行正式转换,并把产物路径与验证结果记录下来

## 2026-03-12 16:32:00 +0800 阶段进展

- [x] 核对仓库里 `v1 / v2 / SH3` 分别对应哪条格式链路
- [x] 确认 `s1-point_cloud.ply` 应该走 `Sog4D` 而不是 `Splat4D`
- [x] 对目标资产执行正式转换,并记录产物路径与验证结果

**目前这一步已完成**
- 新产物:
  - `Assets/Gsplat/sog4d/s1_point_cloud_v2_sh3_20260312.sog4d`
- 关键结果:
  - `validate ok (v2)`
  - Unity importer 产物类型为 `UnityEngine.GameObject`
- 下一步建议:
  - [ ] 以这份 v2 + SH3 产物为基线,再和旧 v1 / ply / supersplat 做显示质量对照

## 2026-03-12 16:40:00 +0800 用户纠正: 目标格式是 `.splat4d`,不是 `.sog4d`

- 当前行动目的:
  - 撤回上一轮把目标格式误判成 `.sog4d` 的执行口径
  - 回到用户明确要求的 `.splat4d v2 + SH3` 转换链路
- 为什么要先停下来重查:
  - 仓库里 `.splat4d` 与 `.sog4d` 是两条不同格式线
  - 用户现在明确要的是 `.splat4d`
  - 不能把已经完成的 `.sog4d v2` 误当成这次需求的完成结果
- 下一步行动:
  - [ ] 核对 `.splat4d v2` 在当前仓库里的真实支持范围
  - [ ] 查清 `.splat4d` exporter 是否已经支持 SH3 输出
  - [ ] 若已支持,直接对 `s1-point_cloud.ply` 执行正式转换
  - [ ] 若未支持,评估并落地所需实现
我正在先回读这条支线的上下文文件, 因为这次需求已经从渲染排查切到 .ply -> .splat4d v2 + SH3 导出实现, 我需要先把已有结论和待办重新装回工作记忆里, 避免重复走错格式。
现在我先读取 task_plan、notes、WORKLOG、LATER_PLANS、ERRORFIX、EPIPHANY 这套支线上下文文件。

## 2026-03-12 16:47:00 +0800 新一轮目标切换: `.ply -> .splat4d v2 + SH3`

- 当前行动目的:
  - 用户已经把目标明确成: 把 `Assets/Gsplat/ply/s1-point_cloud.ply` 转成 `.splat4d` 格式的 `v2 + SH3`
  - 这不再只是“单帧 v1 exporter”问题,而是要把 `.splat4d v2` 的 SH 导出能力真正补到工具链里
- 当前已知事实:
  - importer/runtime 已支持 `.splat4d v2 + SH(delta-v1)`
  - 现有 `Tools~/Splat4D/ply_sequence_to_splat4d.py` 还停留在 raw v1 + SH0-only
  - 我上一轮误导出了 `.sog4d`,这次必须先把格式边界钉死再实现
- 下一步行动:
  - [ ] 补读 `openspec/specs/4dgs-core/spec.md`、`Editor/GsplatSplat4DImporter.cs`、`Tools~/Splat4D/ply_sequence_to_splat4d.py`
  - [ ] 设计 `.splat4d v2` 的 header / section writer 与 SH3 导出参数
  - [ ] 用真实 `s1-point_cloud.ply` 产出 `.splat4d v2 + SH3`
  - [ ] 做离线解析与 Unity MCP 导入验证
- 当前状态:
  - 本轮进入 exporter 升级阶段
  - 目标文件格式锁定为 `.splat4d`,不是 `.sog4d`

## 2026-03-12 17:32:00 +0800 exporter 升级进展: v2 + SH3 路径已落地

- 已完成:
  - [x] 补读 `.splat4d v2` 规格与 importer 代码
  - [x] 为 `Tools~/Splat4D/ply_sequence_to_splat4d.py` 增加 `--splat4d-version 2`
  - [x] 增加单帧 `.splat4d v2 + SH(full)` 导出路径
  - [x] 增加 `--sh-bands` / `--sh-codebook-count` / `--sh-centroids-type` / `--self-check`
  - [x] 增加 Python 回归测试,覆盖:
    - 单帧 `v2 + SH3` section 结构
    - 多帧输入拒绝
- 当前验证证据:
  - `python3 -m py_compile Tools~/Splat4D/ply_sequence_to_splat4d.py`
    - 通过
  - `python3 -m unittest Tools~/Splat4D/tests/test_single_frame_cli.py`
    - `Ran 6 tests`
    - `OK`
- 当前剩余待办:
  - [ ] 用真实 `Assets/Gsplat/ply/s1-point_cloud.ply` 导出 `.splat4d v2 + SH3`
  - [ ] 解析输出 header / META / section table
  - [ ] 用 Unity MCP 刷新导入并检查 Console / 资源状态

## 2026-03-12 17:58:00 +0800 真实导出已完成, Unity 在线闭环受会话状态阻塞

- 已完成:
  - [x] 用真实 `Assets/Gsplat/ply/s1-point_cloud.ply` 导出 `.splat4d v2 + SH3`
  - [x] 解析输出 header / META / section table
- 当前未闭环项:
  - [ ] 用 Unity MCP 直接读取 imported asset / console
- 阻塞现象:
  - 当前项目已有 Unity Editor 进程持锁
  - Unity MCP 返回 `no_unity_session`
  - `refresh_unity(...wait_for_ready=true)` 60s 超时
  - 新资产目前还没有 `.meta`, `Editor.log` 里也还没看到这份文件的导入记录
- 当前状态:
  - exporter 和离线结构验证已完成
  - Unity 在线导入验证需要等当前 Editor 会话恢复可交互状态

## 2026-03-12 18:05:00 +0800 新问题: `v2 + SH3` 导入后颜色异常偏彩

- 当前行动目的:
  - 用户反馈新导出的 `s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d` 颜色不正常
  - 现象是原本灰色金属面变成明显彩色
- 为什么先做这一步:
  - 这说明“高阶 SH 已经进来了”,但颜色语义可能不对
  - 更像是 SH basis / 系数排列 / importer 写入布局 / runtime 解释方式不一致,而不是 alpha 边界问题
- 当前主假设:
  - `.ply` 的 `f_rest_*` 到 `GsplatAsset.SHs` 的排列或语义和当前 runtime 预期不一致
- 当前最强备选解释:
  - 也可能是 codebook 聚类量化失真太大,把本来接近中性的 SH 染偏
- 下一步行动:
  - [ ] 对比 PLY 原始 SH 分布 与 导出后 SHCT/SHLB 还原值的误差
  - [ ] 审计 importer / runtime 对 SH 数组的 layout 假设
  - [ ] 判断是“布局错误”还是“量化过强”

## 2026-03-12 18:18:00 +0800 新行动: 锁定 `s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d` 的颜色异常

- 当前行动目的:
  - 用户已经把异常对象指向 `Assets/Gsplat/splat/v2/s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d`
  - 需要把“颜色偏彩”从泛化怀疑收敛到这一个具体资产与它的 SH 数据布局上
- 为什么现在先做这个:
  - 现象已经不是 alpha 边界或 tonemapping 讨论,而是具体的颜色语义异常
  - 最像的候选方向是 `PLY f_rest_*` 的通道排列解释,这可以先做最小可证伪实验
- 下一步行动:
  - [ ] 读取 `Editor/GsplatImporter.cs` 与当前 exporter 的 `_read_ply_frame()`
  - [ ] 用脚本同时按两种 SH 排列解释 `s1-point_cloud.ply`
  - [ ] 对比统计量,判断哪种解释更接近中性灰材质的预期
  - [ ] 若确认 exporter 排列错位,直接修复并重导出目标 `.splat4d`
- 当前状态:
  - 进入“颜色异常 -> SH 布局/量化”定位阶段

## 2026-03-12 18:26:00 +0800 阶段进展: `s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d` 颜色异常已定位到 SH 排列错位

- [x] 读取 `Editor/GsplatImporter.cs` 与当前 exporter 的 `_read_ply_frame()`
- [x] 用脚本同时按两种 SH 排列解释 `s1-point_cloud.ply`
- [x] 直接反解当前 `.splat4d` 并比较它更贴近哪种源解释
- [x] 判断主矛盾是“布局错误”,不是“量化过强”
- 当前结论:
  - 当前 exporter 把 `f_rest_*` 错按 `RGBRGB...` 交织读入
  - 而仓库现有 PLY importer / runtime 契约是 `RRR... GGG... BBB...`
  - 因此当前 v2 资产把错位后的 SH 压缩进了 `SHCT/SHLB`,导致金属灰面偏彩
- 下一步行动:
  - [ ] 修复 `_read_ply_frame()` 的 SH 重排逻辑
  - [ ] 增加最小回归测试锁定 `channel-major` 契约
  - [ ] 重导出目标 `.splat4d` 并做误差/测试验证

## 2026-03-12 18:35:00 +0800 新现象: Unity batch verify 报 full-label 资产残留 delta segments

- 已观察到的事实:
  - `Tools/Gsplat/Batch Verify/Verify s1_point_cloud_v2_sh3` 已执行
  - Console 输出:
    - `[Gsplat][BatchVerify] Reimporting: Assets/Gsplat/splat/v2/s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d`
    - `Exception: Single-frame full-label asset should not carry delta-v1 segments.`
- 这说明:
  - 新的 SH 排列修复已经成功走到 Unity importer
  - 但 importer/runtime 侧还存在一条新的待定位路径
- 下一步行动:
  - [ ] 审计 `Editor/GsplatSplat4DImporter.cs` 对 `Sh*DeltaSegments` 的写入/清理逻辑
  - [ ] 判断是“full labels 未清空旧 delta 状态”还是“文件被误判成 delta-v1”
  - [ ] 若确认是 importer 状态残留,补清理并重跑 Unity batch verify

## 2026-03-12 18:52:00 +0800 阶段收尾: 目标资产颜色异常已闭环修复

- [x] 修复 `_read_ply_frame()` 的 SH 重排逻辑
- [x] 增加最小回归测试锁定 `channel-major` 契约
- [x] 重导出目标 `.splat4d` 并做离线误差验证
- [x] 定位 Unity batch verify 的 delta 判定误报
- [x] 调整 batch verify 到与 runtime 一致的语义
- [x] 新增 Editor 回归测试并在 Unity 中跑通
- 当前状态:
  - `Assets/Gsplat/splat/v2/s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d` 已按正确 SH 排列重导出
  - Unity batch verify 已通过
  - 支线本轮目标已完成

## 2026-03-12 19:02:00 +0800 新行动: 记录 Sog4D 后续项 + 对比 SuperSplat 的高斯呈现差异

- 当前行动目的:
  - 把“审计 `Tools~/Sog4D/ply_sequence_to_sog4d.py` 的 SH 排列风险”正式记成 OpenSpec change, 但本轮不实现
  - 同时排查 Unity 当前高斯呈现为什么相比 SuperSplat 在线版更“脏”、局部颜色更明显
- 为什么现在这样做:
  - 第一条属于后续治理, 适合结构化记录,避免遗忘
  - 第二条属于新的视觉质量问题,需要重新按“现象 -> 假设 -> 验证”收证据
- 下一步行动:
  - [ ] 用 OpenSpec 创建 `Sog4D SH 布局审计` 变更记录
  - [ ] 对照当前 Unity shader / runtime 和 SuperSplat 的 shader / 片元公式
  - [ ] 判断“脏感/局部颜色明显”更像是核函数、alpha/coverage、还是颜色/SH 解码差异
- 当前状态:
  - 进入“记录一个后续变更 + 启动新一轮高斯呈现对比分析”阶段

## 2026-03-12 19:20:00 +0800 继续步骤: SuperSplat 脏感差异分析

- 当前行动目的:
  - 按用户最新要求,把"为什么 Unity 里看起来比 SuperSplat 更脏"单独收敛成一次正式分析
  - 这次先做证据收集与结论分级,不直接改 shader
- 当前已知前提:
  - `s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d` 的 PLY SH 排列错误已经修复
  - `sog4d-ply-sh-layout-audit` 已经建成 OpenSpec change,当前不落代码
- 当前主假设:
  - Unity 与 PlayCanvas/SuperSplat 在 splat footprint 抗锯齿 / alpha energy compensation 上仍有差异
- 当前最强备选解释:
  - 颜色输出路径或 SH 到片元的传递方式,使局部高频颜色更容易显脏
- 下一步行动:
  - [ ] 对照 `Runtime/Shaders/Gsplat.shader` 与 SuperSplat / PlayCanvas 的 corner, AA, output 路径
  - [ ] 形成"现象 -> 假设 -> 验证计划 -> 结论"记录到 `notes__splat4d_single_frame_support.md`
  - [ ] 回写 `WORKLOG__splat4d_single_frame_support.md` 与 `EPIPHANY_LOG__splat4d_single_frame_support.md` 视情况补充

## 2026-03-12 19:55:00 +0800 阶段进展: SuperSplat 脏感差异分析完成首轮证据收敛

- [x] 对照 `Runtime/Shaders/Gsplat.shader` 与 SuperSplat / PlayCanvas 的 corner, AA, output 路径
- [x] 形成"现象 -> 假设 -> 验证计划 -> 结论"并记录到 `notes__splat4d_single_frame_support.md`
- [x] 回写 `WORKLOG__splat4d_single_frame_support.md`
- [x] 评估是否需要 `EPIPHANY_LOG__splat4d_single_frame_support.md`

## 2026-03-12 19:55:00 +0800 当前状态

- 已完成本轮用户要求的分析任务.
- 当前已确认两类差异同时存在:
  - 算法差异: Unity 当前没有用上 PlayCanvas 的 `aaFactor` AA compensation
  - 观察环境差异: 当前 Unity HDRP scene post FX 明显重于 SuperSplat 默认配置
- 本轮不改代码.
- 如果后续继续,优先做:
  - 纯观察模式对照
  - 然后再做 `aaFactor` A/B

## 2026-03-12 20:10:00 +0800 新行动: 只补 `aaFactor` compensation, 不动 `normExp`

- 当前行动目的:
  - 响应用户"直接做 2"的要求
  - 只修改 `aaFactor` 抗锯齿补偿链路,保持高斯主核不变
- 这次的最小实现目标:
  - shader 真正编译出 `GSPLAT_AA` 路径
  - 材质实例默认启用该 keyword
  - `ClipCorner` 与 `alphaGauss` 都使用 `aaFactor`,但 `ParticleDots` 不受影响
- 下一步行动:
  - [ ] 审计 `v2f` 与材质 keyword 路径,确定最小改动点
  - [ ] 修改 shader / settings
  - [ ] 编译验证并做一轮 Unity 截图/读取确认

## 2026-03-12 20:18:00 +0800 验证修正

- 已观察到的事实:
  - `Gsplat.csproj` 构建成功
  - 并行触发 `Gsplat.Editor.csproj` 时命中了 `Gsplat.deps.json` 文件锁竞争
- 当前判断:
  - 这是验证过程的并发问题,不是本轮代码改动引入的编译错误
- 下一步行动:
  - [ ] 串行重跑 `Gsplat.Editor.csproj`
  - [ ] 串行重跑 `Gsplat.Tests.Editor.csproj`
  - [ ] 再做一轮 Unity screenshot / console 确认

## 2026-03-12 20:26:00 +0800 收尾验证: renderer 级 `aaFactor` 开关

- 当前行动目的:
  - 把 `GSPLAT_AA` 从全局默认开启收敛为 renderer 级对照开关
  - 在当前激活对象上直接打开,形成用户可观察的 A/B 现场
- 下一步行动:
  - [ ] 串行构建 `Gsplat.Editor.csproj` 与 `Gsplat.Tests.Editor.csproj`
  - [ ] 刷新 Unity 编译并确认 console 无 error
  - [ ] 将 `s1_point_cloud_v2_sh3_full_k8192_f32_20260312.EnableFootprintAACompensation` 设为 true

## 2026-03-12 20:34:00 +0800 阶段收尾: `aaFactor` 对照开关已落地到当前对象

- [x] 串行构建 `Gsplat.Editor.csproj` 与 `Gsplat.Tests.Editor.csproj`
- [x] 刷新 Unity 编译并确认 console 无新 error
- [x] 将 `s1_point_cloud_v2_sh3_full_k8192_f32_20260312.EnableFootprintAACompensation` 设为 true
- [x] 回读当前对象属性确认开关已生效

## 2026-03-12 20:34:00 +0800 当前状态

- 本轮代码改动已完成.
- `GSPLAT_AA` 已经成为 renderer 级 A/B 开关,默认语义与 PlayCanvas 对齐为关闭.
- 当前激活对象已被设置为开启,可供用户直接观察.
- 若后续继续,下一步应是:
  - 用户在 Unity 里肉眼对比当前对象观感
  - 再决定是否要把这条开关继续细化成更可控的参数(例如只影响 clip 或只影响 alpha)

## 2026-03-12 20:42:00 +0800 新行动: 回滚 `EnableFootprintAACompensation` 现场开关, 转向 `PLY` 导入契约对照

- 已观察到的事实:
  - 用户明确反馈: 不启用 `EnableFootprintAACompensation` 还好,启用后少了很多细节,观感不对
- 这意味着:
  - 上一轮把 `aaFactor` 当成主要修复方向的假设不成立
  - 当前更像是 `PLY` 导入/解码契约与 SuperSplat 不一致,而不是 footprint AA 补偿缺失
- 下一步行动:
  - [ ] 把当前对象 `EnableFootprintAACompensation` 恢复为 false
  - [ ] 对照 Unity `Editor/GsplatImporter.cs` 与 PlayCanvas / SuperSplat 的 PLY 读取与解码逻辑
  - [ ] 重点排查 `f_dc / opacity / scale / rotation / SH scale` 的解释差异

## 2026-03-12 19:41:30 +0800 新行动: 回到 AA 细节丢失现象,先隔离观察环境再查细节保留参数

- 已观察到的现象:
  - 用户再次确认: `EnableFootprintAACompensation=false` 观感还好
  - 一旦开启, 细节明显变少, 这与"默认补 AA 会更接近 SuperSplat"的旧判断冲突
- 当前主假设:
  - 当前最值得怀疑的不是 `aaFactor` 本身, 而是观察环境仍混入 HDRP post FX, 或 Unity / PlayCanvas 对低 alpha 小 footprint splat 的保留策略不同
- 当前最强备选解释:
  - 颜色输出路径, 例如 gamma/linear 处理, 仍可能让局部颜色显得更脏
- 下一步行动:
  - [ ] 用 Unity MCP 回读当前 scene volume / target renderer 状态
  - [ ] 对照 PlayCanvas / SuperSplat 的 `alphaClip` / `minPixelSize` / dither / output 路径
  - [ ] 形成新的"现象 -> 假设 -> 验证计划 -> 结论"记录
- 当前状态:
  - 正在重新收敛变量, 避免再次把错误假设当根因

## 2026-03-12 19:55:00 +0800 阶段进展: `EnableFootprintAACompensation` 细节丢失原因完成第二轮收敛

- [x] 用 Unity MCP 回读当前 scene volume / target renderer 状态
- [x] 对照 PlayCanvas / SuperSplat 的 `alphaClip` / `minPixelSize` / dither / output 路径
- [x] 形成新的"现象 -> 假设 -> 验证计划 -> 结论"记录

## 2026-03-12 19:55:00 +0800 当前状态

- 当前 scene 已确认 `Volume=0`, 旧的"post FX 仍在干扰本轮观察"前提已失效
- 当前最可信结论是:
  - `EnableFootprintAACompensation` 对这份资产不应默认开启
  - 原因更像是数据契约不匹配,不是 Unity 当前 `aaFactor` 数学链路写错
- 若继续排查与 SuperSplat 的剩余质量差异,下一步应转向颜色输出链而不是继续纠缠 `aaFactor`
