#!/usr/bin/env python3
"""Prepare only the authenticated M_HOPCLP/M1 pre-snap successor, offline and private."""
import argparse
import copy
import importlib.util
import json
from pathlib import Path
import re
import sys

spec = importlib.util.spec_from_file_location("presnap_legacy_c2", Path(__file__).with_name("prepare_c2_profile.py"))
c2 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(c2)
selected, audio, require, sha = c2.selected, c2.audio, c2.require, c2.sha
ROOT = c2.ROOT
PROFILE = "contact282-c2-m1-presnap-switch"
STABLE_ID = "original-teams-" + PROFILE
ENTRY_PROFILE = "OriginalRelease57Hz-C2-M1-PreSnapSwitch-InitSoundBanks-v1"
IMPACT_SCHEMA = "original-impact-contact282-c2-m1-presnap-switch/v1"
MOVEMENT_SHA = "2f2d888ff89a6a89d9b72d19590115a71cce8ff5c7c7d34b8c313205cf0358ef"
C2_PROGRAMS_SHA = "0935d54982b80f40f143033c3543837601148968175c0f33210e051f8e16a5cc"
BASE_IMPACT_SHA = "2fee43a7a20dad06d8846b1d558a69d8648041368091ab12677c941e19b66cd1"
HIT_SOURCE_SHA = "3086a45961bd1c4ce2bab8134822c74185aff3aeb5da941eccf8fff61c177fcf"
HOP_INVENTORY_SHA = "ec3a334976e0dc0b13accc9b2b9cff098e61c800bd2eba1bf02f831c03f84cef"
HOP_CLIP_SHA = "df5e69344d049646e07143c4cbfec0d577e442fe63152099967436fa91c4e2bb"
HOP_SOURCE_SHA = "53d0c56e06680e594de6af1771108c31cda390aa0abac13f092cecbbcff3dbad"
ANIMATION_SHA = "50aa432ff9b332ec9087133f5bccb43ae18e1ca7c0bf7830cbdd675d3e024046"
DATA_SHA = "e9a2ccbfffda0e83ae9ba409e4fbfd6f6c6f074ebd024aa30367d9d06c29c653"
IMPACT_SHA = "456d525aa75ba343942613793cc437d6732f0b16d972a981537929daece9f3d7"
HOP_BINDING = {"path": "plyrseq.c", "script": "m_hopclp_anim", "function": "attack_snd",
               "kind": "AttackSoundFixed", "line": 3409, "arguments": [127, 0, 0]}
ATTACK_SOUND = {"path": "sounds.c", "function": "attack_snd", "line": 1638,
                "bank_source": "plyr_bnk_str", "playlist_source": "snd1",
                "volume": 160, "pan": 127, "human_priority": 10, "drone_priority": 15}
NEW_CALLBACK_FIELDS = {"base_m1_animation_manifest_sha256", "base_m1_callback_payload_sha256",
                       "presnap_switch_animation_manifest_sha256",
                       "presnap_switch_movement_programs_sha256", "attack_sound"}
NEW_INVENTORY_FIELDS = {"base_m1_animation_manifest_sha256", "presnap_switch_manifest_sha256"}


def read(path, pin=None, limit=4 * 1024 * 1024):
    content = selected._read_bytes(selected._absolute(path), limit)
    require(pin is None or sha(content) == pin, "Explicit trusted content pin differs: " + str(path))
    return content


def document(content):
    return json.loads(content, object_pairs_hook=selected._unique_object, parse_constant=selected._nonfinite)


def programs(path):
    content = read(path, MOVEMENT_SHA, 1024 * 1024)
    read(ROOT / "Assets/Scripts/Gameplay/SourceAnimationPrograms.C2.cs", C2_PROGRAMS_SHA, 1024 * 1024)
    return content


def destination_root(destination, inputs):
    destination = selected._absolute(destination)
    selected._no_symlinks(destination)
    scratch = ROOT / ".squad/.scratch"
    require(destination != scratch and destination.is_relative_to(scratch),
            "All original/converted outputs must remain beneath this checkout's ignored scratch.")
    for source in inputs:
        source = selected._absolute(source)
        selected._no_symlinks(source)
        require(not destination.is_relative_to(source) and not source.is_relative_to(destination),
                "Input/output roots overlap.")
    require(not destination.exists(), "Choose a create-new output root; never overwrite a prior generation.")
    return destination


