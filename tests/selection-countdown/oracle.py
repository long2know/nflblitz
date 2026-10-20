"""Execute unchanged pinned timer/flasher and selected caller bodies; no numeric goldens."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "tools/rosters"))
from c_source import CData, closing_brace

PINS = {
    "nfl/playsel.c": "f5e87b921a4038d03f55174bbd5425241939e798",
    "nfl/lib/select/select.c": "26c3955a4081aff73d6e4f1f7633ae86f24e2c4a",
    "nfl/Game.c": "a005a9b44f032080f3dd6341dd4f5eb6104d9cd7",
    "nfl/include/select.h": "458b10e92f9b76a8e38c003246359e0745d8f83e",
    "nfl/include/id.h": "d2cc50865dd105c4143ac6b769765bd859cc4ff4",
    "nfl/include/sndcalls.h": "69f6c483502fb689eb1991169e0e31858378c763",
    "nfl/include/PlayStuf.h": "3df5e8e8a3f478c3b02860a401c950d1e83c5ad3",
    "nfl/include/GAME.H": "09d65c92971c32c44b1dc49fb4e4e98b371b1cd3",
    "nfl/nfl.c": "15b49474fb55e8bac280b6515fcf7bf59cdf8664",
    "nfl/sysfont.c": "59a34a5c5d4ad1075d80b3856284f814e4bd1e31",
    "midway/include/goose/colors.h": "d57aca63af71f4917e133f6d3ffb3a2e4cc0e12d",
    "midway/lib/goose/process.c": "967ca53910f24ddfa48a14485113a8fd0c989959",
    "midway/lib/goose/mthread.c": "7b44637c3d59e2256ad099226b7ea17f108907ef",
}


def require(value, message):
    if not value:
        raise ValueError(message)


def verified_authority(root):
    root = Path(root).absolute()
    require(root.resolve() == root and root.is_dir(), "Regular explicit original source authority is required.")
    raw = {}
    for name, pin in PINS.items():
        path = root / name
        require(path.is_file() and not path.is_symlink() and path.resolve() == path,
                "Missing/escaping timer authority: " + name)
        data = path.read_bytes()
        require(hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest() == pin,
                "Wrong authenticated timer Git blob: " + name)
        raw[name] = data
    return raw


def cases():
    def case(name, initial=9, ticks=605, **kw):
        return dict(name=name, initial=initial, ticks=ticks, holdStart=0, holdEnd=0,
                    holdValue=0, suspendAt=0, resumeAt=0, suspendBoth=0, stopAt=0, **kw)
    rows = [case("quarter0-full"), case("later-quarter-full", initial=5, ticks=377)]
    for value in (8, 9, 10):
        row = case("instruction-threshold-" + str(value))
        row.update(holdStart=1, holdEnd=41, holdValue=value, ticks=650)
        rows.append(row)
    row = case("instruction-only-before-number-sleep")
    row.update(holdStart=20, holdEnd=101, holdValue=12, ticks=690)
    rows.append(row)
    row = case("instruction-at-zero")
    row.update(holdStart=510, holdEnd=551, holdValue=9, ticks=655)
    rows.append(row)
    for both in (0, 1):
        row = case("real-suspended-" + ("timer-and-child" if both else "timer-only"))
        row.update(suspendAt=11, resumeAt=51, suspendBoth=both, ticks=650)
        rows.append(row)
    for stop in (1, 2, 20, 58, 515, 595, 596, 597):
        row = case("caller-teardown-at-" + str(stop))
        row.update(stopAt=stop)
        rows.append(row)
    return rows


def harness(raw):
    env = {}
    for name in ("nfl/include/select.h", "nfl/include/id.h", "nfl/include/sndcalls.h",
                 "nfl/include/PlayStuf.h", "nfl/include/GAME.H", "midway/include/goose/colors.h"):
        env = CData(raw[name].decode("latin1"), env).enums().expressions.macros
    defines = ""
    for name, macro in env.items():
        if name in ("NULL", "TRUE", "FALSE"):
            continue
        parameters = "" if macro.parameters is None else "(" + ",".join(macro.parameters) + ")"
        defines += "#define " + name + parameters + " " + macro.text + "\n"
    select = CData(raw["nfl/lib/select/select.c"].decode("latin1"), env)
    game = CData(raw["nfl/Game.c"].decode("latin1"), env)
    playsel = CData(raw["nfl/playsel.c"].decode("latin1"), env)
    process = CData(raw["midway/lib/goose/process.c"].decode("latin1"), env)
    require(re.search(r"\{\s*&bast18_font,\s*FONTID_BAST18\s*\}", game.text),
            "Actual FONTID_BAST18 registry binding changed.")
    require('strcpy(cursor_bnk_str, "cursors");' in raw["nfl/nfl.c"].decode("latin1"),
            "Actual cursor bank initializer changed.")
    require("tsec = (int)((1.0f /" in raw["midway/lib/goose/mthread.c"].decode("latin1"),
            "Actual runtime tsec derivation is required, never Game.c's initial100 as duration.")
    timer = "static void timedown_play_select(int *args)" + select.function("timedown_play_select")[1]
    flash = "static void flash_text_proc(int *args)" + game.function("flash_text_proc")[1]
    parent = playsel.function("play_select")[1]
    bypass_start = parent.index("if ((!(p_status & TEAM_MASK(0))")
    bypass_end = closing_brace(parent, parent.index("{", bypass_start))
    bypass = parent[bypass_start:bypass_end + 1]
    start = parent.index("if (game_info.game_quarter)")
    end = re.search(r'qcreate\("timer",TIMER_PID,timedown_play_select,9,[^;]+;', parent[start:])
    require(end is not None, "Actual quarter-dependent timer qcreate branch is required.")
    spawn = parent[start:start + end.end()]
    require(spawn.count('qcreate("timer"') == 2 and "timedown_play_select,5," in spawn,
            "Both original initial9/5 caller operands are required.")
    cleanup = re.search(r"killall\(\s*TIMER_PID,\s*-1\s*\);.*?clobber_playsel\(\);", parent, re.S)
    require(cleanup is not None, "Actual timer kill/strings/clobber teardown is required.")
    wait_start = re.search(r"\bdo\s*\{", parent[parent.index('qcreate("tm2plysl"'):])
    require(wait_start is not None, "Original caller wait loop is required.")
    wait_start = parent.index('qcreate("tm2plysl"') + wait_start.start()
    wait_end = re.search(r"if \(game_info\.team_play\[1\].*?show_freept_idiot_box = TRUE;", parent[wait_start:], re.S)
    require(wait_end is not None, "Original caller finish and ordered CPU completion are required.")
    wait_body = parent[wait_start:wait_start + wait_end.end()]
    controller = playsel.function("play_select_cntrl")[1]
    suffix = controller[controller.rindex("if(game_info.team_play[tm_num] == (play_t *)-1)"):].rstrip()
    require(suffix.endswith("}"), "Original controller suffix unavailable.")
    suffix = suffix[:-1]
    choose = "static void PlaySel__ChoosePlay(int side,int page,int p_num,int cur_pos,int tm_num,int force_choose)" + \
        playsel.function("PlaySel__ChoosePlay")[1]
    make = "static play_t *PlaySel__MakeChoice(int side,int page,int p_num,int cur_pos,int tm_num,int force_choose)" + \
        playsel.function("PlaySel__MakeChoice")[1]
    table = re.search(r"(?m)^static int cursor_snds\[\]\s*=\s*\{[^}]+\};", playsel.text)[0]
    kill_prefix = playsel.function("clobber_playsel")[1]
    kill_prefix = kill_prefix[kill_prefix.index("killall("):kill_prefix.index("del1c(")]
    proc = ""
    for name, parameters in (("suspend_process", "struct process_node *p"),
                             ("resume_process", "struct process_node *p"),
                             ("resume_all", "void"),
                             ("suspend_multiple_processes", "int id,int mask")):
        proc += "static void " + name + "(" + parameters + ")" + process.function(name)[1] + "\n"
    text = (HERE / "oracle_shim.c").read_text()
    return text.replace("/* ORIGINAL_DEFINES */", defines).replace("/* ORIGINAL_PROCESS */", proc) \
        .replace("/* ORIGINAL_TIMER */", timer).replace("/* ORIGINAL_FLASH */", flash) \
        .replace("/* ORIGINAL_SPAWN */", spawn).replace("/* ORIGINAL_CLEANUP */", cleanup[0]) \
        .replace("/* ORIGINAL_CLOBBER_PREFIX */", kill_prefix).replace("/* ORIGINAL_CHOICE */", table + "\n" + make + "\n" + choose) \
        .replace("/* ORIGINAL_FORCED_CONTROLLER_SUFFIX */", suffix).replace("/* ORIGINAL_NO_HUMAN_BYPASS */", bypass) \
        .replace("/* ORIGINAL_CALLER_WAIT */", wait_body)


def generate(authority, output, fixtures):
    raw = verified_authority(authority)
    output.mkdir(parents=True)
    text = harness(raw)
    source = output / "original-selection-timer.c"
    source.write_text(text)
    executable = output / "original-selection-timer"
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output))
    command = ["gcc", "-std=gnu11", "-O0", "-Wno-int-to-pointer-cast", "-Wno-pointer-to-int-cast",
               str(source), "-o", str(executable)]
    build = subprocess.run(command, env=env, text=True, capture_output=True, timeout=90)
    (output / "compile.log").write_text(build.stdout + build.stderr)
    require(build.returncode == 0, "Unchanged timer C failed to compile:\n" + build.stderr)
    result = {}
    for row in fixtures:
        values = [row[n] for n in ("initial", "ticks", "holdStart", "holdEnd", "holdValue",
                                  "suspendAt", "resumeAt", "suspendBoth", "stopAt")]
        run = subprocess.run([str(executable), *map(str, values)], env=env, capture_output=True, text=True, timeout=10)
        require(run.returncode == 0, "Original C timer execution failed: " + row["name"] + ":\n" + run.stderr)
        result[row["name"]] = json.loads(run.stdout)
        require(len(result[row["name"]]) == row["ticks"], "Original C omitted source dispatches.")
        (output / (row["name"] + ".json")).write_text(run.stdout)
    callers = subprocess.run([str(executable), "callers"], env=env, capture_output=True, text=True, timeout=10)
    require(callers.returncode == 0, "Original forced-choice callers failed: " + callers.stderr)
    caller_rows = json.loads(callers.stdout)
    require(len(caller_rows) == 11 and all(c["passed"] for c in caller_rows),
            "All11 unchanged C caller/wait/current-choice/custom/PAT/no-human operands must execute.")
    (output / "caller-cases.json").write_text(callers.stdout)
    receipt = dict(schema="original-selection-timer/v1",
        inputs={n: dict(gitBlobSha1=PINS[n], sha256=hashlib.sha256(b).hexdigest(), bytes=len(b)) for n, b in raw.items()},
        cases=len(fixtures), dispatches=sum(r["ticks"] for r in fixtures), callers=caller_rows,
        generatedSha256=hashlib.sha256(text.encode()).hexdigest(),
        executableSha256=hashlib.sha256(executable.read_bytes()).hexdigest(), command=command,
        tsec=57, nativeSchedulerQualified=False, nativeTutorialExecuted=False, pcmOrDspPlayback=False,
        scope="Unchanged timedown_play_select/flash_text_proc; unchanged caller spawn/cleanup and forced-controller "
              "suffix, ChoosePlay/MakeChoice. Declared ucontext host and symbolic text/play handles; "
              "actual Goose suspend/resume bodies. No native ABI, custom decompression, video timing or DSP claim.")
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return result
