// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Gsplat.Tests
{
    public sealed class GsplatLidarShaderPropertyTests
    {
        [Test]
        public void LidarShader_ContainsShowHideOverlayProperties()
        {
            // 说明:
            // - RadarScan(LiDAR) 的 show/hide noise 在用户侧出现过“完全无变化”的反馈.
            // - 其中一类高概率根因是: shader 没有暴露对应的 property,导致 MPB 下发被忽略.
            // - 这里用单测锁定“属性契约”: shader 必须包含 show/hide overlay 所需的隐藏属性(Noise/Warp/Glow).
            const string kShaderAssetPath = "Packages/wu.yize.gsplat/Runtime/Shaders/GsplatLidar.shader";

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(kShaderAssetPath);
            Assert.IsNotNull(shader, $"Failed to load LiDAR shader at path: {kShaderAssetPath}");
            Assert.AreEqual("Gsplat/LiDAR", shader.name, "Unexpected shader name. Possibly loaded a different asset.");

            // Shader 反射: propertyIndex 必须存在.
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarParticleAAAnalyticCoverage"), 0);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarParticleAAFringePixels"), 0);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarPointJitterCellFraction"), 0);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarExternalHitBiasMeters"), 0);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarShowHideNoiseMode"), 0);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarShowHideNoiseStrength"), 0);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarShowHideNoiseScale"), 0);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarShowHideNoiseSpeed"), 0);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarShowHideWarpPixels"), 0);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarShowHideWarpStrength"), 0);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarShowHideGlowColor"), 0);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarShowHideGlowIntensity"), 0);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarUnscannedIntensity"), 0);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarIntensityDistanceDecay"), 0);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarUnscannedIntensityDistanceDecay"), 0);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarIntensityDistanceDecayMode"), 0);
            Assert.GreaterOrEqual(shader.FindPropertyIndex("_LidarEnableScanMotion"), 0);

            // Material: HasProperty(int) 必须为 true,否则 Render 时的诊断会显示 hasProp=0.
            var mat = new Material(shader);
            try
            {
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarParticleAAAnalyticCoverage")));
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarParticleAAFringePixels")));
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarPointJitterCellFraction")));
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarExternalHitBiasMeters")));
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarShowHideNoiseMode")));
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarShowHideNoiseStrength")));
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarShowHideNoiseScale")));
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarShowHideNoiseSpeed")));
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarShowHideWarpPixels")));
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarShowHideWarpStrength")));
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarShowHideGlowColor")));
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarShowHideGlowIntensity")));
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarUnscannedIntensity")));
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarIntensityDistanceDecay")));
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarUnscannedIntensityDistanceDecay")));
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarIntensityDistanceDecayMode")));
                Assert.IsTrue(mat.HasProperty(Shader.PropertyToID("_LidarEnableScanMotion")));
            }
            finally
            {
                Object.DestroyImmediate(mat);
            }
        }

        [Test]
        public void LidarShader_UsesAnalyticCoverageAndExternalHitCompetition()
        {
            // 说明:
            // - external target 与 gsplat 的核心语义不是“叠加显示”,而是逐 cell 竞争 first return 最近距离.
            // - 本轮又新增了 analytic coverage,因此这里顺手锁定:
            //   1) shader shell 正在包含共享 pass core.
            //   2) pass core 里确实存在 `fwidth` 驱动的 analytic coverage 路线.
            const string kShaderAssetPath = "Packages/wu.yize.gsplat/Runtime/Shaders/GsplatLidar.shader";
            const string kPassCoreAssetPath = "Packages/wu.yize.gsplat/Runtime/Shaders/GsplatLidarPassCore.hlsl";

            var projectRoot = Directory.GetParent(Application.dataPath);
            Assert.IsNotNull(projectRoot, "Failed to resolve Unity project root from Application.dataPath.");

            var shaderFullPath = Path.Combine(projectRoot.FullName, kShaderAssetPath);
            var passCoreFullPath = Path.Combine(projectRoot.FullName, kPassCoreAssetPath);
            Assert.IsTrue(File.Exists(shaderFullPath), $"Expected shader source file to exist: {shaderFullPath}");
            Assert.IsTrue(File.Exists(passCoreFullPath), $"Expected shared pass core file to exist: {passCoreFullPath}");

            var shaderText = File.ReadAllText(shaderFullPath);
            var passCoreText = File.ReadAllText(passCoreFullPath);

            StringAssert.Contains("#include \"GsplatLidarPassCore.hlsl\"", shaderText);
            StringAssert.Contains("float _LidarParticleAAAnalyticCoverage;", passCoreText);
            StringAssert.Contains("float _LidarParticleAAFringePixels;", passCoreText);
            StringAssert.Contains("float _LidarPointJitterCellFraction;", passCoreText);
            StringAssert.Contains("float _LidarExternalHitBiasMeters;", passCoreText);
            StringAssert.Contains("float _LidarEnableScanMotion;", passCoreText);
            StringAssert.Contains("float _LidarBeamMinRad;", passCoreText);
            StringAssert.Contains("float _LidarBeamMaxRad;", passCoreText);
            StringAssert.Contains("#define GSPLAT_LIDAR_A2C_PASS 0", passCoreText);
            StringAssert.Contains("if (_LidarEnableScanMotion > 0.5)", passCoreText);
            StringAssert.Contains("float trail01 = 1.0;", passCoreText);
            StringAssert.Contains("float ResolveLidarSubpixelCoverageSupportPx(float pointRadiusPxRaw)", passCoreText);
            StringAssert.Contains("return (pointRadiusPxRaw > 1.0e-4 && pointRadiusPxRaw < 1.0) ? 1.0 : 0.0;", passCoreText);
            StringAssert.Contains("float ResolveLidarCoveragePadPx(float pointRadiusPxRaw, float coverageAaEnabled)", passCoreText);
            StringAssert.Contains("float coveragePadPx = ResolveLidarCoveragePadPx(rPx, coverageAaEnabled);", passCoreText);
            StringAssert.Contains("float paddedRadiusPx = rPx + coveragePadPx;", passCoreText);
            StringAssert.Contains("float subpixelCoverageSupportPx = ResolveLidarSubpixelCoverageSupportPx(pointRadiusPxRaw);", passCoreText);
            StringAssert.Contains("float outerLimit = 1.0 + coveragePadPx / pointRadiusPx;", passCoreText);
            StringAssert.Contains("float fixedCoverageAlphaShape = saturate(signedEdgePx / max(coveragePadPx, 1.0e-4) + 0.5);",
                passCoreText);
            StringAssert.Contains("float pointRadiusPxRaw = max(_LidarPointRadiusPixels, 0.0);", passCoreText);
            StringAssert.Contains("float pointRadiusPx = max(pointRadiusPxRaw, 1.0e-4);", passCoreText);
            StringAssert.Contains("float signedEdgePx = signedEdge * pointRadiusPx;", passCoreText);
            StringAssert.Contains("float analyticWidthPx = max(fwidth(signedEdgePx), 1.0e-4);", passCoreText);
            StringAssert.Contains("saturate(_LidarParticleAAAnalyticCoverage)", passCoreText);
            StringAssert.Contains("uint externalRangeSqBits = _LidarExternalRangeSqBits[cellId];", passCoreText);
            StringAssert.Contains("bool useExternalHit = hasExternalHit && (!hasSplatHit || externalRangeSqBits < splatRangeSqBits);",
                passCoreText);
            StringAssert.Contains("uint rangeSqBits = useExternalHit ? externalRangeSqBits : splatRangeSqBits;", passCoreText);
            StringAssert.Contains("float renderRange = range;", passCoreText);
            StringAssert.Contains("renderRange = max(range - max(_LidarExternalHitBiasMeters, 0.0), 0.0);", passCoreText);
            StringAssert.Contains("float3 worldPos = mul(_LidarMatrixL2W, float4(dirLocal * renderRange, 1.0)).xyz;",
                passCoreText);
        }

        [Test]
        public void LidarShader_ProtectsSubpixelPointRadiusAndUsesStableInCellJitter()
        {
            // 说明:
            // - 用户已经明确反馈两类视觉异常:
            //   1) `LidarPointRadiusPixels < 1` 时显示不连续.
            //   2) 点分布过于均匀时,高密度下会出现有组织的摩尔纹.
            // - 这里锁定修复后的 shader 契约:
            //   - fragment 必须保留真实 subpixel 半径,不能再把它强制折叠到 1px.
            //   - 顶点阶段必须支持 stable in-cell jitter,用每个 cell 的稳定 hash 在角域内部打散规则栅格.
            const string kPassCoreAssetPath = "Packages/wu.yize.gsplat/Runtime/Shaders/GsplatLidarPassCore.hlsl";

            var projectRoot = Directory.GetParent(Application.dataPath);
            Assert.IsNotNull(projectRoot, "Failed to resolve Unity project root from Application.dataPath.");

            var passCoreFullPath = Path.Combine(projectRoot.FullName, kPassCoreAssetPath);
            Assert.IsTrue(File.Exists(passCoreFullPath), $"Expected shared pass core file to exist: {passCoreFullPath}");

            var passCoreText = File.ReadAllText(passCoreFullPath);

            StringAssert.Contains("float pointRadiusPxRaw = max(_LidarPointRadiusPixels, 0.0);", passCoreText);
            StringAssert.Contains("float pointRadiusPx = max(pointRadiusPxRaw, 1.0e-4);", passCoreText);
            StringAssert.Contains("float coveragePadPx = ResolveLidarCoveragePadPx(rPx, coverageAaEnabled);", passCoreText);
            StringAssert.Contains("float coveragePadPx = ResolveLidarCoveragePadPx(pointRadiusPxRaw, coverageAaEnabled);", passCoreText);
            StringAssert.Contains("if (coverageAaEnabled > 0.5 || subpixelCoverageSupportPx > 0.0)", passCoreText);
            StringAssert.Contains("float pointJitterCellFraction = saturate(_LidarPointJitterCellFraction);", passCoreText);
            StringAssert.Contains("float azimuthStepRad = azimuthSpanRad / max((float)_LidarAzimuthBins, 1.0);", passCoreText);
            StringAssert.Contains("float beamStepRad = beamSpanRad / max((float)_LidarBeamCount, 1.0);", passCoreText);
            StringAssert.Contains("Hash13(float3((float)cellId, 17.13, 31.77))", passCoreText);
            StringAssert.Contains("Hash13(float3((float)cellId, 53.11, 12.77))", passCoreText);
            StringAssert.Contains("dirLocal = normalize(dirLocal + azimuthTangent * jitterAzRad + beamTangent * jitterBeamRad);",
                passCoreText);
        }

        [Test]
        public void LidarAlphaToCoverageShader_DeclaresAlphaToMaskOn()
        {
            const string kA2CShaderAssetPath = "Packages/wu.yize.gsplat/Runtime/Shaders/GsplatLidarAlphaToCoverage.shader";

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(kA2CShaderAssetPath);
            Assert.IsNotNull(shader, $"Failed to load LiDAR A2C shader at path: {kA2CShaderAssetPath}");
            Assert.AreEqual("Gsplat/LiDARAlphaToCoverage", shader.name,
                "Unexpected LiDAR A2C shader name. Possibly loaded a different asset.");

            var projectRoot = Directory.GetParent(Application.dataPath);
            Assert.IsNotNull(projectRoot, "Failed to resolve Unity project root from Application.dataPath.");

            var shaderFullPath = Path.Combine(projectRoot.FullName, kA2CShaderAssetPath);
            Assert.IsTrue(File.Exists(shaderFullPath), $"Expected A2C shader source file to exist: {shaderFullPath}");

            var shaderText = File.ReadAllText(shaderFullPath);
            StringAssert.Contains("AlphaToMask On", shaderText);
            StringAssert.Contains("_LidarParticleAAFringePixels", shaderText);
            StringAssert.Contains("_LidarPointJitterCellFraction", shaderText);
            StringAssert.Contains("_LidarExternalHitBiasMeters", shaderText);
            StringAssert.Contains("_LidarEnableScanMotion", shaderText);
            StringAssert.Contains("#define GSPLAT_LIDAR_A2C_PASS 1", shaderText);
            StringAssert.Contains("#include \"GsplatLidarPassCore.hlsl\"", shaderText);
            StringAssert.Contains("\"RenderType\"=\"TransparentCutout\"", shaderText);
            StringAssert.Contains("Blend One Zero", shaderText);
            Assert.IsFalse(shaderText.Contains("Blend SrcAlpha OneMinusSrcAlpha"),
                "A2C shell 不应继续沿用普通透明混合,否则很难体现 coverage-first 的差异.");
        }
    }
}
