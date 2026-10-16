#!/usr/bin/env python3
"""Actual-C M1 checks and isolated mutants, with explicit Unity or SDK-only API compilation."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

from cases import all_cases, attach, command, identity_map, lifecycle
from fetch_inputs import PINS, ROOT, SUITE, fetch
from oracle import Oracle

ARTIFACTS = SUITE / ".artifacts"
OWNED = ("SourceMovementState.cs", "SourceMovement.cs", "SourceEvasion.cs", "SourceEvasionLeap.cs")
DEPENDENCIES = ("SourcePlaybook.cs", "SourceDroneAI.cs", "SourceRandom.cs",
                "SourceClock.cs", "SourceMatchRules.cs", "SourceMatchFlow.cs")
MUTANTS = (
    ("turbo-nine-inclusive", "SourceMovement.cs",
     "releasedTicks > 0 && releasedTicks < 9", "releasedTicks > 0 && releasedTicks <= 9", "State.NonTurboTime"),
    ("fire-still-drains-turbo", "SourceMovement.cs",
     "stationTeamFire || infiniteTurbo", "infiniteTurbo", "Turbo"),
    ("ignore-source-field-friction", "SourceMovement.cs",
     "x *= fieldFrictionMultiplier; z *= fieldFrictionMultiplier;",
     "x *= 1f; z *= 1f;", "State.Velocity"),
    ("override-uses-minimum", "SourceMovement.cs",
     "speed = Math.Max(speed, e.State.OverrideSpeed);", "speed = Math.Min(speed, e.State.OverrideSpeed);", "State."),
    ("backpedal-384-exclusive", "SourceMovement.cs",
     "viewMove >= 384", "viewMove > 384", "State."),
    ("offscreen-carrier-speed-tuned", "SourceMovement.cs",
     "speed *= c.Carrier == -1 ? 1.5f : 2.5f;", "speed *= c.Carrier == -1 ? 1.5f : 2.4f;", "State.Velocity"),
    ("qb-backpedal-turbo-uses-forward-speed", "SourceMovement.cs",
     'turbo = e.Data.Constant("QB_BACKP_TURBO_SPEED");', 'turbo = e.Data.Constant("QB_TURBO_SPEED");', "State.Velocity"),
    ("slow-second-spin", "SourceEvasion.cs",
     "return teamSpinCount > 2;", "return teamSpinCount >= 2;", "Allowed"),
    ("qb-identity-replaced-with-mode", "SourceEvasion.cs",
     "state.PlayerId == (context.OffenseSide != 0 ? 10 : 3)", "state.Mode == SourceActorMode.Quarterback", "State."),
    ("qb-two-turbo-inclusive", "SourceEvasion.cs",
     "context.TeamIndexedTurbo > 2f", "context.TeamIndexedTurbo >= 2f", "State.TeamHurdleCount"),
    ("gravity-retuned", "SourceMovement.cs",
     "e.State.VelocityY += -0.08f;", "e.State.VelocityY += -0.1f;", "State.VelocityY"),
    ("source-tick-gate-removed", "SourceMovement.cs",
     "if (!context.Tick.Execute) return state;", "if (!context.Tick.Execute && state.PlayerId == -1) return state;", "State."),
    ("leap-removes-second-Y-prediction", "SourceEvasionLeap.cs",
     "ty += t * (input.TargetIsExecutingActor ? state.VelocityY : velocity.Y) + 0.5f * -0.08f * t * t;",
     "ty += 0f;", "State.VelocityY"),
    ("leap-minimum-return-caps-Y", "SourceEvasionLeap.cs",
     "SourceMovement.Velocity(ref state, directions, 0f, input.MinimumPlanarVelocity, state.TargetHeading);\n                    return",
     "SourceMovement.Velocity(ref state, directions, 0f, input.MinimumPlanarVelocity, state.TargetHeading);\n                    if (state.VelocityY > input.MaximumYVelocity) state.VelocityY = input.MaximumYVelocity;\n                    return",
     "State.VelocityY"),
    ("leap-ground-time-rounded-up", "SourceEvasionLeap.cs",
     "time = (int)selected;", "time = (int)Math.Ceiling(selected);", "State."),
    ("friction-equality-clamped", "SourceMovement.cs",
     "if (e.State.Friction > speed)", "if (e.State.Friction >= speed)", "State."),
)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def paths():
    return sorted((ROOT / "Assets/Scripts/Contracts").rglob("*.cs")) + [
        ROOT / "Assets/Scripts/Gameplay" / name for name in OWNED + DEPENDENCIES
    ]


def capture():
    for _ in range(3):
        content = {path.relative_to(ROOT).as_posix(): path.read_bytes() for path in paths()}
        hashes = {name: sha(data) for name, data in content.items()}
        if hashes == {path.relative_to(ROOT).as_posix(): sha(path.read_bytes()) for path in paths()}:
            return content, hashes
    raise RuntimeError("Pure production dependency files are changing; cannot obtain a coherent snapshot.")


def summary(text):
    result = None
    for line in text.splitlines():
        if line.startswith('{"schema":"source-movement-checks/v1"'):
            result = json.loads(line)
    return result


def runtime_producer():
    path = ROOT / "tools/movement/export.py"
    spec = importlib.util.spec_from_file_location("source_movement_runtime_export", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def require_runtime_data(original_data, payload):
    producer = runtime_producer()
    producer.validate(payload)
    if producer.RECIPE["source"] != {key: PINS[key] for key in ("repository", "revision", "tree", "profile")}:
        raise RuntimeError("Runtime producer and actual-C suite refer to different source revisions/profiles.")
    if any(PINS["blobs"].get(path) != pin for path, pin in producer.RECIPE["inputs"].items()):
        raise RuntimeError("Runtime producer and actual-C suite have different input blob pins.")
    for field in sorted(producer.RUNTIME_FIELDS):
        if payload["data"][field] != original_data[field]:
            raise RuntimeError("Runtime movement data." + field + " differs from actual original C (including bits/order/duplicates).")


def execute(label, source_root, build_root, environment, cases_path, commands):
    invocation = [
        "dotnet", "run", "--project", str(SUITE / "MovementChecks.csproj"), "--configuration", "Release", "--verbosity", "quiet",
        "-p:MovementSourceRoot=" + str(source_root) + "/",
        "-p:BaseIntermediateOutputPath=" + str(build_root / "obj") + "/",
        "-p:BaseOutputPath=" + str(build_root / "bin") + "/", "--", str(cases_path),
    ]
    commands.append(invocation)
    process = subprocess.run(invocation, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=180)
    text = process.stdout + process.stderr
    (ARTIFACTS / (label + ".log")).write_text(text)
    return process.returncode, summary(text), text


def unity_compile(content, reference, environment, workspace):
    suffix = tuple(part.lower() for part in reference.parts[-4:])
    if (not reference.is_file() or suffix != ("netstandard", "ref", "2.1.0", "netstandard.dll")
            or any(part.lower() == "netstandard.library.ref" for part in reference.parts)):
        raise RuntimeError("Supply the actual installed Unity Data/NetStandard/ref/2.1.0/netstandard.dll; no fake Unity types/references.")
    sdk = subprocess.run(["dotnet", "--list-sdks"], capture_output=True, text=True, check=True, env=environment).stdout.splitlines()[-1]
    version, directory = sdk.split(" ", 1)
    compiler = Path(directory.strip("[]")) / version / "Roslyn/bincore/csc.dll"
    output = ARTIFACTS / "SourceMovement.unity-netstandard21.dll"
    invocation = [
        "dotnet", str(compiler), "-nologo", "-langversion:7.3", "-nostdlib+", "-warnaserror+", "-target:library",
        "-optimize+", "-r:" + str(reference), "-out:" + str(output),
    ] + [str(workspace / name) for name in content]
    process = subprocess.run(invocation, env=environment, cwd=ROOT, capture_output=True, text=True, timeout=120)
    receipt = {
        "command": invocation, "exit_code": process.returncode, "language": "C#7.3", "warnings_as_errors": True,
        "reference": str(reference), "reference_sha256": sha(reference.read_bytes()),
        "reference_profile": "Actual installed Unity .NET Standard 2.1 reference APIs, no engine launch",
        "validation_profile": "unity-netstandard21",
        "unity_reference_validated": True,
        "unity_editor_or_browser_executed": False,
        "diagnostics": process.stdout + process.stderr,
    }
    if process.returncode:
        raise RuntimeError("Actual Unity reference compilation failed:\n" + receipt["diagnostics"])
    receipt["assembly_sha256"] = sha(output.read_bytes())
    (ARTIFACTS / "unity-netstandard21-compile.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return receipt


def sdk_compile(content, environment, workspace):
    build_root = workspace.parent / "sdk-api-build"
    build_root.mkdir(parents=True, exist_ok=True)
    reference_receipt = build_root / "references.txt"
    invocation = [
        "dotnet", "build", str(SUITE / "MovementSdkProfile.csproj"), "--configuration", "Release", "--verbosity", "quiet",
        "-p:MovementSourceRoot=" + str(workspace) + "/",
        "-p:BaseIntermediateOutputPath=" + str(build_root / "obj") + "/",
        "-p:BaseOutputPath=" + str(build_root / "bin") + "/",
        "-p:MovementReferenceReceipt=" + str(reference_receipt),
    ]
    process = subprocess.run(invocation, env=environment, cwd=ROOT, capture_output=True, text=True, timeout=180)
    receipt = {
        "command": invocation, "exit_code": process.returncode, "language": "C#7.3", "warnings_as_errors": True,
        "validation_profile": "sdk-netstandard21", "target_framework": "netstandard2.1",
        "reference_profile": "SDK Microsoft.NETStandard.Library.Ref API compile; NOT Unity validated",
        "unity_reference_validated": False, "unity_editor_or_browser_executed": False,
        "diagnostics": process.stdout + process.stderr,
        "production_sources": sorted(content),
    }
    output_receipt = ARTIFACTS / "sdk-netstandard21-compile.json"
    if process.returncode:
        output_receipt.write_text(json.dumps(receipt, indent=2) + "\n")
        raise RuntimeError("Explicit SDK .NET Standard 2.1 compilation failed; no Unity fallback:\n" + receipt["diagnostics"])
    references = [Path(line) for line in reference_receipt.read_text(encoding="utf-8-sig").splitlines() if line]
    standards = [
        path for path in references if path.name == "netstandard.dll" and "netstandard2.1" in path.parts
        and any(part.lower() == "netstandard.library.ref" for part in path.parts)
    ]
    if len(standards) != 1 or not standards[0].is_file():
        raise RuntimeError("SDK mode must resolve the actual NETStandard.Library.Ref netstandard2.1 assembly, not a Unity/fake reference.")
    output = ARTIFACTS / "SourceMovement.sdk-netstandard21.dll"
    built = build_root / "bin/Release/netstandard2.1/SourceMovement.sdk-netstandard21.dll"
    shutil.copyfile(built, output)
    receipt.update(
        reference=str(standards[0]), reference_sha256=sha(standards[0].read_bytes()),
        references=[{"path": str(path), "sha256": sha(path.read_bytes())} for path in references],
        assembly_sha256=sha(output.read_bytes()),
    )
    output_receipt.write_text(json.dumps(receipt, indent=2) + "\n")
    return receipt


def parse_arguments(arguments=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fetch-inputs", action="store_true", help="Reuse verified local caches, fetching missing bounded pinned public blobs only.")
    profile = parser.add_mutually_exclusive_group(required=True)
    profile.add_argument("--unity-reference", type=Path, help="Actual installed Unity .NET Standard 2.1 reference assembly; no Editor is launched.")
    profile.add_argument("--sdk-netstandard21", action="store_true",
                         help="Explicit asset-free CI mode: SDK NETStandard.Library.Ref APIs, NOT Unity validated. No Unity fallback.")
    return parser.parse_args(arguments)


def main():
    args = parse_arguments()
    validation_profile = "sdk-netstandard21" if args.sdk_netstandard21 else "unity-netstandard21"
    ARTIFACTS.mkdir(exist_ok=True)
    report_path = ARTIFACTS / ("result-sdk-netstandard21.json" if args.sdk_netstandard21 else "result.json")
    report_path.unlink(missing_ok=True)
    environment = dict(os.environ)
    for key, child in (("TMPDIR", "work"), ("DOTNET_CLI_HOME", "dotnet-home"), ("NUGET_PACKAGES", "nuget")):
        directory = ARTIFACTS / child
        directory.mkdir(exist_ok=True)
        environment[key] = str(directory)
    environment.update(TMP=environment["TMPDIR"], TEMP=environment["TMPDIR"],
                       DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", PYTHONDONTWRITEBYTECODE="1")
    before = {name: sha((ROOT / "Assets/Scripts/Gameplay" / name).read_bytes()) for name in OWNED}
    workspace = ARTIFACTS / ("isolated-" + uuid.uuid4().hex)
    commands = []
    evidence = {
        "schema": "source-movement-actual-c-regressions/v1", "status": "failed", "source": PINS,
        "production_helpers_before": before, "commands": commands, "mutations": [],
        "validation_profile": validation_profile,
        "unity_reference_validated": False,
        "unity_editor_or_browser_executed": False, "integrated_core_qualified": False, "full_gameplay_parity_claimed": False,
        "boundaries": {
            "animation": "Actual original change_anim/1/2 and begin_twopart_mode with explicit first-script-header _animate_block response fixture. No full original animation VM, root motion, contact or clip timing claim.",
            "random": "Explicit raw draw tapes at original C call sites; original randrng reduction, no guessed PRNG. Animation/audio/control internals are explicit boundary callbacks, not verified full draw ordering.",
            "input": "Explicit logical A/B/C/D switches and source 49-way input; original cabinet bit acquisition and host-to-source scheduling belong to callers.",
            "abi": "Host C int32/float32, -O0/no-fast-math/no contraction. Script symbols use tagged fixture strings and canonicalized original 32-bit D-script tags, not original linked numeric sequence IDs/MIPS pointer addresses.",
            "lifecycle": "Original script launch operands drive consecutive actual C player_proc physics states. These prove helper airborne/landing/momentum/friction, not integrated source animation anchors/callback timing.",
        },
    }
    try:
        if args.fetch_inputs:
            fetch()
        oracle = Oracle()
        data = oracle.execute(["data"])[0]
        data["Tables"] = oracle.tables
        producer = runtime_producer()
        runtime_output = ROOT / "tools/movement/.artifacts/source-movement-runtime.json"
        runtime_payload, runtime_receipt = producer.export(ARTIFACTS / "original", runtime_output)
        require_runtime_data(data, runtime_payload)
        evidence["runtime_data_export"] = {
            "schema": runtime_payload["schema"], "path": runtime_output.relative_to(ROOT).as_posix(),
            "output_sha256": runtime_receipt["output_sha256"],
            "manifest": runtime_output.with_suffix(".manifest.json").relative_to(ROOT).as_posix(),
            "manifest_sha256": sha(runtime_output.with_suffix(".manifest.json").read_bytes()),
            "matches_actual_original_c": True, "animation_fixture_included": False,
            "runtime_fields": runtime_receipt["runtime_fields"],
        }
        rows = all_cases(data)
        original = oracle.execute([command(row) for row in rows])
        sites = identity_map(oracle)
        attach(rows, original, sites)
        rows.extend(lifecycle(oracle, sites))
        cases_path = ARTIFACTS / "cases.json"
        cases_path.write_text(json.dumps({"Source": PINS, "Data": data, "Cases": rows}, separators=(",", ":")) + "\n")
        (ARTIFACTS / "source-movement-data.json").write_text(json.dumps({
            "schema": "original-source-movement-data/v1", "source": PINS, "data": data,
            "warning": "Ignored local converted source data; no rights/redistribution or packaging authorization implied.",
        }, indent=2) + "\n")
        evidence.update(
            original_c_cases=len(rows), cases_sha256=sha(cases_path.read_bytes()),
            original_c_draws=sum(len(row["Expected"]["Draws"]) for row in rows),
            original_c_commands=sum(len(row["Expected"]["Commands"]) for row in rows),
            original_c_selections=oracle.selections, original_c_compiler=oracle.compiler,
            case_family_counts={prefix: sum(row["Name"].split("/")[0] == prefix for row in rows)
                                for prefix in sorted({row["Name"].split("/")[0] for row in rows})},
        )
        content, captured_hashes = capture()
        evidence["production_snapshot_sha256"] = captured_hashes
        baseline_root = workspace / "baseline/source"
        for name, value in content.items():
            target = baseline_root / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(value)
        if args.sdk_netstandard21:
            evidence["sdk_api_compile"] = sdk_compile(content, environment, baseline_root)
        else:
            evidence["unity_api_compile"] = unity_compile(content, args.unity_reference.resolve(), environment, baseline_root)
            evidence["unity_reference_validated"] = True
        code, baseline, text = execute("baseline", baseline_root, workspace / "baseline/build", environment, cases_path, commands)
        evidence["baseline"] = baseline
        if code != 0 or baseline is None or baseline["status"] != "passed" or baseline["cases"] != len(rows):
            print(text)
            raise RuntimeError("Actual-production movement baseline did not pass the complete original-C corpus.")
        print("PASS baseline:", baseline["cases"], "original-C cases;", baseline["assertions"], "production assertions;",
              validation_profile + (" (NOT Unity validated)." if args.sdk_netstandard21 else " (Unity reference APIs only)."))
        for label, filename, old, new, offending_field in MUTANTS:
            mutant_root = workspace / label / "source"
            for name, value in content.items():
                target = mutant_root / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(value)
            target = mutant_root / "Assets/Scripts/Gameplay" / filename
            value = target.read_text()
            if value.count(old) != 1:
                raise RuntimeError("Actual production mutant anchor is not unique: " + label)
            target.write_text(value.replace(old, new))
            code, result, text = execute("mutation-" + label, mutant_root, workspace / label / "build",
                                         environment, cases_path, commands)
            receipt = {"name": label, "source": filename, "old": old, "new": new, "exit_code": code,
                       "result": result, "expected_offending_field_prefix": offending_field, "live_source_edited": False}
            evidence["mutations"].append(receipt)
            if (code != 1 or result is None or result["status"] != "failed" or not result["field"].startswith(offending_field)
                    or result["assertions"] <= 0):
                print(text)
                raise RuntimeError("Real-source mutant was not rejected by its numeric/state/action assertion: " + label)
            shutil.rmtree(workspace / label)
            print("REJECTED", label, "at", result["case"] + "." + result["field"])
        code, final, text = execute("final-baseline", baseline_root, workspace / "baseline/build", environment, cases_path, commands)
        evidence["final_baseline"] = final
        if code != 0 or final != baseline:
            print(text)
            raise RuntimeError("Final unmodified production baseline diverged.")
        if before != {name: sha((ROOT / "Assets/Scripts/Gameplay" / name).read_bytes()) for name in OWNED}:
            raise RuntimeError("Live production movement helpers changed during validation.")
        evidence["status"] = "passed"
        return 0
    finally:
        after = {name: sha((ROOT / "Assets/Scripts/Gameplay" / name).read_bytes()) for name in OWNED}
        evidence["production_helpers_after"] = after
        evidence["live_helper_hashes_unchanged"] = after == before
        if workspace.exists():
            shutil.rmtree(workspace)
        evidence["isolated_workspaces_removed"] = not workspace.exists()
        if before != after:
            evidence["status"] = "failed"
            evidence["failure"] = "Live movement helper hashes changed during validation."
        report_path.write_text(json.dumps(evidence, indent=2) + "\n")


if __name__ == "__main__":
    sys.exit(main())
