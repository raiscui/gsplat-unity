// 这是 Samples~ 下的验证脚本,用于复现/验证 4DGS 的 bounds 扩展是否能避免相机剔除错误.
// - 重点验证: splat 在时间窗内移动到静态 bounds 外时,仍不会被相机剔除.
// - 注意: 这是用于验证的最小例子,不是生产代码.

using UnityEngine;

namespace Gsplat.Samples
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class Gsplat4DCullingRepro : MonoBehaviour
    {
        // 运动幅度(对象空间),用来把 splat 明显推到静态 bounds 外.
        [Min(0.0f)] public float MoveDistance = 10.0f;

        // 高斯尺度(对象空间). 越大越容易看清.
        [Min(0.0001f)] public float SplatScale = 0.15f;

        // 自动播放速度(归一化时间/秒).
        public float Speed = 0.25f;

        GsplatRenderer m_renderer;
        GsplatAsset m_runtimeAsset;

        void OnEnable()
        {
            // 确保同一个 GameObject 上有 renderer,避免用户手动配一堆东西.
            m_renderer = GetComponent<GsplatRenderer>();
            if (!m_renderer)
                m_renderer = gameObject.AddComponent<GsplatRenderer>();

            // 这个验证用例应该走 Gsplat 主后端,因为我们要观察的是剔除是否正确.
            m_renderer.EnableGsplatBackend = true;
            m_renderer.AsyncUpload = false;
            m_renderer.GammaToLinear = false;
            m_renderer.SHDegree = 0;

            // 创建一个仅含 2 个 splat 的 4D 资产(运行时内存对象,不落盘).
            m_runtimeAsset = ScriptableObject.CreateInstance<GsplatAsset>();
            m_runtimeAsset.name = "Gsplat4DCullingRepro_RuntimeAsset";
            m_runtimeAsset.SplatCount = 2;
            m_runtimeAsset.SHBands = 0;

            // 两个 splat 初始都在原点附近,静态 bounds 非常小.
            m_runtimeAsset.Positions = new[]
            {
                new Vector3(0, 0, 0),
                new Vector3(0, 0, 0.5f)
            };

            // 颜色使用 f_dc 表达. 这里置 0 会显示为 0.5 灰(因为 shader 内会做 *SH_C0 + 0.5).
            m_runtimeAsset.Colors = new[]
            {
                new Vector4(0, 0, 0, 1),
                new Vector4(0, 0, 0, 1)
            };

            // 简单的各向同性尺度 + 单位四元数.
            m_runtimeAsset.Scales = new[]
            {
                Vector3.one * SplatScale,
                Vector3.one * SplatScale
            };
            m_runtimeAsset.Rotations = new[]
            {
                new Vector4(1, 0, 0, 0),
                new Vector4(1, 0, 0, 0)
            };

            // 4D 字段: 在 t=0..1 内可见,并沿 +X 方向移动.
            m_runtimeAsset.Velocities = new[]
            {
                new Vector3(MoveDistance, 0, 0),
                new Vector3(MoveDistance, 0, 0)
            };
            m_runtimeAsset.Times = new[] { 0.0f, 0.0f };
            m_runtimeAsset.Durations = new[] { 1.0f, 1.0f };
            m_runtimeAsset.MaxSpeed = MoveDistance;
            m_runtimeAsset.MaxDuration = 1.0f;

            // 静态 bounds 故意做得很小: 只包住初始点.
            var b = new Bounds(m_runtimeAsset.Positions[0], Vector3.zero);
            b.Encapsulate(m_runtimeAsset.Positions[1]);
            m_runtimeAsset.Bounds = b;

            // 绑定到 renderer 并开启自动播放,便于目视观察.
            m_renderer.GsplatAsset = m_runtimeAsset;
            m_renderer.AutoPlay = true;
            m_renderer.Loop = true;
            m_renderer.Speed = Speed;
        }

        void OnDisable()
        {
            // 清理运行时创建的 ScriptableObject,避免编辑器里反复 Enable/Disable 后泄漏.
            if (m_runtimeAsset)
            {
                DestroyImmediate(m_runtimeAsset);
                m_runtimeAsset = null;
            }
        }

        void OnValidate()
        {
            // Inspector 改参数时,尽量同步到运行时 asset 上,避免用户困惑.
            if (!m_runtimeAsset)
                return;

            MoveDistance = Mathf.Max(0.0f, MoveDistance);
            SplatScale = Mathf.Max(0.0001f, SplatScale);

            m_runtimeAsset.Scales[0] = Vector3.one * SplatScale;
            m_runtimeAsset.Scales[1] = Vector3.one * SplatScale;

            m_runtimeAsset.Velocities[0] = new Vector3(MoveDistance, 0, 0);
            m_runtimeAsset.Velocities[1] = new Vector3(MoveDistance, 0, 0);
            m_runtimeAsset.MaxSpeed = MoveDistance;
        }
    }
}

