#!/usr/bin/env python3
"""Compile production preflight from explicit roots and guard their source inventory."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
SUITE = Path(__file__).resolve().parent
ARTIFACTS = SUITE / ".artifacts"

def source_hashes(roots):
    paths = {path for root in set(roots) for path in (root / "Assets/Scripts").rglob("*.cs")}
    return {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(paths)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--b1-root", type=Path, required=True)
    parser.add_argument("--ui-root", type=Path, required=True)
    parser.add_argument("--team-root", type=Path, required=True)
    parser.add_argument("--hud-root", type=Path)
    parser.add_argument("--playbook-root", type=Path)
    args = parser.parse_args()
    if bool(args.hud_root) != bool(args.playbook_root):
        raise SystemExit("Supply both actual HUD and original playbook roots, or neither.")
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    roots = [args.b1_root.resolve(strict=True), args.ui_root.resolve(strict=True), args.team_root.resolve(strict=True)]
    dependencies = source_hashes(roots + [ROOT])
    production = [ROOT / "Assets/Scripts/AssetImport/SourceTeamInterfacePackage.cs",
                  ROOT / "Assets/Scripts/Editor/SourceTeamInterfaceImporter.cs",
                  ROOT / "Assets/Scripts/Editor/SourceTeamInterfaceChecks.cs"]
    protected = {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in production}
    command = ["dotnet", "run", "--project", str(SUITE / "PackagingChecks.csproj"), "--configuration", "Release",
               "--verbosity", "quiet", "-p:BlitzB1Root=" + str(roots[0]) + "/",
               "-p:BlitzUiRoot=" + str(roots[1]) + "/", "-p:BlitzTeamRoot=" + str(roots[2]) + "/", "--"]
    if args.hud_root:
        command += [str(args.hud_root.resolve(strict=True)), str(args.playbook_root.resolve(strict=True)), str(ARTIFACTS), str(roots[1])]
    env = dict(os.environ, TMPDIR=str(ARTIFACTS), TMP=str(ARTIFACTS), TEMP=str(ARTIFACTS),
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True)
    print(result.stdout, end=""); print(result.stderr, end="", file=sys.stderr)
    if protected != {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in production}:
        raise RuntimeError("Production preflight sources changed during validation.")
    if dependencies != source_hashes(roots + [ROOT]):
        raise RuntimeError("Explicit dependency source inventory or hashes changed during validation.")
    (ARTIFACTS / "result.json").write_text(json.dumps({
        "status": "passed" if result.returncode == 0 else "failed", "actual_input_roots": [str(p) for p in roots],
        "production_sha256": protected, "output": result.stdout + result.stderr, "command": command,
        "dependency_source_sha256": dependencies, "input_roots_explicit": True,
        "Unity_stubs": False, "Editor_executed": False, "native_assets_saved": False,
    }, indent=2) + "\n")
    return result.returncode


if __name__ == "__main__":
    raise SystemExit(main())
