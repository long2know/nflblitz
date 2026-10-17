#!/usr/bin/env python3
"""Compare actual post-VM caller rows against runtime-extracted unchanged original player physics."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess


SUITE = Path(__file__).resolve().parent
ROOT = SUITE.parents[1]
PLAYER_BLOB = "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c"


def compare(source_root, cases, output):
    original = Path(source_root) / "player.c"
    raw = original.read_bytes()
    if hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest() != PLAYER_BLOB:
        raise ValueError("Actual original player.c blob does not match its exact source pin.")
    case_path = Path(cases).resolve()
    rows = case_path.read_text().splitlines()
    if len(rows) != 664 or any(len(row.split("\t")) != 22 for row in rows):
        raise ValueError("Expected the complete664 actual post-VM Core rows with22 fields each.")
    match = re.search(rb"\b(?:static\s+)?int\s+in_bounds\s*\(\s*obj_3d\s*\*\s*\w+\s*\)\s*\{", raw)
    if match is None:
        raise ValueError("Original in_bounds function was not found.")
    depth, end = 1, match.end()
    while depth:
        depth += (raw[end:end + 1] == b"{") - (raw[end:end + 1] == b"}")
        end += 1
    start = raw.index(b"\t\tif( !(pobj->adata[0].animode & MODE_PUPPET ))")
    stop = raw.index(b"\n\t\t#ifdef DEBUG", start)
    windows = [("in_bounds", match.start(), end), ("player_proc/full-active-physics", start, stop)]
    generated = (SUITE / "physics_driver.c").read_bytes().replace(
        b"/* ORIGINAL_BOUNDS */", raw[match.start():end]).replace(
        b"/* ORIGINAL_PLAYER_PHYSICS */", raw[start:stop])
    output = Path(output).resolve()
    if not output.is_relative_to(SUITE / ".artifacts"):
        raise ValueError("Native comparison artifacts must stay in this suite's ignored .artifacts.")
    output.mkdir(parents=True, exist_ok=True)
    work = output / "work"
    work.mkdir(exist_ok=True)
    path, binary = output / "actual-caller-physics.c", output / "actual-caller-physics"
    path.write_bytes(generated)
    compiler = shutil.which("gcc") or shutil.which("clang")
    if compiler is None:
        raise ValueError("A native C compiler is required.")
    environment = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work))
    command = [compiler, "-std=c99", "-O0", "-fno-fast-math", "-ffp-contract=off",
               "-fexcess-precision=standard", str(path), "-lm", "-o", str(binary)]
    build = subprocess.run(command, capture_output=True, text=True, env=environment, timeout=120)
    (output / "compiler.log").write_text(build.stdout + build.stderr)
    if build.returncode:
        raise RuntimeError("Original physics compilation failed, not a behavioral rejection.")
    with case_path.open("rb") as stream:
        result = subprocess.run([str(binary)], stdin=stream, capture_output=True,
                                text=True, env=environment, timeout=120)
    record = {"schema": "actual-source-movement-caller-physics/v1",
              "status": "passed" if result.returncode == 0 else "failed",
              "actual_Core_post_animation_inputs": len(rows), "exact_original_C_assertions": len(rows) * 8,
              "source_blob": PLAYER_BLOB, "source_sha256": hashlib.sha256(raw).hexdigest(),
              "actual_rows_sha256": hashlib.sha256(case_path.read_bytes()).hexdigest(),
              "command": command, "exit_code": result.returncode, "output": result.stdout + result.stderr,
              "original_windows": [
                  {"symbol": symbol, "start_byte": first, "end_byte_exclusive": last,
                   "first_line": raw[:first].count(b"\n") + 1, "sha256": hashlib.sha256(raw[first:last]).hexdigest(),
                   "unchanged_original_bytes": True} for symbol, first, last in windows],
              "boundary_first_header_used": False, "MIPS_binary_parity_claimed": False}
    (output / "result.json").write_text(json.dumps(record, indent=2) + "\n")
    print(record["output"], end="")
    if result.returncode:
        raise RuntimeError("Actual post-VM production state differs from original C; see native result.")
    if hashlib.sha256(original.read_bytes()).hexdigest() != record["source_sha256"]:
        raise RuntimeError("Original source changed during native comparison.")
    return record


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--cases", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=SUITE / ".artifacts/native")
    args = parser.parse_args()
    compare(args.source_root, args.cases, args.output)
