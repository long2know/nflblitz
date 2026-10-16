#!/usr/bin/env python3
"""Pinned, local-only DCS bank preparation. No upstream scripts are executed."""

import argparse
import base64
import hashlib
import io
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import sys
import wave

HERE = Path(__file__).resolve().parent
ARTIFACTS = HERE / ".artifacts"
SOURCE = ARTIFACTS / "source"
INPUTS = HERE / "inputs.json"
MAGIC = b"SAUD\x01\x00\x00\x00"
RATE = 31250
FRAME_SAMPLES = 240
TAIL_FRAMES = 2
FLAGS = ["-std=c++17", "-O2", "-fno-strict-aliasing", "-fwrapv", "-ffp-contract=off"]


class AudioError(ValueError):
    pass


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def git_blob(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()


def canonical(value):
    return (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=True) + "\n").encode("ascii")


def pins():
    return json.loads(INPUTS.read_text(encoding="utf-8"))


def verify_blob(data, expected, name):
    actual = git_blob(data)
    if actual != expected:
        raise AudioError(f"{name}: Git blob mismatch: expected {expected}, got {actual}")
    return data


def verify_sources(fetch=False):
    receipt = {}
    for source in pins()["sources"]:
        for name, blob in source["files"].items():
            path = SOURCE / source["cache"] / name
            if not path.exists():
                if not fetch:
                    raise AudioError(f"missing pinned input {path}; run prepare.py fetch")
                api = f"repos/{source['repository']}/git/blobs/{blob}"
                response = json.loads(subprocess.check_output(["gh", "api", api]))
                if response.get("encoding") != "base64":
                    raise AudioError(f"{name}: unexpected GitHub blob encoding")
                data = verify_blob(base64.b64decode(response["content"], validate=False), blob, name)
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
            data = verify_blob(path.read_bytes(), blob, str(path))
            receipt[source["cache"] + "/" + name] = {
                "git_blob": blob, "sha256": sha256(data), "bytes": len(data)
            }
    return receipt


def u16be(data, offset):
    return struct.unpack_from(">H", data, offset)[0]


def parse_bank(data):
    if len(data) < 134 or not data.startswith(b"DCS 3.0G "):
        raise AudioError("not the established DCS 3.0G container")
    slots, data_size = struct.unpack_from("<HI", data, 128)
    base = 134 + slots * 4
    if slots == 0 or base > len(data) or data_size != len(data) - base:
        raise AudioError("invalid sparse table extent or bank data size")
    offsets = {}
    for playlist_id in range(slots):
        relative = struct.unpack_from("<I", data, 134 + 4 * playlist_id)[0]
        if relative == 0xFFFFFFFF:
            continue
        absolute = base + relative
        if absolute + 2 > len(data):
            raise AudioError(f"playlist {playlist_id}: address outside bank")
        offsets[playlist_id] = absolute
    if not offsets or min(offsets.values()) != base:
        raise AudioError("playlist table does not start at the data base")
    starts = sorted(set(offsets.values()))
    entries = []
    for playlist_id, absolute in offsets.items():
        next_start = next((x for x in starts if x > absolute), None)
        limit = next_start if next_start is not None else absolute + 18
        if limit - absolute < 2 or limit > len(data):
            raise AudioError(f"playlist {playlist_id}: overlapping or truncated program")
        raw = data[absolute:limit]
        entry = {
            "id": playlist_id, "offset": absolute, "raw_program_hex": raw.hex(),
            "sample_decode_supported": False, "isolated_supported": False,
            "unsupported_reason": "program is outside the established single-stream subset",
        }
        # This is deliberately a profile, NOT a claim that all BNK entries have
        # an 18-byte stride. Unknown controls are retained verbatim, never skipped.
        if (len(raw) == 18 and raw[:6] == b"\x01\x01\x00\x00\x07\x00"
                and raw[7:11] == b"\x00\x00\x01\x00" and raw[17] == 0):
            relative = int.from_bytes(raw[11:14], "big")
            stream_offset = absolute + 11 + relative
            if stream_offset < starts[-1] + 18 or stream_offset + 18 > len(data):
                raise AudioError(f"playlist {playlist_id}: stream outside compressed-data region")
            frames = u16be(data, stream_offset)
            if frames == 0 or frames == 65535:
                raise AudioError(f"playlist {playlist_id}: invalid stream frame count")
            entry.update({
                "program_type_byte": raw[0], "program_channel_byte": raw[1],
                "mix_opcode": 7, "mix_channel": 0, "mix_signed": struct.unpack("b", raw[6:7])[0],
                "mix_wait_frames": 0, "play_wait_frames": 0, "play_channel": 0,
                "stream_address_field_offset": absolute + 11,
                "stream_relative_address": relative, "stream_offset": stream_offset,
                "repeat_count": raw[14], "end_wait_frames": u16be(raw, 15),
                "stream_frames": frames,
            })
            if raw[6] > 127:
                entry["unsupported_reason"] = "negative mixing level not qualified"
            elif raw[14] != 1:
                entry["unsupported_reason"] = "repeating streams require a DCS loop/tail implementation"
            else:
                entry["sample_decode_supported"] = True
                if entry["end_wait_frames"] != frames + 1:
                    entry["unsupported_reason"] = "nonstandard end timing requires a playlist interpreter"
                else:
                    entry["isolated_supported"] = True
                    del entry["unsupported_reason"]
        entries.append(entry)
    return {
        "slots": slots, "data_base": base, "data_bytes": data_size,
        "header_hex": data[:128].hex(), "entries": entries,
    }


