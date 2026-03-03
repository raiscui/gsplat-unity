// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEngine;

namespace Gsplat.Tests
{
    public sealed class GsplatLidarScanTests
    {
        static void InvokeValidateLidarSerializedFields(object obj, string ownerName)
        {
            // 说明:
            // - ValidateLidarSerializedFields 是 runtime 内部的字段级 clamp(防 NaN/Inf/负数/非法组合).
            // - 单测目标是锁定其语义,避免未来无意改动导致平台差异/黑屏/卡死.
            // - 这里用反射调用,避免测试依赖 Unity 的 OnValidate/PlayerLoop 行为细节.
            var m = obj.GetType().GetMethod("ValidateLidarSerializedFields",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, $"Expected {ownerName}.ValidateLidarSerializedFields to exist.");
            m.Invoke(obj, null);
        }

        static void InvokeSyncLidarColorBlendTargetFromSerializedMode(object obj, string ownerName, bool animated)
        {
            // 说明:
            // - RadarScan 的颜色切换按钮依赖 LiDAR color transition 的状态机推进.
            // - 我们用反射直接调用内部 sync 函数,避免把内部实现暴露成 public API.
            var m = obj.GetType().GetMethod("SyncLidarColorBlendTargetFromSerializedMode",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, $"Expected {ownerName}.SyncLidarColorBlendTargetFromSerializedMode to exist.");
            m.Invoke(obj, new object[] { animated });
        }

        static void SetPrivateField<T>(object obj, string ownerName, string fieldName, T value)
        {
            var f = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, $"Expected private field {ownerName}.{fieldName} to exist.");
            f.SetValue(obj, value);
        }

