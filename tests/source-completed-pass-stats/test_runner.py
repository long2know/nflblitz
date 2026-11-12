#!/usr/bin/python3.12
"""New standard-library contract units; no compiler or old campaign."""
import importlib.util
import io
import json
import os
from pathlib import Path
import signal
import sys
import unittest

SPEC = importlib.util.spec_from_file_location("completed_pass_runner", Path(__file__).with_name("run.py"))
RUN = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUN)


class FocusedContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.guard = RUN.load_guard()
        cls.caller = RUN.data(RUN.ROOT / RUN.CALLER)
        cls.flow = RUN.data(RUN.ROOT / "Assets/Scripts/Gameplay/SourceMatchFlow.cs")

    def rejected(self, changed, expected):
        with self.assertRaises(self.guard.StaticAssertionFailure) as failure:
            self.guard.require_caller_wiring(self.flow, changed)
        self.assertEqual(expected, failure.exception.field)

    def test_literal_inventory_is_frozen_and_complete(self):
        inventory = RUN.expected_inventory()
        self.assertEqual(inventory, RUN.read_json(RUN.PREP / "expected-inventory.json"))
        self.assertEqual(55, inventory["caseCount"])
        self.assertEqual(55, len(set(inventory["cases"])))
        self.assertEqual(20, len(inventory["controls"]))
        self.assertEqual(inventory["fieldCount"], len({r["field"] for r in inventory["fields"]}))
        values = {r["field"]:r["expected"] for r in inventory["fields"]}
        self.assertEqual(-13, values["pass.loss.away.state.away.passing-yards"])
        self.assertEqual(-9, values["pass.lifecycle.snapshot.final.home.total-yards"])
        self.assertEqual(1, values["pass.conversion.home.state.home.pass-touchdowns"])

    def test_actual77_and_exact_two_harness_paths(self):
        self.assertEqual(77, len(RUN.source_paths()))
        self.assertEqual(("tests/source-completed-pass-stats/Program.cs",
                          "tests/source-offensive-stats/Program.cs"), RUN.HARNESS_SOURCES)
        RUN.verify_authored()

    def test_four_inverse_checks_and_four_old_constructors(self):
        result = RUN.static_preservation()
        self.assertEqual(4, result["constructors"])
        self.assertEqual(4, len(result["fourWholeFileInverses"]))
        self.assertTrue(result["existingCompletionViewUnchanged"])
        self.assertFalse(result["simulationExecuted"])

    def test_unchanged_two_stage_guard_accepts_current_source(self):
        result = self.guard.require_caller_wiring(self.flow, self.caller)
        self.assertEqual("passed-static-only", result["status"])
        self.assertFalse(result["compiled"])
        self.assertFalse(result["simulation_executed"])
        self.assertEqual(RUN.OLD_REVERSE, result["preedit_source_sha256"])

    def test_call_missing_duplicate_altered_relocated_and_comment_fail(self):
        text = self.caller.decode()
        call = "            Model.AccumulateSourcePassCompletion(endingInput, resolution, Model.CarrierId);\n"
        changed = (
            text.replace(RUN.CALL_AFTER, RUN.CALL_BEFORE),
            text.replace(call, call + call),
            text.replace(call, call.replace("Model.CarrierId", "Model.CarrierId + 1")),
            text.replace(RUN.CALL_AFTER, RUN.CALL_BEFORE + call),
            text.replace(RUN.CALL_AFTER, RUN.CALL_BEFORE) + "\n/*\n" + RUN.CALL_AFTER + "*/\n",
        )
        for item in changed:
            with self.subTest(shape=item[-60:]):
                self.rejected(item.encode(), "caller.completed-pass.exact-authorized-insertion")

    def test_unrelated_caller_edit_still_fails_whole_source_guard(self):
        changed = self.caller.replace(b"Model.SourceRequestedOnside = false;", b"Model.SourceRequestedOnside = true;")
        self.assertNotEqual(changed, self.caller)
        self.rejected(changed, "caller.only-authorized-call-changed-captain-preserved")

    def test_existing_static_fixture_shapes_keep_named_failures(self):
        for control in self.guard.STATIC_CONTROLS:
            with self.subTest(field=control[3]):
                self.rejected(self.guard.mutate_static_caller(control, self.caller), control[3])

    def test_twenty_controls_have_unique_actual_source_anchors(self):
        inventory = RUN.expected_inventory()
        fields = {r["field"] for r in inventory["fields"]}
        for control in inventory["controls"]:
            raw = RUN.data(RUN.ROOT / control["file"])
            self.assertEqual(1, raw.count(control["before"].encode()), control["id"])
            self.assertNotEqual(raw, raw.replace(control["before"].encode(), control["after"].encode()))
            self.assertIn(control["requiredAssertion"], fields)
            self.assertIn(control["case"], inventory["cases"])

    def test_only_exact_normal_difference_with_clean_prefix_qualifies(self):
        expected = RUN.expected_inventory()
        control = expected["controls"][1]
        selected = [r for r in expected["fields"] if r["case"] == control["case"]]
        stop = next(i for i,r in enumerate(selected) if r["field"] == control["requiredAssertion"])
        fields = [{k:r[k] for k in ("case","field","expected")} for r in selected[:stop+1]]
        for row in fields:
            row["actual"] = row["expected"]
        fields[-1]["actual"] = 999
        valid = {"status":"assertion-failed","selectedCase":control["case"],
                 "failedAssertion":control["requiredAssertion"],"cases":[],
                 "assertions":len(fields),"fields":fields}
        RUN.qualify_control(2, valid, control, expected)
        for code, changed in (
            (1, valid), (2, dict(valid, status="unexpected-error")),
            (2, dict(valid, selectedCase="unrelated")), (2, dict(valid, failedAssertion="unrelated")),
            (2, dict(valid, fields=fields[:-1])), (2, dict(valid, assertions=999)),
        ):
            with self.assertRaises(RuntimeError):
                RUN.qualify_control(code, changed, control, expected)
        for key, value in (("expected",999), ("actual",fields[-1]["expected"])):
            altered = [dict(r) for r in fields]
            altered[-1][key] = value
            with self.assertRaises(RuntimeError):
                RUN.qualify_control(2, dict(valid, fields=altered), control, expected)
        altered = [dict(r) for r in fields]
        altered[0]["actual"] = -999
        with self.assertRaises(RuntimeError):
            RUN.qualify_control(2, dict(valid, fields=altered), control, expected)

    def test_synthetic_fixture_qualification_is_not_authentic_admission(self):
        fixture = {"randomRequests":0,"originalProvenanceSupplied":False,
                   "initializationStepsPerLayer":1,"sourceFrames":1,"selectedFrame":0,
                   "joints":15,"actorsInitialized":14,"layersInitialized":28}
        RUN.fixture_check({"fixture":fixture})
        for key, value in (("actorsInitialized",13), ("layersInitialized",14),
                           ("randomRequests",1), ("originalProvenanceSupplied",True)):
            with self.assertRaises(RuntimeError):
                RUN.fixture_check({"fixture":dict(fixture, **{key:value})})


def main():
    RUN.authorities()
    tag, started = RUN.entry("units", [RUN.PYTHON,"-I","-S","-B"]+sys.argv)
    signal.signal(signal.SIGALRM, RUN.alarm_timeout)
    signal.alarm(180)
    code = 1
    summary = {}
    try:
        RUN.verify_authored()
        buffer = io.StringIO()
        result = unittest.TextTestRunner(stream=buffer, verbosity=2).run(
            unittest.defaultTestLoader.loadTestsFromTestCase(FocusedContracts))
        code = 0 if result.wasSuccessful() else 1
        log = RUN.record(RUN.PREP/"unit-log.json", {"text":buffer.getvalue()})
        summary = {"exit":code,"tests":result.testsRun,"failures":len(result.failures),
                   "errors":len(result.errors),"log":log,"compilers":0,"managed":0}
        print(buffer.getvalue())
    except Exception as error:
        summary = {"exit":1,"error":type(error).__name__,"message":str(error)}
        print(json.dumps(summary), file=sys.stderr)
    RUN.record(RUN.PREP/"unit-result.json", summary)
    RUN.finish_entry(tag, started, code, summary)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
