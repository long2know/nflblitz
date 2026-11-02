#!/usr/bin/python3.12
"""Eight fresh standard-library tests; never starts a compiler or old runner."""
import copy
from contextlib import ExitStack, contextmanager
import importlib.util
import json
from pathlib import Path
import re
import sys
import unittest
from unittest import mock

spec = importlib.util.spec_from_file_location("source_result_progression_runner", Path(__file__).with_name("run.py"))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)
STATIC_NEGATIVES = []
REVISION_IO = None


class ResultProgressionRunnerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.contract = runner.read_json(runner.OUT / "contract.json")
        cls.frozen, cls.tools = runner.verify_frozen()
        cls.inventory = runner.read_json(cls.frozen["inventory"]["path"])
        cls.controls = runner.read_json(cls.frozen["controls"]["path"])

    def test_01_frozen_literal_schema(self):
        self.assertEqual(self.inventory, runner.literal_inventory(self.contract))
        self.assertEqual(38, len(self.inventory))
        self.assertEqual(854, sum(len(row["fields"]) for row in self.inventory))
        self.assertEqual(18, len(self.controls))

    def test_02_exact_sources_references_fixture(self):
        self.assertEqual(77, len(self.frozen["sources"]))
        self.assertEqual(2, len(self.frozen["harnesses"]))
        self.assertEqual(167, len(self.tools["references"]["net10.0"]))
        self.assertEqual(121, len(self.tools["references"]["netstandard2.1"]))
        fixture = self.frozen["harnesses"][1]
        self.assertEqual(str(runner.ROOT / runner.FIXTURE_REL), fixture["path"])
        self.assertFalse(fixture["copied"])
        harness = (runner.ROOT / runner.HARNESS_REL).read_text()
        self.assertEqual(1, harness.count("Program.ComponentState()"))
        self.assertNotIn("Program.Main(", harness)
        self.assertNotIn("System.Reflection", harness)
        self.assertNotIn("FormatterServices", harness)

    def test_03_inverses_and_api_preservation(self):
        self.assertEqual(3, len(runner.inverse_production(self.contract)))
        self.assertEqual(7, len(self.contract["preservedBlocks"]["sevenConstructorsNoTrailingLf"]))
        self.assertEqual(36, len(self.contract["publicApiPlan"]["exactDelegated36Names"]))

    def test_04_current_pure_caller_six_negatives(self):
        guard = runner.load_guard(self.contract)
        flow = (runner.ROOT / "Assets/Scripts/Gameplay/SourceMatchFlow.cs").read_bytes()
        caller = (runner.ROOT / "Assets/Scripts/Gameplay/GameSimulation.Source.cs").read_bytes()
        self.assertEqual("passed-static-only", guard["require_caller_wiring"](flow, caller)["status"])
        after = (
            "            SourcePlayEndInput endingInput = pending == null ? new SourcePlayEndInput(\n"
            "                Model.SourcePlaybook, Model.SourceOffensePlay, Model.SourceRules, cause,\n"
            "                SourceBallTypeFor(Model.BallKind), Model.SourceFlags,\n")
        before = (
            "            SourcePlayEndInput endingInput = pending == null ? new SourcePlayEndInput(\n"
            "                Model.SourceRules, cause, SourceBallTypeFor(Model.BallKind), Model.SourceFlags,\n")
        text = caller.decode()
        omitted = text.replace(after, before)
        decoy = omitted + "\n/*" + after + "*/\n"
        punt = "            Model.SourceFlags |= SourceGameFlags.BallPunted;\n            Model.SourceKickReturnCatch = -1;\n"
        common = "            Model.AccumulateSourcePlayEndStats(endingInput, resolution, Model.CarrierId);\n"
        changed_flow = flow.replace(b"            int kicker = start.State.OffenseSide;\n",
                                   b"            int kicker = start.State.OffenseSide; // changed\n")
        self.assertNotEqual(flow, changed_flow)
        variants = [
            (flow, omitted.encode()),
            (flow, decoy.encode()),
            (flow, caller + b"\n"),
            (flow, text.replace(punt, punt.replace("= -1;", "= -2;")).encode()),
            (flow, text.replace(common, common + common).encode()),
            (changed_flow, caller),
        ]
        plans = self.contract["unchangedLiveDependenciesAndCallerProof"]["pureStaticGuardReuse"]["staticNegatives"]
        for (new_flow, new_caller), (name, field) in zip(variants, plans):
            with self.assertRaises(guard["StaticAssertionFailure"]) as caught:
                guard["require_caller_wiring"](new_flow, new_caller)
            self.assertEqual(field, caught.exception.field)
            STATIC_NEGATIVES.append({"id": name, "firstFailure": field})
        self.assertEqual(6, len(STATIC_NEGATIVES))

    def clean_report(self):
        return {"status": "passed", "cases": [r["id"] for r in self.inventory],
                "fields": [{"case": row["id"], "field": name, "expected": value, "actual": value}
                           for row in self.inventory for name, value in zip(row["fields"], row["expected"])]}

    def test_05_ordered_literal_parser_rejects_false_success(self):
        good = self.clean_report()
        self.assertEqual(854, runner.validate_report(good, self.inventory, 0)["fields"])
        variants = []
        missing = copy.deepcopy(good)
        missing["fields"].pop()
        variants.append(missing)
        reordered = copy.deepcopy(good)
        reordered["fields"][0], reordered["fields"][1] = reordered["fields"][1], reordered["fields"][0]
        variants.append(reordered)
        golden = copy.deepcopy(good)
        golden["fields"][0]["expected"] += 1
        golden["fields"][0]["actual"] += 1
        variants.append(golden)
        variants.append({"status": "passed", "cases": good["cases"], "fields": []})
        for bad in variants:
            with self.assertRaises(runner.ProtocolFailure):
                runner.validate_report(bad, self.inventory, 0)

    def test_06_single_real_mutations_and_exact_prefixes(self):
        for control in self.controls:
            case, index = runner.control_expectations(control, self.inventory)
            original = Path(control["source"]["path"]).read_bytes()
            mutant = Path(control["mutated"]["path"]).read_bytes()
            anchor = control["anchor"].encode()
            self.assertEqual(1, original.count(anchor))
            self.assertEqual(mutant, original.replace(anchor, control["replacement"].encode()))
            report = {"status": "assertion-failed", "cases": [],
                      "fields": [{"case": case["id"], "field": case["fields"][i],
                                  "expected": case["expected"][i], "actual": case["expected"][i]}
                                 for i in range(index)]}
            failure = {"case": control["case"], "field": control["field"],
                       "expected": control["expected"], "actual": control["actual"]}
            report["fields"].append(failure)
            report["failure"] = failure
            runner.validate_report(report, self.inventory, 2, control)
            with self.assertRaises(runner.ProtocolFailure):
                runner.validate_report(report, self.inventory, 3, control)

    def test_07_strict_environment_and_redaction(self):
        expected = runner.environment()
        runner.check_environment(expected, expected)
        for changed in (dict(expected, SHLVL="2"),
                        dict(expected, HOME="SENTINEL_SECRET_NOT_PERSISTED"),
                        dict(expected, UNKNOWN_KEY="SENTINEL_SECRET_NOT_PERSISTED"),
                        dict(expected, _="SENTINEL_SECRET_NOT_PERSISTED")):
            with self.assertRaises(runner.ProtocolFailure) as caught:
                runner.check_environment(changed, expected)
            encoded = json.dumps(caught.exception.details)
            self.assertNotIn("SENTINEL_SECRET_NOT_PERSISTED", encoded)
            self.assertEqual("VALUE_NOT_PERSISTED", caught.exception.details["value"])
            self.assertIs(False, caught.exception.details["accepted"])
        runner.check_environment(dict(expected, _=runner.PYTHON), expected)

    def test_08_budgets_and_technical_errors_are_not_kills(self):
        self.assertEqual({"metadata": 3, "unit": 1, "driver": 1, "compiler": 21, "managed": 20}, runner.LIMITS)
        self.assertEqual(67108864, runner.MAX_BYTES)
        self.assertEqual(4194304, runner.CHILD_BYTES)
        self.assertGreater(runner.time_left(), 0)
        with self.assertRaises(runner.ProtocolFailure):
            runner.validate_report({"status": "technical-error", "cases": [], "fields": []},
                                   self.inventory, 3, self.controls[0])
        with self.assertRaises(runner.ProtocolFailure):
            runner.unique_json(b'{"key":1,"key":2}')
        self.assertLess(runner.artifact_bytes(), runner.MAX_BYTES)


