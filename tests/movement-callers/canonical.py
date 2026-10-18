#!/usr/bin/env python3
"""Compare actual canonical281 composition with a separately labelled isolated cache witness."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(ROOT / "tests/parity"))
from contact_inputs import fixture_profile
from prepare_inputs import fixture_hashes
from kernel_sources import canonical_hashes, production_paths, C2_ANIMATION_SHA
from compare_physics import compare

CACHE_SOURCE = "Assets/Scripts/Gameplay/SourceMovementIntegration.cs"
CACHE_SOURCE_SHA = "bceb5116ef2762306a5a1c42b3e4e17b674465fee359fc08f4df2631ecdc0943"
CALL = """                IReadOnlyList<SourceVector3> joints = geometry.CameraJoints(
                    GameSnapshot.SourcePlayerView(actor, Model.SourceTick, true), root, frame);
                if (joints == null || joints.Count != 15)
                    throw new InvalidOperationException("The original post-player camera cache requires all fifteen actual joints.");
"""
OBSERVED_CALL = """                PlayerView qaActualCacheView = GameSnapshot.SourcePlayerView(actor, Model.SourceTick, true);
                IReadOnlyList<SourceVector3> joints = geometry.CameraJoints(
                    qaActualCacheView, root, frame);
                if (joints == null || joints.Count != 15)
                    throw new InvalidOperationException("The original post-player camera cache requires all fifteen actual joints.");
                global::CanonicalCacheCapture.Record(this, qaActualCacheView, root, frame, joints);
