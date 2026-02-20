// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using UnityEngine;

namespace Gsplat
{
    /// <summary>
    /// Keyframe 序列的插值模式.
    /// - Nearest: 只采样 i0 帧.
    /// - Linear: 采样 i0/i1 并按 a 插值.
    /// </summary>
    public enum GsplatInterpolationMode
    {
        Nearest = 0,
        Linear = 1
    }

    /// <summary>
    /// `.sog4d` 的 layout 描述.
    /// 当前 spec 只定义 row-major.
    /// </summary>
    [Serializable]
    public struct GsplatSequenceLayout
    {
        /// <summary>
        /// 布局类型.
        /// 目前只允许 "row-major"(由 importer 校验).
        /// </summary>
        public string Type;

        /// <summary>
        /// 属性图宽度(像素).
        /// </summary>
        public int Width;

        /// <summary>
        /// 属性图高度(像素).
        /// </summary>
        public int Height;
    }

    /// <summary>
    /// `.sog4d` 的时间轴映射.
    /// - Uniform: 等间隔分布在 [0,1].
    /// - Explicit: 使用显式时间数组.
    /// </summary>
    [Serializable]
    public struct GsplatSequenceTimeMapping
    {
        public enum MappingType
        {
            Uniform = 0,
            Explicit = 1
        }

        public MappingType Type;

        /// <summary>
        /// 当 Type=Explicit 时生效.
        /// 每一帧的时间(归一化到 [0,1]),长度应等于 FrameCount.
        /// </summary>
        public float[] FrameTimesNormalized;

        /// <summary>
        /// 根据 specs/4dgs-keyframe-motion 的定义,
        /// 从 TimeNormalized 计算相邻帧索引(i0,i1)与插值因子 a.
        /// </summary>
        public void EvaluateFromTimeNormalized(int frameCount, float timeNormalized, out int i0, out int i1,
            out float a)
        {
            // -----------------------------
            // 统一做 clamp,避免用户传入越界.
            // -----------------------------
            var t = Mathf.Clamp01(timeNormalized);

            // frameCount 非法时,给一个最安全的输出.
            if (frameCount <= 0)
            {
                i0 = 0;
                i1 = 0;
                a = 0.0f;
                return;
            }

            // -----------------------------
            // Uniform: O(1)
            // -----------------------------
            if (Type == MappingType.Uniform)
            {
                if (frameCount == 1)
                {
                    i0 = 0;
                    i1 = 0;
                    a = 0.0f;
                    return;
                }

                var u = t * (frameCount - 1);
                i0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, frameCount - 1);
                i1 = Mathf.Min(i0 + 1, frameCount - 1);
                a = Mathf.Clamp01(u - i0);
                return;
            }

            // -----------------------------
            // Explicit: 二分查找 O(logN)
            // - specs 要求单调非递减,但仍需要防御空数组/短数组.
            // -----------------------------
            var times = FrameTimesNormalized;
            if (times == null || times.Length == 0)
            {
                // 防御: explicit 却没给数组,回退到 0 帧.
                i0 = 0;
                i1 = 0;
                a = 0.0f;
                return;
            }

            // 防御: 以较小长度为准,避免数组越界.
            var n = Mathf.Min(frameCount, times.Length);
            if (n <= 1)
            {
                i0 = 0;
                i1 = 0;
                a = 0.0f;
                return;
            }

            // clamp 到首尾帧.
            if (t <= times[0])
            {
                i0 = 0;
                i1 = 0;
                a = 0.0f;
                return;
            }

            if (t >= times[n - 1])
            {
                i0 = n - 1;
                i1 = n - 1;
                a = 0.0f;
                return;
            }

            // 二分: 找到满足 times[i0] <= t <= times[i1] 的相邻帧.
            var left = 0;
            var right = n - 1;
            while (left + 1 < right)
            {
                var mid = (left + right) / 2;
                if (times[mid] <= t)
                    left = mid;
                else
                    right = mid;
            }

            i0 = left;
            i1 = right;

