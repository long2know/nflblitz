"""Independent synthetic multiframe WANs; no upstream motion or model data."""

import contextlib
import hashlib
import importlib.util
import io
import json
import math
import os
from pathlib import Path
import struct
import subprocess
import sys
import types
import unittest
import uuid


MODULE_PATH = Path(__file__).resolve().parents[1] / "read_wan_clip.py"
SPEC = importlib.util.spec_from_file_location("tools.animation.read_wan_clip", MODULE_PATH)
clip = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(clip)
FLAGS = (0x4000, 0x4004, 0x4000, 0, 0x4010, 0, 0, 0x4000)
SELECTED = [0, 1, 2, 4, 7]
ANGLES = (0, 90, 180, 240, 300, 359, 361, 450)
IDENTITY = (1., 0., 0., 0., 1., 0., 0., 0., 1.)


def rotation_z(angle):
    c, s = math.cos(math.radians(angle)), math.sin(math.radians(angle))
    return (c, -s, 0., s, c, 0., 0., 0., 1.)


def root(frame):
    return (frame + .5, -2. * frame - 1., 3. * frame + 2.)


def chunk(tag, payload, number=13, reference=0):
    result = struct.pack(">4sIII", tag, len(payload) + 8, number, reference) + payload
    return result + b"\0" * (len(result) % 2)


def fixture(*, flags=FLAGS, sequence_flags=0x4001, limbs=15, reference=0,
            extra_record=b"", missing=None, name=b"synthetic.bip"):
    """Return bytes and independently computed absolute record/data offsets."""
    header = struct.pack("<20sII10I", name, sequence_flags, limbs, *([0] * 10))
    prefix = chunk(b"ORIG", b"independent synthetic fixture")
    count = len(flags)
    payloads = {
        -1: struct.pack(f"<{count}i", *flags),
        -2: b"".join(struct.pack("<3f", *root(frame)) for frame in range(count)),
    }
    for limb in range(1, limbs + 1):
        payloads[limb] = b"".join(
            struct.pack("<12f", limb + .25, -limb - .5, frame + 2.,
                        *(rotation_z(ANGLES[frame]) if limb == 1 else IDENTITY))
            for frame in range(count))
    # Flags need not be the first record, nor limbs be sorted.
    order = [-2, *range(limbs, 0, -1), -1]
    records, positions = [], {}
    position = 8 + len(prefix) + 16 + len(header)
    for kind in order:
        if kind == missing:
            continue
        record = struct.pack("<ii", kind, count) + payloads[kind]
        positions[kind] = {"record": position, "data": position + 8}
        records.append(record)
        position += len(record)
    body = header + b"".join(records) + extra_record + struct.pack("<i", 0)
    payload = prefix + chunk(b"3AN ", body, reference=reference)
    return b"WIFF" + struct.pack(">I", len(payload)) + payload, positions


def provenance(data):
    return {
        "repository": "synthetic/fixture", "commit": "a" * 40, "path": "generated.wan",
        "git_blob_sha1": hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest(),
    }


def decode(data, module=clip):
    return module.full_clip(data, "SYNTHETIC", provenance(data))


def matrix_from_quaternion(q):
    x, y, z, w = q
    return (
        1 - 2 * (y*y + z*z), 2 * (x*y - z*w), 2 * (x*z + y*w),
        2 * (x*y + z*w), 1 - 2 * (x*x + z*z), 2 * (y*z - x*w),
        2 * (x*z - y*w), 2 * (y*z + x*w), 1 - 2 * (x*x + y*y),
    )


def shortest_slerp(a, b, t):
    dot = sum(x * y for x, y in zip(a, b))
    if dot < 0:
        b, dot = [-v for v in b], -dot
    if dot > .9995:
        q = [x + t * (y - x) for x, y in zip(a, b)]
        n = math.sqrt(sum(v*v for v in q))
        return [v/n for v in q]
    angle = math.acos(min(1., dot))
    return [(math.sin((1-t) * angle) * x + math.sin(t * angle) * y) / math.sin(angle)
            for x, y in zip(a, b)]


