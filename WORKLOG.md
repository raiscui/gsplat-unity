# WORKLOG

## 2026-02-26 11:48:36 +0800
- (续档) 原 `WORKLOG.md` 已重命名为 `WORKLOG_2026-02-26_114836.md`,因为超过 1000 行.
- 本文件从此时间点起继续记录后续工作.

## 2026-02-26 12:26:00 +0800
- 新增一种新的显示效果: `ParticleDots`(粒子圆片/圆点).
- 支持通过 API 在 `Gaussian` 常规效果与 `ParticleDots` 之间切换.
- 切换默认带动画.
  - 曲线: `easeInOutQuart`.
  - 时长: 1.5 秒.
- 点大小可调.
  - 语义: 屏幕像素半径(px radius).

### 变更内容
- Runtime:
  - `Runtime/GsplatUtils.cs`: 新增 `GsplatRenderStyle` 与 `GsplatUtils.EaseInOutQuart`.
  - `Runtime/GsplatRenderer.cs`: 增加 RenderStyle 字段与 API(`SetRenderStyle`,`SetParticleDotRadiusPixels`),并实现切换动画状态机(含 Editor ticker 兜底).
  - `Runtime/GsplatSequenceRenderer.cs`: 同步支持 RenderStyle 字段/API/动画,并在 Update 与 SRP 相机回调链路中推进与下发 uniforms.
  - `Runtime/GsplatRendererImpl.cs`: 新增 uniforms 下发:
    - `_RenderStyleBlend`
    - `_ParticleDotRadiusPixels`
- Shader:
  - `Runtime/Shaders/Gsplat.shader`:
    - 新增 ParticleDots 的 vertex/frag 核(实心 + 柔边).
    - 通过 `_RenderStyleBlend` 做单次 draw 的形态渐变(morph).
    - `blend=0` 时保持旧行为(保留 `<2px` early-out 与 frustum cull).
- Tests:
  - `Tests/Editor/GsplatUtilsTests.cs`: 增加 `EaseInOutQuart` 关键采样点单测.
  - `Tests/Editor/GsplatVisibilityAnimationTests.cs`: 增加 RenderStyle 动画收敛单测(反射推进,不依赖 Editor PlayerLoop).
- Docs:
  - `README.md`: 增加 Render style 说明与 API 示例.
  - `CHANGELOG.md`: Unreleased 记录新增 RenderStyle 功能.

### 回归(证据)
- Unity 6000.3.8f1,`-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - total=30, passed=28, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_renderstyle_2026-02-26_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_renderstyle_2026-02-26_noquit.log`

## 2026-02-26 12:52:00 +0800
- 解决 Inspector 下 RenderStyle 下拉切换“不看到动画”的体验问题.
  - 原因: 下拉只是改序列化字段,不会走 `SetRenderStyle(..., animated:true)` 的动画状态机.
- Editor Inspector 增加两个按钮用于播放动画切换:
  - `Editor/GsplatRendererEditor.cs`: `Gaussian(动画)` / `ParticleDots(动画)`.
  - `Editor/GsplatSequenceRendererEditor.cs`: 同步增加.
- 同时改良了 Editor 按钮触发的参数一致性:
  - 在触发按钮型 API 前先 `serializedObject.ApplyModifiedProperties()`,
    避免按钮读取到旧的 duration/参数导致“改了但没生效”的错觉.

### 回归(证据)
- Unity 6000.3.8f1 EditMode tests:
  - total=30, passed=28, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_renderstyle_inspector_buttons_2026-02-26.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_renderstyle_inspector_buttons_2026-02-26.log`

## 2026-02-26 13:33:00 +0800
- 修复 RenderStyle(Gaussian <-> ParticleDots) 切换时的边缘 pop-in/out:
  - 表现:
    - Gaussian -> Dots 动画尾部,部分靠屏幕外缘的 splat 会突然消失.
    - Dots -> Gaussian 动画头部,同一批 splat 会突然出现.
  - 根因:
    - shader 在 `styleBlend==1` 时跳过 Gaussian corner 计算.
    - 当 dotCorner 因 frustum cull 不可用时,vertex 阶段会直接 discard,导致动画头尾 pop.
  - 修复:
    - `Runtime/Shaders/Gsplat.shader`: 先算 dotCorner,当 `styleBlend<1` 或 dotCorner 不可用时才算 Gaussian corner 作为兜底几何.
    - fragment 增加 `uvDot`(屏幕像素半径归一化),并改为“两种核都不贡献才 discard”,从而让这些 splat 通过 alpha 平滑淡出/淡入,不再突然 pop.

### 回归(证据)
- Unity 6000.3.8f1 EditMode tests:
  - total=30, passed=28, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_renderstyle_popfix_2026-02-26.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_renderstyle_popfix_2026-02-26.log`

## 2026-03-01 15:39:48 +0800
- (仅规格工单) 讨论并收敛了"ParticleDots 车载 LiDAR 采集显示"的目标参数,并创建 OpenSpec change 作为后续实现载体.
  - 说明:
    - 本阶段不落地代码.
    - 高斯基元(splat)可完全不显示,仅作为"环境采样点".
    - 最终渲染为新生成的规则点云,以获得更整齐的 128 线束环线.
- 已创建 OpenSpec change:
  - name: `particle-dots-lidar-scan`
  - schema: spec-driven
  - artifacts: proposal -> design/specs -> tasks

