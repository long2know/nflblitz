#!/usr/bin/env python3
"""Privately extract only pinned trail/draw/math; compare defined typed output with actual C#."""
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
spec = importlib.util.spec_from_file_location("trail_original_extraction", ROOT / "tests/fire-spfx/oracle.py")
extraction = importlib.util.module_from_spec(spec)
spec.loader.exec_module(extraction)
PINS = {
    "player": "61fb1be76029663294630e8633574ed065496da1ef6329071817e80622870b52",
    "pipe": "ad9ae52d41d90931c138f08511c49a997f16b798a4541567b4fcc34fcc375eb4",
    "trigs": "ae169332068a0561759bf5eb6ccaac4400a54ca5465d3b302908539388cf0ed4",
    "id": "06748a93cc40f76bd1a38a4315744449f6c743147a2ad5439221d564583720ea",
    "process": "9059fa40564d964706f91accb62089e8cc67293102718a86aec9a8441b6b0b7e",
    "object": "162fead2796b0ef7748c1cafc70b0a7d5217dfb434996cb84bb22ac05c18c737",
    "nfl": "97013eed27a110e11f7180954ed7b7977ee7dc113c372d426392a686948ab434",
    "selection": "c09cc5ce3306756eaafbbc3b68e0f9c0e426df093194feeba0ee0d1284f66c74",
}


def compare(actual, expected, path):
    if isinstance(expected, dict):
        assert set(actual) == set(expected), path + ": exact output field inventory differs"
        for key in expected:
            compare(actual[key], expected[key], path + "." + key)
    elif isinstance(expected, list):
        assert len(actual) == len(expected), path + ": exact output length differs"
        for i, (a, b) in enumerate(zip(actual, expected)):
            compare(a, b, path + "[" + str(i) + "]")
    elif isinstance(expected, float):
        assert math.isclose(actual, expected, rel_tol=2e-7, abs_tol=2e-5), f"{path}: C#{actual} != C{expected}"
    else:
        assert actual == expected, f"{path}: C#{actual!r} != C{expected!r}"


