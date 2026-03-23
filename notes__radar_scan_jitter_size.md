## [2026-03-23 16:37:02 +0800] [Session ID: 20260323_6] 笔记: RadarScan 抖动波纹与小粒径异常的首轮静态证据

## 现象

- 用户反馈 1: RadarScan 粒子增加位置抖动后,当密度提高时会出现明显波纹。
- 用户反馈 2: 当粒子大小小于 `1` 时,显示不正常。

## 当前主假设

- 主假设A: 当前 show/hide 的屏幕空间 `warpPx` 完全按固定像素位移工作,没有和粒子半径或采样间距建立比例关系。密度一高,规则点阵会被同相位的平滑噪声场推成有组织的波纹。
- 主假设B: fragment 中把 `pointRadiusPx` 强制钳到 `>= 1.0` 后,`0..1px` 的粒径区间会共享同一套覆盖率/边缘宽度计算,导致亚像素大小不再连续变化,从而出现“小于 1 显示不正常”。

## 最强备选解释

- 备选解释A: 波纹的主因不是 `warpPx` 过大,而是 `EvalLidarShowHideNoise01` 的低频 model-space 噪声与规则 LiDAR 栅格形成拍频。即使把位移幅度缩小,仍可能保留明暗波纹。
- 备选解释B: 小粒径异常并不来自 fragment clamp,而是顶点阶段的几何外扩(`paddedRadiusPx` / `uvScale`)与 fragment 的 `outerLimit` 使用了不一致的半径定义。

## 静态证据

### 来源1: `Runtime/Shaders/GsplatLidarPassCore.hlsl`

- `frag(...)` 中存在:
  - `float pointRadiusPx = max(_LidarPointRadiusPixels, 1.0);`
  - `float signedEdgePx = signedEdge * pointRadiusPx;`
  - `float analyticWidthPx = max(fwidth(signedEdgePx), 1.0e-4);`
- 这意味着 `_LidarPointRadiusPixels < 1` 时,fragment 覆盖率计算不会继续变小,而是被折叠到 `1px` 语义。
- `vert(...)` 中的屏幕空间位移抖动存在:
  - `warpPx = noiseStrengthVis * warpWeight * max(_LidarShowHideWarpPixels, 0.0) * warpStrengthMul;`
  - `proj.xy += warpOffset;`
- 该 `warpPx` 只受噪声强度、warp 强度和用户像素值控制,没有使用 `LidarPointRadiusPixels` 或 LiDAR 栅格 spacing。

### 来源2: `Runtime/Lidar/GsplatLidarScan.cs`

- `RenderPointCloud(...)` 下发点半径时仅做 `Mathf.Max(pointRadiusPixels, 0.0f)`。
- 也就是说 C# 侧允许 `0..1` 的合法小粒径进入 shader,问题不是在运行时直接被 C# clamp 到 `1`。

### 来源3: `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`

- 默认参数:
  - `LidarPointRadiusPixels = 2.0f`
  - `NoiseStrength = 0.6f`
  - `WarpStrength = 2.0f`
  - `LidarShowHideWarpPixels = 6.0f`
- 按当前公式估算,在 `NoiseStrength=0.6`、`WarpStrength=2`、`warpWeight≈1` 时:
  - 实际 `warpPx ≈ sqrt(0.6) * 6 * 1 ≈ 4.65px`
  - 对 `radius=0.5px` 的点来说,位移约为点半径的 `9.3x`
  - 对 `radius=0.25px` 的点来说,位移约为点半径的 `18.6x`

### 来源4: `Tests/Editor/GsplatLidarShaderPropertyTests.cs`

- 现有测试显式锁定了字符串:
  - `float pointRadiusPx = max(_LidarPointRadiusPixels, 1.0);`
- 这说明如果我们修复小粒径异常,必须同步更新测试,否则旧测试会把错误行为继续当成正确契约。

## 最小验证

- 公式验证1:
  - 用独立脚本代入当前 fragment 公式后,`radius=0.25 / 0.5 / 0.75 / 1.0` 在 coverage 计算上得到完全相同的 `pointRadiusPx=1.0`。
  - 这支持主假设B。
