"""Reuse the frozen P1 extraction; no original tables/functions are stored here."""
import json
from pathlib import Path
import re
import subprocess

from support import SUITE, PINS, GateError, blob_sha1, sha256, verify_inputs


DRONE_FUNCTIONS = (
    "init_drones_prelineup", "init_drones_postlineup", "_drone_lineup", "_drone_dead", "_drone_lineps",
    "_drone_line", "_drone_qbps", "_drone_qb", "_drone_wpnps", "_drone_wpn",
    "_drone_carrier", "_drone_dbps", "_drone_db", "_drone_kicker",
    "get_drone_switches", "dir2stick", "carrier_dir", "carrier_dist",
    "assign_blockers", "icmp", "move_to_block", "move_to_intercept",
    "intercept_time", "pdistxz", "sell_fake", "nearest_defender",
    "update_receiver_data", "mtm_unhappiness", "dline_unhappiness",
    "qb_unhappiness", "dbps_unhappiness", "carrier_unhappiness",
    "choose_wr_imp_point", "find_receiver", "clean_shot", "find_diver",
    "find_attacker", "trips_lineup", "choose_roll_dir", "evade_dir",
    "likely_gain", "int_time", "do_zone", "db_choose_defense", "drone_cover_assign",
)
FUNCTIONS = (
    [("lib/ani3d/pipe3D.c", name) for name in ("isin", "icos", "iasin", "iacos")]
    + [("lib/ani3d/ANIM.C", "ptangle")]
    + [("drone.c", name) for name in DRONE_FUNCTIONS]
    + [("nfl.c", name) for name in ("match_receiver", "strong_left", "lineup_players")]
)
SPECIAL_SCRIPTS = (
    "c_jmpint_anim", "juke_c_anim", "spin_anim", "r_histep_anim",
    "m_wavqb_anim", "m_wavqbf_anim", "c_no_vel1_anim", "c_no_vel2_anim",
)
LITERAL_OR_COMMENT = re.compile(r'"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*.*?\*/', re.S)


def strip_comments(text):
    return LITERAL_OR_COMMENT.sub(
        lambda match: re.sub(r"[^\n]", " ", match[0]) if match[0].startswith(("//", "/*")) else match[0],
        text)


def one(pattern, text, identity, flags=re.S):
    matches = list(re.finditer(pattern, text, flags))
    if len(matches) != 1:
        raise GateError("Expected exactly one unchanged original declaration/body: " + identity)
    return matches[0]


def body(text, name):
    match = one(r"\b(?:static\s+)?(?:float|int|void)\s+" + re.escape(name) + r"\s*\([^;{}]*\)\s*\{",
                text, name)
    masked = strip_comments(text)
    begin = text.index("{", match.start())
    depth, at = 1, begin + 1
    while depth and at < len(text):
        depth += (masked[at] == "{") - (masked[at] == "}")
        at += 1
    if depth:
        raise GateError("Unterminated original function: " + name)
    return text[match.start():at]


def preprocess(data, environment):
    text = data.decode("latin1").replace("\r\n", "\n")
    text = re.sub(r"^\s*#\s*include[^\n]*", "", text, flags=re.M)
    command = ["gcc", "-E", "-P", "-x", "c", "-"]
    process = subprocess.run(command, input=text, text=True, capture_output=True,
                             env=environment, check=True, timeout=60)
    return process.stdout


def inventory(documents, recipe):
    """Discover symbol names/array lengths, never compute expected C values."""
    playbook = strip_comments(documents["playbook.c"].decode("latin1"))
    formations = {}
    for match in re.finditer(r"\bfline_t\s+(\w+)\[\]\s*=\s*\{(.*?)\};", playbook, re.S):
        rows = [row.split(",") for row in re.findall(r"\{([^{}]+)\}", match[2])]
        if len(rows) != 7 or any(len(row) != 5 for row in rows):
            raise GateError("Unsupported original formation inventory: " + match[1])
        formations[match[1]] = [row[2].strip() for row in rows]
    routes = {match[1]: len([word for word in match[2].split(",") if word.strip()])
              for match in re.finditer(r"\bint\s+(\w+)\[\]\s*=\s*\{([^{}]*)\};", playbook, re.S)}
    plays = [match[1] for match in re.finditer(r"\bplay_t\s+(\w+)\s*=\s*\{", playbook)]
    handicap = strip_comments(documents["handicap.c"].decode("latin1"))
    handicap_fields = [match[1] for match in re.finditer(r"\b(?:int|float)\s+(\w+_ht)\[12\]\s*=", handicap)]
    if (len(formations) != 29 or len(routes) != 140 or len(plays) != 88 or len(set(plays)) != 88
            or len(handicap_fields) != 34 or len(set(handicap_fields)) != 34
            or len(recipe["weighted_pools"]) != 7 or len(recipe["singleton_pools"]) != 6):
        raise GateError("Pinned original C symbol inventory is incomplete/unsupported.")
    scripts = sorted({script for rows in formations.values() for script in rows})
    if set(scripts) & set(SPECIAL_SCRIPTS) or any(re.fullmatch(r"[A-Za-z_]\w*", script) is None for script in scripts):
        raise GateError("Unsupported original formation script identity.")
    return {"formations": formations, "routes": routes, "plays": plays, "handicap_fields": handicap_fields, "scripts": scripts}


