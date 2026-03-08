// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Gsplat.Tests
{
    public sealed class GsplatLidarExternalGpuCaptureTests
    {
        static Type GetExternalGpuCaptureType()
        {
            var type = typeof(GsplatRenderer).Assembly.GetType("Gsplat.GsplatLidarExternalGpuCapture");
            Assert.IsNotNull(type, "Expected internal type Gsplat.GsplatLidarExternalGpuCapture to exist.");
            return type;
        }

        static object CreateExternalGpuCapture()
        {
            var type = GetExternalGpuCaptureType();
            var instance = Activator.CreateInstance(type, nonPublic: true);
            Assert.IsNotNull(instance, "Failed to create GsplatLidarExternalGpuCapture via reflection.");
            return instance;
        }

        static object InvokeCreateCameraFrustumLayout(Camera frustumCamera,
            int baseAzimuthBins, int baseBeamCount,
            float baselineUpFovDeg, float baselineDownFovDeg)
        {
            var asm = typeof(GsplatRenderer).Assembly;
            var layoutType = asm.GetType("Gsplat.GsplatLidarLayout");
            Assert.IsNotNull(layoutType, "Expected internal type Gsplat.GsplatLidarLayout to exist.");

            var m = layoutType.GetMethod("TryCreateCameraFrustum", BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(m, "Expected GsplatLidarLayout.TryCreateCameraFrustum to exist.");

            var args = new object[] { frustumCamera, baseAzimuthBins, baseBeamCount, baselineUpFovDeg, baselineDownFovDeg, null, null };
            var succeeded = (bool)m.Invoke(null, args);
            Assert.IsTrue(succeeded, $"Expected TryCreateCameraFrustum to succeed. reason={args[6]}");
            return args[5];
        }

        static string InvokeDebugGetStaticCaptureDirtyReason(object capture,
            Camera frustumCamera,
            object layout,
            int captureWidth,
            int captureHeight,
            GameObject[] staticTargets)
        {
            var m = capture.GetType().GetMethod("DebugGetStaticCaptureDirtyReasonForInputs",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, "Expected debug dirty-reason helper to exist.");
            return (string)m.Invoke(capture, new object[] { frustumCamera, layout, captureWidth, captureHeight, staticTargets });
        }

        static int InvokeDebugCommitStaticCaptureSignature(object capture,
            Camera frustumCamera,
            object layout,
            int captureWidth,
            int captureHeight,
            GameObject[] staticTargets)
        {
            var m = capture.GetType().GetMethod("DebugCommitStaticCaptureSignatureForInputs",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, "Expected debug signature commit helper to exist.");
            return (int)m.Invoke(capture, new object[] { frustumCamera, layout, captureWidth, captureHeight, staticTargets });
        }

        static float InvokeDebugComputeRayDepthSqFromLinearViewDepth(float linearViewDepth, float rayForwardDot)
        {
            var type = GetExternalGpuCaptureType();
            var m = type.GetMethod("DebugComputeRayDepthSqFromLinearViewDepth",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(m, "Expected debug depthSq helper to exist.");
            return (float)m.Invoke(null, new object[] { linearViewDepth, rayForwardDot });
        }

        static bool InvokeIsDynamicCaptureUpdateDue(double nowRealtime,
            float updateHz,
            double lastCaptureRealtime,
            out string reason)
        {
            var type = GetExternalGpuCaptureType();
            var m = type.GetMethod("IsDynamicCaptureUpdateDue", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(m, "Expected private static cadence helper to exist.");

            var args = new object[] { nowRealtime, updateHz, lastCaptureRealtime, null };
            var due = (bool)m.Invoke(null, args);
            reason = args[3] as string;
            return due;
        }

        static Bounds InvokeResolveVisibilityLocalBoundsForThisFrame(object obj, string ownerName)
        {
            var m = obj.GetType().GetMethod("ResolveVisibilityLocalBoundsForThisFrame",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(m, $"Expected {ownerName}.ResolveVisibilityLocalBoundsForThisFrame to exist.");
            return (Bounds)m.Invoke(obj, null);
        }

        [Test]
        public void ExternalGpuCapture_StaticSignatureDetectsMaterialStateAndLayoutChanges()
        {
            var capture = CreateExternalGpuCapture();
            var cameraGo = new GameObject("ExternalGpuCapture_SignatureCamera");
            var root = new GameObject("external-static-root");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Material material = null;

            try
            {
                var camera = cameraGo.AddComponent<Camera>();
                camera.orthographic = false;
                camera.fieldOfView = 60.0f;
                camera.aspect = 16.0f / 9.0f;
                camera.pixelRect = new Rect(0.0f, 0.0f, 640.0f, 360.0f);

                cube.transform.SetParent(root.transform, false);
                cube.transform.position = new Vector3(0.0f, 0.0f, 5.0f);

                var renderer = cube.GetComponent<MeshRenderer>();
                var shader = Shader.Find("Unlit/Color");
                Assert.IsNotNull(shader, "Expected built-in shader `Unlit/Color` to exist.");

                material = new Material(shader)
                {
                    color = new Color(0.2f, 0.7f, 0.3f, 1.0f)
                };
                renderer.sharedMaterial = material;

                var layout = InvokeCreateCameraFrustumLayout(camera, 2048, 128, 10.0f, -30.0f);

                var committedHash = InvokeDebugCommitStaticCaptureSignature(capture, camera, layout, 640, 360, new[] { root });
                Assert.AreNotEqual(0, committedHash);
                Assert.AreEqual("none",
                    InvokeDebugGetStaticCaptureDirtyReason(capture, camera, layout, 640, 360, new[] { root }));

                material.color = new Color(0.9f, 0.1f, 0.2f, 1.0f);
                Assert.AreEqual("renderer-material",
                    InvokeDebugGetStaticCaptureDirtyReason(capture, camera, layout, 640, 360, new[] { root }),
                    "Expected `_Color` change to invalidate static signature.");

                InvokeDebugCommitStaticCaptureSignature(capture, camera, layout, 640, 360, new[] { root });
                root.SetActive(false);
                Assert.AreEqual("renderer-state",
                    InvokeDebugGetStaticCaptureDirtyReason(capture, camera, layout, 640, 360, new[] { root }),
                    "Expected activeInHierarchy change to invalidate static signature.");

                root.SetActive(true);
                InvokeDebugCommitStaticCaptureSignature(capture, camera, layout, 640, 360, new[] { root });
                Assert.AreEqual("capture-layout",
                    InvokeDebugGetStaticCaptureDirtyReason(capture, camera, layout, 320, 180, new[] { root }),
                    "Expected capture RT layout change to invalidate static signature.");
            }
            finally
            {
                if (capture is IDisposable disposable)
                    disposable.Dispose();

                if (material)
                    UnityEngine.Object.DestroyImmediate(material);

                UnityEngine.Object.DestroyImmediate(cameraGo);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ExternalGpuCapture_DynamicCadenceHelper_RespectsIntervalAndTimeReset()
        {
            Assert.IsTrue(InvokeIsDynamicCaptureUpdateDue(10.0, 5.0f, -1.0, out var reason));
            Assert.AreEqual("uninitialized", reason);

            Assert.IsFalse(InvokeIsDynamicCaptureUpdateDue(10.10, 5.0f, 10.0, out reason));
            Assert.AreEqual("none", reason);

            Assert.IsTrue(InvokeIsDynamicCaptureUpdateDue(10.21, 5.0f, 10.0, out reason));
            Assert.AreEqual("cadence-due", reason);

            Assert.IsTrue(InvokeIsDynamicCaptureUpdateDue(9.0, 5.0f, 10.0, out reason));
            Assert.AreEqual("time-reset", reason);

            Assert.IsFalse(InvokeIsDynamicCaptureUpdateDue(20.05, 0.0f, 20.0, out reason));
            Assert.AreEqual("none", reason, "Invalid updateHz should fall back to 10Hz cadence.");

            Assert.IsTrue(InvokeIsDynamicCaptureUpdateDue(20.11, 0.0f, 20.0, out reason));
            Assert.AreEqual("cadence-due", reason);
        }

        [Test]
        public void ExternalGpuCapture_DebugDepthSqHelper_UsesRayDistanceSemantics()
        {
            var depthSq = InvokeDebugComputeRayDepthSqFromLinearViewDepth(5.0f, 0.5f);
            Assert.AreEqual(100.0f, depthSq, 1.0e-6f,
                "Expected helper to convert linear view depth into LiDAR ray-distance squared.");
        }

        [Test]
        public void ResolveVisibilityLocalBoundsForThisFrame_EncapsulatesExternalTargets_WhenFrustumMode_GsplatRenderer()
        {
            var host = new GameObject("GsplatRenderer_FrustumVisibilityUnion");
            var cameraGo = new GameObject("GsplatRenderer_FrustumVisibilityUnionCamera");
            var staticRoot = new GameObject("external-static-root");
            var dynamicRoot = new GameObject("external-dynamic-root");
            var staticChild = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var dynamicChild = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var asset = ScriptableObject.CreateInstance<GsplatAsset>();

            try
            {
                host.SetActive(false);

                var frustumCamera = cameraGo.AddComponent<Camera>();
                frustumCamera.fieldOfView = 55.0f;
                frustumCamera.aspect = 16.0f / 9.0f;
                frustumCamera.pixelRect = new Rect(0.0f, 0.0f, 640.0f, 360.0f);

                staticChild.transform.SetParent(staticRoot.transform, false);
                staticChild.transform.position = new Vector3(10.0f, 0.0f, 0.0f);
                dynamicChild.transform.SetParent(dynamicRoot.transform, false);
                dynamicChild.transform.position = new Vector3(-12.0f, 0.0f, 0.0f);

                asset.Bounds = new Bounds(Vector3.zero, Vector3.one * 2.0f);

                var renderer = host.AddComponent<GsplatRenderer>();
                renderer.GsplatAsset = asset;
                renderer.EnableLidarScan = true;
                renderer.LidarApertureMode = GsplatLidarApertureMode.CameraFrustum;
                renderer.LidarFrustumCamera = frustumCamera;
                renderer.LidarExternalStaticTargets = new[] { staticRoot };
                renderer.LidarExternalDynamicTargets = new[] { dynamicRoot };

                var localBounds = InvokeResolveVisibilityLocalBoundsForThisFrame(renderer, nameof(GsplatRenderer));

                Assert.LessOrEqual(localBounds.min.x, -12.5f + 1.0e-4f);
                Assert.GreaterOrEqual(localBounds.max.x, 10.5f - 1.0e-4f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraGo);
                UnityEngine.Object.DestroyImmediate(staticRoot);
                UnityEngine.Object.DestroyImmediate(dynamicRoot);
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ResolveVisibilityLocalBoundsForThisFrame_EncapsulatesExternalTargets_WhenFrustumMode_GsplatSequenceRenderer()
        {
            var host = new GameObject("GsplatSequenceRenderer_FrustumVisibilityUnion");
            var cameraGo = new GameObject("GsplatSequenceRenderer_FrustumVisibilityUnionCamera");
            var staticRoot = new GameObject("sequence-external-static-root");
            var dynamicRoot = new GameObject("sequence-external-dynamic-root");
            var staticChild = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var dynamicChild = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var asset = ScriptableObject.CreateInstance<GsplatSequenceAsset>();

            try
            {
                host.SetActive(false);

                var frustumCamera = cameraGo.AddComponent<Camera>();
                frustumCamera.fieldOfView = 55.0f;
                frustumCamera.aspect = 16.0f / 9.0f;
                frustumCamera.pixelRect = new Rect(0.0f, 0.0f, 640.0f, 360.0f);

                staticChild.transform.SetParent(staticRoot.transform, false);
                staticChild.transform.position = new Vector3(10.0f, 0.0f, 0.0f);
                dynamicChild.transform.SetParent(dynamicRoot.transform, false);
                dynamicChild.transform.position = new Vector3(-12.0f, 0.0f, 0.0f);

                asset.UnionBounds = new Bounds(Vector3.zero, Vector3.one * 2.0f);

                var renderer = host.AddComponent<GsplatSequenceRenderer>();
                renderer.SequenceAsset = asset;
                renderer.EnableLidarScan = true;
                renderer.LidarApertureMode = GsplatLidarApertureMode.CameraFrustum;
                renderer.LidarFrustumCamera = frustumCamera;
                renderer.LidarExternalStaticTargets = new[] { staticRoot };
                renderer.LidarExternalDynamicTargets = new[] { dynamicRoot };

                var localBounds = InvokeResolveVisibilityLocalBoundsForThisFrame(renderer, nameof(GsplatSequenceRenderer));

                Assert.LessOrEqual(localBounds.min.x, -12.5f + 1.0e-4f);
                Assert.GreaterOrEqual(localBounds.max.x, 10.5f - 1.0e-4f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraGo);
                UnityEngine.Object.DestroyImmediate(staticRoot);
                UnityEngine.Object.DestroyImmediate(dynamicRoot);
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }
}
