"""Private execution of unchanged active cabinet controllers/caller and real page tables."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("selection_timer_authority", HERE / "oracle.py")
timer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(timer)
require, CData, closing_brace = timer.require, timer.CData, timer.closing_brace
from c_source import without_comments
EXTRA_PINS = {
    "nfl/PlayStuf.c": "12197b46ea73cb2563fcb80b8c61c4f1e819b314",
    "nfl/pmisc.c": "50426d40ecd03515cecd295fb80a63ffa0cb0683",
}


def verified_authority(root):
    raw = timer.verified_authority(root)
    for name, pin in EXTRA_PINS.items():
        path = Path(root) / name
        require(path.is_file() and not path.is_symlink() and path.resolve() == path,
                "Missing/escaping random controller authority: " + name)
        data = path.read_bytes()
        require(hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest() == pin,
                "Wrong authenticated random controller Git blob: " + name)
        raw[name] = data
    return raw


def cases():
    rows = []

    def add(name, raw=0, sides=(0,), four=False, captain=0, start=24, mode="normal",
            pages=0, flipped=False, selected=-1, cpu=False, hidden=False, buttons=0):
        stations = [0, 1, 2, 3] if four else [0] if cpu else [0, 1]
        owners = {s: s // 2 if four else s for s in stations}
        leaders = {side: (side * 2 + captain if four else side) for side in (0, 1)}
        events = {}

        def sample(at, station, x=0, z=0, held=0):
            events[at, station] = dict(tick=at, station=station, x=x, z=z, held=held)

        for side in sides:
            station = leaders[side]
            if mode == "noncaptain":
                station ^= 1
            prefix = 22
            if hidden:
                for at, z in ((22, -3), (23, 0), (24, -3), (25, 0)):
                    sample(at, station, z=z)
                prefix = 26
            for page in range(pages):
                sample(prefix + 2 * page, station, held=4)
                sample(prefix + 2 * page + 1, station)
            prefix += 2 * pages
            if flipped:
                sample(prefix, station, held=1)
                sample(prefix + 1, station)
            if mode == "early":
                begin, end = 2, 4
            else:
                begin, end = start - 2, start
            if mode not in ("one-left", "none"):
                sample(begin, station, x=-3)
                if mode != "held-left":
                    sample(begin + 1, station)
            if mode != "none":
                sample(end, station, x=0 if mode == "wrong-direction" else -3,
                       held=buttons if mode == "combined" else 4)
                sample(end + 1, station)
            if mode == "physical-overrides":
                sample(end + 3, station, x=3, z=3, held=7)
                sample(end + 9, station, x=-3, z=-3, held=2)
                sample(end + 12, station)
        if selected >= 0:
            sample(22, leaders[selected], held=2)
            sample(23, leaders[selected])
        rows.append(dict(name=name, raw=raw, four=four, captain=captain, cpu=cpu,
            stations=[dict(id=s, side=owners[s]) for s in stations], sides=list(sides),
            ticks=610 if start > 500 else 95, mode=mode, suspendAt=0, resumeAt=0,
            events=sorted(events.values(), key=lambda e: (e["tick"], e["station"]))))

    for raw in range(4):
        add("random-offense-raw" + str(raw), raw=raw)
        add("random-defense-raw" + str(raw), raw=raw, sides=(1,))
    add("random-both-two-station", raw=99, sides=(0, 1))
    for captain in (0, 1):
        add("random-both-four-captain" + str(captain), raw=captain, sides=(0, 1), four=True, captain=captain)
    add("random-four-single-teammates", raw=3, sides=(0, 1), four=True, captain=1)
    rows[-1]["stations"] = [dict(id=1, side=0), dict(id=3, side=1)]
    add("random-from-page3-preflipped", raw=0, start=34, pages=3, flipped=True)
    add("random-hidden-offense", raw=2, start=28, hidden=True)
    add("random-ignores-physical-ABC-and-B", raw=3, mode="physical-overrides")
    add("random-preserves-selected-other-side", raw=0, sides=(1,), selected=0)
    add("random-before-CPU-caller", raw=0, cpu=True)
    for start in (576, 577, 578, 596):
        add("random-timeout-start" + str(start), raw=2, start=start, sides=(0, 1))
    for mode in ("none", "one-left", "held-left", "wrong-direction", "early"):
        add("random-no-trigger-" + mode, mode=mode)
    for buttons in (3, 5, 6, 7):
        add("random-no-trigger-combo" + str(buttons), mode="combined", buttons=buttons)
    add("random-no-trigger-noncaptain", mode="noncaptain", four=True, captain=1)
    add("random-suspended-real-consumer", raw=0, sides=(0, 1))
    rows[-1].update(suspendAt=28, resumeAt=36)
    add("random-timeout-suspended-consumer", raw=1, sides=(0, 1), start=576)
    rows[-1].update(suspendAt=579, resumeAt=599)
    return rows


def initializer(source, name):
    match = re.search(r"\b" + name + r"\s*((?:\[[^\]\n]*\]\s*)+)\s*=\s*\{", source.text)
    require(match is not None, "Actual original initializer unavailable: " + name)
    start = match.end() - 1
    end = closing_brace(source.text, start)
    return match[1] + "=" + source.text[start:end + 1] + ";"


def harness(raw):
    env = {}
    for name in ("nfl/include/select.h", "nfl/include/id.h", "nfl/include/sndcalls.h",
                 "nfl/include/PlayStuf.h", "nfl/include/GAME.H", "midway/include/goose/colors.h"):
        env = CData(raw[name].decode("latin1"), env).enums().expressions.macros
    pages = CData(raw["nfl/PlayStuf.c"].decode("latin1"), env)
    env = pages.expressions.macros
    controller = CData(raw["nfl/playsel.c"].decode("latin1"), env).enums()
    env = controller.expressions.macros
    definitions = ""
    for name, value in env.items():
        if name in ("NULL", "TRUE", "FALSE"):
            continue
        parameters = "" if value.parameters is None else "(" + ",".join(value.parameters) + ")"
        definitions += "#define " + name + parameters + " " + value.text + "\n"
    tables = "\n".join("static PLAYSTUF__SLOT " + name + initializer(pages, name)
        for name in ("off_slots", "def_slots", "extra_slots", "team_slots"))
    tables += "\nstatic PLAYSTUF__PAGE play_pages" + initializer(pages, "play_pages")
    pools = ("short_off_plays", "long_off_plays", "short_def_plays", "long_def_plays",
             "short_def_plays2", "long_def_plays2", "blitz_def_plays")
    tables += "\n" + "\n".join("static play_t *" + name + initializer(controller, name) for name in pools)
    plays = sorted(set(re.findall(r"&((?:play_\w+|custom_play))\b", tables)) | {"play_extra_point", "custom_play"})
    declarations = "\n".join('static play_t ' + name + '={"' + name + '"};' for name in plays)
    graphics = sorted(set(re.findall(r"&(\w+)\b", tables)) - set(plays) -
                      {"off_slots", "def_slots", "extra_slots", "team_slots"})
    declarations += "\nstatic int " + ",".join(graphics) + ";\n"
    for audit in sorted(set(re.findall(r"\b[A-Z][A-Z_0-9]*_AUD\b", tables)) - set(env)):
        definitions += "#define " + audit + " 0\n"
    bodies = ""
    spans = {}
    for source, name, signature in (
        (controller, "play_select_cntrl", "static void play_select_cntrl(int *args)"),
        (controller, "PlaySel__MakeChoice", "static play_t *PlaySel__MakeChoice(int side,int page,int p_num,int cur_pos,int tm_num,int force_choose)"),
        (controller, "PlaySel__ChoosePlay", "static void PlaySel__ChoosePlay(int side,int page,int p_num,int cur_pos,int tm_num,int force_choose)"),
        (controller, "cpu_pick_play", "static void original_cpu_pick_play(int pnum)"),
        (pages, "PlayStuf__SetupCurrentPlayPages", "static void PlayStuf__SetupCurrentPlayPages(int do_specials,int do_extra_pt)"),
        (pages, "PlayStuf__GetSlot", "static PLAYSTUF__SLOT *original_get_slot(int side,int page,int num)"),
        (CData(raw["nfl/pmisc.c"].decode("latin1"), env), "off_captain", "static int off_captain(void)"),
        (CData(raw["nfl/lib/select/select.c"].decode("latin1"), env), "timedown_play_select", "static void timedown_play_select(int *args)"),
        (CData(raw["nfl/Game.c"].decode("latin1"), env), "flash_text_proc", "static void flash_text_proc(int *args)"),
    ):
        line, body = source.function(name)
        bodies += signature + body + "\n"
        spans[name] = dict(firstLine=line, lastLine=line + body.count("\n"), bodySha256=hashlib.sha256(body.encode()).hexdigest())
    sounds = "\n".join("static int " + n + initializer(controller, n)
                       for n in ("cursor_snds", "cursor_edge_snds", "cursor_ids"))
    parent = controller.function("play_select")[1]
    start = parent.index('qcreate("tm1plysl"')
    end = parent.index("sleep(1);", start) + len("sleep(1);")
    spawn = parent[start:end]
    start = parent.index("\tdo", end) if "\tdo" in parent[end:] else parent.index("do\n", end)
    end = parent.index("show_freept_idiot_box = TRUE;", parent.index("if (game_info.team_play[1]", start)) + len("show_freept_idiot_box = TRUE;")
    wait = parent[start:end]
    teardown = controller.function("clobber_playsel")[1]
    teardown = teardown[teardown.index("killall("):teardown.index("del1c(")]
    process = CData(raw["midway/lib/goose/process.c"].decode("latin1"), env)
    process_bodies = "\n".join("static void " + name + "(" + args + ")" + process.function(name)[1]
        for name, args in (("suspend_process", "struct process_node *p"), ("resume_process", "struct process_node *p")))
    mthread = raw["midway/lib/goose/mthread.c"].decode("latin1")
    masked = without_comments(mthread)
    wrappers = list(re.finditer(r"\bqcreate\s*\([^;{}]*\)\s*\{", masked))
    require(len(wrappers) == 1, "Exactly one original qcreate wrapper is required.")
    opening = wrappers[0].end() - 1
    end = closing_brace(masked, opening)
    # The unrelated MIPS inline assembler contains non-C # directives; only this intact C wrapper is compiled.
    body = CData("void qcreate(void)" + mthread[opening:end + 1], env).function("qcreate")[1]
    line = mthread[:opening].count("\n") + 1
    qcreate = "static struct process_node *qcreate(char *name,int process_id,void (*entry_func)(),int arg1,int arg2,int arg3,int arg4)" + body
    spans["qcreate"] = dict(sourcePath="midway/lib/goose/mthread.c", firstLine=line,
        lastLine=line + body.count("\n"), bodySha256=hashlib.sha256(body.encode()).hexdigest())
    line, body = process.function("create_process")
    spans["create_process-service-basis-not-native-execution"] = dict(sourcePath="midway/lib/goose/process.c",
        firstLine=line, lastLine=line + body.count("\n"), bodySha256=hashlib.sha256(body.encode()).hexdigest())
    function_paths = dict(play_select_cntrl="nfl/playsel.c", PlaySel__MakeChoice="nfl/playsel.c",
        PlaySel__ChoosePlay="nfl/playsel.c", cpu_pick_play="nfl/playsel.c",
        PlayStuf__SetupCurrentPlayPages="nfl/PlayStuf.c", PlayStuf__GetSlot="nfl/PlayStuf.c",
        off_captain="nfl/pmisc.c", timedown_play_select="nfl/lib/select/select.c", flash_text_proc="nfl/Game.c")
    for name, path in function_paths.items():
        spans[name]["sourcePath"] = path
    text = (HERE / "random_shim.c").read_text()
    for key, value in dict(DEFINES=definitions, DECLARATIONS=declarations, TABLES=tables, SOUNDS=sounds,
                           BODIES=bodies, SPAWN=spawn, WAIT=wait, TEARDOWN=teardown,
                           PROCESS=process_bodies, QCREATE=qcreate).items():
        text = text.replace("/* ORIGINAL_" + key + " */", value)
    return text, spans


def generate(authority, output, fixtures, boundaries):
    raw = verified_authority(authority)
    output.mkdir(parents=True)
    text, spans = harness(raw)
    source, executable = output / "original-random.c", output / "original-random"
    source.write_text(text)
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output))
    command = ["gcc", "-std=gnu11", "-O0", "-Wno-int-to-pointer-cast", "-Wno-pointer-to-int-cast",
               "-Wno-int-conversion", str(source), "-o", str(executable)]
    build = subprocess.run(command, env=env, capture_output=True, text=True, timeout=90)
    (output / "compile.log").write_text(build.stdout + build.stderr)
    require(build.returncode == 0, "Unchanged active random controller C failed to compile:\n" + build.stderr)
    results = {}
    for case in fixtures:
        context = boundaries[case["name"]]
        header = [case["raw"], int(case["four"]), case["captain"],
                  sum(1 << s["id"] for s in case["stations"]), context["offense"], 4, 8,
                  context["los"], context["down"], context["quarter"], context["time"], context["playType"], case["ticks"],
                  context["firstDown"], context["homeScore"], context["awayScore"], case["suspendAt"], case["resumeAt"]]
        frames = {(e["tick"], e["station"]): e for e in case["events"]}
        current = [dict(x=0, z=0, held=0) for _ in range(4)]
        previous = [0] * 4
        lines = [" ".join(map(str, header))]
        for tick in range(1, case["ticks"] + 1):
            for station in range(4):
                current[station] = frames.get((tick, station), current[station])
                row = current[station]
                joy = (4 if row["x"] < 0 else 8 if row["x"] > 0 else 0) | (1 if row["z"] < 0 else 2 if row["z"] > 0 else 0)
                pressed = row["held"] & ~previous[station]
                previous[station] = row["held"]
                lines.append(f"{joy} {pressed}")
        payload = "\n".join(lines) + "\n"
        (output / (case["name"] + ".inputs")).write_text(payload)
        run = subprocess.run([str(executable)], input=payload, env=env, capture_output=True, text=True, timeout=10)
        require(run.returncode == 0, "Original random controller failed " + case["name"] + ":\n" + run.stderr)
        result = json.loads(run.stdout)
        # Compare only the attempted controller's sounds; pre-existing manual sound publication is a separate boundary.
        result["sounds"] = [r for r in result["sounds"] if r[1] in case["sides"]]
        results[case["name"]] = result
        (output / (case["name"] + ".json")).write_text(json.dumps(result) + "\n")
    pins = dict(timer.PINS, **EXTRA_PINS)
    receipt = dict(schema="original-cabinet-random/v1", command=command, spans=spans,
        inputs={n: dict(gitBlobSha1=pins[n], bytes=len(b), sha256=hashlib.sha256(b).hexdigest()) for n, b in raw.items()},
        cases=len(fixtures), generatedSha256=hashlib.sha256(text.encode()).hexdigest(),
        executableSha256=hashlib.sha256(executable.read_bytes()).hexdigest(),
        rawInputPolicy="Unfiltered constant raw input per case, including constructor/AI/camera/sounds in managed execution; not target random().",
        scope="Unchanged active #if1 controllers, choices, CPU caller, slot tables/page setup, off_captain, timer/flasher, parent spawn/wait/teardown. "
              "Authored ucontext/source service bindings and symbolic graphics/play handles; not native scheduler/ABI/pixels/DSP/custom decompression.")
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return results
