// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

Shader "Gsplat/LiDAR"
{
    Properties {}
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
            // LiDAR 点云(Depth 模式)需要“真正不透明”的观感:
            // - 使用 alpha blend,当 alpha=1 时会完全覆盖背景颜色,不再受底图影响.
            // - LidarDepthOpacity 用于调节 Depth 模式下的 alpha(0..1).
            // - ColorMask RGB: 不写入 alpha 通道,避免污染 CameraTarget 的 alpha.
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require compute

            #include "UnityCG.cginc"

            // ----------------------------------------------------------------
            // 说明: 该 shader 只负责“看起来像 LiDAR 的规则点云”绘制.
            // - 点云来自 range image(minRangeSqBits/minSplatId),并由 LUT(az/beam sincos)重建方向.
            // - 点大小语义为屏幕像素半径(px radius).
            // - 颜色模式:
            //   - Depth: 距离映射色带
            //   - SplatColorSH0: 采样命中的 splat SH0 基础色
            // ----------------------------------------------------------------

            bool _GammaToLinear;
            int _SplatInstanceSize;

            // range image 尺寸:
            // - cellCount = beamCount * azimuthBins
            int _LidarCellCount;
            int _LidarAzimuthBins;
            int _LidarBeamCount;

            // LiDAR 位姿:
            // - local->world
            float4x4 _LidarMatrixL2W;

            // 点大小(px radius)
            float _LidarPointRadiusPixels;

            // 颜色模式:
            // - 0: Depth
            // - 1: SplatColorSH0
            int _LidarColorMode;

            float _LidarDepthNear;
            float _LidarDepthFar;

            // 扫描前沿/余辉:
            float _LidarRotationHz;
            float _LidarTrailGamma;
            float _LidarIntensity;
            float _LidarDepthOpacity;
            float _LidarTime;

            StructuredBuffer<uint> _LidarMinRangeSqBits;
            StructuredBuffer<uint> _LidarMinSplatId;
            StructuredBuffer<float2> _LidarAzSinCos;
            StructuredBuffer<float2> _LidarBeamSinCos;

            // splat 基础色(SH0)存放于 _ColorBuffer:
            // - decode 与主 shader 保持一致: rgb = sh0 * SH_C0 + 0.5
            StructuredBuffer<float4> _ColorBuffer;

            static const uint kLidarInfBits = 0x7f7fffff; // float max
            static const uint kLidarInvalidId = 0xffffffff;
            static const float SH_C0 = 0.28209479177387814;

            struct appdata
            {
                float4 vertex : POSITION;
                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_STEREO_INSTANCING_ENABLED)
                uint instanceID : SV_InstanceID;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 rgb : TEXCOORD1;
                float trail01 : TEXCOORD2;
            };

            float3 HsvToRgb(float3 c)
            {
                // 来自常见的 hsv2rgb 近似实现:
                // - h,s,v 均为 [0,1]
                // - 只用 frac/abs/lerp,便宜且跨平台差异小.
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            float3 DepthToCyanRed(float t)
            {
                // 深度渐变(从青到红,中间经过蓝/紫):
                // - h: 0.5(cyan/180°) -> 1.0(red/360°)
                // - 这条路径会自然经过 blue/purple,符合“青 -> 蓝 -> 紫 -> 红”的观感预期.
                t = saturate(t);
                float h = lerp(0.5, 1.0, t);
                return HsvToRgb(float3(h, 1.0, 1.0));
            }

            bool InitLidarSource(appdata v, out uint cellId, out uint beamIndex, out uint azBin)
            {
                cellId = 0;
                beamIndex = 0;
                azBin = 0;

                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_STEREO_INSTANCING_ENABLED)
                cellId = v.instanceID * (uint)_SplatInstanceSize + asuint(v.vertex.z);
                #else
                cellId = unity_InstanceID * (uint)_SplatInstanceSize + asuint(v.vertex.z);
                #endif

                if (cellId >= (uint)_LidarCellCount)
                    return false;

                // cellId = beamIndex * azimuthBins + azBin
                if (_LidarAzimuthBins <= 0)
                    return false;

                beamIndex = cellId / (uint)_LidarAzimuthBins;
                azBin = cellId - beamIndex * (uint)_LidarAzimuthBins;
                if (beamIndex >= (uint)_LidarBeamCount)
                    return false;

                return true;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = float4(0, 0, 0, 0);
                o.uv = float2(0, 0);
                o.rgb = float3(0, 0, 0);
                o.trail01 = 0.0;

                uint cellId;
                uint beamIndex;
                uint azBin;
                if (!InitLidarSource(v, cellId, beamIndex, azBin))
                {
                    o.vertex = float4(0.0, 0.0, 2.0, 1.0); // discardVec
                    return o;
                }

                uint rangeSqBits = _LidarMinRangeSqBits[cellId];
                if (rangeSqBits >= kLidarInfBits)
                {
                    o.vertex = float4(0.0, 0.0, 2.0, 1.0);
                    return o;
                }

                float rangeSq = asfloat(rangeSqBits);
                float range = sqrt(max(rangeSq, 0.0));

                // 方向重建:
                float2 azSC = _LidarAzSinCos[azBin]; // (sin,cos)
                float2 beSC = _LidarBeamSinCos[beamIndex]; // (sin,cos)
                float3 dirLocal = float3(azSC.x * beSC.y, beSC.x, azSC.y * beSC.y);

                float3 worldPos = mul(_LidarMatrixL2W, float4(dirLocal * range, 1.0)).xyz;

                // view/proj:
                float4 view = mul(UNITY_MATRIX_V, float4(worldPos, 1.0));
                if (view.z > 0.0)
                {
                    o.vertex = float4(0.0, 0.0, 2.0, 1.0);
                    return o;
                }
                float4 proj = mul(UNITY_MATRIX_P, view);
                proj.z = clamp(proj.z, -abs(proj.w), abs(proj.w));

                // 颜色:
                float3 baseRgb = float3(1.0, 1.0, 1.0);
                if (_LidarColorMode == 0)
                {
                    float denom = max(_LidarDepthFar - _LidarDepthNear, 1e-6);
                    float depth01 = saturate((range - _LidarDepthNear) / denom);
                    baseRgb = DepthToCyanRed(depth01);
                }
                else
                {
                    uint splatId = _LidarMinSplatId[cellId];
                    if (splatId == kLidarInvalidId)
                    {
                        o.vertex = float4(0.0, 0.0, 2.0, 1.0);
                        return o;
                    }

                    float4 sh0 = _ColorBuffer[splatId];
                    baseRgb = sh0.rgb * SH_C0 + 0.5;
                }

                // 扫描前沿+余辉(按 azBin 年龄):
                // - headBin 由 realtime 与 RotationHz 决定.
                // - deltaBins 表示“落后扫描头多少个 bin”.
                float head01 = frac(_LidarTime * _LidarRotationHz);
                uint headBin = (uint)floor(head01 * (float)_LidarAzimuthBins);
                if (headBin >= (uint)_LidarAzimuthBins)
                    headBin = (uint)_LidarAzimuthBins - 1u;

                uint deltaBins = headBin >= azBin
                    ? (headBin - azBin)
                    : (headBin + (uint)_LidarAzimuthBins - azBin);

                float age01 = (float)deltaBins / max((float)_LidarAzimuthBins, 1.0);
                float t = saturate(1.0 - age01);
                float trail01 = (_LidarTrailGamma <= 0.0) ? 1.0 : pow(max(t, 1e-6), _LidarTrailGamma);
                trail01 = saturate(trail01);

                // 点大小(px radius):
                float rPx = max(_LidarPointRadiusPixels, 0.0);
                float2 c = proj.ww / _ScreenParams.xy;
                float2 offset = float2(v.vertex.x, v.vertex.y) * rPx * c;

                o.vertex = proj + float4(offset.x, _ProjectionParams.x * offset.y, 0.0, 0.0);
                o.uv = float2(v.vertex.x, v.vertex.y);
                o.rgb = max(baseRgb, float3(0.0, 0.0, 0.0));
                o.trail01 = trail01;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // 形状: 正方形点(屏幕空间)
                float2 a = abs(i.uv);
                float d = max(a.x, a.y); // Chebyshev distance
                if (d > 1.0) discard;

                // 实心 + 柔边:
                // - feather 越小越“硬方块”,越大越柔.
                const float kSquareFeather = 0.10;
                float inner = 1.0 - kSquareFeather;
                float alphaShape = 1.0 - smoothstep(inner, 1.0, d);

                // 强度语义:
                // - Trail 与 Shape 决定点的覆盖范围(Shape)与余辉衰减(Trail).
                // - Intensity 只控制 rgb 亮度,避免出现“调强度=变透明”的错觉.
                float trail = saturate(i.trail01);
                float intensity = max(_LidarIntensity, 0.0);
                // DepthOpacity:
                // - 仅 Depth 模式生效,用于调节 alpha(0..1),从而得到“真正的不透明/透明”.
                float depthOpacity = (_LidarColorMode == 0) ? saturate(_LidarDepthOpacity) : 1.0;
                float alpha = saturate(alphaShape * depthOpacity);

                // 余辉只影响亮度,不影响 alpha,避免在浅色底图上出现“透明发灰”.
                float brightness = intensity * trail;
                if (brightness * alpha < 1.0 / 255.0) discard;

                float3 rgb = i.rgb * brightness;
                if (_GammaToLinear)
                    return float4(GammaToLinearSpace(rgb), alpha);
                return float4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
