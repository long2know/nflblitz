#!/usr/bin/env python3
"""Build and run the actual record-cabinet consumer with isolated outputs."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import uuid

root = Path(__file__).resolve().parents[3]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("data_root")
parser.add_argument("selector", nargs="?", choices=[
    "--terminal-only", "--natural-full-match", "--source-menu-driver", "--observed-resume", "--statistics-to-career"])
parser.add_argument("--output", type=Path, default=root / ".squad/.scratch/original-record-ui-20261030")
parser.add_argument("--statistics-controls", action="store_true",
                    help="Run two isolated actual-source controls and force a fresh final statistics baseline")
args, extra = parser.parse_known_args()
selector = args.selector or (extra[0] if len(extra) == 1 and extra[0] in [
    "--terminal-only", "--natural-full-match", "--source-menu-driver", "--observed-resume", "--statistics-to-career"] else None)
if extra and selector is None:
    parser.error("Choose one supported focused selector.")
if args.statistics_controls and selector != "--statistics-to-career":
    parser.error("--statistics-controls requires --statistics-to-career.")
if args.statistics_controls and os.environ.get("SOURCE_RECORD_STAT_CASE"):
    parser.error("The causal-control campaign requires the full sixteen-row baseline, not a case filter.")
sdk = os.environ.get("SOURCE_PLAYER_DOTNET") or shutil.which("dotnet")
if not sdk:
    raise SystemExit("Set SOURCE_PLAYER_DOTNET to the existing SDK; no SDK is installed by this check.")
owned = args.output.resolve()
if not owned.is_relative_to(root / ".squad/.scratch"):
    parser.error("Owned outputs must remain below this worktree .squad/.scratch.")
run = owned / "runs" / uuid.uuid4().hex
run.mkdir(parents=True)
for directory in [owned / "cli", owned / "build-tmp"]:
    directory.mkdir(exist_ok=True)
env = dict(os.environ, DOTNET_ROOT=str(Path(sdk).resolve().parent),
    TMPDIR=str(owned / "build-tmp"), DOTNET_CLI_HOME=str(owned / "cli"),
    DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1",
    DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true")
project = root / "tests/records/cabinet/SourceRecords.CabinetChecks.csproj"
properties = ["-p:DirectoryBuildPropsPath=" + str(project.parent / "OutputPaths.props"),
    "-p:SourceRecordsCabinetOutputRoot=" + str(owned) + "/",
    "-p:UseSharedCompilation=false", "-nodeReuse:false"]

commands = []
def execute(command, name, environment=env):
    commands.append(command)
    (run / name).parent.mkdir(parents=True, exist_ok=True)
    with (run / name).open("wb") as stream:
        result = subprocess.run(command, cwd=root, env=environment, stdout=stream, stderr=subprocess.STDOUT)
    text = (run / name).read_text(errors="replace")
    if selector == "--statistics-to-career" and "checks" in name:
        for line in text.splitlines():
            try:
                row = json.loads(line)
                if "statisticsToCareerCase" in row:
                    print(json.dumps({"case": row["statisticsToCareerCase"], "status": row["status"],
                                      "score": [row["finalHome"], row["finalAway"]], "periods": row["periods"]}))
                elif "statisticsToCareer" in row:
                    print(line)
            except ValueError:
                if line.startswith(("System.", " --->")):
                    print(line)
    else:
        print("\n".join(text.splitlines()[-24:]))
    return result.returncode, text

def build(extra_properties, prefix, rebuild=False):
    command = [sdk, "build", str(project), "-c", "Release", "--no-restore",
               "--nologo", "-v", "quiet", *extra_properties, *(["-t:Rebuild"] if rebuild else [])]
    code, text = execute(command, prefix + "build.log")
    if code and "NETSDK1004" in text:
        code, _ = execute([sdk, "restore", str(project), "--nologo", "-v", "quiet", *extra_properties],
                          prefix + "restore.log")
        if not code:
            code, text = execute(command, prefix + "build-after-restore.log")
    return code, text

source_paths = sorted({* (root / "Assets/Scripts").rglob("*.cs"),
                       * (root / "tools/source-player/Runtime").glob("*.cs"), project.parent / "Program.cs"})
source_hashes = {str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest() for path in source_paths}
code, text = build(properties, "")
baseline = None
if not code:
    assembly = owned / "build/SourceRecords.CabinetChecks/bin/Release/net10.0/SourceRecords.CabinetChecks.dll"
    code, baseline = execute([sdk, str(assembly), str(Path(args.data_root).resolve()),
        *([selector] if selector else [])], "checks.log")
controls = []
if not code and args.statistics_controls:
    cases = (
        ("pass-yardage", "Assets/Scripts/Gameplay/SimulationState.cs", "completed-pass",
         "SourcePassingYards[side] = checked(SourcePassingYards[side] + gain);",
         "SourcePassingYards[side] = checked(SourcePassingYards[side] + gain + 1);",
         "home.passing expected=19 actual=20"),
        ("career-opposite-interception", "Assets/Scripts/Storage/SourcePlayerRecords.cs", "interception",
         "side == 0 ? final.Interceptions.AwayInterceptionsMade : final.Interceptions.HomeInterceptionsMade",
         "side == 0 ? final.Interceptions.HomeInterceptionsMade : final.Interceptions.AwayInterceptionsMade",
         "career.Intercepted expected=1 actual=0"),
    )
    for name, relative, selected, old, new, expected in cases:
        directory = run / "controls" / name
        copied = directory / "source"
        for path in (root / "Assets/Scripts").rglob("*.cs"):
            target = copied / path.relative_to(root)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(path.read_bytes())
        target = copied / relative
        text = target.read_text()
        if text.count(old) != 1:
            raise RuntimeError("The exact actual-source control anchor changed: " + name)
        target.write_text(text.replace(old, new))
        outputs = directory / "output"
        selected_properties = [value for value in properties if not value.startswith("-p:SourceRecordsCabinetOutputRoot=")]
        selected_properties += ["-p:SourceRecordsCabinetOutputRoot=" + str(outputs) + "/",
                                "-p:BlitzSourceRoot=" + str(copied) + "/"]
        build_code, _ = build(selected_properties, "controls/" + name + "/", rebuild=True)
        if build_code:
            raise RuntimeError("A control compile failure is inconclusive, not a semantic rejection: " + name)
        mutant = outputs / "build/SourceRecords.CabinetChecks/bin/Release/net10.0/SourceRecords.CabinetChecks.dll"
        test_code, output = execute([sdk, str(mutant), str(Path(args.data_root).resolve()), "--statistics-to-career"],
                                   "controls/" + name + "/checks.log",
                                   dict(env, SOURCE_RECORD_STAT_CASE=selected))
        if test_code == 0 or expected not in output:
            raise RuntimeError("The actual-source control did not reach its named behavioral assertion: " + name)
        controls.append({"name": name, "buildExit": build_code, "checksExit": test_code, "rejectedBy": expected,
                         "mutatedSourceSha256": hashlib.sha256(target.read_bytes()).hexdigest()})
    code, _ = build(properties, "final-", rebuild=True)
    if not code:
        code, baseline = execute([sdk, str(assembly), str(Path(args.data_root).resolve()), "--statistics-to-career"],
                                 "final-checks.log")
same_generation = source_hashes == {
    str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest() for path in source_paths}
if not same_generation:
    code = 1
final_summary = json.loads(baseline.strip().splitlines()[-1]) if baseline and code == 0 else None
if selector == "--statistics-to-career" and final_summary is not None and not env.get("SOURCE_RECORD_STAT_CASE"):
    if final_summary.get("statisticsToCareer") != "passed" or final_summary.get("cases") != 16:
        raise RuntimeError("The complete statistics selector must report all sixteen accepted cases.")
receipt = {"schema": "actual-cabinet-record-checks/v1", "exitCode": code, "selector": selector,
           "commands": commands, "inputRoot": str(Path(args.data_root).resolve()), "sameGeneration": same_generation,
           "sourceHashes": source_hashes, "compiledSemanticControls": controls,
           "finalSummary": final_summary}
(run / "result.json").write_text(json.dumps(receipt, indent=2) + "\n")
print("Evidence:", run.relative_to(root))
sys.exit(code)
