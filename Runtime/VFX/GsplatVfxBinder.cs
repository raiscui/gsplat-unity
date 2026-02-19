// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

#if GSPLAT_ENABLE_VFX_GRAPH

using System;
using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.VFX.Utility;

namespace Gsplat.VFX
{
    // --------------------------------------------------------------------
    // GsplatVfxBinder
    // - 目标: 复刻 SplatVFX 的使用体验:
    //   1) 用 GraphicsBuffer 喂 VFX Graph.
    //   2) 用 VFX Property Binder 自动完成绑定.
    // - 同时: 我们要支持 4DGS(velocity/time/duration + TimeNormalized).
    //
    // 设计取舍(重要):
    // - 为了让 VFX Graph sample 尽量简单,我们在 compute shader 里生成:
    //   - AxisBuffer: 由 rotation+scale 计算出的 3 个轴向向量(带尺度).
    //   - VfxPosition/VfxColor:
    //     - 按 4D 语义计算 pos(t),时间窗外 alpha=0.
    //     - 把 f_dc 颜色系数解码为 baseRgb(对齐主后端: f_dc*SH_C0+0.5).
    // - 这样 VFX Graph 可以复用类似 SplatVFX 的图结构(Position/Axis/Color),且不容易把 4D 语义写错.
    // --------------------------------------------------------------------
	    [AddComponentMenu("VFX/Property Binders/Gsplat VFX Binder")]
	    [VFXBinder("Gsplat")]
	    [ExecuteAlways]
	    public sealed class GsplatVfxBinder : VFXBinderBase
	    {
	        // 绑定源: 提供 GPU buffers 与 TimeNormalized 的 GsplatRenderer.
	        public GsplatRenderer GsplatRenderer;

	        // VFX 后端辅助 compute shader(包含 BuildAxes/BuildDynamic 两个 kernel).
	        public ComputeShader VfxComputeShader;

	        [Tooltip("编辑器预览: 在非 Play 模式下,当 TimeNormalized/SplatCount 变化时自动 Step 一帧.这样你不点 VisualEffect.Play,仅拖动 TimeNormalized 也能在 Scene 里预览更新.")]
	        public bool PreviewInEditor = true;

	        // 默认 VFX compute shader 路径:
	        // - 仅用于 Editor 下的自动填充(降低手工搭建成本).
	        // - 运行时不会尝试按路径加载(避免引入 UnityEditor 依赖).
	        const string k_defaultVfxComputeShaderPath = GsplatUtils.k_PackagePath + "Runtime/Shaders/GsplatVfx.compute";

#if UNITY_EDITOR
	        public override void Reset()
	        {
	            base.Reset();

	            // 组件首次添加到 GameObject 时触发:
	            // - 如果用户没手工指定 compute shader,这里自动填上默认值.
	            // - 这样可以避免进入 Play 之后才在 Console 里看到缺失报错.
	            TryAutoAssignDefaultVfxComputeShader();
	        }

	        void OnValidate()
	        {
	            // Inspector 修改/脚本重载时触发,再兜底一次,避免引用意外变空.
	            TryAutoAssignDefaultVfxComputeShader();
	        }

	        void TryAutoAssignDefaultVfxComputeShader()
	        {
	            if (VfxComputeShader != null)
	                return;

	            // 注意: 这里故意使用 AssetDatabase 并限制在 UNITY_EDITOR,
	            // 以保证 Player 构建不依赖 UnityEditor 程序集.
	            VfxComputeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(k_defaultVfxComputeShaderPath);
	        }
#endif

	        // ----------------------------------------------------------------
	        // VFX Graph exposed properties(默认名与 SplatVFX sample 一致).
	        // 用户也可以在 Inspector 里改成自己的命名.
	        // ----------------------------------------------------------------
        public string SplatCountProperty { get => (string)m_splatCountProperty; set => m_splatCountProperty = value; }
        public string PositionBufferProperty { get => (string)m_positionBufferProperty; set => m_positionBufferProperty = value; }
        public string AxisBufferProperty { get => (string)m_axisBufferProperty; set => m_axisBufferProperty = value; }
        public string ColorBufferProperty { get => (string)m_colorBufferProperty; set => m_colorBufferProperty = value; }

