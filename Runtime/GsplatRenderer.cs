// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using UnityEngine;

namespace Gsplat
{
    [ExecuteAlways]
    public class GsplatRenderer : MonoBehaviour, IGsplat
    {
        public GsplatAsset GsplatAsset;
        [Range(0, 3)] public int SHDegree = 3;
        public bool GammaToLinear;
        public bool AsyncUpload;
        [Tooltip("是否启用 Gsplat 主后端(Compute 排序 + Gsplat.shader)渲染. 仅使用 VFX Graph 后端时可关闭,避免双重渲染与排序开销.")]
        public bool EnableGsplatBackend = true;
        [Range(0, 1)] public float TimeNormalized;
        public bool AutoPlay;
        public float Speed = 1.0f;
        public bool Loop = true;

        [Tooltip("Max splat count to be uploaded per frame")]
        public uint UploadBatchSize = 100000;

        public bool RenderBeforeUploadComplete = true;

        GsplatAsset m_prevAsset;
        GsplatRendererImpl m_renderer;

        public bool Valid =>
            EnableGsplatBackend &&
            !m_disabledDueToError &&
            GsplatAsset &&
            (RenderBeforeUploadComplete ? SplatCount > 0 : SplatCount == m_effectiveSplatCount);

        public uint SplatCount => GsplatAsset ? m_effectiveSplatCount - m_pendingSplatCount : 0;
        public ISorterResource SorterResource => m_renderer.SorterResource;
        public bool Has4D => m_renderer != null && m_renderer.Has4D;
        bool IGsplat.Has4D => m_renderer != null && m_renderer.Has4D;
        float IGsplat.TimeNormalized => m_timeNormalizedThisFrame;
        int IGsplat.TimeModel => GetEffectiveTimeModel();
        float IGsplat.TemporalCutoff => GetEffectiveTemporalCutoff();
        GraphicsBuffer IGsplat.VelocityBuffer => m_renderer != null ? m_renderer.VelocityBuffer : null;
        GraphicsBuffer IGsplat.TimeBuffer => m_renderer != null ? m_renderer.TimeBuffer : null;
        GraphicsBuffer IGsplat.DurationBuffer => m_renderer != null ? m_renderer.DurationBuffer : null;

        // 公开 GPU buffers,用于可选的 VFX Graph 后端绑定等场景.
        public GraphicsBuffer PositionBuffer => m_renderer != null ? m_renderer.PositionBuffer : null;
        public GraphicsBuffer ScaleBuffer => m_renderer != null ? m_renderer.ScaleBuffer : null;
        public GraphicsBuffer RotationBuffer => m_renderer != null ? m_renderer.RotationBuffer : null;
        public GraphicsBuffer ColorBuffer => m_renderer != null ? m_renderer.ColorBuffer : null;
        public GraphicsBuffer SHBuffer => m_renderer != null ? m_renderer.SHBuffer : null;
        public GraphicsBuffer VelocityBuffer => m_renderer != null ? m_renderer.VelocityBuffer : null;
        public GraphicsBuffer TimeBuffer => m_renderer != null ? m_renderer.TimeBuffer : null;
        public GraphicsBuffer DurationBuffer => m_renderer != null ? m_renderer.DurationBuffer : null;
        public byte EffectiveSHBands => m_renderer != null ? m_renderer.SHBands : (byte)0;

        uint m_pendingSplatCount;
        float m_timeNormalizedThisFrame;
        uint m_effectiveSplatCount;
        byte m_effectiveSHBands;
        bool m_effectiveHas4D;
        bool m_disabledDueToError;

        static bool Has4DFields(GsplatAsset asset)
        {
            // 4D 数组只要有任意一个缺失,就视为 3D-only 资产,避免运行期出现数组越界或未绑定 buffer.
            return asset != null &&
                   asset.Velocities != null &&
                   asset.Times != null &&
                   asset.Durations != null;
        }

        int GetEffectiveTimeModel()
        {
            // 兼容旧资产:
            // - 旧版本没有 TimeModel 字段时,默认值可能为 0.
            // - 我们把 0 视为 window,以保持旧行为不变.
            var m = (int)(GsplatAsset ? GsplatAsset.TimeModel : (byte)0);
            return m == 2 ? 2 : 1;
        }

        float GetEffectiveTemporalCutoff()
        {
            var c = GsplatAsset ? GsplatAsset.TemporalGaussianCutoff : 0.0f;
            if (float.IsNaN(c) || float.IsInfinity(c) || c <= 0.0f || c >= 1.0f)
                return 0.01f;
            return c;
        }

