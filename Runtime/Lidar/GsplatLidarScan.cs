// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    /// <summary>
    /// LiDAR 的离散采样布局.
    ///
    /// 设计目的:
    /// - 把“active cell 数量”和“真实角域范围”绑成一个整体.
    /// - 让 360 模式与 frustum 模式共用同一套 buffer/LUT/compute/draw 入口.
    /// - 避免外层只改了 count,却漏改角域或 cell 映射,导致语义撕裂.
    /// </summary>
    internal readonly struct GsplatLidarLayout
    {
        const float k_minSpanRad = 1.0e-6f;

        public GsplatLidarApertureMode ApertureMode { get; }
        public int ActiveAzimuthBins { get; }
        public int ActiveBeamCount { get; }
        public float AzimuthMinRad { get; }
        public float AzimuthMaxRad { get; }
        public float BeamMinRad { get; }
        public float BeamMaxRad { get; }

        public bool IsFrustum => ApertureMode == GsplatLidarApertureMode.CameraFrustum;
        public int CellCount => Mathf.Max(ActiveAzimuthBins * ActiveBeamCount, 1);
        public float AzimuthSpanRad => Mathf.Max(AzimuthMaxRad - AzimuthMinRad, k_minSpanRad);
        public float BeamSpanRad => Mathf.Max(BeamMaxRad - BeamMinRad, k_minSpanRad);

        GsplatLidarLayout(GsplatLidarApertureMode apertureMode,
            int activeAzimuthBins, int activeBeamCount,
            float azimuthMinRad, float azimuthMaxRad,
            float beamMinRad, float beamMaxRad)
        {
            ApertureMode = apertureMode;
            ActiveAzimuthBins = Mathf.Max(activeAzimuthBins, 1);
            ActiveBeamCount = Mathf.Max(activeBeamCount, 1);

            if (!IsFinite(azimuthMinRad))
                azimuthMinRad = -Mathf.PI;
            if (!IsFinite(azimuthMaxRad))
                azimuthMaxRad = Mathf.PI;
            if (!IsFinite(beamMinRad))
                beamMinRad = -0.5f;
            if (!IsFinite(beamMaxRad))
                beamMaxRad = 0.5f;

            if (azimuthMaxRad - azimuthMinRad < k_minSpanRad)
                azimuthMaxRad = azimuthMinRad + k_minSpanRad;
            if (beamMaxRad - beamMinRad < k_minSpanRad)
                beamMaxRad = beamMinRad + k_minSpanRad;

            AzimuthMinRad = azimuthMinRad;
            AzimuthMaxRad = azimuthMaxRad;
            BeamMinRad = beamMinRad;
            BeamMaxRad = beamMaxRad;
        }

        public static GsplatLidarLayout CreateSurround360(int azimuthBins, int beamCount,
            float upFovDeg, float downFovDeg)
        {
            return new GsplatLidarLayout(
                GsplatLidarApertureMode.Surround360,
                Mathf.Max(azimuthBins, 1),
                Mathf.Max(beamCount, 1),
                -Mathf.PI,
                Mathf.PI,
                downFovDeg * Mathf.Deg2Rad,
                upFovDeg * Mathf.Deg2Rad);
        }

        public static bool TryCreateCameraFrustum(Camera frustumCamera,
            int baseAzimuthBins, int baseBeamCount,
            float baselineUpFovDeg, float baselineDownFovDeg,
            out GsplatLidarLayout layout, out string invalidReason)
        {
            layout = CreateSurround360(baseAzimuthBins, baseBeamCount, baselineUpFovDeg, baselineDownFovDeg);
            invalidReason = null;

            if (!frustumCamera)
            {
                invalidReason = "LidarFrustumCamera 为空或已经失效";
                return false;
            }

            if (frustumCamera.orthographic)
            {
                invalidReason = "当前只支持 perspective camera, orthographic camera 不能作为 LiDAR frustum aperture";
                return false;
            }

            var pixelRect = frustumCamera.pixelRect;
            var hasValidPixelRect = IsFinite(pixelRect.width) && IsFinite(pixelRect.height) &&
                                    pixelRect.width > 0.0f && pixelRect.height > 0.0f;
            var aspect = frustumCamera.aspect;
            if (!hasValidPixelRect && (!IsFinite(aspect) || aspect <= 0.0f))
            {
                invalidReason = "camera.pixelRect 与 camera.aspect 同时无效";
                return false;
            }

            var fieldOfView = frustumCamera.fieldOfView;
            if (!IsFinite(fieldOfView) || fieldOfView <= 0.0f || fieldOfView >= 179.0f)
            {
                invalidReason = $"camera.fieldOfView 无效({fieldOfView})";
                return false;
            }

            if (!TryComputeFrustumAngleBounds(frustumCamera,
                    out var azimuthMinRad, out var azimuthMaxRad,
                    out var beamMinRad, out var beamMaxRad,
                    out invalidReason))
            {
                return false;
            }

            var activeAzimuthBins = ScaleCountKeepingDensity(baseAzimuthBins, azimuthMaxRad - azimuthMinRad,
                Mathf.PI * 2.0f);
            var baselineBeamSpanRad = Mathf.Max((baselineUpFovDeg - baselineDownFovDeg) * Mathf.Deg2Rad, k_minSpanRad);
            var activeBeamCount = ScaleCountKeepingDensity(baseBeamCount, beamMaxRad - beamMinRad,
                baselineBeamSpanRad);

            layout = new GsplatLidarLayout(
                GsplatLidarApertureMode.CameraFrustum,
                activeAzimuthBins,
                activeBeamCount,
                azimuthMinRad,
                azimuthMaxRad,
                beamMinRad,
                beamMaxRad);
            return true;
        }

        static bool TryComputeFrustumAngleBounds(Camera frustumCamera,
            out float azimuthMinRad, out float azimuthMaxRad,
            out float beamMinRad, out float beamMaxRad,
            out string invalidReason)
        {
            azimuthMinRad = float.PositiveInfinity;
            azimuthMaxRad = float.NegativeInfinity;
            beamMinRad = float.PositiveInfinity;
            beamMaxRad = float.NegativeInfinity;
            invalidReason = null;

            // 关键说明:
            // - 水平/垂直极值不一定在 corner 上.
            // - 对称透视相机里,最大 elev 往往出现在 top-center,最大 azimuth 往往出现在 left/right-center.
            // - 因此这里采样 corner + edge-center,避免只看 corner 导致 FOV 被系统性低估.
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
                var worldPoint = frustumCamera.ViewportToWorldPoint(new Vector3(sample.x, sample.y, 1.0f));
                var localDirection = frustumCamera.transform.InverseTransformPoint(worldPoint);

                if (!TryComputeAngles(localDirection, out var azimuthRad, out var beamRad))
                {
                    invalidReason = "camera frustum 角域计算失败(投影或 frustum sample 无效)";
                    return false;
                }

                azimuthMinRad = Mathf.Min(azimuthMinRad, azimuthRad);
                azimuthMaxRad = Mathf.Max(azimuthMaxRad, azimuthRad);
                beamMinRad = Mathf.Min(beamMinRad, beamRad);
                beamMaxRad = Mathf.Max(beamMaxRad, beamRad);
            }

            if (!IsFinite(azimuthMinRad) || !IsFinite(azimuthMaxRad) ||
                !IsFinite(beamMinRad) || !IsFinite(beamMaxRad))
            {
                invalidReason = "camera frustum 角域出现 NaN/Inf";
                return false;
            }

            if (azimuthMaxRad - azimuthMinRad < k_minSpanRad)
            {
                invalidReason = "camera frustum 的水平角域过小,无法生成有效 LiDAR layout";
                return false;
            }

            if (beamMaxRad - beamMinRad < k_minSpanRad)
            {
                invalidReason = "camera frustum 的垂直角域过小,无法生成有效 LiDAR layout";
                return false;
            }

            return true;
        }

        static bool TryComputeAngles(Vector3 localDirection, out float azimuthRad, out float beamRad)
        {
            azimuthRad = 0.0f;
            beamRad = 0.0f;

            if (!IsFinite(localDirection.x) || !IsFinite(localDirection.y) || !IsFinite(localDirection.z))
                return false;

            var direction = localDirection.normalized;
            if (!IsFinite(direction.x) || !IsFinite(direction.y) || !IsFinite(direction.z))
                return false;

            var horizontal = Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z);
            if (!IsFinite(horizontal))
                return false;

            azimuthRad = Mathf.Atan2(direction.x, direction.z);
            beamRad = Mathf.Atan2(direction.y, Mathf.Max(horizontal, k_minSpanRad));
            return IsFinite(azimuthRad) && IsFinite(beamRad);
        }

        static int ScaleCountKeepingDensity(int baseCount, float activeSpanRad, float baselineSpanRad)
        {
            baseCount = Mathf.Max(baseCount, 1);
            if (!IsFinite(activeSpanRad) || activeSpanRad <= 0.0f ||
                !IsFinite(baselineSpanRad) || baselineSpanRad <= 0.0f)
            {
                return 1;
            }

            var scaled = (double)baseCount * activeSpanRad / baselineSpanRad;
            if (double.IsNaN(scaled) || double.IsInfinity(scaled) || scaled <= 0.0)
                return 1;

            return Mathf.Max(1, Mathf.RoundToInt((float)scaled));
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// LiDAR 采集显示: GPU 资源与 LUT 管理器.
    ///
    /// 职责边界(很重要):
    /// - 本类只负责:
    ///   1) range image buffers(minRangeSqBits/minSplatId)的创建/释放/重建.
    ///   2) 方向 LUT buffers(azSinCos/beamSinCos)的创建与更新.
    /// - 本类不负责:
    ///   - compute dispatch(清表/归约/解析 id). 这些由上层调度(便于做 UpdateHz 门禁与测试).
    ///   - draw call 提交. 这些由上层渲染链路决定(包含 EditMode 相机回调驱动).
    /// </summary>
    internal sealed class GsplatLidarScan : IDisposable
    {
        // --------------------------------------------------------------------
        // range image:
        // - 每个 cell 对应一个 (beamIndex,azimuthBin).
        // - minRangeSqBits: float 的 bit 表示(asuint(depthSq)),用于 atomic min(只适用于非负数).
        //   - depth 的语义是: 点到“bin center 射线”的投影距离(dot(pos,dirCenter)).
        //   - 这样渲染时把值放回同一条离散射线上不会形成“厚壳”外推.
        // - minSplatId: 与 minRange 对齐的 splat id(用于 SplatColorSH0 模式).
        // --------------------------------------------------------------------
        public GraphicsBuffer MinRangeSqBitsBuffer { get; private set; }
        public GraphicsBuffer MinSplatIdBuffer { get; private set; }
        public GraphicsBuffer ExternalRangeSqBitsBuffer { get; private set; }
        public GraphicsBuffer ExternalBaseColorBuffer { get; private set; }

        // --------------------------------------------------------------------
        // LUT:
        // - azSinCos[AzimuthBins]  = (sin(az), cos(az))
        // - beamSinCos[BeamCount] = (sin(elev), cos(elev))
        // --------------------------------------------------------------------
        public GraphicsBuffer AzSinCosBuffer { get; private set; }
        public GraphicsBuffer BeamSinCosBuffer { get; private set; }

        // range image 的缓存维度:
        // - 用于判断是否需要重建 range buffers.
        int m_cachedRangeAzimuthBins;
        int m_cachedRangeBeamCount;

        // LUT 的缓存维度:
        // - 用于判断是否需要重建/重算 LUT buffers.
        int m_cachedLutAzimuthBins;
        int m_cachedLutBeamCount;

        float m_cachedLutAzimuthMinRad = float.NaN;
        float m_cachedLutAzimuthMaxRad = float.NaN;
        float m_cachedLutBeamMinRad = float.NaN;
        float m_cachedLutBeamMaxRad = float.NaN;

        Vector2[] m_azSinCosScratch;
        Vector2[] m_beamSinCosScratch;
        uint[] m_externalRangeSqBitsScratch;
        Vector4[] m_externalBaseColorScratch;

        // --------------------------------------------------------------------
        // UpdateHz 调度状态:
        // - 记录上一次成功重建 range image 的 realtime 时间点.
        // - 当参数变化/资源重建时,会把它置为 -1,强制下一次 tick 立即更新.
        // --------------------------------------------------------------------
        double m_lastRangeImageUpdateRealtime = -1.0;

        // --------------------------------------------------------------------
        // Compute dispatch 资源:
        // - LiDAR 的 compute kernel 复用 `Gsplat.compute`(与排序同一个 compute shader 资产).
        // - 这里缓存 kernel id,避免每次更新都 FindKernel.
        // --------------------------------------------------------------------
        ComputeShader m_compute;
        int m_kernelClearRangeImage = -1;
        int m_kernelReduceMinRangeSq = -1;
        int m_kernelResolveMinSplatId = -1;

        // 复用一个 CommandBuffer 来下发 compute(避免频繁 new).
        CommandBuffer m_cmd;

        // Compute 参数的 property id(避免字符串查找).
        static readonly int k_positionBuffer = Shader.PropertyToID("_PositionBuffer");
        static readonly int k_velocityBuffer = Shader.PropertyToID("_VelocityBuffer");
        static readonly int k_timeBuffer = Shader.PropertyToID("_TimeBuffer");
        static readonly int k_durationBuffer = Shader.PropertyToID("_DurationBuffer");
        static readonly int k_lidarMinRangeSqBits = Shader.PropertyToID("_LidarMinRangeSqBits");
        static readonly int k_lidarMinSplatId = Shader.PropertyToID("_LidarMinSplatId");
        static readonly int k_lidarExternalRangeSqBits = Shader.PropertyToID("_LidarExternalRangeSqBits");
        static readonly int k_lidarExternalBaseColor = Shader.PropertyToID("_LidarExternalBaseColor");
        static readonly int k_lidarMatrixModelToLidar = Shader.PropertyToID("_LidarMatrixModelToLidar");
        static readonly int k_lidarCellCount = Shader.PropertyToID("_LidarCellCount");
        static readonly int k_lidarAzimuthBins = Shader.PropertyToID("_LidarAzimuthBins");
        static readonly int k_lidarAzimuthMinRad = Shader.PropertyToID("_LidarAzimuthMinRad");
        static readonly int k_lidarAzimuthMaxRad = Shader.PropertyToID("_LidarAzimuthMaxRad");
        static readonly int k_lidarUpFovRad = Shader.PropertyToID("_LidarUpFovRad");
        static readonly int k_lidarDownFovRad = Shader.PropertyToID("_LidarDownFovRad");
        static readonly int k_lidarDepthNearSq = Shader.PropertyToID("_LidarDepthNearSq");
        static readonly int k_lidarDepthFarSq = Shader.PropertyToID("_LidarDepthFarSq");
        static readonly int k_lidarMinSplatOpacity = Shader.PropertyToID("_LidarMinSplatOpacity");
        static readonly int k_lidarSplatBaseIndex = Shader.PropertyToID("_LidarSplatBaseIndex");
        static readonly int k_lidarSplatCount = Shader.PropertyToID("_LidarSplatCount");
        static readonly int k_has4D = Shader.PropertyToID("_Has4D");
        static readonly int k_timeNormalized = Shader.PropertyToID("_TimeNormalized");
        static readonly int k_timeModel = Shader.PropertyToID("_TimeModel");
        static readonly int k_temporalCutoff = Shader.PropertyToID("_TemporalCutoff");

        const int k_lidarThreads = 256; // 与 compute shader 内的 LIDAR_GROUP_SIZE 保持一致
        const uint k_lidarInfBits = 0x7f7fffff; // float max,与 shader 侧保持一致

        // --------------------------------------------------------------------
        // Render(点云)相关:
        // - MaterialPropertyBlock + per-renderer MaterialInstance,用于 Metal 下“必绑资源”稳态.
        // --------------------------------------------------------------------
        Material m_materialInstance;
        Shader m_materialShader;
        MaterialPropertyBlock m_propertyBlock;

#if UNITY_EDITOR
        // ----------------------------------------------------------------
        // Editor 诊断:
        // - 用户反馈 "RadarScan show/hide noise 看不到变化" 时,需要先证明:
        //   1) 实际使用的 shader 是哪一份(AssetDatabase path).
        //   2) show/hide + noise 参数是否真的非 0 并进入 draw.
        // - 该日志默认不会刷屏:
        //   - 只在 show/hide 动画进行中(mode=1/2)才可能打印.
        //   - 并且有节流(同一段动画最多每 ~1s 打一次,且模式切换必打一次).
        // ----------------------------------------------------------------
        double m_debugLastLoggedShowHideRealtime = -1.0;
        int m_debugLastLoggedShowHideMode;
        float m_debugLastLoggedShowHideProgress01;
        int m_debugLastLoggedParticleAaRequestedMode = int.MinValue;
        int m_debugLastLoggedParticleAaEffectiveMode = int.MinValue;
        int m_debugLastLoggedParticleAaMsaaSamples = int.MinValue;
        int m_debugLastLoggedParticleAaCameraId = int.MinValue;
#endif

        static readonly int k_gammaToLinear = Shader.PropertyToID("_GammaToLinear");
        static readonly int k_splatInstanceSize = Shader.PropertyToID("_SplatInstanceSize");
        static readonly int k_lidarBeamCount = Shader.PropertyToID("_LidarBeamCount");
        static readonly int k_lidarMatrixL2W = Shader.PropertyToID("_LidarMatrixL2W");
        static readonly int k_lidarMatrixW2M = Shader.PropertyToID("_LidarMatrixW2M");
        static readonly int k_lidarPointRadiusPixels = Shader.PropertyToID("_LidarPointRadiusPixels");
        static readonly int k_lidarParticleAaAnalyticCoverage = Shader.PropertyToID("_LidarParticleAAAnalyticCoverage");
        static readonly int k_lidarParticleAaFringePixels = Shader.PropertyToID("_LidarParticleAAFringePixels");
        static readonly int k_lidarExternalHitBiasMeters = Shader.PropertyToID("_LidarExternalHitBiasMeters");
        static readonly int k_lidarColorMode = Shader.PropertyToID("_LidarColorMode");
        static readonly int k_lidarColorBlend = Shader.PropertyToID("_LidarColorBlend");
        static readonly int k_lidarVisibility = Shader.PropertyToID("_LidarVisibility");
        static readonly int k_lidarShowHideGate = Shader.PropertyToID("_LidarShowHideGate");
        static readonly int k_lidarShowHideMode = Shader.PropertyToID("_LidarShowHideMode");
        static readonly int k_lidarShowHideProgress = Shader.PropertyToID("_LidarShowHideProgress");
        static readonly int k_lidarShowHideSourceMaskMode = Shader.PropertyToID("_LidarShowHideSourceMaskMode");
        static readonly int k_lidarShowHideSourceMaskProgress = Shader.PropertyToID("_LidarShowHideSourceMaskProgress");
        static readonly int k_lidarShowHideCenterModel = Shader.PropertyToID("_LidarShowHideCenterModel");
        static readonly int k_lidarShowHideMaxRadius = Shader.PropertyToID("_LidarShowHideMaxRadius");
        static readonly int k_lidarShowHideRingWidth = Shader.PropertyToID("_LidarShowHideRingWidth");
        static readonly int k_lidarShowHideTrailWidth = Shader.PropertyToID("_LidarShowHideTrailWidth");
        static readonly int k_lidarShowHideNoiseMode = Shader.PropertyToID("_LidarShowHideNoiseMode");
        static readonly int k_lidarShowHideNoiseStrength = Shader.PropertyToID("_LidarShowHideNoiseStrength");
        static readonly int k_lidarShowHideNoiseScale = Shader.PropertyToID("_LidarShowHideNoiseScale");
        static readonly int k_lidarShowHideNoiseSpeed = Shader.PropertyToID("_LidarShowHideNoiseSpeed");
        static readonly int k_lidarShowHideWarpPixels = Shader.PropertyToID("_LidarShowHideWarpPixels");
        static readonly int k_lidarShowHideWarpStrength = Shader.PropertyToID("_LidarShowHideWarpStrength");
        static readonly int k_lidarShowHideGlowColor = Shader.PropertyToID("_LidarShowHideGlowColor");
        static readonly int k_lidarShowHideGlowIntensity = Shader.PropertyToID("_LidarShowHideGlowIntensity");
        static readonly int k_lidarDepthNear = Shader.PropertyToID("_LidarDepthNear");
        static readonly int k_lidarDepthFar = Shader.PropertyToID("_LidarDepthFar");
        static readonly int k_lidarRotationHz = Shader.PropertyToID("_LidarRotationHz");
        static readonly int k_lidarTrailGamma = Shader.PropertyToID("_LidarTrailGamma");
        static readonly int k_lidarIntensity = Shader.PropertyToID("_LidarIntensity");
        static readonly int k_lidarUnscannedIntensity = Shader.PropertyToID("_LidarUnscannedIntensity");
        static readonly int k_lidarIntensityDistanceDecay = Shader.PropertyToID("_LidarIntensityDistanceDecay");
        static readonly int k_lidarUnscannedIntensityDistanceDecay =
            Shader.PropertyToID("_LidarUnscannedIntensityDistanceDecay");
        static readonly int k_lidarIntensityDistanceDecayMode = Shader.PropertyToID("_LidarIntensityDistanceDecayMode");
        static readonly int k_lidarDepthOpacity = Shader.PropertyToID("_LidarDepthOpacity");
        static readonly int k_lidarTime = Shader.PropertyToID("_LidarTime");
        static readonly int k_lidarAzSinCos = Shader.PropertyToID("_LidarAzSinCos");
        static readonly int k_lidarBeamSinCos = Shader.PropertyToID("_LidarBeamSinCos");
        static readonly int k_colorBuffer = Shader.PropertyToID("_ColorBuffer");

        public void Dispose()
        {
            DisposeRangeImageBuffers();
            DisposeLutBuffers();

            if (m_cmd != null)
            {
                m_cmd.Release();
                m_cmd = null;
            }

            m_compute = null;
            m_kernelClearRangeImage = -1;
            m_kernelReduceMinRangeSq = -1;
            m_kernelResolveMinSplatId = -1;

            DisposeMaterialInstance();
        }

        public bool RangeImageValid =>
            MinRangeSqBitsBuffer != null && MinRangeSqBitsBuffer.IsValid() &&
            MinSplatIdBuffer != null && MinSplatIdBuffer.IsValid() &&
            ExternalRangeSqBitsBuffer != null && ExternalRangeSqBitsBuffer.IsValid() &&
            ExternalBaseColorBuffer != null && ExternalBaseColorBuffer.IsValid();

        public bool LutValid =>
            AzSinCosBuffer != null && AzSinCosBuffer.IsValid() &&
            BeamSinCosBuffer != null && BeamSinCosBuffer.IsValid();

        public int RangeCellCount => RangeImageValid ? Mathf.Max(MinRangeSqBitsBuffer.count, 0) : 0;

        public void EnsureRangeImageBuffers(in GsplatLidarLayout layout)
        {
            // 说明:
            // - buffer 的尺寸规则是: cellCount = beamCount * azimuthBins.
            // - 当任意一个维度变化时,必须重建 buffer,否则会出现越界/旧数据残留.
            var azimuthBins = Mathf.Max(layout.ActiveAzimuthBins, 1);
            var beamCount = Mathf.Max(layout.ActiveBeamCount, 1);

            var cellCount = azimuthBins * beamCount;
            if (cellCount <= 0)
                cellCount = 1;

            var needRecreate = !RangeImageValid ||
                               m_cachedRangeAzimuthBins != azimuthBins ||
                               m_cachedRangeBeamCount != beamCount;

            if (!needRecreate)
                return;

            DisposeRangeImageBuffers();

            // 注意:
            // - 这里使用 GraphicsBuffer 而不是 ComputeBuffer,与主后端保持一致.
            // - stride=4 bytes(uint).
            MinRangeSqBitsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(uint));
            MinSplatIdBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(uint));
            ExternalRangeSqBitsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(uint));
            ExternalBaseColorBuffer =
                new GraphicsBuffer(GraphicsBuffer.Target.Structured, cellCount, sizeof(float) * 4);

            m_cachedRangeAzimuthBins = azimuthBins;
            m_cachedRangeBeamCount = beamCount;

            // buffer 发生变化时强制下一次更新.
            m_lastRangeImageUpdateRealtime = -1.0;

            // external hit buffer 不依赖 compute clear.
            // - 因此在重建时先主动填成“无命中(inf + 黑色)”,避免首次渲染读到脏数据.
            ClearExternalHits(cellCount);
        }

        public void EnsureRangeImageBuffers(int azimuthBins, int beamCount)
        {
            EnsureRangeImageBuffers(GsplatLidarLayout.CreateSurround360(azimuthBins, beamCount, 0.0f, 0.0f));
        }

        public void EnsureLutBuffers(in GsplatLidarLayout layout)
        {
            // 说明:
            // - LUT 的尺寸规则:
            //   - azSinCos: azimuthBins
            //   - beamSinCos: beamCount
            // - LUT 的内容规则:
            //   - az: [azimuthMinRad,azimuthMaxRad] 的 bin center.
            //   - beam: [beamMinRad,beamMaxRad] 的 bin center.
            var azimuthBins = Mathf.Max(layout.ActiveAzimuthBins, 1);
            var beamCount = Mathf.Max(layout.ActiveBeamCount, 1);

            var needRecreateBuffers = !LutValid ||
                                      AzSinCosBuffer.count != azimuthBins ||
                                      BeamSinCosBuffer.count != beamCount;

            if (needRecreateBuffers)
            {
                DisposeLutBuffers();
                AzSinCosBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, azimuthBins,
                    sizeof(float) * 2);
                BeamSinCosBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, beamCount,
                    sizeof(float) * 2);
            }

            var needRegen = m_cachedLutAzimuthBins != azimuthBins ||
                            m_cachedLutBeamCount != beamCount ||
                            !Mathf.Approximately(m_cachedLutAzimuthMinRad, layout.AzimuthMinRad) ||
                            !Mathf.Approximately(m_cachedLutAzimuthMaxRad, layout.AzimuthMaxRad) ||
                            !Mathf.Approximately(m_cachedLutBeamMinRad, layout.BeamMinRad) ||
                            !Mathf.Approximately(m_cachedLutBeamMaxRad, layout.BeamMaxRad);

            if (!needRegen)
                return;

            EnsureAzSinCos(layout);
            EnsureBeamSinCos(layout);

            m_cachedLutAzimuthBins = azimuthBins;
            m_cachedLutBeamCount = beamCount;
            m_cachedLutAzimuthMinRad = layout.AzimuthMinRad;
            m_cachedLutAzimuthMaxRad = layout.AzimuthMaxRad;
            m_cachedLutBeamMinRad = layout.BeamMinRad;
            m_cachedLutBeamMaxRad = layout.BeamMaxRad;

            // 方向映射发生变化时,range image 的语义也改变了,下一次应立即重建.
            m_lastRangeImageUpdateRealtime = -1.0;
        }

        public void EnsureLutBuffers(int azimuthBins, float upFovDeg, float downFovDeg, int beamCount)
        {
            EnsureLutBuffers(GsplatLidarLayout.CreateSurround360(azimuthBins, beamCount, upFovDeg, downFovDeg));
        }

        // --------------------------------------------------------------------
        // UpdateHz 调度: 纯逻辑(不依赖 GPU),便于 EditMode tests 验证.
        // --------------------------------------------------------------------
        public bool IsRangeImageUpdateDue(double nowRealtime, float updateHz)
        {
            if (double.IsNaN(nowRealtime) || double.IsInfinity(nowRealtime))
                return true;

            if (float.IsNaN(updateHz) || float.IsInfinity(updateHz) || updateHz <= 0.0f)
                updateHz = 10.0f;

            var interval = 1.0 / updateHz;
            if (interval <= 0.0 || double.IsNaN(interval) || double.IsInfinity(interval))
                interval = 0.1;

            // now 回退(例如域重载/时间基重置)时,视为需要立即更新.
            if (m_lastRangeImageUpdateRealtime < 0.0 || nowRealtime < m_lastRangeImageUpdateRealtime)
                return true;

            return (nowRealtime - m_lastRangeImageUpdateRealtime) >= interval;
        }

        public void MarkRangeImageUpdated(double nowRealtime)
        {
            if (double.IsNaN(nowRealtime) || double.IsInfinity(nowRealtime))
                nowRealtime = 0.0;

            m_lastRangeImageUpdateRealtime = nowRealtime;
        }

        public void ForceRangeImageUpdateDue()
        {
            m_lastRangeImageUpdateRealtime = -1.0;
        }

        public void UploadExternalHits(uint[] externalRangeSqBits, Vector4[] externalBaseColors, int cellCount)
        {
            if (!RangeImageValid)
                return;

            cellCount = Mathf.Clamp(cellCount, 0, ExternalRangeSqBitsBuffer.count);
            if (cellCount <= 0)
                return;

            if (externalRangeSqBits == null || externalBaseColors == null ||
                externalRangeSqBits.Length < cellCount || externalBaseColors.Length < cellCount)
            {
                ClearExternalHits(cellCount);
                return;
            }

            ExternalRangeSqBitsBuffer.SetData(externalRangeSqBits, 0, 0, cellCount);
            ExternalBaseColorBuffer.SetData(externalBaseColors, 0, 0, cellCount);
        }

        public void ClearExternalHits(int cellCount)
        {
            if (ExternalRangeSqBitsBuffer == null || !ExternalRangeSqBitsBuffer.IsValid() ||
                ExternalBaseColorBuffer == null || !ExternalBaseColorBuffer.IsValid())
            {
                return;
            }

            cellCount = Mathf.Clamp(cellCount, 0, ExternalRangeSqBitsBuffer.count);
            if (cellCount <= 0)
                return;

            EnsureExternalHitScratch(cellCount);
            for (var i = 0; i < cellCount; i++)
            {
                m_externalRangeSqBitsScratch[i] = k_lidarInfBits;
                m_externalBaseColorScratch[i] = Vector4.zero;
            }

            ExternalRangeSqBitsBuffer.SetData(m_externalRangeSqBitsScratch, 0, 0, cellCount);
            ExternalBaseColorBuffer.SetData(m_externalBaseColorScratch, 0, 0, cellCount);
        }

        void EnsureExternalHitScratch(int cellCount)
        {
            if (m_externalRangeSqBitsScratch == null || m_externalRangeSqBitsScratch.Length != cellCount)
                m_externalRangeSqBitsScratch = new uint[cellCount];

            if (m_externalBaseColorScratch == null || m_externalBaseColorScratch.Length != cellCount)
                m_externalBaseColorScratch = new Vector4[cellCount];
        }

