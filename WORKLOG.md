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

## 2026-03-08 14:33:20 +0800 任务名称: 回答 RadarScan show/hide noise 是否等于 Unity Value Curl Noise

### 任务内容
- 核对当前包里 RadarScan(LiDAR) show/hide 运动噪声的真实实现.
- 对照 Unity 官方 `Value Curl Noise` 文档,判断两者是"同一个东西"还是"同家族但不是同一实现".

### 完成过程
- 读取 `Runtime/GsplatRenderer.cs`,确认:
  - `VisibilityNoiseMode` 默认值是 `ValueSmoke`.
  - `CurlSmoke` 被明确描述为"基于 value noise 的梯度/旋度构造".
- 读取 `Runtime/Shaders/Gsplat.hlsl` 与 `Runtime/Shaders/GsplatLidarPassCore.hlsl`,确认:
  - 代码不是调用 VFX Graph 节点.
  - 而是自己实现了 `value noise -> vector potential -> curl(A)` 的 curl-like 向量场.
- 核对 Unity 官方文档:
  - `Value Curl Noise` 的表述同样是基于 `Value Noise` 并加入 `curl function`.
- 整理出口径:
  - 默认不是 `Value Curl Noise` 风格,因为默认 mode 仍是 `ValueSmoke`.
  - 当切到 `CurlSmoke` 时,属于与 Unity `Value Curl Noise` 同家族的算法思路,但不是直接调用那个 Operator.

### 总结感悟
- 这类问题最容易混淆"视觉像不像"与"实现是不是同一个节点/同一套代码".
- 以后回答类似问题时,要先区分:
  - 默认配置是什么.
  - 是否直接依赖某官方节点.
  - 还是只是在算法思路上同源或近似.
  - 增加 `LidarMinSplatOpacity`(默认 1/255)过滤低 opacity 的透明噪声 splats,避免形成“透明外壳”.

### 回归(证据)
- Unity 6000.3.8f1,EditMode tests:
  - total=33, passed=31, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_opacity_filter_2026-03-01_225104.xml`

## 2026-03-08 20:02:50 +0800
- 修复 frustum external RadarScan 粒子"显示在模型后面"的问题.

### 变更内容
- Runtime:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
  - `Runtime/Lidar/GsplatLidarScan.cs`
  - 新增/接通 `LidarExternalHitBiasMeters` 的 runtime 下发链路.
  - 该参数默认 `0.01f`,只作用于 external hit 的最终渲染位置重建.
- Shader:
  - `Runtime/Shaders/GsplatLidar.shader`
  - `Runtime/Shaders/GsplatLidarAlphaToCoverage.shader`
  - `Runtime/Shaders/GsplatLidarPassCore.hlsl`
  - 新增隐藏属性 `_LidarExternalHitBiasMeters`.
  - `useExternalHit` 路径改为:
    - 保留真实 `range`
    - 额外计算 `renderRange = max(range - bias, 0)`
    - `worldPos` 用 `renderRange` 重建
  - 结果是点云沿传感器射线轻微前推,但 first return / depth 颜色 / 距离衰减仍使用真实距离.
- Tests:
  - `Tests/Editor/GsplatLidarScanTests.cs`
  - `Tests/Editor/GsplatLidarShaderPropertyTests.cs`
  - 补充 clamp/default, shader property 契约,以及 pass core bias 逻辑断言.
- Docs:
  - `README.md`
  - `CHANGELOG.md`

## 2026-03-08 20:42:00 +0800
- 再次修复 frustum external RadarScan 粒子整体跑到 external mesh 背光面/远侧的问题.

### 变更内容
- Runtime:
  - `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`
  - external GPU capture 不再依赖 `RFloat + BlendOp Max` 选最近面.
  - `CaptureGroup(...)` 改为:
    - depth pass 用硬件 depth buffer 选择最近表面
    - color pass 复用同一个 depth/stencil,并保留上一 pass 的 depth(`ClearRenderTarget(false, true, ...)`)
- Shader:
  - `Runtime/Shaders/GsplatLidarExternalCapture.shader`
    - 继续保留 `Cull Off`
    - `DepthCapture` 改为 `ZTest LEqual + ZWrite On + Blend One Zero`
    - 深度颜色 RT 直接写最近表面的 `linearDepth`
    - `SurfaceColorCapture` 改为 `ZTest Equal + ZWrite Off + Blend One Zero`
    - 删除对 `_LidarExternalResolvedDepthTex` 的依赖
  - `Runtime/Shaders/Gsplat.compute`
    - `ResolveExternalFrustumHits` 改回直接读取 capture 的 `linearDepth`
    - 再用 `linearDepth / rayDirSensor.z` 还原 LiDAR ray distance
- Tests:
  - `Tests/Editor/GsplatLidarExternalGpuCaptureTests.cs`
  - 新增/更新约束:
    - hidden capture shader 必须使用 `Cull Off + hardware depth`
    - color pass 必须保留上一 pass 的 depth buffer
    - resolve 不应再把 capture texture 当 encoded depth 解码
- Docs:
  - `CHANGELOG.md`

### 根因复盘
- 之前那条 `encoded-depth + BlendOp Max` 路线,理论上是为了绕开平台 depth 语义.
- 但它把正确性押在了"浮点 RT blending 一定稳定"这个前提上.
- 一旦平台/驱动在这条路线上退化成"最后写入者赢",闭合 mesh 就会很容易把 far side 留下来.
- 改回 `Cull Off + hardware depth nearest surface` 后:
  - front/back 判定问题和 nearest-surface 竞争问题被拆开处理
  - 更贴近 GPU 天然语义,对球体这类闭合模型更稳

### 回归(证据)
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`
  - 定向 `Gsplat.Tests.GsplatLidarExternalGpuCaptureTests`
    - total=`8`, passed=`8`, failed=`0`, skipped=`0`
    - XML:
      - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_external_gpu_capture_frontface_2026-03-08.xml`
  - 全包 `Gsplat.Tests`
    - total=`85`, passed=`83`, failed=`0`, skipped=`2`
    - XML:
      - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_full_external_frontface_2026-03-08.xml`

## 2026-03-08 21:23:00 +0800
- 修复 frustum external RadarScan 在 reversed-Z 平台上稳定抓到 closed mesh 后表面的根因问题.

### 变更内容
- Runtime:
  - `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`
  - external capture depth pass 现在会按平台切换:
    - forward-Z: `CompareFunction.LessEqual`, clearDepth=`1`
    - reversed-Z: `CompareFunction.GreaterEqual`, clearDepth=`0`
  - 仍保留:
    - `Cull Off`
    - color pass 复用同一 depth/stencil
    - color pass `ZTest Equal`
- Shader:
  - `Runtime/Shaders/GsplatLidarExternalCapture.shader`
  - depth pass 的 `ZTest` 从写死 `LEqual` 改为材质属性:
    - `_LidarExternalDepthZTest`
- Tests:
  - `Tests/Editor/GsplatLidarExternalGpuCaptureTests.cs`
  - 新增真实功能验证:
    - 用球体离屏 capture 直接读中心像素
    - 锁定它必须命中前表面深度 `4.5`,而不是后表面 `5.5`
  - 同步更新 source/shader 契约测试:
    - 必须按平台设置 `ZTest`
    - 必须按平台设置 clearDepth

### 根因复盘
- 这次不是“external hit 没被用到”.
- 也不是 `linearDepth / rayDirSensor.z` 的公式错误.
- 真正根因是:
  - 当 external capture 切回 hardware depth 路线后,
  - depth pass 仍沿用了 forward-Z 假设.
- 在 Metal 这类 reversed-Z 平台上:
  - `LEqual + clearDepth=1`
  - 会让 closed mesh 稳定把 far side 留在 capture 结果里.
- 这个结论不是靠猜出来的,而是靠功能性测试直接读回球体中心像素确认的:
  - 修复前=`5.5`
  - 修复后=`4.5`

### 回归(证据)
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`
  - 功能测试:
    - `Gsplat.Tests.GsplatLidarExternalGpuCaptureTests.ExternalGpuCaptureDepthPass_CenterPixelMatchesSphereFrontDepth`
    - passed
    - XML:
      - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_external_capture_functional_2026-03-08_v2.xml`
  - external capture 定向组:
    - total=`9`, passed=`9`, failed=`0`, skipped=`0`
    - XML:
      - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_external_gpu_capture_group_2026-03-08_v3.xml`
  - 全包 `Gsplat.Tests`
    - total=`86`, passed=`85`, failed=`0`, skipped=`1`
    - XML:
      - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_full_external_fix_2026-03-08_v4.xml`

## 2026-03-08 21:36:00 +0800
- 收窄 `LidarExternalHitBiasMeters` 的默认参数面:
  - 能力保留
  - 默认值与 fallback 改回 `0`

### 变更内容
- Runtime:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
  - `Runtime/Lidar/GsplatLidarScan.cs`
  - `LidarExternalHitBiasMeters` 的序列化默认值与 NaN/Inf fallback 统一改为 `0`
- Shader:
  - `Runtime/Shaders/GsplatLidar.shader`
  - `Runtime/Shaders/GsplatLidarAlphaToCoverage.shader`
  - hidden property `_LidarExternalHitBiasMeters` 默认值改为 `0`
- Editor/Docs:
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`
  - `README.md`
  - 文案统一改成:
    - 默认 0(关闭)
    - 只有在普通 mesh 仍可见且粒子看起来略微压在后面时,再按需加到 `0.01/0.02/...`
- Tests:
  - `Tests/Editor/GsplatLidarScanTests.cs`
  - 默认值断言从 `0.01` 改为 `0`

### 设计结论
- 现在真正的根因修复已经是 external capture 的 reversed-Z 语义补齐.
- `LidarExternalHitBiasMeters` 不再承担“默认就该开着救火”的角色.
- 更合适的定位是:
  - 默认关闭
  - 作为可选的 render-only 微调开关保留
  - 同步记录 `LidarExternalHitBiasMeters` 的用途与限制.

### 回归(证据)
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`83`, passed=`81`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_hit_bias_2026-03-08.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_external_hit_bias_2026-03-08.log`

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

## 2026-03-08 14:07:00 +0800
- RadarScan 粒子抗锯齿继续收敛:
  - 在此前“像素尺度 AnalyticCoverage + coverage-first A2C shell”的基础上,
    又补上了非 `LegacySoftEdge` 模式的外扩 edge fringe.
  - 目的不是单纯调 alpha 曲线,而是给 coverage 真正留出原边界外的几何空间,
    让 `LidarPointRadiusPixels=2` 一类小点也能长出可见的 AA fringe.