        public string ScaleBufferProperty { get => (string)m_scaleBufferProperty; set => m_scaleBufferProperty = value; }
        public string RotationBufferProperty { get => (string)m_rotationBufferProperty; set => m_rotationBufferProperty = value; }
        public string ShBufferProperty { get => (string)m_shBufferProperty; set => m_shBufferProperty = value; }

        public string VelocityBufferProperty { get => (string)m_velocityBufferProperty; set => m_velocityBufferProperty = value; }
        public string TimeBufferProperty { get => (string)m_timeBufferProperty; set => m_timeBufferProperty = value; }
        public string DurationBufferProperty { get => (string)m_durationBufferProperty; set => m_durationBufferProperty = value; }

        public string TimeNormalizedProperty { get => (string)m_timeNormalizedProperty; set => m_timeNormalizedProperty = value; }
        public string Has4DProperty { get => (string)m_has4DProperty; set => m_has4DProperty = value; }

        // 兼容性说明:
        // - 我们最初参考 SplatVFX 的 binder 写法,会用 `[VFXPropertyBinding(...)]` 标注属性类型,
        //   这样 Inspector 里会有更友好的属性绑定 UI.
        // - 但在某些 Unity/VFX Graph 版本中,`VFXPropertyBinding` 属性并不存在或不在 Runtime 可引用的程序集里,
        //   会导致本包在启用 VFX 后端时直接编译失败(CS0246).
        // - 该属性对运行时绑定逻辑不是必需的,因为我们在 `IsValid/UpdateBinding` 里显式调用
        //   `Has*/Set*` 做校验与写入.
        // 因此这里移除 `VFXPropertyBinding`,仅保留序列化字段,保证跨版本可编译.
        [SerializeField]
        ExposedProperty m_splatCountProperty = "SplatCount";

        [SerializeField]
        ExposedProperty m_positionBufferProperty = "PositionBuffer";

        [SerializeField]
        ExposedProperty m_axisBufferProperty = "AxisBuffer";

        [SerializeField]
        ExposedProperty m_colorBufferProperty = "ColorBuffer";

        // 可选 raw buffers(高级图可能会用).
        [SerializeField]
        ExposedProperty m_scaleBufferProperty = "ScaleBuffer";

        [SerializeField]
        ExposedProperty m_rotationBufferProperty = "RotationBuffer";

        [SerializeField]
        ExposedProperty m_shBufferProperty = "SHBuffer";

        [SerializeField]
        ExposedProperty m_velocityBufferProperty = "VelocityBuffer";

        [SerializeField]
        ExposedProperty m_timeBufferProperty = "TimeBuffer";

        [SerializeField]
        ExposedProperty m_durationBufferProperty = "DurationBuffer";

        [SerializeField]
        ExposedProperty m_timeNormalizedProperty = "TimeNormalized";

        [SerializeField]
        ExposedProperty m_has4DProperty = "Has4D";

        // ----------------------------------------------------------------
        // Compute shader binding IDs
        // ----------------------------------------------------------------
        static readonly int k_positionBuffer = Shader.PropertyToID("_PositionBuffer");
        static readonly int k_scaleBuffer = Shader.PropertyToID("_ScaleBuffer");
        static readonly int k_rotationBuffer = Shader.PropertyToID("_RotationBuffer");
        static readonly int k_colorBuffer = Shader.PropertyToID("_ColorBuffer");
        static readonly int k_velocityBuffer = Shader.PropertyToID("_VelocityBuffer");
        static readonly int k_timeBuffer = Shader.PropertyToID("_TimeBuffer");
        static readonly int k_durationBuffer = Shader.PropertyToID("_DurationBuffer");

        static readonly int k_axisBuffer = Shader.PropertyToID("_AxisBuffer");
        static readonly int k_vfxPositionBuffer = Shader.PropertyToID("_VfxPositionBuffer");
        static readonly int k_vfxColorBuffer = Shader.PropertyToID("_VfxColorBuffer");

        static readonly int k_splatCount = Shader.PropertyToID("_SplatCount");
        static readonly int k_timeNormalized = Shader.PropertyToID("_TimeNormalized");
        static readonly int k_has4D = Shader.PropertyToID("_Has4D");

