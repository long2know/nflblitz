#!/usr/bin/env python3
"""Run actual-source DTO/rules checks and five isolated offending-field mutations."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid


SUITE = Path(__file__).resolve().parent
ROOT = SUITE.parents[1]
ARTIFACTS = SUITE / ".artifacts"
RULE_SOURCES = ("SourceClock.cs", "SourceMatchRules.cs", "SourceMatchFlow.cs")
EXPECTED = {
    "promoted_dto_assertions": 177,
    "enum_mappings": 65,
    "additional_dto_assertions": 23,
    "rules_flow_assertions": 122,
    "assertions": 322,
}
MUTATIONS = (
    ("roster-array-alias", "SourceConfigurationContracts.cs",
     "RosterIndices = Array.AsReadOnly(copy);",
     "RosterIndices = Array.AsReadOnly((int[])rosterIndices);",
     "SourceTeamSelection.RosterIndices detached"),
    ("score-array-alias", "SourceMatchContracts.cs",
     "QuarterScores = Array.AsReadOnly(scoresCopy);",
     "QuarterScores = Array.AsReadOnly((SourceQuarterScore[])quarterScores);",
     "SourceMatchView.QuarterScores detached per-period deltas"),
    ("selected-ordinal-leak", "SourceAnimationContracts.cs",
     "int frame = clip.SelectedSourceFrames[frameOrdinal];",
     "int frame = frameOrdinal;",
     "selected ordinals 1/2 map to authoring indices 3/5"),
    ("delayed-controller", "SourceInputContracts.cs",
     "public int ControllerPlayerId { get { return IsSpecified ? controllerPlayerId : -1; } }",
     "public int ControllerPlayerId { get { return IsSpecified && PossessionCreditEligible ? controllerPlayerId : -1; } }",
     "controller ownership independent from last_pos/credit duration"),
    ("nan-guard-removed", "SourceConfigurationContracts.cs",
     "if (float.IsNaN(value) || float.IsInfinity(value))",
     "if (float.IsInfinity(value))",
     "non-finite station turbo rejected"),
)


def source_files():
    contracts = ROOT / "Assets/Scripts/Contracts"
    required = (
        "GameplayContracts.cs", "SourceAnimationContracts.cs", "SourceConfigurationContracts.cs",
        "SourceInputContracts.cs", "SourceMatchContracts.cs",
    )
    if any(not (contracts / name).is_file() for name in required):
        raise RuntimeError("All five actual Contracts files are required; substitute DTOs are forbidden.")
    return sorted(contracts.rglob("*.cs")) + [
        ROOT / "Assets/Scripts/Gameplay" / name for name in RULE_SOURCES
    ]


def hashes():
    return {path.relative_to(ROOT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in source_files()}


def snapshot():
    contents = {path.relative_to(ROOT).as_posix(): path.read_bytes() for path in source_files()}
    captured = {name: hashlib.sha256(data).hexdigest() for name, data in contents.items()}
    if hashes() != captured:
        raise RuntimeError("Actual source inventory changed while capturing the isolated snapshot.")
    return contents, captured


def require_baseline(exit_code, summary):
    if (exit_code != 0 or not isinstance(summary, dict)
            or summary.get("schema") != "actual-source-contract-checks/v1"
            or summary.get("status") != "passed"
            or summary.get("full_gameplay_parity_claimed") is not False
            or any(summary.get(key) != count for key, count in EXPECTED.items())):
        raise RuntimeError("Expected exact passing 177 DTO / 65 mappings / 23 extra / 122 rules checks.")


def require_mutation(exit_code, summary, assertion):
    if (exit_code != 1 or not isinstance(summary, dict)
            or summary.get("schema") != "actual-source-contract-checks/v1"
            or summary.get("status") != "failed"
            or summary.get("failed_assertion") != assertion
            or not isinstance(summary.get("assertions"), int)
            or not 0 < summary["assertions"] <= EXPECTED["assertions"]):
        raise RuntimeError("Mutant did not fail its exact offending-field assertion: " + assertion)


def main():
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    workspace = ARTIFACTS / ("run-" + uuid.uuid4().hex)
    report = ARTIFACTS / "result.json"
    report.unlink(missing_ok=True)
    environment = dict(os.environ)
    cache = ARTIFACTS / "runtime-cache"
    cache.mkdir(exist_ok=True)
    environment.update(TMPDIR=str(cache), TMP=str(cache), TEMP=str(cache),
                       DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    evidence = {
        "schema": "actual-source-contract-regressions/v1",
        "status": "failed",
        "commands": [],
        "mutations": [],
        "profile": "Actual sources compiled as C#7.3 netstandard2.1, warnings-as-errors; net10.0 runner.",
        "full_gameplay_parity_claimed": False,
        "unity_editor_native_build_browser_or_hosting_executed": False,
    }
    before = None
    exit_code = 1

    def execute(label, source_root):
        project_root = workspace / label / "tests/contracts"
        for path in SUITE.rglob("*"):
            if (path.is_file() and path.suffix in (".cs", ".csproj")
                    and not any(part in (".artifacts", "bin", "obj") for part in path.relative_to(SUITE).parts)):
                copied = project_root / path.relative_to(SUITE)
                copied.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(path, copied)
        build = ["dotnet", "build", str(project_root / "ContractChecks.csproj"),
                 "--configuration", "Release", "--verbosity", "quiet",
                 "-p:BlitzSourceRoot=" + source_root.as_posix() + "/"]
        evidence["commands"].append(build)
        compiled = subprocess.run(build, cwd=ROOT, env=environment, capture_output=True,
                                  text=True, timeout=180)
        (ARTIFACTS / (label + "-build.log")).write_text(
            compiled.stdout + compiled.stderr, encoding="utf-8")
        if compiled.returncode:
            raise RuntimeError("Actual-source build failed, not an assertion rejection: " + label +
                               "; see " + label + "-build.log")
        command = ["dotnet", str(project_root / "bin/Release/net10.0/ContractChecks.dll")]
        evidence["commands"].append(command)
        process = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True,
                                 text=True, timeout=60)
        (ARTIFACTS / (label + ".log")).write_text(process.stdout + process.stderr, encoding="utf-8")
        summaries = [json.loads(line) for line in process.stdout.splitlines() if line.startswith('{"schema":')]
        summary = summaries[0] if len(summaries) == 1 else None
        return process.returncode, summary

    try:
        contents, before = snapshot()
        evidence["production_sha256_before"] = before
        code, baseline = execute("baseline", ROOT)
        evidence["baseline_exit_code"] = code
        evidence["baseline"] = baseline
        require_baseline(code, baseline)
        if hashes() != before:
            raise RuntimeError("Actual source inventory changed during the baseline.")
        for name, filename, anchor, replacement, assertion in MUTATIONS:
            source_root = workspace / name / "source"
            for relative, data in contents.items():
                path = source_root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
            relative = "Assets/Scripts/Contracts/" + filename
            target = source_root / relative
            original = target.read_text(encoding="utf-8")
            if original.count(anchor) != 1:
                raise RuntimeError("Actual-source mutation anchor must be unique: " + name)
            target.write_text(original.replace(anchor, replacement), encoding="utf-8")
            code, summary = execute(name, source_root)
            result = {
                "id": name, "production_file": relative, "before": anchor, "after": replacement,
                "source_sha256": before[relative],
                "mutated_source_sha256": hashlib.sha256(target.read_bytes()).hexdigest(),
                "exit_code": code, "summary": summary, "live_source_modified": False,
            }
            evidence["mutations"].append(result)
            require_mutation(code, summary, assertion)
            result["rejected_by_assertion"] = assertion
            result["behaviorally_rejected"] = True
            if hashes() != before:
                raise RuntimeError("Actual source inventory changed during isolated mutations.")
            print("PASS mutation: " + name + " -> " + assertion)
        code, final = execute("final-baseline", ROOT)
        evidence["final_baseline_exit_code"] = code
        evidence["final_baseline"] = final
        require_baseline(code, final)
        evidence["status"] = "passed"
        exit_code = 0
    except (OSError, RuntimeError, ValueError, subprocess.TimeoutExpired) as error:
        evidence["error"] = str(error)
        print("CONTRACT REGRESSIONS FAILED: " + str(error), file=sys.stderr)
    finally:
        if workspace.exists():
            shutil.rmtree(workspace)
        evidence["isolated_source_and_build_trees_removed"] = not workspace.exists()
        try:
            after = hashes()
            evidence["production_sha256_after"] = after
            evidence["production_hashes_unchanged"] = before is not None and after == before
        except (OSError, RuntimeError) as error:
            evidence["production_hashes_unchanged"] = False
            evidence["hash_error"] = str(error)
        if not evidence["production_hashes_unchanged"]:
            evidence["status"] = "inconclusive-sources-changed"
            exit_code = 1
        report.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
        print("Evidence: " + report.relative_to(ROOT).as_posix())
    if exit_code == 0:
        print("PASS: 322 actual-source assertions (177 promoted DTO, 23 extra guards, 122 rules/flow); "
              "five real-source mutants rejected; shared source hashes unchanged.")
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
