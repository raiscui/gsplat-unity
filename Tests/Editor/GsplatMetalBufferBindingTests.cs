// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Gsplat.Tests
{
    public class GsplatMetalBufferBindingTests
    {
        [UnityTest]
        public IEnumerator Render_DoesNotTrigger_MetalMissingComputeBufferWarning()
        {
            // ----------------------------------------------------------------
            // 该用例的目标是防回归:
            // - macOS/Metal 下,如果 shader 需要的任意 StructuredBuffer 没绑定,Unity 会跳过 draw call 并输出 warning.
            // - 用户侧表现为: SceneView/GameView 里整体“消失/闪烁”,并且 warning 可能只打印一次.
            // - 这里我们通过捕获 log 来断言: 在正确绑定 buffers 的情况下,不应出现该 warning.
            // ----------------------------------------------------------------
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal)
                Assert.Ignore("This regression test is Metal-only.");

            // 确保 settings 可用(会触发自动创建 GsplatSettings.asset).
            var settings = GsplatSettings.Instance;
            Assert.IsNotNull(settings);

            // 强制走 ActiveCameraOnly 并使用显式 override,避免测试依赖 SceneView 的存在.
            settings.CameraMode = GsplatCameraMode.ActiveCameraOnly;

            // 创建一个最小的离屏相机,避免污染 Editor 视口.
            var camGo = new GameObject("GsplatMetalTestCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = -1;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.targetTexture = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32);

            // 构造一个最小 renderer impl(尽量贴近用户的真实配置: SH3 + 4D).
            var renderer = new GsplatRendererImpl(splatCount: 1, shBands: 3, has4D: true);
            var root = new GameObject("GsplatMetalTestRoot");
            var tr = root.transform;
            var bounds = new Bounds(Vector3.zero, Vector3.one);

            // 填充最小数据,避免 shader 读取未初始化内容.
            renderer.PositionBuffer.SetData(new[] { Vector3.zero });
            renderer.ScaleBuffer.SetData(new[] { Vector3.one });
            renderer.RotationBuffer.SetData(new[] { new Vector4(1, 0, 0, 0) });
            renderer.ColorBuffer.SetData(new[] { new Vector4(1, 1, 1, 1) });
            renderer.VelocityBuffer.SetData(new[] { Vector3.zero });
            renderer.TimeBuffer.SetData(new[] { 0.0f });
            renderer.DurationBuffer.SetData(new[] { 1.0f });

            var coeffCount = GsplatUtils.SHBandsToCoefficientCount(3);
            renderer.SHBuffer.SetData(new Vector3[coeffCount]);

            var sawMissingComputeBufferWarning = false;
            void OnLog(string condition, string stacktrace, LogType type)
            {
                if (string.IsNullOrEmpty(condition))
                    return;

                if (condition.Contains("Gsplat/Standard") &&
                    condition.Contains("requires a ComputeBuffer at index") &&
                    condition.Contains("Skipping draw calls"))
                {
                    sawMissingComputeBufferWarning = true;
                }
            }

            Application.logMessageReceived += OnLog;
            try
            {
                // 显式指定 ActiveCamera,保证 Render 只对该相机提交 draw.
                GsplatSorter.Instance.ActiveGameCameraOverride = cam;

                // 连续渲染几帧,覆盖“首次使用 shader/material”与“后续稳定渲染”两种路径.
                for (var i = 0; i < 3; i++)
                {
                    renderer.Render(
                        splatCount: 1,
                        transform: tr,
                        localBounds: bounds,
                        layer: 0,
                        gammaToLinear: false,
                        shDegree: 3,
                        timeNormalized: 0.5f,
                        motionPadding: 0.0f,
                        timeModel: 1,
                        temporalCutoff: 0.01f);

                    cam.Render();
                    yield return null;
                }
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
                GsplatSorter.Instance.ActiveGameCameraOverride = null;

                renderer.Dispose();

                if (cam.targetTexture)
                    Object.DestroyImmediate(cam.targetTexture);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(root);
            }

            Assert.IsFalse(
                sawMissingComputeBufferWarning,
                "Saw Metal warning: Gsplat/Standard requires a ComputeBuffer at index ... but none provided.");
        }
    }
}
