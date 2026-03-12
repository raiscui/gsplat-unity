# WORKLOG: 单帧普通 3DGS `.ply -> .splat4d` OpenSpec 立项

## 2026-03-11 22:22:27 +0800 任务名称: 新建 `splat4d-single-frame-ply-support` change

### 任务内容
- 为“单个普通 3DGS `.ply` 转单帧 `.splat4d`,并由 Unity `GsplatRenderer` 正常显示”创建新的 OpenSpec change。
- 只创建 change 脚手架并读取首个 artifact 模板,不继续生成 artifact 内容。

### 完成过程
- 读取了现有主线与活跃支线上下文,确认这次任务应单开 `__splat4d_single_frame_support` 上下文集。
- 检查 `openspec/changes/` 后确认当前不存在同名 change。
- 创建:
  - `openspec/changes/splat4d-single-frame-ply-support/`
- 运行状态检查:
  - `openspec status --change "splat4d-single-frame-ply-support"`
- 读取首个 artifact 说明:
  - `openspec instructions proposal --change "splat4d-single-frame-ply-support"`

### 总结感悟
- 这次立项和之前的 `sog4d-single-frame-ply-support` 结构相似,但目标格式和运行时路径不同,单独开支线上下文是必要的。
- 当前最合适的下一步不是直接写实现,而是先把 `proposal` 的 capability 边界写清楚:
  - 单帧普通 3DGS `.ply`
  - `.splat4d` 输出
  - `GsplatRenderer` 显示契约

## 2026-03-11 22:22:27 +0800 任务名称: 创建 `splat4d-single-frame-ply-support` 的 `proposal`

### 任务内容
- 根据用户澄清,为 `splat4d-single-frame-ply-support` 创建第一个 artifact:
  - `proposal.md`

### 完成过程
- 读取了:
  - `openspec status --change "splat4d-single-frame-ply-support" --json`
  - `openspec instructions proposal --change "splat4d-single-frame-ply-support" --json`
  - `openspec/specs/4dgs-core/spec.md`
  - `openspec/specs/4dgs-playback-api/spec.md`
  - `Tools~/Splat4D/ply_sequence_to_splat4d.py`
  - `README.md`
- 结合用户边界澄清,把 proposal 的职责拆成:
  - 工具侧新增单帧普通 `.ply -> .splat4d`
  - Unity 侧修改现有 `.splat4d -> GsplatRenderer` 的单帧保证
- 创建:
  - `openspec/changes/splat4d-single-frame-ply-support/proposal.md`
- 刷新状态:
  - `Progress: 1/4 artifacts complete`

### 总结感悟
- 这次最关键的不是“有没有 `.splat4d` 运行时”,而是把“已有链路”和“新增保证”拆干净。
- `README` 已经写了 `--input-ply`,但 CLI 还没跟上,这是 proposal 里很有价值的动机证据。

## 2026-03-11 22:22:27 +0800 任务名称: 创建 `splat4d-single-frame-ply-support` 的 `design`

### 任务内容
- 为 `splat4d-single-frame-ply-support` 创建第二个 artifact:
  - `design.md`

### 完成过程
- 读取了:
  - `openspec status --change "splat4d-single-frame-ply-support" --json`
  - `openspec instructions design --change "splat4d-single-frame-ply-support" --json`
  - `openspec/changes/splat4d-single-frame-ply-support/proposal.md`
  - `Editor/GsplatSplat4DImporter.cs`
  - `Runtime/GsplatRenderer.cs`
  - `Tools~/Splat4D/ply_sequence_to_splat4d.py`
  - 历史参考:
    - `openspec/changes/archive/2026-02-17-add-4dgs-support/design.md`
- 关键设计结论:
  - 单帧 `.splat4d` 继续复用现有 `GsplatAsset -> GsplatRenderer` 主链路
  - 工具侧通过 `--input-ply` 正式暴露单文件入口
  - 单帧导出语义不新造 `static` 模式,直接复用现有 `average + 1 frame`
  - Unity 侧优先靠 contract / tests / 最小 guard 硬化,不新增单帧专用 renderer
- 创建:
  - `openspec/changes/splat4d-single-frame-ply-support/design.md`
- 刷新状态:
  - `Progress: 2/4 artifacts complete`

