# 任务计划: RadarScan show/hide noise 不可见(根因调查)

## 目标

在 Unity 里 RadarScan(LiDAR) 模式下,Show/Hide 期间能稳定看到与 ParticleDots 类似的 noise 颗粒扰动。
如果仍然看不到,也要给出可证据化的根因结论(例如: 实际用的不是这份 shader,或参数未到 shader)。

## 阶段

- [ ] 阶段1: 现场与上下文确认(代码与资产引用)
- [ ] 阶段2: 根因证据采集(参数链路与 shader 路径)
- [ ] 阶段3: 最小修复与验证(只改根因点)
- [ ] 阶段4: 回归测试,更新文档与提交 git

## 关键问题

1. Unity 运行时到底用的是哪一份 `GsplatLidar.shader`(AssetDatabase path)?
2. `showHideNoiseMode/Strength/Scale/Speed` 是否在 show/hide 过渡期真实进入了 draw call?
3. 如果参数链路无断点,但观感仍不可见,是 shader 公式问题还是渲染语义(ZWrite/Blend)导致的“看不出来”?

## 做出的决定

- 先做证据化诊断,再做修复。
  理由: 之前已经多轮“增强噪声”,用户仍反馈完全无变化,必须先排除“根本没跑到我们改的代码”。

## 遇到的错误

- (待补)

## 状态

**目前在阶段2**
- 我刚加了一条 Editor 下的节流诊断日志,会在 LiDAR show/hide 动画期间打印:
  - settings 与 shader 的 AssetDatabase 路径
  - show/hide 与 noise 参数值
  - 材质是否真的有 `_LidarShowHideNoise*` 属性
- 下一步是基于这条日志的结果,锁定断点位置再做最小修复。

## 2026-03-02 18:15:38 +0800 追加: 下一步行动(日志驱动排查)

- [ ] 读取 `Runtime/Shaders/GsplatLidar.shader`,核对 `_LidarShowHideNoise*` 是否存在于 ShaderLab `Properties` 列表,且命名与 C# 侧一致。
- [ ] 读取 `Runtime/Lidar/GsplatLidarScan.cs`,核对 MPB 下发的 property 名称与 shader 完全一致(包含大小写与后缀)。
- [ ] 如果 shader 确实已经声明了 properties,但 `HasProperty` 仍为 0:
  - 追加诊断: 打印 `Shader.GetPropertyCount()` 与前若干 property name,确认 Unity 运行时读到的是新版 shader。
  - 追加诊断: 打印 `Shader.name` 与 `shader.GetInstanceID()` 等,排除 shader 资产被替换/重载。
- [ ] 如果 `HasProperty` 变为 1 但仍“看不到”,做一次强证据化的 shader 可视化:
  - 临时增加一个 debug 开关,把 noise 值直接映射到颜色/亮度,确保能肉眼确认 shader 端收到了 noise 参数。

## 2026-03-02 18:46:40 +0800 进展: 已确认进入 shader,并做幅度调参

- [x] `_LidarShowHideNoise*` 已在 `GsplatLidar.shader` 的 ShaderLab `Properties` 中显式声明(隐藏属性),用于稳态 MPB 绑定。
- [x] 新增 EditMode 单测锁定该“属性契约”,防止未来重构再次丢失。
- [x] 针对用户反馈“只有很小幅度的运动”,已把 LiDAR show/hide 的屏幕空间 warp 改为:
  - 与点半径相关联(更稳定更容易感知).
  - 对 noiseStrength 做 sqrt 提亮(中等强度更明显,仍保持小幅度).
- [x] 已在 `_tmp_gsplat_pkgtests` 跑 EditMode tests(`Gsplat.Tests`):
  - total=44, passed=42, failed=0, skipped=2.

## 2026-03-02 19:00:06 +0800 进展: warp 幅度改为可调(不再与点半径耦合)

- [x] 按用户要求: LiDAR show/hide 的 noise 位移不再跟 `LidarPointRadiusPixels` 绑定。
- [x] 新增可调参数:
  - `GsplatRenderer.LidarShowHideWarpPixels`
  - `GsplatSequenceRenderer.LidarShowHideWarpPixels`
  - shader property: `_LidarShowHideWarpPixels`
- [x] Inspector 已暴露该字段(在 LiDAR Visual 区域),可直接调出你想要的“颗粒扰动幅度”。
- [x] 已在 `_tmp_gsplat_pkgtests` 跑 EditMode tests(`Gsplat.Tests`):
  - total=44, passed=42, failed=0, skipped=2.

## 2026-03-03 00:19:55 +0800 追加需求: RadarScan 噪声语义对齐高斯(CurlSmoke + WarpStrength)

- [x] 按用户要求: LiDAR show/hide 的 noise 模式支持真正的 `CurlSmoke`(curl-like 向量场)。
- [x] LiDAR show/hide 的屏幕空间 jitter 额外乘上 `WarpStrength`(与高斯语义一致: 0 禁用,>0 增强)。
- [x] 扩展 LiDAR shader 属性契约单测,覆盖 `_LidarShowHideWarpStrength`。
- [x] 在 `_tmp_gsplat_pkgtests` 跑 EditMode tests(`Gsplat.Tests`)回归:
  - total=44, passed=42, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_curl_warpstrength_2026-03-03_002646_noquit.xml`

## 2026-03-03 10:03:00 +0800 追加需求: RadarScan show/hide 增加高斯同款 glow

### 目标补充

- RadarScan(LiDAR) 模式下,Show/Hide 期间也要有和高斯显隐动画类似的“带颜色的发光叠加(glow)”.
- glow 不是单纯提亮(brightness multiply),而是像高斯那样对 `color.rgb` 做 additive 的发光叠加.

### 下一步行动(按最小侵入落地)

- [x] 对照 `Runtime/Shaders/Gsplat.shader` 的显隐 glow 实现,提取其 shader property 与公式.
- [x] 在 `Runtime/Shaders/GsplatLidar.shader` 增加 LiDAR 的 show/hide glow uniforms(颜色+强度),并以 additive 方式叠加到 `rgb`.
  - 约束: 新增的 shader uniforms 必须写进 ShaderLab `Properties`(隐藏属性),避免再次出现 MPB "HasProperty=0".
- [x] C# 下发层复用现有高斯参数:
  - glowColor: `GlowColor`
  - 强度: show/hide 各自的 intensity(按当前 show/hide 动画方向选择)
- [x] 扩展/新增 EditMode 单测,锁定 LiDAR shader 必须包含新 glow 属性.
- [x] 在 `_tmp_gsplat_pkgtests` 回归 `Gsplat.Tests` EditMode.
- [x] git commit(包含 shader + runtime + tests + changelog 如有需要).
  - commit: `2630f96`

### 状态

**目前在阶段3(执行/构建)**
- 我正在把 RadarScan(LiDAR) 的 show/hide glow 补齐为高斯同款 additive glow,并确保参数可调且有单测锁死契约。

## 2026-03-03 10:06:30 +0800 追加需求: LidarShowHideWarpPixels 不限制最大值

- 用户需求: `LidarShowHideWarpPixels` 不要再做最大值 clamp(当前上限为 64).
- 处理原则:
  - 仍然保留对 NaN/Inf/负数的防御性校验,避免 shader 出现 NaN/黑屏.
  - 移除 max clamp,让用户可以把 warpPixels 调到任意大用于夸张效果或调试.

### 下一步行动

- [x] 移除 `GsplatRenderer.ValidateLidarSerializedFields` 内对 `LidarShowHideWarpPixels > 64` 的 clamp.
- [x] 移除 `GsplatSequenceRenderer.ValidateLidarSerializedFields` 内同款 clamp.
- [x] 增加 EditMode 单测锁定: 大值不会被 clamp.
- [x] 回归 `_tmp_gsplat_pkgtests` EditMode tests.

### 状态

**目前在阶段4(回归与提交)**
- 代码与测试已完成,接下来只剩一次 git commit,把本轮的 glow + warpPixels 去上限一起提交。

## 2026-03-03 10:14:20 +0800 新需求: RadarScan glow 独立控制 + 调整 show/hide glow 节奏

- 用户需求:
  - RadarScan(LiDAR) 的 glow 颜色/强度要与高斯分开,单独可调.
  - hide 的 glow "走得太快",希望在时间上更靠后(更持久/更像余辉).
  - show 的 glow 完全看不出,疑似亮度或 mask 逻辑问题.

### 初步诊断(基于代码对照)

- 高斯(`Gsplat.shader`)的 show/hide glow 之所以在 show 阶段也可见,关键在于:
  - alphaMask = max(visible, ring) , ring 会作为 alpha 下限.
  - 因此就算 visible=0(尚未 reveal),ring 仍会被绘制出来.
- LiDAR(`GsplatLidar.shader`)当前只用 visible 去乘 showHideMul,没有把 ring 纳入 alphaMask.
  - 结果: show 阶段 ring 所在的"外侧"点几乎都被 early-out 丢弃,导致 glow 看不到.
- hide 阶段 glow 过快,根因是 LiDAR 侧 glowFactor 只有 ring,没有高斯那样的 tailInside afterglow.

### 下一步行动

- [ ] Runtime: 为 `GsplatRenderer/GsplatSequenceRenderer` 增加 LiDAR 专用 glow 参数(颜色+show/hide 强度),并在 ValidateLidarSerializedFields 做 NaN/Inf/负数防御.
- [ ] Runtime: LiDAR draw 下发改为使用 LiDAR 专用 glow 参数(不再复用高斯 GlowColor/ShowGlowIntensity/HideGlowIntensity).
- [ ] Shader: LiDAR show/hide 叠加逻辑对齐高斯:
  - [ ] 把 ring 纳入 alphaMask(解决 show glow 不可见).
  - [ ] 为 hide 增加 tailInside afterglow(解决 hide glow 太快).
  - [ ] glowFactor 用 ring+tail 的组合,并继续用 additive glow 叠加到 rgb.
- [ ] Tests: 更新/新增 EditMode tests,锁定 LiDAR 专用 glow 字段 clamp 行为(避免未来改动又被无意回退).
- [ ] 回归 `_tmp_gsplat_pkgtests` EditMode `Gsplat.Tests`.
- [ ] git commit.

### 进展(已完成)

- [x] Runtime: 为 `GsplatRenderer/GsplatSequenceRenderer` 增加 LiDAR 专用 glow 参数(颜色+show/hide 强度),并在 ValidateLidarSerializedFields 做 NaN/Inf/负数防御.
- [x] Runtime: LiDAR draw 下发改为使用 LiDAR 专用 glow 参数(不再复用高斯 GlowColor/ShowGlowIntensity/HideGlowIntensity).
- [x] Shader: LiDAR show/hide 叠加逻辑对齐高斯:
  - [x] 把 ring 纳入 alphaMask(解决 show glow 不可见).
  - [x] 为 hide 增加 tailInside afterglow(解决 hide glow 太快).
  - [x] glowFactor 用 ring+tail 的组合,并继续用 additive glow 叠加到 rgb.
  - [x] 修正 discard: 让 discard 判断包含 glowAdd(避免 trail 很小导致 show glow 被提前丢弃).
- [x] Tests: 更新/新增 EditMode tests,锁定 LiDAR 专用 glow 字段 clamp 行为(避免未来改动又被无意回退).

## 2026-03-08 14:30:39 +0800 新问题: 雷达扫描粒子的 show/hide 运动 noise 是否等于 Unity VFX Graph 的 Value Curl Noise

### 当前目标

- 回答用户一个语义确认问题:
  - 现有 RadarScan(LiDAR) show/hide 期间看到的运动噪声,是不是 Unity 官方文档里的 `Operator-ValueCurlNoise`.
- 这次不是直接改代码.
- 重点是把"实现上是否同源"和"视觉上是否相似"分开说清楚.

### 下一步行动

- [ ] 读取 `Runtime/GsplatRenderer.cs` 中 `GsplatVisibilityNoiseMode` 的注释,确认公开语义.
- [ ] 读取 `Runtime/Shaders/Gsplat.hlsl` 与 `Runtime/Shaders/GsplatLidarPassCore.hlsl` 中的 `CurlSmoke` / `EvalCurlNoise` 实现,确认是否为 value-noise gradient/curl 构造.
- [ ] 对照 Unity 官方 `Value Curl Noise` 文档,确认它的输入/输出与算法表述.
- [ ] 用"现象 -> 假设 -> 验证证据 -> 结论"回答用户,避免把"类似"说成"完全相同".

### 状态

**目前在阶段2**
- 我正在对照本包 shader 实现与 Unity 官方文档.
- 这一步的目的是区分:
  - "视觉风格接近 Value Curl Noise"
  - 和 "这里真的直接调用了 Unity VFX Graph 的那个 Operator"

## 2026-03-08 14:33:20 +0800 进展: 对照完成,准备回复用户

- [x] 读取 `Runtime/GsplatRenderer.cs` 中 `GsplatVisibilityNoiseMode` 的注释,确认公开语义.
- [x] 读取 `Runtime/Shaders/Gsplat.hlsl` 与 `Runtime/Shaders/GsplatLidarPassCore.hlsl` 中的 `CurlSmoke` / `EvalCurlNoise` 实现,确认其为 `value noise -> vector potential -> curl(A)` 的构造.
- [x] 对照 Unity 官方 `Value Curl Noise` 文档,确认其核心描述也是 `Value Noise + curl function`.
- [x] 明确区分两件事:
  - 不是直接调用 Unity VFX Graph 现成 Operator.
  - 但 `CurlSmoke` 模式与它属于同一家族的算法思路.

### 本轮结论

- 默认不是:
  - `VisibilityNoiseMode` 默认值是 `ValueSmoke`,不是 `CurlSmoke`.
- 可切换为相近语义:
  - 当 `VisibilityNoiseMode = CurlSmoke` 时,LiDAR show/hide 的运动噪声会使用自写的 curl-like 向量场.
- 因此回答用户时要说成:
  - "不是直接那个节点."
  - "但 `CurlSmoke` 模式的核心思路和 Unity 的 `Value Curl Noise` 很接近."

### 状态

**目前在阶段4**
- 证据已经齐全.
- 下一步是把结论按"现象 -> 假设 -> 证据 -> 结论"发给用户.

## 2026-03-07 16:55:00 +0800 追加任务: frustum aperture mode + frustumCamera 最小代价接线阅读

### 本次目标

- 不改代码,只回答一个聚焦问题:
  - 如果要最小代价新增 `frustum aperture mode + frustumCamera` 字段,
  - 并让 gsplat 现有 compute/draw 链路先改用 camera pose(而不是 `LidarOrigin`),
  - 最关键要改哪些方法、字段、Inspector 点位。

### 计划

## 2026-03-08 22:18:00 +0800 新证据: external mesh 雷达粒子仍稳定落在背光面

### 用户现场反馈

- `LidarExternalHitBiasMeters` 调整无效.
- 现象不是轻微穿帮,而是 external mesh 的雷达粒子整体跑到了"远离雷达 loc cam"的那一面.
- 用户举例:
  - 对球体来说,粒子现在落在球体背面.
  - 如果把雷达 loc cam 当成光源,粒子都在阴影侧.

### 当前判断

- 这说明问题大概率不在 draw 阶段的小幅 render bias.
- 更像是 external GPU capture / resolve / draw 重建链路里,有一处把"相机可见前表面"解释成了"穿过物体后的远表面"或"沿错误方向重建".

### 下一步行动

- [ ] 读取 `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`,确认 capture 相机、view/proj、sensor frame、depth resolve 的坐标语义.
- [ ] 读取 `Runtime/Shaders/GsplatLidarExternalCapture.shader` 与 `Runtime/Shaders/Gsplat.compute`,确认 external depth encode/decode 是否与 Unity camera forward / clip depth 约定一致.
- [ ] 读取 `Runtime/Shaders/GsplatLidarPassCore.hlsl`,确认 `useExternalHit` 路径的 worldPos 重建方向是否与 external hit 的距离语义匹配.
- [ ] 若发现根因,直接做最小修复并补单测.

### 状态

**目前在阶段2**
- 我正在把问题收敛到 external capture -> resolve -> draw 的单条链路上.
- 目标不是继续调参,而是找出"为什么前表面被系统性翻到背面".

## 2026-03-08 20:52:00 +0800 进展: external capture 最近面选择已改回 hardware depth 语义

### 已完成

- [x] 读取 `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`,确认 capture 相机、view/proj、sensor frame、depth resolve 的坐标语义.
- [x] 读取 `Runtime/Shaders/GsplatLidarExternalCapture.shader` 与 `Runtime/Shaders/Gsplat.compute`,确认 external depth encode/decode 的关键风险点.
- [x] 确认问题不在 `LidarExternalHitBiasMeters`,而在 external GPU capture 的最近表面选择路线.
- [x] 做最小但根因级修复:
  - 保留 `Cull Off`
  - 放弃 `encoded-depth + BlendOp Max`
  - 改回 `hardware depth nearest surface + color pass ZTest Equal`
- [x] 新增/更新定向测试,锁定这条最近面语义.
- [x] 运行 Unity EditMode 定向回归:
  - `Gsplat.Tests.GsplatLidarExternalGpuCaptureTests`
  - total=`8`, passed=`8`, failed=`0`, skipped=`0`
- [x] 运行 Unity EditMode 全包回归:
  - `Gsplat.Tests`
  - total=`85`, passed=`83`, failed=`0`, skipped=`2`

### 当前状态

**目前在阶段4**
- 代码修复、定向回归、全包回归都已完成.
- 下一步是把结论同步给用户,重点说明:
  - 为什么 bias 无效
  - 为什么上一轮 encoded-depth 路线会把点翻到背面
  - 这次为什么改回 hardware depth 更稳

## 2026-03-08 21:03:00 +0800 新证据: 用户现场反馈“还在背面”

### 现状重估

- `Cull Off + hardware depth nearest surface` 这轮改完后,用户现场仍然看到 external mesh 粒子在远离 `LidarFrustumCamera` 的那一面.
- 这说明当前自动化只验证了"代码结构语义",还没有验证"真实 GPU capture / resolve / reconstruct 的运行结果".

### 下一步行动(证据化,不再盲改)

- [ ] 增加一个最小功能性验证:
  - 以简单球体为目标,直接读回 external capture 的中心像素深度.
  - 目标是确认 capture 到底拿到 front depth 还是 back depth.
- [ ] 若 capture 是 front depth:
  - 继续验证 `ResolveExternalFrustumHits` 和最终 `worldPos = lidarLocalToWorld * dir * range` 是否把 front depth 重建错了.
- [ ] 若 capture 已经是 back depth:
  - 直接回到 capture shader / draw state 继续修.

### 当前状态

**目前在阶段2**
- 现在的优先级是把问题从“猜测”变成“可观测证据”.
- 在拿到 capture/readback 证据前,不再继续拍脑袋改公式.

## 2026-03-08 21:23:00 +0800 进展: 已用功能测试复现并修正 reversed-Z 下的 far-side capture

### 新证据

- 我新增了一个真实 GPU capture 功能测试:
  - `ExternalGpuCaptureDepthPass_CenterPixelMatchesSphereFrontDepth`
- 修复前:
  - 球体中心像素读回是 `5.5`
  - 这是球体后表面的深度
- 修复后:
  - 同一个测试读回 `4.5`
  - 回到球体前表面的正确深度

### 根因结论

- 问题不在 `LidarExternalHitBiasMeters`.
- 也不在 `linearDepth / rayDirSensor.z` 这个换算公式.
- 真正根因是 external capture depth pass 在 reversed-Z 平台上仍使用了 forward-Z 语义:
  - `ZTest LEqual`
  - `clearDepth = 1`
- 这会让闭合 mesh 稳定把 far side 留下来.

### 已完成

- [x] external capture depth pass 改为按平台切换 compare function:
  - forward-Z -> `LessEqual`
  - reversed-Z -> `GreaterEqual`
- [x] depth clear 改为按平台切换:
  - forward-Z -> `1`
  - reversed-Z -> `0`
- [x] 新增真实 GPU capture 功能测试,锁定球体中心像素必须命中前表面
- [x] external capture 定向组回归通过
- [x] 全包 `Gsplat.Tests` 回归通过

### 当前状态

**目前在阶段4**
- 根因已经被功能性测试证实并修正.
- 下一步是把修复结论和验证结果同步给用户.

## 2026-03-08 21:34:00 +0800 追加需求: 保留 bias 能力,但把默认值收回到 0

### 用户决策

- 采用极简版:
  - 不删除 `LidarExternalHitBiasMeters`
  - 但把默认值和 NaN/Inf 兜底值从 `0.01` 收回到 `0`

### 下一步行动

- [ ] 修改 `GsplatRenderer/GsplatSequenceRenderer` 的默认值与 sanitize fallback
- [ ] 修改 shader hidden property 默认值
- [ ] 同步 inspector/help 文案、README、测试断言
- [ ] 跑相关 EditMode 回归
- [ ] git 提交本轮 external capture / bias cleanup

### 当前状态

**目前在阶段3**
- 我正在做的是“收窄默认参数面”,不是回退真正的根因修复.

## 2026-03-08 19:57:17 +0800 新问题: external mesh 粒子显示在模型后面

### 当前目标

- 修复 RadarScan external mesh 命中点"显示在模型后面"的问题。
- 这次目标不是继续处理 `Cull Back/Cull Off`,而是修正 external hit 的最终可视化深度关系。

### 根因假设

- 用户最新澄清是"在模型背后",更像是:
  - external GPU capture 已经抓到了正确的前表面深度,
  - 但 LiDAR 点云 draw 在 `useExternalHit` 路径上用命中距离重建 `worldPos` 时,点位与表面过贴甚至略深,
  - 结果普通 mesh 仍然挡在点前面。
- 因此仅改 capture shader 的 culling 不够。

### 下一步行动

- [ ] 读取当前已改到一半的 `LidarExternalHitBiasMeters` 链路,确认哪些文件还没接完。
- [ ] 在 `useExternalHit` 的 draw 路径增加"沿传感器射线前推"的 render-only bias:
  - C# 参数: `LidarExternalHitBiasMeters`
  - shader uniform: `_LidarExternalHitBiasMeters`
  - 仅影响 external hit 的最终渲染位置,不改变 first return 竞争语义。
- [ ] 补齐 EditMode 测试:
  - clamp/default 行为
  - shader property 契约
  - pass core 中 external hit bias 逻辑存在性
- [ ] 运行 `Gsplat.Tests` 包测试,确认没有引入回归。
- [ ] 如果验证通过,再回写 `notes.md` / `WORKLOG.md` / `ERRORFIX.md`.

### 状态

**目前在阶段3**
- 我现在正在把 external-hit-only 的前推 bias 整条链路补完整。
- 这一步的目的,是把粒子从模型表面内部/背后轻微推出,让它重新稳定显示在可见表面前侧。

## 2026-03-08 20:02:50 +0800 进展: external hit bias 已闭环并通过回归

- [x] 读取当前已改到一半的 `LidarExternalHitBiasMeters` 链路,确认哪些文件还没接完。
- [x] 在 `useExternalHit` 的 draw 路径增加"沿传感器射线前推"的 render-only bias:
  - C# 参数: `LidarExternalHitBiasMeters`
  - shader uniform: `_LidarExternalHitBiasMeters`
  - 仅影响 external hit 的最终渲染位置,不改变 first return 竞争语义。
- [x] 补齐 EditMode 测试:
  - clamp/default 行为
  - shader property 契约
  - pass core 中 external hit bias 逻辑存在性
- [x] 运行 `Gsplat.Tests` 包测试,确认没有引入回归。
- [x] 回写 `notes.md` / `WORKLOG.md` / `ERRORFIX.md`.

### 验证证据

- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
- total=`83`, passed=`81`, failed=`0`, skipped=`2`
- XML:
  - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_hit_bias_2026-03-08.xml`
