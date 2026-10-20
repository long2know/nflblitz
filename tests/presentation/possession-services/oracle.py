"""Unchanged selected C branches/functions on declared ucontext continuations, never native ABI proof."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools/rosters"))
from c_source import CData, closing_brace

PINS = {
    "cambot.c": "7411bdd1d13b4cbe968a2888f8189cce5d16fafa9f66cf9e4665a1adfbe5ac85",
    "plyrinfo.c": "69020d3f9526ee2b841c29ba9d5699fe4c01d3ac1c83c8f5e3733c25a7143def",
    "Game.c": "fe5de5c212bcbc9f1dfb36c17afed765fc7be6a18c8075166aca625c6f67e162",
    "select.c": "7e66183b17ee889ab78605ee09a84ef06598c4c3a3bd39166e81a1bd6eeae863",
    "process.c": "9059fa40564d964706f91accb62089e8cc67293102718a86aec9a8441b6b0b7e",
    "mthread.c": "ee27d5efe6b7ca4ab39ae82be16d3b235625e625b4220e2e96525ae42170d7a0",
    "sounds.c": "f42c2555cc24b31b8654eb35d163b79acf72e16cc7ac41b893ab75c9caaf4605",
    "include/sndcalls.h": "d4b95f2d905e85426a5255af91f9ab30d84cc188c0378e457c66135ca2a99c02",
    "sound.h": "54b2d81f5a1dc47f882499b11d7f2ede8cce0aff79841029bdb92994fedcdc4d",
    "include/id.h": "06748a93cc40f76bd1a38a4315744449f6c743147a2ad5439221d564583720ea",
    "include/GAME.H": "17f70ebdef3d903b7369050f434b948c11329f6bbda881a950e098f59e5eead3",
    "include/player.h": "9f280ff09cf13720501528126792d816ad7862bdf74a6b21b7e8bf5320b44d80",
    "nfl.c": "97013eed27a110e11f7180954ed7b7977ee7dc113c372d426392a686948ab434",
}


def function(data, name, signature):
    return signature + data.function(name)[1] + "\n"


def harness(raw):
    env = {}
    defines = ""
    for label in ("include/GAME.H", "include/player.h", "include/id.h", "sound.h", "include/sndcalls.h"):
        data = CData(raw[label].decode("latin1"), env).enums()
        env = data.expressions.macros
    for name, macro in env.items():
        if name in ("NULL", "TRUE", "FALSE"):
            continue
        params = "" if macro.parameters is None else "(" + ",".join(macro.parameters) + ")"
        defines += "#define " + name + params + " " + macro.text + "\n"
    camera = CData(raw["cambot.c"].decode("latin1"), env)
    playerinfo = CData(raw["plyrinfo.c"].decode("latin1"), env)
    game = CData(raw["Game.c"].decode("latin1"), env)
    select = CData(raw["select.c"].decode("latin1"), env)
    process = CData(raw["process.c"].decode("latin1"), env)
    sounds = CData(raw["sounds.c"].decode("latin1"), env)
    shim = (Path(__file__).parent / "oracle_host.c").read_text()
    branch_start = camera.text.index("\tif (pgi->game_mode == GM_IN_PLAY || pgi->game_mode == GM_PLAY_OVER)",
                                      camera.text.index("static int cambot_special(", camera.text.index("static int cambot_special(") + 1))
    first_end = closing_brace(camera.text, camera.text.index("{", branch_start))
    otherwise = re.match(r"\s*else\s*\{", camera.text[first_end + 1:])
    if otherwise is None:
        raise ValueError("Original possession branch lost its non-live else arm.")
    else_open = first_end + 1 + otherwise.end() - 1
    branch_end = closing_brace(camera.text, else_open) + 1
    branch = camera.text[branch_start:branch_end]
    if branch.count("static int tout;") != 1:
        raise ValueError("Exact original possession branch timeout declaration unavailable.")
    # Read-only address telemetry for the local static. No branch, counter or process field changes.
    branch = branch.replace("static int tout;", "static int tout; timeout_witness = &tout;")
    proc = ""
    for name in ("suspend_process", "resume_process", "resume_all", "existp",
                 "suspend_multiple_processes", "resume_multiple_processes"):
        signature = ("struct process_node *" if name == "existp" else "void ") + "original_" + name
        body = process.function(name)[1]
        params = {
            "suspend_process": "(struct process_node *p)", "resume_process": "(struct process_node *p)",
            "resume_all": "(void)", "existp": "(int process_id,int mask)",
            "suspend_multiple_processes": "(int id,int mask)", "resume_multiple_processes": "(int id,int mask)",
        }[name]
        proc += signature + params + body + "\n"
    # Calls from the unchanged multiple-process bodies still use the actual single-node bodies.
    proc = proc.replace("suspend_process(pn)", "original_suspend_process(pn)").replace("resume_process(pn)", "original_resume_process(pn)")
    match = re.search(r"static image_info_t \*message_imgs\[\]\s*=\s*\{", playerinfo.text)
    end = closing_brace(playerinfo.text, match.end() - 1)
    images = playerinfo.text[match.start():end + 1] + ";\n"
    names = sorted(set(re.findall(r"&(\w+)", images)))
    declarations = "".join("static image_info_t " + name + ";\n" for name in names)
    body = playerinfo.function("show_message_proc")[1]
    start = body.index("\n\tif (t == MS_1STDOWN || t == MS_INTERCEPTION")
    active_end = closing_brace(body, body.index("{", start))
    message = "static void original_show_message_proc(int *args)" + body[:active_end + 1] + '\n unavailable("unselected message"); }\n'
    table = re.search(r"static int intercepted_sp\[\]\s*=\s*\{[^}]+\};", sounds.text)[0]
    speech = table + "\n" + function(sounds, "intercepted_ball_sp", "static void intercepted_ball_sp(void)")
    flash = function(game, "flash_obj_proc", "static void flash_obj_proc(int *args)")
    flash += function(select, "flash_obj_white", "static void flash_obj_white(sprite_info_t *obj,int cnt,int delay)")
    return shim.replace("/* ORIGINAL_DEFINES */", defines).replace("/* ORIGINAL_PROCESS */", proc) \
        .replace("/* ORIGINAL_IMAGES */", declarations + images).replace("/* ORIGINAL_MESSAGE */", message) \
        .replace("/* ORIGINAL_SPEECH */", speech).replace("/* ORIGINAL_FLASH */", flash) \
        .replace("/* ORIGINAL_POSSESSION_BRANCH */", branch)


def verified_authority(authority):
    authority = Path(authority).absolute()
    if authority.resolve() != authority or not authority.is_dir():
        raise ValueError("Explicit regular original-C authority directory is required.")
    actual = {p.relative_to(authority).as_posix() for p in authority.rglob("*") if p.is_file() or p.is_symlink()}
    if actual != set(PINS):
        raise ValueError("Exact13 original-C authority inventory differs: missing=" + repr(sorted(set(PINS) - actual)) +
                         "; unexpected=" + repr(sorted(actual - set(PINS))))
    raw = {}
    for name, expected in PINS.items():
        path = authority / name
        if path.is_symlink() or path.resolve() != path:
            raise ValueError("Escaping original-C authority input: " + name)
        raw[name] = path.read_bytes()
        actual = hashlib.sha256(raw[name]).hexdigest()
        if actual != expected:
            raise ValueError("Wrong authenticated actual possession authority: " + name + " sha256=" + actual)
    return raw


def generate(authority, output, cases):
    authority, output = Path(authority), Path(output)
    raw = verified_authority(authority)
    output.mkdir(parents=True)
    nfl = raw["nfl.c"].decode("ascii")
    if len(re.findall(r"game_info\.off_turnover\s*=\s*1\s*;", nfl)) != 2:
        raise ValueError("Retain both actual nfl fourth-down turnover producers.")
    text = harness(raw)
    source = output / "original-possession-services.c"
    source.write_text(text)
    executable = output / "original-possession-services"
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output))
    command = ["gcc", "-std=gnu11", "-O0", "-Wno-int-to-pointer-cast", "-Wno-pointer-to-int-cast",
               str(source), "-o", str(executable)]
    build = subprocess.run(command, env=env, capture_output=True, text=True, timeout=120)
    (output / "compile.log").write_text(build.stdout + build.stderr)
    if build.returncode:
        raise RuntimeError("Original selected C compile failed:\n" + build.stderr)
    result = {}
    for case in cases:
        keys = ("mode", "type", "ballFlags", "flags", "result", "off", "last", "turnover", "state", "ticks", "ordinal")
        values = [int(case[k]) for k in keys] + [case.get(k, 0) for k in ("killAt", "suspendAt", "menuAt", "resumeAt", "secondAt")]
        run = subprocess.run([str(executable), *map(str, values)], env=env, capture_output=True, text=True, timeout=30)
        if run.returncode:
            raise RuntimeError("Original C possession case failed: " + case["name"] + ":\n" + run.stderr)
        result[case["name"]] = json.loads(run.stdout)
        if len(result[case["name"]]) != case["ticks"]:
            raise ValueError("Original C omitted a declared source dispatch: " + case["name"])
        (output / (case["name"] + ".json")).write_text(run.stdout)
    receipt = dict(schema="original-possession-services-host/v1", originalInputs=PINS, compiler=command,
                   generatedSha256=hashlib.sha256(text.encode()).hexdigest(),
                   executableSha256=hashlib.sha256(executable.read_bytes()).hexdigest(), cases=len(cases),
                   declaredDispatches=sum(c["ticks"] for c in cases), nativeSchedulerQualified=False,
                   scope="Unchanged active cambot_special possession branch; actual show_message selected prefix/branch, "
                         "flash_obj_proc/flash_obj_white/intercepted_ball_sp/table and Goose mask/exists bodies. "
                         "Read-only tout address telemetry; native MIPS/ABI/sort/stack ordering not qualified.",
                   host="ucontext sleep continuations; low-address sprite handles retain original int casts. "
                        "qcreate head queues a new sleep1 node for next dispatch. Known level-zero then ascending Core slots. "
                        "Logical preloaded resource ownership; no WMS/Glide/DSP emulation. Adaptors count fixed host wakes, not original actor physics.")
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return result
