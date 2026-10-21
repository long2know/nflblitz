import importlib.util
from pathlib import Path
import re
import sys
import unittest

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
SPEC = importlib.util.spec_from_file_location("portable_kick_runner", HERE / "run.py")
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)

METHOD_SPEC = importlib.util.spec_from_file_location(
    "kick_meter_source_methods", RUNNER.ROOT / "tests/interface/stage_followup.py")
METHODS = importlib.util.module_from_spec(METHOD_SPEC)
METHOD_SPEC.loader.exec_module(METHODS)


class KickRunnerContracts(unittest.TestCase):
    def test_controls_edit_real_unique_production_anchors_and_name_numeric_or_caller_outputs(self):
        self.assertEqual(len(RUNNER.MUTATIONS), 17)
        self.assertEqual(len({entry[0] for entry in RUNNER.MUTATIONS}), 17)
        for identity, section, edits, expected in RUNNER.MUTATIONS:
            with self.subTest(control=identity):
                self.assertIn(section, ("meters", "physics", "callers", "disabled", "options", "lifecycle"))
                self.assertGreater(len(expected), 12)
                self.assertNotIn(expected, ("failed", "error", "exception", "status"))
                source = {}
                for name, old, new in edits:
                    self.assertTrue(name.startswith("Assets/Scripts/"))
                    data = source.get(name, (RUNNER.ROOT / name).read_text())
                    self.assertEqual(data.count(old), 1, identity)
                    self.assertNotEqual(old, new)
                    source[name] = data.replace(old, new)

    def test_real_compile_closure_includes_existing_cabinet_and_HUD_consumers(self):
        paths = {str(p.relative_to(RUNNER.ROOT)) for p in RUNNER.production_paths()}
        self.assertEqual(len(paths), 78)
        self.assertIn("Assets/Scripts/Gameplay/GameSimulation.Possession.cs", paths)
        self.assertIn("Assets/Scripts/Gameplay/SourcePlaySelectionTimer.cs", paths)
        self.assertIn("Assets/Scripts/Contracts/SourceKickMeterContracts.cs", paths)
        self.assertIn("Assets/Scripts/Gameplay/SourceKickMeterProcess.cs", paths)
        self.assertIn("Assets/Scripts/Gameplay/GameSimulation.FireSpfx.cs", paths)
        self.assertIn("Assets/Scripts/Contracts/SourceFireSpfxContracts.cs", paths)
        self.assertIn("Assets/Scripts/AssetImport/SourceFireSpfxData.cs", paths)
        self.assertIn("Assets/Scripts/AssetImport/SourceTeamPoppedHeadData.cs", paths)
        self.assertIn("Assets/Scripts/AssetImport/SourceFreeBallData.cs", paths)
        self.assertIn("Assets/Scripts/AssetImport/SourceNormalCameraData.cs", paths)
        self.assertIn("Assets/Scripts/Contracts/SourceCameraContracts.cs", paths)
        self.assertEqual(len(RUNNER.EXTRA_PURE), 5)
        for name in RUNNER.EXTRA_PURE:
            self.assertTrue((RUNNER.ROOT / name).is_file(), name)

    def test_runtime_exposes_independent_opt_in_and_real_typed_provider(self):
        runtime = (RUNNER.ROOT / "Assets/Scripts/Runtime/BlitzRuntime.cs").read_text()
        services = (RUNNER.ROOT / "Assets/Scripts/Runtime/SourceInterfaceServices.cs").read_text()
        awake = METHODS.compact(METHODS.method(runtime, "Awake"))
        create = METHODS.compact(METHODS.method(services, "CreateSimulation"))
        self.assertEqual(awake.count(
            "SourceKickMeterModekickMeters=SourceKickMeterOptions.FromUrl(Application.absoluteURL);"), 1)
        self.assertEqual(awake.count(
            "sourceServices.CreateSimulation(prefab,compatibility,fireEffects:fire,kickMeters:kickMeters);"), 1)
        self.assertIn(METHODS.compact("""
            if (prefab == null) {
                if (kickMeters != SourceKickMeterMode.Disabled)
                    throw new SourceInterfaceException("Portable-authored kick meters require an actual source C1/C2/M1 profile, not graybox.");
        """), awake)
        self.assertIn(METHODS.compact("""
            public GameSimulation CreateSimulation(SourcePlayerRig prefab,
                SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
                SourceFireGameplayConfiguration fireEffects = null,
                SourceKickMeterMode kickMeters = SourceKickMeterMode.Disabled) {
        """), METHODS.compact(METHODS.uncomment(services)))
        invalid_mode = METHODS.compact("""
            if (kickMeters != SourceKickMeterMode.Disabled && kickMeters != SourceKickMeterMode.PortableAuthored)
                throw new ArgumentOutOfRangeException(nameof(kickMeters));
        """)
        self.assertIn(invalid_mode, create)
        self.assertIn("SourceKickMeterMetricskickMetrics=", create)
        self.assertEqual(re.findall(r"kickMetrics=([^;]+);", create), [
            "kickMeters==SourceKickMeterMode.PortableAuthored"
            "?originalHud.Data.CreatePortableAuthoredKickMeterMetrics():null"
        ], "SourceInterfaceServices.CreateSimulation must opt in independently and leave disabled metrics null.")
        self.assertLess(create.index(invalid_mode), create.index("SourceKickMeterMetricskickMetrics="))

        common = ("animations,pivots,playbook,Configuration(),teams.Rosters,PresentationSettings,"
                  "MovementData,MovementSettings,SoundVmData")
        m1 = common + ",bootstrap,compatibility:compatibility,fireEffects:fireEffects,kickMeterMetrics:kickMetrics)"
        expected = [
            "SourceC2Composition.CreatePreSnapSwitchSimulation(" + m1,
            "SourceC2Composition.CreateM1Simulation(" + m1,
            "SourceC2Composition.CreateSimulation(" + common + ",bootstrap,kickMeterMetrics:kickMetrics)",
            "SourceContactComposition.CreateSimulation(" + common + ",kickMeterMetrics:kickMetrics)",
        ]
        self.assertEqual(re.findall(r"\breturn([^;]+);", create), expected,
                         "SourceInterfaceServices.CreateSimulation must forward typed kick metrics through every actual factory.")
        self.assertIn("if(Profile.UsesC2Data){SourceC2Bootstrapbootstrap="
                      "SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(C2Data);", create)
        for profile, call in zip(("IsContact282C2M1PreSnapSwitch", "IsContact281C2M1"), expected):
            with self.subTest(profile=profile):
                self.assertIn("if(Profile." + profile + ")return" + call + ";", create)
        self.assertTrue(create.endswith("return" + expected[2] + ";}return" + expected[3] + ";"),
                        "Old C2 and C1 routes must retain their own typed-provider forwarding.")

        c2 = (RUNNER.ROOT / "Assets/Scripts/Runtime/SourceC2Composition.cs").read_text()
        arguments = ("animations,referencePivots,playbook,configuration,rosters,presentation,movementData,"
                     "movementSettings,legacySoundFunctions,c2Bootstrap,randomFactory,seed,handicapSettings,"
                     "compatibility,fireEffects,kickMeterMetrics")
        for factory, profile in (
                ("CreateSimulation", "Contact281C2"),
                ("CreateM1Simulation", "Contact281C2M1"),
                ("CreatePreSnapSwitchSimulation", "Contact282C2M1PreSnapSwitch")):
            with self.subTest(factory=factory):
                self.assertEqual(METHODS.compact(METHODS.method(c2, factory)),
                                 "returnCreateChecked(SourceTeamResourceProfile." + profile + "," + arguments + ");",
                                 "SourceC2Composition." + factory + " must retain the same typed kick provider.")
        checked = METHODS.compact(METHODS.method(c2, "CreateChecked"))
        self.assertIn("vargame=newGameSimulation(animations,geometry,playbook,configuration,randomFactory,seed,"
                      "rosters,presentation,movementData,movementSettings,c2Bootstrap,handicapSettings,compatibility,"
                      "fireEffects,kickMeterMetrics);", checked)
        contact = METHODS.compact(METHODS.method(
            (RUNNER.ROOT / "Assets/Scripts/Runtime/SourceContactComposition.cs").read_text(), "CreateSimulation"))
        self.assertIn("vargame=newGameSimulation(animations,geometry,playbook,configuration,randomFactory,seed,"
                      "rosters,presentation,movementData,movementSettings,handicapSettings:handicapSettings,"
                      "kickMeterMetrics:kickMeterMetrics);", contact)

        provider = METHODS.compact(METHODS.method(
            (RUNNER.ROOT / "Assets/Scripts/AssetImport/SourceInterfaceData.cs").read_text(),
            "CreatePortableAuthoredKickMeterMetrics"))
        self.assertTrue(provider.startswith(
            "SourceInterfaceInputTables.Require(ContentSha256==SourceKickMeterMetrics.AuthoredInterfaceSha256,"),
            "SourceInterfaceData must reject an unpinned authored HUD before reading dimensions.")
        self.assertIn('SourceInterfaceImagebar=Image("k_bar"),marker=Image("k_marker"),punt=Image("p_meter");', provider)
        self.assertIn('SourceInterfaceInputTables.Require(bar.Source=="img2d/plyrinfo.wif"'
                      '&&marker.Source==bar.Source&&punt.Source==bar.Source,', provider)
        self.assertTrue(provider.endswith(
            "returnnewSourceKickMeterMetrics(ContentSha256,bar.Width,marker.Width,punt.Height);"),
            "The typed provider must use the real authored bar/marker widths and punt height, not native guesses.")

    def test_feature_runner_requires_actual_C_and_all_retained_caller_categories(self):
        text = (HERE / "run.py").read_text()
        for value in ('summary.get("disabledAssertions") != 817', 'summary.get("meterFlows") != 648',
                      'summary.get("physicsCases") != 656', 'summary.get("lifecycleCases") != 39',
                      'summary.get("additionalCases") != 15', 'originalC_shadow_and_launch_compared'):
            self.assertIn(value, text)
        self.assertIn('"0 Warning(s)"', text)
        self.assertIn('"0 Error(s)"', text)
        self.assertIn("baseline != final", text)
        self.assertIn("inconclusive-inputs-changed", text)


if __name__ == "__main__":
    unittest.main()


if __name__ == "__main__":
    unittest.main()
