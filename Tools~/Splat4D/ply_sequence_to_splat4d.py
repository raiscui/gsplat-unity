#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
把 gaussian-splatting 风格的 PLY 转换为 `.splat4d`.

当前脚本支持两条导出路径:
- v1(默认): 沿用历史 raw record 流.
  - 支持 `average` / `keyframe`
  - 不带 header
  - SH0-only
- v2(本轮新增): 面向“单帧普通 3DGS PLY -> `.splat4d v2 + SH`”
  - 仅支持单帧输入
  - 写 `SPL4DV02` header + section table
  - SH rest 按 band 写 `SHCT/SHLB`
  - 单帧默认使用 `labelsEncoding=full`,避免伪造动态 SH 语义
"""

from __future__ import annotations

import argparse
import math
import re
import struct
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Optional

import numpy as np

try:
    from sklearn.cluster import MiniBatchKMeans
except Exception:
    MiniBatchKMeans = None  # type: ignore[assignment]


SH_C0: float = 0.28209479177387814

_TIME_FILE_RE = re.compile(r"(?:^|/|\\)(?:time_)?(\d+)(?:\D|$)")

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

_V2_HEADER_STRUCT = struct.Struct("<8sIIIIIIIIQQQ")
_V2_TABLE_HEADER_STRUCT = struct.Struct("<4sIII")
_V2_SECTION_ENTRY_STRUCT = struct.Struct("<IIIIQQ")
_V2_META_PREFIX_STRUCT = struct.Struct("<IfII")
_V2_BAND_INFO_STRUCT = struct.Struct("<IIII")


def _print_error(msg: str) -> None:
    print(f"[splat4d][error] {msg}", file=sys.stderr)


def _print_info(msg: str) -> None:
    print(f"[splat4d] {msg}", file=sys.stderr)


def _print_warn(msg: str) -> None:
    print(f"[splat4d][warn] {msg}", file=sys.stderr)


@dataclass(frozen=True)
class _PlyHeader:
    fmt: str
    endian: str
    vertex_count: int
    vertex_props: list[tuple[str, np.dtype]]


@dataclass(frozen=True)
class PlyFrame:
    positions: np.ndarray
    f_dc: np.ndarray
    opacity_raw: np.ndarray
    scale_raw: np.ndarray
    rot_raw: np.ndarray
    rest: Optional[np.ndarray]


@dataclass(frozen=True)
class _V2BandInfo:
    codebook_count: int
    centroids_type: int
    labels_encoding: int


@dataclass(frozen=True)
class _V2Section:
    kind: str
    band: int
    start_frame: int
    frame_count: int
    payload: bytes


@dataclass(frozen=True)
class _ParsedV2Header:
    magic: str
    version: int
    header_size_bytes: int
    section_count: int
    record_size_bytes: int
    splat_count: int
    sh_bands: int
    time_model: int
    frame_count: int
    section_table_offset: int


@dataclass(frozen=True)
class _ParsedV2Section:
    kind: str
    band: int
    start_frame: int
    frame_count: int
    offset: int
    length: int


# -----------------------------------------------------------------------------
# PLY 读取
# -----------------------------------------------------------------------------


def _parse_ply_header(fp) -> _PlyHeader:
    first = fp.readline()
    if not first:
        raise ValueError("PLY: empty file")
    if first.strip() != b"ply":
        raise ValueError(f"PLY: invalid magic: {first!r}")

    fmt = None  # type: Optional[str]
    endian: str = "="
    vertex_count = None  # type: Optional[int]
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
            if len(parts) < 3:
                raise ValueError(f"PLY: invalid property line: {line}")
            if parts[1] == "list":
                raise ValueError("PLY: list property is not supported by this tool")
            ply_type = parts[1]
            name = parts[2]
            if ply_type not in _PLY_TYPE_TO_DTYPE:
                raise ValueError(f"PLY: unsupported property type: {ply_type}")
            dt_code = _PLY_TYPE_TO_DTYPE[ply_type]
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
    missing = [field for field in fields if field not in names]
    if missing:
        raise ValueError(f"{path}: 缺少字段 {', '.join(missing)}")


def _find_rest_fields(v: np.ndarray) -> list[str]:
    names = v.dtype.names or []
    rest: list[tuple[int, str]] = []
    for name in names:
        if not name.startswith("f_rest_"):
            continue
        try:
            idx = int(name[len("f_rest_"):])
        except ValueError:
            continue
        rest.append((idx, name))
    rest.sort(key=lambda item: item[0])
    return [name for _, name in rest]


def _detect_sh_bands_from_rest_fields(rest_fields: list[str]) -> int:
    rest_prop_count = len(rest_fields)
    if rest_prop_count == 0:
        return 0
    if rest_prop_count % 3 != 0:
        raise ValueError(f"PLY `f_rest_*` 字段数量必须是 3 的倍数, got {rest_prop_count}")

    rest_coeff_count = rest_prop_count // 3
    sh_bands = int(round(math.sqrt(rest_coeff_count + 1) - 1))
    if (sh_bands + 1) * (sh_bands + 1) - 1 != rest_coeff_count:
        raise ValueError(
            f"无法从 restCoeffCount={rest_coeff_count} 推导 SH bands. 需要满足 restCoeffCount = (bands+1)^2 - 1"
        )
    if sh_bands < 0 or sh_bands > 3:
        raise ValueError(f"检测到的 SH bands 超出当前支持范围: {sh_bands}")
    return sh_bands


def _sort_key(path: Path) -> tuple:
    m = _TIME_FILE_RE.search(str(path))
    if m is not None:
        return (0, int(m.group(1)))
    return (1, str(path))


def _list_ply_files(input_dir: Path) -> list[Path]:
    files = [path for path in input_dir.iterdir() if path.is_file() and path.suffix.lower() == ".ply"]
    files.sort(key=_sort_key)
    return files


def _resolve_input_ply_files(args: argparse.Namespace) -> list[Path]:
    input_ply = getattr(args, "input_ply", None)
    if input_ply is not None:
        input_ply = Path(input_ply)
        if not input_ply.exists():
            raise ValueError(f"input-ply 不存在: {input_ply}")
        if not input_ply.is_file():
            raise ValueError(f"input-ply 不是文件: {input_ply}")
        if input_ply.suffix.lower() != ".ply":
            raise ValueError(f"input-ply 必须是 .ply 文件: {input_ply}")
        return [input_ply]

    input_dir = getattr(args, "input_dir", None)
    if input_dir is not None:
        input_dir = Path(input_dir)
        if not input_dir.exists():
            raise ValueError(f"input-dir 不存在: {input_dir}")
        if not input_dir.is_dir():
            raise ValueError(f"input-dir 不是目录: {input_dir}")
        ply_files = _list_ply_files(input_dir)
        if not ply_files:
            raise ValueError(f"input-dir 下未找到 .ply: {input_dir}")
        return ply_files

    raise ValueError("需要二选一提供 --input-ply 或 --input-dir")


def _read_ply_frame(path: Path, rest_field_names: list[str] | None = None) -> PlyFrame:
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

    rest = None
    if rest_field_names:
        _require_fields(v, path, rest_field_names)
        flat = np.stack([v[name] for name in rest_field_names], axis=1).astype(np.float32, copy=False)
        if flat.shape[1] % 3 != 0:
            raise ValueError(f"{path}: `f_rest_*` 字段数量不是 3 的倍数: {flat.shape[1]}")
        rest_coeff_count = flat.shape[1] // 3
        # `f_rest_*` 的字段顺序要和仓库现有 PLY importer 保持一致:
        # 先写完全部 R coeff,再写 G coeff,最后写 B coeff.
        # 这里如果直接按 `RGBRGB...` reshape,会把灰色材质错误染成彩色.
        rest = np.stack(
            [
                flat[:, 0:rest_coeff_count],
                flat[:, rest_coeff_count : rest_coeff_count * 2],
                flat[:, rest_coeff_count * 2 : rest_coeff_count * 3],
            ],
            axis=2,
        )

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
    x = x.astype(np.float32, copy=False)
    out = np.empty_like(x, dtype=np.float32)
    pos = x >= 0
    out[pos] = 1.0 / (1.0 + np.exp(-x[pos]))
    exp_x = np.exp(x[~pos])
    out[~pos] = exp_x / (1.0 + exp_x)
    return out


def _decode_opacity(opacity_raw: np.ndarray, mode: str) -> np.ndarray:
    if mode == "logit":
        return _stable_sigmoid(opacity_raw)
    if mode == "linear":
        return np.clip(opacity_raw.astype(np.float32, copy=False), 0.0, 1.0)
    raise ValueError(f"unknown opacity_mode: {mode}")


def _decode_scale(scale_raw: np.ndarray, mode: str) -> np.ndarray:
    if mode == "log":
        return np.exp(scale_raw.astype(np.float32, copy=False)).astype(np.float32, copy=False)
    if mode == "linear":
        return scale_raw.astype(np.float32, copy=False)
    raise ValueError(f"unknown scale_mode: {mode}")


def _normalize_quat_wxyz(q: np.ndarray) -> np.ndarray:
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
    q = np.clip(q_norm, -1.0, 1.0)
    q8 = np.round(q * 128.0 + 128.0).astype(np.int32)
    q8 = np.clip(q8, 0, 255).astype(np.uint8)
    return q8


def _quantize_0_1_to_u8(x: np.ndarray) -> np.ndarray:
    x = np.clip(x.astype(np.float32, copy=False), 0.0, 1.0)
    return np.clip(np.round(x * 255.0), 0, 255).astype(np.uint8)


def _weighted_choice_no_replace(rng: np.random.Generator, n: int, size: int, weights: np.ndarray) -> np.ndarray:
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


# -----------------------------------------------------------------------------
# v1 record 构造
# -----------------------------------------------------------------------------


def _build_records(
    *,
    positions: np.ndarray,
    velocities: np.ndarray,
    f_dc: np.ndarray,
    opacity_raw: np.ndarray,
    scale_raw: np.ndarray,
    rot_raw: np.ndarray,
    time0: float,
    duration: float,
    scale_mode: str,
    opacity_mode: str,
) -> np.ndarray:
    if positions.shape != velocities.shape:
        raise ValueError(f"positions/velocities shape mismatch: {positions.shape} vs {velocities.shape}")
    if positions.shape[1] != 3:
        raise ValueError(f"positions must be [N,3], got {positions.shape}")
    n = positions.shape[0]

    base_rgb = 0.5 + SH_C0 * f_dc.astype(np.float32, copy=False)
    rgb8 = _quantize_0_1_to_u8(base_rgb)
    alpha = _decode_opacity(opacity_raw, opacity_mode)
    a8 = _quantize_0_1_to_u8(alpha)
    scale_lin = _decode_scale(scale_raw, scale_mode)
    q_norm = _normalize_quat_wxyz(rot_raw)
    q8 = _quantize_quat_to_u8(q_norm)

    dtype = np.dtype(
        [
            ("px", "<f4"),
            ("py", "<f4"),
            ("pz", "<f4"),
            ("sx", "<f4"),
            ("sy", "<f4"),
            ("sz", "<f4"),
            ("r", "u1"),
            ("g", "u1"),
            ("b", "u1"),
            ("a", "u1"),
            ("rw", "u1"),
            ("rx", "u1"),
            ("ry", "u1"),
            ("rz", "u1"),
            ("vx", "<f4"),
            ("vy", "<f4"),
            ("vz", "<f4"),
            ("time", "<f4"),
            ("duration", "<f4"),
            ("pad0", "<f4"),
            ("pad1", "<f4"),
            ("pad2", "<f4"),
        ],
        align=False,
    )
    if dtype.itemsize != 64:
        raise RuntimeError(f"internal error: record dtype itemsize={dtype.itemsize}, expected 64")

    rec = np.empty(n, dtype=dtype)
    rec["px"] = positions[:, 0]
    rec["py"] = positions[:, 1]
    rec["pz"] = positions[:, 2]
    rec["sx"] = scale_lin[:, 0]
    rec["sy"] = scale_lin[:, 1]
    rec["sz"] = scale_lin[:, 2]
    rec["r"] = rgb8[:, 0]
    rec["g"] = rgb8[:, 1]
    rec["b"] = rgb8[:, 2]
    rec["a"] = a8
    rec["rw"] = q8[:, 0]
    rec["rx"] = q8[:, 1]
    rec["ry"] = q8[:, 2]
    rec["rz"] = q8[:, 3]
    rec["vx"] = velocities[:, 0]
    rec["vy"] = velocities[:, 1]
    rec["vz"] = velocities[:, 2]
    rec["time"] = np.float32(time0)
    rec["duration"] = np.float32(duration)
    rec["pad0"] = np.float32(0.0)
    rec["pad1"] = np.float32(0.0)
    rec["pad2"] = np.float32(0.0)
    return rec


def _write_records(fp, rec: np.ndarray) -> None:
    fp.write(rec.tobytes(order="C"))


def _run_average_mode(
    *,
    ply_files: list[Path],
    output_path: Path,
    scale_mode: str,
    opacity_mode: str,
) -> None:
    first = _read_ply_frame(ply_files[0])
    if len(ply_files) >= 2:
        last = _read_ply_frame(ply_files[-1])
        if last.positions.shape != first.positions.shape:
            raise ValueError(
                "首帧与末帧点数不一致,无法按 index 对齐计算 velocity.\n"
                f"first={first.positions.shape[0]}, last={last.positions.shape[0]}"
            )
        velocities = (last.positions - first.positions).astype(np.float32)
    else:
        velocities = np.zeros_like(first.positions, dtype=np.float32)

    rec = _build_records(
        positions=first.positions,
        velocities=velocities,
        f_dc=first.f_dc,
        opacity_raw=first.opacity_raw,
        scale_raw=first.scale_raw,
        rot_raw=first.rot_raw,
        time0=0.0,
        duration=1.0,
        scale_mode=scale_mode,
        opacity_mode=opacity_mode,
    )

    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("wb") as fp:
        _write_records(fp, rec)

    print(f"[OK] wrote {len(rec):,} splats -> {output_path}")


def _run_keyframe_mode(
    *,
    ply_files: list[Path],
    output_path: Path,
    frame_step: int,
    scale_mode: str,
    opacity_mode: str,
) -> None:
    if frame_step <= 0:
        raise ValueError("--frame-step must be > 0")
    if len(ply_files) < 2:
        raise ValueError("keyframe 模式至少需要 2 个 PLY")

    n_frames = len(ply_files)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    total_records = 0
    segments = 0

    with output_path.open("wb") as out:
        for i in range(0, n_frames - 1, frame_step):
            j = min(i + frame_step, n_frames - 1)
            if j <= i:
                break

            a = _read_ply_frame(ply_files[i])
            b = _read_ply_frame(ply_files[j])

            if b.positions.shape != a.positions.shape:
                raise ValueError(
                    "相邻 keyframe 点数不一致,无法按 index 对齐计算 velocity.\n"
                    f"frame{i}={a.positions.shape[0]}, frame{j}={b.positions.shape[0]}"
                )

            dt = (j - i) / float(n_frames - 1)
            if dt <= 0:
                raise ValueError("dt must be > 0")

            velocities = (b.positions - a.positions).astype(np.float32) / np.float32(dt)
            t0 = i / float(n_frames - 1)

            rec = _build_records(
                positions=a.positions,
                velocities=velocities,
                f_dc=a.f_dc,
                opacity_raw=a.opacity_raw,
                scale_raw=a.scale_raw,
                rot_raw=a.rot_raw,
                time0=t0,
                duration=dt,
                scale_mode=scale_mode,
                opacity_mode=opacity_mode,
            )
            _write_records(out, rec)
            total_records += len(rec)
            segments += 1

            if segments == 1 or (segments % 10) == 0:
                _print_info(
                    f"[keyframe] segment {i:>5} -> {j:>5} "
                    f"t0={t0:.4f} dt={dt:.4f} (+{len(rec):,} records)"
                )

    print(f"[OK] wrote {total_records:,} splats -> {output_path}")
    print(f"     frames={n_frames}, frame_step={frame_step}, segments={segments}")


# -----------------------------------------------------------------------------
# `.splat4d v2` 单帧导出
# -----------------------------------------------------------------------------


def _band_coeff_range(band: int) -> tuple[int, int]:
    if band == 1:
        return 0, 3
    if band == 2:
        return 3, 5
    if band == 3:
        return 8, 7
    raise ValueError(f"invalid SH band: {band}")


def _fourcc_to_u32(code: str) -> int:
    if len(code) != 4:
        raise ValueError(f"fourcc must be 4 chars, got {code!r}")
    return struct.unpack("<I", code.encode("ascii"))[0]


def _u32_to_fourcc(value: int) -> str:
    return struct.pack("<I", value).decode("ascii")


def _build_sh_importance_weights(frame: PlyFrame, scale_mode: str, opacity_mode: str) -> np.ndarray:
    opacity = _decode_opacity(frame.opacity_raw, opacity_mode).astype(np.float64, copy=False)
    scale_lin = _decode_scale(frame.scale_raw, scale_mode).astype(np.float64, copy=False)
    volume = np.prod(np.maximum(scale_lin, 1e-12), axis=1)
    return np.maximum(opacity * volume, 0.0)


def _fit_weighted_minibatch_kmeans(
    *,
    name: str,
    features: np.ndarray,
    weights: np.ndarray,
    codebook_count: int,
    seed: int,
    max_fit_samples: int,
) -> tuple[np.ndarray, np.ndarray]:
    if MiniBatchKMeans is None:
        raise ValueError(f"{name}: 当前环境缺少 scikit-learn,无法生成 SH codebook. 请安装 sklearn")
    if features.ndim != 2:
        raise ValueError(f"{name}: features 必须是 2D, got {features.shape}")
    if features.shape[0] == 0:
        raise ValueError(f"{name}: 没有可用样本")
    if codebook_count <= 0:
        raise ValueError(f"{name}: codebookCount 必须 > 0, got {codebook_count}")

    effective_k = min(int(codebook_count), features.shape[0], 65535)
    if effective_k != codebook_count:
        _print_warn(f"{name}: codebookCount {codebook_count} 调整为 {effective_k}")

    if effective_k == 1:
        centroid = np.mean(features.astype(np.float32, copy=False), axis=0, keepdims=True)
        labels = np.zeros((features.shape[0],), dtype=np.uint16)
        return centroid.astype(np.float32, copy=False), labels

    rng = np.random.default_rng(seed)
    fit_sample_count = min(features.shape[0], max(max_fit_samples, effective_k))
    fit_idx = _weighted_choice_no_replace(rng, features.shape[0], fit_sample_count, weights)
    if fit_idx.size == 0:
        fit_x = features.astype(np.float32, copy=False)
    else:
        fit_x = features[fit_idx].astype(np.float32, copy=False)

    _print_info(
        f"{name}: fitting MiniBatchKMeans(k={effective_k}, sample={fit_x.shape[0]}, dim={fit_x.shape[1]})"
    )
    km = MiniBatchKMeans(n_clusters=effective_k, random_state=seed, batch_size=4096, n_init=3)
    km.fit(fit_x)

    labels = np.empty((features.shape[0],), dtype=np.uint16)
    batch_size = 65536
    for start in range(0, features.shape[0], batch_size):
        end = min(start + batch_size, features.shape[0])
        labels[start:end] = km.predict(features[start:end].astype(np.float32, copy=False)).astype(np.uint16)
        if features.shape[0] > batch_size and start == 0:
            _print_info(f"{name}: predicting labels in batches of {batch_size}")

    return km.cluster_centers_.astype(np.float32, copy=False), labels


def _encode_centroids_bytes(centroids: np.ndarray, centroids_type: str) -> bytes:
    if centroids_type == "f16":
        return centroids.astype("<f2", copy=False).tobytes(order="C")
    if centroids_type == "f32":
        return centroids.astype("<f4", copy=False).tobytes(order="C")
    raise ValueError(f"unknown sh_centroids_type: {centroids_type}")


def _build_v2_meta_bytes(
    *,
    band_infos: dict[int, _V2BandInfo],
    temporal_gaussian_cutoff: float,
) -> bytes:
    out = bytearray()
    out.extend(_V2_META_PREFIX_STRUCT.pack(1, float(temporal_gaussian_cutoff), 0, 0))
    for band in (1, 2, 3):
        info = band_infos.get(band, _V2BandInfo(codebook_count=0, centroids_type=0, labels_encoding=0))
        out.extend(
            _V2_BAND_INFO_STRUCT.pack(
                int(info.codebook_count),
                int(info.centroids_type),
                int(info.labels_encoding),
                0,
            )
        )
    if len(out) != 64:
        raise RuntimeError(f"internal error: META length={len(out)}, expected 64")
    return bytes(out)


def _write_splat4d_v2(
    *,
    output_path: Path,
    rec: np.ndarray,
    sh_bands: int,
    time_model: int,
    frame_count: int,
    temporal_gaussian_cutoff: float,
    band_infos: dict[int, _V2BandInfo],
    sh_sections: list[_V2Section],
) -> None:
    shct_by_band = {section.band: section for section in sh_sections if section.kind == "SHCT"}
    shlb_by_band = {section.band: section for section in sh_sections if section.kind == "SHLB"}
    expected_labels_bytes = int(rec.shape[0]) * 2
    for band, info in band_infos.items():
        coeff_offset, coeff_count = _band_coeff_range(band)
        del coeff_offset
        shct = shct_by_band.get(band)
        shlb = shlb_by_band.get(band)
        if shct is None or shlb is None:
            raise ValueError(f"v2 内部错误: band={band} 缺少 SHCT/SHLB")
        scalar_bytes = 2 if info.centroids_type == 1 else 4
        expected_centroids_bytes = int(info.codebook_count) * coeff_count * 3 * scalar_bytes
        if len(shct.payload) != expected_centroids_bytes:
            raise ValueError(
                f"v2 内部错误: SHCT bytes mismatch for band={band}, got {len(shct.payload)}, expected {expected_centroids_bytes}"
            )
        if len(shlb.payload) != expected_labels_bytes:
            raise ValueError(
                f"v2 内部错误: SHLB bytes mismatch for band={band}, got {len(shlb.payload)}, expected {expected_labels_bytes}"
            )

    sections = [
        _V2Section("RECS", 0, 0, 0, rec.tobytes(order="C")),
        _V2Section("META", 0, 0, 0, _build_v2_meta_bytes(band_infos=band_infos, temporal_gaussian_cutoff=temporal_gaussian_cutoff)),
        *sh_sections,
    ]

    payload_size = sum(len(section.payload) for section in sections)
    section_table_offset = _V2_HEADER_STRUCT.size + payload_size

    entries: list[_ParsedV2Section] = []
    offset = _V2_HEADER_STRUCT.size
    for section in sections:
        entries.append(
            _ParsedV2Section(
                kind=section.kind,
                band=section.band,
                start_frame=section.start_frame,
                frame_count=section.frame_count,
                offset=offset,
                length=len(section.payload),
            )
        )
        offset += len(section.payload)

    header_bytes = _V2_HEADER_STRUCT.pack(
        b"SPL4DV02",
        2,
        _V2_HEADER_STRUCT.size,
        len(entries),
        64,
        int(rec.shape[0]),
        int(sh_bands),
        int(time_model),
        int(frame_count),
        int(section_table_offset),
        0,
        0,
    )

    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("wb") as fp:
        fp.write(header_bytes)
        for section in sections:
            fp.write(section.payload)
        fp.write(_V2_TABLE_HEADER_STRUCT.pack(b"SECT", 1, len(entries), 0))
        for entry in entries:
            fp.write(
                _V2_SECTION_ENTRY_STRUCT.pack(
                    _fourcc_to_u32(entry.kind),
                    entry.band,
                    entry.start_frame,
                    entry.frame_count,
                    entry.offset,
                    entry.length,
                )
            )


def _parse_splat4d_v2(path: Path) -> tuple[_ParsedV2Header, list[_ParsedV2Section]]:
    data = path.read_bytes()
    if len(data) < _V2_HEADER_STRUCT.size:
        raise ValueError(f"{path}: 文件长度过小,不足 64 bytes")

    (
        magic_b,
        version,
        header_size_bytes,
        section_count,
        record_size_bytes,
        splat_count,
        sh_bands,
        time_model,
        frame_count,
        section_table_offset,
        _reserved0,
        _reserved1,
    ) = _V2_HEADER_STRUCT.unpack_from(data, 0)

    header = _ParsedV2Header(
        magic=magic_b.decode("ascii"),
        version=version,
        header_size_bytes=header_size_bytes,
        section_count=section_count,
        record_size_bytes=record_size_bytes,
        splat_count=splat_count,
        sh_bands=sh_bands,
        time_model=time_model,
        frame_count=frame_count,
        section_table_offset=section_table_offset,
    )

    if header.magic != "SPL4DV02":
        raise ValueError(f"{path}: invalid magic {header.magic!r}")
    if header.version != 2:
        raise ValueError(f"{path}: invalid version {header.version}")
    if header.header_size_bytes != 64:
        raise ValueError(f"{path}: invalid headerSizeBytes {header.header_size_bytes}")
    if header.record_size_bytes != 64:
        raise ValueError(f"{path}: invalid recordSizeBytes {header.record_size_bytes}")
    if header.section_table_offset + _V2_TABLE_HEADER_STRUCT.size > len(data):
        raise ValueError(f"{path}: sectionTableOffset 超出文件范围")

    table_magic, table_version, table_section_count, _table_reserved = _V2_TABLE_HEADER_STRUCT.unpack_from(
        data, header.section_table_offset
    )
    if table_magic != b"SECT":
        raise ValueError(f"{path}: invalid section table magic {table_magic!r}")
    if table_version != 1:
        raise ValueError(f"{path}: invalid section table version {table_version}")
    if table_section_count != header.section_count:
        raise ValueError(
            f"{path}: header.sectionCount={header.section_count} 与 table.sectionCount={table_section_count} 不一致"
        )

    sections: list[_ParsedV2Section] = []
    cursor = header.section_table_offset + _V2_TABLE_HEADER_STRUCT.size
    for _ in range(header.section_count):
        if cursor + _V2_SECTION_ENTRY_STRUCT.size > len(data):
            raise ValueError(f"{path}: section entry 超出文件范围")
        kind_u32, band, start_frame, section_frame_count, offset, length = _V2_SECTION_ENTRY_STRUCT.unpack_from(data, cursor)
        section = _ParsedV2Section(
            kind=_u32_to_fourcc(kind_u32),
            band=band,
            start_frame=start_frame,
            frame_count=section_frame_count,
            offset=offset,
            length=length,
        )
        if section.offset + section.length > len(data):
            raise ValueError(
                f"{path}: section {section.kind} 越界, offset={section.offset}, length={section.length}, file={len(data)}"
            )
        sections.append(section)
        cursor += _V2_SECTION_ENTRY_STRUCT.size

    return header, sections


def _run_single_frame_v2_mode(
    *,
    ply_path: Path,
    output_path: Path,
    scale_mode: str,
    opacity_mode: str,
    sh_bands_arg: Optional[int],
    sh_codebook_count: int,
    sh_centroids_type: str,
    seed: int,
    self_check: bool,
) -> None:
    vertices = _read_ply_vertices(ply_path)
    rest_fields = _find_rest_fields(vertices)
    detected_sh_bands = _detect_sh_bands_from_rest_fields(rest_fields)

    if sh_bands_arg is None:
        sh_bands = detected_sh_bands
    else:
        sh_bands = int(sh_bands_arg)

    if sh_bands < 0 or sh_bands > 3:
        raise ValueError(f"--sh-bands 必须是 0..3, got {sh_bands}")
    if sh_bands > detected_sh_bands:
        raise ValueError(f"请求导出 shBands={sh_bands},但 PLY 只能提供 shBands={detected_sh_bands}.")
    if detected_sh_bands > sh_bands:
        _print_warn(f"PLY 检测到 shBands={detected_sh_bands},但本次只导出前 {sh_bands} 个 band")
    if sh_bands == 0 and detected_sh_bands > 0:
        _print_warn("当前导出被显式设为 shBands=0,将忽略 PLY 中的高阶 SH")

    expected_rest_coeff_count = (sh_bands + 1) * (sh_bands + 1) - 1 if sh_bands > 0 else 0
    rest_field_names = rest_fields[: expected_rest_coeff_count * 3] if sh_bands > 0 else None
    frame = _read_ply_frame(ply_path, rest_field_names)

    rec = _build_records(
        positions=frame.positions,
        velocities=np.zeros_like(frame.positions, dtype=np.float32),
        f_dc=frame.f_dc,
        opacity_raw=frame.opacity_raw,
        scale_raw=frame.scale_raw,
        rot_raw=frame.rot_raw,
        time0=0.0,
        duration=1.0,
        scale_mode=scale_mode,
        opacity_mode=opacity_mode,
    )

    sh_sections: list[_V2Section] = []
    band_infos: dict[int, _V2BandInfo] = {}
    if sh_bands > 0:
        if frame.rest is None:
            raise ValueError("内部错误: 期望存在 SH rest,但读取结果为空")

        importance = _build_sh_importance_weights(frame, scale_mode, opacity_mode)
        centroids_type_code = 1 if sh_centroids_type == "f16" else 2

        for band in range(1, sh_bands + 1):
            coeff_offset, coeff_count = _band_coeff_range(band)
            band_features = frame.rest[:, coeff_offset : coeff_offset + coeff_count, :].reshape(
                frame.rest.shape[0], coeff_count * 3
            )
            centroids, labels = _fit_weighted_minibatch_kmeans(
                name=f"sh{band}",
                features=band_features,
                weights=importance,
                codebook_count=sh_codebook_count,
                seed=seed + band,
                max_fit_samples=200_000,
            )
            centroids3 = centroids.reshape(centroids.shape[0], coeff_count, 3)
            sh_sections.append(
                _V2Section(
                    kind="SHCT",
                    band=band,
                    start_frame=0,
                    frame_count=0,
                    payload=_encode_centroids_bytes(centroids3, sh_centroids_type),
                )
            )
            sh_sections.append(
                _V2Section(
                    kind="SHLB",
                    band=band,
                    start_frame=0,
                    frame_count=0,
                    payload=labels.astype("<u2", copy=False).tobytes(order="C"),
                )
            )
            band_infos[band] = _V2BandInfo(
                codebook_count=int(centroids.shape[0]),
                centroids_type=centroids_type_code,
                labels_encoding=1,
            )
            _print_info(
                f"sh{band}: codebookCount={centroids.shape[0]}, coeffCount={coeff_count}, centroidsType={centroids_type_code}"
            )

    _write_splat4d_v2(
        output_path=output_path,
        rec=rec,
        sh_bands=sh_bands,
        time_model=1,
        frame_count=1,
        temporal_gaussian_cutoff=0.01,
        band_infos=band_infos,
        sh_sections=sh_sections,
    )

    if self_check:
        header, sections = _parse_splat4d_v2(output_path)
        if header.splat_count != rec.shape[0]:
            raise ValueError(f"self-check: splatCount mismatch {header.splat_count} vs {rec.shape[0]}")
        if header.sh_bands != sh_bands:
            raise ValueError(f"self-check: shBands mismatch {header.sh_bands} vs {sh_bands}")
        section_names = ", ".join(f"{section.kind}(band={section.band})" for section in sections)
        _print_info(
            f"self-check ok: splatCount={header.splat_count}, shBands={header.sh_bands}, frameCount={header.frame_count}"
        )
        _print_info(f"self-check sections: {section_names}")

    print(f"[OK] wrote {len(rec):,} splats -> {output_path}")


# -----------------------------------------------------------------------------
# CLI
# -----------------------------------------------------------------------------


def _build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Convert PLY sequence to .splat4d")
    input_group = parser.add_mutually_exclusive_group(required=True)
    input_group.add_argument("--input-ply", type=Path, help="单个普通 3DGS .ply 文件")
    input_group.add_argument("--input-dir", type=Path, help="包含一个或多个 .ply 的目录")
    parser.add_argument("--output", type=Path, required=True, help="输出 .splat4d 路径")
    parser.add_argument(
        "--mode",
        choices=["average", "keyframe"],
        default="average",
        help="average: N 条记录(平均速度). keyframe: N*segments 条记录(分段速度).",
    )
    parser.add_argument("--frame-step", type=int, default=5, help="keyframe 模式的步长(帧间隔)")
    parser.add_argument(
        "--scale-mode",
        choices=["log", "linear"],
        default="log",
        help="PLY 的 scale_0/1/2 是 log(scale) 还是线性 scale",
    )
    parser.add_argument(
        "--opacity-mode",
        choices=["logit", "linear"],
        default="logit",
        help="PLY 的 opacity 是 logit(sigmoid 前)还是线性 alpha",
    )
    parser.add_argument(
        "--splat4d-version",
        type=int,
        choices=[1, 2],
        default=1,
        help="1: 历史 raw v1 exporter. 2: 单帧 `.splat4d v2 + SH` exporter",
    )
    parser.add_argument(
        "--sh-bands",
        type=int,
        default=None,
        help="仅对 v2 有意义. 缺省时自动从 `f_rest_*` 推导 0..3",
    )
    parser.add_argument(
        "--sh-codebook-count",
        type=int,
        default=4096,
        help="仅对 v2+SH 有意义. 每个 band 的 codebook 项数上限",
    )
    parser.add_argument(
        "--sh-centroids-type",
        choices=["f16", "f32"],
        default="f32",
        help="仅对 v2+SH 有意义. SHCT 里 centroids 的存储精度",
    )
    parser.add_argument("--seed", type=int, default=1234, help="v2 SH codebook 的随机种子")
    parser.add_argument(
        "--self-check",
        action="store_true",
        help="写出后做一次最小结构自检. v2 会校验 header/section table, v1 会校验文件长度是 64 的倍数",
    )
    return parser


def main(argv: list[str]) -> int:
    parser = _build_arg_parser()
    args = parser.parse_args(argv)

    try:
        ply_files = _resolve_input_ply_files(args)

        _print_info(f"input frames: {len(ply_files)}")
        _print_info(f"first: {ply_files[0]}")
        _print_info(f"last : {ply_files[-1]}")
        _print_info(f"target version: v{args.splat4d_version}")

        if args.splat4d_version == 1:
            if args.sh_bands not in (None, 0):
                raise ValueError("`.splat4d v1` 不支持高阶 SH. 如需 SH1/2/3,请改用 `--splat4d-version 2`")

            if args.mode == "average":
                _run_average_mode(
                    ply_files=ply_files,
                    output_path=args.output,
                    scale_mode=args.scale_mode,
                    opacity_mode=args.opacity_mode,
                )
            else:
                _run_keyframe_mode(
                    ply_files=ply_files,
                    output_path=args.output,
                    frame_step=args.frame_step,
                    scale_mode=args.scale_mode,
                    opacity_mode=args.opacity_mode,
                )

            if args.self_check:
                size = args.output.stat().st_size
                if size % 64 != 0:
                    raise ValueError(f"self-check: v1 输出长度 {size} 不是 64 的整数倍")
                _print_info(f"self-check ok: raw v1 bytes={size}, records={size // 64}")
            return 0

        if args.mode != "average":
            raise ValueError("当前 `.splat4d v2` exporter 只支持 `--mode average`, 不支持 `keyframe`")
        if len(ply_files) != 1:
            raise ValueError(
                "当前 `.splat4d v2` exporter 先只支持单帧输入. 多帧序列若直接写 v2,会把动态位置和静态 SH 混成不对称资产"
            )

        _run_single_frame_v2_mode(
            ply_path=ply_files[0],
            output_path=args.output,
            scale_mode=args.scale_mode,
            opacity_mode=args.opacity_mode,
            sh_bands_arg=args.sh_bands,
            sh_codebook_count=args.sh_codebook_count,
            sh_centroids_type=args.sh_centroids_type,
            seed=int(args.seed),
            self_check=bool(args.self_check),
        )
        return 0
    except ValueError as exc:
        _print_error(str(exc))
        return 2


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
