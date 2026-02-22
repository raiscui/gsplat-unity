# WORKLOG

## 2026-02-16 17:40:21 +0800
- 初始化四文件上下文,准备把 4DGS 方案以 OpenSpec change 的形式落盘.

## 2026-02-16 17:50:20 +0800
- 已在仓库根目录执行 `openspec init --tools codex`,生成 `openspec/` 与 `.codex/`.
- 已创建 OpenSpec change: `openspec/changes/add-4dgs-support/`(schema: spec-driven).
- 已生成并通过严格校验的 artifacts:
  - `openspec/changes/add-4dgs-support/proposal.md`
  - `openspec/changes/add-4dgs-support/design.md`
  - `openspec/changes/add-4dgs-support/specs/4dgs-core/spec.md`
  - `openspec/changes/add-4dgs-support/specs/4dgs-playback-api/spec.md`
  - `openspec/changes/add-4dgs-support/specs/4dgs-vfx-backend/spec.md`
  - `openspec/changes/add-4dgs-support/specs/4dgs-resource-budgeting/spec.md`
  - `openspec/changes/add-4dgs-support/tasks.md`

## 2026-02-16 21:38:27 +0800
- 已根据用户反馈调整 `add-4dgs-support` 的 scope:
  - VFX 工作流优先按 `SplatVFX` 风格改进(导入体验 + binder + buffer 驱动 VFX Graph).
  - `.splat4d` 二进制格式被提升为一期 tasks,不再只是二期备忘.
- 已更新并重新严格校验 artifacts:
  - `openspec/changes/add-4dgs-support/proposal.md`
  - `openspec/changes/add-4dgs-support/design.md`
  - `openspec/changes/add-4dgs-support/specs/4dgs-core/spec.md`
  - `openspec/changes/add-4dgs-support/specs/4dgs-vfx-backend/spec.md`
  - `openspec/changes/add-4dgs-support/tasks.md`
- `openspec validate add-4dgs-support --strict` 已通过,任务数更新为 32 项.

## 2026-02-17 00:18:02 +0800
- 已完成 `add-4dgs-support` 的剩余实现任务,并把 change 标记为 complete(32/32):
  - VFX Graph 后端:
    - 新增 `Runtime/VFX/GsplatVfxBinder.cs`(基于 VFX Property Binder 绑定 GPU buffers,并同步 `TimeNormalized`).
    - 新增 `Runtime/Shaders/GsplatVfx.compute`(生成 AxisBuffer,并按 4DGS 语义生成动态 position/color).
    - `.splat4d` 导入器增强为 SplatVFX 风格: 自动生成 prefab + 可选 VisualEffect + binder.
    - 增加 `Samples~/VFXGraphSample` 作为最小可用 sample,并提供验证清单.
  - 文档:
    - `README.md` 补充 4D PLY 字段、`.splat4d` 格式、播放控制与 VFX 后端说明.
    - `Documentation~/Implementation Details.md` 补充 4D buffers、排序 key、shader 裁剪与 bounds 扩展说明.
  - OpenSpec:
    - `openspec validate add-4dgs-support --strict` 已通过.

## 2026-02-17 00:02:11 +0800
- 已输出 `add-4dgs-support` 的 change 状态与 artifacts 列表,并向用户解释:
  - 当前 3DGS 的主渲染管线是 "compute 排序 + 自定义 shader 绘制".
  - 4DGS 的主后端是在这条管线上扩展 `pos(t)` 与时间窗裁剪,并提供 `TimeNormalized` 播放控制.
  - VFX Graph 后端是可选的工作流后端,按 `SplatVFX` 风格实现 binder + sample,并设置规模上限与回退策略.


## 2026-02-17 10:23:51 +0800
- 已把 OpenSpec delta specs 同步到主 specs(用于长期维护):
  - `openspec/specs/4dgs-core/spec.md`
  - `openspec/specs/4dgs-playback-api/spec.md`
  - `openspec/specs/4dgs-vfx-backend/spec.md`
  - `openspec/specs/4dgs-resource-budgeting/spec.md`
- 已归档 OpenSpec change:
  - `openspec/changes/archive/2026-02-17-add-4dgs-support/`
- 归档后 `openspec list --json` 输出为空 changes,说明 active changes 已清空.


