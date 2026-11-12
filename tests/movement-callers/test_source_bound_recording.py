import copy
import json
from pathlib import Path
import unittest
from unittest.mock import patch

import prepare_public_replay as public
import source_bound_recording as recorder


HERE = Path(__file__).resolve().parent


class SourceBoundRecordingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.v4 = public.load_fixture(
            HERE / "fixtures" / (public.CORE_CURRENT_CASE + ".jsonl.gz"),
            public.CORE_CURRENT_CASE)
        cls.old_choice = public.decode(cls.v4.splitlines()[4180])["command"]

    def test_retained_v4_is_rejected_against_its_actual_public_capture_side(self):
        evidence = public.decode((HERE / "fixtures/source-bound-v5-retained-v4-negative.json").read_bytes())
        self.assertEqual(public.sha(self.v4), evidence["public_sha256"])
        self.assertEqual(evidence["capture_host"], 4180)
        self.assertEqual(evidence["dispatch_host"], 4181)
        self.assertEqual(evidence["source_tick"], 3972)
        self.assertEqual(evidence["capture"]["possessed_side"], evidence["dispatch"]["possessed_side"])
        with self.assertRaisesRegex(ValueError, "Wrong-side public menu choice at capture host 4180"):
            recorder.validate_menu_snapshot(
                self.old_choice,
                {"Phase": evidence["capture"]["phase"],
                 "Possession": evidence["capture"]["possessed_side"]}, 4180)

    def test_side_is_taken_from_each_matching_public_snapshot_not_starting_possession(self):
        defense = self.old_choice
        recorder.validate_menu_snapshot(defense, {"Phase": 0, "Possession": 1}, 42)
        offense = copy.deepcopy(defense)
        offense["StationInputs"]["Stations"][0]["Command"]["Play"].update(
            Playbook=0, PlaySymbol="play_hail_mary")
        recorder.validate_menu_snapshot(offense, {"Phase": 0, "Possession": 0}, 43)
        with self.assertRaisesRegex(ValueError, "Wrong-side"):
            recorder.validate_menu_snapshot(offense, {"Phase": 0, "Possession": 1}, 44)
        offense["StationInputs"]["Stations"][0]["StationId"] = 1
        with self.assertRaisesRegex(ValueError, "captain"):
            recorder.validate_menu_snapshot(offense, {"Phase": 0, "Possession": 0}, 45)

    def test_old_source_generation_cannot_be_reused_even_with_same_controller(self):
        production = {"Gameplay.cs": b"current source"}
        harness = {"Controller.cs": b"unchanged public controller"}
        policy = dict(productionSourceSha256=recorder.source_pins(production),
                      sourceManifestSha256=recorder.manifest(recorder.source_pins(production)),
                      harnessSha256=recorder.source_pins(harness),
                      harnessManifestSha256=recorder.manifest(recorder.source_pins(harness)),
                      frozenCodeSha256=recorder.source_pins({**production, **harness}))
        recorder.validate_source_binding(policy, production, harness)
        with self.assertRaisesRegex(ValueError, "old vector cannot be relabeled"):
            recorder.validate_source_binding(policy, {"Gameplay.cs": b"previous generation"}, harness)
        with self.assertRaisesRegex(ValueError, "source generation changed"):
            recorder.validate_source_binding(policy, production, {"Controller.cs": b"retuned policy"})

    def test_authoring_does_not_replace_a_wrong_side_command_with_a_valid_one(self):
        raw = json.dumps(dict(host=0, command=self.old_choice)).encode() + b"\n"
        receipt = {"publicInputSha256": public.sha(raw)}
        with self.assertRaisesRegex(ValueError, "Exactly12000"):
            recorder.validate_capture(raw, [], receipt)
        wrong_hash = dict(receipt, publicInputSha256="0" * 64)
        with self.assertRaisesRegex(ValueError, "differ from the actual"):
            recorder.validate_capture(raw, [], wrong_hash)
        invalid_phase = {"Phase": 1, "Possession": 1}
        with self.assertRaisesRegex(ValueError, "never repair, discard, or reassign"):
            recorder.validate_menu_snapshot(self.old_choice, invalid_phase, 0)

    def test_export_requires_the_same_observed_command_not_a_corrected_row(self):
        command = copy.deepcopy(self.old_choice)
        command["StationInputs"]["Stations"][0]["IsSpecified"] = True
        raw = json.dumps(dict(host=0, command=command)).encode() + b"\n"
        receipt = {"publicInputSha256": public.sha(raw)}
        observation = dict(host=0, command=command, visible={"Phase": 0, "Possession": 1},
                           beforeSourceTick=0, SourceTick=0)
        with patch.object(public, "HOSTS", 1):
            projected, binding = recorder.validate_capture(raw, [json.dumps(observation)], receipt)
            self.assertEqual(binding["menuChoices"], 1)
            self.assertEqual(public.decode(projected)["command"]["StationInputs"]["Stations"][0]
                             ["Command"]["Play"]["PlaySymbol"], "play_suicide_blitz")
            observation["command"] = copy.deepcopy(command)
            observation["command"]["StationInputs"]["Stations"][0]["Command"]["Play"].update(
                Playbook=0, PlaySymbol="play_hail_mary")
            with self.assertRaisesRegex(ValueError, "SAME ordered public pre-Step snapshot"):
                recorder.validate_capture(raw, [json.dumps(observation)], receipt)

    def test_external_output_requires_the_explicit_exact_session_recovery_child(self):
        scope = Path("/session-state/declared/files/io-recovery-declared/new-author")
        with patch.object(Path, "resolve", lambda path: path), patch.object(Path, "exists", return_value=False):
            with self.assertRaisesRegex(ValueError, "exact explicitly assigned"):
                recorder.author_output(scope)
            self.assertEqual(recorder.author_output(scope, scope), scope)
            with self.assertRaisesRegex(ValueError, "exact explicitly assigned"):
                recorder.author_output(scope / "unassigned-child", scope)
            with self.assertRaisesRegex(ValueError, "exact explicitly assigned"):
                recorder.author_output(Path("/other-files/new-author"), Path("/other-files/new-author"))

    def test_real_v5_fixture_is_separate_and_cannot_fall_back_to_v4(self):
        fixture = HERE / "fixtures" / (public.SOURCE_BOUND_CASE + ".jsonl.gz")
        data = public.load_fixture(fixture, public.SOURCE_BOUND_CASE)
        recipe = public.source_bound_recipe()
        self.assertEqual(len(data.splitlines()), 12000)
        self.assertEqual(public.sha(data), recipe["jsonl"]["sha256"])
        self.assertNotEqual(data, self.v4)
        with self.assertRaisesRegex(ValueError, "immutable public command identity"):
            public.load_fixture(HERE / "fixtures" / (public.CORE_CURRENT_CASE + ".jsonl.gz"),
                                public.SOURCE_BOUND_CASE)

    def test_source_bound_recipe_rejects_relabeling_with_recomputed_digest(self):
        recipe_path = HERE / "fixtures" / (public.SOURCE_BOUND_CASE + ".recipe.json")
        actual = public.decode(recipe_path.read_bytes())
        old_recipe = public.current_recipe(public.CORE_CURRENT_CASE)
        for field, value in (("source_manifest_sha256", old_recipe["source_manifest_sha256"]),
                             ("source_count", 103), ("station_layout", 4),
                             ("source_compatibility", "strict"), ("native_equivalent", True)):
            changed = copy.deepcopy(actual)
            changed[field] = value
            raw = json.dumps(changed).encode()
            with self.subTest(field=field), patch.object(Path, "read_bytes", return_value=raw), \
                    patch.object(public, "SOURCE_BOUND_RECIPE_SHA256", public.sha(raw)):
                with self.assertRaisesRegex(ValueError, "source-bound public recording contract"):
                    public.source_bound_recipe(recipe_path)

    def test_started_authority_cannot_execute_another_gameplay_window(self):
        with patch.object(Path, "read_bytes", return_value=b"{}"), \
                patch.object(Path, "exists", return_value=True):
            with self.assertRaisesRegex(ValueError, "never retry or extend"):
                recorder.author(Path("declared-author/policy.json"))


if __name__ == "__main__":
    unittest.main()
