// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
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

        // --------------------------------------------------------------------
        // `.splat4d v2` SH delta-v1 runtime state(可选)
        // - 当 asset 未提供 delta 字段,或 compute 不可用时,这些资源为 null.
        // - 设计目标: 在 TimeNormalized 播放时,按 targetFrame 应用 label updates,
        //   并用 compute scatter 更新 SHBuffer.
        // --------------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        struct ShDeltaUpdate
        {
            public uint splatId;
            public uint label;
        }

        sealed class ShDeltaSegmentRuntime
        {
            public int StartFrame;
            public int FrameCount;
            public int LabelCount;
            public byte[] BaseLabelsBytes; // u16 little-endian
            public byte[] DeltaBytes; // delta-v1 header+body
            public int[] BlockOffsets; // length = FrameCount-1, points to updateCount within DeltaBytes
        }

        bool m_shDeltaDisabled;
        bool m_shDeltaInitialized;
        int m_shDeltaFrameCount;
        int m_shDeltaCurrentFrame;
        int m_shDeltaCurrentSegmentIndex;

        ComputeShader m_shDeltaCS;
        int m_kernelApplySh1 = -1;
        int m_kernelApplySh2 = -1;
        int m_kernelApplySh3 = -1;

        GraphicsBuffer m_shDeltaUpdatesBuffer;
        int m_shDeltaUpdatesCapacity;
        ShDeltaUpdate[] m_shDeltaUpdatesScratch;

        GraphicsBuffer m_sh1CentroidsBuffer;
        GraphicsBuffer m_sh2CentroidsBuffer;
        GraphicsBuffer m_sh3CentroidsBuffer;

        ShDeltaSegmentRuntime[] m_sh1Segments;
        ShDeltaSegmentRuntime[] m_sh2Segments;
        ShDeltaSegmentRuntime[] m_sh3Segments;

        ushort[] m_sh1Labels;
        ushort[] m_sh2Labels;
        ushort[] m_sh3Labels;
        ushort[] m_shLabelsScratch;

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

        static int BandCoeffCount(int band) => band switch
        {
            1 => 3,
            2 => 5,
            3 => 7,
            _ => 0
        };

        static int BandCoeffOffset(int band) => band switch
        {
            1 => 0,
            2 => 3,
            3 => 8,
            _ => 0
        };

        static int DivRoundUp(int x, int d) => (x + d - 1) / d;

        void DisposeShDeltaResources()
        {
            m_shDeltaUpdatesBuffer?.Dispose();
            m_shDeltaUpdatesBuffer = null;
            m_shDeltaUpdatesCapacity = 0;
            m_shDeltaUpdatesScratch = null;

            m_sh1CentroidsBuffer?.Dispose();
            m_sh2CentroidsBuffer?.Dispose();
            m_sh3CentroidsBuffer?.Dispose();
            m_sh1CentroidsBuffer = null;
            m_sh2CentroidsBuffer = null;
            m_sh3CentroidsBuffer = null;

            m_sh1Segments = null;
            m_sh2Segments = null;
            m_sh3Segments = null;

            m_sh1Labels = null;
            m_sh2Labels = null;
            m_sh3Labels = null;
            m_shLabelsScratch = null;

            m_shDeltaCS = null;
            m_kernelApplySh1 = -1;
            m_kernelApplySh2 = -1;
            m_kernelApplySh3 = -1;

            m_shDeltaInitialized = false;
            m_shDeltaDisabled = false;
            m_shDeltaFrameCount = 0;
            m_shDeltaCurrentFrame = 0;
            m_shDeltaCurrentSegmentIndex = 0;
        }

        void EnsureShDeltaUpdatesCapacity(int required)
        {
            required = Math.Max(required, 1);
            if (m_shDeltaUpdatesBuffer != null &&
                m_shDeltaUpdatesCapacity >= required &&
                m_shDeltaUpdatesScratch != null &&
                m_shDeltaUpdatesScratch.Length >= required)
            {
                return;
            }

            // 说明:
            // - updates 可能在 segment 边界处变大(需要 diff 到 base labels).
            // - 这里用 NextPowerOfTwo 减少反复扩容次数.
            var cap = Mathf.NextPowerOfTwo(required);
            if (cap < required)
                cap = required; // 极端情况下 NextPowerOfTwo 溢出时兜底

            m_shDeltaUpdatesBuffer?.Dispose();
            m_shDeltaUpdatesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 8);
            m_shDeltaUpdatesCapacity = cap;
            m_shDeltaUpdatesScratch = new ShDeltaUpdate[cap];
        }

        static bool TryValidateKernel(ComputeShader cs, int kernel, string kernelName, out string error)
        {
            error = null;
            if (!cs)
            {
                error = "ComputeShader is null";
                return false;
            }

            try
            {
                if (!cs.IsSupported(kernel))
                {
                    error = $"kernel not supported: {kernelName}";
                    return false;
                }
            }
            catch (Exception e)
            {
                error = $"kernel validation threw: {kernelName}: {e.Message}";
                return false;
            }

            return true;
        }

        static bool TryBuildSegmentRuntimes(
            Splat4DShDeltaSegment[] segments,
            int effectiveSplatCount,
            out ShDeltaSegmentRuntime[] runtimes,
            out string error)
        {
            runtimes = null;
            error = null;

            if (segments == null || segments.Length == 0)
            {
                error = "segments missing";
                return false;
            }

            var expectedLabelCount = -1;
            var outArr = new ShDeltaSegmentRuntime[segments.Length];
            for (var i = 0; i < segments.Length; i++)
            {
                var s = segments[i];
                if (s == null)
                {
                    error = $"segment[{i}] is null";
                    return false;
                }

                if (s.FrameCount <= 0)
                {
                    error = $"segment[{i}] invalid FrameCount={s.FrameCount}";
                    return false;
                }

                if (s.BaseLabelsBytes == null || s.BaseLabelsBytes.Length < effectiveSplatCount * 2)
                {
                    error = $"segment[{i}] base labels bytes too small: {s.BaseLabelsBytes?.Length ?? 0}";
                    return false;
                }

                if (s.DeltaBytes == null || s.DeltaBytes.Length < 28)
                {
                    error = $"segment[{i}] delta bytes too small: {s.DeltaBytes?.Length ?? 0}";
                    return false;
                }

                var span = s.DeltaBytes.AsSpan();
                // delta-v1 header: magic(8) + version/start/count/splatCount/labelCount (5*u32)
                var version = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8, 4));
                var segStart = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12, 4));
                var segCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16, 4));
                var splatCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(20, 4));
                var labelCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(24, 4));
                if (version != 1 || segStart != s.StartFrame || segCount != s.FrameCount)
                {
                    error = $"segment[{i}] delta header mismatch: v={version} start={segStart} count={segCount}";
                    return false;
                }

                if (splatCount < effectiveSplatCount)
                {
                    error = $"segment[{i}] delta splatCount too small: {splatCount} < effective {effectiveSplatCount}";
                    return false;
                }
                if (labelCount <= 0)
                {
                    error = $"segment[{i}] delta labelCount invalid: {labelCount}";
                    return false;
                }
                if (expectedLabelCount < 0)
                    expectedLabelCount = labelCount;
                else if (expectedLabelCount != labelCount)
                {
                    error = $"segment[{i}] delta labelCount mismatch: {labelCount} != expected {expectedLabelCount}";
                    return false;
                }

                var blockCount = s.FrameCount - 1;
                var offsets = new int[Math.Max(blockCount, 0)];
                var p = 28;
                for (var b = 0; b < blockCount; b++)
                {
                    if (p + 4 > span.Length)
                    {
                        error = $"segment[{i}] delta truncated while building offsets";
                        return false;
                    }

                    offsets[b] = p;
                    var updateCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(p, 4));
                    p += 4;

                    var need = (long)updateCount * 8L;
                    if (need < 0 || p + need > span.Length)
                    {
                        error = $"segment[{i}] delta updates payload out of range: updateCount={updateCount}";
                        return false;
                    }

                    p += (int)need;
                }

                if (p != span.Length)
                {
                    error = $"segment[{i}] delta has trailing bytes: parsed={p} total={span.Length}";
                    return false;
                }

                outArr[i] = new ShDeltaSegmentRuntime
                {
                    StartFrame = s.StartFrame,
                    FrameCount = s.FrameCount,
                    LabelCount = labelCount,
                    BaseLabelsBytes = s.BaseLabelsBytes,
                    DeltaBytes = s.DeltaBytes,
                    BlockOffsets = offsets
                };
            }

            runtimes = outArr;
            return true;
        }

        static int FindSegmentIndex(ShDeltaSegmentRuntime[] segments, int frame)
        {
            if (segments == null)
                return -1;

            for (var i = 0; i < segments.Length; i++)
            {
                var s = segments[i];
                var start = s.StartFrame;
                var endExcl = start + s.FrameCount;
                if (frame >= start && frame < endExcl)
                    return i;
            }

            return -1;
        }

        static int ReadUpdateBlock(
            ShDeltaSegmentRuntime seg,
            int relFrame,
            int effectiveSplatCount,
            int maxLabelExclusive,
            ShDeltaUpdate[] outUpdates)
        {
            // relFrame: segment 内相对帧索引. startFrame 本身是 rel=0,没有 delta block.
            if (relFrame <= 0)
                return 0;

            var span = seg.DeltaBytes.AsSpan();
            var blockOffset = seg.BlockOffsets[relFrame - 1];
            var updateCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(blockOffset, 4));
            var p = blockOffset + 4;

            var wrote = 0;
            var hasLast = false;
            uint lastSplatId = 0;
            for (var i = 0; i < updateCount; i++)
            {
                var splatId = (uint)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(p, 4));
                p += 4;
                var newLabel = (uint)BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(p, 2));
                p += 2;
                var reserved = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(p, 2));
                p += 2;

                // reserved 必须为 0. 若遇到非法数据,直接抛异常让上层禁用动态 SH.
                if (reserved != 0)
                    throw new InvalidDataException("delta-v1 reserved field must be 0");

                // 约束: 同一帧 block 内 splatId 必须严格递增(与 spec 对齐).
                if (hasLast && splatId <= lastSplatId)
                    throw new InvalidDataException("delta-v1 splatId must be strictly increasing within a frame");
                hasLast = true;
                lastSplatId = splatId;

                if (newLabel >= (uint)maxLabelExclusive)
                    throw new InvalidDataException("delta-v1 label out of range");

                if (splatId >= (uint)effectiveSplatCount)
                    continue; // 被 CapSplatCount 截断的部分直接忽略,避免 OOB.

                if (wrote >= outUpdates.Length)
                    throw new InvalidDataException("delta-v1 updateCount exceeds scratch capacity");
                outUpdates[wrote++] = new ShDeltaUpdate { splatId = splatId, label = newLabel };
            }

            return wrote;
        }

        static void DecodeLabelsAtFrame(
            ShDeltaSegmentRuntime seg,
            int targetFrame,
            int effectiveSplatCount,
            int maxLabelExclusive,
            ushort[] outLabels)
        {
            // 1) base labels(段起始帧的绝对状态)
            Buffer.BlockCopy(seg.BaseLabelsBytes, 0, outLabels, 0, effectiveSplatCount * 2);

            // 2) 逐帧应用 delta blocks,直到 targetFrame
            var rel = targetFrame - seg.StartFrame;
            if (rel <= 0)
                return;

            var span = seg.DeltaBytes.AsSpan();
            for (var r = 1; r <= rel; r++)
            {
                var blockOffset = seg.BlockOffsets[r - 1];
                var updateCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(blockOffset, 4));
                var p = blockOffset + 4;
                var hasLast = false;
                uint lastSplatId = 0;
                for (var i = 0; i < updateCount; i++)
                {
                    var splatId = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(p, 4));
                    p += 4;
                    var newLabel = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(p, 2));
                    p += 2;
                    var reserved = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(p, 2));
                    p += 2;
                    if (reserved != 0)
                        throw new InvalidDataException("delta-v1 reserved field must be 0");

                    if (hasLast && (uint)splatId <= lastSplatId)
                        throw new InvalidDataException("delta-v1 splatId must be strictly increasing within a frame");
                    hasLast = true;
                    lastSplatId = (uint)splatId;

                    if (newLabel >= maxLabelExclusive)
                        throw new InvalidDataException("delta-v1 label out of range");

                    if (splatId < 0 || splatId >= effectiveSplatCount)
                        continue;
                    outLabels[splatId] = newLabel;
                }
            }
        }

        static bool TryValidateSegmentsAligned(
            ShDeltaSegmentRuntime[] baseSegments,
            ShDeltaSegmentRuntime[] otherSegments,
            int band,
            out string error)
        {
            error = null;
            if (baseSegments == null || baseSegments.Length == 0)
            {
                error = "base segments missing";
                return false;
            }

            if (otherSegments == null || otherSegments.Length != baseSegments.Length)
            {
                error =
                    $"band={band} segments length mismatch: {otherSegments?.Length ?? 0} != {baseSegments.Length}";
                return false;
            }

            for (var i = 0; i < baseSegments.Length; i++)
            {
                var a = baseSegments[i];
                var b = otherSegments[i];
                if (a.StartFrame != b.StartFrame || a.FrameCount != b.FrameCount)
                {
                    error =
                        $"band={band} segments not aligned at index={i}: " +
                        $"(start,count)=({b.StartFrame},{b.FrameCount}) != base({a.StartFrame},{a.FrameCount})";
                    return false;
                }
            }

            return true;
        }

        static bool TryValidateBaseLabelsRange(
            ShDeltaSegmentRuntime[] segments,
            int effectiveSplatCount,
            int band,
            out string error)
        {
            error = null;
            if (segments == null || segments.Length == 0)
            {
                error = $"band={band} segments missing";
                return false;
            }

            var maxLabelExclusive = segments[0].LabelCount;
            if (maxLabelExclusive <= 0)
            {
                error = $"band={band} invalid labelCount={maxLabelExclusive}";
                return false;
            }

            // 说明:
            // - importer 只会解码 startFrame=0 的 base labels.
            // - 其它 segment 的 base labels 仅做 header 校验,不做 label range 校验.
            // - runtime 需要保证“任意 seek 到 segment base”都不会把越界 label 喂给 GPU.
            //
            // 这里选择在 init 时一次性校验所有 segments 的 base labels,换取运行期更安全.
            for (var s = 0; s < segments.Length; s++)
            {
                var seg = segments[s];
                var byteLen = effectiveSplatCount * 2;
                if (seg.BaseLabelsBytes == null || seg.BaseLabelsBytes.Length < byteLen)
                {
                    error = $"band={band} segment[{s}] base labels too small";
                    return false;
                }

                if (BitConverter.IsLittleEndian)
                {
                    var labels = MemoryMarshal.Cast<byte, ushort>(seg.BaseLabelsBytes.AsSpan(0, byteLen));
                    for (var i = 0; i < effectiveSplatCount; i++)
                    {
                        if (labels[i] >= maxLabelExclusive)
                        {
                            error =
                                $"band={band} segment[{s}] base label out of range: " +
                                $"splatId={i} label={labels[i]} >= {maxLabelExclusive}";
                            return false;
                        }
                    }
                }
                else
                {
                    var span = seg.BaseLabelsBytes.AsSpan(0, byteLen);
                    for (var i = 0; i < effectiveSplatCount; i++)
                    {
                        var label = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(i * 2, 2));
                        if (label >= maxLabelExclusive)
                        {
                            error =
                                $"band={band} segment[{s}] base label out of range: " +
                                $"splatId={i} label={label} >= {maxLabelExclusive}";
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        bool TryDispatchShDeltaUpdates(int band, GraphicsBuffer centroids, int kernel, ShDeltaUpdate[] updates, int updateCount)
        {
            if (updateCount <= 0)
                return true;
            // 注意: GraphicsBuffer 不是 UnityEngine.Object,不能用 `!buffer` 这种写法做空值判断.
            if (!m_shDeltaCS || kernel < 0 || centroids == null || m_renderer == null || m_renderer.SHBuffer == null)
                return false;

            EnsureShDeltaUpdatesCapacity(updateCount);
            m_shDeltaUpdatesBuffer.SetData(updates, 0, 0, updateCount);

            var restCoeffCountTotal = GsplatUtils.SHBandsToCoefficientCount(m_renderer.SHBands);
            var coeffCount = BandCoeffCount(band);
            var coeffOffset = BandCoeffOffset(band);

            m_shDeltaCS.SetInt("_UpdateCount", updateCount);
            m_shDeltaCS.SetInt("_RestCoeffCountTotal", restCoeffCountTotal);
            m_shDeltaCS.SetInt("_BandCoeffOffset", coeffOffset);
            m_shDeltaCS.SetInt("_BandCoeffCount", coeffCount);
            m_shDeltaCS.SetBuffer(kernel, "_Updates", m_shDeltaUpdatesBuffer);
            m_shDeltaCS.SetBuffer(kernel, "_Centroids", centroids);
            m_shDeltaCS.SetBuffer(kernel, "_SHBuffer", m_renderer.SHBuffer);

            var groups = DivRoundUp(updateCount, 256);
            m_shDeltaCS.Dispatch(kernel, groups, 1, 1);
            return true;
        }

        void TryInitShDeltaRuntime()
        {
            DisposeShDeltaResources();

            if (!GsplatAsset || m_renderer == null)
                return;
            if (m_renderer.SHBands <= 0)
                return;

            // delta 数据必须存在且 frameCount 有意义.
            if (GsplatAsset.ShFrameCount <= 0 || GsplatAsset.Sh1DeltaSegments == null || GsplatAsset.Sh1DeltaSegments.Length == 0)
                return;

            if (!SystemInfo.supportsComputeShaders)
            {
                Debug.LogWarning("[Gsplat] 当前平台不支持 ComputeShader,将禁用 `.splat4d` 动态 SH(delta-v1).");
                m_shDeltaDisabled = true;
                return;
            }

            var settings = GsplatSettings.Instance;
            if (!settings || !settings.ShDeltaComputeShader)
            {
                Debug.LogWarning("[Gsplat] 缺少 ShDeltaComputeShader,将禁用 `.splat4d` 动态 SH(delta-v1).");
                m_shDeltaDisabled = true;
                return;
            }

            m_shDeltaCS = settings.ShDeltaComputeShader;
            try
            {
                m_kernelApplySh1 = m_shDeltaCS.FindKernel("ApplySh1Updates");
                m_kernelApplySh2 = m_shDeltaCS.FindKernel("ApplySh2Updates");
                m_kernelApplySh3 = m_shDeltaCS.FindKernel("ApplySh3Updates");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Gsplat] ShDeltaComputeShader 缺少必需 kernel,将禁用动态 SH: {e.Message}");
                m_shDeltaDisabled = true;
                return;
            }

            if (!TryValidateKernel(m_shDeltaCS, m_kernelApplySh1, "ApplySh1Updates", out var kernelError))
            {
                Debug.LogWarning($"[Gsplat] ShDeltaComputeShader kernel 无效,将禁用动态 SH: {kernelError}");
                m_shDeltaDisabled = true;
                return;
            }

            // band2/band3 的 kernel 是否可用,按需校验(只要 effectiveSHBands 用不到,就不强制要求).
            if (m_renderer.SHBands >= 2 &&
                !TryValidateKernel(m_shDeltaCS, m_kernelApplySh2, "ApplySh2Updates", out kernelError))
            {
                Debug.LogWarning($"[Gsplat] ShDeltaComputeShader kernel 无效,将禁用动态 SH: {kernelError}");
                m_shDeltaDisabled = true;
                return;
            }
            if (m_renderer.SHBands >= 3 &&
                !TryValidateKernel(m_shDeltaCS, m_kernelApplySh3, "ApplySh3Updates", out kernelError))
            {
                Debug.LogWarning($"[Gsplat] ShDeltaComputeShader kernel 无效,将禁用动态 SH: {kernelError}");
                m_shDeltaDisabled = true;
                return;
            }

            var effectiveSplatCount = checked((int)m_effectiveSplatCount);

            // 1) segments runtime
            if (!TryBuildSegmentRuntimes(GsplatAsset.Sh1DeltaSegments, effectiveSplatCount, out m_sh1Segments, out var err))
            {
                Debug.LogWarning($"[Gsplat] SH delta segments 无效(band=1),将禁用动态 SH: {err}");
                m_shDeltaDisabled = true;
                return;
            }
            if (m_renderer.SHBands >= 2)
            {
                if (!TryBuildSegmentRuntimes(GsplatAsset.Sh2DeltaSegments, effectiveSplatCount, out m_sh2Segments, out err))
                {
                    Debug.LogWarning($"[Gsplat] SH delta segments 无效(band=2),将禁用动态 SH: {err}");
                    m_shDeltaDisabled = true;
                    return;
                }
            }
            if (m_renderer.SHBands >= 3)
            {
                if (!TryBuildSegmentRuntimes(GsplatAsset.Sh3DeltaSegments, effectiveSplatCount, out m_sh3Segments, out err))
                {
                    Debug.LogWarning($"[Gsplat] SH delta segments 无效(band=3),将禁用动态 SH: {err}");
                    m_shDeltaDisabled = true;
                    return;
                }
            }

            // 1.1) 多 band 时要求 segments 对齐(同一 index 对应同一 [start,count]).
            // - 这能简化运行期的 “frame -> segment” 映射,并避免 band 间状态不一致.
            if (m_renderer.SHBands >= 2 &&
                !TryValidateSegmentsAligned(m_sh1Segments, m_sh2Segments, 2, out err))
            {
                Debug.LogWarning($"[Gsplat] SH delta segments 不对齐,将禁用动态 SH: {err}");
                m_shDeltaDisabled = true;
                return;
            }
            if (m_renderer.SHBands >= 3 &&
                !TryValidateSegmentsAligned(m_sh1Segments, m_sh3Segments, 3, out err))
            {
                Debug.LogWarning($"[Gsplat] SH delta segments 不对齐,将禁用动态 SH: {err}");
                m_shDeltaDisabled = true;
                return;
            }

            // 1.2) 校验所有 segments 的 base labels 范围(避免 seek 时 GPU 越界).
            if (!TryValidateBaseLabelsRange(m_sh1Segments, effectiveSplatCount, 1, out err) ||
                (m_renderer.SHBands >= 2 && !TryValidateBaseLabelsRange(m_sh2Segments, effectiveSplatCount, 2, out err)) ||
                (m_renderer.SHBands >= 3 && !TryValidateBaseLabelsRange(m_sh3Segments, effectiveSplatCount, 3, out err)))
            {
                Debug.LogWarning($"[Gsplat] SH base labels 无效,将禁用动态 SH: {err}");
                m_shDeltaDisabled = true;
                return;
            }

            // 2) centroids buffers(常驻)
            if (GsplatAsset.Sh1Centroids == null || GsplatAsset.Sh1Centroids.Length == 0)
            {
                Debug.LogWarning("[Gsplat] 缺少 Sh1Centroids,将禁用动态 SH(delta-v1).");
                m_shDeltaDisabled = true;
                return;
            }
            // 每个 band 的 centroids 数量必须匹配 delta header 的 labelCount.
            if (GsplatAsset.Sh1Centroids.Length != m_sh1Segments[0].LabelCount * BandCoeffCount(1))
            {
                Debug.LogWarning("[Gsplat] Sh1Centroids 长度与 delta labelCount 不一致,将禁用动态 SH(delta-v1).");
                m_shDeltaDisabled = true;
                return;
            }
            m_sh1CentroidsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GsplatAsset.Sh1Centroids.Length, 12);
            m_sh1CentroidsBuffer.SetData(GsplatAsset.Sh1Centroids);

            if (m_renderer.SHBands >= 2)
            {
                if (GsplatAsset.Sh2Centroids == null || GsplatAsset.Sh2Centroids.Length == 0)
                {
                    Debug.LogWarning("[Gsplat] 缺少 Sh2Centroids,将禁用动态 SH(delta-v1).");
                    m_shDeltaDisabled = true;
                    return;
                }
                if (GsplatAsset.Sh2Centroids.Length != m_sh2Segments[0].LabelCount * BandCoeffCount(2))
                {
                    Debug.LogWarning("[Gsplat] Sh2Centroids 长度与 delta labelCount 不一致,将禁用动态 SH(delta-v1).");
                    m_shDeltaDisabled = true;
                    return;
                }
                m_sh2CentroidsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GsplatAsset.Sh2Centroids.Length, 12);
                m_sh2CentroidsBuffer.SetData(GsplatAsset.Sh2Centroids);
            }
            if (m_renderer.SHBands >= 3)
            {
                if (GsplatAsset.Sh3Centroids == null || GsplatAsset.Sh3Centroids.Length == 0)
                {
                    Debug.LogWarning("[Gsplat] 缺少 Sh3Centroids,将禁用动态 SH(delta-v1).");
                    m_shDeltaDisabled = true;
                    return;
                }
                if (GsplatAsset.Sh3Centroids.Length != m_sh3Segments[0].LabelCount * BandCoeffCount(3))
                {
                    Debug.LogWarning("[Gsplat] Sh3Centroids 长度与 delta labelCount 不一致,将禁用动态 SH(delta-v1).");
                    m_shDeltaDisabled = true;
                    return;
                }
                m_sh3CentroidsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GsplatAsset.Sh3Centroids.Length, 12);
                m_sh3CentroidsBuffer.SetData(GsplatAsset.Sh3Centroids);
            }

            // 3) labels state(从 startFrame=0 的 base labels 初始化)
            m_shLabelsScratch = new ushort[effectiveSplatCount];

            m_sh1Labels = new ushort[effectiveSplatCount];
            Buffer.BlockCopy(m_sh1Segments[0].BaseLabelsBytes, 0, m_sh1Labels, 0, effectiveSplatCount * 2);
            if (m_renderer.SHBands >= 2)
            {
                m_sh2Labels = new ushort[effectiveSplatCount];
                Buffer.BlockCopy(m_sh2Segments[0].BaseLabelsBytes, 0, m_sh2Labels, 0, effectiveSplatCount * 2);
            }
            if (m_renderer.SHBands >= 3)
            {
                m_sh3Labels = new ushort[effectiveSplatCount];
                Buffer.BlockCopy(m_sh3Segments[0].BaseLabelsBytes, 0, m_sh3Labels, 0, effectiveSplatCount * 2);
            }

            m_shDeltaFrameCount = GsplatAsset.ShFrameCount;
            m_shDeltaCurrentFrame = 0;
            m_shDeltaCurrentSegmentIndex = 0;

            // updates buffer 先给一个小容量,后续按需扩容.
            EnsureShDeltaUpdatesCapacity(1024);

            m_shDeltaInitialized = true;
        }

        void TryApplyShDeltaForTime(float t)
        {
            if (!m_shDeltaInitialized || m_shDeltaDisabled)
                return;

            if (m_pendingSplatCount > 0)
                return; // 异步上传未完成时,避免被 UploadData 覆盖.

            var frameCount = m_shDeltaFrameCount;
            if (frameCount <= 1)
                return;

            var target = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(t) * (frameCount - 1)), 0, frameCount - 1);
            if (target == m_shDeltaCurrentFrame)
                return;

            try
            {
                if (!TryApplyShDeltaToFrame(target))
                {
                    Debug.LogWarning("[Gsplat] 动态 SH(delta-v1) 更新失败,将禁用后续更新并保持 frame0.");
                    m_shDeltaDisabled = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Gsplat] 动态 SH(delta-v1) 更新异常,将禁用后续更新并保持 frame0: {e.Message}");
                m_shDeltaDisabled = true;
            }
        }

        bool TryApplyShDeltaToFrame(int targetFrame)
        {
            var effectiveSplatCount = checked((int)m_effectiveSplatCount);
            if (effectiveSplatCount <= 0)
                return true;

            // 优化: 仅处理最常见的顺序播放(前进 1 帧).
            if (targetFrame == m_shDeltaCurrentFrame + 1)
            {
                var segIndex = FindSegmentIndex(m_sh1Segments, targetFrame);
                if (segIndex == m_shDeltaCurrentSegmentIndex)
                {
                    var seg = m_sh1Segments[segIndex];
                    var rel = targetFrame - seg.StartFrame;
                    if (rel > 0)
                    {
                        // 先读 updateCount 决定 scratch 容量,避免 updateCount 较大时数组越界.
                        var required = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                            seg.DeltaBytes.AsSpan(seg.BlockOffsets[rel - 1], 4));
                        if (m_renderer.SHBands >= 2)
                        {
                            var seg2 = m_sh2Segments[segIndex];
                            var u2 = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                                seg2.DeltaBytes.AsSpan(seg2.BlockOffsets[rel - 1], 4));
                            required = Math.Max(required, u2);
                        }
                        if (m_renderer.SHBands >= 3)
                        {
                            var seg3 = m_sh3Segments[segIndex];
                            var u3 = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                                seg3.DeltaBytes.AsSpan(seg3.BlockOffsets[rel - 1], 4));
                            required = Math.Max(required, u3);
                        }
                        EnsureShDeltaUpdatesCapacity(required);

                        // band1
                        var wrote = ReadUpdateBlock(seg, rel, effectiveSplatCount, seg.LabelCount, m_shDeltaUpdatesScratch);
                        if (!TryDispatchShDeltaUpdates(1, m_sh1CentroidsBuffer, m_kernelApplySh1, m_shDeltaUpdatesScratch, wrote))
                            return false;
                        for (var i = 0; i < wrote; i++)
                            m_sh1Labels[m_shDeltaUpdatesScratch[i].splatId] = (ushort)m_shDeltaUpdatesScratch[i].label;

                        // band2/band3(可选)
                        if (m_renderer.SHBands >= 2)
                        {
                            var seg2 = m_sh2Segments[segIndex];
                            wrote = ReadUpdateBlock(seg2, rel, effectiveSplatCount, seg2.LabelCount, m_shDeltaUpdatesScratch);
                            if (!TryDispatchShDeltaUpdates(2, m_sh2CentroidsBuffer, m_kernelApplySh2, m_shDeltaUpdatesScratch, wrote))
                                return false;
                            for (var i = 0; i < wrote; i++)
                                m_sh2Labels[m_shDeltaUpdatesScratch[i].splatId] = (ushort)m_shDeltaUpdatesScratch[i].label;
                        }
                        if (m_renderer.SHBands >= 3)
                        {
                            var seg3 = m_sh3Segments[segIndex];
                            wrote = ReadUpdateBlock(seg3, rel, effectiveSplatCount, seg3.LabelCount, m_shDeltaUpdatesScratch);
                            if (!TryDispatchShDeltaUpdates(3, m_sh3CentroidsBuffer, m_kernelApplySh3, m_shDeltaUpdatesScratch, wrote))
                                return false;
                            for (var i = 0; i < wrote; i++)
                                m_sh3Labels[m_shDeltaUpdatesScratch[i].splatId] = (ushort)m_shDeltaUpdatesScratch[i].label;
                        }

                        m_shDeltaCurrentFrame = targetFrame;
                        return true;
                    }
                }
            }

            // 兜底: seek/jump/backward -> 从 segment base 解码到目标帧,再 diff 应用.
            var newSegIndex = FindSegmentIndex(m_sh1Segments, targetFrame);
            if (newSegIndex < 0)
                return false;

            EnsureShDeltaUpdatesCapacity(effectiveSplatCount);

            // band1
            var s1 = m_sh1Segments[newSegIndex];
            DecodeLabelsAtFrame(s1, targetFrame, effectiveSplatCount, s1.LabelCount, m_shLabelsScratch);
            var wroteDiff = 0;
            for (var i = 0; i < effectiveSplatCount; i++)
            {
                var cur = m_sh1Labels[i];
                var next = m_shLabelsScratch[i];
                if (cur == next)
                    continue;
                m_shDeltaUpdatesScratch[wroteDiff++] = new ShDeltaUpdate { splatId = (uint)i, label = next };
            }
            if (!TryDispatchShDeltaUpdates(1, m_sh1CentroidsBuffer, m_kernelApplySh1, m_shDeltaUpdatesScratch, wroteDiff))
                return false;
            for (var i = 0; i < wroteDiff; i++)
                m_sh1Labels[m_shDeltaUpdatesScratch[i].splatId] = (ushort)m_shDeltaUpdatesScratch[i].label;

            // band2/band3
            if (m_renderer.SHBands >= 2)
            {
                var s2 = m_sh2Segments[newSegIndex];
                DecodeLabelsAtFrame(s2, targetFrame, effectiveSplatCount, s2.LabelCount, m_shLabelsScratch);
                wroteDiff = 0;
                for (var i = 0; i < effectiveSplatCount; i++)
                {
                    var cur = m_sh2Labels[i];
                    var next = m_shLabelsScratch[i];
                    if (cur == next)
                        continue;
                    m_shDeltaUpdatesScratch[wroteDiff++] = new ShDeltaUpdate { splatId = (uint)i, label = next };
                }
                if (!TryDispatchShDeltaUpdates(2, m_sh2CentroidsBuffer, m_kernelApplySh2, m_shDeltaUpdatesScratch, wroteDiff))
                    return false;
                for (var i = 0; i < wroteDiff; i++)
                    m_sh2Labels[m_shDeltaUpdatesScratch[i].splatId] = (ushort)m_shDeltaUpdatesScratch[i].label;
            }
            if (m_renderer.SHBands >= 3)
            {
                var s3 = m_sh3Segments[newSegIndex];
                DecodeLabelsAtFrame(s3, targetFrame, effectiveSplatCount, s3.LabelCount, m_shLabelsScratch);
                wroteDiff = 0;
                for (var i = 0; i < effectiveSplatCount; i++)
                {
                    var cur = m_sh3Labels[i];
                    var next = m_shLabelsScratch[i];
                    if (cur == next)
                        continue;
                    m_shDeltaUpdatesScratch[wroteDiff++] = new ShDeltaUpdate { splatId = (uint)i, label = next };
                }
                if (!TryDispatchShDeltaUpdates(3, m_sh3CentroidsBuffer, m_kernelApplySh3, m_shDeltaUpdatesScratch, wroteDiff))
                    return false;
                for (var i = 0; i < wroteDiff; i++)
                    m_sh3Labels[m_shDeltaUpdatesScratch[i].splatId] = (ushort)m_shDeltaUpdatesScratch[i].label;
            }

            m_shDeltaCurrentSegmentIndex = newSegIndex;
            m_shDeltaCurrentFrame = targetFrame;
            return true;
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
            {
                // renderer 创建失败时也要清理 delta 资源,避免旧状态残留.
                TryInitShDeltaRuntime();
                return;
            }
