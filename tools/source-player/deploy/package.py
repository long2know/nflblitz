#!/usr/bin/env python3
"""Validate one separately pinned original AppBundle and stage only its image inputs."""

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
SELECTED_PROFILE = "original-teams-complete-original303-selected-world-v2"
SELECTED_MANIFEST_SHA = "aeb2efdebdbe260f63f6d785c461048dc9cec337098490761c2815cd8ffc4edd"
CALLER_PROFILE = "aot-rooted-bounded-caller-drain/v1"
AV306_PROFILES = {
    "original-teams-complete-av306": {
        "factory": "SourceC2Composition.CreateCompleteAv306Simulation",
        "manifest": "51e94caffcf5875e024206eb069dfd905ee13e134b09e7f5020ff68349461e5d",
        "files": 1153,
        "bytes": 300324289,
        "caller": "aot-rooted-bounded-caller-drain-av306/v1",
    },
    "original-teams-complete-av306-two-station-records-v1": {
        "factory": "SourceC2Composition.CreateCompleteAv306TwoStationRecordsSimulation",
        "manifest": "745129381c326e9f237ac4e6c3e020503553aada3e1f993dfb9321c09ee8f32d",
        "files": 1154,
        "bytes": 300324845,
        "caller": "aot-rooted-bounded-caller-drain-av306-two-station-records/v1",
    },
}
TOP_FILES = {
    "index.html", "main.js", "input.js", "renderer.js", "audio.js", "records.js",
    "build-info.json", "package.json", "SourcePlayer.Browser.runtimeconfig.json",
}
DEPLOYMENT_FILES = {
    "Dockerfile": None,
    "nginx.conf": "/etc/nginx/nginx.conf",
    "entrypoint.sh": "/opt/nflblitz/deploy/entrypoint.sh",
    "healthcheck.sh": "/opt/nflblitz/deploy/healthcheck.sh",
    "runtime-common.sh": "/opt/nflblitz/deploy/runtime-common.sh",
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
    selected = build["profile"] == SELECTED_PROFILE
    av306 = AV306_PROFILES.get(build["profile"])
    expected_profile = build["profile"] if av306 else SELECTED_PROFILE if selected else PROFILE
    expected_factory = av306["factory"] if av306 else FACTORY
    expected_manifest = av306["manifest"] if av306 else SELECTED_MANIFEST_SHA if selected else MANIFEST_SHA
    require(build["profile"] == expected_profile and build["factory"] == expected_factory and
            build["dataManifestSha256"] == expected_manifest == pins["data/manifest.json"]["sha256"],
            "The exact separately pinned original963, caller1114, AV3061153 or namedTwo1154 profile/factory/manifest is required.")
    if selected or av306:
        execution = build.get("execution", {})
        expected_caller = av306["caller"] if av306 else CALLER_PROFILE
        require(execution.get("mode") == "aot" and execution.get("runAOTCompilation") is True and
                execution.get("publishTrimmed") is True and execution.get("jsonReflectionEnabled") is True and
                execution.get("callerProfile") == expected_caller and
                pins.get("_framework/dotnet.native.wasm", {}).get("sha256") == execution.get("nativeRuntimeSha256"),
                "The selected original container requires its exact actual rooted-AOT caller-drain execution profile.")
    manifest = json.loads((root / "data/manifest.json").read_bytes())
    expected_schema = "blitz-portable-source-player/v3" if av306 else (
        "blitz-portable-source-player/v2" if selected else "blitz-portable-source-player/v1")
    require(manifest["schema"] == expected_schema and
            manifest["profile"] == build["profile"] and manifest["factory"] == expected_factory,
            "Incorrect original-data composition.")
    files = manifest["files"]
    expected_files = av306["files"] if av306 else 1114 if selected else 963
    expected_bytes = av306["bytes"] if av306 else 262649366 if selected else 244325118
    require(len(files) == expected_files and sum(entry["bytes"] for entry in files) == expected_bytes,
            "The exact separately pinned original payload count/bytes are required.")
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


def stage_deployment(context):
    for name in (*DEPLOYMENT_FILES, ".dockerignore"):
        require((HERE / name).is_file() and not (HERE / name).is_symlink(),
                "A regular deployment input is required: " + name)
        shutil.copyfile(HERE / name, context / name)
        (context / name).chmod(0o555 if name.endswith(".sh") else 0o444)
        require(sha(context / name) == sha(HERE / name), "Deployment input changed during staging: " + name)
    deployment = {
        "schema": "nflblitz-runtime-base-url/v1",
        "files": [{"path": name, "imagePath": image_path, "bytes": (context / name).stat().st_size,
                   "sha256": sha(context / name), "mode": "0555" if name.endswith(".sh") else "0444"}
                  for name, image_path in sorted(DEPLOYMENT_FILES.items())],
        "defaultBaseUrl": "/",
        "generatedConfig": "/tmp/nflblitz-${uid}/nginx.conf",
        "runtimeWritablePath": "/tmp",
        "runtimeDirectoryMode": "0700",
        "readOnlyRootRequired": False,
        "requiredRuntimeMounts": [],
    }
    deployment_path = context / "deployment.json"
    deployment_path.write_text(json.dumps(deployment, indent=2, sort_keys=True) + "\n")
    deployment_path.chmod(0o444)
    return sha(deployment_path)


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
    for path in (context / "AppBundle").rglob("*"):
        if path.is_dir():
            path.chmod(0o755)
    (context / "AppBundle").chmod(0o755)
    deployment_sha = stage_deployment(context)
    receipt = {
        "schema": "nflblitz-container-bundle/v1",
        "qualification": "static-server-preflight-only" if preflight else "awaiting-exact-image-browser-acceptance",
        "sourceBaseCommit": source_revision,
        "compiledSourceDigest": source_digest,
        "compiledSourceFiles": len(build["sources"]),
        "originalSourceRevision": json.loads((root / "data/manifest.json").read_bytes())["sourceRevision"],
        "buildInfoSha256": build_sha,
        "dataManifestSha256": build["dataManifestSha256"],
        "profile": build["profile"],
        "factory": build["factory"],
        "payloadFiles": len(json.loads((root / "data/manifest.json").read_bytes())["files"]),
        "payloadBytes": sum(entry["bytes"] for entry in json.loads((root / "data/manifest.json").read_bytes())["files"]),
        "bundleFiles": len(records),
        "bundleBytes": sum(entry["bytes"] for entry in records),
        "bundleInventorySha256": hashlib.sha256(json.dumps(records, sort_keys=True, separators=(",", ":")).encode()).hexdigest(),
        "imageAppFileMode": "0444",
        "imageAppDirectoryMode": "0755",
        "deploymentProfile": "runtime-base-url/v1",
        "deploymentSha256": deployment_sha,
        "files": records,
    }
    if build["profile"] == SELECTED_PROFILE or build["profile"] in AV306_PROFILES:
        receipt["executionProfile"] = build["execution"]["callerProfile"]
        receipt["nativeRuntimeSha256"] = build["execution"]["nativeRuntimeSha256"]
    (context / "bundle-inventory.json").write_text(json.dumps(receipt, indent=2, sort_keys=True) + "\n")
    (context / "bundle-inventory.json").chmod(0o444)
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
