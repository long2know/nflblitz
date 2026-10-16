#!/usr/bin/env python3
"""Original toy SDL/pixels only; no upstream art or reference vertices."""

import importlib.util
import json
import os
from pathlib import Path
import shutil
import struct
import sys
import unittest
import uuid


MODULE = Path(os.environ.get("BLITZ_ENVIRONMENT_CONVERTER", Path(__file__).with_name("prepare_environment.py")))
SPEC = importlib.util.spec_from_file_location("environment_under_test", MODULE)
env = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = env
SPEC.loader.exec_module(env)
PROJECT = Path(__file__).absolute().parents[2]


def toy_polyset(name, eight=False, shader="paint"):
    points = [(0, 0, 0), (2, 0, 0), (0, 0, 3)]
    if eight:
        points = [(x, y, z) for x in (0, 2) for y in (0, 1) for z in (0, 3)]
    vertices = ",".join("(cv((%s, %s, %s), 1))" % p for p in points)
    return ("polyset " + name + " (\nactive = ON, smooth = OFF, doublesided = OFF, opposite = OFF,\n"
            "shader = (" + shader + "), vertices = (" + vertices + "),\n"
            "texture_vertices = (st( 0.0, 0.0 ),st( 1.0, 0.0 ),st( 0.0, 1.0 )),\n"
            "polygons = (polygon ((0, 1, 2),(0, 1, 2), 0 )),\n);\n")


def toy_sdl(names=("tile",), eight=False):
    return ("""DEFINITION
version = 8.2;
texture paintfile (
 procedure = file,
 image = "/artist/toy/Tile.tif",
 rotate = 13.0,
 urepeat = 0.5,
 vrepeat = 2.0,
 uoffset = -0.25,
 voffset = 0.125,
);
shader paint (
 model = lambert,
 color = paintfile,
);
""" + "".join(toy_polyset(n, eight) for n in names) + """
MODEL
{ trn(9000, 8000, 7000); rot(yaxis, 33); instance tile(); }
""").encode("ascii")


def toy_tga(desc=8, bpp=32):
    pixels = bytes((0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 128))
    if bpp == 24:
        pixels = b"".join(pixels[i:i + 3] for i in range(0, 16, 4))
    return struct.pack("<BBBHHBHHHHBB", 0, 0, 2, 0, 0, 0, 0, 0, 2, 2, bpp, desc) + pixels


def toy_wms(fmt=11, words=(0xFC00, 0x83E0, 0x801F, 0, 0xFFFF), filtering=1):
    return struct.pack("<If8I", 0x8005, -1.0, filtering, 0, 2, 2, 8, 7, 3, fmt) + struct.pack("<5H", *words)


def draw_record(name, zone1="-1", zone2="-1"):
    return ("{&limb_" + name + ",0,GR_DEPTHBUFFER_WBUFFER,GR_CMP_LESS,FXTRUE,0,"
            "GR_COLORCOMBINE_DECAL_TEXTURE,GR_ALPHASOURCE_TEXTURE_ALPHA,GR_TEXTURECOMBINE_DECAL,"
            "GR_TEXTURECLAMP_CLAMP,GR_BLEND_ONE,GR_BLEND_ZERO,GR_BLEND_ONE,GR_BLEND_ZERO,"
            "GR_CHROMAKEY_DISABLE,0,GR_CMP_ALWAYS,0,GR_CULL_NEGATIVE," + zone1 + "," + zone2 + "}")


def toy_array(name, body):
    return ("static limb_draw_info_t " + name + "[] = {" + body + "};\n")


