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
        // - 因此这里把 show/hide noise 参数显式声明为隐藏属性,用于稳态绑定与诊断.
        // ----------------------------------------------------------------
        [HideInInspector] _LidarShowHideNoiseMode("_LidarShowHideNoiseMode", Float) = 0
        [HideInInspector] _LidarShowHideNoiseStrength("_LidarShowHideNoiseStrength", Float) = 0
        [HideInInspector] _LidarShowHideNoiseScale("_LidarShowHideNoiseScale", Float) = 0
        [HideInInspector] _LidarShowHideNoiseSpeed("_LidarShowHideNoiseSpeed", Float) = 0
        [HideInInspector] _LidarShowHideWarpPixels("_LidarShowHideWarpPixels", Float) = 6
        [HideInInspector] _LidarShowHideWarpStrength("_LidarShowHideWarpStrength", Float) = 2
        // show/hide glow(对齐高斯 show/hide 的 additive glow 语义):
        // - 注意: glow 必须是“加到 rgb 上”的叠加,而不是仅仅乘 brightness.
        // - 这里把 glowColor/glowIntensity 也声明成隐藏属性,确保 MPB 下发稳定生效.
        [HideInInspector] _LidarShowHideGlowColor("_LidarShowHideGlowColor", Color) = (1,0.45,0.1,1)
        [HideInInspector] _LidarShowHideGlowIntensity("_LidarShowHideGlowIntensity", Float) = 0
        // 扫描强度:
        // - `_LidarIntensity` 控制“扫描头/刚扫过区域”的强度.
        // - `_LidarUnscannedIntensity` 控制“未扫到(或远离扫描头)区域”的底色强度.
        // - 当 `_LidarUnscannedIntensity=0` 时,保持旧行为: 亮度随 trail 衰减到 0.
        [HideInInspector] _LidarUnscannedIntensity("_LidarUnscannedIntensity", Float) = 0
        // 距离衰减(近强远弱):
        // - 这里把衰减乘数也声明为隐藏属性,确保 MPB 下发稳态生效.
        // - 0 表示不衰减(兼容旧项目).
        [HideInInspector] _LidarIntensityDistanceDecay("_LidarIntensityDistanceDecay", Float) = 0
        [HideInInspector] _LidarUnscannedIntensityDistanceDecay("_LidarUnscannedIntensityDistanceDecay", Float) = 0
        // 距离衰减模式:
        // - 0: Reciprocal(反比),1: Exponential(指数).
        // - 默认 0,以兼容旧项目.
        [HideInInspector] _LidarIntensityDistanceDecayMode("_LidarIntensityDistanceDecayMode", Float) = 0
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
            // world->model:
            // - 用于在 LiDAR shader 中复用 splat 的 model 空间 show/hide 语义.
            float4x4 _LidarMatrixW2M;

            // 点大小(px radius)
            float _LidarPointRadiusPixels;

            // 颜色模式:
            // - 0: Depth
            // - 1: SplatColorSH0
            int _LidarColorMode;
            float _LidarColorBlend;
            float _LidarVisibility;
            // show/hide overlay:
            // - gate: 0/1,用于终态 Hidden 的硬门禁.
            // - mode/progress/source: 与 splat show/hide 语义对齐.
            float _LidarShowHideGate;
            int _LidarShowHideMode;
            float _LidarShowHideProgress;
            int _LidarShowHideSourceMaskMode;
            float _LidarShowHideSourceMaskProgress;
            float3 _LidarShowHideCenterModel;
            float _LidarShowHideMaxRadius;
            float _LidarShowHideRingWidth;
            float _LidarShowHideTrailWidth;
            // show/hide noise:
            // - 语义与 splat 侧 VisibilityNoise* 对齐.
            // - 0: ValueSmoke, 1: CurlSmoke(curl-like 向量场), 2: HashLegacy.
            int _LidarShowHideNoiseMode;
            float _LidarShowHideNoiseStrength;
            float _LidarShowHideNoiseScale;
            float _LidarShowHideNoiseSpeed;
            // show/hide warp(屏幕像素):
            // - 这是“点云粒子抖动/扰动”的可调幅度,用于匹配用户期望的可见程度.
            // - 设计为独立参数,不要与点半径耦合,避免用户调点大小时噪声幅度也被动变化.
            float _LidarShowHideWarpPixels;
            // show/hide warp 强度倍率(0..3):
            // - 与高斯 show/hide 的 WarpStrength 语义对齐:
            //   - 0: 禁用位移扰动.
            //   - 2: 默认强度.
            //   - 3: 更强扰动(可能需要更小的 WarpPixels).
            float _LidarShowHideWarpStrength;
            // show/hide glow(对齐高斯 show/hide 的 additive glow 语义):
            // - glowColor: 使用 rgb,alpha 不参与.
            // - glowIntensity: >=0.
            float4 _LidarShowHideGlowColor;
            float _LidarShowHideGlowIntensity;

            float _LidarDepthNear;
            float _LidarDepthFar;

            // 扫描前沿/余辉:
            float _LidarRotationHz;
            float _LidarTrailGamma;
            float _LidarIntensity;
            float _LidarUnscannedIntensity;
            float _LidarIntensityDistanceDecay;
            float _LidarUnscannedIntensityDistanceDecay;
            float _LidarIntensityDistanceDecayMode;
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
                float showHideMul : TEXCOORD3;
                float showHideGlow : TEXCOORD4;
                float range : TEXCOORD5;
            };

            float EaseInOutQuad(float t)
            {
                t = saturate(t);
                if (t < 0.5)
                    return 2.0 * t * t;

                float u = -2.0 * t + 2.0;
                return 1.0 - (u * u) * 0.5;
            }

            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash13(i + float3(0.0, 0.0, 0.0));
                float n100 = Hash13(i + float3(1.0, 0.0, 0.0));
                float n010 = Hash13(i + float3(0.0, 1.0, 0.0));
                float n110 = Hash13(i + float3(1.0, 1.0, 0.0));
                float n001 = Hash13(i + float3(0.0, 0.0, 1.0));
                float n101 = Hash13(i + float3(1.0, 0.0, 1.0));
                float n011 = Hash13(i + float3(0.0, 1.0, 1.0));
                float n111 = Hash13(i + float3(1.0, 1.0, 1.0));

                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            // ----------------------------------------------------------------
            // Curl-like 噪声场(对齐高斯 show/hide 的 CurlSmoke):
            // - 用 3 份独立的 value noise 作为 vector potential A(p)=(Ax,Ay,Az),
            //   然后取 curl(A)=∇×A 得到“旋涡/流动”更明显的向量场.
            //
            // 说明:
            // - 这里复用 ValueNoise 的 hash+fade 形式,无需贴图,可在 model space 稳态复现.
            // - 该计算相对更重,因此只建议在 show/hide 过渡期间使用.
            // ----------------------------------------------------------------
            void ValueNoise3DGrad(float3 p, out float3 grad01)
            {
                float3 ip = floor(p);
                float3 fp = frac(p);

                // fade: f(t)=t^2*(3-2t), f'(t)=6t(1-t)
                float3 u = fp * fp * (3.0 - 2.0 * fp);
                float3 du = 6.0 * fp * (1.0 - fp);

                float n000 = Hash13(ip + float3(0.0, 0.0, 0.0));
                float n100 = Hash13(ip + float3(1.0, 0.0, 0.0));
                float n010 = Hash13(ip + float3(0.0, 1.0, 0.0));
                float n110 = Hash13(ip + float3(1.0, 1.0, 0.0));
                float n001 = Hash13(ip + float3(0.0, 0.0, 1.0));
                float n101 = Hash13(ip + float3(1.0, 0.0, 1.0));
                float n011 = Hash13(ip + float3(0.0, 1.0, 1.0));
                float n111 = Hash13(ip + float3(1.0, 1.0, 1.0));

                // x 方向插值
                float nx00 = lerp(n000, n100, u.x);
                float nx10 = lerp(n010, n110, u.x);
                float nx01 = lerp(n001, n101, u.x);
                float nx11 = lerp(n011, n111, u.x);

                // y 方向插值
                float nxy0 = lerp(nx00, nx10, u.y);
                float nxy1 = lerp(nx01, nx11, u.y);

                // ∂/∂x:
                // - 只有 u.x 依赖 x,因此只需要对 x 方向的 lerp 求导,再把结果继续按 y/z 插值.
                float dnx00_dx = (n100 - n000) * du.x;
                float dnx10_dx = (n110 - n010) * du.x;
                float dnx01_dx = (n101 - n001) * du.x;
                float dnx11_dx = (n111 - n011) * du.x;
                float dnxy0_dx = lerp(dnx00_dx, dnx10_dx, u.y);
                float dnxy1_dx = lerp(dnx01_dx, dnx11_dx, u.y);
                float dn_dx = lerp(dnxy0_dx, dnxy1_dx, u.z);

                // ∂/∂y:
                // - nxy0 = nx00 + (nx10-nx00)*u.y
                // - nxy1 = nx01 + (nx11-nx01)*u.y
                float dnxy0_dy = (nx10 - nx00) * du.y;
                float dnxy1_dy = (nx11 - nx01) * du.y;
                float dn_dy = lerp(dnxy0_dy, dnxy1_dy, u.z);

                // ∂/∂z:
                // - noise = nxy0 + (nxy1-nxy0)*u.z
                float dn_dz = (nxy1 - nxy0) * du.z;

                grad01 = float3(dn_dx, dn_dy, dn_dz);
            }

            float3 EvalCurlNoise(float3 p)
            {
                // 3 个不同 offset 的噪声,作为 vector potential 的三个分量.
                // 说明: offset 只要足够大且不共线即可,这里选固定常数用于可复现.
                float3 g1;
                float3 g2;
                float3 g3;
                ValueNoise3DGrad(p + float3(17.13, 31.77, 47.11), g1);
                ValueNoise3DGrad(p + float3(53.11, 12.77, 9.71), g2);
                ValueNoise3DGrad(p + float3(29.21, 83.11, 11.73), g3);

                // curl(A) = (∂Az/∂y - ∂Ay/∂z, ∂Ax/∂z - ∂Az/∂x, ∂Ay/∂x - ∂Ax/∂y)
                //
                // 注意:
                // - 这里把 grad 当作 signed 的比例场来用,仅影响尺度不影响方向.
                // - 我们后续主要用 normalize(curl) 做方向,因此不必过度纠结常数.
                return float3(
                    (g3.y - g2.z),
                    (g1.z - g3.x),
                    (g2.x - g1.y)
                );
            }

            float EvalLidarShowHideNoise01(float3 modelPos, float tNoise)
            {
                float scale = max(_LidarShowHideNoiseScale, 0.0);
                if (scale <= 0.0)
                    return 0.5;

                float3 tVec = float3(tNoise, tNoise * 1.37, tNoise * 1.93);
                float3 pBase = modelPos * scale;
                int mode = _LidarShowHideNoiseMode;
                if (mode == 2)
                {
                    // HashLegacy:
                    // - 更碎更抖,用于对齐 splat 的 legacy 噪声观感.
                    return Hash13(pBase * 2.2 + tVec * 2.5);
                }

                // ValueSmoke / CurlSmoke:
                // - 之前频率较低,在常见资产尺度下噪声不明显.
                // - 这里提高采样频率,并叠加一层轻量 domain warp,让 show/hide 边界有可见颗粒感.
                float freq = mode == 1 ? 1.35 : 0.95;
                float timeMul = mode == 1 ? 2.8 : 1.4;
                float3 p = pBase * freq + tVec * timeMul;

                // LiDAR 点云密度低于 splat,这里适当放大强度映射,保证默认参数下也能明显看到 noise 颗粒感.
                float noiseStrength = saturate(_LidarShowHideNoiseStrength * 1.6);
                if (mode == 1 && noiseStrength > 1.0e-4)
                {
                    // CurlSmoke:
                    // - 使用 curl-like 向量场做 domain warp,让噪声更像旋涡/流体流动,更少随机抖动感.
                    float3 curlVec = EvalCurlNoise(pBase * 0.85 + tVec * 1.70 + float3(13.7, 7.3, 19.1));
                    curlVec = normalize(curlVec + 1.0e-5);
                    float warpAmp = 0.15 + noiseStrength * 0.65;
                    p += curlVec * warpAmp;
                }

                float n0 = ValueNoise3D(p);
                float n1 = ValueNoise3D(p * 2.03 + float3(17.13, 31.77, 47.11));
                return saturate(lerp(n0, n1, 0.42));
            }

            float EvalLidarShowHideVisibleMask(int mode, float progress, float dist, float maxRadius, float trailWidth,
                float noiseSigned, float noise01, float noiseStrength)
            {
                trailWidth = max(trailWidth, 1e-5);
                progress = saturate(progress);
                float progressExpand = EaseInOutQuad(progress);
                float radius = progressExpand * (maxRadius + trailWidth);
                float edgeDist = dist - radius;

                float passed0 = saturate((-edgeDist) / trailWidth);
                float noiseWeight0 = (mode == 1) ? (1.0 - passed0) : passed0;

                // show 起始门控:
                // - 让 LiDAR 的边界抖动(jitter)也从 0 开始,避免 show 初期因为 jitter 下限导致“固定半径漏出”.
                // - 仅影响 mode==1(show). hide 保持原有下限,避免 trailWidth 很小时 noise 完全不可见.
                float showWidthScale = 1.0;
                if (mode == 1)
                {
                    const float kShowSizeRampEnd = 0.16;
                    const float kShowStartWidthScale = 0.0;
                    float showSizeRamp = smoothstep(0.0, kShowSizeRampEnd, progress);
                    showWidthScale = lerp(kShowStartWidthScale, 1.0, showSizeRamp);
                }

                float jitterBase = max(trailWidth * 0.75, maxRadius * 0.015 * showWidthScale);
                float noiseWeightJitter = lerp(0.35, 1.0, noiseWeight0);
                float jitter = noiseStrength * jitterBase;
                float edgeDistNoisy = edgeDist + noiseSigned * jitter * noiseWeightJitter;
                float edgeDistForFade = edgeDistNoisy;
                if (mode == 2)
                {
                    float noiseSignedIn = min(noiseSigned, 0.0);
                    edgeDistForFade = edgeDist + noiseSignedIn * jitter * noiseWeightJitter;
                }

                float passed = saturate((-edgeDistForFade) / trailWidth);
                float passedForFade = (mode == 2) ? (passed * passed) : passed;
                float visible = (mode == 1) ? passed : (1.0 - passedForFade);
                float noiseWeight = (mode == 1) ? (1.0 - passed) : passed;
                float ashMul = saturate(1.0 - noiseStrength * noiseWeight * (1.0 - noise01));
                visible *= ashMul;
                return saturate(visible);
            }

            float EvalLidarShowHideRingMask(int mode, float progress, float dist, float maxRadius, float ringWidth, float trailWidth,
                float noiseSigned,
                float noiseStrength)
            {
                ringWidth = max(ringWidth, 1e-5);
                trailWidth = max(trailWidth, 1e-5);
                progress = saturate(progress);
                float progressExpand = EaseInOutQuad(progress);
                float radius = progressExpand * (maxRadius + trailWidth);
                float edgeDist = dist - radius;
                float passed0 = saturate((-edgeDist) / trailWidth);
                float noiseWeight0 = (mode == 1) ? (1.0 - passed0) : passed0;

                // show 起始门控(同 EvalLidarShowHideVisibleMask):
                // - jitter 下限也要从 0 开始,避免 show 初期 ring 被噪声抖出“固定半径”.
                float showWidthScale = 1.0;
                if (mode == 1)
                {
                    const float kShowSizeRampEnd = 0.16;
                    const float kShowStartWidthScale = 0.0;
                    float showSizeRamp = smoothstep(0.0, kShowSizeRampEnd, progress);
                    showWidthScale = lerp(kShowStartWidthScale, 1.0, showSizeRamp);
                }

                float jitterBase = max(trailWidth * 0.75, maxRadius * 0.015 * showWidthScale);
                float noiseWeight = lerp(0.35, 1.0, noiseWeight0);
                float jitter = noiseStrength * jitterBase;
                float edgeDistNoisy = edgeDist + noiseSigned * jitter * noiseWeight;
                float ringOut = 1.0 - saturate(edgeDistNoisy / ringWidth);
                ringOut *= step(0.0, edgeDistNoisy);
                return smoothstep(0.0, 1.0, ringOut);
            }

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
                o.showHideMul = 1.0;
                o.showHideGlow = 0.0;
                o.range = 0.0;

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
                // - `Depth -> SplatColor` 通过 `_LidarColorBlend` 做平滑过渡,避免枚举切换硬跳变.
                float denom = max(_LidarDepthFar - _LidarDepthNear, 1e-6);
                float depth01 = saturate((range - _LidarDepthNear) / denom);
                float3 depthRgb = DepthToCyanRed(depth01);
                float3 splatRgb = depthRgb;

                float colorBlend = saturate(_LidarColorBlend);
                if (colorBlend > 1.0e-4)
                {
                    uint splatId = _LidarMinSplatId[cellId];
                    if (splatId != kLidarInvalidId)
                    {
                        float4 sh0 = _ColorBuffer[splatId];
                        splatRgb = sh0.rgb * SH_C0 + 0.5;
                    }
                }
                float3 baseRgb = lerp(depthRgb, splatRgb, colorBlend);

                // show/hide overlay:
                // - 让 RadarScan 在 Show/Hide 时也具备与 ParticleDots 接近的可见性语义.
                float showHideMul = saturate(_LidarShowHideGate);
                float showHideGlow = 0.0;
                if (showHideMul > 0.0)
                {
                    int mode = _LidarShowHideMode;
                    if (mode == 1 || mode == 2)
                    {
                        float progress = saturate(_LidarShowHideProgress);
                        float maxRadius = max(_LidarShowHideMaxRadius, 1e-5);
                        float trailWidth = max(_LidarShowHideTrailWidth, 1e-5);
                        float ringWidth = max(_LidarShowHideRingWidth, 1e-5);

                        // show: 起始时刻(progress=0)必须完全不可见.
                        // 说明: LiDAR 存在 jitter 下限,如果不显式门控,progress=0 时可能“漏出”一小圈点,
                        // 观感像是突然弹出一个有尺寸的球形范围.
                        if (mode == 1 && progress <= 0.0)
                        {
                            showHideMul = 0.0;
                            showHideGlow = 0.0;
                        }

                        // show 起始“几何尺寸门控”(对齐 splat 的 show 体验):
                        // - 当 radius 很小时,ringWidth/trailWidth 仍是常量会导致早期可见 band 过厚,
                        //   观感像“突然出现一个球壳”.
                        // - 这里让 width 从 0 平滑放大到正常值,让可见范围真正从 0 开始长大.
                        float showWidthScale = 1.0;
                        if (mode == 1)
                        {
                            const float kShowSizeRampEnd = 0.16;
                            const float kShowStartWidthScale = 0.0;
                            float showSizeRamp = smoothstep(0.0, kShowSizeRampEnd, progress);
                            showWidthScale = lerp(kShowStartWidthScale, 1.0, showSizeRamp);
                            trailWidth = max(trailWidth * showWidthScale, 1e-5);
                            ringWidth = max(ringWidth * showWidthScale, 1e-5);
                        }

                        // 与 splat hide 保持一致: 起始宽度从很小开始.
                        if (mode == 2)
                        {
                            const float kHideSizeRampEnd = 0.16;
                            const float kHideStartWidthScale = 0.04;
                            float hideSizeRamp = smoothstep(0.0, kHideSizeRampEnd, progress);
                            float widthScale = lerp(kHideStartWidthScale, 1.0, hideSizeRamp);
                            trailWidth = max(trailWidth * widthScale, 1e-5);
                            ringWidth = max(ringWidth * widthScale, 1e-5);
                        }

                        float3 modelPos = mul(_LidarMatrixW2M, float4(worldPos, 1.0)).xyz;
                        float3 deltaModel = modelPos - _LidarShowHideCenterModel;
                        float distModel = length(deltaModel);
                        float noiseStrength = saturate(_LidarShowHideNoiseStrength);
                        // 说明:
                        // - 用户侧常用的 NoiseStrength 往往在 0.2..0.5 区间.
                        // - 若直接线性使用,屏幕空间位移会非常小,肉眼几乎看不出来.
                        // - 这里用 sqrt 做轻微“提亮”,让中等强度更可感知,但强度=1 仍保持 1.
                        float noiseStrengthVis = sqrt(max(noiseStrength, 0.0));
                        float noise01 = 0.5;
                        float noiseSigned = 0.0;
                        float tNoise = 0.0;
                        if (noiseStrength > 1.0e-4)
                        {
                            // show/hide 的噪声相位统一使用 lidar realtime,保证雷达与 splat 观感节奏一致.
                            tNoise = _LidarTime * _LidarShowHideNoiseSpeed;
                            noise01 = EvalLidarShowHideNoise01(modelPos, tNoise);
                            noiseSigned = noise01 * 2.0 - 1.0;
                        }

                        // ------------------------------------------------------------
                        // show/hide mask + glow(对齐高斯 show/hide 的关键语义):
                        // - alphaMask 必须包含 ring,否则 show 阶段 ring 位于“外侧”会被 early-out 丢弃,
                        //   用户会感觉 show 的 glow 完全看不到.
                        // - hide glow 不能只有一条薄 ring,必须有内侧 afterglow tail,否则会显得“走得太快”.
                        //
                        // 这里直接复用高斯的数学结构:
                        // 1) edgeDist/radius/passed 统一定义.
                        // 2) ring 在外侧(edgeDist>=0).
                        // 3) visible 在内侧(edgeDist<=0)逐步从 0->1(show) 或 1->0(hide).
                        // 4) tailInside 在内侧,用于 afterglow.
                        // 5) alphaPrimary = max(visible, ring),并在 show 时允许 tailInside 提供受限 alpha 下限.
                        // ------------------------------------------------------------
                        float progressExpand = EaseInOutQuad(progress);
                        float radiusShowHide = progressExpand * (maxRadius + trailWidth);
                        float edgeDist = distModel - radiusShowHide;

                        // passed0: 粗略进度(不含噪声),用于决定 jitter 权重,避免递归依赖.
                        float passed0 = saturate((-edgeDist) / trailWidth);
                        float noiseWeight0 = (mode == 1) ? (1.0 - passed0) : passed0;

                        // 边界扰动:
                        // - LiDAR 的点云密度更低,这里为 jitter 提供一个更稳态的距离尺度下限(绑定 maxRadius),
                        //   避免 trailWidth 很小时噪声几乎不可见.
                        // - 但在 show 初期,如果不门控这个下限,会导致边界被抖出一个“固定半径的漏出球”.
                        //   因此让 maxRadius 下限也乘上 showWidthScale(仅 show 早期会 <1).
                        float jitterBase = max(trailWidth * 0.75, maxRadius * 0.015 * showWidthScale);
                        float noiseWeightJitter = lerp(0.35, 1.0, noiseWeight0);
                        float jitter = noiseStrength * jitterBase;
                        float edgeDistNoisy = edgeDist + noiseSigned * jitter * noiseWeightJitter;

                        // hide 防 lingering:
                        // - fade/shrink 不允许噪声把边界往外推,确保 passed 能达到 1.
                        float edgeDistForFade = edgeDistNoisy;
                        if (mode == 2)
                        {
                            float noiseSignedIn = min(noiseSigned, 0.0);
                            edgeDistForFade = edgeDist + noiseSignedIn * jitter * noiseWeightJitter;
                        }

                        // ring: 只在外侧(edgeDistNoisy>=0)出现.
                        float ringOut = 1.0 - saturate(edgeDistNoisy / ringWidth);
                        ringOut *= step(0.0, edgeDistNoisy);
                        float ring = smoothstep(0.0, 1.0, ringOut);

                        // visible: 只描述“可见度”,不包含 ring/tail.
                        float passedForMask = saturate((-edgeDistForFade) / trailWidth);
                        float passedForFade = (mode == 2) ? (passedForMask * passedForMask) : passedForMask;
                        float visible = (mode == 1) ? passedForMask : (1.0 - passedForFade);
                        float noiseWeight = (mode == 1) ? (1.0 - passedForMask) : passedForMask;
                        float ashMul = saturate(1.0 - noiseStrength * noiseWeight * (1.0 - noise01));
                        visible = saturate(visible * ashMul);

                        // 内侧 afterglow/tail:
                        // - 只出现在边界内侧(edgeDistNoisy<=0),并朝内逐渐衰减.
                        // - 乘以(1-ring)避免前沿过曝,保持“ring 永远更亮”的对比.
                        float tailInside = (1.0 - passedForFade) * step(0.0, -edgeDistNoisy);
                        tailInside *= (1.0 - ring);

                        // primary alphaMask:
                        // - ring 必须可见(尤其 show 阶段).
                        float alphaPrimary = max(visible, ring);
                        if (mode == 1)
                        {
                            // show: 允许 tailInside 提供受限 alpha 下限,避免 glow 被 alpha 吃掉.
                            float tailAlpha = tailInside * tailInside;
                            tailAlpha *= 0.45;
                            alphaPrimary = max(alphaPrimary, tailAlpha);
                        }

                        // 叠加 source mask:
                        // - show: max(source, primary),保留 hide 中断前已可见的区域.
                        // - hide: source * primary,避免 show 中断后外圈整片弹出.
                        int sourceMode = _LidarShowHideSourceMaskMode;
                        if (sourceMode < 1 || sourceMode > 4)
                            sourceMode = 1;
                        float sourceProgress = saturate(_LidarShowHideSourceMaskProgress);
                        float sourceMask = 1.0;
                        if (sourceMode == 2)
                        {
                            sourceMask = 0.0;
                        }
                        else if (sourceMode == 3)
                        {
                            sourceMask = EvalLidarShowHideVisibleMask(
                                1, sourceProgress, distModel, maxRadius, trailWidth, noiseSigned, noise01, noiseStrength);
                        }
                        else if (sourceMode == 4)
                        {
                            sourceMask = EvalLidarShowHideVisibleMask(
                                2, sourceProgress, distModel, maxRadius, trailWidth, noiseSigned, noise01, noiseStrength);
                        }

                        if (mode == 1)
                            showHideMul *= max(alphaPrimary, sourceMask);
                        else
                            showHideMul *= alphaPrimary * sourceMask;

                        // show/hide glowFactor:
                        // - ring+tail 组合,让 hide 的 glow 更持久,show 的 glow 更容易被看见.
                        float glowNoiseMul = lerp(1.0, lerp(0.45, 1.0, noise01), noiseStrengthVis);
                        float ringGlow = ring * glowNoiseMul;
                        float tailGlow = pow(tailInside, 0.5);
                        if (mode == 1)
                        {
                            float showBoost = lerp(1.35, 1.0, progressExpand);
                            showHideGlow = ringGlow * showBoost + tailGlow * showBoost * 0.85;
                        }
                        else
                        {
                            showHideGlow = ringGlow * 1.25 + tailGlow;
                        }

                        // 与 ParticleDots 对齐的“粒子噪声位移感”:
                        // - 只在 show/hide 过渡期间增强前沿附近点的屏幕空间位移抖动.
                        // - 目的: 不仅是边界明暗噪声,还要有明显的“颗粒扰动”观感.
                        if (noiseStrength > 1.0e-4)
                        {
                            float passed = saturate((-edgeDist) / max(trailWidth, 1.0e-5));
                            float edgeWeight = (mode == 1) ? (1.0 - passed) : passed;
                            float globalWarp = (mode == 1) ? (1.0 - progressExpand) : progressExpand;
                            globalWarp = smoothstep(0.0, 1.0, globalWarp);
                            float warpWeight = saturate(edgeWeight + ring * 0.40 + globalWarp * 0.45);

                            // 位移尺度(屏幕像素):
                            // - 由用户显式调参 `_LidarShowHideWarpPixels` 控制.
                            // - 目的: 把“能否看见噪声扰动”从“点大小/资产尺度”里解耦出来.
                            float warpStrength = clamp(_LidarShowHideWarpStrength, 0.0, 3.0);
                            // WarpStrength 的默认值是 2,这里把它归一化到“默认倍率=1”.
                            float warpStrengthMul = warpStrength * 0.5;
                            float warpPx = noiseStrengthVis * warpWeight * max(_LidarShowHideWarpPixels, 0.0) * warpStrengthMul;

                            // 当 warpPx 极小时直接跳过,避免浪费 curl/noise 计算.
                            if (warpPx > 1.0e-5)
                            {
                                float2 warpDir = float2(0.0, 1.0);
                                int noiseMode = _LidarShowHideNoiseMode;
                                if (noiseMode == 1)
                                {
                                    // CurlSmoke:
                                    // - 用 curl-like 向量场决定位移方向,让抖动更像“流动/旋涡”.
                                    float3 tVec = float3(tNoise, tNoise * 1.37, tNoise * 1.93);
                                    float scale = max(_LidarShowHideNoiseScale, 0.0);
                                    float3 pBase = modelPos * scale;
                                    float3 curlVec = EvalCurlNoise(pBase * 0.85 + tVec * 1.70 + float3(101.17, 17.31, 9.77));

                                    // 让 curl 更偏向切向(围绕中心转),减少把点推离/推近中心的“径向拉扯”感.
                                    if (distModel > 1.0e-5)
                                    {
                                        float3 radial = deltaModel / distModel;
                                        curlVec = curlVec - radial * dot(curlVec, radial);

                                        float3 axis = (abs(radial.y) < 0.99) ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                                        float3 tangent = normalize(cross(axis, radial));
                                        float3 bitangent = cross(radial, tangent);

                                        float2 dir2 = float2(dot(curlVec, tangent), dot(curlVec, bitangent));
                                        warpDir = normalize(dir2 + float2(1.0e-4, -1.0e-4));
                                    }
                                    else
                                    {
                                        float2 dir2 = float2(curlVec.x, curlVec.z);
                                        warpDir = normalize(dir2 + float2(1.0e-4, -1.0e-4));
                                    }
                                }
                                else
                                {
                                    float warp01a = EvalLidarShowHideNoise01(modelPos + float3(17.13, 31.77, 47.11),
                                        tNoise * 1.37 + 0.73);
                                    float warp01b = EvalLidarShowHideNoise01(modelPos + float3(53.11, 12.77, 9.71),
                                        tNoise * 1.91 + 1.19);
                                    float2 dir2 = float2(warp01a * 2.0 - 1.0, warp01b * 2.0 - 1.0);
                                    warpDir = normalize(dir2 + float2(1.0e-4, -1.0e-4));
                                }

                                // 让位移的最小幅度不要太小(否则用户会觉得“还是没噪声”).
                                float warpNoiseMul = lerp(0.65, 1.0, abs(noiseSigned));
                                float2 warpOffset = warpDir * (warpPx * warpNoiseMul) * (proj.w / _ScreenParams.xy);
                                proj.xy += warpOffset;
                            }
                        }
                    }
                }

                if (showHideMul <= 0.0)
                {
                    o.vertex = float4(0.0, 0.0, 2.0, 1.0);
                    return o;
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
                o.showHideMul = showHideMul;
                o.showHideGlow = showHideGlow;
                o.range = range;
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
                // - UnscannedIntensity 用于避免“扫过后变黑”:
                //   - 扫描头未扫到(或远离扫描头)的区域仍保留一层底色亮度.
                float trail = saturate(i.trail01);
                float dist = max(i.range, 0.0);
                float scanDecay = max(_LidarIntensityDistanceDecay, 0.0);
                float unscannedDecay = max(_LidarUnscannedIntensityDistanceDecay, 0.0);
                float decayMode01 = saturate(_LidarIntensityDistanceDecayMode);

                // 距离衰减函数:
                // - Reciprocal: atten(dist)=1/(1+dist*decay) (数值稳定,兼容旧项目默认)
                // - Exponential: atten(dist)=exp(-dist*decay) (指数衰减,近强远弱更明显)
                float scanAttenReciprocal = rcp(1.0 + dist * scanDecay);
                float unscannedAttenReciprocal = rcp(1.0 + dist * unscannedDecay);

                // exp(x)=exp2(x/ln(2)),这里用 exp2 做指数衰减(通常更友好).
                const float kInvLn2 = 1.4426950408889634; // 1/ln(2)
                float scanAttenExponential = exp2(-dist * scanDecay * kInvLn2);
                float unscannedAttenExponential = exp2(-dist * unscannedDecay * kInvLn2);

                float scanAtten = lerp(scanAttenReciprocal, scanAttenExponential, decayMode01);
                float unscannedAtten = lerp(unscannedAttenReciprocal, unscannedAttenExponential, decayMode01);

                float scanIntensity = max(_LidarIntensity, 0.0) * scanAtten;
                float unscannedIntensity = max(_LidarUnscannedIntensity, 0.0) * unscannedAtten;
                float intensity = lerp(unscannedIntensity, scanIntensity, trail);
                float colorBlend = saturate(_LidarColorBlend);
                float visibility = saturate(_LidarVisibility);
                // DepthOpacity:
                // - 当颜色从 Depth 向 SplatColor 过渡时,opacity 也同步从 `LidarDepthOpacity` 平滑过渡到 1.
                float depthOpacity = lerp(saturate(_LidarDepthOpacity), 1.0, colorBlend);
                float showHideMul = saturate(i.showHideMul);
                float alpha = saturate(alphaShape * depthOpacity * visibility * showHideMul);

                // 余辉只影响亮度,不影响 alpha,避免在浅色底图上出现“透明发灰”.
                // show/hide glow(对齐高斯的 additive glow):
                // - 这里用 glowColor 做 RGB 叠加,而不是仅仅提亮 brightness.
                // - glow 强度随 show/hide ring/tail(glowFactor)变化.
                // - 关键: discard 判断必须考虑 glowAdd,否则 show 阶段(尚未被扫描头扫到,trail 很小)会被直接丢弃,
                //   看起来就像“show glow 完全没生效”.
                float glowFactor = saturate(i.showHideGlow);
                float glowIntensity = max(_LidarShowHideGlowIntensity, 0.0);

                // glow 的 trail 衰减:
                // - 不能完全跟随 scan trail,否则 show 阶段大部分点都会太暗,用户感觉“看不出 glow”.
                // - 这里给一个下限,让 glow 在未被扫描头扫到时也仍可见.
                float trailForGlow = lerp(0.35, 1.0, trail);
                float3 glowAdd = _LidarShowHideGlowColor.rgb * glowFactor * glowIntensity * (intensity * visibility) * trailForGlow;

                float brightness = intensity * visibility * showHideMul;
                brightness *= (1.0 + glowFactor * 0.85);

                float3 rgb = i.rgb * brightness + glowAdd;

                float maxRgb = max(rgb.x, max(rgb.y, rgb.z));
                if (maxRgb * alpha < 1.0 / 255.0) discard;
                if (_GammaToLinear)
                    return float4(GammaToLinearSpace(rgb), alpha);
                return float4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
