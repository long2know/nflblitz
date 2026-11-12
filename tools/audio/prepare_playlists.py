#!/usr/bin/env python3
"""Compile authenticated original playlist programs, preserving waits and real loop cycles."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess

import prepare as audio

ROOT = Path(__file__).absolute().parents[2]
MAGIC = b"SAUD\x02\0\0\0"
OPERANDS = {0: 0, 1: 5, 2: 1, 7: 2, 8: 2, 9: 2, 10: 4, 11: 4, 12: 4,
            13: 0, 14: 1, 15: 0, 16: 2, 17: 4, 18: 4}
SELECTED_BANKS = ("CURSORS.BNK", "CURSORS2.BNK", "PLAYERS.BNK", "GENERIC.BNK", "TKPERM.BNK",
                  "Tkbank3.bnk", "Tkbank4.bnk", "Taunts3.bnk", "Taunts4.bnk",
                  "MAIN1.BNK", "MAIN2.BNK", "MAIN3.BNK", "Main4.bnk", "OTIME.BNK",
                  "INTRO.BNK", "HALFTIME.BNK", "GAMEOVER.BNK", "BUYIN.BNK")


def require(condition, detail):
    if not condition:
        raise audio.AudioError(detail)


def parse_program(bank, offset, limit, implicit_global=False):
    require(bank[offset:offset + 2] == b"\x01\x01", "unsupported original program type/channel-count")
    p, instructions, stack = offset + 2, [], []
    sizes = dict(OPERANDS) if implicit_global else OPERANDS
    if implicit_global:
        sizes.update({0x13: 1, 0x14: 3})
    while p + 3 <= limit:
        start = p
        wait, op = struct.unpack_from(">HB", bank, p)
        require(op in sizes, "unsupported original opcode0x%02x at%d" % (op, p + 2))
        p += 3
        size = sizes[op]
        require(p + size <= limit, "truncated original program operand")
        args = bank[p:p + size]
        item = dict(offset=start - offset, wait=wait, opcode=op, operands=args.hex())
        if op == 1:
            require(args[0] == 0, "cross-channel stream is outside compiled single-channel profile")
            address = p + 1 + int.from_bytes(args[1:4], "big")
            require(p + size <= address <= len(bank) - 18, "invalid relative original stream")
            frames = audio.u16be(bank, address)
            require(0 < frames < 65535, "invalid original stream frame count")
            item.update(stream=address, frames=frames, repeat=args[4])
        elif op in (2, 7, 8, 9, 10, 11, 12, 16, 17, 18):
            require(args[0] == 0, "cross-channel control needs the real concurrent playlist mixer")
        if op == 14:
            stack.append(len(instructions))
        elif op == 15:
            require(stack, "original playlist loop stack underflow")
            item["loopStart"] = stack[-1]
            if bytes.fromhex(instructions[stack[-1]]["operands"])[0] == 0:
                instructions.append(item)
                p += size
                break
            stack.pop()
        instructions.append(item)
        p += size
        require(len(instructions) <= 1024, "playlist instruction budget exceeded")
        if op == 0 or wait == 65535:
            break
    require(instructions and (instructions[-1]["opcode"] in (0, 15) or instructions[-1]["wait"] == 65535),
            "original playlist has no bounded recognized terminator")
    return bank[offset:p], instructions


def timeline(instructions):
    pc, frame, loops, steps, active = 0, 0, [], 0, None
    while pc < len(instructions):
        steps += 1
        require(steps <= 8192, "original playlist execution budget exceeded")
        item = instructions[pc]
        if item["wait"] == 65535:
            require(active is not None, "control-only infinite wait has no audio")
            start, frames, repeat = active
            if repeat == 0:
                return dict(loopStartFrame=start, loopFrames=frames)
            return dict(endFrame=start + frames * repeat)
        frame += item["wait"]
        op, args = item["opcode"], bytes.fromhex(item["operands"])
        if op == 0:
            return dict(endFrame=frame)
        if op == 1:
            active = (frame, item["frames"], item["repeat"])
        elif op == 14:
            loops.append(dict(pc=pc + 1, count=args[0], start=frame))
        elif op == 15:
            require(loops, "original runtime loop stack underflow")
            loop = loops[-1]
            if loop["count"] == 0:
                require(frame > loop["start"], "zero-duration original infinite program loop")
                return dict(loopStartFrame=loop["start"], loopFrames=frame - loop["start"])
            if loop["count"] > 1:
                loop["count"] -= 1
                pc = loop["pc"]
                continue
            loops.pop()
        pc += 1
    raise audio.AudioError("unclosed original playlist timeline")


def package(name, source, slots, entries):
    text = name.encode("ascii")
    out = bytearray(MAGIC + bytes([len(text)]) + text + bytes.fromhex(audio.git_blob(source))
                    + bytes.fromhex(audio.sha256(source)) + struct.pack("<HH", slots, len(entries)))
    for entry, pcm in entries:
        program = bytes.fromhex(entry["programHex"])
        out += struct.pack("<HIH", entry["id"], entry["offset"], len(program)) + program
        out += struct.pack("<IIII", entry["renderedFrames"], entry["loopStartSample"],
                           entry["loopEndSample"], len(pcm))
        out += bytes.fromhex(audio.sha256(pcm)) + pcm
    return bytes(out)


def prepare(source, reference, receipt, output, team_inputs=None):
    source, reference, output = map(lambda p: p.absolute(), (source, reference, output))
    require(output.resolve() == output and output.is_relative_to(ROOT / ".squad/.scratch")
            and not output.exists(), "choose a create-new physical ignored audio output")
    require(not output.is_relative_to(source) and not source.is_relative_to(output), "audio input/output overlap")
    identities = {p["path"]: p for p in json.loads(receipt.read_bytes())["files"]}
    selected_banks = list(SELECTED_BANKS)
    if team_inputs is not None:
        teams = json.loads(team_inputs.read_bytes())
        require(teams.get("schema") == "source-team-sound-bank-inputs/v1" and
                teams.get("rosterSha256") == "f747741e95766185e0c615ded549d1ce2f5fec0c48ed2ecb29ba1b755764539b" and
                [team["teamId"] for team in teams["teams"]] == list(range(31)),
                "exact original31-team roster sound-bank metadata required")
        for team in teams["teams"]:
            require(team["bankName"] == team["sourceName"][:8] and
                    team["sourcePath"].lower() == "sounds/" + team["bankName"].lower() + ".bnk",
                    "source team sound bank must use actual teaminfo.name8, not a prefix/alias")
            selected_banks.append(Path(team["sourcePath"]).name)
    config = audio.pins()
    decoder_pins = next(p["files"] for p in config["sources"] if p["cache"] == "DCSExplorer")
    for name, blob in decoder_pins.items():
        require(audio.git_blob((reference / name).read_bytes()) == blob, "original decoder dependency changed:" + name)
    toolchain = dict(version=subprocess.check_output(["g++", "-dumpfullversion"], text=True).strip(),
                     target=subprocess.check_output(["g++", "-dumpmachine"], text=True).strip())
    audio.validate_toolchain(toolchain, config["native_toolchain"])
    output.mkdir(parents=True)
    work = output / "build-scratch"
    work.mkdir()
    executable = work / "render-playlist"
    command = ["g++", *audio.FLAGS, "-I", str(reference / "DCSDecoder"),
               str(Path(__file__).with_name("render_playlist.cpp")),
               str(reference / "DCSDecoder/DCSDecoder.cpp"), str(reference / "DCSDecoder/DCSDecoderNative.cpp"),
               "-o", str(executable)]
    subprocess.run(command, env=dict(os.environ, TMPDIR=str(work)), check=True, capture_output=True)
    programs, unsupported, banks, total_pcm = [], [], [], 0
    for filename in selected_banks:
        path = source / "sounds" / filename
        if not path.exists():
            unsupported.append(dict(source="sounds/" + filename, detail="exact selected original input not prepared"))
            continue
        raw = path.read_bytes()
        identity = identities["sounds/" + filename]
        require(audio.git_blob(raw) == identity["gitBlobSha1"] and audio.sha256(raw) == identity["sha256"],
                "original audio bank pin differs:" + filename)
        slots, size = struct.unpack_from("<HI", raw, 128)
        base = 134 + slots * 4
        require(raw.startswith(b"DCS 3.0G ") and base + size == len(raw), "invalid original DCS3 bank container")
        starts = {n: base + struct.unpack_from("<I", raw, 134 + n * 4)[0]
                  for n in range(slots) if struct.unpack_from("<I", raw, 134 + n * 4)[0] != 0xffffffff}
        entries = []
        for number, offset in starts.items():
            try:
                limit = min((n for n in starts.values() if n > offset), default=len(raw))
                program, instructions = parse_program(raw, offset, limit)
                time = timeline(instructions)
                if "loopFrames" in time:
                    frame_count = time["loopStartFrame"] + time["loopFrames"] * 3 + 2
                else:
                    frame_count = time["endFrame"] + 2
                require(0 < frame_count <= 32768, "original playlist exceeds bounded render duration")
                target = work / "current.pcm"
                result = subprocess.run([str(executable), str(path), str(offset), str(len(program)),
                                         str(frame_count), str(target)], capture_output=True, text=True)
                require(result.returncode == 0, result.stderr.strip())
                pcm = target.read_bytes()
                target.unlink()
                require(len(pcm) == frame_count * 240 * 2 and any(pcm), "original playlist yielded no audible PCM")
                start = stop = 0
                if "loopFrames" in time:
                    start = (time["loopStartFrame"] + time["loopFrames"] + 2) * 240
                    stop = start + time["loopFrames"] * 240
                    require(pcm[start * 2:stop * 2] == pcm[stop * 2:(stop + stop - start) * 2],
                            "actual decoded second/third original loop cycles are not byte-identical")
                    pcm = pcm[:stop * 2]
                entry = dict(id=number, offset=offset, programHex=program.hex(), instructions=instructions,
                             renderedFrames=len(pcm) // 480, loopStartSample=start, loopEndSample=stop,
                             pcmSha256=audio.sha256(pcm), pcmBytes=len(pcm), **time)
                entries.append((entry, pcm))
            except (audio.AudioError, ValueError, struct.error) as error:
                unsupported.append(dict(bank=Path(filename).stem.lower(), playlist=number,
                                        source="sounds/" + filename, offset=offset, detail=str(error)))
        require(entries, "selected original bank has no qualified audible programs:" + filename)
        name = Path(filename).stem.lower()
        content = package(name, raw, slots, entries)
        (output / (name + ".saud")).write_bytes(content)
        total_pcm += sum(len(pcm) for _, pcm in entries)
        banks.append(dict(bank=name, path=name + ".saud", sha256=audio.sha256(content), bytes=len(content),
                          sourcePath="sounds/" + filename, sourceGitBlob=audio.git_blob(raw),
                          sourceSha256=audio.sha256(raw), slots=slots))
        programs.extend(dict(bank=name, **entry) for entry, _ in entries)
    manifest = dict(schema="original-compiled-playlists/v2",
                    profile="original-teams-complete-original303-source-banks" if team_inputs is not None else "original-teams-complete-original303",
                    policy="portable-compiled-original-playlists/v2", sourceRevision=config["sources"][0]["commit"],
                    banks=banks, programs=[dict(bank=p["bank"], id=p["id"], pcmSha256=p["pcmSha256"],
                                               loopStartSample=p["loopStartSample"], loopEndSample=p["loopEndSample"])
                                          for p in programs],
                    compiler=toolchain, decoderRevision=config["sources"][2]["commit"],
                    qualification="Executed pinned CPU playlist VM after exact BNK relative-address relocation; real second/third loop cycles compared; NOT arcade DSP/native equivalence.")
    content = audio.canonical(manifest)
    (output / "manifest.json").write_bytes(content)
    report = dict(schema="original-compiled-playlists-preparation/v2", output=str(output),
                  manifestSha256=audio.sha256(content), banks=banks, programs=programs, unsupported=unsupported,
                  decodedPcmBytes=total_pcm, compilerCommand=command,
                  decoderExecutableSha256=audio.sha256(executable.read_bytes()))
    (output / "qualification.json").write_bytes(audio.canonical(report))
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--reference", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--team-inputs", type=Path)
    args = parser.parse_args()
    report = prepare(args.source, args.reference, args.receipt, args.output, args.team_inputs)
    print(json.dumps({k: v for k, v in report.items() if k not in ("programs", "banks")}, indent=2))


if __name__ == "__main__":
    main()