## 2026-03-01 15:58:54 +0800
- (规格工单) 使用 `$openspec-ff-change` 快速生成 `particle-dots-lidar-scan` 的全部 artifacts(4/4),用于后续直接进入实现.
  - `openspec/changes/particle-dots-lidar-scan/proposal.md`
  - `openspec/changes/particle-dots-lidar-scan/design.md`
  - `openspec/changes/particle-dots-lidar-scan/specs/gsplat-lidar-scan-visualization/spec.md`
  - `openspec/changes/particle-dots-lidar-scan/tasks.md`
- 关键需求参数(已写入 artifacts):
  - 360 度,RotationHz=5,UpdateHz=10,1 圈保留+前沿余辉.
  - BeamCount=128,AzimuthBins=2048,"上少下多"竖直分布.
  - 颜色模式: Depth(1m..200m) / SplatColor(SH0).
  - 点大小: 默认 2px,可调.

## 2026-03-01 20:47:37 +0800
- 落地 OpenSpec change: `particle-dots-lidar-scan`(apply 24/24 tasks).
- 新增能力: Experimental "LiDAR scan visualization"(车载风格点云采集观感),默认关闭,不影响原有 Gaussian/ParticleDots.

### 变更内容
- Runtime:
  - `Runtime/Lidar/GsplatLidarScan.cs`: LiDAR range image/LUT 生命周期 + UpdateHz 门禁 + compute dispatch + 点云 draw 提交(含 Metal 必绑资源策略).
  - `Runtime/GsplatRenderer.cs`:
    - 新增 LiDAR 参数字段 + clamp.
    - UpdateHz=10 触发 range image 重建,RotationHz=5 做扫描前沿/余辉亮度.
    - 解耦 splat buffers 与 splat sort/draw: `HideSplatsWhenLidarEnabled=true` 时不提交 splat sort/draw,但 LiDAR compute/draw 正常.
    - EditMode: 启用 LiDAR 时受控驱动 `RepaintAllViews`,保证扫描前沿连续播放.
  - `Runtime/GsplatSequenceRenderer.cs`:
    - 同步 LiDAR 参数/compute/draw.
    - 同步解耦门禁(显式 `IGsplat.SplatCount` 返回 0 跳过 sorter),HideSplatsWhenLidarEnabled 时仅显示 LiDAR 点云.
    - EditMode: 同款 repaint 驱动.
  - `Runtime/GsplatSettings.cs`: 增加 `LidarShader` 与 `LidarMaterial`,用于 LiDAR 点云绘制.
  - `Runtime/GsplatUtils.cs`: 新增 `GsplatLidarColorMode` 与 beams 归一化工具(固定 total=128,上少下多).
- Compute/Shader:
  - `Runtime/Shaders/Gsplat.compute`: 新增 LiDAR kernels:
    - `ClearRangeImage`
    - `ReduceMinRangeSq`(first return rangeSq 原子 min)
    - `ResolveMinSplatId`(两阶段保证 range/id 严格对应,确定性 tie-break).
  - `Runtime/Shaders/GsplatLidar.shader`: 新增 LiDAR 点云渲染 shader(圆点/圆片,px radius,Depth/SH0 颜色,扫描余辉,Intensity 亮度倍率).
- Editor:
  - `Editor/GsplatRendererEditor.cs`/`Editor/GsplatSequenceRendererEditor.cs`:
    - 增加 LiDAR 调参区(集中展示常用参数,显示有效网格尺寸/点数,Origin 缺失提示).
- Tests:
  - `Tests/Editor/GsplatLidarScanTests.cs`: 参数 clamp + UpdateHz 门禁纯逻辑回归(不跑 GPU compute).
- Docs:
  - `README.md`: 增加 LiDAR 用法/参数/API 示例 + 手动验证清单.
  - `CHANGELOG.md`: Unreleased 记录新增 LiDAR 扫描点云能力.

### 回归(证据)
- Unity 6000.3.8f1,EditMode tests:
  - total=33, passed=31, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_particle_dots_lidar_scan_2026-03-01_204400.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_particle_dots_lidar_scan_2026-03-01_204400.log`

## 2026-03-01 22:12:00 +0800
- LiDAR: 竖直线束不再拆分 Up/Down,统一为 `LidarBeamCount`,并在 `[LidarDownFovDeg..LidarUpFovDeg]` 匀角度采样生成竖直 LUT.
- 修复 LiDAR 点云“厚壳”偏移: compute 侧存储 `depth^2 = dot(posLidar,dirBinCenter)^2`,替代旧的 `|pos|^2`,并在 compute 侧绑定 LUT 保持与渲染重建一致.

### 变更内容
- Runtime:
  - `Runtime/GsplatRenderer.cs`/`Runtime/GsplatSequenceRenderer.cs`: 参数改为 `LidarBeamCount`,并同步 clamp/调度/渲染调用.
  - `Runtime/Lidar/GsplatLidarScan.cs`: LUT 生成改为匀角度采样; compute dispatch 绑定 LUT; range 语义改为 depth 投影.
- Compute:
  - `Runtime/Shaders/Gsplat.compute`: 移除 Up/Down beams,增加 `_LidarBeamCount` + LUT buffer,range image 改为 depth^2 归约.
- Editor/Tests/Docs:
  - `Editor/GsplatRendererEditor.cs`/`Editor/GsplatSequenceRendererEditor.cs`: 面板只显示 `LidarBeamCount`.
  - `Tests/Editor/GsplatLidarScanTests.cs`: 更新 clamp 断言适配新字段.
  - `README.md`: 更新 LiDAR 默认网格描述.

### 回归(证据)
- Unity 6000.3.8f1,EditMode tests:
  - total=33, passed=31, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_beamcount_shellfix_2026-03-01_221012.xml`