### 总结感悟
- 现有实现里最有价值的事实,不是“还能不能做”,而是“单帧静态语义其实已经在 average 路径里自然存在”。
- 这让本次 change 可以把力气集中在:
  - 正式入口
  - 正式契约
  - 正式验证

## 2026-03-11 22:22:27 +0800 任务名称: 创建 `splat4d-single-frame-ply-support` 的 `specs`

### 任务内容
- 为 `splat4d-single-frame-ply-support` 创建第三个 artifact:
  - `specs`

### 完成过程
- 读取了:
  - `openspec status --change "splat4d-single-frame-ply-support" --json`
  - `openspec instructions specs --change "splat4d-single-frame-ply-support" --json`
  - `openspec/specs/4dgs-core/spec.md`
  - `openspec/specs/4dgs-playback-api/spec.md`
  - `openspec/changes/sog4d-single-frame-ply-support/specs/...`
- 创建了 3 份 spec:
  - `specs/splat4d-ply-conversion/spec.md`
  - `specs/4dgs-core/spec.md`
  - `specs/4dgs-playback-api/spec.md`
- 本轮的核心 requirement 落点:
  - 工具侧正式支持单文件普通 3DGS `.ply`
  - 单帧导出的 canonical 静态默认值:
    - `vx/vy/vz = 0`
    - `time = 0`
    - `duration = 1`
  - 单帧静态 `.splat4d` 继续作为合法 4D 资产导入
  - `TimeNormalized / AutoPlay / Loop` 下画面仍必须稳定为固定帧
- 刷新状态:
  - `Progress: 3/4 artifacts complete`

### 总结感悟
- 这轮最重要的不是“写了几份 spec”,而是把“工具入口新增”和“Unity 侧既有链路的正式保证”拆成了不同 capability 里的 requirement。
- 这样后面的 `tasks` 更容易落到代码与测试,不会把所有事情揉成一句“支持单帧”。

## 2026-03-11 22:22:27 +0800 任务名称: 创建 `splat4d-single-frame-ply-support` 的 `tasks`

### 任务内容
- 为 `splat4d-single-frame-ply-support` 创建最后一个 artifact:
  - `tasks.md`

### 完成过程
- 读取了:
  - `openspec status --change "splat4d-single-frame-ply-support" --json`
  - `openspec instructions tasks --change "splat4d-single-frame-ply-support" --json`
  - 已完成的 `proposal.md`
  - 已完成的 `design.md`
  - 已完成的 3 份 spec 文件
- 创建:
  - `openspec/changes/splat4d-single-frame-ply-support/tasks.md`
- 将任务拆为 4 组:
  - `Exporter And CLI`
  - `Unity Importer And Runtime Semantics`
  - `Tests And Fixtures`
  - `Docs And Final Verification`
- 刷新状态:
  - `Progress: 4/4 artifacts complete`
  - `All artifacts complete!`

### 总结感悟
- 到这一步,这条 change 的边界已经完整闭环:
  - proposal 说明为什么做
  - design 说明怎么做
  - specs 说明系统必须做到什么
  - tasks 把它拆成可执行清单
- 下一步最自然的动作已经不是继续写 artifact,而是进入实现阶段

## 2026-03-11 23:46:18 +0800 任务名称: apply `splat4d-single-frame-ply-support`(第一轮实现)

### 任务内容
- 正式实现普通单帧 3DGS `.ply -> .splat4d` 的工具入口。
- 为 Unity 侧补齐单帧 `.splat4d` 的 importer / runtime 契约测试与最小 guard。
- 同步 README 与工具文档,并补仓库内正式 `.ply` 验收夹具。

### 完成过程
- 修改 `Tools~/Splat4D/ply_sequence_to_splat4d.py`:
  - 新增 `--input-ply`
  - 将 `--input-ply` / `--input-dir` 改为互斥必选
  - 统一归一到同一份 `ply_files` 主流程
  - 把工具错误收敛成稳定的 CLI 报错文本
- 新增工具测试:
  - `Tools~/Splat4D/tests/test_single_frame_cli.py`
  - 覆盖:
    - 互斥参数失败
    - 单帧成功导出
    - 默认静态 4D 字段
    - 缺字段失败
    - 单帧误用 `keyframe` 失败
