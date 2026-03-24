// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Gsplat.Tests
{
    public sealed class GsplatUtilsTests
    {
        static Vector2Int[] InvokeDebugResolveLinearComputeDispatchChunksForInputs(int itemCount,
            int threadsPerGroup,
            int maxGroupsPerDispatch)
        {
            var method = typeof(GsplatUtils).GetMethod("DebugResolveLinearComputeDispatchChunksForInputs",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Expected internal compute dispatch chunk helper to exist.");
            return (Vector2Int[])method.Invoke(null, new object[] { itemCount, threadsPerGroup, maxGroupsPerDispatch });
        }

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
        public void EaseInOutQuart_ReturnsExpectedValues()
        {
            // 目的: 锁定 render style 切换默认动画曲线的关键采样点,避免节奏被意外修改.
            Assert.AreEqual(0.0f, GsplatUtils.EaseInOutQuart(0.0f), 1e-6f);
            Assert.AreEqual(0.03125f, GsplatUtils.EaseInOutQuart(0.25f), 1e-6f);
            Assert.AreEqual(0.5f, GsplatUtils.EaseInOutQuart(0.5f), 1e-6f);
            Assert.AreEqual(0.96875f, GsplatUtils.EaseInOutQuart(0.75f), 1e-6f);
            Assert.AreEqual(1.0f, GsplatUtils.EaseInOutQuart(1.0f), 1e-6f);
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

        [Test]
        public void TransformBounds_RotatesAabbAndKeepsTranslation()
        {
            // 目的:
            // - 锁定 `TransformBounds` 的核心几何语义: 它返回的是“变换后的 AABB”,不是简单平移 center.
            // - 这里选 90 度绕 Y 旋转,便于直接验证 x/z extents 交换.
            var sourceBounds = new Bounds(Vector3.zero, new Vector3(2.0f, 4.0f, 6.0f));
            var matrix = Matrix4x4.TRS(new Vector3(10.0f, 1.0f, -2.0f), Quaternion.Euler(0.0f, 90.0f, 0.0f),
                Vector3.one);

            var transformed = GsplatUtils.TransformBounds(sourceBounds, matrix);

            Assert.That(transformed.center.x, Is.EqualTo(10.0f).Within(1.0e-5f));
            Assert.That(transformed.center.y, Is.EqualTo(1.0f).Within(1.0e-5f));
            Assert.That(transformed.center.z, Is.EqualTo(-2.0f).Within(1.0e-5f));
            Assert.That(transformed.size.x, Is.EqualTo(6.0f).Within(1.0e-5f));
            Assert.That(transformed.size.y, Is.EqualTo(4.0f).Within(1.0e-5f));
            Assert.That(transformed.size.z, Is.EqualTo(2.0f).Within(1.0e-5f));
        }

        [Test]
        public void BuildRigidTransformMatrices_IgnoresScaleButPreservesPose()
        {
            // 目的:
            // - 锁定 LiDAR 传感器矩阵的核心语义: 只保留 position + rotation,不吸收节点缩放.
            // - 防止 future refactor 再次把 `localToWorldMatrix/worldToLocalMatrix` 直接塞回 LiDAR 路径.
            var go = new GameObject("GsplatUtilsTests_BuildRigidTransformMatrices");
            try
            {
                var tr = go.transform;
                tr.position = new Vector3(3.0f, -2.0f, 5.0f);
                tr.rotation = Quaternion.Euler(15.0f, 30.0f, -10.0f);
                tr.localScale = new Vector3(2.0f, 3.0f, 4.0f);

                GsplatUtils.BuildRigidTransformMatrices(tr, out var localToWorld, out var worldToLocal);

                var localProbe = new Vector3(0.0f, 0.0f, 5.0f);
                var expectedWorld = tr.position + tr.rotation * localProbe;
                var actualWorld = localToWorld.MultiplyPoint3x4(localProbe);
                var roundTrip = worldToLocal.MultiplyPoint3x4(expectedWorld);

                Assert.That(actualWorld.x, Is.EqualTo(expectedWorld.x).Within(1.0e-5f));
                Assert.That(actualWorld.y, Is.EqualTo(expectedWorld.y).Within(1.0e-5f));
                Assert.That(actualWorld.z, Is.EqualTo(expectedWorld.z).Within(1.0e-5f));

                Assert.That(roundTrip.x, Is.EqualTo(localProbe.x).Within(1.0e-5f));
                Assert.That(roundTrip.y, Is.EqualTo(localProbe.y).Within(1.0e-5f));
                Assert.That(roundTrip.z, Is.EqualTo(localProbe.z).Within(1.0e-5f));

                var scaledWorld = tr.localToWorldMatrix.MultiplyPoint3x4(localProbe);
                Assert.That(Vector3.Distance(scaledWorld, expectedWorld), Is.GreaterThan(1.0f),
                    "对照组应证明直接使用带缩放矩阵会把 LiDAR 世界点位推远,否则这个回归测试就失去意义.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DebugResolveLinearComputeDispatchChunksForInputs_WithinLimit_UsesSingleChunk()
        {
            var chunks = InvokeDebugResolveLinearComputeDispatchChunksForInputs(itemCount: 1024,
                threadsPerGroup: 256,
                maxGroupsPerDispatch: 65535);

            Assert.AreEqual(1, chunks.Length);
            Assert.AreEqual(0, chunks[0].x);
            Assert.AreEqual(4, chunks[0].y);
        }

        [Test]
        public void DebugResolveLinearComputeDispatchChunksForInputs_ExceedingLimit_SplitsIntoMultipleDispatches()
        {
            const int threadsPerGroup = 256;
            const int maxGroupsPerDispatch = 65535;
            var maxItemsPerDispatch = threadsPerGroup * maxGroupsPerDispatch;

            var chunks = InvokeDebugResolveLinearComputeDispatchChunksForInputs(itemCount: maxItemsPerDispatch + 1,
                threadsPerGroup: threadsPerGroup,
                maxGroupsPerDispatch: maxGroupsPerDispatch);

            Assert.AreEqual(2, chunks.Length);
            Assert.AreEqual(0, chunks[0].x);
            Assert.AreEqual(maxGroupsPerDispatch, chunks[0].y);
            Assert.AreEqual(maxItemsPerDispatch, chunks[1].x);
            Assert.AreEqual(1, chunks[1].y);
        }
    }
}
