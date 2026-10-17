#!/usr/bin/env python3
"""Bind the complete original impact catalog to exact C1 source instruction sites."""

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sys

SPEC = importlib.util.spec_from_file_location("contact_profile", Path(__file__).with_name("prepare_contact_profile.py"))
profile = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(profile)
BASE_IMPACT_SHA = "794b1fa2865733677d08d5c465dad6a002966750a7d0523138f61f3a25be1fb3"
PLYRSEQ_BLOB = "28cef0d1082f2b0c9241930fdc8886de4c8f5438"
require = profile.require


def instruction_sites(program_bytes):
    result, script = [], None
    for line in program_bytes.decode("utf-8").splitlines():
        owner = re.search(r'programs\["([A-Za-z_]\w*)"\]', line)
        if owner:
            script = owner[1]
        match = re.search(r'SourceAnimationOp\.(CameraCue|ShakerCue|PresentationCallback)\b.*?sourceLine: (\d+)', line)
        if not match:
            continue
        text = re.search(r'text: "([^"]+)"', line)
        operands = {key: int(value) for key, value in re.findall(r'\b(a|b|c): (-?\d+)\b', line)}
        result.append({"script": script, "line": int(match[2]),
                       "kind": {"CameraCue": "camera", "ShakerCue": "shaker",
                                "PresentationCallback": "callback"}[match[1]],
                       "text": text[1] if text else None, "operands": operands})
    require(len(result) == 360, "Expected the frozen C1 273 callbacks /62 shakers /25 cameras")
    require({kind: sum(site["kind"] == kind for site in result) for kind in ("callback", "shaker", "camera")}
            == {"callback": 273, "shaker": 62, "camera": 25}, "C1 presentation instruction inventory changed")
    return result