def persist(destination, outputs):
    destination.mkdir(parents=True)
    for name, content in outputs.items():
        selected._relative_path(name)
        path = destination / name
        path.parent.mkdir(parents=True, exist_ok=True)
        selected._no_symlinks(path)
        with path.open("xb") as stream:
            stream.write(content)
    selected._verify_bundle(destination, outputs)
    return {name: {"bytes": len(content), "sha256": sha(content)}
            for name, content in sorted(outputs.items())}


def validate_hop(inventory, clip):
    require(inventory.get("schema") == selected.INVENTORY_SCHEMA and len(inventory.get("clips", [])) == 1,
            "Hop export requires exactly one selected clip.")
    entry = inventory["clips"][0]
    sequence = clip.get("sequence", {})
    require(entry.get("runtime_name") == sequence.get("runtime_name") == "M_HOPCLP" and
            entry.get("source_frames") == entry.get("selected_frames") ==
            sequence.get("source_frame_count") == sequence.get("selected_frame_count") == 35 and
            sequence.get("limb_count") == 15 and sequence.get("authoring_name") == "m_hopclp",
            "Actual M_HOPCLP35/fifteen-joint source identity is required.")
    require(entry.get("selected_source_frame_indices") ==
            sequence.get("selected_source_frame_indices") == list(range(35)) and
            entry.get("source_first") == 0 and entry.get("source_last") == 34,
            "M_HOPCLP selected map must be exactly original authoring0..34.")
    provenance = clip.get("provenance", {})
    require(provenance.get("path") == entry.get("source_path") == "anim/misc2.wan" and
            provenance.get("git_blob_sha1") == entry.get("source_git_blob_sha1") ==
            "5b3083a71b1a7729d508b804712f5a754afaca5b" and
            provenance.get("sha256") == entry.get("source_sha256") == HOP_SOURCE_SHA,
            "M_HOPCLP original WAN source content differs.")
    require(sequence.get("chunk_number") == 6 and sequence.get("chunk_byte_offset") == 92472 and
            sequence.get("chunk_end_byte_offset") == 118456 and sequence.get("chunk_reference") == 0 and
            clip.get("root_translation", {}).get("runtime_scale") == 0.12898,
            "M_HOPCLP original chunk/scale differs.")
    frames = clip.get("frames", [])
    require(len(clip.get("joints", [])) == 15 and len(frames) == 35 and
            all(frame.get("source_frame_index") == n and frame.get("exported_frame_index") == n and
                len(frame.get("rotations_xyzw", [])) == 15 for n, frame in enumerate(frames)),
            "M_HOPCLP complete original selected pose mapping is required.")
    quality = clip.get("validation", {})
    require(quality.get("validated_source_frames") == 35 and quality.get("validated_limb_frames") == 525 and
            quality.get("matrix_error_limit") == 1e-4 and
            0 <= quality.get("max_matrix_orthogonality_error", 1) <= 1e-4 and
            0 <= quality.get("max_matrix_determinant_error", 1) <= 1e-4 and
            0 <= quality.get("max_quaternion_norm_error", 1) <= 1e-12,
            "M_HOPCLP actual selected exporter quality checks failed.")


def validate_union(inventory):
    clips = inventory.get("clips", [])
    require(inventory.get("profile") == STABLE_ID and len(clips) == 282 and
            len({entry["runtime_name"] for entry in clips}) == 282 and
            sum(entry["selected_frames"] for entry in clips) == 26265,
            "Exact successor282/26265 profile/counts required.")
    require(inventory.get("base_m1_animation_manifest_sha256") == c2.ANIMATION_SHA and
            inventory.get("presnap_switch_manifest_sha256") == HOP_INVENTORY_SHA and
            inventory.get("producer_policy") == "immutable-source281-plus-single-M_HOPCLP-presnap-switch/v1",
            "Successor animation lineage differs.")
    hop = [entry for entry in clips if entry["runtime_name"] == "M_HOPCLP"]
    require(len(hop) == 1 and hop[0]["sha256"] == HOP_CLIP_SHA and
            hop[0]["selected_source_frame_indices"] == list(range(35)),
            "Missing or changed authentic M_HOPCLP selected clip.")
    legacy = copy.deepcopy(inventory)
    for name in NEW_INVENTORY_FIELDS:
        legacy.pop(name)
    legacy.update(profile="original-teams-contact281-c2",
                  producer_policy="immutable-source272-plus-disjoint-source9-c2/v1",
                  clips=[entry for entry in clips if entry["runtime_name"] != "M_HOPCLP"])
    require(sha(selected.reader.encode_clip(legacy).encode()) == c2.ANIMATION_SHA,
            "All281 inherited inventory entries and converter qualifications must remain byte-identical.")


