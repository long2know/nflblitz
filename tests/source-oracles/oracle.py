#!/usr/bin/env python3
"""Compile byte-verified original C slices; never reproduce their rules in Python."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess


SUITE = Path(__file__).resolve().parent
ROOT = SUITE.parents[1]
PINS = json.loads((SUITE / "pins.json").read_text(encoding="utf-8"))
FLAGS = [
    "-std=c11", "-O0", "-fno-fast-math", "-ffp-contract=off",
    "-fexcess-precision=standard", "-fno-strict-aliasing", "-Wall", "-Wextra",
]


class OracleError(RuntimeError):
    pass


def blob_sha1(data: bytes) -> str:
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()


def verified_sources(source_root: Path) -> dict[str, bytes]:
    sources = {}
    for name, expected in PINS["blobs"].items():
        path = source_root / name
        if not path.is_file():
            raise OracleError(f"Required user-supplied original source is missing: {path}")
        data = path.read_bytes()
        actual = blob_sha1(data)
        if actual != expected:
            raise OracleError(f"Source blob mismatch: {name}; expected {expected}, actual {actual}")
        sources[name] = data
    return sources


def matching_brace(data: bytes, opening: int) -> int:
    tokens = re.finditer(
        rb'/\*.*?\*/|//[^\r\n]*|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|[{}]',
        data[opening:], re.DOTALL,
    )
    depth = 0
    for token in tokens:
        if token.group() == b"{":
            depth += 1
        elif token.group() == b"}":
            depth -= 1
            if depth == 0:
                return opening + token.end()
    raise OracleError("Unbalanced original C selection")


def function_window(data: bytes, symbol: str) -> tuple[int, int]:
    pattern = (
        rb"(?m)^(?:static\s+)?(?:void|float|int)\s+" + symbol.encode("ascii")
        + rb"\s*\([^;{}]*\)\s*\{"
    )
    matches = list(re.finditer(pattern, data))
    if len(matches) != 1:
        raise OracleError(f"Expected one original function body: {symbol}; found {len(matches)}")
    match = matches[0]
    opening = data.index(b"{", match.start(), match.end())
    return match.start(), matching_brace(data, opening)


def unique_offset(data: bytes, marker: bytes) -> int:
    if data.count(marker) != 1:
        raise OracleError(f"Original selection marker is not unique: {marker!r}")
    return data.index(marker)


class Oracle:
    def __init__(self, source_root: Path, artifacts: Path | None = None, compiler: str | None = None):
        self.source_root = source_root.resolve()
        self.artifacts = (artifacts or SUITE / ".artifacts" / "run").resolve()
        if ROOT not in self.artifacts.parents:
            raise OracleError("Oracle artifacts must stay inside this project, never in temporary directories.")
        self.artifacts.mkdir(parents=True, exist_ok=True)
        self.sources = verified_sources(self.source_root)
        self.selections: list[dict] = []
        self.compiler = shutil.which(compiler) if compiler else (shutil.which("gcc") or shutil.which("clang"))
        if not self.compiler:
            raise OracleError("No gcc/clang found; install only after this demonstrated compiler failure.")
        self.environment = dict(os.environ)
        work = self.artifacts / "work"
        work.mkdir(exist_ok=True)
        self.environment.update(TMPDIR=str(work), TMP=str(work), TEMP=str(work))
        self.executable = self.artifacts / "original-oracle"
        self.build()

    def select(self, name: str, start: int, end: int, symbol: str) -> bytes:
        data = self.sources[name]
        selected = data[start:end]
        if not selected:
            raise OracleError(f"Empty original C selection: {symbol}")
        self.selections.append({
            "path": name, "symbol": symbol, "start_byte": start, "end_byte_exclusive": end,
            "first_line": data[:start].count(b"\n") + 1,
            "last_line": data[:end].count(b"\n") + 1,
            "sha256": hashlib.sha256(selected).hexdigest(),
            "unmodified_original_bytes": True,
        })
        first_line = data[:start].count(b"\n") + 1
        directive = f'\n#line {first_line} "{name}"\n'.encode("ascii")
        return directive + selected + b"\n"

    def function(self, name: str, symbol: str) -> bytes:
        return self.select(name, *function_window(self.sources[name], symbol), symbol)

    def macros(self, name: str, names: set[str], prefixes: tuple[str, ...] = ()) -> bytes:
        data = self.sources[name]
        output = []
        seen = set()
        for match in re.finditer(rb"(?m)^#\s*define\s+([A-Za-z_]\w*)[^\r\n]*", data):
            symbol = match.group(1).decode("ascii")
            if symbol not in names and not symbol.startswith(prefixes):
                continue
            if symbol in seen:
                raise OracleError(f"Duplicate macro selection: {name}:{symbol}")
            seen.add(symbol)
            output.append(self.select(name, match.start(), match.end(), symbol))
        missing = names - seen
        if missing:
            raise OracleError(f"Missing original macros: {sorted(missing)}")
        return b"".join(output)

    def struct(self, name: str, tag: str, typedef: str) -> bytes:
        data = self.sources[name]
        matches = list(re.finditer(rb"(?m)^typedef struct " + tag.encode("ascii") + rb"\r?$", data))
        if len(matches) != 1:
            raise OracleError(f"Expected one original struct declaration: {tag}")
        start = matches[0].start()
        end = unique_offset(data, f"}} {typedef};".encode("ascii")) + len(typedef) + 3
        return self.select(name, start, end, typedef)

    def build(self):
        generated = [
            b"/* Generated from verified external original source; ignored, not redistributable fixtures. */\n",
            b"#include <stddef.h>\n",
            self.macros("include/GAME.H", {
                "FALSE", "TRUE", "N_TYPES", "N_CAUSES", "NUM_QUARTERS", "MAX_QUARTERS",
                "QUARTER_TIME", "FIELDHZ", "FOCFAC", "FIELDX", "FIELDX2", "WORLDX",
                "WORLDX2", "LESSER", "GREATER", "MAX_HIKE_TIME", "CONTROL_TIME",
            }, ("PT_", "PEC_", "PR_", "PRF_", "GF_", "GS_")),
            self.macros("include/player.h", set(), ("LB_",)),
            b"typedef struct original_opaque_play play_t;\n",
            self.struct("include/GAME.H", "tdata", "tdata_t"),
            self.struct("include/GAME.H", "gameinfo", "game_info_t"),
            b'\n#line 1 "oracle-shim-globals"\n',
            b"game_info_t game_info;\ntdata_t tdata_blocks[2];\n",
            b"struct { int type; } ball_obj;\nint fpend;\nint clock_active;\n",
        ]
        result = self.sources["result.c"]
        start = unique_offset(result, b"static void _norm_car_oob( void );")
        generated.append(self.select("result.c", start, len(result), "result_functions + all 30 original functions"))
        nfl = self.sources["nfl.c"]
        table_start = unique_offset(nfl, b"static int time_table[5]")
        table_end = nfl.index(b"};", table_start) + 2
        generated.append(self.select("nfl.c", table_start, table_end, "time_table"))
        generated.append(b'\n#line 1 "oracle-shim-boundaries"\n')
        generated.append((SUITE / "boundaries.c").read_bytes())
        generated.append(self.function("nfl.c", "game_clock_proc"))
        generated.append(b"\n#define rand oracle_rand\n")
        generated.append(self.function("nfl.c", "cpu_do_onside"))
        generated.append(b"\n#undef rand\n")
        clock_start, clock_end = function_window(nfl, "game_clock_proc")
        loop_start = nfl.index(b"while (gt != 0)", clock_start, clock_end)
        loop_end = matching_brace(nfl, nfl.index(b"{", loop_start, clock_end))
        generated.append(b"\nstatic void original_clock_iteration(void) {\nint gt = game_info.game_time;\nint ticks_per_second;\n")
        generated.append(self.select("nfl.c", loop_start, loop_end, "game_clock_proc:entire original while loop"))
        generated.append(b"}\nstatic void original_spot_and_classify(void) {\n")
        spot_start = unique_offset(nfl, b"\t\t// round play-end field position toward")
        spot_end = nfl.index(b"#ifndef NO_FIRE", spot_start)
        generated.append(self.select("nfl.c", spot_start, spot_end, "play-end rounding + dispatch + touchdown clamp"))
        generated.append(b"}\nstatic void original_discard_fraction(void) {\n")
        discard_start = unique_offset(nfl, b"\t\tgame_info.game_time = game_info.game_time & 0x00FFFF00;")
        discard_end = nfl.index(b";", discard_start) + 1
        generated.append(self.select("nfl.c", discard_start, discard_end, "end-of-play fractional discard"))
        generated.append(b"}\n")
        generated.append(self.struct("include/ani3d.h", "_vec3d", "vec3d"))
        generated.append(b"\n#define fsqrt sqrtf\n")
        for symbol in ("vxv", "magv", "det"):
            generated.append(self.function("lib/ani3d/pipe3D.c", symbol))
        generated.append(self.function("detect.c", "point2line_d"))
        generated.append(self.macros("detect.c", {"CLAMP"}))
        generated.append(self.function("detect.c", "line_int_check"))
        generated.append(b'\n#line 1 "oracle-shim-main"\n')
        generated.append((SUITE / "driver.c").read_bytes())
        self.unit = self.artifacts / "original-unit.c"
        self.unit.write_bytes(b"".join(generated))
        command = [self.compiler, *FLAGS, str(self.unit), "-lm", "-o", str(self.executable)]
        built = subprocess.run(command, env=self.environment, capture_output=True, text=True, cwd=ROOT)
        (self.artifacts / "compiler.log").write_text(built.stdout + built.stderr, encoding="utf-8")
        if built.returncode:
            raise OracleError(f"Original C compilation failed:\n{built.stdout}{built.stderr}")
        version = subprocess.run([self.compiler, "--version"], capture_output=True, text=True, check=True)
        self.compiler_record = {
            "path": self.compiler, "version": version.stdout.splitlines()[0],
            "command": command, "flags": FLAGS, "warnings": built.stderr,
            "generated_unit_sha256": hashlib.sha256(self.unit.read_bytes()).hexdigest(),
            "binary_sha256": hashlib.sha256(self.executable.read_bytes()).hexdigest(),
        }

    def execute(self, commands: list[str]) -> list[dict]:
        process = subprocess.run(
            [str(self.executable)], input="\n".join(commands) + "\n",
            capture_output=True, text=True, env=self.environment, cwd=ROOT, timeout=60,
        )
        if process.returncode:
            raise OracleError(f"Original C oracle failed ({process.returncode}): {process.stderr}")
        rows = [json.loads(line) for line in process.stdout.splitlines()]
        if len(rows) != len(commands):
            raise OracleError(f"Expected {len(commands)} original C results, received {len(rows)}")
        self.last_stderr = process.stderr
        return rows

    def manifest(self) -> dict:
        return {
            "schema": "original-c-oracle-evidence/v1",
            "source": PINS, "compiler": self.compiler_record,
            "field_layout_and_macros": self.execute(["meta"])[0],
            "selections": self.selections,
            "limitations": [
                "Native host compiler, not the original MIPS compiler/ABI or exact arcade binary.",
                "Original complete game_info_t/tdata_t declarations; play_t is an opaque pointer.",
                "ball_obj projects only its original int type field; no source code here depends on ball layout.",
                "Sound and adjustment IDs are symbolic boundary handles, not source audio/CMOS numeric parity.",
                "sleep/die are controlled observation boundaries, not Goose scheduling.",
                "cpu_do_onside receives explicitly supplied raw nonnegative C rand values; no libc or MIPS RNG sequence is invented. compute_score returns supplied team totals, not a scoring-aggregation oracle.",
                "line_int_check executes original vxv/magv/det; the platform fsqrt primitive is host sqrtf, not a MIPS math-library claim.",
                "Clock custom-state calls execute the entire unchanged original while body; clock-init also executes original initialization.",
                "No source motion data, complete engine emulation, underlying platform RNG or browser execution.",
            ],
        }
