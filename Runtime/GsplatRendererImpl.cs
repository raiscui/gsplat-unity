// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEngine;

namespace Gsplat
{
    public class GsplatRendererImpl
    {
        public uint SplatCount { get; private set; }
        public byte SHBands { get; private set; }
        public bool Has4D { get; private set; }

        // --------------------------------------------------------------------
        // Metal buffer 绑定稳态:
        // - 用户在 macOS/Metal 下遇到 "requires a ComputeBuffer at index ... Skipping draw calls" 的跳绘制 warning.
        // - 这会导致视口内整体“消失/闪烁”,并且 Unity 可能只打印一次,后续静默跳过.
        // - 为了把“buffer 绑定”从不稳定的 MPB(SetBuffer)路径里救出来,这里为每个 renderer 维护一个材质实例,
        //   并把所有 StructuredBuffers 绑定到材质实例上(必要时 MPB 作为补充).
        // --------------------------------------------------------------------
        Material m_materialInstance;
        byte m_materialInstanceSHBands = 255;

        MaterialPropertyBlock m_propertyBlock;
        public GraphicsBuffer PositionBuffer { get; private set; }
        public GraphicsBuffer ScaleBuffer { get; private set; }
        public GraphicsBuffer RotationBuffer { get; private set; }
        public GraphicsBuffer ColorBuffer { get; private set; }
        public GraphicsBuffer SHBuffer { get; private set; }
        public GraphicsBuffer OrderBuffer { get; private set; }
        public GraphicsBuffer VelocityBuffer { get; private set; }
        public GraphicsBuffer TimeBuffer { get; private set; }
        public GraphicsBuffer DurationBuffer { get; private set; }
        public ISorterResource SorterResource { get; private set; }

        public bool Valid =>
            PositionBuffer != null && PositionBuffer.IsValid() &&
            ScaleBuffer != null && ScaleBuffer.IsValid() &&
            RotationBuffer != null && RotationBuffer.IsValid() &&
            ColorBuffer != null && ColorBuffer.IsValid() &&
            OrderBuffer != null && OrderBuffer.IsValid() &&
            (SHBands == 0 || (SHBuffer != null && SHBuffer.IsValid())) &&
            // 注意: shader 里即使 Has4D=false,仍然声明了 Velocity/Time/Duration buffers(运行时分支).
            // 在 Metal 下如果任意一个 StructuredBuffer 未绑定,Unity 会直接跳过 draw call 以避免崩溃.
            // 因此这里把 4D buffers(含 dummy)视为“渲染必需资源”,不再仅在 Has4D=true 时检查.
            VelocityBuffer != null && VelocityBuffer.IsValid() &&
            TimeBuffer != null && TimeBuffer.IsValid() &&
            DurationBuffer != null && DurationBuffer.IsValid();

        static readonly int k_orderBuffer = Shader.PropertyToID("_OrderBuffer");
        static readonly int k_positionBuffer = Shader.PropertyToID("_PositionBuffer");
        static readonly int k_scaleBuffer = Shader.PropertyToID("_ScaleBuffer");
        static readonly int k_rotationBuffer = Shader.PropertyToID("_RotationBuffer");
        static readonly int k_colorBuffer = Shader.PropertyToID("_ColorBuffer");
        static readonly int k_shBuffer = Shader.PropertyToID("_SHBuffer");
        static readonly int k_velocityBuffer = Shader.PropertyToID("_VelocityBuffer");
        static readonly int k_timeBuffer = Shader.PropertyToID("_TimeBuffer");
        static readonly int k_durationBuffer = Shader.PropertyToID("_DurationBuffer");
        static readonly int k_matrixM = Shader.PropertyToID("_MATRIX_M");
        static readonly int k_splatInstanceSize = Shader.PropertyToID("_SplatInstanceSize");
        static readonly int k_splatCount = Shader.PropertyToID("_SplatCount");
        static readonly int k_splatBaseIndex = Shader.PropertyToID("_SplatBaseIndex");
        static readonly int k_gammaToLinear = Shader.PropertyToID("_GammaToLinear");
        static readonly int k_shDegree = Shader.PropertyToID("_SHDegree");
        static readonly int k_has4D = Shader.PropertyToID("_Has4D");
        static readonly int k_timeNormalized = Shader.PropertyToID("_TimeNormalized");
        static readonly int k_timeModel = Shader.PropertyToID("_TimeModel");
        static readonly int k_temporalCutoff = Shader.PropertyToID("_TemporalCutoff");

        // ----------------------------------------------------------------
        // Render style: Gaussian <-> ParticleDots
        // - 通过 `_RenderStyleBlend` 做单次 draw 的 shader morph.
        // - `_ParticleDotRadiusPixels` 仅在 ParticleDots 或过渡期生效.
        // ----------------------------------------------------------------
        static readonly int k_renderStyleBlend = Shader.PropertyToID("_RenderStyleBlend");
        static readonly int k_particleDotRadiusPixels = Shader.PropertyToID("_ParticleDotRadiusPixels");

