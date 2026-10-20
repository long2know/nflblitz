#!/usr/bin/env python3
"""Guard tests for the actual-source regression runner; not gameplay evidence."""
import importlib.util
import os
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch
import uuid


SPEC = importlib.util.spec_from_file_location("contract_runner", Path(__file__).with_name("run.py"))
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


class ContractRunnerGuards(unittest.TestCase):
    def baseline(self):
        return dict(RUNNER.EXPECTED, schema="actual-source-contract-checks/v1",
                    status="passed", full_gameplay_parity_claimed=False)

    def test_exact_assertion_inventory_is_required(self):
        RUNNER.require_baseline(0, self.baseline())
        for field in RUNNER.EXPECTED:
            for value in (0, RUNNER.EXPECTED[field] - 1, RUNNER.EXPECTED[field] + 1, None):
                with self.subTest(field=field, value=value), self.assertRaises(RuntimeError):
                    summary = self.baseline()
                    summary[field] = value
                    RUNNER.require_baseline(0, summary)

    def test_status_only_zero_discovery_or_nonzero_exit_cannot_pass(self):
        for summary in (None, {}, {"status": "passed"}):
            with self.subTest(summary=summary), self.assertRaises(RuntimeError):
                RUNNER.require_baseline(0, summary)
        with self.assertRaises(RuntimeError):
            RUNNER.require_baseline(1, self.baseline())

    def test_error_or_full_parity_claim_cannot_pass(self):
        for field, value in (("status", "error"), ("schema", "different"),
                             ("full_gameplay_parity_claimed", True)):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                summary = self.baseline()
                summary[field] = value
                RUNNER.require_baseline(0, summary)

    def test_mutation_requires_the_actual_offending_field_assertion(self):
        for name, _, _, _, assertion in RUNNER.MUTATIONS:
            summary = {"schema": "actual-source-contract-checks/v1", "status": "failed",
                       "failed_assertion": assertion, "assertions": 1}
            with self.subTest(name=name):
                RUNNER.require_mutation(1, summary, assertion)
                summary["failed_assertion"] = "unrelated assertion"
                with self.assertRaises(RuntimeError):
                    RUNNER.require_mutation(1, summary, assertion)

    def test_compilation_failure_crash_or_surviving_mutant_is_not_a_kill(self):
        assertion = RUNNER.MUTATIONS[0][-1]
        for exit_code, summary in ((1, None), (1, {"status": "error"}), (0, self.baseline()),
                                   (134, {"schema": "actual-source-contract-checks/v1",
                                          "status": "failed", "failed_assertion": assertion, "assertions": 1}),
                                   (1, {"schema": "actual-source-contract-checks/v1", "status": "failed",
                                        "failed_assertion": assertion, "assertions": 0})):
            with self.subTest(exit_code=exit_code, summary=summary), self.assertRaises(RuntimeError):
                RUNNER.require_mutation(exit_code, summary, assertion)

    def test_all_twenty_one_anchors_name_unique_actual_source_fields(self):
        self.assertEqual(21, len(RUNNER.MUTATIONS))
        for name, filename, anchor, replacement, _ in RUNNER.MUTATIONS:
            with self.subTest(name=name):
                path = RUNNER.ROOT / "Assets/Scripts/Contracts" / filename
                self.assertEqual(1, path.read_text(encoding="utf-8").count(anchor))
                self.assertNotEqual(anchor, replacement)

    def test_snapshot_tracks_all_contracts_and_exact_pure_rule_sources(self):
        contents, hashes = RUNNER.snapshot()
        expected = {path.relative_to(RUNNER.ROOT).as_posix()
                    for path in (RUNNER.ROOT / "Assets/Scripts/Contracts").rglob("*.cs")}
        expected.update("Assets/Scripts/Gameplay/" + name for name in RUNNER.RULE_SOURCES)
        self.assertEqual(expected, set(contents))
        self.assertEqual(expected, set(hashes))
        self.assertEqual(hashes, RUNNER.hashes())

    def test_presentation_inventory_and_pure_model_include_are_required(self):
        self.assertEqual(189, RUNNER.EXPECTED["presentation_dto_assertions"])
        self.assertEqual(511, RUNNER.EXPECTED["assertions"])
        self.assertEqual(65, RUNNER.EXPECTED["enum_mappings"])
        path = RUNNER.ROOT / "Assets/Scripts/Contracts/SourcePresentationContracts.cs"
        self.assertIn(path, RUNNER.source_files())
        project = (RUNNER.SUITE / "model/PureContracts.csproj").read_text(encoding="utf-8")
        self.assertIn("Assets/Scripts/Contracts/**/*.cs", project)
        self.assertNotIn("Gameplay/**/*.cs", project)
        self.assertNotIn("SourceRandom.cs", project)
        self.assertNotIn("Presentation/*.cs", project)

    def test_actual_harness_snapshot_prunes_generated_trees_before_traversal(self):
        actual = RUNNER.harness_snapshot()
        self.assertTrue(actual)
        parent = Path(os.environ.get("BLITZ_CI_TEST_OUTPUT", RUNNER.ROOT / ".squad/.scratch/contract-harness-tests"))
        self.assertTrue(parent.resolve().is_relative_to(RUNNER.ROOT))
        work = parent / uuid.uuid4().hex
        work.mkdir(parents=True)
        self.addCleanup(shutil.rmtree, work)
        for name, data in actual.items():
            target = work / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        for name in (".artifacts/mutant/Program.cs", "bin/generated/Unexpected.csproj", "model/obj/Stale.cs"):
            target = work / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text("must not be a harness input")
        walk = os.walk
        visited = []
        def tracked(*args, **kwargs):
            for row in walk(*args, **kwargs):
                visited.append(Path(row[0]).relative_to(work))
                yield row
        with patch.object(RUNNER, "SUITE", work), patch.object(RUNNER.os, "walk", tracked):
            captured = RUNNER.harness_snapshot()
        self.assertEqual(captured, actual, "every actual harness/project byte is retained, without stale shadow inputs")
        self.assertFalse(any({".artifacts", "bin", "obj"} & set(path.parts) for path in visited),
                         "filtering after rglob would still race/traverse other workers' build trees")


if __name__ == "__main__":
    unittest.main()
