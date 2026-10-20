#!/usr/bin/env python3
"""Export the narrow original interception-speech operands; no original tables enter Git."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT / "tools/rosters"), str(ROOT / "tools/assets")]
from c_source import CData

INPUTS = {
    "sounds.c": ("f5ac3fd2cec5c20998bb613066a355e0d0d4d0e9", 59853),
    "include/sndcalls.h": ("69f6c483502fb689eb1991169e0e31858378c763", 12978),
}
SOUND_HEADER = ("b67e1f1d07cc22faf3f4c9e0269e091460750a46", 9795)


def pinned(root, path, pin):
    candidate = root / path
    if candidate.is_symlink() or not candidate.is_file():
        raise ValueError("A regular explicit original input is required: " + path)
    raw = candidate.read_bytes()
    blob = hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()
    if (blob, len(raw)) != pin:
        raise ValueError("Wrong actual original Git blob/size: " + path)
    return raw


def convert(source, sound_header):
    raw = {name: pinned(source, name, pin) for name, pin in INPUTS.items()}
    raw["goose/sound.h"] = pinned(sound_header.parent, sound_header.name, SOUND_HEADER)
    env = CData(raw["goose/sound.h"].decode("ascii")).enums().expressions.macros
    header = CData(raw["include/sndcalls.h"].decode("ascii"), env).enums()
    sounds = CData(raw["sounds.c"].decode("ascii"), header.expressions.macros).enums()
    rows, _ = sounds.array("intercepted_sp")
    ids = [int(sounds.expressions.evaluate(row.raw)) for row in rows]
    if len(ids) != 5:
        raise ValueError("The original weighted interception table changed.")
    body = sounds.function("intercepted_ball_sp")[1]
    if not re.search(r"snd_scall_bank\s*\(\s*ancr_swaped_bnk_str\s*,\s*intercepted_sp\s*\[\s*randrng", body):
        raise ValueError("Original interception producer no longer matches its declared operands.")
    values = [int(header.expressions.evaluate(name)) for name in ("ANCR_VOL2", "LVL4")]
    payload = b"CMSG\x01\0\0\0" + struct.pack("<8I", *ids, values[0], 127, values[1])
    receipt = dict(schema="source-possession-speech/v1", payloadSha256=hashlib.sha256(payload).hexdigest(),
                   inputs={name: hashlib.sha256(value).hexdigest() for name, value in raw.items()},
                   sourceRevision="89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
                   toolsRevision="1792293aaaea00a919b59e8c05716978e85b4333",
                   source="sounds.c:308,829-832; include/sndcalls.h:23,420; goose/sound.h:72,80",
                   pcmOrDspPlayback=False)
    return payload, receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--sound-header", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists() or ROOT / ".squad/.scratch" not in output.parents:
        parser.error("Choose a NEW ignored worktree-local output.")
    if subprocess.run(["git", "check-ignore", "-q", str(output)], cwd=ROOT).returncode:
        parser.error("Original derived sound data must stay ignored.")
    payload, receipt = convert(args.source_root, args.sound_header)
    output.mkdir(parents=True)
    (output / "possession-speech.bytes").write_bytes(payload)
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps(receipt))


if __name__ == "__main__":
    main()
