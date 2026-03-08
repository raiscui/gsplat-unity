// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gsplat
{
    /// <summary>
    /// RadarScan 外部目标 helper.
    ///
    /// 设计目标:
    /// - 把外部 `GameObject[]` 的真实 mesh 命中逻辑,与现有 gsplat GPU range image 解耦.
    /// - 通过隔离 `PhysicsScene` + MeshCollider 代理 + RaycastCommand 批量查询,产出 per-cell 外部 hit.
    /// - 输出结果直接写回 `GsplatLidarScan` 的 external hit buffers,供最终 LiDAR shader 逐 cell 竞争最近命中.
    /// </summary>
    internal sealed class GsplatLidarExternalTargetHelper : IDisposable
    {
        const uint k_lidarInfBits = 0x7f7fffff;
        const int k_minCommandsPerJob = 64;
        static readonly int k_baseColor = Shader.PropertyToID("_BaseColor");
        static readonly int k_color = Shader.PropertyToID("_Color");

        struct SubMeshTriangleRange
        {
            public int StartTriangle;
            public int EndTriangleExclusive;
        }

        sealed class ProxyEntry
        {
            public int SourceRendererId;
            public Renderer SourceRenderer;
            public MeshFilter SourceMeshFilter;
            public SkinnedMeshRenderer SourceSkinnedRenderer;
            public GameObject ProxyObject;
            public Transform ProxyTransform;
            public MeshCollider ProxyCollider;
            public Mesh CurrentMesh;
            public Mesh BakedMesh;
            public Material[] SharedMaterials;
            public SubMeshTriangleRange[] SubMeshTriangleRanges;
            public bool HasCapturedForceRenderingOff;
            public bool OriginalForceRenderingOff;
            public bool ForceRenderingOffApplied;

            public bool IsSkinned => SourceSkinnedRenderer != null;
        }

        Scene m_proxyScene;
        PhysicsScene m_proxyPhysicsScene;
        bool m_proxySceneReady;

        readonly Dictionary<int, ProxyEntry> m_entriesBySourceRendererId = new();
        readonly Dictionary<int, ProxyEntry> m_entriesByColliderId = new();
        readonly HashSet<int> m_seenRendererIdsThisPass = new();
        readonly HashSet<string> m_loggedWarnings = new();
        readonly List<int> m_staleRendererIds = new();

        NativeArray<RaycastCommand> m_commands;
        NativeArray<RaycastHit> m_results;

        Vector3[] m_localDirections;
        uint[] m_hitRangeSqBits;
        Vector4[] m_hitBaseColors;

        int m_cachedDirectionAzimuthBins;
        int m_cachedDirectionBeamCount;
        float m_cachedDirectionAzimuthMinRad = float.NaN;
        float m_cachedDirectionAzimuthMaxRad = float.NaN;
        float m_cachedDirectionBeamMaxRad = float.NaN;
        float m_cachedDirectionBeamMinRad = float.NaN;

        public void Dispose()
        {
            DisposeNativeArrays();
            DisposeAllProxyEntries();
            TryUnloadProxyScene();
        }

        public bool TryUpdateExternalHits(GsplatLidarScan lidarScan,
            GameObject[] lidarExternalTargets,
            GsplatLidarExternalTargetVisibilityMode visibilityMode,
            Transform lidarSensorTransform,
            in GsplatLidarLayout layout,
            float depthFar)
        {
            if (lidarScan == null)
                return false;

            var cellCount = layout.CellCount;

            EnsureHitScratch(cellCount);
            ClearHitScratch(cellCount);

            SyncProxyEntries(lidarExternalTargets);
            if (!lidarSensorTransform || m_entriesBySourceRendererId.Count == 0)
            {
                ApplySourceRendererVisibilityMode(GsplatLidarExternalTargetVisibilityMode.KeepVisible);
                lidarScan.UploadExternalHits(m_hitRangeSqBits, m_hitBaseColors, cellCount);
                return false;
            }

            if (!EnsureProxyScene())
            {
                ApplySourceRendererVisibilityMode(GsplatLidarExternalTargetVisibilityMode.KeepVisible);
                lidarScan.UploadExternalHits(m_hitRangeSqBits, m_hitBaseColors, cellCount);
                return false;
            }

            ApplySourceRendererVisibilityMode(visibilityMode);
            EnsureDirections(layout);
            UpdateProxyEntriesFromSource();
            Physics.SyncTransforms();

            ScheduleRaycasts(lidarSensorTransform, cellCount, depthFar);
            ResolveHitsFromResults(cellCount);

            lidarScan.UploadExternalHits(m_hitRangeSqBits, m_hitBaseColors, cellCount);
            return true;
        }

        public static bool TryComputeWorldBounds(GameObject[] lidarExternalTargets, out Bounds worldBounds)
        {
            worldBounds = default;
            if (lidarExternalTargets == null || lidarExternalTargets.Length == 0)
                return false;

            var hasBounds = false;
            var seenRendererIds = new HashSet<int>();
            for (var rootIndex = 0; rootIndex < lidarExternalTargets.Length; rootIndex++)
            {
                var root = lidarExternalTargets[rootIndex];
                if (!root)
                    continue;

                var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: false);
                for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    var renderer = renderers[rendererIndex];
                    if (!IsSupportedRenderer(renderer))
                        continue;

                    var rendererId = renderer.GetInstanceID();
                    if (!seenRendererIds.Add(rendererId))
                        continue;

                    if (!hasBounds)
                    {
                        worldBounds = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        worldBounds.Encapsulate(renderer.bounds);
                    }
                }
            }

            return hasBounds;
        }

        static bool IsSupportedRenderer(Renderer renderer)
        {
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                return false;

            if (renderer is MeshRenderer)
                return true;

            return renderer is SkinnedMeshRenderer;
        }

        void SyncProxyEntries(GameObject[] lidarExternalTargets)
        {
            m_seenRendererIdsThisPass.Clear();

            if (lidarExternalTargets != null)
            {
                for (var rootIndex = 0; rootIndex < lidarExternalTargets.Length; rootIndex++)
                {
                    var root = lidarExternalTargets[rootIndex];
                    if (!root)
                        continue;

                    var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: false);
                    for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                    {
                        var renderer = renderers[rendererIndex];
                        if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                            continue;

                        TryTrackRenderer(renderer, root);
                    }
                }
            }

            m_staleRendererIds.Clear();
            foreach (var pair in m_entriesBySourceRendererId)
            {
                if (!m_seenRendererIdsThisPass.Contains(pair.Key))
                    m_staleRendererIds.Add(pair.Key);
            }

            for (var i = 0; i < m_staleRendererIds.Count; i++)
                RemoveProxyEntry(m_staleRendererIds[i]);
        }

        void TryTrackRenderer(Renderer renderer, GameObject root)
        {
            var rendererId = renderer.GetInstanceID();
            if (!m_seenRendererIdsThisPass.Add(rendererId))
                return;

            if (renderer is MeshRenderer meshRenderer)
            {
                var meshFilter = meshRenderer.GetComponent<MeshFilter>();
                if (!meshFilter || !meshFilter.sharedMesh)
                {
                    LogWarningOnce($"missing-meshfilter:{rendererId}",
                        $"[Gsplat][LiDAR][ExternalTargets] 已忽略缺少 MeshFilter/sharedMesh 的 MeshRenderer: {renderer.name}", renderer);
                    return;
                }

                EnsureProxyEntry(meshRenderer, meshFilter, null);
                return;
            }

            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                if (!skinnedMeshRenderer.sharedMesh)
                {
                    LogWarningOnce($"missing-skinned-mesh:{rendererId}",
                        $"[Gsplat][LiDAR][ExternalTargets] 已忽略缺少 sharedMesh 的 SkinnedMeshRenderer: {renderer.name}", renderer);
                    return;
                }

                EnsureProxyEntry(skinnedMeshRenderer, null, skinnedMeshRenderer);
                return;
            }

            LogWarningOnce($"unsupported:{rendererId}",
                $"[Gsplat][LiDAR][ExternalTargets] 已忽略暂不支持的 Renderer 类型 `{renderer.GetType().Name}`. root={root.name}, renderer={renderer.name}", renderer);
        }

        void EnsureProxyEntry(Renderer sourceRenderer, MeshFilter sourceMeshFilter, SkinnedMeshRenderer sourceSkinnedRenderer)
        {
            var rendererId = sourceRenderer.GetInstanceID();
            if (m_entriesBySourceRendererId.TryGetValue(rendererId, out var existingEntry))
            {
                existingEntry.SourceRenderer = sourceRenderer;
                existingEntry.SourceMeshFilter = sourceMeshFilter;
                existingEntry.SourceSkinnedRenderer = sourceSkinnedRenderer;
                return;
            }

            if (!EnsureProxyScene())
                return;

            var proxyObject = new GameObject($"LiDARExternalProxy_{sourceRenderer.name}")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            SceneManager.MoveGameObjectToScene(proxyObject, m_proxyScene);

            var proxyCollider = proxyObject.AddComponent<MeshCollider>();
            proxyCollider.hideFlags = HideFlags.HideAndDontSave;

            var entry = new ProxyEntry
            {
                SourceRendererId = rendererId,
                SourceRenderer = sourceRenderer,
                SourceMeshFilter = sourceMeshFilter,
                SourceSkinnedRenderer = sourceSkinnedRenderer,
                ProxyObject = proxyObject,
                ProxyTransform = proxyObject.transform,
                ProxyCollider = proxyCollider,
            };

            m_entriesBySourceRendererId.Add(rendererId, entry);
            m_entriesByColliderId[proxyCollider.GetInstanceID()] = entry;
        }

        bool EnsureProxyScene()
        {
            if (m_proxySceneReady && m_proxyScene.IsValid() && m_proxyScene.isLoaded && m_proxyPhysicsScene.IsValid())
                return true;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                m_proxyScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            }
            else
