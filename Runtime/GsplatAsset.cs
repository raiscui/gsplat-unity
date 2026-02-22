// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using UnityEngine;

namespace Gsplat
{
    // --------------------------------------------------------------------
    // `.splat4d format v2` 的 SH delta-v1 承载结构(可选)
    // - Importer 读取 SHLB(base labels) 与 SHDL(delta bytes)后填充.
    // - Runtime 用它在播放时应用稀疏 label updates,并通过 compute shader 更新 SHBuffer.
    // --------------------------------------------------------------------
    [Serializable]
    public sealed class Splat4DShDeltaSegment
    {
        public int StartFrame;
        public int FrameCount;

        // base labels(u16[N]) 的原始小端 bytes.长度必须为 splatCount*2.
        // 备注: 使用 byte[] 是为了确保 Unity 序列化稳定(避免 ushort[] 在不同 Unity 版本下兼容性不一致).
        public byte[] BaseLabelsBytes;

        // delta-v1 原始 bytes(含 header+body).Importer 会做 header 校验.
        public byte[] DeltaBytes;
    }

    public class GsplatAsset : ScriptableObject
    {
        public uint SplatCount;
        public byte SHBands; // 0, 1, 2, or 3
        public Bounds Bounds;
        [HideInInspector] public Vector3[] Positions;
        [HideInInspector] public Vector4[] Colors; // RGB, Opacity
        [HideInInspector] public Vector3[] SHs;
        [HideInInspector] public Vector3[] Scales;
        [HideInInspector] public Vector4[] Rotations; // Quaternion, wxyz

        // --------------------------------------------------------------------
        // 4DGS 扩展字段(可选)
        // - 当导入器未提供这些字段时,它们会保持为 null.
        // - Runtime 侧会把 null 视为静态默认值:
        //   - velocity=0
        //   - time=0
        //   - duration=1
        // --------------------------------------------------------------------
        [HideInInspector] public Vector3[] Velocities; // 每 1.0 归一化时间的位移(对象空间)
        [HideInInspector] public float[] Times; // 归一化起始时间 [0,1]
        [HideInInspector] public float[] Durations; // 归一化持续时间 [0,1]

        // --------------------------------------------------------------------
        // 时间核语义(仅在 4D 字段存在时使用)
        // - 1(window): visible iff t0 <= t <= t0+duration
        // - 2(gaussian): temporalWeight = exp(-0.5 * ((t-mu)/sigma)^2)
        //
        // 兼容性约定:
        // - 旧资产没有该字段时,反序列化默认值可能为 0. Runtime 侧应把 0 视为 window.
        // --------------------------------------------------------------------
        [HideInInspector] public byte TimeModel; // 0/1=window, 2=gaussian
        [HideInInspector] public float TemporalGaussianCutoff; // 例如 0.01,用于把极小权重视为不可见

        // --------------------------------------------------------------------
        // Motion 统计(用于 bounds 扩展等保守估算)
        // --------------------------------------------------------------------
        [HideInInspector] public float MaxSpeed; // max(|velocity|)
        [HideInInspector] public float MaxDuration; // max(duration)

        // --------------------------------------------------------------------
        // `.splat4d v2` SH labelsEncoding=delta-v1(可选)
        // - 当这些字段为空时,视为静态 SH(保持旧行为).
        // - Frame 语义: frame = round(TimeNormalized*(ShFrameCount-1)).
        // --------------------------------------------------------------------
        [HideInInspector] public int ShFrameCount;

        // per-band centroids(解码后的 float3 数组,entry-major: [label][coeff]).
        [HideInInspector] public Vector3[] Sh1Centroids;
        [HideInInspector] public Vector3[] Sh2Centroids;
        [HideInInspector] public Vector3[] Sh3Centroids;

        // per-band delta segments(每段含 base labels + delta bytes).
        [HideInInspector] public Splat4DShDeltaSegment[] Sh1DeltaSegments;
        [HideInInspector] public Splat4DShDeltaSegment[] Sh2DeltaSegments;
        [HideInInspector] public Splat4DShDeltaSegment[] Sh3DeltaSegments;
    }
}