        void RefreshEffectiveConfigAndLog()
        {
            m_effectiveHas4D = Has4DFields(GsplatAsset);
            m_effectiveSplatCount = GsplatAsset ? GsplatAsset.SplatCount : 0;
            m_effectiveSHBands = GsplatAsset ? GsplatAsset.SHBands : (byte)0;

            // 创建前估算 GPU 资源占用,让失败是可解释的.
            var desiredBytes = GsplatUtils.EstimateGpuBytes(m_effectiveSplatCount, m_effectiveSHBands, m_effectiveHas4D);
            var desiredMiB = GsplatUtils.BytesToMiB(desiredBytes);
            Debug.Log(
                $"[Gsplat] GPU 资源估算: {desiredMiB:F1} MiB " +
                $"(splats={m_effectiveSplatCount}, shBands={m_effectiveSHBands}, has4d={(m_effectiveHas4D ? 1 : 0)})");

            var settings = GsplatSettings.Instance;
            if (!settings)
                return;

            // 以显卡总显存的比例做风险提示(注意: 这不是实时可用显存).
            var vramMiB = SystemInfo.graphicsMemorySize;
            var warnRatio = Mathf.Clamp01(settings.VramWarnRatio);
            var thresholdBytes = (long)(vramMiB * 1024L * 1024L * warnRatio);
            if (vramMiB > 0 && warnRatio > 0.0f && desiredBytes > thresholdBytes)
            {
                var thresholdMiB = GsplatUtils.BytesToMiB(thresholdBytes);
                Debug.LogWarning(
                    $"[Gsplat] 资源风险较高: 估算 {desiredMiB:F1} MiB > 阈值 {thresholdMiB:F1} MiB " +
                    $"(显存 {vramMiB} MiB * {warnRatio:P0}). " +
                    $"建议: 降低 SH 阶数,限制 splat 数量,或使用更大显存的 GPU.");

                // 自动降级(可配置)
                var beforeCount = m_effectiveSplatCount;
                var beforeBands = m_effectiveSHBands;

                switch (settings.AutoDegrade)
                {
                    case GsplatAutoDegradePolicy.ReduceSH:
                        if (m_effectiveSHBands > 0)
                            m_effectiveSHBands = 0;
                        break;
                    case GsplatAutoDegradePolicy.CapSplatCount:
                        if (settings.AutoDegradeMaxSplatCount > 0 && m_effectiveSplatCount > settings.AutoDegradeMaxSplatCount)
                            m_effectiveSplatCount = settings.AutoDegradeMaxSplatCount;
                        break;
                    case GsplatAutoDegradePolicy.ReduceSHThenCapSplatCount:
                        if (m_effectiveSHBands > 0)
                            m_effectiveSHBands = 0;
                        if (settings.AutoDegradeMaxSplatCount > 0 && m_effectiveSplatCount > settings.AutoDegradeMaxSplatCount)
                            m_effectiveSplatCount = settings.AutoDegradeMaxSplatCount;
                        break;
                }

                if (beforeCount != m_effectiveSplatCount || beforeBands != m_effectiveSHBands)
                {
                    var afterBytes = GsplatUtils.EstimateGpuBytes(m_effectiveSplatCount, m_effectiveSHBands, m_effectiveHas4D);
                    var afterMiB = GsplatUtils.BytesToMiB(afterBytes);
                    Debug.LogWarning(
                        $"[Gsplat] AutoDegrade 生效: " +
                        $"splats {beforeCount} -> {m_effectiveSplatCount}, " +
                        $"shBands {beforeBands} -> {m_effectiveSHBands}, " +
                        $"估算 {desiredMiB:F1} MiB -> {afterMiB:F1} MiB");
                }
            }
        }

        bool TryCreateOrRecreateRenderer()
        {
            m_disabledDueToError = false;
            m_pendingSplatCount = 0;

            if (!GsplatAsset)
            {
                m_effectiveSplatCount = 0;
                m_effectiveSHBands = 0;
                m_effectiveHas4D = false;
                m_renderer?.Dispose();
                m_renderer = null;
                return false;
            }

            RefreshEffectiveConfigAndLog();

            try
            {
                if (m_renderer == null)
                    m_renderer = new GsplatRendererImpl(m_effectiveSplatCount, m_effectiveSHBands, m_effectiveHas4D);
                else
                    m_renderer.RecreateResources(m_effectiveSplatCount, m_effectiveSHBands, m_effectiveHas4D);
            }
            catch (Exception ex)
            {
                // 失败要可行动: 给出 buffer 创建失败的恢复建议,并禁用当前 renderer 的渲染.
                m_disabledDueToError = true;
                m_renderer?.Dispose();
                m_renderer = null;
                Debug.LogError(
                    $"[Gsplat] GraphicsBuffer 创建失败,已禁用该对象的渲染. " +
                    $"建议: 降低 SH(或启用 AutoDegrade=ReduceSH),减少 splat 数(或启用 CapSplatCount),或更换更大显存 GPU.\n" +
                    ex);
                return false;
            }

            return true;
        }

