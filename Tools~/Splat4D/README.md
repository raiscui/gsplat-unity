# Tools~/Splat4D

本目录用于离线生成本仓库支持的 `.splat4d` 二进制文件.
它是 Unity 插件包的一部分,但目录名带 `~`,Unity 不会把它当作代码导入编译.

## `.splat4d` 的关键约定(和本仓库 importer 对齐)

- **无 header**,按 record 数组直接存.
- **little-endian**.
- **64 bytes/record**.
- **SH0 only**:
  - `r/g/b` 存的是 `baseRgb = f_dc * SH_C0 + 0.5` 的量化结果.
  - Unity 侧会用 `f_dc = (baseRgb - 0.5) / SH_C0` 还原 DC 系数.
- `a` 是 opacity(0..1) 的量化.
- quaternion bytes 的还原规则与 SplatVFX 一致:
  - `v = (byte - 128) / 128`,得到 `[-1,1]` 近似,再 normalize.

## 从 `hustvl/4DGaussians` 生成 `.splat4d`(推荐流程)

4DGaussians 的核心是时间条件 deformation network.
它不会直接给你 per-gaussian 的 `velocity/time/duration`.
因此你需要先导出多时间点的 3DGS PLY,再做 velocity 的差分/拟合.

### Step 1: 导出 per-timestamp PLY 序列

在 4DGaussians 仓库里运行他们提供的脚本 `export_perframe_3DGS.py`:

```bash
python export_perframe_3DGS.py \\
  --iteration 14000 \\
  --configs arguments/dnerf/lego.py \\
  --model_path output/dnerf/lego
```

它会把序列写到:

- `output/dnerf/lego/gaussian_pertimestamp/time_00000.ply`
- `output/dnerf/lego/gaussian_pertimestamp/time_00001.ply`
- ...

### Step 2: 把 PLY 序列转换为 `.splat4d`

在本仓库根目录运行:

```bash
python3 Tools~/Splat4D/ply_sequence_to_splat4d.py \\
  --input-dir /path/to/gaussian_pertimestamp \\
  --output /path/to/out.splat4d \\
  --mode average
```

可选的更高保真模式(会增加 splat 数):

```bash
python3 Tools~/Splat4D/ply_sequence_to_splat4d.py \\
  --input-dir /path/to/gaussian_pertimestamp \\
  --output /path/to/out_keyframed.splat4d \\
  --mode keyframe \\
  --frame-step 5
```

说明:
- keyframe 模式会把时间轴按 `frame_step` 分段.
  - 对每一段,用 `pos` 做差分计算该段的常量速度 `vel`.
  - 该段会写成一批 `.splat4d` records,并用 `time0/duration` 限定可见的时间窗.
- 当最后一段不足 `frame_step` 时,工具会自动补一个更短的尾段,保证覆盖到最后一帧(也就是 `t=1.0`).
- 输出 record 数大约为: `N * ceil((frames-1)/frame_step)`.

## 注意事项

- 该转换器假设序列中每个 PLY 的点数相同,并且 vertex 顺序一致.
  - 这对 4DGaussians 的 `export_perframe_3DGS.py` 是成立的(同一组 Gaussians,不同时间采样).
- `.splat4d` importer 不做坐标轴隐式翻转.
  - 如你需要从训练坐标系转到 Unity 坐标系,请在导出阶段做变换.