## 2026-03-01 22:52:00 +0800
- LiDAR: 进一步修复“包一层”观感,让 first return 采样对齐渲染可见性:
  - compute 侧加入 4D 时间核裁剪(window/gaussian)与速度位移(与主 shader 一致),避免命中时间窗外的 splats.
  - 增加 `LidarMinSplatOpacity`(默认 1/255)过滤低 opacity 的透明噪声 splats,避免形成“透明外壳”.

### 回归(证据)
- Unity 6000.3.8f1,EditMode tests:
  - total=33, passed=31, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_opacity_filter_2026-03-01_225104.xml`

## 2026-03-01 23:56:00 +0800
- LiDAR 点云渲染观感调整:
  - 点形改为正方形(软边).
  - additive blend,不再出现“透明发灰”.
  - Depth 配色改为 cyan -> red 渐变.

### 回归(证据)
- Unity 6000.3.8f1,EditMode tests:
  - total=33, passed=31, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_shader_square_2026-03-01_235423.xml`

## 2026-03-02 00:29:57 +0800
- 修正 LiDAR 的 Depth 色带路径:
  - 原实现虽然是 cyan -> red,但因 hue 端点处理导致走了 cyan -> blue -> purple -> red.
  - 现改为更可控的“深度热力图”常用路径: cyan -> green -> yellow -> red(避免蓝/紫过渡).
- 同步文档:
  - `README.md`/`CHANGELOG.md` 更新 Depth 配色描述.

### 回归(证据)
- Unity 6000.3.8f1,EditMode tests:
  - total=33, passed=31, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_depth_colormap_2026-03-02_002839_noquit.xml`
- Commit: `278742e`

## 2026-03-02 00:37:38 +0800
- 需求澄清后修正: Depth 色带目标就是 "青 -> 蓝 -> 紫 -> 红".
  - 因此把 Depth 色带恢复为 HSV hue 0.5(cyan) -> 1.0(red/360°) 的渐变路径.
  - 同步回滚 README/CHANGELOG 的文案描述,保持一致.

## 2026-03-02 11:22:37 +0800
- LiDAR: Depth 颜色模式增加“透明度/可见性”调参项 `LidarDepthOpacity`.
  - 目的: Depth 模式下可以按场景调整点云覆盖感,不必只能靠 `LidarIntensity` 硬顶亮度.
  - 注意: 由于点云使用 additive blend,这里的 opacity 语义是对亮度/覆盖率的缩放(更像\"可见性\").

### 变更内容
- Runtime:
  - `Runtime/GsplatRenderer.cs`: 新增 `LidarDepthOpacity` 字段并 clamp,并在点云 draw 时下发到 shader.
  - `Runtime/GsplatSequenceRenderer.cs`: 同步新增字段/clamp/下发.
  - `Runtime/Lidar/GsplatLidarScan.cs`: `RenderPointCloud(...)` 增加参数与 `_LidarDepthOpacity` MPB 下发.
- Shader:
  - `Runtime/Shaders/GsplatLidar.shader`: 增加 `_LidarDepthOpacity`,仅在 `LidarColorMode=Depth` 时参与强度计算.
- Editor:
  - `Editor/GsplatRendererEditor.cs`/`Editor/GsplatSequenceRendererEditor.cs`: Inspector 增加 `LidarDepthOpacity`(仅 Depth 生效时可编辑).
- Tests/Docs:
  - `Tests/Editor/GsplatLidarScanTests.cs`: 增加 clamp 回归覆盖.
  - `README.md`/`CHANGELOG.md`: 补充参数说明.

### 回归(证据)
- Unity 6000.3.8f1,EditMode tests:
  - total=33, passed=31, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_depth_opacity_2026-03-02_112159_noquit.xml`
- Commit: `1362d14`

## 2026-03-02 11:57:33 +0800
- LiDAR: Depth 点云实现“真正不透明”(不再 additive 发光).
  - 之前的 `LidarDepthOpacity` 在 additive blend 下只能当作亮度/覆盖率缩放,无法做到 alpha=1 覆盖背景.
  - 现改为 alpha blend:
    - `LidarDepthOpacity` 直接参与 alpha(仅 Depth 模式).
    - `LidarTrailGamma` 只影响亮度(rgb),不影响 alpha,避免浅色底图上出现“透明发灰”.
    - `ColorMask RGB` 避免写入 alpha 通道污染 CameraTarget alpha.

### 回归(证据)
- Unity 6000.3.8f1,EditMode tests:
  - total=33, passed=31, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_depth_opacity_alpha_2026-03-02_115702_noquit.xml`
- Commit: `e6e5615`

## 2026-03-02 12:32:15 +0800
- LiDAR: 修复点云内部遮挡乱序.
  - 触发原因: alpha blend 但 `ZWrite Off`,透明遮挡需要排序,单次 instanced draw 无法保证 per-point 顺序.
  - 修复: 开启 `ZWrite On`,用深度缓冲稳定遮挡(近点遮挡远点).

### 回归(证据)
- Unity 6000.3.8f1,EditMode tests:
  - total=33, passed=31, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_zwrite_2026-03-02_123150_noquit.xml`
- Commit: `2bf675c`

