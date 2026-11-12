#!/usr/bin/env python3
"""Finite native-opcode comparisons; original bodies and real clips stay in private outputs."""

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import select
import struct
import subprocess
import sys
import tarfile
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tests/contact"))
import source

PINS = {
    "lib/ani3d/ANIM.C": "97d7856d67a432937e249be274ef67a9ad354dc1",
    "lib/ani3d/pipe3D.c": "0966ecaf0d7eef7d9c635a97899a421ce1eeea41",
    "include/ani3d.h": "826c6322467fcfe503b8402b540e4731671f9b6b",
    "include/player.h": "a86b1a5b52ba842b206cd7dd0bbb26521db6dcb8",
    "player.c": "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c",
    "plyrseq.c": "28cef0d1082f2b0c9241930fdc8886de4c8f5438",
    "pmisc.c": "50426d40ecd03515cecd295fb80a63ffa0cb0683",
    "CamData.c": "4bfc54c6b9633b076f01163c2142c7fce799a0e0",
    "cambot.c": "00c9c913bc6a697dfd590666bf5597f2c2bc1eb7",
}
CLASSES = {
    "ANI_ATTACK_OFF", "ANI_ATTACK_ON", "ANI_CLRFLAG", "ANI_CODE", "ANI_CODE_BNZ",
    "ANI_CODE_BZ", "ANI_END", "ANI_LEAPATT", "ANI_LEAPAT_TGTX", "ANI_PREP_XITION",
    "ANI_RUNTOFRAME", "ANI_SETCAMBOT", "ANI_SETFLAG", "ANI_SETFRICTION", "ANI_SETVELS",
    "ANI_SETYVEL", "ANI_SET_FACE_REL_VEL", "ANI_SHAKER", "ANI_TURNHACK",
}


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def require(value, message):
    if not value:
        raise ValueError(message)


def floating(value):
    return struct.unpack("<f", struct.pack("<f", value))[0].hex() + "f"


def bits(value):
    return struct.unpack("<I", struct.pack("<f", value))[0]


def run_capture(original_root, data_root, output, dotnet, diagnostic, controls=None, left_release=False,
                suite="opcodes", source_controls=False):
    require(not output.exists(), "Use a new Nov11 output directory.")
    require(output.is_relative_to(ROOT / ".squad/.scratch") or
            output.parent == Path("/home/long2know/nflblitz-unity-evidence-20261111"),
            "Private receipts belong in project scratch or the uniquely owned Nov11 Linux evidence directory.")
    os.chdir(output.parent)
    output = Path(output.name)
    output.mkdir()
    files = set()
    for folder in ("Contracts", "Gameplay", "AssetImport", "Storage"):
        files.update(ROOT.glob("Assets/Scripts/" + folder + "/*.cs"))
    props = ROOT / "tools/source-player/ProductionSources.props"
    for item in ET.parse(props).iter("Compile"):
        name = item.attrib["Include"].replace("$(BlitzSourceRoot)", "")
        files.update(ROOT.glob(name))
    files.update((ROOT / "tools/source-player/Runtime").glob("*.cs"))
    for name in (
        "tools/source-player/SourcePlayer.Core.csproj",
        "tools/source-player/SourcePlayer.AnimationOpcodeChecks.csproj",
        "tools/source-player/AnimationOpcodeChecks.cs",
        "tools/source-player/Directory.Build.props",
        "tools/source-player/ProductionSources.props",
        "tools/source-player/dotnet.sh",
        "tools/animation/tests/check_remaining_opcodes.py",
        "tests/contact/source.py",
    ):
        files.add(ROOT / name)
    pins = {}
    with zipfile.ZipFile(output / "captured-source.zip", "x", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(files):
            name, raw = path.relative_to(ROOT).as_posix(), path.read_bytes()
            pins[name] = sha(raw); archive.writestr(name, raw)
    require(all(sha((ROOT / name).read_bytes()) == pin for name, pin in pins.items()),
            "Source changed during capture; no mixed epoch accepted.")
    (output / "captured-source-pins.json").write_text(json.dumps(pins, indent=2))
    scratch = ROOT / ".squad/.scratch/a1-opcode-posix-20261111"
    scratch.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, SOURCE_PLAYER_DOTNET=str(dotnet), SOURCE_PLAYER_DATA_ROOT=str(data_root),
               SOURCE_PLAYER_POSIX_SCRATCH=str(scratch), A1_CAPTURE=str((output / "captured-source.zip").resolve()),
               A1_ORIGINAL=str(original_root), A1_DIAGNOSTIC="1" if diagnostic else "0",
               A1_OLD_CAPTURE=str(controls.resolve()) if controls else "",
               A1_LEFT_RELEASE="1" if left_release else "0",
               A1_SUITE=suite, A1_SOURCE_CONTROLS="1" if source_controls else "0")
    archive_path = output / "outputs.tgz"
    ack_read, ack_write = os.pipe()
    ready_read, ready_write = os.pipe()
    with archive_path.open("xb") as exported:
        env["A1_EXPORT_FD"] = str(exported.fileno())
        command = ["bash", "tools/source-player/with-posix-build.sh", "python3", "-B", str(Path(__file__)), "--inside"]
        env.update(A1_ACK_FD=str(ack_read), A1_READY_FD=str(ready_write))
        child = subprocess.Popen(command, cwd=ROOT, env=env,
            pass_fds=(exported.fileno(), ack_read, ready_write),
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        os.close(ack_read); os.close(ready_write)
        try:
            require(select.select([ready_read], [], [], 570)[0] and os.read(ready_read, 1) == b"1",
                    "The bounded test did not export its outputs.")
            verify = """import hashlib,json,sys,tarfile
from pathlib import Path
p=Path(sys.argv[1])
with tarfile.open(p,'r:gz') as t:
 members={m.name:hashlib.sha256(t.extractfile(m).read()).hexdigest() for m in t if m.isfile()}
print(json.dumps(members))
"""
            verified = subprocess.run([sys.executable, "-B", "-c", verify, str(archive_path.resolve())],
                                      capture_output=True, text=True)
            os.write(ack_write, b"1" if verified.returncode == 0 else b"0")
            stdout, _ = child.communicate(timeout=30)
            require(verified.returncode == 0, "Fresh-host archive readback failed: " + verified.stderr)
        finally:
            os.close(ack_write); os.close(ready_read)
            if child.poll() is None:
                child.terminate(); child.wait(timeout=30)
    (output / "wrapper.log").write_text(stdout)
    members = []
    with tarfile.open(archive_path, "r:gz") as archive:
        for member in archive:
            if member.isfile():
                raw = archive.extractfile(member).read()
                members.append({"path": member.name, "bytes": len(raw), "sha256": sha(raw)})
    receipt = {
        "schema": "finite-a1-opcode-capture-20261111/v1", "exit": child.returncode, "command": command,
        "sourceZipSha256": sha((output / "captured-source.zip").read_bytes()),
        "capturedInputs": len(pins), "outputArchiveSha256": sha(archive_path.read_bytes()), "members": members,
        "diagnosticOnly": diagnostic, "suite": suite, "readbackBeforeNamespaceExit": True,
    }
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2))
    print(json.dumps({key: value for key, value in receipt.items() if key != "members"}))
    print(stdout[-10000:])
    return child.returncode