### 变更内容
- Shader:
  - `Runtime/Shaders/GsplatLidarPassCore.hlsl`
    - 新增 `GSPLAT_LIDAR_A2C_PASS` 缺省宏.
    - 非 Legacy 路线增加 `aaFringePadPx=1.0` 的几何外扩.
    - 用 `paddedRadiusPx` 扩大点片 footprint,并同步放大 `uv`.
    - fragment 改为允许外扩 fringe 区域存在,再用 `outerLimit` 做硬截断.
    - A2C-only 路线使用固定 fringe coverage ramp,Hybrid/Analytic 路线继续走导数驱动 coverage.
  - `Runtime/Shaders/GsplatLidarAlphaToCoverage.shader`
    - 明确为 coverage-first pass:
      - `RenderType=\"TransparentCutout\"`
      - `Blend One Zero`
      - `AlphaToMask On`
      - `#define GSPLAT_LIDAR_A2C_PASS 1`
- Tests:
  - `Tests/Editor/GsplatLidarShaderPropertyTests.cs`
    - 扩展字符串契约断言,锁定外扩 fringe 与 coverage-first shell 语义.
- Docs:
  - `README.md` / `CHANGELOG.md`
    - 同步说明“非 Legacy AA 模式会给点边缘留出一小圈 outer fringe”.

### 回归(证据)
- Unity 6000.3.8f1, EditMode `Gsplat.Tests`
  - total=`82`, passed=`80`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_particle_aa_v3_2026-03-08_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_particle_aa_v3_2026-03-08_noquit.log`

## 2026-03-08 14:33:40 +0800
- RadarScan 粒子 AA 的外扩 fringe 已从 shader 内部常数升级为正式调参项:
  - 新增 `LidarParticleAAFringePixels`
  - 默认 `1.0`
  - `0` 表示不额外外扩,更接近“只在原 footprint 内部软化”

### 变更内容
- Runtime:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
    - 新增 `LidarParticleAAFringePixels` 字段与校验逻辑.
    - LiDAR draw 调用改为把该值下发到 `GsplatLidarScan.RenderPointCloud(...)`.
  - `Runtime/Lidar/GsplatLidarScan.cs`
    - 新增 `_LidarParticleAAFringePixels` property ID.
    - MPB 下发该值到 shader.
    - Editor AA 诊断日志新增 `fringePx=...`.
- Shader:
  - `Runtime/Shaders/GsplatLidar.shader`
  - `Runtime/Shaders/GsplatLidarAlphaToCoverage.shader`
    - 新增隐藏属性 `_LidarParticleAAFringePixels`.
  - `Runtime/Shaders/GsplatLidarPassCore.hlsl`
    - 顶点/片元阶段都改为读取 `_LidarParticleAAFringePixels`,替代原先写死的 `1.0`.
- Editor:
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`
    - LiDAR 面板新增 `LidarParticleAAFringePixels` 输入框.
    - helpbox 补充“值越大,AA fringe 越明显”的说明.
- Tests / Docs:
  - `Tests/Editor/GsplatLidarScanTests.cs`
  - `Tests/Editor/GsplatLidarShaderPropertyTests.cs`
  - `README.md`
  - `CHANGELOG.md`

### 回归(证据)
- Unity 6000.3.8f1, EditMode `Gsplat.Tests`
  - total=`82`, passed=`80`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_particle_aa_fringe_param_2026-03-08.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_particle_aa_fringe_param_2026-03-08.log`

## 2026-03-08 19:39:40 +0800
- 修复 frustum external mesh 的 RadarScan 粒子“贴到 mesh 背面”的回归.

### 根因
- `Runtime/Shaders/GsplatLidarExternalCapture.shader` 原来使用 `Cull Back`.
- 该 shader 运行在手动 VP 的离屏 capture 路径中:
  - `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`
  - `CommandBuffer.SetViewProjectionMatrices(view, projection)`
- 在 RT flip / 手性 / 负缩放等组合下,依赖 `Cull Back` 容易把 front/back 判定翻掉.
- 一旦翻掉,external hit 会稳定来自 mesh 背面,最后 RadarScan 粒子也就“贴到背后”.

### 修复内容
- Shader:
  - `Runtime/Shaders/GsplatLidarExternalCapture.shader`
    - capture pass 从 `Cull Back` 改为 `Cull Off`
    - 增加中文注释,说明要让 depth buffer 选择最近可见表面
- Tests:
  - `Tests/Editor/GsplatLidarExternalGpuCaptureTests.cs`
    - 新增 `ExternalGpuCaptureShader_UsesCullOffToPreferNearestVisibleSurface`
    - 锁定 hidden capture shader 不得再回退为依赖背面剔除

### 回归(证据)
- Unity 6000.3.8f1, EditMode `Gsplat.Tests`
  - total=`83`, passed=`81`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_capture_culloff_v2_2026-03-08.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_external_capture_culloff_v2_2026-03-08.log`

## 2026-03-08 00:23:12 +0800
- 继续实现 OpenSpec change: `lidar-camera-frustum-external-gpu-scan`
  - 本轮完成:
    - `2.1`
    - `2.2`
    - `2.3`
    - `2.4`
    - `2.5`
    - `6.1`
    - `6.2`

### 变更内容
- Runtime:
  - `Runtime/Lidar/GsplatLidarScan.cs`
    - 引入统一的 `GsplatLidarLayout`,把 active counts + azimuth/beam angle ranges 绑定成一份共享语义.
    - `EnsureRangeImageBuffers` / `EnsureLutBuffers` / `TryRebuildRangeImage` / `RenderPointCloud` 改为直接吃 layout.
    - compute 下发新增 `_LidarAzimuthMinRad` / `_LidarAzimuthMaxRad`.
    - 新增 `RangeCellCount`,用于在 layout 失效或 external targets 为空时安全清空当前 external hit buffers.
  - `Runtime/GsplatRenderer.cs`
    - 新增 `TryGetEffectiveLidarLayout` 与 `TryGetEffectiveLidarRuntimeContext`.
    - LiDAR resource/tick/draw/external sync 统一切到 `layout + sensor frame` 主链.
    - frustum camera 无效时明确 warning 并禁用新路径,不再进入不确定状态.
  - `Runtime/GsplatSequenceRenderer.cs`
    - 与 `GsplatRenderer` 同步上述 layout/runtime context 改造.
  - `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs`
    - CPU fallback ray directions 改为按 layout 角域生成,不再硬编码 360 + `[downFov,upFov]`.
    - 方向缓存新增 azimuth min/max 维度,避免 frustum 水平角变化时误复用旧方向数组.
- Shader / Compute:
  - `Runtime/Shaders/Gsplat.compute`
    - `TryComputeLidarCell` 改为按 layout azimuth range 落 bin,不再固定 `[-pi,pi]`.
  - `Runtime/Shaders/GsplatLidar.shader`
    - draw 的扫描头年龄改为按真实 360 方位角计算.
    - 目的: 避免 frustum 模式因 active bins 变少而把 `RotationHz` 视觉语义偷偷加速.
- Tests:
  - `Tests/Editor/GsplatLidarScanTests.cs`
    - 新增 frustum active-cell count 推导测试.
    - 新增 orthographic frustum camera 失效测试.
    - 更新 external helper 反射调用,改为传入新的 surround layout.

