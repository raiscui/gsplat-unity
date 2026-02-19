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
            PositionBuffer != null &&
            ScaleBuffer != null &&
            RotationBuffer != null &&
            ColorBuffer != null &&
            (SHBands == 0 || SHBuffer != null) &&
            (!Has4D || (VelocityBuffer != null && TimeBuffer != null && DurationBuffer != null));

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
        static readonly int k_gammaToLinear = Shader.PropertyToID("_GammaToLinear");
        static readonly int k_shDegree = Shader.PropertyToID("_SHDegree");
        static readonly int k_has4D = Shader.PropertyToID("_Has4D");
        static readonly int k_timeNormalized = Shader.PropertyToID("_TimeNormalized");

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
            m_propertyBlock ??= new MaterialPropertyBlock();
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
        public void Render(uint splatCount, Transform transform, Bounds localBounds, int layer,
            bool gammaToLinear = false, int shDegree = 3, float timeNormalized = 0.0f, float motionPadding = 0.0f)
        {
            if (!Valid || !GsplatSettings.Instance.Valid || !GsplatSorter.Instance.Valid)
                return;

            m_propertyBlock.SetInteger(k_splatCount, (int)splatCount);
            m_propertyBlock.SetInteger(k_gammaToLinear, gammaToLinear ? 1 : 0);
            m_propertyBlock.SetInteger(k_splatInstanceSize, (int)GsplatSettings.Instance.SplatInstanceSize);
            m_propertyBlock.SetInteger(k_shDegree, shDegree);
            m_propertyBlock.SetMatrix(k_matrixM, transform.localToWorldMatrix);
            m_propertyBlock.SetInteger(k_has4D, Has4D ? 1 : 0);
            m_propertyBlock.SetFloat(k_timeNormalized, Mathf.Clamp01(timeNormalized));

            // 对 4D 运动做保守 bounds 扩展,避免相机剔除错误.
            if (motionPadding > 0.0f && !float.IsNaN(motionPadding) && !float.IsInfinity(motionPadding))
                localBounds.Expand(motionPadding * 2.0f);
            var rp = new RenderParams(GsplatSettings.Instance.Materials[SHBands])
            {
                worldBounds = GsplatUtils.CalcWorldBounds(localBounds, transform),
                matProps = m_propertyBlock,
                layer = layer
            };

            Graphics.RenderMeshPrimitives(rp, GsplatSettings.Instance.Mesh, 0,
                Mathf.CeilToInt(splatCount / (float)GsplatSettings.Instance.SplatInstanceSize));
        }
    }
}
