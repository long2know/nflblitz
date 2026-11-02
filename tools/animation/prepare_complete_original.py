#!/usr/bin/env python3
"""Extend the immutable282 library with the21 exact original TD/reaction clips."""

import argparse
import copy
import hashlib
import json
from pathlib import Path

import prepare_player_sequences as selected

reader = selected.reader
BASE_SHA256 = "50aa432ff9b332ec9087133f5bccb43ae18e1ca7c0bf7830cbdd675d3e024046"
BASE_CALLBACK_SHA256 = "e9a2ccbfffda0e83ae9ba409e4fbfd6f6c6f074ebd024aa30367d9d06c29c653"
BASE_IMPACT_SHA256 = "456d525aa75ba343942613793cc437d6732f0b16d972a981537929daece9f3d7"
PROFILE = "original-teams-complete-original303"
CALLBACK_PROFILE = "OriginalRelease57Hz-C2-M1-CompleteOriginal303-InitSoundBanks-v1"
PINS = {
    "anim/ENDZONE.WAN": "e272649c098b26db7e26757f11b1da267e5ffb5e",
    "anim/inzone99.wan": "b0337c15b7691aca59a7c7876f21a5e5bdf0948e",
    "anim/misc.wan": "da6818b994f1e4ba93e48e151382a8dc96f27f58",
    "anim/misc3.wan": "ba79820423945efc7c45c2b66fe719511090a1ba",
    "anim/misc4.wan": "911c4f39800c5cd406fd5148cc3d51482b19d1f1",
    "anim/Runs.wan": "2f835b476d64d6b0d5cc37b0cf8ec2c0a997ec9e",
}
CLIPS = (
    ("ENDZ_06", "anim/ENDZONE.WAN", 163, 163, 162),
    ("ENDZ_08", "anim/ENDZONE.WAN", 75, 75, 74),
    ("ENDZ_20", "anim/ENDZONE.WAN", 171, 171, 170),
    ("M_EZDNCE", "anim/inzone99.wan", 369, 185, 368),
    ("M_EZJASN", "anim/inzone99.wan", 194, 97, 192),
    ("M_EZPUSH", "anim/inzone99.wan", 290, 145, 288),
    ("M_EZJON1", "anim/inzone99.wan", 300, 150, 298),
    ("M_EZJON2", "anim/inzone99.wan", 300, 150, 298),
    ("INZONE1", "anim/misc.wan", 333, 333, 332),
    ("INZONE2", "anim/misc.wan", 242, 242, 241),
    ("INZONE3", "anim/misc.wan", 388, 388, 387),
    ("M_CELCHE", "anim/misc3.wan", 295, 295, 294),
    ("M_TAUNBC", "anim/misc3.wan", 215, 108, 214),
    ("M_EZCTHR", "anim/misc3.wan", 220, 220, 219),
    ("M_EZCWHA", "anim/misc3.wan", 275, 275, 274),
    ("M_EZWAV1", "anim/misc3.wan", 335, 335, 334),
    ("M_EZCGUN", "anim/misc3.wan", 400, 300, 299),
    ("M_EZCSPN", "anim/misc3.wan", 325, 325, 324),
    ("M_EZCHRS", "anim/misc4.wan", 350, 151, 300),
    ("R_SLO_D1", "anim/Runs.wan", 186, 186, 185),
    ("R_SLO_D2", "anim/Runs.wan", 218, 218, 217),
)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def canonical(value):
    return (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=True) + "\n").encode("ascii")


def pinned(root, name, expected):
    raw = selected._read_bytes(selected._absolute(root / name), 8 * 1024 * 1024)
    selected._require(sha(raw) == expected, "Frozen inherited input changed: " + name)
    return raw


