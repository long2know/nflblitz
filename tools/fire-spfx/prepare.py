#!/usr/bin/env python3
"""Offline, authorized portable SDL/TGA fire bundle; never native TEXUS/WMS equivalence."""
import argparse
import importlib.util
import json
from pathlib import Path
import re
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("fire_environment_parser", ROOT / "tools/environments/prepare_environment.py")
assets = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = assets
spec.loader.exec_module(assets)

SCHEMA = "blitz-source-fire-spfx-portable/v1"
DUST_SCHEMA = "blitz-source-fire-spfx-portable/v2"
ORIGIN = "original-authoring-TGA32-and-SDL-portable-not-WMS"
ASPECT_ORIGIN = "decoded-authoring-dimensions-Glide-enum-not-observed-WMS-header"
UV_ORIGIN = "raw-SDL-ST-normalized-Unity-bottom-left-not-generated-spfx.h"
DUST_METADATA_ORIGIN = "authenticated-TGA-v2-tag20-developer-area-excluded-from-pixel-view"


def tga_pixel_view(data, dust=False):
    if not dust:
        return data
    # All ten authenticated authoring blobs carry the same nonpixel Photoshop
    # developer directory (tag20). Keep the original whole-file identity; pass
    # only its validated pixel view to the existing strict TGA decoder.
    assets.require(len(data) == 4458 and assets.digest(data[4114:]) ==
                   "4de6fb5218a64c3a2ee6c5fd52d32bbf3359295ed4033501c3b9044e89bc89a5",
                   "Original DIRT nonpixel developer directory/tag20/footer changed.")
    return data[:4114]


def geometry_from_sdl(data):
    text = data.decode("ascii")
    assets.require(len(re.findall(r"\bversion\s*=\s*7\.51\s*;", text)) == 1, "Expected the pinned SDL7.51 input.")
    # Reuse the existing cv/st/polygon grammar; only its8.2 version guard needs an explicit7.51 adapter.
    adapted = re.sub(r"\bversion\s*=\s*7\.51\s*;", "version = 8.2;", text, count=1).encode("ascii")
    scene = assets.read_sdl(adapted, "models/spfx.sdl", scale=(4.0, 4.0, 4.0))
    assets.require(len(scene.polysets) == 1, "Exactly the original Polyset required.")
    poly = scene.polysets[0]
    assets.require(poly.name == "Polyset" and len(poly.positions) == 4 and len(poly.uvs) == 4
                   and len(poly.triangles) == 2 and poly.shaders == ["SPFX"],
                   "Wrong original SDL geometry/shader inventory.")
    assets.require(poly.flags == {"active": "ON", "smooth": "ON", "doublesided": "OFF", "opposite": "OFF"},
                   "Changed original SDL flags.")
    packed = bytearray(b"BZFXG001" + struct.pack("<3I", 4, 4, 2))
    for point in poly.positions:
        packed.extend(struct.pack("<3f", *point))
    for uv in poly.uvs:
        packed.extend(struct.pack("<2f", *uv))
    for selector in (0, 1):
        for triangle in poly.triangles:
            assets.require(triangle[2] == 0, "Unexpected SDL texture slot.")
            packed.extend(struct.pack("<3I", *triangle[selector]))
    assets.require(len(packed) == 148, "Portable geometry format length.")
    return bytes(packed)


