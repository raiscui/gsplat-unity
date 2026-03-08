// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

Shader "Hidden/Gsplat/LidarExternalCapture"
{
    Properties
    {
        [HideInInspector] _LidarCaptureBaseColor("LiDAR Capture Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _LidarExternalDepthZTest("LiDAR External Depth ZTest", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
        }

        // 说明:
        // - frustum external capture 的目标是拿到“当前相机看到的最近表面”.
        // - 若这里继续依赖面剔除(`Cull Back`),在手动 VP / RT flip / 负缩放/镜像 transform 组合下,
        //   很容易把 front/back 判定翻掉,表现成 LiDAR 粒子稳定落到 mesh 背面.
        // - 改成 `Cull Off` 后,由 depth buffer 自然选择最近可见表面,对闭合 mesh 更稳健.
        Cull Off
        Pass
        {
            Name "DepthCapture"
            ColorMask R
            // 关键语义:
            // - `Cull Off` 只负责避免 front/back 判反.
            // - "当前像素最近表面"重新交给硬件 depth buffer 决定.
            // - 颜色 RT 中写入的是最近表面的线性 view depth,供后续 compute resolve 转回 LiDAR 射线距离.
            // - ZTest 不能写死成 LEqual:
            //   reversed-Z 平台需要 GreaterEqual,否则闭合 mesh 会稳定留下 far side.
            ZTest [_LidarExternalDepthZTest]
            ZWrite On
            Blend One Zero

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragDepth

            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float linearDepth : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.positionOS);

                // 这里显式输出正值线性 view depth.
                // 后续 compute resolve 会把它还原成沿 LiDAR cell-center ray 的深度语义.
                float3 positionVS = UnityObjectToViewPos(input.positionOS);
                output.linearDepth = max(-positionVS.z, 0.0);
                return output;
            }

            float FragDepth(Varyings input) : SV_Target
            {
                return input.linearDepth;
            }
            ENDHLSL
        }

        Pass
        {
            Name "SurfaceColorCapture"
            // 只让与上一 pass 最近深度完全一致的表面写颜色.
            // 这样 external color 与 external depth 必然来自同一层最近表面.
            ZTest Equal
            ZWrite Off
            Blend One Zero

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragColor

            #include "UnityCG.cginc"

            float4 _LidarCaptureBaseColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                return output;
            }

            float4 FragColor(Varyings input) : SV_Target
            {
                return _LidarCaptureBaseColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
