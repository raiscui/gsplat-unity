// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System.Reflection;
using NUnit.Framework;

namespace Gsplat.Tests
{
    public sealed class GsplatSequenceAssetTests
    {
        [Test]
        public void EvaluateFromTimeNormalized_UniformSingleFrame_AlwaysReturnsFixedFrame()
        {
            // 单帧 uniform 映射在任意时间点都必须退化成固定帧.
            // 这条语义是 `.sog4d` 单帧 bundle 能继续走 sequence 路线的基础契约.
            var mapping = new GsplatSequenceTimeMapping
            {
                Type = GsplatSequenceTimeMapping.MappingType.Uniform
            };

            var samples = new[] { -0.25f, 0.0f, 0.35f, 1.0f, 1.75f };
            for (var i = 0; i < samples.Length; i++)
            {
                mapping.EvaluateFromTimeNormalized(1, samples[i], out var i0, out var i1, out var a);
                Assert.AreEqual(0, i0, $"uniform 单帧 i0 应固定为 0, sampleIndex={i}");
                Assert.AreEqual(0, i1, $"uniform 单帧 i1 应固定为 0, sampleIndex={i}");
                Assert.AreEqual(0.0f, a, 1e-6f, $"uniform 单帧插值因子应固定为 0, sampleIndex={i}");
            }
        }

        [Test]
        public void EvaluateFromTimeNormalized_ExplicitSingleFrame_AlwaysReturnsFixedFrame()
        {
            // explicit 单帧同样不能因为 frameTimesNormalized 存在而要求“下一帧”.
            // 不同 TimeNormalized 输入都应得到相同的固定帧结果.
            var mapping = new GsplatSequenceTimeMapping
            {
                Type = GsplatSequenceTimeMapping.MappingType.Explicit,
                FrameTimesNormalized = new[] { 0.42f }
            };

            var samples = new[] { -0.25f, 0.0f, 0.42f, 1.0f, 1.75f };
            for (var i = 0; i < samples.Length; i++)
            {
                mapping.EvaluateFromTimeNormalized(1, samples[i], out var i0, out var i1, out var a);
                Assert.AreEqual(0, i0, $"explicit 单帧 i0 应固定为 0, sampleIndex={i}");
                Assert.AreEqual(0, i1, $"explicit 单帧 i1 应固定为 0, sampleIndex={i}");
                Assert.AreEqual(0.0f, a, 1e-6f, $"explicit 单帧插值因子应固定为 0, sampleIndex={i}");
            }
        }

        [Test]
        public void ShouldUseLinearInterpolation_SameFramePairFallsBackToFixedFrame()
        {
            // decode 阶段真正决定“要不要读第二帧”的地方在 renderer.
            // 这里用反射锁定其单帧退化语义,避免未来重构把 `Linear + same frame` 又误当成双帧插值.
            var method = typeof(GsplatSequenceRenderer).GetMethod(
                "ShouldUseLinearInterpolation",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.IsNotNull(method, "单帧插值退化 helper 丢失了,请同步更新测试.");

            Assert.IsFalse((bool)method.Invoke(null, new object[] { GsplatInterpolationMode.Linear, 0, 0 }),
                "Linear 模式在同一帧对上时必须退化为固定帧.");
            Assert.IsFalse((bool)method.Invoke(null, new object[] { GsplatInterpolationMode.Nearest, 0, 1 }),
                "Nearest 模式永远不应开启线性插值.");
            Assert.IsTrue((bool)method.Invoke(null, new object[] { GsplatInterpolationMode.Linear, 0, 1 }),
                "只有 Linear + 两个不同帧索引时,才允许真正的双帧插值.");
        }
    }
}
