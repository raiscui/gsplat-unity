#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import subprocess
import struct
import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "ply_sequence_to_splat4d.py"
FIXTURE_PLY_PATH = Path(__file__).resolve().parent / "data" / "single_frame_valid_3dgs.ply"

RECORD_DTYPE = np.dtype(
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


def _minimal_single_frame_ply_text(*, include_opacity: bool = True) -> str:
    # 说明:
    # - 这是普通单帧 3DGS 的最小可读样例.
    # - 测试里按需移除 `opacity`,用来验证“缺字段时明确失败”的语义.
    header_and_value = [
        ("property float x", "0.0"),
        ("property float y", "0.0"),
        ("property float z", "0.0"),
        ("property float f_dc_0", "0.10"),
        ("property float f_dc_1", "0.20"),
        ("property float f_dc_2", "0.30"),
        ("property float scale_0", "1.0"),
        ("property float scale_1", "1.0"),
        ("property float scale_2", "1.0"),
        ("property float rot_0", "1.0"),
        ("property float rot_1", "0.0"),
        ("property float rot_2", "0.0"),
        ("property float rot_3", "0.0"),
    ]
    if include_opacity:
        header_and_value.insert(6, ("property float opacity", "1.0"))

    header_lines = [
        "ply",
        "format ascii 1.0",
        "element vertex 1",
        *[item[0] for item in header_and_value],
        "end_header",
    ]
    value_line = " ".join(item[1] for item in header_and_value)
    return "\n".join([*header_lines, value_line, ""])


def _minimal_single_frame_sh3_ply_text() -> str:
    # 说明:
    # - 生成 2 个 splat 的最小 SH3 PLY.
    # - 每个 splat 都带完整 45 个 `f_rest_*`,用于验证 `.splat4d v2 + SH3` 导出结构.
    base_fields = [
        "property float x",
        "property float y",
        "property float z",
        "property float f_dc_0",
        "property float f_dc_1",
        "property float f_dc_2",
        "property float opacity",
        "property float scale_0",
        "property float scale_1",
        "property float scale_2",
        "property float rot_0",
        "property float rot_1",
        "property float rot_2",
        "property float rot_3",
    ]
    rest_fields = [f"property float f_rest_{idx}" for idx in range(45)]

    header_lines = [
        "ply",
        "format ascii 1.0",
        "element vertex 2",
        *base_fields,
        *rest_fields,
        "end_header",
    ]

    values = []
    for vertex_idx in range(2):
        row = [
            f"{float(vertex_idx):.2f}",
            f"{float(vertex_idx) * 0.1:.2f}",
            f"{float(vertex_idx) * 0.2:.2f}",
            f"{0.10 + 0.02 * vertex_idx:.4f}",
            f"{0.20 + 0.02 * vertex_idx:.4f}",
            f"{0.30 + 0.02 * vertex_idx:.4f}",
            "1.0",
            "1.0",
            "1.0",
            "1.0",
            "1.0",
            "0.0",
            "0.0",
            "0.0",
        ]
        row.extend(f"{0.001 * (vertex_idx + 1) * (rest_idx + 1):.6f}" for rest_idx in range(45))
        values.append(" ".join(row))

    return "\n".join([*header_lines, *values, ""])


def _single_vertex_channel_major_sh3_ply_text() -> str:
    # 说明:
    # - 这里故意把 `f_rest_*` 写成 `RRR... GGG... BBB...` 三段.
    # - 这样导出后只要读取顺序错成 `RGBRGB...`,测试就会立刻失败.
    base_fields = [
        "property float x",
        "property float y",
        "property float z",
        "property float f_dc_0",
        "property float f_dc_1",
        "property float f_dc_2",
        "property float opacity",
        "property float scale_0",
        "property float scale_1",
        "property float scale_2",
        "property float rot_0",
        "property float rot_1",
        "property float rot_2",
        "property float rot_3",
    ]
    rest_fields = [f"property float f_rest_{idx}" for idx in range(45)]
    rest_values = [str(value) for value in range(1, 16)]
    rest_values.extend(str(value) for value in range(101, 116))
    rest_values.extend(str(value) for value in range(201, 216))

    header_lines = [
        "ply",
        "format ascii 1.0",
        "element vertex 1",
        *base_fields,
        *rest_fields,
        "end_header",
    ]
    value_line = " ".join(
        [
            "0.0",
            "0.0",
            "0.0",
            "0.10",
            "0.20",
            "0.30",
            "1.0",
            "1.0",
            "1.0",
            "1.0",
            "1.0",
            "0.0",
            "0.0",
            "0.0",
            *rest_values,
        ]
    )
    return "\n".join([*header_lines, value_line, ""])


def _parse_v2_file(path: Path) -> tuple[dict[str, int | str], list[dict[str, int | str]], bytes]:
    data = path.read_bytes()
    header_values = struct.unpack_from("<8sIIIIIIIIQQQ", data, 0)
    header = {
        "magic": header_values[0].decode("ascii"),
        "version": header_values[1],
        "header_size_bytes": header_values[2],
        "section_count": header_values[3],
        "record_size_bytes": header_values[4],
        "splat_count": header_values[5],
        "sh_bands": header_values[6],
        "time_model": header_values[7],
        "frame_count": header_values[8],
        "section_table_offset": header_values[9],
    }

    table_offset = int(header["section_table_offset"])
    table_magic, table_version, table_section_count, _reserved = struct.unpack_from("<4sIII", data, table_offset)
    if table_magic != b"SECT":
        raise AssertionError(f"unexpected section table magic: {table_magic!r}")
    if table_version != 1:
        raise AssertionError(f"unexpected section table version: {table_version}")
    if table_section_count != header["section_count"]:
        raise AssertionError("section count mismatch between header and table")

    sections = []
    cursor = table_offset + 16
    for _ in range(int(header["section_count"])):
        kind_u32, band, start_frame, frame_count, offset, length = struct.unpack_from("<IIIIQQ", data, cursor)
        kind = struct.pack("<I", kind_u32).decode("ascii")
        sections.append(
            {
                "kind": kind,
                "band": band,
                "start_frame": start_frame,
                "frame_count": frame_count,
                "offset": offset,
                "length": length,
            }
        )
        cursor += 32

    return header, sections, data


def _parse_v2_meta_band_infos(sections: list[dict[str, int | str]], data: bytes) -> list[tuple[int, int, int, int]]:
    meta_section = next(section for section in sections if section["kind"] == "META")
    meta_bytes = data[int(meta_section["offset"]): int(meta_section["offset"] + meta_section["length"])]
    return [struct.unpack_from("<IIII", meta_bytes, 16 + 16 * band_idx) for band_idx in range(3)]


def _decode_v2_shct_centroids(
    *,
    sections: list[dict[str, int | str]],
    data: bytes,
    band: int,
    codebook_count: int,
    coeff_count: int,
    centroids_type: int,
) -> np.ndarray:
    shct = next(section for section in sections if section["kind"] == "SHCT" and section["band"] == band)
    payload = data[int(shct["offset"]): int(shct["offset"] + shct["length"])]
    if centroids_type == 1:
        values = np.frombuffer(payload, dtype="<f2").astype(np.float32)
    elif centroids_type == 2:
        values = np.frombuffer(payload, dtype="<f4").astype(np.float32)
    else:
        raise AssertionError(f"unexpected centroids_type: {centroids_type}")
    return values.reshape(codebook_count, coeff_count, 3)


class SingleFrameCliTests(unittest.TestCase):
    maxDiff = None

    def run_cmd(self, *args: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(SCRIPT_PATH), *args],
            text=True,
            capture_output=True,
            timeout=120,
            check=False,
        )

    def test_rejects_input_ply_and_input_dir_together(self) -> None:
        with tempfile.TemporaryDirectory(prefix="splat4d_cli_mutex_") as tmp_dir_str:
            tmp_dir = Path(tmp_dir_str)
            input_ply = tmp_dir / "single_frame.ply"
            input_ply.write_text(_minimal_single_frame_ply_text(), encoding="utf-8")

            out_path = tmp_dir / "out.splat4d"
            result = self.run_cmd(
                "--input-ply",
                str(input_ply),
                "--input-dir",
                str(tmp_dir),
                "--output",
                str(out_path),
            )

            self.assertNotEqual(
                result.returncode,
                0,
                msg=f"互斥参数本应失败,但命令意外成功.\nstdout:\n{result.stdout}\nstderr:\n{result.stderr}",
            )
            self.assertIn("not allowed with argument", result.stderr)

    def test_single_ply_writes_static_single_frame_record(self) -> None:
        with tempfile.TemporaryDirectory(prefix="splat4d_single_frame_") as tmp_dir_str:
            tmp_dir = Path(tmp_dir_str)
            out_path = tmp_dir / "single_frame.splat4d"
            result = self.run_cmd(
                "--input-ply",
                str(FIXTURE_PLY_PATH),
                "--output",
                str(out_path),
                "--mode",
                "average",
                "--opacity-mode",
                "linear",
                "--scale-mode",
                "linear",
            )

            self.assertEqual(
                result.returncode,
                0,
                msg=f"单帧导出失败.\nstdout:\n{result.stdout}\nstderr:\n{result.stderr}",
            )
            self.assertTrue(out_path.exists(), "导出成功后应该生成 .splat4d 文件")
            self.assertIn("input frames: 1", result.stderr)

            payload = out_path.read_bytes()
            self.assertEqual(len(payload), 64, "单帧单点输出应恰好为 1 条 64B record")

            records = np.frombuffer(payload, dtype=RECORD_DTYPE)
            self.assertEqual(len(records), 1)
            self.assertAlmostEqual(float(records["vx"][0]), 0.0, places=6)
            self.assertAlmostEqual(float(records["vy"][0]), 0.0, places=6)
            self.assertAlmostEqual(float(records["vz"][0]), 0.0, places=6)
            self.assertAlmostEqual(float(records["time"][0]), 0.0, places=6)
            self.assertAlmostEqual(float(records["duration"][0]), 1.0, places=6)

    def test_missing_required_gaussian_field_fails_clearly(self) -> None:
        with tempfile.TemporaryDirectory(prefix="splat4d_missing_field_") as tmp_dir_str:
            tmp_dir = Path(tmp_dir_str)
            input_ply = tmp_dir / "missing_opacity.ply"
            input_ply.write_text(
                _minimal_single_frame_ply_text(include_opacity=False),
                encoding="utf-8",
            )

            out_path = tmp_dir / "missing_field.splat4d"
            result = self.run_cmd(
                "--input-ply",
                str(input_ply),
                "--output",
                str(out_path),
                "--opacity-mode",
                "linear",
                "--scale-mode",
                "linear",
            )

            self.assertNotEqual(
                result.returncode,
                0,
                msg=f"缺少必需字段本应失败,但命令意外成功.\nstdout:\n{result.stdout}\nstderr:\n{result.stderr}",
            )
            self.assertIn("[splat4d][error]", result.stderr)
            self.assertIn("缺少字段 opacity", result.stderr)

    def test_single_frame_rejects_keyframe_mode_clearly(self) -> None:
        with tempfile.TemporaryDirectory(prefix="splat4d_keyframe_single_") as tmp_dir_str:
            tmp_dir = Path(tmp_dir_str)
            out_path = tmp_dir / "keyframe_single.splat4d"
            result = self.run_cmd(
                "--input-ply",
                str(FIXTURE_PLY_PATH),
                "--output",
                str(out_path),
                "--mode",
                "keyframe",
                "--opacity-mode",
                "linear",
                "--scale-mode",
                "linear",
            )

            self.assertNotEqual(
                result.returncode,
                0,
                msg=f"单帧 keyframe 本应失败,但命令意外成功.\nstdout:\n{result.stdout}\nstderr:\n{result.stderr}",
            )
            self.assertIn("keyframe 模式至少需要 2 个 PLY", result.stderr)

    def test_single_ply_can_write_v2_sh3_sections(self) -> None:
        with tempfile.TemporaryDirectory(prefix="splat4d_v2_sh3_") as tmp_dir_str:
            tmp_dir = Path(tmp_dir_str)
            input_ply = tmp_dir / "single_frame_sh3.ply"
            input_ply.write_text(_minimal_single_frame_sh3_ply_text(), encoding="utf-8")

            out_path = tmp_dir / "single_frame_sh3_v2.splat4d"
            result = self.run_cmd(
                "--input-ply",
                str(input_ply),
                "--output",
                str(out_path),
                "--mode",
                "average",
                "--opacity-mode",
                "linear",
                "--scale-mode",
                "linear",
                "--splat4d-version",
                "2",
                "--sh-bands",
                "3",
                "--sh-codebook-count",
                "2",
                "--sh-centroids-type",
                "f32",
                "--self-check",
            )

            self.assertEqual(
                result.returncode,
                0,
                msg=f"v2 + SH3 导出失败.\nstdout:\n{result.stdout}\nstderr:\n{result.stderr}",
            )
            self.assertTrue(out_path.exists(), "导出成功后应该生成 `.splat4d v2` 文件")
            self.assertIn("self-check ok", result.stderr)

            header, sections, data = _parse_v2_file(out_path)
            self.assertEqual("SPL4DV02", header["magic"])
            self.assertEqual(2, header["version"])
            self.assertEqual(64, header["header_size_bytes"])
            self.assertEqual(64, header["record_size_bytes"])
            self.assertEqual(2, header["splat_count"])
            self.assertEqual(3, header["sh_bands"])
            self.assertEqual(1, header["time_model"])
            self.assertEqual(1, header["frame_count"])
            self.assertEqual(8, header["section_count"])

            section_keys = [(section["kind"], section["band"]) for section in sections]
            self.assertEqual(
                [
                    ("RECS", 0),
                    ("META", 0),
                    ("SHCT", 1),
                    ("SHLB", 1),
                    ("SHCT", 2),
                    ("SHLB", 2),
                    ("SHCT", 3),
                    ("SHLB", 3),
                ],
                section_keys,
            )

            meta_section = next(section for section in sections if section["kind"] == "META")
            meta_bytes = data[int(meta_section["offset"]): int(meta_section["offset"] + meta_section["length"])]
            meta_version, gaussian_cutoff, delta_segment_length, reserved0 = struct.unpack_from("<IfII", meta_bytes, 0)
            self.assertEqual(1, meta_version)
            self.assertAlmostEqual(0.01, gaussian_cutoff, places=6)
            self.assertEqual(0, delta_segment_length)
            self.assertEqual(0, reserved0)

            band_infos = [struct.unpack_from("<IIII", meta_bytes, 16 + 16 * band_idx) for band_idx in range(3)]
            self.assertEqual((2, 2, 1, 0), band_infos[0])
            self.assertEqual((2, 2, 1, 0), band_infos[1])
            self.assertEqual((2, 2, 1, 0), band_infos[2])

    def test_single_ply_v2_sh3_preserves_channel_major_rest_layout(self) -> None:
        with tempfile.TemporaryDirectory(prefix="splat4d_v2_layout_") as tmp_dir_str:
            tmp_dir = Path(tmp_dir_str)
            input_ply = tmp_dir / "channel_major_sh3.ply"
            input_ply.write_text(_single_vertex_channel_major_sh3_ply_text(), encoding="utf-8")

            out_path = tmp_dir / "channel_major_sh3_v2.splat4d"
            result = self.run_cmd(
                "--input-ply",
                str(input_ply),
                "--output",
                str(out_path),
                "--mode",
                "average",
                "--opacity-mode",
                "linear",
                "--scale-mode",
                "linear",
                "--splat4d-version",
                "2",
                "--sh-bands",
                "3",
                "--sh-codebook-count",
                "1",
                "--sh-centroids-type",
                "f32",
            )

            self.assertEqual(
                result.returncode,
                0,
                msg=f"channel-major SH3 导出失败.\nstdout:\n{result.stdout}\nstderr:\n{result.stderr}",
            )

            header, sections, data = _parse_v2_file(out_path)
            self.assertEqual(1, header["splat_count"])
            band_infos = _parse_v2_meta_band_infos(sections, data)
            self.assertEqual((1, 2, 1, 0), band_infos[0])
            self.assertEqual((1, 2, 1, 0), band_infos[1])
            self.assertEqual((1, 2, 1, 0), band_infos[2])

            sh1 = _decode_v2_shct_centroids(
                sections=sections,
                data=data,
                band=1,
                codebook_count=1,
                coeff_count=3,
                centroids_type=2,
            )[0]
            sh2 = _decode_v2_shct_centroids(
                sections=sections,
                data=data,
                band=2,
                codebook_count=1,
                coeff_count=5,
                centroids_type=2,
            )[0]
            sh3 = _decode_v2_shct_centroids(
                sections=sections,
                data=data,
                band=3,
                codebook_count=1,
                coeff_count=7,
                centroids_type=2,
            )[0]

            np.testing.assert_allclose(
                sh1,
                np.array(
                    [
                        [1.0, 101.0, 201.0],
                        [2.0, 102.0, 202.0],
                        [3.0, 103.0, 203.0],
                    ],
                    dtype=np.float32,
                ),
            )
            np.testing.assert_allclose(
                sh2,
                np.array(
                    [
                        [4.0, 104.0, 204.0],
                        [5.0, 105.0, 205.0],
                        [6.0, 106.0, 206.0],
                        [7.0, 107.0, 207.0],
                        [8.0, 108.0, 208.0],
                    ],
                    dtype=np.float32,
                ),
            )
            np.testing.assert_allclose(
                sh3,
                np.array(
                    [
                        [9.0, 109.0, 209.0],
                        [10.0, 110.0, 210.0],
                        [11.0, 111.0, 211.0],
                        [12.0, 112.0, 212.0],
                        [13.0, 113.0, 213.0],
                        [14.0, 114.0, 214.0],
                        [15.0, 115.0, 215.0],
                    ],
                    dtype=np.float32,
                ),
            )

    def test_v2_rejects_multi_frame_input_clearly(self) -> None:
        with tempfile.TemporaryDirectory(prefix="splat4d_v2_multiframe_") as tmp_dir_str:
            tmp_dir = Path(tmp_dir_str)
            (tmp_dir / "time_00000.ply").write_text(_minimal_single_frame_sh3_ply_text(), encoding="utf-8")
            (tmp_dir / "time_00001.ply").write_text(_minimal_single_frame_sh3_ply_text(), encoding="utf-8")

            out_path = tmp_dir / "invalid_multi_frame_v2.splat4d"
            result = self.run_cmd(
                "--input-dir",
                str(tmp_dir),
                "--output",
                str(out_path),
                "--mode",
                "average",
                "--opacity-mode",
                "linear",
                "--scale-mode",
                "linear",
                "--splat4d-version",
                "2",
                "--sh-bands",
                "3",
            )

            self.assertNotEqual(
                result.returncode,
                0,
                msg=f"多帧 v2 输入本应失败,但命令意外成功.\nstdout:\n{result.stdout}\nstderr:\n{result.stderr}",
            )
            self.assertIn("当前 `.splat4d v2` exporter 先只支持单帧输入", result.stderr)


if __name__ == "__main__":
    unittest.main()
