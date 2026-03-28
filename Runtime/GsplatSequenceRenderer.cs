// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Gsplat
{
    /// <summary>
    /// `.sog4d` 导入后的序列渲染组件.
    /// - 负责播放控制(TimeNormalized/AutoPlay/Speed/Loop).
    /// - 负责把“当前时间”的两帧量化 streams 解码+插值到 float buffers(后续任务实现).
    /// - 复用现有 GsplatSorter + GsplatRendererImpl 的排序与渲染路径.
    /// </summary>
    [ExecuteAlways]
    public sealed class GsplatSequenceRenderer : MonoBehaviour, IGsplat, IGsplatRenderSubmitter
    {
        const float k_defaultDualTrackSwitchTriggerProgress01 = 0.35f;

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
        [Tooltip("是否启用 Gaussian footprint 的抗锯齿补偿(对应 PlayCanvas 的 `GSPLAT_AA`).\n" +
                 "说明:\n" +
                 "- 默认关闭,与 SuperSplat / PlayCanvas 的默认配置一致.\n" +
                 "- 更适合对带 AA 训练/导出的 splat 做对照.\n" +
                 "- 对普通 3DGS 数据开启后,可能会让画面更软,也可能整体变暗.")]
        public bool EnableFootprintAACompensation;

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

        // --------------------------------------------------------------------
        // Render Style: Gaussian <-> Particle Dots
        // - 目标: 提供一个“更像粒子/点云”的圆片/圆点显示效果,并支持与高斯常规显示效果动画切换.
        // - 切换动画:
        //   - 几何 morph 使用 easeInOutQuart
        //   - alpha handoff 使用更柔和的 easeInOutSine
        //   - 默认时长 1.5 秒(可改)
        // --------------------------------------------------------------------
        [Header("Render Style (Gaussian / Particle Dots)")]
        [Tooltip("渲染风格.\n" +
                 "- Gaussian: 常规高斯基元(椭圆高斯核).\n" +
                 "- ParticleDots: 粒子圆片/圆点(屏幕空间圆盘).")]
        public GsplatRenderStyle RenderStyle = GsplatRenderStyle.Gaussian;

        [Min(0.0f)]
        [Tooltip("ParticleDots 的圆点半径(屏幕像素,px radius).\n" +
                 "说明:\n" +
                 "- 该参数只影响 ParticleDots 与切换过渡期.\n" +
                 "- 0 表示点半径为 0,效果接近不可见(用于特殊需要).")]
        public float ParticleDotRadiusPixels = 4.0f;

        [Min(0.0f)]
        [Tooltip("渲染风格切换的默认时长(秒).\n" +
                 "当你通过 API `SetRenderStyle(..., durationSeconds:-1)` 调用时,会使用该默认值.")]
        public float RenderStyleSwitchDurationSeconds = 1.5f;

        // --------------------------------------------------------------------
        // LiDAR 采集显示(车载风格,规则点云)
        // - 默认关闭,不影响现有 Gaussian/ParticleDots.
        // - 复用序列后端解码出来的 splat buffers(Position/Color...)作为"环境采样点".
        // - GPU compute 生成规则 range image(beam x azimuthBin),并具备第一回波(first return)遮挡语义.
        // --------------------------------------------------------------------
        [Header("LiDAR Scan (Experimental)")]
        [Tooltip("是否启用 LiDAR 采集显示.\n" +
                 "说明:\n" +
                 "- 默认关闭,不影响现有渲染.\n" +
                 "- 启用后会额外 dispatch compute(按 UpdateHz)生成 range image,并绘制规则点云.\n" +
                 "- 如果当前口径模式缺少必需的 sensor 配置,将不会渲染 LiDAR 点云(并输出一次提示日志).")]
        public bool EnableLidarScan;

        [Tooltip("LiDAR 扫描口径模式.\n" +
                 "说明:\n" +
                 "- Surround360: 继续使用传统 360 度扫描,传感器位姿来自 LidarOrigin.\n" +
                 "- CameraFrustum: 扫描口径与传感器位姿都直接来自 LidarFrustumCamera.")]
        public GsplatLidarApertureMode LidarApertureMode = GsplatLidarApertureMode.Surround360;

        [Tooltip("frustum 模式下的 LiDAR 相机.\n" +
                 "说明:\n" +
                 "- 该 camera 直接提供 sensor origin + rotation + projection.\n" +
                 "- 当前阶段会先把它作为 authoritative sensor pose.\n" +
                 "- 后续 active cells / GPU capture 也会继续对齐这个 camera.")]
        public Camera LidarFrustumCamera;

        [Tooltip("360 模式下的 LiDAR 原点与朝向.\n" +
                 "系统将以该 Transform 的 world position/rotation 作为 LiDAR 安装位姿.\n" +
                 "注意: 仅在 LidarApertureMode=Surround360 时该字段才是必填项.")]
        public Transform LidarOrigin;

        [FormerlySerializedAs("LidarExternalTargets")]
        [Tooltip("参与 RadarScan 的 static external 目标根对象数组.\n" +
                 "说明:\n" +
                 "- 数组中的每个元素都会被视为一个 static 扫描根对象.\n" +
                 "- 后续会递归收集其子层级中的 MeshRenderer / SkinnedMeshRenderer.\n" +
                 "- 旧字段 LidarExternalTargets 会自动迁到这里.\n" +
                 "- 留空时保持当前纯 gsplat LiDAR 行为不变.")]
        public GameObject[] LidarExternalStaticTargets = Array.Empty<GameObject>();

        [Tooltip("参与 RadarScan 的 dynamic external 目标根对象数组.\n" +
                 "说明:\n" +
                 "- 适合会动的 mesh / skinned mesh / 动画物体.\n" +
                 "- 当前起手阶段先把它接入现有 external scan 输入.\n" +
                 "- 后续 frustum GPU capture 路线会对它应用独立更新门禁.")]
        public GameObject[] LidarExternalDynamicTargets = Array.Empty<GameObject>();

        [Min(0.0f)]
        [Tooltip("dynamic external targets 的独立更新频率(Hz).\n" +
                 "说明:\n" +
                 "- <=0 或非法值会回退到 10Hz.\n" +
                 "- 当前阶段先完成字段与 Inspector 接线.\n" +
                 "- 后续 frustum GPU capture 路线会消费该门禁,用于 dynamic 组复用上一轮 capture.")]
        public float LidarExternalDynamicUpdateHz = 10.0f;

        [Tooltip("frustum external GPU capture 的分辨率模式.\n" +
                 "说明:\n" +
                 "- Auto: 沿用当前 frustum capture 的默认基准尺寸.\n" +
                 "- Scale: 在 Auto 基准上再乘以倍率,用于 supersample / 降采样.\n" +
                 "- 如果 external mesh 轮廓出现 depth 台阶 / stair-stepping,优先先尝试 `Scale`.\n" +
                 "- Explicit: 直接使用 `LidarExternalCaptureResolution` 指定的宽高.\n" +
                 "- 仅在 `LidarApertureMode=CameraFrustum` 且 external mesh 走 GPU capture 时生效.")]
        public GsplatLidarExternalCaptureResolutionMode LidarExternalCaptureResolutionMode =
            GsplatLidarExternalCaptureResolutionMode.Auto;

        [Min(0.01f)]
        [Tooltip("frustum external GPU capture 的分辨率倍率.\n" +
                 "说明:\n" +
                 "- 仅在 `LidarExternalCaptureResolutionMode=Scale` 时生效.\n" +
                 "- 1 表示保持 Auto 基准尺寸.\n" +
                 "- >1 表示 supersample,可提高 external depth / color capture 精度,也是缓解 external depth 台阶的首选入口.\n" +
                 "- <1 表示降低 capture 分辨率,换取性能.\n" +
                 "- 更高倍率会增加离屏 RT 的显存、带宽与 capture 成本,但不会改变 LiDAR beam / azimuth grid 的离散语义.")]
        public float LidarExternalCaptureResolutionScale = 1.0f;

        [Tooltip("frustum external GPU capture 的显式分辨率(width,height).\n" +
                 "说明:\n" +
                 "- 仅在 `LidarExternalCaptureResolutionMode=Explicit` 时生效.\n" +
                 "- 建议保持与 frustum camera 的 aspect 接近,避免把 capture 采样拉伸.\n" +
                 "- 这是 external mesh depth/color capture 的离屏分辨率,不是最终屏幕输出分辨率.\n" +
                 "- 更高显式分辨率会提高离屏采样精度,但也会提高离屏 capture 开销.\n" +
                 "- 该参数提高的是 external capture fidelity,不会把 external hit 改成 blur / bilinear depth 混合.")]
        public Vector2Int LidarExternalCaptureResolution = new(1920, 1080);

        [Tooltip("frustum external GPU capture 的 edge-aware resolve 模式.\n" +
                 "说明:\n" +
                 "- Off: 保持当前中心 uv 的 point texel read 语义.\n" +
                 "- Kernel2x2: 读取围绕 candidate uv 的 2x2 depth 邻域,做保边 nearest resolve.\n" +
                 "- Kernel3x3: 读取 3x3 depth 邻域,质量更高,但成本也更高.\n" +
                 "- 这是保边选样,不是 blur / bilinear depth mixing.\n" +
                 "- 默认 Off,避免旧场景隐式增加开销或改变语义.")]
        public GsplatLidarExternalEdgeAwareResolveMode LidarExternalEdgeAwareResolveMode =
            GsplatLidarExternalEdgeAwareResolveMode.Off;

        [Tooltip("frustum external GPU capture 的 subpixel resolve 模式.\n" +
                 "说明:\n" +
                 "- Off: 只评估中心 uv.\n" +
                 "- Quad4: 评估固定 4 个亚像素 candidate,用于降低单 texel 决策的相位误差.\n" +
                 "- 该模式使用 deterministic pattern,不会引入随机 temporal jitter.\n" +
                 "- 与 edge-aware resolve 可独立开关,也可同时开启.\n" +
                 "- 默认 Off,保持方案1行为不变.")]
        public GsplatLidarExternalSubpixelResolveMode LidarExternalSubpixelResolveMode =
            GsplatLidarExternalSubpixelResolveMode.Off;

        [Obsolete("Use LidarExternalStaticTargets and LidarExternalDynamicTargets instead.")]
        public GameObject[] LidarExternalTargets
        {
            get => LidarExternalStaticTargets;
            set => LidarExternalStaticTargets = value ?? Array.Empty<GameObject>();
        }

        [Tooltip("external target 的普通 mesh 可见性模式.\n" +
                 "说明:\n" +
                 "- KeepVisible: 外部目标继续显示普通 mesh,同时参与 RadarScan.\n" +
                 "- ForceRenderingOff: 外部目标继续参与 RadarScan,但不再显示普通 mesh shader 效果.\n" +
                 "- ForceRenderingOffInPlayMode: 仅在 Play 模式隐藏普通 mesh,编辑器平时仍显示.\n" +
                 "- 默认 ForceRenderingOff,更贴合\"scan-only 外部目标\"语义.")]
        public GsplatLidarExternalTargetVisibilityMode LidarExternalTargetVisibilityMode =
            GsplatLidarExternalTargetVisibilityMode.ForceRenderingOff;

        [Min(0.0f)]
        [Tooltip("LiDAR 扫描头旋转频率(Hz).\n" +
                 "说明:\n" +
                 "- 仅用于渲染阶段的扫描前沿/余辉亮度调制.\n" +
                 "- 默认 5Hz(1 圈 0.2 秒).\n" +
                 "- 当值非法或 <=0 时会回退到安全默认值(5Hz).")]
        public float LidarRotationHz = 5.0f;

        [Tooltip("是否启用 RadarScan 的扫描头运动效果.\n" +
                 "说明:\n" +
                 "- 开启(默认): 使用扫描头旋转 + 余辉 trail 的传统 RadarScan 观感.\n" +
                 "- 关闭: 不再播放扫描头运动,所有 LiDAR 粒子会直接以稳定可见的点云形式呈现.\n" +
                 "- 该开关不会关闭 LiDAR 采样/点云渲染本身,也不会影响开关时的淡入淡出(show/hide)过渡.")]
        public bool LidarEnableScanMotion = true;

        [Min(0.0f)]
        [Tooltip("LiDAR range image 的更新频率(Hz).\n" +
                 "说明:\n" +
                 "- v1 采用方案 X: 每次更新都会全量重建 360 range image.\n" +
                 "- 默认 10Hz(0.1 秒更新一次).\n" +
                 "- 当值非法或 <=0 时会回退到安全默认值(10Hz).")]
        public float LidarUpdateHz = 10.0f;

        [Min(64)]
        [Tooltip("水平角分辨率(azimuth bins).\n" +
                 "说明:\n" +
                 "- 默认 2048.\n" +
                 "- 该值越大,扫描线越细腻,但 compute 与渲染开销也更高.")]
        public int LidarAzimuthBins = 2048;

        [Tooltip("竖直视场上界(度,上方,通常为正).\n" +
                 "默认 +10 度.\n" +
                 "提示: 车载常见是\"上少下多\",上方视场相对更窄.")]
        public float LidarUpFovDeg = 10.0f;

        [Tooltip("竖直视场下界(度,下方,通常为负).\n" +
                 "默认 -30 度.")]
        public float LidarDownFovDeg = -30.0f;

        [Min(1)]
        [Tooltip("竖直线束数量(BeamCount).\n" +
                 "说明:\n" +
                 "- 不再拆分 UpBeams/DownBeams,而是在 [DownFovDeg..UpFovDeg] 内做匀角度采样.\n" +
                 "- 你只需要控制总线数与上下视场,即可得到\"上下统一\"的竖直分布.\n" +
                 "- 默认 128.\n" +
                 "- 当前不再设置硬上限,但 beamCount * azimuthBins 会直接放大 range image / compute / 显存开销.")]
        public int LidarBeamCount = 128;

        [Min(0.0f)]
        [Tooltip("有效距离下限(米).\n" +
                 "小于该距离的回波会被忽略.\n" +
                 "默认 1m.")]
        public float LidarDepthNear = 1.0f;

        [Min(0.0f)]
        [Tooltip("有效距离上限(米).\n" +
                 "大于该距离的回波会被忽略.\n" +
                 "默认 200m.")]
        public float LidarDepthFar = 200.0f;

        [Min(0.0f)]
        [Tooltip("LiDAR 点云的点半径(屏幕像素,px radius).\n" +
                 "默认 2px,可调.")]
        public float LidarPointRadiusPixels = 2.0f;

        [Range(0.0f, 1.0f)]
        [Tooltip("LiDAR 点位在每个采样 cell 内的稳定抖动比例(0..1).\n" +
                 "说明:\n" +
                 "- 目的: 打散过于规则的 beam x azimuth 栅格,减轻高密度时的摩尔纹.\n" +
                 "- 0: 完全关闭,保持严格落在 bin center.\n" +
                 "- 1: 最多抖到半个 cell 的角域范围.\n" +
                 "- 默认 0.35,在保留传感器结构感的同时减少规则拍频.")]
        public float LidarPointJitterCellFraction = 0.35f;

        [Min(0.0f)]
        [Tooltip("external mesh 命中的 RadarScan 粒子前推距离(米).\n" +
                 "说明:\n" +
                 "- 仅作用于 external target hit 的最终粒子渲染,不会改 first return 竞争结果.\n" +
                 "- 作用是沿 LiDAR/sensor 射线把粒子轻微往前推,避免原始 mesh 仍可见时,粒子因为深度太贴而看起来落在模型后面.\n" +
                 "- 设为 0 表示不做前推.\n" +
                 "- 默认 0m(关闭).")]
        public float LidarExternalHitBiasMeters = 0.0f;

        [Tooltip("RadarScan(LiDAR) 粒子抗锯齿模式.\n" +
                 "说明:\n" +
                 "- LegacySoftEdge: 保持当前固定 feather 的旧边缘语义.\n" +
                 "- AnalyticCoverage: 推荐模式. 使用屏幕导数驱动的本地 coverage AA,不依赖 MSAA.\n" +
                 "- AlphaToCoverage: 依赖有效 MSAA. 无 MSAA 时会自动回退到 AnalyticCoverage.\n" +
                 "- AnalyticCoveragePlusAlphaToCoverage: analytic coverage 与 A2C 叠加. 无 MSAA 时同样回退到 AnalyticCoverage.\n" +
                 "- 默认 LegacySoftEdge,以保持旧场景升级后的视觉兼容.")]
        public GsplatLidarParticleAntialiasingMode LidarParticleAntialiasingMode =
            GsplatLidarParticleAntialiasingMode.LegacySoftEdge;

        [Min(0.0f)]
        [Tooltip("RadarScan(LiDAR) 粒子 AA 的外扩 fringe 宽度(像素).\n" +
                 "说明:\n" +
                 "- 仅在非 LegacySoftEdge 模式生效.\n" +
                 "- 值越大,原始点边界外可用于 coverage 过渡的空间越大,边缘更容易看出来.\n" +
                 "- 设为 0 表示不额外外扩,AA 基本只会在原 footprint 内部软化.\n" +
                 "- 默认 1px,保持当前外扩 AA 的基线观感.")]
        public float LidarParticleAAFringePixels = 1.0f;

        [Tooltip("RadarScan(LiDAR) show(淡入)动画时长(秒).\n" +
                 "说明:\n" +
                 "- 仅影响 RadarScan 模式的开关淡入淡出,不影响高斯/ParticleDots 的显隐燃烧环动画(ShowDuration/HideDuration).\n" +
                 "- < 0: 复用 RenderStyleSwitchDurationSeconds.\n" +
                 "- = 0: 立即显示(无淡入).\n" +
                 "- > 0: 使用该时长淡入.")]
        public float LidarShowDuration = -1.0f;

        [Tooltip("RadarScan(LiDAR) hide(淡出)动画时长(秒).\n" +
                 "说明:\n" +
                 "- 仅影响 RadarScan 模式的开关淡入淡出.\n" +
                 "- < 0: 复用 RenderStyleSwitchDurationSeconds.\n" +
                 "- = 0: 立即隐藏(无淡出).\n" +
                 "- > 0: 使用该时长淡出.")]
        public float LidarHideDuration = -1.0f;

        [Min(0.0f)]
        [Tooltip("LiDAR show/hide 期间的噪声位移幅度(屏幕像素,px).\n" +
                 "说明:\n" +
                 "- 仅在 RadarScan(LiDAR) 播放 show/hide 过渡时生效.\n" +
                 "- 该值越大,边界附近的粒子扰动越明显.\n" +
                 "- 设为 0 可禁用位移(只保留明暗噪声).\n" +
                 "- 默认 6.")]
        public float LidarShowHideWarpPixels = 6.0f;

        [Tooltip("LiDAR show/hide 的噪声空间频率(基于 model space).\n" +
                 "说明:\n" +
                 "- 仅影响 RadarScan(LiDAR) 的 show/hide 遮罩噪声与粒子位移(warp)使用的噪声场.\n" +
                 "- < 0: 复用全局 NoiseScale.\n" +
                 "- >= 0: 使用该值作为 LiDAR 专用 NoiseScale.")]
        public float LidarShowHideNoiseScale = -1.0f;

        [Tooltip("LiDAR show/hide 的噪声随时间变化速度.\n" +
                 "说明:\n" +
                 "- 仅影响 RadarScan(LiDAR) 的 show/hide.\n" +
                 "- < 0: 复用全局 NoiseSpeed.\n" +
                 "- >= 0: 使用该值作为 LiDAR 专用 NoiseSpeed.")]
        public float LidarShowHideNoiseSpeed = -1.0f;

        [Tooltip("LiDAR show/hide 的 glow 颜色(仅影响 RadarScan 的 show/hide glow).\n" +
                 "说明:\n" +
                 "- 该参数不会影响高斯 show/hide 的 GlowColor.\n" +
                 "- alpha 通道不参与计算,仅使用 rgb.")]
        public Color LidarShowHideGlowColor = new(1.0f, 0.45f, 0.1f, 1.0f);

        [Min(0.0f)]
        [Tooltip("LiDAR show 阶段的 glow 强度(仅影响 RadarScan 的 show/hide glow).\n" +
                 "提示:\n" +
                 "- 如果你觉得 show 的 glow 不明显,优先调大该值.\n" +
                 "- 它不会影响高斯 show 的 ShowGlowIntensity.")]
        public float LidarShowGlowIntensity = 1.5f;

        [Min(0.0f)]
        [Tooltip("LiDAR hide 阶段的 glow 强度(仅影响 RadarScan 的 show/hide glow).\n" +
                 "提示:\n" +
                 "- hide 若觉得 glow 过暗或过短,可以调大该值.\n" +
                 "- 它不会影响高斯 hide 的 HideGlowIntensity.")]
        public float LidarHideGlowIntensity = 2.5f;

        [Tooltip("LiDAR 点云颜色模式.\n" +
                 "- Depth: 由距离映射颜色(DepthNear..DepthFar).\n" +
                 "- SplatColorSH0: 采样 first return 对应 splat 的基础颜色(SH0).")]
        public GsplatLidarColorMode LidarColorMode = GsplatLidarColorMode.Depth;

        [Tooltip("LiDAR Depth 模式的近处颜色.\n" +
                 "说明:\n" +
                 "- 仅在 LidarColorMode=Depth 时生效.\n" +
                 "- 保持默认青色 + 默认远处红色时,会继续沿用历史的青 -> 蓝 -> 紫 -> 红距离色带.\n" +
                 "- 一旦你改动近色或远色,Depth 模式就会按这两个颜色直接插值.")]
        public Color LidarDepthNearColor = GsplatUtils.k_LidarDepthNearColorDefault;

        [Tooltip("LiDAR Depth 模式的远处颜色.\n" +
                 "说明:\n" +
                 "- 仅在 LidarColorMode=Depth 时生效.\n" +
                 "- 保持默认红色 + 默认近处青色时,会继续沿用历史的青 -> 蓝 -> 紫 -> 红距离色带.\n" +
                 "- 一旦你改动近色或远色,Depth 模式就会按这两个颜色直接插值.")]
        public Color LidarDepthFarColor = GsplatUtils.k_LidarDepthFarColorDefault;

        [Min(0.0f)]
        [Tooltip("扫描余辉曲线指数(TrailGamma).\n" +
                 "值越大,扫描前沿越锐利,余辉衰减越快.\n" +
                 "默认 2.")]
        public float LidarTrailGamma = 2.0f;

        [Min(0.0f)]
        [Tooltip("LiDAR 点云整体强度倍率.\n" +
                 "会与余辉亮度共同作用.\n" +
                 "默认 1.")]
        public float LidarIntensity = 1.0f;

        [Tooltip("是否在下一次扫描前保留未扫到的点云(不提前消失).\n" +
                 "说明:\n" +
                 "- 关闭(默认): 行为与旧版一致,未扫到区域会随余辉衰减到 0.\n" +
                 "- 开启: 未扫到区域会以 LidarUnscannedIntensity 作为底色亮度保留.\n" +
                 "提示:\n" +
                 "- 一般建议保持 LidarIntensity >= LidarUnscannedIntensity,否则扫描头可能会反而更暗.")]
        public bool LidarKeepUnscannedPoints;

        [Min(0.0f)]
        [Tooltip("LiDAR 未扫到(或远离扫描头)区域的底色强度倍率.\n" +
                 "说明:\n" +
                 "- 用于避免\"扫过后变黑\".\n" +
                 "- 仅在 LidarKeepUnscannedPoints=true 时生效.\n" +
                 "- 默认 0.2.")]
        public float LidarUnscannedIntensity = 0.2f;

        [Tooltip("LiDAR 强度的距离衰减模式(近强远弱).\n" +
                 "说明:\n" +
                 "- Reciprocal: atten(dist)=1/(1+dist*decay).\n" +
                 "- Exponential: atten(dist)=exp(-dist*decay).\n" +
                 "提示:\n" +
                 "- decay 参数仍由 LidarIntensityDistanceDecay/LidarUnscannedIntensityDistanceDecay 提供.\n" +
                 "- 默认 Reciprocal,以兼容旧项目.")]
        public GsplatLidarDistanceDecayMode LidarIntensityDistanceDecayMode = GsplatLidarDistanceDecayMode.Reciprocal;

        [Min(0.0f)]
        [Tooltip("LiDAR 扫描强度随距离衰减的乘数(近强远弱).\n" +
                 "说明:\n" +
                 "- 0: 不衰减(兼容旧项目).\n" +
                 "- >0: 按距离做衰减.\n" +
                 "衰减函数:\n" +
                 "- 由 LidarIntensityDistanceDecayMode 决定.\n" +
                 "  - Reciprocal: atten(dist)=1/(1+dist*decay).\n" +
                 "  - Exponential: atten(dist)=exp(-dist*decay).\n" +
                 "提示:\n" +
                 "- dist 使用当前点的 range(单位与 DepthNear/Far 一致,通常是米).")]
        public float LidarIntensityDistanceDecay;

        [Min(0.0f)]
        [Tooltip("LiDAR 未扫到区域底色强度随距离衰减的乘数(近强远弱).\n" +
                 "说明:\n" +
                 "- 0: 不衰减(兼容旧项目).\n" +
                 "- >0: 按距离做衰减.\n" +
                 "- 仅在 LidarKeepUnscannedPoints=true 时最终可见(否则底色强度本身会被置 0).\n" +
                 "衰减函数:\n" +
                 "- 由 LidarIntensityDistanceDecayMode 决定.\n" +
                 "  - Reciprocal: atten(dist)=1/(1+dist*decay).\n" +
                 "  - Exponential: atten(dist)=exp(-dist*decay).\n" +
                 "提示:\n" +
                 "- 一般建议该值 <= LidarIntensityDistanceDecay,让远处的底色衰减更快一些.")]
        public float LidarUnscannedIntensityDistanceDecay;

        [Range(0.0f, 1.0f)]
        [Tooltip("LiDAR Depth 模式下的点云透明度(0..1).\n" +
                 "说明:\n" +
                 "- 仅在 LidarColorMode=Depth 时生效.\n" +
                 "- 该值直接参与 alpha blending: 1 为不透明,0 为不可见.\n" +
                 "- 默认 1(不透明).")]
        public float LidarDepthOpacity = 1.0f;

        [Range(0.0f, 1.0f)]
        [Tooltip("LiDAR 采样时的最小 splat opacity 阈值.\n" +
                 "说明:\n" +
                 "- 用于过滤掉几乎不可见的 splats,避免 first return 命中\"透明噪声外壳\".\n" +
                 "- 默认 1/255(与主 splat shader 的 discard 阈值对齐).")]
        public float LidarMinSplatOpacity = 1.0f / 255.0f;

        [Tooltip("当 EnableLidarScan=true 时,是否隐藏 splat 的 sort/draw(仅显示 LiDAR 点云).\n" +
                 "说明:\n" +
                 "- 开启后,仍会保持 splat GPU buffers(用于 LiDAR 采样),但不会提交 splat 的排序与绘制.\n" +
                 "- 默认关闭,以保持旧行为不变.")]
        public bool HideSplatsWhenLidarEnabled;

        // --------------------------------------------------------------------
        // 可选: 显隐燃烧环动画(show/hide)
        // - 默认关闭,不影响旧行为与性能.
        // - 序列后端的 decode 也会在 Hidden 状态被停掉,避免“不可见但仍在解码/排序”的浪费.
        // --------------------------------------------------------------------
        [Header("Visibility Animation (Burn Reveal)")]
        [Tooltip("是否启用显隐燃烧环动画. 默认关闭,不影响旧行为与性能.")]
        public bool EnableVisibilityAnimation;

        [Tooltip("显隐动画中心.\n" +
                 "- 不为空: 以该 Transform 为中心.\n" +
                 "- 为空: 回退使用序列 union bounds.center.")]
        public Transform VisibilityCenter;

        [Tooltip("启用组件时是否自动播放 show 动画(从隐藏->显示).\n" +
                 "仅在 EnableVisibilityAnimation=true 时生效.")]
        public bool PlayShowOnEnable = true;

        [Tooltip("显隐动画进度的驱动方式.\n" +
                 "说明:\n" +
                 "- WorldSpeed: 按目标世界空间前沿速度推进,不同尺度的 3DGS 更容易得到接近的 reveal 形态.\n" +
                 "- LegacyDuration: 兼容旧行为,继续按 ShowDuration / HideDuration 直接推进 progress.\n" +
                 "- 默认按当前项目里 ckpt 参考效果切到 WorldSpeed.")]
        public GsplatVisibilityProgressMode VisibilityProgressMode = GsplatVisibilityProgressMode.WorldSpeed;

        [Min(0.0f)]
        [Tooltip("显隐 reveal 半径的额外倍率.\n" +
                 "说明:\n" +
                 "- 1: 使用序列 union bounds 推导出的原始 reveal 半径.\n" +
                 "- <1: reveal 球更小,同时在 WorldSpeed 模式下也会更快播完.\n" +
                 "- >1: reveal 球更大,同时在 WorldSpeed 模式下会播得更久.")]
        public float VisibilityRadiusScale = GsplatVisibilityAnimationUtil.k_DefaultVisibilityRadiusScale;

        [Min(0.0f)]
        [Tooltip("show 阶段的目标世界空间前沿速度(units/s).\n" +
                 "说明:\n" +
                 "- 仅在 VisibilityProgressMode=WorldSpeed 时生效.\n" +
                 "- 默认值按当前项目里 `ckpt_29999_v2_sh3_seg50_k512_f32_colmap_latest` 的现有效果标定.\n" +
                 "- 值越大,show 的球形扩张越快.")]
        public float ShowWorldSpeed = GsplatVisibilityAnimationUtil.k_DefaultShowWorldSpeed;

        [Min(0.0f)]
        [Tooltip("hide 阶段的目标世界空间前沿速度(units/s).\n" +
                 "说明:\n" +
                 "- 仅在 VisibilityProgressMode=WorldSpeed 时生效.\n" +
                 "- 默认值同样按当前项目里 ckpt 的现有效果标定.\n" +
                 "- 值越大,hide 的燃烧收束越快.")]
        public float HideWorldSpeed = GsplatVisibilityAnimationUtil.k_DefaultHideWorldSpeed;

        [Min(0.0f)]
        [Tooltip("show 动画时长(秒).\n" +
                 "说明:\n" +
                 "- 仅在 VisibilityProgressMode=LegacyDuration 时使用.\n" +
                 "- WorldSpeed 模式下,show 的总时长会由目标世界速度和 reveal 半径共同决定.")]
        public float ShowDuration = 1.0f;

        [Min(0.0f)]
        [Tooltip("hide 动画时长(秒).\n" +
                 "说明:\n" +
                 "- 仅在 VisibilityProgressMode=LegacyDuration 时使用.\n" +
                 "- WorldSpeed 模式下,hide 的总时长会由目标世界速度和 reveal 半径共同决定.")]
        public float HideDuration = 1.2f;

        [Range(0.0f, 1.0f)]
        [Tooltip("双向 dual-track 切换里“第二效果”开始介入的共享触发进度(0..1).\n" +
                 "说明:\n" +
                 "- `show-hide-switch-高斯` 与 `show-hide-switch-雷达` 共用这一条阈值.\n" +
                 "- 0 表示第二效果立刻开始,1 表示等第一效果完全结束才开始.\n" +
                 "- 默认 0.35,对应当前已验证过的 overlap 手感.")]
        public float DualTrackSwitchTriggerProgress01 = k_defaultDualTrackSwitchTriggerProgress01;

        [Range(0.0f, 1.0f)]
        [FormerlySerializedAs("RingWidthNormalized")]
        [Tooltip("show: 燃烧环宽度(径向空间宽度,相对 maxRadius 的比例).\n" +
                 "注意: 这不是粒子大小,它只影响“燃烧前沿在空间中有多厚”.")]
        public float ShowRingWidthNormalized = 0.06f;

        [Range(0.0f, 1.0f)]
        [FormerlySerializedAs("TrailWidthNormalized")]
        [Tooltip("show: 燃烧拖尾宽度(径向空间宽度,相对 maxRadius 的比例).\n" +
                 "它决定被扫过区域“慢慢透明/消失”的距离尺度.\n" +
                 "注意: 这不是粒子大小,它只影响“拖尾在空间中有多宽”.")]
        public float ShowTrailWidthNormalized = 0.12f;

        [Range(0.0f, 1.0f)]
        [Tooltip("hide: 燃烧环宽度(径向空间宽度,相对 maxRadius 的比例).")]
        public float HideRingWidthNormalized = 0.06f;

        [Range(0.0f, 1.0f)]
        [Tooltip("hide: 燃烧拖尾宽度(径向空间宽度,相对 maxRadius 的比例).\n" +
                 "它决定被扫过区域“慢慢透明/消失”的距离尺度.")]
        public float HideTrailWidthNormalized = 0.12f;

        [Range(0.0f, 1.0f)]
        [Tooltip("show: 高斯基元(粒子)最小尺寸(相对正常尺寸).\n" +
                 "说明:\n" +
                 "- 该参数控制 show 阶段 splat 从“极小”长到正常的起点.\n" +
                 "- 值越小,\"从无到有\"越明显,但 ring 前沿更容易看成小点点.\n" +
                 "- 值越大,更容易看清 ring,但“从极小开始”的感觉会变弱.")]
        public float ShowSplatMinScale = 0.001f;

        [Range(0.0f, 1.0f)]
        [Tooltip("show: 燃烧前沿 ring 阶段的粒子最小尺寸(相对正常尺寸).\n" +
                 "说明:\n" +
                 "- 用于解决 \"ring 阶段全是很小的点点\" 的体感.\n" +
                 "- 它只影响 ring 前沿附近,不会改变已稳定显示区域(最终仍会长到 1.0).")]
        public float ShowRingSplatMinScale = 0.033f;

        [Range(0.0f, 1.0f)]
        [Tooltip("show: 内侧 afterglow/tail 阶段的粒子最小尺寸(相对正常尺寸).\n" +
                 "说明:\n" +
                 "- 用于控制余辉/拖尾区域的粒子“粗细”.\n" +
                 "- 建议小于 ShowRingSplatMinScale,让前沿更突出,拖尾更细腻.")]
        public float ShowTrailSplatMinScale = 0.0132f;

        [Range(0.0f, 1.0f)]
        [Tooltip("hide: 燃烧期间的粒子最小尺寸(相对正常尺寸).\n" +
                 "说明:\n" +
                 "- 值越小,燃烧时越快变小.\n" +
                 "- 但过小会让 \"看起来消失太快\" 更明显,通常需要配合 alpha trail 一起调参.")]
        public float HideSplatMinScale = 0.06f;

        [Tooltip("燃烧发光颜色.")]
        public Color GlowColor = new(1.0f, 0.45f, 0.1f, 1.0f);

        [Min(0.0f)]
        [Tooltip("show 阶段的发光强度.")]
        public float ShowGlowIntensity = 1.5f;

        [Min(0.0f)]
        [Tooltip("show 起始阶段额外亮度倍数(>=1 更像“点燃瞬间更亮”).\n" +
                 "说明: 该参数主要用于放大 show 的燃烧环前沿 glow,让中心起燃更有冲击力.\n" +
                 "若你希望全程更亮,请优先调大 ShowGlowIntensity.")]
        public float ShowGlowStartBoost = 1.0f;

        [Range(0.0f, 3.0f)]
        [Tooltip("show: 燃烧前沿 ring 的“星火闪烁”强度(0=关闭).\n" +
                 "说明:\n" +
                 "- 该效果会用 curl-like 噪声场调制 ring glow 亮度,让它像火星/星星一样闪烁.\n" +
                 "- 只影响 show 的 ring 前沿,不改变 hide.\n" +
                 "- 值越大,闪烁越明显,但 show 阶段的 shader 计算也会更重一些.")]
        public float ShowGlowSparkleStrength = 0.0f;

        [Min(0.0f)]
        [Tooltip("hide 阶段的发光强度.")]
        public float HideGlowIntensity = 2.5f;

        [Min(0.0f)]
        [Tooltip("hide 起始阶段额外亮度倍数(>1 更像“先高亮燃烧”).")]
        public float HideGlowStartBoost = 2.0f;

        [Range(0.0f, 1.0f)]
        [Tooltip("噪波强度(0..1).\n" +
                 "- show: 越往后越弱.\n" +
                 "- hide: 越往后越强(更碎屑).")]
        public float NoiseStrength = 0.6f;

        [Min(0.0f)]
        [Tooltip("噪波空间频率(基于 model space).")]
        public float NoiseScale = 1.0f;

        [Min(0.0f)]
        [Tooltip("噪波随时间变化速度.")]
        public float NoiseSpeed = 1.0f;

        [Range(0.0f, 3.0f)]
        [Tooltip("空间扭曲(位移)强度倍率(0..3).\n" +
                 "说明: 该参数只影响 show/hide 期间的 position warp,不影响 alpha 的灰烬颗粒/边界抖动.\n" +
                 "- 0: 禁用位移扭曲.\n" +
                 "- 1: 默认强度.\n" +
                 "- 2~3: 更明显的空间扭曲(可能需要更保守的 bounds 扩展).")]
        public float WarpStrength = 2.0f;

        [Tooltip("显隐动画的噪声类型(主要影响 position warp 的噪声场).\n" +
                 "- ValueSmoke: 当前默认,更平滑更像烟雾.\n" +
                 "- CurlSmoke: 更像连续的旋涡/流动(开销更高).\n" +
                 "- HashLegacy: 旧版对照,更碎更抖.")]
        public GsplatVisibilityNoiseMode VisibilityNoiseMode = GsplatVisibilityNoiseMode.ValueSmoke;

        // --------------------------------------------------------------------
        // 显隐燃烧环动画 runtime 状态(非序列化)
        // --------------------------------------------------------------------
        enum VisibilityAnimState
        {
            Visible = 0,
            Hidden = 1,
            Showing = 2,
            Hiding = 3,
        }

        // --------------------------------------------------------------------
        // source mask:
        // - 解决 show/hide 中途切换时的整片突变.
        // - 通过保留“切换前可见分布”做叠加合成.
        // --------------------------------------------------------------------
        enum VisibilitySourceMaskMode
        {
            None = 0,
            FullVisible = 1,
            FullHidden = 2,
            ShowSnapshot = 3,
            HideSnapshot = 4,
        }

        VisibilityAnimState m_visibilityState = VisibilityAnimState.Visible;
        float m_visibilityProgress01 = 1.0f;
        float m_visibilityLastAdvanceRealtime = -1.0f;
        VisibilitySourceMaskMode m_visibilitySourceMaskMode = VisibilitySourceMaskMode.FullVisible;
        float m_visibilitySourceMaskProgress01 = 1.0f;

        // --------------------------------------------------------------------
        // Render style 切换 runtime 状态(非序列化):
        // - m_renderStyleBlend01:
        //   - 控制几何/轮廓 morph
        // - m_renderStyleAlphaBlend01:
        //   - 控制 alpha handoff
        //   - 允许比几何 morph 更柔和,降低“旧形态突然断电”的体感
        // --------------------------------------------------------------------
        float m_renderStyleBlend01;
        float m_renderStyleAlphaBlend01;
        bool m_renderStyleAnimating;
        float m_renderStyleAnimProgress01;
        float m_renderStyleAnimStartBlend01;
        float m_renderStyleAnimStartAlphaBlend01;
        float m_renderStyleAnimTargetBlend01;
        float m_renderStyleAnimDurationSeconds = 1.5f;
        float m_renderStyleLastAdvanceRealtime = -1.0f;

        // --------------------------------------------------------------------
        // LiDAR 动画状态(非序列化):
        // - m_lidarColorBlend01:
        //   - 0 = Depth
        //   - 1 = SplatColorSH0
        // - m_lidarVisibility01:
        //   - 0 = 雷达效果不可见
        //   - 1 = 雷达效果完全可见
        // --------------------------------------------------------------------
        const float k_lidarColorSwitchDurationSeconds = 0.35f;
        float m_lidarColorBlend01;
        bool m_lidarColorAnimating;
        float m_lidarColorAnimProgress01;
        float m_lidarColorAnimStartBlend01;
        float m_lidarColorAnimTargetBlend01;
        float m_lidarColorAnimDurationSeconds = k_lidarColorSwitchDurationSeconds;
        float m_lidarColorLastAdvanceRealtime = -1.0f;

        float m_lidarVisibility01;
        bool m_lidarVisibilityAnimating;
        float m_lidarVisibilityAnimProgress01;
        float m_lidarVisibilityAnimStart01;
        float m_lidarVisibilityAnimTarget01;
        float m_lidarVisibilityAnimDurationSeconds = 1.5f;
        float m_lidarVisibilityLastAdvanceRealtime = -1.0f;
        const float k_lidarHideSplatsAfterVisibility01 = 0.999f;

        // 关闭雷达时,为了播放淡出动画,会暂时保持 runtime draw 链路在线.
        bool m_lidarKeepAliveDuringFadeOut;

        // --------------------------------------------------------------------
        // RadarScan -> Gaussian 的 show/hide 编排状态(非序列化)
        // --------------------------------------------------------------------
        bool m_pendingRadarToGaussianShowSwitch;
        bool m_pendingRadarToGaussianDisableLidar;
        bool m_radarToGaussianLidarHideOverlayActive;
        float m_radarToGaussianLidarHideOverlayProgress01 = 1.0f;
        float m_radarToGaussianLidarHideOverlayLastAdvanceRealtime = -1.0f;
        VisibilitySourceMaskMode m_radarToGaussianLidarHideOverlaySourceMaskMode = VisibilitySourceMaskMode.FullVisible;
        float m_radarToGaussianLidarHideOverlaySourceMaskProgress01 = 1.0f;

        // --------------------------------------------------------------------
        // Gaussian -> RadarScan 的 show/hide 编排状态(非序列化)
        // --------------------------------------------------------------------
        bool m_pendingGaussianToRadarShowSwitch;
        bool m_pendingGaussianToRadarFinalizeRadarMode;
        bool m_gaussianToRadarLidarShowOverlayActive;
        float m_gaussianToRadarLidarShowOverlayProgress01 = 1.0f;
        float m_gaussianToRadarLidarShowOverlayLastAdvanceRealtime = -1.0f;
        VisibilitySourceMaskMode m_gaussianToRadarLidarShowOverlaySourceMaskMode =
            VisibilitySourceMaskMode.FullHidden;
        float m_gaussianToRadarLidarShowOverlaySourceMaskProgress01;

#if UNITY_EDITOR
        // --------------------------------------------------------------------
        // Editor 体验修复: 让 show/hide 动画在“鼠标不动”时也能连续播放.
        //
        // 说明:
        // - 原因与 `GsplatRenderer` 完全一致: EditMode 下视口 repaint 往往是事件驱动,
        //   如果没有鼠标交互,show/hide 这种纯 shader/uniform 动画就会“看起来不动”.
        // - 这里复用同样策略: 仅在 Showing/Hiding 期间主动 Repaint,并在结束时补 1 次刷新.
        // --------------------------------------------------------------------
        double m_visibilityEditorLastRepaintTime = -1.0;
        double m_visibilityEditorLastDiagTickTime = -1.0;

        // --------------------------------------------------------------------
        // Editor update ticker(关键补强):
        // - 仅靠 Update/相机回调触发 repaint,某些编辑器状态下仍可能只刷一次就停,导致动画看起来“卡住”.
        // - 因此增加 EditorApplication.update 驱动的 ticker:
        //   - 只在 Showing/Hiding 期间注册.
        //   - 主动推进状态机并触发 repaint.
        //   - 动画结束后自动注销,避免空闲耗电.
        // --------------------------------------------------------------------
        static readonly HashSet<GsplatSequenceRenderer> s_visibilityEditorTickers = new();
        static readonly List<GsplatSequenceRenderer> s_visibilityEditorTickersToRemove = new();
        static bool s_visibilityEditorUpdateHooked;
        static double s_visibilityEditorLastTickTime;

        bool IsAnyAnimationActiveForEditorTicker()
        {
            if (EnableVisibilityAnimation &&
                (m_visibilityState == VisibilityAnimState.Showing || m_visibilityState == VisibilityAnimState.Hiding))
            {
                return true;
            }

            if (m_renderStyleAnimating)
                return true;

            if (m_lidarColorAnimating || m_lidarVisibilityAnimating)
                return true;

            if (IsRadarToGaussianDualTrackActive() || IsGaussianToRadarDualTrackActive())
            {
                return true;
            }

            // LiDAR 扫描前沿(亮度余辉)是纯时间驱动:
            // - EditMode 下如果没有持续 repaint,会出现“鼠标不动就不动”的错觉.
            // - 因此当雷达 runtime 链路仍在(包括 fade-out 期间)且已指定 Origin 时,都需要 ticker 驱动 Repaint.
            if (IsLidarRuntimeActive() && ResolveConfiguredLidarSensorTransformOrNull())
                return true;

            return false;
        }

        bool IsLidarRuntimeActive()
        {
            return EnableLidarScan || m_lidarKeepAliveDuringFadeOut;
        }

        bool ShouldDelayHideSplatsForLidarFadeIn()
        {
            // 入雷达时避免黑场:
            // - `HideSplatsWhenLidarEnabled` 不能在雷达可见性还是 0 的瞬间就停掉 splat.
            // - 这里与静态后端保持一致:
            //   1) 雷达 fade-in 未完成时,继续保留 splat
            //   2) Gaussian -> ParticleDots 的 render-style 退场未完成时,也继续保留 splat
            // - 否则高斯 alpha 还没退完就会像被瞬间掐掉.
            if (!EnableLidarScan || !HideSplatsWhenLidarEnabled)
                return false;

            var lidarFadeInStillRunning =
                m_lidarVisibilityAnimating &&
                m_lidarVisibilityAnimTarget01 >= 0.999f &&
                m_lidarVisibility01 < k_lidarHideSplatsAfterVisibility01;

            var gaussianAlphaFadeStillRunning =
                m_renderStyleAnimating &&
                m_renderStyleAnimTargetBlend01 >= 0.999f;

            return lidarFadeInStillRunning || gaussianAlphaFadeStillRunning;
        }

        static void EnsureVisibilityEditorUpdateHooked()
        {
            if (s_visibilityEditorUpdateHooked)
                return;

            s_visibilityEditorUpdateHooked = true;
            UnityEditor.EditorApplication.update += TickVisibilityAnimationsInEditor;
        }

        static void UnhookVisibilityEditorUpdateIfIdle()
        {
            if (!s_visibilityEditorUpdateHooked || s_visibilityEditorTickers.Count != 0)
                return;

            UnityEditor.EditorApplication.update -= TickVisibilityAnimationsInEditor;
            s_visibilityEditorUpdateHooked = false;
        }

        static void TickVisibilityAnimationsInEditor()
        {
            if (Application.isPlaying || Application.isBatchMode)
            {
                s_visibilityEditorTickers.Clear();
                s_visibilityEditorTickersToRemove.Clear();
                if (s_visibilityEditorUpdateHooked)
                {
                    UnityEditor.EditorApplication.update -= TickVisibilityAnimationsInEditor;
                    s_visibilityEditorUpdateHooked = false;
                }
                return;
            }

            if (s_visibilityEditorTickers.Count == 0)
            {
                UnhookVisibilityEditorUpdateIfIdle();
                return;
            }

            const double kMinInterval = 1.0 / 60.0;
            var now = UnityEditor.EditorApplication.timeSinceStartup;
            if (now - s_visibilityEditorLastTickTime < kMinInterval)
                return;
            s_visibilityEditorLastTickTime = now;

            s_visibilityEditorTickersToRemove.Clear();
            foreach (var r in s_visibilityEditorTickers)
            {
                if (!r || !r.isActiveAndEnabled)
                {
                    s_visibilityEditorTickersToRemove.Add(r);
                    continue;
                }

                if (!r.IsAnyAnimationActiveForEditorTicker())
                {
                    s_visibilityEditorTickersToRemove.Add(r);
                    continue;
                }

                r.AdvancePendingRadarToGaussianShowSwitchIfNeeded();
                r.AdvancePendingGaussianToRadarShowSwitchIfNeeded();
                r.AdvanceVisibilityStateIfNeeded();
                r.AdvanceRenderStyleStateIfNeeded();
                r.AdvanceLidarAnimationStateIfNeeded();

                // LiDAR 扫描前沿需要持续 repaint 才能看到"旋转"与余辉变化(尤其是 EditMode).
                if (r.IsLidarRuntimeActive() && r.ResolveConfiguredLidarSensorTransformOrNull())
                    r.RequestEditorRepaintForVisibilityAnimation(force: false, reason: "LiDAR");

                // 诊断(可选): 仅在 EnableEditorDiagnostics=true 时记录,并做额外节流.
                if (GsplatEditorDiagnostics.Enabled)
                {
                    const double kDiagInterval = 0.25;
                    if (r.m_visibilityEditorLastDiagTickTime < 0.0 ||
                        now - r.m_visibilityEditorLastDiagTickTime >= kDiagInterval)
                    {
                        r.m_visibilityEditorLastDiagTickTime = now;
                        if (r.EnableVisibilityAnimation &&
                            (r.m_visibilityState == VisibilityAnimState.Showing ||
                             r.m_visibilityState == VisibilityAnimState.Hiding))
                        {
                            GsplatEditorDiagnostics.MarkVisibilityState(r, "ticker.tick",
                                r.m_visibilityState.ToString(), r.m_visibilityProgress01);
                        }
                    }
                }

                if (!r.IsAnyAnimationActiveForEditorTicker())
                {
                    s_visibilityEditorTickersToRemove.Add(r);
                }
            }

            foreach (var r in s_visibilityEditorTickersToRemove)
                s_visibilityEditorTickers.Remove(r);

            UnhookVisibilityEditorUpdateIfIdle();
        }

        void RegisterVisibilityEditorTickerIfAnimating()
        {
            if (Application.isPlaying || Application.isBatchMode)
                return;

            if (!IsAnyAnimationActiveForEditorTicker())
                return;

            EnsureVisibilityEditorUpdateHooked();
            s_visibilityEditorTickers.Add(this);

            if (GsplatEditorDiagnostics.Enabled)
            {
                GsplatEditorDiagnostics.MarkVisibilityState(this, "ticker.register",
                    m_visibilityState.ToString(), m_visibilityProgress01);
            }
        }

        void UnregisterVisibilityEditorTickerIfAny()
        {
            if (s_visibilityEditorTickers.Remove(this) && GsplatEditorDiagnostics.Enabled)
            {
                GsplatEditorDiagnostics.MarkVisibilityState(this, "ticker.unregister",
                    m_visibilityState.ToString(), m_visibilityProgress01);
            }
            UnhookVisibilityEditorUpdateIfIdle();
        }

        void RequestEditorRepaintForVisibilityAnimation(bool force = false, string reason = "VisibilityAnim")
        {
            if (Application.isPlaying)
                return;

            if (Application.isBatchMode)
                return;

            const double kMinInterval = 1.0 / 60.0;
            var now = UnityEditor.EditorApplication.timeSinceStartup;
            if (!force && m_visibilityEditorLastRepaintTime > 0.0 &&
                now - m_visibilityEditorLastRepaintTime < kMinInterval)
                return;

            m_visibilityEditorLastRepaintTime = now;

            if (GsplatEditorDiagnostics.Enabled)
            {
                GsplatEditorDiagnostics.MarkVisibilityRepaint(this, reason, force,
                    m_visibilityState.ToString(), m_visibilityProgress01);
            }

            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
#endif

        GsplatSequenceAsset m_prevAsset;
        GsplatRendererImpl m_renderer;

        // --------------------------------------------------------------------
        // LiDAR runtime(非序列化):
        // - 负责 range image/LUT 等 GPU 资源的生命周期管理.
        // - compute dispatch 与 draw call 会在后续 tasks 中实现,这里先把资源管理打底.
        // --------------------------------------------------------------------
        GsplatLidarScan m_lidarScan;
        GsplatLidarExternalTargetHelper m_lidarExternalTargetHelper;
        GsplatLidarExternalGpuCapture m_lidarExternalGpuCapture;
        bool m_lidarLoggedMissingOrigin;
        GameObject[] m_lidarCombinedExternalTargetsScratch = Array.Empty<GameObject>();

        bool m_disabledDueToError;
        float m_timeNormalizedThisFrame;
        float m_nextRendererRecoveryTime;

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
        int m_kernelDecodeSHV2 = -1;

        GraphicsBuffer m_scaleCodebookBuffer;
        GraphicsBuffer m_sh0CodebookBuffer;

        // v1: 单一 shN centroids
        GraphicsBuffer m_shNCentroidsBuffer;

        // v2: SH rest 按 band 拆分为 sh1/sh2/sh3 三套 centroids
        GraphicsBuffer m_sh1CentroidsBuffer;
        GraphicsBuffer m_sh2CentroidsBuffer;
        GraphicsBuffer m_sh3CentroidsBuffer;

        public bool Valid =>
            (EnableGsplatBackend || IsLidarRuntimeActive()) &&
            // burn reveal 的 Hidden 本质是“停止 splat 的 sort/draw”.
            // 但 LiDAR 是独立显示模式: 当 EnableLidarScan=true 时,仍允许组件参与回调链路与点云渲染.
            (IsLidarRuntimeActive() || m_visibilityState != VisibilityAnimState.Hidden) &&
            !m_disabledDueToError &&
            SequenceAsset &&
            m_renderer != null &&
            m_decodeCS != null;

        public uint SplatCount => SequenceAsset ? m_effectiveSplatCount : 0;
        uint IGsplat.SplatCount => ShouldSubmitSplatsThisFrame() ? SplatCount : 0;
        uint IGsplat.SplatBaseIndex => 0;

        public ISorterResource SorterResource => m_renderer != null ? m_renderer.SorterResource : null;

        // 4DGS buffers(本序列后端不使用 velocity/time/duration,但仍返回 dummy buffer 以避免 compute 绑定报错).
        bool IGsplat.Has4D => m_renderer != null && m_renderer.Has4D;
        float IGsplat.TimeNormalized => m_timeNormalizedThisFrame;
        int IGsplat.TimeModel => 1; // sequence 后端不使用 4D 时间核,保持 window 默认值
        float IGsplat.TemporalCutoff => 0.01f;
        GraphicsBuffer IGsplat.VelocityBuffer => m_renderer != null ? m_renderer.VelocityBuffer : null;
        GraphicsBuffer IGsplat.TimeBuffer => m_renderer != null ? m_renderer.TimeBuffer : null;
        GraphicsBuffer IGsplat.DurationBuffer => m_renderer != null ? m_renderer.DurationBuffer : null;

        void IGsplatRenderSubmitter.SubmitDrawForCamera(Camera camera)
        {
            // 说明:
            // - 与 `GsplatRenderer` 一致,该回调仅用于 Editor 非 Play 模式的“相机回调驱动渲染”.
            // - 序列后端的 decode 仍在 Update 中执行,这里仅提交 draw(避免重复解码与重复渲染).
            if (Application.isPlaying)
                return;

            AdvancePendingRadarToGaussianShowSwitchIfNeeded();
            AdvancePendingGaussianToRadarShowSwitchIfNeeded();
            AdvanceVisibilityStateIfNeeded();
            AdvanceRenderStyleStateIfNeeded();
            AdvanceLidarAnimationStateIfNeeded();

            if (!Valid || m_renderer == null || !m_renderer.Valid || !SequenceAsset)
                return;

            // splat draw:
            // - HideSplatsWhenLidarEnabled=true 时仅显示 LiDAR 点云,不提交 splat 的 sort/draw.
            if (ShouldSubmitSplatsThisFrame())
            {
                var localBounds = ResolveVisibilityLocalBoundsForThisFrame();
                PushVisibilityUniformsForThisFrame(localBounds);
                PushRenderStyleUniformsForThisFrame();

                var boundsForRender = CalcVisibilityExpandedRenderBounds(localBounds);
                m_renderer.RenderForCamera(camera, SplatCount, transform, boundsForRender, gameObject.layer,
                    GammaToLinear, EnableFootprintAACompensation, SHDegree, m_timeNormalizedThisFrame, motionPadding: 0.0f,
                    timeModel: 1, temporalCutoff: 0.01f,
                    diagTag: "EditMode.CameraCallback");
            }

            // LiDAR 点云也需要在相机回调链路提交 draw,避免 EditMode 下多次 beginCameraRendering 时闪烁.
            RenderLidarForCamera(camera);
        }

        // 公开 GPU buffers,用于调试/扩展后端(例如 VFX)绑定等场景.
        public GraphicsBuffer PositionBuffer => m_renderer != null ? m_renderer.PositionBuffer : null;
        public GraphicsBuffer ScaleBuffer => m_renderer != null ? m_renderer.ScaleBuffer : null;
        public GraphicsBuffer RotationBuffer => m_renderer != null ? m_renderer.RotationBuffer : null;
        public GraphicsBuffer ColorBuffer => m_renderer != null ? m_renderer.ColorBuffer : null;
        public GraphicsBuffer SHBuffer => m_renderer != null ? m_renderer.SHBuffer : null;
        public GraphicsBuffer OrderBuffer => m_renderer != null ? m_renderer.OrderBuffer : null;
        public byte EffectiveSHBands => m_renderer != null ? m_renderer.SHBands : (byte)0;

        // --------------------------------------------------------------------
        // Public API: Render style 切换(Gaussian <-> ParticleDots)
        // --------------------------------------------------------------------
        float ResolveRadarScanVisibilityDurationSeconds(bool enableRadarScan, float durationSeconds)
        {
            // 说明: 与 GsplatRenderer 的同名逻辑保持一致.
            var d = durationSeconds;
            if (d < 0.0f)
            {
                var overrideD = enableRadarScan ? LidarShowDuration : LidarHideDuration;
                if (!float.IsNaN(overrideD) && !float.IsInfinity(overrideD) && overrideD >= 0.0f)
                    d = overrideD;
                else
                    d = RenderStyleSwitchDurationSeconds;
            }

            if (float.IsNaN(d) || float.IsInfinity(d) || d < 0.0f)
                d = 0.0f;
            return d;
        }

        float ResolveLidarUnscannedIntensityForShader()
        {
            // 说明:
            // - 序列后端与单帧后端保持一致:
            //   - 用户希望"扫过后不要变黑".
            //   - 并且需要一个选项,控制"是否在下一次扫描前消失".
            if (!LidarKeepUnscannedPoints)
                return 0.0f;

            var v = LidarUnscannedIntensity;
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0.0f)
                v = 0.2f;
            return v;
        }

        bool HasRadarVisibilityToExit()
        {
            if (EnableLidarScan || m_lidarKeepAliveDuringFadeOut)
                return true;

            if (m_lidarVisibilityAnimating)
                return true;

            return m_lidarVisibility01 > 1.0e-4f;
        }

        float GetDualTrackSwitchTriggerProgress01()
        {
            var progress01 = DualTrackSwitchTriggerProgress01;
            if (float.IsNaN(progress01) || float.IsInfinity(progress01))
                progress01 = k_defaultDualTrackSwitchTriggerProgress01;
            return Mathf.Clamp01(progress01);
        }

        bool HasGaussianMainVisualToExit()
        {
            if (m_visibilityState == VisibilityAnimState.Hidden)
                return false;

            if (RenderStyle == GsplatRenderStyle.Gaussian)
                return true;

            if (m_renderStyleAnimating && m_renderStyleAnimTargetBlend01 < 0.999f)
                return true;

            return m_renderStyleBlend01 < 0.999f;
        }

        bool IsRadarToGaussianDualTrackActive()
        {
            return m_pendingRadarToGaussianShowSwitch ||
                   m_pendingRadarToGaussianDisableLidar ||
                   m_radarToGaussianLidarHideOverlayActive;
        }

        bool IsGaussianToRadarDualTrackActive()
        {
            return m_pendingGaussianToRadarShowSwitch ||
                   m_pendingGaussianToRadarFinalizeRadarMode ||
                   m_gaussianToRadarLidarShowOverlayActive;
        }

        void ApplyVisibilityStableState(bool visible)
        {
            m_visibilityState = visible ? VisibilityAnimState.Visible : VisibilityAnimState.Hidden;
            m_visibilityProgress01 = 1.0f;
            m_visibilityLastAdvanceRealtime = -1.0f;
            SetVisibilitySourceMask(visible
                ? VisibilitySourceMaskMode.FullVisible
                : VisibilitySourceMaskMode.FullHidden);
        }

        void ResetRadarToGaussianLidarHideOverlayState()
        {
            // ----------------------------------------------------------------
            // 双轨切换的专用 hide overlay 状态:
            // - 只服务于 "RadarScan -> Gaussian" 这条 overlap 切换路径.
            // - 一旦切换被取消/完成,统一回到稳定默认值,避免残留脏状态污染下一次按钮触发.
            // ----------------------------------------------------------------
            m_radarToGaussianLidarHideOverlayActive = false;
            m_radarToGaussianLidarHideOverlayProgress01 = 1.0f;
            m_radarToGaussianLidarHideOverlayLastAdvanceRealtime = -1.0f;
            m_radarToGaussianLidarHideOverlaySourceMaskMode = VisibilitySourceMaskMode.FullVisible;
            m_radarToGaussianLidarHideOverlaySourceMaskProgress01 = 1.0f;
        }

        void ResetGaussianToRadarLidarShowOverlayState()
        {
            // ----------------------------------------------------------------
            // 反向 dual-track 的专用 show overlay:
            // - 只服务于 "Gaussian -> RadarScan" 这条 overlap 切换路径.
            // - 取消/完成后统一归零,避免把 show mode 残留到普通雷达模式里.
            // ----------------------------------------------------------------
            m_gaussianToRadarLidarShowOverlayActive = false;
            m_gaussianToRadarLidarShowOverlayProgress01 = 1.0f;
            m_gaussianToRadarLidarShowOverlayLastAdvanceRealtime = -1.0f;
            m_gaussianToRadarLidarShowOverlaySourceMaskMode = VisibilitySourceMaskMode.FullHidden;
            m_gaussianToRadarLidarShowOverlaySourceMaskProgress01 = 0.0f;
        }

        void CaptureRadarToGaussianLidarHideOverlayFromCurrentHideState()
        {
            if (!EnableVisibilityAnimation)
            {
                ResetRadarToGaussianLidarHideOverlayState();
                return;
            }

            var progress01 = Mathf.Clamp01(m_visibilityProgress01);
            if (m_visibilityState == VisibilityAnimState.Hidden)
                progress01 = 1.0f;

            m_radarToGaussianLidarHideOverlayActive = progress01 < 1.0f;
            m_radarToGaussianLidarHideOverlayProgress01 = progress01;
            m_radarToGaussianLidarHideOverlayLastAdvanceRealtime = -1.0f;
            m_radarToGaussianLidarHideOverlaySourceMaskMode = m_visibilitySourceMaskMode;
            m_radarToGaussianLidarHideOverlaySourceMaskProgress01 =
                Mathf.Clamp01(m_visibilitySourceMaskProgress01);
        }

        void AdvanceRadarToGaussianLidarHideOverlayIfNeeded()
        {
            if (!m_radarToGaussianLidarHideOverlayActive)
                return;

            if (!EnableVisibilityAnimation)
            {
                ResetRadarToGaussianLidarHideOverlayState();
                return;
            }

            var now = Time.realtimeSinceStartup;
            var dt = 0.0f;
            if (m_radarToGaussianLidarHideOverlayLastAdvanceRealtime >= 0.0f)
                dt = Mathf.Max(0.0f, now - m_radarToGaussianLidarHideOverlayLastAdvanceRealtime);
            m_radarToGaussianLidarHideOverlayLastAdvanceRealtime = now;
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0.0f)
                dt = 0.0f;

            m_radarToGaussianLidarHideOverlayProgress01 = CalcVisibilityAdvancedProgress(
                dt,
                hiding: true,
                m_radarToGaussianLidarHideOverlayProgress01);

            if (m_radarToGaussianLidarHideOverlayProgress01 >= 1.0f)
            {
                m_radarToGaussianLidarHideOverlayProgress01 = 1.0f;
                m_radarToGaussianLidarHideOverlayActive = false;
            }
        }

        bool WillVisibilityHideReachDualTrackTriggerThisTick()
        {
            if (!EnableVisibilityAnimation)
                return false;

            if (m_visibilityState == VisibilityAnimState.Hidden)
                return true;

            if (m_visibilityState != VisibilityAnimState.Hiding)
                return false;

            var triggerProgress01 = GetDualTrackSwitchTriggerProgress01();
            if (m_visibilityProgress01 >= triggerProgress01)
                return true;

            var dt = 0.0f;
            if (m_visibilityLastAdvanceRealtime >= 0.0f)
                dt = Mathf.Max(0.0f, Time.realtimeSinceStartup - m_visibilityLastAdvanceRealtime);
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0.0f)
                dt = 0.0f;

            if (dt <= 0.0f)
                return false;

            return CalcVisibilityAdvancedProgress(dt, hiding: true, m_visibilityProgress01) >= triggerProgress01;
        }

        void FinalizeRadarToGaussianLidarDisableIfNeeded()
        {
            if (!m_pendingRadarToGaussianDisableLidar)
                return;

            if (m_radarToGaussianLidarHideOverlayActive)
                return;

            // ----------------------------------------------------------------
            // 关键语义:
            // - Gaussian show 启动后,LiDAR 还要继续把专用 hide overlay 跑到终点.
            // - 只有这条 hide 轨真的结束,才允许关闭 LiDAR runtime.
            // ----------------------------------------------------------------
            SetRadarScanEnabledWithoutCancel(enableRadarScan: false, animated: false, durationSeconds: 0.0f);
        }

        void CancelPendingRadarToGaussianShowSwitch()
        {
            m_pendingRadarToGaussianShowSwitch = false;
            m_pendingRadarToGaussianDisableLidar = false;
            ResetRadarToGaussianLidarHideOverlayState();
        }

        void CancelPendingGaussianToRadarShowSwitch()
        {
            m_pendingGaussianToRadarShowSwitch = false;
            m_pendingGaussianToRadarFinalizeRadarMode = false;
            ResetGaussianToRadarLidarShowOverlayState();
        }

        void CancelPendingDualTrackSwitches()
        {
            CancelPendingRadarToGaussianShowSwitch();
            CancelPendingGaussianToRadarShowSwitch();
        }

        void ArmRadarToGaussianShowSwitch()
        {
            m_pendingRadarToGaussianShowSwitch = true;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                RequestEditorRepaintForVisibilityAnimation(force: true, reason: "RadarToGaussian.Arm");
                RegisterVisibilityEditorTickerIfAnimating();
            }
