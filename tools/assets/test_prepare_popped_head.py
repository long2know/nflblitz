"""Public synthetic geometry only. Original phead/calibration vectors are never fixtures."""
from pathlib import Path
import os
import shutil
import unittest

import prepare_popped_head as head


SYNTHETIC = b"""DEFINITION version=8.2;
texture hedshu#2(procedure=file,image="/authoring/hedshu.TIF",urepeat=1.0,vrepeat=1.0,uoffset=0.0,voffset=0.0,rotate=0.0,);
shader HEDSHU#2(model=lambert,color=hedshu#2,);
polyset phead_obj(
active=ON,motion_blur=OFF,smooth=OFF,doublesided=OFF,opposite=OFF,
shader=(HEDSHU#2),casts_shadow=FALSE,
vertices=((cv((0,0,0),1)),(cv((1,0,0),1)),(cv((0,1,0),1))),
texture_vertices=(st(0,0),st(1,0),st(0,1)),
polygons=(polygon((0,1,2),(0,1,2),0))
);
MODEL {instance phead_obj();}
"""


class PoppedHeadPreparation(unittest.TestCase):
    def test_strict_geometry_and_header_preserve_scale_winding_square_uv_and_raw_texture_code(self):
        poly = head.geometry(SYNTHETIC)
        self.assertEqual(poly.positions, [(0, 0, 0), (head.SDL.f32(1.2), 0, 0), (0, head.SDL.f32(1.2), 0)])
        self.assertEqual(poly.triangles, [((0, 1, 2), (0, 1, 2), 0)])
        generated = head.header(poly).decode("ascii")
        self.assertIn("{0,3,6,0,1,2,4}", generated, "raw4 must remain the source stride-four HEDSHU selector")
        self.assertIn("{256.000000f,256.000000f}", generated)
        self.assertIn("NOT recovered native head.h", generated)

    def test_unsupported_geometry_material_uv_index_and_degenerate_input_are_rejected(self):
        for old, new in (
            (b"version=8.2", b"version=9.0"),
            (b"shader=(HEDSHU#2)", b"shader=(SLVMASK)"),
            (b"(0,1,2),0)", b"(0,1,3),0)"),
            (b"(0,1,2),0)", b"(0,1,2),1)"),
            (b"st(1,0)", b"st(-1,0)"),
            (b"st(1,0)", b"st(NaN,0)"),
            (b"cv((0,1,0)", b"cv((0,0,0)"),
            (b"smooth=OFF", b"smooth=ON"),
            (b"casts_shadow=FALSE,", b"casts_shadow=FALSE,normals=(0,0,1),"),
        ):
            with self.subTest(new=new):
                self.assertIn(old, SYNTHETIC)
                with self.assertRaises((ValueError, head.SDL.EnvironmentError)):
                    head.geometry(SYNTHETIC.replace(old, new, 1))

    def test_wrong_original_hash_cannot_be_promoted_to_original_geometry(self):
        owned = head.ROOT / ".squad/.scratch" / ("phead-public-test-" + str(os.getpid()))
        self.assertFalse(owned.exists())
        owned.mkdir(mode=0o700, parents=True)
        try:
            (owned / "synthetic.sdl").write_bytes(SYNTHETIC)
            with self.assertRaisesRegex(ValueError, "Exact pinned original"):
                head.prepare(owned / "synthetic.sdl", owned, owned / "output")
            self.assertFalse((owned / "output").exists())
        finally:
            shutil.rmtree(owned)

    def test_symlink_and_traversal_do_not_reach_source_geometry(self):
        owned = head.ROOT / ".squad/.scratch" / ("phead-public-path-" + str(os.getpid()))
        self.assertFalse(owned.exists())
        owned.mkdir(mode=0o700, parents=True)
        try:
            (owned / "synthetic.sdl").write_bytes(SYNTHETIC)
            (owned / "alias.sdl").symlink_to(owned / "synthetic.sdl")
            with self.assertRaises(head.files.PreparationError):
                head.prepare(owned / "alias.sdl", owned, owned / "output")
            with self.assertRaises(head.files.PreparationError):
                head.files.absolute_path("../phead.sdl", owned)
        finally:
            shutil.rmtree(owned)


if __name__ == "__main__":
    unittest.main()
