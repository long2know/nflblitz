#!/usr/bin/env python3
"""Future real-entry rejections and mutations of an actual closed baseline artifact."""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import sys
import unittest


ROOT = "/data/source/nflblitz/.squad/.scratch/source-next-default-audibles-45f"
CAMPAIGN = ROOT + "/.squad/.scratch/source-following-active-p1/metadata/source-after-current52-next-gap/implementation-01"
RELEASE = CAMPAIGN + "/parent-execution-release.json"
RUN = OUTPUT = SEAL = BASELINE = BASELINE_WAIT = None
WAITS = []
OBSERVED = []


def expected_environment():
    c = CAMPAIGN + "/runtime"
    return {
        "PATH": "/usr/bin:/bin", "HOME": c + "/home",
        "TMPDIR": c + "/scratch", "TMP": c + "/scratch", "TEMP": c + "/scratch",
        "XDG_CACHE_HOME": c + "/cache", "XDG_CONFIG_HOME": c + "/config",
        "XDG_DATA_HOME": c + "/xdg-data", "DOTNET_CLI_HOME": c + "/dotnet-home",
        "NUGET_PACKAGES": c + "/nuget-packages", "NUGET_HTTP_CACHE_PATH": c + "/nuget-http-cache",
        "LANG": "C", "LC_ALL": "C", "TZ": "UTC", "SHLVL": "1",
        "DOTNET_ROOT": "/home/long2know/.dotnet", "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1", "DOTNET_NOLOGO": "1",
        "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "1", "DOTNET_MULTILEVEL_LOOKUP": "0",
        "NUGET_XMLDOC_MODE": "skip", "DOTNET_EnableDiagnostics": "0",
    }


def bootstrap(argv):
    class Parser(argparse.ArgumentParser):
        def error(self, message):
            raise ValueError("arguments")
    parser = Parser(add_help=False, allow_abbrev=False)
    parser.add_argument("--release", required=True)
    parser.add_argument("--release-bytes", required=True, type=int)
    parser.add_argument("--release-sha256", required=True)
    parser.add_argument("--baseline-report-bytes", required=True, type=int)
    parser.add_argument("--baseline-report-sha256", required=True)
    args = parser.parse_args(argv)
    if args.release != RELEASE or re.fullmatch(r"[0-9a-f]{64}", args.release_sha256) is None:
        raise ValueError("external-release-identity")

    def pinned(row, expected_path):
        if (row.get("path") != expected_path or type(row.get("bytes")) is not int
                or not 0 < row["bytes"] <= 65536
                or not isinstance(row.get("sha256"), str)
                or re.fullmatch(r"[0-9a-f]{64}", row["sha256"]) is None):
            raise ValueError("bootstrap-pin-shape")
        descriptor = os.open(row["path"], os.O_RDONLY | os.O_NOFOLLOW)
        with os.fdopen(descriptor, "rb") as stream:
            if not stat.S_ISREG(os.fstat(stream.fileno()).st_mode):
                raise ValueError("bootstrap-regular-file")
            raw = stream.read(row["bytes"] + 1)
        if len(raw) != row["bytes"] or hashlib.sha256(raw).hexdigest() != row["sha256"]:
            raise ValueError("bootstrap-pin")
        return raw

    release = json.loads(pinned({"path": args.release, "bytes": args.release_bytes,
                                 "sha256": args.release_sha256}, RELEASE))
    if release.get("decision") != "ADMIT_SOURCE_TOUCHDOWN_STATS_EXECUTION_R1":
        raise ValueError("bootstrap-authority")
    packet = json.loads(pinned(release["package"], CAMPAIGN + "/execution-packet.json"))
    if packet.get("environment") != expected_environment():
        raise ValueError("bootstrap-environment")
    driver_path = ROOT + "/tests/source-touchdown-stats/run.py"
    for path in (driver_path, ROOT + "/tests/source-touchdown-stats/test_runner.py"):
        row = next(row for row in packet["writePaths"] if row["path"] == path)
        pinned(row, path)
    return args, driver_path