#endif
        }

        void BeginGaussianShowFromHiddenForRadarSwitch()
        {
            // ----------------------------------------------------------------
            // Radar -> Gaussian 专用 show 起点:
            // - 按钮点击起点不能直接把共享显隐状态压成 Hidden,
            //   否则 LiDAR 也会被同一套 overlay 一起裁没.
            // - 因此这里把 Gaussian 的 show 启动延后到“半程触发”这一刻.
            // ----------------------------------------------------------------
            if (!EnableVisibilityAnimation)
            {
                ApplyVisibilityStableState(visible: true);
                return;
            }

            SetVisibilitySourceMask(VisibilitySourceMaskMode.FullHidden);
            m_visibilityState = VisibilityAnimState.Showing;
            m_visibilityProgress01 = 0.0f;
            m_visibilityLastAdvanceRealtime = -1.0f;

#if UNITY_EDITOR
            RequestEditorRepaintForVisibilityAnimation(force: true, reason: "RadarToGaussian.ShowStart");
            RegisterVisibilityEditorTickerIfAnimating();
#endif
        }

        bool BeginRadarHideForRadarToGaussianShowSwitch(float durationSeconds)
        {
            // ----------------------------------------------------------------
            // 关键语义:
            // - 这个按钮的“雷达粒子 hide”主视觉语言来自 `visibility hide`.
            // - 因此这里不再让 LiDAR 自己的 visibility fade 抢跑并提前把点云淡没.
            // - 我们只要求:
            //   1) Radar runtime 在 overlap 结束前保持在线.
            //   2) 真正的粒子退场由 burn/noise hide 轨负责.
            // ----------------------------------------------------------------
            _ = durationSeconds;
            m_lidarKeepAliveDuringFadeOut = false;
            return HasRadarVisibilityToExit();
        }

        void BeginVisibilityHideForRadarToGaussianShowSwitch()
        {
            // ----------------------------------------------------------------
            // 前半段视觉语言:
            // - 序列后端也要与静态后端一致,复用 `visibility hide` 按钮的 hide 过程.
            // - 这样 LiDAR overlay 才会真正进入 hide 的 noise / glow 处理.
            // ----------------------------------------------------------------
            if (!EnableVisibilityAnimation)
                return;

            PlayHideWithoutCancel();
        }

        void TriggerRadarToGaussianShowSwitchNow()
        {
            if (!m_pendingRadarToGaussianShowSwitch)
                return;

            m_pendingRadarToGaussianShowSwitch = false;
            CaptureRadarToGaussianLidarHideOverlayFromCurrentHideState();
            m_pendingRadarToGaussianDisableLidar = true;
            // 不能走 `SetRenderStyle(...)`,因为它会先 cancel,把刚抓到的专用 hide overlay 清掉.
            SetRenderStyleWithoutCancel(GsplatRenderStyle.Gaussian, animated: false, durationSeconds: 0.0f);
            BeginGaussianShowFromHiddenForRadarSwitch();
        }

        void AdvancePendingRadarToGaussianShowSwitchIfNeeded()
        {
            AdvanceRadarToGaussianLidarHideOverlayIfNeeded();
            FinalizeRadarToGaussianLidarDisableIfNeeded();

            if (!m_pendingRadarToGaussianShowSwitch)
                return;

            // ----------------------------------------------------------------
            // 关键修正:
            // - show 的触发点现在绑定到“visibility hide 轨”自身.
            // - 当前阈值会比 0.5 略早一点,让衔接更紧密,但仍保留明显的先 hide 再 overlap 体感.
            // ----------------------------------------------------------------
            if (WillVisibilityHideReachDualTrackTriggerThisTick())
            {
                TriggerRadarToGaussianShowSwitchNow();
                return;
            }

            if (!EnableVisibilityAnimation && !HasRadarVisibilityToExit())
                TriggerRadarToGaussianShowSwitchNow();
        }

        void BeginGaussianToRadarLidarShowOverlay()
        {
            if (!EnableVisibilityAnimation)
            {
                ResetGaussianToRadarLidarShowOverlayState();
                return;
            }

            m_gaussianToRadarLidarShowOverlayActive = true;
            m_gaussianToRadarLidarShowOverlayProgress01 = 0.0f;
            m_gaussianToRadarLidarShowOverlayLastAdvanceRealtime = -1.0f;
            m_gaussianToRadarLidarShowOverlaySourceMaskMode = VisibilitySourceMaskMode.FullHidden;
            m_gaussianToRadarLidarShowOverlaySourceMaskProgress01 = 0.0f;
        }

        void AdvanceGaussianToRadarLidarShowOverlayIfNeeded()
        {
            if (!m_gaussianToRadarLidarShowOverlayActive)
                return;

            if (!EnableVisibilityAnimation)
            {
                ResetGaussianToRadarLidarShowOverlayState();
                return;
            }

            var now = Time.realtimeSinceStartup;
            var dt = 0.0f;
            if (m_gaussianToRadarLidarShowOverlayLastAdvanceRealtime >= 0.0f)
                dt = Mathf.Max(0.0f, now - m_gaussianToRadarLidarShowOverlayLastAdvanceRealtime);
            m_gaussianToRadarLidarShowOverlayLastAdvanceRealtime = now;
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0.0f)
                dt = 0.0f;

            m_gaussianToRadarLidarShowOverlayProgress01 = CalcVisibilityAdvancedProgress(
                dt,
                hiding: false,
                m_gaussianToRadarLidarShowOverlayProgress01);

            if (m_gaussianToRadarLidarShowOverlayProgress01 >= 1.0f)
            {
                m_gaussianToRadarLidarShowOverlayProgress01 = 1.0f;
                m_gaussianToRadarLidarShowOverlayActive = false;
            }
        }

        void ArmGaussianToRadarShowSwitch()
        {
            m_pendingGaussianToRadarShowSwitch = true;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                RequestEditorRepaintForVisibilityAnimation(force: true, reason: "GaussianToRadar.Arm");
                RegisterVisibilityEditorTickerIfAnimating();
            }
