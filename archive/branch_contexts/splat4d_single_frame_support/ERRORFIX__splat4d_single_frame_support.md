# ERRORFIX: 单帧普通 3DGS `.ply -> .splat4d` OpenSpec 立项

## 2026-03-12 00:05:00 +0800 问题: batchmode 验证时 `Kernel 'BuildAxes' not found`

### 现象
- `Unity -batchmode -nographics -executeMethod Gsplat.Editor.BatchVerifySplat4DImport.VerifyStaticSingleFrameFixture`
  - 功能上 exporter / importer / runtime 都成功
  - 但日志里会出现:
    - `Kernel 'BuildAxes' not found`

### 原因
- `Runtime/VFX/GsplatVfxBinder.cs`
  - `OnEnable()` 会进入 `EnsureKernelsReady()`
  - `EnsureKernelsReady()` 会调用 `ComputeShader.FindKernel(...)`
- 在 `GraphicsDeviceType.Null` 的 headless 环境里:
  - `FindKernel(...)` 即使被 `try/catch` 包住
  - Unity 仍会先把 kernel missing 记到日志

### 修复
- 新增 `HasNullGraphicsDevice()`
- 在 `EnsureKernelsReady()` 中:
  - `GraphicsDeviceType.Null` 时直接返回 `false`
  - 普通环境下先 `HasKernel("BuildAxes")` / `HasKernel("BuildDynamic")`
  - 仅在存在时才 `FindKernel(...)`

### 验证
- `python3 -m unittest Tools~/Splat4D/tests/test_single_frame_cli.py`
  - `OK`
- `dotnet build Gsplat.Editor.csproj`
  - `0 warnings`
  - `0 errors`
- `dotnet build Gsplat.Tests.Editor.csproj`
  - `0 errors`
- `Unity ... VerifyStaticSingleFrameFixture`
  - 仍输出 importer/runtime 成功日志
  - 不再出现 `Kernel 'BuildAxes' not found`
- `Unity ... -runTests ...`(去掉 `-quit`)
  - 生成 XML
  - `19/19 passed`

## 2026-03-12 18:52:00 +0800 问题: `s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d` 颜色偏彩,灰色金属变成彩色

### 现象
- 用户指定资产:
  - `Assets/Gsplat/splat/v2/s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d`
- 画面观感:
  - 原本偏灰的金属表面明显染成彩色

### 原因
- `Tools~/Splat4D/ply_sequence_to_splat4d.py::_read_ply_frame()`
  - 之前把 PLY `f_rest_*` 直接 `reshape(N, coeff, 3)`
  - 这相当于按 `RGBRGB...` 交织解释 SH 数据
- 但仓库现有 PLY importer 契约是:
  - `RRR... GGG... BBB...`
- 结果是:
  - v2 exporter 会把错位后的 SH 压缩到 `SHCT/SHLB`
  - 导致本应中性的材质被放大成彩色差异

### 修复
- 修改 `Tools~/Splat4D/ply_sequence_to_splat4d.py`
  - 把 `f_rest_*` 重排改成 channel-major 解释
- 新增 Python 回归测试:
  - `Tools~/Splat4D/tests/test_single_frame_cli.py`
  - 用 1 个顶点的 `RRR... GGG... BBB...` SH3 PLY 验证导出 centroid 的排列
- 重导出:
  - `Assets/Gsplat/splat/v2/s1_point_cloud_v2_sh3_full_k8192_f32_20260312.splat4d`

### 验证
- `python3 -m py_compile Tools~/Splat4D/ply_sequence_to_splat4d.py Tools~/Splat4D/tests/test_single_frame_cli.py`
  - 通过
- `python3 -m unittest Tools~/Splat4D/tests/test_single_frame_cli.py`
  - `Ran 7 tests`
  - `OK`
- 新资产离线反解:
  - `asset vs channel-major: mae=0.007287`
  - `asset vs interleaved: mae=0.029000`
- Unity batch verify:
  - `Tools/Gsplat/Batch Verify/Verify s1_point_cloud_v2_sh3`
  - 成功输出 imported 统计信息,无异常

### 补充修复
- batch verify 之前把 `deltaSegments != null` 直接当异常
- 但 runtime 的真实口径是 `null` 或 `Length==0` 都表示“无 delta-v1”
- 已同步修正 `Editor/BatchVerifySplat4DImport.cs`,并新增 Editor 测试锁定该契约
