// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using UnityEditor;
using UnityEngine;

namespace Gsplat.Editor
{
    public static class BatchVerifySplat4DImport
    {
        // --------------------------------------------------------------------
        // Unity -executeMethod 入口.
        // 目的: 用 batchmode 确认 `.splat4d` 导入结果(尤其是 timeModel 自动识别)是否符合预期.
        // 注意: 该方法会强制 reimport,并对结果做最小统计,失败会抛异常让 Unity 以非 0 退出.
        // --------------------------------------------------------------------
        [MenuItem("Tools/Gsplat/Batch Verify/Verify ckpt_29999_v2_gaussian")]
        public static void VerifyCkpt29999V2Gaussian()
        {
            const string assetPath = "Assets/Gsplat/splat/ckpt_29999_v2_gaussian.splat4d";

            Debug.Log($"[Gsplat][BatchVerify] Reimporting: {assetPath}");
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            var allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (allAssets == null || allAssets.Length == 0)
                throw new Exception($"LoadAllAssetsAtPath returned empty for {assetPath}");

            global::Gsplat.GsplatAsset gsplatAsset = null;
            foreach (var obj in allAssets)
            {
                if (obj is global::Gsplat.GsplatAsset a)
                {
                    gsplatAsset = a;
                    break;
                }
            }

            if (!gsplatAsset)
                throw new Exception($"No {nameof(global::Gsplat.GsplatAsset)} sub-asset found in {assetPath}");

            // ----------------------------------------------------------------
            // 统计 time/duration 的 min/max,用于确认:
            // - window(timeModel=1)时: time/duration 应落在 [0,1]
            // - gaussian(timeModel=2)时: time(mu) 允许越界,且 duration(sigma) 应 >= epsilon
            // ----------------------------------------------------------------
            var times = gsplatAsset.Times;
            var durations = gsplatAsset.Durations;
            if (times == null || durations == null || times.Length == 0 || durations.Length == 0)
                throw new Exception($"Missing Times/Durations arrays in imported asset: {assetPath}");

            var minTime = float.PositiveInfinity;
            var maxTime = float.NegativeInfinity;
            for (var i = 0; i < times.Length; i++)
            {
                var t = times[i];
                if (t < minTime) minTime = t;
                if (t > maxTime) maxTime = t;
            }

            var minDuration = float.PositiveInfinity;
            var maxDuration = float.NegativeInfinity;
            for (var i = 0; i < durations.Length; i++)
            {
                var d = durations[i];
                if (d < minDuration) minDuration = d;
                if (d > maxDuration) maxDuration = d;
            }

            Debug.Log(
                $"[Gsplat][BatchVerify] Imported {assetPath}: " +
                $"SplatCount={gsplatAsset.SplatCount}, SHBands={gsplatAsset.SHBands}, " +
                $"TimeModel={gsplatAsset.TimeModel}, Cutoff={gsplatAsset.TemporalGaussianCutoff}, " +
                $"time(min={minTime}, max={maxTime}), duration(min={minDuration}, max={maxDuration}), " +
                $"MaxSpeed={gsplatAsset.MaxSpeed}, MaxDuration={gsplatAsset.MaxDuration}");

            // ----------------------------------------------------------------
            // 对本项目内的样例文件,我们预期会被识别为 gaussian(timeModel=2).
            // 若不是,说明自动识别没有触发,渲染大概率会退化成"薄层/稀疏".
            // ----------------------------------------------------------------
            if (gsplatAsset.TimeModel != 2)
                throw new Exception($"Expected TimeModel=2 for {assetPath}, got {gsplatAsset.TimeModel}");
        }

        [MenuItem("Tools/Gsplat/Batch Verify/Verify static single-frame fixture")]
        public static void VerifyStaticSingleFrameFixture()
        {
            const string assetFolder = "Assets/__GsplatBatchVerify";
            const string assetPath = assetFolder + "/static_single_frame_from_fixture.splat4d";

            static string GetProjectRootPath()
            {
                return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            }

            static string QuoteProcessArg(string value)
            {
                if (string.IsNullOrEmpty(value))
                    return "\"\"";
                if (value.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '"' }) < 0)
                    return value;
                return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }

