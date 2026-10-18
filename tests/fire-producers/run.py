#!/usr/bin/env python3
"""Real Core/factory/fire runtime caller integration; no public replay or Unity execution."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
from xml.sax.saxutils import escape

import ball_blink_reference

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).parent


def main():
    p = argparse.ArgumentParser(description=__doc__)
    for name in ("fixture", "sound", "m1", "bundle", "manifest-sha256", "output"):
        p.add_argument("--" + name, required=True)
    p.add_argument("--reference-proof", type=Path, required=True)
    p.add_argument("--source-root", type=Path, default=ROOT)
    args = p.parse_args()
    out = Path(args.output).absolute()
    if out.exists() or not out.is_relative_to(HERE / ".artifacts"):
        p.error("Use a new ignored caller-proof output.")
    (out / "empty-feed").mkdir(parents=True)
    source = args.source_root.resolve()
    paths = [path for d in ("Contracts", "Gameplay", "AssetImport")
             for path in sorted((source / "Assets/Scripts" / d).glob("*.cs"))]
    paths += [source / "Assets/Scripts" / x for x in (
        "Presentation/SourceCinematicModel.cs", "Presentation/SourceAudioFunctions.cs",
        "Presentation/SourceC2SoundFunctions.cs", "Presentation/SourceFireSpfxRenderModel.cs",
        "Runtime/SourceC2Composition.cs", "Runtime/SourceContactComposition.cs", "Runtime/SourceFireSpfxRuntime.cs")]
    snapshot = {str(f.relative_to(source)): hashlib.sha256(f.read_bytes()).hexdigest() for f in paths}
    project = out / "ProducerChecks.csproj"
    includes = paths + [ROOT / "tests/parity/FullKernelInputs.cs", HERE / "Program.cs"]
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
        '<OutputType>Exe</OutputType><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
        '<UseSharedCompilation>false</UseSharedCompilation><TreatWarningsAsErrors>true</TreatWarningsAsErrors>'
        '</PropertyGroup><ItemGroup>' + "".join('<Compile Include="' + escape(str(f)) + '"/>' for f in includes)
        + '</ItemGroup></Project>')
    env = dict(os.environ, TMPDIR=str(out), TMP=str(out), TEMP=str(out), DOTNET_NOLOGO="1",
               DOTNET_CLI_TELEMETRY_OPTOUT="1", MSBUILDDISABLENODEREUSE="1")
    props = ["-p:BaseIntermediateOutputPath=" + str(out / "obj") + "/", "-p:BaseOutputPath=" + str(out / "bin") + "/"]
    commands = []

    def run(label, argv):
        commands.append(argv)
        result = subprocess.run(argv, cwd=ROOT, env=env, capture_output=True, text=True, timeout=240)
        (out / (label + ".log")).write_text(result.stdout + result.stderr)
        return result

    cmd = ["dotnet", "build", str(project), "--no-restore", "-c", "Release", "--verbosity", "quiet", *props]
    result = run("compile", cmd)
    if result.returncode and "NETSDK1004" in result.stdout + result.stderr:
        restored = run("restore-missing-assets", ["dotnet", "restore", str(project), "--source", str(out / "empty-feed"),
            "--verbosity", "quiet", "-p:NuGetAudit=false", *props])
        if restored.returncode:
            raise SystemExit(restored.stdout + restored.stderr)
        result = run("compile", cmd)
    report = dict(status="failed", source_sha256=snapshot, commands=commands, compile_exit=result.returncode,
                  no_public_replay=True, Unity_executed=False)
    if result.returncode == 0:
        result = run("callers", ["dotnet", str(out / "bin/Release/net10.0/ProducerChecks.dll"),
            args.fixture, args.sound, args.m1, args.bundle, args.manifest_sha256, str(out / "production.json")])
        report["caller_exit"] = result.returncode
        if result.returncode == 0:
            report["production"] = json.loads((out / "production.json").read_bytes())
            ball_blink_reference.require_inventory(report["production"])
            reference = json.loads(args.reference_proof.read_bytes())
            if reference.get("status") != "passed" or reference.get("originalSourceSha256") != \
                    "61fb1be76029663294630e8633574ed065496da1ef6329071817e80622870b52":
                raise AssertionError("Actual pinned original-C reference proof required.")
            executable = args.reference_proof.resolve().parent / "oracle/oracle"
            native = []
            for fixture in report["production"]["nativeEmitterProbes"]:
                values = ",".join(str(v) for v in fixture["input"])
                checked = run("native-emitter", [str(executable), "5", "0", "0", "0", "0", "0", "0", values])
                if checked.returncode:
                    raise AssertionError("Unchanged original emitter component did not return.")
                original = json.loads(checked.stdout)
                if original["requests"] != fixture["requests"] or [
                        {key: row[key] for key in ("bound", "value", "line")} for row in original["random"]] != fixture["random"]:
                    raise AssertionError("Actual Core emitter inputs/requests/draws differ from unchanged original C.")
                native.append(original)
            report["original_C_actual_caller_comparisons"] = native
            report["reference_proof_sha256"] = hashlib.sha256(args.reference_proof.read_bytes()).hexdigest()
            report["reference_executable_sha256"] = hashlib.sha256(executable.read_bytes()).hexdigest()
            try:
                report["original_C_ball_blink_owner"] = ball_blink_reference.compare(
                    reference["originalInputs"]["player"], report["production"], out / "ball-blink-oracle")
            except (AssertionError, OSError, ValueError, subprocess.SubprocessError) as error:
                report["ball_blink_reference_error"] = str(error)
                print("BALL BLINK REFERENCE FAILED: " + str(error), file=sys.stderr)
            else:
                report["status"] = "passed"
    report["source_unchanged"] = snapshot == {str(f.relative_to(source)): hashlib.sha256(f.read_bytes()).hexdigest() for f in paths}
    (out / "result.json").write_text(json.dumps(report, indent=2) + "\n")
    print((out / ("callers.log" if "caller_exit" in report else "compile.log")).read_text()[-7000:])
    return 0 if report["status"] == "passed" and report["source_unchanged"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
