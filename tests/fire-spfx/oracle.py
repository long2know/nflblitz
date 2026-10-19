#!/usr/bin/env python3
"""Extract pinned original function bodies into private output and run declared C inputs."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]
PIN = "61fb1be76029663294630e8633574ed065496da1ef6329071817e80622870b52"
FUNCTIONS = ("player_burst_into_flames", "plyr_fire_trail_proc", "ball_fire_trail_proc", "flash_plyr_proc", "draw_spfx", "maybe_plyr_fire")
GEOM_SHA256 = "f6c729fa3b0a70fcdaca41df8151729a747b8cdc8a4fdcf4f06b094156bb643a"
GEOM_BLOB = "fecf16a0511ab5d14a7f5e3a96742407e1d43aa6"


def projection_reference(original_geom, points, output):
    raw = original_geom.read_bytes()
    assert hashlib.sha256(raw).hexdigest() == GEOM_SHA256, "Active original geom.s SHA256 changed."
    assert hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest() == GEOM_BLOB
    assembly = raw.decode("ascii")
    for operation in ("lui\t\tt0,0x4426", "nmadd.s\tf15,f15,f14,f2",
                      "madd.s\tf18,f25,f18,f23", "mul.s\tf19,f19,f28"):
        assert operation in assembly, "Pinned active geom.s projection operation missing: " + operation
    output.mkdir(exist_ok=True)
    source = output / "geom-unclamped-equation.c"
    source.write_text(r'''/* Active geom.s:123,147-169,188-204,224-229 equation only.
No native MIPS FMA/clamp/rounding/W-buffer/pixel equivalence is claimed. */
#include <stdio.h>
int main(void) {
    float camera_x, camera_y, camera_z; int height, count=0;
    putchar('[');
    while (scanf("%f %f %f %d", &camera_x, &camera_y, &camera_z, &height)==4) {
        if (camera_z==0 || (height!=384 && height!=256) || count>=128) return 2;
        float negated_x=-camera_x;
        float reciprocal=1.0f/camera_z;
        float perspective=664.0f*reciprocal;
        float yscale=height==256 ? 2.0f/3.0f : 1.0f;
        float x=256.0f+negated_x*perspective;
        float y=(float)height*.5f+(camera_y*yscale)*perspective;
        printf("%s[%.9g,%.9g,%.9g]",count++ ? "," : "",x,y,reciprocal);
    }
    puts("]"); return 0;
}
''')
    executable = output / "geom-equation"
    command = ["gcc", "-std=c99", "-O0", "-ffp-contract=off", str(source), "-o", str(executable)]
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output))
    built = subprocess.run(command, env=env, capture_output=True, text=True, timeout=60)
    (output / "compile.log").write_text(built.stdout + built.stderr)
    assert built.returncode == 0, "Bounded original-asm equation reference failed to compile: " + built.stderr
    text = "".join(" ".join(map(str, [*point["camera"], point["verticalResolution"]])) + "\n" for point in points)
    run = subprocess.run([str(executable)], input=text, env=env, capture_output=True, text=True, check=True, timeout=10)
    result = json.loads(run.stdout)
    assert len(result) == len(points)
    (output / "results.json").write_text(json.dumps({
        "authoritySha256": GEOM_SHA256, "authorityGitBlobSha1": GEOM_BLOB,
        "authority": "active NFL lib/ani3d/geom.s nmadd.s accumulated-X negation before664 perspective",
        "compilerCommand": command, "inputs": points, "raster": result,
        "nativeMipsOrPixelsQualified": False, "clampFmaAndNativeWBufferQualified": False,
    }, indent=2) + "\n")
    return result


def extract(text, name, label="player.c"):
    match = re.search(r"^[A-Za-z_][A-Za-z0-9_ \t*]*\b" + name + r"\s*\([^;{}]*\)\s*\{", text, re.M)
    if match is None:
        raise AssertionError("Missing exact original function: " + name)
    opening = match.end() - 1
    cleaned = re.sub(r'"(?:\\.|[^"\\])*"|/\*.*?\*/|//[^\n]*',
                     lambda m: m[0] if m[0].startswith('"') else re.sub(r"[^\n]", " ", m[0]),
                     text, flags=re.S)
    depth = 0
    for end in range(opening, len(cleaned)):
        depth += (cleaned[end] == "{") - (cleaned[end] == "}")
        if depth == 0:
            line = text[:match.start()].count("\n") + 1
            return '#line ' + str(line) + ' "' + label + '"\n' + text[match.start():end + 1] + "\n"
    raise AssertionError("Unterminated original function: " + name)


def run(original_player, cases_path, geometry_path, original_object, original_process, output):
    raw = original_player.read_bytes()
    if hashlib.sha256(raw).hexdigest() != PIN:
        raise AssertionError("The selected original player.c pin changed; no fallback source.")
    output.mkdir()
    source = (Path(__file__).parent / "oracle_shim.c").read_text()
    extracted = "\n".join(extract(raw.decode("ascii"), function) for function in FUNCTIONS)
    target = output / "original-functions.c"
    original = raw.decode("ascii")
    selection_start = original.index("switch( ball_obj.type )", original.index("// ball special effects"))
    selection_end = original.index("switch( effect )", selection_start)
    effect_end = original.index("// heading", selection_end)
    selection = ('static int original_ball_selection_body(void) {\nstatic int effect=EFFECT_NONE;\n#line ' +
                 str(original[:selection_start].count("\n") + 1) + ' "player.c"\n' + original[selection_start:effect_end] +
                 "\nreturn effect;\n}\n")
    target.write_text(source.replace("/* ORIGINAL_FUNCTIONS */", extracted).replace("/* ORIGINAL_BALL_SELECTION_BODY */", selection))
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output))
    executable = output / "oracle"
    command = ["gcc", "-std=c99", "-O0", "-ffp-contract=off", str(target), "-lm", "-o", str(executable)]
    built = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=120)
    (output / "compile.log").write_text(built.stdout + built.stderr)
    if built.returncode:
        raise AssertionError("Original-C oracle compilation failed:\n" + built.stdout + built.stderr)
    kinds = {"PlayerBurst": 0, "PlayerPuff": 1, "BallPuff": 2, "FlashPlayerWhite": 3}
    results = {}
    fixtures = json.loads(cases_path.read_text())
    for fixture in fixtures["cases"]:
        tape = fixture["draws"]
        args = [str(kinds[fixture["kind"]]), *map(str, fixture["args"][:3]), str(fixture["ticks"]),
                str(fixture["modeSwitchTick"]), "constant-zero" if tape is None else ",".join(map(str, tape)) if tape else "empty",
                str(geometry_path)]
        result = subprocess.run([str(executable), *args], cwd=ROOT, env=env, capture_output=True, text=True, timeout=20)
        if result.returncode:
            raise AssertionError(f"Original-C case{fixture['name']} failed: {result.stderr}")
        results[fixture["name"]] = json.loads(result.stdout)
    result = subprocess.run([str(executable), "4", "0", "0", "0", "0", "0", "empty", str(geometry_path)],
                            cwd=ROOT, env=env, capture_output=True, text=True, check=True)
    render = json.loads(result.stdout)
    emitters = {}
    for fixture in fixtures["maybeCases"]:
        values = [fixture["actor"], int(fixture["offense"]), fixture["station"], fixture["flags"], fixture["vx"], fixture["vz"],
                  int(fixture["reacting"]), int(fixture["qb"]), int(fixture["teamFire"]), fixture["pcount"], 0]
        result = subprocess.run([str(executable), "5", "0", "0", "0", "0", "0", str(fixture["draw"]), ",".join(map(str, values))],
                                cwd=ROOT, env=env, capture_output=True, text=True, check=True)
        emitters[fixture["name"]] = json.loads(result.stdout)
    sequence = ";".join(",".join(map(str, [f["kind"], int(f["fire"]), int(f["turbo"]), int(f["bounce"]), int(f["teamFire"]), f["flight"], f["mode"]]))
                        for f in fixtures["ballSequence"])
    result = subprocess.run([str(executable), "6", "0", "0", "0", "0", "0", "empty", sequence],
                            cwd=ROOT, env=env, capture_output=True, text=True, check=True)
    report = {"sourceSha256": PIN, "functions": list(FUNCTIONS), "compilerCommand": command,
              "caseCount": len(results), "cases": results,
              "render": render, "emitters": emitters, "ballSequence": json.loads(result.stdout),
              "ballSelectionBody": {"sourceLines": [original[:selection_start].count("\n") + 1, original[:selection_end].count("\n")],
                                    "namedBindingsOnly": True, "includesOriginalFireTripletBody": True,
                                    "includesOriginalPcloudBtrailFallthroughAndBlink": True},
              "qualification": "Extracted original C on declared host inputs/component order; no native scheduler/ABI/WMS/MIPS-pixel proof."}
    object_bytes, process_bytes = original_object.read_bytes(), original_process.read_bytes()
    if hashlib.sha1(b"blob " + str(len(object_bytes)).encode("ascii") + b"\0" + object_bytes).hexdigest() != "c99e75df81f4993e463a65362358b304e2bc5c6c":
        raise AssertionError("Wrong pinned Goose object.c.")
    if hashlib.sha256(process_bytes).hexdigest() != "9059fa40564d964706f91accb62089e8cc67293102718a86aec9a8441b6b0b7e":
        raise AssertionError("Wrong pinned Goose process.c.")
    object_functions = ("add_object_block", "insert_object", "create_object", "delete_object", "delete_multiple_objects",
                        "hide_all_3d_objects", "unhide_all_3d_objects")
    process_functions = ("suspend_process", "resume_process", "resume_all", "killall", "suspend_multiple_processes", "resume_multiple_processes")
    controls = (Path(__file__).parent / "controls_shim.c").read_text()
    controls = controls.replace("/* ORIGINAL_OBJECT_FUNCTIONS */", "\n".join(extract(object_bytes.decode("ascii"), f, "object.c") for f in object_functions))
    controls = controls.replace("/* ORIGINAL_PROCESS_FUNCTIONS */", "\n".join(extract(process_bytes.decode("ascii"), f, "process.c") for f in process_functions))
    control_source = output / "original-controls.c"; control_source.write_text(controls)
    control_executable = output / "controls"
    built = subprocess.run(["gcc", "-std=c99", "-O0", str(control_source), "-o", str(control_executable)],
                           cwd=ROOT, env=env, capture_output=True, text=True, timeout=120)
    (output / "controls-compile.log").write_text(built.stdout + built.stderr)
    if built.returncode:
        raise AssertionError("Original object/process C compilation failed: " + built.stderr)
    result = subprocess.run([str(control_executable)], cwd=ROOT, env=env, capture_output=True, text=True, check=True)
    report.update(controls=json.loads(result.stdout), objectSourceSha256=hashlib.sha256(object_bytes).hexdigest(),
                  processSourceSha256=hashlib.sha256(process_bytes).hexdigest(), controlFunctions=list(object_functions + process_functions),
                  partialKillMasksQualified=False, staleObjectPointerReuseQualified=False)
    (output / "results.json").write_text(json.dumps(report, indent=2) + "\n")
    return report