class ClipTests(unittest.TestCase):
    def assert_complete_contract(self, module=clip):
        data, positions = fixture()
        result = decode(data, module)
        self.assertEqual(result["schema"], "original-wan-clip/v1")
        self.assertEqual(result["sequence"]["source_frame_count"], 8)
        self.assertEqual(result["sequence"]["selected_frame_count"], 5)
        self.assertEqual(result["sequence"]["selected_source_frame_indices"], SELECTED)
        self.assertEqual([f["source_frame_index"] for f in result["frames"]], SELECTED)
        self.assertEqual([f["exported_frame_index"] for f in result["frames"]], list(range(5)))
        for frame, source in zip(result["frames"], SELECTED):
            self.assertEqual(frame["source_frame_flags"], FLAGS[source])
            self.assertEqual(frame["root_translation"]["authoring_xyz"], list(root(source)))
            self.assertEqual(frame["root_translation"]["scaled_runtime_xyz"],
                             [v * .12898 for v in root(source)])
            self.assertEqual(len(frame["rotations_xyzw"]), 15)
            expected = [0., 0., math.sin(math.radians(ANGLES[source]) / 2),
                        math.cos(math.radians(ANGLES[source]) / 2)]
            for actual, value in zip(frame["rotations_xyzw"][0], expected):
                self.assertAlmostEqual(actual, value, places=7)
            for q in frame["rotations_xyzw"]:
                self.assertAlmostEqual(sum(v*v for v in q), 1., places=14)
        layout = {record["id"]: record for record in result["record_layout"]}
        for kind, offset in positions.items():
            self.assertEqual(layout[kind]["record_offset"], offset["record"])
            self.assertEqual(layout[kind]["data_offset"], offset["data"])
            self.assertEqual(layout[kind]["count"], 8)
            self.assertEqual(layout[kind]["stride"], 4 if kind == -1 else 12 if kind == -2 else 48)
        self.assertEqual(result["provenance"]["git_blob_sha1"], provenance(data)["git_blob_sha1"])
        self.assertEqual(result["provenance"]["sha256"], hashlib.sha256(data).hexdigest())
        self.assertEqual(result["validation"]["validated_limb_frames"], 120)
        self.assertEqual(result["validation"]["validated_source_frames"], 8)
        return result

    def test_every_selected_first_middle_last_frame_order_root_and_rotations(self):
        result = self.assert_complete_contract()
        self.assertEqual(result["sequence"]["animation_flags"], 0x4001)
        self.assertEqual(result["sequence"]["runtime_name"], "SYNTHETIC")
        self.assertEqual(result["sequence"]["chunk_number"], 13)
        self.assertEqual(result["root_translation"]["runtime_scale"], .12898)
        expected = [
            ("pelvis", None), ("torso", 0), ("right_upper_arm", 1), ("right_forearm", 2),
            ("right_hand", 3), ("left_upper_arm", 1), ("left_forearm", 5), ("left_hand", 6),
            ("head", 1), ("right_thigh", 0), ("right_calf", 9), ("right_foot", 10),
            ("left_thigh", 0), ("left_calf", 12), ("left_foot", 13),
        ]
        self.assertEqual([(j["name"], j["parent_index"]) for j in result["joints"]], expected)
        self.assertEqual([j["source_limb_id"] for j in result["joints"]], list(range(1, 16)))
        self.assertTrue(all(value is None for key, value in result["playback"].items()
                            if key != "status"))
        self.assertEqual(result["loop_boundary"]["first_source_frame_index"], 0)
        self.assertEqual(result["loop_boundary"]["last_source_frame_index"], 7)
        self.assertAlmostEqual(result["loop_boundary"]["last_to_first_quaternion_dots"][0],
                               -math.sqrt(.5))

    def test_every_output_matrix_and_source_byte_boundaries_independently(self):
        data, positions = fixture(flags=(0x4000,) * 8)
        result = decode(data)
        for frame in result["frames"]:
            source = frame["source_frame_index"]
            for index, q in enumerate(frame["rotations_xyzw"]):
                original = struct.unpack_from("<12f", data, positions[index + 1]["data"] + source * 48)
                for actual, value in zip(matrix_from_quaternion(q), original[3:]):
                    self.assertAlmostEqual(actual, value, places=6)
        self.assertGreaterEqual(result["validation"]["min_adjacent_source_quaternion_dot"], 0.)

    def test_first_selected_matches_unchanged_legacy_reader_exactly(self):
        data, _ = fixture(flags=(0, 0, 0, 0x4000, 0x4000, 0, 0, 0x4000))
        result = decode(data)
        static = clip.wan.first_pose(data, "synthetic", provenance(data))
        first = result["frames"][0]
        self.assertEqual(first["source_frame_index"], 3)
        self.assertEqual(first["rotations_xyzw"], [j["quaternion_xyzw"] for j in static["joints"]])
        self.assertEqual(first["root_translation"]["authoring_xyz"],
                         static["root_translation"]["authoring_xyz"])
        self.assertEqual(first["root_translation"]["scaled_runtime_xyz"],
                         static["root_translation"]["scaled_runtime_xyz"])

    def test_identity_90_180_and_loop_seam_are_correct_interpolation_inputs(self):
        result = decode(fixture()[0])
        q0, q90, q180 = [f["rotations_xyzw"][0] for f in result["frames"][:3]]
        for actual, value in zip(shortest_slerp(q0, q180, .5), q90):
            self.assertAlmostEqual(actual, value, places=7)
        for actual, value in zip(shortest_slerp(q90, [-v for v in q90], .5), q90):
            self.assertAlmostEqual(actual, value, places=7)
        seam = shortest_slerp(result["frames"][-1]["rotations_xyzw"][0], q0, .5)
        expected_matrix = rotation_z(45)
        for actual, value in zip(matrix_from_quaternion(seam), expected_matrix):
            self.assertAlmostEqual(actual, value, places=7)

    def test_deselected_frames_and_frames_after_last_export_are_validated(self):
        good, positions = fixture(flags=(0x4000, 0, 0, 0, 0, 0, 0, 0))
        corruptions = [
            (positions[-2]["data"] + 7 * 12, math.nan, "root"),
            (positions[15]["data"] + 7 * 48, math.inf, "authoring limb offset"),
            (positions[15]["data"] + 7 * 48 + 12, math.inf, "non-finite rotation"),
            (positions[15]["data"] + 7 * 48 + 12, 2., "proper orthonormal"),
            (positions[15]["data"] + 7 * 48 + 12, -1., "proper orthonormal"),
        ]
        for position, value, message in corruptions:
            data = bytearray(good)
            struct.pack_into("<f", data, position, value)
            with self.subTest(position=position, value=value):
                with self.assertRaisesRegex(clip.wan.WanError, message):
                    decode(data)

    def test_records_counts_selection_references_duplicates_and_names(self):
        cases = [
            fixture(limbs=14)[0], fixture(limbs=16)[0], fixture(reference=1)[0],
            fixture(sequence_flags=0x4002)[0], fixture(sequence_flags=1)[0],
            fixture(flags=(0,) * 8)[0], fixture(missing=-2)[0], fixture(missing=7)[0],
            fixture(name=b"\xff.bip")[0], fixture(extra_record=struct.pack("<ii", -3, 8))[0],
            fixture(extra_record=struct.pack("<ii", -1, 8) + b"\0" * 32)[0],
        ]
        good, positions = fixture()
        for kind, count in ((-1, 0), (-1, -1), (-1, 1025), (-1, 2**31-1), (8, 7), (8, 9)):
            data = bytearray(good)
            struct.pack_into("<i", data, positions[kind]["record"] + 4, count)
            cases.append(data)
        for data in cases:
            with self.subTest(length=len(data)), self.assertRaises(clip.wan.WanError):
                decode(data)
        with self.assertRaisesRegex(clip.wan.WanError, "found 0"):
            clip.full_clip(good, "ABSENT", provenance(good))
        sequence_offset = 8 + len(chunk(b"ORIG", b"independent synthetic fixture"))
        payload = good[sequence_offset:] * 2
        duplicate = b"WIFF" + struct.pack(">I", len(payload)) + payload
        with self.assertRaisesRegex(clip.wan.WanError, "found 2"):
            decode(duplicate)

    def test_container_truncation_lengths_padding_and_terminator(self):
        good, _ = fixture()
        cases = [b"", good[:7], b"RIFF" + good[4:], good[:-1], good + b"\0"]
        for length in (0, len(good) - 9, len(good) - 7, 2**32 - 1):
            cases.append(good[:4] + struct.pack(">I", length) + good[8:])
        cases.append(b"WIFF" + struct.pack(">I", 16) + struct.pack(">4sIII", b"3AN ", 4, 1, 0))
        data = bytearray(good)
        struct.pack_into("<i", data, len(data) - 4, 3)
        cases.append(data)
        for data in cases:
            with self.subTest(prefix=data[:20]), self.assertRaises(clip.wan.WanError):
                decode(data)

    def test_corrupt_source_pins_and_metadata_never_decode(self):
        data, _ = fixture()
        for key, value in (
                ("git_blob_sha1", "0" * 40), ("git_blob_sha1", "bad"),
                ("commit", "ABCDEF" * 7), ("repository", "missing-owner"),
                ("path", "../source.wan"), ("path", "/source.wan"), ("path", ""), ("path", ".")):
            pin = provenance(data)
            pin[key] = value
            with self.subTest(key=key), self.assertRaises(clip.wan.WanError):
                clip.full_clip(data, "synthetic", pin)
        changed = data[:-1] + bytes([data[-1] ^ 1])
        with self.assertRaisesRegex(clip.wan.WanError, "does not match"):
            clip.full_clip(changed, "synthetic", provenance(data))

    def test_compact_output_is_deterministic_finite_and_preserves_all_frames(self):
        first = clip.encode_clip(decode(fixture()[0]))
        self.assertEqual(first, clip.encode_clip(decode(fixture()[0])))
        self.assertEqual(first.count("\n"), 1)
        self.assertNotIn("NaN", first)
        self.assertNotIn("Infinity", first)
        self.assertEqual([f["source_frame_index"] for f in json.loads(first)["frames"]], SELECTED)

    def test_isolated_real_decoder_mutations_are_rejected_by_artifact_assertions(self):
        source_bytes = MODULE_PATH.read_bytes()
        source = source_bytes.decode()
        mutations = {
            "dropped-middle-frame": ('"frames": frames,', '"frames": frames[:2] + frames[3:],'),
            "wrong-frame-order": ('"frames": frames,', '"frames": list(reversed(frames)),'),
            "wrong-hemisphere": ("zip(previous, q)) < 0:", "zip(previous, q)) < -2:"),
            "wrong-root-scale": (
                "[value * wan.TRANSLATION_SCALE for value in translation]",
                "[value for value in translation]"),
        }
        self.assert_complete_contract()
        for name, (old, new) in mutations.items():
            with self.subTest(mutation=name):
                self.assertEqual(source.count(old), 1)
                mutant = types.ModuleType("tools.animation.mutant_wan_clip")
                mutant.__file__ = str(MODULE_PATH)
                mutant.__package__ = "tools.animation"
                exec(compile(source.replace(old, new), str(MODULE_PATH), "exec"), mutant.__dict__)
                with self.assertRaises(AssertionError):
                    self.assert_complete_contract(mutant)
        self.assertEqual(MODULE_PATH.read_bytes(), source_bytes)