### 回归(证据)
- OpenSpec:
  - `openspec instructions apply --change "lidar-camera-frustum-external-gpu-scan" --json`
  - progress: `12 / 35`
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests.GsplatLidarScanTests`
  - total=31, passed=31, failed=0, skipped=0
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_frustum_layout_2026-03-08_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_frustum_layout_2026-03-08_noquit.log`
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=74, passed=72, failed=0, skipped=2
  - skipped=2 为既有 ignore:
    - `GsplatMetalBufferBindingTests.Render_DoesNotTrigger_MetalMissingComputeBufferWarning`
    - `GsplatVfxBinderTests.AutoAssignsDefaultVfxComputeShader_WhenMissing`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_frustum_layout_full_2026-03-08_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_frustum_layout_full_2026-03-08_noquit.log`

## 2026-03-07 01:09:26 +0800
- 为“RadarScan 支持 `GameObject[]` 外部模型参与扫描”的方案创建了新的 OpenSpec change:
  - name: `lidar-external-targets`
  - location: `openspec/changes/lidar-external-targets/`
  - schema: `spec-driven`
- 当前 artifact 进度:
  - `proposal`: ready
  - `design`: blocked by `proposal`
  - `specs`: blocked by `proposal`
  - `tasks`: blocked by `design, specs`
- 已读取首个 artifact `proposal` 的 instructions.
  - 输出路径: `openspec/changes/lidar-external-targets/proposal.md`
  - 下一步应围绕“为什么现在需要让外部模型与 gsplat 一起参与 LiDAR first return”起草 proposal.

## 2026-03-07 01:13:40 +0800
- 使用 `$openspec-ff-change` 完成 `lidar-external-targets` 的全部 OpenSpec artifacts,当前 change 已 apply-ready.

### 已生成 artifacts
- `openspec/changes/lidar-external-targets/proposal.md`
  - 锁定了能力边界: `LidarExternalTargets : GameObject[]`、真实 mesh 命中、与 gsplat 的 first return 竞争、外部命中材质主色.
- `openspec/changes/lidar-external-targets/design.md`
  - 固定了技术路线: 保留现有 gsplat range image,新增“隔离 PhysicsScene + 真实 mesh 代理 + RaycastCommand”外部命中源,并在 draw 阶段合并.
- `openspec/changes/lidar-external-targets/specs/gsplat-lidar-external-targets/spec.md`
  - 落下了 capability requirement: 外部目标收集、真实 mesh、per-cell 最近距离竞争、材质主色、联合 bounds、HideSplatsWhenLidarEnabled 下的持续扫描.
- `openspec/changes/lidar-external-targets/tasks.md`
  - 将实现拆成 5 组 checklist,覆盖 API/Inspector、runtime helper、hit 合并、颜色与 bounds、测试文档.

### 状态确认
- `openspec status --change lidar-external-targets`
  - progress: `4/4 artifacts complete`
  - result: `All artifacts complete!`

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

## 2026-03-03 14:30:00 +0800
- RadarScan(LiDAR) 支持独立的 ShowDuration/HideDuration(开关淡入淡出),不再强绑定 RenderStyleSwitchDurationSeconds.

### 背景与目标

- 用户需求: RadarScan 的 show/hide 时长要独立设置.
- 约束: 不能影响高斯/ParticleDots 的显隐燃烧环动画 `ShowDuration/HideDuration`.
- 兼容策略: 默认保持旧行为.

### 变更内容

- Runtime: 新增 LiDAR 专用时长字段
  - [GsplatRenderer.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Runtime/GsplatRenderer.cs)
  - [GsplatSequenceRenderer.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Runtime/GsplatSequenceRenderer.cs)
  - 新增字段:
    - `LidarShowDuration`(show 淡入时长)
    - `LidarHideDuration`(hide 淡出时长)
  - 默认值 `-1`: 表示复用 `RenderStyleSwitchDurationSeconds`(兼容旧项目).
  - `SetRadarScanEnabled()` 行为:
    - 当 `durationSeconds < 0` 时:
      - show: 若 `LidarShowDuration >= 0` 则用它,否则回退到 `RenderStyleSwitchDurationSeconds`.
      - hide: 若 `LidarHideDuration >= 0` 则用它,否则回退到 `RenderStyleSwitchDurationSeconds`.
    - 当 `durationSeconds >= 0` 时: 强制使用 override.
  - `ValidateLidarSerializedFields()` 增加 NaN/Inf/负数防御,把负数统一归一为 `-1`.

- Editor: LiDAR 面板暴露新字段
  - [GsplatRendererEditor.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Editor/GsplatRendererEditor.cs)
  - [GsplatSequenceRendererEditor.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Editor/GsplatSequenceRendererEditor.cs)
  - 在 LiDAR 的 "Timing" 区域新增:
    - `LidarShowDuration`
    - `LidarHideDuration`
  - 同步更新 RenderStyle 区域提示文案,解释 RadarScan 的时长来源.

- Tests:
  - [GsplatLidarScanTests.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Tests/Editor/GsplatLidarScanTests.cs)
    - clamp 测试覆盖新字段.
    - 新增测试锁定 "override/fallback" 的决策逻辑.

### 回归(证据)

- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`):
  - total=52, passed=50, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_duration_overrides_2026-03-03_142917_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_duration_overrides_2026-03-03_142917_noquit.log`

## 2026-03-03 14:33:41 +0800
- 补强 Inspector 体验: 即便 EnableLidarScan=false,也能看到并设置 `LidarShowDuration/LidarHideDuration`.

### 变更内容

- Editor:
  - [GsplatRendererEditor.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Editor/GsplatRendererEditor.cs)
  - [GsplatSequenceRendererEditor.cs](/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Packages/wu.yize.gsplat/Editor/GsplatSequenceRendererEditor.cs)
  - 在 LiDAR 面板中新增 "Transition" 小节.
  - `EnableLidarScan` 关闭时也显示 `LidarShowDuration/LidarHideDuration`,避免用户找不到入口.

### 回归(证据)

- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`):
  - total=52, passed=50, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_duration_inspector_2026-03-03_143314_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_duration_inspector_2026-03-03_143314_noquit.log`

## 2026-03-03 15:43:07 +0800
- LiDAR(RadarScan): 增加"未扫到区域底色强度"与"是否保留"选项,用于解决"扫过后变黑".
  - 新增字段:
    - `LidarKeepUnscannedPoints`(开关)
    - `LidarUnscannedIntensity`(底色强度)
  - Shader:
    - `Runtime/Shaders/GsplatLidar.shader` 新增 `_LidarUnscannedIntensity`.
    - 每点强度改为 `lerp(unscannedIntensity, scanIntensity, trail)`.
  - Runtime:
    - `Runtime/Lidar/GsplatLidarScan.cs` 增加 `_LidarUnscannedIntensity` MPB 下发.
    - `Runtime/GsplatRenderer.cs`/`Runtime/GsplatSequenceRenderer.cs` 增加 Keep 决策函数,并把最终值传入 LiDAR draw.
  - Editor:
    - `Editor/GsplatRendererEditor.cs`/`Editor/GsplatSequenceRendererEditor.cs` 暴露新字段,并提示语义.
  - Tests:
    - `Tests/Editor/GsplatLidarShaderPropertyTests.cs` 增加 `_LidarUnscannedIntensity` 属性契约断言.
    - `Tests/Editor/GsplatLidarScanTests.cs` 增加 clamp 断言与 Keep 决策逻辑回归.

### 回归(证据)
- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`): total=54, passed=52, failed=0, skipped=2
- XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_unscanned_intensity_2026-03-03_154114_noquit.xml`
- log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_unscanned_intensity_2026-03-03_154114_noquit.log`

## 2026-03-03 16:11:53 +0800
- LiDAR(RadarScan): `LidarIntensity`/`LidarUnscannedIntensity` 支持按距离衰减(近强远弱),并提供两个可调的衰减乘数.

### 变更内容
- Runtime:
  - `Runtime/GsplatRenderer.cs`/`Runtime/GsplatSequenceRenderer.cs` 新增字段:
    - `LidarIntensityDistanceDecay`
    - `LidarUnscannedIntensityDistanceDecay`
  - `ValidateLidarSerializedFields()` 增加 NaN/Inf/负数防御,非法值回退为 0(不衰减).
  - `Runtime/Lidar/GsplatLidarScan.cs` 下发到 shader:
    - `_LidarIntensityDistanceDecay`
    - `_LidarUnscannedIntensityDistanceDecay`
- Shader:
  - `Runtime/Shaders/GsplatLidar.shader`:
    - 新增 `_LidarIntensityDistanceDecay/_LidarUnscannedIntensityDistanceDecay`.
    - 强度衰减函数:
      - `atten(dist)=1/(1+dist*decay)`
    - 最终强度语义:
      - `scanIntensity=LidarIntensity*atten(range, LidarIntensityDistanceDecay)`
      - `unscannedIntensity=LidarUnscannedIntensity*atten(range, LidarUnscannedIntensityDistanceDecay)`
      - `lerp(unscannedIntensity, scanIntensity, trail)`
- Editor:
  - `Editor/GsplatRendererEditor.cs`/`Editor/GsplatSequenceRendererEditor.cs` 暴露两个衰减字段,并提示 0 表示不衰减.
- Tests:
  - `Tests/Editor/GsplatLidarShaderPropertyTests.cs` 扩展属性契约断言.
  - `Tests/Editor/GsplatLidarScanTests.cs` 扩展 clamp 回归覆盖.

### 回归(证据)
- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`): total=54, passed=52, failed=0, skipped=2
- XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_distance_decay_2026-03-03_161038_noquit.xml`
- log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_distance_decay_2026-03-03_161038_noquit.log`

## 2026-03-03 18:16:10 +0800
- LiDAR(RadarScan): 距离衰减增加"指数衰减"模式,并提供可切换选项.
  - 新增字段:
    - `LidarIntensityDistanceDecayMode`(`Reciprocal` / `Exponential`,默认 `Reciprocal`).
  - 说明:
    - 继续复用 `LidarIntensityDistanceDecay/LidarUnscannedIntensityDistanceDecay` 两个衰减乘数.
    - 仅改变距离衰减函数形态,不改变 decay 参数语义.

### 变更内容

- Runtime:
  - `Runtime/GsplatUtils.cs`: 新增 enum `GsplatLidarDistanceDecayMode`.
  - `Runtime/GsplatRenderer.cs`/`Runtime/GsplatSequenceRenderer.cs`:
    - 新增 `LidarIntensityDistanceDecayMode` 字段(默认 Reciprocal).
    - `ValidateLidarSerializedFields()` 增加非法 enum 防御,回退到 `Reciprocal`.
    - LiDAR draw 提交时把 mode 传入 `GsplatLidarScan.RenderPointCloud(...)`.
  - `Runtime/Lidar/GsplatLidarScan.cs`:
    - 新增 shader property `_LidarIntensityDistanceDecayMode` 下发(0=Reciprocal,1=Exponential).
- Shader:
  - `Runtime/Shaders/GsplatLidar.shader`:
    - 新增 `_LidarIntensityDistanceDecayMode`.
    - 按模式选择距离衰减函数:
      - Reciprocal: `atten(dist)=1/(1+dist*decay)`
      - Exponential: `atten(dist)=exp(-dist*decay)`(实现上使用 `exp2(-dist*decay/ln(2))`)
- Editor:
  - `Editor/GsplatRendererEditor.cs`/`Editor/GsplatSequenceRendererEditor.cs`:
    - LiDAR 面板暴露 `LidarIntensityDistanceDecayMode` 下拉.
    - helpbox 同步提示两种衰减函数与 `decay=0` 语义.
- Tests:
  - `Tests/Editor/GsplatLidarShaderPropertyTests.cs`: 扩展属性契约断言,覆盖 `_LidarIntensityDistanceDecayMode`.
  - `Tests/Editor/GsplatLidarScanTests.cs`: 增加非法 enum 回退到 `Reciprocal` 的回归断言.
- Docs:
  - `README.md`/`CHANGELOG.md` 同步新增字段与说明.

### 回归(证据)

- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`): total=54, passed=52, failed=0, skipped=2
- XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_distance_decay_mode_2026-03-03_181542_noquit.xml`
- log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_distance_decay_mode_2026-03-03_181542_noquit.log`

## 2026-03-07 02:19:33 +0800
- 落地 OpenSpec change: `lidar-external-targets`(21/21 tasks complete, `openspec instructions apply` 已到 `all_done`).

### 变更内容
- Runtime:
  - `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs`
    - 新增 external target helper.
    - 递归收集 `LidarExternalTargets` 根对象下的 `MeshRenderer + MeshFilter` / `SkinnedMeshRenderer`.
    - 静态 mesh 使用真实 `sharedMesh`,skinned mesh 使用 `BakeMesh()` 快照.
    - 维护 proxy collider + batch `RaycastCommand` 查询.
    - 支持 `_BaseColor` / `_Color` 主色提取与 `triangleIndex -> submesh -> material` 映射缓存.
    - EditMode 改为使用 preview scene 分支创建/释放 proxy scene,避免 `SceneManager.CreateScene` 在非 Play 模式下报错.
  - `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`
    - 接入 external helper 生命周期与 `LidarUpdateHz` 同步 tick.
    - 当 external targets 为空时主动清空 external hit buffer,并释放 helper.
    - 新增 `ResolveVisibilityLocalBoundsForThisFrame()`,让 splat visibility 与 LiDAR show/hide 都使用 gsplat + external targets 的联合 bounds.
  - `Runtime/Lidar/GsplatLidarScan.cs`
    - 修正 external hit clear 常量.
    - external hit buffers 继续作为 LiDAR draw 的必绑资源.
  - `package.json`
    - 新增依赖 `com.unity.modules.physics`.
    - 目的: external targets 依赖 `PhysicsScene` / `MeshCollider` / `RaycastCommand`,不能再假设宿主工程已经手工启用 Physics 内建包.
- Shader:
  - `Runtime/Shaders/GsplatLidar.shader`
    - external hit 与 gsplat hit 按每个 cell 竞争最近距离.
    - `Depth` 模式走统一深度色.
    - `SplatColorSH0` 模式下,external hit 使用 external base color,gsplat hit 继续使用 SH0.
