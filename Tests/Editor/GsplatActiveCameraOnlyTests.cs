// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using System.Reflection;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace Gsplat.Tests
{
    public sealed class GsplatActiveCameraOnlyTests
    {
        sealed class TestGsplat : IGsplat
        {
            readonly GameObject m_go;

            public TestGsplat(GameObject go)
            {
                m_go = go;
            }

            public Transform transform => m_go ? m_go.transform : null;
            public uint SplatCount => 1;
            public uint SplatBaseIndex => 0;
            public ISorterResource SorterResource => null;
            public bool isActiveAndEnabled => true;
            public bool Valid => true;

            // 说明: 这里不测试 4D 行为,只需要满足接口即可.
            public bool Has4D => false;
            public float TimeNormalized => 0.0f;
            public int TimeModel => 1;
            public float TemporalCutoff => 0.01f;
            public GraphicsBuffer VelocityBuffer => null;
            public GraphicsBuffer TimeBuffer => null;
            public GraphicsBuffer DurationBuffer => null;
        }

        GsplatCameraMode m_prevCameraMode;

        [SetUp]
        public void SetUp()
        {
            // 目的: 每个用例都强制在 ActiveCameraOnly 下运行,并清理 override.
            var settings = GsplatSettings.Instance;
            m_prevCameraMode = settings.CameraMode;
            settings.CameraMode = GsplatCameraMode.ActiveCameraOnly;

            GsplatSorter.Instance.ActiveGameCameraOverride = null;

#if UNITY_EDITOR
            // 目的: 保证测试不被项目默认场景中的 Main Camera/其它相机污染.
            // - `EmptyScene` 不会自动创建 Camera/Light,更适合做“相机选择规则”的单元测试.
            // - 这样用例里的“单相机/多相机”假设才是可控且可复现的.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
#endif
        }

        [TearDown]
        public void TearDown()
        {
            // 目的: 避免测试污染用户的 settings 资产与 sorter 全局状态.
            var settings = GsplatSettings.Instance;
            settings.CameraMode = m_prevCameraMode;

            GsplatSorter.Instance.ActiveGameCameraOverride = null;
        }

        [Test]
        public void TryGetActiveCamera_OverrideTakesPrecedence()
        {
            var goA = new GameObject("GsplatActiveCameraOnlyTests_CamA");
            var goB = new GameObject("GsplatActiveCameraOnlyTests_CamB");
            try
            {
                var camA = goA.AddComponent<Camera>();
                var camB = goB.AddComponent<Camera>();
                Assert.That(camA, Is.Not.Null);
                Assert.That(camB, Is.Not.Null);

                GsplatSorter.Instance.ActiveGameCameraOverride = camB;

                Assert.That(GsplatSorter.Instance.TryGetActiveCamera(out var active), Is.True);
                Assert.That(active, Is.EqualTo(camB));
            }
            finally
            {
                Object.DestroyImmediate(goA);
                Object.DestroyImmediate(goB);
            }
        }

        [Test]
        public void ResolveActiveGameOrVrCamera_SingleGameCamera_IsSelected()
        {
            var go = new GameObject("GsplatActiveCameraOnlyTests_SingleCam");
            try
            {
                var cam = go.AddComponent<Camera>();
                Assert.That(cam, Is.Not.Null);

                var mi = typeof(GsplatSorter).GetMethod("ResolveActiveGameOrVrCamera",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(mi, Is.Not.Null);

                var resolved = mi.Invoke(GsplatSorter.Instance, null) as Camera;
                Assert.That(resolved, Is.EqualTo(cam));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ResolveActiveGameOrVrCamera_WhenMultipleCameras_PrefersMainCameraTag()
        {
            var goA = new GameObject("GsplatActiveCameraOnlyTests_MainCam");
            var goB = new GameObject("GsplatActiveCameraOnlyTests_OtherCam");
            try
            {
                var camMain = goA.AddComponent<Camera>();
                var camOther = goB.AddComponent<Camera>();
                Assert.That(camMain, Is.Not.Null);
                Assert.That(camOther, Is.Not.Null);

                // 目的: 锁定我们选择 `Camera.main` 的规则(以 MainCamera tag 为依据).
                goA.tag = "MainCamera";

                var mi = typeof(GsplatSorter).GetMethod("ResolveActiveGameOrVrCamera",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(mi, Is.Not.Null);

                var resolved = mi.Invoke(GsplatSorter.Instance, null) as Camera;
                Assert.That(resolved, Is.EqualTo(camMain));
            }
            finally
            {
                Object.DestroyImmediate(goA);
                Object.DestroyImmediate(goB);
            }
        }

        [Test]
        public void GatherGsplatsForCamera_ActiveCameraOnly_GatesNonActiveCamera()
        {
            var gsGo = new GameObject("GsplatActiveCameraOnlyTests_Gsplat");
            var camAGo = new GameObject("GsplatActiveCameraOnlyTests_CamA");
            var camBGo = new GameObject("GsplatActiveCameraOnlyTests_CamB");

            var gs = new TestGsplat(gsGo);
            try
            {
                var camA = camAGo.AddComponent<Camera>();
                var camB = camBGo.AddComponent<Camera>();
                Assert.That(camA, Is.Not.Null);
                Assert.That(camB, Is.Not.Null);

                // 让 camA 成为 ActiveCamera.
                GsplatSorter.Instance.ActiveGameCameraOverride = camA;

                GsplatSorter.Instance.RegisterGsplat(gs);
                Assert.That(GsplatSorter.Instance.GatherGsplatsForCamera(camA), Is.True);
                Assert.That(GsplatSorter.Instance.GatherGsplatsForCamera(camB), Is.False);
            }
            finally
            {
                GsplatSorter.Instance.UnregisterGsplat(gs);
                Object.DestroyImmediate(gsGo);
                Object.DestroyImmediate(camAGo);
                Object.DestroyImmediate(camBGo);
            }
        }
    }
}