#endif
            m_proxyScene = SceneManager.CreateScene(
                $"GsplatLidarExternalTargets_{GetHashCode()}",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            m_proxyPhysicsScene = m_proxyScene.GetPhysicsScene();
            m_proxySceneReady = m_proxyScene.IsValid() && m_proxyScene.isLoaded && m_proxyPhysicsScene.IsValid();

            if (!m_proxySceneReady)
            {
                Debug.LogWarning("[Gsplat][LiDAR][ExternalTargets] 创建隔离 PhysicsScene 失败,本轮 external target 扫描将被跳过.");
                return false;
            }

            return true;
        }

        void UpdateProxyEntriesFromSource()
        {
            m_staleRendererIds.Clear();
            foreach (var pair in m_entriesBySourceRendererId)
            {
                var entry = pair.Value;
                if (!TryRefreshProxyEntry(entry))
                    m_staleRendererIds.Add(pair.Key);
            }

            for (var i = 0; i < m_staleRendererIds.Count; i++)
                RemoveProxyEntry(m_staleRendererIds[i]);
        }

        bool TryRefreshProxyEntry(ProxyEntry entry)
        {
            if (entry == null || !entry.SourceRenderer || !entry.ProxyCollider || !entry.ProxyTransform)
                return false;

            var sourceTransform = entry.SourceRenderer.transform;
            entry.ProxyTransform.SetPositionAndRotation(sourceTransform.position, sourceTransform.rotation);
            entry.ProxyTransform.localScale = sourceTransform.lossyScale;

            if (entry.IsSkinned)
                return RefreshSkinnedProxy(entry);

            return RefreshStaticMeshProxy(entry);
        }

        bool RefreshStaticMeshProxy(ProxyEntry entry)
        {
            if (!entry.SourceMeshFilter || !entry.SourceMeshFilter.sharedMesh)
                return false;

            var sharedMesh = entry.SourceMeshFilter.sharedMesh;
            if (entry.CurrentMesh != sharedMesh)
            {
                entry.CurrentMesh = sharedMesh;
                entry.ProxyCollider.sharedMesh = sharedMesh;
                RefreshMaterialCache(entry, sharedMesh);
            }

            return true;
        }

        bool RefreshSkinnedProxy(ProxyEntry entry)
        {
            var skinnedRenderer = entry.SourceSkinnedRenderer;
            if (!skinnedRenderer || !skinnedRenderer.sharedMesh)
                return false;

            entry.BakedMesh ??= CreateBakedMesh(skinnedRenderer);
            skinnedRenderer.BakeMesh(entry.BakedMesh, useScale: true);

            entry.CurrentMesh = entry.BakedMesh;
            entry.ProxyCollider.sharedMesh = null;
            entry.ProxyCollider.sharedMesh = entry.BakedMesh;
            RefreshMaterialCache(entry, entry.BakedMesh);
            return true;
        }

        static Mesh CreateBakedMesh(SkinnedMeshRenderer skinnedRenderer)
        {
            var bakedMesh = new Mesh
            {
                name = $"LiDARExternalBaked_{skinnedRenderer.name}",
                hideFlags = HideFlags.HideAndDontSave
            };
            bakedMesh.MarkDynamic();
            return bakedMesh;
        }

        void RefreshMaterialCache(ProxyEntry entry, Mesh mesh)
        {
            entry.SharedMaterials = entry.SourceRenderer.sharedMaterials;
            var subMeshCount = mesh ? mesh.subMeshCount : 0;
            if (subMeshCount <= 0)
            {
                entry.SubMeshTriangleRanges = Array.Empty<SubMeshTriangleRange>();
                return;
            }

            if (entry.SubMeshTriangleRanges == null || entry.SubMeshTriangleRanges.Length != subMeshCount)
                entry.SubMeshTriangleRanges = new SubMeshTriangleRange[subMeshCount];

            var startTriangle = 0;
            for (var subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                var triangleCount = (int)mesh.GetIndexCount(subMeshIndex) / 3;
                entry.SubMeshTriangleRanges[subMeshIndex] = new SubMeshTriangleRange
                {
                    StartTriangle = startTriangle,
                    EndTriangleExclusive = startTriangle + triangleCount
                };
                startTriangle += triangleCount;
            }
        }

        void EnsureDirections(in GsplatLidarLayout layout)
        {
            var azimuthBins = Mathf.Max(layout.ActiveAzimuthBins, 1);
            var beamCount = Mathf.Max(layout.ActiveBeamCount, 1);
            var cellCount = Mathf.Max(azimuthBins * beamCount, 1);
            var needsRebuild = m_localDirections == null ||
                               m_localDirections.Length != cellCount ||
                               m_cachedDirectionAzimuthBins != azimuthBins ||
                               m_cachedDirectionBeamCount != beamCount ||
                               !Mathf.Approximately(m_cachedDirectionAzimuthMinRad, layout.AzimuthMinRad) ||
                               !Mathf.Approximately(m_cachedDirectionAzimuthMaxRad, layout.AzimuthMaxRad) ||
                               !Mathf.Approximately(m_cachedDirectionBeamMaxRad, layout.BeamMaxRad) ||
                               !Mathf.Approximately(m_cachedDirectionBeamMinRad, layout.BeamMinRad);
            if (!needsRebuild)
                return;

            m_localDirections = new Vector3[cellCount];

            var cellIndex = 0;
            var beamSpanRad = layout.BeamSpanRad;
            var azimuthSpanRad = layout.AzimuthSpanRad;
            for (var beamIndex = 0; beamIndex < beamCount; beamIndex++)
            {
                var beamCenter01 = (beamIndex + 0.5f) / beamCount;
                var beamRad = layout.BeamMinRad + beamCenter01 * beamSpanRad;
                var beamSin = Mathf.Sin(beamRad);
                var beamCos = Mathf.Cos(beamRad);

                for (var azimuthIndex = 0; azimuthIndex < azimuthBins; azimuthIndex++)
                {
                    var azimuthCenter01 = (azimuthIndex + 0.5f) / azimuthBins;
                    var azimuthRad = layout.AzimuthMinRad + azimuthCenter01 * azimuthSpanRad;
                    var azimuthSin = Mathf.Sin(azimuthRad);
                    var azimuthCos = Mathf.Cos(azimuthRad);
                    m_localDirections[cellIndex++] = new Vector3(azimuthSin * beamCos, beamSin, azimuthCos * beamCos);
                }
            }

            m_cachedDirectionAzimuthBins = azimuthBins;
            m_cachedDirectionBeamCount = beamCount;
            m_cachedDirectionAzimuthMinRad = layout.AzimuthMinRad;
            m_cachedDirectionAzimuthMaxRad = layout.AzimuthMaxRad;
            m_cachedDirectionBeamMaxRad = layout.BeamMaxRad;
            m_cachedDirectionBeamMinRad = layout.BeamMinRad;
        }

        void EnsureHitScratch(int cellCount)
        {
            if (m_hitRangeSqBits == null || m_hitRangeSqBits.Length != cellCount)
                m_hitRangeSqBits = new uint[cellCount];

            if (m_hitBaseColors == null || m_hitBaseColors.Length != cellCount)
                m_hitBaseColors = new Vector4[cellCount];

            if (!m_commands.IsCreated || m_commands.Length != cellCount)
            {
                DisposeNativeArrays();
                m_commands = new NativeArray<RaycastCommand>(cellCount, Allocator.Persistent);
                m_results = new NativeArray<RaycastHit>(cellCount, Allocator.Persistent);
            }
        }

        void ClearHitScratch(int cellCount)
        {
            for (var i = 0; i < cellCount; i++)
            {
                m_hitRangeSqBits[i] = k_lidarInfBits;
                m_hitBaseColors[i] = Vector4.zero;
            }
        }

        void ScheduleRaycasts(Transform lidarSensorTransform, int cellCount, float depthFar)
        {
            var origin = lidarSensorTransform.position;
            var rotation = lidarSensorTransform.rotation;
            var maxDistance = Mathf.Max(depthFar, 0.0f);
            var queryParameters = new QueryParameters(Physics.DefaultRaycastLayers,
                false,
                QueryTriggerInteraction.Ignore,
                false);

            for (var cellId = 0; cellId < cellCount; cellId++)
            {
                var direction = rotation * m_localDirections[cellId];
                m_commands[cellId] = new RaycastCommand(m_proxyPhysicsScene, origin, direction, queryParameters,
                    maxDistance);
            }

            var handle = RaycastCommand.ScheduleBatch(m_commands, m_results, k_minCommandsPerJob, 1, default);
            handle.Complete();
        }

        void ResolveHitsFromResults(int cellCount)
        {
            for (var cellId = 0; cellId < cellCount; cellId++)
            {
                var hit = m_results[cellId];
                if (!hit.collider)
                    continue;

                if (!m_entriesByColliderId.TryGetValue(hit.collider.GetInstanceID(), out var entry) || entry == null)
                    continue;

                var distance = Mathf.Max(hit.distance, 0.0f);
                var rangeSq = distance * distance;
                m_hitRangeSqBits[cellId] = unchecked((uint)BitConverter.SingleToInt32Bits(rangeSq));
                m_hitBaseColors[cellId] = ResolveBaseColor(entry, hit.triangleIndex);
            }
        }

        static Vector4 ResolveBaseColor(ProxyEntry entry, int triangleIndex)
        {
            var material = ResolveHitMaterial(entry, triangleIndex);
            if (!material)
                return Color.white;

            if (material.HasProperty(k_baseColor))
                return material.GetColor(k_baseColor);

            if (material.HasProperty(k_color))
                return material.GetColor(k_color);

            return Color.white;
        }

        static Material ResolveHitMaterial(ProxyEntry entry, int triangleIndex)
        {
            var materials = entry.SharedMaterials;
            if (materials == null || materials.Length == 0)
                return null;

            if (triangleIndex < 0 || entry.SubMeshTriangleRanges == null || entry.SubMeshTriangleRanges.Length == 0)
                return materials[0];

            for (var subMeshIndex = 0; subMeshIndex < entry.SubMeshTriangleRanges.Length; subMeshIndex++)
            {
                var range = entry.SubMeshTriangleRanges[subMeshIndex];
                if (triangleIndex < range.StartTriangle || triangleIndex >= range.EndTriangleExclusive)
                    continue;

                if (subMeshIndex >= 0 && subMeshIndex < materials.Length)
                    return materials[subMeshIndex];

                return materials[0];
            }

            return materials[0];
        }

        void RemoveProxyEntry(int sourceRendererId)
        {
            if (!m_entriesBySourceRendererId.TryGetValue(sourceRendererId, out var entry))
                return;

            m_entriesBySourceRendererId.Remove(sourceRendererId);
            if (entry.ProxyCollider)
                m_entriesByColliderId.Remove(entry.ProxyCollider.GetInstanceID());

            DestroyProxyEntry(entry);
        }

        void DisposeAllProxyEntries()
        {
            foreach (var pair in m_entriesBySourceRendererId)
                DestroyProxyEntry(pair.Value);

            m_entriesBySourceRendererId.Clear();
            m_entriesByColliderId.Clear();
        }

        void DestroyProxyEntry(ProxyEntry entry)
        {
            if (entry == null)
                return;

            RestoreSourceRendererVisibility(entry);

            if (entry.BakedMesh)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEngine.Object.DestroyImmediate(entry.BakedMesh);
                else
#endif
                    UnityEngine.Object.Destroy(entry.BakedMesh);
            }

            if (entry.ProxyObject)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEngine.Object.DestroyImmediate(entry.ProxyObject);
                else