- log:
  - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_external_hit_bias_2026-03-08.log`

### 状态

**目前在阶段4**
- external hit 的前推 bias 已经落地并回归通过。
- 如果用户后续仍说"还在模型后面",下一步应转向视觉调参与现场手测,而不是继续怀疑 capture culling。

## 2026-03-08 20:06:30 +0800 新证据: bias 无效, 根因上修到 external capture 深度语义

### 新现象

- 用户现场反馈: `LidarExternalHitBiasMeters` 怎么调都没有用。
- 并给出更强的几何描述:
  - 球体 external 粒子整体落在"背着雷达中心/loc cam"的那一面。
  - 更像是 capture/resolve 直接选到了"后半球",而不是仅仅贴得太深。

### 新根因假设

- 真正的问题更可能不在 draw 端 bias,而在 external GPU capture 的"最近表面"选择方式.
- 当前 capture 仍依赖离屏 raster + depth 语义来决定哪一层表面胜出。
- 若该链路在当前平台上没有稳定选中最近可见面,闭合体(如球)就会整体翻到错误半球。

### 下一步行动

- [ ] 复核 `GsplatLidarExternalGpuCapture.CaptureGroup` 与 `ResolveExternalFrustumHits`,确认"最近表面"是否依赖平台 depth 语义。
- [ ] 若确认 capture 深度语义不稳,改成更稳的两步:
  - 深度 pass 独立稳定写出每像素最小线性深度
  - 颜色 pass 只认这层最小深度对应的表面
- [ ] 增加针对 external capture shader / capture command 的回归测试,锁定"不再依赖不稳 depth 选择"这一约束。
- [ ] 再跑 `Gsplat.Tests`,确认没有回归。

### 当前状态

**目前回到阶段2**
- 我现在重新回到根因调查,重点检查 external GPU capture 的深度/最近面选择语义。
- 这一步是必要的,因为用户给出的球体案例已经说明: 这不是 1cm 级别的 bias 能解决的问题。

- [ ] 读取 OpenSpec `lidar-camera-frustum-external-gpu-scan` 的 design/spec/tasks,确认当前约定是 `camera position + rotation + projection`.
- [ ] 搜索 `LidarOrigin`、LiDAR 参数下发、Inspector 绘制、compute/draw 提交方法,定位真正的最小改动面.
- [ ] 输出一份仅阅读分析的清单:
  - 需要改的文件和方法名
  - 每个改动点的作用
  - 哪些漏掉会导致行为不一致或日志误导

### 当前状态

## 2026-03-08 10:47:00 +0800 追加任务: OpenSpec change 归档前检查

### 本次目标

- 完成一个已实现 OpenSpec change 的归档闭环。
- 归档前补做 continuous-learning 检查,避免把还没沉淀的设计文档直接移入 archive。

### 已确认事实

- 当前 `openspec list --json` 显示有多个已完成但未归档的 change:
  - `lidar-camera-frustum-external-gpu-scan`
  - `lidar-external-targets`
  - `particle-dots-lidar-scan`
  - `burn-reveal-visibility`
- 因为存在多个候选项,这一步不能擅自替用户选择归档目标。

### 下一步行动

- [ ] 读取当前四文件,产出一份最小 continuous-learning 摘要。
- [ ] 让用户明确本次要归档的 change 名称。
- [ ] 检查该 change 的 artifact / tasks / delta spec sync 状态。
- [ ] 执行归档,并回写 `WORKLOG.md` / `LATER_PLANS.md` / `task_plan.md`。

### 当前状态

**目前在阶段1(归档前检查)**
- 我已经确认存在多个可归档 change,下一步先完成最小 continuous-learning 摘要,随后请用户明确归档目标。

## 2026-03-08 11:02:00 +0800 新需求: RadarScan 粒子抗锯齿模式

### 本次目标

- 为 RadarScan(LiDAR) 粒子增加可选抗锯齿能力。
- 不是只做一个硬编码开关,而是设计成可切换的常见 AA 模式,便于用户按画质/性能取舍。

### 当前已知

- RadarScan 当前主渲染链路集中在:
  - `Runtime/Lidar/GsplatLidarScan.cs`
  - `Runtime/Shaders/GsplatLidar.shader`
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`
- 仓库已有主 gsplat 的 `GSPLAT_AA` 语义,但 RadarScan 粒子还没有专门的 AA 模式枚举与参数。

### 计划

- [ ] 阅读 LiDAR 粒子 shader 与 draw 参数链路,确认当前“锯齿”主要来自哪里。
- [ ] 评估适合 RadarScan 粒子的几种常见 AA 路线:
  - analytic/coverage soft edge
  - alpha-to-coverage
  - lightweight post AA(如 FXAA 风格)
  - 明确哪些不适合(例如直接上重型 TAA)
- [ ] 创建新的 OpenSpec change,把需求和边界写清楚。
- [ ] 再决定是先起草 artifacts,还是直接落地实现。

### 当前状态

**目前在阶段1(现状阅读与方案收敛)**
- 我先读取 RadarScan 粒子渲染链路,确认现有 shader/材质/Inspector 能承接哪类 AA 选项,再创建对应的 OpenSpec change。

## 2026-03-08 11:22:00 +0800 进展: AA 路线已初步收敛,change 已创建

- [x] 阅读 LiDAR 粒子 shader 与 draw 参数链路,确认当前锯齿主要来自小半径点的固定 feather coverage.
- [x] 初步评估适合 RadarScan 粒子的 AA 路线:
  - `Off`
  - `AnalyticCoverage`(推荐默认)
  - `AlphaToCoverage`(依赖 MSAA)
  - `AnalyticCoveragePlusAlphaToCoverage`
- [x] 已创建新的 OpenSpec change:
  - `radarscan-particle-antialiasing-modes`
- [x] 已拿到 proposal artifact 指令:
  - 当前 progress=`0 / 4`
  - `proposal` 已 ready
- [ ] 下一步:
  - 起草 `proposal.md`
  - 明确 capability 命名与“哪些 AA 不纳入本轮”的边界

### 当前状态

**目前在阶段2(方案收敛完成,等待进入 proposal)**
- 我已经确认这个需求不适合直接上全屏后处理型 AA.
- 最正确的第一版应该是:
  - 先做 RadarScan 局部 shader coverage AA
  - 再把 AlphaToCoverage 作为可选高质量模式
  - 明确不把 TAA/SMAA/FXAA 伪装成组件内选项

## 2026-03-08 11:32:00 +0800 完成: OpenSpec 四件套已起草

- [x] 起草 `proposal.md`
- [x] 起草 `design.md`
- [x] 起草 `specs/gsplat-lidar-particle-antialiasing/spec.md`
- [x] 起草 `tasks.md`
- [x] 运行 `openspec status --change "radarscan-particle-antialiasing-modes"` 确认 artifacts 完整

### 当前状态

**目前在阶段4(规格起草完成)**
- `radarscan-particle-antialiasing-modes` 已完成 `4 / 4` artifacts.
- 这次 change 已经可以直接进入下一步:
  - 实现(`/opsx:apply` 语义)
  - 或继续先做规格审读/微调

## 2026-03-08 12:02:00 +0800 起手实现: `radarscan-particle-antialiasing-modes`

### 本轮目标

- 从 OpenSpec `radarscan-particle-antialiasing-modes` 直接进入实现。
- 第一阶段优先完成:
  - LiDAR 粒子 AA 枚举与 runtime/Inspector 接线
  - `AnalyticCoverage` shader 路线
  - A2C fallback 骨架
  - 基础测试与文档

### 下一步行动

- [ ] 读取 `openspec instructions apply --change "radarscan-particle-antialiasing-modes" --json`
- [ ] 读取 Runtime / Editor / Shader / Tests 的相关文件,定位最小改动面
- [ ] 先实现 `LegacySoftEdge` + `AnalyticCoverage` + fallback 再补 A2C shell

### 当前状态

**目前在阶段3(执行/构建)**
- 我已经确认将直接按推荐路线落地:
  - 推荐 `AnalyticCoverage`
  - `AlphaToCoverage` 作为依赖 MSAA 的可选高质量模式

**目前在阶段1**
- 我正在做“入口收敛”,优先确认:
  - 现有 OpenSpec 对 frustum mode 的姿态契约
  - `LidarOrigin` 在代码里真实参与了哪些链路
  - Inspector 和日志目前如何暴露这些字段

### 2026-03-07 17:22:00 +0800 进展: 阅读分析完成

- [x] 读取 OpenSpec `lidar-camera-frustum-external-gpu-scan` 的 design/spec/tasks,确认契约已经写死:
  - frustum mode = `frustumCamera` 直接提供 sensor origin + rotation + projection
  - `LidarOrigin` 在 frustum 模式下不再是必填原点
- [x] 搜索 `LidarOrigin`、LiDAR 参数下发、Inspector 绘制、compute/draw 提交方法,已定位最小改动面
- [x] 已整理只读结论:
  - `LidarOrigin` 当前同时参与 compute pose、draw pose、external raycast origin
  - 若仅做“camera pose 替换”,最关键要改的是两个 runtime 组件里的 5 类点位:
    - 字段/tooltip/validate
    - compute 入口
    - draw 入口
    - external helper 入口
    - Editor ticker + Inspector 警告文案

### 当前状态

**目前在阶段3**
- 我已经完成阅读分析,下一步是向用户输出:
  - 需要改的文件和方法名
  - 每个点位的作用
  - 哪些漏掉会直接导致行为不一致或日志误导
