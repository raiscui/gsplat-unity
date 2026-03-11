#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import json
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "ply_sequence_to_sog4d.py"


def _minimal_single_frame_ply_text() -> str:
    # 这个最小样例只覆盖 bands=0 的单帧正式入口回归.
    # 字段集合严格对齐工具当前要求,避免测试因为“字段不全”误伤 CLI 逻辑.
    return """ply
format ascii 1.0
element vertex 1
property float x
property float y
property float z
property float f_dc_0
property float f_dc_1
property float f_dc_2
property float opacity
property float scale_0
property float scale_1
property float scale_2
property float rot_0
property float rot_1
property float rot_2
property float rot_3
end_header
0.0 0.0 0.0 0.10 0.20 0.30 1.0 1.0 1.0 1.0 1.0 0.0 0.0 0.0
"""


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

    def test_pack_rejects_input_ply_and_input_dir_together(self) -> None:
        with tempfile.TemporaryDirectory(prefix="sog4d_cli_mutex_") as tmp_dir_str:
            tmp_dir = Path(tmp_dir_str)
            input_ply = tmp_dir / "single_frame.ply"
            input_ply.write_text(_minimal_single_frame_ply_text(), encoding="utf-8")

            out_path = tmp_dir / "out.sog4d"
            result = self.run_cmd(
                "pack",
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

    def test_pack_single_ply_emits_one_frame_bundle_and_validate_passes(self) -> None:
        with tempfile.TemporaryDirectory(prefix="sog4d_single_frame_") as tmp_dir_str:
            tmp_dir = Path(tmp_dir_str)
            input_ply = tmp_dir / "single_frame.ply"
            input_ply.write_text(_minimal_single_frame_ply_text(), encoding="utf-8")

            out_path = tmp_dir / "single_frame.sog4d"
            pack = self.run_cmd(
                "pack",
                "--input-ply",
                str(input_ply),
                "--output",
                str(out_path),
                "--time-mapping",
                "uniform",
                "--opacity-mode",
                "linear",
                "--scale-mode",
                "linear",
                "--scale-codebook-size",
                "1",
                "--scale-sample-count",
                "1",
                "--sh-bands",
                "0",
                "--self-check",
            )

            self.assertEqual(
                pack.returncode,
                0,
                msg=f"单帧打包失败.\nstdout:\n{pack.stdout}\nstderr:\n{pack.stderr}",
            )
            self.assertTrue(out_path.exists(), "pack 成功后应该生成 .sog4d 文件")
            self.assertIn("frames: 1", pack.stderr)
            self.assertIn("validate ok (bands=0).", pack.stderr)

            with zipfile.ZipFile(out_path, "r") as archive:
                meta = json.loads(archive.read("meta.json").decode("utf-8"))

            self.assertEqual(meta["format"], "sog4d")
            self.assertEqual(meta["frameCount"], 1)
            self.assertEqual(meta["timeMapping"]["type"], "uniform")
            self.assertEqual(len(meta["streams"]["position"]["rangeMin"]), 1)
            self.assertEqual(len(meta["streams"]["position"]["rangeMax"]), 1)

            validate = self.run_cmd("validate", "--input", str(out_path))
            self.assertEqual(
                validate.returncode,
                0,
                msg=f"单帧 bundle validate 失败.\nstdout:\n{validate.stdout}\nstderr:\n{validate.stderr}",
            )
            self.assertIn("validate ok (bands=0).", validate.stderr)


if __name__ == "__main__":
    unittest.main()
