#!/usr/bin/env python3
"""Independent bounded C replay using pinned original quaternion functions.

Original functions are extracted only into ignored local outputs. No source
motion, original function implementation, historical executable or lookup table
is included in this test file.
"""

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import struct
import subprocess


ROOT = Path(__file__).resolve().parents[3]
SCRATCH = ROOT / ".squad" / ".scratch" / "source-rskip-export"

C_READER = r'''
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#define X 0
#define Y 1
#define Z 2
#define W 3
typedef float matrix[3][3];
typedef float quat[4];
static quat frames[1024][15];
static int owner[1024][15], address[1024][15];
static float roots[1024][3];
static int flags[1024];
static unsigned char *data;
static size_t length;
static void need(int ok) { if (!ok) { fputs("bounded raw C replay assertion\n", stderr); exit(3); } }
static uint32_t u32(size_t p) {
    need(p + 4 <= length);
    return (uint32_t)data[p] | (uint32_t)data[p+1] << 8 |
        (uint32_t)data[p+2] << 16 | (uint32_t)data[p+3] << 24;
}
static uint32_t be32(size_t p) {
    need(p + 4 <= length);
    return (uint32_t)data[p] << 24 | (uint32_t)data[p+1] << 16 |
        (uint32_t)data[p+2] << 8 | (uint32_t)data[p+3];
}
static float f32(size_t p) {
    uint32_t bits = u32(p); float value; memcpy(&value, &bits, 4);
    need(isfinite(value)); return value;
}
/* Original pinned functions are inserted here by the local test runner. */
'''

C_MAIN = r'''
int main(int argc, char **argv) {
    need(argc == 3 && sizeof(float) == 4 && sizeof(uint32_t) == 4);
    FILE *input = fopen(argv[1], "rb"); need(input != NULL);
    need(!fseek(input, 0, SEEK_END)); long size = ftell(input);
    need(size > 8 && size <= 67108864); length = (size_t)size;
    rewind(input); data = malloc(length); need(data != NULL);
    need(fread(data, 1, length, input) == length); fclose(input);
    need(!memcmp(data, "WIFF", 4) && be32(4) == length - 8);
    int found = 0;
    for (size_t chunk = 8; chunk < length;) {
        need(chunk + 16 <= length);
        size_t end = chunk + 8 + be32(chunk + 4);
        need(end <= length && end >= chunk + 16);
        if (!memcmp(data + chunk, "3AN ", 4)) {
            size_t body = chunk + 16;
            need(body + 68 <= end && u32(body + 24) == 15);
            uint32_t aflags = u32(body + 20);
            need(!(aflags & ~0x4001U));
            if (aflags & 0x4000) {
                int number = (int)be32(chunk + 8), count = -1, seen[16] = {0};
                size_t p = body + 68;
                for (;;) {
                    need(p + 4 <= end);
                    int32_t kind = (int32_t)u32(p); p += 4;
                    if (!kind) { need(p == end); break; }
                    need(p + 4 <= end);
                    int n = (int)u32(p); p += 4;
                    need(n > 0 && n <= 1024 && (count == -1 || count == n)); count = n;
                    int stride = kind == -1 ? 4 : kind == -2 ? 12 : 48;
                    need(p + (size_t)n * stride <= end);
                    if (kind == -1) {
                        for (int i = 0; i < n; i++) flags[i] = (int)u32(p + 4*i);
                    } else if (kind == -2) {
                        for (int i = 0; i < n; i++)
                            for (int a = 0; a < 3; a++) roots[i][a] = f32(p + i*12 + a*4);
                    } else {
                        need(kind >= 1 && kind <= 15 && !seen[kind]); seen[kind] = 1;
                        for (int i = 0; i < n; i++) {
                            matrix m; quat q;
                            for (int a = 0; a < 9; a++) m[a/3][a%3] = f32(p + i*48 + 12 + a*4);
                            mat2quat(m, q);
                            if (i) adjust_quat(q, frames[i-1][kind-1]);
                            memcpy(frames[i][kind-1], q, sizeof(quat));
                            owner[i][kind-1] = number;
                            address[i][kind-1] = (int)(p + i*48 + 12);
                        }
                    }
                    p += (size_t)n * stride;
                }
                if (!memcmp(data + body, "r_skip", 7)) {
                    need(!found && count == 70 && !seen[5] && aflags == 0x4000);
                    found = 1;
                    FILE *out = fopen(argv[2], "wb"); need(out != NULL);
                    need(fwrite("WANORCL1", 1, 8, out) == 8);
                    uint32_t total = 70; need(fwrite(&total, 4, 1, out) == 1);
                    for (int i = 0; i < count; i++) {
                        need(flags[i] & 0x4000);
                        uint32_t index = (uint32_t)i;
                        fwrite(&index, 4, 1, out); fwrite(&flags[i], 4, 1, out);
                        fwrite(roots[i], 4, 3, out);
                        float scaled[3];
                        for (int a = 0; a < 3; a++) scaled[a] = roots[i][a] * 0.12898f;
                        fwrite(scaled, 4, 3, out); fwrite(frames[i], 4, 60, out);
                        fwrite(owner[i], 4, 15, out); fwrite(address[i], 4, 15, out);
                    }
                    need(!ferror(out) && !fclose(out));
                }
            }
        }
        chunk = end + (end & 1);
    }
    need(found); free(data); return 0;
}
'''