## 2026-02-17 10:43:38 +0800
- 已补充 `.splat4d` 的离线导出工具与说明,用于把动态 3DGS/4DGS 的 PLY 序列转换为 `.splat4d`:
  - `Tools~/Splat4D/ply_sequence_to_splat4d.py`
  - `Tools~/Splat4D/README.md`
- 已在 `notes.md` 记录 4DGaussians/FreeTimeGsVanilla 等仓库的字段映射结论与两条生成路线(平均速度 vs 分段 keyframe).


## 2026-02-17 11:25:24 +0800
- 已在内部 MIT 版 FreeTimeGsVanilla 增加 `.splat4d` 直出导出器(从 ckpt_*.pt 导出):
  - `../FreeTimeGsVanilla/src/export_splat4d.py`
  - `../FreeTimeGsVanilla/README.md` 增加了使用说明
- 该导出器使用 `temporal_threshold` 把 FreeTimeGS 的时间高斯核(σ=exp(durations))裁成硬窗口,并把 position 平移到窗口起点,以兼容本仓库 `.splat4d` 的 time/duration 语义.

## 2026-02-17 11:36:16 +0800
- 已回读归档的 OpenSpec change(`openspec/changes/archive/2026-02-17-add-4dgs-support/`)与现有导出工具说明.
- 已整理并输出两条导出路线给用户:
  - `hustvl/4DGaussians`: `export_perframe_3DGS.py` 导出 PLY 序列,再用 `Tools~/Splat4D/ply_sequence_to_splat4d.py` 生成 `.splat4d`.
  - `FreeTimeGsVanilla`: 直接从 checkpoint(`ckpt_*.pt`)用 `src/export_splat4d.py` 生成 `.splat4d`.

## 2026-02-17 12:08:41 +0800
- 已对本机目录 `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp` 的 `time_*.ply` 做 header 扫描,核对“点数是否一致”.
- 结论:
  - 20 帧 PLY 的 vertex 点数一致,每帧均为 `element vertex 27673`.
  - `format` 为 `binary_little_endian`,vertex property layout 也在全帧一致.
- 影响:
  - 满足“按 vertex index 对齐”这一前提,可以用 `Tools~/Splat4D/ply_sequence_to_splat4d.py` 走差分/分段拟合 velocity 的路线生成 `.splat4d`.

## 2026-02-17 12:47:41 +0800
- 修复了离线工具 `Tools~/Splat4D/ply_sequence_to_splat4d.py` 的一个关键边界问题:
  - 旧版 keyframe 模式在 `(frames-1)` 不能被 `--frame-step` 整除时会遗漏尾段,导致 `t` 接近 1.0 时 splat 全部不可见.
  - 新版会自动补一个更短的尾段 segment,保证覆盖到最后一帧(`t=1.0`).
- 已同步更新 `Tools~/Splat4D/README.md`,补充 keyframe 分段说明与输出规模估算公式.

## 2026-02-17 16:32:24 +0800
- 已按用户要求用 average 模式把本机 4DGaussians 导出的 PLY 序列转换为可导入 Unity 的 `.splat4d`:
  - input: `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp/time_*.ply`
  - output: `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp_average.splat4d`
  - record 数: 27673(文件大小 1771072 bytes,对齐 64 bytes/record)

## 2026-02-17 21:08:36 +0800
- 修复 Unity 编译错误: `Runtime/VFX/GsplatVfxBinder.cs` 引用 `VFXPropertyBinding` 导致 CS0246.
  - 处理方式: 移除所有 `[VFXPropertyBinding(...)]` 标注,仅保留 `[SerializeField]`.
  - 原因: 该 attribute 在部分 Unity/VFX Graph 版本中不可用或不可被 Runtime asmdef 引用,但对运行时绑定逻辑非必需.

## 2026-02-18 08:49:20 +0800
- 新增贡献者指南: `AGENTS.md`.
  - 说明了本仓库(UPM 包)的目录结构(`Runtime/`, `Editor/`, `Samples~/`, `Documentation~/`, `Tools~/`, `openspec/`).
  - 提供了关键开发与验证命令示例(例如 `.splat4d` 离线转换脚本).
  - 总结了代码风格与可选依赖的编译开关(`GSPLAT_ENABLE_URP/HDRP/VFX_GRAPH`)以及提交/PR 约定.
  - 同步补齐 `AGENTS.md.meta`,避免 Unity 导入期自动生成 meta 导致 GUID 漂移.

