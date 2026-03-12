# Tools~/Splat4D

本目录用于离线生成本仓库支持的 `.splat4d` 二进制文件.
它是 Unity 插件包的一部分,但目录名带 `~`,Unity 不会把它当作代码导入编译.

## `.splat4d` 的关键约定(和本仓库 importer 对齐)

当前 exporter 有两条输出路径:

- `v1`(默认)
  - **无 header**,按 record 数组直接存
  - **little-endian**
  - **64 bytes/record**
  - **SH0 only**
    - `r/g/b` 存的是 `baseRgb = f_dc * SH_C0 + 0.5` 的量化结果
    - Unity 侧会用 `f_dc = (baseRgb - 0.5) / SH_C0` 还原 DC 系数
- `v2`(当前先支持单帧 `.ply`)
  - 写 `SPL4DV02` header + section table
  - `RECS` 仍沿用同一份 64B record layout
  - 高阶 SH 按 band 写入:
    - `SHCT`(centroids)
    - `SHLB`(full labels)
  - 单帧默认使用 `labelsEncoding=full`
    - 这样不会人为制造 `frameCount=1` 的 delta-v1 动态 SH 语义

共同约定:

- `a` 是 opacity(0..1) 的量化
- quaternion bytes 的还原规则与 SplatVFX 一致:
  - `v = (byte - 128) / 128`,得到 `[-1,1]` 近似,再 normalize

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

单帧普通 3DGS `.ply` 的正式入口:

```bash
python3 Tools~/Splat4D/ply_sequence_to_splat4d.py \\
  --input-ply /path/to/frame.ply \\
  --output /path/to/out_single_frame.splat4d \\
  --mode average
```

单帧高质量 `.splat4d v2 + SH3`:

```bash
python3 Tools~/Splat4D/ply_sequence_to_splat4d.py \\
  --input-ply /path/to/frame_with_rest_sh3.ply \\
  --output /path/to/out_single_frame_v2_sh3.splat4d \\
  --mode average \\
  --splat4d-version 2 \\
  --sh-bands 3 \\
  --sh-codebook-count 8192 \\
  --sh-centroids-type f32 \\
  --self-check
```

PLY 序列目录:

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

输入规则:

- `--input-ply` 与 `--input-dir` 互斥,必须二选一.
- 如果你只有 1 个普通 3DGS `.ply`,优先使用 `--input-ply`,不要额外创建临时目录.
- 单帧输入在 `average` 模式下会继续复用标准 64B record 契约:
  - `vx/vy/vz = 0`
  - `time = 0`
  - `duration = 1`
- 单帧输入误用 `keyframe` 模式会明确失败:
  - `keyframe 模式至少需要 2 个 PLY`
- `--splat4d-version 2` 当前先只支持单帧输入:
  - 这样可以保证 `RECS`、`SHCT`、`SHLB` 都保持“每个 splat 1 条 base 记录”的对称语义
  - 多帧序列若要继续表达分段时间窗,当前仍应使用 `v1 + keyframe`

## 注意事项

- 该转换器假设序列中每个 PLY 的点数相同,并且 vertex 顺序一致.
  - 这对 4DGaussians 的 `export_perframe_3DGS.py` 是成立的(同一组 Gaussians,不同时间采样).
- `.splat4d` importer 不做坐标轴隐式翻转.
  - 如你需要从训练坐标系转到 Unity 坐标系,请在导出阶段做变换.