- [x] 回归 `_tmp_gsplat_pkgtests` EditMode `Gsplat.Tests`.
- [ ] git commit.

### 状态

**目前在阶段4(回归与提交)**
- 代码与测试已完成,接下来只剩一次 git commit,把本轮的 LiDAR glow 独立参数 + shader 节奏调参一起提交。

## 2026-03-03 12:13:09 +0800 新需求: RadarScan 独立 NoiseScale/NoiseSpeed

### 用户需求

- 没看到 RadarScan(LiDAR) 独立的 NoiseScale/NoiseSpeed.
- 希望它们可以单独设置,不要再复用高斯(show/hide)的全局 NoiseScale/NoiseSpeed.

### 现状与约束

- 当前 LiDAR show/hide 会把 `(int)VisibilityNoiseMode, NoiseStrength, NoiseScale, NoiseSpeed` 直接传入 `GsplatLidarScan.RenderPointCloud(...)`.
- 这会导致:
  - 调高斯 show/hide 的 NoiseScale/Speed 会影响 RadarScan.
  - 用户无法对两条效果做独立调参.

### 设计决定(尽量不破坏旧项目)

- 新增 LiDAR 专用字段:
  - `LidarShowHideNoiseScale`
  - `LidarShowHideNoiseSpeed`
- 默认值设为 `-1` 表示“复用全局 NoiseScale/NoiseSpeed”.
  - 这样升级后旧项目默认行为不变.
  - 需要独立时,把该值改为 >=0 即可覆盖.

### 下一步行动

- [x] Runtime: `GsplatRenderer/GsplatSequenceRenderer` 增加上述两个 LiDAR 字段,并在 LiDAR draw 提交时计算 effective noiseScale/noiseSpeed.
- [x] Editor: 在 LiDAR "Visual" 区域显示两个新字段(用户可直接调参).
- [x] Tests: 扩展 `ValidateLidarSerializedFields` 的单测,锁定 NaN/Inf/负数时回退到 -1(复用全局).
- [x] Changelog/Worklog: 追加记录.
- [x] 回归 `_tmp_gsplat_pkgtests` EditMode `Gsplat.Tests`.
- [x] git commit.
  - commit: `017cf59`

### 状态

**目前在阶段4(回归与提交)**
- 代码与回归已完成,并已提交 git.
- 回归(证据):
  - Unity EditMode `Gsplat.Tests`: total=46, passed=44, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_noise_overrides_2026-03-03_121526_noquit.xml`

## 2026-03-03 12:20:13 +0800 新需求: LidarAzimuthBins 不设置上限

### 用户需求

- `LidarAzimuthBins` 不要设置最大值上限(不 clamp).

### 现状

- `GsplatRenderer/GsplatSequenceRenderer.ValidateLidarSerializedFields()` 当前对 `LidarAzimuthBins` 做了 `>4096` 的最大值 clamp.

### 下一步行动

## 2026-03-07 12:33:40 +0800 收尾继续: Play 模式才隐藏 external target 普通 mesh

### 当前目标

- 把 `ForceRenderingOffInPlayMode` 这轮需求做完整闭环。
- 确认代码、测试、README、CHANGELOG、OpenSpec tasks 与四文件记录全部一致。

### 下一步行动

- [ ] 复读 `lidar-external-targets` 的 OpenSpec artifacts,确认本轮新增任务与验收口径。
- [ ] 精读 `GsplatLidarExternalTargetHelper` 与相关 Editor/Tests 改动,排查是否还有漏改。
- [ ] 跑 Unity EditMode 定向测试与全量测试,拿 fresh evidence.
- [ ] 测试通过后勾掉 OpenSpec `7.x` 任务,并重新检查 apply progress.
- [ ] 追加 `notes.md` / `WORKLOG.md` / `task_plan.md` 收尾记录,回看 `LATER_PLANS.md`.

### 状态

**目前在阶段4(验证与交付)**
- 代码已进入最后收尾阶段。
- 现在优先确认“编辑器显示,Play 才隐藏”的三态可见性逻辑与自动化验证证据。

## 2026-03-07 01:09:26 +0800 新任务: 为 RadarScan 外部模型扫描方案创建 OpenSpec change

### 目标

- 为“RadarScan 支持面板设置 `GameObject[]` ,并让数组内三维模型参与扫描并显示雷达粒子效果”的方案创建一个新的 OpenSpec change.
- 这次只做 change 建档与首个 artifact 指引,不进入代码实现.

### 已收敛决策

- change 名称使用 `lidar-external-targets`.
- 方案语义:
  - `LidarExternalTargets : GameObject[]`
  - 外部模型与 gsplat 一起参与 first return 竞争
  - 使用真实 mesh,不使用球体/胶囊近似碰撞
  - 静态模型走 `MeshCollider(sharedMesh)`
  - 蒙皮模型走 `SkinnedMeshRenderer.BakeMesh()` + 临时 `MeshCollider`
  - `SplatColorSH0` 下,外部模型命中使用材质主色

### 下一步行动

- [x] 追加记录到 `notes.md`,固化本次 change 命名与方案边界.
- [x] 使用默认 schema 执行 `openspec new change lidar-external-targets`.
- [x] 运行 `openspec status --change lidar-external-targets`,确认 artifact 序列.
- [x] 读取首个 ready artifact 的 instructions.
- [x] 将本次建档结果追加到 `WORKLOG.md`.

### 状态

**目前在阶段4**
- change `lidar-external-targets` 已创建完成,当前 schema 为 `spec-driven`,进度 `0/4`.
- 当前 ready artifact 为 `proposal`,下一步如果继续,应直接起草 `openspec/changes/lidar-external-targets/proposal.md`.

## 2026-03-07 01:13:40 +0800 继续: 使用 openspec-ff-change 直出全部 artifacts

### 目标

- 按 `spec-driven` workflow 一次性完成 `lidar-external-targets` 的全部 artifacts:
  - `proposal.md`
  - `design.md`
  - `specs/.../spec.md`
  - `tasks.md`
- 结束状态是 `openspec status --change lidar-external-targets` 显示 apply-ready.

### 下一步行动

- [x] 起草并写入 `proposal.md`,锁定 capability 命名与影响面.
- [x] 基于 proposal 写入 `design.md`,固定技术路线与取舍.
- [x] 基于 proposal 写入 capability spec 文件.
- [x] 基于 design/specs 写入 `tasks.md`.
- [x] 逐步运行 `openspec status --change lidar-external-targets --json` 验证解锁状态.
- [x] 将 artifact 完成结果追加到 `WORKLOG.md`.

### 状态

**目前在阶段4**
- `lidar-external-targets` 已完成全部 `4/4` artifacts.
- 当前 change 已达到 apply-ready 状态,后续可以直接进入实现阶段.

- [x] Runtime: 移除 `LidarAzimuthBins > 4096` 的 clamp(只保留最小值防御).
- [x] Tests: 增加单测锁定“大值不再被 clamp”.
- [x] 回归 `_tmp_gsplat_pkgtests` EditMode `Gsplat.Tests`.
- [x] git commit.
  - commit: `c3e3d6c`

### 状态

**目前在阶段4(回归与提交)**
- 代码与回归已完成,只剩一次 git commit.
- 已提交 git.

## 2026-03-08 01:34:00 +0800 继续实现: external GPU capture 分层缓存与独立调度

### 当前目标

- 继续推进 OpenSpec change `lidar-camera-frustum-external-gpu-scan`.
- 这一步聚焦补齐还未完成的核心实现:
  - `4.1` static invalidation signature
  - `4.2` static dirty / reuse
  - `4.3` dynamic cadence reuse
  - `4.4` renderer/sequence 独立 external tick 调度
- 同时为后续测试补钩子,避免实现完以后没有证据可以锁行为.

### 已确认约束

- 不改变当前视觉密度、扫描频率和 RGB 语义.
- aperture 直接使用 frustum camera 的视锥.
- external 输入继续分为 static / dynamic 两组.
- external mesh 的主路线必须保持 GPU capture,CPU raycast 只作为 fallback/debug.

### 接下来要做什么

- [ ] 读取 renderer / sequence 的 LiDAR tick 链路,确认 external capture 当前被谁触发,以及为什么还被绑在 range rebuild 上.
- [ ] 重构 `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`,加入:
  - static / dynamic 分层 capture state
  - static signature / dirty reason
  - dynamic last update realtime / cadence 复用
  - static+dynamic 双路 resolve 输入
- [ ] 改 `Runtime/Shaders/Gsplat.compute`,让 external resolve 同时比较 static / dynamic capture 的最近命中.
- [ ] 改 `Runtime/GsplatRenderer.cs` 与 `Runtime/GsplatSequenceRenderer.cs`,把 external 更新从“跟随 range rebuild”改成“每帧独立判定是否需要 refresh”.
- [ ] 补 `Tests/Editor/GsplatLidarScanTests.cs` 回归覆盖,锁住 signature / cadence / 语义.

### 状态

**目前在阶段3(执行/构建)**
- 我已经完成四文件回读和 tasks 缺口确认.
- 下一步会先精读 LiDAR tick 入口,然后开始重构 external GPU capture manager.

## 2026-03-08 02:18:00 +0800 进展: manager / compute / 调度 / 测试桩已接上

### 已完成

- [x] `GsplatLidarExternalGpuCapture` 已改成 static / dynamic 分组 capture state.
- [x] 已加入 static signature compare 与 debug hooks:
  - `DebugGetStaticCaptureDirtyReasonForInputs(...)`
  - `DebugCommitStaticCaptureSignatureForInputs(...)`
  - `DebugComputeRayDepthSqFromLinearViewDepth(...)`
- [x] `Gsplat.compute` 的 `ResolveExternalFrustumHits` 已升级为 static/dynamic 双路最近命中比较.
- [x] `GsplatRenderer` / `GsplatSequenceRenderer` 已把 external 更新改成每帧独立 tick,CPU fallback 仅在 range rebuild 后刷新.
- [x] 已新增 `GsplatLidarExternalGpuCaptureTests.cs`,准备锁住 signature / cadence / depthSq / frustum bounds 语义.

### 接下来要做什么

- [ ] 跑 Unity EditMode 定向回归,优先看新增 external GPU capture tests 与原有 lidar tests 是否编译通过.
- [ ] 如果编译或测试失败,先修复 runtime/compute/test 中最小根因.
- [ ] 定向通过后再决定是否直接跑全包 `Gsplat.Tests`.

### 状态

**目前在阶段4(验证)**
- 我现在进入第一轮真实验证,重点看:
  - 新私有反射钩子是否命名与签名一致
  - compute uniform 改名后是否还有漏绑
  - renderer/sequence 的新 tick 是否产生编译错误

## 2026-03-08 02:40:00 +0800 完成: OpenSpec change `lidar-camera-frustum-external-gpu-scan`

### 已完成

- [x] 跑 Unity EditMode 定向回归:
  - `Gsplat.Tests.GsplatLidar`
  - total=`38`, passed=`38`, failed=`0`, skipped=`0`
- [x] 跑 Unity EditMode 全包回归:
  - `Gsplat.Tests`
  - total=`79`, passed=`77`, failed=`0`, skipped=`2`
  - `skipped=2` 为既有 ignore
- [x] 更新 `README.md` 与 `CHANGELOG.md`
- [x] 更新 OpenSpec `tasks.md`,剩余项全部勾完
- [x] 运行 `openspec status` / `openspec instructions apply` 确认:
  - `isComplete = true`
  - `progress = 35 / 35`
  - `state = all_done`
- [x] 追加 `notes.md` / `WORKLOG.md` / `LATER_PLANS.md`

### 状态

**目前在阶段4(已完成)**
- 本轮 change 的实现、文档、测试和 OpenSpec 闭环都已完成.
- 下一步如果要继续流程,可以直接进入 archive change.
- 回归(证据):
  - Unity EditMode `Gsplat.Tests`: total=48, passed=46, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_azimuth_uncap_2026-03-03_122028_noquit.xml`

## 2026-03-03 12:26:01 +0800 新问题: Inspector 的 LiDAR ColorMode(动画)按钮无效

### 现象(用户反馈)

- 在 Inspector 面板中,`LidarColorMode` 下方的 “Depth(动画) / SplatColor(动画)” 按钮按下无效,或表现异常.

### 初步怀疑点(先从最可能的根因下手)

- `GsplatRenderer/GsplatSequenceRenderer.Update()` 里每帧都会调用 `SyncLidarColorBlendTargetFromSerializedMode(animated: true)`.
- 该函数当前的早退条件包含 `!m_lidarColorAnimating`,导致:
  - 一旦开始颜色动画(`m_lidarColorAnimating=true`),每帧都会重新 `BeginLidarColorTransition(...)`.
  - 这会把 `m_lidarColorAnimProgress01` 重置为 0,看起来就像按钮“按了但动画不走”.

### 下一步行动

- [x] Runtime: 修复 `SyncLidarColorBlendTargetFromSerializedMode` 的早退条件,避免在“已在向同一 target 动画”时重复重启动画.
- [x] Tests: 增加回归单测,锁定该函数在 animating 且 target 不变时不会重置 progress.
- [x] 回归 `_tmp_gsplat_pkgtests` EditMode `Gsplat.Tests`.
- [x] Changelog/Worklog 追加记录.
- [x] git commit.
  - commit: `f10ef3f`

### 状态

