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
                nameof(GsplatSequenceRenderer.LidarApertureMode),
                nameof(GsplatSequenceRenderer.LidarFrustumCamera),
                nameof(GsplatSequenceRenderer.LidarOrigin),
                nameof(GsplatSequenceRenderer.LidarExternalStaticTargets),
                nameof(GsplatSequenceRenderer.LidarExternalDynamicTargets),
                nameof(GsplatSequenceRenderer.LidarExternalDynamicUpdateHz),
                nameof(GsplatSequenceRenderer.LidarExternalCaptureResolutionMode),
                nameof(GsplatSequenceRenderer.LidarExternalCaptureResolutionScale),
                nameof(GsplatSequenceRenderer.LidarExternalCaptureResolution),
                nameof(GsplatSequenceRenderer.LidarExternalTargetVisibilityMode),
                nameof(GsplatSequenceRenderer.LidarRotationHz),
                nameof(GsplatSequenceRenderer.LidarEnableScanMotion),
                nameof(GsplatSequenceRenderer.LidarUpdateHz),
                nameof(GsplatSequenceRenderer.LidarAzimuthBins),
                nameof(GsplatSequenceRenderer.LidarUpFovDeg),
                nameof(GsplatSequenceRenderer.LidarDownFovDeg),
                nameof(GsplatSequenceRenderer.LidarBeamCount),
                nameof(GsplatSequenceRenderer.LidarDepthNear),
                nameof(GsplatSequenceRenderer.LidarDepthFar),
                nameof(GsplatSequenceRenderer.LidarPointRadiusPixels),
                nameof(GsplatSequenceRenderer.LidarExternalHitBiasMeters),
                nameof(GsplatSequenceRenderer.LidarParticleAntialiasingMode),
                nameof(GsplatSequenceRenderer.LidarParticleAAFringePixels),
                nameof(GsplatSequenceRenderer.LidarShowDuration),
                nameof(GsplatSequenceRenderer.LidarHideDuration),
                nameof(GsplatSequenceRenderer.LidarShowHideWarpPixels),
                nameof(GsplatSequenceRenderer.LidarShowHideNoiseScale),
                nameof(GsplatSequenceRenderer.LidarShowHideNoiseSpeed),
                nameof(GsplatSequenceRenderer.LidarShowHideGlowColor),
                nameof(GsplatSequenceRenderer.LidarShowGlowIntensity),
                nameof(GsplatSequenceRenderer.LidarHideGlowIntensity),
                nameof(GsplatSequenceRenderer.LidarColorMode),
                nameof(GsplatSequenceRenderer.LidarTrailGamma),
                nameof(GsplatSequenceRenderer.LidarIntensity),
                nameof(GsplatSequenceRenderer.LidarIntensityDistanceDecayMode),
                nameof(GsplatSequenceRenderer.LidarKeepUnscannedPoints),
                nameof(GsplatSequenceRenderer.LidarUnscannedIntensity),
                nameof(GsplatSequenceRenderer.LidarIntensityDistanceDecay),
                nameof(GsplatSequenceRenderer.LidarUnscannedIntensityDistanceDecay),
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

            // RadarScan 的开关淡入淡出时长:
            // - 即便当前未启用 EnableLidarScan,也允许用户先把时长调好,再用按钮开启雷达.
            EditorGUI.indentLevel++;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Transition", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarShowDuration)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarHideDuration)));
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
            var apertureProp = serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarApertureMode));
            EditorGUILayout.PropertyField(apertureProp);
            var useFrustumCamera = apertureProp.enumValueIndex == (int)GsplatLidarApertureMode.CameraFrustum;
            if (useFrustumCamera)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarFrustumCamera)));
                EditorGUILayout.HelpBox(
                    "sensor-frame 契约:\n" +
                    "- frustum camera 直接提供 sensor origin + rotation + projection.\n" +
                    "- 当前模式下 LidarOrigin 不再是必填原点.\n" +
                    "- 这一步先把现有 LiDAR compute/draw/external pose 对齐到该 camera.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarOrigin)));
                EditorGUILayout.HelpBox(
                    "sensor-frame 契约:\n" +
                    "- Surround360 模式继续使用 LidarOrigin 的位置+朝向作为 LiDAR 安装位姿.\n" +
                    "- 如果这里为空,LiDAR 点云不会生成.",
                    MessageType.Info);
            }

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarExternalStaticTargets)),
                includeChildren: true);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarExternalDynamicTargets)),
                includeChildren: true);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarExternalDynamicUpdateHz)));
            if (useFrustumCamera)
            {
                var captureResolutionModeProp =
                    serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarExternalCaptureResolutionMode));
                var captureResolutionScaleProp =
                    serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarExternalCaptureResolutionScale));
                var explicitCaptureResolutionProp =
                    serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarExternalCaptureResolution));

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
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarExternalTargetVisibilityMode)));
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
                    var frustumCameraProp = serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarFrustumCamera));
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
                    var originProp = serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarOrigin));
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
                var enableScanMotionProp =
                    serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarEnableScanMotion));
                EditorGUILayout.PropertyField(enableScanMotionProp);
                using (new EditorGUI.DisabledScope(!enableScanMotionProp.boolValue &&
                                                   !enableScanMotionProp.hasMultipleDifferentValues))
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarRotationHz)));
                }
                EditorGUILayout.HelpBox(
                    "提示:\n" +
                    "- `LidarEnableScanMotion` 控制的是扫描头旋转 + trail 余辉,不是 LiDAR 点云本体.\n" +
                    "- 关闭后会直接显示稳定的雷达粒子,适合只看规则点云分布,不想保留扫描动作的场景.\n" +
                    "- 关闭后 `LidarRotationHz`、`LidarTrailGamma` 与未扫到底色相关参数会失去实际作用.",
                    MessageType.Info);

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
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarExternalHitBiasMeters)));
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarParticleAntialiasingMode)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarParticleAAFringePixels)));
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
                using (new EditorGUI.DisabledScope(!enableScanMotionProp.boolValue &&
                                                   !enableScanMotionProp.hasMultipleDifferentValues))
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarTrailGamma)));
                }
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarIntensity)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarIntensityDistanceDecayMode)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarIntensityDistanceDecay)));
                var keepUnscannedProp =
                    serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarKeepUnscannedPoints));
                using (new EditorGUI.DisabledScope(!enableScanMotionProp.boolValue &&
                                                   !enableScanMotionProp.hasMultipleDifferentValues))
                {
                    EditorGUILayout.PropertyField(keepUnscannedProp);
                }
                using (new EditorGUI.DisabledScope(
                           (!enableScanMotionProp.boolValue &&
                            !enableScanMotionProp.hasMultipleDifferentValues) ||
                           (!keepUnscannedProp.boolValue && !keepUnscannedProp.hasMultipleDifferentValues)))
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarUnscannedIntensity)));
                    EditorGUILayout.PropertyField(
                        serializedObject.FindProperty(nameof(GsplatSequenceRenderer.LidarUnscannedIntensityDistanceDecay)));
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
                "使用下方按钮可播放动画切换:\n" +
                "- Gaussian/ParticleDots: 时长=RenderStyleSwitchDurationSeconds(默认 easeInOutQuart).\n" +
                "- RadarScan: RenderStyle 仍用 RenderStyleSwitchDurationSeconds,但雷达淡入/淡出优先用 LidarShowDuration/LidarHideDuration(>=0),否则复用 RenderStyleSwitchDurationSeconds.\n" +
                "- show-hide-switch-高斯: 走双轨 overlap 切换. 雷达先完整执行 visibility hide,高斯在 hide 过半前一点开始 show,中段允许两者同屏,hide 结束后才关闭 LiDAR.\n" +
                "- show-hide-switch-雷达: 走反向双轨 overlap 切换. 高斯先执行 visibility hide,到共享阈值后开始 Radar show,高斯 hide 完成后再落到稳定 RadarScan.\n" +
                "- 两个 dual-track 按钮共用 DualTrackSwitchTriggerProgress01,默认 0.35.",
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

                if (GUILayout.Button("show-hide-switch-高斯"))
                {
                    foreach (var obj in targets)
                    {
                        var r = obj as GsplatSequenceRenderer;
                        if (!r)
                            continue;
                        r.PlayRadarScanToGaussianShowHideSwitch(durationSeconds: -1.0f);
                        EditorUtility.SetDirty(r);
                    }

                    EditorApplication.QueuePlayerLoopUpdate();
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }

                if (GUILayout.Button("show-hide-switch-雷达"))
                {
                    foreach (var obj in targets)
                    {
                        var r = obj as GsplatSequenceRenderer;
                        if (!r)
                            continue;
                        r.PlayGaussianToRadarScanShowHideSwitch(durationSeconds: -1.0f);
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
