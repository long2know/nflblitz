"""Original, generated-in-memory fixtures. No upstream animation samples."""

import contextlib
import importlib.util
import io
import json
import math
from pathlib import Path
import struct
import unittest
import uuid


MODULE_PATH = Path(__file__).resolve().parents[1] / "read_wan_pose.py"
SPEC = importlib.util.spec_from_file_location("read_wan_pose", MODULE_PATH)
wan = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(wan)
IDENTITY = (1., 0., 0., 0., 1., 0., 0., 0., 1.)
TURN_Z = (0., -1., 0., 1., 0., 0., 0., 0., 1.)


def chunk(tag, body, number=1, reference=0):
    payload = struct.pack(">II", number, reference) + body
    return tag + struct.pack(">I", len(payload)) + payload + b"\0" * (len(payload) % 2)


def container(*chunks):
    payload = b"".join(chunks)
    return b"WIFF" + struct.pack(">I", len(payload)) + payload


def fixture(*, flags=(0x4000, 0x4000), name=b"synthetic.bip", limbs=15,
            sequence_flags=0x4001, extra_record=b"", missing=None, reference=0,
            matrix=TURN_Z, translation=(2., -3., 5.)):
    count = len(flags)
    header = struct.pack("<20sII10I", name, sequence_flags, limbs, *([0] * 10))
    records = [
        (-1, struct.pack("<ii", -1, count) + struct.pack(f"<{count}i", *flags)),
        (-2, struct.pack("<ii", -2, count) + struct.pack("<3f", *translation) * count),
    ]
    for limb in range(1, limbs + 1):
        # Deliberately nonzero evidence offsets must not become runtime joint translations.
        frame = struct.pack("<12f", 7., 11., -13., *(matrix if limb == 1 else IDENTITY))
        records.append((limb, struct.pack("<ii", limb, count) + frame * count))
    body = header + b"".join(record for kind, record in records if kind != missing)
    return container(
        chunk(b"ORIG", b"generated fixture"),
        chunk(b"3AN ", body + extra_record + struct.pack("<i", 0), 9, reference),
    )


def pose(data, sequence="SYNTHETIC"):
    return wan.first_pose(data, sequence, {
        "repository": "synthetic/fixture", "commit": "a" * 40,
        "path": "generated.wan", "git_blob_sha1": wan.git_blob_sha1(data),
    })


