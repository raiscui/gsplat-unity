// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

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

            // Material: HasProperty(int) 必须为 true,否则 Render 时的诊断会显示 hasProp=0.
            var mat = new Material(shader);
            try
            {
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
            }
            finally
            {
                Object.DestroyImmediate(mat);
            }
        }
    }
}