        const int k_threads = 256;

        int m_kernelBuildAxes = -1;
        int m_kernelBuildDynamic = -1;

        // 由 rotation+scale 计算出的轴向向量 buffer(3 vec3 / splat).
        GraphicsBuffer m_axisBuffer;

        // VFX 侧使用的动态 position/color buffers(把 4D 语义"烘焙"进去).
        GraphicsBuffer m_vfxPositionBuffer;
        GraphicsBuffer m_vfxColorBuffer;

        // 用于检测 renderer buffers 是否发生重建(引用变化).
        GraphicsBuffer m_lastScaleBuffer;
        GraphicsBuffer m_lastRotationBuffer;

        int m_lastCapacity = -1;
        bool m_axesDirty = true;
        bool m_warnedOverLimit;
        bool m_warnedMissingCompute;

	        // ----------------------------------------------------------------
	        // 动态 buffers rebuild cache
        // - 4D 场景下,VfxPosition/VfxColor 需要随 TimeNormalized 变化而重建.
        // - 3D 场景下,VfxPosition/VfxColor 主要用于:
        //   - 把 f_dc 颜色解码为 baseRgb(避免在 ShaderGraph 里重复做).
        // - 缓存命中时跳过 Dispatch,避免编辑器与运行时空转.
        // ----------------------------------------------------------------
        int m_lastDynamicActiveCount = -1;
        float m_lastDynamicTimeNormalized = float.NaN;
        GraphicsBuffer m_lastDynamicPositionBuffer;
        GraphicsBuffer m_lastDynamicColorBuffer;
        GraphicsBuffer m_lastDynamicVelocityBuffer;
        GraphicsBuffer m_lastDynamicTimeBuffer;
        GraphicsBuffer m_lastDynamicDurationBuffer;

#if UNITY_EDITOR
        // ----------------------------------------------------------------
        // 编辑器预览 step cache
        // - VisualEffect 在非 Play 模式下默认不播放,所以只改 exposed property 并不会触发 Update Context.
        // - 这里缓存关键参数,在发生变化时 Step 一帧(AdvanceOneFrame)让图执行一次 Update,从而刷新预览画面.
        // ----------------------------------------------------------------
        bool m_editorPreviewInitialized;
        int m_editorPreviewLastSplatCount = -1;
        float m_editorPreviewLastTimeNormalized = float.NaN;
        bool m_editorPreviewLastHas4D;
#endif

        static int DivRoundUp(int x, int y) => (x + y - 1) / y;

	        protected override void OnEnable()
	        {
	            // 重要: 必须调用基类 OnEnable,让 VFXPropertyBinder 能把本 binder 注册进更新列表.
	            // 否则 UpdateBinding 不会被执行,从而出现"看似挂上了组件但完全不工作"的隐性问题.
	            base.OnEnable();

#if UNITY_EDITOR
	            // Editor 侧兜底: 有些场景下(例如用户手工添加 binder),
	            // 可能没点开 Inspector 就直接进入了 Play,此时靠 Reset/OnValidate 可能还没填值.
	            // 因此在 OnEnable 再尝试一次,尽量把"可自愈的配置缺失"消灭在运行前.
	            TryAutoAssignDefaultVfxComputeShader();
#endif

	            // 如果用户没手工指定,默认尝试取同 GameObject 上的 renderer.
	            if (!GsplatRenderer)
	                GsplatRenderer = GetComponent<GsplatRenderer>();

	            // Kernel id 缓存,避免每帧 FindKernel.
	            if (VfxComputeShader)
	            {
	                m_kernelBuildAxes = VfxComputeShader.FindKernel("BuildAxes");
	                m_kernelBuildDynamic = VfxComputeShader.FindKernel("BuildDynamic");
	            }
	        }

        protected override void OnDisable()
        {
            ReleaseInternalBuffers();
            m_warnedOverLimit = false;
            m_warnedMissingCompute = false;

            // 重要: 调用基类 OnDisable,把本 binder 从 VFXPropertyBinder 的更新列表移除.
            base.OnDisable();
        }

