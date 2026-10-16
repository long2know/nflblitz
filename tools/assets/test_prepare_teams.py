"""Original synthetic team preparation cases; no archived artwork is included."""
import copy
from pathlib import Path
import shutil
import unittest
import uuid

import prepare_player as files
import prepare_teams as teams
from export_rosters import SourceError


class TeamSourceSelectionTests(unittest.TestCase):
    def test_present_runtime_identity_wins_without_filename_guess(self):
        asset = {"source_path": "img3d/ABCALOD.wms", "runtime_filename": "ABCALOD.WMS"}
        self.assertEqual(teams.texture_source(asset, {"img3d/ABCALOD.tga": {}}), asset["source_path"])

    def test_missing_runtime_uses_exact_name_authoring_tga_only(self):
        asset = {"id": "runtime/ARMHND2.WMS", "source_path": None, "runtime_filename": "ARMHND2.WMS"}
        tree = {"img3d/armhnd2.tga": {}, "img3d/armhnd.tga": {}, "img2d/armhnd2.tga": {}}
        self.assertEqual(teams.texture_source(asset, tree), "img3d/armhnd2.tga")
        self.assertIsNone(teams.texture_source(asset, {"img3d/armhnd.tga": {}}))

    def test_missing_uniform_cannot_alias_another_team_or_old_prefix(self):
        asset = {"id": "runtime/TITHLOD.WMS", "source_path": None, "runtime_filename": "TITHLOD.WMS"}
        self.assertIsNone(teams.texture_source(asset, {"img3d/OILHLOD.wms": {}, "img3d/titjerfh.tga": {}}))

    def test_ambiguous_case_authoring_input_fails(self):
        asset = {"id": "runtime/HED_TEST.WMS", "source_path": None, "runtime_filename": "HED_TEST.WMS"}
        with self.assertRaisesRegex(SourceError, "Ambiguous"):
            teams.texture_source(asset, {"img3d/HED_TEST.TGA": {}, "img3d/hed_test.tga": {}})


class OriginalAtlasRecipeTests(unittest.TestCase):
    def setUp(self):
        self.layout = ("TGA_COUNT> 4\nPACK_WIDTH> 128\nPACK_HEIGHT> 256\n"
                       "PACK_TEXTURES> ABCJERFH.TGA ABCLGHLH.TGA ABCSVMKH.TGA ABCNUM.TGA\nPACK> ABCH.TGA\n"
                       "PACK_TEXTURES> ABCJERFA.TGA ABCLGHLA.TGA ABCSVMKA.TGA ABCNUM2.TGA\nPACK> ABCA.TGA\n")
        self.commands = ("copy ABCH.tga ABCHLOD.tga\ntexus -t argb1555 -o ABCHLOD.3df ABCHLOD.tga\n"
                         "copy ABCA.tga ABCALOD.tga\ntexus -t argb1555 -o ABCALOD.3df ABCALOD.tga\n")
        self.tree = {"img3d/" + name.lower(): {} for name in (
            "ABCJERFH.TGA", "ABCLGHLH.TGA", "ABCSVMKH.TGA", "ABCNUM.TGA",
            "ABCJERFA.TGA", "ABCLGHLA.TGA", "ABCSVMKA.TGA", "ABCNUM2.TGA")}

    def test_actual_active_recipe_keeps_home_and_away_pieces_separate(self):
        home = teams.atlas_pieces("ABCHLOD.WMS", self.layout, self.commands, self.tree)
        away = teams.atlas_pieces("ABCALOD.WMS", self.layout, self.commands, self.tree)
        self.assertEqual(home, ["img3d/abcjerfh.tga", "img3d/abclghlh.tga", "img3d/abcsvmkh.tga", "img3d/abcnum.tga"])
        self.assertEqual(away, ["img3d/abcjerfa.tga", "img3d/abclghla.tga", "img3d/abcsvmka.tga", "img3d/abcnum2.tga"])

    def test_missing_any_authored_piece_is_not_filled_with_home_or_another_team(self):
        for path in self.tree:
            with self.subTest(path=path):
                tree = dict(self.tree)
                tree.pop(path)
                variant = "ABCHLOD.WMS" if path in teams.atlas_pieces("ABCHLOD.WMS", self.layout, self.commands, self.tree) else "ABCALOD.WMS"
                with self.assertRaisesRegex(SourceError, "authored pieces"):
                    teams.atlas_pieces(variant, self.layout, self.commands, tree)

    def test_duplicate_pack_target_rejected_not_first_match(self):
        with self.assertRaisesRegex(SourceError, "ambiguous"):
            teams.atlas_pieces("ABCHLOD.WMS", self.layout + self.layout, self.commands, self.tree)

    def test_runtime_1555_branch_is_required_not_inactive_4444_selection_branch(self):
        for commands in (self.commands.replace("argb1555", "argb4444"), self.commands.replace("copy ABCH", "REM copy ABCH"),
                         self.commands.replace("ABCHLOD.3df ABCHLOD.tga", "ABCH.3df ABCH.tga")):
            with self.subTest(commands=commands):
                with self.assertRaisesRegex(SourceError, "active uniform conversion"):
                    teams.atlas_pieces("ABCHLOD.WMS", self.layout, commands, self.tree)

    def test_four_piece_original_order_is_not_replaced_by_alphabetical_sort(self):
        output = teams.atlas_pieces("ABCHLOD.WMS", self.layout, self.commands, self.tree)
        self.assertNotEqual(output, sorted(output))
        self.assertTrue(output[-1].endswith("num.tga"), "Only the fourth c_hack input uses number/alpha policy.")


class PinnedInputTests(unittest.TestCase):
    def setUp(self):
        self.root = teams.ROOT / ".squad/.scratch" / ("team-input-test-" + uuid.uuid4().hex)
        self.root.mkdir(parents=True, exist_ok=False)
        self.label = "models/synthetic.h"
        path = self.root / self.label
        path.parent.mkdir()
        self.bytes = b"original synthetic test data, not archived geometry\n"
        path.write_bytes(self.bytes)
        pin = files.fingerprint(self.bytes)
        self.tree = {self.label: {"type": "blob", "mode": "100644", "size": len(self.bytes),
                                  "sha": pin["git_blob_sha1"]}}

    def tearDown(self):
        shutil.rmtree(self.root)

    def test_exact_original_blob_bytes_are_returned_without_mutation(self):
        self.assertEqual(teams.read_pinned(self.root, self.tree, self.label), self.bytes)
        self.assertEqual((self.root / self.label).read_bytes(), self.bytes)

    def test_same_length_wrong_source_content_is_rejected(self):
        (self.root / self.label).write_bytes(self.bytes.replace(b"synthetic", b"mutation!"))
        with self.assertRaisesRegex(SourceError, "content pin"):
            teams.read_pinned(self.root, self.tree, self.label)

    def test_missing_tree_identity_and_symlink_blob_mode_fail(self):
        for tree in ({}, {self.label: dict(self.tree[self.label], mode="120000")}):
            with self.subTest(tree=tree):
                with self.assertRaisesRegex(SourceError, "regular original source"):
                    teams.read_pinned(self.root, tree, self.label)

    def test_relative_traversal_is_rejected_before_read(self):
        with self.assertRaisesRegex(SourceError, "relative path"):
            teams.read_pinned(self.root, self.tree, "../synthetic.h")

    def test_mutated_final_roster_bytes_fail_before_any_source_read(self):
        with self.assertRaisesRegex(SourceError, "roster content SHA256"):
            teams.build_bundle(self.root, b'{"schema":"invented"}', "synthetic-test")


if __name__ == "__main__":
    unittest.main()