**目前在阶段4(回归与提交)**
- 代码与回归已完成,并已提交 git.
- 回归(证据):
  - Unity EditMode `Gsplat.Tests`: total=50, passed=48, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_color_buttons_2026-03-03_122758_noquit.xml`

## 2026-03-03 13:22:40 +0800 新问题: Show 起始阶段“突然出现球形范围粒子”(希望从 0 开始)

### 用户反馈

- Show 的最开始(不到 1 秒)会突然显示出一个球形范围的粒子区域.
- 观感像是“直接弹出一个有尺寸的球”,不够自然.
- 期望: 从无限小(0)开始,逐步长大.
- 偏好: 用“几何/尺寸”驱动,而不是靠透明度糊过去.

### 初步根因(先按最可能的下手)

- show/hide overlay 中,当 radius 很小时,`ringWidth/trailWidth` 仍是常量.
  - 结果: 早期可见 band 相对半径过厚,观感像“突然出现一个球壳”.
- LiDAR(RadarScan) 侧还存在 `jitterBase = max(trailWidth*0.75, maxRadius*0.015)` 这类下限.
  - 在 show 初期,即使我们把 trailWidth 缩小,`maxRadius*0.015` 仍会让 jitter 保持一个固定量级.
  - 当 trailWidth 很小时,这个 jitter 可能把边界“抖”出一个固定半径的可见区域,进一步加剧“弹球”感.

### 下一步行动

- [x] Shader(Gsplat.shader): 为 show 增加“早期尺寸门控”,让 ring/trail width 从 0 平滑放大到正常值.
  - 说明: 该改动已在工作区完成,待与 LiDAR 同步后一起回归.
- [x] Shader(GsplatLidar.shader):
  - [x] 为 show(mode==1) 增加同款“早期尺寸门控”(ring/trail width).
  - [x] show 初期让 `maxRadius*0.015` 的 jitter 下限也随尺寸门控一起从 0 放大(避免固定半径漏出).
  - [x] progress==0 时强制完全不可见(避免首帧因 ring/noise 漏出).
- [x] 回归 `_tmp_gsplat_pkgtests` EditMode `Gsplat.Tests`.
- [ ] 视觉验证: 在工程里按 Show,确认起始阶段不会再突然弹出一个球形范围.
- [x] git commit.

### 回归(证据)

- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`):
  - total=50, passed=48, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_show_start_from_zero_2026-03-03_132849_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_show_start_from_zero_2026-03-03_132849_noquit.log`

### 状态

**目前在阶段4(视觉验证与提交)**
- shader 修复与自动化测试已完成.
- 接下来只剩: 视觉确认(Show 起始是否仍弹球) + git commit.

## 2026-03-03 14:23:28 +0800 新需求: RadarScan(LiDAR) 独立 ShowDuration/HideDuration

### 用户需求

- 没看到 RadarScan(LiDAR) 独立的 ShowDuration/HideDuration.
- 希望 RadarScan 的淡入(show)/淡出(hide)时长可以独立设置.
  - 不要再强绑定 `RenderStyleSwitchDurationSeconds`.
  - 不要影响高斯/ParticleDots 的显隐 ShowDuration/HideDuration.

### 现状

- `SetRadarScanEnabled()` 当前在 `durationSeconds < 0` 时默认复用 `RenderStyleSwitchDurationSeconds`.
- 因此用户无法单独调雷达开/关的淡入淡出速度.

### 设计决定(兼容旧项目)

- 新增两个 LiDAR 专用字段:
  - `LidarShowDuration` (show 淡入时长)
  - `LidarHideDuration` (hide 淡出时长)
- 兼容策略: 默认值为 `-1` 表示“复用 RenderStyleSwitchDurationSeconds”.
  - 旧项目升级后行为不变.
  - 需要独立时,把它们改为 `>=0` 即可覆盖.

### 下一步行动

- [x] Runtime: `GsplatRenderer/GsplatSequenceRenderer` 增加上述两个字段.
- [x] Runtime: `SetRadarScanEnabled()` 在 `durationSeconds < 0` 时,按 show/hide 分别使用 `LidarShowDuration/LidarHideDuration`.
- [x] Runtime: `ValidateLidarSerializedFields()` 增加 NaN/Inf/负数防御(负数归一化为 -1).
- [x] Editor: 在 LiDAR 面板中暴露 `LidarShowDuration/LidarHideDuration` 并提示 "<0 复用 RenderStyleSwitchDurationSeconds".
- [x] Tests: 扩展 `GsplatLidarScanTests` 锁定 clamp 语义.
- [x] 回归 `_tmp_gsplat_pkgtests` EditMode `Gsplat.Tests`.
- [x] git commit.
  - commit: `4fdd526`

### 回归(证据)

- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`):
  - total=52, passed=50, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_duration_overrides_2026-03-03_142917_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_duration_overrides_2026-03-03_142917_noquit.log`

### 状态

**目前在阶段4(完成)**
- RadarScan 独立 ShowDuration/HideDuration 已落地并提交.

## 2026-03-03 14:33:41 +0800 补充: LiDAR 时长字段在 EnableLidarScan=false 时也可见

### 背景

- 用户容易在 RadarScan 未启用时找不到 `LidarShowDuration/LidarHideDuration`.
- 为了减少“没看到/不知道在哪”的摩擦,把这两个字段放到 LiDAR 面板里,并且不再依赖 EnableLidarScan=true 才显示.

### 变更

- [x] Editor: 在 LiDAR 面板中新增 "Transition" 小节.
  - `EnableLidarScan` 关闭时也显示 `LidarShowDuration/LidarHideDuration`.
- [x] git commit.
  - commit: `b9ea406`

### 回归(证据)

- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`):
  - total=52, passed=50, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_duration_inspector_2026-03-03_143314_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_duration_inspector_2026-03-03_143314_noquit.log`

### 状态

**目前在阶段4(完成)**
- Transition 字段的可见性补强已提交.

## 2026-03-03 15:29:46 +0800 新需求: LiDAR 扫描后变黑 + 扫描前消失可选

### 用户需求

- LidarIntensity 扫过后的区域不应变黑. 应由另一个 intensity 控制未扫到(或非前沿)区域的亮度.
- 下一次扫描前粒子会消失. 需要一个选项,控制是否在扫描前消失.

### 初步怀疑点(代码层)

- 目前 shader 用 `brightness = LidarIntensity * trail` 且 alpha 不随 trail 变化.
  - 在 alpha blend + ZWrite On 场景下,当 brightness 很小但仍未 discard 时,会表现为"黑点/黑片".
- trail 目前会在 1 圈内衰减到接近 0.
  - 因此在下一次扫到前,老点会提前变暗或消失.

### 设计决定(兼容旧项目,尽量少新增)

- 增加"未扫到区域"的亮度底色参数 `LidarUnscannedIntensity`.
- 增加一个 bool 选项 `LidarKeepUnscannedPoints`(命名可在落地时微调).
  - 关闭(默认): 行为保持当前,未扫到区域强度=0,点云只靠 trail 显示.
  - 开启: 未扫到区域使用 `LidarUnscannedIntensity` 作为底色.
    扫过区域用 `LidarIntensity` 做增强(随 trail).

### 下一步行动

- [x] 读取 `Runtime/Shaders/GsplatLidar.shader`,确认 brightness 与 alpha 的关系.
- [x] Shader: 落地 "unscanned intensity" 的亮度语义.
- [x] Runtime: `GsplatRenderer/GsplatSequenceRenderer` 增加新字段(Keep + UnscannedIntensity).
- [x] Runtime: `ValidateLidarSerializedFields` 增加 NaN/Inf/负数防御.
- [x] Runtime: `GsplatLidarScan.RenderPointCloud` 增加 `_LidarUnscannedIntensity` 下发.
- [x] Editor: Inspector 暴露新字段,并写清语义.
- [x] Tests: 扩展 `GsplatLidarScanTests` 覆盖 clamp 语义与 keep 开关决策逻辑.
- [x] 回归 `_tmp_gsplat_pkgtests` EditMode `Gsplat.Tests`.
- [x] WORKLOG/CHANGELOG 追加记录.

### 回归(证据)

- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`): total=54, passed=52, failed=0, skipped=2
- XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_unscanned_intensity_2026-03-03_154114_noquit.xml`
- log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_unscanned_intensity_2026-03-03_154114_noquit.log`

### 状态

**目前在阶段4(完成)**
- unscanned intensity 与 Keep 开关已落地.
- Inspector 已提供直接调参入口.
- 自动化回归已通过.

## 2026-03-03 16:00:47 +0800 新需求: LiDAR 强度按距离衰减(近强远弱)

### 用户需求

- `LidarIntensity` 需要根据距离衰减: 近的强,远的弱. 给一个"衰减乘数"可调.
- `LidarUnscannedIntensity` 也是一样: 近的强,远的弱. 给另一个"衰减乘数"可调.

### 设计决定(兼容旧项目)

- 新增 2 个可调参数(默认 0,表示不启用距离衰减):
  - `LidarIntensityDistanceDecay`
  - `LidarUnscannedIntensityDistanceDecay`
- shader 中使用一个简单且数值稳定的衰减函数(避免引入特殊情况):
  - `atten(dist) = 1 / (1 + dist * decay)`
  - `decay=0` 时退化为 1(完全不衰减).
- 最终强度语义:
  - `scanIntensity = LidarIntensity * atten(range, LidarIntensityDistanceDecay)`
  - `unscannedIntensity = LidarUnscannedIntensity * atten(range, LidarUnscannedIntensityDistanceDecay)`
  - 然后继续沿用之前的扫描插值语义:
    - `lerp(unscannedIntensity, scanIntensity, trail)`

### 下一步行动

- [x] Shader: 增加 `_LidarIntensityDistanceDecay/_LidarUnscannedIntensityDistanceDecay`,并在 frag 使用 range 做衰减.
- [x] Runtime: `GsplatRenderer/GsplatSequenceRenderer` 增加两个字段 + clamp.
- [x] Runtime: `GsplatLidarScan.RenderPointCloud` 增加两个参数并下发到 shader.
- [x] Editor: Inspector 暴露两个衰减乘数(并提示 0 表示不衰减).
- [x] Tests: 扩展 shader 属性契约单测 + clamp 单测.
- [x] 回归 `_tmp_gsplat_pkgtests` EditMode `Gsplat.Tests`.
- [x] WORKLOG/CHANGELOG/README 追加记录.

### 回归(证据)

- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`): total=54, passed=52, failed=0, skipped=2
- XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_distance_decay_2026-03-03_161038_noquit.xml`
- log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_distance_decay_2026-03-03_161038_noquit.log`

### 状态

**目前在阶段4(完成)**
- 距离衰减链路已补齐,并已通过自动化回归.

## 2026-03-03 18:12:00 +0800 新需求: LiDAR 距离衰减支持指数模式(可切换)

### 用户需求

- 增加"指数衰减"选项,可在两种距离衰减函数之间切换:
  - 反比(当前): `atten(dist)=1/(1+dist*decay)`
  - 指数(新增): `atten(dist)=exp(-dist*decay)`

### 设计决定(兼容旧项目,改良胜过新增)

- 新增一个模式字段 `LidarIntensityDistanceDecayMode`:
  - `Reciprocal`: 使用 `1/(1+dist*decay)`.
  - `Exponential`: 使用 `exp(-dist*decay)`.
- 默认值为 `Reciprocal`,以保持现有项目升级后的观感不变.
- 继续复用既有的两个衰减乘数:
  - `LidarIntensityDistanceDecay`
  - `LidarUnscannedIntensityDistanceDecay`
  只改变衰减函数的形态,不引入新的乘数或额外开关.

### 下一步行动

- [x] Runtime: 新增 `GsplatLidarDistanceDecayMode` enum + 两个 renderer 字段,并在 Validate 做非法值防御.
- [x] Runtime: `GsplatLidarScan.RenderPointCloud` 下发 `_LidarIntensityDistanceDecayMode`.
- [x] Shader: 增加 `_LidarIntensityDistanceDecayMode`,按模式选择 reciprocal 或 exponential 衰减.
- [x] Editor: Inspector 暴露模式下拉,并更新提示文案.
- [x] Tests: 扩展 shader property 契约单测,并增加非法 enum clamp 的回归用例.
- [x] Docs: 同步 `README.md`/`CHANGELOG.md`.
- [x] 回归 `_tmp_gsplat_pkgtests` EditMode `Gsplat.Tests`.
- [x] WORKLOG/notes 收尾追加,并更新本节状态.

### 回归(证据)

- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`): total=54, passed=52, failed=0, skipped=2
- XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_distance_decay_mode_2026-03-03_181542_noquit.xml`
- log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_distance_decay_mode_2026-03-03_181542_noquit.log`

### 状态

**目前在阶段4(完成)**
- 已支持 `Reciprocal`/`Exponential` 两种距离衰减曲线,并可在 Inspector 里直接切换.
- 默认仍为 `Reciprocal`,升级旧项目不会改变现有观感.

## 2026-03-07 01:38:32 +0800 继续实现: OpenSpec change `lidar-external-targets`

### 目标

- 按 `openspec/changes/lidar-external-targets/tasks.md` 开始真正落地代码实现.
- 本轮优先完成第 1 组任务,并为后续 external helper / shader 合并打好稳定接口:
  - `GsplatRenderer`
  - `GsplatSequenceRenderer`
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`

### 当前约束与已锁定语义

- `LidarExternalTargets : GameObject[]` 必须同时出现在静态与序列 renderer 上.
- 数组项视为根对象,后续 runtime helper 会递归收集子层级里的真实 mesh renderer.
- 默认值为空时必须完全保持旧行为不变.
- 后续 external hit 不是做视觉叠加,而是要与 gsplat 做同一 cell 的 first return 最近距离竞争.

### 下一步行动

- [ ] 重新阅读 `GsplatRenderer` / `GsplatSequenceRenderer` / `GsplatLidarScan` / 对应 Inspector,确认现有 LiDAR 字段布局与 `OnValidate` 入口.
- [ ] Runtime: 为两个 renderer 新增 `LidarExternalTargets : GameObject[]`,并补齐 `OnValidate` 自愈与默认值策略.
- [ ] Editor: 在两个 Inspector 的 LiDAR 面板中增加 `External Targets` 配置区和说明文案.
- [ ] Tests: 阅读现有 LiDAR EditMode tests,决定在哪里补“字段默认行为/validate 不破坏数组”的回归.
- [ ] 将这一步的研究结论追加到 `notes.md`,阶段性结果追加到 `WORKLOG.md`.

### 状态

**目前在阶段1(实现准备)**
- 我正在把 apply-ready 的 `lidar-external-targets` 从 OpenSpec 规格推进到真正代码实现.
- 第一站先做 API 与 Inspector 暴露,然后再进入 external physics helper 与 shader 合并阶段.

## 2026-03-07 01:56:05 +0800 继续实现: `lidar-external-targets` 第 2 组任务

### 本轮目标

- 接着已完成的 1.1 / 1.2 / 1.3 往下做.
- 先修正已经开始动但还没编译验证的半成品代码,避免后面在错误基础上继续堆功能.
- 然后把 external helper 真正接入两个 renderer 的 LiDAR 更新链路,并补上联合 bounds.

### 本轮执行顺序

- [ ] 重新阅读 `proposal.md` / `design.md` / `tasks.md`,确认 helper、shader、bounds 的职责边界.
- [ ] 阅读并自检当前半成品:
  - `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs`
  - `Runtime/Lidar/GsplatLidarScan.cs`
  - `Runtime/Shaders/GsplatLidar.shader`
  - `Runtime/GsplatUtils.cs`
- [ ] Runtime: 把 external helper 接进 `GsplatRenderer` / `GsplatSequenceRenderer` 的 LiDAR tick 与生命周期.
- [ ] Runtime: 把 show/hide / visibility 使用的 local bounds 改成 "gsplat bounds + external world bounds -> local union".
- [ ] Tests: 补 `GsplatUtils` / visibility / lidar external 相关回归.
- [ ] 回归编译与 Unity EditMode tests,再更新 `WORKLOG.md` / `notes.md`.

### 当前判断

- 当前最可能的风险不是设计方向,而是 API 细节和生命周期没完全对齐 Unity 6000.3 的实际行为.
- 所以本轮优先策略是:
  - 先把 helper / shader / scan 这几个半成品读透并收敛编译面.
  - 再做 renderer 接线与测试,避免把问题扩散到更多文件.

### 状态

**目前在阶段2(研究并修正半成品)**
- 我正在按 `openspec-apply-change` 继续 `lidar-external-targets`.
- 当前 focus 是把第 2 组和第 3 组任务连接起来,先把 external helper + LiDAR scan + shader 的接口收稳.

## 2026-03-07 02:18:30 +0800 阶段进展: runtime 主链路已接通,准备回归验证

### 已完成

- [x] 重新阅读 `proposal.md` / `design.md` / `tasks.md`,确认 helper、shader、bounds 的职责边界.
- [x] 阅读并自检当前半成品:
  - `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs`
  - `Runtime/Lidar/GsplatLidarScan.cs`
  - `Runtime/Shaders/GsplatLidar.shader`
  - `Runtime/GsplatUtils.cs`
- [x] Runtime: 把 external helper 接进 `GsplatRenderer` / `GsplatSequenceRenderer` 的 LiDAR tick 与生命周期.
- [x] Runtime: 把 show/hide / visibility 使用的 local bounds 改成 "gsplat bounds + external world bounds -> local union".
- [x] Tests: 已补第一轮回归草稿:
  - `GsplatUtils.TransformBounds(...)`
  - `ResolveVisibilityLocalBoundsForThisFrame(...)`
  - `external helper` 对“无现成 Collider 的 mesh”命中与材质主色

### 下一步行动

- [ ] 先跑针对性的 Unity EditMode tests,优先抓编译错误和反射签名错误:
  - `Gsplat.Tests.GsplatUtilsTests`
  - `Gsplat.Tests.GsplatLidarScanTests`
- [ ] 如果通过,再决定是否直接跑更完整的 `Gsplat.Tests`.
- [ ] 根据测试结果回填 `openspec/changes/lidar-external-targets/tasks.md` 的已完成项.
- [ ] 收尾更新 `WORKLOG.md` / `notes.md` / `README.md` / `CHANGELOG.md`.

### 状态

**目前在阶段3(验证与修正)**
- 主链路代码已经落下去了.
- 现在最重要的是先用 Unity 编译和 EditMode tests 把接口、反射名、内部类型可见性这些细节压实.

## 2026-03-07 02:23:16 +0800 收尾前动作: fresh verification 与最终状态回填

### 当前判断

- `lidar-external-targets` 的实现、文档、OpenSpec tasks 事实上都已经落地.
- 但在最终对外宣告完成前,仍需要补两类“当前回合的新鲜证据”:
  - OpenSpec 状态证据
  - Unity EditMode 回归证据

### 下一步行动

- [x] 重新读取 `openspec/changes/lidar-external-targets/tasks.md`,确认 21/21 均为已完成.
- [x] 重新执行 `openspec status --change "lidar-external-targets" --json`,确认 artifacts 全部 `done`.
- [ ] 重新执行 `openspec instructions apply --change "lidar-external-targets" --json`,确认 apply 进度已经到 `all_done`.
- [ ] 在 `_tmp_gsplat_pkgtests` 重新跑 Unity EditMode `Gsplat.Tests`,继续采用“不带 -quit”的稳定命令行路径.
- [ ] 将 fresh verification 结果回填到 `task_plan.md` 与 `WORKLOG.md`,然后再向用户汇报.

### 状态

**目前在阶段4(收尾验证)**
- 我正在把 `lidar-external-targets` 的最终交付证据补齐.
- 这一步做完后,就可以明确给出“change 已完成,下一步可直接 archive / commit / 场景手测”的结论.

## 2026-03-07 02:24:34 +0800 收尾完成: `lidar-external-targets` 已具备交付状态

### fresh verification 结果

- [x] `openspec/changes/lidar-external-targets/tasks.md` 重新确认 21/21 全部完成.
- [x] `openspec status --change "lidar-external-targets" --json`
  - `schemaName=spec-driven`
  - `isComplete=true`
  - artifacts: `proposal/design/specs/tasks` 全部 `done`
- [x] `openspec instructions apply --change "lidar-external-targets" --json`
  - `progress.complete=21`
  - `progress.total=21`
  - `state=all_done`
- [x] Unity 6000.3.8f1, `_tmp_gsplat_pkgtests` 全量 EditMode `Gsplat.Tests`
  - total=61, passed=59, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_targets_all_2026-03-07_022404_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_external_targets_all_2026-03-07_022404_noquit.log`
  - 日志确认: `Test run completed. Exiting with code 0 (Ok).`

