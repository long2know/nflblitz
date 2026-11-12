#!/usr/bin/env python3
"""Eight new metadata/controller tests; no old harness or simulation entrypoint."""
import copy
import importlib.util
import json
from pathlib import Path
import sys
import unittest


SUITE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("place_kick_admitted_driver", SUITE / "run.py")
DRIVER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DRIVER)
STATIC_RESULTS = []


class PlaceKickMetadataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.authorities = DRIVER.authorities()
        cls.contract = cls.authorities["contract.json"]
        cls.manifest = DRIVER.input_manifest()
        cls.inventory = DRIVER.read_json(DRIVER.PREP / "expected-inventory.json")
        cls.mutations = DRIVER.read_json(DRIVER.PREP / "mutation-inventory.json")
        cls.contents = DRIVER.contents_from_manifest(cls.manifest)
        row = cls.manifest["guard"]
        cls.guard = DRIVER.pinned(row["path"], row["bytes"], row["sha256"])

    def test_01_exact_literal_inventory(self):
        DRIVER.validate_inventory(self.inventory, self.contract)
        flat = DRIVER.flattened(self.inventory)
        self.assertEqual(603, len(flat))
        self.assertEqual("01-canonical-home-down.classification.status", flat[0]["field"])
        self.assertEqual("32-source-snapshot-literal-and-nonsource-null.nonsource.match-null",
                         flat[-1]["field"])
        changed = copy.deepcopy(self.inventory)
        changed["producerCases"][0]["expected"][9] = 999
        with self.assertRaises(RuntimeError):
            DRIVER.validate_inventory(changed, self.contract)

    def test_02_exact_source_reference_vectors(self):
        DRIVER.closure_shape(self.manifest["production"])
        sources = self.manifest["production"] + self.manifest["harnesses"]
        self.assertEqual(79, len({row["path"] for row in sources}))
        self.assertEqual([DRIVER.HARNESS, DRIVER.FIXTURE],
                         [row["relativePath"] for row in self.manifest["harnesses"]])
        fixture = self.manifest["harnesses"][1]
        self.assertEqual("07946c95ad09dfd8eda4eaeeec805f739d6e5218ce85c4680f9ba56e40b95b25",
                         fixture["sha256"])
        self.assertEqual(167, len(self.manifest["references"]["net10.0"]))
        self.assertEqual(121, len(self.manifest["references"]["netstandard2.1"]))
        duplicate = list(self.manifest["production"])
        duplicate[-1] = duplicate[0]
        with self.assertRaises(RuntimeError):
            DRIVER.closure_shape(duplicate)

    def test_03_unique_actual_source_mutations_and_prefixes(self):
        actual = DRIVER.mutation_inventory(self.contents, self.contract, self.inventory)
        self.assertEqual(self.mutations, actual)
        self.assertEqual(17, len(actual["controls"]))
        self.assertEqual(17, len({row["mutatedSourceSha256"] for row in actual["controls"]}))
        for row in actual["controls"]:
            self.assertNotEqual(row["capturedSourceSha256"], row["mutatedSourceSha256"])
            self.assertEqual(1, self.contents[row["path"]].decode().count(row["anchor"]))
        self.assertEqual(515, actual["controls"][-1]["prefixFieldsIncludingFailure"])
        self.assertEqual(23, actual["controls"][-1]["completedCases"])

    def test_04_current_guard_all_inverse_stages(self):
        pure = DRIVER.load_pure_guard(self.guard)
        result = pure["require_caller_wiring"](self.contents[DRIVER.FLOW],
                                             self.contents[DRIVER.CALLER])
        self.assertEqual("passed-static-only", result["status"])
        self.assertFalse(result["compiled"])
        self.assertFalse(result["simulation_executed"])
        restored = pure["restore_place_kick_stats_caller"](self.contents[DRIVER.CALLER].decode())
        self.assertEqual(157533, len(restored.encode()))
        self.assertEqual("7c72a709e5510f7580cae8d623201f454ea1f747acdd0687ea1941cee57d4f72",
                         DRIVER.digest(restored.encode()))
        restored = pure["restore_play_end_stats_caller"](restored)
        self.assertEqual("ddb34cac693331d566bdbca855f08fce92599952ef10a2751017a657188a73dd",
                         DRIVER.digest(restored.encode()))

    def test_05_six_static_caller_negatives(self):
        results = DRIVER.static_negatives(self.guard, self.contents[DRIVER.FLOW],
                                          self.contents[DRIVER.CALLER])
        self.assertEqual(6, len(results))
        self.assertEqual(6, len({row["id"] for row in results}))
        self.assertTrue(all(not row["compiled"] and not row["semanticKill"]
                            and not row["liveSourceModified"] for row in results))
        STATIC_RESULTS.extend(results)

    def test_06_actual_pending_constructor_capture(self):
        text = self.contents[DRIVER.C2].decode()
        self.assertEqual(1, text.count(DRIVER.PENDING_AFTER))
        restored = DRIVER.replace_once(text, DRIVER.PENDING_AFTER, DRIVER.PENDING_BEFORE)
        self.assertEqual(20596, len(restored.encode()))
        self.assertEqual("5b1b5791e9ac34858a2de71a626bfb07269f40d98025a2967d2f3608df250c0c",
                         DRIVER.digest(restored.encode()))
        self.assertIn("ApplySourcePlayEnd(pending.Input.Cause, pending.PlayerId, pending.Position, "
                      "pending.AttackerId, pending);", text)
        self.assertIn("Model.SourceFumbleX, Model.SourceLastResult) : pending.Input;",
                      self.contents[DRIVER.CALLER].decode())

    def test_07_seven_inverses_six_constructors_and_prefix(self):
        preserved = DRIVER.preservation(self.contents, self.guard, self.contract,
                                       self.authorities["implementation-admission.json"])
        self.assertEqual(DRIVER.read_json(DRIVER.PREP / "preservation.json"), preserved)
        self.assertEqual(7, len(preserved["wholeFileInverses"]))
        self.assertEqual(6, len(preserved["oldConstructors"]))
        self.assertEqual(35, preserved["delegatedArgumentCount"])
        self.assertEqual(1461, preserved["completion"]["bytes"])
        self.assertEqual("47082185c5cc7dc25e574b99b02021a45b2db051ec1a9683914d3a041b0d3336",
                         preserved["completion"]["sha256"])
        self.assertTrue(preserved["priorChainPrefix"]["unchanged"])

    def test_08_technical_failures_cannot_be_semantic_kills(self):
        literals = DRIVER.flattened(self.inventory)
        ids = [row["id"] for row in self.inventory["producerCases"] + self.inventory["apiCases"]]
        summary = {
            "schema": "source-place-kick-statistics-component/v1",
            "status": "passed", "entryPoint": "SourcePlaceKickStatsChecks.Main",
            "completedCases": 32, "completedCaseIds": ids, "fieldCount": 603,
            "fields": [dict(row, actual=row["expected"]) for row in literals],
            "failedField": None, "error": None, "fixture": DRIVER.FIXTURE_EXPECTED.copy(),
            "genuineCommonOrSchedulerExecuted": False, "originalAssetsAdmitted": False,
            "wholeGameParityClaimed": False,
        }
        DRIVER.verify_pass(summary, self.inventory, 0)
        with self.assertRaises(RuntimeError):
            DRIVER.verify_pass(summary, self.inventory, 1)
        control = self.mutations["controls"][0]
        failed = copy.deepcopy(summary)
        count = control["prefixFieldsIncludingFailure"]
        failed.update(status="assertion-failed", completedCases=control["completedCases"],
                      completedCaseIds=ids[:control["completedCases"]], fieldCount=count,
                      failedField=control["failedField"], fields=failed["fields"][:count])
        failed["fields"][-1]["actual"] += 1
        DRIVER.verify_control(failed, self.inventory, control, 2)
        for code in (0, 1, -9):
            with self.assertRaises(RuntimeError):
                DRIVER.verify_control(failed, self.inventory, control, code)
        changed = copy.deepcopy(failed)
        changed["fields"][-1]["expected"] += 1
        with self.assertRaises(RuntimeError):
            DRIVER.verify_control(changed, self.inventory, control, 2)
        required = DRIVER.clean_environment()
        self.assertEqual("1", required["SHLVL"])
        tree = DRIVER.ast.parse((SUITE / "run.py").read_bytes())
        calls = [node for node in DRIVER.ast.walk(tree)
                 if isinstance(node, DRIVER.ast.Call) and isinstance(node.func, DRIVER.ast.Name)
                 and node.func.id == "require" and len(node.args) == 2
                 and isinstance(node.args[1], DRIVER.ast.Constant)
                 and node.args[1].value == "Python environment is not the minimal admitted environment"]
        self.assertEqual(1, len(calls))
        predicate = calls[0].args[0]
        self.assertIsInstance(predicate, DRIVER.ast.Compare)
        self.assertEqual("observed_env", predicate.left.id)
        self.assertEqual("expected_env", predicate.comparators[0].id)
        self.assertIsInstance(predicate.ops[0], DRIVER.ast.Eq)
        expression = compile(DRIVER.ast.fix_missing_locations(DRIVER.ast.Expression(predicate)),
                             "actual-strict-environment-comparison", "eval")
        self.assertTrue(eval(expression, {"__builtins__": {}},
                             {"observed_env": required.copy(), "expected_env": required}))
        missing = required.copy()
        missing.pop("SHLVL")
        wrong = dict(required, SHLVL="2")
        extra = dict(required, SYNTHETIC_UNADMITTED_KEY="synthetic-nonsecret")
        for candidate in (missing, wrong, extra):
            self.assertFalse(eval(expression, {"__builtins__": {}},
                                  {"observed_env": candidate, "expected_env": required}))
        requested = []
        private_name = "SYNTHETIC_UNKNOWN_PRIVATE_VALUE"
        def known_only(name):
            requested.append(name)
            if name == private_name:
                raise AssertionError("An unknown environment value must never be fetched.")
            return required[name]
        boundary = DRIVER.environment_boundary(required, set(required) | {private_name}, known_only)
        self.assertEqual([private_name], boundary["unknownKeyNames"])
        self.assertNotIn(private_name, requested)
        self.assertFalse(boundary["unknownKeyValuesReadHashedOrPersisted"])
        boundary = DRIVER.environment_boundary(required, wrong, wrong.__getitem__)
        self.assertEqual([{"name": "SHLVL", "expected": "1", "actual": "VALUE_NOT_PERSISTED",
                           "actualValuePersisted": False}], boundary["knownAdmittedDifferences"])
        self.assertEqual(3, DRIVER.prior_history()["consumed"]["metadata"])
        self.assertEqual(6, DRIVER.CEILINGS["metadata"])
        self.assertEqual(DRIVER.PROCESS / "continuation-03", DRIVER.JOURNAL)
        self.assertEqual(DRIVER.PROCESS / "continuation-02/phase-start.json", DRIVER.WINDOW_START)