## 2026-02-18 09:40:45 +0800
- 新增 Unity Test Framework 最小测试骨架(EditMode),用于本地/CI 的快速回归:
  - `Tests/Editor/Gsplat.Tests.Editor.asmdef`:
    - Editor-only,并启用 `TestAssemblies` 引用,依赖 `Runtime/Gsplat.asmdef`.
  - `Tests/Editor/GsplatUtilsTests.cs`:
    - 覆盖 `GsplatUtils` 的纯逻辑函数(`SHBands`,`EstimateGpuBytes`,`CalcWorldBounds`).
  - 已补齐相关 `.meta`,避免 Unity 导入期生成 GUID 漂移.
  - 已同步更新 `AGENTS.md` 的 Testing Guidelines,明确测试入口与位置.

## 2026-02-18 12:47:59 +0800
- 已按用户要求从 `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp` 输出两个单帧 `.splat4d`:
  - frame 2:
    - input: `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp/time_00002.ply`
    - output: `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp_frame_00002.splat4d`
  - frame 5:
    - input: `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp/time_00005.ply`
    - output: `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp_frame_00005.splat4d`
- 导出方式:
  - 使用 `Tools~/Splat4D/ply_sequence_to_splat4d.py --mode average`.
  - 每次只输入 1 帧 PLY,因此 velocity=0,time=0,duration=1(静态快照,全时间可见).
- 输出校验:
  - record 数: 27673
  - 文件大小: 1771072 bytes(=27673*64,对齐 64 bytes/record)

## 2026-02-18 13:03:50 +0800
- 用户澄清需求: 需要的是 keyframe 序列 `.splat4d`(不是单帧快照).
- 已从 `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp/time_*.ply` 导出两份 keyframe 序列文件:
  - frame_step=2:
    - output: `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp_keyframe_step_00002.splat4d`
    - frames=20, segments=10
    - records=276730, size=17710720 bytes(=276730*64)
  - frame_step=5:
    - output: `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp_keyframe_step_00005.splat4d`
    - frames=20, segments=4
    - records=110692, size=7084288 bytes(=110692*64)
- 语义说明:
  - 每个 segment 写一批 records,其 `time=time0`,`duration=dt`,velocity 为该段差分得到的常量速度.
  - Unity 侧会用 `pos(t)=pos0+vel*(t-time0)` 还原该段线性运动.

## 2026-02-18 13:15:30 +0800
- 已对比 PlayCanvas `SOG(.sog)` 与 `KSPLAT(.ksplat)` 的文件形态与压缩策略(用于规划 `.splat4d` 下一代格式).
- 结论摘要:
  - `.sog`: 多 WebP + `meta.json` 的“纹理流”格式,强调高压缩与 GPU 就绪,并且更容易通过“加新纹理”扩展新属性.
  - `.ksplat`: 单文件二进制,强调加载速度(格式贴合某个 viewer 的内部布局),并提供压缩等级(0/1/2).
- 对 `.splat4d` 改版的建议(当前倾向):
  - Unity 侧优先保持“单文件容器”减少资产碎片(更像 `.ksplat`).
  - 编码层则优先借鉴 `.sog` 的量化与 SH palette 思路,以获得更好的体积/带宽收益.

## 2026-02-18 13:32:34 +0800
- 已按路线B新建 OpenSpec change: `sog4d-sequence-format`.
  - 路径: `openspec/changes/sog4d-sequence-format/`
  - schema: spec-driven
  - artifacts: 0/4,proposal 已 ready.
- 已获取 proposal 的 instructions/template,用于下一步“细细讨论”时直接起草:
  - `openspec instructions proposal --change "sog4d-sequence-format"`

## 2026-02-18 14:02:59 +0800
- 已写入路线B的 proposal 文档(解释 WHY,并拆分 capabilities 边界):
  - `openspec/changes/sog4d-sequence-format/proposal.md`
- `openspec status --change "sog4d-sequence-format"` 显示进度 1/4,已解锁 `specs` 与 `design`.

## 2026-02-18 14:05:09 +0800
- 修复 VFX 手工搭建时的常见配置缺失:
  - `GsplatVfxBinder` 在 Inspector 漏配 `VfxComputeShader` 时,
    会在 Editor 下自动回填默认 compute shader:
    - `Packages/wu.yize.gsplat/Runtime/Shaders/GsplatVfx.compute`
  - 代码落点: `Runtime/VFX/GsplatVfxBinder.cs`(Reset/OnValidate/OnEnable,严格 `#if UNITY_EDITOR`).
