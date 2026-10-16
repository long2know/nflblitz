#!/usr/bin/env python3
r"""Offline, pinned MOD_266 / Bears-home / PC_WHITE preparation; Python 3 stdlib only.

Example (from the project root):
  python3 -B tools/assets/prepare_player.py \
    --source-root .squad/.scratch/reference/nflblitz2000 \
    --destination .squad/.scratch/prepared-player \
    --confirm-local-use --authorization-reference owner-directed-local-preview-2026-10-15

The destination is a new directory below this project's ignored .squad/.scratch.
It contains four byte-identical named source files, the decoded atlas, an existing
Unity-importer-compatible request, a material manifest, and provenance/notices.
No network, Unity, Git, source-tree scanning, recoloring, or reconstructed atlas.
Inputs must be trusted and quiescent; these checks are not an OS sandbox against
hostile concurrent filesystem replacement, hard links, or bind mounts.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import stat
import struct


RECIPE_NAME = "player_inputs.json"
REQUEST_NAME = "local-plyr266.import.json"
MATERIALS_NAME = "player-materials.json"
PROVENANCE_NAME = "player-provenance.json"
WMS_HEADER = (32773, 0, 1, 0, 128, 256, 8, 0, 4, 11)
TGA_HEADER = struct.Struct("<BBBHHBHHHHBB")
TGA_FOOTER = b"\0" * 8 + b"TRUEVISION-XFILE.\0"


class PreparationError(ValueError):
    """An explicitly unsupported input or unsafe preparation request."""


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def fingerprint(data):
    return {
        "bytes": len(data),
        "sha256": sha256(data),
        "git_blob_sha1": hashlib.sha1(
            b"blob " + str(len(data)).encode("ascii") + b"\0" + data
        ).hexdigest(),
    }


def json_bytes(value):
    return (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=True) + "\n").encode("ascii")


def path_parts(value, allow_absolute=False):
    raw = os.fspath(value)
    if not raw or any(character in raw for character in ("\\", "\0", ":")):
        raise PreparationError("Use a nonempty portable path without backslashes, NUL or colon.")
    absolute = raw.startswith("/")
    parts = raw[1:].split("/") if absolute else raw.split("/")
    if absolute and not allow_absolute:
        raise PreparationError("Manifest paths must be relative: " + raw)
    if raw == "/" and allow_absolute:
        return ()
    if any(part in ("", ".", "..") or part.endswith((" ", ".")) for part in parts):
        raise PreparationError("Path aliases, traversal and empty components are forbidden: " + raw)
    return tuple(parts)


def absolute_path(value, base):
    path_parts(value, allow_absolute=True)
    path = Path(value)
    return path if path.is_absolute() else base / path


def check_physical(path, directory=False):
    """Inspect every existing component without resolving away a symlink."""
    current = Path(path.anchor)
    for part in path.parts[1:]:
        current = current / part
        entry = current.lstat()
        if stat.S_ISLNK(entry.st_mode):
            raise PreparationError("Symlinks are forbidden: " + str(current))
        if current != path and not stat.S_ISDIR(entry.st_mode):
            raise PreparationError("Not a physical directory: " + str(current))
    if directory and not stat.S_ISDIR(path.lstat().st_mode):
        raise PreparationError("Not a physical directory: " + str(path))


def read_regular(path, byte_limit):
    check_physical(path)
    if not stat.S_ISREG(path.lstat().st_mode):
        raise PreparationError("Only regular input files are supported: " + str(path))
    descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    with os.fdopen(descriptor, "rb") as source:
        info = os.fstat(source.fileno())
        if not stat.S_ISREG(info.st_mode):
            raise PreparationError("Only regular input files are supported: " + str(path))
        if info.st_size > byte_limit:
            raise PreparationError("Input exceeds its pinned byte length: " + str(path))
        data = source.read(byte_limit + 1)
    if len(data) > byte_limit:
        raise PreparationError("Input exceeds its pinned byte length: " + str(path))
    return data


def mip_dimensions(width, height):
    if any(size < 1 or size > 256 or size & (size - 1) for size in (width, height)):
        raise PreparationError("Unsupported WMS dimensions: require powers of two in 1..256.")
    result = [(width, height)]
    while result[-1] != (1, 1):
        width, height = max(1, width // 2), max(1, height // 2)
        result.append((width, height))
    return result


def decode_argb1555_level0(data, expected_dimensions=None):
    """Decode only the validated observed 40-byte-header ARGB1555 structure.

    Small power-of-two dimensions support original synthetic tests, not a claim
    about other historical WMS formats. Every full mip-chain byte is required.
    """
    if len(data) < 40:
        raise PreparationError("Truncated WMS header; expected ten little-endian uint32 fields.")
    header = struct.unpack_from("<10I", data)
    for index, value in enumerate(header):
        if index not in (4, 5) and value != WMS_HEADER[index]:
            raise PreparationError("Unsupported WMS header/format at field " + str(index) + ".")
    width, height = header[4:6]
    levels = mip_dimensions(width, height)
    if expected_dimensions is not None and (width, height) != expected_dimensions:
        raise PreparationError("WMS dimensions differ from the pinned player atlas.")
    expected_bytes = 40 + sum(w * h * 2 for w, h in levels)
    if len(data) != expected_bytes:
        raise PreparationError("WMS must contain exactly the complete mip chain; expected "
                               + str(expected_bytes) + " bytes, got " + str(len(data)) + ".")
    pixels = bytearray(width * height * 4)
    for index, (word,) in enumerate(struct.iter_unpack("<H", data[40:40 + width * height * 2])):
        blue = word & 31
        green = (word >> 5) & 31
        red = (word >> 10) & 31
        alpha = 255 if word & 0x8000 else 0
        pixels[index * 4:index * 4 + 4] = bytes((
            (blue << 3) | (blue >> 2),
            (green << 3) | (green >> 2),
            (red << 3) | (red >> 2),
            alpha,
        ))
    return TGA_HEADER.pack(0, 0, 2, 0, 0, 0, 0, 0, width, height, 32, 0x28) + pixels


def inspect_tga(data, expected=None):
    if len(data) < TGA_HEADER.size:
        raise PreparationError("Truncated TGA header.")
    identifier, palette, kind, first, count, depth, x, y, width, height, bpp, descriptor = (
        TGA_HEADER.unpack_from(data)
    )
    if (identifier, palette, kind, first, count, depth, x, y) != (0, 0, 2, 0, 0, 0, 0, 0):
        raise PreparationError("Unsupported TGA: require uncompressed, non-paletted type 2.")
    if not width or not height or bpp not in (24, 32) or descriptor not in (
        (0, 0x20) if bpp == 24 else (8, 0x28)
    ):
        raise PreparationError("Unsupported TGA dimensions, channels or origin descriptor.")
    end = TGA_HEADER.size + width * height * (bpp // 8)
    if len(data) < end or data[end:] not in (b"", TGA_FOOTER):
        raise PreparationError("TGA pixels truncated or unsupported extension/developer/trailing data.")
    metadata = {"width": width, "height": height, "bits_per_pixel": bpp, "descriptor": descriptor}
    if expected is not None and metadata != expected:
        raise PreparationError("TGA metadata differs from the pinned source.")
    return dict(metadata, image_type=2, origin="top-left" if descriptor & 0x20 else "bottom-left",
                pixel_offset=18, pixel_bytes=end - 18)


def load_recipe(project_root):
    path = project_root / "tools/assets" / RECIPE_NAME
    data = read_regular(path, 65536)
    recipe = json.loads(data)
    if recipe["schema_version"] != 1:
        raise PreparationError("Unsupported preparation recipe schema.")
    return recipe, sha256(data)


def collect_inputs(source_root, recipe):
    inputs = {}
    records = []
    for specification in recipe["inputs"]:
        label = specification["path"]
        path_parts(label)
        if label in inputs:
            raise PreparationError("Duplicate pinned input: " + label)
        try:
            data = read_regular(source_root / label, specification["bytes"])
        except FileNotFoundError as error:
            raise PreparationError("Missing required pinned input: " + label) from error
        actual = fingerprint(data)
        for field in ("bytes", "sha256", "git_blob_sha1"):
            if actual[field] != specification[field]:
                raise PreparationError("Pinned " + field + " mismatch: " + label)
        if "tga" in specification:
            inspect_tga(data, specification["tga"])
        inputs[label] = data
        records.append(dict(actual, path=label, copied=specification["copy"]))
    return inputs, records


def build_bundle(source_root, recipe, recipe_sha, project_root, authorization_reference):
    inputs, records = collect_inputs(source_root, recipe)
    atlas = recipe["atlas"]
    path_parts(atlas["output"])
    original = inputs[atlas["source"]]
    derivative = decode_argb1555_level0(original, (atlas["width"], atlas["height"]))
    levels = mip_dimensions(atlas["width"], atlas["height"])
    if (list(struct.unpack_from("<10I", original)) != atlas["header_uint32_le"]
            or len(levels) != atlas["mip_levels"]
            or len(original) - 40 != atlas["mip_bytes"]):
        raise PreparationError("Atlas header or mip layout differs from the pinned recipe.")
    if len(derivative) != atlas["output_bytes"] or sha256(derivative) != atlas["output_sha256"]:
        raise PreparationError("Decoded atlas does not match the pinned level-0 derivative hash.")
    bundle = {entry["path"]: inputs[entry["path"]] for entry in recipe["inputs"] if entry["copy"]}
    if atlas["output"] in bundle:
        raise PreparationError("Derived atlas would overwrite an original input.")
    bundle[atlas["output"]] = derivative
    request = dict(recipe["request"], reuseAuthorizationConfirmed=True,
                   authorizationReference=authorization_reference)
    for label in (request["modelHeader"], request["skeletonHeader"]):
        path_parts(label)
        if label not in bundle:
            raise PreparationError("Importer source is not in the explicit output bundle: " + label)
    texture_paths = list(dict.fromkeys(entry["texturePath"] for entry in request["materialMap"]))
    textures = []
    for label in texture_paths:
        path_parts(label)
        if label not in bundle:
            raise PreparationError("Mapped texture is not in the explicit output bundle: " + label)
        textures.append(dict(inspect_tga(bundle[label]), path=label, sha256=sha256(bundle[label]),
                             bytes=len(bundle[label]),
                             identity="local-level0-derivative" if label == atlas["output"]
                             else "byte-identical-original-authoring-tga"))
    bundle[REQUEST_NAME] = json_bytes(request)
    bundle[MATERIALS_NAME] = json_bytes({
        "schema_version": 1, "selected_variant": recipe["selected_variant"],
        "request_path": REQUEST_NAME, "ordered_runtime_textures": recipe["ordered_runtime_textures"],
        "materialMap": request["materialMap"], "textures": textures,
        "uv_convention": "Honor each TGA descriptor first; then u=s/sourceSUnits, v=1-t/sourceTUnits.",
        "alpha_policy": "Preserve original alpha; no recolor or opaque fallback. Atlas cutoff 0.01 is a "
                        "declared Unity preview adaptation, not a proven historical alpha-test threshold.",
    })
    notice = recipe["notice"]
    start, end = notice["lines"]
    notice_text = b"".join(inputs[notice["path"]].splitlines(keepends=True)[start - 1:end]).decode("ascii")
    if not notice_text.strip() or len(inputs[notice["path"]].splitlines()) < end:
        raise PreparationError("Pinned source notice is missing or incomplete.")
    notice_input = next(record for record in records if record["path"] == notice["path"])
    bundle[PROVENANCE_NAME] = json_bytes({
        "schema_version": 1, "repository": recipe["repository"], "revision": recipe["revision"],
        "root_tree_sha": recipe["root_tree_sha"], "selected_variant": recipe["selected_variant"],
        "generator": {"path": "tools/assets/prepare_player.py",
                      "sha256": sha256(read_regular(project_root / "tools/assets/prepare_player.py", 65536)),
                      "recipe_path": "tools/assets/" + RECIPE_NAME, "recipe_sha256": recipe_sha},
        "authorization_reference": authorization_reference,
        "rights": "Explicitly authorized local preparation only; original Midway material and its "
                  "derivatives are not new project art. No license grant, publication, upload, "
                  "commit, push or redistribution is implied.",
        "verified_original_inputs": records,
        "source_notice": {"path": notice["path"], "lines": notice["lines"],
                          "git_blob_sha1": notice_input["git_blob_sha1"], "verbatim": notice_text},
        "derivative": {
            "path": atlas["output"], "source_path": atlas["source"],
            "source_git_blob_sha1": fingerprint(original)["git_blob_sha1"],
            "source_sha256": sha256(original), "sha256": sha256(derivative), "bytes": len(derivative),
            "identity": "Local level-0 derivative of original runtime atlas, NOT a historical TGA.",
            "recipe": "40-byte little-endian header; full ARGB1555 mip chain required. Level 0 only: "
                      "B=w&31, G=(w>>5)&31, R=(w>>10)&31, A=255 if w&0x8000 else 0; "
                      "expand each color (c<<3)|(c>>2); BGRA32 TGA, top-first, descriptor 0x28.",
            "original_mip_levels": len(levels), "rendered_mip_levels": 1,
            "alpha_zero_pixels": derivative[21::4].count(0),
            "alpha_opaque_pixels": derivative[21::4].count(255),
        },
        "artifacts_except_this_record": [
            {"path": label, "bytes": len(data), "sha256": sha256(data)}
            for label, data in sorted(bundle.items())
        ],
        "qualifications": [
            "This is one normal-body Bears-home PC_WHITE bind-pose preparation, not game parity.",
            "Direct skin TGAs are original authoring inputs, not reproduced ARMHND/HEDSHU WMS files.",
            "No atlas reconstruction, alpha override, recoloring, resizing or source mutation.",
            "No WAN animation, jersey-number overlay geometry, stadium, audio or Unity execution.",
            "Unity cutoff 0.01 and supplied axis/winding settings retain the existing preview request; "
            "original hardware lighting, filtering edges and inherited alpha-test reference are unverified.",
            "Original lower mip levels remain in the source WMS; only level 0 is emitted.",
        ],
    })
    return bundle


def make_directories(path):
    if path.exists() or path.is_symlink():
        check_physical(path, directory=True)
        return
    make_directories(path.parent)
    try:
        path.mkdir(mode=0o700)
    except FileExistsError:
        check_physical(path, directory=True)


def verify_identical(destination, bundle):
    check_physical(destination, directory=True)
    expected_directories = {
        str(parent) for label in bundle for parent in PurePosixPath(label).parents
        if str(parent) != "."
    }
    found = set()
    for root, directories, files in os.walk(destination, followlinks=False):
        for name in directories + files:
            path = Path(root) / name
            check_physical(path)
            label = path.relative_to(destination).as_posix()
            if label in expected_directories and path.is_dir():
                continue
            if label not in bundle or path.is_dir():
                raise PreparationError("Existing output has unexpected entries: " + label)
            if read_regular(path, len(bundle[label])) != bundle[label]:
                raise PreparationError("Existing output is not byte-identical: " + label)
            found.add(label)
    if found != set(bundle):
        raise PreparationError("Existing output is incomplete; it will not be repaired or overwritten.")


def prepare(source_root, destination, authorization_reference, reuse_identical=False):
    if (not authorization_reference or len(authorization_reference) > 128
            or any(character not in "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_"
                   for character in authorization_reference)):
        raise PreparationError("Use a non-secret authorization reference of 1..128 letters/digits/-/_.")
    project_root = Path(__file__).absolute().parents[2]
    check_physical(project_root, directory=True)
    source_root = absolute_path(source_root, Path.cwd())
    check_physical(source_root, directory=True)
    destination = absolute_path(destination, project_root)
    work_area = project_root / ".squad/.scratch"
    if work_area not in destination.parents:
        raise PreparationError("Destination must be a new child of this project's ignored .squad/.scratch.")
    if (source_root == destination or source_root in destination.parents
            or destination in source_root.parents):
        raise PreparationError("Source root and destination must not overlap.")
    recipe, recipe_sha = load_recipe(project_root)
    bundle = build_bundle(source_root, recipe, recipe_sha, project_root, authorization_reference)
    if destination.exists() or destination.is_symlink():
        if not reuse_identical:
            raise PreparationError("Destination already exists; use a new path or --reuse-identical.")
        verify_identical(destination, bundle)
        reused = True
    else:
        make_directories(destination.parent)
        destination.mkdir(mode=0o700)
        for label, data in sorted(bundle.items()):
            path_parts(label)
            target = destination / label
            make_directories(target.parent)
            descriptor = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL
                                 | getattr(os, "O_NOFOLLOW", 0), 0o600)
            with os.fdopen(descriptor, "wb") as output:
                output.write(data)
        verify_identical(destination, bundle)
        reused = False
    return {
        "destination": destination.relative_to(project_root).as_posix(),
        "request": REQUEST_NAME, "reused_identical": reused,
        "artifacts": [{"path": label, "bytes": len(data), "sha256": sha256(data)}
                      for label, data in sorted(bundle.items())],
    }


def main(argv=None):
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="Requires the five pinned player inputs plus cambot.c solely for its source notice. "
               "Existing outputs are never deleted or overwritten; failed partial outputs are retained. "
               "Run synthetic tests: python3 -B -m unittest discover -s tools/assets -p 'test_*.py'.",
    )
    parser.add_argument("--source-root", required=True, help="User-supplied physical pinned source tree.")
    parser.add_argument("--destination", required=True, help="New directory under .squad/.scratch.")
    parser.add_argument("--confirm-local-use", required=True, action="store_true",
                        help="Confirm authorization for local preparation; does not grant redistribution.")
    parser.add_argument("--authorization-reference", required=True,
                        help="Non-secret local authorization label, e.g. owner-directed-local-preview-2026-10-15.")
    parser.add_argument("--reuse-identical", action="store_true",
                        help="Read-only reuse only if the entire existing bundle is exactly identical.")
    args = parser.parse_args(argv)
    try:
        result = prepare(args.source_root, args.destination, args.authorization_reference,
                         args.reuse_identical)
    except (PreparationError, OSError, json.JSONDecodeError, UnicodeDecodeError) as error:
        parser.exit(1, "error: " + str(error) + "\n")
    print(json_bytes(result).decode("ascii"), end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
