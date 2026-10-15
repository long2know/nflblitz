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


if __name__ == "__main__":
    unittest.main()
