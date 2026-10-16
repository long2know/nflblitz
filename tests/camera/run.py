#!/usr/bin/env python3
"""Run the original camera/kernel checks and six isolated real-model mutations."""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import uuid

suite = Path(__file__).resolve().parent
root = suite.parents[1]
artifacts = suite / ".artifacts"
workspace = artifacts / ("run-" + uuid.uuid4().hex)
workspace.mkdir(parents=True, exist_ok=False)
report = artifacts / "result.json"
report.unlink(missing_ok=True)
project = suite / "CameraChecks.csproj"
expected_assertions = 3568
model = root / "Assets/Scripts/Presentation/NormalCameraModel.cs"
def production_files():
    paths = [model, root / "Assets/Scripts/Editor/NormalCameraChecks.cs",
             root / "Assets/Scripts/Presentation/PlayCamera.cs"]
    paths += sorted((root / "Assets/Scripts/Contracts").rglob("*.cs"))
    paths += sorted((root / "Assets/Scripts/Gameplay").rglob("*.cs"))
    paths += sorted((root / "Assets/Scripts/AssetImport").glob("*.cs"))
    return paths

def hashes():
    return {path.relative_to(root).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in production_files()}

before = hashes()
env = dict(os.environ)
for key, subdir in (("TMPDIR", "work"), ("DOTNET_CLI_HOME", "dotnet-home"), ("NUGET_PACKAGES", "nuget")):
    directory = artifacts / subdir
    directory.mkdir(exist_ok=True)
    env[key] = str(directory)
env["TMP"] = env["TEMP"] = env["TMPDIR"]
env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")

def run(source_root=None):
    extra = [] if source_root is None else ["-p:BlitzSourceRoot=" + source_root.as_posix() + "/"]
    return subprocess.run(["dotnet", "run", "--project", str(project), "--configuration", "Release",
                           "--verbosity", "quiet", *extra], cwd=root, env=env, capture_output=True, text=True)

def check_success(result):
    print(result.stdout.strip())
    match = re.search(r"^PASS: normal camera / (\d+) behavioral assertions / exact production model \+ kernel", result.stdout, re.MULTILINE)
    if result.returncode or match is None or int(match.group(1)) != expected_assertions:
        raise SystemExit(result.stdout + result.stderr + "\nExpected exactly 3568 camera behavioral assertions.")
    return int(match.group(1))

mutations = [
    ("focal", "ReferenceFocalPixels = 664f", "ReferenceFocalPixels = 512f", "664px focal length"),
    ("qb-lead", "QuarterbackLeadYards = 6f", "QuarterbackLeadYards = 0f", "lead six yards"),
    ("dead-orientation", "else if (!stopped)", "else", "must not flip the view"),
    ("viewport-fit", "distance = Math.Max(distance, RequiredDistance(state, setup, aspect));",
     "distance = Math.Max(distance, 0f);", "escaped the safe perspective viewport"),
    ("units", "ArchiveUnitsPerYard = 390f / 50f", "ArchiveUnitsPerYard = 390f / 39f",
     "source pole at 57 Hz"),
    ("frame-time", "1d - Math.Exp(-FollowRate * deltaSeconds)", "0.1d",
     "source pole at 57 Hz"),
]
mutation_results = []
try:
    baseline_assertions = check_success(run())
    for name, original, replacement, expected in mutations:
        source_root = workspace / ("source-" + name)
        for path in production_files():
            copied = source_root / path.relative_to(root)
            copied.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, copied)
        isolated = source_root / model.relative_to(root)
        source = isolated.read_text(encoding="utf-8")
        if source.count(original) != 1:
            raise SystemExit("Mutation anchor is not unique: " + name)
        isolated.write_text(source.replace(original, replacement), encoding="utf-8")
        result = run(source_root)
        detail = result.stdout + result.stderr
        if result.returncode == 0 or expected not in detail:
            raise SystemExit("Mutation did not fail its intended assertion: " + name + "\n" + detail)
        print("PASS mutation: " + name + " -> " + expected)
        mutation_results.append({"name": name, "rejected_by": expected})
    final_assertions = check_success(run())
finally:
    shutil.rmtree(workspace)
    if before != hashes():
        raise SystemExit("Production sources changed during isolated validation.")

result = {
    "status": "passed",
    "target": "C# 7.3, .NET 10; exact production camera, contracts, gameplay kernel and pure editor checks",
    "assertions": baseline_assertions,
    "final_baseline_assertions": final_assertions,
    "expected_assertions": expected_assertions,
    "mutations": mutation_results,
    "production_sha256_before": before,
    "production_sha256_after": hashes(),
    "production_hashes_unchanged": True,
    "isolated_source_and_fixture_trees_removed": not workspace.exists(),
    "native_unity_checks": "not compiled or executed; UNITY_EDITOR is not defined and no Unity stubs are supplied",
}
report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print("PASS: six isolated real-model mutations rejected; production SHA-256 hashes unchanged.")