        void ReleaseInternalBuffers()
        {
            m_axisBuffer?.Dispose();
            m_vfxPositionBuffer?.Dispose();
            m_vfxColorBuffer?.Dispose();

            m_axisBuffer = null;
            m_vfxPositionBuffer = null;
            m_vfxColorBuffer = null;

            m_lastScaleBuffer = null;
            m_lastRotationBuffer = null;
            m_lastCapacity = -1;
            m_axesDirty = true;

            ResetDynamicRebuildCache();

#if UNITY_EDITOR
            ResetEditorPreviewCache();
#endif
        }

        void ResetDynamicRebuildCache()
        {
            m_lastDynamicActiveCount = -1;
            m_lastDynamicTimeNormalized = float.NaN;
            m_lastDynamicPositionBuffer = null;
            m_lastDynamicColorBuffer = null;
            m_lastDynamicVelocityBuffer = null;
            m_lastDynamicTimeBuffer = null;
            m_lastDynamicDurationBuffer = null;
        }

#if UNITY_EDITOR
        void ResetEditorPreviewCache()
        {
            m_editorPreviewInitialized = false;
            m_editorPreviewLastSplatCount = -1;
            m_editorPreviewLastTimeNormalized = float.NaN;
            m_editorPreviewLastHas4D = false;
        }
#endif

        bool EnsureKernelsReady()
        {
            if (!VfxComputeShader)
                return false;
            if (m_kernelBuildAxes >= 0 && m_kernelBuildDynamic >= 0)
                return true;

            try
            {
                m_kernelBuildAxes = VfxComputeShader.FindKernel("BuildAxes");
                m_kernelBuildDynamic = VfxComputeShader.FindKernel("BuildDynamic");
                return true;
            }
            catch
            {
                return false;
            }
        }

        bool TryGetCapacityAndCount(out int capacity, out int activeCount)
        {
            capacity = 0;
            activeCount = 0;

            if (!GsplatRenderer)
                return false;

            var pos = GsplatRenderer.PositionBuffer;
            if (pos == null)
                return false;

            capacity = pos.count;
            activeCount = (int)Mathf.Clamp(GsplatRenderer.SplatCount, 0, (uint)capacity);
            return capacity > 0;
        }

        bool IsOverVfxLimit(int capacity)
        {
            var settings = GsplatSettings.Instance;
            if (!settings || settings.MaxSplatsForVfx == 0)
                return false;
            return capacity > settings.MaxSplatsForVfx;
        }