def inside():
    scratch = Path(os.environ["SOURCE_PLAYER_POSIX_SCRATCH"])
    outputs = scratch / "outputs"
    outputs.mkdir()
    status = 1
    try:
        with zipfile.ZipFile(os.environ["A1_CAPTURE"]) as archive:
            source_bytes = {name: archive.read(name) for name in archive.namelist()}
        suite = os.environ.get("A1_SUITE", "opcodes")
        state_path = "Assets/Scripts/Gameplay/SourceAnimationState.cs"
        variants = [("baseline", {})]
        if os.environ["A1_OLD_CAPTURE"]:
            with zipfile.ZipFile(os.environ["A1_OLD_CAPTURE"]) as archive:
                old = archive.read(state_path)
            require(sha(old) == "b9a0a71656f3bcf02c3a042ba37e1a8d378b1b8e69527a16e05688537b6967e5",
                    "The entire immutable old b9 state, not a hand-built approximation, is required.")
            actual = source_bytes[state_path]
            writer = b"if (returned == 0) Mode |= SourceAnimationModes.Zero;\n            else Mode &= ~SourceAnimationModes.Zero;"
            require(actual.count(writer) == 1, "One exact real known-return writer required.")
            variants.extend([
                ("entire-old-b9-control", {state_path: old}),
                ("remove-real-mode-writer-control", {state_path: actual.replace(writer, b"", 1)}),
                ("forced-fresh-final", {}),
            ])
        if os.environ.get("A1_SOURCE_CONTROLS") == "1":
            require(not os.environ["A1_OLD_CAPTURE"], "Use either portable source controls or the old capture control.")
            if suite == "left-releases":
                path = "Assets/Scripts/Gameplay/SourceAnimationPrograms.OriginalSound.cs"
                old = source_bytes[path].decode()
                for script, set_line, clear_line in (
                    ("qb_rthrow_sl_anim", 587, 593), ("qb_throw_sl_anim", 728, 734), ("qb_throw_ll_anim", 787, 793)):
                    line = f'            OriginalFlippedRelease(programs, "{script}", {set_line}, {clear_line});\n'
                    require(old.count(line) == 1, "Exact original three-programme bracket call required.")
                    old = old.replace(line, "", 1)
                start = old.index("        private static void OriginalFlippedRelease(")
                end = old.index("        private static void OriginalInsertedSound(", start)
                old = (old[:start] + old[end:]).encode()
                require(sha(old) == "6b83e2e4ea2269fd9ed5d8c7c8a1b50126436709c18fee443b4e7afe0f9aef7d",
                        "The portable control must reconstruct the entire original6b83 programme file.")
                variants.extend([("entire-old-programme-control", {path: old}), ("forced-fresh-final", {})])
            elif suite == "camera-cache":
                path = "Assets/Scripts/Gameplay/SourceCatchGeometry.cs"
                text = source_bytes[path].decode()
                start = text.index("            SourceAnimationLibrary original = library as SourceAnimationLibrary;")
                end = text.index("            SourceEvaluatedPose pose =", start)
                old = (text[:start] + text[end:]).encode()
                require(sha(old) == "b2aa397cbc42d4960c792e91af99a133f055112f96050610cbc3ad67bff76959",
                        "The portable cache control must reconstruct the entire originalb2 geometry file.")
                game = "Assets/Scripts/Gameplay/GameSimulation.Source.cs"
                caller = source_bytes[game]
                marker = b"wrist = ContactCachedWorldJoint(quarterback, wristJoint);"
                require(caller.count(marker) == 1, "Exact actual cached release consumer required.")
                variants.extend([
                    ("old-cache-producer-only", {path: old}),
                    ("fresh-release-caller-only", {game: caller.replace(marker, b"wrist = SourceWorldJoint(quarterback, wristJoint);", 1)}),
                    ("forced-fresh-final", {})])
            else:
                raise ValueError("Portable source controls are for left-releases or camera-cache.")
        empty_feed = scratch / "empty-feed"; empty_feed.mkdir()
        sdk = os.environ["SOURCE_PLAYER_DOTNET"]
        summaries = []
        for label, replacements in variants:
            captured = scratch / ("source-" + label); captured.mkdir()
            stage = outputs / label if len(variants) > 1 else outputs
            stage.mkdir(exist_ok=True)
            inputs = dict(source_bytes, **replacements)
            input_pins = {name: sha(raw) for name, raw in inputs.items()}
            for name, raw in inputs.items():
                path = captured / name
                path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(raw)
            writable = {captured / "tools/source-player" / name for name in ("bin", "obj")}
            for path in writable:
                path.mkdir()
            for path in captured.rglob("*"):
                path.chmod(0o700 if path in writable else 0o500 if path.is_dir() else 0o400)
            captured.chmod(0o500)
            project = captured / "tools/source-player/SourcePlayer.AnimationOpcodeChecks.csproj"
            commands = [
                [sdk, "restore", str(project), "--source", str(empty_feed), "-v:q", "-p:UseSharedCompilation=false"],
                [sdk, "build", str(project), "--no-restore", "-v:q", "-p:UseSharedCompilation=false",
                 "-p:PathMap=" + str(captured) + "=/source"],
                [sdk, str(captured / "tools/source-player/bin/SourcePlayer.AnimationOpcodeChecks/Debug/net10.0/SourcePlayer.AnimationOpcodeChecks.dll"),
                 os.environ["SOURCE_PLAYER_DATA_ROOT"], str(stage / "observations.json")]
                + (["--" + suite] if suite != "opcodes" else
                   ["--left-release-diagnostic"] if os.environ["A1_LEFT_RELEASE"] == "1" else []),
                [sys.executable, "-B", str(captured / "tools/animation/tests/check_remaining_opcodes.py"),
                 "--source", os.environ["A1_ORIGINAL"], "--observations", str(stage / "observations.json"),
                 "--output", str(stage / "native"), "--suite", suite]
                + (["--diagnostic"] if os.environ["A1_DIAGNOSTIC"] == "1" else []),
            ]
            if suite == "raw-throw-selectors": commands.pop()
            results = []
            for index, command in enumerate(commands):
                result = subprocess.run(command, cwd=captured, capture_output=True, text=True)
                (stage / (str(index) + ".log")).write_text(result.stdout + result.stderr)
                results.append({"command": command, "exit": result.returncode})
                (stage / "commands.json").write_text(json.dumps(results, indent=2))
                print("VARIANT", label, "STEP", index, "EXIT", result.returncode,
                      (result.stdout + result.stderr)[-2800:])
                if result.returncode:
                    break
            current = {name: sha((captured / name).read_bytes()) for name in input_pins}
            require(current == input_pins, "Captured compile inputs changed: " + label)
            (stage / "source-before-after.json").write_text(json.dumps({
                "before": input_pins, "after": current, "stable": True,
                "explicitReplacements": {name: sha(raw) for name, raw in replacements.items()}}, indent=2))
            assemblies = {}
            for name in ("SourcePlayer.Core", "SourcePlayer.AnimationOpcodeChecks"):
                path = captured / ("tools/source-player/bin/" + name + "/Debug/net10.0/" + name + ".dll")
                if path.exists():
                    destination = stage / "assemblies" / path.name
                    destination.parent.mkdir(exist_ok=True)
                    raw = path.read_bytes(); destination.write_bytes(raw); assemblies[path.name] = sha(raw)
            native = json.loads((stage / "native/result.json").read_bytes()) if (stage / "native/result.json").exists() else None
            summaries.append({"name": label, "commands": results, "native": native, "assemblies": assemblies})
            if label == "baseline" and results[-1]["exit"] != 0:
                raise ValueError("The baseline must pass before any source control is run.")
        if len(variants) == 1:
            status = summaries[0]["commands"][-1]["exit"]
        else:
            require(all(len(s["commands"]) == 4 and all(c["exit"] == 0 for c in s["commands"][:3])
                        and s["native"] is not None for s in summaries), "Every variant must compile cleanly and execute the real VM and original C.")
            require(not summaries[0]["native"]["failures"] and not summaries[-1]["native"]["failures"],
                    "Baseline and forced-fresh final must pass.")
            if suite == "opcodes":
                require(summaries[1]["native"]["failures"], "Entire-old source control was not rejected.")
                consumer_fields = {f["field"] for f in summaries[2]["native"]["failures"]
                                   if f["case"] == "code-bz-live-blocks-csi-production"}
                require({"nativeConditionalBlocksCameraConsumer", "nativeConditionalKeepsActualPriorCamera", "randomCalls"}
                        <= consumer_fields, "The real mode-writer mutation must reach the actual wrong branch, CSI/render and RNG consumer.")
            elif suite == "left-releases":
                require(summaries[1]["native"]["failures"], "The reconstructed whole original programme was not rejected.")
                observed = [json.loads((outputs / value["name"] / "observations.json").read_bytes()) for value in summaries]
                for actual, control in zip(observed[0]["cases"], observed[1]["cases"]):
                    if not actual["left"]:
                        require(actual == control, "A right-side control changed under the three-left-only bracket revision.")
                    else:
                        require(actual["ReleaseTick"] == control["ReleaseTick"] and
                                pose_bits(actual["BeforeRelease"]["pose"]) == pose_bits(control["BeforeRelease"]["pose"]) and
                                actual["BeforeRelease"]["randomDraws"] == control["BeforeRelease"]["randomDraws"] and
                                actual["AfterRelease"]["randomDraws"] == control["AfterRelease"]["randomDraws"],
                                "The left bracket changed actual timing, pose or RNG counts.")
            elif suite == "camera-cache":
                producer, caller = (value["native"]["failures"] for value in summaries[1:3])
                require(any(f["kind"] == "joints" for f in producer) and
                        not any(f["kind"] == "camera" for f in producer), "Cache producer cause was not isolated.")
                require(any(f["kind"] == "native-release" for f in caller) and
                        not any(f["kind"] in ("camera", "joints") for f in caller), "Release caller cause was not isolated.")
                observed = [json.loads((outputs / value["name"] / "observations.json").read_bytes()) for value in summaries]
                for other in observed[1:]:
                    for actual, control in zip(observed[0]["cases"], other["cases"]):
                        a, b = actual["BeforeRelease"], control["BeforeRelease"]
                        require(a["tick"] == b["tick"] and a["previousCache"]["tick"] == b["previousCache"]["tick"] and
                                a["currentCamera"]["cameraTick"] == b["currentCamera"]["cameraTick"] and
                                pose_bits(a["currentPose"]) == pose_bits(b["currentPose"]) and
                                a["randomDraws"] == b["randomDraws"] and
                                actual["AfterRelease"]["randomDraws"] == control["AfterRelease"]["randomDraws"],
                                "Cache or caller cause changed actual timing, pose or RNG counts.")
            require(summaries[0]["assemblies"] == summaries[-1]["assemblies"],
                    "Forced-fresh identical source must produce identical actual Core/test DLLs.")
            status = 0
        (outputs / "campaign.json").write_text(json.dumps({
            "schema": "actual-animation-opcode-causal-campaign-20261111/v1", "exit": status,
            "variants": summaries}, indent=2))
    except Exception as error:
        (outputs / "failure.txt").write_text(repr(error))
        print("FAILED", repr(error)); status = 1
    finally:
        with os.fdopen(int(os.environ["A1_EXPORT_FD"]), "wb", closefd=False) as handle:
            with tarfile.open(fileobj=handle, mode="w:gz") as archive:
                for path in sorted(outputs.rglob("*")):
                    if path.is_file():
                        archive.add(path, arcname=path.relative_to(outputs).as_posix(), recursive=False)
            handle.flush()
        os.write(int(os.environ["A1_READY_FD"]), b"1")
        require(os.read(int(os.environ["A1_ACK_FD"]), 1) == b"1", "Host readback rejected the archive before namespace exit.")
    return status


