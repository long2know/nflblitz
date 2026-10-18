#!/usr/bin/env python3
"""Guard strict summary/mutant layers; not a substitute for actual M1 caller evidence."""
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock


SUITE = Path(__file__).resolve().parent
sys.path.insert(0, str(SUITE))
SPEC = importlib.util.spec_from_file_location("movement_caller_runner", SUITE / "run.py")
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
COMPAT_SPEC = importlib.util.spec_from_file_location("compatibility_runner", SUITE / "compatibility.py")
COMPAT = importlib.util.module_from_spec(COMPAT_SPEC)
COMPAT_SPEC.loader.exec_module(COMPAT)


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


class CarrierlessCompatibilityGuards(unittest.TestCase):
    def setUp(self):
        (SUITE / ".artifacts").mkdir(exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(dir=SUITE / ".artifacts")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.cases = []
        kinds = ["Pass", "FieldGoal", "Kickoff", "Punt", "Fumble", "Bobble", "Lateral"]
        for station in range(4):
            actor = [4, 11, 5, 12][station]
            for kind in kinds:
                for held in (False, True):
                    if not held and kind in kinds[:3]:
                        continue
                    for closest in (False, True):
                        self.cases.append({"input": {
                            "actor": actor, "team": actor // 7, "station": station,
                            "carrier": (10 if actor < 7 else 3) if held else -1,
                            "ballKind": kind, "offenseSide": 1 if actor < 7 else 0,
                            "playOver": False, "onScreen": False, "intendedReceiver": -1, "tx": 0, "tz": 0,
                            "players": [{"x": 100 * (i % 7 + 1), "z": 0, "station": -1, "unint": False}
                                        for i in range(14)], "angles": [0] * 196},
                            "selected": actor if closest else 0 if actor < 7 else 7})

    def check(self, command):
        path = self.root / "cases.json"
        path.write_text(json.dumps(self.cases))
        return COMPAT.check_carrierless_oracle(self.root / "never-executed", path, command, "synthetic-guard-only")

    def test_original_controls_are_unchanged_and_new_thirteen_anchors_are_unique(self):
        self.assertEqual(12, len(COMPAT.MUTANTS))
        self.assertEqual(13, len(COMPAT.CARRIERLESS_MUTANTS))
        for name, filename, old, new, expected in COMPAT.CARRIERLESS_MUTANTS:
            with self.subTest(name=name):
                self.assertEqual(1, (COMPAT.ROOT / filename).read_text().count(old))
                self.assertNotEqual(old, new)
                self.assertGreater(len(expected), 30)

    def test_original_source_pin_rejects_before_compiling_any_adapter(self):
        source = self.root / "player.c"
        source.write_text("not original source")
        command = Mock()
        with self.assertRaisesRegex(ValueError, "exact original player.c"):
            COMPAT.carrierless_oracle(source, self.root, command)
        command.assert_not_called()

    def test_oracle_rejects_undefined_inputs_incomplete_shapes_and_duplicate_cases(self):
        for mutate in (
            lambda rows: rows[0]["input"].update(carrier=-1),
            lambda rows: rows[0]["input"].update(carrier=-2, ballKind="Punt"),
            lambda rows: rows[0]["input"].update(carrier=14),
            lambda rows: rows[0]["input"].update(actor=-1),
            lambda rows: rows[0]["input"].update(team=2),
            lambda rows: rows[0]["input"].update(station=4),
            lambda rows: rows[0]["input"].update(offenseSide=2),
            lambda rows: rows[0]["input"].update(angles=[0] * 195),
            lambda rows: rows[0]["input"]["players"][0].update(unint=True),
            lambda rows: rows.__setitem__(1, rows[0]),
        ):
            original = json.loads(json.dumps(self.cases))
            mutate(self.cases)
            command = Mock()
            with self.subTest(mutate=mutate), self.assertRaises(ValueError):
                self.check(command)
            command.assert_not_called()
            self.cases = original

    def test_oracle_requires_exact_successful_complete_c_output_not_a_zip_prefix(self):
        lines = [str(row["selected"]) for row in self.cases]
        accepted = Mock(return_value=SimpleNamespace(returncode=0, stdout="\n".join(lines) + "\n"))
        self.assertEqual("passed-defined-domain-only", self.check(accepted)["status"])
        for code, output in (
            (1, lines), (0, lines[:-1]), (0, lines + ["0"]), (0, ["999"] + lines[1:]),
        ):
            command = Mock(return_value=SimpleNamespace(returncode=code, stdout="\n".join(output) + "\n"))
            with self.subTest(code=code, count=len(output)), self.assertRaises(RuntimeError):
                self.check(command)


if __name__ == "__main__":
    unittest.main()
