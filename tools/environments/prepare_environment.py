#!/usr/bin/env python3
"""Offline conversion of the active, pinned Alias SDL field2/stadpie environment.

Only explicitly supplied local inputs are read. Nothing is downloaded. Original
art/derivatives must stay in ignored storage; see --help and environment_inputs.json.
"""

import argparse
from collections import Counter
from dataclasses import dataclass
import hashlib
import json
import math
import os
from pathlib import Path
import re
import stat
import struct
import sys


SOURCE_COMMIT = "89f840d58d152a2a9f230cb8d0cdffbd554c8ded"
TOOLS_COMMIT = "1792293aaaea00a919b59e8c05716978e85b4333"
SCHEMA = "blitz-source-environment/v1"
MESH_MAGIC = b"BZENM001"
FLOAT = r"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?"
NAME = r"[a-zA-Z0-9_#]+"
FIELD_BRANCHES = {
    "grass": (0, None), "old-grass": (1, None),
    "turf": (2, "astro_turf_field_textures"),
    "dirt": (3, "dirt_field_textures"),
    "snow": (4, "snow_field_textures"),
    "asphalt": (5, "asphalt_field_textures"),
    "mud": (6, "mud_field_textures"),
}
STADIUM_BRANCHES = {
    "day": (0, "day_stadium_textures"),
    "night": (1, None), "snow": (2, "snow_stadium_textures"),
    "none": (3, None),
}
STATE_FIELDS = (
    "limb", "modeChangeFlags", "depthMode", "depthFunction", "depthMask",
    "depthBias", "colorFunction", "alphaSource", "textureFunction", "clampMode",
    "rgbSource", "rgbDestination", "alphaSourceBlend", "alphaDestinationBlend",
    "chromaMode", "chromaValue", "alphaFunction", "alphaValue", "cullMode",
    "zone1", "zone2",
)


class EnvironmentError(ValueError):
    """An identified missing, corrupt, or unsupported original asset."""


def require(condition, message):
    if not condition:
        raise EnvironmentError(message)


def f32(value):
    try:
        result = struct.unpack("<f", struct.pack("<f", value))[0]
    except (OverflowError, struct.error) as error:
        raise EnvironmentError("SDL scalar exceeds IEEE binary32.") from error
    require(math.isfinite(result), "Nonfinite SDL scalar.")
    return result


def printed_float(value):
    # cvtmod prints %f (six decimal places), then the generated C compiler reads float.
    return f32(float(format(f32(value), ".6f")))


def digest(data):
    return hashlib.sha256(data).hexdigest()


def fingerprint(data):
    return {"bytes": len(data), "sha256": digest(data),
            "gitBlobSha1": hashlib.sha1(b"blob " + str(len(data)).encode("ascii")
                                       + b"\0" + data).hexdigest()}


def json_bytes(value):
    return (json.dumps(value, indent=2, ensure_ascii=True, allow_nan=False) + "\n").encode("ascii")


def portable(value):
    require(isinstance(value, str) and 0 < len(value) <= 512 and not value.startswith("/"),
            "Expected a portable source-relative path.")
    for part in value.split("/"):
        require(re.fullmatch(r"[A-Za-z0-9_.-]{1,128}", part) is not None
                and part not in (".", "..") and not part.endswith("."),
                "Unsafe relative asset path: " + value)
        stem = part.split(".")[0].upper()
        require(stem not in ("CON", "PRN", "AUX", "NUL")
                and re.fullmatch(r"(?:COM|LPT)[1-9]", stem) is None,
                "Reserved path alias: " + value)
    return value


def physical(path, directory=False):
    path = Path(os.path.abspath(path))
    current = Path(path.anchor)
    for part in path.parts[1:]:
        current /= part
        require(not stat.S_ISLNK(current.lstat().st_mode), "Symlink asset path: " + current.name)
    require(stat.S_ISDIR(path.stat().st_mode) if directory else stat.S_ISREG(path.stat().st_mode),
            "Wrong asset path type: " + path.name)
    return path


