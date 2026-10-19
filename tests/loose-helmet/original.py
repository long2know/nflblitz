#!/usr/bin/env python3
"""Private pinned original-C/helper/process/draw and CVTMOD comparison; not native execution."""
import argparse
from collections import Counter
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import struct
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("helmet_original_extractor", ROOT / "tests/fire-spfx/oracle.py")
oracle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(oracle)
PINS = {
    "player.c": "61fb1be76029663294630e8633574ed065496da1ef6329071817e80622870b52",
    "nfl.c": "97013eed27a110e11f7180954ed7b7977ee7dc113c372d426392a686948ab434",
    "include/player.h": "a86b1a5b52ba842b206cd7dd0bbb26521db6dcb8",
    "include/id.h": "d2cc50865dd105c4143ac6b769765bd859cc4ff4",
    "include/GAME.H": "09d65c92971c32c44b1dc49fb4e4e98b371b1cd3",
    "include/ani3d.h": "826c6322467fcfe503b8402b540e4731671f9b6b",
    "include/sndcalls.h": "69f6c483502fb689eb1991169e0e31858378c763",
    "lib/ani3d/TRIGS.H": "a07d76798931f1240bdf7f3b8e753ebd7832763a",
    "lib/ani3d/pipe3D.c": "0966ecaf0d7eef7d9c635a97899a421ce1eeea41",
    "lib/ani3d/geom.s": "fecf16a0511ab5d14a7f5e3a96742407e1d43aa6",
    "process.c": "967ca53910f24ddfa48a14485113a8fd0c989959",
    "object.c": "c99e75df81f4993e463a65362358b304e2bc5c6c",
    "midwayvideo/include/goose/PROCESS.H": "d372245175e31399855acfa2dccddbea90da8e10",
    "midwayvideo/include/goose/object.h": "729e842d2198f51a64725acaa9d2a73adf67eec3",
    "models/models.mak": "75fb787ac9096101b51c920ba32cadbb593315f6",
    "models/phelm.sdl": "d086f6944207a0ff2fa906ef7fc9692dcfb289ec",
    "midwayvideo/tools/cvtmod/CVTMOD.C": "00173a92849bb7d1bc1e4fa69248ff9e2679a5e5",
    "midwayvideo/tools/cvtmod/CVTMOD.L": "53ae8aeedf58d7a390470d2262885e9b33420c1b",
}


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def compare(actual, expected, label):
    assert actual == expected, label + ": exact integer/IEEE-binary32 fields differ"


def canonical(corners):
    corners = tuple(corners)
    return min(corners, corners[1:] + corners[:1], corners[2:] + corners[:2])


def bits(value):
    return struct.unpack("<i", struct.pack("<f", value))[0]