class ActualContracts(unittest.TestCase):
    def entry(self, scenario):
        environment = expected_environment()
        argv = ["--release", RELEASE, "--release-bytes", "1", "--release-sha256", "0" * 64]
        forbidden = []
        expected_code, expected_field = 95, "arguments"
        if scenario == "U01":
            environment["BLITZ_UNAPPROVED_SYNTHETIC"] = "synthetic-rejection-marker"
            forbidden = ["BLITZ_UNAPPROVED_SYNTHETIC", "synthetic-rejection-marker"]
            expected_code, expected_field = 93, "environment-keys"
        elif scenario == "U02":
            del environment["SHLVL"]
            expected_code, expected_field = 93, "environment-keys"
        elif scenario == "U03":
            environment["HOME"] = CAMPAIGN + "/synthetic-wrong-value"
            forbidden = [environment["HOME"]]
            expected_code, expected_field = 94, "environment-values"
        elif scenario == "U04":
            argv = []
        elif scenario == "U05":
            argv[1] = CAMPAIGN + "/not-authority.json"
            expected_field = "external-release-identity"
        elif scenario == "U06":
            argv[3] = "65537"
            expected_field = "metadata-size"
        elif scenario == "U07":
            argv[5] = "not-a-digest"
            expected_field = "external-release-identity"
        elif scenario == "U08":
            argv += ["--execution-label", "not-admitted"]
            expected_field = "execution-label"
        else:
            self.fail("unknown-real-entry-vector")
        command = ["/usr/bin/python3.12", "-I", "-S", "-B",
                   ROOT + "/tests/source-touchdown-stats/run.py"] + argv
        receipt, pin, stdout, stderr = RUN.bounded_child(
            command, environment, OUTPUT, scenario, 4096, 4096, 20, "negative-entry",
            persist_output=False, persist_argv=False)
        WAITS.append(pin)
        RUN.require_wait(receipt, "negative-entry")
        self.assertEqual(expected_code, receipt["actualWaitExit"])
        text = (stdout + stderr).decode("utf-8")
        self.assertTrue(all(value not in text for value in forbidden), "rejection-privacy-projection")
        records = [json.loads(line) for line in stdout.decode().splitlines()]
        self.assertEqual(expected_field, records[-1]["field"])
        self.assertEqual(0, records[-1]["metadataReads"])
        self.assertFalse(records[-1]["compilerOrManagedKillCredit"])
        OBSERVED.append({"id": scenario, "field": records[-1]["field"], "metadataReads": 0,
                         "waitedExit": receipt["actualWaitExit"]})

    def rejects_component(self, name, raw, field):
        try:
            RUN.component_result(raw, 0, SEAL)
        except RUN.GateFailure as error:
            self.assertEqual(97, error.code)
            self.assertEqual(field, error.field)
            OBSERVED.append({"id": name, "field": error.field})
        else:
            self.fail("actual-artifact-negative-survived")

    def test_U09_actual_baseline_fields(self):
        result = RUN.component_result(BASELINE, BASELINE_WAIT["actualWaitExit"], SEAL)
        self.assertEqual(28, result["cases"])
        self.assertEqual(78, result["assertions"])
        for line in (
            b"ASSERT|T01.SourceTouchdowns.Home|1|1|PASS",
            b"ASSERT|T03.SourceTouchdowns.Away|1|1|PASS",
            b"ASSERT|C03.ExceptionType|ArgumentOutOfRangeException|ArgumentOutOfRangeException|PASS",
            b"ASSERT|C03.ParamName|homeTouchdowns|homeTouchdowns|PASS",
        ):
            self.assertIn(line, BASELINE.splitlines())
        OBSERVED.append({"id": "U09", "cases": 28, "assertions": 78,
                         "actualFields": ["T01.SourceTouchdowns.Home", "T03.SourceTouchdowns.Away",
                                          "C03.ExceptionType", "C03.ParamName"]})

    def test_U10_actual_owner_field_tampered(self):
        raw = BASELINE.replace(b"ASSERT|T01.SourceTouchdowns.Home|1|1|PASS",
                               b"ASSERT|T01.SourceTouchdowns.Home|1|0|FAIL", 1)
        raw = raw.replace(b"SUMMARY|28|78|0|0", b"SUMMARY|28|78|1|0", 1)
        self.rejects_component("U10", raw, "component-literal-T01.SourceTouchdowns.Home")

    def test_U11_actual_exception_parameter_tampered(self):
        raw = BASELINE.replace(b"ASSERT|C03.ParamName|homeTouchdowns|homeTouchdowns|PASS",
                               b"ASSERT|C03.ParamName|homeTouchdowns|awayTouchdowns|FAIL", 1)
        raw = raw.replace(b"SUMMARY|28|78|0|0", b"SUMMARY|28|78|1|0", 1)
        self.rejects_component("U11", raw, "component-literal-C03.ParamName")

    def test_U12_actual_field_removed(self):
        raw = BASELINE.replace(b"ASSERT|T01.SourceTouchdowns.Away|0|0|PASS\n", b"", 1)
        self.rejects_component("U12", raw, "component-field-roster")

    def test_U13_actual_case_duplicated(self):
        raw = BASELINE.replace(b"CASE|T01\n", b"CASE|T01\nCASE|T01\n", 1)
        self.rejects_component("U13", raw, "component-case-roster")

    def test_U14_actual_summary_tampered(self):
        raw = BASELINE.replace(b"SUMMARY|28|78|0|0", b"SUMMARY|28|77|0|0", 1)
        self.rejects_component("U14", raw, "component-summary")

    def test_U15_actual_wait_receipt_tampered(self):
        mutated = dict(BASELINE_WAIT, waited=False)
        try:
            RUN.require_wait(mutated, "managed")
        except RUN.GateFailure as error:
            self.assertEqual(97, error.code)
            self.assertEqual("child-wait-closure", error.field)
            OBSERVED.append({"id": "U15", "field": error.field})
        else:
            self.fail("actual-wait-artifact-negative-survived")


