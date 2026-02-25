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

        static void AdvanceVisibilityState(GsplatRenderer renderer)
        {
            // 说明:
            // - Unity EditMode tests 的执行环境下,ExecuteAlways.Update 可能不会稳定触发.
            // - 但我们仍需要验证“时间推进后进入 Hidden,并让 Valid=false”的门禁语义.
            // - 因此这里用反射显式推进一次状态机,让测试不依赖 Editor PlayerLoop 行为细节.
            Assert.IsNotNull(s_advanceVisibilityStateIfNeeded, "Expected GsplatRenderer.AdvanceVisibilityStateIfNeeded to exist.");
            s_advanceVisibilityStateIfNeeded.Invoke(renderer, null);
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