- Tests:
  - `Tests/Editor/GsplatLidarScanTests.cs`
    - 新增联合 bounds 回归.
    - 新增 external helper 最小 physics 集成测试(无 source collider 的 mesh 也能命中,并返回材质主色).
  - `Tests/Editor/GsplatUtilsTests.cs`
    - 新增 `TransformBounds(...)` 几何回归.
  - `Tests/Editor/GsplatLidarShaderPropertyTests.cs`
    - 新增 shader 文本契约测试,锁定 external hit 最近距离竞争表达式.
- Docs / Spec:
  - `README.md`: 补充 `LidarExternalTargets` 用法、颜色语义、SkinnedMesh 行为与验证清单.
  - `CHANGELOG.md`: 记录 external target RadarScan 能力与 Physics 依赖.
  - `openspec/changes/lidar-external-targets/tasks.md`: 21/21 全部完成.

### 回归(证据)
- Unity 6000.3.8f1, EditMode `Gsplat.Tests.GsplatLidarScanTests`
  - total=18, passed=18, failed=0, skipped=0
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_targets_lidarscan_2026-03-07_0234_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_external_targets_lidarscan_2026-03-07_0234_noquit.log`
- Unity 6000.3.8f1, EditMode `Gsplat.Tests.GsplatUtilsTests`
  - total=8, passed=8, failed=0, skipped=0
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_targets_utils_2026-03-07_0234_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_external_targets_utils_2026-03-07_0234_noquit.log`
- Unity 6000.3.8f1, EditMode `Gsplat.Tests`
  - total=61, passed=59, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_targets_all_2026-03-07_0238_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_external_targets_all_2026-03-07_0238_noquit.log`

## 2026-03-07 02:24:34 +0800
- 对 `lidar-external-targets` 做了一次 fresh verification 收尾,确认这次变更已经可以进入 archive / commit 阶段.

### fresh verification
- OpenSpec:
  - `openspec status --change "lidar-external-targets" --json`
    - `schemaName=spec-driven`
    - `isComplete=true`
    - `proposal/design/specs/tasks` 全部 `done`
  - `openspec instructions apply --change "lidar-external-targets" --json`
    - `progress=21/21`
    - `state=all_done`
- Unity:
  - Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=61, passed=59, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_targets_all_2026-03-07_022404_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_external_targets_all_2026-03-07_022404_noquit.log`
  - 日志确认: `Test run completed. Exiting with code 0 (Ok).`

### 收尾结论
- `LATER_PLANS.md` 已回看,本轮无需新增延期项.
- 当前未跟踪文件均属于本轮新增成果,需要在后续提交时一并纳入版本控制:
  - `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs`
  - `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs.meta`
  - `openspec/changes/lidar-external-targets.meta`
  - `openspec/changes/lidar-external-targets/`

## 2026-03-07 12:16:43 +0800
- 补完 `lidar-external-targets` 的外部目标“scan-only 可见性”语义.
  - 目标: 让参与 RadarScan 的 external mesh 默认不再显示普通 mesh shader,但继续参与 LiDAR 扫描.

### 变更内容
- Runtime:
  - `Runtime/GsplatUtils.cs`
    - 新增 enum: `GsplatLidarExternalTargetVisibilityMode`
      - `KeepVisible`
      - `ForceRenderingOff`
  - `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`
    - 新增字段 `LidarExternalTargetVisibilityMode`
    - 默认值: `ForceRenderingOff`
    - `ValidateLidarSerializedFields()` 增加非法 enum 回退
    - external helper 调用链新增 visibility mode 参数传递
  - `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs`
    - helper 现在会追踪每个 source renderer 的原始 `forceRenderingOff`
    - `ForceRenderingOff` 时隐藏普通 mesh 显示,但保持 external scan 正常
    - 在目标移除 / helper Dispose 时恢复原始状态,避免污染用户场景
- Editor:
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`
    - LiDAR Inputs 区域新增 visibility mode 配置
    - helpbox 明确 `KeepVisible / ForceRenderingOff` 语义
- Tests:
  - `Tests/Editor/GsplatLidarScanTests.cs`
    - 增加新组件默认值测试(默认 `ForceRenderingOff`)
    - 扩展 validate 回归,锁定非法 enum 会回退到 `ForceRenderingOff`
    - 扩展 helper 集成测试,锁定“隐藏 source renderer + Dispose 后恢复原状态”
- Docs / Spec:
  - `README.md`
  - `CHANGELOG.md`
  - `openspec/changes/lidar-external-targets/{proposal.md,design.md,tasks.md}`
  - `openspec/changes/lidar-external-targets/specs/gsplat-lidar-external-targets/spec.md`

### 回归(证据)
- Unity 6000.3.8f1, EditMode `Gsplat.Tests.GsplatLidarScanTests`
  - total=20, passed=20, failed=0, skipped=0
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_visibility_lidarscan_2026-03-07_121526_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_external_visibility_lidarscan_2026-03-07_121526_noquit.log`
- Unity 6000.3.8f1, EditMode `Gsplat.Tests`
  - total=63, passed=61, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_visibility_all_2026-03-07_121556_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_external_visibility_all_2026-03-07_121556_noquit.log`
- OpenSpec:
  - `openspec instructions apply --change "lidar-external-targets" --json`
  - 结果: `progress=26/26`, `state=all_done`

## 2026-03-07 13:20:40 +0800
- external target 可见性模式继续扩展为三态,新增 `ForceRenderingOffInPlayMode`.
  - 语义:
    - `KeepVisible`: 一直显示普通 mesh
    - `ForceRenderingOff`: 一直只保留 scan-only
    - `ForceRenderingOffInPlayMode`: 编辑器非 Play 显示普通 mesh,Play 模式自动切成 scan-only

### 变更内容
- Runtime:
  - `Runtime/GsplatUtils.cs`
    - `GsplatLidarExternalTargetVisibilityMode` 新增 `ForceRenderingOffInPlayMode`
  - `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs`
    - 抽出 `ShouldForceSourceRendererOff(mode, isPlaying)` 决策
    - `ForceRenderingOffInPlayMode` 时仅在 `Application.isPlaying=true` 才强制 `forceRenderingOff`
  - `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`
    - tooltip 与 `ValidateLidarSerializedFields()` 同步接受第三态
- Editor:
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`
    - Inspector helpbox 改为三态说明
- Tests:
  - `Tests/Editor/GsplatLidarScanTests.cs`
    - 新增 `ExternalTargetVisibilityMode_PlayModeOnly_OnlyForcesOffWhenPlaying`
- Docs / Spec:
  - `README.md`
  - `CHANGELOG.md`
  - `openspec/changes/lidar-external-targets/{proposal.md,design.md,tasks.md}`
  - `openspec/changes/lidar-external-targets/specs/gsplat-lidar-external-targets/spec.md`

### 回归(证据)
- Unity 6000.3.8f1, EditMode `Gsplat.Tests.GsplatLidarScanTests`
  - total=21, passed=21, failed=0, skipped=0
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_visibility_playonly_lidarscan_2026-03-07_131944_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_external_visibility_playonly_lidarscan_2026-03-07_131944_noquit.log`
- Unity 6000.3.8f1, EditMode `Gsplat.Tests`
  - total=64, passed=62, failed=0, skipped=2
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_visibility_playonly_all_2026-03-07_132009_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_external_visibility_playonly_all_2026-03-07_132009_noquit.log`
- OpenSpec:
  - `openspec instructions apply --change "lidar-external-targets" --json`
  - 结果: `progress=31/31`, `state=all_done`

## 2026-03-07 14:16:40 +0800
- 新建 OpenSpec change:
  - name: `lidar-camera-frustum-external-gpu-scan`
  - schema: `spec-driven`
- 该 change 用于承接下一阶段的 RadarScan 性能优化路线:
  - 扫描口径从 360 度转为 camera frustum
  - external target 拆分 static / dynamic
  - external mesh 扫描优先考虑 GPU depth/color 路线

### 当前状态
- change 路径:
  - `openspec/changes/lidar-camera-frustum-external-gpu-scan/`
- 当前 artifact 进度:
  - `0/4`
- 当前 ready artifact:
  - `proposal`

## 2026-03-07 14:44:00 +0800
- 一次性补完 OpenSpec change `lidar-camera-frustum-external-gpu-scan` 的最后一个 artifact:`tasks.md`.

### 产出文件
- `openspec/changes/lidar-camera-frustum-external-gpu-scan/tasks.md`

### 任务拆分结果
- 共 6 组任务:
  - `Aperture API 与 Inspector`
  - `Frustum active cells 与 LiDAR 资源`
  - `External GPU capture 基础设施`
  - `Static / Dynamic 更新策略与 fallback`
  - `external / gsplat 命中合并与最终显示语义`
  - `测试、文档与验证`
- 任务内容已经覆盖:
  - camera frustum aperture mode
  - static / dynamic external target arrays
  - dynamic 独立更新频率
  - external GPU depth/color capture
  - external / gsplat nearest-hit 合并
  - visibility / show-hide / fallback / docs / tests

### OpenSpec 状态(证据)
- `openspec status --change "lidar-camera-frustum-external-gpu-scan" --json`
  - `schemaName=spec-driven`
  - `isComplete=true`
  - `proposal=done`
  - `design=done`
  - `specs=done`
  - `tasks=done`

### 收尾说明
- 本轮只完成规格起草,没有开始代码实现.
- 现在这个 change 已经从 `3/4 artifacts complete` 进入 `4/4 artifacts complete`,后续可以直接进入 apply / implementation.

## 2026-03-07 14:50:56 +0800
- 对 `lidar-camera-frustum-external-gpu-scan` 做了一次实现可行性审查,重点复核 external GPU depth/color 路线是否存在方向性问题.

### 审查结论
- 总体方向正确:
  - `camera frustum + external GPU capture` 仍然是比继续堆 CPU `RaycastCommand` 更合理的性能路线.
- 但在真正开始 apply 前,建议先补齐 4 个关键设计约束:
  - 明确 `frustum camera` 与 `LidarOrigin` 的外参关系
  - 明确 external depth 到 LiDAR range 的换算契约
  - 明确 `color RT` 如何保持当前“材质主色”语义
  - 扩充 static capture 的 invalidation 条件

### 说明
- 本轮仍未开始代码实现.
- 这次产出属于“规格复核与风险前移”,目的是避免后续实现出一个“能跑但几何/颜色语义错位”的 GPU 版本.

## 2026-03-07 15:40:54 +0800
- 按审查结论继续补强 `lidar-camera-frustum-external-gpu-scan` 的 artifacts,把 4 个关键约束正式写进 design/spec/tasks.

