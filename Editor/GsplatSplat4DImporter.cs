// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Gsplat.Editor
{
    // `.splat4d` 是为 4DGS 工作流准备的二进制格式.
    // 目标是比 PLY 更快导入,并更贴近类似 SplatVFX 的使用体验.
    //
    // 重要说明:
    // - 这是 Unity 插件(UPM 包)内的 importer,不能假设宿主 Unity 项目一定安装了 VFX Graph.
    // - 因此该 importer 的基础职责是: 生成可被 Gsplat 主后端渲染的 `GsplatAsset`.
    // - 如需 VFX Graph 的“一键预制体”体验,会在后续任务里通过可选宏隔离实现.
    // importer version bump:
    // - v2: v1 `.splat4d` 在遇到明显非归一化的 time/duration 时,支持自动识别为 gaussian(mu+sigma).
    // - 目的: 避免旧数据在 Unity 内被错误按 window 语义裁剪,导致观感退化成"薄层/稀疏".
    // - v3: `.splat4d v2` labelsEncoding=delta-v1 时,把 per-segment base labels + delta bytes 持久化到资产,供运行时应用.
    [ScriptedImporter(3, "splat4d")]
    public sealed class GsplatSplat4DImporter : ScriptedImporter
    {
        // 与 HLSL 侧 `SH_C0` 保持一致,用于把 baseRgb 还原回 f_dc 系数:
        // baseRgb = f_dc * SH_C0 + 0.5  =>  f_dc = (baseRgb - 0.5) / SH_C0
        const float k_shC0 = 0.28209479177387814f;

        const int k_recordSizeBytes = 64;

        // `.splat4d` v2 magic(headered).
        // - v1: 无 header,直接是 record 数组.
        // - v2: header + section table,用于承载 SH rest 与更准确的时间核语义.
        static readonly byte[] k_magicV2 = { (byte)'S', (byte)'P', (byte)'L', (byte)'4', (byte)'D', (byte)'V', (byte)'0', (byte)'2' };

        static uint FourCC(string code)
        {
            // 与 exporter(Python)侧 `struct.unpack("<I", b"ABCD")` 对齐:
            // value = b[0] + (b[1]<<8) + (b[2]<<16) + (b[3]<<24)
            if (code == null || code.Length != 4)
                throw new ArgumentException("fourcc must be 4 chars", nameof(code));
            return (uint)(byte)code[0] |
                   ((uint)(byte)code[1] << 8) |
                   ((uint)(byte)code[2] << 16) |
                   ((uint)(byte)code[3] << 24);
        }

        const uint k_sectRecs = 0x53434552; // "RECS"
        const uint k_sectMeta = 0x4154454D; // "META"
        const uint k_sectShCt = 0x54434853; // "SHCT"
        const uint k_sectShLb = 0x424C4853; // "SHLB"
        const uint k_sectShDl = 0x4C444853; // "SHDL"

        struct Splat4DV2Header
        {
            public uint version;
            public uint headerSizeBytes;
            public uint sectionCount;
            public uint recordSizeBytes;
            public uint splatCount;
            public uint shBands;
            public uint timeModel; // 1=window,2=gaussian
            public uint frameCount;
            public ulong sectionTableOffset;
        }

        struct Splat4DV2Section
        {
            public uint kind;
            public uint band; // 0 or 1..3
            public uint startFrame;
            public uint frameCount;
            public ulong offset;
            public ulong length;
        }

        struct Splat4DV2BandInfo
        {
            public uint codebookCount;
            public uint centroidsType; // 1=f16,2=f32
            public uint labelsEncoding; // 1=full,2=delta-v1
        }

        struct Splat4DV2MetaV1
        {
            public uint metaVersion;
            public float temporalGaussianCutoff;
            public uint deltaSegmentLength;
            public Splat4DV2BandInfo sh1;
            public Splat4DV2BandInfo sh2;
            public Splat4DV2BandInfo sh3;
        }

        static bool HasV2Magic(ReadOnlySpan<byte> head8)
        {
            if (head8.Length < 8) return false;
            for (var i = 0; i < 8; i++)
            {
                if (head8[i] != k_magicV2[i])
                    return false;
            }
            return true;
        }

        static float HalfToFloat(ushort h)
        {
            // IEEE 754 half -> float32
            // - 不依赖 System.Half,以提高 Unity 版本兼容性.
            var sign = (h >> 15) & 0x1;
            var exp = (h >> 10) & 0x1f;
            var mant = h & 0x3ff;

            if (exp == 0)
            {
                if (mant == 0)
                {
                    // +/- 0
                    var zeroBits = sign << 31;
                    return BitConverter.Int32BitsToSingle(zeroBits);
                }

                // subnormal: 归一化 mantissa
                while ((mant & 0x400) == 0)
                {
                    mant <<= 1;
                    exp -= 1;
                }
                exp += 1;
                mant &= 0x3ff;
            }
            else if (exp == 31)
            {
                // Inf/NaN
                var infNaNBits = (sign << 31) | (0xff << 23) | (mant << 13);
                return BitConverter.Int32BitsToSingle(infNaNBits);
            }

            exp = exp + (127 - 15);
            var bits = (sign << 31) | (exp << 23) | (mant << 13);
            return BitConverter.Int32BitsToSingle(bits);
        }

        // 注意: 该 struct 布局必须严格匹配 `.splat4d` 的 64 bytes record layout.
        // 这里选择按字段顺序声明,并用 Pack=1 保证不会被 C# 自动插入 padding.
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct Splat4DRecord
        {
            public float px, py, pz;
            public float sx, sy, sz;
            public byte r, g, b, a;
            public byte rw, rx, ry, rz;
            public float vx, vy, vz;
            public float time;
            public float duration;
            public float pad0, pad1, pad2;
        }

#if GSPLAT_ENABLE_VFX_GRAPH
        // SplatVFX 风格: 导入后自动生成 prefab + VisualEffect + binder.
        // - VFX Graph asset 放在 Samples~ 中,但 Unity 的 Sample import 会把它拷贝到 `Assets/Samples/...`.
        //   因此这里需要同时尝试:
        //   1) 包内路径(开发时/某些工程布局下可用)
        //   2) `Assets/Samples/...` 下的查找(用户从 Package Manager 导入 sample 后的真实落点)
        const string k_defaultVfxAssetPath = GsplatUtils.k_PackagePath + "Samples~/VFXGraphSample/VFX/Splat.vfx";
        const string k_defaultVfxSortedAssetPath = GsplatUtils.k_PackagePath + "Samples~/VFXGraphSample/VFX/SplatSorted.vfx";

        // VFX 后端辅助 compute shader(用于生成 AxisBuffer/动态 buffers).
        const string k_vfxComputeShaderPath = GsplatUtils.k_PackagePath + "Runtime/Shaders/GsplatVfx.compute";

        static UnityEngine.VFX.VisualEffectAsset TryLoadDefaultVfxAsset()
        {
            // 1) 优先包内路径(在某些工程/开发布局下可能可直接访问).
            var a = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(k_defaultVfxSortedAssetPath);
            if (a != null)
                return a;
            a = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(k_defaultVfxAssetPath);
            if (a != null)
                return a;

            // 2) 次选: Unity sample 实际导入落点.
            // - 只搜索 `Assets/Samples`,避免全工程扫描带来的 import 性能回退.
            // - 以 path suffix 过滤,防止同名资源误命中.
            if (!AssetDatabase.IsValidFolder("Assets/Samples"))
                return null;

            var sampleFolders = new[] { "Assets/Samples" };
            a = FindVfxAssetInFolders("SplatSorted", "/VFX/SplatSorted.vfx", sampleFolders);
            if (a != null)
                return a;
            a = FindVfxAssetInFolders("Splat", "/VFX/Splat.vfx", sampleFolders);
            return a;
        }

        static UnityEngine.VFX.VisualEffectAsset FindVfxAssetInFolders(string nameHint, string pathSuffix, string[] searchFolders)
        {
            var query = $"t:VisualEffectAsset {nameHint}";
            var guids = AssetDatabase.FindAssets(query, searchFolders);
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                    continue;

                // 统一分隔符,并用 suffix 过滤.
                var normalized = path.Replace('\\', '/');
                if (!normalized.EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                return AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(path);
            }

            return null;
        }
#endif

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var gsplatAsset = ScriptableObject.CreateInstance<GsplatAsset>();
            gsplatAsset.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            var bounds = new Bounds();

            // C# 数组与 ReadAllBytes 都不太友好地支持超大文件,这里保持与 PLY importer 一致的上限.
            using (var fs = new FileStream(ctx.assetPath, FileMode.Open, FileAccess.Read))
            {
                if (fs.Length >= 2 * 1024 * 1024 * 1024L)
                {
                    if (GsplatSettings.Instance.ShowImportErrors)
                        Debug.LogError($"{ctx.assetPath} import error: currently files larger than 2GB are not supported");
                    return;
                }
                // 读 magic 选择 v1/v2.
                if (fs.Length == 0)
                {
                    if (GsplatSettings.Instance.ShowImportErrors)
                        Debug.LogError($"{ctx.assetPath} import error: empty .splat4d file");
                    return;
                }

                var head8 = new byte[8];
                var nHead = fs.Read(head8, 0, head8.Length);
                fs.Seek(0, SeekOrigin.Begin);

                if (nHead == 8 && HasV2Magic(head8))
                {
                    if (!TryImportV2(ctx, gsplatAsset, fs, ref bounds))
                        return;
                }
                else
                {
                    // v1: legacy(无 header)
                    var bytes = File.ReadAllBytes(ctx.assetPath);
                    if (bytes.Length % k_recordSizeBytes != 0)
                    {
                        if (GsplatSettings.Instance.ShowImportErrors)
                            Debug.LogError(
                                $"{ctx.assetPath} import error: invalid .splat4d byte length {bytes.Length}, must be multiple of {k_recordSizeBytes}");
                        return;
                    }

                    // `.splat4d` 是无 header 的 record 数组.
                    var source = MemoryMarshal.Cast<byte, Splat4DRecord>(bytes.AsSpan());
                    var count = source.Length;
                    gsplatAsset.SplatCount = (uint)count;

                    // v1: 只承载 SH0(DC) 与 opacity,不承载高阶 SH.
                    gsplatAsset.SHBands = 0;
                    gsplatAsset.SHs = null;

                    // v1: legacy(无 header),无法明确标注时间核语义.
                    // - 正常情况下它应当是 window(time0+duration),且 time/duration 都是归一化到 [0,1] 的.
                    // - 但实践里我们也会遇到 "仍是 v1 layout,但 time/duration 更像 gaussian(mu+sigma)" 的旧数据.
                    //   若强行按 window 语义渲染,会把可见 splat 压成一个时间切片,看起来就像"薄层/稀疏".
                    // 因此这里先按 window 假设初始化,稍后会基于统计做一次轻量自动识别.
                    gsplatAsset.TimeModel = 1;
                    gsplatAsset.TemporalGaussianCutoff = 0.01f;

                    gsplatAsset.Positions = new Vector3[count];
                    gsplatAsset.Scales = new Vector3[count];
                    gsplatAsset.Rotations = new Vector4[count];
                    gsplatAsset.Colors = new Vector4[count];

                    gsplatAsset.Velocities = new Vector3[count];
                    gsplatAsset.Times = new float[count];
                    gsplatAsset.Durations = new float[count];

                    // clamp 统计: 只要发生过 clamp,就输出一次 warning,并包含统计信息.
                    var clamped = false;
                    var minTime = float.PositiveInfinity;
                    var maxTime = float.NegativeInfinity;
                    var minDuration = float.PositiveInfinity;
                    var maxDuration = float.NegativeInfinity;
                    var maxSpeed = 0.0f;

                    // v1 自动识别辅助统计:
                    // - 如果 time/duration 大量越界,基本可以判定不是 "window+归一化" 的数据.
                    // - 这种情况下,把它当 gaussian(mu+sigma) 解析更符合直觉,也更接近常见训练 checkpoint 的时间核定义.
                    var outOfRangeTimeCount = 0;
                    var outOfRangeDurationCount = 0;

                    for (var i = 0; i < count; i++)
                    {
                        var src = source[i];

                        // 位置/尺度
                        var pos = new Vector3(src.px, src.py, src.pz);
                        gsplatAsset.Positions[i] = pos;
                        gsplatAsset.Scales[i] = new Vector3(src.sx, src.sy, src.sz);

                        // 旋转: bytes -> [-1,1] -> normalize -> 存储为 float4(w,x,y,z)
                        var qw = (src.rw - 128) / 128.0f;
                        var qx = (src.rx - 128) / 128.0f;
                        var qy = (src.ry - 128) / 128.0f;
                        var qz = (src.rz - 128) / 128.0f;
                        var q = new Vector4(qw, qx, qy, qz);
                        var qLen = q.magnitude;
                        if (qLen < 1e-8f || float.IsNaN(qLen) || float.IsInfinity(qLen))
                        {
                            clamped = true;
                            q = new Vector4(1, 0, 0, 0);
                        }
                        else
                        {
                            q /= qLen;
                        }

                        gsplatAsset.Rotations[i] = q;

                        // 颜色: r/g/b/a 是 baseRgb + opacity,需要还原成 f_dc + opacity
                        var baseR = src.r / 255.0f;
                        var baseG = src.g / 255.0f;
                        var baseB = src.b / 255.0f;
                        var opacity = src.a / 255.0f;
                        var fdcR = (baseR - 0.5f) / k_shC0;
                        var fdcG = (baseG - 0.5f) / k_shC0;
                        var fdcB = (baseB - 0.5f) / k_shC0;
                        gsplatAsset.Colors[i] = new Vector4(fdcR, fdcG, fdcB, opacity);

                        // 4D 字段: velocity/time/duration
                        var vel = new Vector3(src.vx, src.vy, src.vz);
                        if (float.IsNaN(vel.x) || float.IsInfinity(vel.x)) { vel.x = 0.0f; clamped = true; }
                        if (float.IsNaN(vel.y) || float.IsInfinity(vel.y)) { vel.y = 0.0f; clamped = true; }
                        if (float.IsNaN(vel.z) || float.IsInfinity(vel.z)) { vel.z = 0.0f; clamped = true; }

                        var t0 = src.time;
                        var dt = src.duration;
                        if (float.IsNaN(t0) || float.IsInfinity(t0)) { t0 = 0.0f; clamped = true; }
                        if (float.IsNaN(dt) || float.IsInfinity(dt)) { dt = 0.0f; clamped = true; }

                        minTime = Mathf.Min(minTime, t0);
                        maxTime = Mathf.Max(maxTime, t0);
                        minDuration = Mathf.Min(minDuration, dt);
                        maxDuration = Mathf.Max(maxDuration, dt);

                        gsplatAsset.Velocities[i] = vel;
                        gsplatAsset.Times[i] = t0;
                        gsplatAsset.Durations[i] = dt;

                        maxSpeed = Mathf.Max(maxSpeed, vel.magnitude);
                        if (t0 < 0.0f || t0 > 1.0f)
                            outOfRangeTimeCount++;
                        if (dt < 0.0f || dt > 1.0f)
                            outOfRangeDurationCount++;

                        // bounds
                        if (i == 0) bounds = new Bounds(pos, Vector3.zero);
                        else bounds.Encapsulate(pos);

                        if ((i & 0x3fff) == 0)
                        {
                            EditorUtility.DisplayProgressBar("Importing Gsplat Asset", "Reading .splat4d records",
                                i / (float)count);
                        }
                    }

                    gsplatAsset.Bounds = bounds;
                    gsplatAsset.MaxSpeed = maxSpeed;

                    // v1 时间核自动识别:
                    // - 如果越界比例足够高,我们认为该 v1 文件更像 gaussian(mu+sigma).
                    // - 否则保持 v1 传统 window(time0+duration)语义,并对 time/duration 做 clamp 到 [0,1].
                    var timeOorRatio = outOfRangeTimeCount / (float)count;
                    var durationOorRatio = outOfRangeDurationCount / (float)count;
                    var assumeGaussian = timeOorRatio > 0.01f || durationOorRatio > 0.01f;

                    if (assumeGaussian)
                    {
                        gsplatAsset.TimeModel = 2;

                        // gaussian bounds: 用 cutoff 把 sigma 映射成一个“可见半宽”,用于 motion padding.
                        // halfWidthFactor = sqrt(-2*ln(cutoff))
                        var cutoff = gsplatAsset.TemporalGaussianCutoff;
                        if (float.IsNaN(cutoff) || float.IsInfinity(cutoff) || cutoff <= 0.0f || cutoff >= 1.0f)
                            cutoff = 0.01f;
                        gsplatAsset.TemporalGaussianCutoff = cutoff;
                        var halfWidthFactor = Mathf.Sqrt(-2.0f * Mathf.Log(cutoff));

                        var maxDurationForBounds = 0.0f;
                        for (var i = 0; i < count; i++)
                        {
                            var sigma = gsplatAsset.Durations[i];
                            if (float.IsNaN(sigma) || float.IsInfinity(sigma) || sigma < 1e-6f)
                            {
                                sigma = 1e-6f;
                                clamped = true;
                            }

                            gsplatAsset.Durations[i] = sigma;
                            maxDurationForBounds = Mathf.Max(maxDurationForBounds, halfWidthFactor * sigma);
                        }

                        gsplatAsset.MaxDuration = maxDurationForBounds;

                        if (GsplatSettings.Instance.ShowImportErrors)
                        {
                            Debug.LogWarning(
                                $"{ctx.assetPath} import warning: legacy v1 `.splat4d` looks like gaussian(mu+sigma). " +
                                $"auto set timeModel=2. " +
                                $"time(min={minTime}, max={maxTime}), duration(min={minDuration}, max={maxDuration}), " +
                                $"outOfRangeTimeRatio={timeOorRatio:P2}, outOfRangeDurationRatio={durationOorRatio:P2}");
                        }
                    }
                    else
                    {
                        var maxDurationClamped = 0.0f;
                        for (var i = 0; i < count; i++)
                        {
                            var t0 = gsplatAsset.Times[i];
                            var dt = gsplatAsset.Durations[i];
                            var t0Clamped = Mathf.Clamp01(t0);
                            var dtClamped = Mathf.Clamp01(dt);
                            if (t0Clamped != t0 || dtClamped != dt)
                                clamped = true;

                            gsplatAsset.Times[i] = t0Clamped;
                            gsplatAsset.Durations[i] = dtClamped;
                            maxDurationClamped = Mathf.Max(maxDurationClamped, dtClamped);
                        }

                        gsplatAsset.MaxDuration = maxDurationClamped;
                    }

                    if (clamped && GsplatSettings.Instance.ShowImportErrors)
                    {
                        Debug.LogWarning(
                            $"{ctx.assetPath} import warning: sanitized/clamped values. " +
                            $"time(min={minTime}, max={maxTime}), duration(min={minDuration}, max={maxDuration}), timeModel={gsplatAsset.TimeModel}");
                    }

                    EditorUtility.ClearProgressBar();
                }
            }

            ctx.AddObjectToAsset("gsplatAsset", gsplatAsset);

            // ----------------------------------------------------------------
            // 一键可用: 自动生成一个 prefab,挂上 GsplatRenderer(主后端)与可选的 VFX 组件.
            // - 没装 VFX Graph 时: 只有 GsplatRenderer,依旧可直接播放.
            // - 装了 VFX Graph 时: 额外挂 VisualEffect + VFXPropertyBinder + GsplatVfxBinder.
            // ----------------------------------------------------------------
            var prefab = new GameObject(gsplatAsset.name);
            var renderer = prefab.AddComponent<GsplatRenderer>();
            renderer.GsplatAsset = gsplatAsset;

#if GSPLAT_ENABLE_VFX_GRAPH
            // VFX Graph 是可选依赖. 当安装了包时,我们尽量提供 SplatVFX 风格的 prefab 体验.
            // 优先选择 "质量优先" 的 sorted 变体:
            // - 它在 VFX Output 上启用了 sorting,遮挡关系更接近 Gsplat 主后端.
            // - 如果宿主项目/旧版本包中不存在该资产,则回退到原 `Splat.vfx`.
            var vfxAsset = TryLoadDefaultVfxAsset();
            var vfxCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(k_vfxComputeShaderPath);

            if (vfxAsset != null && vfxCompute != null)
            {
                var vfx = prefab.AddComponent<UnityEngine.VFX.VisualEffect>();
                vfx.visualEffectAsset = vfxAsset;

                var binderBase = prefab.AddComponent<UnityEngine.VFX.Utility.VFXPropertyBinder>();
                var binder = binderBase.AddPropertyBinder<Gsplat.VFX.GsplatVfxBinder>();
                binder.GsplatRenderer = renderer;
                binder.VfxComputeShader = vfxCompute;

                // 默认避免双重渲染: 当 VFX Graph asset 存在时,优先让 VFX 后端负责可视化.
                renderer.EnableGsplatBackend = false;
            }
            else if (vfxAsset != null)
            {
                Debug.LogWarning(
                    $"[Gsplat][VFX] 找到了默认 VFX Graph asset,但缺少 compute shader: {k_vfxComputeShaderPath}. " +
                    "将回退使用 Gsplat 主后端渲染. 请确认包内容完整,或手动在 GsplatVfxBinder 上指定 compute shader.");
            }
            else
            {
                // Samples~ 默认不会被导入为 Asset,用户需要在 Package Manager 里点 Import Sample.
                Debug.LogWarning(
                    $"[Gsplat][VFX] 未找到默认 VFX Graph asset: {k_defaultVfxSortedAssetPath} 或 {k_defaultVfxAssetPath}. " +
                    "并且在 `Assets/Samples/**/VFX/` 下也未搜索到 `SplatSorted.vfx`/`Splat.vfx`. " +
                    "你仍可使用 Gsplat 主后端渲染. 若需要 VFX 后端,请导入本包 Samples~/VFXGraphSample,或手动指定 VisualEffectAsset.");
            }
#endif

            ctx.AddObjectToAsset("prefab", prefab);
            ctx.SetMainObject(prefab);
        }

        static bool TryImportV2(AssetImportContext ctx, GsplatAsset gsplatAsset, FileStream fs, ref Bounds bounds)
        {
            bool Fail(string message)
            {
                if (GsplatSettings.Instance.ShowImportErrors)
                    Debug.LogError($"{ctx.assetPath} import error: {message}");
                EditorUtility.ClearProgressBar();
                return false;
            }

            try
            {
                // -----------------------------
                // 1) 读取 header
                // -----------------------------
                fs.Seek(0, SeekOrigin.Begin);
                var br = new BinaryReader(fs);

                var magic = br.ReadBytes(8);
                if (magic.Length != 8 || !HasV2Magic(magic))
                    return Fail("invalid v2 magic (expected SPL4DV02)");

                var header = new Splat4DV2Header
                {
                    version = br.ReadUInt32(),
                    headerSizeBytes = br.ReadUInt32(),
                    sectionCount = br.ReadUInt32(),
                    recordSizeBytes = br.ReadUInt32(),
                    splatCount = br.ReadUInt32(),
                    shBands = br.ReadUInt32(),
                    timeModel = br.ReadUInt32(),
                    frameCount = br.ReadUInt32(),
                    sectionTableOffset = br.ReadUInt64(),
                };
                // reserved0/reserved1
                br.ReadUInt64();
                br.ReadUInt64();

                if (header.version != 2)
                    return Fail($"invalid v2 header.version={header.version} (expected 2)");
                if (header.headerSizeBytes != 64)
                    return Fail($"invalid v2 header.headerSizeBytes={header.headerSizeBytes} (expected 64)");
                if (header.recordSizeBytes != 64)
                    return Fail($"invalid v2 header.recordSizeBytes={header.recordSizeBytes} (expected 64)");
                if (header.splatCount == 0)
                    return Fail("invalid v2 header.splatCount=0");
                if (header.shBands > 3)
                    return Fail($"invalid v2 header.shBands={header.shBands} (must be 0..3)");
                if (header.timeModel != 1 && header.timeModel != 2)
                    return Fail($"invalid v2 header.timeModel={header.timeModel} (must be 1 or 2)");
                if (header.sectionTableOffset >= (ulong)fs.Length)
                    return Fail($"invalid v2 header.sectionTableOffset={header.sectionTableOffset} (fileLength={fs.Length})");

                // -----------------------------
                // 2) 读取 section table
                // -----------------------------
                fs.Seek((long)header.sectionTableOffset, SeekOrigin.Begin);
                var sectMagic = br.ReadBytes(4);
                if (sectMagic.Length != 4 || sectMagic[0] != (byte)'S' || sectMagic[1] != (byte)'E' || sectMagic[2] != (byte)'C' || sectMagic[3] != (byte)'T')
                    return Fail("invalid v2 section table magic (expected SECT)");

                var sectVersion = br.ReadUInt32();
                var sectCount = br.ReadUInt32();
                br.ReadUInt32(); // reserved
                if (sectVersion != 1)
                    return Fail($"invalid v2 section table version={sectVersion} (expected 1)");
                if (sectCount != header.sectionCount)
                    return Fail($"invalid v2 sectionCount mismatch: header={header.sectionCount}, table={sectCount}");
                if (sectCount == 0)
                    return Fail("invalid v2 sectionCount=0");

                var sections = new Splat4DV2Section[sectCount];
                for (var i = 0; i < sectCount; i++)
                {
                    sections[i] = new Splat4DV2Section
                    {
                        kind = br.ReadUInt32(),
                        band = br.ReadUInt32(),
                        startFrame = br.ReadUInt32(),
                        frameCount = br.ReadUInt32(),
                        offset = br.ReadUInt64(),
                        length = br.ReadUInt64(),
                    };
                }

                bool ValidateRange(Splat4DV2Section s, string debugName)
                {
                    var end = s.offset + s.length;
                    if (end > (ulong)fs.Length)
                        return Fail($"{debugName} out of file range: offset={s.offset}, length={s.length}, fileLength={fs.Length}");
                    return true;
                }

                Splat4DV2Section? FindSingle(uint kind, uint band)
                {
                    for (var i = 0; i < sections.Length; i++)
                    {
                        var s = sections[i];
                        if (s.kind == kind && s.band == band)
                            return s;
                    }
                    return null;
                }

                var recsOpt = FindSingle(k_sectRecs, band: 0);
                if (recsOpt == null)
                    return Fail("v2 missing required section: RECS");
                var recs = recsOpt.Value;
                if (!ValidateRange(recs, "RECS")) return false;
                var expectedRecsBytes = (ulong)header.splatCount * (ulong)header.recordSizeBytes;
                if (recs.length != expectedRecsBytes)
                    return Fail($"invalid RECS.length={recs.length}, expected {expectedRecsBytes}");

                var metaOpt = FindSingle(k_sectMeta, band: 0);
                if (metaOpt == null)
                    return Fail("v2 missing required section: META");
                var metaSection = metaOpt.Value;
                if (!ValidateRange(metaSection, "META")) return false;
                if (metaSection.length != 64)
                    return Fail($"invalid META.length={metaSection.length} (expected 64)");

                // -----------------------------
                // 3) 读取 META
                // -----------------------------
                fs.Seek((long)metaSection.offset, SeekOrigin.Begin);
                var meta = new Splat4DV2MetaV1
                {
                    metaVersion = br.ReadUInt32(),
                    temporalGaussianCutoff = br.ReadSingle(),
                    deltaSegmentLength = br.ReadUInt32(),
                };
                br.ReadUInt32(); // reserved0
                meta.sh1 = new Splat4DV2BandInfo
                {
                    codebookCount = br.ReadUInt32(),
                    centroidsType = br.ReadUInt32(),
                    labelsEncoding = br.ReadUInt32(),
                };
                br.ReadUInt32(); // reserved
                meta.sh2 = new Splat4DV2BandInfo
                {
                    codebookCount = br.ReadUInt32(),
                    centroidsType = br.ReadUInt32(),
                    labelsEncoding = br.ReadUInt32(),
                };
                br.ReadUInt32(); // reserved
                meta.sh3 = new Splat4DV2BandInfo
                {
                    codebookCount = br.ReadUInt32(),
                    centroidsType = br.ReadUInt32(),
                    labelsEncoding = br.ReadUInt32(),
                };
                br.ReadUInt32(); // reserved

                if (meta.metaVersion != 1)
                    return Fail($"invalid META.metaVersion={meta.metaVersion} (expected 1)");

                // 时间核配置写入资产(兼容旧资产: 0 视为 window)
                gsplatAsset.TimeModel = (byte)(header.timeModel == 2 ? 2 : 1);
                var cutoff = meta.temporalGaussianCutoff;
                if (float.IsNaN(cutoff) || float.IsInfinity(cutoff) || cutoff <= 0.0f || cutoff >= 1.0f)
                    cutoff = 0.01f;
                gsplatAsset.TemporalGaussianCutoff = cutoff;

                // -----------------------------
                // 4) 读取 base records
                // -----------------------------
                var count = checked((int)header.splatCount);
                gsplatAsset.SplatCount = header.splatCount;

                gsplatAsset.Positions = new Vector3[count];
                gsplatAsset.Scales = new Vector3[count];
                gsplatAsset.Rotations = new Vector4[count];
                gsplatAsset.Colors = new Vector4[count];

                gsplatAsset.Velocities = new Vector3[count];
                gsplatAsset.Times = new float[count];
                gsplatAsset.Durations = new float[count];

                // SH rest(可选)
                var shBands = (byte)header.shBands;
                gsplatAsset.SHBands = shBands;
                if (shBands > 0)
                {
                    var restCoeffCount = GsplatUtils.SHBandsToCoefficientCount(shBands);
                    gsplatAsset.SHs = new Vector3[restCoeffCount * count];
                }
                else
                {
                    gsplatAsset.SHs = null;
                }

                // clamp 统计: 只要发生过 clamp,就输出一次 warning,并包含统计信息.
                var clamped = false;
                var minTime = float.PositiveInfinity;
                var maxTime = float.NegativeInfinity;
                var minDuration = float.PositiveInfinity;
                var maxDuration = float.NegativeInfinity;
                var maxSpeed = 0.0f;
                var maxDurationForBounds = 0.0f;

                // gaussian bounds: 用 cutoff 把 sigma 映射成一个“可见半宽”.
                // halfWidthFactor = sqrt(-2*ln(cutoff)), windowLength = 2*halfWidthFactor*sigma
                var halfWidthFactor = 0.0f;
                if (gsplatAsset.TimeModel == 2)
                    halfWidthFactor = Mathf.Sqrt(-2.0f * Mathf.Log(gsplatAsset.TemporalGaussianCutoff));

                fs.Seek((long)recs.offset, SeekOrigin.Begin);

                // chunk 读取,避免一次性读入超大 byte[]
                const int chunkRecords = 16384;
                var chunkBytes = chunkRecords * k_recordSizeBytes;
                var buffer = new byte[chunkBytes];

                var readIndex = 0;
                while (readIndex < count)
                {
                    var remain = count - readIndex;
                    var wantRecords = Mathf.Min(chunkRecords, remain);
                    var wantBytes = wantRecords * k_recordSizeBytes;

                    var got = 0;
                    while (got < wantBytes)
                    {
                        var n = fs.Read(buffer, got, wantBytes - got);
                        if (n <= 0)
                            return Fail("unexpected EOF while reading RECS");
                        got += n;
                    }

                    var span = buffer.AsSpan(0, wantBytes);
                    var records = MemoryMarshal.Cast<byte, Splat4DRecord>(span);
                    for (var j = 0; j < records.Length; j++)
                    {
                        var i = readIndex + j;
                        var src = records[j];

                        // 位置/尺度
                        var pos = new Vector3(src.px, src.py, src.pz);
                        gsplatAsset.Positions[i] = pos;
                        gsplatAsset.Scales[i] = new Vector3(src.sx, src.sy, src.sz);

                        // 旋转: bytes -> [-1,1] -> normalize -> 存储为 float4(w,x,y,z)
                        var qw = (src.rw - 128) / 128.0f;
                        var qx = (src.rx - 128) / 128.0f;
                        var qy = (src.ry - 128) / 128.0f;
                        var qz = (src.rz - 128) / 128.0f;
                        var q = new Vector4(qw, qx, qy, qz);
                        var qLen = q.magnitude;
                        if (qLen < 1e-8f || float.IsNaN(qLen) || float.IsInfinity(qLen))
                        {
                            clamped = true;
                            q = new Vector4(1, 0, 0, 0);
                        }
                        else
                        {
                            q /= qLen;
                        }
                        gsplatAsset.Rotations[i] = q;

                        // 颜色: r/g/b/a 是 baseRgb + opacity,需要还原成 f_dc + opacity
                        var baseR = src.r / 255.0f;
                        var baseG = src.g / 255.0f;
                        var baseB = src.b / 255.0f;
                        var opacity = src.a / 255.0f;
                        var fdcR = (baseR - 0.5f) / k_shC0;
                        var fdcG = (baseG - 0.5f) / k_shC0;
                        var fdcB = (baseB - 0.5f) / k_shC0;
                        gsplatAsset.Colors[i] = new Vector4(fdcR, fdcG, fdcB, opacity);

                        // 4D 字段: velocity/time/duration
                        var vel = new Vector3(src.vx, src.vy, src.vz);
                        if (float.IsNaN(vel.x) || float.IsInfinity(vel.x)) { vel.x = 0.0f; clamped = true; }
                        if (float.IsNaN(vel.y) || float.IsInfinity(vel.y)) { vel.y = 0.0f; clamped = true; }
                        if (float.IsNaN(vel.z) || float.IsInfinity(vel.z)) { vel.z = 0.0f; clamped = true; }
                        gsplatAsset.Velocities[i] = vel;

                        var t0 = src.time;
                        var dt = src.duration;
                        if (float.IsNaN(t0) || float.IsInfinity(t0)) { t0 = 0.0f; clamped = true; }
                        if (float.IsNaN(dt) || float.IsInfinity(dt)) { dt = 0.0f; clamped = true; }

                        minTime = Mathf.Min(minTime, t0);
                        maxTime = Mathf.Max(maxTime, t0);
                        minDuration = Mathf.Min(minDuration, dt);
                        maxDuration = Mathf.Max(maxDuration, dt);

                        // window/gaussian:
                        // - window: clamp 到 [0,1]
                        // - gaussian: time=mu 不做 clamp(允许超出 [0,1]), duration=sigma 只做下限保护
                        float t0Out;
                        float dtOut;
                        if (gsplatAsset.TimeModel == 2)
                        {
                            t0Out = t0;
                            dtOut = dt;
                            if (dtOut < 1e-6f) { dtOut = 1e-6f; clamped = true; }
                        }
                        else
                        {
                            t0Out = Mathf.Clamp01(t0);
                            dtOut = Mathf.Clamp01(dt);
                            if (t0Out != t0 || dtOut != dt)
                                clamped = true;
                        }

                        gsplatAsset.Times[i] = t0Out;
                        gsplatAsset.Durations[i] = dtOut;

                        // motion 统计:
                        maxSpeed = Mathf.Max(maxSpeed, vel.magnitude);
                        if (gsplatAsset.TimeModel == 2)
                        {
                            // 用 cutoff 推导“可见半宽”,用于 bounds padding.
                            // 说明:
                            // - gaussian visible region: |t - mu| <= halfWidthFactor*sigma
                            // - pos(t) = pos(mu) + vel*(t - mu),最大位移由 halfWidth 决定,不是 windowLength(2*halfWidth)
                            maxDurationForBounds = Mathf.Max(maxDurationForBounds, halfWidthFactor * dtOut);
                        }
                        else
                        {
                            maxDurationForBounds = Mathf.Max(maxDurationForBounds, dtOut);
                        }

                        // bounds(基于 canonical pos0)
                        if (i == 0) bounds = new Bounds(pos, Vector3.zero);
                        else bounds.Encapsulate(pos);

                        if ((i & 0x3fff) == 0)
                        {
                            EditorUtility.DisplayProgressBar("Importing Gsplat Asset", "Reading .splat4d v2 records",
                                i / (float)count);
                        }
                    }

                    readIndex += records.Length;
                }

                gsplatAsset.Bounds = bounds;
                gsplatAsset.MaxSpeed = maxSpeed;
                gsplatAsset.MaxDuration = maxDurationForBounds;

                if (clamped && GsplatSettings.Instance.ShowImportErrors)
                {
                    Debug.LogWarning(
                        $"{ctx.assetPath} import warning: clamped time/duration. " +
                        $"time(min={minTime}, max={maxTime}), duration(min={minDuration}, max={maxDuration}), timeModel={gsplatAsset.TimeModel}");
                }

                // -----------------------------
                // 5) SH rest(per-band)解码(可选)
                // -----------------------------
                if (shBands > 0)
                {
                    if (!TryDecodeShBandsFromV2(ctx, fs, br, header, meta, sections, gsplatAsset))
                        return false;
                }

                EditorUtility.ClearProgressBar();
                return true;
            }
            catch (Exception e)
            {
                return Fail($"exception while importing v2: {e.Message}");
            }
        }

        static bool TryDecodeShBandsFromV2(AssetImportContext ctx, FileStream fs, BinaryReader br,
            Splat4DV2Header header, Splat4DV2MetaV1 meta, Splat4DV2Section[] sections, GsplatAsset asset)
        {
            bool Fail(string message)
            {
                if (GsplatSettings.Instance.ShowImportErrors)
                    Debug.LogError($"{ctx.assetPath} import error: {message}");
                EditorUtility.ClearProgressBar();
                return false;
            }

            var splatCount = checked((int)header.splatCount);
            var shBands = (int)header.shBands;
            var restCoeffCountTotal = GsplatUtils.SHBandsToCoefficientCount((byte)shBands);

            // 避免旧字段残留:
            // - 同一路径下资产在 reimport 时会复用同一个 ScriptableObject 实例,
            //   如果不清空,可能出现“新文件没带 delta,但资产还残留旧 delta”的混乱状态.
            asset.ShFrameCount = 0;
            asset.Sh1Centroids = null;
            asset.Sh2Centroids = null;
            asset.Sh3Centroids = null;
            asset.Sh1DeltaSegments = null;
            asset.Sh2DeltaSegments = null;
            asset.Sh3DeltaSegments = null;

            Splat4DV2BandInfo GetBandInfo(int band)
            {
                return band switch
                {
                    1 => meta.sh1,
                    2 => meta.sh2,
                    3 => meta.sh3,
                    _ => default
                };
            }

            int BandCoeffCount(int band) => band switch
            {
                1 => 3,
                2 => 5,
                3 => 7,
                _ => 0
            };

            int BandCoeffOffset(int band) => band switch
            {
                1 => 0,
                2 => 3,
                3 => 8,
                _ => 0
            };

            void SetBandCentroids(int band, Vector3[] centroids)
            {
                switch (band)
                {
                    case 1:
                        asset.Sh1Centroids = centroids;
                        break;
                    case 2:
                        asset.Sh2Centroids = centroids;
                        break;
                    case 3:
                        asset.Sh3Centroids = centroids;
                        break;
                }
            }

            void SetBandDeltaSegments(int band, global::Gsplat.Splat4DShDeltaSegment[] deltaSegments)
            {
                switch (band)
                {
                    case 1:
                        asset.Sh1DeltaSegments = deltaSegments;
                        break;
                    case 2:
                        asset.Sh2DeltaSegments = deltaSegments;
                        break;
                    case 3:
                        asset.Sh3DeltaSegments = deltaSegments;
                        break;
                }
            }

            Splat4DV2Section? FindSingle(uint kind, uint band)
            {
                for (var i = 0; i < sections.Length; i++)
                {
                    var s = sections[i];
                    if (s.kind == kind && s.band == band)
                        return s;
                }
                return null;
            }

            bool ValidateRange(Splat4DV2Section s, string debugName)
            {
                var end = s.offset + s.length;
                if (end > (ulong)fs.Length)
                    return Fail($"{debugName} out of file range: offset={s.offset}, length={s.length}, fileLength={fs.Length}");
                return true;
            }

            // per band decode
            for (var band = 1; band <= shBands; band++)
            {
                var info = GetBandInfo(band);
                if (info.codebookCount == 0)
                    return Fail($"META missing band info: sh{band}.codebookCount=0");
                if (info.centroidsType != 1 && info.centroidsType != 2)
                    return Fail($"META invalid sh{band}.centroidsType={info.centroidsType} (must be 1 or 2)");
                if (info.labelsEncoding != 1 && info.labelsEncoding != 2)
                    return Fail($"META invalid sh{band}.labelsEncoding={info.labelsEncoding} (must be 1 or 2)");

                var ctOpt = FindSingle(k_sectShCt, (uint)band);
                if (ctOpt == null)
                    return Fail($"v2 missing required section: SHCT band={band}");
                var ct = ctOpt.Value;
                if (!ValidateRange(ct, $"SHCT(band={band})")) return false;

                // labels: full 或 delta-v1(我们只需要 base labels(第一个 segment))
                Splat4DV2Section labelsSection;
                List<global::Gsplat.Splat4DShDeltaSegment> deltaSegmentsForBand = null;
                var expectedLabelsBytes = (ulong)header.splatCount * 2ul;
                if (info.labelsEncoding == 1)
                {
                    var lbOpt = FindSingle(k_sectShLb, (uint)band);
                    if (lbOpt == null)
                        return Fail($"v2 missing required section: SHLB(full) band={band}");
                    labelsSection = lbOpt.Value;
                }
                else
                {
                    if (header.frameCount == 0)
                        return Fail("v2 header.frameCount must be >0 when labelsEncoding=delta-v1");

                    // 找到 segment startFrame=0 的 base labels.
                    Splat4DV2Section? lb0 = null;
                    for (var i = 0; i < sections.Length; i++)
                    {
                        var s = sections[i];
                        if (s.kind == k_sectShLb && s.band == (uint)band && s.startFrame == 0 && s.frameCount > 0)
                        {
                            lb0 = s;
                            break;
                        }
                    }
                    if (lb0 == null)
                        return Fail($"v2 missing base labels segment(startFrame=0) for band={band}");
                    labelsSection = lb0.Value;

                    // 轻量校验 segments 覆盖性与 delta header,并把 base labels + delta bytes 持久化到资产.
                    deltaSegmentsForBand = new List<global::Gsplat.Splat4DShDeltaSegment>();
                    var expectedStart = 0u;
                    while (expectedStart < header.frameCount)
                    {
                        Splat4DV2Section? segLb = null;
                        Splat4DV2Section? segDl = null;
                        for (var i = 0; i < sections.Length; i++)
                        {
                            var s = sections[i];
                            if (s.band != (uint)band)
                                continue;
                            if (s.startFrame != expectedStart)
                                continue;
                            if (s.kind == k_sectShLb)
                                segLb = s;
                            else if (s.kind == k_sectShDl)
                                segDl = s;
                        }
                        if (segLb == null || segDl == null)
                            return Fail($"v2 delta-v1 segments broken for band={band}: missing SHLB/SHDL at startFrame={expectedStart}");
                        if (segLb.Value.frameCount == 0 || segLb.Value.frameCount != segDl.Value.frameCount)
                            return Fail($"v2 delta-v1 segments mismatch for band={band} startFrame={expectedStart}");
                        if (segLb.Value.length != expectedLabelsBytes)
                            return Fail($"invalid SHLB.length for band={band} startFrame={expectedStart}: {segLb.Value.length}, expected {expectedLabelsBytes}");

                        if (!ValidateRange(segLb.Value, $"SHLB(band={band},start={expectedStart})")) return false;

                        if (!ValidateRange(segDl.Value, $"SHDL(band={band},start={expectedStart})")) return false;
                        fs.Seek((long)segDl.Value.offset, SeekOrigin.Begin);
                        var deltaMagic = br.ReadBytes(8);
                        if (deltaMagic.Length != 8)
                            return Fail("unexpected EOF while reading delta header magic");
                        var deltaMagicOk =
                            (deltaMagic[0] == (byte)'S' && deltaMagic[1] == (byte)'O' && deltaMagic[2] == (byte)'G') ||
                            (deltaMagic[0] == (byte)'S' && deltaMagic[1] == (byte)'P' && deltaMagic[2] == (byte)'L');
                        if (!deltaMagicOk)
                            return Fail($"invalid delta magic in SHDL(band={band},start={expectedStart})");
                        var deltaVersion = br.ReadUInt32();
                        var segStart = br.ReadUInt32();
                        var segCount = br.ReadUInt32();
                        var deltaSplatCount = br.ReadUInt32();
                        var deltaLabelCount = br.ReadUInt32();
                        if (deltaVersion != 1 ||
                            segStart != expectedStart ||
                            segCount != segLb.Value.frameCount ||
                            deltaSplatCount != header.splatCount ||
                            deltaLabelCount != info.codebookCount)
                        {
                            return Fail($"invalid delta header in SHDL(band={band},start={expectedStart})");
                        }

                        // 读取并持久化 base labels / delta bytes(运行时使用).
                        fs.Seek((long)segLb.Value.offset, SeekOrigin.Begin);
                        var baseBytes = br.ReadBytes((int)segLb.Value.length);
                        if (baseBytes.Length != (int)segLb.Value.length)
                            return Fail($"unexpected EOF while reading SHLB(band={band},start={expectedStart})");

                        fs.Seek((long)segDl.Value.offset, SeekOrigin.Begin);
                        var deltaBytes = br.ReadBytes((int)segDl.Value.length);
                        if (deltaBytes.Length != (int)segDl.Value.length)
                            return Fail($"unexpected EOF while reading SHDL(band={band},start={expectedStart})");

                        deltaSegmentsForBand.Add(new global::Gsplat.Splat4DShDeltaSegment
                        {
                            StartFrame = (int)expectedStart,
                            FrameCount = (int)segLb.Value.frameCount,
                            BaseLabelsBytes = baseBytes,
                            DeltaBytes = deltaBytes
                        });

                        expectedStart += segLb.Value.frameCount;
                    }

                    asset.ShFrameCount = (int)header.frameCount;
                }

                if (!ValidateRange(labelsSection, $"SHLB(band={band})")) return false;
                if (labelsSection.length != expectedLabelsBytes)
                    return Fail($"invalid SHLB.length for band={band}: {labelsSection.length}, expected {expectedLabelsBytes}");

                // 读取 centroids
                var coeffCount = BandCoeffCount(band);
                var scalarBytes = info.centroidsType == 1 ? 2 : 4;
                var expectedCentroidsBytes = (ulong)info.codebookCount * (ulong)(coeffCount * 3) * (ulong)scalarBytes;
                if (ct.length != expectedCentroidsBytes)
                    return Fail($"invalid SHCT.length for band={band}: {ct.length}, expected {expectedCentroidsBytes}");

                fs.Seek((long)ct.offset, SeekOrigin.Begin);
                var centroidsBytes = br.ReadBytes((int)ct.length);
                if (centroidsBytes.Length != (int)ct.length)
                    return Fail($"unexpected EOF while reading SHCT(band={band})");

                var centroids = new Vector3[(int)info.codebookCount * coeffCount];
                if (info.centroidsType == 1)
                {
                    var halves = MemoryMarshal.Cast<byte, ushort>(centroidsBytes.AsSpan());
                    for (var k = 0; k < (int)info.codebookCount; k++)
                    {
                        for (var c = 0; c < coeffCount; c++)
                        {
                            var baseIdx = (k * coeffCount + c) * 3;
                            centroids[k * coeffCount + c] = new Vector3(
                                HalfToFloat(halves[baseIdx + 0]),
                                HalfToFloat(halves[baseIdx + 1]),
                                HalfToFloat(halves[baseIdx + 2]));
                        }
                    }
                }
                else
                {
                    var floats = MemoryMarshal.Cast<byte, float>(centroidsBytes.AsSpan());
                    for (var k = 0; k < (int)info.codebookCount; k++)
                    {
                        for (var c = 0; c < coeffCount; c++)
                        {
                            var baseIdx = (k * coeffCount + c) * 3;
                            centroids[k * coeffCount + c] = new Vector3(
                                floats[baseIdx + 0],
                                floats[baseIdx + 1],
                                floats[baseIdx + 2]);
                        }
                    }
                }

                // centroids 持久化到资产(运行时应用 delta 需要它).
                SetBandCentroids(band, centroids);

                // 读取 labels
                fs.Seek((long)labelsSection.offset, SeekOrigin.Begin);
                var labelsBytes = br.ReadBytes((int)labelsSection.length);
                if (labelsBytes.Length != (int)labelsSection.length)
                    return Fail($"unexpected EOF while reading SHLB(band={band})");
                var labels = MemoryMarshal.Cast<byte, ushort>(labelsBytes.AsSpan());

                // 写入资产 SHs
                var coeffOffset = BandCoeffOffset(band);
                if (coeffOffset + coeffCount > restCoeffCountTotal)
                    return Fail($"internal error: band coeff range out of restCoeffCountTotal (band={band})");

                for (var i = 0; i < splatCount; i++)
                {
                    var label = labels[i];
                    if (label >= info.codebookCount)
                        return Fail($"SH labels out of range: band={band} splatId={i} label={label} >= {info.codebookCount}");

                    var centroidBase = (int)label * coeffCount;
                    var dstBase = i * restCoeffCountTotal + coeffOffset;
                    for (var c = 0; c < coeffCount; c++)
                        asset.SHs[dstBase + c] = centroids[centroidBase + c];

                    if ((i & 0x3ffff) == 0)
                    {
                        EditorUtility.DisplayProgressBar("Importing Gsplat Asset", $"Decoding SH band={band}",
                            i / (float)splatCount);
                    }
                }

                // delta segments 持久化到资产(仅 delta-v1).
                if (deltaSegmentsForBand != null)
                    SetBandDeltaSegments(band, deltaSegmentsForBand.ToArray());
            }

            return true;
        }
    }
}