def read_file(root, relative, maximum=32 * 1024 * 1024):
    portable(relative)
    try:
        path = physical(root / relative)
        require(path.stat().st_size <= maximum, "Oversized input: " + relative)
        with path.open("rb") as stream:
            data = stream.read(maximum + 1)
        require(len(data) <= maximum, "Oversized input: " + relative)
        return data
    except FileNotFoundError as error:
        raise EnvironmentError("Missing required original input: " + relative) from error


def uncomment(text):
    # Preserve quoted image filenames and line boundaries while removing SDL/C comments.
    return re.sub(r'"(?:\\.|[^"\\])*"|/\*.*?\*/|//[^\n]*',
                  lambda m: m[0] if m[0].startswith('"') else "\n" * m[0].count("\n"),
                  text, flags=re.S)


def balanced(text, start):
    require(text[start] == "(", "Expected an SDL opening parenthesis.")
    depth, quoted, escaped = 0, False, False
    for index in range(start, len(text)):
        c = text[index]
        if quoted:
            if escaped:
                escaped = False
            elif c == "\\":
                escaped = True
            elif c == '"':
                quoted = False
        elif c == '"':
            quoted = True
        elif c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return text[start + 1:index], index + 1
    raise EnvironmentError("Unterminated SDL declaration.")


def declarations(text, kind):
    result = []
    for match in re.finditer(r"^" + kind + r"\s+(" + NAME + r")\s*\(", text, re.M):
        body, end = balanced(text, text.index("(", match.start()))
        require(text[end:].lstrip().startswith(";"), "Unterminated SDL " + kind + ": " + match[1])
        require(all(name != match[1] for name, _ in result),
                "Duplicate SDL " + kind + ": " + match[1])
        result.append((match[1], body))
    return result


@dataclass
class Texture:
    name: str
    file: str
    repeat: tuple
    offset: tuple
    rotation: float


@dataclass
class Polyset:
    name: str
    positions: list
    uvs: list
    triangles: list
    shaders: list
    flags: dict


@dataclass
class Scene:
    name: str
    textures: dict
    shaders: dict
    polysets: list
    slots: list


def texture_scalar(body, name, default):
    match = re.search(r"\b" + name + r"\s*=\s*(" + FLOAT + r")\s*,", body)
    if match is None:
        return f32(default)
    # CVTMOD.L:get_tex_parm advances to the first digit, discarding a leading sign.
    return f32(float(re.sub(r"^[^0-9]*", "", match[1])))


