// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace Gsplat
{
    /// <summary>
    /// 运行时 `.sog4d` bundle 读取器(面向 Player build).
    ///
    /// 设计目标(对齐 OpenSpec tasks 9.1/9.2):
    /// - 允许在 Player build 中直接从 `.sog4d`(ZIP bundle)创建可播放的 <see cref="GsplatSequenceAsset"/>.
    /// - 支持按需加载“帧 chunk”,在长序列下显著降低显存峰值.
    ///
    /// 重要约束:
    /// - WebP 解码依赖 Unity 的 <see cref="ImageConversion.LoadImage"/>.
    ///   如果宿主 Unity 版本不支持 WebP,这里会 fail-fast 并返回可行动错误.
    /// - chunk 策略采用 "overlap=1":
    ///   下一 chunk 会重复包含上一 chunk 的最后一帧,从而保证任意相邻两帧(i0,i1)总能在同一个 chunk 内.
    ///   这样可以保持现有 decode compute shader 不变(仍然只绑定一套 Texture2DArray).
    /// </summary>
    internal sealed class GsplatSog4DRuntimeBundle : IDisposable
    {
        const int k_supportedVersion = 1;

        // --------------------------------------------------------------------
        // Json DTO(与 Editor importer 保持一致,字段名需与 meta.json 完全一致)
        // --------------------------------------------------------------------
        [Serializable]
        sealed class Sog4DMetaJson
        {
            public string format;
            public int version;
            public int splatCount;
            public int frameCount;
            public TimeMappingJson timeMapping;
            public LayoutJson layout;
            public StreamsJson streams;
        }

        [Serializable]
        sealed class TimeMappingJson
        {
            public string type; // "uniform" | "explicit"
            public float[] frameTimesNormalized;
        }

        [Serializable]
        sealed class LayoutJson
        {
            public string type; // "row-major"
            public int width;
            public int height;
        }

        [Serializable]
        sealed class StreamsJson
        {
            public PositionStreamJson position;
            public ScaleStreamJson scale;
            public RotationStreamJson rotation;
            public ShStreamJson sh;
        }

        [Serializable]
        sealed class PositionStreamJson
        {
            public Vector3[] rangeMin;
            public Vector3[] rangeMax;
            public string hiPath;
            public string loPath;
        }

        [Serializable]
        sealed class ScaleStreamJson
        {
            public Vector3[] codebook;
            public string indicesPath;
        }

        [Serializable]
        sealed class RotationStreamJson
        {
            public string path;
        }

        [Serializable]
        sealed class ShStreamJson
        {
            public int bands; // 0..3
            public string sh0Path;
            public float[] sh0Codebook; // len=256

            // bands>0
            public int shNCount;
            public string shNCentroidsType; // "f16" | "f32"
            public string shNCentroidsPath; // e.g. "shN_centroids.bin"
            public string shNLabelsEncoding; // "full" | "delta-v1"
            public string shNLabelsPath; // template
            public ShNDeltaSegmentJson[] shNDeltaSegments;
        }

        [Serializable]
        sealed class ShNDeltaSegmentJson
        {
            public int startFrame;
            public int frameCount;
            public string baseLabelsPath;
            public string deltaPath;
        }

        // --------------------------------------------------------------------
        // Bundle state
        // --------------------------------------------------------------------
        readonly MemoryStream m_stream;
        readonly ZipArchive m_archive;
        readonly Dictionary<string, ZipArchiveEntry> m_entriesByName;

        readonly Sog4DMetaJson m_meta;

        // --------------------------------------------------------------------
        // Chunk state
        // --------------------------------------------------------------------
        readonly bool m_enableChunking;
        readonly int m_chunkFrameCountRequested;
        readonly int m_chunkStep; // chunkFrameCount - 1 (overlap=1)

        int m_loadedChunkStartFrame;
        int m_loadedChunkFrameCount;

        public int LoadedChunkStartFrame => m_loadedChunkStartFrame;
        public int LoadedChunkFrameCount => m_loadedChunkFrameCount;
        public bool ChunkingEnabled => m_enableChunking && m_chunkFrameCountRequested > 0;

        GsplatSog4DRuntimeBundle(
            MemoryStream stream,
            ZipArchive archive,
            Dictionary<string, ZipArchiveEntry> entriesByName,
            Sog4DMetaJson meta,
            bool enableChunking,
            int chunkFrameCountRequested)
        {
            m_stream = stream;
            m_archive = archive;
            m_entriesByName = entriesByName;
            m_meta = meta;

            m_enableChunking = enableChunking;
            m_chunkFrameCountRequested = chunkFrameCountRequested;
            m_chunkStep = Mathf.Max(1, m_chunkFrameCountRequested - 1);

            m_loadedChunkStartFrame = 0;
            m_loadedChunkFrameCount = 0;
        }

        public void Dispose()
        {
            m_archive?.Dispose();
            m_stream?.Dispose();
        }

        public static bool TryOpen(byte[] sog4dBytes, bool enableChunking, int chunkFrameCountRequested,
            out GsplatSog4DRuntimeBundle bundle, out string error)
        {
            bundle = null;
            error = null;

            if (sog4dBytes == null || sog4dBytes.Length == 0)
            {
                error = "RuntimeSog4dBundle bytes is empty.";
                return false;
            }

            try
            {
                var stream = new MemoryStream(sog4dBytes, writable: false);
                var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
                var entriesByName = BuildEntryMap(archive);

                if (!entriesByName.TryGetValue("meta.json", out var metaEntry))
                {
                    archive.Dispose();
                    stream.Dispose();
                    error = "bundle missing required file: meta.json";
                    return false;
                }

                if (!TryReadZipEntryUtf8Text(metaEntry, out var metaJsonText, out var metaReadError))
                {
                    archive.Dispose();
                    stream.Dispose();
                    error = $"failed to read meta.json: {metaReadError}";
                    return false;
                }

                Sog4DMetaJson meta;
                try
                {
                    meta = JsonUtility.FromJson<Sog4DMetaJson>(metaJsonText);
                }
                catch (Exception e)
                {
                    archive.Dispose();
                    stream.Dispose();
                    error = $"meta.json parse error: {e.Message}";
                    return false;
                }

                if (meta == null)
                {
                    archive.Dispose();
                    stream.Dispose();
                    error = "meta.json parse error: JsonUtility returned null";
                    return false;
                }

                if (!TryValidateMetaBasics(meta, entriesByName, out var validateErr))
                {
                    archive.Dispose();
                    stream.Dispose();
                    error = validateErr;
                    return false;
                }

                // chunk 参数的防御:
                // - chunkFrameCount <= 0 => 视为不启用 chunk.
                // - chunkFrameCount == 1 => overlap=1 不成立,回退到 full load.
                var useChunking = enableChunking && chunkFrameCountRequested >= 2 && meta.frameCount > chunkFrameCountRequested;

                bundle = new GsplatSog4DRuntimeBundle(stream, archive, entriesByName, meta, useChunking,
                    chunkFrameCountRequested);
                return true;
            }
            catch (InvalidDataException e)
            {
                error = $"invalid ZIP bundle: {e.Message}";
                return false;
            }
            catch (Exception e)
            {
                error = $"unexpected runtime bundle exception: {e.Message}";
                return false;
            }
        }

        public bool TryCreateSequenceAsset(out GsplatSequenceAsset asset, out string error)
        {
            asset = null;
            error = null;

            try
            {
                var a = ScriptableObject.CreateInstance<GsplatSequenceAsset>();
                a.name = "Sog4D_RuntimeSequenceAsset";

                a.SplatCount = (uint)m_meta.splatCount;
                a.FrameCount = m_meta.frameCount;
                a.Layout = new GsplatSequenceLayout
                {
                    Type = m_meta.layout.type,
                    Width = m_meta.layout.width,
                    Height = m_meta.layout.height
                };

                a.PositionRangeMin = m_meta.streams.position.rangeMin;
                a.PositionRangeMax = m_meta.streams.position.rangeMax;
                a.ScaleCodebook = m_meta.streams.scale.codebook;
                a.SHBands = (byte)Mathf.Clamp(m_meta.streams.sh.bands, 0, 3);
                a.Sh0Codebook = m_meta.streams.sh.sh0Codebook;

                if (!TryBuildBoundsFromPositionRanges(a.PositionRangeMin, a.PositionRangeMax, out var unionBounds,
                        out var perFrameBounds, out var boundsErr))
                {
                    UnityEngine.Object.Destroy(a);
                    error = boundsErr;
                    return false;
                }

                a.UnionBounds = unionBounds;
                a.PerFrameBounds = perFrameBounds;

                // timeMapping
                if (!TryBuildTimeMappingRuntime(m_meta, out var mappingRuntime, out var timeErr))
                {
                    UnityEngine.Object.Destroy(a);
                    error = timeErr;
                    return false;
                }
                a.TimeMapping = mappingRuntime;

                // SH rest palette
                if (a.SHBands > 0)
                {
                    a.ShNCount = m_meta.streams.sh.shNCount;
                    a.ShNCentroidsType = m_meta.streams.sh.shNCentroidsType;

                    var centroidsPath = NormalizeZipPath(m_meta.streams.sh.shNCentroidsPath);
                    if (!m_entriesByName.TryGetValue(centroidsPath, out var centroidsEntry))
                    {
                        UnityEngine.Object.Destroy(a);
                        error = $"bundle missing referenced file: {centroidsPath}";
                        return false;
                    }

                    if (!TryReadZipEntryBytes(centroidsEntry, out var centroidsBytes, out var centroidsReadErr))
                    {
                        UnityEngine.Object.Destroy(a);
                        error = $"failed to read shN_centroids.bin: {centroidsPath}: {centroidsReadErr}";
                        return false;
                    }

                    // 基本尺寸校验(对齐 importer 行为,避免运行时黑盒).
                    var restCoeffCount = (a.SHBands + 1) * (a.SHBands + 1) - 1;
                    var scalarBytes = a.ShNCentroidsType == "f16" ? 2 : 4;
                    var expectedBytes = (long)a.ShNCount * restCoeffCount * 3L * scalarBytes;
                    if (centroidsBytes.LongLength != expectedBytes)
                    {
                        UnityEngine.Object.Destroy(a);
                        error =
                            $"shN_centroids.bin size mismatch: expected {expectedBytes} bytes, got {centroidsBytes.LongLength} bytes. " +
                            $"(shNCount={a.ShNCount}, restCoeffCount={restCoeffCount}, type={a.ShNCentroidsType})";
                        return false;
                    }

                    a.ShNCentroidsBytes = centroidsBytes;
                }

                // 初始 chunk: 以 (0,1) 为目标,确保播放起始可用.
                var desiredStart = 0;
                var desiredCount = m_enableChunking ? Mathf.Min(m_chunkFrameCountRequested, a.FrameCount) : a.FrameCount;
                if (!TryLoadChunkTexturesIntoAsset(a, desiredStart, desiredCount, out var loadErr))
                {
                    DestroySequenceAssetAndTextures(a);
                    error = loadErr;
                    return false;
                }

                m_loadedChunkStartFrame = desiredStart;
                m_loadedChunkFrameCount = desiredCount;

                asset = a;
                return true;
            }
            catch (Exception e)
            {
                error = $"failed to create runtime GsplatSequenceAsset: {e.Message}";
                return false;
            }
        }

        public bool TryEnsureChunkForFramePair(GsplatSequenceAsset asset, int frame0, int frame1, out int local0,
            out int local1, out string error)
        {
            local0 = frame0;
            local1 = frame1;
            error = null;

            if (asset == null)
            {
                error = "SequenceAsset is null.";
                return false;
            }

            // full-load 模式下,全局索引就是 layer 索引.
            if (!ChunkingEnabled)
                return true;

            var i0 = Mathf.Clamp(frame0, 0, asset.FrameCount - 1);
            var i1 = Mathf.Clamp(frame1, 0, asset.FrameCount - 1);
            // overlap=1 的关键点:
            // - chunkStart 必须以 i0 计算,而不是 i1.
            // - 反例: chunkFrameCount=50(step=49)时,相邻帧 (48,49) 应落在 chunkStart=0,
            //         若用 i1=49 计算会错误选择 chunkStart=49,从而缺少 frame 48.
            var t = i0;

            // overlap=1 chunk 选择策略:
            // - 用较大的帧索引 t 计算 chunkStart,可保证相邻帧跨边界时选择“后一个 chunk”.
            // - chunkStart = floor(t / step) * step, step=chunkFrameCount-1.
            var desiredStart = (t / m_chunkStep) * m_chunkStep;
            var desiredCount = Mathf.Min(m_chunkFrameCountRequested, asset.FrameCount - desiredStart);

            if (desiredStart != m_loadedChunkStartFrame || desiredCount != m_loadedChunkFrameCount ||
                asset.PositionHi == null || asset.PositionHi.depth != desiredCount)
            {
                if (!TryLoadChunkTexturesIntoAsset(asset, desiredStart, desiredCount, out var loadErr))
                {
                    error = loadErr;
                    return false;
                }

                m_loadedChunkStartFrame = desiredStart;
                m_loadedChunkFrameCount = desiredCount;
            }

            local0 = i0 - m_loadedChunkStartFrame;
            local1 = i1 - m_loadedChunkStartFrame;
            if (local0 < 0 || local1 < 0 || local0 >= m_loadedChunkFrameCount || local1 >= m_loadedChunkFrameCount)
            {
                // 理论上不会发生,发生了说明 chunk 选择逻辑有 bug.
                error =
                    $"runtime chunk mapping bug: frame0={i0}, frame1={i1}, loadedStart={m_loadedChunkStartFrame}, loadedCount={m_loadedChunkFrameCount}";
                return false;
            }

            return true;
        }

        // --------------------------------------------------------------------
        // Meta validation(basics)
        // --------------------------------------------------------------------
        static bool TryValidateMetaBasics(Sog4DMetaJson meta, Dictionary<string, ZipArchiveEntry> entriesByName,
            out string error)
        {
            error = null;

            if (!string.Equals(meta.format, "sog4d", StringComparison.Ordinal))
            {
                error = $"meta.json invalid format: expected \"sog4d\", got \"{meta.format}\"";
                return false;
            }

            if (meta.version != k_supportedVersion)
            {
                error = $"meta.json unsupported version: expected {k_supportedVersion}, got {meta.version}";
                return false;
            }

            if (meta.splatCount <= 0)
            {
                error = $"meta.json invalid splatCount: {meta.splatCount}";
                return false;
            }

            if (meta.frameCount <= 0)
            {
                error = $"meta.json invalid frameCount: {meta.frameCount}";
                return false;
            }

            if (meta.timeMapping == null || string.IsNullOrEmpty(meta.timeMapping.type))
            {
                error = "meta.json missing required field: timeMapping.type";
                return false;
            }

            if (meta.layout == null)
            {
                error = "meta.json missing required field: layout";
                return false;
            }

            if (meta.layout.type != "row-major")
            {
                error = $"meta.json invalid layout.type: expected \"row-major\", got \"{meta.layout.type}\"";
                return false;
            }

            if (meta.layout.width <= 0 || meta.layout.height <= 0)
            {
                error = $"meta.json invalid layout size: width={meta.layout.width}, height={meta.layout.height}";
                return false;
            }

            var pixelCapacity = (long)meta.layout.width * meta.layout.height;
            if (pixelCapacity < meta.splatCount)
            {
                error = $"meta.json invalid layout size: width*height={pixelCapacity} < splatCount={meta.splatCount}";
                return false;
            }

            if (meta.streams == null)
            {
                error = "meta.json missing required field: streams";
                return false;
            }

            // 仅做最关键的结构校验:
            // - 具体文件存在性与 WebP 尺寸校验会在 chunk 加载时做 fail-fast.
            if (meta.streams.position == null || string.IsNullOrEmpty(meta.streams.position.hiPath) ||
                string.IsNullOrEmpty(meta.streams.position.loPath))
            {
                error = "meta.json missing required stream: streams.position";
                return false;
            }

            if (meta.streams.scale == null || meta.streams.scale.codebook == null ||
                meta.streams.scale.codebook.Length == 0 || string.IsNullOrEmpty(meta.streams.scale.indicesPath))
            {
                error = "meta.json missing required stream: streams.scale";
                return false;
            }

            if (meta.streams.rotation == null || string.IsNullOrEmpty(meta.streams.rotation.path))
            {
                error = "meta.json missing required stream: streams.rotation";
                return false;
            }

            if (meta.streams.sh == null || string.IsNullOrEmpty(meta.streams.sh.sh0Path) ||
                meta.streams.sh.sh0Codebook == null || meta.streams.sh.sh0Codebook.Length != 256)
            {
                error = "meta.json missing required stream: streams.sh";
                return false;
            }

            if (meta.streams.sh.bands < 0 || meta.streams.sh.bands > 3)
            {
                error = $"meta.json invalid streams.sh.bands: {meta.streams.sh.bands} (must be 0..3)";
                return false;
            }

            if (meta.streams.sh.bands == 0)
                return true;

            // SH bands>0: centroids 与 labels
            if (meta.streams.sh.shNCount <= 0 || meta.streams.sh.shNCount > 65535)
            {
                error = $"meta.json invalid streams.sh.shNCount: {meta.streams.sh.shNCount} (must be 1..65535)";
                return false;
            }

            if (meta.streams.sh.shNCentroidsType != "f16" && meta.streams.sh.shNCentroidsType != "f32")
            {
                error =
                    $"meta.json invalid streams.sh.shNCentroidsType: {meta.streams.sh.shNCentroidsType} (must be \"f16\" or \"f32\")";
                return false;
            }

            var centroidsPath = NormalizeZipPath(meta.streams.sh.shNCentroidsPath);
            if (!entriesByName.ContainsKey(centroidsPath))
            {
                error = $"bundle missing referenced file: {centroidsPath} (from streams.sh.shNCentroidsPath)";
                return false;
            }

            var labelsEncoding = meta.streams.sh.shNLabelsEncoding;
            if (string.IsNullOrEmpty(labelsEncoding))
                labelsEncoding = "full";

            if (labelsEncoding == "full")
            {
                if (string.IsNullOrEmpty(meta.streams.sh.shNLabelsPath))
                {
                    error = "meta.json missing required field: streams.sh.shNLabelsPath";
                    return false;
                }
                return true;
            }

            if (labelsEncoding != "delta-v1")
            {
                error = $"meta.json invalid streams.sh.shNLabelsEncoding: {labelsEncoding}";
                return false;
            }

            var segments = meta.streams.sh.shNDeltaSegments;
            if (segments == null || segments.Length == 0)
            {
                error = "meta.json missing required field: streams.sh.shNDeltaSegments";
                return false;
            }

            // 连续性校验(对齐 importer/spec),避免运行时随机跳帧时出现黑盒.
            var expectedStart = 0;
            var totalFrames = 0;
            for (var i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                if (seg == null)
                {
                    error = $"meta.json invalid shNDeltaSegments[{i}]: null";
                    return false;
                }

                if (seg.startFrame != expectedStart)
                {
                    error = $"meta.json invalid shNDeltaSegments[{i}].startFrame: expected {expectedStart}, got {seg.startFrame}";
                    return false;
                }

                if (seg.frameCount <= 0)
                {
                    error = $"meta.json invalid shNDeltaSegments[{i}].frameCount: {seg.frameCount}";
                    return false;
                }

                expectedStart = seg.startFrame + seg.frameCount;
                totalFrames += seg.frameCount;
            }

            if (segments[0].startFrame != 0)
            {
                error = "meta.json invalid shNDeltaSegments: first segment startFrame must be 0";
                return false;
            }

            if (totalFrames != meta.frameCount)
            {
                error = $"meta.json invalid shNDeltaSegments: total frameCount sum must be {meta.frameCount}, got {totalFrames}";
                return false;
            }

            return true;
        }

        // --------------------------------------------------------------------
        // Chunk texture loading
        // --------------------------------------------------------------------
        bool TryLoadChunkTexturesIntoAsset(GsplatSequenceAsset asset, int chunkStartFrame, int chunkFrameCount,
            out string error)
        {
            error = null;

            // 防御: 限制 chunk 参数.
            if (chunkStartFrame < 0 || chunkStartFrame >= asset.FrameCount)
            {
                error = $"invalid chunkStartFrame: {chunkStartFrame}";
                return false;
            }

            if (chunkFrameCount <= 0 || chunkStartFrame + chunkFrameCount > asset.FrameCount)
            {
                error = $"invalid chunkFrameCount: start={chunkStartFrame}, count={chunkFrameCount}, frameCount={asset.FrameCount}";
                return false;
            }

            // 先释放旧纹理,避免显存叠加.
            DestroyTextureIfAny(ref asset.PositionHi);
            DestroyTextureIfAny(ref asset.PositionLo);
            DestroyTextureIfAny(ref asset.ScaleIndices);
            DestroyTextureIfAny(ref asset.Rotation);
            DestroyTextureIfAny(ref asset.Sh0);
            DestroyTextureIfAny(ref asset.ShNLabels);

            var w = asset.Layout.Width;
            var h = asset.Layout.Height;

            // position
            if (!TryBuildTexture2DArrayFromPerFrameWebp(asset, m_meta.streams.position.hiPath, chunkStartFrame,
                    chunkFrameCount, w, h, "PositionHi", u16MaxExclusive: -1, out asset.PositionHi, out error))
                return false;
            if (!TryBuildTexture2DArrayFromPerFrameWebp(asset, m_meta.streams.position.loPath, chunkStartFrame,
                    chunkFrameCount, w, h, "PositionLo", u16MaxExclusive: -1, out asset.PositionLo, out error))
                return false;

            // scale indices(u16 RG)
            if (!TryBuildTexture2DArrayFromPerFrameWebp(asset, m_meta.streams.scale.indicesPath, chunkStartFrame,
                    chunkFrameCount, w, h, "ScaleIndices", u16MaxExclusive: m_meta.streams.scale.codebook.Length,
                    out asset.ScaleIndices, out error))
                return false;

            // rotation
            if (!TryBuildTexture2DArrayFromPerFrameWebp(asset, m_meta.streams.rotation.path, chunkStartFrame,
                    chunkFrameCount, w, h, "Rotation", u16MaxExclusive: -1, out asset.Rotation, out error))
                return false;

            // sh0
            if (!TryBuildTexture2DArrayFromPerFrameWebp(asset, m_meta.streams.sh.sh0Path, chunkStartFrame,
                    chunkFrameCount, w, h, "Sh0", u16MaxExclusive: -1, out asset.Sh0, out error))
                return false;

            // shN labels
            if (asset.SHBands > 0)
            {
                var labelsEncoding = m_meta.streams.sh.shNLabelsEncoding;
                if (string.IsNullOrEmpty(labelsEncoding))
                    labelsEncoding = "full";

                if (labelsEncoding == "full")
                {
                    if (!TryBuildTexture2DArrayFromPerFrameWebp(asset, m_meta.streams.sh.shNLabelsPath, chunkStartFrame,
                            chunkFrameCount, w, h, "ShNLabels", u16MaxExclusive: m_meta.streams.sh.shNCount,
                            out asset.ShNLabels, out error))
                        return false;
                }
                else if (labelsEncoding == "delta-v1")
                {
                    if (!TryBuildShNLabelsChunkFromDeltaV1(asset, chunkStartFrame, chunkFrameCount, w, h,
                            out asset.ShNLabels, out error))
                        return false;
                }
                else
                {
                    error = $"meta.json invalid streams.sh.shNLabelsEncoding: {labelsEncoding}";
                    return false;
                }
            }

            return true;
        }

        bool TryBuildTexture2DArrayFromPerFrameWebp(
            GsplatSequenceAsset asset,
            string template,
            int chunkStartFrame,
            int chunkFrameCount,
            int width,
            int height,
            string debugName,
            int u16MaxExclusive,
            out Texture2DArray array,
            out string error)
        {
            array = null;
            error = null;

            try
            {
                array = new Texture2DArray(width, height, chunkFrameCount, TextureFormat.RGBA32, mipChain: false,
                    linear: true)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0
                };

                for (var local = 0; local < chunkFrameCount; local++)
                {
                    var globalFrame = chunkStartFrame + local;
                    var path = NormalizeZipPath(template.Replace("{frame}", globalFrame.ToString("D5")));
                    if (!m_entriesByName.TryGetValue(path, out var entry))
                    {
                        DestroyTexture2DArray(ref array);
                        error = $"bundle missing referenced file: {path}";
                        return false;
                    }

                    if (!TryDecodeWebpToRgba32Texture(entry, debugName, out var decoded, out var decodeErr))
                    {
                        DestroyTexture2DArray(ref array);
                        error = $"failed to decode WebP: {path}: {decodeErr}";
                        return false;
                    }

                    try
                    {
                        if (decoded.width != width || decoded.height != height)
                        {
                            DestroyTexture2DArray(ref array);
                            error = $"WebP size mismatch: {path}: expected {width}x{height}, got {decoded.width}x{decoded.height}";
                            return false;
                        }

                        var pixels = decoded.GetPixels32();
                        if (u16MaxExclusive > 0)
                        {
                            if (!TryValidateU16RgIndexMap(pixels, m_meta.splatCount, u16MaxExclusive, out var badSplatId,
                                    out var badValue))
                            {
                                DestroyTexture2DArray(ref array);
                                error =
                                    $"{debugName} u16 index out of range: frame={globalFrame}, splatId={badSplatId}, value={badValue}, maxExclusive={u16MaxExclusive}. path={path}";
                                return false;
                            }
                        }

                        array.SetPixels32(pixels, local, 0);
                    }
                    finally
                    {
                        UnityEngine.Object.Destroy(decoded);
                    }
                }

                array.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                return true;
            }
            catch (Exception e)
            {
                DestroyTexture2DArray(ref array);
                error = $"failed to build Texture2DArray for {debugName}: {e.Message}";
                return false;
            }
        }

        bool TryBuildShNLabelsChunkFromDeltaV1(
            GsplatSequenceAsset asset,
            int chunkStartFrame,
            int chunkFrameCount,
            int width,
            int height,
            out Texture2DArray labelsArray,
            out string error)
        {
            labelsArray = null;
            error = null;

            var segments = m_meta.streams.sh.shNDeltaSegments;
            if (segments == null || segments.Length == 0)
            {
                error = "meta.json missing required field: streams.sh.shNDeltaSegments";
                return false;
            }

            try
            {
                labelsArray = new Texture2DArray(width, height, chunkFrameCount, TextureFormat.RGBA32, mipChain: false,
                    linear: true)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0
                };

                var chunkEndExclusive = chunkStartFrame + chunkFrameCount;

                for (var segIndex = 0; segIndex < segments.Length; segIndex++)
                {
                    var seg = segments[segIndex];
                    if (seg == null)
                        continue;

                    var segStart = seg.startFrame;
                    var segEndExclusive = seg.startFrame + seg.frameCount;
                    if (segEndExclusive <= chunkStartFrame)
                        continue;
                    if (segStart >= chunkEndExclusive)
                        break;

                    // 1) base labels
                    var basePath = NormalizeZipPath(seg.baseLabelsPath);
                    if (!m_entriesByName.TryGetValue(basePath, out var baseEntry))
                    {
                        DestroyTexture2DArray(ref labelsArray);
                        error = $"bundle missing referenced file: {basePath}";
                        return false;
                    }

                    if (!TryDecodeWebpToRgba32Texture(baseEntry, "ShNBaseLabels", out var baseTex, out var baseErr))
                    {
                        DestroyTexture2DArray(ref labelsArray);
                        error = $"failed to decode WebP: {basePath}: {baseErr}";
                        return false;
                    }

                    Color32[] current;
                    try
                    {
                        if (baseTex.width != width || baseTex.height != height)
                        {
                            DestroyTexture2DArray(ref labelsArray);
                            error = $"WebP size mismatch: {basePath}: expected {width}x{height}, got {baseTex.width}x{baseTex.height}";
                            return false;
                        }

                        current = baseTex.GetPixels32();
                    }
                    finally
                    {
                        UnityEngine.Object.Destroy(baseTex);
                    }

                    if (!TryValidateU16RgIndexMap(current, m_meta.splatCount, m_meta.streams.sh.shNCount, out var badSplatId,
                            out var badValue))
                    {
                        DestroyTexture2DArray(ref labelsArray);
                        error =
                            $"shN base labels out of range: segment={segIndex}, frame={seg.startFrame}, splatId={badSplatId}, label={badValue} >= shNCount={m_meta.streams.sh.shNCount}. path={basePath}";
                        return false;
                    }

                    // base frame 写入(如果在 chunk 内)
                    if (segStart >= chunkStartFrame && segStart < chunkEndExclusive)
                    {
                        labelsArray.SetPixels32(current, segStart - chunkStartFrame, 0);
                    }

                    // 2) delta file
                    var deltaPath = NormalizeZipPath(seg.deltaPath);
                    if (!m_entriesByName.TryGetValue(deltaPath, out var deltaEntry))
                    {
                        DestroyTexture2DArray(ref labelsArray);
                        error = $"bundle missing referenced file: {deltaPath}";
                        return false;
                    }

                    using (var s = deltaEntry.Open())
                    using (var br = new BinaryReader(s))
                    {
                        if (!TryValidateDeltaV1Header(br, segIndex, seg, m_meta, out var headerErr))
                        {
                            DestroyTexture2DArray(ref labelsArray);
                            error = headerErr;
                            return false;
                        }

                        // 应用 delta blocks.
                        for (var localFrame = 1; localFrame < seg.frameCount; localFrame++)
                        {
                            var globalFrame = segStart + localFrame;
                            if (globalFrame >= chunkEndExclusive)
                                break;

                            uint updateCount;
                            try
                            {
                                updateCount = br.ReadUInt32();
                            }
                            catch (EndOfStreamException)
                            {
                                DestroyTexture2DArray(ref labelsArray);
                                error = $"delta-v1 truncated: {deltaPath}: missing updateCount for frame {globalFrame}";
                                return false;
                            }

                            if (updateCount > (uint)m_meta.splatCount)
                            {
                                DestroyTexture2DArray(ref labelsArray);
                                error =
                                    $"delta-v1 invalid updateCount: {deltaPath}: updateCount={updateCount} > splatCount={m_meta.splatCount} at frame {globalFrame}";
                                return false;
                            }

                            var hasPrev = false;
                            uint prevSplatId = 0;
                            for (uint u = 0; u < updateCount; u++)
                            {
                                uint splatId;
                                ushort label;
                                ushort reserved;
                                try
                                {
                                    splatId = br.ReadUInt32();
                                    label = br.ReadUInt16();
                                    reserved = br.ReadUInt16();
                                }
                                catch (EndOfStreamException)
                                {
                                    DestroyTexture2DArray(ref labelsArray);
                                    error =
                                        $"delta-v1 truncated: {deltaPath}: unexpected end while reading updates (frame {globalFrame}, update {u}/{updateCount})";
                                    return false;
                                }

                                if (reserved != 0)
                                {
                                    DestroyTexture2DArray(ref labelsArray);
                                    error = $"delta-v1 invalid reserved field: {deltaPath}: reserved={reserved} (frame {globalFrame}, update {u})";
                                    return false;
                                }

                                if (splatId >= (uint)m_meta.splatCount)
                                {
                                    DestroyTexture2DArray(ref labelsArray);
                                    error = $"delta-v1 invalid splatId: {deltaPath}: splatId={splatId} >= splatCount={m_meta.splatCount} (frame {globalFrame}, update {u})";
                                    return false;
                                }

                                if (label >= (ushort)m_meta.streams.sh.shNCount)
                                {
                                    DestroyTexture2DArray(ref labelsArray);
                                    error = $"delta-v1 invalid label: {deltaPath}: label={label} >= shNCount={m_meta.streams.sh.shNCount} (frame {globalFrame}, update {u})";
                                    return false;
                                }

                                if (hasPrev && splatId <= prevSplatId)
                                {
                                    DestroyTexture2DArray(ref labelsArray);
                                    error =
                                        $"delta-v1 invalid splatId order: {deltaPath}: splatId must be strictly increasing within a block (frame {globalFrame}, update {u})";
                                    return false;
                                }

                                prevSplatId = splatId;
                                hasPrev = true;

                                var pixelIndex = (int)splatId;
                                var c = current[pixelIndex];
                                c.r = (byte)(label & 0xff);
                                c.g = (byte)((label >> 8) & 0xff);
                                current[pixelIndex] = c;
                            }

                            if (globalFrame >= chunkStartFrame)
                            {
                                labelsArray.SetPixels32(current, globalFrame - chunkStartFrame, 0);
                            }
                        }
                    }
                }

                labelsArray.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                return true;
            }
            catch (Exception e)
            {
                DestroyTexture2DArray(ref labelsArray);
                error = $"failed to expand shN delta-v1 chunk: {e.Message}";
                return false;
            }
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------
        static Dictionary<string, ZipArchiveEntry> BuildEntryMap(ZipArchive archive)
        {
            var map = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                var name = NormalizeZipPath(entry.FullName);
                if (string.IsNullOrEmpty(name))
                    continue;
                if (!map.ContainsKey(name))
                    map.Add(name, entry);
            }

            return map;
        }

        static string NormalizeZipPath(string path)
        {
            if (path == null)
                return string.Empty;

            var p = path.Replace('\\', '/').Trim();
            while (p.StartsWith("./", StringComparison.Ordinal))
                p = p.Substring(2);
            while (p.StartsWith("/", StringComparison.Ordinal))
                p = p.Substring(1);
            p = p.TrimEnd('/');
            return p;
        }

        static bool TryReadZipEntryUtf8Text(ZipArchiveEntry entry, out string text, out string error)
        {
            var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            try
            {
                using var s = entry.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                text = utf8Strict.GetString(ms.ToArray());
                error = null;
                return true;
            }
            catch (Exception e)
            {
                text = null;
                error = e.Message;
                return false;
            }
        }

        static bool TryReadZipEntryBytes(ZipArchiveEntry entry, out byte[] bytes, out string error)
        {
            try
            {
                using var s = entry.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                bytes = ms.ToArray();
                error = null;
                return true;
            }
            catch (Exception e)
            {
                bytes = null;
                error = e.Message;
                return false;
            }
        }

        static bool TryDecodeWebpToRgba32Texture(ZipArchiveEntry entry, string debugName, out Texture2D texture,
            out string error)
        {
            texture = null;
            error = null;

            try
            {
                using var s = entry.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                var bytes = ms.ToArray();

                texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true)
                {
                    name = debugName,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0
                };

                if (!ImageConversion.LoadImage(texture, bytes, markNonReadable: false))
                {
                    UnityEngine.Object.Destroy(texture);
                    texture = null;
                    error = "Unity ImageConversion.LoadImage returned false (可能是当前 Unity 版本不支持 WebP 解码)";
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
                texture = null;
                error = e.Message;
                return false;
            }
        }

        static bool TryValidateDeltaV1Header(BinaryReader br, int segIndex, ShNDeltaSegmentJson seg, Sog4DMetaJson meta,
            out string error)
        {
            error = null;

            byte[] magic;
            try
            {
                magic = br.ReadBytes(8);
            }
            catch (EndOfStreamException)
            {
                error = $"delta-v1 truncated: missing magic in segment {segIndex}";
                return false;
            }

            if (magic.Length != 8 ||
                magic[0] != (byte)'S' || magic[1] != (byte)'O' || magic[2] != (byte)'G' || magic[3] != (byte)'4' ||
                magic[4] != (byte)'D' || magic[5] != (byte)'L' || magic[6] != (byte)'B' || magic[7] != (byte)'1')
            {
                error = $"delta-v1 invalid magic in segment {segIndex}";
                return false;
            }

            try
            {
                var version = br.ReadUInt32();
                if (version != 1)
                {
                    error = $"delta-v1 invalid version: expected 1, got {version} (segment {segIndex})";
                    return false;
                }

                var segmentStartFrame = br.ReadUInt32();
                var segmentFrameCount = br.ReadUInt32();
                var splatCount = br.ReadUInt32();
                var shNCount = br.ReadUInt32();

                if (segmentStartFrame != (uint)seg.startFrame)
                {
                    error =
                        $"delta-v1 header mismatch: segmentStartFrame expected {seg.startFrame}, got {segmentStartFrame} (segment {segIndex})";
                    return false;
                }

                if (segmentFrameCount != (uint)seg.frameCount)
                {
                    error =
                        $"delta-v1 header mismatch: segmentFrameCount expected {seg.frameCount}, got {segmentFrameCount} (segment {segIndex})";
                    return false;
                }

                if (splatCount != (uint)meta.splatCount)
                {
                    error = $"delta-v1 header mismatch: splatCount expected {meta.splatCount}, got {splatCount} (segment {segIndex})";
                    return false;
                }

                if (shNCount != (uint)meta.streams.sh.shNCount)
                {
                    error =
                        $"delta-v1 header mismatch: shNCount expected {meta.streams.sh.shNCount}, got {shNCount} (segment {segIndex})";
                    return false;
                }

                return true;
            }
            catch (EndOfStreamException)
            {
                error = $"delta-v1 truncated header in segment {segIndex}";
                return false;
            }
        }

        static bool TryBuildTimeMappingRuntime(Sog4DMetaJson meta, out GsplatSequenceTimeMapping runtime, out string error)
        {
            runtime = default;
            error = null;

            if (meta.timeMapping == null)
            {
                error = "meta.json missing required field: timeMapping";
                return false;
            }

            if (string.IsNullOrEmpty(meta.timeMapping.type))
            {
                error = "meta.json missing required field: timeMapping.type";
                return false;
            }

            if (meta.timeMapping.type == "uniform")
            {
                runtime.Type = GsplatSequenceTimeMapping.MappingType.Uniform;
                runtime.FrameTimesNormalized = null;
                return true;
            }

            if (meta.timeMapping.type != "explicit")
            {
                error = $"meta.json invalid timeMapping.type: {meta.timeMapping.type}";
                return false;
            }

            var times = meta.timeMapping.frameTimesNormalized;
            if (times == null || times.Length != meta.frameCount)
            {
                error = $"meta.json invalid timeMapping.frameTimesNormalized length: expected {meta.frameCount}, got {(times == null ? 0 : times.Length)}";
                return false;
            }

            runtime.Type = GsplatSequenceTimeMapping.MappingType.Explicit;
            runtime.FrameTimesNormalized = times;
            return true;
        }

        static bool TryBuildBoundsFromPositionRanges(Vector3[] rangeMin, Vector3[] rangeMax, out Bounds unionBounds,
            out Bounds[] perFrameBounds, out string error)
        {
            unionBounds = default;
            perFrameBounds = null;
            error = null;

            if (rangeMin == null || rangeMax == null || rangeMin.Length == 0 || rangeMax.Length == 0)
            {
                error = "position range arrays are missing or empty.";
                return false;
            }

            if (rangeMin.Length != rangeMax.Length)
            {
                error = $"position range length mismatch: rangeMin={rangeMin.Length}, rangeMax={rangeMax.Length}";
                return false;
            }

            perFrameBounds = new Bounds[rangeMin.Length];
            for (var frame = 0; frame < rangeMin.Length; frame++)
            {
                var min = rangeMin[frame];
                var max = rangeMax[frame];
                var size = max - min;
                if (size.x < 0 || size.y < 0 || size.z < 0)
                {
                    error = $"position range invalid: rangeMax < rangeMin at frame {frame}";
                    return false;
                }

                var center = (min + max) * 0.5f;
                perFrameBounds[frame] = new Bounds(center, size);

                if (frame == 0)
                    unionBounds = perFrameBounds[frame];
                else
                    unionBounds.Encapsulate(perFrameBounds[frame]);
            }

            return true;
        }

        static bool TryValidateU16RgIndexMap(Color32[] pixels, int splatCount, int maxExclusive, out int badSplatId,
            out ushort badValue)
        {
            badSplatId = -1;
            badValue = 0;

            if (pixels == null || pixels.Length == 0)
                return true;

            var limit = Mathf.Min(splatCount, pixels.Length);
            for (var i = 0; i < limit; i++)
            {
                var c = pixels[i];
                var v = (ushort)(c.r | (c.g << 8));
                if (v >= maxExclusive)
                {
                    badSplatId = i;
                    badValue = v;
                    return false;
                }
            }

            return true;
        }

        static void DestroyTextureIfAny(ref Texture2DArray texture)
        {
            if (texture == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);

            texture = null;
        }

        static void DestroyTexture2DArray(ref Texture2DArray texture)
        {
            if (texture == null)
                return;

            UnityEngine.Object.Destroy(texture);
            texture = null;
        }

        static void DestroySequenceAssetAndTextures(GsplatSequenceAsset asset)
        {
            if (asset == null)
                return;

            DestroyTextureIfAny(ref asset.PositionHi);
            DestroyTextureIfAny(ref asset.PositionLo);
            DestroyTextureIfAny(ref asset.ScaleIndices);
            DestroyTextureIfAny(ref asset.Rotation);
            DestroyTextureIfAny(ref asset.Sh0);
            DestroyTextureIfAny(ref asset.ShNLabels);

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(asset);
            else
                UnityEngine.Object.DestroyImmediate(asset);
        }
    }
}
