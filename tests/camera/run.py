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
selector = root / "Assets/Scripts/Presentation/SourceAudioFunctions.cs"
c2_selector = root / "Assets/Scripts/Presentation/SourceC2SoundFunctions.cs"
def production_files():
    paths = [model, root / "Assets/Scripts/Editor/NormalCameraChecks.cs",
             root / "Assets/Scripts/Presentation/PlayCamera.cs", selector, c2_selector]
    paths += sorted((root / "Assets/Scripts/Contracts").rglob("*.cs"))
    paths += sorted((root / "Assets/Scripts/Gameplay").rglob("*.cs"))
    paths += sorted((root / "Assets/Scripts/AssetImport").glob("*.cs"))
    return paths

def hashes():
    return {path.relative_to(root).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in production_files()}

before = hashes()
sources = {path.relative_to(root): path.read_bytes() for path in production_files()}
if {path.as_posix(): hashlib.sha256(data).hexdigest() for path, data in sources.items()} != before:
    raise SystemExit("Production sources changed while capturing the camera snapshot.")
env = dict(os.environ)
for key, subdir in (("TMPDIR", "work"), ("DOTNET_CLI_HOME", "dotnet-home"), ("NUGET_PACKAGES", "nuget")):
    directory = artifacts / subdir
    directory.mkdir(exist_ok=True)
    env[key] = str(directory)
env["TMP"] = env["TEMP"] = env["TMPDIR"]
env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
commands = []
build_results = {}

def invoke(command, label, phase):
    result = subprocess.run(command, cwd=root, env=env, capture_output=True, text=True, timeout=180)
    commands.append({"label": label, "phase": phase, "command": command, "exit_code": result.returncode})
    (artifacts / (label + "-" + phase + ".log")).write_text(result.stdout + result.stderr, encoding="utf-8")
    return result

def run(source_root, label):
    build_root = workspace / ("build-" + label)
    properties = ["-p:BlitzSourceRoot=" + source_root.as_posix() + "/",
                  "-p:BaseIntermediateOutputPath=" + str(build_root / "obj") + "/",
                  "-p:BaseOutputPath=" + str(build_root / "bin") + "/"]
    command = ["dotnet", "build", str(project), "--no-restore", "--configuration", "Release",
               "--verbosity", "quiet", *properties]
    compiled = invoke(command, label, "build")
    if compiled.returncode and "NETSDK1004" in compiled.stdout + compiled.stderr:
        feed = workspace / "empty-feed"
        feed.mkdir(exist_ok=True)
        restored = invoke(["dotnet", "restore", str(project), "--source", str(feed),
                           "-p:NuGetAudit=false", "--verbosity", "quiet", *properties], label, "restore-local-sdk")
        if restored.returncode:
            raise RuntimeError(restored.stdout + restored.stderr)
        compiled = invoke(command, label, "build-restored")
    build_results[label] = compiled.returncode
    if compiled.returncode:
        raise RuntimeError("Compilation error is NOT a rejected camera assertion mutant: " + label + "\n" +
                           compiled.stdout + compiled.stderr)
    return invoke(["dotnet", str(build_root / "bin/Release/net10.0/CameraChecks.dll")], label, "execute")

def snapshot(name, mutation):
    source_root = workspace / ("source-" + name)
    for relative, data in sources.items():
        copied = source_root / relative
        copied.parent.mkdir(parents=True, exist_ok=True)
        copied.write_bytes(data)
    isolated = source_root / model.relative_to(root)
    source = isolated.read_text(encoding="utf-8")
    _, original, replacement, _ = mutation
    if source.count(original) != 1:
        raise SystemExit("Mutation anchor is not unique: " + name)
    isolated.write_text(source.replace(original, replacement), encoding="utf-8")
    copied_hashes = {relative.as_posix(): hashlib.sha256((source_root / relative).read_bytes()).hexdigest()
                     for relative in sources}
    if [name for name in before if copied_hashes[name] != before[name]] != [model.relative_to(root).as_posix()]:
        raise SystemExit("Isolated camera snapshot changed more than its intended production model.")
    return source_root

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
compile_failure_probes = []
try:
    baseline_assertions = check_success(run(root, "baseline"))
    for label, omitted, compile_guard, probe_name in (
        ("missing-selector", selector,
         "Actual frozen SourceAudioFunctions is a required pure Core VM dependency; no stub or omitted selector.",
         "focal-with-missing-real-selector"),
        ("missing-c2-selector", c2_selector,
         "Actual SourceC2SoundFunctions is a required pure Core VM dependency; no stub or omitted selector.",
         "focal-with-missing-real-c2-selector"),
    ):
        missing_selector_root = snapshot(label, mutations[0])
        (missing_selector_root / omitted.relative_to(root)).unlink()
        try:
            run(missing_selector_root, label)
        except RuntimeError as error:
            if compile_guard not in str(error) or "Compilation error is NOT a rejected camera assertion mutant:" not in str(error):
                raise
        else:
            raise SystemExit("Omitting the real selector was not rejected by the camera compile gate: " + label)
        compile_failure_probes.append({
            "name": probe_name,
            "omitted_source": omitted.relative_to(root).as_posix(),
            "build_exit_code": build_results[label],
            "required_diagnostic": compile_guard,
            "runtime_executed": any(c["label"] == label and c["phase"] == "execute" for c in commands),
            "accepted_as_camera_mutant_kill": False,
        })
        print("PASS guard: missing " + omitted.name + " is a compile failure, NOT a camera mutant kill.")
    for mutation in mutations:
        name, _, _, expected = mutation
        source_root = snapshot(name, mutation)
        result = run(source_root, name)
        detail = result.stdout + result.stderr
        if result.returncode != 1 or expected not in detail:
            raise SystemExit("Mutation did not fail its intended assertion: " + name + "\n" + detail)
        print("PASS mutation: " + name + " -> " + expected)
        mutation_results.append({"name": name, "rejected_by": expected, "build_exit_code": build_results[name],
                                 "execution_exit_code": result.returncode,
                                 "same_root_selector_sha256": hashlib.sha256(
                                     (source_root / selector.relative_to(root)).read_bytes()).hexdigest(),
                                 "same_root_c2_selector_sha256": hashlib.sha256(
                                     (source_root / c2_selector.relative_to(root)).read_bytes()).hexdigest()})
    final_assertions = check_success(run(root, "final-baseline"))
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
    "compile_failure_probe": compile_failure_probes[0],
    "compile_failure_probes": compile_failure_probes,
    "commands": commands,
    "production_sha256_before": before,
    "production_sha256_after": hashes(),
    "production_hashes_unchanged": True,
    "isolated_source_and_fixture_trees_removed": not workspace.exists(),
    "native_unity_checks": "not compiled or executed; UNITY_EDITOR is not defined and no Unity stubs are supplied",
}
report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print("PASS: six isolated real-model mutations rejected; production SHA-256 hashes unchanged.")
