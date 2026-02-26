// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Serialization;

namespace Gsplat
{
    /// <summary>
    /// 显隐燃烧环动画: 噪声类型.
    /// - 主要用于控制 show/hide 期间的 position warp(空间扭曲)噪声场.
    /// - 同时也可作为“旧效果/新效果”的对照开关,便于调参时快速比对观感.
    /// </summary>
    public enum GsplatVisibilityNoiseMode
    {
        /// <summary>
        /// 默认: value noise + 轻量 domain warp.
        /// - 空间上更连续,更像“烟雾的扭曲与波动”.
        /// - 开销中等,适合常用.
        /// </summary>
        ValueSmoke = 0,

        /// <summary>
        /// 新增: curl-like 旋涡噪声场(基于 value noise 的梯度/旋度构造).
        /// - 更像连续的“空间流动”,更容易形成旋涡感.
        /// - 开销更高,建议只在 show/hide 动画期间使用(本效果默认就是).
        /// </summary>
        CurlSmoke = 1,

        /// <summary>
        /// 旧版对照: hash noise(更碎更抖).
        /// - 主要用于和更平滑的 noise 做对照,或用于调试/性能基线.
        /// </summary>
        HashLegacy = 2,
    }

    [ExecuteAlways]
    public class GsplatRenderer : MonoBehaviour, IGsplat, IGsplatRenderSubmitter
    {
        public GsplatAsset GsplatAsset;
        [Range(0, 3)] public int SHDegree = 3;
        public bool GammaToLinear;
        public bool AsyncUpload;
        [Tooltip("是否启用 Gsplat 主后端(Compute 排序 + Gsplat.shader)渲染. 仅使用 VFX Graph 后端时可关闭,避免双重渲染与排序开销.")]
        public bool EnableGsplatBackend = true;
        [Range(0, 1)] public float TimeNormalized;
        public bool AutoPlay;
        public float Speed = 1.0f;
        public bool Loop = true;

        // --------------------------------------------------------------------
        // 可选: 显隐燃烧环动画(show/hide)
        // - 默认关闭,不影响旧行为与性能.
        // - 该动画用于“初始隐藏 -> 燃烧环扩散显示”和“中心起燃 -> 燃烧成灰消失”.
        // --------------------------------------------------------------------
        [Header("Visibility Animation (Burn Reveal)")]
        [Tooltip("是否启用显隐燃烧环动画. 默认关闭,不影响旧行为与性能.")]
        public bool EnableVisibilityAnimation;

        [Tooltip("显隐动画中心.\n" +
                 "- 不为空: 以该 Transform 为中心.\n" +
                 "- 为空: 回退使用点云 bounds.center.")]
        public Transform VisibilityCenter;

        [Tooltip("启用组件时是否自动播放 show 动画(从隐藏->显示).\n" +
                 "仅在 EnableVisibilityAnimation=true 时生效.")]
        public bool PlayShowOnEnable = true;

        [Min(0.0f)]
        [Tooltip("show 动画时长(秒).")]
        public float ShowDuration = 1.0f;

        [Min(0.0f)]
        [Tooltip("hide 动画时长(秒).")]
        public float HideDuration = 1.2f;

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
        // 显隐燃烧环动画 runtime 状态(非序列化):
        // - Hidden: 完全不可见,并且 `Valid=false` 用于从根源停止 sorter gather 与 draw 提交.
        // - Showing/Hiding: 动画进行中,仍然保持 `Valid=true` 以允许正常排序与渲染.
        // --------------------------------------------------------------------
        enum VisibilityAnimState
        {
            Visible = 0,
            Hidden = 1,
            Showing = 2,
            Hiding = 3,
        }

        VisibilityAnimState m_visibilityState = VisibilityAnimState.Visible;
        float m_visibilityProgress01 = 1.0f;
        float m_visibilityLastAdvanceRealtime = -1.0f;

#if UNITY_EDITOR
        // --------------------------------------------------------------------
        // Editor 体验修复: 让 show/hide 动画在“鼠标不动”时也能连续播放.
        //
        // 背景:
        // - EditMode 下,SceneView/GameView 往往是“事件驱动 repaint”.
        // - 当用户鼠标不动时,视口不会持续 repaint,体感像“动画不播放/卡住”.
        // - 我们的显隐动画是 shader/uniform 驱动,必须有 repaint 才能看到连续帧.
        //
        // 方案:
        // - 仅在 Showing/Hiding 期间,主动请求 PlayerLoop + Repaint.
        // - 动画结束时再补 1 次强制刷新,避免停在“最后一帧之前”的错觉.
        // - batchmode/tests 环境下不触发 repaint,避免无意义调用与潜在不稳定.
        // --------------------------------------------------------------------
        double m_visibilityEditorLastRepaintTime = -1.0;
        double m_visibilityEditorLastDiagTickTime = -1.0;

        // --------------------------------------------------------------------
        // Editor update ticker(关键补强):
        // - 用户反馈: 仅靠在 Update/相机回调里请求 repaint,某些情况下仍会出现“只刷一次就停了”.
        // - 这是典型的“鸡生蛋”问题:
        //   - 没有 repaint -> 相机回调不跑,用户看不到动画.
        //   - 没有 Update/回调 -> 我们也不再继续请求 repaint,动画就卡住.
        //
        // 因此这里增加一个 EditorApplication.update 驱动的 ticker:
        // - 只在 Showing/Hiding 期间注册.
        // - 每个 Editor update tick 主动推进状态机(Advance)并触发 repaint.
        // - 动画结束后自动注销,避免空闲耗电.
        // --------------------------------------------------------------------
        static readonly HashSet<GsplatRenderer> s_visibilityEditorTickers = new();
        static readonly List<GsplatRenderer> s_visibilityEditorTickersToRemove = new();
        static bool s_visibilityEditorUpdateHooked;
        static double s_visibilityEditorLastTickTime;

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
            // PlayMode 或 batchmode 下不做任何事,并清理挂钩,避免残留开销.
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

            // 没有任何对象在播放显隐动画时,自动注销 update hook.
            if (s_visibilityEditorTickers.Count == 0)
            {
                UnhookVisibilityEditorUpdateIfIdle();
                return;
            }

            // 全局节流: 即便 EditorApplication.update 很频繁,也最多按 60fps 推进一次.
            const double kMinInterval = 1.0 / 60.0;
            var now = UnityEditor.EditorApplication.timeSinceStartup;
            if (now - s_visibilityEditorLastTickTime < kMinInterval)
                return;
            s_visibilityEditorLastTickTime = now;

