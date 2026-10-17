#!/usr/bin/env python3
"""Production pure presentation checks; optional explicitly supplied local art."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
SUITE = Path(__file__).resolve().parent
ARTIFACTS = SUITE / ".artifacts"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--actual", type=Path, help="Explicit ignored prepared-teams-all31 directory.")
    parser.add_argument("--cinematics", type=Path, help="Explicit ignored original impact-presentation.json.")
    parser.add_argument("--source-root", type=Path, help="Explicit pinned original source; compile unchanged C camera functional oracle.")
    args = parser.parse_args()
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    sys.path.insert(0, str(ROOT / "tools/rosters/tests"))
    from fixtures import synthetic_dataset
    from export_rosters import json_bytes
    fixture = ARTIFACTS / "synthetic-rosters.json"
    fixture.write_bytes(json_bytes(synthetic_dataset()))
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1",
               PYTHONDONTWRITEBYTECODE="1", TMPDIR=str(ARTIFACTS), TMP=str(ARTIFACTS), TEMP=str(ARTIFACTS))
    command = ["dotnet", "run", "--project", str(SUITE / "PresentationChecks.csproj"),
               "--configuration", "Release", "--verbosity", "quiet", "--", str(fixture)]
    if args.actual:
        command.append(str(args.actual.resolve(strict=True)))
    elif args.cinematics or args.source_root:
        command.append("-")
    if args.cinematics:
        command.append(str(args.cinematics.resolve(strict=True)))
    if args.source_root:
        if not args.cinematics:
            raise SystemExit("--source-root requires --cinematics, so the oracle checks the actual converted catalog.")
        from source_oracle import generate
        command.append(str(generate(args.source_root, ARTIFACTS / "source-oracle")))
    result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True)
    print(result.stdout, end="")
    print(result.stderr, end="", file=sys.stderr)
    sources = sorted((ROOT / "Assets/Scripts/AssetImport").glob("SourceTeam*.cs"))
    sources += sorted((ROOT / "Assets/Scripts/Presentation").glob("SourceTeam*.cs"))
    sources += [ROOT / "Assets/Scripts/Presentation/SourcePlayerRig.cs",
                ROOT / "Assets/Scripts/Presentation/SourcePlayerProvider.cs"]
    report = {"status": "passed" if result.returncode == 0 else "failed",
              "actual_assets_requested": args.actual is not None,
              "output": result.stdout + result.stderr,
              "production_sha256": {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
                                    for path in sources},
              "editor_executed": False, "native_or_browser_parity_claimed": False}
    (ARTIFACTS / "result.json").write_text(json.dumps(report, indent=2) + "\n")
    return result.returncode


if __name__ == "__main__":
    raise SystemExit(main())
