"""Compile byte-pinned unchanged detect.c windows with explicit host VM/audio boundaries."""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

from source import PINS, read, function, blob
from translate import constants, translate

ROOT = Path(__file__).resolve().parents[2]
SUITE = Path(__file__).resolve().parent


def build(original, operands, camera_original, output):
    original, output = Path(original), Path(output).resolve()
    if not output.is_relative_to(SUITE / ".artifacts"):
        raise ValueError("C oracle outputs must stay in this worktree's ignored contact artifacts.")
    output.mkdir(parents=True, exist_ok=True)
    sources = read(original)
    pipe = (original / "lib/ani3d/pipe3D.c").read_bytes()
    if blob(pipe) != "0966ecaf0d7eef7d9c635a97899a421ce1eeea41":
        raise ValueError("Exact original line-intersection math is required.")
    sources["lib/ani3d/pipe3D.c"] = pipe
    animation = (Path(camera_original) / "lib/ani3d/ANIM.C").read_bytes()
    if blob(animation) != "97d7856d67a432937e249be274ef67a9ad354dc1":
        raise ValueError("Exact original ANIM.C puppet position source is required.")
    sources["lib/ani3d/ANIM.C"] = animation
    trigs = (original / "lib/ani3d/TRIGS.H").read_bytes()
    if blob(trigs) != "a07d76798931f1240bdf7f3b8e753ebd7832763a":
        raise ValueError("Exact original integer trig input is required.")
    sources["lib/ani3d/TRIGS.H"] = trigs
    windows = []

    def selected(path, start, end, name):
        data = sources[path][start:end]
        first = sources[path][:start].count(b"\n") + 1
        windows.append({"path": path, "function": name, "first_line": first,
                        "last_line": sources[path][:end].count(b"\n") + 1,
                        "sha256": hashlib.sha256(data).hexdigest(), "unchanged_original_bytes": True})
        return f'\n#line {first} "{path}"\n' + data.decode("latin1") + "\n"

    def original_function(path, name):
        start, end, data = function(sources[path], name)
        return selected(path, start, end, name)

    definitions = {}
    playstuff = (original / "include/PlayStuf.h").read_bytes()
    if blob(playstuff) != "3df5e8e8a3f478c3b02860a401c950d1e83c5ad3":
        raise ValueError("Exact original block play flags are required.")
    sources["include/PlayStuf.h"] = playstuff
    for name in ("include/player.h", "include/ani3d.h", "include/PlayStuf.h"):
        for raw in sources[name].decode("latin1").splitlines():
            match = re.match(r"#define\s+(\w+)\s+(.+?)(?:\s*//.*)?$", raw)
            if match and match[1].startswith(("PF_", "ATTMODE_", "JOINT_", "MODE_", "JOB_", "LB_", "BF_", "PLF_")):
                definitions[match[1]] = match[2]
    # GAME.H constants are verified against its already pinned full-kernel input.
    game = (original / "include/GAME.H").read_bytes()
    if blob(game) != "09d65c92971c32c44b1dc49fb4e4e98b371b1cd3":
        raise ValueError("Exact original game modes/flags are required.")
    for raw in game.decode("latin1").splitlines():
        match = re.match(r"#define\s+(\w+)\s+(.+?)(?:\s*//.*)?$", raw)
        if match and match[1].startswith(("GF_", "LB_", "GM_", "PT_", "PLF_")):
            definitions[match[1]] = match[2]
    programs = translate(sources, operands)
    sequence_names = sorted(set(re.findall(r"\bSEQ_(\w+)", sources["detect.c"].decode("latin1")))
                            | {"SPIN", "T_CRZDIV", "T_SPRHED", "RUN"})
    for i, name in enumerate(sequence_names):
        definitions["SEQ_" + name] = str(i)
    constants_text = "\n".join("#define " + key + " " + value for key, value in sorted(definitions.items()))
    declaration_names = set(programs)
    detect = sources["detect.c"]
    # Original tables include function forward declarations, with all multiplicities intact.
    start = detect.index(b"typedef void (*cfunc)")
    end = detect.index(b"\n///////////////////////////////////////////////////////////////////////////////", start)
    tables = selected("detect.c", start, end, "original contact animation/probability tables")
    declaration_names.update(re.findall(r"\b([a-z]\w*_anim|fall_\d+f?|m_nostomp)\b", tables))
    declaration_names.discard("change_anim")
    scripts_text = "\n".join('static char ' + name + '[]="' + name + '";' for name in sorted(declaration_names))
    scripts_text += "\nstatic unsigned script_mode(const char *name) {\n"
    for name, rows in programs.items():
        mode = next((row.get("a", 0) for row in rows if row["op"] == "SetMode"), 0)
        scripts_text += f'if(!strcmp(name,"{name}"))return {mode}u;\n'
    scripts_text += "fprintf(stderr,\"unknown script: %s\\n\",name);exit(97);}\n"
    handicap_tables = []
    for name in ("oline_block_ht", "qb_stiff_arm_ht", "bob_up_ht", "bob_cap_ht",
                 "bobble_human_ht", "bobble_hteam_ht", "bobble_drone_ht"):
        data = sources["handicap.c"]
        match = re.search(rb"(?m)^int\s+" + name.encode() + rb"\[12\][^;]+;", data)
        if not match:
            raise ValueError("Missing exact original handicap table " + name)
        handicap_tables.append(selected("handicap.c", match.start(), match.end(), name))
    functions = [selected("lib/ani3d/TRIGS.H", 0, len(trigs), "original quarter-sine table")]
    functions += [original_function("lib/ani3d/pipe3D.c", name) for name in ("isin", "icos", "vxv", "magv", "det")]
    for macro in ("FRELX", "FRELZ"):
        match = re.search(rb"(?m)^#define[ \t]+" + macro.encode() + rb"[^\r\n]*", animation)
        if not match:
            raise ValueError("Missing original puppet rotation macro " + macro)
        functions.append(selected("lib/ani3d/ANIM.C", match.start(), match.end(), macro))
    functions.append(original_function("lib/ani3d/ANIM.C", "set_puppet_pos"))
    functions += [original_function("pmisc.c", name) for name in ("norm2", "bearing")]
    # Entry/geometry/dispatcher/active attack and block bodies are never translated or edited.
    for name in ("player_detect_proc", "line_int_check", "point2line_d", "extend_line", "do_collision", "att_none",
                 "att_push", "att_dive", "att_kick", "att_late_hit", "att_spear_head", "att_swipe",
                 "att_head", "att_hurdle", "evade_vs_tackle", "fumble_or_bobble"):
        functions.append(original_function("detect.c", name))
    block_start = detect.index(b"typedef struct _block_result_t")
    block_end = detect.index(b"\n//////////////////////////////////////////////////////////////////////////////", detect.index(b"block_result_t bt_rush_total2", block_start))
    functions.append(selected("detect.c", block_start, block_end, "original six block result tables"))
    functions.append(original_function("detect.c", "block_vs_rush"))
    functions.append(original_function("detect.c", "delay_plow_proc"))
    functions.insert(0, original_function("player.c", "maybe_pop_helmet"))
    # Record the real do_collision entry without altering its original body.
    tables += "\n#define do_collision oracle_do_collision\n"
    # A wrapper declaration is used by the unchanged detector; the original remains separately callable.
    functions_text = "\n".join(functions)
    functions_text = functions_text.replace("#define do_collision oracle_do_collision", "")
    text = (SUITE / "driver.c").read_text()
    text = text.replace("/* ORIGINAL_CONSTANTS */", constants_text)
    text = text.replace("/* ORIGINAL_SCRIPTS */", scripts_text)
    # Use a call-only macro around the unchanged detector selection, not a rewritten C expression.
    functions_text = functions_text.replace(
        '\n#line ', '\n#line ')
    text = text.replace("/* ORIGINAL_TABLES */", "\n".join(handicap_tables) + tables.removesuffix("\n#define do_collision oracle_do_collision\n"))
    # Detector body calls the authored wrapper; all original dispatch/body functions retain exact bytes.
    detector_start, detector_end, _ = function(detect, "player_detect_proc")
    detector_text = selected("detect.c", detector_start, detector_end, "detector wrapper inclusion")
    original_detector = original_function("detect.c", "player_detect_proc")
    functions_text = functions_text.replace(original_detector, "")
    wrapper = """
static void oracle_do_collision(fbplyr_data *a,fbplyr_data *d,int mode) {
 char value[16];snprintf(value,sizeof(value),"%d",mode);
 event("hit",a->plyrnum,d->plyrnum,value);do_collision(a,d,mode);
}
#define do_collision oracle_do_collision
"""
    text = text.replace("/* ORIGINAL_FUNCTIONS */", functions_text + wrapper + detector_text + "\n#undef do_collision\n")
    generated, binary = output / "contact-original.c", output / "contact-original"
    generated.write_bytes(text.encode("latin1"))
    compiler = shutil.which("gcc") or shutil.which("clang")
    if not compiler:
        raise RuntimeError("A native C compiler is required for original contact evidence.")
    work = output / "work"
    work.mkdir(exist_ok=True)
    environment = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work))
    command = [compiler, "-std=c99", "-O0", "-fno-fast-math", "-ffp-contract=off",
               "-fexcess-precision=standard", "-Werror=implicit-function-declaration",
               str(generated), "-lm", "-o", str(binary)]
    process = subprocess.run(command, cwd=ROOT, env=environment, capture_output=True, text=True)
    (output / "compiler.log").write_text(process.stdout + process.stderr)
    if process.returncode:
        raise RuntimeError("Original C contact compilation failed; see contact/.artifacts/native/compiler.log.")
    receipt = {"schema": "source-contact-original-c/v1", "source_revision": "89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
               "status": "compiled", "command": command, "windows": windows,
               "source_sha256": {name: hashlib.sha256(data).hexdigest() for name, data in sources.items()},
               "boundary": "Actual unchanged one-wake detector/dispatch/active attack/evade/block/fumble/helmet C. "
                           "Projected host structs, explicit draw tape; ChangeAnimation applies only source initial MODE, "
                           "without pose/VM execution. Audio/DSP/effects are excluded. Separate full-kernel tests execute actual clips/VM.",
               "sequence_names": sequence_names, "full_gameplay_parity_claimed": False}
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return binary, receipt