            s_visibilityEditorTickersToRemove.Clear();
            foreach (var r in s_visibilityEditorTickers)
            {
                // 域重载/销毁/禁用时做清理.
                if (!r || !r.isActiveAndEnabled || !r.EnableVisibilityAnimation)
                {
                    s_visibilityEditorTickersToRemove.Add(r);
                    continue;
                }

                // 只在 Showing/Hiding 期间 tick,其它状态不占用 update.
                if (r.m_visibilityState != VisibilityAnimState.Showing &&
                    r.m_visibilityState != VisibilityAnimState.Hiding)
                {
                    s_visibilityEditorTickersToRemove.Add(r);
                    continue;
                }

                // 推进显隐状态机:
                // - 该函数内部会根据状态调用 RequestEditorRepaintForVisibilityAnimation().
                r.AdvanceVisibilityStateIfNeeded();

                // 诊断(可选):
                // - 只有在 EnableEditorDiagnostics=true 时才记录到 ring buffer,避免默认刷屏.
                // - 这里做额外节流,避免每帧都记一条导致 ring buffer 很快被淹没.
                if (GsplatEditorDiagnostics.Enabled)
                {
                    const double kDiagInterval = 0.25;
                    if (r.m_visibilityEditorLastDiagTickTime < 0.0 ||
                        now - r.m_visibilityEditorLastDiagTickTime >= kDiagInterval)
                    {
                        r.m_visibilityEditorLastDiagTickTime = now;
                        GsplatEditorDiagnostics.MarkVisibilityState(r, "ticker.tick",
                            r.m_visibilityState.ToString(), r.m_visibilityProgress01);
                    }
                }

                // 如果 tick 一次后就结束了(例如 duration=0),下一帧移除即可.
                if (r.m_visibilityState != VisibilityAnimState.Showing &&
                    r.m_visibilityState != VisibilityAnimState.Hiding)
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

            if (m_visibilityState != VisibilityAnimState.Showing && m_visibilityState != VisibilityAnimState.Hiding)
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

            // batchmode 下没有可视窗口,RepaintAllViews 没意义.
            // 同时避免在 CI/命令行 tests 环境引入潜在的不稳定因素.
            if (Application.isBatchMode)
                return;

            // 轻量节流: 避免 Editor update 频率过高时刷屏/耗电.
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

        [Tooltip("Max splat count to be uploaded per frame")]
        public uint UploadBatchSize = 100000;

        public bool RenderBeforeUploadComplete = true;

        GsplatAsset m_prevAsset;
        GsplatRendererImpl m_renderer;

        // --------------------------------------------------------------------
        // `.splat4d v2` SH delta-v1 runtime state(可选)
        // - 当 asset 未提供 delta 字段,或 compute 不可用时,这些资源为 null.
        // - 设计目标: 在 TimeNormalized 播放时,按 targetFrame 应用 label updates,
        //   并用 compute scatter 更新 SHBuffer.
        // --------------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        struct ShDeltaUpdate
        {
            public uint splatId;
            public uint label;
        }

        sealed class ShDeltaSegmentRuntime
        {
            public int StartFrame;
            public int FrameCount;
            public int LabelCount;
            public byte[] BaseLabelsBytes; // u16 little-endian
            public byte[] DeltaBytes; // delta-v1 header+body
            public int[] BlockOffsets; // length = FrameCount-1, points to updateCount within DeltaBytes
        }

        bool m_shDeltaDisabled;
        bool m_shDeltaInitialized;
        int m_shDeltaFrameCount;
        int m_shDeltaCurrentFrame;
        int m_shDeltaCurrentSegmentIndex;

        ComputeShader m_shDeltaCS;
        int m_kernelApplySh1 = -1;
        int m_kernelApplySh2 = -1;
        int m_kernelApplySh3 = -1;

        GraphicsBuffer m_shDeltaUpdatesBuffer;
        int m_shDeltaUpdatesCapacity;
        ShDeltaUpdate[] m_shDeltaUpdatesScratch;

        GraphicsBuffer m_sh1CentroidsBuffer;
        GraphicsBuffer m_sh2CentroidsBuffer;
        GraphicsBuffer m_sh3CentroidsBuffer;

        ShDeltaSegmentRuntime[] m_sh1Segments;
        ShDeltaSegmentRuntime[] m_sh2Segments;
        ShDeltaSegmentRuntime[] m_sh3Segments;

        ushort[] m_sh1Labels;
        ushort[] m_sh2Labels;
        ushort[] m_sh3Labels;
        ushort[] m_shLabelsScratch;

        public bool Valid =>
            EnableGsplatBackend &&
            m_visibilityState != VisibilityAnimState.Hidden &&
            !m_disabledDueToError &&
            GsplatAsset &&
            (RenderBeforeUploadComplete ? SplatCount > 0 : SplatCount == m_effectiveSplatCount);

        public uint SplatCount => GsplatAsset ? m_effectiveSplatCount - m_pendingSplatCount : 0;
        uint IGsplat.SplatCount => m_sortSplatCountThisFrame;
        uint IGsplat.SplatBaseIndex => m_sortSplatBaseIndexThisFrame;
        public ISorterResource SorterResource => m_renderer.SorterResource;
        public bool Has4D => m_renderer != null && m_renderer.Has4D;
        bool IGsplat.Has4D => m_renderer != null && m_renderer.Has4D;
        float IGsplat.TimeNormalized => m_timeNormalizedThisFrame;
        int IGsplat.TimeModel => GetEffectiveTimeModel();
        float IGsplat.TemporalCutoff => GetEffectiveTemporalCutoff();
        GraphicsBuffer IGsplat.VelocityBuffer => m_renderer != null ? m_renderer.VelocityBuffer : null;
        GraphicsBuffer IGsplat.TimeBuffer => m_renderer != null ? m_renderer.TimeBuffer : null;
        GraphicsBuffer IGsplat.DurationBuffer => m_renderer != null ? m_renderer.DurationBuffer : null;

        void IGsplatRenderSubmitter.SubmitDrawForCamera(Camera camera)
        {
            // 说明:
            // - 该回调由 `GsplatSorter` 在 Editor 相机渲染回调(beginCameraRendering)中触发.
            // - 目的: 解决 Editor 下“同一帧多次渲染,但 Update 只提交一次 draw”导致的闪烁.
            // - Play 模式仍走 Update 提交 draw,避免重复渲染.
            if (Application.isPlaying)
                return;

            // 在 EditMode 下,Update 与相机回调的调用时序可能不稳定.
            // 这里在相机回调入口也推进一次显隐动画状态,避免动画“卡住”.
            AdvanceVisibilityStateIfNeeded();

            if (!Valid || m_renderer == null || !m_renderer.Valid || !GsplatAsset)
                return;

            // 确保本次 draw 使用本帧最新的显隐 uniforms(避免 Update/CameraCallback 行为漂移).
            PushVisibilityUniformsForThisFrame(GsplatAsset.Bounds);

            var motionPadding = 0.0f;
            if (m_renderer.Has4D)
            {
                motionPadding = GsplatAsset.MaxSpeed * GsplatAsset.MaxDuration;
                if (motionPadding < 0.0f || float.IsNaN(motionPadding) || float.IsInfinity(motionPadding))
                    motionPadding = 0.0f;
            }

            var boundsForRender = CalcVisibilityExpandedRenderBounds(GsplatAsset.Bounds);
            m_renderer.RenderForCamera(camera, m_sortSplatCountThisFrame, transform, boundsForRender,
                gameObject.layer, GammaToLinear, SHDegree, m_timeNormalizedThisFrame, motionPadding,
                timeModel: GetEffectiveTimeModel(), temporalCutoff: GetEffectiveTemporalCutoff(),
                diagTag: "EditMode.CameraCallback",
                splatBaseIndex: m_sortSplatBaseIndexThisFrame);
        }

        // 公开 GPU buffers,用于可选的 VFX Graph 后端绑定等场景.
        public GraphicsBuffer PositionBuffer => m_renderer != null ? m_renderer.PositionBuffer : null;
        public GraphicsBuffer ScaleBuffer => m_renderer != null ? m_renderer.ScaleBuffer : null;
        public GraphicsBuffer RotationBuffer => m_renderer != null ? m_renderer.RotationBuffer : null;
        public GraphicsBuffer ColorBuffer => m_renderer != null ? m_renderer.ColorBuffer : null;
        public GraphicsBuffer SHBuffer => m_renderer != null ? m_renderer.SHBuffer : null;
        public GraphicsBuffer VelocityBuffer => m_renderer != null ? m_renderer.VelocityBuffer : null;
        public GraphicsBuffer TimeBuffer => m_renderer != null ? m_renderer.TimeBuffer : null;
        public GraphicsBuffer DurationBuffer => m_renderer != null ? m_renderer.DurationBuffer : null;
        public byte EffectiveSHBands => m_renderer != null ? m_renderer.SHBands : (byte)0;

        uint m_pendingSplatCount;
        float m_timeNormalizedThisFrame;
        uint m_sortSplatBaseIndexThisFrame;
        uint m_sortSplatCountThisFrame;
        uint m_effectiveSplatCount;
        byte m_effectiveSHBands;
        bool m_effectiveHas4D;
        bool m_disabledDueToError;
        float m_nextRendererRecoveryTime;

        // --------------------------------------------------------------------
        // keyframe `.splat4d(window)` segment 优化(可选):
        // - 典型 keyframe 资产会把多个时间段(segment)的 records 依次追加到同一个 arrays/buffers 中.
        // - 同一时刻往往只有一个 segment 可见.
        // - 若我们仍对全量 records 做 radix sort,成本会按 segment 数线性膨胀,PlayMode 很容易卡成 PPT.
        //
        // 本优化的目标:
        // - 检测出 "time/duration 常量 + segments 不重叠" 的 keyframe 形态.
        // - 播放时仅对当前 segment 的子范围 [baseIndex, baseIndex+count) 做 sort+draw.
        // - 不满足条件时保持旧行为(全量排序 + shader 硬裁剪).
        // --------------------------------------------------------------------
        struct TimeSegment
        {
            public uint BaseIndex;
            public uint Count;
            public float Time0;
            public float Duration;
            public float End;
        }

        bool m_timeSegmentsEnabled;
        TimeSegment[] m_timeSegments;

        static bool Has4DFields(GsplatAsset asset)
        {
            // 4D 数组只要有任意一个缺失,就视为 3D-only 资产,避免运行期出现数组越界或未绑定 buffer.
            return asset != null &&
                   asset.Velocities != null &&
                   asset.Times != null &&
                   asset.Durations != null;
        }

        int GetEffectiveTimeModel()
        {
            // 兼容旧资产:
            // - 旧版本没有 TimeModel 字段时,默认值可能为 0.
            // - 我们把 0 视为 window,以保持旧行为不变.
            var m = (int)(GsplatAsset ? GsplatAsset.TimeModel : (byte)0);
            return m == 2 ? 2 : 1;
        }

        float GetEffectiveTemporalCutoff()
        {
            var c = GsplatAsset ? GsplatAsset.TemporalGaussianCutoff : 0.0f;
            if (float.IsNaN(c) || float.IsInfinity(c) || c <= 0.0f || c >= 1.0f)
                return 0.01f;
            return c;
        }

        static int BandCoeffCount(int band) => band switch
        {
            1 => 3,
            2 => 5,
            3 => 7,
            _ => 0
        };

        static int BandCoeffOffset(int band) => band switch
        {
            1 => 0,
            2 => 3,
            3 => 8,
            _ => 0
        };

        static int DivRoundUp(int x, int d) => (x + d - 1) / d;

        static int FloatBits(float v)
        {
            unsafe
            {
                return *(int*)&v;
            }
        }

        void DisposeShDeltaResources()
        {
            m_shDeltaUpdatesBuffer?.Dispose();
            m_shDeltaUpdatesBuffer = null;
            m_shDeltaUpdatesCapacity = 0;
            m_shDeltaUpdatesScratch = null;

            m_sh1CentroidsBuffer?.Dispose();
            m_sh2CentroidsBuffer?.Dispose();
            m_sh3CentroidsBuffer?.Dispose();
            m_sh1CentroidsBuffer = null;
            m_sh2CentroidsBuffer = null;
            m_sh3CentroidsBuffer = null;

            m_sh1Segments = null;
            m_sh2Segments = null;
            m_sh3Segments = null;

            m_sh1Labels = null;
            m_sh2Labels = null;
            m_sh3Labels = null;
            m_shLabelsScratch = null;

            m_shDeltaCS = null;
            m_kernelApplySh1 = -1;
            m_kernelApplySh2 = -1;
            m_kernelApplySh3 = -1;

            m_shDeltaInitialized = false;
            m_shDeltaDisabled = false;
            m_shDeltaFrameCount = 0;
            m_shDeltaCurrentFrame = 0;
            m_shDeltaCurrentSegmentIndex = 0;
        }

        void EnsureShDeltaUpdatesCapacity(int required)
        {
            required = Math.Max(required, 1);
            if (m_shDeltaUpdatesBuffer != null &&
                m_shDeltaUpdatesCapacity >= required &&
                m_shDeltaUpdatesScratch != null &&
                m_shDeltaUpdatesScratch.Length >= required)
            {
                return;
            }

            // 说明:
            // - updates 可能在 segment 边界处变大(需要 diff 到 base labels).
            // - 这里用 NextPowerOfTwo 减少反复扩容次数.
            var cap = Mathf.NextPowerOfTwo(required);
            if (cap < required)
                cap = required; // 极端情况下 NextPowerOfTwo 溢出时兜底

            m_shDeltaUpdatesBuffer?.Dispose();
            m_shDeltaUpdatesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 8);
            m_shDeltaUpdatesCapacity = cap;
            m_shDeltaUpdatesScratch = new ShDeltaUpdate[cap];
        }

