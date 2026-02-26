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

        static readonly FieldInfo s_renderStyleBlend01Field =
            typeof(GsplatRenderer).GetField("m_renderStyleBlend01", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly FieldInfo s_renderStyleAnimatingField =
            typeof(GsplatRenderer).GetField("m_renderStyleAnimating", BindingFlags.Instance | BindingFlags.NonPublic);

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
