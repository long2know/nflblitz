import importlib.util
from pathlib import Path
import struct
import sys
import unittest

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import prepare


def chunk(tag, data):
    return tag + len(data).to_bytes(4, "big") + data + (b"\0" if len(data) & 1 else b"")


def image(name="synthetic", width=2, height=1, bits=32, pixels=b"\x11\x22\x33\x44\x55\x66\x77\x88", palette=0, ax=1, ay=-2):
    header = bytearray(56)
    struct.pack_into(">II", header, 0, 1, palette)
    header[8:8 + len(name)] = name.encode("ascii")
    struct.pack_into("<HHH", header, 32, width, height, bits)
    struct.pack_into("<hh", header, 46, ax, ay)
    return chunk(b"IMG ", bytes(header) + pixels)


def wiff(*chunks):
    body = b"".join(chunks)
    return b"WIFF" + len(body).to_bytes(4, "big") + body


class PrepareChecks(unittest.TestCase):
    def test_rgba_not_tga_bgra(self):
        data = prepare.wif_images(wiff(image()), "synthetic.wif")["synthetic"]
        self.assertEqual(data.rgba, b"\x11\x22\x33\x44\x55\x66\x77\x88")
        self.assertEqual((data.width, data.height, data.ax, data.ay), (2, 1, 1, -2))

    def test_original_palette_reference_not_nearest_name(self):
        header = bytearray(36)
        struct.pack_into(">I", header, 0, 0x1002)
        struct.pack_into("<4H", header, 28, 2, 2, 0, 0)
        palette = chunk(b"PAL ", bytes(header) + b"\0\0\0\0\0\x20\x40\x60")
        data = prepare.wif_images(wiff(image(bits=8, pixels=b"\0\1", palette=0x1002), palette), "synthetic.wif")
        self.assertEqual(data["synthetic"].rgba, b"\0\0\0\0\x20\x40\x60\xff")
        with self.assertRaisesRegex(prepare.InterfaceError, "missing exact palette"):
            prepare.wif_images(wiff(image(bits=8, pixels=b"\0\1", palette=0x1003), palette), "synthetic.wif")

    def test_bad_lengths_and_unknown_chunks(self):
        valid = wiff(image())
        with self.assertRaisesRegex(prepare.InterfaceError, "WIFF length"):
            prepare.wif_images(valid[:-1], "truncated")
        with self.assertRaisesRegex(prepare.InterfaceError, "unsupported IMG payload"):
            prepare.wif_images(wiff(image(width=3)), "dimensions")
        with self.assertRaisesRegex(prepare.InterfaceError, "unsupported WIFF chunk"):
            prepare.wif_images(wiff(chunk(b"NOPE", b"abc"), image()), "unknown")

    def test_palette_pixel_bounds(self):
        header = bytearray(36)
        struct.pack_into(">I", header, 0, 17)
        struct.pack_into("<4H", header, 28, 1, 1, 0, 0)
        with self.assertRaisesRegex(prepare.InterfaceError, "outside its actual palette"):
            prepare.wif_images(wiff(image(bits=8, pixels=b"\0\1", palette=17),
                                   chunk(b"PAL ", bytes(header) + b"\0\x11\x22\x33")), "bad-index")

    def test_no_case_alias_or_missing_glyph_fallback(self):
        with self.assertRaisesRegex(prepare.InterfaceError, "ambiguous image identity"):
            prepare.wif_images(wiff(image("Glyph"), image("glyph")), "aliases")
        definitions = []
        for name in prepare.FONT_NAMES:
            definitions.append("static image_info_t *" + name + "_font_table[] = { &missing };")
            definitions.append("font_info_t " + name + "_font = { sizeof(table), 0x21, 0x21, 9, 5, 0, tex, table, 0 };")
        with self.assertRaisesRegex(prepare.InterfaceError, "Missing actual original glyph"):
            prepare.fonts("\n".join(definitions), {})

    def test_unselected_authoring_drafts_do_not_alias_active_symbols(self):
        decoded = prepare.wif_images(wiff(image("!Untitled-1"), image("!Untitled-1"), image("active")), "drafts")
        self.assertEqual(set(decoded), {"active"})

    def test_atlas_rows_metrics_and_pixel_identity(self):
        source = prepare.Image("synthetic", 1, 2, -3, 4, b"\xff\0\0\xff\0\xff\0\xff", "synthetic")
        pixels, width, height, records = prepare.pack({"synthetic": source}, 8)
        record = records[0]
        offset = (record["y"] * width + record["x"]) * 4
        self.assertEqual(pixels[offset:offset + 4], b"\0\xff\0\xff")
        self.assertEqual(pixels[offset + width * 4:offset + width * 4 + 4], b"\xff\0\0\xff")
        self.assertEqual((record["anchor_x"], record["anchor_y"]), (-3, 4))
        self.assertEqual(record["pixels_sha256"], prepare.sha256(source.rgba))
        self.assertLessEqual(record["y"] + record["height"], height)

    def test_integer_parser_never_evaluates_source(self):
        self.assertEqual(prepare.int_array("int synthetic[] = { 0x10, -1, /* gap */ 24 };", "synthetic", 3, -1, 24), [16, -1, 24])
        for value in ("func()", "__import__('os')", "1 << 4", "1.1"):
            with self.assertRaises(prepare.InterfaceError):
                prepare.integer(value)

    def test_cropped_or_fabricated_table_rejected(self):
        with self.assertRaisesRegex(prepare.InterfaceError, "shape/range"):
            prepare.int_array("int synthetic[] = { 0, 49 };", "synthetic", 2, 0, 48)
        with self.assertRaisesRegex(prepare.InterfaceError, "Missing original array"):
            prepare.int_array("int other[] = { 0 };", "synthetic", 1, 0, 48)


if __name__ == "__main__":
    unittest.main()