#endif
        }

        void TriggerGaussianToRadarShowSwitchNow()
        {
            if (!m_pendingGaussianToRadarShowSwitch)
                return;

            m_pendingGaussianToRadarShowSwitch = false;
            m_pendingGaussianToRadarFinalizeRadarMode = true;
            HideSplatsWhenLidarEnabled = true;
            SetRadarScanEnabledWithoutCancel(enableRadarScan: true, animated: false, durationSeconds: 0.0f);
            BeginGaussianToRadarLidarShowOverlay();
        }

        void FinalizeGaussianToRadarRadarModeIfNeeded()
        {
            if (!m_pendingGaussianToRadarFinalizeRadarMode)
                return;

            if (m_visibilityState != VisibilityAnimState.Hidden)
                return;

            m_pendingGaussianToRadarFinalizeRadarMode = false;
            ResetGaussianToRadarLidarShowOverlayState();
            HideSplatsWhenLidarEnabled = true;
            SetRadarScanEnabledWithoutCancel(enableRadarScan: true, animated: false, durationSeconds: 0.0f);
            SetRenderStyleWithoutCancel(GsplatRenderStyle.ParticleDots, animated: false, durationSeconds: 0.0f);
            ApplyVisibilityStableState(visible: true);
        }

        void AdvancePendingGaussianToRadarShowSwitchIfNeeded()
        {
            AdvanceGaussianToRadarLidarShowOverlayIfNeeded();
            FinalizeGaussianToRadarRadarModeIfNeeded();

            if (!m_pendingGaussianToRadarShowSwitch)
                return;

            if (WillVisibilityHideReachDualTrackTriggerThisTick())
            {
                TriggerGaussianToRadarShowSwitchNow();
                return;
            }

            if (!EnableVisibilityAnimation && !HasGaussianMainVisualToExit())
                TriggerGaussianToRadarShowSwitchNow();
        }

        public void PlayRadarScanToGaussianShowHideSwitch(float durationSeconds = -1.0f)
        {
            CancelPendingDualTrackSwitches();

            if (!HasRadarVisibilityToExit())
            {
                SetRenderStyleWithoutCancel(GsplatRenderStyle.Gaussian, animated: false, durationSeconds: 0.0f);
                SetVisibleWithoutCancel(visible: true, animated: true);
                return;
            }

            if (!EnableVisibilityAnimation)
            {
                SetRadarScanEnabledWithoutCancel(enableRadarScan: false, animated: false, durationSeconds: 0.0f);
                SetRenderStyleWithoutCancel(GsplatRenderStyle.Gaussian, animated: false, durationSeconds: 0.0f);
                SetVisibleWithoutCancel(visible: true, animated: false);
                return;
            }

            var radarHideStarted = BeginRadarHideForRadarToGaussianShowSwitch(durationSeconds);

            if (radarHideStarted)
                BeginVisibilityHideForRadarToGaussianShowSwitch();

            if (!radarHideStarted)
            {
                TriggerRadarToGaussianShowSwitchNow();
                return;
            }

            ArmRadarToGaussianShowSwitch();
        }

        public void PlayGaussianToRadarScanShowHideSwitch(float durationSeconds = -1.0f)
        {
            CancelPendingDualTrackSwitches();

            _ = durationSeconds;

            if (!HasGaussianMainVisualToExit())
            {
                HideSplatsWhenLidarEnabled = true;
                SetRadarScanEnabledWithoutCancel(enableRadarScan: true, animated: false, durationSeconds: 0.0f);
                SetRenderStyleWithoutCancel(GsplatRenderStyle.ParticleDots, animated: false, durationSeconds: 0.0f);
                SetVisibleWithoutCancel(visible: true, animated: false);
                return;
            }

            if (!EnableVisibilityAnimation)
            {
                HideSplatsWhenLidarEnabled = true;
                SetRadarScanEnabledWithoutCancel(enableRadarScan: true, animated: false, durationSeconds: 0.0f);
                SetRenderStyleWithoutCancel(GsplatRenderStyle.ParticleDots, animated: false, durationSeconds: 0.0f);
                SetVisibleWithoutCancel(visible: true, animated: false);
                return;
            }

            PlayHideWithoutCancel();

            if (WillVisibilityHideReachDualTrackTriggerThisTick())
            {
                TriggerGaussianToRadarShowSwitchNow();
                return;
            }

            ArmGaussianToRadarShowSwitch();
        }

        void SetRenderStyleAndRadarScanWithoutCancel(GsplatRenderStyle style, bool enableRadarScan, bool animated = true,
            float durationSeconds = -1.0f)
        {
            // ----------------------------------------------------------------
            // 组合切换 API:
            // - 目标: 让序列后端与静态后端拥有一致的“风格 + 雷达模式”切换语义.
            // - RadarScan 模式约定:
            //   1) 强制切到 ParticleDots.
            //   2) 自动开启 HideSplatsWhenLidarEnabled,保证默认进入纯雷达观感.
            // ----------------------------------------------------------------
            if (enableRadarScan)
            {
                HideSplatsWhenLidarEnabled = true;
                style = GsplatRenderStyle.ParticleDots;
            }

            SetRadarScanEnabledWithoutCancel(enableRadarScan, animated, durationSeconds);
            SetRenderStyleWithoutCancel(style, animated, durationSeconds);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
#endif
        }

        public void SetRenderStyleAndRadarScan(GsplatRenderStyle style, bool enableRadarScan, bool animated = true,
            float durationSeconds = -1.0f)
        {
            CancelPendingDualTrackSwitches();
            SetRenderStyleAndRadarScanWithoutCancel(style, enableRadarScan, animated, durationSeconds);
        }

        void SetRadarScanEnabledWithoutCancel(bool enableRadarScan, bool animated = true, float durationSeconds = -1.0f)
        {
            var d = ResolveRadarScanVisibilityDurationSeconds(enableRadarScan, durationSeconds);

            EnableLidarScan = enableRadarScan;

            if (enableRadarScan)
            {
                m_lidarKeepAliveDuringFadeOut = false;
                BeginLidarVisibilityTransition(target01: 1.0f, animated, d);
            }
            else
            {
                if (animated && m_lidarVisibility01 > 0.0f)
                {
                    m_lidarKeepAliveDuringFadeOut = true;
                    BeginLidarVisibilityTransition(target01: 0.0f, animated: true, durationSeconds: d);
                }
                else
                {
                    m_lidarKeepAliveDuringFadeOut = false;
                    BeginLidarVisibilityTransition(target01: 0.0f, animated: false, durationSeconds: 0.0f);
                }
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                RequestEditorRepaintForVisibilityAnimation(force: true, reason: "LiDAR.ModeSwitch");
                RegisterVisibilityEditorTickerIfAnimating();
            }
#endif
        }

        public void SetRadarScanEnabled(bool enableRadarScan, bool animated = true, float durationSeconds = -1.0f)
        {
            CancelPendingDualTrackSwitches();
            SetRadarScanEnabledWithoutCancel(enableRadarScan, animated, durationSeconds);
        }

        public void SetLidarColorMode(GsplatLidarColorMode colorMode, bool animated = true, float durationSeconds = -1.0f)
        {
            LidarColorMode = colorMode;

            var target = colorMode == GsplatLidarColorMode.SplatColorSH0 ? 1.0f : 0.0f;
            var d = durationSeconds;
            if (d < 0.0f)
                d = k_lidarColorSwitchDurationSeconds;
            if (float.IsNaN(d) || float.IsInfinity(d) || d < 0.0f)
                d = 0.0f;

            BeginLidarColorTransition(target, animated, d);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                RequestEditorRepaintForVisibilityAnimation(force: true, reason: "LiDAR.ColorSwitch");
                RegisterVisibilityEditorTickerIfAnimating();
            }
