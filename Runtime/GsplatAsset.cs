// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEngine;

namespace Gsplat
{
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
    }
}
