#!/usr/bin/env python3
"""Extend the exact private558 M1 bundle by five authenticated pre-snap-switch entries."""
import argparse
import json
from pathlib import Path
import sys

from prepare import Bundle, HERE, PROJECT, PINS, SOURCE, digest, portable, read, require

sys.path.insert(0, str(PROJECT / "tests/parity"))
from kernel_sources import (PRESNAP_ANIMATION_SHA, PRESNAP_DATA_SHA, PRESNAP_HOP_SHA,
                            PRESNAP_IMPACT_SHA, PRESNAP_MOVEMENT_SHA, successor_hashes)

PROFILE = "original-teams-contact282-c2-m1-presnap-switch"
BASE_MANIFEST_SHA256 = "d7f8324d73bdf01c686c9b09b7bca2defe502b7accf58ab8335e59e0fb210134"
C2_PROGRAMS_SHA256 = "0935d54982b80f40f143033c3543837601148968175c0f33210e051f8e16a5cc"
NEW_ENTRIES = {
    "animations": "animation/export-inventory-presnap-switch.json",
    "callbacks": "kernel/c2-presnap-switch-callback-data.json",
    "impact": "kernel/impact-presnap-switch-presentation.json",
    "settings": "settings-presnap-switch.json",
}


def prepare(source, expected_manifest, profile_inputs, impact, settings, output):
    source, profile_inputs, impact, settings, output = (
        p.absolute() for p in (source, profile_inputs, impact, settings, output))
    for input_path in (source, profile_inputs, impact.parent, settings.parent):
        require(output != input_path and input_path not in output.parents and output not in input_path.parents,
                "Pre-snap switch output must not overlap an immutable input.")
    raw = read(source, "manifest.json", expected_manifest)
    require(expected_manifest == BASE_MANIFEST_SHA256, "The explicit approved558 base manifest pin is required.")
    original = json.loads(raw)
    require(original["schema"] == "blitz-portable-source-player/v1" and
            original["profile"] == "original-teams-contact281-c2-m1" and
            original["sourceRevision"] == SOURCE and len(original["files"]) == 558 and
            all(name in original["entrypoints"] for name in ("normalCamera", "possessionSound", "freeBall")),
            "The complete pinned558 historical M1 player bundle is required.")
    expected_files = {portable(pin["path"]) for pin in original["files"]} | {"manifest.json"}
    actual_files = {p.relative_to(source).as_posix() for p in source.rglob("*") if p.is_file()}
    require(len(expected_files) == 559 and actual_files == expected_files and
            not any(p.is_symlink() for p in source.rglob("*")), "Unpinned/aliased/symlinked base player inputs.")
    inputs_before = successor_hashes(profile_inputs)
    programs = PROJECT / "Assets/Scripts/Gameplay"
    require(digest((programs / "SourceAnimationPrograms.Movement.cs").read_bytes()) == PRESNAP_MOVEMENT_SHA and
            digest((programs / "SourceAnimationPrograms.C2.cs").read_bytes()) == C2_PROGRAMS_SHA256,
            "Source program generation changed; a new U1 freeze and S1 regeneration are required.")
    old_settings = json.loads((HERE / "match-settings.json").read_bytes())
    new_settings = json.loads(read(settings.parent, settings.name, digest(
        (HERE / "match-settings.presnap-switch.json").read_bytes())))
    require(old_settings["station_layout"] == 2 and new_settings["station_layout"] == 4 and
            {**new_settings, "station_layout": 2} == old_settings,
            "Only StationLayout TwoStation->FourStation may change in the single-human default.")
    bundle = Bundle()
    for pin in original["files"]:
        data = bundle.add(source, pin["path"], pin["sha256"])
        require(len(data) == pin["bytes"], "Inherited byte count differs: " + pin["path"])
    for entry, expected in (("animations", PINS["animations"]), ("callbacks", PINS["callbacks"]),
                            ("impact", PINS["impact"]), ("sound", PINS["sound"]), ("hit", PINS["hit"])):
        require(digest(bundle.files[original["entrypoints"][entry]]) == expected,
                "Historical input identity differs: " + entry)
    inventory = bundle.document(profile_inputs, "animation282/export-inventory.json", PRESNAP_ANIMATION_SHA,
                                NEW_ENTRIES["animations"])
    base_inventory = json.loads(bundle.files[original["entrypoints"]["animations"]])
    old_clips = {clip["runtime_name"]: clip for clip in base_inventory["clips"]}
    new_clips = {clip["runtime_name"]: clip for clip in inventory["clips"]}
    require(set(new_clips) == set(old_clips) | {"M_HOPCLP"} and
            all(new_clips[name] == old for name, old in old_clips.items()),
            "All281 inherited selected clip records must remain byte-identical by identity.")
    for clip in inventory["clips"]:
        data = read(profile_inputs / "animation282", clip["path"], clip["sha256"])
        destination = "animation/" + clip["path"]
        if destination in bundle.files:
            require(bundle.files[destination] == data, "Reused animation bytes differ: " + destination)
        else:
            require(clip["runtime_name"] == "M_HOPCLP" and clip["sha256"] == PRESNAP_HOP_SHA and
                    clip["selected_frames"] == 35 and len(data) == 58307, "Only the authentic35-pose hop may be added.")
            bundle.add(profile_inputs / "animation282", clip["path"], PRESNAP_HOP_SHA, destination)
    bundle.add(profile_inputs, "c2-callback-data.json", PRESNAP_DATA_SHA, NEW_ENTRIES["callbacks"])
    bundle.add(impact.parent, impact.name, PRESNAP_IMPACT_SHA, NEW_ENTRIES["impact"])
    bundle.add(settings.parent, settings.name, digest((HERE / "match-settings.presnap-switch.json").read_bytes()),
               NEW_ENTRIES["settings"])
    require(len(bundle.files) == 563, "The successor must add exactly five files, never rewrite inherited entries.")
    entries, origins = dict(original["entrypoints"]), dict(original["origins"])
    entries.update(NEW_ENTRIES)
    origins.update({
        "preSnapSwitchBaseManifestSha256": expected_manifest,
        "animation": "actual production282 library: all281 inherited pins plus original M_HOPCLP35/15",
        "preSnapSwitch": "Exact authenticated15-binding callback/bootstrap generation; single physical station0/Home, FourStation layout.",
        "preSnapSwitchMovementProgramsSha256": PRESNAP_MOVEMENT_SHA,
        "c2ProgramsSha256": C2_PROGRAMS_SHA256,
    })
    manifest, manifest_hash = bundle.persist(output, entries, origins, profile=PROFILE)
    pins = {pin["path"]: pin for pin in manifest["files"]}
    require(all(pins[pin["path"]] == pin for pin in original["files"]), "Every558 inherited pin must be retained.")
    require(successor_hashes(profile_inputs) == inputs_before and
            (source / "manifest.json").read_bytes() == raw, "Frozen producer inputs changed during preparation.")
    return dict(schema="source-player-presnap-switch-data/v1", profile=PROFILE,
                manifestSha256=manifest_hash, baseManifestSha256=expected_manifest, fileCount=563,
                originalPinsPreserved=True, addedFiles=sorted(set(pins) - (expected_files - {"manifest.json"})),
                animationSha256=PRESNAP_ANIMATION_SHA, callbacksSha256=PRESNAP_DATA_SHA,
                impactSha256=PRESNAP_IMPACT_SHA, movementProgramsSha256=PRESNAP_MOVEMENT_SHA,
                settingsSha256=pins[NEW_ENTRIES["settings"]]["sha256"], formatMigration=False)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--input-manifest-sha256", required=True)
    parser.add_argument("--profile-inputs", type=Path, required=True)
    parser.add_argument("--impact", type=Path, required=True)
    parser.add_argument("--settings", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        print(json.dumps(prepare(args.input, args.input_manifest_sha256, args.profile_inputs,
                                 args.impact, args.settings, args.output), indent=2))
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(1, "Pre-snap switch player data rejected: " + str(error) + "\n")


if __name__ == "__main__":
    main()
