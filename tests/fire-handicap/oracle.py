"""Compile selected unchanged active original rules; source/data/output remain ignored."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess

PINS = {
    "player.c": "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c",
    "nfl.c": "15b49474fb55e8bac280b6515fcf7bf59cdf8664",
    "result.c": "eaf1201d0531a9d06d8ce0b1e9e75463062e2923",
    "handicap.c": "871424596fb9786af3602a49cc9d20c466df8ca1",
    "teamdata.c": "e5340046b33e1d2addc702feb7a94d1a0f69afdb",
    "include/GAME.H": "09d65c92971c32c44b1dc49fb4e4e98b371b1cd3",
    "include/player.h": "a86b1a5b52ba842b206cd7dd0bbb26521db6dcb8",
    "include/ani3d.h": "826c6322467fcfe503b8402b540e4731671f9b6b",
}
ROOT = Path(__file__).resolve().parents[2]
SUITE = Path(__file__).resolve().parent


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def closing(raw, start):
    depth = 0
    for match in re.finditer(rb'/\*.*?\*/|//[^\r\n]*|"(?:\\.|[^"\\])*"|[{}]', raw[start:], re.S):
        if match[0] == b"{":
            depth += 1
        elif match[0] == b"}":
            depth -= 1
            if depth == 0:
                return start + match.end()
    raise ValueError("Unclosed original C block")


def build(original, output, handicap_header):
    original, output = Path(original).absolute(), Path(output).absolute()
    if not output.is_relative_to(ROOT / ".squad/.scratch"):
        raise ValueError("Generated original reference must stay in this worktree's ignored scratch.")
    output.mkdir(parents=True, exist_ok=False)
    sources, hashes, windows = {}, {}, []
    for name, pin in PINS.items():
        path = original / name
        if any(p.is_symlink() for p in (path, *path.parents)):
            raise ValueError("Original source symlink is forbidden: " + name)
        raw = path.read_bytes()
        if hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest() != pin:
            raise ValueError("Pinned original source differs: " + name)
        sources[name], hashes[name] = raw, sha(raw)
    header = Path(handicap_header).absolute()
    if any(p.is_symlink() for p in (header, *header.parents)):
        raise ValueError("Original handicap header symlink is forbidden.")
    raw = header.read_bytes()
    if hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest() != "45eef32b32721196e4575e59a0fd2ce01e0fbbdb":
        raise ValueError("Pinned original include/handicap.h differs.")
    sources["include/handicap.h"], hashes["include/handicap.h"] = raw, sha(raw)

    def select(name, start, end, identity):
        raw = sources[name][start:end]
        if not raw:
            raise ValueError("Empty original window: " + identity)
        windows.append(dict(path=name, identity=identity, first_line=sources[name][:start].count(b"\n") + 1,
                            start_byte=start, end_byte_exclusive=end, sha256=sha(raw), unchanged_bytes=True))
        return raw.decode("latin1")

    def function(name, symbol):
        raw = sources[name]
        matches = list(re.finditer(rb"(?m)^(?:static\s+)?(?:void|int)\s+" + symbol.encode()
                                   + rb"\s*\([^;{}]*\)\s*\{", raw))
        if len(matches) != 1:
            raise ValueError("Expected one original function: " + symbol)
        return select(name, matches[0].start(), raw.index(b"\n}", matches[0].end()) + 2, symbol)

    constants = []
    prefixes = ("PT_", "PR_", "PEC_", "GF_", "GM_", "LB_", "BF_", "TEAM")
    names = {"N_TYPES", "N_CAUSES", "CONTROL_TIME", "DEFENSE_FIRECNT", "OFFENSE_FIRECNT", "FIELDHB",
             "FIELDHZ", "FIELDW", "FOCFAC", "FDYARDS", "WORLDX", "FIELDX", "LESSER", "GREATER",
             "PF_PEND_FCHECK", "MODE_REACTING", "MODE_QB", "YD2UN", "OFFH"}
    for name in ("include/GAME.H", "include/player.h", "include/ani3d.h", "include/handicap.h"):
        for match in re.finditer(rb"(?m)^#define[ \t]+(\w+)[^\r\n]*(?:\r?\n|$)", sources[name]):
            symbol = match[1].decode()
            if symbol in names or symbol.startswith(prefixes):
                constants.append(select(name, match.start(), match.end(), "macro/" + symbol))
    # TRUE/FALSE are the platform Boolean seams, not gameplay datasets.
    constants.append("#define TRUE 1\n#define FALSE 0\n")
    player, nfl = sources["player.c"], sources["nfl.c"]
    begin = player.index(b"\tif (ppdata->plyrnum == game_info.ball_carrier)",
                         player.index(b"// ball carrier checks (NOT control logic)"))
    end = player.index(b"\t\t// have I scored a touchdown?", begin)
    carrier = ("static void original_carrier(fbplyr_data *ppdata) {\nobj_3d *pobj=&ppdata->odata;\n"
               + select("player.c", begin, end, "_mode_wpn/carrier-control-fire-and-landing") + "\n}\n}\n")
    down = re.search(rb"(?m)^void player_down\([^;{}]*\)\s*\{", player).start()
    begin = player.index(b"\tif( game_info.last_pos != game_info.off_side )", down)
    forced = ("static void original_forced_control(fbplyr_data *ppdata) {\n"
              + select("player.c", begin, closing(player, player.index(b"{", begin)),
                       "player_down/forced-control-no-fire-reset") + "\n}\n")
    begin = nfl.index(b"\t\t// if last_pos != off_side")
    loss = nfl.index(b"#ifndef NO_FIRE", nfl.index(b"// play-end bookkeeping", begin))
    prep = select("nfl.c", begin, loss, "game_proc/control-restore-round-classify-clamp")
    end = nfl.index(b"\t\t// inc stats.", loss)
    loss_window = select("nfl.c", loss, end, "game_proc/loss-yard-fire")
    begin = nfl.index(b"\t\t// next play defaults to PT_NORMAL", end)
    switch = nfl.index(b"switch( game_info.play_result )", begin)
    end = closing(nfl, nfl.index(b"{", switch))
    result_switch = select("nfl.c", begin, end, "game_proc/original-result-switch")
    result = ("static void original_result(int cause) {\nint i,dont_update_los;\ngame_info.play_end_cause=cause;\n"
              + prep + loss_window + result_switch + "\n}\n")
    begin = nfl.index(b"\t\t// clear completion counter if no completed forward pass")
    start_if = nfl.index(b"\t\tif ", begin)
    end = closing(nfl, nfl.index(b"{", start_if))
    dismissed = ("static void original_result_dismissed(void) {\n"
                 + select("nfl.c", begin, end, "game_proc/result-dismissed-forward-catcher") + "\n}\n")
    begin = sources["result.c"].index(b"static void _norm_car_oob( void );")
    classifiers = select("result.c", begin, len(sources["result.c"]), "active-original-classifiers")
    data = sources["teamdata.c"]
    start = data.index(b"const struct fbteam_data teaminfo[]")
    end = closing(data, data.index(b"{", start))
    active = re.sub(rb"/\*.*?\*/|//[^\r\n]*", b"", data[start:end], flags=re.S)
    bases = re.findall(rb'\{\s*\{[^{}]*\}\s*,\s*(\d+)\s*,\s*"', active)
    if len(bases) != 31:
        raise ValueError("Expected all31 active original teaminfo drone_base fields.")
    windows.append(dict(path="teamdata.c", identity="teaminfo.drone_base-only-projection",
                        first_line=data[:start].count(b"\n") + 1, start_byte=start, end_byte_exclusive=end,
                        sha256=sha(data[start:end]), unchanged_bytes=False, selected_rows=len(bases),
                        scope="Independently projected only the integer field consumed by unchanged set_handicaps."))
    team_data = "static const struct fbteam_data teaminfo[]={" + ",".join(
        "{" + value.decode() + "}" for value in bases) + "};\n"
    start = data.index(b"struct diff_set\tdiff_settings[9]")
    end = data.index(b";", closing(data, data.index(b"{", start))) + 1
    tuning = select("teamdata.c", start, end, "diff_settings/original-nine-rows")
    handicap = "int random_drones,human_winstreak,user_base_adjust,drones_lose,drones_winning;\nplay_t *play_hist[5];\n"
    handicap += function("handicap.c", "set_handicaps")
    start = nfl.index(b"\t\t\t\tobj_3d * pobj =", nfl.index(b"// misc initialization"))
    end = nfl.index(b"game_info.ball_carrier = -1;", start) + len(b"game_info.ball_carrier = -1;")
    quick = ("static void original_quick_setup(void) {\n"
             + select("nfl.c", start, end, "game_proc/quick-kick-original-handicap-and-draw-order") + "\n}\n")
    tables = []
    for symbol in ("min_ko_catch_ht", "max_ko_catch_ht"):
        start = sources["handicap.c"].index(("int " + symbol + "[12]").encode())
        end = sources["handicap.c"].index(b";", start) + 1
        tables.append(select("handicap.c", start, end, symbol))
    funcs = [function("player.c", "reset_team_fire"), function("player.c", "reset_onfire_values"),
             function("player.c", "check_receiver_fire"), function("player.c", "in_bounds"),
             classifiers, carrier, forced, result, dismissed, handicap, quick]
    stats = sorted(set(re.findall(r"tdata_blocks\[[^\n;]+?\]\.(\w+)", "\n".join(funcs))))
    generated = (SUITE / "driver.c").read_text()
    generated = generated.replace("/* ORIGINAL_CONSTANTS */", "\n".join(constants))
    generated = generated.replace("/* ORIGINAL_STATS_FIELDS */",
                                  "static struct {" + "".join("int " + s + ";" for s in stats) + "} tdata_blocks[2];")
    generated = generated.replace("/* ORIGINAL_DATA */", team_data + tuning + "\n".join(tables))
    generated = generated.replace("/* ORIGINAL_FUNCTIONS */", "\n".join(funcs))
    path, binary = output / "original-fire-handicap.c", output / "original-fire-handicap"
    path.write_bytes(generated.encode("latin1"))
    work = output / "work"
    work.mkdir()
    command = ["gcc", "-std=c99", "-O0", "-fno-fast-math", "-ffp-contract=off",
               "-fexcess-precision=standard", "-Werror=implicit-function-declaration", str(path), "-lm", "-o", str(binary)]
    process = subprocess.run(command, capture_output=True, text=True, cwd=ROOT, timeout=120,
                             env=dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work)))
    (output / "compiler.log").write_text(process.stdout + process.stderr)
    receipt = dict(schema="original-fire-handicap-reference/v1", source_revision="89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
                   source_sha256=hashes, windows=windows, command=command, exit_code=process.returncode,
                   generated_sha256=sha(path.read_bytes()), source_hz=57, NO_FIRE=False, USE_OLD_QB=False,
                   GETUPAGAIN=1, DOSETCAM=1,
                   omitted="Sound/render/qcreate processes and unrelated first-down sound predicate; no target ABI/RNG emulation.")
    if process.returncode == 0:
        receipt["binary_sha256"] = sha(binary.read_bytes())
    (output / "provenance.json").write_text(json.dumps(receipt, indent=2) + "\n")
    if process.returncode != 0:
        raise RuntimeError("Original C reference did not compile:\n" + process.stdout + process.stderr)
    return binary, receipt