### 本轮补强内容
- `design.md`
  - 明确 frustum mode 的 sensor-frame 契约:
    - `LidarOrigin` 仍是 beam origin
    - frustum camera 提供 aperture 朝向与 projection
    - external GPU capture 使用 synthetic frustum view
  - 明确 external depth 不能直接拿 hardware depth 充当 LiDAR range
  - 明确 `surfaceColor RT` 只保留 `_BaseColor` / `_Color` 主色语义
  - 明确 static invalidation 需要覆盖 renderer/material/capture-layout 变化
- `spec.md`
  - 新增 requirement/scenario:
    - frustum sensor-frame contract
    - external depth -> LiDAR ray-distance 语义转换
    - GPU main-color capture 语义
    - static capture 的完整 invalidation 条件
    - scan-only hidden mesh 仍参与 GPU capture
- `tasks.md`
  - 新增或改写任务,把上述契约拆成可执行项与回归测试项

### OpenSpec 状态
- `openspec status --change "lidar-camera-frustum-external-gpu-scan" --json`
  - `isComplete=true`
  - `proposal/design/specs/tasks = done`

### 说明
- 本轮仍未开始实现代码.
- 重点是先把“应该怎么做才正确”收紧,避免仓促落地后在几何语义和颜色语义上返工.

## 2026-03-07 16:40:12 +0800
- 根据最新设计决定,再次修订 `lidar-camera-frustum-external-gpu-scan` 的 sensor-frame 契约:
  - frustum 模式下,直接使用 frustum camera 的位置作为 LiDAR 原点
  - `LidarOrigin` 不再参与 frustum 模式原点定义
  - `LidarOrigin` 保留给旧 360 路线

### 同步修改
- `design.md`
- `spec.md`
- `tasks.md`

### 结果
- 当前 artifacts 已统一为:
  - frustum mode = camera position + rotation + projection
  - external GPU resolve 继续保持 LiDAR `depthSq` 语义
  - GPU color capture 继续保持 `_BaseColor` / `_Color` 主色语义
## 2026-03-07 17:23:00 +0800
- 完成一次“只读不改码”的 LiDAR frustum 最小接线分析.

### 分析范围
- 目标问题:
  - 若要最小代价新增 `frustum aperture mode + frustumCamera`
  - 并让现有 gsplat LiDAR compute/draw 链路先改用 camera pose(而不是 `LidarOrigin`)
  - 当前仓库里最关键要改哪些方法/字段/Inspector 点位

### 结论摘要
- `LidarOrigin` 当前同时参与:
  - compute pose(`modelToLidar`)
  - draw pose(`lidarLocalToWorld`)
  - external helper raycast origin
- 最小改动主战场:
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
  - `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs`
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`
- `Runtime/Lidar/GsplatLidarScan.cs` 与 `Runtime/Shaders/Gsplat.compute` 当前已经通过矩阵参数消费 sensor pose
  - 如果这一步只切 pose source,它们大概率不用先改
  - 但真正做“frustum active cells / workload reduction”时仍需要继续改 LUT 与 cell 生成逻辑

### 额外识别的高风险漏点
- 只改 compute 不改 draw -> range image 与点云世界位置错位
- 忘记 external helper -> external hit 与 gsplat hit 不在同一 sensor frame
- 忘记 Editor ticker / HelpBox / warning 文案 -> frustum 模式行为正常,但编辑器提示仍声称“缺少 LidarOrigin 无法渲染”

## 2026-03-07 23:38:00 +0800
- 起手落地 `lidar-camera-frustum-external-gpu-scan` 的第一批 apply 任务(1.1 ~ 1.5).

### 本轮实现
- Runtime:
  - `Runtime/GsplatUtils.cs`
    - 新增 `GsplatLidarApertureMode`(`Surround360` / `CameraFrustum`).
  - `Runtime/GsplatRenderer.cs`
    - 新增 `LidarApertureMode`、`LidarFrustumCamera`、`LidarExternalStaticTargets`、`LidarExternalDynamicTargets`、`LidarExternalDynamicUpdateHz`.
    - 用 `FormerlySerializedAs("LidarExternalTargets")` 把旧场景 external 输入迁到 static 组.
    - 保留旧 API 名 `LidarExternalTargets` 作为兼容 property,映射到 static 组.
    - 新增统一 sensor pose helper,让 compute/draw/external helper/EditMode repaint gate 都不再直接硬编码吃 `LidarOrigin`.
  - `Runtime/GsplatSequenceRenderer.cs`
    - 同步新增上述字段与兼容路径.
    - 同步切到统一 sensor pose helper,保持静态/序列两条 LiDAR 链路一致.
  - `Runtime/Lidar/GsplatLidarExternalTargetHelper.cs`
    - external CPU helper 的输入语义从 `lidarOrigin` 收敛为 `lidarSensorTransform`,避免和 gsplat 主链 frame 不一致.
- Editor:
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`
    - LiDAR 面板新增 aperture mode、frustum camera、static/dynamic arrays、dynamic updateHz.
    - warning/helpbox 改为按模式区分:
      - `Surround360` 看 `LidarOrigin`
      - `CameraFrustum` 看 `LidarFrustumCamera`
- Tests:
  - `Tests/Editor/GsplatLidarScanTests.cs`
    - 新增 aperture mode 默认值测试.
    - 新增旧 `LidarExternalTargets` 兼容 property 测试.
    - 新增 frustum 模式下 sensor pose 直接取 camera.transform 的回归测试.
    - 扩展 validate 测试,覆盖 aperture enum 回退、dynamic updateHz 默认值、static/dynamic 数组归一化.
    - 扩展 visibility bounds 测试,锁定 static/dynamic 两组 external targets 的联合范围.

### 验证
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests.GsplatLidarScanTests`
  - total=27, passed=27, failed=0, skipped=0
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_frustum_pose_start_2026-03-07.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_frustum_pose_start_2026-03-07.log`
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=70, passed=68, failed=0, skipped=2
  - 说明: skipped=2 为既有 Ignore,本轮没有新增失败
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_frustum_pose_full_2026-03-07.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_frustum_pose_full_2026-03-07.log`

### OpenSpec 同步
- `openspec/changes/lidar-camera-frustum-external-gpu-scan/tasks.md`
  - 已勾选完成:
    - `1.1`
    - `1.2`
    - `1.3`
    - `1.4`
    - `1.5`

## 2026-03-08 01:07:20 +0800
- 落地 OpenSpec change: `lidar-camera-frustum-external-gpu-scan` 的 `3.1 ~ 3.7`.
- 新增 frustum external GPU capture 主链:
  - `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`
    - 维护 static / dynamic external renderer 收集.
    - 用 explicit render list + override material + `CommandBuffer.DrawMesh(...)` 做离屏 capture.
    - 产出 depth RT / surfaceColor RT,再调用 compute resolve 写回 LiDAR external hit buffers.
  - `Runtime/Shaders/GsplatLidarExternalCapture.shader`
    - depth pass 输出正值 linear view depth.
    - surfaceColor pass 只保留 `_BaseColor` / `_Color` 主色语义.
  - `Runtime/Shaders/Gsplat.compute`
    - 新增 `ResolveExternalFrustumHits`.
    - 用“每 cell 一线程”的方式,把 capture RT 还原成 LiDAR `depthSq + baseColor`.
  - `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`
    - frustum 模式下优先走 GPU capture manager.
    - GPU 路线不可用时回退旧 CPU helper.
    - 补齐 manager 生命周期释放.
  - `Runtime/GsplatSettings.cs`
    - 增加 `LidarExternalCaptureShader` 与 `LidarExternalCaptureMaterial`.

### 回归(证据)
- Unity 6000.3.8f1,定向 EditMode tests:
  - `Gsplat.Tests.GsplatLidarScanTests`
  - total=`31`, passed=`31`, failed=`0`, skipped=`0`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_external_gpu_capture_targeted_2026-03-08.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_external_gpu_capture_targeted_2026-03-08.log`
- Unity 6000.3.8f1,全包 EditMode tests:
  - `Gsplat.Tests`
  - total=`74`, passed=`72`, failed=`0`, skipped=`2`
  - `skipped=2` 为既有 ignore,本轮无新增失败
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_external_gpu_capture_full_2026-03-08.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_external_gpu_capture_full_2026-03-08.log`

## 2026-03-08 02:38:00 +0800
- 继续完成 OpenSpec change: `lidar-camera-frustum-external-gpu-scan` 的剩余实现与收尾,最终把 apply 进度推进到 `35/35`.

### 变更内容
- Runtime:
  - `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`
    - 改成 static / dynamic 分组 capture state.
    - static 组新增完整 invalidation signature:
      - frustum camera pose / projection / aspect / pixelRect
      - capture RT layout
      - renderer state
      - transform / mesh / material main color
    - dynamic 组新增独立 cadence helper,按 `LidarExternalDynamicUpdateHz` 复用上一轮 capture.
    - resolve 阶段同时读取 static / dynamic 两组 RT,并选最近 external hit 写回 LiDAR buffers.
    - 新增 debug hooks,供 EditMode tests 反射锁定:
      - `DebugGetStaticCaptureDirtyReasonForInputs(...)`
      - `DebugCommitStaticCaptureSignatureForInputs(...)`
      - `DebugComputeRayDepthSqFromLinearViewDepth(...)`
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
    - external 更新链路改为每帧独立 tick.
    - GPU frustum 路线自己判断是否需要 capture / resolve.
    - CPU `RaycastCommand` fallback 仍只在 range rebuild 后刷新.
- Compute:
  - `Runtime/Shaders/Gsplat.compute`
    - `ResolveExternalFrustumHits` 升级为 static / dynamic 双路 external capture 最近命中比较.
    - 锁定写入语义是 LiDAR ray-distance 的 `depthSq`,而不是 raw camera hardware depth.
- Tests:
  - `Tests/Editor/GsplatLidarExternalGpuCaptureTests.cs`
    - 新增 static signature / dynamic cadence / depthSq helper / frustum bounds 回归.
- Docs:
  - `README.md`
    - 更新 LiDAR section:
      - `LidarApertureMode`
      - `LidarFrustumCamera`
      - `LidarExternalStaticTargets`
      - `LidarExternalDynamicTargets`
      - `LidarExternalDynamicUpdateHz`
      - GPU capture 语义与 CPU fallback 行为
  - `CHANGELOG.md`
    - 记录 frustum aperture、split external inputs、static reuse、dynamic cadence、独立 external tick.
- OpenSpec:
  - `openspec/changes/lidar-camera-frustum-external-gpu-scan/tasks.md`
    - 已勾选 `4.1 ~ 4.4`
    - 已勾选 `4.6`
    - 已勾选 `6.3 ~ 6.7`

### 回归(证据)
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests.GsplatLidar`
  - total=`38`, passed=`38`, failed=`0`, skipped=`0`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_external_gpu_capture_lidar_2026-03-08.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_external_gpu_capture_lidar_2026-03-08.log`
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`79`, passed=`77`, failed=`0`, skipped=`2`
  - `skipped=2` 为既有 ignore,本轮无新增失败
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_external_gpu_capture_full_2026-03-08_r2.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_external_gpu_capture_full_2026-03-08_r2.log`
- OpenSpec:
  - `openspec status --change "lidar-camera-frustum-external-gpu-scan" --json`
    - `isComplete = true`
  - `openspec instructions apply --change "lidar-camera-frustum-external-gpu-scan" --json`
    - `progress = 35 / 35`
    - `state = all_done`