def blob(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()


def extract_function(text, declaration):
    match = re.search(declaration + r"\s*\{", text)
    if match is None:
        raise ValueError("pinned original function declaration missing")
    start, position, depth = match.start(), match.end(), 1
    while depth:
        character = text[position]
        depth += (character == "{") - (character == "}")
        position += 1
    return text[start:position]


def run(wan, converter, clip, output):
    output = output.resolve()
    if not output.is_relative_to(SCRATCH.resolve()) or output == SCRATCH.resolve():
        raise ValueError("oracle output must remain in the owned ignored source-rskip-export subtree")
    if output.exists():
        raise ValueError("oracle output directory must be new")
    original = converter.read_bytes()
    if blob(original) != "33c7e489d9b7a40d3b0cd435236db8c357d143fe":
        raise ValueError("original converter source blob mismatch")
    data = wan.read_bytes()
    if blob(data) != "256acb7ddb838627312193ae19db66abd18ac146":
        raise ValueError("original misc99 WAN blob mismatch")
    text = original.decode("ascii")
    functions = "\n".join(extract_function(text, declaration) for declaration in (
        r"void mat2quat\( matrix mat, quat q \)",
        r"void adjust_quat\( quat p, quat q \)",
    ))
    output.mkdir(parents=True)
    source, executable, binary = output / "oracle.c", output / "oracle", output / "oracle.bin"
    source.write_text(C_READER + functions + C_MAIN, encoding="ascii")
    env = dict(os.environ)
    work = output / "work"
    work.mkdir()
    for key in ("TMPDIR", "TMP", "TEMP"):
        env[key] = str(work)
    subprocess.run(["gcc", "-std=c99", "-O0", "-ffp-contract=off", str(source),
                    "-lm", "-o", str(executable)], env=env, check=True, capture_output=True)
    subprocess.run([str(executable), str(wan.resolve()), str(binary)],
                   env=env, check=True, capture_output=True)
    raw = binary.read_bytes()
    assert raw[:8] == b"WANORCL1" and struct.unpack_from("<I", raw, 8)[0] == 70
    frame_size = 8 + (6 + 60 + 15 + 15) * 4
    assert len(raw) == 12 + 70 * frame_size
    decoded = json.loads(clip.read_bytes())
    assert len(decoded["frames"]) == 70
    max_q = max_root = 0.
    for i, frame in enumerate(decoded["frames"]):
        values = struct.unpack_from("<II66f30i", raw, 12 + i * frame_size)
        index, flags = values[:2]
        assert index == i == frame["source_frame_index"] == frame["exported_frame_index"]
        assert flags == frame["source_frame_flags"] == 0x4000
        assert tuple(frame["root_translation"]["authoring_xyz"]) == values[2:5]
        max_root = max(max_root, *(abs(a-b) for a, b in zip(
            frame["root_translation"]["scaled_runtime_xyz"], values[5:8])))
        for joint, q in enumerate(frame["rotations_xyzw"]):
            original_q = values[8 + joint * 4:12 + joint * 4]
            max_q = max(max_q, *(abs(a-b) for a, b in zip(q, original_q)))
            assert values[68 + joint] == (12 if joint == 4 else 13), "original C last-writer ownership"
        assert values[83 + 4] == 2820422 + i * 48 + 12, "original C retained matrix byte identity"
    assert math.isfinite(max_q) and max_q < 0.000002, "original float C quaternion agreement"
    assert math.isfinite(max_root) and max_root < 0.00002, "original float C root agreement"
    report = {
        "schema": "source-rskip-original-c-oracle/v1", "frames": 70,
        "quaternions": 70 * 15, "target_matrix_records": 14,
        "retained_limb_id": 5, "retained_original_chunk": 12,
        "all_original_C_last_writers_checked": True,
        "original_converter_blob": blob(original), "original_wan_blob": blob(data),
        "max_quaternion_component_error": max_q, "max_scaled_root_error": max_root,
        "oracle_binary_sha256": hashlib.sha256(raw).hexdigest(),
        "oracle_source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
        "independence": "Separate bounded C raw-WAN parser and pinned original C float mat2quat/adjust_quat; no Python decoder imports.",
        "limits": "Source-converter state replay, not the historical executable or shipped SEQ/MIPS binary; Python doubles intentionally are not C float bit identity.",
    }
    (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--wan", type=Path, required=True)
    parser.add_argument("--converter-source", type=Path, required=True)
    parser.add_argument("--clip", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(run(args.wan, args.converter_source, args.clip, args.output), sort_keys=True))
