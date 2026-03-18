// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Gsplat.Tests
{
    public sealed class GsplatVisibilityAnimationTests
    {
        const float kRadarToGaussianShowTriggerProgress01 = 0.35f;

        static readonly MethodInfo s_advanceVisibilityStateIfNeeded =
            typeof(GsplatRenderer).GetMethod("AdvanceVisibilityStateIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly MethodInfo s_advanceRenderStyleStateIfNeeded =
            typeof(GsplatRenderer).GetMethod("AdvanceRenderStyleStateIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly MethodInfo s_advanceLidarAnimationStateIfNeeded =
            typeof(GsplatRenderer).GetMethod("AdvanceLidarAnimationStateIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly MethodInfo s_buildLidarShowHideOverlayForThisFrame =
            typeof(GsplatRenderer).GetMethod("BuildLidarShowHideOverlayForThisFrame", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly MethodInfo s_updateSortRangeForTime =
            typeof(GsplatRenderer).GetMethod("UpdateSortRangeForTime", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly MethodInfo s_manualUpdateMethod =
            typeof(GsplatRenderer).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly MethodInfo s_has4DFieldsMethod =
            typeof(GsplatRenderer).GetMethod("Has4DFields", BindingFlags.Static | BindingFlags.NonPublic);

        static readonly FieldInfo s_renderStyleBlend01Field =
            typeof(GsplatRenderer).GetField("m_renderStyleBlend01", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_renderStyleAlphaBlend01Field =
            typeof(GsplatRenderer).GetField("m_renderStyleAlphaBlend01", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_renderStyleAnimatingField =
            typeof(GsplatRenderer).GetField("m_renderStyleAnimating", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_lidarColorBlend01Field =
            typeof(GsplatRenderer).GetField("m_lidarColorBlend01", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_lidarColorAnimatingField =
            typeof(GsplatRenderer).GetField("m_lidarColorAnimating", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_lidarVisibility01Field =
            typeof(GsplatRenderer).GetField("m_lidarVisibility01", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_lidarVisibilityAnimatingField =
            typeof(GsplatRenderer).GetField("m_lidarVisibilityAnimating", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_visibilityStateField =
            typeof(GsplatRenderer).GetField("m_visibilityState", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_visibilityProgress01Field =
            typeof(GsplatRenderer).GetField("m_visibilityProgress01", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_visibilityLastAdvanceRealtimeField =
            typeof(GsplatRenderer).GetField("m_visibilityLastAdvanceRealtime", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_visibilitySourceMaskModeField =
            typeof(GsplatRenderer).GetField("m_visibilitySourceMaskMode", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_visibilitySourceMaskProgressField =
            typeof(GsplatRenderer).GetField("m_visibilitySourceMaskProgress01", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_effectiveSplatCountField =
            typeof(GsplatRenderer).GetField("m_effectiveSplatCount", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_pendingSplatCountField =
            typeof(GsplatRenderer).GetField("m_pendingSplatCount", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_sortSplatBaseIndexThisFrameField =
            typeof(GsplatRenderer).GetField("m_sortSplatBaseIndexThisFrame", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_sortSplatCountThisFrameField =
            typeof(GsplatRenderer).GetField("m_sortSplatCountThisFrame", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_prevAssetField =
            typeof(GsplatRenderer).GetField("m_prevAsset", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_timeNormalizedThisFrameField =
            typeof(GsplatRenderer).GetField("m_timeNormalizedThisFrame", BindingFlags.Instance | BindingFlags.NonPublic);

        static void AdvanceVisibilityState(GsplatRenderer renderer)
        {
            // 说明:
            // - Unity EditMode tests 的执行环境下,ExecuteAlways.Update 可能不会稳定触发.
            // - 但我们仍需要验证“时间推进后进入 Hidden,并让 Valid=false”的门禁语义.
            // - 因此这里用反射显式推进一次状态机,让测试不依赖 Editor PlayerLoop 行为细节.
            Assert.IsNotNull(s_advanceVisibilityStateIfNeeded, "Expected GsplatRenderer.AdvanceVisibilityStateIfNeeded to exist.");
            s_advanceVisibilityStateIfNeeded.Invoke(renderer, null);
        }

        static void AdvanceRenderStyleState(GsplatRenderer renderer)
        {
            // 说明:
            // - render style 的动画推进同样不应依赖 Editor PlayerLoop 的触发细节.
            // - 这里用反射显式推进一次,让测试对 Unity 的窗口重绘/回调时序不敏感.
            Assert.IsNotNull(s_advanceRenderStyleStateIfNeeded, "Expected GsplatRenderer.AdvanceRenderStyleStateIfNeeded to exist.");
            s_advanceRenderStyleStateIfNeeded.Invoke(renderer, null);
        }

        static void AdvanceLidarAnimationState(GsplatRenderer renderer)
        {
            Assert.IsNotNull(s_advanceLidarAnimationStateIfNeeded,
                "Expected GsplatRenderer.AdvanceLidarAnimationStateIfNeeded to exist.");
            s_advanceLidarAnimationStateIfNeeded.Invoke(renderer, null);
        }

        static void UpdateSortRangeForTime(GsplatRenderer renderer, float timeNormalized)
        {
            Assert.IsNotNull(s_updateSortRangeForTime, "Expected GsplatRenderer.UpdateSortRangeForTime to exist.");
            s_updateSortRangeForTime.Invoke(renderer, new object[] { timeNormalized });
        }

        static void InvokeManualUpdate(GsplatRenderer renderer)
        {
            Assert.IsNotNull(s_manualUpdateMethod, "Expected GsplatRenderer.Update to exist.");
            s_manualUpdateMethod.Invoke(renderer, null);
        }

        static bool InvokeHas4DFields(GsplatAsset asset)
        {
            Assert.IsNotNull(s_has4DFieldsMethod, "Expected GsplatRenderer.Has4DFields to exist.");
            return (bool)s_has4DFieldsMethod.Invoke(null, new object[] { asset });
        }

        static float GetRenderStyleBlend01(GsplatRenderer renderer)
        {
            // 说明: 该值是 shader morph 的核心 uniform,需要锁定其收敛到目标值的语义.
            Assert.IsNotNull(s_renderStyleBlend01Field, "Expected private field 'm_renderStyleBlend01' to exist on GsplatRenderer.");
            return (float)s_renderStyleBlend01Field.GetValue(renderer);
        }

        static float GetRenderStyleAlphaBlend01(GsplatRenderer renderer)
        {
            Assert.IsNotNull(s_renderStyleAlphaBlend01Field,
                "Expected private field 'm_renderStyleAlphaBlend01' to exist on GsplatRenderer.");
            return (float)s_renderStyleAlphaBlend01Field.GetValue(renderer);
        }

        static bool GetRenderStyleAnimating(GsplatRenderer renderer)
        {
            Assert.IsNotNull(s_renderStyleAnimatingField, "Expected private field 'm_renderStyleAnimating' to exist on GsplatRenderer.");
            return (bool)s_renderStyleAnimatingField.GetValue(renderer);
        }

        static float GetLidarColorBlend01(GsplatRenderer renderer)
        {
            Assert.IsNotNull(s_lidarColorBlend01Field, "Expected private field 'm_lidarColorBlend01' to exist on GsplatRenderer.");
            return (float)s_lidarColorBlend01Field.GetValue(renderer);
        }

        static bool GetLidarColorAnimating(GsplatRenderer renderer)
        {
            Assert.IsNotNull(s_lidarColorAnimatingField, "Expected private field 'm_lidarColorAnimating' to exist on GsplatRenderer.");
            return (bool)s_lidarColorAnimatingField.GetValue(renderer);
        }

        static float GetLidarVisibility01(GsplatRenderer renderer)
        {
            Assert.IsNotNull(s_lidarVisibility01Field, "Expected private field 'm_lidarVisibility01' to exist on GsplatRenderer.");
            return (float)s_lidarVisibility01Field.GetValue(renderer);
        }

        static bool GetLidarVisibilityAnimating(GsplatRenderer renderer)
        {
            Assert.IsNotNull(s_lidarVisibilityAnimatingField,
                "Expected private field 'm_lidarVisibilityAnimating' to exist on GsplatRenderer.");
            return (bool)s_lidarVisibilityAnimatingField.GetValue(renderer);
        }

        static uint GetSubmittedSplatCount(GsplatRenderer renderer)
        {
            // 说明:
            // - `IGsplat.SplatCount` 会走 `ShouldSubmitSplatsThisFrame` 门禁.
            // - 因此它可直接作为“当前帧是否还在提交 splat sort/draw”的证据.
            return ((IGsplat)renderer).SplatCount;
        }

        static string GetVisibilityStateName(GsplatRenderer renderer)
        {
            Assert.IsNotNull(s_visibilityStateField, "Expected private field 'm_visibilityState' to exist on GsplatRenderer.");
            var stateObj = s_visibilityStateField.GetValue(renderer);
            Assert.IsNotNull(stateObj, "Expected visibility state enum value to be non-null.");
            return stateObj.ToString();
        }

        static float GetVisibilityProgress01(GsplatRenderer renderer)
        {
            Assert.IsNotNull(s_visibilityProgress01Field,
                "Expected private field 'm_visibilityProgress01' to exist on GsplatRenderer.");
            return (float)s_visibilityProgress01Field.GetValue(renderer);
        }

        static string GetVisibilitySourceMaskModeName(GsplatRenderer renderer)
        {
            Assert.IsNotNull(s_visibilitySourceMaskModeField,
                "Expected private field 'm_visibilitySourceMaskMode' to exist on GsplatRenderer.");
            var modeObj = s_visibilitySourceMaskModeField.GetValue(renderer);
            Assert.IsNotNull(modeObj, "Expected source mask mode enum value to be non-null.");
            return modeObj.ToString();
        }

        static float GetVisibilitySourceMaskProgress01(GsplatRenderer renderer)
        {
            Assert.IsNotNull(s_visibilitySourceMaskProgressField,
                "Expected private field 'm_visibilitySourceMaskProgress01' to exist on GsplatRenderer.");
            return (float)s_visibilitySourceMaskProgressField.GetValue(renderer);
        }

        static void SetEffectiveSplatState(GsplatRenderer renderer, uint effectiveSplatCount, uint pendingSplatCount)
        {
            // 说明:
            // - 这些字段本来由 renderer 初始化路径维护.
            // - 测试里为了避免真的创建 GPU 资源,只把“逻辑层需要的最小状态”补齐.
            Assert.IsNotNull(s_effectiveSplatCountField, "Expected private field 'm_effectiveSplatCount' to exist on GsplatRenderer.");
            Assert.IsNotNull(s_pendingSplatCountField, "Expected private field 'm_pendingSplatCount' to exist on GsplatRenderer.");
            s_effectiveSplatCountField.SetValue(renderer, effectiveSplatCount);
            s_pendingSplatCountField.SetValue(renderer, pendingSplatCount);
        }

        static void SetPreviousAsset(GsplatRenderer renderer, GsplatAsset asset)
        {
            Assert.IsNotNull(s_prevAssetField, "Expected private field 'm_prevAsset' to exist on GsplatRenderer.");
            s_prevAssetField.SetValue(renderer, asset);
        }

        static uint GetSortBaseIndexThisFrame(GsplatRenderer renderer)
        {
            Assert.IsNotNull(s_sortSplatBaseIndexThisFrameField,
                "Expected private field 'm_sortSplatBaseIndexThisFrame' to exist on GsplatRenderer.");
            return (uint)s_sortSplatBaseIndexThisFrameField.GetValue(renderer);
        }

        static uint GetSortCountThisFrame(GsplatRenderer renderer)
        {
            Assert.IsNotNull(s_sortSplatCountThisFrameField,
                "Expected private field 'm_sortSplatCountThisFrame' to exist on GsplatRenderer.");
            return (uint)s_sortSplatCountThisFrameField.GetValue(renderer);
        }

        static float GetTimeNormalizedThisFrame(GsplatRenderer renderer)
        {
            Assert.IsNotNull(s_timeNormalizedThisFrameField,
                "Expected private field 'm_timeNormalizedThisFrame' to exist on GsplatRenderer.");
            return (float)s_timeNormalizedThisFrameField.GetValue(renderer);
        }

        static void SetVisibilityStateByName(GsplatRenderer renderer, string stateName)
        {
            Assert.IsNotNull(s_visibilityStateField, "Expected private field 'm_visibilityState' to exist on GsplatRenderer.");
            var enumType = s_visibilityStateField.FieldType;
            var enumValue = System.Enum.Parse(enumType, stateName);
            s_visibilityStateField.SetValue(renderer, enumValue);
        }

        static void SetVisibilityProgress01(GsplatRenderer renderer, float progress01)
        {
            Assert.IsNotNull(s_visibilityProgress01Field,
                "Expected private field 'm_visibilityProgress01' to exist on GsplatRenderer.");
            s_visibilityProgress01Field.SetValue(renderer, Mathf.Clamp01(progress01));
        }

        static void SetVisibilityLastAdvanceRealtime(GsplatRenderer renderer, float realtimeSeconds)
        {
            Assert.IsNotNull(s_visibilityLastAdvanceRealtimeField,
                "Expected private field 'm_visibilityLastAdvanceRealtime' to exist on GsplatRenderer.");
            s_visibilityLastAdvanceRealtimeField.SetValue(renderer, realtimeSeconds);
        }

        static void SetVisibilitySourceMaskByName(GsplatRenderer renderer, string modeName, float progress01)
        {
            Assert.IsNotNull(s_visibilitySourceMaskModeField,
                "Expected private field 'm_visibilitySourceMaskMode' to exist on GsplatRenderer.");
            var enumType = s_visibilitySourceMaskModeField.FieldType;
            var enumValue = System.Enum.Parse(enumType, modeName);
            s_visibilitySourceMaskModeField.SetValue(renderer, enumValue);

            Assert.IsNotNull(s_visibilitySourceMaskProgressField,
                "Expected private field 'm_visibilitySourceMaskProgress01' to exist on GsplatRenderer.");
            s_visibilitySourceMaskProgressField.SetValue(renderer, Mathf.Clamp01(progress01));
        }

        static (float gate, int mode, float progress, int sourceMode, float sourceProgress, float maxRadius, float ringWidth, float trailWidth) BuildLidarShowHideOverlay(
            GsplatRenderer renderer, Bounds localBounds)
        {
            Assert.IsNotNull(s_buildLidarShowHideOverlayForThisFrame,
                "Expected GsplatRenderer.BuildLidarShowHideOverlayForThisFrame to exist.");

            var args = new object[]
            {
                localBounds,
                0.0f, 0, 1.0f, 1, 1.0f, Vector3.zero, 0.0f, 0.0f, 0.0f
            };
            s_buildLidarShowHideOverlayForThisFrame.Invoke(renderer, args);
            return ((float)args[1], (int)args[2], (float)args[3], (int)args[4], (float)args[5], (float)args[7],
                (float)args[8], (float)args[9]);
        }

        static GsplatAsset CreateMinimalAsset1Splat()
        {
            // 目的: 构造一个最小可用的 GsplatAsset,用于验证显隐状态机门禁.
            var asset = ScriptableObject.CreateInstance<GsplatAsset>();
            asset.SplatCount = 1;
            asset.SHBands = 0;
            asset.Bounds = new Bounds(Vector3.zero, Vector3.one);

            asset.Positions = new[] { Vector3.zero };
            asset.Scales = new[] { Vector3.one };
            asset.Rotations = new[] { new Vector4(1, 0, 0, 0) };
            asset.Colors = new[] { new Vector4(1, 1, 1, 1) };

            // 4D 字段保持为空,表示静态 3D 资产.
            asset.Velocities = null;
            asset.Times = null;
            asset.Durations = null;
            return asset;
        }

        static GsplatAsset CreateMinimalAssetWithBounds(Bounds bounds)
        {
            var asset = ScriptableObject.CreateInstance<GsplatAsset>();
            asset.SplatCount = 1;
            asset.SHBands = 0;
            asset.Bounds = bounds;
            asset.Positions = new[] { bounds.center };
            asset.Scales = new[] { Vector3.one };
            asset.Rotations = new[] { new Vector4(1, 0, 0, 0) };
            asset.Colors = new[] { new Vector4(1, 1, 1, 1) };
            asset.Velocities = null;
            asset.Times = null;
            asset.Durations = null;
            return asset;
        }

        static float EaseInOutQuad(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < 0.5f)
                return 2.0f * t * t;

            var a = -2.0f * t + 2.0f;
            return 1.0f - (a * a) * 0.5f;
        }

        static GsplatAsset CreateMinimalStatic4DAsset1Splat()
        {
            // 目标:
            // - 构造“静态单帧 `.splat4d`”在 renderer 逻辑层的最小等价资产.
            // - velocity=0,time=0,duration=1,表示整个归一化时间轴都显示同一批 splats.
            var asset = ScriptableObject.CreateInstance<GsplatAsset>();
            asset.SplatCount = 1;
            asset.SHBands = 0;
            asset.Bounds = new Bounds(Vector3.zero, Vector3.one);

            asset.Positions = new[] { Vector3.zero };
            asset.Scales = new[] { Vector3.one };
            asset.Rotations = new[] { new Vector4(1, 0, 0, 0) };
            asset.Colors = new[] { new Vector4(1, 1, 1, 1) };
            asset.Velocities = new[] { Vector3.zero };
            asset.Times = new[] { 0.0f };
            asset.Durations = new[] { 1.0f };
            asset.MaxSpeed = 0.0f;
            asset.MaxDuration = 1.0f;
            asset.TimeModel = 1;
            asset.TemporalGaussianCutoff = 0.01f;
            return asset;
        }

        static GsplatAsset CreateBroken4DAssetWithTruncatedArrays()
        {
            // 目标:
            // - 模拟“4D arrays 非空但长度不够”的异常资产.
            // - 这正是本次最值当的 guard 目标: 应稳态回退,不能让上传路径继续吃到越界数组.
            var asset = ScriptableObject.CreateInstance<GsplatAsset>();
            asset.SplatCount = 2;
            asset.Positions = new[] { Vector3.zero, Vector3.one };
            asset.Scales = new[] { Vector3.one, Vector3.one };
            asset.Rotations = new[] { new Vector4(1, 0, 0, 0), new Vector4(1, 0, 0, 0) };
            asset.Colors = new[] { new Vector4(1, 1, 1, 1), new Vector4(1, 1, 1, 1) };
            asset.Velocities = new[] { Vector3.zero };
            asset.Times = new[] { 0.0f };
            asset.Durations = new[] { 1.0f };
            return asset;
        }

        [UnityTest]
        public IEnumerator PlayHide_EndsHidden_ValidBecomesFalse()
        {
            var go = new GameObject("GsplatVisibilityAnimationTests_PlayHide");
            go.SetActive(false);
            var asset = CreateMinimalAsset1Splat();
            var r = go.AddComponent<GsplatRenderer>();

            // 显式启用显隐动画,但不要 OnEnable 自动播 show,避免测试依赖动画完成时序.
            r.EnableVisibilityAnimation = true;
            r.VisibilityProgressMode = GsplatVisibilityProgressMode.LegacyDuration;
            r.PlayShowOnEnable = false;
            r.HideDuration = 0.05f;

            r.GsplatAsset = asset;

            go.SetActive(true);

            // 等一帧让 OnEnable 跑完,并完成资源创建/上传.
            yield return null;

            Assert.IsTrue(r.Valid, "Precondition failed: renderer should be valid and visible before hiding.");

            r.PlayHide();

            // 等待 hide 播放完成并进入 Hidden,此时 Valid 应该变为 false.
            var t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.0f)
            {
                AdvanceVisibilityState(r);
                if (!r.Valid)
                    break;
                yield return null;
            }

            Assert.IsFalse(r.Valid, "Expected renderer to become invalid (hidden) after PlayHide completes.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(asset);
        }

        [UnityTest]
        public IEnumerator SetRenderStyle_Animated_ReachesTargetBlend()
        {
            // 目的:
            // - 验证 RenderStyle 从 Gaussian -> ParticleDots 的默认动画可以收敛到目标 blend=1.
            // - 该测试不依赖真实渲染资源(不需要 GsplatAsset),避免在 -nographics 环境下受限.
            var go = new GameObject("GsplatVisibilityAnimationTests_RenderStyle");
            go.SetActive(false);
            var r = go.AddComponent<GsplatRenderer>();

            // 明确起点: hard set 到 Gaussian,确保 blend=0.
            r.SetRenderStyle(GsplatRenderStyle.Gaussian, animated: false);

            go.SetActive(true);
            yield return null;

            Assert.AreEqual(0.0f, GetRenderStyleBlend01(r), 1e-6f, "Precondition failed: expected Gaussian blend=0.");

            // 触发动画切换,用很短的 duration 缩短测试时间.
            r.SetRenderStyle(GsplatRenderStyle.ParticleDots, animated: true, durationSeconds: 0.05f);

            var t0 = Time.realtimeSinceStartup;
            var sawIntermediate = false;
            while (Time.realtimeSinceStartup - t0 < 1.0f)
            {
                AdvanceRenderStyleState(r);

                var blend = GetRenderStyleBlend01(r);
                if (blend > 0.0f && blend < 1.0f)
                    sawIntermediate = true;

                if (!GetRenderStyleAnimating(r) && Mathf.Abs(blend - 1.0f) < 1e-3f)
                    break;

                yield return null;
            }

            Assert.IsTrue(sawIntermediate, "Expected render style blend to go through an intermediate value during animation.");
            Assert.IsFalse(GetRenderStyleAnimating(r), "Expected render style animation to finish within the timeout.");
            Assert.AreEqual(1.0f, GetRenderStyleBlend01(r), 1e-3f, "Expected render style blend to converge to ParticleDots target=1.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SetRenderStyleAndRadarScan_SupportsRendererBidirectionalSwitch()
        {
            // 说明:
            // - 本用例锁定“高斯/粒子 <-> RadarScan”双向切换语义.
            // - RadarScan 开启时应强制进入 ParticleDots 并启用 HideSplatsWhenLidarEnabled.
            // - 退出 RadarScan 后应恢复为调用方指定的风格(此处验证 Gaussian).
            var go = new GameObject("GsplatVisibilityAnimationTests_RendererRadarSwitch");
            go.SetActive(false);
            var r = go.AddComponent<GsplatRenderer>();

            r.SetRenderStyleAndRadarScan(GsplatRenderStyle.Gaussian, enableRadarScan: true, animated: false);

            Assert.IsTrue(r.EnableLidarScan, "Expected radar mode to enable LiDAR scan.");
            Assert.IsTrue(r.HideSplatsWhenLidarEnabled,
                "Expected radar mode to default to pure radar view (hide splat draw).");
            Assert.AreEqual(GsplatRenderStyle.ParticleDots, r.RenderStyle,
                "Expected radar mode to force ParticleDots render style.");
            Assert.AreEqual(1.0f, GetRenderStyleBlend01(r), 1e-6f,
                "Expected hard switch in radar mode to set style blend to ParticleDots target.");

            r.SetRenderStyleAndRadarScan(GsplatRenderStyle.Gaussian, enableRadarScan: false, animated: false);

            Assert.IsFalse(r.EnableLidarScan, "Expected disabling radar mode to disable LiDAR scan.");
            Assert.AreEqual(GsplatRenderStyle.Gaussian, r.RenderStyle,
                "Expected exiting radar mode to follow requested render style.");
            Assert.AreEqual(0.0f, GetRenderStyleBlend01(r), 1e-6f,
                "Expected hard switch back to Gaussian to set style blend to 0.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void SetRenderStyleAndRadarScan_SupportsSequenceBidirectionalSwitch()
        {
            // 说明:
            // - 序列后端需要与静态后端保持同样的切换语义.
            // - 这里验证组合 API 在 `GsplatSequenceRenderer` 上的字段结果一致.
            var go = new GameObject("GsplatVisibilityAnimationTests_SequenceRadarSwitch");
            go.SetActive(false);
            var r = go.AddComponent<GsplatSequenceRenderer>();

            r.SetRenderStyleAndRadarScan(GsplatRenderStyle.Gaussian, enableRadarScan: true, animated: false);

            Assert.IsTrue(r.EnableLidarScan, "Expected radar mode to enable LiDAR scan.");
            Assert.IsTrue(r.HideSplatsWhenLidarEnabled,
                "Expected radar mode to default to pure radar view (hide splat draw).");
            Assert.AreEqual(GsplatRenderStyle.ParticleDots, r.RenderStyle,
                "Expected radar mode to force ParticleDots render style.");

            r.SetRenderStyleAndRadarScan(GsplatRenderStyle.Gaussian, enableRadarScan: false, animated: false);

            Assert.IsFalse(r.EnableLidarScan, "Expected disabling radar mode to disable LiDAR scan.");
            Assert.AreEqual(GsplatRenderStyle.Gaussian, r.RenderStyle,
                "Expected exiting radar mode to follow requested render style.");

            Object.DestroyImmediate(go);
        }

        [UnityTest]
        public IEnumerator SetLidarColorMode_Animated_ReachesTargetBlend()
        {
            var go = new GameObject("GsplatVisibilityAnimationTests_LidarColor");
            go.SetActive(false);
            var r = go.AddComponent<GsplatRenderer>();
            r.SetLidarColorMode(GsplatLidarColorMode.Depth, animated: false);

            go.SetActive(true);
            yield return null;

            Assert.AreEqual(0.0f, GetLidarColorBlend01(r), 1e-6f, "Precondition failed: expected Depth blend=0.");

            r.SetLidarColorMode(GsplatLidarColorMode.SplatColorSH0, animated: true, durationSeconds: 0.05f);

            var t0 = Time.realtimeSinceStartup;
            var sawIntermediate = false;
            while (Time.realtimeSinceStartup - t0 < 1.0f)
            {
                AdvanceLidarAnimationState(r);
                var blend = GetLidarColorBlend01(r);
                if (blend > 0.0f && blend < 1.0f)
                    sawIntermediate = true;
                if (!GetLidarColorAnimating(r) && Mathf.Abs(blend - 1.0f) < 1e-3f)
                    break;
                yield return null;
            }

            Assert.IsTrue(sawIntermediate, "Expected LiDAR color blend to pass through intermediate values.");
            Assert.IsFalse(GetLidarColorAnimating(r), "Expected LiDAR color animation to finish within timeout.");
            Assert.AreEqual(1.0f, GetLidarColorBlend01(r), 1e-3f,
                "Expected LiDAR color blend to converge to SplatColor target=1.");

            Object.DestroyImmediate(go);
        }

        [UnityTest]
        public IEnumerator SetRadarScanEnabled_Animated_FadesOutVisibility()
        {
            var go = new GameObject("GsplatVisibilityAnimationTests_LidarVisibility");
            go.SetActive(false);
            var r = go.AddComponent<GsplatRenderer>();
            r.SetRadarScanEnabled(enableRadarScan: true, animated: false);

            go.SetActive(true);
            yield return null;

            Assert.IsTrue(r.EnableLidarScan, "Precondition failed: expected radar mode enabled.");
            Assert.AreEqual(1.0f, GetLidarVisibility01(r), 1e-6f, "Precondition failed: expected visibility=1.");

            r.SetRadarScanEnabled(enableRadarScan: false, animated: true, durationSeconds: 0.05f);
            Assert.IsFalse(r.EnableLidarScan, "Expected desired radar mode to turn off immediately.");

            var t0 = Time.realtimeSinceStartup;
            var sawIntermediate = false;
            while (Time.realtimeSinceStartup - t0 < 1.0f)
            {
                AdvanceLidarAnimationState(r);
                var vis = GetLidarVisibility01(r);
                if (vis > 0.0f && vis < 1.0f)
                    sawIntermediate = true;
                if (!GetLidarVisibilityAnimating(r) && vis < 1e-3f)
                    break;
                yield return null;
            }

            Assert.IsTrue(sawIntermediate, "Expected LiDAR visibility to pass through intermediate values.");
            Assert.IsFalse(GetLidarVisibilityAnimating(r), "Expected LiDAR visibility animation to finish within timeout.");
            Assert.LessOrEqual(GetLidarVisibility01(r), 1e-3f, "Expected LiDAR visibility to fade to zero.");

            Object.DestroyImmediate(go);
        }

        [UnityTest]
        public IEnumerator SetRenderStyleAndRadarScan_Animated_DelayHideSplatsUntilRadarVisible()
        {
            // 目的:
            // - 锁定“入雷达不黑场”的语义.
            // - 开启 RadarScan 动画后,起始阶段应继续提交 splats;
            //   待雷达可见性淡入完成,并且 Gaussian -> ParticleDots 的 alpha 退场也完成后,
            //   才因 HideSplatsWhenLidarEnabled 停掉 splat 提交.
            var go = new GameObject("GsplatVisibilityAnimationTests_RadarEnterNoBlackFrame");
            go.SetActive(false);
            var asset = CreateMinimalAsset1Splat();
            var r = go.AddComponent<GsplatRenderer>();
            r.GsplatAsset = asset;

            go.SetActive(true);
            yield return null;

            // 预热: 等待一小段时间,确保最小资产已进入可提交状态.
            var warmStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - warmStart < 1.0f && GetSubmittedSplatCount(r) == 0)
                yield return null;

            Assert.Greater(GetSubmittedSplatCount(r), 0u,
                "Precondition failed: expected splat submission before entering radar mode.");

            r.SetRenderStyleAndRadarScan(GsplatRenderStyle.Gaussian, enableRadarScan: true, animated: true, durationSeconds: 0.05f);

            Assert.IsTrue(r.EnableLidarScan, "Expected radar mode to enable LiDAR.");
            Assert.IsTrue(r.HideSplatsWhenLidarEnabled, "Expected radar mode to request pure radar view.");
            Assert.Greater(GetSubmittedSplatCount(r), 0u,
                "Expected splats to remain submitted at radar fade-in start (avoid black frame).");

            var t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.0f)
            {
                AdvanceLidarAnimationState(r);
                AdvanceRenderStyleState(r);
                if (!GetLidarVisibilityAnimating(r) && !GetRenderStyleAnimating(r))
                    break;
                yield return null;
            }

            Assert.IsFalse(GetLidarVisibilityAnimating(r), "Expected radar fade-in animation to finish within timeout.");
            Assert.IsFalse(GetRenderStyleAnimating(r),
                "Expected Gaussian -> ParticleDots render-style animation to finish within timeout.");
            Assert.AreEqual(0u, GetSubmittedSplatCount(r),
                "Expected splat submission to stop only after radar fade-in and Gaussian alpha fade both complete.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(asset);
        }

        [UnityTest]
        public IEnumerator SetRenderStyleAndRadarScan_Animated_KeepsSplatsUntilGaussianAlphaFadeFinishes()
        {
            // 目的:
            // - 复现用户反馈的“高斯 alpha 退场没做完就突然消失”。
            // - 构造一个最小场景:
            //   1) LiDAR 淡入很快完成
            //   2) RenderStyle 的 Gaussian -> ParticleDots 动画更慢
            // - 期望: 只要 render-style 动画还没结束,splat 仍应继续提交,否则体感会像高斯被瞬间掐掉.
            var go = new GameObject("GsplatVisibilityAnimationTests_RadarEnter_KeepSplatsUntilAlphaFadeFinishes");
            go.SetActive(false);
            var asset = CreateMinimalAsset1Splat();
            var r = go.AddComponent<GsplatRenderer>();
            r.GsplatAsset = asset;
            r.RenderStyleSwitchDurationSeconds = 0.2f;
            r.LidarShowDuration = 0.05f;

            go.SetActive(true);
            yield return null;

            var warmStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - warmStart < 1.0f && GetSubmittedSplatCount(r) == 0)
                yield return null;

            Assert.Greater(GetSubmittedSplatCount(r), 0u,
                "Precondition failed: expected Gaussian mode to submit splats before entering radar mode.");

            r.SetRenderStyleAndRadarScan(GsplatRenderStyle.Gaussian, enableRadarScan: true, animated: true, durationSeconds: -1.0f);

            Assert.IsTrue(r.EnableLidarScan, "Expected radar mode to enable LiDAR.");
            Assert.IsTrue(r.HideSplatsWhenLidarEnabled, "Expected radar mode to request pure radar view.");
            Assert.IsTrue(GetRenderStyleAnimating(r),
                "Precondition failed: expected Gaussian -> ParticleDots render-style animation to start.");

            var t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.0f)
            {
                AdvanceLidarAnimationState(r);
                AdvanceRenderStyleState(r);

                if (!GetLidarVisibilityAnimating(r) && GetRenderStyleAnimating(r))
                {
                    var geometryBlend = GetRenderStyleBlend01(r);
                    var alphaBlend = GetRenderStyleAlphaBlend01(r);
                    Assert.Less(alphaBlend, geometryBlend,
                        "Expected render-style alpha handoff to stay behind geometry morph so Gaussian fade does not feel abruptly cut.");
                    Assert.Greater(geometryBlend - alphaBlend, 0.05f,
                        "Expected the softened alpha handoff to create a visible buffer between geometry morph and alpha fade.");
                    Assert.Greater(GetSubmittedSplatCount(r), 0u,
                        "Expected splats to remain submitted while Gaussian alpha fade is still finishing.");
                    break;
                }

                yield return null;
            }

            Assert.IsFalse(GetLidarVisibilityAnimating(r),
                "Expected LiDAR fade-in animation to finish within timeout.");
            Assert.IsTrue(GetRenderStyleAnimating(r),
                "Expected render-style animation to still be in progress when the slower Gaussian alpha fade is under test.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(asset);
        }

        [UnityTest]
        public IEnumerator PlayRadarScanToGaussianShowHideSwitch_DelaysGaussianShowUntilHalfway()
        {
            // 目的:
            // - 锁定新按钮的核心编排语义:
            //   1) 先让雷达走 hide.
            //   2) 半程前高斯不能抢跑.
            //   3) 到半程后才切到 Gaussian 并启动 show.
            var go = new GameObject("GsplatVisibilityAnimationTests_RadarToGaussian_ShowHideSwitch");
            go.SetActive(false);
            var asset = CreateMinimalAsset1Splat();
            var r = go.AddComponent<GsplatRenderer>();
            r.EnableVisibilityAnimation = true;
            r.VisibilityProgressMode = GsplatVisibilityProgressMode.LegacyDuration;
            r.PlayShowOnEnable = false;
            r.ShowDuration = 0.2f;
            r.HideDuration = 0.3f;
            r.GsplatAsset = asset;

            go.SetActive(true);
            yield return null;

            // 预热:
            // - 先让最小资产进入“可提交 splat”的稳定状态.
            // - 后面 overlap 阶段才能用 `IGsplat.SplatCount` 直接证明门禁已放开.
            var warmStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - warmStart < 1.0f && GetSubmittedSplatCount(r) == 0)
                yield return null;

            Assert.Greater(GetSubmittedSplatCount(r), 0u,
                "Precondition failed: expected Gaussian mode to submit splats before entering RadarScan.");

            r.SetRenderStyleAndRadarScan(GsplatRenderStyle.Gaussian, enableRadarScan: true, animated: false);

            Assert.IsTrue(r.EnableLidarScan, "Precondition failed: expected radar mode enabled.");
            Assert.AreEqual(GsplatRenderStyle.ParticleDots, r.RenderStyle,
                "Precondition failed: expected radar mode to force ParticleDots.");
            Assert.AreEqual(0u, GetSubmittedSplatCount(r),
                "Precondition failed: expected radar-only mode to block splat submission when HideSplatsWhenLidarEnabled=true.");

            r.PlayRadarScanToGaussianShowHideSwitch();

            Assert.IsTrue(r.EnableLidarScan, "Expected RadarScan to stay enabled during the front half of radar hide.");
            Assert.AreEqual("Hiding", GetVisibilityStateName(r),
                "Expected front half to enter the same visibility hide process as the Hide button.");
            Assert.AreEqual(GsplatRenderStyle.ParticleDots, r.RenderStyle,
                "Expected render style to remain ParticleDots before half delay elapses.");
            Assert.AreEqual(0u, GetSubmittedSplatCount(r),
                "Expected splat submission to remain blocked during the radar-only front half.");
            var overlayBeforeHalf = BuildLidarShowHideOverlay(r, asset.Bounds);
            Assert.Greater(overlayBeforeHalf.gate, 0.99f,
                "Expected LiDAR overlay gate to stay open during radar hide front half.");
            Assert.AreEqual(2, overlayBeforeHalf.mode,
                "Expected LiDAR overlay to use hide mode during radar hide front half.");

            var switchStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - switchStart < 0.04f)
            {
                InvokeManualUpdate(r);
                yield return null;
            }

            Assert.IsTrue(r.EnableLidarScan, "Expected RadarScan to remain enabled before the early overlap trigger.");
            Assert.AreEqual("Hiding", GetVisibilityStateName(r),
                "Expected front phase to keep running the visibility hide process before the overlap trigger.");
            Assert.AreEqual(GsplatRenderStyle.ParticleDots, r.RenderStyle,
                "Expected render style to stay ParticleDots before the overlap trigger.");
            Assert.Less(GetVisibilityProgress01(r), kRadarToGaussianShowTriggerProgress01,
                "Expected the shared visibility hide progress to stay below the early trigger point before Gaussian show starts.");

            var triggered = false;
            while (Time.realtimeSinceStartup - switchStart < 1.0f)
            {
                InvokeManualUpdate(r);
                if (r.RenderStyle == GsplatRenderStyle.Gaussian && GetVisibilityStateName(r) == "Showing")
                {
                    triggered = true;
                    break;
                }

                yield return null;
            }

            Assert.IsTrue(triggered, "Expected Gaussian show to start after the early overlap trigger.");
            Assert.AreEqual(GsplatRenderStyle.Gaussian, r.RenderStyle,
                "Expected second phase to switch render style to Gaussian.");
            Assert.AreEqual("Showing", GetVisibilityStateName(r),
                "Expected second phase to enter Gaussian show animation.");
            Assert.IsTrue(r.EnableLidarScan,
                "Expected overlap phase to keep the LiDAR main enable flag alive until the dedicated hide overlay finishes.");
            var overlayDuringOverlap = BuildLidarShowHideOverlay(r, asset.Bounds);
            Assert.Greater(overlayDuringOverlap.gate, 0.99f,
                "Expected LiDAR overlay gate to remain open during the overlap phase.");
            Assert.AreEqual(2, overlayDuringOverlap.mode,
                "Expected overlap phase to keep using the dedicated LiDAR hide overlay instead of Gaussian show overlay.");
            Assert.Greater(GetSubmittedSplatCount(r), 0u,
                "Expected Gaussian splats to be submitted during overlap even though LiDAR is still enabled.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void PlayRadarScanToGaussianShowHideSwitch_DelayedFirstTick_DoesNotStartShowBeforeLidarHalfway()
        {
            // 目的:
            // - 复现“第一次 tick 很晚才到来”这一类编辑器真实场景.
            // - 若半程判断绑在 wall-clock 而不是 LiDAR 动画进度上,
            //   则第一次 Update 时就会直接切到 Gaussian + Showing.
            var go = new GameObject("GsplatVisibilityAnimationTests_RadarToGaussian_DelayedFirstTick");
            go.SetActive(false);
            var r = go.AddComponent<GsplatRenderer>();
            r.EnableVisibilityAnimation = true;
            r.VisibilityProgressMode = GsplatVisibilityProgressMode.LegacyDuration;
            r.ShowDuration = 0.2f;
            r.HideDuration = 0.3f;
            r.SetRenderStyleAndRadarScan(GsplatRenderStyle.Gaussian, enableRadarScan: true, animated: false);

            r.PlayRadarScanToGaussianShowHideSwitch();

            var waitStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - waitStart < 0.08f)
            {
                // 故意什么都不做:
                // - 不让 LiDAR hide 状态机推进
                // - 只让墙钟时间流逝
            }

            InvokeManualUpdate(r);

            Assert.AreEqual(GsplatRenderStyle.ParticleDots, r.RenderStyle,
                "Expected delayed first tick to keep ParticleDots until LiDAR hide really reaches the overlap trigger.");
            Assert.AreEqual("Hiding", GetVisibilityStateName(r),
                "Expected switch button to enter the visibility hide process immediately, even before the first tick advances time.");
            Assert.IsTrue(r.EnableLidarScan,
                "Expected delayed first tick to keep RadarScan enabled before the overlap trigger.");
            Assert.Less(GetVisibilityProgress01(r), kRadarToGaussianShowTriggerProgress01,
                "Expected delayed first tick to keep the visibility hide progress below the early trigger point.");
            var overlay = BuildLidarShowHideOverlay(r, new Bounds(Vector3.zero, Vector3.one));
            Assert.AreEqual(2, overlay.mode,
                "Expected delayed first tick to remain on the hide overlay rather than jumping to Gaussian show.");

            Object.DestroyImmediate(go);
        }

        [UnityTest]
        public IEnumerator PlayRadarScanToGaussianShowHideSwitch_DisablesLidarOnlyAfterDedicatedHideOverlayCompletes()
        {
            // 目的:
            // - 锁定 dual-track 的最终关门时机.
            // - Gaussian show 可以在 hide 过半前一点开始,但 `EnableLidarScan` 必须等专用 hide overlay 跑完才关闭.
            var go = new GameObject("GsplatVisibilityAnimationTests_RadarToGaussian_HideCompletesBeforeDisable");
            go.SetActive(false);
            var asset = CreateMinimalAsset1Splat();
            var r = go.AddComponent<GsplatRenderer>();
            r.EnableVisibilityAnimation = true;
            r.VisibilityProgressMode = GsplatVisibilityProgressMode.LegacyDuration;
            r.PlayShowOnEnable = false;
            r.ShowDuration = 0.2f;
            r.HideDuration = 0.25f;
            r.GsplatAsset = asset;

            go.SetActive(true);
            yield return null;

            var warmStart = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - warmStart < 1.0f && GetSubmittedSplatCount(r) == 0)
                yield return null;

            Assert.Greater(GetSubmittedSplatCount(r), 0u,
                "Precondition failed: expected Gaussian mode to submit splats before entering RadarScan.");

            r.SetRenderStyleAndRadarScan(GsplatRenderStyle.Gaussian, enableRadarScan: true, animated: false);
            r.PlayRadarScanToGaussianShowHideSwitch();

            var overlapStarted = false;
            var t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.5f)
            {
                InvokeManualUpdate(r);

                if (!overlapStarted &&
                    r.RenderStyle == GsplatRenderStyle.Gaussian &&
                    GetVisibilityStateName(r) == "Showing")
                {
                    overlapStarted = true;
                    Assert.IsTrue(r.EnableLidarScan,
                        "Expected LiDAR main enable flag to remain true when overlap just starts.");
                }

                if (overlapStarted && r.EnableLidarScan)
                {
                    var overlay = BuildLidarShowHideOverlay(r, asset.Bounds);
                    Assert.AreEqual(2, overlay.mode,
                        "Expected dedicated LiDAR hide overlay to remain active for the whole overlap phase.");
                    Assert.Greater(overlay.gate, 0.99f,
                        "Expected LiDAR overlay gate to stay open until the dedicated hide overlay completes.");
                }

                if (overlapStarted && !r.EnableLidarScan)
                    break;

                yield return null;
            }

            Assert.IsTrue(overlapStarted, "Expected Gaussian overlap phase to start within timeout.");
            Assert.IsFalse(r.EnableLidarScan,
                "Expected LiDAR main enable flag to turn off only after the dedicated hide overlay finishes.");
            var overlayAfterDisable = BuildLidarShowHideOverlay(r, asset.Bounds);
            Assert.AreNotEqual(2, overlayAfterDisable.mode,
                "Expected dedicated LiDAR hide overlay to be gone once LiDAR main enable flag is released.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(asset);
        }

        [UnityTest]
        public IEnumerator PlayHide_DuringShowing_RestartsHideFromZero()
        {
            var go = new GameObject("GsplatVisibilityAnimationTests_Interrupt_ShowToHide");
            go.SetActive(false);
            var asset = CreateMinimalAsset1Splat();
            var r = go.AddComponent<GsplatRenderer>();

            r.EnableVisibilityAnimation = true;
            r.VisibilityProgressMode = GsplatVisibilityProgressMode.LegacyDuration;
            r.PlayShowOnEnable = false;
            r.ShowDuration = 0.2f;
            r.HideDuration = 0.2f;
            r.GsplatAsset = asset;

            go.SetActive(true);
            yield return null;

            // 先切到 Hidden,再触发 show.
            r.SetVisible(false, animated: false);
            Assert.IsFalse(r.Valid, "Precondition failed: expected hidden state after hard hide.");

            r.PlayShow();
            Assert.AreEqual("Showing", GetVisibilityStateName(r), "Expected PlayShow to enter Showing state.");

            var t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.0f)
            {
                AdvanceVisibilityState(r);
                if (GetVisibilityProgress01(r) > 0.2f)
                    break;
                yield return null;
            }

            var beforeReverse = GetVisibilityProgress01(r);
            Assert.Greater(beforeReverse, 0.0f, "Precondition failed: show progress should have advanced.");

            r.PlayHide();
            Assert.AreEqual("Hiding", GetVisibilityStateName(r),
                "Expected hide-interrupt during Showing to restart Hiding mode.");
            Assert.LessOrEqual(GetVisibilityProgress01(r), 1e-4f,
                "Expected hide-interrupt during Showing to restart progress near zero.");
            Assert.AreEqual("ShowSnapshot", GetVisibilitySourceMaskModeName(r),
                "Expected hide-interrupt during Showing to capture source as ShowSnapshot for compositing.");
            Assert.Greater(GetVisibilitySourceMaskProgress01(r), 1e-3f,
                "Expected show snapshot progress to preserve the pre-interrupt visible range.");

            var t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 1.0f)
            {
                AdvanceVisibilityState(r);
                if (!r.Valid)
                    break;
                yield return null;
            }

            Assert.IsFalse(r.Valid, "Expected renderer to finish hidden after show->hide interrupt.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(asset);
        }

        [UnityTest]
        public IEnumerator PlayShow_DuringHiding_RestartsShowFromZero()
        {
            var go = new GameObject("GsplatVisibilityAnimationTests_Interrupt_HideToShow");
            go.SetActive(false);
            var asset = CreateMinimalAsset1Splat();
            var r = go.AddComponent<GsplatRenderer>();

            r.EnableVisibilityAnimation = true;
            r.VisibilityProgressMode = GsplatVisibilityProgressMode.LegacyDuration;
            r.PlayShowOnEnable = false;
            r.ShowDuration = 0.2f;
            r.HideDuration = 0.2f;
            r.GsplatAsset = asset;

            go.SetActive(true);
            yield return null;

            Assert.IsTrue(r.Valid, "Precondition failed: renderer should start visible.");

            r.PlayHide();
            Assert.AreEqual("Hiding", GetVisibilityStateName(r), "Expected PlayHide to enter Hiding state.");

            var t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.0f)
            {
                AdvanceVisibilityState(r);
                if (GetVisibilityProgress01(r) > 0.2f)
                    break;
                yield return null;
            }

            var beforeReverse = GetVisibilityProgress01(r);
            Assert.Greater(beforeReverse, 0.0f, "Precondition failed: hide progress should have advanced.");

            r.PlayShow();
            Assert.AreEqual("Showing", GetVisibilityStateName(r),
                "Expected show-interrupt during Hiding to restart Showing mode.");
            Assert.LessOrEqual(GetVisibilityProgress01(r), 1e-4f,
                "Expected show-interrupt during Hiding to restart progress near zero.");
            Assert.AreEqual("HideSnapshot", GetVisibilitySourceMaskModeName(r),
                "Expected show-interrupt during Hiding to capture source as HideSnapshot for compositing.");
            Assert.Greater(GetVisibilitySourceMaskProgress01(r), 1e-3f,
                "Expected hide snapshot progress to preserve the pre-interrupt visible range.");

            var t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 1.0f)
            {
                AdvanceVisibilityState(r);
                if (r.Valid && GetVisibilityStateName(r) == "Visible")
                    break;
                yield return null;
            }

            Assert.IsTrue(r.Valid, "Expected renderer to finish visible after hide->show interrupt.");
            Assert.AreEqual("Visible", GetVisibilityStateName(r), "Expected final state to be Visible.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void BuildLidarShowHideOverlay_HiddenState_OutputsGateZero()
        {
            var go = new GameObject("GsplatVisibilityAnimationTests_LidarOverlay_Hidden");
            var r = go.AddComponent<GsplatRenderer>();
            r.EnableVisibilityAnimation = true;

            SetVisibilityStateByName(r, "Hidden");
            SetVisibilityProgress01(r, 1.0f);
            SetVisibilitySourceMaskByName(r, "FullHidden", 0.0f);

            var overlay = BuildLidarShowHideOverlay(r, new Bounds(Vector3.zero, Vector3.one));
            Assert.LessOrEqual(overlay.gate, 1e-6f, "Expected hidden state to hard-gate LiDAR show/hide overlay.");
            Assert.AreEqual(0, overlay.mode, "Expected hidden steady state to use mode=0.");
            Assert.AreEqual(2, overlay.sourceMode, "Expected hidden steady state source mode=FullHidden.");
            Assert.LessOrEqual(overlay.sourceProgress, 1e-6f, "Expected hidden steady state source progress=0.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void BuildLidarShowHideOverlay_HidingState_OutputsHideModeAndSnapshot()
        {
            var go = new GameObject("GsplatVisibilityAnimationTests_LidarOverlay_Hiding");
            var r = go.AddComponent<GsplatRenderer>();
            r.EnableVisibilityAnimation = true;

            SetVisibilityStateByName(r, "Hiding");
            SetVisibilityProgress01(r, 0.35f);
            SetVisibilitySourceMaskByName(r, "ShowSnapshot", 0.72f);

            var overlay = BuildLidarShowHideOverlay(r, new Bounds(Vector3.zero, Vector3.one));
            Assert.GreaterOrEqual(overlay.gate, 1.0f - 1e-6f, "Expected hiding state to keep LiDAR overlay enabled.");
            Assert.AreEqual(2, overlay.mode, "Expected hiding state to output hide mode.");
            Assert.AreEqual(0.35f, overlay.progress, 1e-5f, "Expected hiding state progress to be forwarded.");
            Assert.AreEqual(3, overlay.sourceMode, "Expected source mask mode=ShowSnapshot in hide interrupt phase.");
            Assert.AreEqual(0.72f, overlay.sourceProgress, 1e-5f, "Expected source snapshot progress to be forwarded.");
            Assert.Greater(overlay.maxRadius, 1e-6f, "Expected positive max radius for LiDAR show/hide radial mask.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void BuildLidarShowHideOverlay_VisibilityRadiusScale_ScalesMaxRadius()
        {
            var go = new GameObject("GsplatVisibilityAnimationTests_VisibilityRadiusScale");
            var r = go.AddComponent<GsplatRenderer>();
            var bounds = new Bounds(Vector3.zero, Vector3.one * 2.0f);

            r.EnableVisibilityAnimation = true;
            r.VisibilityRadiusScale = 1.0f;
            var overlayA = BuildLidarShowHideOverlay(r, bounds);

            r.VisibilityRadiusScale = 2.0f;
            var overlayB = BuildLidarShowHideOverlay(r, bounds);

            Assert.AreEqual(overlayA.maxRadius * 2.0f, overlayB.maxRadius, 1e-5f,
                "Expected VisibilityRadiusScale to multiply reveal maxRadius.");
            Assert.AreEqual(overlayA.trailWidth * 2.0f, overlayB.trailWidth, 1e-5f,
                "Expected VisibilityRadiusScale to scale trail width together with maxRadius.");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void AdvanceVisibilityState_WorldSpeedMode_KeepsRevealFrontDistanceComparableAcrossBoundsScales()
        {
            var smallGo = new GameObject("GsplatVisibilityAnimationTests_WorldSpeed_Small");
            var largeGo = new GameObject("GsplatVisibilityAnimationTests_WorldSpeed_Large");
            var smallAsset = CreateMinimalAssetWithBounds(new Bounds(Vector3.zero, new Vector3(2.0f, 2.0f, 2.0f)));
            var largeAsset = CreateMinimalAssetWithBounds(new Bounds(Vector3.zero, new Vector3(20.0f, 20.0f, 20.0f)));
            var small = smallGo.AddComponent<GsplatRenderer>();
            var large = largeGo.AddComponent<GsplatRenderer>();

            small.GsplatAsset = smallAsset;
            large.GsplatAsset = largeAsset;

            small.EnableVisibilityAnimation = true;
            large.EnableVisibilityAnimation = true;
            small.VisibilityProgressMode = GsplatVisibilityProgressMode.WorldSpeed;
            large.VisibilityProgressMode = GsplatVisibilityProgressMode.WorldSpeed;
            small.ShowWorldSpeed = 12.5f;
            large.ShowWorldSpeed = 12.5f;
            small.ShowTrailWidthNormalized = 0.2f;
            large.ShowTrailWidthNormalized = 0.2f;

            SetVisibilityStateByName(small, "Showing");
            SetVisibilityStateByName(large, "Showing");
            SetVisibilityProgress01(small, 0.0f);
            SetVisibilityProgress01(large, 0.0f);
            SetVisibilitySourceMaskByName(small, "FullHidden", 0.0f);
            SetVisibilitySourceMaskByName(large, "FullHidden", 0.0f);

            var now = Time.realtimeSinceStartup;
            SetVisibilityLastAdvanceRealtime(small, now - 0.1f);
            SetVisibilityLastAdvanceRealtime(large, now - 0.1f);

            AdvanceVisibilityState(small);
            AdvanceVisibilityState(large);

            var smallOverlay = BuildLidarShowHideOverlay(small, smallAsset.Bounds);
            var largeOverlay = BuildLidarShowHideOverlay(large, largeAsset.Bounds);

            var smallRadius = EaseInOutQuad(smallOverlay.progress) * (smallOverlay.maxRadius + smallOverlay.trailWidth);
            var largeRadius = EaseInOutQuad(largeOverlay.progress) * (largeOverlay.maxRadius + largeOverlay.trailWidth);

            Assert.AreEqual(smallRadius, largeRadius, 1e-3f,
                "Expected WorldSpeed mode to keep reveal front distance comparable across different bounds scales.");
            Assert.Less(largeOverlay.progress, smallOverlay.progress,
                "Expected larger bounds to advance more slowly in normalized progress when using the same world speed.");

            Object.DestroyImmediate(smallGo);
            Object.DestroyImmediate(largeGo);
            Object.DestroyImmediate(smallAsset);
            Object.DestroyImmediate(largeAsset);
        }

        [Test]
        public void AdvanceVisibilityState_LegacyDurationMode_ProgressRemainsBoundsIndependent()
        {
            var smallGo = new GameObject("GsplatVisibilityAnimationTests_LegacyDuration_Small");
            var largeGo = new GameObject("GsplatVisibilityAnimationTests_LegacyDuration_Large");
            var smallAsset = CreateMinimalAssetWithBounds(new Bounds(Vector3.zero, new Vector3(2.0f, 2.0f, 2.0f)));
            var largeAsset = CreateMinimalAssetWithBounds(new Bounds(Vector3.zero, new Vector3(20.0f, 20.0f, 20.0f)));
            var small = smallGo.AddComponent<GsplatRenderer>();
            var large = largeGo.AddComponent<GsplatRenderer>();

            small.GsplatAsset = smallAsset;
            large.GsplatAsset = largeAsset;

            small.EnableVisibilityAnimation = true;
            large.EnableVisibilityAnimation = true;
            small.VisibilityProgressMode = GsplatVisibilityProgressMode.LegacyDuration;
            large.VisibilityProgressMode = GsplatVisibilityProgressMode.LegacyDuration;
            small.ShowDuration = 4.0f;
            large.ShowDuration = 4.0f;

            SetVisibilityStateByName(small, "Showing");
            SetVisibilityStateByName(large, "Showing");
            SetVisibilityProgress01(small, 0.0f);
            SetVisibilityProgress01(large, 0.0f);
            SetVisibilitySourceMaskByName(small, "FullHidden", 0.0f);
            SetVisibilitySourceMaskByName(large, "FullHidden", 0.0f);

            var now = Time.realtimeSinceStartup;
            SetVisibilityLastAdvanceRealtime(small, now - 0.1f);
            SetVisibilityLastAdvanceRealtime(large, now - 0.1f);

            AdvanceVisibilityState(small);
            AdvanceVisibilityState(large);

            Assert.AreEqual(GetVisibilityProgress01(small), GetVisibilityProgress01(large), 1e-5f,
                "Expected LegacyDuration mode to keep normalized progress independent from bounds size.");

            Object.DestroyImmediate(smallGo);
            Object.DestroyImmediate(largeGo);
            Object.DestroyImmediate(smallAsset);
            Object.DestroyImmediate(largeAsset);
        }

        [Test]
        public void LidarRenderPointCloud_Signature_ContainsShowHideNoiseParams()
        {
            // 说明:
            // - Radar show/hide noise 需要从 renderer 透传到 LiDAR shader.
            // - 这里锁定 GsplatLidarScan.RenderPointCloud 的参数契约,避免后续重构时丢参导致“开关有效但无噪声”.
            var lidarScanType = typeof(GsplatRenderer).Assembly.GetType("Gsplat.GsplatLidarScan");
            Assert.IsNotNull(lidarScanType, "Expected runtime type Gsplat.GsplatLidarScan to exist.");

            var renderPointCloud = lidarScanType.GetMethod("RenderPointCloud",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(renderPointCloud, "Expected GsplatLidarScan.RenderPointCloud to exist.");

            var parameters = renderPointCloud.GetParameters();
            var modeIndex = System.Array.FindIndex(parameters, p => p.Name == "showHideNoiseMode");
            var strengthIndex = System.Array.FindIndex(parameters, p => p.Name == "showHideNoiseStrength");
            var scaleIndex = System.Array.FindIndex(parameters, p => p.Name == "showHideNoiseScale");
            var speedIndex = System.Array.FindIndex(parameters, p => p.Name == "showHideNoiseSpeed");

            Assert.GreaterOrEqual(modeIndex, 0, "Expected RenderPointCloud to expose showHideNoiseMode parameter.");
            Assert.GreaterOrEqual(strengthIndex, 0,
                "Expected RenderPointCloud to expose showHideNoiseStrength parameter.");
            Assert.GreaterOrEqual(scaleIndex, 0, "Expected RenderPointCloud to expose showHideNoiseScale parameter.");
            Assert.GreaterOrEqual(speedIndex, 0, "Expected RenderPointCloud to expose showHideNoiseSpeed parameter.");

            Assert.AreEqual(typeof(int), parameters[modeIndex].ParameterType,
                "Expected showHideNoiseMode parameter type=int.");
            Assert.AreEqual(typeof(float), parameters[strengthIndex].ParameterType,
                "Expected showHideNoiseStrength parameter type=float.");
            Assert.AreEqual(typeof(float), parameters[scaleIndex].ParameterType,
                "Expected showHideNoiseScale parameter type=float.");
            Assert.AreEqual(typeof(float), parameters[speedIndex].ParameterType,
                "Expected showHideNoiseSpeed parameter type=float.");
        }

        [UnityTest]
        public IEnumerator PlayShow_FromHidden_ValidBecomesTrue()
        {
            var go = new GameObject("GsplatVisibilityAnimationTests_PlayShow");
            go.SetActive(false);
            var asset = CreateMinimalAsset1Splat();
            var r = go.AddComponent<GsplatRenderer>();

            r.EnableVisibilityAnimation = true;
            r.VisibilityProgressMode = GsplatVisibilityProgressMode.LegacyDuration;
            r.PlayShowOnEnable = false;
            r.ShowDuration = 0.05f;
            r.HideDuration = 0.05f;
            r.GsplatAsset = asset;

            go.SetActive(true);
            yield return null;

            Assert.IsTrue(r.Valid, "Precondition failed: renderer should start visible.");

            r.PlayHide();

            // 先等待进入 Hidden.
            var t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.0f)
            {
                AdvanceVisibilityState(r);
                if (!r.Valid)
                    break;
                yield return null;
            }

            Assert.IsFalse(r.Valid, "Expected renderer to be hidden before showing.");

            r.PlayShow();

            // show 调用后应从 Hidden/不可见态恢复,Valid 变为 true.
            var t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 1.0f)
            {
                AdvanceVisibilityState(r);
                if (r.Valid)
                    break;
                yield return null;
            }

            Assert.IsTrue(r.Valid, "Expected renderer to become valid (showing/visible) after PlayShow.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void StaticSingleFrame4D_Has4DFieldsRejectsTruncatedArrays()
        {
            var validAsset = CreateMinimalStatic4DAsset1Splat();
            var brokenAsset = CreateBroken4DAssetWithTruncatedArrays();

            Assert.IsTrue(InvokeHas4DFields(validAsset),
                "静态单帧 4D 资产的 canonical arrays 齐全且等长时,应被识别为合法 4D 资产.");
            Assert.IsFalse(InvokeHas4DFields(brokenAsset),
                "4D arrays 只是非空但长度不足时,必须回退为非 4D,避免后续上传越界.");

            Object.DestroyImmediate(validAsset);
            Object.DestroyImmediate(brokenAsset);
        }

        [Test]
        public void StaticSingleFrame4D_UpdateSortRangeForAnyTime_AlwaysKeepsWholeSplatSet()
        {
            var go = new GameObject("GsplatVisibilityAnimationTests_StaticSingleFrameSortRange");
            go.SetActive(false);
            var asset = CreateMinimalStatic4DAsset1Splat();
            var renderer = go.AddComponent<GsplatRenderer>();
            renderer.GsplatAsset = asset;

            SetEffectiveSplatState(renderer, effectiveSplatCount: 1, pendingSplatCount: 0);

            var samples = new[] { -0.25f, 0.0f, 0.35f, 1.0f, 1.75f };
            for (var i = 0; i < samples.Length; i++)
            {
                UpdateSortRangeForTime(renderer, samples[i]);
                Assert.AreEqual(0u, GetSortBaseIndexThisFrame(renderer),
                    $"静态单帧 4D 资产不应因为时间变化偏移 baseIndex, sampleIndex={i}");
                Assert.AreEqual(1u, GetSortCountThisFrame(renderer),
                    $"静态单帧 4D 资产在任意时间点都应继续提交完整单帧 splat 集, sampleIndex={i}");
            }

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(asset);
        }

        [UnityTest]
        public IEnumerator StaticSingleFrame4D_AutoPlayLoopOnlyChangesTime_NotFrameCardinality()
        {
            // 先等到 deltaTime 有效:
            // - 手动反射调用 `Update` 时,我们仍依赖 Unity 提供本帧 deltaTime.
            // - 若 deltaTime 始终为 0,Loop/Clamp 的时间推进语义就无法被证伪.
            var guardFrames = 0;
            while (Time.deltaTime <= 0.0f && guardFrames < 3)
            {
                guardFrames++;
                yield return null;
            }

            Assert.Greater(Time.deltaTime, 0.0f, "需要一个正的 deltaTime 才能验证 AutoPlay/Loop 语义.");

            var asset = CreateMinimalStatic4DAsset1Splat();

            var loopOffGo = new GameObject("GsplatVisibilityAnimationTests_StaticSingleFrameLoopOff");
            loopOffGo.SetActive(false);
            var loopOffRenderer = loopOffGo.AddComponent<GsplatRenderer>();
            loopOffRenderer.GsplatAsset = asset;
            loopOffRenderer.EnableGsplatBackend = false;
            loopOffRenderer.AutoPlay = true;
            loopOffRenderer.Speed = 1000.0f;
            loopOffRenderer.Loop = false;
            loopOffRenderer.TimeNormalized = 0.95f;
            SetEffectiveSplatState(loopOffRenderer, effectiveSplatCount: 1, pendingSplatCount: 0);
            SetPreviousAsset(loopOffRenderer, asset);

            InvokeManualUpdate(loopOffRenderer);
            Assert.AreEqual(1.0f, loopOffRenderer.TimeNormalized, 1e-6f,
                "Loop=false 时,AutoPlay 应把时间钳在 1.0,而不是要求不存在的第二帧.");
            Assert.AreEqual(1.0f, GetTimeNormalizedThisFrame(loopOffRenderer), 1e-6f,
                "本帧缓存时间应与序列化 TimeNormalized 保持一致.");
            Assert.AreEqual(1u, GetSortCountThisFrame(loopOffRenderer),
                "即便 AutoPlay 把时间推到末端,静态单帧 4D 资产仍应保持完整单帧提交.");

            var loopOnGo = new GameObject("GsplatVisibilityAnimationTests_StaticSingleFrameLoopOn");
            loopOnGo.SetActive(false);
            var loopOnRenderer = loopOnGo.AddComponent<GsplatRenderer>();
            loopOnRenderer.GsplatAsset = asset;
            loopOnRenderer.EnableGsplatBackend = false;
            loopOnRenderer.AutoPlay = true;
            loopOnRenderer.Speed = 1000.0f;
            loopOnRenderer.Loop = true;
            loopOnRenderer.TimeNormalized = 0.95f;
            SetEffectiveSplatState(loopOnRenderer, effectiveSplatCount: 1, pendingSplatCount: 0);
            SetPreviousAsset(loopOnRenderer, asset);

            InvokeManualUpdate(loopOnRenderer);
            Assert.GreaterOrEqual(loopOnRenderer.TimeNormalized, 0.0f,
                "Loop=true 时,AutoPlay 后的时间仍应保持在合法归一化区间.");
            Assert.Less(loopOnRenderer.TimeNormalized, 1.0f,
                "Loop=true 时,AutoPlay 应回绕到 [0,1),而不是卡在末端.");
            Assert.AreEqual(loopOnRenderer.TimeNormalized, GetTimeNormalizedThisFrame(loopOnRenderer), 1e-6f,
                "本帧缓存时间应跟随 Loop 后的最新归一化时间.");
            Assert.AreEqual(1u, GetSortCountThisFrame(loopOnRenderer),
                "Loop=true 时也不应把静态单帧 4D 资产错误地当成需要第二帧的序列.");

            Object.DestroyImmediate(loopOffGo);
            Object.DestroyImmediate(loopOnGo);
            Object.DestroyImmediate(asset);
        }
    }
}
