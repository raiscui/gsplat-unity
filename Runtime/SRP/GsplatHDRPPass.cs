// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

#if GSPLAT_ENABLE_HDRP
using UnityEngine.Rendering.HighDefinition;

namespace Gsplat
{
    [System.Serializable]
    public class GsplatHDRPPass : CustomPass
    {
        protected override void Execute(CustomPassContext ctx)
        {
            // SRP 下我们统一在 `RenderPipelineManager.beginCameraRendering` 驱动排序,
            // 避免同一相机在 HDRP CustomPass 中重复 dispatch sort(性能与一致性都会受影响).
            if (GsplatSorter.Instance.SortDrivenBySrpCallback)
                return;

            if (GsplatSorter.Instance.Valid && GsplatSettings.Instance.Valid && GsplatSorter.Instance.GatherGsplatsForCamera(ctx.hdCamera.camera))
                GsplatSorter.Instance.DispatchSort(ctx.cmd, ctx.hdCamera.camera);
        }
    }
}

#endif
