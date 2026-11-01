#!/usr/bin/env python3
"""Keep real transport mutation anchors aligned with the current source."""
import importlib.util
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[3]
SPEC = importlib.util.spec_from_file_location("runtime_transport_runner", Path(__file__).with_name("run.py"))
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


class RuntimeMutationAnchors(unittest.TestCase):
    def test_all_eighteen_controls_target_one_actual_current_source_expression(self):
        self.assertEqual(18, len(RUNNER.MUTANTS))
        self.assertEqual(18, len({row[0] for row in RUNNER.MUTANTS}))
        for name, relative, before, after, assertion in RUNNER.MUTANTS:
            with self.subTest(mutant=name):
                source = (ROOT / relative).read_text()
                self.assertEqual(1, source.count(before), name + " must target actual current production")
                self.assertNotEqual(before, after)
                self.assertTrue(assertion)

    def test_forged_and_subcall_controls_preserve_the_current_pairing_and_order_contracts(self):
        controls = {row[0]: row for row in RUNNER.MUTANTS}
        self.assertIn("ReferenceEquals(actual, dispatch)", controls["forged-dispatch"][2])
        self.assertIn("paired.TryGetValue(dispatch.OriginalCue, out actual)", controls["forged-dispatch"][3])
        self.assertIn("ReferenceEquals(call.OriginalCue, dispatch.OriginalCue)", controls["subcall-cue-alias"][2])
        self.assertEqual("call.SubcallOrder != i", controls["subcall-order"][2])

    def test_producer_controls_target_actual_emission_metadata_reset_and_callback_scope(self):
        for name, relative, before, after, assertion in RUNNER.PRODUCER_MUTANTS:
            with self.subTest(mutant=name):
                self.assertEqual(1, (ROOT / relative).read_text().count(before))
                self.assertNotEqual(before, after)
                self.assertTrue(assertion)


if __name__ == "__main__":
    unittest.main()