        static bool TryValidateKernel(ComputeShader cs, int kernel, string kernelName, out string error)
        {
            error = null;
            if (!cs)
            {
                error = "ComputeShader is null";
                return false;
            }

            try
            {
                if (!cs.IsSupported(kernel))
                {
                    error = $"kernel not supported: {kernelName}";
                    return false;
                }
            }
            catch (Exception e)
            {
                error = $"kernel validation threw: {kernelName}: {e.Message}";
                return false;
            }

            return true;
        }

        static bool TryBuildSegmentRuntimes(
            Splat4DShDeltaSegment[] segments,
            int effectiveSplatCount,
            out ShDeltaSegmentRuntime[] runtimes,
            out string error)
        {
            runtimes = null;
            error = null;

            if (segments == null || segments.Length == 0)
            {
                error = "segments missing";
                return false;
            }

            var expectedLabelCount = -1;
            var outArr = new ShDeltaSegmentRuntime[segments.Length];
            for (var i = 0; i < segments.Length; i++)
            {
                var s = segments[i];
                if (s == null)
                {
                    error = $"segment[{i}] is null";
                    return false;
                }

                if (s.FrameCount <= 0)
                {
                    error = $"segment[{i}] invalid FrameCount={s.FrameCount}";
                    return false;
                }

                if (s.BaseLabelsBytes == null || s.BaseLabelsBytes.Length < effectiveSplatCount * 2)
                {
                    error = $"segment[{i}] base labels bytes too small: {s.BaseLabelsBytes?.Length ?? 0}";
                    return false;
                }

                if (s.DeltaBytes == null || s.DeltaBytes.Length < 28)
                {
                    error = $"segment[{i}] delta bytes too small: {s.DeltaBytes?.Length ?? 0}";
                    return false;
                }

                var span = s.DeltaBytes.AsSpan();
                // delta-v1 header: magic(8) + version/start/count/splatCount/labelCount (5*u32)
                var version = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8, 4));
                var segStart = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12, 4));
                var segCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16, 4));
                var splatCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(20, 4));
                var labelCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(24, 4));
                if (version != 1 || segStart != s.StartFrame || segCount != s.FrameCount)
                {
                    error = $"segment[{i}] delta header mismatch: v={version} start={segStart} count={segCount}";
                    return false;
                }

                if (splatCount < effectiveSplatCount)
                {
                    error = $"segment[{i}] delta splatCount too small: {splatCount} < effective {effectiveSplatCount}";
                    return false;
                }
                if (labelCount <= 0)
                {
                    error = $"segment[{i}] delta labelCount invalid: {labelCount}";
                    return false;
                }
                if (expectedLabelCount < 0)
                    expectedLabelCount = labelCount;
                else if (expectedLabelCount != labelCount)
                {
                    error = $"segment[{i}] delta labelCount mismatch: {labelCount} != expected {expectedLabelCount}";
                    return false;
                }

                var blockCount = s.FrameCount - 1;
                var offsets = new int[Math.Max(blockCount, 0)];
                var p = 28;
                for (var b = 0; b < blockCount; b++)
                {
                    if (p + 4 > span.Length)
                    {
                        error = $"segment[{i}] delta truncated while building offsets";
                        return false;
                    }

                    offsets[b] = p;
                    var updateCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(p, 4));
                    p += 4;

                    var need = (long)updateCount * 8L;
                    if (need < 0 || p + need > span.Length)
                    {
                        error = $"segment[{i}] delta updates payload out of range: updateCount={updateCount}";
                        return false;
                    }

                    p += (int)need;
                }

                if (p != span.Length)
                {
                    error = $"segment[{i}] delta has trailing bytes: parsed={p} total={span.Length}";
                    return false;
                }

                outArr[i] = new ShDeltaSegmentRuntime
                {
                    StartFrame = s.StartFrame,
                    FrameCount = s.FrameCount,
                    LabelCount = labelCount,
                    BaseLabelsBytes = s.BaseLabelsBytes,
                    DeltaBytes = s.DeltaBytes,
                    BlockOffsets = offsets
                };
            }

            runtimes = outArr;
            return true;
        }

        static int FindSegmentIndex(ShDeltaSegmentRuntime[] segments, int frame)
        {
            if (segments == null)
                return -1;

            for (var i = 0; i < segments.Length; i++)
            {
                var s = segments[i];
                var start = s.StartFrame;
                var endExcl = start + s.FrameCount;
                if (frame >= start && frame < endExcl)
                    return i;
            }

            return -1;
        }

        static int ReadUpdateBlock(
            ShDeltaSegmentRuntime seg,
            int relFrame,
            int effectiveSplatCount,
            int maxLabelExclusive,
            ShDeltaUpdate[] outUpdates)
        {
            // relFrame: segment 内相对帧索引. startFrame 本身是 rel=0,没有 delta block.
            if (relFrame <= 0)
                return 0;

            var span = seg.DeltaBytes.AsSpan();
            var blockOffset = seg.BlockOffsets[relFrame - 1];
            var updateCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(blockOffset, 4));
            var p = blockOffset + 4;

            var wrote = 0;
            var hasLast = false;
            uint lastSplatId = 0;
            for (var i = 0; i < updateCount; i++)
            {
                var splatId = (uint)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(p, 4));
                p += 4;
                var newLabel = (uint)BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(p, 2));
                p += 2;
                var reserved = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(p, 2));
                p += 2;

                // reserved 必须为 0. 若遇到非法数据,直接抛异常让上层禁用动态 SH.
                if (reserved != 0)
                    throw new InvalidDataException("delta-v1 reserved field must be 0");

                // 约束: 同一帧 block 内 splatId 必须严格递增(与 spec 对齐).
                if (hasLast && splatId <= lastSplatId)
                    throw new InvalidDataException("delta-v1 splatId must be strictly increasing within a frame");
                hasLast = true;
                lastSplatId = splatId;

                if (newLabel >= (uint)maxLabelExclusive)
                    throw new InvalidDataException("delta-v1 label out of range");

                if (splatId >= (uint)effectiveSplatCount)
                    continue; // 被 CapSplatCount 截断的部分直接忽略,避免 OOB.

                if (wrote >= outUpdates.Length)
                    throw new InvalidDataException("delta-v1 updateCount exceeds scratch capacity");
                outUpdates[wrote++] = new ShDeltaUpdate { splatId = splatId, label = newLabel };
            }

            return wrote;
        }

        static void DecodeLabelsAtFrame(
            ShDeltaSegmentRuntime seg,
            int targetFrame,
            int effectiveSplatCount,
            int maxLabelExclusive,
            ushort[] outLabels)
        {
            // 1) base labels(段起始帧的绝对状态)
            Buffer.BlockCopy(seg.BaseLabelsBytes, 0, outLabels, 0, effectiveSplatCount * 2);

            // 2) 逐帧应用 delta blocks,直到 targetFrame
            var rel = targetFrame - seg.StartFrame;
            if (rel <= 0)
                return;

            var span = seg.DeltaBytes.AsSpan();
            for (var r = 1; r <= rel; r++)
            {
                var blockOffset = seg.BlockOffsets[r - 1];
                var updateCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(blockOffset, 4));
                var p = blockOffset + 4;
                var hasLast = false;
                uint lastSplatId = 0;
                for (var i = 0; i < updateCount; i++)
                {
                    var splatId = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(p, 4));
                    p += 4;
                    var newLabel = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(p, 2));
                    p += 2;
                    var reserved = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(p, 2));
                    p += 2;
                    if (reserved != 0)
                        throw new InvalidDataException("delta-v1 reserved field must be 0");

                    if (hasLast && (uint)splatId <= lastSplatId)
                        throw new InvalidDataException("delta-v1 splatId must be strictly increasing within a frame");
                    hasLast = true;
                    lastSplatId = (uint)splatId;

                    if (newLabel >= maxLabelExclusive)
                        throw new InvalidDataException("delta-v1 label out of range");

                    if (splatId < 0 || splatId >= effectiveSplatCount)
                        continue;
                    outLabels[splatId] = newLabel;
                }
            }
        }

        static bool TryValidateSegmentsAligned(
            ShDeltaSegmentRuntime[] baseSegments,
            ShDeltaSegmentRuntime[] otherSegments,
            int band,
            out string error)
        {
            error = null;
            if (baseSegments == null || baseSegments.Length == 0)
            {
                error = "base segments missing";
                return false;
            }

            if (otherSegments == null || otherSegments.Length != baseSegments.Length)
            {
                error =
                    $"band={band} segments length mismatch: {otherSegments?.Length ?? 0} != {baseSegments.Length}";
                return false;
            }

            for (var i = 0; i < baseSegments.Length; i++)
            {
                var a = baseSegments[i];
                var b = otherSegments[i];
                if (a.StartFrame != b.StartFrame || a.FrameCount != b.FrameCount)
                {
                    error =
                        $"band={band} segments not aligned at index={i}: " +
                        $"(start,count)=({b.StartFrame},{b.FrameCount}) != base({a.StartFrame},{a.FrameCount})";
                    return false;
                }
            }

            return true;
        }

        static bool TryValidateBaseLabelsRange(
            ShDeltaSegmentRuntime[] segments,
            int effectiveSplatCount,
            int band,
            out string error)
        {
            error = null;
            if (segments == null || segments.Length == 0)
            {
                error = $"band={band} segments missing";
                return false;
            }

            var maxLabelExclusive = segments[0].LabelCount;
            if (maxLabelExclusive <= 0)
            {
                error = $"band={band} invalid labelCount={maxLabelExclusive}";
                return false;
            }

            // 说明:
            // - importer 只会解码 startFrame=0 的 base labels.
            // - 其它 segment 的 base labels 仅做 header 校验,不做 label range 校验.
            // - runtime 需要保证“任意 seek 到 segment base”都不会把越界 label 喂给 GPU.
            //
            // 这里选择在 init 时一次性校验所有 segments 的 base labels,换取运行期更安全.
            for (var s = 0; s < segments.Length; s++)
            {
                var seg = segments[s];
                var byteLen = effectiveSplatCount * 2;
                if (seg.BaseLabelsBytes == null || seg.BaseLabelsBytes.Length < byteLen)
                {
                    error = $"band={band} segment[{s}] base labels too small";
                    return false;
                }

                if (BitConverter.IsLittleEndian)
                {
                    var labels = MemoryMarshal.Cast<byte, ushort>(seg.BaseLabelsBytes.AsSpan(0, byteLen));
                    for (var i = 0; i < effectiveSplatCount; i++)
                    {
                        if (labels[i] >= maxLabelExclusive)
                        {
                            error =
                                $"band={band} segment[{s}] base label out of range: " +
                                $"splatId={i} label={labels[i]} >= {maxLabelExclusive}";
                            return false;
                        }
                    }
                }
                else
                {
                    var span = seg.BaseLabelsBytes.AsSpan(0, byteLen);
                    for (var i = 0; i < effectiveSplatCount; i++)
                    {
                        var label = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(i * 2, 2));
                        if (label >= maxLabelExclusive)
                        {
                            error =
                                $"band={band} segment[{s}] base label out of range: " +
                                $"splatId={i} label={label} >= {maxLabelExclusive}";
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        bool TryDispatchShDeltaUpdates(int band, GraphicsBuffer centroids, int kernel, ShDeltaUpdate[] updates, int updateCount)
        {
            if (updateCount <= 0)
                return true;
            // 注意: GraphicsBuffer 不是 UnityEngine.Object,不能用 `!buffer` 这种写法做空值判断.
            if (!m_shDeltaCS || kernel < 0 || centroids == null || m_renderer == null || m_renderer.SHBuffer == null)
                return false;

            EnsureShDeltaUpdatesCapacity(updateCount);
            m_shDeltaUpdatesBuffer.SetData(updates, 0, 0, updateCount);

            var restCoeffCountTotal = GsplatUtils.SHBandsToCoefficientCount(m_renderer.SHBands);
            var coeffCount = BandCoeffCount(band);
            var coeffOffset = BandCoeffOffset(band);

            m_shDeltaCS.SetInt("_UpdateCount", updateCount);
            m_shDeltaCS.SetInt("_RestCoeffCountTotal", restCoeffCountTotal);
            m_shDeltaCS.SetInt("_BandCoeffOffset", coeffOffset);
            m_shDeltaCS.SetInt("_BandCoeffCount", coeffCount);
            m_shDeltaCS.SetBuffer(kernel, "_Updates", m_shDeltaUpdatesBuffer);
            m_shDeltaCS.SetBuffer(kernel, "_Centroids", centroids);
            m_shDeltaCS.SetBuffer(kernel, "_SHBuffer", m_renderer.SHBuffer);

            var groups = DivRoundUp(updateCount, 256);
            m_shDeltaCS.Dispatch(kernel, groups, 1, 1);
            return true;
        }

        void TryInitShDeltaRuntime()
        {
            DisposeShDeltaResources();

            if (!GsplatAsset || m_renderer == null)
                return;
            if (m_renderer.SHBands <= 0)
                return;

            // delta 数据必须存在且 frameCount 有意义.
            if (GsplatAsset.ShFrameCount <= 0 || GsplatAsset.Sh1DeltaSegments == null || GsplatAsset.Sh1DeltaSegments.Length == 0)
                return;

            if (!SystemInfo.supportsComputeShaders)
            {
                Debug.LogWarning("[Gsplat] 当前平台不支持 ComputeShader,将禁用 `.splat4d` 动态 SH(delta-v1).");
                m_shDeltaDisabled = true;
                return;
            }

            var settings = GsplatSettings.Instance;
            if (!settings || !settings.ShDeltaComputeShader)
            {
                Debug.LogWarning("[Gsplat] 缺少 ShDeltaComputeShader,将禁用 `.splat4d` 动态 SH(delta-v1).");
                m_shDeltaDisabled = true;
                return;
            }

            m_shDeltaCS = settings.ShDeltaComputeShader;
            try
            {
                m_kernelApplySh1 = m_shDeltaCS.FindKernel("ApplySh1Updates");
                m_kernelApplySh2 = m_shDeltaCS.FindKernel("ApplySh2Updates");
                m_kernelApplySh3 = m_shDeltaCS.FindKernel("ApplySh3Updates");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Gsplat] ShDeltaComputeShader 缺少必需 kernel,将禁用动态 SH: {e.Message}");
                m_shDeltaDisabled = true;
                return;
            }

            if (!TryValidateKernel(m_shDeltaCS, m_kernelApplySh1, "ApplySh1Updates", out var kernelError))
            {
                Debug.LogWarning($"[Gsplat] ShDeltaComputeShader kernel 无效,将禁用动态 SH: {kernelError}");
                m_shDeltaDisabled = true;
                return;
            }

            // band2/band3 的 kernel 是否可用,按需校验(只要 effectiveSHBands 用不到,就不强制要求).
            if (m_renderer.SHBands >= 2 &&
                !TryValidateKernel(m_shDeltaCS, m_kernelApplySh2, "ApplySh2Updates", out kernelError))
            {
                Debug.LogWarning($"[Gsplat] ShDeltaComputeShader kernel 无效,将禁用动态 SH: {kernelError}");
                m_shDeltaDisabled = true;
                return;
            }
            if (m_renderer.SHBands >= 3 &&
                !TryValidateKernel(m_shDeltaCS, m_kernelApplySh3, "ApplySh3Updates", out kernelError))
            {
                Debug.LogWarning($"[Gsplat] ShDeltaComputeShader kernel 无效,将禁用动态 SH: {kernelError}");
                m_shDeltaDisabled = true;
                return;
            }

            var effectiveSplatCount = checked((int)m_effectiveSplatCount);

            // 1) segments runtime
            if (!TryBuildSegmentRuntimes(GsplatAsset.Sh1DeltaSegments, effectiveSplatCount, out m_sh1Segments, out var err))
            {
                Debug.LogWarning($"[Gsplat] SH delta segments 无效(band=1),将禁用动态 SH: {err}");
                m_shDeltaDisabled = true;
                return;
            }
            if (m_renderer.SHBands >= 2)
            {
                if (!TryBuildSegmentRuntimes(GsplatAsset.Sh2DeltaSegments, effectiveSplatCount, out m_sh2Segments, out err))
                {
                    Debug.LogWarning($"[Gsplat] SH delta segments 无效(band=2),将禁用动态 SH: {err}");
                    m_shDeltaDisabled = true;
                    return;
                }
            }
            if (m_renderer.SHBands >= 3)
            {
                if (!TryBuildSegmentRuntimes(GsplatAsset.Sh3DeltaSegments, effectiveSplatCount, out m_sh3Segments, out err))
                {
                    Debug.LogWarning($"[Gsplat] SH delta segments 无效(band=3),将禁用动态 SH: {err}");
                    m_shDeltaDisabled = true;
                    return;
                }
            }

            // 1.1) 多 band 时要求 segments 对齐(同一 index 对应同一 [start,count]).
            // - 这能简化运行期的 “frame -> segment” 映射,并避免 band 间状态不一致.
            if (m_renderer.SHBands >= 2 &&
                !TryValidateSegmentsAligned(m_sh1Segments, m_sh2Segments, 2, out err))
            {
                Debug.LogWarning($"[Gsplat] SH delta segments 不对齐,将禁用动态 SH: {err}");
                m_shDeltaDisabled = true;
                return;
            }
            if (m_renderer.SHBands >= 3 &&
                !TryValidateSegmentsAligned(m_sh1Segments, m_sh3Segments, 3, out err))
            {
                Debug.LogWarning($"[Gsplat] SH delta segments 不对齐,将禁用动态 SH: {err}");
                m_shDeltaDisabled = true;
                return;
            }

            // 1.2) 校验所有 segments 的 base labels 范围(避免 seek 时 GPU 越界).
            if (!TryValidateBaseLabelsRange(m_sh1Segments, effectiveSplatCount, 1, out err) ||
                (m_renderer.SHBands >= 2 && !TryValidateBaseLabelsRange(m_sh2Segments, effectiveSplatCount, 2, out err)) ||
                (m_renderer.SHBands >= 3 && !TryValidateBaseLabelsRange(m_sh3Segments, effectiveSplatCount, 3, out err)))
            {
                Debug.LogWarning($"[Gsplat] SH base labels 无效,将禁用动态 SH: {err}");
                m_shDeltaDisabled = true;
                return;
            }

            // 2) centroids buffers(常驻)
            if (GsplatAsset.Sh1Centroids == null || GsplatAsset.Sh1Centroids.Length == 0)
            {
                Debug.LogWarning("[Gsplat] 缺少 Sh1Centroids,将禁用动态 SH(delta-v1).");
                m_shDeltaDisabled = true;
                return;
            }
            // 每个 band 的 centroids 数量必须匹配 delta header 的 labelCount.
            if (GsplatAsset.Sh1Centroids.Length != m_sh1Segments[0].LabelCount * BandCoeffCount(1))
            {
                Debug.LogWarning("[Gsplat] Sh1Centroids 长度与 delta labelCount 不一致,将禁用动态 SH(delta-v1).");
                m_shDeltaDisabled = true;
                return;
            }
            m_sh1CentroidsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GsplatAsset.Sh1Centroids.Length, 12);
            m_sh1CentroidsBuffer.SetData(GsplatAsset.Sh1Centroids);

            if (m_renderer.SHBands >= 2)
            {
                if (GsplatAsset.Sh2Centroids == null || GsplatAsset.Sh2Centroids.Length == 0)
                {
                    Debug.LogWarning("[Gsplat] 缺少 Sh2Centroids,将禁用动态 SH(delta-v1).");
                    m_shDeltaDisabled = true;
                    return;
                }
                if (GsplatAsset.Sh2Centroids.Length != m_sh2Segments[0].LabelCount * BandCoeffCount(2))
                {
                    Debug.LogWarning("[Gsplat] Sh2Centroids 长度与 delta labelCount 不一致,将禁用动态 SH(delta-v1).");
                    m_shDeltaDisabled = true;
                    return;
                }
                m_sh2CentroidsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GsplatAsset.Sh2Centroids.Length, 12);
                m_sh2CentroidsBuffer.SetData(GsplatAsset.Sh2Centroids);
            }
            if (m_renderer.SHBands >= 3)
            {
                if (GsplatAsset.Sh3Centroids == null || GsplatAsset.Sh3Centroids.Length == 0)
                {
                    Debug.LogWarning("[Gsplat] 缺少 Sh3Centroids,将禁用动态 SH(delta-v1).");
                    m_shDeltaDisabled = true;
                    return;
                }
                if (GsplatAsset.Sh3Centroids.Length != m_sh3Segments[0].LabelCount * BandCoeffCount(3))
                {
                    Debug.LogWarning("[Gsplat] Sh3Centroids 长度与 delta labelCount 不一致,将禁用动态 SH(delta-v1).");
                    m_shDeltaDisabled = true;
                    return;
                }
                m_sh3CentroidsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GsplatAsset.Sh3Centroids.Length, 12);
                m_sh3CentroidsBuffer.SetData(GsplatAsset.Sh3Centroids);
            }

            // 3) labels state(从 startFrame=0 的 base labels 初始化)
            m_shLabelsScratch = new ushort[effectiveSplatCount];

            m_sh1Labels = new ushort[effectiveSplatCount];
            Buffer.BlockCopy(m_sh1Segments[0].BaseLabelsBytes, 0, m_sh1Labels, 0, effectiveSplatCount * 2);
            if (m_renderer.SHBands >= 2)
            {
                m_sh2Labels = new ushort[effectiveSplatCount];
                Buffer.BlockCopy(m_sh2Segments[0].BaseLabelsBytes, 0, m_sh2Labels, 0, effectiveSplatCount * 2);
            }
            if (m_renderer.SHBands >= 3)
            {
                m_sh3Labels = new ushort[effectiveSplatCount];
                Buffer.BlockCopy(m_sh3Segments[0].BaseLabelsBytes, 0, m_sh3Labels, 0, effectiveSplatCount * 2);
            }

            m_shDeltaFrameCount = GsplatAsset.ShFrameCount;
            m_shDeltaCurrentFrame = 0;
            m_shDeltaCurrentSegmentIndex = 0;

            // updates buffer 先给一个小容量,后续按需扩容.
            EnsureShDeltaUpdatesCapacity(1024);

            m_shDeltaInitialized = true;
        }

        void TryApplyShDeltaForTime(float t)
        {
            if (!m_shDeltaInitialized || m_shDeltaDisabled)
                return;

            if (m_pendingSplatCount > 0)
                return; // 异步上传未完成时,避免被 UploadData 覆盖.

            var frameCount = m_shDeltaFrameCount;
            if (frameCount <= 1)
                return;

            var target = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(t) * (frameCount - 1)), 0, frameCount - 1);
            if (target == m_shDeltaCurrentFrame)
                return;

            try
            {
                if (!TryApplyShDeltaToFrame(target))
                {
                    Debug.LogWarning("[Gsplat] 动态 SH(delta-v1) 更新失败,将禁用后续更新并保持 frame0.");
                    m_shDeltaDisabled = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Gsplat] 动态 SH(delta-v1) 更新异常,将禁用后续更新并保持 frame0: {e.Message}");
                m_shDeltaDisabled = true;
            }
        }

        bool TryApplyShDeltaToFrame(int targetFrame)
        {
            var effectiveSplatCount = checked((int)m_effectiveSplatCount);
            if (effectiveSplatCount <= 0)
                return true;

            // 优化: 仅处理最常见的顺序播放(前进 1 帧).
            if (targetFrame == m_shDeltaCurrentFrame + 1)
            {
                var segIndex = FindSegmentIndex(m_sh1Segments, targetFrame);
                if (segIndex == m_shDeltaCurrentSegmentIndex)
                {
                    var seg = m_sh1Segments[segIndex];
                    var rel = targetFrame - seg.StartFrame;
                    if (rel > 0)
                    {
                        // 先读 updateCount 决定 scratch 容量,避免 updateCount 较大时数组越界.
                        var required = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                            seg.DeltaBytes.AsSpan(seg.BlockOffsets[rel - 1], 4));
                        if (m_renderer.SHBands >= 2)
                        {
                            var seg2 = m_sh2Segments[segIndex];
                            var u2 = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                                seg2.DeltaBytes.AsSpan(seg2.BlockOffsets[rel - 1], 4));
                            required = Math.Max(required, u2);
                        }
                        if (m_renderer.SHBands >= 3)
                        {
                            var seg3 = m_sh3Segments[segIndex];
                            var u3 = (int)BinaryPrimitives.ReadUInt32LittleEndian(
                                seg3.DeltaBytes.AsSpan(seg3.BlockOffsets[rel - 1], 4));
                            required = Math.Max(required, u3);
                        }
                        EnsureShDeltaUpdatesCapacity(required);

                        // band1
                        var wrote = ReadUpdateBlock(seg, rel, effectiveSplatCount, seg.LabelCount, m_shDeltaUpdatesScratch);
                        if (!TryDispatchShDeltaUpdates(1, m_sh1CentroidsBuffer, m_kernelApplySh1, m_shDeltaUpdatesScratch, wrote))
                            return false;
                        for (var i = 0; i < wrote; i++)
                            m_sh1Labels[m_shDeltaUpdatesScratch[i].splatId] = (ushort)m_shDeltaUpdatesScratch[i].label;

                        // band2/band3(可选)
                        if (m_renderer.SHBands >= 2)
                        {
                            var seg2 = m_sh2Segments[segIndex];
                            wrote = ReadUpdateBlock(seg2, rel, effectiveSplatCount, seg2.LabelCount, m_shDeltaUpdatesScratch);
                            if (!TryDispatchShDeltaUpdates(2, m_sh2CentroidsBuffer, m_kernelApplySh2, m_shDeltaUpdatesScratch, wrote))
                                return false;
                            for (var i = 0; i < wrote; i++)
                                m_sh2Labels[m_shDeltaUpdatesScratch[i].splatId] = (ushort)m_shDeltaUpdatesScratch[i].label;
                        }
                        if (m_renderer.SHBands >= 3)
                        {
                            var seg3 = m_sh3Segments[segIndex];
                            wrote = ReadUpdateBlock(seg3, rel, effectiveSplatCount, seg3.LabelCount, m_shDeltaUpdatesScratch);
                            if (!TryDispatchShDeltaUpdates(3, m_sh3CentroidsBuffer, m_kernelApplySh3, m_shDeltaUpdatesScratch, wrote))
                                return false;
                            for (var i = 0; i < wrote; i++)
                                m_sh3Labels[m_shDeltaUpdatesScratch[i].splatId] = (ushort)m_shDeltaUpdatesScratch[i].label;
                        }

                        m_shDeltaCurrentFrame = targetFrame;
                        return true;
                    }
                }
            }

            // 兜底: seek/jump/backward -> 从 segment base 解码到目标帧,再 diff 应用.
            var newSegIndex = FindSegmentIndex(m_sh1Segments, targetFrame);
            if (newSegIndex < 0)
                return false;

            EnsureShDeltaUpdatesCapacity(effectiveSplatCount);

            // band1
            var s1 = m_sh1Segments[newSegIndex];
            DecodeLabelsAtFrame(s1, targetFrame, effectiveSplatCount, s1.LabelCount, m_shLabelsScratch);
            var wroteDiff = 0;
            for (var i = 0; i < effectiveSplatCount; i++)
            {
                var cur = m_sh1Labels[i];
                var next = m_shLabelsScratch[i];
                if (cur == next)
                    continue;
                m_shDeltaUpdatesScratch[wroteDiff++] = new ShDeltaUpdate { splatId = (uint)i, label = next };
            }
            if (!TryDispatchShDeltaUpdates(1, m_sh1CentroidsBuffer, m_kernelApplySh1, m_shDeltaUpdatesScratch, wroteDiff))
                return false;
            for (var i = 0; i < wroteDiff; i++)
                m_sh1Labels[m_shDeltaUpdatesScratch[i].splatId] = (ushort)m_shDeltaUpdatesScratch[i].label;

            // band2/band3
            if (m_renderer.SHBands >= 2)
            {
                var s2 = m_sh2Segments[newSegIndex];
                DecodeLabelsAtFrame(s2, targetFrame, effectiveSplatCount, s2.LabelCount, m_shLabelsScratch);
                wroteDiff = 0;
                for (var i = 0; i < effectiveSplatCount; i++)
                {
                    var cur = m_sh2Labels[i];
                    var next = m_shLabelsScratch[i];
                    if (cur == next)
                        continue;
                    m_shDeltaUpdatesScratch[wroteDiff++] = new ShDeltaUpdate { splatId = (uint)i, label = next };
                }
                if (!TryDispatchShDeltaUpdates(2, m_sh2CentroidsBuffer, m_kernelApplySh2, m_shDeltaUpdatesScratch, wroteDiff))
                    return false;
                for (var i = 0; i < wroteDiff; i++)
                    m_sh2Labels[m_shDeltaUpdatesScratch[i].splatId] = (ushort)m_shDeltaUpdatesScratch[i].label;
            }
            if (m_renderer.SHBands >= 3)
            {
                var s3 = m_sh3Segments[newSegIndex];
                DecodeLabelsAtFrame(s3, targetFrame, effectiveSplatCount, s3.LabelCount, m_shLabelsScratch);
                wroteDiff = 0;
                for (var i = 0; i < effectiveSplatCount; i++)
                {
                    var cur = m_sh3Labels[i];
                    var next = m_shLabelsScratch[i];
                    if (cur == next)
                        continue;
                    m_shDeltaUpdatesScratch[wroteDiff++] = new ShDeltaUpdate { splatId = (uint)i, label = next };
                }
                if (!TryDispatchShDeltaUpdates(3, m_sh3CentroidsBuffer, m_kernelApplySh3, m_shDeltaUpdatesScratch, wroteDiff))
                    return false;
                for (var i = 0; i < wroteDiff; i++)
                    m_sh3Labels[m_shDeltaUpdatesScratch[i].splatId] = (ushort)m_shDeltaUpdatesScratch[i].label;
            }

            m_shDeltaCurrentSegmentIndex = newSegIndex;
            m_shDeltaCurrentFrame = targetFrame;
            return true;
        }

        void RefreshEffectiveConfigAndLog()
        {
            m_effectiveHas4D = Has4DFields(GsplatAsset);
            m_effectiveSplatCount = GsplatAsset ? GsplatAsset.SplatCount : 0;
            m_effectiveSHBands = GsplatAsset ? GsplatAsset.SHBands : (byte)0;

            // 创建前估算 GPU 资源占用,让失败是可解释的.
            var desiredBytes = GsplatUtils.EstimateGpuBytes(m_effectiveSplatCount, m_effectiveSHBands, m_effectiveHas4D);
            var desiredMiB = GsplatUtils.BytesToMiB(desiredBytes);
            Debug.Log(
                $"[Gsplat] GPU 资源估算: {desiredMiB:F1} MiB " +
                $"(splats={m_effectiveSplatCount}, shBands={m_effectiveSHBands}, has4d={(m_effectiveHas4D ? 1 : 0)})");

            var settings = GsplatSettings.Instance;
            if (!settings)
                return;

            // 以显卡总显存的比例做风险提示(注意: 这不是实时可用显存).
            var vramMiB = SystemInfo.graphicsMemorySize;
            var warnRatio = Mathf.Clamp01(settings.VramWarnRatio);
            var thresholdBytes = (long)(vramMiB * 1024L * 1024L * warnRatio);
            if (vramMiB > 0 && warnRatio > 0.0f && desiredBytes > thresholdBytes)
            {
                var thresholdMiB = GsplatUtils.BytesToMiB(thresholdBytes);
                Debug.LogWarning(
                    $"[Gsplat] 资源风险较高: 估算 {desiredMiB:F1} MiB > 阈值 {thresholdMiB:F1} MiB " +
                    $"(显存 {vramMiB} MiB * {warnRatio:P0}). " +
                    $"建议: 降低 SH 阶数,限制 splat 数量,或使用更大显存的 GPU.");

                // 自动降级(可配置)
                var beforeCount = m_effectiveSplatCount;
                var beforeBands = m_effectiveSHBands;

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

                if (beforeCount != m_effectiveSplatCount || beforeBands != m_effectiveSHBands)
                {
                    var afterBytes = GsplatUtils.EstimateGpuBytes(m_effectiveSplatCount, m_effectiveSHBands, m_effectiveHas4D);
                    var afterMiB = GsplatUtils.BytesToMiB(afterBytes);
                    Debug.LogWarning(
                        $"[Gsplat] AutoDegrade 生效: " +
                        $"splats {beforeCount} -> {m_effectiveSplatCount}, " +
                        $"shBands {beforeBands} -> {m_effectiveSHBands}, " +
                        $"估算 {desiredMiB:F1} MiB -> {afterMiB:F1} MiB");
                }
            }
        }

        void RefreshTimeSegments()
        {
            // 默认关闭:
            // - 只有满足“keyframe 多 segment 且不重叠”的强条件时才开启.
            m_timeSegmentsEnabled = false;
            m_timeSegments = null;

            if (!GsplatAsset || !m_effectiveHas4D)
                return;

            // 仅对 window model 生效:
            // - gaussian model 的时间核是连续权重,无法简单选出“唯一 segment”.
            if (GetEffectiveTimeModel() != 1)
                return;

            var times = GsplatAsset.Times;
            var durations = GsplatAsset.Durations;
            if (times == null || durations == null)
                return;

            var total = checked((int)m_effectiveSplatCount);
            if (total <= 0 || times.Length < total || durations.Length < total)
                return;

            var segments = new List<TimeSegment>(16);

            var i = 0;
            while (i < total)
            {
                var t0 = times[i];
                var dt = durations[i];

                // 防御: 发现非法值则直接放弃优化,避免误判导致范围错误.
                if (float.IsNaN(t0) || float.IsInfinity(t0) ||
                    float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0.0f)
                {
                    m_timeSegmentsEnabled = false;
                    m_timeSegments = null;
                    return;
                }

                var tBits = FloatBits(t0);
                var dtBits = FloatBits(dt);

                var j = i + 1;
                while (j < total && FloatBits(times[j]) == tBits && FloatBits(durations[j]) == dtBits)
                    j++;

                segments.Add(new TimeSegment
                {
                    BaseIndex = (uint)i,
                    Count = (uint)(j - i),
                    Time0 = t0,
                    Duration = dt,
                    End = t0 + dt
                });

                i = j;
            }

            if (segments.Count <= 1)
                return;

            // ----------------------------------------------------------------
            // 启用条件(保守):
            // - segment 的 time0 单调不减.
            // - segments 不重叠(prevEnd <= nextTime0).
            //
            // 这样在任意时刻 t,最多只有 1 个 segment 需要被渲染,才能安全地用“子范围 sort+draw”优化.
            // ----------------------------------------------------------------
            const float k_epsilon = 1e-5f;
            for (var si = 1; si < segments.Count; si++)
            {
                var prev = segments[si - 1];
                var cur = segments[si];

                if (cur.Count == 0)
                    return;

                if (cur.Time0 + k_epsilon < prev.Time0)
                    return;

                if (prev.End > cur.Time0 + k_epsilon)
                    return;
            }

            // 兜底校验: 最后一段不能越过 total.
            var last = segments[segments.Count - 1];
            if (last.BaseIndex + last.Count > (uint)total)
                return;

            m_timeSegments = segments.ToArray();
            m_timeSegmentsEnabled = true;

            var minCount = m_timeSegments[0].Count;
            var maxCount = minCount;
            for (var si = 1; si < m_timeSegments.Length; si++)
            {
                var c = m_timeSegments[si].Count;
                if (c < minCount)
                    minCount = c;
                if (c > maxCount)
                    maxCount = c;
            }

            // 说明:
            // - 这条 log 只在 asset/recreate 时打印,不会按帧刷屏.
            // - 便于用户确认“当前 asset 是否命中了 segment 优化”.
            Debug.Log(
                $"[Gsplat] 已启用 keyframe segment 子范围 sort/draw 优化: " +
                $"segments={m_timeSegments.Length}, perSegmentCount={minCount}..{maxCount}, totalRecords={m_effectiveSplatCount}.");
        }

        int FindTimeSegmentIndex(float t)
        {
            if (!m_timeSegmentsEnabled || m_timeSegments == null || m_timeSegments.Length == 0)
                return -1;

            // 与 shader 保持一致: 归一化时间强制落在 [0,1].
            t = Mathf.Clamp01(t);

            // 二分: 找到最后一个 Time0 <= t 的 segment.
            var lo = 0;
            var hi = m_timeSegments.Length - 1;
            var candidate = -1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                if (t < m_timeSegments[mid].Time0)
                {
                    hi = mid - 1;
                }
                else
                {
                    candidate = mid;
                    lo = mid + 1;
                }
            }

            if (candidate < 0)
                return -1;

            // 覆盖边界误差:
            // - keyframe 分段常见 dt=1/N,浮点加法可能出现极小的累积误差.
            // - 用一个小 epsilon 让 t==End 附近仍命中.
            const float k_epsilon = 1e-5f;
            return t <= m_timeSegments[candidate].End + k_epsilon ? candidate : -1;
        }

        void UpdateSortRangeForTime(float t)
        {
            // uploadedCount 语义:
            // - 对于同步上传: uploadedCount==totalRecords.
            // - 对于异步上传: uploadedCount 是已写入 GPU buffers 的前缀长度[0,uploadedCount).
            var uploadedCount = SplatCount;

            if (!m_timeSegmentsEnabled || m_timeSegments == null || m_timeSegments.Length == 0)
            {
                m_sortSplatBaseIndexThisFrame = 0;
                m_sortSplatCountThisFrame = uploadedCount;
                return;
            }

            var segIndex = FindTimeSegmentIndex(t);
            if (segIndex < 0)
            {
                // 保守兜底: 若无法定位 segment,回退到全量排序/渲染以保证正确性.
                m_sortSplatBaseIndexThisFrame = 0;
                m_sortSplatCountThisFrame = uploadedCount;
                return;
            }

            var seg = m_timeSegments[segIndex];
            var baseIndex = seg.BaseIndex;
            var count = seg.Count;

            // 异步上传兜底: segment 可能尚未完全上传,这里按已上传前缀做 clamp,避免越界读取.
            if (baseIndex >= uploadedCount)
            {
                m_sortSplatBaseIndexThisFrame = baseIndex;
                m_sortSplatCountThisFrame = 0;
                return;
            }

            var available = uploadedCount - baseIndex;
            if (count > available)
                count = available;

            m_sortSplatBaseIndexThisFrame = baseIndex;
            m_sortSplatCountThisFrame = count;
        }

        bool TryCreateOrRecreateRenderer()
        {
            m_disabledDueToError = false;
            m_pendingSplatCount = 0;

            if (!GsplatAsset)
            {
                m_effectiveSplatCount = 0;
                m_effectiveSHBands = 0;
                m_effectiveHas4D = false;
                m_timeSegmentsEnabled = false;
                m_timeSegments = null;
                m_renderer?.Dispose();
                m_renderer = null;
                return false;
            }

            RefreshEffectiveConfigAndLog();
            RefreshTimeSegments();

            try
            {
                if (m_renderer == null)
                    m_renderer = new GsplatRendererImpl(m_effectiveSplatCount, m_effectiveSHBands, m_effectiveHas4D);
                else
                    m_renderer.RecreateResources(m_effectiveSplatCount, m_effectiveSHBands, m_effectiveHas4D);
            }
            catch (Exception ex)
            {
                // 失败要可行动: 给出 buffer 创建失败的恢复建议,并禁用当前 renderer 的渲染.
                m_disabledDueToError = true;
                m_renderer?.Dispose();
                m_renderer = null;
                Debug.LogError(
                    $"[Gsplat] GraphicsBuffer 创建失败,已禁用该对象的渲染. " +
                    $"建议: 降低 SH(或启用 AutoDegrade=ReduceSH),减少 splat 数(或启用 CapSplatCount),或更换更大显存 GPU.\n" +
                    ex);
                return false;
            }

            return true;
        }

        void SetBufferData()
        {
            var count = (int)m_effectiveSplatCount;
            if (count <= 0)
                return;

            m_renderer.PositionBuffer.SetData(GsplatAsset.Positions, 0, 0, count);
            m_renderer.ScaleBuffer.SetData(GsplatAsset.Scales, 0, 0, count);
            m_renderer.RotationBuffer.SetData(GsplatAsset.Rotations, 0, 0, count);
            m_renderer.ColorBuffer.SetData(GsplatAsset.Colors, 0, 0, count);
            if (m_renderer.SHBands > 0)
            {
                var coefficientCount = GsplatUtils.SHBandsToCoefficientCount(m_renderer.SHBands);
                m_renderer.SHBuffer.SetData(GsplatAsset.SHs, 0, 0, coefficientCount * count);
            }

            if (m_renderer.Has4D)
            {
                m_renderer.VelocityBuffer.SetData(GsplatAsset.Velocities, 0, 0, count);
                m_renderer.TimeBuffer.SetData(GsplatAsset.Times, 0, 0, count);
                m_renderer.DurationBuffer.SetData(GsplatAsset.Durations, 0, 0, count);
            }
        }


        void SetBufferDataAsync()
        {
            m_pendingSplatCount = m_effectiveSplatCount;
        }

        void UploadData()
        {
            var offset = (int)(m_effectiveSplatCount - m_pendingSplatCount);
            var count = (int)Math.Min(UploadBatchSize, m_pendingSplatCount);
            m_pendingSplatCount -= (uint)count;
            m_renderer.PositionBuffer.SetData(GsplatAsset.Positions, offset, offset, count);
            m_renderer.ScaleBuffer.SetData(GsplatAsset.Scales, offset, offset, count);
            m_renderer.RotationBuffer.SetData(GsplatAsset.Rotations, offset, offset, count);
            m_renderer.ColorBuffer.SetData(GsplatAsset.Colors, offset, offset, count);
            if (m_renderer.Has4D)
            {
                m_renderer.VelocityBuffer.SetData(GsplatAsset.Velocities, offset, offset, count);
                m_renderer.TimeBuffer.SetData(GsplatAsset.Times, offset, offset, count);
                m_renderer.DurationBuffer.SetData(GsplatAsset.Durations, offset, offset, count);
            }

            if (m_renderer.SHBands <= 0) return;
            var coefficientCount = GsplatUtils.SHBandsToCoefficientCount(m_renderer.SHBands);
            m_renderer.SHBuffer.SetData(GsplatAsset.SHs, coefficientCount * offset,
                coefficientCount * offset, coefficientCount * count);
        }

        // --------------------------------------------------------------------
        // Public API: show/hide 显隐控制
        // --------------------------------------------------------------------
        public void SetVisible(bool visible, bool animated = true)
        {
            // 说明:
            // - 该 API 只影响“可见性”,不影响组件 enabled 与 GameObject active.
            // - animated=true 且 EnableVisibilityAnimation=true 时,播放燃烧环动画.
            // - 其它情况走硬切(立即 Visible/Hidden),但仍会在 Hidden 时停掉 sorter/draw 开销.
            if (!animated || !EnableVisibilityAnimation)
            {
                m_visibilityState = visible ? VisibilityAnimState.Visible : VisibilityAnimState.Hidden;
                m_visibilityProgress01 = 1.0f;
                return;
            }

            if (visible)
                PlayShow();
            else
                PlayHide();
        }

        public void PlayShow()
        {
            if (!EnableVisibilityAnimation)
            {
                // 动画未启用时,退化为硬切 show.
                m_visibilityState = VisibilityAnimState.Visible;
                m_visibilityProgress01 = 1.0f;
                return;
            }

            // 已经在“可见或正在显示”时,不重复触发.
            if (m_visibilityState is VisibilityAnimState.Visible or VisibilityAnimState.Showing)
                return;

            // 若当前正在 hide,允许无缝反向.
            var startProgress = 0.0f;
            if (m_visibilityState == VisibilityAnimState.Hiding)
                startProgress = 1.0f - Mathf.Clamp01(m_visibilityProgress01);

            m_visibilityState = VisibilityAnimState.Showing;
            m_visibilityProgress01 = Mathf.Clamp01(startProgress);

            // 重置时间基准,避免因停顿导致第一帧 dt 巨大而“瞬间播完”.
            m_visibilityLastAdvanceRealtime = -1.0f;

#if UNITY_EDITOR
            // 立刻触发一次刷新,避免“点了 Show 但视口没动”导致看起来没反应.
            RequestEditorRepaintForVisibilityAnimation(force: true, reason: "PlayShow");
            RegisterVisibilityEditorTickerIfAnimating();
#endif
        }

        public void PlayHide()
        {
            if (!EnableVisibilityAnimation)
            {
                // 动画未启用时,退化为硬切 hide.
                m_visibilityState = VisibilityAnimState.Hidden;
                m_visibilityProgress01 = 1.0f;
                return;
            }

            // 已经在“隐藏或正在隐藏”时,不重复触发.
            if (m_visibilityState is VisibilityAnimState.Hidden or VisibilityAnimState.Hiding)
                return;

            // 若当前正在 show,允许无缝反向.
            var startProgress = 0.0f;
            if (m_visibilityState == VisibilityAnimState.Showing)
                startProgress = 1.0f - Mathf.Clamp01(m_visibilityProgress01);

            m_visibilityState = VisibilityAnimState.Hiding;
            m_visibilityProgress01 = Mathf.Clamp01(startProgress);

            // 重置时间基准,避免因停顿导致第一帧 dt 巨大而“瞬间播完”.
            m_visibilityLastAdvanceRealtime = -1.0f;

#if UNITY_EDITOR
            RequestEditorRepaintForVisibilityAnimation(force: true, reason: "PlayHide");
            RegisterVisibilityEditorTickerIfAnimating();
#endif
        }

        void InitVisibilityOnEnable()
        {
            // 默认保持旧行为: 组件启用后直接可见.
            m_visibilityState = VisibilityAnimState.Visible;
            m_visibilityProgress01 = 1.0f;
            m_visibilityLastAdvanceRealtime = -1.0f;

            // 只有显式启用该功能时才会自动播放 show.
            if (!EnableVisibilityAnimation || !PlayShowOnEnable)
                return;

            // 语义: “初始隐藏 -> 播放 show”.
            // - 这里直接进入 Showing,并从 progress=0 开始推进.
            m_visibilityState = VisibilityAnimState.Showing;
            m_visibilityProgress01 = 0.0f;

#if UNITY_EDITOR
            // OnEnable 触发 show 时也要请求 repaint,避免“启用后不动就不播”.
            RequestEditorRepaintForVisibilityAnimation(force: true, reason: "OnEnable");
            RegisterVisibilityEditorTickerIfAnimating();
#endif
        }

        void AdvanceVisibilityStateIfNeeded()
        {
            // 只有动画启用时才推进 Showing/Hiding 的 progress.
            if (!EnableVisibilityAnimation)
                return;

            var prevState = m_visibilityState;

            var now = Time.realtimeSinceStartup;
            var dt = 0.0f;
            if (m_visibilityLastAdvanceRealtime >= 0.0f)
                dt = Mathf.Max(0.0f, now - m_visibilityLastAdvanceRealtime);
            m_visibilityLastAdvanceRealtime = now;

            // dt 异常时直接视为 0,避免 progress 跳变.
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0.0f)
                dt = 0.0f;

            if (m_visibilityState == VisibilityAnimState.Showing)
            {
                if (ShowDuration <= 0.0f || float.IsNaN(ShowDuration) || float.IsInfinity(ShowDuration))
                {
                    m_visibilityProgress01 = 1.0f;
                    m_visibilityState = VisibilityAnimState.Visible;
                }
                else
                {
                    m_visibilityProgress01 = Mathf.Clamp01(m_visibilityProgress01 + dt / ShowDuration);
                    if (m_visibilityProgress01 >= 1.0f)
                        m_visibilityState = VisibilityAnimState.Visible;
                }
            }
            else if (m_visibilityState == VisibilityAnimState.Hiding)
            {
                if (HideDuration <= 0.0f || float.IsNaN(HideDuration) || float.IsInfinity(HideDuration))
                {
                    m_visibilityProgress01 = 1.0f;
                    m_visibilityState = VisibilityAnimState.Hidden;
                }
                else
                {
                    m_visibilityProgress01 = Mathf.Clamp01(m_visibilityProgress01 + dt / HideDuration);
                    if (m_visibilityProgress01 >= 1.0f)
                        m_visibilityState = VisibilityAnimState.Hidden;
                }
            }