- 新增仓库内正式夹具:
  - `Tools~/Splat4D/tests/data/single_frame_valid_3dgs.ply`
- Unity 侧:
  - `Runtime/GsplatRenderer.cs`
    - `Has4DFields(...)` 增加长度 guard
  - `Tests/Editor/GsplatSplat4DImporterDeltaV1Tests.cs`
    - 增加单帧 raw v1 `.splat4d` 导入测试
  - `Tests/Editor/GsplatVisibilityAnimationTests.cs`
    - 增加静态单帧 4D runtime 逻辑测试
- 文档:
  - `README.md`
  - `Tools~/Splat4D/README.md`
  - 已补 `--input-ply`、单帧默认值和固定帧语义

### 验证结果
- Python:
  - `python3 -m unittest Tools~/Splat4D/tests/test_single_frame_cli.py`
  - 结果:
    - `Ran 4 tests`
    - `OK`
- 真实夹具导出:
  - `python3 Tools~/Splat4D/ply_sequence_to_splat4d.py --input-ply Tools~/Splat4D/tests/data/single_frame_valid_3dgs.ply --output /tmp/single_frame_valid_3dgs.splat4d --mode average --opacity-mode linear --scale-mode linear`
  - 解包检查:
    - `records = 1`
    - `vx/vy/vz = 0/0/0`
    - `time = 0`
    - `duration = 1`
- C# 编译:
  - `dotnet build Gsplat.Tests.Editor.csproj`
    - `0 errors`
    - 有 4 个既有无关 warning
  - `dotnet build Gsplat.Editor.csproj`
    - `0 warnings`
    - `0 errors`
- Unity EditMode 实测:
  - 当前未完成
  - 原因不是测试失败,而是同项目已有 Unity Editor 进程占用 `Temp/UnityLockfile`

### 总结感悟
- 这次最正确的做法确实不是新造单帧专用路线,而是把现有 `.splat4d` 主链路正式暴露出来并用测试锁住。
- 目前剩余工作已经收敛成“释放 Unity 项目锁后做最终闭环验证”,实现面本身已经基本完成。

## 2026-03-11 23:18:00 +0800 任务名称: 只读探索单帧 `.splat4d` EditMode 测试落点

### 任务内容
- 在不改代码的前提下,定位“静态单帧 `.splat4d` 仍是合法 4D 资产,且在 `TimeNormalized / AutoPlay / Loop` 下不会退化成伪动态”的最小、最稳 Unity EditMode 测试方案。
- 输出最适合补测试的现有文件、可复用 helper / 反射入口、以及最值当的最小 runtime guard 候选点。

