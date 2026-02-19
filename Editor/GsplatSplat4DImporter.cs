// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
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
    [ScriptedImporter(1, "splat4d")]
    public sealed class GsplatSplat4DImporter : ScriptedImporter
    {
        // 与 HLSL 侧 `SH_C0` 保持一致,用于把 baseRgb 还原回 f_dc 系数:
        // baseRgb = f_dc * SH_C0 + 0.5  =>  f_dc = (baseRgb - 0.5) / SH_C0
        const float k_shC0 = 0.28209479177387814f;

        const int k_recordSizeBytes = 64;

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
        // - VFX Graph asset 放在 Samples~ 中,所以这里用 package 路径直接加载.
        const string k_defaultVfxAssetPath = GsplatUtils.k_PackagePath + "Samples~/VFXGraphSample/VFX/Splat.vfx";
        const string k_defaultVfxSortedAssetPath = GsplatUtils.k_PackagePath + "Samples~/VFXGraphSample/VFX/SplatSorted.vfx";

        // VFX 后端辅助 compute shader(用于生成 AxisBuffer/动态 buffers).
        const string k_vfxComputeShaderPath = GsplatUtils.k_PackagePath + "Runtime/Shaders/GsplatVfx.compute";
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
            }

            var bytes = File.ReadAllBytes(ctx.assetPath);
            if (bytes.Length == 0)
            {
                if (GsplatSettings.Instance.ShowImportErrors)
                    Debug.LogError($"{ctx.assetPath} import error: empty .splat4d file");
                return;
            }

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

            // 一期 `.splat4d` 只承载 SH0(DC) 与 opacity,不承载高阶 SH.
            gsplatAsset.SHBands = 0;
            gsplatAsset.SHs = null;

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
            var maxDurationClamped = 0.0f;

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

                var t0Clamped = Mathf.Clamp01(t0);
                var dtClamped = Mathf.Clamp01(dt);
                if (t0Clamped != t0 || dtClamped != dt)
                    clamped = true;

                gsplatAsset.Velocities[i] = vel;
                gsplatAsset.Times[i] = t0Clamped;
                gsplatAsset.Durations[i] = dtClamped;

                // motion 统计(基于 clamp 后的 duration,更贴近运行时可见时间窗)
                maxSpeed = Mathf.Max(maxSpeed, vel.magnitude);
                maxDurationClamped = Mathf.Max(maxDurationClamped, dtClamped);

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
            gsplatAsset.MaxDuration = maxDurationClamped;

            if (clamped && GsplatSettings.Instance.ShowImportErrors)
            {
                Debug.LogWarning(
                    $"{ctx.assetPath} import warning: clamped time/duration to [0,1]. " +
                    $"time(min={minTime}, max={maxTime}), duration(min={minDuration}, max={maxDuration})");
            }

            EditorUtility.ClearProgressBar();

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
            var vfxAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(k_defaultVfxSortedAssetPath);
            if (vfxAsset == null)
                vfxAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.VFX.VisualEffectAsset>(k_defaultVfxAssetPath);
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
                // Samples~ 可能没被导入/没被识别,此时给出可执行提示.
                Debug.LogWarning(
                    $"[Gsplat][VFX] 未找到默认 VFX Graph asset: {k_defaultVfxSortedAssetPath} 或 {k_defaultVfxAssetPath}. " +
                    "你仍可使用 Gsplat 主后端渲染. 若需要 VFX 后端,请导入本包 Samples~/VFXGraphSample,或手动指定 VisualEffectAsset.");
            }
#endif

            ctx.AddObjectToAsset("prefab", prefab);
            ctx.SetMainObject(prefab);
        }
    }
}