def prepare(source, base, output):
    source, base, output = map(selected._absolute, (source, base, output))
    selected._no_symlinks(source)
    selected._no_symlinks(base)
    selected._no_symlinks(output)
    selected._require(output.is_relative_to(reader.wan.SCRATCH) and output != reader.wan.SCRATCH
                      and not output.exists(), "Choose a create-new ignored animation output.")
    for root in (source, base):
        selected._require(not output.is_relative_to(root) and not root.is_relative_to(output),
                          "Original/inherited/output roots must not overlap.")
    original_raw = pinned(base, "animation/export-inventory-presnap-switch.json", BASE_SHA256)
    original = json.loads(original_raw, object_pairs_hook=selected._unique_object)
    selected._require(len(original["clips"]) == 282
                      and len({e["runtime_name"] for e in original["clips"]}) == 282,
                      "The complete actual282 inventory is required.")
    outputs = {}
    for entry in original["clips"]:
        outputs[entry["path"]] = pinned(base, "animation/" + entry["path"], entry["sha256"])
    cache, additions = {}, []
    for name, path, source_frames, frames, last in CLIPS:
        if path not in cache:
            cache[path] = selected._read_source(source, path)
        raw = cache[path]
        clip = reader.full_clip(raw, name, {
            "repository": "historicalsource/nflblitz2000",
            "commit": "89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
            "path": path, "git_blob_sha1": PINS[path], "local_input": path,
            "source_linkage": "caller-supplied commit/path; content checked against expected blob offline",
        })
        encoded = reader.encode_clip(clip).encode("utf-8")
        entry = selected._inventory_entry({
            "runtime_name": name, "source_path": path, "source_git_blob_sha1": PINS[path],
            "source_frames": source_frames, "selected_frames": frames, "source_first": 0, "source_last": last,
        }, clip, encoded)
        selected._require(entry["path"] not in outputs, "The TD extension must be disjoint from282.")
        outputs[entry["path"]] = encoded
        additions.append(entry)
    inventory = copy.deepcopy(original)
    inventory.update(profile=PROFILE, producer_policy="immutable-source282-plus-disjoint-exact21-td-reactions/v1",
                     base_presnap_switch_animation_manifest_sha256=BASE_SHA256,
                     clips=sorted(original["clips"] + additions, key=lambda e: e["runtime_name"]))
    frame_count = sum(e["selected_frames"] for e in inventory["clips"])
    selected._require(len(inventory["clips"]) == 303
                      and len({e["runtime_name"] for e in inventory["clips"]}) == 303,
                      "The new inventory must contain303 unique original sequences.")
    outputs["export-inventory.json"] = reader.encode_clip(inventory).encode("utf-8")
    callbacks = json.loads(pinned(base, "kernel/c2-presnap-switch-callback-data.json", BASE_CALLBACK_SHA256))
    callbacks.update(profile=CALLBACK_PROFILE, animation_manifest_sha256=sha(outputs["export-inventory.json"]),
                     base_presnap_switch_callback_payload_sha256=BASE_CALLBACK_SHA256)
    callbacks["format_source"].update(clip_count=303, selected_frame_count=frame_count)
    outputs["c2-complete-original-callback-data.json"] = canonical(callbacks)
    impact = json.loads(pinned(base, "kernel/impact-presnap-switch-presentation.json", BASE_IMPACT_SHA256))
    impact["schema"] = "original-impact-complete-original303/v1"
    impact["contact_binding"].update(animation_manifest_sha256=sha(outputs["export-inventory.json"]),
        c2_payload_sha256=sha(outputs["c2-complete-original-callback-data.json"]),
        base_presnap_switch_catalog_sha256=BASE_IMPACT_SHA256)
    outputs["impact-complete-original-presentation.json"] = canonical(impact)
    output.mkdir(parents=True)
    for name, raw in outputs.items():
        target = output / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(raw)
    selected._verify_bundle(output, outputs)
    selected._require(pinned(base, "animation/export-inventory-presnap-switch.json", BASE_SHA256)
                      == original_raw, "Immutable282 inventory changed during preparation.")
    return dict(schema="original-complete303-animation-preparation/v1", profile=PROFILE,
                output=str(output), clips=303, selectedFrames=frame_count, addedClips=additions,
                inheritedManifestSha256=BASE_SHA256,
                files=[dict(path=name, bytes=len(raw), sha256=sha(raw))
                       for name, raw in sorted(outputs.items())],
                animationManifestSha256=sha(outputs["export-inventory.json"]),
                callbackSha256=sha(outputs["c2-complete-original-callback-data.json"]),
                impactSha256=sha(outputs["impact-complete-original-presentation.json"]),
                scriptsImplemented=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--base", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(prepare(args.source, args.base, args.output), indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
