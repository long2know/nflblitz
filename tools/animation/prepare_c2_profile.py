#!/usr/bin/env python3
"""Reproduce the frozen C2 281-clip union and source-pinned callback payloads offline."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ("taunt_1_anim", "taunt_2_anim", "taunt_3_anim", "taunt_4_anim", "m_laugh1_anim",
           "m_qbwhat_anim", "m_qbfite_anim", "m_mscat1_anim", "m_mscat2_anim")
BASE_SHA = "9cae962bbb861bffe4d379bc30919cf34c0238ed35be82e605253d3059cfede9"
EXTRA_SHA = "3e69a29f11c15c5d318f486824b1a3bba1f9489905e20d4c9ce76742af7a8c5f"
ANIMATION_SHA = "e87401c8cf3e348cc2d9c7255a1f18f43e41b1694ff7dff45c4bf025a5ecce4f"
DATA_SHA = "9d2cb7ac5d92c3c2178fc1970a68ccad778fb7d319513b1787fe3ba3fe905f99"
HIT_SHA = "bb978454ab57b09357e422f048e4035bea01a4d5932f453dd89be39838c6e904"
SOURCE_PINS = {
    "player.c": "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c",
    "plyrseq.c": "28cef0d1082f2b0c9241930fdc8886de4c8f5438",
    "sounds.c": "f5ac3fd2cec5c20998bb613066a355e0d0d4d0e9",
    "Game.c": "a005a9b44f032080f3dd6341dd4f5eb6104d9cd7",
    "nfl.c": "15b49474fb55e8bac280b6515fcf7bf59cdf8664",
    "include/sndcalls.h": "69f6c483502fb689eb1991169e0e31858378c763",
}


def module(name, relative):
    spec = importlib.util.spec_from_file_location(name, ROOT / relative)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


selected = module("c2_selected_producer", "tools/animation/prepare_player_sequences.py")
audio = module("c2_canonical_audio", "tools/audio/prepare_functions.py")
require = selected._require


def sha(data):
    return hashlib.sha256(data).hexdigest()


def prepare(source_root, base_root, extra_root, destination, reuse=False):
    source_root, base_root, extra_root, destination = (
        selected._absolute(p) for p in (source_root, base_root, extra_root, destination))
    for p in (source_root, base_root, extra_root, destination):
        selected._no_symlinks(p)
    require(destination.is_relative_to(ROOT / ".squad/.scratch") and destination != ROOT / ".squad/.scratch",
            "All original/converted resources must remain ignored/local.")
    for p in (source_root, base_root, extra_root):
        require(not destination.is_relative_to(p) and not p.is_relative_to(destination), "Input/output roots overlap.")
    require(not destination.exists() or reuse, "Destination exists; --reuse only verifies an identical bundle.")
    sources = {name: selected._read_bytes(source_root / name, 4 * 1024 * 1024) for name in SOURCE_PINS}
    require(all(selected.reader.wan.git_blob_sha1(sources[n]) == pin for n, pin in SOURCE_PINS.items()),
            "Original C2 source blob differs from the frozen schema.")
    outputs, inventories = {}, []
    for root, pin, count in ((base_root, BASE_SHA, 272), (extra_root, EXTRA_SHA, 9)):
        raw = selected._read_bytes(root / selected.MANIFEST, 1024 * 1024)
        require(sha(raw) == pin, "Explicit old272/real9 manifest pin mismatch.")
        inventory = json.loads(raw)
        require(len(inventory["clips"]) == count, "Wrong old272/real9 clip inventory.")
        inventories.append(inventory)
        for entry in inventory["clips"]:
            selected._relative_path(entry["path"])
            content = selected._read_bytes(root / entry["path"], 8 * 1024 * 1024)
            require(sha(content) == entry["sha256"] and "animation281/" + entry["path"] not in outputs,
                    "Missing/changed/overlapping original clip: " + entry["runtime_name"])
            outputs["animation281/" + entry["path"]] = content
    base, extra = inventories
    entries = sorted(base["clips"] + extra["clips"], key=lambda e: e["runtime_name"])
    require(len({e["runtime_name"] for e in entries}) == 281 and
            sum(e["selected_frames"] for e in entries) == 26230, "Exact disjoint 281/26230 union required.")
    union = {"schema": base["schema"], "source": base["source"], "format_source": base["format_source"],
             "profile": "original-teams-contact281-c2", "selected_base_manifest_sha256": BASE_SHA,
             "supplemental_manifest_sha256": EXTRA_SHA, "producer_policy": "immutable-source272-plus-disjoint-source9-c2/v1",
             "converter_replay": base["converter_replay"],
             "shipped_SEQ_numeric_ids_or_quantization_verified": False, "clips": entries}
    union_bytes = selected.reader.encode_clip(union).encode()
    old, _, _, definitions = audio.produce(source_root)
    require(sha(audio.encode(old)) == "4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280",
            "Historical six-binding SAFN changed.")
    seq = sources["plyrseq.c"].decode("ascii").splitlines()
    bindings = []
    for script in SCRIPTS:
        start = next(i for i, line in enumerate(seq) if line.strip() == script + ":")
        stop = next(i for i in range(start, len(seq)) if seq[i].strip() == ".byte\tANI_END")
        calls = [(i + 1, re.fullmatch(r"\.word\s+(taunt_speech|get_hit_snd),(-?\d+),(-?\d+),(-?\d+)", seq[i].strip()))
                 for i in range(start, stop) if not seq[i].lstrip().startswith("#")]
        calls = [(line, m) for line, m in calls if m]
        require(len(calls) == 1, "Expected one exact original C2 sound site: " + script)
        line, match = calls[0]
        bindings.append({"path": "plyrseq.c", "script": script, "function": match[1],
                         "kind": "HitFunctionExtension" if match[1] == "get_hit_snd" else "TauntSpeech",
                         "line": line, "arguments": [int(match[n]) for n in (2, 3, 4)]})
    hit = next(b for b in bindings if b["kind"] == "HitFunctionExtension")
    extension = json.loads(json.dumps(old))
    extension["bindings"] = [{k: v for k, v in hit.items() if k != "kind"}]
    hit_bytes = audio.encode(extension)
    sound = sources["sounds.c"].decode("ascii")
    tables = []
    for symbol in ("short_taunt_sp", "long_taunt_sp", "laugh_taunt_sp", "histep_taunt_sp"):
        match = re.search(r"^static int " + symbol + r"\[\]\s*=\s*\{([^}]+)\};", sound, re.M)
        require(match is not None, "Missing pinned speech table: " + symbol)
        tables.append({"symbol": symbol, "source_line": sound[:match.start()].count("\n") + 1,
                       "values": [int(v) for v in match[1].split(",")]})
    game = sources["Game.c"].decode("ascii")
    banks = {}
    for name, symbol in (("announcer", "announcer_strs"), ("taunt", "taunt_bnk_strs")):
        match = re.search(r"\b" + symbol + r"\[\]\s*=\s*\{([^}]+)\}", game)
        require(match is not None, "Missing original bank pool.")
        banks[name] = re.findall(r'"([^"]+)"', match[1])
    iv = lambda name: audio.integer(name, definitions)
    v4, v3, p3, p4, p5, whistle = [iv(n) for n in
        ("VOLUME4", "VOLUME3", "PRIORITY_LVL3", "PRIORITY_LVL4", "PRIORITY_LVL5", "WHISTLE1_SND")]
    call_lines = (511, 513, 515, 519, 524, 527, 6018, 1839, 2434)
    playlists = [t["symbol"] for t in tables] + ["11", "sp"] + ["WHISTLE1_SND+randrng(2)"] * 3
    call_sites = []
    for i, line in enumerate(call_lines):
        path = "sounds.c" if i < 6 else "player.c"
        require("snd_scall_bank(" in sources[path].decode("ascii").splitlines()[line - 1],
                "Original C2 callee source site changed.")
        call_sites.append({"path": path, "function": "taunt_speech" if i < 6 else
                           "whistle_proc" if i == 6 else "_mode_qb" if i == 7 else "_mode_wpn",
                           "line": line, "playlist_source": playlists[i], "volume": v3 if i == 6 else v4,
                           "pan": 127, "priority": p4 if i in (3, 6) else p5 if i > 6 else p3})
    data = {
        "schema": "original-source-c2-callback-data/v1", "profile": "OriginalRelease57Hz-C2-InitSoundBanks-v1",
        "source": {"repository": "historicalsource/nflblitz2000",
                   "revision": "89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
                   "tree": "a7e0352125d46075b7d98e87253ab89b9160a4c7", "blobs": SOURCE_PINS,
                   "source_hz": 57, "use_old_qb": 0},
        "format_source": {"repository": "historicalsource/midwayvideo",
                          "revision": "1792293aaaea00a919b59e8c05716978e85b4333",
                          "process_blob": "967ca53910f24ddfa48a14485113a8fd0c989959",
                          "dispatcher_blob": "7b44637c3d59e2256ad099226b7ea17f108907ef",
                          "legacy_animation_manifest_sha256": BASE_SHA,
                          "extra_animation_manifest_sha256": EXTRA_SHA, "clip_count": 281,
                          "selected_frame_count": 26230},
        "animation_manifest_sha256": sha(union_bytes),
        "legacy_sound_payload_sha256": sha(audio.encode(old)), "hit_extension_payload_sha256": sha(hit_bytes),
        "programs": list(SCRIPTS), "vm_bindings": bindings, "banks": banks, "speech_tables": tables,
        "sound_calls": {"taunt_volume": v4, "pan": 127, "taunt_priority": p3, "histep_priority": p4,
                        "whistle_base": whistle, "whistle_volume": v3, "whistle_priority": p4,
                        "immediate_whistle_volume": v4, "immediate_whistle_priority": p5,
                        "whistle_chance": 25, "whistle_sleep": 30, "sites": call_sites},
        "end_producer_sites": {"whistle_qcreate_line": 5948, "pid": 0, "arguments": [0, 0, 0, 0],
                               "chance_line": 6014, "sleep_line": 6017, "emit_line": 6018,
                               "celebrate_line": 6007, "celebrate_time": 60},
    }
    data_bytes = audio.canonical(data)
    require((sha(union_bytes), sha(data_bytes), sha(hit_bytes)) == (ANIMATION_SHA, DATA_SHA, HIT_SHA),
            "Canonical C2 byte identity differs; stop, never rebaseline frozen source data.")
    outputs.update({"animation281/export-inventory.json": union_bytes, "c2-callback-data.json": data_bytes,
                    "m-qbfite-hit-extension.safn": hit_bytes,
                    "m-qbfite-hit-extension-source.json": audio.canonical(extension)})
    for name in ("animation281/export-inventory.json", "c2-callback-data.json", "m-qbfite-hit-extension.safn"):
        outputs[name + ".sha256"] = (sha(outputs[name]) + "\n").encode()
    if destination.exists():
        actual = {p.relative_to(destination).as_posix() for p in destination.rglob("*") if p.is_file()}
        require(actual == set(outputs), "Existing output inventory differs.")
        require(all(selected._read_bytes(destination / n, len(b)) == b for n, b in outputs.items()),
                "Existing canonical C2 bytes differ.")
    else:
        destination.mkdir(parents=True)
        for name, content in outputs.items():
            path = destination / name
            path.parent.mkdir(parents=True, exist_ok=True)
            with path.open("xb") as stream:
                stream.write(content)
    require(all(selected._read_bytes(source_root / n, len(raw)) == raw for n, raw in sources.items()),
            "Pinned original inputs changed during preparation.")
    return {"schema": "source-c2-production-preparation/v1", "animation_manifest_sha256": ANIMATION_SHA,
            "c2_data_sha256": DATA_SHA, "hit_extension_sha256": HIT_SHA, "clips": 281,
            "selected_frames": 26230, "files": len(outputs), "reused": reuse,
            "output_sha256": {n: sha(b) for n, b in outputs.items()}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("source-root", "base-bundle", "extra-bundle", "destination"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--reuse", action="store_true")
    args = parser.parse_args()
    try:
        print(json.dumps(prepare(args.source_root, args.base_bundle, args.extra_bundle, args.destination, args.reuse), indent=2))
        return 0
    except (ValueError, OSError, KeyError, StopIteration) as error:
        print("error: " + str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
