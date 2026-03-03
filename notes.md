# 笔记: RadarScan show/hide noise 不可见

## 现象(用户反馈)

- RadarScan(LiDAR) 模式下,Show/Hide 期间“完全看不到 noise/变化”。
- 之前已经做过多轮 shader 增强,但用户观感仍无变化。

## 已知事实(来自代码与场景序列化)

- `Assets/OutdoorsScene.unity` 中该对象序列化了:
  - `EnableLidarScan=1`
  - `EnableVisibilityAnimation=1`
  - `VisibilityNoiseMode=1`
  - `NoiseStrength=0.354`, `NoiseScale=6`, `NoiseSpeed=0.7`
- C# 侧 `GsplatRenderer/GsplatSequenceRenderer -> GsplatLidarScan.RenderPointCloud` 已传入 noise 参数。
- `Runtime/Shaders/GsplatLidar.shader` 中已存在 `_LidarShowHideNoise*` uniforms,且在 show/hide 期间参与 primary/source/ring/warp。

## 新增证据采集点(本轮新增)

- 在 `Runtime/Lidar/GsplatLidarScan.cs` 增加了 Editor 下的节流诊断日志:
  - 日志 tag: `[Gsplat][LiDAR][ShowHideDiag]`
  - 目的: 把“实际使用的 shader 路径”和“参数是否进入 draw”变成证据。

## 重要怀疑点(待证伪/证实)

- Unity 可能没有在跑我们改的那份 shader,或者实际 draw 的材质不包含这些属性。
- 若日志显示参数非 0 且 shader 路径正确,但仍不可见,则应转向渲染语义层面分析(例如 ZWrite 对噪声观感的抑制)。

## 2026-03-02 17:55:00 +0800: continuous-learning 四文件摘要(因文件超 1000 行续档)

## 四文件摘要(用于决定是否提取 skill)

- 任务目标(task_plan):
  - 过去一段时间连续落地了: Editor 闪烁修复(SRP 多次 beginCameraRendering),Show/Hide 燃烧环动画,RenderStyle 切换动画,以及 RadarScan(LiDAR) 模式与 Show/Hide 的联动与 noise 补齐.
- 关键决定(task_plan):
  - 多次强调 "先证据化,再修复",尤其在 "调参多轮仍无变化" 时必须回到根因调查.
  - LiDAR show/hide 语义对齐 ParticleDots: primary/source mask 叠加规则,以及 ring/trail 的宽度语义.
- 关键发现(notes):
  - Unity Editor(SRP) 同帧可能多次 beginCameraRendering,若 draw 只在 Update 提交会造成闪烁.
  - 当用户感觉 "参数传了但视觉没变" 时,优先怀疑 shader 调用签名/参数消费链路,而不是先调公式.
- 实际变更(WORKLOG):
  - 新增/扩展 LiDAR show/hide overlay uniforms 与 shader 实现.
  - 增加 EditMode 测试锁定参数契约(防止重构丢参).
- 错误与根因(ERRORFIX):
  - 多个历史问题的共性: "工程里存在多个来源/多份副本"(例如 sample copy,外部包目录),导致改了 A 却在跑 B.
- 可复用点候选(1-3 条):
  - 当用户反馈 "完全看不到变化" 时,必须先证明运行时实际使用的资源路径(例如 shader 的 AssetDatabase path),再讨论公式与观感.
  - ripgrep 默认尊重 .gitignore,在 Unity 项目中搜索 Packages/ 下内容时,必要时使用 --no-ignore.
  - ScriptableObject settings 新增字段时,OnValidate 回填能兼容旧资产,但要注意资产未保存时 YAML 不会出现字段,会增加 "引用不落盘" 的排障难度.
- 是否需要固化到 docs/specs: 是.
  - 主题: "Unity 里改 shader/包代码但运行时没变化" 的排障手册(证据链优先).
- 是否提取/更新 skill: 是(建议新增 1 条 self-learning skill).
  - 理由: 这是高频踩坑,且根因往往不是公式,而是 "实际跑的不是你改的那份".

## 2026-03-03 10:06:50 +0800 追加: LidarShowHideWarpPixels max clamp

- 用户反馈: 需要把 `LidarShowHideWarpPixels` 调到更大值,用于更明显的扰动.
- 现状: `ValidateLidarSerializedFields` 会把该值 clamp 到 64,导致“调大但不生效”.
- 决策: 移除 max clamp,仅保留 NaN/Inf/负数防御.

