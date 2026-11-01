#!/usr/bin/env python3
"""Validate one immutable original303 AppBundle and stage only its image inputs."""

import argparse
import base64
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil


HERE = Path(__file__).resolve().parent
PROFILE = "original-teams-complete-original303-prematch"
FACTORY = "SourceC2Composition.CreateCompleteOriginalPrematchSimulation"
MANIFEST_SHA = "309ca9cf4f7f0d3b9f8b43427ab581fda4257d072ea00bfdb4ccc0003c2cba4f"
TOP_FILES = {
    "index.html", "main.js", "input.js", "renderer.js", "audio.js", "records.js",
    "build-info.json", "package.json", "SourcePlayer.Browser.runtimeconfig.json",
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def relative(value):
    require(isinstance(value, str) and re.fullmatch(r"[A-Za-z0-9_./-]+", value) and
            all(part not in ("", ".", "..") for part in value.split("/")) and
            not PurePosixPath(value).is_absolute(), "Unsafe bundle path: " + repr(value))
    return value


def inventory(root):
    require(root.is_dir() and not root.is_symlink(), "AppBundle must be a real directory.")
    records = []
    aliases = set()
    for path in sorted(root.rglob("*")):
        name = path.relative_to(root).as_posix()
        require(not path.is_symlink(), "Symlinked bundle input: " + name)
        if name == ".stamp":
            require(path.is_file(), "Unexpected SDK stamp directory.")
            continue
        relative(name)
        require(not any(part.startswith(".") for part in name.split("/")),
                "Hidden bundle input: " + name)
        require(name.lower() not in aliases, "Aliased bundle input: " + name)
        aliases.add(name.lower())
        require(path.is_file() or path.is_dir(), "Nonregular bundle input: " + name)
        if path.is_file():
            records.append({"path": name, "bytes": path.stat().st_size, "sha256": sha(path)})
    return records


def inspect_bundle(root, expected_build_sha, expected_source_digest):
    records = inventory(root)
    pins = {entry["path"]: entry for entry in records}
    require({p.name for p in root.iterdir()} - {".stamp"} == TOP_FILES | {"data", "_framework"},
            "AppBundle top-level inventory changed; no blank/default or unrelated input may be packaged.")
    require(TOP_FILES <= pins.keys(), "Required browser modules/build receipt are missing.")
    require(pins["build-info.json"]["sha256"] == expected_build_sha, "build-info.json identity changed.")
    build = json.loads((root / "build-info.json").read_bytes())
    require(build["schema"] == "blitz-source-player-build/v1" and
            build["runtimeIdentifier"] == "browser-wasm" and
            build["mainAssembly"] == "SourcePlayer.Browser" and build["nativeUnityBuild"] is False,
            "A real portable browser build receipt is required.")
    digest = hashlib.sha256(json.dumps(build["sources"], sort_keys=True).encode()).hexdigest()
    require(build["sourceRootDigest"] == digest == expected_source_digest,
            "Compiled source inventory/digest differs from the accepted bundle.")
    require(build["profile"] == PROFILE and build["factory"] == FACTORY and
            build["dataManifestSha256"] == MANIFEST_SHA == pins["data/manifest.json"]["sha256"],
            "The complete original963 profile/factory/manifest is required; no historical fallback.")
    manifest = json.loads((root / "data/manifest.json").read_bytes())
    require(manifest["schema"] == "blitz-portable-source-player/v1" and
            manifest["profile"] == PROFILE and manifest["factory"] == FACTORY,
            "Incorrect original-data composition.")
    files = manifest["files"]
    require(len(files) == 963 and sum(entry["bytes"] for entry in files) == 244325118,
            "The exact963-file/244325118-byte original payload is required.")
    expected_data = {"data/manifest.json"}
    for entry in files:
        name = "data/" + relative(entry["path"])
        require(name not in expected_data, "Duplicate data pin: " + name)
        expected_data.add(name)
        require(pins.get(name) == dict(entry, path=name), "Missing/changed original payload: " + name)
    require({name for name in pins if name.startswith("data/")} == expected_data,
            "Data payload has extra or missing files.")
    assemblies = {"_framework/SourcePlayer.Core.wasm", "_framework/SourcePlayer.Browser.wasm"}
    require({entry["path"] for entry in build["assemblies"]} == assemblies,
            "Actual compiled Core and Browser Webcil assemblies are required.")
    for entry in build["assemblies"]:
        require(pins.get(entry["path"]) == entry, "Compiled assembly identity changed: " + entry["path"])
    boot = (root / "_framework/dotnet.boot.js").read_text()
    require("/*json-start*/" in boot, "Actual .NET browser boot resource configuration is missing.")
    config, _ = json.JSONDecoder().raw_decode(boot.split("/*json-start*/", 1)[1])
    require(config["mainAssemblyName"] == "SourcePlayer.Browser.dll", "Incorrect .NET main assembly.")
    for group, resources in config["resources"].items():
        if group == "hash":
            continue
        require(isinstance(resources, list), "Unknown .NET resource group: " + group)
        for resource in resources:
            name = "_framework/" + relative(resource["name"])
            require(name in pins, "Missing .NET boot resource: " + name)
            if "integrity" in resource:
                integrity = "sha256-" + base64.b64encode(bytes.fromhex(pins[name]["sha256"])).decode()
                require(resource["integrity"] == integrity, "Changed .NET boot resource: " + name)
    for name in ("dotnet.js", "dotnet.runtime.js", "dotnet.native.js", "dotnet.native.wasm"):
        require("_framework/" + name in pins, "Missing .NET runtime: " + name)
    for name in assemblies | {"_framework/dotnet.native.wasm"}:
        with (root / name).open("rb") as stream:
            require(stream.read(4) == b"\0asm", "Not actual WebAssembly: " + name)
    source_modules = {entry["path"].split("/")[-1]: entry["sha256"] for entry in build["sources"]
                      if entry["path"].startswith("tools/source-player/wwwroot/")}
    for name in ("index.html", "main.js", "input.js", "renderer.js", "audio.js", "records.js"):
        require(source_modules.get(name) == pins[name]["sha256"], "Emitted module differs from source receipt: " + name)
    return build, records


def stage_bundle(root, context, build_sha, source_digest, source_revision, preflight=False):
    build, records = inspect_bundle(root, build_sha, source_digest)
    require(re.fullmatch(r"[0-9a-f]{40}", source_revision), "An exact source-base Git commit is required.")
    require(not context.exists() and not context.is_symlink(), "Refusing an existing build context: " + str(context))
    context.mkdir(parents=True)
    for entry in records:
        target = context / "AppBundle" / entry["path"]
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(root / entry["path"], target)
        require(sha(target) == entry["sha256"], "Bundle changed during staging: " + entry["path"])
        target.chmod(0o444)
    for name in ("Dockerfile", "nginx.conf", ".dockerignore"):
        shutil.copyfile(HERE / name, context / name)
    receipt = {
        "schema": "nflblitz-container-bundle/v1",
        "qualification": "static-server-preflight-only" if preflight else "awaiting-exact-image-browser-acceptance",
        "sourceBaseCommit": source_revision,
        "compiledSourceDigest": source_digest,
        "compiledSourceFiles": len(build["sources"]),
        "originalSourceRevision": json.loads((root / "data/manifest.json").read_bytes())["sourceRevision"],
        "buildInfoSha256": build_sha,
        "dataManifestSha256": MANIFEST_SHA,
        "profile": PROFILE,
        "factory": FACTORY,
        "payloadFiles": 963,
        "payloadBytes": 244325118,
        "bundleFiles": len(records),
        "bundleBytes": sum(entry["bytes"] for entry in records),
        "bundleInventorySha256": hashlib.sha256(json.dumps(records, sort_keys=True, separators=(",", ":")).encode()).hexdigest(),
        "files": records,
    }
    (context / "bundle-inventory.json").write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n")
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--context", type=Path, required=True, help="New, owned ignored output directory.")
    parser.add_argument("--build-sha256", required=True)
    parser.add_argument("--source-digest", required=True)
    parser.add_argument("--source-revision", required=True)
    parser.add_argument("--preflight-only", action="store_true")
    args = parser.parse_args()
    try:
        receipt = stage_bundle(args.bundle, args.context, args.build_sha256, args.source_digest,
                               args.source_revision, args.preflight_only)
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(1, str(error) + "\n")
    print(json.dumps({key: value for key, value in receipt.items() if key != "files"}, indent=2))


if __name__ == "__main__":
    main()