def run(originals, production, output):
    output.mkdir()
    raw = {}
    for name, pin in PINS.items():
        raw[name] = Path(originals[name]).read_bytes()
        assert hashlib.sha256(raw[name]).hexdigest() == pin, "Wrong pinned original " + name
    header = raw["id"].decode("ascii")
    for name, expected in {"BALL_PID": 0x900, "OID_BALL": 0x400, "DRAWORDER_BALL": 0x50000, "RL_BALL": 0x600}.items():
        match = re.search(r"^\s*#define\s+" + name + r"\s+(0x[0-9a-fA-F]+)", header, re.M)
        assert match and int(match[1], 16) == expected, "Pinned id.h symbol differs: " + name
    id_blob = hashlib.sha1(b"blob " + str(len(raw["id"])).encode() + b"\0" + raw["id"]).hexdigest()
    assert id_blob == "d2cc50865dd105c4143ac6b769765bd859cc4ff4"
    process, objects = raw["process"].decode("ascii"), raw["object"].decode("ascii")
    assert "pn->process_sleep_time = 1;" in process and "pn->run_level = 0;" in process
    assert "while(sn_list && (sn_list->draw_order <= pobj->draw_order))" in objects
    nfl, selection = raw["nfl"].decode("ascii"), raw["selection"].decode("ascii")
    for code in ("suspend_multiple_processes(0, 0x8000);", "resume_multiple_processes(SPFX_PID, -1);",
                 "hide_all_3d_objects();", "resume_all();", "unhide_all_3d_objects();"):
        assert code in selection, "Verified selection control missing: " + code
    assert not re.search(r"killall\s*\(\s*BALL_PID|delete_multiple_objects\s*\(\s*OID_BALL", nfl + selection)
    player = raw["player"].decode("ascii")
    active = re.sub(r"//[^\n]*|/\*.*?\*/", "", player, flags=re.S)
    assert re.search(r'qcreate\(\s*"balltrl",\s*BALL_PID,\s*trail_ball_proc,\s*0,\s*0,\s*0,\s*0\s*\)', active)
    assert not re.search(r"set_process_run_level\s*\([^;]*trail_ball_proc", active)
    source = (HERE / "oracle_shim.c").read_text()
    source = source.replace("/* ORIGINAL_TRIGS */", raw["trigs"].decode("ascii"))
    source = source.replace("/* ORIGINAL_MATH */", "\n".join(extraction.extract(raw["pipe"].decode("ascii"), name, "pipe3D.c")
                            for name in ("isin", "icos", "rotx", "roty", "rotz", "mxm")))
    source = source.replace("/* ORIGINAL_DRAW */", extraction.extract(player, "draw_ball"))
    source = source.replace("/* ORIGINAL_TRAIL */", extraction.extract(player, "trail_ball_proc"))
    emitted, executable = output / "original-trail-draw.c", output / "original-trail-draw"
    emitted.write_text(source)
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output))
    command = ["cc", "-std=c99", "-O0", "-ffp-contract=off", "-fsanitize=undefined",
               "-fno-sanitize-recover=undefined", str(emitted), "-o", str(executable)]
    built = subprocess.run(command, env=env, capture_output=True, text=True, timeout=90)
    (output / "compile.log").write_text(built.stdout + built.stderr)
    assert built.returncode == 0, "The defined-domain original-C oracle must compile: " + built.stderr
    rows = {}
    for label in ("normal", "suspended", "draw"):
        text = ""
        if label == "draw":
            for case in production["drawCases"]:
                i = case["input"]
                values = [i["carrier"], i["globalFlags"], i["bigBall"], int(i["hidden"]), i["localFlags"],
                          i["alpha"], i["scale"], *i["position"], i["heading"], i["elevation"], i["roll"],
                          *i["cameraEye"], *i["cameraMatrix"]]
                text += " ".join(map(str, values)) + "\n"
        result = subprocess.run([str(executable), label], input=text, env=env,
                                capture_output=True, text=True, timeout=30)
        (output / (label + ".log")).write_text(result.stdout + result.stderr)
        assert result.returncode == 0 and not result.stderr, "Defined original C execution/sanitizer failure: " + result.stderr
        rows[label] = json.loads(result.stdout)
    compare(production["normalRows"], rows["normal"], "trail_ball_proc.normal")
    compare(production["suspendedRows"], rows["suspended"], "trail_ball_proc.suspended")
    actual = [{k: v for k, v in row.items() if k != "input"} for row in production["drawCases"]]
    compare(actual, rows["draw"], "draw_ball.current-global-vs-local-copy")
    report = {
        "schema": "original-trail-ball-defined-domain/v1", "status": "passed", "pins": PINS,
        "headerGitBlobSha1": id_blob, "compilerCommand": command,
        "extractedFunctionClosure": ["trail_ball_proc", "draw_ball", "isin", "icos", "rotx", "roty", "rotz", "mxm"],
        "normalRows": len(rows["normal"]), "suspendedRows": len(rows["suspended"]), "drawCases": len(rows["draw"]),
        "exactOutputFieldAndLengthComparison": True, "undefinedSanitizerPassed": True,
        "adapters": [
            "Typed authored render-field structs, not native layout or byte-copy/ABI acceptance.",
            "Unsigned alpha at GL color packing avoids original signed alpha<<24 overflow.",
            "Cooperative ucontext stack remains alive during sleeps; object_data is never read after return.",
            "Declared host level0/short-sleep dispatch, not target process sorting/restart visits.",
            "Hidden nodes are skipped by the authored object-list adapter, not an extra draw_ball branch.",
        ],
        "sourceLifecycleTrace": {
            "ballCreation": "verified nfl.c:694 creates the persistent main ball at RL_BALL",
            "trailBirth": "verified player.c:10361 queues BALL_PID at default level0; RL_BALL setter is commented out",
            "selection": "verified playsel.c:852-859 suspends/hides existing BALL nodes; only SPFX is resumed",
            "exit": "verified playsel.c:777/782 clears SPFX/puffs only;1530-1531 resumes/unhides BALL nodes",
            "newPlay": "verified nfl.c:1109-1110,1290-1291 clears puff/SPFX, not BALL_PID/OID_BALL",
            "restart": "Core host reset drops its owned processes/objects; not a native program-reset/heap witness",
        },
        "nativePixelsOrAbiQualified": False,
    }
    (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")
    return report