def main():
    try:
        runner.enter("U1", "unit")
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(ResultProgressionRunnerTests)
        result = unittest.TextTestRunner(verbosity=1).run(suite)
        passed = result.wasSuccessful()
        runner.write_json(runner.VALID / "U1-result.json",
                          {"status": "passed" if passed else "failed-stop-no-retry",
                           "testsRun": result.testsRun, "passed": passed,
                           "failures": len(result.failures), "errors": len(result.errors),
                           "staticNegatives": STATIC_NEGATIVES, "actualUtc": runner.utc()})
        runner.finish_return(0 if passed else 1)
        return 0 if passed else 1
    except Exception as error:
        runner.fail(error)
        return 1


class RevisionEntryEnvironmentTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if REVISION_IO is None:
            raise RuntimeError("revision.context-required")
        cls.io = REVISION_IO
        cls.fixture_sequence = 0
        cls.fixture_ids = []

    def safe(self, condition, field):
        self.assertTrue(bool(condition), field)

    @contextmanager
    def fixture(self, change=None, bad_authority=False):
        cls = type(self)
        cls.fixture_sequence += 1
        case_id = "entry-" + str(cls.fixture_sequence).zfill(2)
        cls.fixture_ids.append(case_id)
        root = self.io.PREP / "unit-fixtures" / case_id
        outcome = root / "outcome"
        proc, valid, prep = (outcome / name for name in ("process-state", "validation", "preparation"))
        self.io.write_json(root / "fixture.json",
                           {"schema": "revision-synthetic-in-process-entry-fixture/v1",
                            "id": case_id, "test": self._testMethodName,
                            "syntheticInProcess": True, "actualChildLaunches": 0,
                            "actualUnitPid": runner.os.getpid(), "actualUtc": runner.utc()})
        for name in ("contract.json", "implementation-admission.json", "execution-admission.json"):
            raw = (self.io.D / name).read_bytes()
            if bad_authority and name == "implementation-admission.json":
                raw += b" "
            self.io.write_bytes(outcome / name, raw)
        expected = self.io.expected_environment()
        execution = self.io.read_json(self.io.D / "execution-admission.json")
        for key, suffix in execution["environmentOverride"]["allInsideThisOutcome"].items():
            expected[key] = str(outcome / suffix)
        self.io.write_json(prep / "environment.json", expected)
        phase = "REVISION-ENTRY-FIXTURE"
        outer = {"utcEpochSeconds": str(runner.datetime.now(runner.timezone.utc).timestamp()),
                 "syntheticInProcess": True, "actualChildLaunches": 0, "fixture": case_id}
        self.io.write_json(proc / "outer-M1.reservation.json", outer)
        self.io.write_json(proc / ("outer-" + phase + ".reservation.json"), outer)
        actual = dict(expected)
        if change is not None:
            change(actual)
        with ExitStack() as stack:
            stack.enter_context(mock.patch.multiple(
                runner, OUT=outcome, PREP=prep, PROC=proc, VALID=valid,
                PHASE=None, ENTRY=None, DEADLINE=None))
            stack.enter_context(mock.patch.object(runner.os, "environ", actual))
            stack.enter_context(mock.patch.object(runner.signal, "signal"))
            stack.enter_context(mock.patch.object(runner.signal, "setitimer"))
            yield {"root": root, "outcome": outcome, "proc": proc, "valid": valid,
                   "phase": phase, "expected": expected, "actual": actual}

    def reservation(self, fixture):
        return self.io.read_json(fixture["proc"] / (fixture["phase"] + ".reservation.json"))

    def assert_safe_metadata(self, reservation):
        names = reservation["details"]["safeActualEnvironmentKeyNames"]
        self.safe(isinstance(names, list) and all(
            isinstance(name, str) and re.fullmatch(r"[A-Za-z_][A-Za-z_0-9]{0,127}", name)
            for name in names), "entry.persisted-key-name-policy")
        self.safe(names == sorted(names), "entry.projected-token-order")

    def reject(self, fixture, field):
        try:
            runner.enter(fixture["phase"], "unit")
        except runner.ProtocolFailure as error:
            self.safe(error.field == field, "entry.exact-rejection-field")
            runner.fail(error)
        else:
            self.fail("entry.rejection-required")
        reservation = self.reservation(fixture)
        self.assert_safe_metadata(reservation)
        self.safe(not (fixture["proc"] / (fixture["phase"] + ".entry.json")).exists(),
                  "entry.no-false-success-entry")
        failure = self.io.read_json(fixture["valid"] / (fixture["phase"] + "-failure.json"))
        self.safe(failure["field"] == field, "entry.persisted-rejection-field")
        self.safe(len(list(fixture["proc"].glob(fixture["phase"] + ".reservation.json"))) == 1,
                  "entry.rejection-charged")
        return reservation, failure

    def receipt_bytes(self, fixture):
        paths = sorted(fixture["proc"].glob(fixture["phase"] + ".*.json"))
        paths += sorted(fixture["valid"].glob("*.json"))
        return b"".join(path.read_bytes() for path in paths)

    def assert_value_private(self, fixture, failure):
        self.safe(failure["details"]["value"] == "VALUE_NOT_PERSISTED"
                  and failure["details"]["accepted"] is False, "entry.value-diagnostic-private")
        self.safe(b"REVISION_SYNTHETIC_UNTRUSTED_VALUE" not in self.receipt_bytes(fixture),
                  "entry.unexpected-value-absent")
        self.safe(self.reservation(fixture)["environment"] == fixture["expected"],
                  "entry.only-expected-values-persisted")

    def test_01_exact_environment(self):
        with self.fixture() as fixture:
            runner.enter(fixture["phase"], "unit")
            reservation = self.reservation(fixture)
            self.assert_safe_metadata(reservation)
            self.safe(reservation["details"]["safeActualEnvironmentKeyNames"]
                      == sorted(fixture["expected"]), "entry.exact28-name-projection")
            entry = self.io.read_json(fixture["proc"] / (fixture["phase"] + ".entry.json"))
            start = runner.datetime.fromisoformat(reservation["details"]["entryActualUtcBeforeChecks"])
            reserved = runner.datetime.fromisoformat(reservation["reservedActualUtc"])
            self.safe(start.utcoffset().total_seconds() == 0 and reserved.utcoffset().total_seconds() == 0
                      and start <= reserved, "entry.utc-before-reservation")
            self.safe(entry["environmentCheck"]["exact"] is True
                      and len(entry["environmentCheck"]["actualKeys"]) == 28,
                      "entry.actual-exact-environment")

    def test_02_safe_unknown(self):
        name = "REVISION_SYNTHETIC_UNKNOWN_KEY"
        with self.fixture(lambda actual: actual.update({name: "REVISION_SYNTHETIC_UNTRUSTED_VALUE"})) as fixture:
            reservation, failure = self.reject(fixture, "environment.unknown-keys")
            self.safe(reservation["details"]["safeActualEnvironmentKeyNames"]
                      == sorted(list(fixture["expected"]) + [name]), "entry.safe-unknown-represented")
            self.safe(failure["details"]["keys"] == [name], "entry.raw-keyset-still-rejects")
            self.assert_value_private(fixture, failure)

    def test_03_malformed_and_overlength(self):
        names = ("SYNTHETIC-KEY\n", "SYNTHETIC_" + chr(233),
                 "SYNTHETIC_" + chr(0xDCFF), "X" * 129)
        for name in names:
            with self.fixture(lambda actual: actual.update(
                    {name: "REVISION_SYNTHETIC_UNTRUSTED_VALUE"})) as fixture:
                reservation, failure = self.reject(fixture, "environment.unknown-keys")
                self.safe(reservation["details"]["safeActualEnvironmentKeyNames"]
                          == sorted(list(fixture["expected"]) + ["KEY_NAME_REDACTED"]),
                          "entry.unsafe-name-redacted-before-write")
                self.safe(failure["details"]["keys"] == ["KEY_NAME_REDACTED"],
                          "entry.failure-name-redacted")
                self.safe(json.dumps(name).encode() not in self.receipt_bytes(fixture),
                          "entry.raw-unsafe-name-absent")
                self.assert_value_private(fixture, failure)

    def test_04_length_boundary_and_multiplicity(self):
        name = "N" * 128
        with self.fixture(lambda actual: actual.update(
                {name: "REVISION_SYNTHETIC_UNTRUSTED_VALUE"})) as fixture:
            reservation, failure = self.reject(fixture, "environment.unknown-keys")
            self.safe(reservation["details"]["safeActualEnvironmentKeyNames"]
                      == sorted(list(fixture["expected"]) + [name]), "entry.128-name-representable")
            self.assert_value_private(fixture, failure)
        def multiple(actual):
            actual.update({"A" * 129: "REVISION_SYNTHETIC_UNTRUSTED_VALUE",
                           "B" * 129: "REVISION_SYNTHETIC_UNTRUSTED_VALUE",
                           "KEY_NAME_REDACTED": "REVISION_SYNTHETIC_UNTRUSTED_VALUE"})
        with self.fixture(multiple) as fixture:
            reservation, failure = self.reject(fixture, "environment.unknown-keys")
            self.safe(reservation["details"]["safeActualEnvironmentKeyNames"]
                      == sorted(list(fixture["expected"]) + ["KEY_NAME_REDACTED"] * 3),
                      "entry.redaction-multiplicity")
            self.safe(failure["details"]["keys"] == ["KEY_NAME_REDACTED"] * 3,
                      "entry.marker-never-grants-acceptance")
            self.assert_value_private(fixture, failure)

    def test_05_strict_values_and_missing(self):
        changes = (lambda actual: actual.update(SHLVL="2"),
                   lambda actual: actual.update(HOME="REVISION_SYNTHETIC_UNTRUSTED_VALUE"))
        for change in changes:
            with self.fixture(change) as fixture:
                _, failure = self.reject(fixture, "environment.value-mismatch")
                self.assert_value_private(fixture, failure)
        with self.fixture(lambda actual: actual.pop("HOME")) as fixture:
            _, failure = self.reject(fixture, "environment.missing-keys")
            self.safe(failure["details"] == {"keys": ["HOME"]}, "entry.missing-shape-unchanged")

    def test_06_exact_underscore(self):
        for present in (False, True):
            change = (lambda actual: actual.update(_=runner.PYTHON)) if present else None
            with self.fixture(change) as fixture:
                runner.enter(fixture["phase"], "unit")
                reservation = self.reservation(fixture)
                self.assert_safe_metadata(reservation)
                entry = self.io.read_json(fixture["proc"] / (fixture["phase"] + ".entry.json"))
                self.safe(entry["environmentCheck"]["exact"] is True
                          and entry["environmentCheck"]["underscorePresent"] is present,
                          "entry.exact-underscore-exception")
        with self.fixture(lambda actual: actual.update(_="REVISION_SYNTHETIC_UNTRUSTED_VALUE")) as fixture:
            _, failure = self.reject(fixture, "environment.underscore")
            self.assert_value_private(fixture, failure)

    def test_07_before_authority_and_charging(self):
        def malformed(actual):
            actual["SYNTHETIC-\n"] = "REVISION_SYNTHETIC_UNTRUSTED_VALUE"
        with self.fixture(malformed, bad_authority=True) as fixture:
            reservation, failure = self.reject(fixture, "input.pin")
            self.safe(reservation["details"]["safeActualEnvironmentKeyNames"]
                      == sorted(list(fixture["expected"]) + ["KEY_NAME_REDACTED"]),
                      "entry.safe-even-before-authority-rejection")
            self.safe(b"REVISION_SYNTHETIC_UNTRUSTED_VALUE" not in self.receipt_bytes(fixture),
                      "entry.authority-failure-value-private")
            path = fixture["proc"] / (fixture["phase"] + ".reservation.json")
            first = path.read_bytes()
            try:
                runner.enter(fixture["phase"], "unit")
            except runner.ProtocolFailure as error:
                self.safe(error.field == "reservation.unit-cap", "entry.failure-stays-charged")
                runner.write_json(fixture["valid"] / "second-cap-failure.json",
                                  {"field": error.field, "actualUtc": runner.utc(),
                                   "syntheticInProcess": True, "actualChildLaunches": 0})
            else:
                self.fail("entry.second-attempt-must-not-refund")
            self.safe(path.read_bytes() == first, "entry.first-reservation-immutable")

    def test_08_emitted_artifact_negative(self):
        with self.fixture(lambda actual: actual.update(
                {"SYNTHETIC-KEY": "REVISION_SYNTHETIC_UNTRUSTED_VALUE"})) as fixture:
            reservation, _ = self.reject(fixture, "environment.unknown-keys")
            mutated = copy.deepcopy(reservation)
            names = mutated["details"]["safeActualEnvironmentKeyNames"]
            names[names.index("KEY_NAME_REDACTED")] = "SYNTHETIC-UNSAFE\n"
            with self.assertRaises(AssertionError):
                self.assert_safe_metadata(mutated)
            self.safe(json.dumps("SYNTHETIC-UNSAFE\n").encode() not in self.receipt_bytes(fixture),
                      "entry.artifact-negative-memory-only")


