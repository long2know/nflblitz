#!/usr/bin/env python3
"""Sixteen future real-entry regressions; no compiler or managed child is started."""
from datetime import datetime, timezone
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import sys
import unittest


ROOT = "/data/source/nflblitz/.squad/.scratch/source-next-default-audibles-45f"
CAMPAIGN = ROOT + "/.squad/.scratch/source-following-active-p1/metadata/source-fourth-down-attempts/campaign-01"
KEYS = frozenset((
    "PATH", "HOME", "TMPDIR", "TMP", "TEMP", "XDG_CACHE_HOME", "XDG_CONFIG_HOME",
    "XDG_DATA_HOME", "DOTNET_CLI_HOME", "NUGET_PACKAGES", "NUGET_HTTP_CACHE_PATH",
    "LANG", "LC_ALL", "TZ", "SHLVL", "DOTNET_ROOT", "DOTNET_CLI_TELEMETRY_OPTOUT",
    "DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "DOTNET_NOLOGO",
    "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE", "DOTNET_MULTILEVEL_LOOKUP",
    "NUGET_XMLDOC_MODE", "DOTNET_EnableDiagnostics",
))
RUN = SEALER = PACKAGE = RELEASE_ARGS = OUTPUT = None
WAITS = []


def expected_environment():
    c = CAMPAIGN
    return {
        "PATH": "/usr/bin:/bin", "HOME": c + "/home",
        "TMPDIR": c + "/tmp", "TMP": c + "/tmp", "TEMP": c + "/tmp",
        "XDG_CACHE_HOME": c + "/cache", "XDG_CONFIG_HOME": c + "/config",
        "XDG_DATA_HOME": c + "/xdg-data", "DOTNET_CLI_HOME": c + "/dotnet-home",
        "NUGET_PACKAGES": c + "/nuget-packages", "NUGET_HTTP_CACHE_PATH": c + "/nuget-http-cache",
        "LANG": "C", "LC_ALL": "C", "TZ": "UTC", "SHLVL": "1",
        "DOTNET_ROOT": "/home/long2know/.dotnet", "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1", "DOTNET_NOLOGO": "1",
        "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "1", "DOTNET_MULTILEVEL_LOOKUP": "0",
        "NUGET_XMLDOC_MODE": "skip", "DOTNET_EnableDiagnostics": "0",
    }


def verified_new_driver(argv):
    parser = argparse.ArgumentParser()
    parser.add_argument("--release", required=True)
    parser.add_argument("--release-bytes", required=True, type=int)
    parser.add_argument("--release-sha256", required=True)
    args = parser.parse_args(argv)
    expected_release = ROOT + "/.squad/.scratch/source-following-active-p1/metadata/source-fourth-down-attempts/parent-phase-c-release.json"
    if args.release != expected_release:
        raise ValueError("external-release-path")

    def pinned(row):
        if type(row["bytes"]) is not int or not 0 < row["bytes"] <= 65536:
            raise ValueError("bootstrap-size")
        descriptor = os.open(row["path"], os.O_RDONLY | os.O_NOFOLLOW)
        with os.fdopen(descriptor, "rb") as stream:
            raw = stream.read(row["bytes"] + 1)
        if len(raw) != row["bytes"] or hashlib.sha256(raw).hexdigest() != row["sha256"]:
            raise ValueError("bootstrap-pin")
        return raw

    release = json.loads(pinned({"path": args.release, "bytes": args.release_bytes,
                                 "sha256": args.release_sha256}))
    if (release.get("decision") != "ADMIT_PHASE_C_SOURCE_FOURTH_DOWN_ATTEMPTS"
            or release.get("agentId") != "e738b90a-1df9-4e80-8617-1d3878ff45ae"
            or release["package"]["path"] != CAMPAIGN + "/phase-c-inputs-r1.json"):
        raise ValueError("bootstrap-authority")
    package = json.loads(pinned(release["package"]))
    if package["environment"] != expected_environment():
        raise ValueError("bootstrap-environment")
    row = next(row for row in package["tests"]
               if row["path"] == ROOT + "/tests/source-fourth-down-attempts/run.py")
    pinned(row)
    return row["path"]


