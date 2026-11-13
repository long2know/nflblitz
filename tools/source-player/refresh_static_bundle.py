#!/usr/bin/env python3
"""Rebind verified loader-only static bytes without rebuilding or relabeling the unchanged Core/WASM generation."""
import copy
import hashlib
import json
from pathlib import Path
import re

BASE_BUILD_SHA = "bbec531e361e01e628b0a82db07acc40ba79bd16c44142cd19129ace437236b3"
BASE_INVENTORY_SHA = "b2f69cb31e51e88841f70b33dc8a752fbb39985bdcc220aeffc9203307dd12b5"
BASE_SOURCE_DIGEST = "4f519b55f331581a5c423e893de9d418251271387470d08639474a02e8e485b8"
BASE_NATIVE_SHA = "c9ff5d40f9815bbcabc16ed0485f2c538e031ef149b0ea5894d884d0c85c8e95"


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def encoded(value):
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode()


def require(value, message):
    if not value:
        raise ValueError(message)


def refresh(base_build_bytes, base_inventory_bytes, source_root, source_revision, *, view_controls=False):
    require(sha(base_build_bytes) == BASE_BUILD_SHA and sha(base_inventory_bytes) == BASE_INVENTORY_SHA,
            "The exact verified7419/bbec base product and complete payload inventory are required.")
    require(source_revision is None or re.fullmatch(r"[0-9a-f]{40}", source_revision),
            "Static provenance must be an actual Git commit or an explicitly uncommitted candidate.")
    root = Path(source_root)
    original = json.loads(base_build_bytes)
    inventory = json.loads(base_inventory_bytes)
    require(original["sourceRootDigest"] == BASE_SOURCE_DIGEST and
            original["execution"]["nativeRuntimeSha256"] == BASE_NATIVE_SHA and
            original["profile"] == "original-teams-complete-av306",
            "Only the unchanged genuinely compiled6ad GENERAL1153 Core/native generation may be reused.")
    old = {row["path"]: row for row in original["sources"]}
    project = "tools/source-player/SourcePlayer.Browser.csproj"
    project_bytes = (root / project).read_bytes()
    require(sha(project_bytes) == old[project]["sha256"],
            "Static refresh cannot change the compiled project, C#/roots/flags or project behavior.")
    current = copy.deepcopy(original)
    updated = {row["path"]: row for row in current["sources"]}
    static_files = {}
    static_names = ("main.js", "verified-assets.js") + (("index.html", "view-controls.js") if view_controls else ())
    static_paths = ["tools/source-player/wwwroot/" + name for name in static_names]
    for name in static_names:
        path = "tools/source-player/wwwroot/" + name
        raw = (root / path).read_bytes()
        static_files[name] = raw
        updated[path] = {"origin": "preview", "path": path, "sha256": sha(raw)}
    for path, row in old.items():
        if path in static_paths:
            continue
        candidate = root / path
        require(candidate.is_file() and sha(candidate.read_bytes()) == row["sha256"],
                "Static refresh cannot omit or change any other compiled/browser source: " + path)
    current["sources"] = sorted((row for row in updated.values() if row["origin"] == "production"), key=lambda row: row["path"])
    current["sources"] += sorted((row for row in updated.values() if row["origin"] == "preview"), key=lambda row: row["path"])
    current["sourceRootDigest"] = sha(json.dumps(current["sources"], sort_keys=True).encode())
    current["verifiedRuntimeResources"] = {
        "schema": "source-verified-runtime-resources/v1",
        "files": [row for row in inventory["files"] if row["path"].startswith("_framework/") and
                  Path(row["path"]).suffix in (".wasm", ".dat")],
    }
    current["execution"]["compiledAotReuse"] = {
        "schema": "unchanged-core-wasm-loader-static-refresh/v1",
        "originalBuildInfoSha256": BASE_BUILD_SHA, "originalCompiledSourceDigest": BASE_SOURCE_DIGEST,
        "originalCompiledHead": "6ad57cbd7969f3e0fe2473cebe6d076f6d0c7189",
        "staticSourceRevision": source_revision, "staticSourceUncommittedCandidate": source_revision is None,
        "changedStaticSources": static_paths,
        "projectChange": "None; all C#/compile/trimmer/project bytes identical",
        "applicationObjects": copy.deepcopy(original["execution"]["applicationObjects"]),
        "nativeRuntimeSha256": BASE_NATIVE_SHA,
    }
    current["verifiedAssetCache"] = {
        "schema": "persistent-verified-content-cache/v1", "key": "normalized-application-base-and-sha256",
        "verifyCacheHitsAndDownloads": True, "fallback": "explicit-warning-and-verified-network-only",
    }
    build_bytes = encoded(current)
    static_files["build-info.json"] = build_bytes
    records = {row["path"]: row for row in inventory["files"]}
    for name, raw in static_files.items():
        records[name] = {"path": name, "bytes": len(raw), "sha256": sha(raw)}
    inventory["files"] = [records[name] for name in sorted(records)]
    inventory.update(sourceBaseCommit=source_revision, compiledSourceDigest=current["sourceRootDigest"],
                     compiledSourceFiles=len(current["sources"]), buildInfoSha256=sha(build_bytes),
                     bundleFiles=len(records), bundleBytes=sum(row["bytes"] for row in records.values()),
                     bundleInventorySha256=sha(json.dumps(inventory["files"], sort_keys=True,
                                                         separators=(",", ":")).encode()),
                     qualification="static-loader-candidate-awaiting-actual-cache-browser-and-image-acceptance")
    inventory["actualCompiledSourceDigest"] = BASE_SOURCE_DIGEST
    inventory["actualCompiledHead"] = "6ad57cbd7969f3e0fe2473cebe6d076f6d0c7189"
    return current, static_files, inventory