def prepare(original_root, output, authorization_reference, with_dust=False):
    assets.require(authorization_reference and len(authorization_reference) <= 1024, "Explicit local reuse authorization reference required.")
    assets.require(Path(original_root).is_absolute(), "Supply an explicit absolute authorized original root.")
    original_root = assets.physical(original_root, directory=True)
    assets.require(".." not in Path(output).parts, "Dot-segment output aliases are forbidden.")
    output = Path(output).absolute()
    assets.require(output.is_relative_to(ROOT / ".squad/.scratch") or output.is_relative_to(ROOT / "Assets/Imported"),
                   "Private fire bundle output must be below this worktree's ignored roots.")
    assets.require(not output.exists() and not output.is_symlink(), "Choose a new non-overwriting private bundle output.")
    assets.physical(output.parent, directory=True)
    catalog = json.loads((Path(__file__).parent / "inputs.json").read_text())
    assets.require(catalog["sourceCommit"] == assets.SOURCE_COMMIT and
                   catalog["sourceTree"] == "a7e0352125d46075b7d98e87253ab89b9160a4c7" and
                   len(catalog["inputs"]) == 18, "Pinned input inventory must contain18 originals at the selected revision.")
    payloads, inputs, frames, dust_inputs, dust_frames = {}, [], [], [], []
    dust_pins = catalog["dustInputs"] if with_dust else []
    assets.require(not with_dust or len(dust_pins) == 10, "Exactly ten pinned original DIRT inputs required.")
    for ordinal, pin in enumerate(catalog["inputs"] + dust_pins):
        dust = ordinal >= 18
        index = ordinal - 18 if dust else ordinal
        assets.require(pin["path"] == (f"img3d/dirt{index + 1}.tga" if dust else
                       "models/spfx.sdl" if index == 0 else f"img3d/trail{index}.tga"),
                       "Original input paths and ordinal frame order are fixed.")
        data = assets.read_file(original_root, pin["path"], maximum=65536)
        identity = assets.fingerprint(data)
        assets.require(all(identity[k] == pin[k] for k in ("bytes", "sha256", "gitBlobSha1")),
                       "Wrong original input pin: " + pin["path"])
        relative = "inputs/" + pin["path"] + ".bytes"
        payloads[relative] = data
        (dust_inputs if dust else inputs).append(dict(pin, payloadPath=relative))
        if not dust and index == 0:
            payloads["geometry.bytes"] = geometry_from_sdl(data)
        else:
            decoded = assets.decode_tga(tga_pixel_view(data, dust), pin["path"])
            assets.require(decoded["width"] == decoded["height"] == 32 and decoded["sourceFormat"] == "TGA32"
                           and data[17] == 8, "Original TRAIL/DIRT dimensions, alpha or bottom-left origin changed.")
            (dust_frames if dust else frames).append({"inputIndex": index, "width": decoded["width"], "height": decoded["height"],
                           "bitsPerPixel": 32, "alphaBits": 8, "topOrigin": False, "rightOrigin": False,
                           "integerAspect": 3 + decoded["height"].bit_length() - decoded["width"].bit_length(),
                           "aspectOrigin": ASPECT_ORIGIN})
            if dust:
                dust_frames[-1]["nonPixelDataOrigin"] = DUST_METADATA_ORIGIN
    manifest = {"schema": DUST_SCHEMA if with_dust else SCHEMA, "stableId": "source-fire-spfx-authoring-tga-sdl-v1",
                "sourceRepository": "historicalsource/nflblitz2000", "sourceCommit": assets.SOURCE_COMMIT,
                "sourceTree": "a7e0352125d46075b7d98e87253ab89b9160a4c7", "toolsCommit": assets.TOOLS_COMMIT,
                "reuseAuthorizationConfirmed": True, "authorizationReference": authorization_reference,
                "inputOrigin": ORIGIN, "nativeConvertedTextureEquivalent": False, "generatedSpfxHeaderEquivalent": False,
                "sourceGeometryFactor": 4.0, "uvOrigin": UV_ORIGIN, "meshPath": "geometry.bytes",
                "sourceSdlVersion": "7.51", "geometryParser": "existing-cv-st-triangle-reader-with-explicit-7.51-header-adapter",
                "meshBytes": len(payloads["geometry.bytes"]), "meshSha256": assets.digest(payloads["geometry.bytes"]),
                "inputs": inputs, "frames": frames}
    if with_dust:
        manifest.update(dustInputs=dust_inputs, dustFrames=dust_frames)
    output.mkdir()
    for relative, data in payloads.items():
        path = output / assets.portable(relative)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
    raw = assets.json_bytes(manifest)
    (output / "manifest.json").write_bytes(raw)
    return {"manifestSha256": assets.digest(raw), "inputs": len(inputs) + len(dust_inputs),
            "frames": len(frames), "dustFrames": len(dust_frames),
            "inputOrigin": ORIGIN, "nativeConvertedTextureEquivalent": False, "generatedSpfxHeaderEquivalent": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--authorization-reference", required=True)
    parser.add_argument("--with-dust", action="store_true", help="Explicit v2 migration retaining the v1 SDL/17-TRAIL pins plus all ten DIRT frames.")
    args = parser.parse_args()
    print(json.dumps(prepare(args.original_root, args.output, args.authorization_reference, args.with_dust), indent=2))


if __name__ == "__main__":
    main()
