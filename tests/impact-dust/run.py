#!/usr/bin/env python3
"""Compile and execute real pure source-effect code; synthetic inputs, no original assets or Unity."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def require(value, message):
    if not value:
        raise AssertionError(message)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--controls", action="store_true")
    parser.add_argument("--source-root", type=Path, default=ROOT)
    args = parser.parse_args()
    output = args.output.resolve()
    require(output.is_relative_to(ROOT / ".squad/.scratch") and not output.exists(),
            "Choose a new owned ignored impact-dust output.")
    output.mkdir(parents=True)
    source_root = args.source_root.resolve()
    names = ["AssetImport/AuthorizedAssetFiles.cs", "AssetImport/GeneratedPlayerData.cs",
             "AssetImport/SourceFireSpfxData.cs", "Runtime/SourceFireSpfxRuntime.cs",
             "Presentation/SourceFireSpfxRenderModel.cs"]
    files = {p.relative_to(source_root).as_posix(): p.read_bytes()
             for p in (source_root / "Assets/Scripts/Contracts").glob("*.cs")}
    files.update({"Assets/Scripts/" + name: (source_root / "Assets/Scripts" / name).read_bytes() for name in names})
    files["Program.cs"] = (HERE / "Program.cs").read_bytes()
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output),
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    report = {"schema": "source-impact-dust-asset-free/v1", "status": "failed",
              "command": [sys.executable, *sys.argv], "sources": {p: digest(b) for p, b in files.items()},
              "syntheticInputs": True, "originalAssetsRequired": False, "UnityExecuted": False,
              "nativeQualified": False, "runs": []}
    try:
        sdks = subprocess.check_output(["dotnet", "--list-sdks"], env=env, text=True)
        version, prefix = max(re.findall(r"^(\d+\.\d+\.\d+)[^\n]*\[([^\]]+)\]$", sdks, re.M),
                              key=lambda row: tuple(map(int, row[0].split("."))))
        compiler = Path(prefix) / version / "Roslyn/bincore/csc.dll"
        refs = sorted((Path(prefix).parent / "packs/Microsoft.NETCore.App.Ref").glob("10.0.*/ref/net10.0"))[-1]

        def run(label, sources):
            directory = output / label
            directory.mkdir()
            paths = []
            for name, raw in sources.items():
                path = directory / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(raw)
                paths.append(path)
            assembly = directory / "Dust.Checks.dll"
            options = ["-nologo", "-nostdlib+", "-target:exe", "-langversion:7.3", "-warnaserror+",
                       "-deterministic+", "-out:" + str(assembly)]
            options += ["-r:" + str(p) for p in sorted(refs.glob("*.dll"))] + [str(p) for p in paths]
            response = directory / "compile.rsp"
            response.write_text("\n".join(json.dumps(o) for o in options) + "\n")
            command = ["dotnet", str(compiler), "-noconfig", "@" + str(response)]
            built = subprocess.run(command, env=env, cwd=ROOT, capture_output=True, text=True, timeout=120)
            (directory / "build.log").write_text(built.stdout + built.stderr)
            row = {"name": label, "compile": built.returncode, "compileCommand": command}
            report["runs"].append(row)
            require(built.returncode == 0 and not (built.stdout + built.stderr),
                    "Compile errors never count as behavioral control failures: " + built.stdout + built.stderr)
            assembly.with_suffix(".runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
                "tfm": "net10.0", "framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"}}}))
            result = subprocess.run(["dotnet", str(assembly)], env=env, cwd=ROOT, capture_output=True,
                                    text=True, timeout=60)
            text = result.stdout + result.stderr
            (directory / "run.log").write_text(text)
            row.update(exit=result.returncode, output=text, assemblySha256=digest(assembly.read_bytes()))
            print(label + ": " + text, end="")
            return row

        baseline = run("baseline", files)
        require(baseline["exit"] == 0, "Real compiled dust behavior failed.")
        if args.controls:
            for label, name, before, after, expected in mutations():
                original = files[name].decode()
                require(original.count(before) == 1, "Unique real-source mutation anchor: " + label)
                mutant = dict(files)
                mutant[name] = original.replace(before, after, 1).encode()
                row = run(label, mutant)
                row["requiredNamedFailure"] = expected
                require(row["exit"] == 1 and "DUST ASSERTION: " + expected in row["output"],
                        "Compiled semantic control survived or failed for wrong cause: " + label)
            require(run("final-baseline", files)["exit"] == 0, "Final production baseline failed.")
        require(all((source_root / name).read_bytes() == raw for name, raw in files.items() if name != "Program.cs"),
                "Production source changed during bounded proof.")
        report["status"] = "passed"
        return 0
    except (AssertionError, OSError, subprocess.SubprocessError) as error:
        report["error"] = str(error)
        print(str(error), file=sys.stderr)
        return 1
    finally:
        (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")


def mutations():
    runtime = "Assets/Scripts/Runtime/SourceFireSpfxRuntime.cs"
    contracts = "Assets/Scripts/Contracts/SourceFireSpfxContracts.cs"
    return [
        ("child-order", contracts, "argument == 1 ? new[] { 14, 11 }", "argument == 1 ? new[] { 11, 14 }",
         "helper must preserve original 1/2/4 qcreate joint order"),
        ("entry-rng-bound", runtime, "p.Cursor = Draw(3, Site(9624", "p.Cursor = Draw(4, Site(9624",
         "exactly one shared randrng(3) must occur at original entry line9624"),
        ("texture-too-early", runtime, "if (p.Wait > p.Args.A2)", "if (p.Wait >= p.Args.A2)",
         "initial DIRT0 then original j>sleep_time texture timing"),
        ("random-initial-texture", runtime, "p.Wait = 0; p.State = 1;", "p.Wait = 0; p.State = 1; obj.Frame = p.Cursor;",
         "initial DIRT0 then original j>sleep_time texture timing"),
        ("entry-camera-transpose", runtime,
         "float x = j.X * m[0] + j.Y * m[4] + j.Z * m[8] + c.X;\n                float z",
         "float x = j.X * m[0] + j.Y * m[1] + j.Z * m[2] + c.X;\n                float z",
         "entry cached camera transpose and frozen X velocity"),
        ("missing-exit-alert", runtime, "if (p.Entry == SourceFireEntry.DustCloud && p.ObjectSerial != 0)",
         "if (p.Entry == SourceFireEntry.DustCloud && p.ObjectSerial < 0)",
         "kill must invoke dust_cloud_die_alert and delete its still-existing object exactly once"),
        ("dust-bound-to-fire-texture", contracts,
         "TextureFrame + (TextureKind == SourceFireTextureKind.Dust ? SourceFireIds.FrameCount : 0)",
         "TextureFrame + (TextureKind == SourceFireTextureKind.Dust ? 0 : 0)",
         "real frame consumer must bind original DIRT, not TRAIL or a counter"),
    ]


if __name__ == "__main__":
    raise SystemExit(main())
