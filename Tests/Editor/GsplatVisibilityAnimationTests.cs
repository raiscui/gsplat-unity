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
        static readonly MethodInfo s_advanceVisibilityStateIfNeeded =
            typeof(GsplatRenderer).GetMethod("AdvanceVisibilityStateIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly MethodInfo s_advanceRenderStyleStateIfNeeded =
            typeof(GsplatRenderer).GetMethod("AdvanceRenderStyleStateIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly MethodInfo s_advanceLidarAnimationStateIfNeeded =
            typeof(GsplatRenderer).GetMethod("AdvanceLidarAnimationStateIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly MethodInfo s_buildLidarShowHideOverlayForThisFrame =
            typeof(GsplatRenderer).GetMethod("BuildLidarShowHideOverlayForThisFrame", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_renderStyleBlend01Field =
            typeof(GsplatRenderer).GetField("m_renderStyleBlend01", BindingFlags.Instance | BindingFlags.NonPublic);

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

        static readonly FieldInfo s_visibilitySourceMaskModeField =
            typeof(GsplatRenderer).GetField("m_visibilitySourceMaskMode", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_visibilitySourceMaskProgressField =
            typeof(GsplatRenderer).GetField("m_visibilitySourceMaskProgress01", BindingFlags.Instance | BindingFlags.NonPublic);

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

        static float GetRenderStyleBlend01(GsplatRenderer renderer)
        {
            // 说明: 该值是 shader morph 的核心 uniform,需要锁定其收敛到目标值的语义.
            Assert.IsNotNull(s_renderStyleBlend01Field, "Expected private field 'm_renderStyleBlend01' to exist on GsplatRenderer.");
            return (float)s_renderStyleBlend01Field.GetValue(renderer);
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

        static (float gate, int mode, float progress, int sourceMode, float sourceProgress, float maxRadius) BuildLidarShowHideOverlay(
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
            return ((float)args[1], (int)args[2], (float)args[3], (int)args[4], (float)args[5], (float)args[7]);
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

        [UnityTest]
        public IEnumerator PlayHide_EndsHidden_ValidBecomesFalse()
        {
            var go = new GameObject("GsplatVisibilityAnimationTests_PlayHide");
            go.SetActive(false);
            var asset = CreateMinimalAsset1Splat();
            var r = go.AddComponent<GsplatRenderer>();

            // 显式启用显隐动画,但不要 OnEnable 自动播 show,避免测试依赖动画完成时序.
            r.EnableVisibilityAnimation = true;
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
            //   待雷达可见性淡入完成后,才因 HideSplatsWhenLidarEnabled 停掉 splat 提交.
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
                if (!GetLidarVisibilityAnimating(r))
                    break;
                yield return null;
            }

            Assert.IsFalse(GetLidarVisibilityAnimating(r), "Expected radar fade-in animation to finish within timeout.");
            Assert.AreEqual(0u, GetSubmittedSplatCount(r),
                "Expected splat submission to stop after radar fade-in completes when HideSplatsWhenLidarEnabled=true.");

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
    }
}