### 完成过程
- 读取了:
  - `Tests/Editor/GsplatSplat4DImporterDeltaV1Tests.cs`
  - `Tests/Editor/GsplatVisibilityAnimationTests.cs`
  - `Tests/Editor/GsplatSequenceAssetTests.cs`
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatAsset.cs`
  - `Editor/GsplatSplat4DImporter.cs`
  - `openspec/changes/splat4d-single-frame-ply-support/specs/4dgs-core/spec.md`
  - `openspec/changes/splat4d-single-frame-ply-support/specs/4dgs-playback-api/spec.md`
- 结构化搜索确认了:
  - 仓库里已经有单帧 static `.splat4d` importer 测试和最小 raw v1 构造器
  - `GsplatVisibilityAnimationTests` 已经是本仓库成熟的 `GsplatRenderer` EditMode 反射测试模板
  - `GsplatSequenceAssetTests` 已经有“单帧 + 多个 TimeNormalized 样本 -> 固定结果”的断言模式
- 同时识别出一个候选风险:
  - `TryInitShDeltaRuntime()` 对 `ShFrameCount == 1` 仍会初始化
  - 但 `TryApplyShDeltaForTime()` 又会在 `frameCount <= 1` 时直接 no-op

### 总结感悟
- 这条问题最稳的做法不是新建大而全的视觉回归,而是沿用现有 importer 夹具 + renderer 反射测试套路,用最少的断言把契约钉住。
- 当前最有价值的 guard 候选,不是去改 `AutoPlay` 或 `Loop`,而是先让“单帧不进入动态 SH runtime”这件事在语义上更对齐。

## 2026-03-12 00:05:00 +0800 任务名称: apply `splat4d-single-frame-ply-support`(收尾验证与日志清理)

### 任务内容
- 清理 batchmode / `-nographics` 下新增的 VFX kernel error。
- 跑完整的工具测试、Unity 编译、真实闭环验证与 EditMode XML 回归。

### 完成过程
- 修改 `Runtime/VFX/GsplatVfxBinder.cs`:
  - 新增 `GraphicsDeviceType.Null` 提前短路
  - 使用 `HasKernel(...)` 先做存在性探测
- 验证:
  - `python3 -m unittest Tools~/Splat4D/tests/test_single_frame_cli.py`
  - `dotnet build Gsplat.Editor.csproj`
  - `dotnet build Gsplat.Tests.Editor.csproj`
  - `Unity -batchmode -nographics -executeMethod Gsplat.Editor.BatchVerifySplat4DImport.VerifyStaticSingleFrameFixture`
  - `Unity -batchmode -nographics -runTests ...`
- 额外确认:
  - `-runTests` 在当前 Unity 版本下,带 `-quit` 仍可能不产出 XML
  - 去掉 `-quit` 后,目标 19 个 EditMode tests 全部通过

### 总结感悟
- 真正需要修掉的不是 importer 主链路,而是 VFX 预览链路在 headless 环境里越界参与了验证流程。
- 对 Unity CLI 测试来说,这次再次验证了一个仓库级经验:
  - 当 `-runTests` 没有产出 XML 时,优先去掉 `-quit`,而不是先怀疑测试本身。

## 2026-03-12 09:38:00 +0800 任务名称: 用户资产 `jimeng-sd2-point_cloud.ply` 转换并导入 Unity

### 任务内容
- 将下载目录里的普通 3DGS `.ply` 转为单帧 `.splat4d`
- 把结果导入当前 Unity 项目并确认 importer 正常读回

### 完成过程
- 运行:
  - `python3 Tools~/Splat4D/ply_sequence_to_splat4d.py --input-ply /Users/cuiluming/Downloads/jimeng-sd2-point_cloud.ply --output /Users/cuiluming/Downloads/jimeng-sd2-point_cloud.splat4d --mode average --opacity-mode linear --scale-mode linear`
- 复制到项目:
  - `Assets/Gsplat/splat/jimeng-sd2-point_cloud.splat4d`
- 用临时 Editor batch 入口验证:
  - importer 已生成可用 prefab
  - `GsplatRenderer` 与 `GsplatAsset` 均存在
  - `SplatCount=63132`
- 清理:
  - 删除临时入口脚本,不把验证脚手架留在项目里

### 总结感悟
- 这次用户真实资产验证进一步说明,新增的单帧 `.ply -> .splat4d` 工具入口已经能直接服务真实工作流,不只是测试夹具能通过。

## 2026-03-12 12:24:25 +0800 任务名称: 验证单帧 `.splat4d v2 + delta-v1` 的动态 SH 初始化路径是否真实可达

### 任务内容
- 对 2026-03-11 23:18:00 记录的“不对称路径”做最小动态验证。
- 目标不是修复,而是确认这条路径在当前 Unity runtime 里到底会不会真的发生。

### 完成过程
- 先复用既有 `.splat4d v2 + delta-v1` 格式知识,生成了一份临时单帧资产:
  - `frameCount=1`
  - `shBands=1`
  - `labelsEncoding=delta-v1`
- 再通过 Unity 当前会话创建一次性 Editor probe:
  - 导入该资产
  - 实例化 prefab
  - 反射读取 `GsplatRenderer` 的动态 SH 私有字段
  - 再手动调用一次 `TryApplyShDeltaForTime(1.0f)` 验证 no-op
- 动态结果确认:
  - runtime 确实完成了动态 SH 初始化
  - 但当前帧仍停在 `0`,播放侧不会推进
- 最后已清理本次临时探针:
  - 临时 script 已删除
  - 临时 `.splat4d` 资产与目录已删除

### 总结感悟
- 这条问题最关键的价值,不是“多分配了一个 buffer”,而是它已经从静态候选风险升级成了真实可达的契约不对齐。
- 但它目前更像未来 exporter / 外部资产接入时会稳定踩中的语义坑,而不是当前 raw v1 单帧路径下已经确认的用户可见渲染故障。

## [2026-03-12 16:32:00 +0800] 任务名称: 将 `s1-point_cloud.ply` 重打为 `SOG4D v2 + SH3`

### 任务内容
- 把 `Assets/Gsplat/ply/s1-point_cloud.ply` 从现有的 `v1 + SH3` 旧产物口径,升级为真正的 `v2 + SH3` 产物
- 使用仓库里已经落地的 `four codebooks(sh1/sh2/sh3)` 链路
- 同时完成离线 validate 与 Unity 导入可用性确认

### 完成过程
- 先确认源 PLY 本身具备完整 SH3:
  - `f_rest_* = 45`
  - `restCoeffCount = 15`
  - 自动推导 `bands = 3`
- 再确认旧产物确实还是 v1:
  - `Assets/Gsplat/sog4d/s1_point_cloud_fixed_auto_20260311.sog4d`
  - `version = 1`
  - `shNCentroidsType = f16`
- 然后执行正式重打包:
  - 输出路径:
    - `Assets/Gsplat/sog4d/s1_point_cloud_v2_sh3_20260312.sog4d`
  - 关键参数:
    - `--sh-split-by-band`
    - `--shN-centroids-type f32`
    - `--sh0-codebook-method kmeans`
    - `--shN-labels-encoding delta-v1`
    - `--self-check`
- 最后做两层验证:
  - 离线 `validate ok (v2)`
  - Unity MCP 搜索确认 `assetType = UnityEngine.GameObject`
  - Console 未出现与该资产相关的 error/warning

### 总结感悟
- 这次真正重要的不是“又导出了一份文件”,而是把 `s1` 从旧的 v1 SH3 基线切到了真正的 v2 SH3 基线.
- 后续如果再讨论“为什么还没有 supersplat 那样的质量感”,就应该优先用这份 v2 产物做观察对象,否则输入基线本身就已经落后了.

## 2026-03-12 17:55:00 +0800 任务名称: `s1-point_cloud.ply -> .splat4d v2 + SH3`

### 任务内容
- 把真实源文件 `Assets/Gsplat/ply/s1-point_cloud.ply` 转成 `.splat4d` 的 `v2 + SH3`,不再沿用旧的 `v1 + SH0-only` 路径。
- 补齐离线 exporter、Python 回归、以及后续 Unity batch 验证入口。

### 完成过程
- 修改 `Tools~/Splat4D/ply_sequence_to_splat4d.py`:
  - 新增 `--splat4d-version 2`
  - 新增 `--sh-bands` / `--sh-codebook-count` / `--sh-centroids-type` / `--self-check`
  - 新增单帧 `.splat4d v2 + SH(full)` 导出逻辑
  - 写出 `RECS + META + SHCT + SHLB`
  - 对 v2 明确限制为“单帧输入 + average 模式”,避免把多帧位置和静态 SH 混成不对称资产
- 修改 `Tools~/Splat4D/tests/test_single_frame_cli.py`:
  - 新增 `v2 + SH3` 结构测试
  - 新增“多帧输入拒绝”的失败测试
- 更新文档:
  - `Tools~/Splat4D/README.md`
  - `README.md`
- 新增 Unity batch 验证入口:
  - `Editor/BatchVerifySplat4DImport.cs`
  - `VerifyS1PointCloudV2Sh3()`
- 真实导出产物:
  - `Assets/Gsplat/splat/v2/s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d`

### 验证结果
- `python3 -m unittest Tools~/Splat4D/tests/test_single_frame_cli.py`
  - `Ran 6 tests`
  - `OK`
- `dotnet build Gsplat.Editor.csproj`
  - `0 warnings`
  - `0 errors`
- 真实导出自检:
  - `magic = SPL4DV02`
  - `splatCount = 169133`
  - `shBands = 3`
  - `sectionCount = 8`
  - `META.sh1/sh2/sh3.count = 8192`
  - `labelsEncoding = full`
- Unity 动态导入验证:
  - 当前未完成闭环
  - 直接原因不是 exporter 失败,而是当前打开中的 Unity Editor 会话不处于可用的 MCP 交互状态
  - `refresh_unity` 也在等待 readiness 60s 后超时

### 总结感悟
- 这次最关键的取舍,不是把 `.splat4d v2` 一次性做成“全场景万能格式”,而是先把“单帧高质量 PLY -> v2 + SH3”这条最值当的路径做对。
- 对单帧资产使用 `labelsEncoding=full` 是更干净的语义: 它避免了 `frameCount=1` 的动态 SH 初始化/播放不对称问题。

## 2026-03-12 18:52:00 +0800 任务名称: 修复 `s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d` 颜色偏彩

### 任务内容
- 排查并修复 `Assets/Gsplat/splat/v2/s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d` 的颜色异常。
- 闭环到 Unity importer/runtime,确认不是只在离线脚本层修了一半。

### 完成过程
- 先静态对照 `Editor/GsplatImporter.cs` 与 `Tools~/Splat4D/ply_sequence_to_splat4d.py`。
- 再做两轮动态实验:
  - 同一份 `s1-point_cloud.ply` 同时按 `interleaved` / `channel-major` 解释,比较 chroma 与 grayness
  - 反解当前 `.splat4d` 资产,确认它确实更贴近错误的 `interleaved` 排列
- 然后修复 exporter:
  - `Tools~/Splat4D/ply_sequence_to_splat4d.py`
  - 把 `f_rest_*` 重排改成 `RRR... GGG... BBB... -> [coeff, rgb]`
- 新增 Python 回归测试:
  - `Tools~/Splat4D/tests/test_single_frame_cli.py`
  - 锁定 v2 SH3 导出的 channel-major 契约
- 覆盖重导出目标资产:
  - `Assets/Gsplat/splat/v2/s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d`
- Unity batch verify 初次暴露“full-label 资产残留 delta segments”异常后:
  - 审计 importer/runtime 逻辑
  - 确认 runtime 语义是 `null` 或 `Length==0` 都表示无 delta
  - 调整 `Editor/BatchVerifySplat4DImport.cs` 的判断口径
  - 新增 Editor 测试 `ImportV2_FullLabels_DoesNotExposeUsableDeltaSegments`

### 总结感悟
- 这次最关键的不是 shader 调参,而是把“PLY SH 字段布局契约”找准。
- 另一个容易忽视的点是: Unity 的空数组和 null 在验证口径上要和 runtime 保持一致,否则很容易把验证器自己写成误报源。

## 2026-03-12 19:55:00 +0800 任务名称: 对照 SuperSplat 分析 Unity 高斯呈现为何更脏

### 任务内容
- 对照 `SuperSplat / PlayCanvas engine` 与当前 Unity `Gsplat` 的高斯渲染路径.
- 判断用户看到的"更脏 / 局部颜色更明显"是否来自算法差异.
- 本轮只做证据收集和结论分级,不改代码.

### 完成过程
- 对照了三组关键代码:
  - Unity: `Runtime/Shaders/Gsplat.shader` / `Runtime/Shaders/Gsplat.hlsl`
  - PlayCanvas engine: `gsplat.js` / `gsplatCorner.js` / `gsplatOutput.js`
  - SuperSplat: `splat-shader.ts` / `scene-config.ts`
- 发现 Unity 与 PlayCanvas 的高斯主核 `normExp` 基本一致.
- 进一步确认真正的算法差异在 `aaFactor`:
  - PlayCanvas 在 vertex 阶段把 `corner.aaFactor` 乘回 alpha
  - Unity 虽然保留了 `aaFactor` 计算代码,但主 shader 没有使用,材质侧也没有启用 `GSPLAT_AA`
- 同时用 Unity MCP 读取了当前场景和目标对象:
  - 目标对象是 `s1_point_cloud_v2_sh3_full_k8192_f32_20260312`
  - 当前对象处于 `Gaussian` 模式,且未开启可见性动画
  - 当前 HDRP Volume 开着多种后处理,说明对照环境并不纯净
- 额外做了一个最小数值实验:
  - 证明 `aaFactor` 对小 footprint splat 不是微调,而可能是数倍量级的 alpha 能量差

### 总结感悟
- 这轮最重要的收获不是直接改 shader.
- 而是先把变量拆开了:
  - 高斯主核本身并不是主要分歧
  - 真正值得继续验证的是 `aaFactor` 补偿链路
  - 但在做 shader A/B 之前,必须先把 HDRP 场景后处理隔离掉

## 2026-03-12 20:34:00 +0800 任务名称: 把 `aaFactor` 补偿做成 renderer 级 A/B 开关

### 任务内容
- 实现 `GSPLAT_AA` 的 shader 路径,但不再全局默认开启.
- 改为 renderer 级可切换开关,用于和 SuperSplat 做针对性 A/B.
- 在当前激活对象上打开该开关,便于用户直接观察.

### 完成过程
- 在 `Runtime/Shaders/Gsplat.shader` 中:
  - 增加 `#pragma multi_compile_local __ GSPLAT_AA`
  - 增加 `gaussAlphaMul` varying
  - 让 `ClipCorner` 与 `alphaGauss` 使用同一个 `aaFactor`
  - 保持 `ParticleDots` 路径不受影响
