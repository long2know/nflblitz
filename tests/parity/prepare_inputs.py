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
MOVEMENT_SHA = "89c79007e04e9a17894c126c34dc66c199eb5a5961a4c749b1eb6b3e0e3951d6"
MOVEMENT_SELECTED_SHA = "5e8fc7c5f2011303fd7b1aea87e6f957b1acdf4cbb15a43fd82f978d4b944b68"
MOVEMENT_ANIMATION_SHA = "f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e"
SOUND_SHA = "4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280"
SOUND_INCLUDE_SHA = "db9e2f18fb6c62147843250ddd80c36712360a46193ab02ef23a96ab48224096"
SOUND_AUDIT_SHA = "4601c44212825a87b5b8c46c15dd91cccc58ad84e7e22e40d696a16afa5cab40"


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


def fixture_hashes(manifest_path, sound_functions=None):
    manifest_path = Path(manifest_path)
    root = manifest_path.parent
    manifest = json.loads(manifest_path.read_bytes())
    if manifest.get("schema") == "source-contact-kernel-real-inputs/v1":
        from contact_inputs import fixture_hashes as contact_hashes
        return contact_hashes(manifest_path, sound_functions)
    if manifest.get("schema") != "source-kernel-real-inputs/v1" or manifest.get("source_revision") != REVISION:
        raise ValueError("Wrong real-input fixture schema/revision.")
    paths = [manifest_path]
    expected = {"animation": ANIMATION_SHA, "rosters": ROSTER_SHA, "playbook": PLAYBOOK_SHA,
                "movement": MOVEMENT_SHA, "movement_animation": MOVEMENT_ANIMATION_SHA,
                "movement_selected_animation": MOVEMENT_SELECTED_SHA, "sound_functions": SOUND_SHA,
                "sound_function_original": SOUND_INCLUDE_SHA, "sound_function_manifest": SOUND_AUDIT_SHA}
    for name in ("animation", "rosters", "playbook", "model", "skeleton", "standing_pose",
                 "movement", "movement_animation", "movement_selected_animation",
                 "sound_functions", "sound_function_original", "sound_function_manifest"):
        entry = manifest[name]
        path = verified_file(root, entry["path"], entry["sha256"])
        if name in expected and entry["sha256"] != expected[name]:
            raise ValueError("Actual fixture content pin changed: " + name)
        paths.append(path)
    for name, count, frames in (("animation", 117, 10288), ("movement_animation", 157, 13490),
                                ("movement_selected_animation", 156, 13420)):
        animation = root / manifest[name]["path"]
        entries = json.loads(animation.read_bytes())["clips"]
        if len(entries) != count or sum(entry["selected_frames"] for entry in entries) != frames:
            raise ValueError("Actual distinct library inventory changed: " + name)
        for entry in entries:
            paths.append(verified_file(animation.parent, entry["path"], entry["sha256"]))
    if sound_functions is not None:
        sound_functions = Path(sound_functions).absolute()
        paths.append(verified_file(sound_functions.parent, sound_functions.name, SOUND_SHA))
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
    sound_name = "kernel-" + hashlib.sha256(output.relative_to(ROOT).as_posix().encode("ascii")).hexdigest()[:16]
    commands = [
        [sys.executable, "-B", "tools/animation/prepare_player_sequences.py", "--source-root", str(source),
         "--catalog", "tools/animation/ball_sequences.json", "--destination", str(output / "animation")],
        [sys.executable, "-B", "tools/playbooks/convert.py", "--source-root", str(source),
         "--output", str(output / "playbook")],
        [sys.executable, "-B", "tools/rosters/export_rosters.py", "--source-root", str(source),
         "--output", str(output / "rosters.json")],
        [sys.executable, "-B", "tools/animation/prepare_player_sequences.py", "--source-root", str(source),
         "--catalog", "tools/animation/movement_sequences.json", "--destination", str(output / "movement-selected")],
        [sys.executable, "-B", "tools/animation/prepare_movement_complete.py", "--source-root", str(source),
         "--selected-bundle", str(output / "movement-selected"),
         "--catalog", "tools/animation/movement_complete_sequences.json",
         "--destination", str(output / "movement-animation")],
        [sys.executable, "-B", "tools/movement/export.py", "--source-root", str(source),
         "--output", "tools/movement/.artifacts/source-movement-runtime.json"],
        [sys.executable, "-B", "tools/audio/prepare_functions.py", "--source-root", str(source),
         "--name", sound_name],
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
    runtime_bytes = (ROOT / "tools/movement/.artifacts/source-movement-runtime.json").read_bytes()
    if hashlib.sha256(runtime_bytes).hexdigest() != MOVEMENT_SHA:
        raise ValueError("Strict runtime-only movement export differs from its actual trusted pin.")
    (output / "movement-runtime.json").write_bytes(runtime_bytes)
    sound_output = ROOT / "tools/audio/.artifacts/functions" / sound_name
    (output / "audio-functions").mkdir()
    for name, expected in (("sound-functions.safn", SOUND_SHA), ("original-functions.inc", SOUND_INCLUDE_SHA),
                           ("manifest.json", SOUND_AUDIT_SHA)):
        path = sound_output / name
        if sha(path) != expected:
            raise ValueError("Actual sound-function producer output differs from its frozen pin: " + name)
        (output / "audio-functions" / name).write_bytes(path.read_bytes())
    manifest = {
        "schema": "source-kernel-real-inputs/v1", "source_revision": REVISION,
        "animation": {"path": "animation/export-inventory.json", "sha256": ANIMATION_SHA,
                      "clips": 117, "selected_frames": 10288},
        "playbook": {"path": "playbook/catalog.tsv", "sha256": PLAYBOOK_SHA},
        "rosters": {"path": "rosters.json", "sha256": ROSTER_SHA},
        "model": {"path": "models/plyr266.h", "sha256": selected["models/plyr266.h"]["sha256"]},
        "skeleton": {"path": "models/skeleton.h", "sha256": selected["models/skeleton.h"]["sha256"]},
        "standing_pose": {"path": "standing-pose.json", "sha256": sha(output / "standing-pose.json")},
        "movement": {"path": "movement-runtime.json", "sha256": MOVEMENT_SHA},
        "movement_animation": {"path": "movement-animation/export-inventory.json",
                               "sha256": MOVEMENT_ANIMATION_SHA, "clips": 157, "selected_frames": 13490},
        "movement_selected_animation": {"path": "movement-selected/export-inventory.json",
                                        "sha256": MOVEMENT_SELECTED_SHA, "clips": 156, "selected_frames": 13420},
        "sound_functions": {"path": "audio-functions/sound-functions.safn", "sha256": SOUND_SHA},
        "sound_function_original": {"path": "audio-functions/original-functions.inc", "sha256": SOUND_INCLUDE_SHA},
        "sound_function_manifest": {"path": "audio-functions/manifest.json", "sha256": SOUND_AUDIT_SHA},
        "recipes_sha256": {name: sha(ROOT / name) for name in (
            "tools/animation/ball_sequences.json", "tools/playbooks/recipe.json",
            "tools/rosters/source_inputs.json", "tools/assets/player_inputs.json",
            "tools/movement/recipe.json", "tools/animation/movement_sequences.json",
            "tools/animation/movement_complete_sequences.json", "tools/audio/function-inputs.json")},
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
