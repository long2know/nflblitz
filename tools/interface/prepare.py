#!/usr/bin/env python3
"""Prepare pinned original cabinet/HUD data offline; generated art never belongs in Git."""
import argparse
from dataclasses import dataclass
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/environments"))
import prepare_environment as assets

SCHEMA = "original-source-interface/v1"
FONT_NAMES = ("bast8t", "bast10", "bast13", "bast18", "bast25", "clock12")


class InterfaceError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise InterfaceError(message)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def blob(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()


def integer(text):
    text = text.strip().rstrip("fF")
    require(re.fullmatch(r"-?(?:0[xX][0-9a-fA-F]+|\d+)", text) is not None,
            "Unsupported original integer expression: " + text)
    return -int(text[1:], 0) if text.startswith("-0x") else int(text, 16 if text.lower().startswith("0x") else 10)


def array(text, name):
    text = assets.uncomment(text)
    match = re.search(r"\b" + re.escape(name) + r"\s*(?:\[[^\]]*\])+\s*=\s*\{(.*?)\};", text, re.S)
    require(match is not None, "Missing original array: " + name)
    return match[1]


def int_array(text, name, count, minimum, maximum):
    body = array(text, name)
    result = [integer(value) for value in body.split(",") if value.strip()]
    require(len(result) == count and all(minimum <= v <= maximum for v in result),
            "Original table shape/range changed: " + name)
    return result


@dataclass
class Image:
    symbol: str
    width: int
    height: int
    ax: int
    ay: int
    rgba: bytes
    source: str


def wif_images(data, identity):
    """Strict observed WIFF IMG/PAL layout, guarded by full original input pins."""
    require(len(data) >= 8 and data[:4] == b"WIFF"
            and int.from_bytes(data[4:8], "big") == len(data) - 8, identity + ": invalid WIFF length.")
    position, images, palettes = 8, [], {}
    while position < len(data):
        require(position + 8 <= len(data), identity + ": truncated WIFF chunk.")
        tag, size = data[position:position + 4], int.from_bytes(data[position + 4:position + 8], "big")
        start, end = position + 8, position + 8 + size
        require(end + (size & 1) <= len(data), identity + ": truncated WIFF payload/padding.")
        body = data[start:end]
        if tag == b"IMG ":
            require(size >= 56, identity + ": truncated IMG metadata.")
            name_bytes = body[8:24].split(b"\0", 1)[0]
            require(re.fullmatch(rb"[!A-Za-z0-9_-]+", name_bytes) is not None, identity + ": invalid IMG identity.")
            name = name_bytes.decode("ascii")
            width, height, bits = struct.unpack_from("<HHH", body, 32)
            ax, ay = struct.unpack_from("<hh", body, 46)
            require(0 < width <= 2048 and 0 < height <= 2048 and bits in (8, 24, 32)
                    and size == 56 + width * height * (bits // 8), identity + ": unsupported IMG payload: " + name)
            images.append((name, width, height, bits, ax, ay, int.from_bytes(body[4:8], "big"), body[56:]))
        elif tag == b"PAL ":
            require(size >= 36, identity + ": truncated PAL metadata.")
            key = int.from_bytes(body[:4], "big")
            count, depth, first, reserved = struct.unpack_from("<4H", body, 28)
            require(key not in palettes and 0 < count <= 256 and 1 <= depth <= 8 and first == reserved == 0
                    and size == 36 + count * 4, identity + ": unsupported/duplicate PAL.")
            palettes[key] = [tuple(body[i + 1:i + 4]) for i in range(36, size, 4)]
        else:
            require(tag == b"ORIG", identity + ": unsupported WIFF chunk " + repr(tag))
        position = end + (size & 1)
    require(position == len(data) and images, identity + ": empty or malformed WIFF.")
    result = {}
    for name, width, height, bits, ax, ay, palette, pixels in images:
        if name.startswith("!"):
            continue  # Unselected authoring drafts cannot be C/LOD image symbols and may repeat names.
        symbol = name.lower()
        require(symbol not in result, identity + ": ambiguous image identity: " + name)
        if bits == 32:
            rgba = pixels  # Authored WIFF uses RGBA, unlike the original TGA BGR(A).
        elif bits == 24:
            rgba = b"".join(pixels[i:i + 3] + b"\xff" for i in range(0, len(pixels), 3))
        else:
            require(palette in palettes, identity + ": missing exact palette for " + name)
            colors = palettes[palette]
            require(max(pixels) < len(colors), identity + ": pixel outside its actual palette: " + name)
            # The active sysfont recipe is RGB332 with Goose black chromakey.
            rgba = b"".join(bytes(colors[index]) + (b"\0" if colors[index] == (0, 0, 0) else b"\xff")
                            for index in pixels)
        result[symbol] = Image(symbol, width, height, ax, ay, rgba, identity)
    return result


def fonts(text, images):
    text = assets.uncomment(text)
    result = []
    for name in FONT_NAMES:
        table = array(text, name + "_font_table")
        entries = []
        for entry in table.split(","):
            if not entry.strip():
                continue
            match = re.fullmatch(r"\s*&([A-Za-z_][A-Za-z0-9_]*)\s*", entry)
            require(match is not None or re.fullmatch(r"\s*\(void\s*\*\)\s*0\s*", entry) is not None,
                    "Unsupported original font table entry: " + entry)
            entries.append(match[1] if match else None)
        definition = re.search(r"\bfont_info_t\s+" + name + r"_font\s*=\s*\{(.*?)\};", text, re.S)
        require(definition is not None, "Missing original font definition: " + name)
        fields = [part.strip() for part in definition[1].split(",") if part.strip()]
        require(len(fields) == 9, "Original font shape changed: " + name)
        start, end, height, space, spacing = (integer(field) for field in fields[1:6])
        require(len(entries) == end - start + 1, "Original font character count mismatch: " + name)
        glyphs = []
        for index, symbol in enumerate(entries):
            if symbol is None:
                continue
            require(symbol in images, "Missing actual original glyph image: " + name + "/" + symbol)
            glyphs.append([start + index, symbol])
        result.append({"id": name, "height": height, "space": space, "spacing": spacing, "glyphs": glyphs})
    return result


def slots_and_pages(text):
    clean = assets.uncomment(text)
    defines = {name: integer(value) for name, value in re.findall(r"^\s*#define\s+(\w+)\s+(-?(?:0x[0-9a-fA-F]+|\d+))", clean, re.M)}
    row = re.compile(r'\{\s*"([^"]+)"\s*,\s*(?:&(\w+)|NULL)\s*,\s*\w+\s*,\s*\{\s*(?:&(\w+)|NULL)\s*,\s*(?:&(\w+)|NULL)\s*\}\s*\}')
    tables = {}
    for table in ("off_slots", "def_slots", "extra_slots", "team_slots"):
        tables[table] = [{"name": m[1], "play": m[2], "normal": m[3], "flipped": m[4]} for m in row.finditer(array(clean, table))]
    require(len(tables["off_slots"]) == 41 and len(tables["def_slots"]) == 22
            and len(tables["team_slots"]) == 93 and len(tables["extra_slots"]) == 1,
            "Original play-slot cardinality changed.")
    references = re.findall(r"&(\w+)\[(\w+)\]", array(clean, "play_pages"))
    require(len(references) == 63, "Original standard page cardinality changed.")
    pages = []
    for side, first, count in (("offense", 0, 45), ("defense", 45, 18)):
        for index in range(count // 9):
            entries = []
            for table, key in references[first + index * 9:first + (index + 1) * 9]:
                require(table in tables and key in defines and defines[key] < len(tables[table]), "Unknown original page slot.")
                entries.append({"table": table, "index": defines[key]})
            pages.append({"side": side, "page": index, "slots": entries})
    return tables, pages


def original_planes(source, tools, work):
    """Run the unchanged blob-pinned cvtmod with the actual models.mak flags."""
    work.mkdir(parents=True, exist_ok=False)
    compat = work / "compat.h"
    compat.write_text("#include <stdlib.h>\n#include <unistd.h>\n#include <math.h>\n"
                      "static double pow2(int exponent) { return ldexp(1.0, exponent); }\n", encoding="ascii")
    executable = work / "original-cvtmod"
    compiler_environment = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work))
    build = subprocess.run(["gcc", "-pipe", "-x", "c", "-std=gnu89", "-O0", "-include", str(compat),
                            str(tools / "tools/cvtmod/CVTMOD.C"), "-lm", "-o", str(executable)],
                           capture_output=True, text=True, timeout=120, env=compiler_environment)
    require(build.returncode == 0, "Pinned original plane compiler failed: " + build.stderr[-4000:])
    process = subprocess.run([str(executable), "-z", "-q", "-name", "plyslct"], input=source,
                             capture_output=True, timeout=120)
    require(process.returncode == 0, "Original play-plane conversion failed: " + process.stderr.decode("ascii", "replace")[-2000:])
    header = process.stdout.decode("ascii")

    def records(kind, symbol, floating):
        match = re.search(r"\b" + kind + r"\s+" + re.escape(symbol) + r"\s*\[\s*\]\s*=\s*\{(.*?)\};", header, re.S)
        require(match is not None, "Original converter omitted " + symbol)
        return [[float(v.strip().rstrip("fF")) if floating else integer(v) for v in entry.split(",")]
                for entry in re.findall(r"\{([^{}]+)\}", match[1])]

    result = []
    for name in re.findall(r"\bLIMB\s+limb_(\w+)\s*=", header):
        positions = records("VERTEX", name + "_vtx", True)
        uvs = records("ST", name + "_st", True)
        triangles = records("TRI", name + "_tris", False)
        require(triangles[-1][-1] == -20, "Original triangle stream missing sentinel: " + name)
        expanded, previous, material = [], {}, None
        for triangle in triangles[:-1]:
            require(len(triangle) == 7, "Original TRI record shape changed.")
            if triangle[6] >= 0:
                require(triangle[6] % 4 == 0, "Invalid original texture selector.")
                material = triangle[6] // 4
            require(material in (0, 1), "Unexpected original play texture selector: " + name)
            points = []
            for packed, st in zip(triangle[:3], triangle[3:6]):
                require(packed % 3 == 0 and packed // 3 < len(positions), "Invalid original packed plane vertex.")
                vertex = packed // 3
                if st != 255:
                    require(st < len(uvs), "Invalid original packed plane UV.")
                    previous[vertex] = uvs[st]
                require(vertex in previous, "Original compressed plane UV has no previous value.")
                points.append(positions[vertex] + previous[vertex])
            expanded.append({"texture": "playsel8" if material == 0 else "psarrow1", "vertices": points})
        result.append({"id": "limb_" + name, "triangles": expanded})
    require(len(result) > 50, "Original play planes are incomplete.")
    return result, sha256(process.stdout)


def pack(images, atlas_width=1024):
    positions, x, y, row_height = [], 1, 1, 0
    ordered = sorted(images.values(), key=lambda im: (-im.height, -im.width, im.symbol))
    for image in ordered:
        require(image.width + 2 <= atlas_width, "Original image exceeds atlas width: " + image.symbol)
        if x + image.width + 1 > atlas_width:
            x, y, row_height = 1, y + row_height + 2, 0
        positions.append((image, x, y))
        x += image.width + 2
        row_height = max(row_height, image.height)
    atlas_height = 1 << (y + row_height + 1).bit_length()
    require(atlas_height <= 4096, "Original interface atlas exceeds bounded size.")
    pixels = bytearray(atlas_width * atlas_height * 4)
    records = []
    for image, left, top in positions:
        bottom = atlas_height - top - image.height
        for row in range(image.height):
            offset = ((atlas_height - 1 - top - row) * atlas_width + left) * 4
            pixels[offset:offset + image.width * 4] = image.rgba[row * image.width * 4:(row + 1) * image.width * 4]
        records.append({"id": image.symbol, "x": left, "y": bottom, "width": image.width, "height": image.height,
                        "anchor_x": image.ax, "anchor_y": image.ay, "source": image.source,
                        "pixels_sha256": sha256(image.rgba)})
    return bytes(pixels), atlas_width, atlas_height, sorted(records, key=lambda r: r["id"])


def prepare(source_root, tools_root, work, pins=None):
    recipe = pins if pins is not None else json.loads((Path(__file__).parent / "pins.json").read_bytes())
    require(recipe["schema"] == "original-interface-inputs/v1", "Unknown interface input recipe.")
    sources, evidence = {}, []
    for group, root in (("source", source_root), ("tools", tools_root)):
        for path, expected in recipe[group].items():
            data = assets.read_file(root, path)
            require(blob(data) == expected, "Pinned original Git blob mismatch: " + group + "/" + path)
            sources[(group, path)] = data
            evidence.append({"repository": group, "path": path, "git_blob": expected, "sha256": sha256(data)})
    text = lambda group, name: sources[(group, name)].decode("ascii")
    switch = text("tools", "lib/goose/switch.c")
    quantization = {
        "raw49": int_array(switch, "xlat_49way", 256, 0, 48),
        "digital8": int_array(switch, "xlat_49to8", 49, 0, 15),
        "directions": int_array(text("source", "player.c"), "dir49_table", 49, -1, 1023)
    }
    all_images = {}
    for path in recipe["source"]:
        if path.endswith(".wif"):
            decoded = wif_images(sources[("source", path)], path)
            require(not (all_images.keys() & decoded.keys()), "Ambiguous original image across WIF inputs.")
            all_images.update(decoded)
    font_records = fonts(text("source", "sysfont.c"), all_images)
    slots, pages = slots_and_pages(text("source", "PlayStuf.c"))
    required = {glyph[1] for font in font_records for glyph in font["glyphs"]}
    required.update(("gameclock", "tb_empty", "tb_blue", "tb_green", "tb_red", "tb_yellow",
                     "arrow_blue", "arrow_green", "arrow_red", "arrow_yellow",
                     "arrow_num1", "arrow_num2", "arrow_num3", "arrow_num4",
                     "big1", "big2", "big3", "big4", "playresult", "play_tab",
                     "k_box", "k_bar", "k_ball", "k_marker", "k_windtext", "k_windarrow", "p_meter", "aud_menu"))
    for symbol in all_images:
        if symbol.startswith("msg_") or symbol.startswith("sp_") or symbol.startswith("firbar"):
            required.add(symbol)
    for table in slots.values():
        for slot in table:
            if slot["normal"] and not slot["normal"].startswith("limb_"):
                required.add(slot["normal"])
    require(required <= all_images.keys(), "Missing referenced original images: " + repr(sorted(required - all_images.keys())))
    rgba, width, height, image_records = pack({name: all_images[name] for name in required})
    planes, header_hash = original_planes(sources[("source", "models/plyslct.sdl")], tools_root, work)
    files = {"interface.rgba": rgba}
    textures = [{"id": "interface", "path": "interface.rgba", "width": width, "height": height,
                 "sha256": sha256(rgba), "filter": "point"}]
    for symbol, path in (("playsel8", "img3d/PLAYSEL8.TGA"), ("psarrow1", "img3d/psarrow1.TGA")):
        decoded = assets.decode_tga(sources[("source", path)], path)
        relative = symbol + ".rgba"
        files[relative] = decoded["mips"][0]
        textures.append({"id": symbol, "path": relative, "width": decoded["width"], "height": decoded["height"],
                         "sha256": sha256(files[relative]), "filter": "bilinear"})
    team_text = text("source", "teamdata.c")
    team_strings = re.findall(r'"([^"]*)"', array(team_text, "teaminfo"))
    require(len(team_strings) == 31 * 4, "Original HUD team abbreviation order/cardinality changed.")
    abbrevs = team_strings[2::4]
    data = {
        "schema": SCHEMA, "source_revision": recipe["source_revision"], "source_tree": recipe["source_tree"],
        "tools_revision": recipe["tools_revision"], "profile": "normal-release-57hz-authored-interface",
        "inputs": evidence, "quantization": quantization, "textures": textures, "images": image_records,
        "fonts": font_records, "slots": slots, "pages": pages, "team_abbreviations": abbrevs,
        "planes": planes, "original_plane_header_sha256": header_hash,
        "qualifications": [
            "Actual authored WIFF glyph extents/anchors/palettes and original sysfont character mapping/spacing; no system font.",
            "Atlas is deterministically repacked from pinned authored pixels, not the missing Composit c.exe generated sysfont.h/plyrinfo.h runtime atlas.",
            "Missing Composit c.exe clipping and Texus RGB332/ARGB4444/ARGB1555 quantization/mips are not claimed bit-identical.",
            "Play planes are generated by unchanged pinned CVTMOD.C with models.mak -z -q; host qsort equal-depth ordering is not MIPS proof.",
            "Analog-to-seven-Hall-level host thresholds are an explicit adapter; Goose raw49/digital8 tables and player direction table are exact.",
            "Original Goose scheduler/libc RNG, cabinet powerups/custom pages and PCM bank playback require separate qualification."
        ]
    }
    files["interface.json"] = assets.json_bytes(data)
    return files


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True, help="Explicit local complete pinned nflblitz2000 archive.")
    parser.add_argument("--tools-root", type=Path, required=True, help="Explicit local pinned midwayvideo archive.")
    parser.add_argument("--output", type=Path, required=True, help="NEW ignored local output; never Assets source or Git art.")
    args = parser.parse_args(argv)
    work = None
    try:
        source, tools = assets.physical(args.source_root, True), assets.physical(args.tools_root, True)
        output = Path(os.path.abspath(args.output))
        try:
            relative = output.relative_to(ROOT)
        except ValueError as error:
            raise InterfaceError("Original interface output must be project-local and ignored.") from error
        assets.portable(relative.as_posix())
        require(subprocess.run(["git", "check-ignore", "-q", str(relative)], cwd=ROOT).returncode == 0,
                "Original interface output must be ignored.")
        parent = ROOT
        for part in relative.parts[:-1]:
            parent /= part
            if parent.exists() or parent.is_symlink():
                assets.physical(parent, True)
            else:
                parent.mkdir()
        work = parent / ("interface-work-" + uuid.uuid4().hex)
        files = prepare(source, tools, work)
        parent = assets.physical(parent, True)
        destination = parent / output.name
        require(not destination.exists() and not destination.is_symlink(), "Choose a NEW interface output; nothing is overwritten.")
        require(not any(p.name.lower() == destination.name.lower() for p in parent.iterdir()),
                "An output casing alias already exists.")
        destination.mkdir()
        for relative, data in files.items():
            assets.portable(relative)
            target = destination / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            with target.open("xb") as stream:
                stream.write(data)
        print(json.dumps({"status": "prepared-original-authored-interface", "output": str(args.output),
                          "manifest_sha256": sha256(files["interface.json"]),
                          "files": len(files), "native_browser_parity_claimed": False}))
        return 0
    except (InterfaceError, assets.EnvironmentError, OSError, ValueError, subprocess.TimeoutExpired) as error:
        print("Original interface preparation rejected: " + str(error), file=sys.stderr)
        return 1
    finally:
        if work is not None and work.exists():
            shutil.rmtree(work)


if __name__ == "__main__":
    raise SystemExit(main())