class ReaderTests(unittest.TestCase):
    def test_pose_artifact_mapping_rotation_translation_and_provenance(self):
        data = fixture()
        result = pose(data)
        self.assertEqual(result["schema"], "original-wan-first-pose/v1")
        self.assertEqual(result["provenance"]["git_blob_sha1"], wan.git_blob_sha1(data))
        self.assertEqual(result["provenance"]["byte_length"], len(data))
        self.assertTrue(result["provenance"]["content_pin_verified"])
        self.assertEqual(result["sequence"]["runtime_name"], "SYNTHETIC")
        self.assertEqual(result["sequence"]["chunk_number"], 9)
        self.assertEqual(result["sequence"]["source_frame_count"], 2)
        self.assertEqual(result["sequence"]["source_frame_index"], 0)
        self.assertEqual(result["sequence"]["exported_frame_index"], 0)
        expected = [
            ("pelvis", None), ("torso", 0),
            ("right_upper_arm", 1), ("right_forearm", 2), ("right_hand", 3),
            ("left_upper_arm", 1), ("left_forearm", 5), ("left_hand", 6),
            ("head", 1), ("right_thigh", 0), ("right_calf", 9), ("right_foot", 10),
            ("left_thigh", 0), ("left_calf", 12), ("left_foot", 13),
        ]
        self.assertEqual([(j["name"], j["parent_index"]) for j in result["joints"]], expected)
        for index, joint in enumerate(result["joints"]):
            self.assertEqual(joint["source_limb_id"], index + 1)
            self.assertEqual(joint["fbplyr_skel_index"], index)
            self.assertEqual(joint["authoring_offset_ignored_by_converter"], [7., 11., -13.])
            self.assertAlmostEqual(sum(v * v for v in joint["quaternion_xyzw"]), 1.)
            if index:
                self.assertEqual(joint["quaternion_xyzw"], [0., 0., 0., 1.])
        root_q = result["joints"][0]["quaternion_xyzw"]
        for actual, expected_value in zip(root_q, [0., 0., math.sqrt(.5), math.sqrt(.5)]):
            self.assertAlmostEqual(actual, expected_value)
        self.assertEqual(result["root_translation"]["authoring_xyz"], [2., -3., 5.])
        self.assertEqual(result["root_translation"]["scaled_runtime_xyz"],
                         [2 * .12898, -3 * .12898, 5 * .12898])
        record = result["record_layout"][2]
        self.assertEqual(record["id"], 1)
        self.assertEqual(struct.unpack_from("<12f", data, record["data_offset"])[3:], TURN_Z)
        self.assertEqual(result["joints"][0]["source_frame_byte_offset"], record["data_offset"])
        self.assertEqual(result["validation"]["max_matrix_determinant_error"], 0.)

    def test_first_exported_frame_is_not_necessarily_source_frame_zero(self):
        result = pose(fixture(flags=(0, 0x4000)))
        self.assertEqual(result["sequence"]["source_frame_index"], 1)
        self.assertEqual(result["sequence"]["selected_frame_count"], 1)
        self.assertEqual(result["sequence"]["exported_frame_index"], 0)
        record = next(r for r in result["record_layout"] if r["id"] == 1)
        self.assertEqual(result["joints"][0]["source_frame_byte_offset"],
                         record["data_offset"] + 48)

    def test_quaternion_hemisphere_continuity_across_deselected_frame(self):
        def rotation_z(degrees):
            angle = math.radians(degrees)
            c, s = math.cos(angle), math.sin(angle)
            return (c, -s, 0., s, c, 0., 0., 0., 1.)
        data = bytearray(fixture(flags=(0, 0x4000), matrix=rotation_z(179)))
        result = pose(data)
        record = next(r for r in result["record_layout"] if r["id"] == 1)
        struct.pack_into("<9f", data, record["data_offset"] + 48 + 12, *rotation_z(-179))
        result = pose(data)
        q = result["joints"][0]["quaternion_xyzw"]
        self.assertGreater(q[2], 0.)
        self.assertLess(q[3], 0.)
        self.assertAlmostEqual(sum(v * v for v in q), 1.)

    def test_quaternion_all_branches_and_rotation_direction(self):
        matrices = [
            (IDENTITY, [0., 0., 0., 1.]),
            ((1., 0., 0., 0., -1., 0., 0., 0., -1.), [1., 0., 0., 0.]),
            ((-1., 0., 0., 0., 1., 0., 0., 0., -1.), [0., 1., 0., 0.]),
            ((-1., 0., 0., 0., -1., 0., 0., 0., 1.), [0., 0., 1., 0.]),
            (TURN_Z, [0., 0., math.sqrt(.5), math.sqrt(.5)]),
            ((0., 1., 0., -1., 0., 0., 0., 0., 1.),
             [0., 0., -math.sqrt(.5), math.sqrt(.5)]),
        ]
        for matrix, expected in matrices:
            with self.subTest(matrix=matrix):
                for actual, value in zip(wan.matrix_to_quaternion(matrix), expected):
                    self.assertAlmostEqual(actual, value)

    def test_small_float_roundoff_is_normalized(self):
        matrix = tuple(value * 1.000001 for value in TURN_Z)
        q = wan.matrix_to_quaternion(matrix)
        self.assertAlmostEqual(sum(value * value for value in q), 1., places=14)

    def test_proper_rotation_and_finite_pose_are_required(self):
        bad_matrices = [
            (0.,) * 9,
            (1., 0., 0., 0., 1., 0., 0., 0., -1.),
            (2., 0., 0., 0., 1., 0., 0., 0., 1.),
            (math.nan,) + IDENTITY[1:],
            (math.inf,) + IDENTITY[1:],
        ]
        for matrix in bad_matrices:
            with self.subTest(matrix=matrix), self.assertRaises(wan.WanError):
                pose(fixture(matrix=matrix))
        with self.assertRaisesRegex(wan.WanError, "non-finite root"):
            pose(fixture(translation=(0., math.nan, 0.)))
        data = bytearray(fixture())
        first = pose(data)["joints"][0]["source_frame_byte_offset"]
        struct.pack_into("<f", data, first, math.inf)
        with self.assertRaisesRegex(wan.WanError, "non-finite authoring"):
            pose(data)

    def test_container_lengths_padding_and_chunk_bounds(self):
        good = fixture()
        mutations = [b"", good[:7], b"RIFF" + good[4:], good + b"\0", good[:-1]]
        for declared in (0, len(good) - 9, len(good) - 7, 2 ** 32 - 1):
            mutations.append(good[:4] + struct.pack(">I", declared) + good[8:])
        mutations.append(b"WIFF" + struct.pack(">I", 16) + b"3AN " + struct.pack(">III", 4, 1, 0))
        mutations.append(container(b"3AN " + struct.pack(">III", 99999, 1, 0)))
        mutations.append(container(chunk(b"ORIG", b"x")[:-1]))
        for data in mutations:
            with self.subTest(prefix=data[:20]), self.assertRaises(wan.WanError):
                pose(data)

    def test_unsupported_sequence_shape_and_selection(self):
        cases = [
            fixture(limbs=14), fixture(limbs=16), fixture(sequence_flags=0x4002),
            fixture(sequence_flags=1), fixture(flags=(0, 0)), fixture(reference=1),
            fixture(missing=5), fixture(extra_record=struct.pack("<ii", -3, 2)),
            fixture(extra_record=struct.pack("<ii", 16, 2)),
            fixture(extra_record=struct.pack("<ii", -1, 2) + b"\0" * 8),
        ]
        for data in cases:
            with self.subTest(length=len(data)), self.assertRaises(wan.WanError):
                pose(data)
        with self.assertRaisesRegex(wan.WanError, "found 0"):
            pose(fixture(), "absent")
        with self.assertRaisesRegex(wan.WanError, "found 2"):
            data = fixture()
            _, _, _, offset, _, _ = list(wan._chunks(data))[-1]
            pose(container(data[offset:], data[offset:]))
        with self.assertRaisesRegex(wan.WanError, "truncated"):
            pose(container(chunk(b"3AN ", b"\0" * 20)))

    def test_bad_counts_truncated_records_and_unknown_names(self):
        original = fixture()
        flags_record = pose(original)["record_layout"][0]["record_offset"]
        for count in (-1, 0, 3, 1025, 2 ** 31 - 1):
            data = bytearray(original)
            struct.pack_into("<i", data, flags_record + 4, count)
            with self.subTest(count=count), self.assertRaises(wan.WanError):
                pose(data)
        data = bytearray(original)
        root_record = pose(data)["record_layout"][1]["record_offset"]
        struct.pack_into("<i", data, root_record + 4, 1)
        with self.assertRaisesRegex(wan.WanError, "inconsistent"):
            pose(data)
        with self.assertRaisesRegex(wan.WanError, "non-ASCII"):
            pose(fixture(name=b"\xff.bip"))
        data = bytearray(original)
        struct.pack_into("<i", data, len(data) - 4, 1)
        with self.assertRaises(wan.WanError):
            pose(data)

    def test_hash_pin_and_json_determinism(self):
        data = fixture()
        with self.assertRaisesRegex(wan.WanError, "does not match"):
            wan.first_pose(data, "synthetic", {"git_blob_sha1": "0" * 40})
        with self.assertRaisesRegex(wan.WanError, "expected Git"):
            wan.first_pose(data, "synthetic", {})
        first, second = wan.encode_pose(pose(data)), wan.encode_pose(pose(data))
        self.assertEqual(first, second)
        self.assertEqual(json.loads(first)["sequence"]["source_frame_index"], 0)
        self.assertNotIn("NaN", first)
        self.assertNotIn("Infinity", first)