        // ----------------------------------------------------------------
        // 可选: 显隐燃烧环动画 uniforms
        // - 注意: 这些字段全部是可选的,默认 mode=0 表示完全禁用,不改变旧行为.
        // - 为了避免 shader 侧出现未初始化值导致的“偶发闪一下”,我们选择每帧都显式写入.
        // ----------------------------------------------------------------
        static readonly int k_visibilityMode = Shader.PropertyToID("_VisibilityMode");
        static readonly int k_visibilityNoiseMode = Shader.PropertyToID("_VisibilityNoiseMode");
        static readonly int k_visibilityProgress = Shader.PropertyToID("_VisibilityProgress");
        static readonly int k_visibilityCenterModel = Shader.PropertyToID("_VisibilityCenterModel");
        static readonly int k_visibilityMaxRadius = Shader.PropertyToID("_VisibilityMaxRadius");
        static readonly int k_visibilityRingWidth = Shader.PropertyToID("_VisibilityRingWidth");
        static readonly int k_visibilityTrailWidth = Shader.PropertyToID("_VisibilityTrailWidth");
        static readonly int k_visibilityShowMinScale = Shader.PropertyToID("_VisibilityShowMinScale");
        static readonly int k_visibilityShowRingMinScale = Shader.PropertyToID("_VisibilityShowRingMinScale");
        static readonly int k_visibilityShowTrailMinScale = Shader.PropertyToID("_VisibilityShowTrailMinScale");
        static readonly int k_visibilityHideMinScale = Shader.PropertyToID("_VisibilityHideMinScale");
	        static readonly int k_visibilityGlowColor = Shader.PropertyToID("_VisibilityGlowColor");
	        static readonly int k_visibilityGlowIntensity = Shader.PropertyToID("_VisibilityGlowIntensity");
	        static readonly int k_visibilityShowGlowStartBoost = Shader.PropertyToID("_VisibilityShowGlowStartBoost");
	        static readonly int k_visibilityShowGlowSparkleStrength = Shader.PropertyToID("_VisibilityShowGlowSparkleStrength");
	        static readonly int k_visibilityHideGlowStartBoost = Shader.PropertyToID("_VisibilityHideGlowStartBoost");
	        static readonly int k_visibilityNoiseStrength = Shader.PropertyToID("_VisibilityNoiseStrength");
	        static readonly int k_visibilityNoiseScale = Shader.PropertyToID("_VisibilityNoiseScale");
	        static readonly int k_visibilityNoiseSpeed = Shader.PropertyToID("_VisibilityNoiseSpeed");
	        static readonly int k_visibilityWarpStrength = Shader.PropertyToID("_VisibilityWarpStrength");
        static readonly int k_visibilityTime = Shader.PropertyToID("_VisibilityTime");

        Material GetOrCreateMaterialInstance()
        {
            var settings = GsplatSettings.Instance;
            if (!settings || settings.Materials == null)
                return null;

            if (SHBands >= settings.Materials.Length)
                return null;

            var baseMat = settings.Materials[SHBands];
            if (!baseMat)
                return null;

            // 同一个 renderer 里,只要 SHBands/Shader 不变,材质实例可以复用.
            if (m_materialInstance && m_materialInstanceSHBands == SHBands && m_materialInstance.shader == baseMat.shader)
                return m_materialInstance;

            DestroyMaterialInstance();

            m_materialInstance = new Material(baseMat)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = $"GsplatRendererImplMaterialInstance(SH{SHBands})"
            };
            m_materialInstanceSHBands = SHBands;
            return m_materialInstance;
        }

        void DestroyMaterialInstance()
        {
            if (!m_materialInstance)
                return;

#if UNITY_EDITOR
            if (Application.isPlaying)
                Object.Destroy(m_materialInstance);
            else
                Object.DestroyImmediate(m_materialInstance);
#else
            Object.Destroy(m_materialInstance);
#endif

            m_materialInstance = null;
            m_materialInstanceSHBands = 255;
        }

