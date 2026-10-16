#!/usr/bin/env python3
"""Compare this port with a freshly compiled, blob-verified original cvtmod.

No historical binary is executed and no source is downloaded or copied into Git.
The original C, regenerated headers, executable and receipt stay in NEW ignored
output supplied by --destination. gcc and the original local source are required.
"""

import argparse
from collections import Counter
import json
from pathlib import Path
import re
import subprocess
import sys

import prepare_environment as port


def array(text, kind, name):
    match = re.search(r"\b" + kind + r"\s*" + re.escape(name) + r"\s*\[\s*\]\s*=\s*\{(.*?)\};", text, re.S)
    port.require(match is not None, "Original C output missing array: " + name)
    return match[1]


def triples(body, floating):
    result = []
    for entry in re.findall(r"\{([^{}]+)\}", body):
        values = entry.split(",")
        result.append(tuple(port.f32(float(v.strip().rstrip("fF"))) if floating else int(v.strip())
                            for v in values))
    return result


def canonical(corners):
    corners = tuple(corners)
    return min(corners, corners[1:] + corners[:1], corners[2:] + corners[:2])


def compare(scene, header):
    expected_slots = [re.sub(r"\.[^.]{3}$", ".wms", scene.textures[n].file) for n in scene.slots]
    actual_slots = re.findall(r'"([^"]+)"', array(header, r"char\s*\*", scene.name + "_textures"))
    port.require(actual_slots == expected_slots, "Original C texture slot order differs: " + scene.name)
    result = []
    for polyset in scene.polysets:
        positions = triples(array(header, "VERTEX", polyset.name + "_vtx"), True)
        sts = triples(array(header, "ST", polyset.name + "_st"), True)
        triangles = triples(array(header, "TRI", polyset.name + "_tris"), False)
        port.require(positions == polyset.positions, "Original C binary32/printed vertex mismatch: " + polyset.name)
        port.require(triangles[-1] == (0, 0, 0, 0, 0, 0, -20)
                     and len(triangles) == len(polyset.triangles) + 1,
                     "Original C triangle count/sentinel mismatch: " + polyset.name)
        actual, previous_uv, material = Counter(), {}, None
        for triangle in triangles[:-1]:
            vs, uv_indices, texture = triangle[:3], triangle[3:6], triangle[6]
            if texture >= 0:
                port.require(texture % 4 == 0, "Invalid original texture selector.")
                material = texture // 4
            port.require(material is not None and 0 <= material < len(scene.slots), "Uninitialized original material.")
            corners = []
            for v, st in zip(vs, uv_indices):
                port.require(v % 3 == 0, "Invalid original packed vertex index.")
                v //= 3
                if st != 255:
                    port.require(0 <= st < len(sts), "Invalid original packed UV index.")
                    previous_uv[v] = sts[st]
                port.require(v in previous_uv, "Uninitialized original compressed UV.")
                corners.append((v, previous_uv[v]))
            actual[(material, canonical(corners))] += 1
        expected = Counter((scene.slots.index(texture), canonical(corners))
                           for texture, corners in port.expanded_triangles(scene, polyset))
        port.require(actual == expected,
                     "Original C expanded index/winding/UV/material mismatch: " + scene.name + "/" + polyset.name)
        result.append({"name": polyset.name, "vertices": len(positions), "generatedStCount": len(sts),
                       "triangles": len(triangles) - 1, "boundsSource": port.bounds(positions)})
    return {"sourceSet": scene.name, "polysets": result, "textureSlots": actual_slots,
            "vertices": sum(len(p.positions) for p in scene.polysets),
            "triangles": sum(len(p.triangles) for p in scene.polysets)}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--tools-root", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    args = parser.parse_args(argv)
    project = Path(__file__).absolute().parents[2]
    recipe = port.load_recipe(project)
    try:
        source_root, tools_root = port.physical(args.source_root, True), port.physical(args.tools_root, True)
        for specification in recipe["converterEvidence"]:
            data = port.read_file(tools_root, specification["path"], specification["bytes"])
            actual = port.fingerprint(data)
            port.require(all(actual[key] == specification[key] for key in ("bytes", "gitBlobSha1")),
                         "Original converter Git blob mismatch: " + specification["path"])
        port.write_bundle(project, args.destination, {
            "compat.h": b"#include <stdlib.h>\n#include <unistd.h>\n#include <math.h>\n"
                        b"static double pow2(int exponent) { return ldexp(1.0, exponent); }\n",
        })
        destination = args.destination.absolute()
        executable = destination / "original-cvtmod"
        compilation = subprocess.run([
            "gcc", "-x", "c", "-std=gnu89", "-O0", "-include", str(destination / "compat.h"),
            str(tools_root / "tools/cvtmod/CVTMOD.C"), "-lm", "-o", str(executable),
        ], capture_output=True, text=True)
        port.require(compilation.returncode == 0, "Fresh original C build failed:\n" + compilation.stderr)
        receipt = {"sourceCommit": port.SOURCE_COMMIT, "toolsCommit": port.TOOLS_COMMIT,
                   "oracle": "Fresh original CVTMOD.C, no JONHEY define; models.mak active flags.",
                   "portabilityShim": "Standard headers and pow2(0)=ldexp(1,0)=1; no original geometry/parser changes.",
                   "results": []}
        for name, source in (("field2", "models/field2.sdl"), ("stadpie", "models/stadpie.sdl")):
            data = port.read_file(source_root, source)
            specification = next(e for e in recipe["inputs"] if e["path"] == source)
            port.require(port.fingerprint(data)["gitBlobSha1"] == specification["gitBlobSha1"],
                         "Original SDL Git blob mismatch: " + source)
            generated_name = "field2" if name == "field2" else "stadium"
            process = subprocess.run([str(executable), "-name", generated_name, "-f", "65.0", "-fz", "57.5"],
                                     input=data, capture_output=True)
            port.require(process.returncode == 0, "Original C converter failed: " + name)
            header = process.stdout.decode("ascii")
            (destination / (name + ".generated.h")).write_bytes(process.stdout)
            scene = port.read_sdl(data, name)
            scene.name = generated_name
            compared = compare(scene, header)
            compared["generatedHeaderSha256"] = port.digest(process.stdout)
            receipt["results"].append(compared)
        (destination / "comparison.json").write_bytes(port.json_bytes(receipt))
        print(json.dumps({"status": "PASS-original-C-transform-index-winding-UV-material-comparison",
                          "sets": len(receipt["results"]),
                          "polysets": sum(len(s["polysets"]) for s in receipt["results"]),
                          "vertices": sum(s["vertices"] for s in receipt["results"]),
                          "triangles": sum(s["triangles"] for s in receipt["results"])}))
        return 0
    except (port.EnvironmentError, OSError, ValueError) as error:
        print("Original C environment comparison rejected: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