- 公式验证2:
  - 用当前默认参数估算 `warpPx` 后,其量级明显大于小粒径本身。
  - 这支持主假设A。

## 还缺的动态证据

- 还没有针对真实 shader 数学写成自动化回归测试,因此当前仍是“高置信候选假设”,还不是最终根因结论。
- 下一步要补两类最小可证伪实验:
  - 实验1: 写纯数学单测,证明 `size < 1` 现在会被折叠成 `1px` 行为,修复后应恢复连续变化。
  - 实验2: 写纯数学单测或提取函数,证明 warp 应该受到“点半径 / AA fringe / 当前可见度”的上限约束,否则默认参数会远超粒径尺度。

## [2026-03-23 16:56:29 +0800] [Session ID: 20260323_7] 笔记: 用户澄清后对主假设的回滚与新证据

## 现象

- 用户已明确纠正: 当前说的不是 `warp` 形变,而是“点分布太均匀,密度提高后出现摩尔纹”。
- 同时,`size < 1` 的显示异常仍然是独立存在的问题,需要继续保留修复。

## 上一假设回滚

- 上一轮把“show/hide warp 过大”当成主因,这个口径不再成立。
- 新证据推翻点:
  - 用户直接否认了 `warp` 是他当前描述的问题。
  - 现有代码里 LiDAR 点位本身就是严格按 `(azBin, beamIndex)` 的 bin center 重建,即使没有 show/hide 动画,高密度时也天然会形成规则栅格。

## 当前主假设

- 主假设A: 摩尔纹的主因是“规则 bin center 栅格过于均匀”,不是 `warp`。当 `LidarAzimuthBins` / `LidarBeamCount` 提高后,规则角域采样与屏幕像素栅格发生拍频,于是出现有组织的波纹/摩尔纹。
- 主假设B: `size < 1` 的异常仍来自 fragment 覆盖率以前把半径折叠到 `>= 1px`。这个问题和摩尔纹不是同一个根因,但可以在同一轮一起修完。

## 最强备选解释

- 备选解释A: 摩尔纹不只是 bin center 太规则,还可能和当前每个 cell 的绘制顺序、方形点精灵边界、AA fringe 共同叠加。
- 备选解释B: 当前新加的 in-cell jitter 如果只在 shader 绘制阶段生效,而不影响 first return 竞争,可能依然会保留一部分规则纹理感。这属于“效果强弱”问题,不是是否该加 jitter 的根本方向问题。

## 新的静态证据

### 来源1: `Runtime/Shaders/GsplatLidarPassCore.hlsl`

- 点位方向仍然按 LUT 的中心值重建:
  - `float2 azSC = _LidarAzSinCos[azBin];`
  - `float2 beSC = _LidarBeamSinCos[beamIndex];`
  - `float3 dirLocal = float3(azSC.x * beSC.y, beSC.x, azSC.y * beSC.y);`
- 这说明当前每个 LiDAR cell 默认都精确落在规则角网格中心。
- 已落地的 subpixel 修复值得保留:
  - `float pointRadiusPxRaw = max(_LidarPointRadiusPixels, 0.0);`
  - `float pointRadiusPx = max(pointRadiusPxRaw, 1.0e-4);`

### 来源2: `Runtime/Lidar/GsplatLidarScan.cs`

- `RenderPointCloud(...)` 当前还没有接收 `pointJitterCellFraction` 参数。
- `MaterialPropertyBlock` 里也还没有 `_LidarPointJitterCellFraction` / `_LidarBeamMinRad` / `_LidarBeamMaxRad` 的下发。
- 这说明工作区现在是“上层字段和调用点先改了一半,底层参数链还没补齐”的状态。

### 来源3: `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`

- 新字段 `LidarPointJitterCellFraction = 0.35f` 已经加进序列化对象,并且两个调用点都已经把它传给 `m_lidarScan.RenderPointCloud(...)`。
- 两个 `ValidateLidarSerializedFields(...)` 路径也已经加了 `Mathf.Clamp01` 防御。
- 这说明本轮应该继续把整条链补完,而不是撤回到“完全不做 jitter”。

### 来源4: `Editor` / `Shader shell` / `Tests`