## 2026-03-08 11:30:00 +0800
- 新建并一次性起草完成 OpenSpec change: `radarscan-particle-antialiasing-modes`.
- 已完成 artifacts:
  - `openspec/changes/radarscan-particle-antialiasing-modes/proposal.md`
  - `openspec/changes/radarscan-particle-antialiasing-modes/design.md`
  - `openspec/changes/radarscan-particle-antialiasing-modes/specs/gsplat-lidar-particle-antialiasing/spec.md`
  - `openspec/changes/radarscan-particle-antialiasing-modes/tasks.md`

### 本轮规格结论
- RadarScan 粒子 AA 作为组件内能力,本轮范围锁定为:
  - `LegacySoftEdge`
  - `AnalyticCoverage`
  - `AlphaToCoverage`
  - `AnalyticCoveragePlusAlphaToCoverage`
- 关键决策:
  - `AnalyticCoverage` 作为推荐路线,用 shader 本地 coverage AA 解决小半径点的边缘锯齿与闪烁.
  - `AlphaToCoverage` 只作为依赖 MSAA 的可选高质量模式.
  - 无 MSAA 时,A2C 相关模式统一回退到 `AnalyticCoverage`.
  - 本轮明确不把 FXAA / SMAA / TAA 作为 RadarScan 组件内选项.
  - 兼容优先: 默认保持 `LegacySoftEdge`,避免老场景升级后无提示变观感.

### OpenSpec 状态
- `openspec status --change "radarscan-particle-antialiasing-modes"`
  - `Progress: 4/4 artifacts complete`
  - `All artifacts complete`

## 2026-03-08 12:46:01 +0800
- 落地 OpenSpec change: `radarscan-particle-antialiasing-modes`.
- RadarScan(LiDAR) 粒子新增可选抗锯齿模式,同时保持旧场景兼容.

### 变更内容
- Runtime:
  - `Runtime/GsplatUtils.cs`
    - 新增 `GsplatLidarParticleAntialiasingMode`.
    - 新增 A2C 可用性判断、模式归一化与 effective mode fallback helper.
  - `Runtime/GsplatRenderer.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
    - 新增 `LidarParticleAntialiasingMode` 序列化字段.
    - validate 非法值时回退到 `LegacySoftEdge`.
    - draw 提交时同时传 requested / effective AA mode.
  - `Runtime/GsplatSettings.cs`
    - 新增 `LidarAlphaToCoverageShader`.
    - 新增 `LidarAlphaToCoverageMaterial`.
  - `Runtime/Lidar/GsplatLidarScan.cs`
    - `RenderPointCloud(...)` 接入 LiDAR AA 模式选择.
    - A2C 不可用时运行时回退到 `AnalyticCoverage`.
    - MPB 新增 `_LidarParticleAAAnalyticCoverage`.
- Shader:
  - `Runtime/Shaders/GsplatLidarPassCore.hlsl`
    - 抽出 LiDAR 粒子 shared core,避免普通 shell 与 A2C shell 双份漂移.
  - `Runtime/Shaders/GsplatLidar.shader`
    - 改为 include shared core.
    - 保留 `LegacySoftEdge`,并加入 `AnalyticCoverage`.
  - `Runtime/Shaders/GsplatLidarAlphaToCoverage.shader`
    - 新增 LiDAR A2C shell.
    - 明确声明 `AlphaToMask On`.
- Editor:
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`
    - LiDAR Visual 区域暴露 AA 模式.
    - helpbox 明确推荐 `AnalyticCoverage`,并说明 A2C 依赖 MSAA 与 fallback 语义.
- Tests:
  - `Tests/Editor/GsplatLidarScanTests.cs`
    - 覆盖默认值、非法值归一化、A2C fallback.
    - 修复 RT 清理顺序,避免 Unity 记录 `Camera.targetTexture` 生命周期错误.
  - `Tests/Editor/GsplatLidarShaderPropertyTests.cs`
    - 锁定 analytic coverage 路线与 `AlphaToMask On` 契约.
- Docs:
  - `README.md`
    - 新增各 AA 模式差异、推荐值与 MSAA 前提说明.
  - `CHANGELOG.md`
    - 记录 RadarScan 粒子新增抗锯齿模式.
- OpenSpec:
  - `openspec/changes/radarscan-particle-antialiasing-modes/tasks.md`
    - 已勾选 `19 / 19`

### 回归(证据)
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`82`, passed=`80`, failed=`0`, skipped=`2`
  - `skipped=2` 为既有 ignore,本轮无新增失败
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_particle_aa_2026-03-08_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_particle_aa_2026-03-08_noquit.log`
- OpenSpec:
  - `openspec instructions apply --change "radarscan-particle-antialiasing-modes" --json`
    - progress=`19 / 19`
    - state=`all_done`

## 2026-03-08 13:18:00 +0800
- 根据用户实测反馈“`AnalyticCoverage` / `AlphaToCoverage` 和 `LegacySoftEdge` 几乎没区别”,继续修正 RadarScan 粒子 AA 语义.

### 根因
- `AnalyticCoverage` 原实现直接在归一化 uv 上做 `fwidth`,对 `2px` 小点的可见差异偏弱.
- `AlphaToCoverage` 原 shell 只是“透明混合 LiDAR pass + `AlphaToMask On`”,不是真正的 coverage-first 设计,因此很难体现 A2C 的真实收益.

### 变更内容
- Shader:
  - `Runtime/Shaders/GsplatLidarPassCore.hlsl`
    - `AnalyticCoverage` 改为像素尺度 coverage:
      - `signedEdgePx = signedEdge * max(_LidarPointRadiusPixels, 1.0)`
      - `analyticWidthPx = fwidth(signedEdgePx)`
  - `Runtime/Shaders/GsplatLidarAlphaToCoverage.shader`
    - A2C shell 改为 coverage-first:
      - `RenderType="TransparentCutout"`
      - `Blend One Zero`
      - `AlphaToMask On`
      - 保留 `ZWrite On`
- Runtime:
  - `Runtime/Lidar/GsplatLidarScan.cs`
    - Editor 诊断扩展到所有非 `LegacySoftEdge` 模式.
    - 日志现在会显示:
      - requested / effective mode
      - shader 名称
      - analytic 开关
      - 当前 passMode 是 `alpha-blend` 还是 `coverage-first`
- Tests / Docs:
  - `Tests/Editor/GsplatLidarShaderPropertyTests.cs`
    - 锁定新的像素尺度 analytic coverage 公式.
    - 锁定 A2C shell 不再使用 `Blend SrcAlpha OneMinusSrcAlpha`.
  - `README.md`
  - `Editor/GsplatRendererEditor.cs`
  - `Editor/GsplatSequenceRendererEditor.cs`
  - `CHANGELOG.md`
    - 同步更新 A2C 现在是 coverage-first 语义,并说明 `AnalyticCoverage` 对小点更明显.

### 回归(证据)
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`82`, passed=`80`, failed=`0`, skipped=`2`
  - `skipped=2` 为既有 ignore,本轮无新增失败
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_particle_aa_v2_2026-03-08_noquit.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_particle_aa_v2_2026-03-08_noquit.log`

## 2026-03-08 21:47:41 +0800
- 收尾并确认本轮 RadarScan external capture / 粒子 AA 改动可以提交.

### 本轮最终结论
- external mesh 粒子跑到背面的真正根因,不是 `LidarExternalHitBiasMeters`,而是 frustum external GPU capture 在 reversed-Z 平台上仍使用了 forward-Z 语义.
- 修复方式:
  - `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`
    - 按 `SystemInfo.usesReversedZBuffer` 切换深度比较与 clear depth.
  - `Runtime/Shaders/GsplatLidarExternalCapture.shader`
    - depth pass 改成由材质属性驱动 `ZTest`
    - 保持 `Cull Off`,继续让硬件深度选最近可见表面.
- 为防止再次回归,补了真实功能测试:
  - `Tests/Editor/GsplatLidarExternalGpuCaptureTests.cs`
    - `ExternalGpuCaptureDepthPass_CenterPixelMatchesSphereFrontDepth`
    - 它直接锁定球体中心像素应该命中前表面深度.
- 按用户最终选择,保留 `LidarExternalHitBiasMeters`,但把默认值与 fallback 统一收回到 `0`.
- 同一轮也包含已完成的 RadarScan 粒子 AA 落地:
  - `LegacySoftEdge`
  - `AnalyticCoverage`
  - `AlphaToCoverage`
  - `AnalyticCoveragePlusAlphaToCoverage`
  - `LidarParticleAAFringePixels`

