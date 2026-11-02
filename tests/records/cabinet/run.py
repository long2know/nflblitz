#!/usr/bin/env python3
"""Build and run the actual record-cabinet consumer with isolated outputs."""
import argparse
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
    "--terminal-only", "--natural-full-match", "--source-menu-driver", "--observed-resume"])
args, extra = parser.parse_known_args()
selector = args.selector or (extra[0] if len(extra) == 1 and extra[0] in [
    "--terminal-only", "--natural-full-match", "--source-menu-driver", "--observed-resume"] else None)
if extra and selector is None:
    parser.error("Choose one supported focused selector.")
sdk = os.environ.get("SOURCE_PLAYER_DOTNET") or shutil.which("dotnet")
if not sdk:
    raise SystemExit("Set SOURCE_PLAYER_DOTNET to the existing SDK; no SDK is installed by this check.")
owned = root / ".squad/.scratch/original-record-ui-20261030"
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
    "-p:UseSharedCompilation=false", "-nodeReuse:false"]

def execute(command, name):
    with (run / name).open("wb") as stream:
        result = subprocess.run(command, cwd=root, env=env, stdout=stream, stderr=subprocess.STDOUT)
    text = (run / name).read_text(errors="replace")
    print("\n".join(text.splitlines()[-24:]))
    return result.returncode, text

code, text = execute([sdk, "build", str(project), "-c", "Release", "--no-restore",
    "--nologo", "-v", "quiet", *properties], "build.log")
if code and "NETSDK1004" in text:
    code, _ = execute([sdk, "restore", str(project), "--nologo", "-v", "quiet", *properties], "restore.log")
    if not code:
        code, _ = execute([sdk, "build", str(project), "-c", "Release", "--no-restore",
            "--nologo", "-v", "quiet", *properties], "build-after-restore.log")
if not code:
    assembly = owned / "build/SourceRecords.CabinetChecks/bin/Release/net10.0/SourceRecords.CabinetChecks.dll"
    code, _ = execute([sdk, str(assembly), str(Path(args.data_root).resolve()),
        *([selector] if selector else [])], "checks.log")
print("Evidence:", run.relative_to(root))
sys.exit(code)
