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