        void EnsureBuffersAllocated(int capacity, bool needDynamic)
        {
            // capacity 变化时,必须重建(避免越界).
            if (m_lastCapacity != capacity)
            {
                ReleaseInternalBuffers();
                m_lastCapacity = capacity;
            }

            // AxisBuffer: 3 vec3 / splat.
            if (m_axisBuffer == null)
            {
                m_axisBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * 3,
                    System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector3)));
                m_axesDirty = true;
            }

            // VfxPosition/VfxColor:
            // - 4D 时: 用于 pos(t) 与时间窗裁剪(alpha=0).
            // - 3D 时: 仍然保留该缓冲,用来把 f_dc 解码为 baseRgb,避免 VFX 侧颜色语义不一致.
            //
            // 说明:
            // - 这会比"3D-only 直接透传 ColorBuffer"多占用一些显存,
            //   但能显著减少 VFX 与主后端的颜色差异,且容量受 MaxSplatsForVfx 限制.
            m_vfxPositionBuffer ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector3)));
            m_vfxColorBuffer ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity,
                System.Runtime.InteropServices.Marshal.SizeOf(typeof(Vector4)));

            // 语义变化(3D<->4D)时,下一次动态 rebuild 逻辑会自动命中 cache miss,
            // 这里不需要额外处理;仅保留入参是为了让调用点更清晰.
            _ = needDynamic;
        }

        void RebuildAxesIfNeeded(int activeCount)
        {
            if (!EnsureKernelsReady())
                return;
            if (!GsplatRenderer)
                return;

            var scale = GsplatRenderer.ScaleBuffer;
            var rot = GsplatRenderer.RotationBuffer;
            if (scale == null || rot == null || m_axisBuffer == null)
                return;

            // buffers 重建或引用变化时,重新计算 AxisBuffer.
            if (m_lastScaleBuffer != scale || m_lastRotationBuffer != rot)
            {
                m_lastScaleBuffer = scale;
                m_lastRotationBuffer = rot;
                m_axesDirty = true;
            }

            if (!m_axesDirty)
                return;

            VfxComputeShader.SetInt(k_splatCount, activeCount);
            VfxComputeShader.SetBuffer(m_kernelBuildAxes, k_scaleBuffer, scale);
            VfxComputeShader.SetBuffer(m_kernelBuildAxes, k_rotationBuffer, rot);
            VfxComputeShader.SetBuffer(m_kernelBuildAxes, k_axisBuffer, m_axisBuffer);

            VfxComputeShader.Dispatch(m_kernelBuildAxes, DivRoundUp(activeCount, k_threads), 1, 1);
            m_axesDirty = false;
        }

        void RebuildDynamicBuffersIfNeeded(int activeCount, float timeNormalized)
        {
            if (!EnsureKernelsReady())
                return;
            if (!GsplatRenderer)
                return;

            if (m_vfxPositionBuffer == null || m_vfxColorBuffer == null)
                return;

            var pos = GsplatRenderer.PositionBuffer;
            var col = GsplatRenderer.ColorBuffer;
            var vel = GsplatRenderer.VelocityBuffer;
            var t0 = GsplatRenderer.TimeBuffer;
            var dt = GsplatRenderer.DurationBuffer;

            if (pos == null || col == null || vel == null || t0 == null || dt == null)
                return;

            // 3D-only 时,timeNormalized 对输出无影响,用一个稳定值避免 Inspector 拖动导致空转.
            var effectiveTimeNormalized = GsplatRenderer.Has4D ? timeNormalized : 0.0f;

            // 输入完全一致时跳过 Dispatch,避免 TimeNormalized 静止时每帧空转.
            // 注意: 这里对 timeNormalized 用 Approximately,避免 Inspector slider 的微小抖动导致无法命中缓存.
            if (m_lastDynamicActiveCount == activeCount &&
                Mathf.Approximately(m_lastDynamicTimeNormalized, effectiveTimeNormalized) &&
                m_lastDynamicPositionBuffer == pos &&
                m_lastDynamicColorBuffer == col &&
                m_lastDynamicVelocityBuffer == vel &&
                m_lastDynamicTimeBuffer == t0 &&
                m_lastDynamicDurationBuffer == dt)
            {
                return;
            }

            m_lastDynamicActiveCount = activeCount;
            m_lastDynamicTimeNormalized = effectiveTimeNormalized;
            m_lastDynamicPositionBuffer = pos;
            m_lastDynamicColorBuffer = col;
            m_lastDynamicVelocityBuffer = vel;
            m_lastDynamicTimeBuffer = t0;
            m_lastDynamicDurationBuffer = dt;

            VfxComputeShader.SetInt(k_splatCount, activeCount);
            VfxComputeShader.SetInt(k_has4D, GsplatRenderer.Has4D ? 1 : 0);
            VfxComputeShader.SetFloat(k_timeNormalized, Mathf.Clamp01(effectiveTimeNormalized));

            VfxComputeShader.SetBuffer(m_kernelBuildDynamic, k_positionBuffer, pos);
            VfxComputeShader.SetBuffer(m_kernelBuildDynamic, k_colorBuffer, col);
            VfxComputeShader.SetBuffer(m_kernelBuildDynamic, k_velocityBuffer, vel);
            VfxComputeShader.SetBuffer(m_kernelBuildDynamic, k_timeBuffer, t0);
            VfxComputeShader.SetBuffer(m_kernelBuildDynamic, k_durationBuffer, dt);

            VfxComputeShader.SetBuffer(m_kernelBuildDynamic, k_vfxPositionBuffer, m_vfxPositionBuffer);
            VfxComputeShader.SetBuffer(m_kernelBuildDynamic, k_vfxColorBuffer, m_vfxColorBuffer);

            VfxComputeShader.Dispatch(m_kernelBuildDynamic, DivRoundUp(activeCount, k_threads), 1, 1);
        }