def compare(original_root, observations, output, diagnostic=False):
    output.mkdir()
    raw = {name: (original_root / name).read_bytes() for name in PINS}
    for name, pin in PINS.items():
        require(source.blob(raw[name]) == pin, "Original Git blob changed: " + name)
    data = json.loads(observations.read_bytes())
    require(data["schema"] == "actual-original-animation-opcode-observations/v1", "Actual opcode observations required.")
    native_scripts = source.scripts(raw["plyrseq.c"])
    header = raw["include/ani3d.h"].decode("latin1")
    definitions = {}
    for path in ("include/ani3d.h", "include/player.h"):
        for name, value in re.findall(rb"(?m)^\s*#define\s+(\w+)\s+([^\r\n]+)", raw[path]):
            name, value = name.decode(), value.decode().split("//", 1)[0].strip()
            if name.startswith(("MODE_", "AA_", "AH_", "PF_", "RF_", "GM_", "ATTMODE_", "JOINT_")):
                definitions[name] = value
    declarations = [
        "#include <stdint.h>\n#include <stdio.h>\n#include <stdlib.h>\n#include <string.h>\n#include <math.h>",
        "#define TRUE 1\n#define FALSE 0\n#define ANI_DEBUG 0\n#define NUM_PLAYERS 14\n#define GM_IN_PLAY 2",
        "#define PI_RAD 3.1415926f\n#define HALFPI (PI_RAD*.5f)\n#define EPSILON .00001f\n#define GRAVITY -.08f",
        "#define X 0\n#define Y 1\n#define Z 2\n#define W 3",
        "typedef void *Texture_node_t;",
    ]
    for tag in ("anim_header", "_anidata", "_obj_3d"):
        match = re.search(r"typedef struct " + tag + r"\s*\{.*?\}\s*\w+;", header, re.S)
        require(match is not None, "Original native structure required: " + tag)
        declarations.append(match[0])
    camera_type = re.search(r"typedef struct _CAMSEQINFO\s*\{.*?\}\s*CAMSEQINFO;", header, re.S)
    camera_modes = next(m[0] for m in re.finditer(r"enum\s*\{.*?\};", header, re.S) if "CAM_MODECNT" in m[0])
    require(camera_type is not None, "Original CSI structure required.")
    declarations.extend([camera_type[0], camera_modes])
    declarations.extend([
        "typedef union {int i;float f;} intfloat_t;",
        "typedef struct fbplyr_data {obj_3d odata;struct fbplyr_data *puppet_link;"
        "int plyrnum,throw_target,team,attack_mode,attack_time,acj1,acj2;float acx1,acx2,acr;"
        "float jpos[15][3];} fbplyr_data;",
        "static fbplyr_data player_blocks[14]; static int p2p_angles[14][14];",
        "static struct {int game_mode,ball_carrier,off_side,team_fire[2];} game_info;",
        "static struct {float tx,tz;int int_receiver;} ball_obj;",
        "static struct {CAMSEQINFO csi;int tick_cnt;void *pmodeobj;float x,y,z,xform[12];} cambot;",
        "int shaker_amp,shaker_tot,shaker_cnt;",
        "static int raw_random,random_calls,standard_calls;",
        "static int randrng(int n){random_calls++;return raw_random%n;}",
        "static int fixture_c_rand(void){standard_calls++;return raw_random;}\n#define rand fixture_c_rand",
        "static float fsqrt(float x){return sqrtf(x);}",
        "static void lockup(void){abort();} static void sleep(int n){(void)n;abort();}",
        "static void set_puppet_pos(obj_3d *a,obj_3d *b){(void)a;(void)b;abort();}",
        "static float lookup[256];static char *anim_blocks[1],*seq_names[4096];",
        "#define GETWORD(x) ((int32_t)((uint32_t)(uint8_t)pani->pscript[(x)]"
        "+((uint32_t)(uint8_t)pani->pscript[(x)+1]<<8)"
        "+((uint32_t)(uint8_t)pani->pscript[(x)+2]<<16)"
        "+((uint32_t)(uint8_t)pani->pscript[(x)+3]<<24)))",
        "static void w32(char *p,uintptr_t v){if(v>UINT32_MAX)abort();uint32_t n=(uint32_t)v;memcpy(p,&n,4);}",
        "static void wf(char *p,float v){memcpy(p,&v,4);}",
        "static uint32_t bits(float v){uint32_t n;memcpy(&n,&v,4);return n;}",
        "static char branch_target[1];",
    ])
    declarations.extend("#define " + key + " " + value for key, value in definitions.items())
    declarations.append("static float trigs[257]={" + ",".join(map(floating, data["quarterSine"])) + "};")
    for macro in ("FRELX", "FRELZ"):
        match = re.search(rb"(?m)^#define\s*" + macro.encode() + rb"\([^\r\n]+", raw["lib/ani3d/ANIM.C"])
        require(match is not None, "Original relative-velocity macro required.")
        declarations.append(match[0].decode("ascii"))
    names = {
        "lib/ani3d/ANIM.C": (
            "_next_frame", "_ani_end", "_ani_setflag", "_ani_clrflag", "_ani_setvels", "_ani_setyvel",
            "_ani_set_face_rel_vel", "_ani_setfriction", "_ani_prep_xition", "_ani_attack_on",
            "_ani_attack_off", "_ani_turnhack", "_ani_runtoframe", "_ani_code", "_ani_bz",
            "_ani_bnz", "_ani_code_bz", "_ani_code_bnz", "_ani_leapat_tgtx",
            "_ani_setcambot", "select_csi_item", "_ani_shaker", "_ani_leapatt", "ptangle", "_mid_angle",
        ),
        "lib/ani3d/pipe3D.c": ("isin", "icos", "iasin", "iacos"),
        "player.c": ("face_throw_target", "plyr_have_ball", "plyr_rand", "rand_func"),
    }
    bodies, body_pins = [], {}
    for path, functions in names.items():
        for name in functions:
            _, _, body = source.function(raw[path], name)
            bodies.append(body.decode("latin1"))
            body_pins[name] = {"path": path, "sha256": sha(body)}
    for path in ("player.c", "pmisc.c"):
        try:
            _, _, body = source.function(raw[path], "bearing")
        except ValueError:
            continue
        bodies.append(body.decode("latin1")); body_pins["bearing"] = {"path": path, "sha256": sha(body)}
        break
    require("bearing" in body_pins, "Unchanged original bearing callback dependency required.")
    pointer = re.search(rb"(?m)^CAMSEQINFO\s*\*\s*select_csi_ptr\s*\([^;{}]*\)\s*\{", raw["lib/ani3d/ANIM.C"])
    require(pointer is not None, "Original CSI selector body required.")
    end = source.closing(raw["lib/ani3d/ANIM.C"], pointer.end() - 1)
    body = raw["lib/ani3d/ANIM.C"][pointer.start():end]
    bodies.append(body.decode("latin1"))
    body_pins["select_csi_ptr"] = {"path": "lib/ani3d/ANIM.C", "sha256": sha(body)}
    for text in bodies:
        declarations.append(text[:text.index("{")].strip() + ";")
    declarations.extend(bodies)
    # Selection/copy is angle-agnostic. Keep the actual authored degree scalars at
    # this table boundary; the real Host independently consumes its admitted CSI
    # conversion. This is not a DG2RD/native camera-angle comparison.
    table = re.search(rb"CAMSEQINFO csi_zm_divetack\[\]\s*=\s*\{.*?\r?\n\{-1\}\};", raw["CamData.c"], re.S)
    require(table is not None, "Exact original dive camera table required.")
    declarations.extend(["#define DG2RD(x) (x)", table[0].decode("latin1")])
    clip_map = {clip["name"]: clip for clip in data["clips"]}
    for n, clip in enumerate(data["clips"]):
        declarations.append("static float roots_" + str(n) + "[]={" +
                            ",".join(floating(x) for frame in clip["frames"] for x in frame["root"]) + "};")
        declarations.append("static float quats_" + str(n) + "[]={" +
                            ",".join(floating(x) for frame in clip["frames"] for q in frame["pose"] for x in q) + "};")
        clip["index"] = n
    declarations.append(r'''
static void row(int n,fbplyr_data *f,anidata *a,char *operand){
 obj_3d *o=&f->odata;
 printf("{\"case\":%d,\"mode\":%u,\"ordinal\":%d,\"hold\":%d,\"vcount\":%d,\"vframe\":%d,\"turn\":%d,",
 n,a->animode,a->iframe,a->anicnt,a->vcount,a->vframe,a->hackval);
 printf("\"flags\":%d,\"facing\":%d,\"targetFacing\":%d,\"upperFacing\":%d,\"targetUpperFacing\":%d,",
 o->flags,o->fwd,o->tgt_fwd,o->fwd2,o->tgt_fwd2);
 printf("\"attackMode\":%d,\"attackTime\":%d,\"attackJointA\":%d,\"attackJointB\":%d,",
 f->attack_mode,f->attack_time,f->acj1,f->acj2);
 printf("\"attackRadii\":[%u,%u,%u],\"root\":[%u,%u,%u],\"position\":[%u,%u,%u],",
 bits(f->acx1),bits(f->acx2),bits(f->acr),bits(o->ax),bits(o->ay),bits(o->az),bits(o->x),bits(o->y),bits(o->z));
 printf("\"velocity\":[%u,%u,%u],\"friction\":%u,\"branched\":%s,\"repeat\":%s,\"randomCalls\":%d,\"standardCalls\":%d,\"pose\":[",
 bits(o->vx),bits(o->vy),bits(o->vz),bits(o->friction),a->pscript==branch_target?"true":"false",
 a->pscript==operand-1?"true":"false",random_calls,standard_calls);
 for(int i=0;i<60;i++)printf("%s%u",i?",":"",bits(a->pq[i]));
 printf("],\"cameraMode\":%d,\"cameraTarget\":%s,\"cameraResetTick\":%d,\"shakerAmplitude\":%d,\"shakerTotal\":%d,\"shakerElapsed\":%d}\n",
 cambot.csi.mode,cambot.pmodeobj==o?"true":"false",cambot.tick_cnt,shaker_amp,shaker_tot,shaker_cnt);
}
int main(void){
''')
    opcode_sites = []
    for n, case in enumerate(data["cases"]):
        native = case["native"]
        rows = native_scripts[case["script"]]["rows"]
        matching = [i for i, row in enumerate(rows) if row[1] == "byte" and row[2].split(",")[0] == native]
        # A lowered flag instruction can implement either SETFLAG or CLRFLAG.
        occurrence = case["occurrence"] if native not in ("ANI_SETFLAG", "ANI_CLRFLAG") else 0
        if case["sourceLine"]:
            previous = [i for i in matching if rows[i][0] <= case["sourceLine"] <= rows[i][0] + 1]
            if previous:
                matching = previous; occurrence = 0
        require(len(matching) > occurrence, "Exact active native opcode site required: " + case["name"])
        start = matching[occurrence]
        operands = []
        initial = rows[start][2].split(",")[1:]
        operands.extend(("byte", item.strip()) for item in initial)
        for _, kind, text in rows[start + 1:]:
            if kind == "label" or kind == "byte" and text.split(",")[0].startswith("ANI_"):
                break
            operands.extend((kind, item.strip()) for item in text.split(","))
            if native == "ANI_RUNTOFRAME":
                break
        if native in ("ANI_ATTACK_OFF", "ANI_END"):
            operands = []
        elif native in ("ANI_CODE_BNZ", "ANI_CODE_BZ"):
            require(len(operands) == 5, "Native branch must retain callback/three args/target.")
        opcode_sites.append({"name": case["name"], "native": native, "script": case["script"],
                             "line": rows[start][0], "operands": operands})
        b, inp = case["before"], case["input"]
        clip = clip_map[case["clip"]]; ci = clip["index"]
        # Fixture records use decoded float frame storage, not shipped compressed SEQ memory.
        declarations.append("{fbplyr_data f={0};obj_3d *o=&f.odata;anidata *a=o->adata;char operand[128]={0};"
                            "memset(player_blocks,0,sizeof(player_blocks));memset(&cambot,0,sizeof(cambot));cambot.tick_cnt=99;"
                            "shaker_amp=shaker_tot=0;shaker_cnt=0;"
                            "anihdr h={0};h.nframes=" + str(len(clip["frames"])) + ";h.nlimbs=15;a->pahdr=&h;"
                            f"a->pxlate=roots_{ci};a->pframe=(char*)(quats_{ci}+60);"
                            f"a->pq=quats_{ci}+{b['ordinal']*60};a->pscript=operand;"
                            f"o->ascale={floating(case['scale'])};")
        scalar = {
            "f.plyrnum": inp["actor"], "f.throw_target": inp["pendingTarget"],
            "game_info.game_mode": inp["gameMode"], "game_info.ball_carrier": inp["carrier"],
            "o->flags": b["flags"], "o->fwd": b["facing"], "o->tgt_fwd": b["targetFacing"],
            "o->fwd2": b["upperFacing"], "o->tgt_fwd2": b["targetUpperFacing"],
            "a->iframe": b["ordinal"], "a->anicnt": b["hold"], "a->animode": b["mode"],
            "a->vcount": b["vcount"], "a->vframe": b["vframe"], "a->hackval": b["turn"],
            "f.attack_mode": b["attackMode"], "f.attack_time": b["attackTime"],
            "f.acj1": b["attackJointA"], "f.acj2": b["attackJointB"],
            "raw_random": case["randomInput"], "random_calls": 0, "standard_calls": 0,
        }
        declarations.extend(key + "=" + str(value) + ";" for key, value in scalar.items())
        arrays = {
            ("o->x", "o->y", "o->z"): b["position"],
            ("o->ax", "o->ay", "o->az"): b["root"],
            ("o->vx", "o->vy", "o->vz"): b["velocity"],
            ("f.acx1", "f.acx2", "f.acr"): b["attackRadii"],
        }
        for keys, values in arrays.items():
            declarations.extend(key + "=" + floating(value) + ";" for key, value in zip(keys, values))
        declarations.append("o->friction=" + floating(b["friction"]) + ";")
        declarations.append("o->pobj_hit=&player_blocks[8].odata;player_blocks[8].odata.fwd=" + str(inp["hitFacing"]) + ";")
        declarations.append("p2p_angles[3][4]=137;player_blocks[3].odata.fwd=o->fwd;")
        declarations.append("ball_obj.tx=o->x+" + floating(inp["ballDelta"][0]) +
                            ";ball_obj.tz=o->z+" + floating(inp["ballDelta"][1]) + ";")
        if "shakerElapsed" in inp:
            declarations.append("shaker_cnt=" + str(inp["shakerElapsed"]) +
                                ";cambot.csi.mode=" + str(inp["beforeCameraMode"]) + ";")
        if native == "ANI_LEAPATT":
            leap = inp["leap"]
            require(leap["cacheTick"] == case["consumer"]["sourceTick"] - 1 and
                    leap["cameraTick"] == case["consumer"]["sourceTick"], "Actual preceding camera cache required.")
            for index, value in enumerate(leap["transform"]):
                declarations.append(f"cambot.xform[{index}]={floating(value)};")
            for axis, value in zip("xyz", leap["position"]):
                declarations.append("cambot." + axis + "=" + floating(value) + ";")
            for index, point in enumerate(leap["joints"]):
                for axis, value in enumerate(point):
                    declarations.append(f"player_blocks[{leap['target']}].jpos[{index}][{axis}]={floating(value)};")
            for axis, value in zip(("vx", "vy", "vz"), leap["velocity"]):
                declarations.append(f"player_blocks[{leap['target']}].odata.{axis}={floating(value)};")
        offset = 0
        for kind, token in operands:
            if kind == "byte":
                declarations.append(f"operand[{offset}]=(char)({token});"); offset += 1
            elif kind == "float":
                declarations.append(f"wf(operand+{offset},{floating(float(token))});"); offset += 4
            else:
                value = "(uintptr_t)&" + token if token in ("face_throw_target", "plyr_have_ball", "plyr_rand", "rand_func", "csi_zm_divetack") else \
                    "(uintptr_t)branch_target" if native in ("ANI_CODE_BNZ", "ANI_CODE_BZ") and offset == 16 else \
                    "(uint32_t)(" + token + ")"
                declarations.append(f"w32(operand+{offset},{value});"); offset += 4
        function = "_" + native.lower()
        require(function in body_pins, "Native body not yet qualified: " + native)
        declarations.append(f"{function}(o,a);row({n},&f,a,operand);}}")
    declarations.append("return 0;}")
    c_file = output / "original_opcodes.c"
    c_file.write_text("\n".join(declarations))
    command = ["gcc", "-std=c11", "-O0", "-ffp-contract=off", "-fno-fast-math",
               "-fno-strict-aliasing", "-fsigned-char", "-no-pie", "-Wno-int-to-pointer-cast",
               str(c_file), "-lm", "-o", str(output / "original_opcodes")]
    compiled = subprocess.run(command, capture_output=True, text=True)
    (output / "compile.log").write_text(compiled.stdout + compiled.stderr)
    require(compiled.returncode == 0, "Original C compile failed: " + compiled.stderr[-6000:])
    execution = subprocess.run([str(output / "original_opcodes")], capture_output=True, text=True, timeout=30)
    (output / "oracle.jsonl").write_text(execution.stdout)
    require(execution.returncode == 0, "Original C execution failed: " + execution.stderr[-2000:])
    failures, assertions = [], 0
    for line in execution.stdout.splitlines():
        expected = json.loads(line); case = data["cases"][expected["case"]]; observed = case["after"]
        def check(field, a, b):
            nonlocal assertions
            assertions += 1
            if a != b:
                failures.append({"case": case["name"], "field": field, "original": a, "production": b})
        check("error", None, case["error"])
        for field in ("mode", "ordinal", "hold", "vcount", "vframe", "turn", "flags", "facing",
                      "targetFacing", "upperFacing", "targetUpperFacing", "attackMode", "attackTime",
                      "attackJointA", "attackJointB"):
            check(field, expected[field], observed[field])
        for field in ("attackRadii", "root", "position", "velocity"):
            for axis, (a, b) in enumerate(zip(expected[field], observed[field])):
                check(field + str(axis), a, bits(b))
        check("friction", expected["friction"], bits(observed["friction"]))
        for index, (a, b) in enumerate(zip(expected["pose"], (x for q in observed["pose"] for x in q))):
            check("pose" + str(index), a, bits(b))
        check("randomCalls", expected["randomCalls"], sum(d["service"] == "randrng" for d in case["draws"]))
        check("standardCalls", expected["standardCalls"], sum(d["service"] == "C-rand-modulo" for d in case["draws"]))
        if case["native"] in ("ANI_CODE_BNZ", "ANI_CODE_BZ"):
            check("branchTaken", expected["branched"],
                  observed["pc"] != case["before"]["pc"] or observed["script"] != case["before"]["script"])
        if case["native"] == "ANI_RUNTOFRAME":
            check("repeat", expected["repeat"], observed["pc"] == case["before"]["pc"] - 1)
        if case["native"] in ("ANI_CODE", "ANI_CODE_BNZ", "ANI_CODE_BZ"):
            check("knownIntEvidence", "KnownInt", observed["codeEvidence"])
        if "consumer" in case:
            consumer = case["consumer"]
            check("sameWakeRender", consumer["sourceTick"], consumer["renderTick"])
            check("realPoseMatrixExtent", 14 * 15 * 16, consumer["matrixValues"])
            if case["native"] == "ANI_SETCAMBOT":
                check("actualCameraMode", expected["cameraMode"], consumer["cameraMode"])
                check("actualCameraTarget", expected["cameraTarget"], consumer["cameraTarget"] == 3)
                check("actualCameraWake", consumer["sourceTick"], consumer["cameraTick"])
                check("nativeCameraReset", 0, expected["cameraResetTick"])
            if case["native"] == "ANI_CODE_BZ":
                check("nativeConditionalBlocksCameraConsumer", None, consumer["cameraCue"])
                check("nativeConditionalKeepsActualPriorCamera", expected["cameraMode"], consumer["cameraMode"])
            if case["native"] == "ANI_SHAKER":
                check("actualShakerAmplitude", expected["shakerAmplitude"], consumer["shakerAmplitude"])
                check("actualShakerTotal", expected["shakerTotal"], consumer["shakerTotal"])
                check("actualOpcodeShakerCount", expected["shakerElapsed"], consumer["shakerElapsed"])
            if case["native"] == "ANI_LEAPATT":
                check("actualNineWordConsumption", 36, consumer["nativeLeapBytes"])
                check("actualPreviousJointCache", consumer["sourceTick"] - 1, consumer["nativeLeapCache"])
    covered = {case["native"] for case in data["cases"]}
    if not diagnostic:
        require(covered == CLASSES, "Finite19 class closure incomplete: " + repr(sorted(CLASSES - covered)))
    result = {
        "schema": "unchanged-original-animation-opcode-comparison-20261111/v1",
        "status": "diagnostic" if diagnostic else "passed" if not failures else "failed",
        "cases": len(data["cases"]), "classes": sorted(covered), "assertions": assertions,
        "failures": failures, "sourcePins": PINS, "unchangedBodyPins": body_pins,
        "compilerCommand": command, "dataManifestSha256": data["manifestSha256"],
        "opcodeSites": opcode_sites,
        "policyChecks": data.get("policyChecks", []),
        "boundaries": [
            "Actual compiled VM instructions and actual bound Core callbacks; declared opcode-entry fixtures.",
            "Original functions are extracted byte-for-byte, not reimplemented expectation formulas.",
            "Genuine admitted selected WAN float roots/quaternions; decoded storage, not shipped numeric SEQ IDs.",
            "Explicit C-rand/randrng input-service fixtures, not target PRNG/binary/DSP/pixel identity.",
            "CSI selection/copy uses authentic authored table scalars as opaque payload; DG2RD/native camera-angle equivalence is not asserted.",
        ],
    }
    (output / "result.json").write_text(json.dumps(result, indent=2))
    return result