#endif
        }

        void ApplyRenderStyleTransition(GsplatRenderStyle style, bool animated, float durationSeconds)
        {
            RenderStyle = style;

            var target = style == GsplatRenderStyle.ParticleDots ? 1.0f : 0.0f;

            var d = durationSeconds;
            if (d < 0.0f)
                d = RenderStyleSwitchDurationSeconds;
            if (float.IsNaN(d) || float.IsInfinity(d) || d < 0.0f)
                d = 0.0f;

            if (!animated || d <= 0.0f)
            {
                m_renderStyleBlend01 = target;
                m_renderStyleAlphaBlend01 = target;
                m_renderStyleAnimating = false;
                m_renderStyleAnimProgress01 = 1.0f;
                m_renderStyleAnimStartBlend01 = target;
                m_renderStyleAnimStartAlphaBlend01 = target;
                m_renderStyleAnimTargetBlend01 = target;
                m_renderStyleLastAdvanceRealtime = -1.0f;

                PushRenderStyleUniformsForThisFrame();

#if UNITY_EDITOR
                RequestEditorRepaintForVisibilityAnimation(force: true, reason: "RenderStyle.HardSet");
#endif
                return;
            }

            m_renderStyleAnimating = true;
            m_renderStyleAnimProgress01 = 0.0f;
            m_renderStyleAnimStartBlend01 = m_renderStyleBlend01;
            m_renderStyleAnimStartAlphaBlend01 = m_renderStyleAlphaBlend01;
            m_renderStyleAnimTargetBlend01 = target;
            m_renderStyleAnimDurationSeconds = d;
            m_renderStyleLastAdvanceRealtime = -1.0f;

#if UNITY_EDITOR
            RequestEditorRepaintForVisibilityAnimation(force: true, reason: "RenderStyle.AnimStart");
            RegisterVisibilityEditorTickerIfAnimating();
#endif
        }

        void SetRenderStyleWithoutCancel(GsplatRenderStyle style, bool animated = true, float durationSeconds = -1.0f)
        {
            ApplyRenderStyleTransition(style, animated, durationSeconds);
        }

        public void SetRenderStyle(GsplatRenderStyle style, bool animated = true, float durationSeconds = -1.0f)
        {
            CancelPendingDualTrackSwitches();
            SetRenderStyleWithoutCancel(style, animated, durationSeconds);
        }

        public void SetParticleDotRadiusPixels(float radiusPixels)
        {
            if (float.IsNaN(radiusPixels) || float.IsInfinity(radiusPixels) || radiusPixels < 0.0f)
                radiusPixels = 0.0f;

            ParticleDotRadiusPixels = radiusPixels;
            PushRenderStyleUniformsForThisFrame();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
#endif
        }

        // --------------------------------------------------------------------
        // Public API: show/hide 显隐控制
        // --------------------------------------------------------------------
        void SetVisibilitySourceMask(VisibilitySourceMaskMode mode, float progress01 = 1.0f)
        {
            m_visibilitySourceMaskMode = mode;
            if (mode == VisibilitySourceMaskMode.ShowSnapshot || mode == VisibilitySourceMaskMode.HideSnapshot)
            {
                if (float.IsNaN(progress01) || float.IsInfinity(progress01))
                    progress01 = 0.0f;
                m_visibilitySourceMaskProgress01 = Mathf.Clamp01(progress01);
            }
            else
            {
                m_visibilitySourceMaskProgress01 = mode == VisibilitySourceMaskMode.FullHidden ? 0.0f : 1.0f;
            }
        }

        void CaptureVisibilitySourceMaskForShowTransition()
        {
            if (m_visibilityState == VisibilityAnimState.Hiding &&
                (m_visibilitySourceMaskMode == VisibilitySourceMaskMode.ShowSnapshot ||
                 m_visibilitySourceMaskMode == VisibilitySourceMaskMode.FullHidden))
            {
                return;
            }

            switch (m_visibilityState)
            {
                case VisibilityAnimState.Hidden:
                    SetVisibilitySourceMask(VisibilitySourceMaskMode.FullHidden);
                    break;
                case VisibilityAnimState.Hiding:
                    SetVisibilitySourceMask(VisibilitySourceMaskMode.HideSnapshot, m_visibilityProgress01);
                    break;
                case VisibilityAnimState.Showing:
                    SetVisibilitySourceMask(VisibilitySourceMaskMode.ShowSnapshot, m_visibilityProgress01);
                    break;
                default:
                    SetVisibilitySourceMask(VisibilitySourceMaskMode.FullVisible);
                    break;
            }
        }

        void CaptureVisibilitySourceMaskForHideTransition()
        {
            if (m_visibilityState == VisibilityAnimState.Showing &&
                (m_visibilitySourceMaskMode == VisibilitySourceMaskMode.HideSnapshot ||
                 m_visibilitySourceMaskMode == VisibilitySourceMaskMode.FullVisible))
            {
                return;
            }

            switch (m_visibilityState)
            {
                case VisibilityAnimState.Visible:
                    SetVisibilitySourceMask(VisibilitySourceMaskMode.FullVisible);
                    break;
                case VisibilityAnimState.Showing:
                    SetVisibilitySourceMask(VisibilitySourceMaskMode.ShowSnapshot, m_visibilityProgress01);
                    break;
                case VisibilityAnimState.Hiding:
                    SetVisibilitySourceMask(VisibilitySourceMaskMode.HideSnapshot, m_visibilityProgress01);
                    break;
                default:
                    SetVisibilitySourceMask(VisibilitySourceMaskMode.FullHidden);
                    break;
            }
        }

        void SetVisibleWithoutCancel(bool visible, bool animated = true)
        {
            if (!animated || !EnableVisibilityAnimation)
            {
                ApplyVisibilityStableState(visible);
                return;
            }

            if (visible)
                PlayShowWithoutCancel();
            else
                PlayHideWithoutCancel();
        }

        public void SetVisible(bool visible, bool animated = true)
        {
            CancelPendingDualTrackSwitches();
            SetVisibleWithoutCancel(visible, animated);
        }

        void PlayShowWithoutCancel()
        {
            if (!EnableVisibilityAnimation)
            {
                ApplyVisibilityStableState(visible: true);
                return;
            }

            if (m_visibilityState is VisibilityAnimState.Visible or VisibilityAnimState.Showing)
                return;

            // show 叠加: 先抓 source,再启动新 show.
            CaptureVisibilitySourceMaskForShowTransition();
            m_visibilityState = VisibilityAnimState.Showing;
            m_visibilityProgress01 = 0.0f;
            m_visibilityLastAdvanceRealtime = -1.0f;

#if UNITY_EDITOR
            RequestEditorRepaintForVisibilityAnimation(force: true, reason: "PlayShow");
            RegisterVisibilityEditorTickerIfAnimating();
#endif
        }

        public void PlayShow()
        {
            CancelPendingDualTrackSwitches();
            PlayShowWithoutCancel();
        }

        void PlayHideWithoutCancel()
        {
            if (!EnableVisibilityAnimation)
            {
                ApplyVisibilityStableState(visible: false);
                return;
            }

            if (m_visibilityState is VisibilityAnimState.Hidden or VisibilityAnimState.Hiding)
                return;

            // hide 叠加: 先抓 source,再启动新 hide.
            CaptureVisibilitySourceMaskForHideTransition();
            m_visibilityState = VisibilityAnimState.Hiding;
            m_visibilityProgress01 = 0.0f;
            m_visibilityLastAdvanceRealtime = -1.0f;

#if UNITY_EDITOR
            RequestEditorRepaintForVisibilityAnimation(force: true, reason: "PlayHide");
            RegisterVisibilityEditorTickerIfAnimating();
#endif
        }

        public void PlayHide()
        {
            CancelPendingDualTrackSwitches();
            PlayHideWithoutCancel();
        }

        void InitVisibilityOnEnable()
        {
            m_visibilityState = VisibilityAnimState.Visible;
            m_visibilityProgress01 = 1.0f;
            m_visibilityLastAdvanceRealtime = -1.0f;
            SetVisibilitySourceMask(VisibilitySourceMaskMode.FullVisible);

            if (!EnableVisibilityAnimation || !PlayShowOnEnable)
                return;

            SetVisibilitySourceMask(VisibilitySourceMaskMode.FullHidden);
            m_visibilityState = VisibilityAnimState.Showing;
            m_visibilityProgress01 = 0.0f;

#if UNITY_EDITOR
            RequestEditorRepaintForVisibilityAnimation(force: true, reason: "OnEnable");
            RegisterVisibilityEditorTickerIfAnimating();
#endif
        }

        void InitRenderStyleOnEnable()
        {
            m_renderStyleAnimating = false;
            m_renderStyleAnimProgress01 = 1.0f;
            m_renderStyleAnimStartBlend01 = 0.0f;
            m_renderStyleAnimTargetBlend01 = RenderStyle == GsplatRenderStyle.ParticleDots ? 1.0f : 0.0f;
            m_renderStyleBlend01 = m_renderStyleAnimTargetBlend01;
            m_renderStyleAnimStartAlphaBlend01 = m_renderStyleAnimTargetBlend01;
            m_renderStyleAlphaBlend01 = m_renderStyleAnimTargetBlend01;

            m_renderStyleAnimDurationSeconds = RenderStyleSwitchDurationSeconds;
            if (float.IsNaN(m_renderStyleAnimDurationSeconds) || float.IsInfinity(m_renderStyleAnimDurationSeconds) ||
                m_renderStyleAnimDurationSeconds < 0.0f)
            {
                m_renderStyleAnimDurationSeconds = 1.5f;
            }

            m_renderStyleLastAdvanceRealtime = -1.0f;
        }

        void InitLidarAnimationOnEnable()
        {
            var colorTarget = LidarColorMode == GsplatLidarColorMode.SplatColorSH0 ? 1.0f : 0.0f;
            m_lidarColorBlend01 = colorTarget;
            m_lidarColorAnimating = false;
            m_lidarColorAnimProgress01 = 1.0f;
            m_lidarColorAnimStartBlend01 = colorTarget;
            m_lidarColorAnimTargetBlend01 = colorTarget;
            m_lidarColorAnimDurationSeconds = k_lidarColorSwitchDurationSeconds;
            m_lidarColorLastAdvanceRealtime = -1.0f;

            var visibilityTarget = EnableLidarScan ? 1.0f : 0.0f;
            m_lidarVisibility01 = visibilityTarget;
            m_lidarVisibilityAnimating = false;
            m_lidarVisibilityAnimProgress01 = 1.0f;
            m_lidarVisibilityAnimStart01 = visibilityTarget;
            m_lidarVisibilityAnimTarget01 = visibilityTarget;
            m_lidarVisibilityAnimDurationSeconds = RenderStyleSwitchDurationSeconds;
            m_lidarVisibilityLastAdvanceRealtime = -1.0f;
            m_lidarKeepAliveDuringFadeOut = false;
            CancelPendingDualTrackSwitches();
        }

        void SyncLidarColorBlendTargetFromSerializedMode(bool animated)
        {
            var target = LidarColorMode == GsplatLidarColorMode.SplatColorSH0 ? 1.0f : 0.0f;
            // 重要:
            // - 该函数会在 Update/OnValidate 中被调用,用于让 “直接改序列化字段” 也能驱动颜色过渡.
            // - 如果在已经 animating 且 target 不变时仍反复 Begin,会导致 progress 每帧被重置为 0,
            //   表现为 Inspector 的颜色切换按钮按下后动画不走(看起来无效).
            if (Mathf.Abs(m_lidarColorAnimTargetBlend01 - target) < 1e-6f)
                return;

            BeginLidarColorTransition(target, animated, k_lidarColorSwitchDurationSeconds);
        }

        bool LidarNeedsSplatIdThisFrame()
        {
            if (LidarColorMode == GsplatLidarColorMode.SplatColorSH0)
                return true;

            if (m_lidarColorAnimating)
                return true;

            if (m_lidarColorBlend01 > 1.0e-4f)
                return true;

            return m_lidarColorAnimTargetBlend01 > 1.0e-4f;
        }

        void BeginLidarColorTransition(float targetBlend01, bool animated, float durationSeconds)
        {
            targetBlend01 = Mathf.Clamp01(targetBlend01);
            if (float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds) || durationSeconds < 0.0f)
                durationSeconds = 0.0f;

            if (!animated || durationSeconds <= 0.0f)
            {
                m_lidarColorBlend01 = targetBlend01;
                m_lidarColorAnimating = false;
                m_lidarColorAnimProgress01 = 1.0f;
                m_lidarColorAnimStartBlend01 = targetBlend01;
                m_lidarColorAnimTargetBlend01 = targetBlend01;
                m_lidarColorAnimDurationSeconds = k_lidarColorSwitchDurationSeconds;
                m_lidarColorLastAdvanceRealtime = -1.0f;
                return;
            }

            m_lidarColorAnimating = true;
            m_lidarColorAnimProgress01 = 0.0f;
            m_lidarColorAnimStartBlend01 = m_lidarColorBlend01;
            m_lidarColorAnimTargetBlend01 = targetBlend01;
            m_lidarColorAnimDurationSeconds = durationSeconds;
            m_lidarColorLastAdvanceRealtime = -1.0f;
        }

        void BeginLidarVisibilityTransition(float target01, bool animated, float durationSeconds)
        {
            target01 = Mathf.Clamp01(target01);
            if (float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds) || durationSeconds < 0.0f)
                durationSeconds = 0.0f;

            if (!animated || durationSeconds <= 0.0f)
            {
                m_lidarVisibility01 = target01;
                m_lidarVisibilityAnimating = false;
                m_lidarVisibilityAnimProgress01 = 1.0f;
                m_lidarVisibilityAnimStart01 = target01;
                m_lidarVisibilityAnimTarget01 = target01;
                m_lidarVisibilityAnimDurationSeconds = RenderStyleSwitchDurationSeconds;
                m_lidarVisibilityLastAdvanceRealtime = -1.0f;
                if (target01 <= 0.0f && !EnableLidarScan)
                    m_lidarKeepAliveDuringFadeOut = false;
                return;
            }

            m_lidarVisibilityAnimating = true;
            m_lidarVisibilityAnimProgress01 = 0.0f;
            m_lidarVisibilityAnimStart01 = m_lidarVisibility01;
            m_lidarVisibilityAnimTarget01 = target01;
            m_lidarVisibilityAnimDurationSeconds = durationSeconds;
            m_lidarVisibilityLastAdvanceRealtime = -1.0f;
        }

        void AdvanceLidarAnimationStateIfNeeded()
        {
            if (!m_lidarColorAnimating && !m_lidarVisibilityAnimating)
                return;

            var now = Time.realtimeSinceStartup;

            if (m_lidarColorAnimating)
            {
                var dt = 0.0f;
                if (m_lidarColorLastAdvanceRealtime >= 0.0f)
                    dt = Mathf.Max(0.0f, now - m_lidarColorLastAdvanceRealtime);
                m_lidarColorLastAdvanceRealtime = now;
                if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0.0f)
                    dt = 0.0f;

                var duration = m_lidarColorAnimDurationSeconds;
                if (duration <= 0.0f || float.IsNaN(duration) || float.IsInfinity(duration))
                {
                    m_lidarColorAnimProgress01 = 1.0f;
                }
                else
                {
                    m_lidarColorAnimProgress01 = Mathf.Clamp01(m_lidarColorAnimProgress01 + dt / duration);
                }

                var t = GsplatUtils.EaseInOutQuart(m_lidarColorAnimProgress01);
                m_lidarColorBlend01 = Mathf.Lerp(m_lidarColorAnimStartBlend01, m_lidarColorAnimTargetBlend01, t);
                if (m_lidarColorAnimProgress01 >= 1.0f)
                {
                    m_lidarColorBlend01 = m_lidarColorAnimTargetBlend01;
                    m_lidarColorAnimating = false;
                }
            }

            if (m_lidarVisibilityAnimating)
            {
                var dt = 0.0f;
                if (m_lidarVisibilityLastAdvanceRealtime >= 0.0f)
                    dt = Mathf.Max(0.0f, now - m_lidarVisibilityLastAdvanceRealtime);
                m_lidarVisibilityLastAdvanceRealtime = now;
                if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0.0f)
                    dt = 0.0f;

                var duration = m_lidarVisibilityAnimDurationSeconds;
                if (duration <= 0.0f || float.IsNaN(duration) || float.IsInfinity(duration))
                {
                    m_lidarVisibilityAnimProgress01 = 1.0f;
                }
                else
                {
                    m_lidarVisibilityAnimProgress01 = Mathf.Clamp01(m_lidarVisibilityAnimProgress01 + dt / duration);
                }

                var t = GsplatUtils.EaseInOutQuart(m_lidarVisibilityAnimProgress01);
                m_lidarVisibility01 = Mathf.Lerp(m_lidarVisibilityAnimStart01, m_lidarVisibilityAnimTarget01, t);
                if (m_lidarVisibilityAnimProgress01 >= 1.0f)
                {
                    m_lidarVisibility01 = m_lidarVisibilityAnimTarget01;
                    m_lidarVisibilityAnimating = false;
                    if (m_lidarVisibility01 <= 0.0f && !EnableLidarScan)
                        m_lidarKeepAliveDuringFadeOut = false;
                }
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (m_lidarColorAnimating || m_lidarVisibilityAnimating)
                    RequestEditorRepaintForVisibilityAnimation(force: false, reason: "LiDAR.Anim.Advance");
                else
                    RequestEditorRepaintForVisibilityAnimation(force: true, reason: "LiDAR.Anim.Finish");
            }