#endif
                    UnityEngine.Object.Destroy(entry.ProxyObject);
            }
        }

        void TryUnloadProxyScene()
        {
            if (!m_proxyScene.IsValid() || !m_proxyScene.isLoaded)
                return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(m_proxyScene))
                    UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(m_proxyScene);
                else
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(m_proxyScene, removeScene: true);
            }
            else
#endif
            SceneManager.UnloadSceneAsync(m_proxyScene);
            m_proxyScene = default;
            m_proxyPhysicsScene = default;
            m_proxySceneReady = false;
        }

        void DisposeNativeArrays()
        {
            if (m_commands.IsCreated)
                m_commands.Dispose();
            if (m_results.IsCreated)
                m_results.Dispose();
        }

        void ApplySourceRendererVisibilityMode(GsplatLidarExternalTargetVisibilityMode visibilityMode)
        {
            foreach (var pair in m_entriesBySourceRendererId)
                ApplySourceRendererVisibility(pair.Value, visibilityMode);
        }

        static void ApplySourceRendererVisibility(ProxyEntry entry,
            GsplatLidarExternalTargetVisibilityMode visibilityMode)
        {
            if (entry == null)
                return;

            if (ShouldForceSourceRendererOff(visibilityMode, Application.isPlaying))
            {
                CaptureAndForceSourceRendererOff(entry);
                return;
            }

            RestoreSourceRendererVisibility(entry);
        }

        static bool ShouldForceSourceRendererOff(GsplatLidarExternalTargetVisibilityMode visibilityMode, bool isPlaying)
        {
            if (visibilityMode == GsplatLidarExternalTargetVisibilityMode.ForceRenderingOff)
                return true;

            if (visibilityMode == GsplatLidarExternalTargetVisibilityMode.ForceRenderingOffInPlayMode)
                return isPlaying;

            return false;
        }

        static void CaptureAndForceSourceRendererOff(ProxyEntry entry)
        {
            if (entry == null || !entry.SourceRenderer)
                return;

            if (!entry.HasCapturedForceRenderingOff)
            {
                entry.OriginalForceRenderingOff = entry.SourceRenderer.forceRenderingOff;
                entry.HasCapturedForceRenderingOff = true;
            }

            entry.SourceRenderer.forceRenderingOff = true;
            entry.ForceRenderingOffApplied = true;
        }

        static void RestoreSourceRendererVisibility(ProxyEntry entry)
        {
            if (entry == null)
                return;

            if (entry.SourceRenderer && entry.HasCapturedForceRenderingOff)
                entry.SourceRenderer.forceRenderingOff = entry.OriginalForceRenderingOff;

            entry.HasCapturedForceRenderingOff = false;
            entry.ForceRenderingOffApplied = false;
        }

        void LogWarningOnce(string key, string message, UnityEngine.Object context)
        {
            if (!m_loggedWarnings.Add(key))
                return;

            Debug.LogWarning(message, context);
        }
    }
}