def read_sdl(data, label, scale=(65.0, 65.0, 57.5)):
    try:
        text = uncomment(data.decode("ascii"))
    except UnicodeDecodeError as error:
        raise EnvironmentError(label + ": SDL must be ASCII.") from error
    require(re.search(r"\bversion\s*=\s*8\.2\s*;", text) is not None,
            label + ": expected the original Alias SDL 8.2 schema.")
    textures = {}
    for name, body in declarations(text, "texture"):
        image = re.search(r'\bimage\s*=\s*"([^"]+)"\s*,', body)
        procedure = re.search(r"\bprocedure\s*=\s*(" + NAME + ")", body)
        if image is None:
            continue  # Original non-file/projection textures cannot become referenced materials.
        require(procedure is not None and procedure[1] == "file",
                label + ": unsupported SDL texture procedure: " + name)
        filename = image[1].rsplit("/", 1)[-1]
        require(re.fullmatch(r"[A-Za-z0-9_.-]+", filename) is not None,
                label + ": unsupported texture filename: " + name)
        textures[name] = Texture(name, filename,
                                 tuple(texture_scalar(body, p, 0) for p in ("urepeat", "vrepeat")),
                                 tuple(texture_scalar(body, p, 0) for p in ("uoffset", "voffset")),
                                 texture_scalar(body, "rotate", 0))
    shaders = {}
    for name, body in declarations(text, "shader"):
        color = re.search(r"\bcolor\s*=\s*(" + NAME + r")\s*,", body)
        shaders[name] = color[1] if color else None
    polysets = []
    references = Counter()
    for name, body in declarations(text, "polyset"):
        positions = [tuple(printed_float(f32(f32(float(p)) * f32(scale[i])))
                           for i, p in enumerate(match))
                     for match in re.findall(r"cv\(\(\s*(" + FLOAT + r"),\s*(" + FLOAT
                                             + r"),\s*(" + FLOAT + r")\s*\)", body)]
        uvs = [tuple(f32(float(p)) for p in match)
               for match in re.findall(r"\bst\(\s*(" + FLOAT + r"),\s*(" + FLOAT + r")\s*\)", body)]
        require(0 < len(positions) <= 10240 and 0 < len(uvs) <= 30720,
                label + ": invalid vertex/UV count for " + name)
        bindings = re.findall(r"\bshader\s*=\s*\(\s*(" + NAME + r")\s*\)", body)
        require(0 < len(bindings) <= 128, label + ": missing/oversized shader list: " + name)
        for shader in bindings:
            require(shader in shaders and shaders[shader] in textures,
                    label + ": missing textured shader identity: " + name + "/" + shader)
        triangles = []
        for match in re.finditer(r"\bpolygon\s*\(", body):
            polygon, _ = balanced(body, body.index("(", match.start()))
            groups = re.findall(r"\(([^()]+)\)", polygon)
            require(len(groups) in (2, 3), label + ": unsupported polygon tuple count: " + name)
            indices = []
            for group in groups:
                require(re.fullmatch(r"\s*\d+\s*,\s*\d+\s*,\s*\d+\s*", group) is not None,
                        label + ": only original triangular polygons are supported: " + name)
                indices.append(tuple(int(p) for p in group.split(",")))
            selector = re.search(r",\s*(\d+)\s*$", polygon)
            require(selector is not None, label + ": missing polygon shader index: " + name)
            shader = int(selector[1])
            require(shader < len(bindings), label + ": shader index out of range: " + name)
            require(all(v < len(positions) for v in indices[0]),
                    label + ": vertex index out of range: " + name)
            require(all(v < len(uvs) for v in indices[1]),
                    label + ": texture index out of range: " + name)
            triangles.append((indices[0], indices[1], shader))
            references[shaders[bindings[shader]]] += 1
        require(0 < len(triangles) <= 40960, label + ": invalid polygon count: " + name)
        require(len(re.findall(r"\bcv\s*\(", body)) == len(positions)
                and len(re.findall(r"\bst\s*\(", body)) == len(uvs),
                label + ": unsupported vertex or UV syntax: " + name)
        flags = {flag: (re.search(r"\b" + flag + r"\s*=\s*(" + NAME + ")", body)[1]
                        if re.search(r"\b" + flag + r"\s*=\s*(" + NAME + ")", body) else "")
                 for flag in ("active", "smooth", "doublesided", "opposite")}
        polysets.append(Polyset(name, positions, uvs, triangles, bindings, flags))
    require(0 < len(polysets) <= 1024, label + ": invalid polyset count.")
    # Original strcmp order is case-sensitive; repeated filenames remain distinct texture slots.
    slots = sorted((name for name in textures if references[name]), key=lambda n: textures[n].file)
    return Scene(Path(label).stem, textures, shaders, polysets, slots)


def bounds(points):
    return ([min(p[i] for p in points) for i in range(3)]
            + [max(p[i] for p in points) for i in range(3)])


def expanded_triangles(scene, polyset):
    for vertices, uv_indices, shader_index in sorted(polyset.triangles, key=lambda tri: tri[2]):
        texture_name = scene.shaders[polyset.shaders[shader_index]]
        texture = scene.textures[texture_name]
        corners = []
        for vertex, uv_index in zip(vertices, uv_indices):
            st = polyset.uvs[uv_index]
            s, t = (f32(f32(st[i] * texture.repeat[i]) + texture.offset[i]) for i in range(2))
            generated_st = (printed_float(s), printed_float(f32(1.0 - t)))
            corners.append((vertex, generated_st))
        yield texture_name, corners


