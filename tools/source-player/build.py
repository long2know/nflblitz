#!/usr/bin/env python3
"""Publish the actual .NET browser app and copy an explicit ignored original-data bundle."""

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET
from prepare import portable, read, require


HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[1]
OLD_PROFILE = "original-teams-contact281-c2-m1"
PROFILE = "original-teams-contact282-c2-m1-presnap-switch"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def source_inventory(root):
    sources = set()
    for group in ("Contracts", "Gameplay", "AssetImport"):
        sources.update((root / "Assets/Scripts" / group).glob("*.cs"))
    props = ET.parse(HERE / "ProductionSources.props").getroot()
    for item in props.iter("Compile"):
        name = item.attrib["Include"].replace("$(BlitzSourceRoot)", "")
        if "*" not in name:
            sources.add(root / name)
    records = [{"path": str(path.relative_to(root)), "sha256": sha(path), "origin": "production"}
               for path in sorted(sources)]
    for path in sorted(list((HERE / "Runtime").glob("*.cs")) + list((HERE / "wwwroot").glob("*")) +
                       list(HERE.glob("*.props")) + list(HERE.glob("*.csproj")) + [HERE / "Browser.cs"]):
        records.append({"path": str(path.relative_to(PROJECT)), "sha256": sha(path), "origin": "preview"})
    return records


def verify_private_inputs(data, manifest):
    expected = {"manifest.json"}
    aliases = {"manifest.json"}
    for entry in manifest["files"]:
        relative = portable(entry["path"])
        require(relative.lower() not in aliases, "Duplicate/aliased private input: " + relative)
        aliases.add(relative.lower())
        expected.add(relative)
        raw = read(data, relative, entry["sha256"])
        require(len(raw) == entry["bytes"], "Explicit private input byte count changed: " + relative)
    actual = set()
    for path in data.rglob("*"):
        require(not path.is_symlink(), "Symlinked private input: " + path.relative_to(data).as_posix())
        if path.is_file():
            actual.add(path.relative_to(data).as_posix())
    require(actual == expected, "Private input inventory differs; unexpected=" +
            repr(sorted(actual - expected)) + ", missing=" + repr(sorted(expected - actual)))
    if manifest["profile"] == PROFILE:
        require(len(manifest["files"]) == 563, "Pre-snap switch requires the exact563-file successor.")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", type=Path, required=True, help="Explicit pinned private data output from prepare.py.")
    parser.add_argument("--profile", choices=(OLD_PROFILE, PROFILE), default=PROFILE,
                        help="New enabled default; historical M1 data requires its explicit old stable ID.")
    parser.add_argument("--source-root", type=Path, default=PROJECT,
                        help="Immutable accepted production source root (BlitzSourceRoot override).")
    parser.add_argument("--source-handoff", type=Path,
                        help="Required frozen Core handoff when using an external production source root.")
    parser.add_argument("--source-handoff-sha256")
    args = parser.parse_args(argv)
    data = args.data.resolve()
    if not data.is_dir() or not (data / "manifest.json").is_file():
        parser.error("A real complete private data bundle is required; no assets are fetched by build.")
    manifest = json.loads((data / "manifest.json").read_bytes())
    if manifest.get("schema") != "blitz-portable-source-player/v1":
        parser.error("A complete prepared source-player manifest is required.")
    if manifest.get("profile") != args.profile:
        parser.error("Prepared manifest.profile differs from --profile; missing new data never falls back to historical M1.")
    try:
        verify_private_inputs(data, manifest)
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.error(str(error))
    root = args.source_root.resolve()
    if "enum SourceGameplayCompatibility" not in (root / "Assets/Scripts/Contracts/SourceMatchContracts.cs").read_text():
        parser.error("The accepted portable production Core API is required. Integrate that dependency or supply --source-root pointing to its immutable accepted snapshot; strict-source fallback is forbidden.")
    before = source_inventory(root)
    handoff_hash = None
    if root != PROJECT:
        if args.source_handoff is None or args.source_handoff_sha256 is None:
            parser.error("An external immutable source root requires its explicit --source-handoff and --source-handoff-sha256.")
        handoff_hash = sha(args.source_handoff)
        if handoff_hash != args.source_handoff_sha256:
            parser.error("The accepted frozen source handoff SHA256 changed.")
        frozen = json.loads(args.source_handoff.read_bytes())
        if frozen.get("schema") != "source-portable-m1-handoff/v1" or frozen.get("code_writes_frozen") is not True:
            parser.error("External Core source is not an explicitly frozen portable handoff.")
        production = [record for record in before if record["origin"] == "production"]
        if any(frozen["current_all113_source_sha256"].get(record["path"]) != record["sha256"] for record in production):
            parser.error("The actual compiled production closure differs from the accepted frozen Core source hashes.")
    subprocess.run(["bash", str(HERE / "dotnet.sh"), "publish", str(HERE / "SourcePlayer.Browser.csproj"),
                    "-c", "Release", "--nologo", "-v", "minimal", "-p:BlitzSourceRoot=" + str(root) + "/"],
                   cwd=PROJECT, check=True)
    after = source_inventory(root)
    if before != after:
        parser.error("Production or preview sources changed during publish; no build receipt was emitted.")
    output = HERE / "bin/SourcePlayer.Browser/Release/net10.0/browser-wasm/AppBundle"
    for relative in ["manifest.json"] + [entry["path"] for entry in manifest["files"]]:
        target = output / "data" / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.parent.resolve() != target.parent or target.is_symlink():
            parser.error("Refusing symlinked private publish output.")
        shutil.copyfile(data / relative, target)
        if sha(target) != sha(data / relative):
            parser.error("Private source input changed while being copied: " + relative)
    framework = output / "_framework"
    assemblies = [p for p in framework.iterdir() if p.name.startswith("SourcePlayer.") and p.suffix == ".wasm"]
    if len(assemblies) != 2 or not any(p.name.startswith("SourcePlayer.Core") for p in assemblies):
        parser.error("Actual C# Core and Browser Webcil assemblies were not emitted.")
    sdk = subprocess.check_output(["bash", str(HERE / "dotnet.sh"), "--version"], cwd=PROJECT, text=True).strip()
    receipt = {
        "schema": "blitz-source-player-build/v1",
        "runtimeFlavor": "portable C# browser preview (not a Unity player build)",
        "sdkVersion": sdk,
        "mainAssembly": "SourcePlayer.Browser",
        "sourceRootDigest": hashlib.sha256(json.dumps(before, sort_keys=True).encode()).hexdigest(),
        "sources": before,
        "dataManifestSha256": sha(data / "manifest.json"),
        "profile": args.profile,
        "factory": "SourceC2Composition.CreatePreSnapSwitchSimulation" if args.profile == PROFILE else
                   "SourceC2Composition.CreateM1Simulation",
        "acceptedSourceHandoffSha256": handoff_hash,
        "assemblies": [{"path": "_framework/" + p.name, "bytes": p.stat().st_size, "sha256": sha(p)}
                       for p in sorted(assemblies)],
        "runtimeIdentifier": "browser-wasm",
        "unsafePermission": "JSImport/JSExport generated marshalling only; no original C or unsafe memory execution",
        "nativeUnityBuild": False,
    }
    (output / "build-info.json").write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n")
    print(json.dumps({"output": str(output), "receiptSha256": sha(output / "build-info.json"),
                      "dataManifestSha256": receipt["dataManifestSha256"], "sourceFiles": len(before)}, indent=2))


if __name__ == "__main__":
    main()
