#!/usr/bin/env python3
"""Asset-free failure guards only; these do not execute or qualify gameplay."""
import copy
from pathlib import Path
import tempfile
import unittest

import ball_blink_reference as reference
import mutations

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]


class BallBlinkFailureGuards(unittest.TestCase):
    def fixture(self):
        return {
            "status": "passed", "ballBlinkAssertions": reference.ASSERTIONS,
            "assertions": reference.RETAINED_ASSERTIONS + reference.ASSERTIONS,
            "fullGameQualified": False, "nativeQualified": False,
            "ballBlinkProbes": [
                {"name": "guard-" + str(i),
                 "input": {"kind": "Fumble", "rawFlags": 0, "flight": 0, "mode": 2,
                           "carrier": -1, "teamFire": 0, "offSide": 0},
                 "actual": {"effect": "None", "white": False, "flight": 1}}
                for i in range(reference.CASES)
            ],
        }

    def test_exact_nonempty_assertion_and_case_inventory(self):
        reference.require_inventory(self.fixture())
        for field, value in (("status", "failed"), ("ballBlinkAssertions", 0),
                             ("ballBlinkAssertions", reference.ASSERTIONS - 1),
                             ("ballBlinkAssertions", reference.ASSERTIONS + 1),
                             ("ballBlinkAssertions", float(reference.ASSERTIONS)),
                             ("assertions", reference.ASSERTIONS),
                             ("assertions", reference.RETAINED_ASSERTIONS + reference.ASSERTIONS + 1),
                             ("ballBlinkProbes", []), ("fullGameQualified", True),
                             ("nativeQualified", True)):
            with self.subTest(field=field, value=value), self.assertRaises(AssertionError):
                reference.require_inventory(dict(self.fixture(), **{field: value}))
        for value in (None, {}, [], "passed"):
            with self.subTest(value=value), self.assertRaises(AssertionError):
                reference.require_inventory(value)

    def test_ordered_probe_shape_and_domain_are_required(self):
        for field, value in (("kind", "Unspecified"), ("carrier", 14), ("flight", -1),
                             ("flight", 0x7fffffff), ("mode", 6), ("offSide", 2),
                             ("teamFire", -1), ("rawFlags", "32")):
            data = self.fixture()
            data["ballBlinkProbes"][0]["input"][field] = value
            with self.subTest(field=field), self.assertRaises(AssertionError):
                reference.require_inventory(data)
        for value in (None, [], "Blink", {}):
            data = self.fixture()
            data["ballBlinkProbes"][0] = value
            with self.subTest(value=value), self.assertRaises(AssertionError):
                reference.require_inventory(data)
        for field, value in (("white", 1), ("effect", "Invented"), ("flight", "1")):
            data = self.fixture()
            data["ballBlinkProbes"][0]["actual"][field] = value
            with self.subTest(field=field), self.assertRaises(AssertionError):
                reference.require_inventory(data)
        data = self.fixture()
        data["ballBlinkProbes"][1] = copy.deepcopy(data["ballBlinkProbes"][0])
        with self.assertRaises(AssertionError):
            reference.require_inventory(data)

    def test_unpinned_source_fails_before_native_comparison(self):
        owned = HERE / ".artifacts/runner-guards"
        owned.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=owned) as directory:
            original = Path(directory) / "player.c"
            original.write_bytes(b"/* authored invalid-source guard */\n")
            with self.assertRaisesRegex(AssertionError, "input pin changed"):
                reference.compare(original, self.fixture(), Path(directory) / "never-created")
            self.assertFalse((Path(directory) / "never-created").exists())

    def test_five_new_controls_keep_the_ten_existing_controls_and_exact_production_anchors(self):
        self.assertEqual(15, len(mutations.MUTANTS))
        controls = [row for row in mutations.MUTANTS if row[0].startswith(("ball-blink-", "ball-white-"))]
        self.assertEqual(5, len(controls))
        for name, path, old, new, failure in controls:
            with self.subTest(control=name):
                self.assertEqual(1, (ROOT / path).read_text().count(old))
                self.assertNotEqual(old, new)
                self.assertIn("original ball blink owner", failure)


if __name__ == "__main__":
    unittest.main()
