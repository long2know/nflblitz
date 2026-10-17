#!/usr/bin/env python3
"""Compile the actual pure production closure against an explicit real .NET Standard 2.1 reference."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import uuid
import sys


ROOT = Path(__file__).resolve().parents[2]
ARTIFACTS = Path(__file__).resolve().parent / ".artifacts"
sys.path.insert(0, str(ROOT / "tests/parity"))
from kernel_sources import production_paths


def source_hashes():
    return {path.relative_to(ROOT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in production_paths()}


def compiler_path():
    version = subprocess.run(["dotnet", "--version"], cwd=ROOT, capture_output=True,
                             text=True, check=True).stdout.strip()
    rows = subprocess.run(["dotnet", "--list-sdks"], cwd=ROOT, capture_output=True,
                          text=True, check=True).stdout.splitlines()
    for row in rows:
        if row.startswith(version + " ["):
            return Path(row.split("[", 1)[1].rstrip("]")) / version / "Roslyn/bincore/csc.dll"
    raise RuntimeError("Selected SDK compiler was not found in dotnet --list-sdks.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference", type=Path, required=True, help="Actual Unity/SDK netstandard2.1 netstandard.dll, never a shim.")
    parser.add_argument("--expected-reference-sha256")
    args = parser.parse_args()
    reference = args.reference.resolve()
    reference_hash = hashlib.sha256(reference.read_bytes()).hexdigest()
    if args.expected_reference_sha256 and reference_hash != args.expected_reference_sha256:
        parser.error("Actual .NET Standard reference content pin differs.")
    before = source_hashes()
    if not all(any("/" + name + "/" in path for path in before) for name in ("Contracts", "Gameplay", "AssetImport")):
        raise RuntimeError("Actual production compile inventories must be nonempty.")
    ARTIFACTS.mkdir(exist_ok=True)
    work = ARTIFACTS / ("profile-" + uuid.uuid4().hex)
    work.mkdir()
    output = work / "Blitz.SourceBall.netstandard21.dll"
    command = ["dotnet", str(compiler_path()), "-nologo", "-noconfig", "-nostdlib+",
               "-target:library", "-langversion:7.3", "-warnaserror+", "-reference:" + str(reference),
               "-out:" + str(output), *[str(ROOT / name) for name in before]]
    evidence = {"status": "failed", "command": command, "actual_reference": str(reference),
                "reference_sha256": reference_hash, "production_sha256_before": before,
                "unity_editor_executed": False, "full_gameplay_parity_claimed": False}
    try:
        process = subprocess.run(command, cwd=ROOT, capture_output=True, text=True,
                                 env=dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work)), timeout=180)
        evidence.update(exit_code=process.returncode, diagnostics=process.stdout + process.stderr)
        if process.returncode == 0:
            evidence["assembly_sha256"] = hashlib.sha256(output.read_bytes()).hexdigest()
            evidence["status"] = "passed"
    finally:
        evidence["production_sha256_after"] = source_hashes()
        evidence["production_hashes_unchanged"] = before == evidence["production_sha256_after"]
        if not evidence["production_hashes_unchanged"]:
            evidence["status"] = "inconclusive-sources-changed"
        shutil.rmtree(work)
        evidence["own_build_removed"] = True
        (ARTIFACTS / "profile-result.json").write_text(json.dumps(evidence, indent=2) + "\n")
    print(evidence["diagnostics"])
    print("Actual C#7.3 / .NET Standard2.1 profile:", evidence["status"], "/", len(before), "production sources")
    return 0 if evidence["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
