"""Synthetic public fixtures; original medball2 vertices/UVs remain private."""
import unittest
import prepare_free_ball as ball

SYNTHETIC = b"""DEFINITION version=7.51;
texture HEDSHU(procedure=file,image="/authoring/hedshu.tif",urepeat=1.0,vrepeat=1.0,uoffset=0.0,voffset=0.0,rotate=0.0,);
shader hedshu(model=lambert,color=HEDSHU,);
polyset medball_obj(active=ON,motion_blur=ON,smooth=ON,doublesided=OFF,opposite=OFF,
shader=(hedshu),casts_shadow=TRUE,
vertices=((cv((2,3,4),1)),(cv((3,3,4),1)),(cv((2,4,4),1))),
texture_vertices=(st(0,0),st(1,0),st(0,1)),
polygons=(polygon((0,1,2),(0,1,2),0)));
MODEL {instance medball_obj();}
"""


class FreeBallPreparation(unittest.TestCase):
    def test_actual_converter_preserves_unscaled_authoring_pivot_and_decal_zero(self):
        poly = ball.geometry(SYNTHETIC)
        self.assertEqual(poly.positions, [(2, 3, 4), (3, 3, 4), (2, 4, 4)])
        self.assertEqual(poly.triangles, [((0, 1, 2), (0, 1, 2), 0)])
        header = ball.header(poly).decode("ascii")
        self.assertIn("{2.000000f,3.000000f,4.000000f}", header)
        self.assertIn("{0,3,6,0,1,2,0}", header, "Free ball uses the sole HEDSHU decal, not held-ball material6.")
        self.assertIn("limb_medball_obj_big", header)
        self.assertIn("NOT recovered native medball2.h", header)

    def test_parser_rejects_wrong_mesh_material_pivot_grammar_and_invalid_faces(self):
        for old, new in (
            (b"medball_obj", b"hand001b"), (b"hedshu.tif", b"ball.tif"),
            (b"version=7.51", b"version=8.2"), (b"smooth=ON", b"smooth=OFF"),
            (b"st(1,0)", b"st(2,0)"), (b"(0,1,2),0)", b"(0,1,3),0)"),
            (b"(0,1,2),0)", b"(0,1,2),1)"),
            (b"cv((2,4,4)", b"cv((2,3,4)"),
            (b"casts_shadow=TRUE,", b"casts_shadow=TRUE,translate=(1,2,3),"),
        ):
            with self.subTest(new=new), self.assertRaises(ValueError):
                ball.geometry(SYNTHETIC.replace(old, new, 1))


if __name__ == "__main__":
    unittest.main()