- `Editor/GsplatRendererEditor.cs` 已经把 `LidarPointJitterCellFraction` 排除出默认绘制,准备放进自定义 LiDAR 调参区。
- `Editor/GsplatSequenceRendererEditor.cs` 还没同步这个字段。
- `Runtime/Shaders/GsplatLidar.shader` 与 `Runtime/Shaders/GsplatLidarAlphaToCoverage.shader` 还没声明 `_LidarPointJitterCellFraction` 隐藏属性。
- `Tests/Editor/GsplatLidarShaderPropertyTests.cs` 里还有一条错误方向的测试,仍在锁“warp footprint cap”。

## 当前结论

- 目前可以确认的静态结论:
  - `size < 1` 的 subpixel 修复方向是对的,应该保留。
  - 摩尔纹主方向应该从“show/hide warp 限幅”切回“打散规则 cell 栅格”。
- 还没完成的动态验证:
  - 需要用测试锁定新参数确实走通到 shader。
  - 需要确保新 jitter 方案不会把 first return 数据竞争语义改掉,也不会引入时间闪烁。

## 下一步验证计划

- 先补齐 `GsplatLidarScan.cs` 的参数签名与 MPB 下发。
- 再补 shader 壳文件和两个 inspector 的可视化入口。
- 最后把错误方向的 shader 测试改成“stable in-cell jitter + subpixel radius”契约,并跑一次包级 EditMode 测试确认没有新增失败。

## [2026-03-23 17:24:59] [Session ID: 20260323_8] 笔记: `size < 1` 粒子消失问题的最小证伪

## 现象

- 用户确认 `jitter` 已经可以接受。
- 当前剩余问题收敛为: LiDAR 粒子半径小于 `1px` 时,会出现“显示不出来 / 容易消失”的现象。
- 当前默认 `LidarParticleAntialiasingMode` 仍是 `LegacySoftEdge`。

## 当前主假设

- 主假设A: 现在的问题不只是 fragment 里保留了真实 subpixel 半径,还因为顶点阶段也把 billboard 几何真实缩到了 `< 1px`。
- 主假设B: 当几何 footprint 太小而且仍走 `LegacySoftEdge` 时,标准像素中心采样很容易根本不命中这个 quad,于是 fragment 不执行,看起来就像“粒子消失”。
- 主假设C: 即便强行给几何加大 footprint,如果 fragment 仍只按真实半径做硬边界 discard,subpixel 点仍可能没有任何可见 coverage,所以需要把“真实半径”和“光栅/coverage 支撑半径”分开。

## 最强备选解释

- 备选解释A: 真正导致消失的是 A2C / AnalyticCoverage 分支的某个 alpha 公式不连续,而不是几何 footprint 太小。
- 备选解释B: 问题只发生在某个特定 AA 模式,默认 Legacy 路径未必需要修。

## 静态证据

### 来源1: `Runtime/Shaders/GsplatLidarPassCore.hlsl`

- 顶点阶段当前直接使用真实半径做几何外扩:
  - `float rPx = max(_LidarPointRadiusPixels, 0.0);`
  - `float paddedRadiusPx = rPx + aaFringePadPx;`
  - `float2 offset = float2(v.vertex.x, v.vertex.y) * paddedRadiusPx * c;`
- 默认 `LegacySoftEdge` 下 `coverageAaEnabled == 0`,因此 `aaFringePadPx == 0`,也就是 `<1px` 点会真的生成一个 `<1px` 的屏幕空间 quad。
- fragment 阶段当前对 subpixel 只修了“真实半径不再钳到 1px”: 
  - `float pointRadiusPxRaw = max(_LidarPointRadiusPixels, 0.0);`
  - `float pointRadiusPx = max(pointRadiusPxRaw, 1.0e-4);`
- 但这还没有解决“几何根本没被光栅化”的问题。

### 来源2: `Runtime/GsplatRenderer.cs` / `Runtime/GsplatSequenceRenderer.cs`

- 默认 AA 模式是 `LegacySoftEdge`。
- 默认 `LidarParticleAAFringePixels = 1.0f`,但它只在非 Legacy 模式生效。
- 这意味着默认配置下,subpixel 点并不会自动获得额外 raster / coverage 支撑空间。