## 2026-03-02 15:22:10 +0800
- 实现 `Render Style` 动画区新增 `RadarScan(动画)` 快捷按钮,支持以下双向切换:
  - `Gaussian/ParticleDots -> RadarScan`: 自动启用 LiDAR,并切换到 `ParticleDots` 动画.
  - `RadarScan -> Gaussian/ParticleDots`: 自动关闭 LiDAR,并切换回指定风格动画.

### 代码改动
- Runtime:
  - `Runtime/GsplatRenderer.cs`: 新增 `SetRenderStyleAndRadarScan(...)` 组合 API.
  - `Runtime/GsplatSequenceRenderer.cs`: 新增同名组合 API,保持序列后端语义一致.
- Editor:
  - `Editor/GsplatRendererEditor.cs`: Render Style 按钮区改为 3 个按钮(`Gaussian(动画)`/`ParticleDots(动画)`/`RadarScan(动画)`),统一调用组合 API.
  - `Editor/GsplatSequenceRendererEditor.cs`: 同步改造为 3 按钮与相同行为.
- Tests:
  - `Tests/Editor/GsplatVisibilityAnimationTests.cs` 新增 2 个回归用例:
    - `SetRenderStyleAndRadarScan_SupportsRendererBidirectionalSwitch`
    - `SetRenderStyleAndRadarScan_SupportsSequenceBidirectionalSwitch`
- Docs:
  - `CHANGELOG.md` 记录新增组合 API 与 Inspector 双向切换行为.

### 回归验证
- Unity 6000.3.8f1:
  - 命令: `-batchmode -nographics -runTests -testPlatform EditMode -testFilter Gsplat.Tests`
  - 结果: total=35, passed=33, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_radar_button_2026-03-02_1516_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_radar_button_2026-03-02_1516_noquit.log`

## 2026-03-02 16:21:00 +0800
- 修复你反馈的两处 LiDAR/RenderStyle 切换硬切问题:
  - `Depth -> SplatColorSH0` 从枚举硬切改为可动画混合(`_LidarColorBlend`).
  - `RadarScan -> Gaussian/ParticleDots` 从立即关闭改为可见性淡出(`_LidarVisibility` + keep-alive fade-out).
- Runtime 新增 API:
  - `SetRadarScanEnabled(bool enableRadarScan, bool animated = true, float durationSeconds = -1.0f)`
  - `SetLidarColorMode(GsplatLidarColorMode colorMode, bool animated = true, float durationSeconds = -1.0f)`
- Editor:
  - `GsplatRendererEditor` / `GsplatSequenceRendererEditor` 的 LiDAR Visual 区新增 `Depth(动画)` / `SplatColor(动画)` 快捷按钮.
- Shader:
  - `GsplatLidar.shader` 增加颜色渐变与可见性淡入淡出,并让 DepthOpacity 在颜色过渡时平滑趋近到 1.
- 测试:
  - `GsplatVisibilityAnimationTests` 新增 2 条动画推进回归用例并通过.

### 回归(证据)
- Unity 6000.3.8f1, EditMode tests:
  - total=37, passed=35, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_radar_anim_fix_2026-03-02_1612_noquit.xml`

## 2026-03-02 16:30:00 +0800
- 修复 RadarScan 入场黑场: `Gaussian/ParticleDots -> RadarScan` 时,当 `HideSplatsWhenLidarEnabled=true` 不再立即停 splat.
- 具体策略:
  - 在 LiDAR fade-in(目标可见性=1)期间延迟隐藏 splat.
  - 当雷达可见性接近完成后再执行纯雷达隐藏策略,避免“先黑一帧/几帧”体感.
- 覆盖范围:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
- 测试:
  - `Tests/Editor/GsplatVisibilityAnimationTests.cs` 新增:
    - `SetRenderStyleAndRadarScan_Animated_DelayHideSplatsUntilRadarVisible`

### 回归(证据)
- Unity 6000.3.8f1, EditMode tests:
  - total=38, passed=36, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_radar_enter_no_black_2026-03-02_1640_noquit.xml`

## 2026-03-02 17:12:00 +0800
- 修复 `Hide` 按钮触发时的突兀亮球(glow burst):
  - 在 `Runtime/Shaders/Gsplat.shader` 的 hide 分支增加 `hideIntroGate`(短时平滑门控).
  - ring 与 glow 在 hide 起始阶段不再 frame-0 直接满强度出现,改为从 0 平滑抬升.
- 根因结论(已验证):
  - 非 `HideDuration` 曲线跳变问题.
  - 原逻辑在 progress=0 就进入 hide ring/glow 路径,且默认 hide boost/intensity 偏高导致体感突兀.

### 回归(证据)
- Unity 6000.3.8f1, EditMode tests:
  - total=38, passed=36, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_hide_glow_intro_gate_2026-03-02_1706_noquit.xml`

## 2026-03-02 17:25:00 +0800
- 根据反馈回撤上一版“hide 透明度门控”做法,改为几何优先方案:
  - `Runtime/Shaders/Gsplat.shader` 在 hide 模式下将 `ringWidth/trailWidth` 做起始缩放.
  - 起始宽度比例从 `kHideStartWidthScale=0.04` 开始,在短区间内平滑拉到 1.
  - 结果: hide 触发时球体从很小开始长大,避免首帧大球突兀.
- 说明:
  - 本次未通过 alpha 或 glow 透明度掩盖问题,而是直接控制几何范围.

