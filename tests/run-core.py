#!/usr/bin/env python3
"""Run real xUnit tests and reject empty, skipped, or incomplete test discovery."""

import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "tests/Blitz.Core.Tests/Blitz.Core.Tests.csproj"
NAMESPACE = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def source_hashes(source_root):
    result = {}
    for folder in ("Contracts", "Gameplay", "AssetImport"):
        for path in sorted((source_root / "Assets/Scripts" / folder).rglob("*.cs")):
            result[str(path.relative_to(source_root))] = hashlib.sha256(path.read_bytes()).hexdigest()
    if not any("/Gameplay/" in name for name in result):
        raise RuntimeError("Actual Gameplay sources are missing; no substitute kernel is permitted.")
    return result


def verify_results(report, inventory):
    document = ET.parse(report)
    results = document.findall(".//t:UnitTestResult", NAMESPACE)
    if not results:
        raise RuntimeError("Runner produced zero actual test results.")
    observed = Counter()
    bad = []
    for result in results:
        name = result.attrib["testName"]
        method = name.split("(", 1)[0].rsplit(".", 1)[-1]
        observed[method] += 1
        if result.attrib.get("outcome") != "Passed":
            bad.append({"name": name, "outcome": result.attrib.get("outcome")})
    expected = Counter(inventory["requiredMethods"])
    if observed != expected:
        missing = dict(expected - observed)
        unexpected = dict(observed - expected)
        raise RuntimeError("Case inventory mismatch: missing=" + str(missing) + ", unexpected=" + str(unexpected))
    if bad:
        raise RuntimeError("Nonpassing or skipped cases: " + json.dumps(bad))
    return {"total": len(results), "passed": len(results), "failed": 0, "skipped": 0, "methods": dict(observed)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--no-restore", action="store_true")
    args = parser.parse_args()
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    output = ROOT / "tests/.artifacts/core" / stamp
    output.mkdir(parents=True, exist_ok=False)
    cache = ROOT / "tests/.artifacts/runtime-cache"
    cache.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, TMPDIR=str(cache), DOTNET_CLI_TELEMETRY_OPTOUT="1")
    command = [
        "dotnet", "test", str(PROJECT), "--configuration", "Release",
        "--logger", "trx;LogFileName=core.trx", "--results-directory", str(output),
        "--verbosity", "minimal",
    ]
    if args.no_restore:
        command.append("--no-restore")
    evidence = {
        "timestampUtc": stamp,
        "command": command,
        "scope": "Exact Contracts, Gameplay and pure AssetImport sources, not Unity/WebGL certification.",
        "status": "failed",
    }
    exit_code = 1
    try:
        before = source_hashes(ROOT)
        evidence["sources"] = before
        result = subprocess.run(command, cwd=ROOT, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if len(result.stdout) > 10000:
            print("Runner output abbreviated; complete output is in the evidence runner.txt.")
        print(result.stdout[-10000:], end="")
        (output / "runner.txt").write_text(result.stdout, encoding="utf-8")
        evidence["runnerExitCode"] = result.returncode
        if before != source_hashes(ROOT):
            raise RuntimeError("Production sources changed while tests ran; rerun against one stable source revision.")
        inventory = json.loads((PROJECT.parent / "cases.json").read_text(encoding="utf-8"))
        report = output / "core.trx"
        if not report.is_file():
            raise RuntimeError("No TRX was produced; the test command did not execute the required suite.")
        evidence["results"] = verify_results(report, inventory)
        if result.returncode != 0:
            raise RuntimeError("dotnet test returned " + str(result.returncode))
        evidence["status"] = "passed"
        exit_code = 0
        print("Verified inventory: {total} actual tests, {passed} passed, zero skipped.".format(**evidence["results"]))
    except (OSError, RuntimeError, ValueError, ET.ParseError) as error:
        evidence["error"] = str(error)
        print("CORE REGRESSION FAILED: " + str(error), file=sys.stderr)
    finally:
        (output / "evidence.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
        print("Evidence: " + str((output / "evidence.json").relative_to(ROOT)))
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
