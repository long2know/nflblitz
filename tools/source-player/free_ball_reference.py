#!/usr/bin/env python3
"""Compile only exact pinned original free-ball math/draw functions into an ignored local oracle."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(ROOT / "tools/assets"))
import prepare_free_ball as ball

PINS = {
    "player": "61fb1be76029663294630e8633574ed065496da1ef6329071817e80622870b52",
    "pipe": "ad9ae52d41d90931c138f08511c49a997f16b798a4541567b4fcc34fcc375eb4",
    "trigs": "ae169332068a0561759bf5eb6ccaac4400a54ca5465d3b302908539388cf0ed4",
}


def function(text, result, name):
    match = re.search(r"\b" + result + r"\s+" + name + r"\s*\([^)]*\)\s*\{", text)
    assert match is not None, "Exact original function missing: " + name
    depth = 1
    end = match.end()
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[match.start():end]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("player", "pipe", "trigs", "sdl", "inputs", "geometry", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    output = args.output.absolute()
    assert output.is_relative_to(HERE / "artifacts") and not output.exists(), "Choose a new ignored owned oracle output."
    output.mkdir(parents=True)
    raw = {}
    for name, expected in PINS.items():
        raw[name] = getattr(args, name).read_bytes()
        assert hashlib.sha256(raw[name]).hexdigest() == expected, "Original source pin mismatch: " + name
    sdl = args.sdl.read_bytes()
    assert hashlib.sha256(sdl).hexdigest() == ball.SOURCE_SHA256
    poly = ball.geometry(sdl)
    emitted = json.loads(args.geometry.read_bytes())
    vertices, indices = emitted["vertices"], emitted["indices"]
    assert len(indices) == len(poly.triangles) * 3
    for face, (points, uvs, _) in enumerate(poly.triangles):
        for corner, source in enumerate((0, 2, 1)):
            i = indices[face * 3 + corner] * 5
            assert vertices[i:i + 3] == list(poly.positions[points[source]]), "Actual upload lost original geometry/pivot."
            u, v = poly.uvs[uvs[source]]
            expected_uv = [ball.SDL.f32(ball.head.printed(ball.SDL.f32(u * 256)) / 256),
                           ball.SDL.f32(1 - ball.SDL.f32(ball.head.printed(ball.SDL.f32(ball.SDL.f32(1 - v) * 256)) / 256))]
            assert vertices[i + 3:i + 5] == expected_uv, "Actual upload lost original UV/material mapping."
    text = r"""
#include <stdio.h>
#include <string.h>
typedef struct { float x,y,z; int fwd,fwd2; } obj_3d;
typedef struct { obj_3d odata; int flags; unsigned alpha; int phi; float scale; } ball_data;
ball_data ball_obj;
struct { int ball_carrier; } game_info;
struct { float x,y,z,xform[12]; } cambot;
int pup_bigball, limb_medball_obj_big, ball_decal=0, ball_decal2=1, ball_ratio;
int drawn,selected,white;
float captured[12];
#define GLIDE3 1
#define BF_WHITE 1
#define BF_ALT 64
#define BF_PHANTOM 2048
#define grConstantColorValue(...) ((void)0)
#define guAlphaSource(...) ((void)0)
#define grTexCombineFunction(...) ((void)0)
#define grTexClampMode(...) ((void)0)
#define grDepthBufferMode(...) ((void)0)
#define grDepthBufferFunction(...) ((void)0)
#define grDepthMask(...) ((void)0)
#define grChromakeyMode(...) ((void)0)
#define grAlphaBlendFunction(...) ((void)0)
#define GR_COLORCOMBINE_DECAL_TEXTURE_ADD_CCALPHA 1
#define GR_COLORCOMBINE_TEXTURE_TIMES_ALPHA 0
void guColorCombineFunction(int value) { white=value; }
void render_limb(int *limb,float *matrix,int *decal,int *ratio) {
  drawn=1; selected=*decal; memcpy(captured,matrix,sizeof(captured));
}
"""
    text += raw["trigs"].decode("ascii") + "\n"
    pipe = raw["pipe"].decode("ascii")
    for result, name in (("float", "isin"), ("float", "icos"), ("void", "rotx"), ("void", "roty"),
                         ("void", "rotz"), ("void", "mxm")):
        text += function(pipe, result, name) + "\n"
    text += function(raw["player"].decode("ascii"), "void", "draw_ball") + "\n"
    text += r"""
int main(void) {
  while (scanf("%d %d %d %d %d %f %d %f %f %f %f %f %f",
    &game_info.ball_carrier,&ball_obj.flags,&ball_obj.odata.fwd,&ball_obj.odata.fwd2,
    &ball_obj.phi,&ball_obj.scale,&pup_bigball,&ball_obj.odata.x,&ball_obj.odata.y,&ball_obj.odata.z,
    &cambot.x,&cambot.y,&cambot.z)==13) {
    for(int i=0;i<12;i++) if(scanf("%f",&cambot.xform[i])!=1) return 2;
    ball_obj.alpha=255; drawn=selected=white=0;
    memset(captured,0,sizeof(captured));
    draw_ball(&ball_obj);
    printf("%d %d %d",drawn,selected,white);
    for(int i=0;i<12;i++) printf(" %.9g",captured[i]);
    putchar('\n');
  }
  return 0;
}
"""
    source, executable = output / "original-free-ball.c", output / "original-free-ball"
    source.write_text(text)
    command = ["cc", "-std=c99", "-O0", "-ffp-contract=off", str(source), "-o", str(executable)]
    environment = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output))
    result = subprocess.run(command, env=environment, capture_output=True, text=True, timeout=60)
    (output / "compile.log").write_text(result.stdout + result.stderr)
    assert result.returncode == 0, "The original C oracle must compile before any comparison."
    inputs = json.loads(args.inputs.read_bytes())
    lines = []
    for row in inputs:
        values = [row["carrier"], row["rawFlags"], row["heading"], row["elevation"], row["roll"], row["scale"],
                  row["pupBigBall"], *row["position"], *row["cameraEye"], *row["cameraMatrix"]]
        lines.append(" ".join(map(str, values)))
    run = subprocess.run([str(executable)], input="\n".join(lines) + "\n",
                         env=environment, capture_output=True, text=True, check=True, timeout=60)
    actual = run.stdout.splitlines()
    assert len(actual) == len(inputs)
    cases = []
    for row, line in zip(inputs, actual):
        values = line.split()
        cases.append({"input": row, "drawn": values[0] == "1", "texture": int(values[1]),
                      "white": values[2] == "1", "matrix": list(map(float, values[3:]))})
    report = {"schema": "original-free-ball-draw-reference/v1", "status": "compiled-reference-produced",
              "originalSourcePins": PINS, "sourceRevision": ball.head.SOURCE,
              "command": command, "compiler": subprocess.check_output(["cc", "--version"], text=True).splitlines()[0],
              "originalFunctionClosure": ["draw_ball", "isin", "icos", "rotx", "roty", "rotz", "mxm"],
              "actualUploadedGeometryAndUvCornersCompared": len(indices),
              "cases": cases, "nativeToolchainOrPixelsQualified": False}
    (output / "reference.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps({key: report[key] for key in ("status", "actualUploadedGeometryAndUvCornersCompared", "nativeToolchainOrPixelsQualified")} |
                     {"cases": len(cases), "reference": str(output / "reference.json")}))


if __name__ == "__main__":
    main()