#endif
        }

        void AdvanceVisibilityStateIfNeeded()
        {
            if (!EnableVisibilityAnimation)
                return;

            var prevState = m_visibilityState;

            var now = Time.realtimeSinceStartup;
            var dt = 0.0f;
            if (m_visibilityLastAdvanceRealtime >= 0.0f)
                dt = Mathf.Max(0.0f, now - m_visibilityLastAdvanceRealtime);
            m_visibilityLastAdvanceRealtime = now;

            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0.0f)
                dt = 0.0f;

            if (m_visibilityState == VisibilityAnimState.Showing)
            {
                m_visibilityProgress01 = CalcVisibilityAdvancedProgress(dt, hiding: false, m_visibilityProgress01);
                if (m_visibilityProgress01 >= 1.0f)
                    m_visibilityState = VisibilityAnimState.Visible;
            }
            else if (m_visibilityState == VisibilityAnimState.Hiding)
            {
                m_visibilityProgress01 = CalcVisibilityAdvancedProgress(dt, hiding: true, m_visibilityProgress01);
                if (m_visibilityProgress01 >= 1.0f)
                    m_visibilityState = VisibilityAnimState.Hidden;
            }

            if (m_visibilityState == VisibilityAnimState.Visible)
                SetVisibilitySourceMask(VisibilitySourceMaskMode.FullVisible);
            else if (m_visibilityState == VisibilityAnimState.Hidden)
                SetVisibilitySourceMask(VisibilitySourceMaskMode.FullHidden);

#if UNITY_EDITOR
            if (GsplatEditorDiagnostics.Enabled && prevState != m_visibilityState)
            {
                GsplatEditorDiagnostics.MarkVisibilityState(this, "state.change",
                    m_visibilityState.ToString(), m_visibilityProgress01);
            }

            if (m_visibilityState == VisibilityAnimState.Showing || m_visibilityState == VisibilityAnimState.Hiding)
            {
                RequestEditorRepaintForVisibilityAnimation(force: false, reason: "Advance");
            }
            else if ((prevState == VisibilityAnimState.Showing || prevState == VisibilityAnimState.Hiding) &&
                     (m_visibilityState == VisibilityAnimState.Visible || m_visibilityState == VisibilityAnimState.Hidden))
            {
                RequestEditorRepaintForVisibilityAnimation(force: true, reason: "Advance.Finish");
            }
#endif
        }

        void AdvanceRenderStyleStateIfNeeded()
        {
            if (!m_renderStyleAnimating)
                return;

            var now = Time.realtimeSinceStartup;
            var dt = 0.0f;
            if (m_renderStyleLastAdvanceRealtime >= 0.0f)
                dt = Mathf.Max(0.0f, now - m_renderStyleLastAdvanceRealtime);
            m_renderStyleLastAdvanceRealtime = now;

            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0.0f)
                dt = 0.0f;

            var d = m_renderStyleAnimDurationSeconds;
            if (float.IsNaN(d) || float.IsInfinity(d) || d <= 0.0f)
                d = 0.0f;

            if (d <= 0.0f)
            {
                m_renderStyleBlend01 = m_renderStyleAnimTargetBlend01;
                m_renderStyleAlphaBlend01 = m_renderStyleAnimTargetBlend01;
                m_renderStyleAnimating = false;
                m_renderStyleAnimProgress01 = 1.0f;
            }
            else
            {
                m_renderStyleAnimProgress01 = Mathf.Clamp01(m_renderStyleAnimProgress01 + dt / d);
                var morphEased = GsplatUtils.EaseInOutQuart(m_renderStyleAnimProgress01);
                var alphaEased = GsplatUtils.EaseInOutSine(m_renderStyleAnimProgress01);
                m_renderStyleBlend01 =
                    Mathf.Lerp(m_renderStyleAnimStartBlend01, m_renderStyleAnimTargetBlend01, morphEased);
                m_renderStyleAlphaBlend01 =
                    Mathf.Lerp(m_renderStyleAnimStartAlphaBlend01, m_renderStyleAnimTargetBlend01, alphaEased);

                if (m_renderStyleAnimProgress01 >= 1.0f)
                {
                    m_renderStyleBlend01 = m_renderStyleAnimTargetBlend01;
                    m_renderStyleAlphaBlend01 = m_renderStyleAnimTargetBlend01;
                    m_renderStyleAnimating = false;
                }
            }

#if UNITY_EDITOR
            if (m_renderStyleAnimating)
            {
                RequestEditorRepaintForVisibilityAnimation(force: false, reason: "RenderStyle.Advance");
            }
            else
            {
                RequestEditorRepaintForVisibilityAnimation(force: true, reason: "RenderStyle.Advance.Finish");
            }