def make_maps(metadata, recipe):
    maps = ['static play_t *get_play(const char *name) {']
    for name in metadata["plays"]:
        maps.append(f'if(!strcmp(name,"{name}")) return &{name};')
    maps.append('fprintf(stderr,"unknown play %s\\n",name);abort();}')
    maps.append('static int *get_route(const char *name) {')
    for name in metadata["routes"]:
        maps.append(f'if(!strcmp(name,"{name}")) return {name};')
    maps.append('fprintf(stderr,"unknown route %s\\n",name);abort();}')
    maps.append('static const char *route_id(int *p,int *offset) { uintptr_t q=(uintptr_t)p;')
    for name, length in metadata["routes"].items():
        maps.append(f'if(q>=(uintptr_t){name} && q<(uintptr_t)({name}+{length})){{*offset=(int)(p-{name});return "{name}";}}')
    maps.append('*offset=-1;return "invalid";}')
    maps.append('static const char *script_id(char *p) { if(!p) return "-";')
    for name in SPECIAL_SCRIPTS:
        maps.append(f'if(p==&{name})return "{name}";')
    maps.append('return "unknown";}')
    for ctype, function, names, expression in (
            ("play_t", "play_id", metadata["plays"], "&{name}"),
            ("char", "formation_script_id", metadata["scripts"], "{name}"),
            ("fline_t", "formation_id", metadata["formations"], "{name}")):
        maps.append(f'static const char *{function}({ctype} *p) {{')
        for name in names:
            maps.append('if(p==' + expression.format(name=name) + ')return "' + name + '";')
        maps.append('return "unknown";}')
    maps.append("static void emit_catalog(void) {")
    for name in metadata["formations"]:
        maps.append(f'for(int i=0;i<7;i++)printf("F {name} %d %.9g %.9g %d %d %s\\n",i,{name}[i].x,{name}[i].z,{name}[i].mode,{name}[i].control,formation_script_id({name}[i].seq));')
    for name in metadata["plays"]:
        maps.append(f'printf("D {name} %s %d",formation_id({name}.formation),{name}.flags);')
        maps.append(f'for(int i=0;i<7;i++){{int offset;printf(" %s",route_id({name}.routes[i],&offset));}}puts("");')
    for name in metadata["routes"]:
        maps.append(f'for(unsigned i=0;i<sizeof({name})/sizeof(int);i++)printf("R {name} %u %d\\n",i,{name}[i]);')
    for family, item in recipe["weighted_pools"].items():
        name = item["symbol"]
        maps.append(f'for(unsigned i=0;i<sizeof({name})/sizeof(play_t*);i++)printf("P {family} %lu %u %s\\n",sizeof({name})/sizeof(play_t*),i,play_id({name}[i]));')
    for family, name in recipe["singleton_pools"].items():
        maps.append(f'printf("P {family} 1 0 %s\\n",play_id(&{name}));')
    for name in metadata["handicap_fields"]:
        maps.append(f'for(int i=0;i<12;i++)printf("H {name} %d %.9g\\n",i,(double){name}[i]);')
    maps += ['for(int i=0;i<257;i++)printf("T %d %.9g\\n",i,trigs[i]);',
             'for(int i=0;i<49;i++)printf("V %d %d\\n",i,dir49_table[i]);',
             'puts("END");fflush(stdout);}']
    return "\n".join(maps)


