// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

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

        static Vector2Int InvokeDebugResolveCaptureSizeForInputs(Camera frustumCamera,
            object layout,
            GsplatLidarExternalCaptureResolutionMode captureResolutionMode,
            float captureResolutionScale,
            Vector2Int explicitCaptureResolution)
        {
            var type = GetExternalGpuCaptureType();
            var m = type.GetMethod("DebugResolveCaptureSizeForInputs",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(m, "Expected debug capture-size helper to exist.");
            return (Vector2Int)m.Invoke(null,
                new object[]
                {
                    frustumCamera,
                    layout,
                    captureResolutionMode,
                    captureResolutionScale,
                    explicitCaptureResolution
                });
        }

        static Vector2[] InvokeDebugResolveSubpixelCandidateUvsForInputs(Vector2 uv,
            Vector2Int captureSize,
            GsplatLidarExternalSubpixelResolveMode subpixelResolveMode)
        {
            var type = GetExternalGpuCaptureType();
            var m = type.GetMethod("DebugResolveSubpixelCandidateUvsForInputs",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(m, "Expected debug subpixel candidate helper to exist.");
            return (Vector2[])m.Invoke(null, new object[] { uv, captureSize, subpixelResolveMode });
        }

        static Vector2Int[] InvokeDebugResolveEdgeAwareNeighborhoodPixelsForInputs(Vector2 uv,
            Vector2Int captureSize,
            GsplatLidarExternalEdgeAwareResolveMode edgeAwareResolveMode)
        {
            var type = GetExternalGpuCaptureType();
            var m = type.GetMethod("DebugResolveEdgeAwareNeighborhoodPixelsForInputs",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(m, "Expected debug edge-aware neighborhood helper to exist.");
            return (Vector2Int[])m.Invoke(null, new object[] { uv, captureSize, edgeAwareResolveMode });
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
        public void ExternalGpuCapture_DebugResolveCaptureSizeForInputs_UsesAutoScaleAndExplicitModes()
        {
            var cameraGo = new GameObject("ExternalGpuCapture_CaptureSizeCamera");

            try
            {
                var camera = cameraGo.AddComponent<Camera>();
                camera.orthographic = false;
                camera.fieldOfView = 60.0f;
                camera.aspect = 16.0f / 9.0f;
                camera.pixelRect = new Rect(0.0f, 0.0f, 640.0f, 360.0f);

                var layout = InvokeCreateCameraFrustumLayout(camera, 2048, 128, 10.0f, -30.0f);

                Assert.AreEqual(new Vector2Int(640, 360),
                    InvokeDebugResolveCaptureSizeForInputs(camera,
                        layout,
                        GsplatLidarExternalCaptureResolutionMode.Auto,
                        1.0f,
                        new Vector2Int(1920, 1080)));

                Assert.AreEqual(new Vector2Int(960, 540),
                    InvokeDebugResolveCaptureSizeForInputs(camera,
                        layout,
                        GsplatLidarExternalCaptureResolutionMode.Scale,
                        1.5f,
                        new Vector2Int(1920, 1080)));

                Assert.AreEqual(new Vector2Int(1234, 567),
                    InvokeDebugResolveCaptureSizeForInputs(camera,
                        layout,
                        GsplatLidarExternalCaptureResolutionMode.Explicit,
                        1.0f,
                        new Vector2Int(1234, 567)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void ExternalGpuCapture_DebugResolveCaptureSizeForInputs_FallsBackToTargetTextureAndClampsToHardwareLimit()
        {
            var cameraGo = new GameObject("ExternalGpuCapture_CaptureSizeFallbackCamera");
            RenderTexture targetTexture = null;

            try
            {
                var camera = cameraGo.AddComponent<Camera>();
                camera.orthographic = false;
                camera.fieldOfView = 60.0f;
                camera.aspect = 16.0f / 9.0f;
                camera.pixelRect = new Rect(0.0f, 0.0f, 640.0f, 360.0f);

                var layout = InvokeCreateCameraFrustumLayout(camera, 2048, 128, 10.0f, -30.0f);
                targetTexture = new RenderTexture(320, 180, 0);
                camera.pixelRect = Rect.zero;
                camera.targetTexture = targetTexture;

                Assert.AreEqual(new Vector2Int(320, 180),
                    InvokeDebugResolveCaptureSizeForInputs(camera,
                        layout,
                        GsplatLidarExternalCaptureResolutionMode.Auto,
                        1.0f,
                        new Vector2Int(1920, 1080)));

                var maxTextureSize = Mathf.Max(SystemInfo.maxTextureSize, 1);
                Assert.AreEqual(new Vector2Int(maxTextureSize, maxTextureSize),
                    InvokeDebugResolveCaptureSizeForInputs(camera,
                        layout,
                        GsplatLidarExternalCaptureResolutionMode.Explicit,
                        1.0f,
                        new Vector2Int(maxTextureSize + 257, maxTextureSize + 513)));
            }
            finally
            {
                if (cameraGo)
                {
                    var camera = cameraGo.GetComponent<Camera>();
                    if (camera)
                        camera.targetTexture = null;
                }

                if (targetTexture)
                    UnityEngine.Object.DestroyImmediate(targetTexture);

                UnityEngine.Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void ExternalGpuCapture_DebugResolveCaptureSizeForInputs_SanitizesInvalidScaleAndSupportsDownsample()
        {
            var cameraGo = new GameObject("ExternalGpuCapture_CaptureSizeScaleSanitizeCamera");

            try
            {
                var camera = cameraGo.AddComponent<Camera>();
                camera.orthographic = false;
                camera.fieldOfView = 60.0f;
                camera.aspect = 16.0f / 9.0f;
                camera.pixelRect = new Rect(0.0f, 0.0f, 640.0f, 360.0f);

                var layout = InvokeCreateCameraFrustumLayout(camera, 2048, 128, 10.0f, -30.0f);
                var autoSize = InvokeDebugResolveCaptureSizeForInputs(camera,
                    layout,
                    GsplatLidarExternalCaptureResolutionMode.Auto,
                    1.0f,
                    new Vector2Int(1920, 1080));

                Assert.AreEqual(autoSize,
                    InvokeDebugResolveCaptureSizeForInputs(camera,
                        layout,
                        GsplatLidarExternalCaptureResolutionMode.Scale,
                        float.NaN,
                        new Vector2Int(1920, 1080)),
                    "非法 scale 应回退到 Auto 基准尺寸,而不是生成未定义 capture size.");

                Assert.AreEqual(autoSize,
                    InvokeDebugResolveCaptureSizeForInputs(camera,
                        layout,
                        GsplatLidarExternalCaptureResolutionMode.Scale,
                        -2.0f,
                        new Vector2Int(1920, 1080)),
                    "负数 scale 应回退到 Auto 基准尺寸.");

                Assert.AreEqual(new Vector2Int(320, 180),
                    InvokeDebugResolveCaptureSizeForInputs(camera,
                        layout,
                        GsplatLidarExternalCaptureResolutionMode.Scale,
                        0.5f,
                        new Vector2Int(1920, 1080)),
                    "合法的 `Scale < 1` 应继续作为 downsample 工作,而不是被强行钳回 Auto.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraGo);
            }
        }

        [Test]
        public void ExternalGpuCapture_DebugResolveSubpixelCandidateUvsForInputs_KeepsOffAtCenterAndQuad4Deterministic()
        {
            var uv = new Vector2(0.5f, 0.5f);
            var captureSize = new Vector2Int(8, 4);

            var offCandidates = InvokeDebugResolveSubpixelCandidateUvsForInputs(
                uv,
                captureSize,
                GsplatLidarExternalSubpixelResolveMode.Off);
            CollectionAssert.AreEqual(new[] { uv }, offCandidates);

            var quadCandidatesA = InvokeDebugResolveSubpixelCandidateUvsForInputs(
                uv,
                captureSize,
                GsplatLidarExternalSubpixelResolveMode.Quad4);
            var quadCandidatesB = InvokeDebugResolveSubpixelCandidateUvsForInputs(
                uv,
                captureSize,
                GsplatLidarExternalSubpixelResolveMode.Quad4);

            Assert.AreEqual(4, quadCandidatesA.Length);
            CollectionAssert.AreEqual(quadCandidatesA, quadCandidatesB,
                "Quad4 candidate pattern 必须 deterministic,不能依赖随机 jitter.");

            Assert.AreEqual(new Vector2(0.46875f, 0.4375f), quadCandidatesA[0]);
            Assert.AreEqual(new Vector2(0.53125f, 0.4375f), quadCandidatesA[1]);
            Assert.AreEqual(new Vector2(0.46875f, 0.5625f), quadCandidatesA[2]);
            Assert.AreEqual(new Vector2(0.53125f, 0.5625f), quadCandidatesA[3]);
        }

        [Test]
        public void ExternalGpuCapture_DebugResolveEdgeAwareNeighborhoodPixelsForInputs_UsesKernel2x2And3x3Layouts()
        {
            var uv = new Vector2(0.51f, 0.49f);
            var captureSize = new Vector2Int(8, 6);

            var offPixels = InvokeDebugResolveEdgeAwareNeighborhoodPixelsForInputs(
                uv,
                captureSize,
                GsplatLidarExternalEdgeAwareResolveMode.Off);
            CollectionAssert.AreEqual(new[] { new Vector2Int(4, 2) }, offPixels);

            var kernel2x2Pixels = InvokeDebugResolveEdgeAwareNeighborhoodPixelsForInputs(
                uv,
                captureSize,
                GsplatLidarExternalEdgeAwareResolveMode.Kernel2x2);
            CollectionAssert.AreEqual(
                new[]
                {
                    new Vector2Int(3, 2),
                    new Vector2Int(4, 2),
                    new Vector2Int(3, 3),
                    new Vector2Int(4, 3)
                },
                kernel2x2Pixels);

            var kernel3x3Pixels = InvokeDebugResolveEdgeAwareNeighborhoodPixelsForInputs(
                uv,
                captureSize,
                GsplatLidarExternalEdgeAwareResolveMode.Kernel3x3);
            CollectionAssert.AreEqual(
                new[]
                {
                    new Vector2Int(3, 1),
                    new Vector2Int(4, 1),
                    new Vector2Int(5, 1),
                    new Vector2Int(3, 2),
                    new Vector2Int(4, 2),
                    new Vector2Int(5, 2),
                    new Vector2Int(3, 3),
                    new Vector2Int(4, 3),
                    new Vector2Int(5, 3)
                },
                kernel3x3Pixels);
        }

        [Test]
        public void ExternalGpuCaptureShader_UsesCullOffAndHardwareDepthToPreferNearestVisibleSurface()
        {
            const string kShaderAssetPath = "Packages/wu.yize.gsplat/Runtime/Shaders/GsplatLidarExternalCapture.shader";

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(kShaderAssetPath);
            Assert.IsNotNull(shader, $"Failed to load LiDAR external capture shader at path: {kShaderAssetPath}");
            Assert.AreEqual("Hidden/Gsplat/LidarExternalCapture", shader.name,
                "Unexpected LiDAR external capture shader name. Possibly loaded a different asset.");

            var projectRoot = Directory.GetParent(Application.dataPath);
            Assert.IsNotNull(projectRoot, "Failed to resolve Unity project root from Application.dataPath.");

            var shaderFullPath = Path.Combine(projectRoot.FullName, kShaderAssetPath);
            Assert.IsTrue(File.Exists(shaderFullPath), $"Expected shader source file to exist: {shaderFullPath}");

            var shaderText = File.ReadAllText(shaderFullPath);
            StringAssert.IsMatch(@"(?m)^\s*Cull Off\s*$", shaderText);
            Assert.IsFalse(Regex.IsMatch(shaderText, @"(?m)^\s*Cull Back\s*$"),
                "External capture 不应继续依赖背面剔除,否则在手动 VP / 负缩放等场景下容易把 front/back 判反.");
            StringAssert.Contains("_LidarExternalDepthZTest", shaderText,
                "DepthCapture 的 ZTest 必须可按平台切换,否则 reversed-Z 平台会稳定把 far side 留下来.");
            StringAssert.Contains("ZTest [_LidarExternalDepthZTest]", shaderText,
                "DepthCapture 应通过材质属性切换 LessEqual / GreaterEqual.");
            StringAssert.Contains("ZWrite On", shaderText,
                "DepthCapture 必须写入深度,否则 color pass 无法只保留最近表面.");
            StringAssert.Contains("return input.linearDepth;", shaderText,
                "DepthCapture 应直接把最近表面的线性 view depth 写入颜色 RT.");
            StringAssert.Contains("ZTest Equal", shaderText,
                "SurfaceColorCapture 应只保留与最近深度一致的表面颜色.");
            Assert.IsFalse(shaderText.Contains("BlendOp Max"),
                "本轮修复不应继续依赖 `RFloat + BlendOp Max`,避免某些平台退化成最后写入者赢.");
            Assert.IsFalse(shaderText.Contains("_LidarExternalResolvedDepthTex"),
                "SurfaceColorCapture 不应再依赖额外的 resolved depth 纹理.");
        }

        [Test]
        public void ExternalGpuResolve_UsesLinearDepthTextureBeforeRayDistanceConversion()
        {
            const string kComputeAssetPath = "Packages/wu.yize.gsplat/Runtime/Shaders/Gsplat.compute";

            var projectRoot = Directory.GetParent(Application.dataPath);
            Assert.IsNotNull(projectRoot, "Failed to resolve Unity project root from Application.dataPath.");

            var computeFullPath = Path.Combine(projectRoot.FullName, kComputeAssetPath);
            Assert.IsTrue(File.Exists(computeFullPath), $"Expected compute source file to exist: {computeFullPath}");

            var computeText = File.ReadAllText(computeFullPath);
            StringAssert.Contains("float linearDepth = linearDepthTex.Load(int3(pixel, 0)).x;",
                computeText,
                "point texel read 现在应收敛在统一 helper 里,而不是继续在 static/dynamic 分支各写一份.");
            StringAssert.Contains("float rayDepth = linearDepth / rayForwardDot;",
                computeText,
                "Resolve 仍必须先读取线性 view depth,再转换成 LiDAR ray depth.");
            Assert.IsFalse(computeText.Contains("_LidarExternalStaticLinearDepthTex.Sample("),
                "external static depth resolve 应保持 point texel read,不要偷偷切成 bilinear Sample.");
            Assert.IsFalse(computeText.Contains("_LidarExternalDynamicLinearDepthTex.Sample("),
                "external dynamic depth resolve 应保持 point texel read,不要偷偷切成 bilinear Sample.");
            Assert.IsFalse(computeText.Contains("_LidarExternalStaticLinearDepthTex.SampleLevel("),
                "static depth resolve 不应通过 SampleLevel 做额外纹理过滤.");
            Assert.IsFalse(computeText.Contains("_LidarExternalDynamicLinearDepthTex.SampleLevel("),
                "dynamic depth resolve 不应通过 SampleLevel 做额外纹理过滤.");
            Assert.IsFalse(computeText.Contains("float linearDepth = rcp(encodedDepth);"),
                "Resolve 不应再把 capture texture 当作 encoded depth 解码.");
            StringAssert.Contains("LoadExternalPointSample(",
                computeText,
                "linear depth -> ray depth 的点采样语义必须收敛在统一 helper 里.");
            StringAssert.Contains("int _LidarExternalEdgeAwareResolveMode;", computeText,
                "compute resolve 必须显式接收 edge-aware resolve mode.");
            StringAssert.Contains("int _LidarExternalSubpixelResolveMode;", computeText,
                "compute resolve 必须显式接收 subpixel resolve mode.");
            StringAssert.Contains("ResolveExternalCaptureSource(", computeText,
                "external resolve 应通过统一 helper 收敛 static / dynamic 的 hybrid resolve 语义.");
            StringAssert.Contains("_LidarExternalStaticLinearDepthTex,", computeText,
                "static capture 仍必须走线性 depth 纹理输入.");
            StringAssert.Contains("_LidarExternalDynamicLinearDepthTex,", computeText,
                "dynamic capture 仍必须走线性 depth 纹理输入.");
            StringAssert.Contains("GetExternalSubpixelCandidateUv(", computeText,
                "Quad4 subpixel candidate 生成必须独立成 helper,以便锁定 deterministic pattern.");
            StringAssert.Contains("GetExternalNeighborhoodPixel(", computeText,
                "edge-aware resolve 必须显式读取 kernel 邻域,而不是偷偷退回 blur/bilinear.");
            StringAssert.Contains("return bestNeighborhoodSample.Valid != 0 ? bestNeighborhoodSample : centerSample;", computeText,
                "edge-aware 过滤失败时必须回退中心 point sample.");
            StringAssert.Contains("_LidarExternalBaseColor[cell] = bestSample.Valid != 0 ? bestSample.BaseColor : 0.0;", computeText,
                "final color 必须跟随最终 depth winner,不能单独 average.");
        }

        [Test]
        public void ExternalGpuCaptureSource_PreservesDepthBufferForColorPass()
        {
            const string kSourceAssetPath = "Packages/wu.yize.gsplat/Runtime/Lidar/GsplatLidarExternalGpuCapture.cs";

            var projectRoot = Directory.GetParent(Application.dataPath);
            Assert.IsNotNull(projectRoot, "Failed to resolve Unity project root from Application.dataPath.");

            var sourceFullPath = Path.Combine(projectRoot.FullName, kSourceAssetPath);
            Assert.IsTrue(File.Exists(sourceFullPath), $"Expected source file to exist: {sourceFullPath}");

            var sourceText = File.ReadAllText(sourceFullPath);
            StringAssert.Contains("m_materialInstance.SetFloat(k_lidarExternalDepthZTest, depthZTest);", sourceText,
                "capture helper 必须按平台设置 external depth 的 compare function.");
            StringAssert.Contains("m_cmd.ClearRenderTarget(true, true, Color.clear, clearDepth);", sourceText,
                "depth pass 必须按平台使用正确的 clearDepth,否则 reversed-Z 平台会稳定留下 far side.");
            StringAssert.Contains("m_cmd.ClearRenderTarget(false, true, Color.clear);", sourceText,
                "Surface color pass 必须保留上一 pass 的 depth buffer,否则无法稳定锁定最近表面颜色.");
        }

        [Test]
        public void ExternalGpuCaptureSource_PassesHybridResolveModesToCompute()
        {
            const string kSourceAssetPath = "Packages/wu.yize.gsplat/Runtime/Lidar/GsplatLidarExternalGpuCapture.cs";

            var projectRoot = Directory.GetParent(Application.dataPath);
            Assert.IsNotNull(projectRoot, "Failed to resolve Unity project root from Application.dataPath.");

            var sourceFullPath = Path.Combine(projectRoot.FullName, kSourceAssetPath);
            Assert.IsTrue(File.Exists(sourceFullPath), $"Expected source file to exist: {sourceFullPath}");

            var sourceText = File.ReadAllText(sourceFullPath);
            StringAssert.Contains("k_lidarExternalEdgeAwareResolveMode", sourceText,
                "GPU capture helper 必须显式持有 edge-aware resolve mode 的 compute property ID.");
            StringAssert.Contains("k_lidarExternalSubpixelResolveMode", sourceText,
                "GPU capture helper 必须显式持有 subpixel resolve mode 的 compute property ID.");
            StringAssert.Contains("m_cmd.SetComputeIntParam(computeShader, k_lidarExternalEdgeAwareResolveMode, (int)edgeAwareResolveMode);",
                sourceText,
                "ExecuteResolve 必须把 edge-aware resolve mode 下发到 compute shader.");
            StringAssert.Contains("m_cmd.SetComputeIntParam(computeShader, k_lidarExternalSubpixelResolveMode, (int)subpixelResolveMode);",
                sourceText,
                "ExecuteResolve 必须把 subpixel resolve mode 下发到 compute shader.");
        }

        [Test]
        public void ExternalGpuCaptureSource_KeepsDepthAndSurfaceColorCaptureDimensionsAligned()
        {
            const string kSourceAssetPath = "Packages/wu.yize.gsplat/Runtime/Lidar/GsplatLidarExternalGpuCapture.cs";

            var projectRoot = Directory.GetParent(Application.dataPath);
            Assert.IsNotNull(projectRoot, "Failed to resolve Unity project root from Application.dataPath.");

            var sourceFullPath = Path.Combine(projectRoot.FullName, kSourceAssetPath);
            Assert.IsTrue(File.Exists(sourceFullPath), $"Expected source file to exist: {sourceFullPath}");

            var sourceText = File.ReadAllText(sourceFullPath);
            StringAssert.Contains(
                "buffers.LinearDepthTexture = CreateColorTexture(\"GsplatLiDARExternalLinearDepth\", captureWidth, captureHeight,",
                sourceText,
                "linearDepth capture 必须与 supersampled capture 宽高保持一致.");
            StringAssert.Contains(
                "buffers.SurfaceColorTexture = CreateColorTexture(\"GsplatLiDARExternalSurfaceColor\", captureWidth, captureHeight,",
                sourceText,
                "surfaceColor capture 必须与 supersampled capture 宽高保持一致.");
            StringAssert.Contains(
                "buffers.DepthStencilTexture = new RenderTexture(captureWidth, captureHeight, 24, RenderTextureFormat.Depth)",
                sourceText,
                "depth/stencil 也必须与 depth/color capture 使用同一套 capture 尺寸.");
        }

        [Test]
        public void ExternalGpuResolve_CombinedPath_EvaluatesSubpixelThenEdgeAwareThenFinalWinner()
        {
            const string kComputeAssetPath = "Packages/wu.yize.gsplat/Runtime/Shaders/Gsplat.compute";

            var projectRoot = Directory.GetParent(Application.dataPath);
            Assert.IsNotNull(projectRoot, "Failed to resolve Unity project root from Application.dataPath.");

            var computeFullPath = Path.Combine(projectRoot.FullName, kComputeAssetPath);
            Assert.IsTrue(File.Exists(computeFullPath), $"Expected compute source file to exist: {computeFullPath}");

            var computeText = File.ReadAllText(computeFullPath);
            var resolveSourceIndex = computeText.IndexOf("ExternalResolveSample ResolveExternalCaptureSource(",
                StringComparison.Ordinal);
            Assert.GreaterOrEqual(resolveSourceIndex, 0,
                "Expected hybrid resolve source helper to exist.");

            var candidateUvIndex = computeText.IndexOf("float2 candidateUv = GetExternalSubpixelCandidateUv(",
                resolveSourceIndex,
                StringComparison.Ordinal);
            var candidateResolveIndex = computeText.IndexOf("ExternalResolveSample candidateSample = ResolveExternalCandidate(",
                resolveSourceIndex,
                StringComparison.Ordinal);
            var finalWinnerIndex = computeText.IndexOf("if (IsExternalResolveSampleCloser(candidateSample, bestCandidateSample))",
                resolveSourceIndex,
                StringComparison.Ordinal);

            Assert.Greater(candidateUvIndex, resolveSourceIndex,
                "Combined path 必须先生成 subpixel candidate uv.");
            Assert.Greater(candidateResolveIndex, candidateUvIndex,
                "Combined path 必须在 candidate uv 之后再执行 edge-aware resolve.");
            Assert.Greater(finalWinnerIndex, candidateResolveIndex,
                "Combined path 必须在 candidate resolve 之后再做 final nearest winner 选择.");
        }

        [Test]
        public void ExternalGpuCaptureDepthPass_CenterPixelMatchesSphereFrontDepth()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("当前图形设备为 Null,无法执行真实 external GPU capture 深度验证.");

            const int kCaptureSize = 65;
            var cameraGo = new GameObject("ExternalGpuCapture_DepthCamera");
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Material material = null;
            CommandBuffer cmd = null;
            RenderTexture linearDepthRt = null;
            RenderTexture depthRt = null;
            Texture2D readbackTexture = null;

            try
            {
                var camera = cameraGo.AddComponent<Camera>();
                camera.orthographic = false;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100.0f;
                camera.fieldOfView = 60.0f;
                camera.aspect = 1.0f;

                sphere.transform.position = new Vector3(0.0f, 0.0f, 5.0f);
                sphere.transform.localScale = Vector3.one;

                var shader = Shader.Find("Hidden/Gsplat/LidarExternalCapture");
                Assert.IsNotNull(shader, "Expected hidden LiDAR external capture shader to exist.");
                material = new Material(shader);
                material.SetFloat("_LidarExternalDepthZTest",
                    SystemInfo.usesReversedZBuffer
                        ? (float)CompareFunction.GreaterEqual
                        : (float)CompareFunction.LessEqual);

                var linearDepthFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)
                    ? RenderTextureFormat.RFloat
                    : RenderTextureFormat.ARGBFloat;
                linearDepthRt = new RenderTexture(kCaptureSize, kCaptureSize, 0, linearDepthFormat, RenderTextureReadWrite.Linear)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 1
                };
                linearDepthRt.Create();

                depthRt = new RenderTexture(kCaptureSize, kCaptureSize, 24, RenderTextureFormat.Depth)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 1
                };
                depthRt.Create();

                cmd = new CommandBuffer { name = "Gsplat.Tests.ExternalGpuCaptureDepthPass" };
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix,
                    GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
                cmd.SetRenderTarget(linearDepthRt.colorBuffer, depthRt.depthBuffer);
                cmd.ClearRenderTarget(true, true, Color.clear, SystemInfo.usesReversedZBuffer ? 0.0f : 1.0f);
                cmd.DrawMesh(sphere.GetComponent<MeshFilter>().sharedMesh,
                    sphere.transform.localToWorldMatrix,
                    material,
                    0,
                    0);
                Graphics.ExecuteCommandBuffer(cmd);

                readbackTexture = new Texture2D(kCaptureSize, kCaptureSize, TextureFormat.RGBAFloat, false, true);
                var previousActive = RenderTexture.active;
                RenderTexture.active = linearDepthRt;
                readbackTexture.ReadPixels(new Rect(0, 0, kCaptureSize, kCaptureSize), 0, 0);
                readbackTexture.Apply(false, false);
                RenderTexture.active = previousActive;

                var centerPixel = readbackTexture.GetPixel(kCaptureSize / 2, kCaptureSize / 2).r;
                const float kExpectedFrontDepth = 4.5f;
                const float kExpectedBackDepth = 5.5f;

                Assert.That(centerPixel, Is.EqualTo(kExpectedFrontDepth).Within(0.15f),
                    $"Expected capture center pixel to land on sphere front depth (~{kExpectedFrontDepth}), but got {centerPixel:0.###}.");
                Assert.That(Mathf.Abs(centerPixel - kExpectedBackDepth), Is.GreaterThan(0.5f),
                    "Capture center pixel unexpectedly looks like the sphere back depth, which would explain用户看到粒子落在背面.");
            }
            finally
            {
                if (cmd != null)
                    cmd.Release();

                if (readbackTexture)
                    UnityEngine.Object.DestroyImmediate(readbackTexture);

                if (linearDepthRt)
                    UnityEngine.Object.DestroyImmediate(linearDepthRt);

                if (depthRt)
                    UnityEngine.Object.DestroyImmediate(depthRt);

                if (material)
                    UnityEngine.Object.DestroyImmediate(material);

                if (sphere)
                    UnityEngine.Object.DestroyImmediate(sphere);

                if (cameraGo)
                    UnityEngine.Object.DestroyImmediate(cameraGo);
            }
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