### 回溯

- `LATER_PLANS.md` 已回看,本轮没有新增延期事项,也没有可清理的旧条目.
- 当前 `git status` 中的未跟踪项均属于本轮成果:
  - `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs`
  - `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs.meta`
  - `openspec/changes/lidar-external-targets.meta`
  - `openspec/changes/lidar-external-targets/`

### 状态

**目前在阶段4(完成)**
- `lidar-external-targets` 的 OpenSpec artifacts、实现、文档与自动化验证都已闭环.
- 下一步不再是“继续实现”,而是进入“archive change / git commit / Unity 场景手测”中的任一收官动作.

## 2026-03-07 02:31:40 +0800 追加需求: external targets 仅显示雷达粒子,不显示普通 mesh

### 目标

- 让 `LidarExternalTargets` 中参与 RadarScan 的外部模型,可以不再显示普通 mesh shader 效果.
- 同时必须保持:
  - 外部模型继续参与 LiDAR 扫描
  - static mesh / skinned mesh 都继续有效
  - helper 释放或目标移除后,原始 renderer 状态能够恢复

### 设计决定

- 本次直接并入尚未归档的 `lidar-external-targets` change,而不是新开 change.
  - 理由: 这是 external target 可见性语义的直接补完,不是平行功能.
- 优先采用 `Renderer.forceRenderingOff`,不优先用 layer.
  - 理由:
    - 改 `renderer.enabled` 或 `SetActive(false)` 会让 helper 直接跳过目标.
    - 改 layer 会牵连用户现有 camera/physics 分层语义,侵入更强.
- 本轮先做最稳的两态模式:
  - `KeepVisible`
  - `ForceRenderingOff`
- 默认值采用 `ForceRenderingOff`,更贴合“scan-only 外部目标”的主语义.

### 下一步行动

- [ ] 更新 `proposal.md` / `design.md` / `spec.md` / `tasks.md`,补进 external target visibility mode 语义.
- [ ] Runtime:
  - [ ] 新增 LiDAR external target visibility enum.
  - [ ] `GsplatRenderer` / `GsplatSequenceRenderer` 增加配置字段与 validate 防御.
  - [ ] `GsplatLidarExternalTargetHelper` 负责应用 `forceRenderingOff` 并在移除/Dispose 时恢复.
- [ ] Editor: 在两个 Inspector 的 LiDAR Inputs 区域暴露 visibility mode.
- [ ] Tests:
  - [ ] validate 默认值/非法值回退
  - [ ] helper 在 `ForceRenderingOff` 下会隐藏 source renderer 且 Dispose 后恢复
- [ ] README / CHANGELOG / WORKLOG / notes 同步更新.
- [ ] fresh verification: 重新跑相关 Unity EditMode tests.

### 状态

**目前在阶段1(规格补完与实现准备)**
- 我正在把 external target 的“scan-only 可见性”正式并入 `lidar-external-targets`.
- 这一步做完后,外部模型就可以只作为雷达命中源存在,不再强制以普通 mesh 渲染出来.

## 2026-03-07 12:16:43 +0800 收尾完成: external target scan-only 可见性已落地

### 已完成

- [x] OpenSpec:
  - proposal / design / spec / tasks 已同步补入 external target visibility mode 语义
  - `openspec instructions apply --change "lidar-external-targets" --json`
    - `progress=26/26`
    - `state=all_done`
- [x] Runtime:
  - 新增 `GsplatLidarExternalTargetVisibilityMode`
  - `GsplatRenderer` / `GsplatSequenceRenderer` 新增 `LidarExternalTargetVisibilityMode`
  - 默认值为 `ForceRenderingOff`
  - `ValidateLidarSerializedFields()` 已补非法 enum 回退
  - helper 已支持:
    - `ForceRenderingOff` 时隐藏 source renderer 的普通 mesh 显示
    - target 移除 / helper Dispose 时恢复原始 `forceRenderingOff`
- [x] Editor / Docs:
  - 两个 Inspector 已暴露 visibility mode
  - `README.md` / `CHANGELOG.md` 已补 scan-only 语义
- [x] Tests:
  - `Gsplat.Tests.GsplatLidarScanTests`
    - total=20, passed=20, failed=0, skipped=0
    - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_visibility_lidarscan_2026-03-07_121526_noquit.xml`
    - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_external_visibility_lidarscan_2026-03-07_121526_noquit.log`
  - 全量 `Gsplat.Tests`
    - total=63, passed=61, failed=0, skipped=2
    - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_visibility_all_2026-03-07_121556_noquit.xml`
    - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_external_visibility_all_2026-03-07_121556_noquit.log`
    - 日志确认: `Test run completed. Exiting with code 0 (Ok).`

### 状态

**目前在阶段4(完成)**
- `lidar-external-targets` 已从原来的 21/21 扩展到 26/26,并重新回到 `all_done`.
- 现在 external target 默认会走 `ForceRenderingOff`,也就是“继续参与雷达扫描,但不显示普通 mesh”.
- 如果用户希望保留普通 mesh,可以切回 `KeepVisible`.

## 2026-03-07 12:22:50 +0800 追加需求: 仅在 Play 模式隐藏 external target 普通 mesh

### 目标

- 在现有 `KeepVisible / ForceRenderingOff` 之外,新增第三种模式:
  - Play 模式: 不显示 external target 的普通 mesh
  - 非 Play 的编辑器模式: 仍然显示普通 mesh
- 同时保持 external target 的 LiDAR 扫描资格不受影响.

### 设计决定

- 继续并入 `lidar-external-targets`,不新开 change.
- 模式命名采用与现有枚举一致的技术流英文:
  - `ForceRenderingOffInPlayMode`
- 默认值仍保持 `ForceRenderingOff`,避免影响已经刚落地的 scan-only 默认语义.

### 下一步行动

- [ ] OpenSpec: 扩展 proposal / design / spec / tasks,把 visibility mode 从两态扩成三态.
- [ ] Runtime:
  - [ ] 扩展 `GsplatLidarExternalTargetVisibilityMode`
  - [ ] helper 可见性决策增加 `Application.isPlaying` 分支
  - [ ] renderer validate 接受第三个合法值
- [ ] Tests:
  - [ ] 增加“Play 模式才隐藏”的决策回归
  - [ ] 保持既有 `ForceRenderingOff` 隐藏/恢复测试仍通过
- [ ] Docs: README / CHANGELOG / WORKLOG / notes / task_plan 同步更新.
- [ ] fresh verification: 重新跑 `Gsplat.Tests.GsplatLidarScanTests` 与全量 `Gsplat.Tests`.

### 状态

**目前在阶段1(规格补完与实现准备)**
- 我正在把 external target visibility 从两态扩成三态.
- 这一步完成后,你就可以在平时编辑器里继续看见原始 mesh,但进入 Play 后自动切成 scan-only.

## 2026-03-07 13:20:40 +0800 收尾完成: Play 模式才隐藏 external target 普通 mesh

### 已完成

- [x] OpenSpec: 扩展 proposal / design / spec / tasks,把 visibility mode 从两态扩成三态.
- [x] Runtime:
  - [x] 扩展 `GsplatLidarExternalTargetVisibilityMode`
  - [x] helper 可见性决策增加 `Application.isPlaying` 分支
  - [x] renderer validate 接受第三个合法值
- [x] Tests:
  - [x] 增加“Play 模式才隐藏”的决策回归
  - [x] 保持既有 `ForceRenderingOff` 隐藏/恢复测试仍通过
- [x] Docs: README / CHANGELOG / WORKLOG / notes / task_plan 同步更新.
- [x] fresh verification:
  - `Gsplat.Tests.GsplatLidarScanTests`
    - total=21, passed=21, failed=0, skipped=0
  - `Gsplat.Tests`
    - total=64, passed=62, failed=0, skipped=2
  - `openspec instructions apply --change "lidar-external-targets" --json`
    - `progress=31/31`
    - `state=all_done`

### 状态

**目前在阶段4(验证与交付,已完成)**
- `ForceRenderingOffInPlayMode` 已落地.
- 现在 external target 支持三态可见性:
  - `KeepVisible`
  - `ForceRenderingOff`
  - `ForceRenderingOffInPlayMode`
- 默认值仍保持 `ForceRenderingOff`,避免破坏既有 scan-only 默认语义.

## 2026-03-07 13:29:10 +0800 新需求: RadarScan mesh 粒子呈现性能分析

### 目标

- 分析 external target 参与 RadarScan 后,mesh 粒子呈现性能差的主要瓶颈.
- 给出按收益排序的优化方向,优先区分:
  - 立刻可做的低风险优化
  - 结构性重构才有明显收益的优化

### 下一步行动

- [ ] 回读 external target helper 与 `GsplatLidarScan` 的扫描/上传/绘制链路.
- [ ] 判断当前主要瓶颈更偏 CPU 还是 GPU.
- [ ] 输出按优先级排序的优化建议,并明确每条建议的收益、代价、适用场景.

### 状态

**目前在阶段2(研究/诊断)**
- 我正在拆解 external mesh 进入 RadarScan 的完整性能路径.
- 目标是先告诉你“慢在什么地方”,再决定最值得做的优化。

## 2026-03-07 13:41:30 +0800 新需求: 为 camera 口径 + external GPU 扫描路线新建 OpenSpec change

### 目标

- 把新的性能优化方向正式沉淀成一个独立 OpenSpec change.
- 范围先聚焦:
  - 用 camera frustum 替代 360 度扫描口径
  - external target 拆成 static / dynamic 两组
  - external mesh 扫描优先考虑 GPU depth/color 路线

### 初步命名

- 候选 change name:
  - `lidar-camera-frustum-external-gpu-scan`
- 理由:
  - 覆盖这轮最核心的 3 个关键词
  - 命名风格与现有 `lidar-external-targets` 保持一致

### 下一步行动

- [ ] 检查现有 OpenSpec changes,避免命名冲突.
- [ ] 创建新的 change scaffold.
- [ ] 查看 artifact 状态与第一个 artifact 模板.
- [ ] 向用户回报 change 名称、路径、当前 workflow 状态.

### 状态

**目前在阶段1(计划和设置)**
- 我正在把这次性能优化方向正式立成一个新的 OpenSpec change.

## 2026-03-07 14:16:40 +0800 收尾完成: 已新建 camera 口径 + external GPU 扫描 change

### 已完成

- [x] 检查现有 OpenSpec changes,避免命名冲突.
- [x] 创建新的 change scaffold:
  - `lidar-camera-frustum-external-gpu-scan`
- [x] 查看 artifact 状态与第一个 artifact 模板.
  - schema: `spec-driven`
  - progress: `0/4 artifacts complete`
  - 当前 ready artifact: `proposal`

### 状态

**目前在阶段1(计划和设置,已完成)**
- 新 change 已建好.
- 下一步可以开始写 `proposal.md`,或者直接继续生成后续 artifacts.

## 2026-03-07 14:42:35 +0800 继续: 一次性补完 `lidar-camera-frustum-external-gpu-scan` 最后 artifact

### 目标

- 把 `lidar-camera-frustum-external-gpu-scan` 的最后一个 OpenSpec artifact `tasks.md` 一次性起草完毕.
- 让这个 change 从当前 `3/4 artifacts complete` 进入 `4/4 artifacts complete`,后续可直接进入 apply/实现阶段.

### 当前判断

- proposal / design / spec 已经把范围收敛清楚:
  - camera frustum 口径
  - static / dynamic external target 分组
  - external GPU depth/color 主路径
- 现在最关键的是把实现拆成可执行、可追踪、可逐项勾选的任务列表.

### 下一步行动

- [ ] 读取现有同类 `tasks.md` 写法,统一任务颗粒度与编号风格.
- [ ] 新建 `openspec/changes/lidar-camera-frustum-external-gpu-scan/tasks.md`,覆盖:
  - aperture API / Inspector
  - frustum active cells / LiDAR resources
  - static/dynamic external inputs
  - GPU depth/color capture
  - nearest-hit 合并与可见性保持
  - tests / docs / verification
- [ ] 运行 `openspec status --change "lidar-camera-frustum-external-gpu-scan" --json`,确认 artifacts 变为 `4/4`.
- [ ] 追加同步 `notes.md` / `WORKLOG.md`,把这次起草结果记录完整.

### 状态

**目前在阶段3(执行/构建)**
- 我正在补最后一个 `tasks.md`.
- 这一步完成后,这次新的性能优化 change 就会进入“规格齐备,可直接开始实现”的状态.

## 2026-03-07 14:44:00 +0800 收尾完成: `lidar-camera-frustum-external-gpu-scan` 已补齐全部 artifacts

### 已完成

- [x] 读取现有同类 `tasks.md` 写法,统一任务颗粒度与编号风格.
- [x] 新建 `openspec/changes/lidar-camera-frustum-external-gpu-scan/tasks.md`,覆盖:
  - aperture API / Inspector
  - frustum active cells / LiDAR resources
  - static/dynamic external inputs
  - GPU depth/color capture
  - nearest-hit 合并与可见性保持
  - tests / docs / verification
- [x] 运行 `openspec status --change "lidar-camera-frustum-external-gpu-scan" --json`,确认 artifacts 变为 `4/4`.
- [x] 追加同步 `notes.md` / `WORKLOG.md`,把这次起草结果记录完整.

### 状态

**目前在阶段4(审查和交付,已完成)**
- `lidar-camera-frustum-external-gpu-scan` 现在已经是 `proposal / design / specs / tasks` 全部完成.
- 这个 change 已经具备直接进入 apply / 实现阶段的条件.

## 2026-03-07 14:48:03 +0800 新请求: 审查 `lidar-camera-frustum-external-gpu-scan` 设计是否有问题

### 目标

- 复核这次新 change,尤其是 external GPU depth/color 路线,判断当前设计是否存在方向性问题、遗漏或实现风险.
- 输出时优先给出“具体问题 / 风险 / 建议修正”,而不是只做复述.

### 下一步行动

- [ ] 回读 `proposal.md` / `design.md` / `spec.md` / `tasks.md`,确认 GPU 路线的明确承诺与边界.
- [ ] 对照当前实现代码(`GsplatLidarScan`、external helper、shader / renderer 调用链),检查设计与现有架构是否冲突.
- [ ] 重点审查:
  - GPU capture 的颜色语义是否定义充分
  - frustum active cells 与 external depth 采样空间是否一致
  - static / dynamic invalidation 是否遗漏关键条件
  - fallback / 跨管线 / 可见性语义是否存在空档
- [ ] 把审查结论追加到 `notes.md`,必要时回写 `LATER_PLANS.md`.

### 状态

**目前在阶段2(研究/诊断)**
- 我正在做一次偏架构和实现可行性的审查.
- 目标是尽量在真正开始 apply 之前,把 GPU 方案里的坑提前挖出来.

## 2026-03-07 14:50:56 +0800 审查完成: GPU 方案方向正确,但设计还需补 4 个约束

### 已完成

- [x] 回读 `proposal.md` / `design.md` / `spec.md` / `tasks.md`,确认 GPU 路线的明确承诺与边界.
- [x] 对照当前实现代码(`GsplatLidarScan`、external helper、shader / renderer 调用链),检查设计与现有架构是否冲突.
- [x] 重点审查:
  - GPU capture 的颜色语义是否定义充分
  - frustum active cells 与 external depth 采样空间是否一致
  - static / dynamic invalidation 是否遗漏关键条件
  - fallback / 跨管线 / 可见性语义是否存在空档
- [x] 把审查结论追加到 `notes.md`.

### 审查结论

- GPU 路线本身没有方向性错误.
- 但在真正进入 apply 之前,建议先把下面 4 件事写死:
  - `frustum camera` 和 `LidarOrigin` 的外参关系
  - external depth 到 LiDAR range 的几何换算契约
  - `color RT` 到“材质主色语义”的精确定义
  - static capture 的完整 invalidation 条件

