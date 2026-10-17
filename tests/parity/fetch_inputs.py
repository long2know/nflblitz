#!/usr/bin/env python3
"""Acquire only metadata-pinned original inputs in an explicit ignored worktree directory."""
import argparse
import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[2]
REPOSITORY = "historicalsource/nflblitz2000"
REVISION = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded"
TREE = "a7e0352125d46075b7d98e87253ab89b9160a4c7"
CAMERA_INPUTS = {
    "include/GAME.H": "09d65c92971c32c44b1dc49fb4e4e98b371b1cd3",
    "include/ani3d.h": "826c6322467fcfe503b8402b540e4731671f9b6b",
    "include/camdefs.h": "8586d537afe10067e110722f6ba42bb3f9f07952",
    "CamData.c": "4bfc54c6b9633b076f01163c2142c7fce799a0e0",
    "cambot.c": "00c9c913bc6a697dfd590666bf5597f2c2bc1eb7",
    "plyrseq.c": "28cef0d1082f2b0c9241930fdc8886de4c8f5438",
    "detect.c": "68c239fa2e617988107fbebeb4dfeff5bbfe9233",
    "player.c": "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c",
    "lib/ani3d/ANIM.C": "97d7856d67a432937e249be274ef67a9ad354dc1",
    "lib/ani3d/pipe3D.c": "0966ecaf0d7eef7d9c635a97899a421ce1eeea41",
}
SOUND_STATE_INPUTS = {
    "Game.c": "a005a9b44f032080f3dd6341dd4f5eb6104d9cd7",
    "mainproc.c": "ab10aa4eb5a1f181bef598faea6def3c2c96c12c",
}


def sound_function_pins():
    spec = importlib.util.spec_from_file_location(
        "source_kernel_sound_inputs", ROOT / "tools/audio/fetch_function_inputs.py")
    helper = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(helper)
    return helper.input_pins()


