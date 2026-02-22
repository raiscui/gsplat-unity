// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

#if UNITY_EDITOR
using System;
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
    }
}
#endif
