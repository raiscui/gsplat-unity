// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Gsplat.Tests
{
    public sealed class GsplatSplat4DImporterDeltaV1Tests
    {
        const string k_TestRootAssetPath = "Assets/__GsplatSplat4DImporterDeltaV1Tests";
        const string k_TestAssetPath = k_TestRootAssetPath + "/minimal_delta_v1.splat4d";
        const string k_FullLabelSingleFrameAssetPath = k_TestRootAssetPath + "/minimal_full_label_single_frame_v2.splat4d";
        const string k_StaticSingleFrameAssetPath = k_TestRootAssetPath + "/static_single_frame_v1.splat4d";
        const string k_StaticSingleFrameFromFixtureAssetPath = k_TestRootAssetPath + "/static_single_frame_from_fixture.splat4d";

        [SetUp]
        public void SetUp()
        {
            // 目的: 让 importer 的错误明确出现在 Console,便于定位.
            GsplatSettings.Instance.ShowImportErrors = true;

            if (!AssetDatabase.IsValidFolder(k_TestRootAssetPath))
                AssetDatabase.CreateFolder("Assets", "__GsplatSplat4DImporterDeltaV1Tests");
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

        static void WriteBytesToAssetPath(string assetPath, byte[] bytes)
        {
            var fullPath = Path.GetFullPath(Path.Combine(GetProjectRootPath(), assetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, bytes);
        }

        static string QuoteProcessArg(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            if (value.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '"' }) < 0)
                return value;

            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        static void ExportFixturePlyToAssetPath(string assetPath)
        {
            var projectRoot = GetProjectRootPath();
            var scriptPath = Path.Combine(projectRoot, "Packages", "wu.yize.gsplat", "Tools~", "Splat4D",
                "ply_sequence_to_splat4d.py");
            var fixturePlyPath = Path.Combine(projectRoot, "Packages", "wu.yize.gsplat", "Tools~", "Splat4D",
                "tests", "data", "single_frame_valid_3dgs.ply");
            var outputFullPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(outputFullPath));

            Assert.IsTrue(File.Exists(scriptPath), $"缺少 exporter 脚本: {scriptPath}");
            Assert.IsTrue(File.Exists(fixturePlyPath), $"缺少单帧 3DGS 夹具: {fixturePlyPath}");

            var arguments = string.Join(" ", new[]
            {
                QuoteProcessArg(scriptPath),
                "--input-ply",
                QuoteProcessArg(fixturePlyPath),
                "--output",
                QuoteProcessArg(outputFullPath),
                "--mode",
                "average",
                "--opacity-mode",
                "linear",
                "--scale-mode",
                "linear"
            });

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = arguments,
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            Assert.IsTrue(process.Start(), "启动 Python exporter 失败.");

            if (!process.WaitForExit(120000))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // 这里只是测试超时后的兜底清理.
                }

                Assert.Fail("Python exporter 执行超时(120s).");
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            Assert.AreEqual(0, process.ExitCode,
                $"Python exporter 失败.\nstdout:\n{stdout}\nstderr:\n{stderr}");
            Assert.IsTrue(File.Exists(outputFullPath),
                $"exporter 已返回成功,但未生成输出文件: {outputFullPath}\nstdout:\n{stdout}\nstderr:\n{stderr}");
        }

        static uint FourCC(string code)
        {
            if (code == null || code.Length != 4)
                throw new ArgumentException("fourcc must be 4 chars", nameof(code));
            return (uint)(byte)code[0] |
                   ((uint)(byte)code[1] << 8) |
                   ((uint)(byte)code[2] << 16) |
                   ((uint)(byte)code[3] << 24);
        }

        // --------------------------------------------------------------------
        // 最小 `.splat4d v2` 构造器:
        // - shBands=1, labelsEncoding=delta-v1, 1 个 segment 覆盖全帧.
        // - delta-v1 至少包含 1 个非 0 updateCount.
        // --------------------------------------------------------------------
        static byte[] BuildMinimalSplat4DV2DeltaV1()
        {
            const int splatCount = 8;
            const int recordSizeBytes = 64;
            const int shBands = 1;
            const int frameCount = 3;

            const int sh1CodebookCount = 4;
            const int sh1CoeffCount = 3;

            // -----------------------------
            // 1) sections payload
            // -----------------------------
            byte[] BuildRecsBytes()
            {
                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);

                for (var i = 0; i < splatCount; i++)
                {
                    // position(float3)
                    bw.Write((float)i);
                    bw.Write(0.0f);
                    bw.Write(0.0f);

                    // scale(float3): 避免 0 导致渲染侧出现退化.
                    bw.Write(1.0f);
                    bw.Write(1.0f);
                    bw.Write(1.0f);

                    // color(bytes): baseRgb=0.5 -> f_dc=0, opacity=1
                    bw.Write((byte)128);
                    bw.Write((byte)128);
                    bw.Write((byte)128);
                    bw.Write((byte)255);

                    // rotation(bytes): 约等于 identity(归一化后接近 (1,0,0,0))
                    bw.Write((byte)255); // w
                    bw.Write((byte)128); // x
                    bw.Write((byte)128); // y
                    bw.Write((byte)128); // z

                    // velocity(float3)
                    bw.Write(0.0f);
                    bw.Write(0.0f);
                    bw.Write(0.0f);

                    // time/duration(window): 让其不触发 clamp warning.
                    bw.Write(0.0f); // time
                    bw.Write(1.0f); // duration

                    // pad(float3)
                    bw.Write(0.0f);
                    bw.Write(0.0f);
                    bw.Write(0.0f);
                }

                var bytes = ms.ToArray();
                Assert.AreEqual(splatCount * recordSizeBytes, bytes.Length, "RECS bytes length mismatch");
                return bytes;
            }

            byte[] BuildMetaBytes()
            {
                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);

                // META v1, length=64
                bw.Write(1u); // metaVersion
                bw.Write(0.01f); // temporalGaussianCutoff
                bw.Write(3u); // deltaSegmentLength(仅用于记录,tests 不依赖)
                bw.Write(0u); // reserved0

                // sh1 band info
                bw.Write((uint)sh1CodebookCount);
                bw.Write(2u); // centroidsType: 2=f32
                bw.Write(2u); // labelsEncoding: 2=delta-v1
                bw.Write(0u); // reserved

                // sh2 band info(不使用)
                bw.Write(0u);
                bw.Write(0u);
                bw.Write(0u);
                bw.Write(0u);

                // sh3 band info(不使用)
                bw.Write(0u);
                bw.Write(0u);
                bw.Write(0u);
                bw.Write(0u);

                var bytes = ms.ToArray();
                Assert.AreEqual(64, bytes.Length, "META bytes length must be 64");
                return bytes;
            }

            byte[] BuildSh1CentroidsBytes()
            {
                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);

                // entry-major: [label][coeff] -> float3
                for (var k = 0; k < sh1CodebookCount; k++)
                {
                    for (var c = 0; c < sh1CoeffCount; c++)
                    {
                        var baseV = k * 10.0f + c;
                        bw.Write(baseV + 0.0f);
                        bw.Write(baseV + 0.1f);
                        bw.Write(baseV + 0.2f);
                    }
                }

                var bytes = ms.ToArray();
                Assert.AreEqual(sh1CodebookCount * sh1CoeffCount * 3 * 4, bytes.Length, "SHCT bytes length mismatch");
                return bytes;
            }

            byte[] BuildSh1BaseLabelsBytes()
            {
                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);
                for (var i = 0; i < splatCount; i++)
                    bw.Write((ushort)0);
                var bytes = ms.ToArray();
                Assert.AreEqual(splatCount * 2, bytes.Length, "SHLB bytes length mismatch");
                return bytes;
            }

            byte[] BuildSh1DeltaBytes()
            {
                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);

                // delta-v1 header
                bw.Write(new byte[] { (byte)'S', (byte)'P', (byte)'L', (byte)'4', (byte)'D', (byte)'L', (byte)'B', (byte)'1' });
                bw.Write(1u); // version
                bw.Write(0u); // segmentStartFrame
                bw.Write((uint)frameCount); // segmentFrameCount
                bw.Write((uint)splatCount);
                bw.Write((uint)sh1CodebookCount); // labelCount

                // frame=1: 2 updates
                bw.Write(2u); // updateCount
                bw.Write(3u); // splatId
                bw.Write((ushort)1); // newLabel
                bw.Write((ushort)0); // reserved
                bw.Write(5u);
                bw.Write((ushort)2);
                bw.Write((ushort)0);

                // frame=2: 1 update
                bw.Write(1u);
                bw.Write(5u);
                bw.Write((ushort)3);
                bw.Write((ushort)0);

                var bytes = ms.ToArray();
                Assert.Greater(bytes.Length, 28, "delta bytes should include header+body");
                return bytes;
            }

            var recsBytes = BuildRecsBytes();
            var metaBytes = BuildMetaBytes();
            var shctBytes = BuildSh1CentroidsBytes();
            var shlbBytes = BuildSh1BaseLabelsBytes();
            var shdlBytes = BuildSh1DeltaBytes();

            var sects = new List<(uint kind, uint band, uint startFrame, uint frameCount, byte[] bytes)>
            {
                (FourCC("RECS"), 0u, 0u, 0u, recsBytes),
                (FourCC("META"), 0u, 0u, 0u, metaBytes),
                (FourCC("SHCT"), 1u, 0u, 0u, shctBytes),
                (FourCC("SHLB"), 1u, 0u, (uint)frameCount, shlbBytes),
                (FourCC("SHDL"), 1u, 0u, (uint)frameCount, shdlBytes),
            };

            // -----------------------------
            // 2) layout: header + section table + payload
            // -----------------------------
            var sectionCount = sects.Count;
            const ulong sectionTableOffset = 64;
            var sectionTableSize = 16 + sectionCount * 32;
            var dataOffset = (ulong)sectionTableOffset + (ulong)sectionTableSize;

            var sectionEntries = new (uint kind, uint band, uint startFrame, uint frameCount, ulong offset, ulong length)[sectionCount];
            var cur = dataOffset;
            for (var i = 0; i < sectionCount; i++)
            {
                var s = sects[i];
                sectionEntries[i] = (s.kind, s.band, s.startFrame, s.frameCount, cur, (ulong)s.bytes.Length);
                cur += (ulong)s.bytes.Length;
            }

            using var outMs = new MemoryStream();
            using var bwOut = new BinaryWriter(outMs);

            // v2 header(含 magic),总长度=64 bytes.
            bwOut.Write(new byte[] { (byte)'S', (byte)'P', (byte)'L', (byte)'4', (byte)'D', (byte)'V', (byte)'0', (byte)'2' });
            bwOut.Write(2u); // version
            bwOut.Write(64u); // headerSizeBytes(含 magic)
            bwOut.Write((uint)sectionCount);
            bwOut.Write((uint)recordSizeBytes);
            bwOut.Write((uint)splatCount);
            bwOut.Write((uint)shBands);
            bwOut.Write(1u); // timeModel=window
            bwOut.Write((uint)frameCount);
            bwOut.Write(sectionTableOffset);
            bwOut.Write(0ul); // reserved0
            bwOut.Write(0ul); // reserved1

            Assert.AreEqual(64, outMs.Length, "v2 header length must be 64 bytes");

            // section table
            bwOut.Write(new byte[] { (byte)'S', (byte)'E', (byte)'C', (byte)'T' });
            bwOut.Write(1u); // sectVersion
            bwOut.Write((uint)sectionCount);
            bwOut.Write(0u); // reserved

            for (var i = 0; i < sectionCount; i++)
            {
                var e = sectionEntries[i];
                bwOut.Write(e.kind);
                bwOut.Write(e.band);
                bwOut.Write(e.startFrame);
                bwOut.Write(e.frameCount);
                bwOut.Write(e.offset);
                bwOut.Write(e.length);
            }

            Assert.AreEqual((long)sectionTableOffset + sectionTableSize, outMs.Length, "section table size mismatch");

            // payload
            for (var i = 0; i < sectionCount; i++)
                bwOut.Write(sects[i].bytes);

            var fileBytes = outMs.ToArray();
            Assert.AreEqual((long)cur, fileBytes.Length, "final file length mismatch");
            return fileBytes;
        }

        // --------------------------------------------------------------------
        // 最小 `.splat4d v2` full-label 构造器:
        // - shBands=1
        // - labelsEncoding=full
        // - 单帧静态语义,不携带任何 SHDL/delta 段
        // --------------------------------------------------------------------
        static byte[] BuildMinimalSplat4DV2FullLabels()
        {
            const int splatCount = 1;
            const int recordSizeBytes = 64;
            const int shBands = 1;
            const int frameCount = 1;

            const int sh1CodebookCount = 2;
            const int sh1CoeffCount = 3;

            byte[] BuildRecsBytes()
            {
                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);

                // position(float3)
                bw.Write(0.0f);
                bw.Write(0.0f);
                bw.Write(0.0f);

                // scale(float3)
                bw.Write(1.0f);
                bw.Write(1.0f);
                bw.Write(1.0f);

                // color(bytes): 中性灰 + opacity=1
                bw.Write((byte)128);
                bw.Write((byte)128);
                bw.Write((byte)128);
                bw.Write((byte)255);

                // rotation(bytes): 约等于 identity
                bw.Write((byte)255);
                bw.Write((byte)128);
                bw.Write((byte)128);
                bw.Write((byte)128);

                // velocity/time/duration
                bw.Write(0.0f);
                bw.Write(0.0f);
                bw.Write(0.0f);
                bw.Write(0.0f);
                bw.Write(1.0f);

                // pad(float3)
                bw.Write(0.0f);
                bw.Write(0.0f);
                bw.Write(0.0f);

                var bytes = ms.ToArray();
                Assert.AreEqual(splatCount * recordSizeBytes, bytes.Length, "RECS bytes length mismatch");
                return bytes;
            }

            byte[] BuildMetaBytes()
            {
                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);

                bw.Write(1u); // metaVersion
                bw.Write(0.01f);
                bw.Write(0u); // deltaSegmentLength
                bw.Write(0u);

                // sh1 band info
                bw.Write((uint)sh1CodebookCount);
                bw.Write(2u); // centroidsType: f32
                bw.Write(1u); // labelsEncoding: full
                bw.Write(0u);

                // sh2 / sh3 不使用
                for (var i = 0; i < 8; i++)
                    bw.Write(0u);

                var bytes = ms.ToArray();
                Assert.AreEqual(64, bytes.Length, "META bytes length must be 64");
                return bytes;
            }

            byte[] BuildSh1CentroidsBytes()
            {
                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);

                // label 0
                bw.Write(1.0f); bw.Write(1.1f); bw.Write(1.2f);
                bw.Write(2.0f); bw.Write(2.1f); bw.Write(2.2f);
                bw.Write(3.0f); bw.Write(3.1f); bw.Write(3.2f);

                // label 1
                bw.Write(10.0f); bw.Write(10.1f); bw.Write(10.2f);
                bw.Write(20.0f); bw.Write(20.1f); bw.Write(20.2f);
                bw.Write(30.0f); bw.Write(30.1f); bw.Write(30.2f);

                var bytes = ms.ToArray();
                Assert.AreEqual(sh1CodebookCount * sh1CoeffCount * 3 * 4, bytes.Length, "SHCT bytes length mismatch");
                return bytes;
            }

            byte[] BuildSh1LabelsBytes()
            {
                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);
                bw.Write((ushort)1);
                var bytes = ms.ToArray();
                Assert.AreEqual(splatCount * 2, bytes.Length, "SHLB bytes length mismatch");
                return bytes;
            }

            var recsBytes = BuildRecsBytes();
            var metaBytes = BuildMetaBytes();
            var shctBytes = BuildSh1CentroidsBytes();
            var shlbBytes = BuildSh1LabelsBytes();

            var sects = new List<(uint kind, uint band, uint startFrame, uint frameCount, byte[] bytes)>
            {
                (FourCC("RECS"), 0u, 0u, 0u, recsBytes),
                (FourCC("META"), 0u, 0u, 0u, metaBytes),
                (FourCC("SHCT"), 1u, 0u, 0u, shctBytes),
                (FourCC("SHLB"), 1u, 0u, 0u, shlbBytes),
            };

            var sectionCount = sects.Count;
            const ulong sectionTableOffset = 64;
            var sectionTableSize = 16 + sectionCount * 32;
            var dataOffset = (ulong)sectionTableOffset + (ulong)sectionTableSize;

            var sectionEntries = new (uint kind, uint band, uint startFrame, uint frameCount, ulong offset, ulong length)[sectionCount];
            var cur = dataOffset;
            for (var i = 0; i < sectionCount; i++)
            {
                var s = sects[i];
                sectionEntries[i] = (s.kind, s.band, s.startFrame, s.frameCount, cur, (ulong)s.bytes.Length);
                cur += (ulong)s.bytes.Length;
            }

            using var outMs = new MemoryStream();
            using var bwOut = new BinaryWriter(outMs);

            bwOut.Write(new byte[] { (byte)'S', (byte)'P', (byte)'L', (byte)'4', (byte)'D', (byte)'V', (byte)'0', (byte)'2' });
            bwOut.Write(2u);
            bwOut.Write(64u);
            bwOut.Write((uint)sectionCount);
            bwOut.Write((uint)recordSizeBytes);
            bwOut.Write((uint)splatCount);
            bwOut.Write((uint)shBands);
            bwOut.Write(1u); // timeModel=window
            bwOut.Write((uint)frameCount);
            bwOut.Write(sectionTableOffset);
            bwOut.Write(0ul);
            bwOut.Write(0ul);

            Assert.AreEqual(64, outMs.Length, "v2 header length must be 64 bytes");

            bwOut.Write(new byte[] { (byte)'S', (byte)'E', (byte)'C', (byte)'T' });
            bwOut.Write(1u);
            bwOut.Write((uint)sectionCount);
            bwOut.Write(0u);

            for (var i = 0; i < sectionCount; i++)
            {
                var e = sectionEntries[i];
                bwOut.Write(e.kind);
                bwOut.Write(e.band);
                bwOut.Write(e.startFrame);
                bwOut.Write(e.frameCount);
                bwOut.Write(e.offset);
                bwOut.Write(e.length);
            }

            Assert.AreEqual((long)sectionTableOffset + sectionTableSize, outMs.Length, "section table size mismatch");

            for (var i = 0; i < sectionCount; i++)
                bwOut.Write(sects[i].bytes);

            var fileBytes = outMs.ToArray();
            Assert.AreEqual((long)cur, fileBytes.Length, "final file length mismatch");
            return fileBytes;
        }

        static ushort[][] DecodeDeltaV1SingleSegment(ushort[] baseLabels, byte[] deltaBytes, int frameCount)
        {
            // delta-v1 layout:
            // header(28B): magic(8) + version/start/count/splatCount/labelCount (5*u32)
            // body: (frameCount-1) blocks, each: updateCount(u32) + updateCount*(u32,u16,u16)
            Assert.IsNotNull(deltaBytes);
            Assert.GreaterOrEqual(deltaBytes.Length, 28);

            var span = deltaBytes.AsSpan();
            var version = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8, 4));
            var segStart = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12, 4));
            var segCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16, 4));
            Assert.AreEqual(1u, version, "delta version mismatch");
            Assert.AreEqual(0u, segStart, "segmentStartFrame mismatch");
            Assert.AreEqual((uint)frameCount, segCount, "segmentFrameCount mismatch");

            var labelsByFrame = new ushort[frameCount][];
            var current = (ushort[])baseLabels.Clone();
            labelsByFrame[0] = (ushort[])current.Clone();

            var p = 28;
            for (var f = 1; f < frameCount; f++)
            {
                Assert.LessOrEqual(p + 4, span.Length, "delta truncated while reading updateCount");
                var updateCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(p, 4));
                p += 4;

                for (var i = 0; i < updateCount; i++)
                {
                    Assert.LessOrEqual(p + 8, span.Length, "delta truncated while reading update");
                    var splatId = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(p, 4));
                    p += 4;
                    var newLabel = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(p, 2));
                    p += 2;
                    var reserved = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(p, 2));
                    p += 2;
                    Assert.AreEqual(0, reserved, "delta reserved field must be 0");
                    Assert.GreaterOrEqual(splatId, 0);
                    Assert.Less(splatId, current.Length);
                    current[splatId] = newLabel;
                }

                labelsByFrame[f] = (ushort[])current.Clone();
            }

            Assert.AreEqual(span.Length, p, "delta has trailing bytes");
            return labelsByFrame;
        }

        static byte[] BuildMinimalStaticSingleFrameV1()
        {
            // 目标:
            // - 构造最贴近工具导出结果的最小 raw v1 `.splat4d`.
            // - 保持 velocity=0,time=0,duration=1,验证 importer 会继续产出 canonical 4D arrays.
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // position(float3)
            bw.Write(1.25f);
            bw.Write(-0.5f);
            bw.Write(2.0f);

            // scale(float3)
            bw.Write(1.0f);
            bw.Write(2.0f);
            bw.Write(3.0f);

            // color(bytes): baseRgb=0.5 -> f_dc 近似 0, opacity=1
            bw.Write((byte)128);
            bw.Write((byte)128);
            bw.Write((byte)128);
            bw.Write((byte)255);

            // rotation(bytes): 约等于 identity
            bw.Write((byte)255);
            bw.Write((byte)128);
            bw.Write((byte)128);
            bw.Write((byte)128);

            // velocity(float3): 单帧静态语义
            bw.Write(0.0f);
            bw.Write(0.0f);
            bw.Write(0.0f);

            // time/duration(window)
            bw.Write(0.0f);
            bw.Write(1.0f);

            // pad(float3)
            bw.Write(0.0f);
            bw.Write(0.0f);
            bw.Write(0.0f);

            var bytes = ms.ToArray();
            Assert.AreEqual(64, bytes.Length, "raw v1 single-frame record must be 64 bytes");
            return bytes;
        }

        [Test]
        public void ImportV2_DeltaV1_PersistsSegments_AndDecodeMatchesExpected()
        {
            var bytes = BuildMinimalSplat4DV2DeltaV1();
            WriteBytesToAssetPath(k_TestAssetPath, bytes);

            AssetDatabase.ImportAsset(k_TestAssetPath, ImportAssetOptions.ForceUpdate);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_TestAssetPath);
            Assert.IsNotNull(prefab, "imported prefab is null");

            var renderer = prefab.GetComponent<GsplatRenderer>();
            Assert.IsNotNull(renderer, "prefab missing GsplatRenderer");

            var asset = renderer.GsplatAsset;
            Assert.IsNotNull(asset, "GsplatRenderer.GsplatAsset is null");

            Assert.AreEqual(1, asset.SHBands);
            Assert.AreEqual(3, asset.ShFrameCount);
            Assert.IsNotNull(asset.Sh1Centroids);
            Assert.AreEqual(12, asset.Sh1Centroids.Length, "sh1 centroids length mismatch");

            Assert.IsNotNull(asset.Sh1DeltaSegments);
            Assert.AreEqual(1, asset.Sh1DeltaSegments.Length, "segment count mismatch");

            var seg = asset.Sh1DeltaSegments[0];
            Assert.AreEqual(0, seg.StartFrame);
            Assert.AreEqual(3, seg.FrameCount);

            // base labels(frame0)
            var splatCount = checked((int)asset.SplatCount);
            var baseLabels = new ushort[splatCount];
            Buffer.BlockCopy(seg.BaseLabelsBytes, 0, baseLabels, 0, splatCount * 2);

            // delta header magic quick check
            Assert.GreaterOrEqual(seg.DeltaBytes.Length, 8);
            Assert.AreEqual((byte)'S', seg.DeltaBytes[0]);
            Assert.AreEqual((byte)'P', seg.DeltaBytes[1]);
            Assert.AreEqual((byte)'L', seg.DeltaBytes[2]);

            var labelsByFrame = DecodeDeltaV1SingleSegment(baseLabels, seg.DeltaBytes, frameCount: 3);

            // 期望 labels 序列:
            // - frame0: all 0
            // - frame1: splat3=1, splat5=2
            // - frame2: splat3=1, splat5=3
            Assert.AreEqual(0, labelsByFrame[0][3]);
            Assert.AreEqual(0, labelsByFrame[0][5]);

            Assert.AreEqual(1, labelsByFrame[1][3]);
            Assert.AreEqual(2, labelsByFrame[1][5]);

            Assert.AreEqual(1, labelsByFrame[2][3]);
            Assert.AreEqual(3, labelsByFrame[2][5]);
        }

        [Test]
        public void ImportV2_FullLabels_DoesNotExposeUsableDeltaSegments()
        {
            var bytes = BuildMinimalSplat4DV2FullLabels();
            WriteBytesToAssetPath(k_FullLabelSingleFrameAssetPath, bytes);

            AssetDatabase.ImportAsset(k_FullLabelSingleFrameAssetPath, ImportAssetOptions.ForceUpdate);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_FullLabelSingleFrameAssetPath);
            Assert.IsNotNull(prefab, "imported prefab is null");

            var renderer = prefab.GetComponent<GsplatRenderer>();
            Assert.IsNotNull(renderer, "prefab missing GsplatRenderer");

            var asset = renderer.GsplatAsset;
            Assert.IsNotNull(asset, "GsplatRenderer.GsplatAsset is null");

            Assert.AreEqual(1, asset.SHBands);
            Assert.AreEqual(0, asset.ShFrameCount, "full-label 资产不应初始化动态 SH frame 语义");

            var sh1DeltaSegmentCount = asset.Sh1DeltaSegments?.Length ?? 0;
            var sh2DeltaSegmentCount = asset.Sh2DeltaSegments?.Length ?? 0;
            var sh3DeltaSegmentCount = asset.Sh3DeltaSegments?.Length ?? 0;
            Assert.AreEqual(0, sh1DeltaSegmentCount, "full-label 资产不应暴露可用的 Sh1 delta segments");
            Assert.AreEqual(0, sh2DeltaSegmentCount, "full-label 资产不应暴露可用的 Sh2 delta segments");
            Assert.AreEqual(0, sh3DeltaSegmentCount, "full-label 资产不应暴露可用的 Sh3 delta segments");

            Assert.IsNotNull(asset.SHs);
            Assert.AreEqual(3, asset.SHs.Length, "band1 资产应解码 3 个 rest coeff");
            Assert.AreEqual(new Vector3(10.0f, 10.1f, 10.2f), asset.SHs[0]);
            Assert.AreEqual(new Vector3(20.0f, 20.1f, 20.2f), asset.SHs[1]);
            Assert.AreEqual(new Vector3(30.0f, 30.1f, 30.2f), asset.SHs[2]);
        }

        [Test]
        public void ImportV1_StaticSingleFrame4D_PreservesCanonicalArrays()
        {
            var bytes = BuildMinimalStaticSingleFrameV1();
            WriteBytesToAssetPath(k_StaticSingleFrameAssetPath, bytes);

            AssetDatabase.ImportAsset(k_StaticSingleFrameAssetPath, ImportAssetOptions.ForceUpdate);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_StaticSingleFrameAssetPath);
            Assert.IsNotNull(prefab, "imported prefab is null");

            var renderer = prefab.GetComponent<GsplatRenderer>();
            Assert.IsNotNull(renderer, "prefab missing GsplatRenderer");

            var asset = renderer.GsplatAsset;
            Assert.IsNotNull(asset, "GsplatRenderer.GsplatAsset is null");

            Assert.AreEqual(1u, asset.SplatCount, "单帧 raw v1 `.splat4d` 应导入为 1 个 splat");
            Assert.AreEqual(0, asset.SHBands, "raw v1 `.splat4d` 只承载 SH0");
            Assert.AreEqual(1, asset.TimeModel, "单帧工具导出的 raw v1 `.splat4d` 应保持 window 语义");

            Assert.IsNotNull(asset.Velocities, "Velocities 应存在");
            Assert.IsNotNull(asset.Times, "Times 应存在");
            Assert.IsNotNull(asset.Durations, "Durations 应存在");
            Assert.AreEqual(1, asset.Velocities.Length, "Velocities 长度应与 splatCount 一致");
            Assert.AreEqual(1, asset.Times.Length, "Times 长度应与 splatCount 一致");
            Assert.AreEqual(1, asset.Durations.Length, "Durations 长度应与 splatCount 一致");

            Assert.AreEqual(Vector3.zero, asset.Velocities[0], "单帧静态 `.splat4d` 的 velocity 应固定为 0");
            Assert.AreEqual(0.0f, asset.Times[0], 1e-6f, "单帧静态 `.splat4d` 的 time 应固定为 0");
            Assert.AreEqual(1.0f, asset.Durations[0], 1e-6f, "单帧静态 `.splat4d` 的 duration 应固定为 1");
            Assert.AreEqual(1.0f, asset.MaxDuration, 1e-6f, "window 模式下 MaxDuration 应反映单帧 duration");
        }

        [Test]
        public void ImportV1_StaticSingleFrame4D_RealFixturePlyThroughExporterAndImporter()
        {
            ExportFixturePlyToAssetPath(k_StaticSingleFrameFromFixtureAssetPath);

            AssetDatabase.ImportAsset(k_StaticSingleFrameFromFixtureAssetPath, ImportAssetOptions.ForceUpdate);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_StaticSingleFrameFromFixtureAssetPath);
            Assert.IsNotNull(prefab, "真实夹具导出的 `.splat4d` 导入后 main prefab 应该存在");

            var renderer = prefab.GetComponent<GsplatRenderer>();
            Assert.IsNotNull(renderer, "导入 prefab 应自动挂载 GsplatRenderer");

            var asset = renderer.GsplatAsset;
            Assert.IsNotNull(asset, "GsplatRenderer.GsplatAsset 不应为空");

            Assert.AreEqual(1u, asset.SplatCount, "真实单帧 `.ply` 夹具导出后应保持 1 个 splat");
            Assert.AreEqual(0, asset.SHBands, "当前 `.splat4d` exporter/export path 仍应保持 SH0-only");
            Assert.AreEqual(1, asset.TimeModel, "单帧 exporter 输出应继续走 window 语义");
            Assert.IsNotNull(asset.Velocities, "Velocities 应存在");
            Assert.IsNotNull(asset.Times, "Times 应存在");
            Assert.IsNotNull(asset.Durations, "Durations 应存在");
            Assert.AreEqual(1, asset.Velocities.Length, "Velocities 长度应与 splatCount 一致");
            Assert.AreEqual(1, asset.Times.Length, "Times 长度应与 splatCount 一致");
            Assert.AreEqual(1, asset.Durations.Length, "Durations 长度应与 splatCount 一致");
            Assert.AreEqual(Vector3.zero, asset.Velocities[0], "单帧 exporter 输出的 velocity 应固定为 0");
            Assert.AreEqual(0.0f, asset.Times[0], 1e-6f, "单帧 exporter 输出的 time 应固定为 0");
            Assert.AreEqual(1.0f, asset.Durations[0], 1e-6f, "单帧 exporter 输出的 duration 应固定为 1");
        }
    }
}
