#!/usr/bin/env python3
"""Extend an immutable selected bundle using an explicit source-converter replay."""

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sys

try:
    from . import prepare_player_sequences as selected
    from . import read_wan_replay as replay
except ImportError:
    def _module(name):
        spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + ".py"))
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module
    selected = _module("prepare_player_sequences")
    replay = _module("read_wan_replay")


CATALOG = Path(__file__).with_name("movement_complete_sequences.json")
SCHEMA = "original-wan-movement-complete-catalog/v1"
CODE_PINS = {
    "plyrseq.c": "28cef0d1082f2b0c9241930fdc8886de4c8f5438",
    "player.c": "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c",
    "anim/MAKEFILE": "2e77acc8baa9e5f915a54a12dab7c55e59e29f81",
    "anim/seattle.mak": "19f043e713eedcaaf5a8545801f7b3dca2331fd5",
}
require = selected._require
WanError = selected.WanError


def load_catalog(path):
    path = selected._absolute(path)
    data = selected._read_bytes(path, selected.MAX_METADATA_BYTES)
    catalog = json.loads(data, object_pairs_hook=selected._unique_object,
                         parse_constant=selected._nonfinite)
    require(isinstance(catalog, dict) and catalog.keys() == {
        "schema", "source", "selected_base", "sequence_count", "source_frame_count",
        "selected_frame_count", "extension", "converter_replay", "code_references",
        "source_playback_evidence",
    }, "complete catalog must contain only the defined metadata fields")
    require(catalog["schema"] == SCHEMA, f"expected {SCHEMA}")
    base = catalog["selected_base"]
    require(isinstance(base, dict) and base.keys() == {
        "catalog", "catalog_sha256", "manifest_sha256", "sequence_count",
        "source_frame_count", "selected_frame_count",
    }, "selected base requires an immutable catalog and manifest identity")
    require(isinstance(base["catalog"], str)
            and re.fullmatch(r"[A-Za-z0-9_-]+\.json", base["catalog"]),
            "selected base catalog must be an adjacent portable JSON filename")
    base_path = path.with_name(base["catalog"])
    base_catalog, base_hash = selected.load_catalog(base_path)
    require(base_hash == base["catalog_sha256"], "selected base catalog SHA256 mismatch")
    selected._hex(base["manifest_sha256"], 64, "selected base manifest SHA256")
    require(catalog["source"] == base_catalog["source"],
            "extension and selected base source pins must agree")
    entry = catalog["extension"]
    require(isinstance(entry, dict) and entry.keys() == selected.ENTRY_FIELDS,
            "extension must contain only the original identity/count/boundary fields")
    require(entry["runtime_name"] == "R_SKIP" and
            entry["source_path"] == "anim/misc99.wan" and
            entry["source_git_blob_sha1"] == "256acb7ddb838627312193ae19db66abd18ac146",
            "only the explicitly audited pinned R_SKIP source is qualified")
    require(all(type(entry[key]) is int for key in (
        "source_frames", "selected_frames", "source_first", "source_last")),
        "extension counts and boundaries must be integers, not booleans")
    require(tuple(entry[k] for k in ("source_frames", "selected_frames", "source_first", "source_last"))
            == (70, 70, 0, 69), "R_SKIP must retain all seventy original selected authoring frames")
    require("R_SKIP" not in {e["runtime_name"] for e in base_catalog["clips"]},
            "extension must not replace or alias a selected-base entry")
    replay.validate_recipe(catalog["converter_replay"])
    for key in ("sequence_count", "source_frame_count", "selected_frame_count"):
        require(type(base[key]) is int and base[key] == base_catalog[key],
                f"selected base {key} does not match the frozen catalog")
        additional = 1 if key == "sequence_count" else entry[
            "source_frames" if key == "source_frame_count" else "selected_frames"]
        require(type(catalog[key]) is int and catalog[key] == base[key] + additional,
                f"complete catalog {key} must be exactly the selected base plus R_SKIP")
    refs = catalog["code_references"]
    require(isinstance(refs, list) and len(refs) == len(CODE_PINS),
            "all four active source/build references are required")
    require({r.get("path"): r.get("git_blob_sha1") for r in refs if isinstance(r, dict)} == CODE_PINS,
            "source-reference pins must identify the audited active caller and build recipe")
    for reference in refs:
        require(reference.keys() == {"path", "git_blob_sha1", "lines"} and
                isinstance(reference["lines"], list) and len(reference["lines"]) == 2 and
                all(type(i) is int and i > 0 for i in reference["lines"]) and
                reference["lines"][0] <= reference["lines"][1],
                "invalid source-reference line range")
    playback = catalog["source_playback_evidence"]
    require(isinstance(playback, dict) and playback.keys() == {
        "profile", "script", "cycle", "steady_cycle", "steady_exported_frames",
        "steady_frames_per_source_tick", "steady_frames_per_second_at_57Hz",
        "entry", "anchors", "shipped_SEQ_quantization_claimed",
    } and playback.get("profile") == "normal57Hz/USE_OLD_QB=FALSE"
            and playback.get("script") == "r_skip_anim" and playback.get("cycle") == "r_skip_cyc"
            and all(type(playback[key]) is int for key in (
                "steady_exported_frames", "steady_frames_per_source_tick",
                "steady_frames_per_second_at_57Hz"))
            and playback.get("steady_exported_frames") == 70
            and playback.get("steady_frames_per_source_tick") == 1
            and playback.get("steady_frames_per_second_at_57Hz") == 57
            and playback.get("shipped_SEQ_quantization_claimed") is False,
            "explicit original script/profile timing evidence is required")
    require(all(isinstance(playback[key], str) and 0 < len(playback[key]) <= 1024
                for key in ("profile", "script", "cycle", "steady_cycle", "entry", "anchors")),
            "playback evidence must be bounded descriptive source metadata, not motion data")
    return catalog, hashlib.sha256(data).hexdigest(), base_path


