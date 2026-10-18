#!/usr/bin/env python3
"""Strict offline original phead SDL reconstruction, not recovered native head.h."""
import argparse
from collections import Counter, defaultdict
import importlib.util
import json
import math
from pathlib import Path
import re
import sys

import prepare_player as files

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("popped_head_sdl", ROOT / "tools/environments/prepare_environment.py")
SDL = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = SDL
SPEC.loader.exec_module(SDL)
SCHEMA = "original-source-authored-popped-head/v1"
SOURCE = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded"
SHA256 = "3fb4e566b57670e397fde16c6f515d06c95379420074c2d6c7b111bca843e257"
BLOB = "23410e01bb6db9e5fd9bacdd9440691147046798"
ORIGIN = "original-phead-SDL-source-authored-reconstruction-not-native-head.h"
HEADER = "phead.source-authored.h"
CALIBRATION = {
    "plyr266": ("37dfe2afcef46c9ebfa21298486454a6112aaaf40cf38b979925449e53fe9294",
                "9d91e22e0f80dc369a52c8956c6fb3f7b37504a3b266a8cba4f5125d93741149"),
    "plyr498": ("194152a05820fb4f134ff47ca48898e4fc217d0729d74fdbee456fb12ceaf2a3",
                "2f170cd7d052d541c2ee3259252b0c8653e161195c38bc85d95ec63a7c422621"),
}


def require(value, message):
    if not value:
        raise ValueError(message)


def printed(value):
    return SDL.printed_float(value)


def array(text, kind, name):
    match = re.search(r"\b" + kind + r"\s*" + re.escape(name) + r"\s*\[\s*\]\s*=\s*\{(.*?)\};", text, re.S)
    require(match is not None, "Missing exact calibration declaration: " + name)
    return match[1]


def rows(body, floating):
    return [tuple(SDL.f32(float(v.strip().rstrip("fF"))) if floating else int(v.strip())
                  for v in row.split(",")) for row in re.findall(r"\{([^{}]+)\}", body)]


def canonical(corners):
    corners = tuple(corners)
    return min(corners, corners[1:] + corners[:1], corners[2:] + corners[:2])


def calibrate(sdl_bytes, header_bytes, stem):
    """Compare actual generated output by expanded faces, never copied expected vectors."""
    text = sdl_bytes.decode("ascii")
    require(len(re.findall(r"\bversion\s*=\s*7\.51\s*;", text)) == 1, "Calibration requires exact SDL7.51.")
    adapted = re.sub(r"\bversion\s*=\s*7\.51\s*;", "version=8.2;", text, count=1).encode("ascii")
    scene = SDL.read_sdl(adapted, stem, scale=(1.2, 1.2, 1.2))
    header = SDL.uncomment(header_bytes.decode("ascii"))
    names = re.findall(r'"([^"]+)"', array(header, r"char\s*\*", stem + "_textures"))
    expected_names = [re.sub(r"\.[^.]{3}$", ".wms", scene.textures[n].file) for n in scene.slots]
    require(names == expected_names, "Original calibration texture-slot identities differ: " + stem)
    results = []
    suffix = stem.removeprefix("plyr")
    for poly in scene.polysets:
        name = poly.name + suffix
        points = rows(array(header, "VERTEX", name + "_vtx"), True)
        sts = rows(array(header, "ST", name + "_st"), True)
        triangles = rows(array(header, "TRI", name + "_tris"), False)
        require(points == poly.positions, "Original binary32/printed vertex transform differs: " + name)
        require(triangles[-1] == (0, 0, 0, 0, 0, 0, -20), "Original TRI terminator differs: " + name)
        expected = Counter()
        skin_uvs = defaultdict(list)
        for face in poly.triangles:
            texture = scene.textures[scene.shaders[poly.shaders[face[2]]]]
            slot = names.index(re.sub(r"\.[^.]{3}$", ".wms", texture.file))
            corners = []
            for vertex, coordinate in zip(face[0], face[1]):
                uv = poly.uvs[coordinate]
                s = SDL.f32(SDL.f32(uv[0] * texture.repeat[0]) + texture.offset[0])
                t = SDL.f32(SDL.f32(uv[1] * texture.repeat[1]) + texture.offset[1])
                corners.append((vertex, (printed(SDL.f32(256 * s)), printed(SDL.f32(256 * SDL.f32(1 - t))))))
            corners = canonical(corners)
            expected[(slot, tuple(v for v, _ in corners))] += 1
            if slot == 1:
                skin_uvs[tuple(v for v, _ in corners)].append(corners)
        actual, cached, slot = Counter(), {}, -1
        max_skin_error, skin_corners = 0, 0
        for triangle in triangles[:-1]:
            if triangle[6] >= 0:
                require(triangle[6] % 4 == 0, "Calibration raw texture code lost stride4.")
                slot = triangle[6] // 4
            corners = []
            for vertex, coordinate in zip(triangle[:3], triangle[3:6]):
                require(vertex % 3 == 0, "Calibration vertex stride changed.")
                vertex //= 3
                if coordinate != 255:
                    cached[vertex] = sts[coordinate]
                require(vertex in cached, "Calibration UV cache is uninitialized.")
                corners.append((vertex, cached[vertex]))
            corners = canonical(corners)
            indices = tuple(v for v, _ in corners)
            actual[(slot, indices)] += 1
            if slot == 1:
                errors = [max(abs(a - b) for (_, x), (_, y) in zip(corners, candidate) for a, b in zip(x, y))
                          for candidate in skin_uvs[indices]]
                require(errors, "Missing original HEDSHU calibration corner.")
                max_skin_error = max(max_skin_error, min(errors))
                skin_corners += 3
        require(actual == expected, "Original expanded topology/winding/material differs: " + name)
        # SDL exports six decimal places. Packed uniform headers have later c_hack UV remaps;
        # the HEDSHU skin used by phead is not one of those packed uniform regions.
        require(max_skin_error <= 0.5e-6 * 256, "Original HEDSHU UV transform exceeds SDL export precision: " + name)
        results.append({"polyset": poly.name, "vertices": len(points), "faces": len(triangles) - 1,
                        "exactCoordinates": True, "exactTopologyWindingMaterials": True,
                        "skinUvCorners": skin_corners, "maxSkinUvSourceUnitDelta": max_skin_error,
                        "skinUvPrecisionBoundFromSixDecimalSdl": 0.5e-6 * 256,
                        "packedUniformUvs": "Existing original generated/header c_hack remaps retained, not replaced by this head conversion."})
    return {"model": stem, "polysets": results, "sdlSha256": files.sha256(sdl_bytes),
            "generatedHeaderSha256": files.sha256(header_bytes)}