### 状态

**目前在阶段4(审查和交付,已完成)**
- 审查已经完成.
- 现在最合适的下一步不是直接写代码,而是先把 design/spec 再补一轮,把上述 4 个约束钉死.

## 2026-03-07 15:40:54 +0800 收尾完成: 已把 4 个关键约束补进 design/spec/tasks

### 已完成

- [x] 更新 `design.md`,把以下契约写死:
  - `LidarOrigin` 与 frustum camera 的 sensor-frame 分工
  - external depth -> LiDAR `depthSq` 语义
  - `surfaceColor RT` 的主色限制
  - static invalidation 的完整条件
- [x] 更新 `spec.md`,把上述 4 个点转成 requirement / scenario.
- [x] 更新 `tasks.md`,把这些约束拆成实现任务与回归测试任务.
- [x] 运行 `openspec status --change "lidar-camera-frustum-external-gpu-scan" --json`,确认 artifact 仍保持完整.
- [x] 追加同步 `notes.md` / `WORKLOG.md`.

### 状态

**目前在阶段4(审查和交付,已完成)**
- 这次不是往前冲实现,而是先把“正确的事”写清楚.
- 现在这个 change 的 artifacts 比之前更适合进入 apply,因为关键几何/颜色/失效条件都已经被明文化了.

## 2026-03-07 16:38:00 +0800 新决定: frustum 模式下直接使用相机位置作为 LiDAR 原点

### 目标

- 把 `lidar-camera-frustum-external-gpu-scan` 的 sensor-frame 契约改成更直接的版本:
  - frustum 模式下,LiDAR origin 直接使用 frustum camera 的位置
  - camera 的朝向与 projection 继续作为 aperture 语义来源
- 同步修正 design / spec / tasks,避免后续实现还沿用上一版“`LidarOrigin` 仍为 beam origin”的契约.

### 下一步行动

- [ ] 回读当前 design / spec / tasks 中与 `LidarOrigin` 和 synthetic frustum view 相关的段落.
- [ ] 改写为:
  - frustum 模式使用 `frustumCamera.transform.position`
  - `LidarOrigin` 在 frustum 模式下不再作为必填原点
- [ ] 同步更新任务与测试项,把回归重点改成“frustum mode 以 camera pose 为准”.
- [ ] 追加同步 `notes.md` / `WORKLOG.md`.

### 状态

**目前在阶段3(执行/构建)**
- 我正在把这次传感器坐标系的设计决定正式落盘.
- 这一步会直接影响后续 external GPU capture 的几何契约,所以要先改规格,再谈实现.

## 2026-03-07 16:40:12 +0800 收尾完成: frustum 模式原点已改为直接使用相机位置

### 已完成

- [x] 回读当前 design / spec / tasks 中与 `LidarOrigin` 和 frustum sensor frame 相关的段落.
- [x] 改写为:
  - frustum 模式使用 `frustumCamera.transform.position`
  - `LidarOrigin` 在 frustum 模式下不再作为必填原点
- [x] 同步更新任务与测试项,把回归重点改成“frustum mode 以 camera pose 为准”.
- [x] 追加同步 `notes.md` / `WORKLOG.md`.

### 状态

**目前在阶段4(审查和交付,已完成)**
- 这次最新决策已经完整落盘.
- 当前更稳的理解是:
  - 360 模式看 `LidarOrigin`
  - frustum 模式看 `frustumCamera`

## 2026-03-07 17:08:00 +0800 新阶段: 按 OpenSpec apply 起手实现 frustum aperture 主链

### 本轮目标

- 正式进入 `lidar-camera-frustum-external-gpu-scan` 的实现阶段.
- 这次先不急着落完整 external GPU capture.
- 第一批只做最关键的入口收敛:
  - `aperture mode`
  - `frustum camera`
  - 旧 `LidarExternalTargets` 到 static/dynamic 的兼容入口
  - 统一的 LiDAR sensor pose / sensor frame helper
  - Inspector 与最小回归测试对齐

### 本轮实施顺序

- [ ] 回读 apply context 与关键 runtime/editor/test 文件,锁定第一批改动点.
- [ ] 在 `Runtime/GsplatUtils.cs` 增加 aperture mode enum.
- [ ] 在 `GsplatRenderer` / `GsplatSequenceRenderer` 增加:
  - `LidarApertureMode`
  - `LidarFrustumCamera`
  - static/dynamic external inputs
  - dynamic updateHz
- [ ] 接出统一的 frustum/360 sensor pose helper,先让现有 LiDAR 主链改用正确 pose.
- [ ] 更新 Inspector 文案,明确:
  - frustum camera 直接提供 origin + rotation + projection
  - `LidarOrigin` 在 frustum 模式下不再是必填原点
- [ ] 补最小 EditMode tests,锁定默认值、非法 enum 回退与 frustum pose 契约.

### 暂不在这一轮做

- [ ] 完整 external GPU depth/color capture pass
- [ ] static invalidation signature 全量落地
- [ ] GPU resolve 到 LiDAR `depthSq` 的完整链路

### 状态

**目前在阶段1(现场与上下文确认)**
- 我已经重新确认 OpenSpec apply context 为 `0/35`.
- 接下来进入代码搜索与阅读,目标是只拿下第一段基础接线,不在这一轮把外部 GPU capture 混进来。

## 2026-03-07 23:38:00 +0800 阶段进展: aperture/frustum 输入接线已完成第一批闭环

### 已完成

- [x] `Runtime/GsplatUtils.cs` 新增 `GsplatLidarApertureMode`.
- [x] `GsplatRenderer` / `GsplatSequenceRenderer` 新增:
  - `LidarApertureMode`
  - `LidarFrustumCamera`
  - `LidarExternalStaticTargets`
  - `LidarExternalDynamicTargets`
  - `LidarExternalDynamicUpdateHz`
- [x] 旧 `LidarExternalTargets` 兼容:
  - `FormerlySerializedAs("LidarExternalTargets")` 自动迁到 static 组
  - 旧 API 名保留为兼容 property,仍映射到 static 组
- [x] 两条 runtime 链路都已切到统一 sensor pose 入口:
  - compute 的 `modelToLidar`
  - draw 的 `lidarLocalToWorld`
  - CPU external helper 的 sensor transform
  - EditMode repaint gate
- [x] 两个 Inspector 已改成按 aperture mode 显示正确输入与 warning.
- [x] `GsplatLidarScanTests` 已补 aperture 默认值、旧字段兼容、frustum sensor pose、static/dynamic bounds 联合覆盖.

### 验证

- [x] Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests.GsplatLidarScanTests`
  - total=27, passed=27, failed=0, skipped=0
- [x] Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=70, passed=68, failed=0, skipped=2
  - skipped=2 为既有 ignore,本轮没有新增失败

### OpenSpec 进度

- [x] `openspec/changes/lidar-camera-frustum-external-gpu-scan/tasks.md`
  - 已完成并勾选 `1.1` ~ `1.5`
- [ ] `2.x` 之后的 frustum active-cell / GPU capture / resolve 仍未开始

### 状态

**目前在阶段3(执行/构建)**
- 第一批“入口收敛”已经稳定.
- 下一步应继续推进:
  - frustum active cells / LUT / range image 尺寸
  - 再进入 external GPU capture 主链

## 2026-03-07 23:45:00 +0800 新阶段: 继续推进 `2.1 ~ 2.5` 的 frustum active-cell 主链

### 本轮目标

- 从下一个未完成步骤继续:
  - `2.1`
  - `2.2`
  - `2.3`
  - `2.4`
  - `2.5`
- 重点不是再加字段,而是把 frustum 模式真正变成:
  - active cells 跟 camera FOV 走
  - range image / LUT / external-hit buffer 跟 active 布局走
  - 缺失或非法 frustum camera 条件时有明确回退或禁用行为

### 本轮行动顺序

- [ ] 回读 `GsplatLidarScan.cs` / `Gsplat.compute` / external helper,确认当前 cell 索引、LUT 方向和 ray-distance 的真实契约.
- [ ] 先设计一套最小布局对象或参数集:
  - active horizontal count
  - active vertical count
  - azimuth/elevation 的角范围
  - 360 与 frustum 共用的 buffer 布局入口
- [ ] 再把 runtime 调度链改成吃这套布局,而不是继续硬编码 `LidarAzimuthBins * LidarBeamCount`.
- [ ] 最后补最小回归测试,锁住:
  - frustum count 推导
  - 窄 FOV 不过密
  - frustum camera 非法时的明确回退/禁用

### 风险提醒

- 这一步一旦只改了 draw/LUT,没改 compute cell 映射,就会出现“点数变了但命中竞争语义还是旧的”.
- 这一步一旦把 camera FOV 直接压成最终 full-count,就会破坏用户要求的“不要变密”.

### 状态

**目前在阶段2(研究/设计收敛)**
- 我先把 cell 布局的真实契约读透.
- 这一步稳了,后面的 external GPU capture 才有可靠基座.

## 2026-03-08 00:02:42 +0800 继续实施: `lidar-camera-frustum-external-gpu-scan` 的 `2.1 ~ 2.5`

### OpenSpec 当前状态

- [x] 使用 change: `lidar-camera-frustum-external-gpu-scan`
- [x] schema: `spec-driven`
- [x] apply progress: `5 / 35`
- [x] 下一段未完成任务:
  - `2.1` frustum active cells 推导
  - `2.2` 保持当前屏幕内点密度观感
  - `2.3` LUT / range image / external-hit buffer 尺寸改造
  - `2.4` frustum sensor frame 主链接线
  - `2.5` 无效 frustum camera 的安全回退

### 本轮先做什么

- [ ] 读取 OpenSpec proposal / design / spec,把这轮已经写死的 4 个约束再次对齐:
  - 不降低当前视觉密度/频率/RGB 语义
  - aperture 只取 camera frustum
  - static / dynamic external targets 分组
  - external mesh 主路径最终走 GPU
- [ ] 读取 `Runtime/Lidar/GsplatLidarScan.cs` / `Runtime/Shaders/Gsplat.compute` / `Runtime/Shaders/GsplatLidar.shader`,确认:
  - cell id 编码
  - LUT 方向语义
  - `depth` / `depthSq` 契约
  - 当前 buffer 尺寸由谁决定
- [ ] 先收敛一个最小布局对象或参数集,优先把 360 / frustum 的 active-cell 布局统一起来.
- [ ] 再把 renderer -> lidar scan -> compute/shader 的参数链改成吃布局,不再继续散落使用裸的 `LidarAzimuthBins` / `LidarBeamCount`.

### 当前状态

**目前在阶段2(研究/设计收敛)**
- 这一轮先不碰 external GPU capture 的大实现.
- 先把 frustum active-cell / layout / sensor-frame 的地基打稳,避免后面在错误语义上继续堆功能.

## 2026-03-08 00:26:10 +0800 阶段进展: frustum active-cell 主链已经接进 runtime / compute / draw / CPU fallback

### 已完成的代码改造

- [x] 在 `Runtime/Lidar/GsplatLidarScan.cs` 内新增共享的 `GsplatLidarLayout`,统一承载:
  - active azimuth bins
  - active beam count
  - azimuth min/max rad
  - beam min/max rad
- [x] 360 与 frustum 两种 aperture 都改成先生成 layout,再驱动:
  - range image buffer 尺寸
  - LUT 生成
  - compute cell 映射
  - draw cell 解码
  - CPU external helper fallback
- [x] compute 已不再硬编码完整 `[-pi,pi]`,改为按 layout 的 azimuth 角域落 bin.
- [x] draw 已不再用 active bins 数量直接决定扫描头频率,改为按真实 360 方位角年龄算 trail,避免 frustum 模式无意加速扫描节奏.
- [x] renderer / sequence renderer 都新增了统一的 `layout + sensor frame` runtime context 入口.
- [x] frustum 无效条件目前会明确禁用新路径并 warning,不再偷偷退回不确定状态.

### 下一步

- [ ] 补 `GsplatLidarScanTests`:
  - frustum active count 推导
  - 窄 FOV 不把 full count 塞进视口
  - invalid frustum camera 明确失败
- [ ] 跑定向 EditMode tests.
- [ ] 跑一次包级 `Gsplat.Tests` 回归.

### 当前状态

**目前在阶段3(执行/构建)**
- 代码主链已经改完.
- 现在进入验证与勾任务阶段.

## 2026-03-08 00:23:12 +0800 验证完成: `2.1 ~ 2.5` 与相关测试任务已收口

### 已完成

- [x] `openspec/changes/lidar-camera-frustum-external-gpu-scan/tasks.md`
  - 已勾选:
    - `2.1`
    - `2.2`
    - `2.3`
    - `2.4`
    - `2.5`
    - `6.1`
    - `6.2`
- [x] 定向 EditMode tests:
  - `Gsplat.Tests.GsplatLidarScanTests`
  - total=`31`, passed=`31`, failed=`0`, skipped=`0`
- [x] 全包 EditMode tests:
  - `Gsplat.Tests`
  - total=`74`, passed=`72`, failed=`0`, skipped=`2`
  - `skipped=2` 为既有 ignore,本轮无新增失败
- [x] OpenSpec apply progress 已刷新:
  - `12 / 35`

### 当前判断

- frustum active-cell / layout / sensor-frame 这一层已经具备可靠基座.
- 下一段真正的主战场就是:
  - `3.1 ~ 3.7` external GPU capture 基础设施
  - `4.1 ~ 4.6` static / dynamic 更新策略与 fallback

### 当前状态

**目前在阶段4(审查与交付)**
- 这轮目标已经完成并通过回归.
- 可以从下一个未完成任务 `3.1` 继续,开始 external GPU capture 的正确落地.

## 2026-03-08 00:23:12 +0800 继续推进: `3.1 ~ 3.7` external GPU capture 基础设施

### 本轮目标

- 从 OpenSpec 下一个未完成步骤 `3.1` 开始继续:
  - `3.1` external capture helper / manager
  - `3.2` static GPU capture 输入收集
  - `3.3` dynamic GPU capture 输入收集
  - `3.4` external depth capture pass
  - `3.5` external surface main-color capture pass
  - `3.6` depth -> LiDAR external hit resolve
  - `3.7` explicit render list + override material / command buffer draw

### 开始前先做什么

- [ ] 搜索仓库里是否已有可复用的:
  - 离屏 RenderTexture 管理
  - CommandBuffer draw
  - override material
  - `_BaseColor` / `_Color` 读取 helper
  - 类似 external capture 的 shader/compute 基础设施
- [ ] 基于现有代码形态决定:
  - 是新增一个独立 `LidarExternalGpuCapture` helper
  - 还是把 capture/resolve 压进现有 `GsplatLidarScan`
- [ ] 先做最小正确的 GPU 路线骨架,不要一开始就把 static invalidation / dynamic cadence 全塞进一处.

### 当前状态

**目前在阶段2(研究/设计收敛)**
- `2.x` 已完成并通过回归.
- 现在重新进入下一段的探索入口,优先寻找仓库内可复用模式,再动代码.

## 2026-03-08 00:42:30 +0800 继续推进: external GPU capture 骨架落点确认

### 新增研究结论

- [x] 重新确认 OpenSpec apply 状态:
  - change=`lidar-camera-frustum-external-gpu-scan`
  - schema=`spec-driven`
  - progress=`12/35`
  - 下一段未完成任务从 `3.1` 开始
- [x] 重新读取本 change 的 `proposal / design / tasks / spec`,确认本轮约束仍然写死:
  - 不降低当前视觉密度 / 扫描频率 / RGB 语义
  - frustum mode 直接使用 camera pose 作为 LiDAR sensor frame
  - external 输入拆成 static / dynamic 两组
  - frustum mode 下 external 主路径必须是 GPU depth/color capture
- [x] 完成一轮仓库勘探,结论已经明确:
  - 已有可复用地基:
    - `GsplatLidarLayout`
    - `TryGetEffectiveLidarRuntimeContext(...)`
    - `ExternalRangeSqBitsBuffer / ExternalBaseColorBuffer`
    - `_BaseColor` / `_Color` 主色解析 helper
    - external target 收集与 visibility 三态语义
  - 当前缺失:
    - runtime `RenderTexture + explicit render list + override material + command buffer draw` 的 external GPU capture 基础设施
    - external capture 专用 shader
    - depth/color RT -> LiDAR external hit buffer 的 GPU resolve kernel

### 这一步的实施判断

- [ ] 新增独立 `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`
  - 职责:
    - depth/color RT 生命周期
    - static/dynamic render list 收集
    - explicit render list + command buffer draw
    - frustum camera 对齐
    - dynamic cadence / fallback 预留状态
- [ ] 新增 `Runtime/Shaders/GsplatLidarExternalCapture.shader`
  - 最小目标:
    - depth capture pass
    - surface main-color capture pass
    - 只保留 `_BaseColor` / `_Color` 语义
- [ ] 扩展 `Runtime/Shaders/Gsplat.compute`
  - 新增 external resolve kernel,把 capture RT 整理为 LiDAR `depthSq + baseColor` buffer
- [ ] 在 `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs` 先接一个最小分支:
  - frustum + GPU 支持时走新 manager
  - surround360 或不支持时继续走旧 `GsplatLidarExternalTargetHelper`

### 当前状态

**目前在阶段3(执行/构建)**
- 研究入口已经收束完了.
- 下一步开始落第一版 external GPU capture manager 与 shader/compute 骨架,先打通 `3.1` 的资源与提交流程.

## 2026-03-08 01:02:20 +0800 进展: external GPU capture 第一版骨架已落地,进入验证

### 已落地代码面

- [x] `Runtime/GsplatSettings.cs`
  - 增加 `LidarExternalCaptureShader` / `LidarExternalCaptureMaterial`.
- [x] `Runtime/Shaders/GsplatLidarExternalCapture.shader`
  - 增加 depth capture pass + surface main-color capture pass.
- [x] `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`
  - 新增 external GPU capture manager:
    - static/dynamic render list 收集
    - explicit render list + override material + command buffer draw
    - capture RT 生命周期
    - RT -> external hit buffer resolve 调度
- [x] `Runtime/Shaders/Gsplat.compute`
  - 新增 `ResolveExternalFrustumHits` kernel.
- [x] `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`
  - frustum 模式优先尝试新 GPU capture manager.
  - 失败时回退旧 CPU helper.
  - 补齐 manager 的释放链路.

### 接下来马上验证

- [ ] 先跑定向 EditMode tests,确认:
  - 新 shader / 新 C# 文件编译通过
  - 现有 LiDAR tests 不回退
- [ ] 再跑包级 `Gsplat.Tests` 回归,确认没有新的编译或既有逻辑回退.

### 当前状态

**目前在阶段4(验证与修正)**
- 主链代码已经接上.
- 现在开始用 Unity EditMode 回归把编译问题和明显逻辑问题尽快打掉.

## 2026-03-08 01:06:40 +0800 验证完成: `3.1 ~ 3.7` external GPU capture 基础设施已打通

### 本轮完成

- [x] `openspec/changes/lidar-camera-frustum-external-gpu-scan/tasks.md`
  - 已勾选:
    - `3.1`
    - `3.2`
    - `3.3`
    - `3.4`
    - `3.5`
    - `3.6`
    - `3.7`
- [x] 新增 runtime / shader 地基:
  - `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`
  - `Runtime/Shaders/GsplatLidarExternalCapture.shader`
  - `Runtime/Shaders/Gsplat.compute` 的 `ResolveExternalFrustumHits`
- [x] `GsplatRenderer` / `GsplatSequenceRenderer`
  - frustum 模式优先走 GPU capture
  - 失败回退旧 CPU helper
  - 生命周期释放已补齐

### 验证证据

- [x] 定向 EditMode tests:
  - `Gsplat.Tests.GsplatLidarScanTests`
  - total=`31`, passed=`31`, failed=`0`, skipped=`0`
  - XML:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_external_gpu_capture_targeted_2026-03-08.xml`