def converted_faces(header):
    def array(kind, name):
        match = re.search(r"\b" + kind + r"\s+" + name + r"\s*\[\]\s*=\s*\{(.*?)\};", header, re.S)
        assert match, "Actual original CVTMOD output declaration missing: " + name
        return re.findall(r"\{([^{}]+)\}", match[1])
    points = [tuple(float(v.strip().rstrip("f")) for v in row.split(",")) for row in array("VERTEX", "phelm_obj_vtx")]
    uvs = [tuple(float(v.strip().rstrip("f")) for v in row.split(",")) for row in array("ST", "phelm_obj_st")]
    triangles = [tuple(int(v) for v in row.split(",")) for row in array("TRI", "phelm_obj_tris")]
    assert len(points) == 63 and len(triangles) == 90 and triangles[-1][-1] == -20
    result, cache = Counter(), {}
    for row in triangles[:-1]:
        corners = []
        for vertex, uv in zip(row[:3], row[3:6]):
            assert vertex % 3 == 0
            vertex //= 3
            if uv != 255:
                cache[vertex] = uvs[uv]
            assert vertex in cache
            s, t = cache[vertex]
            corners.append(tuple(bits(v) for v in points[vertex]) + (bits(s), bits(1 - struct.unpack("<f", struct.pack("<f", t))[0])))
        result[canonical(corners)] += 1
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--authority", type=Path, required=True)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--assembly", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    assert output.is_relative_to(ROOT / ".squad/.scratch") and not output.exists()
    output.mkdir(parents=True)
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output), DOTNET_NOLOGO="1")
    report = {"schema": "source-loose-helmet-original-C/v1", "status": "failed", "invocation": sys.argv,
              "originalCommit": "89f840d58d152a2a9f230cb8d0cdffbd554c8ded",
              "originalTree": "a7e0352125d46075b7d98e87253ab89b9160a4c7",
              "toolsCommit": "1792293aaaea00a919b59e8c05716978e85b4333",
              "originalSources": {}, "commands": [], "nativeQualified": False, "platformPRNG": "ExternalUnverified",
              "definedDomain": "Declared host binary32/int32 component wakes and tapes; sqrtf host adapter, no target ABI/FMA/scheduler/PRNG equivalence.",
              "drawDomain": "alpha127 keeps the source signed alpha<<24 in its defined C domain. Live portable alpha255 is tested in the actual runtime/GPU, not claimed as defined native C signed-shift behavior."}
    try:
        authority = {}
        for path, expected in PINS.items():
            raw = (args.authority / path).read_bytes()
            blob = hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()
            assert (sha(raw) if len(expected) == 64 else blob) == expected, "Wrong exact original input: " + path
            authority[path] = raw.decode("ascii")
            report["originalSources"][path] = {"sha256": sha(raw), "gitBlobSha1": blob}
        quarter_values = [float(x) for x in re.findall(r"[-+]?\d+\.\d+", authority["lib/ani3d/TRIGS.H"])]
        assert len(quarter_values) == 257
        quarter = output / "original-quarter-sines.json"
        quarter.write_text(json.dumps(quarter_values))
        player = authority["player.c"]
        proc = oracle.extract(player, "loose_helmet_proc")
        assert "process_exit" not in proc and "while (1)" in proc
        assert re.search(r"^ball_data\s+loose_helmet_obj\s*;", player, re.M)
        ball = re.search(r"typedef struct _ball_data\s*\{.*?\}\s*ball_data;", authority["include/player.h"], re.S)[0]
        integers = ("phi", "pitch", "pitch_rate", "alpha", "flight_time", "flags", "bounces", "bounce_time")
        ball += "\n" + "\n".join('_Static_assert(_Generic(((ball_data*)0)->' + field +
                                   ',int:1,default:0),"original ' + field + ' is int");' for field in integers)
        macros = []
        for path, names in (
            ("include/player.h", ("BF_WHITE", "BF_BOUNCE", "BF_VEL_PITCH", "ISHUMAN", "JOINT_NECK", "PF_POP_HELMET")),
            ("include/id.h", ("LOOSE_HELMET_PID", "OID_LOOSE_HELMET", "DRAWORDER_BALL")),
            ("include/GAME.H", ("GM_IN_PLAY",)),
            ("include/ani3d.h", ("GRAVITY",)),
            ("include/sndcalls.h", ("BALL_HIT_GRND_SND", "P_HARD_HIT3_SND", "VOLUME1", "ANCR_VOL2", "PRIORITY_LVL2")),
            ("midwayvideo/include/goose/PROCESS.H", ("PD_WIPEOUT", "NODESTRUCT")),
            ("midwayvideo/include/goose/object.h", ("OF_NONE",)),
        ):
            for name in names:
                match = re.search(r"^#define[ \t]+" + name + r"(?=[ \t(]).*$", authority[path], re.M)
                assert match, "Missing exact original macro: " + name
                macros.append(match[0])
        floor = re.search(r"^#define[ \t]+HELMET_FLOOR[ \t]+.*$", player, re.M)[0]
        macros.append(floor)
        lifecycle = "\n".join(oracle.extract(authority[path], name, path) for path, name in
                              (("process.c", "kill"), ("process.c", "killall"),
                               ("object.c", "delete_object"), ("object.c", "delete_multiple_objects")))
        direction = authority["lib/ani3d/TRIGS.H"] + "\n" + "\n".join(
            oracle.extract(authority["lib/ani3d/pipe3D.c"], name, "lib/ani3d/pipe3D.c")
            for name in ("isin", "icos", "iasin", "rotx", "roty", "rotz", "mxm"))
        functions = "\n".join(oracle.extract(player, name) for name in
                              ("maybe_pop_helmet", "loose_helmet_proc", "draw_loose_helmet"))
        cleanup = authority["nfl.c"].splitlines()[866:868]
        assert cleanup[0].strip() == "killall(LOOSE_HELMET_PID,0xFFFFFFFF);" and \
               cleanup[1].strip() == "delete_multiple_objects(OID_LOOSE_HELMET, 0xFFFFFFFF);"
        new_play = 'static void source_new_play(void) {\n#line 867 "nfl.c"\n' + "\n".join(cleanup) + "\n}\n"
        source = (HERE / "original_shim.c").read_text()
        for key, value in {"BALL_DATA": ball, "MACROS": "\n".join(macros), "LIFECYCLE": lifecycle,
                           "DIRECTION": direction, "FUNCTIONS": functions, "NEW_PLAY": new_play}.items():
            source = source.replace("/* ORIGINAL_" + key + " */", value)
        path = output / "original-loose-helmet.c"
        path.write_text(source)

        def command(label, argv, **kwargs):
            report["commands"].append([str(a) for a in argv])
            result = subprocess.run([str(a) for a in argv], cwd=ROOT, env=env, text=True, capture_output=True,
                                    timeout=90, **kwargs)
            (output / (label + ".log")).write_text(result.stdout + result.stderr)
            assert result.returncode == 0, label + " failed:\n" + result.stdout + result.stderr
            return result.stdout

        executable = output / "original-glide3"
        command("compile-original-glide3", ["gcc", "-std=c11", "-O0", "-DGLIDE3", "-ffp-contract=off",
                "-fexcess-precision=standard", path, "-lm", "-o", executable])
        legacy = output / "original-pre-glide3"
        command("compile-original-pre-glide3", ["gcc", "-std=c11", "-O0", "-ffp-contract=off",
                "-fexcess-precision=standard", path, "-lm", "-o", legacy])
        managed = json.loads(command("managed-full-synthetic", ["dotnet", args.assembly]))
        assert managed["status"] == "passed"
        report["managedAssertions"] = managed["assertions"]
        for index, row in enumerate(managed["helperRows"]):
            original = json.loads(command("helper-" + str(index), [executable, "helper", *map(str, row["input"])]))
            for name in ("popped", "objectAlive", "processAlive", "operations"):
                compare(row[name], original[name], "actual helper " + name)
            compare([(d["bound"], d["value"], int(d["site"].split(":")[-1])) for d in row["random"]],
                    [(d["bound"], d["value"], d["line"]) for d in original["random"]], "original helper draw order")
            hard_hit = int(re.search(r"^#define\s+P_HARD_HIT3_SND\s+(\d+)", authority["include/sndcalls.h"], re.M)[1])
            expected_sounds = []
            for bank, call, volume, pan, priority in row["sounds"]:
                expected_sounds.append({"bank": bank, "call": hard_hit if call == "P_HARD_HIT3_SND" else int(call),
                                       "volume": 255, "pan": int(pan), "priority": priority})
            compare(expected_sounds, original["sounds"], "original helper sound selection")
        report["helperCases"] = len(managed["helperRows"])
        cases = []
        for entry in (0, 44, 45, 99):
            for height in (-19.5, 0, 10):
                for flags in (0, 2):
                    name = f"trace-{entry}-{height}-{flags}"
                    actual = json.loads(command(name + "-managed", ["dotnet", args.assembly, "--trace",
                                        entry, height, flags, 1020, 420, quarter]))
                    expected = json.loads(command(name + "-original", [executable, "trace", entry, height, flags, 1020, 420]))
                    compare(actual, expected, name)
                    cases.append({"entry": entry, "neckY": height, "flags": flags, "wakes": 420,
                                  "exactBinary32AndIntFields": True, "randomCalls": len(actual["random"])})
        report["physicsCases"] = cases
        report["sourceWakesCompared"] = len(cases) * 420
        actual = json.loads(command("carry-managed", ["dotnet", args.assembly, "--carry", quarter]))
        expected = json.loads(command("carry-original", [executable, "carry"]))
        compare(actual, expected, "original global carryover across kill/delete/replacement")
        report["globalCarryoverCompared"] = True
        truth = json.loads(command("original-lifecycle", [executable, "lifecycle"]))
        compare(truth, {"killLeavesObject": True, "killLeavesProcess": False, "deleteRemovesObject": True, "exitAlerts": 0},
                "original kill/delete separation without dust exit alert")
        report["lifecycle"] = truth
        actual = json.loads(command("draw-managed", ["dotnet", args.assembly, "--draw", quarter]))
        expected = json.loads(command("draw-original-glide3", [executable, "draw"]))
        before_glide3 = json.loads(command("draw-original-pre-glide3", [legacy, "draw"]))
        for port, original, old in zip(actual, expected, before_glide3):
            compare(port["matrix"], original["matrix"], "source Y*X(-fwd2)*Z(phi), scale/relative camera matrix")
            compare(original["matrix"], old["matrix"], "both original Glide branches share actual source matrix")
            compare(port["white"], bool(original["globalFlags"] & 1), "current GLOBAL ball WHITE")
            assert original["alphaSource"] == "GR_ALPHASOURCE_CC_ALPHA" and original["depthFunction"] == "GR_CMP_LESS"
            assert original["depthWrite"] == 1 and original["ratio"] == 4 and original["decal"] == 4321 and original["phelm"]
            assert original["blend"] == "GR_BLEND_SRC_ALPHA/GR_BLEND_ONE_MINUS_SRC_ALPHA/GR_BLEND_ONE/GR_BLEND_ZERO"
            assert original["colorFunction"] == ("GR_COLORCOMBINE_DECAL_TEXTURE_ADD_CCALPHA" if port["white"] else "GR_COLORCOMBINE_TEXTURE_TIMES_ALPHA")
            assert original["constantColor"] == (0xff000000 if port["white"] else 0x7f000000)
            assert old["colorFunction"] == ("GR_COLORCOMBINE_ONE" if port["white"] else "GR_COLORCOMBINE_TEXTURE_TIMES_ALPHA")
            assert old["constantColor"] == 0x7f000000
        report["drawCases"] = len(actual)
        report["legacyWhiteBranch"] = "Original pre-GLIDE3 ONE retains CC alpha; portable reused shader represents GLIDE3 white override. Native profiles remain unqualified."

        compat = output / "cvtmod-host-compat.h"
        compat.write_text("#include <stdlib.h>\n#include <string.h>\n#include <strings.h>\n#include <ctype.h>\n"
                          "double pow2(int);\nint yywrap(void);\nchar *strlwr(char *);\nchar *strupr(char *);\n"
                          "#define stricmp strcasecmp\n#define strnicmp strncasecmp\n")
        adapter = output / "cvtmod-host-compat.c"
        adapter.write_text('#include <math.h>\n#include <ctype.h>\ndouble pow2(int x){return pow(2.0,(double)x);}\n'
                           'char *strlwr(char *s){for(char *p=s;*p;p++)*p=(char)tolower((unsigned char)*p);return s;}\n'
                           'char *strupr(char *s){for(char *p=s;*p;p++)*p=(char)toupper((unsigned char)*p);return s;}\n')
        converter = output / "original-cvtmod"
        command("compile-original-cvtmod", ["gcc", "-x", "c", "-std=gnu99", "-O0", "-ffp-contract=off",
                "-fexcess-precision=standard", "-include", compat, args.authority / "midwayvideo/tools/cvtmod/CVTMOD.C",
                adapter, "-lm", "-o", converter])
        converted = command("original-cvtmod-output", [converter, "-f", "1.2", "-z", "-l", "-t"],
                            input=authority["models/phelm.sdl"])
        (output / "helm.original-host-generated.h").write_text(converted)
        bundle = json.loads((args.bundle / "manifest.json").read_text())
        mesh = (args.bundle / bundle["helmetMeshPath"]).read_bytes()
        geometry = json.loads(command("managed-geometry", ["dotnet", args.assembly, "--geometry", args.bundle, sha((args.bundle / "manifest.json").read_bytes())]))
        points = struct.unpack_from("<189i", mesh, 20)
        uv_bits = struct.unpack_from("<210i", mesh, 20 + 63 * 12)
        vertices = struct.unpack_from("<267i", mesh, 20 + 63 * 12 + 105 * 8)
        uvs = struct.unpack_from("<267i", mesh, 20 + 63 * 12 + 105 * 8 + 267 * 4)
        compare(geometry["positionBits"], list(points), "real C# PHELM geometry coordinates")
        compare(geometry["uvBits"], list(uv_bits), "real C# PHELM normalized material coordinates")
        compare(geometry["vertices"], list(vertices), "real C# PHELM source topology")
        compare(geometry["uvs"], list(uvs), "real C# PHELM UV corner bindings")
        faces = Counter()
        for i in range(0, 267, 3):
            faces[canonical(tuple(tuple(points[vertices[k] * 3:vertices[k] * 3 + 3]) +
                                  tuple(uv_bits[uvs[k] * 2:uvs[k] * 2 + 2]) for k in range(i, i + 3)))] += 1
        compare(faces, converted_faces(converted), "actual original CVTMOD -f1.2 -z -l -t expanded geometry/UV/winding")
        report["geometry"] = {"meshSha256": sha(mesh), "vertices": 63, "uvs": 105, "triangles": 89,
                              "actualOriginalConverterExecuted": True, "expandedFacesExactBinary32": True,
                              "sourceAuthoringRecipe": "models/models.mak helm.h: cvtmod -f1.2 -z -l -t < phelm.sdl",
                              "hostAdapter": "pow2(0)=1 normalized default and host case helpers; original yywrap retained; not original executable/native generated-header equivalence.",
                              "nativeGeneratedHeaderEquivalent": False}
        assert "ieee32\t128.0,\t256.0" in authority["lib/ani3d/geom.s"]
        report["literalRatio4"] = "actual active geom.s draw_tris ar_values[4]=128,256; normalized ST retained on the actual128x256 team pdecal2."
        report["status"] = "passed"
        print(json.dumps({k: v for k, v in report.items() if k in ("status", "managedAssertions", "helperCases",
                          "sourceWakesCompared", "globalCarryoverCompared", "drawCases", "geometry")}, indent=2))
        return 0
    except (AssertionError, OSError, subprocess.SubprocessError) as error:
        report["error"] = str(error)
        print(str(error), file=sys.stderr)
        return 1
    finally:
        (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    raise SystemExit(main())