        void SetBufferData()
        {
            var count = (int)m_effectiveSplatCount;
            if (count <= 0)
                return;

            m_renderer.PositionBuffer.SetData(GsplatAsset.Positions, 0, 0, count);
            m_renderer.ScaleBuffer.SetData(GsplatAsset.Scales, 0, 0, count);
            m_renderer.RotationBuffer.SetData(GsplatAsset.Rotations, 0, 0, count);
            m_renderer.ColorBuffer.SetData(GsplatAsset.Colors, 0, 0, count);
            if (m_renderer.SHBands > 0)
            {
                var coefficientCount = GsplatUtils.SHBandsToCoefficientCount(m_renderer.SHBands);
                m_renderer.SHBuffer.SetData(GsplatAsset.SHs, 0, 0, coefficientCount * count);
            }

            if (m_renderer.Has4D)
            {
                m_renderer.VelocityBuffer.SetData(GsplatAsset.Velocities, 0, 0, count);
                m_renderer.TimeBuffer.SetData(GsplatAsset.Times, 0, 0, count);
                m_renderer.DurationBuffer.SetData(GsplatAsset.Durations, 0, 0, count);
            }
        }


        void SetBufferDataAsync()
        {
            m_pendingSplatCount = m_effectiveSplatCount;
        }

        void UploadData()
        {
            var offset = (int)(m_effectiveSplatCount - m_pendingSplatCount);
            var count = (int)Math.Min(UploadBatchSize, m_pendingSplatCount);
            m_pendingSplatCount -= (uint)count;
            m_renderer.PositionBuffer.SetData(GsplatAsset.Positions, offset, offset, count);
            m_renderer.ScaleBuffer.SetData(GsplatAsset.Scales, offset, offset, count);
            m_renderer.RotationBuffer.SetData(GsplatAsset.Rotations, offset, offset, count);
            m_renderer.ColorBuffer.SetData(GsplatAsset.Colors, offset, offset, count);
            if (m_renderer.Has4D)
            {
                m_renderer.VelocityBuffer.SetData(GsplatAsset.Velocities, offset, offset, count);
                m_renderer.TimeBuffer.SetData(GsplatAsset.Times, offset, offset, count);
                m_renderer.DurationBuffer.SetData(GsplatAsset.Durations, offset, offset, count);
            }

            if (m_renderer.SHBands <= 0) return;
            var coefficientCount = GsplatUtils.SHBandsToCoefficientCount(m_renderer.SHBands);
            m_renderer.SHBuffer.SetData(GsplatAsset.SHs, coefficientCount * offset,
                coefficientCount * offset, coefficientCount * count);
        }


        void OnEnable()
        {
            GsplatSorter.Instance.RegisterGsplat(this);
            m_timeNormalizedThisFrame = Mathf.Clamp01(TimeNormalized);
            if (!TryCreateOrRecreateRenderer())
                return;
#if UNITY_EDITOR
            if (AsyncUpload && Application.isPlaying)
#else
            if (AsyncUpload)
#endif
                SetBufferDataAsync();
            else
                SetBufferData();

            // 避免下一帧 Update 再次重复触发一次重建.
            m_prevAsset = GsplatAsset;
        }

        void OnDisable()
        {
            GsplatSorter.Instance.UnregisterGsplat(this);
            m_renderer?.Dispose();
            m_renderer = null;
            m_prevAsset = null;
        }

        void Update()
        {
            if (!m_disabledDueToError && m_renderer != null && m_pendingSplatCount > 0)
                UploadData();

            // ----------------------------------------------------------------
            // 播放控制: TimeNormalized / AutoPlay / Speed / Loop
            // - 这里把最终用于排序与渲染的时间缓存到 `m_timeNormalizedThisFrame`,
            //   以保证同一帧内 compute 排序与 shader 渲染使用同一个 t.
            // ----------------------------------------------------------------
            if (float.IsNaN(Speed) || float.IsInfinity(Speed))
                Speed = 0.0f;

            if (AutoPlay)
            {
                var next = TimeNormalized + Time.deltaTime * Speed;
                TimeNormalized = Loop ? Mathf.Repeat(next, 1.0f) : Mathf.Clamp01(next);
            }

            m_timeNormalizedThisFrame = Mathf.Clamp01(TimeNormalized);

            if (m_prevAsset != GsplatAsset)
            {
                m_prevAsset = GsplatAsset;
                if (TryCreateOrRecreateRenderer())
                {
#if UNITY_EDITOR
                    if (AsyncUpload && Application.isPlaying)
#else
                    if (AsyncUpload)
#endif
                        SetBufferDataAsync();
                    else
                        SetBufferData();
                }
            }

            if (Valid)
            {
                var motionPadding = 0.0f;
                if (m_renderer.Has4D)
                {
                    motionPadding = GsplatAsset.MaxSpeed * GsplatAsset.MaxDuration;
                    if (motionPadding < 0.0f || float.IsNaN(motionPadding) || float.IsInfinity(motionPadding))
                        motionPadding = 0.0f;
                }

                m_renderer.Render(SplatCount, transform, GsplatAsset.Bounds,
                    gameObject.layer, GammaToLinear, SHDegree, m_timeNormalizedThisFrame, motionPadding,
                    timeModel: GetEffectiveTimeModel(), temporalCutoff: GetEffectiveTemporalCutoff());
            }
        }
    }
}