## 最小动态验证

### 验证脚本

```bash
python3 - <<'PY'
import math

def covered_pixel_centers(radius_px, center_offset_x, center_offset_y):
    hits=[]
    left = center_offset_x - radius_px
    right = center_offset_x + radius_px
    bottom = center_offset_y - radius_px
    top = center_offset_y + radius_px
    for iy in range(-1, 2):
        for ix in range(-1, 2):
            px = ix + 0.5
            py = iy + 0.5
            if left <= px <= right and bottom <= py <= top:
                hits.append((ix, iy))
    return hits

print('radius\toffset\thit_count\thits')
for radius in [1.0, 0.75, 0.5, 0.4, 0.3, 0.25, 0.1]:
    for off in [0.0, 0.25, 0.5]:
        hits = covered_pixel_centers(radius, off, off)
        print(f'{radius:.2f}\t{off:.2f}\t{len(hits)}\t{hits}')
    print('-')
PY
```

### 关键输出

- `radius=0.40, offset=0.00 -> hit_count=0`
- `radius=0.30, offset=0.00 -> hit_count=0`
- `radius=0.25, offset=0.00 -> hit_count=0`
- `radius=0.10, offset=0.25 -> hit_count=0`

### 结论

- 这个最小实验支持主假设A/B:
  - 当点几何真实缩到 `< 1px` 后,标准像素中心采样下确实可能一个像素都打不到。
  - 所以“显示消失”不能只靠保留真实半径解决,还需要单独保留一层 subpixel 的 raster / coverage 支撑 footprint。
- 目前还缺的部分是主假设C 的正式实现验证:
  - 需要把 shader 改成“真实半径用于视觉强度,支撑半径用于光栅和 coverage”,再跑测试确认这条链路成立。

## [2026-03-23 17:24:59] [Session ID: 20260323_8] 笔记: subpixel 消失问题的修复结论与验证结果

## 结论

- 主假设A/B 已被当前实现与测试共同支持:
  - 问题不只是 fragment 保留了真实半径后仍然异常。
  - 更关键的是 `<1px` 点在默认 `LegacySoftEdge` 路径下, billboard 几何和 coverage 支撑都过小,标准像素中心采样下可能完全打不到片元。
- 最终修复不是把真实半径再钳回 `1px`。
- 最终修复是把“真实半径”和“subpixel 的 raster / coverage 支撑宽度”拆开:
  - 真实半径继续保留给视觉语义。
  - `<1px` 时额外保留 `1px` 的 coverage support,只服务于光栅与 coverage,不改真实 size 语义。

## 已落地实现

### `Runtime/Shaders/GsplatLidarPassCore.hlsl`

- 新增 `ResolveLidarSubpixelCoverageSupportPx(float pointRadiusPxRaw)`
  - 当 `0 < pointRadiusPxRaw < 1` 时,返回 `1.0`。
- 新增 `ResolveLidarCoveragePadPx(float pointRadiusPxRaw, float coverageAaEnabled)`
  - 合并 AA fringe 与 subpixel support,统一给顶点/片元两侧使用。
- 顶点阶段改为:
  - `float coveragePadPx = ResolveLidarCoveragePadPx(rPx, coverageAaEnabled);`
  - `float paddedRadiusPx = rPx + coveragePadPx;`
- 片元阶段改为:
  - `float subpixelCoverageSupportPx = ResolveLidarSubpixelCoverageSupportPx(pointRadiusPxRaw);`
  - `float coveragePadPx = ResolveLidarCoveragePadPx(pointRadiusPxRaw, coverageAaEnabled);`
  - `float outerLimit = 1.0 + coveragePadPx / pointRadiusPx;`
  - `float fixedCoverageAlphaShape = saturate(signedEdgePx / max(coveragePadPx, 1.0e-4) + 0.5);`
  - `if (coverageAaEnabled > 0.5 || subpixelCoverageSupportPx > 0.0)`
- 这样即使用户还在默认 `LegacySoftEdge`,当粒径小于 `1px` 时也不会因为支撑 footprint 退化而直接消失。

