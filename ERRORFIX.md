# ERRORFIX: RadarScan show/hide noise 不可见

## 2026-03-02

### 现象

- 用户反馈 RadarScan(LiDAR) 模式下,Show/Hide 期间“完全看不到 noise/变化”。

### 初步判断

- 由于已经多轮“增强 shader 噪声”,但用户仍完全无变化。
- 必须先排除: Unity 根本没跑到我们改的 shader 或参数链路有断点。

### 证据采集(已做)

- 在 `Runtime/Lidar/GsplatLidarScan.cs` 添加 Editor 下节流诊断日志:
  - `[Gsplat][LiDAR][ShowHideDiag]`
  - 打印 settings/shader 的 AssetDatabase path,show/hide 参数,noise 参数,以及材质是否存在 `_LidarShowHideNoise*` 属性。

### 下一步

- 依据诊断日志定位断点:
  - 若 shaderPath 不指向 `Packages/wu.yize.gsplat/Runtime/Shaders/GsplatLidar.shader`,优先修正引用/包来源。
  - 若 noise 参数为 0,回溯 RenderPointCloud 调用参数来源。
  - 若两者都正确但仍不可见,再调整 shader 观感(只改根因点)。

## 2026-03-03

### 现象

- LiDAR(RadarScan) 扫描头扫过后的区域会变黑.
- 下一次扫描前,老点会提前变暗或消失.

### 根因

- `Runtime/Shaders/GsplatLidar.shader` 旧逻辑:
  - `brightness = LidarIntensity * trail`.
  - alpha 不随 trail 变化.
- 在 alpha blend + ZWrite On 场景下:
  - 当 brightness 很小但仍未 discard 时,点会表现为"黑点/黑片".
  - trail 会在 1 圈内衰减到接近 0,因此老点在下一次扫到前会提前变暗或消失.

### 修复

- 引入"未扫到区域底色强度"(亮度下限)与 Keep 开关:
  - 新增字段:
    - `LidarKeepUnscannedPoints`(是否在下一次扫描前保留未扫到区域)
    - `LidarUnscannedIntensity`(未扫到区域底色强度)
  - Shader 新增 `_LidarUnscannedIntensity`,并把每点强度改为:
    - `lerp(unscannedIntensity, scanIntensity, trail)`
  - Runtime 下发 `_LidarUnscannedIntensity`:
    - Keep 关闭时强制下发 0,保持旧行为.
    - Keep 开启时下发 `LidarUnscannedIntensity`,避免"扫过后变黑".

### 验证(证据)

- Unity 6000.3.8f1, EditMode tests(`Gsplat.Tests`): total=54, passed=52, failed=0, skipped=2
- XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_unscanned_intensity_2026-03-03_154114_noquit.xml`

## 2026-03-08

### 现象

- `radarscan-particle-antialiasing-modes` 首轮 Unity EditMode 回归里,两个 A2C fallback 测试失败.
- 失败不是断言错,而是 Unity 记录了 error log:
  - `Releasing render texture that is set as Camera.targetTexture!`

### 根因

- `Tests/Editor/GsplatLidarScanTests.cs` 里的两个测试在 `finally` 中直接销毁了 `targetTexture`.
- 但此时 `Camera.targetTexture` 仍然指向该 RT.
- Unity 会把这种销毁顺序记成错误日志,从而导致整组测试失败.

### 修复

- 把测试清理顺序改为:
  - 先 `camera.targetTexture = null`
  - 再 `DestroyImmediate(targetTexture)`
  - 最后 `DestroyImmediate(cameraGo)`
- 同时把 `camera` 提升到 `try/finally` 外层变量,确保 `finally` 能安全解绑.

### 验证(证据)

- 目标测试:
  - `Gsplat.Tests.GsplatLidarScanTests.ResolveEffectiveLidarParticleAntialiasingMode_FallsBackToAnalyticCoverageWithoutMsaa`
  - `Gsplat.Tests.GsplatLidarScanTests.ResolveEffectiveLidarParticleAntialiasingMode_KeepsA2CWhenMsaaIsAvailable`
  - 在 XML 中均为 `Passed`
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`82`, passed=`80`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_particle_aa_2026-03-08_noquit.xml`

## 2026-03-08

### 现象

- 用户实测: `AnalyticCoverage` / `AlphaToCoverage` 与 `LegacySoftEdge` 几乎看不出区别.

### 根因

- `AnalyticCoverage` 原实现直接在归一化 uv 上做 `fwidth`,对 `2px` 小点的边缘差异偏弱.
- `AlphaToCoverage` 原 A2C shell 只是把 `AlphaToMask On` 叠在普通透明混合 pass 上.
- 这不符合 coverage-first 的使用语义,因此很难产生用户期待的可见收益.