### 回归(证据)
- Unity 6000.3.8f1, EditMode tests:
  - total=38, passed=36, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_hide_glow_small_start_2026-03-02_1720_noquit.xml`

## 2026-03-02 17:35:00 +0800
- 修复 show/hide 在动画中途快速反复点击时的显示异常:
  - 旧逻辑: 中断时直接切换 `Showing/Hiding` 模式并做进度镜像,容易出现中途跳态.
  - 新逻辑: 保持当前模式不变,仅反转进度方向(可中断反向).
- 实现细节:
  - `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs` 新增 `m_visibilityProgressDirection`.
  - `PlayShow/PlayHide` 支持在 `Showing/Hiding` 状态内反向推进而不切换模式.
  - `AdvanceVisibilityStateIfNeeded` 按方向正反推进并在边界收敛到 `Visible/Hidden`.
- 测试补强:
  - `PlayHide_DuringShowing_ReversesInPlaceWithoutModeJump`
  - `PlayShow_DuringHiding_ReversesInPlaceWithoutModeJump`

### 回归(证据)
- Unity 6000.3.8f1, EditMode tests:
  - total=40, passed=38, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_visibility_interrupt_reverse_2026-03-02_1730_noquit.xml`

## 2026-03-02 18:42:00 +0800 - 修复 Show/Hide 中途切换,改为 source-mask 叠加

### 本次改动
- `Runtime/GsplatRenderer.cs`
  - 新增 `VisibilitySourceMaskMode` 与 source runtime 字段.
  - 新增 `SetVisibilitySourceMask(...)`.
  - 新增 `CaptureVisibilitySourceMaskForShowTransition()` / `CaptureVisibilitySourceMaskForHideTransition()`.
  - `PlayShow/PlayHide` 中断逻辑改为“先抓 source,再启动目标动画”.
  - `AdvanceVisibilityStateIfNeeded` 在终态统一回收 source 到 `FullVisible/FullHidden`.
  - `PushVisibilityUniformsForThisFrame` 下发 `sourceMaskMode/sourceMaskProgress`.

- `Runtime/GsplatSequenceRenderer.cs`
  - 与 `GsplatRenderer` 同步 source-mask 叠加逻辑与 uniform 下发.

- `Runtime/GsplatRendererImpl.cs`
  - `SetVisibilityUniforms` 新增参数:
    - `sourceMaskMode`
    - `sourceMaskProgress`
  - 增加对应 shader property id 并写入 MPB.

- `Runtime/Shaders/Gsplat.shader`
  - 新增 uniforms:
    - `_VisibilitySourceMaskMode`
    - `_VisibilitySourceMaskProgress`
  - 新增 `EvalVisibilityVisibleMask(...)` 用于 snapshot mask 评估.
  - 合成规则:
    - show: `max(primary, source)`
    - hide: `primary * source`
  - 保留 show source 覆盖区时,把 `visibilitySizeMul` 回拉到正常尺寸,避免外圈小点化.

- `Tests/Editor/GsplatVisibilityAnimationTests.cs`
  - 新增 source 字段反射与 helper.
  - 在两条中断测试中新增断言:
    - `Show -> Hide` 时 source=`ShowSnapshot`.
    - `Hide -> Show` 时 source=`HideSnapshot`.

- `CHANGELOG.md`
  - 更新 `Unreleased -> Fixed`,记录 source-mask 叠加修复语义.

### 验证
- Unity EditMode `Gsplat.Tests`
  - total=40, passed=38, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_visibility_source_overlay_2026-03-02_1808_noquit.xml`

### 2026-03-02 18:50:00 +0800 - 二次回归确认
- 为避免测试文案调整引入误报,再次执行 `Gsplat.Tests`.
- 结果: total=40, passed=38, failed=0, skipped=2.
- XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_visibility_source_overlay_2026-03-02_1848_noquit.xml`

## 2026-03-03 01:07:00 +0800 - RadarScan 支持 Show/Hide(对齐 ParticleDots)

### 实施内容
- `Runtime/GsplatRenderer.cs`
  - 新增 `BuildLidarShowHideOverlayForThisFrame(...)`.
  - 在 `RenderLidarInUpdateIfNeeded` / `RenderLidarForCamera` 下发 LiDAR show/hide overlay 参数.
  - Hidden 终态下,`showHideGate=0 && mode=0` 时直接跳过 LiDAR draw.

- `Runtime/GsplatSequenceRenderer.cs`
  - 同步新增 `BuildLidarShowHideOverlayForThisFrame(...)`.
  - 同步 LiDAR render 调用参数扩展与 hidden draw skip.

- `Runtime/Lidar/GsplatLidarScan.cs`
  - `RenderPointCloud(...)` 扩展 show/hide overlay 参数.
  - 新增 LiDAR shader 属性绑定:
    - `_LidarMatrixW2M`
    - `_LidarShowHideGate`
    - `_LidarShowHideMode`
    - `_LidarShowHideProgress`
    - `_LidarShowHideSourceMaskMode`
    - `_LidarShowHideSourceMaskProgress`
    - `_LidarShowHideCenterModel`
    - `_LidarShowHideMaxRadius`
    - `_LidarShowHideRingWidth`
    - `_LidarShowHideTrailWidth`