def blob_sha1(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()


def ignored_output(path):
    absolute = Path(path).absolute()
    scratch = ROOT / ".squad/.scratch"
    if ".." in Path(path).parts or not absolute.is_relative_to(scratch) or absolute == scratch:
        raise ValueError("Output must be a descendant of this worktree's ignored .squad/.scratch.")
    for component in (absolute, *absolute.parents):
        if component.is_symlink():
            raise ValueError("Output symlinks are forbidden.")
    return absolute


def input_pins():
    pins = {}

    def add(name, pin):
        path = Path(name)
        if path.is_absolute() or ".." in path.parts or not name:
            raise ValueError("Nonportable original input path.")
        if name in pins and pins[name] != pin:
            raise ValueError("Conflicting actual original blob pins: " + name)
        pins[name] = pin

    animation = json.loads((ROOT / "tools/animation/ball_sequences.json").read_bytes())
    for clip in animation["clips"]:
        add(clip["source_path"], clip["source_git_blob_sha1"])
    movement_catalog = json.loads((ROOT / "tools/animation/movement_sequences.json").read_bytes())
    for clip in movement_catalog["clips"]:
        add(clip["source_path"], clip["source_git_blob_sha1"])
    complete = json.loads((ROOT / "tools/animation/movement_complete_sequences.json").read_bytes())
    for reference in complete["code_references"]:
        add(reference["path"], reference["git_blob_sha1"])
    movement = json.loads((ROOT / "tools/movement/recipe.json").read_bytes())
    for name, pin in movement["inputs"].items():
        add(name, pin)
    playbook = json.loads((ROOT / "tools/playbooks/recipe.json").read_bytes())
    for entry in playbook["inputs"].values():
        add(entry["path"], entry["blob_sha1"])
    rosters = json.loads((ROOT / "tools/rosters/source_inputs.json").read_bytes())
    for entry in rosters["inputs"]:
        add(entry["path"], entry["git_blob_sha1"])
    original = json.loads((ROOT / "tests/source-oracles/pins.json").read_bytes())
    for name, pin in original["blobs"].items():
        add(name, pin)
    for name, pin in CAMERA_INPUTS.items():
        add(name, pin)
    for name, pin in sound_function_pins().items():
        add(name, pin)
    for name, pin in SOUND_STATE_INPUTS.items():
        add(name, pin)
    return dict(sorted(pins.items()))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--cache", type=Path, action="append", default=[],
                        help="Explicit read-only original cache; only matching bytes are used.")
    parser.add_argument("--blob-file", action="append", default=[],
                        help="Explicit canonical/path=verified-file override, still blob-checked.")
    parser.add_argument("--download", action="store_true", help="Explicitly permit pinned Git blob/tree API reads.")
    args = parser.parse_args()
    destination = ignored_output(args.output)
    pins = input_pins()
    overrides = {}
    for entry in args.blob_file:
        name, separator, path = entry.partition("=")
        if not separator or name not in pins or name in overrides:
            parser.error("--blob-file requires one known canonical/path=verified-file.")
        overrides[name] = Path(path)
    records = []
    for name, pin in pins.items():
        target = destination / name
        candidates = [target]
        if name in overrides:
            candidates.append(overrides[name])
        candidates += [cache / name for cache in args.cache]
        data = None
        rejected = []
        for path in candidates:
            if not path.is_file():
                continue
            candidate = path.read_bytes()
            if blob_sha1(candidate) == pin:
                data = candidate
                break
            rejected.append({"path": str(path), "actual_blob_sha1": blob_sha1(candidate)})
            if path == target:
                raise ValueError("Existing isolated input differs from its pin; never overwritten: " + name)
        if data is None:
            if not args.download:
                raise ValueError("Missing pinned input " + name + "; supply an explicit cache or --download.")
            command = ["gh", "api", f"repos/{REPOSITORY}/git/blobs/{pin}"]
            response = json.loads(subprocess.run(command, capture_output=True, text=True,
                                                 check=True, timeout=90).stdout)
            if response.get("sha") != pin or response.get("encoding") != "base64":
                raise ValueError("Git blob API identity/encoding mismatch: " + name)
            data = base64.b64decode(response["content"])
        if blob_sha1(data) != pin:
            raise ValueError("Original bytes failed their exact Git blob pin: " + name)
        target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists():
            target.write_bytes(data)
        records.append({"path": name, "git_blob_sha1": pin, "bytes": len(data),
                        "sha256": hashlib.sha256(data).hexdigest(), "rejected_cache_bytes": rejected})
    tree_target = destination / "tree.json"
    tree_candidates = [tree_target, *[cache / "tree.json" for cache in args.cache]]
    tree_bytes = None
    for path in tree_candidates:
        if path.is_file():
            candidate = path.read_bytes()
            parsed = json.loads(candidate)
            if parsed.get("sha") == TREE and not parsed.get("truncated", False):
                tree_bytes = candidate
                break
            if path == tree_target:
                raise ValueError("Existing isolated tree metadata has the wrong identity.")
    if tree_bytes is None:
        if not args.download:
            raise ValueError("Missing complete pinned tree.json; supply its cache or --download.")
        command = ["gh", "api", f"repos/{REPOSITORY}/git/trees/{TREE}?recursive=1"]
        tree_bytes = subprocess.run(command, capture_output=True, check=True, timeout=90).stdout
    tree = json.loads(tree_bytes)
    if tree.get("sha") != TREE or tree.get("truncated", False):
        raise ValueError("Tree metadata identity/completeness mismatch.")
    if not tree_target.exists():
        tree_target.write_bytes(tree_bytes)
    receipt = {"schema": "source-kernel-original-inputs/v1", "repository": REPOSITORY,
               "revision": REVISION, "tree": TREE, "verified_blobs": records,
               "tree_sha256": hashlib.sha256(tree_bytes).hexdigest(),
               "tree_objects_verified_by_roster_producer_before_use": True}
    (destination / "input-receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print("PASS: " + str(len(records)) + " exact pinned original inputs; complete tree supplied for producer verification.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
