# SOG4D Tools

这是一组离线工具.
它用于把逐帧 keyframe 的 3DGS/4DGS PLY 序列打包为单文件 `.sog4d`(ZIP bundle).

核心目标:
- 单文件分发友好.
- 导入期可解码为 `Texture2DArray`(数据图) + `GsplatSequenceAsset`.
- 运行时可通过 `GsplatSequenceRenderer` 做逐帧插值播放.

## 0. 环境与依赖(先确认能跑)

### 0.1 `pack` 和 `validate` 的依赖差异

这个工具分两类子命令:
- `pack`: 会做 codebook/palette 拟合 + WebP 写入.
- `validate`: 只做 bundle 完整性检查 + WebP 解码 + 越界检查.

`pack` 依赖:
- `numpy`
- `Pillow`(必须带 WebP 支持)
- `scikit-learn`(MiniBatchKMeans,用于 codebook/palette 拟合)
- `scipy`(cKDTree,用于 scale 最近邻量化)

`validate` 依赖:
- `numpy`
- `Pillow`(必须带 WebP 支持)

常见依赖缺失报错:
- `Pillow 缺少 WebP 支持`
- `当前环境缺少 scikit-learn`
- `当前环境缺少 scipy(cKDTree)`

### 0.2 快速检查 Pillow WebP 支持

```bash
python3 -c 'from PIL import features; print("Pillow WebP:", features.check("webp"))'
```

如果输出 `False`,说明你当前安装的 Pillow 不支持 WebP.
这时即使 `pip install pillow` 成功,也可能仍然不行.
你需要安装带 WebP 的 Pillow(取决于系统 libwebp 与构建选项).

### 0.3 可选: 建一个干净的虚拟环境(推荐)

```bash
python3 -m venv .venv
source .venv/bin/activate
python3 -m pip install -U pip
python3 -m pip install numpy pillow scipy scikit-learn
```

## 1. 输入数据前提(避免打包到一半才炸)

### 1.1 PLY 文件命名与排序

脚本会扫描 `--input-dir` 下的所有 `.ply`,并按文件名里的数字排序.
推荐命名:
- `time_00000.ply`
- `time_00001.ply`
- ...

### 1.2 每帧必须一致的约束

- 每一帧的 `element vertex N` 必须一致.
- 每一帧的 vertex property 列表必须一致.

### 1.3 必需字段(最小集合)

SH0-only(也就是 `--sh-bands 0`)需要:
- `x,y,z`
- `scale_0,scale_1,scale_2`
- `rot_0,rot_1,rot_2,rot_3`(wxyz)
- `opacity`
- `f_dc_0,f_dc_1,f_dc_2`

如果你要带 SH1-3,还需要:
- `f_rest_0,f_rest_1,...`
- `f_rest_*` 的总数量必须是 3 的倍数.

## 2. 生成 `.sog4d`(命令菜谱,直接复制粘贴)

我把常见配置整理成一组“菜谱”.
你可以先用最小可用跑通链路.
再逐步打开 SH 和 delta-v1.

### 2.1 最小可用(强制 SH0-only,先跑通)

适用场景:
- 先确认 Unity 导入与播放链路没问题.
- 暂时不需要高阶 SH.

```bash
python3 Tools~/Sog4D/ply_sequence_to_sog4d.py pack \
  --input-dir /path/to/time_*.ply \
  --output out_sh0.sog4d \
  --time-mapping uniform \
  --sh-bands 0 \
  --self-check
```

### 2.2 带 SH3(默认推荐,delta-v1)

适用场景:
- 保留 SH3 细节.
- 帧数较多,希望 labels 体积更小.

```bash
python3 Tools~/Sog4D/ply_sequence_to_sog4d.py pack \
  --input-dir /path/to/time_*.ply \
  --output out_sh3_delta.sog4d \
  --time-mapping uniform \
  --sh-bands 3 \
  --shN-count 8192 \
  --shN-centroids-type f16 \
  --shN-labels-encoding delta-v1 \
  --delta-segment-length 50 \
  --self-check
```

要点:
- `--shN-centroids-type f16` 通常能显著减小 `shN_centroids.bin` 的体积.
- `--delta-segment-length` 越大,segment 数越少,文件数更少.
  但单个 delta 文件会更大.

### 2.3 带 SH3(全量 labels,不使用 delta)

适用场景:
- 你想要最直观的 bundle 结构.
- 你宁愿体积更大,也要减少 delta 相关复杂度.

```bash
python3 Tools~/Sog4D/ply_sequence_to_sog4d.py pack \
  --input-dir /path/to/time_*.ply \
  --output out_sh3_full.sog4d \
  --time-mapping uniform \
  --sh-bands 3 \
  --shN-count 8192 \
  --shN-centroids-type f16 \
  --shN-labels-encoding full \
  --self-check
```

