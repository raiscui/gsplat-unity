## [2026-03-18 14:18:00] [Session ID: a5122445-83f8-4367-a55f-188f1411a83d] 笔记: RadarScan 动作层与粒子层拆分

## 现象

- 用户希望"关掉雷达扫描动作效果,只呈现雷达粒子"。
- 代码里既存在 RadarScan 开关淡入淡出(`LidarShowDuration` / `LidarHideDuration`),也存在持续扫描头运动(`LidarRotationHz` / `LidarTrailGamma`)。

## 当前假设

- 用户说的"扫描动作效果"更接近持续扫描头运动,不是 RadarScan 的开关 show/hide 过渡。
- 如果直接关 `EnableLidarScan`,会把粒子本身一起关掉,不符合目标。
- 更合理的切口是给 LiDAR shader 增加一个"是否启用扫描头运动"参数,在关闭时把 `trail01` 固定为 `1`,从而保留点云本体。

## 静态证据

- `Runtime/Shaders/GsplatLidarPassCore.hlsl` 中:
  - `headAz = frac(_LidarTime * _LidarRotationHz) * 2pi - pi`
  - `trail01 = pow(max(t, 1e-6), _LidarTrailGamma)`
  - 最终 `intensity = lerp(unscannedIntensity, scanIntensity, trail)`
- `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs` 中:
  - `RenderPointCloud(...)` 会把 `LidarRotationHz`、`LidarTrailGamma`、`LidarKeepUnscannedPoints` 等参数一路送进 LiDAR shader
  - `EnableLidarScan` 负责的是 LiDAR runtime 是否开启,不是单独的扫描头动画层

## 备选解释

- 备选解释A: 用户其实想关的是 RadarScan 的 show/hide 过渡噪声/辉光,而不是持续扫描头运动。
- 备选解释B: 用户想要的是"保留粒子,但不旋转扫描头,同时也不保留 trail 衰减"。这与"只把 RotationHz 设为 0"不同,因为设为 0 仍可能保留一条固定亮带。

## 验证计划

- 在运行时新增 `LidarEnableScanMotion` 开关,默认 `true`
- C# -> LiDAR 渲染桥接 -> shader hidden property 全链路打通
- shader 中关闭该开关时跳过 `headAz/trail01` 扫描逻辑,直接渲染稳定点云
- 用编辑器单测锁定:
  - 新字段默认值
  - shader property 契约
  - pass core 确实包含 scan-motion gate

## 当前结论

- 这是一个"高置信静态结论 + 待测试验证"的方案,还不能直接写成已完成结论。
- 下一步进入正式实现,并用测试与编译输出补足动态证据。

## [2026-03-18 14:45:00] [Session ID: a5122445-83f8-4367-a55f-188f1411a83d] 笔记: 动态验证结果

## 已验证结论

- `LidarEnableScanMotion` 已经贯通到以下链路:
  - `GsplatRenderer` / `GsplatSequenceRenderer`
  - `GsplatLidarScan.RenderPointCloud(...)`
  - `GsplatLidar.shader` / `GsplatLidarAlphaToCoverage.shader`
  - `GsplatLidarPassCore.hlsl`
  - 对应 Inspector 与 README / CHANGELOG
- shader 侧关闭 `LidarEnableScanMotion` 后,会直接让 `trail01 = 1.0`,从而保留稳定的 LiDAR 粒子显示,不再依赖扫描头旋转和 trail 衰减。

## 动态证据

- 定向测试通过:
  - `Gsplat.Tests.GsplatLidarShaderPropertyTests.LidarShader_ContainsShowHideOverlayProperties`
  - `Gsplat.Tests.GsplatLidarShaderPropertyTests.LidarShader_UsesAnalyticCoverageAndExternalHitCompetition`
  - `Gsplat.Tests.GsplatLidarShaderPropertyTests.LidarAlphaToCoverageShader_DeclaresAlphaToMaskOn`
  - `Gsplat.Tests.GsplatLidarScanTests.NewGsplatRenderer_DefaultsLidarEnableScanMotionToTrue`
  - `Gsplat.Tests.GsplatLidarScanTests.NewGsplatSequenceRenderer_DefaultsLidarEnableScanMotionToTrue`
- 整包 `Gsplat.Tests.Editor` 也实际运行过,说明本次代码至少完成了 Unity 编译进入测试阶段。

## 与本次改动无关的现存失败

- `Gsplat.Tests.GsplatSplat4DImporterDeltaV1Tests.ImportV1_StaticSingleFrame4D_RealFixturePlyThroughExporterAndImporter`
  - 失败原因: `python3` 环境缺少 `numpy`
- 多条已有 `GsplatVisibilityAnimationTests.*`
  - 失败表现: 期望中间动画值 / `deltaTime > 0` 等历史问题
- 这些失败在本次定向测试之外,当前没有证据表明是 `LidarEnableScanMotion` 引入的回退