class Workspace(unittest.TestCase):
    def setUp(self):
        self.work = PROJECT / ".squad/.scratch" / ("environment-unit-" + uuid.uuid4().hex)
        self.work.mkdir(parents=True, exist_ok=False)

    def tearDown(self):
        shutil.rmtree(self.work)

    def fixture(self):
        root = self.work / "source"
        sources = {
            "data2.c": b"/* Original synthetic include selection. */",
            "models/models.mak": b"/* Original synthetic recipe marker. */",
            "models/field2.sdl": toy_sdl(("tile", "marker", "post", "inactive")),
            "models/stadpie.sdl": toy_sdl(("stands",) + tuple("box" + str(level) + letter
                                              for letter in "abcdef" for level in (1, 2, 3)), eight=True),
            "field.c": (
                toy_array("field2_limb_draw_info", draw_record("tile"))
                + toy_array("field2_limb_draw_info2", draw_record("marker"))
                + toy_array("goalposts2_limb_draw_info", draw_record("post"))
                + 'char *day_stadium_textures[] = {"Tile.wms",NULL};\n'
            ).encode("ascii"),
            "include/nstadlim.h": toy_array("stadium2_limb_draw_info", draw_record("stands", "ZONE1A", "ZONE2F")).encode("ascii"),
            "include/id.h": b"/* Original synthetic render pass marker. */",
            "lib/ani3d/pipe3D.c": b"/* Original synthetic winding marker. */",
        }
        textures = {"img3d/Tile.tga": toy_tga()}
        for path, data in {**sources, **textures}.items():
            target = root / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        recipe = {
            "sourceCommit": env.SOURCE_COMMIT, "sourceRootTree": "1" * 40,
            "converterEvidence": [],
            "inputs": [dict(env.fingerprint(data), path=path) for path, data in sources.items()],
            "textures": [dict(env.fingerprint(data), path=path) for path, data in textures.items()],
        }
        return root, recipe


class SdlTests(unittest.TestCase):
    def test_original_schema_transform_and_baked_group_semantics(self):
        scene = env.read_sdl(toy_sdl(), "toy.sdl")
        self.assertEqual(scene.name, "toy")
        self.assertEqual(scene.polysets[0].positions, [(0, 0, 0), (130, 0, 0), (0, 0, 172.5)])
        self.assertEqual(scene.polysets[0].flags["doublesided"], "OFF")
        self.assertEqual(scene.textures["paintfile"].rotation, 13)
        self.assertEqual(scene.slots, ["paintfile"])

    def test_original_scalar_binary32_then_six_decimal_rounding(self):
        data = toy_sdl().replace(b"cv((2, 0, 0)", b"cv((1.23456789, 0, 0)")
        value = env.read_sdl(data, "toy").polysets[0].positions[1][0]
        self.assertEqual(value, struct.unpack("<f", struct.pack("<f", 80.24691))[0])
        self.assertNotEqual(value, 1.23456789 * 65)

    def test_uv_repeat_offset_original_sign_behavior_and_flip(self):
        scene = env.read_sdl(toy_sdl(), "toy")
        expanded = list(env.expanded_triangles(scene, scene.polysets[0]))
        self.assertEqual(expanded[0], ("paintfile", [(0, (0.25, 0.875)), (1, (0.75, 0.875)), (2, (0.25, -1.125))]))
        data, count, submeshes = env.mesh_bytes(scene, scene.polysets[0], 4)
        self.assertEqual((data[:8], count, submeshes), (b"BZENM001", 3, 1))
        rows = list(struct.iter_unpack("<5f", data[16:76]))
        self.assertEqual(rows[2], (0, 0, -172.5, 0.25, 2.125))
        self.assertEqual(struct.unpack_from("<5I", data, 76), (4, 3, 0, 1, 2))
        self.assertGreater(-rows[1][0] * rows[2][2], 0)  # Up-facing ground after renderer/reflection compensation.

    def test_optional_normal_tuple_is_not_a_vertex_or_uv_index(self):
        data = toy_sdl().replace(b"(0, 1, 2), 0 )),", b"(0, 1, 2), (99, 98, 97), 0 )),")
        self.assertEqual(env.read_sdl(data, "toy").polysets[0].triangles[0], ((0, 1, 2), (0, 1, 2), 0))

    def test_missing_shader_or_image_is_identified(self):
        for data in (toy_sdl().replace(b"shader = (paint)", b"shader = (unmatched)"),
                     toy_sdl().replace(b"color = paintfile", b"color = gonefile")):
            with self.assertRaisesRegex(env.EnvironmentError, "missing textured shader identity: tile"):
                env.read_sdl(data, "toy")

    def test_geometry_rejections(self):
        mutations = [
            (b"version = 8.2", b"version = 9.0", "Alias SDL 8.2"),
            (b"polygon ((0, 1, 2)", b"polygon ((0, 1, 99)", "vertex index out of range"),
            (b"(0, 1, 2), 0 )),", b"(0, 1, 99), 0 )),", "texture index out of range"),
            (b"(0, 1, 2), 0 )),", b"(0, 1, 2), 3 )),", "shader index out of range"),
            (b"polygon ((0, 1, 2)", b"polygon ((0, 1, 2, 3)", "triangular polygons"),
            (b"cv((2, 0, 0)", b"cv((NaN, 0, 0)", "vertex index out of range"),
        ]
        for old, new, message in mutations:
            with self.subTest(message=message), self.assertRaisesRegex(env.EnvironmentError, message):
                env.read_sdl(toy_sdl().replace(old, new), "toy")

    def test_duplicate_names_and_unterminated_blocks_rejected(self):
        data = toy_sdl().replace(b"MODEL", toy_polyset("tile").encode("ascii") + b"MODEL")
        with self.assertRaisesRegex(env.EnvironmentError, "Duplicate SDL polyset"):
            env.read_sdl(data, "toy")
        with self.assertRaisesRegex(env.EnvironmentError, "Unterminated"):
            env.read_sdl(toy_sdl().replace(b");\nshader", b"\nshader", 1), "toy")

    def test_comments_do_not_introduce_geometry_and_strings_survive(self):
        scene = env.read_sdl(b"/* polyset fake( cv((99,88,77) */\n" + toy_sdl(), "toy")
        self.assertEqual(len(scene.polysets), 1)
        self.assertEqual(scene.textures["paintfile"].file, "Tile.tif")


