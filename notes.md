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

## 2026-03-07 01:09:26 +0800 新建 OpenSpec change 前的方案固化

- 本轮不是直接实现代码,而是先把“外部 GameObject 参与 RadarScan 扫描”的方案正式建成一个新的 OpenSpec change.
- 命名决定:
  - 使用 `lidar-external-targets`
  - 理由: 与现有字段命名 `LidarExternalTargets` 对齐,也比 `radarscan-external-gameobjects` 更短更稳定.
- 方案边界:
  - 目标能力是给 `GsplatRenderer` / `GsplatSequenceRenderer` 增加 `GameObject[]` 面板字段.
  - 数组中的外部三维模型要与 gsplat 一起参与同一套 LiDAR first return 竞争.
  - 当前不接受“只做视觉叠加”的弱语义方案.
- 命中与几何语义:
  - 使用真实 mesh 参与射线命中,不使用球体/胶囊/盒体近似碰撞体.
  - 静态模型使用 `MeshCollider + sharedMesh`.
  - `SkinnedMeshRenderer` 使用 `BakeMesh()` 烘焙快照后再进入碰撞查询.
- 颜色语义:
  - `Depth` 模式下,外部模型与 gsplat 一样走深度色.
  - `SplatColorSH0` 模式下:
    - gsplat 命中继续取 SH0 基础色.
    - 外部模型命中取 Renderer/Material 的主色.
- 推荐实现路线(后续 design 需要写清楚):
  - 保留现有 gsplat 的 GPU range image 链路.
  - 外部模型走“隔离 `PhysicsScene` + 真实 mesh 代理 + `RaycastCommand` 批量查询”.
  - 最终在 LiDAR draw 阶段逐 cell 比较 gsplat hit 与 external hit 的距离,选择最近者.
- 决策: 增加两个 LiDAR 专用字段:
  - `LidarShowHideNoiseScale`
  - `LidarShowHideNoiseSpeed`
- 兼容策略:
  - 默认值用 `-1` 表示“复用全局 NoiseScale/NoiseSpeed”.
  - 这样升级后旧项目行为不变,需要独立时再把值改为 >=0 覆盖即可.

## 2026-03-08 14:30:39 +0800 追加: RadarScan show/hide noise 与 Unity Value Curl Noise 的关系

- 现象:
  - 用户询问当前雷达扫描粒子的 show/hide 运动 noise,是否就是 Unity VFX Graph 文档里的 `Value Curl Noise`.
- 静态证据:
  - `Runtime/GsplatRenderer.cs`
    - `VisibilityNoiseMode` 默认值是 `GsplatVisibilityNoiseMode.ValueSmoke`.
    - `CurlSmoke` 的注释明确写着: "curl-like 旋涡噪声场(基于 value noise 的梯度/旋度构造)".
  - `Runtime/GsplatRenderer.cs` -> LiDAR draw 提交:
    - `VisibilityNoiseMode` 会直接传给 LiDAR show/hide 路径,不是 LiDAR 自己另起一套 mode.
  - `Runtime/Shaders/Gsplat.hlsl`
    - `GsplatEvalCurlNoise(float3 p)` 的实现是:
      - 用 3 份独立 value noise 作为 vector potential `A(p)=(Ax,Ay,Az)`
      - 再显式计算 `curl(A)=∇×A`
  - `Runtime/Shaders/GsplatLidarPassCore.hlsl`
    - LiDAR 侧 `EvalCurlNoise(float3 p)` 也是同样思路.
    - `mode == 1` 时,这个 curl-like 向量场会参与:
      - show/hide noise 的 domain warp
      - show/hide 粒子屏幕位移方向(`warpDir`)
- 官方文档证据:
  - Unity `Value Curl Noise` 文档写明:
    - 它基于 `Value Noise` 的数学形式,并额外加入 `curl function`.
    - 结果是 turbulent noise,并且是 divergence-free/incompressible.
- 结论:
  - 不是"直接调用 Unity VFX Graph 那个现成 Operator".
  - 但当 `VisibilityNoiseMode = CurlSmoke` 时,算法家族是同一类:
    - 都是 `value noise + curl` 这一思路.
  - 默认情况下并不是它:
    - 默认 mode 是 `ValueSmoke`,不是 `CurlSmoke`.
  - 所以更准确的说法是:
    - "当前实现里有一个自写的、与 Unity `Value Curl Noise` 同家族的 curl-like noise 模式."
    - "只有切到 `CurlSmoke` 时,show/hide 运动 noise 才会接近你贴的那种味道."

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

## 2026-03-03 14:23:28 +0800 追加: RadarScan 独立 ShowDuration/HideDuration

- 需求: RadarScan(LiDAR) 的开关淡入/淡出时长要独立调参.
- 兼容策略: 新字段默认 `-1` 表示复用 `RenderStyleSwitchDurationSeconds`.
- 目标: 不影响高斯/ParticleDots 的 `ShowDuration/HideDuration`(燃烧环显隐).

- 实际落地:
  - Runtime:
    - `GsplatRenderer/GsplatSequenceRenderer` 新增 `LidarShowDuration/LidarHideDuration`.
    - `SetRadarScanEnabled()` 在 durationSeconds<0 时,show/hide 分别优先使用 LiDAR 专用时长(>=0),否则回退到 RenderStyleSwitchDurationSeconds.
    - `ValidateLidarSerializedFields()` 对新字段做 NaN/Inf/负数归一化(-1).
  - Editor:
    - LiDAR 面板的 Timing 区域暴露 `LidarShowDuration/LidarHideDuration` 并提示 "<0 复用 RenderStyleSwitchDurationSeconds".
    - RenderStyle 区域 helpbox/warn 文案同步更新,避免误导.
  - Tests:
    - `GsplatLidarScanTests` 扩展 clamp 断言.
    - 新增反射测试锁定 `ResolveRadarScanVisibilityDurationSeconds` 的 override/fallback 语义.

- 自动化回归:
  - Unity 6000.3.8f1, EditMode `Gsplat.Tests`: total=52, passed=50, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_duration_overrides_2026-03-03_142917_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_duration_overrides_2026-03-03_142917_noquit.log`

## 2026-03-03 14:33:41 +0800 追加: Transition 字段在关闭雷达时也显示

- Editor: LiDAR 面板新增 "Transition" 小节.
  - `EnableLidarScan=false` 时仍显示 `LidarShowDuration/LidarHideDuration`.
- 回归:
  - EditMode `Gsplat.Tests`: total=52, passed=50, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_duration_inspector_2026-03-03_143314_noquit.xml`

## 2026-03-03 15:44:19 +0800: LiDAR 扫过后变黑(亮度语义修正)

- 根因:
  - `GsplatLidar.shader` 旧逻辑让亮度随 `trail` 衰减到接近 0,但 alpha 不随 trail 变化.
  - 在 alpha blend + ZWrite On 下,当亮度很小但仍未 discard 时,会表现为"黑点/黑片".
- 修复思路:
  - 引入未扫到区域的底色强度 `unscannedIntensity`.
  - 每点强度使用 `lerp(unscannedIntensity, scanIntensity, trail)`.
  - 当 `unscannedIntensity=0` 时,行为保持旧版.

## 2026-03-03 16:12:40 +0800: LiDAR 强度按距离衰减(近强远弱)

- 需求:
  - `LidarIntensity` 与 `LidarUnscannedIntensity` 都要随距离衰减.
  - 并且各自有独立的"衰减乘数"可调.

## 2026-03-08 20:42:00 +0800 追加: external mesh 雷达粒子稳定跑到背光面

- 用户最新现场证据:
  - `LidarExternalHitBiasMeters` 怎么调都没有用.
  - 现象不是"轻微埋进表面",而是 external mesh 的雷达粒子整体落在"远离雷达 loc cam"的那一面.
  - 对球体来说,看起来像粒子稳定跑到了阴影面/背光面.

- 本轮重新判断:
  - 这说明问题不只是 draw 端的 render bias 太小.
  - 更像是 external GPU capture 的"最近表面选择"本身出了问题.

- 关键复盘:
  - 上一轮为了绕开平台 depth 语义,把 external capture depth pass 改成:
    - 往颜色 RT 写 `1 / linearDepth`
    - `BlendOp Max`
    - color pass 再通过 `_LidarExternalResolvedDepthTex` 只认最近深度
  - 这条路线理论上可行.
  - 但它隐含依赖了一个更脆弱的前提:
    - `RFloat/ARGBFloat` 颜色 RT 上的 blending 语义必须稳定且正确
    - 否则就可能退化成"最后写入者赢"
  - 一旦退化,闭合 mesh(比如球体)就很容易把 far side/back side 留下来.

- 本轮改法(根因上修):
  - 保留 `Cull Off`,继续避免镜像/负缩放把 front/back 判反.
  - 但把"最近表面"选择重新交给硬件 depth buffer:
    - `DepthCapture`
      - `ZTest LEqual`
      - `ZWrite On`
      - 颜色 RT 直接写最近表面的 `linearDepth`
    - `SurfaceColorCapture`
      - 复用同一个 depth/stencil
      - `ZTest Equal`
      - 只允许与最近深度相等的表面写颜色
  - `CaptureGroup(...)` 第二个 pass 改为只清 color,不清 depth:
    - `ClearRenderTarget(false, true, Color.clear)`
  - `ResolveExternalFrustumHits` 也同步回归为:
    - 直接把 capture texture 当 `linearDepth` 读
    - 然后再 `linearDepth / rayDirSensor.z` 还原 LiDAR ray distance

- 为什么这版更稳:
  - `Cull Off` 解决的是 front/back 判定问题.
  - depth buffer 解决的是"当前像素最近表面到底是谁"的问题.
  - 这两件事分工清晰后,闭合 mesh 的 nearest visible surface 语义更接近 GPU 天然能力.
  - 也避免把正确性押在 `BlendOp Max` 对浮点 RT 的平台实现上.