- 回归与文档同步:
  - `Tests/Editor/Gsplat.Tests.Editor.asmdef` 增加 `GSPLAT_ENABLE_VFX_GRAPH` 的 `versionDefines`.
  - 新增 EditMode 单测: `Tests/Editor/GsplatVfxBinderTests.cs`.
  - 更新 sample 文档: `Samples~/VFXGraphSample/README.md`(手工搭建步骤注明默认会自动填充).

## 2026-02-18 14:21:29 +0800
- 修复 EditMode 测试编译错误(用户反馈 CS0012/CS1061):
  - 原因: 测试强类型引用 `GsplatVfxBinder` 导致 tests 需要显式引用 `Unity.VisualEffectGraph.Runtime`,
    但 Unity asmdef 不会自动带上传递依赖.
  - 修复: `Tests/Editor/GsplatVfxBinderTests.cs` 改为反射获取类型 + `Behaviour.enabled` 触发 `OnEnable`,
    从而避免对 VFX Runtime 的编译期硬依赖; 当类型不存在时直接 `Assert.Ignore`.
- 已用 Unity MCP 触发 `refresh_unity` 编译并读取 Console:
  - 未再出现该组编译错误.

## 2026-02-18 15:15:11 +0800
- 修复 VFX 后端无法随 `TimeNormalized` 播放 4DGS 动画的问题:
  - 根因: `Samples~/VFXGraphSample/VFX/Splat.vfx` 之前缺失 Update Context,导致 buffer 只在 Initialize 阶段写入粒子属性,后续不再更新.
  - 修复:
    - `Samples~/VFXGraphSample/VFX/InitializeSplat.vfxblock`: 允许在 Init+Update 阶段使用(用于复用同一套“从 buffer 写回属性”的逻辑).
    - `Samples~/VFXGraphSample/VFX/Splat.vfx`: 增加 `VFXBasicUpdate` Context,并改为 Spawn -> Init -> Update -> Output,让粒子每帧从 `PositionBuffer/ColorBuffer` 刷新.
- Unity MCP 验证:
  - `refresh_unity` 后 Console 未出现 VFX Graph 资产导入/编译错误.

## 2026-02-18 17:20:11 +0800
- 已完成 OpenSpec change `sog4d-sequence-format` 的关键编码决策收敛(性能优先):
  - opacity 并入 `sh0.webp` alpha,移除独立 `opacity.webp` stream.
  - u16 labels 统一采用 little-endian(`index = r + (g << 8)`),对齐 SOG v2.
  - SH 高阶系数采用 `shN_centroids.bin`(默认 float16 little-endian) + per-frame `shN_labels.webp`,避免 palette 走 JSON.
  - `{frame}` 路径模板定义为十进制 frameIndex,左侧补零到至少 5 位.
- 已同步更新 artifacts 并通过严格校验:
  - `openspec validate sog4d-sequence-format --strict`

## 2026-02-18 15:34:22 +0800
- 已按路线B补齐 OpenSpec change `sog4d-sequence-format` 的全部 artifacts(4/4):
  - `openspec/changes/sog4d-sequence-format/proposal.md`
  - `openspec/changes/sog4d-sequence-format/specs/**/spec.md`
  - `openspec/changes/sog4d-sequence-format/design.md`
  - `openspec/changes/sog4d-sequence-format/tasks.md`
- 已对齐可落地性与严格校验:
  - 修正多个 spec requirement 的首句,确保包含 MUST,从而通过 `openspec validate --strict`.
  - 补充 time mapping 的边界语义:
    - uniform: `frameCount==1` 时定义 `t0=0.0`
    - explicit: `t[i1]==t[i0]` 时定义 `a=0.0`
- 已补齐 Unity `.meta`:
  - `openspec/changes/sog4d-sequence-format/design.md.meta`
  - `openspec/changes/sog4d-sequence-format/tasks.md.meta`
- 验证:
  - `openspec validate sog4d-sequence-format --strict` 通过.

## 2026-02-18 18:44:20 +0800
- [OpenSpec][sog4d] 拍板默认策略: `delta-v1` 默认启用 + `shNCount=8192`
  - 变更:
    - `openspec/changes/sog4d-sequence-format/design.md`: 明确 exporter 默认 `shNLabelsEncoding="delta-v1"`,默认 `shNCount=8192`,segment 默认 50.
    - `openspec/changes/sog4d-sequence-format/tasks.md`: 补充 exporter 默认行为的任务备注.
    - `notes.md`: 追加你的偏好与默认值落盘记录.
  - 验证:
    - `openspec validate sog4d-sequence-format --strict` 通过.

