#!/usr/bin/env python3
"""Compare actual ball-owner observations to unchanged pinned ball_proc selections."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

PIN = "61fb1be76029663294630e8633574ed065496da1ef6329071817e80622870b52"
CASES = 25
ASSERTIONS = 171
RETAINED_ASSERTIONS = 40
KINDS = {"Pass": 0, "Fumble": 1, "Punt": 2, "Kickoff": 3, "FieldGoal": 4, "Bobble": 5, "Lateral": 6}


def require_inventory(production):
    if not isinstance(production, dict):
        raise AssertionError("Exact nonempty ball blink owner assertion/case inventory required.")
    probes = production.get("ballBlinkProbes")
    if (production.get("status") != "passed" or type(production.get("ballBlinkAssertions")) is not int
            or production.get("ballBlinkAssertions") != ASSERTIONS
            or type(production.get("assertions")) is not int
            or production.get("assertions") != RETAINED_ASSERTIONS + ASSERTIONS
            or not isinstance(probes, list) or len(probes) != CASES
            or production.get("fullGameQualified") is not False or production.get("nativeQualified") is not False):
        raise AssertionError("Exact nonempty ball blink owner assertion/case inventory required.")
    for probe in probes:
        if (not isinstance(probe, dict) or not isinstance(probe.get("name"), str) or not probe["name"]
                or not isinstance(probe.get("input"), dict) or not isinstance(probe.get("actual"), dict)
                or set(probe["input"]) != {"kind", "rawFlags", "flight", "mode", "carrier", "teamFire", "offSide"}
                or set(probe.get("actual", {})) != {"effect", "white", "flight"}
                or not isinstance(probe["input"]["kind"], str) or probe["input"]["kind"] not in KINDS
                or any(type(probe["input"][key]) is not int for key in
                       ("rawFlags", "flight", "mode", "carrier", "teamFire", "offSide"))
                or not 0 <= probe["input"]["rawFlags"] <= 0x7fffffff
                or not 0 <= probe["input"]["flight"] < 0x7fffffff
                or probe["input"]["mode"] not in range(6) or probe["input"]["carrier"] not in range(-1, 14)
                or probe["input"]["teamFire"] not in (0, 1) or probe["input"]["offSide"] not in (0, 1)
                or probe["actual"]["effect"] not in ("None", "Fire", "PlayerCloud", "BallTrail", "Blink")
                or type(probe["actual"]["white"]) is not bool or type(probe["actual"]["flight"]) is not int
                or not 0 <= probe["actual"]["flight"] <= 0x7fffffff):
            raise AssertionError("Exact original ball blink probe shape required.")
    if len({p["name"] for p in probes}) != CASES:
        raise AssertionError("Unique ordered ball blink cases required.")
    return probes


def compare(original_player, production, output):
    probes = require_inventory(production)
    original_player, output = Path(original_player), Path(output)
    if original_player.name != "player.c" or original_player.is_symlink():
        raise AssertionError("Explicit original player.c input required.")
    raw = original_player.read_bytes()
    if hashlib.sha256(raw).hexdigest() != PIN:
        raise AssertionError("Original ball_proc input pin changed.")
    output.mkdir()
    start = raw.index(b"void ball_proc(")
    guard = raw.index(b"\n\t\tif ((game_info.ball_carrier == -1)", start)
    guard_end = raw.index(b"{", guard) + 1
    increment = raw.index(b"\n\t\t\tball_obj.flight_time += 1;", guard_end)
    increment_end = raw.index(b"// ball special effects", increment)
    selection = raw.index(b"switch( ball_obj.type )", increment_end)
    selection_end = raw.index(b"switch( effect )", selection)
    blink = raw.index(b"if( ball_obj.flight_time % 10 == 0 )", selection_end)
    blink_end = raw.index(b"break;", blink)
    windows = []

    def selected(label, first, last):
        body = raw[first:last]
        windows.append({"name": label, "first_line": raw[:first].count(b"\n") + 1,
                        "start_byte": first, "end_byte_exclusive": last,
                        "bytes": len(body), "sha256": hashlib.sha256(body).hexdigest(),
                        "unchanged_original_bytes": True})
        return body

    header = b"""
