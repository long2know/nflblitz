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
        retained = 808 if mode == "controlled" else RUNNER.COMPLETE_ASSERTIONS if mode == "complete" else RUNNER.BOUNDED_ASSERTIONS
        cache = dict(RUNNER.CACHE_TARGET_COUNTS)
        row = {"schema": "source-movement-caller-checks/v1",
                "status": "bounded-passed" if mode == "bounded" else "passed",
                "assertions": retained if mode == "controlled" else retained + cache["AddedAssertions"],
                "retained_M1_assertions": retained,
                "actual_Hurdle_VM_scripts": 16, "actual_MaybeDive_windows_per_hurdle": 7,
                "actual_Core_post_VM_physics_cases": 664, "actual_Core_LEAPATT_operand_bytes": 36,
                "whole_M1_or_full_gameplay_qualified": False,
                "known_R_SKIP_asset_missing": mode == "bounded",
                "complete_publication_qualification": mode == "complete",
                "actual_animation_library_manifest": RUNNER.MOVEMENT_SELECTED_SHA if mode == "bounded"
                else RUNNER.MOVEMENT_ANIMATION_SHA,
                "public_smoke_trace": RUNNER.SMOKE_TRACE, "public_smoke_source_tick": 2781,
                "public_smoke_R_SKIP_frames_after2299": 2, "public_smoke_cache_assertions": 36120,
                "public_smoke_resolved_sound_dispatches": 13, "public_smoke_sound_random_draws": 17,
                "public_smoke_R_SKIP_instance_advanced": True}
        if mode != "controlled":
            row["source_time_cache"] = cache
        return row

    def cache_summary(self):
        return {"schema": "source-movement-caller-checks/v1", "status": "cache-contract-passed",
                "mode": "targeted-source474-cache-contract-only", "complete_publication_qualification": False,
                "whole_M1_or_full_gameplay_qualified": False,
                "actual_animation_library_manifest": RUNNER.CONTACT_ANIMATION_SHA,
                "public_smoke_trace": RUNNER.SMOKE_TRACE, "public_smoke_source_tick": 474,
                "retained_old_smoke_assertions": 422, "assertions": RUNNER.CACHE_TARGET_ASSERTIONS,
                "source_time_cache": dict(RUNNER.CACHE_TARGET_COUNTS)}

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

    def test_new272_is_explicit_and_does_not_relabel_156_or157(self):
        for mode in ("complete", "controlled"):
            row = self.summary(mode)
            with self.subTest(mode=mode), self.assertRaises(RuntimeError):
                RUNNER.require_summary(0, row, mode, RUNNER.CONTACT_ANIMATION_SHA)
            row["actual_animation_library_manifest"] = RUNNER.CONTACT_ANIMATION_SHA
            RUNNER.require_summary(0, row, mode, RUNNER.CONTACT_ANIMATION_SHA)
        RUNNER.require_summary(0, self.summary("bounded"), "bounded", RUNNER.CONTACT_ANIMATION_SHA)

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

    def test_complete_sound_m1_requires_real_post_gap_pose_advancement_cache_and_rng(self):
        for field, value in (("public_smoke_trace", "old-unverified-LCG-window"),
                             ("public_smoke_source_tick", 2299),
                             ("public_smoke_R_SKIP_frames_after2299", 1),
                             ("public_smoke_R_SKIP_instance_advanced", False),
                             ("public_smoke_cache_assertions", 35909),
                             ("public_smoke_resolved_sound_dispatches", 0),
                             ("public_smoke_sound_random_draws", 0)):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                row = self.summary("complete")
                row[field] = value
                RUNNER.require_summary(0, row, "complete")

    def test_targeted_source474_cache_proof_is_exact_and_never_full_m1_green(self):
        RUNNER.require_cache_summary(0, self.cache_summary())
        for field, value in (("assertions", RUNNER.CACHE_TARGET_ASSERTIONS - 1),
                             ("retained_old_smoke_assertions", 421), ("status", "passed"),
                             ("public_smoke_source_tick", 473), ("complete_publication_qualification", True),
                             ("actual_animation_library_manifest", RUNNER.MOVEMENT_ANIMATION_SHA)):
            row = self.cache_summary()
            row[field] = value
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                RUNNER.require_cache_summary(0, row)
        for field, value in RUNNER.CACHE_TARGET_COUNTS.items():
            row = self.cache_summary()
            row["source_time_cache"][field] = False if isinstance(value, bool) else value - 1
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                RUNNER.require_cache_summary(0, row)
        for code in (1, 2, 134):
            with self.subTest(code=code), self.assertRaises(RuntimeError):
                RUNNER.require_cache_summary(code, self.cache_summary())

    def test_observer_assertions_cannot_replace_any_retained_full_m1_contract(self):
        for mode in ("complete", "bounded"):
            for field, value in (("CurrentRows", 0), ("PreviousRows", 0), ("ReferenceRows", 0),
                                 ("CurrentMatrixFrames", 0), ("OrdinaryRows", 0),
                                 ("ContactFrames", 0), ("DisplacedActors", 0), ("source474Observed", False)):
                row = self.summary(mode)
                row["source_time_cache"][field] = value
                with self.subTest(mode=mode, field=field), self.assertRaises(RuntimeError):
                    RUNNER.require_summary(0, row, mode)
            row = self.summary(mode)
            row["retained_M1_assertions"] -= 1
            row["assertions"] -= 1
            with self.subTest(mode=mode), self.assertRaises(RuntimeError):
                RUNNER.require_summary(0, row, mode)

    def test_three_new_compiled_cache_controls_do_not_replace_the_original_eight(self):
        self.assertEqual(8, len(RUNNER.MUTANTS))
        self.assertEqual({"refresh-after-contact", "wrong-current-root-basis", "drop-contact-cache-checks"},
                         {row[0] for row in RUNNER.CACHE_MUTANTS})
        for name, kind, path, old, new, field in RUNNER.CACHE_MUTANTS:
            self.assertIn(kind, ("production", "harness"))
            self.assertNotEqual(old, new)
            self.assertGreater(len(field), 30)
            if kind == "harness":
                self.assertEqual(1, (RUNNER.ROOT / path).read_bytes().count(old.encode()), name)
        text = (SUITE / "run.py").read_text()
        main_body = text[text.index("def main():"):]
        self.assertLess(main_body.index("return cache_contract(args)"), main_body.index("before = hashes()"))
        self.assertIn("tests/audio/vm/SourceSoundVmSources.props", text)
        self.assertIn("Cache control compile errors are NOT kills", text)
        self.assertIn('"--cache-contract-current"', text)
        self.assertIn('result["required_cache_contract"]', text)
        self.assertIn("current_cache_snapshot(snapshot)", text)


if __name__ == "__main__":
    unittest.main()
