import copy
import json
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch
import uuid

import original_m1_prevent_exhibition as scenario


class PreventExhibitionGuards(unittest.TestCase):
    def policy(self):
        return dict(
            schema="source-original-M1-prevent-exhibition-frozen-first-window/v1",
            case=scenario.CASE, name=scenario.NAME, version=scenario.VERSION,
            recordingGeneration=scenario.GENERATION,
            baseControllerVersion=scenario.reviewed.VERSION, controllerSource=scenario.v8.CONTROLLER,
            controllerSha256=scenario.v8.CONTROLLER_SHA256, wrapperSource=scenario.WRAPPER,
            worktree=str(scenario.ROOT), authority=str(scenario.AUTHORITY), maxHosts=12000,
            maximumRawValues=32768, maximumInitialWindows=1, maximumAuthoringInvocations=1,
            finalOldM1ScenarioCycle=True, noRetryOrExtension=True, seed=1,
            providerProfile="source-sound-smoke/raw-vector-v1", rawVector=list(scenario.reviewed.RAW),
            rawVectorSha256=scenario.reviewed.RAW_SHA256,
            sourceManifestSha256=scenario.v8.SOURCE_SHA256,
            replayHarnessManifestSha256=scenario.v8.REPLAY_HARNESS_SHA256,
            configuration=scenario.exporter.current_configuration(False),
            configurationSha256=scenario.v8.CONFIGURATION_SHA256,
            startupObject="OriginalM1PreventExhibitionEntry", profile="original-teams-contact281-c2-m1",
            factory="SourceC2Composition.CreateM1Simulation",
            callbackDataSha256=scenario.canonical.C2_M1_DATA_SHA,
            animationSha256=scenario.canonical.C2_ANIMATION_SHA,
            policyDecision=copy.deepcopy(scenario.POLICY_DECISION))

    def test_exact_exhibition_policy_cannot_relabel_failed_v8_or_tune_configuration(self):
        scenario.identity(self.policy())
        for field, value in (
                ("case", scenario.v8.CASE), ("version", scenario.reviewed.VERSION),
                ("recordingGeneration", scenario.v8.GENERATION),
                ("baseControllerVersion", "changed"),
                ("controllerSha256", "0" * 64), ("seed", 2), ("maxHosts", 12001),
                ("maximumRawValues", 32769), ("maximumAuthoringInvocations", 2),
                ("authority", str(scenario.v8.AUTHORITY)), ("finalOldM1ScenarioCycle", False),
                ("profile", "original-teams-contact282-c2-m1-presnap-switch"),
                ("factory", "SourceC2Composition.CreatePreSnapSwitchSimulation"),
                ("rawVector", list(reversed(scenario.reviewed.RAW)))):
            with self.subTest(field=field), self.assertRaises(ValueError):
                scenario.identity(dict(self.policy(), **{field: value}))
        for field, value in (("minimumFieldXInclusive", 74), ("maximumFieldXExclusive", 96),
                             ("cushionYards", 6), ("preserveOriginalTurbo", False),
                             ("atOrAboveCushion", "choose a different lane after observing RNG")):
            policy = self.policy()
            policy["policyDecision"][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                scenario.identity(policy)

    def test_separate_wrapper_reuses_public_projection_and_never_touches_private_model(self):
        production, harness, adapter = scenario.inventories()
        self.assertEqual((len(production), len(harness), len(adapter)), (103, 12, 2))
        self.assertEqual(scenario.sha(harness[scenario.v8.CONTROLLER]), scenario.v8.CONTROLLER_SHA256)
        source = adapter[scenario.WRAPPER].decode()
        wrapper = source[source.index("    private sealed class PreventExhibitionController"):
                         source.index("    private static bool SamePreventIntent")]
        self.assertEqual(wrapper.count("controller.Choose(view)"), 1)
        self.assertIn("new OpenSpaceController()", wrapper)
        self.assertIn("view.Phase != GamePhase.LiveBall || view.Possession != TeamId.Away", wrapper)
        for forbidden in ("GameSimulation", "IGameState", "SourceTick", "Animation", "Random",
                          "rawCursor", ".Model", ".Step(", "TwoPart", "Clock", "Score", ".Velocity"):
            self.assertNotIn(forbidden, wrapper)
        self.assertIn("OpenSpacePolicyChecks()", source)
        author = source[source.index("    private static int AuthorOriginalM1PreventExhibition"):]
        self.assertLess(author.index("FileMode.CreateNew"), author.index("GameSimulation game = Create("))
        self.assertEqual(author.count("GameSimulation game = Create("), 1)
        self.assertEqual(author.count("game.Step(command)"), 1)
        self.assertIn("if (faults.Count != 0) break;", author)

    def test_consumed_final_invocation_stops_without_another_source_attempt(self):
        work = scenario.ROOT / ".squad/.scratch" / ("prevent-consumed-unit-" + uuid.uuid4().hex)
        work.mkdir(parents=True)
        try:
            (work / "policy.json").write_text(json.dumps(self.policy()))
            (work / "only-authoring-invocation.json").write_text("{}")
            with patch.object(scenario, "AUTHORITY", work), patch.object(scenario, "POLICY", work / "policy.json"), \
                    patch.object(scenario, "check_frozen") as check, \
                    patch.object(scenario, "execute", side_effect=AssertionError("no second authoring attempt")), \
                    self.assertRaisesRegex(ValueError, "consumed"):
                scenario.run()
            check.assert_not_called()
        finally:
            shutil.rmtree(work)

    def test_final_failed_window_is_not_exported_and_export_never_executes_gameplay(self):
        work = scenario.ROOT / ".squad/.scratch" / ("prevent-export-unit-" + uuid.uuid4().hex)
        (work / "capture").mkdir(parents=True)
        try:
            (work / "policy.json").write_text(json.dumps(self.policy()))
            receipt = {"policySha256": "0" * 64}
            receipt_raw = json.dumps(receipt).encode()
            (work / "capture/public-window-result.json").write_bytes(receipt_raw)
            for code, status in ((1, "UNMET-public-R_SKIP"), (2, "STOP-new-source-fault")):
                result = dict(exit=code, status=status, actualAuthoringInvocations=1, maximumInitialWindows=1,
                    receipt=receipt, receiptSha256=scenario.sha(receipt_raw),
                    frozenSourceHarnessToolsInputsAndHistoryUnchanged=True)
                (work / "authoring-result.json").write_text(json.dumps(result))
                with patch.object(scenario, "AUTHORITY", work), \
                        patch.object(scenario, "POLICY", work / "policy.json"), \
                        patch.object(scenario, "check_frozen"), \
                        patch.object(scenario.public, "write_new_or_verify") as write, \
                        patch.object(scenario, "execute", side_effect=AssertionError("export is inputs-only")), \
                        self.assertRaisesRegex(ValueError, "authentic complete positive final exhibition"):
                    scenario.export()
                write.assert_not_called()
        finally:
            shutil.rmtree(work)

    def test_real_compiled_mutation_inventory_targets_boundaries_cushion_turbo_and_delegation(self):
        source = (scenario.ROOT / scenario.WRAPPER).read_text()
        for name, old, new, expected in scenario.POLICY_MUTANTS:
            with self.subTest(name=name):
                self.assertEqual(source.count(old), 1)
                self.assertNotEqual(old, new)
                self.assertIn(expected, source)


if __name__ == "__main__":
    unittest.main()
