// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using ZipCompressionLevel = System.IO.Compression.CompressionLevel;

namespace Gsplat.Tests
{
    public sealed class GsplatSog4DImporterTests
    {
        const string k_TestRootAssetPath = "Assets/__GsplatSog4DImporterTests";

        // 注意: 这里故意把样例存为 `.zip`,避免 Unity 在导入包时就触发 `.sog4d` importer.
        // 测试里会把它复制成 `.sog4d`,再显式调用 ImportAsset.
        const string k_BaseBundleZipPath =
            "Packages/wu.yize.gsplat/Tests/Editor/Sog4DTestData/minimal_valid_delta_v1.sog4d.zip";

        // --------------------------------------------------------------------
        // Json DTO: 用 JsonUtility 生成 meta.json,避免手写 JSON 出现逗号/转义错误.
        // - 字段名必须与 spec 完全一致,因此这里使用 lowerCamelCase.
        // --------------------------------------------------------------------
        [Serializable]
        sealed class Sog4DMetaJson
        {
            public string format = "sog4d";
            public int version = 1;
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
            public string type = "row-major";
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
            public string shNCentroidsPath;
            public string shNLabelsEncoding; // "full" | "delta-v1"

            // delta-v1
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

        [SetUp]
        public void SetUp()
        {
            // 目的: 确保 importer 的错误会被输出到 Console,便于 LogAssert 捕获.
            GsplatSettings.Instance.ShowImportErrors = true;

            if (!AssetDatabase.IsValidFolder(k_TestRootAssetPath))
                AssetDatabase.CreateFolder("Assets", "__GsplatSog4DImporterTests");
        }

        [TearDown]
        public void TearDown()
        {
            // 清理测试生成的临时资产,避免污染其它测试.
            AssetDatabase.DeleteAsset(k_TestRootAssetPath);
        }

        static string GetProjectRootPath()
        {
            // Application.dataPath => <project>/Assets
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        static byte[] ReadPackageFileBytes(string packageRelativePath)
        {
            var fullPath = Path.GetFullPath(Path.Combine(GetProjectRootPath(), packageRelativePath));
            return File.ReadAllBytes(fullPath);
        }

        static byte[] ReadZipEntryBytes(byte[] zipBytes, string entryName)
        {
            using var ms = new MemoryStream(zipBytes, writable: false);
            using var z = new ZipArchive(ms, ZipArchiveMode.Read);
            var e = z.GetEntry(entryName);
            Assert.IsNotNull(e, $"zip 缺少 entry: {entryName}");
            using var s = e.Open();
            using var outMs = new MemoryStream();
            s.CopyTo(outMs);
            return outMs.ToArray();
        }

	        static bool SupportsWebpDecoding(byte[] baseBundleZipBytes)
	        {
	            // 说明:
	            // - 目标是判断“当前环境下 importer 能否解码 WebP 数据图”.
	            // - importer 的策略是:
	            //   1) 先尝试 Unity 内置 `ImageConversion.LoadImage`(如果宿主 Unity 支持 WebP,这是最快路径).
	            //   2) 若 LoadImage 返回 false,再 fallback 到包内自带的 `libwebp` decoder(见 `GsplatWebpNative`).
	            // - 因此 tests 的能力探测也按同样顺序,避免因为 Unity 版本差异造成误判.
	            //
	            // 注意: tests 程序集不直接引用 `Gsplat.Editor` asmdef,避免引入不必要的编译期耦合.
	            var webp = ReadZipEntryBytes(baseBundleZipBytes, "frames/00000/sh0.webp");

	            // 1) Unity 内置解码(如果支持,直接返回 true).
	            Texture2D unityTex = null;
	            try
	            {
	                unityTex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true);
	                if (ImageConversion.LoadImage(unityTex, webp, markNonReadable: false))
	                    return true;
	            }
	            catch
	            {
	                // 忽略异常,继续尝试 native fallback.
	            }
	            finally
	            {
	                if (unityTex != null)
	                    UnityEngine.Object.DestroyImmediate(unityTex);
	            }

	            // 2) native fallback(反射调用 `Gsplat.Editor.GsplatWebpNative.SupportsWebpDecoding(byte[])`).
	            try
	            {
	                var t = Type.GetType("Gsplat.Editor.GsplatWebpNative, Gsplat.Editor");
	                if (t == null)
	                    return false;

	                var m = t.GetMethod("SupportsWebpDecoding",
	                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public |
	                    System.Reflection.BindingFlags.NonPublic);
	                if (m == null)
	                    return false;

	                return m.Invoke(null, new object[] { webp }) is bool ok && ok;
	            }
	            catch
	            {
	                return false;
	            }
	        }