def mesh_bytes(scene, polyset, slot_base):
    vertices, split, submeshes = [], {}, {}
    for texture_name, corners in expanded_triangles(scene, polyset):
        slot = slot_base + scene.slots.index(texture_name)
        indices = submeshes.setdefault(slot, [])
        for vertex, generated_st in corners:
            key = (vertex, generated_st, slot)
            if key not in split:
                split[key] = len(vertices)
                x, y, z = polyset.positions[vertex]
                # Renderer321 XOR the common actor (+X,+Y,-Z) reflection = declared123.
                vertices.append((x, y, f32(-z), generated_st[0], f32(1.0 - generated_st[1])))
            indices.append(split[key])
    data = bytearray(MESH_MAGIC + struct.pack("<II", len(vertices), len(submeshes)))
    for vertex in vertices:
        data.extend(struct.pack("<5f", *vertex))
    for slot, indices in submeshes.items():
        data.extend(struct.pack("<II", slot, len(indices)))
        data.extend(struct.pack("<" + "I" * len(indices), *indices))
    return bytes(data), len(vertices), len(submeshes)


def array_body(text, name):
    matches = re.findall(r"\b" + re.escape(name) + r"\s*\[\s*\]\s*=\s*\{(.*?)\};", text, re.S)
    require(len(matches) == 1, "Missing/ambiguous original source array: " + name)
    return matches[0]


def string_array(text, name):
    body = array_body(text, name)
    require(re.fullmatch(r'(?:\s*"[^"]+"\s*,)*\s*NULL\s*', body) is not None,
            "Unsupported original selector array: " + name)
    return re.findall(r'"([^"]+)"', body)


def draw_table(text, name, scene_name, render_pass):
    body = array_body(text, name)
    result = []
    for entry in re.findall(r"\{([^{}]+)\}", body):
        values = [p.strip() for p in entry.strip().rstrip(",").split(",")]
        require(len(values) == len(STATE_FIELDS), "Unsupported original draw record: " + name)
        state = dict(zip(STATE_FIELDS, values))
        require(state["limb"].startswith("&limb_"), "Missing original draw limb: " + name)
        limb = state.pop("limb")[6:]
        state.pop("modeChangeFlags")
        for key in ("depthBias", "chromaValue", "alphaValue"):
            state[key] = int(state[key], 0)
        for key in ("zone1", "zone2"):
            zone = state[key]
            require(zone == "-1" or re.fullmatch(r"ZONE[123][A-F]", zone),
                    "Unknown original draw-zone identity: " + zone)
            state[key] = -1 if zone == "-1" else (ord(zone[-1]) - ord("A")) * 3 + int(zone[-2]) - 1
        result.append({"sourceSet": scene_name, "name": limb, "pass": render_pass,
                       "sourceTable": name, "sourceOrder": len(result), "state": state})
    require(result, "Empty original draw table: " + name)
    return result


def bottom_first_rgba(width, height, pixels, top_first, right_first=False):
    rows = [pixels[y * width * 4:(y + 1) * width * 4] for y in range(height)]
    if right_first:
        rows = [b"".join(row[x * 4:x * 4 + 4] for x in range(width - 1, -1, -1)) for row in rows]
    return b"".join(reversed(rows) if top_first else rows)


