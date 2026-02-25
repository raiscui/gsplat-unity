// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

Shader "Gsplat/Standard"
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
            ZWrite Off
            Blend One OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require compute
            #pragma multi_compile SH_BANDS_0 SH_BANDS_1 SH_BANDS_2 SH_BANDS_3

            #include "UnityCG.cginc"
            #include "Gsplat.hlsl"
            bool _GammaToLinear;
            int _SplatCount;
            int _SplatInstanceSize;
            uint _SplatBaseIndex;
            int _SHDegree;
            int _Has4D;
            float _TimeNormalized;
            int _TimeModel; // 1=window(time0+duration), 2=gaussian(mu+sigma)
            float _TemporalCutoff; // gaussian cutoff,例如 0.01
            float4x4 _MATRIX_M;

            // ----------------------------------------------------------------
            // 可选: 显隐燃烧环动画(uniform 驱动,默认关闭)
            // - _VisibilityMode=0: 完全禁用,保持旧行为与性能.
            // - _VisibilityMode=1: show,从中心向外扩散 reveal.
            // - _VisibilityMode=2: hide,从中心向外扩散 burn-out(更亮,噪波更碎屑).
            // ----------------------------------------------------------------
            int _VisibilityMode;
            float _VisibilityProgress; // 0..1
            float3 _VisibilityCenterModel; // model space
            float _VisibilityMaxRadius; // model space
            float _VisibilityRingWidth; // model space
            float _VisibilityTrailWidth; // model space
            float4 _VisibilityGlowColor; // rgb used
            float _VisibilityGlowIntensity;
            float _VisibilityHideGlowStartBoost;
            float _VisibilityNoiseStrength; // 0..1
            float _VisibilityNoiseScale;
            float _VisibilityNoiseSpeed;
            float _VisibilityWarpStrength; // 0..3
            float _VisibilityTime;

            StructuredBuffer<uint> _OrderBuffer;
            StructuredBuffer<float3> _PositionBuffer;
            StructuredBuffer<float3> _VelocityBuffer;
            StructuredBuffer<float> _TimeBuffer;
            StructuredBuffer<float> _DurationBuffer;
            StructuredBuffer<float3> _ScaleBuffer;
            StructuredBuffer<float4> _RotationBuffer;
            StructuredBuffer<float4> _ColorBuffer;

            #ifndef SH_BANDS_0
            StructuredBuffer<float3> _SHBuffer;
            #endif

            struct appdata
            {
                float4 vertex : POSITION;
                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_STEREO_INSTANCING_ENABLED)
                uint instanceID : SV_InstanceID;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            bool InitSource(appdata v, out SplatSource source)
            {
                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_STEREO_INSTANCING_ENABLED)
                source.order = v.instanceID * _SplatInstanceSize + asuint(v.vertex.z);
                #else
                source.order = unity_InstanceID * _SplatInstanceSize + asuint(v.vertex.z);
                #endif

                // HLSL(d3d11) 会对 signed/unsigned 混用给出编译错误,这里显式 cast 以消除歧义.
                if (source.order >= (uint)_SplatCount)
                    return false;

                // 子范围渲染:
                // - OrderBuffer 内存的是 local index(0..SplatCount-1).
                // - 通过 baseIndex 把它映射回全量 buffers 的 absolute splatId.
                source.id = _SplatBaseIndex + _OrderBuffer[source.order];
                source.cornerUV = float2(v.vertex.x, v.vertex.y);
                return true;
            }

            bool InitCenter(float3 modelCenter, out SplatCenter center)
            {
                // 兼容部分平台/编译器(d3d11)的“out 参数必须在所有路径初始化”要求.
                center.view = float3(0.0, 0.0, 0.0);
                center.proj = float4(0.0, 0.0, 0.0, 0.0);
                center.projMat00 = 0.0;
                center.modelView = 0;

                float4x4 modelView = mul(UNITY_MATRIX_V, _MATRIX_M);
                float4 centerView = mul(modelView, float4(modelCenter, 1.0));
                if (centerView.z > 0.0)
                {
                    return false;
                }
                float4 centerProj = mul(UNITY_MATRIX_P, centerView);
                centerProj.z = clamp(centerProj.z, -abs(centerProj.w), abs(centerProj.w));
                center.view = centerView.xyz / centerView.w;
                center.proj = centerProj;
                center.projMat00 = UNITY_MATRIX_P[0][0];
                center.modelView = modelView;
                return true;
            }

            // sample covariance vectors
            SplatCovariance ReadCovariance(SplatSource source)
            {
                float4 quat = _RotationBuffer[source.id];
                float3 scale = _ScaleBuffer[source.id];
                return CalcCovariance(quat, scale);
            }

            // ----------------------------------------------------------------
            // Easing: easeInOutQuad
            // - 用于控制 burn reveal 的“环扩散速度曲线”.
            //
            // 期望观感:
            // - 起始阶段更慢(更像“点燃/聚能”).
            // - 中段更快(扩散更明显).
            // - 末尾减速(更自然收尾,减少最后一瞬间的突兀感).
            //
            // 标准定义:
            // - easeInOutQuad(t) = 2t^2,                 t < 0.5
            //                   = 1 - ((-2t + 2)^2)/2,   t >= 0.5
            // ----------------------------------------------------------------
            float EaseInOutQuad(float t)
            {
                t = saturate(t);
                if (t < 0.5)
                    return 2.0 * t * t;

                float u = -2.0 * t + 2.0;
                return 1.0 - (u * u) * 0.5;
            }

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color: COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                SplatSource source;
                if (!InitSource(v, source))
                {
                    o.vertex = discardVec;
                    return o;
                }

                float temporalWeight = 1.0;
                float3 modelCenter = _PositionBuffer[source.id];
                if (_Has4D != 0)
                {
                    float t0 = _TimeBuffer[source.id];
                    float dt = _DurationBuffer[source.id];
                    float t = _TimeNormalized;
                    if (_TimeModel == 1)
                    {
                        // window: 时间窗外直接硬裁剪,不产生任何颜色贡献.
                        if (t < t0 || t > t0 + dt)
                        {
                            o.vertex = discardVec;
                            return o;
                        }
                    }
                    else
                    {
                        // gaussian: t0=mu, dt=sigma
                        float sigma = max(dt, 1e-6);
                        float x = (t - t0) / sigma;
                        temporalWeight = exp(-0.5 * x * x);
                        if (temporalWeight < _TemporalCutoff)
                        {
                            o.vertex = discardVec;
                            return o;
                        }
                    }

                    float3 vel = _VelocityBuffer[source.id];
                    modelCenter = modelCenter + vel * (t - t0);
                }

                // ------------------------------------------------------------
                // 可选: 显隐燃烧环动画
                // - 在 InitCenter 之前计算,便于早退 discard(省后续 SH/协方差等计算).
                // - 注意:
                //   1) ClipCorner 仍基于 baseAlpha,避免 alphaMask 影响几何尺寸导致“剧烈闪烁”.
                //   2) 但在 show/hide 期间,我们允许:
                //      - 对 modelCenter 做一次空间扭曲(让 noise 产生“扭曲粒子”的位移效果).
                //      - 对 corner.offset 做一次缩放(让 splat 从极小->正常,或正常->极小).
                // ------------------------------------------------------------
                float visibilityAlphaMask = 1.0;
                float3 visibilityGlowAdd = float3(0.0, 0.0, 0.0);
                float visibilitySizeMul = 1.0;
                float3 modelCenterBase = modelCenter;
                if (_VisibilityMode != 0)
                {
                    float progress = saturate(_VisibilityProgress);
                    // 燃烧环扩散的速度曲线(用户期望 easeInOutQuad).
                    float progressExpand = EaseInOutQuad(progress);
                    float3 centerModel = _VisibilityCenterModel;
                    float3 delta = modelCenterBase - centerModel;
                    float dist = length(delta);

                    float trailWidth = max(_VisibilityTrailWidth, 1e-5);
                    float ringWidth = max(_VisibilityRingWidth, 1e-5);

                    // show: 起始时刻(progress=0)必须完全不可见.
                    // 说明: 即便 ringWidth/noise 存在,也不能在 progress=0 时提前“漏出”任何 splat.
                    if (_VisibilityMode == 1 && progress <= 0.0)
                    {
                        o.vertex = discardVec;
                        return o;
                    }

                    // 重要: radius 的终点要“超出对象边界”一段 trailWidth,
                    // 这样 progress=1 时,对象边缘也能完成 reveal/burn,避免最后一瞬间 pop.
                    float maxRadius = max(_VisibilityMaxRadius, 1e-5);
                    float radius = progressExpand * (maxRadius + trailWidth);
                    float edgeDist = dist - radius;

                    // smoke-like noise(更平滑,更像烟雾的“扭曲+波动”):
                    // - 以 modelCenterBase(xyz) + time 为输入,构造连续变化的噪声场.
                    // - per-splat 只做很小的相位偏移(改时间相位,不破坏空间连续性),避免整片区域同步抖动.
                    float tNoise = _VisibilityTime * _VisibilityNoiseSpeed;
                    float3 tNoiseVec = float3(tNoise, tNoise * 1.37, tNoise * 1.93);

                    // 注意:
                    // - 这里把 NoiseScale 做一个内建降频(0.25),避免默认参数下噪波过碎导致“很混乱”.
                    float3 smokePos = modelCenterBase * (_VisibilityNoiseScale * 0.25) + tNoiseVec;

                    float base01;
                    float baseSigned;
                    GsplatEvalValueNoise01(smokePos, base01, baseSigned);

                    // 轻量 domain warp(用一个噪声去扭曲采样域),更像烟雾的团簇与流动.
                    float baseSignedZ = (baseSigned + (base01 * 2.0 - 1.0)) * 0.5;
                    float3 smokeDomainWarp = float3(baseSigned, base01 * 2.0 - 1.0, baseSignedZ) * (_VisibilityNoiseStrength * 0.65);
                    float3 smokePosWarp = smokePos + smokeDomainWarp;

                    // 使用“warp 后”的噪声作为最终噪声源:
                    // - jitter/ash/warpVec 都共享同一个连续场,观感更像烟雾而不是随机抖动.
                    float warp01a;
                    float warpSignedA;
                    GsplatEvalValueNoise01(smokePosWarp + float3(17.13, 31.77, 47.11), warp01a, warpSignedA);

                    float warp01b;
                    float warpSignedB;
                    GsplatEvalValueNoise01(smokePosWarp + float3(53.11, 12.77, 9.71), warp01b, warpSignedB);

                    float noise01 = warp01a;
                    float noiseSigned = warpSignedA;

                    // 先基于无噪声 edgeDist 推一个粗略 passed,再用它决定噪波权重(避免递归依赖).
                    float passed0 = saturate((-edgeDist) / trailWidth);
                    float noiseWeight0 = (_VisibilityMode == 1) ? (1.0 - passed0) : passed0;

                    // 边界扰动: jitter 的距离尺度绑定 trailWidth,让参数更直觉.
                    float jitter = _VisibilityNoiseStrength * trailWidth * 0.75;
                    float edgeDistNoisy = edgeDist + noiseSigned * jitter * noiseWeight0;

                    // 燃烧环(边缘): ring=1 表示正在燃烧边界,0 表示远离边界.
                    //
                    // 视觉语义:
                    // - show: ring 允许在边界两侧都有一点宽度(更像“发光边缘”).
                    // - hide: ring 更像“燃烧前沿”,应主要出现在未燃烧侧(外侧),
                    //   这样 trail(渐隐)会更自然地落在内侧(已燃烧区域),避免体感“trail 在外”.
                    float ring;
                    if (_VisibilityMode == 2)
                    {
                        // hide: ring 只在外侧(edgeDistNoisy>=0)出现.
                        float ringOut = 1.0 - saturate(edgeDistNoisy / ringWidth);
                        ringOut *= step(0.0, edgeDistNoisy);
                        ring = smoothstep(0.0, 1.0, ringOut);
                    }
                    else
                    {
                        // show: 边界两侧都可见一点宽度.
                        ring = 1.0 - saturate(abs(edgeDistNoisy) / ringWidth);
                        ring = smoothstep(0.0, 1.0, ring);
                    }

                    // passed=1 表示“燃烧环已扫过该 splat”,passed=0 表示“尚未到达”.
                    float passed = saturate((-edgeDistNoisy) / trailWidth);
                    float visible = (_VisibilityMode == 1) ? passed : (1.0 - passed);
                    float noiseWeight = (_VisibilityMode == 1) ? (1.0 - passed) : passed;

                    // 灰烬颗粒感: hide 后半程更强.
                    float ashMul = saturate(1.0 - _VisibilityNoiseStrength * noiseWeight * (1.0 - noise01));
                    visible *= ashMul;

                    // alphaMask: ring 本身必须可见(发光边缘),因此与 visible 取 max.
                    visibilityAlphaMask = max(visible, ring);

                    // 全不可见时直接早退,避免后续计算.
                    if (visibilityAlphaMask <= 0.0)
                    {
                        o.vertex = discardVec;
                        return o;
                    }

                    float glowIntensity = _VisibilityGlowIntensity;
                    if (_VisibilityMode == 2)
                    {
                        // hide: 起始更亮,随后衰减到 1x.
                        glowIntensity *= lerp(_VisibilityHideGlowStartBoost, 1.0, progressExpand);
                    }
                    visibilityGlowAdd = _VisibilityGlowColor.rgb * ring * glowIntensity;

                    // --------------------------------------------------------
                    // 大小变化:
                    // - show: splat 从“极小”逐步长到正常大小.
                    // - hide: splat 从正常大小逐步缩到“极小”.
                    //
                    // 设计:
                    // - 用 passed 作为每个 splat 的局部进度,让“被燃烧环扫过”的区域自然变大/变小.
                    // - show 时设置一个很小的 minScale,避免 progress 刚开始时 ring 过于不可见.
                    // --------------------------------------------------------
                    float sizeT = (_VisibilityMode == 1) ? passed : (1.0 - passed);
                    // 用更“慢一点”的 easing,让 grow/shrink 更容易被肉眼感知:
                    // - show: 刚出现时更小,随后更慢地长到正常.
                    // - hide: 更快缩小到极小,更像烧成灰.
                    sizeT = saturate(sizeT);
                    // show: 尺寸变大更快一些,避免 ring 阶段全是很小的点点.
                    // hide: shrink 更强,更像“烧成灰”.
                    float sizePow = (_VisibilityMode == 1) ? 0.5 : 4.0;
                    sizeT = pow(sizeT, sizePow);
                    sizeT = smoothstep(0.0, 1.0, sizeT);
                    float minScale = (_VisibilityMode == 1) ? 0.01 : 0.0;
                    visibilitySizeMul = lerp(minScale, 1.0, sizeT);
                    if (_VisibilityMode == 2)
                    {
                        // hide 额外叠加一个 global shrink:
                        // - 让“尚未被燃烧环扫到”的外圈 splats 也会逐渐变小,避免体感一直很大.
                        // - 越接近结束(progress->1),整体越接近 0.
                        float globalShrink = saturate(1.0 - progressExpand);
                        globalShrink = smoothstep(0.0, 1.0, globalShrink);
                        visibilitySizeMul *= globalShrink;
                    }

                    // --------------------------------------------------------
                    // 空间扭曲(位移):
                    // - 让 noise 不只是“alpha 抖动”,而是产生明显的中心位置扭曲(类似扭曲粒子).
                    // - 扭曲强度遵循 show/hide 语义:
                    //   - show: 越稳定越弱(noiseWeight=1-passed).
                    //   - hide: 越烧掉越强(noiseWeight=passed).
                    //
                    // 注意:
                    // - 扭曲不参与 passed/ring 的判定,它们全部基于 modelCenterBase 计算,
                    //   避免阈值抖动导致“闪一下/跳一下”.
                    // --------------------------------------------------------
                    // 全局不稳定度:
                    // - show: 越接近结束越稳定,因此用 (1-progress).
                    // - hide: 越接近结束越“碎”,因此用 progress.
                    float globalWarp = (_VisibilityMode == 1) ? (1.0 - progressExpand) : progressExpand;
                    globalWarp = smoothstep(0.0, 1.0, globalWarp);

                    float warpWeight = saturate(noiseWeight + ring * 0.35 + globalWarp * 0.45);
                    float warpAmp = _VisibilityNoiseStrength * _VisibilityWarpStrength * warpWeight * maxRadius * 0.12;
                    if (warpAmp > 0.0 && dist > 1e-5)
                    {
                        float3 radial = delta / dist;
                        float3 axis = (abs(radial.y) < 0.99) ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                        float3 tangent = normalize(cross(axis, radial));
                        float3 bitangent = cross(radial, tangent);

                        float3 warpVec = tangent * warpSignedA + bitangent * warpSignedB;
                        // 轻微径向分量,让扭曲更像“空间被拉扯”.
                        warpVec += radial * (warp01a * 2.0 - 1.0) * 0.45;
                        warpVec = normalize(warpVec + 1e-5);

                        modelCenter = modelCenterBase + warpVec * warpAmp;
                    }
                }

                SplatCenter center;
                if (!InitCenter(modelCenter, center))
                {
                    o.vertex = discardVec;
                    return o;
                }

                SplatCovariance cov = ReadCovariance(source);
                SplatCorner corner;
                if (!InitCorner(source, cov, center, corner))
                {
                    o.vertex = discardVec;
                    return o;
                }

                float4 color = _ColorBuffer[source.id];
                // gaussian: 把 temporal weight 乘到 opacity 上,实现平滑淡入淡出.
                // window: temporalWeight=1.0,保持旧行为.
                if (_Has4D != 0)
                    color.w *= temporalWeight;
                color.rgb = color.rgb * SH_C0 + 0.5;
                #ifndef SH_BANDS_0
                // calculate the model-space view direction
                float3 dir = normalize(mul(center.view, (float3x3)center.modelView));
                float3 sh[SH_COEFFS];
                for (int i = 0; i < SH_COEFFS; i++)
                    sh[i] = _SHBuffer[source.id * SH_COEFFS + i];
                color.rgb += EvalSH(sh, dir, _SHDegree);
                #endif

                // 显隐动画在 SH 之后叠加:
                // - glow: 加到 rgb 上.
                // - alphaMask: 乘到 alpha 上.
                // 注意: ClipCorner 不应基于“被 mask 后的 alpha”来缩放几何,否则会导致 splat 尺寸随动画剧烈变化.
                // 因此这里先保留 baseAlpha 做 ClipCorner,最后再把 mask 乘到最终输出的 alpha 上.
                float baseAlpha = color.w;
                if (_VisibilityMode != 0)
                {
                    color.rgb += visibilityGlowAdd;
                }

                // ClipCorner 的数学域要求 alpha >= 1/255,否则会出现 NaN.
                // 当 alpha 很小时,最终也会在 fragment 阶段被 discard,因此这里对 ClipCorner 的 alpha 做一个下限 clamp.
                ClipCorner(corner, max(baseAlpha, 1.0 / 255.0));

                // 显隐动画大小变化:
                // - 在 ClipCorner 之后缩放 corner.offset,让几何真正变小/变大.
                // - ClipCorner 仍只看 baseAlpha,避免 alphaMask 影响几何尺寸导致 flicker.
                corner.offset *= visibilitySizeMul;

                float finalAlpha = baseAlpha;
                if (_VisibilityMode != 0)
                {
                    finalAlpha = baseAlpha * visibilityAlphaMask;
                }

                o.vertex = center.proj + float4(corner.offset.x, _ProjectionParams.x * corner.offset.y, 0, 0);
                o.color = float4(max(color.rgb, float3(0, 0, 0)), finalAlpha);
                o.uv = corner.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float A = dot(i.uv, i.uv);
                if (A > 1.0) discard;
                float alpha = exp(-A * 4.0) * i.color.a;
                if (alpha < 1.0 / 255.0) discard;
                if (_GammaToLinear)
                    return float4(GammaToLinearSpace(i.color.rgb) * alpha, alpha);
                return float4(i.color.rgb * alpha, alpha);
            }
            ENDHLSL


        }
    }
}