def validate_callbacks(data, inventory_sha):
    require(data.get("profile") == ENTRY_PROFILE and data.get("animation_manifest_sha256") == inventory_sha and
            data.get("base_m1_animation_manifest_sha256") == c2.ANIMATION_SHA and
            data.get("base_m1_callback_payload_sha256") == c2.M1_DATA_SHA and
            data.get("presnap_switch_animation_manifest_sha256") == HOP_INVENTORY_SHA and
            data.get("presnap_switch_movement_programs_sha256") == MOVEMENT_SHA,
            "Explicit successor callback/profile/library/Movement generation differs.")
    require(data.get("format_source", {}).get("clip_count") == 282 and
            data["format_source"].get("selected_frame_count") == 26265,
            "Successor callbacks require exact282/26265 inventory counts.")
    require(data.get("programs") == list(c2.SCRIPTS) + ["m_hopclp_anim"] and
            len(data.get("vm_bindings", [])) == 15 and data["vm_bindings"][-1] == HOP_BINDING,
            "Exact fifteen-binding generation and registered hop tuple are required.")
    require(data.get("attack_sound") == ATTACK_SOUND,
            "Actual positive attack_snd1638 human/drone descriptor differs.")
    legacy = copy.deepcopy(data)
    for name in NEW_CALLBACK_FIELDS:
        legacy.pop(name)
    legacy.update(profile=c2.M1_ENTRY_PROFILE, animation_manifest_sha256=c2.ANIMATION_SHA,
                  programs=list(c2.SCRIPTS), vm_bindings=legacy["vm_bindings"][:-1])
    legacy["format_source"].update(clip_count=281, selected_frame_count=26230)
    require(sha(audio.canonical(legacy)) == c2.M1_DATA_SHA,
            "All fourteen old bindings, nine call templates, tables and bank order must remain byte-identical.")


def source_attack(sources):
    sound = sources["sounds.c"].decode("ascii")
    function = audio.function_source(sound, "attack_snd")
    calls = audio.call_sites(function)
    require(function["line"] == 1623 and len(calls) == 4 and calls[1] ==
            {"line": 1638, "args": ["plyr_bnk_str", "snd1", "VOLUME2", "127", "priority"]},
            "Actual positive attack_snd source call changed.")
    clean = re.sub(r"\s+", "", function["clean"])
    require("if(ISHUMAN(ppdata))priority=PRIORITY_LVL2;elsepriority=PRIORITY_LVL1;" in clean and
            "elseif(snd1>0)snd_scall_bank(plyr_bnk_str,snd1,VOLUME2,127,priority);" in clean and
            "if(snd2>0)" in clean and "if(snd3>0)" in clean,
            "Actual attack_snd positive/human/drone branches changed.")
    definitions = audio.definitions(sources["include/sndcalls.h"].decode("ascii"))
    require([audio.integer(name, definitions) for name in ("VOLUME2", "PRIORITY_LVL2", "PRIORITY_LVL1")] ==
            [160, 10, 15], "Use original priorities, never the flagged LVL2 macro.")
    lines = sources["plyrseq.c"].decode("ascii").splitlines()
    require(lines[3407].strip() == ".byte\tANI_CODE" and
            re.fullmatch(r"\.word\s+attack_snd,127,0,0", lines[3408].strip()) is not None and
            "m_hopclp_anim:" in "\n".join(lines[3398:3409]),
            "Original m_hopclp_anim3409 attack_snd127,0,0 tuple changed.")
    return copy.deepcopy(ATTACK_SOUND)


