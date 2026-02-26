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
            // Render style: Gaussian <-> ParticleDots
            // - `_RenderStyleBlend`:
            //   - 0: Gaussian(旧行为)
            //   - 1: ParticleDots(屏幕空间圆片/圆点)
            //   - (0,1): 单次 draw 的形态渐变(morph)
            // - `_ParticleDotRadiusPixels`: dot 半径(px radius).
            // ----------------------------------------------------------------
            float _RenderStyleBlend;
            float _ParticleDotRadiusPixels;

            // ----------------------------------------------------------------
            // 可选: 显隐燃烧环动画(uniform 驱动,默认关闭)
            // - _VisibilityMode=0: 完全禁用,保持旧行为与性能.
            // - _VisibilityMode=1: show,从中心向外扩散 reveal.
            // - _VisibilityMode=2: hide,从中心向外扩散 burn-out(更亮,噪波更碎屑).
            // ----------------------------------------------------------------
            int _VisibilityMode;
            // 显隐噪声类型:
            // - 0: ValueSmoke(默认,更平滑更像烟雾)
            // - 1: CurlSmoke(更像旋涡/流动,主要用于 position warp)
            // - 2: HashLegacy(旧版对照,更碎更抖)
            int _VisibilityNoiseMode;
            float _VisibilityProgress; // 0..1
            float3 _VisibilityCenterModel; // model space
            float _VisibilityMaxRadius; // model space
            float _VisibilityRingWidth; // model space
            float _VisibilityTrailWidth; // model space
            // 粒子大小(高斯基元尺寸)控制:
            // - 注意: 这不是 ring/trail 的“空间宽度”.
            //   ShowRingWidthNormalized/ShowTrailWidthNormalized 控制的是径向空间宽度,
            //   而这里的 MinScale 控制的是 corner.offset 的缩放(等价于屏幕上 splat 的大小).
            float _VisibilityShowMinScale; // 0..1
            float _VisibilityShowRingMinScale; // 0..1
            float _VisibilityShowTrailMinScale; // 0..1
            float _VisibilityHideMinScale; // 0..1
            float4 _VisibilityGlowColor; // rgb used
            float _VisibilityGlowIntensity;
            float _VisibilityShowGlowStartBoost;
            float _VisibilityShowGlowSparkleStrength;
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

            // ----------------------------------------------------------------
            // Easing: easeOutCirc
            // - 用于 hide 的 size shrink 节奏:
            //   先迅速变小,再更慢地收尾.
            //
            // 标准定义:
            // - easeOutCirc(t) = sqrt(1 - (t-1)^2)
            // ----------------------------------------------------------------
            float EaseOutCirc(float t)
            {
                t = saturate(t);
                float u = t - 1.0;
                return sqrt(saturate(1.0 - u * u));
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
                    float warp01a;
                    float warpSignedA;
                    float warp01b;
                    float warpSignedB;
                    float3 smokePosWarp = smokePos;

                    // 噪声类型切换:
                    // - HashLegacy: 旧版对照,更碎更抖(无 domain warp).
                    // - ValueSmoke/CurlSmoke: 使用更平滑的 value noise + 轻量 domain warp,更像烟雾团簇与流动.
                    if (_VisibilityNoiseMode == 2)
                    {
                        GsplatEvalHashNoise01(smokePos, base01, baseSigned);
                        smokePosWarp = smokePos;
                        GsplatEvalHashNoise01(smokePosWarp + float3(17.13, 31.77, 47.11), warp01a, warpSignedA);
                        GsplatEvalHashNoise01(smokePosWarp + float3(53.11, 12.77, 9.71), warp01b, warpSignedB);
                    }
                    else
                    {
                        GsplatEvalValueNoise01(smokePos, base01, baseSigned);

                        // 轻量 domain warp(用一个噪声去扭曲采样域),更像烟雾的团簇与流动.
                        float baseSignedZ = (baseSigned + (base01 * 2.0 - 1.0)) * 0.5;
                        float3 smokeDomainWarp = float3(baseSigned, base01 * 2.0 - 1.0, baseSignedZ) * (_VisibilityNoiseStrength * 0.65);
                        smokePosWarp = smokePos + smokeDomainWarp;

                        // 使用“warp 后”的噪声作为最终噪声源:
                        // - jitter/ash/warpVec 都共享同一个连续场,观感更像烟雾而不是随机抖动.
                        GsplatEvalValueNoise01(smokePosWarp + float3(17.13, 31.77, 47.11), warp01a, warpSignedA);
                        GsplatEvalValueNoise01(smokePosWarp + float3(53.11, 12.77, 9.71), warp01b, warpSignedB);
                    }

                    float noise01 = warp01a;
                    float noiseSigned = warpSignedA;

                    // 先基于无噪声 edgeDist 推一个粗略 passed,再用它决定噪波权重(避免递归依赖).
                    float passed0 = saturate((-edgeDist) / trailWidth);
                    float noiseWeight0 = (_VisibilityMode == 1) ? (1.0 - passed0) : passed0;

                    // 边界扰动: jitter 的距离尺度绑定 trailWidth,让参数更直觉.
                    float jitter = _VisibilityNoiseStrength * trailWidth * 0.75;
                    float edgeDistNoisy = edgeDist + noiseSigned * jitter * noiseWeight0;

                    // 重要修正(hide 末尾残留):
                    // - hide 的 fade/shrink 如果完全跟随 edgeDistNoisy,当 noiseSigned 为正时会把边界“往外推”,
                    //   导致局部 passed 永远达不到 1,于是动画末尾会出现少量 splats lingering(残留很久才消失).
                    // - 解决思路:
                    //   - ring/glow 仍然使用 edgeDistNoisy,保留“燃烧边界抖动”的质感.
                    //   - 但 hide 的 fade/shrink 使用一个更稳态的 edgeDistForFade:
                    //     只允许噪声把边界往内咬(min(noiseSigned,0)),不允许往外推,确保最终一定能烧尽.
                    float edgeDistForFade = edgeDistNoisy;
                    if (_VisibilityMode == 2)
                    {
                        float noiseSignedIn = min(noiseSigned, 0.0);
                        edgeDistForFade = edgeDist + noiseSignedIn * jitter * noiseWeight0;
                    }

                    // 燃烧环(边缘): ring=1 表示正在燃烧边界,0 表示远离边界.
                    //
                    // 视觉语义(统一为“前沿在外侧,拖尾/余辉在内侧”):
                    // - show/hide: ring 更像“燃烧前沿”,主要出现在未燃烧侧(外侧,edgeDist>=0).
                    //   这样:
                    //   1) 前沿 ring 永远在最外侧先到(更符合“燃烧扩散”的直觉).
                    //   2) 内侧 afterglow/tail 会朝内衰减,内部更亮,外围不突兀.
                    float ring;
                    {
                        // ring 只在外侧(edgeDistNoisy>=0)出现.
                        float ringOut = 1.0 - saturate(edgeDistNoisy / ringWidth);
                        ringOut *= step(0.0, edgeDistNoisy);
                        ring = smoothstep(0.0, 1.0, ringOut);
                    }

                    // passed=1 表示“燃烧环已扫过该 splat”,passed=0 表示“尚未到达”.
                    float passed = saturate((-edgeDistForFade) / trailWidth);

                    // hide 余辉更“拖尾”:
                    // - 用户反馈: glow 一过,余辉粒子几乎就全没了.
                    // - 根因: hide 的 visible/tail 直接用线性(1-passed)衰减时,会显得过快/过短.
                    // - 处理: 对 hide 的 passed 做一个轻量 ease-in(平方),让衰减在前段更慢,尾段更快.
                    //   这样余辉存在时间更长,但 passed=1 时仍能完全烧尽(不引入 lingering).
                    float passedForFade = passed;
                    float passedForTail = passed;
                    if (_VisibilityMode == 2)
                    {
                        passedForFade = passed * passed;
                        passedForTail = passedForFade;
                    }

                    float visible = (_VisibilityMode == 1) ? passed : (1.0 - passedForFade);
                    float noiseWeight = (_VisibilityMode == 1) ? (1.0 - passed) : passed;

                    // 内侧 afterglow/tail:
                    // - 只出现在边界内侧(edgeDistNoisy<=0),并朝内逐渐衰减.
                    // - 乘以(1-ring)避免前沿过曝,并保证“前沿 ring 永远更亮”.
                    float tailInside = (1.0 - passedForTail) * step(0.0, -edgeDistNoisy);
                    tailInside *= (1.0 - ring);

                    // 灰烬颗粒感: hide 后半程更强.
                    float ashMul = saturate(1.0 - _VisibilityNoiseStrength * noiseWeight * (1.0 - noise01));
                    visible *= ashMul;

                    // alphaMask:
                    // - ring 本身必须可见(发光前沿).
                    // - show: 为了让内侧 afterglow 在刚扫过时“内部更亮”且肉眼可见,
                    //   允许 tailInside 提供一个受限的 alpha 下限(否则 premul alpha 下 glow 会被 alpha 吃掉).
                    visibilityAlphaMask = max(visible, ring);
                    if (_VisibilityMode == 1)
                    {
                        float tailAlpha = tailInside * tailInside;
                        tailAlpha *= 0.45;
                        visibilityAlphaMask = max(visibilityAlphaMask, tailAlpha);
                    }

                    // 全不可见时直接早退,避免后续计算.
                    if (visibilityAlphaMask <= 0.0)
                    {
                        o.vertex = discardVec;
                        return o;
                    }

                    float glowIntensity = _VisibilityGlowIntensity;
                    float glowFactor = ring;
                    if (_VisibilityMode == 1)
                    {
                        // show:
                        // - 允许在起始阶段更亮(更像“点燃瞬间”).
                        // - 这里用 eased progress 做一个轻量衰减,避免全程都过曝.
                        float showBoost = lerp(_VisibilityShowGlowStartBoost, 1.0, progressExpand);

                        // show 内侧 afterglow tail:
                        // - 用户反馈“内部不够亮”,因此这里在前沿后方(内侧)增加一段更平滑的余辉.
                        // - tail 只出现在边界内侧(edgeDistNoisy<=0),并朝内逐渐衰减.
                        // - 设计原则: ring 负责“前沿更亮”,tail 负责“内侧余辉更柔”.
                        //
                        // 额外需求(星火闪烁):
                        // - 用户希望 show 的 ring glow 像火星/星星一样闪闪.
                        // - 这里用 curl-like 噪声场对 ringGlow 做调制:
                        //   - 空间上形成“稀疏亮点”
                        //   - 时间上产生“闪烁/跳动”
                        // - 该效果只影响 ring(前沿),tail 仍保持更柔和的余辉,避免整体变成噪点墙.
                        float ringGlow = ring * showBoost;
                        float sparkleStrength = _VisibilityShowGlowSparkleStrength;
                        if (sparkleStrength > 0.0 && ring > 1e-4)
                        {
                            // sparkPos:
                            // - smokePos 做了 0.25 降频以避免“很混乱”,但星火需要更细的空间变化.
                            // - 这里用更高一点的频率(0.85)并把时间加速(×3),产生更明显的闪烁节奏.
                            float3 sparkPos = modelCenterBase * (_VisibilityNoiseScale * 0.85) + tNoiseVec * 3.0;

                            // curl noise: 连续的旋涡/流动向量场.
                            float3 curlSpark = GsplatEvalCurlNoise(sparkPos + float3(11.7, 19.3, 7.1));
                            float curlMag01 = saturate(length(curlSpark) * 0.35);

                            // 稀疏亮点:
                            // - curlMag01 多数时候偏小,通过幂次把它变成“偶尔很亮”的星火.
                            float sparkMask = pow(curlMag01, 3.0);

                            // 时间闪烁:
                            // - 使用已有的第二份噪声采样(warp01b)作为 twinkle 相位,避免再做额外噪声采样.
                            // - pow 提高“闪一下”的离散感(类似火星闪烁).
                            float twinkle = pow(saturate(warp01b), 8.0);

                            float sparkle = sparkMask * twinkle;

                            // 只提升 ring 前沿的亮度(乘性),不影响 alphaMask,避免引入新的残留问题.
                            ringGlow *= 1.0 + sparkleStrength * sparkle * 2.0;
                        }

                        float tailGlow = pow(tailInside, 0.5);
                        glowFactor = ringGlow + tailGlow * showBoost * 0.85;
                    }
                    else if (_VisibilityMode == 2)
                    {
                        // hide:
                        // - 前沿(ring)更亮(Boost),但不随扩散向外变弱(避免外围突兀).
                        // - 增加一个“向内衰减”的 afterglow tail(位于内侧),让衰减方向朝内而不是朝外.
                        //
                        // 说明:
                        // - tailInside 在边界内侧(edgeDistNoisy<=0)取值,在边界处=1,向内逐渐衰减到 0.
                        // - 这样“中心先烧掉,更早冷却”,视觉上 tail 会朝内变弱.
                        glowFactor = ring * _VisibilityHideGlowStartBoost + tailInside;
                    }

                    visibilityGlowAdd = _VisibilityGlowColor.rgb * glowFactor * glowIntensity;

                    // --------------------------------------------------------
                    // 大小变化:
                    // - show: splat 从“极小”逐步长到正常大小.
                    // - hide: splat 从正常大小逐步缩到“极小”.
                    //
                    // 设计:
                    // - 用 passed 作为每个 splat 的局部进度,让“被燃烧环扫过”的区域自然变大/变小.
                    // - show 时设置一个很小的 minScale,并额外提供 ring/tail 的 size floor,
                    //   避免用户反馈的“ring 阶段全是很小的点点”.
                    //
                    // 注意:
                    // - ShowRingWidthNormalized/ShowTrailWidthNormalized 控制的是径向空间宽度(环在空间里有多厚),
                    //   不是粒子大小.
                    // - 这里的 scale 才是“粒子大小”(corner.offset 的缩放).
                    // --------------------------------------------------------
                    float minScaleShow = saturate(_VisibilityShowMinScale);
                    float minScaleHide = saturate(_VisibilityHideMinScale);
                    float showRingMinScale = max(saturate(_VisibilityShowRingMinScale), minScaleShow);
                    float showTrailMinScale = max(saturate(_VisibilityShowTrailMinScale), minScaleShow);
                    if (_VisibilityMode == 1)
                    {
                        // show:
                        // - 继续沿用“慢一点”的 grow,让从极小->正常的过程更容易被肉眼感知.
                        float tGrow = saturate(passed);
                        tGrow = pow(tGrow, 0.5);
                        tGrow = smoothstep(0.0, 1.0, tGrow);

                        float baseSize = lerp(minScaleShow, 1.0, tGrow);

                        // ring/tail 的 size floor:
                        // - ring 在外侧,passed 往往接近 0,因此仅靠 baseSize 会出现“全是小点点”.
                        // - 用 ring/tailInside 作为权重,给它们一个更大的最小尺寸,让燃烧前沿更可读.
                        float ringSizeFloor = lerp(minScaleShow, showRingMinScale, ring);
                        float tailSizeFloor = lerp(minScaleShow, showTrailMinScale, tailInside);

                        visibilitySizeMul = max(baseSize, max(ringSizeFloor, tailSizeFloor));
                    }
                    else
                    {
                        // hide:
                        // - 目标: 让“余辉粒子”在 glow 扫过后仍能存在一段时间(且尺寸不要立刻变到极小).
                        // - 解决用户反馈:
                        //   1) glow 一过,余辉几乎全没了 -> 在 tail 内把 shrink 拉长(到更靠后才变到最终 min).
                        //   2) 进入 glow 时仍希望已经明显变小 -> 仍保留在前沿到来前的预收缩.
                        //
                        // shrinkBand:
                        // - 允许比 ringWidth 更宽一点,让 shrink 在 glow 前就提前开始.
                        float shrinkBand = max(ringWidth, trailWidth * 0.35);
                        shrinkBand = max(shrinkBand, 1e-5);

                        // hideAfterglowScale:
                        // - 在燃烧前沿附近(余辉区域)保持一个“更可读”的 size.
                        // - 最终仍会在 tail 末端收敛到 minScaleHide(由 passedForFade 驱动).
                        // - 这里先用一个简单的派生规则(×2)作为默认行为,后续若需要可再拆成独立参数.
                        float hideAfterglowScale = min(1.0, max(minScaleHide, minScaleHide * 2.0));

                        // 预收缩(前沿到来前):
                        // - 0: 远离前沿(尺寸保持正常)
                        // - 1: 到达前沿(已缩到 afterglowScale)
                        float tApproach = saturate((shrinkBand - edgeDistForFade) / shrinkBand);
                        tApproach = EaseOutCirc(tApproach);
                        float preScale = lerp(1.0, hideAfterglowScale, tApproach);

                        // tail 内继续 shrink(前沿扫过后):
                        // - 使用 passedForFade(带 easing)让余辉阶段缩小更慢,避免“glow 一过就全没了”.
                        float insideScale = lerp(hideAfterglowScale, minScaleHide, passedForFade);

                        // burned=0: 前沿未到,用 preScale
                        // burned=1: 已被扫过,用 insideScale
                        float burned = step(0.0, -edgeDistForFade);
                        visibilitySizeMul = lerp(preScale, insideScale, burned);
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
                        float3 warpVec = float3(0.0, 0.0, 0.0);

                        if (_VisibilityNoiseMode == 1)
                        {
                            // CurlSmoke:
                            // - 用 curl-like 向量场生成更连续的“旋涡/流动”扭曲方向.
                            // - 让 warp 更像烟雾流动,减少随机抖动感.
                            float3 curlVec = GsplatEvalCurlNoise(smokePosWarp + float3(101.17, 17.31, 9.77));

                            // 让“旋涡”更偏向切向(围绕中心转),减少径向把对象拉散的感觉.
                            curlVec = curlVec - radial * dot(curlVec, radial);
                            warpVec = curlVec;

                            // 仍然保留一点径向分量,让扭曲更像“空间被拉扯”.
                            warpVec += radial * (warp01a * 2.0 - 1.0) * 0.25;
                        }
                        else
                        {
                            // ValueSmoke / HashLegacy:
                            // - 用两个标量噪声在 tangent/bitangent 上混合,得到稳定的“扭曲粒子”方向.
                            float3 axis = (abs(radial.y) < 0.99) ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                            float3 tangent = normalize(cross(axis, radial));
                            float3 bitangent = cross(radial, tangent);

                            warpVec = tangent * warpSignedA + bitangent * warpSignedB;
                            warpVec += radial * (warp01a * 2.0 - 1.0) * 0.45;
                        }

                        // hide 语义修正: trail 在内侧,不应被 warp 推到外圈
                        // - 当前 reveal/burn 的判定(passed/ring)刻意不受 warp 影响(避免阈值抖动).
                        // - 但如果 warp 把“内侧拖尾区域”的 splat 往径向外侧推,肉眼会产生
                        //   "trail 跑到外圈" 的错觉(尤其在 warpStrength 较大时更明显).
                        // - 因此在 hide 模式下,我们禁止 warpVec 的“径向外推”分量:
                        //   - 允许切向扭曲(旋涡/烟雾流动)
                        //   - 允许径向内咬(更像被吸进燃烧中心)
                        //   - 但不允许往外推过前沿 ring
                        if (_VisibilityMode == 2)
                        {
                            float outward = max(0.0, dot(warpVec, radial));
                            warpVec -= radial * outward;
                        }

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

                // ------------------------------------------------------------
                // Render style: Gaussian <-> ParticleDots
                // - 目标: 保持单次 draw,通过 shader morph 实现两种显示风格的平滑切换.
                // - 注意:
                //   - `blend==0` 时必须保持旧行为(包括 InitCorner 的 early-out).
                //   - dot 角点使用屏幕空间半径(px),更像粒子/点云调试视图.
                // ------------------------------------------------------------
                float styleBlend = saturate(_RenderStyleBlend);

                SplatCorner gaussCorner;
                gaussCorner.offset = float2(0.0, 0.0);
                gaussCorner.uv = float2(0.0, 0.0);
                bool hasGaussCorner = false;

                if (styleBlend < 1.0)
                {
                    SplatCovariance cov = ReadCovariance(source);
                    hasGaussCorner = InitCorner(source, cov, center, gaussCorner);

                    // 旧行为锁定:
                    // - 当 styleBlend==0 时,InitCorner 失败应直接丢弃(保持历史优化: <2px early-out,frustum cull).
                    if (!hasGaussCorner && styleBlend <= 0.0)
                    {
                        o.vertex = discardVec;
                        return o;
                    }
                }

                SplatCorner dotCorner;
                dotCorner.offset = float2(0.0, 0.0);
                dotCorner.uv = float2(0.0, 0.0);
                bool hasDotCorner = false;

                if (styleBlend > 0.0)
                {
                    // dot 的屏幕空间半径(px).
                    float rPx = max(_ParticleDotRadiusPixels, 0.0);
                    float2 c = center.proj.ww / _ScreenParams.xy;

                    // 简单 frustum cull(x/y):
                    // - rPx 直接作为“最大像素偏移”.
                    // - dot 即使很小也应允许渲染(不做 <2px early-out).
                    if (!any(abs(center.proj.xy) - float2(rPx, rPx) * c > center.proj.ww))
                    {
                        dotCorner.offset = source.cornerUV * rPx * c;
                        dotCorner.uv = source.cornerUV;
                        hasDotCorner = true;
                    }
                }

                // 过渡期兜底:
                // - 当某一侧 corner 不可用时,用另一侧兜底,避免切换期间“突然整点消失”.
                if (!hasGaussCorner && !hasDotCorner)
                {
                    o.vertex = discardVec;
                    return o;
                }
                if (!hasGaussCorner)
                    gaussCorner = dotCorner;
                if (!hasDotCorner)
                    dotCorner = gaussCorner;

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

                // ClipCorner:
                // - 仅对 Gaussian corner 执行(保持旧的 kernel 裁剪优化与数学稳定性).
                // - dot 不做 ClipCorner,避免 dot 半径被 baseAlpha 影响,保证“像粒子一样”的直觉控制.
                if (hasGaussCorner && styleBlend < 1.0)
                {
                    // ClipCorner 的数学域要求 alpha >= 1/255,否则会出现 NaN.
                    // 当 alpha 很小时,最终也会在 fragment 阶段被 discard,因此这里对 ClipCorner 的 alpha 做一个下限 clamp.
                    ClipCorner(gaussCorner, max(baseAlpha, 1.0 / 255.0));
                }

                // corner morph:
                // - offset/uv 都做线性插值,再由 fragment 阶段决定核形态与 alpha.
                SplatCorner corner;
                corner.offset = lerp(gaussCorner.offset, dotCorner.offset, styleBlend);
                corner.uv = lerp(gaussCorner.uv, dotCorner.uv, styleBlend);

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
                float styleBlend = saturate(_RenderStyleBlend);

                // Gaussian: 旧核形态.
                float alphaGauss = exp(-A * 4.0) * i.color.a;

                // ParticleDots: 实心 + 柔边(soft edge).
                // - kDotFeather 越大,柔边越宽.
                // - 这里先用常量,保持 API 简洁; 如后续需要再外露成参数.
                const float kDotFeather = 0.15;
                float inner = 1.0 - kDotFeather;
                float inner2 = inner * inner;
                float alphaDot = (1.0 - smoothstep(inner2, 1.0, A)) * i.color.a;

                float alpha = lerp(alphaGauss, alphaDot, styleBlend);
                if (alpha < 1.0 / 255.0) discard;
                if (_GammaToLinear)
                    return float4(GammaToLinearSpace(i.color.rgb) * alpha, alpha);
                return float4(i.color.rgb * alpha, alpha);
            }
            ENDHLSL


        }
    }
}
