"""Failure-gate tests only; these are not original gameplay parity evidence."""
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import unittest
from unittest import mock
import uuid

sys.dont_write_bytecode = True
import support
import oracle

SPEC = importlib.util.spec_from_file_location("playbook_runner", Path(__file__).with_name("run.py"))
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class PlaybookRunnerGuards(unittest.TestCase):
    def setUp(self):
        self.directory = support.ARTIFACTS / ("guard-" + uuid.uuid4().hex)
        self.directory.mkdir(parents=True)
        self.addCleanup(shutil.rmtree, self.directory)

    def baseline(self):
        return dict(support.PINS["expected"], schema="source-playbook-checks/v1", status="passed", passed=True,
                    scope="all", full_gameplay_parity_claimed=False,
                    observed_entry_opcodes=[1, 2, 3, 4, 5, 6, 7, 8, 9, 13, 14, 17],
                    observed_scripts=["c_jmpint_anim", "c_no_vel1_anim", "juke_c_anim", "spin_anim"])

    def mutation(self, name):
        mutation = next(value for value in runner.MUTATIONS if value[0] == name)
        summary = {
            "schema": "source-playbook-checks/v1", "status": "failed", "passed": False, "assertions": 1,
            "context": mutation[2], "scope": "quick", "full_gameplay_parity_claimed": False,
            "failed_assertion": mutation[3], "failure_kind": "assertion", "exception_type": "Checks+AssertionFailure",
        }
        if name == "route-opcode":
            summary.update(assertions=0, failed_assertion=None, failure_kind="error",
                           exception_type="System.FormatException", error="pr_goto_endzone: unknown opcode 999")
        return mutation, summary

    def test_exact_original_matrix_inventory_is_required(self):
        runner.require_baseline(0, self.baseline())
        for field, count in support.PINS["expected"].items():
            for value in (0, count - 1, count + 1, True, None):
                summary = self.baseline()
                summary[field] = value
                with self.subTest(field=field, value=value), self.assertRaises(support.GateError):
                    runner.require_baseline(0, summary)

    def test_status_only_quick_scope_crash_or_parity_claim_cannot_pass(self):
        for summary in (None, {}, {"status": "passed"}):
            with self.subTest(summary=summary), self.assertRaises(support.GateError):
                runner.require_baseline(0, summary)
        for field, value in (("scope", "quick"), ("status", "error"), ("full_gameplay_parity_claimed", True),
                             ("assertions", 0), ("assertions", True), ("original_rng_calls", 0),
                             ("command_retained_ticks", 0), ("buttons_retained_ticks", 0),
                             ("cross_actor_effects", 0), ("observed_entry_opcodes", []), ("observed_scripts", [])):
            summary = self.baseline()
            summary[field] = value
            with self.subTest(field=field), self.assertRaises(support.GateError):
                runner.require_baseline(0, summary)
        with self.assertRaises(support.GateError):
            runner.require_baseline(1, self.baseline())

    def test_only_exact_revision_tree_release_profile_and_recipe_pins_are_supported(self):
        recipe = json.loads((support.ROOT / "tools/playbooks/recipe.json").read_text())
        support.require_recipe(recipe)
        for field, value in (("source_revision", "0" * 40), ("source_tree", "0" * 40),
                             ("profile", dict(recipe["profile"], use_old_qb=True)),
                             ("profile", dict(recipe["profile"], ticks_per_second=60))):
            changed = dict(recipe)
            changed[field] = value
            with self.subTest(field=field, value=value), self.assertRaises(support.GateError):
                support.require_recipe(changed)
        recipe["inputs"]["selection"]["blob_sha1"] = "0" * 40
        with self.assertRaisesRegex(support.GateError, "input pins"):
            support.require_recipe(recipe)

    def test_public_pins_agree_with_existing_source_oracles_and_producer(self):
        original = json.loads((support.ROOT / "tests/source-oracles/pins.json").read_text())
        self.assertEqual(original["revision"], support.PINS["revision"])
        self.assertEqual(original["tree"], support.PINS["tree"])
        for name in original["blobs"].keys() & support.PINS["blobs"].keys():
            self.assertEqual(original["blobs"][name], support.PINS["blobs"][name], name)
        recipe = json.loads((support.ROOT / "tools/playbooks/recipe.json").read_text())
        for item in recipe["inputs"].values():
            self.assertEqual(item["blob_sha1"], support.PINS["blobs"][item["path"]])
        self.assertEqual(14, len(support.PINS["blobs"]))
        self.assertEqual(226, sum(item["count"] for item in recipe["weighted_pools"].values()))

    def test_actual_source_snapshot_is_bounded_to_complete_contracts_six_pure_helpers_and_producer(self):
        contents, hashes = support.snapshot()
        expected = {"Assets/Scripts/Contracts/" + name for name in support.CONTRACTS}
        expected.update("Assets/Scripts/Gameplay/" + name for name in support.GAMEPLAY)
        expected.update("tools/playbooks/" + name for name in support.PRODUCER)
        self.assertEqual(expected, set(contents))
        self.assertEqual(expected, set(hashes))
        self.assertEqual(hashes, support.live_hashes())
        self.assertEqual(9, len(support.CONTRACTS))
        self.assertEqual(15, sum(name.endswith(".cs") for name in contents))
        for name in ("SourceFireSpfxContracts.cs", "SourceKickMeterContracts.cs"):
            relative = "Assets/Scripts/Contracts/" + name
            self.assertEqual(contents[relative], (support.ROOT / relative).read_bytes())
            self.assertEqual(hashes[relative], support.sha256(contents[relative]))
        runner_source = (support.SUITE / "run.py").read_text()
        self.assertIn('"profile": "Actual complete nine Contracts +', runner_source)
        self.assertNotIn('"profile": "Actual complete seven Contracts +', runner_source)
        self.assertFalse(any("GameSimulation" in name or "Movement" in name for name in contents))
        for name in ("model/PurePlaybooks.csproj", "PlaybookChecks.csproj"):
            project = (support.SUITE / name).read_text()
            self.assertIn("<LangVersion>7.3</LangVersion>", project)
            self.assertIn("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", project)
            self.assertNotIn("Gameplay/**/*.cs", project)
            ignored = subprocess.run(
                ["git", "check-ignore", "--no-index", "--quiet", str(support.SUITE / name)],
                cwd=support.ROOT, capture_output=True, text=True, check=False)
            self.assertEqual(1, ignored.returncode, "Permanent project must be publishable, not Unity-generated/ignored: " + name)
        self.assertIn("<TargetFramework>netstandard2.1</TargetFramework>",
                      (support.SUITE / "model/PurePlaybooks.csproj").read_text())
        self.assertIn('Assets/Scripts/Contracts/**/*.cs',
                      (support.SUITE / "model/PurePlaybooks.csproj").read_text())

    def test_live_source_hash_guard_rejects_changed_actual_copied_source(self):
        contents, before = support.snapshot()
        contracts = {name for name in contents if name.startswith("Assets/Scripts/Contracts/")}
        self.assertEqual(9, len(contracts))
        self.assertIn("Assets/Scripts/Contracts/SourceContactContracts.cs", contracts)
        self.assertEqual({"Assets/Scripts/Contracts/" + name for name in support.CONTRACTS}, contracts)
        for name, data in contents.items():
            path = self.directory / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        support.require_unchanged(before, self.directory)
        for name in ("SourceContactContracts.cs", "SourceFireSpfxContracts.cs", "SourceKickMeterContracts.cs"):
            relative = "Assets/Scripts/Contracts/" + name
            contract = self.directory / relative
            contract.unlink()
            with self.subTest(missing=name), self.assertRaisesRegex(support.GateError, "missing=.*" + name):
                support.source_paths(self.directory)
            contract.write_bytes(contents[relative])
            renamed = contract.with_name("Unqualified" + contract.stem + "Dto.cs")
            contract.rename(renamed)
            with self.subTest(renamed=name), self.assertRaisesRegex(
                    support.GateError, "missing=.*" + name + ".*unexpected=.*" + renamed.name):
                support.source_paths(self.directory)
            renamed.rename(contract)
        contact = self.directory / "Assets/Scripts/Contracts/SourceContactContracts.cs"
        extra = contact.parent / "UnexpectedContract.cs"
        extra.write_text("// Owned negative inventory control, never compiled as a production DTO.\n")
        with self.assertRaisesRegex(support.GateError, "unexpected=.*UnexpectedContract.cs"):
            support.source_paths(self.directory)
        extra.unlink()
        support.require_unchanged(before, self.directory)
        target = self.directory / "Assets/Scripts/Gameplay/SourceDroneAI.cs"
        original = target.read_text()
        self.assertEqual(1, original.count(runner.DRONE_ANCHOR))
        target.write_text(original.replace(runner.DRONE_ANCHOR, runner.DRONE_REPLACEMENT))
        with self.assertRaisesRegex(support.GateError, "LiveSources"):
            support.require_unchanged(before, self.directory)
        with self.assertRaisesRegex(support.GateError, "Frozen actual-source SHA256"):
            support.snapshot(self.directory)
        self.assertEqual(before, support.live_hashes())

    def test_missing_wrong_hash_and_escaping_original_inputs_fail_closed(self):
        data = b"explicitly synthetic input, not original source\n"
        with mock.patch.dict(support.PINS["blobs"], {"fixture.c": support.blob_sha1(data)}, clear=True):
            with self.assertRaisesRegex(support.GateError, "Missing"):
                support.verify_inputs(self.directory)
            path = self.directory / "fixture.c"
            path.write_bytes(data)
            self.assertEqual({"fixture.c": data}, support.verify_inputs(self.directory))
            path.write_bytes(data + b"changed")
            with self.assertRaisesRegex(support.GateError, "hash mismatch"):
                support.verify_inputs(self.directory)
            path.unlink()
            outside = self.directory / "other.c"
            outside.write_bytes(data)
            path.symlink_to(outside)
            with self.assertRaisesRegex(support.GateError, "escaping"):
                support.verify_inputs(self.directory)

    def manifest(self):
        recipe = json.loads((support.ROOT / "tools/playbooks/recipe.json").read_text())
        counts = dict(recipe["expected_counts"], pool_entries={"Synthetic": 1})
        return {
            "schema": "source-playbook-conversion/v1", "source_revision": support.PINS["revision"],
            "source_tree": support.PINS["tree"], "profile": support.PINS["profile"],
            "source_inputs": recipe["inputs"], "recipe_sha256": "0" * 64, "converter_sha256": "0" * 64,
            "catalog": {"path": "catalog.tsv", "sha256": "0" * 64, "format": "source-playbook/v1"},
            "counts": counts, "opcode_occurrences": {"1": 1}, "data_boundary": ["synthetic schema fixture"],
            "full_gameplay_parity_claimed": False,
        }

    def test_actual_manifest_schema_rejects_missing_extra_bad_hash_and_parity_claim(self):
        schema = json.loads((support.ROOT / "tools/playbooks/manifest.schema.json").read_text())
        support.validate_schema(self.manifest(), schema)
        for field, value in (("full_gameplay_parity_claimed", True), ("recipe_sha256", "bad"), ("extra", 1)):
            manifest = self.manifest()
            manifest[field] = value
            with self.subTest(field=field), self.assertRaises(support.GateError):
                support.validate_schema(manifest, schema)
        manifest = self.manifest()
        del manifest["source_inputs"]
        with self.assertRaises(support.GateError):
            support.validate_schema(manifest, schema)
        with self.assertRaisesRegex(support.GateError, "Unsupported manifest schema keywords"):
            support.validate_schema({}, {"oneOf": []})

    def test_manifest_provenance_and_catalog_mismatch_cannot_pass_shape_only(self):
        contents, _ = support.snapshot()
        recipe = json.loads(contents["tools/playbooks/recipe.json"])
        schema = json.loads(contents["tools/playbooks/manifest.schema.json"])
        with self.assertRaisesRegex(support.GateError, "manifest/catalog identity"):
            runner.require_manifest(self.manifest(), b"synthetic-not-original-catalog", recipe, contents, schema)

    def test_actual_weighted_ordinal_mutant_preserves_count_order_and_other_duplicate(self):
        entries = ["synthetic_" + str(i) for i in range(52)]
        entries[27:29] = ["play_switch", "play_switch"]
        data = ("pool\tShortOffense\tshort_off_plays\tfixture_blob\t1\t" + ",".join(entries) + "\n").encode()
        changed = runner.mutate_catalog(data, "weighted-ordinal").decode().strip().split("\t")[5].split(",")
        self.assertEqual(52, len(changed))
        self.assertEqual("play_da_bomb", changed[27])
        self.assertEqual("play_switch", changed[28])
        self.assertEqual(entries[:27], changed[:27])
        self.assertEqual(entries[28:], changed[28:])
        with self.assertRaises(support.GateError):
            runner.mutate_catalog(data + data, "weighted-ordinal")

    def test_actual_route_mutant_replaces_opcode_not_parameters_or_identity(self):
        data = b"route\tpr_goto_endzone\tfixture_blob\t1\t1,2,0,5\n"
        self.assertEqual(b"route\tpr_goto_endzone\tfixture_blob\t1\t999,2,0,5\n", runner.mutate_catalog(data, "route-opcode"))
        with self.assertRaises(support.GateError):
            runner.mutate_catalog(b"", "route-opcode")
        with self.assertRaises(support.GateError):
            runner.mutate_catalog(data, "unsupported")

    def test_mutation_rejection_requires_exact_artifact_guard_not_crash_or_build_error(self):
        for name, _, _, _ in runner.MUTATIONS:
            mutation, summary = self.mutation(name)
            runner.require_mutation(1, summary, mutation)
            for code in (0, 2, 134):
                with self.subTest(name=name, code=code), self.assertRaises(support.GateError):
                    runner.require_mutation(code, summary, mutation)
            for field, value in (("context", "unrelated"), ("exception_type", "CompilerFailure"),
                                 ("full_gameplay_parity_claimed", True), ("passed", True)):
                changed = dict(summary)
                changed[field] = value
                with self.subTest(name=name, field=field), self.assertRaises(support.GateError):
                    runner.require_mutation(1, changed, mutation)
            changed = dict(summary)
            changed["error" if name == "route-opcode" else "failed_assertion"] = "unrelated failure"
            with self.assertRaises(support.GateError):
                runner.require_mutation(1, changed, mutation)

    def test_original_function_selection_rejects_missing_or_ambiguous_bodies(self):
        text = "static int fixture(int a) {\n return a + 1;\n}"
        self.assertEqual(text, oracle.body(text, "fixture"))
        with self.assertRaises(support.GateError):
            oracle.body(text, "missing")
        with self.assertRaises(support.GateError):
            oracle.body(text + "\n" + text, "fixture")
        self.assertEqual(53, len(oracle.FUNCTIONS))
        self.assertEqual(53, len(set(oracle.FUNCTIONS)))
        self.assertIn(("drone.c", "get_drone_switches"), oracle.FUNCTIONS)
        self.assertIn(("nfl.c", "lineup_players"), oracle.FUNCTIONS)

    def test_unit_test_gate_never_accepts_skips_zero_discovery_or_status_only(self):
        runner.require_unit_tests(0, "Ran 14 tests in 0.1s\n\nOK\n", 14)
        for code, output in ((1, "Ran 14 tests in 0.1s\nOK\n"), (0, "OK\n"),
                             (0, "Ran 0 tests in 0.1s\nOK\n"),
                             (0, "Ran 14 tests in 0.1s\nOK (skipped=1)\n")):
            with self.subTest(code=code, output=output), self.assertRaises(support.GateError):
                runner.require_unit_tests(code, output, 14)


if __name__ == "__main__":
    unittest.main()
