// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    /// <summary>
    /// `.sog4d` 导入后的序列渲染组件.
    /// - 负责播放控制(TimeNormalized/AutoPlay/Speed/Loop).
    /// - 负责把“当前时间”的两帧量化 streams 解码+插值到 float buffers(后续任务实现).
    /// - 复用现有 GsplatSorter + GsplatRendererImpl 的排序与渲染路径.
    /// </summary>
    [ExecuteAlways]
    public sealed class GsplatSequenceRenderer : MonoBehaviour, IGsplat
    {
        public GsplatSequenceAsset SequenceAsset;

        [Header("Runtime .sog4d Loading (Player build)")]
        [Tooltip("可选: 在 Player build 中直接从 `.sog4d` ZIP bundle 加载序列.\n" +
                 "当 SequenceAsset 为空且处于 Play Mode 时,会用该 bundle 创建一个运行时的 GsplatSequenceAsset.\n" +
                 "注意: 依赖 Unity ImageConversion.LoadImage 支持 WebP,否则会 fail-fast.")]
        public TextAsset RuntimeSog4dBundle;

        [Tooltip("可选: 在 Player build 中从磁盘路径加载 `.sog4d`.\n" +
                 "- 当该字段非空时,优先使用它.\n" +
                 "- 相对路径会自动拼到 Application.streamingAssetsPath.\n" +
                 "注意: 在 Android 等平台,StreamingAssets 可能无法用 File.ReadAllBytes 直接读取.\n" +
                 "此时建议改用 RuntimeSog4dBundle(TextAsset)或自行用 UnityWebRequest 读取 bytes 后再赋值.")]
        public string RuntimeSog4dPath;

        [Tooltip("是否启用按需加载帧 chunk(降低显存峰值,适合长序列).\n" +
                 "实现采用 overlap=1,确保相邻两帧总能落在同一个 chunk 内,从而保持 decode compute shader 不变.")]
        public bool RuntimeEnableChunkStreaming = true;

        [Min(2)]
        [Tooltip("chunk 的目标帧数(默认 50).\n" +
                 "当 FrameCount <= chunkFrameCount 时会自动回退为 full-load.\n" +
                 "当 chunk 启用时,chunk 之间会有 1 帧 overlap,以覆盖跨边界的 (i0,i1) 插值对.")]
        public int RuntimeChunkFrameCount = 50;

        [Tooltip("序列解码 compute shader.\n" +
                 "默认应为 Packages/wu.yize.gsplat/Runtime/Shaders/GsplatSequenceDecode.compute")]
        public ComputeShader DecodeComputeShader;

        [Range(0, 3)] public int SHDegree = 3;
        public bool GammaToLinear;

        [Tooltip("是否启用 Gsplat 主后端(Compute 排序 + Gsplat.shader)渲染.\n" +
                 "如果未来增加其它后端(例如 VFX Graph 序列后端),可关闭以避免双重渲染与排序开销.")]
        public bool EnableGsplatBackend = true;

        [Tooltip("关键帧序列的插值模式.\n" +
                 "- Nearest: 只采样 i0 帧.\n" +
                 "- Linear: 采样 i0/i1 并按 a 插值.")]
        public GsplatInterpolationMode InterpolationMode = GsplatInterpolationMode.Linear;

        [Range(0, 1)] public float TimeNormalized;
        public bool AutoPlay;
        public float Speed = 1.0f;
        public bool Loop = true;

        GsplatSequenceAsset m_prevAsset;
        GsplatRendererImpl m_renderer;
        bool m_disabledDueToError;
        float m_timeNormalizedThisFrame;

        // --------------------------------------------------------------------
        // Runtime bundle state(可选)
        // --------------------------------------------------------------------
        GsplatSog4DRuntimeBundle m_runtimeBundle;
        bool m_ownsRuntimeSequenceAsset;

        // --------------------------------------------------------------------
        // Effective config(资源预算/自动降级后生效)
        // - 这些值用于 renderer 的资源创建,以及 decode/sort/render 的实际执行.
        // --------------------------------------------------------------------
        uint m_effectiveSplatCount;
        byte m_effectiveSHBands;
        GsplatInterpolationMode m_effectiveInterpolationMode;

        // --------------------------------------------------------------------
        // 解码资源(由 SequenceAsset 的静态数据生成,并在 asset 变化时重建)
        // --------------------------------------------------------------------
        const int k_decodeThreads = 256;

        ComputeShader m_decodeCS;
        int m_kernelDecodeSH0 = -1;
        int m_kernelDecodeSH = -1;

        GraphicsBuffer m_scaleCodebookBuffer;
        GraphicsBuffer m_sh0CodebookBuffer;
        GraphicsBuffer m_shNCentroidsBuffer;

        public bool Valid =>
            EnableGsplatBackend &&
            !m_disabledDueToError &&
            SequenceAsset &&
            m_renderer != null &&
            m_decodeCS != null;

        public uint SplatCount => SequenceAsset ? m_effectiveSplatCount : 0;

        public ISorterResource SorterResource => m_renderer != null ? m_renderer.SorterResource : null;

        // 4DGS buffers(本序列后端不使用 velocity/time/duration,但仍返回 dummy buffer 以避免 compute 绑定报错).
        bool IGsplat.Has4D => m_renderer != null && m_renderer.Has4D;
        float IGsplat.TimeNormalized => m_timeNormalizedThisFrame;
        GraphicsBuffer IGsplat.VelocityBuffer => m_renderer != null ? m_renderer.VelocityBuffer : null;
        GraphicsBuffer IGsplat.TimeBuffer => m_renderer != null ? m_renderer.TimeBuffer : null;
        GraphicsBuffer IGsplat.DurationBuffer => m_renderer != null ? m_renderer.DurationBuffer : null;

        // 公开 GPU buffers,用于调试/扩展后端(例如 VFX)绑定等场景.
        public GraphicsBuffer PositionBuffer => m_renderer != null ? m_renderer.PositionBuffer : null;
        public GraphicsBuffer ScaleBuffer => m_renderer != null ? m_renderer.ScaleBuffer : null;
        public GraphicsBuffer RotationBuffer => m_renderer != null ? m_renderer.RotationBuffer : null;
        public GraphicsBuffer ColorBuffer => m_renderer != null ? m_renderer.ColorBuffer : null;
        public GraphicsBuffer SHBuffer => m_renderer != null ? m_renderer.SHBuffer : null;
        public GraphicsBuffer OrderBuffer => m_renderer != null ? m_renderer.OrderBuffer : null;
        public byte EffectiveSHBands => m_renderer != null ? m_renderer.SHBands : (byte)0;

        void OnEnable()
        {
            GsplatSorter.Instance.RegisterGsplat(this);
            m_timeNormalizedThisFrame = Mathf.Clamp01(TimeNormalized);

            // tasks 9.1/9.2: Player build 运行时加载 `.sog4d` bundle.
            // - 仅在 Play Mode 下启用,避免 EditMode 下反复创建 GPU 资源导致卡顿/泄漏.
            // - 当 SequenceAsset 已经由 importer 提供时(常见工作流),不会走该路径.
            if (Application.isPlaying && !SequenceAsset && RuntimeSog4dBundle)
            {
                TryLoadRuntimeSog4dBundle();
            }

            if (!TryCreateOrRecreateRenderer())
                return;

            // ----------------------------------------------------------------
            // 注意:
            // - decode 资源创建可能因 shader 编译/平台支持等原因失败.
            // - 但 renderer 已经创建成功时,我们仍应更新 m_prevAsset,避免 Update 每帧都重建 renderer 刷屏.
            // - decode 资源的重试逻辑由 Update 里 "m_decodeCS==null" 分支负责.
            // ----------------------------------------------------------------
            m_prevAsset = SequenceAsset;

            if (!TryCreateOrRecreateDecodeResources())
                return;
        }

        void OnDisable()
        {
            GsplatSorter.Instance.UnregisterGsplat(this);
            DisposeDecodeResources();
            m_renderer?.Dispose();
            m_renderer = null;
            m_prevAsset = null;

            // 释放 runtime bundle 与运行时创建的 asset/纹理.
            m_runtimeBundle?.Dispose();
            m_runtimeBundle = null;
            DestroyOwnedRuntimeSequenceAssetIfAny();
        }

        void Update()
        {
            // ----------------------------------------------------------------
            // 播放控制: TimeNormalized / AutoPlay / Speed / Loop
            // - 与 GsplatRenderer 保持一致: 缓存 this-frame time,确保同帧排序与渲染一致.
            // ----------------------------------------------------------------
            if (float.IsNaN(Speed) || float.IsInfinity(Speed))
                Speed = 0.0f;

            if (AutoPlay)
            {
                var next = TimeNormalized + Time.deltaTime * Speed;
                TimeNormalized = Loop ? Mathf.Repeat(next, 1.0f) : Mathf.Clamp01(next);
            }

            m_timeNormalizedThisFrame = Mathf.Clamp01(TimeNormalized);

            if (m_prevAsset != SequenceAsset)
            {
                m_prevAsset = SequenceAsset;
                if (!TryCreateOrRecreateRenderer())
                    return;

                // SequenceAsset 变化时,必须丢弃旧的 decode 资源.
                // 否则即便 buffer 尺寸相同,也会因为 codebook/centroids 内容不同而解码出错.
                DisposeDecodeResources();

                if (!TryCreateOrRecreateDecodeResources())
                    return;
            }

            // decode 资源可能在 OnEnable 或 asset-change 时创建失败(例如 compute shader 尚未完成编译).
            // 此时 m_decodeCS 会被置空. 我们在这里做一次轻量重试,避免把问题变成“必须手动重启组件”.
            if (SequenceAsset && m_renderer != null && m_decodeCS == null)
            {
                if (!TryCreateOrRecreateDecodeResources())
                    return;
            }

            if (!Valid)
                return;

            // ----------------------------------------------------------------
            // tasks 4.3/4.6:
            // - 在排序与渲染之前,先把当前时间的两帧数据解码+插值写入 float buffers.
            // - 这样 sorter 读取 PositionBuffer 时就是“当前帧结果”,不需要改排序管线.
            // ----------------------------------------------------------------
            if (!TryDecodeThisFrame())
                return;

            m_renderer.Render(SplatCount, transform, SequenceAsset.UnionBounds, gameObject.layer,
                GammaToLinear, SHDegree, m_timeNormalizedThisFrame, motionPadding: 0.0f);
        }

        bool TryCreateOrRecreateRenderer()
        {
            m_disabledDueToError = false;

            if (!SequenceAsset)
            {
                m_renderer?.Dispose();
                m_renderer = null;
                return false;
            }

            RefreshEffectiveConfigAndLog();

            try
            {
                if (m_renderer == null)
                    m_renderer = new GsplatRendererImpl(m_effectiveSplatCount, m_effectiveSHBands, has4D: false);
                else
                    m_renderer.RecreateResources(m_effectiveSplatCount, m_effectiveSHBands, has4D: false);
            }
            catch (Exception ex)
            {
                m_disabledDueToError = true;
                m_renderer?.Dispose();
                m_renderer = null;
                Debug.LogError(
                    $"[Gsplat][Sequence] GraphicsBuffer 创建失败,已禁用该对象的渲染.\n" +
                    $"建议: 降低 SH 阶数,减少 splat 数量,或更换更大显存 GPU.\n" +
                    ex);
                return false;
            }

            return true;
        }

        void RefreshEffectiveConfigAndLog()
        {
            m_effectiveSplatCount = SequenceAsset ? SequenceAsset.SplatCount : 0;
            m_effectiveSHBands = SequenceAsset ? SequenceAsset.SHBands : (byte)0;
            m_effectiveInterpolationMode = InterpolationMode;

            if (!SequenceAsset)
                return;

            var hasShNStreams = SequenceAsset.SHBands > 0;
            var scaleCodebookCount = SequenceAsset.ScaleCodebook != null ? SequenceAsset.ScaleCodebook.Length : 0;
            var shNCount = hasShNStreams ? SequenceAsset.ShNCount : 0;

            // tasks 6.1: 把 `.sog4d` 的量化纹理 + palette 纳入预算估算.
            // tasks 9.2: 当启用 chunk streaming 时,实际 GPU 常驻帧数应以 loaded chunk 为准(避免误触发 AutoDegrade).
            var loadedFrameCountForGpuEstimate = SequenceAsset.FrameCount;
            if (m_runtimeBundle != null && m_runtimeBundle.ChunkingEnabled && m_runtimeBundle.LoadedChunkFrameCount > 0)
                loadedFrameCountForGpuEstimate = m_runtimeBundle.LoadedChunkFrameCount;

            var desiredBytes = GsplatUtils.EstimateSog4dGpuBytes(
                m_effectiveSplatCount,
                loadedFrameCountForGpuEstimate,
                SequenceAsset.Layout.Width,
                SequenceAsset.Layout.Height,
                m_effectiveSHBands,
                hasShNStreams,
                scaleCodebookCount,
                shNCount);
            var desiredMiB = GsplatUtils.BytesToMiB(desiredBytes);
            Debug.Log(
                $"[Gsplat][Sequence] GPU 资源估算: {desiredMiB:F1} MiB " +
                $"(splats={m_effectiveSplatCount}, frames={SequenceAsset.FrameCount}, layout={SequenceAsset.Layout.Width}x{SequenceAsset.Layout.Height}, shBands={m_effectiveSHBands}, interp={m_effectiveInterpolationMode})");

            var settings = GsplatSettings.Instance;
            if (!settings)
                return;

            // 风险阈值: 以显卡总显存的比例做提示(注意: 这不是实时可用显存).
            var vramMiB = SystemInfo.graphicsMemorySize;
            var warnRatio = Mathf.Clamp01(settings.VramWarnRatio);
            var thresholdBytes = (long)(vramMiB * 1024L * 1024L * warnRatio);
            if (vramMiB <= 0 || warnRatio <= 0.0f || desiredBytes <= thresholdBytes)
                return;

            var thresholdMiB = GsplatUtils.BytesToMiB(thresholdBytes);
            Debug.LogWarning(
                $"[Gsplat][Sequence] 资源风险较高: 估算 {desiredMiB:F1} MiB > 阈值 {thresholdMiB:F1} MiB " +
                $"(显存 {vramMiB} MiB * {warnRatio:P0}). " +
                $"建议: 降低 SH,限制 splat 数,或关闭插值(Nearest).");

            // tasks 5.3/6.2: 自动降级(可配置).
            var beforeCount = m_effectiveSplatCount;
            var beforeBands = m_effectiveSHBands;
            var beforeInterp = m_effectiveInterpolationMode;

            switch (settings.AutoDegrade)
            {
                case GsplatAutoDegradePolicy.ReduceSH:
                    if (m_effectiveSHBands > 0)
                        m_effectiveSHBands = 0;
                    break;
                case GsplatAutoDegradePolicy.CapSplatCount:
                    if (settings.AutoDegradeMaxSplatCount > 0 && m_effectiveSplatCount > settings.AutoDegradeMaxSplatCount)
                        m_effectiveSplatCount = settings.AutoDegradeMaxSplatCount;
                    break;
                case GsplatAutoDegradePolicy.ReduceSHThenCapSplatCount:
                    if (m_effectiveSHBands > 0)
                        m_effectiveSHBands = 0;
                    if (settings.AutoDegradeMaxSplatCount > 0 && m_effectiveSplatCount > settings.AutoDegradeMaxSplatCount)
                        m_effectiveSplatCount = settings.AutoDegradeMaxSplatCount;
                    break;
            }

            if (settings.AutoDegradeDisableInterpolation)
                m_effectiveInterpolationMode = GsplatInterpolationMode.Nearest;

            if (beforeCount != m_effectiveSplatCount || beforeBands != m_effectiveSHBands || beforeInterp != m_effectiveInterpolationMode)
            {
                var afterBytes = GsplatUtils.EstimateSog4dGpuBytes(
                    m_effectiveSplatCount,
                    SequenceAsset.FrameCount,
                    SequenceAsset.Layout.Width,
                    SequenceAsset.Layout.Height,
                    m_effectiveSHBands,
                    hasShNStreams,
                    scaleCodebookCount,
                    shNCount);
                var afterMiB = GsplatUtils.BytesToMiB(afterBytes);

                Debug.LogWarning(
                    $"[Gsplat][Sequence] AutoDegrade 生效: " +
                    $"splats {beforeCount} -> {m_effectiveSplatCount}, " +
                    $"shBands {beforeBands} -> {m_effectiveSHBands}, " +
                    $"interp {beforeInterp} -> {m_effectiveInterpolationMode}, " +
                    $"估算 {desiredMiB:F1} MiB -> {afterMiB:F1} MiB");
            }
        }

        void DisposeDecodeResources()
        {
            m_scaleCodebookBuffer?.Dispose();
            m_sh0CodebookBuffer?.Dispose();
            m_shNCentroidsBuffer?.Dispose();

            m_scaleCodebookBuffer = null;
            m_sh0CodebookBuffer = null;
            m_shNCentroidsBuffer = null;

            m_decodeCS = null;
            m_kernelDecodeSH0 = -1;
            m_kernelDecodeSH = -1;
        }

        bool TryCreateOrRecreateDecodeResources()
        {
            m_disabledDueToError = false;

            if (!SequenceAsset)
            {
                DisposeDecodeResources();
                return false;
            }

            // ----------------------------------------------------------------
            // 重要 guard:
            // - 在 `-batchmode -nographics` 等“无图形设备”的场景下,ComputeShader kernel 可能无法正确编译/反射.
            // - 这会导致后续 `Dispatch` 持续刷屏类似 "Kernel at index (...) is invalid" 的 error log.
            // - 这里选择直接禁用当前对象,避免把无关的渲染噪声带进 CI/命令行测试,也避免编辑器卡 log.
            // ----------------------------------------------------------------
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogWarning("[Gsplat][Sequence] 当前 graphicsDeviceType 为 Null,已跳过序列 decode(无图形设备).");
                m_disabledDueToError = true;
                DisposeDecodeResources();
                return false;
            }

            if (!SystemInfo.supportsComputeShaders)
            {
                Debug.LogError("[Gsplat][Sequence] 当前 GPU/平台不支持 ComputeShader,无法播放 .sog4d 序列.");
                m_disabledDueToError = true;
                DisposeDecodeResources();
                return false;
            }

            if (!SystemInfo.supports2DArrayTextures)
            {
                Debug.LogError("[Gsplat][Sequence] 当前 GPU/平台不支持 Texture2DArray,无法播放 .sog4d 序列.");
                m_disabledDueToError = true;
                DisposeDecodeResources();
                return false;
            }

            // 1) 解码 compute shader
            if (!DecodeComputeShader)
            {
                Debug.LogError(
                    "[Gsplat][Sequence] 缺少 DecodeComputeShader. " +
                    "请在 Inspector 指定为 Packages/wu.yize.gsplat/Runtime/Shaders/GsplatSequenceDecode.compute");
                m_disabledDueToError = true;
                DisposeDecodeResources();
                return false;
            }

            m_decodeCS = DecodeComputeShader;
            // 仅验证“当前会实际使用”的 kernel,避免某个未使用 kernel 的反射/编译问题阻塞播放.
            // - SHBands==0: 只会用 DecodeKeyframesSH0
            // - SHBands>0: 只会用 DecodeKeyframesSH
            if (m_effectiveSHBands > 0)
            {
                if (!m_decodeCS.HasKernel("DecodeKeyframesSH"))
                {
                    Debug.LogError("[Gsplat][Sequence] DecodeComputeShader 缺少必需 kernel: DecodeKeyframesSH.");
                    m_disabledDueToError = true;
                    DisposeDecodeResources();
                    return false;
                }

                m_kernelDecodeSH = m_decodeCS.FindKernel("DecodeKeyframesSH");
                m_kernelDecodeSH0 = -1;

                if (!TryValidateDecodeKernel(m_decodeCS, m_kernelDecodeSH, "DecodeKeyframesSH"))
                {
                    m_disabledDueToError = true;
                    DisposeDecodeResources();
                    return false;
                }
            }
            else
            {
                if (!m_decodeCS.HasKernel("DecodeKeyframesSH0"))
                {
                    Debug.LogError("[Gsplat][Sequence] DecodeComputeShader 缺少必需 kernel: DecodeKeyframesSH0.");
                    m_disabledDueToError = true;
                    DisposeDecodeResources();
                    return false;
                }

                m_kernelDecodeSH0 = m_decodeCS.FindKernel("DecodeKeyframesSH0");
                m_kernelDecodeSH = -1;

                if (!TryValidateDecodeKernel(m_decodeCS, m_kernelDecodeSH0, "DecodeKeyframesSH0"))
                {
                    m_disabledDueToError = true;
                    DisposeDecodeResources();
                    return false;
                }
            }

            // 2) codebook buffers
            if (!TryCreateOrRecreateScaleCodebookBuffer())
                return false;
            if (!TryCreateOrRecreateSh0CodebookBuffer())
                return false;

            // 3) SH rest centroids buffer(仅当 SHBands>0 时需要)
            if (m_effectiveSHBands > 0)
            {
                if (!TryCreateOrRecreateShNCentroidsBuffer())
                    return false;
            }
            else
            {
                m_shNCentroidsBuffer?.Dispose();
                m_shNCentroidsBuffer = null;
            }

            return true;
        }

        static bool TryValidateDecodeKernel(ComputeShader computeShader, int kernel, string kernelName)
        {
            // ----------------------------------------------------------------
            // 为什么需要这个检查:
            // - `ComputeShader.FindKernel` 能找到名字,不代表该 kernel 在当前 Graphics API 下成功编译.
            // - 当 kernel 编译失败或不被支持时,`Dispatch` 往往只输出 error log,
            //   并且不会抛异常,导致运行期变成“持续报错但逻辑还在往下走”的黑盒.
            // - 这里用 `GetKernelThreadGroupSizes` 做一次 fail-fast 反射,尽早把问题暴露为可操作报错.
            // ----------------------------------------------------------------
            // 1) 先用 IsSupported 做基本能力探测(会考虑 #pragma require).
            // - 这比直接依赖某些反射 API 更稳,也更贴近“能不能跑”的问题本身.
            try
            {
                if (!computeShader.IsSupported(kernel))
                {
                    Debug.LogError(
                        $"[Gsplat][Sequence] DecodeComputeShader kernel 无效: {kernelName}. " +
                        "当前设备不支持该 kernel 所需能力(ComputeShader.IsSupported=false).");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[Gsplat][Sequence] DecodeComputeShader kernel 无效: {kernelName}. " +
                    "在调用 ComputeShader.IsSupported 时发生异常,这通常意味着 compute shader 未正确编译或不被支持.\n" +
                    ex);
                return false;
            }

            // 2) 尝试查询 thread group sizes(非强制).
            // - 在少数 Unity/平台组合下,GetKernelThreadGroupSizes 可能抛出 IndexOutOfRangeException,
            //   但 kernel 仍然可能可正常 Dispatch(属于 Unity 内部反射/缓存问题).
            // - 因此这里把它当成“加分项检查”: 成功就校验,失败就降级为 warning.
            try
            {
                computeShader.GetKernelThreadGroupSizes(kernel, out var x, out var y, out var z);

                // numthreads 的 0 不是合法值,出现就视为异常.
                if (x == 0 || y == 0 || z == 0)
                {
                    Debug.LogError(
                        $"[Gsplat][Sequence] DecodeComputeShader kernel 无效: {kernelName}. " +
                        "GetKernelThreadGroupSizes 返回了 0,这通常意味着 kernel 未成功编译.");
                    return false;
                }

                // 轻量一致性检查: 我们的 dispatch 逻辑假设 X 维线程数为 256.
                if (x != k_decodeThreads || y != 1 || z != 1)
                {
                    Debug.LogWarning(
                        $"[Gsplat][Sequence] DecodeComputeShader kernel 线程组大小与预期不一致: {kernelName}. " +
                        $"numthreads=({x},{y},{z}), 预期=({k_decodeThreads},1,1). " +
                        "如果你修改了 compute shader 的 numthreads,也需要同步更新 C# 的 k_decodeThreads.");
                }

                return true;
            }
            catch (IndexOutOfRangeException ex)
            {
                Debug.LogWarning(
                    $"[Gsplat][Sequence] DecodeComputeShader kernel 线程组反射失败(将继续尝试运行): {kernelName}. " +
                    "GetKernelThreadGroupSizes 抛出了 IndexOutOfRangeException. " +
                    "这在部分 Unity/平台组合下可能是已知问题,但不一定代表 Dispatch 一定失败.\n" +
                    ex.Message);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[Gsplat][Sequence] DecodeComputeShader kernel 线程组反射失败(将继续尝试运行): {kernelName}. " +
                    "GetKernelThreadGroupSizes 抛出了异常.\n" +
                    ex);
                return true;
            }
        }

        bool TryCreateOrRecreateScaleCodebookBuffer()
        {
            var codebook = SequenceAsset.ScaleCodebook;
            if (codebook == null || codebook.Length == 0)
            {
                Debug.LogError("[Gsplat][Sequence] SequenceAsset.ScaleCodebook 为空,无法解码 scale.");
                m_disabledDueToError = true;
                return false;
            }

            if (m_scaleCodebookBuffer != null && m_scaleCodebookBuffer.count == codebook.Length)
                return true;

            try
            {
                m_scaleCodebookBuffer?.Dispose();
                m_scaleCodebookBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, codebook.Length,
                    Marshal.SizeOf(typeof(Vector3)))
                {
                    name = "Sog4D_ScaleCodebook"
                };
                m_scaleCodebookBuffer.SetData(codebook);
                return true;
            }
            catch (Exception ex)
            {
                m_disabledDueToError = true;
                m_scaleCodebookBuffer?.Dispose();
                m_scaleCodebookBuffer = null;
                Debug.LogError(
                    $"[Gsplat][Sequence] ScaleCodebook GraphicsBuffer 创建失败,已禁用该对象的渲染. " +
                    $"count={codebook.Length}, stride={Marshal.SizeOf(typeof(Vector3))}.\n" +
                    ex);
                return false;
            }
        }

        bool TryCreateOrRecreateSh0CodebookBuffer()
        {
            var codebook = SequenceAsset.Sh0Codebook;
            if (codebook == null || codebook.Length != 256)
            {
                Debug.LogError("[Gsplat][Sequence] SequenceAsset.Sh0Codebook 必须为 256 项,无法解码 sh0.");
                m_disabledDueToError = true;
                return false;
            }

            if (m_sh0CodebookBuffer != null && m_sh0CodebookBuffer.count == codebook.Length)
                return true;

            try
            {
                m_sh0CodebookBuffer?.Dispose();
                m_sh0CodebookBuffer =
                    new GraphicsBuffer(GraphicsBuffer.Target.Structured, codebook.Length, sizeof(float))
                    {
                        name = "Sog4D_Sh0Codebook"
                    };
                m_sh0CodebookBuffer.SetData(codebook);
                return true;
            }
            catch (Exception ex)
            {
                m_disabledDueToError = true;
                m_sh0CodebookBuffer?.Dispose();
                m_sh0CodebookBuffer = null;
                Debug.LogError(
                    $"[Gsplat][Sequence] Sh0Codebook GraphicsBuffer 创建失败,已禁用该对象的渲染. " +
                    $"count={codebook.Length}, stride={sizeof(float)}.\n" +
                    ex);
                return false;
            }
        }

        bool TryCreateOrRecreateShNCentroidsBuffer()
        {
            var bytes = SequenceAsset.ShNCentroidsBytes;
            if (bytes == null || bytes.Length == 0)
            {
                Debug.LogError("[Gsplat][Sequence] SequenceAsset.ShNCentroidsBytes 为空,无法解码 SH rest.");
                m_disabledDueToError = true;
                return false;
            }

            if (string.IsNullOrEmpty(SequenceAsset.ShNCentroidsType))
            {
                Debug.LogError("[Gsplat][Sequence] SequenceAsset.ShNCentroidsType 为空,无法解码 SH rest.");
                m_disabledDueToError = true;
                return false;
            }

            var restCoeffCount = (m_effectiveSHBands + 1) * (m_effectiveSHBands + 1) - 1;
            var scalarBytes = SequenceAsset.ShNCentroidsType == "f16" ? 2 : 4;
            var expectedBytes = (long)SequenceAsset.ShNCount * restCoeffCount * 3L * scalarBytes;
            if (bytes.LongLength != expectedBytes)
            {
                Debug.LogError(
                    $"[Gsplat][Sequence] ShNCentroidsBytes 尺寸不匹配: expected {expectedBytes}, got {bytes.LongLength}. " +
                    $"(shNCount={SequenceAsset.ShNCount}, restCoeffCount={restCoeffCount}, type={SequenceAsset.ShNCentroidsType})");
                m_disabledDueToError = true;
                return false;
            }

            var vecCount = SequenceAsset.ShNCount * restCoeffCount;
            if (m_shNCentroidsBuffer != null && m_shNCentroidsBuffer.count == vecCount)
                return true;

            // 目前为了实现简单与稳定,统一把 palette 解码成 float32 的 Vector3 buffer.
            // 后续若要进一步提升性能/显存,可以考虑在 compute 里直接支持 f16 解码(见 tasks 9.*).
            var decoded = new Vector3[vecCount];
            if (SequenceAsset.ShNCentroidsType == "f32")
            {
                var floats = MemoryMarshal.Cast<byte, float>(bytes.AsSpan());
                for (var i = 0; i < vecCount; i++)
                {
                    decoded[i] = new Vector3(
                        floats[i * 3 + 0],
                        floats[i * 3 + 1],
                        floats[i * 3 + 2]);
                }
            }
            else
            {
                var halves = MemoryMarshal.Cast<byte, ushort>(bytes.AsSpan());
                for (var i = 0; i < vecCount; i++)
                {
                    decoded[i] = new Vector3(
                        HalfToFloat(halves[i * 3 + 0]),
                        HalfToFloat(halves[i * 3 + 1]),
                        HalfToFloat(halves[i * 3 + 2]));
                }
            }

            try
            {
                m_shNCentroidsBuffer?.Dispose();
                m_shNCentroidsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vecCount,
                    Marshal.SizeOf(typeof(Vector3)))
                {
                    name = "Sog4D_ShNCentroids"
                };
                m_shNCentroidsBuffer.SetData(decoded);
                return true;
            }
            catch (Exception ex)
            {
                m_disabledDueToError = true;
                m_shNCentroidsBuffer?.Dispose();
                m_shNCentroidsBuffer = null;
                Debug.LogError(
                    $"[Gsplat][Sequence] ShNCentroids GraphicsBuffer 创建失败,已禁用该对象的渲染. " +
                    $"count={vecCount}, stride={Marshal.SizeOf(typeof(Vector3))}.\n" +
                    ex);
                return false;
            }
        }

        bool TryDecodeThisFrame()
        {
            if (!SequenceAsset)
                return false;

            // 必需 streams
            if (!SequenceAsset.PositionHi || !SequenceAsset.PositionLo || !SequenceAsset.ScaleIndices ||
                !SequenceAsset.Rotation || !SequenceAsset.Sh0)
            {
                Debug.LogError("[Gsplat][Sequence] SequenceAsset 缺少必需的 Texture2DArray streams,请重新导入 .sog4d.");
                m_disabledDueToError = true;
                return false;
            }

            // SH rest(当 SHBands>0 时必须存在)
            if (m_effectiveSHBands > 0 && !SequenceAsset.ShNLabels)
            {
                Debug.LogError("[Gsplat][Sequence] SequenceAsset.SHBands>0 但缺少 ShNLabels,请重新导入 .sog4d.");
                m_disabledDueToError = true;
                return false;
            }

            if (m_renderer == null || m_decodeCS == null)
                return false;

            // tasks 4.5: explicit time mapping 的二分查找与重复时间点处理,在 EvaluateFromTimeNormalized 内完成.
            SequenceAsset.TimeMapping.EvaluateFromTimeNormalized(SequenceAsset.FrameCount, m_timeNormalizedThisFrame,
                out var i0, out var i1, out var a);

            var useLinear = m_effectiveInterpolationMode == GsplatInterpolationMode.Linear && i0 != i1;
            if (!useLinear)
            {
                i1 = i0;
                a = 0.0f;
            }

            // tasks 9.2: chunk streaming
            // - chunk 模式下,SequenceAsset 内的 Texture2DArray 只包含“当前 chunk”的若干帧.
            // - 因此 compute shader 的 layer 索引必须使用 local frame index.
            var frame0Layer = i0;
            var frame1Layer = i1;
            if (m_runtimeBundle != null && m_runtimeBundle.ChunkingEnabled)
            {
                if (!m_runtimeBundle.TryEnsureChunkForFramePair(SequenceAsset, i0, i1, out frame0Layer, out frame1Layer,
                        out var chunkErr))
                {
                    m_disabledDueToError = true;
                    Debug.LogError($"[Gsplat][Sequence] runtime chunk load failed: {chunkErr}");
                    return false;
                }
            }

            // 量化 position 的 per-frame range
            var rangeMin0 = SequenceAsset.PositionRangeMin[i0];
            var rangeMax0 = SequenceAsset.PositionRangeMax[i0];
            var rangeMin1 = SequenceAsset.PositionRangeMin[i1];
            var rangeMax1 = SequenceAsset.PositionRangeMax[i1];

            // 选择 kernel: SHBands==0 用 SH0 kernel,否则用 SH kernel.
            var kernel = m_effectiveSHBands > 0 ? m_kernelDecodeSH : m_kernelDecodeSH0;

            // 常量参数
            m_decodeCS.SetInt("_SplatCount", (int)SplatCount);
            m_decodeCS.SetInt("_LayoutWidth", SequenceAsset.Layout.Width);
            m_decodeCS.SetInt("_Frame0", frame0Layer);
            m_decodeCS.SetInt("_Frame1", frame1Layer);
            m_decodeCS.SetFloat("_InterpA", a);
            m_decodeCS.SetInt("_UseLinear", useLinear ? 1 : 0);

            m_decodeCS.SetVector("_PosRangeMin0", rangeMin0);
            m_decodeCS.SetVector("_PosRangeMax0", rangeMax0);
            m_decodeCS.SetVector("_PosRangeMin1", rangeMin1);
            m_decodeCS.SetVector("_PosRangeMax1", rangeMax1);

            // Metal 兼容性: shader 内不能 GetDimensions(buffer),所以这里显式传入 buffer count.
            // 这些值用于 shader 侧的 clamp 与越界防御.
            m_decodeCS.SetInt("_ScaleCodebookCount", m_scaleCodebookBuffer != null ? m_scaleCodebookBuffer.count : 0);
            m_decodeCS.SetInt("_Sh0CodebookCount", m_sh0CodebookBuffer != null ? m_sh0CodebookBuffer.count : 0);
            m_decodeCS.SetInt("_ShNCentroidsCount", m_shNCentroidsBuffer != null ? m_shNCentroidsBuffer.count : 0);

            // 输入纹理
            m_decodeCS.SetTexture(kernel, "_PositionHi", SequenceAsset.PositionHi);
            m_decodeCS.SetTexture(kernel, "_PositionLo", SequenceAsset.PositionLo);
            m_decodeCS.SetTexture(kernel, "_ScaleIndices", SequenceAsset.ScaleIndices);
            m_decodeCS.SetTexture(kernel, "_Rotation", SequenceAsset.Rotation);
            m_decodeCS.SetTexture(kernel, "_Sh0", SequenceAsset.Sh0);

            // codebook buffers
            m_decodeCS.SetBuffer(kernel, "_ScaleCodebook", m_scaleCodebookBuffer);
            m_decodeCS.SetBuffer(kernel, "_Sh0Codebook", m_sh0CodebookBuffer);

            // 输出 buffers
            m_decodeCS.SetBuffer(kernel, "_OutPosition", m_renderer.PositionBuffer);
            m_decodeCS.SetBuffer(kernel, "_OutScale", m_renderer.ScaleBuffer);
            m_decodeCS.SetBuffer(kernel, "_OutRotation", m_renderer.RotationBuffer);
            m_decodeCS.SetBuffer(kernel, "_OutColor", m_renderer.ColorBuffer);

            // SH rest
            if (m_effectiveSHBands > 0)
            {
                var restCoeffCount = (m_effectiveSHBands + 1) * (m_effectiveSHBands + 1) - 1;
                m_decodeCS.SetInt("_RestCoeffCount", restCoeffCount);
                m_decodeCS.SetTexture(kernel, "_ShNLabels", SequenceAsset.ShNLabels);
                m_decodeCS.SetBuffer(kernel, "_ShNCentroids", m_shNCentroidsBuffer);
                m_decodeCS.SetBuffer(kernel, "_OutSH", m_renderer.SHBuffer);
            }

            // dispatch
            var groups = Mathf.CeilToInt(SplatCount / (float)k_decodeThreads);
            if (groups <= 0)
                return true;

            try
            {
                m_decodeCS.Dispatch(kernel, groups, 1, 1);
                return true;
            }
            catch (Exception ex)
            {
                // tasks 6.3: fail-fast,给出可行动错误,并禁用当前对象避免持续报错.
                m_disabledDueToError = true;
                Debug.LogError(
                    $"[Gsplat][Sequence] Decode compute dispatch 失败,已禁用该对象的渲染. " +
                    $"kernel={(m_effectiveSHBands > 0 ? "DecodeKeyframesSH" : "DecodeKeyframesSH0")}, groups={groups}, splats={SplatCount}.\n" +
                    $"建议: 检查 ComputeShader 是否支持当前 GPU,降低 splat 数,降低 SH,或关闭插值.\n" +
                    ex);
                return false;
            }
        }

        static float HalfToFloat(ushort h)
        {
            // ----------------------------------------------------------------
            // IEEE 754 half -> float32
            // - 不依赖 System.Half,以提高 Unity 版本兼容性.
            // - 参考公开的 half 转 float 位级算法.
            // ----------------------------------------------------------------
            var sign = (h >> 15) & 0x1;
            var exp = (h >> 10) & 0x1f;
            var mant = h & 0x3ff;

            if (exp == 0)
            {
                if (mant == 0)
                {
                    // +/- 0
                    var zeroBits = sign << 31;
                    return IntBitsToFloat(zeroBits);
                }

                // subnormal: 归一化 mantissa
                while ((mant & 0x400) == 0)
                {
                    mant <<= 1;
                    exp -= 1;
                }

                exp += 1;
                mant &= 0x3ff;
            }
            else if (exp == 31)
            {
                // Inf/NaN
                var infExp = 255;
                var bits = (sign << 31) | (infExp << 23) | (mant << 13);
                return IntBitsToFloat(bits);
            }

            // normal
            var fExp = exp + (127 - 15);
            var fMant = mant << 13;
            var fBits = (sign << 31) | (fExp << 23) | fMant;
            return IntBitsToFloat(fBits);
        }

        static float IntBitsToFloat(int bits)
        {
            unsafe
            {
                return *(float*)&bits;
            }
        }

        bool TryLoadRuntimeSog4dBundle()
        {
            // 防御: 避免重复加载.
            if (m_runtimeBundle != null || m_ownsRuntimeSequenceAsset)
                return true;

            byte[] bytes = null;
            if (!string.IsNullOrEmpty(RuntimeSog4dPath))
            {
                var path = RuntimeSog4dPath;
                if (!Path.IsPathRooted(path))
                    path = Path.Combine(Application.streamingAssetsPath, path);

                try
                {
                    bytes = File.ReadAllBytes(path);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Gsplat][Sequence] 读取 runtime .sog4d 文件失败: {path}\n{e}");
                    return false;
                }
            }
            else if (RuntimeSog4dBundle)
            {
                bytes = RuntimeSog4dBundle.bytes;
            }

            if (bytes == null || bytes.Length == 0)
                return false;

            var chunkFrameCount = Mathf.Max(2, RuntimeChunkFrameCount);
            if (!GsplatSog4DRuntimeBundle.TryOpen(bytes, RuntimeEnableChunkStreaming, chunkFrameCount,
                    out var bundle, out var openErr))
            {
                Debug.LogError($"[Gsplat][Sequence] 打开 runtime .sog4d 失败: {openErr}");
                return false;
            }

            if (!bundle.TryCreateSequenceAsset(out var runtimeAsset, out var createErr))
            {
                bundle.Dispose();
                Debug.LogError($"[Gsplat][Sequence] 创建 runtime SequenceAsset 失败: {createErr}");
                return false;
            }

            m_runtimeBundle = bundle;
            SequenceAsset = runtimeAsset;
            m_ownsRuntimeSequenceAsset = true;
            return true;
        }

        void DestroyOwnedRuntimeSequenceAssetIfAny()
        {
            if (!m_ownsRuntimeSequenceAsset || !SequenceAsset)
                return;

            // 注意: 这里的 Texture2DArray 都是运行时创建的,必须显式销毁,否则会泄漏 GPU 资源.
            DestroyRuntimeTexture2DArray(ref SequenceAsset.PositionHi);
            DestroyRuntimeTexture2DArray(ref SequenceAsset.PositionLo);
            DestroyRuntimeTexture2DArray(ref SequenceAsset.ScaleIndices);
            DestroyRuntimeTexture2DArray(ref SequenceAsset.Rotation);
            DestroyRuntimeTexture2DArray(ref SequenceAsset.Sh0);
            DestroyRuntimeTexture2DArray(ref SequenceAsset.ShNLabels);

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(SequenceAsset);
            else
                UnityEngine.Object.DestroyImmediate(SequenceAsset);

            SequenceAsset = null;
            m_ownsRuntimeSequenceAsset = false;
        }

        static void DestroyRuntimeTexture2DArray(ref Texture2DArray texture)
        {
            if (!texture)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);

            texture = null;
        }
    }
}
