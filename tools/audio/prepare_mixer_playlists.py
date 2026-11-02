#!/usr/bin/env python3
"""Extend exact original banks with executed2.33 implicit-global mixer programmes."""

import argparse
import json
import os
from pathlib import Path
import struct
import subprocess

import prepare as audio
import prepare_playlists as playlists

ROOT = Path(__file__).absolute().parents[2]
BASE = "a36dd1938a305114bb8699bec70395466ea2707fcd8f8d2aa8ccb356b0e9e823"
REFERENCE = "b231397143c03d0a707d67116aaccacde655b6245816bae8c4fd72a8f4937ed1"
SELECTED = {"cursors": (20, 28, 80), "intro": (6, 7, 11, 12), "players": (192,)}


def existing_entries(raw):
    playlists.require(raw[:8] == playlists.MAGIC, "the immutable ancestor must contain only v2 programmes")
    p = 9 + raw[8] + 52
    slots, count = struct.unpack_from("<HH", raw, p)
    p += 4
    prefix = raw[8:p]
    entries = {}
    for _ in range(count):
        begin = p
        number, offset, length = struct.unpack_from("<HIH", raw, p)
        p += 8 + length
        frames, start, end, size = struct.unpack_from("<IIII", raw, p)
        p += 16 + 32 + size
        playlists.require(p <= len(raw) and number not in entries, "truncated/duplicate ancestor programme")
        entries[number] = raw[begin:p]
    playlists.require(p == len(raw), "ancestor programme trailing bytes")
    return prefix, slots, entries


def points(frames, index):
    result, previous = [], None
    for row in frames:
        if row[index] != previous:
            result.append((row[0], row[index]))
            previous = row[index]
    return result


def encode_points(values):
    playlists.require(1 <= len(values) <= 8192 and values[0][0] == 0,
                      "a bounded actual initial mixer state is required")
    previous = -1
    out = bytearray(struct.pack("<H", len(values)))
    for frame, value in values:
        playlists.require(frame > previous and 0 <= frame <= 32768 and -8191 <= value <= 8191,
                          "invalid executed mixer frame/level")
        out += struct.pack("<Ih", frame, value)
        previous = frame
    return bytes(out)


