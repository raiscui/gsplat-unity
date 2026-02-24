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

    // 相机模式:
    // - ActiveCameraOnly(默认): 只对“当前激活相机”排序+渲染,避免多相机重复 sort 的线性性能灾难.
    // - AllCameras: 保持兼容行为,所有相机都能看到 Gsplat(但多相机会带来更高开销).
    public enum GsplatCameraMode
    {
        ActiveCameraOnly = 0,
        AllCameras = 1
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
                    settings.ShDeltaComputeShader =
                        AssetDatabase.LoadAssetAtPath<ComputeShader>(GsplatUtils.k_PackagePath +
                                                                     "Runtime/Shaders/GsplatShDelta.compute");
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
        public ComputeShader ShDeltaComputeShader;

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
        // 相机模式(多相机性能开关)
        // --------------------------------------------------------------------
        [Tooltip(
            "决定 Gsplat 是否对所有相机排序+渲染.\n" +
            "- ActiveCameraOnly(默认): 性能优先.\n" +
            "  - Play 模式/Player: 只对 1 个 ActiveCamera 做排序与渲染,用于避免反射/探针/多视口等多相机导致的重复 sort 开销.\n" +
            "  - Editor 非 Play: 默认保证 SceneView 稳定可见并可排序,以避免 overlay/UIElements 等交互导致“显示/不显示”闪烁.\n" +
            "    - 当你最近一次交互的是 GameView(点击/聚焦)时,ActiveCamera 会保持为 Game/VR 相机,用于在 EditMode 预览 GameView.\n" +
            "      - 这样你去 Inspector 拖动 `TimeNormalized` 时,GameView 不会因为焦点转移而突然消失.\n" +
            "    - 若你希望完全不依赖 Editor 的焦点/鼠标窗口信号(例如复杂相机切换系统),请使用 `GsplatActiveCameraOverride`.\n" +
            "- AllCameras: 所有相机都能看到 Gsplat(兼容模式),但在 >1M splats 时多相机可能导致性能线性恶化.\n" +
            "建议: 如果你需要多个相机(例如 portals/反射/探针/辅助相机)都正确看到,请切到 AllCameras.\n" +
            "否则在 ActiveCameraOnly 下我们只保证“主要编辑视角(SceneView)与主相机(Play/Player)”的体验与性能.")]
        public GsplatCameraMode CameraMode = GsplatCameraMode.ActiveCameraOnly;

        // --------------------------------------------------------------------
        // Unity Editor Play Mode 性能开关
        // --------------------------------------------------------------------
        [Tooltip(
            "在 Unity Editor 的 Play 模式下,如果 Scene 视图也在渲染,会导致对 GameView 与 SceneView 各排序一次,性能可能显著下降.\n" +
            "开启后会在 Play 模式跳过 SceneView 相机的排序,优先保证 GameView 的流畅度.\n" +
            "注意: 这只影响 Editor 的 SceneView,不影响 Player build.")]
        public bool SkipSceneViewSortingInPlayMode = true;

        [Tooltip(
            "当 `SkipSceneViewSortingInPlayMode` 开启时,是否允许在 SceneView 窗口聚焦(你正在操作/查看 SceneView)时临时恢复排序.\n" +
            "适用场景: 你希望 Play 模式优先保证 GameView 性能,但在你交互 SceneView 时仍保持显示正确.\n" +
            "注意: 这只影响 Editor 的 SceneView,不影响 Player build.")]
        public bool AllowSceneViewSortingWhenFocusedInPlayMode = true;

        [Tooltip(
            "在 Unity Editor 的 Play 模式下,SceneView 额外渲染会带来额外 draw cost.\n" +
            "开启后会在 Play 模式仅对 Game/VR 相机提交 Gsplat draw call,从而让 SceneView 不再渲染 Gsplat.\n" +
            "注意: 这只影响 Editor 的 SceneView,不影响 Player build.")]
        public bool SkipSceneViewRenderingInPlayMode = true;

        // --------------------------------------------------------------------
        // 调试开关(默认关闭)
        // --------------------------------------------------------------------
        [Tooltip(
            "启用 Editor 诊断日志(用于排查 EditMode 下的闪烁/消失).\n" +
            "说明:\n" +
            "- 该开关会输出较多日志,并可能影响编辑器性能.\n" +
            "- 仅建议在需要定位问题时临时开启,问题定位后请关闭.")]
        public bool EnableEditorDiagnostics = false;

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
#if UNITY_EDITOR
            // 兼容旧 settings 资产: 新增字段可能为空,这里尽量自动补齐默认值,降低升级成本.
            if (!ShDeltaComputeShader)
            {
                ShDeltaComputeShader =
                    AssetDatabase.LoadAssetAtPath<ComputeShader>(GsplatUtils.k_PackagePath +
                                                                 "Runtime/Shaders/GsplatShDelta.compute");
            }
#endif
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
