#!/usr/bin/env python3
"""Guard strict summary/mutant layers; not a substitute for actual M1 caller evidence."""
import importlib.util
from pathlib import Path
import sys
import unittest


SUITE = Path(__file__).resolve().parent
sys.path.insert(0, str(SUITE))
SPEC = importlib.util.spec_from_file_location("movement_caller_runner", SUITE / "run.py")
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


class MovementCallerGuards(unittest.TestCase):
    def summary(self, mode):
        return {"schema": "source-movement-caller-checks/v1",
                "status": "bounded-passed" if mode == "bounded" else "passed",
                "assertions": 808 if mode == "controlled" else 36732,
                "actual_Hurdle_VM_scripts": 16, "actual_MaybeDive_windows_per_hurdle": 7,
                "actual_Core_post_VM_physics_cases": 664, "actual_Core_LEAPATT_operand_bytes": 36,
                "whole_M1_or_full_gameplay_qualified": False,
                "known_R_SKIP_asset_missing": mode == "bounded",
                "complete_publication_qualification": mode == "complete",
                "actual_animation_library_manifest": RUNNER.MOVEMENT_SELECTED_SHA if mode == "bounded"
                else RUNNER.MOVEMENT_ANIMATION_SHA}

    def test_complete_and_controlled_exact_inventories(self):
        for mode in ("bounded", "complete", "controlled"):
            RUNNER.require_summary(0, self.summary(mode), mode)
            for field in ("assertions", "actual_Hurdle_VM_scripts", "actual_MaybeDive_windows_per_hurdle",
                          "actual_Core_post_VM_physics_cases", "actual_Core_LEAPATT_operand_bytes"):
                with self.subTest(mode=mode, field=field), self.assertRaises(RuntimeError):
                    row = self.summary(mode)
                    row[field] -= 1
                    RUNNER.require_summary(0, row, mode)

    def test_missing_asset_can_never_be_complete_publication_green(self):
        with self.assertRaises(RuntimeError):
            RUNNER.require_summary(0, self.summary("bounded"), "complete")
        row = self.summary("complete")
        row["known_R_SKIP_asset_missing"] = True
        with self.assertRaises(RuntimeError):
            RUNNER.require_summary(0, row, "complete")

    def test_wrong_loaded_library_or_claimed_whole_parity_fails(self):
        for field, value in (("actual_animation_library_manifest", RUNNER.MOVEMENT_SELECTED_SHA),
                             ("whole_M1_or_full_gameplay_qualified", True),
                             ("complete_publication_qualification", False)):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                row = self.summary("complete")
                row[field] = value
                RUNNER.require_summary(0, row, "complete")

    def test_empty_or_nonzero_process_cannot_pass(self):
        for code, row in ((0, None), (0, {}), (2, self.summary("complete"))):
            with self.subTest(code=code), self.assertRaises(RuntimeError):
                RUNNER.require_summary(code, row, "complete")

    def test_only_required_offending_assertion_is_a_mutant_rejection(self):
        row = {"schema": "source-movement-caller-checks/v1", "status": "failed",
               "failed_assertion": "M1 CORE ASSERTION: actual field", "assertions": 1}
        RUNNER.require_mutant(1, row, "actual field")
        for code, value in ((0, row), (2, row), (134, row), (1, None),
                            (1, dict(row, status="error")), (1, dict(row, failed_assertion="unrelated")),
                            (1, dict(row, assertions=0))):
            with self.subTest(code=code), self.assertRaises(RuntimeError):
                RUNNER.require_mutant(code, value, "actual field")

    def test_all_eight_real_source_mutation_anchors_are_unique(self):
        self.assertEqual(8, len(RUNNER.MUTANTS))
        for name, filename, old, new, _ in RUNNER.MUTANTS:
            with self.subTest(name=name):
                data = (RUNNER.ROOT / "Assets/Scripts/Gameplay" / filename).read_bytes()
                self.assertEqual(1, data.count(old.encode()))
                self.assertNotEqual(old, new)


if __name__ == "__main__":
    unittest.main()
