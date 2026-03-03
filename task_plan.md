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

- [x] Runtime: 移除 `LidarAzimuthBins > 4096` 的 clamp(只保留最小值防御).
- [x] Tests: 增加单测锁定“大值不再被 clamp”.
- [x] 回归 `_tmp_gsplat_pkgtests` EditMode `Gsplat.Tests`.
- [x] git commit.
  - commit: `c3e3d6c`

### 状态

**目前在阶段4(回归与提交)**
- 代码与回归已完成,只剩一次 git commit.
- 已提交 git.
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
- [ ] Shader(GsplatLidar.shader):
  - [x] 为 show(mode==1) 增加同款“早期尺寸门控”(ring/trail width).
  - [x] show 初期让 `maxRadius*0.015` 的 jitter 下限也随尺寸门控一起从 0 放大(避免固定半径漏出).
  - [x] progress==0 时强制完全不可见(避免首帧因 ring/noise 漏出).
- [x] 回归 `_tmp_gsplat_pkgtests` EditMode `Gsplat.Tests`.
- [ ] 视觉验证: 在工程里按 Show,确认起始阶段不会再突然弹出一个球形范围.
- [ ] git commit.

### 回归(证据)

- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`):
  - total=50, passed=48, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_show_start_from_zero_2026-03-03_132849_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_show_start_from_zero_2026-03-03_132849_noquit.log`

### 状态

**目前在阶段4(视觉验证与提交)**
- shader 修复与自动化测试已完成.
- 接下来只剩: 视觉确认(Show 起始是否仍弹球) + git commit.
