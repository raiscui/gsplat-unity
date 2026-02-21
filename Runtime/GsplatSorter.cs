// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    public interface IGsplat
    {
        public Transform transform { get; }
        public uint SplatCount { get; }
        public ISorterResource SorterResource { get; }
        public bool isActiveAndEnabled { get; }
        public bool Valid { get; }

        // --------------------------------------------------------------------
        // 4DGS 可选字段: 排序阶段需要知道当前时间与对应 buffers.
        // - 对于 3D-only 资产:
        //   - Has4D=false
        //   - buffers 允许返回 dummy(非 null)以避免 compute 绑定报错
        // --------------------------------------------------------------------
        public bool Has4D { get; }
        public float TimeNormalized { get; }
        public int TimeModel { get; } // 1=window, 2=gaussian(0 视为 window)
        public float TemporalCutoff { get; } // gaussian cutoff,仅在 TimeModel=2 时使用
        public GraphicsBuffer VelocityBuffer { get; }
        public GraphicsBuffer TimeBuffer { get; }
        public GraphicsBuffer DurationBuffer { get; }
    }

    public interface ISorterResource
    {
        public GraphicsBuffer PositionBuffer { get; }
        public GraphicsBuffer OrderBuffer { get; }
        public void Dispose();
    }

    // some codes of this class originated from the GaussianSplatRenderSystem in aras-p/UnityGaussianSplatting by Aras Pranckevičius
    // https://github.com/aras-p/UnityGaussianSplatting/blob/main/package/Runtime/GaussianSplatRenderer.cs
    public class GsplatSorter
    {
        class Resource : ISorterResource
        {
            public GraphicsBuffer PositionBuffer { get; }
            public GraphicsBuffer OrderBuffer { get; }

            public GraphicsBuffer InputKeys { get; private set; }
            public GsplatSortPass.SupportResources Resources { get; }
            public bool Initialized;

            public Resource(uint count, GraphicsBuffer positionBuffer, GraphicsBuffer orderBuffer)
            {
                PositionBuffer = positionBuffer;
                OrderBuffer = orderBuffer;

                InputKeys = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)count, sizeof(uint));
                Resources = GsplatSortPass.SupportResources.Load(count);
            }

            public void Dispose()
            {
                InputKeys?.Dispose();
                Resources.Dispose();

                InputKeys = null;
            }
        }

        public static GsplatSorter Instance => s_instance ??= new GsplatSorter();
        static GsplatSorter s_instance;
        CommandBuffer m_commandBuffer;
        readonly HashSet<IGsplat> m_gsplats = new();
        readonly HashSet<Camera> m_camerasInjected = new();
        readonly List<IGsplat> m_activeGsplats = new();
        GsplatSortPass m_sortPass;
        public const string k_PassName = "SortGsplats";

        public bool Valid => m_sortPass is { Valid: true };

        public void InitSorter(ComputeShader computeShader)
        {
            m_sortPass = computeShader ? new GsplatSortPass(computeShader) : null;
        }

        public void RegisterGsplat(IGsplat gsplat)
        {
            if (m_gsplats.Count == 0)
            {
                if (!GraphicsSettings.currentRenderPipeline)
                    Camera.onPreCull += OnPreCullCamera;
            }

            m_gsplats.Add(gsplat);
        }

        public void UnregisterGsplat(IGsplat gsplat)
        {
            if (!m_gsplats.Remove(gsplat))
                return;
            if (m_gsplats.Count != 0) return;

            if (m_camerasInjected != null)
            {
                if (m_commandBuffer != null)
                    foreach (var cam in m_camerasInjected.Where(cam => cam))
                        cam.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, m_commandBuffer);
                m_camerasInjected.Clear();
            }

            m_activeGsplats.Clear();
            m_commandBuffer?.Dispose();
            m_commandBuffer = null;
            Camera.onPreCull -= OnPreCullCamera;
        }

        public bool GatherGsplatsForCamera(Camera cam)
        {
            if (!cam)
                return false;

            if (cam.cameraType == CameraType.Preview)
                return false;

            // ----------------------------------------------------------------
            // Editor Play Mode 的一个常见性能坑:
            // - Play 模式时,GameView 与 SceneView 往往会同时渲染.
            // - HDRP 下 CustomPass 以及 Builtin 的 Camera.onPreCull 都是“按相机触发”.
            // - 这会导致同一帧内对两个相机各做一次 GPU 排序,看起来就像“Play 模式 AutoPlay 不流畅”.
            // 因此这里提供一个 settings 开关,在 Play 模式下可选择跳过 SceneView 相机的排序.
            // ----------------------------------------------------------------
            var settings = GsplatSettings.Instance;
            if (Application.isPlaying && settings && settings.SkipSceneViewSortingInPlayMode &&
                cam.cameraType == CameraType.SceneView)
            {
                return false;
            }

            m_activeGsplats.Clear();
            var cullingMask = cam.cullingMask;
            foreach (var gs in m_gsplats)
            {
                if (gs is not { isActiveAndEnabled: true, Valid: true })
                    continue;

                // 如果相机的 culling mask 不包含对象 layer,则该相机不会渲染该对象.
                // 此时排序属于纯浪费,会放大 Play 模式下多相机的性能问题.
                if (gs.transform && (cullingMask & (1 << gs.transform.gameObject.layer)) == 0)
                    continue;

                m_activeGsplats.Add(gs);
            }
            return m_activeGsplats.Count != 0;
        }

        void InitialClearCmdBuffer(Camera cam)
        {
            m_commandBuffer ??= new CommandBuffer { name = k_PassName };
            if (!GraphicsSettings.currentRenderPipeline && cam &&
                !m_camerasInjected.Contains(cam))
            {
                cam.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, m_commandBuffer);
                m_camerasInjected.Add(cam);
            }

            m_commandBuffer.Clear();
        }

        void OnPreCullCamera(Camera camera)
        {
            if (!Valid || !GsplatSettings.Instance.Valid || !GatherGsplatsForCamera(camera))
                return;

            InitialClearCmdBuffer(camera);
            DispatchSort(m_commandBuffer, camera);
        }

        public void DispatchSort(CommandBuffer cmd, Camera camera)
        {
            foreach (var gs in m_activeGsplats)
            {
                var res = (Resource)gs.SorterResource;
                if (!res.Initialized)
                {
                    m_sortPass.InitPayload(cmd, res.OrderBuffer, (uint)res.OrderBuffer.count);
                    res.Initialized = true;
                }

                var sorterArgs = new GsplatSortPass.Args
                {
                    Count = gs.SplatCount,
                    MatrixMv = camera.worldToCameraMatrix * gs.transform.localToWorldMatrix,
                    PositionBuffer = res.PositionBuffer,
                    VelocityBuffer = gs.VelocityBuffer,
                    TimeBuffer = gs.TimeBuffer,
                    DurationBuffer = gs.DurationBuffer,
                    Has4D = gs.Has4D,
                    TimeNormalized = gs.TimeNormalized,
                    TimeModel = gs.TimeModel,
                    TemporalCutoff = gs.TemporalCutoff,
                    InputKeys = res.InputKeys,
                    InputValues = res.OrderBuffer,
                    Resources = res.Resources
                };
                m_sortPass.Dispatch(cmd, sorterArgs);
            }
        }

        public ISorterResource CreateSorterResource(uint count, GraphicsBuffer positionBuffer,
            GraphicsBuffer orderBuffer)
        {
            return new Resource(count, positionBuffer, orderBuffer);
        }
    }
}