        static T GetPrivateField<T>(object obj, string ownerName, string fieldName)
        {
            var f = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, $"Expected private field {ownerName}.{fieldName} to exist.");
            return (T)f.GetValue(obj);
        }

        static void SetLidarFieldsToInvalidValues(GsplatRenderer r)
        {
            // 说明: 这组非法值覆盖常见风险:
            // - NaN/Inf
            // - <=0
            // - DepthFar <= DepthNear
            r.EnableLidarScan = true;

            r.LidarRotationHz = float.NaN;
            r.LidarUpdateHz = 0.0f;

            r.LidarAzimuthBins = 1;

            r.LidarUpFovDeg = float.PositiveInfinity;
            r.LidarDownFovDeg = float.NaN;

            r.LidarBeamCount = 0;

            r.LidarDepthNear = 1.0f;
            r.LidarDepthFar = 0.5f;

            r.LidarPointRadiusPixels = float.NegativeInfinity;
            r.LidarShowHideWarpPixels = float.NaN;
            r.LidarShowHideNoiseScale = float.NaN;
            r.LidarShowHideNoiseSpeed = float.PositiveInfinity;
            r.LidarShowHideGlowColor = new Color(float.NaN, 1.0f, 1.0f, 1.0f);
            r.LidarShowGlowIntensity = float.NaN;
            r.LidarHideGlowIntensity = float.PositiveInfinity;
            r.LidarTrailGamma = -1.0f;
            r.LidarIntensity = float.PositiveInfinity;
            r.LidarDepthOpacity = float.NaN;
            r.LidarMinSplatOpacity = float.NaN;
        }

        static void SetLidarFieldsToInvalidValues(GsplatSequenceRenderer r)
        {
            // 说明: 与 GsplatRenderer 的测试输入保持一致,用于锁定两个组件的 clamp 语义不漂移.
            r.EnableLidarScan = true;

            r.LidarRotationHz = float.NaN;
            r.LidarUpdateHz = 0.0f;

            r.LidarAzimuthBins = 1;

            r.LidarUpFovDeg = float.PositiveInfinity;
            r.LidarDownFovDeg = float.NaN;

            r.LidarBeamCount = 0;

            r.LidarDepthNear = 1.0f;
            r.LidarDepthFar = 0.5f;

            r.LidarPointRadiusPixels = float.NegativeInfinity;
            r.LidarShowHideWarpPixels = float.NaN;
            r.LidarShowHideNoiseScale = float.NaN;
            r.LidarShowHideNoiseSpeed = float.PositiveInfinity;
            r.LidarShowHideGlowColor = new Color(float.NaN, 1.0f, 1.0f, 1.0f);
            r.LidarShowGlowIntensity = float.NaN;
            r.LidarHideGlowIntensity = float.PositiveInfinity;
            r.LidarTrailGamma = -1.0f;
            r.LidarIntensity = float.PositiveInfinity;
            r.LidarDepthOpacity = float.NaN;
            r.LidarMinSplatOpacity = float.NaN;
        }

        [Test]
        public void ValidateLidarSerializedFields_ClampsInvalidValues_GsplatRenderer()
        {
            // 注意:
            // - 这里用 FormatterServices 创建“未初始化对象”,避免 MonoBehaviour 被 Unity 生命周期回调触发(OnEnable 创建 GPU 资源).
            // - ValidateLidarSerializedFields 本身只读写字段,不依赖 Unity native handle,因此可安全用这种方式测试.
            var r = (GsplatRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatRenderer));
            SetLidarFieldsToInvalidValues(r);

            InvokeValidateLidarSerializedFields(r, nameof(GsplatRenderer));

            Assert.AreEqual(5.0f, r.LidarRotationHz);
            Assert.AreEqual(10.0f, r.LidarUpdateHz);
            Assert.AreEqual(2048, r.LidarAzimuthBins);

            Assert.AreEqual(10.0f, r.LidarUpFovDeg);
            Assert.AreEqual(-30.0f, r.LidarDownFovDeg);

            Assert.AreEqual(GsplatUtils.k_LidarDefaultBeamCount, r.LidarBeamCount);

            Assert.AreEqual(1.0f, r.LidarDepthNear);
            Assert.AreEqual(2.0f, r.LidarDepthFar);

            Assert.AreEqual(2.0f, r.LidarPointRadiusPixels);
            Assert.AreEqual(6.0f, r.LidarShowHideWarpPixels);
            Assert.AreEqual(-1.0f, r.LidarShowHideNoiseScale);
            Assert.AreEqual(-1.0f, r.LidarShowHideNoiseSpeed);
            Assert.AreEqual(1.0f, r.LidarShowHideGlowColor.r);
            Assert.AreEqual(0.45f, r.LidarShowHideGlowColor.g, 1e-6f);
            Assert.AreEqual(0.1f, r.LidarShowHideGlowColor.b, 1e-6f);
            Assert.AreEqual(1.5f, r.LidarShowGlowIntensity);
            Assert.AreEqual(2.5f, r.LidarHideGlowIntensity);
            Assert.AreEqual(2.0f, r.LidarTrailGamma);
            Assert.AreEqual(1.0f, r.LidarIntensity);
            Assert.AreEqual(1.0f, r.LidarDepthOpacity);
            Assert.AreEqual(1.0f / 255.0f, r.LidarMinSplatOpacity, 1e-6f);
        }

        [Test]
        public void ValidateLidarSerializedFields_ClampsInvalidValues_GsplatSequenceRenderer()
        {
            var r = (GsplatSequenceRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatSequenceRenderer));
            SetLidarFieldsToInvalidValues(r);

            InvokeValidateLidarSerializedFields(r, nameof(GsplatSequenceRenderer));

            Assert.AreEqual(5.0f, r.LidarRotationHz);
            Assert.AreEqual(10.0f, r.LidarUpdateHz);
            Assert.AreEqual(2048, r.LidarAzimuthBins);
            Assert.AreEqual(GsplatUtils.k_LidarDefaultBeamCount, r.LidarBeamCount);
            Assert.AreEqual(2.0f, r.LidarDepthFar);
            Assert.AreEqual(6.0f, r.LidarShowHideWarpPixels);
            Assert.AreEqual(-1.0f, r.LidarShowHideNoiseScale);
            Assert.AreEqual(-1.0f, r.LidarShowHideNoiseSpeed);
            Assert.AreEqual(1.0f, r.LidarShowHideGlowColor.r);
            Assert.AreEqual(0.45f, r.LidarShowHideGlowColor.g, 1e-6f);
            Assert.AreEqual(0.1f, r.LidarShowHideGlowColor.b, 1e-6f);
            Assert.AreEqual(1.5f, r.LidarShowGlowIntensity);
            Assert.AreEqual(2.5f, r.LidarHideGlowIntensity);
            Assert.AreEqual(1.0f, r.LidarDepthOpacity);
            Assert.AreEqual(1.0f / 255.0f, r.LidarMinSplatOpacity, 1e-6f);
        }

        [Test]
        public void ValidateLidarSerializedFields_DoesNotClampWarpPixelsMax_GsplatRenderer()
        {
            // 说明:
            // - 用户需要把 `LidarShowHideWarpPixels` 调到非常大(例如用于夸张效果或调试).
            // - 因此这里锁定语义: 不再对 warpPixels 做最大值 clamp(仅防御 NaN/Inf/负数).
            var r = (GsplatRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatRenderer));
            r.EnableLidarScan = true;
            r.LidarShowHideWarpPixels = 1234.5f;

            InvokeValidateLidarSerializedFields(r, nameof(GsplatRenderer));
            Assert.AreEqual(1234.5f, r.LidarShowHideWarpPixels, 1e-6f);
        }

        [Test]
        public void ValidateLidarSerializedFields_DoesNotClampWarpPixelsMax_GsplatSequenceRenderer()
        {
            var r = (GsplatSequenceRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatSequenceRenderer));
            r.EnableLidarScan = true;
            r.LidarShowHideWarpPixels = 1234.5f;

            InvokeValidateLidarSerializedFields(r, nameof(GsplatSequenceRenderer));
            Assert.AreEqual(1234.5f, r.LidarShowHideWarpPixels, 1e-6f);
        }

        [Test]
        public void ValidateLidarSerializedFields_DoesNotClampAzimuthBinsMax_GsplatRenderer()
        {
            // 说明:
            // - 用户需要把 `LidarAzimuthBins` 调到更高的角分辨率.
            // - 因此这里锁定语义: 不再对 azimuthBins 做最大值 clamp(仅保留最小值防御).
            var r = (GsplatRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatRenderer));
            r.EnableLidarScan = true;
            r.LidarAzimuthBins = 16384;

            InvokeValidateLidarSerializedFields(r, nameof(GsplatRenderer));
            Assert.AreEqual(16384, r.LidarAzimuthBins);
        }

        [Test]
        public void ValidateLidarSerializedFields_DoesNotClampAzimuthBinsMax_GsplatSequenceRenderer()
        {
            var r = (GsplatSequenceRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatSequenceRenderer));
            r.EnableLidarScan = true;
            r.LidarAzimuthBins = 16384;

            InvokeValidateLidarSerializedFields(r, nameof(GsplatSequenceRenderer));
            Assert.AreEqual(16384, r.LidarAzimuthBins);
        }

        [Test]
        public void SyncLidarColorBlendTargetFromSerializedMode_DoesNotRestartWhenAnimating_GsplatRenderer()
        {
            // 说明:
            // - 之前的 bug: Update/OnValidate 每帧调用 sync 时,在 m_lidarColorAnimating=true 的情况下会反复 Begin,
            //   使 progress 被重置为 0,导致 Inspector 的颜色切换按钮看起来“按了但不动”.
            // - 这里锁定语义: 当已经在向同一 target 动画时,sync 不应重启动画.
            var r = (GsplatRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatRenderer));
            r.EnableLidarScan = true;
            r.LidarColorMode = GsplatLidarColorMode.SplatColorSH0;

            SetPrivateField(r, nameof(GsplatRenderer), "m_lidarColorAnimating", true);
            SetPrivateField(r, nameof(GsplatRenderer), "m_lidarColorAnimTargetBlend01", 1.0f);
            SetPrivateField(r, nameof(GsplatRenderer), "m_lidarColorAnimProgress01", 0.5f);
            SetPrivateField(r, nameof(GsplatRenderer), "m_lidarColorBlend01", 0.25f);

            InvokeSyncLidarColorBlendTargetFromSerializedMode(r, nameof(GsplatRenderer), animated: true);

            var p = GetPrivateField<float>(r, nameof(GsplatRenderer), "m_lidarColorAnimProgress01");
            Assert.AreEqual(0.5f, p, 1e-6f);
        }

        [Test]
        public void SyncLidarColorBlendTargetFromSerializedMode_DoesNotRestartWhenAnimating_GsplatSequenceRenderer()
        {
            var r = (GsplatSequenceRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatSequenceRenderer));
            r.EnableLidarScan = true;
            r.LidarColorMode = GsplatLidarColorMode.SplatColorSH0;

            SetPrivateField(r, nameof(GsplatSequenceRenderer), "m_lidarColorAnimating", true);
            SetPrivateField(r, nameof(GsplatSequenceRenderer), "m_lidarColorAnimTargetBlend01", 1.0f);
            SetPrivateField(r, nameof(GsplatSequenceRenderer), "m_lidarColorAnimProgress01", 0.5f);
            SetPrivateField(r, nameof(GsplatSequenceRenderer), "m_lidarColorBlend01", 0.25f);

            InvokeSyncLidarColorBlendTargetFromSerializedMode(r, nameof(GsplatSequenceRenderer), animated: true);

            var p = GetPrivateField<float>(r, nameof(GsplatSequenceRenderer), "m_lidarColorAnimProgress01");
            Assert.AreEqual(0.5f, p, 1e-6f);
        }

        [Test]
        public void IsRangeImageUpdateDue_RespectsUpdateHzGate()
        {
            // 说明:
            // - UpdateHz 门禁应为纯逻辑,不依赖 GPU,因此可以在 EditMode tests 中稳定验证.
            // - 这里通过反射创建 internal 类型 `GsplatLidarScan`,避免为了测试而扩大 public API.
            var asm = typeof(GsplatRenderer).Assembly;
            var scanType = asm.GetType("Gsplat.GsplatLidarScan");
            Assert.IsNotNull(scanType, "Expected internal type Gsplat.GsplatLidarScan to exist in assembly 'Gsplat'.");

            var scan = Activator.CreateInstance(scanType, nonPublic: true);
            Assert.IsNotNull(scan, "Failed to create instance of Gsplat.GsplatLidarScan via reflection.");

            var isDue = scanType.GetMethod("IsRangeImageUpdateDue", BindingFlags.Instance | BindingFlags.Public);
            var mark = scanType.GetMethod("MarkRangeImageUpdated", BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(isDue, "Expected public method IsRangeImageUpdateDue to exist on GsplatLidarScan.");
            Assert.IsNotNull(mark, "Expected public method MarkRangeImageUpdated to exist on GsplatLidarScan.");

            // 初始(last=-1)一定 due.
            Assert.IsTrue((bool)isDue.Invoke(scan, new object[] { 10.0, 10.0f }));

            // 更新一次后,在 interval(0.1s) 内不 due.
            mark.Invoke(scan, new object[] { 10.0 });
            Assert.IsFalse((bool)isDue.Invoke(scan, new object[] { 10.05, 10.0f }));
            Assert.IsTrue((bool)isDue.Invoke(scan, new object[] { 10.1001, 10.0f }));

            // updateHz 非法时回退到 10Hz(0.1s).
            mark.Invoke(scan, new object[] { 20.0 });
            Assert.IsFalse((bool)isDue.Invoke(scan, new object[] { 20.05, 0.0f }));
            Assert.IsTrue((bool)isDue.Invoke(scan, new object[] { 20.11, 0.0f }));

            // now 回退时视为 due(例如域重载/时间基重置).
            mark.Invoke(scan, new object[] { 30.0 });
            Assert.IsTrue((bool)isDue.Invoke(scan, new object[] { 29.0, 10.0f }));
        }
    }
}