def pose_bits(pose):
    return [[bits(value) for value in rotation] for rotation in pose]

def compare_left_releases(original_root, observations, output, diagnostic=False):
    PINS = {
        "player.c": "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c",
        "plyrseq.c": "28cef0d1082f2b0c9241930fdc8886de4c8f5438",
        "lib/ani3d/ANIM.C": "97d7856d67a432937e249be274ef67a9ad354dc1",
        "lib/ani3d/pipe3D.c": "0966ecaf0d7eef7d9c635a97899a421ce1eeea41",
        "include/ani3d.h": "826c6322467fcfe503b8402b540e4731671f9b6b",
        "include/player.h": "a86b1a5b52ba842b206cd7dd0bbb26521db6dcb8",
        "include/GAME.H": "09d65c92971c32c44b1dc49fb4e4e98b371b1cd3",
    }
    raw = {name: (original_root / name).read_bytes() for name in PINS}
    for name, pin in PINS.items():
        require(source.blob(raw[name]) == pin, "Original Git blob changed: " + name)
    data = json.loads(observations.read_bytes())
    output.mkdir()
    require(data["schema"] == "actual-public-left-throw-release-observations-20261111/v1", "Actual public release observations required.")
    scripts = source.scripts(raw["plyrseq.c"])
    before_masks, clears, sites = [], [], []
    for case in data["cases"]:
        rows = scripts[case["script"]]["rows"]
        release = [i for i, row in enumerate(rows) if row[1] == "word" and row[2] == "qb_release_ball,0,0,0"]
        require(len(release) == 1, "One exact original release per programme required.")
        index = release[0]
        previous = [(i, row[2]) for i, row in enumerate(rows[:index])
                    if row[1] == "byte" and row[2] in ("ANI_SETFLAG", "ANI_CLRFLAG")]
        before_masks.append(previous[-1][1] if previous else None)
        following = rows[index + 1:index + 3]
        clear = len(following) == 2 and following[0][2] == "ANI_CLRFLAG" and following[1][2] == "PF_FLIP"
        clears.append(clear)
        require(not case["left"] or previous and previous[-1][1] == "ANI_SETFLAG" and clear,
                "The proposed three left brackets must be exactly source-backed.")
        sites.append({"script": case["script"], "releaseOperandLine": rows[index][0],
                      "beforeNativeOpcode": before_masks[-1], "afterNativeClear": clear})
    header = raw["include/ani3d.h"].decode("latin1")
    c = [
        "#include <stdint.h>\n#include <stdio.h>\n#include <stdlib.h>\n#include <string.h>\n#include <math.h>",
        "#define TRUE 1\n#define FALSE 0\n#define ANI_DEBUG 0\n#define LESSER(a,b) ((a)<(b)?(a):(b))",
        "typedef void *Texture_node_t;",
    ]
    for tag in ("anim_header", "_anidata", "_obj_3d"):
        match = re.search(r"typedef struct " + tag + r"\s*\{.*?\}\s*\w+;", header, re.S)
        require(match is not None, "Original animation structure required.")
        c.append(match[0])
    catch = re.search(rb"typedef struct _catch_t\s*\{.*?\}\s*catch_t;", raw["player.c"], re.S)
    require(catch is not None, "Original catch type required.")
    c.append(catch[0].decode("latin1"))
    wanted = {
        "BALL_VELOCITY", "BALL_YARDS", "BALL_YMAX", "TIME_MAX", "COS45", "TBALL_VELOCITY",
        "TBALL_YMAX", "TTIME_MAX", "FBALL_YARDS", "FBALL_VELOCITY", "FBALL_YMAX", "FTIME_MAX",
        "GRAVITY", "FIELDHB", "FIELDW", "FIELDHZ", "FOCFAC", "YD2UN", "FIELDX",
        "ISHUMAN", "CF_NO_UNINT", "PF_FLIP", "PF_THROWING", "BF_TURBO_PASS", "BF_FREEZE",
        "BF_SPINING", "MODE_UNINT", "MODE_TWOPART", "JOINT_LWRIST", "JOINT_RWRIST",
    }
    definitions = {}
    for path in ("include/ani3d.h", "include/player.h", "include/GAME.H", "player.c"):
        for line in raw[path].decode("latin1").splitlines():
            match = re.match(r"^\s*#define\s+(\w+)(.*)$", line)
            if match and match[1] in wanted:
                definitions[match[1]] = line.split("//", 1)[0].strip()
    require(wanted <= definitions.keys(), "Missing original scalar/macro authority: " + str(wanted - definitions.keys()))
    c.extend(definitions.values())
    c.extend([
        "typedef struct {obj_3d odata;int team,station;float jpos[15][3];} fbplyr_data;",
        "static struct {int off_side,los,team_fire[2];} game_info;",
        "static struct {obj_3d odata;int flags,int_receiver,flight_time,total_time;float ox,oy,oz,tx,ty,tz;} ball_obj;",
        "static struct {float x,y,z,xform[12];} cambot;",
        "static int pup_fastpass,did_catch;static char *last_catch;",
        "static catch_t *catch_front_tbl,*catch_behind_tbl,*catch_lside_tbl,*catch_rside_tbl;",
        "static float fsqrt(float x){return sqrtf(x);}static void lockup(void){abort();}",
        'static int randrng(int n){(void)n;fprintf(stderr,"Unqualified canned-catch branch\\n");abort();}',
        'static void drone_catch(int a,char*b,int c){(void)a;(void)b;(void)c;abort();}',
        "static float trigs[257]={" + ",".join(map(floating, data["quarterSine"])) + "};",
        "#define GETWORD(x) ((int32_t)((uint32_t)(uint8_t)pani->pscript[x]+"
        "((uint32_t)(uint8_t)pani->pscript[(x)+1]<<8)+((uint32_t)(uint8_t)pani->pscript[(x)+2]<<16)+"
        "((uint32_t)(uint8_t)pani->pscript[(x)+3]<<24)))",
        "static uint32_t bits(float v){uint32_t n;memcpy(&n,&v,4);return n;}",
    ])
    for macro in ("FRELX", "FRELZ"):
        match = re.search(rb"(?m)^#define\s*" + macro.encode() + rb"\([^\r\n]+", raw["lib/ani3d/ANIM.C"])
        c.append(match[0].decode("latin1"))
    bodies, body_pins = [], {}
    for path, names in (
        ("lib/ani3d/pipe3D.c", ("isin", "icos")),
        ("lib/ani3d/ANIM.C", ("_ani_setflag", "_ani_clrflag")),
        ("player.c", ("set_ball_vels",)),
    ):
        for name in names:
            _, _, body = source.function(raw[path], name)
            body_pins[name] = {"path": path, "sha256": sha(body)}
            bodies.append(body.decode("latin1"))
    for body in bodies:
        c.append(body[:body.index("{")].strip() + ";")
    c.extend(bodies)
    _, _, ball_proc = source.function(raw["player.c"], "ball_proc")
    begin = ball_proc.index(b"if ((!(ball_obj.flags & BF_FREEZE))")
    opening = ball_proc.index(b"{", begin)
    end = source.closing(ball_proc, opening)
    movement = ball_proc[begin:end]
    increment = b"ball_obj.flight_time += 1;"
    require(increment in ball_proc, "Original flight-time increment required.")
    body_pins["ball_proc/active-free-ball-motion"] = {"path": "player.c", "sha256": sha(movement)}
    c.append("static void advance_kernel(void){\n" + movement.decode("latin1") + "\n" + increment.decode() + "\n}")
    c.append(r'''
static void row(int n,int frame,int before,int after){
 printf("{\"case\":%d,\"frame\":%d,\"flagsBefore\":%d,\"flagsAfter\":%d,\"elapsed\":%d,\"total\":%d,",
 n,frame,before,after,ball_obj.flight_time,ball_obj.total_time);
 printf("\"origin\":[%u,%u,%u],\"position\":[%u,%u,%u],\"velocity\":[%u,%u,%u],\"target\":[%u,%u,%u]}\n",
 bits(ball_obj.ox),bits(ball_obj.oy),bits(ball_obj.oz),
 bits(ball_obj.odata.x),bits(ball_obj.odata.y),bits(ball_obj.odata.z),
 bits(ball_obj.odata.vx),bits(ball_obj.odata.vy),bits(ball_obj.odata.vz),
 bits(ball_obj.tx),bits(ball_obj.ty),bits(ball_obj.tz));
}
int main(void){
''')
    for index, case in enumerate(data["cases"]):
        release = case["BeforeRelease"]; target = release["receiver"]
        require(target["velocity"][0] == 0 and target["velocity"][2] == 0,
                "This finite release oracle admits stationary receivers only, never fake canned data.")
        c.append("{fbplyr_data thrower={0},receiver={0};obj_3d *o=&thrower.odata;char operand[4];"
                 "anidata *a=o->adata;memset(&ball_obj,0,sizeof(ball_obj));memset(&cambot,0,sizeof(cambot));"
                 ""
                 "thrower.station=0;receiver.station=-1;game_info.off_side=0;game_info.los=40;")
        require(release["priorCacheTick"] == release["sourceTick"] - 1 and
                release["currentCameraTick"] == release["sourceTick"], "Actual preceding cache/current camera required.")
        for axis, value in zip("xyz", release["cameraPosition"]):
            c.append("cambot." + axis + "=" + floating(value) + ";")
        for index_matrix, value in enumerate(release["cameraMatrix"]):
            c.append("cambot.xform[" + str(index_matrix) + "]=" + floating(value) + ";")
        for joint, point in ((7, release["cameraJoints"][7]), (4, release["cameraJoints"][4])):
            for axis, value in enumerate(point):
                c.append(f"thrower.jpos[{joint}][{axis}]={floating(value)};")
        for axis, value in zip("xyz", target["position"]):
            c.append("receiver.odata." + axis + "=" + floating(value) + ";")
        for axis, value in zip(("vx", "vy", "vz"), target["velocity"]):
            c.append("receiver.odata." + axis + "=" + floating(value) + ";")
        initial = case["BeforeSet"]["flags"] if case["BeforeSet"] is not None else release["flags"]
        c.append(f"o->flags={initial};uint32_t mask=PF_FLIP;memcpy(operand,&mask,4);a->pscript=operand;")
        if before_masks[index]:
            c.append(("_ani_setflag" if before_masks[index] == "ANI_SETFLAG" else "_ani_clrflag") + "(o,a);")
        c.append("int before=o->flags;set_ball_vels(o,&receiver.odata);a->pscript=operand;")
        if clears[index]:
            c.append("_ani_clrflag(o,a);")
        c.append(f"row({index},-1,before,o->flags);")
        for frame, observed in enumerate(case["trajectory"]):
            c.append("while(ball_obj.flight_time<" + str(observed["ball"]["elapsed"]) + ")advance_kernel();"
                     f"row({index},{frame},before,o->flags);")
        c.append("}")
    c.append("return 0;}")
    source_path = output / "original_left_release.c"; source_path.write_text("\n".join(c))
    command = ["gcc", "-std=c11", "-O0", "-ffp-contract=off", "-fno-fast-math", "-fno-strict-aliasing",
               "-fsigned-char", str(source_path), "-lm", "-o", str(output / "original_left_release")]
    built = subprocess.run(command, capture_output=True, text=True)
    (output / "compile.log").write_text(built.stdout + built.stderr)
    require(built.returncode == 0, "Original release oracle compile failed: " + built.stderr[-5000:])
    execution = subprocess.run([str(output / "original_left_release")], capture_output=True, text=True, timeout=30)
    (output / "oracle.jsonl").write_text(execution.stdout)
    require(execution.returncode == 0, "Original release oracle execution failed: " + execution.stderr[-2000:])
    failures, assertions = list(data["failures"]), data["assertions"]
    def check(case, field, expected, observed):
        nonlocal assertions
        assertions += 1
        if expected != observed:
            failures.append({"case": case["script"], "field": field, "original": expected, "production": observed})
    for line in execution.stdout.splitlines():
        expected = json.loads(line); case = data["cases"][expected["case"]]
        release = case["BeforeRelease"]
        if expected["frame"] == -1:
            observed = case["AfterRelease"]["ball"]
            check(case, "originalPF-before-real-release", expected["flagsBefore"] & 1, release["flags"] & 1)
            if case["left"]:
                check(case, "actual-clear-continuation", True, case["AfterClear"] is not None)
                if case["AfterClear"]:
                    check(case, "originalPF-after-real-clear", expected["flagsAfter"] & 1, case["AfterClear"]["flags"] & 1)
                    check(case, "clear-sourceTick", case["ReleaseTick"], case["AfterClear"]["sourceTick"])
                    check(case, "clear-does-not-change-pose", pose_bits(case["AfterRelease"]["pose"]), pose_bits(case["AfterClear"]["pose"]))
                if case["BeforeSet"]:
                    check(case, "set-sourceTick", case["ReleaseTick"], case["BeforeSet"]["sourceTick"])
                    check(case, "set-does-not-mirror-pose", pose_bits(case["BeforeSet"]["pose"]), pose_bits(release["pose"]))
        else:
            frame = case["trajectory"][expected["frame"]]
            observed = frame["ball"]
            check(case, "actualSource-flightTime", expected["elapsed"], observed["elapsed"])
            check(case, "sourceTick-flightTime", frame["sourceTick"] - case["ReleaseTick"],
                  observed["elapsed"] - case["trajectory"][0]["ball"]["elapsed"])
            for axis, offset in enumerate((16, 17, 18)):
                check(case, "real-freeball-translation" + str(axis), expected["position"][axis], bits(frame["freeBallPacket"][offset]))
        for field in ("origin", "position", "velocity", "target"):
            for axis, (original, actual) in enumerate(zip(expected[field], observed[field])):
                check(case, field + str(axis), original, bits(actual))
        check(case, "totalFlightTicks", expected["total"], observed["total"])
    result = {
        "schema": "actual-original-left-release-trajectory-comparison-20261111/v1",
        "status": "passed" if not failures else "failed", "cases": len(data["cases"]),
        "assertions": assertions, "failures": failures, "originalSites": sites,
        "sourcePins": PINS, "unchangedOriginalBodyPins": body_pins, "compilerCommand": command,
        "manifestSha256": data["ManifestSha256"],
        "boundaries": [
            "Only the three complete-profile original left-QB SET/CLEAR brackets are repaired.",
            "Full programme-entry fixtures, actual public source/callback/ball/pose/audio consumers; not physical selector coverage.",
            "Original set_ball_vels receives actual preceding camera-space cache and current camera. Fresh world wrists are diagnostics only and are not oracle input.",
            "Stationary receiver inputs disable original canned-catch branch; its unbound data/callback paths hard-fail if reached, never supply invented outcomes.",
            "Original active ball_proc motion block is unchanged; no source ball constants, RNG or clocks are tuned.",
            "This selector proves the actual cache consumer; the camera-cache selector separately qualifies original cache production. No target binary ABI, GPU pixels or DSP equivalence claim.",
        ],
    }
    (output / "result.json").write_text(json.dumps(result, indent=2))
    print(json.dumps({k:result[k] for k in ("status", "cases", "assertions", "failures")}, indent=2)[:10000])
    return result