class ActualEntry(unittest.TestCase):
    def check_entry(self, entry, scenario):
        environment = RUN.expected_environment()
        expected_exit = 0
        omitted_values = []
        label = "main"
        arguments = list(RELEASE_ARGS) + ["--precheck-only"]
        if scenario == "unexpected":
            environment["BLITZ_CI_MUTATION_WORKERS"] = "synthetic-rejection-input"
            omitted_values = ["BLITZ_CI_MUTATION_WORKERS", "synthetic-rejection-input"]
            expected_exit = 93
        elif scenario == "secret-shaped":
            environment["GITHUB_TOKEN"] = "synthetic-not-a-credential"
            omitted_values = ["GITHUB_TOKEN", "synthetic-not-a-credential"]
            expected_exit = 93
        elif scenario == "original-shaped":
            environment["NFL_ORIGINAL_ROOT"] = CAMPAIGN + "/tmp/synthetic-not-original"
            omitted_values = ["NFL_ORIGINAL_ROOT", environment["NFL_ORIGINAL_ROOT"]]
            expected_exit = 93
        elif scenario == "missing":
            del environment["SHLVL"]
            expected_exit = 93
        elif scenario == "wrong-value":
            environment["HOME"] = CAMPAIGN + "/wrong-home-input"
            omitted_values = [environment["HOME"]]
            expected_exit = 94
        elif scenario == "wrong-release":
            arguments[1] = CAMPAIGN + "/not-an-authority.json"
            expected_exit = 95
        elif scenario == "reservation-conflict":
            label = "probe-driver-conflict" if entry == "driver" else "probe-waiter-conflict"
            conflict = Path(CAMPAIGN) / "execution" / label
            conflict.parent.mkdir(parents=True, exist_ok=True)
            conflict.mkdir()
            arguments += ["--execution-label", label]
            expected_exit = 96
        elif scenario != "positive":
            self.fail("Unknown authored real-entry vector")
        path = ROOT + "/tests/source-fourth-down-attempts/run.py" if entry == "driver" else CAMPAIGN + "/phase_c_waiter.py"
        argv = SEALER.literal_python_argv(environment, path, arguments)
        name = entry + "-" + scenario
        receipt, pin, stdout, stderr = SEALER.bounded_child(
            argv, RUN.expected_environment(), OUTPUT, name, 4096, 4096, 20,
            record_argv=False, record_output=False,
        )
        WAITS.append(pin)
        self.assertIsNone(receipt["technical"])
        self.assertEqual(expected_exit, receipt["actualWaitExit"])
        text = (stdout + stderr).decode("utf-8")
        for forbidden in omitted_values:
            if forbidden in text:
                self.fail("rejection-privacy-projection")
        records = [json.loads(line) for line in stdout.decode().splitlines() if line]
        final = records[-1]
        if expected_exit in (93, 94):
            self.assertEqual(0, final["metadataReads"])
        if expected_exit == 0:
            self.assertEqual("PRECHECK_ONLY", final["status"])
            self.assertEqual(0, final["compilerStarts"])
            self.assertEqual(0, final["managedStarts"])
            self.assertFalse(final["reservationCreated"])
        self.assertFalse((Path(CAMPAIGN) / "execution" / "main").exists())


def make_test(entry, scenario):
    def test(self):
        self.check_entry(entry, scenario)
    return test


for _entry in ("driver", "waiter"):
    for _scenario in ("positive", "unexpected", "secret-shaped", "original-shaped",
                      "missing", "wrong-value", "wrong-release", "reservation-conflict"):
        setattr(ActualEntry, "test_" + _entry + "_" + _scenario.replace("-", "_"),
                make_test(_entry, _scenario))


def main():
    global RUN, SEALER, PACKAGE, RELEASE_ARGS, OUTPUT
    print(json.dumps({"actualUtc": datetime.now(timezone.utc).isoformat(),
                      "entry": "fourth-down-attempts-unit"}), flush=True)
    keys = frozenset(os.environ.keys())
    print(json.dumps({"actualKeyCount": len(keys), "expectedKeyCount": len(KEYS),
                      "extraKeyCount": len(keys - KEYS), "missingKeyCount": len(KEYS - keys),
                      "safeApprovedPresentKeyNames": sorted(keys & KEYS), "metadataReads": 0}), flush=True)
    if keys != KEYS:
        print(json.dumps({"status": "REJECTED", "field": "environment-keys", "metadataReads": 0}))
        return 93
    if any(os.environ[key] != value for key, value in expected_environment().items()):
        print(json.dumps({"status": "REJECTED", "field": "environment-values", "metadataReads": 0}))
        return 94
    try:
        driver_path = verified_new_driver(sys.argv[1:])
        spec = importlib.util.spec_from_file_location(
            "_fourth_down_unit_new_driver", driver_path)
        RUN = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(RUN)
        RUN.safe_entry(emit=False)
        parsed = RUN.arguments(sys.argv[1:])
        _, PACKAGE, _ = RUN.load_release(parsed)
        SEALER = RUN.import_pinned(PACKAGE["helpers"]["input_sealer"], "_fourth_down_unit_sealer")
        for row in PACKAGE["helpers"].values():
            SEALER.check_pin(row, 65536)
        for row in PACKAGE["tests"]:
            SEALER.check_pin(row, 65536)
        OUTPUT = Path(CAMPAIGN) / "unit-entry-01"
        OUTPUT.mkdir()
        RELEASE_ARGS = ["--release", parsed.release, "--release-bytes", str(parsed.release_bytes),
                        "--release-sha256", parsed.release_sha256]
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(ActualEntry)
        result = unittest.TextTestRunner(verbosity=1).run(suite)
        SEALER.write_json(OUTPUT / "unit-result.json", {
            "schema": "source-fourth-down-attempts-real-entry-regressions/v1",
            "testsRun": result.testsRun, "successful": result.wasSuccessful(),
            "actualPrecheckChildStarts": len(WAITS), "actualWaitReceipts": WAITS,
            "compilerStarts": 0, "managedStarts": 0,
            "controlledRejectedKeysAndValuesNotPersistedInArgvReceipts": True,
        })
        return 0 if result.wasSuccessful() and result.testsRun == 16 and len(WAITS) == 16 else 1
    except Exception as error:
        code = error.code if RUN is not None and isinstance(error, RUN.GateFailure) else 97
        print(json.dumps({"status": "REJECTED_OR_INCONCLUSIVE", "exceptionType": type(error).__name__,
                          "metadataReads": 0 if RUN is None else RUN.METADATA_READS}))
        return code


if __name__ == "__main__":
    sys.exit(main())
