// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

Shader "Gsplat/LiDAR"
{
    Properties
    {
        // ----------------------------------------------------------------
        // 注意:
        // - 我们在 C# 侧通过 MaterialPropertyBlock 下发这些参数.
        // - 某些 Unity/SRP 组合下,如果 shaderLab 的 Properties 未声明,MPB 设置可能会被忽略,
        //   导致“代码传参非 0,但画面完全无变化”.
        // - 因此这里把 show/hide / AA 参数显式声明为隐藏属性,用于稳态绑定与诊断.
        // ----------------------------------------------------------------
        [HideInInspector] _LidarParticleAAAnalyticCoverage("_LidarParticleAAAnalyticCoverage", Float) = 0
        [HideInInspector] _LidarParticleAAFringePixels("_LidarParticleAAFringePixels", Float) = 1
        [HideInInspector] _LidarPointJitterCellFraction("_LidarPointJitterCellFraction", Float) = 0.35
        [HideInInspector] _LidarExternalHitBiasMeters("_LidarExternalHitBiasMeters", Float) = 0
        [HideInInspector] _LidarShowHideNoiseMode("_LidarShowHideNoiseMode", Float) = 0
        [HideInInspector] _LidarShowHideNoiseStrength("_LidarShowHideNoiseStrength", Float) = 0
        [HideInInspector] _LidarShowHideNoiseScale("_LidarShowHideNoiseScale", Float) = 0
        [HideInInspector] _LidarShowHideNoiseSpeed("_LidarShowHideNoiseSpeed", Float) = 0
        [HideInInspector] _LidarShowHideWarpPixels("_LidarShowHideWarpPixels", Float) = 6
        [HideInInspector] _LidarShowHideWarpStrength("_LidarShowHideWarpStrength", Float) = 2
        [HideInInspector] _LidarShowHideGlowColor("_LidarShowHideGlowColor", Color) = (1,0.45,0.1,1)
        [HideInInspector] _LidarShowHideGlowIntensity("_LidarShowHideGlowIntensity", Float) = 0
        [HideInInspector] _LidarUnscannedIntensity("_LidarUnscannedIntensity", Float) = 0
        [HideInInspector] _LidarIntensityDistanceDecay("_LidarIntensityDistanceDecay", Float) = 0
        [HideInInspector] _LidarUnscannedIntensityDistanceDecay("_LidarUnscannedIntensityDistanceDecay", Float) = 0
        [HideInInspector] _LidarIntensityDistanceDecayMode("_LidarIntensityDistanceDecayMode", Float) = 0
        [HideInInspector] _LidarEnableScanMotion("_LidarEnableScanMotion", Float) = 1
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            // 重要:
            // - alpha blend 下若不写 depth,点云内部的遮挡关系会依赖绘制顺序,很容易看起来“乱穿”.
            // - 这里开启 ZWrite,让更近的点能稳定遮挡更远的点(看起来更像真实点云/回波).
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require compute

            #include "UnityCG.cginc"
            #include "GsplatLidarPassCore.hlsl"
            ENDHLSL
        }
    }
}
