#!/usr/bin/env python3
"""Extend the immutable469 impact sites with the exact eight original C2 speech sites."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sys

spec = importlib.util.spec_from_file_location("c2_canonical_profile", Path(__file__).with_name("prepare_c2_profile.py"))
profile = importlib.util.module_from_spec(spec)
spec.loader.exec_module(profile)


def prepare(base, callbacks, plyrseq, programs, destination):
    def read(path, pin):
        data = profile.selected._read_bytes(profile.selected._absolute(path), 4 * 1024 * 1024)
        profile.require(profile.sha(data) == pin, "Explicit original/C2 metadata content pin differs.")
        return json.loads(data)
    catalog = read(base, "738d712b6f80d8a9456338548e7bbce1fdf88baa46f342a7c45efffd02a8f23a")
    data = read(callbacks, profile.DATA_SHA)
    raw = profile.selected._read_bytes(profile.selected._absolute(plyrseq), 4 * 1024 * 1024)
    profile.require(profile.selected.reader.wan.git_blob_sha1(raw) == profile.SOURCE_PINS["plyrseq.c"],
                    "Pinned plyrseq.c changed.")
    program_bytes = profile.selected._read_bytes(profile.selected._absolute(programs), 1024 * 1024)
    profile.require(profile.sha(program_bytes) == "0935d54982b80f40f143033c3543837601148968175c0f33210e051f8e16a5cc",
                    "Actual frozen C2 programs are required.")
    lines = raw.decode("ascii").splitlines()
    existing = {cue["line"]: cue for cue in catalog["animation_cues"]}
    added = []
    for binding in data["vm_bindings"]:
        line = binding["line"]
        match = re.fullmatch(r"\.word\s+(\w+),(-?\d+),(-?\d+),(-?\d+)", lines[line - 1].strip())
        profile.require(match is not None and match[1] == binding["function"] and
                        [int(match[n]) for n in (2, 3, 4)] == binding["arguments"],
                        "C2 original callback operands differ.")
        arguments = [match[n] for n in (1, 2, 3, 4)]
        if line in existing:
            profile.require(existing[line]["script"] == binding["script"] and existing[line]["arguments"] == arguments,
                            "Existing original hit7538 must remain unchanged.")
        else:
            profile.require(binding["kind"] == "TauntSpeech", "Unexpected new original callback kind.")
            catalog["animation_cues"].append({"script": binding["script"], "line": line, "kind": "callback",
                "arguments": arguments, "execution": "actual ordered C2 source callback; selection occurs in Core, not presentation"})
            added.append(line)
    profile.require(len(added) == 8 and len(catalog["animation_cues"]) == 477, "Exact469+8 C2 closure required.")
    binding = catalog["contact_binding"]
    binding.update(legacy_animation_manifest_sha256=binding["animation_manifest_sha256"],
                   legacy_programs_sha256=binding["programs_sha256"],
                   animation_manifest_sha256=profile.ANIMATION_SHA, programs_sha256=profile.sha(program_bytes),
                   c2_payload_sha256=profile.DATA_SHA)
    binding["new_callback_lines"] = sorted(binding["new_callback_lines"] + added)
    catalog["schema"] = "original-impact-contact281-c2/v1"
    catalog["animation_cues"].sort(key=lambda cue: cue["line"])
    catalog["qualifications"] += [
        "Explicit original281 C2 bank-entry profile; all469 old sites, tables and original camera math are unchanged.",
        "Eight real taunt_speech sites added; hit7538 retains its original site and uses explicit HitFunctionExtension dispatch.",
        "Native whistle events have no animation cue or synthetic actor/site; raw output transport is not DSP playback.",
    ]
    encoded = (json.dumps(catalog, sort_keys=True, indent=2) + "\n").encode()
    destination = profile.selected._absolute(destination)
    profile.require(destination.is_relative_to(profile.ROOT / ".squad/.scratch"), "Original catalogs must remain ignored/local.")
    profile.selected._no_symlinks(destination)
    if destination.exists():
        profile.selected._verify_bundle(destination, {"impact-presentation.json": encoded})
    else:
        destination.mkdir(parents=True)
        with (destination / "impact-presentation.json").open("xb") as stream:
            stream.write(encoded)
    return {"schema": catalog["schema"], "sha256": profile.sha(encoded), "sites": 477,
            "animation_manifest_sha256": profile.ANIMATION_SHA, "c2_data_sha256": profile.DATA_SHA,
            "C2_programs_sha256": profile.sha(program_bytes), "new_speech_sites": sorted(added)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("base-catalog", "callback-data", "plyrseq", "programs", "destination"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(prepare(args.base_catalog, args.callback_data, args.plyrseq, args.programs, args.destination), indent=2))
        return 0
    except (OSError, ValueError, KeyError) as error:
        print("error: " + str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