### `Tests/Editor/GsplatLidarShaderPropertyTests.cs`

- 把 shader 契约测试同步到新语义:

## [2026-03-23 21:02:50 +0800] [Session ID: 20260323_9] 笔记: external capture supersampling 方案1的 OpenSpec 设计依据

## 现象

- 用户希望继续推进“方案1”,即通过 external capture supersampling 缓解 frustum external GPU capture 的 depth 台阶。
- 当前目标不是直接改实现代码,而是先把 OpenSpec change 快进到可实施状态。

## 已确认的静态证据

### 来源1: `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`

- external capture 创建的 RT 当前使用:
  - `filterMode = FilterMode.Point`
  - `antiAliasing = 1`
- 这说明 capture 结果本身没有依赖硬件 MSAA 做平滑。

### 来源2: `Runtime/Shaders/Gsplat.compute`

- `ResolveExternalFrustumHits` 当前会把 `uv` 直接映射成整数像素:
  - `int2 staticPixel = int2(...)`
- 然后使用:
  - `_LidarExternalStaticLinearDepthTex.Load(int3(staticPixel, 0)).x`
- dynamic external 路径也是同样模式。
- 这说明当前 resolve 是典型的 point texel read,不是 bilinear,也不是 neighborhood-aware resolve。

### 来源3: `Runtime/Shaders/GsplatLidarExternalCapture.shader`

- external capture 写入的是最近表面的线性 view depth。
- 这是一张离散屏幕空间 depth 图,不是连续几何求交结果。

## 当前结论

- 对于 frustum external GPU capture 这条链路来说,“depth 基于像素采样而产生台阶感”已经有代码证据支撑。
- 但这不能外推成“高密度波纹细缝全部都由 depth 台阶单独造成”。
- 当前更稳妥的 OpenSpec 方案1应该是:
  - 先提高 external capture 的离屏分辨率
  - 继续保留 point resolve 与 nearest-surface / nearest-hit 语义
  - 暂不引入 blur、naive bilinear 深度混合、edge-aware resolve

## 最强备选解释

- 备选解释A: 轮廓台阶确实能被 supersampling 缓解,但规则 LiDAR 栅格自身的 moire 仍会残留。
- 备选解释B: 如果直接做 blur 或 bilinear 深度混合,虽然边缘看起来更“顺”,但前后表面可能被错误混合,反而破坏 first-hit 语义。

## 对 OpenSpec artifact 的影响

- `design.md` 需要明确:
  - 方案1是“提高 capture fidelity”,不是“改变 depth resolve 语义”
  - `Scale` 模式是首选质量入口
  - 风险主要是显存/带宽/性能开销,不是语义变化
- capability spec 需要锁定:
  - supersampling 是正式支持的质量路径
  - `Scale` 模式下分辨率推导必须可预测
  - supersampling 不得改变最近表面选择语义
  - 文档/Inspector 要把它作为台阶问题的推荐缓解手段
  - 锁定 `ResolveLidarSubpixelCoverageSupportPx`
  - 锁定 `ResolveLidarCoveragePadPx`
  - 锁定 `<1px` 时会启用额外 coverage support,而不是回到旧的 `max(..., 1.0)` 语义

## 验证结果

### 编译验证

```bash
dotnet build ../../Gsplat.Tests.Editor.csproj -v minimal
```

结果:
- `0 个警告`
- `0 个错误`

### Unity 包级 EditMode 验证

```bash
/Applications/Unity/Hub/Editor/6000.3.8f1/Unity.app/Contents/MacOS/Unity \
  -batchmode \
  -accept-apiupdate \
  -projectPath /tmp/gsplat_pkg_min_project \
  -runTests \
  -testPlatform EditMode \
  -assemblyNames Gsplat.Tests.Editor \
  -testResults /tmp/gsplat_pkg_min_project_tests_20260323_173424.xml \
  -logFile /tmp/gsplat_pkg_min_project_unity_20260323_173424.log
```

结果摘要:
- `total=123`
- `passed=117`
- `failed=3`
- `skipped=3`