#if UNITY_EDITOR
        void TryLogShowHideDiagnostics(GsplatSettings settings,
            float showHideGate, int showHideMode, float showHideProgress,
            int showHideSourceMaskMode, float showHideSourceMaskProgress,
            float showHideMaxRadius, float showHideRingWidth, float showHideTrailWidth,
            int showHideNoiseMode, float showHideNoiseStrength, float showHideNoiseScale, float showHideNoiseSpeed,
            float showHideWarpPixels, float showHideWarpStrength,
            Color showHideGlowColor, float showHideGlowIntensity)
        {
            // 只在过渡期记录,避免正常运行刷屏.
            if (showHideMode != 1 && showHideMode != 2)
                return;

            // 强节流: 只在以下情况打印:
            // 1) mode 变化(新一段动画开始).
            // 2) progress 显著回退(新一段动画开始).
            // 3) 距离上次打印超过 1s(用于长动画抽样).
            var now = (double)Time.realtimeSinceStartup;
            var progress01 = Mathf.Clamp01(showHideProgress);

            var progressBackJump = progress01 + 0.25f < m_debugLastLoggedShowHideProgress01;
            var modeChanged = showHideMode != m_debugLastLoggedShowHideMode;
            var timeDue = m_debugLastLoggedShowHideRealtime < 0.0 || now - m_debugLastLoggedShowHideRealtime > 1.0;

            if (!modeChanged && !progressBackJump && !timeDue)
                return;

            m_debugLastLoggedShowHideRealtime = now;
            m_debugLastLoggedShowHideMode = showHideMode;
            m_debugLastLoggedShowHideProgress01 = progress01;

            var shader = settings ? settings.LidarShader : null;
            var shaderPath = shader ? UnityEditor.AssetDatabase.GetAssetPath(shader) : "null";
            var settingsPath = settings ? UnityEditor.AssetDatabase.GetAssetPath(settings) : "null";
            var matShader = m_materialInstance ? m_materialInstance.shader : null;
            var matShaderPath = matShader ? UnityEditor.AssetDatabase.GetAssetPath(matShader) : "null";

            var hasNoiseStrengthProp = (m_materialInstance && m_materialInstance.HasProperty(k_lidarShowHideNoiseStrength))
                ? 1
                : 0;
            var hasNoiseModeProp = (m_materialInstance && m_materialInstance.HasProperty(k_lidarShowHideNoiseMode))
                ? 1
                : 0;
            var hasWarpPixelsProp = (m_materialInstance && m_materialInstance.HasProperty(k_lidarShowHideWarpPixels))
                ? 1
                : 0;
            var hasWarpStrengthProp = (m_materialInstance && m_materialInstance.HasProperty(k_lidarShowHideWarpStrength))
                ? 1
                : 0;
            var hasGlowColorProp = (m_materialInstance && m_materialInstance.HasProperty(k_lidarShowHideGlowColor)) ? 1 : 0;
            var hasGlowIntensityProp =
                (m_materialInstance && m_materialInstance.HasProperty(k_lidarShowHideGlowIntensity)) ? 1 : 0;
            var hasGateProp = (m_materialInstance && m_materialInstance.HasProperty(k_lidarShowHideGate)) ? 1 : 0;
            var hasModeProp = (m_materialInstance && m_materialInstance.HasProperty(k_lidarShowHideMode)) ? 1 : 0;
            var hasProgressProp = (m_materialInstance && m_materialInstance.HasProperty(k_lidarShowHideProgress)) ? 1 : 0;
            var hasVisibilityProp = (m_materialInstance && m_materialInstance.HasProperty(k_lidarVisibility)) ? 1 : 0;
            var hasIntensityProp = (m_materialInstance && m_materialInstance.HasProperty(k_lidarIntensity)) ? 1 : 0;
            var hasDepthOpacityProp = (m_materialInstance && m_materialInstance.HasProperty(k_lidarDepthOpacity)) ? 1 : 0;

            Debug.Log(
                "[Gsplat][LiDAR][ShowHideDiag] " +
                $"settings={settingsPath} " +
                $"shader={DescribeShader(shader)} shaderPath={shaderPath} " +
                $"matShader={DescribeShader(matShader)} matShaderPath={matShaderPath} " +
                $"gate={showHideGate:0.###} mode={showHideMode} p={progress01:0.###} " +
                $"srcMode={showHideSourceMaskMode} srcP={Mathf.Clamp01(showHideSourceMaskProgress):0.###} " +
                $"noise(mode={showHideNoiseMode} str={showHideNoiseStrength:0.###} scale={showHideNoiseScale:0.###} spd={showHideNoiseSpeed:0.###} warpPx={showHideWarpPixels:0.###} warpStr={showHideWarpStrength:0.###}) " +
                $"glow(col={showHideGlowColor.r:0.###},{showHideGlowColor.g:0.###},{showHideGlowColor.b:0.###} inten={showHideGlowIntensity:0.###}) " +
                $"shape(maxR={showHideMaxRadius:0.###} ringW={showHideRingWidth:0.###} trailW={showHideTrailWidth:0.###}) " +
                $"hasProp(gate={hasGateProp} mode={hasModeProp} p={hasProgressProp} " +
                $"vis={hasVisibilityProp} inten={hasIntensityProp} depOp={hasDepthOpacityProp} " +
                $"noiseMode={hasNoiseModeProp} noiseStr={hasNoiseStrengthProp} warpPx={hasWarpPixelsProp} warpStr={hasWarpStrengthProp} " +
                $"glowCol={hasGlowColorProp} glowI={hasGlowIntensityProp})");
        }

        static string DescribeShader(Shader shader)
        {
            if (!shader)
                return "null";
            return $"{shader.name}#{shader.GetInstanceID()}";
        }

        void TryLogParticleAntialiasingDiagnostics(Camera camera,
            GsplatLidarParticleAntialiasingMode requestedMode,
            GsplatLidarParticleAntialiasingMode effectiveMode,
            float fringePixels)
        {
            if (requestedMode == GsplatLidarParticleAntialiasingMode.LegacySoftEdge)
                return;

            var cameraId = camera ? camera.GetInstanceID() : 0;
            var msaaSamples = GetEffectiveMsaaSampleCount(camera);
            var msaaDiagnostics = GsplatUtils.GetLidarParticleMsaaDiagnosticSummary(camera);
            if (m_debugLastLoggedParticleAaRequestedMode == (int)requestedMode &&
                m_debugLastLoggedParticleAaEffectiveMode == (int)effectiveMode &&
                m_debugLastLoggedParticleAaMsaaSamples == msaaSamples &&
                m_debugLastLoggedParticleAaCameraId == cameraId)
            {
                return;
            }

            m_debugLastLoggedParticleAaRequestedMode = (int)requestedMode;
            m_debugLastLoggedParticleAaEffectiveMode = (int)effectiveMode;
            m_debugLastLoggedParticleAaMsaaSamples = msaaSamples;
            m_debugLastLoggedParticleAaCameraId = cameraId;

            var cameraName = camera ? camera.name : "<null-camera>";
            var materialShader = m_materialInstance ? m_materialInstance.shader : null;
            var shaderName = materialShader ? materialShader.name : "<null-shader>";
            var analyticCoverageEnabled = GsplatUtils.UsesLidarParticleAnalyticCoverage(effectiveMode) ? 1 : 0;
            var coverageMode = GsplatUtils.UsesLidarParticleAlphaToCoverage(effectiveMode) ? "coverage-first" : "alpha-blend";

            if (GsplatUtils.UsesLidarParticleAlphaToCoverage(requestedMode) && effectiveMode != requestedMode)
            {
                Debug.LogWarning(
                    "[Gsplat][LiDAR][AA] " +
                    $"camera={cameraName} requested={requestedMode} effective={effectiveMode}. " +
                    $"A2C 当前未生效,已回退到 {effectiveMode}. " +
                    $"{msaaDiagnostics} " +
                    $"shader={shaderName} analytic={analyticCoverageEnabled} fringePx={Mathf.Max(fringePixels, 0.0f):0.###} passMode={coverageMode}.");
                return;
            }

            Debug.Log(
                "[Gsplat][LiDAR][AA] " +
                $"camera={cameraName} requested={requestedMode} effective={effectiveMode}. " +
                $"{msaaDiagnostics} " +
                $"shader={shaderName} analytic={analyticCoverageEnabled} fringePx={Mathf.Max(fringePixels, 0.0f):0.###} passMode={coverageMode}.");
        }

        static int GetEffectiveMsaaSampleCount(Camera camera)
        {
            return GsplatUtils.GetLidarParticleMsaaSampleCount(camera);
        }
