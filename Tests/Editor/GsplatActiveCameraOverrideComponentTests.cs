// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using UnityEngine;

namespace Gsplat.Tests
{
    public sealed class GsplatActiveCameraOverrideComponentTests
    {
        GameObject m_goA;
        GameObject m_goB;

        [SetUp]
        public void SetUp()
        {
            // 目的: 每个用例开始时都清空 override,避免静态状态污染断言.
            GsplatSorter.Instance.ActiveGameCameraOverride = null;

            // 说明:
            // - 这里不依赖场景里的任何默认相机.
            // - 相机类型只要是 Game/VR 即可,一般新建 Camera 默认就是 Game.
            m_goA = new GameObject("OverrideCamA");
            m_goA.AddComponent<Camera>();

            m_goB = new GameObject("OverrideCamB");
            m_goB.AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            // 清理:
            // - DestroyImmediate 确保 OnDisable/OnDestroy 在 EditMode tests 里立刻执行,
            //   使 `GsplatActiveCameraOverride` 能把自己从静态列表中移除.
            if (m_goA)
                Object.DestroyImmediate(m_goA);
            if (m_goB)
                Object.DestroyImmediate(m_goB);

            GsplatSorter.Instance.ActiveGameCameraOverride = null;
        }

        [Test]
        public void SingleOverrideComponent_SetsSorterOverrideCamera()
        {
            var camA = m_goA.GetComponent<Camera>();
            Assert.IsNotNull(camA);

            m_goA.AddComponent<GsplatActiveCameraOverride>();

            Assert.AreEqual(camA, GsplatSorter.Instance.ActiveGameCameraOverride);
        }

        [Test]
        public void HigherPriority_Wins()
        {
            var camA = m_goA.GetComponent<Camera>();
            var camB = m_goB.GetComponent<Camera>();
            Assert.IsNotNull(camA);
            Assert.IsNotNull(camB);

            var a = m_goA.AddComponent<GsplatActiveCameraOverride>();
            a.Priority = 0;

            var b = m_goB.AddComponent<GsplatActiveCameraOverride>();
            b.Priority = 10;

            Assert.AreEqual(camB, GsplatSorter.Instance.ActiveGameCameraOverride);
        }

        [Test]
        public void SamePriority_LastEnabledWins_AndDisablingRestoresPrevious()
        {
            var camA = m_goA.GetComponent<Camera>();
            var camB = m_goB.GetComponent<Camera>();
            Assert.IsNotNull(camA);
            Assert.IsNotNull(camB);

            // 同优先级:
            var a = m_goA.AddComponent<GsplatActiveCameraOverride>();
            a.Priority = 0;

            var b = m_goB.AddComponent<GsplatActiveCameraOverride>();
            b.Priority = 0;

            // 后启用者 wins:
            Assert.AreEqual(camB, GsplatSorter.Instance.ActiveGameCameraOverride);

            // 禁用后应回退到另一个仍启用的 override:
            b.enabled = false;
            Assert.AreEqual(camA, GsplatSorter.Instance.ActiveGameCameraOverride);
        }
    }
}

