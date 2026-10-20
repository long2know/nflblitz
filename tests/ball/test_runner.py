#!/usr/bin/env python3
"""Guard the bounded ball assertion/mutation inventory, not gameplay itself."""
import importlib.util
import hashlib
import os
from pathlib import Path
import shutil
import sys
import unittest
import uuid


SUITE = Path(__file__).resolve().parent
sys.path.insert(0, str(SUITE))
SPEC = importlib.util.spec_from_file_location("ball_runner", SUITE / "run.py")
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


class BallRunnerGuards(unittest.TestCase):
    def summary(self):
        return dict(RUNNER.EXPECTED, schema="actual-source-ball-checks/v1",
                    status="passed", full_gameplay_parity_claimed=False)

    def test_only_fresh_pinned_original_C_tables_can_enter_independent_mutant_roots(self):
        parent = Path(os.environ.get("BLITZ_CI_TEST_OUTPUT", RUNNER.ROOT / ".squad/.scratch/ball-oracle-tests"))
        self.assertTrue(parent.resolve().is_relative_to(RUNNER.ROOT))
        work = parent / uuid.uuid4().hex
        work.mkdir(parents=True)
        self.addCleanup(shutil.rmtree, work)
        table = work / "oracle-cases.tsv"
        table.write_bytes(b"fresh test oracle table\n")
        (work / "old-managed-proof.json").write_text('{"status":"passed"}\n')
        pins = {table.name: hashlib.sha256(table.read_bytes()).hexdigest()}
        captured = RUNNER.original_c_table_snapshot(work, pins)
        self.assertEqual(captured, {Path(table.name): table.read_bytes()})
        self.assertNotIn(Path("old-managed-proof.json"), captured, "old managed evidence must never be copied as fresh")
        first = RUNNER.isolated_oracle_root(work, "first-run", "same-mutant", captured)
        (first / "old-managed-proof.json").write_text('{"status":"passed"}\n')
        second = RUNNER.isolated_oracle_root(work, "second-run", "same-mutant", captured)
        self.assertNotEqual(first, second, "repeated author runs must not share prior mutant diagnostics")
        self.assertEqual((second / table.name).read_bytes(), table.read_bytes())
        self.assertFalse((second / "old-managed-proof.json").exists())
        (first / table.name).write_bytes(b"mutated private output\n")
        self.assertEqual((second / table.name).read_bytes(), captured[Path(table.name)])
        with self.assertRaises(FileExistsError):
            RUNNER.isolated_oracle_root(work, "second-run", "same-mutant", captured)
        table.write_bytes(b"changed test oracle table\n")
        with self.assertRaisesRegex(RuntimeError, "freshly executed oracle"):
            RUNNER.original_c_table_snapshot(work, pins)
        with self.assertRaisesRegex(RuntimeError, "freshly executed oracle"):
            RUNNER.original_c_table_snapshot(work, {})

    def test_exact_complete_assertion_inventory_is_required(self):
        RUNNER.require_baseline(0, self.summary(), "all")
        for key, count in RUNNER.EXPECTED.items():
            for value in (0, count - 1, count + 1, None):
                with self.subTest(field=key, value=value), self.assertRaises(RuntimeError):
                    summary = self.summary()
                    summary[key] = value
                    RUNNER.require_baseline(0, summary, "all")

    def test_each_author_section_still_checks_its_exact_inventory(self):
        for section in ("native", "caller", "sentinel", "geometry", "orientation"):
            with self.subTest(section=section):
                RUNNER.require_baseline(0, self.summary(), section)
                with self.assertRaises(RuntimeError):
                    RUNNER.require_baseline(0, {"status": "passed"}, section)

    def test_current272_requires_exact_retained_count_and_actual_profile_without_rewriting_history(self):
        row = dict(self.summary(), canonical_B1_animation_manifest=RUNNER.CONTACT_ANIMATION_SHA,
                   caller_runtime_animation_manifest=RUNNER.CONTACT_ANIMATION_SHA,
                   caller_assertions=RUNNER.CONTACT272_CALLER_ASSERTIONS,
                   production_contact272_selected=True,
                   caller_added_FIFO_assertions=0, geometry_added_profile_assertions=0)
        self.assertEqual(18017, RUNNER.EXPECTED["caller_assertions"])
        self.assertEqual(17980 + 14 + 23 + 1, RUNNER.CONTACT272_CALLER_ASSERTIONS)
        RUNNER.require_baseline(0, row, "all", RUNNER.CONTACT_ANIMATION_SHA)
        RUNNER.require_baseline(0, row, "caller", RUNNER.CONTACT_ANIMATION_SHA)
        for field, value in (
                ("caller_assertions", 18017), ("caller_assertions", 18019),
                ("caller_assertions", 0), ("caller_assertions", RUNNER.C2_CALLER_ASSERTIONS),
                ("production_contact272_selected", False), ("production_contact272_selected", None),
                ("caller_added_FIFO_assertions", 44), ("geometry_added_profile_assertions", 9)):
            with self.subTest(field=field, value=value), self.assertRaises(RuntimeError):
                RUNNER.require_baseline(0, dict(row, **{field: value}), "all", RUNNER.CONTACT_ANIMATION_SHA)
        for field in ("canonical_B1_animation_manifest", "caller_runtime_animation_manifest"):
            for wrong_profile in ("old157-is-not272", RUNNER.C2_ANIMATION_SHA):
                changed = dict(row, **{field: wrong_profile})
                with self.subTest(field=field, profile=wrong_profile), self.assertRaises(RuntimeError):
                    RUNNER.require_baseline(0, changed, "all", RUNNER.CONTACT_ANIMATION_SHA)
        with self.assertRaises(RuntimeError):
            RUNNER.require_baseline(0, row, "all", RUNNER.C2_ANIMATION_SHA)

    def test_canonical281_keeps_all_old_ball_counts_and_requires_actual_complete_identity(self):
        row = dict(self.summary(), canonical_B1_animation_manifest=RUNNER.C2_ANIMATION_SHA,
                   caller_runtime_animation_manifest=RUNNER.C2_ANIMATION_SHA,
                   caller_assertions=RUNNER.C2_CALLER_ASSERTIONS,
                   caller_added_FIFO_assertions=RUNNER.FIFO_ADDED_ASSERTIONS,
                   geometry_caller_assertions=32 + RUNNER.GEOMETRY_ADDED_ASSERTIONS,
                   geometry_added_profile_assertions=RUNNER.GEOMETRY_ADDED_ASSERTIONS)
        RUNNER.require_baseline(0, row, "all", RUNNER.C2_ANIMATION_SHA)
        for field, value in (("caller_assertions", 18017), ("caller_assertions", 18018),
                             ("caller_assertions", RUNNER.C2_CALLER_ASSERTIONS + 1),
                             ("caller_added_FIFO_assertions", 0),
                             ("geometry_caller_assertions", 32),
                             ("geometry_added_profile_assertions", 0)):
            with self.subTest(field=field, value=value), self.assertRaises(RuntimeError):
                RUNNER.require_baseline(0, dict(row, **{field: value}), "all", RUNNER.C2_ANIMATION_SHA)
        for field in ("canonical_B1_animation_manifest", "caller_runtime_animation_manifest"):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                RUNNER.require_baseline(0, dict(row, **{field: RUNNER.CONTACT_ANIMATION_SHA}),
                                       "all", RUNNER.C2_ANIMATION_SHA)
        factory = (RUNNER.SUITE / "CanonicalInputs.cs").read_text()
        self.assertIn("if (inputs.IsC2Profile)", factory)
        self.assertIn("return inputs.Create(seed, configuration, randomFactory, presentation);", factory)
        self.assertNotIn("new GameSimulation(", factory)
        self.assertNotIn("new SourceCatchGeometry(", factory)

    def test_two_profile_geometry_preserves32_and_named_unfiltered_controlled_input(self):
        self.assertEqual(18017, RUNNER.EXPECTED["caller_assertions"])
        self.assertEqual(17980 + 14 + 23 + 1 + 44, RUNNER.C2_CALLER_ASSERTIONS)
        self.assertEqual(32, RUNNER.EXPECTED["geometry_caller_assertions"])
        self.assertEqual(9, RUNNER.GEOMETRY_ADDED_ASSERTIONS)
        for row in RUNNER.GEOMETRY_MUTATIONS:
            self.assertEqual((RUNNER.ROOT / "Assets/Scripts/Gameplay" / row[1]).read_text().count(row[2]), row[6])
            self.assertIn("default-seed standing BatDown", row[5])
        source = (RUNNER.SUITE / "GeometryCallerChecks.cs").read_text()
        self.assertIn('site => 99', source)
        self.assertIn('source-catch-controller/raw99-all-sites/v1', source)
        self.assertIn('SourceRandomEvidence.ExternalUnverified', source)
        self.assertIn('CheckDefaultStandingBatDown(real, negative);', source)
        self.assertIn('originalGeometryAssertionsRetained = 32', source)
        self.assertIn('catcher.Role = ActorRole.Defender;', source)
        self.assertIn('game.Model.PassTicksElapsed = 45;', source)

    def test_menu_FIFO_adapter_has_exact_queue_clock_bound_and_three_unique_causal_controls(self):
        self.assertEqual(44, RUNNER.FIFO_ADDED_ASSERTIONS)
        self.assertEqual(3, len(RUNNER.FIFO_MUTATIONS))
        self.assertEqual(20, len(RUNNER.MUTATIONS))
        for name, filename, old, new, section, expected, count in RUNNER.FIFO_MUTATIONS:
            self.assertEqual(section, "flip-fifo")
            self.assertEqual((RUNNER.ROOT / "Assets/Scripts/Gameplay" / filename).read_text().count(old), count, name)
            self.assertNotEqual(old, new)
            self.assertIn("exact specified flip", expected)
        text = (RUNNER.SUITE / "SourceCallerChecks.cs").read_text()
        start = text.index("private static int DeliverMenuPacket(")
        end = text.index("private static int OneNextSourceWake(", start)
        helper = text[start:end]
        self.assertIn("(60 * expected.Length - remainder + 56) / 57", helper)
        self.assertIn("host < bound", helper)
        self.assertIn("SamePacket(game.Model.SourceCurrentInput, expected[delivered])", helper)
        self.assertIn("expected.Length - delivered", helper)
        self.assertNotIn(".Phase", helper)
        self.assertNotIn(".Clear(", helper)
        self.assertNotIn(".Enqueue(", helper)
        self.assertNotIn(".Dequeue(", helper)
        self.assertNotIn("SetValue(", text)

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

    def test_fresh_press_mutant_targets_active_post_begin_tick_state_merge(self):
        row = next(item for item in RUNNER.MUTATIONS if item[0] == "raw-new-pressed")
        self.assertEqual("SourceMovementIntegration.cs", row[1])
        self.assertEqual("state.NewButtons |= (SourceDroneButtons)(int)station.Pressed;", row[2])
        self.assertIn("post-BeginTick latched fresh B", row[5])
        source = (RUNNER.ROOT / "Assets/Scripts/Gameplay" / row[1]).read_text()
        begin = source.index("private void UpdateSourceMovementPlayers(")
        tick = source.index("state = SourceMovement.BeginTick(", begin)
        merge = source.index(row[2], tick)
        apply = source.index("ApplySourceMovement(actor, state);", merge)
        self.assertLess(tick, merge)
        self.assertLess(merge, apply)

    def test_source_transform_mutant_still_omits_odd_dispatches_in_the_actual_shared_helper(self):
        row = next(item for item in RUNNER.MUTATIONS if item[0] == "source-transform-wake")
        self.assertEqual("GameSimulation.Source.cs", row[1])
        self.assertEqual("if (SourceStationInputTransform == null) return command;", row[2])
        self.assertEqual(
            "if (SourceStationInputTransform == null || Model.SourceTick % 2 != 0) return command;", row[3])
        self.assertEqual(("caller", "ordered raw drain transforms exactly once", 1), row[4:])
        source = (RUNNER.ROOT / "Assets/Scripts/Gameplay" / row[1]).read_text()
        start = source.index("private PlayerCommand TransformSourceStationInput(")
        end = source.index("private void SourceStep(", start)
        helper = source[start:end]
        self.assertIn(row[2], helper)
        self.assertIn("SourceStationInputTransform(new GameSnapshot(Model)", helper)

    def test_orientation_is_additive_and_each_control_targets_actual_compilable_production(self):
        self.assertEqual(34713, RUNNER.EXPECTED["orientation_ball_assertions"])
        self.assertEqual(470666, RUNNER.EXPECTED["native_ball_assertions"])
        self.assertEqual(20, len(RUNNER.MUTATIONS))
        self.assertEqual(3, len(RUNNER.ORIENTATION_MUTATIONS))
        self.assertEqual(3, len({row[0] for row in RUNNER.ORIENTATION_MUTATIONS}))
        for name, filename, old, new, section, expected, count in RUNNER.ORIENTATION_MUTATIONS:
            with self.subTest(control=name):
                self.assertEqual(section, "orientation")
                self.assertEqual((RUNNER.ROOT / "Assets/Scripts/Gameplay" / filename).read_text().count(old), count)
                self.assertNotEqual(old, new)
                self.assertIn("original ball_proc orientation", expected)


if __name__ == "__main__":
    unittest.main()
