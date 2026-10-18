#!/usr/bin/env python3
"""Extract unchanged active original C windows at runtime; all originals/generated data stay ignored."""
import argparse
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys


SUITE = Path(__file__).resolve().parent
ROOT = SUITE.parents[1]
sys.path.insert(0, str(ROOT / "tests/source-oracles"))
from oracle import matching_brace

PINS = {
    "player.c": "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c",
    "pmisc.c": "50426d40ecd03515cecd295fb80a63ffa0cb0683",
    "detect.c": "68c239fa2e617988107fbebeb4dfeff5bbfe9233",
    "handicap.c": "871424596fb9786af3602a49cc9d20c466df8ca1",
    "nfl.c": "15b49474fb55e8bac280b6515fcf7bf59cdf8664",
    "lib/ani3d/TRIGS.H": "a07d76798931f1240bdf7f3b8e753ebd7832763a",
}
EXPECTED = {"C": 24000, "D": 4096, "F": 10000, "A": 8000}
ORIENTATION_CASES = 1769


def sha(data):
    return hashlib.sha256(data).hexdigest()


def build(source_root, fixture_manifest, output):
    source_root, output = Path(source_root).resolve(), Path(output).resolve()
    if not output.is_relative_to(ROOT) or output == ROOT:
        raise ValueError("Native generated artifacts must stay inside this worktree.")
    output.mkdir(parents=True, exist_ok=True)
    sources = {}
    for name, pin in PINS.items():
        data = (source_root / name).read_bytes()
        actual = hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()
        if actual != pin:
            raise ValueError("Original C input pin mismatch: " + name)
        sources[name] = data
    windows, omitted = [], []

    def function_window(data, symbol):
        pattern = rb"(?m)^(?:static\s+)?(?:void|float|int)\s+" + symbol.encode() + rb"\s*\([^;{}]*\)\s*\{"
        matches = list(re.finditer(pattern, data))
        if len(matches) != 1:
            raise ValueError("Expected exactly one pinned original function: " + symbol)
        start = matches[0].start()
        # These pinned C functions close at column zero; inactive alternative branches have unequal braces.
        end = data.index(b"\n}", matches[0].end()) + 2
        return start, end

    def select(name, start, end, symbol):
        data = sources[name][start:end]
        if not data:
            raise ValueError("Empty original selection: " + symbol)
        windows.append({"path": name, "symbol": symbol, "start_byte": start,
                        "end_byte_exclusive": end, "sha256": sha(data),
                        "first_line": sources[name][:start].count(b"\n") + 1,
                        "unchanged_original_bytes": True})
        return data.decode("latin1")

    def active(name, start, end, symbol):
        data = sources[name]
        segment = data[start:end]
        marker = re.search(rb"(?m)^[ \t]*#ifdef 0[^\n]*\n", segment)
        if marker is None:
            return select(name, start, end, symbol)
        opening = start + marker.start()
        cursor = start + marker.end()
        depth = 1
        for match in re.finditer(rb"(?m)^[ \t]*#(if|ifdef|ifndef|endif)\b[^\n]*(?:\n|$)", data[cursor:end]):
            depth += -1 if match.group(1) == b"endif" else 1
            if depth == 0:
                closing = cursor + match.end()
                omitted.append({"path": name, "first_line": data[:opening].count(b"\n") + 1,
                                "sha256": sha(data[opening:closing]),
                                "reason": "Archive '#ifdef 0 //DEBUG' is an inactive diagnostic block; omitted, never rewritten."})
                return select(name, start, opening, symbol + "/before-disabled-debug") + active(
                    name, closing, end, symbol + "/after-disabled-debug")
        raise ValueError("Unterminated original inactive debug block.")

    def function(name, symbol):
        return active(name, *function_window(sources[name], symbol), symbol)

    functions = [
        function("player.c", "dist3d"), function("player.c", "in_bounds"),
        function("pmisc.c", "intercept_ok"), function("pmisc.c", "solve_quad"),
        function("pmisc.c", "deflect_pass"), function("player.c", "bobble_pass"),
        function("player.c", "fumble_ball"), function("detect.c", "fumble_or_bobble"),
    ]
    start, end = function_window(sources["player.c"], "standard_catch")
    boundary = sources["player.c"].index(b"\n\t// catch it", start, end)
    functions.append(active("player.c", start, boundary, "standard_catch/decision-prefix") +
                     "\n oracle_result=catch_result;\n}\n")
    tables = []
    for name in ("nocatch_ht", "of_bobble_ht", "db_air_batdown_ht", "db_stand_bobble_ht",
                 "bob_up_ht", "bob_cap_ht", "bobble_human_ht", "bobble_hteam_ht", "bobble_drone_ht"):
        start = sources["handicap.c"].index(("int " + name + "[12]").encode())
        end = sources["handicap.c"].index(b";", start) + 1
        tables.append(select("handicap.c", start, end, name))
    player = sources["player.c"]
    ball_start, ball_end = function_window(player, "ball_proc")
    start = player.index(b"\n\t\t\tif (!(game_info.game_flags & GF_FWD_PASS)", ball_start, ball_end)
    end = player.index(b"\n\t\t\t// ball special effects", start, ball_end)
    physics = select("player.c", start, end, "ball_proc/physics-prefix")
    crossing = player.index(b"\n\t\tif(( game_info.play_type != PT_KICKOFF )", end, ball_end)
    opening = player.index(b"{", crossing, ball_end)
    crossing_end = matching_brace(player, opening)
    functions.append("static void advance_kernel(void) {\nfloat prev[3],f,vxz;\n" + physics +
                     select("player.c", crossing, crossing_end, "ball_proc/original-cross-LOS-block") + "\n}\n")
    heading_start = player.index(b"\n\t\t\t// heading", end, ball_end)
    heading_closings = list(re.finditer(rb"\r?\n\t\t}\r?\n\t\telse", player[heading_start:ball_end]))
    if len(heading_closings) != 1:
        raise ValueError("Expected the unique original free-ball/held-ball heading boundary.")
    heading_end = heading_start + heading_closings[0].start()
    functions.append("static void orientation_kernel(void) {\nfloat vx,vy,vz,vxz,vxyz;\nint angle;\n" +
                     select("player.c", heading_start, heading_end, "ball_proc/original-heading-block") + "\n}\n")
    functions.append(function("player.c", "spin_ball"))
    fixture_manifest = Path(fixture_manifest).resolve()
    manifest = json.loads(fixture_manifest.read_bytes())
    catalog_path = fixture_manifest.parent / manifest["playbook"]["path"]
    catalog = catalog_path.read_bytes()
    if sha(catalog) != "b180291759fb22c8ae7b2de7d417867e15243f6071bb1a672f11bb2fb6da4bd3":
        raise ValueError("Actual converted original TRIGS.H input must retain its pin.")
    trig = next(line.split("\t")[3] for line in catalog.decode().splitlines() if line.startswith("trig\t")).split(",")
    if len(trig) != 257:
        raise ValueError("Expected the actual 257-entry original quarter-sine input.")
    literals = ",".join(value + (".0" if "." not in value and "e" not in value.lower() else "") + "f" for value in trig)
    driver = (SUITE / "driver.c").read_text()
    driver = driver.replace("/* RUNTIME_ORIGINAL_SINE_TABLE */", "static const float sine_table[257]={" + literals + "};")
    driver = driver.replace("/* RUNTIME_ORIGINAL_FUNCTIONS */", "\n".join(tables + functions))
    caller = (SUITE / "caller_driver.c").read_text()
    pick_start, pick_end = function_window(player, "pick_receiver")
    constants = player.rindex(b"#define LEFTMOST_FWD", 0, pick_start)
    nfl = sources["nfl.c"]
    touchdown = nfl.index(b"case PR_TOUCHDOWN:")
    witness = re.search(rb"game_info\.down\s*=\s*-1;", nfl[touchdown:])
    if witness is None:
        raise ValueError("Missing original touchdown down sentinel assignment.")
    assignment = select("nfl.c", touchdown + witness.start(), touchdown + witness.end(),
                        "PR_TOUCHDOWN/original-down-assignment")
    callers = function("player.c", "plyr_have_ball") + "\n" + select(
        "player.c", constants, pick_start, "pick_receiver/original-selector-constants") + function(
            "player.c", "pick_receiver") + function("player.c", "target_cmp")
    callers += "\nstatic int original_touchdown_down(void) {\n" + assignment + "\nreturn game_info.down;\n}\n"
    caller = caller.replace("/* RUNTIME_ORIGINAL_CALLERS */", callers)
    compiler = shutil.which("gcc") or shutil.which("clang")
    if compiler is None:
        raise ValueError("A native C compiler is required.")
    work = output / "work"
    work.mkdir(exist_ok=True)
    environment = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work))
    commands = []
    for label, text in (("ball", driver), ("caller", caller)):
        generated, binary = output / (label + "-original.c"), output / (label + "-original")
        generated.write_bytes(text.encode("latin1"))
        command = [compiler, "-std=c99", "-O0", "-fno-fast-math", "-ffp-contract=off",
                   "-fexcess-precision=standard", str(generated), "-lm", "-o", str(binary)]
        commands.append(command)
        process = subprocess.run(command, env=environment, capture_output=True, text=True)
        (output / (label + "-compiler.log")).write_text(process.stdout + process.stderr)
        if process.returncode:
            raise ValueError("Pinned original C compilation failed; see " + label + "-compiler.log")
        if label == "ball":
            modes = [(None, "oracle-cases.tsv"), ("orientation", "orientation-cases.tsv")]
        else:
            modes = [("gate", "caller-gate-oracle.tsv"), ("receiver", "caller-receiver-oracle.tsv"),
                     ("sentinel", "pat-sentinel.tsv")]
        for mode, name in modes:
            with (output / name).open("wb") as stream:
                subprocess.run([str(binary)] + ([] if mode is None else [mode]), env=environment,
                               stdout=stream, check=True, timeout=120)
    rows = Counter(line.split("\t", 1)[0] for line in (output / "oracle-cases.tsv").read_text().splitlines())
    if dict(rows) != EXPECTED:
        raise ValueError("Original ball oracle case inventory changed: " + str(rows))
    for name, count in (("caller-gate-oracle.tsv", 800), ("caller-receiver-oracle.tsv", 12544),
                        ("pat-sentinel.tsv", 1), ("orientation-cases.tsv", ORIENTATION_CASES)):
        if len((output / name).read_text().splitlines()) != count:
            raise ValueError("Original caller oracle case inventory changed: " + name)
    outputs = {name: sha((output / name).read_bytes()) for name in (
        "oracle-cases.tsv", "caller-gate-oracle.tsv", "caller-receiver-oracle.tsv", "pat-sentinel.tsv",
        "orientation-cases.tsv")}
    receipt = {"schema": "source-ball-original-c/v1", "status": "passed", "source_blobs": PINS,
               "source_sha256": {name: sha(data) for name, data in sources.items()},
               "original_windows": windows, "inactive_debug_omissions": omitted, "commands": commands,
               "case_counts": dict(rows), "caller_gate_cases": 800, "receiver_cases": 12544,
               "sentinel_assignment_witness_cases": 1, "orientation_cases": ORIENTATION_CASES, "output_sha256": outputs,
               "boundary": "Original active decision/ball bodies; projected host structs, prescribed RNG, host sqrtf and qsort. Audio/effects callbacks excluded.",
               "original_target_binary_scheduler_random_qsort_or_full_game_parity": False}
    (output / "native-result.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print("PASS: runtime-extracted unchanged active original C / 46096 ball + 13344 caller cases + sentinel witness.")
    return receipt


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--fixture-manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=SUITE / ".artifacts/native")
    options = parser.parse_args()
    build(options.source_root, options.fixture_manifest, options.output)
