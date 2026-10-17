"""Audit the actual tracked public replay and reject causal schema/identity mutations."""
import copy
import json
from pathlib import Path
import shutil
import subprocess
import sys
import unittest
import uuid

import prepare_public_replay as replay


class PublicReplayTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = replay.load_fixture(replay.FIXTURE)
        cls.rows = replay.audit_public(cls.data)

    def setUp(self):
        self.work = replay.ROOT / "tests/movement-callers/.artifacts" / ("public-input-unit-" + uuid.uuid4().hex)
        self.work.mkdir(parents=True)

    def tearDown(self):
        shutil.rmtree(self.work)

    def mutate(self, change, diagnostic):
        rows = copy.deepcopy(self.rows)
        change(rows)
        fixture = self.work / "mutated.jsonl.gz"
        fixture.write_bytes(replay.compress(replay.encode(rows)))
        with self.assertRaisesRegex(ValueError, diagnostic):
            replay.load_fixture(fixture)

    def test_actual_fixture_is_only_the_exact12000_public_commands(self):
        self.assertEqual(len(self.rows), 12000)
        self.assertEqual(len(self.data), replay.PUBLIC_BYTES)
        self.assertEqual(replay.sha(self.data), replay.PUBLIC_SHA256)
        self.assertEqual(replay.sha(replay.FIXTURE.read_bytes()), replay.FIXTURE_SHA256)
        self.assertEqual(replay.compress(self.data), replay.FIXTURE.read_bytes())
        self.assertLess(replay.FIXTURE.stat().st_size * 50, len(self.data))
        self.assertEqual({tuple(row) for row in self.rows}, {("command", "host")})
        self.assertTrue(all(set(row["command"]) == replay.COMMAND_FIELDS for row in self.rows))
        self.assertEqual(len({json.dumps(row["command"], sort_keys=True) for row in self.rows}), 7)

    def test_nonpublic_observations_are_rejected_at_every_public_boundary(self):
        for name, change in (
            ("state", lambda rows: rows[0].update(state={})),
            ("SourceRandomDraws", lambda rows: rows[0]["command"].update(SourceRandomDraws=[])),
            ("geometry", lambda rows: rows[0]["command"]["Move"].update(geometry=[])),
            ("seed", lambda rows: rows[0]["command"]["Source"].update(seed=1)),
            ("pose", lambda rows: rows[0]["command"]["Source"]["Play"].update(pose=[])),
        ):
            with self.subTest(field=name):
                self.mutate(change, name)

    def test_station_frames_cannot_hide_nonpublic_observations(self):
        def private_station(rows):
            rows[0]["command"]["StationInputs"] = {"Stations": [{
                "StationId": 0, "StickX": 0, "StickZ": 0, "Held": 0, "Pressed": 0,
                "Released": 0, "Command": copy.deepcopy(rows[0]["command"]["Source"]),
                "pose": [],
            }]}
        self.mutate(private_station, "pose")
        self.mutate(lambda rows: rows[0]["command"].update(
            StationInputs={"Stations": [], "actorState": {}}), "actorState")

    def test_missing_public_fields_and_host_reordering_are_rejected(self):
        self.mutate(lambda rows: rows[0]["command"].pop("Turbo"), "Turbo")
        self.mutate(lambda rows: rows[0].pop("host"), "host")
        self.mutate(lambda rows: rows[1].update(host=0), "contiguous host")
        self.mutate(lambda rows: rows[0].update(host=True), "contiguous host")
        self.mutate(lambda rows: rows.reverse(), "contiguous host")

    def test_missing_or_extra_host_count_is_not_a_pass(self):
        self.mutate(lambda rows: rows.pop(), "exact12000")
        self.mutate(lambda rows: rows.append(copy.deepcopy(rows[-1])), "exact12000")

    def test_valid_public_field_change_fails_immutable_identity(self):
        self.mutate(lambda rows: rows[0]["command"].update(Action=0), "command identity SHA256")
        self.mutate(lambda rows: rows[0]["command"]["Move"].update(X=1), "command identity SHA256")
        self.mutate(lambda rows: rows[0]["command"].update(Turbo=0), "must be boolean")

    def test_duplicate_json_fields_nonfinite_values_and_container_mutation_fail(self):
        duplicate = self.data.replace(b'"host":0', b'"host":0,"host":0', 1)
        with self.assertRaisesRegex(ValueError, "duplicate JSON field"):
            replay.audit_public(duplicate)
        nonfinite = self.data.replace(b'"X":0', b'"X":NaN', 1)
        with self.assertRaisesRegex(ValueError, "nonfinite JSON"):
            replay.audit_public(nonfinite)
        packed = bytearray(replay.FIXTURE.read_bytes())
        packed[4] ^= 1
        fixture = self.work / "changed-header.jsonl.gz"
        fixture.write_bytes(packed)
        with self.assertRaisesRegex(ValueError, "container SHA256"):
            replay.load_fixture(fixture)

    def test_prepare_cli_emits_actual_reader_schema_without_source_or_simulation(self):
        output = self.work / "commands.jsonl"
        command = [sys.executable, "-B", str(Path(replay.__file__)), "prepare",
                   "--fixture", str(replay.FIXTURE), "--output", str(output)]
        result = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual(report["schema"], "source-public-replay-inputs/v1")
        self.assertEqual(report["hosts"], 12000)
        self.assertFalse(report["simulation_executed"])
        self.assertEqual(output.read_bytes(), self.data)
        self.assertEqual(replay.audit_public(output.read_bytes()), self.rows)
        self.assertIn('GetProperty("host")', (replay.ROOT / "tests/movement-callers/Canonical.cs").read_text())
        self.assertIn('GetProperty("command")', (replay.ROOT / "tests/movement-callers/Canonical.cs").read_text())
        wrong = self.work / "not-reader-schema.json"
        result = subprocess.run(command[:-1] + [str(wrong)], capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("must be .jsonl", result.stderr)
        self.assertFalse(wrong.exists())

    def test_absent_corrupt_or_wrong_source_never_emits_a_fallback(self):
        output = self.work / "commands.jsonl"
        for fixture in (self.work / "missing.gz", self.work / "bad.gz"):
            if fixture.name == "bad.gz":
                fixture.write_bytes(b"not a gzip fixture")
            result = subprocess.run([sys.executable, "-B", str(Path(replay.__file__)), "prepare",
                                     "--fixture", str(fixture), "--output", str(output)],
                                    capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("Public replay preparation failed:", result.stderr)
            self.assertFalse(output.exists())
        source = self.work / "wrong-source.json"
        source.write_text('{"commands":[]}')
        with self.assertRaisesRegex(ValueError, "reviewed mixed source SHA256"):
            replay.export(source, self.work / "never-created.gz")
        self.assertFalse((self.work / "never-created.gz").exists())

    def test_existing_different_output_is_not_overwritten(self):
        output = self.work / "existing.jsonl"
        output.write_bytes(b"preserved prior evidence")
        with self.assertRaisesRegex(ValueError, "never overwrite"):
            replay.write_new_or_verify(output, self.data)
        self.assertEqual(output.read_bytes(), b"preserved prior evidence")
        with self.assertRaisesRegex(ValueError, "own ignored output"):
            replay.output_path(replay.ROOT / "Assets/unowned.jsonl")


if __name__ == "__main__":
    unittest.main()
