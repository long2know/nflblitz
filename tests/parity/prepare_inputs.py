#!/usr/bin/env python3
"""Prepare the real full-kernel fixture using the permanent pinned production-data producers."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

from fetch_inputs import ROOT, REVISION, blob_sha1, ignored_output


ANIMATION_SHA = "f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274"
ROSTER_SHA = "f747741e95766185e0c615ded549d1ce2f5fec0c48ed2ecb29ba1b755764539b"
PLAYBOOK_SHA = "b180291759fb22c8ae7b2de7d417867e15243f6071bb1a672f11bb2fb6da4bd3"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verified_file(root, relative, expected_sha):
    relative = Path(relative)
    if relative.is_absolute() or ".." in relative.parts:
        raise ValueError("Fixture file path escapes its root.")
    path = root / relative
    for component in (path, *path.parents):
        if component.is_symlink():
            raise ValueError("Fixture symlinks are forbidden.")
        if component == root:
            break
    if sha(path) != expected_sha:
        raise ValueError("Actual fixture content pin changed: " + str(relative))
    return path


def fixture_hashes(manifest_path):
    manifest_path = Path(manifest_path)
    root = manifest_path.parent
    manifest = json.loads(manifest_path.read_bytes())
    if manifest.get("schema") != "source-kernel-real-inputs/v1" or manifest.get("source_revision") != REVISION:
        raise ValueError("Wrong real-input fixture schema/revision.")
    paths = [manifest_path]
    expected = {"animation": ANIMATION_SHA, "rosters": ROSTER_SHA, "playbook": PLAYBOOK_SHA}
    for name in ("animation", "rosters", "playbook", "model", "skeleton", "standing_pose"):
        entry = manifest[name]
        path = verified_file(root, entry["path"], entry["sha256"])
        if name in expected and entry["sha256"] != expected[name]:
            raise ValueError("Actual fixture content pin changed: " + name)
        paths.append(path)
    animation = root / manifest["animation"]["path"]
    entries = json.loads(animation.read_bytes())["clips"]
    if len(entries) != 117 or sum(entry["selected_frames"] for entry in entries) != 10288:
        raise ValueError("Expected the complete canonical 117-clip / 10288-frame input.")
    for entry in entries:
        path = verified_file(animation.parent, entry["path"], entry["sha256"])
        paths.append(path)
    return {str(path.resolve()): sha(path) for path in paths}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--reuse", action="store_true", help="Read-only verification of existing pinned fixture bytes.")
    args = parser.parse_args()
    source = args.source_root.resolve()
    output = ignored_output(args.output)
    if not output.is_relative_to(ROOT / ".squad/.scratch/SourceData"):
        parser.error("Use this worktree's .squad/.scratch/SourceData/ subtree for the roster producer.")
    if output.is_relative_to(source) or source.is_relative_to(output):
        parser.error("Source and generated fixture roots must be independent.")
    manifest_path = output / "fixture.json"
    if output.exists():
        if not args.reuse:
            parser.error("Fixture destination exists; use a new output or explicit --reuse.")
        hashes = fixture_hashes(manifest_path)
        print("PASS: read-only verification of " + str(len(hashes)) + " actual pinned fixture files.")
        return 0
    output.mkdir(parents=True)
    work = output / "work"
    work.mkdir()
    environment = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
                       PYTHONDONTWRITEBYTECODE="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    commands = [
        [sys.executable, "-B", "tools/animation/prepare_player_sequences.py", "--source-root", str(source),
         "--catalog", "tools/animation/ball_sequences.json", "--destination", str(output / "animation")],
        [sys.executable, "-B", "tools/playbooks/convert.py", "--source-root", str(source),
         "--output", str(output / "playbook")],
        [sys.executable, "-B", "tools/rosters/export_rosters.py", "--source-root", str(source),
         "--output", str(output / "rosters.json")],
    ]
    for index, command in enumerate(commands):
        result = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True, timeout=240)
        (output / ("producer-" + str(index) + ".log")).write_text(result.stdout + result.stderr, encoding="utf-8")
        if result.returncode:
            raise ValueError("Pinned permanent producer failed: " + command[2] + "\n" + result.stdout + result.stderr)
    sys.path.insert(0, str(ROOT / "tools/animation"))
    import read_wan_pose
    pose = read_wan_pose.first_pose((source / "anim/Qb.wan").read_bytes(), "QBSTANCE", {
        "repository": "historicalsource/nflblitz2000", "commit": REVISION, "path": "anim/Qb.wan",
        "git_blob_sha1": "a5bed4bc6699a88281e888660e31e349d4009d3f",
    })
    (output / "standing-pose.json").write_text(read_wan_pose.encode_pose(pose), encoding="utf-8")
    recipe = json.loads((ROOT / "tools/assets/player_inputs.json").read_bytes())
    selected = {entry["path"]: entry for entry in recipe["inputs"]
                if entry["path"] in ("models/plyr266.h", "models/skeleton.h")}
    for name, expected in selected.items():
        data = (source / name).read_bytes()
        if (len(data) != expected["bytes"] or hashlib.sha256(data).hexdigest() != expected["sha256"]
                or blob_sha1(data) != expected["git_blob_sha1"]):
            raise ValueError("Actual original geometry header pin mismatch: " + name)
        target = output / name
        target.parent.mkdir(exist_ok=True)
        target.write_bytes(data)
    manifest = {
        "schema": "source-kernel-real-inputs/v1", "source_revision": REVISION,
        "animation": {"path": "animation/export-inventory.json", "sha256": ANIMATION_SHA,
                      "clips": 117, "selected_frames": 10288},
        "playbook": {"path": "playbook/catalog.tsv", "sha256": PLAYBOOK_SHA},
        "rosters": {"path": "rosters.json", "sha256": ROSTER_SHA},
        "model": {"path": "models/plyr266.h", "sha256": selected["models/plyr266.h"]["sha256"]},
        "skeleton": {"path": "models/skeleton.h", "sha256": selected["models/skeleton.h"]["sha256"]},
        "standing_pose": {"path": "standing-pose.json", "sha256": sha(output / "standing-pose.json")},
        "recipes_sha256": {name: sha(ROOT / name) for name in (
            "tools/animation/ball_sequences.json", "tools/playbooks/recipe.json",
            "tools/rosters/source_inputs.json", "tools/assets/player_inputs.json")},
    }
    manifest_path.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    hashes = fixture_hashes(manifest_path)
    (output / "preparation.json").write_text(json.dumps({
        "status": "passed", "commands": commands, "fixture_sha256": sha(manifest_path),
        "generated_input_sha256": hashes, "full_gameplay_parity_claimed": False,
    }, indent=2) + "\n", encoding="utf-8")
    work.rmdir()
    print("PASS: actual real-input fixture " + str(manifest_path.relative_to(ROOT)) +
          " / " + str(len(hashes)) + " pinned generated files / no original data in Git.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
