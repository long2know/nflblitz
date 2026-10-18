#!/usr/bin/env python3
"""Offline, pinned medball2 SDL reconstruction for player.c:draw_ball (not native medball2.h)."""
import argparse
import json
import math
from pathlib import Path
import re

import prepare_popped_head as head

files, SDL = head.files, head.SDL
ROOT = head.ROOT
SCHEMA = "original-source-authored-free-ball/v1"
ORIGIN = "original-medball2-SDL-source-authored-not-native-medball2.h"
SOURCE_SHA256 = "d54218dd7d0fc45fe7ad7072cb39b12913dfed80687999f69d5e93a269bc9704"
SOURCE_BLOB = "ede5c3590475f55011340f2b6bcfb4b51304e1f0"
HEADER = "medball2.source-authored.h"
RECIPE = "models/models.mak medball2.h: cvtmod -name medball2 -append _big; default factor1; no recenter or player bind pivot."


def geometry(data):
    text = SDL.uncomment(data.decode("ascii"))
    head.require(len(re.findall(r"\bversion\s*=\s*7\.51\s*;", text)) == 1, "Expected exact free-ball SDL7.51.")
    declarations = SDL.declarations(text, "polyset")
    head.require(len(declarations) == 1 and declarations[0][0] == "medball_obj",
                 "Exactly the original medball_obj is required, not a held-ball hand.")
    number = SDL.FLOAT
    point = r"\(\s*cv\s*\(\s*\(\s*" + number + r"\s*,\s*" + number + r"\s*,\s*" + number + r"\s*\)\s*,\s*1\s*\)\s*\)"
    uv = r"st\s*\(\s*" + number + r"\s*,\s*" + number + r"\s*\)"
    triple = r"\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*\)"
    face = r"polygon\s*\(\s*" + triple + r"\s*,\s*" + triple + r"\s*,\s*0\s*\)"
    sequence = lambda item: r"\(\s*" + item + r"(?:\s*,\s*" + item + r")*\s*\)"
    grammar = (r"\s*active\s*=\s*ON\s*,\s*motion_blur\s*=\s*ON\s*,\s*smooth\s*=\s*ON\s*,"
               r"\s*doublesided\s*=\s*OFF\s*,\s*opposite\s*=\s*OFF\s*,"
               r"\s*shader\s*=\s*\(\s*hedshu\s*\)\s*,\s*casts_shadow\s*=\s*TRUE\s*,"
               r"\s*vertices\s*=\s*" + sequence(point) + r"\s*,\s*texture_vertices\s*=\s*" +
               sequence(uv) + r"\s*,\s*polygons\s*=\s*" + sequence(face) + r"\s*")
    head.require(re.fullmatch(grammar, declarations[0][1]) is not None,
                 "Unsupported free-ball geometry/flags/material grammar.")
    adapted = re.sub(rb"\bversion\s*=\s*7\.51\s*;", b"version=8.2;", data, count=1)
    scene = SDL.read_sdl(adapted, "models/medball2.sdl", scale=(1, 1, 1))
    poly = scene.polysets[0]
    texture = scene.textures[scene.shaders[poly.shaders[0]]]
    head.require(texture.file == "hedshu.tif" and texture.repeat == (1, 1) and
                 texture.offset == (0, 0) and texture.rotation == 0,
                 "Free-ball material must retain the original HEDSHU atlas and identity UV transform.")
    head.require(all(math.isfinite(v) and abs(v) <= 1000000 for p in poly.positions for v in p) and
                 all(0 <= v <= 1 for p in poly.uvs for v in p), "Invalid free-ball positions/UVs.")
    for vertices, _, material in poly.triangles:
        head.require(material == 0 and len(set(vertices)) == 3, "Invalid free-ball triangle/material.")
        a, b, c = (poly.positions[v] for v in vertices)
        u, v = ([b[i] - a[i] for i in range(3)], [c[i] - a[i] for i in range(3)])
        head.require(any(u[(i + 1) % 3] * v[(i + 2) % 3] - u[(i + 2) % 3] * v[(i + 1) % 3] != 0
                         for i in range(3)), "Zero-area free-ball triangle.")
    return poly