#endif
        }

        void PushRenderStyleUniformsForThisFrame()
        {
            if (m_renderer == null)
                return;

            var dotRadius = ParticleDotRadiusPixels;
            if (float.IsNaN(dotRadius) || float.IsInfinity(dotRadius) || dotRadius < 0.0f)
                dotRadius = 0.0f;

            var blend = m_renderStyleBlend01;
            if (float.IsNaN(blend) || float.IsInfinity(blend))
                blend = 0.0f;
            blend = Mathf.Clamp01(blend);

            var alphaBlend = m_renderStyleAlphaBlend01;
            if (float.IsNaN(alphaBlend) || float.IsInfinity(alphaBlend))
                alphaBlend = blend;
            alphaBlend = Mathf.Clamp01(alphaBlend);

            m_renderer.SetRenderStyleUniforms(blend, alphaBlend, dotRadius);
        }

        void PushVisibilityUniformsForThisFrame(Bounds localBounds)
        {
            if (m_renderer == null)
                return;

            var mode = 0;
            var progress = 1.0f;
            var sourceMode = (int)m_visibilitySourceMaskMode;
            var sourceProgress = m_visibilitySourceMaskProgress01;
            if (EnableVisibilityAnimation)
            {
                if (m_visibilityState == VisibilityAnimState.Showing)
                {
                    mode = 1;
                    progress = m_visibilityProgress01;
                }
                else if (m_visibilityState == VisibilityAnimState.Hiding)
                {
                    mode = 2;
                    progress = m_visibilityProgress01;
                }
            }

            if (float.IsNaN(sourceProgress) || float.IsInfinity(sourceProgress))
                sourceProgress = 1.0f;
            sourceProgress = Mathf.Clamp01(sourceProgress);

            var centerModel = CalcVisibilityCenterModel(localBounds);
            var maxRadius = CalcVisibilityEffectiveMaxRadius(localBounds, centerModel);

            // show/hide 的 ring/trail 宽度允许分别调参.
            var ringWidthNorm = ShowRingWidthNormalized;
            var trailWidthNorm = ShowTrailWidthNormalized;
            if (mode == 2)
            {
                ringWidthNorm = HideRingWidthNormalized;
                trailWidthNorm = HideTrailWidthNormalized;
            }

            var ringWidth = GsplatVisibilityAnimationUtil.CalcRadialWidth(maxRadius, ringWidthNorm);
            var trailWidth = GsplatVisibilityAnimationUtil.CalcRadialWidth(maxRadius, trailWidthNorm);

            var glowIntensity = mode == 2 ? HideGlowIntensity : ShowGlowIntensity;
            var showGlowSparkleStrength = mode == 1 ? ShowGlowSparkleStrength : 0.0f;
            var t = Time.realtimeSinceStartup;

            m_renderer.SetVisibilityUniforms(
                mode: mode,
                noiseMode: (int)VisibilityNoiseMode,
                progress: progress,
                sourceMaskMode: sourceMode,
                sourceMaskProgress: sourceProgress,
                centerModel: centerModel,
                maxRadius: maxRadius,
                ringWidth: ringWidth,
                trailWidth: trailWidth,
                showMinScale: ShowSplatMinScale,
                showRingMinScale: ShowRingSplatMinScale,
                showTrailMinScale: ShowTrailSplatMinScale,
                hideMinScale: HideSplatMinScale,
                glowColor: GlowColor,
                glowIntensity: glowIntensity,
                showGlowStartBoost: ShowGlowStartBoost,
                showGlowSparkleStrength: showGlowSparkleStrength,
                hideGlowStartBoost: HideGlowStartBoost,
                noiseStrength: NoiseStrength,
                noiseScale: NoiseScale,
                noiseSpeed: NoiseSpeed,
                warpStrength: WarpStrength,
                timeSeconds: t);
        }

        void BuildLidarShowHideOverlayForThisFrame(Bounds localBounds,
            out float gate,
            out int mode,
            out float progress,
            out int sourceMaskMode,
            out float sourceMaskProgress,
            out Vector3 centerModel,
            out float maxRadius,
            out float ringWidth,
            out float trailWidth)
        {
            gate = 1.0f;
            mode = 0;
            progress = 1.0f;
            sourceMaskMode = (int)VisibilitySourceMaskMode.FullVisible;
            sourceMaskProgress = 1.0f;

            centerModel = CalcVisibilityCenterModel(localBounds);
            maxRadius = CalcVisibilityEffectiveMaxRadius(localBounds, centerModel);
            ringWidth = GsplatVisibilityAnimationUtil.CalcRadialWidth(maxRadius, ShowRingWidthNormalized);
            trailWidth = GsplatVisibilityAnimationUtil.CalcRadialWidth(maxRadius, ShowTrailWidthNormalized);

            if (m_pendingGaussianToRadarFinalizeRadarMode)
            {
                if (m_gaussianToRadarLidarShowOverlayActive)
                {
                    gate = 1.0f;
                    mode = 1;
                    progress = Mathf.Clamp01(m_gaussianToRadarLidarShowOverlayProgress01);
                    sourceMaskMode = (int)m_gaussianToRadarLidarShowOverlaySourceMaskMode;
                    sourceMaskProgress = Mathf.Clamp01(m_gaussianToRadarLidarShowOverlaySourceMaskProgress01);
                    ringWidth = GsplatVisibilityAnimationUtil.CalcRadialWidth(maxRadius, ShowRingWidthNormalized);
                    trailWidth = GsplatVisibilityAnimationUtil.CalcRadialWidth(maxRadius, ShowTrailWidthNormalized);
                    return;
                }

                // ----------------------------------------------------------------
                // reverse finalize 尾段:
                // - `show-hide-switch-雷达` 的 LiDAR 已经完成前半段 reveal.
                // - 若专用 show overlay 先结束,但 finalize 还没落到稳定 RadarScan,
                //   这里不能回退到共享 Gaussian hide/hidden,否则会出现后半段闪断.
                // ----------------------------------------------------------------
                gate = 1.0f;
                mode = 0;
                progress = 1.0f;
                sourceMaskMode = (int)VisibilitySourceMaskMode.FullVisible;
                sourceMaskProgress = 1.0f;
                ringWidth = GsplatVisibilityAnimationUtil.CalcRadialWidth(maxRadius, ShowRingWidthNormalized);
                trailWidth = GsplatVisibilityAnimationUtil.CalcRadialWidth(maxRadius, ShowTrailWidthNormalized);
                return;
            }

            if (m_radarToGaussianLidarHideOverlayActive)
            {
                gate = 1.0f;
                mode = 2;
                progress = Mathf.Clamp01(m_radarToGaussianLidarHideOverlayProgress01);
                sourceMaskMode = (int)m_radarToGaussianLidarHideOverlaySourceMaskMode;
                sourceMaskProgress = Mathf.Clamp01(m_radarToGaussianLidarHideOverlaySourceMaskProgress01);
                ringWidth = GsplatVisibilityAnimationUtil.CalcRadialWidth(maxRadius, HideRingWidthNormalized);
                trailWidth = GsplatVisibilityAnimationUtil.CalcRadialWidth(maxRadius, HideTrailWidthNormalized);
                return;
            }

            if (!EnableVisibilityAnimation)
            {
                if (m_visibilityState == VisibilityAnimState.Hidden)
                {
                    gate = 0.0f;
                    sourceMaskMode = (int)VisibilitySourceMaskMode.FullHidden;
                    sourceMaskProgress = 0.0f;
                }
                return;
            }

            switch (m_visibilityState)
            {
                case VisibilityAnimState.Showing:
                    mode = 1;
                    progress = Mathf.Clamp01(m_visibilityProgress01);
                    sourceMaskMode = (int)m_visibilitySourceMaskMode;
                    sourceMaskProgress = Mathf.Clamp01(m_visibilitySourceMaskProgress01);
                    ringWidth = GsplatVisibilityAnimationUtil.CalcRadialWidth(maxRadius, ShowRingWidthNormalized);
                    trailWidth = GsplatVisibilityAnimationUtil.CalcRadialWidth(maxRadius, ShowTrailWidthNormalized);
                    break;

                case VisibilityAnimState.Hiding:
                    mode = 2;
                    progress = Mathf.Clamp01(m_visibilityProgress01);
                    sourceMaskMode = (int)m_visibilitySourceMaskMode;
                    sourceMaskProgress = Mathf.Clamp01(m_visibilitySourceMaskProgress01);
                    ringWidth = GsplatVisibilityAnimationUtil.CalcRadialWidth(maxRadius, HideRingWidthNormalized);
                    trailWidth = GsplatVisibilityAnimationUtil.CalcRadialWidth(maxRadius, HideTrailWidthNormalized);
                    break;

                case VisibilityAnimState.Hidden:
                    gate = 0.0f;
                    sourceMaskMode = (int)VisibilitySourceMaskMode.FullHidden;
                    sourceMaskProgress = 0.0f;
                    break;

                default:
                    sourceMaskMode = (int)VisibilitySourceMaskMode.FullVisible;
                    sourceMaskProgress = 1.0f;
                    break;
            }

            if (sourceMaskMode < (int)VisibilitySourceMaskMode.FullVisible ||
                sourceMaskMode > (int)VisibilitySourceMaskMode.HideSnapshot)
            {
                sourceMaskMode = (int)VisibilitySourceMaskMode.FullVisible;
                sourceMaskProgress = 1.0f;
            }

            if (sourceMaskMode == (int)VisibilitySourceMaskMode.FullVisible)
                sourceMaskProgress = 1.0f;
            else if (sourceMaskMode == (int)VisibilitySourceMaskMode.FullHidden)
                sourceMaskProgress = 0.0f;
        }

        Vector3 CalcVisibilityCenterModel(Bounds localBounds)
        {
            if (VisibilityCenter)
            {
                var worldPos = VisibilityCenter.position;
                if (!float.IsNaN(worldPos.x) && !float.IsNaN(worldPos.y) && !float.IsNaN(worldPos.z) &&
                    !float.IsInfinity(worldPos.x) && !float.IsInfinity(worldPos.y) && !float.IsInfinity(worldPos.z))
                {
                    return transform.InverseTransformPoint(worldPos);
                }
            }

            return localBounds.center;
        }

        static float CalcVisibilityMaxRadius(Bounds localBounds, Vector3 centerModel)
        {
            var ext = localBounds.extents;
            if (ext.x < 0.0f || ext.y < 0.0f || ext.z < 0.0f)
                return 0.0f;

            if (float.IsNaN(ext.x) || float.IsNaN(ext.y) || float.IsNaN(ext.z) ||
                float.IsInfinity(ext.x) || float.IsInfinity(ext.y) || float.IsInfinity(ext.z))
                return 0.0f;

            var c = localBounds.center;
            var maxDistSq = 0.0f;
            for (var sx = -1; sx <= 1; sx += 2)
            for (var sy = -1; sy <= 1; sy += 2)
            for (var sz = -1; sz <= 1; sz += 2)
            {
                var corner = c + new Vector3(sx * ext.x, sy * ext.y, sz * ext.z);
                var d = corner - centerModel;
                var distSq = d.sqrMagnitude;
                if (distSq > maxDistSq)
                    maxDistSq = distSq;
            }

            return Mathf.Sqrt(maxDistSq);
        }

        float CalcVisibilityEffectiveMaxRadius(Bounds localBounds, Vector3 centerModel)
        {
            var rawMaxRadius = CalcVisibilityMaxRadius(localBounds, centerModel);
            return GsplatVisibilityAnimationUtil.CalcScaledMaxRadius(rawMaxRadius, VisibilityRadiusScale);
        }

        float CalcVisibilityAdvancedProgress(float dt, bool hiding, float currentProgress)
        {
            var duration = hiding ? HideDuration : ShowDuration;
            var worldSpeed = hiding ? HideWorldSpeed : ShowWorldSpeed;
            var trailWidthNorm = hiding ? HideTrailWidthNormalized : ShowTrailWidthNormalized;
            var progressMode = GsplatVisibilityAnimationUtil.SanitizeProgressMode(VisibilityProgressMode);

            if (progressMode == GsplatVisibilityProgressMode.LegacyDuration)
            {
                return GsplatVisibilityAnimationUtil.CalcAdvancedProgress(
                    currentProgress,
                    dt,
                    progressMode,
                    duration,
                    worldSpeed,
                    totalRange: 0.0f);
            }

            var localBounds = ResolveVisibilityLocalBoundsForThisFrame();
            var centerModel = CalcVisibilityCenterModel(localBounds);
            var maxRadius = CalcVisibilityEffectiveMaxRadius(localBounds, centerModel);
            var totalRange = GsplatVisibilityAnimationUtil.CalcTotalRange(maxRadius, trailWidthNorm);

            return GsplatVisibilityAnimationUtil.CalcAdvancedProgress(
                currentProgress,
                dt,
                progressMode,
                duration,
                worldSpeed,
                totalRange);
        }

        Bounds CalcVisibilityExpandedRenderBounds(Bounds baseBounds)
        {
            if (!EnableVisibilityAnimation)
                return baseBounds;

            if (m_visibilityState != VisibilityAnimState.Showing && m_visibilityState != VisibilityAnimState.Hiding)
                return baseBounds;

            var ns = NoiseStrength;
            if (float.IsNaN(ns) || float.IsInfinity(ns))
                ns = 0.0f;
            ns = Mathf.Clamp01(ns);
            if (ns <= 0.0f)
                return baseBounds;

            var ws = WarpStrength;
            if (float.IsNaN(ws) || float.IsInfinity(ws) || ws < 0.0f)
                ws = 0.0f;
            ws = Mathf.Clamp(ws, 0.0f, 3.0f);
            if (ws <= 0.0f)
                return baseBounds;

            var centerModel = CalcVisibilityCenterModel(baseBounds);
            var maxRadius = CalcVisibilityEffectiveMaxRadius(baseBounds, centerModel);

            // 与 shader 侧 warpAmp 的同量纲上界(更保守一点),避免 CPU culling 裁掉扭曲位移后的 splats.
            var warpPadding = maxRadius * ns * ws * 0.15f;
            if (warpPadding > 0.0f && !float.IsNaN(warpPadding) && !float.IsInfinity(warpPadding))
                baseBounds.Expand(warpPadding * 2.0f);

            return baseBounds;
        }

        Bounds ResolveVisibilityLocalBoundsForThisFrame()
        {
            var localBounds = SequenceAsset ? SequenceAsset.UnionBounds : new Bounds(Vector3.zero, Vector3.one);
            if (!GsplatLidarExternalTargetHelper.TryComputeWorldBounds(ResolveAllLidarExternalTargetsForScan(),
                    out var externalWorldBounds))
                return localBounds;

            var externalLocalBounds = GsplatUtils.TransformBounds(externalWorldBounds, transform.worldToLocalMatrix);
            localBounds.Encapsulate(externalLocalBounds);
            return localBounds;
        }

        bool UsesFrustumLidarAperture()
        {
            return LidarApertureMode == GsplatLidarApertureMode.CameraFrustum;
        }

        Camera ResolveConfiguredLidarFrustumCameraOrNull()
        {
            return UsesFrustumLidarAperture() && LidarFrustumCamera ? LidarFrustumCamera : null;
        }

        Transform ResolveConfiguredLidarSensorTransformOrNull()
        {
            var frustumCamera = ResolveConfiguredLidarFrustumCameraOrNull();
            if (frustumCamera)
                return frustumCamera.transform;

            return UsesFrustumLidarAperture() ? null : LidarOrigin;
        }

        void LogMissingLidarSensorConfigurationOnce()
        {
            if (m_lidarLoggedMissingOrigin)
                return;

            m_lidarLoggedMissingOrigin = true;
            if (UsesFrustumLidarAperture())
            {
                Debug.LogWarning(
                    "[Gsplat][Sequence][LiDAR] EnableLidarScan=true 且 LidarApertureMode=CameraFrustum, " +
                    "但 LidarFrustumCamera 为空或失效. 当前不会渲染 LiDAR 点云.");
                return;
            }

            Debug.LogWarning(
                "[Gsplat][Sequence][LiDAR] EnableLidarScan=true 但 LidarOrigin 为空. " +
                "请在 Surround360 模式下指定 LidarOrigin Transform.");
        }

        void LogInvalidLidarFrustumConfigurationOnce(string invalidReason)
        {
            if (m_lidarLoggedMissingOrigin)
                return;

            m_lidarLoggedMissingOrigin = true;
            Debug.LogWarning(
                "[Gsplat][Sequence][LiDAR] LidarApertureMode=CameraFrustum, " +
                $"但当前 frustum camera 配置无效: {invalidReason}. LiDAR 点云与 external scan 本轮将跳过.");
        }

        bool TryGetEffectiveLidarLayout(out GsplatLidarLayout layout, out Camera frustumCamera, bool logWhenMissing = true)
        {
            frustumCamera = ResolveConfiguredLidarFrustumCameraOrNull();
            if (!UsesFrustumLidarAperture())
            {
                layout = GsplatLidarLayout.CreateSurround360(
                    LidarAzimuthBins,
                    LidarBeamCount,
                    LidarUpFovDeg,
                    LidarDownFovDeg);
                return true;
            }

            if (!frustumCamera)
            {
                layout = default;
                if (logWhenMissing)
                    LogMissingLidarSensorConfigurationOnce();
                return false;
            }

            if (!GsplatLidarLayout.TryCreateCameraFrustum(
                    frustumCamera,
                    LidarAzimuthBins,
                    LidarBeamCount,
                    LidarUpFovDeg,
                    LidarDownFovDeg,
                    out layout,
                    out var invalidReason))
            {
                if (logWhenMissing)
                    LogInvalidLidarFrustumConfigurationOnce(invalidReason);
                return false;
            }

            m_lidarLoggedMissingOrigin = false;
            return true;
        }

        bool TryGetEffectiveLidarRuntimeContext(out GsplatLidarLayout layout,
            out Matrix4x4 lidarLocalToWorld, out Matrix4x4 worldToLidar,
            out Camera frustumCamera, bool logWhenMissing = true)
        {
            lidarLocalToWorld = Matrix4x4.identity;
            worldToLidar = Matrix4x4.identity;

            if (!TryGetEffectiveLidarLayout(out layout, out frustumCamera, logWhenMissing))
                return false;

            var sensorTransform = ResolveConfiguredLidarSensorTransformOrNull();
            if (!sensorTransform)
            {
                if (logWhenMissing)
                    LogMissingLidarSensorConfigurationOnce();
                return false;
            }

            m_lidarLoggedMissingOrigin = false;
            // 序列后端与静态后端保持同一条 LiDAR 传感器语义:
            // - 传感器矩阵只表达位姿(position + rotation),不吸收节点缩放。
            // - 这样 external target 的世界距离命中与最终点位重建才能保持一致。
            GsplatUtils.BuildRigidTransformMatrices(sensorTransform, out lidarLocalToWorld, out worldToLidar);
            return true;
        }

        bool TryGetEffectiveLidarSensorFrame(out Matrix4x4 lidarLocalToWorld, out Matrix4x4 worldToLidar,
            out Camera frustumCamera, bool logWhenMissing = true)
        {
            return TryGetEffectiveLidarRuntimeContext(out _, out lidarLocalToWorld, out worldToLidar,
                out frustumCamera, logWhenMissing);
        }

        GameObject[] ResolveAllLidarExternalTargetsForScan()
        {
            var staticTargets = LidarExternalStaticTargets ?? Array.Empty<GameObject>();
            var dynamicTargets = LidarExternalDynamicTargets ?? Array.Empty<GameObject>();

            if (staticTargets.Length == 0)
                return dynamicTargets;

            if (dynamicTargets.Length == 0)
                return staticTargets;

            var combinedCount = staticTargets.Length + dynamicTargets.Length;
            if (m_lidarCombinedExternalTargetsScratch == null ||
                m_lidarCombinedExternalTargetsScratch.Length != combinedCount)
            {
                m_lidarCombinedExternalTargetsScratch = new GameObject[combinedCount];
            }

            Array.Copy(staticTargets, 0, m_lidarCombinedExternalTargetsScratch, 0, staticTargets.Length);
            Array.Copy(dynamicTargets, 0, m_lidarCombinedExternalTargetsScratch, staticTargets.Length,
                dynamicTargets.Length);
            return m_lidarCombinedExternalTargetsScratch;
        }

        void DisposeLidarExternalTargetHelper()
        {
            m_lidarExternalTargetHelper?.Dispose();
            m_lidarExternalTargetHelper = null;
        }

        void DisposeLidarExternalGpuCapture()
        {
            m_lidarExternalGpuCapture?.Dispose();
            m_lidarExternalGpuCapture = null;
        }

        void TickLidarExternalHitsIfNeeded(bool rangeImageUpdatedThisFrame)
        {
            if (m_lidarScan == null)
                return;

            var cellCount = m_lidarScan.RangeCellCount;
            if (cellCount <= 0)
                cellCount = Mathf.Max(Mathf.Max(LidarAzimuthBins, 1) * Mathf.Max(LidarBeamCount, 1), 1);
            var externalTargets = ResolveAllLidarExternalTargetsForScan();

            if (externalTargets == null || externalTargets.Length == 0)
            {
                DisposeLidarExternalTargetHelper();
                DisposeLidarExternalGpuCapture();
                m_lidarScan.ClearExternalHits(cellCount);
                return;
            }

            if (!TryGetEffectiveLidarLayout(out var layout, out var frustumCamera, logWhenMissing: false))
            {
                DisposeLidarExternalTargetHelper();
                DisposeLidarExternalGpuCapture();
                m_lidarScan.ClearExternalHits(cellCount);
                return;
            }

            // external route 选择规则:
            // - frustum 模式优先走 GPU capture,因为这是当前主路径.
            // - surround360 或 GPU 路线不可用时,继续回退旧 CPU helper.
            var settings = GsplatSettings.Instance;
            if (layout.IsFrustum && frustumCamera && settings)
            {
                m_lidarExternalGpuCapture ??= new GsplatLidarExternalGpuCapture();
                if (m_lidarExternalGpuCapture.TryCaptureExternalHits(m_lidarScan,
                        settings,
                        frustumCamera,
                        layout,
                        LidarExternalStaticTargets,
                        LidarExternalDynamicTargets,
                        LidarExternalDynamicUpdateHz,
                        LidarExternalCaptureResolutionMode,
                        LidarExternalCaptureResolutionScale,
                        LidarExternalCaptureResolution,
                        LidarExternalEdgeAwareResolveMode,
                        LidarExternalSubpixelResolveMode,
                        LidarExternalTargetVisibilityMode))
                {
                    DisposeLidarExternalTargetHelper();
                    return;
                }
            }
            else
            {
                DisposeLidarExternalGpuCapture();
            }

            if (!rangeImageUpdatedThisFrame)
                return;

            m_lidarExternalTargetHelper ??= new GsplatLidarExternalTargetHelper();
            m_lidarExternalTargetHelper.TryUpdateExternalHits(m_lidarScan,
                externalTargets,
                LidarExternalTargetVisibilityMode,
                ResolveConfiguredLidarSensorTransformOrNull(),
                layout,
                LidarDepthFar);
        }

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

            InitVisibilityOnEnable();
            InitRenderStyleOnEnable();
            InitLidarAnimationOnEnable();
            PushVisibilityUniformsForThisFrame(ResolveVisibilityLocalBoundsForThisFrame());
            PushRenderStyleUniformsForThisFrame();
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            // Editor 下该组件被禁用时,显隐动画 ticker 也必须解绑,避免静态集合残留引用.
            UnregisterVisibilityEditorTickerIfAny();