            // 避免除以 0: 当 t1==t0 时定义 a=0(与 spec 一致).
            var t0 = times[i0];
            var t1 = times[i1];
            if (t1 > t0)
                a = Mathf.Clamp01((t - t0) / (t1 - t0));
            else
                a = 0.0f;
        }
    }

    /// <summary>
    /// `.sog4d` 导入后的可播放序列资产.
    /// 注意: 这是 Runtime 可引用的 ScriptableObject,
    /// 具体导入逻辑由 Editor 的 ScriptedImporter 负责.
    /// </summary>
    public sealed class GsplatSequenceAsset : ScriptableObject
    {
        // --------------------------------------------------------------------
        // 基础元数据
        // --------------------------------------------------------------------
        // `.sog4d` meta.json.version.
        // - v1: 单一 SHN(palette+labels).
        // - v2: SH rest 按 band 拆分为 sh1/sh2/sh3 三套(palette+labels).
        public int Sog4DVersion;

        public uint SplatCount;
        public int FrameCount;
        public byte SHBands; // 0..3

        public GsplatSequenceTimeMapping TimeMapping;
        public GsplatSequenceLayout Layout;

        // --------------------------------------------------------------------
        // Bounds
        // - UnionBounds: 覆盖所有帧,用于保守剔除.
        // - PerFrameBounds: 可选,用于调试/更精细剔除策略.
        // --------------------------------------------------------------------
        public Bounds UnionBounds;
        [HideInInspector] public Bounds[] PerFrameBounds;

        // --------------------------------------------------------------------
        // Position stream(量化纹理 + per-frame range)
        // --------------------------------------------------------------------
        [HideInInspector] public Vector3[] PositionRangeMin;
        [HideInInspector] public Vector3[] PositionRangeMax;
        [HideInInspector] public Texture2DArray PositionHi;
        [HideInInspector] public Texture2DArray PositionLo;

        // --------------------------------------------------------------------
        // Scale stream(codebook + per-frame indices)
        // --------------------------------------------------------------------
        [HideInInspector] public Vector3[] ScaleCodebook;
        [HideInInspector] public Texture2DArray ScaleIndices;

        // --------------------------------------------------------------------
        // Rotation stream(per-frame quantized quaternion)
        // --------------------------------------------------------------------
        [HideInInspector] public Texture2DArray Rotation;

        // --------------------------------------------------------------------
        // SH0 stream(DC + opacity)
        // - `Sh0` 为 per-frame 数据图(RGBA8).
        // - `Sh0Codebook` 把 byte 索引映射回 float 的 f_dc 系数.
        // --------------------------------------------------------------------
        [HideInInspector] public Texture2DArray Sh0;
        [HideInInspector] public float[] Sh0Codebook;

        // --------------------------------------------------------------------
        // SH rest stream(可选)
        // - v1: palette(bin) + labels(webp 或 delta-v1 展开).
        // - v2: 仍是 palette+labels,但按 band 拆成 sh1/sh2/sh3 三套.
        // - 导入期(Editor)可以把 delta 展开为 per-frame labels,以保持运行时随机访问简单.
        // --------------------------------------------------------------------
        [HideInInspector] public int ShNCount;
        [HideInInspector] public string ShNCentroidsType; // "f16" | "f32"
        [HideInInspector] public byte[] ShNCentroidsBytes; // `shN_centroids.bin` 的原始字节
        [HideInInspector] public Texture2DArray ShNLabels;

        // --------------------------------------------------------------------
        // v2: SH rest 按 band 拆分(可选)
        // - bands>=1: sh1 必需.
        // - bands>=2: sh2 必需.
        // - bands>=3: sh3 必需.
        // --------------------------------------------------------------------
        [HideInInspector] public int Sh1Count;
        [HideInInspector] public string Sh1CentroidsType; // "f16" | "f32"
        [HideInInspector] public byte[] Sh1CentroidsBytes; // `sh1_centroids.bin`
        [HideInInspector] public Texture2DArray Sh1Labels;

        [HideInInspector] public int Sh2Count;
        [HideInInspector] public string Sh2CentroidsType; // "f16" | "f32"
        [HideInInspector] public byte[] Sh2CentroidsBytes; // `sh2_centroids.bin`
        [HideInInspector] public Texture2DArray Sh2Labels;

        [HideInInspector] public int Sh3Count;
        [HideInInspector] public string Sh3CentroidsType; // "f16" | "f32"
        [HideInInspector] public byte[] Sh3CentroidsBytes; // `sh3_centroids.bin`
        [HideInInspector] public Texture2DArray Sh3Labels;
    }
}