- 在 `Runtime/GsplatRenderer.cs` 与 `Runtime/GsplatSequenceRenderer.cs` 中:
  - 新增 `EnableFootprintAACompensation`
  - 把该开关传到 `GsplatRendererImpl`
- 在 `Runtime/GsplatRendererImpl.cs` 中:
  - 增加 per-renderer keyword 应用逻辑
  - 每个 renderer 的材质实例可独立开/关 `GSPLAT_AA`
- 在 `Runtime/GsplatSettings.cs` 中:
  - 回滚了"全局默认启用 `GSPLAT_AA`"的错误方案
- 用 Unity MCP 把当前对象:
  - `s1_point_cloud_v2_sh3_full_k8192_f32_20260312`
  - 的 `EnableFootprintAACompensation` 设为 `true`

### 验证结果
- `dotnet build ../../Gsplat.Editor.csproj`
  - `0 warnings`
  - `0 errors`
- `dotnet build ../../Gsplat.Tests.Editor.csproj`
  - `0 errors`
  - 有 3 个既有 warning,与本轮改动无关
- Unity compile / refresh 后:
  - console 无新的 shader / C# error
  - 当前对象属性回读确认:
    - `EnableFootprintAACompensation = true`

### 总结感悟
- 这轮真正重要的不是把 `aaFactor` "打开",而是把它放到正确的语义层级.
- 它更像一个数据条件相关的观察开关,而不是适合默认全场景强开的普适修复.

