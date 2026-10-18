#!/usr/bin/env python3
"""Run the real packaging contract against isolated mutations of NEW production sources."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[3]
SUITE = Path(__file__).resolve().parent
ARTIFACTS = SUITE / ".artifacts" / "mutations"
PACKAGE = "Assets/Scripts/AssetImport/SourceTeamInterfacePackage.cs"
CHECKS = "Assets/Scripts/Editor/SourceTeamInterfaceChecks.cs"
IMPORTER = "Assets/Scripts/Editor/SourceTeamInterfaceImporter.cs"
MUTANTS = (
    ("designated-home-defaulted", PACKAGE, '(TeamId)presentation.Integer("designated_home_side", 0, 1)',
     '(TeamId)(0 * presentation.Integer("designated_home_side", 0, 1))'),
    ("actual-station-heads-lost", PACKAGE, 'presentation.Integers("station_heads", 4, -1, 19)',
     'Array.ConvertAll(presentation.Integers("station_heads", 4, -1, 19), head => 0)'),
    ("whole-pup-bool-normalized", PACKAGE, 'presentation.Integer("pup_bigball", int.MinValue, int.MaxValue)',
     'Math.Sign(presentation.Integer("pup_bigball", int.MinValue, int.MaxValue))'),
    ("original-profile-becomes-legacy", PACKAGE, '(SourceSimulationProfile)root.Integer("profile", 1, 1)',
     '(SourceSimulationProfile)(1 - root.Integer("profile", 1, 1))'),
    ("captain-defaulted", PACKAGE, '(SourceCaptainSlot)root.Integer("captain", 0, 1)',
     '(SourceCaptainSlot)(0 * root.Integer("captain", 0, 1))'),
    ("weighted-original-ordinal-discarded", PACKAGE, 'play.Integer("pool_index", -1, int.MaxValue)',
     '0 * play.Integer("pool_index", -1, int.MaxValue)'),
    ("configuration-pin-bypassed", PACKAGE,
     'IsHash(expectedHash) && AuthorizedAssetRoot.Sha256(bytes) == expectedHash', 'IsHash(expectedHash)'),
    ("P1-file-content-pin-bypassed", PACKAGE,
     'Require(AuthorizedAssetRoot.Sha256(book) == expectedPlaybookHash, "Original P1 catalog content pin mismatch.");', ''),
    ("actual-atlas-check-caller-bypassed", PACKAGE, 'byte[] verified = hud.RequireTextureBytes(pair.Key, rgba);',
     'byte[] verified = rgba;'),
    ("actual-shader-pin-bypassed", PACKAGE,
     'Require(AuthorizedAssetRoot.Sha256(shader) == HudShaderSha256, "Actual original HUD shader source pin mismatch.");',
     'Require(shader.Length > 0, "Shader nonempty only.");'),
    ("texture-byte-alias-leaked", PACKAGE, 'public byte[] CopyBytes() { return (byte[])bytes.Clone(); }',
     'public byte[] CopyBytes() { return bytes; }'),
    ("configuration-byte-alias-leaked", PACKAGE, 'public byte[] CopyConfiguration() { return (byte[])configuration.Clone(); }',
     'public byte[] CopyConfiguration() { return configuration; }'),
    ("wrong-emitted-playbook-bytes", PACKAGE, 'public byte[] CopyPlaybook() { return (byte[])playbook.Clone(); }',
     'public byte[] CopyPlaybook() { return (byte[])manifest.Clone(); }'),
    ("unignored-resource-output-admitted", PACKAGE,
     'assetPath.StartsWith("Assets/Imported/", StringComparison.Ordinal)',
     'assetPath.StartsWith("Assets/", StringComparison.Ordinal)'),
    ("wrong-resource-key-admitted", PACKAGE,
     'assetPath.EndsWith("/Resources/" + resourceKey + ".asset", StringComparison.Ordinal)',
     'assetPath.EndsWith(".asset", StringComparison.Ordinal)'),
    ("saved-pup-not-verified", CHECKS,
     'expected.Presentation.PupBigBall == presentation.PupBigBall &&', ''),
    ("saved-difficulty-not-verified", CHECKS, 'match.Difficulty == actual.Difficulty', 'true'),
    ("saved-roster-bindings-not-verified", CHECKS, 'a.RosterIndices.SequenceEqual(b.RosterIndices)', 'true'),
)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("b1-root", "ui-root", "team-root", "hud-root", "playbook-root"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    roots = [getattr(args, name).resolve(strict=True) for name in
             ("b1_root", "ui_root", "team_root", "hud_root", "playbook_root")]
    if roots[0] == ROOT:
        raise SystemExit("No Primary hot M1 input; select actual protected B1.")
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    production = {name: (ROOT / name).read_bytes() for name in (
        PACKAGE, IMPORTER, CHECKS,
        "Assets/Scripts/Presentation/SourceAudioFunctions.cs",
        "Assets/Scripts/Presentation/SourceC2SoundFunctions.cs",
    )}
    results = []
    work = ARTIFACTS / "isolated-new-source"
    try:
        work.mkdir()
        env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                   DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
        for label, name, before, after in MUTANTS:
            source = production[name].decode()
            assert source.count(before) == 1, ("Mutation no longer selects one actual production site", label)
            changed = source.replace(before, after, 1).encode()
            for filename, content in production.items():
                target = work / filename
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(changed if filename == name else content)
            command = ["dotnet", "run", "--project", str(SUITE / "PackagingChecks.csproj"),
                       "--configuration", "Release", "--verbosity", "quiet",
                       "-p:BlitzB1Root=" + str(roots[0]) + "/", "-p:BlitzUiRoot=" + str(roots[1]) + "/",
                       "-p:BlitzTeamRoot=" + str(roots[2]) + "/", "-p:BlitzPackagingRoot=" + str(work) + "/", "--",
                       str(roots[3]), str(roots[4]), str(work / "case-inputs"), str(roots[1])]
            process = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
            output = process.stdout + process.stderr
            assert not recompile_failure(output), ("Invalid compile-only mutant", label, output)
            assert process.returncode != 0, ("Actual production regression escaped contract", label)
            assert "FAIL packaging:" in output, ("Mutant did not fail an artifact/field assertion", label, output)
            (ARTIFACTS / (label + ".log")).write_text(output)
            results.append({"name": label, "source": name, "actual_mutated_source_sha256": digest(changed),
                            "rejected": True, "assertion": next(line for line in output.splitlines() if "FAIL packaging:" in line)})
            print("REJECTED " + label)
        assert all((ROOT / name).read_bytes() == content for name, content in production.items())
        print("PASS " + str(len(results)) + " actual NEW production packaging mutants rejected; no protected source edited")
        return 0
    finally:
        (ARTIFACTS / "result.json").write_text(json.dumps({
            "schema": "source-interface-package-mutants/v1", "mutants": results,
            "expected": len(MUTANTS), "complete": len(results) == len(MUTANTS),
            "actual_production_sha256": {name: digest(content) for name, content in production.items()},
            "protected_B1_UI_Team_modified": False, "Unity_stubs": False, "Editor_executed": False,
        }, indent=2) + "\n")
        if work.exists():
            shutil.rmtree(work)


def recompile_failure(output):
    return "error CS" in output or "The build failed" in output or "error MSB" in output


if __name__ == "__main__":
    raise SystemExit(main())