#if UNITY_EDITOR
            if (AsyncUpload && Application.isPlaying)
#else
            if (AsyncUpload)
#endif
                SetBufferDataAsync();
            else
                SetBufferData();

            // 初始化 delta runtime(若 asset 没有 delta 字段,这里会自动 no-op).
            TryInitShDeltaRuntime();

            // 避免下一帧 Update 再次重复触发一次重建.
            m_prevAsset = GsplatAsset;
        }

        void OnDisable()
        {
            GsplatSorter.Instance.UnregisterGsplat(this);
            DisposeShDeltaResources();
            m_renderer?.Dispose();
            m_renderer = null;
            m_prevAsset = null;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // 编辑态拖动 `TimeNormalized` 时,SceneView 往往不会像 GameView 那样稳定触发“排序+渲染”的完整链路.
            // 这里的目标是: 你在 Inspector 拖动滑条时,SceneView 立刻 Repaint,并且排序使用最新的时间参数.
            if (Application.isPlaying)
                return;

            var t = TimeNormalized;
            if (float.IsNaN(t) || float.IsInfinity(t))
                t = 0.0f;
            t = Mathf.Clamp01(t);

            m_timeNormalizedThisFrame = t;

            // 触发 Editor 的渲染循环:
            // - QueuePlayerLoopUpdate: 让 ExecuteAlways 的 Update 尽快执行(包括动态 SH(delta)等逻辑).
            // - RepaintAll: 让 SceneView 相机立刻渲染,从而触发按相机排序(beginCameraRendering).
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditor.SceneView.RepaintAll();

            // 可选: 如果启用了 `.splat4d` 动态 SH(delta-v1),尽量在编辑态拖动时也同步刷新一次.
            TryApplyShDeltaForTime(t);
        }
#endif

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

                // asset 或 renderer 发生变化时,delta runtime 必须重建(包括清理旧资源).
                TryInitShDeltaRuntime();
            }

            // 在渲染前按 TimeNormalized 应用 delta-v1 updates(仅在帧变化时 dispatch).
            TryApplyShDeltaForTime(m_timeNormalizedThisFrame);

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