## 2026-02-18 15:35:59 +0800
- 修复你当前 Unity 工程里 "sample copy 不自动更新" 导致的持续复现:
  - 发现工程内存在 Unity 复制出来的 sample:
    - `Assets/Samples/Gsplat/1.1.2/VFX Graph Sample(SplatVFX style)`
  - 这份 copy 仍是旧版 VFX Graph(缺失 Update Context),会继续表现为“只有按 `VisualEffect.Play()` 才刷新一帧”.
- 已把包内 sample 的关键修复同步覆盖到该 copy(仅 3 个文件):
  - `Assets/Samples/Gsplat/1.1.2/VFX Graph Sample(SplatVFX style)/VFX/Splat.vfx`
  - `Assets/Samples/Gsplat/1.1.2/VFX Graph Sample(SplatVFX style)/VFX/InitializeSplat.vfxblock`
  - `Assets/Samples/Gsplat/1.1.2/VFX Graph Sample(SplatVFX style)/README.md`
- Unity MCP 验证:
  - `refresh_unity` 后读取 Console,未出现新的导入/编译错误.

## 2026-02-18 15:48:48 +0800
- 同步外部包目录 `/Users/cuiluming/local_doc/l_dev/my/unity/gsplat-unity` 的关键修复,避免两份包行为分叉:
  - `Runtime/VFX/GsplatVfxBinder.cs`:
    - Editor 下自动回填默认 `VfxComputeShader`.
    - `OnEnable/OnDisable` 调用基类,保证 binder 注册/注销稳定.
    - 缺失报错提示复用默认路径常量.
  - `Samples~/VFXGraphSample`:
    - `VFX/InitializeSplat.vfxblock` 允许在 Update 阶段复用.
    - `VFX/Splat.vfx` 增加 Update Context,实现真正每帧刷新.
    - `README.md` 同步说明与排查提示.

## 2026-02-18 18:20:48 +0800
- [OpenSpec][sog4d] 补齐 SH codebook/palette 生成策略与 delta labels 的工程化落点
  - 更新 design(把生成策略,默认值建议,以及 `delta-v1` 的 trade-off 写清楚):
    - `openspec/changes/sog4d-sequence-format/design.md`
  - 更新 tasks(补充 importer/exporter/tests 对 `delta-v1` 的任务拆分):
    - `openspec/changes/sog4d-sequence-format/tasks.md`
  - 追加 notes(修正 DualGS arXiv 链接,并核对 "only one percent... save the first frame + changed positions" 的原文):
    - `notes.md`
- 验证:
  - `openspec validate sog4d-sequence-format --strict` 通过.

## 2026-02-18 22:13:00 +0800
- 收尾验证(不启动 Unity):
  - `openspec validate sog4d-sequence-format --strict`
  - `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py validate --input Tests/Editor/Sog4DTestData/minimal_valid_delta_v1.sog4d.zip`
  - `python3 -m compileall Tools~/Sog4D -q`
- 四文件维护:
  - 因 `task_plan.md` 超过 1000 行,已续档为 `task_plan_2026-02-18_220625.md`,并新开 `task_plan.md`.
- 下一步(还未执行):
  - 把 change `sog4d-sequence-format` 的 delta specs 同步到 `openspec/specs/**`.
  - 归档 change 到 `openspec/changes/archive/YYYY-MM-DD-sog4d-sequence-format/`.

## 2026-02-18 22:23:00 +0800
- 已同步 delta specs -> main specs:
  - 新增 main specs:
    - `openspec/specs/sog4d-container/spec.md`
    - `openspec/specs/sog4d-sequence-encoding/spec.md`
    - `openspec/specs/sog4d-unity-importer/spec.md`
    - `openspec/specs/4dgs-keyframe-motion/spec.md`
  - 更新 main spec:
    - `openspec/specs/4dgs-resource-budgeting/spec.md`(纳入 `.sog4d` 的纹理/palette/双帧窗口/插值降级等预算因素)
    - `openspec/specs/4dgs-playback-api/spec.md`(补齐 Purpose 长度,通过 strict 校验)
