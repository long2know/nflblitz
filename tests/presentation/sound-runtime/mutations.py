#!/usr/bin/env python3
"""Compile isolated actual sound package/consumer regressions; real data/catalog are explicit optional inputs."""
import argparse
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
PACKAGE = "Assets/Scripts/AssetImport/SourceTeamInterfacePackage.cs"
PLAYBACK = "Assets/Scripts/Presentation/SourceCinematicPlayback.cs"
DEFAULT = (
    ("new-sound-services-alias-old8", PACKAGE,
     'SoundVmServicesResourceKey = "BlitzSourceInterface/original-movement157-soundvm-match-services"',
     'SoundVmServicesResourceKey = "BlitzSourceInterface/original-movement157-match-services"'),
    ("SAFN-resource-alias-animation", PACKAGE,
     'SoundVmFunctionsResourceKey = "SourceData/sound-functions-safn1-4a557304"',
     'SoundVmFunctionsResourceKey = "SourceData/local-plyr266-movement157-f0ae9395"'),
    ("wrong-SAFN-byte-count", PACKAGE, "SoundVmFunctionsBytes = 1983", "SoundVmFunctionsBytes = 1982"),
    ("unignored-sound-output", PACKAGE,
     'assetPath.StartsWith("Assets/Imported/", StringComparison.Ordinal)', 'assetPath.StartsWith("Assets/", StringComparison.Ordinal)'),
)
ACTUAL = (
    ("wrong-trusted-SAFN-hash-admitted", PACKAGE,
     'Require(expectedSha256 == SoundVmFunctionsSha256, "Source SoundVM requires its exact frozen SAFN1 identity.");', ''),
    ("Unsupported-becomes-resolved0", PLAYBACK, "dispatch.Status != SourceSoundVmStatus.Resolved || ", ""),
    ("resolved0-fabricates-output", PLAYBACK, "if (dispatch.Result.Calls.Count == 0) return;", ""),
    ("nonempty-missing-DSP-silenced", PLAYBACK,
     'throw new InvalidOperationException("Resolved original sound has raw output but no actual DSP3 consumer is bound; no silence/isolated preview substitute.");',
     'return;'),
    ("raw-output-delivered-twice", PLAYBACK, "actualRawOutputConsumer(dispatch);",
     "actualRawOutputConsumer(dispatch); actualRawOutputConsumer(dispatch);"),
    ("different-OriginalCue-admitted", PLAYBACK, "!ReferenceEquals(call.OriginalCue, cue) || ", ""),
    ("resolved-cue-order-not-consumed", PLAYBACK,
     "RequireOriginalCallback(cue);\n            SourcePresentationContext context = cue.Context;\n            Ordered(context.SourceTick, context.ProcessOrder, context.ActorPlayerId);",
     "RequireOriginalCallback(cue);\n            SourcePresentationContext context = cue.Context;"),
    ("original-literal-validation-lost", PLAYBACK, "literal != values[i]", "false"),
)
PACKAGE_ACTUAL = (
    ("new-sound-profile-silently-old8", PACKAGE, "HasSoundVm ? SoundVmServicesResourceKey :", "HasSoundVm ? MovementServicesResourceKey :"),
    ("sound-byte-alias-leaked", PACKAGE, "return (byte[])soundVmFunctions.Clone();", "return soundVmFunctions;"),
)
GUARDS = (
    ("C1-factory-binding-disconnected", "Assets/Scripts/Runtime/SourceContactComposition.cs",
     "game.BindSourceSoundVmData(soundFunctions);", "", "bind immutable SAFN"),
    ("C2-factory-binding-disconnected", "Assets/Scripts/Runtime/SourceC2Composition.cs",
     "game.BindSourceSoundVmData(legacySoundFunctions);", "", "bind immutable SAFN"),
    ("services-sound-input-disconnected", "Assets/Scripts/Runtime/SourceInterfaceServices.cs",
     "SourceContactComposition.CreateSimulation(animations, pivots, playbook, Configuration(),\n"
     "                teams.Rosters, PresentationSettings, MovementData, MovementSettings, SoundVmData)",
     "SourceContactComposition.CreateSimulation(animations, pivots, playbook, Configuration(),\n"
     "                teams.Rosters, PresentationSettings, MovementData, MovementSettings, null)",
     "immutable sound to all real C1/C2 factories"),
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sound-vm-data", type=Path)
    parser.add_argument("--impact-catalog", type=Path)
    parser.add_argument("--hud-root", type=Path)
    parser.add_argument("--playbook-root", type=Path)
    parser.add_argument("--movement-data", type=Path)
    args = parser.parse_args()
    if bool(args.sound_vm_data) != bool(args.impact_catalog):
        parser.error("Actual consumer mutants require BOTH explicit SAFN data and original impact catalog.")
    full = bool(args.hud_root and args.playbook_root and args.movement_data and args.sound_vm_data)
    if any((args.hud_root, args.playbook_root, args.movement_data)) and not full:
        parser.error("Actual package mutants require explicit SAFN/HUD/P1/movement together.")
    cases = DEFAULT + (ACTUAL if args.sound_vm_data else ()) + (PACKAGE_ACTUAL if full else ())
    OUT.mkdir(parents=True, exist_ok=True)
    work = OUT / "isolated-source"
    work.mkdir()
    sources = {path.relative_to(ROOT).as_posix(): path.read_bytes() for path in sorted((ROOT / "Assets/Scripts").rglob("*.cs"))}
    results = []
    guard_results = []
    try:
        for name, raw in sources.items():
            target = work / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(raw)
        env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                   DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
        for label, name, before, after in cases:
            original = sources[name].decode()
            assert original.count(before) == 1, (label, "actual mutation site changed")
            changed = original.replace(before, after, 1)
            (work / name).write_text(changed)
            command = ["dotnet", "run", "--project", str(SUITE / "SoundPackagingChecks.csproj"),
                       "--configuration", "Release", "--verbosity", "quiet", "-p:SoundPackagingRoot=" + str(work) + "/", "--",
                       str(args.sound_vm_data.resolve()) if args.sound_vm_data else "-",
                       str(args.impact_catalog.resolve()) if args.impact_catalog else "-"]
            if full:
                command += [str(args.hud_root.resolve()), str(args.playbook_root.resolve()), str(args.movement_data.resolve()), str(work / "package-inputs")]
            result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
            output = result.stdout + result.stderr
            assert "error CS" not in output and "The build failed" not in output, (label, "compile-only mutant", output)
            assert result.returncode != 0, (label, "actual regression escaped")
            assert "FAIL sound-packaging:" in output, (label, "must fail a named artifact/consumer assertion", output)
            (OUT / (label + ".log")).write_text(output)
            results.append({"name": label, "compiled": True, "rejected": True,
                            "actual_source_mutant_sha256": hashlib.sha256(changed.encode()).hexdigest(),
                            "assertion": next(line for line in output.splitlines() if "FAIL sound-packaging:" in line)})
            (work / name).write_bytes(sources[name])
            print("REJECTED compiled " + label, flush=True)
        spec = importlib.util.spec_from_file_location("actual_sound_package_source_guards", SUITE / "run.py")
        runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(runner)
        assert runner.guards(work) == 23
        for label, name, before, after, assertion in GUARDS:
            original = sources[name].decode()
            assert original.count(before) == 1, (label, "actual source guard mutation site changed")
            changed = original.replace(before, after, 1)
            (work / name).write_text(changed)
            try:
                runner.guards(work)
            except AssertionError as error:
                assert assertion in str(error), (label, "unrelated source guard failed", str(error))
                guard_results.append({"name": label, "source_guard_only": True,
                                      "compiled_mutant_claimed": False,
                                      "actual_source_mutant_sha256": hashlib.sha256(changed.encode()).hexdigest(),
                                      "assertion": str(error)})
            else:
                raise AssertionError((label, "actual selected-root source guard regression escaped"))
            (work / name).write_bytes(sources[name])
            print("REJECTED source guard " + label, flush=True)
        assert runner.guards(work) == 23
        assert all((ROOT / name).read_bytes() == raw for name, raw in sources.items())
        print("PASS " + str(len(results)) + " actual compiled sound package/consumer mutants rejected")
        print("PASS " + str(len(guard_results)) + " actual selected-root factory/service guard mutants rejected (not compiled controls)")
        return 0
    finally:
        unchanged = all((ROOT / name).read_bytes() == raw for name, raw in sources.items())
        (OUT / "result.json").write_text(json.dumps({"schema": "sound-packaging-mutants/v1",
            "mutants": results, "expected": len(cases), "source_guard_mutants": guard_results,
            "expected_source_guard_mutants": len(GUARDS),
            "complete": len(results) == len(cases) and len(guard_results) == len(GUARDS) and unchanged,
            "source_inventory_unchanged": unchanged,
            "actual_SAFN_and_catalog": bool(args.sound_vm_data), "actual_package": full,
            "source_sha256": {name: hashlib.sha256(raw).hexdigest() for name, raw in sources.items()},
            "current_checkout_edited": False, "Editor_executed": False}, indent=2) + "\n")
        shutil.rmtree(work)


if __name__ == "__main__":
    raise SystemExit(main())
