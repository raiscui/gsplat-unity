// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Gsplat.Tests
{
    public sealed class GsplatVfxBinderTests
    {
        [Test]
        public void AutoAssignsDefaultVfxComputeShader_WhenMissing()
        {
            // 目的: 防止用户手工搭建 VFX Graph 工作流时漏配 compute shader,
            // 导致 `GsplatVfxBinder.UpdateBinding` 每帧输出 error,并且影响排查其它问题.
            //
            // 设计点(重要):
            // - 这里故意不用 `Gsplat.VFX.GsplatVfxBinder` 的强类型引用,
            //   避免测试程序集在编译期硬依赖 `Unity.VisualEffectGraph.Runtime`.
            // - 这样在“未安装 VFX Graph 包”的项目中,本测试可以直接 Ignore,
            //   不会因为缺少程序集引用而导致整个 tests 编译失败.
            var binderType = typeof(GsplatUtils).Assembly.GetType("Gsplat.VFX.GsplatVfxBinder");
            if (binderType == null)
            {
                Assert.Ignore("未启用/未安装 VFX Graph,跳过 `GsplatVfxBinder` 的 Editor 自愈测试.");
                return;
            }

            const string expectedPath = GsplatUtils.k_PackagePath + "Runtime/Shaders/GsplatVfx.compute";
            var expected = AssetDatabase.LoadAssetAtPath<ComputeShader>(expectedPath);
            Assert.NotNull(expected, $"测试依赖缺失: 找不到 compute shader: {expectedPath}");

            var go = new GameObject("GsplatVfxBinderTests");
            try
            {
                var component = go.AddComponent(binderType);
                Assert.NotNull(component);

                // 模拟“用户漏配 compute shader”: 手动清空后重新启用组件,
                // 触发 binder 的 Editor-only 兜底逻辑自动回填默认 compute shader.
                var vfxComputeField = binderType.GetField("VfxComputeShader");
                Assert.NotNull(vfxComputeField);
                vfxComputeField.SetValue(component, null);

                var behaviour = component as Behaviour;
                Assert.NotNull(behaviour);
                behaviour.enabled = false;
                behaviour.enabled = true;

                var assigned = vfxComputeField.GetValue(component) as ComputeShader;
                Assert.NotNull(assigned);
                Assert.AreEqual(expectedPath, AssetDatabase.GetAssetPath(assigned));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
