#!/usr/bin/env python3
"""Reject realistic mutations of actual production presentation code, never live edits."""
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
MUTATIONS = (
    ("physical-away", "Assets/Scripts/AssetImport/SourceTeamPresentationModel.cs",
     "SourceUniformVariant variant = side == 0 ? pair.Side0 : pair.Side1;",
     "SourceUniformVariant variant = SourceUniformVariant.Home;", "clash matrix"),
    ("all-white-skin", "Assets/Scripts/AssetImport/SourceTeamPresentationModel.cs",
     "library.GetUniform(team, variant, player.Skin)", "library.GetUniform(team, variant, SourceSkinSelector.White)", "roster skin"),
    ("flipped-hand", "Assets/Scripts/AssetImport/SourceTeamPresentationModel.cs",
     "draw.QuarterbackMode && draw.Flip ?", "draw.QuarterbackMode && !draw.Flip ?", "held/flip/jumbo"),
    ("role-is-not-plyrmode", "Assets/Scripts/AssetImport/SourceTeamPresentationModel.cs",
     "source.RawPlayerMode == 5", "actor.Role == Blitz.Contracts.ActorRole.Quarterback", "not ActorRole"),
    ("wrong-phantom-bit", "Assets/Scripts/AssetImport/SourceTeamPresentationModel.cs",
     "(ball.RawFlags & 0x800) != 0", "(ball.RawFlags & 0x200) != 0", "not ActorRole"),
    ("decimal-number", "Assets/Scripts/AssetImport/SourceTeamGeneratedModels.cs",
     "int tens = packedNumber >> 4", "int tens = packedNumber / 10", "BCD"),
    ("duplicated-number-digit", "Assets/Scripts/AssetImport/SourceTeamGeneratedModels.cs",
     "int value = digit == 0 ? tens : ones;", "int value = tens;", "ones"),
    ("camera-extra-tick", "Assets/Scripts/Presentation/SourceCinematicModel.cs",
     "tick >= entry.ModeTicks", "tick > entry.ModeTicks", "expiry"),
    ("source-spline-stretch", "Assets/Scripts/Presentation/SourceCinematicModel.cs",
     "SourceCinematicMath.Scale(movement, 2f / 3f)", "SourceCinematicMath.Scale(movement, 1f / 3f)", "unchanged original C"),
    ("camera-tick-zero-acceleration", "Assets/Scripts/Presentation/SourceCinematicModel.cs",
     "AccelerateDecelerate(0, entry.TravelTicks); flags = MoveEye;",
     "flags = MoveEye;", "unchanged original C"),
    ("shaker-carrier-guard", "Assets/Scripts/Presentation/SourceImpactShaker.cs",
     "if (executingActorId == carrierId) ElapsedTicks = 0;", "ElapsedTicks = 0;", "noncarrier"),
    ("shaker-decay", "Assets/Scripts/Presentation/SourceImpactShaker.cs",
     "exponent[64 * ElapsedTicks / TotalTicks]", "exponent[0]", "actual source shaker"),
    ("dust-wait", "Assets/Scripts/Presentation/SourceImpactShaker.cs",
     "if (wait > sleepTime)", "if (wait >= sleepTime)", "source dust"),
    ("duplicate-camera-consumption", "Assets/Scripts/Presentation/SourceCinematicPlayback.cs",
     "sourceTick <= lastCameraTick", "sourceTick < lastCameraTick", "duplicate source camera"),
)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--actual", type=Path, required=True)
    parser.add_argument("--cinematics", type=Path, required=True)
    parser.add_argument("--oracle", type=Path, default=ARTIFACTS / "source-oracle/source-camera-oracle.tsv")
    args = parser.parse_args()
    fixture = ARTIFACTS / "synthetic-rosters.json"
    if not fixture.is_file() or not args.oracle.is_file():
        raise SystemExit("Run run.py --actual --cinematics --source-root first; actual baseline and C oracle are required.")
    source_files = sorted((ROOT / "Assets/Scripts/AssetImport").glob("*.cs"))
    source_files += sorted((ROOT / "Assets/Scripts/Contracts").glob("*.cs"))
    source_files += sorted((ROOT / "Assets/Scripts/Gameplay").glob("*.cs"))
    for name in ("SourceCinematicModel", "SourceCinematicPlayback", "SourceNormalCameraModel", "SourceImpactShaker",
                 "SourceCinematicDepthRange", "SourceAudioFunctions", "SourceC2SoundFunctions"):
        source_files.append(ROOT / ("Assets/Scripts/Presentation/" + name + ".cs"))
    protected = {str(path.relative_to(ROOT)): digest(path) for path in source_files
                 if not path.name.startswith("SourceInterface")}
    workspace = ARTIFACTS / ("mutations-" + uuid.uuid4().hex)
    workspace.mkdir(parents=True, exist_ok=False)
    env = dict(os.environ, TMPDIR=str(workspace), TMP=str(workspace), TEMP=str(workspace),
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    results = []
    try:
        for name, label, original, replacement, diagnostic in MUTATIONS:
            copied_root = workspace / name
            for path in source_files:
                target = copied_root / path.relative_to(ROOT)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(path, target)
            changed = copied_root / label
            source = changed.read_text()
            if source.count(original) != 1:
                raise RuntimeError("Mutation anchor is not unique: " + name)
            changed.write_text(source.replace(original, replacement))
            command = ["dotnet", "run", "--project", str(SUITE / "PresentationChecks.csproj"), "--configuration", "Release",
                       "--verbosity", "quiet", "-p:BlitzSourceRoot=" + str(copied_root) + "/", "--",
                       str(fixture), str(args.actual.resolve()), str(args.cinematics.resolve()), str(args.oracle.resolve())]
            result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True)
            output = result.stdout + result.stderr
            if result.returncode == 0 or diagnostic.lower() not in output.lower() or "error CS" in output:
                raise RuntimeError("Actual source mutant did not fail the intended behavioral assertion: " + name + "\n" + output)
            print("PASS actual production mutation: " + name + " -> " + diagnostic)
            results.append({"name": name, "file": label, "rejected_by": diagnostic,
                            "mutant_output_sha256": hashlib.sha256(output.encode()).hexdigest()})
    finally:
        shutil.rmtree(workspace)
        after = {label: digest(ROOT / label) for label in protected}
        if after != protected:
            raise RuntimeError("Actual protected production sources changed during isolated mutation validation.")
    (ARTIFACTS / "mutations.json").write_text(json.dumps({
        "status": "passed", "mutants": results, "protected_source_sha256": protected,
        "live_production_changed": False, "isolated_mutant_sources_removed": True,
    }, indent=2) + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
