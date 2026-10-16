#!/usr/bin/env python3
"""Prove the actual-source thirty-yard regression gate catches a ten-yard mutation."""

from datetime import datetime, timezone
from collections import Counter
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("core_runner", ROOT / "tests/run-core.py")
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


def main():
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    output = ROOT / "tests/.artifacts/mutations" / stamp
    output.mkdir(parents=True, exist_ok=False)
    evidence = {"timestampUtc": stamp, "status": "failed", "mutation": "FirstDownYards: 30 -> 10"}
    before = RUNNER.source_hashes(ROOT)
    evidence["sharedSourceHashesBefore"] = before
    project_name = "tests/Blitz.Core.Tests"
    copy_root = output / "isolated"
    target = copy_root / "Assets/Scripts/Contracts/GameplayContracts.cs"
    original_copy = None
    exit_code = 1
    try:
        for folder in ("Contracts", "Gameplay", "AssetImport"):
            source = ROOT / "Assets/Scripts" / folder
            for path in source.rglob("*.cs"):
                destination = copy_root / path.relative_to(ROOT)
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(path, destination)
        shutil.copytree(ROOT / project_name, copy_root / project_name,
                        ignore=shutil.ignore_patterns("bin", "obj"))
        original_copy = target.read_bytes()
        text = original_copy.decode("utf-8")
        needle = "public const int FirstDownYards = 30;"
        if text.count(needle) != 1:
            raise RuntimeError("Expected exactly one actual production thirty-yard constant to mutate.")
        target.write_text(text.replace(needle, "public const int FirstDownYards = 10;"), encoding="utf-8")
        evidence["originalConstantSourceSha256"] = hashlib.sha256(original_copy).hexdigest()
        evidence["mutatedConstantSourceSha256"] = hashlib.sha256(target.read_bytes()).hexdigest()
        report = output / "mutation.trx"
        cache = ROOT / "tests/.artifacts/runtime-cache"
        cache.mkdir(parents=True, exist_ok=True)
        command = [
            "dotnet", "test", str(copy_root / project_name / "Blitz.Core.Tests.csproj"),
            "--configuration", "Release", "--logger", "trx;LogFileName=mutation.trx",
            "--results-directory", str(output), "--verbosity", "minimal",
            "--filter", "FullyQualifiedName~ReferenceConstantsAreThirtyYardSevenPlayerFourQuarterSlice|"
                        "FullyQualifiedName~InitialSnapshotHasCompleteStableActorsAndScrimmage|"
                        "FullyQualifiedName~FirstDownAtExactlyThirtyYardsResetsDownAndTarget",
        ]
        evidence["command"] = command
        result = subprocess.run(command, cwd=copy_root,
                                env=dict(os.environ, TMPDIR=str(cache), DOTNET_CLI_TELEMETRY_OPTOUT="1"),
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        print(result.stdout, end="")
        (output / "runner.txt").write_text(result.stdout, encoding="utf-8")
        evidence["runnerExitCode"] = result.returncode
        if result.returncode == 0:
            raise RuntimeError("Mutation survived: the real ten-yard regression did not make the harness red.")
        if not report.is_file():
            raise RuntimeError("Mutation failed to build/run; a compilation failure is not behavioral evidence.")
        results = ET.parse(report).findall(".//t:UnitTestResult", RUNNER.NAMESPACE)
        observed = Counter(item.attrib["testName"].split("(", 1)[0].rsplit(".", 1)[-1] for item in results)
        expected = Counter({
            "ReferenceConstantsAreThirtyYardSevenPlayerFourQuarterSlice": 1,
            "InitialSnapshotHasCompleteStableActorsAndScrimmage": 1,
            "FirstDownAtExactlyThirtyYardsResetsDownAndTarget": 2,
        })
        if observed != expected or any(item.attrib.get("outcome") not in ("Passed", "Failed") for item in results):
            raise RuntimeError("Mutation run is missing cases or contains skipped/incomplete results.")
        evidence["results"] = dict(Counter(item.attrib["outcome"] for item in results))
        failed = [item.attrib["testName"] for item in results if item.attrib.get("outcome") == "Failed"]
        if not any("InitialSnapshotHasCompleteStableActorsAndScrimmage" in name or
                   "FirstDownAtExactlyThirtyYardsResetsDownAndTarget" in name for name in failed):
            raise RuntimeError("No actual gameplay state/rule assertion rejected the mutation; constant-only red is insufficient.")
        evidence["failedAssertions"] = failed
        evidence["status"] = "mutation-killed"
        exit_code = 0
    except (OSError, RuntimeError, ValueError, ET.ParseError) as error:
        evidence["error"] = str(error)
        print("MUTATION PROOF FAILED: " + str(error), file=sys.stderr)
    finally:
        if original_copy is not None:
            target.write_bytes(original_copy)
            evidence["isolatedCopyRestored"] = hashlib.sha256(target.read_bytes()).hexdigest() == hashlib.sha256(original_copy).hexdigest()
            if not evidence["isolatedCopyRestored"]:
                evidence["status"] = "copy-restore-failed"
                exit_code = 1
        evidence["sharedSourceHashesAfter"] = RUNNER.source_hashes(ROOT)
        evidence["sharedSourcesUnchanged"] = before == evidence["sharedSourceHashesAfter"]
        if not evidence["sharedSourcesUnchanged"]:
            evidence["status"] = "inconclusive-sources-changed"
            exit_code = 1
        (output / "evidence.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
        shutil.rmtree(copy_root)
        print("Evidence: " + str((output / "evidence.json").relative_to(ROOT)))
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