def parse_listing(text, bank_name):
    if not re.search(r"Bank name:\s*" + re.escape(bank_name) + r"\.BNK\b", text):
        raise AudioError("listing bank identity mismatch")
    descriptions = {}
    for match in re.finditer(
            r"^\s*(\d+)\s+\$([0-9a-fA-F]+)\s+(\d+)\s+[\d.]+\s+[\d.]+\s+([^\r\n]+)",
            text, re.M):
        dec, hex_id, tracks, description = match.groups()
        if int(dec) != int(hex_id, 16) or int(dec) in descriptions or int(tracks) != 1:
            raise AudioError("ambiguous or multi-track listing entry")
        descriptions[int(dec)] = description.strip()
    count = re.search(r"Total number of playlists:\s*(\d+)", text)
    if not count or int(count[1]) != len(descriptions):
        raise AudioError("listing playlist count mismatch")
    return descriptions


def impact_candidates(sound_source, header, config):
    # Preserve duplicate table entries: they change RNG probabilities.
    definitions = {k: int(v) for k, v in re.findall(r"^#define\s+(\w+)\s+(\d+)", header, re.M)}
    tables = {}
    for name in config["impact_tables"]:
        match = re.search(r"\b" + name + r"\[\]\s*=\s*\{([^}]+)\}", sound_source)
        if not match:
            raise AudioError(f"missing original impact table {name}")
        symbols = re.findall(r"\bP_\w+\b", match[1])
        if not symbols or any(symbol not in definitions for symbol in symbols):
            raise AudioError(f"unresolved original impact table {name}")
        tables[name] = [definitions[symbol] for symbol in symbols]
    fixed = definitions[config["impact_fixed_symbol"]]
    return tables, sorted({fixed}.union(*(set(values) for values in tables.values())))


def validate_toolchain(actual, expected):
    if actual != expected:
        raise AudioError(f"unqualified native compiler profile: expected {expected}, got {actual}")


def build_decoder(reference=False):
    verify_sources()
    toolchain = {
        "version": subprocess.check_output(["g++", "-dumpfullversion"], text=True).strip(),
        "target": subprocess.check_output(["g++", "-dumpmachine"], text=True).strip(),
    }
    validate_toolchain(toolchain, pins()["native_toolchain"])
    output = ARTIFACTS / "bin" / ("BnkExtract" if reference else "decode")
    output.parent.mkdir(parents=True, exist_ok=True)
    library = SOURCE / "DCSExplorer" / "DCSDecoder"
    main = SOURCE / "DCSExplorer/BnkExtract/BnkExtract.cpp" if reference else HERE / "decode.cpp"
    command = ["g++", *FLAGS, "-I", str(library), str(main),
               str(library / "DCSDecoder.cpp"), str(library / "DCSDecoderNative.cpp"),
               "-o", str(output)]
    scratch = ARTIFACTS / "build-scratch"
    scratch.mkdir(parents=True, exist_ok=True)
    environment = dict(os.environ, TMPDIR=str(scratch))
    result = subprocess.run(command, capture_output=True, text=True, env=environment)
    if result.returncode:
        raise AudioError("decoder compilation failed:\n" + result.stderr)
    return output, {
        "compiler": subprocess.check_output(["g++", "--version"], text=True).splitlines()[0],
        "toolchain": toolchain,
        "flags": FLAGS, "binary_sha256": sha256(output.read_bytes()),
        "diagnostics": result.stderr.strip(),
    }


