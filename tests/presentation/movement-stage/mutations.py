#!/usr/bin/env python3
"""Pass the actual baseline, then compile real pure-source mutants and require their exact failures."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[3]
SUITE = Path(__file__).resolve().parent
OUT = SUITE / ".artifacts/mutations"
MODEL = "Assets/Scripts/Presentation/SourceCinematicModel.cs"
PACKAGE = "Assets/Scripts/AssetImport/SourceTeamInterfacePackage.cs"
CHECKS = "Assets/Scripts/Editor/SourceTeamInterfaceChecks.cs"
PURE = (
    ("stale-tick-admitted", MODEL, " || tick != executingSourceTick", ""),
    ("suspended-frame-admitted", MODEL, " || cameraProcessSuspended", ""),
    ("restart-cache-retained", MODEL, "{ frame = null; tick = -1; }", "{ }"),
    ("duplicate-source-wake-admitted", MODEL, " || tick >= 0 && executingSourceTick <= tick", ""),
    ("viewport-width-admitted", MODEL, "horizontalResolution != 512f || ", ""),
    ("wrong-DIP-aspect", MODEL, "return horizontalResolution / verticalResolution;", "return 1f;"),
    ("source-eye-converted-to-yards", MODEL, "new SourceCinematicTransform(state.Eye, new[] {",
     "new SourceCinematicTransform(new SourceVector3(state.Eye.X / 7.8f, state.Eye.Y / 7.8f, state.Eye.Z / 7.8f), new[] {"),
    ("Unity-affine-translation", MODEL, "right.X, right.Y, right.Z, 0f,", "right.X, right.Y, right.Z, state.Eye.X,"),
    ("cross-product-handedness", MODEL, "Normalize(Cross(above, forward))", "Normalize(Cross(forward, above))"),
    ("157-resource-aliased117", PACKAGE, '"SourceData/local-plyr266-movement157-f0ae9395"',
     '"BlitzSourcePlayers/original-teams/animation-library"'),
    ("157-clip-count-is156", PACKAGE, "MovementAnimationSha256, 157, 13490, true", "MovementAnimationSha256, 156, 13420, true"),
    ("saved-team-mask-not-compared", CHECKS, "settings.InfiniteTurboTeamMask == expected.InfiniteTurboTeamMask &&", ""),
)
GUARDS = (
    ("getter-stamp-removed", "Assets/Scripts/Presentation/PlayCamera.cs", "lastSourceStageTick != executingSourceTick", "false"),
    ("render-changes-source-aspect", "Assets/Scripts/Presentation/PlayCamera.cs",
     "if (!movement157) sourceAspect = aspect;", "sourceAspect = aspect;"),
    ("frame-publish-not-produced", "Assets/Scripts/Presentation/SourceCinematicPlayback.cs",
     "executingSourceCamera.Publish(sourceTick, CurrentSourceCamera);", ""),
    ("runtime-current-frame-not-injected", "Assets/Scripts/Runtime/BlitzRuntime.cs",
     "simulation.SetSourceMovementCameraFrame(", "DiscardedSourceMovementCameraFrame("),
    ("raw-GAME-H-replaced-phase", "Assets/Scripts/Runtime/BlitzRuntime.cs",
     "state => simulation.SourceCameraRawGameMode", "state => (int)state.Phase"),
    ("resolved-generic-replay", "Assets/Scripts/Runtime/BlitzRuntime.cs",
     "playCamera.ConsumeResolvedSourceSound(dispatch, soundTransport.DeliverResolved);",
     "playCamera.ConsumeSourceCue(dispatch.OriginalCue);"),
    ("resolved-falls-through", "Assets/Scripts/Runtime/BlitzRuntime.cs",
     "playCamera.ConsumeResolvedSourceSound(dispatch, soundTransport.DeliverResolved);\n"
     "                    soundTransport.FlushNativePrefix();\n                    return;",
     "playCamera.ConsumeResolvedSourceSound(dispatch, soundTransport.DeliverResolved);\n"
     "                    soundTransport.FlushNativePrefix();"),
    ("native-prefix-flush-removed", "Assets/Scripts/Runtime/BlitzRuntime.cs",
     "soundTransport.FlushNativePrefix();\n                    return;", "return;"),
    ("resolved-output-disconnected", "Assets/Scripts/Runtime/BlitzRuntime.cs",
     "playCamera.ConsumeResolvedSourceSound(dispatch, soundTransport.DeliverResolved);",
     "playCamera.ConsumeResolvedSourceSound(dispatch);"),
    ("resolved-route-noop", "Assets/Scripts/Runtime/BlitzRuntime.cs",
     "playCamera.ConsumeResolvedSourceSound(dispatch, soundTransport.DeliverResolved);", ""),
    ("double-resolved-cue-route", "Assets/Scripts/Runtime/BlitzRuntime.cs",
     "SourcePresentationCueRaised += ConsumeSourcePresentationCue;",
     "SourcePresentationCueRaised += ConsumeSourcePresentationCue;\nSourcePresentationCueRaised += ConsumeSourcePresentationCue;"),
    ("cue-lifecycle-not-removed", "Assets/Scripts/Runtime/BlitzRuntime.cs",
     "SourcePresentationCueRaised -= ConsumeSourcePresentationCue;", ""),
    ("double-host-lifecycle", "Assets/Scripts/Runtime/BlitzRuntime.cs",
     "AfterHostStep += playCamera.ObserveSourceHostStep;",
     "AfterHostStep += playCamera.ObserveSourceHostStep;\nAfterHostStep += playCamera.ObserveSourceHostStep;"),
    ("step-finally-drain-noop", "Assets/Scripts/Runtime/FixedStepBridge.cs",
     "if (drained != null) drained();", ""),
    ("initial-cue-drain-missing", "Assets/Scripts/Runtime/BlitzRuntime.cs",
     "            OnAfterSimulationStep();", ""),
    ("camera-resolved-wrapper-noop", "Assets/Scripts/Presentation/PlayCamera.cs",
     "SourcePlayback.ConsumeResolvedSourceSound(dispatch, actualRawOutputConsumer);", ""),
    ("source-only-camera-guard-removed", "Assets/Scripts/Runtime/BlitzRuntime.cs",
     "if (IsSourceProfile)\n                {\n                    SourceInterfaceMovementBootstrap bootstrap = sourceServices.MovementBootstrap;",
     "if (true)\n                {\n                    SourceInterfaceMovementBootstrap bootstrap = sourceServices.MovementBootstrap;"),
    ("restart-sound-reset-removed", "Assets/Scripts/Runtime/BlitzRuntime.cs",
     "if (soundTransport != null) soundTransport.Reset();", ""),
    ("restart-fire-epoch-reset-removed", "Assets/Scripts/Runtime/BlitzRuntime.cs",
     "if (sourceFireRenderer != null) sourceFireRenderer.ResetSourceEpoch();", ""),
)
PURE_FAILURES = {
    "stale-tick-admitted": "stale source frame must reject",
    "suspended-frame-admitted": "menu suspended camera frame must reject",
    "restart-cache-retained": "restart invalidates cached generation must reject",
    "duplicate-source-wake-admitted": "duplicate source wake must reject",
    "viewport-width-admitted": "not original DIP source dimensions must reject",
    "wrong-DIP-aspect": "original high DIP raster",
    "source-eye-converted-to-yards": "position stays original source units",
    "Unity-affine-translation": "source transform zero affine column, no Unity translation",
    "cross-product-handedness": "source above-cross-forward right X",
    "157-resource-aliased117": "NEW157 exact resource key",
    "157-clip-count-is156": "Blitz.AssetImport.SourceInterfaceException: Original original-teams-movement157 requires its exact "
                            "156-clip/13420-frame manifest; old or future inventories are not aliases.",
    "saved-team-mask-not-compared": "team mask must not become bool must reject",
}
ROUTE_FAILURE = "paired resolved sound uses actual output once and returns before the sole generic fallback"
OWNER_FAILURE = "one resolved cue route and camera/host/sound lifecycle owner"
GUARD_FAILURES = {
    "getter-stamp-removed": "actual PlayCamera getter checks exact cached source stage and pure stamped playback",
    "render-changes-source-aspect": "render aspect must not change M1's original DIP camera inputs",
    "frame-publish-not-produced": "stamp only the successfully computed current pure source matrix",
    "runtime-current-frame-not-injected":
        "actual provider samples once, reads current pure frame, sets Core frame INSIDE same pre-player callback",
    "raw-GAME-H-replaced-phase": "use Core's actual GAME.H raw mode, not GamePhase numeric cast",
    "resolved-generic-replay": ROUTE_FAILURE,
    "resolved-falls-through": ROUTE_FAILURE,
    "native-prefix-flush-removed": ROUTE_FAILURE,
    "resolved-output-disconnected": ROUTE_FAILURE,
    "resolved-route-noop": ROUTE_FAILURE,
    "double-resolved-cue-route": OWNER_FAILURE,
    "cue-lifecycle-not-removed": OWNER_FAILURE,
    "double-host-lifecycle": OWNER_FAILURE,
    "step-finally-drain-noop": "every actual Step finally drains before cue delivery and batch completion, including skips/failure",
    "initial-cue-drain-missing": "initial/restart batches use the same drain-before-cue and completion protocol",
    "camera-resolved-wrapper-noop":
        "actual PlayCamera resolved wrapper forwards the same dispatch and output, never a no-op or generic replay",
    "source-only-camera-guard-removed": "source-only camera wiring and all sound lifecycle hooks precede initial cues",
    "restart-sound-reset-removed": "actual sound batch completion and restart hooks are connected to transport, not no-ops",
    "restart-fire-epoch-reset-removed": "actual sound batch completion and restart hooks are connected to transport, not no-ops",
}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    work = OUT / "isolated-current-source"
    work.mkdir()
    source = {path.relative_to(ROOT).as_posix(): path.read_bytes()
              for path in sorted((ROOT / "Assets/Scripts").rglob("*.cs"))}
    rows, guards = [], []
    baselines, closure = {}, {}
    try:
        assert set(PURE_FAILURES) == {row[0] for row in PURE}
        assert set(GUARD_FAILURES) == {row[0] for row in GUARDS}
        spec = importlib.util.spec_from_file_location("actual_movement_caller_guards", SUITE / "run.py")
        runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(runner)
        baselines["actual_caller_guards"] = runner.caller_guards(ROOT)
        for name, data in source.items():
            target = work / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                   DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", PYTHONDONTWRITEBYTECODE="1")
        project = SUITE / "StageChecks.csproj"
        assembly = SUITE / "bin/Release/net10.0/StageChecks.dll"

        def execute(label, source_root, required_failure=None):
            command = ["dotnet", "build", str(project), "--no-restore", "--configuration", "Release",
                       "--verbosity", "quiet", "-p:NuGetAudit=false", "-p:BlitzStageRoot=" + str(source_root) + "/"]
            build = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
            build_output = build.stdout + build.stderr
            if build.returncode and "NETSDK1004" in build_output:
                feed = OUT / "empty-feed"
                feed.mkdir(exist_ok=True)
                restored = subprocess.run(["dotnet", "restore", str(project), "--source", str(feed),
                                          "-p:NuGetAudit=false", "--verbosity", "quiet"],
                                         cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
                (OUT / (label + "-restore.log")).write_text(restored.stdout + restored.stderr)
                assert restored.returncode == 0, (label, "existing SDK restore failed", restored.stdout, restored.stderr)
                build = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
                build_output = build.stdout + build.stderr
            (OUT / (label + "-build.log")).write_text(build_output)
            assert build.returncode == 0, (label, "compilation failure is not a mutation kill", build_output)
            checked = subprocess.run(["dotnet", str(assembly)], cwd=ROOT, env=env,
                                     capture_output=True, text=True, timeout=180)
            output = checked.stdout + checked.stderr
            (OUT / (label + ".log")).write_text(output)
            if required_failure is None:
                assert checked.returncode == 0 and re.fullmatch(
                    r"PASS M1 source-camera/157 packaging: \d+ actual production assertions; no native/game claim\n",
                    output), (label, "actual pure baseline failed", output)
            else:
                assert checked.returncode == 1 and output.splitlines()[0] == required_failure, (
                    label, "mutant failed a different contract", required_failure, output)
            return {"compiled": True, "build_command": command, "build_exit": build.returncode,
                    "run_command": ["dotnet", str(assembly)], "run_exit": checked.returncode, "output": output}

        baselines["actual_project"] = execute("baseline", ROOT)
        print("PASS actual checkout caller/compiled baseline FIRST", flush=True)
        selected = subprocess.run(["dotnet", "msbuild", str(project), "-getItem:Compile",
                                   "-p:BlitzStageRoot=" + str(work) + "/"],
                                  cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
        assert selected.returncode == 0, ("actual compile-item closure query failed", selected.stdout, selected.stderr)
        items = json.loads(selected.stdout)["Items"]["Compile"]
        included = {Path(item["FullPath"]).resolve() for item in items}
        for name in ("Assets/Scripts/Gameplay/SourceSoundVm.cs", "Assets/Scripts/Presentation/SourceCinematicPlayback.cs",
                     "Assets/Scripts/Presentation/SourceAudioFunctions.cs",
                     "Assets/Scripts/Presentation/SourceC2SoundFunctions.cs"):
            assert (work / name).resolve() in included, ("real dependency not selected from mutation root", name)
        assert all((work / name).resolve() in included for name in source if
                   name.startswith(("Assets/Scripts/Gameplay/", "Assets/Scripts/AssetImport/", "Assets/Scripts/Contracts/")))
        assert not any(ROOT / name in included for name in source), "mutation builds must not compile checkout production"
        closure = {"BlitzStageRoot": str(work), "command": selected.args,
                   "compile_items": [item["FullPath"] for item in items],
                   "real_sound_VM_playback_selector_from_mutation_root": True}
        baselines["isolated_caller_guards"] = runner.caller_guards(work)
        baselines["isolated_project"] = execute("isolated-baseline", work)
        assert baselines["isolated_project"]["output"] == baselines["actual_project"]["output"]
        for label, name, before, after in PURE:
            original = source[name].decode()
            assert original.count(before) == 1, (label, "mutation site changed")
            changed = original.replace(before, after, 1)
            (work / name).write_text(changed)
            expected = PURE_FAILURES[label]
            if not expected.startswith("Blitz."):
                expected = "System.InvalidOperationException: FAIL movement-presentation: " + expected
            checked = execute(label, work, expected)
            rows.append({"name": label, "mutated_actual_source_sha256": hashlib.sha256(changed.encode()).hexdigest(),
                         "compiled": True, "rejected_by_actual_production_test": True,
                         "output": checked["output"].splitlines()[0], "required_failure": expected, "execution": checked})
            (work / name).write_bytes(source[name])
            print("REJECTED compiled " + label, flush=True)
        for label, name, before, after in GUARDS:
            original = source[name].decode()
            assert original.count(before) == 1, (label, "actual call site changed")
            (work / name).write_text(original.replace(before, after, 1))
            try:
                runner.caller_guards(work)
            except AssertionError as error:
                expected = "M1 presentation caller guard: " + GUARD_FAILURES[label]
                assert str(error) == expected, (label, "caller mutant failed a different guard", str(error), expected)
                guards.append({"name": label, "rejected": True, "assertion": str(error),
                               "required_assertion": expected,
                               "kind": "actual-source transport guard; not native execution"})
            else:
                raise AssertionError("Actual source caller regression escaped: " + label)
            (work / name).write_bytes(source[name])
            print("REJECTED caller guard " + label, flush=True)
        baselines["final_caller_guards"] = runner.caller_guards(ROOT)
        baselines["final_project"] = execute("final", ROOT)
        assert baselines["final_project"]["output"] == baselines["actual_project"]["output"]
        assert all((ROOT / name).read_bytes() == data for name, data in source.items())
        print("PASS " + str(len(rows)) + " compiled production mutants + " + str(len(guards)) + " caller guard mutants")
        return 0
    finally:
        (OUT / "result.json").write_text(json.dumps({"schema": "movement-presentation-mutants/v1",
            "baselines": baselines, "selected_compile_closure": closure,
            "pure_compiled_mutants": rows, "caller_guard_mutants": guards,
            "complete": len(rows) == len(PURE) and len(guards) == len(GUARDS) and "final_project" in baselines,
            "source_inputs_sha256": {name: hashlib.sha256(data).hexdigest() for name, data in source.items()},
            "actual_checkout_sources_edited": False, "Editor_executed": False}, indent=2) + "\n")
        shutil.rmtree(work)


if __name__ == "__main__":
    raise SystemExit(main())
