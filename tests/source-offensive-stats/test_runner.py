#!/usr/bin/python3.12
"""One isolated standard-library command; never starts a compiler or old campaign."""
import importlib.util
import io
import json
import os
from pathlib import Path
import signal
import sys
import unittest

SPEC = importlib.util.spec_from_file_location("completion_focused_runner", Path(__file__).with_name("run.py"))
RUN = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUN)


class FocusedContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.guard = RUN.load_guard()
        cls.caller = RUN.data(RUN.ROOT / RUN.CALLER)
        cls.flow = RUN.data(RUN.ROOT / "Assets/Scripts/Gameplay/SourceMatchFlow.cs")

    def rejected(self, changed, field):
        with self.assertRaises(self.guard.StaticAssertionFailure) as failure:
            self.guard.require_caller_wiring(self.flow, changed)
        self.assertEqual(field, failure.exception.field)

    def test_frozen_inventory_is_exact_and_literal(self):
        expected = RUN.expected_inventory()
        self.assertEqual((35, 558, 9), (expected["caseCount"], expected["fieldCount"], len(expected["controls"])))
        self.assertEqual(expected, RUN.read_json(RUN.PREP / "expected-inventory.json"))
        self.assertEqual(35, len(set(expected["cases"])))
        self.assertEqual(558, len({row["field"] for row in expected["fields"]}))

    def test_actual77_and_before_captures_are_not_shadows(self):
        self.assertEqual(77, len(RUN.source_paths()))
        RUN.verify_sources()
        RUN.verify_frozen_authored()

    def test_adapted_guard_preserves_old_whole_source_hash(self):
        result = self.guard.require_caller_wiring(self.flow, self.caller)
        self.assertEqual("passed-static-only", result["status"])
        self.assertFalse(result["compiled"])
        self.assertFalse(result["simulation_executed"])
        self.assertEqual(RUN.OLD_REVERSE, self.guard.CALLER_PREEDIT_SHA256)
        self.assertEqual(RUN.OLD_REVERSE, result["preedit_source_sha256"])

    def test_missing_duplicate_altered_and_relocated_call_fail(self):
        text = self.caller.decode()
        call = "            Model.AccumulateSourcePassCompletion(endingInput, resolution, Model.CarrierId);\n"
        changes = (
            text.replace(RUN.AFTER_CALL, RUN.BEFORE_CALL),
            text.replace(call, call + call),
            text.replace(call, call.replace("Model.CarrierId", "Model.CarrierId + 1")),
            text.replace(RUN.AFTER_CALL, RUN.BEFORE_CALL + call),
        )
        for changed in changes:
            with self.subTest(change=changed[-70:]):
                self.rejected(changed.encode(), "caller.completed-pass.exact-authorized-insertion")

    def test_comment_decoy_cannot_supply_a_missing_executable_call(self):
        missing = self.caller.decode().replace(RUN.AFTER_CALL, RUN.BEFORE_CALL)
        self.rejected((missing + "\n/*\n" + RUN.AFTER_CALL + "*/\n").encode(),
                      "caller.completed-pass.exact-authorized-insertion")

    def test_unrelated_edit_still_fails_unchanged_whole_source_guard(self):
        changed = self.caller.replace(b"Model.SourceRequestedOnside = false;", b"Model.SourceRequestedOnside = true;")
        self.assertNotEqual(changed, self.caller)
        self.rejected(changed, "caller.only-authorized-call-changed-captain-preserved")

    def test_unchanged_old_caller_fixture_shapes_keep_their_fields(self):
        for _, anchor, replacement, field in self.guard.STATIC_CONTROLS:
            with self.subTest(field=field):
                changed = self.guard.mutate_static_caller((_, anchor, replacement, field), self.caller)
                self.rejected(changed, field)
        for anchor, replacement, field in (
            (self.guard.CURRENT_CALL, self.guard.CURRENT_CALL.replace("first, half,", "first, false,"),
             "caller.plan.original-input-arguments"),
            (self.guard.CURRENT_CALL,
             self.guard.CURRENT_CALL.replace("sourceConfiguration,", "Model.SourceConfiguration,"),
             "caller.plan.configured-stations"),
        ):
            self.rejected(self.caller.replace(anchor.encode(), replacement.encode()), field)

    def test_controls_mutate_unique_actual_production_copy_anchors(self):
        for control in RUN.CONTROLS:
            raw = RUN.data(RUN.ROOT / control[1])
            self.assertEqual(1, raw.count(control[2].encode()), control[0])
            self.assertNotEqual(raw, raw.replace(control[2].encode(), control[3].encode()), control[0])

    def test_only_normal_named_semantic_difference_can_kill(self):
        control = RUN.CONTROLS[4]
        expected = RUN.expected_inventory()
        declared = next(row for row in expected["fields"] if row["field"] == control[5])
        field = {key: declared[key] for key in ("case", "field", "expected")}
        field["actual"] = 3
        valid = {"status": "assertion-failed", "failedAssertion": control[5], "selectedCase": control[4],
                 "assertions": 1, "cases": [], "fields": [field]}
        RUN.qualify_control(2, valid, control, expected)
        for exit_code, changed in (
            (1, valid),
            (2, dict(valid, status="unexpected-error")),
            (2, dict(valid, failedAssertion="unrelated")),
            (2, dict(valid, fields=[dict(field, actual=field["expected"])])),
            (2, dict(valid, fields=[dict(field, expected=999)])),
        ):
            with self.assertRaises(RuntimeError):
                RUN.qualify_control(exit_code, changed, control, expected)


def main():
    signal.alarm(180)
    prelaunch = RUN.PREP / "0003-unit-prelaunch.json"
    RUN.record(RUN.PREP / "unit-entry.json", {
        "atUtc": RUN.now(), "pid": os.getpid(), "parentPid": os.getppid(),
        "argv": [RUN.PYTHON, "-I", "-S", "-B", str(Path(__file__)), "-q"],
        "cwd": os.getcwd(), "entryEnvironment": dict(os.environ), "prelaunch": RUN.identity(prelaunch),
        "consumedUnits": 1, "compilers": 0, "managed": 0,
    })
    RUN.verify_frozen_authored()
    buffer = io.StringIO()
    result = unittest.TextTestRunner(stream=buffer, verbosity=2).run(
        unittest.defaultTestLoader.loadTestsFromTestCase(FocusedContracts))
    exit_code = 0 if result.wasSuccessful() else 1
    RUN.exclusive(RUN.PREP / "unit-log.txt", buffer.getvalue().encode())
    RUN.record(RUN.PREP / "unit-result.json", {
        "atUtc": RUN.now(), "pid": os.getpid(), "exit": exit_code, "tests": result.testsRun,
        "failures": len(result.failures), "errors": len(result.errors),
        "prelaunch": RUN.identity(prelaunch), "log": RUN.identity(RUN.PREP / "unit-log.txt"),
        "compilers": 0, "managed": 0, "oldCampaignMainExecutions": 0,
    })
    print(buffer.getvalue())
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