### 2.4 体积优先(更小的 shN palette)

适用场景:
- 更小 bundle.
- 可以接受 SH rest 近似误差变大.

```bash
python3 Tools~/Sog4D/ply_sequence_to_sog4d.py pack \
  --input-dir /path/to/time_*.ply \
  --output out_sh3_small.sog4d \
  --time-mapping uniform \
  --sh-bands 3 \
  --shN-count 4096 \
  --shN-centroids-type f16 \
  --shN-labels-encoding delta-v1 \
  --delta-segment-length 50 \
  --self-check
```

### 2.5 质量优先(更大采样量,更稳定的 codebook/palette)

适用场景:
- 你在做最终交付.
- 你希望减小量化误差.
- 你可以接受打包更慢,并且更吃内存.

```bash
python3 Tools~/Sog4D/ply_sequence_to_sog4d.py pack \
  --input-dir /path/to/time_*.ply \
  --output out_quality.sog4d \
  --time-mapping uniform \
  --opacity-mode auto \
  --scale-mode exp \
  --sh-bands 3 \
  --scale-codebook-size 8192 \
  --scale-sample-count 400000 \
  --sh0-codebook-method kmeans \
  --sh0-sample-count 4000000 \
  --shN-count 8192 \
  --shN-sample-count 400000 \
  --shN-centroids-type f32 \
  --shN-labels-encoding delta-v1 \
  --delta-segment-length 50 \
  --seed 0 \
  --zip-compression stored \
  --self-check
```

要点:
- `--seed` 固定后,采样与 k-means 结果可复现.
- `--opacity-mode`:
  - 如果你的 PLY 的 `opacity` 是 logit(例如 gaussian-splatting/4DGaussians 常见输出),建议显式用 `--opacity-mode sigmoid`.
  - 如果你确认 `opacity` 已经是 [0,1],用 `--opacity-mode linear`(或保留 `auto` 让脚本推断).
- `--scale-mode exp` 是本工具默认值.
  - 仅当你确认 `scale_0/1/2` 已经是线性 scale 时,才需要显式改成 `--scale-mode linear`.
- 采样量不是越大越好.
  你可以先从 200k/400k 级别开始调.
  不要一上来就跑到几百万,很容易内存爆掉.
- `--shN-centroids-type f32` 更精确,但体积更大.
  如果你更在意体积,可以改回 `f16`.
- `--zip-compression`:
  - 默认 `stored`(不压缩,最快).
  - 如果你想压缩 `meta.json`/`shN_centroids.bin`/`sh/delta_*.bin`,可以改为 `deflated`.
  - 但注意: `.sog4d` 的体积大头通常在 WebP 数据图里,ZIP 再压缩收益往往不大.

### 2.6 显式时间轴(time-mapping=explicit)

适用场景:
- 你的帧不是等间隔采样.
- 你希望播放时的时间分布更贴近真实时间轴.

写法A: 逗号分隔:

```bash
python3 Tools~/Sog4D/ply_sequence_to_sog4d.py pack \
  --input-dir /path/to/time_*.ply \
  --output out_explicit.sog4d \
  --time-mapping explicit \
  --frame-times "0,0.1,0.35,0.6,1" \
  --sh-bands 0 \
  --self-check
```

写法B: 文件路径(每行一个 float):

```bash
python3 Tools~/Sog4D/ply_sequence_to_sog4d.py pack \
  --input-dir /path/to/time_*.ply \
  --output out_explicit.sog4d \
  --time-mapping explicit \
  --frame-times /path/to/frame_times.txt \
  --sh-bands 0 \
  --self-check
```

### 2.7 线性 opacity / 线性 scale(当你的 PLY 不是 logit/log-scale)

适用场景:
- 你的 `opacity` 已经是 [0,1].
- 你的 `scale_0/1/2` 已经是线性 scale,不是 log(scale).

```bash
python3 Tools~/Sog4D/ply_sequence_to_sog4d.py pack \
  --input-dir /path/to/time_*.ply \
  --output out_linear.sog4d \
  --time-mapping uniform \
  --opacity-mode linear \
  --scale-mode linear \
  --sh-bands 0 \
  --self-check
```

### 2.8 手动指定 layout(调试或对齐外部工具)

适用场景:
- 你想要固定宽高.
- 你希望 bundle 的纹理尺寸满足某些约束.

```bash
python3 Tools~/Sog4D/ply_sequence_to_sog4d.py pack \
  --input-dir /path/to/time_*.ply \
  --output out_layout.sog4d \
  --time-mapping uniform \
  --layout-width 256 \
  --layout-height 256 \
  --sh-bands 0 \
  --self-check
```