def decode_tga(data, identity):
    require(len(data) >= 18, identity + ": truncated TGA header.")
    ident, cmap, kind, first, count, depth, x, y, width, height, bpp, desc = struct.unpack_from("<BBBHHBHHHHBB", data)
    require(cmap == first == count == depth == x == y == 0 and kind == 2 and bpp in (24, 32),
            identity + ": unsupported TGA; require uncompressed true-color 24/32-bit, no palette.")
    require(0 < width <= 4096 and 0 < height <= 4096 and desc & 0xC0 == 0
            and desc & 15 == (8 if bpp == 32 else 0),
            identity + ": unsupported TGA dimensions/attribute/interleave flags.")
    offset, size = 18 + ident, width * height * (bpp // 8)
    end = offset + size
    require(end <= len(data) and data[end:] in (b"", b"\0" * 8 + b"TRUEVISION-XFILE.\0"),
            identity + ": truncated TGA pixels or unsupported extension/trailing bytes.")
    rgba = bytearray()
    for pixel in range(width * height):
        p = offset + pixel * (bpp // 8)
        rgba.extend((data[p + 2], data[p + 1], data[p], data[p + 3] if bpp == 32 else 255))
    return {"width": width, "height": height, "sourceFormat": "TGA" + str(bpp),
            "sourceHeader": [kind, bpp, desc], "filterType": 1, "lodBias": 0.0, "trilinear": False,
            "mips": [bottom_first_rgba(width, height, bytes(rgba), bool(desc & 32), bool(desc & 16))]}


def decode_wms(data, identity):
    require(len(data) >= 40, identity + ": truncated 40-byte WMS texture_hdr_t.")
    version, bias, filtering, trilinear, width, height, small, large, aspect, fmt = struct.unpack_from("<If8I", data)
    require(version == 0x8005 and math.isfinite(bias) and filtering in (0, 1, 2) and trilinear in (0, 1),
            identity + ": unsupported WMS version/filter/trilinear flags.")
    require(0 <= large <= small <= 8 and all(0 < x <= 256 and x & (x - 1) == 0 for x in (width, height))
            and max(width, height) == 256 >> large,
            identity + ": inconsistent WMS dimensions/LOD range.")
    ratio = math.log2(height / width)
    require(ratio.is_integer() and -3 <= ratio <= 3 and aspect == int(ratio) + 3,
            identity + ": inconsistent WMS aspect ratio.")
    formats = {10: "RGB565", 11: "ARGB1555", 12: "ARGB4444"}
    require(fmt in formats, identity + ": unsupported WMS format " + str(fmt)
            + " (only verified RGB565/ARGB1555/ARGB4444; palettes/NCC/other channels are not guessed).")
    offset, mips = 40, []
    for level in range(small - large + 1):
        w, h = max(1, width >> level), max(1, height >> level)
        byte_depth = 2
        end = offset + w * h * byte_depth
        require(end <= len(data), identity + ": truncated WMS mip " + str(large + level) + ".")
        rgba = bytearray()
        for p in range(offset, end, byte_depth):
            word = struct.unpack_from("<H", data, p)[0]
            a = 255
            if fmt == 10:
                r5, g6, b5 = (word >> 11) & 31, (word >> 5) & 63, word & 31
                r, g, b = (r5 << 3) | (r5 >> 2), (g6 << 2) | (g6 >> 4), (b5 << 3) | (b5 >> 2)
            elif fmt == 11:
                r5, g5, b5 = (word >> 10) & 31, (word >> 5) & 31, word & 31
                r, g, b = (r5 << 3) | (r5 >> 2), (g5 << 3) | (g5 >> 2), (b5 << 3) | (b5 >> 2)
                a = 255 if word & 0x8000 else 0
            else:
                a, r, g, b = ((word >> s) & 15 for s in (12, 8, 4, 0))
                a, r, g, b = (v * 17 for v in (a, r, g, b))
            rgba.extend((r, g, b, a))
        mips.append(bottom_first_rgba(w, h, bytes(rgba), True))
        offset = end
    require(offset == len(data), identity + ": trailing/mismatched WMS mip data.")
    return {"width": width, "height": height, "sourceFormat": "WMS/" + formats[fmt],
            "sourceHeader": list(struct.unpack_from("<10I", data)), "filterType": filtering,
            "lodBias": bias, "trilinear": bool(trilinear), "mips": mips}


def load_recipe(project):
    recipe = json.loads(read_file(project, "tools/environments/environment_inputs.json", 256 * 1024))
    require(recipe["schemaVersion"] == 1 and recipe["sourceCommit"] == SOURCE_COMMIT
            and recipe["toolsCommit"] == TOOLS_COMMIT, "Unsupported environment input recipe/pin.")
    return recipe


def prepare(source_root, recipe, source_commit, field="grass", stadium="day", home_prefix="BEA",
            authorization_reference="owner-directed-local-use"):
    require(source_commit == SOURCE_COMMIT and recipe["sourceCommit"] == source_commit,
            "Source commit mismatch; expected pinned " + SOURCE_COMMIT + ".")
    require(field in FIELD_BRANCHES and stadium in STADIUM_BRANCHES, "Unsupported original field/stadium selector.")
    require(re.fullmatch(r"[A-Z]{3}", home_prefix) is not None, "Home prefix must be an original three-letter team prefix.")
    require(home_prefix == "BEA", "Unsupported home-prefix in this pinned environment recipe: " + home_prefix
            + ". Only BEA endzones are pinned here; this is not a claim that other original art is missing.")
    require(re.fullmatch(r"[A-Za-z0-9_. -]{1,160}", authorization_reference) is not None,
            "Supply a short authorization reference, not a machine path or secret.")
    source_root = physical(source_root, True)
    inventory = {e["path"]: e for e in recipe["inputs"]}
    texture_inventory = {e["path"]: e for e in recipe["textures"]}
    source_records, cached = [], {}

    def original(path):
        if path not in cached:
            specification = inventory.get(path) or texture_inventory.get(path)
            require(specification is not None, "Input is not in the pinned environment recipe: " + path)
            data = read_file(source_root, path, specification["bytes"])
            actual = fingerprint(data)
            require(actual["bytes"] == specification["bytes"]
                    and actual["gitBlobSha1"] == specification["gitBlobSha1"],
                    "Pinned original Git blob/length mismatch: " + path)
            if "sha256" in specification:
                require(actual["sha256"] == specification["sha256"], "Pinned SHA256 mismatch: " + path)
            source_records.append(dict(actual, path=path))
            cached[path] = data
        return cached[path]

    for entry in recipe["inputs"]:
        original(entry["path"])
    field_source = uncomment(original("field.c").decode("ascii"))
    stadium_source = uncomment(original("include/nstadlim.h").decode("ascii"))
    scenes = [read_sdl(original("models/field2.sdl"), "field2"),
              read_sdl(original("models/stadpie.sdl"), "stadpie")]
    draws = draw_table(field_source, "field2_limb_draw_info", "field2", "field")
    if stadium != "none":
        draws += draw_table(stadium_source, "stadium2_limb_draw_info", "stadpie", "stadium")
    draws += draw_table(field_source, "field2_limb_draw_info2", "field2", "field2")
    draws += draw_table(field_source, "goalposts2_limb_draw_info", "field2", "goalposts")
    drawn = {(entry["sourceSet"], entry["name"]) for entry in draws}
    slots, texture_specs, texture_indices, files, mesh_specs = [], [], {}, {}, []
    slot_base = 0
    for scene in scenes:
        table = FIELD_BRANCHES[field][1] if scene.name == "field2" else STADIUM_BRANCHES[stadium][1]
        originals = [re.sub(r"\.[^.]{3}$", ".wms", scene.textures[n].file) for n in scene.slots]
        selected = string_array(field_source, table) if table else originals
        require(len(selected) == len(originals), "Selector/material count mismatch: " + str(table))
        for local_slot, (texture_name, selected_name) in enumerate(zip(scene.slots, selected)):
            authored_name = originals[local_slot]
            if scene.name == "field2" and authored_name.lower() in ("beabg.wms", "beaez1.wms", "beaez2.wms", "beaez3.wms"):
                selected_name = home_prefix + selected_name[3:]
            matches = [e for e in recipe["textures"] if Path(e["path"]).name.lower() == selected_name.lower()]
            tga_name = re.sub(r"\.wms$", ".tga", selected_name, flags=re.I)
            # Exact runtime WMS first, exact authoring basename second. Neither a palette nor art fallback.
            if not matches:
                matches = [e for e in recipe["textures"] if Path(e["path"]).name.lower() == tga_name.lower()]
            require(len(matches) == 1, "Missing/ambiguous pinned original texture: " + scene.name + "/"
                    + authored_name + " -> " + selected_name + " (authoring " + tga_name + ").")
            path = matches[0]["path"]
            if scene.name == "stadpie" and stadium == "none":
                slots.append({"sourceSet": scene.name, "sourceTexture": texture_name,
                              "authoredName": authored_name, "selectedName": selected_name, "textureIndex": -1})
                continue
            if path not in texture_indices:
                data = original(path)
                decoded = decode_wms(data, path) if path.lower().endswith(".wms") else decode_tga(data, path)
                rgba = b"".join(decoded.pop("mips"))
                mip_count = 1 if not path.lower().endswith(".wms") else decoded["sourceHeader"][6] - decoded["sourceHeader"][7] + 1
                relative = "textures/texture-" + str(len(texture_specs)).zfill(3) + ".rgba"
                files[relative] = rgba
                spec = dict(decoded, sourcePath=path, sourceGitBlobSha1=matches[0]["gitBlobSha1"],
                            sourceSha256=digest(data), rgbaPath=relative, rgbaSha256=digest(rgba),
                            rgbaBytes=len(rgba), mipCount=mip_count,
                            samplingWrap="GR_TEXTURECLAMP_WRAP",
                            hasAlpha=any(rgba[p] != 255 for p in range(3, len(rgba), 4)))
                texture_indices[path] = len(texture_specs)
                texture_specs.append(spec)
            slots.append({"sourceSet": scene.name, "sourceTexture": texture_name, "authoredName": authored_name,
                          "selectedName": selected_name, "textureIndex": texture_indices[path]})
        for polyset in scene.polysets:
            data, vertex_count, submesh_count = mesh_bytes(scene, polyset, slot_base)
            relative = portable("meshes/" + scene.name + "-" + polyset.name + ".meshbin")
            files[relative] = data
            role = "render" if (scene.name, polyset.name) in drawn else (
                "zone-volume" if scene.name == "stadpie" and re.fullmatch(r"box[123][a-f]", polyset.name)
                else "inactive-authoring")
            mesh_specs.append({
                "sourceSet": scene.name, "name": polyset.name, "role": role,
                "sourceVertexCount": len(polyset.positions), "sourceUvCount": len(polyset.uvs),
                "triangleCount": len(polyset.triangles), "vertexCount": vertex_count,
                "submeshCount": submesh_count, "boundsSource": bounds(polyset.positions),
                "materialSlots": list(dict.fromkeys(slot_base + scene.slots.index(texture)
                                                    for texture, _ in expanded_triangles(scene, polyset))),
                "boundsUnity": bounds([(x, y, -z) for x, y, z in polyset.positions]),
                "authoredFlags": polyset.flags, "meshPath": relative, "meshSha256": digest(data),
                "meshBytes": len(data),
            })
        slot_base += len(scene.slots)
    for index, draw in enumerate(draws):
        matches = [i for i, mesh in enumerate(mesh_specs)
                   if mesh["sourceSet"] == draw["sourceSet"] and mesh["name"] == draw["name"]]
        require(len(matches) == 1, "Draw table refers to a missing original polyset: " + draw["name"])
        draw["meshIndex"] = matches[0]
        # FIELD < PLAYER < FIELD2 in include/id.h. All alpha field layers precede actors.
        draw["renderQueue"] = (1800 + index) if draw["pass"] in ("field", "stadium") else 2400 + index
    volumes = []
    for zone in range(18):
        name = "box" + str(zone % 3 + 1) + chr(ord("a") + zone // 3)
        mesh = next((m for m in mesh_specs if m["sourceSet"] == "stadpie" and m["name"] == name), None)
        require(mesh is not None and mesh["role"] == "zone-volume", "Missing original draw-zone volume: " + name)
        volumes.append({"zone": zone, "name": name, "meshIndex": mesh_specs.index(mesh)})
    manifest = {
        "schema": SCHEMA, "sourceRepository": "historicalsource/nflblitz2000",
        "sourceCommit": source_commit, "sourceRootTree": recipe["sourceRootTree"],
        "toolsRepository": "historicalsource/midwayvideo", "toolsCommit": TOOLS_COMMIT,
        "stableId": "source-field2-stadpie", "authorizationReference": authorization_reference,
        "reuseAuthorizationConfirmed": True, "archiveUnitsPerYard": 7.8,
        "unityAxes": "+X,+Y,-Z", "sourceTriangleOrder": "Renderer321", "reverseDeclaredTriangle": False,
        "originSource": [0, 0, 0], "scaleSource": [65, 65, 57.5],
        "fieldSelector": FIELD_BRANCHES[field][0], "stadiumSelector": STADIUM_BRANCHES[stadium][0],
        "homePrefix": home_prefix, "inputs": source_records, "converterEvidence": recipe["converterEvidence"],
        "meshes": mesh_specs, "textures": texture_specs, "slots": slots,
        "draws": draws, "zoneVolumes": volumes,
        "conversionNotes": [
            "Alias SDL 8.2 cv/st/polygon, shader/file references; not OBJ or modern SDL.",
            "cvtmod -f65 -fz57.5; binary32 operations and generated C six-decimal scalar rounding.",
            "MODEL groups/transforms, rotate and authored sidedness are not applied by original cvtmod; geometry is already baked.",
            "Original default local-shader triangle sort, duplicate filename slots, repeat/offset UV transforms retained.",
            "Packed TRI/255 state compression is expanded, not mistaken for strips; no strip flag in active model recipes.",
            "Only runtime draw-table limbs render. Eighteen box polysets are visibility volumes, never opaque stadium art.",
            "pipe3D.c load_textures creates WRAP/WRAP textures; subsequent guTexSource restores these attributes after limb CLAMP state. Out-of-range authored UVs must repeat.",
            "Exact selected authoring TGA pixels or validated per-file WMS mip chain; no synthetic texture/colors.",
            "TGA inputs have one authored level; original texus-generated mip filtering/quantization is not reconstructed.",
            "Original zone IDs/volumes retained; Unity per-piece frustum clipping replaces hardware zone screening.",
            "Goalposts use the original fully opaque constant-alpha state; source gameplay-dependent camera/kickoff fading is pending.",
            "Original atmospheric/fog/hardware lighting, HUD/audio/gameplay and N64 edition identity are not established.",
        ],
        "upstreamNotice": original("field.c").decode("ascii").split("*/", 1)[0] + "*/",
        "publication": "User-directed local use only; original art and converted derivatives are not newly licensed project art.",
    }
    files["environment.json"] = json_bytes(manifest)
    return manifest, files


def write_bundle(project, destination, files):
    project = physical(project, True)
    destination = Path(os.path.abspath(destination))
    try:
        relative = destination.relative_to(project).as_posix()
    except ValueError as error:
        raise EnvironmentError("Destination must be below this project's ignored storage.") from error
    portable(relative)
    require(any(relative.startswith(prefix) for prefix in (".squad/.scratch/", "Build/", "Assets/Imported/")),
            "Destination must be inside ignored .squad/.scratch, Build, or Assets/Imported.")
    require(not destination.exists() and not destination.with_name(destination.name + ".meta").exists(),
            "Destination already exists; choose a new output. Nothing is overwritten.")
    ancestor = destination.parent
    while not ancestor.exists():
        ancestor = ancestor.parent
    physical(ancestor, True)
    destination.mkdir(parents=True, exist_ok=False)
    for relative, data in files.items():
        portable(relative)
        path = destination / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("xb") as stream:
            stream.write(data)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", required=True, type=Path)
    parser.add_argument("--source-commit", required=True, help="Explicit original source commit pin.")
    parser.add_argument("--destination", required=True, type=Path, help="NEW ignored output directory; no overwrite.")
    parser.add_argument("--field", choices=FIELD_BRANCHES, default="grass")
    parser.add_argument("--stadium", choices=STADIUM_BRANCHES, default="day")
    parser.add_argument("--home-prefix", choices=("BEA",), default="BEA",
                        help="This environment recipe pins the original Bears endzone material branch.")
    parser.add_argument("--confirm-local-use", action="store_true", required=True)
    parser.add_argument("--authorization-reference", required=True)
    args = parser.parse_args(argv)
    project = Path(__file__).absolute().parents[2]
    try:
        manifest, files = prepare(args.source_root, load_recipe(project), args.source_commit,
                                  args.field, args.stadium, args.home_prefix, args.authorization_reference)
        write_bundle(project, args.destination, files)
        print(json.dumps({"status": "converted-original-environment", "meshes": len(manifest["meshes"]),
                          "drawnPieces": len(manifest["draws"]), "textures": len(manifest["textures"]),
                          "zoneVolumes": len(manifest["zoneVolumes"]),
                          "manifestSha256": digest(files["environment.json"])}))
        return 0
    except (EnvironmentError, OSError, KeyError, json.JSONDecodeError) as error:
        print("Source environment conversion rejected: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
