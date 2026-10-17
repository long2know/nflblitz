#!/usr/bin/env python3
"""Compile isolated real pure-source mutants; separately reject real producer/caller guard mutants."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
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
)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    work = OUT / "isolated-current-source"
    work.mkdir()
    source = {path.relative_to(ROOT).as_posix(): path.read_bytes()
              for path in sorted((ROOT / "Assets/Scripts").rglob("*.cs"))}
    rows, guards = [], []
    try:
        for name, data in source.items():
            target = work / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                   DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", PYTHONDONTWRITEBYTECODE="1")
        for label, name, before, after in PURE:
            original = source[name].decode()
            assert original.count(before) == 1, (label, "mutation site changed")
            changed = original.replace(before, after, 1)
            (work / name).write_text(changed)
            command = ["dotnet", "run", "--project", str(SUITE / "StageChecks.csproj"), "--configuration", "Release",
                       "--verbosity", "quiet", "-p:BlitzStageRoot=" + str(work) + "/"]
            result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
            output = result.stdout + result.stderr
            assert "error CS" not in output and "The build failed" not in output, (label, "compile-only failure", output)
            assert result.returncode != 0, (label, "escaped actual pure contract")
            (OUT / (label + ".log")).write_text(output)
            rows.append({"name": label, "mutated_actual_source_sha256": hashlib.sha256(changed.encode()).hexdigest(),
                         "compiled": True, "rejected_by_actual_production_test": True, "output": output.splitlines()[0]})
            (work / name).write_bytes(source[name])
            print("REJECTED compiled " + label, flush=True)
        spec = importlib.util.spec_from_file_location("actual_movement_caller_guards", SUITE / "run.py")
        runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(runner)
        for label, name, before, after in GUARDS:
            original = source[name].decode()
            assert original.count(before) == 1, (label, "actual call site changed")
            (work / name).write_text(original.replace(before, after, 1))
            try:
                runner.caller_guards(work)
            except AssertionError as error:
                guards.append({"name": label, "rejected": True, "assertion": str(error),
                               "kind": "actual-source transport guard; not native execution"})
            else:
                raise AssertionError("Actual source caller regression escaped: " + label)
            (work / name).write_bytes(source[name])
            print("REJECTED caller guard " + label, flush=True)
        assert all((ROOT / name).read_bytes() == data for name, data in source.items())
        print("PASS " + str(len(rows)) + " compiled production mutants + " + str(len(guards)) + " caller guard mutants")
        return 0
    finally:
        (OUT / "result.json").write_text(json.dumps({"schema": "movement-presentation-mutants/v1",
            "pure_compiled_mutants": rows, "caller_guard_mutants": guards,
            "complete": len(rows) == len(PURE) and len(guards) == len(GUARDS),
            "source_inputs_sha256": {name: hashlib.sha256(data).hexdigest() for name, data in source.items()},
            "actual_checkout_sources_edited": False, "Editor_executed": False}, indent=2) + "\n")
        shutil.rmtree(work)


if __name__ == "__main__":
    raise SystemExit(main())
