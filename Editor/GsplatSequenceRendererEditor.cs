// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEditor;
using UnityEngine;

namespace Gsplat.Editor
{
    [CustomEditor(typeof(GsplatSequenceRenderer))]
    public sealed class GsplatSequenceRendererEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPropertiesExcluding(serializedObject, "m_Script",
                // LiDAR: 这里做一个更清晰的调参区,避免字段散落在默认绘制里.
                nameof(GsplatSequenceRenderer.EnableLidarScan),
                nameof(GsplatSequenceRenderer.LidarOrigin),
                nameof(GsplatSequenceRenderer.LidarRotationHz),
                nameof(GsplatSequenceRenderer.LidarUpdateHz),
                nameof(GsplatSequenceRenderer.LidarAzimuthBins),
                nameof(GsplatSequenceRenderer.LidarUpFovDeg),
                nameof(GsplatSequenceRenderer.LidarDownFovDeg),
                nameof(GsplatSequenceRenderer.LidarBeamCount),
                nameof(GsplatSequenceRenderer.LidarDepthNear),
                nameof(GsplatSequenceRenderer.LidarDepthFar),
                nameof(GsplatSequenceRenderer.LidarPointRadiusPixels),
                nameof(GsplatSequenceRenderer.LidarShowHideWarpPixels),
                nameof(GsplatSequenceRenderer.LidarShowHideNoiseScale),
                nameof(GsplatSequenceRenderer.LidarShowHideNoiseSpeed),
                nameof(GsplatSequenceRenderer.LidarShowHideGlowColor),
                nameof(GsplatSequenceRenderer.LidarShowGlowIntensity),
                nameof(GsplatSequenceRenderer.LidarHideGlowIntensity),
                nameof(GsplatSequenceRenderer.LidarColorMode),
                nameof(GsplatSequenceRenderer.LidarTrailGamma),
                nameof(GsplatSequenceRenderer.LidarIntensity),
                nameof(GsplatSequenceRenderer.LidarDepthOpacity),
                nameof(GsplatSequenceRenderer.LidarMinSplatOpacity),
                nameof(GsplatSequenceRenderer.HideSplatsWhenLidarEnabled));

            // ----------------------------------------------------------------
            // LiDAR 调参区:
            // - 与 `GsplatRenderer` 的 Inspector 行为保持一致,便于用户在静态/序列资产之间切换时复用调参经验.
            // - 序列后端会在每帧 decode 完成后再跑 LiDAR 采样,保证采样对应当前帧插值结果.
            // ----------------------------------------------------------------
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("LiDAR Scan (Experimental)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "说明:\n" +
                "- LiDAR 点云是规则网格(beam x azimuthBin),并具备 first return(第一回波)遮挡语义.\n" +
                "- HideSplatsWhenLidarEnabled 可让你只看点云(不提交 splat sort/draw),但仍会保留 decode 后的 buffers 供采样.\n" +
                "- EditMode 下会在启用时自动驱动 Repaint,用于扫描前沿/余辉的连续播放.",
                MessageType.Info);

            var enableLidarProp = serializedObject.FindProperty(nameof(GsplatSequenceRenderer.EnableLidarScan));
            EditorGUILayout.PropertyField(enableLidarProp);

