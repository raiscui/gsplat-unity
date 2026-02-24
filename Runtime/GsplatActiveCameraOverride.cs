// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using UnityEngine;

namespace Gsplat
{
    /// <summary>
    /// 让项目可以“指定一个 Game/VR Camera 作为 Gsplat 的 ActiveCamera”.
    /// 这样就不需要依赖 Editor 的焦点/鼠标窗口信号,从而避免 ActiveCameraOnly 下的视口闪烁.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class GsplatActiveCameraOverride : MonoBehaviour
    {
        [Tooltip(
            "当场景里存在多个 Override 组件时,优先级更高者生效.\n" +
            "当优先级相同时,最后启用(Enable)的那个生效.")]
        public int Priority = 0;

        static readonly List<GsplatActiveCameraOverride> s_instances = new();
        static long s_sequenceCounter;

        Camera m_cachedCamera;
        long m_sequence;

        Camera GetCamera()
        {
            if (!m_cachedCamera)
                m_cachedCamera = GetComponent<Camera>();
            return m_cachedCamera;
        }

        void OnEnable()
        {
            // 记录启用顺序,用于同优先级 tie-break.
            m_sequence = ++s_sequenceCounter;

            if (!s_instances.Contains(this))
                s_instances.Add(this);

            ApplyBestOverrideCamera();
        }

        void OnDisable()
        {
            s_instances.Remove(this);
            ApplyBestOverrideCamera();
        }

        void OnDestroy()
        {
            // Unity 的回调顺序在某些编辑器场景下可能不稳定,这里做一次兜底清理.
            s_instances.Remove(this);
            ApplyBestOverrideCamera();
        }

        static void ApplyBestOverrideCamera()
        {
            // ----------------------------------------------------------------
            // 说明:
            // - Override 只能指向 Game/VR 相机,避免把 Preview/Reflection 等内部相机误设为 ActiveCamera.
            // - 只在组件启用/禁用时重算,避免每帧扫描带来的额外开销.
            // ----------------------------------------------------------------
            GsplatActiveCameraOverride best = null;

            // 清理掉已销毁的实例引用,避免列表长期积累 null.
            for (var i = s_instances.Count - 1; i >= 0; i--)
            {
                if (s_instances[i])
                    continue;
                s_instances.RemoveAt(i);
            }

            for (var i = 0; i < s_instances.Count; i++)
            {
                var inst = s_instances[i];
                if (!inst || !inst.isActiveAndEnabled)
                    continue;

                var cam = inst.GetCamera();
                if (!cam || !cam.isActiveAndEnabled)
                    continue;

                var t = cam.cameraType;
                if (t != CameraType.Game && t != CameraType.VR)
                    continue;

                if (best == null)
                {
                    best = inst;
                    continue;
                }

                if (inst.Priority > best.Priority ||
                    (inst.Priority == best.Priority && inst.m_sequence > best.m_sequence))
                {
                    best = inst;
                }
            }

            var bestCam = best ? best.GetCamera() : null;
            GsplatSorter.Instance.ActiveGameCameraOverride = bestCam;
        }
    }
}