- Specs 验证:
  - `openspec validate --specs --strict` 通过
  - `openspec validate --all --strict` 通过
- 已归档 change:
  - `openspec/changes/archive/2026-02-18-sog4d-sequence-format/`
  - `openspec list --json` 显示无 active changes

## 2026-02-19 10:11:17 +0800
- 修复 Unity 编译歧义引用(CS0104):
  - `Tests/Editor/GsplatSog4DImporterTests.cs`: 使用 `ZipCompressionLevel` 别名指向 `System.IO.Compression.CompressionLevel`
  - `Editor/GsplatSog4DImporter.cs`: 使用 `Object` 别名指向 `UnityEngine.Object`

## 2026-02-19 10:34:20 +0800
- Unity 侧验证完成(已重新编译并执行 EditMode tests):
  - 报告文件: `/Users/cuiluming/Library/Application Support/DefaultCompany/st-dongfeng-worldmodel/TestResults.xml`
  - 汇总: total=17, passed=11, failed=0, skipped=6
- 说明:
  - Console 中的 `import error:` 日志为 importer 负例用例刻意触发,用于验证报错信息,属于预期输出.
  - skipped 的 6 个用例原因为当前 Unity 版本不支持 WebP 解码(测试已按预期跳过).

## 2026-02-19 11:17:50 +0800
- 让 `.sog4d` importer 的 WebP 解码在 macOS Editor 可用,从而跑满回归测试:
  - 新增原生 decoder: `Editor/Plugins/macOS/libGsplatWebpDecoder.dylib`(libwebpdecoder,universal: arm64+x86_64)
  - 新增 P/Invoke 包装: `Editor/GsplatWebpNative.cs`
  - Importer fallback: `Editor/GsplatSog4DImporter.cs` 在 `ImageConversion.LoadImage` 返回 false 时自动改走 `GsplatWebpNative.TryDecodeRgba32`
  - Tests 探测改造: `Tests/Editor/GsplatSog4DImporterTests.cs` 改为反射调用 `GsplatWebpNative.SupportsWebpDecoding`,避免被 `LoadImage==false` 误判导致 Ignore
- 验证(证据型):
  - Unity 6000.3.8f1 临时工程下 `Gsplat.Tests.GsplatSog4DImporterTests`:
    - passed=10, failed=0, skipped=0

## 2026-02-19 11:55:25 +0800
- 让 `.sog4d` importer tests 里原本 skipped 的 6 个用例可稳定执行:
  - 补齐 native plugin `.meta`(避免 Unity 导入/加载不稳定):
    - `Editor/Plugins.meta`
    - `Editor/Plugins/macOS.meta`
    - `Editor/Plugins/macOS/libGsplatWebpDecoder.dylib.meta`
  - tests 的 WebP 能力探测与 importer 对齐:
    - `Tests/Editor/GsplatSog4DImporterTests.cs`: 先尝试 `ImageConversion.LoadImage`,失败再反射调用 `Gsplat.Editor.GsplatWebpNative` 的 native fallback.
  - 额外修复: 让 CI/命令行(`-batchmode -nographics`)也能跑测试而不被渲染初始化噪声击穿:
    - `Runtime/GsplatSettings.cs`: 在 `SystemInfo.graphicsDeviceType==Null` 时跳过 sorter 初始化,避免 "Kernel 'InitPayload' not found" 的 error log 导致单测失败.
- 证据型验证:
  - Unity 6000.3.8f1 临时工程: `/private/tmp/gsplat_webp_test_project_02_results.xml`
  - `Gsplat.Tests.GsplatSog4DImporterTests`: passed=10, failed=0, skipped=0

## 2026-02-19 12:41:19 +0800
- 真实工程回归测试已确认“原本 skipped 的 6 个用例”不再跳过:
  - 报告: `/Users/cuiluming/Library/Application Support/DefaultCompany/st-dongfeng-worldmodel/TestResults.xml`
  - 汇总: total=17, passed=17, failed=0, skipped=0
- 备注:
  - Console 中的 `import error:` 是负例用例刻意触发,用于验证报错信息,属于预期输出.