def wav_bytes(pcm):
    if len(pcm) % 2:
        raise AudioError("unaligned PCM16")
    output = io.BytesIO()
    with wave.open(output, "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(RATE)
        wav.writeframes(pcm)
    return output.getvalue()


def validate_pcm(pcm, frames):
    if len(pcm) != (frames + TAIL_FRAMES) * FRAME_SAMPLES * 2:
        raise AudioError("decoder PCM length does not match the original frame count")
    samples = struct.unpack("<" + "h" * (len(pcm) // 2), pcm)
    if not any(samples):
        raise AudioError("original decoded sample is all zero; refusing silent substitution")
    return {
        "pcm_samples": len(samples), "pcm_bytes": len(pcm), "rate": RATE, "channels": 1,
        "duration_seconds": len(samples) / RATE, "pcm_sha256": sha256(pcm),
        "minimum": min(samples), "maximum": max(samples),
        "nonzero_samples": sum(x != 0 for x in samples),
    }


def package_bytes(bank_name, source_blob, source_sha256, slots, prepared):
    name = bank_name.lower().encode("ascii")
    result = bytearray(MAGIC + bytes([len(name)]) + name)
    result += bytes.fromhex(source_blob) + bytes.fromhex(source_sha256)
    result += struct.pack("<HH", slots, len(prepared))
    for entry, pcm in prepared:
        program = bytes.fromhex(entry["raw_program_hex"])
        result += struct.pack("<HIH", entry["id"], entry["offset"], len(program)) + program
        result += struct.pack("<IHI", entry["stream_offset"], entry["stream_frames"], len(pcm))
        result += bytes.fromhex(sha256(pcm)) + pcm
    return bytes(result)


def extract(name):
    if not re.fullmatch(r"[A-Za-z0-9_-]+", name):
        raise AudioError("run name must be a simple directory name")
    source_receipt = verify_sources()
    decoder, build = build_decoder()
    root = ARTIFACTS / "decoded" / name
    if root.exists():
        raise AudioError(f"output already exists: {root}; choose a fresh --name")
    root.mkdir(parents=True)
    nfl = SOURCE / "nflblitz2000"
    config = pins()
    tables, players = impact_candidates(
        (nfl / "sounds.c").read_text(), (nfl / "include/sndcalls.h").read_text(), config)
    manifest = {
        "schema": "source-audio-preparation/v1",
        "decoder_profile": config["decoder_profile"],
        "rights": config["original_rights"],
        "impact_tables": tables, "impact_candidate_ids": players,
        "source_inputs": source_receipt, "banks": [],
        "host_parameter_values": {
            "volume_bytes": {"VOLUME1": 115, "VOLUME2": 160, "VOLUME3": 205, "VOLUME4": 235, "VOLUME5": 255},
            "priority_words": {"PRIORITY_LVL0": 20, "PRIORITY_LVL1": 15, "PRIORITY_LVL2": 10,
                               "PRIORITY_LVL3": 5, "PRIORITY_LVL4": 1, "PRIORITY_LVL5": 0},
            "pan_byte_used_by_reached_calls": 127,
            "basis": "Pinned include/sndcalls.h:59-64,413-417; sounds.c:1501-1619; host snd_scall_direct only clamps/transports these parameters.",
            "DSP3_gain_pan_priority_arbitration_established": False,
        },
        "call_policy": {
            "volume_pan_priority": "Host snd_scall_direct transports these unchanged; DSP3 mapping/arbitration is NOT implemented. They are not Unity linear volume/pan/priority.",
            "callback_selection": "Must run synchronously in the original actor process using Core state and the shared original RNG. This tool and the Unity adapter never select a callback or draw RNG.",
            "playback": "Isolated one-shot decoded PCM only, with the playlist mixing level baked in. Not a SourceCallback consumer.",
        },
    }
    products = {}
    for bank_name in ["CURSORS", "PLAYERS"]:
        bank_path = nfl / "sounds" / (bank_name + ".BNK")
        data = bank_path.read_bytes()
        bank = parse_bank(data)
        listing_path = nfl / "sounds" / (bank_name + ".LST")
        descriptions = parse_listing(listing_path.read_text(), bank_name)
        if set(descriptions) != {entry["id"] for entry in bank["entries"]}:
            raise AudioError(f"{bank_name}: listing and sparse index IDs disagree")
        requested = (set(config["cursor_consumer_ids"]) if bank_name == "CURSORS" else set(players))
        bank_dir = root / bank_name.lower()
        bank_dir.mkdir()
        decoded = {}
        prepared = []
        for entry in bank["entries"]:
            entry["description"] = descriptions[entry["id"]]
            entry["prepared"] = entry["id"] in requested
            if not entry["prepared"]:
                continue
            if not entry["sample_decode_supported"]:
                raise AudioError(f"required original playlist {bank_name}:{entry['id']} is unsupported")
            key = (entry["stream_offset"], entry["mix_signed"])
            stem = f"sample_{key[0]:06x}_mix{key[1]}"
            if key not in decoded:
                compressed = bank_dir / (stem + ".dcs")
                # Retain the exact pinned remainder, not a guessed CMP extent.
                # GetStreamInfo measures the stream's actual consumed bit range.
                compressed.write_bytes(data[entry["stream_offset"]:])
                pcm_path = bank_dir / (stem + ".pcm")
                result = subprocess.run(
                    [str(decoder), str(compressed), str(key[1]), str(pcm_path)],
                    text=True, capture_output=True)
                if result.returncode:
                    raise AudioError(f"{bank_name}:{entry['id']}: decoder failed: {result.stderr}")
                stream_info = json.loads(result.stdout)
                pcm = pcm_path.read_bytes()
                stats = validate_pcm(pcm, entry["stream_frames"])
                if stream_info["sample_count"] != stats["pcm_samples"]:
                    raise AudioError("decoder receipt and PCM counts disagree")
                wav = wav_bytes(pcm)
                (bank_dir / (stem + ".wav")).write_bytes(wav)
                consumed = stream_info["consumed_bytes"]
                stream_info.update({
                    "compressed_offset": entry["stream_offset"],
                    "compressed_consumed_sha256": sha256(data[key[0]:key[0] + consumed]),
                    "header_hex": data[key[0] + 2:key[0] + 18].hex(),
                    "wav_sha256": sha256(wav), **stats,
                })
                decoded[key] = (pcm, stream_info, stem)
            pcm, stream_info, stem = decoded[key]
            entry["sample"] = dict(stream_info, pcm_file=stem + ".pcm", wav_file=stem + ".wav")
            prepared.append((entry, pcm))
        expected = set(requested)
        if expected != {entry["id"] for entry, _ in prepared}:
            raise AudioError(f"{bank_name}: missing required playlist")
        key = "nflblitz2000/sounds/" + bank_name + ".BNK"
        source = source_receipt[key]
        package = package_bytes(bank_name, source["git_blob"], source["sha256"], bank["slots"], prepared)
        package_name = bank_name.lower() + ".saud"
        (root / package_name).write_bytes(package)
        products[package_name] = sha256(package)
        bank.update({
            "name": bank_name.lower(), "source_git_blob": source["git_blob"],
            "source_sha256": source["sha256"], "listing_sha256": sha256(listing_path.read_bytes()),
            "prepared_playlist_ids": sorted(requested), "decoded_variants": len(decoded),
            "package": package_name, "package_sha256": sha256(package),
        })
        manifest["banks"].append(bank)
    content = canonical(manifest)
    (root / "manifest.json").write_bytes(content)
    products["manifest.json"] = sha256(content)
    receipt = {
        "schema": "source-audio-extraction-receipt/v1", "build": build,
        "products_sha256": products,
        "tool_sha256": sha256(Path(__file__).read_bytes()),
        "wrapper_sha256": sha256((HERE / "decode.cpp").read_bytes()),
        "inputs_sha256": sha256(INPUTS.read_bytes()),
    }
    (root / "receipt.json").write_bytes(canonical(receipt))
    print(json.dumps({"output": str(root.relative_to(HERE)), "products_sha256": products}, sort_keys=True))
    return root


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["fetch", "verify-inputs", "extract", "build-reference"])
    parser.add_argument("--name", default="original")
    args = parser.parse_args()
    if args.command == "fetch":
        print(json.dumps(verify_sources(fetch=True), sort_keys=True))
    elif args.command == "verify-inputs":
        print(f"Verified {len(verify_sources())} exact Git blobs")
    elif args.command == "build-reference":
        path, build = build_decoder(reference=True)
        print(json.dumps({"path": str(path), **build}, sort_keys=True))
    else:
        extract(args.name)


if __name__ == "__main__":
    try:
        main()
    except (AudioError, OSError, subprocess.SubprocessError, KeyError) as error:
        print(f"source-audio: {error}", file=sys.stderr)
        sys.exit(1)