def prepare(base, source, reference, native_reference, output):
    base, source, reference, output = (p.absolute() for p in (base, source, reference, output))
    playlists.require(all(p.resolve() == p for p in (base, source, reference, output)) and
                      output.is_relative_to(ROOT / ".squad/.scratch") and not output.exists(),
                      "choose a create-new physical ignored extended audio output")
    for p in (base, source, reference):
        playlists.require(not output.is_relative_to(p) and not p.is_relative_to(output), "audio root overlap")
    ancestor = (base / "manifest.json").read_bytes()
    playlists.require(audio.sha256(ancestor) == BASE, "exact49-bank original CPU ancestor required")
    evidence = native_reference.read_bytes()
    playlists.require(audio.sha256(evidence) == REFERENCE, "exact independent real2.33 handler/lifecycle reference required")
    manifest = json.loads(ancestor)
    config = audio.pins()
    decoder_pins = next(p["files"] for p in config["sources"] if p["cache"] == "DCSExplorer")
    for name, blob in decoder_pins.items():
        playlists.require(audio.git_blob((reference / name).read_bytes()) == blob, "pinned CPU dependency changed:" + name)
    toolchain = dict(version=subprocess.check_output(["g++", "-dumpfullversion"], text=True).strip(),
                     target=subprocess.check_output(["g++", "-dumpmachine"], text=True).strip())
    audio.validate_toolchain(toolchain, config["native_toolchain"])
    output.mkdir(parents=True)
    work = output / "build-scratch"
    work.mkdir()
    executable = work / "render-global-mixer"
    command = ["g++", *audio.FLAGS, "-I", str(reference / "DCSDecoder"),
               str(Path(__file__).with_name("render_mixer_playlist.cpp")),
               str(reference / "DCSDecoder/DCSDecoder.cpp"), str(reference / "DCSDecoder/DCSDecoderNative.cpp"),
               "-o", str(executable)]
    subprocess.run(command, env=dict(os.environ, TMPDIR=str(work)), check=True, capture_output=True)
    banks, programs, extended = [], list(manifest["programs"]), []
    for bank in manifest["banks"]:
        raw = (base / bank["path"]).read_bytes()
        playlists.require(audio.sha256(raw) == bank["sha256"], "ancestor bank changed:" + bank["bank"])
        if bank["bank"] not in SELECTED:
            content = raw
        else:
            prefix, slots, entries = existing_entries(raw)
            original = (source / bank["sourcePath"]).read_bytes()
            playlists.require(audio.git_blob(original) == bank["sourceGitBlob"] and
                              audio.sha256(original) == bank["sourceSha256"], "original BNK differs")
            count = struct.unpack_from("<H", original, 128)[0]
            start = 134 + count * 4
            starts = {n: start + struct.unpack_from("<I", original, 134 + n * 4)[0]
                      for n in range(count) if struct.unpack_from("<I", original, 134 + n * 4)[0] != 0xffffffff}
            new_entries = {n: b"\x02" + data for n, data in entries.items()}
            for number in SELECTED[bank["bank"]]:
                playlists.require(number not in entries, "an old qualified programme cannot be replaced")
                offset = starts[number]
                limit = min((n for n in starts.values() if n > offset), default=len(original))
                programme, instructions = playlists.parse_program(original, offset, limit, implicit_global=True)
                playlists.require(any(i["opcode"] in (0x13, 0x14) for i in instructions),
                                  "selected programme contains no actual implicit-global operator")
                time = playlists.timeline(instructions)
                playlists.require("endFrame" in time, "this narrow extension requires a finite genuine00 terminator")
                frame_count = time["endFrame"] + 2
                renders = []
                for primary in (0, 1):
                    target, info = work / "current.pcm", work / "current.json"
                    subprocess.run([str(executable), str(source / bank["sourcePath"]), str(offset),
                                    str(len(programme)), str(frame_count), str(primary), str(target), str(info)],
                                   check=True, capture_output=True)
                    pcm, trace = target.read_bytes(), json.loads(info.read_bytes())
                    playlists.require(len(pcm) == frame_count * 480 and any(pcm) and
                                      trace["stopFrame"] == time["endFrame"] and
                                      trace["frames"][-1][3:] == [0, 0],
                                      "actual CPU programme halt/stream/tail/frame proof differs")
                    renders.append((pcm, trace))
                    target.unlink(); info.unlink()
                own_pcm, own = renders[0]
                local_pcm, local = renders[1]
                music = points(local["frames"], 1)
                own_levels = points(own["frames"], 2)
                playlists.require(music[0][1] < 0 and music[-1][1] == 0 and own_levels[-1][1] == 0,
                                  "actual global0 duck and00 matrix reset must execute")
                data = bytearray(b"\x03" + struct.pack("<HIH", number, offset, len(programme)) + programme)
                data += struct.pack("<IIII", frame_count, 0, 0, len(local_pcm))
                data += bytes.fromhex(audio.sha256(local_pcm)) + local_pcm
                data += struct.pack("<I", len(own_pcm)) + bytes.fromhex(audio.sha256(own_pcm)) + own_pcm
                data += struct.pack("<I", time["endFrame"]) + bytes.fromhex(REFERENCE)
                data += encode_points(music) + encode_points(own_levels)
                new_entries[number] = bytes(data)
                summary = dict(bank=bank["bank"], id=number, pcmSha256=audio.sha256(local_pcm),
                               loopStartSample=0, loopEndSample=0, implicitGlobalMixer=True)
                programs.append(summary)
                extended.append(dict(**summary, programHex=programme.hex(), offset=offset,
                                     frames=frame_count, stopFrame=time["endFrame"], global0=music,
                                     own0=own_levels, own0PcmSha256=audio.sha256(own_pcm),
                                     own0AndRebased1PcmByteEqual=own_pcm == local_pcm))
            # The original header name/blob/source identities remain identical.
            prefix = prefix[:-2] + struct.pack("<H", len(new_entries))
            content = b"SAUD\x03\0\0\0" + prefix + b"".join(new_entries[n] for n in sorted(new_entries))
        (output / bank["path"]).write_bytes(content)
        banks.append(dict(bank, sha256=audio.sha256(content), bytes=len(content)))
    result = dict(manifest, schema="original-compiled-playlists/v3",
                  profile="original-teams-complete-original303-prematch",
                  policy="portable-dcs233-global-mix-programs/v3", banks=banks, programs=programs,
                  nativeMixerReferenceSha256=REFERENCE, nativeMixerReferencePath="native-reference.json",
                  implicitGlobalMixerPrograms=len(extended), compiler=toolchain,
                  qualification="Pinned CPU VM executes original programmes after independently source-backed2.33 "
                    "13/14 lowering, with programme-local primary0/1 separation and exported live global0 "
                    "matrix envelopes. PCM/native allocation/aggregate headroom remain portable, not DSP-equivalent.")
    encoded = audio.canonical(result)
    (output / "manifest.json").write_bytes(encoded)
    (output / "native-reference.json").write_bytes(evidence)
    report = dict(schema="original-dcs233-global-mixer-preparation/v1",
                  manifestSha256=audio.sha256(encoded), output=str(output), banks=len(banks),
                  programs=len(programs), extended=extended, nativeMixerReferenceSha256=REFERENCE,
                  compilerCommand=command, decoderExecutableSha256=audio.sha256(executable.read_bytes()))
    (output / "qualification.json").write_bytes(audio.canonical(report))
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("base", "source", "reference", "native-reference", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(prepare(args.base, args.source, args.reference, args.native_reference, args.output), indent=2))


if __name__ == "__main__":
    main()