## 2026-02-19 16:02:27 +0800
- 修复 `.sog4d` 序列播放时的 compute kernel invalid:
  - `Runtime/Shaders/GsplatSequenceDecode.compute`: 数据图读取改为 `Texture2DArray<float4>` + `Float4ToU8` 还原 byte,提升 Metal/跨平台兼容性.
  - `Runtime/GsplatSequenceRenderer.cs`: 增加 kernel 有效性 fail-fast(`GetKernelThreadGroupSizes`)与 `-nographics` guard,避免 Dispatch 只刷 error log 的黑盒.
- 离线工具验证(证据型):
  - `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py validate --input Tests/Editor/Sog4DTestData/minimal_valid_delta_v1.sog4d.zip`

## 2026-02-19 17:49:46 +0800
- 已把 `.sog4d` 的 pack 生成命令整理成手册(多配置菜谱):
  - `Tools~/Sog4D/README.md`:
    - 依赖与环境准备(pack/validate 的依赖差异).
    - 输入数据前提(必需字段,每帧一致性,命名排序).
    - 多套可复制粘贴的打包命令(SH0-only,SH3+delta-v1,full labels,explicit time,体积/质量取舍等).
    - 参数速查与 Unity 播放常见报错提示.

## 2026-02-19 17:54:42 +0800
- 修复 macOS/Metal 下 `.sog4d` 序列 decode compute shader 编译失败:
  - 根因: Metal 不支持 `StructuredBuffer.GetDimensions` 这类 buffer size query.
  - `Runtime/Shaders/GsplatSequenceDecode.compute`: 移除 `GetDimensions`,改为由 C# 传入 `_ScaleCodebookCount/_Sh0CodebookCount/_ShNCentroidsCount`.
  - `Runtime/GsplatSequenceRenderer.cs`: dispatch 前显式 `SetInt(...)` 传入各 buffer count,用于 shader 内 clamp/越界防御.

## 2026-02-19 18:53:37 +0800
- continuous-learning(提取可复用知识):
  - 新增用户级 skill: `~/.codex/skills/self-learning.unity-metal-compute-kernel-invalid/SKILL.md`
  - 更新项目协作约定: `AGENTS.md` 补充 Metal compute 的 `GetDimensions` 限制,以及 RGBA8 UNorm 数据图读取建议.

## 2026-02-19 19:12:31 +0800
- 修复 Metal 下 decode kernel 验证误判(避免 `GetKernelThreadGroupSizes` 误报阻塞播放):
  - `Runtime/GsplatSequenceRenderer.cs`:
    - 仅验证当前会实际使用的 kernel(`shBands==0` 用 SH0,否则用 SH).
    - 先用 `ComputeShader.IsSupported` 做能力探测.
    - `GetKernelThreadGroupSizes` 降级为非强制检查(抛 `IndexOutOfRangeException` 时仅 warning).
    - `OnEnable/Update` 稳态优化: decode 失败不再触发每帧 renderer 重建刷屏; asset 变化时先 `DisposeDecodeResources`.

## 2026-02-19 19:14:49 +0800
- 清理 Metal 下的 shader warning(避免 Console 污染):
  - `Runtime/Shaders/GsplatSequenceDecode.compute`: 移除 `isnan/isinf` 检查,仅保留 epsilon 防御.
  - 回溯 `LATER_PLANS.md`: 已完成该候选项并清除.

## 2026-02-19 20:13:39 +0800
- 已从 4DGaussians 导出的 PLY 序列输出“质量优先”的 `.sog4d`(用于 Unity 序列播放):
  - input: `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp/time_*.ply`
  - output: `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp/gaussian_pertimestamp_quality_sh3.sog4d`
  - 校验: `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py validate --input /Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp/gaussian_pertimestamp_quality_sh3.sog4d`
    - 输出: `[sog4d] validate ok (delta-v1).`
  - meta.json 核心字段:
    - splats=27673, frames=20, layout=167x166
    - shBands=3, shNCount=8192, shNCentroidsType=f32, shNLabelsEncoding=delta-v1
- `.sog4d` pack 工具稳态改进(避免高质量配置下采样崩溃):
  - `Tools~/Sog4D/ply_sequence_to_sog4d.py`:
    - 新增 `_weighted_choice_no_replace`,修复 `numpy.random.Generator.choice(replace=False,p=...)` 在非零权重不足时抛 ValueError 的问题.
    - `importance=opacity*volume` 改为在 float64 下计算(避免 float32 下溢导致大量 0 权重).
    - sh0/scale/shN 的采样逻辑统一走同一套稳态函数,减少边界分叉.

