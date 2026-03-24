// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    /// <summary>
    /// frustum 模式下的 external GPU capture manager.
    ///
    /// 职责边界:
    /// - static / dynamic 两组 external renderer 的收集与资源管理.
    /// - static invalidation signature / dynamic cadence.
    /// - 离屏 capture(depth + surfaceColor) 与最终 resolve.
    /// - surround360 / GPU 不可用时,仍由外层回退旧 CPU helper.
    /// </summary>
    internal sealed class GsplatLidarExternalGpuCapture : IDisposable
    {
        const int k_resolveThreads = 256;
        const int k_depthPassIndex = 0;
        const int k_colorPassIndex = 1;

        const string k_dirtyNone = "none";
        const string k_dirtyUninitialized = "uninitialized";
        const string k_dirtyCaptureInvalid = "capture-invalid";
        const string k_dirtyCameraPose = "camera-pose";
        const string k_dirtyCameraProjection = "camera-projection";
        const string k_dirtyCameraAspect = "camera-aspect";
        const string k_dirtyCameraPixelRect = "camera-pixel-rect";
        const string k_dirtyCaptureLayout = "capture-layout";
        const string k_dirtyRendererSet = "renderer-set";
        const string k_dirtyRendererState = "renderer-state";
        const string k_dirtyRendererTransform = "renderer-transform";
        const string k_dirtyRendererMesh = "renderer-mesh";
        const string k_dirtyRendererMaterial = "renderer-material";
        const string k_dynamicCadenceDue = "cadence-due";
        const string k_dynamicNowReset = "time-reset";
        const float k_forwardZClearDepth = 1.0f;
        const float k_reversedZClearDepth = 0.0f;

        static readonly int k_lidarCaptureBaseColor = Shader.PropertyToID("_LidarCaptureBaseColor");
        static readonly int k_lidarExternalDepthZTest = Shader.PropertyToID("_LidarExternalDepthZTest");
        static readonly int k_lidarCellCount = Shader.PropertyToID("_LidarCellCount");
        static readonly int k_lidarAzimuthBins = Shader.PropertyToID("_LidarAzimuthBins");
        static readonly int k_lidarBeamCount = Shader.PropertyToID("_LidarBeamCount");
        static readonly int k_lidarAzSinCos = Shader.PropertyToID("_LidarAzSinCos");
        static readonly int k_lidarBeamSinCos = Shader.PropertyToID("_LidarBeamSinCos");
        static readonly int k_lidarExternalCaptureProjection = Shader.PropertyToID("_LidarExternalCaptureProjection");
        static readonly int k_lidarExternalStaticCaptureSize = Shader.PropertyToID("_LidarExternalStaticCaptureSize");
        static readonly int k_lidarExternalDynamicCaptureSize = Shader.PropertyToID("_LidarExternalDynamicCaptureSize");
        static readonly int k_lidarExternalStaticLinearDepthTex = Shader.PropertyToID("_LidarExternalStaticLinearDepthTex");
        static readonly int k_lidarExternalStaticSurfaceColorTex = Shader.PropertyToID("_LidarExternalStaticSurfaceColorTex");
        static readonly int k_lidarExternalDynamicLinearDepthTex = Shader.PropertyToID("_LidarExternalDynamicLinearDepthTex");
        static readonly int k_lidarExternalDynamicSurfaceColorTex = Shader.PropertyToID("_LidarExternalDynamicSurfaceColorTex");
        static readonly int k_lidarExternalRangeSqBits = Shader.PropertyToID("_LidarExternalRangeSqBits");
        static readonly int k_lidarExternalBaseColor = Shader.PropertyToID("_LidarExternalBaseColor");
        static readonly int k_baseColor = Shader.PropertyToID("_BaseColor");
        static readonly int k_color = Shader.PropertyToID("_Color");

        sealed class CaptureEntry
        {
            public int SourceRendererId;
            public Renderer SourceRenderer;
            public MeshFilter SourceMeshFilter;
            public SkinnedMeshRenderer SourceSkinnedRenderer;
            public Mesh BakedMesh;
            public Mesh CurrentMesh;
            public Material[] SharedMaterials = Array.Empty<Material>();
            public Color[] SubMeshBaseColors = Array.Empty<Color>();
            public int[] SubMeshMaterialIds = Array.Empty<int>();
            public bool HasCapturedForceRenderingOff;
            public bool OriginalForceRenderingOff;
            public bool ForceRenderingOffApplied;

            public bool IsSkinned => SourceSkinnedRenderer != null;
        }

        sealed class CaptureBuffers
        {
            public RenderTexture LinearDepthTexture;
            public RenderTexture SurfaceColorTexture;
            public RenderTexture DepthStencilTexture;
            public int CaptureWidth;
            public int CaptureHeight;

            public bool Valid =>
                LinearDepthTexture && LinearDepthTexture.IsCreated() &&
                SurfaceColorTexture && SurfaceColorTexture.IsCreated() &&
                DepthStencilTexture && DepthStencilTexture.IsCreated();
        }

        sealed class EntrySnapshot
        {
            public int RendererId;
            public bool RendererEnabled;
            public bool RendererActiveInHierarchy;
            public Matrix4x4 LocalToWorldMatrix;
            public int MeshId;
            public int SubMeshCount;
            public int[] SubMeshMaterialIds = Array.Empty<int>();
            public Color[] SubMeshBaseColors = Array.Empty<Color>();
        }

        sealed class CaptureSignature
        {
            public Vector3 CameraPosition;
            public Quaternion CameraRotation;
            public Matrix4x4 CameraProjection;
            public float CameraAspect;
            public Rect CameraPixelRect;
            public int CaptureWidth;
            public int CaptureHeight;
            public int ActiveAzimuthBins;
            public int ActiveBeamCount;
            public float AzimuthMinRad;
            public float AzimuthMaxRad;
            public float BeamMinRad;
            public float BeamMaxRad;
            public int Hash;
            public EntrySnapshot[] Entries = Array.Empty<EntrySnapshot>();
        }

        sealed class StaticCaptureState
        {
            public readonly CaptureBuffers Buffers = new();
            public CaptureSignature Signature;
            public bool CaptureValid;
            public string LastDirtyReason = k_dirtyUninitialized;
        }

        sealed class DynamicCaptureState
        {
            public readonly CaptureBuffers Buffers = new();
            public CaptureSignature StructureSignature;
            public bool CaptureValid;
            public double LastCaptureRealtime = -1.0;
            public string LastRefreshReason = k_dirtyUninitialized;
        }

        readonly Dictionary<int, CaptureEntry> m_staticEntriesByRendererId = new();
        readonly Dictionary<int, CaptureEntry> m_dynamicEntriesByRendererId = new();
        readonly List<CaptureEntry> m_staticEntries = new();
        readonly List<CaptureEntry> m_dynamicEntries = new();
        readonly List<CaptureEntry> m_drawableEntriesScratch = new();
        readonly HashSet<int> m_seenRendererIds = new();
        readonly List<int> m_staleRendererIds = new();
        readonly HashSet<string> m_loggedWarnings = new();

        readonly StaticCaptureState m_staticCaptureState = new();
        readonly DynamicCaptureState m_dynamicCaptureState = new();

        CommandBuffer m_cmd;
        Material m_materialInstance;
        Shader m_materialShader;
        MaterialPropertyBlock m_propertyBlock;

        ComputeShader m_compute;
        int m_kernelResolveExternalFrustumHits = -1;

        RenderTexture m_fallbackLinearDepthTexture;
        RenderTexture m_fallbackSurfaceColorTexture;
        GraphicsBuffer m_lastResolvedRangeSqBitsBuffer;
        GraphicsBuffer m_lastResolvedBaseColorBuffer;
        bool m_hasResolvedExternalHits;

        public int StaticEntryCount => m_staticEntries.Count;
        public int DynamicEntryCount => m_dynamicEntries.Count;
        public bool StaticCaptureValid => m_staticCaptureState.CaptureValid;
        public bool DynamicCaptureValid => m_dynamicCaptureState.CaptureValid;
        public string LastStaticDirtyReason => m_staticCaptureState.LastDirtyReason;
        public string LastDynamicRefreshReason => m_dynamicCaptureState.LastRefreshReason;

        public void Dispose()
        {
            RestoreAllSourceRendererVisibility();

            DisposeGroupEntries(m_staticEntriesByRendererId);
            DisposeGroupEntries(m_dynamicEntriesByRendererId);
            m_staticEntries.Clear();
            m_dynamicEntries.Clear();
            m_drawableEntriesScratch.Clear();

            ResetStaticCaptureState();
            ResetDynamicCaptureState();
            DisposeFallbackTextures();
            DisposeMaterialInstance();

            if (m_cmd != null)
            {
                m_cmd.Release();
                m_cmd = null;
            }

            m_compute = null;
            m_kernelResolveExternalFrustumHits = -1;
            m_lastResolvedRangeSqBitsBuffer = null;
            m_lastResolvedBaseColorBuffer = null;
            m_hasResolvedExternalHits = false;
        }

        public bool TryCaptureExternalHits(GsplatLidarScan lidarScan,
            GsplatSettings settings,
            Camera frustumCamera,
            in GsplatLidarLayout layout,
            GameObject[] staticTargets,
            GameObject[] dynamicTargets,
            float dynamicUpdateHz,
            GsplatLidarExternalCaptureResolutionMode captureResolutionMode,
            float captureResolutionScale,
            Vector2Int explicitCaptureResolution,
            GsplatLidarExternalTargetVisibilityMode visibilityMode)
        {
            if (lidarScan == null || settings == null || !frustumCamera)
                return false;

            if (!layout.IsFrustum || frustumCamera.orthographic)
                return false;

            if (!lidarScan.RangeImageValid || !lidarScan.LutValid)
                return false;

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsComputeShaders)
                return false;

            if (!settings.ComputeShader || !settings.LidarExternalCaptureMaterial)
                return false;

            SyncGroupEntries(staticTargets, m_staticEntriesByRendererId, m_staticEntries);
            SyncGroupEntries(dynamicTargets, m_dynamicEntriesByRendererId, m_dynamicEntries);

            if (m_staticEntries.Count == 0 && m_dynamicEntries.Count == 0)
            {
                RestoreAllSourceRendererVisibility();
                DisposeFallbackTextures();
                ResetStaticCaptureState();
                ResetDynamicCaptureState();
                m_lastResolvedRangeSqBitsBuffer = null;
                m_lastResolvedBaseColorBuffer = null;
                m_hasResolvedExternalHits = false;
                lidarScan.ClearExternalHits(Mathf.Max(layout.CellCount, 1));
                return false;
            }

            if (!EnsureMaterialInstance(settings.LidarExternalCaptureMaterial))
                return false;

            if (!EnsureResolveKernel(settings.ComputeShader))
                return false;

            if (!TryResolveCaptureSize(frustumCamera,
                    layout,
                    captureResolutionMode,
                    captureResolutionScale,
                    explicitCaptureResolution,
                    out var captureWidth,
                    out var captureHeight))
                return false;

            EnsureFallbackTextures();

            var viewMatrix = frustumCamera.worldToCameraMatrix;
            var projectionMatrix = GL.GetGPUProjectionMatrix(frustumCamera.projectionMatrix, true);
            var now = (double)Time.realtimeSinceStartup;

            ApplyVisibilityMode(visibilityMode);

            var staticChanged = UpdateStaticCaptureIfNeeded(frustumCamera,
                viewMatrix,
                projectionMatrix,
                layout,
                captureWidth,
                captureHeight);
            var dynamicChanged = UpdateDynamicCaptureIfNeeded(frustumCamera,
                viewMatrix,
                projectionMatrix,
                layout,
                captureWidth,
                captureHeight,
                now,
                dynamicUpdateHz);

            var needsResolve = staticChanged ||
                               dynamicChanged ||
                               !m_hasResolvedExternalHits ||
                               !ReferenceEquals(m_lastResolvedRangeSqBitsBuffer, lidarScan.ExternalRangeSqBitsBuffer) ||
                               !ReferenceEquals(m_lastResolvedBaseColorBuffer, lidarScan.ExternalBaseColorBuffer);

            if (!needsResolve)
                return true;

            return ExecuteResolve(lidarScan, settings.ComputeShader, layout, projectionMatrix);
        }

        void SyncGroupEntries(GameObject[] roots,
            Dictionary<int, CaptureEntry> entriesByRendererId,
            List<CaptureEntry> orderedEntries)
        {
            orderedEntries.Clear();
            m_seenRendererIds.Clear();

            if (roots != null)
            {
                for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                {
                    var root = roots[rootIndex];
                    if (!root)
                        continue;

                    var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
                    for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                    {
                        var renderer = renderers[rendererIndex];
                        TryTrackRenderer(renderer, root, entriesByRendererId, orderedEntries);
                    }
                }
            }

            m_staleRendererIds.Clear();
            foreach (var pair in entriesByRendererId)
            {
                if (!m_seenRendererIds.Contains(pair.Key))
                    m_staleRendererIds.Add(pair.Key);
            }

            for (var i = 0; i < m_staleRendererIds.Count; i++)
                RemoveEntry(entriesByRendererId, m_staleRendererIds[i]);

            orderedEntries.Sort(static (a, b) => a.SourceRendererId.CompareTo(b.SourceRendererId));
        }

        void TryTrackRenderer(Renderer renderer,
            GameObject root,
            Dictionary<int, CaptureEntry> entriesByRendererId,
            List<CaptureEntry> orderedEntries)
        {
            if (!IsTrackableRendererType(renderer))
                return;

            var rendererId = renderer.GetInstanceID();
            if (!m_seenRendererIds.Add(rendererId))
                return;

            if (renderer is MeshRenderer meshRenderer)
            {
                var meshFilter = meshRenderer.GetComponent<MeshFilter>();
                if (!meshFilter || !meshFilter.sharedMesh)
                {
                    LogWarningOnce($"missing-meshfilter:{rendererId}",
                        $"[Gsplat][LiDAR][ExternalGpuCapture] 已忽略缺少 MeshFilter/sharedMesh 的 MeshRenderer: {renderer.name}",
                        renderer);
                    return;
                }

                orderedEntries.Add(EnsureEntry(entriesByRendererId, meshRenderer, meshFilter, null));
                return;
            }

            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                if (!skinnedMeshRenderer.sharedMesh)
                {
                    LogWarningOnce($"missing-skinned-mesh:{rendererId}",
                        $"[Gsplat][LiDAR][ExternalGpuCapture] 已忽略缺少 sharedMesh 的 SkinnedMeshRenderer: {renderer.name}",
                        renderer);
                    return;
                }

                orderedEntries.Add(EnsureEntry(entriesByRendererId, skinnedMeshRenderer, null, skinnedMeshRenderer));
                return;
            }

            LogWarningOnce($"unsupported:{rendererId}",
                $"[Gsplat][LiDAR][ExternalGpuCapture] 已忽略暂不支持的 Renderer 类型 `{renderer.GetType().Name}`. root={root.name}, renderer={renderer.name}",
                renderer);
        }

        static bool IsTrackableRendererType(Renderer renderer)
        {
            if (!renderer)
                return false;

            if (renderer is MeshRenderer)
                return true;

            return renderer is SkinnedMeshRenderer;
        }

        CaptureEntry EnsureEntry(Dictionary<int, CaptureEntry> entriesByRendererId,
            Renderer sourceRenderer,
            MeshFilter sourceMeshFilter,
            SkinnedMeshRenderer sourceSkinnedRenderer)
        {
            var rendererId = sourceRenderer.GetInstanceID();
            if (entriesByRendererId.TryGetValue(rendererId, out var entry))
            {
                entry.SourceRenderer = sourceRenderer;
                entry.SourceMeshFilter = sourceMeshFilter;
                entry.SourceSkinnedRenderer = sourceSkinnedRenderer;
                return entry;
            }

            entry = new CaptureEntry
            {
                SourceRendererId = rendererId,
                SourceRenderer = sourceRenderer,
                SourceMeshFilter = sourceMeshFilter,
                SourceSkinnedRenderer = sourceSkinnedRenderer,
            };
            entriesByRendererId.Add(rendererId, entry);
            return entry;
        }

        bool UpdateStaticCaptureIfNeeded(Camera frustumCamera,
            Matrix4x4 viewMatrix,
            Matrix4x4 projectionMatrix,
            in GsplatLidarLayout layout,
            int captureWidth,
            int captureHeight)
        {
            if (m_staticEntries.Count == 0)
            {
                m_staticCaptureState.LastDirtyReason = k_dirtyRendererSet;
                return ResetStaticCaptureState();
            }

            var currentSignature = BuildCaptureSignature(m_staticEntries,
                frustumCamera,
                layout,
                captureWidth,
                captureHeight,
                includeTransforms: true);
            if (currentSignature == null)
            {
                m_staticCaptureState.LastDirtyReason = k_dirtyCaptureInvalid;
                return ResetStaticCaptureState();
            }

            var dirtyReason = GetCaptureDirtyReason(m_staticCaptureState.Signature,
                currentSignature,
                m_staticCaptureState.CaptureValid,
                includeTransforms: true);
            m_staticCaptureState.LastDirtyReason = dirtyReason;
            if (dirtyReason == k_dirtyNone)
                return false;

            EnsureCaptureBuffers(m_staticCaptureState.Buffers, captureWidth, captureHeight);
            CaptureGroup(m_staticEntries, m_staticCaptureState.Buffers, viewMatrix, projectionMatrix);
            m_staticCaptureState.Signature = currentSignature;
            m_staticCaptureState.CaptureValid = true;
            return true;
        }

        bool UpdateDynamicCaptureIfNeeded(Camera frustumCamera,
            Matrix4x4 viewMatrix,
            Matrix4x4 projectionMatrix,
            in GsplatLidarLayout layout,
            int captureWidth,
            int captureHeight,
            double nowRealtime,
            float dynamicUpdateHz)
        {
            if (m_dynamicEntries.Count == 0)
            {
                m_dynamicCaptureState.LastRefreshReason = k_dirtyRendererSet;
                return ResetDynamicCaptureState();
            }

            var structureSignature = BuildCaptureSignature(m_dynamicEntries,
                frustumCamera,
                layout,
                captureWidth,
                captureHeight,
                includeTransforms: false);
            if (structureSignature == null)
            {
                m_dynamicCaptureState.LastRefreshReason = k_dirtyCaptureInvalid;
                return ResetDynamicCaptureState();
            }

            var structureDirtyReason = GetCaptureDirtyReason(m_dynamicCaptureState.StructureSignature,
                structureSignature,
                m_dynamicCaptureState.CaptureValid,
                includeTransforms: false);
            var cadenceDue = IsDynamicCaptureUpdateDue(nowRealtime, dynamicUpdateHz, m_dynamicCaptureState.LastCaptureRealtime,
                out var cadenceReason);
            if (structureDirtyReason == k_dirtyNone && !cadenceDue)
                return false;

            m_dynamicCaptureState.LastRefreshReason = structureDirtyReason != k_dirtyNone
                ? structureDirtyReason
                : cadenceReason;

            EnsureCaptureBuffers(m_dynamicCaptureState.Buffers, captureWidth, captureHeight);
            CaptureGroup(m_dynamicEntries, m_dynamicCaptureState.Buffers, viewMatrix, projectionMatrix);
            m_dynamicCaptureState.StructureSignature = structureSignature;
            m_dynamicCaptureState.CaptureValid = true;
            m_dynamicCaptureState.LastCaptureRealtime = SanitizeRealtime(nowRealtime);
            return true;
        }

        CaptureSignature BuildCaptureSignature(List<CaptureEntry> entries,
            Camera frustumCamera,
            in GsplatLidarLayout layout,
            int captureWidth,
            int captureHeight,
            bool includeTransforms)
        {
            if (!frustumCamera)
                return null;

            var snapshots = new EntrySnapshot[entries.Count];
            var hash = 17;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || !entry.SourceRenderer || !TryRefreshEntry(entry, bakeSkinned: false))
                    return null;

                var snapshot = BuildEntrySnapshot(entry, includeTransforms);
                snapshots[i] = snapshot;
                hash = CombineHash(hash, snapshot.RendererId);
                hash = CombineHash(hash, snapshot.RendererEnabled ? 1 : 0);
                hash = CombineHash(hash, snapshot.RendererActiveInHierarchy ? 1 : 0);
                if (includeTransforms)
                    hash = CombineHash(hash, snapshot.LocalToWorldMatrix);
                hash = CombineHash(hash, snapshot.MeshId);
                hash = CombineHash(hash, snapshot.SubMeshCount);
                for (var subMeshIndex = 0; subMeshIndex < snapshot.SubMeshMaterialIds.Length; subMeshIndex++)
                    hash = CombineHash(hash, snapshot.SubMeshMaterialIds[subMeshIndex]);
                for (var subMeshIndex = 0; subMeshIndex < snapshot.SubMeshBaseColors.Length; subMeshIndex++)
                    hash = CombineHash(hash, snapshot.SubMeshBaseColors[subMeshIndex]);
            }

            var signature = new CaptureSignature
            {
                CameraPosition = frustumCamera.transform.position,
                CameraRotation = frustumCamera.transform.rotation,
                CameraProjection = frustumCamera.projectionMatrix,
                CameraAspect = frustumCamera.aspect,
                CameraPixelRect = frustumCamera.pixelRect,
                CaptureWidth = captureWidth,
                CaptureHeight = captureHeight,
                ActiveAzimuthBins = Mathf.Max(layout.ActiveAzimuthBins, 1),
                ActiveBeamCount = Mathf.Max(layout.ActiveBeamCount, 1),
                AzimuthMinRad = layout.AzimuthMinRad,
                AzimuthMaxRad = layout.AzimuthMaxRad,
                BeamMinRad = layout.BeamMinRad,
                BeamMaxRad = layout.BeamMaxRad,
                Entries = snapshots,
            };

            hash = CombineHash(hash, signature.CameraPosition);
            hash = CombineHash(hash, signature.CameraRotation);
            hash = CombineHash(hash, signature.CameraProjection);
            hash = CombineHash(hash, signature.CameraAspect);
            hash = CombineHash(hash, signature.CameraPixelRect);
            hash = CombineHash(hash, signature.CaptureWidth);
            hash = CombineHash(hash, signature.CaptureHeight);
            hash = CombineHash(hash, signature.ActiveAzimuthBins);
            hash = CombineHash(hash, signature.ActiveBeamCount);
            hash = CombineHash(hash, signature.AzimuthMinRad);
            hash = CombineHash(hash, signature.AzimuthMaxRad);
            hash = CombineHash(hash, signature.BeamMinRad);
            hash = CombineHash(hash, signature.BeamMaxRad);
            signature.Hash = hash;
            return signature;
        }

        static EntrySnapshot BuildEntrySnapshot(CaptureEntry entry, bool includeTransforms)
        {
            var mesh = ResolveSourceMesh(entry);
            var subMeshCount = mesh ? Mathf.Max(mesh.subMeshCount, 0) : 0;
            var snapshot = new EntrySnapshot
            {
                RendererId = entry.SourceRendererId,
                RendererEnabled = entry.SourceRenderer && entry.SourceRenderer.enabled,
                RendererActiveInHierarchy = entry.SourceRenderer && entry.SourceRenderer.gameObject.activeInHierarchy,
                LocalToWorldMatrix = includeTransforms && entry.SourceRenderer
                    ? entry.SourceRenderer.transform.localToWorldMatrix
                    : Matrix4x4.identity,
                MeshId = mesh ? mesh.GetInstanceID() : 0,
                SubMeshCount = subMeshCount,
                SubMeshMaterialIds = new int[subMeshCount],
                SubMeshBaseColors = new Color[subMeshCount],
            };

            for (var subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                snapshot.SubMeshMaterialIds[subMeshIndex] =
                    subMeshIndex < entry.SubMeshMaterialIds.Length ? entry.SubMeshMaterialIds[subMeshIndex] : 0;
                snapshot.SubMeshBaseColors[subMeshIndex] =
                    subMeshIndex < entry.SubMeshBaseColors.Length ? entry.SubMeshBaseColors[subMeshIndex] : Color.white;
            }

            return snapshot;
        }

        static Mesh ResolveSourceMesh(CaptureEntry entry)
        {
            if (entry == null)
                return null;

            if (entry.SourceMeshFilter)
                return entry.SourceMeshFilter.sharedMesh;

            if (entry.SourceSkinnedRenderer)
                return entry.SourceSkinnedRenderer.sharedMesh;

            return null;
        }

        static string GetCaptureDirtyReason(CaptureSignature previous,
            CaptureSignature current,
            bool captureValid,
            bool includeTransforms)
        {
            if (current == null)
                return k_dirtyCaptureInvalid;

            if (previous == null)
                return k_dirtyUninitialized;

            if (!captureValid)
                return k_dirtyCaptureInvalid;

            if (!Approximately(previous.CameraPosition, current.CameraPosition) ||
                !Approximately(previous.CameraRotation, current.CameraRotation))
            {
                return k_dirtyCameraPose;
            }

            if (!Approximately(previous.CameraProjection, current.CameraProjection))
                return k_dirtyCameraProjection;

            if (!Mathf.Approximately(previous.CameraAspect, current.CameraAspect))
                return k_dirtyCameraAspect;

            if (!Approximately(previous.CameraPixelRect, current.CameraPixelRect))
                return k_dirtyCameraPixelRect;

            if (previous.CaptureWidth != current.CaptureWidth ||
                previous.CaptureHeight != current.CaptureHeight ||
                previous.ActiveAzimuthBins != current.ActiveAzimuthBins ||
                previous.ActiveBeamCount != current.ActiveBeamCount ||
                !Mathf.Approximately(previous.AzimuthMinRad, current.AzimuthMinRad) ||
                !Mathf.Approximately(previous.AzimuthMaxRad, current.AzimuthMaxRad) ||
                !Mathf.Approximately(previous.BeamMinRad, current.BeamMinRad) ||
                !Mathf.Approximately(previous.BeamMaxRad, current.BeamMaxRad))
            {
                return k_dirtyCaptureLayout;
            }

            if (previous.Entries.Length != current.Entries.Length)
                return k_dirtyRendererSet;

            for (var i = 0; i < current.Entries.Length; i++)
            {
                var previousEntry = previous.Entries[i];
                var currentEntry = current.Entries[i];
                if (previousEntry.RendererId != currentEntry.RendererId)
                    return k_dirtyRendererSet;

                if (previousEntry.RendererEnabled != currentEntry.RendererEnabled ||
                    previousEntry.RendererActiveInHierarchy != currentEntry.RendererActiveInHierarchy)
                {
                    return k_dirtyRendererState;
                }

                if (includeTransforms &&
                    !Approximately(previousEntry.LocalToWorldMatrix, currentEntry.LocalToWorldMatrix))
                {
                    return k_dirtyRendererTransform;
                }

                if (previousEntry.MeshId != currentEntry.MeshId ||
                    previousEntry.SubMeshCount != currentEntry.SubMeshCount)
                {
                    return k_dirtyRendererMesh;
                }

                if (previousEntry.SubMeshMaterialIds.Length != currentEntry.SubMeshMaterialIds.Length ||
                    previousEntry.SubMeshBaseColors.Length != currentEntry.SubMeshBaseColors.Length)
                {
                    return k_dirtyRendererMaterial;
                }

                for (var subMeshIndex = 0; subMeshIndex < currentEntry.SubMeshMaterialIds.Length; subMeshIndex++)
                {
                    if (previousEntry.SubMeshMaterialIds[subMeshIndex] != currentEntry.SubMeshMaterialIds[subMeshIndex])
                        return k_dirtyRendererMaterial;

                    if (!Approximately(previousEntry.SubMeshBaseColors[subMeshIndex],
                            currentEntry.SubMeshBaseColors[subMeshIndex]))
                    {
                        return k_dirtyRendererMaterial;
                    }
                }
            }

            return k_dirtyNone;
        }

        static bool IsDynamicCaptureUpdateDue(double nowRealtime,
            float updateHz,
            double lastCaptureRealtime,
            out string reason)
        {
            if (float.IsNaN(updateHz) || float.IsInfinity(updateHz) || updateHz <= 0.0f)
                updateHz = 10.0f;

            if (double.IsNaN(nowRealtime) || double.IsInfinity(nowRealtime))
            {
                reason = k_dynamicCadenceDue;
                return true;
            }

            if (lastCaptureRealtime < 0.0)
            {
                reason = k_dirtyUninitialized;
                return true;
            }

            if (nowRealtime < lastCaptureRealtime)
            {
                reason = k_dynamicNowReset;
                return true;
            }

            var interval = 1.0 / updateHz;
            if (double.IsNaN(interval) || double.IsInfinity(interval) || interval <= 0.0)
                interval = 0.1;

            if ((nowRealtime - lastCaptureRealtime) >= interval)
            {
                reason = k_dynamicCadenceDue;
                return true;
            }

            reason = k_dirtyNone;
            return false;
        }

        static double SanitizeRealtime(double nowRealtime)
        {
            if (double.IsNaN(nowRealtime) || double.IsInfinity(nowRealtime))
                return 0.0;

            return nowRealtime;
        }

        bool TryRefreshEntry(CaptureEntry entry, bool bakeSkinned)
        {
            if (entry == null || !entry.SourceRenderer)
                return false;

            if (entry.IsSkinned)
                return RefreshSkinnedEntry(entry, bakeSkinned);

            return RefreshStaticEntry(entry);
        }

        bool RefreshStaticEntry(CaptureEntry entry)
        {
            if (!entry.SourceMeshFilter || !entry.SourceMeshFilter.sharedMesh)
                return false;

            entry.CurrentMesh = entry.SourceMeshFilter.sharedMesh;
            RefreshMaterialCache(entry, entry.CurrentMesh);
            return entry.CurrentMesh && entry.CurrentMesh.subMeshCount > 0;
        }

        bool RefreshSkinnedEntry(CaptureEntry entry, bool bakeSkinned)
        {
            var skinnedRenderer = entry.SourceSkinnedRenderer;
            if (!skinnedRenderer || !skinnedRenderer.sharedMesh)
                return false;

            if (bakeSkinned)
            {
                entry.BakedMesh ??= CreateBakedMesh(skinnedRenderer);
                skinnedRenderer.BakeMesh(entry.BakedMesh, useScale: true);
                entry.CurrentMesh = entry.BakedMesh;
            }
            else
            {
                entry.CurrentMesh = skinnedRenderer.sharedMesh;
            }

            RefreshMaterialCache(entry, entry.CurrentMesh ? entry.CurrentMesh : skinnedRenderer.sharedMesh);
            return entry.CurrentMesh && entry.CurrentMesh.subMeshCount > 0;
        }

        static Mesh CreateBakedMesh(SkinnedMeshRenderer skinnedRenderer)
        {
            var bakedMesh = new Mesh
            {
                name = $"LiDARExternalGpuCapture_{skinnedRenderer.name}",
                hideFlags = HideFlags.HideAndDontSave
            };
            bakedMesh.MarkDynamic();
            return bakedMesh;
        }

        void RefreshMaterialCache(CaptureEntry entry, Mesh mesh)
        {
            entry.SharedMaterials = entry.SourceRenderer ? entry.SourceRenderer.sharedMaterials : Array.Empty<Material>();
            var subMeshCount = mesh ? Mathf.Max(mesh.subMeshCount, 0) : 0;
            if (subMeshCount <= 0)
            {
                entry.SubMeshBaseColors = Array.Empty<Color>();
                entry.SubMeshMaterialIds = Array.Empty<int>();
                return;
            }

            if (entry.SubMeshBaseColors == null || entry.SubMeshBaseColors.Length != subMeshCount)
                entry.SubMeshBaseColors = new Color[subMeshCount];

            if (entry.SubMeshMaterialIds == null || entry.SubMeshMaterialIds.Length != subMeshCount)
                entry.SubMeshMaterialIds = new int[subMeshCount];

            for (var subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
            {
                var material = ResolveMaterialForSubMesh(entry.SharedMaterials, subMeshIndex);
                entry.SubMeshMaterialIds[subMeshIndex] = material ? material.GetInstanceID() : 0;
                entry.SubMeshBaseColors[subMeshIndex] = ResolveBaseColor(material);
            }
        }

        static Material ResolveMaterialForSubMesh(Material[] materials, int subMeshIndex)
        {
            if (materials == null || materials.Length == 0)
                return null;

            if (subMeshIndex >= 0 && subMeshIndex < materials.Length && materials[subMeshIndex])
                return materials[subMeshIndex];

            return materials[0];
        }

        static Color ResolveBaseColor(Material material)
        {
            if (!material)
                return Color.white;

            if (material.HasProperty(k_baseColor))
                return material.GetColor(k_baseColor);

            if (material.HasProperty(k_color))
                return material.GetColor(k_color);

            return Color.white;
        }

        bool EnsureMaterialInstance(Material sourceMaterial)
        {
            if (!sourceMaterial || !sourceMaterial.shader)
                return false;

            if (m_materialInstance && m_materialShader == sourceMaterial.shader)
            {
                m_materialInstance.CopyPropertiesFromMaterial(sourceMaterial);
                return true;
            }

            DisposeMaterialInstance();
            m_materialInstance = new Material(sourceMaterial) { hideFlags = HideFlags.HideAndDontSave };
            m_materialShader = sourceMaterial.shader;
            return true;
        }

        bool EnsureResolveKernel(ComputeShader computeShader)
        {
            if (!computeShader)
            {
                m_compute = null;
                m_kernelResolveExternalFrustumHits = -1;
                return false;
            }

            if (m_compute == computeShader && m_kernelResolveExternalFrustumHits >= 0)
                return true;

            try
            {
                m_kernelResolveExternalFrustumHits = computeShader.FindKernel("ResolveExternalFrustumHits");
                m_compute = computeShader;
                return m_kernelResolveExternalFrustumHits >= 0;
            }
            catch (Exception)
            {
                m_compute = null;
                m_kernelResolveExternalFrustumHits = -1;
                return false;
            }
        }

        static bool TryResolveAutoCaptureBaseSize(Camera frustumCamera,
            in GsplatLidarLayout layout,
            out int captureWidth,
            out int captureHeight)
        {
            captureWidth = 0;
            captureHeight = 0;

            if (!frustumCamera)
                return false;

            var pixelRect = frustumCamera.pixelRect;
            if (pixelRect.width > 0.0f && pixelRect.height > 0.0f)
            {
                captureWidth = Mathf.Max(Mathf.RoundToInt(pixelRect.width), 1);
                captureHeight = Mathf.Max(Mathf.RoundToInt(pixelRect.height), 1);
                return true;
            }

            if (frustumCamera.targetTexture)
            {
                captureWidth = Mathf.Max(frustumCamera.targetTexture.width, 1);
                captureHeight = Mathf.Max(frustumCamera.targetTexture.height, 1);
                return true;
            }

            captureWidth = Mathf.Max(layout.ActiveAzimuthBins, 1);
            captureHeight = Mathf.Max(layout.ActiveBeamCount, 1);
            return true;
        }

        static int ClampCaptureDimensionToHardwareLimit(int value)
        {
            return Mathf.Clamp(value, 1, Mathf.Max(SystemInfo.maxTextureSize, 1));
        }

        static int ResolveScaledCaptureDimension(int baseValue, float scale)
        {
            var scaledValue = (double)baseValue * scale;
            if (double.IsNaN(scaledValue) || double.IsInfinity(scaledValue) || scaledValue <= 0.0)
                return ClampCaptureDimensionToHardwareLimit(baseValue);

            var roundedValue = scaledValue >= int.MaxValue
                ? int.MaxValue
                : (int)Math.Round(scaledValue, MidpointRounding.AwayFromZero);
            return ClampCaptureDimensionToHardwareLimit(Mathf.Max(roundedValue, 1));
        }

        static bool TryResolveCaptureSize(Camera frustumCamera,
            in GsplatLidarLayout layout,
            GsplatLidarExternalCaptureResolutionMode captureResolutionMode,
            float captureResolutionScale,
            Vector2Int explicitCaptureResolution,
            out int captureWidth,
            out int captureHeight)
        {
            captureWidth = 0;
            captureHeight = 0;

            var sanitizedMode =
                GsplatUtils.SanitizeLidarExternalCaptureResolutionMode(captureResolutionMode);
            var sanitizedScale =
                float.IsNaN(captureResolutionScale) || float.IsInfinity(captureResolutionScale) || captureResolutionScale <= 0.0f
                    ? 1.0f
                    : captureResolutionScale;

            if (sanitizedMode == GsplatLidarExternalCaptureResolutionMode.Explicit)
            {
                // 说明:
                // - 显式模式直接让用户精确指定 capture RT 宽高.
                // - 仍然需要按硬件上限 clamp,避免申请超过设备支持的纹理尺寸.
                // - 它只改变 external depth / surfaceColor capture 的离屏 fidelity,
                //   不改变后续 point resolve / nearest-hit 的选择语义.
                captureWidth = ClampCaptureDimensionToHardwareLimit(Mathf.Max(explicitCaptureResolution.x, 1));
                captureHeight = ClampCaptureDimensionToHardwareLimit(Mathf.Max(explicitCaptureResolution.y, 1));
                return true;
            }

            if (!TryResolveAutoCaptureBaseSize(frustumCamera, layout, out captureWidth, out captureHeight))
                return false;

            captureWidth = ClampCaptureDimensionToHardwareLimit(captureWidth);
            captureHeight = ClampCaptureDimensionToHardwareLimit(captureHeight);

            if (sanitizedMode != GsplatLidarExternalCaptureResolutionMode.Scale)
                return true;

            // 说明:
            // - Scale 模式在 Auto 基准尺寸上做 supersample / 降采样.
            // - 它保留默认决策来源,但把精度与性能的权衡显式交给用户控制.
            // - `scale = 1` 时应继续等价于 Auto 基准尺寸,避免给旧场景引入隐式分辨率变化.
            captureWidth = ResolveScaledCaptureDimension(captureWidth, sanitizedScale);
            captureHeight = ResolveScaledCaptureDimension(captureHeight, sanitizedScale);
            return true;
        }

        void EnsureFallbackTextures()
        {
            if (!m_fallbackLinearDepthTexture)
            {
                var linearDepthFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)
                    ? RenderTextureFormat.RFloat
                    : RenderTextureFormat.ARGBFloat;
                m_fallbackLinearDepthTexture = CreateColorTexture("GsplatLiDARExternalFallbackLinearDepth", 1, 1,
                    linearDepthFormat);
            }

            if (!m_fallbackSurfaceColorTexture)
            {
                var surfaceColorFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                    ? RenderTextureFormat.ARGBHalf
                    : RenderTextureFormat.ARGB32;
                m_fallbackSurfaceColorTexture = CreateColorTexture("GsplatLiDARExternalFallbackSurfaceColor", 1, 1,
                    surfaceColorFormat);
            }
        }

        void DisposeFallbackTextures()
        {
            DisposeRenderTexture(ref m_fallbackLinearDepthTexture);
            DisposeRenderTexture(ref m_fallbackSurfaceColorTexture);
        }

        void EnsureCaptureBuffers(CaptureBuffers buffers, int captureWidth, int captureHeight)
        {
            if (buffers == null)
                return;

            var needRecreate = !buffers.Valid ||
                               buffers.CaptureWidth != captureWidth ||
                               buffers.CaptureHeight != captureHeight;
            if (!needRecreate)
                return;

            DisposeCaptureBuffers(buffers);

            // 关键约束:
            // - linearDepth / surfaceColor / depthStencil 必须共享同一套 capture 宽高.
            // - supersampling 只能统一提高 external capture fidelity,不能让 depth / color layout 脱节.
            // - 这样最终 point resolve 才能在同一 texel 位置拿到同一最近表面的距离与颜色.
            var linearDepthFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)
                ? RenderTextureFormat.RFloat
                : RenderTextureFormat.ARGBFloat;
            buffers.LinearDepthTexture = CreateColorTexture("GsplatLiDARExternalLinearDepth", captureWidth, captureHeight,
                linearDepthFormat);

            var surfaceColorFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                ? RenderTextureFormat.ARGBHalf
                : RenderTextureFormat.ARGB32;
            buffers.SurfaceColorTexture = CreateColorTexture("GsplatLiDARExternalSurfaceColor", captureWidth, captureHeight,
                surfaceColorFormat);

            buffers.DepthStencilTexture = new RenderTexture(captureWidth, captureHeight, 24, RenderTextureFormat.Depth)
            {
                name = "GsplatLiDARExternalDepthStencil",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1
            };
            buffers.DepthStencilTexture.Create();

            buffers.CaptureWidth = captureWidth;
            buffers.CaptureHeight = captureHeight;
        }

        static RenderTexture CreateColorTexture(string name, int width, int height, RenderTextureFormat format)
        {
            var texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1
            };
            texture.Create();
            return texture;
        }

        void CaptureGroup(List<CaptureEntry> sourceEntries,
            CaptureBuffers buffers,
            Matrix4x4 viewMatrix,
            Matrix4x4 projectionMatrix)
        {
            if (buffers == null || !buffers.Valid)
                return;

            m_cmd ??= new CommandBuffer { name = "Gsplat.LidarExternalGpuCapture" };
            m_cmd.Clear();
            m_cmd.SetViewProjectionMatrices(viewMatrix, projectionMatrix);

            BuildDrawableEntries(sourceEntries, m_drawableEntriesScratch);
            var depthZTest = SystemInfo.usesReversedZBuffer
                ? (float)CompareFunction.GreaterEqual
                : (float)CompareFunction.LessEqual;
            var clearDepth = SystemInfo.usesReversedZBuffer
                ? k_reversedZClearDepth
                : k_forwardZClearDepth;
            m_materialInstance.SetFloat(k_lidarExternalDepthZTest, depthZTest);

            // 关键语义:
            // - 这里保留 `Cull Off`,避免镜像/负缩放把 front/back 判反.
            // - 但"最近表面"的选择重新交给硬件 depth buffer:
            //   depth pass 写入线性 view depth + 深度缓冲.
            // - 额外注意 reversed-Z 平台:
            //   如果仍用 `LessEqual + clearDepth=1`,闭合 mesh 会稳定把 far side 留下来.
            //   因此这里显式按平台切换 compare function 与 clear depth.
            // - color pass 使用同一个 depth/stencil,只用 `ZTest Equal` 取最近表面对应的颜色.
            // - 这样可以避免 `RFloat + BlendOp Max` 在某些平台上退化成"最后写入者赢",
            //   从而把球体/闭合 mesh 的点稳定翻到背面.
            m_cmd.SetRenderTarget(buffers.LinearDepthTexture.colorBuffer, buffers.DepthStencilTexture.depthBuffer);
            m_cmd.ClearRenderTarget(true, true, Color.clear, clearDepth);
            DrawEntriesForPass(m_drawableEntriesScratch, k_depthPassIndex, usePerSubMeshColor: false);

            m_cmd.SetRenderTarget(buffers.SurfaceColorTexture.colorBuffer, buffers.DepthStencilTexture.depthBuffer);
            m_cmd.ClearRenderTarget(false, true, Color.clear);
            DrawEntriesForPass(m_drawableEntriesScratch, k_colorPassIndex, usePerSubMeshColor: true);

            Graphics.ExecuteCommandBuffer(m_cmd);
            m_drawableEntriesScratch.Clear();
        }

        void BuildDrawableEntries(List<CaptureEntry> sourceEntries, List<CaptureEntry> drawableEntries)
        {
            drawableEntries.Clear();
            if (sourceEntries == null || sourceEntries.Count == 0)
                return;

            for (var entryIndex = 0; entryIndex < sourceEntries.Count; entryIndex++)
            {
                var entry = sourceEntries[entryIndex];
                if (entry == null || !entry.SourceRenderer)
                    continue;

                if (!entry.SourceRenderer.enabled || !entry.SourceRenderer.gameObject.activeInHierarchy)
                    continue;

                if (!TryRefreshEntry(entry, bakeSkinned: entry.IsSkinned))
                    continue;

                drawableEntries.Add(entry);
            }
        }

        bool ExecuteResolve(GsplatLidarScan lidarScan,
            ComputeShader computeShader,
            in GsplatLidarLayout layout,
            Matrix4x4 projectionMatrix)
        {
            m_cmd ??= new CommandBuffer { name = "Gsplat.LidarExternalGpuResolve" };
            m_cmd.Clear();

            m_cmd.SetComputeIntParam(computeShader, k_lidarCellCount, layout.CellCount);
            m_cmd.SetComputeIntParam(computeShader, k_lidarAzimuthBins, Mathf.Max(layout.ActiveAzimuthBins, 1));
            m_cmd.SetComputeIntParam(computeShader, k_lidarBeamCount, Mathf.Max(layout.ActiveBeamCount, 1));
            m_cmd.SetComputeMatrixParam(computeShader, k_lidarExternalCaptureProjection, projectionMatrix);
            m_cmd.SetComputeVectorParam(computeShader, k_lidarExternalStaticCaptureSize,
                ResolveCaptureSizeVector(m_staticCaptureState.Buffers, m_staticCaptureState.CaptureValid));
            m_cmd.SetComputeVectorParam(computeShader, k_lidarExternalDynamicCaptureSize,
                ResolveCaptureSizeVector(m_dynamicCaptureState.Buffers, m_dynamicCaptureState.CaptureValid));

            m_cmd.SetComputeTextureParam(computeShader, m_kernelResolveExternalFrustumHits,
                k_lidarExternalStaticLinearDepthTex,
                ResolveLinearDepthTexture(m_staticCaptureState.Buffers, m_staticCaptureState.CaptureValid));
            m_cmd.SetComputeTextureParam(computeShader, m_kernelResolveExternalFrustumHits,
                k_lidarExternalStaticSurfaceColorTex,
                ResolveSurfaceColorTexture(m_staticCaptureState.Buffers, m_staticCaptureState.CaptureValid));
            m_cmd.SetComputeTextureParam(computeShader, m_kernelResolveExternalFrustumHits,
                k_lidarExternalDynamicLinearDepthTex,
                ResolveLinearDepthTexture(m_dynamicCaptureState.Buffers, m_dynamicCaptureState.CaptureValid));
            m_cmd.SetComputeTextureParam(computeShader, m_kernelResolveExternalFrustumHits,
                k_lidarExternalDynamicSurfaceColorTex,
                ResolveSurfaceColorTexture(m_dynamicCaptureState.Buffers, m_dynamicCaptureState.CaptureValid));

            m_cmd.SetComputeBufferParam(computeShader, m_kernelResolveExternalFrustumHits,
                k_lidarAzSinCos, lidarScan.AzSinCosBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelResolveExternalFrustumHits,
                k_lidarBeamSinCos, lidarScan.BeamSinCosBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelResolveExternalFrustumHits,
                k_lidarExternalRangeSqBits, lidarScan.ExternalRangeSqBitsBuffer);
            m_cmd.SetComputeBufferParam(computeShader, m_kernelResolveExternalFrustumHits,
                k_lidarExternalBaseColor, lidarScan.ExternalBaseColorBuffer);

            var groups = DivRoundUp(layout.CellCount, k_resolveThreads);
            m_cmd.DispatchCompute(computeShader, m_kernelResolveExternalFrustumHits, groups, 1, 1);
            Graphics.ExecuteCommandBuffer(m_cmd);

            m_lastResolvedRangeSqBitsBuffer = lidarScan.ExternalRangeSqBitsBuffer;
            m_lastResolvedBaseColorBuffer = lidarScan.ExternalBaseColorBuffer;
            m_hasResolvedExternalHits = true;
            return true;
        }

        Vector4 ResolveCaptureSizeVector(CaptureBuffers buffers, bool captureValid)
        {
            if (captureValid && buffers != null && buffers.Valid)
                return new Vector4(buffers.CaptureWidth, buffers.CaptureHeight, 0.0f, 0.0f);

            return Vector4.zero;
        }

        RenderTexture ResolveLinearDepthTexture(CaptureBuffers buffers, bool captureValid)
        {
            if (captureValid && buffers != null && buffers.Valid)
                return buffers.LinearDepthTexture;

            return m_fallbackLinearDepthTexture;
        }

        RenderTexture ResolveSurfaceColorTexture(CaptureBuffers buffers, bool captureValid)
        {
            if (captureValid && buffers != null && buffers.Valid)
                return buffers.SurfaceColorTexture;

            return m_fallbackSurfaceColorTexture;
        }

        void DrawEntriesForPass(List<CaptureEntry> entries, int passIndex, bool usePerSubMeshColor)
        {
            if (entries == null || entries.Count == 0 || !m_materialInstance)
                return;

            m_propertyBlock ??= new MaterialPropertyBlock();

            for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                var entry = entries[entryIndex];
                if (entry == null || !entry.SourceRenderer || !entry.CurrentMesh)
                    continue;

                var mesh = entry.CurrentMesh;
                var subMeshCount = Mathf.Max(mesh.subMeshCount, 0);
                if (subMeshCount <= 0)
                    continue;

                var matrix = entry.SourceRenderer.transform.localToWorldMatrix;
                for (var subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
                {
                    if (usePerSubMeshColor)
                    {
                        m_propertyBlock.Clear();
                        m_propertyBlock.SetColor(k_lidarCaptureBaseColor, ResolveSubMeshBaseColor(entry, subMeshIndex));
                        m_cmd.DrawMesh(mesh, matrix, m_materialInstance, subMeshIndex, passIndex, m_propertyBlock);
                        continue;
                    }

                    m_cmd.DrawMesh(mesh, matrix, m_materialInstance, subMeshIndex, passIndex);
                }
            }
        }

        static Color ResolveSubMeshBaseColor(CaptureEntry entry, int subMeshIndex)
        {
            if (entry?.SubMeshBaseColors == null || entry.SubMeshBaseColors.Length == 0)
                return Color.white;

            if (subMeshIndex >= 0 && subMeshIndex < entry.SubMeshBaseColors.Length)
                return entry.SubMeshBaseColors[subMeshIndex];

            return entry.SubMeshBaseColors[0];
        }

        void ApplyVisibilityMode(GsplatLidarExternalTargetVisibilityMode visibilityMode)
        {
            ApplyVisibilityMode(m_staticEntriesByRendererId, visibilityMode);
            ApplyVisibilityMode(m_dynamicEntriesByRendererId, visibilityMode);
        }

        static void ApplyVisibilityMode(Dictionary<int, CaptureEntry> entriesByRendererId,
            GsplatLidarExternalTargetVisibilityMode visibilityMode)
        {
            foreach (var pair in entriesByRendererId)
                ApplySourceRendererVisibility(pair.Value, visibilityMode);
        }

        void RestoreAllSourceRendererVisibility()
        {
            ApplyVisibilityMode(m_staticEntriesByRendererId, GsplatLidarExternalTargetVisibilityMode.KeepVisible);
            ApplyVisibilityMode(m_dynamicEntriesByRendererId, GsplatLidarExternalTargetVisibilityMode.KeepVisible);
        }

        static void ApplySourceRendererVisibility(CaptureEntry entry,
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

        static void CaptureAndForceSourceRendererOff(CaptureEntry entry)
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

        static void RestoreSourceRendererVisibility(CaptureEntry entry)
        {
            if (entry == null)
                return;

            if (entry.SourceRenderer && entry.HasCapturedForceRenderingOff)
                entry.SourceRenderer.forceRenderingOff = entry.OriginalForceRenderingOff;

            entry.HasCapturedForceRenderingOff = false;
            entry.ForceRenderingOffApplied = false;
        }

        void RemoveEntry(Dictionary<int, CaptureEntry> entriesByRendererId, int sourceRendererId)
        {
            if (!entriesByRendererId.TryGetValue(sourceRendererId, out var entry))
                return;

            entriesByRendererId.Remove(sourceRendererId);
            DestroyEntry(entry);
        }

        void DisposeGroupEntries(Dictionary<int, CaptureEntry> entriesByRendererId)
        {
            foreach (var pair in entriesByRendererId)
                DestroyEntry(pair.Value);

            entriesByRendererId.Clear();
        }

        void DestroyEntry(CaptureEntry entry)
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
        }

        bool ResetStaticCaptureState()
        {
            var hadState = m_staticCaptureState.CaptureValid ||
                           m_staticCaptureState.Signature != null ||
                           m_staticCaptureState.Buffers.Valid;
            DisposeCaptureBuffers(m_staticCaptureState.Buffers);
            m_staticCaptureState.Signature = null;
            m_staticCaptureState.CaptureValid = false;
            return hadState;
        }

        bool ResetDynamicCaptureState()
        {
            var hadState = m_dynamicCaptureState.CaptureValid ||
                           m_dynamicCaptureState.StructureSignature != null ||
                           m_dynamicCaptureState.Buffers.Valid;
            DisposeCaptureBuffers(m_dynamicCaptureState.Buffers);
            m_dynamicCaptureState.StructureSignature = null;
            m_dynamicCaptureState.CaptureValid = false;
            m_dynamicCaptureState.LastCaptureRealtime = -1.0;
            return hadState;
        }

        void DisposeMaterialInstance()
        {
            if (!m_materialInstance)
                return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEngine.Object.DestroyImmediate(m_materialInstance);
            else
#endif
                UnityEngine.Object.Destroy(m_materialInstance);

            m_materialInstance = null;
            m_materialShader = null;
            m_propertyBlock = null;
        }

        void DisposeCaptureBuffers(CaptureBuffers buffers)
        {
            if (buffers == null)
                return;

            DisposeRenderTexture(ref buffers.LinearDepthTexture);
            DisposeRenderTexture(ref buffers.SurfaceColorTexture);
            DisposeRenderTexture(ref buffers.DepthStencilTexture);
            buffers.CaptureWidth = 0;
            buffers.CaptureHeight = 0;
        }

        static void DisposeRenderTexture(ref RenderTexture texture)
        {
            if (!texture)
                return;

            texture.Release();
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEngine.Object.DestroyImmediate(texture);
            else
#endif
                UnityEngine.Object.Destroy(texture);
            texture = null;
        }

        void LogWarningOnce(string key, string message, UnityEngine.Object context)
        {
            if (!m_loggedWarnings.Add(key))
                return;

            Debug.LogWarning(message, context);
        }

        static int DivRoundUp(int x, int d)
        {
            if (d <= 0)
                return 0;

            return (x + d - 1) / d;
        }

        static bool Approximately(Vector3 a, Vector3 b)
        {
            return (a - b).sqrMagnitude <= 1.0e-8f;
        }

        static bool Approximately(Quaternion a, Quaternion b)
        {
            return Quaternion.Angle(a, b) <= 1.0e-4f;
        }

        static bool Approximately(Rect a, Rect b)
        {
            return Mathf.Abs(a.x - b.x) <= 1.0e-4f &&
                   Mathf.Abs(a.y - b.y) <= 1.0e-4f &&
                   Mathf.Abs(a.width - b.width) <= 1.0e-4f &&
                   Mathf.Abs(a.height - b.height) <= 1.0e-4f;
        }

        static bool Approximately(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) <= 1.0e-5f &&
                   Mathf.Abs(a.g - b.g) <= 1.0e-5f &&
                   Mathf.Abs(a.b - b.b) <= 1.0e-5f &&
                   Mathf.Abs(a.a - b.a) <= 1.0e-5f;
        }

        static bool Approximately(Matrix4x4 a, Matrix4x4 b)
        {
            for (var i = 0; i < 16; i++)
            {
                if (Mathf.Abs(a[i] - b[i]) > 1.0e-5f)
                    return false;
            }

            return true;
        }

        static int CombineHash(int hash, int value)
        {
            unchecked
            {
                return hash * 31 + value;
            }
        }

        static int CombineHash(int hash, float value)
        {
            return CombineHash(hash, BitConverter.SingleToInt32Bits(value));
        }

        static int CombineHash(int hash, Vector3 value)
        {
            hash = CombineHash(hash, value.x);
            hash = CombineHash(hash, value.y);
            hash = CombineHash(hash, value.z);
            return hash;
        }

        static int CombineHash(int hash, Quaternion value)
        {
            hash = CombineHash(hash, value.x);
            hash = CombineHash(hash, value.y);
            hash = CombineHash(hash, value.z);
            hash = CombineHash(hash, value.w);
            return hash;
        }

        static int CombineHash(int hash, Rect value)
        {
            hash = CombineHash(hash, value.x);
            hash = CombineHash(hash, value.y);
            hash = CombineHash(hash, value.width);
            hash = CombineHash(hash, value.height);
            return hash;
        }

        static int CombineHash(int hash, Color value)
        {
            hash = CombineHash(hash, value.r);
            hash = CombineHash(hash, value.g);
            hash = CombineHash(hash, value.b);
            hash = CombineHash(hash, value.a);
            return hash;
        }

        static int CombineHash(int hash, Matrix4x4 value)
        {
            for (var i = 0; i < 16; i++)
                hash = CombineHash(hash, value[i]);
            return hash;
        }

        // --------------------------------------------------------------------
        // Debug hooks:
        // - 只给 EditMode tests / 诊断使用.
        // - 不暴露到 public API,避免把实现细节固化成对外契约.
        // --------------------------------------------------------------------
        string DebugGetStaticCaptureDirtyReasonForInputs(Camera frustumCamera,
            GsplatLidarLayout layout,
            int captureWidth,
            int captureHeight,
            GameObject[] staticTargets)
        {
            SyncGroupEntries(staticTargets, m_staticEntriesByRendererId, m_staticEntries);
            var signature = BuildCaptureSignature(m_staticEntries,
                frustumCamera,
                layout,
                captureWidth,
                captureHeight,
                includeTransforms: true);
            return GetCaptureDirtyReason(m_staticCaptureState.Signature, signature, m_staticCaptureState.CaptureValid,
                includeTransforms: true);
        }

        int DebugCommitStaticCaptureSignatureForInputs(Camera frustumCamera,
            GsplatLidarLayout layout,
            int captureWidth,
            int captureHeight,
            GameObject[] staticTargets)
        {
            SyncGroupEntries(staticTargets, m_staticEntriesByRendererId, m_staticEntries);
            var signature = BuildCaptureSignature(m_staticEntries,
                frustumCamera,
                layout,
                captureWidth,
                captureHeight,
                includeTransforms: true);
            m_staticCaptureState.Signature = signature;
            m_staticCaptureState.CaptureValid = signature != null;
            m_staticCaptureState.LastDirtyReason = signature != null ? k_dirtyNone : k_dirtyCaptureInvalid;
            return signature?.Hash ?? 0;
        }

        static float DebugComputeRayDepthSqFromLinearViewDepth(float linearViewDepth, float rayForwardDot)
        {
            if (linearViewDepth <= 0.0f || float.IsNaN(linearViewDepth) || float.IsInfinity(linearViewDepth))
                return float.PositiveInfinity;

            if (rayForwardDot <= 1.0e-6f || float.IsNaN(rayForwardDot) || float.IsInfinity(rayForwardDot))
                return float.PositiveInfinity;

            var depth = linearViewDepth / rayForwardDot;
            if (depth <= 0.0f || float.IsNaN(depth) || float.IsInfinity(depth))
                return float.PositiveInfinity;

            return depth * depth;
        }

        static Vector2Int DebugResolveCaptureSizeForInputs(Camera frustumCamera,
            GsplatLidarLayout layout,
            GsplatLidarExternalCaptureResolutionMode captureResolutionMode,
            float captureResolutionScale,
            Vector2Int explicitCaptureResolution)
        {
            return TryResolveCaptureSize(frustumCamera,
                layout,
                captureResolutionMode,
                captureResolutionScale,
                explicitCaptureResolution,
                out var captureWidth,
                out var captureHeight)
                ? new Vector2Int(captureWidth, captureHeight)
                : Vector2Int.zero;
        }
    }
}
