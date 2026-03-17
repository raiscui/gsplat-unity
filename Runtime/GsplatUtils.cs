// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
#if GSPLAT_ENABLE_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace Gsplat
{
    /// <summary>
    /// Gsplat 的显示风格.
    /// - Gaussian: 常规高斯基元渲染(椭圆高斯核).
    /// - ParticleDots: 粒子圆片/圆点(屏幕空间圆盘).
    /// </summary>
    public enum GsplatRenderStyle
    {
        Gaussian = 0,
        ParticleDots = 1
    }

    /// <summary>
    /// LiDAR 点云颜色模式.
    /// - Depth: 由距离(DepthNear..DepthFar)映射颜色.
    /// - SplatColorSH0: 采样 first return 对应 splat 的基础颜色(SH0).
    /// </summary>
    public enum GsplatLidarColorMode
    {
        Depth = 0,
        SplatColorSH0 = 1
    }

    /// <summary>
    /// LiDAR 强度的距离衰减模式(近强远弱).
    /// - Reciprocal: atten(dist)=1/(1+dist*decay)
    /// - Exponential: atten(dist)=exp(-dist*decay)
    /// </summary>
    public enum GsplatLidarDistanceDecayMode
    {
        Reciprocal = 0,
        Exponential = 1
    }

    /// <summary>
    /// LiDAR 扫描口径模式.
    /// - Surround360: 继续使用传统 360 度水平口径,传感器位姿来自 `LidarOrigin`.
    /// - CameraFrustum: 口径与传感器外参都直接来自 `LidarFrustumCamera`.
    /// </summary>
    public enum GsplatLidarApertureMode
    {
        Surround360 = 0,
        CameraFrustum = 1
    }

    /// <summary>
    /// external target 的普通 mesh 可见性模式.
    /// - KeepVisible: 继续显示原始 mesh,同时参与 LiDAR 扫描.
    /// - ForceRenderingOff: 不显示原始 mesh,仅保留 LiDAR 扫描语义.
    /// - ForceRenderingOffInPlayMode: 仅在 Play 模式隐藏原始 mesh,编辑器平时仍显示.
    /// </summary>
    public enum GsplatLidarExternalTargetVisibilityMode
    {
        KeepVisible = 0,
        ForceRenderingOff = 1,
        ForceRenderingOffInPlayMode = 2
    }

    /// <summary>
    /// frustum external GPU capture 的分辨率模式.
    /// - Auto: 复用当前 frustum camera 的默认 capture 基准尺寸.
    /// - Scale: 先解析 Auto 的基准尺寸,再乘以倍率.
    /// - Explicit: 直接使用显式的宽高.
    /// </summary>
    public enum GsplatLidarExternalCaptureResolutionMode
    {
        Auto = 0,
        Scale = 1,
        Explicit = 2
    }

    /// <summary>
    /// LiDAR 粒子抗锯齿模式.
    /// - LegacySoftEdge: 继续使用固定 feather 的旧边缘语义.
    /// - AnalyticCoverage: 使用屏幕导数驱动的本地 coverage AA.
    /// - AlphaToCoverage: 依赖 MSAA 的 alpha-to-coverage.
    /// - AnalyticCoveragePlusAlphaToCoverage: analytic coverage 与 A2C 叠加.
    /// </summary>
    public enum GsplatLidarParticleAntialiasingMode
    {
        LegacySoftEdge = 0,
        AnalyticCoverage = 1,
        AlphaToCoverage = 2,
        AnalyticCoveragePlusAlphaToCoverage = 3
    }

    public static class GsplatUtils
    {
        public const string k_PackagePath = "Packages/wu.yize.gsplat/";
        public const int k_LidarDefaultBeamCount = 128;
        public const int k_LidarDefaultUpBeams = 16;
        public const int k_LidarDefaultDownBeams = 112;

#if GSPLAT_ENABLE_HDRP
        // --------------------------------------------------------------------
        // HDRP: A2C / MSAA 兼容
        // - HDRP 会主动把 `Camera.allowMSAA` 设成 false,因为它把这个字段当 legacy 入口.
        // - 真正的 MSAA 状态要从 HDRP 的 Frame Settings 聚合结果里取.
        // - 这里用缓存反射拿内部 `AggregateFrameSettings(...)`,避免把 LiDAR A2C 误判成不可用.
        // --------------------------------------------------------------------
        static readonly Type s_hdrpRenderingPathFrameSettingsType =
            typeof(HDAdditionalCameraData).Assembly.GetType(
                "UnityEngine.Rendering.HighDefinition.RenderingPathFrameSettings");

        static readonly MethodInfo s_tryGetRenderPipelineSettingsMethod =
            FindTryGetRenderPipelineSettingsMethod();

        static readonly MethodInfo s_tryGetHdrpRenderingPathFrameSettingsMethod =
            CreateTryGetHdrpRenderingPathFrameSettingsMethod();

        static readonly MethodInfo s_hdrpAggregateFrameSettingsMethod =
            FindHdrpAggregateFrameSettingsMethod();
#endif

        public static float Sigmoid(float x)
        {
            return 1.0f / (1.0f + Mathf.Exp(-x));
        }

        public static bool IsValidLidarParticleAntialiasingMode(GsplatLidarParticleAntialiasingMode mode)
        {
            return mode == GsplatLidarParticleAntialiasingMode.LegacySoftEdge ||
                   mode == GsplatLidarParticleAntialiasingMode.AnalyticCoverage ||
                   mode == GsplatLidarParticleAntialiasingMode.AlphaToCoverage ||
                   mode == GsplatLidarParticleAntialiasingMode.AnalyticCoveragePlusAlphaToCoverage;
        }

        public static GsplatLidarParticleAntialiasingMode SanitizeLidarParticleAntialiasingMode(
            GsplatLidarParticleAntialiasingMode mode)
        {
            return IsValidLidarParticleAntialiasingMode(mode)
                ? mode
                : GsplatLidarParticleAntialiasingMode.LegacySoftEdge;
        }

        public static bool UsesLidarParticleAnalyticCoverage(GsplatLidarParticleAntialiasingMode mode)
        {
            return mode == GsplatLidarParticleAntialiasingMode.AnalyticCoverage ||
                   mode == GsplatLidarParticleAntialiasingMode.AnalyticCoveragePlusAlphaToCoverage;
        }

        public static bool UsesLidarParticleAlphaToCoverage(GsplatLidarParticleAntialiasingMode mode)
        {
            return mode == GsplatLidarParticleAntialiasingMode.AlphaToCoverage ||
                   mode == GsplatLidarParticleAntialiasingMode.AnalyticCoveragePlusAlphaToCoverage;
        }

        public static int GetLidarParticleMsaaSampleCount(Camera camera)
        {
#if GSPLAT_ENABLE_HDRP
            if (TryGetHdrpResolvedMsaaSampleCount(camera, out var hdrpSamples))
                return hdrpSamples;
#endif

            if (!camera || !camera.allowMSAA)
                return 1;

            var targetTexture = camera.targetTexture;
            if (targetTexture)
                return Mathf.Max(targetTexture.antiAliasing, 1);

            return Mathf.Max(QualitySettings.antiAliasing, 1);
        }

        public static bool IsLidarParticleMsaaAvailable(Camera camera)
        {
            return GetLidarParticleMsaaSampleCount(camera) > 1;
        }

        public static string GetLidarParticleMsaaDiagnosticSummary(Camera camera)
        {
#if GSPLAT_ENABLE_HDRP
            if (TryGetHdrpResolvedMsaaSampleCount(camera, out var hdrpSamples))
            {
                var hdrpSource = camera && camera.targetTexture
                    ? "hdrp-frame-settings+target-texture"
                    : "hdrp-frame-settings";
                return
                    $"cameraAllowMSAA={(camera && camera.allowMSAA ? 1 : 0)} msaaSamples={hdrpSamples} msaaSource={hdrpSource}";
            }
#endif

            if (!camera)
                return "cameraAllowMSAA=0 msaaSamples=1 msaaSource=no-camera";

            if (!camera.allowMSAA)
                return "cameraAllowMSAA=0 msaaSamples=1 msaaSource=camera-allowmsaa-disabled";

            if (camera.targetTexture)
                return
                    $"cameraAllowMSAA=1 msaaSamples={Mathf.Max(camera.targetTexture.antiAliasing, 1)} msaaSource=target-texture";

            return
                $"cameraAllowMSAA=1 msaaSamples={Mathf.Max(QualitySettings.antiAliasing, 1)} msaaSource=quality-settings";
        }

        public static GsplatLidarParticleAntialiasingMode ResolveEffectiveLidarParticleAntialiasingMode(
            GsplatLidarParticleAntialiasingMode requestedMode,
            Camera camera)
        {
            requestedMode = SanitizeLidarParticleAntialiasingMode(requestedMode);
            if (!UsesLidarParticleAlphaToCoverage(requestedMode))
                return requestedMode;

            return IsLidarParticleMsaaAvailable(camera)
                ? requestedMode
                : GsplatLidarParticleAntialiasingMode.AnalyticCoverage;
        }

        public static bool IsValidLidarExternalCaptureResolutionMode(GsplatLidarExternalCaptureResolutionMode mode)
        {
            return mode == GsplatLidarExternalCaptureResolutionMode.Auto ||
                   mode == GsplatLidarExternalCaptureResolutionMode.Scale ||
                   mode == GsplatLidarExternalCaptureResolutionMode.Explicit;
        }

        public static GsplatLidarExternalCaptureResolutionMode SanitizeLidarExternalCaptureResolutionMode(
            GsplatLidarExternalCaptureResolutionMode mode)
        {
            return IsValidLidarExternalCaptureResolutionMode(mode)
                ? mode
                : GsplatLidarExternalCaptureResolutionMode.Auto;
        }

#if GSPLAT_ENABLE_HDRP
        static MethodInfo FindTryGetRenderPipelineSettingsMethod()
        {
            foreach (var method in typeof(GraphicsSettings).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != nameof(GraphicsSettings.TryGetRenderPipelineSettings) ||
                    !method.IsGenericMethodDefinition)
                {
                    continue;
                }

                var parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].IsOut)
                    return method;
            }

            return null;
        }

        static MethodInfo CreateTryGetHdrpRenderingPathFrameSettingsMethod()
        {
            if (s_tryGetRenderPipelineSettingsMethod == null || s_hdrpRenderingPathFrameSettingsType == null)
                return null;

            return s_tryGetRenderPipelineSettingsMethod.MakeGenericMethod(s_hdrpRenderingPathFrameSettingsType);
        }

        static MethodInfo FindHdrpAggregateFrameSettingsMethod()
        {
            if (s_hdrpRenderingPathFrameSettingsType == null)
                return null;

            return typeof(FrameSettings).GetMethod(
                "AggregateFrameSettings",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[]
                {
                    s_hdrpRenderingPathFrameSettingsType,
                    typeof(FrameSettings).MakeByRefType(),
                    typeof(Camera),
                    typeof(HDAdditionalCameraData),
                    typeof(HDRenderPipelineAsset)
                },
                null);
        }

        static bool TryGetHdrpResolvedMsaaSampleCount(Camera camera, out int sampleCount)
        {
            sampleCount = 1;
            if (!camera)
                return false;

            var hdrpAsset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
            if (!hdrpAsset)
                return false;

            if (!TryGetHdrpAggregatedFrameSettings(camera, hdrpAsset, out var frameSettings))
                return false;

            // 说明:
            // - HDRP 2022+ 已把旧的 `FrameSettingsField.MSAA` 迁成 `MSAAMode`.
            // - 真实项目里常见状态是:
            //   - legacy bit(`MSAA`) 仍然为 false
            //   - 但 `msaaMode=FromHDRPAsset/MSAA4X` 已经有效
            // - 如果这里继续看旧 bit,就会把已经生效的 HDRP MSAA 误判成 1x.
            sampleCount = Mathf.Max((int)frameSettings.GetResolvedMSAAMode(hdrpAsset), 1);

            // 说明:
            // - HDRP 的 Frame Settings 说明“理论上这台 camera 允许 MSAA”.
            // - 但如果实际输出到的 RenderTexture 不是多重采样,那 A2C 仍然不会成立.
            // - 因此这里再与 targetTexture 的 sample count 取 min,逼近真实 render target.
            if (camera.targetTexture)
                sampleCount = Mathf.Min(sampleCount, Mathf.Max(camera.targetTexture.antiAliasing, 1));

            return true;
        }

        static bool TryGetHdrpAggregatedFrameSettings(Camera camera, HDRenderPipelineAsset hdrpAsset,
            out FrameSettings frameSettings)
        {
            frameSettings = default;
            if (s_tryGetHdrpRenderingPathFrameSettingsMethod == null || s_hdrpAggregateFrameSettingsMethod == null)
                return false;

            object additionalData = null;
            camera.TryGetComponent(out HDAdditionalCameraData hdAdditionalCameraData);
            additionalData = hdAdditionalCameraData;

            var getSettingsArgs = new object[] { null };
            var gotSettings = (bool)s_tryGetHdrpRenderingPathFrameSettingsMethod.Invoke(null, getSettingsArgs);
            if (!gotSettings || getSettingsArgs[0] == null)
                return false;

            var aggregateArgs = new object[] { getSettingsArgs[0], frameSettings, camera, additionalData, hdrpAsset };
            s_hdrpAggregateFrameSettingsMethod.Invoke(null, aggregateArgs);
            frameSettings = (FrameSettings)aggregateArgs[1];
            return true;
        }