def prepare_profile(source_root, base_m1_inputs, hop_bundle, movement_programs, destination):
    destination = destination_root(destination, (source_root, base_m1_inputs, hop_bundle, movement_programs))
    movement = programs(movement_programs)
    source_root, base, hop = map(selected._absolute, (source_root, base_m1_inputs, hop_bundle))
    sources = {name: read(source_root / name) for name in c2.SOURCE_PINS}
    require(all(selected.reader.wan.git_blob_sha1(sources[name]) == pin
                for name, pin in c2.SOURCE_PINS.items()), "Authenticated original source blob differs.")
    attack = source_attack(sources)
    old_raw = read(base / "animation281/export-inventory.json", c2.ANIMATION_SHA)
    old = document(old_raw)
    old_data = read(base / "c2-callback-data.json", c2.M1_DATA_SHA)
    callbacks = document(old_data)
    hit = read(base / "m-qbfite-hit-extension.safn", c2.HIT_SHA)
    hit_source = read(base / "m-qbfite-hit-extension-source.json", HIT_SOURCE_SHA)
    inputs = {"animation281/export-inventory.json": old_raw, "c2-callback-data.json": old_data,
              "m-qbfite-hit-extension.safn": hit, "m-qbfite-hit-extension-source.json": hit_source}
    for name, content in list(inputs.items()):
        if name.endswith(".json") and name.endswith("-source.json"):
            continue
        inputs[name + ".sha256"] = read(base / (name + ".sha256"), sha((sha(content) + "\n").encode()))
    outputs = {}
    for entry in old["clips"]:
        selected._relative_path(entry["path"])
        name = "animation281/" + entry["path"]
        content = read(base / name, entry["sha256"], 8 * 1024 * 1024)
        require("animation282/" + entry["path"] not in outputs, "Duplicate inherited clip path.")
        inputs[name] = content
        outputs["animation282/" + entry["path"]] = content
    require(len(inputs) == 288, "The old M1 input requires its exact288-file closure.")
    selected._verify_bundle(base, inputs)
    hop_raw = read(hop / selected.MANIFEST, HOP_INVENTORY_SHA)
    clip_raw = read(hop / "clips/M_HOPCLP.json", HOP_CLIP_SHA, 8 * 1024 * 1024)
    selected._verify_bundle(hop, {selected.MANIFEST: hop_raw, "clips/M_HOPCLP.json": clip_raw})
    hop_inventory = document(hop_raw)
    validate_hop(hop_inventory, document(clip_raw))
    inventory = copy.deepcopy(old)
    inventory.update(profile=STABLE_ID, base_m1_animation_manifest_sha256=c2.ANIMATION_SHA,
                     presnap_switch_manifest_sha256=HOP_INVENTORY_SHA,
                     producer_policy="immutable-source281-plus-single-M_HOPCLP-presnap-switch/v1",
                     clips=sorted(old["clips"] + hop_inventory["clips"], key=lambda entry: entry["runtime_name"]))
    validate_union(inventory)
    inventory_raw = selected.reader.encode_clip(inventory).encode()
    callbacks.update(profile=ENTRY_PROFILE, animation_manifest_sha256=sha(inventory_raw),
                     base_m1_animation_manifest_sha256=c2.ANIMATION_SHA,
                     base_m1_callback_payload_sha256=c2.M1_DATA_SHA,
                     presnap_switch_animation_manifest_sha256=HOP_INVENTORY_SHA,
                     presnap_switch_movement_programs_sha256=MOVEMENT_SHA, attack_sound=attack)
    callbacks["format_source"].update(clip_count=282, selected_frame_count=26265)
    callbacks["programs"].append("m_hopclp_anim")
    callbacks["vm_bindings"].append(copy.deepcopy(HOP_BINDING))
    validate_callbacks(callbacks, sha(inventory_raw))
    callback_raw = audio.canonical(callbacks)
    require(sha(inventory_raw) == ANIMATION_SHA and sha(callback_raw) == DATA_SHA,
            "Fresh canonical successor bytes differ; never rebaseline a frozen generation.")
    outputs.update({"animation282/clips/M_HOPCLP.json": clip_raw,
                    "animation282/export-inventory.json": inventory_raw,
                    "c2-callback-data.json": callback_raw, "m-qbfite-hit-extension.safn": hit,
                    "m-qbfite-hit-extension-source.json": hit_source})
    for name in ("animation282/export-inventory.json", "c2-callback-data.json", "m-qbfite-hit-extension.safn"):
        outputs[name + ".sha256"] = (sha(outputs[name]) + "\n").encode()
    require(len(outputs) == 289, "Exact289-file successor producer closure required.")
    require(programs(movement_programs) == movement and
            all(read(source_root / name) == value for name, value in sources.items()),
            "Frozen source/Movement inputs changed during preparation.")
    return {"schema": "presnap-switch-profile-preparation/v1", "profile": STABLE_ID,
            "animation_manifest_sha256": sha(inventory_raw), "callback_payload_sha256": sha(callback_raw),
            "movement_programs_sha256": MOVEMENT_SHA, "clips": 282, "selected_frames": 26265,
            "bindings": 15, "files": persist(destination, outputs)}