def main():
    ledger = None
    try:
        DRIVER.require(sys.argv[1:] == ["-q"], "Only the full eight-test -q invocation is admitted")
        ledger = DRIVER.Ledger("U1")
        DRIVER.append_controller_revision()
        DRIVER.verify_input_pins(DRIVER.input_manifest(), live=True)
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(PlaceKickMetadataTests)
        result = unittest.TextTestRunner(verbosity=0).run(suite)
        passed = result.wasSuccessful() and result.testsRun == 8 and len(STATIC_RESULTS) == 6
        summary = {
            "schema": "source-place-kick-statistics-unit-result/v1",
            "status": "passed" if passed else "failed", "actualUtc": DRIVER.utc(),
            "testsRun": result.testsRun, "staticNegativeControls": STATIC_RESULTS,
            "failures": [{"test": str(test), "trace": trace} for test, trace in result.failures],
            "errors": [{"test": str(test), "trace": trace} for test, trace in result.errors],
            "oldModuleCliImported": False, "oldCampaignInvocations": 0,
            "compiled": False, "genuineSimulationExecuted": False,
        }
        output = DRIVER.write_json(DRIVER.UNIT_RESULT, summary)
        completion = ledger.finish("passed" if passed else "failed", output)
        print(json.dumps({"status": summary["status"], "unitResult": output,
                          "pythonCompletion": completion}, sort_keys=True))
        return 0 if passed else 1
    except BaseException as error:
        failure = {"status": "failed", "error": repr(error), "retryAuthorized": False}
        if ledger is not None:
            failure["pythonCompletion"] = ledger.finish("failed", failure.copy())
        print(json.dumps(failure, sort_keys=True))
        return 1


if __name__ == "__main__":
    sys.exit(main())