## 2026-02-19 21:12:30 +0800
- 为了确认 `ply_sequence_to_sog4d.py pack` 在多配置下都能稳定工作,我额外输出了多档 `.sog4d` 并逐个 `--self-check`:
  - 输出目录: `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp_out/`
  - SH0-only(最快,最小路径):
    - `gaussian_pertimestamp_fast_sh0.sog4d`
    - `validate ok (bands=0)`, size=7.3MiB
  - SH3 + delta-v1(多 segment,覆盖 segment 边界逻辑):
    - `gaussian_pertimestamp_balanced_sh3_delta_seg5_shN4096_f16.sog4d`
    - `validate ok (delta-v1)`, size=7.8MiB
  - SH3 + full labels(覆盖 full labels 路径):
    - `gaussian_pertimestamp_balanced_sh3_full_shN8192_f16.sog4d`
    - `validate ok (full labels)`, size=8.7MiB
- 同步改良文档,避免“命令看起来不同但其实是同一套配置”的困惑:
  - `Tools~/Sog4D/README.md` 的 "2.5 质量优先" 现在显式写出:
    - `--opacity-mode/--scale-mode` 的默认值与何时需要强制指定.
    - `--zip-compression` 默认值与 `deflated` 的实际收益点.
- 补充:
  - 为了避免上游脚本清理 input 目录导致输出丢失,我把 `.sog4d` 输出集中放到:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/gaussian_pertimestamp_out/`
  - 质量优先版本也已复制到该目录:
    - `gaussian_pertimestamp_out/gaussian_pertimestamp_quality_sh3.sog4d`

## 2026-02-21 15:34:59 +0800
- 为了让 FreeTimeGsVanilla 改版 exporter 能直接生成可被 Unity 导入的 `.sog4d`,我回读了 importer + pack 工具,并把规格对齐到实现.
- 已更新规格文档,补齐实现里已经支持但 OpenSpec 之前未覆盖的部分:
  - `openspec/specs/sog4d-sequence-encoding/spec.md`:
    - 补齐 `meta.json.version=2` 的 SH rest per-band 编码(sh1/sh2/sh3).
    - 补齐 delta-v1 update entry 的真实布局与约束:
      - `(u32 splatId, u16 newLabel, u16 reserved=0)`.
      - 同一个 block 内 `splatId` 必须严格递增.
- 规格一致性校验:
  - `openspec validate --specs --strict` 通过(8 passed).
- 已在对话中输出统一后的 `.sog4d` 详细规格,并给出权威参考文件路径(Importer/Tools/Specs).

## 2026-02-21 16:20:30 +0800
- 为了让 FreeTimeGsVanilla 改版更快落地 `.sog4d` exporter,我补了一份“从 checkpoint 到 `.sog4d`”的数据流映射手册:
  - `Tools~/Sog4D/FreeTimeGsCheckpointToSog4D.md`
- 同时在工具 README 增加入口,让使用者不需要翻聊天记录:
  - `Tools~/Sog4D/README.md`

## 2026-02-22 15:20:43 +0800
- 修复 FreeTimeGsVanilla 导出的 legacy `.sog4d` meta.json 兼容问题(让 Unity 能导入):
  - `Editor/GsplatSog4DImporter.cs` / `Runtime/GsplatSog4DRuntimeBundle.cs`:
    - ZIP entry map 改为“同名取最后一个”,符合 zip update 语义,也支持通过追加修复 `meta.json`.
  - `Tools~/Sog4D/ply_sequence_to_sog4d.py`:
    - 新增 `normalize-meta` 子命令:
      - 自动补齐 `meta.format="sog4d"`.
      - 把 Vector3 字段从 `[[x,y,z]]` 规范化为 `{x,y,z}`(Unity `JsonUtility` 可解析).
    - 已对真实文件执行并 `validate ok`:
      - `python3 Tools~/Sog4D/ply_sequence_to_sog4d.py normalize-meta --input <file>.sog4d --validate`
- 改良 `.splat4d` 一键导入的 VFX 默认资产查找:
  - `Editor/GsplatSplat4DImporter.cs`:
    - 增加对 `Assets/Samples/**/VFX/SplatSorted.vfx`/`Splat.vfx` 的搜索(适配 Unity 的 Sample import 落点).
    - 找不到时的 warning 文案更准确,减少误导.
- 文档同步:
  - `Tools~/Sog4D/README.md` 增加 `normalize-meta` 用法与典型报错解释.