class TextureTests(unittest.TestCase):
    def test_tga_exact_channels_alpha_and_bottom_origin(self):
        info = env.decode_tga(toy_tga(), "toy.tga")
        self.assertEqual(info["sourceFormat"], "TGA32")
        self.assertEqual(info["mips"], [bytes((255, 0, 0, 255, 0, 255, 0, 255,
                                             0, 0, 255, 255, 255, 255, 255, 128))])

    def test_tga_top_right_origin_is_normalized_without_recolor(self):
        info = env.decode_tga(toy_tga(8 | 32 | 16), "toy.tga")
        self.assertEqual(info["mips"][0], bytes((255, 255, 255, 128, 0, 0, 255, 255,
                                               0, 255, 0, 255, 255, 0, 0, 255)))
        self.assertEqual(env.decode_tga(toy_tga(0, 24), "toy.tga")["mips"][0][3::4], b"\xff" * 4)

    def test_tga_rejects_palette_rle_truncation_and_unknown_trailer(self):
        for index, value in ((1, 1), (2, 10), (16, 16), (17, 0x48)):
            data = bytearray(toy_tga())
            data[index] = value
            with self.assertRaisesRegex(env.EnvironmentError, "toy.tga: unsupported"):
                env.decode_tga(bytes(data), "toy.tga")
        for data in (toy_tga()[:-1], toy_tga() + b"unknown"):
            with self.assertRaisesRegex(env.EnvironmentError, "truncated TGA pixels"):
                env.decode_tga(data, "toy.tga")

    def test_wms_argb1555_all_authored_mips_and_flags(self):
        info = env.decode_wms(toy_wms(), "toy.wms")
        self.assertEqual((info["sourceFormat"], info["lodBias"], info["filterType"]), ("WMS/ARGB1555", -1, 1))
        self.assertEqual(info["mips"], [bytes((0, 0, 255, 255, 0, 0, 0, 0,
                                              255, 0, 0, 255, 0, 255, 0, 255)), b"\xff" * 4])

    def test_wms_rgb565_is_not_decoded_as_argb1555(self):
        info = env.decode_wms(toy_wms(10, (0xF800, 0x07E0, 0x001F, 0, 0xFFFF)), "toy.wms")
        self.assertEqual(info["mips"][0], bytes((0, 0, 255, 255, 0, 0, 0, 255,
                                               255, 0, 0, 255, 0, 255, 0, 255)))

    def test_wms_argb4444_retains_fractional_alpha(self):
        info = env.decode_wms(toy_wms(12, (0x8F00,) * 5), "toy.wms")
        self.assertEqual(info["mips"], [bytes((255, 0, 0, 136)) * 4, bytes((255, 0, 0, 136))])

    def test_wms_unverified_palettes_are_explicitly_rejected_not_guessed(self):
        header = struct.pack("<If8I", 0x8005, 0, 1, 0, 2, 2, 8, 7, 3, 5)
        palette = bytearray(1024)
        palette[4:8] = bytes((17, 31, 71, 0))
        data = header + palette + bytes((1, 1, 1, 1, 1))
        with self.assertRaisesRegex(env.EnvironmentError, "toy.wms: unsupported WMS format 5"):
            env.decode_wms(data, "toy.wms")
        palette[4:8] = bytes((3, 2, 1, 0))
        with self.assertRaisesRegex(env.EnvironmentError, "other.wms: unsupported WMS format 5"):
            env.decode_wms(header + palette + bytes((1,) * 5), "other.wms")

    def test_wms_rejects_unknown_version_format_flags_lods_and_data(self):
        for offset, value, message in ((0, 5, "version"), (8, 19, "filter"),
                                        (12, 2, "trilinear"), (16, 3, "dimensions"),
                                        (24, 6, "LOD"), (32, 4, "aspect"),
                                        (36, 9, "format 9")):
            data = bytearray(toy_wms())
            struct.pack_into("<I", data, offset, value)
            with self.subTest(offset=offset), self.assertRaisesRegex(env.EnvironmentError, message):
                env.decode_wms(bytes(data), "toy.wms")
        with self.assertRaisesRegex(env.EnvironmentError, "truncated WMS mip"):
            env.decode_wms(toy_wms()[:-1], "toy.wms")
        with self.assertRaisesRegex(env.EnvironmentError, "trailing/mismatched WMS"):
            env.decode_wms(toy_wms() + b"x", "toy.wms")