#include <stdio.h>
#define LB_PASS 0
#define LB_FUMBLE 1
#define LB_PUNT 2
#define LB_KICKOFF 3
#define LB_FIELD_GOAL 4
#define LB_BOBBLE 5
#define LB_LATERAL 6
#define BF_WHITE 1
#define BF_TURBO_PASS 4
#define BF_FIRE_PASS 8
#define BF_BOUNCE 32
#define BF_PHANTOM 2048
#define GM_IN_PLAY 2
#define GM_PLAY_OVER 3
#define EFFECT_NONE 0
#define EFFECT_FIRE 1
#define EFFECT_PCLOUD 2
#define EFFECT_BTRAIL 3
#define EFFECT_BLINK 4
static struct { int type,flags,flight_time; } ball_obj;
static struct { int ball_carrier,game_mode,team_fire[2],off_side; } game_info;
static int effect=EFFECT_NONE;
static void original_owner_step(void) {
"""
    source = (header + selected("free-or-phantom-guard", guard, guard_end)
              + selected("postincrement-and-white-clear", increment, increment_end)
              + selected("sticky-selection-and-play-over-priority", selection, selection_end)
              + b"\nif(effect==EFFECT_BLINK) {\n" + selected("original-blink-write", blink, blink_end)
              + b"\n}\n}\n}\n")
    source += b"""
int main(void) {
 const char *names[]={"None","Fire","PlayerCloud","BallTrail","Blink"};
 int team;
 while(scanf("%d %d %d %d %d %d %d",&ball_obj.type,&ball_obj.flags,&ball_obj.flight_time,
   &game_info.game_mode,&game_info.ball_carrier,&team,&game_info.off_side)==7) {
  if(game_info.off_side<0 || game_info.off_side>1 || team<0 || team>1)return 2;
  game_info.team_fire[0]=game_info.team_fire[1]=0;
  game_info.team_fire[game_info.off_side]=team;
  original_owner_step();
  printf("{\\"effect\\":\\"%s\\",\\"white\\":%s,\\"flight\\":%d}\\n",
   names[effect],(ball_obj.flags&BF_WHITE)?"true":"false",ball_obj.flight_time);
 }
 return 0;
}
"""
    compiler = shutil.which("gcc") or shutil.which("clang")
    if compiler is None:
        raise ValueError("An actual C compiler is required for the original ball blink comparison.")
    generated, binary = output / "original-ball-blink.c", output / "original-ball-blink"
    generated.write_bytes(source)
    command = [compiler, "-std=c99", "-O0", "-fno-fast-math", "-ffp-contract=off", str(generated), "-o", str(binary)]
    environment = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output))
    built = subprocess.run(command, capture_output=True, text=True, env=environment, timeout=120)
    (output / "compile.log").write_text(built.stdout + built.stderr)
    if built.returncode:
        raise AssertionError("Original ball blink C must compile before any behavioral comparison.")
    lines = []
    for probe in probes:
        p = probe["input"]
        lines.append(" ".join(str(v) for v in (KINDS[p["kind"]], p["rawFlags"], p["flight"], p["mode"],
                                              p["carrier"], p["teamFire"], p["offSide"])))
    observed = subprocess.run([str(binary)], input="\n".join(lines) + "\n", capture_output=True,
                              text=True, env=environment, check=True, timeout=30)
    rows = [json.loads(line) for line in observed.stdout.splitlines()]
    if len(rows) != CASES:
        raise AssertionError("Original C must execute every ordered ball blink probe.")
    for probe, expected in zip(probes, rows):
        if probe["actual"] != expected:
            raise AssertionError("Original C ball blink differs at " + probe["name"])
    if hashlib.sha256(original_player.read_bytes()).hexdigest() != PIN:
        raise AssertionError("Original ball_proc input changed during comparison.")
    report = {"schema": "original-ball-blink-owner-reference/v1", "status": "passed",
              "original_source_sha256": PIN, "original_windows": windows, "cases": CASES,
              "command": command, "compiler_exit": built.returncode,
              "generated_source_sha256": hashlib.sha256(source).hexdigest(),
              "executable_sha256": hashlib.sha256(binary.read_bytes()).hexdigest(),
              "source_unchanged": True,
              "boundary": "Original free/phantom guard, flight increment/white clear, sticky selector and blink write on actual owner inputs; physics/effect processes remain separately qualified.",
              "native_target_or_full_game_parity": False}
    (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")
    return report