def prepare_impact(base_catalog, callback_data, animation_manifest, movement_programs, destination):
    destination = destination_root(destination, (base_catalog, callback_data, animation_manifest, movement_programs))
    movement = programs(movement_programs)
    catalog = document(read(base_catalog, BASE_IMPACT_SHA))
    callback_raw, animation_raw = read(callback_data, DATA_SHA), read(animation_manifest, ANIMATION_SHA)
    callbacks, inventory = document(callback_raw), document(animation_raw)
    validate_union(inventory)
    validate_callbacks(callbacks, sha(animation_raw))
    require(catalog["schema"] == "original-impact-contact281-c2-m1/v1" and
            len(catalog["animation_cues"]) == 482 and
            len(catalog["contact_binding"]["camera_sites"]) == 25 and
            len(catalog["contact_binding"]["new_callback_lines"]) == 41,
            "Exact old M1 482-site/25-camera/41-speech catalog required.")
    sites = [site for site in catalog["animation_cues"] if site["line"] == 3409]
    require(len(sites) == 1 and sites[0]["script"] == "m_hopclp_anim" and
            sites[0]["kind"] == "callback" and sites[0]["arguments"] == ["attack_snd", "127", "0", "0"],
            "Original hop callback must already be present; never append a483rd site.")
    binding = catalog["contact_binding"]
    require(binding["programs_sha256"] == C2_PROGRAMS_SHA and
            binding["animation_manifest_sha256"] == c2.ANIMATION_SHA and
            binding["c2_payload_sha256"] == c2.M1_DATA_SHA, "Old M1 impact binding differs.")
    binding.update(animation_manifest_sha256=sha(animation_raw), c2_payload_sha256=sha(callback_raw),
                   presnap_switch_movement_programs_sha256=MOVEMENT_SHA,
                   base_m1_catalog_sha256=BASE_IMPACT_SHA,
                   base_m1_animation_manifest_sha256=c2.ANIMATION_SHA,
                   base_m1_callback_payload_sha256=c2.M1_DATA_SHA,
                   presnap_switch_manifest_sha256=HOP_INVENTORY_SHA)
    catalog["schema"] = IMPACT_SCHEMA
    catalog["qualifications"].append(
        "Explicit pre-snap282/15-binding successor; all482 sites,25 camera bindings,41 additional speech lines, tables and camera data are unchanged. Hop3409 already exists. C2 and Movement program pins are distinct; no PCM/native qualification.")
    encoded = audio.canonical(catalog)
    require(sha(encoded) == IMPACT_SHA, "Fresh rebound impact bytes differ from the frozen generation.")
    require(programs(movement_programs) == movement, "Frozen Movement program changed during impact preparation.")
    return {"schema": IMPACT_SCHEMA, "sites": 482, "camera_bindings": 25, "additional_speech_lines": 41,
            "animation_manifest_sha256": sha(animation_raw), "callback_payload_sha256": sha(callback_raw),
            "C2_programs_sha256": C2_PROGRAMS_SHA, "movement_programs_sha256": MOVEMENT_SHA,
            "files": persist(destination, {"impact-presentation.json": encoded})}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    profile = commands.add_parser("profile")
    for name in ("source-root", "base-m1-inputs", "hop-bundle", "movement-programs", "destination"):
        profile.add_argument("--" + name, type=Path, required=True)
    impact = commands.add_parser("impact")
    for name in ("base-catalog", "callback-data", "animation-manifest", "movement-programs", "destination"):
        impact.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        result = (prepare_profile(args.source_root, args.base_m1_inputs, args.hop_bundle,
                                  args.movement_programs, args.destination) if args.command == "profile" else
                  prepare_impact(args.base_catalog, args.callback_data, args.animation_manifest,
                                 args.movement_programs, args.destination))
        print(json.dumps({name: value for name, value in result.items() if name != "files"},
                         indent=2, sort_keys=True))
        return 0
    except (ValueError, OSError, KeyError, TypeError, UnicodeError) as error:
        print("error: " + str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