#if UNITY_EDITOR
            if (GsplatEditorDiagnostics.Enabled && prevState != m_visibilityState)
            {
                GsplatEditorDiagnostics.MarkVisibilityState(this, "state.change",
                    m_visibilityState.ToString(), m_visibilityProgress01);
            }

            // Editor 下,在 Showing/Hiding 期间主动 Repaint,保证动画无需鼠标交互也能连续推进.
            // 同时在动画刚结束时补 1 次强制刷新,避免停在“最后一帧之前”的错觉.
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

        void PushVisibilityUniformsForThisFrame(Bounds localBounds)
        {
            if (m_renderer == null)
                return;

            // mode: 0=off,1=show,2=hide
            var mode = 0;
            var progress = 1.0f;

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

            var centerModel = CalcVisibilityCenterModel(localBounds);
            var maxRadius = CalcVisibilityMaxRadius(localBounds, centerModel);

            // show/hide 的 ring/trail 宽度允许分别调参.
            var ringWidthNorm = ShowRingWidthNormalized;
            var trailWidthNorm = ShowTrailWidthNormalized;
            if (mode == 2)
            {
                ringWidthNorm = HideRingWidthNormalized;
                trailWidthNorm = HideTrailWidthNormalized;
            }

            var ringWidth = maxRadius * Mathf.Clamp01(ringWidthNorm);
            var trailWidth = maxRadius * Mathf.Clamp01(trailWidthNorm);

            var glowIntensity = mode == 2 ? HideGlowIntensity : ShowGlowIntensity;
            var showGlowSparkleStrength = mode == 1 ? ShowGlowSparkleStrength : 0.0f;

            // 使用 realtimeSinceStartup 保证 EditMode 也能随时间抖动.
            var t = Time.realtimeSinceStartup;

            m_renderer.SetVisibilityUniforms(
                mode: mode,
                noiseMode: (int)VisibilityNoiseMode,
                progress: progress,
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

        Vector3 CalcVisibilityCenterModel(Bounds localBounds)
        {
            // 规则:
            // - 用户指定 VisibilityCenter 时,以该 Transform 的 world position 为中心.
            // - 否则回退使用 bounds.center.
            if (VisibilityCenter)
            {
                var worldPos = VisibilityCenter.position;
                if (!float.IsNaN(worldPos.x) && !float.IsNaN(worldPos.y) && !float.IsNaN(worldPos.z) &&
                    !float.IsInfinity(worldPos.x) && !float.IsInfinity(worldPos.y) && !float.IsInfinity(worldPos.z))
                {
                    var modelPos = transform.InverseTransformPoint(worldPos);
                    return modelPos;
                }
            }

            return localBounds.center;
        }

        static float CalcVisibilityMaxRadius(Bounds localBounds, Vector3 centerModel)
        {
            // 用 bounds 8 个角点到 center 的最大距离作为 maxRadius,保证扩散能覆盖整个对象.
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

        Bounds CalcVisibilityExpandedRenderBounds(Bounds baseBounds)
        {
            // 说明:
            // - 本次改进增加了“空间扭曲(位移)”效果,会让 splat 的中心位置在 shader 中偏移.
            // - 若不扩展 worldBounds,Unity 的 CPU culling 可能会把“偏移到 bounds 外”的 splats 直接裁掉,
            //   表现为边缘区域突然消失或不连续.
            //
            // 策略:
            // - 仅在 Showing/Hiding(动画进行中)时扩展 bounds.
            // - 扩展量取一个偏保守的上界,宁可稍微多画一点,也不要让动画被裁掉.
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
            var maxRadius = CalcVisibilityMaxRadius(baseBounds, centerModel);

            // 与 shader 侧 warpAmp 的同量纲上界(更保守一点).
            var warpPadding = maxRadius * ns * ws * 0.15f;
            if (warpPadding > 0.0f && !float.IsNaN(warpPadding) && !float.IsInfinity(warpPadding))
                baseBounds.Expand(warpPadding * 2.0f);

            return baseBounds;
        }


        void OnEnable()
        {
            GsplatSorter.Instance.RegisterGsplat(this);
            m_timeNormalizedThisFrame = Mathf.Clamp01(TimeNormalized);
            if (!TryCreateOrRecreateRenderer())
            {
                // renderer 创建失败时也要清理 delta 资源,避免旧状态残留.
                TryInitShDeltaRuntime();
                return;
            }
#if UNITY_EDITOR
            if (AsyncUpload && Application.isPlaying)
#else
            if (AsyncUpload)
#endif
                SetBufferDataAsync();
            else
                SetBufferData();

            // 初始化 delta runtime(若 asset 没有 delta 字段,这里会自动 no-op).
            TryInitShDeltaRuntime();

            // 避免下一帧 Update 再次重复触发一次重建.
            m_prevAsset = GsplatAsset;

            // 初始化本帧 sort/draw 子范围(可能命中 keyframe segment 优化).
            UpdateSortRangeForTime(m_timeNormalizedThisFrame);

            // 初始化显隐动画状态:
            // - 默认不启用该效果,保持旧行为.
            // - 启用且 PlayShowOnEnable=true 时,会以 Showing(0->1) 的方式从隐藏渐进显示.
            InitVisibilityOnEnable();
            PushVisibilityUniformsForThisFrame(GsplatAsset.Bounds);
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            // Editor 下该组件被禁用时,显隐动画 ticker 也必须解绑,避免静态集合残留引用.
            UnregisterVisibilityEditorTickerIfAny();
#endif
            GsplatSorter.Instance.UnregisterGsplat(this);
            DisposeShDeltaResources();
            m_renderer?.Dispose();
            m_renderer = null;
            m_prevAsset = null;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // 编辑态拖动 `TimeNormalized` 时,SceneView 往往不会像 GameView 那样稳定触发“排序+渲染”的完整链路.
            // 这里的目标是: 你在 Inspector 拖动滑条时,SceneView 立刻 Repaint,并且排序使用最新的时间参数.
            if (Application.isPlaying)
                return;

            var t = TimeNormalized;
            if (float.IsNaN(t) || float.IsInfinity(t))
                t = 0.0f;
            t = Mathf.Clamp01(t);

            m_timeNormalizedThisFrame = t;
            UpdateSortRangeForTime(t);

            // 触发 Editor 的渲染循环:
            // - QueuePlayerLoopUpdate: 让 ExecuteAlways 的 Update 尽快执行(包括动态 SH(delta)等逻辑).
            // - RepaintAll: 让 SceneView 相机立刻渲染,从而触发按相机排序(beginCameraRendering).
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            // RepaintAllViews 会同时刷新 SceneView/GameView,避免“拖动 Inspector 滑条时 GameView 不更新/突然消失”的错觉.
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

            // 可选: 如果启用了 `.splat4d` 动态 SH(delta-v1),尽量在编辑态拖动时也同步刷新一次.
            TryApplyShDeltaForTime(t);
        }
#endif

        void Update()
        {
            // 先推进显隐动画状态机:
            // - 这样本帧 render/相机回调使用的 uniforms 都基于同一个 progress.
            // - 也能确保 hide 播完后及时进入 Hidden,从根源停掉 sorter/draw 开销.
            AdvanceVisibilityStateIfNeeded();

            // ----------------------------------------------------------------
            // 稳态恢复: GPU buffer 可能在 Editor/Metal 下因域重载、图形设备切换等原因失效.
            // - 典型表象: Unity 输出 "requires a ComputeBuffer ... Skipping draw calls" 并导致视口闪烁/消失.
            // - 仅检查 `!= null` 不够,必须结合 `GraphicsBuffer.IsValid()`.
            // - 这里做一次节流的自动重建,避免用户必须手动禁用/启用组件.
            // ----------------------------------------------------------------
            if (!m_disabledDueToError && m_renderer != null && !m_renderer.Valid && GsplatAsset)
            {
                var now = Time.realtimeSinceStartup;
                if (now >= m_nextRendererRecoveryTime)
                {
                    m_nextRendererRecoveryTime = now + 1.0f;
                    Debug.LogWarning("[Gsplat] 检测到 GraphicsBuffer 已失效,将尝试自动重建 renderer 资源(可能会有一次性卡顿).");

                    if (TryCreateOrRecreateRenderer())
                    {
#if UNITY_EDITOR
                        if (AsyncUpload && Application.isPlaying)
#else
                        if (AsyncUpload)
#endif
                            SetBufferDataAsync();
                        else
                            SetBufferData();

                        // renderer 重建后,delta runtime 也必须重建(它依赖新的 SHBuffer).
                        TryInitShDeltaRuntime();
                    }
                }
            }

            if (!m_disabledDueToError && m_renderer != null && m_pendingSplatCount > 0)
                UploadData();

            // ----------------------------------------------------------------
            // 播放控制: TimeNormalized / AutoPlay / Speed / Loop
            // - 这里把最终用于排序与渲染的时间缓存到 `m_timeNormalizedThisFrame`,
            //   以保证同一帧内 compute 排序与 shader 渲染使用同一个 t.
            // ----------------------------------------------------------------
            if (float.IsNaN(Speed) || float.IsInfinity(Speed))
                Speed = 0.0f;

            if (AutoPlay)
            {
                var next = TimeNormalized + Time.deltaTime * Speed;
                TimeNormalized = Loop ? Mathf.Repeat(next, 1.0f) : Mathf.Clamp01(next);
            }

            m_timeNormalizedThisFrame = Mathf.Clamp01(TimeNormalized);

            if (m_prevAsset != GsplatAsset)
            {
                m_prevAsset = GsplatAsset;
                if (TryCreateOrRecreateRenderer())
                {
#if UNITY_EDITOR
                    if (AsyncUpload && Application.isPlaying)
#else
                    if (AsyncUpload)
#endif
                        SetBufferDataAsync();
                    else
                        SetBufferData();
                }

                // asset 或 renderer 发生变化时,delta runtime 必须重建(包括清理旧资源).
                TryInitShDeltaRuntime();
            }

            // 更新本帧 sort/draw 子范围:
            // - 依赖最新的 TimeNormalized.
            // - 依赖最新的 upload 进度(异步上传时 SplatCount 会变化).
            // - asset 变化时 `RefreshTimeSegments` 可能改变 segment 判定,因此这里在 asset-check 之后统一刷新.
            UpdateSortRangeForTime(m_timeNormalizedThisFrame);

            // 在渲染前按 TimeNormalized 应用 delta-v1 updates(仅在帧变化时 dispatch).
            TryApplyShDeltaForTime(m_timeNormalizedThisFrame);

            if (Valid)
            {
                var motionPadding = 0.0f;
                if (m_renderer.Has4D)
                {
                    motionPadding = GsplatAsset.MaxSpeed * GsplatAsset.MaxDuration;
                    if (motionPadding < 0.0f || float.IsNaN(motionPadding) || float.IsInfinity(motionPadding))
                        motionPadding = 0.0f;
                }

                // 每次渲染前都推一次显隐 uniforms:
                // - Update 渲染路径.
                // - SRP 相机回调渲染路径(即便 Update 本帧不 submit draw,也要把 MPB 更新到最新状态).
                PushVisibilityUniformsForThisFrame(GsplatAsset.Bounds);

#if UNITY_EDITOR
                // Editor 非 Play 模式下,SRP 相机可能在同一帧内触发多次渲染(beginCameraRendering).
                // 为避免“Update 只提交一次 draw”导致 SceneView/GameView 闪烁,
                // 我们在 SRP 下改由 `GsplatSorter` 的相机回调驱动 draw 提交.
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
                    var boundsForRender = CalcVisibilityExpandedRenderBounds(GsplatAsset.Bounds);
                    m_renderer.Render(m_sortSplatCountThisFrame, transform, boundsForRender,
                        gameObject.layer, GammaToLinear, SHDegree, m_timeNormalizedThisFrame, motionPadding,
                        timeModel: GetEffectiveTimeModel(), temporalCutoff: GetEffectiveTemporalCutoff(),
                        splatBaseIndex: m_sortSplatBaseIndexThisFrame);
                }
            }
        }
    }
}
