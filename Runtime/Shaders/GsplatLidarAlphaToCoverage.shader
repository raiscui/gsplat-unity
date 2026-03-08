// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

Shader "Gsplat/LiDARAlphaToCoverage"
{
    Properties
    {
        // ----------------------------------------------------------------
        // LiDAR A2C shell:
        // - 只负责提供 `AlphaToMask On` 这一 pass 状态差异.
        // - 主体 HLSL 逻辑与普通 LiDAR shader 共用 `GsplatLidarPassCore.hlsl`,
        //   避免 show/hide / external hit / 颜色路径维护成两份.
        // ----------------------------------------------------------------
        [HideInInspector] _LidarParticleAAAnalyticCoverage("_LidarParticleAAAnalyticCoverage", Float) = 0
        [HideInInspector] _LidarParticleAAFringePixels("_LidarParticleAAFringePixels", Float) = 1
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
    }
    SubShader
    {
        Tags
        {
            // 说明:
            // - 这里不再把 A2C 当成“普通透明混合 pass 顺手加 AlphaToMask”.
            // - 改成 coverage-first 路线:
            //   alpha 主要用于 sample coverage,颜色写入走不透明式覆盖.
            // - Queue 仍保留 Transparent,尽量减少与现有 LiDAR / splat 提交顺序的耦合变化.
            "RenderType"="TransparentCutout"
            "Queue"="Transparent"
        }

        Pass
        {
            ZWrite On
            Blend One Zero
            ColorMask RGB
            Cull Off
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require compute

            #include "UnityCG.cginc"
            #define GSPLAT_LIDAR_A2C_PASS 1
            #include "GsplatLidarPassCore.hlsl"
            ENDHLSL
        }
    }
}