与本次相关的测试结果:
- `Gsplat.Tests.GsplatLidarShaderPropertyTests.LidarShader_UsesAnalyticCoverageAndExternalHitCompetition => Passed`
- `Gsplat.Tests.GsplatLidarShaderPropertyTests.LidarShader_ProtectsSubpixelPointRadiusAndUsesStableInCellJitter => Passed`
- `Gsplat.Tests.GsplatLidarShaderPropertyTests.LidarAlphaToCoverageShader_DeclaresAlphaToMaskOn => Passed`
- `Gsplat.Tests.GsplatLidarScanTests.ValidateLidarSerializedFields_PreservesSubpixelPointRadius_GsplatRenderer => Passed`
- `Gsplat.Tests.GsplatLidarScanTests.ValidateLidarSerializedFields_PreservesSubpixelPointRadius_GsplatSequenceRenderer => Passed`

剩余 3 个失败测试:
- `Gsplat.Tests.GsplatVisibilityAnimationTests.AdvanceVisibilityState_LegacyDurationMode_ProgressRemainsBoundsIndependent`
- `Gsplat.Tests.GsplatVisibilityAnimationTests.AdvanceVisibilityState_WorldSpeedMode_KeepsRevealFrontDistanceComparableAcrossBoundsScales`
- `Gsplat.Tests.GsplatVisibilityAnimationTests.SetRenderStyleAndRadarScan_Animated_KeepsSplatsUntilGaussianAlphaFadeFinishes`

判断:
- 这 3 个失败与本次 LiDAR subpixel / jitter 修复无直接对应关系。
- 当前证据支持“本次改动没有扩大失败面,相关 LiDAR 测试已通过”。

## [2026-03-23 17:24:59] [Session ID: 20260323_8] 笔记: 为什么密度打高后会出现空隙和波纹细缝

## 现象

- 用户追问: 为什么 LiDAR / RadarScan 的粒子密度打高以后,反而会出现大量空隙和波纹细缝。
- 这类现象在“点分布非常均匀 + 单点尺寸较小”时最明显。

## 当前主假设

- 主假设A: 这不是“密度越高,几何上真的更稀疏”了。
- 主假设B: 真正出现的是采样拍频:
  - LiDAR 点是规则角栅格.
  - 屏幕显示又是规则像素栅格.
  - 当两套规则频率接近但又不完全一致时,会出现有组织的明暗带、细缝、空隙,也就是摩尔纹 / beat pattern。
- 主假设C: 当单点半径较小,甚至进入 subpixel 区间时,每个点对像素的覆盖本来就很脆弱,所以这种拍频会被放大,看起来像“大量空隙”。

## 最强备选解释

- 备选解释A: 不是摩尔纹,而是 show/hide 的 warp 或噪声把点真的拉开了。
- 备选解释B: 不是规则栅格问题,而是 fragment alpha 或深度混合公式造成的周期性丢失。

## 当前证据

### 静态证据

- 当前 LiDAR 点默认按 `(azBin, beamIndex)` 的规则角域中心重建。
- 在未加 jitter 的情况下,每个 cell 都落在非常规则的 bin center 上。
- 这天然会形成一个规则点阵,与屏幕像素栅格发生拍频。

### 最小数值实验

- 用规则点阵覆盖规则像素中心做简化模拟。
- 只改变点阵步长(step),保持点为小方形 footprint。
- 结果会出现明显的 coverage 波动: 有些步长 / 相位组合下覆盖率突然掉很多,看起来就是“空一条缝”或“出现明暗波纹”。

## 当前结论

- 目前更符合证据的解释是:
  - 密度打高以后,问题不是“点不够多”,而是“点太规则”。
  - 当规则点阵进入接近像素频率的区间时,屏幕上看到的是采样拍频,不是物理密度本身。
- 所以用户看到“大量空隙和波纹细缝”,本质上是 aliasing / moire,不是数据真的缺了一大片。
- 这也是为什么 stable in-cell jitter 会有效:
  - 它不是增加点数。
  - 它是在打散规则相位,让原本集中成条纹的误差变成更均匀的细噪声。

## [2026-03-23 17:24:59] [Session ID: 20260323_8] 笔记: camera depth 像素阶梯假设的静态验证

## 现象