        void BindBuffersToPropertyBlock()
        {
            m_propertyBlock ??= new MaterialPropertyBlock();

            // ----------------------------------------------------------------
            // 为什么每次都重新绑定 buffers:
            // - 用户反馈 Metal 下出现:
            //   "requires a ComputeBuffer at index (...) to be bound, but none provided"
            //   Unity 会直接跳过 draw call(避免崩溃),表现为视口内“偶发消失/闪烁”.
            // - 在一些 Unity/平台组合下,MaterialPropertyBlock 在调用 SetInteger/SetMatrix 等后,
            //   buffer 绑定可能被覆盖或丢失(看起来像是“之前 SetBuffer 没生效”).
            // - 因此这里在每次 Render 前都把所有 StructuredBuffers 再绑定一次,确保稳态.
            // ----------------------------------------------------------------
            m_propertyBlock.SetBuffer(k_orderBuffer, OrderBuffer);
            m_propertyBlock.SetBuffer(k_positionBuffer, PositionBuffer);
            m_propertyBlock.SetBuffer(k_scaleBuffer, ScaleBuffer);
            m_propertyBlock.SetBuffer(k_rotationBuffer, RotationBuffer);
            m_propertyBlock.SetBuffer(k_colorBuffer, ColorBuffer);
            if (SHBands > 0)
                m_propertyBlock.SetBuffer(k_shBuffer, SHBuffer);

            m_propertyBlock.SetBuffer(k_velocityBuffer, VelocityBuffer);
            m_propertyBlock.SetBuffer(k_timeBuffer, TimeBuffer);
            m_propertyBlock.SetBuffer(k_durationBuffer, DurationBuffer);

            // 同时把 buffers 绑定到 per-renderer 材质实例上,作为 Metal 下的稳态兜底.
            var mat = GetOrCreateMaterialInstance();
            if (mat)
            {
                mat.SetBuffer(k_orderBuffer, OrderBuffer);
                mat.SetBuffer(k_positionBuffer, PositionBuffer);
                mat.SetBuffer(k_scaleBuffer, ScaleBuffer);
                mat.SetBuffer(k_rotationBuffer, RotationBuffer);
                mat.SetBuffer(k_colorBuffer, ColorBuffer);
                if (SHBands > 0)
                    mat.SetBuffer(k_shBuffer, SHBuffer);

                mat.SetBuffer(k_velocityBuffer, VelocityBuffer);
                mat.SetBuffer(k_timeBuffer, TimeBuffer);
                mat.SetBuffer(k_durationBuffer, DurationBuffer);
            }
        }

#if UNITY_EDITOR
        // --------------------------------------------------------------------
        // Editor Play Mode 相机缓存:
        // - 我们在“只渲染 GameView,不渲染 SceneView”的模式下需要枚举相机.
        // - 避免使用 `Camera.allCameras` 带来的 GC 分配,这里用 `Camera.GetAllCameras` + 缓存数组.
        // --------------------------------------------------------------------
        static Camera[] s_editorPlayRenderCameras;

        static int GetAllCamerasNonAllocEditor(ref Camera[] cameras)
        {
            var count = Camera.allCamerasCount;
            if (count <= 0)
                return 0;

            if (cameras == null || cameras.Length < count)
                cameras = new Camera[Mathf.NextPowerOfTwo(count)];

            return Camera.GetAllCameras(cameras);
        }
#endif

        public GsplatRendererImpl(uint splatCount, byte shBands, bool has4D)
        {
            SplatCount = splatCount;
            SHBands = shBands;
            Has4D = has4D;
            CreateResources(splatCount);
            CreatePropertyBlock();
        }

        public void RecreateResources(uint splatCount, byte shBands, bool has4D)
        {
            if (SplatCount == splatCount && SHBands == shBands && Has4D == has4D)
                return;
            Dispose();
            SplatCount = splatCount;
            SHBands = shBands;
            Has4D = has4D;
            CreateResources(splatCount);
            CreatePropertyBlock();
        }