def _verify_code(source_root, catalog):
    sources = {}
    for reference in catalog["code_references"]:
        path = reference["path"]
        data = selected._read_bytes(source_root / path, 4 * 1024 * 1024)
        require(replay.wan.git_blob_sha1(data) == reference["git_blob_sha1"],
                f"{path}: active source-reference Git blob mismatch")
        text = data.decode("ascii")
        start, stop = reference["lines"]
        lines = text.splitlines()
        require(stop <= len(lines), f"{path}: source-reference lines are out of bounds")
        sources[path] = "\n".join(lines[start - 1:stop])
    script = sources["plyrseq.c"]
    require(all(token in script for token in (
        "r_skip_anim:", "r_skip_cyc:", "SEQ_R_SKIP", "ANI_RUNTOEND,1", "AA_PROPFRAME",
        "ANI_INTTOSEQ,4", "MODE_ANCHORXZ",
    )), "pinned source-reference range does not contain the complete active R_SKIP program")
    require("r_skip_anim" in sources["player.c"]
            and "r_skip.seq" in sources["anim/seattle.mak"]
            and "wan2seq -s 0.12898" in sources["anim/MAKEFILE"],
            "active selector/SEQ inventory/source conversion references are incomplete")


def _producer_identity():
    return {
        "policy": replay.POLICY,
        "files": {
            name: hashlib.sha256(Path(__file__).with_name(name).read_bytes()).hexdigest()
            for name in ("prepare_movement_complete.py", "read_wan_replay.py",
                         "prepare_player_sequences.py", "read_wan_clip.py", "read_wan_pose.py")
        },
    }