- 自动化验证:
  - 定向:
    - `Gsplat.Tests.GsplatLidarExternalGpuCaptureTests`
    - total=`8`, passed=`8`, failed=`0`, skipped=`0`
    - XML:
      - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_external_gpu_capture_frontface_2026-03-08.xml`
  - 全包:
    - `Gsplat.Tests`
    - total=`85`, passed=`83`, failed=`0`, skipped=`2`
    - XML:
      - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_full_external_frontface_2026-03-08.xml`

## 2026-03-08 21:23:00 +0800 追加: 功能性证据确认真正根因是 reversed-Z 语义错误

- 新增功能测试:
  - `Tests/Editor/GsplatLidarExternalGpuCaptureTests.cs`
  - `ExternalGpuCaptureDepthPass_CenterPixelMatchesSphereFrontDepth`
- 测试场景:
  - 原点相机朝 +Z
  - 球体中心在 `(0, 0, 5)`
  - 半径 `0.5`
  - 正确前表面深度应为 `4.5`
  - 错误后表面深度应为 `5.5`

- 关键发现:
  - 修复前,中心像素读回是 `5.5`
  - 这直接证明 external capture depth pass 的确把球体 far side 留了下来
  - 所以当时用户看到“粒子都在阴影面”,不是错觉,而是 capture 真错了

- 真正根因:
  - external capture depth pass 虽然已经切回了 hardware depth 路线
  - 但仍然使用了 forward-Z 的固定语义:
    - `ZTest LEqual`
    - `ClearRenderTarget(..., depth=1)`
  - 在 Metal 这类 reversed-Z 平台上,这会把闭合 mesh 的 far side 保留下来

- 本轮修复:
  - `Runtime/Shaders/GsplatLidarExternalCapture.shader`
    - depth pass 的 `ZTest` 改为材质属性:
      - `ZTest [_LidarExternalDepthZTest]`
  - `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`
    - 按 `SystemInfo.usesReversedZBuffer` 设置:
      - forward-Z: `CompareFunction.LessEqual`, clearDepth=`1`
      - reversed-Z: `CompareFunction.GreaterEqual`, clearDepth=`0`
  - color pass 继续保留:
    - 同一 depth/stencil
    - `ZTest Equal`

