// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using UnityEditor;
using UnityEngine;

namespace Gsplat.Editor
{
    [CustomEditor(typeof(GsplatRenderer))]
    public class GsplatRendererEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPropertiesExcluding(serializedObject, "m_Script", nameof(GsplatRenderer.UploadBatchSize),
                nameof(GsplatRenderer.RenderBeforeUploadComplete),
                // LiDAR: 这里做一个更清晰的调参区,避免字段散落在默认绘制里.
                nameof(GsplatRenderer.EnableLidarScan),
                nameof(GsplatRenderer.LidarOrigin),
                nameof(GsplatRenderer.LidarRotationHz),
                nameof(GsplatRenderer.LidarUpdateHz),
                nameof(GsplatRenderer.LidarAzimuthBins),
                nameof(GsplatRenderer.LidarUpFovDeg),
                nameof(GsplatRenderer.LidarDownFovDeg),
                nameof(GsplatRenderer.LidarBeamCount),
                nameof(GsplatRenderer.LidarDepthNear),
                nameof(GsplatRenderer.LidarDepthFar),
                nameof(GsplatRenderer.LidarPointRadiusPixels),
                nameof(GsplatRenderer.LidarShowDuration),
                nameof(GsplatRenderer.LidarHideDuration),
                nameof(GsplatRenderer.LidarShowHideWarpPixels),
                nameof(GsplatRenderer.LidarShowHideNoiseScale),
                nameof(GsplatRenderer.LidarShowHideNoiseSpeed),
                nameof(GsplatRenderer.LidarShowHideGlowColor),
                nameof(GsplatRenderer.LidarShowGlowIntensity),
                nameof(GsplatRenderer.LidarHideGlowIntensity),
                nameof(GsplatRenderer.LidarColorMode),
                nameof(GsplatRenderer.LidarTrailGamma),
                nameof(GsplatRenderer.LidarIntensity),
                nameof(GsplatRenderer.LidarDepthOpacity),
                nameof(GsplatRenderer.LidarMinSplatOpacity),
                nameof(GsplatRenderer.HideSplatsWhenLidarEnabled));
            
