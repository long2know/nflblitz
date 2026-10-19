#!/usr/bin/env python3
"""Asset-free lifecycle runner guards; synthetic summaries never execute gameplay."""
import importlib.util
from pathlib import Path
import unittest

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("match_lifecycle_runner", HERE / "run.py")
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


class LifecycleRunnerGuards(unittest.TestCase):
    def summary(self):
        return dict(schema="source-match-ended-caller-checks/v1", status="passed", assertions=593,
                    cases=sorted(RUNNER.required_cases()), whole_game_or_native_qualified=False,
                    declared_boundary_cases_not_public_full_match=True)

    def test_exact_positive_inventory_and_scope_are_required(self):
        RUNNER.require_summary(0, self.summary())
        for key, value in (
            ("assertions", 592), ("status", "bounded-passed"), ("schema", "other"),
            ("whole_game_or_native_qualified", True), ("declared_boundary_cases_not_public_full_match", False),
            ("cases", self.summary()["cases"][:-1]), ("cases", ["substituted"] + self.summary()["cases"][1:]),
            ("cases", self.summary()["cases"][:-1] + self.summary()["cases"][:1]),
        ):
            summary = self.summary()
            summary[key] = value
            with self.subTest(key=key, value=value), self.assertRaises(RuntimeError):
                RUNNER.require_summary(0, summary)

    def test_empty_error_or_nonzero_process_cannot_be_positive(self):
        for code, summary in ((0, None), (0, {}), (1, self.summary()), (134, self.summary())):
            with self.subTest(code=code), self.assertRaises(RuntimeError):
                RUNNER.require_summary(code, summary)

    def test_controls_require_exact_named_runtime_failure_not_compile_or_unrelated_error(self):
        summary = self.summary()
        summary.update(status="failed", assertions=25, cases=summary["cases"][:1], failed_assertion="causal contract")
        RUNNER.require_summary(1, summary, "causal contract")
        for code, changes in ((0, {}), (2, {}), (134, {}), (1, {"status": "error"}),
                              (1, {"failed_assertion": "unrelated"}), (1, {"assertions": 0})):
            with self.subTest(code=code, changes=changes), self.assertRaises(RuntimeError):
                RUNNER.require_summary(code, dict(summary, **changes), "causal contract")

    def test_all_nine_controls_are_unique_actual_production_edits(self):
        self.assertEqual(9, len(RUNNER.MUTANTS))
        self.assertEqual(9, len({item[0] for item in RUNNER.MUTANTS}))
        for identity, name, old, new, expected in RUNNER.MUTANTS:
            with self.subTest(identity=identity):
                self.assertEqual(1, (RUNNER.ROOT / name).read_text().count(old))
                self.assertNotEqual(old, new)
                self.assertGreater(len(expected), 35)

    def test_real_bridge_is_local_to_this_compile_closure_not_a_global_inventory_rebase(self):
        original = RUNNER.production_paths()
        self.assertEqual(76, len(original))
        self.assertIn(RUNNER.ROOT / "Assets/Scripts/AssetImport/SourceNormalCameraData.cs", original)
        self.assertIn(RUNNER.ROOT / "Assets/Scripts/Contracts/SourceCameraContracts.cs", original)
        self.assertNotIn(RUNNER.ROOT / RUNNER.BRIDGE, original)
        project = (HERE / "MatchLifecycleChecks.csproj").read_text()
        self.assertIn('$(BlitzSourceRoot)Assets/Scripts/Runtime/FixedStepBridge.cs', project)
        self.assertIn("../parity/FullKernelInputs.cs", project)
        self.assertIn("!match-lifecycle/MatchLifecycleChecks.csproj", (RUNNER.ROOT / "tests/.gitignore").read_text())
        self.assertEqual(71, len(RUNNER.required_cases()))


if __name__ == "__main__":
    unittest.main()