- `Runtime/Shaders/GsplatLidar.shader`
  - 新增 show/hide overlay uniforms 与 `world->model` 矩阵.
  - 新增 `EaseInOutQuad` / `EvalLidarShowHideVisibleMask`.
  - 在 vert 计算 LiDAR show/hide 掩码并支持 source-mask 叠加.
  - 在 frag 中将 `alpha/brightness` 乘以 show/hide 掩码,并给边缘带轻微 glow boost.

- `Tests/Editor/GsplatVisibilityAnimationTests.cs`
  - 新增反射 helper:
    - `BuildLidarShowHideOverlay(...)`
    - `SetVisibilityStateByName(...)`
    - `SetVisibilitySourceMaskByName(...)`
  - 新增测试:
    - `BuildLidarShowHideOverlay_HiddenState_OutputsGateZero`
    - `BuildLidarShowHideOverlay_HidingState_OutputsHideModeAndSnapshot`

- `CHANGELOG.md`
  - 增加 RadarScan show/hide 对齐修复记录.

### 验证
- Unity EditMode `Gsplat.Tests`
  - total=42, passed=40, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_showhide_overlay_2026-03-03_0100_noquit.xml`

## 2026-03-03 01:37:00 +0800
- 修复 `RadarScan` show/hide 缺少粒子 noise 质感的问题.

### 变更内容
- `Runtime/Shaders/GsplatLidar.shader`
  - 补齐 show/hide noise 链路:
    - primary mask 接入 noise.
    - source-mask(`ShowSnapshot`/`HideSnapshot`)接入同一 noise 语义.
    - ring glow 前沿改为 noise jitter 边界.
  - `EvalLidarShowHideVisibleMask` 调整为 show/hide 都受 `ashMul` 影响,与 splat 侧观感更一致.
- `Tests/Editor/GsplatVisibilityAnimationTests.cs`
  - 新增 `LidarRenderPointCloud_Signature_ContainsShowHideNoiseParams`.
  - 通过反射锁定 `RenderPointCloud` 的 noise 参数契约,防止未来回归.
- `CHANGELOG.md`
  - 记录 RadarScan show/hide noise 修复说明.

### 回归(证据)
- Unity 6000.3.8f1, EditMode `Gsplat.Tests`
  - total=43, passed=41, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_showhide_noise_2026-03-03_0131_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_showhide_noise_2026-03-03_0131.log`

## 2026-03-03 02:06:00 +0800
- RadarScan show/hide noise 二次增强(提升肉眼可见度):
  - 提高 LiDAR noise 采样频率.
  - Curl 模式增加轻量 domain warp + 双层噪声混合.
  - 边界 jitter 增加最小幅度下限,避免 trail 较小时噪声被吞掉.
  - ring glow 增加 noise 明暗调制.

### 回归(证据)
- Unity EditMode `Gsplat.Tests`
  - total=43, passed=41, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_showhide_noise_tune2_2026-03-03_0204_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_showhide_noise_tune2_2026-03-03_0204.log`

## 2026-03-03 02:14:00 +0800
- RadarScan show/hide noise 再增强:
  - LiDAR 路径内部 `noiseStrength` 映射提升到 `x1.6`,保证默认参数也能看出噪声.
  - 修复一次变量命名冲突隐患(`noiseWeight` -> `noiseWeightJitter`).

### 回归(证据)
- Unity EditMode `Gsplat.Tests`
  - total=43, passed=41, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_showhide_noise_tune3_2026-03-03_0213_noquit.xml`

## 2026-03-03 02:22:00 +0800
- 按 `ParticleDots` 观感补齐 `RadarScan` show/hide 噪声位移效果:
  - 新增边界粒子位移抖动(screen-space warp),并按 edge/ring/progress 控制权重.
  - 解决"有遮罩噪声但看不到粒子扰动"的问题.

### 回归(证据)
- Unity EditMode `Gsplat.Tests`
  - total=43, passed=41, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_showhide_noise_tune4_2026-03-03_0220_noquit.xml`

## 2026-03-02 19:00:06 +0800
- LiDAR: 按用户要求把 RadarScan show/hide 的 noise 位移幅度改为可调:
  - 新增 `GsplatRenderer.LidarShowHideWarpPixels` / `GsplatSequenceRenderer.LidarShowHideWarpPixels`(单位: 屏幕像素).
  - shader 新增 `_LidarShowHideWarpPixels`,show/hide 的屏幕空间 jitter 不再与点半径耦合.
- Tests:
  - 新增 `GsplatLidarShaderPropertyTests`,锁定 LiDAR shader 必须包含 `_LidarShowHideNoise*` + `_LidarShowHideWarpPixels` 属性契约.
  - 更新 `GsplatLidarScanTests`,覆盖新字段的 clamp 语义(非法值回退到默认 6px).

### 回归(证据)
- Unity 6000.3.8f1, EditMode `Gsplat.Tests`
  - total=44, passed=42, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_showhide_warp_pixels_2026-03-02_185824_noquit.xml`

## 2026-03-03 00:30:00 +0800
- RadarScan(LiDAR) 的 show/hide noise 语义进一步对齐高斯:
  - `CurlSmoke` 现在使用 curl-like 向量场(而不是近似的 value noise),整体扰动更像“旋涡/流动”.
  - show/hide 的屏幕空间 jitter 额外乘上 `WarpStrength`(与高斯语义一致: 0 禁用,>0 增强).
- Tests:
  - 更新 `GsplatLidarShaderPropertyTests`,锁定 `_LidarShowHideWarpStrength` 必须存在.

