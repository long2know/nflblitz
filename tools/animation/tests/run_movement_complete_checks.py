#!/usr/bin/env python3
"""Verify actual local movement157 data, independent oracles and isolated mutants."""

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OWNED = ROOT / ".squad" / ".scratch" / "source-rskip-export"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(args):
    output = args.output.resolve()
    if not output.is_relative_to(OWNED) or output == OWNED or output.exists():
        raise ValueError("checks output must be a new directory beneath owned source-rskip-export")
    output.mkdir(parents=True)
    spec = importlib.util.spec_from_file_location("original_c_oracle", HERE / "check_converter_replay.py")
    oracle = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(oracle)
    oracle_result = oracle.run(args.source_root / "anim/misc99.wan", args.converter_source,
                               args.bundle / "clips/R_SKIP.json", output / "original-c")
    env = dict(os.environ)
    for variable, suffix in (("TMPDIR", "work"), ("TMP", "work"), ("TEMP", "work"),
                             ("DOTNET_CLI_HOME", "dotnet-home"), ("NUGET_PACKAGES", "nuget")):
        path = OWNED / suffix
        path.mkdir(exist_ok=True)
        env[variable] = str(path)
    env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", PYTHONDONTWRITEBYTECODE="1")
    project = HERE / "MovementComplete.Checks.csproj"
    production_project = HERE / "MovementComplete.Production.csproj"
    files = []
    for item in ET.parse(production_project).getroot().iter("Compile"):
        pattern = item.attrib["Include"].replace("$(ProductionRoot)", "")
        files.extend(ROOT.glob(pattern))
    before = {path.relative_to(ROOT).as_posix(): digest(path) for path in files}
    arguments = [str(args.bundle.resolve()), str(args.selected_bundle.resolve()),
                 str((args.source_root / "anim/misc99.wan").resolve()),
                 str(output / "original-c/oracle.bin"), str(args.model.resolve()),
                 str(args.skeleton.resolve()), str(args.baseline56.resolve()), str(args.baseline117.resolve())]

    def checked(name, *, source_root=None, bundle=None):
        property_args = [] if source_root is None else ["-p:ProductionRoot=" + str(source_root) + "/"]
        selected_args = list(arguments)
        if bundle is not None:
            selected_args[0] = str(bundle)
        command = ["dotnet", "run", "--no-restore", "--project", str(project), "--configuration",
                   "Release", "--verbosity", "quiet", *property_args, "--", *selected_args]
        result = subprocess.run(command, cwd=ROOT, env=env, text=True, capture_output=True)
        if "NETSDK1004" in result.stdout + result.stderr:
            subprocess.run(["dotnet", "restore", str(project), "--verbosity", "quiet"],
                           cwd=ROOT, env=env, check=True, capture_output=True)
            result = subprocess.run(command, cwd=ROOT, env=env, text=True, capture_output=True)
        (output / (name + ".log")).write_text(result.stdout + result.stderr)
        return result

    def successful(result):
        if result.returncode:
            raise AssertionError(result.stdout + result.stderr)
        report = json.loads(result.stdout.strip().splitlines()[-1])
        assert report["clips"] == 157 and report["selectedFrames"] == 13490
        assert report["malformedCases"] == 14 and report["independentRawMatrixFrames"] == 70
        return report

    baseline = successful(checked("baseline"))
    mutations = [
        ("qualification-match", "SourceAnimationLibrary.cs",
         "expected.retained_data_offset == actual.retained_data_offset",
         "expected.retained_data_offset >= actual.retained_data_offset",
         "ASSERT inventory/document replay mismatch must be rejected"),
        ("qualification-audit", "SourceAnimationLibrary.cs",
         "actual.retained_data_offset == 2820422",
         "actual.retained_data_offset >= 2820422",
         "ASSERT changed retained-record metadata must be rejected"),
        ("raw-flag-preservation", "SourceAnimationLibrary.cs",
         "expectedDecodedSha256, sequence.animation_flags, sequence.source_frame_count",
         "expectedDecodedSha256, sequence.animation_flags | 1U, sequence.source_frame_count",
         "ASSERT typed raw selected/uncompressed flags"),
        ("pose-basis", "SourceAnimationSampling.cs",
         "var unity = new SourceRotation(-q.X, -q.Y, q.Z, q.W);",
         "var unity = new SourceRotation(q.X, q.Y, q.Z, q.W);",
         "ASSERT independent raw matrix/production actor pose"),
    ]
    rejected = []
    source_copies, artifact_copies = output / "source-mutants", output / "artifact-mutants"
    try:
        for name, file_name, anchor, replacement, expected in mutations:
            source_root = source_copies / name
            for original in files:
                copied = source_root / original.relative_to(ROOT)
                copied.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(original, copied)
            path = source_root / "Assets/Scripts/AssetImport" / file_name
            text = path.read_text()
            assert text.count(anchor) == 1, name + " unique real-source mutation anchor"
            path.write_text(text.replace(anchor, replacement))
            result = checked(name, source_root=source_root)
            combined = result.stdout + result.stderr
            assert result.returncode != 0 and expected in combined and "error CS" not in combined, name + combined
            rejected.append({"name": name, "rejected_by": expected, "build_failure": False})
        successful(checked("restore-primary"))
        for name in ("wrong-retained-pose", "wrong-root"):
            copied = artifact_copies / name
            shutil.copytree(args.bundle, copied)
            clip_path = copied / "clips/R_SKIP.json"
            clip = json.loads(clip_path.read_bytes())
            if name == "wrong-retained-pose":
                clip["frames"][0]["rotations_xyzw"][4] = clip["frames"][1]["rotations_xyzw"][4]
                expected = "ASSERT original C quaternion"
            else:
                root = clip["frames"][35]["root_translation"]
                root["authoring_xyz"][0] += 1.
                root["scaled_runtime_xyz"][0] = root["authoring_xyz"][0] * .12898
                expected = "ASSERT C raw root X"
            clip_path.write_text(json.dumps(clip, separators=(",", ":")) + "\n")
            manifest_path = copied / "export-inventory.json"
            manifest = json.loads(manifest_path.read_bytes())
            next(entry for entry in manifest["clips"] if entry["runtime_name"] == "R_SKIP")["sha256"] = digest(clip_path)
            manifest_path.write_text(json.dumps(manifest, separators=(",", ":")) + "\n")
            result = checked(name, bundle=copied)
            combined = result.stdout + result.stderr
            assert result.returncode != 0 and expected in combined and "error CS" not in combined, name + combined
            rejected.append({"name": name, "rejected_by": expected, "build_failure": False})
        final = successful(checked("final-baseline"))
    finally:
        for path in (source_copies, artifact_copies):
            if path.exists():
                shutil.rmtree(path)
        assert before == {path.relative_to(ROOT).as_posix(): digest(path) for path in files}, \
            "production files changed during isolated checks"
    report = {
        "schema": "source-rskip-movement-complete-checks/v1", "baseline": baseline,
        "final_baseline": final, "original_C_oracle": oracle_result,
        "isolated_mutants": rejected, "production_sha256": before,
        "production_unchanged_during_checks": True, "isolated_copies_removed": True,
        "native_editor_browser_or_full_game_acceptance": False,
    }
    (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("bundle", "selected-bundle", "source-root", "converter-source",
                 "model", "skeleton", "baseline56", "baseline117", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    options = parser.parse_args()
    result = run(options)
    print(json.dumps({"assertions": result["baseline"]["assertions"],
                      "mutants_rejected": len(result["isolated_mutants"]),
                      "report": str(options.output / "result.json")}, sort_keys=True))