#endif
            GsplatSorter.Instance.UnregisterGsplat(this);
            DisposeDecodeResources();
            DisposeLidarExternalTargetHelper();
            DisposeLidarExternalGpuCapture();
            m_lidarScan?.Dispose();
            m_lidarScan = null;
            m_renderer?.Dispose();
            m_renderer = null;
            m_prevAsset = null;
            m_lidarKeepAliveDuringFadeOut = false;

            // 释放 runtime bundle 与运行时创建的 asset/纹理.
            m_runtimeBundle?.Dispose();
            m_runtimeBundle = null;
            DestroyOwnedRuntimeSequenceAssetIfAny();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // 编辑态拖动 `TimeNormalized` 时,主动触发 SceneView 刷新,
            // 避免出现“切到 GameView 才更新”的错觉.
            if (Application.isPlaying)
                return;

            ValidateVisibilitySerializedFields();

            var t = TimeNormalized;
            if (float.IsNaN(t) || float.IsInfinity(t))
                t = 0.0f;
            t = Mathf.Clamp01(t);

            m_timeNormalizedThisFrame = t;

            // Render style(编辑态参数同步):
            var dotRadius = ParticleDotRadiusPixels;
            if (float.IsNaN(dotRadius) || float.IsInfinity(dotRadius) || dotRadius < 0.0f)
                ParticleDotRadiusPixels = 0.0f;

            var dur = RenderStyleSwitchDurationSeconds;
            if (float.IsNaN(dur) || float.IsInfinity(dur) || dur < 0.0f)
                RenderStyleSwitchDurationSeconds = 1.5f;

            if (!m_renderStyleAnimating)
            {
                m_renderStyleBlend01 = RenderStyle == GsplatRenderStyle.ParticleDots ? 1.0f : 0.0f;
                m_renderStyleAlphaBlend01 = m_renderStyleBlend01;
            }

            // LiDAR(编辑态参数同步):
            ValidateLidarSerializedFields();
            SyncLidarColorBlendTargetFromSerializedMode(animated: true);
            if (!m_lidarVisibilityAnimating && !IsLidarRuntimeActive())
                m_lidarVisibility01 = 0.0f;

            PushRenderStyleUniformsForThisFrame();

            // LiDAR 扫描前沿在 EditMode 需要持续 repaint:
            // - 这里在 OnValidate 时尝试注册 ticker,避免“打开开关但视图不动”的错觉.
            RegisterVisibilityEditorTickerIfAnimating();

            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            // RepaintAllViews 会同时刷新 SceneView/GameView,避免“拖动 Inspector 滑条时 GameView 不更新”的错觉.
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
#endif

        void Update()
        {
            ValidateVisibilitySerializedFields();

            AdvancePendingRadarToGaussianShowSwitchIfNeeded();
            AdvancePendingGaussianToRadarShowSwitchIfNeeded();
            AdvanceVisibilityStateIfNeeded();
            AdvanceRenderStyleStateIfNeeded();
            AdvanceLidarAnimationStateIfNeeded();

#if UNITY_EDITOR
            // EditMode 下,LiDAR 扫描前沿/余辉需要持续 repaint 才能看到变化.
            // - 这里做一次轻量注册,即便用户在脚本里直接改 EnableLidarScan,也能自愈.
            RegisterVisibilityEditorTickerIfAnimating();
#endif

            // LiDAR 参数防御:
            // - Play 模式下用户可能从脚本写入 NaN/Inf/负数.
            // - 这里仅在启用 LiDAR 时做一次轻量 clamp,保证后续 compute/draw 不会炸.
            if (IsLidarRuntimeActive())
            {
                ValidateLidarSerializedFields();
                SyncLidarColorBlendTargetFromSerializedMode(animated: true);
            }
            else if (m_lidarScan != null)
            {
                // 资源释放策略:
                // - LiDAR 默认关闭,因此当用户把开关关掉时,我们选择立即释放 range/LUT buffers,
                //   避免长时间占用 GPU 内存.
                // - 下次再打开时会按当前参数重建.
                DisposeLidarExternalTargetHelper();
                DisposeLidarExternalGpuCapture();
                m_lidarScan.Dispose();
                m_lidarScan = null;
            }

            // ----------------------------------------------------------------
            // 稳态恢复: 与 GsplatRenderer 一致,在 Editor/Metal 下 buffer 可能会失效.
            // - 这里做一次节流的自动重建,避免序列后端出现“突然消失且无法自愈”.
            // ----------------------------------------------------------------
            if (!m_disabledDueToError && m_renderer != null && !m_renderer.Valid && SequenceAsset)
            {
                var now = Time.realtimeSinceStartup;
                if (now >= m_nextRendererRecoveryTime)
                {
                    m_nextRendererRecoveryTime = now + 1.0f;
                    Debug.LogWarning("[Gsplat][Sequence] 检测到 GraphicsBuffer 已失效,将尝试自动重建 renderer/decoder 资源(可能会有一次性卡顿).");

                    if (!TryCreateOrRecreateRenderer())
                        return;

                    // renderer 变化时,旧的 decode 资源不再安全,必须重建.
                    DisposeDecodeResources();
                    if (!TryCreateOrRecreateDecodeResources())
                        return;
                }
            }

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

            // LiDAR GPU 资源准备:
            // - 这里先完成 buffers/LUT 的创建与重建,为后续 compute/draw 链路打底.
            // - 注意: `-batchmode -nographics` 下 graphicsDeviceType=Null,必须跳过任何 GPU 资源创建.
            if (IsLidarRuntimeActive() && m_renderer != null && m_renderer.Valid)
                EnsureLidarResources();

            if (!Valid)
                return;

            // ----------------------------------------------------------------
            // tasks 4.3/4.6:
            // - 在排序与渲染之前,先把当前时间的两帧数据解码+插值写入 float buffers.
            // - 这样 sorter 读取 PositionBuffer 时就是“当前帧结果”,不需要改排序管线.
            // ----------------------------------------------------------------
            if (!TryDecodeThisFrame())
                return;

            // LiDAR range image 更新:
            // - 序列后端的 PositionBuffer 是每帧 decode 生成的,因此 LiDAR 的采样必须发生在 decode 之后.
            var rangeImageUpdatedThisFrame = TickLidarRangeImageIfNeeded();
            TickLidarExternalHitsIfNeeded(rangeImageUpdatedThisFrame);

#if UNITY_EDITOR
            var settings = GsplatSettings.Instance;
            if (!Application.isPlaying &&
                GsplatSorter.Instance.SortDrivenBySrpCallback &&
                settings && settings.CameraMode == GsplatCameraMode.ActiveCameraOnly)
            {
                // do nothing: draw submitted via camera callbacks.
            }
            else
#endif
            {
                if (ShouldSubmitSplatsThisFrame())
                {
                    var localBounds = ResolveVisibilityLocalBoundsForThisFrame();
                    PushVisibilityUniformsForThisFrame(localBounds);
                    PushRenderStyleUniformsForThisFrame();

                    var boundsForRender = CalcVisibilityExpandedRenderBounds(localBounds);
                    m_renderer.Render(SplatCount, transform, boundsForRender, gameObject.layer,
                        GammaToLinear, EnableFootprintAACompensation, SHDegree, m_timeNormalizedThisFrame, motionPadding: 0.0f,
                        timeModel: 1, temporalCutoff: 0.01f);
                }

                // LiDAR 点云的 Update 渲染路径:
                // - Play 模式下 sorter 不会在相机回调里补交 draw,因此这里需要从 Update 提交一次.
                // - EditMode + SRP + ActiveCameraOnly 时,Update 不提交,点云将由 SubmitDrawForCamera 提交.
                RenderLidarInUpdateIfNeeded();
            }
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

            var scaleCodebookCount = SequenceAsset.ScaleCodebook != null ? SequenceAsset.ScaleCodebook.Length : 0;

            // v2 兼容策略: 历史 v1 资产可能没有写入 Sog4DVersion(默认 0),这里把它视为 v1.
            var assetVersion = SequenceAsset.Sog4DVersion;
            if (assetVersion == 0)
                assetVersion = 1;
            var useV2 = assetVersion == 2;

            // `.sog4d` 的量化纹理里,SH rest 的 labels 张数:
            // - v1: 1 张 shN_labels
            // - v2: sh1/sh2/sh3 各 1 张,因此张数=effectiveShBands(1..3)
            void CalcShRestBudget(byte effectiveBands, out int labelStreamCount, out int centroidsVecCount)
            {
                labelStreamCount = 0;
                centroidsVecCount = 0;

                if (effectiveBands <= 0)
                    return;

                labelStreamCount = useV2 ? effectiveBands : 1;

                if (useV2)
                {
                    // v2: centroids 是分 band 的多套 codebook.
                    centroidsVecCount += SequenceAsset.Sh1Count * 3;
                    if (effectiveBands >= 2)
                        centroidsVecCount += SequenceAsset.Sh2Count * 5;
                    if (effectiveBands >= 3)
                        centroidsVecCount += SequenceAsset.Sh3Count * 7;
                }
                else
                {
                    // v1: 单一 shN codebook,每个 label 有 restCoeffCount 个 float3.
                    var restCoeffCount = GsplatUtils.SHBandsToCoefficientCount(effectiveBands);
                    centroidsVecCount = SequenceAsset.ShNCount * restCoeffCount;
                }
            }

            // tasks 6.1: 把 `.sog4d` 的量化纹理 + palette 纳入预算估算.
            // tasks 9.2: 当启用 chunk streaming 时,实际 GPU 常驻帧数应以 loaded chunk 为准(避免误触发 AutoDegrade).
            var loadedFrameCountForGpuEstimate = SequenceAsset.FrameCount;
            if (m_runtimeBundle != null && m_runtimeBundle.ChunkingEnabled && m_runtimeBundle.LoadedChunkFrameCount > 0)
                loadedFrameCountForGpuEstimate = m_runtimeBundle.LoadedChunkFrameCount;

            CalcShRestBudget(m_effectiveSHBands, out var shRestLabelStreamCount, out var shRestCentroidsVecCount);

            var desiredBytes = GsplatUtils.EstimateSog4dGpuBytes(
                m_effectiveSplatCount,
                loadedFrameCountForGpuEstimate,
                SequenceAsset.Layout.Width,
                SequenceAsset.Layout.Height,
                m_effectiveSHBands,
                scaleCodebookCount,
                shRestLabelStreamCount,
                shRestCentroidsVecCount);
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
                CalcShRestBudget(m_effectiveSHBands, out var shRestLabelStreamCountAfter, out var shRestCentroidsVecCountAfter);

                var afterBytes = GsplatUtils.EstimateSog4dGpuBytes(
                    m_effectiveSplatCount,
                    loadedFrameCountForGpuEstimate,
                    SequenceAsset.Layout.Width,
                    SequenceAsset.Layout.Height,
                    m_effectiveSHBands,
                    scaleCodebookCount,
                    shRestLabelStreamCountAfter,
                    shRestCentroidsVecCountAfter);
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
            m_sh1CentroidsBuffer?.Dispose();
            m_sh2CentroidsBuffer?.Dispose();
            m_sh3CentroidsBuffer?.Dispose();

            m_scaleCodebookBuffer = null;
            m_sh0CodebookBuffer = null;
            m_shNCentroidsBuffer = null;
            m_sh1CentroidsBuffer = null;
            m_sh2CentroidsBuffer = null;
            m_sh3CentroidsBuffer = null;

            m_decodeCS = null;
            m_kernelDecodeSH0 = -1;
            m_kernelDecodeSH = -1;
            m_kernelDecodeSHV2 = -1;
        }

        static void NormalizeLidarExternalTargets(ref GameObject[] lidarExternalTargets)
        {
            // 说明:
            // - Unity 历史场景升级后,新字段常见状态是 null.
            // - 这里统一归一化成空数组,避免后续 helper/遍历逻辑反复判空.
            if (lidarExternalTargets == null || lidarExternalTargets.Length == 0)
            {
                lidarExternalTargets = Array.Empty<GameObject>();
                return;
            }

            // 说明:
            // - Inspector 的数组槽位经常会残留 Missing/None.
            // - 这里仅清理空引用,保留用户原始顺序,不偷偷做去重或重排.
            var validCount = 0;
            for (var i = 0; i < lidarExternalTargets.Length; i++)
            {
                if (lidarExternalTargets[i])
                    validCount++;
            }

            if (validCount == lidarExternalTargets.Length)
                return;

            if (validCount == 0)
            {
                lidarExternalTargets = Array.Empty<GameObject>();
                return;
            }

            var normalizedTargets = new GameObject[validCount];
            var writeIndex = 0;
            for (var i = 0; i < lidarExternalTargets.Length; i++)
            {
                var target = lidarExternalTargets[i];
                if (!target)
                    continue;

                normalizedTargets[writeIndex++] = target;
            }

            lidarExternalTargets = normalizedTargets;
        }

        void ValidateLidarSerializedFields()
        {
            // 说明:
            // - 与 GsplatRenderer 的同名函数保持一致,避免两个组件对同一组字段产生不同语义.
            // - 这是“字段级别”的参数校验,会直接写回序列化字段.

            if (float.IsNaN(LidarRotationHz) || float.IsInfinity(LidarRotationHz) || LidarRotationHz <= 0.0f)
                LidarRotationHz = 5.0f;

            if (float.IsNaN(LidarUpdateHz) || float.IsInfinity(LidarUpdateHz) || LidarUpdateHz <= 0.0f)
                LidarUpdateHz = 10.0f;

            if (LidarApertureMode != GsplatLidarApertureMode.Surround360 &&
                LidarApertureMode != GsplatLidarApertureMode.CameraFrustum)
            {
                LidarApertureMode = GsplatLidarApertureMode.Surround360;
            }

            if (float.IsNaN(LidarExternalDynamicUpdateHz) || float.IsInfinity(LidarExternalDynamicUpdateHz) ||
                LidarExternalDynamicUpdateHz <= 0.0f)
            {
                LidarExternalDynamicUpdateHz = 10.0f;
            }

            LidarExternalCaptureResolutionMode =
                GsplatUtils.SanitizeLidarExternalCaptureResolutionMode(LidarExternalCaptureResolutionMode);
            LidarExternalEdgeAwareResolveMode =
                GsplatUtils.SanitizeLidarExternalEdgeAwareResolveMode(LidarExternalEdgeAwareResolveMode);
            LidarExternalSubpixelResolveMode =
                GsplatUtils.SanitizeLidarExternalSubpixelResolveMode(LidarExternalSubpixelResolveMode);

            if (float.IsNaN(LidarExternalCaptureResolutionScale) || float.IsInfinity(LidarExternalCaptureResolutionScale) ||
                LidarExternalCaptureResolutionScale <= 0.0f)
            {
                LidarExternalCaptureResolutionScale = 1.0f;
            }

            if (LidarExternalCaptureResolution.x < 1)
                LidarExternalCaptureResolution.x = 1920;
            if (LidarExternalCaptureResolution.y < 1)
                LidarExternalCaptureResolution.y = 1080;

            if (LidarAzimuthBins < 64)
                LidarAzimuthBins = 2048;
            // 说明:
            // - 按用户需求: 不再对 LidarAzimuthBins 设置最大值上限.
            // - 风险: 过大的 azimuthBins 会导致 range image 分辨率变大,显著增加 compute 与显存开销.
            // - 这里仅保留最小值防御,避免 0/负数/过小引发异常.

            if (float.IsNaN(LidarUpFovDeg) || float.IsInfinity(LidarUpFovDeg))
                LidarUpFovDeg = 10.0f;
            if (float.IsNaN(LidarDownFovDeg) || float.IsInfinity(LidarDownFovDeg))
                LidarDownFovDeg = -30.0f;
            LidarUpFovDeg = Mathf.Clamp(LidarUpFovDeg, 0.0f, 89.0f);
            LidarDownFovDeg = Mathf.Clamp(LidarDownFovDeg, -89.0f, 0.0f);

            // BeamCount:
            // - 只保留一个总线束数,竖直方向按 [DownFov..UpFov] 匀角度采样.
            // - 当前不再做最大值 clamp,让高线束 LiDAR 配置可以完整传到底层.
            // - 实际可承受上限主要取决于 beamCount * azimuthBins 带来的 buffer / compute / 显存成本.
            if (LidarBeamCount < 1)
                LidarBeamCount = GsplatUtils.k_LidarDefaultBeamCount;

            if (float.IsNaN(LidarDepthNear) || float.IsInfinity(LidarDepthNear) || LidarDepthNear <= 0.0f)
                LidarDepthNear = 1.0f;
            if (float.IsNaN(LidarDepthFar) || float.IsInfinity(LidarDepthFar) || LidarDepthFar <= 0.0f)
                LidarDepthFar = 200.0f;
            if (LidarDepthFar <= LidarDepthNear)
                LidarDepthFar = LidarDepthNear + 1.0f;

            if (float.IsNaN(LidarPointRadiusPixels) || float.IsInfinity(LidarPointRadiusPixels))
                LidarPointRadiusPixels = 2.0f;
            if (LidarPointRadiusPixels < 0.0f)
                LidarPointRadiusPixels = 0.0f;
            if (float.IsNaN(LidarPointJitterCellFraction) || float.IsInfinity(LidarPointJitterCellFraction))
                LidarPointJitterCellFraction = 0.35f;
            else
                LidarPointJitterCellFraction = Mathf.Clamp01(LidarPointJitterCellFraction);
            if (float.IsNaN(LidarExternalHitBiasMeters) || float.IsInfinity(LidarExternalHitBiasMeters))
                LidarExternalHitBiasMeters = 0.0f;
            if (LidarExternalHitBiasMeters < 0.0f)
                LidarExternalHitBiasMeters = 0.0f;

            LidarParticleAntialiasingMode =
                GsplatUtils.SanitizeLidarParticleAntialiasingMode(LidarParticleAntialiasingMode);
            if (float.IsNaN(LidarParticleAAFringePixels) || float.IsInfinity(LidarParticleAAFringePixels))
                LidarParticleAAFringePixels = 1.0f;
            if (LidarParticleAAFringePixels < 0.0f)
                LidarParticleAAFringePixels = 0.0f;

            if (float.IsNaN(LidarShowDuration) || float.IsInfinity(LidarShowDuration) || LidarShowDuration < 0.0f)
                LidarShowDuration = -1.0f;
            if (float.IsNaN(LidarHideDuration) || float.IsInfinity(LidarHideDuration) || LidarHideDuration < 0.0f)
                LidarHideDuration = -1.0f;

            if (float.IsNaN(LidarShowHideWarpPixels) || float.IsInfinity(LidarShowHideWarpPixels) || LidarShowHideWarpPixels < 0.0f)
                LidarShowHideWarpPixels = 6.0f;

            if (float.IsNaN(LidarShowHideNoiseScale) || float.IsInfinity(LidarShowHideNoiseScale) || LidarShowHideNoiseScale < 0.0f)
                LidarShowHideNoiseScale = -1.0f;

            if (float.IsNaN(LidarShowHideNoiseSpeed) || float.IsInfinity(LidarShowHideNoiseSpeed) || LidarShowHideNoiseSpeed < 0.0f)
                LidarShowHideNoiseSpeed = -1.0f;

            if (float.IsNaN(LidarShowHideGlowColor.r) || float.IsNaN(LidarShowHideGlowColor.g) || float.IsNaN(LidarShowHideGlowColor.b) ||
                float.IsInfinity(LidarShowHideGlowColor.r) || float.IsInfinity(LidarShowHideGlowColor.g) || float.IsInfinity(LidarShowHideGlowColor.b))
            {
                LidarShowHideGlowColor = new Color(1.0f, 0.45f, 0.1f, 1.0f);
            }

            LidarDepthNearColor =
                GsplatUtils.SanitizeColorRgb(LidarDepthNearColor, GsplatUtils.k_LidarDepthNearColorDefault);
            LidarDepthFarColor =
                GsplatUtils.SanitizeColorRgb(LidarDepthFarColor, GsplatUtils.k_LidarDepthFarColorDefault);

            if (float.IsNaN(LidarShowGlowIntensity) || float.IsInfinity(LidarShowGlowIntensity) || LidarShowGlowIntensity < 0.0f)
                LidarShowGlowIntensity = 1.5f;

            if (float.IsNaN(LidarHideGlowIntensity) || float.IsInfinity(LidarHideGlowIntensity) || LidarHideGlowIntensity < 0.0f)
                LidarHideGlowIntensity = 2.5f;

            if (float.IsNaN(LidarTrailGamma) || float.IsInfinity(LidarTrailGamma) || LidarTrailGamma < 0.0f)
                LidarTrailGamma = 2.0f;

            if (float.IsNaN(LidarIntensity) || float.IsInfinity(LidarIntensity) || LidarIntensity < 0.0f)
                LidarIntensity = 1.0f;

            if (float.IsNaN(LidarUnscannedIntensity) || float.IsInfinity(LidarUnscannedIntensity) || LidarUnscannedIntensity < 0.0f)
                LidarUnscannedIntensity = 0.2f;

            // 距离衰减模式:
            // - 该字段可能来自序列化(例如 YAML 手工改坏或历史版本漂移),这里做一次防御性校验.
            // - 非法值统一回退到默认 Reciprocal,以保持兼容.
            if (LidarIntensityDistanceDecayMode != GsplatLidarDistanceDecayMode.Reciprocal &&
                LidarIntensityDistanceDecayMode != GsplatLidarDistanceDecayMode.Exponential)
            {
                LidarIntensityDistanceDecayMode = GsplatLidarDistanceDecayMode.Reciprocal;
            }

            if (LidarExternalTargetVisibilityMode != GsplatLidarExternalTargetVisibilityMode.KeepVisible &&
                LidarExternalTargetVisibilityMode != GsplatLidarExternalTargetVisibilityMode.ForceRenderingOff &&
                LidarExternalTargetVisibilityMode != GsplatLidarExternalTargetVisibilityMode.ForceRenderingOffInPlayMode)
            {
                LidarExternalTargetVisibilityMode = GsplatLidarExternalTargetVisibilityMode.ForceRenderingOff;
            }

            if (float.IsNaN(LidarIntensityDistanceDecay) || float.IsInfinity(LidarIntensityDistanceDecay) || LidarIntensityDistanceDecay < 0.0f)
                LidarIntensityDistanceDecay = 0.0f;

            if (float.IsNaN(LidarUnscannedIntensityDistanceDecay) || float.IsInfinity(LidarUnscannedIntensityDistanceDecay) || LidarUnscannedIntensityDistanceDecay < 0.0f)
                LidarUnscannedIntensityDistanceDecay = 0.0f;

            if (float.IsNaN(LidarDepthOpacity) || float.IsInfinity(LidarDepthOpacity) || LidarDepthOpacity < 0.0f)
                LidarDepthOpacity = 1.0f;
            LidarDepthOpacity = Mathf.Clamp01(LidarDepthOpacity);

            if (float.IsNaN(LidarMinSplatOpacity) || float.IsInfinity(LidarMinSplatOpacity) || LidarMinSplatOpacity < 0.0f)
                LidarMinSplatOpacity = 1.0f / 255.0f;
            LidarMinSplatOpacity = Mathf.Clamp01(LidarMinSplatOpacity);

            NormalizeLidarExternalTargets(ref LidarExternalStaticTargets);
            NormalizeLidarExternalTargets(ref LidarExternalDynamicTargets);
        }

        void ValidateVisibilitySerializedFields()
        {
            VisibilityProgressMode = GsplatVisibilityAnimationUtil.SanitizeProgressMode(VisibilityProgressMode);

            VisibilityRadiusScale = GsplatVisibilityAnimationUtil.SanitizeRadiusScale(VisibilityRadiusScale);
            ShowWorldSpeed = GsplatVisibilityAnimationUtil.SanitizeWorldSpeed(
                ShowWorldSpeed,
                GsplatVisibilityAnimationUtil.k_DefaultShowWorldSpeed);
            HideWorldSpeed = GsplatVisibilityAnimationUtil.SanitizeWorldSpeed(
                HideWorldSpeed,
                GsplatVisibilityAnimationUtil.k_DefaultHideWorldSpeed);

            ShowDuration = GsplatVisibilityAnimationUtil.SanitizeDuration(ShowDuration, 1.0f);
            HideDuration = GsplatVisibilityAnimationUtil.SanitizeDuration(HideDuration, 1.2f);

            if (float.IsNaN(DualTrackSwitchTriggerProgress01) || float.IsInfinity(DualTrackSwitchTriggerProgress01))
                DualTrackSwitchTriggerProgress01 = k_defaultDualTrackSwitchTriggerProgress01;
            DualTrackSwitchTriggerProgress01 = Mathf.Clamp01(DualTrackSwitchTriggerProgress01);
        }

        void EnsureLidarResources()
        {
            // 重要 guard:
            // - CI/命令行测试常用 `-batchmode -nographics`,此时 graphicsDeviceType 为 Null.
            // - 在 Null 设备上创建 GraphicsBuffer/ComputeShader 很容易刷 error log,导致测试失败.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;

            if (!TryGetEffectiveLidarLayout(out var layout, out _, logWhenMissing: true))
                return;

            m_lidarScan ??= new GsplatLidarScan();
            m_lidarScan.EnsureRangeImageBuffers(layout);
            m_lidarScan.EnsureLutBuffers(layout);
        }

        bool TickLidarRangeImageIfNeeded()
        {
            // 说明:
            // - v1 采用方案 X: UpdateHz 门禁下,每次全量重建一次 360 range image.
            // - 序列后端的 PositionBuffer 是每帧 decode 生成的,因此该函数必须在 TryDecodeThisFrame 之后调用.
            if (!IsLidarRuntimeActive() || m_lidarScan == null || m_renderer == null || !m_renderer.Valid)
                return false;

            if (!TryGetEffectiveLidarRuntimeContext(out var layout, out _, out var worldToLidar, out _,
                    logWhenMissing: true))
                return false;

            var settings = GsplatSettings.Instance;
            if (!settings || !settings.ComputeShader)
                return false;

            var now = (double)Time.realtimeSinceStartup;
            if (!m_lidarScan.IsRangeImageUpdateDue(now, LidarUpdateHz))
                return false;

            // sequence 后端目前没有 keyframe segment 子范围,因此采样全量 splats.
            var count = SplatCount > int.MaxValue ? int.MaxValue : (int)SplatCount;
            var modelToLidar = worldToLidar * transform.localToWorldMatrix;

            var needsSplatId = LidarNeedsSplatIdThisFrame();
            if (m_lidarScan.TryRebuildRangeImage(settings.ComputeShader,
                    m_renderer.PositionBuffer,
                    m_renderer.VelocityBuffer,
                    m_renderer.TimeBuffer,
                    m_renderer.DurationBuffer,
                    m_renderer.ColorBuffer,
                    m_renderer.Has4D ? 1 : 0,
                    m_timeNormalizedThisFrame,
                    timeModel: 1,
                    temporalCutoff: 0.01f,
                    LidarMinSplatOpacity,
                    modelToLidar, splatBaseIndex: 0, splatCount: count,
                    layout,
                    LidarDepthNear, LidarDepthFar,
                    needsSplatId))
            {
                m_lidarScan.MarkRangeImageUpdated(now);
                return true;
            }

            return false;
        }

        void RenderLidarInUpdateIfNeeded()
        {
            if (!IsLidarRuntimeActive() || m_lidarScan == null || m_renderer == null || !m_renderer.Valid)
                return;

            if (!TryGetEffectiveLidarRuntimeContext(out var layout, out var lidarLocalToWorld, out _, out _,
                    logWhenMissing: true))
                return;

            var settings = GsplatSettings.Instance;
            if (!settings)
                return;

#if UNITY_EDITOR
            // Editor 非 Play + SRP + ActiveCameraOnly:
            // - draw 由 sorter 的相机回调驱动(见 SubmitDrawForCamera),Update 不提交,避免双绘制.
            if (!Application.isPlaying &&
                GsplatSorter.Instance.SortDrivenBySrpCallback &&
                settings.CameraMode == GsplatCameraMode.ActiveCameraOnly)
            {
                return;
            }
#endif

            Camera targetCam = null;
            if (settings.CameraMode == GsplatCameraMode.ActiveCameraOnly)
            {
                if (!GsplatSorter.Instance.TryGetActiveCamera(out var cam) || !cam)
                    return;
                targetCam = cam;
            }

            var localBounds = ResolveVisibilityLocalBoundsForThisFrame();
            BuildLidarShowHideOverlayForThisFrame(localBounds,
                out var showHideGate,
                out var showHideMode,
                out var showHideProgress,
                out var showHideSourceMaskMode,
                out var showHideSourceMaskProgress,
                out var showHideCenterModel,
                out var showHideMaxRadius,
                out var showHideRingWidth,
                out var showHideTrailWidth);

            if (showHideGate <= 1.0e-4f && showHideMode == 0)
                return;

            var showHideGlowIntensity = showHideMode == 2 ? LidarHideGlowIntensity : LidarShowGlowIntensity;
            var showHideNoiseScale = LidarShowHideNoiseScale >= 0.0f ? LidarShowHideNoiseScale : NoiseScale;
            var showHideNoiseSpeed = LidarShowHideNoiseSpeed >= 0.0f ? LidarShowHideNoiseSpeed : NoiseSpeed;
            var lidarUnscannedIntensity = ResolveLidarUnscannedIntensityForShader();
            var effectiveLidarParticleAaMode =
                GsplatUtils.ResolveEffectiveLidarParticleAntialiasingMode(LidarParticleAntialiasingMode, targetCam);

            m_lidarScan.RenderPointCloud(settings, targetCam, gameObject.layer, GammaToLinear,
                layout,
                lidarLocalToWorld, Time.realtimeSinceStartup, LidarRotationHz, LidarEnableScanMotion,
                LidarDepthNear, LidarDepthFar, LidarDepthNearColor, LidarDepthFarColor,
                LidarPointRadiusPixels, LidarPointJitterCellFraction, LidarParticleAAFringePixels,
                LidarParticleAntialiasingMode, effectiveLidarParticleAaMode,
                LidarColorMode, m_lidarColorBlend01, m_lidarVisibility01,
                LidarTrailGamma, LidarIntensity, lidarUnscannedIntensity,
                LidarIntensityDistanceDecay, LidarUnscannedIntensityDistanceDecay, LidarIntensityDistanceDecayMode,
                LidarDepthOpacity, LidarExternalHitBiasMeters,
                m_renderer.ColorBuffer,
                transform.worldToLocalMatrix,
                showHideGate, showHideMode, showHideProgress,
                showHideSourceMaskMode, showHideSourceMaskProgress,
                showHideCenterModel, showHideMaxRadius, showHideRingWidth, showHideTrailWidth,
                (int)VisibilityNoiseMode, NoiseStrength, showHideNoiseScale, showHideNoiseSpeed,
                LidarShowHideWarpPixels, WarpStrength,
                LidarShowHideGlowColor, showHideGlowIntensity);
        }

        void RenderLidarForCamera(Camera camera)
        {
            if (!IsLidarRuntimeActive() || m_lidarScan == null || m_renderer == null || !m_renderer.Valid)
                return;

            if (!camera)
                return;

            if (!TryGetEffectiveLidarRuntimeContext(out var layout, out var lidarLocalToWorld, out _, out _,
                    logWhenMissing: true))
                return;

            if ((camera.cullingMask & (1 << gameObject.layer)) == 0)
                return;

            var settings = GsplatSettings.Instance;
            if (!settings)
                return;

            var localBounds = ResolveVisibilityLocalBoundsForThisFrame();
            BuildLidarShowHideOverlayForThisFrame(localBounds,
                out var showHideGate,
                out var showHideMode,
                out var showHideProgress,
                out var showHideSourceMaskMode,
                out var showHideSourceMaskProgress,
                out var showHideCenterModel,
                out var showHideMaxRadius,
                out var showHideRingWidth,
                out var showHideTrailWidth);

            if (showHideGate <= 1.0e-4f && showHideMode == 0)
                return;

            var showHideGlowIntensity = showHideMode == 2 ? LidarHideGlowIntensity : LidarShowGlowIntensity;
            var showHideNoiseScale = LidarShowHideNoiseScale >= 0.0f ? LidarShowHideNoiseScale : NoiseScale;
            var showHideNoiseSpeed = LidarShowHideNoiseSpeed >= 0.0f ? LidarShowHideNoiseSpeed : NoiseSpeed;
            var lidarUnscannedIntensity = ResolveLidarUnscannedIntensityForShader();
            var effectiveLidarParticleAaMode =
                GsplatUtils.ResolveEffectiveLidarParticleAntialiasingMode(LidarParticleAntialiasingMode, camera);

            m_lidarScan.RenderPointCloud(settings, camera, gameObject.layer, GammaToLinear,
                layout,
                lidarLocalToWorld, Time.realtimeSinceStartup, LidarRotationHz, LidarEnableScanMotion,
                LidarDepthNear, LidarDepthFar, LidarDepthNearColor, LidarDepthFarColor,
                LidarPointRadiusPixels, LidarPointJitterCellFraction, LidarParticleAAFringePixels,
                LidarParticleAntialiasingMode, effectiveLidarParticleAaMode,
                LidarColorMode, m_lidarColorBlend01, m_lidarVisibility01,
                LidarTrailGamma, LidarIntensity, lidarUnscannedIntensity,
                LidarIntensityDistanceDecay, LidarUnscannedIntensityDistanceDecay, LidarIntensityDistanceDecayMode,
                LidarDepthOpacity, LidarExternalHitBiasMeters,
                m_renderer.ColorBuffer,
                transform.worldToLocalMatrix,
                showHideGate, showHideMode, showHideProgress,
                showHideSourceMaskMode, showHideSourceMaskProgress,
                showHideCenterModel, showHideMaxRadius, showHideRingWidth, showHideTrailWidth,
                (int)VisibilityNoiseMode, NoiseStrength, showHideNoiseScale, showHideNoiseSpeed,
                LidarShowHideWarpPixels, WarpStrength,
                LidarShowHideGlowColor, showHideGlowIntensity);
        }

        bool ShouldSubmitSplatsThisFrame()
        {
            // 说明:
            // - splat 的 sort/draw 与 LiDAR 是解耦的.
            // - 当用户启用 LiDAR 且要求隐藏 splats 时,这里返回 false,从根源停掉排序与绘制开销.
            if (!EnableGsplatBackend)
                return false;

            if (EnableLidarScan &&
                HideSplatsWhenLidarEnabled &&
                !ShouldDelayHideSplatsForLidarFadeIn() &&
                !m_pendingRadarToGaussianDisableLidar &&
                !m_pendingGaussianToRadarFinalizeRadarMode)
            {
                return false;
            }

            // burn reveal 的 Hidden 状态表示“完全不可见”,并且应停掉 sort/draw.
            if (m_visibilityState == VisibilityAnimState.Hidden)
                return false;

            if (m_disabledDueToError || !SequenceAsset)
                return false;

            return true;
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

            // v2 兼容策略:
            // - 新增的 `SequenceAsset.Sog4DVersion` 字段对历史 v1 资产来说可能是默认值 0.
            // - 因此这里把 0 视为 v1,避免旧资产在升级脚本后突然无法播放.
            var assetVersion = SequenceAsset.Sog4DVersion;
            if (assetVersion == 0)
                assetVersion = 1;
            var useV2 = assetVersion == 2;

            // 仅验证“当前会实际使用”的 kernel,避免某个未使用 kernel 的反射/编译问题阻塞播放.
            // - SHBands==0: 只会用 DecodeKeyframesSH0
            // - SHBands>0:
            //   - v1: DecodeKeyframesSH
            //   - v2: DecodeKeyframesSH_V2
            if (m_effectiveSHBands > 0)
            {
                if (useV2)
                {
                    if (!m_decodeCS.HasKernel("DecodeKeyframesSH_V2"))
                    {
                        Debug.LogError("[Gsplat][Sequence] DecodeComputeShader 缺少必需 kernel: DecodeKeyframesSH_V2.");
                        m_disabledDueToError = true;
                        DisposeDecodeResources();
                        return false;
                    }

                    m_kernelDecodeSHV2 = m_decodeCS.FindKernel("DecodeKeyframesSH_V2");
                    m_kernelDecodeSH = -1;
                    m_kernelDecodeSH0 = -1;

                    if (!TryValidateDecodeKernel(m_decodeCS, m_kernelDecodeSHV2, "DecodeKeyframesSH_V2"))
                    {
                        m_disabledDueToError = true;
                        DisposeDecodeResources();
                        return false;
                    }
                }
                else
                {
                    if (!m_decodeCS.HasKernel("DecodeKeyframesSH"))
                    {
                        Debug.LogError("[Gsplat][Sequence] DecodeComputeShader 缺少必需 kernel: DecodeKeyframesSH.");
                        m_disabledDueToError = true;
                        DisposeDecodeResources();
                        return false;
                    }

                    m_kernelDecodeSH = m_decodeCS.FindKernel("DecodeKeyframesSH");
                    m_kernelDecodeSHV2 = -1;
                    m_kernelDecodeSH0 = -1;

                    if (!TryValidateDecodeKernel(m_decodeCS, m_kernelDecodeSH, "DecodeKeyframesSH"))
                    {
                        m_disabledDueToError = true;
                        DisposeDecodeResources();
                        return false;
                    }
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
                m_kernelDecodeSHV2 = -1;

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
                if (useV2)
                {
                    // v2: sh1/sh2/sh3
                    m_shNCentroidsBuffer?.Dispose();
                    m_shNCentroidsBuffer = null;

                    if (!TryCreateOrRecreateShBandCentroidsBuffer("sh1", coeffCount: 3, SequenceAsset.Sh1Count,
                            SequenceAsset.Sh1CentroidsType, SequenceAsset.Sh1CentroidsBytes, ref m_sh1CentroidsBuffer))
                        return false;

                    if (m_effectiveSHBands >= 2)
                    {
                        if (!TryCreateOrRecreateShBandCentroidsBuffer("sh2", coeffCount: 5, SequenceAsset.Sh2Count,
                                SequenceAsset.Sh2CentroidsType, SequenceAsset.Sh2CentroidsBytes, ref m_sh2CentroidsBuffer))
                            return false;
                    }
                    else
                    {
                        m_sh2CentroidsBuffer?.Dispose();
                        m_sh2CentroidsBuffer = null;
                    }

                    if (m_effectiveSHBands >= 3)
                    {
                        if (!TryCreateOrRecreateShBandCentroidsBuffer("sh3", coeffCount: 7, SequenceAsset.Sh3Count,
                                SequenceAsset.Sh3CentroidsType, SequenceAsset.Sh3CentroidsBytes, ref m_sh3CentroidsBuffer))
                            return false;
                    }
                    else
                    {
                        m_sh3CentroidsBuffer?.Dispose();
                        m_sh3CentroidsBuffer = null;
                    }
                }
                else
                {
                    // v1: 单一 shN
                    m_sh1CentroidsBuffer?.Dispose();
                    m_sh2CentroidsBuffer?.Dispose();
                    m_sh3CentroidsBuffer?.Dispose();
                    m_sh1CentroidsBuffer = null;
                    m_sh2CentroidsBuffer = null;
                    m_sh3CentroidsBuffer = null;

                    if (!TryCreateOrRecreateShNCentroidsBuffer())
                        return false;
                }
            }
            else
            {
                m_shNCentroidsBuffer?.Dispose();
                m_shNCentroidsBuffer = null;
                m_sh1CentroidsBuffer?.Dispose();
                m_sh2CentroidsBuffer?.Dispose();
                m_sh3CentroidsBuffer?.Dispose();
                m_sh1CentroidsBuffer = null;
                m_sh2CentroidsBuffer = null;
                m_sh3CentroidsBuffer = null;
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

        bool TryCreateOrRecreateShBandCentroidsBuffer(
            string bandName,
            int coeffCount,
            int count,
            string centroidsType,
            byte[] centroidsBytes,
            ref GraphicsBuffer centroidsBuffer)
        {
            var nice = char.ToUpperInvariant(bandName[0]) + bandName.Substring(1);

            if (count <= 0)
            {
                Debug.LogError($"[Gsplat][Sequence] {nice}Count 非法: {count}");
                m_disabledDueToError = true;
                return false;
            }

            if (centroidsBytes == null || centroidsBytes.Length == 0)
            {
                Debug.LogError($"[Gsplat][Sequence] SequenceAsset.{nice}CentroidsBytes 为空,无法解码 SH rest({nice}).");
                m_disabledDueToError = true;
                return false;
            }

            if (string.IsNullOrEmpty(centroidsType))
            {
                Debug.LogError($"[Gsplat][Sequence] SequenceAsset.{nice}CentroidsType 为空,无法解码 SH rest({nice}).");
                m_disabledDueToError = true;
                return false;
            }

            var scalarBytes = centroidsType == "f16" ? 2 : 4;
            var expectedBytes = (long)count * coeffCount * 3L * scalarBytes;
            if (centroidsBytes.LongLength != expectedBytes)
            {
                Debug.LogError(
                    $"[Gsplat][Sequence] {nice}CentroidsBytes 尺寸不匹配: expected {expectedBytes}, got {centroidsBytes.LongLength}. " +
                    $"(count={count}, coeffCount={coeffCount}, type={centroidsType})");
                m_disabledDueToError = true;
                return false;
            }

            var vecCount = count * coeffCount;
            if (centroidsBuffer != null && centroidsBuffer.count == vecCount)
                return true;

            // 目前为了实现简单与稳定,统一把 palette 解码成 float32 的 Vector3 buffer.
            // 后续若要进一步提升性能/显存,可以考虑在 compute 里直接支持 f16 解码.
            var decoded = new Vector3[vecCount];
            if (centroidsType == "f32")
            {
                var floats = MemoryMarshal.Cast<byte, float>(centroidsBytes.AsSpan());
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
                var halves = MemoryMarshal.Cast<byte, ushort>(centroidsBytes.AsSpan());
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
                centroidsBuffer?.Dispose();
                centroidsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vecCount, Marshal.SizeOf(typeof(Vector3)))
                {
                    name = $"Sog4D_{nice}Centroids"
                };
                centroidsBuffer.SetData(decoded);
                return true;
            }
            catch (Exception ex)
            {
                m_disabledDueToError = true;
                centroidsBuffer?.Dispose();
                centroidsBuffer = null;
                Debug.LogError(
                    $"[Gsplat][Sequence] {nice}Centroids GraphicsBuffer 创建失败,已禁用该对象的渲染. " +
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
            var assetVersion = SequenceAsset.Sog4DVersion;
            if (assetVersion == 0)
                assetVersion = 1;
            var useV2 = assetVersion == 2;

            if (m_effectiveSHBands > 0)
            {
                if (useV2)
                {
                    if (!SequenceAsset.Sh1Labels)
                    {
                        Debug.LogError("[Gsplat][Sequence] SequenceAsset.SHBands>0 但缺少 Sh1Labels,请重新导入 .sog4d.");
                        m_disabledDueToError = true;
                        return false;
                    }

                    if (m_effectiveSHBands >= 2 && !SequenceAsset.Sh2Labels)
                    {
                        Debug.LogError("[Gsplat][Sequence] SequenceAsset.SHBands>=2 但缺少 Sh2Labels,请重新导入 .sog4d.");
                        m_disabledDueToError = true;
                        return false;
                    }

                    if (m_effectiveSHBands >= 3 && !SequenceAsset.Sh3Labels)
                    {
                        Debug.LogError("[Gsplat][Sequence] SequenceAsset.SHBands>=3 但缺少 Sh3Labels,请重新导入 .sog4d.");
                        m_disabledDueToError = true;
                        return false;
                    }
                }
                else
                {
                    if (!SequenceAsset.ShNLabels)
                    {
                        Debug.LogError("[Gsplat][Sequence] SequenceAsset.SHBands>0 但缺少 ShNLabels,请重新导入 .sog4d.");
                        m_disabledDueToError = true;
                        return false;
                    }
                }
            }

            if (m_renderer == null || m_decodeCS == null)
                return false;

            // tasks 4.5: explicit time mapping 的二分查找与重复时间点处理,在 EvaluateFromTimeNormalized 内完成.
            SequenceAsset.TimeMapping.EvaluateFromTimeNormalized(SequenceAsset.FrameCount, m_timeNormalizedThisFrame,
                out var i0, out var i1, out var a);

            var useLinear = ShouldUseLinearInterpolation(m_effectiveInterpolationMode, i0, i1);
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

            // 选择 kernel:
            // - SHBands==0: DecodeKeyframesSH0
            // - SHBands>0:
            //   - v1: DecodeKeyframesSH
            //   - v2: DecodeKeyframesSH_V2
            var kernel = m_kernelDecodeSH0;
            var kernelName = "DecodeKeyframesSH0";
            if (m_effectiveSHBands > 0)
            {
                kernel = useV2 ? m_kernelDecodeSHV2 : m_kernelDecodeSH;
                kernelName = useV2 ? "DecodeKeyframesSH_V2" : "DecodeKeyframesSH";
            }

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
            m_decodeCS.SetInt("_Sh1CentroidsCount", m_sh1CentroidsBuffer != null ? m_sh1CentroidsBuffer.count : 0);
            m_decodeCS.SetInt("_Sh2CentroidsCount", m_sh2CentroidsBuffer != null ? m_sh2CentroidsBuffer.count : 0);
            m_decodeCS.SetInt("_Sh3CentroidsCount", m_sh3CentroidsBuffer != null ? m_sh3CentroidsBuffer.count : 0);

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

                if (useV2)
                {
                    m_decodeCS.SetTexture(kernel, "_Sh1Labels", SequenceAsset.Sh1Labels);
                    m_decodeCS.SetBuffer(kernel, "_Sh1Centroids", m_sh1CentroidsBuffer);

                    if (m_effectiveSHBands >= 2)
                    {
                        m_decodeCS.SetTexture(kernel, "_Sh2Labels", SequenceAsset.Sh2Labels);
                        m_decodeCS.SetBuffer(kernel, "_Sh2Centroids", m_sh2CentroidsBuffer);
                    }

                    if (m_effectiveSHBands >= 3)
                    {
                        m_decodeCS.SetTexture(kernel, "_Sh3Labels", SequenceAsset.Sh3Labels);
                        m_decodeCS.SetBuffer(kernel, "_Sh3Centroids", m_sh3CentroidsBuffer);
                    }
                }
                else
                {
                    m_decodeCS.SetTexture(kernel, "_ShNLabels", SequenceAsset.ShNLabels);
                    m_decodeCS.SetBuffer(kernel, "_ShNCentroids", m_shNCentroidsBuffer);
                }

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
                    $"kernel={kernelName}, groups={groups}, splats={SplatCount}.\n" +
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

        static bool ShouldUseLinearInterpolation(GsplatInterpolationMode interpolationMode, int frame0, int frame1)
        {
            // 单帧 bundle 或其它退化情形下,只要最终命中的还是同一帧,
            // 就必须退化为固定帧读取,不能再假设“存在独立第二帧可插值”.
            return interpolationMode == GsplatInterpolationMode.Linear && frame0 != frame1;
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
            DestroyRuntimeTexture2DArray(ref SequenceAsset.Sh1Labels);
            DestroyRuntimeTexture2DArray(ref SequenceAsset.Sh2Labels);
            DestroyRuntimeTexture2DArray(ref SequenceAsset.Sh3Labels);

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
