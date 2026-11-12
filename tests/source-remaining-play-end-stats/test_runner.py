#!/usr/bin/env python3
"""The one admitted twelve-test stdlib runner; never launches a compiler."""
import copy
import importlib.util
import io
import json
from pathlib import Path
import sys
import unittest


SPEC = importlib.util.spec_from_file_location("remaining_play_end_runner", Path(__file__).with_name("run.py"))
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class RunnerContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = runner.frozen_inputs()
        cls.contract = runner.read_json(runner.OUT / "contract.json")
        cls.inventory = runner.read_json(cls.manifest["inventory"]["path"])
        cls.mutations = runner.read_json(cls.manifest["mutations"]["path"])
        cls.production = {row["relativePath"]: row for row in cls.manifest["production"]}
        cls.guard_source = runner.regular_bytes(runner.ROOT / runner.GUARD).decode()
        cls.guard = runner.pure_guard(cls.guard_source)
        cls.caller = runner.regular_bytes(cls.production[runner.CALLER]["path"]).decode()
        cls.flow = runner.regular_bytes(cls.production[runner.FLOW]["path"])

    def test_01_exact_literal_inventory(self):
        table = self.contract["literalTestInventory"]
        names = []
        for row in table["dispatchRows"]:
            names += [row[0] + "-home", row[0] + "-away"]
        names += [row["name"] for row in table["explicitSentinelReturns"]]
        for row in table["captureRows"]:
            names += [row[0] + "-home", row[0] + "-away"]
        names += [row["name"] for row in table["apiCases"] + table["supportCases"]]
        names += [table["mixedLifecycle"]["name"]]
        self.assertEqual([row["name"] for row in self.inventory["cases"]], names)
        self.assertEqual(len(names), 132)
        self.assertEqual(len(set(names)), 132)
        self.assertEqual(self.inventory["scalarCount"], self.manifest["scalarCount"])
        self.assertEqual(sum(len(case["fields"]) for case in self.inventory["cases"]),
                         self.inventory["scalarCount"])
        for vector in table["outcomes"].values():
            self.assertEqual(len(vector), 10)
        for case in self.inventory["cases"]:
            self.assertEqual(len(case["fields"]), len({row["name"] for row in case["fields"]}))
            self.assertTrue(all(row["name"].startswith(case["name"] + ".") for row in case["fields"]))
            self.assertTrue(all(isinstance(row["value"], (str, int, float, bool, type(None)))
                                for row in case["fields"]))

    def test_02_exact_control_declarations(self):
        declarations = self.contract["freshControlsProposal"]["controls"]
        self.assertEqual(len(self.mutations), 30)
        self.assertEqual(len({(row["source"], row["anchor"]) for row in self.mutations}), 30)
        for declared, frozen in zip(declarations, self.mutations):
            self.assertEqual((frozen["id"], frozen["case"], frozen["failure"], frozen["expected"]),
                             (declared[0], declared[2], declared[2] + "." + declared[3], declared[4]))
            case = next(case for case in self.inventory["cases"] if case["name"] == frozen["case"])
            field = case["fields"][frozen["firstFailureIndex"]]
            self.assertEqual((field["name"], field["value"]), (frozen["failure"], frozen["expected"]))

    def test_03_unique_actual_mutations_and_missing_anchor(self):
        for mutation in self.mutations:
            source = runner.regular_bytes(self.production[mutation["source"]]["path"]).decode()
            changed = runner.mutate(source, mutation["source"], mutation["anchor"], mutation["replacement"])
            self.assertEqual(runner.digest(changed.encode()), mutation["mutatedAfter"]["sha256"])
        with self.assertRaisesRegex(RuntimeError, "anchor not unique"):
            runner.replace_unique("actual-source", "absent", "replacement")

    def test_04_duplicate_anchor_rejected(self):
        mutation = self.mutations[0]
        source = runner.regular_bytes(self.production[mutation["source"]]["path"]).decode()
        with self.assertRaisesRegex(RuntimeError, "anchor not unique"):
            runner.mutate(source + mutation["anchor"], mutation["source"],
                          mutation["anchor"], mutation["replacement"])

    def test_05_forbidden_mutation_targets_rejected(self):
        for path in (runner.FIXTURE, runner.HARNESS, "preparation/expected-inventory.json",
                     "Assets/Scripts/Gameplay/SourceMatchRules.cs"):
            with self.assertRaisesRegex(RuntimeError, "outside actual released production"):
                runner.mutate("actual", path, "actual", "changed")

    def test_06_closed_source_set(self):
        expected = [row["relativePath"] for row in self.manifest["production"]]
        runner.validate_sources(expected, expected)
        for changed in (expected[:-1], expected + ["Assets/Scripts/Gameplay/SHADOW.cs"],
                        expected[:-1] + expected[:1]):
            with self.assertRaisesRegex(RuntimeError, "closure changed"):
                runner.validate_sources(changed, expected)

    def test_07_exact_reference_and_tool_identity(self):
        for profile in ("net10.0", "netstandard2.1"):
            rows = self.manifest["references"][profile]
            runner.validate_references(rows, profile)
            changed = copy.deepcopy(rows)
            changed[0]["path"] = str(Path(changed[0]["path"]).with_name("unadmitted-sibling.dll"))
            with self.assertRaisesRegex(RuntimeError, "exact named pinned"):
                runner.validate_references(changed, profile)
        tool = dict(self.manifest["tools"][0], sha256="0" * 64)
        with self.assertRaisesRegex(RuntimeError, "Pinned input mismatch"):
            runner.check_pin(tool["path"], tool)

    def test_08_pure_ast_guard_only(self):
        names = set(self.guard) - {"__builtins__", "hashlib", "re", "__name__"}
        self.assertEqual(names, runner.PURE_CONSTANTS | runner.PURE_FUNCTIONS | {"StaticAssertionFailure"})
        for forbidden in ("subprocess", "sys", "run_mutations", "main", "MUTATIONS"):
            self.assertNotIn(forbidden, self.guard)
        self.assertEqual(self.guard["require_caller_wiring"](self.flow, self.caller.encode())["status"],
                         "passed-static-only")

    def test_09_eight_current_wiring_negative_controls(self):
        declared = runner.read_json(runner.PREP / "static-controls.json")
        observed = []
        for identity, changed, expected_field in runner.static_mutants(self.caller, self.guard):
            with self.assertRaises(self.guard["StaticAssertionFailure"]) as failure:
                self.guard["require_caller_wiring"](self.flow, changed.encode())
            self.assertEqual(failure.exception.field, expected_field, identity)
            frozen = next(row for row in declared if row["id"] == identity)
            self.assertEqual(runner.digest(changed.encode()), frozen["sourceAfterSha256"])
            observed.append({"id": identity, "field": failure.exception.field,
                             "mutatedSha256": frozen["sourceAfterSha256"]})
        self.assertEqual(len(observed), 8)
        runner.write_json(runner.PREP / "static-control-result.json", observed)

    def test_10_literal_verifier_rejects_wrong_offending_value(self):
        case = self.inventory["cases"][0]
        fixture = {"clipKey": "SYNTHETIC_COMPLETION_SNAPSHOT_FRAME",
                   "programKey": "synthetic_completion_snapshot_frame_only", "sourceFrames": 1,
                   "selectedFrame": 0, "joints": 15, "actorsInitialized": 14, "layersInitialized": 28,
                   "initializationStepsPerLayer": 1, "program": "NewSequence,NextFrame(1),End",
                   "randomRequests": 0, "originalProvenanceSupplied": False}
        payload = {"fixture": fixture, "genuineSimulationExecuted": False, "cases": [case["name"]],
                   "status": "passed", "failure": None,
                   "fields": [{"name": row["name"], "unit": row["unit"], "expected": row["value"],
                               "actual": row["value"]} for row in case["fields"]]}
        payload["fieldCount"] = len(payload["fields"])
        runner.verify_result(payload, self.inventory, case["name"])
        payload["fields"][3]["actual"] = -999
        with self.assertRaisesRegex(RuntimeError, "Literal semantic mismatch: " + payload["fields"][3]["name"]):
            runner.verify_result(payload, self.inventory, case["name"])

    def test_11_reservation_and_failure_consumption(self):
        before = {key: 0 for key in runner.LIMITS}
        reserved = runner.counts_after(before, "csc")
        self.assertEqual(before["csc"], 0)
        self.assertEqual(reserved["csc"], 1)
        failure_receipt = {"exit": 1, "counts": reserved}
        self.assertEqual(failure_receipt["counts"]["csc"], 1)
        exhausted = dict(before, csc=33)
        with self.assertRaisesRegex(RuntimeError, "ceiling exhausted"):
            runner.counts_after(exhausted, "csc")
        tree = runner.ast.parse(runner.regular_bytes(runner.SUITE / "run.py"))
        function = next(node for node in tree.body if isinstance(node, runner.ast.FunctionDef) and node.name == "child")
        calls = [node for node in runner.ast.walk(function) if isinstance(node, runner.ast.Call)]
        reserve = next(node.lineno for node in calls if isinstance(node.func, runner.ast.Name)
                       and node.func.id == "reserve_child")
        spawn = next(node.lineno for node in calls if isinstance(node.func, runner.ast.Attribute)
                     and node.func.attr == "Popen")
        self.assertLess(reserve, spawn)

    def test_12_ns21_compile_only(self):
        argv, _, sources, references = runner.compile_argv(
            self.manifest, runner.VALIDATION / "ns21-compatibility-only", ns21=True)
        self.assertEqual(len(sources), 77)
        self.assertEqual(len(references), 121)
        self.assertIn("/target:library", argv)
        self.assertNotIn("/main:SourceRemainingPlayEndStatsChecks", argv)
        self.assertTrue(all("captured-production" in source for source in sources))
        execution_allowed = not ("/target:library" in argv)
        self.assertFalse(execution_allowed)
        with self.assertRaisesRegex(RuntimeError, "NS2.1 is compile-only"):
            runner.require(execution_allowed, "NS2.1 is compile-only")


def main():
    envelope = runner.claim_outer("U1", "units")
    try:
        stream = io.StringIO()
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(RunnerContractTests)
        result = unittest.TextTestRunner(stream=stream, verbosity=2).run(suite)
        successful = result.wasSuccessful() and result.testsRun == 12
        report = {"schema": "source-remaining-play-end-python-units/v1", "testsRun": result.testsRun,
                  "successful": successful, "failures": [(str(test), text) for test, text in result.failures],
                  "errors": [(str(test), text) for test, text in result.errors], "output": stream.getvalue(),
                  "staticControls": 8, "compilerOrManagedLaunches": 0}
        runner.write_json(runner.PREP / "unit-result.json", report)
        if successful:
            runner.outer_template("D1", "drivers", runner.counters(), runner.pin(runner.PREP / "input-manifest.json"))
        print(json.dumps(report, sort_keys=True), flush=True)
        runner.finish_outer(envelope, 0 if successful else 1, result=report)
        return 0 if successful else 1
    except Exception as error:
        print(json.dumps({"status": "failed-stop-needs-precise-continuation", "error": str(error)}), flush=True)
        runner.finish_outer(envelope, 1, error=str(error))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