            static MethodInfo RequireMethod(Type type, string name, BindingFlags flags)
            {
                var method = type.GetMethod(name, flags);
                if (method == null)
                    throw new Exception($"Missing method: {type.FullName}.{name}");
                return method;
            }

            static FieldInfo RequireField(Type type, string name, BindingFlags flags)
            {
                var field = type.GetField(name, flags);
                if (field == null)
                    throw new Exception($"Missing field: {type.FullName}.{name}");
                return field;
            }

            var projectRoot = GetProjectRootPath();
            var scriptPath = Path.Combine(projectRoot, "Packages", "wu.yize.gsplat", "Tools~", "Splat4D",
                "ply_sequence_to_splat4d.py");
            var fixturePlyPath = Path.Combine(projectRoot, "Packages", "wu.yize.gsplat", "Tools~", "Splat4D",
                "tests", "data", "single_frame_valid_3dgs.ply");
            var outputFullPath = Path.Combine(projectRoot, assetPath);

            if (!File.Exists(scriptPath))
                throw new Exception($"Missing exporter script: {scriptPath}");
            if (!File.Exists(fixturePlyPath))
                throw new Exception($"Missing single-frame fixture ply: {fixturePlyPath}");

            try
            {
                if (!AssetDatabase.IsValidFolder(assetFolder))
                    AssetDatabase.CreateFolder("Assets", "__GsplatBatchVerify");

                Directory.CreateDirectory(Path.GetDirectoryName(outputFullPath));

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

                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = "python3",
                        Arguments = arguments,
                        WorkingDirectory = projectRoot,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    if (!process.Start())
                        throw new Exception("Failed to start python3 exporter process.");

                    if (!process.WaitForExit(120000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                            // 超时后的 best-effort 清理.
                        }

                        throw new Exception("python3 exporter timed out after 120s.");
                    }

                    var stdout = process.StandardOutput.ReadToEnd();
                    var stderr = process.StandardError.ReadToEnd();
                    if (process.ExitCode != 0)
                    {
                        throw new Exception(
                            $"python3 exporter failed with exitCode={process.ExitCode}.\nstdout:\n{stdout}\nstderr:\n{stderr}");
                    }

                    Debug.Log($"[Gsplat][BatchVerify] Exporter ok.\nstdout:\n{stdout}\nstderr:\n{stderr}");
                }

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (!prefab)
                    throw new Exception($"Imported prefab is null: {assetPath}");

                var renderer = prefab.GetComponent<global::Gsplat.GsplatRenderer>();
                if (!renderer)
                    throw new Exception($"Imported prefab is missing GsplatRenderer: {assetPath}");

                var gsplatAsset = renderer.GsplatAsset;
                if (!gsplatAsset)
                    throw new Exception($"Imported renderer has null GsplatAsset: {assetPath}");

                if (gsplatAsset.SplatCount != 1)
                    throw new Exception($"Expected SplatCount=1, got {gsplatAsset.SplatCount}");
                if (gsplatAsset.SHBands != 0)
                    throw new Exception($"Expected SHBands=0 for `.splat4d` exporter path, got {gsplatAsset.SHBands}");
                if (gsplatAsset.TimeModel != 1)
                    throw new Exception($"Expected TimeModel=1(window), got {gsplatAsset.TimeModel}");
                if (gsplatAsset.Velocities == null || gsplatAsset.Velocities.Length != 1)
                    throw new Exception("Velocities array is missing or length mismatch.");
                if (gsplatAsset.Times == null || gsplatAsset.Times.Length != 1)
                    throw new Exception("Times array is missing or length mismatch.");
                if (gsplatAsset.Durations == null || gsplatAsset.Durations.Length != 1)
                    throw new Exception("Durations array is missing or length mismatch.");
                if (gsplatAsset.Velocities[0] != Vector3.zero)
                    throw new Exception($"Expected velocity=0, got {gsplatAsset.Velocities[0]}");
                if (Mathf.Abs(gsplatAsset.Times[0] - 0.0f) > 1e-6f)
                    throw new Exception($"Expected time=0, got {gsplatAsset.Times[0]}");
                if (Mathf.Abs(gsplatAsset.Durations[0] - 1.0f) > 1e-6f)
                    throw new Exception($"Expected duration=1, got {gsplatAsset.Durations[0]}");