            if (enableLidarProp.boolValue)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarOrigin)));
                var originProp = serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarOrigin));
                if (originProp.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "EnableLidarScan=true 但 LidarOrigin 为空.\n" +
                        "LiDAR 点云不会渲染. 请指定一个 Transform(位置+朝向作为 LiDAR 安装位姿).",
                        MessageType.Warning);
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Grid / FOV", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarAzimuthBins)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarUpFovDeg)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarDownFovDeg)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarBeamCount)));

                var azBins = Mathf.Max(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarAzimuthBins)).intValue, 0);
                var beamCount = Mathf.Max(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarBeamCount)).intValue, 0);
                var pointCount = (long)beamCount * azBins;
                EditorGUILayout.LabelField(
                    $"有效网格: {beamCount} beams x {azBins} azBins (约 {pointCount:N0} 点)");

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Timing", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarUpdateHz)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarRotationHz)));

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Visual", EditorStyles.boldLabel);
                var colorModeProp = serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarColorMode));
                EditorGUILayout.PropertyField(colorModeProp);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Depth(动画)"))
                    {
                        serializedObject.ApplyModifiedProperties();
                        foreach (var obj in targets)
                        {
                            var r = obj as GsplatSequenceRenderer;
                            if (!r)
                                continue;
                            r.SetLidarColorMode(GsplatLidarColorMode.Depth, animated: true);
                            EditorUtility.SetDirty(r);
                        }

                        EditorApplication.QueuePlayerLoopUpdate();
                        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                        serializedObject.Update();
                    }

                    if (GUILayout.Button("SplatColor(动画)"))
                    {
                        serializedObject.ApplyModifiedProperties();
                        foreach (var obj in targets)
                        {
                            var r = obj as GsplatSequenceRenderer;
                            if (!r)
                                continue;
                            r.SetLidarColorMode(GsplatLidarColorMode.SplatColorSH0, animated: true);
                            EditorUtility.SetDirty(r);
                        }

                        EditorApplication.QueuePlayerLoopUpdate();
                        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                        serializedObject.Update();
                    }
                }
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarDepthNear)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarDepthFar)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarMinSplatOpacity)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarPointRadiusPixels)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarShowHideWarpPixels)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarShowHideNoiseScale)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarShowHideNoiseSpeed)));
                EditorGUILayout.HelpBox(
                    "提示: LidarShowHideNoiseScale/LidarShowHideNoiseSpeed < 0 时会复用全局 NoiseScale/NoiseSpeed.\n" +
                    "如需 RadarScan 独立调参,把它们改为 >= 0 即可覆盖.",
                    MessageType.Info);
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarShowHideGlowColor)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarShowGlowIntensity)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarHideGlowIntensity)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarTrailGamma)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarIntensity)));
                using (new EditorGUI.DisabledScope(colorModeProp.enumValueIndex != (int)GsplatLidarColorMode.Depth))
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarDepthOpacity)));

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Splat Visibility", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.HideSplatsWhenLidarEnabled)));

                EditorGUI.indentLevel--;
            }

            // ----------------------------------------------------------------
            // 重要: 在触发按钮型 API 之前,先把 Inspector 中的修改写回到对象字段.
            // - 否则按钮触发时读取到的 duration/参数可能还是旧值,造成“改了但没生效”的错觉.
            // ----------------------------------------------------------------
            serializedObject.ApplyModifiedProperties();

            // ----------------------------------------------------------------
            // RenderStyle 快捷按钮:
            // - Inspector 的枚举下拉本质是“序列化字段赋值”,默认不播放动画(这是 Unity 的常规行为).
            // - 下面三个按钮会调用 `SetRenderStyleAndRadarScan(...)`,从而统一处理风格动画与雷达模式切换.
            // - 多选 targets 时,对每个对象都触发一次.
            // ----------------------------------------------------------------
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Render Style", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "说明: 直接修改 RenderStyle 枚举是硬切(不会播放切换动画).\n" +
                "使用下方按钮可播放动画切换(默认 easeInOutQuart,时长为 RenderStyleSwitchDurationSeconds).\n" +
                "RadarScan(动画) 会自动切到 ParticleDots 并开启雷达扫描模式.",
                MessageType.Info);

            var anyDurationZero = false;
            foreach (var obj in targets)
            {
                var r = obj as GsplatSequenceRenderer;
                if (!r)
                    continue;
                if (r.RenderStyleSwitchDurationSeconds <= 0.0f || float.IsNaN(r.RenderStyleSwitchDurationSeconds) ||
                    float.IsInfinity(r.RenderStyleSwitchDurationSeconds))
                {
                    anyDurationZero = true;
                    break;
                }
            }

            if (anyDurationZero)
            {
                EditorGUILayout.HelpBox(
                    "注意: 部分对象的 RenderStyleSwitchDurationSeconds <= 0,动画会退化为硬切.\n" +
                    "如需动画,请把 RenderStyleSwitchDurationSeconds 设为大于 0 的值(例如 1.5).",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Gaussian(动画)"))
                {
                    foreach (var obj in targets)
                    {
                        var r = obj as GsplatSequenceRenderer;
                        if (!r)
                            continue;
                        r.SetRenderStyleAndRadarScan(GsplatRenderStyle.Gaussian, enableRadarScan: false,
                            animated: true, durationSeconds: -1.0f);
                        EditorUtility.SetDirty(r);
                    }

                    // 编辑态触发动画时,主动刷新 PlayerLoop + 视图,避免“按了没反应”的错觉.
                    EditorApplication.QueuePlayerLoopUpdate();
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }

                if (GUILayout.Button("ParticleDots(动画)"))
                {
                    foreach (var obj in targets)
                    {
                        var r = obj as GsplatSequenceRenderer;
                        if (!r)
                            continue;
                        r.SetRenderStyleAndRadarScan(GsplatRenderStyle.ParticleDots, enableRadarScan: false,
                            animated: true, durationSeconds: -1.0f);
                        EditorUtility.SetDirty(r);
                    }

                    EditorApplication.QueuePlayerLoopUpdate();
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }

                if (GUILayout.Button("RadarScan(动画)"))
                {
                    foreach (var obj in targets)
                    {
                        var r = obj as GsplatSequenceRenderer;
                        if (!r)
                            continue;
                        r.SetRenderStyleAndRadarScan(GsplatRenderStyle.ParticleDots, enableRadarScan: true,
                            animated: true, durationSeconds: -1.0f);
                        EditorUtility.SetDirty(r);
                    }

                    EditorApplication.QueuePlayerLoopUpdate();
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }
            }

            // ----------------------------------------------------------------
            // Show/Hide 快捷按钮:
            // - 用于快速验证显隐燃烧环动画与参数调节效果.
            // - 多选 targets 时,对每个对象都触发一次.
            // ----------------------------------------------------------------
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Visibility", EditorStyles.boldLabel);

            var anyAnimationDisabled = false;
            foreach (var obj in targets)
            {
                var r = obj as GsplatSequenceRenderer;
                if (!r || r.EnableVisibilityAnimation)
                    continue;
                anyAnimationDisabled = true;
                break;
            }

            if (anyAnimationDisabled)
            {
                EditorGUILayout.HelpBox(
                    "部分对象未启用 EnableVisibilityAnimation. 点击 Show/Hide 时会退化为硬切(不播放燃烧环动画).",
                    MessageType.Info);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Show"))
                {
                    foreach (var obj in targets)
                    {
                        var r = obj as GsplatSequenceRenderer;
                        if (!r)
                            continue;
                        r.PlayShow();
                        EditorUtility.SetDirty(r);
                    }

                    EditorApplication.QueuePlayerLoopUpdate();
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }

                if (GUILayout.Button("Hide"))
                {
                    foreach (var obj in targets)
                    {
                        var r = obj as GsplatSequenceRenderer;
                        if (!r)
                            continue;
                        r.PlayHide();
                        EditorUtility.SetDirty(r);
                    }

                    EditorApplication.QueuePlayerLoopUpdate();
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }
            }
        }
    }
}
