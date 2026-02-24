// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Gsplat
{
#if UNITY_EDITOR
    // --------------------------------------------------------------------
    // 说明:
    // - `InitializeOnLoad` 用于确保在 Editor 域重载后,我们能稳定挂上 log 监听.
    // - 目的: 捕获 Metal 下的 "requires a ComputeBuffer ..." 这类跳绘制 warning,
    //   并自动 dump 我们的 ring buffer,让证据可追溯.
    // --------------------------------------------------------------------
    [UnityEditor.InitializeOnLoad]
#endif
    /// <summary>
    /// Editor 闪烁诊断工具.
    ///
    /// 设计目标:
    /// - 默认零开销(关闭时 no-op).
    /// - 开启后收集“相机渲染回调 vs draw 提交”的时序证据,定位闪烁根因:
    ///   - 是否存在 SceneView 相机确实渲染了,但当帧没有提交 draw.
    ///   - 是否 draw 提交到了一个“并未参与该帧渲染”的相机实例(实例抖动/目标不一致).
    ///
    /// 使用方式:
    /// - 在 Project Settings/Gsplat 勾选 EnableEditorDiagnostics.
    /// - 复现一次闪烁,观察 Console/Editor.log 中的 [GsplatDiag] dump.
    /// </summary>
    static class GsplatEditorDiagnostics
    {
        // 说明:
        // - 这里不使用 Conditional 编译宏,而是用 settings 开关进行运行时控制.
        // - 目的: 让用户不需要改 Scripting Define Symbols,也能快速开关日志.
        public static bool Enabled
        {
            get
            {
                var settings = GsplatSettings.Instance;
                return settings && settings.EnableEditorDiagnostics;
            }
        }

#if UNITY_EDITOR
        struct Entry
        {
            public int Frame;
            public double Time;
            public string Message;
        }

        const int k_ringCapacity = 512;
        static readonly Entry[] s_ring = new Entry[k_ringCapacity];
        static int s_ringHead;
        static int s_ringCount;

        static int s_frame = -1;
        static readonly HashSet<int> s_sceneViewRenderedCameras = new();
        static readonly HashSet<int> s_sceneViewDrawnCameras = new();
        static readonly Dictionary<int, int> s_sceneViewRenderCounts = new();
        static readonly Dictionary<int, int> s_sceneViewDrawCounts = new();

        // ----------------------------------------------------------------
        // render serial(诊断用):
        // - Editor 下同一 `Time.frameCount` 可能出现多次 `BeginCameraRendering`.
        // - 我们用一个递增的序号把每一次“相机渲染回调”标记出来,并尝试把 draw 与之关联.
        // - 当 rs 不匹配(= -1)时,通常意味着 draw 是在 Update 中提交的,而不是在相机回调链路里提交的.
        // ----------------------------------------------------------------
        static int s_renderSerial;
        static int s_currentRenderSerial;
        static int s_currentRenderCameraId;

        static double s_lastDumpTime;
        static double s_lastMetalWarningDumpTime;

        static bool s_handlingLog;

        static GsplatEditorDiagnostics()
        {
            // ----------------------------------------------------------------
            // 监听 Editor 日志:
            // - Metal 下如果某个 StructuredBuffer 没绑定,Unity 会跳过 draw call 防止崩溃.
            // - 但该 warning 可能只打印一次,后续静默,导致用户体感为“闪烁但没 log”.
            // - 因此我们在这里监听到该 warning 时,立即 dump 现场.
            // ----------------------------------------------------------------
            Application.logMessageReceived += OnLogMessageReceived;
        }

        static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            // 防止递归: dump 自己会打印 log,会再次进入该回调.
            if (s_handlingLog)
                return;

            if (string.IsNullOrEmpty(condition))
                return;

            // 忽略我们自己的输出,避免误触发.
            if (condition.StartsWith("[GsplatDiag]"))
                return;

            // 只抓最关键的 Metal 跳绘制 warning.
            // 注意: Unity 的文案可能会有小差异,因此用 contains 做容错匹配.
            if (!condition.Contains("Gsplat/Standard") ||
                !condition.Contains("requires a ComputeBuffer at index") ||
                !condition.Contains("Skipping draw calls"))
                return;

            // 限流: 避免同一类 warning 刷屏.
            const double k_minMetalDumpIntervalSeconds = 2.0;
            var now = UnityEditor.EditorApplication.timeSinceStartup;
            if (now - s_lastMetalWarningDumpTime < k_minMetalDumpIntervalSeconds)
                return;
            s_lastMetalWarningDumpTime = now;

            var bufferIndex = TryParseComputeBufferIndex(condition);

            // 这里即使没开 EnableEditorDiagnostics,也至少输出一次映射与提示,避免用户卡住.
            s_handlingLog = true;
            try
            {
                if (Enabled)
                {
                    DumpNow($"metal-warning(index={bufferIndex})");
                }
                else
                {
                    var sb = new StringBuilder(4096);
                    sb.AppendLine("[GsplatDiag] DETECTED: Metal skipped draw calls due to missing ComputeBuffer binding.");
                    sb.AppendLine($"[GsplatDiag] message={condition}");
                    sb.AppendLine($"[GsplatDiag] parsedIndex={bufferIndex}");
                    AppendShaderBufferIndexMap(sb);
                    sb.AppendLine("[GsplatDiag] Note: EnableEditorDiagnostics is OFF. Turn it on in Project Settings/Gsplat for full ring-buffer dump.");
                    Debug.LogWarning(sb.ToString());
                }
            }
            finally
            {
                s_handlingLog = false;
            }
        }

        static int TryParseComputeBufferIndex(string condition)
        {
            // 形如: "... requires a ComputeBuffer at index 3 to be bound ..."
            const string k_token = "requires a ComputeBuffer at index ";
            var start = condition.IndexOf(k_token, StringComparison.Ordinal);
            if (start < 0)
                return -1;

            start += k_token.Length;
            var end = start;
            while (end < condition.Length && char.IsDigit(condition[end]))
                end++;

            if (end <= start)
                return -1;

            if (int.TryParse(condition.Substring(start, end - start), out var value))
                return value;
            return -1;
        }

        static void EnsureFrame()
        {
            var frame = Time.frameCount;
            if (s_frame == frame)
                return;

            // 帧切换意味着上一帧的事件已经收敛,此时可以检查是否出现“SceneView 渲染了但没提交 draw”的异常.
            DumpIfSceneViewRenderedButNotDrawn(s_frame);

            s_frame = frame;
            s_sceneViewRenderedCameras.Clear();
            s_sceneViewDrawnCameras.Clear();
            s_sceneViewRenderCounts.Clear();
            s_sceneViewDrawCounts.Clear();
        }

        static void IncrementCount(Dictionary<int, int> counts, int id)
        {
            if (counts.TryGetValue(id, out var c))
                counts[id] = c + 1;
            else
                counts[id] = 1;
        }

        static void AddEvent(string message)
        {
            EnsureFrame();

            s_ring[s_ringHead] = new Entry
            {
                Frame = Time.frameCount,
                Time = UnityEditor.EditorApplication.timeSinceStartup,
                Message = message
            };

            s_ringHead = (s_ringHead + 1) % k_ringCapacity;
            s_ringCount = Mathf.Min(s_ringCount + 1, k_ringCapacity);
        }

        static string DescribeCamera(Camera cam)
        {
            if (!cam)
                return "null";

            var id = cam.GetInstanceID();

            // 说明:
            // - SceneView 的内部 camera 有时会出现 `enabled=false` / `isActiveAndEnabled=false`,
            //   但它依然会参与 SRP 的渲染回调链路(beginCameraRendering).
            // - 把 enabled 状态一起打印出来,方便定位“我们为何认为它可/不可渲染”的门禁问题.
            var en = cam.enabled ? 1 : 0;
            var act = cam.isActiveAndEnabled ? 1 : 0;
            return $"{cam.cameraType}/{cam.name}#{id} en={en} act={act}";
        }

        static string DescribeWindow(UnityEditor.EditorWindow w)
        {
            if (!w)
                return "null";

            return w.GetType().Name;
        }

        static UnityEditor.SceneView TryGetSceneViewMouseOverWindow()
        {
            // ----------------------------------------------------------------
            // SceneView.mouseOverWindow 并非所有 Unity 版本都有,因此用反射尝试读取.
            // 该值在 overlay/UIElements 场景下可能比 EditorWindow.mouseOverWindow 更可靠.
            // ----------------------------------------------------------------
            var prop = typeof(UnityEditor.SceneView).GetProperty(
                "mouseOverWindow",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (prop == null)
                return null;

            try
            {
                return prop.GetValue(null) as UnityEditor.SceneView;
            }
            catch
            {
                return null;
            }
        }

        static void DumpIfSceneViewRenderedButNotDrawn(int frameToCheck)
        {
            if (!Enabled)
                return;

            // 只在“我们期望 SceneView 稳定渲染”的场景做一致性检查,避免在 AllCameras 等模式下误报.
            if (Application.isPlaying)
                return;

            var settings = GsplatSettings.Instance;
            if (!settings || settings.CameraMode != GsplatCameraMode.ActiveCameraOnly)
                return;

            if (frameToCheck < 0)
                return;

            if (s_sceneViewRenderedCameras.Count == 0)
                return;

            var mismatch = false;
            foreach (var kv in s_sceneViewRenderCounts)
            {
                var id = kv.Key;
                var renderCount = kv.Value;
                var drawCount = s_sceneViewDrawCounts.TryGetValue(id, out var d) ? d : 0;

                // 关键检查:
                // - 过去我们只检查“本帧有没有 draw”.
                // - 但 Editor 下同一帧可能多次渲染同一个 SceneView camera,如果 draw 只提交一次,
                //   就可能导致某些 render invocation 没有 splats,体感为闪烁.
                if (renderCount > drawCount)
                {
                    mismatch = true;
                    break;
                }
            }

            if (!mismatch)
                return;

            // 限流:
            // - 闪烁时可能每帧都触发,不加限流会刷屏到无法用.
            const double k_minDumpIntervalSeconds = 1.0;
            var now = UnityEditor.EditorApplication.timeSinceStartup;
            if (now - s_lastDumpTime < k_minDumpIntervalSeconds)
                return;
            s_lastDumpTime = now;

            var sb = new StringBuilder(8192);
            sb.AppendLine($"[GsplatDiag] DETECTED: SceneView renderCount > drawCount. frame={frameToCheck}");

            sb.Append("[GsplatDiag] renderedSceneViews=");
            AppendIdSet(sb, s_sceneViewRenderedCameras);
            sb.AppendLine();

            sb.Append("[GsplatDiag] drawnSceneViews=");
            AppendIdSet(sb, s_sceneViewDrawnCameras);
            sb.AppendLine();

            sb.Append("[GsplatDiag] sceneView.renderCounts=");
            AppendIdCountMap(sb, s_sceneViewRenderCounts);
            sb.AppendLine();

            sb.Append("[GsplatDiag] sceneView.drawCounts=");
            AppendIdCountMap(sb, s_sceneViewDrawCounts);
            sb.AppendLine();

            var focused = UnityEditor.EditorWindow.focusedWindow;
            var over = UnityEditor.EditorWindow.mouseOverWindow;
            var svOver = TryGetSceneViewMouseOverWindow();
            sb.AppendLine($"[GsplatDiag] focusedWindow={DescribeWindow(focused)} mouseOverWindow={DescribeWindow(over)} sceneView.mouseOverWindow={(svOver ? "SceneView" : "null")}");

            // SceneView 列表快照(帮助判断“我们画的相机”和“真正渲染的相机”是否同源).
            sb.AppendLine($"[GsplatDiag] sceneViews.count={UnityEditor.SceneView.sceneViews.Count}");
            foreach (var v in UnityEditor.SceneView.sceneViews)
            {
                if (v is not UnityEditor.SceneView sv || sv == null)
                    continue;

                sb.AppendLine($"[GsplatDiag] sceneView.camera={DescribeCamera(sv.camera)}");
            }

            sb.AppendLine("[GsplatDiag] recent events (newest last):");
            AppendRingBuffer(sb);

            AppendShaderBufferIndexMap(sb);
            Debug.LogWarning(sb.ToString());
        }

        static void DumpNow(string reason)
        {
            if (!Enabled)
            {
                Debug.LogWarning("[GsplatDiag] EnableEditorDiagnostics is OFF. Turn it on in Project Settings/Gsplat.");
                return;
            }

            EnsureFrame();

            var sb = new StringBuilder(8192);
            sb.AppendLine($"[GsplatDiag] DUMP: reason={reason} frame={s_frame}");

            sb.Append("[GsplatDiag] renderedSceneViews=");
            AppendIdSet(sb, s_sceneViewRenderedCameras);
            sb.AppendLine();

            sb.Append("[GsplatDiag] drawnSceneViews=");
            AppendIdSet(sb, s_sceneViewDrawnCameras);
            sb.AppendLine();

            sb.Append("[GsplatDiag] sceneView.renderCounts=");
            AppendIdCountMap(sb, s_sceneViewRenderCounts);
            sb.AppendLine();

            sb.Append("[GsplatDiag] sceneView.drawCounts=");
            AppendIdCountMap(sb, s_sceneViewDrawCounts);
            sb.AppendLine();

            var focused = UnityEditor.EditorWindow.focusedWindow;
            var over = UnityEditor.EditorWindow.mouseOverWindow;
            var svOver = TryGetSceneViewMouseOverWindow();
            sb.AppendLine($"[GsplatDiag] focusedWindow={DescribeWindow(focused)} mouseOverWindow={DescribeWindow(over)} sceneView.mouseOverWindow={(svOver ? "SceneView" : "null")}");

            sb.AppendLine($"[GsplatDiag] sceneViews.count={UnityEditor.SceneView.sceneViews.Count}");
            foreach (var v in UnityEditor.SceneView.sceneViews)
            {
                if (v is not UnityEditor.SceneView sv || sv == null)
                    continue;

                sb.AppendLine($"[GsplatDiag] sceneView.camera={DescribeCamera(sv.camera)}");
            }

            sb.AppendLine("[GsplatDiag] recent events (newest last):");
            AppendRingBuffer(sb);

            AppendShaderBufferIndexMap(sb);
            Debug.LogWarning(sb.ToString());
        }

        [UnityEditor.MenuItem("Tools/Gsplat/Dump Editor Diagnostics")]
        static void MenuDumpNow()
        {
            DumpNow("menu");
        }

        static void AppendIdSet(StringBuilder sb, HashSet<int> set)
        {
            sb.Append('[');
            var first = true;
            foreach (var id in set)
            {
                if (!first)
                    sb.Append(',');
                first = false;
                sb.Append(id);
            }
            sb.Append(']');
        }

        static void AppendIdCountMap(StringBuilder sb, Dictionary<int, int> counts)
        {
            sb.Append('{');
            var first = true;
            foreach (var kv in counts)
            {
                if (!first)
                    sb.Append(", ");
                first = false;
                sb.Append(kv.Key);
                sb.Append(':');
                sb.Append(kv.Value);
            }
            sb.Append('}');
        }

        static void AppendRingBuffer(StringBuilder sb)
        {
            var start = (s_ringHead - s_ringCount + k_ringCapacity) % k_ringCapacity;
            for (var i = 0; i < s_ringCount; i++)
            {
                var idx = (start + i) % k_ringCapacity;
                var e = s_ring[idx];
                sb.AppendLine($"  t={e.Time:0.000} f={e.Frame} {e.Message}");
            }
        }

        public static void MarkCameraRendering(Camera cam, string phase)
        {
            if (!Enabled || !cam)
                return;

            // 注意: serial 是全局递增,不按帧重置,这样更容易在日志里关联前后文.
            s_renderSerial++;
            s_currentRenderSerial = s_renderSerial;
            s_currentRenderCameraId = cam.GetInstanceID();

            AddEvent($"[CAM_RENDER] rs={s_currentRenderSerial} phase={phase} cam={DescribeCamera(cam)} mask=0x{cam.cullingMask:X}");

            if (cam.cameraType == CameraType.SceneView)
            {
                var id = cam.GetInstanceID();
                s_sceneViewRenderedCameras.Add(id);
                IncrementCount(s_sceneViewRenderCounts, id);
            }
        }

        public static void MarkSortSkipped(Camera cam, string reason)
        {
            if (!Enabled)
                return;

            AddEvent($"[SORT_SKIP] cam={DescribeCamera(cam)} reason={reason}");
        }

        public static void MarkSortDispatched(Camera cam, int activeGsplatCount)
        {
            if (!Enabled)
                return;

            AddEvent($"[SORT] cam={DescribeCamera(cam)} activeGsplats={activeGsplatCount}");
        }

        public static void MarkDrawSubmitted(Camera cam, int layer, int instanceCount, string tag)
        {
            if (!Enabled || !cam)
                return;

            var id = cam.GetInstanceID();
            var rs = id == s_currentRenderCameraId ? s_currentRenderSerial : -1;
            AddEvent($"[DRAW] rs={rs} tag={tag} cam={DescribeCamera(cam)} layer={layer} instances={instanceCount}");

            if (cam.cameraType == CameraType.SceneView)
            {
                s_sceneViewDrawnCameras.Add(id);
                IncrementCount(s_sceneViewDrawCounts, id);
            }
        }

        public static void LogRenderSkipped(string reason)
        {
            if (!Enabled)
                return;

            AddEvent($"[RENDER_SKIP] reason={reason}");
        }

        static void AppendShaderBufferIndexMap(StringBuilder sb)
        {
            // ----------------------------------------------------------------
            // 把 "ComputeBuffer index" 映射回 shader 的属性名,用于定位到底缺的是哪个 buffer.
            //
            // 说明:
            // - Unity/平台的 index 语义不总是直观,所以这里同时打印:
            //   - Shader propertyIndex(0..propertyCount-1)
            //   - 我们枚举到的 buffer 序号 buffer[i]
            // ----------------------------------------------------------------
            var settings = GsplatSettings.Instance;
            if (!settings || !settings.Shader)
            {
                sb.AppendLine("[GsplatDiag] shader buffer map: settings/shader is null");
                return;
            }

            var shader = settings.Shader;
            try
            {
                var propertyCount = shader.GetPropertyCount();
                sb.AppendLine($"[GsplatDiag] shader={shader.name} propertyCount={propertyCount} gfx={SystemInfo.graphicsDeviceType}");

                var bufferIndex = 0;
                for (var propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
                {
                    var type = shader.GetPropertyType(propertyIndex);
                    var typeName = type.ToString();

                    // 兼容不同 Unity 版本的命名: Buffer/ConstantBuffer/ComputeBuffer(如果未来变更).
                    if (typeName.IndexOf("Buffer", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var name = shader.GetPropertyName(propertyIndex);
                        sb.AppendLine($"[GsplatDiag] buffer[{bufferIndex}] propertyIndex={propertyIndex} name={name} type={typeName}");
                        bufferIndex++;
                    }
                }

                // 额外输出我们关心的几个属性的 propertyIndex,方便对照.
                const string k_order = "_OrderBuffer";
                const string k_position = "_PositionBuffer";
                const string k_velocity = "_VelocityBuffer";
                const string k_time = "_TimeBuffer";
                const string k_duration = "_DurationBuffer";
                const string k_scale = "_ScaleBuffer";
                const string k_rotation = "_RotationBuffer";
                const string k_color = "_ColorBuffer";
                const string k_sh = "_SHBuffer";

                sb.AppendLine($"[GsplatDiag] find(_OrderBuffer)={shader.FindPropertyIndex(k_order)}");
                sb.AppendLine($"[GsplatDiag] find(_PositionBuffer)={shader.FindPropertyIndex(k_position)}");
                sb.AppendLine($"[GsplatDiag] find(_VelocityBuffer)={shader.FindPropertyIndex(k_velocity)}");
                sb.AppendLine($"[GsplatDiag] find(_TimeBuffer)={shader.FindPropertyIndex(k_time)}");
                sb.AppendLine($"[GsplatDiag] find(_DurationBuffer)={shader.FindPropertyIndex(k_duration)}");
                sb.AppendLine($"[GsplatDiag] find(_ScaleBuffer)={shader.FindPropertyIndex(k_scale)}");
                sb.AppendLine($"[GsplatDiag] find(_RotationBuffer)={shader.FindPropertyIndex(k_rotation)}");
                sb.AppendLine($"[GsplatDiag] find(_ColorBuffer)={shader.FindPropertyIndex(k_color)}");
                sb.AppendLine($"[GsplatDiag] find(_SHBuffer)={shader.FindPropertyIndex(k_sh)}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[GsplatDiag] shader buffer map failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
#else
        // Player build: no-op
        public static void MarkCameraRendering(Camera cam, string phase) { }
        public static void MarkSortSkipped(Camera cam, string reason) { }
        public static void MarkSortDispatched(Camera cam, int activeGsplatCount) { }
        public static void MarkDrawSubmitted(Camera cam, int layer, int instanceCount, string tag) { }
        public static void LogRenderSkipped(string reason) { }
#endif
    }
}
