"""Original synthetic tests only: no upstream code, artwork, network or Unity."""

import copy
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import unittest
import uuid

import prepare_player as tool


TOOL_DIRECTORY = Path(__file__).absolute().parent
PROJECT_ROOT = TOOL_DIRECTORY.parents[1]
EXPANDED = (0, 8, 16, 24, 33, 41, 49, 57, 66, 74, 82, 90, 99, 107, 115, 123,
            132, 140, 148, 156, 165, 173, 181, 189, 198, 206, 214, 222, 231, 239, 247, 255)
COLORS = ((31, 0, 0, 1), (0, 31, 0, 1), (0, 0, 31, 1), (31, 31, 31, 0),
          (0, 0, 0, 0), (1, 2, 3, 1), (15, 16, 17, 0), (30, 9, 22, 1))
EXPECTED_RGBA = ((255, 0, 0, 255), (0, 255, 0, 255), (0, 0, 255, 255), (255, 255, 255, 0),
                 (0, 0, 0, 0), (8, 16, 24, 255), (123, 132, 140, 0), (247, 74, 181, 255))


def synthetic_wms(width=4, height=2, colors=COLORS):
    header = list(tool.WMS_HEADER)
    header[4:6] = width, height
    words = [b << 0 | g << 5 | r << 10 | a << 15 for r, g, b, a in colors]
    payload = struct.pack("<" + str(len(words)) + "H", *words)
    for level, (w, h) in enumerate(tool.mip_dimensions(width, height)[1:], 1):
        payload += struct.pack("<H", 0x8000 | level) * (w * h)
    return struct.pack("<10I", *header) + payload


def synthetic_tga(bpp=32, descriptor=8):
    pixels = b"".join(bytes((b, g, r, a) if bpp == 32 else (b, g, r))
                      for r, g, b, a in EXPECTED_RGBA[:4])
    return tool.TGA_HEADER.pack(0, 0, 2, 0, 0, 0, 0, 0, 2, 2, bpp, descriptor) + pixels + tool.TGA_FOOTER


def rgba_from_top_origin_tga(data):
    fields = struct.unpack("<BBBHHBHHHHBB", data[:18])
    width, height, bpp, descriptor = fields[8:]
    channels = bpp // 8
    pixels = [tuple(data[index + offset] for offset in (2, 1, 0))
              + (data[index + 3] if channels == 4 else 255,)
              for index in range(18, 18 + width * height * channels, channels)]
    rows = [pixels[index:index + width] for index in range(0, len(pixels), width)]
    if not descriptor & 0x20:
        rows.reverse()
    return [pixel for row in rows for pixel in row]


