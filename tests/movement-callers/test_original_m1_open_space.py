import copy
import json
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch
import uuid

import original_m1_open_space as scenario


class OriginalM1ScenarioGuards(unittest.TestCase):
    def policy(self):
        return dict(
            schema="source-original-M1-open-space-frozen-first-window/v1", case=scenario.CASE,
            recordingGeneration=scenario.GENERATION, name=scenario.reviewed.NAME, version=scenario.reviewed.VERSION,
            worktree=str(scenario.ROOT), authority=str(scenario.AUTHORITY), controllerSource=scenario.CONTROLLER,
            controllerSha256=scenario.CONTROLLER_SHA256, maxHosts=12000, maximumInitialWindows=1,
            maximumAuthoringInvocations=1, noRetryOrExtension=True, seed=1,
            providerProfile="source-sound-smoke/raw-vector-v1", rawVector=list(scenario.reviewed.RAW),
            rawVectorSha256=scenario.reviewed.RAW_SHA256, sourceManifestSha256=scenario.SOURCE_SHA256,
            replayHarnessManifestSha256=scenario.REPLAY_HARNESS_SHA256,
            configuration=scenario.exporter.current_configuration(False),
            configurationSha256=scenario.CONFIGURATION_SHA256, startupObject="OriginalM1OpenSpaceEntry",
            profile="original-teams-contact281-c2-m1", factory="SourceC2Composition.CreateM1Simulation",
            callbackDataSha256=scenario.canonical.C2_M1_DATA_SHA, animationSha256=scenario.canonical.C2_ANIMATION_SHA)

    def test_separate_original_profile_cannot_alias_v7_retune_or_change_authority(self):
        scenario.identity(self.policy())
        for field, value in (
                ("case", scenario.reviewed.CASE), ("recordingGeneration", scenario.reviewed.GENERATION),
                ("profile", "original-teams-contact282-c2-m1-presnap-switch"),
                ("factory", "SourceC2Composition.CreatePreSnapSwitchSimulation"),
                ("callbackDataSha256", scenario.canonical.PRESNAP_DATA_SHA),
                ("controllerSha256", "0" * 64), ("seed", 2), ("maxHosts", 12001),
                ("maximumAuthoringInvocations", 2), ("maximumInitialWindows", 2),
                ("authority", str(scenario.AUTHORITY.parent / "retry")),
                ("rawVector", list(reversed(scenario.reviewed.RAW)))):
            with self.subTest(field=field), self.assertRaises(ValueError):
                scenario.identity(dict(self.policy(), **{field: value}))
        policy = copy.deepcopy(self.policy())
        policy["configuration"]["stationLayout"] = 4
        with self.assertRaises(ValueError):
            scenario.identity(policy)

    def test_original_profile_uses_exact_existing_281_closure_not_successor_provider(self):
        with patch.object(scenario.canonical, "fixture_profile") as profile, \
                patch.object(scenario.canonical, "fixture_hashes", return_value={"contact": "a" * 64}), \
                patch.object(scenario.canonical, "canonical_hashes",
                             return_value={str(n): "b" * 64 for n in range(1121)}) as old, \
                patch.object(scenario.canonical, "successor_hashes",
                             side_effect=AssertionError("enabled282 is not original-M1")):
            data = scenario.inputs(Path("fixture"), Path("sounds"), Path("original-m1"))
            self.assertEqual(len(data), 1122)
            profile.assert_called_once_with(Path("fixture"), "contact272")
            old.assert_called_once_with(Path("original-m1"), "contact281-c2-m1")
            old.return_value["extra"] = "c" * 64
            with self.assertRaisesRegex(ValueError, "Exact original-M1/281 data closure"):
                scenario.inputs(Path("fixture"), Path("sounds"), Path("original-m1"))

    def test_adapter_reuses_unchanged_controller_and_marks_before_sole_game_construction(self):
        production, harness, adapter = scenario.inventories()
        self.assertEqual((len(production), len(harness), len(adapter)), (103, 12, 2))
        self.assertEqual(scenario.sha(harness[scenario.CONTROLLER]), scenario.CONTROLLER_SHA256)
        source = adapter["tests/movement-callers/OriginalM1OpenSpace.cs"].decode()
        self.assertIn("var controller = new OpenSpaceController();", source)
        self.assertNotIn("class OpenSpaceController", source)
        self.assertLess(source.index("FileMode.CreateNew"), source.index("GameSimulation game = Create("))
        self.assertEqual(source.count("game.Step(command)"), 1)
        self.assertEqual(source.count("GameSimulation game = Create("), 1)
        for forbidden in ("game.Model.Possession =", "game.Model.Phase =", "new Random(",
                          "AdvanceAnimation(", "SetValue("):
            self.assertNotIn(forbidden, source)
        self.assertIn("return OpenSpacePolicyChecks();", source)
        self.assertIn("if (faults.Count != 0) break;", source)
        self.assertIn("!game.SourceDefensivePreSnapSwitchEnabled", source)

    def test_export_rejects_failed_unmet_or_wrong_profile_before_writing_public_files(self):
        work = scenario.ROOT / ".squad/.scratch" / ("original-m1-export-unit-" + uuid.uuid4().hex)
        (work / "capture").mkdir(parents=True)
        try:
            (work / "policy.json").write_text(json.dumps(self.policy()))
            receipt = {"policySha256": "0" * 64}
            receipt_raw = json.dumps(receipt).encode()
            (work / "capture/public-window-result.json").write_bytes(receipt_raw)
            for code, status in ((1, "UNMET-public-R_SKIP"), (2, "STOP-new-source-fault")):
                result = dict(exit=code, status=status, actualAuthoringInvocations=1,
                              maximumInitialWindows=1, receipt=receipt,
                              receiptSha256=scenario.sha(receipt_raw),
                              frozenSourceHarnessToolsInputsAndHistoryUnchanged=True)
                (work / "authoring-result.json").write_text(json.dumps(result))
                with patch.object(scenario, "AUTHORITY", work), \
                        patch.object(scenario, "POLICY", work / "policy.json"), \
                        patch.object(scenario, "check_frozen"), \
                        patch.object(scenario.public, "write_new_or_verify") as write, \
                        patch.object(scenario, "execute", side_effect=AssertionError("export must not execute a game")), \
                        self.assertRaisesRegex(ValueError, "authentic complete positive sole"):
                    scenario.export()
                write.assert_not_called()
        finally:
            shutil.rmtree(work)

    def test_consumed_invocation_stops_before_execution_even_without_constructor_marker(self):
        work = scenario.ROOT / ".squad/.scratch" / ("original-m1-marker-unit-" + uuid.uuid4().hex)
        work.mkdir(parents=True)
        try:
            (work / "policy.json").write_text(json.dumps(self.policy()))
            (work / "only-authoring-invocation.json").write_text("{}")
            with patch.object(scenario, "AUTHORITY", work), patch.object(scenario, "POLICY", work / "policy.json"), \
                    patch.object(scenario, "check_frozen") as check, \
                    patch.object(scenario, "execute", side_effect=AssertionError("no second invocation")), \
                    self.assertRaisesRegex(ValueError, "already been consumed"):
                scenario.run()
            check.assert_not_called()
        finally:
            shutil.rmtree(work)

    def test_authentic_exhausted_v8_receipt_cannot_be_promoted_or_aliased_to_enabled_profile(self):
        record = json.loads((scenario.canonical.HERE /
            "provenance/original-m1-v8-failed-20261102.json").read_bytes())
        with self.assertRaisesRegex(ValueError, "no genuine public R_SKIP witness"):
            scenario.validate_window(record["receipt"], record["executionCounts"], [])
        enabled = dict(record["receipt"], profile="original-teams-contact282-c2-m1-presnap-switch",
                       factory="REAL SourceC2Composition.CreatePreSnapSwitchSimulation",
                       sourceData=scenario.canonical.PRESNAP_DATA_SHA)
        with self.assertRaisesRegex(ValueError, "actual Portable M1 first window"):
            scenario.validate_window(enabled, record["executionCounts"], [])

    def test_retained_actual_v4_queued_side_fault_rejects_context_tampering(self):
        record = json.loads((scenario.canonical.HERE /
            "provenance/original-m1-v8-failed-20261102.json").read_bytes())
        context = record["currentV4"]["sourceSide"]
        self.assertEqual(scenario.canonical._m1_side_diagnostic_view(context, "uninstrumented"), context)
        for field in ("guard", "applied_stations", "current_rules"):
            changed = copy.deepcopy(context)
            if field == "guard":
                changed[field]["possession"] = 1
            elif field == "applied_stations":
                changed[field][0]["symbol"] = "play_da_bomb"
            else:
                changed[field]["offense_side"] = 1
            with self.subTest(field=field):
                result = scenario.canonical._m1_side_diagnostic_view(changed, "uninstrumented")
                self.assertEqual(result["status"], "conflicting-evidence")


if __name__ == "__main__":
    unittest.main()
