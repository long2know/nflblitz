#!/usr/bin/env python3
"""Compiled real Runtime sound transport; optional real Core fixture and complete Unity API."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[3]
SUITE = ROOT / "tests/audio/runtime"
ARTIFACTS = SUITE / ".artifacts"
sys.path.insert(0, str(ROOT / "tests/ci"))
from isolated_mutations import run_mutations
TRANSPORT = "Assets/Scripts/Runtime/SourceSoundVmTransport.cs"
BRIDGE = "Assets/Scripts/Runtime/FixedStepBridge.cs"
MUTANTS = (
    ("empty-clears-pending", TRANSPORT, "LastBatch = batch;", "LastBatch = batch; pending.Clear();",
     "empty/skipped drain cannot discard pending records"),
    ("cue-context-alias", TRANSPORT, "!ReferenceEquals(pending.Peek().OriginalCue, cue)",
     "pending.Peek().OriginalCue.Context.SourceTick != cue.Context.SourceTick",
     "context-equivalent reconstructed cue is not OriginalCue"),
    ("missing-consumer-silence", TRANSPORT, "FailedDispatch = dispatch;\n                throw new InvalidOperationException(failure);",
     "FailedDispatch = dispatch;\n                return;",
     "nonempty calls without real output fail explicitly"),
    ("zero-needs-output", TRANSPORT, "if (dispatch.Result.Calls.Count == 0) return;",
     "if (dispatch.Result.Calls.Count < 0) return;", "legitimate resolved zero calls need no output consumer"),
    ("no-paired-completion", TRANSPORT, "if (awaitingOutput.Count != 0)", "if (awaitingOutput.Count < 0)",
     "paired nonempty sound cannot be silently undelivered"),
    ("replayed-callback", TRANSPORT, "if (paired.ContainsKey(cue))", "if (paired.ContainsKey(cue) && pending.Count < 0)",
     "duplicate original raw cue cannot replay"),
    ("forged-dispatch", TRANSPORT,
     "paired.TryGetValue(dispatch.OriginalCue, out actual) && ReferenceEquals(actual, dispatch) &&",
     "paired.TryGetValue(dispatch.OriginalCue, out actual) && actual != null &&",
     "forged dispatch with same cue cannot replace Core result"),
    ("unsupported-is-resolved", TRANSPORT, "RequireHealthy();\n            SourceSoundVmDispatch actual;",
     "RequireHealthy();\n            if (dispatch.Status == SourceSoundVmStatus.Unsupported) return;\n            SourceSoundVmDispatch actual;",
     "unsupported is not a resolved output"),
    ("partial-loses-calls", TRANSPORT, "FailedDispatch = dispatch;\n                }",
     "FailedDispatch = null;\n                }", "partial failure retains calls and context across empty drains"),
    ("partial-not-sticky", TRANSPORT, 'failure = "Original sound selection failed after partial execution: " + dispatch.Detail + ". Restart is required.";',
     'failure = null;', "partial selection faults until explicit restart"),
    ("subcall-cue-alias", TRANSPORT,
     "call.HasOriginalAnimationCue && ReferenceEquals(call.OriginalCue, dispatch.OriginalCue)",
     "call.HasOriginalAnimationCue && call.OriginalCue != null", "raw subcall cannot replace exact OriginalCue identity"),
    ("subcall-order", TRANSPORT, "call.SubcallOrder != i",
     "call.SubcallOrder < 0", "raw subcalls must retain zero-based order"),
    ("unpaired-drop", TRANSPORT, "if (pending.Count != 0)", "if (pending.Count < 0)",
     "unpaired drained record is visible failure"),
    ("skip-empty-step-drain", BRIDGE, "if (drained != null) drained();",
     "if (drained != null && State.Tick % 2 != 0) drained();", "sound batch is drained after every host step including source skips"),
    ("skip-failed-step-drain", BRIDGE,
     "finally\n                {\n                    Action drained = AfterSimulationStep;\n                    if (drained != null) drained();\n                }",
     "finally { }\n                Action drained = AfterSimulationStep;\n                if (drained != null) drained();",
     "finally drain retains partial record on failed Step"),
    ("restart-retains-transport", BRIDGE, "if (restarted != null) restarted();",
     "if (restarted != null && State.Tick > 0) restarted();", "actual bridge restart clears transport without selection replay"),
)


def require(value, message):
    if not value:
        raise RuntimeError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fixture-manifest", type=Path, help="Explicit real157 Core input manifest.")
    parser.add_argument("--sound-functions", type=Path, help="Explicit original1983-byte SAFN4a557 payload.")
    parser.add_argument("--unity-editor-data", type=Path, help="Actual6000.3.24 references; no Editor is started.")
    parser.add_argument("--no-mutations", action="store_true", help="Explicit author-only baseline; not complete mutation evidence.")
    args = parser.parse_args(argv)
    if bool(args.fixture_manifest) != bool(args.sound_functions):
        parser.error("Real Core tests require BOTH --fixture-manifest and --sound-functions; no source-data fallback.")
    for supplied in (args.fixture_manifest, args.sound_functions):
        if supplied is not None and not supplied.is_file():
            parser.error("Missing explicit original input: " + str(supplied))
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    work = ARTIFACTS / ("run-" + uuid.uuid4().hex)
    cache = ARTIFACTS / "process-local"
    cache.mkdir(exist_ok=True)
    environment = dict(os.environ, TMPDIR=str(cache), TMP=str(cache), TEMP=str(cache),
                       PYTHONDONTWRITEBYTECODE="1", DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    paths = []
    for directory in ("Contracts", "Gameplay", "AssetImport"):
        paths.extend(sorted((ROOT / "Assets/Scripts" / directory).rglob("*.cs")))
    paths += [ROOT / name for name in (
        "Assets/Scripts/Presentation/SourceAudioFunctions.cs",
        "Assets/Scripts/Presentation/SourceC2SoundFunctions.cs",
        "Assets/Scripts/Presentation/SourceCinematicModel.cs",
        "Assets/Scripts/Runtime/SourceC2Composition.cs",
        TRANSPORT, BRIDGE, "tests/parity/FullKernelInputs.cs",
        "tests/parity/FullKernelSources.props")]
    source = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in paths}
    guarded = {name: digest(data) for name, data in source.items()}
    result = {"schema": "source-sound-runtime-gate/v1", "status": "failed", "source_sha256": guarded,
              "actual_inputs_supplied": args.fixture_manifest is not None, "mutants": [],
              "Unity_native_DSP_browser_or_audio_playback_claimed": False}
    try:
        def execute(label, mutant=None):
            selected = work / label
            for name, data in source.items():
                target = selected / name
                target.parent.mkdir(parents=True, exist_ok=True)
                if mutant and name == mutant[1]:
                    text = data.decode()
                    require(text.count(mutant[2]) == 1, "Mutation target is not unique: " + label)
                    data = text.replace(mutant[2], mutant[3], 1).encode()
                target.write_bytes(data)
            tests = selected / "tests/audio/runtime"
            tests.mkdir(parents=True)
            for name in ("Program.cs", "ActualCore.cs", "SourceSoundRuntime.Checks.csproj"):
                shutil.copyfile(SUITE / name, tests / name)
            scratch = selected / "runtime"
            scratch.mkdir()
            execute_env = dict(environment, TMPDIR=str(scratch), TMP=str(scratch), TEMP=str(scratch))
            build = ["dotnet", "build", str(tests / "SourceSoundRuntime.Checks.csproj"),
                     "--configuration", "Release", "--verbosity", "quiet",
                     "-p:BlitzSourceRoot=" + str(selected) + "/"]
            compiled = subprocess.run(build, cwd=ROOT, env=execute_env, text=True, capture_output=True, timeout=180)
            (ARTIFACTS / (label + "-build.log")).write_text(compiled.stdout + compiled.stderr)
            require(compiled.returncode == 0, "Compilation failure is NOT a rejected Runtime mutant: " + label)
            command = ["dotnet", str(tests / "bin/Release/net10.0/SourceSoundRuntime.Checks.dll")]
            if args.fixture_manifest and not mutant:
                command += [str(args.fixture_manifest.resolve()), str(args.sound_functions.resolve())]
            checked = subprocess.run(command, cwd=ROOT, env=execute_env, text=True, capture_output=True, timeout=180)
            (ARTIFACTS / (label + ".log")).write_text(checked.stdout + checked.stderr)
            reports = [json.loads(line) for line in checked.stdout.splitlines() if line.startswith('{"schema":')]
            require(len(reports) == 1 and reports[0]["schema"] == "source-sound-runtime-checks/v1",
                    "Missing actual transport assertion artifact: " + label)
            report = reports[0]
            if mutant:
                require(checked.returncode == 1 and report["status"] == "failed" and mutant[4] in report["failed_assertion"],
                        "Compiled Runtime mutant failed a different artifact: " + label + " " + str(report))
            else:
                require(checked.returncode == 0 and report["status"] == "passed" and report["assertions"] >= 50,
                        "Actual Runtime transport baseline failed: " + label + " " + str(report))
                if args.fixture_manifest:
                    require(report["actual_assertions"] >= 20, "Real Core batches/partial state were not exercised.")
            return report

        result["baseline"] = execute("baseline")
        if not args.no_mutations:
            def mutation(mutant):
                report = execute(mutant[0], mutant)
                return {"id": mutant[0], "offending_assertion": report["failed_assertion"], "status": "rejected"}
            result["mutants"] = run_mutations(MUTANTS, mutation)
        final = execute("final")
        require(final == result["baseline"], "Final transport baseline changed.")
        for name, sha in guarded.items():
            require(digest((ROOT / name).read_bytes()) == sha, "Shared producer/transport source changed during test: " + name)
        if args.unity_editor_data:
            command = [sys.executable, "-B", str(ROOT / "tests/interface/stage_followup.py"),
                       "--verify-clean-checkout", "--unity-editor-data", str(args.unity_editor_data.resolve(strict=True))]
            checked = subprocess.run(command, cwd=ROOT, env=environment, text=True, capture_output=True, timeout=300)
            (ARTIFACTS / "actual-api.log").write_text(checked.stdout + checked.stderr)
            require(checked.returncode == 0, "Complete actual Unity API/inventory gate failed; see actual-api.log.")
            result["actual_Unity_API"] = json.loads((ROOT / "tests/interface/.artifacts/stage-followup/result-api.json").read_bytes())
        result.update(status="passed", shared_sources_unchanged=True, baseline_final_equal=True)
        print(json.dumps({"status": "passed", "assertions": result["baseline"]["assertions"],
                          "actual_assertions": result["baseline"]["actual_assertions"],
                          "compiled_mutants_rejected": len(result["mutants"]),
                          "actual_Unity_API_compiled": args.unity_editor_data is not None, "audible_claimed": False}))
        return 0
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        result["error"] = str(error)
        print("Source sound Runtime gate failed: " + str(error), file=sys.stderr)
        return 1
    finally:
        encoded = json.dumps(result, indent=2) + "\n"
        (ARTIFACTS / "result.json").write_text(encoded)
        (ARTIFACTS / ("result-actual.json" if args.fixture_manifest else "result-default.json")).write_text(encoded)
        if work.exists():
            shutil.rmtree(work)


if __name__ == "__main__":
    raise SystemExit(main())