## 2026-03-12 19:55:00 +0800 任务名称: 复核 `EnableFootprintAACompensation` 为何会吃掉细节

### 任务内容
- 重新验证当前 scene 是否仍有 post FX 干扰
- 对照 Unity / PlayCanvas / SuperSplat 的 `aaFactor`、`ClipCorner`、`alphaClip`、`minPixelSize` 语义
- 判断这是不是算法实现错误,还是选项语义与当前数据不匹配

### 完成过程
- 用 Unity MCP 读取 `mcpforunity://scene/volumes`,确认当前 HDRP scene 已无 Volume 组件
- 回读目标对象 `s1_point_cloud_v2_sh3_full_k8192_f32_20260312` 的 `GsplatRenderer`,确认本轮结束时已恢复为 `EnableFootprintAACompensation=false`
- 对照了以下关键源码:
  - Unity: `Runtime/Shaders/Gsplat.hlsl`, `Runtime/Shaders/Gsplat.shader`
  - PlayCanvas: `gsplatCorner.js`, `gsplat.js`, `gsplat-params.js`
  - SuperSplat: `splat-shader.ts`
- 确认 Unity 当前 `aaFactor` 数学公式、`ClipCorner` 公式、2px 早退条件都与 PlayCanvas 主体一致
- 确认 PlayCanvas 参数层明确把 `antiAlias` 标成默认关闭,并注明只适用于 AA-trained / AA-exported 数据
- 做了一轮顺序切换 + 截图回读,得到 "大量轻微像素重分布,不是整图大幅变暗" 的动态证据

### 总结感悟
- 这轮最重要的不是继续改 shader,而是把选项语义和数据契约对上了
- `aaFactor` 不是一个天然应该默认打开的“画质增强”,它本身就依赖源数据前提
- 下一轮如果还要追 SuperSplat 的观感差异,更应该查颜色输出链,而不是继续围绕 `EnableFootprintAACompensation` 打转
