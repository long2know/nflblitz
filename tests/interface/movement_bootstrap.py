#!/usr/bin/env python3
"""Current-checkout M1 bootstrap gate; optional real Unity API/actual-data composition."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
from types import SimpleNamespace
import uuid
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]
SUITE = ROOT / "tests/interface"
ARTIFACTS = SUITE / ".artifacts/movement-bootstrap"
SERVICES = "Assets/Scripts/Runtime/SourceInterfaceServices.cs"
MUTANTS = (
    ("swapped-defense", "row.Line, row.Back, row.Base, row.Range, row.Plow",
     "row.Back, row.Line, row.Base, row.Range, row.Plow", "actual composition preserves original line-speed bits"),
    ("fake-friction", "new SourceMovementRuntimeData(movement, actual.Friction, rows)",
     "new SourceMovementRuntimeData(movement, new float[7], rows)", "actual composition preserves original friction bits"),
    ("collapsed-team-mask", "actual.SpeedPowerup, actual.FastTurboTeamMask, actual.RunOutOfBoundsTeamMask",
     "actual.SpeedPowerup, actual.FastTurboTeamMask == 0 ? 0 : 1, actual.RunOutOfBoundsTeamMask",
     "actual settings preserve fast-turbo team mask"),
    ("inverted-global-speed", "actual.SpeedPowerup, actual.FastTurboTeamMask",
     "!actual.SpeedPowerup, actual.FastTurboTeamMask", "actual settings preserve global speed"),
)


def require(value, message):
    if not value:
        raise RuntimeError(message)


def execute(command, log, environment, timeout=240):
    process = subprocess.run(command, cwd=ROOT, env=environment, text=True, capture_output=True, timeout=timeout)
    log.parent.mkdir(parents=True, exist_ok=True)
    log.write_text(process.stdout + process.stderr)
    return process


def actual_composition(stage, editor, movement, environment):
    source, _, guarded, _ = stage.release_sources(ROOT)
    work = ARTIFACTS / ("composition-" + uuid.uuid4().hex)
    work.mkdir(parents=True)
    original_artifacts = stage.ARTIFACTS
    results = []
    try:
        for variant in (None,) + MUTANTS + (None,):
            label = "baseline" if not results else "final" if variant is None else variant[0]
            selected = work / label
            for relative, data in source.items():
                target = selected / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                if relative == SERVICES and variant:
                    text = data.decode()
                    require(text.count(variant[1]) == 1, "Actual service mutation target is not unique: " + label)
                    data = text.replace(variant[1], variant[2], 1).encode()
                target.write_bytes(data)
            tests = selected / "tests/interface"
            tests.mkdir(parents=True)
            for name in stage.PROJECTS:
                shutil.copyfile(SUITE / name, tests / name)
            stage.ARTIFACTS = ARTIFACTS / label
            stage.ARTIFACTS.mkdir(parents=True, exist_ok=True)
            options = SimpleNamespace(source_root=selected, core_root=None, team_root=None, base_root=None,
                                      team_manifest=None, dependency_overlay=None, dependency_overlay_sha256=None)
            api = stage.compile_actual(editor, options)
            runtime = Path(api["results"][0]["assembly_path"])
            runner = selected / "runner"
            runner.mkdir()
            shutil.copyfile(SUITE / "MovementCompositionChecks.cs", runner / "Program.cs")
            references = [(runtime.stem, runtime)] + [
                (path.stem, path) for path in sorted((editor / "Managed/UnityEngine").glob("*.dll"))]
            project = runner / "CompositionChecks.csproj"
            project.write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                '<TargetFramework>net10.0</TargetFramework><LangVersion>7.3</LangVersion>'
                '<ImplicitUsings>disable</ImplicitUsings><Nullable>disable</Nullable>'
                '<TreatWarningsAsErrors>true</TreatWarningsAsErrors><UseSharedCompilation>false</UseSharedCompilation>'
                '<NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>' +
                "".join('<Reference Include="' + escape(name) + '"><HintPath>' + escape(str(path)) +
                        '</HintPath></Reference>' for name, path in references) +
                '</ItemGroup></Project>\n')
            built = execute(["dotnet", "build", str(project), "--configuration", "Release", "--verbosity", "quiet"],
                            stage.ARTIFACTS / "runner-build.log", environment)
            require(built.returncode == 0, "Actual-runtime composition runner failed to compile; not mutation evidence: " + label)
            checked = execute(["dotnet", str(runner / "bin/Release/net10.0/CompositionChecks.dll"), str(movement)],
                              stage.ARTIFACTS / "runner.log", environment)
            summaries = [json.loads(line) for line in checked.stdout.splitlines() if line.startswith('{"schema":')]
            require(len(summaries) == 1 and summaries[0]["schema"] == "source-interface-movement-composition-checks/v1",
                    "Missing actual production composition assertion artifact: " + label)
            report = summaries[0]
            if variant:
                require(checked.returncode == 1 and report["status"] == "failed"
                        and report["failed_assertion"] == variant[3],
                        "Actual runtime mutant did not fail its offending-field assertion: " + label)
            else:
                require(checked.returncode == 0 and report["status"] == "passed" and report["assertions"] >= 10000,
                        "Actual production composition baseline failed: " + label)
            results.append({"id": label, "assertions": report["assertions"], "status": "rejected" if variant else "passed",
                            "failed_assertion": report.get("failed_assertion"), "runtime_API_sources": api["scripts"],
                            "actual_Unity_references": True, "Unity_native_execution": False})
        require(results[0]["assertions"] == results[-1]["assertions"], "Actual composition final baseline changed.")
        for path, expected in guarded.items():
            require(stage.digest(path.read_bytes()) == expected, "Shared source changed during actual-runtime checks: " + str(path))
        return results
    finally:
        stage.ARTIFACTS = original_artifacts
        shutil.rmtree(work)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity-editor-data", type=Path, help="Actual Unity6000.3.24 Editor/Data; no Editor is started.")
    parser.add_argument("--movement", type=Path, help="Explicit original89c790 runtime payload; requires actual Unity references.")
    args = parser.parse_args(argv)
    if args.movement and not args.unity_editor_data:
        parser.error("--movement requires --unity-editor-data to execute the actual complete Runtime assembly, not stubs.")
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    cache = ARTIFACTS / "process-local"
    cache.mkdir(exist_ok=True)
    environment = dict(os.environ, TMPDIR=str(cache), TMP=str(cache), TEMP=str(cache),
                       PYTHONDONTWRITEBYTECODE="1", DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    result = {"schema": "source-interface-movement-bootstrap-gate/v1", "status": "failed",
              "source_mode": "explicit-current-checkout", "Unity_native_Editor_browser_executed": False,
              "whole_M1_parity_claimed": False, "actual_movement_input_supplied": args.movement is not None}
    try:
        command = [sys.executable, "-B", str(SUITE / "run.py"), "--bootstrap-mutations"]
        checked = execute(command, ARTIFACTS / "pure.log", environment)
        require(checked.returncode == 0, "Pure actual bootstrap/mutation tests failed; see pure.log.")
        pure = json.loads((SUITE / ".artifacts/result-synthetic.json").read_bytes())
        result["pure_assertions"] = pure["baseline"]["assertions"]
        result["compiled_bootstrap_mutants_rejected"] = len(pure["bootstrap_mutations"])
        command = [sys.executable, "-B", str(SUITE / "stage_followup.py"), "--verify-clean-checkout"]
        if args.unity_editor_data:
            command += ["--unity-editor-data", str(args.unity_editor_data.resolve(strict=True))]
        checked = execute(command, ARTIFACTS / "stage.log", environment)
        require(checked.returncode == 0, "Actual runtime/camera/services inventory gate failed; see stage.log.")
        stage_report = json.loads((SUITE / ".artifacts/stage-followup" /
                                   ("result-api.json" if args.unity_editor_data else "result-guards.json")).read_bytes())
        result["stage"] = stage_report
        if args.movement:
            movement = args.movement.resolve(strict=True)
            spec = importlib.util.spec_from_file_location("actual_interface_stage", SUITE / "stage_followup.py")
            stage = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(stage)
            result["actual_runtime_composition"] = actual_composition(
                stage, args.unity_editor_data.resolve(strict=True), movement, environment)
        result.update(status="passed", implicit_original_assets_or_scratch_snapshots_required=False)
        print(json.dumps({"status": "passed", "pure_assertions": result["pure_assertions"],
                          "compiled_bootstrap_mutants_rejected": result["compiled_bootstrap_mutants_rejected"],
                          "runtime_structural_mutants_rejected": stage_report["rejected_actual_source_guard_mutants"] +
                              stage_report["rejected_actual_service_guard_mutants"] +
                              stage_report["rejected_actual_factory_guard_mutants"],
                          "actual_Unity_API_compiled": args.unity_editor_data is not None,
                          "actual_runtime_data_tested": args.movement is not None, "native_claimed": False}))
        return 0
    except (OSError, ValueError, RuntimeError, AssertionError, subprocess.SubprocessError) as error:
        result["error"] = str(error)
        print("M1 interface bootstrap gate failed: " + str(error), file=sys.stderr)
        return 1
    finally:
        encoded = json.dumps(result, indent=2) + "\n"
        (ARTIFACTS / "result.json").write_text(encoded)
        (ARTIFACTS / ("result-api.json" if args.unity_editor_data else "result-guards.json")).write_text(encoded)


if __name__ == "__main__":
    raise SystemExit(main())