注意:
- `width*height` 必须 >= `splatCount`.

### 2.9 ZIP 压缩方式(通常不需要改)

适用场景:
- 你极端在意 `meta.json` 与 delta bin 的体积.

```bash
python3 Tools~/Sog4D/ply_sequence_to_sog4d.py pack \
  --input-dir /path/to/time_*.ply \
  --output out_deflated.sog4d \
  --time-mapping uniform \
  --sh-bands 0 \
  --zip-compression deflated \
  --self-check
```

说明:
- `.sog4d` 的大头体积在 WebP 数据图里.
- WebP 本身是压缩格式.
  ZIP 再压缩通常收益很小.

### 2.10 查看 `.sog4d` bundle 内容(它本质是 ZIP)

```bash
unzip -l out.sog4d | head
```

建议:
- 输出文件名用 `.sog4d` 扩展名.
  这样 Unity 的 `ScriptedImporter` 能直接识别并导入.

## 3. 校验 `.sog4d`(只校验,不打包)

```bash
python3 Tools~/Sog4D/ply_sequence_to_sog4d.py validate --input out.sog4d
```

它会做:
- meta.json 字段与 streams 完整性校验.
- layout 尺寸校验(width*height >= splatCount).
- WebP 文件存在性校验.
- labels/indices 越界检查(需要解码 WebP,会比较耗时).

如果你在 pack 时加了 `--self-check`,它会在输出后自动跑一遍 validate.

## 4. 参数速查(你到底该改哪个)

- `--sh-bands`:
  - 建议先用 `0` 跑通链路.
  - 需要 SH3 时用 `3`.
  - 不填时会按 PLY 的 `f_rest_*` 自动推导.
- `--shN-count`:
  - 越大,SH rest 的 palette 更细,质量更好.
  - 越小,体积更小,误差更大.
- `--shN-centroids-type`:
  - `f16` 更省体积,通常够用.
  - `f32` 更精确,但更大.
- `--shN-labels-encoding`:
  - `delta-v1` 更省体积(更适合长序列).
  - `full` 更直观,但每帧都有一张 labels WebP.
- `--delta-segment-length`:
  - 只在 `delta-v1` 下生效.
  - 越大,segment 越少,文件数更少.
- `--opacity-mode`:
  - `auto` 会在 [0,1] 与 logit 之间自动判断.
  - 你确认是线性值时,用 `linear`.
  - 你确认是 logit 时,用 `sigmoid`.
- `--scale-mode`:
  - 本工具默认 `exp`,适配大多数 gaussian-splatting PLY 的 log(scale).
  - 你确认 PLY 写的是线性 scale 时,用 `linear`.
- `--seed`:
  - 固定后可复现采样与 k-means.
- `--self-check`:
  - 强烈建议一直开.
  - 它会帮你把“坏 bundle”在导入 Unity 前就挡住.

## 5. 在 Unity 中显示与播放

1. 把生成的 `out.sog4d` 放到 Unity 工程的 `Assets/`(或任意子目录)下.
2. 等待导入完成.
   - importer 会把 WebP 数据图解码成 `Texture2DArray` 子资产.
   - importer 会生成一个可直接拖拽使用的 prefab(main object),并挂上 `GsplatSequenceRenderer`.
3. 把该 prefab 拖进场景.
4. 在 Inspector 里设置播放参数:
   - `AutoPlay`: 自动播放.
   - `Speed`: 归一化时间/秒.
   - `Loop`: 循环.
   - `InterpolationMode`: `Nearest` 或 `Linear`.
5. 如果 `DecodeComputeShader` 没有自动回填,请手动指定为:
   - `Packages/wu.yize.gsplat/Runtime/Shaders/GsplatSequenceDecode.compute`

常见运行时报错:
- `HLSLcc: Metal shading language does not support buffer size query from shader`:
  - 这表示 decode compute shader 在 Metal 下编译失败.
  - 原因通常是 shader 内部做了 `StructuredBuffer.GetDimensions` 这类 buffer size query.
  - 解决: 更新到已移除 `GetDimensions` 并由 C# 传入 buffer count 的版本.
- `GsplatSequenceDecode.compute: Kernel at index (...) is invalid`:
  - 这通常意味着 decode compute kernel 在当前 Graphics API 下编译失败或不被支持.
  - 请确认 `DecodeComputeShader` 指向上面那个 compute 文件.
  - 如果你在 `-batchmode -nographics`(无图形设备)下运行,序列播放会被设计为禁用.
    这种模式主要用于跑 importer/tests,不用于播放渲染.
