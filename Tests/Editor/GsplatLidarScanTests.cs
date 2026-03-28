// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat.Tests
{
    public sealed class GsplatLidarScanTests
    {
        static Type FindLoadedType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        static object GetEnumValue(Type enumType, string name, string ownerName)
        {
            Assert.IsNotNull(enumType, $"Expected reflected enum {ownerName} to exist.");
            return Enum.Parse(enumType, name);
        }

        static object GetReflectedMemberValue(object obj, string ownerName, string memberName)
        {
            Assert.IsNotNull(obj, $"Expected reflected owner {ownerName} to be non-null.");

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var field = obj.GetType().GetField(memberName, flags);
            if (field != null)
            {
                return field.GetValue(obj);
            }

            var property = obj.GetType().GetProperty(memberName, flags);
            Assert.IsNotNull(property, $"Expected reflected member {ownerName}.{memberName} to exist.");
            return property.GetValue(obj);
        }

        static T GetReflectedMemberValue<T>(object obj, string ownerName, string memberName)
        {
            return (T)GetReflectedMemberValue(obj, ownerName, memberName);
        }

        static void SetReflectedMemberValue(object obj, string ownerName, string memberName, object value)
        {
            Assert.IsNotNull(obj, $"Expected reflected owner {ownerName} to be non-null.");

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var field = obj.GetType().GetField(memberName, flags);
            if (field != null)
            {
                field.SetValue(obj, value);
                return;
            }

            var property = obj.GetType().GetProperty(memberName, flags);
            Assert.IsNotNull(property, $"Expected reflected member {ownerName}.{memberName} to exist.");
            property.SetValue(obj, value);
        }

        static object InvokeReflectedMethod(object obj, string ownerName, string methodName, params object[] args)
        {
            Assert.IsNotNull(obj, $"Expected reflected owner {ownerName} to be non-null.");

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var method = obj.GetType().GetMethod(methodName, flags);
            Assert.IsNotNull(method, $"Expected reflected method {ownerName}.{methodName} to exist.");
            return method.Invoke(obj, args);
        }

        static Component AddComponentByType(GameObject gameObject, Type componentType, string ownerName)
        {
            Assert.IsNotNull(componentType, $"Expected reflected component type {ownerName} to exist.");

            var component = gameObject.AddComponent(componentType);
            Assert.IsNotNull(component, $"Expected GameObject.AddComponent({ownerName}) to succeed.");
            return component;
        }

        static void SetReflectedBitArrayValue(object bitArray, string ownerName, uint index, bool value)
        {
            Assert.IsNotNull(bitArray, $"Expected reflected bit array {ownerName} to be non-null.");

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            var bitArrayType = bitArray.GetType();
            var indexer =
                bitArrayType.GetProperty("Item", flags, null, typeof(bool), new[] { typeof(uint) }, null) ??
                bitArrayType.GetProperty("Item", flags, null, typeof(bool), new[] { typeof(int) }, null);
            Assert.IsNotNull(indexer, $"Expected reflected bit array indexer {ownerName}.Item to exist.");

            var parameterType = indexer.GetIndexParameters()[0].ParameterType;
            var boxedIndex = parameterType == typeof(uint)
                ? (object)index
                : Convert.ChangeType(index, parameterType);
            indexer.SetValue(bitArray, value, new[] { boxedIndex });
        }

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

        static float InvokeResolveRadarScanVisibilityDurationSeconds(object obj, string ownerName, bool enableRadarScan,
            float durationSeconds)
        {
            // 说明:
            // - ResolveRadarScanVisibilityDurationSeconds 是 RadarScan 开/关淡入淡出时长的决策逻辑.
            // - 我们用反射锁定其语义,避免未来又把 LiDAR 的时长重新绑回 RenderStyleSwitchDurationSeconds.
            var m = obj.GetType().GetMethod("ResolveRadarScanVisibilityDurationSeconds",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, $"Expected {ownerName}.ResolveRadarScanVisibilityDurationSeconds to exist.");
            return (float)m.Invoke(obj, new object[] { enableRadarScan, durationSeconds });
        }

        static float InvokeResolveLidarUnscannedIntensityForShader(object obj, string ownerName)
        {
            // 说明:
            // - ResolveLidarUnscannedIntensityForShader 是 LiDAR 点云“底色强度”的决策逻辑.
            // - 它把 Keep 开关与 UnscannedIntensity 合并成最终下发到 shader 的数值.
            var m = obj.GetType().GetMethod("ResolveLidarUnscannedIntensityForShader",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, $"Expected {ownerName}.ResolveLidarUnscannedIntensityForShader to exist.");
            return (float)m.Invoke(obj, null);
        }

        static Bounds InvokeResolveVisibilityLocalBoundsForThisFrame(object obj, string ownerName)
        {
            // 说明:
            // - 该函数是“gsplat bounds + external target bounds”联合逻辑的唯一入口.
            // - 用反射锁住它,可以避免未来有人只改了 LiDAR overlay,却漏改 splat visibility 的 bounds.
            var m = obj.GetType().GetMethod("ResolveVisibilityLocalBoundsForThisFrame",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, $"Expected {ownerName}.ResolveVisibilityLocalBoundsForThisFrame to exist.");
            return (Bounds)m.Invoke(obj, null);
        }

        static Transform InvokeResolveConfiguredLidarSensorTransformOrNull(object obj, string ownerName)
        {
            var m = obj.GetType().GetMethod("ResolveConfiguredLidarSensorTransformOrNull",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, $"Expected {ownerName}.ResolveConfiguredLidarSensorTransformOrNull to exist.");
            return (Transform)m.Invoke(obj, null);
        }

        static object InvokeTryGetEffectiveLidarLayout(object obj, string ownerName, bool logWhenMissing,
            out bool succeeded, out Camera resolvedFrustumCamera)
        {
            var m = obj.GetType().GetMethod("TryGetEffectiveLidarLayout",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, $"Expected {ownerName}.TryGetEffectiveLidarLayout to exist.");

            var args = new object[] { null, null, logWhenMissing };
            succeeded = (bool)m.Invoke(obj, args);
            resolvedFrustumCamera = args[1] as Camera;
            return args[0];
        }

        static object InvokeTryGetEffectiveLidarRuntimeContext(object obj, string ownerName, bool logWhenMissing,
            out bool succeeded, out Matrix4x4 lidarLocalToWorld, out Matrix4x4 worldToLidar,
            out Camera resolvedFrustumCamera)
        {
            var m = obj.GetType().GetMethod("TryGetEffectiveLidarRuntimeContext",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, $"Expected {ownerName}.TryGetEffectiveLidarRuntimeContext to exist.");

            var args = new object[] { null, Matrix4x4.identity, Matrix4x4.identity, null, logWhenMissing };
            succeeded = (bool)m.Invoke(obj, args);
            lidarLocalToWorld = (Matrix4x4)args[1];
            worldToLidar = (Matrix4x4)args[2];
            resolvedFrustumCamera = args[3] as Camera;
            return args[0];
        }

        static void AssertMatrixReconstructsRigidWorldDistance(Matrix4x4 lidarLocalToWorld, Matrix4x4 worldToLidar,
            Transform sensorTransform, float rangeMeters)
        {
            var localPoint = Vector3.forward * rangeMeters;
            var expectedWorldPoint = sensorTransform.position + sensorTransform.rotation * localPoint;
            var actualWorldPoint = lidarLocalToWorld.MultiplyPoint3x4(localPoint);
            var recoveredLocalPoint = worldToLidar.MultiplyPoint3x4(expectedWorldPoint);

            Assert.That(actualWorldPoint.x, Is.EqualTo(expectedWorldPoint.x).Within(1.0e-5f));
            Assert.That(actualWorldPoint.y, Is.EqualTo(expectedWorldPoint.y).Within(1.0e-5f));
            Assert.That(actualWorldPoint.z, Is.EqualTo(expectedWorldPoint.z).Within(1.0e-5f));

            Assert.That(recoveredLocalPoint.x, Is.EqualTo(localPoint.x).Within(1.0e-5f));
            Assert.That(recoveredLocalPoint.y, Is.EqualTo(localPoint.y).Within(1.0e-5f));
            Assert.That(recoveredLocalPoint.z, Is.EqualTo(localPoint.z).Within(1.0e-5f));
        }

        static T GetReflectedPropertyValue<T>(object obj, string ownerName, string propertyName)
        {
            Assert.IsNotNull(obj, $"Expected {ownerName} reflected object to be non-null.");
            var p = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(p, $"Expected reflected property {ownerName}.{propertyName} to exist.");
            return (T)p.GetValue(obj);
        }

        static object InvokeCreateSurround360Layout(int azimuthBins, int beamCount, float upFovDeg, float downFovDeg)
        {
            var asm = typeof(GsplatRenderer).Assembly;
            var layoutType = asm.GetType("Gsplat.GsplatLidarLayout");
            Assert.IsNotNull(layoutType, "Expected internal type Gsplat.GsplatLidarLayout to exist.");

            var m = layoutType.GetMethod("CreateSurround360", BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(m, "Expected static factory GsplatLidarLayout.CreateSurround360 to exist.");
            return m.Invoke(null, new object[] { azimuthBins, beamCount, upFovDeg, downFovDeg });
        }

        static int ComputeExpectedFrustumActiveAzimuthBins(int baseAzimuthBins, Camera camera)
        {
            var verticalFovRad = camera.fieldOfView * Mathf.Deg2Rad;
            var horizontalFovRad = 2.0f * Mathf.Atan(Mathf.Tan(verticalFovRad * 0.5f) * camera.aspect);
            return Mathf.Max(1, Mathf.RoundToInt(baseAzimuthBins * horizontalFovRad / (Mathf.PI * 2.0f)));
        }

        static int ComputeExpectedFrustumActiveBeamCount(int baseBeamCount, float baselineUpFovDeg,
            float baselineDownFovDeg, Camera camera)
        {
            var baselineVerticalSpanRad = Mathf.Max((baselineUpFovDeg - baselineDownFovDeg) * Mathf.Deg2Rad, 1.0e-6f);
            var verticalFovRad = camera.fieldOfView * Mathf.Deg2Rad;
            return Mathf.Max(1, Mathf.RoundToInt(baseBeamCount * verticalFovRad / baselineVerticalSpanRad));
        }

        static void ComputeFrustumAngleBoundsFromViewportSamples(Camera camera, Matrix4x4 worldToSensor,
            out float azimuthMinRad, out float azimuthMaxRad,
            out float beamMinRad, out float beamMaxRad)
        {
            azimuthMinRad = float.PositiveInfinity;
            azimuthMaxRad = float.NegativeInfinity;
            beamMinRad = float.PositiveInfinity;
            beamMaxRad = float.NegativeInfinity;

            var viewportSamples = new[]
            {
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 0.0f),
                new Vector2(0.0f, 1.0f),
                new Vector2(1.0f, 1.0f),
                new Vector2(0.5f, 0.0f),
                new Vector2(0.5f, 1.0f),
                new Vector2(0.0f, 0.5f),
                new Vector2(1.0f, 0.5f),
            };

            for (var i = 0; i < viewportSamples.Length; i++)
            {
                var sample = viewportSamples[i];
                var worldPoint = camera.ViewportToWorldPoint(new Vector3(sample.x, sample.y, 1.0f));
                var localDirection = worldToSensor.MultiplyPoint3x4(worldPoint);
                var direction = localDirection.normalized;
                var horizontal = Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z);
                var azimuthRad = Mathf.Atan2(direction.x, direction.z);
                var beamRad = Mathf.Atan2(direction.y, Mathf.Max(horizontal, 1.0e-6f));

                azimuthMinRad = Mathf.Min(azimuthMinRad, azimuthRad);
                azimuthMaxRad = Mathf.Max(azimuthMaxRad, azimuthRad);
                beamMinRad = Mathf.Min(beamMinRad, beamRad);
                beamMaxRad = Mathf.Max(beamMaxRad, beamRad);
            }
        }

        static bool InvokeShouldForceSourceRendererOff(GsplatLidarExternalTargetVisibilityMode visibilityMode, bool isPlaying)
        {
            var asm = typeof(GsplatRenderer).Assembly;
            var helperType = asm.GetType("Gsplat.GsplatLidarExternalTargetHelper");
            Assert.IsNotNull(helperType, "Expected internal type Gsplat.GsplatLidarExternalTargetHelper to exist.");

            var m = helperType.GetMethod("ShouldForceSourceRendererOff",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(m, "Expected private static helper visibility decision method to exist.");
            return (bool)m.Invoke(null, new object[] { visibilityMode, isPlaying });
        }

        static void SetLidarFieldsToInvalidValues(GsplatRenderer r)
        {
            // 说明: 这组非法值覆盖常见风险:
            // - NaN/Inf
            // - <=0
            // - DepthFar <= DepthNear
            r.EnableLidarScan = true;
            r.LidarApertureMode = (GsplatLidarApertureMode)123;
            r.LidarExternalDynamicUpdateHz = 0.0f;
            r.LidarExternalEdgeAwareResolveMode = (GsplatLidarExternalEdgeAwareResolveMode)123;
            r.LidarExternalSubpixelResolveMode = (GsplatLidarExternalSubpixelResolveMode)123;

            r.LidarRotationHz = float.NaN;
            r.LidarUpdateHz = 0.0f;

            r.LidarAzimuthBins = 1;

            r.LidarUpFovDeg = float.PositiveInfinity;
            r.LidarDownFovDeg = float.NaN;

            r.LidarBeamCount = 0;

            r.LidarDepthNear = 1.0f;
            r.LidarDepthFar = 0.5f;

            r.LidarPointRadiusPixels = float.NegativeInfinity;
            r.LidarPointJitterCellFraction = float.NaN;
            r.LidarParticleAntialiasingMode = (GsplatLidarParticleAntialiasingMode)123;
            r.LidarParticleAAFringePixels = float.NaN;
            r.LidarExternalHitBiasMeters = float.PositiveInfinity;
            r.LidarShowDuration = float.NaN;
            r.LidarHideDuration = float.PositiveInfinity;
            r.LidarShowHideWarpPixels = float.NaN;
            r.LidarShowHideNoiseScale = float.NaN;
            r.LidarShowHideNoiseSpeed = float.PositiveInfinity;
            r.LidarShowHideGlowColor = new Color(float.NaN, 1.0f, 1.0f, 1.0f);
            r.LidarDepthNearColor = new Color(float.NaN, 1.0f, 1.0f, 1.0f);
            r.LidarDepthFarColor = new Color(1.0f, float.PositiveInfinity, 0.0f, 1.0f);
            r.LidarShowGlowIntensity = float.NaN;
            r.LidarHideGlowIntensity = float.PositiveInfinity;
            r.LidarTrailGamma = -1.0f;
            r.LidarIntensity = float.PositiveInfinity;
            r.LidarKeepUnscannedPoints = true;
            r.LidarUnscannedIntensity = float.NaN;
            r.LidarIntensityDistanceDecayMode = (GsplatLidarDistanceDecayMode)123;
            r.LidarIntensityDistanceDecay = float.NaN;
            r.LidarUnscannedIntensityDistanceDecay = float.PositiveInfinity;
            r.LidarDepthOpacity = float.NaN;
            r.LidarMinSplatOpacity = float.NaN;
            r.LidarExternalTargetVisibilityMode = (GsplatLidarExternalTargetVisibilityMode)123;
            r.LidarExternalStaticTargets = null;
            r.LidarExternalDynamicTargets = null;
        }

        static void SetLidarFieldsToInvalidValues(GsplatSequenceRenderer r)
        {
            // 说明: 与 GsplatRenderer 的测试输入保持一致,用于锁定两个组件的 clamp 语义不漂移.
            r.EnableLidarScan = true;
            r.LidarApertureMode = (GsplatLidarApertureMode)123;
            r.LidarExternalDynamicUpdateHz = 0.0f;
            r.LidarExternalEdgeAwareResolveMode = (GsplatLidarExternalEdgeAwareResolveMode)123;
            r.LidarExternalSubpixelResolveMode = (GsplatLidarExternalSubpixelResolveMode)123;

            r.LidarRotationHz = float.NaN;
            r.LidarUpdateHz = 0.0f;

            r.LidarAzimuthBins = 1;

            r.LidarUpFovDeg = float.PositiveInfinity;
            r.LidarDownFovDeg = float.NaN;

            r.LidarBeamCount = 0;

            r.LidarDepthNear = 1.0f;
            r.LidarDepthFar = 0.5f;

            r.LidarPointRadiusPixels = float.NegativeInfinity;
            r.LidarPointJitterCellFraction = float.NaN;
            r.LidarParticleAntialiasingMode = (GsplatLidarParticleAntialiasingMode)123;
            r.LidarParticleAAFringePixels = float.NaN;
            r.LidarExternalHitBiasMeters = float.PositiveInfinity;
            r.LidarShowDuration = float.NaN;
            r.LidarHideDuration = float.PositiveInfinity;
            r.LidarShowHideWarpPixels = float.NaN;
            r.LidarShowHideNoiseScale = float.NaN;
            r.LidarShowHideNoiseSpeed = float.PositiveInfinity;
            r.LidarShowHideGlowColor = new Color(float.NaN, 1.0f, 1.0f, 1.0f);
            r.LidarDepthNearColor = new Color(float.NaN, 1.0f, 1.0f, 1.0f);
            r.LidarDepthFarColor = new Color(1.0f, float.PositiveInfinity, 0.0f, 1.0f);
            r.LidarShowGlowIntensity = float.NaN;
            r.LidarHideGlowIntensity = float.PositiveInfinity;
            r.LidarTrailGamma = -1.0f;
            r.LidarIntensity = float.PositiveInfinity;
            r.LidarKeepUnscannedPoints = true;
            r.LidarUnscannedIntensity = float.NaN;
            r.LidarIntensityDistanceDecayMode = (GsplatLidarDistanceDecayMode)123;
            r.LidarIntensityDistanceDecay = float.NaN;
            r.LidarUnscannedIntensityDistanceDecay = float.PositiveInfinity;
            r.LidarDepthOpacity = float.NaN;
            r.LidarMinSplatOpacity = float.NaN;
            r.LidarExternalTargetVisibilityMode = (GsplatLidarExternalTargetVisibilityMode)123;
            r.LidarExternalStaticTargets = null;
            r.LidarExternalDynamicTargets = null;
        }

        [Test]
        public void NewGsplatRenderer_DefaultsLidarApertureModeToSurround360()
        {
            var go = new GameObject("GsplatRenderer_DefaultApertureMode");
            try
            {
                go.SetActive(false);
                var renderer = go.AddComponent<GsplatRenderer>();
                Assert.AreEqual(GsplatLidarApertureMode.Surround360, renderer.LidarApertureMode);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NewGsplatSequenceRenderer_DefaultsLidarApertureModeToSurround360()
        {
            var go = new GameObject("GsplatSequenceRenderer_DefaultApertureMode");
            try
            {
                go.SetActive(false);
                var renderer = go.AddComponent<GsplatSequenceRenderer>();
                Assert.AreEqual(GsplatLidarApertureMode.Surround360, renderer.LidarApertureMode);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NewGsplatRenderer_DefaultsLidarEnableScanMotionToTrue()
        {
            var go = new GameObject("GsplatRenderer_DefaultScanMotion");
            try
            {
                go.SetActive(false);
                var renderer = go.AddComponent<GsplatRenderer>();
                Assert.IsTrue(renderer.LidarEnableScanMotion);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NewGsplatSequenceRenderer_DefaultsLidarEnableScanMotionToTrue()
        {
            var go = new GameObject("GsplatSequenceRenderer_DefaultScanMotion");
            try
            {
                go.SetActive(false);
                var renderer = go.AddComponent<GsplatSequenceRenderer>();
                Assert.IsTrue(renderer.LidarEnableScanMotion);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NewGsplatRenderer_DefaultsExternalTargetVisibilityModeToForceRenderingOff()
        {
            var go = new GameObject("GsplatRenderer_DefaultVisibilityMode");
            try
            {
                go.SetActive(false);
                var renderer = go.AddComponent<GsplatRenderer>();
                Assert.AreEqual(GsplatLidarExternalTargetVisibilityMode.ForceRenderingOff,
                    renderer.LidarExternalTargetVisibilityMode);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NewGsplatSequenceRenderer_DefaultsExternalTargetVisibilityModeToForceRenderingOff()
        {
            var go = new GameObject("GsplatSequenceRenderer_DefaultVisibilityMode");
            try
            {
                go.SetActive(false);
                var renderer = go.AddComponent<GsplatSequenceRenderer>();
                Assert.AreEqual(GsplatLidarExternalTargetVisibilityMode.ForceRenderingOff,
                    renderer.LidarExternalTargetVisibilityMode);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NewGsplatRenderer_DefaultsHybridResolveModesToOff()
        {
            var go = new GameObject("GsplatRenderer_DefaultHybridResolveModes");
            try
            {
                go.SetActive(false);
                var renderer = go.AddComponent<GsplatRenderer>();
                Assert.AreEqual(GsplatLidarExternalEdgeAwareResolveMode.Off, renderer.LidarExternalEdgeAwareResolveMode);
                Assert.AreEqual(GsplatLidarExternalSubpixelResolveMode.Off, renderer.LidarExternalSubpixelResolveMode);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NewGsplatSequenceRenderer_DefaultsHybridResolveModesToOff()
        {
            var go = new GameObject("GsplatSequenceRenderer_DefaultHybridResolveModes");
            try
            {
                go.SetActive(false);
                var renderer = go.AddComponent<GsplatSequenceRenderer>();
                Assert.AreEqual(GsplatLidarExternalEdgeAwareResolveMode.Off, renderer.LidarExternalEdgeAwareResolveMode);
                Assert.AreEqual(GsplatLidarExternalSubpixelResolveMode.Off, renderer.LidarExternalSubpixelResolveMode);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LegacyExternalTargetsProperty_MapsToStaticTargets_GsplatRenderer()
        {
            var go = new GameObject("GsplatRenderer_LegacyExternalTargetsProperty");
            var a = new GameObject("legacy-static-a");
            var b = new GameObject("legacy-static-b");
            try
            {
                go.SetActive(false);
                var renderer = go.AddComponent<GsplatRenderer>();
                typeof(GsplatRenderer).GetProperty("LidarExternalTargets")!
                    .SetValue(renderer, new[] { a, b });

                CollectionAssert.AreEqual(new[] { a, b }, renderer.LidarExternalStaticTargets);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void LegacyExternalTargetsProperty_MapsToStaticTargets_GsplatSequenceRenderer()
        {
            var go = new GameObject("GsplatSequenceRenderer_LegacyExternalTargetsProperty");
            var a = new GameObject("legacy-seq-static-a");
            var b = new GameObject("legacy-seq-static-b");
            try
            {
                go.SetActive(false);
                var renderer = go.AddComponent<GsplatSequenceRenderer>();
                typeof(GsplatSequenceRenderer).GetProperty("LidarExternalTargets")!
                    .SetValue(renderer, new[] { a, b });

                CollectionAssert.AreEqual(new[] { a, b }, renderer.LidarExternalStaticTargets);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void ExternalTargetVisibilityMode_PlayModeOnly_OnlyForcesOffWhenPlaying()
        {
            Assert.IsFalse(InvokeShouldForceSourceRendererOff(
                GsplatLidarExternalTargetVisibilityMode.KeepVisible, isPlaying: false));
            Assert.IsFalse(InvokeShouldForceSourceRendererOff(
                GsplatLidarExternalTargetVisibilityMode.KeepVisible, isPlaying: true));

            Assert.IsTrue(InvokeShouldForceSourceRendererOff(
                GsplatLidarExternalTargetVisibilityMode.ForceRenderingOff, isPlaying: false));
            Assert.IsTrue(InvokeShouldForceSourceRendererOff(
                GsplatLidarExternalTargetVisibilityMode.ForceRenderingOff, isPlaying: true));

            Assert.IsFalse(InvokeShouldForceSourceRendererOff(
                GsplatLidarExternalTargetVisibilityMode.ForceRenderingOffInPlayMode, isPlaying: false));
            Assert.IsTrue(InvokeShouldForceSourceRendererOff(
                GsplatLidarExternalTargetVisibilityMode.ForceRenderingOffInPlayMode, isPlaying: true));
        }

        [Test]
        public void ResolveConfiguredLidarSensorTransformOrNull_UsesFrustumCameraInFrustumMode_GsplatRenderer()
        {
            var host = new GameObject("GsplatRenderer_FrustumSensorPose");
            var origin = new GameObject("lidar-origin");
            var cameraGo = new GameObject("lidar-frustum-camera");
            try
            {
                host.SetActive(false);
                var camera = cameraGo.AddComponent<Camera>();
                var renderer = host.AddComponent<GsplatRenderer>();

                renderer.LidarOrigin = origin.transform;
                renderer.LidarFrustumCamera = camera;
                renderer.LidarApertureMode = GsplatLidarApertureMode.CameraFrustum;

                Assert.AreSame(camera.transform,
                    InvokeResolveConfiguredLidarSensorTransformOrNull(renderer, nameof(GsplatRenderer)));

                renderer.LidarApertureMode = GsplatLidarApertureMode.Surround360;
                Assert.AreSame(origin.transform,
                    InvokeResolveConfiguredLidarSensorTransformOrNull(renderer, nameof(GsplatRenderer)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(origin);
                UnityEngine.Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void ResolveConfiguredLidarSensorTransformOrNull_UsesFrustumCameraInFrustumMode_GsplatSequenceRenderer()
        {
            var host = new GameObject("GsplatSequenceRenderer_FrustumSensorPose");
            var origin = new GameObject("lidar-seq-origin");
            var cameraGo = new GameObject("lidar-seq-frustum-camera");
            try
            {
                host.SetActive(false);
                var camera = cameraGo.AddComponent<Camera>();
                var renderer = host.AddComponent<GsplatSequenceRenderer>();

                renderer.LidarOrigin = origin.transform;
                renderer.LidarFrustumCamera = camera;
                renderer.LidarApertureMode = GsplatLidarApertureMode.CameraFrustum;

                Assert.AreSame(camera.transform,
                    InvokeResolveConfiguredLidarSensorTransformOrNull(renderer, nameof(GsplatSequenceRenderer)));

                renderer.LidarApertureMode = GsplatLidarApertureMode.Surround360;
                Assert.AreSame(origin.transform,
                    InvokeResolveConfiguredLidarSensorTransformOrNull(renderer, nameof(GsplatSequenceRenderer)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(origin);
                UnityEngine.Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void TryGetEffectiveLidarRuntimeContext_IgnoresSensorScale_GsplatRenderer()
        {
            var host = new GameObject("GsplatRenderer_ScaledLidarSensor");
            var origin = new GameObject("lidar-origin-scaled");
            try
            {
                host.SetActive(false);
                host.transform.localScale = new Vector3(2.0f, 2.0f, 2.0f);

                origin.transform.SetParent(host.transform, false);
                origin.transform.localPosition = new Vector3(1.5f, -0.5f, 2.0f);
                origin.transform.localRotation = Quaternion.Euler(0.0f, 35.0f, 0.0f);
                origin.transform.localScale = Vector3.one;

                var renderer = host.AddComponent<GsplatRenderer>();
                renderer.EnableLidarScan = true;
                renderer.LidarApertureMode = GsplatLidarApertureMode.Surround360;
                renderer.LidarOrigin = origin.transform;

                InvokeTryGetEffectiveLidarRuntimeContext(renderer, nameof(GsplatRenderer), logWhenMissing: false,
                    out var succeeded, out var lidarLocalToWorld, out var worldToLidar, out var resolvedCamera);

                Assert.IsTrue(succeeded);
                Assert.IsNull(resolvedCamera);
                AssertMatrixReconstructsRigidWorldDistance(lidarLocalToWorld, worldToLidar, origin.transform, 6.0f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(origin);
            }
        }

        [Test]
        public void TryGetEffectiveLidarRuntimeContext_IgnoresSensorScale_GsplatSequenceRenderer()
        {
            var host = new GameObject("GsplatSequenceRenderer_ScaledLidarSensor");
            var origin = new GameObject("lidar-seq-origin-scaled");
            try
            {
                host.SetActive(false);
                host.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

                origin.transform.SetParent(host.transform, false);
                origin.transform.localPosition = new Vector3(-2.0f, 1.0f, 3.0f);
                origin.transform.localRotation = Quaternion.Euler(10.0f, -20.0f, 5.0f);
                origin.transform.localScale = Vector3.one;

                var renderer = host.AddComponent<GsplatSequenceRenderer>();
                renderer.EnableLidarScan = true;
                renderer.LidarApertureMode = GsplatLidarApertureMode.Surround360;
                renderer.LidarOrigin = origin.transform;

                InvokeTryGetEffectiveLidarRuntimeContext(renderer, nameof(GsplatSequenceRenderer), logWhenMissing: false,
                    out var succeeded, out var lidarLocalToWorld, out var worldToLidar, out var resolvedCamera);

                Assert.IsTrue(succeeded);
                Assert.IsNull(resolvedCamera);
                AssertMatrixReconstructsRigidWorldDistance(lidarLocalToWorld, worldToLidar, origin.transform, 4.0f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(origin);
            }
        }

        [Test]
        public void TryGetEffectiveLidarLayout_UsesFrustumDensityDerivedCounts_GsplatRenderer()
        {
            var host = new GameObject("GsplatRenderer_FrustumLayout");
            var cameraGo = new GameObject("lidar-layout-camera");
            try
            {
                host.SetActive(false);
                var renderer = host.AddComponent<GsplatRenderer>();
                var camera = cameraGo.AddComponent<Camera>();
                camera.aspect = 16.0f / 9.0f;
                camera.fieldOfView = 60.0f;

                renderer.LidarApertureMode = GsplatLidarApertureMode.CameraFrustum;
                renderer.LidarFrustumCamera = camera;
                renderer.LidarAzimuthBins = 2048;
                renderer.LidarBeamCount = 128;
                renderer.LidarUpFovDeg = 10.0f;
                renderer.LidarDownFovDeg = -30.0f;

                var layout = InvokeTryGetEffectiveLidarLayout(renderer, nameof(GsplatRenderer), logWhenMissing: false,
                    out var succeeded, out var resolvedCamera);

                Assert.IsTrue(succeeded);
                Assert.AreSame(camera, resolvedCamera);

                var activeAzimuthBins = GetReflectedPropertyValue<int>(layout, "GsplatLidarLayout", "ActiveAzimuthBins");
                var activeBeamCount = GetReflectedPropertyValue<int>(layout, "GsplatLidarLayout", "ActiveBeamCount");
                var cellCount = GetReflectedPropertyValue<int>(layout, "GsplatLidarLayout", "CellCount");

                Assert.AreEqual(ComputeExpectedFrustumActiveAzimuthBins(renderer.LidarAzimuthBins, camera),
                    activeAzimuthBins);
                Assert.AreEqual(ComputeExpectedFrustumActiveBeamCount(
                        renderer.LidarBeamCount,
                        renderer.LidarUpFovDeg,
                        renderer.LidarDownFovDeg,
                        camera),
                    activeBeamCount);
                Assert.AreEqual(activeAzimuthBins * activeBeamCount, cellCount);
                Assert.Less(activeAzimuthBins, renderer.LidarAzimuthBins);
                Assert.Greater(activeBeamCount, renderer.LidarBeamCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void TryGetEffectiveLidarLayout_UsesFrustumDensityDerivedCounts_GsplatSequenceRenderer()
        {
            var host = new GameObject("GsplatSequenceRenderer_FrustumLayout");
            var cameraGo = new GameObject("lidar-seq-layout-camera");
            try
            {
                host.SetActive(false);
                var renderer = host.AddComponent<GsplatSequenceRenderer>();
                var camera = cameraGo.AddComponent<Camera>();
                camera.aspect = 4.0f / 3.0f;
                camera.fieldOfView = 40.0f;

                renderer.LidarApertureMode = GsplatLidarApertureMode.CameraFrustum;
                renderer.LidarFrustumCamera = camera;
                renderer.LidarAzimuthBins = 2048;
                renderer.LidarBeamCount = 128;
                renderer.LidarUpFovDeg = 10.0f;
                renderer.LidarDownFovDeg = -30.0f;

                var layout = InvokeTryGetEffectiveLidarLayout(renderer, nameof(GsplatSequenceRenderer),
                    logWhenMissing: false, out var succeeded, out var resolvedCamera);

                Assert.IsTrue(succeeded);
                Assert.AreSame(camera, resolvedCamera);

                var activeAzimuthBins = GetReflectedPropertyValue<int>(layout, "GsplatLidarLayout", "ActiveAzimuthBins");
                var activeBeamCount = GetReflectedPropertyValue<int>(layout, "GsplatLidarLayout", "ActiveBeamCount");

                Assert.AreEqual(ComputeExpectedFrustumActiveAzimuthBins(renderer.LidarAzimuthBins, camera),
                    activeAzimuthBins);
                Assert.AreEqual(ComputeExpectedFrustumActiveBeamCount(
                        renderer.LidarBeamCount,
                        renderer.LidarUpFovDeg,
                        renderer.LidarDownFovDeg,
                        camera),
                    activeBeamCount);
                Assert.Less(activeAzimuthBins, renderer.LidarAzimuthBins);
                Assert.AreEqual(renderer.LidarBeamCount, activeBeamCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void TryGetEffectiveLidarLayout_UsesRigidCameraFrameForAngles_WhenParentScaled()
        {
            var host = new GameObject("GsplatRenderer_ScaledFrustumLayout");
            var cameraParent = new GameObject("lidar-scaled-camera-parent");
            var cameraGo = new GameObject("lidar-scaled-layout-camera");
            try
            {
                host.SetActive(false);

                cameraParent.transform.position = new Vector3(1.0f, 2.0f, -3.0f);
                cameraParent.transform.rotation = Quaternion.Euler(7.0f, 22.0f, -5.0f);
                cameraParent.transform.localScale = new Vector3(2.0f, 0.5f, 3.0f);

                cameraGo.transform.SetParent(cameraParent.transform, false);
                cameraGo.transform.localPosition = new Vector3(0.25f, -0.1f, 0.4f);
                cameraGo.transform.localRotation = Quaternion.Euler(-4.0f, 15.0f, 0.0f);

                var renderer = host.AddComponent<GsplatRenderer>();
                var camera = cameraGo.AddComponent<Camera>();
                camera.aspect = 16.0f / 9.0f;
                camera.fieldOfView = 60.0f;

                renderer.LidarApertureMode = GsplatLidarApertureMode.CameraFrustum;
                renderer.LidarFrustumCamera = camera;
                renderer.LidarAzimuthBins = 2048;
                renderer.LidarBeamCount = 128;
                renderer.LidarUpFovDeg = 10.0f;
                renderer.LidarDownFovDeg = -30.0f;

                var layout = InvokeTryGetEffectiveLidarLayout(renderer, nameof(GsplatRenderer), logWhenMissing: false,
                    out var succeeded, out _);

                Assert.IsTrue(succeeded);

                GsplatUtils.BuildRigidTransformMatrices(camera.transform, out _, out var rigidWorldToSensor);
                ComputeFrustumAngleBoundsFromViewportSamples(camera, rigidWorldToSensor,
                    out var expectedAzimuthMinRad, out var expectedAzimuthMaxRad,
                    out var expectedBeamMinRad, out var expectedBeamMaxRad);

                ComputeFrustumAngleBoundsFromViewportSamples(camera, camera.transform.worldToLocalMatrix,
                    out var scaledAzimuthMinRad, out var scaledAzimuthMaxRad,
                    out var scaledBeamMinRad, out var scaledBeamMaxRad);

                Assert.That(Mathf.Abs(expectedBeamMinRad - scaledBeamMinRad), Is.GreaterThan(1.0e-3f),
                    "这个测试需要证明: 带父级缩放时,scaled local frame 与 rigid frame 的 beam 角域确实不同,否则它就不能充当有效回归样本.");

                var actualAzimuthMinRad = GetReflectedPropertyValue<float>(layout, "GsplatLidarLayout", "AzimuthMinRad");
                var actualAzimuthMaxRad = GetReflectedPropertyValue<float>(layout, "GsplatLidarLayout", "AzimuthMaxRad");
                var actualBeamMinRad = GetReflectedPropertyValue<float>(layout, "GsplatLidarLayout", "BeamMinRad");
                var actualBeamMaxRad = GetReflectedPropertyValue<float>(layout, "GsplatLidarLayout", "BeamMaxRad");

                Assert.That(actualAzimuthMinRad, Is.EqualTo(expectedAzimuthMinRad).Within(1.0e-4f));
                Assert.That(actualAzimuthMaxRad, Is.EqualTo(expectedAzimuthMaxRad).Within(1.0e-4f));
                Assert.That(actualBeamMinRad, Is.EqualTo(expectedBeamMinRad).Within(1.0e-4f));
                Assert.That(actualBeamMaxRad, Is.EqualTo(expectedBeamMaxRad).Within(1.0e-4f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraParent);
            }
        }

        [Test]
        public void TryGetEffectiveLidarLayout_RejectsOrthographicCamera_GsplatRenderer()
        {
            var host = new GameObject("GsplatRenderer_InvalidFrustumLayout");
            var cameraGo = new GameObject("lidar-invalid-layout-camera");
            try
            {
                host.SetActive(false);
                var renderer = host.AddComponent<GsplatRenderer>();
                var camera = cameraGo.AddComponent<Camera>();
                camera.orthographic = true;

                renderer.LidarApertureMode = GsplatLidarApertureMode.CameraFrustum;
                renderer.LidarFrustumCamera = camera;

                InvokeTryGetEffectiveLidarLayout(renderer, nameof(GsplatRenderer), logWhenMissing: false,
                    out var succeeded, out var resolvedCamera);

                Assert.IsFalse(succeeded);
                Assert.AreSame(camera, resolvedCamera);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void TryGetEffectiveLidarLayout_RejectsOrthographicCamera_GsplatSequenceRenderer()
        {
            var host = new GameObject("GsplatSequenceRenderer_InvalidFrustumLayout");
            var cameraGo = new GameObject("lidar-seq-invalid-layout-camera");
            try
            {
                host.SetActive(false);
                var renderer = host.AddComponent<GsplatSequenceRenderer>();
                var camera = cameraGo.AddComponent<Camera>();
                camera.orthographic = true;

                renderer.LidarApertureMode = GsplatLidarApertureMode.CameraFrustum;
                renderer.LidarFrustumCamera = camera;

                InvokeTryGetEffectiveLidarLayout(renderer, nameof(GsplatSequenceRenderer), logWhenMissing: false,
                    out var succeeded, out var resolvedCamera);

                Assert.IsFalse(succeeded);
                Assert.AreSame(camera, resolvedCamera);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraGo);
            }
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
            Assert.AreEqual(GsplatLidarApertureMode.Surround360, r.LidarApertureMode);
            Assert.AreEqual(10.0f, r.LidarExternalDynamicUpdateHz);
            Assert.AreEqual(GsplatLidarExternalEdgeAwareResolveMode.Off, r.LidarExternalEdgeAwareResolveMode);
            Assert.AreEqual(GsplatLidarExternalSubpixelResolveMode.Off, r.LidarExternalSubpixelResolveMode);
            Assert.AreEqual(2048, r.LidarAzimuthBins);

            Assert.AreEqual(10.0f, r.LidarUpFovDeg);
            Assert.AreEqual(-30.0f, r.LidarDownFovDeg);

            Assert.AreEqual(GsplatUtils.k_LidarDefaultBeamCount, r.LidarBeamCount);

            Assert.AreEqual(1.0f, r.LidarDepthNear);
            Assert.AreEqual(2.0f, r.LidarDepthFar);

            Assert.AreEqual(2.0f, r.LidarPointRadiusPixels);
            Assert.AreEqual(0.35f, r.LidarPointJitterCellFraction, 1e-6f);
            Assert.AreEqual(GsplatLidarParticleAntialiasingMode.LegacySoftEdge, r.LidarParticleAntialiasingMode);
            Assert.AreEqual(1.0f, r.LidarParticleAAFringePixels);
            Assert.AreEqual(0.0f, r.LidarExternalHitBiasMeters);
            Assert.AreEqual(-1.0f, r.LidarShowDuration);
            Assert.AreEqual(-1.0f, r.LidarHideDuration);
            Assert.AreEqual(6.0f, r.LidarShowHideWarpPixels);
            Assert.AreEqual(-1.0f, r.LidarShowHideNoiseScale);
            Assert.AreEqual(-1.0f, r.LidarShowHideNoiseSpeed);
            Assert.AreEqual(1.0f, r.LidarShowHideGlowColor.r);
            Assert.AreEqual(0.45f, r.LidarShowHideGlowColor.g, 1e-6f);
            Assert.AreEqual(0.1f, r.LidarShowHideGlowColor.b, 1e-6f);
            Assert.AreEqual(0.0f, r.LidarDepthNearColor.r);
            Assert.AreEqual(1.0f, r.LidarDepthNearColor.g);
            Assert.AreEqual(1.0f, r.LidarDepthNearColor.b);
            Assert.AreEqual(1.0f, r.LidarDepthFarColor.r);
            Assert.AreEqual(0.0f, r.LidarDepthFarColor.g);
            Assert.AreEqual(0.0f, r.LidarDepthFarColor.b);
            Assert.AreEqual(1.5f, r.LidarShowGlowIntensity);
            Assert.AreEqual(2.5f, r.LidarHideGlowIntensity);
            Assert.AreEqual(2.0f, r.LidarTrailGamma);
            Assert.AreEqual(1.0f, r.LidarIntensity);
            Assert.AreEqual(0.2f, r.LidarUnscannedIntensity);
            Assert.AreEqual(GsplatLidarDistanceDecayMode.Reciprocal, r.LidarIntensityDistanceDecayMode);
            Assert.AreEqual(0.0f, r.LidarIntensityDistanceDecay);
            Assert.AreEqual(0.0f, r.LidarUnscannedIntensityDistanceDecay);
            Assert.AreEqual(1.0f, r.LidarDepthOpacity);
            Assert.AreEqual(1.0f / 255.0f, r.LidarMinSplatOpacity, 1e-6f);
            Assert.AreEqual(GsplatLidarExternalTargetVisibilityMode.ForceRenderingOff, r.LidarExternalTargetVisibilityMode);
            Assert.IsNotNull(r.LidarExternalStaticTargets);
            Assert.IsNotNull(r.LidarExternalDynamicTargets);
            Assert.AreEqual(0, r.LidarExternalStaticTargets.Length);
            Assert.AreEqual(0, r.LidarExternalDynamicTargets.Length);
        }

        [Test]
        public void ValidateLidarSerializedFields_ClampsInvalidValues_GsplatSequenceRenderer()
        {
            var r = (GsplatSequenceRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatSequenceRenderer));
            SetLidarFieldsToInvalidValues(r);

            InvokeValidateLidarSerializedFields(r, nameof(GsplatSequenceRenderer));

            Assert.AreEqual(5.0f, r.LidarRotationHz);
            Assert.AreEqual(10.0f, r.LidarUpdateHz);
            Assert.AreEqual(GsplatLidarApertureMode.Surround360, r.LidarApertureMode);
            Assert.AreEqual(10.0f, r.LidarExternalDynamicUpdateHz);
            Assert.AreEqual(GsplatLidarExternalEdgeAwareResolveMode.Off, r.LidarExternalEdgeAwareResolveMode);
            Assert.AreEqual(GsplatLidarExternalSubpixelResolveMode.Off, r.LidarExternalSubpixelResolveMode);
            Assert.AreEqual(2048, r.LidarAzimuthBins);
            Assert.AreEqual(GsplatUtils.k_LidarDefaultBeamCount, r.LidarBeamCount);
            Assert.AreEqual(2.0f, r.LidarDepthFar);
            Assert.AreEqual(0.35f, r.LidarPointJitterCellFraction, 1e-6f);
            Assert.AreEqual(GsplatLidarParticleAntialiasingMode.LegacySoftEdge, r.LidarParticleAntialiasingMode);
            Assert.AreEqual(1.0f, r.LidarParticleAAFringePixels);
            Assert.AreEqual(0.0f, r.LidarExternalHitBiasMeters);
            Assert.AreEqual(-1.0f, r.LidarShowDuration);
            Assert.AreEqual(-1.0f, r.LidarHideDuration);
            Assert.AreEqual(6.0f, r.LidarShowHideWarpPixels);
            Assert.AreEqual(-1.0f, r.LidarShowHideNoiseScale);
            Assert.AreEqual(-1.0f, r.LidarShowHideNoiseSpeed);
            Assert.AreEqual(1.0f, r.LidarShowHideGlowColor.r);
            Assert.AreEqual(0.45f, r.LidarShowHideGlowColor.g, 1e-6f);
            Assert.AreEqual(0.1f, r.LidarShowHideGlowColor.b, 1e-6f);
            Assert.AreEqual(0.0f, r.LidarDepthNearColor.r);
            Assert.AreEqual(1.0f, r.LidarDepthNearColor.g);
            Assert.AreEqual(1.0f, r.LidarDepthNearColor.b);
            Assert.AreEqual(1.0f, r.LidarDepthFarColor.r);
            Assert.AreEqual(0.0f, r.LidarDepthFarColor.g);
            Assert.AreEqual(0.0f, r.LidarDepthFarColor.b);
            Assert.AreEqual(1.5f, r.LidarShowGlowIntensity);
            Assert.AreEqual(2.5f, r.LidarHideGlowIntensity);
            Assert.AreEqual(1.0f, r.LidarDepthOpacity);
            Assert.AreEqual(1.0f / 255.0f, r.LidarMinSplatOpacity, 1e-6f);
            Assert.AreEqual(0.2f, r.LidarUnscannedIntensity);
            Assert.AreEqual(GsplatLidarDistanceDecayMode.Reciprocal, r.LidarIntensityDistanceDecayMode);
            Assert.AreEqual(0.0f, r.LidarIntensityDistanceDecay);
            Assert.AreEqual(0.0f, r.LidarUnscannedIntensityDistanceDecay);
            Assert.AreEqual(GsplatLidarExternalTargetVisibilityMode.ForceRenderingOff, r.LidarExternalTargetVisibilityMode);
            Assert.IsNotNull(r.LidarExternalStaticTargets);
            Assert.IsNotNull(r.LidarExternalDynamicTargets);
            Assert.AreEqual(0, r.LidarExternalStaticTargets.Length);
            Assert.AreEqual(0, r.LidarExternalDynamicTargets.Length);
        }

        [Test]
        public void ValidateLidarSerializedFields_RemovesNullExternalTargetSlots_GsplatRenderer()
        {
            var a = new GameObject("lidar-ext-a");
            var b = new GameObject("lidar-ext-b");
            try
            {
                var r = (GsplatRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatRenderer));
                r.EnableLidarScan = true;
                r.LidarExternalStaticTargets = new[] { a, null, b, null };
                r.LidarExternalDynamicTargets = new[] { null, b, null, a };

                InvokeValidateLidarSerializedFields(r, nameof(GsplatRenderer));

                CollectionAssert.AreEqual(new[] { a, b }, r.LidarExternalStaticTargets);
                CollectionAssert.AreEqual(new[] { b, a }, r.LidarExternalDynamicTargets);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void ValidateLidarSerializedFields_RemovesNullExternalTargetSlots_GsplatSequenceRenderer()
        {
            var a = new GameObject("lidar-seq-ext-a");
            var b = new GameObject("lidar-seq-ext-b");
            try
            {
                var r = (GsplatSequenceRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatSequenceRenderer));
                r.EnableLidarScan = true;
                r.LidarExternalStaticTargets = new[] { a, null, b, null };
                r.LidarExternalDynamicTargets = new[] { null, b, null, a };

                InvokeValidateLidarSerializedFields(r, nameof(GsplatSequenceRenderer));

                CollectionAssert.AreEqual(new[] { a, b }, r.LidarExternalStaticTargets);
                CollectionAssert.AreEqual(new[] { b, a }, r.LidarExternalDynamicTargets);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
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
        public void ValidateLidarSerializedFields_PreservesSubpixelPointRadius_GsplatRenderer()
        {
            // 说明:
            // - 本轮用户反馈的是“<1px 显示异常”,不是“Inspector 不允许输入 <1”.
            // - 因此先锁定 C# 侧语义: 合法的 subpixel 值必须原样保留,不要在 validate 阶段被偷偷抬回 1.
            var r = (GsplatRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatRenderer));
            r.EnableLidarScan = true;
            r.LidarPointRadiusPixels = 0.35f;

            InvokeValidateLidarSerializedFields(r, nameof(GsplatRenderer));
            Assert.AreEqual(0.35f, r.LidarPointRadiusPixels, 1e-6f);
        }

        [Test]
        public void ValidateLidarSerializedFields_PreservesSubpixelPointRadius_GsplatSequenceRenderer()
        {
            var r = (GsplatSequenceRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatSequenceRenderer));
            r.EnableLidarScan = true;
            r.LidarPointRadiusPixels = 0.35f;

            InvokeValidateLidarSerializedFields(r, nameof(GsplatSequenceRenderer));
            Assert.AreEqual(0.35f, r.LidarPointRadiusPixels, 1e-6f);
        }

        [Test]
        public void ValidateLidarSerializedFields_ClampsPointJitterCellFractionToUnitInterval_GsplatRenderer()
        {
            var r = (GsplatRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatRenderer));
            r.EnableLidarScan = true;
            r.LidarPointJitterCellFraction = 1.7f;

            InvokeValidateLidarSerializedFields(r, nameof(GsplatRenderer));
            Assert.AreEqual(1.0f, r.LidarPointJitterCellFraction, 1e-6f);
        }

        [Test]
        public void ValidateLidarSerializedFields_ClampsPointJitterCellFractionToUnitInterval_GsplatSequenceRenderer()
        {
            var r = (GsplatSequenceRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatSequenceRenderer));
            r.EnableLidarScan = true;
            r.LidarPointJitterCellFraction = 1.7f;

            InvokeValidateLidarSerializedFields(r, nameof(GsplatSequenceRenderer));
            Assert.AreEqual(1.0f, r.LidarPointJitterCellFraction, 1e-6f);
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
        public void ValidateLidarSerializedFields_DoesNotClampBeamCountMax_GsplatRenderer()
        {
            // 说明:
            // - 之前 `LidarBeamCount` 会在 validate 阶段被悄悄压回 512.
            // - 现在已经改成“只防御非法小值,不再做历史保守上限钳制”.
            var r = (GsplatRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatRenderer));
            r.EnableLidarScan = true;
            r.LidarBeamCount = 2048;

            InvokeValidateLidarSerializedFields(r, nameof(GsplatRenderer));
            Assert.AreEqual(2048, r.LidarBeamCount);
        }

        [Test]
        public void ValidateLidarSerializedFields_DoesNotClampBeamCountMax_GsplatSequenceRenderer()
        {
            var r = (GsplatSequenceRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatSequenceRenderer));
            r.EnableLidarScan = true;
            r.LidarBeamCount = 2048;

            InvokeValidateLidarSerializedFields(r, nameof(GsplatSequenceRenderer));
            Assert.AreEqual(2048, r.LidarBeamCount);
        }

        [Test]
        public void ResolveEffectiveLidarParticleAntialiasingMode_FallsBackToAnalyticCoverageWithoutMsaa()
        {
            var cameraGo = new GameObject("lidar-aa-fallback-camera");
            var targetTexture = new RenderTexture(32, 32, 16, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1
            };
            Camera camera = null;

            try
            {
                camera = cameraGo.AddComponent<Camera>();
                camera.allowMSAA = false;
                camera.targetTexture = targetTexture;

                Assert.AreEqual(
                    GsplatLidarParticleAntialiasingMode.AnalyticCoverage,
                    GsplatUtils.ResolveEffectiveLidarParticleAntialiasingMode(
                        GsplatLidarParticleAntialiasingMode.AlphaToCoverage,
                        camera));
                Assert.AreEqual(
                    GsplatLidarParticleAntialiasingMode.AnalyticCoverage,
                    GsplatUtils.ResolveEffectiveLidarParticleAntialiasingMode(
                        GsplatLidarParticleAntialiasingMode.AnalyticCoveragePlusAlphaToCoverage,
                        camera));
            }
            finally
            {
                if (camera != null)
                {
                    // 先解除 Camera 对 RT 的引用,避免 Unity 把正常清理记成 error log.
                    camera.targetTexture = null;
                }

                UnityEngine.Object.DestroyImmediate(targetTexture);
                UnityEngine.Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void ResolveEffectiveLidarParticleAntialiasingMode_KeepsA2CWhenMsaaIsAvailable()
        {
            var cameraGo = new GameObject("lidar-aa-msaa-camera");
            var targetTexture = new RenderTexture(32, 32, 16, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };
            Camera camera = null;

            try
            {
                camera = cameraGo.AddComponent<Camera>();
                camera.allowMSAA = true;
                camera.targetTexture = targetTexture;

                Assert.AreEqual(
                    GsplatLidarParticleAntialiasingMode.AlphaToCoverage,
                    GsplatUtils.ResolveEffectiveLidarParticleAntialiasingMode(
                        GsplatLidarParticleAntialiasingMode.AlphaToCoverage,
                        camera));
                Assert.AreEqual(
                    GsplatLidarParticleAntialiasingMode.AnalyticCoveragePlusAlphaToCoverage,
                    GsplatUtils.ResolveEffectiveLidarParticleAntialiasingMode(
                        GsplatLidarParticleAntialiasingMode.AnalyticCoveragePlusAlphaToCoverage,
                        camera));
            }
            finally
            {
                if (camera != null)
                {
                    // 先解除 Camera 对 RT 的引用,避免 Unity 把正常清理记成 error log.
                    camera.targetTexture = null;
                }

                UnityEngine.Object.DestroyImmediate(targetTexture);
                UnityEngine.Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void GetLidarParticleMsaaSampleCount_HdrpUsesResolvedFrameSettingsEvenWhenCameraAllowMsaaIsFalse()
        {
            var hdrpAssetType = FindLoadedType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset");
            if (hdrpAssetType == null)
            {
                Assert.Ignore("HDRP package is not loaded, skipping HDRP-specific LiDAR A2C test.");
            }

            var hdrpAsset = GraphicsSettings.currentRenderPipeline;
            if (hdrpAsset == null || !hdrpAssetType.IsInstanceOfType(hdrpAsset))
            {
                Assert.Ignore("HDRP-specific LiDAR A2C test requires an active HDRP pipeline.");
            }

            var renderPipelineSettings = GetReflectedMemberValue(
                hdrpAsset,
                hdrpAssetType.FullName,
                "currentPlatformRenderPipelineSettings");
            var supportedLitShaderMode = GetReflectedMemberValue(
                renderPipelineSettings,
                "HDRP RenderPipelineSettings",
                "supportedLitShaderMode");
            if (string.Equals(supportedLitShaderMode.ToString(), "DeferredOnly", StringComparison.Ordinal))
            {
                Assert.Ignore("Current HDRP asset is DeferredOnly, MSAA is intentionally unavailable.");
            }

            if (GetReflectedMemberValue<bool>(renderPipelineSettings, "HDRP RenderPipelineSettings", "supportWater"))
            {
                Assert.Ignore("Current HDRP asset enables Water, HDRP 会在 sanitize 阶段禁用 MSAA.");
            }

            var hdAdditionalCameraDataType = FindLoadedType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData");
            var frameSettingsType = FindLoadedType("UnityEngine.Rendering.HighDefinition.FrameSettings");
            var frameSettingsFieldType = FindLoadedType("UnityEngine.Rendering.HighDefinition.FrameSettingsField");
            var litShaderModeType = FindLoadedType("UnityEngine.Rendering.HighDefinition.LitShaderMode");
            var msaaModeType = FindLoadedType("UnityEngine.Rendering.HighDefinition.MSAAMode");
            var frameSettingsOverrideMaskType =
                FindLoadedType("UnityEngine.Rendering.HighDefinition.FrameSettingsOverrideMask");

            Assert.IsNotNull(hdAdditionalCameraDataType, "Expected HDRP HDAdditionalCameraData type to be available.");
            Assert.IsNotNull(frameSettingsType, "Expected HDRP FrameSettings type to be available.");
            Assert.IsNotNull(frameSettingsFieldType, "Expected HDRP FrameSettingsField type to be available.");
            Assert.IsNotNull(litShaderModeType, "Expected HDRP LitShaderMode enum to be available.");
            Assert.IsNotNull(msaaModeType, "Expected HDRP MSAAMode enum to be available.");
            Assert.IsNotNull(frameSettingsOverrideMaskType,
                "Expected HDRP FrameSettingsOverrideMask type to be available.");

            var cameraGo = new GameObject("hdrp-lidar-aa-camera");
            Camera camera = null;

            try
            {
                camera = cameraGo.AddComponent<Camera>();
                var hdCamera = AddComponentByType(cameraGo, hdAdditionalCameraDataType,
                    "UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData");

                SetReflectedMemberValue(hdCamera, hdAdditionalCameraDataType.FullName, "customRenderingSettings", true);

                var createFrameSettingsMethod = frameSettingsType.GetMethod(
                    "Create",
                    BindingFlags.Public | BindingFlags.Static);
                Assert.IsNotNull(createFrameSettingsMethod, "Expected HDRP FrameSettings.Create() to exist.");
                var customFrameSettings = createFrameSettingsMethod.Invoke(null, null);

                SetReflectedMemberValue(
                    customFrameSettings,
                    frameSettingsType.FullName,
                    "litShaderMode",
                    GetEnumValue(litShaderModeType, "Forward", "UnityEngine.Rendering.HighDefinition.LitShaderMode"));
                InvokeReflectedMethod(
                    customFrameSettings,
                    frameSettingsType.FullName,
                    "SetEnabled",
                    GetEnumValue(frameSettingsFieldType, "MSAA",
                        "UnityEngine.Rendering.HighDefinition.FrameSettingsField"),
                    true);
                SetReflectedMemberValue(
                    customFrameSettings,
                    frameSettingsType.FullName,
                    "msaaMode",
                    GetEnumValue(msaaModeType, "MSAA4X", "UnityEngine.Rendering.HighDefinition.MSAAMode"));
                SetReflectedMemberValue(
                    hdCamera,
                    hdAdditionalCameraDataType.FullName,
                    "m_RenderingPathCustomFrameSettings",
                    customFrameSettings);

                var overrideMask = Activator.CreateInstance(frameSettingsOverrideMaskType);
                var bitArray = GetReflectedMemberValue(overrideMask, frameSettingsOverrideMaskType.FullName, "mask");
                SetReflectedBitArrayValue(
                    bitArray,
                    "HDRP FrameSettingsOverrideMask.mask",
                    Convert.ToUInt32(GetEnumValue(frameSettingsFieldType, "LitShaderMode",
                        "UnityEngine.Rendering.HighDefinition.FrameSettingsField")),
                    true);
                SetReflectedBitArrayValue(
                    bitArray,
                    "HDRP FrameSettingsOverrideMask.mask",
                    Convert.ToUInt32(GetEnumValue(frameSettingsFieldType, "MSAA",
                        "UnityEngine.Rendering.HighDefinition.FrameSettingsField")),
                    true);
                SetReflectedBitArrayValue(
                    bitArray,
                    "HDRP FrameSettingsOverrideMask.mask",
                    Convert.ToUInt32(GetEnumValue(frameSettingsFieldType, "MSAAMode",
                        "UnityEngine.Rendering.HighDefinition.FrameSettingsField")),
                    true);
                SetReflectedMemberValue(overrideMask, frameSettingsOverrideMaskType.FullName, "mask", bitArray);
                SetReflectedMemberValue(
                    hdCamera,
                    hdAdditionalCameraDataType.FullName,
                    "renderingPathCustomFrameSettingsOverrideMask",
                    overrideMask);

                // 说明:
                // - 这里显式把 allowMSAA 设成 false,目的是稳定复现用户遇到的条件.
                // - 测试锁定的是“即使这个 legacy 字段为 false,HDRP resolved MSAA 仍应被识别”.
                camera.allowMSAA = false;

                Assert.IsFalse(camera.allowMSAA,
                    "这个测试要求 camera.allowMSAA=false,用于锁定 HDRP 路径不再把它当成 A2C 硬门槛.");
                Assert.AreEqual(4, GsplatUtils.GetLidarParticleMsaaSampleCount(camera));
                Assert.IsTrue(GsplatUtils.IsLidarParticleMsaaAvailable(camera));
                Assert.AreEqual(
                    GsplatLidarParticleAntialiasingMode.AlphaToCoverage,
                    GsplatUtils.ResolveEffectiveLidarParticleAntialiasingMode(
                        GsplatLidarParticleAntialiasingMode.AlphaToCoverage,
                        camera));

                var diagnostics = GsplatUtils.GetLidarParticleMsaaDiagnosticSummary(camera);
                StringAssert.Contains("msaaSamples=4", diagnostics);
                StringAssert.Contains("msaaSource=hdrp-frame-settings", diagnostics);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void GetLidarParticleMsaaSampleCount_HdrpUsesMsaaModeWhenLegacyMsaaBitIsFalse()
        {
            var hdrpAssetType = FindLoadedType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset");
            if (hdrpAssetType == null)
            {
                Assert.Ignore("HDRP package is not loaded, skipping HDRP-specific LiDAR A2C test.");
            }

            var hdrpAsset = GraphicsSettings.currentRenderPipeline;
            if (hdrpAsset == null || !hdrpAssetType.IsInstanceOfType(hdrpAsset))
            {
                Assert.Ignore("HDRP-specific LiDAR A2C test requires an active HDRP pipeline.");
            }

            var renderPipelineSettings = GetReflectedMemberValue(
                hdrpAsset,
                hdrpAssetType.FullName,
                "currentPlatformRenderPipelineSettings");
            var supportedLitShaderMode = GetReflectedMemberValue(
                renderPipelineSettings,
                "HDRP RenderPipelineSettings",
                "supportedLitShaderMode");
            if (string.Equals(supportedLitShaderMode.ToString(), "DeferredOnly", StringComparison.Ordinal))
            {
                Assert.Ignore("Current HDRP asset is DeferredOnly, MSAA is intentionally unavailable.");
            }

            if (GetReflectedMemberValue<bool>(renderPipelineSettings, "HDRP RenderPipelineSettings", "supportWater"))
            {
                Assert.Ignore("Current HDRP asset enables Water, HDRP 会在 sanitize 阶段禁用 MSAA.");
            }

            var hdAdditionalCameraDataType = FindLoadedType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData");
            var frameSettingsType = FindLoadedType("UnityEngine.Rendering.HighDefinition.FrameSettings");
            var frameSettingsFieldType = FindLoadedType("UnityEngine.Rendering.HighDefinition.FrameSettingsField");
            var litShaderModeType = FindLoadedType("UnityEngine.Rendering.HighDefinition.LitShaderMode");
            var msaaModeType = FindLoadedType("UnityEngine.Rendering.HighDefinition.MSAAMode");
            var frameSettingsOverrideMaskType =
                FindLoadedType("UnityEngine.Rendering.HighDefinition.FrameSettingsOverrideMask");

            Assert.IsNotNull(hdAdditionalCameraDataType, "Expected HDRP HDAdditionalCameraData type to be available.");
            Assert.IsNotNull(frameSettingsType, "Expected HDRP FrameSettings type to be available.");
            Assert.IsNotNull(frameSettingsFieldType, "Expected HDRP FrameSettingsField type to be available.");
            Assert.IsNotNull(litShaderModeType, "Expected HDRP LitShaderMode enum to be available.");
            Assert.IsNotNull(msaaModeType, "Expected HDRP MSAAMode enum to be available.");
            Assert.IsNotNull(frameSettingsOverrideMaskType,
                "Expected HDRP FrameSettingsOverrideMask type to be available.");

            var cameraGo = new GameObject("hdrp-lidar-aa-legacy-bit-false-camera");
            Camera camera = null;

            try
            {
                camera = cameraGo.AddComponent<Camera>();
                var hdCamera = AddComponentByType(cameraGo, hdAdditionalCameraDataType,
                    "UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData");

                SetReflectedMemberValue(hdCamera, hdAdditionalCameraDataType.FullName, "customRenderingSettings", true);

                var createFrameSettingsMethod = frameSettingsType.GetMethod(
                    "Create",
                    BindingFlags.Public | BindingFlags.Static);
                Assert.IsNotNull(createFrameSettingsMethod, "Expected HDRP FrameSettings.Create() to exist.");
                var customFrameSettings = createFrameSettingsMethod.Invoke(null, null);

                SetReflectedMemberValue(
                    customFrameSettings,
                    frameSettingsType.FullName,
                    "litShaderMode",
                    GetEnumValue(litShaderModeType, "Forward", "UnityEngine.Rendering.HighDefinition.LitShaderMode"));
                SetReflectedMemberValue(
                    customFrameSettings,
                    frameSettingsType.FullName,
                    "msaaMode",
                    GetEnumValue(msaaModeType, "MSAA4X", "UnityEngine.Rendering.HighDefinition.MSAAMode"));
                SetReflectedMemberValue(
                    hdCamera,
                    hdAdditionalCameraDataType.FullName,
                    "m_RenderingPathCustomFrameSettings",
                    customFrameSettings);

                var legacyMsaaEnabled = (bool)InvokeReflectedMethod(
                    customFrameSettings,
                    frameSettingsType.FullName,
                    "IsEnabled",
                    GetEnumValue(frameSettingsFieldType, "MSAA",
                        "UnityEngine.Rendering.HighDefinition.FrameSettingsField"));
                Assert.IsFalse(legacyMsaaEnabled,
                    "该测试要求旧的 legacy MSAA bit 保持 false,用于锁定只看 `msaaMode` 的新语义.");

                var overrideMask = Activator.CreateInstance(frameSettingsOverrideMaskType);
                var bitArray = GetReflectedMemberValue(overrideMask, frameSettingsOverrideMaskType.FullName, "mask");
                SetReflectedBitArrayValue(
                    bitArray,
                    "HDRP FrameSettingsOverrideMask.mask",
                    Convert.ToUInt32(GetEnumValue(frameSettingsFieldType, "LitShaderMode",
                        "UnityEngine.Rendering.HighDefinition.FrameSettingsField")),
                    true);
                SetReflectedBitArrayValue(
                    bitArray,
                    "HDRP FrameSettingsOverrideMask.mask",
                    Convert.ToUInt32(GetEnumValue(frameSettingsFieldType, "MSAAMode",
                        "UnityEngine.Rendering.HighDefinition.FrameSettingsField")),
                    true);
                SetReflectedMemberValue(overrideMask, frameSettingsOverrideMaskType.FullName, "mask", bitArray);
                SetReflectedMemberValue(
                    hdCamera,
                    hdAdditionalCameraDataType.FullName,
                    "renderingPathCustomFrameSettingsOverrideMask",
                    overrideMask);

                camera.allowMSAA = false;

                Assert.AreEqual(4, GsplatUtils.GetLidarParticleMsaaSampleCount(camera));
                Assert.IsTrue(GsplatUtils.IsLidarParticleMsaaAvailable(camera));

                var diagnostics = GsplatUtils.GetLidarParticleMsaaDiagnosticSummary(camera);
                StringAssert.Contains("msaaSamples=4", diagnostics);
                StringAssert.Contains("msaaSource=hdrp-frame-settings", diagnostics);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraGo);
            }
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
        public void ResolveRadarScanVisibilityDurationSeconds_UsesOverridesOrFallback_GsplatRenderer()
        {
            var r = (GsplatRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatRenderer));

            r.RenderStyleSwitchDurationSeconds = 1.5f;
            r.LidarShowDuration = 0.7f;
            r.LidarHideDuration = 0.9f;

            // durationSeconds<0: 优先用 LiDAR 专用字段.
            Assert.AreEqual(0.7f,
                InvokeResolveRadarScanVisibilityDurationSeconds(r, nameof(GsplatRenderer), enableRadarScan: true, durationSeconds: -1.0f),
                1e-6f);
            Assert.AreEqual(0.9f,
                InvokeResolveRadarScanVisibilityDurationSeconds(r, nameof(GsplatRenderer), enableRadarScan: false, durationSeconds: -1.0f),
                1e-6f);

            // override>=0: 强制使用 override.
            Assert.AreEqual(2.2f,
                InvokeResolveRadarScanVisibilityDurationSeconds(r, nameof(GsplatRenderer), enableRadarScan: true, durationSeconds: 2.2f),
                1e-6f);

            // LiDAR 字段<0: 回退到 RenderStyleSwitchDurationSeconds.
            r.LidarShowDuration = -1.0f;
            r.LidarHideDuration = -1.0f;
            Assert.AreEqual(1.5f,
                InvokeResolveRadarScanVisibilityDurationSeconds(r, nameof(GsplatRenderer), enableRadarScan: true, durationSeconds: -1.0f),
                1e-6f);
            Assert.AreEqual(1.5f,
                InvokeResolveRadarScanVisibilityDurationSeconds(r, nameof(GsplatRenderer), enableRadarScan: false, durationSeconds: -1.0f),
                1e-6f);
        }

        [Test]
        public void ResolveRadarScanVisibilityDurationSeconds_UsesOverridesOrFallback_GsplatSequenceRenderer()
        {
            var r = (GsplatSequenceRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatSequenceRenderer));

            r.RenderStyleSwitchDurationSeconds = 1.5f;
            r.LidarShowDuration = 0.7f;
            r.LidarHideDuration = 0.9f;

            Assert.AreEqual(0.7f,
                InvokeResolveRadarScanVisibilityDurationSeconds(r, nameof(GsplatSequenceRenderer), enableRadarScan: true, durationSeconds: -1.0f),
                1e-6f);
            Assert.AreEqual(0.9f,
                InvokeResolveRadarScanVisibilityDurationSeconds(r, nameof(GsplatSequenceRenderer), enableRadarScan: false, durationSeconds: -1.0f),
                1e-6f);

            Assert.AreEqual(2.2f,
                InvokeResolveRadarScanVisibilityDurationSeconds(r, nameof(GsplatSequenceRenderer), enableRadarScan: true, durationSeconds: 2.2f),
                1e-6f);

            r.LidarShowDuration = -1.0f;
            r.LidarHideDuration = -1.0f;
            Assert.AreEqual(1.5f,
                InvokeResolveRadarScanVisibilityDurationSeconds(r, nameof(GsplatSequenceRenderer), enableRadarScan: true, durationSeconds: -1.0f),
                1e-6f);
            Assert.AreEqual(1.5f,
                InvokeResolveRadarScanVisibilityDurationSeconds(r, nameof(GsplatSequenceRenderer), enableRadarScan: false, durationSeconds: -1.0f),
                1e-6f);
        }

        [Test]
        public void ResolveLidarUnscannedIntensityForShader_RespectsKeepToggle_GsplatRenderer()
        {
            var r = (GsplatRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatRenderer));

            r.LidarUnscannedIntensity = 0.25f;
            r.LidarKeepUnscannedPoints = false;
            Assert.AreEqual(0.0f, InvokeResolveLidarUnscannedIntensityForShader(r, nameof(GsplatRenderer)), 1e-6f);

            r.LidarKeepUnscannedPoints = true;
            Assert.AreEqual(0.25f, InvokeResolveLidarUnscannedIntensityForShader(r, nameof(GsplatRenderer)), 1e-6f);

            // 非法值兜底:
            // - 即便用户脚本把值写成 NaN,也应回退到默认值,避免 shader 出现黑屏或随机值.
            r.LidarUnscannedIntensity = float.NaN;
            Assert.AreEqual(0.2f, InvokeResolveLidarUnscannedIntensityForShader(r, nameof(GsplatRenderer)), 1e-6f);
        }

        [Test]
        public void ResolveLidarUnscannedIntensityForShader_RespectsKeepToggle_GsplatSequenceRenderer()
        {
            var r = (GsplatSequenceRenderer)FormatterServices.GetUninitializedObject(typeof(GsplatSequenceRenderer));

            r.LidarUnscannedIntensity = 0.25f;
            r.LidarKeepUnscannedPoints = false;
            Assert.AreEqual(0.0f, InvokeResolveLidarUnscannedIntensityForShader(r, nameof(GsplatSequenceRenderer)), 1e-6f);

            r.LidarKeepUnscannedPoints = true;
            Assert.AreEqual(0.25f, InvokeResolveLidarUnscannedIntensityForShader(r, nameof(GsplatSequenceRenderer)), 1e-6f);

            r.LidarUnscannedIntensity = float.PositiveInfinity;
            Assert.AreEqual(0.2f, InvokeResolveLidarUnscannedIntensityForShader(r, nameof(GsplatSequenceRenderer)), 1e-6f);
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

        [Test]
        public void ResolveVisibilityLocalBoundsForThisFrame_EncapsulatesExternalTargets_GsplatRenderer()
        {
            var host = new GameObject("GsplatRenderer_VisibilityUnion");
            var staticRoot = new GameObject("external-static-root");
            var dynamicRoot = new GameObject("external-dynamic-root");
            var staticChild = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var dynamicChild = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var asset = ScriptableObject.CreateInstance<GsplatAsset>();
            try
            {
                host.SetActive(false);
                staticChild.transform.SetParent(staticRoot.transform, false);
                staticChild.transform.position = new Vector3(10.0f, 0.0f, 0.0f);
                dynamicChild.transform.SetParent(dynamicRoot.transform, false);
                dynamicChild.transform.position = new Vector3(-12.0f, 0.0f, 0.0f);

                asset.Bounds = new Bounds(Vector3.zero, Vector3.one * 2.0f);

                var renderer = host.AddComponent<GsplatRenderer>();
                renderer.GsplatAsset = asset;
                renderer.LidarExternalStaticTargets = new[] { staticRoot };
                renderer.LidarExternalDynamicTargets = new[] { dynamicRoot };

                var localBounds = InvokeResolveVisibilityLocalBoundsForThisFrame(renderer, nameof(GsplatRenderer));

                Assert.LessOrEqual(localBounds.min.x, -12.5f + 1.0e-4f);
                Assert.GreaterOrEqual(localBounds.max.x, 10.5f - 1.0e-4f);
                Assert.LessOrEqual(localBounds.min.y, -1.0f + 1.0e-4f);
                Assert.GreaterOrEqual(localBounds.max.y, 1.0f - 1.0e-4f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(staticRoot);
                UnityEngine.Object.DestroyImmediate(dynamicRoot);
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ResolveVisibilityLocalBoundsForThisFrame_EncapsulatesExternalTargets_GsplatSequenceRenderer()
        {
            var host = new GameObject("GsplatSequenceRenderer_VisibilityUnion");
            var staticRoot = new GameObject("sequence-external-static-root");
            var dynamicRoot = new GameObject("sequence-external-dynamic-root");
            var staticChild = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var dynamicChild = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var asset = ScriptableObject.CreateInstance<GsplatSequenceAsset>();
            try
            {
                host.SetActive(false);
                staticChild.transform.SetParent(staticRoot.transform, false);
                staticChild.transform.position = new Vector3(10.0f, 0.0f, 0.0f);
                dynamicChild.transform.SetParent(dynamicRoot.transform, false);
                dynamicChild.transform.position = new Vector3(-12.0f, 0.0f, 0.0f);

                asset.UnionBounds = new Bounds(Vector3.zero, Vector3.one * 2.0f);

                var renderer = host.AddComponent<GsplatSequenceRenderer>();
                renderer.SequenceAsset = asset;
                renderer.LidarExternalStaticTargets = new[] { staticRoot };
                renderer.LidarExternalDynamicTargets = new[] { dynamicRoot };

                var localBounds = InvokeResolveVisibilityLocalBoundsForThisFrame(renderer, nameof(GsplatSequenceRenderer));

                Assert.LessOrEqual(localBounds.min.x, -12.5f + 1.0e-4f);
                Assert.GreaterOrEqual(localBounds.max.x, 10.5f - 1.0e-4f);
                Assert.LessOrEqual(localBounds.min.y, -1.0f + 1.0e-4f);
                Assert.GreaterOrEqual(localBounds.max.y, 1.0f - 1.0e-4f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(staticRoot);
                UnityEngine.Object.DestroyImmediate(dynamicRoot);
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ExternalTargetHelper_HitsMeshWithoutSourceCollider_HidesSourceMesh_AndRestoresOnDispose()
        {
            // 说明:
            // - 这条测试直接锁定本 change 最核心的运行时语义:
            //   没有现成 Collider 的 mesh,也必须通过 proxy mesh collider 被 LiDAR 命中.
            // - 同时锁定 scan-only 语义:
            //   `ForceRenderingOff` 会隐藏 source renderer,但 helper Dispose 后必须恢复原状态.
            // - 并顺手验证材质主色 `_Color` 能进入 external hit 结果.
            var asm = typeof(GsplatRenderer).Assembly;
            var helperType = asm.GetType("Gsplat.GsplatLidarExternalTargetHelper");
            var scanType = asm.GetType("Gsplat.GsplatLidarScan");
            Assert.IsNotNull(helperType, "Expected internal type Gsplat.GsplatLidarExternalTargetHelper to exist.");
            Assert.IsNotNull(scanType, "Expected internal type Gsplat.GsplatLidarScan to exist.");

            var helper = Activator.CreateInstance(helperType, nonPublic: true);
            var scan = Activator.CreateInstance(scanType, nonPublic: true);
            var layout = InvokeCreateSurround360Layout(1, 1, 0.0f, 0.0f);
            Assert.IsNotNull(helper, "Failed to create helper instance via reflection.");
            Assert.IsNotNull(scan, "Failed to create lidar scan instance via reflection.");

            var tryUpdate = helperType.GetMethod("TryUpdateExternalHits", BindingFlags.Instance | BindingFlags.Public);
            var rangeField = helperType.GetField("m_hitRangeSqBits", BindingFlags.Instance | BindingFlags.NonPublic);
            var colorField = helperType.GetField("m_hitBaseColors", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(tryUpdate, "Expected TryUpdateExternalHits to exist.");
            Assert.IsNotNull(rangeField, "Expected helper scratch field m_hitRangeSqBits to exist.");
            Assert.IsNotNull(colorField, "Expected helper scratch field m_hitBaseColors to exist.");

            var lidarOriginGo = new GameObject("lidar-origin");
            var root = new GameObject("external-target-root");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Material material = null;
            MeshRenderer meshRenderer = null;

            try
            {
                cube.transform.SetParent(root.transform, false);
                cube.transform.position = new Vector3(0.0f, 0.0f, 5.0f);

                var sourceCollider = cube.GetComponent<Collider>();
                if (sourceCollider)
                    UnityEngine.Object.DestroyImmediate(sourceCollider);

                meshRenderer = cube.GetComponent<MeshRenderer>();
                var shader = Shader.Find("Unlit/Color");
                Assert.IsNotNull(shader, "Expected built-in shader `Unlit/Color` to exist for test material.");

                material = new Material(shader)
                {
                    color = new Color(0.2f, 0.7f, 0.3f, 1.0f)
                };
                meshRenderer.sharedMaterial = material;

                var updated = (bool)tryUpdate.Invoke(helper, new object[]
                {
                    scan,
                    new[] { root },
                    GsplatLidarExternalTargetVisibilityMode.ForceRenderingOff,
                    lidarOriginGo.transform,
                    layout,
                    50.0f
                });

                Assert.IsTrue(updated, "Expected external helper to report a valid hit update.");
                Assert.IsTrue(meshRenderer.forceRenderingOff,
                    "Expected ForceRenderingOff mode to hide the source renderer while keeping LiDAR scanning active.");

                var ranges = (uint[])rangeField.GetValue(helper);
                var colors = (Vector4[])colorField.GetValue(helper);
                Assert.IsNotNull(ranges);
                Assert.IsNotNull(colors);
                Assert.AreEqual(1, ranges.Length);
                Assert.AreEqual(1, colors.Length);

                var rangeSq = BitConverter.Int32BitsToSingle(unchecked((int)ranges[0]));
                Assert.AreEqual(4.5f * 4.5f, rangeSq, 1.0e-2f,
                    "Expected the ray to hit the cube front face,而不是中心点或漏掉命中.");

                Assert.AreEqual(0.2f, colors[0].x, 1.0e-3f);
                Assert.AreEqual(0.7f, colors[0].y, 1.0e-3f);
                Assert.AreEqual(0.3f, colors[0].z, 1.0e-3f);
            }
            finally
            {
                if (helper is IDisposable disposable)
                    disposable.Dispose();

                if (meshRenderer)
                {
                    Assert.IsFalse(meshRenderer.forceRenderingOff,
                        "Expected helper.Dispose() to restore the source renderer's original forceRenderingOff state.");
                }

                if (material)
                    UnityEngine.Object.DestroyImmediate(material);

                UnityEngine.Object.DestroyImmediate(lidarOriginGo);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
