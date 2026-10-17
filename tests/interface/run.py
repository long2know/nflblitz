#!/usr/bin/env python3
"""Actual C#7.3/netstandard2.1 interface tests and isolated real-production mutants."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[2]
SUITE = Path(__file__).resolve().parent
ARTIFACTS = SUITE / ".artifacts"
MODEL = [
    "Assets/Scripts/AssetImport/SourceTeamRosterLibrary.cs",
    "Assets/Scripts/AssetImport/SourceTeamPresentationModel.cs",
    "Assets/Scripts/AssetImport/SourceInterfaceData.cs",
    "Assets/Scripts/AssetImport/SourceInterfaceMovementData.cs",
    "Assets/Scripts/Runtime/FixedStepBridge.cs",
    "Assets/Scripts/Runtime/SourceCabinetInput.cs",
    "Assets/Scripts/Runtime/SourceCabinetInputPlaySelection.cs",
    "Assets/Scripts/Runtime/SourceInterfacePresentation.cs",
    "Assets/Scripts/Presentation/SourceHudViewport.cs",
    "Assets/Scripts/Presentation/SourceHudModel.cs",
    "Assets/Scripts/Presentation/SourceHudComposer.cs",
]
ENGINE = [
    "Assets/Scripts/Presentation/SourceHudAsset.cs",
    "Assets/Scripts/Runtime/SourceHudWebViewport.cs",
    "Assets/Scripts/Runtime/SourceCabinetInputUnity.cs",
]
MUTATIONS = (
    ("wrong-hall-byte", "Runtime/SourceCabinetInput.cs", "tables.Raw49[rawHallByte]",
     "tables.Raw49[(rawHallByte + 1) & 255]", "Goose original raw49 byte"),
    ("lost-pressed-combo", "Runtime/SourceCabinetInput.cs",
     "SourceButtons pressed = sample.Held & ~previous[sample.StationId];",
     "SourceButtons pressed = SourceButtons.None;", "source simultaneous ABC press remains a raw combo"),
    ("lost-release", "Runtime/SourceCabinetInput.cs",
     "SourceButtons released = previous[sample.StationId] & ~sample.Held;",
     "SourceButtons released = SourceButtons.None;", "source release edges are preserved"),
    ("captain-as-first", "Runtime/SourceCabinetInput.cs",
     "return second < 0 || match.Possession.Captain == SourceCaptainSlot.First ? first : second;",
     "return first;", "captain is teammate slot"),
    ("buffer-is-css", "Presentation/SourceHudViewport.cs", "Portrait = viewport.ContentWidth < 650;",
     "Portrait = viewport.BufferWidth < 650;", "portrait actual DPR2 uses CSS metrics"),
    ("wrong-font-centering", "Presentation/SourceHudModel.cs",
     "Math.Truncate(width * scale / 2)", "Math.Ceiling(width * scale / 2)", "Goose centered text truncates half-width"),
    ("legacy-clock-as-source", "Presentation/SourceHudModel.cs",
     "int minute = (int)((source.PackedGameTime >> 16) & 255)",
     "int minute = state.QuarterTicksRemaining / 3600", "source HUD reads actual packed clock"),
    ("combined-menu-priority", "Runtime/SourceCabinetInputPlaySelection.cs", "switch (input.Pressed)",
     "switch (input.Pressed == (SourceButtons.A | SourceButtons.B) ? SourceButtons.B : input.Pressed)",
     "shipping exact menu switch ignores simultaneous AB"),
    ("visual-only-flip", "Runtime/SourceCabinetInputPlaySelection.cs",
     "return new SourceCommand(SourceAction.None, offenseFlipped: Flipped);",
     "return default(SourceCommand);", "A flip is transported explicitly"),
    ("last-source-event-only", "Runtime/FixedStepBridge.cs",
     "for (int i = 0; i < sourceEvents.Count; i++)",
     "for (int i = sourceEvents.Count - 1; i < sourceEvents.Count; i++)", "every catch-up Step forwards all legacy and source events"),
    ("phantom-inferred-held", "Runtime/SourceInterfacePresentation.cs",
     "(state.Ball.Presentation.RawFlags & 0x800) != 0", "state.Ball.Mode != BallMode.Held",
     "raw draw flags/mode/phantom/bigball map from actual source metadata only"),
    ("original-home-name-overlap", "Presentation/SourceHudComposer.cs",
     "score.X + 62 * scale, score.Y + 18 * scale",
     "score.X + 24 * scale, score.Y + 18 * scale",
     "actual original clock glyphs never overlap HOME/team name"),
    ("movement-numeric-bits-cast", "AssetImport/SourceInterfaceMovementData.cs",
     "float decoded = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);",
     "float decoded = bits;", "runtime movement constant exact binary32 bits"),
)
BOOTSTRAP_MUTATIONS = (
    ("bootstrap-content-pin", "AssetImport/SourceInterfaceMovementData.cs",
     "if (RosterGuard.Sha256(detached) != trustedContentSha256)", "if (detached.Length < 0)", "bootstrap content pin is mandatory"),
    ("bootstrap-field", "AssetImport/SourceInterfaceMovementData.cs",
     'FieldIndex = root.Integer("field_index", 0, 6);',
     'FieldIndex = root.Integer("field_index", 0, 6) == 0 ? 0 : 1;', "bootstrap retains selected field index"),
    ("bootstrap-infinite-mask", "AssetImport/SourceInterfaceMovementData.cs",
     'InfiniteTurboTeamMask = root.Integer("infinite_turbo_team_mask", 0, 3);',
     'InfiniteTurboTeamMask = root.Integer("infinite_turbo_team_mask", 0, 3) & 1;', "bootstrap retains both infinite-turbo team bits"),
    ("bootstrap-fast-mask", "AssetImport/SourceInterfaceMovementData.cs",
     'FastTurboTeamMask = root.Integer("fast_turbo_team_mask", 0, 3);',
     'FastTurboTeamMask = root.Integer("fast_turbo_team_mask", 0, 3) & 1;', "bootstrap retains both fast-turbo team bits"),
    ("bootstrap-bounds-mask", "AssetImport/SourceInterfaceMovementData.cs",
     'RunOutOfBoundsTeamMask = root.Integer("run_out_of_bounds_team_mask", 0, 3);',
     'RunOutOfBoundsTeamMask = root.Integer("run_out_of_bounds_team_mask", 0, 3) & 1;', "bootstrap retains both out-of-bounds team bits"),
    ("bootstrap-global-speed", "AssetImport/SourceInterfaceMovementData.cs",
     'SpeedPowerup = root.Boolean("speed_powerup");', 'SpeedPowerup = !root.Boolean("speed_powerup");',
     "bootstrap speed powerup remains global"),
    ("bootstrap-violence", "AssetImport/SourceInterfaceMovementData.cs",
     'ViolencePowerup = root.Boolean("violence_powerup");', 'ViolencePowerup = !root.Boolean("violence_powerup");',
     "bootstrap violence powerup remains explicit"),
    ("bootstrap-raster", "AssetImport/SourceInterfaceMovementData.cs",
     'VerticalResolution = vertical;', 'VerticalResolution = 384;', "bootstrap retains exact explicit source DIP raster"),
    ("bootstrap-browser-dimensions", "AssetImport/SourceInterfaceMovementData.cs",
     'HorizontalResolution = root.Integer("source_hres", 512, 512);',
     'HorizontalResolution = root.Integer("source_hres", 1, int.MaxValue);', "bootstrap rejects non-original DIP"),
    ("bootstrap-unknown-members", "AssetImport/SourceInterfaceMovementData.cs",
     'root.Done();', '', "bootstrap rejects unknown or fixture-only fields"),
)


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def fingerprint(paths):
    return {path.relative_to(ROOT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, help="Explicit ignored actual tools/interface prepared output.")
    parser.add_argument("--source-root", type=Path, help="Explicit pinned nflblitz2000 inputs for original C table/scan oracle.")
    parser.add_argument("--tools-root", type=Path, help="Explicit pinned midwayvideo inputs.")
    parser.add_argument("--mutations", action="store_true", help="Reject all thirteen real production-source mutants; requires actual inputs.")
    parser.add_argument("--bootstrap-mutations", action="store_true",
                        help="Reject ten actual production M1 bootstrap mutants without original assets or Unity.")
    parser.add_argument("--movement", type=Path, help="Explicit pinned runtime-safe tools/movement payload; never the test Responses export.")
    parser.add_argument("--unity-managed", type=Path, help="Actual Editor/Data/Managed/UnityEngine directory; compile owned adapters, no Editor launch.")
    args = parser.parse_args(argv)
    if bool(args.source_root) != bool(args.tools_root) or args.source_root and not args.bundle:
        parser.error("An original C oracle requires --bundle, --source-root and --tools-root together.")
    if args.mutations and not (args.bundle and args.source_root and args.tools_root):
        parser.error("--mutations requires the explicit actual bundle and both original roots; no silently skipped original gate.")
    if args.mutations and not args.movement:
        parser.error("--mutations also requires the actual runtime-safe --movement payload.")
    if args.movement and not args.bundle:
        parser.error("--movement requires the actual interface --bundle in this combined consumer gate.")
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    work = ARTIFACTS / ("interface-run-" + uuid.uuid4().hex)
    cache = ARTIFACTS / "runtime-cache"
    cache.mkdir(exist_ok=True)
    environment = dict(os.environ, TMPDIR=str(cache), TMP=str(cache), TEMP=str(cache),
                       PYTHONDONTWRITEBYTECODE="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    paths = sorted((ROOT / "Assets/Scripts/Contracts").glob("*.cs")) + [ROOT / p for p in MODEL + ENGINE]
    before = fingerprint(paths)
    contents = {path.relative_to(ROOT).as_posix(): path.read_bytes() for path in paths}
    result = {"schema": "source-interface-regressions/v1", "status": "failed", "commands": [], "mutations": [],
              "profile": "Actual production C#7.3/netstandard2.1 model, warnings-as-errors; net10.0 runner.",
              "original_inputs_supplied": args.source_root is not None, "bootstrap_mutations": [],
              "unity_editor_native_build_browser_or_hosting_executed": False,
              "whole_game_parity_claimed": False, "source_sha256": before}
    baseline = None
    try:
        require(fingerprint(paths) == before, "Production interface dependencies changed during source capture.")
        oracle = None
        if args.source_root:
            from source_oracle import generate
            oracle = generate(args.source_root.resolve(strict=True), args.tools_root.resolve(strict=True), ARTIFACTS / "source-oracle")
        actual = [str(args.bundle.resolve(strict=True))] if args.bundle else []
        if oracle:
            actual.append(str(oracle))
        elif args.movement:
            actual.append("-")
        if args.movement:
            actual.append(str(args.movement.resolve(strict=True)))
        for label, command in (
                ("converter", [sys.executable, "-B", "-m", "unittest", "discover", "-s", "tools/interface", "-p", "test_*.py", "-v"]),
                ("interop", ["node", "--test", "tests/interface/interop.test.mjs"])):
            result["commands"].append(command)
            process = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=120)
            (ARTIFACTS / (label + ".log")).write_text(process.stdout + process.stderr, encoding="utf-8")
            require(process.returncode == 0, "Focused interface " + label + " tests failed; see artifact log.")
        def execute(label, mutation=None):
            source_root = work / label
            for relative, data in contents.items():
                target = source_root / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
            if mutation:
                _, name, old, new, _ = mutation
                target = source_root / "Assets/Scripts" / name
                text = target.read_text(encoding="utf-8")
                require(text.count(old) == 1, "Mutation target is no longer unique: " + label)
                target.write_text(text.replace(old, new), encoding="utf-8")
            project_root = source_root / "tests/interface"
            project_root.mkdir(parents=True, exist_ok=True)
            for name in ("Program.cs", "MovementBootstrapChecks.cs", "InterfaceChecks.csproj", "InterfaceModel.csproj",
                         "Directory.Build.props", "UnityAPICompile.csproj"):
                shutil.copyfile(SUITE / name, project_root / name)
            build = ["dotnet", "build", str(project_root / "InterfaceChecks.csproj"),
                     "--configuration", "Release", "--verbosity", "quiet",
                     "-p:BlitzSourceRoot=" + str(source_root) + "/"]
            result["commands"].append(build)
            compiled = subprocess.run(build, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=180)
            (ARTIFACTS / (label + "-build.log")).write_text(compiled.stdout + compiled.stderr, encoding="utf-8")
            require(compiled.returncode == 0, "Compilation error is NOT a rejected assertion mutant: " + label)
            command = ["dotnet", str(project_root / "bin/Release/net10.0/InterfaceChecks.dll")] + actual
            result["commands"].append(command)
            checked = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=120)
            (ARTIFACTS / (label + ".log")).write_text(checked.stdout + checked.stderr, encoding="utf-8")
            summaries = [json.loads(line) for line in checked.stdout.splitlines() if line.startswith('{"schema":')]
            require(len(summaries) == 1 and summaries[0].get("schema") == "source-interface-checks/v1",
                    "Missing/duplicate actual interface assertion summary: " + label)
            summary = summaries[0]
            if mutation:
                require(checked.returncode == 1 and summary.get("status") == "failed"
                        and mutation[4] in summary.get("failed_assertion", ""),
                        "Mutant did not fail its exact offending-field assertion: " + label + " " + str(summary))
            else:
                require(checked.returncode == 0 and summary.get("status") == "passed" and summary.get("assertions", 0) >= 1500
                        and summary.get("unity_browser_parity_claimed") is False, "Interface baseline did not prove the expected artifact contracts.")
                if args.bundle:
                    require(summary.get("actual_assertions", 0) >= 14000, "Actual original HUD/input inventory was not exercised.")
            return summary, project_root
        baseline, project = execute("baseline")
        result["baseline"] = baseline
        if args.unity_managed:
            managed = args.unity_managed.resolve(strict=True)
            require((managed / "UnityEngine.CoreModule.dll").is_file(), "Actual Unity engine API references are missing.")
            result["actual_unity_api_compiles"] = []
            for define in ("UNITY_EDITOR", "UNITY_WEBGL"):
                command = ["dotnet", "build", str(project / "UnityAPICompile.csproj"),
                           "--configuration", "Release", "--verbosity", "quiet",
                           "-p:BlitzSourceRoot=" + str(project.parents[1]) + "/",
                           "-p:UnityManagedRoot=" + str(managed), "-p:BlitzUnityDefines=" + define]
                result["commands"].append(command)
                compiled = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=180)
                (ARTIFACTS / (define + "-api.log")).write_text(compiled.stdout + compiled.stderr, encoding="utf-8")
                require(compiled.returncode == 0, "Owned adapter actual Unity API compile failed: " + define)
                result["actual_unity_api_compiles"].append({"defines": define, "references": str(managed), "status": "passed"})
        if args.mutations:
            for mutation in MUTATIONS:
                summary, _ = execute(mutation[0], mutation)
                result["mutations"].append({"id": mutation[0], "offending_assertion": summary["failed_assertion"], "status": "rejected"})
        if args.bootstrap_mutations:
            for mutation in BOOTSTRAP_MUTATIONS:
                summary, _ = execute(mutation[0], mutation)
                result["bootstrap_mutations"].append({"id": mutation[0], "offending_assertion": summary["failed_assertion"], "status": "rejected"})
        final, _ = execute("final-baseline")
        require(final == baseline, "Baseline and final assertion artifacts changed.")
        require(fingerprint(paths) == before, "Live production source changed during the isolated interface tests.")
        result.update(status="passed", baseline_and_final_equal=True, production_hashes_unchanged=True)
        print(json.dumps({"status": "passed", "assertions": baseline["assertions"], "actual_assertions": baseline["actual_assertions"],
                          "rejected_mutants": len(result["mutations"]),
                          "rejected_bootstrap_mutants": len(result["bootstrap_mutations"]),
                          "editor_or_browser_executed": False}))
        return 0
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        result["error"] = str(error)
        print("Source interface regression gate failed: " + str(error), file=sys.stderr)
        return 1
    finally:
        encoded = json.dumps(result, indent=2) + "\n"
        (ARTIFACTS / "result.json").write_text(encoded, encoding="utf-8")
        (ARTIFACTS / ("result-original.json" if args.bundle else "result-synthetic.json")).write_text(encoded, encoding="utf-8")
        if work.exists():
            shutil.rmtree(work)


if __name__ == "__main__":
    raise SystemExit(main())
