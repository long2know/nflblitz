#!/usr/bin/env python3
"""Isolated mutations of actual converter/reader code; production files never change."""

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import uuid

import prepare_environment as env


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle-root", required=True, type=Path)
    args = parser.parse_args()
    project = Path(__file__).absolute().parents[2]
    work = project / ".squad/.scratch" / ("environment-mutations-" + uuid.uuid4().hex)
    original = (project / "tools/environments/prepare_environment.py").read_text()
    reader = (project / "Assets/Scripts/AssetImport/SourceEnvironmentData.cs").read_text()
    mutations = [
        ("SDL-axis-scale", original, "scale=(65.0, 65.0, 57.5)", "scale=(65.0, 65.0, 65.0)", "python"),
        ("SDL-to-Unity-UV-flip", original, "f32(1.0 - generated_st[1])", "generated_st[1]", "python"),
        ("material-slot-base", original, "slot = slot_base + scene.slots.index(texture_name)",
         "slot = scene.slots.index(texture_name)", "python"),
        ("explicit-source-pin", original, "source_commit == SOURCE_COMMIT and recipe[\"sourceCommit\"] == source_commit",
         "True", "python"),
        ("ARGB1555-alpha-bit", original, "a = 255 if word & 0x8000 else 0", "a = 255 if word & 1 else 0", "python"),
        ("visibility-volumes-not-art", original,
         '"zone-volume" if scene.name == "stadpie"', '"render" if scene.name == "stadpie"', "python"),
        ("reader-SHA256", reader, "AuthorizedAssetRoot.Sha256(bytes) == expected", "true", "csharp"),
        ("reader-index-range", reader, "Require(index >= 0 && index < count,", "Require(index >= 0,", "csharp"),
        ("reader-bounds", reader, "Require(Equal(bounds, info.boundsUnity),", "Require(true,", "csharp"),
        ("reader-native-clamp-contract", reader, 'state.clampMode == "GR_TEXTURECLAMP_CLAMP"',
         '(state.clampMode == "GR_TEXTURECLAMP_CLAMP" || state.clampMode == "GR_TEXTURECLAMP_WRAP")', "csharp"),
        ("reader-bound-WRAP-restore", reader, 'Require(info.samplingWrap == "GR_TEXTURECLAMP_WRAP",',
         'Require(true,', "csharp"),
    ]
    files = {}
    for name, code, before, after, language in mutations:
        env.require(code.count(before) == 1, "Mutation target is not unique: " + name)
        files[name + (".py" if language == "python" else ".cs")] = code.replace(before, after).encode()
    env.write_bundle(project, work, files)
    results = []
    for name, _, _, _, language in mutations:
        if language == "python":
            environment = dict(os.environ, BLITZ_ENVIRONMENT_CONVERTER=str(work / (name + ".py")),
                               PYTHONDONTWRITEBYTECODE="1")
            command = [sys.executable, "-B", str(project / "tools/environments/test_prepare_environment.py"), "-q"]
        else:
            environment = dict(os.environ)
            command = [sys.executable, "-B", str(project / "tools/environments/run_reader_checks.py"),
                       "--bundle-root", str(args.bundle_root.absolute()), "--reader-source", str(work / (name + ".cs"))]
        process = subprocess.run(command, env=environment, capture_output=True, text=True)
        output = process.stdout + process.stderr
        (work / (name + ".output.txt")).write_text(output)
        caught = process.returncode != 0 and ("Assertion" in output or "Invalid converted environment was accepted" in output
                                              or "FAIL:" in output or "EnvironmentError: Missing original draw-zone volume" in output)
        results.append({"mutation": name, "language": language, "caughtByContractAssertions": caught,
                        "exitCode": process.returncode})
        print(("PASS rejected: " if caught else "FAIL survived/no contract assertion: ") + name)
    receipt = {"status": "PASS" if all(r["caughtByContractAssertions"] for r in results) else "FAIL",
               "mutations": results, "productionConverterSha256": env.digest(original.encode()),
               "productionReaderSha256": env.digest(reader.encode())}
    (work / "mutation-receipt.json").write_bytes(env.json_bytes(receipt))
    env.require((project / "tools/environments/prepare_environment.py").read_text() == original
                and (project / "Assets/Scripts/AssetImport/SourceEnvironmentData.cs").read_text() == reader,
                "Production code changed during isolated mutation validation.")
    print(json.dumps({"status": receipt["status"], "caught": sum(r["caughtByContractAssertions"] for r in results),
                      "total": len(results), "receipt": str((work / "mutation-receipt.json").relative_to(project))}))
    return 0 if receipt["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
