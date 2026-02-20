// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using UnityEngine;

namespace Gsplat
{
    public static class GsplatUtils
    {
        public const string k_PackagePath = "Packages/wu.yize.gsplat/";

        public static float Sigmoid(float x)
        {
            return 1.0f / (1.0f + Mathf.Exp(-x));
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
