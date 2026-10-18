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


class PortableRecordingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.case = replay.POSITIVE_CASE
        cls.identity = replay.RECORDINGS[cls.case]
        cls.data = replay.load_fixture(replay.POSITIVE_FIXTURE, cls.case)
        cls.rows = replay.audit_public(cls.data, cls.case)

    def setUp(self):
        self.work = replay.ROOT / "tests/movement-callers/.artifacts" / ("portable-input-unit-" + uuid.uuid4().hex)
        self.work.mkdir(parents=True)

    def tearDown(self):
        shutil.rmtree(self.work)

    def prepare(self, fixture=None, recipe=None, cases=None, output=None):
        command = [sys.executable, "-B", str(Path(replay.__file__)), "prepare"]
        for case in [self.case] if cases is None else cases:
            command += ["--case", case]
        command += ["--fixture", str(replay.POSITIVE_FIXTURE if fixture is None else fixture),
                    "--recipe", str(replay.POSITIVE_RECIPE if recipe is None else recipe),
                    "--output", str(self.work / "prepared/commands.jsonl" if output is None else output)]
        return subprocess.run(command, capture_output=True, text=True)

    def rejected(self, fixture=None, recipe=None, diagnostic=None, cases=None):
        result = self.prepare(fixture, recipe, cases)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.work / "prepared").exists(), result.stdout + result.stderr)
        self.assertIn(diagnostic or "Public replay preparation failed:", result.stderr)
        self.assertNotIn("Traceback", result.stderr)

    def mutate(self, change, diagnostic, cli=False):
        rows = copy.deepcopy(self.rows)
        change(rows)
        fixture = self.work / "mutated.jsonl.gz"
        fixture.write_bytes(replay.compress(replay.encode(rows)))
        if cli:
            self.rejected(fixture=fixture, diagnostic=diagnostic)
        else:
            with self.assertRaisesRegex(ValueError, diagnostic):
                replay.load_fixture(fixture, self.case)

    def test_named_fixture_has_exact_qualified_bytes_and_minimal_bound_metadata(self):
        self.assertEqual(len(self.data), 8534249)
        self.assertEqual(replay.sha(self.data), "830e7badd16a1712553bbb9ffc96c98000e1b5422d3dbbcaa55714fd5142eb3e")
        self.assertEqual(replay.POSITIVE_FIXTURE.stat().st_size, 66078)
        self.assertEqual(replay.sha(replay.POSITIVE_FIXTURE.read_bytes()),
                         "802bb9958e14cb3a053988057ac6e4dfd03d203a761c0a9cc3c0495f9d523aa9")
        self.assertEqual(len(self.rows), 12000)
        self.assertTrue(all(len(row["command"]["StationInputs"]["Stations"]) == 1 for row in self.rows))
        metadata = replay.decode(replay.POSITIVE_RECIPE.read_text())
        self.assertEqual(metadata, replay.EXPECTED_RECIPE)
        self.assertFalse(metadata["native_equivalent"])
        self.assertTrue(metadata["commands_only"])
        self.assertEqual(metadata["source_compatibility"], "portable")
        self.assertEqual(replay.sha(replay.POSITIVE_RECIPE.read_bytes()), replay.RECIPE_SHA256)

    def test_actual_named_prepare_cli_emits_only_exact_reader_input_without_generation(self):
        result = self.prepare()
        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual(report["case"], self.case)
        self.assertEqual(report["hosts"], 12000)
        self.assertFalse(report["simulation_executed"])
        self.assertFalse(report["historical_unqualified"])
        self.assertEqual(report["public_sha256"], self.identity["public_sha256"])
        self.assertEqual(report["recipe_sha256"], replay.RECIPE_SHA256)
        self.assertEqual((self.work / "prepared/commands.jsonl").read_bytes(), self.data)

    def test_serialized_private_fields_fail_at_every_nested_public_object(self):
        changes = (
            ("state", lambda r: r.update(state={})),
            ("RNG", lambda r: r["command"].update(RNG=[])),
            ("geometry", lambda r: r["command"]["Move"].update(geometry=[])),
            ("seed", lambda r: r["command"]["Source"].update(seed=1)),
            ("pose", lambda r: r["command"]["Source"]["Play"].update(pose=[])),
            ("Model", lambda r: r["command"]["StationInputs"].update(Model={})),
            ("sound", lambda r: r["command"]["StationInputs"]["Stations"][0].update(sound=[])),
            ("draws", lambda r: r["command"]["StationInputs"]["Stations"][0]["Command"].update(draws=[])),
            ("actor", lambda r: r["command"]["StationInputs"]["Stations"][0]["Command"]["Play"].update(actor={})),
        )
        for field, change in changes:
            with self.subTest(field=field):
                self.mutate(lambda rows: change(rows[0]), field)

    def test_constructor_invalid_station_values_fail_actual_preparation_before_output(self):
        for field, value, diagnostic in (
            ("StickX", -4, "must be-3..3"), ("StickX", 4, "must be-3..3"),
            ("StickZ", -4, "must be-3..3"), ("StickZ", 4, "must be-3..3"),
            ("Held", 8, "ABC mask0..7"), ("Pressed", 8, "ABC mask0..7"),
            ("Released", -1, "ABC mask0..7"), ("Released", 8, "ABC mask0..7"),
            ("StationId", 4, "unique0..3"), ("StickX", True, "must be an int32"),
        ):
            with self.subTest(field=field, value=value):
                self.mutate(lambda rows: rows[0]["command"]["StationInputs"]["Stations"][0].update(
                    {field: value}), diagnostic, cli=True)

    def test_specified_SourceAction_bounds_fail_actual_preparation_for_both_command_paths(self):
        for station in (False, True):
            for value in (-1, 21):
                def invalid(rows):
                    command = rows[0]["command"]
                    source = command["StationInputs"]["Stations"][0]["Command"] if station else command["Source"]
                    source.update(IsSpecified=True, Action=value)
                with self.subTest(station=station, value=value):
                    self.mutate(invalid, "Action must be0..20 when specified", cli=True)

    def test_real_preparer_semantic_mutants_cannot_hide_behind_digest_rejection(self):
        source = Path(replay.__file__).read_text()
        mutations = (
            ('require(-3 <= station[name] <= 3,', "StickX", 4, "must be-3..3"),
            ('require(0 <= station[name] <= 7,', "Held", 8, "ABC mask0..7"),
            ('require(0 <= value["Action"] <= 20,', "Action", 21, "Action must be0..20"),
        )
        for guard, field, value, diagnostic in mutations:
            with self.subTest(guard=guard):
                self.assertEqual(source.count(guard), 1)
                namespace = {"__file__": replay.__file__, "__name__": "isolated_preparer_mutant"}
                exec(compile(source.replace(guard, "require(True,", 1), replay.__file__, "exec"), namespace)
                rows = copy.deepcopy(self.rows)
                station = rows[0]["command"]["StationInputs"]["Stations"][0]
                if field == "Action":
                    station["Command"].update(IsSpecified=True, Action=value)
                else:
                    station[field] = value
                fixture = self.work / "semantic-mutant.jsonl.gz"
                fixture.write_bytes(replay.compress(replay.encode(rows)))
                with self.assertRaisesRegex(ValueError, diagnostic):
                    replay.load_fixture(fixture, self.case)
                with self.assertRaisesRegex(ValueError, "command identity SHA256"):
                    namespace["load_fixture"](fixture, self.case)

    def test_station_constructor_boundary_values_are_valid_shapes_not_new_recordings(self):
        for axis, buttons, action in ((-3, 0, 0), (3, 7, 20)):
            row = copy.deepcopy(self.rows[0])
            station = row["command"]["StationInputs"]["Stations"][0]
            station.update(StickX=axis, StickZ=axis, Held=buttons, Pressed=buttons, Released=buttons)
            station["Command"].update(IsSpecified=True, Action=action)
            replay.public_row(row, 0)
        self.mutate(lambda rows: rows[0]["command"].update(Turbo=not rows[0]["command"]["Turbo"]),
                    "command identity SHA256")

    def test_public_types_station_identity_and_nonfinite_values_remain_strict(self):
        self.mutate(lambda rows: rows[0]["command"]["StationInputs"]["Stations"].append(
            copy.deepcopy(rows[0]["command"]["StationInputs"]["Stations"][0])), "unique0..3")
        self.mutate(lambda rows: rows[0]["command"]["StationInputs"].update(Stations=[{}] * 5), "at most4")
        self.mutate(lambda rows: rows[0]["command"]["StationInputs"]["Stations"][0].update(Held=2**31), "int32")
        self.mutate(lambda rows: rows[0]["command"]["Source"].update(IsSpecified=1), "must be boolean")
        for raw, diagnostic in (
            (self.data.replace(b'"host":0', b'"host":0,"host":0', 1), "duplicate JSON field"),
            (self.data.replace(b'"X":0', b'"X":NaN', 1), "nonfinite JSON"),
            (self.data.replace(b'"X":0', b'"X":1e999', 1), "must be finite"),
        ):
            with self.subTest(diagnostic=diagnostic), self.assertRaisesRegex(ValueError, diagnostic):
                replay.audit_public(raw, self.case)

    def test_actual_serialized_order_count_digest_and_gzip_mutations_are_rejected(self):
        self.mutate(lambda rows: rows[1].update(host=0), "contiguous host")
        self.mutate(lambda rows: rows.reverse(), "contiguous host")
        self.mutate(lambda rows: rows.pop(), "exact12000")
        self.mutate(lambda rows: rows.append(copy.deepcopy(rows[-1])), "exact12000")
        self.mutate(lambda rows: rows[0]["command"]["Move"].update(X=1), "command identity SHA256")
        fixture = self.work / "header.jsonl.gz"
        packed = bytearray(replay.POSITIVE_FIXTURE.read_bytes())
        packed[4] ^= 1
        fixture.write_bytes(packed)
        self.rejected(fixture=fixture, diagnostic="container SHA256")
        fixture.write_bytes(replay.POSITIVE_FIXTURE.read_bytes()[:-8])
        self.rejected(fixture=fixture)

    def test_metadata_rejects_wrong_epoch_profile_mode_identity_digest_and_provider(self):
        for field, value in (
            ("schema", "source-public-replay-recording/v2"), ("case", replay.HISTORICAL_CASE),
            ("epoch", "Fixed60Hz"), ("input_profile", "legacy"), ("c2_profile", "contact281-c2"),
            ("source_compatibility", "strict"), ("source_compatibility", "native"),
            ("native_equivalent", True), ("commands_only", False), ("hosts", 11999),
            ("seed", True), ("seed", 2), ("provider_profile", "different-provider"),
        ):
            metadata = copy.deepcopy(replay.EXPECTED_RECIPE)
            metadata[field] = value
            recipe = self.work / "wrong.recipe.json"
            recipe.write_text(json.dumps(metadata, indent=2) + "\n")
            with self.subTest(field=field, value=value):
                self.rejected(recipe=recipe, diagnostic="recipe." + field)
        for section, field, value in (
            ("jsonl", "sha256", replay.PUBLIC_SHA256), ("gzip", "sha256", replay.FIXTURE_SHA256),
            ("jsonl", "bytes", replay.PUBLIC_BYTES), ("gzip", "file", replay.FIXTURE.name),
            ("ai_opponents", "away", 5),
        ):
            metadata = copy.deepcopy(replay.EXPECTED_RECIPE)
            metadata[section][field] = value
            recipe = self.work / "wrong.recipe.json"
            recipe.write_text(json.dumps(metadata, indent=2) + "\n")
            with self.subTest(section=section, field=field):
                self.rejected(recipe=recipe, diagnostic="recipe." + section + "." + field)

    def test_metadata_unknown_private_fields_duplicates_and_noncanonical_bytes_fail(self):
        for section in (None, "jsonl", "gzip", "ai_opponents"):
            metadata = copy.deepcopy(replay.EXPECTED_RECIPE)
            target = metadata if section is None else metadata[section]
            target["privateProof"] = {"RNG": []}
            recipe = self.work / "private.recipe.json"
            recipe.write_text(json.dumps(metadata, indent=2) + "\n")
            with self.subTest(section=section):
                self.rejected(recipe=recipe, diagnostic="privateProof")
        for raw, diagnostic in (
            (replay.POSITIVE_RECIPE.read_bytes().replace(b'"seed": 1', b'"seed": 1, "seed": 1', 1),
             "duplicate JSON field"),
            (replay.POSITIVE_RECIPE.read_bytes() + b"\n", "recipe SHA256"),
        ):
            recipe = self.work / "noncanonical.recipe.json"
            recipe.write_bytes(raw)
            self.rejected(recipe=recipe, diagnostic=diagnostic)

    def test_historical_fixture_cannot_be_silently_relabelled_positive_or_native(self):
        self.rejected(fixture=replay.FIXTURE, diagnostic="command identity SHA256")
        self.rejected(cases=[replay.HISTORICAL_CASE], diagnostic="historical unqualified")
        self.rejected(cases=[], diagnostic="historical unqualified")
        self.assertEqual(replay.sha(replay.load_fixture(replay.FIXTURE)), replay.PUBLIC_SHA256)
        self.assertEqual(replay.sha(replay.FIXTURE.read_bytes()), replay.FIXTURE_SHA256)

    def test_cli_requires_one_known_explicit_case_and_never_creates_fallback_output(self):
        self.rejected(cases=["unknown"], diagnostic="invalid choice")
        self.rejected(cases=[self.case, self.case], diagnostic="duplicate --case")
        self.rejected(cases=[replay.HISTORICAL_CASE, self.case], diagnostic="duplicate --case")
        self.rejected(fixture=self.work / "missing.jsonl.gz")
        self.rejected(recipe=self.work / "missing.recipe.json")

    def test_existing_output_and_lexical_traversal_guards_are_preserved(self):
        output = self.work / "preserved.jsonl"
        output.write_bytes(b"unchanged prior input")
        result = self.prepare(output=output)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("never overwrite", result.stderr)
        self.assertEqual(output.read_bytes(), b"unchanged prior input")
        traversed = self.work / "uncreated/../escape.jsonl"
        result = self.prepare(output=traversed)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("own ignored output", result.stderr)
        self.assertFalse((self.work / "uncreated").exists())
        self.assertFalse((self.work / "escape.jsonl").exists())


if __name__ == "__main__":
    unittest.main()