def prepare_bundle(source_root, selected_bundle, destination, catalog_path=CATALOG, *, reuse=False):
    catalog, catalog_hash, base_path = load_catalog(catalog_path)
    source_root = selected._absolute(source_root)
    selected_bundle = selected._absolute(selected_bundle)
    destination = selected._absolute(destination)
    scratch = selected._absolute(replay.wan.SCRATCH)
    for path in (source_root, selected_bundle, destination):
        selected._no_symlinks(path)
    require(destination != scratch and destination.is_relative_to(scratch),
            "destination must stay beneath ignored .squad/.scratch/")
    for root in (source_root, selected_bundle):
        require(not destination.is_relative_to(root) and not root.is_relative_to(destination),
                "input roots and destination must not contain one another")
    exists = destination.exists()
    require(not exists or reuse, "destination already exists; use --reuse only for an identical complete bundle")
    require(not exists or destination.is_dir(), "destination must be a directory")
    _verify_code(source_root, catalog)
    base_manifest = selected._read_bytes(selected_bundle / selected.MANIFEST, selected.MAX_METADATA_BYTES)
    require(hashlib.sha256(base_manifest).hexdigest() == catalog["selected_base"]["manifest_sha256"],
            "selected base manifest SHA256 mismatch")
    # The existing producer freshly validates every original byte without rewriting it.
    selected.prepare_bundle(source_root, selected_bundle, base_path, reuse=True)
    inventory = json.loads(base_manifest)
    outputs = {
        entry["path"]: selected._read_bytes(selected_bundle / entry["path"], 8 * 1024 * 1024)
        for entry in inventory["clips"]
    }
    expected = catalog["extension"]
    data = selected._read_source(source_root, expected["source_path"])
    clip = replay.full_replay_clip(data, expected["runtime_name"], {
        **catalog["source"], "path": expected["source_path"],
        "git_blob_sha1": expected["source_git_blob_sha1"], "local_input": expected["source_path"],
        "source_linkage": "caller-supplied commit/path; content checked against expected blob offline",
    }, catalog["converter_replay"])
    clip["source_playback_evidence"] = catalog["source_playback_evidence"]
    encoded = selected.reader.encode_clip(clip).encode("utf-8")
    entry = selected._inventory_entry(expected, clip, encoded)
    entry["converter_replay"] = catalog["converter_replay"]
    outputs[entry["path"]] = encoded
    entries = sorted([*inventory["clips"], entry], key=lambda item: item["runtime_name"])
    complete = {
        "schema": selected.INVENTORY_SCHEMA, "source": catalog["source"],
        "format_source": inventory["format_source"], "catalog_sha256": catalog_hash,
        "selected_base_manifest_sha256": catalog["selected_base"]["manifest_sha256"],
        "producer": _producer_identity(), "clips": entries,
        "extension_policy": replay.POLICY,
        "code_references": catalog["code_references"],
    }
    manifest = selected.reader.encode_clip(complete).encode("utf-8")
    require(len(manifest) <= selected.MAX_METADATA_BYTES, "complete inventory exceeds library byte limit")
    outputs[selected.MANIFEST] = manifest
    if exists:
        selected._verify_bundle(destination, outputs)
    else:
        destination.parent.mkdir(parents=True, exist_ok=True)
        selected._no_symlinks(destination)
        destination.mkdir()
        (destination / "clips").mkdir()
        for relative, content in outputs.items():
            path = destination / relative
            selected._no_symlinks(path)
            with path.open("xb") as stream:
                stream.write(content)
        selected._verify_bundle(destination, outputs)
    digest = hashlib.sha256(manifest).hexdigest()
    return {
        "schema": "original-wan-movement-complete-result/v1",
        "manifest": selected.MANIFEST, "manifest_sha256": digest,
        "catalog_sha256": catalog_hash, "selected_base_manifest_sha256":
            catalog["selected_base"]["manifest_sha256"],
        "clips": len(entries), "source_frames": catalog["source_frame_count"],
        "selected_frames": catalog["selected_frame_count"],
        "unchanged_selected_clips": len(inventory["clips"]), "converter_state_replays": 1,
        "resource_identity": f"SourceData/local-plyr266-movement{len(entries)}-{digest[:8]}",
        "reused": exists, "shipped_binary_verified": False,
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True,
                        help="explicit pinned WAN and active code tree; no retrieval or historical scripts")
    parser.add_argument("--selected-bundle", type=Path, required=True,
                        help="immutable complete selected base bundle, freshly checked read-only")
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--catalog", type=Path, default=CATALOG)
    parser.add_argument("--reuse", action="store_true", help="read-only identical-bundle verification")
    args = parser.parse_args(argv)
    try:
        result = prepare_bundle(args.source_root, args.selected_bundle, args.destination,
                                args.catalog, reuse=args.reuse)
        print(selected.reader.encode_clip(result), end="")
        return 0
    except (OSError, ValueError, UnicodeError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
