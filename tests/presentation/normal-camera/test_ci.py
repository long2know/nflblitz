#!/usr/bin/env python3
"""Fail closed if original camera checks/controls stop being selected by durable CI."""
import importlib.util
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class CameraCiChecks(unittest.TestCase):
    def test_game_ci_selects_actual_original_sequences_and_compiled_controls(self):
        workflow = (ROOT / ".github/workflows/game-build.yml").read_text()
        self.assertIn("python3 -B tests/presentation/normal-camera/test_ci.py", workflow)
        self.assertRegex(workflow, r"python3 -B tests/presentation/normal-camera/run\.py --source-root "
                         r"\.squad/\.scratch/SourceData/source-kernel-original --output \S+ --controls")
        self.assertIn("timeout-minutes: 40", workflow)
        self.assertNotIn("continue-on-error", workflow)
        for retained in ("tests/movement-callers/run.py", "tests/match-lifecycle/run.py",
                         "tests/source-host-lifecycle/run.py", "tests/impact-dust/run.py", "tests/loose-helmet/run.py",
                         "tests/presentation/movement-stage/mutations.py", "tests/audio/vm/run.py"):
            self.assertIn(retained, workflow)
        self.assertRegex(workflow, r"timeout-minutes:\s*(?:3\d|[4-9]\d)")

    def test_source_player_retains_all_inherited_node28_and_adds_camera_observer(self):
        workflow = (ROOT / ".github/workflows/source-player.yml").read_text()
        self.assertIn("node --test tools/source-player/normal-camera.browser.test.mjs", workflow)
        for retained in ("input.test.mjs", "renderer.test.mjs", "popped-head.renderer.test.mjs",
                         "free-ball.renderer.test.mjs", "ball-trails.renderer.test.mjs", "dust.renderer.test.mjs",
                         "helmet.renderer.test.mjs"):
            self.assertIn("tools/source-player/" + retained, workflow)
        self.assertNotIn("continue-on-error", workflow)

    def test_compiled_control_inventory_and_exact_behavioral_failure(self):
        runner = load("actual_raw_camera_runner", HERE / "run.py")
        controls = runner.mutations()
        self.assertEqual(len(controls), 26)
        self.assertEqual(len({row[0] for row in controls}), 26)
        for name, path, before, after, failure in controls:
            self.assertEqual((ROOT / path).read_text().count(before), 1, name)
            self.assertNotEqual(before, after)
            self.assertTrue(failure)
        self.assertEqual(runner.caller_guards(), 7)
        program = (HERE / "Program.cs").read_text()
        self.assertEqual(len(re.findall(r'Run\("[^"]+", ', program)), 15)
        self.assertIn("1440", program)
        self.assertIn("maximumAbsoluteErrorByColumn", program)

    def test_coupled_lifecycle_inventory_keeps_both_possessions_and_countercontrols(self):
        oracle = load("actual_raw_camera_oracle", HERE / "source_oracle.py")
        cases = oracle.make_cases({"tables": []}, {"tables": {"csi_kickoff": [{"auto_hike_ticks": 42}] * 11}})
        by_name = {name: rows for name, _, rows in cases}
        lifecycle = cases[-12:]
        self.assertEqual(sum(len(rows) for _, _, rows in lifecycle), 2752)
        for side in range(2):
            tracking = by_name[f"field-goal-side{side}-tracking"]
            for menu in (False, True):
                suffix = "menu" if menu else "continuous"
                for goal in (True, False):
                    rows = by_name[f"field-goal-side{side}-new-{'field-goal' if goal else 'normal'}-{suffix}"]
                    self.assertEqual(rows[:320], tracking)
                    self.assertEqual([row[0] for row in rows[320:]], list(range(520 if menu else 320, 528 if menu else 328)))
                    self.assertEqual({(row[1], row[3], row[13], row[31]) for row in rows[320:]},
                                     {(1, 2 if goal else 0, 4 if goal else 0, 4 if goal else 0)})
                rows = by_name[f"field-goal-side{side}-interrupted-tracking-{suffix}"]
                self.assertEqual(len(rows), 32)
                self.assertEqual(rows[:20], tracking[:20])
                self.assertEqual([row[1] for row in rows[20:]], [3] * 4 + [1] * 8)
                self.assertEqual(rows[24][0] - rows[23][0], 201 if menu else 1)


if __name__ == "__main__":
    unittest.main(verbosity=2)