### 修复

- `AnalyticCoverage` 改为像素尺度 coverage 计算.
- `AlphaToCoverage` shell 改为 coverage-first:
  - `RenderType="TransparentCutout"`
  - `Blend One Zero`
  - `AlphaToMask On`
  - `ZWrite On`

### 验证(证据)

- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`82`, passed=`80`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_particle_aa_v2_2026-03-08_noquit.xml`

## 2026-03-08

### 现象

- 即使完成了“像素尺度 AnalyticCoverage + coverage-first A2C shell”,用户仍判断视觉差异不明显.

### 更深一层根因

- 旧逻辑虽然已经改进了 coverage 公式,但点片几何仍卡在原始 footprint 内.
- 这会导致 AA 只能在原边界内部做 alpha 软化,不能在外侧长出真正的 fringe.
- 对 `LidarPointRadiusPixels=2` 这类小点,这种“只往里软”的改善往往肉眼不明显.

### 修复

- `Runtime/Shaders/GsplatLidarPassCore.hlsl`
  - 对所有非 `LegacySoftEdge` 路线增加 `aaFringePadPx = 1.0`.
  - 顶点阶段用 `paddedRadiusPx` 扩大点片几何.
  - fragment 阶段允许外扩 fringe 区域存在,并用 `outerLimit` 限定最大外扩范围.
- `Runtime/Shaders/GsplatLidarAlphaToCoverage.shader`
  - 保持 coverage-first 语义,并显式定义 `GSPLAT_LIDAR_A2C_PASS 1`,让共享 pass core 进入 A2C 专用 coverage 路线.

### 验证(证据)

- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`82`, passed=`80`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_particle_aa_v3_2026-03-08_noquit.xml`

## 2026-03-08

### 现象

- 用户反馈 external mesh 的 RadarScan 粒子“现在都在 mesh 背面了,之前是正面的”.

### 根因

- 问题不在刚刚的 AA fringe / A2C 调整.
- 真正高风险点是 `Runtime/Shaders/GsplatLidarExternalCapture.shader` 使用了 `Cull Back`.
- 该 shader 在 frustum external GPU capture 里是通过手动 `SetViewProjectionMatrices(...)` 驱动的离屏 capture.
- 在 RT flip、手性变化、负缩放、镜像 transform 等场景里,front/back 判定容易漂移.
- 一旦漂移,`Cull Back` 会稳定留下错误一侧,结果 external hit 来自 mesh 背面.

### 修复

- 把 `Runtime/Shaders/GsplatLidarExternalCapture.shader` 改成 `Cull Off`.
- 让 depth buffer 自然选择最近可见表面,不再依赖 winding/cull 去决定“正面”.
- 新增 EditMode 测试锁定该 hidden shader 的 `Cull Off` 约束.

### 验证(证据)

- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`83`, passed=`81`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_capture_culloff_v2_2026-03-08.xml`

## 2026-03-08

### 现象

- 用户进一步澄清: 不是"命中了 mesh 背面",而是"粒子显示在模型背后".
- `Cull Off` 已经落地后,问题仍存在.

### 根因

- capture 端的确可能影响 front/back 选择,但这次剩余问题不在 capture 端.
- LiDAR draw 端此前对 external hit 直接使用真实 `range` 重建 `worldPos`.
- 当粒子点位与可见 mesh 表面过贴,或者略微落在表面之后,普通 mesh 的深度就会把粒子挡住.
- 所以这是"最终显示深度关系"问题,不是"first return 竞争"问题.

### 修复

- 新增并接通 `LidarExternalHitBiasMeters`(默认 `0.01f`):
  - C# -> MPB -> shader 全链路打通.
  - shader 只在 `useExternalHit` 路径上执行:
    - `renderRange = max(range - bias, 0)`
    - `worldPos` 用 `renderRange` 重建
- 关键约束:
  - 不改 external / splat 的最近命中竞争
  - 不改 `Depth` 色带计算使用的真实 `range`
  - 不改距离衰减使用的真实 `range`

### 验证(证据)

- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`, EditMode `Gsplat.Tests`
  - total=`83`, passed=`81`, failed=`0`, skipped=`2`
  - XML: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_external_hit_bias_2026-03-08.xml`
  - log: `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/unity_tests_lidar_external_hit_bias_2026-03-08.log`

### 补充经验

- Unity 6000.3.8f1 这版命令行跑 TestRunner 时,如果又遇到"只刷新工程就退出,不生成 XML",优先去掉 `-quit` 再重跑.

## 2026-03-08

### 现象

- 用户继续反馈:
  - external mesh 的 RadarScan 粒子不是"略微埋进表面".
  - 而是整体跑到了"远离雷达 loc cam"的那一面.
  - `LidarExternalHitBiasMeters` 怎么调都几乎没效果.

### 根因

- 这证明问题不在 render-only bias.
- 真正更深的根因是:
  - external GPU capture 上一轮改成了 `encoded-depth + BlendOp Max`.
  - 该方案隐含依赖浮点 RT blending 语义稳定.
  - 一旦这条路线上某个平台/驱动退化成"最后写入者赢",闭合 mesh 会更容易把 far side/back side 留到 capture 结果里.
- 所以:
  - `Cull Off` 虽然解决了 front/back winding 判反的问题,
  - 但它并不能保证"当前像素最近表面一定选对".

### 修复

- 保留 `Cull Off`.
- 把"最近表面竞争"重新交给硬件 depth buffer:
  - `Runtime/Shaders/GsplatLidarExternalCapture.shader`
    - `DepthCapture`: `ZTest LEqual + ZWrite On + Blend One Zero`
    - 颜色 RT 直接写 `linearDepth`
    - `SurfaceColorCapture`: `ZTest Equal + ZWrite Off + Blend One Zero`
  - `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`
    - color pass 前只清 color,不清 depth:
      - `ClearRenderTarget(false, true, Color.clear)`
  - `Runtime/Shaders/Gsplat.compute`
    - resolve 改回直接读取 `linearDepth`,不再做 `rcp(encodedDepth)`

### 为什么这次更正确

- `Cull Off` 负责"不要把正反面判错".
- depth buffer 负责"当前像素最近表面是谁".
- 这两个问题拆开后,语义更清晰.
- 也避免了把正确性押在 `BlendOp Max` 对浮点 RT 的平台实现上.

### 验证(证据)

- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`
- 定向 `Gsplat.Tests.GsplatLidarExternalGpuCaptureTests`
  - total=`8`, passed=`8`, failed=`0`, skipped=`0`
  - XML:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_external_gpu_capture_frontface_2026-03-08.xml`
  - 全包 `Gsplat.Tests`
    - total=`85`, passed=`83`, failed=`0`, skipped=`2`
    - XML:
      - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_full_external_frontface_2026-03-08.xml`

## 2026-03-08

### 现象

- 即使 external capture 已经改回 `Cull Off + hardware depth nearest surface`, 用户现场仍反馈粒子还在球体背面.

### 关键证据

- 新增真实 GPU capture 功能测试:
  - `ExternalGpuCaptureDepthPass_CenterPixelMatchesSphereFrontDepth`
- 该测试直接渲染一个球体并读回中心像素深度.
- 修复前读回值是:
  - `5.5`
- 这正是球体后表面的深度.
- 正确前表面深度应为:
  - `4.5`

### 真正根因

- 问题不是 external capture 思路完全错.
- 问题是我们把 hardware depth 路线写成了固定的 forward-Z 语义:
  - `ZTest LEqual`
  - `clearDepth = 1`
- 在 reversed-Z 平台上(例如当前 Metal):
  - 这会让 closed mesh 稳定保留 far side.

### 修复

- `Runtime/Shaders/GsplatLidarExternalCapture.shader`
  - depth pass 的 `ZTest` 改成材质属性:
    - `_LidarExternalDepthZTest`
- `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`
  - 按 `SystemInfo.usesReversedZBuffer` 设置:
    - forward-Z -> `CompareFunction.LessEqual`, clearDepth=`1`
    - reversed-Z -> `CompareFunction.GreaterEqual`, clearDepth=`0`
- color pass 继续保持:
  - 同一 depth/stencil
  - `ZTest Equal`

### 验证(证据)

- 功能测试修复后通过:
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

### 经验沉淀

- 对需要自己驱动 depth buffer 的离屏 pass:
  - 不能把 `ZTest` 和 clearDepth 写死成 forward-Z 常量.
- 特别是在 Unity/Metal 这种 reversed-Z 平台上:
  - 如果出现"闭合 mesh 稳定抓到 far side",要第一时间怀疑:
    - depth compare function
    - clearDepth
  - 而不是只盯着 ray reconstruction 公式.

## 2026-03-08

### 现象

- 在真正根因修复已经完成后,`LidarExternalHitBiasMeters` 仍保持默认 `0.01`.

### 为什么这不理想

- 它会让一个“render-only 微调项”看起来像“默认主修复”.
- 但这次真正解决背面问题的,其实是 reversed-Z capture 修复.
- 因此继续维持非零默认值,会让后续排障口径变混乱.

### 调整

- 保留 `LidarExternalHitBiasMeters` 能力.
- 但把默认值与 fallback 统一改回 `0`.

### 经验沉淀