def compare_camera_cache(original_root, observations, output, diagnostic=False):
 PINS={
  "player.c":"5744c51f30b625b9f80b92388ab49ff0ebc5bb2c",
  "cambot.c":"00c9c913bc6a697dfd590666bf5597f2c2bc1eb7",
  "lib/ani3d/pipe3D.c":"0966ecaf0d7eef7d9c635a97899a421ce1eeea41",
  "lib/ani3d/ANIM.C":"97d7856d67a432937e249be274ef67a9ad354dc1",
  "include/ani3d.h":"826c6322467fcfe503b8402b540e4731671f9b6b",
  "include/player.h":"a86b1a5b52ba842b206cd7dd0bbb26521db6dcb8",
  "include/GAME.H":"09d65c92971c32c44b1dc49fb4e4e98b371b1cd3"}
 output.mkdir()
 raw={n:(original_root/n).read_bytes() for n in PINS}
 for n,pin in PINS.items():require(source.blob(raw[n])==pin,"Original Git blob changed: "+n)
 d=json.loads(observations.read_bytes())
 require(d["schema"]=="actual-nonidentity-camera-cache-release-observations-20261111/v1","Actual nonidentity producer inputs required.")
 skeleton=base64.b64decode(d["originalSkeletonBase64"],validate=True)
 require(sha(skeleton)=="a957d80a322948d7a41667cf80ab621a2ff7b5e74eee6d4da8fba31f216c0a93","Actual immutable original skeleton differs.")
 header=raw["include/ani3d.h"].decode("latin1")
 c=["#include <stdint.h>\n#include <stdio.h>\n#include <stdlib.h>\n#include <string.h>\n#include <math.h>",
    "#define TRUE 1\n#define FALSE 0\n#define ANI_DEBUG 0\n#define LESSER(a,b) ((a)<(b)?(a):(b))","typedef void *Texture_node_t;"]
 for tag in("anim_header","_anidata","_obj_3d","_vec3d","_SNODE"):
  m=re.search(r"typedef struct "+tag+r"\s*\{.*?\}\s*\w+;",header,re.S);require(m is not None,"Original type required:"+tag);c.append(m[0])
 catch=re.search(rb"typedef struct _catch_t\s*\{.*?\}\s*catch_t;",raw["player.c"],re.S);c.append(catch[0].decode("latin1"))
 wanted={"BALL_VELOCITY","BALL_YARDS","BALL_YMAX","TIME_MAX","COS45","TBALL_VELOCITY","TBALL_YMAX","TTIME_MAX",
 "FBALL_YARDS","FBALL_VELOCITY","FBALL_YMAX","FTIME_MAX","GRAVITY","FIELDHB","FIELDW","FIELDHZ","FOCFAC","YD2UN",
 "FIELDX","ISHUMAN","CF_NO_UNINT","PF_FLIP","BF_TURBO_PASS","MODE_UNINT","MODE_TWOPART","JOINT_LWRIST","JOINT_RWRIST",
 "SN_HEADLIMB","SN_HEADLIMB_BIT"}
 defs={}
 for path in("include/ani3d.h","include/player.h","include/GAME.H","player.c"):
  for line in raw[path].decode("latin1").splitlines():
   m=re.match(r"^\s*#define\s+(\w+)(.*)$",line)
   if m and (m[1] in wanted or m[1].startswith("SN_")):defs[m[1]]=line.split("//",1)[0].strip()
 require(wanted<=defs.keys(),"Missing original macro:"+str(wanted-defs.keys()));c.extend(defs.values())
 c.extend([
 "typedef struct {obj_3d odata;int team,station;float matrices[15][12],jpos[15][3];} fbplyr_data;",
 "static struct {float x,y,z,a,b,c,e,f,g,xform[12];} cambot;",
 "static struct {int off_side,los,team_fire[2];} game_info;",
 "static struct {obj_3d odata;int flags,int_receiver,flight_time,total_time;float ox,oy,oz,tx,ty,tz;} ball_obj;",
 "static int pup_fastpass,did_catch;static char *last_catch;static float bighead_scale;",
 "static catch_t *catch_front_tbl,*catch_behind_tbl,*catch_lside_tbl,*catch_rside_tbl;",
 "static float fsqrt(float x){return sqrtf(x);}static void lockup(void){abort();}",
 "static int randrng(int n){(void)n;abort();}static void drone_catch(int a,char*b,int c){(void)a;(void)b;(void)c;abort();}",
 "static float trigs[257]={"+",".join(map(floating,d["quarterSine"]))+"};",
 "static uint32_t bits(float v){uint32_t n;memcpy(&n,&v,4);return n;}"
 ])
 for macro in("FRELX","FRELZ"):
  c.append(re.search(rb"(?m)^#define\s*"+macro.encode()+rb"\([^\r\n]+",raw["lib/ani3d/ANIM.C"])[0].decode())
 bodypins={};bodies=[]
 for path,names in(
  ("lib/ani3d/pipe3D.c",("isin","icos","magv","norm","vxv","roty","mxm","quat2mat","generate_matrices")),
  ("cambot.c",("update_cambot_xform",)),("player.c",("set_ball_vels",))):
  for name in names:
   _,_,body=source.function(raw[path],name);bodypins[name]=dict(path=path,sha256=sha(body));bodies.append(body.decode("latin1"))
 for b in bodies:c.append(b[:b.index("{")].strip()+";")
 c.extend(bodies)
 for name in("fbplyr_skel","tophalf_skel","bothalf_skel"):
  m=re.search(rb"SNODE\s+"+name.encode()+rb"\[\]\s*=\s*\{",skeleton);require(m is not None,"Original skeleton array missing.")
  end=source.closing(skeleton,m.end()-1);c.append(skeleton[m.start():end+1].decode("latin1"))
 _,_,last=source.function(raw["player.c"],"last_player_proc")
 start=last.index(b"roty( pobj->fwd, mat );");end=last.index(b"// do big head logic",start)
 first=last[start:end]
 start2=last.index(b"if( pobj->adata[1].animode & MODE_TWOPART )",end);open2=last.index(b"{",start2)
 end2=source.closing(last,open2)
 else_begin=last.index(b"else",end2);else_open=last.index(b"{",else_begin);end3=source.closing(last,else_open)
 second=last[start2:end3]
 bodypins["last_player_proc/matrix-prefix"]=dict(path="player.c",sha256=sha(first))
 bodypins["last_player_proc/actual-two-part-or-full-matrix-producer"]=dict(path="player.c",sha256=sha(second))
 c.append("static void cache_actor(fbplyr_data *ppdata){obj_3d *pobj=&ppdata->odata;float mat[12]={0},cm[12],cm2[12];bighead_scale=0;\n"+first.decode("latin1")+"\n"+second.decode("latin1")+"\n}")
 c.append(r'''
static void camera_row(int group,int n){
 printf("{\"kind\":\"camera\",\"group\":%d,\"index\":%d,\"matrix\":[",group,n);
 for(int i=0;i<12;i++)printf("%s%u",i?",":"",bits(cambot.xform[i]));printf("]}\n");
}
static void joints_row(int group,int n,fbplyr_data*f){
 printf("{\"kind\":\"joints\",\"group\":%d,\"index\":%d,\"joints\":[",group,n);
 for(int j=0;j<15;j++)for(int k=0;k<3;k++)printf("%s%u",j||k?",":"",bits(f->jpos[j][k]));printf("]}\n");
}
static void release_row(int group,char*kind){
 printf("{\"kind\":\"%s\",\"group\":%d,\"origin\":[%u,%u,%u],\"velocity\":[%u,%u,%u],\"target\":[%u,%u,%u],\"total\":%d}\n",
 kind,group,bits(ball_obj.ox),bits(ball_obj.oy),bits(ball_obj.oz),bits(ball_obj.odata.vx),bits(ball_obj.odata.vy),bits(ball_obj.odata.vz),bits(ball_obj.tx),bits(ball_obj.ty),bits(ball_obj.tz),ball_obj.total_time);
}
int main(void){
''')
 samples=[]
 def set_camera(value):
  eye,focus=value["eye"],value["focus"]
  for name,v in zip(("x","y","z"),eye):c.append("cambot."+name+"="+floating(v)+";")
  for name,v in zip(("a","b","c"),focus):c.append("cambot."+name+"="+floating(v)+";")
  c.append("cambot.e=cambot.a;cambot.f=cambot.b+10.0f;cambot.g=cambot.c;update_cambot_xform();")
 def set_actor(frame):
  actor=frame["actor"]
  c.append("memset(&player,0,sizeof(player));")
  for name,v in zip(("x","y","z"),actor["root"]):c.append("player.odata."+name+"="+floating(v)+";")
  c.append("player.odata.ay="+floating(actor["animationRoot"][1])+";player.odata.ascale="+floating(actor["scale"])+";")
  c.append("player.odata.fwd="+str(actor["facing"])+";player.odata.fwd2="+str(actor["upperFacing"])+";")
  c.append("player.odata.adata[0].pq=primary;player.odata.adata[1].pq=secondary;")
  c.append("player.odata.adata[1].animode="+str(actor["secondaryMode"])+";")
  for i,v in enumerate(x for q in actor["primary"] for x in q):c.append(f"primary[{i}]={floating(v)};")
  if actor["secondary"]:
   for i,v in enumerate(x for q in actor["secondary"] for x in q):c.append(f"secondary[{i}]={floating(v)};")
 for group,case in enumerate(d["cases"]):
  c.append("{fbplyr_data player={0},receiver={0};float primary[60],secondary[60];")
  cameras=case["transitions"]+[x["camera"] for x in case["cacheFrames"]]+[case["BeforeRelease"]["currentCamera"]]
  for index,cam in enumerate(cameras):
   set_camera(cam);c.append(f"camera_row({group},{index});")
  samples.append(cameras)
  for index,frame in enumerate(case["cacheFrames"]):
   set_camera(frame["camera"]);set_actor(frame);c.append(f"cache_actor(&player);joints_row({group},{index},&player);")
  release=case["BeforeRelease"];previous=release["previousCache"]
  set_camera(previous["camera"]);set_actor(previous);c.append("cache_actor(&player);")
  set_camera(release["currentCamera"])
  c.append("player.odata.flags="+str(1 if release["flipped"] else 0)+";player.station=0;receiver.station="+str(release["receiver"]["station"])+";")
  c.append("game_info.off_side="+str(0 if case["side"]=="Home" else 1)+";game_info.los=40;")
  for axis,v in zip(("x","y","z"),release["receiver"]["position"]):c.append("receiver.odata."+axis+"="+floating(v)+";")
  for axis,v in zip(("vx","vy","vz"),release["receiver"]["velocity"]):c.append("receiver.odata."+axis+"="+floating(v)+";")
  c.append("memset(&ball_obj,0,sizeof(ball_obj));set_ball_vels(&player.odata,&receiver.odata);"+f'release_row({group},"native-release");')
  for j,point in enumerate(release["actualStoredCameraJoints"]):
   for k,v in enumerate(point):c.append(f"player.jpos[{j}][{k}]={floating(v)};")
  c.append("memset(&ball_obj,0,sizeof(ball_obj));set_ball_vels(&player.odata,&receiver.odata);"+f'release_row({group},"diagnostic-observed-cache-native-transform");')
  c.append("}")
 c.append("return 0;}")
 path=output/"original_camera_cache_release.c";path.write_text("\n".join(c))
 cmd=["gcc","-std=c11","-O0","-ffp-contract=off","-fno-fast-math","-fno-strict-aliasing",str(path),"-lm","-o",str(output/"original_camera_cache_release")]
 build=subprocess.run(cmd,capture_output=True,text=True);(output/"compile.log").write_text(build.stdout+build.stderr);require(build.returncode==0,"Original C compile failed:"+build.stderr[-6000:])
 exe=subprocess.run([str(output/"original_camera_cache_release")],capture_output=True,text=True,timeout=30)
 (output/"oracle.jsonl").write_text(exe.stdout);require(exe.returncode==0,"Original producer execution failed:"+exe.stderr[-2000:])
 failures=[];assertions=0;diagnostics=[]
 def check(group,kind,tick,field,x,y):
  nonlocal assertions
  assertions+=1
  if x!=y:failures.append(dict(side=d["cases"][group]["side"],kind=kind,tick=tick,field=field,original=x,production=y))
 for line in exe.stdout.splitlines():
  value=json.loads(line);g=value["group"];case=d["cases"][g];kind=value["kind"]
  if kind=="camera":
   obs=samples[g][value["index"]]
   for i,(x,y) in enumerate(zip(value["matrix"],obs["matrix"])):check(g,kind,obs["tick"],"matrix"+str(i),x,bits(y))
  elif kind=="joints":
   obs=case["cacheFrames"][value["index"]]
   for i,(x,y) in enumerate(zip(value["joints"],(v for p in obs["actualCameraJoints"] for v in p))):check(g,kind,obs["tick"],"joint"+str(i),x,bits(y))
  elif kind=="native-release":
   obs=case["AfterRelease"]["ball"]
   for key in("origin","velocity","target"):
    for i,(x,y) in enumerate(zip(value[key],obs[key])):check(g,kind,case["BeforeRelease"]["tick"],key+str(i),x,bits(y))
   check(g,kind,case["BeforeRelease"]["tick"],"total",value["total"],obs["total"])
  else:diagnostics.append(value)
 result=dict(schema="unchanged-original-nonidentity-camera-cache-release-comparison-20261111/v1",status="passed"if not failures else"failed",
 cases=len(d["cases"]),assertions=assertions,failures=failures,diagnosticObservedCacheTransform=diagnostics,
 originalBodyPins=bodypins,sourcePins=PINS,skeletonSha256=sha(skeleton),compilerCommand=cmd,
 scope=["Actual public source releases and preceding cache/order. Native wrist inputs are generated by unchanged original last_player_proc/pipe3D from genuine primitive pose/skeleton inputs, never managed world-wrist.",
 "Native xform is generated by unchanged original update_cambot_xform/norm/vxv from actual eye/focus producer-state inputs; not a full native camera tracking or external DG2RD macro qualification.",
 "All camera/joint/release comparisons are float-bit exact, no tolerance/global mirror/seed/clock/profile change."])
 (output/"result.json").write_text(json.dumps(result,indent=2))
 print(json.dumps(dict(status=result["status"],cases=result["cases"],assertions=assertions,failureCount=len(failures),firstFailures=failures[:12],kinds={k:sum(f["kind"]==k for f in failures)for k in("camera","joints","native-release")} ),indent=2))
 return result