#if UNITY_EDITOR
        void StepVfxIfNeededInEditor(VisualEffect component, int activeCount, float timeNormalized, bool needDynamic)
        {
            // 仅用于编辑器预览:
            // - Play 模式下 VisualEffect 会正常更新,不需要我们插手.
            // - 这里的目标是实现"拖动 TimeNormalized 就能预览",避免用户每次手点 VisualEffect.Play.
            if (!PreviewInEditor)
                return;
            if (Application.isPlaying)
                return;
            if (!component || !component.enabled)
                return;

            // 如果用户正在播放(非暂停),我们不干预其播放控制.
            // 在这种情况下,Update Context 会正常跑,TimeNormalized 变化也会自然刷新.
            if (!component.pause)
                return;

            // 结构性变化(粒子数量/3D-4D 语义切换)需要 Reinit,否则粒子数量可能与 buffer 不一致.
            var needReinit = !m_editorPreviewInitialized ||
                             m_editorPreviewLastSplatCount != activeCount ||
                             m_editorPreviewLastHas4D != needDynamic;

            // 时刻变化只需要 Step 一帧,让 Update Context 把 buffer 采样写回粒子属性.
            var needStep = needReinit ||
                           !Mathf.Approximately(m_editorPreviewLastTimeNormalized, timeNormalized);

            if (!needStep)
                return;

            var prevPause = component.pause;
            try
            {
                // Step 的前置条件: pause=true.
                component.pause = true;

                if (needReinit)
                    component.Reinit();

                // 触发一次"单帧更新",让 VFX Graph 执行 Update Context,从而刷新 Scene 视图预览.
                component.AdvanceOneFrame();
            }
            finally
            {
                component.pause = prevPause;
            }

            m_editorPreviewInitialized = true;
            m_editorPreviewLastSplatCount = activeCount;
            m_editorPreviewLastTimeNormalized = timeNormalized;
            m_editorPreviewLastHas4D = needDynamic;
        }
