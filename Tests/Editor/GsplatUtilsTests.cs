// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using UnityEngine;

namespace Gsplat.Tests
{
    public sealed class GsplatUtilsTests
    {
        [Test]
        public void SHBandsToCoefficientCount_ReturnsExpected()
        {
            // 目的: 锁定 SH bands 与系数数量的映射关系,避免后续修改时引入 off-by-one.
            Assert.AreEqual(0, GsplatUtils.SHBandsToCoefficientCount(0));
            Assert.AreEqual(3, GsplatUtils.SHBandsToCoefficientCount(1));
            Assert.AreEqual(8, GsplatUtils.SHBandsToCoefficientCount(2));
            Assert.AreEqual(15, GsplatUtils.SHBandsToCoefficientCount(3));
        }

        [Test]
        public void CalcSHBandsFromSHPropertyCount_ReturnsExpected()
        {
            // 目的: 验证 PLY 中 `f_rest_*` 属性数量与 bands 的推导一致.
            Assert.AreEqual(0, GsplatUtils.CalcSHBandsFromSHPropertyCount(0));
            Assert.AreEqual(1, GsplatUtils.CalcSHBandsFromSHPropertyCount(9));
            Assert.AreEqual(2, GsplatUtils.CalcSHBandsFromSHPropertyCount(24));
            Assert.AreEqual(3, GsplatUtils.CalcSHBandsFromSHPropertyCount(45));
        }

        [Test]
        public void BytesToMiB_ConvertsCorrectly()
        {
            // 目的: 确保日志里 MiB 估算的单位换算没有偏差.
            Assert.AreEqual(1.0f, GsplatUtils.BytesToMiB(1024L * 1024L), 1e-6f);
        }

        [Test]
        public void EstimateGpuBytes_ZeroSplats_HasGlobalHistogramBuffer()
        {
            // 说明: 即使 splatCount=0,估算函数仍会包含排序实现中的固定全局直方图 buffer.
            Assert.AreEqual(256 * 4 * 4, GsplatUtils.EstimateGpuBytes(0, 0, false));
        }

        [Test]
        public void EstimateGpuBytes_OneSplat_MatchesCurrentFormula()
        {
            // 说明: 这里用手算锁定当前实现中的 buffer 规模,防止后续改动不小心引入倍数级膨胀.
            const long expected = 5192;
            Assert.AreEqual(expected, GsplatUtils.EstimateGpuBytes(1, 0, false));
        }

        [Test]
        public void CalcWorldBounds_IdentityTransform_OnlyTranslates()
        {
            // 目的: 验证 `CalcWorldBounds` 在无旋转/无缩放时不会引入额外误差.
            var go = new GameObject("GsplatUtilsTests_CalcWorldBounds");
            try
            {
                var tr = go.transform;
                tr.position = new Vector3(1, 2, 3);
                tr.rotation = Quaternion.identity;
                tr.localScale = Vector3.one;

                var localBounds = new Bounds(Vector3.zero, Vector3.one * 2); // extents=(1,1,1)
                var worldBounds = GsplatUtils.CalcWorldBounds(localBounds, tr);

                Assert.That(worldBounds.center, Is.EqualTo(tr.position));
                Assert.That(worldBounds.extents, Is.EqualTo(localBounds.extents));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
