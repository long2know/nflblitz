import importlib.util
from pathlib import Path
import sys
import unittest

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
SPEC = importlib.util.spec_from_file_location("portable_kick_runner", HERE / "run.py")
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


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
        self.assertEqual(len(paths), 73)
        self.assertIn("Assets/Scripts/Contracts/SourceKickMeterContracts.cs", paths)
        self.assertIn("Assets/Scripts/Gameplay/SourceKickMeterProcess.cs", paths)
        self.assertIn("Assets/Scripts/Gameplay/GameSimulation.FireSpfx.cs", paths)
        self.assertIn("Assets/Scripts/Contracts/SourceFireSpfxContracts.cs", paths)
        self.assertIn("Assets/Scripts/AssetImport/SourceFireSpfxData.cs", paths)
        self.assertIn("Assets/Scripts/AssetImport/SourceTeamPoppedHeadData.cs", paths)
        self.assertEqual(len(RUNNER.EXTRA_PURE), 5)
        for name in RUNNER.EXTRA_PURE:
            self.assertTrue((RUNNER.ROOT / name).is_file(), name)

    def test_runtime_exposes_independent_opt_in_and_real_typed_provider(self):
        runtime = (RUNNER.ROOT / "Assets/Scripts/Runtime/BlitzRuntime.cs").read_text()
        services = (RUNNER.ROOT / "Assets/Scripts/Runtime/SourceInterfaceServices.cs").read_text()
        self.assertIn("SourceKickMeterOptions.FromUrl(Application.absoluteURL)", runtime)
        self.assertIn("sourceServices.CreateSimulation(prefab, compatibility, fireEffects: fire, kickMeters: kickMeters)", runtime)
        self.assertIn("originalHud.Data.CreatePortableAuthoredKickMeterMetrics()", services)
        self.assertEqual(services.count("kickMeterMetrics: kickMetrics"), 3)
        self.assertIn("kickMeters = SourceKickMeterMode.Disabled", services)

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