#endif

        // ----------------------------------------------------------------
        // VFXBinderBase
        // ----------------------------------------------------------------
        public override bool IsValid(VisualEffect component)
        {
            if (!component || !GsplatRenderer)
                return false;

            // 最低要求: VFX Graph 至少要暴露这几个属性.
            return component.HasUInt(m_splatCountProperty) &&
                   component.HasGraphicsBuffer(m_positionBufferProperty) &&
                   component.HasGraphicsBuffer(m_axisBufferProperty) &&
                   component.HasGraphicsBuffer(m_colorBufferProperty);
        }

        public override void UpdateBinding(VisualEffect component)
        {
            if (!component)
                return;

            if (!GsplatRenderer)
                GsplatRenderer = GetComponent<GsplatRenderer>();
            if (!GsplatRenderer)
                return;

            // compute shader 是 VFX sample 的关键依赖(生成 AxisBuffer/动态 buffers).
            // 如果缺失,就直接退出并给出可执行提示.
	            if (!VfxComputeShader)
	            {
	                if (!m_warnedMissingCompute)
	                {
	                    m_warnedMissingCompute = true;
	                    Debug.LogError(
	                        "[Gsplat][VFX] GsplatVfxBinder 缺少 VfxComputeShader. " +
	                        $"请在 Inspector 中指定为 {k_defaultVfxComputeShaderPath}");
	                }

	                return;
	            }

            // 如果 renderer 还没创建 GPU buffers,就不绑定.
            if (!TryGetCapacityAndCount(out var capacity, out var activeCount))
                return;

            // VFX Graph 的硬上限: 超过后禁用 VFX 后端,并提示用户回退到 Gsplat 主后端.
            if (IsOverVfxLimit(capacity))
            {
                if (component.enabled)
                    component.enabled = false;

	                // 自动回退: 一键 prefab 默认会关闭主后端,这里在超限时主动打开,避免"什么都不画".
                if (GsplatRenderer)
                    GsplatRenderer.EnableGsplatBackend = true;

                if (!m_warnedOverLimit)
                {
                    m_warnedOverLimit = true;
                    var max = GsplatSettings.Instance ? GsplatSettings.Instance.MaxSplatsForVfx : 0;
                    Debug.LogWarning(
                        $"[Gsplat][VFX] 已自动禁用 VFX 后端: splat capacity={capacity} > MaxSplatsForVfx={max}. " +
                        "建议: 仅使用 Gsplat 主后端渲染,或降低 splat 数量后再启用 VFX.");
                }

                return;
            }

            // 在 limit 内,保证 VFX 后端启用.
            if (!component.enabled)
                component.enabled = true;
            m_warnedOverLimit = false;

            // 是否需要动态 buffers: 只有 4D 资产才需要(避免浪费显存).
            var needDynamic = GsplatRenderer.Has4D;
            EnsureBuffersAllocated(capacity, needDynamic);

            // 计算 AxisBuffer(通常是一次性的).
            RebuildAxesIfNeeded(activeCount);

            // 计算动态 position/color(每帧,由 TimeNormalized 驱动).
            var timeNormalized = Mathf.Clamp01(GsplatRenderer.TimeNormalized);
            RebuildDynamicBuffersIfNeeded(activeCount, timeNormalized);

            // ----------------------------------------------------------------
            // 绑定到 VFX Graph
            // ----------------------------------------------------------------
            component.SetUInt(m_splatCountProperty, (uint)activeCount);

            // Position/Color:
            // - 我们优先绑定 VFX 内部 buffers:
            //   - 4D: pos(t) + 时间窗裁剪.
            //   - 3D: f_dc->baseRgb 解码,避免颜色语义不一致.
            // - 如果内部 buffers 不存在(极端情况),再回退到 renderer 原始 buffers.
            var positionForVfx = m_vfxPositionBuffer != null ? m_vfxPositionBuffer : GsplatRenderer.PositionBuffer;
            var colorForVfx = m_vfxColorBuffer != null ? m_vfxColorBuffer : GsplatRenderer.ColorBuffer;

            component.SetGraphicsBuffer(m_positionBufferProperty, positionForVfx);
            component.SetGraphicsBuffer(m_axisBufferProperty, m_axisBuffer);
            component.SetGraphicsBuffer(m_colorBufferProperty, colorForVfx);

            // 可选 raw buffers: 仅在 VFX Graph 真暴露了该属性时才绑定,避免无意义报错.
            if (component.HasGraphicsBuffer(m_scaleBufferProperty))
                component.SetGraphicsBuffer(m_scaleBufferProperty, GsplatRenderer.ScaleBuffer);
            if (component.HasGraphicsBuffer(m_rotationBufferProperty))
                component.SetGraphicsBuffer(m_rotationBufferProperty, GsplatRenderer.RotationBuffer);
            if (component.HasGraphicsBuffer(m_velocityBufferProperty))
                component.SetGraphicsBuffer(m_velocityBufferProperty, GsplatRenderer.VelocityBuffer);
            if (component.HasGraphicsBuffer(m_timeBufferProperty))
                component.SetGraphicsBuffer(m_timeBufferProperty, GsplatRenderer.TimeBuffer);
            if (component.HasGraphicsBuffer(m_durationBufferProperty))
                component.SetGraphicsBuffer(m_durationBufferProperty, GsplatRenderer.DurationBuffer);

            // SH buffer 只有在有 SH 时才绑定.
            if (component.HasGraphicsBuffer(m_shBufferProperty) && GsplatRenderer.EffectiveSHBands > 0)
                component.SetGraphicsBuffer(m_shBufferProperty, GsplatRenderer.SHBuffer);

            // 时间参数同步(即使 sample 不用,也保证高级图可用).
            if (component.HasFloat(m_timeNormalizedProperty))
                component.SetFloat(m_timeNormalizedProperty, timeNormalized);

            if (component.HasInt(m_has4DProperty))
                component.SetInt(m_has4DProperty, needDynamic ? 1 : 0);

#if UNITY_EDITOR
            StepVfxIfNeededInEditor(component, activeCount, timeNormalized, needDynamic);
#endif
        }

        public override string ToString()
        {
            return $"Gsplat : {m_splatCountProperty}, {m_positionBufferProperty}, {m_axisBufferProperty}, {m_colorBufferProperty}";
        }
    }
}

#endif
