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
                nameof(GsplatRenderer.LidarApertureMode),
                nameof(GsplatRenderer.LidarFrustumCamera),
                nameof(GsplatRenderer.LidarOrigin),
                nameof(GsplatRenderer.LidarExternalStaticTargets),
                nameof(GsplatRenderer.LidarExternalDynamicTargets),
                nameof(GsplatRenderer.LidarExternalDynamicUpdateHz),
                nameof(GsplatRenderer.LidarExternalCaptureResolutionMode),
                nameof(GsplatRenderer.LidarExternalCaptureResolutionScale),
                nameof(GsplatRenderer.LidarExternalCaptureResolution),
                nameof(GsplatRenderer.LidarExternalTargetVisibilityMode),
                nameof(GsplatRenderer.LidarRotationHz),
                nameof(GsplatRenderer.LidarUpdateHz),
                nameof(GsplatRenderer.LidarAzimuthBins),
                nameof(GsplatRenderer.LidarUpFovDeg),
                nameof(GsplatRenderer.LidarDownFovDeg),
                nameof(GsplatRenderer.LidarBeamCount),
                nameof(GsplatRenderer.LidarDepthNear),
                nameof(GsplatRenderer.LidarDepthFar),
                nameof(GsplatRenderer.LidarPointRadiusPixels),
                nameof(GsplatRenderer.LidarExternalHitBiasMeters),
                nameof(GsplatRenderer.LidarParticleAntialiasingMode),
                nameof(GsplatRenderer.LidarParticleAAFringePixels),
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
                nameof(GsplatRenderer.LidarIntensityDistanceDecayMode),
                nameof(GsplatRenderer.LidarKeepUnscannedPoints),
                nameof(GsplatRenderer.LidarUnscannedIntensity),
                nameof(GsplatRenderer.LidarIntensityDistanceDecay),
                nameof(GsplatRenderer.LidarUnscannedIntensityDistanceDecay),
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

            // RadarScan 的开关淡入淡出时长:
            // - 这是“切换按钮”最常调的参数之一,即便当前未启用 EnableLidarScan,也应允许用户先调好再开.
            EditorGUI.indentLevel++;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Transition", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarShowDuration)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarHideDuration)));
            EditorGUILayout.HelpBox(
                "提示:\n" +
                "- LidarShowDuration/LidarHideDuration 仅影响 RadarScan 开/关的淡入淡出.\n" +
                "- < 0 时会复用 RenderStyleSwitchDurationSeconds.\n" +
                "- 它们不影响高斯/ParticleDots 的显隐燃烧环动画(ShowDuration/HideDuration).",
                MessageType.Info);
            EditorGUI.indentLevel--;

            EditorGUI.indentLevel++;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Inputs", EditorStyles.boldLabel);
            var apertureProp = serializedObject.FindProperty(nameof(GsplatRenderer.LidarApertureMode));
            EditorGUILayout.PropertyField(apertureProp);
            var useFrustumCamera = apertureProp.enumValueIndex == (int)GsplatLidarApertureMode.CameraFrustum;
            if (useFrustumCamera)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarFrustumCamera)));
                EditorGUILayout.HelpBox(
                    "sensor-frame 契约:\n" +
                    "- frustum camera 直接提供 sensor origin + rotation + projection.\n" +
                    "- 当前模式下 LidarOrigin 不再是必填原点.\n" +
                    "- 这一步先把现有 LiDAR compute/draw/external pose 对齐到该 camera.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarOrigin)));
                EditorGUILayout.HelpBox(
                    "sensor-frame 契约:\n" +
                    "- Surround360 模式继续使用 LidarOrigin 的位置+朝向作为 LiDAR 安装位姿.\n" +
                    "- 如果这里为空,LiDAR 点云不会生成.",
                    MessageType.Info);
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarExternalStaticTargets)),
                includeChildren: true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarExternalDynamicTargets)),
                includeChildren: true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarExternalDynamicUpdateHz)));
            if (useFrustumCamera)
            {
                var captureResolutionModeProp =
                    serializedObject.FindProperty(nameof(GsplatRenderer.LidarExternalCaptureResolutionMode));
                var captureResolutionScaleProp =
                    serializedObject.FindProperty(nameof(GsplatRenderer.LidarExternalCaptureResolutionScale));
                var explicitCaptureResolutionProp =
                    serializedObject.FindProperty(nameof(GsplatRenderer.LidarExternalCaptureResolution));

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("External Capture", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(captureResolutionModeProp);

                using (new EditorGUI.DisabledScope(
                           captureResolutionModeProp.enumValueIndex !=
                           (int)GsplatLidarExternalCaptureResolutionMode.Scale))
                {
                    EditorGUILayout.PropertyField(captureResolutionScaleProp);
                }

                using (new EditorGUI.DisabledScope(
                           captureResolutionModeProp.enumValueIndex !=
                           (int)GsplatLidarExternalCaptureResolutionMode.Explicit))
                {
                    EditorGUILayout.PropertyField(explicitCaptureResolutionProp);
                }

                EditorGUILayout.HelpBox(
                    "external capture 分辨率说明:\n" +
                    "- 仅在 `CameraFrustum + external GPU capture` 路线生效.\n" +
                    "- Auto: 先取 frustum camera 的 pixelRect; 如果无效,再回退 targetTexture; 最后回退 active LiDAR grid.\n" +
                    "- Scale: 在 Auto 基准上乘倍率,适合 supersample 提高 external mesh 边缘采样精度.\n" +
                    "- Explicit: 直接指定离屏 depth/color capture 的宽高.\n" +
                    "- 这个参数控制的是 external mesh 的离屏采样精度,不会改变 LiDAR 自己的 beam / azimuth 离散语义.",
                    MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarExternalTargetVisibilityMode)));
            EditorGUILayout.HelpBox(
                "说明:\n" +
                "- static/dynamic 两组都会参与 RadarScan.\n" +
                "- static 适合不动的 mesh,dynamic 适合会动的 mesh / skinned mesh.\n" +
                "- 系统会递归收集其子层级中的 MeshRenderer / SkinnedMeshRenderer.\n" +
                "- 旧字段 LidarExternalTargets 会自动迁到 static 组.\n" +
                "- LidarExternalDynamicUpdateHz 是给后续独立 dynamic capture 门禁预留的配置入口.\n" +
                "- ForceRenderingOff: 外部目标继续参与扫描,但不再显示普通 mesh.\n" +
                "- ForceRenderingOffInPlayMode: Play 模式隐藏普通 mesh,编辑器平时仍显示.\n" +
                "- KeepVisible: 外部目标继续显示普通 mesh.\n" +
                "- 留空时保持当前纯 gsplat 扫描行为不变.",
                MessageType.Info);
            EditorGUI.indentLevel--;

            if (enableLidarProp.boolValue)
            {
                EditorGUI.indentLevel++;

                if (useFrustumCamera)
                {
                    var frustumCameraProp = serializedObject.FindProperty(nameof(GsplatRenderer.LidarFrustumCamera));
                    if (frustumCameraProp.objectReferenceValue == null)
                    {
                        EditorGUILayout.HelpBox(
                            "EnableLidarScan=true 且 LidarApertureMode=CameraFrustum, 但 LidarFrustumCamera 为空.\n" +
                            "当前不会渲染 LiDAR 点云. 请指定一个 camera.",
                            MessageType.Warning);
                    }
                }
                else
                {
                    var originProp = serializedObject.FindProperty(nameof(GsplatRenderer.LidarOrigin));
                    if (originProp.objectReferenceValue == null)
                    {
                        EditorGUILayout.HelpBox(
                            "EnableLidarScan=true 且 LidarApertureMode=Surround360, 但 LidarOrigin 为空.\n" +
                            "当前不会渲染 LiDAR 点云. 请指定一个 Transform(位置+朝向作为 LiDAR 安装位姿).",
                            MessageType.Warning);
                    }
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
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarExternalHitBiasMeters)));
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty(nameof(GsplatRenderer.LidarParticleAntialiasingMode)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarParticleAAFringePixels)));
                EditorGUILayout.HelpBox(
                    "RadarScan 粒子 AA 说明:\n" +
                    "- 推荐: AnalyticCoverage. 不依赖 MSAA,现在按像素尺度计算 coverage,小点也更容易看出差异.\n" +
                    "- `LidarParticleAAFringePixels` 用来控制边缘外扩宽度. 值越大,AA fringe 越明显.\n" +
                    "- AlphaToCoverage / AnalyticCoveragePlusAlphaToCoverage 需要当前实际渲染相机具备有效 MSAA.\n" +
                    "- HDRP 下会读取 camera 的 HD Frame Settings / resolved MSAA,不要把 `Camera.allowMSAA` 当成判断依据.\n" +
                    "- A2C 现在走 coverage-first 路线,不是普通透明混合,边缘会更像 sample coverage / cutout.\n" +
                    "- 如果当前 camera 没有 MSAA,运行时会自动回退到 AnalyticCoverage,并在 Console 输出一次说明.\n" +
                    "- LegacySoftEdge 用于保持旧项目当前 fixed feather 的边缘语义.",
                    MessageType.Info);
                EditorGUILayout.HelpBox(
                    "external mesh 命中说明:\n" +
                    "- `LidarExternalHitBiasMeters` 会把 external hit 的粒子沿传感器射线轻微前推.\n" +
                    "- 目的是避免原始 mesh 仍可见时,粒子因为深度太贴而看起来落在模型后面.\n" +
                    "- 默认 0 表示关闭.\n" +
                    "- 如果你仍觉得点被模型压住,可以从 0.01 开始逐步提到 0.02 或 0.05 继续观察.",
                    MessageType.Info);
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
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarIntensityDistanceDecayMode)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarIntensityDistanceDecay)));
                var keepUnscannedProp =
                    serializedObject.FindProperty(nameof(GsplatRenderer.LidarKeepUnscannedPoints));
                EditorGUILayout.PropertyField(keepUnscannedProp);
                using (new EditorGUI.DisabledScope(!keepUnscannedProp.boolValue && !keepUnscannedProp.hasMultipleDifferentValues))
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatRenderer.LidarUnscannedIntensity)));
                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty(nameof(GsplatRenderer.LidarUnscannedIntensityDistanceDecay)));
                }
                EditorGUILayout.HelpBox(
                    "提示: 开启 LidarKeepUnscannedPoints 后,未扫到(或远离扫描头)区域会保留底色亮度.\n" +
                    "你可以用 LidarUnscannedIntensity 控制这层底色,避免\"扫过后变黑\".\n" +
                    "距离衰减: LidarIntensityDistanceDecayMode + (LidarIntensityDistanceDecay/LidarUnscannedIntensityDistanceDecay).\n" +
                    "- Reciprocal: atten(dist)=1/(1+dist*decay).\n" +
                    "- Exponential: atten(dist)=exp(-dist*decay).\n" +
                    "- decay=0 表示不衰减.\n" +
                    "关闭时会保持旧行为: 点云只随余辉(trail)显示,在下一次扫描前会逐渐变暗或消失.",
                    MessageType.Info);
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