                // runtime 侧再做一次最小动态验证:
                // - 不依赖真实 draw.
                // - 直接验证不同 TimeNormalized 下 sort range 始终保持整帧 1 splat.
                var rendererType = typeof(global::Gsplat.GsplatRenderer);
                var has4DFieldsMethod = RequireMethod(rendererType, "Has4DFields",
                    BindingFlags.Static | BindingFlags.NonPublic);
                var updateSortRangeForTimeMethod = RequireMethod(rendererType, "UpdateSortRangeForTime",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var effectiveSplatCountField = RequireField(rendererType, "m_effectiveSplatCount",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var pendingSplatCountField = RequireField(rendererType, "m_pendingSplatCount",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var sortBaseIndexField = RequireField(rendererType, "m_sortSplatBaseIndexThisFrame",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var sortCountField = RequireField(rendererType, "m_sortSplatCountThisFrame",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                var has4D = (bool)has4DFieldsMethod.Invoke(null, new object[] { gsplatAsset });
                if (!has4D)
                    throw new Exception("Expected imported asset to remain a legal 4D-capable asset.");

                effectiveSplatCountField.SetValue(renderer, (uint)1);
                pendingSplatCountField.SetValue(renderer, (uint)0);

                var samples = new[] { 0.0f, 0.35f, 1.0f };
                for (var i = 0; i < samples.Length; i++)
                {
                    updateSortRangeForTimeMethod.Invoke(renderer, new object[] { samples[i] });
                    var sortBaseIndex = (uint)sortBaseIndexField.GetValue(renderer);
                    var sortCount = (uint)sortCountField.GetValue(renderer);
                    if (sortBaseIndex != 0u || sortCount != 1u)
                    {
                        throw new Exception(
                            $"Static single-frame runtime verification failed at sample={samples[i]}: " +
                            $"baseIndex={sortBaseIndex}, count={sortCount}");
                    }
                }

                Debug.Log(
                    $"[Gsplat][BatchVerify] Imported {assetPath}: " +
                    $"SplatCount={gsplatAsset.SplatCount}, SHBands={gsplatAsset.SHBands}, " +
                    $"TimeModel={gsplatAsset.TimeModel}, velocity={gsplatAsset.Velocities[0]}, " +
                    $"time={gsplatAsset.Times[0]}, duration={gsplatAsset.Durations[0]}. " +
                    "Runtime samples(0,0.35,1) all kept full sort range.");
            }
            finally
            {
                AssetDatabase.DeleteAsset(assetFolder);
            }
        }

        [MenuItem("Tools/Gsplat/Batch Verify/Verify s1_point_cloud_v2_sh3")]
        public static void VerifyS1PointCloudV2Sh3()
        {
            const string assetPath = "Assets/Gsplat/splat/v2/s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d";
            const int expectedRestCoeffCount = 15;
            const int expectedShCodebookCount = 8192;

            Debug.Log($"[Gsplat][BatchVerify] Reimporting: {assetPath}");
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (!prefab)
                throw new Exception($"Imported prefab is null: {assetPath}");

            var renderer = prefab.GetComponent<global::Gsplat.GsplatRenderer>();
            if (!renderer)
                throw new Exception($"Imported prefab is missing GsplatRenderer: {assetPath}");

            var gsplatAsset = renderer.GsplatAsset;
            if (!gsplatAsset)
                throw new Exception($"Imported renderer has null GsplatAsset: {assetPath}");

            if (gsplatAsset.SplatCount == 0)
                throw new Exception("SplatCount should be > 0.");
            if (gsplatAsset.SHBands != 3)
                throw new Exception($"Expected SHBands=3, got {gsplatAsset.SHBands}");
            if (gsplatAsset.TimeModel != 1)
                throw new Exception($"Expected TimeModel=1(window), got {gsplatAsset.TimeModel}");
            if (gsplatAsset.ShFrameCount != 0)
                throw new Exception($"Expected ShFrameCount=0 for full-label single-frame asset, got {gsplatAsset.ShFrameCount}");

            if (gsplatAsset.SHs == null || gsplatAsset.SHs.Length != gsplatAsset.SplatCount * expectedRestCoeffCount)
            {
                throw new Exception(
                    $"Unexpected SHs length. got={gsplatAsset.SHs?.Length ?? -1}, " +
                    $"expected={gsplatAsset.SplatCount * expectedRestCoeffCount}");
            }

            if (gsplatAsset.Sh1Centroids == null || gsplatAsset.Sh1Centroids.Length != expectedShCodebookCount * 3)
                throw new Exception($"Unexpected Sh1Centroids length: {gsplatAsset.Sh1Centroids?.Length ?? -1}");
            if (gsplatAsset.Sh2Centroids == null || gsplatAsset.Sh2Centroids.Length != expectedShCodebookCount * 5)
                throw new Exception($"Unexpected Sh2Centroids length: {gsplatAsset.Sh2Centroids?.Length ?? -1}");
            if (gsplatAsset.Sh3Centroids == null || gsplatAsset.Sh3Centroids.Length != expectedShCodebookCount * 7)
                throw new Exception($"Unexpected Sh3Centroids length: {gsplatAsset.Sh3Centroids?.Length ?? -1}");

            // 说明:
            // - Runtime 把 `null` 和 `Length==0` 都视为“没有 delta-v1 段”.
            // - Unity 序列化在某些导入/重载时机会把 `null` 数组归一成 empty array.
            // - 因此这里要用“可用 segment 数量”判断,不能只看是否为 null.
            var sh1DeltaSegmentCount = gsplatAsset.Sh1DeltaSegments?.Length ?? 0;
            var sh2DeltaSegmentCount = gsplatAsset.Sh2DeltaSegments?.Length ?? 0;
            var sh3DeltaSegmentCount = gsplatAsset.Sh3DeltaSegments?.Length ?? 0;
            if (sh1DeltaSegmentCount > 0 || sh2DeltaSegmentCount > 0 || sh3DeltaSegmentCount > 0)
            {
                throw new Exception(
                    "Single-frame full-label asset should not carry usable delta-v1 segments. " +
                    $"counts=({sh1DeltaSegmentCount},{sh2DeltaSegmentCount},{sh3DeltaSegmentCount})");
            }

            if (gsplatAsset.Velocities == null || gsplatAsset.Times == null || gsplatAsset.Durations == null)
                throw new Exception("4D arrays are missing.");
            if (gsplatAsset.Velocities.Length != gsplatAsset.SplatCount)
                throw new Exception($"Unexpected Velocities length: {gsplatAsset.Velocities.Length}");
            if (gsplatAsset.Times.Length != gsplatAsset.SplatCount)
                throw new Exception($"Unexpected Times length: {gsplatAsset.Times.Length}");
            if (gsplatAsset.Durations.Length != gsplatAsset.SplatCount)
                throw new Exception($"Unexpected Durations length: {gsplatAsset.Durations.Length}");

            Debug.Log(
                $"[Gsplat][BatchVerify] Imported {assetPath}: " +
                $"SplatCount={gsplatAsset.SplatCount}, SHBands={gsplatAsset.SHBands}, " +
                $"TimeModel={gsplatAsset.TimeModel}, SHs={gsplatAsset.SHs.Length}, " +
                $"Sh1Centroids={gsplatAsset.Sh1Centroids.Length}, " +
                $"Sh2Centroids={gsplatAsset.Sh2Centroids.Length}, " +
                $"Sh3Centroids={gsplatAsset.Sh3Centroids.Length}");
        }
    }
}
#endif
