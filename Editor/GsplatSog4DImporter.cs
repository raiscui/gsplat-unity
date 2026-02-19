// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gsplat.Editor
{
    // `.sog4d` 是面向“逐帧 keyframe + 全属性可插值”的序列 4DGS 单文件格式.
    // - 容器: ZIP bundle,根目录必须包含 meta.json(见 OpenSpec: sog4d-container).
    // - 编码: per-frame 属性图(WebP 数据图) + meta.json 描述(见 OpenSpec: sog4d-sequence-encoding).
    //
    // 本 importer 的阶段性目标(任务 3.1-3.3):
    // 1) 先把 ZIP + meta.json + streams 的完整性校验做扎实,提供 actionable errors.
    // 2) 通过 JsonUtility 解析 meta,并天然忽略未知字段(满足 forward-compat).
    // 3) 校验通过后,创建一个 `GsplatSequenceAsset`(先填元数据),为后续解码与播放铺路.
    [ScriptedImporter(1, "sog4d")]
    public sealed class GsplatSog4DImporter : ScriptedImporter
    {
        const int k_supportedVersion = 1;

        // --------------------------------------------------------------------
        // Json DTO(只用于 meta.json 解析)
        // - 字段名必须与 JSON 完全一致,因此这里使用 lowerCamelCase.
        // - 使用 JsonUtility 的好处是: 未知字段会被忽略,符合 spec 的 forward compatibility 要求.
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

            // bands>0 时必需
            public int shNCount;
            public string shNCentroidsType; // "f16" | "f32"
            public string shNCentroidsPath; // e.g. "shN_centroids.bin"

            // 可选,缺失时视为 "full"
            public string shNLabelsEncoding; // "full" | "delta-v1"

            // full 模式
            public string shNLabelsPath; // e.g. "frames/{frame}/shN_labels.webp"

            // delta-v1 模式
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

        public override void OnImportAsset(AssetImportContext ctx)
        {
            // 防御: 保持与其它 importer 一致,失败时只输出 error 并提前 return.
            // 注意: 不要生成半成品资产,避免后续出现“引用了但运行时崩”的黑盒问题.
            if (!TryImport(ctx, out var sequenceAsset))
                return;

            ctx.AddObjectToAsset("sequenceAsset", sequenceAsset);

            // 量化 streams(导入期解码后的子资产).为空代表该能力尚未生成或该数据在当前配置下不可用.
            AddTexture2DArrayIfNotNull(ctx, "positionHi", sequenceAsset.PositionHi);
            AddTexture2DArrayIfNotNull(ctx, "positionLo", sequenceAsset.PositionLo);
            AddTexture2DArrayIfNotNull(ctx, "scaleIndices", sequenceAsset.ScaleIndices);
            AddTexture2DArrayIfNotNull(ctx, "rotation", sequenceAsset.Rotation);
            AddTexture2DArrayIfNotNull(ctx, "sh0", sequenceAsset.Sh0);
            AddTexture2DArrayIfNotNull(ctx, "shNLabels", sequenceAsset.ShNLabels);

            // ----------------------------------------------------------------
            // task 3.7: 一键可用
            // - 导入后自动生成 prefab,挂上 GsplatSequenceRenderer.
            // - prefab 作为 main object,SequenceAsset 与纹理 streams 作为 sub-assets.
            // ----------------------------------------------------------------
            var prefab = new GameObject(sequenceAsset.name);
            var renderer = prefab.AddComponent<GsplatSequenceRenderer>();
            renderer.SequenceAsset = sequenceAsset;

            // 默认回填解码 compute shader,减少手工配置成本.
            var decodeCsPath = GsplatUtils.k_PackagePath + "Runtime/Shaders/GsplatSequenceDecode.compute";
            var decodeCs = AssetDatabase.LoadAssetAtPath<ComputeShader>(decodeCsPath);
            if (decodeCs != null)
                renderer.DecodeComputeShader = decodeCs;
            else
                Debug.LogWarning($"[Gsplat][SOG4D] 未找到默认解码 compute shader: {decodeCsPath}. 你仍可手动在组件上指定.");

            ctx.AddObjectToAsset("prefab", prefab);
            ctx.SetMainObject(prefab);
        }

        bool TryImport(AssetImportContext ctx, out GsplatSequenceAsset sequenceAsset)
        {
            sequenceAsset = null;

            try
            {
                using var fs = new FileStream(ctx.assetPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);

                // 预先建立 entry 索引,避免对每个文件都做线性查找.
                var entriesByName = BuildEntryMap(archive);

                // 1) meta.json 必须存在于根目录.
                if (!entriesByName.TryGetValue("meta.json", out var metaEntry))
                    return Fail(ctx, "bundle missing required file: meta.json");

                // 2) 读取并解析 meta.json(UTF-8 JSON).
                if (!TryReadZipEntryUtf8Text(metaEntry, out var metaJsonText, out var metaReadError))
                    return Fail(ctx, $"failed to read meta.json: {metaReadError}");

                Sog4DMetaJson meta;
                try
                {
                    meta = JsonUtility.FromJson<Sog4DMetaJson>(metaJsonText);
                }
                catch (Exception e)
                {
                    return Fail(ctx, $"meta.json parse error: {e.Message}");
                }

                if (meta == null)
                    return Fail(ctx, "meta.json parse error: JsonUtility returned null");

                // 3) 顶层字段校验(见 OpenSpec: sog4d-container).
                if (!string.Equals(meta.format, "sog4d", StringComparison.Ordinal))
                    return Fail(ctx, $"meta.json invalid format: expected \"sog4d\", got \"{meta.format}\"");

                if (meta.version != k_supportedVersion)
                {
                    return Fail(ctx,
                        $"meta.json unsupported version: expected {k_supportedVersion}, got {meta.version}");
                }

                if (meta.splatCount <= 0)
                    return Fail(ctx, $"meta.json invalid splatCount: {meta.splatCount}");

                if (meta.frameCount <= 0)
                    return Fail(ctx, $"meta.json invalid frameCount: {meta.frameCount}");

                // 4) timeMapping/layout/streams 的结构与约束校验(见 OpenSpec: sog4d-sequence-encoding).
                if (!TryValidateTimeMapping(ctx, meta, out var timeMappingRuntime))
                    return false;

                if (!TryValidateLayout(ctx, meta, out var layoutRuntime))
                    return false;

                if (!TryValidateStreams(ctx, meta, entriesByName))
                    return false;

                // 5) 到这里,我们可以安全创建资产对象(仅填 meta 与后续解码所需的静态数据).
                var assetName = Path.GetFileNameWithoutExtension(ctx.assetPath);
                var asset = ScriptableObject.CreateInstance<GsplatSequenceAsset>();
                asset.name = assetName;

                asset.SplatCount = (uint)meta.splatCount;
                asset.FrameCount = meta.frameCount;
                asset.TimeMapping = timeMappingRuntime;
                asset.Layout = layoutRuntime;

                // streams meta(导入期真正解码 WebP/centroids 的任务会在后续步骤实现).
                asset.PositionRangeMin = meta.streams.position.rangeMin;
                asset.PositionRangeMax = meta.streams.position.rangeMax;
                asset.ScaleCodebook = meta.streams.scale.codebook;

                asset.SHBands = (byte)Mathf.Clamp(meta.streams.sh.bands, 0, 3);
                asset.Sh0Codebook = meta.streams.sh.sh0Codebook;

                if (asset.SHBands > 0)
                {
                    asset.ShNCount = meta.streams.sh.shNCount;
                    asset.ShNCentroidsType = meta.streams.sh.shNCentroidsType;

                    // `shN_centroids.bin`:
                    // - 这是 SH rest 的 palette,运行时解码必须依赖它.
                    // - 这里直接把原始 bytes 存入 ScriptableObject,避免在工程里额外落地一个文件.
                    var centroidsPath = NormalizeZipPath(meta.streams.sh.shNCentroidsPath);
                    if (!entriesByName.TryGetValue(centroidsPath, out var centroidsEntry))
                    {
                        DestroySequenceAssetAndSubObjects(asset);
                        return Fail(ctx, $"bundle missing referenced file: {centroidsPath}");
                    }

                    if (!TryReadZipEntryBytes(centroidsEntry, out var centroidsBytes, out var centroidsReadErr))
                    {
                        DestroySequenceAssetAndSubObjects(asset);
                        return Fail(ctx, $"failed to read shN_centroids.bin: {centroidsPath}: {centroidsReadErr}");
                    }

                    // 基本尺寸校验,避免运行时才发现 palette 数据被截断或格式不一致.
                    var restCoeffCount = (asset.SHBands + 1) * (asset.SHBands + 1) - 1;
                    var scalarBytes = asset.ShNCentroidsType == "f16" ? 2 : 4;
                    var expectedBytes = (long)asset.ShNCount * restCoeffCount * 3L * scalarBytes;
                    if (centroidsBytes.LongLength != expectedBytes)
                    {
                        DestroySequenceAssetAndSubObjects(asset);
                        return Fail(ctx,
                            $"shN_centroids.bin size mismatch: expected {expectedBytes} bytes, got {centroidsBytes.LongLength} bytes. " +
                            $"(shNCount={asset.ShNCount}, restCoeffCount={restCoeffCount}, type={asset.ShNCentroidsType})");
                    }

                    asset.ShNCentroidsBytes = centroidsBytes;
                }

                // --------------------------------------------------------------------
                // Bounds(任务 3.6)
                // - 直接用 per-frame 的 position rangeMin/rangeMax 生成 PerFrameBounds 与 UnionBounds.
                // - 这样可以避免解码并遍历全量 position 数据,导入更快,峰值内存更低.
                // --------------------------------------------------------------------
                if (!TryBuildBoundsFromPositionRanges(ctx, meta, out var unionBounds, out var perFrameBounds))
                {
                    DestroySequenceAssetAndSubObjects(asset);
                    return false;
                }
                asset.UnionBounds = unionBounds;
                asset.PerFrameBounds = perFrameBounds;

                // --------------------------------------------------------------------
                // WebP 解码(任务 3.4) + per-frame streams 打包(任务 3.5)
                // - 这些 WebP 图像是“数据图”,必须禁用 sRGB/压缩/mipmap.
                // - 当前优先使用 Unity 内置 `ImageConversion.LoadImage` 作为 WebP 解码器.
                //   若宿主 Unity 版本不支持 WebP,会明确报错并 fail-fast.
                // --------------------------------------------------------------------
                var width = meta.layout.width;
                var height = meta.layout.height;
                var frameCount = meta.frameCount;

                if (!TryBuildTexture2DArrayFromPerFrameWebp(ctx, entriesByName, meta.streams.position.hiPath,
                        width, height, frameCount, "PositionHi", out asset.PositionHi))
                {
                    DestroySequenceAssetAndSubObjects(asset);
                    return false;
                }
                if (!TryBuildTexture2DArrayFromPerFrameWebp(ctx, entriesByName, meta.streams.position.loPath,
                        width, height, frameCount, "PositionLo", out asset.PositionLo))
                {
                    DestroySequenceAssetAndSubObjects(asset);
                    return false;
                }
                if (!TryBuildTexture2DArrayFromPerFrameWebp(ctx, entriesByName, meta.streams.scale.indicesPath,
                        width, height, frameCount, "ScaleIndices", out asset.ScaleIndices,
                        u16MaxExclusive: meta.streams.scale.codebook.Length, splatCount: meta.splatCount))
                {
                    DestroySequenceAssetAndSubObjects(asset);
                    return false;
                }
                if (!TryBuildTexture2DArrayFromPerFrameWebp(ctx, entriesByName, meta.streams.rotation.path,
                        width, height, frameCount, "Rotation", out asset.Rotation))
                {
                    DestroySequenceAssetAndSubObjects(asset);
                    return false;
                }
                if (!TryBuildTexture2DArrayFromPerFrameWebp(ctx, entriesByName, meta.streams.sh.sh0Path,
                        width, height, frameCount, "Sh0", out asset.Sh0))
                {
                    DestroySequenceAssetAndSubObjects(asset);
                    return false;
                }

                // bands>0 时,SH rest labels 是必需输入.
                if (asset.SHBands > 0)
                {
                    var labelsEncoding = meta.streams.sh.shNLabelsEncoding;
                    if (string.IsNullOrEmpty(labelsEncoding))
                        labelsEncoding = "full";

                    if (labelsEncoding == "full")
                    {
                        if (!TryBuildTexture2DArrayFromPerFrameWebp(ctx, entriesByName, meta.streams.sh.shNLabelsPath,
                                width, height, frameCount, "ShNLabels", out asset.ShNLabels,
                                u16MaxExclusive: meta.streams.sh.shNCount, splatCount: meta.splatCount))
                        {
                            DestroySequenceAssetAndSubObjects(asset);
                            return false;
                        }
                    }
                    else if (labelsEncoding == "delta-v1")
                    {
                        if (!TryBuildShNLabelsFromDeltaV1(ctx, meta, entriesByName, width, height, out asset.ShNLabels))
                        {
                            DestroySequenceAssetAndSubObjects(asset);
                            return false;
                        }
                    }
                    else
                    {
                        DestroySequenceAssetAndSubObjects(asset);
                        return Fail(ctx, $"meta.json invalid streams.sh.shNLabelsEncoding: {labelsEncoding}");
                    }
                }

                sequenceAsset = asset;
                return true;
            }
            catch (InvalidDataException e)
            {
                // 典型: 输入不是 ZIP 或 ZIP central directory 损坏.
                return Fail(ctx, $"invalid ZIP bundle: {e.Message}");
            }
            catch (Exception e)
            {
                return Fail(ctx, $"unexpected importer exception: {e.Message}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static void AddTexture2DArrayIfNotNull(AssetImportContext ctx, string name, Texture2DArray texture)
        {
            if (texture == null)
                return;

            texture.name = name;
            ctx.AddObjectToAsset(name, texture);
        }

        static bool TryBuildBoundsFromPositionRanges(AssetImportContext ctx, Sog4DMetaJson meta, out Bounds unionBounds,
            out Bounds[] perFrameBounds)
        {
            unionBounds = default;
            perFrameBounds = null;

            var mins = meta.streams.position.rangeMin;
            var maxs = meta.streams.position.rangeMax;
            if (mins == null || maxs == null || mins.Length != meta.frameCount || maxs.Length != meta.frameCount)
                return Fail(ctx, "cannot build bounds: invalid streams.position.rangeMin/rangeMax");

            perFrameBounds = new Bounds[meta.frameCount];
            for (var frame = 0; frame < meta.frameCount; frame++)
            {
                var min = mins[frame];
                var max = maxs[frame];

                if (float.IsNaN(min.x) || float.IsNaN(min.y) || float.IsNaN(min.z) ||
                    float.IsNaN(max.x) || float.IsNaN(max.y) || float.IsNaN(max.z))
                {
                    return Fail(ctx, $"cannot build bounds: NaN in position ranges at frame {frame}");
                }

                if (min.x > max.x || min.y > max.y || min.z > max.z)
                {
                    return Fail(ctx,
                        $"cannot build bounds: rangeMin must be <= rangeMax at frame {frame}, got min={min}, max={max}");
                }

                var size = max - min;
                var center = (min + max) * 0.5f;
                perFrameBounds[frame] = new Bounds(center, size);

                if (frame == 0)
                    unionBounds = perFrameBounds[frame];
                else
                    unionBounds.Encapsulate(perFrameBounds[frame]);
            }

            return true;
        }

        static bool TryBuildTexture2DArrayFromPerFrameWebp(AssetImportContext ctx,
            Dictionary<string, ZipArchiveEntry> entriesByName, string template, int width, int height, int frameCount,
            string debugName, out Texture2DArray array, int u16MaxExclusive = -1, int splatCount = -1)
        {
            array = null;

            try
            {
                array = new Texture2DArray(width, height, frameCount, TextureFormat.RGBA32, mipChain: false,
                    linear: true)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0
                };

                for (var frame = 0; frame < frameCount; frame++)
                {
                    if ((frame & 0x7) == 0)
                    {
                        EditorUtility.DisplayProgressBar("Importing .sog4d",
                            $"Decoding {debugName} (frame {frame}/{frameCount})", frame / (float)frameCount);
                    }

                    var path = NormalizeZipPath(template.Replace("{frame}", frame.ToString("D5")));
                    if (!entriesByName.TryGetValue(path, out var entry))
                        return FailAndDestroyTexture2DArray(ctx, ref array, $"bundle missing referenced file: {path}");

                    if (!TryDecodeWebpToRgba32Texture(entry, debugName, out var decoded, out var decodeErr))
                        return FailAndDestroyTexture2DArray(ctx, ref array, $"failed to decode WebP: {path}: {decodeErr}");

                    try
                    {
                        if (decoded.width != width || decoded.height != height)
                        {
                            return FailAndDestroyTexture2DArray(ctx, ref array,
                                $"WebP size mismatch: {path}: expected {width}x{height}, got {decoded.width}x{decoded.height}");
                        }

                        // 这里使用 SetPixels32 的路径,保证写入的是 CPU-side 像素数据,
                        // 从而让子资产在 Unity 重启/重导入后内容仍然一致.
                        //
                        // 额外校验:
                        // - 对于 u16 index map(labels/indices),spec 要求出现越界值时必须导入失败.
                        // - 这里在导入期做 fail-fast,避免运行时才出现黑盒行为.
                        var pixels = decoded.GetPixels32();
                        if (u16MaxExclusive > 0 && splatCount > 0)
                        {
                            if (!TryValidateU16RgIndexMap(pixels, splatCount, u16MaxExclusive, out var badSplatId,
                                    out var badValue))
                            {
                                return FailAndDestroyTexture2DArray(ctx, ref array,
                                    $"{debugName} u16 index out of range: frame={frame}, splatId={badSplatId}, value={badValue}, maxExclusive={u16MaxExclusive}. path={path}");
                            }
                        }

                        array.SetPixels32(pixels, frame, 0);
                    }
                    finally
                    {
                        Object.DestroyImmediate(decoded);
                    }
                }

                array.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                return true;
            }
            catch (Exception e)
            {
                if (array != null)
                    Object.DestroyImmediate(array);
                array = null;
                return Fail(ctx, $"failed to build Texture2DArray for {debugName}: {e.Message}");
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

	                // 关键: 这些是数据图.
	                // - mipmap 必须关闭,避免字节重采样.
	                // - linear=true,避免采样时发生 sRGB->linear 变换.
	                // - RGBA32,避免格式转换引入不可控的误差.
	                //
	                // 方案:
	                // 1) 优先使用 Unity 内置 `ImageConversion.LoadImage`.
	                // 2) 若宿主 Unity 不支持 WebP(LoadImage 返回 false),则 fallback 到包内自带的 libwebp decoder.
	                texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true)
	                {
	                    name = debugName,
	                    filterMode = FilterMode.Point,
	                    wrapMode = TextureWrapMode.Clamp,
	                    anisoLevel = 0
	                };

	                if (ImageConversion.LoadImage(texture, bytes, markNonReadable: false))
	                    return true;

	                // Unity 版本差异点:
	                // - 如果当前 Unity 不支持 WebP,LoadImage 会返回 false.
	                // - 这里尝试 fallback,避免“同一份 `.sog4d` 在不同 Unity 版本下不可导入”.
	                Object.DestroyImmediate(texture);
	                texture = null;

	                if (!GsplatWebpNative.TryDecodeRgba32(bytes, out var width, out var height, out var rgba,
	                        out var nativeErr))
	                {
	                    error =
	                        $"Unity ImageConversion.LoadImage returned false (可能是当前 Unity 版本不支持 WebP 解码). native fallback failed: {nativeErr}";
	                    return false;
	                }

	                texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: true)
	                {
	                    name = debugName,
	                    filterMode = FilterMode.Point,
	                    wrapMode = TextureWrapMode.Clamp,
	                    anisoLevel = 0
	                };
	                texture.LoadRawTextureData(rgba);
	                texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
	                return true;
	            }
	            catch (Exception e)
	            {
	                if (texture != null)
	                    Object.DestroyImmediate(texture);
                texture = null;
                error = e.Message;
                return false;
            }
        }

        static bool TryBuildShNLabelsFromDeltaV1(AssetImportContext ctx, Sog4DMetaJson meta,
            Dictionary<string, ZipArchiveEntry> entriesByName, int width, int height, out Texture2DArray labelsArray)
        {
            labelsArray = null;

            var segments = meta.streams.sh.shNDeltaSegments;
            if (segments == null || segments.Length == 0)
                return Fail(ctx, "meta.json missing required field: streams.sh.shNDeltaSegments");

            try
            {
                labelsArray = new Texture2DArray(width, height, meta.frameCount, TextureFormat.RGBA32, mipChain: false,
                    linear: true)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0
                };

                for (var segIndex = 0; segIndex < segments.Length; segIndex++)
                {
                    var seg = segments[segIndex];

                    // 1) base labels
                    var basePath = NormalizeZipPath(seg.baseLabelsPath);
                    if (!entriesByName.TryGetValue(basePath, out var baseEntry))
                        return FailAndDestroyTexture2DArray(ctx, ref labelsArray, $"bundle missing referenced file: {basePath}");

                    if (!TryDecodeWebpToRgba32Texture(baseEntry, "ShNBaseLabels", out var baseTex, out var baseErr))
                        return FailAndDestroyTexture2DArray(ctx, ref labelsArray, $"failed to decode WebP: {basePath}: {baseErr}");

                    Color32[] current;
                    try
                    {
                        if (baseTex.width != width || baseTex.height != height)
                        {
                            return FailAndDestroyTexture2DArray(ctx, ref labelsArray,
                                $"WebP size mismatch: {basePath}: expected {width}x{height}, got {baseTex.width}x{baseTex.height}");
                        }

                        current = baseTex.GetPixels32();
                    }
                    finally
                    {
                        Object.DestroyImmediate(baseTex);
                    }

                    if (current.Length != width * height)
                        return FailAndDestroyTexture2DArray(ctx, ref labelsArray, $"base labels pixel count mismatch: {basePath}");

                    // spec 要求 base labels 中出现任意越界值时必须失败.
                    if (!TryValidateU16RgIndexMap(current, meta.splatCount, meta.streams.sh.shNCount, out var badSplatId,
                            out var badValue))
                    {
                        return FailAndDestroyTexture2DArray(ctx, ref labelsArray,
                            $"shN base labels out of range: segment={segIndex}, frame={seg.startFrame}, splatId={badSplatId}, label={badValue} >= shNCount={meta.streams.sh.shNCount}. path={basePath}");
                    }

                    labelsArray.SetPixels32(current, seg.startFrame, 0);

                    // 2) delta file
                    var deltaPath = NormalizeZipPath(seg.deltaPath);
                    if (!entriesByName.TryGetValue(deltaPath, out var deltaEntry))
                        return FailAndDestroyTexture2DArray(ctx, ref labelsArray, $"bundle missing referenced file: {deltaPath}");

                    using (var s = deltaEntry.Open())
                    using (var br = new BinaryReader(s))
                    {
                        if (!TryValidateDeltaV1Header(ctx, br, segIndex, seg, meta))
                        {
                            if (labelsArray != null)
                                Object.DestroyImmediate(labelsArray);
                            labelsArray = null;
                            return false;
                        }

                        // 对 segment 内后续帧逐个应用 delta.
                        for (var localFrame = 1; localFrame < seg.frameCount; localFrame++)
                        {
                            var globalFrame = seg.startFrame + localFrame;

                            if ((globalFrame & 0x7) == 0)
                            {
                                EditorUtility.DisplayProgressBar("Importing .sog4d",
                                    $"Expanding shN delta-v1 (frame {globalFrame}/{meta.frameCount})",
                                    globalFrame / (float)meta.frameCount);
                            }

                            uint updateCount;
                            try
                            {
                                updateCount = br.ReadUInt32();
                            }
                            catch (EndOfStreamException)
                            {
                                return FailAndDestroyTexture2DArray(ctx, ref labelsArray,
                                    $"delta-v1 truncated: {deltaPath}: missing updateCount for frame {globalFrame}");
                            }

                            if (updateCount > (uint)meta.splatCount)
                            {
                                return FailAndDestroyTexture2DArray(ctx, ref labelsArray,
                                    $"delta-v1 invalid updateCount: {deltaPath}: updateCount={updateCount} > splatCount={meta.splatCount} at frame {globalFrame}");
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
                                    return FailAndDestroyTexture2DArray(ctx, ref labelsArray,
                                        $"delta-v1 truncated: {deltaPath}: unexpected end while reading updates (frame {globalFrame}, update {u}/{updateCount})");
                                }

                                if (reserved != 0)
                                {
                                    return FailAndDestroyTexture2DArray(ctx, ref labelsArray,
                                        $"delta-v1 invalid reserved field: {deltaPath}: reserved={reserved} (frame {globalFrame}, update {u})");
                                }

                                if (splatId >= (uint)meta.splatCount)
                                {
                                    return FailAndDestroyTexture2DArray(ctx, ref labelsArray,
                                        $"delta-v1 invalid splatId: {deltaPath}: splatId={splatId} >= splatCount={meta.splatCount} (frame {globalFrame}, update {u})");
                                }

                                if (label >= (ushort)meta.streams.sh.shNCount)
                                {
                                    return FailAndDestroyTexture2DArray(ctx, ref labelsArray,
                                        $"delta-v1 invalid label: {deltaPath}: label={label} >= shNCount={meta.streams.sh.shNCount} (frame {globalFrame}, update {u})");
                                }

                                if (hasPrev && splatId <= prevSplatId)
                                {
                                    return FailAndDestroyTexture2DArray(ctx, ref labelsArray,
                                        $"delta-v1 invalid splatId order: {deltaPath}: splatId must be strictly increasing within a block (frame {globalFrame}, update {u})");
                                }

                                prevSplatId = splatId;
                                hasPrev = true;

                                // row-major: pixelIndex == splatId,与 specs 的映射一致.
                                var pixelIndex = (int)splatId;
                                var c = current[pixelIndex];
                                c.r = (byte)(label & 0xff);
                                c.g = (byte)((label >> 8) & 0xff);
                                current[pixelIndex] = c;
                            }

                            labelsArray.SetPixels32(current, globalFrame, 0);
                        }
                    }
                }

                labelsArray.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                return true;
            }
            catch (Exception e)
            {
                if (labelsArray != null)
                    Object.DestroyImmediate(labelsArray);
                labelsArray = null;
                return Fail(ctx, $"failed to expand shN delta-v1: {e.Message}");
            }
        }

        static bool FailAndDestroyTexture2DArray(AssetImportContext ctx, ref Texture2DArray array, string message)
        {
            if (array != null)
                Object.DestroyImmediate(array);
            array = null;
            return Fail(ctx, message);
        }

        static void DestroySequenceAssetAndSubObjects(GsplatSequenceAsset asset)
        {
            if (asset == null)
                return;

            DestroyTexture2DArray(ref asset.PositionHi);
            DestroyTexture2DArray(ref asset.PositionLo);
            DestroyTexture2DArray(ref asset.ScaleIndices);
            DestroyTexture2DArray(ref asset.Rotation);
            DestroyTexture2DArray(ref asset.Sh0);
            DestroyTexture2DArray(ref asset.ShNLabels);

            Object.DestroyImmediate(asset);
        }

        static void DestroyTexture2DArray(ref Texture2DArray texture)
        {
            if (texture == null)
                return;

            Object.DestroyImmediate(texture);
            texture = null;
        }

        static bool TryValidateU16RgIndexMap(Color32[] pixels, int splatCount, int maxExclusive, out int badSplatId,
            out ushort badValue)
        {
            // u16 的小端打包规则(与 spec 一致):
            // - value = r + (g << 8)
            //
            // 只校验 [0,splatCount) 的有效 splatId 范围.
            // padding 像素(>=splatCount)不参与语义,因此不强制校验.
            badSplatId = -1;
            badValue = 0;

            if (pixels == null || pixels.Length == 0)
                return true;

            if (splatCount <= 0 || maxExclusive <= 0)
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

        static bool TryValidateDeltaV1Header(AssetImportContext ctx, BinaryReader br, int segIndex,
            ShNDeltaSegmentJson seg, Sog4DMetaJson meta)
        {
            // Header:
            // magic: 8 bytes ASCII "SOG4DLB1"
            // version: u32 == 1
            // segmentStartFrame/segmentFrameCount/splatCount/shNCount: must match meta
            byte[] magic;
            try
            {
                magic = br.ReadBytes(8);
            }
            catch (EndOfStreamException)
            {
                return Fail(ctx, $"delta-v1 truncated: missing magic in segment {segIndex}");
            }

            if (magic.Length != 8 ||
                magic[0] != (byte)'S' || magic[1] != (byte)'O' || magic[2] != (byte)'G' || magic[3] != (byte)'4' ||
                magic[4] != (byte)'D' || magic[5] != (byte)'L' || magic[6] != (byte)'B' || magic[7] != (byte)'1')
            {
                return Fail(ctx, $"delta-v1 invalid magic in segment {segIndex}");
            }

            try
            {
                var version = br.ReadUInt32();
                if (version != 1)
                    return Fail(ctx, $"delta-v1 invalid version: expected 1, got {version} (segment {segIndex})");

                var segmentStartFrame = br.ReadUInt32();
                var segmentFrameCount = br.ReadUInt32();
                var splatCount = br.ReadUInt32();
                var shNCount = br.ReadUInt32();

                if (segmentStartFrame != (uint)seg.startFrame)
                {
                    return Fail(ctx,
                        $"delta-v1 header mismatch: segmentStartFrame expected {seg.startFrame}, got {segmentStartFrame} (segment {segIndex})");
                }

                if (segmentFrameCount != (uint)seg.frameCount)
                {
                    return Fail(ctx,
                        $"delta-v1 header mismatch: segmentFrameCount expected {seg.frameCount}, got {segmentFrameCount} (segment {segIndex})");
                }

                if (splatCount != (uint)meta.splatCount)
                {
                    return Fail(ctx,
                        $"delta-v1 header mismatch: splatCount expected {meta.splatCount}, got {splatCount} (segment {segIndex})");
                }

                if (shNCount != (uint)meta.streams.sh.shNCount)
                {
                    return Fail(ctx,
                        $"delta-v1 header mismatch: shNCount expected {meta.streams.sh.shNCount}, got {shNCount} (segment {segIndex})");
                }

                return true;
            }
            catch (EndOfStreamException)
            {
                return Fail(ctx, $"delta-v1 truncated header in segment {segIndex}");
            }
        }

        static Dictionary<string, ZipArchiveEntry> BuildEntryMap(ZipArchive archive)
        {
            var map = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                // ZipArchiveEntry.FullName 可能包含目录条目,这里统一规范化为相对路径.
                var name = NormalizeZipPath(entry.FullName);
                if (string.IsNullOrEmpty(name))
                    continue;

                // 若出现同名条目,保留第一个即可(重复条目属于异常 bundle,但不在此阶段强制 fail).
                if (!map.ContainsKey(name))
                    map.Add(name, entry);
            }

            return map;
        }

        static string NormalizeZipPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            var p = path.Replace('\\', '/');
            while (p.StartsWith("./", StringComparison.Ordinal))
                p = p.Substring(2);
            while (p.StartsWith("/", StringComparison.Ordinal))
                p = p.Substring(1);
            p = p.TrimEnd('/');
            return p;
        }

        static bool TryReadZipEntryUtf8Text(ZipArchiveEntry entry, out string text, out string error)
        {
            // 严格 UTF-8: 非 UTF-8 直接报错,避免 silent data corruption.
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

        static bool TryValidateTimeMapping(AssetImportContext ctx, Sog4DMetaJson meta,
            out GsplatSequenceTimeMapping runtime)
        {
            runtime = default;

            if (meta.timeMapping == null)
                return Fail(ctx, "meta.json missing required field: timeMapping");

            if (string.IsNullOrEmpty(meta.timeMapping.type))
                return Fail(ctx, "meta.json missing required field: timeMapping.type");

            if (meta.timeMapping.type == "uniform")
            {
                runtime.Type = GsplatSequenceTimeMapping.MappingType.Uniform;
                runtime.FrameTimesNormalized = null;
                return true;
            }

            if (meta.timeMapping.type != "explicit")
                return Fail(ctx, $"meta.json invalid timeMapping.type: {meta.timeMapping.type}");

            // explicit
            var times = meta.timeMapping.frameTimesNormalized;
            if (times == null)
                return Fail(ctx, "meta.json missing required field: timeMapping.frameTimesNormalized");

            if (times.Length != meta.frameCount)
            {
                return Fail(ctx,
                    $"meta.json invalid timeMapping.frameTimesNormalized length: expected {meta.frameCount}, got {times.Length}");
            }

            var prev = float.NegativeInfinity;
            for (var i = 0; i < times.Length; i++)
            {
                var t = times[i];
                if (t < 0.0f || t > 1.0f || float.IsNaN(t) || float.IsInfinity(t))
                    return Fail(ctx, $"meta.json invalid frameTimesNormalized[{i}]: {t} (must be in [0,1])");

                if (t < prev)
                {
                    return Fail(ctx,
                        $"meta.json invalid frameTimesNormalized: must be non-decreasing, but frame {i - 1}={prev} > frame {i}={t}");
                }

                prev = t;
            }

            runtime.Type = GsplatSequenceTimeMapping.MappingType.Explicit;
            runtime.FrameTimesNormalized = times;
            return true;
        }

        static bool TryValidateLayout(AssetImportContext ctx, Sog4DMetaJson meta, out GsplatSequenceLayout runtime)
        {
            runtime = default;

            if (meta.layout == null)
                return Fail(ctx, "meta.json missing required field: layout");

            if (string.IsNullOrEmpty(meta.layout.type))
                return Fail(ctx, "meta.json missing required field: layout.type");

            if (meta.layout.type != "row-major")
                return Fail(ctx, $"meta.json invalid layout.type: expected \"row-major\", got \"{meta.layout.type}\"");

            if (meta.layout.width <= 0 || meta.layout.height <= 0)
                return Fail(ctx, $"meta.json invalid layout size: width={meta.layout.width}, height={meta.layout.height}");

            var pixelCapacity = (long)meta.layout.width * meta.layout.height;
            if (pixelCapacity < meta.splatCount)
            {
                return Fail(ctx,
                    $"meta.json invalid layout size: width*height={pixelCapacity} < splatCount={meta.splatCount}");
            }

            runtime.Type = meta.layout.type;
            runtime.Width = meta.layout.width;
            runtime.Height = meta.layout.height;
            return true;
        }

        static bool TryValidateStreams(AssetImportContext ctx, Sog4DMetaJson meta,
            Dictionary<string, ZipArchiveEntry> entriesByName)
        {
            if (meta.streams == null)
                return Fail(ctx, "meta.json missing required field: streams");

            // -----------------------------
            // position
            // -----------------------------
            if (meta.streams.position == null)
                return Fail(ctx, "meta.json missing required stream: streams.position");

            if (meta.streams.position.rangeMin == null || meta.streams.position.rangeMax == null)
            {
                return Fail(ctx, "meta.json missing required fields: streams.position.rangeMin/rangeMax");
            }

            if (meta.streams.position.rangeMin.Length != meta.frameCount)
            {
                return Fail(ctx,
                    $"meta.json invalid streams.position.rangeMin length: expected {meta.frameCount}, got {meta.streams.position.rangeMin.Length}");
            }

            if (meta.streams.position.rangeMax.Length != meta.frameCount)
            {
                return Fail(ctx,
                    $"meta.json invalid streams.position.rangeMax length: expected {meta.frameCount}, got {meta.streams.position.rangeMax.Length}");
            }

            if (!TryValidatePerFrameTemplate(meta.streams.position.hiPath, "streams.position.hiPath", out var posHiErr))
                return Fail(ctx, posHiErr);
            if (!TryValidatePerFrameTemplate(meta.streams.position.loPath, "streams.position.loPath", out var posLoErr))
                return Fail(ctx, posLoErr);

            if (!TryValidatePerFrameFilesExist(entriesByName, meta.streams.position.hiPath, meta.frameCount,
                    out var missingPosHi))
            {
                return Fail(ctx, $"bundle missing referenced file: {missingPosHi} (from streams.position.hiPath)");
            }

            if (!TryValidatePerFrameFilesExist(entriesByName, meta.streams.position.loPath, meta.frameCount,
                    out var missingPosLo))
            {
                return Fail(ctx, $"bundle missing referenced file: {missingPosLo} (from streams.position.loPath)");
            }

            // -----------------------------
            // scale
            // -----------------------------
            if (meta.streams.scale == null)
                return Fail(ctx, "meta.json missing required stream: streams.scale");

            if (meta.streams.scale.codebook == null || meta.streams.scale.codebook.Length == 0)
                return Fail(ctx, "meta.json invalid streams.scale.codebook: must be non-empty");

            if (!TryValidatePerFrameTemplate(meta.streams.scale.indicesPath, "streams.scale.indicesPath", out var scaleIdxErr))
                return Fail(ctx, scaleIdxErr);

            if (!TryValidatePerFrameFilesExist(entriesByName, meta.streams.scale.indicesPath, meta.frameCount,
                    out var missingScaleIdx))
            {
                return Fail(ctx, $"bundle missing referenced file: {missingScaleIdx} (from streams.scale.indicesPath)");
            }

            // -----------------------------
            // rotation
            // -----------------------------
            if (meta.streams.rotation == null)
                return Fail(ctx, "meta.json missing required stream: streams.rotation");

            if (!TryValidatePerFrameTemplate(meta.streams.rotation.path, "streams.rotation.path", out var rotErr))
                return Fail(ctx, rotErr);

            if (!TryValidatePerFrameFilesExist(entriesByName, meta.streams.rotation.path, meta.frameCount,
                    out var missingRot))
            {
                return Fail(ctx, $"bundle missing referenced file: {missingRot} (from streams.rotation.path)");
            }

            // -----------------------------
            // sh
            // -----------------------------
            if (meta.streams.sh == null)
                return Fail(ctx, "meta.json missing required stream: streams.sh");

            if (meta.streams.sh.bands < 0 || meta.streams.sh.bands > 3)
                return Fail(ctx, $"meta.json invalid streams.sh.bands: {meta.streams.sh.bands} (must be 0..3)");

            if (!TryValidatePerFrameTemplate(meta.streams.sh.sh0Path, "streams.sh.sh0Path", out var sh0PathErr))
                return Fail(ctx, sh0PathErr);

            if (meta.streams.sh.sh0Codebook == null || meta.streams.sh.sh0Codebook.Length != 256)
            {
                var got = meta.streams.sh.sh0Codebook == null ? 0 : meta.streams.sh.sh0Codebook.Length;
                return Fail(ctx, $"meta.json invalid streams.sh.sh0Codebook length: expected 256, got {got}");
            }

            if (!TryValidatePerFrameFilesExist(entriesByName, meta.streams.sh.sh0Path, meta.frameCount, out var missingSh0))
                return Fail(ctx, $"bundle missing referenced file: {missingSh0} (from streams.sh.sh0Path)");

            if (meta.streams.sh.bands == 0)
                return true;

            // bands>0: validate shN*
            if (meta.streams.sh.shNCount <= 0 || meta.streams.sh.shNCount > 65535)
            {
                return Fail(ctx,
                    $"meta.json invalid streams.sh.shNCount: {meta.streams.sh.shNCount} (must be 1..65535)");
            }

            if (meta.streams.sh.shNCentroidsType != "f16" && meta.streams.sh.shNCentroidsType != "f32")
            {
                return Fail(ctx,
                    $"meta.json invalid streams.sh.shNCentroidsType: {meta.streams.sh.shNCentroidsType} (must be \"f16\" or \"f32\")");
            }

            if (!TryValidateRelativePath(meta.streams.sh.shNCentroidsPath, "streams.sh.shNCentroidsPath", out var centroidsPathErr))
                return Fail(ctx, centroidsPathErr);

            var centroidsPathNormalized = NormalizeZipPath(meta.streams.sh.shNCentroidsPath);
            if (!entriesByName.ContainsKey(centroidsPathNormalized))
                return Fail(ctx, $"bundle missing referenced file: {centroidsPathNormalized} (from streams.sh.shNCentroidsPath)");

            var labelsEncoding = meta.streams.sh.shNLabelsEncoding;
            if (string.IsNullOrEmpty(labelsEncoding))
                labelsEncoding = "full";

            if (labelsEncoding == "full")
            {
                if (!TryValidatePerFrameTemplate(meta.streams.sh.shNLabelsPath, "streams.sh.shNLabelsPath", out var labelsPathErr))
                    return Fail(ctx, labelsPathErr);

                if (!TryValidatePerFrameFilesExist(entriesByName, meta.streams.sh.shNLabelsPath, meta.frameCount,
                        out var missingLabels))
                {
                    return Fail(ctx, $"bundle missing referenced file: {missingLabels} (from streams.sh.shNLabelsPath)");
                }

                return true;
            }

            if (labelsEncoding != "delta-v1")
                return Fail(ctx, $"meta.json invalid streams.sh.shNLabelsEncoding: {labelsEncoding}");

            // delta-v1
            if (!string.IsNullOrEmpty(meta.streams.sh.shNLabelsPath))
            {
                return Fail(ctx,
                    "meta.json invalid streams.sh: shNLabelsPath MUST NOT be present when shNLabelsEncoding is \"delta-v1\"");
            }

            var segments = meta.streams.sh.shNDeltaSegments;
            if (segments == null || segments.Length == 0)
                return Fail(ctx, "meta.json missing required field: streams.sh.shNDeltaSegments");

            var expectedStart = 0;
            var totalFrames = 0;
            for (var i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                if (seg == null)
                    return Fail(ctx, $"meta.json invalid shNDeltaSegments[{i}]: null");

                if (seg.startFrame != expectedStart)
                {
                    return Fail(ctx,
                        $"meta.json invalid shNDeltaSegments[{i}].startFrame: expected {expectedStart}, got {seg.startFrame}");
                }

                if (seg.frameCount <= 0)
                    return Fail(ctx, $"meta.json invalid shNDeltaSegments[{i}].frameCount: {seg.frameCount}");

                if (seg.startFrame < 0 || seg.startFrame >= meta.frameCount)
                {
                    return Fail(ctx,
                        $"meta.json invalid shNDeltaSegments[{i}].startFrame: {seg.startFrame} (out of range)");
                }

                if (seg.startFrame + seg.frameCount > meta.frameCount)
                {
                    return Fail(ctx,
                        $"meta.json invalid shNDeltaSegments[{i}]: startFrame+frameCount exceeds frameCount ({seg.startFrame}+{seg.frameCount} > {meta.frameCount})");
                }

                if (!TryValidateRelativePath(seg.baseLabelsPath, $"streams.sh.shNDeltaSegments[{i}].baseLabelsPath", out var baseErr))
                    return Fail(ctx, baseErr);
                if (!TryValidateRelativePath(seg.deltaPath, $"streams.sh.shNDeltaSegments[{i}].deltaPath", out var deltaErr))
                    return Fail(ctx, deltaErr);

                var basePath = NormalizeZipPath(seg.baseLabelsPath);
                var deltaPath = NormalizeZipPath(seg.deltaPath);
                if (!entriesByName.ContainsKey(basePath))
                    return Fail(ctx, $"bundle missing referenced file: {basePath} (from shNDeltaSegments[{i}].baseLabelsPath)");
                if (!entriesByName.ContainsKey(deltaPath))
                    return Fail(ctx, $"bundle missing referenced file: {deltaPath} (from shNDeltaSegments[{i}].deltaPath)");

                expectedStart = seg.startFrame + seg.frameCount;
                totalFrames += seg.frameCount;

                // 导入期展开 delta 的实现会在任务 3.8 做,这里先做结构性校验.
            }

            if (segments[0].startFrame != 0)
                return Fail(ctx, "meta.json invalid shNDeltaSegments: first segment startFrame must be 0");

            if (totalFrames != meta.frameCount)
            {
                return Fail(ctx,
                    $"meta.json invalid shNDeltaSegments: total frameCount sum must be {meta.frameCount}, got {totalFrames}");
            }

            return true;
        }

        static bool TryValidatePerFrameTemplate(string template, string fieldName, out string error)
        {
            if (string.IsNullOrEmpty(template))
            {
                error = $"meta.json missing required field: {fieldName}";
                return false;
            }

            if (!template.Contains("{frame}"))
            {
                error = $"meta.json invalid {fieldName}: template must contain {{frame}}, got \"{template}\"";
                return false;
            }

            if (template.Contains("\\"))
            {
                error = $"meta.json invalid {fieldName}: path must use \"/\" separators, got \"{template}\"";
                return false;
            }

            // 通过替换一个示例 frame,把模板当作普通相对路径进行校验.
            var example = template.Replace("{frame}", "00000");
            return TryValidateRelativePath(example, fieldName, out error);
        }

        static bool TryValidateRelativePath(string path, string fieldName, out string error)
        {
            if (string.IsNullOrEmpty(path))
            {
                error = $"meta.json missing required field: {fieldName}";
                return false;
            }

            if (path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("\\", StringComparison.Ordinal))
            {
                error = $"meta.json invalid {fieldName}: path must be relative, got \"{path}\"";
                return false;
            }

            if (path.Contains(":"))
            {
                // 防御: Windows 盘符或 URI 等.
                error = $"meta.json invalid {fieldName}: path must not contain \":\", got \"{path}\"";
                return false;
            }

            if (path.Contains("\\"))
            {
                error = $"meta.json invalid {fieldName}: path must use \"/\" separators, got \"{path}\"";
                return false;
            }

            // 防御: path traversal.
            var normalized = NormalizeZipPath(path);
            var parts = normalized.Split('/');
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (string.IsNullOrEmpty(part))
                {
                    error = $"meta.json invalid {fieldName}: path contains empty segment, got \"{path}\"";
                    return false;
                }

                if (part == "." || part == "..")
                {
                    error = $"meta.json invalid {fieldName}: path must not contain \".\" or \"..\", got \"{path}\"";
                    return false;
                }
            }

            error = null;
            return true;
        }

        static bool TryValidatePerFrameFilesExist(Dictionary<string, ZipArchiveEntry> entriesByName, string template,
            int frameCount, out string missingPath)
        {
            for (var frame = 0; frame < frameCount; frame++)
            {
                var path = NormalizeZipPath(template.Replace("{frame}", frame.ToString("D5")));
                if (!entriesByName.ContainsKey(path))
                {
                    missingPath = path;
                    return false;
                }
            }

            missingPath = null;
            return true;
        }

        static bool Fail(AssetImportContext ctx, string message)
        {
            if (GsplatSettings.Instance.ShowImportErrors)
                Debug.LogError($"{ctx.assetPath} import error: {message}");
            return false;
        }
    }
}
