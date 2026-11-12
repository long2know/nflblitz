import copy
import json
import unittest

import original_m1_prevent_exhibition as scenario


class ExhaustedExhibitionRecordingGuards(unittest.TestCase):
    def record(self):
        return json.loads((scenario.canonical.HERE /
            "provenance/original-m1-v9-prevent-failed-20261102.json").read_bytes())

    def test_authentic_final_receipt_cannot_export_without_actual_two_pose_witness(self):
        record = self.record()
        with self.assertRaisesRegex(ValueError, "no genuine public R_SKIP witness"):
            scenario.validate_window(record["receipt"], record["executionCounts"], [])

    def test_exhibition_use_does_not_replace_original_pose_factory_or_budget_contracts(self):
        record = self.record()
        for field, value in (("preventHosts", 0), ("profile", "original-teams-contact282-c2-m1-presnap-switch"),
                             ("factory", "REAL SourceC2Composition.CreatePreSnapSwitchSimulation")):
            changed = copy.deepcopy(record["receipt"])
            changed[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                scenario.validate_window(changed, record["executionCounts"], [])
        counts = dict(record["executionCounts"], unfilteredRawNextValues=32769, withinPerDriverBudget=False)
        with self.assertRaisesRegex(ValueError, "budget accounting"):
            scenario.validate_window(record["receipt"], counts, [])

    def test_failed_v8_receipt_cannot_be_relabelled_as_a_final_exhibition_window(self):
        old = json.loads((scenario.canonical.HERE /
            "provenance/original-m1-v8-failed-20261102.json").read_bytes())
        with self.assertRaisesRegex(ValueError, "actual final exhibition window"):
            scenario.validate_window(old["receipt"], old["executionCounts"], [])


if __name__ == "__main__":
    unittest.main()
