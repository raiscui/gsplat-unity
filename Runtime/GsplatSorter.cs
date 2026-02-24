// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using System.Reflection;
#endif
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    public interface IGsplat
    {
        public Transform transform { get; }
        public uint SplatCount { get; }
        // 子范围渲染/排序:
        // - 允许把一个大 buffer 里的某一段 [baseIndex, baseIndex+SplatCount) 当作“本帧有效 splats”.
        // - 典型用途: keyframe `.splat4d(window)` 的多 segment records,每帧只渲染当前 segment.
        // - 默认值应为 0(表示从头开始).
        public uint SplatBaseIndex { get; }
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

    // --------------------------------------------------------------------
    // Editor 回调驱动渲染(仅包内使用):
    // - Unity Editor 下同一 `Time.frameCount` 可能触发多次相机渲染(beginCameraRendering).
    // - 如果 draw 只在 `ExecuteAlways.Update` 内提交,就可能只覆盖其中一次 render invocation,
    //   从而在 SceneView/GameView 里表现为“显示/不显示”闪烁.
    // - 因此在 EditMode 下,我们允许 sorter 在相机回调里驱动“再次提交 draw”,确保每次相机渲染都有 draw.
    // --------------------------------------------------------------------
    internal interface IGsplatRenderSubmitter
    {
        void SubmitDrawForCamera(Camera camera);
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
            // Payload 初始化的“有效 key 数量”:
            // - payload 里存的是 local index(0..count-1).
            // - 当本帧 sort count 发生变化时,需要重置 payload,避免旧的 payload 值越界.
            public uint PayloadInitializedCount;

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

        // --------------------------------------------------------------------
        // ActiveCameraOnly: 单相机模式的“激活相机”解析与缓存.
        // - 目标: 把“多相机 N 次 sort”压到“每帧最多 1 次 sort”.
        // - 要点: 解析结果按 `Time.frameCount` 缓存,避免在每个相机回调里重复枚举相机.
        // --------------------------------------------------------------------
        Camera m_activeGameCameraOverride;
        Camera m_lastResolvedActiveCamera;
        int m_cachedActiveCameraFrame = -1;
        bool m_cachedActiveCameraIsPlaying;
        GsplatCameraMode m_cachedActiveCameraMode;
        Camera m_cachedActiveCameraOverride;
        Camera m_cachedActiveCamera;
#if UNITY_EDITOR
        int m_cachedEditorFocusHint;
        int m_editorViewportHint;
        Camera m_editorViewportSceneViewCameraHint;

        // ActiveCameraOnly(EditMode) 下的“每帧只排序一次”门禁.
        // - 我们在 EditMode 会允许任意 SceneView 相机触发排序(避免相机实例抖动导致不排序).
        // - 因此需要额外的 per-frame guard,避免多个 SceneView/多次回调导致重复排序.
        int m_activeCameraOnlyEditModeSortedFrame = -1;

        bool m_editorSceneViewGuiHooked;
        bool m_editorSceneViewCameraTrackValid;
        Vector3 m_editorSceneViewCameraTrackPos;
        Quaternion m_editorSceneViewCameraTrackRot;
        double m_editorSceneViewCameraLastMovedTime;

        // --------------------------------------------------------------------
        // Editor API 兼容性缓存:
        // - 某些 Unity 版本/某些 Editor UI(overlay/UIElements)下,`EditorWindow.mouseOverWindow`
        //   可能为 null 或返回“非 SceneView 的内部窗口”,从而导致 ActiveCameraOnly 误判为 GameView.
        // - UnityEditor.SceneView 在部分版本中提供了 `mouseOverWindow`(SceneView)作为更可靠的信号.
        // - 但该 API 在不同版本的可见性/命名可能不同,因此这里用反射做“可选增强”,避免编译期绑定.
        // --------------------------------------------------------------------
        static PropertyInfo s_sceneViewMouseOverWindowProperty;
        static bool s_sceneViewMouseOverWindowPropertySearched;

        int m_debugLastViewportHint = -1;
        Camera m_debugLastActiveCamera;
#endif
        static Camera[] s_activeCameraCandidates;

        // 显式 override: 允许项目的“主相机切换系统”把当前主相机注入进来.
        // - Play 模式优先使用 override,以保证“你认为的主相机”就是 ActiveCamera.
        public Camera ActiveGameCameraOverride
        {
            get => m_activeGameCameraOverride;
            set
            {
                if (m_activeGameCameraOverride == value)
                    return;

                m_activeGameCameraOverride = value;

                // override 变化属于“行为级别”的改变,需要立刻失效缓存.
                InvalidateActiveCameraCache();
            }
        }

        // --------------------------------------------------------------------
        // SRP 排序驱动策略:
        // - 在 SRP(URP/HDRP) 下,我们改为使用 `RenderPipelineManager.beginCameraRendering`
        //   作为统一的“按相机触发排序”入口,以保证 SceneView(隐藏相机)也能稳定触发 sort.
        // - 在 BiRP 下,仍使用 `Camera.onPreCull`(历史行为).
        // --------------------------------------------------------------------
        public bool SortDrivenBySrpCallback => GraphicsSettings.currentRenderPipeline != null;

#if UNITY_EDITOR
        static bool IsFocusedSceneViewCamera(Camera cam)
        {
            // 说明:
            // - SceneView 的相机是隐藏相机,不会出现在 Hierarchy.
            // - 在 Play 模式下,我们希望“只在你聚焦 SceneView 并交互时”才允许为 SceneView 排序,
            //   以避免无意义的双相机双排序开销.
            var sceneView = UnityEditor.SceneView.lastActiveSceneView;
            if (sceneView == null || !sceneView.hasFocus)
                return false;

            return sceneView.camera == cam;
        }
#endif

        void InvalidateActiveCameraCache()
        {
            m_cachedActiveCameraFrame = -1;
            m_cachedActiveCamera = null;
        }

        static int GetAllCamerasNonAlloc(ref Camera[] cameras)
        {
            var count = Camera.allCamerasCount;
            if (count <= 0)
            {
                // ----------------------------------------------------------------
                // 兜底: `-batchmode -nographics` 或少数编辑器环境下,
                // `Camera.allCamerasCount` 可能返回 0,即使场景里确实存在 Camera.
                //
                // 这会导致 ActiveCameraOnly 解析失败,进一步导致:
                // - 不排序: SceneView/GameView 都可能使用过期 order.
                // - 不渲染: ActiveCameraOnly 下会直接 return,看起来像“啥都不显示”.
                //
                // 这里用 FindObjects 系列做一次兜底.
                // - 只有在 allCamerasCount==0 时才触发,因此不会影响常规运行时性能.
                // ----------------------------------------------------------------
#if UNITY_2023_1_OR_NEWER
                var found = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
#else
                var found = Object.FindObjectsOfType<Camera>();
#endif
                if (found == null || found.Length <= 0)
                    return 0;

                cameras = found;
                return found.Length;
            }

            if (cameras == null || cameras.Length < count)
                cameras = new Camera[Mathf.NextPowerOfTwo(count)];

            return Camera.GetAllCameras(cameras);
        }

#if UNITY_EDITOR
        static bool IsGameViewWindow(UnityEditor.EditorWindow w)
        {
            // 说明:
            // - UnityEditor.GameView 是 internal 类型,我们无法直接引用.
            // - 这里用 Type.Name 做一个“足够稳定”的判定,用于 EditMode 的焦点切换体验.
            if (w == null)
                return false;

            return w.GetType().Name == "GameView";
        }

        static bool TryGetSceneViewCameraFromWindow(UnityEditor.EditorWindow w, out Camera cam)
        {
            cam = null;
            if (w is not UnityEditor.SceneView sv || sv == null)
                return false;

            cam = sv.camera;
            return cam;
        }

        static Camera TryGetAnySceneViewCamera()
        {
            // 说明:
            // - 为了避免“点击 Inspector/Hierarchy 时 SceneView 失焦 -> ActiveCamera 切走 -> SceneView 闪烁”的问题,
            //   EditMode 下我们更偏向“只要 SceneView 存在就优先保证它正确”.
            // - 因此这里不要求 `hasFocus==true`,只要能拿到一个 SceneView.camera 即可.
            var last = UnityEditor.SceneView.lastActiveSceneView;
            if (last != null && last.camera)
                return last.camera;

            foreach (var v in UnityEditor.SceneView.sceneViews)
            {
                if (v is UnityEditor.SceneView sv && sv != null && sv.camera)
                    return sv.camera;
            }

            return null;
        }

        [System.Diagnostics.Conditional("GSPLAT_ACTIVE_CAMERA_DEBUG")]
        void DebugLogActiveCameraEditMode(int viewportHint, Camera resolved)
        {
            if (Application.isPlaying)
                return;

            if (resolved == m_debugLastActiveCamera && viewportHint == m_debugLastViewportHint)
                return;

            m_debugLastActiveCamera = resolved;
            m_debugLastViewportHint = viewportHint;

            var focused = UnityEditor.EditorWindow.focusedWindow;
            var over = UnityEditor.EditorWindow.mouseOverWindow;

            var focusedName = focused ? focused.GetType().Name : "null";
            var overName = over ? over.GetType().Name : "null";
            var camName = resolved ? resolved.name : "null";
            var camType = resolved ? resolved.cameraType.ToString() : "null";

            Debug.Log(
                $"[Gsplat] ActiveCameraOnly(EditMode) active={camName} type={camType} hint={viewportHint} focused={focusedName} over={overName}");
        }

        void EnsureSceneViewDuringGuiHook()
        {
            if (m_editorSceneViewGuiHooked)
                return;

            // 说明:
            // - focusedWindow/mouseOverWindow 在某些交互(鼠标锁定、拖拽 UI 控件等)下可能不稳定.
            // - 但 SceneView.duringSceneGui 是“SceneView 正在处理 GUI 事件”的强信号,更适合做交互锁定.
            UnityEditor.SceneView.duringSceneGui += OnSceneViewDuringSceneGui;
            m_editorSceneViewGuiHooked = true;
        }

        void RemoveSceneViewDuringGuiHook()
        {
            if (!m_editorSceneViewGuiHooked)
                return;

            UnityEditor.SceneView.duringSceneGui -= OnSceneViewDuringSceneGui;
            m_editorSceneViewGuiHooked = false;
        }

        void OnSceneViewDuringSceneGui(UnityEditor.SceneView sceneView)
        {
            if (Application.isPlaying)
                return;

            if (sceneView == null)
                return;

            // ----------------------------------------------------------------
            // SceneView 交互锁定:
            // - 只要用户刚刚在 SceneView 中发生“可能影响视角/需要稳定刷新”的交互,
            //   就把 hint 锁到 SceneView 一小段时间.
            //
            // 典型收益:
            // - 旋转/平移 SceneView 时鼠标扫过 UI,不再把 ActiveCamera 错切到 GameView.
            // - 鼠标锁定导致 mouseOverWindow==null 时,依然能稳定维持 SceneView 为 ActiveCamera.
            // ----------------------------------------------------------------
            var e = Event.current;
            if (e == null)
                return;

            switch (e.type)
            {
                case EventType.MouseDown:
                case EventType.MouseUp:
                case EventType.MouseMove:
                case EventType.MouseDrag:
                case EventType.ScrollWheel:
                case EventType.KeyDown:
                case EventType.KeyUp:
                    m_editorSceneViewCameraLastMovedTime = UnityEditor.EditorApplication.timeSinceStartup;
                    m_editorViewportHint = 1;
                    if (sceneView.camera)
                        m_editorViewportSceneViewCameraHint = sceneView.camera;
                    break;
            }
        }

        static bool TryGetMouseOverSceneViewWindow(out UnityEditor.SceneView sceneView)
        {
            sceneView = null;

            // ----------------------------------------------------------------
            // 反射读取 UnityEditor.SceneView.mouseOverWindow:
            // - API 是否存在取决于 Unity 版本,因此这里必须容错.
            // - 该值的语义应更接近“鼠标当前在某个 SceneView 窗口内”,可用于补强 mouseOverWindow==null 的场景.
            // ----------------------------------------------------------------
            if (!s_sceneViewMouseOverWindowPropertySearched)
            {
                s_sceneViewMouseOverWindowPropertySearched = true;
                s_sceneViewMouseOverWindowProperty = typeof(UnityEditor.SceneView).GetProperty(
                    "mouseOverWindow",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }

            if (s_sceneViewMouseOverWindowProperty == null)
                return false;

            try
            {
                sceneView = s_sceneViewMouseOverWindowProperty.GetValue(null) as UnityEditor.SceneView;
                return sceneView != null;
            }
            catch
            {
                // 某些版本可能在非 UI 线程/特定时机抛异常,这里直接吞掉并视为无信号.
                sceneView = null;
                return false;
            }
        }
#endif

        Camera ResolveActiveGameOrVrCamera()
        {
            // override 优先(主要用于 Play 模式,但在 EditMode/GameView 聚焦时也同样适用).
            if (m_activeGameCameraOverride && m_activeGameCameraOverride.isActiveAndEnabled)
            {
                var t = m_activeGameCameraOverride.cameraType;
                if (t == CameraType.Game || t == CameraType.VR)
                    return m_activeGameCameraOverride;
            }

            var cameraCount = GetAllCamerasNonAlloc(ref s_activeCameraCandidates);
            if (cameraCount <= 0)
                return null;

            Camera only = null;
            var candidateCount = 0;
            Camera main = null;
            Camera bestDepth = null;
            var bestDepthValue = float.MinValue;

            for (var i = 0; i < cameraCount; i++)
            {
                var cam = s_activeCameraCandidates[i];
                if (!cam || !cam.isActiveAndEnabled)
                    continue;

                if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.VR)
                    continue;

                candidateCount++;
                only = cam;

                // 优先级2: Camera.main(使用 MainCamera tag).
                if (!main && cam.CompareTag("MainCamera"))
                    main = cam;

                // 优先级3: depth 最大.
                if (!bestDepth || cam.depth > bestDepthValue)
                {
                    bestDepth = cam;
                    bestDepthValue = cam.depth;
                }
            }

            if (candidateCount <= 0)
                return null;

            if (candidateCount == 1)
                return only;

            if (main)
                return main;

            return bestDepth ? bestDepth : only;
        }

        int GetEditorFocusHintNonAlloc()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
                return 0;

            var focused = UnityEditor.EditorWindow.focusedWindow;

            // ----------------------------------------------------------------
            // SceneView 交互锁定(关键修复):
            // - 用户反馈: 在旋转/移动 SceneView 视角时,鼠标划过其它 UI(包括 GameView tab)仍会闪烁.
            // - 根因: 我们的 hint 更新依赖 window 信号,鼠标划过时可能短暂切换到 GameView.
            // - 解决: 只要 SceneView 相机“正在移动/刚刚移动”,就强制锁定 hint=SceneView 一小段时间.
            //
            // 直觉:
            // - 你在拖拽 SceneView 视角时,你真正关心的是 SceneView 画面稳定正确.
            // - 这段时间内不应该因为鼠标扫过 UI 就切走 ActiveCamera.
            // ----------------------------------------------------------------
            const double k_sceneViewMovementLockSeconds = 0.50;
            var trackSceneCam = m_editorViewportSceneViewCameraHint ? m_editorViewportSceneViewCameraHint : TryGetAnySceneViewCamera();
            if (trackSceneCam && trackSceneCam.transform)
            {
                var t = trackSceneCam.transform;
                var pos = t.position;
                var rot = t.rotation;
                var moved = !m_editorSceneViewCameraTrackValid ||
                            pos != m_editorSceneViewCameraTrackPos ||
                            rot != m_editorSceneViewCameraTrackRot;
                if (moved)
                {
                    m_editorSceneViewCameraTrackValid = true;
                    m_editorSceneViewCameraTrackPos = pos;
                    m_editorSceneViewCameraTrackRot = rot;
                    m_editorSceneViewCameraLastMovedTime = UnityEditor.EditorApplication.timeSinceStartup;
                }

                var sinceMoved = UnityEditor.EditorApplication.timeSinceStartup - m_editorSceneViewCameraLastMovedTime;
                if (sinceMoved >= 0.0 && sinceMoved < k_sceneViewMovementLockSeconds)
                {
                    m_editorViewportHint = 1;
                    m_editorViewportSceneViewCameraHint = trackSceneCam;
                    return 1;
                }
            }

            // ----------------------------------------------------------------
            // 说明: 为什么不用 focusedWindow 直接判断?
            // - 在编辑器里拖动 Inspector 控件/与 UI 交互时,focusedWindow 可能保持在某个 viewport,
            //   或者在多个窗口间抖动.
            // - ActiveCameraOnly 又把渲染门禁绑定到 ActiveCamera,因此这类抖动会直接表现为“显示/不显示”闪烁.
            //
            // 策略:
            // - 只在 mouseOverWindow/focusedWindow 明确是 SceneView/GameView 时更新 hint.
            // - 当鼠标在 Inspector/Hierarchy 等非 viewport UI 上时,保持上一帧 hint 不变.
            // ----------------------------------------------------------------
            var over = UnityEditor.EditorWindow.mouseOverWindow;
            var overIsGameView = IsGameViewWindow(over);

            // ----------------------------------------------------------------
            // 视口 hint 更新规则(稳态):
            // - SceneView: 只要鼠标悬停在 SceneView 上,就认为用户当前关心 SceneView,因此无条件更新 hint=SceneView.
            //   这是为了修复“鼠标在 SceneView UI 上滑动仍闪烁”(focusedWindow 可能不是 SceneView).
            //   注意: 在部分 Unity/Editor UI 组合下,`EditorWindow.mouseOverWindow` 可能为 null 或不是 SceneView,
            //   这里会额外用 `SceneView.mouseOverWindow`(若存在)做补强.
            // - GameView: 仍然要求 over==focused,避免仅仅扫过 tab/标题栏就把 hint 错切到 GameView.
            // ----------------------------------------------------------------
            if (over is UnityEditor.SceneView overSceneView && overSceneView != null)
            {
                m_editorViewportHint = 1;
                if (overSceneView.camera)
                    m_editorViewportSceneViewCameraHint = overSceneView.camera;
                return 1;
            }

            // SceneView 的补强信号:
            // - 当 `mouseOverWindow==null` 或返回“非 SceneView 的内部窗口”时,尝试读取 SceneView.mouseOverWindow.
            // - 该 API 并非所有 Unity 版本都有,因此用反射 + 容错实现.
            if (!overIsGameView && over is not UnityEditor.SceneView &&
                TryGetMouseOverSceneViewWindow(out var mouseOverSceneView) &&
                mouseOverSceneView != null)
            {
                m_editorViewportHint = 1;
                if (mouseOverSceneView.camera)
                    m_editorViewportSceneViewCameraHint = mouseOverSceneView.camera;
                return 1;
            }

            // 关键: `mouseOverWindow==null` 往往是“鼠标锁定/拖拽中/不在 Unity 窗口内”等歧义状态.
            // 在这种状态下,若没有任何强信号表明在 SceneView,就沿用缓存 hint,避免抖动到 GameView.
            if (over == null && m_editorViewportHint != 0)
                return m_editorViewportHint;

            // 注意: 这里要求 over==focused,避免仅仅“鼠标划过 tab/标题栏”就把 hint 切到 GameView.
            // 否则在 SceneView 旋转时,只要鼠标扫过 GameView 标签,ActiveCamera 就会被错误切走,导致闪烁.
            if (over == focused && IsGameViewWindow(over))
            {
                m_editorViewportHint = 2;
                m_editorViewportSceneViewCameraHint = null;
                return 2;
            }

            // 鼠标在其它 UI 上(Inspector/Hierarchy...)时,不要根据 focusedWindow 变更 hint.
            if (over != null)
            {
                if (m_editorViewportHint != 0)
                    return m_editorViewportHint;

                if (TryGetAnySceneViewCamera())
                {
                    m_editorViewportHint = 1;
                    m_editorViewportSceneViewCameraHint = null;
                    return 1;
                }

                return 0;
            }

            if (TryGetSceneViewCameraFromWindow(focused, out var focusedSceneCam))
            {
                m_editorViewportHint = 1;
                m_editorViewportSceneViewCameraHint = focusedSceneCam;
                return 1;
            }

            if (IsGameViewWindow(focused))
            {
                m_editorViewportHint = 2;
                m_editorViewportSceneViewCameraHint = null;
                return 2;
            }

            if (m_editorViewportHint != 0)
                return m_editorViewportHint;

            // 初始兜底: 如果场景里有 SceneView,默认选 SceneView.
            if (TryGetAnySceneViewCamera())
            {
                m_editorViewportHint = 1;
                m_editorViewportSceneViewCameraHint = null;
                return 1;
            }
#endif
            return 0;
        }

        /// <summary>
        /// 获取当前帧的 ActiveCamera(仅在 CameraMode=ActiveCameraOnly 时有意义).
        /// </summary>
        public bool TryGetActiveCamera(out Camera cam)
        {
            cam = null;

            var settings = GsplatSettings.Instance;
            if (!settings || settings.CameraMode != GsplatCameraMode.ActiveCameraOnly)
                return false;

            var isPlaying = Application.isPlaying;
            var canUseFrameCache = !Application.isBatchMode;

            // ----------------------------------------------------------------
            // 显式 override 的优先级:
            // - spec 要求 override 相机存在且有效时,ActiveCamera MUST 为 override 相机.
            // - 这也能避免 Editor 下“焦点/鼠标窗口信号不稳定”时把 ActiveCamera 错切走.
            // ----------------------------------------------------------------
            var overrideCam = m_activeGameCameraOverride;
            if (overrideCam && overrideCam.isActiveAndEnabled && overrideCam.cameraType != CameraType.Preview)
            {
                if (canUseFrameCache)
                {
                    m_cachedActiveCameraFrame = Time.frameCount;
                    m_cachedActiveCameraIsPlaying = isPlaying;
                    m_cachedActiveCameraMode = settings.CameraMode;
                    m_cachedActiveCameraOverride = overrideCam;
                    m_cachedActiveCamera = overrideCam;
#if UNITY_EDITOR
                    m_cachedEditorFocusHint = 0;
#endif
                }

                m_lastResolvedActiveCamera = overrideCam;
                cam = overrideCam;
                return true;
            }

            var focusHint = GetEditorFocusHintNonAlloc();

            // 缓存命中条件:
            // - 同一帧(Time.frameCount)内多次调用
            // - CameraMode/override/play 状态不变
            // - EditMode 下窗口焦点不变(避免在同一帧里焦点切换却还用旧结果)
            //
            // 注意:
            // - 在 batchmode 下,Time.frameCount 可能不稳定或几乎不增长.
            //   此时如果缓存的是 null,会导致后续解析永远命中缓存并返回 false.
            // - 因此:
            //   1) batchmode 下禁用“按帧缓存”
            //   2) 只在缓存值为有效 Camera 时才允许命中缓存
            if (canUseFrameCache &&
                m_cachedActiveCameraFrame == Time.frameCount &&
                m_cachedActiveCameraMode == settings.CameraMode &&
                m_cachedActiveCameraOverride == m_activeGameCameraOverride &&
                m_cachedActiveCameraIsPlaying == isPlaying &&
#if UNITY_EDITOR
                m_cachedEditorFocusHint == focusHint &&
#endif
                m_cachedActiveCamera)
            {
                cam = m_cachedActiveCamera;
                return cam;
            }

            Camera resolved = null;

#if UNITY_EDITOR
            // EditMode: 默认保证 SceneView 稳定,并允许在聚焦 GameView 时预览 Game/VR 相机.
            if (!isPlaying)
            {
                // ----------------------------------------------------------------
                // EditMode ActiveCamera 选择策略(稳态):
                // - 目标:
                //   - SceneView: 鼠标在 overlay/UIElements 区域移动时不闪烁.
                //   - GameView: 用户明确“切到 GameView 并聚焦”时,可以看到 Gsplat.
                //
                // 经验:
                // - `mouseOverWindow` 在 overlay/UIElements 场景下可能为 null 或非 SceneView,属于噪声信号.
                // - `focusedWindow` 更像用户的显式意图(你点了哪个视窗),作为“切到 GameView”的强信号更可靠.
                //
                // 规则:
                // - 如果 GameView 聚焦: 选择 Game/VR 相机.
                // - 否则: 默认选择 SceneView 相机(若存在).
                // - 若用户希望在聚焦其它窗口(Inspector/Console...)时仍以 GameView 为准,请使用显式 override(`GsplatActiveCameraOverride`).
                // ----------------------------------------------------------------
                // 关键修复:
                // - 仅用 `focusedWindow` 会导致一个非常常见的 UX 问题:
                //   - 你在 GameView 里预览画面,
                //   - 然后去 Inspector 拖动 `TimeNormalized` 滑条,
                //   - 焦点会落到 Inspector,ActiveCamera 立刻切回 SceneView,
                //   - 于是 Game camera 的 sort/draw 被 gate 掉,体感为“GameView 突然全消失”.
                // - 因此这里改为使用 `GetEditorFocusHintNonAlloc()` 的 viewport hint:
                //   - 你明确点击过 GameView 后,hint 会保持为 GameView(即便鼠标在 Inspector 上),
                //     直到你再次与 SceneView 交互.
                if (focusHint == 2)
                    resolved = ResolveActiveGameOrVrCamera();

                if (!resolved)
                {
                    if (m_editorViewportSceneViewCameraHint)
                        resolved = m_editorViewportSceneViewCameraHint;
                    else
                        resolved = TryGetAnySceneViewCamera();
                }

                // 兜底:
                // - 极少数环境/批处理下可能没有 SceneView,这里回退为上一帧或 Game/VR 相机.
                if (!resolved)
                {
                    if (m_lastResolvedActiveCamera)
                        resolved = m_lastResolvedActiveCamera;
                    else
                        resolved = ResolveActiveGameOrVrCamera();
                }

                DebugLogActiveCameraEditMode(focusHint, resolved);
            }
#endif

            // Play/Player build: 始终选择 Game/VR 相机,并忽略 SceneView 焦点.
            // - 这里也覆盖了“非 Editor 环境”场景.
            if (!resolved)
                resolved = ResolveActiveGameOrVrCamera();

            m_cachedActiveCameraFrame = Time.frameCount;
            m_cachedActiveCameraIsPlaying = isPlaying;
            m_cachedActiveCameraMode = settings.CameraMode;
            m_cachedActiveCameraOverride = m_activeGameCameraOverride;
            m_cachedActiveCamera = resolved;
#if UNITY_EDITOR
            m_cachedEditorFocusHint = focusHint;
#endif

            if (resolved)
                m_lastResolvedActiveCamera = resolved;

            cam = resolved;
            return cam;
        }

        public void InitSorter(ComputeShader computeShader)
        {
            m_sortPass = computeShader ? new GsplatSortPass(computeShader) : null;
        }

        public void RegisterGsplat(IGsplat gsplat)
        {
            if (m_gsplats.Count == 0)
            {
                // 说明:
                // - 我们同时订阅 BiRP 与 SRP 的入口,并在回调里用 currentRenderPipeline 做门禁.
                // - 这样可以避免“在运行期切换渲染管线/切换项目设置”导致入口不一致.
                Camera.onPreCull += OnPreCullCamera;
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;

#if UNITY_EDITOR
                EnsureSceneViewDuringGuiHook();
#endif
            }

            m_gsplats.Add(gsplat);

            // 注册新对象可能改变“最后一次可见的 ActiveCamera”语义,
            // 这里清一次缓存,确保下一次回调用到的是最新判断.
            InvalidateActiveCameraCache();
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
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

#if UNITY_EDITOR
            RemoveSceneViewDuringGuiHook();
#endif

            InvalidateActiveCameraCache();
        }

        public bool GatherGsplatsForCamera(Camera cam)
        {
            return GatherGsplatsForCamera(cam, out _);
        }

        public bool GatherGsplatsForCamera(Camera cam, out string skipReason)
        {
            skipReason = null;

            if (!cam)
            {
                skipReason = "Camera is null";
                return false;
            }

            if (cam.cameraType == CameraType.Preview)
            {
                skipReason = "CameraType.Preview";
                return false;
            }

            // ----------------------------------------------------------------
            // ActiveCameraOnly: 只允许 ActiveCamera 触发排序.
            // - 这是“多相机性能”最关键的门禁点.
            // - 注意: 我们在这里就 return false,避免后续遍历 gsplats 的额外开销.
            // ----------------------------------------------------------------
            var settings = GsplatSettings.Instance;
            if (settings && settings.CameraMode == GsplatCameraMode.ActiveCameraOnly)
            {
#if UNITY_EDITOR
                // Editor 非 Play 模式下的特殊处理(关键修复):
                // - 用户反馈: 强旋转 SceneView 时,SceneView 会出现“显示/不显示”闪烁,并且转到背后时像没排序.
                // - 进一步分析后发现: Unity 在某些编辑器交互链路里,SceneView 实际参与渲染的 Camera
                //   可能与 `SceneView.lastActiveSceneView.camera` 不是同一个实例(或存在短暂抖动).
                // - 如果我们仍然用“Camera 实例必须完全相等”做门禁,就会出现:
                //   - SceneView 这一帧完全没有排序(看起来像没 sort)
                //   - 叠加渲染门禁,变成整体“显示/不显示”闪烁
                //
                // 解决策略:
                // - 对于 SceneView 相机: EditMode 下允许它触发排序,不依赖 ActiveCamera 的实例相等.
                // - 对于非 SceneView 相机: 仍保持 ActiveCameraOnly 的严格门禁(必须等于 ActiveCamera).
                // - 通过 per-frame guard 避免 SceneView 在同一帧被重复排序.
                if (!Application.isPlaying && cam.cameraType == CameraType.SceneView)
                {
                    // ----------------------------------------------------------------
                    // override MUST win:
                    // - 当 ActiveCamera 被解析为 Game/VR(例如 override 或 GameView 聚焦)时,排序必须跟随该相机.
                    // - 如果仍允许 SceneView 在 EditMode 抢排序,会把 OrderBuffer 刷成“SceneView 视角的排序”,
                    //   导致 GameView 出现排序错乱/闪烁(看起来像随机消失).
                    // ----------------------------------------------------------------
                    if (TryGetActiveCamera(out var activeCam) && activeCam && activeCam.cameraType != CameraType.SceneView)
                    {
                        skipReason = "ActiveCameraOnly(EditMode): SceneView blocked(active camera is Game/VR)";
                        return false;
                    }

                    if (m_activeCameraOnlyEditModeSortedFrame == Time.frameCount)
                    {
                        skipReason = "ActiveCameraOnly(EditMode): SceneView already sorted this frame";
                        return false;
                    }

                    m_activeCameraOnlyEditModeSortedFrame = Time.frameCount;
                }
                else
#endif
                {
                    if (!TryGetActiveCamera(out var activeCam) || activeCam != cam)
                    {
                        skipReason = "ActiveCameraOnly: camera is not ActiveCamera";
                        return false;
                    }
                }
            }

            // ----------------------------------------------------------------
            // Editor Play Mode 的一个常见性能坑:
            // - Play 模式时,GameView 与 SceneView 往往会同时渲染.
            // - HDRP 下 CustomPass 以及 Builtin 的 Camera.onPreCull 都是“按相机触发”.
            // - 这会导致同一帧内对两个相机各做一次 GPU 排序,看起来就像“Play 模式 AutoPlay 不流畅”.
            // 因此这里提供一个 settings 开关,在 Play 模式下可选择跳过 SceneView 相机的排序.
            // ----------------------------------------------------------------
            if (Application.isPlaying && settings && settings.CameraMode == GsplatCameraMode.AllCameras &&
                settings.SkipSceneViewSortingInPlayMode &&
                cam.cameraType == CameraType.SceneView)
            {
#if UNITY_EDITOR
                if (settings.AllowSceneViewSortingWhenFocusedInPlayMode && IsFocusedSceneViewCamera(cam))
                {
                    // SceneView 聚焦时允许排序,保证你交互时显示正确.
                }
                else
                {
                    skipReason = "PlayMode: SkipSceneViewSortingInPlayMode";
                    return false;
                }
#else
                skipReason = "PlayMode: SkipSceneViewSortingInPlayMode";
                return false;
#endif
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
            if (m_activeGsplats.Count == 0)
                skipReason = "No active gsplats for this camera(cullingMask/inactive/invalid)";
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

#if UNITY_EDITOR
        void SubmitEditModeDrawForCamera(Camera camera)
        {
            // 说明:
            // - 仅用于 Editor 非 Play 模式,解决“同一帧多次 BeginCameraRendering 但 draw 只提交一次”导致的闪烁.
            // - Play 模式仍由各 renderer 的 Update 提交 draw,避免重复渲染.
            if (Application.isPlaying)
                return;

            var settings = GsplatSettings.Instance;
            if (!settings || settings.CameraMode != GsplatCameraMode.ActiveCameraOnly)
                return;

            if (!camera)
                return;

            // 避免污染 Preview/Reflection 等内部相机.
            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
                return;

            // ActiveCameraOnly 的渲染门禁(与排序类似,但不包含“每帧只排序一次”的 guard):
            // - Active=SceneView: 允许所有 SceneView 相机渲染(规避实例抖动/多窗口).
            // - Active=Game/VR: 只允许 ActiveCamera 渲染(override MUST win).
            var activeCamOk = TryGetActiveCamera(out var activeCam) && activeCam;
            if (activeCamOk)
            {
                if (activeCam.cameraType == CameraType.SceneView)
                {
                    if (camera.cameraType != CameraType.SceneView)
                        return;
                }
                else if (activeCam.cameraType == CameraType.Game || activeCam.cameraType == CameraType.VR)
                {
                    if (camera != activeCam)
                        return;
                }
                else
                {
                    return;
                }
            }
            else
            {
                // 兜底: ActiveCamera 解析失败时,至少允许 SceneView 相机渲染,避免“彻底不显示”.
                if (camera.cameraType != CameraType.SceneView)
                    return;
            }

            foreach (var gs in m_gsplats)
            {
                // 注意: 这里不复用 m_activeGsplats,因为 gather 的 sort guard 可能导致它不更新.
                if (gs is not { isActiveAndEnabled: true, Valid: true })
                    continue;

                if (gs is IGsplatRenderSubmitter submitter)
                    submitter.SubmitDrawForCamera(camera);
            }
        }
#endif

        void OnPreCullCamera(Camera camera)
        {
            GsplatEditorDiagnostics.MarkCameraRendering(camera, "OnPreCull");

            // BiRP only.
            if (GraphicsSettings.currentRenderPipeline)
            {
                GsplatEditorDiagnostics.MarkSortSkipped(camera, "Not BiRP");
                return;
            }

            var settings = GsplatSettings.Instance;
            var didGather = false;
            string gatherSkipReason = null;
            if (Valid && settings && settings.Valid)
                didGather = GatherGsplatsForCamera(camera, out gatherSkipReason);

            if (!Valid || !settings || !settings.Valid || !didGather)
            {
                if (!Valid)
                    GsplatEditorDiagnostics.MarkSortSkipped(camera, "Sorter invalid");
                else if (!settings || !settings.Valid)
                    GsplatEditorDiagnostics.MarkSortSkipped(camera, "Settings invalid");
                else
                    GsplatEditorDiagnostics.MarkSortSkipped(camera, gatherSkipReason ?? "GatherGsplatsForCamera returned false");
                return;
            }

            InitialClearCmdBuffer(camera);
            DispatchSort(m_commandBuffer, camera);
            GsplatEditorDiagnostics.MarkSortDispatched(camera, m_activeGsplats.Count);
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            GsplatEditorDiagnostics.MarkCameraRendering(camera, "BeginCameraRendering");

            // SRP only.
            if (!GraphicsSettings.currentRenderPipeline)
            {
                GsplatEditorDiagnostics.MarkSortSkipped(camera, "Not SRP");
                return;
            }

            var settings = GsplatSettings.Instance;
            var didGather = false;
            string gatherSkipReason = null;
            if (Valid && settings && settings.Valid)
                didGather = GatherGsplatsForCamera(camera, out gatherSkipReason);

            if (!Valid || !settings || !settings.Valid || !didGather)
            {
                if (!Valid)
                    GsplatEditorDiagnostics.MarkSortSkipped(camera, "Sorter invalid");
                else if (!settings || !settings.Valid)
                    GsplatEditorDiagnostics.MarkSortSkipped(camera, "Settings invalid");
                else
                    GsplatEditorDiagnostics.MarkSortSkipped(camera, gatherSkipReason ?? "GatherGsplatsForCamera returned false");
            }
            else
            {
                // SRP 下没有 CameraEvent 注入,这里直接执行 CommandBuffer.
                InitialClearCmdBuffer(camera);
                DispatchSort(m_commandBuffer, camera);
                GsplatEditorDiagnostics.MarkSortDispatched(camera, m_activeGsplats.Count);
                context.ExecuteCommandBuffer(m_commandBuffer);
                m_commandBuffer.Clear();
            }

#if UNITY_EDITOR
            // 关键补强:
            // - EditMode 下,同一帧可能多次触发 beginCameraRendering.
            // - 如果 draw 只在 Update 内提交,可能只覆盖其中一次,导致闪烁.
            // - 这里在相机回调里补交一次 draw,确保每次相机渲染都能看到 splats.
            SubmitEditModeDrawForCamera(camera);
#endif
        }

        public void DispatchSort(CommandBuffer cmd, Camera camera)
        {
            foreach (var gs in m_activeGsplats)
            {
                var res = (Resource)gs.SorterResource;
                if (gs == null || res == null)
                    continue;

                var baseIndex = gs.SplatBaseIndex;
                var sortCount = gs.SplatCount;
                if (sortCount == 0)
                    continue;

                // 安全门禁: 防止 baseIndex/count 越界导致 compute shader 读越界.
                // - Order/Position 等 buffers 的 count 理论上应一致,这里用最小值做保守兜底.
                var total = (uint)Mathf.Min(res.PositionBuffer.count, res.OrderBuffer.count);
                if (baseIndex >= total)
                    continue;

                var maxCount = total - baseIndex;
                if (sortCount > maxCount)
                    sortCount = maxCount;

                // payload 初始化:
                // - payload 里存的是 local index(0..sortCount-1).
                // - 当 sortCount 变化时必须重置,否则旧 payload 可能包含 >=sortCount 的值,导致越界.
                if (res.PayloadInitializedCount != sortCount)
                {
                    m_sortPass.InitPayload(cmd, res.OrderBuffer, sortCount);
                    res.PayloadInitializedCount = sortCount;
                }

                var sorterArgs = new GsplatSortPass.Args
                {
                    Count = sortCount,
                    BaseIndex = baseIndex,
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