        static void WriteZipToSog4dAsset(string assetPath, byte[] zipBytes)
        {
            var fullPath = Path.GetFullPath(Path.Combine(GetProjectRootPath(), assetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, zipBytes);
        }

        static void ImportAssetExpectError(string assetPath, string expectedImporterMessage)
        {
            LogAssert.Expect(LogType.Error, $"{assetPath} import error: {expectedImporterMessage}");
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        static byte[] BuildMetaJsonBytes(Sog4DMetaJson meta)
        {
            // 注意: importer 使用 strict UTF-8 解码,这里明确禁用 BOM.
            var json = JsonUtility.ToJson(meta, prettyPrint: true);
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
        }

        static void CreateBundleFromBase(
            byte[] baseZipBytes,
            string destAssetPath,
            byte[] metaJsonBytes,
            (string name, byte[] bytes)[] replacements = null)
        {
            var fullPath = Path.GetFullPath(Path.Combine(GetProjectRootPath(), destAssetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

            using var baseMs = new MemoryStream(baseZipBytes, writable: false);
            using var baseZip = new ZipArchive(baseMs, ZipArchiveMode.Read);
            using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var outZip = new ZipArchive(fs, ZipArchiveMode.Create);

            // 1) 写入 meta.json(替换掉 base 的 meta).
            var metaEntry = outZip.CreateEntry("meta.json", ZipCompressionLevel.NoCompression);
            using (var s = metaEntry.Open())
                s.Write(metaJsonBytes, 0, metaJsonBytes.Length);

            // 2) 拷贝 base 的其它 entry(可按 replacements 覆盖).
            for (var i = 0; i < baseZip.Entries.Count; i++)
            {
                var e = baseZip.Entries[i];
                if (string.IsNullOrEmpty(e.Name))
                    continue; // 目录 entry
                if (e.FullName == "meta.json")
                    continue;

                var replaced = false;
                if (replacements != null)
                {
                    for (var r = 0; r < replacements.Length; r++)
                    {
                        if (replacements[r].name == e.FullName)
                        {
                            replaced = true;
                            break;
                        }
                    }
                }
                if (replaced)
                    continue;

                var outEntry = outZip.CreateEntry(e.FullName, ZipCompressionLevel.NoCompression);
                using var src = e.Open();
                using var dst = outEntry.Open();
                src.CopyTo(dst);
            }

            // 3) 写入 replacements.
            if (replacements == null)
                return;

            for (var i = 0; i < replacements.Length; i++)
            {
                var (name, bytes) = replacements[i];
                var outEntry = outZip.CreateEntry(name, ZipCompressionLevel.NoCompression);
                using var s = outEntry.Open();
                s.Write(bytes, 0, bytes.Length);
            }
        }

        [Test]
        public void Import_MissingMetaJson_FailsWithActionableError()
        {
            // 目标: 缺少 meta.json 时必须明确失败.
            var assetPath = k_TestRootAssetPath + "/missing_meta.sog4d";
            var fullPath = Path.GetFullPath(Path.Combine(GetProjectRootPath(), assetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

            // 创建一个空 ZIP(没有 meta.json).
            using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
            {
            }

            ImportAssetExpectError(assetPath, "bundle missing required file: meta.json");
        }

        [Test]
        public void Import_LayoutTooSmall_FailsBeforeDecodingWebp()
        {
            // 目标: layout(width*height < splatCount) 必须在 meta 校验阶段失败.
            var assetPath = k_TestRootAssetPath + "/layout_too_small.sog4d";
            var fullPath = Path.GetFullPath(Path.Combine(GetProjectRootPath(), assetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

            var meta = new Sog4DMetaJson
            {
                splatCount = 4,
                frameCount = 1,
                timeMapping = new TimeMappingJson { type = "uniform" },
                layout = new LayoutJson { width = 1, height = 1 },
                streams = new StreamsJson
                {
                    // streams 这里随便填,因为我们期望在 layout 校验阶段就失败.
                    position = new PositionStreamJson
                    {
                        rangeMin = new[] { Vector3.zero },
                        rangeMax = new[] { Vector3.one },
                        hiPath = "frames/{frame}/position_hi.webp",
                        loPath = "frames/{frame}/position_lo.webp"
                    },
                    scale = new ScaleStreamJson
                    {
                        codebook = new[] { Vector3.one },
                        indicesPath = "frames/{frame}/scale_indices.webp"
                    },
                    rotation = new RotationStreamJson { path = "frames/{frame}/rotation.webp" },
                    sh = new ShStreamJson
                    {
                        bands = 0,
                        sh0Path = "frames/{frame}/sh0.webp",
                        sh0Codebook = new float[256]
                    }
                }
            };

            var metaBytes = BuildMetaJsonBytes(meta);
            using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var e = z.CreateEntry("meta.json", ZipCompressionLevel.NoCompression);
                using var s = e.Open();
                s.Write(metaBytes, 0, metaBytes.Length);
            }

            ImportAssetExpectError(assetPath, "meta.json invalid layout size: width*height=1 < splatCount=4");
        }

        [Test]
        public void Import_MissingReferencedFile_FailsWithActionableError()
        {
            // 目标: meta 引用的 per-frame 文件缺失时,必须报出缺失路径.
            var assetPath = k_TestRootAssetPath + "/missing_file.sog4d";
            var fullPath = Path.GetFullPath(Path.Combine(GetProjectRootPath(), assetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

            var meta = new Sog4DMetaJson
            {
                splatCount = 4,
                frameCount = 1,
                timeMapping = new TimeMappingJson { type = "uniform" },
                layout = new LayoutJson { width = 2, height = 2 },
                streams = new StreamsJson
                {
                    position = new PositionStreamJson
                    {
                        rangeMin = new[] { Vector3.zero },
                        rangeMax = new[] { Vector3.one },
                        hiPath = "frames/{frame}/position_hi.webp",
                        loPath = "frames/{frame}/position_lo.webp"
                    },
                    // 其它 streams 即使缺失也不会被走到(这里故意不填,避免测试夹带其它错误).
                }
            };

            var metaBytes = BuildMetaJsonBytes(meta);
            using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var e = z.CreateEntry("meta.json", ZipCompressionLevel.NoCompression);
                using var s = e.Open();
                s.Write(metaBytes, 0, metaBytes.Length);
            }

            ImportAssetExpectError(assetPath,
                "bundle missing referenced file: frames/00000/position_hi.webp (from streams.position.hiPath)");
        }

        [Test]
        public void Import_MinimalValidDeltaV1_Succeeds()
        {
            // 目标: 最小 `.sog4d` 样例能完整导入,并生成可播放 prefab(main object).
            var baseZipBytes = ReadPackageFileBytes(k_BaseBundleZipPath);
            if (!SupportsWebpDecoding(baseZipBytes))
                Assert.Ignore("当前 Unity 版本不支持 WebP 解码,跳过 .sog4d importer 回归测试.");

            var assetPath = k_TestRootAssetPath + "/minimal_valid.sog4d";
            WriteZipToSog4dAsset(assetPath, baseZipBytes);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            Assert.IsNotNull(prefab, "导入后 main prefab 应该存在");

            var renderer = prefab.GetComponent<GsplatSequenceRenderer>();
            Assert.IsNotNull(renderer, "prefab 上应自动挂载 GsplatSequenceRenderer");
            Assert.IsNotNull(renderer.SequenceAsset, "renderer.SequenceAsset 应自动绑定");
            Assert.AreEqual(3, renderer.SequenceAsset.FrameCount);
            Assert.AreEqual((uint)4, renderer.SequenceAsset.SplatCount);
            Assert.AreEqual(1, renderer.SequenceAsset.SHBands);
        }

        [Test]
        public void Import_ScaleIndicesOutOfRange_FailsFast()
        {
            // 目标: scale_indices.webp 中出现 index >= codebook.Length 时必须失败(对齐 spec).
            var baseZipBytes = ReadPackageFileBytes(k_BaseBundleZipPath);
            if (!SupportsWebpDecoding(baseZipBytes))
                Assert.Ignore("当前 Unity 版本不支持 WebP 解码,跳过需要解码的测试.");

            var meta = new Sog4DMetaJson
            {
                splatCount = 4,
                frameCount = 3,
                timeMapping = new TimeMappingJson { type = "uniform" },
                layout = new LayoutJson { width = 2, height = 2 },
                streams = new StreamsJson
                {
                    position = new PositionStreamJson
                    {
                        rangeMin = new[] { Vector3.zero, Vector3.zero, Vector3.zero },
                        rangeMax = new[] { Vector3.one, Vector3.one, Vector3.one },
                        hiPath = "frames/{frame}/position_hi.webp",
                        loPath = "frames/{frame}/position_lo.webp"
                    },
                    // 关键: 把 scale codebook 缩到 1,让 base bundle 的 scale_indices 立刻越界.
                    scale = new ScaleStreamJson
                    {
                        codebook = new[] { Vector3.one },
                        indicesPath = "frames/{frame}/scale_indices.webp"
                    },
                    rotation = new RotationStreamJson { path = "frames/{frame}/rotation.webp" },
                    sh = new ShStreamJson
                    {
                        bands = 1,
                        sh0Path = "frames/{frame}/sh0.webp",
                        sh0Codebook = new float[256],
                        shNCount = 4,
                        shNCentroidsType = "f16",
                        shNCentroidsPath = "shN_centroids.bin",
                        shNLabelsEncoding = "delta-v1",
                        shNDeltaSegments = new[]
                        {
                            new ShNDeltaSegmentJson
                            {
                                startFrame = 0,
                                frameCount = 2,
                                baseLabelsPath = "frames/00000/shN_labels.webp",
                                deltaPath = "sh/delta_00000.bin"
                            },
                            new ShNDeltaSegmentJson
                            {
                                startFrame = 2,
                                frameCount = 1,
                                baseLabelsPath = "frames/00002/shN_labels.webp",
                                deltaPath = "sh/delta_00002.bin"
                            }
                        }
                    }
                }
            };

            var assetPath = k_TestRootAssetPath + "/scale_oob.sog4d";
            CreateBundleFromBase(baseZipBytes, assetPath, BuildMetaJsonBytes(meta));

            ImportAssetExpectError(assetPath,
                "ScaleIndices u16 index out of range: frame=0, splatId=0, value=3, maxExclusive=1. path=frames/00000/scale_indices.webp");
        }

        [Test]
        public void Import_ShNBaseLabelsOutOfRange_FailsFast()
        {
            // 目标: baseLabelsPath 的 labels 出现 label >= shNCount 时必须失败(对齐 spec).
            var baseZipBytes = ReadPackageFileBytes(k_BaseBundleZipPath);
            if (!SupportsWebpDecoding(baseZipBytes))
                Assert.Ignore("当前 Unity 版本不支持 WebP 解码,跳过需要解码的测试.");

            // 把 shNCount 缩到 1,并同步把 centroids.bin 截断到符合大小.
            var centroidsFull = ReadZipEntryBytes(baseZipBytes, "shN_centroids.bin");
            var centroidsTrimmed = new byte[18]; // shNCount=1, bands=1 => 1*3*3*2 bytes
            Array.Copy(centroidsFull, 0, centroidsTrimmed, 0, centroidsTrimmed.Length);

            var meta = new Sog4DMetaJson
            {
                splatCount = 4,
                frameCount = 3,
                timeMapping = new TimeMappingJson { type = "uniform" },
                layout = new LayoutJson { width = 2, height = 2 },
                streams = new StreamsJson
                {
                    position = new PositionStreamJson
                    {
                        rangeMin = new[] { Vector3.zero, Vector3.zero, Vector3.zero },
                        rangeMax = new[] { Vector3.one, Vector3.one, Vector3.one },
                        hiPath = "frames/{frame}/position_hi.webp",
                        loPath = "frames/{frame}/position_lo.webp"
                    },
                    scale = new ScaleStreamJson
                    {
                        codebook = new[] { Vector3.one, Vector3.one * 2, Vector3.one * 3, Vector3.one * 4 },
                        indicesPath = "frames/{frame}/scale_indices.webp"
                    },
                    rotation = new RotationStreamJson { path = "frames/{frame}/rotation.webp" },
                    sh = new ShStreamJson
                    {
                        bands = 1,
                        sh0Path = "frames/{frame}/sh0.webp",
                        sh0Codebook = new float[256],
                        shNCount = 1,
                        shNCentroidsType = "f16",
                        shNCentroidsPath = "shN_centroids.bin",
                        shNLabelsEncoding = "delta-v1",
                        shNDeltaSegments = new[]
                        {
                            new ShNDeltaSegmentJson
                            {
                                startFrame = 0,
                                frameCount = 2,
                                baseLabelsPath = "frames/00000/shN_labels.webp",
                                deltaPath = "sh/delta_00000.bin"
                            },
                            new ShNDeltaSegmentJson
                            {
                                startFrame = 2,
                                frameCount = 1,
                                baseLabelsPath = "frames/00002/shN_labels.webp",
                                deltaPath = "sh/delta_00002.bin"
                            }
                        }
                    }
                }
            };

            var assetPath = k_TestRootAssetPath + "/shn_oob.sog4d";
            CreateBundleFromBase(
                baseZipBytes,
                assetPath,
                BuildMetaJsonBytes(meta),
                replacements: new[] { ("shN_centroids.bin", centroidsTrimmed) });

            ImportAssetExpectError(assetPath,
                "shN base labels out of range: segment=0, frame=0, splatId=0, label=1 >= shNCount=1. path=frames/00000/shN_labels.webp");
        }

        [Test]
        public void Import_DeltaSegmentsNotContinuous_FailsWithActionableError()
        {
            // 目标: segments 不连续/不覆盖 frameCount 时必须在 meta 校验阶段失败.
            var baseZipBytes = ReadPackageFileBytes(k_BaseBundleZipPath);

            var meta = new Sog4DMetaJson
            {
                splatCount = 4,
                frameCount = 3,
                timeMapping = new TimeMappingJson { type = "uniform" },
                layout = new LayoutJson { width = 2, height = 2 },
                streams = new StreamsJson
                {
                    position = new PositionStreamJson
                    {
                        rangeMin = new[] { Vector3.zero, Vector3.zero, Vector3.zero },
                        rangeMax = new[] { Vector3.one, Vector3.one, Vector3.one },
                        hiPath = "frames/{frame}/position_hi.webp",
                        loPath = "frames/{frame}/position_lo.webp"
                    },
                    scale = new ScaleStreamJson
                    {
                        codebook = new[] { Vector3.one, Vector3.one * 2, Vector3.one * 3, Vector3.one * 4 },
                        indicesPath = "frames/{frame}/scale_indices.webp"
                    },
                    rotation = new RotationStreamJson { path = "frames/{frame}/rotation.webp" },
                    sh = new ShStreamJson
                    {
                        bands = 1,
                        sh0Path = "frames/{frame}/sh0.webp",
                        sh0Codebook = new float[256],
                        shNCount = 4,
                        shNCentroidsType = "f16",
                        shNCentroidsPath = "shN_centroids.bin",
                        shNLabelsEncoding = "delta-v1",
                        // 故意把 startFrame 改成 1,破坏连续性.
                        shNDeltaSegments = new[]
                        {
                            new ShNDeltaSegmentJson
                            {
                                startFrame = 1,
                                frameCount = 2,
                                baseLabelsPath = "frames/00000/shN_labels.webp",
                                deltaPath = "sh/delta_00000.bin"
                            }
                        }
                    }
                }
            };

            var assetPath = k_TestRootAssetPath + "/segments_bad.sog4d";
            CreateBundleFromBase(baseZipBytes, assetPath, BuildMetaJsonBytes(meta));

            ImportAssetExpectError(assetPath, "meta.json invalid shNDeltaSegments[0].startFrame: expected 0, got 1");
        }

        static byte[] BuildDeltaV1Header(uint segmentStartFrame, uint segmentFrameCount, uint splatCount, uint shNCount)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
            bw.Write(Encoding.ASCII.GetBytes("SOG4DLB1"));
            bw.Write((uint)1);
            bw.Write(segmentStartFrame);
            bw.Write(segmentFrameCount);
            bw.Write(splatCount);
            bw.Write(shNCount);
            return ms.ToArray();
        }

        static byte[] BuildDeltaV1_OnlyHeader(uint segmentStartFrame, uint segmentFrameCount, uint splatCount, uint shNCount)
        {
            return BuildDeltaV1Header(segmentStartFrame, segmentFrameCount, splatCount, shNCount);
        }

        static byte[] BuildDeltaV1_WithUpdateCount(uint segmentStartFrame, uint segmentFrameCount, uint splatCount, uint shNCount,
            uint updateCount, (uint splatId, ushort label)[] updates)
        {
            using var ms = new MemoryStream();
            var hdr = BuildDeltaV1Header(segmentStartFrame, segmentFrameCount, splatCount, shNCount);
            ms.Write(hdr, 0, hdr.Length);

            // 仅写 1 个 delta block(对应 segment 内第 2 帧).
            using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
            bw.Write(updateCount);
            if (updates != null)
            {
                for (var i = 0; i < updates.Length; i++)
                {
                    var (sid, lab) = updates[i];
                    bw.Write(sid);
                    bw.Write(lab);
                    bw.Write((ushort)0); // reserved
                }
            }

            return ms.ToArray();
        }

        [Test]
        public void Import_DeltaHeaderMismatch_FailsFast()
        {
            // 目标: delta header 与 meta 不一致时必须失败.
            var baseZipBytes = ReadPackageFileBytes(k_BaseBundleZipPath);
            if (!SupportsWebpDecoding(baseZipBytes))
                Assert.Ignore("当前 Unity 版本不支持 WebP 解码,跳过需要解码的测试.");

            var meta = new Sog4DMetaJson
            {
                splatCount = 4,
                frameCount = 3,
                timeMapping = new TimeMappingJson { type = "uniform" },
                layout = new LayoutJson { width = 2, height = 2 },
                streams = new StreamsJson
                {
                    position = new PositionStreamJson
                    {
                        rangeMin = new[] { Vector3.zero, Vector3.zero, Vector3.zero },
                        rangeMax = new[] { Vector3.one, Vector3.one, Vector3.one },
                        hiPath = "frames/{frame}/position_hi.webp",
                        loPath = "frames/{frame}/position_lo.webp"
                    },
                    scale = new ScaleStreamJson
                    {
                        codebook = new[] { Vector3.one, Vector3.one * 2, Vector3.one * 3, Vector3.one * 4 },
                        indicesPath = "frames/{frame}/scale_indices.webp"
                    },
                    rotation = new RotationStreamJson { path = "frames/{frame}/rotation.webp" },
                    sh = new ShStreamJson
                    {
                        bands = 1,
                        sh0Path = "frames/{frame}/sh0.webp",
                        sh0Codebook = new float[256],
                        shNCount = 4,
                        shNCentroidsType = "f16",
                        shNCentroidsPath = "shN_centroids.bin",
                        shNLabelsEncoding = "delta-v1",
                        shNDeltaSegments = new[]
                        {
                            new ShNDeltaSegmentJson
                            {
                                startFrame = 0,
                                frameCount = 2,
                                baseLabelsPath = "frames/00000/shN_labels.webp",
                                deltaPath = "sh/delta_00000.bin"
                            },
                            new ShNDeltaSegmentJson
                            {
                                startFrame = 2,
                                frameCount = 1,
                                baseLabelsPath = "frames/00002/shN_labels.webp",
                                deltaPath = "sh/delta_00002.bin"
                            }
                        }
                    }
                }
            };

            // 故意把 delta header 的 shNCount 写错.
            var badDelta = BuildDeltaV1_OnlyHeader(segmentStartFrame: 0, segmentFrameCount: 2, splatCount: 4, shNCount: 999);

            var assetPath = k_TestRootAssetPath + "/delta_header_bad.sog4d";
            CreateBundleFromBase(
                baseZipBytes,
                assetPath,
                BuildMetaJsonBytes(meta),
                replacements: new[] { ("sh/delta_00000.bin", badDelta) });

            ImportAssetExpectError(assetPath, "delta-v1 header mismatch: shNCount expected 4, got 999 (segment 0)");
        }

        [Test]
        public void Import_DeltaSplatIdNotIncreasing_FailsFast()
        {
            // 目标: 同一个 delta block 内 splatId 必须严格递增(不允许重复/倒序).
            var baseZipBytes = ReadPackageFileBytes(k_BaseBundleZipPath);
            if (!SupportsWebpDecoding(baseZipBytes))
                Assert.Ignore("当前 Unity 版本不支持 WebP 解码,跳过需要解码的测试.");

            // header 正确,但 update 内倒序.
            var delta = BuildDeltaV1_WithUpdateCount(
                segmentStartFrame: 0,
                segmentFrameCount: 2,
                splatCount: 4,
                shNCount: 4,
                updateCount: 2,
                updates: new[] { (2u, (ushort)0), (1u, (ushort)0) });

            var assetPath = k_TestRootAssetPath + "/delta_order_bad.sog4d";
            // 直接沿用 base bundle 的 meta.json,减少不必要的变量.
            // 说明: meta.json 在 base zip 里已经是合法的 delta-v1(segments=2).
            var baseMeta = ReadZipEntryBytes(baseZipBytes, "meta.json");
            CreateBundleFromBase(
                baseZipBytes,
                assetPath,
                baseMeta,
                replacements: new[] { ("sh/delta_00000.bin", delta) });

            ImportAssetExpectError(assetPath,
                "delta-v1 invalid splatId order: sh/delta_00000.bin: splatId must be strictly increasing within a block (frame 1, update 1)");
        }

        [Test]
        public void Import_DeltaUpdateCountOverflow_FailsFast()
        {
            // 目标: updateCount > splatCount 时必须 fail-fast,避免读溢出.
            var baseZipBytes = ReadPackageFileBytes(k_BaseBundleZipPath);
            if (!SupportsWebpDecoding(baseZipBytes))
                Assert.Ignore("当前 Unity 版本不支持 WebP 解码,跳过需要解码的测试.");

            var delta = BuildDeltaV1_WithUpdateCount(
                segmentStartFrame: 0,
                segmentFrameCount: 2,
                splatCount: 4,
                shNCount: 4,
                updateCount: 5,
                updates: null);

            var assetPath = k_TestRootAssetPath + "/delta_updatecount_bad.sog4d";
            var baseMeta = ReadZipEntryBytes(baseZipBytes, "meta.json");
            CreateBundleFromBase(
                baseZipBytes,
                assetPath,
                baseMeta,
                replacements: new[] { ("sh/delta_00000.bin", delta) });

            ImportAssetExpectError(assetPath,
                "delta-v1 invalid updateCount: sh/delta_00000.bin: updateCount=5 > splatCount=4 at frame 1");
        }
    }
}