def build(source_root, output, recipe, environment):
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    documents = verify_inputs(source_root)
    metadata = inventory(documents, recipe)
    preprocessed = {name: preprocess(documents[name], environment) for name in {name for name, _ in FUNCTIONS}}
    bodies, selections = [], []
    for name, symbol in FUNCTIONS:
        selected = body(preprocessed[name], symbol)
        bodies.append(selected)
        selections.append({
            "path": name, "symbol": symbol, "git_blob_sha1": blob_sha1(documents[name]),
            "preprocessed_function_sha256": sha256(selected.encode("utf-8")),
            "preprocessed_start_line": preprocessed[name][:preprocessed[name].index(selected)].count("\n") + 1,
            "preprocessed_end_line": preprocessed[name][:preprocessed[name].index(selected) + len(selected)].count("\n") + 1,
            "expression_rewrites": 0,
        })
    drone = preprocessed["drone.c"]
    drone_type = one(r"typedef struct _drone_t\s*\{.*?\}\s*drone_t;", drone, "drone_t")[0]
    panic = one(r"static\s+float\s+qb_panic_dist\[12\]\s*=\s*\{[^{}]*\};", drone, "qb_panic_dist")[0]
    dispatch = one(r"static\s+void\s*\(\*drone_functions\[\]\)\s*\(fbplyr_data\s*\*\)\s*=\s*\{[^{}]*\};",
                   drone, "drone_functions")[0]
    macros = []
    for name in ("include/player.h", "include/PlayStuf.h", "include/ani3d.h"):
        text = strip_comments(documents[name].decode("latin1")).replace("\r\n", "\n")
        for line in text.splitlines():
            if re.match(r"\s*#define\s+(?:PF_|DF_|MODE_|DS_|DZ_|LU_|PLF_|JOB_|PPOS_)\w+\s+", line):
                macros.append(line)
    for line in strip_comments(documents["include/player.h"].decode("latin1")).splitlines():
        if re.match(r"\s*#define\s+(?:QB_|WPN|DB|LINE|HUMAN_DEF|CARRIER)\w*SPEED\s+", line):
            macros.append(line)
    handicap = strip_comments(documents["handicap.c"].decode("latin1"))
    arrays = "\n".join(match[0] for match in re.finditer(r"\b(?:int|float)\s+\w+_ht\[12\]\s*=\s*\{[^{}]*\};", handicap, re.S))
    player = strip_comments(documents["player.c"].decode("latin1"))
    directions = one(r"\bint dir49_table\[\]\s*=\s*\{.*?\};", player, "dir49_table")[0]
    selection = strip_comments(documents["playsel.c"].decode("latin1"))
    pools = "\n".join(match[0] for match in re.finditer(r"static\s+play_t\s*\*\w+\[\]\s*=\s*\{[^{}]*\};", selection, re.S))
    for family, symbol in recipe["singleton_pools"].items():
        if re.search(r"game_info\.team_play\[pnum\]\s*=\s*&" + re.escape(symbol) + r"\b", selection) is None:
            raise GateError("Missing original direct CPU singleton: " + family)
    playbook = strip_comments(documents["playbook.c"].decode("latin1"))
    playbook = re.sub(r"^\s*#\s*include[^\n]*", "", playbook, flags=re.M)
    tables = "\n".join("char " + name + "[1];" for name in metadata["scripts"])
    tables += "\n" + "\n".join("char " + name + ";" for name in SPECIAL_SCRIPTS)
    tables += "\n" + documents["lib/ani3d/TRIGS.H"].decode("latin1") + "\n" + directions + "\n" + arrays + "\n" + panic
    tables += "\n" + playbook + "\n" + pools
    prototypes = "\n".join(function[:function.index("{")].strip() + ";" for function in bodies)
    source = (SUITE / "boundaries.c").read_text(encoding="utf-8") + "\n" + "\n".join(macros) + "\n" + drone_type
    source += "\ndrone_t drone_blocks[14],*pdrone;\n" + prototypes + "\nint bearing(int,int);\n" + tables + "\n" + dispatch
    source += "\n" + "\n".join(bodies) + "\n" + make_maps(metadata, recipe) + "\n" + (SUITE / "driver.c").read_text(encoding="utf-8")
    path = output / "original-unit.c"
    path.write_text(source, encoding="utf-8")
    executable = output / "source-oracle"
    command = ["gcc", "-std=c99", "-O0", "-ffp-contract=off", "-fno-fast-math",
               "-Wno-incompatible-pointer-types", str(path), "-lm", "-o", str(executable)]
    process = subprocess.run(command, env=environment, capture_output=True, text=True, timeout=120)
    (output / "compiler.log").write_text(process.stdout + process.stderr, encoding="utf-8")
    if process.returncode:
        raise GateError("Original C compilation failed, not a behavioral rejection; see oracle/compiler.log.")
    compiler = subprocess.run(["gcc", "--version"], env=environment, capture_output=True, text=True, check=True, timeout=30)
    receipt = {
        "schema": "source-playbook-native-oracle/v1", "status": "passed", "source_revision": PINS["revision"],
        "source_tree": PINS["tree"], "source_blobs": PINS["blobs"], "profile": PINS["profile"],
        "selected_functions": selections, "selected_function_count": len(selections),
        "preprocess_command": ["gcc", "-E", "-P", "-x", "c", "-"],
        "preprocess_boundary": "Only include directives are removed. GCC selects original active branches/macros; selected function expressions are never rewritten.",
        "compile_command": command, "compiler": compiler.stdout.splitlines()[0],
        "translation_unit_sha256": sha256(path.read_bytes()), "artifact_sha256": sha256(executable.read_bytes()),
        "shim_boundary": [
            "Projected host structs, not target arcade ABI; host binary32 sqrtf and already-converted integer camera heading.",
            "External logical actor buttons/stations/modes/jobs/flags and synthetic actor/animation input frames.",
            "randrng consumes only explicitly supplied raw integer input tapes; no host/target PRNG is executed.",
            "Host qsort: equal-key target-library ordering is explicitly excluded from differential assertions.",
            "change_anim records script identity only; no original animation interpreter or locomotion.",
            "Undefined array[-1] source branches and audible side initialization are not certified.",
        ],
        "full_gameplay_parity_claimed": False,
    }
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    return receipt