### 最终验证证据
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`86`, passed=`85`, failed=`0`, skipped=`1`
  - `skipped=1` 为既有 ignored,本轮无新增失败
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_full_external_bias_default_zero_2026-03-08.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_full_external_bias_default_zero_2026-03-08.log`

## 2026-03-09 17:37:45 +0800
- 修复 HDRP 场景下 RadarScan 粒子 `AlphaToCoverage` 被错误回退到 `AnalyticCoverage` 的问题.

### 根因结论
- 问题不在用户 HDRP 配置.
- 真正根因是我们之前把 `Camera.allowMSAA` 当成 LiDAR A2C 的硬门槛.
- 但 HDRP 会主动把这个字段设成 `false`,因为它把它视为 legacy MSAA 入口.
- 因此在 HDRP 下:
  - 就算 HDRP Asset + Frame Settings 已经打开 MSAA
  - 旧逻辑仍会错误打印 `allowMSAA=0 msaaSamples=1`
  - 然后把 `AlphaToCoverage` 回退成 `AnalyticCoverage`

### 本轮变更
- `Runtime/GsplatUtils.cs`
  - 新增统一 LiDAR MSAA helper:
    - `GetLidarParticleMsaaSampleCount(Camera)`
    - `GetLidarParticleMsaaDiagnosticSummary(Camera)`
  - HDRP 分支下:
    - 通过缓存反射调用 HDRP 内部 `FrameSettings.AggregateFrameSettings(...)`
    - 读取聚合后的 `FrameSettingsField.MSAA`
    - 读取 `GetResolvedMSAAMode(...)`
    - 若输出到 `RenderTexture`,再与 `targetTexture.antiAliasing` 取最小值,逼近真实 render target
- `Runtime/Lidar/GsplatLidarScan.cs`
  - LiDAR 粒子 AA 诊断日志改为复用统一 helper,不再把 `Camera.allowMSAA` 当成唯一事实来源.
- `Tests/Editor/Gsplat.Tests.Editor.asmdef`
  - 新增 HDRP version define,让测试程序集也能做 HDRP 条件编译.
- `Tests/Editor/GsplatLidarScanTests.cs`
  - 新增 HDRP 条件测试:
    - 锁定“即便 `Camera.allowMSAA=false`,HDRP 仍可通过 resolved Frame Settings 保持 A2C 生效”
- `Editor/GsplatRendererEditor.cs`
- `Editor/GsplatSequenceRendererEditor.cs`
- `README.md`
  - 补充 HDRP 下的口径说明: 要看 HD Frame Settings / resolved MSAA,不能只看 `Camera.allowMSAA`

### 验证(证据)
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests.GsplatLidarScanTests`
  - total=`33`, passed=`33`, failed=`0`, skipped=`0`
  - XML:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_hdrp_a2c_fix_2026-03-09.xml`
  - log:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_hdrp_a2c_fix_2026-03-09.log`
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`86`, passed=`83`, failed=`0`, skipped=`3`
  - `skipped=3` 为既有 ignore / 环境性 skip,本轮无新增失败
  - XML:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_full_lidar_hdrp_a2c_fix_2026-03-09_r2.xml`
  - log:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_full_lidar_hdrp_a2c_fix_2026-03-09_r2.log`

## 2026-03-09 22:18:00 +0800
- 修复真实项目里 `Gsplat.Tests.Editor` 因 HDRP 测试源码直接引用 `UnityEngine.Rendering.HighDefinition` 导致的 `CS0234` 编译失败.

### 根因结论
- 本轮新增的 HDRP 专项测试把“可选 HDRP 依赖”误写成了“测试程序集的编译时硬依赖”.
- `versionDefines` 只能控制源码片段是否参与编译,不能自动补齐 HDRP 程序集引用.
- 所以在真实项目里,测试程序集会先在 `using UnityEngine.Rendering.HighDefinition;` 处直接编译失败.

### 本轮变更
- `Tests/Editor/GsplatLidarScanTests.cs`
  - 删除对 `UnityEngine.Rendering.HighDefinition` 的直接编译期依赖.
  - 新增一组通用反射 helper:
    - 运行时扫描 HDRP 类型
    - 读取/写入反射字段与属性
    - 配置 `FrameSettingsOverrideMask.mask`
  - HDRP A2C 专项测试改成运行时反射版:
    - 有 HDRP 且当前激活 HDRP pipeline 时,继续验证 resolved frame settings 语义
    - 没有 HDRP 时,按预期 `Ignore`
- `Tests/Editor/Gsplat.Tests.Editor.asmdef`
  - 移除本轮新增的 HDRP `versionDefines`,避免继续制造“伪条件依赖”

### 验证证据
- 当前真实项目:
  - `dotnet build Gsplat.Tests.Editor.csproj -nologo`
  - 结果:
    - `0 errors`
    - `4 warnings`
  - 说明用户看到的 `CS0234` 已消失
- `_tmp_gsplat_pkgtests`:
  - Unity 6000.3.8f1 定向 EditMode:
    - `Gsplat.Tests.GsplatLidarScanTests.GetLidarParticleMsaaSampleCount_HdrpUsesResolvedFrameSettingsEvenWhenCameraAllowMsaaIsFalse`
  - XML:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_hdrp_reflection_fix_2026-03-09.xml`
  - 结果=`skipped`
  - reason=`HDRP package is not loaded, skipping HDRP-specific LiDAR A2C test.`
  - 这证明反射版测试可以被 Unity TestRunner 正常发现和执行,在非 HDRP 工程里只会跳过,不会再拖垮编译。

## 2026-03-11 16:55:40 +0800 任务名称: 新建 OpenSpec 变更 `sog4d-single-frame-ply-support`

### 任务内容
- 为“单帧 `.ply` 转 `.sog4d`,并在 Unity 正常显示与使用”创建新的 OpenSpec change.
- 明确本次工作与既有 `sog4d` 主链路的关系,避免把补边界误写成重做格式.

### 完成过程
- 回读了当前上下文文件与 `sog4d` 相关 spec:
  - `task_plan.md`
  - `WORKLOG.md`
  - `notes.md`
  - `LATER_PLANS.md`
  - `ERRORFIX.md`
  - `openspec/specs/sog4d-container/spec.md`
  - `openspec/specs/sog4d-unity-importer/spec.md`
- 检查了仓库现状:
  - 已存在 `Tools~/Sog4D/ply_sequence_to_sog4d.py`
  - 已存在 `Editor/GsplatSog4DImporter.cs` 与 `Runtime/GsplatSog4DRuntimeBundle.cs`
  - 归档 change `2026-02-18-sog4d-sequence-format` 已完成序列主链路
- 记录了两条候选方向:
  - 方向A: 扩展既有 `.sog4d` 主链路,正式支持单帧
  - 方向B: 先做单帧包装入口
- 执行了 OpenSpec 新建流程:
  - `openspec new change "sog4d-single-frame-ply-support"`
  - `openspec status --change "sog4d-single-frame-ply-support"`
  - `openspec instructions proposal --change "sog4d-single-frame-ply-support"`

### 结果
- 成功创建:
  - `openspec/changes/sog4d-single-frame-ply-support/`
- 使用 schema:
  - `spec-driven`
- 当前 artifact 状态:
  - `proposal` ready
  - `design` blocked by `proposal`
  - `specs` blocked by `proposal`
  - `tasks` blocked by `design, specs`
- 已拿到 `proposal.md` 模板与撰写要求,本轮按 skill 要求停在这里

### 总结感悟
- 这次需求最关键的 framing,是“补齐单帧入口的产品承诺”,而不是新增一个平行格式.
- 下一轮只要继续起草 proposal,就应该优先把 Why 和 Capabilities 写准,实现细节留到 design 再展开。

## 2026-03-11 17:07:10 +0800 任务名称: 为 `sog4d-single-frame-ply-support` 创建 proposal artifact

### 任务内容
- 为 OpenSpec change `sog4d-single-frame-ply-support` 创建第一个 artifact:
  - `proposal.md`
- 把单帧 `.ply -> .sog4d -> Unity` 需求落成 capability 级别的提案边界.

### 完成过程
- 读取了:
  - `openspec/specs/sog4d-sequence-encoding/spec.md`
  - `openspec/specs/4dgs-keyframe-motion/spec.md`
  - `openspec/specs/4dgs-playback-api/spec.md`
  - 归档的 `openspec/changes/archive/2026-02-18-sog4d-sequence-format/proposal.md`
  - `Tools~/Sog4D/README.md`
- 额外确认了两个关键事实:
  - 现有 `sog4d-sequence-encoding` 已经明确 `frameCount == 1` 时 `t_0 = 0.0`
  - 现有 `ply_sequence_to_sog4d.py` 没有看到显式拒绝单帧输入的判断
- 基于这些证据,把 proposal 的 capability 边界定为:
  - 新增 `sog4d-ply-conversion`
  - 修改 `sog4d-unity-importer`
  - 修改 `4dgs-keyframe-motion`
- 创建了:
  - `openspec/changes/sog4d-single-frame-ply-support/proposal.md`
- 刷新状态:
  - `openspec status --change "sog4d-single-frame-ply-support"`

### 结果
- OpenSpec 当前状态:
  - `Progress: 1/4 artifacts complete`
  - `proposal` 完成
  - `design` ready
  - `specs` ready
  - `tasks` 仍 blocked by `design, specs`
- proposal 核心口径已经稳定:
  - 这次不是新格式
  - 而是把单帧 `.ply` 正式纳入既有 `.sog4d` 工作流

### 总结感悟
- 如果 proposal 先把 capability 选错,后面的 `specs` 和 `design` 会一起漂.
- 这轮先把“容器层不用改,时间映射基础语义也已有”这件事钉住,后续实现范围就会更聚焦。

## 2026-03-11 17:18:40 +0800 任务名称: 为 `sog4d-single-frame-ply-support` 创建 design artifact

### 任务内容
- 为 OpenSpec change `sog4d-single-frame-ply-support` 创建 `design.md`
- 把“单帧支持到底是补入口,还是要动核心架构”这件事做出技术取舍

### 完成过程
- 读取了:
  - `openspec instructions design --change "sog4d-single-frame-ply-support" --json`
  - `openspec/changes/sog4d-single-frame-ply-support/proposal.md`
  - 归档的 `openspec/changes/archive/2026-02-18-sog4d-sequence-format/design.md`
  - `Runtime/GsplatSequenceAsset.cs`
  - `Editor/GsplatSog4DImporter.cs`
  - `Runtime/GsplatSequenceRenderer.cs`
  - `Tools~/Sog4D/ply_sequence_to_sog4d.py`
  - `README.md`
- 结构化确认了关键事实:
  - `EvaluateFromTimeNormalized(...)` 已经有 `frameCount == 1` 的安全分支
  - importer / runtime 静态校验都只要求 `frameCount > 0`
  - 真正模糊的是 CLI 入口与文档口径
- 创建了:
  - `openspec/changes/sog4d-single-frame-ply-support/design.md`

### 结果
- OpenSpec 状态刷新后:
  - `Progress: 2/4 artifacts complete`
  - `proposal` 完成
  - `design` 完成
  - `specs` ready
  - `tasks` blocked by `specs`
- design 核心结论:
  - 单帧继续复用现有 `.sog4d` 主链路
  - 工具层扩展现有脚本,优先考虑 `--input-ply`
  - Unity 侧继续使用 `GsplatSequenceAsset` / `GsplatSequenceRenderer`
  - 本次以“硬化实现 + 补测试 + 补文档”为主,不重写 decode 架构

### 总结感悟
- 当代码里已经有单帧兼容基础时,最有价值的 design 往往不是“发明新结构”,而是把边界、承诺和验证路径说清楚。
- 这轮 design 把 change 半径压在真正缺失的地方,后面的 specs 和实现就不容易跑偏。

## 2026-03-11 17:29:30 +0800 任务名称: 使用 `openspec-ff-change` 补齐 `sog4d-single-frame-ply-support` 全部 artifact

### 任务内容
- 用 fast-forward 方式把 `sog4d-single-frame-ply-support` 从已完成 `proposal/design` 推进到 apply-ready
- 一次性创建剩余的 `specs` 与 `tasks`

### 完成过程
- 读取了:
  - `openspec instructions specs --change "sog4d-single-frame-ply-support" --json`
  - `openspec instructions tasks --change "sog4d-single-frame-ply-support" --json`
  - `openspec/changes/sog4d-single-frame-ply-support/proposal.md`
  - `openspec/changes/sog4d-single-frame-ply-support/design.md`
  - `openspec/specs/sog4d-unity-importer/spec.md`
  - `openspec/specs/4dgs-keyframe-motion/spec.md`
  - 归档 `openspec/changes/archive/2026-02-18-sog4d-sequence-format/tasks.md`
- 创建了 3 份 spec:
  - `specs/sog4d-ply-conversion/spec.md`
  - `specs/sog4d-unity-importer/spec.md`
  - `specs/4dgs-keyframe-motion/spec.md`
- 创建了实现清单:
  - `openspec/changes/sog4d-single-frame-ply-support/tasks.md`
- specs 的核心落点:
  - 工具侧正式支持“单帧文件 / 序列目录”两类输入
  - importer 明确 `frameCount = 1` 仍是正常 `.sog4d` sequence path
  - keyframe motion 明确单帧时 `i0=i1=0, a=0`
- 最后执行 `openspec status --change "sog4d-single-frame-ply-support"` 刷新状态

### 结果
- OpenSpec 状态已变为:
  - `Progress: 4/4 artifacts complete`
  - `All artifacts complete!`
- 当前 change 已经 ready for implementation

### 总结感悟
- fast-forward 的关键不是“快写完”,而是把 proposal / design / specs / tasks 之间的契约连续性保持住。
- 这轮已经把“单帧支持不是新格式,而是现有 `.sog4d` 路线的正式补完”这件事从提案一路贯穿到了任务层。

## 2026-03-11 18:58:00 +0800 任务名称: 补强 `sog4d-single-frame-ply-support` 的真实样例验收规格

### 任务内容
- 更新 `sog4d-single-frame-ply-support` change 的 proposal / design / specs / tasks。
- 把真实样例 `Assets/Gsplat/ply/s1-point_cloud.ply` 从“会话约定”升级为 OpenSpec 正式验收夹具。

### 完成过程
- 在 proposal 中补充:
  - 这份真实样例必须完成正式 `.ply -> .sog4d`
  - 必须导入当前 Unity 工程
  - 必须完成实际显示验证
- 在 design 中补充:
  - 真实样例验收属于 change 的强制验证策略
  - 不能只靠最小伪造样例证明“能力存在”
- 在 `sog4d-ply-conversion` spec 中新增真实样例转换 requirement 与 scenario。
- 在 `sog4d-unity-importer` spec 中新增真实样例 Unity 导入与显示验证 requirement 与 scenario。
- 在 tasks 中新增显式 checklist:
  - 真实样例转换证据
  - 真实样例 Unity 显示验证

### 总结感悟
- 这次补强后,change 的验收边界更清楚了。
- 后续继续实现时,不会再出现“通用能力看起来支持,但真实样例是否必须过”这种口径模糊的问题。

## 2026-03-11 19:03:00 +0800 任务名称: 澄清 `.ply -> .sog4d` 与 Unity 验收的职责边界

### 任务内容
- 根据用户澄清,修正 `sog4d-single-frame-ply-support` 的规格措辞。
- 明确 `.ply -> .sog4d` 属于脚本工具职责,不是 Unity 内部转换职责。

### 完成过程
- 在 proposal / design 中补充职责边界:
  - `.ply -> .sog4d` 由离线脚本工具完成
  - Unity 只负责导入脚本工具产出的 `.sog4d` 并做显示验证
- 在 `sog4d-ply-conversion` spec 中把真实样例转换场景改写为“通过脚本工具执行”.
- 在 `sog4d-unity-importer` spec 中补充前置条件:
  - 转换已由脚本工具完成
  - Unity 只负责导入与显示验证
- 在 tasks 中同步调整 wording,避免再让人误解为“Unity 内也要承担 `.ply -> .sog4d` 转换”.

### 总结感悟
- 这类链路型需求最容易把“转换职责”和“导入职责”写混。
- 这次纠偏后,OpenSpec 对工具链与 Unity 的分工已经足够明确,后续实现和验收不会再跑偏。

## 2026-03-11 21:45:00 +0800 任务名称: `sog4d-single-frame-ply-support` 真实样例实现与显示验收闭环

### 任务内容
- 扩展 `.sog4d` 工具链,正式支持“单个 `.ply` 文件 -> 单帧 `.sog4d`”.
- 使用真实样例 `Assets/Gsplat/ply/s1-point_cloud.ply` 做最终转换与 Unity 显示验收.
- 补齐 importer/runtime/test/docs/OpenSpec 记录,并确认这条链路不是“只导入不显示”.

### 完成过程
- 工具链:
  - `Tools~/Sog4D/ply_sequence_to_sog4d.py` 新增 `--input-ply`.
  - 与 `--input-dir` 做互斥校验.
  - 单帧入口继续复用既有 `ply_files` 主流程,不新造旁路格式.
- Unity 运行链路:
  - `Editor/GsplatSog4DImporter.cs` 明确单帧导入仍生成 `GsplatSequenceAsset`.
  - `Runtime/GsplatSog4DRuntimeBundle.cs` 与 `Runtime/GsplatSequenceRenderer.cs` 明确单帧 bundle / 固定帧退化语义.
- 自动化验证:
  - `python3 -m unittest Tools~/Sog4D/tests/test_single_frame_cli.py`
  - Unity 定向 EditMode tests:
    - `Gsplat.Tests.GsplatSog4DImporterTests`
    - `Gsplat.Tests.GsplatSequenceAssetTests`
- 真实样例转换:
  - 输入:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/st-dongfeng-worldmodel/st-dongfeng-worldmodel/Assets/Gsplat/ply/s1-point_cloud.ply`
  - 输出:
    - `/tmp/s1_from_file.sog4d`
  - 已执行 `pack --input-ply ... --self-check` 与 `validate`,结果通过.