#endif

        // --------------------------------------------------------------------
        // Render: 规则点云绘制
        // --------------------------------------------------------------------
        public bool RenderPointCloud(GsplatSettings settings, Camera camera, int layer, bool gammaToLinear,
            in GsplatLidarLayout layout,
            Matrix4x4 lidarLocalToWorld, float lidarTime, float rotationHz,
            float depthNear, float depthFar, float pointRadiusPixels, float particleAaFringePixels,
            GsplatLidarParticleAntialiasingMode requestedParticleAntialiasingMode,
            GsplatLidarParticleAntialiasingMode effectiveParticleAntialiasingMode,
            GsplatLidarColorMode colorMode, float colorBlend01, float visibility01,
            float trailGamma, float intensity, float unscannedIntensity,
            float intensityDistanceDecay, float unscannedIntensityDistanceDecay, GsplatLidarDistanceDecayMode intensityDistanceDecayMode,
            float depthOpacity, float externalHitBiasMeters,
            GraphicsBuffer splatColorBuffer,
            Matrix4x4 worldToModel, float showHideGate, int showHideMode, float showHideProgress,
            int showHideSourceMaskMode, float showHideSourceMaskProgress,
            Vector3 showHideCenterModel, float showHideMaxRadius, float showHideRingWidth, float showHideTrailWidth,
            int showHideNoiseMode, float showHideNoiseStrength, float showHideNoiseScale, float showHideNoiseSpeed,
            float showHideWarpPixels, float showHideWarpStrength,
            Color showHideGlowColor, float showHideGlowIntensity)
        {
            if (m_lastRangeImageUpdateRealtime < 0.0)
            {
                // range image 尚未初始化(还没跑过一次 clear/reduce),此时不应渲染,避免随机内存导致的“鬼点”.
                return false;
            }

            if (!RangeImageValid || !LutValid)
                return false;

            if (!settings || !settings.Mesh || settings.SplatInstanceSize == 0 || !settings.LidarMaterial)
                return false;

            if (splatColorBuffer == null || !splatColorBuffer.IsValid())
                return false;

            requestedParticleAntialiasingMode =
                GsplatUtils.SanitizeLidarParticleAntialiasingMode(requestedParticleAntialiasingMode);
            effectiveParticleAntialiasingMode =
                GsplatUtils.SanitizeLidarParticleAntialiasingMode(effectiveParticleAntialiasingMode);

            var effectiveMaterial = settings.LidarMaterial;
            if (GsplatUtils.UsesLidarParticleAlphaToCoverage(effectiveParticleAntialiasingMode))
            {
                if (settings.LidarAlphaToCoverageMaterial)
                {
                    effectiveMaterial = settings.LidarAlphaToCoverageMaterial;
                }
                else
                {
                    effectiveParticleAntialiasingMode = GsplatLidarParticleAntialiasingMode.AnalyticCoverage;
                }
            }

#if UNITY_EDITOR
            TryLogParticleAntialiasingDiagnostics(camera, requestedParticleAntialiasingMode,
                effectiveParticleAntialiasingMode, particleAaFringePixels);
#endif

            if (!EnsureMaterialInstance(effectiveMaterial))
                return false;

            var azimuthBins = Mathf.Max(layout.ActiveAzimuthBins, 1);
            var beamCount = Mathf.Max(layout.ActiveBeamCount, 1);
            var cellCount = layout.CellCount;

            var instanceSize = Mathf.Max((int)settings.SplatInstanceSize, 1);
            var instanceCount = DivRoundUp(cellCount, instanceSize);
            if (instanceCount <= 0)
                return false;

            // world bounds(用于 CPU culling):
            // - 点云以 LiDAR 原点为中心,半径不超过 depthFar.
            var c4 = lidarLocalToWorld.GetColumn(3);
            var center = new Vector3(c4.x, c4.y, c4.z);
            var far = Mathf.Max(depthFar, 1.0f);
            var bounds = new Bounds(center, Vector3.one * (far * 2.0f));

            // MPB(标量/矩阵):
            m_propertyBlock ??= new MaterialPropertyBlock();
            m_propertyBlock.SetInt(k_gammaToLinear, gammaToLinear ? 1 : 0);
            m_propertyBlock.SetInt(k_splatInstanceSize, instanceSize);
            m_propertyBlock.SetInt(k_lidarCellCount, cellCount);
            m_propertyBlock.SetInt(k_lidarAzimuthBins, azimuthBins);
            m_propertyBlock.SetInt(k_lidarBeamCount, beamCount);
            m_propertyBlock.SetFloat(k_lidarAzimuthMinRad, layout.AzimuthMinRad);
            m_propertyBlock.SetFloat(k_lidarAzimuthMaxRad, layout.AzimuthMaxRad);
            m_propertyBlock.SetMatrix(k_lidarMatrixL2W, lidarLocalToWorld);
            m_propertyBlock.SetMatrix(k_lidarMatrixW2M, worldToModel);
            m_propertyBlock.SetFloat(k_lidarPointRadiusPixels, Mathf.Max(pointRadiusPixels, 0.0f));
            m_propertyBlock.SetFloat(k_lidarParticleAaAnalyticCoverage,
                GsplatUtils.UsesLidarParticleAnalyticCoverage(effectiveParticleAntialiasingMode) ? 1.0f : 0.0f);
            m_propertyBlock.SetFloat(k_lidarParticleAaFringePixels, Mathf.Max(particleAaFringePixels, 0.0f));
            m_propertyBlock.SetFloat(k_lidarExternalHitBiasMeters,
                (float.IsNaN(externalHitBiasMeters) || float.IsInfinity(externalHitBiasMeters) || externalHitBiasMeters < 0.0f)
                    ? 0.0f
                    : externalHitBiasMeters);
            m_propertyBlock.SetInt(k_lidarColorMode, (int)colorMode);
            m_propertyBlock.SetFloat(k_lidarColorBlend, Mathf.Clamp01(colorBlend01));
            m_propertyBlock.SetFloat(k_lidarVisibility, Mathf.Clamp01(visibility01));
            m_propertyBlock.SetFloat(k_lidarShowHideGate, Mathf.Clamp01(showHideGate));
            if (showHideMode != 1 && showHideMode != 2)
                showHideMode = 0;
            m_propertyBlock.SetInt(k_lidarShowHideMode, showHideMode);
            m_propertyBlock.SetFloat(k_lidarShowHideProgress, Mathf.Clamp01(showHideProgress));
            if (showHideSourceMaskMode < 1 || showHideSourceMaskMode > 4)
                showHideSourceMaskMode = 1;
            m_propertyBlock.SetInt(k_lidarShowHideSourceMaskMode, showHideSourceMaskMode);
            m_propertyBlock.SetFloat(k_lidarShowHideSourceMaskProgress, Mathf.Clamp01(showHideSourceMaskProgress));
            m_propertyBlock.SetVector(k_lidarShowHideCenterModel,
                new Vector4(showHideCenterModel.x, showHideCenterModel.y, showHideCenterModel.z, 0.0f));
            m_propertyBlock.SetFloat(k_lidarShowHideMaxRadius,
                (float.IsNaN(showHideMaxRadius) || float.IsInfinity(showHideMaxRadius) || showHideMaxRadius < 0.0f)
                    ? 0.0f
                    : showHideMaxRadius);
            m_propertyBlock.SetFloat(k_lidarShowHideRingWidth,
                (float.IsNaN(showHideRingWidth) || float.IsInfinity(showHideRingWidth) || showHideRingWidth < 0.0f)
                    ? 0.0f
                    : showHideRingWidth);
            m_propertyBlock.SetFloat(k_lidarShowHideTrailWidth,
                (float.IsNaN(showHideTrailWidth) || float.IsInfinity(showHideTrailWidth) || showHideTrailWidth < 0.0f)
                    ? 0.0f
                    : showHideTrailWidth);
            if (showHideNoiseMode < 0 || showHideNoiseMode > 2)
                showHideNoiseMode = 0;
            m_propertyBlock.SetInt(k_lidarShowHideNoiseMode, showHideNoiseMode);
            m_propertyBlock.SetFloat(k_lidarShowHideNoiseStrength,
                (float.IsNaN(showHideNoiseStrength) || float.IsInfinity(showHideNoiseStrength))
                    ? 0.0f
                    : Mathf.Clamp01(showHideNoiseStrength));
            m_propertyBlock.SetFloat(k_lidarShowHideNoiseScale,
                (float.IsNaN(showHideNoiseScale) || float.IsInfinity(showHideNoiseScale) || showHideNoiseScale < 0.0f)
                    ? 0.0f
                    : showHideNoiseScale);
            m_propertyBlock.SetFloat(k_lidarShowHideNoiseSpeed,
                (float.IsNaN(showHideNoiseSpeed) || float.IsInfinity(showHideNoiseSpeed) || showHideNoiseSpeed < 0.0f)
                    ? 0.0f
                    : showHideNoiseSpeed);
            m_propertyBlock.SetFloat(k_lidarShowHideWarpPixels,
                (float.IsNaN(showHideWarpPixels) || float.IsInfinity(showHideWarpPixels) || showHideWarpPixels < 0.0f)
                    ? 0.0f
                    : showHideWarpPixels);
            m_propertyBlock.SetFloat(k_lidarShowHideWarpStrength,
                (float.IsNaN(showHideWarpStrength) || float.IsInfinity(showHideWarpStrength) || showHideWarpStrength < 0.0f)
                    ? 0.0f
                    : Mathf.Clamp(showHideWarpStrength, 0.0f, 3.0f));
            m_propertyBlock.SetColor(k_lidarShowHideGlowColor, showHideGlowColor);
            m_propertyBlock.SetFloat(k_lidarShowHideGlowIntensity,
                (float.IsNaN(showHideGlowIntensity) || float.IsInfinity(showHideGlowIntensity) || showHideGlowIntensity < 0.0f)
                    ? 0.0f
                    : showHideGlowIntensity);
            m_propertyBlock.SetFloat(k_lidarDepthNear, depthNear);
            m_propertyBlock.SetFloat(k_lidarDepthFar, depthFar);
            m_propertyBlock.SetFloat(k_lidarRotationHz, rotationHz);
            m_propertyBlock.SetFloat(k_lidarTrailGamma, Mathf.Max(trailGamma, 0.0f));
            m_propertyBlock.SetFloat(k_lidarIntensity, Mathf.Max(intensity, 0.0f));
            m_propertyBlock.SetFloat(k_lidarUnscannedIntensity,
                (float.IsNaN(unscannedIntensity) || float.IsInfinity(unscannedIntensity) || unscannedIntensity < 0.0f)
                    ? 0.0f
                    : unscannedIntensity);
            m_propertyBlock.SetFloat(k_lidarIntensityDistanceDecay,
                (float.IsNaN(intensityDistanceDecay) || float.IsInfinity(intensityDistanceDecay) || intensityDistanceDecay < 0.0f)
                    ? 0.0f
                    : intensityDistanceDecay);
            m_propertyBlock.SetFloat(k_lidarUnscannedIntensityDistanceDecay,
                (float.IsNaN(unscannedIntensityDistanceDecay) || float.IsInfinity(unscannedIntensityDistanceDecay) ||
                 unscannedIntensityDistanceDecay < 0.0f)
                    ? 0.0f
                    : unscannedIntensityDistanceDecay);
            // 距离衰减模式:
            // - 只做两态选择,避免非法值(例如序列化坏值)导致意外落入 Exponential.
            // - 0: Reciprocal, 1: Exponential.
            var intensityDistanceDecayMode01 = intensityDistanceDecayMode == GsplatLidarDistanceDecayMode.Exponential ? 1.0f : 0.0f;
            m_propertyBlock.SetFloat(k_lidarIntensityDistanceDecayMode, intensityDistanceDecayMode01);
            m_propertyBlock.SetFloat(k_lidarDepthOpacity, Mathf.Clamp01(depthOpacity));
            m_propertyBlock.SetFloat(k_lidarTime, lidarTime);

            // 必绑 buffers(Metal 稳态):
            BindBuffersForRender(splatColorBuffer);

#if UNITY_EDITOR
            // 诊断: 在 Editor 里用日志证明:
            // - 我们到底在用哪一份 shader(路径).
            // - show/hide 的 noise 参数是否真的进入了 draw.
            TryLogShowHideDiagnostics(settings,
                showHideGate, showHideMode, showHideProgress,
                showHideSourceMaskMode, showHideSourceMaskProgress,
                showHideMaxRadius, showHideRingWidth, showHideTrailWidth,
                showHideNoiseMode, showHideNoiseStrength, showHideNoiseScale, showHideNoiseSpeed,
                showHideWarpPixels, showHideWarpStrength,
                showHideGlowColor, showHideGlowIntensity);
#endif

            var rp = new RenderParams(m_materialInstance)
            {
                layer = layer,
                worldBounds = bounds,
                matProps = m_propertyBlock,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
            };
            if (camera)
                rp.camera = camera;

            Graphics.RenderMeshPrimitives(rp, settings.Mesh, 0, instanceCount);
            return true;
        }

        // --------------------------------------------------------------------
        // Compute: first return range image 重建
        // - 由上层按 UpdateHz 调度调用.
        // - 当 `needsSplatId=false` 时,会跳过 ResolveMinSplatId(Depth 模式更省).
        // --------------------------------------------------------------------
        public bool TryRebuildRangeImage(ComputeShader computeShader,
            GraphicsBuffer positionBuffer,
            GraphicsBuffer velocityBuffer,
            GraphicsBuffer timeBuffer,
            GraphicsBuffer durationBuffer,
            GraphicsBuffer colorBuffer,
            int has4D,
            float timeNormalized,
            int timeModel,
            float temporalCutoff,
            float minSplatOpacity,
            Matrix4x4 modelToLidar, int splatBaseIndex, int splatCount,
            in GsplatLidarLayout layout,
            float depthNear, float depthFar,
            bool needsSplatId)
        {
            // 说明:
            // - compute 侧会用 LUT 的 bin center 方向来计算 depth(投影),以消除“厚壳”偏移.
            // - 因此这里必须确保 LUT buffers 已准备好,否则 dispatch 可能 invalid.
            if (!RangeImageValid || !LutValid)
                return false;

            if (!computeShader ||
                positionBuffer == null || !positionBuffer.IsValid() ||
                velocityBuffer == null || !velocityBuffer.IsValid() ||
                timeBuffer == null || !timeBuffer.IsValid() ||
                durationBuffer == null || !durationBuffer.IsValid() ||
                colorBuffer == null || !colorBuffer.IsValid())
                return false;

            // guard: 无图形设备/不支持 compute 时直接跳过,避免刷 error log.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsComputeShaders)
                return false;

            // 重新绑定 kernel(首次或 compute shader 变化时).
            if (!EnsureKernels(computeShader))
                return false;

            var azimuthBins = Mathf.Max(layout.ActiveAzimuthBins, 1);
            var beamCount = Mathf.Max(layout.ActiveBeamCount, 1);
            var cellCount = layout.CellCount;

            // depth gate(平方):
            depthNear = Mathf.Max(depthNear, 0.0f);
            depthFar = Mathf.Max(depthFar, 0.0f);
            if (depthFar <= depthNear)
                depthFar = depthNear + 1.0f;
            var nearSq = depthNear * depthNear;
            var farSq = depthFar * depthFar;

            // CommandBuffer:
            // - 我们不直接用 ComputeShader.Dispatch,而是走 CommandBuffer,
            //   以确保能稳定绑定 GraphicsBuffer(与主排序链路一致).
            m_cmd ??= new CommandBuffer { name = "Gsplat.LidarScan" };
            m_cmd.Clear();

            // 全局参数:
            m_cmd.SetComputeMatrixParam(computeShader, k_lidarMatrixModelToLidar, modelToLidar);
            m_cmd.SetComputeIntParam(computeShader, k_lidarCellCount, cellCount);
            m_cmd.SetComputeIntParam(computeShader, k_lidarAzimuthBins, azimuthBins);
            m_cmd.SetComputeIntParam(computeShader, k_lidarBeamCount, beamCount);
            m_cmd.SetComputeFloatParam(computeShader, k_lidarAzimuthMinRad, layout.AzimuthMinRad);
            m_cmd.SetComputeFloatParam(computeShader, k_lidarAzimuthMaxRad, layout.AzimuthMaxRad);
            m_cmd.SetComputeFloatParam(computeShader, k_lidarUpFovRad, layout.BeamMaxRad);
            m_cmd.SetComputeFloatParam(computeShader, k_lidarDownFovRad, layout.BeamMinRad);
            m_cmd.SetComputeFloatParam(computeShader, k_lidarDepthNearSq, nearSq);
            m_cmd.SetComputeFloatParam(computeShader, k_lidarDepthFarSq, farSq);
            m_cmd.SetComputeFloatParam(computeShader, k_lidarMinSplatOpacity, Mathf.Clamp01(minSplatOpacity));
            m_cmd.SetComputeIntParam(computeShader, k_lidarSplatBaseIndex, splatBaseIndex);
            m_cmd.SetComputeIntParam(computeShader, k_lidarSplatCount, splatCount);
            m_cmd.SetComputeIntParam(computeShader, k_has4D, has4D != 0 ? 1 : 0);
            m_cmd.SetComputeFloatParam(computeShader, k_timeNormalized, Mathf.Clamp01(timeNormalized));
            m_cmd.SetComputeIntParam(computeShader, k_timeModel, timeModel);
            m_cmd.SetComputeFloatParam(computeShader, k_temporalCutoff, Mathf.Max(temporalCutoff, 0.0f));

            // 1) Clear:
            m_cmd.SetComputeBufferParam(computeShader, m_kernelClearRangeImage, k_lidarMinRangeSqBits,
                MinRangeSqBitsBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelClearRangeImage, k_lidarMinSplatId, MinSplatIdBuffer);
            // Metal 稳态兜底: 声明的 LUT buffer 也绑定上.
            m_cmd.SetComputeBufferParam(computeShader, m_kernelClearRangeImage, k_lidarAzSinCos, AzSinCosBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelClearRangeImage, k_lidarBeamSinCos, BeamSinCosBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelClearRangeImage, k_positionBuffer, positionBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelClearRangeImage, k_velocityBuffer, velocityBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelClearRangeImage, k_timeBuffer, timeBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelClearRangeImage, k_durationBuffer, durationBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelClearRangeImage, k_colorBuffer, colorBuffer);
            var groupsCells = DivRoundUp(cellCount, k_lidarThreads);
            m_cmd.DispatchCompute(computeShader, m_kernelClearRangeImage, groupsCells, 1, 1);

            // 2) Reduce min range:
            m_cmd.SetComputeBufferParam(computeShader, m_kernelReduceMinRangeSq, k_positionBuffer, positionBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelReduceMinRangeSq, k_velocityBuffer, velocityBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelReduceMinRangeSq, k_timeBuffer, timeBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelReduceMinRangeSq, k_durationBuffer, durationBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelReduceMinRangeSq, k_colorBuffer, colorBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelReduceMinRangeSq, k_lidarMinRangeSqBits,
                MinRangeSqBitsBuffer);
            // Metal 稳态兜底:
            // - 即便当前 kernel 不使用某个 buffer,也尽量绑定上,避免某些平台/编译器组合出现“声明了但未绑定就 dispatch invalid”.
            m_cmd.SetComputeBufferParam(computeShader, m_kernelReduceMinRangeSq, k_lidarMinSplatId, MinSplatIdBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelReduceMinRangeSq, k_lidarAzSinCos, AzSinCosBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelReduceMinRangeSq, k_lidarBeamSinCos, BeamSinCosBuffer);
            var groupsSplats = DivRoundUp(Mathf.Max(splatCount, 1), k_lidarThreads);
            m_cmd.DispatchCompute(computeShader, m_kernelReduceMinRangeSq, groupsSplats, 1, 1);

            // 3) Resolve min splat id(仅在需要颜色时):
            if (needsSplatId)
            {
                m_cmd.SetComputeBufferParam(computeShader, m_kernelResolveMinSplatId, k_positionBuffer, positionBuffer);
                m_cmd.SetComputeBufferParam(computeShader, m_kernelResolveMinSplatId, k_velocityBuffer, velocityBuffer);
                m_cmd.SetComputeBufferParam(computeShader, m_kernelResolveMinSplatId, k_timeBuffer, timeBuffer);
                m_cmd.SetComputeBufferParam(computeShader, m_kernelResolveMinSplatId, k_durationBuffer, durationBuffer);
                m_cmd.SetComputeBufferParam(computeShader, m_kernelResolveMinSplatId, k_colorBuffer, colorBuffer);
                m_cmd.SetComputeBufferParam(computeShader, m_kernelResolveMinSplatId, k_lidarMinRangeSqBits,
                    MinRangeSqBitsBuffer);
                m_cmd.SetComputeBufferParam(computeShader, m_kernelResolveMinSplatId, k_lidarMinSplatId,
                    MinSplatIdBuffer);
                m_cmd.SetComputeBufferParam(computeShader, m_kernelResolveMinSplatId, k_lidarAzSinCos, AzSinCosBuffer);
                m_cmd.SetComputeBufferParam(computeShader, m_kernelResolveMinSplatId, k_lidarBeamSinCos,
                    BeamSinCosBuffer);
                m_cmd.DispatchCompute(computeShader, m_kernelResolveMinSplatId, groupsSplats, 1, 1);
            }

            Graphics.ExecuteCommandBuffer(m_cmd);
            return true;
        }

        void EnsureAzSinCos(in GsplatLidarLayout layout)
        {
            var azimuthBins = Mathf.Max(layout.ActiveAzimuthBins, 1);
            if (m_azSinCosScratch == null || m_azSinCosScratch.Length != azimuthBins)
                m_azSinCosScratch = new Vector2[azimuthBins];

            // 使用 bin center:
            // - 这样 compute(把角度映射到 bin)与 render(从 bin 还原方向)更一致.
            // - 角域: [AzimuthMinRad, AzimuthMaxRad]
            var inv = 1.0f / azimuthBins;
            var azimuthSpanRad = layout.AzimuthSpanRad;
            for (var i = 0; i < azimuthBins; i++)
            {
                var t = (i + 0.5f) * inv;
                var az = layout.AzimuthMinRad + t * azimuthSpanRad;
                m_azSinCosScratch[i] = new Vector2(Mathf.Sin(az), Mathf.Cos(az));
            }

            AzSinCosBuffer.SetData(m_azSinCosScratch, 0, 0, azimuthBins);
        }

        void EnsureBeamSinCos(in GsplatLidarLayout layout)
        {
            var beamCount = Mathf.Max(layout.ActiveBeamCount, 1);
            if (m_beamSinCosScratch == null || m_beamSinCosScratch.Length != beamCount)
                m_beamSinCosScratch = new Vector2[beamCount];

            // 注意:
            // - 这里用整体的 bin center 匀角度采样,覆盖 [BeamMinRad..BeamMaxRad].
            var denom = layout.BeamSpanRad;
            var inv = 1.0f / beamCount;
            for (var i = 0; i < beamCount; i++)
            {
                var t = (i + 0.5f) * inv;
                var el = layout.BeamMinRad + t * denom;
                m_beamSinCosScratch[i] = new Vector2(Mathf.Sin(el), Mathf.Cos(el));
            }

            BeamSinCosBuffer.SetData(m_beamSinCosScratch, 0, 0, beamCount);
        }

        void DisposeRangeImageBuffers()
        {
            MinRangeSqBitsBuffer?.Dispose();
            MinSplatIdBuffer?.Dispose();
            ExternalRangeSqBitsBuffer?.Dispose();
            ExternalBaseColorBuffer?.Dispose();
            MinRangeSqBitsBuffer = null;
            MinSplatIdBuffer = null;
            ExternalRangeSqBitsBuffer = null;
            ExternalBaseColorBuffer = null;
            m_externalRangeSqBitsScratch = null;
            m_externalBaseColorScratch = null;

            // 注意:
            // - range image 的缓存维度只在 buffer 存在时才有意义.
            // - 释放后把它们清掉,避免上层误判“尺寸未变”而跳过重建.
            m_cachedRangeAzimuthBins = 0;
            m_cachedRangeBeamCount = 0;
            m_lastRangeImageUpdateRealtime = -1.0;
        }

        void DisposeLutBuffers()
        {
            AzSinCosBuffer?.Dispose();
            BeamSinCosBuffer?.Dispose();
            AzSinCosBuffer = null;
            BeamSinCosBuffer = null;

            m_azSinCosScratch = null;
            m_beamSinCosScratch = null;

            m_cachedLutAzimuthBins = 0;
            m_cachedLutBeamCount = 0;
            m_cachedLutAzimuthMinRad = float.NaN;
            m_cachedLutAzimuthMaxRad = float.NaN;
            m_cachedLutBeamMinRad = float.NaN;
            m_cachedLutBeamMaxRad = float.NaN;
        }

        static int DivRoundUp(int x, int d)
        {
            if (d <= 0)
                return 0;
            return (x + d - 1) / d;
        }

        bool EnsureKernels(ComputeShader computeShader)
        {
            if (!computeShader)
            {
                m_compute = null;
                m_kernelClearRangeImage = -1;
                m_kernelReduceMinRangeSq = -1;
                m_kernelResolveMinSplatId = -1;
                return false;
            }

            if (m_compute == computeShader &&
                m_kernelClearRangeImage >= 0 &&
                m_kernelReduceMinRangeSq >= 0 &&
                m_kernelResolveMinSplatId >= 0)
            {
                return true;
            }

            m_compute = computeShader;
            try
            {
                m_kernelClearRangeImage = computeShader.FindKernel("ClearRangeImage");
                m_kernelReduceMinRangeSq = computeShader.FindKernel("ReduceMinRangeSq");
                m_kernelResolveMinSplatId = computeShader.FindKernel("ResolveMinSplatId");
            }
            catch (Exception)
            {
                m_kernelClearRangeImage = -1;
                m_kernelReduceMinRangeSq = -1;
                m_kernelResolveMinSplatId = -1;
                return false;
            }

            if (m_kernelClearRangeImage < 0 ||
                m_kernelReduceMinRangeSq < 0 ||
                m_kernelResolveMinSplatId < 0)
            {
                return false;
            }

            // IsSupported 用于更稳定地判断 kernel 是否可运行(避免 Metal 上 FindKernel 成功但 Dispatch invalid).
            if (!computeShader.IsSupported(m_kernelClearRangeImage) ||
                !computeShader.IsSupported(m_kernelReduceMinRangeSq) ||
                !computeShader.IsSupported(m_kernelResolveMinSplatId))
            {
                m_kernelClearRangeImage = -1;
                m_kernelReduceMinRangeSq = -1;
                m_kernelResolveMinSplatId = -1;
                return false;
            }

            return true;
        }

        bool EnsureMaterialInstance(Material baseMaterial)
        {
            if (!baseMaterial)
                return false;

            var shader = baseMaterial.shader;
            if (!m_materialInstance || m_materialShader != shader)
            {
                DisposeMaterialInstance();
                m_materialShader = shader;

                // per-renderer material instance:
                // - 避免 MPB buffer binding 在 Metal 下偶发丢失.
                m_materialInstance = new Material(baseMaterial) { hideFlags = HideFlags.HideAndDontSave };
            }

            return m_materialInstance != null;
        }

        void DisposeMaterialInstance()
        {
            if (!m_materialInstance)
                return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEngine.Object.DestroyImmediate(m_materialInstance);
            else
#endif
                UnityEngine.Object.Destroy(m_materialInstance);

            m_materialInstance = null;
            m_materialShader = null;
        }

        void BindBuffersForRender(GraphicsBuffer splatColorBuffer)
        {
            // 注意:
            // - Metal 下如果任意 StructuredBuffer 未绑定,Unity 可能会直接跳过 draw call(避免崩溃).
            // - 因此这里把 shader 声明的 buffers 都视为“必绑资源”,每次 draw 前统一绑定.

            // MPB:
            m_propertyBlock.SetBuffer(k_lidarMinRangeSqBits, MinRangeSqBitsBuffer);
            m_propertyBlock.SetBuffer(k_lidarMinSplatId, MinSplatIdBuffer);
            m_propertyBlock.SetBuffer(k_lidarExternalRangeSqBits, ExternalRangeSqBitsBuffer);
            m_propertyBlock.SetBuffer(k_lidarExternalBaseColor, ExternalBaseColorBuffer);
            m_propertyBlock.SetBuffer(k_lidarAzSinCos, AzSinCosBuffer);
            m_propertyBlock.SetBuffer(k_lidarBeamSinCos, BeamSinCosBuffer);
            m_propertyBlock.SetBuffer(k_colorBuffer, splatColorBuffer);

            // Material instance(稳态兜底):
            m_materialInstance.SetBuffer(k_lidarMinRangeSqBits, MinRangeSqBitsBuffer);
            m_materialInstance.SetBuffer(k_lidarMinSplatId, MinSplatIdBuffer);
            m_materialInstance.SetBuffer(k_lidarExternalRangeSqBits, ExternalRangeSqBitsBuffer);
            m_materialInstance.SetBuffer(k_lidarExternalBaseColor, ExternalBaseColorBuffer);
            m_materialInstance.SetBuffer(k_lidarAzSinCos, AzSinCosBuffer);
            m_materialInstance.SetBuffer(k_lidarBeamSinCos, BeamSinCosBuffer);
            m_materialInstance.SetBuffer(k_colorBuffer, splatColorBuffer);
        }
    }
}
