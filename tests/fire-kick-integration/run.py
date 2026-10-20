#!/usr/bin/env python3
"""Real combined M1 cabinet/fire/meter/physics/HUD callers and compiled causal controls."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).parent
sys.path.insert(0, str(ROOT / "tests/parity"))
from kernel_sources import production_paths, canonical_hashes
from prepare_inputs import fixture_hashes

GAME = "Assets/Scripts/Gameplay/"
FACTORY = "Assets/Scripts/Runtime/SourceC2Composition.cs"
EXTRA = (
    "Assets/Scripts/Runtime/SourceCabinetInput.cs",
    "Assets/Scripts/Runtime/SourceCabinetInputPlaySelection.cs",
    "Assets/Scripts/Runtime/SourceFireSpfxRuntime.cs",
    "Assets/Scripts/Presentation/SourceFireSpfxRenderModel.cs",
    "Assets/Scripts/Presentation/SourceHudModel.cs",
    "Assets/Scripts/Presentation/SourceHudViewport.cs",
    "Assets/Scripts/Presentation/SourceHudComposer.cs",
)
CONTROLS = (
    ("lost-fire-forwarding", [(FACTORY, "                fireEffects, kickMeterMetrics);",
                              "                null, kickMeterMetrics);")],
     "factory forwards fire independently of kick metrics"),
    ("lost-kick-forwarding", [(FACTORY, "                fireEffects, kickMeterMetrics);",
                              "                fireEffects, null);")],
     "factory forwards kick metrics independently of fire"),
    ("kick-suppresses-fire", [(GAME + "GameSimulation.cs", "sourceFireConfiguration = fireEffects;",
                             "sourceFireConfiguration = kickMeterMetrics == null ? fireEffects : null;")],
     "factory forwards fire independently of kick metrics"),
    ("fire-suppresses-meter", [(GAME + "SourceKickMeterProcess.cs",
                              "            Model.SourceKickMeter = null;",
                              "            Model.SourceKickMeter = null;\n            if (SourceFireEnabled) return;")],
     "actual raw cabinet selection creates the meter even when fire is enabled"),
    ("meter-after-players", [
        (GAME + "GameSimulation.Source.cs", "            StepSourceKickMeter();", "            // isolated post-player race control"),
        (GAME + "GameSimulation.Source.cs", "            UpdateSourceMovementPlayers(command);",
         "            UpdateSourceMovementPlayers(command);\n            StepSourceKickMeter();")],
     "combined raw snap still precedes the meter's NEXT pre-player continuation"),
    ("mixed-frame-disconnected", [(GAME + "GameSimulation.Source.cs",
        "            Model.SourcePossessionMessage = CaptureSourcePossessionMessage();\n            CaptureSourceFireFrame();",
        "            Model.SourcePossessionMessage = CaptureSourcePossessionMessage();\n            if (Model.SourceKickMeter == null) CaptureSourceFireFrame();")],
     "completed combined source frame matches the actual source wake"),
    ("mixed-epoch-leak", [(GAME + "GameSimulation.FireSpfx.cs",
        "sourceFireCamera = null; SourceFireFrame = null;", "sourceFireCamera = null;")],
     "restart clears the combined fire epoch but retains the independent option"),
)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("fixture", "sound", "m1", "hud", "bundle", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--manifest-sha256", required=True)
    args = parser.parse_args()
    output = args.output.absolute()
    if output.exists() or not output.is_relative_to(ROOT / ".squad/.scratch"):
        parser.error("Use a new ignored worktree-local output.")
    (output / "work/empty-feed").mkdir(parents=True)
    env = dict(os.environ, TMPDIR=str(output / "work"), TMP=str(output / "work"), TEMP=str(output / "work"),
               DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", MSBUILDDISABLENODEREUSE="1")
    paths = set(production_paths()) | {ROOT / name for name in EXTRA}
    snapshot = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in paths}
    harness = [ROOT / "tests/parity/FullKernelInputs.cs", HERE / "Program.cs"]
    harness += sorted((ROOT / "tests/kick-meter").glob("*.cs"))
    guarded_harness = {str(p.relative_to(ROOT)): sha(p.read_bytes()) for p in harness + [HERE / "run.py"]}
    inputs = fixture_hashes(args.fixture, args.sound)
    inputs.update(canonical_hashes(args.m1, "contact281-c2-m1"))
    inputs[str(args.hud.absolute())] = sha(args.hud.read_bytes())
    inputs.update({str(p): sha(p.read_bytes()) for p in args.bundle.resolve().rglob("*") if p.is_file()})
    result = dict(schema="source-fire-kick-integration/v1", status="failed",
                  production_before={name: sha(raw) for name, raw in snapshot.items()},
                  harness_before=guarded_harness, inputs_before=inputs, commands=[], controls=[])

    def command(label, argv):
        process = subprocess.run(argv, cwd=ROOT, env=env, capture_output=True, text=True, timeout=240)
        log = (process.stdout + process.stderr).encode()
        (output / (label + ".log")).write_bytes(log)
        result["commands"].append(dict(label=label, argv=argv, exit_code=process.returncode, log_sha256=sha(log)))
        return process

    def execute(label, edits=()):
        source = ROOT if not edits else output / "copies" / label
        if edits:
            for name, raw in snapshot.items():
                p = source / name
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_bytes(raw)
            for name, old, new in edits:
                p = source / name
                raw = p.read_text()
                if raw.count(old) != 1:
                    raise RuntimeError("Unique actual-production control anchor required: " + label + ":" + name)
                p.write_text(raw.replace(old, new))
        build = output / "build" / label
        build.mkdir(parents=True)
        project = build / "FireKickChecks.csproj"
        sources = [source / name for name in sorted(snapshot)] + harness
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
            '<OutputType>Exe</OutputType><StartupObject>IntegrationEntry</StartupObject><LangVersion>7.3</LangVersion>'
            '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><UseSharedCompilation>false</UseSharedCompilation>'
            '<TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup><ItemGroup>'
            + "".join('<Compile Include="' + escape(str(p)) + '"/>' for p in sources) + '</ItemGroup></Project>')
        props = ["-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/", "-p:BaseOutputPath=" + str(build / "bin") + "/"]
        argv = ["dotnet", "build", str(project), "--no-restore", "-c", "Release", "--verbosity", "quiet", *props]
        process = command(label + "-compile", argv)
        if process.returncode and "NETSDK1004" in process.stdout + process.stderr:
            restored = command(label + "-offline-missing-assets", ["dotnet", "restore", str(project), "--source",
                str(output / "work/empty-feed"), "--verbosity", "quiet", "-p:NuGetAudit=false", *props])
            if restored.returncode:
                raise RuntimeError("Missing SDK assets could not be restored offline: " + label)
            process = command(label + "-compile", argv)
        if process.returncode or "0 Warning(s)" not in process.stdout or "0 Error(s)" not in process.stdout:
            raise RuntimeError("Successful zero-diagnostic compilation is required, not a control kill: " + label)
        report = output / (label + ".json")
        argv = ["dotnet", str(build / "bin/Release/net10.0/FireKickChecks.dll"),
                str(args.fixture.absolute()), str(args.sound.absolute()), str(args.m1.absolute()),
                str(args.hud.absolute()), str(args.bundle.absolute()), args.manifest_sha256, str(report)]
        process = command(label, argv)
        observed = json.loads(report.read_bytes())
        if edits:
            shutil.rmtree(source)
        return process.returncode, observed

    def baseline(code, observed):
        if (code or observed.get("status") != "passed" or observed.get("combinedRawCases") != 16
                or observed.get("combinedLaunches") != 8 or observed.get("combinedDisabledGuards") != 8
                or observed.get("combinedRestarts") != 17 or len(observed.get("cases", [])) != 16):
            raise RuntimeError("Exact combined caller/reset inventory required: " + json.dumps(observed)[-5000:])

    try:
        code, observed = execute("baseline")
        baseline(code, observed)
        result["baseline"] = observed
        print("PASS combined baseline: " + str(observed["assertions"]) + " assertions", flush=True)
        for identity, edits, expected in CONTROLS:
            code, failed = execute(identity, edits)
            if (code != 1 or failed.get("status") != "failed"
                    or "FIRE_KICK_INTEGRATION_ASSERTION: " + expected not in failed.get("error", "")):
                raise RuntimeError("Compiled control must reject its exact real-caller contract: "
                                   + identity + ":" + json.dumps(failed)[-2500:])
            result["controls"].append(dict(id=identity, compiled_zero_diagnostics=True, named_failure=expected,
                                           assertions_before_failure=failed["assertions"]))
            print("PASS compiled combined control: " + identity, flush=True)
        code, final = execute("final-baseline")
        baseline(code, final)
        if final != observed:
            raise RuntimeError("Actual baseline/final state and feature observations differ.")
        result["final_baseline"] = final
        result["status"] = "passed"
    except (RuntimeError, OSError, ValueError, subprocess.SubprocessError) as error:
        result["error"] = str(error)
        print(str(error), file=sys.stderr)
    finally:
        result["production_after"] = {name: sha((ROOT / name).read_bytes()) for name in snapshot}
        result["harness_after"] = {name: sha((ROOT / name).read_bytes()) for name in guarded_harness}
        result["inputs_after"] = {name: sha(Path(name).read_bytes()) for name in inputs}
        if (result["production_before"] != result["production_after"] or guarded_harness != result["harness_after"]
                or inputs != result["inputs_after"]):
            result["status"] = "inconclusive-generation-changed"
        if (output / "copies").exists():
            shutil.rmtree(output / "copies")
        (output / "result.json").write_text(json.dumps(result, indent=2) + "\n")
    return 0 if result["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
