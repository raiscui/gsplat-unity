#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
把 gaussian-splatting 风格的 PLY 序列转换为 `.splat4d`.

为什么需要这个脚本:
- 很多动态 3DGS/4DGS 方法(例如 4DGaussians/Deformable-GS)的核心是 time-conditioned deformation.
- 它们通常不会直接输出 per-gaussian 的 `velocity/time/duration`.
- 但本仓库的 `.splat4d` 需要这 3 个 4D 字段,并且假设线性运动:
    pos(t) = pos0 + vel * (t - time0)

因此我们用“多时间点采样 + 差分/分段”的方式近似,把 PLY 序列压成 `.splat4d`.

输入约定(每个 PLY 都必须包含这些字段):
- x, y, z
- f_dc_0, f_dc_1, f_dc_2
- opacity
- scale_0, scale_1, scale_2
- rot_0, rot_1, rot_2, rot_3

输出约定(和 `Editor/GsplatSplat4DImporter.cs` 对齐):
- 无 header, little-endian, 64 bytes/record
- scale 写入的是线性尺度
- alpha 写入的是 [0,1] opacity
- rgb 写入的是 baseRgb = f_dc * SH_C0 + 0.5 的量化结果
- quaternion 写入的是 [-1,1] 分量的量化结果(与 SplatVFX 一致)
"""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Optional

import numpy as np

SH_C0: float = 0.28209479177387814


_TIME_FILE_RE = re.compile(r"(?:^|/|\\\\)(?:time_)?(\\d+)(?:\\D|$)")


_PLY_TYPE_TO_DTYPE: dict[str, str] = {
    # 参考 PLY 规范的常见类型名.
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
    # 只实现我们需要的最小子集:
    # - format: ascii | binary_little_endian | binary_big_endian
    # - element vertex + property 标量字段
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

        if header.fmt == "binary_little_endian" or header.fmt == "binary_big_endian":
            data = np.fromfile(fp, dtype=dtype, count=header.vertex_count)
        elif header.fmt == "ascii":
            # ascii 模式通常只用于调试或小文件.
            # 这里用逐行解析,避免引入额外依赖.
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
                    # 顶部已经检查过 len(items) < cols.
                    # 这里要求严格相等,避免出现我们不理解的额外列.
                    raise ValueError(f"PLY: vertex line has unexpected extra columns: {path} line={i}")
                for (name, dt), token in zip(header.vertex_props, items):
                    # 用 numpy dtype 做一次显式转换,确保与 binary 行为一致.
                    data[name][i] = np.array(token, dtype=dt)
        else:
            raise ValueError(f"PLY: unsupported format: {header.fmt}")
    return data


@dataclass(frozen=True)
class PlyFrame:
    positions: np.ndarray  # [N, 3] float32
    f_dc: np.ndarray  # [N, 3] float32
    opacity_raw: np.ndarray  # [N] float32
    scale_raw: np.ndarray  # [N, 3] float32
    rot_raw: np.ndarray  # [N, 4] float32, (w,x,y,z) but may be un-normalized


def _stable_sigmoid(x: np.ndarray) -> np.ndarray:
    """数值稳定的 sigmoid,输入输出都用 float32."""
    x = x.astype(np.float32, copy=False)
    out = np.empty_like(x, dtype=np.float32)
    pos = x >= 0
    out[pos] = 1.0 / (1.0 + np.exp(-x[pos]))
    exp_x = np.exp(x[~pos])
    out[~pos] = exp_x / (1.0 + exp_x)
    return out


def _normalize_quat_wxyz(q: np.ndarray) -> np.ndarray:
    """把 quaternion 归一化,并做 w>=0 的半球规范化,减少量化抖动."""
    q = q.astype(np.float32, copy=False)
    norm = np.linalg.norm(q, axis=1, keepdims=True).astype(np.float32)  # [N,1]
    norm1 = norm.squeeze(1)  # [N]
    good = np.isfinite(norm1) & (norm1 >= 1e-8)

    out = np.empty_like(q, dtype=np.float32)
    out[good] = q[good] / norm[good]
    out[~good] = np.array([1.0, 0.0, 0.0, 0.0], dtype=np.float32)

    # quaternion 的 q 与 -q 表示同一旋转.
    # 这里强制 w>=0,可以让量化更稳定,减少帧间符号翻转带来的噪声.
    flip = out[:, 0] < 0
    out[flip] *= -1.0
    return out


def _quantize_quat_to_u8(q_norm: np.ndarray) -> np.ndarray:
    """[-1,1] float -> uint8,与 `.splat4d` importer 的还原规则一致."""
    q = np.clip(q_norm, -1.0, 1.0)
    q8 = np.round(q * 128.0 + 128.0).astype(np.int32)
    q8 = np.clip(q8, 0, 255).astype(np.uint8)
    return q8


def _quantize_0_1_to_u8(x: np.ndarray) -> np.ndarray:
    x = np.clip(x, 0.0, 1.0).astype(np.float32, copy=False)
    return np.clip(np.round(x * 255.0), 0, 255).astype(np.uint8)


def _sort_key(p: Path) -> tuple:
    # 优先按 `time_00001.ply` 这种数字排序.
    m = _TIME_FILE_RE.search(str(p))
    if m is not None:
        return (0, int(m.group(1)))
    return (1, str(p))


def _list_ply_files(input_dir: Path) -> list[Path]:
    files = [p for p in input_dir.iterdir() if p.is_file() and p.suffix.lower() == ".ply"]
    files.sort(key=_sort_key)
    return files


def _read_ply_frame(path: Path) -> PlyFrame:
    v = _read_ply_vertices(path)

    def get_f32(name: str) -> np.ndarray:
        if name not in v.dtype.names:
            raise ValueError(f"{path}: 缺少字段 {name}")
        return np.asarray(v[name], dtype=np.float32)

    positions = np.stack([get_f32("x"), get_f32("y"), get_f32("z")], axis=1)
    f_dc = np.stack([get_f32("f_dc_0"), get_f32("f_dc_1"), get_f32("f_dc_2")], axis=1)
    opacity_raw = get_f32("opacity").reshape(-1)
    scale_raw = np.stack([get_f32("scale_0"), get_f32("scale_1"), get_f32("scale_2")], axis=1)
    rot_raw = np.stack([get_f32("rot_0"), get_f32("rot_1"), get_f32("rot_2"), get_f32("rot_3")], axis=1)

    return PlyFrame(
        positions=positions,
        f_dc=f_dc,
        opacity_raw=opacity_raw,
        scale_raw=scale_raw,
        rot_raw=rot_raw,
    )


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

    # 颜色: f_dc -> baseRgb
    base_rgb = 0.5 + SH_C0 * f_dc.astype(np.float32, copy=False)
    rgb8 = _quantize_0_1_to_u8(base_rgb)

    # alpha: opacity(raw) -> alpha(0..1)
    if opacity_mode == "logit":
        alpha = _stable_sigmoid(opacity_raw)
    elif opacity_mode == "linear":
        alpha = opacity_raw.astype(np.float32, copy=False)
    else:
        raise ValueError(f"unknown opacity_mode: {opacity_mode}")
    a8 = _quantize_0_1_to_u8(alpha)

    # scale: raw(log) -> linear
    if scale_mode == "log":
        scale_lin = np.exp(scale_raw.astype(np.float32, copy=False))
    elif scale_mode == "linear":
        scale_lin = scale_raw.astype(np.float32, copy=False)
    else:
        raise ValueError(f"unknown scale_mode: {scale_mode}")

    # rotation: normalize + quantize
    q_norm = _normalize_quat_wxyz(rot_raw)
    q8 = _quantize_quat_to_u8(q_norm)

    # 打包: 64 bytes/record,不允许额外 padding.
    # 注意 dtype 需要显式 little-endian,并保持 align=False.
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
    # numpy -> bytes 是一次性拷贝.
    # 对大文件来说,用 buffered I/O 足够了.
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
        # 时间假设均匀覆盖 [0,1],所以 dt = 1.0
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
    with output_path.open("wb") as f:
        _write_records(f, rec)

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
    if n_frames == 1:
        raise ValueError("internal error: n_frames==1")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    total_records = 0
    segments = 0

    with output_path.open("wb") as out:
        # 注意: 不能要求 (n_frames - 1) 必须被 frame_step 整除.
        # 如果硬做整除,末尾会留一个时间空窗,导致 t 接近 1.0 时 splat 全不可见.
        # 因此这里改成: 最后一段不足 frame_step 时,自动用更短的尾段补齐到最后一帧.
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
                print(
                    f"[keyframe] segment {i:>5} -> {j:>5} "
                    f"t0={t0:.4f} dt={dt:.4f} (+{len(rec):,} records)"
                )

    print(f"[OK] wrote {total_records:,} splats -> {output_path}")
    print(f"     frames={n_frames}, frame_step={frame_step}, segments={segments}")


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Convert PLY sequence to .splat4d")
    parser.add_argument("--input-dir", type=Path, required=True, help="包含 time_*.ply 的目录")
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

    args = parser.parse_args(argv)

    ply_files = _list_ply_files(args.input_dir)
    if not ply_files:
        print(f"未找到 .ply: {args.input_dir}", file=sys.stderr)
        return 2

    print(f"[Info] input frames: {len(ply_files)}")
    print(f"[Info] first: {ply_files[0]}")
    print(f"[Info] last : {ply_files[-1]}")

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

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
