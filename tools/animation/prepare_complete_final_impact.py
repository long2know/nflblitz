#!/usr/bin/env python3
"""Admit exact TD camera WORD operands and the closed complete-original sound cues."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import re

BASE = "dc4428dd86056f70a93ee027c952d39437c4c0c9b9f9a8f3679c3b4908d78c4b"
OPERANDS = (7849, 7981, 8026, 15723, 15763, 17279, 17331, 17494, 17567, 17666, 17889)


def prepare(source, base, bindings, output):
    raw = source.read_bytes()
    if hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest() != "28cef0d1082f2b0c9241930fdc8886de4c8f5438":
        raise ValueError("Original TD opcode source pin differs.")
    content = base.read_bytes()
    if hashlib.sha256(content).hexdigest() != BASE:
        raise ValueError("Actual complete-original base impact identity required.")
    result = json.loads(content)
    lines = raw.decode("ascii").splitlines()
    cues = {cue["line"]: cue for cue in result["animation_cues"]}
    sites = []
    for operand in OPERANDS:
        opcode = operand - 1
        original = cues[opcode]
        if original["kind"] != "camera" or lines[opcode - 1].strip() != ".byte\tANI_SETCAMBOT" or \
                re.fullmatch(r"\.word\s+" + original["arguments"][0], lines[operand - 1].strip()) is None:
            raise ValueError("Exact original TD camera opcode/WORD relationship changed.")
        extra = copy.deepcopy(original)
        extra["line"] = operand
        result["animation_cues"].append(extra)
        sites.append(dict(script=original["script"], opcode_line=opcode, operand_line=operand, table=original["arguments"][0]))
    sound = json.loads(bindings.read_bytes())
    if sound["contentSha256"] != "6d9b16d4974739c75e6e1fd9ff6e8fece93fbad4d2d6e624e74dceaba9979bc9":
        raise ValueError("The exact qualified supplemental sound metadata is required.")
    added = []
    for binding in sound["tuples"]:
        if binding["line"] in cues:
            continue
        result["animation_cues"].append(dict(script=binding["script"], line=binding["line"], kind="callback",
            arguments=[binding["function"], *binding["arguments"]],
            execution="actual ordered VM opcode required; no frame/velocity inference"))
        added.append(binding["line"])
    result["animation_cues"].sort(key=lambda cue: cue["line"])
    if len(result["animation_cues"]) != 508 or len(added) != 15:
        raise ValueError("Actual493 camera plus fifteen closed source sound identities required.")
    result["schema"] = "original-impact-complete-original303-final/v1"
    result["contact_binding"].update(base_complete_original_catalog_sha256=BASE,
        complete_original_td_camera_sites=sites, complete_original_sound_lines=sorted(added))
    encoded = (json.dumps(result, indent=2, sort_keys=True) + "\n").encode()
    if output.exists():
        raise ValueError("Choose a create-new final impact generation.")
    output.write_bytes(encoded)
    return dict(schema="original-complete-final-impact-preparation/v1",
                sha256=hashlib.sha256(encoded).hexdigest(), bytes=len(encoded), cues=508,
                extraCameraOperandSites=sites, extraSoundLines=sorted(added), sourcePin="28cef0d1082f2b0c9241930fdc8886de4c8f5438")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("source", "base", "bindings", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(prepare(args.source, args.base, args.bindings, args.output), indent=2))