class DecoderTests(unittest.TestCase):
    def test_every_pixel_and_tga_contract(self):
        original = bytearray(synthetic_wms())
        original_sha = hashlib.sha256(original).hexdigest()
        result = tool.decode_argb1555_level0(original)
        self.assertEqual(struct.unpack("<BBBHHBHHHHBB", result[:18]),
                         (0, 0, 2, 0, 0, 0, 0, 0, 4, 2, 32, 0x28))
        self.assertEqual(len(result), 18 + 4 * 2 * 4)
        self.assertEqual(rgba_from_top_origin_tga(result), list(EXPECTED_RGBA))
        self.assertEqual(result[18:22], b"\0\0\xff\xff")
        self.assertEqual(result[-4:], bytes((181, 74, 247, 255)))
        self.assertEqual(tool.inspect_tga(result)["origin"], "top-left")
        self.assertEqual(hashlib.sha256(original).hexdigest(), original_sha)
        self.assertEqual(tool.decode_argb1555_level0(original), result)

    def test_all_channel_quantization_and_alpha_values(self):
        colors = [(index, 31 - index, (index * 7) % 32, index % 2) for index in range(32)]
        result = tool.decode_argb1555_level0(synthetic_wms(8, 4, colors))
        expected = [(EXPANDED[r], EXPANDED[g], EXPANDED[b], 255 if a else 0) for r, g, b, a in colors]
        self.assertEqual(rgba_from_top_origin_tga(result), expected)
        self.assertEqual(result[21::4].count(0), 16)
        self.assertEqual(result[21::4].count(255), 16)

    def test_small_square_and_asymmetric_complete_chains(self):
        for width, height, count in ((1, 1, 1), (1, 4, 3), (4, 1, 3), (2, 8, 4), (4, 2, 3)):
            with self.subTest(dimensions=(width, height)):
                data = synthetic_wms(width, height, [COLORS[0]] * (width * height))
                self.assertEqual(len(tool.mip_dimensions(width, height)), count)
                self.assertEqual(rgba_from_top_origin_tga(tool.decode_argb1555_level0(data)),
                                 [EXPECTED_RGBA[0]] * (width * height))

    def test_every_fixed_header_field_rejects_unknown_formats(self):
        for index in (0, 1, 2, 3, 6, 7, 8, 9):
            with self.subTest(field=index):
                data = bytearray(synthetic_wms())
                struct.pack_into("<I", data, index * 4, tool.WMS_HEADER[index] ^ 1)
                with self.assertRaisesRegex(tool.PreparationError, "Unsupported WMS header/format"):
                    tool.decode_argb1555_level0(data)

    def test_wrong_endianness_is_not_silently_reinterpreted(self):
        header = list(tool.WMS_HEADER)
        header[4:6] = 4, 2
        with self.assertRaisesRegex(tool.PreparationError, "Unsupported WMS"):
            tool.decode_argb1555_level0(struct.pack(">10I", *header) + synthetic_wms()[40:])

    def test_header_and_each_missing_mip_or_trailing_bytes_fail(self):
        original = synthetic_wms()
        for data in (b"", original[:39], original[:40], original[:56], original[:-1],
                     original[:-2], original + b"\0", original + b"\0\0"):
            with self.subTest(length=len(data)):
                with self.assertRaises(tool.PreparationError):
                    tool.decode_argb1555_level0(data)

    def test_invalid_or_unbounded_dimensions_fail_before_pixel_allocation(self):
        for width, height in ((0, 2), (2, 0), (3, 2), (512, 1), (1, 65536)):
            with self.subTest(dimensions=(width, height)):
                header = list(tool.WMS_HEADER)
                header[4:6] = width, height
                with self.assertRaisesRegex(tool.PreparationError, "dimensions"):
                    tool.decode_argb1555_level0(struct.pack("<10I", *header))

    def test_cli_dimension_gate_is_separate_from_synthetic_codec(self):
        with self.assertRaisesRegex(tool.PreparationError, "pinned player atlas"):
            tool.decode_argb1555_level0(synthetic_wms(), expected_dimensions=(128, 256))

    def test_direct_tga_origin_and_original_bytes_are_unchanged(self):
        for bpp, descriptor, origin in ((24, 0, "bottom-left"), (32, 8, "bottom-left"),
                                        (24, 0x20, "top-left"), (32, 0x28, "top-left")):
            with self.subTest(bpp=bpp, descriptor=descriptor):
                data = synthetic_tga(bpp, descriptor)
                before = tool.fingerprint(data)
                result = tool.inspect_tga(data, {
                    "width": 2, "height": 2, "bits_per_pixel": bpp, "descriptor": descriptor,
                })
                self.assertEqual(result["origin"], origin)
                self.assertEqual(tool.fingerprint(data), before)
                self.assertEqual(result["pixel_bytes"], 4 * (bpp // 8))

    def test_unsupported_tga_and_truncation_are_explicit(self):
        original = synthetic_tga()
        invalid = [b"", original[:17], original[:-27], original + b"\0"]
        for offset, value in ((0, 1), (1, 1), (2, 10), (16, 16), (17, 0x18)):
            data = bytearray(original)
            data[offset] = value
            invalid.append(bytes(data))
        invalid.append(original[:-26] + b"\x01" + tool.TGA_FOOTER[1:])
        for data in invalid:
            with self.subTest(header=data[:18]):
                with self.assertRaises(tool.PreparationError):
                    tool.inspect_tga(data)


class SyntheticPipelineTests(unittest.TestCase):
    def setUp(self):
        self.workspace = PROJECT_ROOT / ".squad/.scratch" / ("player-tool-test-" + uuid.uuid4().hex)
        self.workspace.mkdir(parents=True, exist_ok=False)
        self.project = self.workspace / "project"
        self.code = self.project / "tools/assets"
        self.code.mkdir(parents=True)
        shutil.copyfile(TOOL_DIRECTORY / "prepare_player.py", self.code / "prepare_player.py")
        self.script = self.code / "prepare_player.py"
        self.source = self.workspace / "supplied-source"
        self.source.mkdir()
        self.destination = self.project / ".squad/.scratch/prepared"
        self.recipe = copy.deepcopy(json.loads((TOOL_DIRECTORY / tool.RECIPE_NAME).read_text()))
        self.inputs = {
            "models/plyr266.h": b"synthetic model; no upstream declarations or geometry\n",
            "models/skeleton.h": b"synthetic rig; no upstream skeleton\n",
            "img3d/armhnd.tga": synthetic_tga(),
            "img3d/hedshu.tga": synthetic_tga(24, 0),
            "img3d/BEAHLOD.wms": synthetic_wms(),
            "cambot.c": b"one\ntwo\nthree\nfour\nfive\nSynthetic notice owned by the test author.\n"
                        b"Local fixture only.\nNo upstream text.\nKeep this notice.\nLine ten.\n"
                        b"Line eleven.\nLine twelve.\n",
        }
        for entry in self.recipe["inputs"]:
            data = self.inputs[entry["path"]]
            entry.update(tool.fingerprint(data))
            if "tga" in entry:
                entry["tga"].update(width=2, height=2)
        derivative = tool.decode_argb1555_level0(self.inputs["img3d/BEAHLOD.wms"])
        self.recipe["atlas"].update(width=4, height=2, mip_levels=3, mip_bytes=22,
                                    output_bytes=len(derivative), output_sha256=tool.sha256(derivative))
        self.recipe["atlas"]["header_uint32_le"][4:6] = 4, 2
        for label, data in self.inputs.items():
            path = self.source / label
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        self.write_recipe()

    def tearDown(self):
        self.assertFalse(self.workspace.is_symlink())
        self.assertEqual(self.workspace.parent, PROJECT_ROOT / ".squad/.scratch")
        shutil.rmtree(self.workspace)

    def write_recipe(self):
        (self.code / tool.RECIPE_NAME).write_bytes(tool.json_bytes(self.recipe))

    def run_cli(self, destination=None, extra=(), source=None):
        command = [sys.executable, "-B", str(self.script), "--source-root", str(source or self.source),
                   "--destination", str(destination or self.destination), "--confirm-local-use",
                   "--authorization-reference", "synthetic-local-use"]
        return subprocess.run(command + list(extra), capture_output=True, text=True, check=False,
                              cwd=self.project, env=dict(os.environ, PYTHONDONTWRITEBYTECODE="1"),
                              timeout=20)

    def assert_failure(self, result, message):
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn(message, result.stderr)
        self.assertNotIn("Traceback", result.stderr)

    def bundle_bytes(self, destination=None):
        root = destination or self.destination
        return {path.relative_to(root).as_posix(): path.read_bytes()
                for path in root.rglob("*") if path.is_file()}

    def test_complete_cli_bundle_matches_importer_and_preserves_notice_and_sources(self):
        result = self.run_cli()
        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual(report["destination"], ".squad/.scratch/prepared")
        self.assertFalse(report["reused_identical"])
        expected_paths = {"models/plyr266.h", "models/skeleton.h", "img3d/armhnd.tga", "img3d/hedshu.tga",
                          "img3d/BEAHLOD.level0.local-preview.tga", tool.REQUEST_NAME,
                          tool.MATERIALS_NAME, tool.PROVENANCE_NAME}
        bundle = self.bundle_bytes()
        self.assertEqual(set(bundle), expected_paths)
        self.assertEqual({entry["path"] for entry in report["artifacts"]}, expected_paths)
        for artifact in report["artifacts"]:
            self.assertEqual(artifact["sha256"], tool.sha256(bundle[artifact["path"]]))
            self.assertEqual(artifact["bytes"], len(bundle[artifact["path"]]))
        for entry in self.recipe["inputs"]:
            self.assertEqual((self.source / entry["path"]).read_bytes(), self.inputs[entry["path"]])
            if entry["copy"]:
                self.assertEqual(bundle[entry["path"]], self.inputs[entry["path"]])
        request = json.loads(bundle[tool.REQUEST_NAME])
        self.assertEqual(request, dict(self.recipe["request"], reuseAuthorizationConfirmed=True,
                                       authorizationReference="synthetic-local-use"))
        self.assertEqual([entry["sourceSUnits"] for entry in request["materialMap"]],
                         [256, 256, 128, 128, 128])
        self.assertEqual([entry["sourceTUnits"] for entry in request["materialMap"]], [256] * 5)
        self.assertEqual([entry["flipV"] for entry in request["materialMap"]], [True] * 5)
        materials = json.loads(bundle[tool.MATERIALS_NAME])
        self.assertEqual(materials["ordered_runtime_textures"], self.recipe["ordered_runtime_textures"])
        self.assertEqual(materials["materialMap"], request["materialMap"])
        self.assertEqual([entry["origin"] for entry in materials["textures"]],
                         ["bottom-left", "bottom-left", "top-left"])
        provenance = json.loads(bundle[tool.PROVENANCE_NAME])
        self.assertEqual(provenance["source_notice"]["verbatim"],
                         b"".join(self.inputs["cambot.c"].splitlines(keepends=True)[5:12]).decode("ascii"))
        self.assertEqual(provenance["derivative"]["source_git_blob_sha1"],
                         tool.fingerprint(self.inputs["img3d/BEAHLOD.wms"])["git_blob_sha1"])
        self.assertEqual(provenance["derivative"]["alpha_zero_pixels"], 3)
        self.assertEqual(provenance["derivative"]["alpha_opaque_pixels"], 5)
        for data in (bundle[tool.REQUEST_NAME], bundle[tool.MATERIALS_NAME], bundle[tool.PROVENANCE_NAME]):
            self.assertNotIn(str(self.workspace).encode(), data)
            self.assertNotIn(b"/data/", data)

    def test_deterministic_outputs_across_destinations(self):
        first = self.run_cli()
        second_destination = self.destination.parent / "second"
        second = self.run_cli(second_destination)
        self.assertEqual(first.returncode, 0, first.stderr)
        self.assertEqual(second.returncode, 0, second.stderr)
        self.assertEqual(self.bundle_bytes(), self.bundle_bytes(second_destination))

    def test_source_hash_mismatch_before_any_output_creation(self):
        path = self.source / "models/plyr266.h"
        path.write_bytes(b"X" + path.read_bytes()[1:])
        self.assert_failure(self.run_cli(), "Pinned sha256 mismatch: models/plyr266.h")
        self.assertFalse(self.destination.parent.exists())

    def test_git_blob_identity_is_checked_independently(self):
        self.recipe["inputs"][0]["git_blob_sha1"] = "0" * 40
        self.write_recipe()
        self.assert_failure(self.run_cli(), "Pinned git_blob_sha1 mismatch")
        self.assertFalse(self.destination.exists())

    def test_wrong_length_and_missing_required_input_fail(self):
        path = self.source / "models/plyr266.h"
        path.write_bytes(path.read_bytes()[:-1])
        self.assert_failure(self.run_cli(), "Pinned bytes mismatch")
        path.unlink()
        self.assert_failure(self.run_cli(), "Missing required pinned input: models/plyr266.h")
        self.assertFalse(self.destination.exists())

    def test_oversized_input_is_bounded(self):
        path = self.source / "models/plyr266.h"
        path.write_bytes(path.read_bytes() + b"x")
        self.assert_failure(self.run_cli(), "exceeds its pinned byte length")

    @unittest.skipUnless(hasattr(os, "mkfifo"), "Named pipes are a POSIX input hazard.")
    def test_non_regular_input_rejected_without_blocking_open(self):
        path = self.source / "models/plyr266.h"
        path.unlink()
        os.mkfifo(path)
        self.assert_failure(self.run_cli(), "Only regular input files are supported")
        self.assertFalse(self.destination.exists())

    def test_hash_valid_unsupported_format_fails_without_substitution(self):
        data = bytearray(self.inputs["img3d/BEAHLOD.wms"])
        struct.pack_into("<I", data, 0, 32774)
        (self.source / "img3d/BEAHLOD.wms").write_bytes(data)
        self.recipe["inputs"][4].update(tool.fingerprint(data))
        self.write_recipe()
        self.assert_failure(self.run_cli(), "Unsupported WMS header/format")
        self.assertFalse(self.destination.exists())

    def test_derived_hash_gate_precedes_output(self):
        self.recipe["atlas"]["output_sha256"] = "0" * 64
        self.write_recipe()
        self.assert_failure(self.run_cli(), "pinned level-0 derivative hash")
        self.assertFalse(self.destination.exists())

    def test_pinned_atlas_header_and_mip_metadata_are_checked(self):
        for field, value in (("mip_levels", 9), ("mip_bytes", 1), ("header_uint32_le", list(tool.WMS_HEADER))):
            with self.subTest(field=field):
                original = self.recipe["atlas"][field]
                self.recipe["atlas"][field] = value
                self.write_recipe()
                self.assert_failure(self.run_cli(), "Atlas header or mip layout")
                self.recipe["atlas"][field] = original
        self.assertFalse(self.destination.exists())

    def test_existing_destination_never_overwritten_even_if_empty(self):
        self.destination.mkdir(parents=True)
        self.assert_failure(self.run_cli(), "Destination already exists")
        sentinel = self.destination / "keep"
        sentinel.write_bytes(b"do not overwrite")
        self.assert_failure(self.run_cli(), "Destination already exists")
        self.assertEqual(sentinel.read_bytes(), b"do not overwrite")

    def test_identity_reuse_is_explicit_and_read_only(self):
        first = self.run_cli()
        self.assertEqual(first.returncode, 0, first.stderr)
        before = self.bundle_bytes()
        timestamps = {path: path.stat().st_mtime_ns for path in self.destination.rglob("*")}
        self.assert_failure(self.run_cli(), "Destination already exists")
        result = self.run_cli(extra=("--reuse-identical",))
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue(json.loads(result.stdout)["reused_identical"])
        self.assertEqual(self.bundle_bytes(), before)
        self.assertEqual({path: path.stat().st_mtime_ns for path in self.destination.rglob("*")}, timestamps)

    def test_existing_destination_file_is_never_overwritten(self):
        self.destination.parent.mkdir(parents=True)
        self.destination.write_bytes(b"keep this existing file")
        self.assert_failure(self.run_cli(), "Destination already exists")
        self.assert_failure(self.run_cli(extra=("--reuse-identical",)), "Not a physical directory")
        self.assertEqual(self.destination.read_bytes(), b"keep this existing file")

    def test_reuse_rejects_changed_missing_and_extra_entries(self):
        self.assertEqual(self.run_cli().returncode, 0)
        image = self.destination / "img3d/armhnd.tga"
        before = image.read_bytes()
        image.write_bytes(b"x" + before[1:])
        self.assert_failure(self.run_cli(extra=("--reuse-identical",)), "not byte-identical: img3d/armhnd.tga")
        image.unlink()
        self.assert_failure(self.run_cli(extra=("--reuse-identical",)), "Existing output is incomplete")
        image.write_bytes(before)
        extra = self.destination / "unexpected"
        extra.mkdir()
        self.assert_failure(self.run_cli(extra=("--reuse-identical",)), "unexpected entries: unexpected")
        self.assertTrue(extra.is_dir())

    def test_source_root_and_ancestor_symlinks_are_rejected(self):
        link = self.workspace / "linked-source"
        link.symlink_to(self.source, target_is_directory=True)
        self.assert_failure(self.run_cli(source=link), "Symlinks are forbidden")
        ancestor = self.workspace / "linked-parent"
        ancestor.symlink_to(self.workspace, target_is_directory=True)
        self.assert_failure(self.run_cli(source=ancestor / "supplied-source"), "Symlinks are forbidden")
        self.assertFalse(self.destination.exists())

    def test_source_file_and_directory_symlinks_are_rejected(self):
        model = self.source / "models/plyr266.h"
        original = self.workspace / "original-model"
        model.rename(original)
        model.symlink_to(original)
        self.assert_failure(self.run_cli(), "Symlinks are forbidden")
        model.unlink()
        model.write_bytes(self.inputs["models/plyr266.h"])
        images = self.source / "img3d"
        originals = self.workspace / "original-images"
        images.rename(originals)
        images.symlink_to(originals, target_is_directory=True)
        self.assert_failure(self.run_cli(), "Symlinks are forbidden")

    def test_destination_and_ancestor_symlinks_are_rejected_without_target_mutation(self):
        self.destination.parent.mkdir(parents=True)
        target = self.workspace / "untouched"
        target.mkdir()
        sentinel = target / "keep"
        sentinel.write_bytes(b"untouched")
        self.destination.symlink_to(target, target_is_directory=True)
        self.assert_failure(self.run_cli(extra=("--reuse-identical",)), "Symlinks are forbidden")
        ancestor = self.destination.parent / "link"
        ancestor.symlink_to(target, target_is_directory=True)
        self.assert_failure(self.run_cli(ancestor / "new"), "Symlinks are forbidden")
        self.assertEqual(list(target.iterdir()), [sentinel])
        self.assertEqual(sentinel.read_bytes(), b"untouched")

    def test_reuse_rejects_internal_symlink(self):
        self.assertEqual(self.run_cli().returncode, 0)
        model = self.destination / "models/plyr266.h"
        model.unlink()
        model.symlink_to(self.source / "models/plyr266.h")
        self.assert_failure(self.run_cli(extra=("--reuse-identical",)), "Symlinks are forbidden")

    def test_destination_confinement_and_traversal(self):
        for destination in (self.workspace / "outside", self.project / "Assets/Imported/no",
                            self.destination.parent, str(self.destination.parent) + "/../escape"):
            with self.subTest(destination=str(destination)):
                self.assert_failure(self.run_cli(destination), "Destination must" if ".." not in str(destination)
                                    else "traversal")
        self.assertFalse((self.workspace / "outside").exists())
        self.assertFalse((self.project / "Assets").exists())

    def test_manifest_path_aliases_and_absolute_paths_rejected(self):
        for label in ("../escape", "/absolute", "models/../escape", "models\\escape", "C:escape",
                      "models/./escape", "models//escape"):
            with self.subTest(path=label):
                self.recipe["inputs"][0]["path"] = label
                self.write_recipe()
                result = self.run_cli()
                self.assertEqual(result.returncode, 1)
                self.assertNotIn("Traceback", result.stderr)
                self.assertFalse(self.destination.exists())

    def test_source_and_destination_overlap_rejected(self):
        inner = self.destination / "source"
        inner.mkdir(parents=True)
        self.assert_failure(self.run_cli(source=inner), "must not overlap")

    def test_authorization_reference_is_explicit_and_non_secret_label(self):
        self.assert_failure(self.run_cli(extra=("--authorization-reference", "not/a/label")),
                            "non-secret authorization reference")

    def test_help_and_required_authorization_and_no_revision_or_network_override(self):
        help_result = subprocess.run([sys.executable, "-B", str(self.script), "--help"],
                                     capture_output=True, text=True, check=False, cwd=self.project)
        self.assertEqual(help_result.returncode, 0)
        self.assertIn("--reuse-identical", help_result.stdout)
        self.assertIn("No network", help_result.stdout)
        self.assertIn("cambot.c", help_result.stdout)
        missing_flag = subprocess.run([sys.executable, "-B", str(self.script), "--source-root",
                                       str(self.source), "--destination", str(self.destination),
                                       "--authorization-reference", "synthetic"],
                                      capture_output=True, text=True, check=False, cwd=self.project)
        self.assertEqual(missing_flag.returncode, 2)
        self.assertIn("--confirm-local-use", missing_flag.stderr)
        for flag in ("--revision", "--fetch", "--manifest"):
            result = self.run_cli(extra=(flag, "unexpected"))
            self.assertEqual(result.returncode, 2)
            self.assertIn("unrecognized arguments", result.stderr)

    def test_real_decoder_channel_mutation_is_caught_in_isolated_copy(self):
        original = self.script.read_bytes()
        needle = b"red = (word >> 10) & 31"
        self.assertEqual(original.count(needle), 1)
        self.script.write_bytes(original.replace(needle, b"red = word & 31", 1))
        shutil.copyfile(TOOL_DIRECTORY / "test_prepare_player.py", self.code / "test_prepare_player.py")
        result = subprocess.run(
            [sys.executable, "-B", "-m", "unittest",
             "test_prepare_player.DecoderTests.test_every_pixel_and_tga_contract"],
            cwd=self.code, capture_output=True, text=True, check=False,
            env=dict(os.environ, PYTHONDONTWRITEBYTECODE="1"),
        )
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("FAIL: test_every_pixel_and_tga_contract", result.stderr)
        self.assertIn("AssertionError", result.stderr)
        self.assertNotIn("ImportError", result.stderr)
        self.assertEqual((TOOL_DIRECTORY / "prepare_player.py").read_bytes(), original)


class PinnedRecipeTests(unittest.TestCase):
    def test_bound_recipe_identity_and_mapping_without_reading_original_inputs(self):
        recipe = json.loads((TOOL_DIRECTORY / tool.RECIPE_NAME).read_text())
        self.assertEqual(recipe["revision"], "89f840d58d152a2a9f230cb8d0cdffbd554c8ded")
        self.assertEqual(recipe["atlas"]["header_uint32_le"], list(tool.WMS_HEADER))
        self.assertEqual((recipe["atlas"]["width"], recipe["atlas"]["height"]), (128, 256))
        self.assertEqual(recipe["atlas"]["output_sha256"],
                         "480de7c62c48d311186aea5656da99bf907a800b18ef1ec02ebe951aae92518e")
        self.assertEqual(recipe["ordered_runtime_textures"],
                         ["ARMHND.WMS", "HEDSHU.WMS", "BEAHLOD.WMS", "BEAHLOD.WMS", "BEAHLOD.WMS"])
        self.assertEqual({entry["path"] for entry in recipe["inputs"]},
                         {"models/plyr266.h", "models/skeleton.h", "img3d/armhnd.tga",
                          "img3d/hedshu.tga", "img3d/BEAHLOD.wms", "cambot.c"})
        self.assertEqual([entry["slot"] for entry in recipe["request"]["materialMap"]],
                         ["armhnd.wms", "hedshu.wms", "jersf.wms", "leghel.wms", "slvmsk.wms"])
        self.assertEqual([entry["texturePath"] for entry in recipe["request"]["materialMap"]],
                         ["img3d/armhnd.tga", "img3d/hedshu.tga"]
                         + ["img3d/BEAHLOD.level0.local-preview.tga"] * 3)
        self.assertEqual([entry["alphaCutoff"] for entry in recipe["request"]["materialMap"]],
                         [0, 0, 0.01, 0.01, 0.01])
        self.assertEqual(recipe["request"]["skeletonLimbArray"], "model266_limbs")


if __name__ == "__main__":
    unittest.main()