## 2026-03-03 10:14:50 +0800 RadarScan glow 不可见/过快 - 根因对照

- 现象:
  - show glow 看不到.
  - hide glow 太快.
- 关键差异(对照高斯 shader):
  - `Gsplat.shader`:
    - `visibilityAlphaPrimary = max(visible, ring)`.
    - ring 本身会作为 alphaMask 下限,因此 show 阶段即便 visible=0,ring 也能被画出来.
    - hide 的 glowFactor 里包含 `tailInside` afterglow,所以 glow 不会只是一条薄薄的 ring.
  - `GsplatLidar.shader`:
    - show/hide 只用 visible 去乘 showHideMul,ring 没有参与 alphaMask,导致 show 阶段 ring 外侧点直接 early-out.
    - glowFactor 只有 ring,没有 tailInside afterglow,导致 hide glow 很快扫完就没了.
- 计划:
  - 把 LiDAR 的 alphaMask 与 glowFactor 按高斯逻辑补齐,并把 glow 参数改为 LiDAR 专用(独立于高斯).

## 2026-03-03 12:13:09 +0800 追加: RadarScan 独立 NoiseScale/NoiseSpeed

- 用户需求: RadarScan(LiDAR) 想要独立的 NoiseScale/NoiseSpeed,不要复用高斯的全局 NoiseScale/NoiseSpeed.
- 现状: `GsplatRenderer/GsplatSequenceRenderer` 调 `GsplatLidarScan.RenderPointCloud(...)` 时直接把 `NoiseScale/NoiseSpeed` 传入 LiDAR show/hide.
- 决策: 增加两个 LiDAR 专用字段:
  - `LidarShowHideNoiseScale`
  - `LidarShowHideNoiseSpeed`
- 兼容策略:
  - 默认值用 `-1` 表示“复用全局 NoiseScale/NoiseSpeed”.
  - 这样升级后旧项目行为不变,需要独立时再把值改为 >=0 覆盖即可.

## 2026-03-03 12:28:24 +0800 追加: LiDAR ColorMode(动画)按钮无效的根因

- 现象: Inspector 中 `LidarColorMode` 下方的 “Depth(动画) / SplatColor(动画)” 按钮按下后动画不走,看起来无效.
- 根因: `SyncLidarColorBlendTargetFromSerializedMode(animated: true)` 的早退条件写错了.
  - 该函数在 Update/OnValidate 中会被频繁调用,用于“脚本直接改字段”时的自愈同步.
  - 但旧逻辑在 `m_lidarColorAnimating=true` 时不会早退,反而每帧都重新 `BeginLidarColorTransition(...)`.
  - 结果是 `m_lidarColorAnimProgress01` 每帧被重置为 0,动画永远走不完.
- 修复策略: 当 `m_lidarColorAnimTargetBlend01` 已经等于目标 target 时,无论当前是否 animating,都直接早退,避免重复 Begin.

## 2026-03-03 13:22:40 +0800 追加: Show 起始“弹出球形范围” - 尺寸门控思路

- 现象: show 最开始(<1s)像是直接出现一个有尺寸的球形粒子范围.
- 高斯/ParticleDots 的根因:
  - radius 很小时,ring/trail width 仍是常量,导致 band 相对半径过厚.
  - 解决: show 早期对 ring/trail width 做 size ramp(从 0 -> 1),让可见范围从 0 开始长大.
- LiDAR(RadarScan) 的额外根因:
  - jitterBase 对 `maxRadius*0.015` 有下限,在 show 初期可能让边界被噪声“抖出”固定半径.
  - 解决: 让这个下限也乘上 show 的 size ramp(仅在 show 早期),避免“固定半径漏出”.

- 实际落地:
  - `Runtime/Shaders/Gsplat.shader`: show(mode=1) 早期对 ring/trail width 做 size ramp(从 0 -> 1).
  - `Runtime/Shaders/GsplatLidar.shader`:
    - show(mode=1) 增加 size ramp(对齐 splat).
    - progress==0 强制 showHideMul=0,避免首帧漏出.
    - `jitterBase` 的 `maxRadius*0.015` 下限在 show 初期乘上 size ramp,避免固定半径漏出.
    - 同步修正 `EvalLidarShowHideVisibleMask/RingMask` 中的 jitter 下限,避免 sourceMask 评估路径出现同类漏出.
- 自动化回归:
  - EditMode `Gsplat.Tests`: total=50, passed=48, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_show_start_from_zero_2026-03-03_132849_noquit.xml`
