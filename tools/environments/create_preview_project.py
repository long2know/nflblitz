#!/usr/bin/env python3
"""Prepare an isolated Unity world-only project; does not start Unity or read activation.

Run from the repository root before a coordinated Editor lease:
  python3 -B tools/environments/create_preview_project.py

The default metadata-only preview_sources.json pins only the six copied C#
sources and their existing metas. --source-hashes <snapshot.json> retains the
explicit custom-snapshot path; no ignored session snapshot is required.

Then use the explicitly licensed Unity 6000.3.24f1 executable with this project,
-batchmode -nographics -quit -buildTarget WebGL and one executeMethod at a time:
  Blitz.Editor.SourceEnvironmentImporter.ImportFromCommandLine
  Blitz.Editor.SourceEnvironmentChecks.VerifyFromCommandLine
  Blitz.Editor.SourceEnvironmentChecks.BuildIsolatedPreview

The first two require -environmentBundleRoot <absolute ignored converted root>
and -environmentManifestSha256 <SHA256 of that root's environment.json>.
No main project source/scene/settings/build or existing browser service is changed.
"""

import argparse
import json
from pathlib import Path
import re
import sys

import prepare_environment as env


SOURCE_FILES = (
    "Assets/Scripts/AssetImport/SourceEnvironmentData.cs",
    "Assets/Scripts/AssetImport/AuthorizedAssetFiles.cs",
    "Assets/Scripts/AssetImport/GeneratedPlayerData.cs",
    "Assets/Scripts/Presentation/SourceEnvironmentProvider.cs",
    "Assets/Scripts/Editor/SourceEnvironmentImporter.cs",
    "Assets/Scripts/Editor/SourceEnvironmentChecks.cs",
)
DEFAULT_SNAPSHOT = "tools/environments/preview_sources.json"


def load_source_snapshot(project, source_hashes=None):
    path = source_hashes if source_hashes is not None else project / DEFAULT_SNAPSHOT
    path = Path(path).absolute()
    snapshot_bytes = env.read_file(path.parent, path.name, 256 * 1024)
    snapshot = json.loads(snapshot_bytes)
    env.require(isinstance(snapshot, dict) and isinstance(snapshot.get("files"), list)
                and 0 < len(snapshot["files"]) <= 256, "Expected a bounded preview source-hash snapshot.")
    env.require(isinstance(snapshot.get("frozenDependencyCheckpoint"), str)
                and re.fullmatch(r"[0-9a-f]{40}", snapshot["frozenDependencyCheckpoint"]) is not None,
                "Preview snapshot requires its frozen dependency checkpoint identity.")
    hashes = {}
    aliases = set()
    for record in snapshot["files"]:
        env.require(isinstance(record, dict) and "path" in record and "sha256" in record,
                    "Invalid preview source-hash record.")
        relative = env.portable(record["path"])
        sha256 = record["sha256"]
        env.require(isinstance(sha256, str) and re.fullmatch(r"[0-9a-f]{64}", sha256) is not None,
                    "Invalid preview source SHA256: " + relative)
        env.require(relative.lower() not in aliases, "Duplicate/aliased preview source identity: " + relative)
        aliases.add(relative.lower())
        hashes[relative] = sha256
    required = {relative for source in SOURCE_FILES for relative in (source, source + ".meta")}
    env.require(required.issubset(hashes), "Preview snapshot must pin all six copied C# sources and their metas.")
    if source_hashes is None:
        env.require(snapshot.get("schema") == "source-world-preview-inputs/v1" and set(hashes) == required,
                    "Default preview snapshot must contain exactly the six copied sources and their metas.")
    return snapshot_bytes, snapshot, hashes


