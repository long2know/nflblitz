#!/usr/bin/env python3
"""Extract unmodified original movement C, with explicit input/animation/audio boundaries."""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

from fetch_inputs import PINS, ROOT, SUITE, blob_sha1

TABLES = (
    "running_taunt_tbl", "push_anim_tbl", "intercept_anim_tbl", "hurdle_tbl",
    "turbo_hurdle_tbl", "breath_seq_tbl", "violent_db_late_hits_tbl", "db_late_hits_tbl",
)
LOCAL_TABLES = ("run_seqs", "run_scripts", "int_run_scripts", "qb_runs", "qb_seqs")


def closing(data, start):
    depth = 0
    for match in re.finditer(rb'/\*.*?\*/|//[^\r\n]*|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|[{}]', data[start:], re.S):
        token = match.group()
        if token == b"{":
            depth += 1
        elif token == b"}":
            depth -= 1
            if depth == 0:
                return start + match.end()
    raise RuntimeError("Unbalanced original C")


def function_span(data, symbol):
    matches = list(re.finditer(rb"(?m)^(?:static\s+)?(?:void|int|float)\s+" + symbol.encode() + rb"\s*\([^;{}]*\)\s*\{", data))
    if len(matches) != 1:
        raise RuntimeError("Expected one original function " + symbol)
    match = matches[0]
    return match.start(), closing(data, data.index(b"{", match.start(), match.end()))


def block_span(data, marker, start=0):
    at = data.index(marker, start)
    return at, closing(data, data.index(b"{", at))


def array_span(data, symbol):
    matches = list(re.finditer(rb"(?m)^[ \t]*(?:static\s+)?(?:const\s+)?(?:char\s*\*|int|float)\s*" +
                               symbol.encode() + rb"\s*(?:\[\d*\])+\s*=\s*\{", data))
    if len(matches) != 1:
        raise RuntimeError("Expected one original array " + symbol + ", found " + str(len(matches)))
    at = matches[0].start()
    return at, closing(data, data.index(b"{", at)) + 1