def make_entry(scenario):
    def test(self):
        self.entry(scenario)
    return test


for _scenario in ("U01", "U02", "U03", "U04", "U05", "U06", "U07", "U08"):
    setattr(ActualContracts, "test_" + _scenario + "_real_entry", make_entry(_scenario))


def main():
    global RUN, OUTPUT, SEAL, BASELINE, BASELINE_WAIT
    print(json.dumps({"actualUtc": datetime.now(timezone.utc).isoformat(),
                      "entry": "source-touchdown-stats-unit"}), flush=True)
    keys, approved = frozenset(os.environ), frozenset(expected_environment())
    print(json.dumps({"actualKeyCount": len(keys), "expectedKeyCount": len(approved),
                      "extraKeyCount": len(keys - approved), "missingKeyCount": len(approved - keys),
                      "safeApprovedPresentKeyNames": sorted(keys & approved), "metadataReads": 0}), flush=True)
    if keys != approved:
        print(json.dumps({"status": "REJECTED", "field": "environment-keys", "metadataReads": 0}))
        return 93
    if any(os.environ[key] != value for key, value in expected_environment().items()):
        print(json.dumps({"status": "REJECTED", "field": "environment-values", "metadataReads": 0}))
        return 94
    try:
        args, driver_path = bootstrap(sys.argv[1:])
        spec = importlib.util.spec_from_file_location("_new_touchdown_runner_only", driver_path)
        RUN = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(RUN)
        RUN.safe_entry(emit=False)
        release_args = ["--release", args.release, "--release-bytes", str(args.release_bytes),
                        "--release-sha256", args.release_sha256]
        packet, execution = RUN.load_release(RUN.arguments(release_args), require_unused=False)
        report = RUN.metadata_pin({
            "path": str(execution / "driver-result.json"), "bytes": args.baseline_report_bytes,
            "sha256": args.baseline_report_sha256}, str(execution / "driver-result.json"))
        if (report.get("status") != "COMPONENT_STATIC_NS21_COMPLETE_PARENT_WAIT_AND_QUALITY_PENDING"
                or report.get("compilerStarts") != 6 or report.get("managedStarts") != 5
                or len(report.get("waits", [])) != 11 or report.get("accepted") is not False):
            raise RUN.GateFailure(97, "actual-closed-baseline-required")
        for pin in report["waits"]:
            wait = RUN.metadata_pin(pin)
            RUN.require_wait(wait, wait["kind"])
        wait_path = str(execution / "baseline/component.wait.json")
        pin = next(row for row in report["waits"] if row["path"] == wait_path)
        BASELINE_WAIT = RUN.metadata_pin(pin, wait_path)
        RUN.require_wait(BASELINE_WAIT, "managed")
        stdout_pin = BASELINE_WAIT["stdout"]
        if stdout_pin["path"] != str(execution / "baseline/component.stdout"):
            raise RUN.GateFailure(97, "actual-baseline-stdout-path")
        BASELINE = RUN.read_pin(stdout_pin, 32768)
        SEAL = RUN.metadata_pin(packet["literalSeal"], CAMPAIGN + "/literals-anchors-sealed.json")
        RUN.component_result(BASELINE, BASELINE_WAIT["actualWaitExit"], SEAL)
        OUTPUT = Path(CAMPAIGN) / "unit-entry"
        RUN.new_directory(OUTPUT)
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(ActualContracts)
        result = unittest.TestResult()
        suite.run(result)
        successful = result.wasSuccessful() and result.testsRun == 15 and len(WAITS) == 8
        RUN.write_json(OUTPUT / "unit-result.json", {
            "schema": "source-touchdown-stats-real-entry-regressions/v1",
            "successful": successful, "testsRun": result.testsRun, "actualNegativeEntryStarts": len(WAITS),
            "waits": WAITS, "observed": OBSERVED,
            "failedTests": [test.id() for test, _ in result.failures + result.errors],
            "compilerStarts": 0, "managedStarts": 0,
            "parentEntryWaitPending": True, "accepted": False,
        })
        RUN.artifact_budget(Path(CAMPAIGN))
        print(json.dumps({"status": "UNIT_COMPLETE_PARENT_WAIT_PENDING" if successful else "UNIT_FAILED",
                          "testsRun": result.testsRun, "negativeEntryStarts": len(WAITS),
                          "compilerStarts": 0, "managedStarts": 0, "accepted": False}), flush=True)
        return 0 if successful else 1
    except Exception as error:
        field = error.field if RUN is not None and isinstance(error, RUN.GateFailure) else type(error).__name__
        print(json.dumps({"status": "REJECTED_OR_INCONCLUSIVE", "field": field,
                          "compilerOrManagedKillCredit": False}), flush=True)
        return error.code if RUN is not None and isinstance(error, RUN.GateFailure) else 97


if __name__ == "__main__":
    sys.exit(main())
