import importlib.util
from pathlib import Path
import unittest

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("combined_fire_kick_runner", HERE / "run.py")
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


class CombinedRunnerContracts(unittest.TestCase):
    def test_controls_target_unique_real_production_sites_and_named_runtime_contracts(self):
        self.assertEqual(len(RUNNER.CONTROLS), 7)
        self.assertEqual(len({row[0] for row in RUNNER.CONTROLS}), 7)
        for identity, edits, expected in RUNNER.CONTROLS:
            with self.subTest(control=identity):
                source = {}
                for name, old, new in edits:
                    self.assertTrue(name.startswith("Assets/Scripts/"))
                    text = source.get(name, (RUNNER.ROOT / name).read_text())
                    self.assertEqual(text.count(old), 1, identity)
                    self.assertNotEqual(old, new)
                    source[name] = text.replace(old, new)
                self.assertGreater(len(expected), 20)
                self.assertNotIn(expected, ("passed", "failed", "exception"))

    def test_actual_compile_inventory_contains_both_features_and_existing_consumers(self):
        paths = {p.relative_to(RUNNER.ROOT).as_posix() for p in RUNNER.production_paths()}
        self.assertEqual(len(paths), 78)
        for name in (
            "Assets/Scripts/Gameplay/GameSimulation.FireSpfx.cs",
            "Assets/Scripts/Gameplay/GameSimulation.Possession.cs",
            "Assets/Scripts/Gameplay/SourcePlaySelectionTimer.cs",
            "Assets/Scripts/Contracts/SourceFireSpfxContracts.cs",
            "Assets/Scripts/AssetImport/SourceFireSpfxData.cs",
            "Assets/Scripts/AssetImport/SourceTeamPoppedHeadData.cs",
            "Assets/Scripts/AssetImport/SourceFreeBallData.cs",
            "Assets/Scripts/AssetImport/SourceNormalCameraData.cs",
            "Assets/Scripts/Contracts/SourceCameraContracts.cs",
            "Assets/Scripts/Gameplay/SourceKickMeterProcess.cs",
            "Assets/Scripts/Contracts/SourceKickMeterContracts.cs",
        ):
            self.assertIn(name, paths)
        for name in RUNNER.EXTRA:
            self.assertTrue((RUNNER.ROOT / name).is_file(), name)
        self.assertEqual(len(paths | set(RUNNER.EXTRA)), 85)


if __name__ == "__main__":
    unittest.main()


if __name__ == "__main__":
    unittest.main()