"""
HARNESS = (
    "tests/movement-callers/MovementCallerChecks.csproj", "tests/movement-callers/Directory.Build.props",
    "tests/movement-callers/Program.cs", "tests/movement-callers/Canonical.cs",
    "tests/movement-callers/Controlled.cs", "tests/movement-callers/InputAndStation.cs",
    "tests/movement-callers/SnapHikeChecks.cs",
    "tests/movement-callers/CompatibilityChecks.cs",
    "tests/parity/FullKernelInputs.cs", "tests/parity/FullKernelSources.props",
    "tests/audio/vm/SourceSoundVmSources.props",
)
CANONICAL_CACHE_MUTANTS = (
    ("refresh-after-contact", "production", "Assets/Scripts/Gameplay/GameSimulation.Source.cs",
     "SourceContacts();", "SourceContacts();\n            CacheSourcePlayerCameraJoints();",
     "canonical isolated observer records only the actual post-player/pre-contact call"),
    ("wrong-current-root-basis", "harness", "tests/movement-callers/Canonical.cs",
     "SourceVector3 referenceRoot = sample.Root;", "SourceVector3 referenceRoot = afterRoot;",
     "canonical ReferenceRows use exact PRE-contact call arguments"),
    ("drop-contact-cache-checks", "harness", "tests/movement-callers/Canonical.cs",
     "Check(SameBits(sample.Joints[joint], reference[joint]),",
     "if (displaced) continue;\n                        Check(SameBits(sample.Joints[joint], reference[joint]),",
     "canonical all14x15 comparisons include EVERY displaced actor"),
)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def inject(original):
    if sha(original) != CACHE_SOURCE_SHA:
        raise ValueError("Canonical cache observation requires the exact frozen original production hash.")
    old, new = CALL.encode(), OBSERVED_CALL.encode()
    if original.count(old) != 1 or b"CanonicalCacheCapture" in original:
        raise ValueError("Canonical observer needs one unique real geometry call/count-guard site.")
    observed = original.replace(old, new)
    if observed.count(new) != 1 or observed.replace(new, old) != original:
        raise ValueError("Removing only the observer/local argument capture must restore the original bytes.")
    return observed


M1_COVERAGE_ASSERTION = "M1 full public R_SKIP gate requires after2299 same-instance two ordinals and two real15 poses"
SNAP_HIKE_CONTROLS = (
    ("early-live", "            SourcePlayerDriver.Snap(Model, SourceDraw);",
     "            Model.Phase = GamePhase.LiveBall;\n            Model.SourceMatchPhase = SourceMatchPhase.LiveBall;\n            SourcePlayerDriver.Snap(Model, SourceDraw);",
     "snap initiation remains PreSnap center-held receiver-1 until real HikeBall opcode"),
    ("stale-holder-frame", "            if (actor.SourceMode == SourceActorMode.Kicker)\n                frame = CurrentSourceKickerVariables(frame);",
     "            /* compiled control: stale preactor holder variables */",
     "actual later kicker consumes current holder variables in SAME HikeBall wake"),
    ("no-hiking-guard", "if (Model.Phase != GamePhase.PreSnap || (Model.SourceFlags & SourceGameFlags.HikingBall) != 0)",
     "if (Model.Phase != GamePhase.PreSnap)",
     "existing hiking flag prevents reentrant snap/interpolation reset or duplicate request"),
)


def snap_hike_main(argv):
    parser = argparse.ArgumentParser(description="Source-correct snap request/HikeBall real-factory timing and causal controls.")
    parser.add_argument("--snap-hike-only", action="store_true", required=True)
    for name in ("fixture-manifest", "sound-functions", "c2-m1-inputs", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args(argv)
    output = args.output.absolute()
    if output.exists() or not any(output.is_relative_to(p) for p in (HERE / ".artifacts", ROOT / ".squad/.scratch")):
        parser.error("Use a new ignored focused output, not a frozen receipt.")
    output.mkdir(parents=True)
    fixture_profile(args.fixture_manifest, "contact272")
    inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
    inputs.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
    production = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in production_paths()}
    pins = {k: sha(v) for k, v in production.items()}
    harness = {name: (ROOT / name).read_bytes() for name in HARNESS}
    work = output / "work"
    work.mkdir()
    (work / "empty-feed").mkdir()
    env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
               DOTNET_CLI_HOME=str(work / "dotnet-home"), NUGET_PACKAGES=str(work / "nuget"),
               DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", PYTHONDONTWRITEBYTECODE="1",
               MSBUILDDISABLENODEREUSE="1")
    result = {"schema": "source-snap-hike-focused-kernel/v1", "status": "failed",
              "controlled_fixtures_NOT_public_R_SKIP_witness": True,
              "production_before": pins, "inputs_before": inputs, "runs": {}, "commands": []}

    def execute(label, observed=False, mutation=None):
        copy = output / "copies" / label
        source, tests = copy / "source", copy / "harness"
        for base, entries in ((source, production), (tests, harness)):
            for name, raw in entries.items():
                if base == source and name == CACHE_SOURCE and observed:
                    raw = inject(raw)
                path = base / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(raw)
        if mutation:
            identity, old, new, expected = mutation
            path = source / "Assets/Scripts/Gameplay/GameSimulation.Source.cs"
            text = path.read_text()
            if text.count(old) != 1:
                raise RuntimeError("Snap-hike mutation must target one actual producer/consumer: " + identity)
            path.write_text(text.replace(old, new))
        project = tests / "tests/movement-callers/MovementCallerChecks.csproj"
        build = output / "build" / label
        common = ["-p:BlitzSourceRoot=" + str(source) + "/", "-p:BaseIntermediateOutputPath=" +
                  str(build / "obj") + "/", "-p:BaseOutputPath=" + str(build / "bin") + "/"]
        command = ["dotnet", "build", str(project), "--no-restore", "--configuration", "Release",
                   "--verbosity", "quiet", *common]
        result["commands"].append(command)
        compiled = subprocess.run(command, cwd=ROOT, env=env, text=True, capture_output=True, timeout=240)
        if compiled.returncode and "NETSDK1004" in compiled.stdout + compiled.stderr:
            restore = ["dotnet", "restore", str(project), "--source", str(work / "empty-feed"),
                       "--verbosity", "quiet", "-p:NuGetAudit=false", *common]
            result["commands"].append(restore)
            restored = subprocess.run(restore, cwd=ROOT, env=env, text=True, capture_output=True, timeout=240)
            if restored.returncode:
                raise RuntimeError("Snap-hike restore failed after missing SDK assets.")
            compiled = subprocess.run(command, cwd=ROOT, env=env, text=True, capture_output=True, timeout=240)
        text = compiled.stdout + compiled.stderr
        (output / (label + "-compile.log")).write_text(text)
        if compiled.returncode or "0 Warning(s)" not in text or "0 Error(s)" not in text:
            raise RuntimeError("Snap-hike causal control requires zero-diagnostic compile: " + label)
        destination = output / label
        command = ["dotnet", str(build / "bin/Release/net10.0/MovementCallerChecks.dll"),
                   "snap-hike", str(args.fixture_manifest.absolute()), str(args.sound_functions.absolute()),
                   str(args.c2_m1_inputs.absolute()), str(destination), "observed" if observed else "plain"]
        result["commands"].append(command)
        process = subprocess.run(command, cwd=ROOT, env=env, text=True, capture_output=True, timeout=240)
        (output / (label + ".log")).write_text(process.stdout + process.stderr)
        receipt = json.loads((destination / "focused-result.json").read_bytes())
        result["runs"][label] = {"exit_code": process.returncode, "compile_zero_diagnostic": True, "receipt": receipt}
        if mutation:
            if process.returncode != 1 or receipt["status"] != "failed" or expected not in receipt.get("error", ""):
                raise RuntimeError("Snap-hike control did not fail its exact named assertion: " + identity)
            result["runs"][label]["named_causal_failure"] = expected
        elif (process.returncode != 0 or receipt["status"] != "passed" or receipt["snapAssertions"] != 421
              or receipt["referenceDelta"] != 4):
            raise RuntimeError("Exact source-operands/VM-derived snap-hike kernel inventory required: " + label)
        shutil.rmtree(copy)
        return receipt

    try:
        plain = execute("plain")
        observed = execute("observed", True)
        for a, b in zip(plain["cases"], observed["cases"]):
            if (a["fieldGoal"], a["requested"], a["released"], a["history"]) != \
                    (b["fieldGoal"], b["requested"], b["released"], b["history"]):
                raise RuntimeError("Actual cache observer changed snap/hike behavior.")
        for spec in SNAP_HIKE_CONTROLS:
            execute("control-" + spec[0], mutation=spec)
        execute("final-plain")
        result["status"] = "passed-focused-not-full-M1"
    except (RuntimeError, ValueError, OSError, subprocess.TimeoutExpired) as error:
        result["error"] = str(error)
    finally:
        copies = output / "copies"
        if copies.exists():
            shutil.rmtree(copies)
        result["production_after"] = {p.relative_to(ROOT).as_posix(): sha(p.read_bytes()) for p in production_paths()}
        result["live_production_unchanged_during_checks"] = result["production_after"] == pins
        if not result["live_production_unchanged_during_checks"]:
            result["status"] = "inconclusive-generation-changed"
        (output / "result.json").write_text(json.dumps(result, indent=2) + "\n")
    print(result["status"])
    return 0 if result["status"] == "passed-focused-not-full-M1" else 1


def require_m1_replay(code, summary, receipt, compatibility="strict"):
    expected = "Portable" if compatibility == "portable" else "StrictSource"
    if receipt.get("gameplayCompatibility", "StrictSource") != expected:
        raise RuntimeError("Explicit gameplay compatibility must match the actual factory receipt.")
    if receipt.get("failure") is not None:
        raise RuntimeError("Actual M1 source fault: stop without rescue, extra wake or next replay.")
    if (receipt.get("completedHosts") != 12000 or receipt.get("SourceTick") != 11400
            or receipt.get("profile") != "original-teams-contact281-c2-m1"
            or receipt.get("factory") != "REAL SourceC2Composition.CreateM1Simulation"
            or receipt.get("sourceData") != "b0c02e5516f4f932625e1f8ccb3ff0fd53ae222657f899eb8c9d0b0c2e74460a"
            or receipt.get("originalPublicVectorRetainedWithoutStateConditionedRegeneration") is not True):
        raise RuntimeError("Entire12000 explicit successor actual-factory input/source identity required.")
    if code == 0 and summary.get("status") == "passed" and summary.get("public_R_SKIP_gate_qualified") is True and receipt.get("positive"):
        return "positive"
    if (code == 1 and summary.get("status") == "failed" and
            M1_COVERAGE_ASSERTION in summary.get("failed_assertion", "") and receipt.get("positive") is None):
        return "unmet-public-R_SKIP"
    raise RuntimeError("M1 replay must pass its real R_SKIP gate or report that exact unmet criterion, never relabel a different failure.")


def _write_m1_result(output, result):
    path = output / "result.json"
    path.write_text(json.dumps(result, indent=2) + "\n")
    status = result["status"]
    if status in ("passed-full-M1-public-caller-gate", "passed-portable-M1-public-caller-gate",
                  "passed-portable-single-public-gate-not-full-qualification",
                  "passed-strict-single-public-gate-not-full-qualification"):
        print("M1", status)
        print("M1 report:", path)
    else:
        print("M1 REGRESSION FAILED:", status, file=sys.stderr)
        if status == "UNMET-public-R_SKIP":
            print("Unmet required assertion:", M1_COVERAGE_ASSERTION, file=sys.stderr)
        print("M1 report:", path, file=sys.stderr)


def m1_main(argv):
    parser = argparse.ArgumentParser(description="Full actual-factory M1 successor fixed-vector gate, never old scoped acceptance.")
    for name in ("fixture-manifest", "sound-functions", "c2-m1-inputs", "source-root", "replay-commands", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--input-profile", choices=("contact272",), default="contact272")
    parser.add_argument("--source-compatibility", action="append", metavar="{strict,portable}")
    parser.add_argument("--single-public-attempt", action="store_true",
                        help="Run one unchanged vector only; never claim the complete compiled-control closure.")
    args = parser.parse_args(argv)
    modes = args.source_compatibility or ["strict"]
    if len(modes) != 1:
        parser.error("argument --source-compatibility: duplicate argument; specify only once")
    if modes[0] not in ("strict", "portable"):
        parser.error("argument --source-compatibility: invalid choice: " + repr(modes[0])
                     + " (choose from 'strict', 'portable')")
    args.source_compatibility = modes[0]
    output = args.output.absolute()
    if output.exists() or not any(output.is_relative_to(p) for p in (HERE / ".artifacts", ROOT / ".squad/.scratch")):
        parser.error("Choose a NEW ignored M1 output, not an existing/frozen receipt.")
    output.mkdir(parents=True)
    fixture_profile(args.fixture_manifest, "contact272")
    inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
    inputs.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
    inputs[str(args.replay_commands.absolute())] = sha(args.replay_commands.read_bytes())
    production = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in production_paths()}
    pins = {name: sha(data) for name, data in production.items()}
    harness = {name: (ROOT / name).read_bytes() for name in HARNESS}
    observed_source = inject(production[CACHE_SOURCE])
    work = output / "work"
    work.mkdir()
    (work / "empty-feed").mkdir()
    env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
               DOTNET_CLI_TELEMETRY_OPTOUT="1", MSBUILDDISABLENODEREUSE="1",
               DOTNET_NOLOGO="1", PYTHONDONTWRITEBYTECODE="1",
               DOTNET_CLI_HOME=str(work / "dotnet-home"), NUGET_PACKAGES=str(work / "nuget"))
    controls_spec = importlib.util.spec_from_file_location("existing_M1_immutable_controls", HERE / "run.py")
    controls = importlib.util.module_from_spec(controls_spec)
    controls_spec.loader.exec_module(controls)
    result = {"schema": "source-C2-M1-complete-actual-factory-replay/v1", "status": "failed",
              "whole_M1_or_game_or_native_accepted": False, "commands": [], "runs": {},
              "inputs_before": inputs, "production_before": pins,
              "harness_sha256": {k: sha(v) for k, v in harness.items()},
              "original8_controls_retained": True, "original3_cache_controls_retained": True,
              "no_new_policy_seed_vector_clock_pointer_or_live_source_changes": True}
    result["gameplay_compatibility"] = args.source_compatibility
    result["no_new_policy_seed_vector_clock_pointer_or_live_source_changes"] = args.source_compatibility == "strict"
    result["public_controller_policy_seed_vector_clock_unchanged"] = True
    result["portable_is_deliberate_correction_not_original_hardware"] = args.source_compatibility == "portable"
    result["complete_compiled_control_closure_executed"] = False

    def execute(label, observed=False, mutation=None, section=None):
        copy = output / "copies" / label
        source, tests = copy / "source", copy / "harness"
        for base, entries in ((source, production), (tests, harness)):
            for name, data in entries.items():
                if base == source and observed and name == CACHE_SOURCE:
                    data = observed_source
                path = base / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
        if mutation:
            identity, area, relative, old, new, expected = mutation
            target = (source if area == "production" else tests) / relative
            text = target.read_text()
            if text.count(old) != 1:
                raise RuntimeError("M1 mutation anchor must remain unique: " + identity)
            target.write_text(text.replace(old, new))
        project = tests / "tests/movement-callers/MovementCallerChecks.csproj"
        build = output / "build" / label
        common = ["-p:BlitzSourceRoot=" + str(source) + "/",
                  "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
                  "-p:BaseOutputPath=" + str(build / "bin") + "/"]
        command = ["dotnet", "build", str(project), "--no-restore", "--configuration", "Release",
                   "--verbosity", "quiet", *common]
        result["commands"].append(command)
        compiled = subprocess.run(command, cwd=ROOT, env=env, text=True, capture_output=True, timeout=240)
        if compiled.returncode and "NETSDK1004" in compiled.stdout + compiled.stderr:
            restore = ["dotnet", "restore", str(project), "--source", str(work / "empty-feed"),
                       "--verbosity", "quiet", "-p:NuGetAudit=false", *common]
            result["commands"].append(restore)
            restored = subprocess.run(restore, cwd=ROOT, env=env, text=True, capture_output=True, timeout=240)
            if restored.returncode:
                raise RuntimeError("M1 isolated restore failed after missing SDK assets.")
            compiled = subprocess.run(command, cwd=ROOT, env=env, text=True, capture_output=True, timeout=240)
        text = compiled.stdout + compiled.stderr
        (output / (label + "-compile.log")).write_text(text)
        if compiled.returncode or "0 Warning(s)" not in text or "0 Error(s)" not in text:
            raise RuntimeError("M1 causal controls require zero-diagnostic compilation: " + label)
        destination = output / label
        command = ["dotnet", str(build / "bin/Release/net10.0/MovementCallerChecks.dll"),
                   str(args.fixture_manifest.absolute()), str(destination),
                   section or ("m1-complete-observed" if observed else "m1-complete"),
                   str(args.sound_functions.absolute())]
        command += ["--c2-m1-inputs", str(args.c2_m1_inputs.absolute())] if section else [
            str(args.c2_m1_inputs.absolute()), str(args.replay_commands.absolute())]
        command += ["--source-compatibility", args.source_compatibility]
        result["commands"].append(command)
        if section is None:
            result["public_vector_executions"] = result.get("public_vector_executions", 0) + 1
        process = subprocess.run(command, cwd=ROOT, env=env, text=True, capture_output=True, timeout=900)
        (output / (label + ".log")).write_text(process.stdout + process.stderr)
        rows = [json.loads(line) for line in process.stdout.splitlines() if line.startswith('{"schema":')]
        summary = rows[-1] if len(rows) == 1 else None
        run = {"exit_code": process.returncode, "summary": summary, "compiled": True, "zero_diagnostics": True}
        result["runs"][label] = run
        if mutation:
            if (process.returncode != 1 or summary is None or summary.get("status") != "failed"
                    or expected not in summary.get("failed_assertion", "") or summary.get("assertions", 0) <= 0):
                raise RuntimeError("M1 compiled control must reject its own named causal assertion: " + identity)
            run["named_causal_failure"] = summary["failed_assertion"]
        elif section:
            if section == "controlled":
                controls.require_summary(process.returncode, summary, "controlled", C2_ANIMATION_SHA)
            elif (process.returncode != 0 or summary.get("status") != "passed"
                  or summary.get("actual_Core_post_VM_physics_cases") != 664):
                raise RuntimeError("Retained M1 caller/physics inventory failed.")
        else:
            receipt = json.loads((destination / "m1-replay-result.json").read_bytes())
            run["receipt_metadata"] = {key: receipt.get(key) for key in (
                "completedHosts", "SourceTick", "gameplayCompatibility", "compatibilityRepairCount",
                "compatibilityRepairs", "rawHandicap", "effectiveHandicap", "rSkipFrames", "positive")}
            if receipt.get("failure"):
                failure = receipt["failure"]
                run["fault_metadata"] = {key: failure.get(key) for key in ("host", "before", "SourceTick", "error")}
                if args.source_compatibility == "portable":
                    result["status"] = "failed-portable-source-fault"
            run["classification"] = require_m1_replay(process.returncode, summary, receipt, args.source_compatibility)
            run["replay_receipt_sha256"] = sha((destination / "m1-replay-result.json").read_bytes())
            run["receipt"] = receipt
        shutil.rmtree(copy)
        return run

    try:
        plain = execute("uninstrumented")
        if args.single_public_attempt:
            result["status"] = ("passed-portable-single-public-gate-not-full-qualification"
                if args.source_compatibility == "portable" else "passed-strict-single-public-gate-not-full-qualification"
                ) if plain["classification"] == "positive" else "UNMET-public-R_SKIP"
            result["M1_public_caller_gate_qualified"] = plain["classification"] == "positive"
            result["public_vector_executions"] = 1
        else:
            observed = execute("isolated-observer", True)
            for name in ("m1-public-replay.jsonl", "m1-all-raw-draws.json", "m1-R_SKIP-real-poses.json"):
                a, b = output / "uninstrumented" / name, output / "isolated-observer" / name
                if a.read_bytes() != b.read_bytes():
                    raise RuntimeError("Passive observer changed actual recorded state/input/pose/RNG: " + name)
            result["uninstrumented_and_actual_observer_state_RNG_and_poses_byte_identical"] = True
            for spec in CANONICAL_CACHE_MUTANTS:
                execute("cache-control-" + spec[0], True, spec)
            execute("controlled-baseline", section="controlled")
            for identity, filename, old, new, expected in controls.MUTANTS:
                execute("caller-control-" + identity, mutation=(identity, "production",
                    "Assets/Scripts/Gameplay/" + filename, old, new, expected), section="controlled")
            execute("controlled-final", section="controlled")
            execute("existing-caller-physics", section="campaigns")
            native_output = HERE / ".artifacts" / ("m1-post-VM-" + sha(str(output).encode())[:16])
            result["original_C_post_VM"] = compare(args.source_root,
                output / "existing-caller-physics/actual-caller-physics.tsv", native_output)
            final = execute("final-uninstrumented")
            if (output / "final-uninstrumented/m1-public-replay.jsonl").read_bytes() != \
                    (output / "uninstrumented/m1-public-replay.jsonl").read_bytes():
                raise RuntimeError("Final actual-factory source baseline changed after causal controls.")
            result["all3_cache_and8_caller_controls_compiled_and_causal"] = True
            result["complete_compiled_control_closure_executed"] = True
            result["status"] = ("passed-portable-M1-public-caller-gate" if args.source_compatibility == "portable"
                else "passed-full-M1-public-caller-gate") if plain["classification"] == "positive" else "UNMET-public-R_SKIP"
            result["whole_M1_or_game_or_native_accepted"] = False
            result["M1_public_caller_gate_qualified"] = plain["classification"] == "positive"
    except (ValueError, OSError, RuntimeError, subprocess.TimeoutExpired) as error:
        result["error"] = str(error)
        print(str(error), file=sys.stderr)
    finally:
        copies = output / "copies"
        if copies.exists():
            shutil.rmtree(copies)
        result["production_after"] = {p.relative_to(ROOT).as_posix(): sha(p.read_bytes()) for p in production_paths()}
        result["live_production_unchanged"] = result["production_after"] == pins
        if not result["live_production_unchanged"]:
            result["status"] = "inconclusive-generation-changed"
        _write_m1_result(output, result)
    return 0 if result["status"] in ("passed-full-M1-public-caller-gate", "passed-portable-M1-public-caller-gate",
        "passed-portable-single-public-gate-not-full-qualification", "passed-strict-single-public-gate-not-full-qualification") else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fixture-manifest", type=Path, required=True)
    parser.add_argument("--sound-functions", type=Path, required=True)
    parser.add_argument("--c2-inputs", type=Path, required=True)
    parser.add_argument("--source-root", type=Path, required=True, help="Pinned original player.c for all664 post-VM cases.")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.absolute()
    if (not output.is_relative_to(ROOT) or output.exists()
            or not any(output.is_relative_to(p) for p in (HERE / ".artifacts", ROOT / ".squad/.scratch"))):
        parser.error("Use a new explicitly ignored local output, never an existing owner receipt.")
    output.mkdir(parents=True)
    fixture_profile(args.fixture_manifest, "contact272")
    inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
    inputs.update(canonical_hashes(args.c2_inputs))
    production = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in production_paths()}
    pins = {name: sha(data) for name, data in production.items()}
    harness = {name: (ROOT / name).read_bytes() for name in HARNESS}
    observed_source = inject(production[CACHE_SOURCE])
    workspace = output / "isolated"
    work = output / "work"
    work.mkdir()
    environment = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                       DOTNET_CLI_TELEMETRY_OPTOUT="1", MSBUILDDISABLENODEREUSE="1",
                       DOTNET_NOLOGO="1", PYTHONDONTWRITEBYTECODE="1",
                       DOTNET_CLI_HOME=str(work / "dotnet-home"), NUGET_PACKAGES=str(work / "nuget"))
    result = {"schema": "source-canonical281-cache-observation-run/v1", "status": "failed",
              "production_sha256_before": pins,
              "harness_sha256": {name: sha(data) for name, data in harness.items()},
              "inputs_sha256_before": inputs, "commands": [], "runs": {},
              "instrumentation": {
                  "original_source": CACHE_SOURCE, "original_sha256": CACHE_SOURCE_SHA,
                  "observed_sha256": sha(observed_source), "unique_call_site": True,
                  "removing_only_insertion_restores_original_bytes": True,
                  "additional_geometry_calls": 0, "replacement_geometry": False,
                  "model_RNG_order_return_or_live_assembly_mutation": False},
              "full_M1_or_game_or_native_acceptance": False}
    controls_spec = importlib.util.spec_from_file_location("canonical_M1_existing_controls", HERE / "run.py")
    controls = importlib.util.module_from_spec(controls_spec)
    controls_spec.loader.exec_module(controls)

    def execute(label, instrumented, mutation=None, section=None):
        copy = workspace / label
        source, tests = copy / "source", copy / "harness"
        for base, entries in ((source, production), (tests, harness)):
            for name, content in entries.items():
                path = base / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(content)
        if instrumented:
            (source / CACHE_SOURCE).write_bytes(observed_source)
        if mutation is not None:
            identity, kind, relative, old, new, expected = mutation
            target = (source if kind == "production" else tests) / relative
            original = target.read_bytes()
            if original.count(old.encode()) != 1:
                raise RuntimeError("Canonical cache control requires one actual unique anchor: " + identity)
            target.write_bytes(original.replace(old.encode(), new.encode()))
        project = tests / "tests/movement-callers/MovementCallerChecks.csproj"
        build = copy / "build"
        command = ["dotnet", "build", str(project), "--no-restore", "--configuration", "Release",
                   "--verbosity", "quiet", "-p:BlitzSourceRoot=" + str(source) + "/",
                   "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
                   "-p:BaseOutputPath=" + str(build / "bin") + "/"]
        result["commands"].append(command)
        compiled = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=240)
        (output / (label + "-compile.log")).write_text(compiled.stdout + compiled.stderr)
        if compiled.returncode and "NETSDK1004" in compiled.stdout + compiled.stderr:
            feed = work / "empty-feed"
            feed.mkdir(exist_ok=True)
            restore = ["dotnet", "restore", str(project), "--source", str(feed), "--verbosity", "quiet",
                       "-p:NuGetAudit=false", "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/"]
            result["commands"].append(restore)
            restored = subprocess.run(restore, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=240)
            (output / (label + "-restore-after-missing-assets.log")).write_text(restored.stdout + restored.stderr)
            if restored.returncode:
                raise RuntimeError("Existing SDK local restore failed after missing assets: " + label)
            compiled = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=240)
            (output / (label + "-compile-after-restore.log")).write_text(compiled.stdout + compiled.stderr)
        if compiled.returncode:
            raise RuntimeError("Canonical harness must compile; a compilation failure is NOT a production failure: " + label)
        mode = section or ("canonical281-observed" if instrumented else "canonical281")
        command = ["dotnet", str(build / "bin/Release/net10.0/MovementCallerChecks.dll"),
                   str(args.fixture_manifest.absolute()), str(output / label), mode,
                   str(args.sound_functions.absolute())]
        command += [str(args.c2_inputs.absolute())] if section is None else ["--c2-inputs", str(args.c2_inputs.absolute())]
        result["commands"].append(command)
        process = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=600)
        (output / (label + ".log")).write_text(process.stdout + process.stderr)
        rows = [json.loads(line) for line in process.stdout.splitlines() if line.startswith('{"schema":')]
        actual = rows[-1] if len(rows) == 1 else None
        result["runs"][label] = {"exit_code": process.returncode, "summary": actual,
                                 "instrumented": instrumented}
        if mutation is not None:
            if (process.returncode != 1 or actual is None or actual.get("status") != "failed"
                    or actual.get("assertions", 0) <= 0 or expected not in actual.get("failed_assertion", "")):
                raise RuntimeError("Compiled canonical cache control did not reject its named causal field: " + identity)
            result["runs"][label].update(compiled=True, named_causal_rejection=True,
                                         expected_assertion=expected, mutation_source=relative,
                                         mutated_sha256=sha(target.read_bytes()))
            return actual
        failure = output / label / "first-canonical-software-failure.json"
        if section is not None:
            failure = output / label / "first-current-source-failure.json"
        if failure.exists():
            result["first_software_failure"] = {"path": str(failure), "sha256": sha(failure.read_bytes())}
            raise RuntimeError("Canonical real-composition stream stopped at a genuine software boundary: " + str(failure))
        if section is not None:
            if section == "controlled":
                from kernel_sources import C2_ANIMATION_SHA
                controls.require_summary(process.returncode, actual, "controlled", C2_ANIMATION_SHA)
            elif (process.returncode != 0 or actual is None or actual.get("status") != "passed"
                  or actual.get("actual_Core_post_VM_physics_cases") != 664
                  or actual.get("actual_Hurdle_VM_scripts") != 16
                  or actual.get("actual_MaybeDive_windows_per_hurdle") != 7
                  or actual.get("complete_publication_qualification") is not False):
                raise RuntimeError("Every existing canonical M1 caller/gesture/physics case group must pass: " + section)
            return actual
        if (process.returncode or actual is None or actual.get("status") != "canonical-entry-window-only"
                or actual.get("full_M1_qualified") is not False):
            raise RuntimeError("Canonical entry compilation/checks did not produce qualified scoped evidence: " + label)
        receipt = json.loads((output / label / "canonical-entry.json").read_bytes())
        if (receipt["completedHosts"] != 12000 or receipt["observed"] != instrumented
                or receipt["factory"] != "REAL SourceC2Composition" or receipt["fullM1OrGameOrNativeAccepted"] is not False):
            raise RuntimeError("Canonical entry/factory/window inventory differs: " + label)
        result["runs"][label]["receipt_sha256"] = sha((output / label / "canonical-entry.json").read_bytes())
        return receipt

    exit_code = 1
    try:
        untouched = execute("uninstrumented", False)
        observed = execute("isolated-observer", True)
        first = (output / "uninstrumented/canonical-observable-stream.jsonl").read_bytes()
        second = (output / "isolated-observer/canonical-observable-stream.jsonl").read_bytes()
        if first != second or untouched["allSharedRawDraws"] != observed["allSharedRawDraws"]:
            raise RuntimeError("Isolated witness changed actual state/events/cache/RNG; it cannot qualify untouched production.")
        result["byte_identical_full_observable_stream_and_all_raw_draws"] = True
        result["compared_host_records"] = len(first.splitlines())
        result["stream_sha256"] = sha(first)
        for spec in CANONICAL_CACHE_MUTANTS:
            execute("control-" + spec[0], True, spec)
        final = execute("final-uninstrumented", False)
        final_stream = (output / "final-uninstrumented/canonical-observable-stream.jsonl").read_bytes()
        if final_stream != first or final["allSharedRawDraws"] != untouched["allSharedRawDraws"]:
            raise RuntimeError("Final untouched real-composition baseline differs after isolated cache controls.")
        result["canonical_compiled_cache_controls"] = len(CANONICAL_CACHE_MUTANTS)
        result["historical_source474_three_controls_preserved_separately"] = True
        result["final_uninstrumented_baseline_identical"] = True
        execute("controlled-baseline", False, section="controlled")
        campaign = execute("existing-M1-campaigns", False, section="campaigns")
        result["original_C_post_VM"] = compare(args.source_root,
            output / "existing-M1-campaigns/actual-caller-physics.tsv", output / "original-C-post-VM")
        for identity, filename, old, new, expected in controls.MUTANTS:
            spec = (identity, "production", "Assets/Scripts/Gameplay/" + filename, old, new, expected)
            execute("M1-control-" + identity, False, spec, section="controlled")
        execute("controlled-final", False, section="controlled")
        final_campaign = execute("final-existing-M1-campaigns", False, section="campaigns")
        if final_campaign.get("assertions") != campaign.get("assertions"):
            raise RuntimeError("Canonical caller campaign assertions changed after isolated original8 controls.")
        result["original8_M1_controls_compiled_and_causal"] = 8
        result["all_existing_M1_case_groups_executed"] = True
        result["old36948_full_M1_gate_not_relabeled_as_canonical"] = True
        result["canonical_R_SKIP_coverage"] = untouched["rSkipFrames"]
        if untouched["rSkipFrames"] == 0:
            result["stronger_full_M1_coverage_blocked"] = "Unchanged unfiltered canonical12000 window does not execute the prior R_SKIP advancement assertions; no vector/filter/assertion change authorized."
        result["status"] = "passed-scoped-entry-and-cache-only"
        exit_code = 0
    except (ValueError, OSError, RuntimeError, subprocess.TimeoutExpired) as error:
        result["error"] = str(error)
        print(str(error), file=sys.stderr)
    finally:
        if workspace.exists():
            shutil.rmtree(workspace)
        result["own_copies_removed"] = not workspace.exists()
        result["production_sha256_after"] = {p.relative_to(ROOT).as_posix(): sha(p.read_bytes()) for p in production_paths()}
        after_inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
        after_inputs.update(canonical_hashes(args.c2_inputs))
        result["inputs_sha256_after"] = after_inputs
        result["production_and_inputs_unchanged"] = pins == result["production_sha256_after"] and inputs == after_inputs
        if not result["production_and_inputs_unchanged"]:
            result["status"] = "inconclusive-generation-changed"
            exit_code = 1
        (output / "result.json").write_text(json.dumps(result, indent=2) + "\n")
    print(result["status"])
    return exit_code


if __name__ == "__main__":
    raise SystemExit(snap_hike_main(sys.argv[1:]) if "--snap-hike-only" in sys.argv else
                     m1_main(sys.argv[1:]) if "--c2-m1-inputs" in sys.argv else main())