### 回归(证据)
- Unity 6000.3.8f1, EditMode `Gsplat.Tests`
  - total=44, passed=42, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_curl_warpstrength_2026-03-03_002646_noquit.xml`

## 2026-03-03 10:10:30 +0800
- RadarScan(LiDAR) show/hide 增加高斯同款 glow(带颜色 additive 叠加,而不是仅 brightness multiply).
  - Shader: `Runtime/Shaders/GsplatLidar.shader`
    - 新增隐藏属性(用于 MPB 稳态下发): `_LidarShowHideGlowColor`, `_LidarShowHideGlowIntensity`.
    - show/hide ring glow 以 additive 方式叠加到 `rgb`(并遵循 scan trail,避免很老的点被重新点亮).
  - Runtime: `Runtime/Lidar/GsplatLidarScan.cs`
    - 新增 MPB 下发: `_LidarShowHideGlowColor`, `_LidarShowHideGlowIntensity`.
  - `GsplatRenderer` / `GsplatSequenceRenderer`:
    - glow 参数复用高斯已有字段: `GlowColor` + `ShowGlowIntensity` / `HideGlowIntensity`.

- 按用户需求移除 `LidarShowHideWarpPixels` 的最大值 clamp.
  - 之前上限为 64,导致用户“调大但不生效”.
  - 现在仅保留 NaN/Inf/负数防御,不再限制最大值.

### 测试与回归(证据)
- 扩展/新增 EditMode tests:
  - `Tests/Editor/GsplatLidarShaderPropertyTests.cs`: 覆盖 LiDAR show/hide glow 属性契约.
  - `Tests/Editor/GsplatLidarScanTests.cs`: 覆盖 `LidarShowHideWarpPixels` 大值不被 clamp.
- Unity 6000.3.8f1,EditMode tests(`Gsplat.Tests`):
  - total=46, passed=44, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_showhide_glow_2026-03-03_095630_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_showhide_glow_2026-03-03_095630_noquit.log`

## 2026-03-03 10:25:40 +0800
- RadarScan(LiDAR) show/hide glow 改为可独立调参,并修复 show 不可见 + hide 过快的问题.

### 背景与根因
- 用户反馈:
  - show 的 glow 基本看不出来.
  - hide 的 glow 走得太快,希望更像余辉(时间更靠后).
  - RadarScan 的 glow 颜色/强度需要与高斯分开,单独控制.
- 根因(对照 `Runtime/Shaders/Gsplat.shader`):
  - 高斯 show/hide 会把 `ring` 纳入 alphaMask(`alphaPrimary=max(visible, ring)`),因此 show 阶段即便 visible=0,ring 也能被画出来.
  - LiDAR 侧之前只用 visible 去乘 showHideMul,ring 没参与 alphaMask,导致 show 阶段 ring 外侧点直接 early-out,看起来像 glow 没生效.
  - LiDAR 侧 glowFactor 之前只有 ring,缺少内侧 afterglow tail,导致 hide glow 很快扫完就没了.

### 变更内容
- Runtime: LiDAR glow 参数独立于高斯
  - [GsplatRenderer.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Runtime/GsplatRenderer.cs)
    - 新增: `LidarShowHideGlowColor`, `LidarShowGlowIntensity`, `LidarHideGlowIntensity`.
    - `ValidateLidarSerializedFields` 增加 NaN/Inf/负数防御.
    - LiDAR draw 下发改为使用上述 LiDAR 专用字段.
  - [GsplatSequenceRenderer.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Runtime/GsplatSequenceRenderer.cs)
    - 同步新增字段 + clamp + 下发.

- Editor: LiDAR 调参区补齐新字段
  - [GsplatRendererEditor.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Editor/GsplatRendererEditor.cs)
  - [GsplatSequenceRendererEditor.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Editor/GsplatSequenceRendererEditor.cs)

- Shader: show/hide 语义对齐高斯,并增强可见性
  - [GsplatLidar.shader](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Runtime/Shaders/GsplatLidar.shader)
    - alphaMask 对齐高斯: `alphaPrimary=max(visible, ring)`,并在 show 阶段允许 tail 提供受限 alpha 下限,解决 show glow 不可见.
    - glowFactor = ring + tail(afterglow),让 hide 的 glow 更持久.
    - 修正 discard: discard 判断改为包含 glowAdd 的最终 rgb,避免 scan trail 很小时 show glow 被提前丢弃.
    - glowAdd 的 trail 衰减加入下限(0.35),避免 show 阶段过暗.

- 诊断日志: show/hide diag 增加 glow 值输出
  - [GsplatLidarScan.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Runtime/Lidar/GsplatLidarScan.cs)

