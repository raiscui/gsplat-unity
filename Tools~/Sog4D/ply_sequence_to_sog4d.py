#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
把逐帧 keyframe 的 gaussian-splatting 风格 PLY 序列打包为单文件 `.sog4d`(ZIP bundle).

这个工具的定位:
- `.sog4d` 不是 `.splat4d` 的替代品,而是面向“真逐帧序列 + 全属性可插值”的新格式.
- 它参考 PlayCanvas SOG v2 的总体思路:
  - meta.json 描述 schema/streams/timeMapping/layout.
  - 属性流以“数据图”(lossless WebP)承载,便于导入期解码为 Texture2DArray.
  - SH(rest) 用 palette(centroids.bin) + labels.webp(或 delta-v1)承载,获得更好的体积/带宽收益.

本脚本默认遵循本仓库 OpenSpec:
- `openspec/changes/sog4d-sequence-format/specs/sog4d-sequence-encoding/spec.md`

注意:
- 这是离线工具,优先保证“可复现 + 明确失败原因”,而不是极限性能.
- 对超大数据集,请通过参数降低采样量/缩小 codebook,避免本机内存爆炸.
"""

from __future__ import annotations

import argparse
import io
import json
import math
import re
import struct
import sys
import zipfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Optional

import numpy as np
from PIL import Image, features

try:
    # 说明: 我们用 MiniBatchKMeans 做离线 VQ,它对大样本更稳.
    # 依赖缺失时会给出可行动的报错提示.
    from sklearn.cluster import MiniBatchKMeans
except Exception:  # pragma: no cover - 运行环境缺失时才会走这里
    MiniBatchKMeans = None  # type: ignore[assignment]

try:
    # scale 的最近邻查询用 cKDTree(3D),比全量 cdist 更省内存.
    from scipy.spatial import cKDTree
except Exception:  # pragma: no cover - 运行环境缺失时才会走这里
    cKDTree = None  # type: ignore[assignment]


# -----------------------------------------------------------------------------
# 常量与小工具
# -----------------------------------------------------------------------------

SH_C0: float = 0.28209479177387814

_TIME_FILE_RE = re.compile(r"(?:^|/|\\\\)(?:time_)?(\\d+)(?:\\D|$)")


def _die(msg: str) -> "NoReturn":
    print(f"[sog4d][error] {msg}", file=sys.stderr)
    raise SystemExit(2)


def _warn(msg: str) -> None:
    print(f"[sog4d][warn] {msg}", file=sys.stderr)


def _info(msg: str) -> None:
    print(f"[sog4d] {msg}", file=sys.stderr)


def _ensure_webp_available() -> None:
    # Pillow 是否具备 WebP 编解码能力取决于构建选项.
    # 没有 WebP 支持的话,就没法保证“字节级无损”的数据图输出.
    if not features.check("webp"):
        _die("Pillow 缺少 WebP 支持. 请安装带 WebP 的 Pillow 版本(例如通过系统 libwebp 构建).")


def _sort_key(p: Path) -> tuple[int, Any]:
    # 优先按 `time_00001.ply` 这种数字排序,否则退化为字典序.
    m = _TIME_FILE_RE.search(str(p))
    if m is not None:
        return (0, int(m.group(1)))
    return (1, str(p))


def _list_ply_files(input_dir: Path) -> list[Path]:
    files = [p for p in input_dir.iterdir() if p.is_file() and p.suffix.lower() == ".ply"]
    files.sort(key=_sort_key)
    return files


# -----------------------------------------------------------------------------
# PLY 读取(最小子集实现,保持与 Tools~/Splat4D/ply_sequence_to_splat4d.py 一致的假设)
# -----------------------------------------------------------------------------

_PLY_TYPE_TO_DTYPE: dict[str, str] = {
    "char": "i1",
    "int8": "i1",
    "uchar": "u1",
    "uint8": "u1",
    "short": "i2",
    "int16": "i2",
    "ushort": "u2",
    "uint16": "u2",
    "int": "i4",
    "int32": "i4",
    "uint": "u4",
    "uint32": "u4",
    "float": "f4",
    "float32": "f4",
    "double": "f8",
    "float64": "f8",
}


@dataclass(frozen=True)
class _PlyHeader:
    fmt: str
    endian: str  # '<' | '>' | '='(native,只用于 ascii)
    vertex_count: int
    vertex_props: list[tuple[str, np.dtype]]  # (name, dtype)


def _parse_ply_header(fp) -> _PlyHeader:
    # 只支持我们需要的最小子集:
    # - format: ascii | binary_little_endian | binary_big_endian
    # - element vertex + property 标量字段
    first = fp.readline()
    if not first:
        raise ValueError("PLY: empty file")
    if first.strip() != b"ply":
        raise ValueError(f"PLY: invalid magic: {first!r}")

    fmt: Optional[str] = None
    endian: str = "="
    vertex_count: Optional[int] = None
    vertex_props: list[tuple[str, np.dtype]] = []
    in_vertex = False

    while True:
        line_b = fp.readline()
        if not line_b:
            raise ValueError("PLY: unexpected EOF before end_header")
        line = line_b.decode("ascii", errors="replace").strip()

        if line == "end_header":
            break

        if not line or line.startswith("comment"):
            continue

        parts = line.split()
        if not parts:
            continue

        if parts[0] == "format":
            if len(parts) < 3:
                raise ValueError(f"PLY: invalid format line: {line}")
            fmt = parts[1]
            if fmt == "binary_little_endian":
                endian = "<"
            elif fmt == "binary_big_endian":
                endian = ">"
            elif fmt == "ascii":
                endian = "="
            else:
                raise ValueError(f"PLY: unsupported format: {fmt}")
            continue

        if parts[0] == "element":
            if len(parts) != 3:
                raise ValueError(f"PLY: invalid element line: {line}")
            elem_name = parts[1]
            elem_count = int(parts[2])
            in_vertex = elem_name == "vertex"
            if in_vertex:
                vertex_count = elem_count
                vertex_props = []
            continue

        if parts[0] == "property" and in_vertex:
            # 只支持标量 property.
            # list property(例如 face indices)我们不需要,遇到直接报错.
            if len(parts) < 3:
                raise ValueError(f"PLY: invalid property line: {line}")
            if parts[1] == "list":
                raise ValueError("PLY: list property is not supported by this tool")
            ply_type = parts[1]
            name = parts[2]
            if ply_type not in _PLY_TYPE_TO_DTYPE:
                raise ValueError(f"PLY: unsupported property type: {ply_type}")
            dt_code = _PLY_TYPE_TO_DTYPE[ply_type]
            # u1/i1 不需要 endian 前缀,其它需要.
            if dt_code in ("u1", "i1"):
                dt = np.dtype(dt_code)
            else:
                dt = np.dtype(endian + dt_code)
            vertex_props.append((name, dt))
            continue

    if fmt is None:
        raise ValueError("PLY: missing format header")
    if vertex_count is None:
        raise ValueError("PLY: missing element vertex header")
    if not vertex_props:
        raise ValueError("PLY: vertex has no properties")
    return _PlyHeader(fmt=fmt, endian=endian, vertex_count=vertex_count, vertex_props=vertex_props)


def _read_ply_vertices(path: Path) -> np.ndarray:
    with path.open("rb") as fp:
        header = _parse_ply_header(fp)
        dtype = np.dtype(header.vertex_props, align=False)

        if header.fmt in ("binary_little_endian", "binary_big_endian"):
            data = np.fromfile(fp, dtype=dtype, count=header.vertex_count)
        elif header.fmt == "ascii":
            # ascii 模式通常只用于调试或小文件.
            cols = len(header.vertex_props)
            data = np.empty(header.vertex_count, dtype=dtype)
            for i in range(header.vertex_count):
                line = fp.readline()
                if not line:
                    raise ValueError(f"PLY: unexpected EOF while reading vertices: {path}")
                items = line.decode("ascii", errors="replace").strip().split()
                if len(items) < cols:
                    raise ValueError(f"PLY: vertex line has too few columns: {path} line={i}")
                if len(items) != cols:
                    raise ValueError(f"PLY: vertex line has unexpected extra columns: {path} line={i}")
                for (name, dt), token in zip(header.vertex_props, items):
                    data[name][i] = np.array(token, dtype=dt)
        else:
            raise ValueError(f"PLY: unsupported format: {header.fmt}")
    return data


def _require_fields(v: np.ndarray, path: Path, fields: list[str]) -> None:
    names = set(v.dtype.names or [])
    missing = [f for f in fields if f not in names]
    if missing:
        _die(f"PLY 缺少必需字段: {missing}. file={path}")


def _find_rest_fields(v: np.ndarray) -> list[str]:
    # 按 f_rest_0,f_rest_1,... 排序输出.
    names = v.dtype.names or []
    rest: list[tuple[int, str]] = []
    for n in names:
        if not n.startswith("f_rest_"):
            continue
        try:
            idx = int(n[len("f_rest_") :])
        except ValueError:
            continue
        rest.append((idx, n))
    rest.sort(key=lambda x: x[0])
    return [n for _, n in rest]


@dataclass(frozen=True)
class PlyFrame:
    # 约定所有数组都是 float32(除非特别说明).
    positions: np.ndarray  # [N,3]
    f_dc: np.ndarray  # [N,3]
    opacity_raw: np.ndarray  # [N]
    scale_raw: np.ndarray  # [N,3]
    rot_raw: np.ndarray  # [N,4] (w,x,y,z)但可能未归一化
    rest: Optional[np.ndarray]  # [N,restCoeffCount,3] or None


def _read_ply_frame(path: Path, rest_field_names: list[str] | None) -> PlyFrame:
    v = _read_ply_vertices(path)

    _require_fields(
        v,
        path,
        [
            "x",
            "y",
            "z",
            "f_dc_0",
            "f_dc_1",
            "f_dc_2",
            "opacity",
            "scale_0",
            "scale_1",
            "scale_2",
            "rot_0",
            "rot_1",
            "rot_2",
            "rot_3",
        ],
    )

    positions = np.stack([v["x"], v["y"], v["z"]], axis=1).astype(np.float32, copy=False)
    f_dc = np.stack([v["f_dc_0"], v["f_dc_1"], v["f_dc_2"]], axis=1).astype(np.float32, copy=False)
    opacity_raw = v["opacity"].astype(np.float32, copy=False)
    scale_raw = np.stack([v["scale_0"], v["scale_1"], v["scale_2"]], axis=1).astype(np.float32, copy=False)
    rot_raw = np.stack([v["rot_0"], v["rot_1"], v["rot_2"], v["rot_3"]], axis=1).astype(np.float32, copy=False)

    rest: Optional[np.ndarray] = None
    if rest_field_names:
        # rest 字段必须齐全,否则视为不合法输入(否则 bands 会变得不确定).
        _require_fields(v, path, rest_field_names)
        flat = np.stack([v[n] for n in rest_field_names], axis=1).astype(np.float32, copy=False)  # [N, R]
        if flat.shape[1] % 3 != 0:
            _die(f"PLY f_rest_* 字段数量不是 3 的倍数: {flat.shape[1]}. file={path}")
        rest_coeff_count = flat.shape[1] // 3
        rest = flat.reshape(flat.shape[0], rest_coeff_count, 3)

    return PlyFrame(
        positions=positions,
        f_dc=f_dc,
        opacity_raw=opacity_raw,
        scale_raw=scale_raw,
        rot_raw=rot_raw,
        rest=rest,
    )


# -----------------------------------------------------------------------------
# 数值与量化
# -----------------------------------------------------------------------------


def _stable_sigmoid(x: np.ndarray) -> np.ndarray:
    # 数值稳定 sigmoid,输入输出都用 float32.
    x = x.astype(np.float32, copy=False)
    out = np.empty_like(x, dtype=np.float32)
    pos = x >= 0
    out[pos] = 1.0 / (1.0 + np.exp(-x[pos]))
    exp_x = np.exp(x[~pos])
    out[~pos] = exp_x / (1.0 + exp_x)
    return out


def _decode_opacity(opacity_raw: np.ndarray, mode: str) -> np.ndarray:
    # ---------------------------------------------------------------------
    # opacity 在不同导出器里可能是:
    # - logit(gaussian-splatting 常见): 需要 sigmoid.
    # - 已经是 [0,1] 线性值: 不需要 sigmoid.
    # ---------------------------------------------------------------------
    if mode == "sigmoid":
        return _stable_sigmoid(opacity_raw)
    if mode == "linear":
        return np.clip(opacity_raw.astype(np.float32, copy=False), 0.0, 1.0)

    # auto
    mn = float(np.nanmin(opacity_raw))
    mx = float(np.nanmax(opacity_raw))
    if 0.0 <= mn and mx <= 1.0:
        return np.clip(opacity_raw.astype(np.float32, copy=False), 0.0, 1.0)
    return _stable_sigmoid(opacity_raw)


def _decode_scale(scale_raw: np.ndarray, mode: str) -> np.ndarray:
    # ---------------------------------------------------------------------
    # scale 在 gaussian-splatting PLY 中常见的是 log(scale),需要 exp.
    # 但一些工具链可能已经写入线性 scale,因此提供参数开关.
    # ---------------------------------------------------------------------
    if mode == "linear":
        return scale_raw.astype(np.float32, copy=False)
    if mode == "exp":
        return np.exp(scale_raw.astype(np.float32, copy=False)).astype(np.float32, copy=False)

    # auto: 这里保守选择 exp(因为错误代价更小,线性 scale 做 exp 会明显爆炸,很容易被发现).
    return np.exp(scale_raw.astype(np.float32, copy=False)).astype(np.float32, copy=False)


def _normalize_quat_wxyz(q: np.ndarray) -> np.ndarray:
    # 把 quaternion 归一化,并做 w>=0 的半球规范化,减少帧间符号翻转.
    q = q.astype(np.float32, copy=False)
    norm = np.linalg.norm(q, axis=1, keepdims=True).astype(np.float32)
    norm1 = norm.squeeze(1)
    good = np.isfinite(norm1) & (norm1 >= 1e-8)

    out = np.empty_like(q, dtype=np.float32)
    out[good] = q[good] / norm[good]
    out[~good] = np.array([1.0, 0.0, 0.0, 0.0], dtype=np.float32)

    flip = out[:, 0] < 0
    out[flip] *= -1.0
    return out


def _quantize_quat_to_u8(q_norm: np.ndarray) -> np.ndarray:
    # [-1,1] float -> uint8,与 `.splat4d`/`.sog4d` spec 一致: v=(byte-128)/128.
    q = np.clip(q_norm, -1.0, 1.0)
    q8 = np.round(q * 128.0 + 128.0).astype(np.int32)
    q8 = np.clip(q8, 0, 255).astype(np.uint8)
    return q8


def _quantize_0_1_to_u8(x: np.ndarray) -> np.ndarray:
    x = np.clip(x.astype(np.float32, copy=False), 0.0, 1.0)
    return np.clip(np.round(x * 255.0), 0, 255).astype(np.uint8)


def _weighted_choice_no_replace(rng: np.random.Generator, n: int, size: int, weights: np.ndarray) -> np.ndarray:
    # ---------------------------------------------------------------------
    # numpy 的 `Generator.choice(..., replace=False, p=...)` 有一个硬约束:
    # - `p` 中非零项数量必须 >= `size`.
    #
    # 在本工具里,`weights` 可能来自:
    # - importance = opacity * volume
    #   其中 volume 是 scale 三维乘积,在 float32 下容易下溢为 0,
    #   从而导致 weights 出现大量 0,再触发 ValueError:
    #   "Fewer non-zero entries in p than size".
    #
    # 这里统一做“稳态采样”:
    # 1) 若权重总和无效/为 0,回退到均匀采样.
    # 2) 若非零权重不足,自动把 size clamp 到非零数量.
    # ---------------------------------------------------------------------
    size = int(min(size, n))
    if size <= 0:
        return np.empty((0,), dtype=np.int32)

    w = weights.astype(np.float64, copy=False)
    s = float(np.sum(w))
    if not (math.isfinite(s) and s > 0.0):
        return rng.choice(n, size=size, replace=False)

    p = w / s
    nonzero = int(np.count_nonzero(p))
    if nonzero <= 0:
        return rng.choice(n, size=size, replace=False)

    if size > nonzero:
        size = nonzero
    if size <= 0:
        return np.empty((0,), dtype=np.int32)

    return rng.choice(n, size=size, replace=False, p=p)


def _quantize_position_u16(positions: np.ndarray, range_min: np.ndarray, range_max: np.ndarray) -> np.ndarray:
    # position 量化规则(对齐 spec):
    # - per-frame rangeMin/rangeMax.
    # - q = clamp(round((x-min)/(max-min)*65535),0,65535)
    positions = positions.astype(np.float32, copy=False)
    range_min = range_min.astype(np.float32, copy=False)
    range_max = range_max.astype(np.float32, copy=False)

    span = (range_max - range_min).astype(np.float32, copy=False)
    safe_span = np.where(span > 1e-20, span, 1.0).astype(np.float32, copy=False)
    t = (positions - range_min) / safe_span
    t = np.clip(t, 0.0, 1.0)
    q = np.round(t * 65535.0).astype(np.int64)
    q = np.clip(q, 0, 65535).astype(np.uint16)

    # span==0 的维度,强制写 0,避免 NaN/Inf.
    zero_span = span <= 1e-20
    if np.any(zero_span):
        q[:, zero_span] = 0

    return q


def _pack_u16_to_rgba(u16: np.ndarray, splat_count: int, width: int, height: int) -> np.ndarray:
    # 把 u16 label/index 写入 RG(小端),并输出 RGBA8 图.
    # - R = low8
    # - G = high8
    # - B = 0
    # - A = 255
    flat = np.zeros((width * height, 4), dtype=np.uint8)
    used = u16[:splat_count].astype(np.uint16, copy=False)
    flat[:splat_count, 0] = (used & 0xFF).astype(np.uint8)
    flat[:splat_count, 1] = ((used >> 8) & 0xFF).astype(np.uint8)
    flat[:splat_count, 3] = 255
    return flat.reshape(height, width, 4)


def _save_webp_lossless_rgba(zf: zipfile.ZipFile, path: str, rgba: np.ndarray) -> None:
    # 关键: 这些是“数据图”,必须 lossless,否则 importer 读出来的 byte 会被破坏.
    # Pillow 未来会移除 `mode=` 参数,这里依赖数组形状(H,W,4)让它自动推导为 RGBA.
    img = Image.fromarray(rgba)
    if img.mode != "RGBA":
        img = img.convert("RGBA")
    with io.BytesIO() as bio:
        img.save(bio, format="WEBP", lossless=True, quality=100, method=6, exact=True)
        zf.writestr(path, bio.getvalue())


# -----------------------------------------------------------------------------
# Codebook / Palette 生成
# -----------------------------------------------------------------------------


def _weighted_quantile(values: np.ndarray, weights: np.ndarray, quantiles: np.ndarray) -> np.ndarray:
    # values/weights: 1D
    # quantiles: [0,1]
    if values.ndim != 1 or weights.ndim != 1:
        _die("weighted_quantile 需要 1D values/weights")
    if values.shape[0] != weights.shape[0]:
        _die("weighted_quantile values/weights 长度不一致")

    values = values.astype(np.float64, copy=False)
    weights = weights.astype(np.float64, copy=False)
    weights = np.maximum(weights, 0.0)
    s = float(weights.sum())
    if not math.isfinite(s) or s <= 0.0:
        # 全 0 权重时,退化为普通分位数.
        return np.quantile(values.astype(np.float64, copy=False), quantiles).astype(np.float32)

    order = np.argsort(values)
    v = values[order]
    w = weights[order]
    cw = np.cumsum(w)
    cw /= cw[-1]
    return np.interp(quantiles, cw, v).astype(np.float32)


def _build_sh0_codebook(
    samples: np.ndarray,
    weights: np.ndarray,
    method: str,
    seed: int,
) -> np.ndarray:
    # sh0Codebook 固定 256 项(按 spec).
    # - method=quantile: 快速且对长尾更稳.
    # - method=kmeans: 误差更小,但需要 sklearn.
    if samples.size == 0:
        _die("sh0Codebook: 没有样本,无法生成")

    if method == "quantile":
        qs = (np.arange(256, dtype=np.float32) + 0.5) / 256.0
        codebook = _weighted_quantile(samples, weights, qs)
        codebook.sort()
        return codebook.astype(np.float32, copy=False)

    if method != "kmeans":
        _die(f"未知 sh0Codebook 生成方法: {method}")

    if MiniBatchKMeans is None:
        _die("选择了 sh0Codebook=kmeans,但当前环境缺少 scikit-learn. 请改用 --sh0-codebook-method quantile.")

    # 1D k-means 的 sample_weight 在不同 sklearn 版本里行为可能变化.
    # 这里用“按权重采样”的方式近似,稳定且可复现.
    rng = np.random.default_rng(seed)
    probs = weights.astype(np.float64, copy=False)
    probs = np.maximum(probs, 0.0)
    s = float(probs.sum())
    if s <= 0:
        probs = None
    else:
        probs /= s

    n = int(min(samples.shape[0], 500_000))
    idx = rng.choice(samples.shape[0], size=n, replace=False if probs is None else True, p=probs)
    x = samples[idx].astype(np.float32).reshape(-1, 1)

    km = MiniBatchKMeans(n_clusters=256, random_state=seed, batch_size=4096, n_init=3)
    km.fit(x)
    codebook = km.cluster_centers_.reshape(-1).astype(np.float32, copy=False)
    codebook.sort()
    return codebook


def _fit_kmeans(
    name: str,
    x: np.ndarray,
    weights: np.ndarray,
    k: int,
    seed: int,
    max_samples: int,
) -> Any:
    # 通用 k-means 拟合:
    # - 用权重采样近似 sample_weight,避免版本差异.
    if MiniBatchKMeans is None:
        _die(f"{name}: 当前环境缺少 scikit-learn,无法执行 k-means. 请安装 sklearn.")

    if x.ndim != 2:
        _die(f"{name}: x 必须是 2D, got {x.shape}")

    if x.shape[0] == 0:
        _die(f"{name}: x 为空,无法拟合")

    eff_k = int(min(k, x.shape[0]))
    if eff_k < k:
        _warn(f"{name}: 样本数 {x.shape[0]} < k={k},自动降级为 k={eff_k}")
    k = eff_k

    rng = np.random.default_rng(seed)
    probs = weights.astype(np.float64, copy=False)
    probs = np.maximum(probs, 0.0)
    s = float(probs.sum())
    if not math.isfinite(s) or s <= 0.0:
        probs = None
    else:
        probs /= s

    n = int(min(x.shape[0], max_samples))
    idx = rng.choice(x.shape[0], size=n, replace=False if probs is None else True, p=probs)
    xs = x[idx].astype(np.float32, copy=False)

    _info(f"{name}: fitting MiniBatchKMeans(k={k}, sample={xs.shape[0]}, dim={xs.shape[1]}) ...")
    km = MiniBatchKMeans(n_clusters=k, random_state=seed, batch_size=4096, n_init=3)
    km.fit(xs)
    return km


def _nearest_u16_labels_bruteforce(x: np.ndarray, centroids: np.ndarray, batch: int) -> np.ndarray:
    # 说明: 这是兜底的最近邻实现.
    # - 对 scale(3D)我们优先用 KDTree,这个主要用于高维 SH rest.
    # - x: [N,D], centroids: [K,D]
    x = x.astype(np.float32, copy=False)
    c = centroids.astype(np.float32, copy=False)
    c_norm2 = np.sum(c * c, axis=1, keepdims=False)  # [K]

    out = np.empty((x.shape[0],), dtype=np.uint16)
    for start in range(0, x.shape[0], batch):
        end = min(start + batch, x.shape[0])
        xb = x[start:end]
        x_norm2 = np.sum(xb * xb, axis=1, keepdims=True)  # [B,1]
        # dist2 = ||x||^2 + ||c||^2 - 2 x·c
        dist2 = x_norm2 + c_norm2.reshape(1, -1) - 2.0 * (xb @ c.T)
        out[start:end] = np.argmin(dist2, axis=1).astype(np.uint16)
    return out


def _quantize_scalar_to_codebook_u8(values: np.ndarray, codebook_sorted: np.ndarray) -> np.ndarray:
    # values: 任意 shape, float32
    # codebook_sorted: [256], 升序
    v = values.astype(np.float32, copy=False)
    cb = codebook_sorted.astype(np.float32, copy=False)
    idx = np.searchsorted(cb, v, side="left").astype(np.int32)
    idx = np.clip(idx, 0, cb.shape[0])

    # 左右邻居比较,取最近.
    idx0 = np.clip(idx - 1, 0, cb.shape[0] - 1)
    idx1 = np.clip(idx, 0, cb.shape[0] - 1)
    d0 = np.abs(v - cb[idx0])
    d1 = np.abs(cb[idx1] - v)
    pick0 = d0 <= d1
    out = np.where(pick0, idx0, idx1).astype(np.uint8)
    return out


# -----------------------------------------------------------------------------
# `.sog4d` 打包与校验
# -----------------------------------------------------------------------------


def _auto_layout(splat_count: int, width: int | None, height: int | None) -> tuple[int, int]:
    if width is not None and width <= 0:
        _die(f"layout.width 必须 >0, got {width}")
    if height is not None and height <= 0:
        _die(f"layout.height 必须 >0, got {height}")

    if width is None and height is None:
        w = int(math.ceil(math.sqrt(splat_count)))
        h = int(math.ceil(splat_count / w))
        return w, h

    if width is not None and height is None:
        h = int(math.ceil(splat_count / width))
        return width, h

    if width is None and height is not None:
        w = int(math.ceil(splat_count / height))
        return w, height

    assert width is not None and height is not None
    if width * height < splat_count:
        _die(f"layout 尺寸不足: width*height={width*height} < splatCount={splat_count}")
    return width, height


def _parse_explicit_times(arg: str, frame_count: int) -> list[float]:
    # 支持两种写法:
    # 1) 逗号分隔: "0,0.1,0.5,1"
    # 2) 文件路径: 每行一个 float
    p = Path(arg)
    if p.exists():
        txt = p.read_text(encoding="utf-8").strip().splitlines()
        times = [float(x.strip()) for x in txt if x.strip()]
    else:
        times = [float(x.strip()) for x in arg.split(",") if x.strip()]

    if len(times) != frame_count:
        _die(f"explicit timeMapping 需要 {frame_count} 个时间点,但得到 {len(times)} 个")

    prev = -1e9
    for i, t in enumerate(times):
        if not (0.0 <= t <= 1.0) or not math.isfinite(t):
            _die(f"frameTimesNormalized[{i}] 非法: {t} (必须在 [0,1])")
        if t < prev:
            _die(f"frameTimesNormalized 必须单调非递减: frame {i-1}={prev} > frame {i}={t}")
        prev = t

    return times


def _zip_compression(name: str) -> int:
    if name == "stored":
        return zipfile.ZIP_STORED
    if name == "deflated":
        return zipfile.ZIP_DEFLATED
    _die(f"未知 zip compression: {name}")
    return zipfile.ZIP_STORED


def _build_segments(frame_count: int, seg_len: int) -> list[dict[str, Any]]:
    if seg_len <= 0:
        _die(f"delta segment length 必须 >0, got {seg_len}")
    segs: list[dict[str, Any]] = []
    start = 0
    while start < frame_count:
        fc = min(seg_len, frame_count - start)
        segs.append(
            {
                "startFrame": int(start),
                "frameCount": int(fc),
                "baseLabelsPath": f"frames/{start:05d}/shN_labels.webp",
                "deltaPath": f"sh/delta_{start:05d}.bin",
            }
        )
        start += fc
    return segs


def _write_delta_v1_header(
    bio: io.BytesIO,
    segment_start_frame: int,
    segment_frame_count: int,
    splat_count: int,
    shn_count: int,
) -> None:
    # 格式对齐 spec: magic="SOG4DLB1"(8 bytes) + u32 fields (little-endian)
    bio.write(b"SOG4DLB1")
    bio.write(struct.pack("<IIIII", 1, segment_start_frame, segment_frame_count, splat_count, shn_count))
    # 注意: Unity importer 当前严格按 spec 读取 5 个 u32,不要在这里加 padding 字段.


def _pack_cmd(args: argparse.Namespace) -> None:
    _ensure_webp_available()

    input_dir = Path(args.input_dir)
    output_path = Path(args.output)
    if not input_dir.exists():
        _die(f"input-dir 不存在: {input_dir}")

    ply_files = _list_ply_files(input_dir)
    if not ply_files:
        _die(f"input-dir 下未找到 .ply: {input_dir}")

    frame_count = len(ply_files)
    _info(f"frames: {frame_count}")

    # ---------------------------------------------------------------------
    # Pass 0: 先用第 0 帧确定 splatCount 与 rest/bands,并建立 rest 字段列表.
    # ---------------------------------------------------------------------
    v0 = _read_ply_vertices(ply_files[0])
    splat_count = int(v0.shape[0])
    if splat_count <= 0:
        _die("splatCount 必须 >0")

    rest_fields = _find_rest_fields(v0)
    rest_prop_count = len(rest_fields)
    if rest_prop_count % 3 != 0:
        _die(f"f_rest_* 字段数量必须是 3 的倍数,got {rest_prop_count}")
    rest_coeff_count = rest_prop_count // 3

    # 根据 restCoeffCount 反推 SH bands.
    # bands 满足: restCoeffCount = (bands+1)^2 - 1
    sh_bands_detected = 0
    if rest_coeff_count > 0:
        b = int(round(math.sqrt(rest_coeff_count + 1) - 1))
        if (b + 1) * (b + 1) - 1 != rest_coeff_count:
            _die(f"无法从 restCoeffCount={rest_coeff_count} 推导 SH bands(需要满足 (b+1)^2-1).")
        sh_bands_detected = b
    if sh_bands_detected < 0 or sh_bands_detected > 3:
        _die(f"SH bands 超出范围: {sh_bands_detected} (只支持 0..3)")

    sh_bands = sh_bands_detected if args.sh_bands is None else int(args.sh_bands)
    if sh_bands < 0 or sh_bands > 3:
        _die(f"--sh-bands 必须是 0..3, got {sh_bands}")
    if sh_bands == 0 and rest_coeff_count != 0:
        _warn("检测到 PLY 包含 f_rest_* 但你强制 --sh-bands 设为 0. 将忽略高阶 SH.")
        rest_fields = []
        rest_coeff_count = 0
    if sh_bands > 0 and rest_coeff_count == 0:
        _die("你要求 sh-bands>0,但 PLY 中未找到 f_rest_* 字段.")

    rest_coeff_count_expected = (sh_bands + 1) * (sh_bands + 1) - 1 if sh_bands > 0 else 0
    if rest_coeff_count != rest_coeff_count_expected:
        _die(
            f"PLY restCoeffCount={rest_coeff_count} 与 sh-bands={sh_bands} 不一致(期望 {rest_coeff_count_expected})."
        )

    _info(f"splats: {splat_count}, shBands: {sh_bands}")

    # ---------------------------------------------------------------------
    # Pass 1: 逐帧统计 range,并采样用于 codebook/palette 拟合.
    # ---------------------------------------------------------------------
    rng = np.random.default_rng(int(args.seed))

    # position per-frame range
    pos_range_min = np.empty((frame_count, 3), dtype=np.float32)
    pos_range_max = np.empty((frame_count, 3), dtype=np.float32)

    # 采样缓存(尽量控制内存)
    sh0_values: list[np.ndarray] = []
    sh0_weights: list[np.ndarray] = []
    scale_feat: list[np.ndarray] = []
    scale_w: list[np.ndarray] = []
    shn_feat: list[np.ndarray] = []
    shn_w: list[np.ndarray] = []

    sh0_target = int(args.sh0_sample_count)
    scale_target = int(args.scale_sample_count)
    shn_target = int(args.shn_sample_count)

    # 平均分配每帧采样预算,避免某帧独占样本.
    # sh0 的采样参数按“标量总量”计数,但每个 splat 会贡献 3 个标量(f_dc.r/g/b).
    sh0_per_frame = max(1, sh0_target // (frame_count * 3))
    scale_per_frame = max(1, scale_target // frame_count)
    shn_per_frame = max(1, shn_target // frame_count) if sh_bands > 0 else 0

    for fi, ply in enumerate(ply_files):
        frame = _read_ply_frame(ply, rest_fields if sh_bands > 0 else None)

        if frame.positions.shape[0] != splat_count:
            _die(f"frame splatCount 不一致: frame {fi} got {frame.positions.shape[0]} expected {splat_count}. file={ply}")

        pos_range_min[fi] = np.min(frame.positions, axis=0)
        pos_range_max[fi] = np.max(frame.positions, axis=0)

        opacity = _decode_opacity(frame.opacity_raw, args.opacity_mode)
        scale_lin = _decode_scale(frame.scale_raw, args.scale_mode)

        # importance 权重用于采样(拟合 codebook/palette).
        # - volume = scale.x * scale.y * scale.z.
        # - 这个乘积对小尺度非常敏感,在 float32 下容易下溢为 0,进而导致“权重全 0”的采样失败.
        # - 因此这里用 float64 计算,再在写入权重缓存时转回 float32.
        volume_f64 = np.prod(scale_lin.astype(np.float64, copy=False), axis=1)
        importance = np.maximum(opacity.astype(np.float64, copy=False) * volume_f64, 0.0)

        # ---- sh0 采样(1D)
        # 每个 splat 提供 3 个样本(f_dc.r/g/b),权重一致.
        if sh0_per_frame > 0:
            idx = _weighted_choice_no_replace(rng, splat_count, min(sh0_per_frame, splat_count), importance)

            vals = frame.f_dc[idx].reshape(-1).astype(np.float32, copy=False)  # 3x
            w = importance[idx].repeat(3).astype(np.float32, copy=False)
            sh0_values.append(vals)
            sh0_weights.append(w)

        # ---- scale 采样(3D,在 log 空间聚类)
        if scale_per_frame > 0:
            idx = _weighted_choice_no_replace(rng, splat_count, min(scale_per_frame, splat_count), opacity)

            sl = np.maximum(scale_lin[idx], 1e-8)
            feat = np.log(sl).astype(np.float32, copy=False)
            w = opacity[idx].astype(np.float32, copy=False)
            scale_feat.append(feat)
            scale_w.append(w)

        # ---- shN 采样(高维,importance 权重)
        if sh_bands > 0 and shn_per_frame > 0:
            assert frame.rest is not None
            idx = _weighted_choice_no_replace(rng, splat_count, min(shn_per_frame, splat_count), importance)

            # rest: [N,restCoeffCount,3] -> [N, D]
            d = frame.rest[idx].reshape(idx.shape[0], -1).astype(np.float32, copy=False)
            w = importance[idx].astype(np.float32, copy=False)
            shn_feat.append(d)
            shn_w.append(w)

        if (fi & 0x7) == 0:
            _info(f"pass1: {fi+1}/{frame_count} frames")

    sh0_samples = np.concatenate(sh0_values, axis=0) if sh0_values else np.empty((0,), dtype=np.float32)
    sh0_w_all = np.concatenate(sh0_weights, axis=0) if sh0_weights else np.empty((0,), dtype=np.float32)
    scale_samples = np.concatenate(scale_feat, axis=0) if scale_feat else np.empty((0, 3), dtype=np.float32)
    scale_w_all = np.concatenate(scale_w, axis=0) if scale_w else np.empty((0,), dtype=np.float32)

    if sh0_samples.size == 0:
        _die("sh0Codebook: 采样结果为空. 你可能给了空序列,或者权重全为 0.")

    _info(f"sh0 samples: {sh0_samples.shape[0]}")
    sh0_codebook = _build_sh0_codebook(sh0_samples, sh0_w_all, args.sh0_codebook_method, int(args.seed))

    # scale codebook
    if scale_samples.shape[0] == 0:
        _die("scale codebook: 采样结果为空")

    scale_codebook_size = int(args.scale_codebook_size)
    scale_km = _fit_kmeans("scaleCodebook(log)", scale_samples, scale_w_all, scale_codebook_size, int(args.seed), 200_000)
    scale_centers_log = scale_km.cluster_centers_.astype(np.float32, copy=False)
    scale_codebook = np.exp(scale_centers_log).astype(np.float32, copy=False)

    # shN centroids/palette
    shn_km = None
    shn_centroids = None
    shn_count = 0
    if sh_bands > 0:
        shn_samples = np.concatenate(shn_feat, axis=0) if shn_feat else np.empty((0, rest_coeff_count * 3), dtype=np.float32)
        shn_w_all = np.concatenate(shn_w, axis=0) if shn_w else np.empty((0,), dtype=np.float32)
        if shn_samples.shape[0] == 0:
            _die("shN centroids: 采样结果为空")

        shn_count_req = int(args.shn_count)
        shn_km = _fit_kmeans("shN_centroids", shn_samples, shn_w_all, shn_count_req, int(args.seed), 200_000)
        shn_centroids = shn_km.cluster_centers_.astype(np.float32, copy=False)  # [K,D]
        shn_count = int(shn_centroids.shape[0])

    # layout
    width, height = _auto_layout(splat_count, args.layout_width, args.layout_height)
    _info(f"layout: {width}x{height} (capacity={width*height})")

    # time mapping
    time_mapping: dict[str, Any]
    if args.time_mapping == "uniform":
        time_mapping = {"type": "uniform"}
    elif args.time_mapping == "explicit":
        if args.frame_times is None:
            _die("--time-mapping explicit 需要 --frame-times")
        time_mapping = {"type": "explicit", "frameTimesNormalized": _parse_explicit_times(args.frame_times, frame_count)}
    else:
        _die(f"未知 time-mapping: {args.time_mapping}")

    # sh labels encoding
    shn_labels_encoding = args.shn_labels_encoding
    if shn_labels_encoding not in ("full", "delta-v1"):
        _die(f"--shN-labels-encoding 必须是 full 或 delta-v1, got {shn_labels_encoding}")

    delta_seg_len = int(args.delta_segment_length)
    sh_delta_segments = _build_segments(frame_count, delta_seg_len) if (sh_bands > 0 and shn_labels_encoding == "delta-v1") else None

    # meta.json (直接生成 Unity JsonUtility 友好的结构: Vector3 用 {x,y,z})
    def v3_list(a: np.ndarray) -> list[dict[str, float]]:
        return [{"x": float(x), "y": float(y), "z": float(z)} for x, y, z in a.tolist()]

    meta: dict[str, Any] = {
        "format": "sog4d",
        "version": 1,
        "splatCount": int(splat_count),
        "frameCount": int(frame_count),
        "timeMapping": time_mapping,
        "layout": {"type": "row-major", "width": int(width), "height": int(height)},
        "streams": {
            "position": {
                "rangeMin": v3_list(pos_range_min),
                "rangeMax": v3_list(pos_range_max),
                "hiPath": "frames/{frame}/position_hi.webp",
                "loPath": "frames/{frame}/position_lo.webp",
            },
            "scale": {
                "codebook": v3_list(scale_codebook),
                "indicesPath": "frames/{frame}/scale_indices.webp",
            },
            "rotation": {"path": "frames/{frame}/rotation.webp"},
            "sh": {
                "bands": int(sh_bands),
                "sh0Path": "frames/{frame}/sh0.webp",
                "sh0Codebook": [float(x) for x in sh0_codebook.tolist()],
            },
        },
    }

    if sh_bands > 0:
        assert shn_centroids is not None
        meta_sh = meta["streams"]["sh"]
        meta_sh["shNCount"] = int(shn_count)
        meta_sh["shNCentroidsType"] = args.shn_centroids_type
        meta_sh["shNCentroidsPath"] = "shN_centroids.bin"
        meta_sh["shNLabelsEncoding"] = shn_labels_encoding

        if shn_labels_encoding == "full":
            meta_sh["shNLabelsPath"] = "frames/{frame}/shN_labels.webp"
        else:
            meta_sh["shNDeltaSegments"] = sh_delta_segments

    # 输出 ZIP
    compression = _zip_compression(args.zip_compression)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    _info(f"writing bundle: {output_path}")
    with zipfile.ZipFile(output_path, "w", compression=compression) as zf:
        # meta.json
        zf.writestr("meta.json", json.dumps(meta, ensure_ascii=False, indent=2))

        # shN centroids.bin
        if sh_bands > 0:
            assert shn_centroids is not None
            rest_count = (sh_bands + 1) * (sh_bands + 1) - 1
            centroids3 = shn_centroids.reshape(shn_count, rest_count, 3)
            if args.shn_centroids_type == "f16":
                data = centroids3.astype("<f2").tobytes(order="C")
            elif args.shn_centroids_type == "f32":
                data = centroids3.astype("<f4").tobytes(order="C")
            else:
                _die(f"未知 shN centroids type: {args.shn_centroids_type}")
            zf.writestr("shN_centroids.bin", data)

        # 为 scale 最近邻准备 KDTree(3D)
        if cKDTree is None:
            _die("当前环境缺少 scipy(cKDTree),无法进行 scale 量化. 请安装 scipy.")
        scale_tree = cKDTree(scale_centers_log.astype(np.float32, copy=False))

        # delta-v1 的状态机
        segs = sh_delta_segments or []
        seg_idx = 0
        seg_end = 0
        delta_bio: Optional[io.BytesIO] = None
        prev_labels: Optional[np.ndarray] = None  # u16 [splatCount]

        def start_segment(seg: dict[str, Any]) -> None:
            nonlocal delta_bio, prev_labels, seg_end
            delta_bio = io.BytesIO()
            _write_delta_v1_header(
                delta_bio,
                int(seg["startFrame"]),
                int(seg["frameCount"]),
                int(splat_count),
                int(shn_count),
            )
            prev_labels = None
            seg_end = int(seg["startFrame"]) + int(seg["frameCount"])

        def flush_segment(seg: dict[str, Any]) -> None:
            nonlocal delta_bio
            assert delta_bio is not None
            zf.writestr(seg["deltaPath"], delta_bio.getvalue())
            delta_bio.close()
            delta_bio = None

        if sh_bands > 0 and shn_labels_encoding == "delta-v1":
            start_segment(segs[0])

        # 逐帧编码并写入 WebP
        for fi, ply in enumerate(ply_files):
            frame = _read_ply_frame(ply, rest_fields if sh_bands > 0 else None)

            # -----------------------------
            # position_hi / position_lo
            # -----------------------------
            q = _quantize_position_u16(frame.positions, pos_range_min[fi], pos_range_max[fi])  # [N,3] u16
            hi = (q >> 8).astype(np.uint8, copy=False)
            lo = (q & 0xFF).astype(np.uint8, copy=False)

            flat_hi = np.zeros((width * height, 4), dtype=np.uint8)
            flat_lo = np.zeros((width * height, 4), dtype=np.uint8)
            flat_hi[:splat_count, 0:3] = hi
            flat_lo[:splat_count, 0:3] = lo
            flat_hi[:splat_count, 3] = 255
            flat_lo[:splat_count, 3] = 255
            rgba_hi = flat_hi.reshape(height, width, 4)
            rgba_lo = flat_lo.reshape(height, width, 4)

            frame_dir = f"frames/{fi:05d}/"
            _save_webp_lossless_rgba(zf, frame_dir + "position_hi.webp", rgba_hi)
            _save_webp_lossless_rgba(zf, frame_dir + "position_lo.webp", rgba_lo)

            # -----------------------------
            # scale_indices
            # -----------------------------
            opacity = _decode_opacity(frame.opacity_raw, args.opacity_mode)
            scale_lin = _decode_scale(frame.scale_raw, args.scale_mode)
            scale_log = np.log(np.maximum(scale_lin, 1e-8)).astype(np.float32, copy=False)

            # KDTree 查询最近的 codebook entry.
            _, idx_scale = scale_tree.query(scale_log, k=1)
            idx_scale = idx_scale.astype(np.uint16, copy=False)
            rgba_scale = _pack_u16_to_rgba(idx_scale, splat_count, width, height)
            _save_webp_lossless_rgba(zf, frame_dir + "scale_indices.webp", rgba_scale)

            # -----------------------------
            # rotation.webp (quat u8)
            # -----------------------------
            qn = _normalize_quat_wxyz(frame.rot_raw)
            q8 = _quantize_quat_to_u8(qn)
            flat_rot = np.zeros((width * height, 4), dtype=np.uint8)
            flat_rot[:splat_count, :] = q8
            rgba_rot = flat_rot.reshape(height, width, 4)
            _save_webp_lossless_rgba(zf, frame_dir + "rotation.webp", rgba_rot)

            # -----------------------------
            # sh0.webp (RGB=codebook index, A=opacity)
            # -----------------------------
            idx_sh0 = _quantize_scalar_to_codebook_u8(frame.f_dc, sh0_codebook)  # [N,3] u8
            a8 = _quantize_0_1_to_u8(opacity)  # [N] u8
            flat_sh0 = np.zeros((width * height, 4), dtype=np.uint8)
            flat_sh0[:splat_count, 0:3] = idx_sh0
            flat_sh0[:splat_count, 3] = a8
            rgba_sh0 = flat_sh0.reshape(height, width, 4)
            _save_webp_lossless_rgba(zf, frame_dir + "sh0.webp", rgba_sh0)

            # -----------------------------
            # shN labels
            # -----------------------------
            if sh_bands > 0:
                assert shn_km is not None
                assert frame.rest is not None
                rest_flat = frame.rest.reshape(splat_count, -1).astype(np.float32, copy=False)

                # sklearn 的 predict 会在 C 侧做距离计算,比 Python 循环更稳.
                labels = shn_km.predict(rest_flat).astype(np.uint16, copy=False)

                if shn_labels_encoding == "full":
                    rgba_labels = _pack_u16_to_rgba(labels, splat_count, width, height)
                    _save_webp_lossless_rgba(zf, frame_dir + "shN_labels.webp", rgba_labels)
                else:
                    # delta-v1
                    assert sh_delta_segments is not None
                    seg = segs[seg_idx]

                    # segment 边界: flush 上一个,开启下一个.
                    if fi >= seg_end:
                        flush_segment(seg)
                        seg_idx += 1
                        start_segment(segs[seg_idx])
                        seg = segs[seg_idx]

                    # segment 首帧: 写 base labels WebP,不写 update block.
                    if prev_labels is None:
                        rgba_labels = _pack_u16_to_rgba(labels, splat_count, width, height)
                        _save_webp_lossless_rgba(zf, seg["baseLabelsPath"], rgba_labels)
                        prev_labels = labels
                    else:
                        assert delta_bio is not None
                        diff = labels != prev_labels
                        splat_ids = np.nonzero(diff)[0].astype(np.uint32, copy=False)
                        delta_bio.write(struct.pack("<I", int(splat_ids.shape[0])))
                        for sid in splat_ids.tolist():
                            lab = int(labels[int(sid)])
                            delta_bio.write(struct.pack("<IHH", int(sid), int(lab), 0))
                        prev_labels = labels

            if (fi & 0x7) == 0:
                _info(f"pack: {fi+1}/{frame_count} frames")

        # delta 最后一个 segment flush
        if sh_bands > 0 and shn_labels_encoding == "delta-v1":
            assert seg_idx < len(segs)
            flush_segment(segs[seg_idx])

    _info("pack done.")

    # 可选: 打包后自检(避免把明显坏包交给 Unity importer).
    if args.self_check:
        _validate_cmd(argparse.Namespace(input=str(output_path), verbose=False))


def _read_zip_json(zf: zipfile.ZipFile, name: str) -> Any:
    try:
        data = zf.read(name)
    except KeyError:
        _die(f"bundle 缺少文件: {name}")
    try:
        return json.loads(data.decode("utf-8"))
    except Exception as e:
        _die(f"meta.json 解析失败: {e}")


def _read_zip_webp_rgba(zf: zipfile.ZipFile, name: str) -> np.ndarray:
    try:
        with zf.open(name) as fp:
            img = Image.open(fp)
            img = img.convert("RGBA")
            arr = np.array(img, dtype=np.uint8)
            return arr
    except KeyError:
        _die(f"bundle 缺少文件: {name}")
    except Exception as e:
        _die(f"WebP 解码失败: {name}: {e}")


def _validate_u16_map_rg(
    rgba: np.ndarray, splat_count: int, width: int, height: int, max_exclusive: int, field: str
) -> None:
    if rgba.shape[0] != height or rgba.shape[1] != width or rgba.shape[2] != 4:
        _die(f"{field}: 图像尺寸不匹配: got {rgba.shape}, expected ({height},{width},4)")

    flat = rgba.reshape(-1, 4)
    rg = flat[:splat_count, 0].astype(np.uint16) + (flat[:splat_count, 1].astype(np.uint16) << 8)
    bad = np.nonzero(rg >= np.uint16(max_exclusive))[0]
    if bad.size > 0:
        i = int(bad[0])
        v = int(rg[i])
        _die(f"{field}: 发现越界值: splatId={i}, value={v}, maxExclusive={max_exclusive}")


def _validate_cmd(args: argparse.Namespace) -> None:
    _ensure_webp_available()

    bundle = Path(args.input)
    if not bundle.exists():
        _die(f"input 不存在: {bundle}")

    with zipfile.ZipFile(bundle, "r") as zf:
        meta = _read_zip_json(zf, "meta.json")

        # -----------------------------------------------------------------
        # 顶层字段(最小校验)
        # -----------------------------------------------------------------
        if meta.get("format") != "sog4d":
            _die(f"meta.json.format 非法: {meta.get('format')}")
        if int(meta.get("version", 0)) != 1:
            _die(f"meta.json.version 非法: {meta.get('version')}")

        splat_count = int(meta.get("splatCount", 0))
        frame_count = int(meta.get("frameCount", 0))
        if splat_count <= 0 or frame_count <= 0:
            _die(f"splatCount/frameCount 非法: splatCount={splat_count}, frameCount={frame_count}")

        layout = meta.get("layout") or {}
        if layout.get("type") != "row-major":
            _die(f"layout.type 非法: {layout.get('type')}")
        width = int(layout.get("width", 0))
        height = int(layout.get("height", 0))
        if width <= 0 or height <= 0:
            _die(f"layout size 非法: {width}x{height}")
        if width * height < splat_count:
            _die(f"layout 容量不足: {width}x{height} < splatCount={splat_count}")

        streams = meta.get("streams") or {}

        def resolve(template: str, frame: int) -> str:
            if "{frame}" not in template:
                _die(f"模板缺少 {{frame}}: {template}")
            return template.replace("{frame}", f"{frame:05d}")

        # position
        pos = streams.get("position") or {}
        for f in range(frame_count):
            _read_zip_webp_rgba(zf, resolve(pos["hiPath"], f))
            _read_zip_webp_rgba(zf, resolve(pos["loPath"], f))

        # scale
        scale = streams.get("scale") or {}
        scale_codebook = scale.get("codebook") or []
        if not scale_codebook:
            _die("streams.scale.codebook 为空")
        for f in range(frame_count):
            rgba = _read_zip_webp_rgba(zf, resolve(scale["indicesPath"], f))
            _validate_u16_map_rg(rgba, splat_count, width, height, len(scale_codebook), f"scale_indices frame={f}")

        # rotation
        rot = streams.get("rotation") or {}
        for f in range(frame_count):
            _read_zip_webp_rgba(zf, resolve(rot["path"], f))

        # sh
        sh = streams.get("sh") or {}
        bands = int(sh.get("bands", 0))
        sh0_codebook = sh.get("sh0Codebook") or []
        if len(sh0_codebook) != 256:
            _die(f"streams.sh.sh0Codebook 长度必须为 256, got {len(sh0_codebook)}")
        for f in range(frame_count):
            _read_zip_webp_rgba(zf, resolve(sh["sh0Path"], f))

        if bands == 0:
            _info("validate ok (bands=0).")
            return

        shn_count = int(sh.get("shNCount", 0))
        if not (1 <= shn_count <= 65535):
            _die(f"shNCount 非法: {shn_count}")

        # centroids size
        centroids_bytes = zf.read(sh["shNCentroidsPath"])
        rest_coeff_count = (bands + 1) * (bands + 1) - 1
        scalar_bytes = 2 if sh["shNCentroidsType"] == "f16" else 4
        expected = shn_count * rest_coeff_count * 3 * scalar_bytes
        if len(centroids_bytes) != expected:
            _die(f"shN_centroids.bin 大小不匹配: expected {expected} got {len(centroids_bytes)}")

        enc = sh.get("shNLabelsEncoding") or "full"
        if enc == "full":
            for f in range(frame_count):
                rgba = _read_zip_webp_rgba(zf, resolve(sh["shNLabelsPath"], f))
                _validate_u16_map_rg(rgba, splat_count, width, height, shn_count, f"shN_labels frame={f}")
            _info("validate ok (full labels).")
            return

        if enc != "delta-v1":
            _die(f"未知 shNLabelsEncoding: {enc}")

        segs = sh.get("shNDeltaSegments") or []
        if not segs:
            _die("delta-v1: shNDeltaSegments 为空")

        # segments 连续性
        start = 0
        for i, seg in enumerate(segs):
            if int(seg["startFrame"]) != start:
                _die(f"delta-v1: segment[{i}].startFrame 不连续: expected {start} got {seg['startFrame']}")
            fc = int(seg["frameCount"])
            if fc <= 0:
                _die(f"delta-v1: segment[{i}].frameCount 非法: {fc}")
            start += fc
        if start != frame_count:
            _die(f"delta-v1: segments 覆盖帧数不等于 frameCount: sum={start}, frameCount={frame_count}")

        # delta 逐段验证(包含 header 与 block 的越界/递增)
        for i, seg in enumerate(segs):
            base_rgba = _read_zip_webp_rgba(zf, seg["baseLabelsPath"])
            _validate_u16_map_rg(base_rgba, splat_count, width, height, shn_count, f"delta-v1 baseLabels seg={i}")

            # 解析 base labels 作为 prev
            flat = base_rgba.reshape(-1, 4)
            prev = (flat[:splat_count, 0].astype(np.uint16) + (flat[:splat_count, 1].astype(np.uint16) << 8)).copy()

            delta = zf.read(seg["deltaPath"])
            bio = io.BytesIO(delta)
            magic = bio.read(8)
            if magic != b"SOG4DLB1":
                _die(f"delta-v1: magic 不匹配: seg={i} got={magic!r}")
            hdr = bio.read(20)
            if len(hdr) != 20:
                _die(f"delta-v1: header 截断: seg={i}")
            ver, seg_start, seg_fc, sc, shc = struct.unpack("<IIIII", hdr)
            if ver != 1:
                _die(f"delta-v1: version 非法: seg={i} got={ver}")
            if int(seg_start) != int(seg["startFrame"]):
                _die(f"delta-v1: segmentStartFrame mismatch: seg={i} meta={seg['startFrame']} file={seg_start}")
            if int(seg_fc) != int(seg["frameCount"]):
                _die(f"delta-v1: segmentFrameCount mismatch: seg={i} meta={seg['frameCount']} file={seg_fc}")
            if int(sc) != splat_count:
                _die(f"delta-v1: splatCount mismatch: seg={i} meta={splat_count} file={sc}")
            if int(shc) != shn_count:
                _die(f"delta-v1: shNCount mismatch: seg={i} meta={shn_count} file={shc}")

            for local in range(1, int(seg_fc)):
                raw = bio.read(4)
                if len(raw) != 4:
                    _die(f"delta-v1 truncated: seg={i} localFrame={local} missing updateCount")
                (uc,) = struct.unpack("<I", raw)
                if uc > splat_count:
                    _die(f"delta-v1 invalid updateCount: seg={i} localFrame={local} updateCount={uc}")

                prev_sid = -1
                for u in range(int(uc)):
                    rec = bio.read(8)
                    if len(rec) != 8:
                        _die(f"delta-v1 truncated: seg={i} localFrame={local} update={u}")
                    sid, lab, res = struct.unpack("<IHH", rec)
                    if res != 0:
                        _die(f"delta-v1 reserved!=0: seg={i} localFrame={local} update={u}")
                    if sid >= splat_count:
                        _die(f"delta-v1 splatId 越界: seg={i} localFrame={local} sid={sid}")
                    if lab >= shn_count:
                        _die(f"delta-v1 label 越界: seg={i} localFrame={local} lab={lab}")
                    if sid <= prev_sid:
                        _die(f"delta-v1 splatId 非严格递增: seg={i} localFrame={local} sid={sid} prev={prev_sid}")
                    prev_sid = int(sid)
                    prev[int(sid)] = np.uint16(lab)

        _info("validate ok (delta-v1).")


def _build_arg_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="ply_sequence_to_sog4d.py")
    sub = p.add_subparsers(dest="cmd", required=True)

    # pack
    pack = sub.add_parser("pack", help="从 time_*.ply 序列打包生成 .sog4d")
    pack.add_argument("--input-dir", required=True, help="包含 time_*.ply 的目录")
    pack.add_argument("--output", required=True, help="输出 .sog4d 路径")
    pack.add_argument("--time-mapping", default="uniform", choices=["uniform", "explicit"], help="uniform 或 explicit")
    pack.add_argument("--frame-times", default=None, help="explicit 模式下的 frameTimesNormalized(逗号或文件路径)")
    pack.add_argument("--layout-width", type=int, default=None, help="layout.width(默认自动)")
    pack.add_argument("--layout-height", type=int, default=None, help="layout.height(默认自动)")
    pack.add_argument("--seed", type=int, default=0, help="随机种子(影响采样与 k-means)")

    # opacity/scale 解码
    pack.add_argument("--opacity-mode", default="auto", choices=["auto", "linear", "sigmoid"], help="opacity 解码方式")
    pack.add_argument("--scale-mode", default="exp", choices=["auto", "linear", "exp"], help="scale 解码方式")

    # scale codebook
    pack.add_argument("--scale-codebook-size", type=int, default=4096, help="scale codebook 大小")
    pack.add_argument("--scale-sample-count", type=int, default=200_000, help="scale 拟合采样量(总量)")

    # sh0 codebook
    pack.add_argument("--sh0-codebook-method", default="quantile", choices=["quantile", "kmeans"], help="sh0Codebook 生成方法")
    pack.add_argument("--sh0-sample-count", type=int, default=1_000_000, help="sh0 拟合采样量(标量总量)")

    # SHN palette + labels
    pack.add_argument("--sh-bands", type=int, default=None, help="强制 SH bands(默认按 PLY f_rest_* 自动推导)")
    pack.add_argument("--shN-count", dest="shn_count", type=int, default=8192, help="shN palette entry 数(默认 8192)")
    pack.add_argument("--shN-centroids-type", dest="shn_centroids_type", default="f16", choices=["f16", "f32"], help="centroids.bin 标量类型")
    pack.add_argument("--shN-sample-count", dest="shn_sample_count", type=int, default=200_000, help="shN centroids 拟合采样量(总量)")
    pack.add_argument(
        "--shN-labels-encoding",
        dest="shn_labels_encoding",
        default="delta-v1",
        choices=["full", "delta-v1"],
        help="labels 输出模式",
    )
    pack.add_argument("--delta-segment-length", type=int, default=50, help="delta-v1 segment 帧数(默认 50)")

    # zip
    pack.add_argument("--zip-compression", default="stored", choices=["stored", "deflated"], help="ZIP 压缩方式")
    pack.add_argument("--self-check", action="store_true", help="打包后自动执行 validate")

    # validate
    val = sub.add_parser("validate", help="自检 .sog4d bundle(越界/缺文件/尺寸等)")
    val.add_argument("--input", required=True, help="输入 .sog4d")
    val.add_argument("--verbose", action="store_true", help="输出更多信息(预留)")

    return p


def main(argv: list[str]) -> None:
    args = _build_arg_parser().parse_args(argv)
    if args.cmd == "pack":
        _pack_cmd(args)
    elif args.cmd == "validate":
        _validate_cmd(args)
    else:
        _die(f"未知命令: {args.cmd}")


if __name__ == "__main__":
    main(sys.argv[1:])
