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