def create_preview_project(project, source_hashes=None):
    project = env.physical(project, True)
    snapshot_bytes, snapshot, hashes = load_source_snapshot(project, source_hashes)
    for path, sha256 in hashes.items():
        env.require(env.digest(env.read_file(project, path)) == sha256, "World source changed before isolated copy: " + path)
    files = {}
    for path in SOURCE_FILES:
        for relative in (path, path + ".meta"):
            data = env.read_file(project, relative)
            env.require(relative in hashes and env.digest(data) == hashes[relative],
                        "Unpinned World/native dependency in isolated copy: " + relative)
            files[relative] = data
    files["Packages/manifest.json"] = env.json_bytes({
        "dependencies": {"com.unity.modules.jsonserialize": "1.0.0"}
    })
    files["Assets/Scripts/Presentation/SourceWorldInspection.cs"] = INSPECTION_SOURCE.encode("ascii")
    files["ProjectSettings/ProjectVersion.txt"] = b"m_EditorVersion: 6000.3.24f1\nm_EditorVersionWithRevision: 6000.3.24f1 (4e7b9b5b6244)\n"
    files["preview-source-receipt.json"] = env.json_bytes({
        "purpose": "Original field2/stadpie world-only native/import/WebGL inspection, not the main gameplay player.",
        "sourceFiles": [{"path": path, "sha256": env.digest(files[path])} for path in SOURCE_FILES],
        "copiedInputs": [{"path": path, "sha256": env.digest(files[path])}
                         for source in SOURCE_FILES for path in (source, source + ".meta")],
        "sourceSnapshotSha256": env.digest(snapshot_bytes),
        "sourceSnapshotProfile": "public-frozen-default" if source_hashes is None else "explicit-custom",
        "frozenDependencyCheckpoint": snapshot["frozenDependencyCheckpoint"],
        "copiedHotGameplayOrAnimationFiles": False,
        "editorStarted": False, "mainProjectChanged": False,
    })
    destination = project / "Build/world-preview/native-project"
    env.write_bundle(project, destination, files)
    for path, data in files.items():
        env.require(env.read_file(destination, path) == data, "Isolated copy did not persist exact bytes: " + path)
    for path, sha256 in hashes.items():
        env.require(env.digest(env.read_file(project, path)) == sha256, "World source changed during isolated copy: " + path)
    return destination


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-hashes", type=Path,
                        help="Explicit custom source-hash snapshot; default is the public metadata-only preview_sources.json.")
    args = parser.parse_args(argv)
    project = Path(__file__).absolute().parents[2]
    try:
        create_preview_project(project, args.source_hashes)
    except (env.EnvironmentError, OSError, KeyError, TypeError, json.JSONDecodeError) as error:
        print("Source world preview preparation rejected: " + str(error), file=sys.stderr)
        return 1
    print("Prepared isolated world-only project: Build/world-preview/native-project; every source/copy hash verified before/after (no Editor started).")
    return 0


INSPECTION_SOURCE = """using UnityEngine;
namespace Blitz.Presentation
{
    public sealed class SourceWorldInspection : MonoBehaviour
    {
        private void Awake()
        {
            SourceEnvironmentProvider prefab = SourceEnvironmentProvider.ResolveWorldMode();
            if (prefab == null)
                throw new System.InvalidOperationException("This inspection requires the installed original world; no graybox.");
            SourceEnvironmentProvider instance = SourceEnvironmentProvider.AttachRequired(prefab, transform);
            instance.ValidateGeneratedWorld();
            Camera camera = new GameObject("Inspection camera / not the gameplay camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            string url = Application.absoluteURL;
            Vector3 eye = new Vector3(-62, 35, -72), focus = new Vector3(0, 5, 0);
            if (url.Contains("view=sideline")) { eye = new Vector3(-20, 20, -42); focus = new Vector3(-10, 4, 0); }
            if (url.Contains("view=endzone")) { eye = new Vector3(-74, 17, -32); focus = new Vector3(-48, 4, 0); }
            camera.transform.position = eye;
            camera.transform.LookAt(focus);
            camera.fieldOfView = 58;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 600;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            Debug.Log("WORLD_PREVIEW_READY manifest=" + prefab.ManifestSha256 + " draws=" + prefab.DrawCount +
                " original field2/stadpie; no gameplay/HUD/actors/camera parity claim.");
        }
    }
}
"""


if __name__ == "__main__":
    raise SystemExit(main())