- Unity 显示验收:
  - 真实导入资产:
    - `Assets/Gsplat/sog4d/s1-point_cloud_single.sog4d`
  - 已确认:
    - `GsplatSequenceRenderer.Valid = true`
    - `SplatCount = 169133`
  - `manage_camera screenshot` 会给出空图假阴性.
  - 最终通过 Unity 主窗口 on-screen GameView 截图确认 `Sog4DSingleFrameVerify` 白色点云已经实际显示.

### 总结感悟
- 这次单帧支持本质上是补齐现有 `.sog4d` 正式入口与正式语义,不是发明新格式.
- Unity 验收里,工具截图接口不一定等价于当前屏幕上的 GameView.
- 以后遇到“明明 Valid 了但截图是空的”,要先区分:
  - 这是显示真的失败
  - 还是取证手段本身给了假阴性

## 2026-03-12 14:21:10 +0800 任务名称: 回读文件上下文与归档,补充 AGENTS 工具使用规范

### 任务内容
- 回读当前主线六文件、多个支线后缀六文件,以及 `archive/` 中的历史上下文文件.
- 从这些上下文里提炼“工具使用错误 -> 正确用法”的长期规则.
- 把当前 `AGENTS.md` 还没覆盖的高价值规则补进去.

### 完成过程
- 范围清点:
  - 检索了当前主线文件:
    - `task_plan.md`
    - `notes.md`
    - `WORKLOG.md`
    - `LATER_PLANS.md`
    - `ERRORFIX.md`
  - 检索了当前支线:
    - `__imgui_layout_error`
    - `__sog4d_display_issue`
    - `__splat4d_edge_opacity`
    - `__splat4d_single_frame_support`
  - 检索了 `archive/` 中的历史 `task_plan/notes/WORKLOG/ERRORFIX`.
- 候选收敛:
  - 先排除了 `AGENTS.md` 已经写过的规则:
    - `-runTests` / `-quit`
    - `Samples~/` copy 不自动更新
    - `AssetDatabase` 路径取证
    - Metal `GetDimensions`
    - Metal buffer 绑定与 `GetKernelThreadGroupSizes`
  - 再把仍缺失的规则收敛成 5 条:
    - 最小 package 测试工程需要 `testables`
    - `-batchmode -nographics` 的 Null graphics device 语义
    - `manage_camera screenshot` 的 EditMode 假阴性
    - 追加 Markdown 要用单引号 heredoc
    - repo-local skill name 长度上限 64
- 文档落地:
  - `AGENTS.md`:
    - 在 `Testing Guidelines` 补了 3 条验证/CLI 相关规范
    - 新增 `Tooling Notes` 小节,补了 2 条 shell/Codex skill 规则
  - `notes.md`:
    - 记录了本次检索范围、候选筛选过程与最终结论
  - `task_plan.md`:
    - 勾选了当前任务的阶段进展并同步当前状态

### 总结感悟
- 上下文文件里最有价值的长期知识,往往不是“怎么修这次 bug”,而是“哪种工具姿势会稳定给出假证据”.
- 这次补进 `AGENTS.md` 的 5 条规则,本质上都属于“减少误判成本”:
  - 少走错误命令
  - 少把 headless 噪声当根因
  - 少把截图假阴性当显示失败

## 2026-03-12 15:02:00 +0800 任务名称: 提交当前单帧 PLY / `.splat4d` 相关改动

### 任务内容
- 整理当前工作区的源码、文档、OpenSpec 与上下文改动.
- 在提交前做最小必要验证,避免只凭静态阅读就直接 commit.
- 识别并排除明显不是正式产物的未跟踪临时文件.

### 完成过程
- 检查了 `git status`、`git diff --stat`、`git submodule status --recursive`.
- 运行了两组 Python 自动化验证:
  - `python3 -m unittest Tools~/Splat4D/tests/test_single_frame_cli.py`
  - `python3 -m unittest Tools~/Sog4D/tests/test_single_frame_cli.py`
- 识别出 `=`、`A_gauss`、`ClipCorner`、`alphaGauss`、`baseAlpha` 及其 `.meta` 属于可疑空文件,本次不纳入提交.

### 总结感悟
- 这类“帮用户直接提交”的动作,关键不是机械执行 `git commit`,而是先把提交边界和验证证据讲清楚.
- 对未跟踪文件,宁可先排除可疑空产物,也不要为了追求工作区更干净而误把垃圾文件带进历史.