class SafeRevisionResult(unittest.TestResult):
    def __init__(self):
        super().__init__()
        self.test_names = []

    def startTest(self, test):
        self.test_names.append(test._testMethodName)
        super().startTest(test)

    def addFailure(self, test, error):
        self.failures.append({"test": test._testMethodName, "kind": "assertion-failed"})

    def addError(self, test, error):
        self.errors.append({"test": test._testMethodName, "kind": "technical-error"})


def revision_main():
    global REVISION_IO
    started = runner.utc()
    try:
        path = runner.OUT / "revision-01/preparation/revision_metadata.py"
        spec = importlib.util.spec_from_file_location("revision_metadata_for_unit", path)
        REVISION_IO = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(REVISION_IO)
        REVISION_IO.unit_begin(started)
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(RevisionEntryEnvironmentTests)
        result = SafeRevisionResult()
        suite.run(result)
        passed = (result.wasSuccessful() and result.testsRun == 8
                  and result.test_names == REVISION_IO.TEST_NAMES)
        REVISION_IO.write_json(
            REVISION_IO.VALID / "R1-U1-result.json",
            {"status": "passed-focused-entry" if passed else "failed-stop-no-retry",
             "actualUtc": runner.utc(), "pid": runner.os.getpid(), "parentPid": runner.os.getppid(),
             "testsRun": result.testsRun, "testNames": result.test_names, "passed": passed,
             "failures": result.failures, "errors": result.errors,
             "syntheticInProcessFixtureIds": RevisionEntryEnvironmentTests.fixture_ids,
             "actualFixtureChildLaunches": 0, "gameplayExecutions": 0,
             "controllerInputRevision": REVISION_IO.identity(
                 REVISION_IO.PREP / "controller-input-revision.json"),
             "notAWaitedSelfExit": True})
        REVISION_IO.remaining()
        return 0 if passed else 1
    except BaseException as error:
        if REVISION_IO is not None:
            REVISION_IO.record_failure(error)
        else:
            print(json.dumps({"status": "failed-stop-no-retry", "field": "unit.bootstrap"}))
        return 1


if __name__ == "__main__":
    sys.exit(revision_main() if "--revision-unit" in sys.argv else main())