### 测试与回归(证据)
- EditMode tests(`Gsplat.Tests`):
  - total=46, passed=44, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_glow_tune_2026-03-03_105818_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_glow_tune_2026-03-03_105818_noquit.log`
- 单测补强:
  - [GsplatLidarScanTests.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Tests/Editor/GsplatLidarScanTests.cs)
    - 覆盖 LiDAR glow 字段的 clamp 行为.

## 2026-03-03 12:16:30 +0800
- RadarScan(LiDAR) show/hide 的 NoiseScale/NoiseSpeed 支持独立设置(不再被全局噪声参数强绑定).

### 变更内容
- Runtime: 增加 LiDAR 专用的 show/hide 噪声覆盖参数
  - [GsplatRenderer.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Runtime/GsplatRenderer.cs)
    - 新增: `LidarShowHideNoiseScale`, `LidarShowHideNoiseSpeed`.
    - 默认值为 `-1`,表示复用全局 `NoiseScale/NoiseSpeed`,以尽量保持旧项目行为不变.
    - LiDAR draw 提交时会计算 effective noiseScale/noiseSpeed,再传入 `GsplatLidarScan.RenderPointCloud(...)`.
  - [GsplatSequenceRenderer.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Runtime/GsplatSequenceRenderer.cs)
    - 同步新增字段与传参逻辑.

- Editor: 在 LiDAR "Visual" 区域暴露新字段,并提示 "<0 复用全局"
  - [GsplatRendererEditor.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Editor/GsplatRendererEditor.cs)
  - [GsplatSequenceRendererEditor.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Editor/GsplatSequenceRendererEditor.cs)

- Tests: 扩展 LiDAR 字段级 clamp 单测
  - [GsplatLidarScanTests.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Tests/Editor/GsplatLidarScanTests.cs)
    - NaN/Inf/负数会回退到 `-1`(复用全局),避免把非法值下发给 shader.

### 测试与回归(证据)
- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`):
  - total=46, passed=44, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_noise_overrides_2026-03-03_121526_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_noise_overrides_2026-03-03_121526_noquit.log`

## 2026-03-03 12:21:10 +0800
- 按用户需求移除 `LidarAzimuthBins` 的最大值 clamp(不再限制到 4096).

### 变更内容
- Runtime:
  - [GsplatRenderer.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Runtime/GsplatRenderer.cs)
    - `ValidateLidarSerializedFields` 不再对 `LidarAzimuthBins > 4096` 做 clamp,只保留最小值防御(` < 64 -> 2048`).
  - [GsplatSequenceRenderer.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Runtime/GsplatSequenceRenderer.cs)
    - 同步移除上限 clamp.
- Tests:
  - [GsplatLidarScanTests.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Tests/Editor/GsplatLidarScanTests.cs)
    - 新增: `ValidateLidarSerializedFields_DoesNotClampAzimuthBinsMax_*` 两条单测,锁定“大值不再被 clamp”的语义.

### 回归(证据)
- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`):
  - total=48, passed=46, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_azimuth_uncap_2026-03-03_122028_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_azimuth_uncap_2026-03-03_122028_noquit.log`

## 2026-03-03 12:29:20 +0800
- 修复 Inspector 中 `LidarColorMode` 下方 “Depth(动画) / SplatColor(动画)” 按钮无效的问题.

### 根因
- `SyncLidarColorBlendTargetFromSerializedMode(animated: true)` 在 Update/OnValidate 中会被频繁调用.
- 旧逻辑在 `m_lidarColorAnimating=true` 时仍会反复 `BeginLidarColorTransition(...)`,导致 progress 每帧被重置为 0,动画永远走不完.

### 修复
- Runtime:
  - [GsplatRenderer.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Runtime/GsplatRenderer.cs)
  - [GsplatSequenceRenderer.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Runtime/GsplatSequenceRenderer.cs)
  - 当 `m_lidarColorAnimTargetBlend01` 已经等于目标 target 时,直接早退,避免重复 Begin.
- Tests:
  - [GsplatLidarScanTests.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Tests/Editor/GsplatLidarScanTests.cs)
  - 新增回归: `SyncLidarColorBlendTargetFromSerializedMode_DoesNotRestartWhenAnimating_*`.

### 回归(证据)
- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`):
  - total=50, passed=48, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_color_buttons_2026-03-03_122758_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_color_buttons_2026-03-03_122758_noquit.log`

## 2026-03-03 13:30:21 +0800
- 修复 Show 起始阶段“突然弹出一个球形范围粒子”的不自然观感,让 show 从 0 尺寸开始平滑长大.

### 背景与根因

- 用户反馈: Show 最开始(不到 1 秒)会突然出现一个球形范围的粒子区域,不像从 0 开始扩散.
- 根因:
  - 高斯/ParticleDots: radius 很小时,ring/trail width 仍是常量,早期 band 过厚,看起来像“弹出球壳”.
  - LiDAR(RadarScan): 还存在 `jitterBase = max(trailWidth*0.75, maxRadius*0.015)` 这类下限.
    - show 初期如果 trailWidth 被缩小,但 `maxRadius*0.015` 仍会让 jitter 保持固定量级.
    - 在 show 初期会把边界抖出一个固定半径的“漏出球”,进一步加剧突兀.

### 变更内容

- Shader: show 早期尺寸门控
  - [Gsplat.shader](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Runtime/Shaders/Gsplat.shader)
    - show(mode=1) 早期对 `trailWidth/ringWidth` 做 size ramp(从 0 -> 1),让可见范围真正从 0 开始长大.
  - [GsplatLidar.shader](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Runtime/Shaders/GsplatLidar.shader)
    - show(mode=1) 增加与 splat 对齐的 size ramp(对 `trailWidth/ringWidth` 从 0 -> 1).
    - progress==0 强制 `showHideMul=0`(避免首帧 ring/noise 漏出).
    - show 初期让 `maxRadius*0.015` jitter 下限也乘上 size ramp,避免固定半径漏出.
    - 同步修正 `EvalLidarShowHideVisibleMask/RingMask` 中的 jitter 下限,避免 sourceMask 评估路径出现同类漏出.

### 回归(证据)

- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`):
  - total=50, passed=48, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_show_start_from_zero_2026-03-03_132849_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_show_start_from_zero_2026-03-03_132849_noquit.log`