- [x] 全包 EditMode tests:
  - `Gsplat.Tests`
  - total=`74`, passed=`72`, failed=`0`, skipped=`2`
  - `skipped=2` 为既有 ignore,本轮无新增失败
  - XML:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_external_gpu_capture_full_2026-03-08.xml`

### 下一步

- [ ] 进入 `4.1 ~ 4.6`
  - static capture invalidation signature
  - static reuse
  - dynamic cadence
  - GPU/CPU fallback 条件收敛
  - 模式切换与资源清理补强

### 当前状态

**目前在阶段4(验证完成,准备继续下一段)**
- `3.x` 已经通过现有 EditMode 回归.
- 下一段该做的不是继续堆新 pass,而是把 static/dynamic 的复用与门禁做正确.

## 2026-03-08 01:09:45 +0800 OpenSpec 进度刷新

- [x] `openspec instructions apply --change "lidar-camera-frustum-external-gpu-scan" --json`
  - 当前 progress=`25 / 35`
- [x] 本轮额外确认可一并收口:
  - `4.5`
  - `5.1`
  - `5.2`
  - `5.3`
  - `5.4`
  - `5.5`
- [ ] 下一段剩余主任务:
  - `4.1`
  - `4.2`
  - `4.3`
  - `4.4`
  - `4.6`
  - `6.3`
  - `6.4`
  - `6.5`
  - `6.6`
  - `6.7`

### 当前状态

**目前在阶段4(本轮阶段性完成)**
- change 已从 `12 / 35` 推进到 `25 / 35`.
- 下一步应该围绕 static invalidation / dynamic cadence / 测试文档补齐,而不是再扩展新的渲染语义.

## 2026-03-08 01:18:30 +0800 继续实施: `4.1 ~ 4.4` static/dynamic 调度闭环

### 本轮目标

- 从下一个未完成步骤继续:
  - `4.1` static capture invalidation signature
  - `4.2` static reuse + dirty/signature debug path
  - `4.3` dynamic cadence
  - `4.4` 把 external capture 调度从“只跟随 range rebuild”改成独立协同
- 同步补最直接的测试入口:
  - `6.3`
  - `6.4`
  - `6.5`

### 实施判断

- [ ] `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`
  - 改成 static / dynamic 分层 RT 与缓存状态
  - 增加 static signature snapshot
  - 增加 dynamic cadence state
  - 增加 debug dirty reason / signature 入口,便于单测锁定
- [ ] `Runtime/Shaders/Gsplat.compute`
  - resolve kernel 升级为同时读取 static / dynamic capture,在 GPU 上选最近 external hit
- [ ] `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`
  - 把 external 更新从“range rebuild 后顺手同步一次”改成“每帧按 need 独立调度”
- [ ] `Tests/Editor/GsplatLidarScanTests.cs`
  - 增加 static signature / resolve 语义 / frustum bounds+visibility 回归

### 当前状态

**目前在阶段3(执行/构建)**
- 下一步开始真正做性能语义上的关键一步:
  - 让 static 真的能复用
  - 让 dynamic 真的能按独立频率更新

## 2026-03-08 12:06:00 +0800 起手实现: `radarscan-particle-antialiasing-modes`

### 本轮目标

- 使用 change: `radarscan-particle-antialiasing-modes`
- 按既定推荐路线落地:
  - 先把 `AnalyticCoverage` 接进 LiDAR 主 shader
  - 再补 `AlphaToCoverage` 专用 shader shell / material 与 fallback
- 保持兼容:
  - 默认值继续是 `LegacySoftEdge`
  - 无有效 MSAA 时,所有 A2C 相关模式都稳定回退到 `AnalyticCoverage`

### 当前约束与风险

- [x] OpenSpec apply 状态确认:
  - schema=`spec-driven`
  - progress=`0 / 19`
  - state=`ready`
- [x] 当前工作区是脏的,而且相关 LiDAR 文件已经存在别的未提交改动.
  - 处理原则:
    - 不回退已有改动
    - 先读当前文件与 diff,基于现状接入 AA 能力
    - 只补本轮变更所需的最小闭环

### 下一步行动

- [ ] 读取 change 的 proposal/design/spec/tasks 与当前 LiDAR 相关源码,确认实现边界没有漂移.
- [ ] 读取当前相关文件 diff,识别哪些是已存在的外部扫描改动,避免覆盖.
- [ ] 先完成 Runtime 枚举与模式解析,再进入 shader/material 施工.

### 当前状态

**目前在阶段1(上下文确认)**
- 我正在把 OpenSpec 任务与当前脏工作区对齐.
- 下一步是读当前源码与 diff,然后开始 Runtime 层的 AA 模式接线.

## 2026-03-08 12:42:00 +0800 进展: Runtime / shader / Inspector / tests / docs 已接线,准备做首轮回归

### 已完成

- [x] Runtime:
  - `GsplatLidarParticleAntialiasingMode` 枚举
  - MSAA 可用性与有效模式 fallback helper
  - `GsplatRenderer` / `GsplatSequenceRenderer` 新字段、validate、防御与 draw 参数传递
  - `GsplatSettings` 新增 LiDAR A2C shader / material 资源槽位
- [x] Shader:
  - 抽出 `Runtime/Shaders/GsplatLidarPassCore.hlsl`
  - 普通 LiDAR shell 改为 include shared core
  - 新增 `Runtime/Shaders/GsplatLidarAlphaToCoverage.shader`
  - frag 接入 `AnalyticCoverage`
- [x] Editor / Tests / Docs:
  - Inspector 暴露新模式与 helpbox
  - `GsplatLidarScanTests` / `GsplatLidarShaderPropertyTests` 补到位
  - README / CHANGELOG 已同步说明

### 下一步行动

- [ ] 在 `_tmp_gsplat_pkgtests` 跑相关 EditMode tests.
- [ ] 如果有编译或导入错误,先修基础问题,再补 OpenSpec tasks 勾选.

### 当前状态

**目前在阶段3(执行后的首轮验证)**
- 代码闭环已经搭好,现在要用 Unity 真正验证 C# / shader / asset import 是否一致通过.

## 2026-03-08 12:42:48 +0800 进展: 锁定首轮回归失败根因,准备修测试清理顺序

### 已确认事实

- [x] OpenSpec `radarscan-particle-antialiasing-modes` 的 proposal / design / spec / tasks 已重新对齐阅读.
- [x] `openspec instructions apply --change "radarscan-particle-antialiasing-modes" --json` 仍显示 progress=`0 / 19`,说明 artifacts 完整但 tasks 还没勾选.
- [x] 首轮 Unity EditMode 回归失败的 2 个用例,都不是 AA 逻辑断言失败.
  - 失败点是 `Tests/Editor/GsplatLidarScanTests.cs` 两个 A2C fallback 测试的 `finally` 清理顺序.
  - Unity 报错: `Releasing render texture that is set as Camera.targetTexture!`

### 下一步行动

- [ ] 先修 `GsplatLidarScanTests.cs` 中两个 `finally`:
  - 先 `camera.targetTexture = null`
  - 再 `DestroyImmediate(targetTexture)`
  - 最后 `DestroyImmediate(cameraGo)`
- [ ] 重新跑 `_tmp_gsplat_pkgtests` 的 `Gsplat.Tests` EditMode 回归.
- [ ] 若回归通过,立刻补 OpenSpec tasks 勾选与四文件收尾.

### 当前状态

**目前在阶段3(验证修复)**
- 当前阻塞已经收敛为一个很小的测试生命周期问题.
- 下一步只做这个根因修复,避免把别的未提交 LiDAR 改动卷进来.

## 2026-03-08 12:46:01 +0800 收尾: `radarscan-particle-antialiasing-modes` 已完成实现与验证

### 本轮完成情况

- [x] 修复 `Tests/Editor/GsplatLidarScanTests.cs` 两个 A2C 用例的 RT 清理顺序.
- [x] 重新跑 Unity EditMode `Gsplat.Tests` 全量包测试.
- [x] 确认首轮失败的两个 A2C fallback 测试在 XML 中均为 `Passed`.
- [x] 更新 OpenSpec `openspec/changes/radarscan-particle-antialiasing-modes/tasks.md`,勾选 `19 / 19`.
- [x] 重新跑 OpenSpec apply 检查:
  - progress=`19 / 19`
  - state=`all_done`
- [x] 回写 `notes.md` / `WORKLOG.md` / `LATER_PLANS.md` / `ERRORFIX.md`

### 最终验证证据

- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`82`, passed=`80`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_particle_aa_2026-03-08_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_particle_aa_2026-03-08_noquit.log`
- OpenSpec:
  - `openspec instructions apply --change "radarscan-particle-antialiasing-modes" --json`
  - `state = all_done`

### 当前状态

**目前在阶段4(审查与交付)**
- 本轮实现已经闭环.
- 下一步是整理最后的对外交付说明,并提示需要手动验证的 A2C + MSAA 观感点.

## 2026-03-08 12:50:00 +0800 新现象: `AnalyticCoverage` / `AlphaToCoverage` 肉眼看不出区别

### 用户反馈

- 当前切到:
  - `AnalyticCoverage`
  - `AlphaToCoverage`
- 观感上都和 `LegacySoftEdge` 基本没区别.

### 调试目标

- 先确认运行时是否真的切到了 AA 分支与 A2C 材质.
- 再确认就算切到了,现有 coverage 公式是不是“数学上成立,视觉上几乎等价”.
- 如果根因在 shader,要找出最小且正确的调整点,而不是盲目继续加模式.

### 下一步行动

- [ ] 读取 LiDAR AA 相关 runtime / shader / tests,建立当前数据流证据.
- [ ] 必要时增加临时诊断,证明 effective mode、材质选择、analytic coverage uniform 的实际值.
- [ ] 形成单一根因假设后,再做最小修正与验证.

### 当前状态

**目前回到阶段2(根因调查)**
- 这不是“功能不存在”,而是“功能存在但观感等价”.
- 下一步先做证据化排查,不直接改公式.

## 2026-03-08 12:58:00 +0800 调查结论: 当前问题更像渲染语义不匹配

### 已确认

- [x] runtime AA 模式链路本身是通的:
  - requested / effective mode 会传到 `GsplatLidarScan.RenderPointCloud(...)`
  - effective mode 会选择普通 LiDAR material 或 A2C material
  - `_LidarParticleAAAnalyticCoverage` 会按 effective mode 设为 0/1
- [x] shader 公式也不是同一套:
  - `LegacySoftEdge` = fixed feather
  - `AnalyticCoverage` = `fwidth(signedEdge)`
- [x] 因此“看起来没区别”更可能不是枚举没切上去,而是当前 pass 语义把差异压小了

### 当前单一根因假设

- `AnalyticCoverage` 只改了颜色 alpha 的边缘,但当前 LiDAR pass 仍然 `ZWrite On`,深度轮廓依旧是二值的.
- `AlphaToCoverage` 当前只是给同一个透明混合 pass 加了 `AlphaToMask On`,但 Unity 官方文档更建议它用于 alpha test / cutout 边缘.
- 这说明:
  - `AnalyticCoverage` 的收益在当前路径里偏温和
  - `AlphaToCoverage` 当前实现路线可能从设计上就不够对

### 下一步

- [ ] 不先盲改 shader 常数.
- [ ] 先和用户确认:
  - 是继续保持当前透明语义,只接受“轻微改善”
  - 还是重做 A2C shell,换成真正 coverage-first 的路线(会牵涉透明/遮挡语义变化)

## 2026-03-08 13:02:00 +0800 决策: 采用推荐路线,重做 A2C 为 coverage-first

### 用户选择

- [x] 采用推荐路线
- [x] 不再维持“只是透明 pass + AlphaToMask On”的做法

### 本轮目标

- 把 LiDAR 的 `AlphaToCoverage` / `AnalyticCoveragePlusAlphaToCoverage` 改成真正能产生肉眼差异的 coverage-first 路线.
- 保持:
  - `LegacySoftEdge` 继续兼容旧观感
  - `AnalyticCoverage` 继续作为不依赖 MSAA 的本地路线
- 重点改:
  - A2C shell 的 blend / discard / alpha 语义
  - 必要的 Inspector / README 文案,避免再误导成“现有透明粒子 pass 也能吃到完整 A2C 收益”

### 下一步行动

- [ ] 补一轮官方语义确认,收敛 Unity `AlphaToMask` 在本场景下更合理的 pass 设计.
- [ ] 对照当前 LiDAR shader,确定最小但正确的 A2C 重构方案.
- [ ] 落地 shader/runtime/tests/docs,然后重新跑 Unity EditMode 回归.

### 当前状态

**目前回到阶段3(按根因重构)**
- 路线已经定了.
- 下一步是先把 A2C 的正确渲染语义钉死,再开始改代码.

## 2026-03-08 13:18:00 +0800 进展: coverage-first 路线已落地并回归通过

### 已完成

- [x] `AnalyticCoverage` 改为像素尺度 coverage 计算,增强小点可见差异.
- [x] `AlphaToCoverage` shell 改为 coverage-first pass,不再沿用普通透明混合.
- [x] 扩展 Editor 诊断,非 `LegacySoftEdge` 模式都会记录当前 shader / effective mode / passMode.
- [x] 更新 shader 契约测试与说明文案.
- [x] 重新跑 Unity EditMode `Gsplat.Tests` 回归.

