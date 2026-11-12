import importlib.util
import ast
from pathlib import Path
import re
import unittest

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("combined_fire_kick_runner", HERE / "run.py")
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


def expected_kernel_sources():
    # Both meter suites use the same explicitly named kernel-closure contract.
    tree = ast.parse((RUNNER.ROOT / "tests/kick-meter/test_runner.py").read_text())
    method = next(node for node in ast.walk(tree) if isinstance(node, ast.FunctionDef) and
                  node.name == "test_real_compile_closure_includes_existing_cabinet_and_HUD_consumers")
    declaration = next(node for node in method.body if isinstance(node, ast.Assign) and
                       any(isinstance(target, ast.Name) and target.id == "expected_by_folder"
                           for target in node.targets))
    folders = ast.literal_eval(declaration.value)
    return {"Assets/Scripts/" + folder + "/" + name
            for folder, names in folders.items() for name in names.split()}


class CombinedRunnerContracts(unittest.TestCase):
    def test_controls_target_unique_real_production_sites_and_named_runtime_contracts(self):
        source = (RUNNER.ROOT / RUNNER.FACTORY).read_text()

        def arguments(text):
            call = re.search(r"var game = new GameSimulation\((.*?)\);", text, re.S)
            self.assertIsNotNone(call)
            return [value.strip() for value in call[1].split(",")]

        original = arguments(source)
        self.assertIn("twoStationRecordProfile: twoStationRecordProfile", original)
        for identity, edits, _ in RUNNER.CONTROLS[:2]:
            with self.subTest(control=identity):
                name, old, new = edits[0]
                self.assertEqual(name, RUNNER.FACTORY)
                self.assertEqual(source.count(old), 1)
                actual = arguments(source.replace(old, new))
                self.assertEqual(len(original), len(actual))
                changed = [(left, right) for left, right in zip(original, actual) if left != right]
                consumer = "fireEffects" if identity == "lost-fire-forwarding" else "kickMeterMetrics"
                self.assertEqual([(consumer, "null")], changed)
                self.assertIn("twoStationRecordProfile: twoStationRecordProfile", actual)

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
        self.assertSetEqual(paths, expected_kernel_sources(),
                            "Retain the complete named base closure plus OriginalSound programmes.")
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
            "Assets/Scripts/Storage/SourcePlayerRecords.cs",
            "Assets/Scripts/Storage/SourceCustomPlayCodec.cs",
            "Assets/Scripts/Presentation/SourceOriginalAudioFunctions.cs",
        ):
            self.assertIn(name, paths)
        for name in RUNNER.EXTRA:
            self.assertTrue((RUNNER.ROOT / name).is_file(), name)
        self.assertSetEqual(set(RUNNER.EXTRA), {
            "Assets/Scripts/Runtime/SourceCabinetInput.cs",
            "Assets/Scripts/Runtime/SourceCabinetInputPlaySelection.cs",
            "Assets/Scripts/Runtime/SourceFireSpfxRuntime.cs",
            "Assets/Scripts/Presentation/SourceFireSpfxRenderModel.cs",
            "Assets/Scripts/Presentation/SourceHudModel.cs",
            "Assets/Scripts/Presentation/SourceHudViewport.cs",
            "Assets/Scripts/Presentation/SourceHudComposer.cs",
            "Assets/Scripts/Presentation/SourceCabinetScreenModel.cs",
            "Assets/Scripts/Presentation/SourceHudComposer.Records.cs",
            "Assets/Scripts/Presentation/SourceRecordScreenModel.cs",
        })


if __name__ == "__main__":
    unittest.main()


if __name__ == "__main__":
    unittest.main()
