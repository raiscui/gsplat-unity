# 笔记: 2026-03-12 continuous-learning 六文件摘要

## 当前状态

- 旧的 `task_plan.md` / `notes.md` / `WORKLOG.md` 已因超过 1000 行续档到 `archive/`。
- 本文件用于保存本轮 continuous-learning 的摘要与沉淀决策。

## 六文件摘要(用于决定如何沉淀知识)

- 涉及的上下文集:
  - 默认组
  - `__imgui_layout_error`
  - `__sog4d_display_issue`
  - `__splat4d_edge_opacity`
  - `__splat4d_single_frame_support`
- 默认组任务目标(`archive/task_plan_2026-03-12_232802.md` 最新段落):
  - 先排查真实多帧 `.splat4d` 是否复用了单帧 PLY 的 SH 排列错误
  - 在缺少原始 ckpt 时,改走真实资产 `SHCT/SHLB/SHDL` 自证路径
- 默认组关键决定:
  - 不把“像根因”直接写成“已确认根因”
  - 当原始 ckpt 缺失时,改用真实 `.splat4d` 字节双重解码做动态证据
- 默认组关键发现(`archive/notes_2026-03-12_232802.md` 最新段落):
  - 真实资产 `ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest.splat4d` 的 `SHCT` 用两套独立索引解码后,三个 band 的 `max_abs_diff` 都是 `0.0`
  - 当前没有证据支持“这份真实多帧资产的颜色问题来自 `SHCT` 写/读 layout 不一致”
- 默认组实际变更(`archive/WORKLOG_2026-03-12_232802.md` 最新段落):
  - 已把“无原始 ckpt 时改做资产自证”沉淀为动态验证方法
  - 默认组六文件原文件因超长已在本轮续档并转入 `archive/`
- 支线组摘要:
  - `__imgui_layout_error`:
    - 现象不是 package 自身 importer 直接导致
    - 更像工程里的 `vInspector / vHierarchy` 在 `BeginScrollView` 包装 `editor?.OnInspectorGUI()` 时缺少 `try/finally`,被 `ExitGUI` 提前打断后布局栈泄漏
  - `__sog4d_display_issue`:
    - 单帧 `.sog4d` 发白的主因在离线 `sh0` 编码策略,不是 runtime shader
    - `sh0-codebook-method=base-rgb` 能把 `sh0/alpha` 严格对齐到同源 `.splat4d`
  - `__splat4d_edge_opacity`:
    - 与 SuperSplat 的关键差异已从“主核公式”收敛到“coverage 保留策略”
    - 更高价值的长期知识是: `ScriptedImporter` 改变输出资产形态时,必须 bump version,否则旧资产会制造“修复没生效”的假象
  - `__splat4d_single_frame_support`:
    - 单帧 PLY 的 `f_rest_*` 真实契约是 channel-major(`RRR... GGG... BBB...`),不是 `RGBRGB...`
    - `EnableFootprintAACompensation` 默认不应强开,它更像 AA-trained / AA-exported 数据的对照开关
- 暂缓事项 / 后续方向(`LATER_PLANS*.md`):
  - `scale codebook` 少量大离群误差仍值得后续验证
  - `.splat4d v2` 多帧 exporter 与 Unity 在线验证还可继续
  - 若继续逼近 SuperSplat 观感,优先看颜色输出链和 coverage 校准,不要再只盯 fragment 主核
- 错误与根因(`ERRORFIX*.md`):
  - `Kernel BuildAxes not found` 本质是 headless `GraphicsDeviceType.Null` 下不该继续走 VFX kernel 检查
  - `.sog4d` 发白本质是 learned `sh0Codebook` 抬亮了暗色负值基元
  - `.ply` 无法直接拖入场景本质是 importer 只产出 `GsplatAsset`,而且旧资产未因 version 变化自动重导入
- 重大风险 / 重要规律(`EPIPHANY_LOG*.md`):
  - 视觉对照前必须重新读取当前 scene / camera / volume 状态,不能复用上一轮现场假设
  - 算法契约不能只看 shader 数学,还要看参数层默认值和适用数据条件
  - `ScriptedImporter` 输出形态变化若不 bump version,现场会持续保留旧缓存结果
- 可复用点候选:
  1. `ScriptedImporter` 输出形态变化 -> `SetMainObject` + version bump + main object 回归测试
  2. 视觉问题排查前重新读取现场状态,避免用过期的 scene/volume 假设继续推理
  3. 数据条件相关的 AA/画质开关,不应因为 shader 链路存在就默认全局开启
- 最适合写到哪里:
  - cross-project 经验:
    - `self-learning.unity-scriptedimporter-version-bump`
  - repo-specific 约定:
    - `AGENTS.md`
  - 对外使用文档:
    - `README.md`
    - `CHANGELOG.md`
- 需要同步的现有文档:
  - `AGENTS.md`
  - `README.md`
  - `CHANGELOG.md`
- 是否需要新增或更新 `docs/` / `specs/` / plan 文档:
  - 否
  - 原因:
    - 本轮最强沉淀点是导入器工作流规则与用户可见导入行为说明
    - 用 `AGENTS.md` / `README.md` / `CHANGELOG.md` 已足够承载
- 是否提取/更新 skill:
  - 是
  - 理由:
    - `ScriptedImporter` 输出形态变化后必须 bump version 这条规律跨项目复用价值高,且有 Unity 官方文档支撑

## 本轮续档与归档结果

- 已续档并归档:
  - `archive/task_plan_2026-03-12_232802.md`
  - `archive/notes_2026-03-12_232802.md`
  - `archive/WORKLOG_2026-03-12_232802.md`
  - `archive/notes__splat4d_edge_opacity_2026-03-12_232802.md`
  - `archive/notes__splat4d_single_frame_support_2026-03-12_232802.md`
- 已新建当前文件:
  - `task_plan.md`
  - `notes.md`
  - `WORKLOG.md`
  - `notes__splat4d_edge_opacity.md`
  - `notes__splat4d_single_frame_support.md`
- 当前判断:
  - 不需要新增 `EPIPHANY_LOG.md`
  - 原因是本轮最关键的新发现,已经由现有支线 `EPIPHANY_LOG__splat4d_edge_opacity.md` 覆盖,本轮主要是在做分流与固化

## 外部资料补证

- Unity 官方 `ScriptedImporterAttribute` 文档说明:
  - `version` 用于让导入层检测 importer 的新版本,并触发重新导入
- Unity 官方 `AssetImportContext.SetMainObject` 文档说明:
  - 传入对象必须先通过 `AddObjectToAsset` 加入
  - 如果上下文里有 `GameObject`,而 `SetMainObject` 不是这些 `GameObject` 之一,主对象选择会被忽略或被任意提升
- 这些资料支持把“输出资产形态变化时要 bump version”沉淀成长期规则,而不是只把它当成这仓库的一次偶发经验
