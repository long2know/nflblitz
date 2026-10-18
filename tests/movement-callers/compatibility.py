#!/usr/bin/env python3
"""Declared strict/portable real-factory cases and compiled controls; never a public replay."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess

from canonical import HARNESS, ROOT
from kernel_sources import production_paths, canonical_hashes
from prepare_inputs import fixture_hashes

GAME = "Assets/Scripts/Gameplay/"
MUTANTS = (
    ("wrong-handicap-last-row", GAME + "SimulationState.cs",
     "Math.Min(length - 1, raw)", "Math.Min(length - 2, raw)",
     "portable raw arithmetic preserved; consumer index saturates11"),
    ("mutate-raw-handicap", GAME + "GameSimulation.FireHandicap.cs",
     "            for (int i = 0; i < levels.Length; i++)",
     "            if (Compatibility == Blitz.Contracts.SourceGameplayCompatibility.Portable) levels[1] = Math.Min(11, levels[1]);\n"
     "            for (int i = 0; i < levels.Length; i++)",
     "portable raw arithmetic preserved; consumer index saturates11"),
    ("wrong-block-last-row", GAME + "SourceContactDriver.Dispatch.cs",
     "                int adjustment = adjustments[blockIndex];",
     "                if (m.SourceCompatibility == SourceGameplayCompatibility.Portable && rusher.SourceBlockCount >= 6) blockIndex = 0;\n"
     "                int adjustment = adjustments[blockIndex];",
     "portable bc consumer uses last existing row with identical draws/reactions"),
    ("mutate-raw-block-count", GAME + "SourceContactDriver.Dispatch.cs",
     "rusher.SourceBlockCount++;", "rusher.SourceBlockCount = blockIndex + 1;",
     "actual raw block count increments without saturation"),
    ("silent-mode-promotion", "Assets/Scripts/Runtime/SourceC2Composition.cs",
     "movementData, movementSettings, c2Bootstrap, handicapSettings, compatibility);",
     "movementData, movementSettings, c2Bootstrap, handicapSettings, SourceGameplayCompatibility.Portable);",
     "actual M1 omitted option stays strict"),
    ("extra-portable-RNG", GAME + "GameSimulation.Source.cs",
     "            Model.SourceHandicapSettings = sourceHandicapSettings;",
     "            Model.SourceHandicapSettings = sourceHandicapSettings;\n"
     "            if (Compatibility == SourceGameplayCompatibility.Portable) SourceDraw(100);",
     "in-domain RNG identical with no compatibility draw"),
    ("silent-repair", GAME + "SimulationState.cs",
     "            SourceCompatibilityRepairs.Add(new SourceCompatibilityRepair(",
     "            if (effective == raw) SourceCompatibilityRepairs.Add(new SourceCompatibilityRepair(",
     "portable repair is not silent"),
)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ("fixture-manifest", "sound-functions", "c2-m1-inputs", "old-c2-inputs", "output"):
        parser.add_argument("--" + key, type=Path, required=True)
    parser.add_argument("--retained-caller-controls-only", action="store_true",
                        help="Run the unchanged8 declared caller controls under StrictSource,not a public replay.")
    args = parser.parse_args()
    out = args.output.absolute()
    if out.exists() or not out.is_relative_to(ROOT / ".squad/.scratch"):
        parser.error("Choose a NEW ignored output under this worktree's .squad/.scratch.")
    out.mkdir(parents=True)
    work = out / "work"
    (work / "empty-feed").mkdir(parents=True)
    env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
               DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", MSBUILDDISABLENODEREUSE="1",
               PYTHONDONTWRITEBYTECODE="1")
    sources = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in production_paths()}
    harness = {name: (ROOT / name).read_bytes() for name in HARNESS}
    pins = {name: digest(data) for name, data in sources.items()}
    inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
    inputs.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
    inputs.update(canonical_hashes(args.old_c2_inputs))
    result = dict(schema="source-portable-compatibility-checks/v1", status="failed", commands=[], controls=[],
                  no_public_replays=True, original_hardware_equivalence=False, production_before=pins,
                  inputs_before=inputs, runs={})
    section = "controlled" if args.retained_caller_controls_only else "compatibility"
    spec = importlib.util.spec_from_file_location("retained_M1_controls", ROOT / "tests/movement-callers/run.py")
    retained = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(retained)

    def command(label, argv):
        result["commands"].append(argv)
        p = subprocess.run(argv, env=env, cwd=ROOT, text=True, capture_output=True, timeout=240)
        (out / (label + ".log")).write_text(p.stdout + p.stderr)
        return p

    def execute(label, mutation=None):
        copy = out / "copies" / label
        for base, files in ((copy / "source", sources), (copy / "harness", harness)):
            for name, data in files.items():
                p = base / name
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_bytes(data)
        if mutation:
            identity, path, old, new, expected = mutation
            p = copy / "source" / path
            data = p.read_text()
            if data.count(old) != 1:
                raise RuntimeError("Mutation anchor must be unique: " + identity)
            p.write_text(data.replace(old, new))
        project = copy / "harness/tests/movement-callers/MovementCallerChecks.csproj"
        build = out / "build" / label
        properties = ["-p:BlitzSourceRoot=" + str(copy / "source") + "/",
                      "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
                      "-p:BaseOutputPath=" + str(build / "bin") + "/"]
        argv = ["dotnet", "build", str(project), "--no-restore", "--configuration", "Release",
                "--verbosity", "quiet", *properties]
        p = command(label + "-compile", argv)
        if p.returncode and "NETSDK1004" in p.stdout + p.stderr:
            restored = command(label + "-missing-assets", ["dotnet", "restore", str(project),
                "--source", str(work / "empty-feed"), "--verbosity", "quiet", "-p:NuGetAudit=false", *properties])
            if restored.returncode:
                raise RuntimeError("Offline missing-asset restore failed: " + label)
            p = command(label + "-compile", argv)
        if p.returncode or "0 Warning(s)" not in p.stdout or "0 Error(s)" not in p.stdout:
            raise RuntimeError("Controls require successful zero-diagnostic compilation: " + label)
        p = command(label, ["dotnet", str(build / "bin/Release/net10.0/MovementCallerChecks.dll"),
            str(args.fixture_manifest.absolute()), str(out / label), section,
            str(args.sound_functions.absolute()), "--c2-m1-inputs", str(args.c2_m1_inputs.absolute()),
            "--compatibility-old-inputs", str(args.old_c2_inputs.absolute())])
        summaries = [json.loads(line) for line in p.stdout.splitlines() if line.startswith('{"schema":')]
        summary = summaries[-1] if len(summaries) == 1 else {}
        result["runs"][label] = dict(exit_code=p.returncode, summary=summary, compiled_zero_diagnostics=True)
        if mutation:
            if (p.returncode != 1 or summary.get("status") != "failed"
                    or mutation[4] not in summary.get("failed_assertion", "")):
                raise RuntimeError("Control did not reject its exact causal contract: " + label)
            result["controls"].append(dict(id=label, named_failure=summary["failed_assertion"]))
        elif args.retained_caller_controls_only:
            retained.require_summary(p.returncode, summary, "controlled", real_animation_sha)
        elif p.returncode or summary.get("status") != "passed" or summary.get("assertions") != 74:
            raise RuntimeError("Exact74 strict/portable component assertions required: " + label)
        shutil.rmtree(copy)
        print("PASS " + label, flush=True)

    try:
        real_animation_sha = "e87401c8cf3e348cc2d9c7255a1f18f43e41b1694ff7dff45c4bf025a5ecce4f"
        execute("baseline")
        mutations = [(identity, GAME + filename, old, new, expected)
                     for identity, filename, old, new, expected in retained.MUTANTS] if args.retained_caller_controls_only else MUTANTS
        for mutation in mutations:
            execute(mutation[0], mutation)
        execute("final-baseline")
        result["status"] = "passed-focused-strict-portable-not-full-M1"
    except (RuntimeError, OSError, ValueError, subprocess.TimeoutExpired) as error:
        result["error"] = str(error)
        print(str(error), flush=True)
    finally:
        result["production_after"] = {p.relative_to(ROOT).as_posix(): digest(p.read_bytes()) for p in production_paths()}
        result["inputs_after"] = {name: digest(Path(name).read_bytes()) for name in inputs}
        if result["production_after"] != pins or result["inputs_after"] != inputs:
            result["status"] = "inconclusive-generation-changed"
        (out / "result.json").write_text(json.dumps(result, indent=2) + "\n")
    return 0 if result["status"] == "passed-focused-strict-portable-not-full-M1" else 1


if __name__ == "__main__":
    raise SystemExit(main())
