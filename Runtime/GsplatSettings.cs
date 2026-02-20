// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    // 资源预算与自动降级策略(用于避免超大规模数据直接 OOM 或不可解释的失败).
    public enum GsplatAutoDegradePolicy
    {
        None = 0,
        ReduceSH = 1,
        CapSplatCount = 2,
        ReduceSHThenCapSplatCount = 3
    }

    public class GsplatSettings : ScriptableObject
    {
        const string k_gsplatSettingsResourcesPath = "GsplatSettings";

        const string k_gsplatSettingsPath =
            "Assets/Gsplat/Settings/Resources/" + k_gsplatSettingsResourcesPath + ".asset";

        static GsplatSettings s_instance;

        public static GsplatSettings Instance
        {
            get
            {
                if (s_instance)
                    return s_instance;

                var settings = Resources.Load<GsplatSettings>(k_gsplatSettingsResourcesPath);
#if UNITY_EDITOR
                if (!settings)
                {
                    var assetPath = Path.GetDirectoryName(k_gsplatSettingsPath);
                    if (!Directory.Exists(assetPath))
                        Directory.CreateDirectory(assetPath);

                    settings = CreateInstance<GsplatSettings>();
                    settings.Shader =
                        AssetDatabase.LoadAssetAtPath<Shader>(GsplatUtils.k_PackagePath +
                                                              "Runtime/Shaders/Gsplat.shader");
                    settings.ComputeShader =
                        AssetDatabase.LoadAssetAtPath<ComputeShader>(GsplatUtils.k_PackagePath +
                                                                     "Runtime/Shaders/Gsplat.compute");
                    settings.OnValidate();
                    AssetDatabase.CreateAsset(settings, k_gsplatSettingsPath);
                    AssetDatabase.SaveAssets();
                }
#endif

                s_instance = settings;
                return s_instance;
            }
        }

        public Shader Shader;
        public ComputeShader ComputeShader;

        [Tooltip(
            "每个 GPU instance 里包含的 splat quad 数量(也可以理解为 instance 的 batch size).\n" +
            "渲染时实例数约为: instanceCount = ceil(SplatCount / SplatInstanceSize).\n" +
            "它不会改变 splat 总数与排序逻辑,主要影响 mesh 大小与 instanceCount.\n" +
            "通常保持 128 即可. 数据量很大时可尝试 256/512.\n" +
            "注意: 设得过大可能触发 Mesh 顶点/索引上限问题.")]
        public uint SplatInstanceSize = 128;
        public bool ShowImportErrors = true;

        // --------------------------------------------------------------------
        // 4DGS/大数据资源预算
        // --------------------------------------------------------------------
        [Range(0.0f, 1.0f)] public float VramWarnRatio = 0.6f;
        public GsplatAutoDegradePolicy AutoDegrade = GsplatAutoDegradePolicy.None;
        public bool AutoDegradeDisableInterpolation = false;
        public uint AutoDegradeMaxSplatCount = 2000000;
        public uint MaxSplatsForVfx = 500000;

        // --------------------------------------------------------------------
        // Unity Editor Play Mode 性能开关
        // --------------------------------------------------------------------
        [Tooltip(
            "在 Unity Editor 的 Play 模式下,如果 Scene 视图也在渲染,会导致对 GameView 与 SceneView 各排序一次,性能可能显著下降.\n" +
            "开启后会在 Play 模式跳过 SceneView 相机的排序,优先保证 GameView 的流畅度.\n" +
            "注意: 这只影响 Editor 的 SceneView,不影响 Player build.")]
        public bool SkipSceneViewSortingInPlayMode = true;

        [Tooltip(
            "在 Unity Editor 的 Play 模式下,SceneView 额外渲染会带来额外 draw cost.\n" +
            "开启后会在 Play 模式仅对 Game/VR 相机提交 Gsplat draw call,从而让 SceneView 不再渲染 Gsplat.\n" +
            "注意: 这只影响 Editor 的 SceneView,不影响 Player build.")]
        public bool SkipSceneViewRenderingInPlayMode = true;

        public Material[] Materials { get; private set; }
        public Mesh Mesh { get; private set; }

        public bool Valid => Materials?.Length != 0 && Mesh && SplatInstanceSize > 0;

        Shader m_prevShader;
        ComputeShader m_prevComputeShader;
        uint m_prevSplatInstanceSize;

        static void InitSorterSafely(ComputeShader computeShader)
        {
            // ----------------------------------------------------------------
            // 为什么需要这个 guard:
            // - CI/命令行测试常用 `-batchmode -nographics`.
            // - 在这种“无图形设备”的模式下,ComputeShader 可能无法正确编译/反射 kernels,
            //   Unity 会输出 error log(例如 "Kernel 'InitPayload' not found").
            // - Unity Test Framework 默认会把未处理的 error log 视为测试失败.
            //
            // 设计取舍:
            // - importer/tests 并不依赖排序器,因此这里在无图形设备时跳过排序器初始化,
            //   避免 batch 测试被无关的渲染初始化噪声击穿.
            // ----------------------------------------------------------------
            if (!computeShader || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                GsplatSorter.Instance.InitSorter(null);
                return;
            }

            GsplatSorter.Instance.InitSorter(computeShader);
        }

        void CreateMeshInstance()
        {
            var meshPositions = new Vector3[4 * SplatInstanceSize];
            var meshIndices = new int[6 * SplatInstanceSize];
            for (uint i = 0; i < SplatInstanceSize; ++i)
            {
                unsafe
                {
                    meshPositions[i * 4] = new Vector3(-1, -1, *(float*)&i);
                    meshPositions[i * 4 + 1] = new Vector3(1, -1, *(float*)&i);
                    meshPositions[i * 4 + 2] = new Vector3(-1, 1, *(float*)&i);
                    meshPositions[i * 4 + 3] = new Vector3(1, 1, *(float*)&i);
                }

                int b = (int)i * 4;
                Array.Copy(new[] { 0 + b, 1 + b, 2 + b, 1 + b, 3 + b, 2 + b }, 0, meshIndices, i * 6, 6);
            }

            Mesh = new Mesh
            {
                name = "GsplatMeshInstance",
                vertices = meshPositions,
                triangles = meshIndices,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        void CreateMaterials()
        {
            if (Materials != null)
                foreach (var mat in Materials)
                    DestroyImmediate(mat);

            if (!Shader)
            {
                Materials = null;
                return;
            }

            Materials = new Material[4];
            for (var i = 0; i < 4; ++i)
            {
                Materials[i] = new Material(Shader) { hideFlags = HideFlags.HideAndDontSave };
                Materials[i].EnableKeyword($"SH_BANDS_{i}");
            }
        }

        void OnValidate()
        {
            if (Shader != m_prevShader)
            {
                CreateMaterials();
                m_prevShader = Shader;
            }

            if (ComputeShader != m_prevComputeShader)
            {
                InitSorterSafely(ComputeShader);
                m_prevComputeShader = ComputeShader;
            }

            if (SplatInstanceSize != m_prevSplatInstanceSize)
            {
                DestroyImmediate(Mesh);
                CreateMeshInstance();
                m_prevSplatInstanceSize = SplatInstanceSize;
            }
        }

        void OnEnable()
        {
            CreateMaterials();
            m_prevShader = Shader;
            InitSorterSafely(ComputeShader);
            m_prevComputeShader = ComputeShader;

            CreateMeshInstance();
            m_prevSplatInstanceSize = SplatInstanceSize;
        }
    }
}