- 用户怀疑: 细缝/阶梯是否来自 camera depth 图本身按像素采样,深度存在 pixel-based 阶梯。
- 用户进一步问: 能不能在深度图采样时做一个过渡。

## 当前主假设

- 主假设A: 如果当前走的是 frustum external GPU capture 路径,那么 external mesh 的深度确实不是连续几何解析求交,而是“先渲染到离屏 depth RT,再在 compute 中按像素读取”。
- 主假设B: 这条路径上的深度读取当前是 nearest / point 风格,没有任何 bilinear / neighborhood 过渡。
- 主假设C: 因此如果用户看到的是 external target 边缘、斜面、远处小结构上的阶梯 / 细缝,那么 depth RT 的像素化确实可能是参与因素。

## 最强备选解释

- 备选解释A: 当前用户看到的主要细缝仍然是 LiDAR 规则角栅格 vs 屏幕像素栅格的摩尔纹,而不是 external depth capture。
- 备选解释B: 即使 external depth 存在像素阶梯,它也更可能影响 external target 的表面轮廓与远处斜边,不一定解释所有“高密度规则细缝”。

## 静态证据

### 来源1: `Runtime/Lidar/GsplatLidarExternalGpuCapture.cs`

- external capture 创建 RT 时明确使用:
  - `filterMode = FilterMode.Point`
  - `antiAliasing = 1`
- 深度与颜色 capture 纹理都是 point filter。

### 来源2: `Runtime/Shaders/Gsplat.compute`

- external frustum resolve 不是连续采样,而是直接把 uv 映射到整数像素:
  - `int2 staticPixel = int2(min((int)(uv.x * (float)staticCaptureWidth), staticCaptureWidth - 1), ... )`
  - `float linearDepth = _LidarExternalStaticLinearDepthTex.Load(int3(staticPixel, 0)).x;`
- dynamic 路径同样如此:
  - `float linearDepth = _LidarExternalDynamicLinearDepthTex.Load(int3(dynamicPixel, 0)).x;`
- `Load(...)` 是整数 texel 读取,没有双线性过滤,也没有邻域平滑。

### 来源3: `Runtime/Shaders/GsplatLidarExternalCapture.shader`

- depth capture pass 写的是“当前像素最近表面”的线性 view depth。
- 这说明 external capture 的深度本质上就是一张屏幕空间离散深度图,不是 per-ray 的连续几何求交结果。

## 当前结论

- 如果讨论的是 frustum external GPU capture 这条链路,那么“深度基于 pixel 采样,存在阶梯”是有静态证据支撑的。
- 但是不能把它直接说成“当前所有波纹细缝的唯一根因”。
- 更准确的说法是:
  - 它是一个真实存在的候选来源。
  - 它更容易在 external target 的边界、斜面、远处小结构上制造台阶感、断续感。
  - 而高密度规则点阵本身的摩尔纹问题,仍然是另一条独立来源。

## 能否做“深度采样过渡”

- 可以做,而且方向上是合理的。
- 但要注意: 不能简单对深度做普通线性 blur,否则会把前后表面混在一起,造成穿帮或漂浮。
- 更稳的方向通常是:
  1. 小邻域(min / median / bilateral-like) 深度 resolve
  2. 对轮廓 / 深度跳变做阈值保护,避免跨物体边缘混合
  3. 或直接 supersample capture RT,降低像素阶梯本身

## [2026-03-23 17:24:59] [Session ID: 20260323_8] 笔记: 已创建方案1的 OpenSpec change

## 目标

- 为“external capture 超采样降低 depth 台阶”创建独立 OpenSpec change。
- change 名称采用: `lidar-external-capture-supersampling`
- 这样后续可以单独跟踪 proposal / design / tasks,避免把实现意图散落在临时对话里。

## [2026-03-23 17:24:59] [Session ID: 20260323_8] 笔记: 已创建 supersampling change 的 proposal

- artifact: `openspec/changes/lidar-external-capture-supersampling/proposal.md`
- capability 命名: `gsplat-lidar-external-capture-quality`
- proposal 重点:
  - external depth 的 texel 阶梯问题
  - supersampling 作为低风险优先方案
  - 不先引入更重的 edge-aware resolve