class CliTests(unittest.TestCase):
    def setUp(self):
        self.directory = wan.SCRATCH / ("animation-reader-test-" + uuid.uuid4().hex)
        self.directory.mkdir(parents=True)
        self.input = self.directory / "synthetic.wan"
        self.output = self.directory / "pose.json"
        self.data = fixture()
        self.input.write_bytes(self.data)
        self.args = [
            str(self.input), "--sequence", "synthetic", "--expected-blob", wan.git_blob_sha1(self.data),
            "--source-repository", "synthetic/fixture", "--source-commit", "a" * 40,
            "--source-path", "generated.wan", "--output", str(self.output),
        ]

    def tearDown(self):
        for path in self.directory.iterdir():
            path.unlink()
        self.directory.rmdir()

    def run_cli(self, args):
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            return wan.main(args)

    def test_cli_emits_inspectable_deterministic_local_artifact(self):
        self.assertEqual(self.run_cli(self.args), 0)
        first = self.output.read_bytes()
        artifact = json.loads(first)
        self.assertEqual(artifact["provenance"]["path"], "generated.wan")
        self.assertEqual(len(artifact["joints"]), 15)
        self.assertAlmostEqual(artifact["joints"][0]["quaternion_xyzw"][2], math.sqrt(.5))
        self.assertEqual(self.run_cli(self.args), 0)
        self.assertEqual(self.output.read_bytes(), first)

    def test_cli_failure_never_emits_pose(self):
        cases = [
            ("--expected-blob", "0" * 40),
            ("--sequence", "missing"),
            ("--source-commit", "not-a-commit"),
            ("--output", str(MODULE_PATH.parent / "must-not-be-written.json")),
            ("--output", str(self.input)),
        ]
        for key, value in cases:
            args = self.args[:]
            args[args.index(key) + 1] = value
            with self.subTest(key=key, value=value):
                self.assertEqual(self.run_cli(args), 2)
                self.assertFalse(self.output.exists())
                self.assertEqual(self.input.read_bytes(), self.data)


if __name__ == "__main__":
    unittest.main()