def geometry(data):
    text = SDL.uncomment(data.decode("ascii"))
    require(len(re.findall(r"\bversion\s*=\s*8\.2\s*;", text)) == 1, "Expected Alias SDL8.2.")
    declarations = list(SDL.declarations(text, "polyset"))
    require(len(declarations) == 1 and declarations[0][0] == "phead_obj", "Exactly phead_obj is required.")
    body = declarations[0][1]
    # The complete referenced geometry grammar is consumed, not permissively regex-extracted.
    number = SDL.FLOAT
    point = r"\(\s*cv\s*\(\s*\(\s*" + number + r"\s*,\s*" + number + r"\s*,\s*" + number + r"\s*\)\s*,\s*1\s*\)\s*\)"
    st = r"st\s*\(\s*" + number + r"\s*,\s*" + number + r"\s*\)"
    triple = r"\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*\)"
    face = r"polygon\s*\(\s*" + triple + r"\s*,\s*" + triple + r"\s*,\s*0\s*\)"
    sequence = lambda item: r"\(\s*" + item + r"(?:\s*,\s*" + item + r")*\s*\)"
    grammar = (r"\s*active\s*=\s*ON\s*,\s*motion_blur\s*=\s*OFF\s*,\s*smooth\s*=\s*OFF\s*,"
               r"\s*doublesided\s*=\s*OFF\s*,\s*opposite\s*=\s*OFF\s*,"
               r"\s*shader\s*=\s*\(\s*HEDSHU#2\s*\)\s*,\s*casts_shadow\s*=\s*FALSE\s*,"
               r"\s*vertices\s*=\s*" + sequence(point) + r"\s*,\s*texture_vertices\s*=\s*" +
               sequence(st) + r"\s*,\s*polygons\s*=\s*" + sequence(face) + r"\s*")
    require(re.fullmatch(grammar, body) is not None, "Unsupported or changed phead polyset grammar/material/flags.")
    scene = SDL.read_sdl(data, "models/phead.sdl", scale=(1.2, 1.2, 1.2))
    poly = scene.polysets[0]
    require(all(math.isfinite(v) and abs(v) <= 1000000 for point in poly.positions for v in point),
            "Phead coordinates must remain finite within the production mesh bounds.")
    texture = scene.textures[scene.shaders[poly.shaders[0]]]
    require(texture.file == "hedshu.TIF" and texture.repeat == (1, 1) and texture.offset == (0, 0) and texture.rotation == 0,
            "Original phead HEDSHU texture transform changed.")
    require(all(0 <= v <= 1 for uv in poly.uvs for v in uv), "Phead UVs must remain finite normalized authoring coordinates.")
    for vertices, _, material in poly.triangles:
        require(material == 0 and len(set(vertices)) == 3, "Phead triangle/material is invalid.")
        a, b, c = (poly.positions[v] for v in vertices)
        u, v = ([b[i] - a[i] for i in range(3)], [c[i] - a[i] for i in range(3)])
        require(any(u[(i + 1) % 3] * v[(i + 2) % 3] - u[(i + 2) % 3] * v[(i + 1) % 3] != 0 for i in range(3)),
                "Phead has a zero-area triangle.")
    return poly