class CliTests(unittest.TestCase):
    def setUp(self):
        self.directory = clip.wan.SCRATCH / ("animation-clip-test-" + uuid.uuid4().hex)
        self.directory.mkdir(parents=True)
        self.input, self.output = self.directory / "synthetic.wan", self.directory / "clip.json"
        self.data = fixture()[0]
        self.input.write_bytes(self.data)
        self.args = [
            str(self.input), "--sequence", "synthetic", "--expected-blob",
            provenance(self.data)["git_blob_sha1"], "--source-repository", "synthetic/fixture",
            "--source-commit", "a" * 40, "--source-path", "generated.wan",
            "--output", str(self.output),
        ]

    def tearDown(self):
        for path in self.directory.iterdir():
            path.unlink()
        self.directory.rmdir()

    def run_cli(self, args):
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            return clip.main(args)

    def test_actual_offline_cli_emits_full_inspectable_artifact_deterministically(self):
        process = subprocess.run([sys.executable, "-B", str(MODULE_PATH), *self.args],
                                 capture_output=True, text=True, check=False)
        self.assertEqual(process.returncode, 0, process.stderr)
        first = self.output.read_bytes()
        result = json.loads(first)
        self.assertEqual([f["source_frame_index"] for f in result["frames"]], SELECTED)
        self.assertEqual(result["provenance"]["path"], "generated.wan")
        self.assertEqual(len(result["frames"][-1]["rotations_xyzw"]), 15)
        self.assertEqual(self.run_cli(self.args), 0)
        self.assertEqual(self.output.read_bytes(), first)

    def test_library_can_be_loaded_by_path_outside_the_repository_import_path(self):
        program = (
            "import importlib.util; "
            f"s=importlib.util.spec_from_file_location('standalone_wan_clip',{str(MODULE_PATH)!r}); "
            "m=importlib.util.module_from_spec(s); s.loader.exec_module(m); "
            "assert callable(m.full_clip); assert callable(m.encode_clip); "
            "print(m.wan.TRANSLATION_SCALE)"
        )
        process = subprocess.run([sys.executable, "-B", "-c", program], cwd=self.directory,
                                 capture_output=True, text=True, check=False)
        self.assertEqual(process.returncode, 0, process.stderr)
        self.assertEqual(process.stdout.strip(), "0.12898")

    def test_cli_rejects_corrupt_pins_and_output_escape_without_altering_input(self):
        for key, value in (
                ("--expected-blob", "0" * 40), ("--sequence", "absent"),
                ("--source-commit", "bad"), ("--source-path", "../source.wan"),
                ("--output", str(MODULE_PATH.parent / "must-not-exist.json")),
                ("--output", str(self.input)), ("--output", str(clip.wan.SCRATCH))):
            args = self.args[:]
            args[args.index(key) + 1] = value
            with self.subTest(key=key):
                self.assertEqual(self.run_cli(args), 2)
                self.assertFalse(self.output.exists())
                self.assertEqual(self.input.read_bytes(), self.data)

    def test_cli_rejects_symlink_and_hardlink_alias_to_input(self):
        alias = self.directory / "alias.json"
        alias.symlink_to(self.input)
        args = self.args[:]
        args[-1] = str(alias)
        self.assertEqual(self.run_cli(args), 2)
        alias.unlink()
        os.link(self.input, alias)
        self.assertEqual(self.run_cli(args), 2)
        self.assertEqual(self.input.read_bytes(), self.data)


if __name__ == "__main__":
    unittest.main()
