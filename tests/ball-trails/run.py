#!/usr/bin/env python3
"""Compile and execute the actual production football-trail contract; no Unity process."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
from xml.sax.saxutils import escape

import oracle

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--manifest-sha256", required=True)
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--playbook", type=Path)
    for name in oracle.PINS:
        parser.add_argument("--original-" + name, type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    if not output.is_relative_to(HERE / ".artifacts") or output.exists():
        parser.error("Choose a new owned ignored ball-trails artifact directory.")
    output.mkdir(parents=True)
    root = args.source_root.resolve()
    playbook = args.playbook
    if playbook is None:
        manifest = json.loads((args.bundle.resolve().parent / "manifest.json").read_bytes())
        playbook = args.bundle.resolve().parent / manifest["entrypoints"]["playbook"]
    project = output / "BallTrail.Checks.csproj"
    project.write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><LangVersion>7.3</LangVersion>
<EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>disable</ImplicitUsings>
<Nullable>disable</Nullable><UseSharedCompilation>false</UseSharedCompilation>
</PropertyGroup><Import Project="''' + escape(str(ROOT / "tools/source-player/ProductionSources.props")) +
                       '''" /><ItemGroup><Compile Include="''' + escape(str(HERE / "Program.cs")) +
                       '''" /></ItemGroup></Project>\n''')
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output))
    dotnet = ["bash", str(ROOT / "tools/source-player/dotnet.sh")]
    properties = ["-p:BlitzSourceRoot=" + str(root) + "/",
                  "-p:BaseIntermediateOutputPath=" + str(output / "obj") + "/"]
    report = {"schema": "source-ball-trails-production-proof/v1", "status": "failed",
              "sourceRoot": str(root), "commands": [], "nativeQualified": False, "UnityExecuted": False,
              "testSha256": sha(HERE / "Program.cs"),
              "playbook": str(playbook), "playbookSha256": sha(playbook),
              "productionSha256": {p.relative_to(root).as_posix(): sha(p)
                                   for p in sorted((root / "Assets/Scripts").rglob("*.cs"))}}

    def run(label, command):
        result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
        log = output / (label + ".log")
        log.write_text(result.stdout + result.stderr)
        report["commands"].append({"label": label, "argv": command, "exitCode": result.returncode,
                                   "logSha256": sha(log)})
        return result

    try:
        command = dotnet + ["build", str(project), "--no-restore", "-c", "Release", "--nologo",
                            "-v", "quiet", "-o", str(output / "managed")] + properties
        built = run("build-first", command)
        if built.returncode and "NETSDK1004" in built.stdout + built.stderr:
            feed = output / "empty-feed"
            feed.mkdir()
            restored = run("offline-restore", dotnet + ["restore", str(project), "--source", str(feed),
                           "-p:NuGetAudit=false", "-v", "quiet"] + properties)
            if restored.returncode:
                raise AssertionError(restored.stdout + restored.stderr)
            built = run("build-after-restore", command)
        report["compiled"] = built.returncode == 0
        if built.returncode:
            raise AssertionError(built.stdout + built.stderr)
        assembly = output / "managed/BallTrail.Checks.dll"
        report["assemblySha256"] = sha(assembly)
        executed = run("production", dotnet + [str(assembly), str(args.bundle.resolve()),
                       args.manifest_sha256, str(output / "production.json"), str(playbook.resolve())])
        print(executed.stdout + executed.stderr, end="")
        if executed.returncode:
            raise AssertionError("Compiled actual-production ball-trail assertions failed.")
        report["production"] = json.loads((output / "production.json").read_bytes())
        originals = {name: getattr(args, "original_" + name) for name in oracle.PINS}
        if any(originals.values()):
            if not all(originals.values()):
                raise AssertionError("Supply the complete explicit pinned original trail/draw/header/scheduler closure.")
            report["originalC"] = oracle.run(originals, report["production"], output / "original-c")
        report["status"] = "passed"
        return 0
    except (AssertionError, OSError, subprocess.SubprocessError) as error:
        report["error"] = str(error)
        print(str(error))
        return 1
    finally:
        (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    raise SystemExit(main())
