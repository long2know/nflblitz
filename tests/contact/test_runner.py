import copy
from pathlib import Path
import shutil
import unittest
import uuid

import run
import source


class ContactRunnerTests(unittest.TestCase):
    def summary(self):
        return {
            "schema": "source-contact-checks/v1", "status": "passed", "assertions": run.EXPECTED_ASSERTIONS["kernel"],
            "source_programs": 131, "source_instructions": 2147, "kernel_scenarios": 22,
            "actual_kernel_puppet_callbacks": 296, "full_contact_or_game_parity": False,
            "base157_sha256": "f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e",
            "extra115_sha256": "907e2d368394e5c9db1bebdf218abae62bb3b841230293670cb3d2db48e05fcd",
            "actual_bound_SAFN": run.SOUND_SHA,
            "production272_sha256": run.CONTACT_SHA,
            "production_profile": "original-teams-contact272",
            "actual_animation_clips": 272, "actual_animation_frames": 24892,
        }

    def test_exact_evidence_layers_required(self):
        run.require_pass(0, self.summary(), "kernel")
        for field, value in (
                ("status", "error"), ("assertions", 0), ("source_programs", 130),
                ("source_instructions", 2146), ("kernel_scenarios", 0),
                ("actual_kernel_puppet_callbacks", 0), ("full_contact_or_game_parity", True),
                ("base157_sha256", "alias"), ("extra115_sha256", "alias"), ("actual_bound_SAFN", "muted"),
                ("production272_sha256", "alias157"), ("production_profile", "original-teams-movement157"),
                ("actual_animation_clips", 157), ("actual_animation_frames", 13490)):
            changed = copy.deepcopy(self.summary())
            changed[field] = value
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                run.require_pass(0, changed, "kernel")
        for code, value in ((1, self.summary()), (0, None), (1, None), (0, {})):
            with self.subTest(code=code, value=value), self.assertRaises(RuntimeError):
                run.require_pass(code, value, "kernel")

    def test_pure_original_case_layers_cannot_be_replaced_by_kernel_or_short_counts(self):
        for mode, prefix in (("emit", "emitted"), ("verify", "verified")):
            row = dict(self.summary(), assertions=run.EXPECTED_ASSERTIONS[mode],
                       **{prefix + "_detector_cases": 1152, prefix + "_segment_cases": 1024})
            run.require_pass(0, row, mode)
            for field in (prefix + "_detector_cases", prefix + "_segment_cases"):
                with self.subTest(mode=mode, field=field), self.assertRaises(RuntimeError):
                    run.require_pass(0, dict(row, **{field: row[field] - 1}), mode)

    def test_recorded_current_generation_assertion_totals_cannot_silently_drop_cases(self):
        for mode, expected in run.EXPECTED_ASSERTIONS.items():
            row = dict(self.summary(), assertions=expected, emitted_detector_cases=1152,
                       emitted_segment_cases=1024, verified_detector_cases=1152, verified_segment_cases=1024)
            run.require_pass(0, row, mode)
            for count in (0, expected - 1, expected + 1):
                with self.subTest(mode=mode, count=count), self.assertRaises(RuntimeError):
                    run.require_pass(0, dict(row, assertions=count), mode)

    def test_canonical281_identity_is_explicit_not_a272_relabel(self):
        current = dict(self.summary(), production_profile="original-teams-contact281-c2",
                       actual_animation_clips=281, actual_animation_frames=26230,
                       c2_animation_sha256=run.C2_ANIMATION_SHA,
                       assertions=run.EXPECTED_ASSERTIONS["kernel"] + 22,
                       added_canonical_result_timing_assertions=22,
                       retained_C1_assertions=run.EXPECTED_ASSERTIONS["kernel"])
        run.require_pass(0, current, "kernel", canonical=True)
        with self.assertRaises(RuntimeError):
            run.require_pass(0, current, "kernel")
        for field, value in (("actual_animation_clips", 272), ("actual_animation_frames", 24892),
                             ("c2_animation_sha256", run.CONTACT_SHA),
                             ("production_profile", "original-teams-contact272"),
                             ("assertions", run.EXPECTED_ASSERTIONS["kernel"] - 1),
                             ("added_canonical_result_timing_assertions", 21),
                             ("retained_C1_assertions", run.EXPECTED_ASSERTIONS["kernel"] - 1)):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                run.require_pass(0, dict(current, **{field: value}), "kernel", canonical=True)

    def test_old_same_wake_contract_and_existing_ten_wakes_are_preserved(self):
        text = (run.SUITE / "KernelChecks.cs").read_text()
        original = """        Check(game.Model.Phase == GamePhase.DeadBall && victim.Down && game.SourceTick > hit
            && game.SourceEvents.Any(e => e.Type == SourceEventType.PlayResolved),
            "actual H_FRONT1 ANI_CODE player_down resolves the real match after impact/flight, not a fall timer");"""
        self.assertEqual(text.count(original), 1)
        self.assertIn("if (!game.SourceC2Enabled)\n" + original, text)
        self.assertIn("if (game.SourceC2Enabled && i == 0) whistleChance = VerifyNextSourceResult", text)
        self.assertIn("long downTick = game.SourceTick;", text)
        self.assertIn("game.SourceTick - downTick >= 10", text)
        self.assertIn("a165e0239cb144673be7a9f7c9fc942852a6fb128f22d994b0a27ceb860e8730", text)
        self.assertIn("events[committed].TauntedAfter == false", text)
        self.assertIn("earliestSuccessfulWhistleTick = 166", text)

    def test_all34_c2_controls_preserve_actual_causal_anchors(self):
        specs = run.c2_specifications(run.ROOT)
        self.assertEqual(len(specs), 34)
        self.assertEqual(len({row[0] for row in specs}), 34)
        self.assertEqual(sum(row[0].startswith("repair-") for row in specs), 9)
        self.assertEqual(specs[0][0], "carrier-probability45")
        self.assertEqual(specs[24][0], "receiver-latch-too-late")
        self.assertEqual(specs[-1][0], "repair-hides-invalid-receiver")
        for identity, path, old, new, field in specs:
            with self.subTest(identity=identity):
                self.assertEqual((run.ROOT / path).read_bytes().count(old.encode()), 1)
                self.assertNotEqual(old, new)
                self.assertGreater(len(field), 20)
        source = (run.SUITE / "run.py").read_text()
        self.assertIn("for name, relative, anchor, replacement, assertion in c2_specifications(ROOT)", source)
        self.assertIn('execute("baseline-c2", "c2")', source)
        self.assertIn('execute("final-c2", "c2")', source)

    def test_c2_exact34934_native_inventory_and_data_pins_are_required(self):
        row = dict(self.summary(), assertions=34934, production_profile="original-teams-contact281-c2",
                   actual_animation_clips=281, actual_animation_frames=26230,
                   c2_animation_sha256=run.C2_ANIMATION_SHA,
                   c2_callback_data_sha256=run.C2_DATA_SHA, c2_hit_extension_sha256=run.C2_HIT_SHA,
                   c2_bounded_evidence={"native_callback_cases": 2146, "native_speech_cases": 8000})
        run.require_pass(0, row, "c2")
        for field, value in (("assertions", 34932), ("c2_callback_data_sha256", "stale"),
                             ("c2_hit_extension_sha256", run.SOUND_SHA),
                             ("c2_bounded_evidence", {"native_callback_cases": 2145, "native_speech_cases": 8000}),
                             ("c2_bounded_evidence", {"native_callback_cases": 2146, "native_speech_cases": 7999})):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                run.require_pass(0, dict(row, **{field: value}), "c2")

    def test_c2_persistent_preparation_has_no_ignored_script_dependency(self):
        current = (run.SUITE / "C2CallbackChecks.cs").read_text()
        self.assertNotIn('".squad/.scratch/c2-original-oracle.py"', current)
        self.assertNotIn("BLITZ_C2_REUSE_NATIVE", current)
        self.assertIn("SourceC2Composition.CreateSimulation(c2Library, real.ReferencePivots", current)
        self.assertIn('Path.Combine(c2NativeRoot, "negative-target", "trap-receipt.json")', current)
        preparer = (run.SUITE / "prepare_c2.py").read_text()
        self.assertIn("tools/animation/prepare_c2_profile.py", preparer)
        self.assertIn('HERE / "c2_oracle.py"', preparer)
        self.assertIn('HERE / "c2_negative_target_oracle.py"', preparer)
        self.assertNotIn("c2-prepare-inputs.py", preparer)
        self.assertNotIn("c2-validate.py", preparer)

    def test_real_mutation_anchors_are_unique(self):
        self.assertEqual(11, len(run.MUTANTS))
        self.assertEqual(9, len(run.BOUNDARY_MUTANTS))
        self.assertEqual(7, len(run.ROUTE_MUTANTS))
        all_mutants = run.MUTANTS + run.BOUNDARY_MUTANTS + run.ROUTE_MUTANTS
        self.assertEqual(27, len({row[0] for row in all_mutants}))
        for name, file, anchor, replacement, mode, assertion in all_mutants:
            path = run.ROOT / "Assets/Scripts/Gameplay" / file
            with self.subTest(name=name):
                self.assertEqual(path.read_bytes().count(anchor.encode()), 1)
                self.assertNotEqual(anchor, replacement)
                self.assertIn(mode, ("kernel", "verify", "heading-boundary", "movement-bearing", "route-target"))
                self.assertGreater(len(assertion), 12)

    def test_boundary_modes_have_exact_counts_and_complete_original13_not_partial_labels(self):
        self.assertEqual(598039, run.EXPECTED_ASSERTIONS["heading-boundary"])
        self.assertEqual(22107, run.EXPECTED_ASSERTIONS["movement-bearing"])
        workspace = run.ARTIFACTS / ("movement-root-unit-" + uuid.uuid4().hex)
        workspace.mkdir(parents=True)
        try:
            with self.assertRaises(FileNotFoundError):
                run.movement_original_hashes(workspace)
            target = workspace / "player.c"
            target.write_bytes(b"owned bad original input guard")
            with self.assertRaisesRegex(RuntimeError, "Original13 movement-bearing blob pin differs: player.c"):
                run.movement_original_hashes(workspace)
        finally:
            shutil.rmtree(workspace)

    def test_route_target_uses_exact_qualified_P1_fourteen_not_movement_thirteen(self):
        recipe = run.json.loads((run.ROOT / "tests/playbooks/pins.json").read_bytes())
        self.assertEqual(28052, run.EXPECTED_ASSERTIONS["route-target"])
        self.assertEqual(recipe["blobs"], run.route_recipe_pins(recipe))
        self.assertEqual(14, len(run.ROUTE_SOURCE_PINS))
        self.assertIn("PlayStuf.c", run.ROUTE_SOURCE_PINS)
        self.assertIn("playbook.c", run.ROUTE_SOURCE_PINS)
        self.assertNotIn("plyrinfo.c", run.ROUTE_SOURCE_PINS)
        self.assertNotIn("include/handicap.h", run.ROUTE_SOURCE_PINS)
        self.assertEqual({"route-old-canonical-guard-caller", "route-producer-drops-raw",
                          "route-canonical-instead-raw-consumer", "route-raw-minus-one-as-missing",
                          "route-circular-fold-consumer", "route-raw-coherence-removed",
                          "route-unqualified-raw-minus1024"}, {row[0] for row in run.ROUTE_MUTANTS})
        for key, value in (("repository", "unqualified/source"), ("revision", "0" * 40),
                           ("tree", "0" * 40), ("profile", dict(recipe["profile"], use_old_qb=True)),
                           ("profile", dict(recipe["profile"], ticks_per_second=57.0))):
            changed = copy.deepcopy(recipe)
            changed[key] = value
            with self.subTest(key=key), self.assertRaisesRegex(RuntimeError, "provenance/profile/exact pins"):
                run.route_recipe_pins(changed)
        changed = copy.deepcopy(recipe)
        changed["blobs"]["drone.c"] = "0" * 40
        with self.assertRaisesRegex(RuntimeError, "exact pins"):
            run.route_recipe_pins(changed)
        changed = copy.deepcopy(recipe)
        changed["blobs"]["playstuf.c"] = changed["blobs"].pop("PlayStuf.c")
        with self.assertRaisesRegex(RuntimeError, "exact pins"):
            run.route_recipe_pins(changed)
        source = (run.SUITE / "run.py").read_text()
        self.assertIn('"--route-original-root", type=Path, required=True', source)
        self.assertIn("MUTANTS + BOUNDARY_MUTANTS + ROUTE_MUTANTS", source)
        self.assertIn('for mode in ("heading-boundary", "movement-bearing", "route-target")', source)
        self.assertIn('for mode in ("inputs", "verify", "sound-short", "heading-boundary", "movement-bearing", "route-target")', source)

    def test_route_original_missing_extra_corrupt_same_count_and_symlink_fail_closed(self):
        workspace = run.ARTIFACTS / ("route-root-unit-" + uuid.uuid4().hex)
        workspace.mkdir(parents=True)
        try:
            with self.assertRaisesRegex(RuntimeError, "inventory differs: missing="):
                run.route_original_hashes(workspace)
            for name in run.ROUTE_SOURCE_PINS:
                target = workspace / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(b"owned invalid-input unit guard; never native compiled")
            with self.assertRaisesRegex(RuntimeError, "Git blob pin differs: playbook.c"):
                run.route_original_hashes(workspace)
            extra = workspace / "not-in-qualified-P1.c"
            extra.write_bytes(b"owned extra-file guard")
            with self.assertRaisesRegex(RuntimeError, "unexpected=.*not-in-qualified-P1.c"):
                run.route_original_hashes(workspace)
            extra.unlink()
            canonical = workspace / "PlayStuf.c"
            canonical.rename(workspace / "playstuf.c")
            with self.assertRaisesRegex(RuntimeError, "missing=.*PlayStuf.c.*unexpected=.*playstuf.c"):
                run.route_original_hashes(workspace)
            (workspace / "playstuf.c").rename(canonical)
            link = workspace / "root-link"
            link.symlink_to(workspace, target_is_directory=True)
            with self.assertRaisesRegex(RuntimeError, "symlink"):
                run.route_original_hashes(link)
        finally:
            shutil.rmtree(workspace)

    def test_original_source_pin_is_not_a_status_flag(self):
        workspace = run.ARTIFACTS / ("runner-unit-" + uuid.uuid4().hex)
        workspace.mkdir(parents=True)
        try:
            target = workspace / "detect.c"
            target.write_text("not the pinned original source\n")
            with self.assertRaisesRegex(ValueError, "Original contact source pin differs: detect.c"):
                source.read(workspace)
            self.assertEqual(target.read_text(), "not the pinned original source\n")
        finally:
            shutil.rmtree(workspace)


if __name__ == "__main__":
    unittest.main()
