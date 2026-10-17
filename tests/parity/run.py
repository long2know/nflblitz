#!/usr/bin/env python3
"""Compare exact production C# with original-C outputs and reject an isolated source mutation."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

from prepare_inputs import fixture_hashes
from contact_inputs import ANIMATION_SHA as CONTACT_ANIMATION_SHA, fixture_profile
from prepare_inputs import ANIMATION_SHA
from kernel_sources import production_paths, canonical_hashes, C2_ANIMATION_SHA

SUITE = Path(__file__).resolve().parent
ROOT = SUITE.parents[1]
ARTIFACTS = SUITE / ".artifacts"


def source_files():
    return production_paths()


def hashes(paths):
    return {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths}

def stable_production_snapshot():
    for _ in range(3):
        contents = {str(path.relative_to(ROOT)): path.read_bytes() for path in source_files()}
        captured = {name: hashlib.sha256(data).hexdigest() for name, data in contents.items()}
        if hashes(source_files()) == captured:
            return contents, captured
    raise RuntimeError("Production source inventory is changing; no coherent isolated mutation snapshot was obtained.")


def require_full_kernel(summary, animation_sha):
    if (not isinstance(summary, dict) or summary.get("status") != "passed"
            or summary.get("full_kernel_host_adapter_exercised") is not True
            or summary.get("contract_assertions") != 332
            or summary.get("real_animation_library_sha256") != animation_sha):
        raise RuntimeError("Actual full-kernel332 adapter checks and explicit animation identity are required.")


def main():
    global ARTIFACTS
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cases", type=Path, required=True)
    parser.add_argument("--full-kernel", action="store_true", help="Also compile and exercise the actual source-profile host adapter.")
    parser.add_argument("--fixture-manifest", type=Path, help="Explicit pinned real-input fixture.json, required with --full-kernel.")
    parser.add_argument("--sound-functions", type=Path, help="Explicit frozen SAFN input for actual source VM execution.")
    parser.add_argument("--c2-inputs", type=Path, help="Explicit frozen current281 inputs with real C2 composition.")
    parser.add_argument("--output", type=Path, help="New ignored child output; preserve historical matrix receipts.")
    parser.add_argument("--input-profile", choices=("legacy", "contact272"), default="legacy",
                        help="Select the explicit production272 fixture without changing legacy117/157 defaults.")
    args = parser.parse_args()
    if args.output is not None:
        selected = args.output.absolute()
        if not selected.is_relative_to(SUITE / ".artifacts") or selected.exists():
            parser.error("--output requires a new tests/parity/.artifacts child.")
        ARTIFACTS = selected
    if args.full_kernel and args.fixture_manifest is None:
        parser.error("--full-kernel requires --fixture-manifest; synthetic/geometry fallbacks are forbidden.")
    fixture_path = args.fixture_manifest.resolve() if args.fixture_manifest is not None else None
    profile = fixture_profile(fixture_path, args.input_profile) if args.full_kernel else None
    animation_sha = C2_ANIMATION_SHA if args.c2_inputs is not None else (
        CONTACT_ANIMATION_SHA if profile == "contact272" else ANIMATION_SHA)
    fixture_before = fixture_hashes(fixture_path, args.sound_functions) if args.full_kernel else None
    if args.c2_inputs is not None:
        if not args.full_kernel:
            parser.error("--c2-inputs requires the actual full-kernel host adapter.")
        fixture_before.update(canonical_hashes(args.c2_inputs))
    case_path = args.cases.resolve()
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    mode = "full-kernel" if args.full_kernel else "focused"
    report_path = ARTIFACTS / ("result-full-kernel.json" if args.full_kernel else "result.json")
    report_path.unlink(missing_ok=True)
    if not case_path.is_file():
        raise SystemExit("Required original C outputs are missing; run tests/source-oracles/run.py first.")
    original_receipt_path = case_path.parent / "result.json"
    original_receipt = json.loads(original_receipt_path.read_text(encoding="utf-8"))
    case_hash = hashlib.sha256(case_path.read_bytes()).hexdigest()
    if (original_receipt.get("status") != "passed"
            or original_receipt.get("cases_sha256") != case_hash
            or original_receipt.get("executed_original_c_cases", 0) == 0):
        raise SystemExit("Original-C execution receipt does not match the actual case artifact.")
    environment = dict(os.environ)
    for key, subdir in (("TMPDIR", "work"), ("DOTNET_CLI_HOME", "dotnet-home"), ("NUGET_PACKAGES", "nuget")):
        directory = ARTIFACTS / subdir
        directory.mkdir(exist_ok=True)
        environment[key] = str(directory)
    environment.update(
        TMP=environment["TMPDIR"], TEMP=environment["TMPDIR"],
        DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
    )
    commands = []
    validation_build = ARTIFACTS / ("validation-build-" + uuid.uuid4().hex)

    def execute(label, source_root=None, build_root=None):
        command = [
            "dotnet", "run", "--project", str(SUITE / "ParityChecks.csproj"),
            "--configuration", "Release", "--verbosity", "quiet",
        ]
        if source_root is not None:
            command.append("-p:BlitzSourceRoot=" + str(source_root) + "/")
        if args.full_kernel:
            command.append("-p:BlitzFullKernel=true")
        build_root = build_root or validation_build
        command += ["-p:BaseIntermediateOutputPath=" + str(build_root / "obj") + "/",
                    "-p:BaseOutputPath=" + str(build_root / "bin") + "/"]
        command += ["--", str(case_path), str(SUITE / "fixtures/synthetic-contract-traces.json")]
        if args.full_kernel:
            command.append(str(fixture_path))
            if args.sound_functions is not None:
                command.append(str(args.sound_functions.resolve()))
            if args.c2_inputs is not None:
                command += ["--c2-inputs", str(args.c2_inputs.absolute())]
        commands.append(command)
        process = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=180)
        text = process.stdout + process.stderr
        (ARTIFACTS / (mode + "-" + label + ".log")).write_text(text, encoding="utf-8")
        summary = None
        for line in process.stdout.splitlines():
            if line.startswith('{"status":'):
                summary = json.loads(line)
        return process, summary, text

    paths = source_files()
    before = hashes(paths)
    workspace = ARTIFACTS / ("isolated-mutation-" + uuid.uuid4().hex)
    evidence = {
        "schema": "original-c-to-production-parity-run/v1",
        "status": "failed", "original_c_receipt": original_receipt,
        "original_c_cases_sha256": case_hash,
        "production_sha256_before": before, "commands": commands,
        "mutation": {"executed": False}, "browser_or_unity_executed": False,
        "full_gameplay_parity_claimed": False,
        "full_kernel_adapter_requested": args.full_kernel,
        "input_profile": profile, "expected_animation_sha256": animation_sha if args.full_kernel else None,
        "real_input_sha256_before": fixture_before,
    }
    try:
        baseline, summary, text = execute("baseline")
        evidence["baseline"] = summary
        evidence["baseline_exit_code"] = baseline.returncode
        if baseline.returncode or summary is None or summary.get("status") != "passed":
            print(text)
            return 1
        if args.full_kernel:
            require_full_kernel(summary, animation_sha)
        if summary["executed_original_c_cases"] != original_receipt["executed_original_c_cases"]:
            raise RuntimeError("Production run did not consume the complete original-C case inventory.")
        contents, snapshot_hashes = stable_production_snapshot()
        evidence["mutation_production_snapshot_sha256"] = snapshot_hashes
        for name, data in contents.items():
            copied = workspace / "source" / name
            copied.parent.mkdir(parents=True, exist_ok=True)
            copied.write_bytes(data)
        mutated = workspace / "source/Assets/Scripts/Gameplay/SourceMatchRules.cs"
        original = mutated.read_text(encoding="utf-8")
        anchor = "/ 78) * 7.8f"
        if original.count(anchor) != 1:
            raise RuntimeError("Actual production rounding mutation anchor is not unique.")
        mutated.write_text(original.replace(anchor, "/ 79) * 7.8f"), encoding="utf-8")
        mutation, mutation_summary, mutation_text = execute("mutation-rounding", workspace / "source", workspace / "build")
        evidence["mutation"] = {
            "executed": True, "source": "Assets/Scripts/Gameplay/SourceMatchRules.cs",
            "mutation": "Change actual copied RoundPlayEndPosition integer divisor 78 -> 79",
            "live_source_modified": False, "exit_code": mutation.returncode, "summary": mutation_summary,
        }
        if (mutation.returncode != 1 or mutation_summary is None
                or mutation_summary.get("status") != "failed"
                or mutation_summary.get("failures", 0) == 0
                or ".play_end_pos_bits" not in mutation_text):
            print(mutation_text)
            raise RuntimeError("Isolated actual C# rounding mutation did not fail the intended original-C numeric comparison.")
        final, final_summary, final_text = execute("final-baseline")
        evidence["final_baseline"] = final_summary
        evidence["final_baseline_exit_code"] = final.returncode
        if final.returncode or final_summary is None or final_summary.get("status") != "passed":
            print(final_text)
            return 1
        if args.full_kernel:
            require_full_kernel(final_summary, animation_sha)
        evidence["status"] = "passed"
        print("PASS: original-C / exact production C# comparison; actual isolated rounding mutation rejected.")
        print(json.dumps(final_summary, separators=(",", ":")))
        return 0
    finally:
        if workspace.exists():
            shutil.rmtree(workspace)
        if validation_build.exists():
            shutil.rmtree(validation_build)
        evidence["isolated_mutation_source_removed"] = not workspace.exists()
        evidence["isolated_validation_build_removed"] = not validation_build.exists()
        evidence["production_sha256_after"] = hashes(source_files())
        evidence["production_hashes_unchanged"] = evidence["production_sha256_after"] == before
        evidence["real_input_sha256_after"] = fixture_hashes(fixture_path, args.sound_functions) if args.full_kernel else None
        if args.c2_inputs is not None:
            evidence["real_input_sha256_after"].update(canonical_hashes(args.c2_inputs))
        evidence["real_input_hashes_unchanged"] = evidence["real_input_sha256_after"] == fixture_before
        stable = evidence["production_hashes_unchanged"] and evidence["real_input_hashes_unchanged"]
        if not stable:
            evidence["status"] = "inconclusive-inputs-or-sources-changed"
        report_path.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
        if not stable:
            raise RuntimeError("Actual production sources or pinned real inputs changed during validation.")


if __name__ == "__main__":
    sys.exit(main())
