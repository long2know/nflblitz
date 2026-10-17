#!/usr/bin/env python3
"""Test the result-inventory guard, not gameplay."""

import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET


SPEC = importlib.util.spec_from_file_location("core_runner", Path(__file__).with_name("run-core.py"))
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


class InventoryGuardTests(unittest.TestCase):
    def verify(self, cases, inventory):
        root = ET.Element("TestRun", xmlns=RUNNER.NAMESPACE["t"])
        results = ET.SubElement(root, "Results")
        for name, outcome in cases:
            ET.SubElement(results, "UnitTestResult", testName=name, outcome=outcome)
        document = ET.ElementTree(ET.fromstring(ET.tostring(root)))
        with patch.object(RUNNER.ET, "parse", return_value=document):
            return RUNNER.verify_results("unused-in-memory.trx", {"requiredMethods": inventory})

    def test_counts_actual_enumerated_theory_cases(self):
        result = self.verify([("Tests.RealRule(team: Home)", "Passed"), ("Tests.RealRule(team: Away)", "Passed")], {"RealRule": 2})
        self.assertEqual(2, result["total"])
        self.assertEqual(0, result["skipped"])

    def test_zero_discovery_cannot_pass(self):
        with self.assertRaisesRegex(RuntimeError, "zero actual"):
            self.verify([], {})

    def test_missing_theory_case_cannot_pass(self):
        with self.assertRaisesRegex(RuntimeError, "inventory mismatch"):
            self.verify([("Tests.RealRule(team: Home)", "Passed")], {"RealRule": 2})

    def test_removed_required_method_cannot_pass(self):
        with self.assertRaisesRegex(RuntimeError, "missing"):
            self.verify([("Tests.Other", "Passed")], {"Other": 1, "RealRule": 1})

    def test_unregistered_extra_case_cannot_pass(self):
        with self.assertRaisesRegex(RuntimeError, "unexpected"):
            self.verify([("Tests.Other", "Passed"), ("Tests.RealRule", "Passed")], {"RealRule": 1})

    def test_skipped_and_failed_cases_cannot_pass(self):
        for outcome in ("NotExecuted", "Skipped", "Failed", "Inconclusive", None):
            with self.subTest(outcome=outcome), self.assertRaisesRegex(RuntimeError, "Nonpassing or skipped"):
                self.verify([("Tests.RealRule", outcome or "")], {"RealRule": 1})

    def test_hash_inventory_covers_actual_assetimport_dependency(self):
        root = Path(__file__).resolve().parents[1]
        actual = RUNNER.source_hashes(root)
        dependencies = sorted((root / "Assets/Scripts/AssetImport").glob("*.cs"))
        self.assertTrue(dependencies, "The actual pure AssetImport dependency must not be omitted.")
        for path in dependencies:
            self.assertIn(path.relative_to(root).as_posix(), actual)
        self.assertIn("Assets/Scripts/Presentation/SourceAudioFunctions.cs", actual,
                      "The imported actual sound selector must be covered by the legacy source hash guard.")

    def test_projects_compiling_actual_catch_geometry_include_its_actual_dependency(self):
        root = Path(__file__).resolve().parents[1]
        geometry = root / "Assets/Scripts/Gameplay/SourceCatchGeometry.cs"
        dependency = root / "Assets/Scripts/AssetImport/SourceActorPoseSampler.cs"
        affected = []
        for project in sorted((root / "tests").rglob("*.csproj")):
            if any(part in (".artifacts", "bin", "obj", "viewport", "movement", "presentation", "interface", "audio") for part in project.parts):
                continue
            included = set()
            declares_geometry = False
            for item in ET.parse(project).findall(".//Compile"):
                for pattern in item.attrib.get("Include", "").split(";"):
                    prefix = "$(BlitzSourceRoot)"
                    if pattern.startswith(prefix):
                        relative = pattern[len(prefix):]
                        included.update(root.glob(relative))
                        # Contracts can publish before the hot geometry source; its include still needs the dependency.
                        declares_geometry |= relative in (
                            geometry.relative_to(root).as_posix(),
                            "Assets/Scripts/Gameplay/*.cs",
                            "Assets/Scripts/Gameplay/**/*.cs",
                        )
            if geometry in included or declares_geometry:
                affected.append(project.relative_to(root / "tests").as_posix())
                self.assertIn(dependency, included, str(project) + " omits actual pose sampling.")
        self.assertEqual([
            "Blitz.Core.Tests/Blitz.Core.Tests.csproj",
            "ball/BallChecks.csproj",
            "camera/CameraChecks.csproj",
            "contact/ContactChecks.csproj",
            "movement-callers/MovementCallerChecks.csproj",
            "parity/ParityChecks.csproj",
        ], affected)


if __name__ == "__main__":
    unittest.main()