def prepare(base_catalog, plyrseq, contact_programs, animation_manifest, destination):
    base = profile.read_json(base_catalog, BASE_IMPACT_SHA)
    manifest_bytes = profile.selected._read_bytes(profile.selected._absolute(animation_manifest), 1024 * 1024)
    manifest = json.loads(manifest_bytes)
    require(manifest.get("profile") == profile.PROFILE and len(manifest["clips"]) == 272
            and manifest.get("selected_base_manifest_sha256") == profile.BASE_SHA
            and manifest.get("supplemental_manifest_sha256") == profile.EXTRA_SHA,
            "Impact generation requires the explicit new production272 manifest, not historical157")
    script_bytes = profile.selected._read_bytes(profile.selected._absolute(plyrseq), 4 * 1024 * 1024)
    require(profile.selected.reader.wan.git_blob_sha1(script_bytes) == PLYRSEQ_BLOB,
            "Original plyrseq.c Git blob differs")
    original_input = next(item for item in base["inputs"] if item["path"] == "plyrseq.c")
    require(original_input["sha256"] == profile.sha(script_bytes), "Original catalog/source identity differs")
    original = script_bytes.decode("latin1").splitlines()
    program_bytes = profile.selected._read_bytes(profile.selected._absolute(contact_programs), 1024 * 1024)
    sites = instruction_sites(program_bytes)
    lookup = {(cue["script"], cue["line"], cue["kind"]): cue for cue in base["animation_cues"]}
    camera_sites, new_callbacks = [], []
    for site in sites:
        key = (site["script"], site["line"], site["kind"])
        raw = original[site["line"] - 1].strip()
        if site["kind"] == "camera":
            require(re.fullmatch(r"\.word\s+" + re.escape(site["text"]), raw) is not None,
                    "C1 CSI operand does not identify its exact original .word line")
            previous = site["line"] - 2
            while previous >= 0 and (not original[previous].strip() or original[previous].strip().startswith("#")):
                previous -= 1
            require(original[previous].strip() == ".byte\tANI_SETCAMBOT" or
                    re.fullmatch(r"\.byte\s+ANI_SETCAMBOT", original[previous].strip()) is not None,
                    "C1 CSI operand has no exact preceding original camera opcode")
            opcode = lookup.get((site["script"], previous + 1, "camera"))
            require(opcode is not None and opcode["arguments"] == [site["text"]],
                    "C1 CSI opcode/table differs from the complete original catalog")
            camera_sites.append({"script": site["script"], "opcode_line": previous + 1,
                                 "operand_line": site["line"], "table": site["text"]})
            require(key not in lookup, "A C1 camera operand unexpectedly aliases an old source site")
            added = dict(opcode, line=site["line"],
                         execution="C1 emitted context names this exact .word CSI operand; canonical .byte site is recorded in contact_binding")
            base["animation_cues"].append(added)
            lookup[key] = added
        elif site["kind"] == "callback":
            match = re.fullmatch(r"\.word\s+([^#]+?)(?:\s*#.*)?", raw)
            require(match is not None, "C1 callback must retain an actual original .word source site")
            arguments = [value.strip() for value in match[1].split(",")]
            require(len(arguments) == 4 and arguments[0] == site["text"],
                    "C1 callback function/operand shape differs from original source")
            for index, operand in enumerate(("a", "b", "c"), 1):
                if re.fullmatch(r"-?\d+", arguments[index]):
                    require(int(arguments[index]) == site["operands"].get(operand, 0),
                            "C1 callback literal/sentinel differs from its original source operand")
            if key not in lookup:
                require(arguments[0] in ("taunt_speech", "tackle_spch"),
                        "Unexpected new callback implementation; do not qualify an invented consumer")
                added = {"script": site["script"], "line": site["line"], "kind": "callback",
                         "arguments": arguments,
                         "execution": "actual ordered original callback; speech selection/consumer remains unsupported"}
                base["animation_cues"].append(added)
                lookup[key] = added
                new_callbacks.append(site["line"])
            require(lookup[key]["arguments"] == arguments, "C1 callback identity differs from original catalog")
        else:
            require(key in lookup and lookup[key]["arguments"] ==
                    [site["operands"].get("a", 0), site["operands"].get("b", 0)],
                    "C1 shaker source site/operands differ from complete original catalog")
    require(len(camera_sites) == 25 and len(new_callbacks) == 28 and len(base["animation_cues"]) == 469,
            "Expected exact C1 coverage: old416 +25 explicit CSI operand sites +28 original speech sites")
    base["schema"] = "original-impact-contact272/v1"
    base["contact_binding"] = {
        "animation_manifest_sha256": profile.sha(manifest_bytes),
        "programs_sha256": profile.sha(program_bytes), "base_catalog_sha256": BASE_IMPACT_SHA,
        "camera_sites": sorted(camera_sites, key=lambda site: site["operand_line"]),
        "new_callback_lines": sorted(new_callbacks),
    }
    base["animation_cues"].sort(key=lambda cue: cue["line"])
    base["qualifications"] += [
        "Explicit C1 production272 profile. The complete old416 sites/tables/shaker values are unchanged.",
        "C1 camera contexts use exact .word operand lines; contact_binding explicitly retains each original .byte opcode site.",
        "The28 additional original taunt_speech/tackle_spch sites are metadata, NOT implemented speech/DSP/effects.",
        "No generic contact notification selects a shot, synthesizes an effect, or resolves unresolved raw contact sound calls.",
    ]
    data = (json.dumps(base, sort_keys=True, indent=2) + "\n").encode("utf-8")
    destination = profile.selected._absolute(destination)
    scratch = profile.selected._absolute(profile.selected.reader.wan.SCRATCH)
    require(destination != scratch and destination.is_relative_to(scratch),
            "Original impact resources must remain ignored/local beneath .squad/.scratch/")
    profile.selected._no_symlinks(destination)
    outputs = {"impact-presentation.json": data}
    if destination.exists():
        profile.selected._verify_bundle(destination, outputs)
    else:
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.mkdir()
        with (destination / "impact-presentation.json").open("xb") as stream:
            stream.write(data)
        profile.selected._verify_bundle(destination, outputs)
    return {"schema": base["schema"], "sha256": profile.sha(data),
            "animation_manifest_sha256": profile.sha(manifest_bytes), "programs_sha256": profile.sha(program_bytes),
            "base_catalog_sha256": BASE_IMPACT_SHA, "C1_presentation_sites": len(sites),
            "camera_operand_bindings": len(camera_sites), "new_unimplemented_speech_sites": len(new_callbacks),
            "complete_animation_sites": len(base["animation_cues"]), "contact_camera_sites": len(base["contact_camera_sites"]),
            "camera_tables": len(base["tables"]), "native_or_audio_executed": False}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("base-catalog", "plyrseq", "contact-programs", "animation-manifest", "destination"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        print(json.dumps(prepare(args.base_catalog, args.plyrseq, args.contact_programs,
                                 args.animation_manifest, args.destination), indent=2))
        return 0
    except (OSError, ValueError, KeyError, TypeError, IndexError) as error:
        print("error: " + str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