def header(poly):
    scalar = lambda x: format(SDL.f32(x), ".6f") + "f"
    lines = ["/* Source-authored original medball2 SDL reconstruction; NOT recovered native medball2.h. */",
             "VERTEX free_ball_vtx[]={"]
    lines += ["{" + ",".join(scalar(v) for v in p) + "}," for p in poly.positions]
    lines += ["};", "ST free_ball_st[]={"]
    lines += ["{" + scalar(head.printed(SDL.f32(s * 256))) + "," +
              scalar(head.printed(SDL.f32(SDL.f32(1 - t) * 256))) + "}," for s, t in poly.uvs]
    lines += ["};", "TRI free_ball_tris[]={"]
    for i, (vertices, coordinates, _) in enumerate(poly.triangles):
        fields = [v * 3 for v in vertices] + list(coordinates) + [0 if i == 0 else -4]
        lines.append("{" + ",".join(map(str, fields)) + "},")
    lines += ["{0,0,0,0,0,0,-20}};", "LIMB limb_medball_obj_big={" + str(len(poly.positions)) + "," +
              str(len(poly.triangles)) + ",NULL,free_ball_vtx,free_ball_st,free_ball_tris};"]
    return ("\n".join(lines) + "\n").encode("ascii")


def prepare(original, calibration_root, output):
    original = files.absolute_path(original, Path.cwd())
    calibration_root = files.absolute_path(calibration_root, Path.cwd())
    files.check_physical(original, directory=False)
    files.check_physical(calibration_root, directory=True)
    raw = files.read_regular(original, 65536)
    head.require(len(raw) == 13635 and files.sha256(raw) == SOURCE_SHA256,
                 "Exact pinned original models/medball2.sdl required.")
    calibration = []
    for stem in ("plyr266", "plyr498"):
        sdl = files.read_regular(calibration_root / (stem + ".sdl"), 128 * 1024)
        generated = files.read_regular(calibration_root / (stem + ".h"), 128 * 1024)
        head.require((files.sha256(sdl), files.sha256(generated)) == head.CALIBRATION[stem],
                     "Exact original SDL/generated calibration pair required: " + stem)
        calibration.append(head.calibrate(sdl, generated, stem))
    poly = geometry(raw)
    converted = header(poly)
    manifest = {"schema": SCHEMA, "origin": ORIGIN, "sourceRevision": head.SOURCE,
                "sourcePath": "models/medball2.sdl", "sourcePayload": "models/medball2.sdl",
                "sourceSha256": SOURCE_SHA256, "sourceGitBlobSha1": SOURCE_BLOB, "sourceBytes": len(raw),
                "headerPath": HEADER, "headerSha256": files.sha256(converted), "headerBytes": len(converted),
                "limbSymbol": "limb_medball_obj_big", "sourceScale": 1, "sourcePivot": "authoring-origin-unchanged",
                "rawTextureCode": 0, "material": "hedshu.wms", "alternateMaterial": "hedshu2.wms",
                "sourceSUnits": 256, "sourceTUnits": 256, "vertices": len(poly.positions),
                "textureCoordinates": len(poly.uvs), "triangles": len(poly.triangles),
                "nativeGeneratedHeaderEquivalent": False,
                "triangleOrderOrigin": "Source SDL polygon order; native compiler ordering unqualified.",
                "recipe": RECIPE, "calibration": calibration}
    bundle = {"models/medball2.sdl": raw, HEADER: converted, "free-ball.json": files.json_bytes(manifest)}
    output = files.absolute_path(output, ROOT)
    head.require(ROOT in output.parents and any(p in (".scratch", "data", "artifacts") for p in output.parts),
                 "Use an explicit ignored output inside this worktree.")
    if output.exists():
        files.verify_identical(output, bundle)
    else:
        files.make_directories(output.parent)
        output.mkdir(mode=0o700)
        for name, payload in bundle.items():
            path = output / name
            files.make_directories(path.parent)
            path.write_bytes(payload)
        files.verify_identical(output, bundle)
    return {"manifestSha256": files.sha256(bundle["free-ball.json"]), "headerSha256": files.sha256(converted),
            "vertices": len(poly.positions), "triangles": len(poly.triangles), "output": str(output)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original-sdl", type=Path, required=True)
    parser.add_argument("--calibration-models", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(prepare(args.original_sdl, args.calibration_models, args.output), indent=2))
    except (ValueError, OSError) as error:
        parser.exit(1, "Original free-ball inputs rejected: " + str(error) + "\n")


if __name__ == "__main__":
    main()
