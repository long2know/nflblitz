#!/usr/bin/env python3
"""Run the original importer/pose checks and eight mutations against isolated source trees."""
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
expected_assertions = 2751
production = [root / "Assets/Scripts/AssetImport" / name for name in (
    "GeneratedCInitializer.cs", "PlayerImportPlan.cs", "GeneratedPlayerReader.cs",
    "BindPoseMeshBuilder.cs", "GeneratedPlayerData.cs", "AuthorizedAssetFiles.cs",
    "SourcePlayerPose.cs",
)]

def hashes():
    return {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in production}

before = hashes()
env = dict(os.environ)
for variable, name in (("TMPDIR", "work"), ("DOTNET_CLI_HOME", "dotnet-home"), ("NUGET_PACKAGES", "nuget")):
    directory = artifacts / name
    directory.mkdir(exist_ok=True)
    env[variable] = str(directory)
env["TMP"] = env["TEMP"] = env["TMPDIR"]
env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
project = suite / "ImportChecks.csproj"

def run(name, source_root=None):
    fixtures = workspace / ("fixtures-" + name)
    fixtures.mkdir(exist_ok=False)
    try:
        property_args = [] if source_root is None else ["-p:BlitzSourceRoot=" + source_root.as_posix() + "/"]
        return subprocess.run(["dotnet", "run", "--project", str(project), "--configuration", "Release",
                               "--verbosity", "quiet", *property_args, "--", str(fixtures)],
                              cwd=root, env=env, text=True, capture_output=True)
    finally:
        shutil.rmtree(fixtures)

def check_success(result):
    print(result.stdout.strip())
    match = re.search(r"^PASS: (\d+) artifact assertions / exact production C# 7\.3 netstandard2\.1", result.stdout, re.MULTILINE)
    if result.returncode or match is None or int(match.group(1)) != expected_assertions:
        raise SystemExit(result.stdout + result.stderr + f"\nExpected exactly {expected_assertions} importer artifact assertions.")
    return int(match.group(1))

mutations = [
    ("stride", "GeneratedPlayerReader.cs", "ReaderPath", "vertices[corner] = packedVertex / 3;",
     "vertices[corner] = packedVertex / 1;", "valid synthetic player parses packed stride-three indices"),
    ("uv-state", "GeneratedPlayerReader.cs", "ReaderPath", "uvForVertex[vertices[corner]] = rawUv[corner];",
     "uvForVertex[vertices[corner]] = 0;", "first face ST indices"),
    ("pivot", "BindPoseMeshBuilder.cs", "BuilderPath", "positions.Add(conversion.Convert(limb.Vertices[vertex]) - pivot);",
     "positions.Add(conversion.Convert(limb.Vertices[vertex]));", "root mesh pivot subtraction"),
    ("winding", "GeneratedPlayerData.cs", "DataPath", "^ (Determinant < 0)", "^ false",
     "reflection compensates triangle winding"),
    ("hash", "AuthorizedAssetFiles.cs", "FilesPath", "sha.ComputeHash(bytes)", "sha.ComputeHash(new byte[0])",
     "SHA-256 hello known answer"),
    ("pose-basis", "SourcePlayerPose.cs", "PosePath", "axis.X * conversion.Determinant",
     "axis.X", "pose reflection axial X sign"),
    ("pose-root-anchor", "SourcePlayerPose.cs", "PosePath", "new AssetVector3(0, ScaledRootTranslation.Y, 0)",
     "ScaledRootTranslation", "pose source XZ anchor X"),
    ("pose-pivot", "SourcePlayerPose.cs", "PosePath", "Translation(new AssetVector3(-p.X,-p.Y,-p.Z))",
     "Translation(new AssetVector3())", "pose known transformed point"),
]
mutation_results = []
try:
    baseline_assertions = check_success(run("baseline"))
    for name, file_name, property_name, anchor, replacement, expected in mutations:
        source_root = workspace / ("source-" + name)
        for path in production:
            copied = source_root / path.relative_to(root)
            copied.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, copied)
        isolated = source_root / "Assets/Scripts/AssetImport" / file_name
        source = isolated.read_text(encoding="utf-8")
        if source.count(anchor) != 1:
            raise SystemExit("Mutation anchor is not unique: " + name)
        isolated.write_text(source.replace(anchor, replacement), encoding="utf-8")
        result = run(name, source_root)
        if result.returncode == 0 or expected not in result.stdout + result.stderr:
            raise SystemExit("Mutation did not fail its intended artifact assertion: " + name + "\n" + result.stdout + result.stderr)
        print("PASS mutation: " + name + " -> " + expected)
        mutation_results.append({"name": name, "rejected_by": expected})
    final_assertions = check_success(run("final"))
finally:
    shutil.rmtree(workspace)
    if before != hashes():
        raise SystemExit("Production files changed during isolated validation.")

result = {
    "status": "passed",
    "target": "C# 7.3, netstandard2.1 exact pure production files, .NET 10 test executable",
    "assertions": baseline_assertions,
    "final_baseline_assertions": final_assertions,
    "expected_assertions": expected_assertions,
    "mutations": mutation_results,
    "production_sha256_before": before,
    "production_sha256_after": hashes(),
    "production_hashes_unchanged": True,
    "isolated_source_and_fixture_trees_removed": not workspace.exists(),
    "native_unity_adapter": "not compiled or executed; requires separate actual licensed Unity checks",
    "original_midway_assets_read_by_tests": False,
}
report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print("PASS: eight isolated real-code mutations rejected; production SHA-256 hashes unchanged; no Unity editor launched.")