class BundleTests(Workspace):
    def test_real_pipeline_fixture_preserves_materials_draw_roles_zones_and_bounds(self):
        root, recipe = self.fixture()
        manifest, files = env.prepare(root, recipe, env.SOURCE_COMMIT)
        self.assertEqual(len(manifest["draws"]), 4)
        self.assertEqual(len(manifest["zoneVolumes"]), 18)
        self.assertEqual([d["name"] for d in manifest["draws"]], ["tile", "stands", "marker", "post"])
        self.assertEqual([d["state"]["zone2"] for d in manifest["draws"]], [-1, 16, -1, -1])
        self.assertEqual(sum(m["role"] == "zone-volume" for m in manifest["meshes"]), 18)
        self.assertEqual(sum(m["role"] == "inactive-authoring" for m in manifest["meshes"]), 1)
        self.assertEqual(len(manifest["textures"]), 1)
        self.assertEqual(manifest["textures"][0]["samplingWrap"], "GR_TEXTURECLAMP_WRAP")
        self.assertEqual(manifest["draws"][0]["state"]["clampMode"], "GR_TEXTURECLAMP_CLAMP")
        self.assertEqual(manifest["slots"][0]["authoredName"], "Tile.wms")
        self.assertEqual(manifest["slots"][0]["textureIndex"], 0)
        self.assertEqual(manifest["meshes"][0]["boundsSource"], [0, 0, 0, 130, 0, 172.5])
        self.assertEqual(manifest["meshes"][0]["boundsUnity"], [0, 0, -172.5, 130, 0, 0])
        self.assertTrue(all(d["renderQueue"] < 2000 for d in manifest["draws"][:2]))
        self.assertTrue(all(d["renderQueue"] > 2000 for d in manifest["draws"][2:]))
        self.assertEqual(files["environment.json"], env.json_bytes(manifest))
        self.assertNotIn(str(root), files["environment.json"].decode())
        for mesh in manifest["meshes"]:
            self.assertEqual(env.digest(files[mesh["meshPath"]]), mesh["meshSha256"])
            self.assertEqual(len(files[mesh["meshPath"]]), mesh["meshBytes"])

    def test_no_stadium_is_explicit_not_a_fallback(self):
        root, recipe = self.fixture()
        manifest, _ = env.prepare(root, recipe, env.SOURCE_COMMIT, stadium="none")
        self.assertEqual(manifest["stadiumSelector"], 3)
        self.assertEqual(len(manifest["draws"]), 3)
        self.assertFalse(any(d["sourceSet"] == "stadpie" for d in manifest["draws"]))

    def test_source_pin_blob_and_missing_path_fail_before_output(self):
        root, recipe = self.fixture()
        with self.assertRaisesRegex(env.EnvironmentError, "Source commit mismatch"):
            env.prepare(root, recipe, "0" * 40)
        target = root / "models/field2.sdl"
        target.write_bytes(target.read_bytes() + b"x")
        with self.assertRaisesRegex(env.EnvironmentError, "Pinned original.*models/field2.sdl|Oversized input"):
            env.prepare(root, recipe, env.SOURCE_COMMIT)
        target.unlink()
        with self.assertRaisesRegex(env.EnvironmentError, "Missing required original input: models/field2.sdl"):
            env.prepare(root, recipe, env.SOURCE_COMMIT)

    def test_exact_missing_texture_not_a_colored_substitute(self):
        root, recipe = self.fixture()
        (root / "img3d/Tile.tga").unlink()
        with self.assertRaisesRegex(env.EnvironmentError, "Missing required original input: img3d/Tile.tga"):
            env.prepare(root, recipe, env.SOURCE_COMMIT)
        recipe["textures"] = []
        with self.assertRaisesRegex(env.EnvironmentError, "field2/Tile.wms -> Tile.wms"):
            env.prepare(root, recipe, env.SOURCE_COMMIT)

    def test_unknown_selector_and_invalid_prefix_fail(self):
        root, recipe = self.fixture()
        with self.assertRaisesRegex(env.EnvironmentError, "selector"):
            env.prepare(root, recipe, env.SOURCE_COMMIT, field="invented-grass")
        with self.assertRaisesRegex(env.EnvironmentError, "three-letter"):
            env.prepare(root, recipe, env.SOURCE_COMMIT, home_prefix="../")
        with self.assertRaisesRegex(env.EnvironmentError, "not a claim that other original art is missing"):
            env.prepare(root, recipe, env.SOURCE_COMMIT, home_prefix="EAG")

    def test_relative_paths_symlinks_and_nonignored_output_rejected(self):
        root, recipe = self.fixture()
        for relative in ("../secret", "/absolute", "x//y", "x\\y", "CON/file"):
            with self.subTest(relative=relative), self.assertRaises(env.EnvironmentError):
                env.portable(relative)
        link = root / "models/link.sdl"
        link.symlink_to("field2.sdl")
        with self.assertRaisesRegex(env.EnvironmentError, "Symlink"):
            env.read_file(root, "models/link.sdl")
        with self.assertRaisesRegex(env.EnvironmentError, "ignored"):
            env.write_bundle(PROJECT, PROJECT / "tools/environments/do-not-write", {"x": b"x"})

    def test_output_persistent_and_never_overwritten(self):
        root, recipe = self.fixture()
        manifest, files = env.prepare(root, recipe, env.SOURCE_COMMIT)
        output = self.work / "converted"
        env.write_bundle(PROJECT, output, files)
        self.assertEqual(json.loads((output / "environment.json").read_text())["schema"], env.SCHEMA)
        self.assertEqual((output / manifest["textures"][0]["rgbaPath"]).read_bytes(),
                         files[manifest["textures"][0]["rgbaPath"]])
        with self.assertRaisesRegex(env.EnvironmentError, "already exists"):
            env.write_bundle(PROJECT, output, files)


if __name__ == "__main__":
    unittest.main()