            if (serializedObject.FindProperty(nameof(GsplatRenderer.AsyncUpload)).boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.UploadBatchSize)));
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty(nameof(GsplatRenderer.RenderBeforeUploadComplete)));
                EditorGUI.indentLevel--;
            }

            // ----------------------------------------------------------------
            // LiDAR 调参区:
            // - 目标: 把 LiDAR 常用参数集中展示,并提供“有效网格尺寸/点数”的即时反馈,便于用户快速调参.
            // - 注意: 这些字段的 clamp/归一化逻辑在 Runtime 的 OnValidate/Update 内完成.
            // ----------------------------------------------------------------
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("LiDAR Scan (Experimental)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "说明:\n" +
                "- 这是一个\"车载 LiDAR 采集观感\"的规则点云显示模式.\n" +
                "- splat 可隐藏但仍作为\"环境采样点\"参与 first return(第一回波).\n" +
                "- EditMode 下会在启用时自动驱动 Repaint,用于扫描前沿/余辉的连续播放.",
                MessageType.Info);

            var enableLidarProp = serializedObject.FindProperty(nameof(GsplatRenderer.EnableLidarScan));
            EditorGUILayout.PropertyField(enableLidarProp);

            if (enableLidarProp.boolValue)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarOrigin)));
                var originProp = serializedObject.FindProperty(nameof(GsplatRenderer.LidarOrigin));
                if (originProp.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "EnableLidarScan=true 但 LidarOrigin 为空.\n" +
                        "LiDAR 点云不会渲染. 请指定一个 Transform(位置+朝向作为 LiDAR 安装位姿).",
                        MessageType.Warning);
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Grid / FOV", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarAzimuthBins)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarUpFovDeg)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarDownFovDeg)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarBeamCount)));

                var azBins = Mathf.Max(serializedObject.FindProperty(nameof(GsplatRenderer.LidarAzimuthBins)).intValue, 0);
                var beamCount = Mathf.Max(serializedObject.FindProperty(nameof(GsplatRenderer.LidarBeamCount)).intValue, 0);
                var pointCount = (long)beamCount * azBins;
                EditorGUILayout.LabelField(
                    $"有效网格: {beamCount} beams x {azBins} azBins (约 {pointCount:N0} 点)");

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Timing", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarUpdateHz)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarRotationHz)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarShowDuration)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarHideDuration)));
                EditorGUILayout.HelpBox(
                    "提示:\n" +
                    "- LidarShowDuration/LidarHideDuration 仅影响 RadarScan 开/关的淡入淡出.\n" +
                    "- < 0 时会复用 RenderStyleSwitchDurationSeconds.\n" +
                    "- 它们不影响高斯/ParticleDots 的显隐燃烧环动画(ShowDuration/HideDuration).",
                    MessageType.Info);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Visual", EditorStyles.boldLabel);
                var colorModeProp = serializedObject.FindProperty(nameof(GsplatRenderer.LidarColorMode));
                EditorGUILayout.PropertyField(colorModeProp);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Depth(动画)"))
                    {
                        serializedObject.ApplyModifiedProperties();
                        foreach (var obj in targets)
                        {
                            var r = obj as GsplatRenderer;
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
                            var r = obj as GsplatRenderer;
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
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarDepthNear)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarDepthFar)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarMinSplatOpacity)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarPointRadiusPixels)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarShowHideWarpPixels)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarShowHideNoiseScale)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarShowHideNoiseSpeed)));
                EditorGUILayout.HelpBox(
                    "提示: LidarShowHideNoiseScale/LidarShowHideNoiseSpeed < 0 时会复用全局 NoiseScale/NoiseSpeed.\n" +
                    "如需 RadarScan 独立调参,把它们改为 >= 0 即可覆盖.",
                    MessageType.Info);
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarShowHideGlowColor)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarShowGlowIntensity)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarHideGlowIntensity)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarTrailGamma)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarIntensity)));
                using (new EditorGUI.DisabledScope(colorModeProp.enumValueIndex != (int)GsplatLidarColorMode.Depth))
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarDepthOpacity)));

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Splat Visibility", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.HideSplatsWhenLidarEnabled)));

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
                "使用下方按钮可播放动画切换:\n" +
                "- Gaussian/ParticleDots: 时长=RenderStyleSwitchDurationSeconds(默认 easeInOutQuart).\n" +
                "- RadarScan: RenderStyle 仍用 RenderStyleSwitchDurationSeconds,但雷达淡入/淡出优先用 LidarShowDuration/LidarHideDuration(>=0),否则复用 RenderStyleSwitchDurationSeconds.",
                MessageType.Info);

            var anyDurationZero = false;
            foreach (var obj in targets)
            {
                var r = obj as GsplatRenderer;
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
                    "注意: 部分对象的 RenderStyleSwitchDurationSeconds <= 0.\n" +
                    "- RenderStyle 切换动画会退化为硬切.\n" +
                    "- RadarScan 的淡入淡出若仍希望有动画,请设置 LidarShowDuration/LidarHideDuration 为 >0(或把 RenderStyleSwitchDurationSeconds 设为 >0).",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Gaussian(动画)"))
                {
                    foreach (var obj in targets)
                    {
                        var r = obj as GsplatRenderer;
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
                        var r = obj as GsplatRenderer;
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
                        var r = obj as GsplatRenderer;
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
                var r = obj as GsplatRenderer;
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
                        var r = obj as GsplatRenderer;
                        if (!r)
                            continue;
                        r.PlayShow();
                        EditorUtility.SetDirty(r);
                    }

                    // 编辑态触发动画时,主动刷新 PlayerLoop + 视图,避免“按了没反应”的错觉.
                    EditorApplication.QueuePlayerLoopUpdate();
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }

                if (GUILayout.Button("Hide"))
                {
                    foreach (var obj in targets)
                    {
                        var r = obj as GsplatRenderer;
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