#endif

        // --------------------------------------------------------------------
        // LiDAR: Up/Down beams 规范化(固定总线束数)
        // - v1 目标: 固定 total=128,并允许用户用 UpBeams/DownBeams 调整“上少下多”的比例.
        // - 规则:
        //   1) clamp 到 [0,total]
        //   2) 若 sum<=0,回退到默认 16/112
        //   3) 否则按比例缩放到 sum==total(DownBeams 由 total-up 推导,保证严格相加)
        // --------------------------------------------------------------------
        public static void NormalizeLidarUpDownBeamsToFixedTotal(ref int upBeams, ref int downBeams,
            int totalBeams = k_LidarDefaultBeamCount,
            int defaultUpBeams = k_LidarDefaultUpBeams,
            int defaultDownBeams = k_LidarDefaultDownBeams)
        {
            totalBeams = Math.Max(totalBeams, 1);

            upBeams = Mathf.Clamp(upBeams, 0, totalBeams);
            downBeams = Mathf.Clamp(downBeams, 0, totalBeams);

            var sum = upBeams + downBeams;
            if (sum <= 0)
            {
                upBeams = Mathf.Clamp(defaultUpBeams, 0, totalBeams);
                downBeams = Mathf.Clamp(defaultDownBeams, 0, totalBeams);

                // 默认值也必须保证严格相加.
                sum = upBeams + downBeams;
                if (sum != totalBeams)
                {
                    upBeams = Mathf.Clamp(upBeams, 0, totalBeams);
                    downBeams = totalBeams - upBeams;
                }
                return;
            }

            if (sum == totalBeams)
                return;

            // 按比例缩放:
            // - 只保留 UpBeams 的比例,DownBeams 由 total-up 推导,避免出现 sum 偏差.
            var scale = (float)totalBeams / sum;
            upBeams = Mathf.Clamp(Mathf.RoundToInt(upBeams * scale), 0, totalBeams);
            downBeams = totalBeams - upBeams;
        }

        // --------------------------------------------------------------------
        // Easing: easeInOutQuart
        // - 用于显示风格切换(Gaussian <-> ParticleDots)的默认动画曲线.
        // - 说明: 与 shader 侧的实现一致,避免 pow(),只用乘法,减少平台差异.
        // - 标准定义:
        //   t < 0.5:  8*t^4
        //   t >= 0.5: 1 - ((-2*t + 2)^4)/2
        // --------------------------------------------------------------------
        public static float EaseInOutQuart(float t)
        {
            if (float.IsNaN(t) || float.IsInfinity(t))
                t = 0.0f;

            t = Mathf.Clamp01(t);

            if (t < 0.5f)
            {
                var t2 = t * t;
                var t4 = t2 * t2;
                return 8.0f * t4;
            }

            var a = -2.0f * t + 2.0f;
            var a2 = a * a;
            var a4 = a2 * a2;
            return 1.0f - 0.5f * a4;
        }

        public const int k_PlyPropertyCountNoSH = 17;

        public static byte CalcSHBandsFromPropertyCount(int propertyCount)
        {
            return CalcSHBandsFromSHPropertyCount(propertyCount - k_PlyPropertyCountNoSH);
        }

        public static byte CalcSHBandsFromSHPropertyCount(int shPropertyCount)
        {
            return (byte)(Math.Sqrt((shPropertyCount + 3) / 3) - 1);
        }

        public static int SHBandsToCoefficientCount(byte shBands)
        {
            return (shBands + 1) * (shBands + 1) - 1;
        }

        public static Bounds CalcWorldBounds(Bounds localBounds, Transform transform)
        {
            var localCenter = localBounds.center;
            var localExtents = localBounds.extents;

            var localCorners = new[]
            {
                localCenter + new Vector3(localExtents.x, localExtents.y, localExtents.z),
                localCenter + new Vector3(localExtents.x, localExtents.y, -localExtents.z),
                localCenter + new Vector3(localExtents.x, -localExtents.y, localExtents.z),
                localCenter + new Vector3(localExtents.x, -localExtents.y, -localExtents.z),
                localCenter + new Vector3(-localExtents.x, localExtents.y, localExtents.z),
                localCenter + new Vector3(-localExtents.x, localExtents.y, -localExtents.z),
                localCenter + new Vector3(-localExtents.x, -localExtents.y, localExtents.z),
                localCenter + new Vector3(-localExtents.x, -localExtents.y, -localExtents.z)
            };

            var worldBounds = new Bounds(transform.TransformPoint(localCorners[0]), Vector3.zero);
            for (var i = 1; i < 8; i++)
                worldBounds.Encapsulate(transform.TransformPoint(localCorners[i]));

            return worldBounds;
        }

        public static Bounds TransformBounds(Bounds sourceBounds, Matrix4x4 matrix)
        {
            var sourceCenter = sourceBounds.center;
            var sourceExtents = sourceBounds.extents;

            var sourceCorners = new[]
            {
                sourceCenter + new Vector3(sourceExtents.x, sourceExtents.y, sourceExtents.z),
                sourceCenter + new Vector3(sourceExtents.x, sourceExtents.y, -sourceExtents.z),
                sourceCenter + new Vector3(sourceExtents.x, -sourceExtents.y, sourceExtents.z),
                sourceCenter + new Vector3(sourceExtents.x, -sourceExtents.y, -sourceExtents.z),
                sourceCenter + new Vector3(-sourceExtents.x, sourceExtents.y, sourceExtents.z),
                sourceCenter + new Vector3(-sourceExtents.x, sourceExtents.y, -sourceExtents.z),
                sourceCenter + new Vector3(-sourceExtents.x, -sourceExtents.y, sourceExtents.z),
                sourceCenter + new Vector3(-sourceExtents.x, -sourceExtents.y, -sourceExtents.z)
            };

            var transformedBounds = new Bounds(matrix.MultiplyPoint3x4(sourceCorners[0]), Vector3.zero);
            for (var i = 1; i < sourceCorners.Length; i++)
                transformedBounds.Encapsulate(matrix.MultiplyPoint3x4(sourceCorners[i]));

            return transformedBounds;
        }

        public static void BuildRigidTransformMatrices(Transform transform,
            out Matrix4x4 localToWorld,
            out Matrix4x4 worldToLocal)
        {
            // LiDAR 传感器坐标系必须只保留平移 + 旋转:
            // - range image / external hit 里的距离语义都是“世界射线距离”.
            // - 如果把节点缩放混进传感器矩阵,最终用 `dir * range` 重建世界点位时,
            //   external hit 会被按缩放倍数再次推远,看起来就会离开 mesh 表面.
            localToWorld = Matrix4x4.identity;
            worldToLocal = Matrix4x4.identity;
            if (!transform)
                return;

            localToWorld = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            worldToLocal = localToWorld.inverse;
        }

        // --------------------------------------------------------------------
        // GPU 资源预算估算(粗略)
        // - 目的: 在创建 GraphicsBuffer 之前给出可读的显存占用提示,让失败是可解释的.
        // - 注意: 这是估算值,不包含驱动/对齐/内部开销,但足够用于风险提示与自动降级决策.
        // --------------------------------------------------------------------
        public static long EstimateGpuBytes(uint splatCount, byte shBands, bool has4D)
        {
            // 核心渲染 buffers
            long bytes = 0;
            bytes += (long)splatCount * 12; // Position: float3
            bytes += (long)splatCount * 12; // Scale: float3
            bytes += (long)splatCount * 16; // Rotation: float4(wxyz)
            bytes += (long)splatCount * 16; // Color: float4(f_dc rgb + opacity)
            bytes += (long)splatCount * 4; // Order: uint

            if (shBands > 0)
            {
                var coeffs = SHBandsToCoefficientCount(shBands);
                bytes += (long)splatCount * coeffs * 12; // SH: float3 * coeffs
            }

            if (has4D)
            {
                bytes += (long)splatCount * 12; // Velocity: float3
                bytes += (long)splatCount * 4; // Time: float
                bytes += (long)splatCount * 4; // Duration: float
            }

            // GPU radix sort buffers(与当前实现对齐)
            bytes += (long)splatCount * 4; // InputKeys: uint
            bytes += (long)splatCount * 4; // AltBuffer: uint
            bytes += (long)splatCount * 4; // AltPayloadBuffer: uint

            const long partitionSize = 3840;
            const long radix = 256;
            const long passes = 4;

            var threadBlocks = (splatCount + partitionSize - 1) / partitionSize;
            var passHistCount = threadBlocks * radix;
            var globalHistCount = radix * passes;
            bytes += passHistCount * 4; // PassHistBuffer: uint
            bytes += globalHistCount * 4; // GlobalHistBuffer: uint

            return bytes;
        }

        // --------------------------------------------------------------------
        // `.sog4d`(keyframe) 的 GPU 资源预算估算(粗略)
        // - 目的: 在创建 GraphicsBuffer/Texture2DArray 之前给出可读提示,并为自动降级提供依据.
        // - 注意: 这是估算值,不包含纹理/驱动/对齐的内部开销,但足够做风险分级.
        // --------------------------------------------------------------------
        public static long EstimateSog4dGpuBytes(uint splatCount, int frameCount, int width, int height,
            byte effectiveShBands, int scaleCodebookCount, int shRestLabelStreamCount, int shRestCentroidsVecCount)
        {
            // 1) 复用现有后端的 float buffers + radix sort buffers 估算.
            long bytes = EstimateGpuBytes(splatCount, effectiveShBands, has4D: false);

            // 2) 量化 streams: Texture2DArray(RGBA32,无压缩).
            // - position_hi/lo + scale_indices + rotation + sh0 + shRestLabels
            // - v1: shRestLabels 为 1 张 labels(Texture2DArray)
            // - v2: shRestLabels 为 sh1/sh2/sh3 三张 labels(Texture2DArray)
            var streams = 5 + Mathf.Max(0, shRestLabelStreamCount);
            var layerBytes = (long)width * height * 4;
            bytes += (long)frameCount * layerBytes * streams;

            // 3) codebook/palette 常驻 buffers
            bytes += (long)scaleCodebookCount * 12; // float3
            bytes += 256L * 4; // sh0Codebook: float

            // SH rest palette:
            // - 运行时为了简化与稳定,会把 centroids(f16/f32)统一解码成 float3 GraphicsBuffer.
            // - 因此预算按 “float3 entry 数量 * 12 bytes” 估算.
            bytes += (long)Mathf.Max(0, shRestCentroidsVecCount) * 12;

            return bytes;
        }

        public static float BytesToMiB(long bytes)
        {
            return bytes / (1024.0f * 1024.0f);
        }
    }
}