- 修复后验证:
  - 同一功能测试已通过:
    - XML:
      - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_external_capture_functional_2026-03-08_v2.xml`
  - external 相关整组:
    - total=`9`, passed=`9`, failed=`0`, skipped=`0`
    - XML:
      - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_external_gpu_capture_group_2026-03-08_v3.xml`
  - 全包:
    - total=`86`, passed=`85`, failed=`0`, skipped=`1`
    - XML:
      - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_full_external_fix_2026-03-08_v4.xml`

## 2026-03-08 21:36:00 +0800 追加: 保留 bias 能力,但默认值收回到 0

- 用户决策:
  - 不删除 `LidarExternalHitBiasMeters`
  - 但把默认值与 NaN/Inf fallback 从 `0.01` 收回到 `0`

- 理由:
  - 现在真正解决“背面”问题的是 reversed-Z capture 修复
  - `LidarExternalHitBiasMeters` 更适合作为可选的 render-only 微调
  - 不应该继续以一个非零默认值,把它伪装成主修复的一部分

- 同步调整点:
  - `GsplatRenderer/GsplatSequenceRenderer`
    - 序列化默认值改为 `0`
    - sanitize fallback 改为 `0`
  - `GsplatLidarScan.RenderPointCloud(...)`
    - MPB fallback 改为 `0`
  - `GsplatLidar.shader` / `GsplatLidarAlphaToCoverage.shader`
    - hidden property 默认值改为 `0`
  - Inspector / README
    - 文案改为“默认关闭,按需再开”
  - `GsplatLidarScanTests`
    - 默认值断言从 `0.01` 改为 `0`
- 落地公式(简单且稳定):
  - `atten(dist)=1/(1+dist*decay)`
  - `decay=0` 时,atten 恒为 1,保持旧行为.
- 最终强度语义:
  - `scanIntensity=LidarIntensity*atten(range, LidarIntensityDistanceDecay)`
  - `unscannedIntensity=LidarUnscannedIntensity*atten(range, LidarUnscannedIntensityDistanceDecay)`
  - `intensity=lerp(unscannedIntensity, scanIntensity, trail)`

## 2026-03-03 18:16:30 +0800 追加: LiDAR 距离衰减增加 Exponential 模式(可切换)

- 用户需求: 增加"指数衰减"选项,可切换衰减曲线形态.
- 设计决定(兼容优先):
  - 新增模式字段 `LidarIntensityDistanceDecayMode`(`Reciprocal` / `Exponential`),默认 `Reciprocal`.

## 2026-03-07 12:36:20 +0800 追加: external target Play 模式专用隐藏 收尾前审读结论

- OpenSpec `lidar-external-targets` 已把本轮需求写进 proposal / design / spec / tasks.
  - 新增语义:
    - `ForceRenderingOffInPlayMode`
    - 编辑器非 Play 保持普通 mesh 可见
    - Play 模式自动切成 scan-only
- 代码链路当前已对齐:
  - `Runtime/GsplatUtils.cs`
    - `GsplatLidarExternalTargetVisibilityMode` 已扩成三态.
  - `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs`
    - 可见性决策集中到 `ShouldForceSourceRendererOff(mode, isPlaying)`.
    - `ForceRenderingOffInPlayMode` 只在 `isPlaying=true` 返回 true.
  - `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`
    - tooltip 与非法值回退逻辑已接受第三态.
  - `Editor/*`
    - helpbox 已同步三态说明.
- `Tests/Editor/GsplatLidarScanTests.cs`
    - 已新增反射测试,直接锁定三态决策语义.

## 2026-03-08 14:06:30 +0800 追加: RadarScan 粒子 AA 为何“开了 MSAA 也不明显”

- 第二层根因已经确认:
  - 即使 `AnalyticCoverage` 换成像素尺度 coverage,即使 A2C shell 改成 coverage-first,
    如果点片几何仍严格卡在原始 footprint 内,AA 也只能“往里软”,无法在原边界外长出真正的 fringe.
  - 对 `LidarPointRadiusPixels=2` 这类小点来说,这种内收式软化非常容易肉眼不明显.

## 2026-03-08 20:02:50 +0800 追加: external mesh 粒子显示在模型后面 的根因与修复

- 现象澄清:
  - 用户补充"是模型背后",重点不是"命中了背面三角形",而是"粒子视觉上落在可见 mesh 表面后方".
- 根因复核:
  - `Runtime/Shaders/GsplatLidarExternalCapture.shader` 改成 `Cull Off` 只能解决 capture 端 front/back 选择错误.
  - 但 LiDAR draw 端此前仍直接用 external hit 的真实 `range` 重建 `worldPos`.
  - 当点位与普通 mesh 表面过贴,再叠加 depth 精度/栅格化差异时,粒子会被源 mesh 挡在后面.
- 修复策略:
  - 新增 render-only 参数 `LidarExternalHitBiasMeters`(默认 `0.01f`).
  - 只在 `useExternalHit` 路径上使用:
    - `renderRange = max(range - bias, 0)`
    - `worldPos` 用 `renderRange` 重建
  - 保持以下语义不变:
    - first return 竞争仍用真实 external/splat range
    - `Depth` 颜色映射仍用真实 `range`
    - 距离衰减仍用真实 `range`
- 这样做的含义:
  - 我们只修正"最终显示深度关系",不篡改扫描物理语义.
  - 这比去改 external capture depth 或 merge 竞争逻辑更稳.
- 验证与附带结论:
  - `Gsplat.Tests` 全包回归通过(83 total / 81 passed / 0 failed / 2 skipped).
  - Unity 6000.3.8f1 下,命令行跑 TestRunner 这版要避免带 `-quit`,否则可能刷新工程后直接退出且不写 XML.
- 本轮修正:
  - `Runtime/Shaders/GsplatLidarPassCore.hlsl`
    - 非 `LegacySoftEdge` 模式统一增加 `aaFringePadPx = 1.0`.
    - 顶点阶段用 `paddedRadiusPx` 扩大点片几何.
    - `uv` 同步按比例放大,让 fragment 能看到原边界外的 fringe 区域.
    - fragment 用 `outerLimit` 截断真正的外扩范围.
  - `AlphaToCoverage` 路线:
    - 继续保留 coverage-first pass 语义.
    - 但 alpha 现在终于有了“原边界外的 coverage 空间”可用,不再只是同 footprint 内的透明软化.
- 验证结果:
  - Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`82`, passed=`80`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_particle_aa_v3_2026-03-08_noquit.xml`

## 2026-03-08 14:33:20 +0800 追加: 把外扩 fringe 从写死常数升级为调参项

- 用户决定: 不再把 `aaFringePadPx` 固定死在 shader 里,而是要在 Inspector 里可调.
- 最终命名:
  - 运行时字段: `LidarParticleAAFringePixels`
  - shader property: `_LidarParticleAAFringePixels`
- 设计取舍:
  - 默认值保持 `1.0`,避免升级后旧场景观感突然变粗.
  - `0` 允许用户主动关闭“原边界外扩”,回到几乎只在原 footprint 内部软化的状态.
  - 负值不保留特殊语义,统一 clamp 到 `0`,避免“半合法”的隐藏模式增加理解成本.
- 辅助改进:
  - `TryLogParticleAntialiasingDiagnostics(...)` 现在会一起记录 `fringePx`,便于用户排查“面板改了是否真的传到 draw”.
- 验证:
  - Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`82`, passed=`80`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_particle_aa_fringe_param_2026-03-08.xml`

## 2026-03-08 19:39:20 +0800 追加: external mesh 雷达粒子贴到背面 - 根因收敛

- 用户现象:
  - “雷达扫描粒子 mesh 上的粒子现在都在 mesh 背面了,之前是正面的.”
- 排查结论:
  - 刚刚几轮 `AnalyticCoverage / A2C / fringe` 改动都没有碰 external mesh capture 链.
  - 真正高风险点在 `Runtime/Shaders/GsplatLidarExternalCapture.shader`:
    - 它一直在用 `Cull Back`.
    - 而这份 shader 又运行在手动 `CommandBuffer.SetViewProjectionMatrices(view, projection)` 的离屏 capture 路径里.
  - 在这种路径下,如果遇到 RT flip、投影手性、负缩放、镜像 transform 等组合,
    front/back 判定更容易漂移.
  - 一旦漂移,`Cull Back` 就会稳定保留错误一侧,于是 resolve 出来的 external hit 会落到 mesh 背面.
- 修复策略:
  - 不继续依赖面剔除来决定“正面”.
  - 改成 `Cull Off`,让 depth buffer 自己选最近可见表面.
  - 对闭合 mesh,这比依赖 winding/cull 更稳健.
- 验证:
  - 新增 `ExternalGpuCaptureShader_UsesCullOffToPreferNearestVisibleSurface` 回归测试.
  - Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`83`, passed=`81`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_capture_culloff_v2_2026-03-08.xml`

## 2026-03-08 12:46:01 +0800 追加: RadarScan 粒子抗锯齿模式落地结论

### 本轮最终设计

- LiDAR 粒子抗锯齿模式正式固定为 4 档:
  - `LegacySoftEdge`
  - `AnalyticCoverage`
  - `AlphaToCoverage`
  - `AnalyticCoveragePlusAlphaToCoverage`
- 兼容策略保持保守:
  - 默认值继续是 `LegacySoftEdge`
  - Inspector / README 推荐值写成 `AnalyticCoverage`
- A2C 语义不做假切换:
  - 用独立 shader shell `Runtime/Shaders/GsplatLidarAlphaToCoverage.shader`
  - pass 状态通过 `AlphaToMask On` 固定在 shell 层
  - 主体 fragment / vertex 逻辑共用 `Runtime/Shaders/GsplatLidarPassCore.hlsl`

### 关键实现结论

- `AnalyticCoverage` 已接到 LiDAR 主 fragment:
  - 保留当前方片距离场
  - 用 `fwidth(signedEdge)` 驱动 edge coverage
  - 不改颜色、show/hide、glow、external hit 竞争语义
- Runtime 已补齐模式解析与 fallback:
  - `GsplatUtils.ResolveEffectiveLidarParticleAntialiasingMode(...)`
  - 无有效 MSAA 时:
    - `AlphaToCoverage -> AnalyticCoverage`
    - `AnalyticCoveragePlusAlphaToCoverage -> AnalyticCoverage`
- `GsplatLidarScan.RenderPointCloud(...)` 已按 effective mode 选择普通 LiDAR material 或 A2C material
- Inspector 与运行时诊断都已明确:
  - A2C 依赖 MSAA
  - 无 MSAA 时会 fallback 到 `AnalyticCoverage`

### 这轮额外踩坑

- 首轮 Unity EditMode 回归不是 AA 逻辑失败,而是测试资源释放顺序问题.
- 触发条件:
  - `Camera.targetTexture` 还绑着 RT 时直接 `DestroyImmediate(targetTexture)`
- Unity 会记 error log:
  - `Releasing render texture that is set as Camera.targetTexture!`
- 正确清理顺序:
  - 先 `camera.targetTexture = null`
  - 再销毁 RT
  - 最后销毁 Camera GameObject

### 自动化验证证据

- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`82`, passed=`80`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_particle_aa_2026-03-08_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_particle_aa_2026-03-08_noquit.log`
- OpenSpec:
  - `openspec instructions apply --change "radarscan-particle-antialiasing-modes" --json`
  - progress=`19 / 19`
  - state=`all_done`

## 2026-03-08 12:58:00 +0800 追加: AA 模式“看起来没区别”的根因判断

### 用户新反馈

- `AnalyticCoverage`
- `AlphaToCoverage`
- 肉眼看起来都和 `LegacySoftEdge` 几乎没差别.

### 代码链路检查结论

- runtime 模式切换链路本身是通的:
  - `GsplatRenderer/GsplatSequenceRenderer` 会把 requested / effective mode 传进 `GsplatLidarScan.RenderPointCloud(...)`
  - `GsplatLidarScan` 会:
    - 按 effective mode 选择普通 LiDAR material 或 `LidarAlphaToCoverageMaterial`
    - 把 `_LidarParticleAAAnalyticCoverage` 设为 0/1
- shared pass core 里的 shader 公式也确实不是“同一段代码”:
  - `LegacySoftEdge`: 固定 `kSquareFeather = 0.10`
  - `AnalyticCoverage`: `fwidth(signedEdge)` 驱动的 coverage
- 也就是说,从代码结构看,问题不像是“模式没切上去”,更像是“切上去以后,视觉差异被当前渲染语义吃掉了”.

### 当前最强根因假设

#### 1) `AnalyticCoverage` 只平滑了颜色 alpha,但没有平滑 depth 轮廓

- 当前 LiDAR pass 仍然是:
  - `ZWrite On`
  - `Blend SrcAlpha OneMinusSrcAlpha`
- 在这种语义下:
  - 颜色边缘虽然更软了
  - 但深度写入仍是二值的
  - 边缘像素一旦没有 `discard`,就会完整挡住背景与后面的点
- 结果:
  - 在密集点云和 `ZWrite On` 路径里,用户肉眼更容易看到的是“深度轮廓/遮挡感”,不是那一层颜色 alpha 的差别
  - 因此 `AnalyticCoverage` 可能数学上生效了,但观感上仍然像“没区别”

#### 2) `AlphaToCoverage` 当前的 pass 设计方向不对

- 我查了 Unity 官方文档:
  - `AlphaToMask` / alpha-to-coverage 主要是为了改善 **alpha test / cutout** 的边缘
  - 更适合“多数像素是完全透明或完全不透明,只有很薄的半透明过渡区”的材质,如草、叶子、铁丝网
  - 官方文档还明确说这类 shader 往往配合 `AlphaTest` / `TransparentCutout` queue
- 但我们当前 LiDAR A2C shell 仍然是:
  - `Queue=Transparent`
  - `Blend SrcAlpha OneMinusSrcAlpha`
  - `ZWrite On`
  - 只是额外加了 `AlphaToMask On`
- 这说明当前 A2C shell 更像是“把 A2C 状态叠在半透明粒子 pass 上”,而不是一个真正围绕 coverage 设计的 pass
- 所以它看不出明显提升,很可能是设计语义本身就不匹配

### 这意味着什么

- 这次用户反馈更像是在指出一个真实设计问题:
  - `AnalyticCoverage` 在当前 `ZWrite On` 点云路径里,收益偏温和
  - `AlphaToCoverage` 当前实现方式,不太像 Unity 官方建议的 cutout / coverage 使用方式
- 如果要让 A2C 的收益真正“肉眼可见”,大概率要重新设计 LiDAR A2C shell 的 blend / queue / alpha 语义
- 这已经不是简单调一个 `fwidth` 系数就能彻底解决的事

### 参考资料

- Unity Manual: ShaderLab `AlphaToMask`
  - https://docs.unity3d.com/cn/2021.3/Manual/SL-AlphaToMask.html
- Unity Manual: ShaderLab Blending / Alpha-to-coverage
  - https://docs.unity3d.com/cn/2018.4/Manual/SL-Blend.html

## 2026-03-08 13:18:00 +0800 追加: 推荐路线已落地(A2C 改为 coverage-first)

### 本轮修正

- `AnalyticCoverage`
  - 不再直接在归一化 uv 上做 `fwidth`.
  - 改为先把 `signedEdge` 换算到像素尺度:
    - `pointRadiusPx = max(_LidarPointRadiusPixels, 1.0)`
    - `signedEdgePx = signedEdge * pointRadiusPx`
    - `analyticWidthPx = fwidth(signedEdgePx)`
  - 这样 2px 小点也能拿到更明显的 AA 过渡带.
- `AlphaToCoverage`
  - 不再沿用普通 LiDAR 的透明混合 pass.
  - A2C shell 改成 coverage-first:
    - `RenderType = TransparentCutout`
    - `Blend One Zero`
    - `AlphaToMask On`
    - 保留 `ZWrite On`
  - `Queue` 仍暂时保留 `Transparent`,尽量减少与现有 LiDAR / splat 提交顺序的连锁变化.

### 关键判断

- 这次不是“参数没切到 shader”.
- 而是用户反馈逼出了一个更本质的问题:
  - 原来的 A2C shell 只是“透明混合 pass + AlphaToMask On”,很难体现 sample coverage 的真实收益.
- 现在改完后:
  - `AnalyticCoverage` 的视觉差异应该更容易看出来
  - `AlphaToCoverage` 的边缘语义会更接近 cutout / sample coverage,不再是假装透明混合也能得到完整 A2C 收益

### 自动化验证

- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`82`, passed=`80`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_particle_aa_v2_2026-03-08_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_particle_aa_v2_2026-03-08_noquit.log`

## 2026-03-08 10:50:00 +0800: 归档前 continuous-learning 四文件摘要

## 四文件摘要(用于决定是否可以归档)

- 任务目标(task_plan.md):
  - 最近一轮主线是完成 `lidar-camera-frustum-external-gpu-scan` 的实现闭环,把外部 mesh 的扫描从 CPU 辅助路径推进到基于相机视锥的 GPU capture 路线,并把 `LidarOrigin` 正式替换为相机位姿语义。
- 关键决定(task_plan.md):
  - 四个约束被写死并落实到实现/规格:
    - 不改变现有视觉形态,不靠减少 `LidarAzimuthBins=2048` 之类手段换性能。
    - 扫描口径只取 frustum camera 的口径,不再做 360 度。
    - static mesh 与 skinned/dynamic mesh 分组处理,静态尽量只重抓一次,动态按独立频率更新。
    - external mesh 扫描优先走 GPU 路线,并且写回 LiDAR ray distance 语义,不能写 raw hardware depth。
- 关键发现(notes.md / WORKLOG.md):
  - external frustum capture 真正决定性能的不是单纯“有没有 GPU”,而是有没有把 external 输入拆成 static / dynamic 两组并做 invalidation/cadence。
  - frustum capture resolve 阶段如果直接回写 camera depth,会破坏 LiDAR 后续以射线距离为核心的统一语义,必须在 compute 中还原成 `depthSq`。
  - external tick 不能继续绑在 `LidarUpdateHz`,否则会把 static reuse 和 dynamic cadence 一起拖回旧节奏。
- 实际变更(WORKLOG.md):
  - 已新增 `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs` 及对应 shader/compute/runtime 接线。
  - 已补 `Tests/Editor/GsplatLidarExternalGpuCaptureTests.cs`。
  - 已同步 `README.md`、`CHANGELOG.md`、OpenSpec `tasks.md`。
  - EditMode 回归:
    - `Gsplat.Tests.GsplatLidar`: 38 passed, 0 failed, 0 skipped。
    - `Gsplat.Tests`: 77 passed, 0 failed, 2 skipped。
- 错误与根因(ERRORFIX.md):
  - 当前 `ERRORFIX.md` 没有这条 change 的新增错误项。
  - 说明这轮主要是能力扩展与性能路线调整,不是一次典型 bugfix。
- 可复用点候选:
  - 当扫描语义已经切到 camera frustum 时, sensor pose / projection / capture layout / renderer signature 必须被看作同一套缓存键,否则 static reuse 会产生“看起来偶发错帧”的脏缓存问题。
  - 需要兼顾性能与视觉一致性时,优先拆“静态复用”和“动态频率”,而不是先砍分辨率或砍粒子数量。
  - GPU capture 的输出若要回到既有 LiDAR 管线,最好在 resolve 阶段统一成原管线使用的距离语义,避免后续 shader / draw / tests 到处做分支兼容。
- 是否需要固化到 docs/specs: 否。
  - 理由: 本轮长期知识已经同步进 `README.md` 与 OpenSpec `design/spec/tasks`,暂时没有额外需要新开长期文档的内容。
- 是否提取/更新 skill: 否。
  - 理由: 这轮经验更偏项目内具体实现与架构收敛,目前写进现有 OpenSpec 和 README 已足够。

## 2026-03-08 11:20:00 +0800: RadarScan 粒子抗锯齿需求收敛

- 变更脚手架:
  - 已创建 OpenSpec change:
    - `openspec/changes/radarscan-particle-antialiasing-modes/`
    - schema: `spec-driven`
- 当前 RadarScan 粒子渲染事实:
  - 主链路在 `Runtime/Lidar/GsplatLidarScan.cs -> Runtime/Shaders/GsplatLidar.shader`.
  - 现在的 LiDAR 粒子是屏幕空间方片+软边:
    - vertex 里按 `LidarPointRadiusPixels` 扩四个 corner.
    - fragment 里用 `Chebyshev distance + smoothstep` 做 `alphaShape`.
  - 当前 soft edge 是固定 feather:
    - `kSquareFeather = 0.10`
    - 这在 2px 一类的小点半径下,边缘 coverage 仍偏硬,容易看到锯齿/闪烁.
  - pass 当前是:
    - `ZWrite On`
    - `Blend SrcAlpha OneMinusSrcAlpha`
    - 这说明它既要保遮挡稳定,又会受 alpha coverage 的边缘质量影响。
- 从主 gsplat 链路得到的启发:
  - `Runtime/Shaders/Gsplat.hlsl` 已有 `GSPLAT_AA` 语义,说明仓库并不排斥 shader 侧 analytic AA 路线。
  - 因此 RadarScan 也更适合优先补“shader coverage AA”,而不是直接绑死到某个全屏后处理。
- Unity 侧约束(已查官方文档):
  - `AlphaToMask` 是 shader pass 状态,语法是 `AlphaToMask On/Off`.
  - 官方明确说明: 没有 MSAA 时结果不可预测。
  - 这意味着 `AlphaToCoverage` 可以做成一个可选项,但不应作为默认方案,也不能假设所有相机/平台都稳定可用。
- 适合本项目 RadarScan 的 AA 模式判断:
  - 推荐 1: `Off`
    - 保持当前行为,用于对照与最低成本路径.
  - 推荐 2: `AnalyticCoverage` 或同义命名
    - 核心: 用 `fwidth` / 屏幕导数驱动边缘 coverage,替代当前固定 feather.
    - 优点: 不依赖相机后处理,不要求 MSAA,最符合当前 RadarScan 的本地 shader 结构.
    - 适合做默认值。
  - 推荐 3: `AlphaToCoverage`
    - 仅在相机/pipeline 开启 MSAA 时有意义.
    - 优点: 对硬边小点的几何锯齿压制更强,同时保留 ZWrite 语义.
    - 风险: 平台/管线差异更大,没有 MSAA 时不可作为稳定主方案.
  - 推荐 4: `AnalyticCoveragePlusAlphaToCoverage`
    - 把 2 和 3 叠加,作为高质量模式.
    - 预期是最好观感,但也最依赖 MSAA 环境。
- 当前不建议纳入本轮第一版的模式:
  - `TAA`
    - RadarScan 本身扫描头在移动,点又小,极容易引入拖影/重影.
    - 且这是 camera/pipeline 级能力,不适合由这个 package 内的 LiDAR shader 自行接管.
  - `SMAA/FXAA` 这类全屏后处理
    - 也属于 camera/pipeline 级接入,需要额外 renderer feature / custom pass / post stack 对接.
    - 对“只想让 RadarScan 点更顺滑”的目标来说,侵入太大.
- 一句话结论:
  - 正确方向不是“给 RadarScan 硬塞所有常见 AA 名词”,而是提供:
    - 一个稳定默认的 shader 内 coverage AA
    - 一个依赖 MSAA 的高质量选项
    - 再明确排除 TAA/SMAA 这类超出组件职责边界的全屏方案。

## 2026-03-08 00:23:12 +0800 追加: frustum active-cell 主链的关键设计结论

- 这轮 `2.1 ~ 2.5` 的关键不是“再加字段”,而是把 LiDAR 的 active layout 从裸参数提升成统一语义对象.
  - 最终做法:
    - 在 `Runtime/Lidar/GsplatLidarScan.cs` 内引入 `GsplatLidarLayout`
    - 同时承载:
      - `ActiveAzimuthBins`
      - `ActiveBeamCount`
      - `AzimuthMin/MaxRad`
      - `BeamMin/MaxRad`
- compute / draw / CPU external fallback 都必须吃同一份 layout.
  - 否则只改 buffer count,没改角域,会出现:
    - compute 命中仍按旧 360 落 bin
    - draw 方向重建和 CPU raycast 方向不一致
    - external helper 命中和 gsplat 命中不在同一条离散射线上
- frustum 角域不能只看 4 个 corner.
  - 原因:
    - 透视相机的最大水平角通常在 left/right-center
    - 最大垂直角通常在 top/bottom-center

## 2026-03-08 00:42:50 +0800 追加: external GPU capture 勘探结论

- 已确认:
  - 仓库里没有现成的 runtime external offscreen GPU capture 基础设施.
  - `Runtime/` 下没有可直接复用的 `RenderTexture + SetRenderTarget + DrawRenderer/DrawMesh + override material` external capture 实现.
- 现成可复用的地基主要有 5 块:
  - `Runtime/Lidar/GsplatLidarScan.cs`
    - 已拥有 external 结果最终落点:
      - `ExternalRangeSqBitsBuffer`
      - `ExternalBaseColorBuffer`
    - 已有稳定的 `CommandBuffer + ComputeShader + Graphics.ExecuteCommandBuffer` 调度模板.
  - `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`
    - `TryGetEffectiveLidarRuntimeContext(...)` 已把 frustum camera -> sensor frame 契约接好.
  - `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs`
    - 已有 external renderer 收集.
    - 已区分 `MeshRenderer` / `SkinnedMeshRenderer`.
    - 已有材质主色语义:
      - `_BaseColor`
      - `_Color`
    - 已有 visibility 三态:
      - `KeepVisible`
      - `ForceRenderingOff`
      - `ForceRenderingOffInPlayMode`
  - `Runtime/Shaders/Gsplat.compute`
    - 已定义当前 LiDAR `cell` / `depthSq` 语义.
    - 新 external resolve kernel 最好直接沿用这套语义,不要再造一套 depth 定义.
  - `Runtime/Shaders/GsplatLidar.shader`
    - 最终 draw 已经消费:
      - `_LidarExternalRangeSqBits`
      - `_LidarExternalBaseColor`
    - 只要新 GPU 路线最终写回这两个 buffer,下游 draw 基本不用重写.
- 设计判断:
  - `3.1 ~ 3.7` 最稳的落点不是“继续给旧 CPU helper 打补丁”.
  - 更合理的是:
    - 新增独立 `GsplatLidarExternalGpuCapture`
    - 旧 `GsplatLidarExternalTargetHelper` 保留给 surround360 / fallback / debug
    - renderer 只负责在 LiDAR tick 时决定当前该走哪一条 external hit 生产路径

## 2026-03-08 01:06:55 +0800 追加: `3.1 ~ 3.7` 实现后的关键语义

- external GPU capture 最终采用了“显式 render list + override material + command buffer draw”.
  - 没有使用 hidden camera.
  - 也没有依赖 source renderer 当前在 scene 里是否可见.
  - draw 入口使用 `CommandBuffer.DrawMesh(...)`,因此 `ForceRenderingOff / ForceRenderingOffInPlayMode` 不会把 capture 链一起关掉.
- capture shader 分成两条 pass:
  - depth pass:
    - 输出正值 linear view depth
    - 仍通过独立 depth-stencil RT 做最近面裁决
  - surfaceColor pass:
    - 只输出 `_BaseColor` / `_Color`
    - 不读贴图
    - 不读光照
    - 不读后处理后的 scene color
- external resolve kernel 没有直接拿 raw camera depth 去和 gsplat `depthSq` 比.
  - 当前做法:
    - 每个 LiDAR cell 一条线程
    - 用当前 cell-center ray 重新投影到 capture RT
    - 从 linear view depth 还原“沿当前 ray 的 depth”
    - 再写成 LiDAR `depthSq`
  - 这个语义与当前 gsplat LiDAR 主链保持一致,因此最终 draw 仍能复用现有 `_LidarExternalRangeSqBits / _LidarExternalBaseColor`.
- 当前 renderer 接线策略:
  - frustum + GPU 资源可用:
    - 优先走 `GsplatLidarExternalGpuCapture`
  - 否则:
    - 回退 `GsplatLidarExternalTargetHelper`
  - 这意味着 `4.5` 的 fallback 主体其实已经有地基了,后面主要是把切换条件写死并做测试.
    - 只看角点会系统性低估 FOV,进而低估 active counts
  - 最终做法:
    - 用 `ViewportToWorldPoint` 采样:
      - 4 corners
      - 4 edge-centers
- frustum 模式下的扫描头年龄不能直接按 active bins 归一化.
  - 否则 aperture 变窄后,扫描头会在同样 `RotationHz` 下“看起来更快”.
  - 最终做法:
    - draw shader 改成按真实 360 方位角算 age:
      - `headAz = frac(time * RotationHz) * 2pi - pi`
      - `cellAz` 来自 layout 的 azimuth range
      - `age01 = wrappedDeltaAz / 2pi`
- frustum camera 的无效条件目前采用“明确禁用新路径”,而不是偷偷回退到 360.
  - 已覆盖:
    - camera 丢失
    - orthographic camera
    - `fieldOfView` 非法
    - `pixelRect` 与 `aspect` 同时无效
- `pixelRect` 不能作为唯一有效性来源.
  - 原因:
    - batchmode / 某些非实际输出相机下,`pixelRect` 可能是 0
    - 但 frustum projection 仍然可能可计算
  - 最终策略:
    - `pixelRect` 无效时,只要 `camera.aspect` 仍有效,layout 仍可继续构建
- 当前还缺 fresh evidence:
  - 需要重新跑 Unity EditMode 定向测试与全量测试.
  - 测试通过后,再勾 OpenSpec `7.1 ~ 7.5`.
  - 继续复用既有两个衰减乘数:
    - `LidarIntensityDistanceDecay`
    - `LidarUnscannedIntensityDistanceDecay`
  - 只改变 atten 函数的形态,不改变 decay 参数语义.
- shader 端按每点 `range` 计算距离衰减:
  - `Reciprocal`: `atten(dist)=1/(1+dist*decay)`
  - `Exponential`: `atten(dist)=exp(-dist*decay)`(实现上使用 `exp2(-dist*decay/ln(2))`)
- 防御:
  - `ValidateLidarSerializedFields` 会把非法 enum 回退到 `Reciprocal`,避免序列化坏值导致意外落入指数模式.

## 2026-03-07 01:39:43 +0800 实现前阅读: lidar-external-targets 第 1 组任务

- `GsplatRenderer` 与 `GsplatSequenceRenderer` 的 LiDAR 字段布局当前已经高度镜像.
  - 这意味着 `LidarExternalTargets` 最适合同时插在 `LidarOrigin` 附近,作为 LiDAR 输入源配置的一部分.
  - 两个 Inspector 也已经各自有独立的 LiDAR 折叠区,适合直接加一个 `External Targets` 小节.
- 当前 `ValidateLidarSerializedFields()` 的职责很明确:
  - 只做“字段级别”的防御和归一化.
  - 不依赖 GPU 资源,也不依赖 Unity 生命周期句柄.
  - 因此外部目标数组的自愈逻辑也应该优先放在这里,这样单测最容易覆盖.
- 第 1 组任务的自愈策略,我准备采用“最稳妥,最少惊讶”的版本:

## 2026-03-07 02:03:40 +0800 实现前阅读: lidar-external-targets 第 2/3 组任务接口盘点

- `GsplatLidarExternalTargetHelper` 当前雏形已经覆盖了本次设计要求的大部分骨架:
  - 递归收集 `MeshRenderer + MeshFilter` / `SkinnedMeshRenderer`
  - 隔离 `PhysicsScene`
  - static mesh 的 `MeshCollider(sharedMesh)` 代理
  - skinned mesh 的 `BakeMesh()` + `MeshCollider` 刷新
  - `RaycastCommand` 批量查询
  - `triangleIndex -> submesh -> material` 映射缓存
  - `_BaseColor` / `_Color` 主色提取
- `GsplatLidarScan` 当前已经把 external hit buffer 链路补到了比较正确的位置:
  - `RangeImageValid` 已要求 external buffers 也有效
  - `EnsureRangeImageBuffers()` 会同时创建 external buffers
  - `BindBuffersForRender()` 已把 external buffers 当成 Metal 必绑资源
  - shader 也已经开始按 cell 比较 gsplat / external 的最近 hit
- 当前最缺的不是思路,而是“接线”和“收尾”:
  - `GsplatRenderer` / `GsplatSequenceRenderer` 还没有真正持有并调用 `GsplatLidarExternalTargetHelper`
  - LiDAR 关闭时还没有同步释放 helper
  - show/hide / visibility 的 bounds 仍然只看 gsplat 自身 bounds
- 当前半成品里最值得优先检查的点:
  - `GsplatLidarScan` 里 external clear scratch 常量名有一处大小写不统一,高概率会直接导致编译失败
  - helper 的 proxy scene 清理在 EditMode 下最好走更显式的关闭路径,避免隐藏 scene 残留
  - helper 目前没有 `.meta`,如果最终要把它作为 Unity 资产稳定纳入包,需要一并补上
- bounds 扩展的最佳落点已经比较明确:
  - 新增 `ResolveVisibilityLocalBoundsForThisFrame()`
  - 统一喂给:
    - `PushVisibilityUniformsForThisFrame(...)`
    - `CalcVisibilityExpandedRenderBounds(...)`
    - `BuildLidarShowHideOverlayForThisFrame(...)`
  - 再覆盖 `OnEnable` / camera callback / Update render 里仍直接传 `GsplatAsset.Bounds` / `SequenceAsset.UnionBounds` 的调用点

## 2026-03-07 02:19:33 +0800 实现后结论: 本轮真正踩到的两个关键坑

- 坑 1: 只改 runtime asmdef 不足以拿到 Physics 类型.
  - 现象: Unity 编译报 `PhysicsScene` / `MeshCollider` / `RaycastCommand` 都找不到.
  - 根因: `_tmp_gsplat_pkgtests` 项目里没有启用内建包 `com.unity.modules.physics`.
  - 正确修法: 在包级 `package.json` 里声明 `com.unity.modules.physics: 1.0.0`.
    - 这比只在 `.asmdef` 里硬塞 `UnityEngine.PhysicsModule` 更符合 UPM 依赖语义.
- 坑 2: `SceneManager.CreateScene(..., LocalPhysicsMode.Physics3D)` 在 Editor 非 Play 模式下不能直接用.
  - 现象: physics 集成测试最初失败,抛 `InvalidOperationException`,提示 EditMode 请改用 `EditorSceneManager.NewScene()`.
  - 本轮采用的稳态修法:
    - EditMode: `EditorSceneManager.NewPreviewScene()`
    - PlayMode: `SceneManager.CreateScene(..., LocalPhysicsMode.Physics3D)`
    - 清理时按 preview scene / 普通 scene 分支关闭
  - 结果: helper 在 EditMode tests 下可以正常创建 proxy scene,并通过真实 mesh 命中测试.
- 本轮测试策略的有效经验:
  - `-runTests` 在这套 Unity 版本里即便把 `-quit` 放在最后,仍可能出现“进程成功退出但 XML 没落盘”.
  - 仓库文档提示是对的: 出现这种情况时,直接去掉 `-quit`,让 TestRunner 自己收尾.
  - `null` 数组归一化为长度 0 数组.
  - 清理数组中的空引用项,避免后续 helper 每帧重复处理无效槽位.
  - 暂时不在字段层做去重或层级裁剪,保持用户手工排列顺序.
- 当前 LiDAR draw 路径仍然只接 `m_renderer.ColorBuffer` 和 show/hide 参数.
  - 所以第 1 组任务可以只改 Runtime API + Inspector + tests.
  - 不需要提前改 shader 或 `GsplatLidarScan.RenderPointCloud(...)` 签名.

## 2026-03-07 02:31:40 +0800 追加设计: external target 普通 mesh 可见性控制

- 当前 helper 对“是否参与扫描”的判断依赖 `renderer.enabled` 与 `gameObject.activeInHierarchy`.
  - 证据:
    - `GsplatLidarExternalTargetHelper.IsSupportedRenderer(...)`
    - `SyncProxyEntries(...)` 内也会再次跳过 disabled renderer
  - 结论:
    - 不能用 `renderer.enabled=false` 或 `SetActive(false)` 来实现“只显示雷达粒子”.
    - 否则目标会直接失去扫描资格.
- `Renderer.forceRenderingOff` 更符合本次目标:
  - 它是“保持 renderer 组件存在,但不走普通显示”的语义.
  - 这样 helper 仍能读取 source renderer / mesh / bounds / material.
- 通过官方 Unity 文档快速核对后的结论:
  - `Renderer.forceRenderingOff`: 可强制关闭对象渲染.
  - `SkinnedMeshRenderer.BakeMesh()` 即使对象离屏,也仍会创建蒙皮快照.
  - 推论: 对 skinned external target 使用 `forceRenderingOff` 仍然可以维持当前 helper 的 baked-mesh 扫描路径.
- 为什么这轮先不做 layer 方案:
  - layer 需要改动目标层级的 layer 状态,并保存/恢复原 layer.
  - 同时它还会牵连用户已有 camera culling mask 与 physics layer matrix.
  - 对“只是想隐藏普通 mesh,保留雷达粒子”这个需求来说,侵入明显大于 `forceRenderingOff`.
- 本轮准备落地的最稳态 API:
  - enum: `GsplatLidarExternalTargetVisibilityMode`
    - `KeepVisible`
    - `ForceRenderingOff`
  - `GsplatRenderer` / `GsplatSequenceRenderer`:
    - 新增 `LidarExternalTargetVisibilityMode`
    - 默认 `ForceRenderingOff`
  - helper:
    - 追踪每个 source renderer 的原始 `forceRenderingOff`
    - 在移除目标、切回 `KeepVisible`、或 helper Dispose 时恢复原值

## 2026-03-07 12:16:43 +0800 验证结论: scan-only visibility 路径已闭环

- 针对性回归:
  - `Gsplat.Tests.GsplatLidarScanTests`
  - total=20, passed=20, failed=0, skipped=0
  - 新增测试已覆盖:
    - 默认值 = `ForceRenderingOff`
    - 非法 enum 回退 = `ForceRenderingOff`
    - helper 在 `ForceRenderingOff` 下隐藏 source renderer
    - helper Dispose 后恢复原始 `forceRenderingOff`
- 全量包回归:
  - `Gsplat.Tests`
  - total=63, passed=61, failed=0, skipped=2
  - 说明: 本轮新增 2 条测试后,总数由 61 增长到 63,且无失败
- OpenSpec 同步结果:
  - `lidar-external-targets` 从 21/21 扩展为 26/26
  - `openspec instructions apply --change "lidar-external-targets" --json` 结果仍为 `all_done`

## 2026-03-07 13:20:40 +0800 验证结论: Play 模式专用隐藏 路径已闭环

- 定向回归:
  - `Gsplat.Tests.GsplatLidarScanTests`
  - total=21, passed=21, failed=0, skipped=0
  - 新增测试已覆盖:
    - `ForceRenderingOffInPlayMode` 在非 Play 不隐藏
    - `ForceRenderingOffInPlayMode` 在 Play 时隐藏
- 全量包回归:
  - `Gsplat.Tests`
  - total=64, passed=62, failed=0, skipped=2
  - 说明: skipped=2 为既有忽略用例,本轮没有新增失败
- OpenSpec 同步结果:
  - `lidar-external-targets` 从 26/26 扩展为 31/31
  - `openspec instructions apply --change "lidar-external-targets" --json`
    - `progress=31/31`
    - `state=all_done`

## 2026-03-07 23:38:00 +0800 追加: frustum aperture 起手实现的观察

- 这一步最该先收敛的不是 `GsplatLidarScan` 或 compute kernel,而是“LiDAR sensor pose 到底从哪来”.
- 现有 `GsplatLidarScan` / `Gsplat.compute` 已经是矩阵消费端:
  - compute 吃 `modelToLidar`
  - draw 吃 `lidarLocalToWorld`
  - 因此只要调用侧先统一 resolve sensor pose,就能先把 camera-pose 主链跑通.
- 旧 external CPU helper 也必须一起切到同一个 sensor transform.
  - 否则 gsplat hit 与 external hit 会落在不同 sensor frame,nearest-hit 语义会悄悄错位.
- 旧 `LidarExternalTargets` 的兼容方案采用:
  - `FormerlySerializedAs("LidarExternalTargets")` 迁到 `LidarExternalStaticTargets`
  - 保留同名兼容 property 映射到 static 组
- 第一批先不碰 frustum active cells / LUT / GPU capture.
  - 否则会把“入口收敛”和“新采样几何”两个变量混在一起,很难定位回归.
- 定向验证结果:
  - `Gsplat.Tests.GsplatLidarScanTests`: total=27, passed=27, failed=0, skipped=0
- 全量验证结果:
  - `Gsplat.Tests`: total=70, passed=68, failed=0, skipped=2
  - `skipped=2` 为既有 ignore,没有新增失败
- 结论:
  - external target 可见性现在是稳定三态:
    - `KeepVisible`
    - `ForceRenderingOff`
    - `ForceRenderingOffInPlayMode`
  - 默认值仍保持 `ForceRenderingOff`,避免破坏既有 scan-only 默认语义.

## 2026-03-07 13:35:30 +0800 追加: RadarScan external mesh 性能分析

- 这是基于当前代码路径的实现级推断,不是 Unity Profiler 的实测结论.
- 当前 external mesh 进入 RadarScan 后,主要有 4 段成本:
  1. gsplat 自己的 LiDAR compute range image
  2. external target helper 的 mesh 扫描
  3. external hit buffer 上传到 GPU
  4. 最终 LiDAR 粒子 draw + shader
- 如果“不开 external mesh 还行,一开 external mesh 明显变慢”,最大嫌疑是第 2 段,不是第 4 段.

### 当前最重的 external helper 路径

- `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs`
  - `TryUpdateExternalHits(...)`
    - 每次 LiDAR tick 都会:
      - `SyncProxyEntries(...)`
      - `UpdateProxyEntriesFromSource()`
      - `ScheduleRaycasts(...)`
      - `ResolveHitsFromResults(...)`
      - `lidarScan.UploadExternalHits(...)`
- 关键热点:
  - `SyncProxyEntries(...)`
    - 每个 tick 都会对 root 做 `GetComponentsInChildren<Renderer>(includeInactive:false)`.
    - external target 层级很深时,这里只是“找对象”也会花掉一笔 CPU.
  - `RefreshSkinnedProxy(...)`
    - 对每个蒙皮目标都会 `BakeMesh()`.
    - 这是典型 CPU 热点.
  - `ScheduleRaycasts(...)`
    - 当前是“每个 cell 一条射线”.
    - 默认参数 `LidarAzimuthBins=2048`, `LidarBeamCount=128`.
    - cellCount = 2048 * 128 = 262144.
    - 也就是每次 LiDAR 更新默认要发 262144 条 raycast.
  - `UploadExternalHits(...)`
    - 每次 tick 都会整块 `SetData` 上传:
      - `uint[cellCount]`
      - `Vector4[cellCount]`
    - 默认 cellCount 下大约是 5MB 级别/次 的 CPU->GPU 上传.

### 当前 draw / shader 路径的成本

- `Runtime/Lidar/GsplatLidarScan.cs`
  - `RenderPointCloud(...)` 每帧都会 `Graphics.RenderMeshPrimitives(...)`.
- `Runtime/Shaders/GsplatLidar.shader`
  - 每个点在 vertex 里都会做:
    - range 重建
    - external / gsplat 最近命中竞争
    - 颜色混合
    - show/hide noise / warp / glow 相关计算
  - 如果 show/hide 动画正在进行,shader 负担会更重.
- 但从代码形态看:
  - “只要开 external mesh 就卡”更像 external helper 的 CPU/raycast 路径.
  - “不开 external mesh 也卡”则更像:
    - gsplat compute 太重
    - 或 LiDAR draw 分辨率太高

### 当前实现里最值得优先动的开关

- `LidarAzimuthBins`
  - 这是最直接的总成本旋钮.
  - 它同时线性影响:
    - raycast 条数
    - external hit 上传量
    - 最终点云绘制点数
- `LidarBeamCount`
  - 同样线性影响 cellCount.
- `LidarUpdateHz`
  - 直接影响:
    - gsplat range image rebuild 频率
    - external mesh raycast 频率
    - skinned mesh `BakeMesh()` 频率
- `LidarColorMode`
  - `Depth` 比 `SplatColorSH0` 更省.
  - 因为 `needsSplatId=false` 时会跳过 `ResolveMinSplatId` compute pass.

### 最佳结构性优化方向

- 不要每次 tick 都全量重建 360 度 range image.
- 改成“只更新当前扫描头经过的 azimuth 子范围”.
- 这会同时降低:
  - gsplat compute
  - external raycast
  - external hit 解析与上传
- 这是本轮代码里潜在收益最大的优化方向,但改动也最大.

### 次级但很值的工程优化

- external target world bounds 做缓存.
  - 当前 `ResolveVisibilityLocalBoundsForThisFrame()` 会多次调用 `TryComputeWorldBounds(...)`.
  - `TryComputeWorldBounds(...)` 每次都会递归遍历 renderer.
- external target renderer 列表做缓存/脏标记.
  - 当前 `SyncProxyEntries(...)` 是每个 LiDAR tick 全量递归扫描.
- skinned mesh 单独降频.
  - 不必和静态 mesh 共用同一个 update 频率.

## 2026-03-07 14:44:00 +0800 任务拆分结论: `lidar-camera-frustum-external-gpu-scan`

- 这次 `tasks.md` 最终按 6 组拆分:
  - `Aperture API 与 Inspector`
  - `Frustum active cells 与 LiDAR 资源`
  - `External GPU capture 基础设施`
  - `Static / Dynamic 更新策略与 fallback`
  - `external / gsplat 命中合并与最终显示语义`
  - `测试、文档与验证`
- 这样拆,是为了把实现顺序压平:
  - 先补 public API / Inspector / 旧字段兼容
  - 再改 frustum cell 与资源布局
  - 再落 external GPU capture 与 static/dynamic 调度
  - 最后才接最终 draw 语义与回归
- 本次任务清单里特意保留了两条边界:
  - 旧 360 + CPU `RaycastCommand` 路线不删,转为 fallback/debug
  - 旧 `LidarExternalTargets` 需要兼容读取或迁移提示,避免老场景升级后 external scan 丢失
- OpenSpec 当前状态:
  - change: `lidar-camera-frustum-external-gpu-scan`
  - schema: `spec-driven`
  - artifacts: `proposal/design/specs/tasks = done`
  - 结论: 现在已经是“规格齐备,可直接进入 apply/实现”的状态

## 2026-03-07 14:50:56 +0800 设计审查结论: GPU 路线有价值,但还缺 4 个关键约束

- 结论先说:
  - 方向本身是对的.
  - `camera frustum + external GPU capture` 比继续堆 CPU `RaycastCommand` 更像正确演进方向.
  - 但当前 design/spec/tasks 还少几条“实现前必须钉死”的几何与语义约束,否则很容易做出“能跑但结果不对”的版本.

### 1. `frustum camera` 与 `LidarOrigin` 的外参关系还没定义死

- 当前实现里,LiDAR 命中的几何语义明确是“从 `LidarOrigin` 沿离散 beam/azimuth 射线出发”:
  - `GsplatLidarExternalTargetHelper.TryUpdateExternalHits(...)` 直接接收 `lidarOrigin`
  - `GsplatLidarScan.RenderPointCloud(...)` / shader 也都是按 LiDAR 局部方向重建世界点
- 但新 design 写的是“frustum 模式使用指定 camera 的 matrices 产出 external depth/color”.
- 如果这个 camera 只是“看向差不多一致”,但位置不等于 `LidarOrigin`,那 external depth 就会变成“camera 视点最近表面”,不再是“LiDAR 视点 first return”.
- 正确做法要二选一:
  - A. 直接规定: frustum camera 必须与 `LidarOrigin` 共位姿,它只是 aperture carrier
  - B. 或规定: camera 只提供 intrinsics(FOV/aspect),extrinsics 一律仍取 `LidarOrigin`

### 2. GPU capture 不能直接把硬件 depth 当成 external range

- 当前 LiDAR range 语义不是普通相机 `view-space z`.
- `Gsplat.compute` 现在存的是“投影到离散 bin center 射线后的 depth^2”,不是随便一种 depth.
- `GsplatLidar.shader` 最终会用 `dirLocal * range` 重建世界点.
- 所以 GPU 路线如果只是渲染一张 depth RT,然后把它直接塞进 external hit buffer,几何一定会偏.
- resolve pass 必须显式做:
  - depth texture -> 线性视空间位置
  - 再转成“沿 LiDAR 当前 cell 射线的距离/或 depth^2”
  - 再写入 external hit buffer

### 3. `baseColor RT` 这个提法,和当前 external 颜色语义并不等价

- 现有 external 路线的颜色语义很克制:
  - 只取材质 `_BaseColor` / `_Color`
  - 不读贴图,不吃灯光,不吃后处理
- 但 design/tasks 里写的是 `depth/color RT` / `baseColor RT`.
- 如果后面真的让 camera 去渲染“正常颜色”,那拿到的会是:
  - 光照后的结果
  - 纹理后的结果
  - 甚至可能受 tonemap / gamma 路线影响
- 这会和 spec 里“继续保持当前 `SplatColorSH0` external surface 主色语义”打架.
- 更稳的 v1 定义应该是:
  - 不叫普通 `color RT`
  - 而是“override pass 输出 material main color”的 `surfaceColor RT`

### 4. static capture 的 invalidation 条件还不够完整

- 现在 spec 只写了:
  - camera pose/projection 变化
  - static target transform / mesh 变化
- 但对 external GPU capture 来说,至少还依赖:
  - renderer enabled / active 状态
  - sharedMaterial / `_BaseColor` / `_Color`
  - camera pixelRect / aspect / target RT 尺寸
- 少了这些,就会出现:
  - 目标颜色已经改了,static capture 还在复用旧颜色
  - 相机分辨率改了,cell-to-RT 映射已经变了,却还在复用旧 capture

### 额外提醒: hidden camera 路线和现有 scan-only visibility 有潜在冲突

- 这不是“GPU 方案一定错”,而是实现路线要谨慎选.
- 如果后面采用“hidden camera 直接渲染 source renderers”的方案,它很可能和:
  - `ForceRenderingOff`
  - `ForceRenderingOffInPlayMode`
形成冲突.
- 因为现有 external target 可见性就是通过 `Renderer.forceRenderingOff` 控普通 mesh 的.
- 更稳的路线更像是:
  - 显式 renderer list
  - `CommandBuffer.DrawRenderer` / override material
  - 或独立 proxy scene / proxy renderer

## 2026-03-07 15:40:54 +0800 规格补强结果: 已把 4 个 GPU 约束写进 artifacts

- 已修改:
  - `openspec/changes/lidar-camera-frustum-external-gpu-scan/design.md`
  - `openspec/changes/lidar-camera-frustum-external-gpu-scan/specs/gsplat-lidar-camera-frustum-external-scan/spec.md`
  - `openspec/changes/lidar-camera-frustum-external-gpu-scan/tasks.md`
- 这次不是新增功能范围,而是把实现契约写硬:
  1. `LidarOrigin` 仍是 beam origin,frustum camera 负责 aperture 朝向与 projection
  2. external GPU resolve 必须把 depth 转成 LiDAR ray-distance / `depthSq` 语义
  3. GPU color capture 限定为 `_BaseColor` / `_Color` 主色语义,不读 lit scene color
  4. static capture invalidation 扩展到 renderer/material/capture-layout 变化
- 额外一条重要实现倾向也已经写进 design/tasks:
  - frustum GPU capture 优先走显式 render list + override material / command buffer draw
  - 不要默认依赖 hidden camera + source renderer 当前可见状态

## 2026-03-07 16:40:12 +0800 设计决策更新: frustum 模式直接使用相机位置

- 用户已明确收敛新的 sensor-frame 决策:
  - frustum 模式下,直接使用 frustum camera 的位置作为 LiDAR 原点
  - camera 的朝向继续作为 aperture 朝向
  - projection / FOV / aspect / pixelRect 仍由该 camera 提供
- 因此上一版“`LidarOrigin` 在 frustum 模式下仍是 beam origin”的约束已被废弃.
- 最新 artifacts 现在统一为:
  - frustum mode = camera position + camera rotation + camera projection
  - `LidarOrigin` 只保留给旧 360 路线或非 frustum 路线
## 2026-03-07 17:20:00 +0800 阅读分析: frustum aperture mode + frustumCamera 最小接线

### 现状

- 目前 `LidarOrigin` 同时参与 3 条主链:
  - `TickLidarRangeImageIfNeeded()` 中的 compute pose:
    - `modelToLidar = LidarOrigin.worldToLocalMatrix * transform.localToWorldMatrix`
  - `RenderLidarInUpdateIfNeeded()` / `RenderLidarForCamera()` 中的 draw pose:
    - `lidarLocalToWorld = LidarOrigin.localToWorldMatrix`
  - `SyncExternalLidarHitsForCurrentRangeImage()` -> `GsplatLidarExternalTargetHelper.TryUpdateExternalHits(...)` 中的 external raycast origin
- `GsplatLidarScan` 自身已经把“sensor pose”和“render camera”分开:
  - `RenderPointCloud(..., Camera camera, Matrix4x4 lidarLocalToWorld, ...)`
  - 其中 `camera` 只是 draw 提交目标相机
  - 真正决定 LiDAR 点云空间位置的是 `lidarLocalToWorld`
- `Gsplat.compute` 的 LiDAR kernel 也已经把 pose 抽象成 `_LidarMatrixModelToLidar`
  - 因此“改用 camera pose”最小代价主要在调用侧,而不一定需要动 compute shader 本体

### OpenSpec 契约

- `openspec/changes/lidar-camera-frustum-external-gpu-scan/specs/gsplat-lidar-camera-frustum-external-scan/spec.md`
  - frustum 模式下,`frustumCamera` 必须直接作为 authoritative LiDAR sensor pose
  - `LidarOrigin` 在 frustum 模式下应退回非必填或被明确忽略

### 最关键改动面

- `Runtime/GsplatRenderer.cs`
  - 字段定义区:
    - 新增 aperture mode
    - 新增 frustumCamera
    - 现有 `LidarOrigin` 的 tooltip/语义说明要改成“360 路线使用”
  - `ValidateLidarSerializedFields()`
    - aperture mode 非法值防御
    - frustum 模式下不要继续隐含要求 `LidarOrigin`
  - `TickLidarRangeImageIfNeeded()`
    - 改 pose 来源与“缺失 origin”日志逻辑
  - `RenderLidarInUpdateIfNeeded()`
    - 改 draw pose 来源
  - `RenderLidarForCamera(Camera camera)`
    - 改 draw pose 来源
  - `SyncExternalLidarHitsForCurrentRangeImage()`
    - 若 external targets 仍启用,必须把 helper 输入 origin 同步切到 frustumCamera/resolve 后的 sensor pose
  - `IsAnyAnimationActiveForEditorTicker()` 与 Editor ticker 内 `r.LidarOrigin` 判定
    - 否则 EditMode 下 frustum 模式可能不 repaint
- `Runtime/GsplatSequenceRenderer.cs`
  - 上述同名字段与同名方法需要镜像修改,否则静态 renderer 和 sequence renderer 行为会分叉
- `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs`
  - `TryUpdateExternalHits(...)`
    - 当前直接收 `Transform lidarOrigin`
    - 如果 gsplat 主链切到 camera pose,而 external helper 还用旧 origin,external hit 会和 gsplat hit 不在同一 sensor frame
- `Editor/GsplatRendererEditor.cs`
  - `DrawPropertiesExcluding(...)`
  - `OnInspectorGUI()` 的 LiDAR 区:
    - 新增 aperture mode / frustumCamera 的 PropertyField
    - 现有 `LidarOrigin` warning/help 文案改成“仅 360 模式必填”
- `Editor/GsplatSequenceRendererEditor.cs`
  - 与 `GsplatRendererEditor.cs` 同步

### 容易漏掉并导致误导的点

- 只改 compute,不改 draw:
  - range image 在 camera pose 下生成,但点云仍按 `LidarOrigin.localToWorldMatrix` 重建,结果会空间错位
- 只改 draw,不改 compute:
  - 画出来的位置像是对了,但 first-return 命中仍按旧 origin 计算,遮挡与颜色竞争不对
- 忘了 external helper:
  - 开启 external target 时,external hit 仍从旧 origin 发射射线,会和 gsplat hit 竞争出错误最近点
- 忘了 Runtime/Editor 的“`LidarOrigin 为空`”日志和 HelpBox:
  - frustum 模式明明提供了 camera,仍会提示“不会渲染,请指定 LidarOrigin”,这是直接误导
- 忘了 Editor ticker 的 `LidarOrigin` gate:
  - EditMode 下会出现“参数改了但扫描前沿不动”的假象
- 只改 `GsplatRenderer`,忘了 `GsplatSequenceRenderer`:
  - 静态 gsplat 资源正常,序列资源仍走旧 origin,行为分叉

### 额外结论

- 如果这一步只做“新增 aperture mode + frustumCamera + pose source 切换”,那么 `GsplatLidarScan` 和 `Gsplat.compute` 本体几乎可以不动
- 但这还不是“真正的 frustum aperture”
  - 当前 LUT / cellCount / azimuth mapping 仍是完整 360 语义
  - 真正开始减少 frustum 外工作量时,才需要继续动 `EnsureLutBuffers()` / `TryRebuildRangeImage()` / active-cell 生成逻辑

## 2026-03-08 02:35:00 +0800 进展记录: static/dynamic capture 策略与最终闭环

### 本轮实现结论

- `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`
  - 已从“每次整组重抓”改为:
    - static signature compare
    - static 失效才重抓
    - dynamic 独立 cadence
    - static / dynamic 分组 capture buffers
    - resolve 时比较两组 external hit 最近者
  - 已补 private debug hooks,给 EditMode tests 锁语义:
    - `DebugGetStaticCaptureDirtyReasonForInputs(...)`
    - `DebugCommitStaticCaptureSignatureForInputs(...)`
    - `DebugComputeRayDepthSqFromLinearViewDepth(...)`
- `Runtime/Shaders/Gsplat.compute`
  - `ResolveExternalFrustumHits` 不再只吃一组 capture RT
  - 现在会分别采样:
    - static linearDepth + surfaceColor
    - dynamic linearDepth + surfaceColor
  - 然后在 GPU 上选最近 external 命中写回 `ExternalRangeSqBitsBuffer / ExternalBaseColorBuffer`
- `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`
  - external tick 已从“只在 range rebuild 成功后调用”改为“每帧独立调度”
  - GPU frustum 路线自己判断是否需要 refresh / resolve
  - CPU helper 仍只在 range rebuild 后更新,作为 surround360 / GPU 不可用 fallback

### 语义确认

- frustum external GPU route 现在满足 4 个写死约束:
  - 不降低现有视觉密度 / 频率 / RGB 语义
  - aperture 来自 `LidarFrustumCamera`
  - external 输入分 static / dynamic 两组
  - external mesh 主路径走 GPU capture
- external depth resolve 已明确不是 raw hardware depth:
  - 先读 capture 的 linear view depth
  - 再除以 rayDir.forwardDot
  - 最后写 LiDAR ray-distance 的 `depthSq`

### 新增回归

- `Tests/Editor/GsplatLidarExternalGpuCaptureTests.cs`
  - static signature:
    - 材质主色变化 -> `renderer-material`
    - active state 变化 -> `renderer-state`
    - capture layout 变化 -> `capture-layout`
  - dynamic cadence:
    - `uninitialized`
    - `cadence-due`
    - `time-reset`
  - depthSq helper:
    - 锁定 ray-distance 语义
  - frustum 模式联合 bounds:
    - `GsplatRenderer`
    - `GsplatSequenceRenderer`

### 最终验证证据

- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests.GsplatLidar`
  - total=`38`, passed=`38`, failed=`0`, skipped=`0`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_external_gpu_capture_lidar_2026-03-08.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_external_gpu_capture_lidar_2026-03-08.log`
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`79`, passed=`77`, failed=`0`, skipped=`2`
  - `skipped=2` 为既有 ignore,本轮无新增失败
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_external_gpu_capture_full_2026-03-08_r2.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_external_gpu_capture_full_2026-03-08_r2.log`

### OpenSpec 状态

- `openspec status --change "lidar-camera-frustum-external-gpu-scan" --json`
  - `isComplete = true`
- `openspec instructions apply --change "lidar-camera-frustum-external-gpu-scan" --json`
  - `progress = 35 / 35`
  - `state = all_done`

## 2026-03-09 17:37:45 +0800

### 现象

- 用户在 HDRP 场景中已经打开 MSAA,但 LiDAR 粒子 A2C 日志仍显示:
  - `requested=AlphaToCoverage`
  - `effective=AnalyticCoverage`
  - `allowMSAA=0 msaaSamples=1`

### 根因

- 当前 `Runtime/GsplatUtils.cs` 把 `Camera.allowMSAA` 当成 A2C 可用性的硬门槛.
- 但 HDRP 自己会在 `HDAdditionalCameraData.OnEnable()` 中把:
  - `m_Camera.allowMSAA = false`
- 也就是说,这个字段在 HDRP 下表达的是“不要走 legacy Camera MSAA 入口”,不是“这台 camera 没有 MSAA”.
- HDRP 真正的 MSAA 语义来自:
  - 当前 HDRP Asset
  - camera 的聚合后 Frame Settings
  - 若输出到 `RenderTexture`,还要再受 RT sample count 约束

### 方案

- 在 `GsplatUtils` 中新增统一的 LiDAR MSAA helper:
  - 普通管线继续沿用 `camera.allowMSAA + targetTexture/QualitySettings`
  - HDRP 通过反射调用 `FrameSettings.AggregateFrameSettings(...)`
  - 再读取 `FrameSettingsField.MSAA` 与 `GetResolvedMSAAMode(...)`
- 诊断日志不再只打印误导性的 `allowMSAA`,而是输出:
  - `cameraAllowMSAA`
  - `msaaSamples`
  - `msaaSource`

### 验证

- 定向 EditMode:
  - `Gsplat.Tests.GsplatLidarScanTests`
  - total=`33`, passed=`33`, failed=`0`, skipped=`0`
  - XML:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_hdrp_a2c_fix_2026-03-09.xml`
- 全包 EditMode:
  - `Gsplat.Tests`
  - total=`86`, passed=`83`, failed=`0`, skipped=`3`
  - `skipped=3` 为既有 ignore / 环境性 skip,本轮无新增失败
  - XML:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_full_lidar_hdrp_a2c_fix_2026-03-09_r2.xml`

### 经验

- HDRP 下凡是与 MSAA / A2C 相关的 runtime 判断:
  - 不能直接读 `Camera.allowMSAA`
  - 应优先读 resolved HD Frame Settings
  - 再结合实际 targetTexture sample count 判断最终 render target 是否真的是 multisampled

## 2026-03-09 22:18:00 +0800

### 现象

- 用户在真实项目中报告:
  - `Packages/wu.yize.gsplat/Tests/Editor/GsplatLidarScanTests.cs(11,29): error CS0234`
- 错误点不是 runtime,而是测试程序集新增了:
  - `using UnityEngine.Rendering.HighDefinition;`

### 根因

- `Tests/Editor/Gsplat.Tests.Editor.asmdef` 新增 `versionDefines` 只能控制源码条件编译.
- 但 asmdef 不能为测试程序集提供“条件 HDRP 程序集引用”.
- 结果就是:
  - 代码以为自己“只在有 HDRP 时才编进来”
  - 实际测试程序集仍没有 `Unity.RenderPipelines.HighDefinition.Runtime` 引用
  - 于是直接在编译阶段炸 `CS0234`

### 修复策略

- 不回滚已经正确的 runtime HDRP A2C helper.
- 只修测试层的依赖方式:
  - 删除测试文件对 `UnityEngine.Rendering.HighDefinition` 的直接 `using`
  - 移除测试 asmdef 里本轮新增的 HDRP `versionDefines`
  - 把 HDRP 专项测试改成纯反射探测:
    - 运行时扫描 `HDRenderPipelineAsset`
    - 运行时扫描 `HDAdditionalCameraData` / `FrameSettings` / `FrameSettingsField`
    - 通过反射配置 custom frame settings 与 override mask
    - 在非 HDRP 工程里用 `Assert.Ignore(...)` 安全跳过

### 验证

- 当前真实项目:
  - `dotnet build Gsplat.Tests.Editor.csproj -nologo`
  - 结果: `0 errors`, `4 warnings`
  - 说明用户报的 `CS0234` 已消失
- `_tmp_gsplat_pkgtests`:
  - 定向运行反射版 HDRP 测试
  - TestRunner 能正常加载该用例
  - 因为测试工程本身没装 HDRP,结果为:
    - `Skipped`
    - reason=`HDRP package is not loaded, skipping HDRP-specific LiDAR A2C test.`

### 经验

- 对 Unity 可选包(HDRP/VFX 等)做测试时:
  - “可选依赖”尽量放到运行时反射层
  - 不要让测试程序集形成编译时硬依赖
  - `versionDefines` 不能替代程序集引用本身