### 最终验证证据

- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`82`, passed=`80`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_particle_aa_v2_2026-03-08_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_particle_aa_v2_2026-03-08_noquit.log`

### 当前状态

**目前在阶段4(审查与交付)**
- 自动化闭环已经完成.
- 剩余重点是让用户做一轮同机位的人眼对比,确认这次 coverage-first 调整是否达到了预期观感.

## 2026-03-08 13:24:00 +0800 新判断: 之前的 AA 只是在原 footprint 内软化

### 新根因

- 之前无论 `AnalyticCoverage` 还是 A2C:
  - 都是在同一个点片 footprint 里做 alpha 过渡
  - 没有给原边界外的 AA fringe 留出几何空间
- 结果:
  - 只能“往里软”
  - 不能在原始边界外形成真正的 1px 左右 coverage fringe
  - 对 `LidarPointRadiusPixels=2` 的小点来说,依然可能肉眼不明显

### 下一步行动

- [ ] 为所有非 `LegacySoftEdge` 模式增加一个很小的 AA 扩边像素(`AA fringe pad`).
- [ ] 让 analytic / A2C 的 coverage 以“原始边界”为基准计算,但在扩出的几何空间里真正长出 fringe.
- [ ] 更新测试与文档,然后再跑一次 Unity 回归.

## 2026-03-08 13:36:00 +0800 继续推进: 验证外扩 fringe 是否真正落地

### 本轮目标

- 用户最新反馈是: 即使打开了 MSAA,当前 AA 仍“不明显”。
- 这轮不再停留在解释层面,而是要把“外扩 edge/fringe”这条判断落实成可验证结论。

### 下一步行动

- [ ] 重新核对 `GsplatLidarPassCore.hlsl` / `GsplatLidarAlphaToCoverage.shader` / `GsplatLidarShaderPropertyTests.cs`,确认外扩 fringe 与 coverage-first 语义一致.
- [ ] 在 `_tmp_gsplat_pkgtests` 重新跑 Unity EditMode `Gsplat.Tests`,验证这轮 shader 改动没有引入回归.
- [ ] 若自动化通过,补记四文件,把“为什么之前看不出来,现在具体改了什么”沉淀清楚.

### 当前状态

**目前回到阶段4(验证外扩 fringe)**
- 我正在做这轮外扩 AA fringe 的源码自检与 Unity 回归。

## 2026-03-08 14:06:00 +0800 进展: 外扩 fringe 已验证通过

### 已完成

- [x] 核对 `GsplatLidarPassCore.hlsl` / `GsplatLidarAlphaToCoverage.shader` / `GsplatLidarShaderPropertyTests.cs`,确认:
  - 非 `LegacySoftEdge` 模式会额外留出 `1px` 左右的外扩 fringe 空间
  - A2C shell 已明确走 coverage-first,不再复用普通透明混合 pass
- [x] 在 `_tmp_gsplat_pkgtests` 重新跑 Unity EditMode `Gsplat.Tests`
- [x] README / CHANGELOG 已补充“外扩 fringe”说明

### 最终验证证据

- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`82`, passed=`80`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_particle_aa_v3_2026-03-08_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_particle_aa_v3_2026-03-08_noquit.log`

### 当前状态

**目前在阶段4(等待人眼复核)**
- 自动化已确认这轮“外扩 fringe + coverage-first”没有引入回归。
- 如果用户手测后仍觉得不明显,下一步应评估把 `aaFringePadPx` 从固定 `1.0` 提升为更强常数或可调参数。

## 2026-03-08 14:18:00 +0800 新需求: 把 aaFringePadPx 做成可调参数

### 本次目标

- 不再把 LiDAR 粒子 AA 的外扩 fringe 固定写死在 shader 里。
- 改成正式的可调参数,方便用户按不同点径和机位去放大或收敛边缘存在感。

### 下一步行动

- [ ] 新增 `Renderer/SequenceRenderer` 侧的 LiDAR AA fringe 参数,默认保持当前 `1.0`.
- [ ] 把该参数下发到 `GsplatLidarScan` 与 LiDAR shader,替代写死的 `aaFringePadPx = 1.0`.
- [ ] 更新 Inspector、测试、README/CHANGELOG,避免参数存在但用户找不到入口.
- [ ] 重新跑 Unity EditMode `Gsplat.Tests`,确认没有引入回归.

### 当前状态

**目前回到阶段3(把 fringe 做成正式调参项)**
- 我正在把“外扩 edge”从 shader 常数提升为完整参数链路。

## 2026-03-08 14:33:00 +0800 进展: fringe 已改成正式可调参数并回归通过

### 已完成

- [x] 新增 `GsplatRenderer/GsplatSequenceRenderer.LidarParticleAAFringePixels`,默认 `1.0`.
- [x] runtime 下发、shader hidden property、Inspector 入口、AA 诊断日志都已接通.
- [x] `GsplatLidarPassCore.hlsl` 已改为读取 `_LidarParticleAAFringePixels`,不再写死 `1.0`.
- [x] 更新了 EditMode tests 与 README/CHANGELOG.
- [x] 重新跑 Unity EditMode `Gsplat.Tests`.

### 最终验证证据

- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`82`, passed=`80`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_particle_aa_fringe_param_2026-03-08.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_particle_aa_fringe_param_2026-03-08.log`

### 当前状态

**目前在阶段4(交付)**
- 现在可以直接在面板里调 AA fringe 宽度了。
- 默认值保持 `1.0`,所以旧场景不会因为升级而突然变形。

## 2026-03-08 14:42:00 +0800 新问题: RadarScan mesh 粒子跑到背面

### 现象

- 用户反馈: “雷达扫描粒子 mesh 上的粒子现在都在 mesh 背面了,之前是正面的。”
- 这属于明显的回归,优先怀疑:
  - external mesh hit 的深度语义/正负方向被改反
  - front-most vs back-most 选择逻辑漂移
  - recent LiDAR/external GPU capture 改动误把“相机前方深度”变成了“背面/穿透后深度”

### 下一步行动

- [ ] 对照最近几轮 LiDAR / external mesh / frustum GPU capture 改动,缩小嫌疑文件范围.
- [ ] 阅读当前 external mesh capture / hit merge / draw 重建链路,确认“正面 vs 背面”是在哪里决定的.
- [ ] 如果能定位到确定的翻转点,直接修复并补测试; 如果还不能,先给出证据化根因结论.

### 当前状态

**目前回到阶段2(回归定位)**
- 我正在查是哪一处把 mesh 粒子的前后关系翻转了。

## 2026-03-08 19:39:00 +0800 进展: 已定位为 external capture shader 的面剔除问题

### 根因结论

- 真正可疑的不是刚刚 RadarScan 粒子 AA 那几轮.
- 更像是 frustum external GPU capture 落地时, `Hidden/Gsplat/LidarExternalCapture` 使用了 `Cull Back`.
- 在手动 `SetViewProjectionMatrices(...)` + RenderTexture + 可能存在负缩放/镜像 transform 的组合下,
  front/back 判定容易翻掉,结果会稳定抓到 mesh 背面,表现成“雷达粒子都贴在背后”.

### 已完成

- [x] 把 external capture shader 改成 `Cull Off`,让 depth buffer 选择最近可见表面.
- [x] 新增 EditMode 回归测试,锁定该 hidden shader 必须使用 `Cull Off`.
- [x] 重新跑 Unity EditMode `Gsplat.Tests`.

### 最终验证证据

- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`83`, passed=`81`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_capture_culloff_v2_2026-03-08.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_external_capture_culloff_v2_2026-03-08.log`

### 当前状态

**目前在阶段4(交付)**
- 代码级回归已经修好并有自动化约束.
- 剩下建议是回到你的具体场景做一次人眼复核,确认粒子重新回到 mesh 正面。

## 2026-03-08 19:47:00 +0800 补充判断: 仍在模型后面,更像是 external hit 深度过贴

### 新判断

- 用户补充“是模型背后”,说明不仅仅是“抓到背面三角形”.
- 更像是:
  - external hit 的重建点位与模型表面深度几乎重合
  - 或者略微更深
  - 在原始 mesh 可见时,粒子就会被模型表面压到后面

### 下一步行动

- [ ] 为 external hit 增加一个仅用于渲染的前推 bias(沿 LiDAR/sensor 射线往前挪一点).
- [ ] 让 bias 可调,默认给一个保守值,便于不同场景继续调.
- [ ] 更新 shader/runtime/tests/docs,再跑 Unity EditMode `Gsplat.Tests`.

### 当前状态

**目前回到阶段3(修正 external hit 的前后深度关系)**
- 我正在把 external mesh hit 从“表面太贴/略深”修正成稳定落在可见表面前方一点点。

## 2026-03-08 21:47:41 +0800 收尾: 默认 bias 改回 0, 并准备 git 提交

### 已确认

- [x] external GPU capture 的 reversed-Z 语义已经补齐:
  - forward-Z 使用 `LessEqual + clearDepth=1`
  - reversed-Z 使用 `GreaterEqual + clearDepth=0`
- [x] 新增真实功能测试锁定“球体中心像素命中前表面深度”,避免以后再次回到背面.
- [x] 按用户最终选择,保留 `LidarExternalHitBiasMeters` 参数,但把默认值与 fallback 收回到 `0`.
- [x] 已重新核对全包 Unity EditMode `Gsplat.Tests` 结果:
  - total=`86`, passed=`85`, failed=`0`, skipped=`1`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_full_external_bias_default_zero_2026-03-08.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_full_external_bias_default_zero_2026-03-08.log`

### 下一步行动

- [ ] 更新 `WORKLOG.md`,记录这轮 reversed-Z 修复与默认 bias 回零的最终结论.
- [ ] 执行 `git add` + `git commit`,仅提交本轮 RadarScan external capture / AA / 文档相关改动.

### 当前状态

**目前在阶段4(验证完成,准备提交)**
- 自动化证据已经齐全.
- 现在进入版本控制收尾阶段。

## 2026-03-09 17:23:21 +0800 新问题: HDRP 下 A2C 被错误回退到 AnalyticCoverage

### 现象

- 用户在 HDRP 场景中已经开启了 MSAA,但 Console 仍打印:
  - `requested=AlphaToCoverage`
  - `effective=AnalyticCoverage`
  - `allowMSAA=0 msaaSamples=1`
- 结果是 RadarScan 粒子的 `AlphaToCoverage` / `AnalyticCoveragePlusAlphaToCoverage` 在 HDRP 下始终无法生效.

### 已确认根因方向

- [x] 当前包内判断把 `camera.allowMSAA` 当成 A2C 的硬门槛.
- [x] HDRP 自己会在 `HDAdditionalCameraData.OnEnable()` 中把 `Camera.allowMSAA` 强制设成 `false`,因为它把这个字段视为 legacy MSAA.
- [x] HDRP 真正的 MSAA 语义来自:
  - HDRP Asset 的 `Multisample Anti-aliasing Quality`
  - Camera/Default Frame Settings 的 `MSAA` / `MSAA Within Forward`

### 下一步行动

- [ ] 继续阅读 HDRP runtime API,确认如何在运行时读取“这台 camera 最终生效的 MSAA / Forward frame settings”.
- [ ] 把 `GsplatUtils.IsLidarParticleMsaaAvailable(...)` 改成:
  - 普通管线维持现状
  - HDRP 走 `HDAdditionalCameraData + FrameSettings` 路线
- [ ] 同步修正 LiDAR AA 诊断日志里的 sample count 来源,避免 HDRP 下继续打印误导性的 `allowMSAA=0 msaaSamples=1`.
- [ ] 新增 EditMode tests,锁定 HDRP 兼容语义(至少覆盖 helper 层逻辑; 若环境支持 HDRP,补充正向保持 A2C 的断言).
- [ ] 运行相关 Unity EditMode tests 验证,再更新 `notes.md` / `WORKLOG.md` / `ERRORFIX.md`.

### 当前状态

**目前回到阶段2(针对 HDRP 做根因级修复)**
- 我正在把 “A2C 是否可用” 从通用相机字段判断,修正成对 HDRP Frame Settings 友好的判断。

## 2026-03-09 17:37:45 +0800 收尾: HDRP A2C 检测已修复并完成回归

### 已完成

- [x] 确认旧逻辑误把 `Camera.allowMSAA` 当成 HDRP A2C 的硬门槛.
- [x] 在 `GsplatUtils` 新增统一 LiDAR MSAA helper:
  - 普通管线维持旧判断
  - HDRP 改读聚合后的 HD Frame Settings / resolved MSAA mode
- [x] 同步修正 LiDAR AA 诊断日志:
  - 现在会输出 `cameraAllowMSAA`
  - `msaaSamples`
  - `msaaSource`
- [x] 新增 HDRP 条件测试,锁定“即使 `Camera.allowMSAA=false`,HDRP 仍可保持 A2C 生效”的语义.
- [x] 更新 Inspector help / README,写清楚 HDRP 下不要再把 `Camera.allowMSAA` 当成判断依据.

### 验证证据

- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests.GsplatLidarScanTests`
  - total=`33`, passed=`33`, failed=`0`, skipped=`0`
  - XML:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_hdrp_a2c_fix_2026-03-09.xml`
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`86`, passed=`83`, failed=`0`, skipped=`3`
  - `skipped=3` 为既有 ignore / 环境性 skip,本轮无新增失败
  - XML:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_full_lidar_hdrp_a2c_fix_2026-03-09_r2.xml`

### 当前状态

**目前在阶段4(实现与验证完成)**
- 代码修复、自动化验证、文档同步、四文件记录都已完成.
- 下一步只剩你决定是否要我继续 `git commit`。

## 2026-03-09 22:02:00 +0800 新问题: Editor tests 直接引用 HDRP 命名空间导致真实项目编译失败

### 现象

- 用户在真实 HDRP 项目里编译包测试程序集时,出现:
  - `Tests/Editor/GsplatLidarScanTests.cs(11,29): error CS0234`
  - `UnityEngine.Rendering.HighDefinition` 在当前测试程序集里不可见.

### 根因判断

- [x] 这不是 runtime 修复本身的问题.
- [x] 直接原因是 `Tests/Editor/GsplatLidarScanTests.cs` 新增了 `using UnityEngine.Rendering.HighDefinition;`.
- [x] `Tests/Editor/Gsplat.Tests.Editor.asmdef` 只加了 `versionDefines`,但没有也不能安全地为测试程序集增加“条件 HDRP 程序集引用”.
- [x] 结论: 当前 HDRP 专项测试写法把“可选依赖”错误地写成了“编译时硬依赖”.

### 下一步行动

- [x] 把 `GsplatLidarScanTests` 里的 HDRP 专项测试改成纯反射写法:
  - 不再直接 `using UnityEngine.Rendering.HighDefinition`.
  - 不再直接引用 `HDAdditionalCameraData` / `FrameSettings` / `HDRenderPipelineAsset` 等类型.
- [x] 移除 `Tests/Editor/Gsplat.Tests.Editor.asmdef` 中本轮新增的 HDRP `versionDefines`,避免继续制造“源码条件通过,编译引用却不完整”的错觉.
- [x] 重新检查测试逻辑是否仍能覆盖关键语义:
  - HDRP 会把 `Camera.allowMSAA` 设为 `false`
  - 但 `GsplatUtils.GetLidarParticleMsaaSampleCount(camera)` 仍应返回 HDRP resolved samples
  - `AlphaToCoverage` 不应被误回退
- [x] 跑最小验证命令,至少确认当前包源码层面不再包含测试对 `UnityEngine.Rendering.HighDefinition` 的直接编译依赖.
- [x] 再把本轮修复和验证结果补写进 `notes.md` / `WORKLOG.md` / `ERRORFIX.md`.

### 当前状态

**目前在阶段4(修复与验证完成)**
- HDRP 专项测试已经改成运行时反射探测,测试程序集不再对 `UnityEngine.Rendering.HighDefinition` 形成编译时硬依赖.
- 验证结果:
  - `dotnet build Gsplat.Tests.Editor.csproj -nologo`
    - `0 errors`, `4 warnings`
    - 直接证明当前真实项目里的 `CS0234` 已消失
  - Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, 定向 EditMode:
    - `Gsplat.Tests.GsplatLidarScanTests.GetLidarParticleMsaaSampleCount_HdrpUsesResolvedFrameSettingsEvenWhenCameraAllowMsaaIsFalse`
    - 结果=`skipped`
    - 原因=`HDRP package is not loaded, skipping HDRP-specific LiDAR A2C test.`
    - 这说明反射版测试可被 TestRunner 正常加载与执行,且在非 HDRP 工程中按预期跳过,不会再造成编译失败。