def main():
    if sys.argv[1:] == ["--inside"]:
        return inside()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--observations", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--diagnostic", action="store_true")
    parser.add_argument("--suite", choices=("opcodes", "left-releases", "camera-cache", "raw-throw-selectors"), default="opcodes")
    parser.add_argument("--source-controls", action="store_true", help="Compile portable exact programme/cache/caller controls and a forced-fresh final.")
    parser.add_argument("--run", type=Path, help="Authenticate data and build the actual Core in a private readonly source capture.")
    parser.add_argument("--dotnet", type=Path, default=Path("/home/long2know/.dotnet/dotnet"))
    parser.add_argument("--controls", type=Path, help="Exact immutable captured-source.zip containing the entire pre-fix b9 state.")
    parser.add_argument("--left-release", action="store_true", help="Separate public left-release source-effect diagnostic, not part of19-class acceptance.")
    args = parser.parse_args()
    if args.run:
        return run_capture(args.source.resolve(), args.run.resolve(), args.output.resolve(), args.dotnet.resolve(),
                           args.diagnostic, args.controls, args.left_release, args.suite, args.source_controls)
    require(args.observations is not None, "Actual VM observation input required.")
    require(args.suite != "raw-throw-selectors", "The raw selector has managed consumer checks only; use --run.")
    compare_suite = {"opcodes": compare, "left-releases": compare_left_releases, "camera-cache": compare_camera_cache}[args.suite]
    result = compare_suite(args.source, args.observations, args.output, args.diagnostic)
    print(json.dumps({key: value for key, value in result.items()
                      if key in ("status", "cases", "classes", "assertions", "failures")}, indent=2)[:12000])
    return 0 if not result["failures"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