class Oracle:
    def __init__(self):
        self.artifacts = SUITE / ".artifacts"
        self.artifacts.mkdir(exist_ok=True)
        self.sources = {}
        for name, expected in PINS["blobs"].items():
            data = (self.artifacts / "original" / name).read_bytes()
            if blob_sha1(data) != expected:
                raise RuntimeError("Movement source hash mismatch: " + name)
            self.sources[name] = data
        self.selections = []
        self.sites = {}
        self.build()

    def select(self, name, start, end, symbol):
        data = self.sources[name]
        selected = data[start:end]
        first = data[:start].count(b"\n") + 1
        self.selections.append({"path": name, "symbol": symbol, "first_line": first,
                                "last_line": data[:end].count(b"\n") + 1,
                                "start_byte": start, "end_byte_exclusive": end,
                                "sha256": hashlib.sha256(selected).hexdigest(), "unmodified_original_bytes": True})
        return ('\n#line %d "%s"\n' % (first, name)).encode() + selected + b"\n"

    def function(self, name, symbol):
        return self.select(name, *function_span(self.sources[name], symbol), symbol)

    def array(self, name, symbol):
        return self.select(name, *array_span(self.sources[name], symbol), symbol)

    def macros(self, name, prefixes=(), names=()):
        result = []
        for match in re.finditer(rb"(?m)^#\s*define\s+(\w+)[^\r\n]*", self.sources[name]):
            symbol = match.group(1).decode()
            if symbol in names or symbol.startswith(prefixes):
                result.append(self.select(name, match.start(), match.end(), symbol))
        return b"".join(result)

    def macro(self, name, symbol):
        data = self.sources[name]
        match = re.search(rb"(?m)^#define[ \t]+" + symbol.encode() + rb"\(", data)
        if not match:
            raise RuntimeError("Missing original macro " + symbol)
        start = match.start()
        end = data.index(b"\n", start) + 1
        while data[start:end].rstrip(b"\r\n").endswith(b"\\"):
            end = data.index(b"\n", end) + 1
        return self.select(name, start, end, symbol)

    def scripts(self):
        player = self.sources["player.c"]
        all_tables = {}
        for name in TABLES + LOCAL_TABLES:
            start, end = array_span(player, name)
            body = player[start:end]
            body = re.sub(rb"//[^\r\n]*|/\*.*?\*/", b"", body, flags=re.S)
            initializer = body[body.index(b"{") + 1:body.rindex(b"}")]
            values = [v.decode().removeprefix("SEQ_") for v in re.findall(rb"\b[A-Za-z_]\w*\b", initializer)]
            all_tables[name] = values
        # Source-header responses are an explicit _animate_block fixture, not an animation VM.
        text = self.sources["plyrseq.c"].decode("latin1")
        responses = {}
        symbols = set(v for table in all_tables.values() for v in table)
        for symbol in re.findall(r"\b([A-Za-z_]\w*_anim(?:_int|_rnd|_top|_bot)?)\b", player.decode("latin1")):
            symbols.add(symbol)
        symbols.update(["fb_mill1", "fb_mill2", "fb_mill3", "juke_c_anim"])
        for symbol in sorted(symbols):
            match = re.search(r"(?m)^" + re.escape(symbol) + r":\s*$", text)
            if not match:
                continue
            tail = text[match.end():]
            # Stop before another externally visible script; internal labels belong to this script.
            stop = re.search(r"(?m)^\s*\.globl\s+", tail)
            if stop:
                tail = tail[:stop.start()]
            lines = [line.strip() for line in tail.splitlines() if not line.lstrip().startswith("#")]
            mode = None
            sequence = None
            zero = False
            velocity = None
            at = 0
            while at < len(lines):
                line = lines[at]
                if re.match(r"\.byte\s+\d", line) or "ANI_INTTOSEQ" in line or "ANI_INTSTREAM" in line:
                    if "ANI_INTTOSEQ" in line and at + 1 < len(lines):
                        seq = re.search(r"\bSEQ_(\w+)", lines[at + 1])
                        if seq:
                            sequence = seq.group(1)
                    break
                if "ANI_SETMODE" in line and at + 1 < len(lines):
                    m = re.search(r"\.word\s+(.+)", lines[at + 1])
                    if m:
                        mode = m.group(1).strip()
                if "ANI_NEWSEQ" in line and at + 1 < len(lines):
                    seq = re.search(r"\bSEQ_(\w+)", lines[at + 1])
                    if seq:
                        sequence = seq.group(1)
                if "ANI_ZEROVELS" in line:
                    zero = True
                if "ANI_SETVELS" in line and at + 1 < len(lines):
                    m = re.search(r"\.float\s+([-\d., ]+)", lines[at + 1])
                    if m:
                        velocity = [float(v) for v in m.group(1).split(",")]
                at += 1
            responses[symbol] = {"mode_expression": mode, "sequence": sequence, "zero": zero, "velocity": velocity}
        return all_tables, responses

    def build(self):
        player = self.sources["player.c"]
        tables, responses = self.scripts()
        # Constants and arrays are exported ONLY into ignored artifacts.
        symbols = set(re.findall(rb"\b[A-Za-z_]\w*_anim(?:_int|_rnd|_top|_bot)?\b", player))
        symbols.discard(b"change_anim")
        for values in tables.values():
            symbols.update(v.encode() for v in values if not v.isupper())
        for name in ["run_seqs", "qb_seqs"]:
            for value in tables[name]:
                symbols.discard(value.encode())
        symbols.update(k.encode() for k in responses)
        sequences = set(re.findall(rb"\bSEQ_(\w+)\b", player))
        sequences.update(v["sequence"].encode() for v in responses.values() if v["sequence"])
        self.sequence_names = sorted(v.decode() for v in sequences)
        declarations = b"".join(
            b'static struct { unsigned char tag; char text[sizeof("' + name + b'")]; } script_' + name +
            b'={0x80,"' + name + b'"};\n#define ' + name + b' (script_' + name + b'.text)\n'
            for name in sorted(symbols))
        declarations += b"static const struct {const char *name;char *script;} script_symbols[]={"
        declarations += b",".join(b'{"' + name + b'",' + name + b"}" for name in sorted(symbols)) + b"};\n"
        declarations += b"".join(("#define SEQ_%s %d\n" % (name, i + 1)).encode() for i, name in enumerate(self.sequence_names))
        declarations += b"static const char *sequence_names[]={\"\","
        declarations += b",".join(json.dumps(name).encode() for name in self.sequence_names) + b"};\n"
        response_c = []
        for symbol, response in responses.items():
            if symbol.encode() not in symbols:
                continue
            mode = response["mode_expression"] or "-1"
            seq = "SEQ_" + response["sequence"] if response["sequence"] else "0"
            velocity = response["velocity"] or [0, 0, 0]
            response_c.append('{"%s",%s,%s,%d,%d,%s,%s,%s}' % (
                symbol, mode, seq, response["zero"], response["velocity"] is not None, *[repr(float(v)) + "f" for v in velocity]))
        generated = [
            b"/* Generated from hash-verified external source. Ignored; no upstream scripts run. */\n",
            b"#include <stdio.h>\n#include <string.h>\n#include <stdlib.h>\n#include <stdint.h>\n#include <math.h>\n",
            self.macros("include/player.h", ("PF_", "MODE_", "JOB_", "LB_", "QB_", "WPN_", "CARRIER_", "DB_", "LINE_", "HUMAN_"),
                        ("ISHUMAN", "ISDRONE", "WPNPS_SPEED", "DBPS_SPEED", "DBPS_TURBO_SPEED")),
            self.macros("include/ani3d.h", ("MODE_",), ("GRAVITY", "AH_COMPRESSED", "AA_PROPFRAME", "SCRIPT",
                                                        "AA_TOGROUND", "AA_TGT_CARRIER", "AA_TGT_TARGET")),
            self.macros("include/PlayStuf.h", ("PLF_",)),
            self.macros("include/GAME.H", ("GM_", "PT_", "GF_", "PLF_"),
                        ("TRUE", "FALSE", "FIELDW", "FIELDHB", "FIELDHZ", "FIELDX", "WORLDX", "FOCFAC",
                         "FRELX", "FRELZ", "LESSER", "GREATER", "ABS", "YD2UN", "NUM_PLAYERS")),
            b"\n#define TEAM_MASK(x) (1 << (x))\n#define PLYRH(x) (game_info.team_handicap[(x)->team])\n",
            b"#define P_A 1\n#define P_B 2\n#define P_C 4\n#define P_D 8\n#define MOTION_WIDTH 60.0f\n",
            self.macro("lib/ani3d/ANIM.C", "GETWORD"),
            self.select("lib/ani3d/ANIM.C",
                        self.sources["lib/ani3d/ANIM.C"].index(b"typedef union intfloat"),
                        self.sources["lib/ani3d/ANIM.C"].index(b"} intfloat_t;") + len(b"} intfloat_t;"),
                        "intfloat_t"),
            declarations,
            (SUITE / "boundary_types.c").read_bytes(),
            self.array("lib/ani3d/TRIGS.H", "trigs"),
            self.function("lib/ani3d/pipe3D.c", "isin"),
            self.function("lib/ani3d/pipe3D.c", "icos"),
            self.function("lib/ani3d/pipe3D.c", "iasin"),
            self.function("lib/ani3d/pipe3D.c", "iacos"),
            self.function("lib/ani3d/ANIM.C", "ptangle"),
            self.function("lib/ani3d/ANIM.C", "_mid_angle"),
            self.function("lib/ani3d/ANIM.C", "_ani_leapatt"),
            self.array("player.c", "dir49_table"),
            self.array("player.c", "field_friction_mult_factor"),
            *[self.array("player.c", name) for name in TABLES],
            *[self.array("handicap.c", name) for name in ("plow_ht", "dback_speed_ht", "dline_speed_ht", "def_acceltime_ht")],
            b"\nstatic script_response responses[]={" + ",\n".join(response_c).encode() + b"};\n",
            (SUITE / "boundaries.c").read_bytes(),
        ]
        # Original change_anim/1/2 execute to the explicit metadata-only _animate_block seam.
        for symbol in ("change_anim", "change_anim1", "change_anim2"):
            generated.append(self.function("lib/ani3d/ANIM.C", symbol))
        generated.append(b"""
static void oracle_change_anim(obj_3d *p,char *s){animation_call_kind=0;change_anim(p,(char *)script_name(s));}
static void oracle_change_anim1(obj_3d *p,char *s){animation_call_kind=1;change_anim1(p,(char *)script_name(s));}
static void oracle_change_anim2(obj_3d *p,char *s){animation_call_kind=2;change_anim2(p,(char *)script_name(s));}
#define change_anim oracle_change_anim
#define change_anim1 oracle_change_anim1
#define change_anim2 oracle_change_anim2
""")
        generated.append(b"\n#define begin_twopart_mode original_begin_twopart_mode\n")
        generated.append(self.function("player.c", "begin_twopart_mode"))
        generated.append(b"\n#undef begin_twopart_mode\n")
        generated.append(b"""static void begin_twopart_mode(obj_3d *pobj) {
    if (!(pobj->adata[1].animode & MODE_TWOPART)) log_command(3, NULL);
    original_begin_twopart_mode(pobj);
}\n""")
        for symbol in ("in_bounds", "zerovels", "turn_toward_tgt", "unique_random", "standard_run", "qb_run", "general_run", "maybe_dive", "slower_spin"):
            generated.append(self.function("player.c", symbol))
        generated.append(self.function("pmisc.c", "set_defense_speeds"))
        proc_start, proc_end = function_span(player, "player_proc")
        a = player.index(b"\t\tif( ppdata->stick_cur == ppdata->stick_old )", proc_start, proc_end)
        b = player.index(b"\t\t// get current switches", a, proc_end)
        generated.append(b"\nstatic void input_head(fbplyr_data *ppdata) {obj_3d *pobj=&ppdata->odata;int was_backp;\n")
        generated.append(self.select("player.c", a, b, "player_proc:history + backpedal clear"))
        generated.append(b"\nsaved_backpedal=was_backp;\n}\n")
        a = player.index(b"\t\tif (pobj->flags & PF_BACKPEDAL)", b, proc_end)
        b = player.index(b"\n#ifdef DEBUG", a, proc_end)
        generated.append(b"\nstatic void input_tail(fbplyr_data *ppdata) {obj_3d *pobj=&ppdata->odata;int was_backp=saved_backpedal;\n")
        generated.append(self.select("player.c", a, b, "player_proc:backpedal persistence + new buttons"))
        a = player.index(b"\t\t// decrement timer on attack boxes", b, proc_end)
        b = player.index(b"\t\t// call plyrmode function", a, proc_end)
        generated.append(self.select("player.c", a, b, "player_proc:timers + turbo + A/B double presses"))
        generated.append(b"\n}\n")
        animation = self.sources["lib/ani3d/ANIM.C"]
        leap_start, leap_end = function_span(animation, "_ani_leapatt")
        a = animation.index(b"\t// if victim is AA_TGT_CARRIER", leap_start, leap_end)
        b = animation.index(b"\n//\tfprintf", a, leap_end)
        generated.append(b"\nstatic int leap_resolve_victim(obj_3d *pobj,int victim){\n")
        generated.append(self.select("lib/ani3d/ANIM.C", a, b, "_ani_leapatt:exact original victim resolution"))
        generated.append(b"\nreturn victim;}\n")
        a, b = block_span(animation, b"\tif( time == AA_TOGROUND )", leap_start)
        generated.append(b"\nstatic int leap_resolve_time(obj_3d *pobj,int time,float offy){float t;\n")
        generated.append(self.select("lib/ani3d/ANIM.C", a, b, "_ani_leapatt:exact original AA_TOGROUND time"))
        generated.append(b"\nreturn time;}\n")
        a = player.index(b"\t\tif( !(pobj->adata[0].animode & MODE_PUPPET ))", proc_start, proc_end)
        b = player.index(b"\n\t\t#ifdef DEBUG", a, proc_end)
        generated.append(b"\nstatic void physics(fbplyr_data *ppdata) {obj_3d *pobj=&ppdata->odata;int needed_oob_correction;float mxz,frx,frz;\n")
        generated.append(self.select("player.c", a, b, "player_proc:complete movement/gravity/friction/bounds iteration"))
        generated.append(b"\n}\n")
        wpn_start, wpn_end = function_span(player, "_mode_wpn")
        a = player.index(b"\t\t// allow player to hurdle outta head plow", wpn_start, wpn_end)
        a, b = block_span(player, b"\t\tif (!(pobj->flags & PF_DRAGGING_PLR))", a)
        generated.append(b"\nstatic void weapon_before(fbplyr_data *ppdata) {obj_3d *pobj=&ppdata->odata;\n")
        generated.append(self.select("player.c", a, b, "_mode_wpn:head-plow hurdle escape"))
        generated.append(b"\n}\n")
        a = player.index(b"\t\t// double-turbo = spin", b, wpn_end)
        b = player.index(b"\n\t}\t// if !UNINT", a, wpn_end)
        generated.append(b"\nstatic int weapon_after(fbplyr_data *ppdata) {obj_3d *pobj=&ppdata->odata;int face_dir;\n")
        generated.append(self.select("player.c", a, b, "_mode_wpn:entire post-run spin/jump/push/drone control block"))
        generated.append(b"\nreturn 0;}\n")
        qb_start, qb_end = function_span(player, "_mode_qb")
        a = player.index(b"\t\t// Has double turbo been pressed", qb_start, qb_end)
        b = player.index(b"\t\t// dec cntr", a, qb_end)
        generated.append(b"\nstatic void quarterback_after(fbplyr_data *ppdata) {obj_3d *pobj=&ppdata->odata;int face_dir=dir49_table[ppdata->stick_cur];if(face_dir<0)face_dir=0;face_dir=(face_dir+camera_heading)&1023;\n")
        generated.append(self.select("player.c", a, b, "_mode_qb:spin + hurdle selection"))
        generated.append(b"\n}\n")
        db_start, db_end = function_span(player, "_mode_db")
        a, b = block_span(player, b"\t\tif ((ppdata->but_new & P_A)", db_start)
        generated.append(b"\nstatic void defensive_jump(fbplyr_data *ppdata) {obj_3d *pobj=&ppdata->odata;int face_dir;\n")
        generated.append(self.select("player.c", a, b, "_mode_db:complete A-button jump/dive/late-hit selection"))
        generated.append(b"\n}\n")
        # Exact source predicates, without calling contact/catch/snap/gameflow bodies.
        predicates = []
        for mode, start, marker in (
            ("MODE_QB", qb_start, b"\tif (!(pobj->adata[0].animode & MODE_UNINT) &&"),
            ("MODE_WPN", wpn_start, b"\tif ((!(pobj->adata[0].animode & MODE_UNINT)"),
            ("MODE_DB", db_start, b"\tif (!(pobj->adata[0].animode & MODE_UNINT))"),
        ):
            at = player.index(marker, start)
            end = player.index(b"{", at)
            predicates.append(b"\nif(pobj->plyrmode==" + mode.encode() + b"){\n" +
                              self.select("player.c", at, end, "mode run eligibility " + mode) + b"return 1;return 0;}\n")
        generated.append(b"\nstatic int mode_allows(fbplyr_data *ppdata){obj_3d *pobj=&ppdata->odata;\n" +
                         b"".join(predicates) + b"return !(pobj->adata[0].animode & MODE_UNINT);}\n")
        a = player.index(b"\t\thuman_control = ISHUMAN(ppdata);", proc_start, proc_end)
        b = player.index(b"\n\t\tif (human_control)", a, proc_end)
        generated.append(b"\nstatic int human_controls(fbplyr_data *ppdata){int human_control,fake_drone;\n")
        generated.append(self.select("player.c", a, b, "player_proc:human/drone/fake-drone control eligibility"))
        generated.append(b"\n(void)fake_drone;return human_control;}\n")
        info = self.sources["plyrinfo.c"]
        a, b = block_span(info, b"\t\t\t\tif ((game_info.plyr_control[i] >= 0)")
        generated.append(b"\nstatic void station_resource(int i){pdata_t *ppb=&pdata_blocks[i];if(!(p_status&(1<<i)))return;\n")
        generated.append(self.select("plyrinfo.c", a, b, "plyrinfo_proc:entire station turbo economy block"))
        generated.append(b"\n}\n")
        constant_names = []
        for match in re.finditer(rb"(?m)^#define\s+(\w*SPEED)\s+", self.sources["include/player.h"]):
            constant_names.append(match.group(1).decode())
        generated.append(b"\nstatic void dump_constants(void){int first=1;printf(\"{\\\"Constants\\\":{\");\n")
        for name in constant_names:
            generated.append(('if(!first)printf(",");first=0;printf("\\\"%s\\\":%%u",bits(%s));\n' % (name, name)).encode())
        generated.append(b'printf("},\\"Responses\\":{");first=1;\n')
        generated.append(b"""for(size_t i=0;i<sizeof(responses)/sizeof(responses[0]);i++){
script_response *r=&responses[i];if(!first)printf(",");first=0;
printf("\\\"%s\\\":{\\\"Mode\\\":%d,\\\"Sequence\\\":\\\"%s\\\",\\\"Zero\\\":%d,\\\"Velocity\\\":[%u,%u,%u],\\\"HasVelocity\\\":%d}",
r->name,r->mode,sequence_names[r->seq],r->zero,bits(r->vx),bits(r->vy),bits(r->vz),r->has_velocity);}
printf("},\\\"Friction\\\":[");
for(int i=0;i<7;i++)printf("%s%u",i?",":"",bits(field_friction_mult_factor[i]));
printf("],\\\"Defense\\\":[");
for(int h=0;h<12;h++){if(h)printf(",");printf("{\\\"Line\\\":[");
for(int i=0;i<6;i++)printf("%s%u",i?",":"",bits(dline_speed_ht[h][i]));
printf("],\\\"Back\\\":[");for(int i=0;i<6;i++)printf("%s%u",i?",":"",bits(dback_speed_ht[h][i]));
printf("],\\\"Base\\\":%d,\\\"Range\\\":%d,\\\"Plow\\\":%d}",def_acceltime_ht[h][0],def_acceltime_ht[h][1],plow_ht[h]);}
printf("],\\\"Angles49\\\":[");
for(int i=0;i<49;i++)printf("%s%d",i?",":"",dir49_table[i]);
printf("],\\\"Trigs\\\":[");
for(int i=0;i<257;i++)printf("%s%u",i?",":"",bits(trigs[i]));
printf("]}\\n");}\n""")
        generated.append((SUITE / "driver.c").read_bytes())
        self.unit = self.artifacts / "original-movement.c"
        self.unit.write_bytes(b"".join(generated))
        self.environment = dict(os.environ)
        work = self.artifacts / "work"
        work.mkdir(exist_ok=True)
        self.environment.update(TMPDIR=str(work), TMP=str(work), TEMP=str(work))
        compiler = shutil.which("gcc") or shutil.which("clang")
        if not compiler:
            raise RuntimeError("No C compiler available.")
        self.executable = self.artifacts / "original-movement"
        flags = ["-std=c11", "-O0", "-fno-fast-math", "-ffp-contract=off", "-fexcess-precision=standard",
                 "-fno-strict-aliasing", "-no-pie", "-Wall", "-Wextra",
                 "-Wno-pointer-to-int-cast", "-Wno-int-to-pointer-cast", "-Wno-unused-variable",
                 "-Wno-unused-function"]
        command = [compiler, *flags, str(self.unit), "-lm", "-o", str(self.executable)]
        result = subprocess.run(command, cwd=ROOT, env=self.environment, capture_output=True, text=True)
        (self.artifacts / "compiler.log").write_text(result.stdout + result.stderr)
        if result.returncode:
            raise RuntimeError("Original movement C compilation failed:\n" + result.stdout + result.stderr)
        self.compiler = {"command": command, "flags": flags, "diagnostics": result.stderr,
                         "unit_sha256": hashlib.sha256(self.unit.read_bytes()).hexdigest(),
                         "binary_sha256": hashlib.sha256(self.executable.read_bytes()).hexdigest()}
        # The same original arrays are the explicit production input, never a test-side speed formula.
        self.tables = tables
        self.responses = responses

    def execute(self, lines):
        result = subprocess.run([str(self.executable)], input="\n".join(lines) + "\n", env=self.environment,
                                cwd=ROOT, capture_output=True, text=True, timeout=120)
        if result.returncode:
            raise RuntimeError("Original movement C failed:\n" + result.stderr)
        rows = [json.loads(line) for line in result.stdout.splitlines()]
        if len(rows) != len(lines):
            raise RuntimeError("Original C result count mismatch.")
        return rows
