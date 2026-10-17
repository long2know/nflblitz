#!/usr/bin/env python3
"""Guard the bounded ball assertion/mutation inventory, not gameplay itself."""
import importlib.util
from pathlib import Path
import sys
import unittest


SUITE = Path(__file__).resolve().parent
sys.path.insert(0, str(SUITE))
SPEC = importlib.util.spec_from_file_location("ball_runner", SUITE / "run.py")
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


class BallRunnerGuards(unittest.TestCase):
    def summary(self):
        return dict(RUNNER.EXPECTED, schema="actual-source-ball-checks/v1",
                    status="passed", full_gameplay_parity_claimed=False)

    def test_exact_complete_assertion_inventory_is_required(self):
        RUNNER.require_baseline(0, self.summary(), "all")
        for key, count in RUNNER.EXPECTED.items():
            for value in (0, count - 1, count + 1, None):
                with self.subTest(field=key, value=value), self.assertRaises(RuntimeError):
                    summary = self.summary()
                    summary[key] = value
                    RUNNER.require_baseline(0, summary, "all")

    def test_each_author_section_still_checks_its_exact_inventory(self):
        for section in ("native", "caller", "sentinel", "geometry"):
            with self.subTest(section=section):
                RUNNER.require_baseline(0, self.summary(), section)
                with self.assertRaises(RuntimeError):
                    RUNNER.require_baseline(0, {"status": "passed"}, section)

    def test_zero_discovery_status_only_nonzero_exit_or_full_parity_claim_cannot_pass(self):
        for code, summary in ((0, None), (0, {}), (0, {"status": "passed"}), (1, self.summary()),
                              (0, dict(self.summary(), full_gameplay_parity_claimed=True))):
            with self.subTest(code=code, summary=summary), self.assertRaises(RuntimeError):
                RUNNER.require_baseline(code, summary, "all")

    def test_mutant_requires_its_actual_offending_field_assertion(self):
        summary = {"schema": "actual-source-ball-checks/v1", "status": "failed",
                   "full_gameplay_parity_claimed": False, "failed_assertion": "actual field"}
        RUNNER.require_mutant(1, summary, "actual field")
        with self.assertRaises(RuntimeError):
            RUNNER.require_mutant(1, summary, "different field")

    def test_compile_failure_crash_unrelated_exception_and_surviving_mutant_are_not_kills(self):
        for code, summary in ((1, None), (2, {"status": "error"}), (0, self.summary()),
                              (134, {"status": "failed", "failed_assertion": "actual field"})):
            with self.subTest(code=code), self.assertRaises(RuntimeError):
                RUNNER.require_mutant(code, summary, "actual field")

    def test_actual_frozen_mutation_anchor_inventory_is_exact(self):
        self.assertEqual(20, len(RUNNER.MUTATIONS))
        self.assertEqual(20, len({row[0] for row in RUNNER.MUTATIONS}))
        for name, filename, anchor, replacement, _, _, occurrences in RUNNER.MUTATIONS:
            with self.subTest(name=name):
                path = RUNNER.ROOT / "Assets/Scripts/Gameplay" / filename
                self.assertEqual(occurrences, path.read_bytes().count(anchor.encode()))
                self.assertNotEqual(anchor, replacement)


if __name__ == "__main__":
    unittest.main()
