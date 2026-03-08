// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

Shader "Hidden/Gsplat/LidarExternalCapture"
{
    Properties
    {
        [HideInInspector] _LidarCaptureBaseColor("LiDAR Capture Base Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
        }

        Cull Back
        ZTest LEqual
        ZWrite On

        Pass
        {
            Name "DepthCapture"
            ColorMask R

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