def header(poly):
    scalar = lambda x: format(SDL.f32(x), ".6f") + "f"
    lines = ["/* Source-authored original phead SDL reconstruction; NOT recovered native head.h. */",
             "VERTEX phead_source_vtx[]={"]
    lines += ["{" + ",".join(scalar(v) for v in point) + "}," for point in poly.positions]
    lines += ["};", "ST phead_source_st[]={"]
    lines += ["{" + scalar(printed(SDL.f32(s * 256))) + "," +
              scalar(printed(SDL.f32(SDL.f32(1 - t) * 256))) + "}," for s, t in poly.uvs]
    lines += ["};", "TRI phead_source_tris[]={"]
    for i, (vertices, coordinates, _) in enumerate(poly.triangles):
        fields = [v * 3 for v in vertices] + list(coordinates) + [4 if i == 0 else -4]
        lines.append("{" + ",".join(map(str, fields)) + "},")
    lines += ["{0,0,0,0,0,0,-20}};", "LIMB limb_phead_obj={" + str(len(poly.positions)) + "," +
              str(len(poly.triangles)) + ",NULL,phead_source_vtx,phead_source_st,phead_source_tris};"]
    return ("\n".join(lines) + "\n").encode("ascii")


def prepare(original, calibration_root, output):
    original = files.absolute_path(original, Path.cwd())
    calibration_root = files.absolute_path(calibration_root, Path.cwd())
    files.check_physical(original, directory=False)
    files.check_physical(calibration_root, directory=True)
    raw = files.read_regular(original, 65536)
    require(len(raw) == 39272 and files.sha256(raw) == SHA256, "Exact pinned original models/phead.sdl SHA256 required.")
    proof = []
    for stem in ("plyr266", "plyr498"):
        source = files.read_regular(calibration_root / (stem + ".sdl"), 128 * 1024)
        generated = files.read_regular(calibration_root / (stem + ".h"), 128 * 1024)
        require((files.sha256(source), files.sha256(generated)) == CALIBRATION[stem],
                "Exact pinned SDL/generated calibration pair required: " + stem)
        proof.append(calibrate(source, generated, stem))
    poly = geometry(raw)
    converted = header(poly)
    manifest = {"schema": SCHEMA, "origin": ORIGIN, "sourceRevision": SOURCE, "sourcePath": "models/phead.sdl",
                "sourceSha256": SHA256, "sourceGitBlobSha1": BLOB, "sourceBytes": len(raw),
                "sourcePayload": "models/phead.sdl", "headerPath": HEADER,
                "headerSha256": files.sha256(converted), "headerBytes": len(converted),
                "sourceScale": 1.2, "sourceHeadJoint": 8, "rawTextureCode": 4, "logicalTextureSlot": 1,
                "sourceSUnits": 256, "sourceTUnits": 256, "vertices": len(poly.positions),
                "textureCoordinates": len(poly.uvs), "triangles": len(poly.triangles),
                "nativeGeneratedHeaderEquivalent": False,
                "triangleOrderOrigin": "Source SDL polygon order; original -z native qsort permutation unqualified.",
                "recipe": "models/models.mak head.h: cvtmod -f1.2 -z -l -s -t; l/t only suppress lists; s square256; z ordering explicitly portable.",
                "calibration": proof}
    bundle = {"models/phead.sdl": raw, HEADER: converted,
              "popped-head.json": files.json_bytes(manifest)}
    output = files.absolute_path(output, ROOT)
    require(ROOT in output.parents and any(p in (".scratch", "data", "artifacts") for p in output.parts),
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
    return {"manifestSha256": files.sha256(bundle["popped-head.json"]), "headerSha256": files.sha256(converted),
            "vertices": len(poly.positions), "triangles": len(poly.triangles), "origin": ORIGIN, "calibration": proof}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original-sdl", type=Path, required=True)
    parser.add_argument("--calibration-models", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(prepare(args.original_sdl, args.calibration_models, args.output), indent=2))
    except (ValueError, OSError, files.PreparationError, SDL.EnvironmentError) as error:
        parser.exit(1, "Original popped head rejected: " + str(error) + "\n")


if __name__ == "__main__":
    main()