        void CreateResources(uint splatCount)
        {
            PositionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector3)));
            ScaleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector3)));
            RotationBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector4)));
            ColorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector4)));
            if (SHBands > 0)
                SHBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    GsplatUtils.SHBandsToCoefficientCount(SHBands) * (int)splatCount,
                    System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector3)));
            OrderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount, sizeof(uint));

            // 注意: 即使 Has4D=false,我们也会创建一个最小的 dummy buffer,
            // 这样 shader/compute 在绑定阶段不会因为缺失 buffer 而报错.
            var fourDCount = Has4D ? (int)splatCount : 1;
            VelocityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, fourDCount,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector3)));
            TimeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, fourDCount, sizeof(float));
            DurationBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, fourDCount, sizeof(float));

            SorterResource = GsplatSorter.Instance.CreateSorterResource(splatCount, PositionBuffer, OrderBuffer);
        }

        void CreatePropertyBlock()
        {
            // 统一收敛到同一条绑定路径,避免“某个 buffer 忘记绑定”导致 Metal 跳绘制.
            BindBuffersToPropertyBlock();
        }

        // ----------------------------------------------------------------
        // Render style uniforms:
        // - 该方法只负责写入 MPB(uniform),不参与排序与资源管理.
        // - 由上层组件负责:
        //   1) 选择 RenderStyle(Gaussian/ParticleDots)
        //   2) 动画曲线与时长(easeInOutQuart,1.5s)
        // ----------------------------------------------------------------
        public void SetRenderStyleUniforms(float blend01, float dotRadiusPixels)
        {
            m_propertyBlock ??= new MaterialPropertyBlock();

            if (float.IsNaN(blend01) || float.IsInfinity(blend01))
                blend01 = 0.0f;
            blend01 = Mathf.Clamp01(blend01);

            if (float.IsNaN(dotRadiusPixels) || float.IsInfinity(dotRadiusPixels) || dotRadiusPixels < 0.0f)
                dotRadiusPixels = 0.0f;

            m_propertyBlock.SetFloat(k_renderStyleBlend, blend01);
            m_propertyBlock.SetFloat(k_particleDotRadiusPixels, dotRadiusPixels);
        }

        // ----------------------------------------------------------------
        // 显隐燃烧环动画: uniforms 下发
        // - 该方法只负责把参数写入 MaterialPropertyBlock.
        // - 由上层组件(GsplatRenderer/GsplatSequenceRenderer)负责:
        //   1) 状态机(Showing/Hiding/Hidden/Visible)
        //   2) 计算 center/maxRadius/ringWidth/trailWidth
        //   3) 决定 mode/progress
        // ----------------------------------------------------------------
        public void SetVisibilityUniforms(int mode, int noiseMode, float progress, Vector3 centerModel, float maxRadius,
            float ringWidth, float trailWidth,
            float showMinScale, float showRingMinScale, float showTrailMinScale, float hideMinScale,
            Color glowColor, float glowIntensity,
            float showGlowStartBoost, float showGlowSparkleStrength, float hideGlowStartBoost,
            float noiseStrength, float noiseScale, float noiseSpeed, float warpStrength, float timeSeconds)
        {
            m_propertyBlock ??= new MaterialPropertyBlock();

            // mode: 0=off,1=show,2=hide
            if (mode != 1 && mode != 2)
                mode = 0;

            // noiseMode:
            // - 0: value smoke(默认)
            // - 1: curl smoke(更像旋涡流动)
            // - 2: hash legacy(旧版对照)
            if (noiseMode < 0 || noiseMode > 2)
                noiseMode = 0;

            if (float.IsNaN(progress) || float.IsInfinity(progress))
                progress = 0.0f;
            progress = Mathf.Clamp01(progress);

            // 半径/宽度类参数允许为 0(表示几乎无效果),但不允许为 NaN/Inf 或负数.
            if (float.IsNaN(maxRadius) || float.IsInfinity(maxRadius) || maxRadius < 0.0f)
                maxRadius = 0.0f;
            if (float.IsNaN(ringWidth) || float.IsInfinity(ringWidth) || ringWidth < 0.0f)
                ringWidth = 0.0f;
            if (float.IsNaN(trailWidth) || float.IsInfinity(trailWidth) || trailWidth < 0.0f)
                trailWidth = 0.0f;

            // 粒子最小尺寸(相对正常尺寸):
            // - 允许为 0(几乎不可见),但不允许 NaN/Inf.
            if (float.IsNaN(showMinScale) || float.IsInfinity(showMinScale))
                showMinScale = 0.0f;
            showMinScale = Mathf.Clamp01(showMinScale);

            if (float.IsNaN(showRingMinScale) || float.IsInfinity(showRingMinScale))
                showRingMinScale = showMinScale;
            showRingMinScale = Mathf.Clamp01(showRingMinScale);
            if (showRingMinScale < showMinScale)
                showRingMinScale = showMinScale;

            if (float.IsNaN(showTrailMinScale) || float.IsInfinity(showTrailMinScale))
                showTrailMinScale = showMinScale;
            showTrailMinScale = Mathf.Clamp01(showTrailMinScale);
            if (showTrailMinScale < showMinScale)
                showTrailMinScale = showMinScale;

            if (float.IsNaN(hideMinScale) || float.IsInfinity(hideMinScale))
                hideMinScale = 0.0f;
            hideMinScale = Mathf.Clamp01(hideMinScale);

	            if (float.IsNaN(glowIntensity) || float.IsInfinity(glowIntensity) || glowIntensity < 0.0f)
	                glowIntensity = 0.0f;
	            if (float.IsNaN(showGlowStartBoost) || float.IsInfinity(showGlowStartBoost) || showGlowStartBoost <= 0.0f)
	                showGlowStartBoost = 1.0f;
	            if (float.IsNaN(showGlowSparkleStrength) || float.IsInfinity(showGlowSparkleStrength) || showGlowSparkleStrength < 0.0f)
	                showGlowSparkleStrength = 0.0f;
	            // 只作为强度倍率,这里做一个合理上限 clamp,避免极端值导致过曝或浪费性能.
	            showGlowSparkleStrength = Mathf.Clamp(showGlowSparkleStrength, 0.0f, 3.0f);
	            if (float.IsNaN(hideGlowStartBoost) || float.IsInfinity(hideGlowStartBoost) || hideGlowStartBoost < 0.0f)
	                hideGlowStartBoost = 1.0f;

            if (float.IsNaN(noiseStrength) || float.IsInfinity(noiseStrength))
                noiseStrength = 0.0f;
            noiseStrength = Mathf.Clamp01(noiseStrength);

            if (float.IsNaN(noiseScale) || float.IsInfinity(noiseScale) || noiseScale < 0.0f)
                noiseScale = 0.0f;
            if (float.IsNaN(noiseSpeed) || float.IsInfinity(noiseSpeed) || noiseSpeed < 0.0f)
                noiseSpeed = 0.0f;

            if (float.IsNaN(warpStrength) || float.IsInfinity(warpStrength) || warpStrength < 0.0f)
                warpStrength = 0.0f;

            if (float.IsNaN(timeSeconds) || float.IsInfinity(timeSeconds))
                timeSeconds = 0.0f;

            m_propertyBlock.SetInteger(k_visibilityMode, mode);
            m_propertyBlock.SetInteger(k_visibilityNoiseMode, noiseMode);
            m_propertyBlock.SetFloat(k_visibilityProgress, progress);
            // Shader 侧为 float3,这里用 Vector4 写入 xyz.
            m_propertyBlock.SetVector(k_visibilityCenterModel, new Vector4(centerModel.x, centerModel.y, centerModel.z, 0.0f));
            m_propertyBlock.SetFloat(k_visibilityMaxRadius, maxRadius);
            m_propertyBlock.SetFloat(k_visibilityRingWidth, ringWidth);
	            m_propertyBlock.SetFloat(k_visibilityTrailWidth, trailWidth);
            m_propertyBlock.SetFloat(k_visibilityShowMinScale, showMinScale);
            m_propertyBlock.SetFloat(k_visibilityShowRingMinScale, showRingMinScale);
            m_propertyBlock.SetFloat(k_visibilityShowTrailMinScale, showTrailMinScale);
            m_propertyBlock.SetFloat(k_visibilityHideMinScale, hideMinScale);
	            m_propertyBlock.SetColor(k_visibilityGlowColor, glowColor);
	            m_propertyBlock.SetFloat(k_visibilityGlowIntensity, glowIntensity);
	            m_propertyBlock.SetFloat(k_visibilityShowGlowStartBoost, showGlowStartBoost);
	            m_propertyBlock.SetFloat(k_visibilityShowGlowSparkleStrength, showGlowSparkleStrength);
	            m_propertyBlock.SetFloat(k_visibilityHideGlowStartBoost, hideGlowStartBoost);
	            m_propertyBlock.SetFloat(k_visibilityNoiseStrength, noiseStrength);
	            m_propertyBlock.SetFloat(k_visibilityNoiseScale, noiseScale);
	            m_propertyBlock.SetFloat(k_visibilityNoiseSpeed, noiseSpeed);
	            m_propertyBlock.SetFloat(k_visibilityWarpStrength, warpStrength);
            m_propertyBlock.SetFloat(k_visibilityTime, timeSeconds);
        }

        public void Dispose()
        {
            PositionBuffer?.Dispose();
            ScaleBuffer?.Dispose();
            RotationBuffer?.Dispose();
            ColorBuffer?.Dispose();
            SHBuffer?.Dispose();
            OrderBuffer?.Dispose();
            VelocityBuffer?.Dispose();
            TimeBuffer?.Dispose();
            DurationBuffer?.Dispose();
            SorterResource?.Dispose();
            DestroyMaterialInstance();

            PositionBuffer = null;
            ScaleBuffer = null;
            RotationBuffer = null;
            ColorBuffer = null;
            SHBuffer = null;
            OrderBuffer = null;
            VelocityBuffer = null;
            TimeBuffer = null;
            DurationBuffer = null;
        }

        // ----------------------------------------------------------------
        // 统一的渲染准备逻辑(两条入口共用):
        // - Render(): 旧入口,内部会根据 CameraMode 选择目标相机.
        // - RenderForCamera(): 新入口,由外部(通常是 Editor 相机回调)指定 camera.
        //
        // 这里把“参数校验 + property block 填充 + buffers 绑定 + RenderParams 构建”收敛到一处,
        // 避免两个入口的行为漂移.
        // ----------------------------------------------------------------
        bool TryPrepareRender(uint splatCount, uint splatBaseIndex, Transform transform, Bounds localBounds, int layer,
            bool gammaToLinear, int shDegree, float timeNormalized, float motionPadding,
            int timeModel, float temporalCutoff,
            out GsplatSettings settings, out RenderParams rp, out int instanceCount)
        {
            settings = GsplatSettings.Instance;
            rp = default;
            instanceCount = 0;

            if (!Valid)
            {
                // 诊断用:
                // - 若这里偶发变成 invalid,说明 buffers 资源生命周期出了问题,会直接导致视口内“整体消失”.
                // - 开启 EnableEditorDiagnostics 后,会把跳过原因写入 trace,用于定位根因.
                GsplatEditorDiagnostics.LogRenderSkipped("RendererImpl.Valid=false (buffers missing)");
                return false;
            }

            if (!settings || !settings.Valid)
            {
                GsplatEditorDiagnostics.LogRenderSkipped("GsplatSettings invalid or null");
                return false;
            }

            if (!GsplatSorter.Instance.Valid)
            {
                GsplatEditorDiagnostics.LogRenderSkipped("GsplatSorter invalid");
                return false;
            }

            // 安全门禁:
            // - shader 侧会用 `_SplatBaseIndex + _OrderBuffer[order]` 访问 buffers.
            // - 因此必须保证 baseIndex/count 在 buffers 范围内,避免读越界导致 GPU 异常/随机消失.
            var total = (uint)Mathf.Min(PositionBuffer.count, OrderBuffer.count);
            if (splatCount == 0)
            {
                GsplatEditorDiagnostics.LogRenderSkipped("splatCount==0");
                return false;
            }
            if (splatBaseIndex >= total || splatCount > total - splatBaseIndex)
            {
                GsplatEditorDiagnostics.LogRenderSkipped("splatBaseIndex/splatCount out of range");
                return false;
            }

            m_propertyBlock.SetInteger(k_splatCount, (int)splatCount);
            m_propertyBlock.SetInteger(k_splatBaseIndex, (int)splatBaseIndex);
            m_propertyBlock.SetInteger(k_gammaToLinear, gammaToLinear ? 1 : 0);
            m_propertyBlock.SetInteger(k_splatInstanceSize, (int)settings.SplatInstanceSize);
            m_propertyBlock.SetInteger(k_shDegree, shDegree);
            m_propertyBlock.SetMatrix(k_matrixM, transform.localToWorldMatrix);
            m_propertyBlock.SetInteger(k_has4D, Has4D ? 1 : 0);
            m_propertyBlock.SetFloat(k_timeNormalized, Mathf.Clamp01(timeNormalized));
            // 时间核:
            // - 兼容旧资产: timeModel=0 视为 window.
            // - temporalCutoff 仅用于 gaussian,但我们仍统一设置,避免 shader 侧分支依赖未初始化值.
            var tm = timeModel == 2 ? 2 : 1;
            var cut = (float.IsNaN(temporalCutoff) || float.IsInfinity(temporalCutoff) || temporalCutoff <= 0.0f || temporalCutoff >= 1.0f)
                ? 0.01f
                : temporalCutoff;
            m_propertyBlock.SetInteger(k_timeModel, tm);
            m_propertyBlock.SetFloat(k_temporalCutoff, cut);

            // 最终 draw 之前再次绑定 buffers,确保 Metal 下不会因为缺失 StructuredBuffer 而跳绘制.
            BindBuffersToPropertyBlock();

            // 对 4D 运动做保守 bounds 扩展,避免相机剔除错误.
            if (motionPadding > 0.0f && !float.IsNaN(motionPadding) && !float.IsInfinity(motionPadding))
                localBounds.Expand(motionPadding * 2.0f);

            // 注意:
            // - 这里优先使用 per-renderer 材质实例,以保证 Metal 下 buffer 绑定稳态.
            // - 如果实例创建失败(配置缺失等),再回退到 settings 的共享材质.
            var mat = m_materialInstance ? m_materialInstance : settings.Materials[SHBands];
            rp = new RenderParams(mat)
            {
                worldBounds = GsplatUtils.CalcWorldBounds(localBounds, transform),
                matProps = m_propertyBlock,
                layer = layer
            };

            instanceCount = Mathf.CeilToInt(splatCount / (float)settings.SplatInstanceSize);
            return true;
        }

        /// <summary>
        /// Render the splats for a specific camera.
        /// </summary>
        /// <remarks>
        /// 说明:
        /// - 这是为 Editor 的“相机回调驱动渲染”准备的入口,用于解决:
        ///   同一帧内相机可能触发多次渲染(beginCameraRendering),但 Update 只提交一次 draw 导致的闪烁.
        /// - 该函数不会做 ActiveCameraOnly 的相机选择逻辑,调用者必须自己确保 camera 是“应该渲染的目标相机”.
        /// </remarks>
        public void RenderForCamera(Camera camera, uint splatCount, Transform transform, Bounds localBounds, int layer,
            bool gammaToLinear = false, int shDegree = 3, float timeNormalized = 0.0f, float motionPadding = 0.0f,
            int timeModel = 1, float temporalCutoff = 0.01f, string diagTag = "RenderForCamera",
            uint splatBaseIndex = 0)
        {
            if (!TryPrepareRender(splatCount, splatBaseIndex, transform, localBounds, layer, gammaToLinear, shDegree,
                    timeNormalized,
                    motionPadding, timeModel, temporalCutoff,
                    out var settings, out var rp, out var instanceCount))
                return;

            // 注意:
            // - 在 Unity Editor 下,SceneView 的内部 camera 可能在 `isActiveAndEnabled` 上表现得不一致,
            //   但它依然会触发 beginCameraRendering 并参与渲染.
            // - 如果我们在这里严格要求 `isActiveAndEnabled==true`,就会导致 EditMode 下“明明相机在渲染,但 draw 被跳过”,
            //   最终表现为整体闪烁/消失.
            //
            // 结论:
            // - RenderForCamera 的调用者应保证该 camera 来自“正在渲染的回调链路”.
            // - 这里仅做 `null/destroyed` 防御,不再 gate `isActiveAndEnabled`.
            if (!camera)
            {
                GsplatEditorDiagnostics.LogRenderSkipped("RenderForCamera: camera is null");
                return;
            }

            if ((camera.cullingMask & (1 << layer)) == 0)
            {
                GsplatEditorDiagnostics.LogRenderSkipped("RenderForCamera: cullingMask excludes layer");
                return;
            }

            rp.camera = camera;
            GsplatEditorDiagnostics.MarkDrawSubmitted(camera, layer, instanceCount, diagTag);
            Graphics.RenderMeshPrimitives(rp, settings.Mesh, 0, instanceCount);
        }

        /// <summary>
        /// Render the splats.
        /// </summary>
        /// <param name="splatCount">It can be less than or equal to the SplatCount property.</param>
        /// <param name="transform">Object transform.</param>
        /// <param name="localBounds">Bounding box in object space.</param>
        /// <param name="layer">Layer used for rendering.</param>
        /// <param name="gammaToLinear">Covert color space from Gamma to Linear.</param>
        /// <param name="shDegree">Order of SH coefficients used for rendering. The final value is capped by the SHBands property.</param>
        /// <param name="timeNormalized">归一化时间 [0,1],仅在 Has4D=true 时生效.</param>
        /// <param name="motionPadding">4D 运动的保守 padding(对象空间),用于避免剔除错误.</param>
        /// <param name="timeModel">时间核语义: 1=window(time0+duration), 2=gaussian(mu+sigma).</param>
        /// <param name="temporalCutoff">gaussian cutoff,仅在 timeModel=2 时使用.</param>
        public void Render(uint splatCount, Transform transform, Bounds localBounds, int layer,
            bool gammaToLinear = false, int shDegree = 3, float timeNormalized = 0.0f, float motionPadding = 0.0f,
            int timeModel = 1, float temporalCutoff = 0.01f, uint splatBaseIndex = 0)
        {
            if (!TryPrepareRender(splatCount, splatBaseIndex, transform, localBounds, layer, gammaToLinear, shDegree,
                    timeNormalized,
                    motionPadding, timeModel, temporalCutoff,
                    out var settings, out var rp, out var instanceCount))
                return;

            // ----------------------------------------------------------------
            // ActiveCameraOnly: 只对 ActiveCamera 提交 draw call.
            // - 目的: 避免“某些相机渲染了,但排序不是基于该相机”的错误显示.
            // - 同时也避免多相机重复 draw 带来的额外开销.
            // ----------------------------------------------------------------
            if (settings && settings.CameraMode == GsplatCameraMode.ActiveCameraOnly)
            {
#if UNITY_EDITOR
                // ----------------------------------------------------------------
                // Editor 非 Play 模式下的稳态策略(关键修复):
                // - 用户反馈:
                //   1) SceneView 在某些交互链路里会“显示/不显示”闪烁.
                //   2) GameView 也可能出现“突然不显示/又出现”(尤其在 UI 交互导致信号抖动时).
                //
                // 根因(我们要同时解决两类问题):
                // - SceneView 侧: Editor 内部渲染链路有时会使用不同的 SceneView Camera 实例参与渲染,
                //   如果我们只对某 1 个 Camera 实例提交 draw,就会出现“提交了但当前帧没被那个实例用上”.
                // - GameView 侧: 如果 EditMode 下无条件只画 SceneView,GameView 永远看不到,体感像“随机消失”.
                //
                // 解决策略:
                // - 先询问 Sorter 的 ActiveCamera 决策(包含 override 与 viewport hint).
                // - ActiveCamera=SceneView:
                //   - 对所有 SceneView 相机提交 draw(规避实例抖动导致的闪烁).
                // - ActiveCamera=Game/VR:
                //   - 只对 ActiveCamera 提交 draw(保证 GameView 稳定,同时维持 ActiveCameraOnly 的语义).
                // ----------------------------------------------------------------
                if (!Application.isPlaying)
                {
                    if (GsplatSorter.Instance.TryGetActiveCamera(out var editActiveCam) && editActiveCam)
                    {
                        // ActiveCamera=SceneView: 画所有 SceneView cameras(规避实例抖动).
                        if (editActiveCam.cameraType == CameraType.SceneView)
                        {
                            var renderedAny = false;

                            // ----------------------------------------------------------------
                            // SceneView cameras(稳态):
                            // - Editor 下的 SceneView 相机是隐藏相机,并不保证会被 `Camera.GetAllCameras` 枚举到.
                            // - 直接遍历 `SceneView.sceneViews` 才能更稳定拿到“当前 SceneView 窗口实际使用的 camera 实例”.
                            // - 这可以降低“只对某一个 SceneView Camera 实例提交 draw,但该帧实际渲染用的不是它”导致的闪烁.
                            // ----------------------------------------------------------------
                            foreach (var v in UnityEditor.SceneView.sceneViews)
                            {
                                if (v is not UnityEditor.SceneView sv || sv == null)
                                    continue;

                                var cam = sv.camera;

                                // 注意:
                                // - Unity Editor 下的 SceneView camera 可能出现 `enabled=false` / `isActiveAndEnabled=false`,
                                //   但它依然会参与渲染并触发 beginCameraRendering.
                                // - 因此这里不能用 `isActiveAndEnabled` 作为渲染门禁,否则会导致 SceneView 偶发“整帧不画”闪烁.
                                if (!cam)
                                    continue;

                                if ((cam.cullingMask & (1 << layer)) == 0)
                                    continue;

                                rp.camera = cam;
                                GsplatEditorDiagnostics.MarkDrawSubmitted(cam, layer, instanceCount, "EditMode.SceneView");
                                Graphics.RenderMeshPrimitives(rp, settings.Mesh, 0, instanceCount);
                                renderedAny = true;
                            }

                            // 兜底:
                            // - 极少数情况下 SceneView.sceneViews 可能为空(或 camera 暂时不可用),
                            //   这里至少对 resolved 的 SceneView camera 实例提交一次 draw,避免彻底不显示.
                            if (!renderedAny && (editActiveCam.cullingMask & (1 << layer)) != 0)
                            {
                                rp.camera = editActiveCam;
                                GsplatEditorDiagnostics.MarkDrawSubmitted(editActiveCam, layer, instanceCount, "EditMode.SceneView.FallbackActive");
                                Graphics.RenderMeshPrimitives(rp, settings.Mesh, 0, instanceCount);
                            }
                            return;
                        }

                        // ActiveCamera=Game/VR: 只对 ActiveCamera 提交 draw,保证 GameView 稳定.
                        if (editActiveCam.cameraType == CameraType.Game || editActiveCam.cameraType == CameraType.VR)
                        {
                            if ((editActiveCam.cullingMask & (1 << layer)) == 0)
                            {
                                GsplatEditorDiagnostics.LogRenderSkipped("EditMode.GameOrVr cullingMask excludes layer");
                                return;
                            }

                            rp.camera = editActiveCam;
                            GsplatEditorDiagnostics.MarkDrawSubmitted(editActiveCam, layer, instanceCount, "EditMode.GameOrVr");
                            Graphics.RenderMeshPrimitives(rp, settings.Mesh, 0, instanceCount);
                            return;
                        }

                        // 其它内部相机类型(EditMode)不渲染,避免污染 Preview/Reflection.
                        return;
                    }
                }
#endif

                if (!GsplatSorter.Instance.TryGetActiveCamera(out var activeCam) || !activeCam)
                {
                    GsplatEditorDiagnostics.LogRenderSkipped("ActiveCameraOnly: TryGetActiveCamera returned false/null");
                    return;
                }

                // 如果 ActiveCamera 的 cullingMask 不包含该 layer,则无需提交 draw.
                if ((activeCam.cullingMask & (1 << layer)) == 0)
                {
                    GsplatEditorDiagnostics.LogRenderSkipped("ActiveCameraOnly: ActiveCamera cullingMask excludes layer");
                    return;
                }

                rp.camera = activeCam;
                GsplatEditorDiagnostics.MarkDrawSubmitted(activeCam, layer, instanceCount, "ActiveCameraOnly");
                Graphics.RenderMeshPrimitives(rp, settings.Mesh, 0, instanceCount);
                return;
            }

#if UNITY_EDITOR
            if (Application.isPlaying && settings && settings.CameraMode == GsplatCameraMode.AllCameras &&
                settings.SkipSceneViewRenderingInPlayMode)
            {
                var cameraCount = GetAllCamerasNonAllocEditor(ref s_editorPlayRenderCameras);
                if (cameraCount > 0)
                {
                    for (var i = 0; i < cameraCount; i++)
                    {
                        var cam = s_editorPlayRenderCameras[i];
                        if (!cam)
                            continue;

                        // 只保证 GameView 流畅:
                        // - 这里仅对 Game/VR 相机提交 draw call.
                        // - SceneView 相机将不会渲染 Gsplat,从而避免额外 draw cost.
                        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.VR)
                            continue;

                        if ((cam.cullingMask & (1 << layer)) == 0)
                            continue;

                        rp.camera = cam;
                        Graphics.RenderMeshPrimitives(rp, settings.Mesh, 0, instanceCount);
                    }

                    return;
                }
            }
#endif

            Graphics.RenderMeshPrimitives(rp, settings.Mesh, 0, instanceCount);
        }
    }
}