- 当某个字段只是辅助性补偿,而不是根因修复时:
  - 更稳妥的默认值通常应该是关闭
  - 让它按需启用,而不是默认偷偷参与行为

## 2026-03-09 17:37:45 +0800

### 问题

- HDRP 场景里即使已经配置好 MSAA,RadarScan 粒子 `LidarParticleAntialiasingMode=AlphaToCoverage` 仍被回退到 `AnalyticCoverage`.
- 诊断日志表现为:
  - `camera=Main Camera requested=AlphaToCoverage effective=AnalyticCoverage`
  - `allowMSAA=0 msaaSamples=1`

### 原因

- 我们把 `Camera.allowMSAA` 当成 LiDAR A2C 是否可用的硬门槛.
- 但 HDRP 自己会在 `HDAdditionalCameraData.OnEnable()` 中把 `Camera.allowMSAA` 强制设成 `false`.
- 因此旧逻辑在 HDRP 下会把“使用 HD Frame Settings 的合法 MSAA”误判成“没有 MSAA”.

### 修复

- `Runtime/GsplatUtils.cs`
  - 新增统一 LiDAR MSAA helper.
  - HDRP 分支通过反射调用内部 `FrameSettings.AggregateFrameSettings(...)`,读取聚合后的:
    - `FrameSettingsField.MSAA`
    - `GetResolvedMSAAMode(...)`
  - 若 camera 输出到 `RenderTexture`,再与 `targetTexture.antiAliasing` 取最小值.
- `Runtime/Lidar/GsplatLidarScan.cs`
  - 诊断日志改为输出:
    - `cameraAllowMSAA`
    - `msaaSamples`
    - `msaaSource`
  - 不再继续输出会误导排障的“只看 Camera.allowMSAA”口径.
- `Tests/Editor/Gsplat.Tests.Editor.asmdef`
  - 增加 HDRP version define.
- `Tests/Editor/GsplatLidarScanTests.cs`
  - 新增 HDRP 条件测试,锁定这条兼容语义.

### 验证

- 定向 EditMode `Gsplat.Tests.GsplatLidarScanTests`
  - total=`33`, passed=`33`, failed=`0`, skipped=`0`
  - XML:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_hdrp_a2c_fix_2026-03-09.xml`
- 全包 EditMode `Gsplat.Tests`
  - total=`86`, passed=`83`, failed=`0`, skipped=`3`
  - XML:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_full_lidar_hdrp_a2c_fix_2026-03-09_r2.xml`

### 以后避免再犯

- HDRP 下凡是和 A2C / MSAA 有关的功能:
  - 不能把 `Camera.allowMSAA` 当成真实运行时状态
  - 必须优先看 resolved HD Frame Settings

## 2026-03-09 22:18:00 +0800

### 问题

- 真实项目编译报错:
  - `Packages/wu.yize.gsplat/Tests/Editor/GsplatLidarScanTests.cs(11,29): error CS0234`
- 报错内容:
  - `UnityEngine.Rendering.HighDefinition` 在测试程序集里不可见

### 原因

- 我之前给 `Tests/Editor/GsplatLidarScanTests.cs` 直接加了:
  - `using UnityEngine.Rendering.HighDefinition;`
- 同时给 `Tests/Editor/Gsplat.Tests.Editor.asmdef` 加了 HDRP `versionDefines`
- 但这只解决了“源码片段开关”,没有解决“测试程序集没有 HDRP 引用”这个更本质的问题
- 因此测试程序集在真实项目里仍会直接编译失败

### 修复

- 删除测试文件里的 HDRP 直接 `using`
- 删除测试 asmdef 里新增的 HDRP `versionDefines`
- 把 HDRP 专项测试改成运行时反射:
  - `FindLoadedType(...)`
  - 反射设置 `customRenderingSettings`
  - 反射写入 `m_RenderingPathCustomFrameSettings`
  - 反射配置 `renderingPathCustomFrameSettingsOverrideMask.mask`
  - 没装 HDRP 时直接 `Assert.Ignore(...)`

### 验证

- `dotnet build Gsplat.Tests.Editor.csproj -nologo`
  - `0 errors`
  - `4 warnings`
- Unity 6000.3.8f1, `_tmp_gsplat_pkgtests`
  - 反射版 HDRP 测试被 TestRunner 正常执行
  - 因测试工程未安装 HDRP,按预期 `Skipped`
  - XML:
    - `/Users/cuiluming/local_doc/l_dev/my/unity/_tmp_gsplat_pkgtests/Logs/TestResults_lidar_hdrp_reflection_fix_2026-03-09.xml`

### 以后避免再犯

- Unity 可选包相关测试:
  - 优先用反射做运行时探测
  - 不要让测试程序集直接 `using` 可选包命名空间
  - `versionDefines` 不是程序集引用替代品
