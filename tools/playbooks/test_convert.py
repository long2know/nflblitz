import copy
import importlib.util
import json
from pathlib import Path
import shutil
import struct
import unittest
from unittest import mock


HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("source_playbook_converter", HERE / "convert.py")
converter = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(converter)


def synthetic_documents():
    header = """
#define DS_WAIT_SNAP 1
#define DS_FACE 2
#define DS_RUN_YDS 4
#define DS_IMPROVISE 5
#define DS_COVER_TGT 6
#define DS_BLOCK 8
#define DS_WAIT 16
#define LU_RIGHT 0x001
#define LU_LEFT 0x002
#define LU_CENTER 0x004
#define LU_CAPN 0x008
#define PLF_REDZONE 0x0400
"""
    player_header = """
#define MODE_LINEPS 2
#define MODE_QBPS 4
#define MODE_WPNPS 6
#define MODE_DBPS 8
"""
    slots = [
        "{-0.5f,-1.0f,line_fixture_anim,MODE_LINEPS,0}",
        "{-0.5f,0.0f,line_fixture_anim,MODE_LINEPS,0}",
        "{-0.5f,1.0f,line_fixture_anim,MODE_LINEPS,0}",
        "{-2.0f,0.0f,qb_fixture_anim,MODE_QBPS,LU_CAPN}",
        "{-3.0f,-2.0f,receiver_fixture_anim,MODE_WPNPS,LU_LEFT}",
        "{-1.0f,2.0f,receiver_fixture_anim,MODE_WPNPS,LU_CENTER}",
        "{-1.0f,4.0f,receiver_fixture_anim,MODE_WPNPS,LU_RIGHT}",
    ]
    defense_slots = ["{1.0f," + str(i) + ".0f,db_fixture_anim,MODE_DBPS,0}" for i in range(7)]
    playbook = (
        "fline_t formation_fixture[] = {" + ",".join(slots) + "};\n"
        "fline_t def_formation_fixture[] = {" + ",".join(defense_slots) + "};\n"
        "int ds_fixture_block[] = {DS_BLOCK};\n"
        "int pr_fixture_alpha[] = {DS_WAIT_SNAP,DS_FACE,0,DS_RUN_YDS,2,DS_IMPROVISE};\n"
        "int pr_fixture_beta[] = {DS_WAIT_SNAP,DS_WAIT,3,DS_FACE,-128,DS_RUN_YDS,1,DS_IMPROVISE};\n"
        "int db_fixture_cover[] = {DS_COVER_TGT};\n"
        "play_t play_fixture_alpha = {formation_fixture,0,{ds_fixture_block,ds_fixture_block,ds_fixture_block,"
        "ds_fixture_block,pr_fixture_alpha,pr_fixture_beta,pr_fixture_alpha}};\n"
        "play_t play_fixture_beta = {formation_fixture,0,{ds_fixture_block,ds_fixture_block,ds_fixture_block,"
        "ds_fixture_block,pr_fixture_beta,pr_fixture_alpha,pr_fixture_beta}};\n"
        "play_t play_fixture_defense = {def_formation_fixture,PLF_REDZONE,{db_fixture_cover,db_fixture_cover,"
        "db_fixture_cover,db_fixture_cover,db_fixture_cover,db_fixture_cover,db_fixture_cover}};\n"
    )
    directions = [(i if i < 24 else i - 1) % 32 * 32 for i in range(49)]
    directions[24] = -1
    return {
        "playbook": playbook,
        "slots": 'SLOT fake_slots[] = {{"SYNTHETIC ALPHA",&play_fixture_alpha,0,{NULL,NULL}},'
                 '{"SYNTHETIC BETA",&play_fixture_beta,0,{NULL,NULL}}};',
        "selection": "static play_t *fixture_weighted[] = {&play_fixture_alpha,&play_fixture_alpha,&play_fixture_beta};\n"
                     "void fixture_pick(void) {game_info.team_play[pnum] = &play_fixture_beta;}\n",
        "playstuf_header": header,
        "player_header": player_header,
        "player": "int dir49_table[] = {" + ",".join(map(str, directions)) + "};",
        "trigs": "float trigs[] = {" + ",".join(str(i / 256) for i in range(257)) + "};",
        "handicap": "int fixture_ht[12] = {" + ",".join(map(str, range(12))) + "};\n"
                    "float fixture_float_ht[12] = {" + ",".join("0.5f" for _ in range(12)) + "};",
        "drone": "#define USE_OLD_QB FALSE\n/* Synthetic identity-only input. */",
        "game_header": "#define tsec 57\n/* Synthetic identity-only input. */",
    }


def synthetic_recipe(documents):
    recipe = json.loads((HERE / "recipe.json").read_text())
    recipe["source_revision"] = "1" * 40
    recipe["source_tree"] = "2" * 40
    recipe["profile"]["identity"] = "SYNTHETIC-CONVERTER-TEST-NOT-GAME-PARITY"
    for role, data in documents.items():
        recipe["inputs"][role]["blob_sha1"] = converter.blob_sha1(data.encode("latin1"))
    recipe["weighted_pools"] = {"ShortOffense": {"symbol": "fixture_weighted", "count": 3}}
    recipe["singleton_pools"] = {"Punt": "play_fixture_beta"}
    recipe["expected_counts"].update(formations=2, routes=4, plays=3, handicap_tables=2)
    return recipe


class ConverterTests(unittest.TestCase):
    def setUp(self):
        self.directory = HERE / ".artifacts/tests" / self._testMethodName
        self.directory.mkdir(parents=True, exist_ok=True)
        self.addCleanup(shutil.rmtree, self.directory)
        self.documents = synthetic_documents()
        self.recipe = synthetic_recipe(self.documents)
        self.source = self.directory / "source"
        for role, pin in self.recipe["inputs"].items():
            path = self.source / pin["path"]
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(self.documents[role].encode("latin1"))

    def test_pinned_public_metadata_has_identities_not_original_arrays(self):
        recipe = json.loads((HERE / "recipe.json").read_text())
        self.assertEqual("89f840d58d152a2a9f230cb8d0cdffbd554c8ded", recipe["source_revision"])
        self.assertEqual("f5e87b921a4038d03f55174bbd5425241939e798", recipe["inputs"]["selection"]["blob_sha1"])
        self.assertEqual(226, sum(pool["count"] for pool in recipe["weighted_pools"].values()))
        self.assertEqual(6, len(recipe["singleton_pools"]))
        self.assertEqual({"formations": 29, "routes": 140, "plays": 88, "handicap_tables": 34,
                          "quarter_sines": 257, "stick_directions": 49}, recipe["expected_counts"])
        self.assertTrue(all("entries" not in value for value in recipe["weighted_pools"].values()))

    def test_weighted_order_duplicates_and_singleton_are_not_normalized(self):
        catalog, counts, _ = converter.convert_documents(self.documents, self.recipe)
        records = [row.split("\t") for row in catalog.decode().splitlines()]
        weighted = next(row for row in records if row[:2] == ["pool", "ShortOffense"])
        self.assertEqual(["play_fixture_alpha", "play_fixture_alpha", "play_fixture_beta"], weighted[5].split(","))
        singleton = next(row for row in records if row[:2] == ["pool", "Punt"])
        self.assertEqual("play_fixture_beta", singleton[5])
        self.assertEqual({"ShortOffense": 3, "Punt": 1}, counts["pool_entries"])
        self.assertEqual(["play_fixture_alpha", "play_fixture_beta", "play_fixture_defense"],
                         [row[1] for row in records if row[0] == "play"])

    def test_route_words_slot_modes_controls_and_source_lines_survive(self):
        catalog, _, opcodes = converter.convert_documents(self.documents, self.recipe)
        records = [row.split("\t") for row in catalog.decode().splitlines()]
        route = next(row for row in records if row[:2] == ["route", "pr_fixture_beta"])
        self.assertEqual("1,16,3,2,-128,4,1,5", route[4])
        self.assertGreater(int(route[3]), 1)
        self.assertEqual(self.recipe["inputs"]["playbook"]["blob_sha1"], route[2])
        formation = next(row for row in records if row[:2] == ["formation", "formation_fixture"])
        self.assertEqual("-3,-2,receiver_fixture_anim,6,2", formation[4].split(";")[4])
        defense = next(row for row in records if row[:2] == ["play", "play_fixture_defense"])
        self.assertEqual("Defense", defense[5])
        self.assertEqual("1024", defense[6])
        self.assertEqual(2, opcodes["4"])

    def test_constant_parser_matches_c_division_suffix_and_float_precision(self):
        constants = converter.Constants()
        self.assertEqual(-2, constants.integer("-7/3"))
        self.assertEqual(-2, constants.integer("7/-3"))
        self.assertEqual(31, constants.integer("0x1f"))
        self.assertEqual(159, constants.integer("0x1Fu | 0x80"))
        self.assertEqual(16, constants.integer("1u << 4"))
        self.assertEqual(0.0, constants.floating("(16777216.0f+1.0f)-16777216.0f"))
        self.assertEqual(1.0, constants.floating("(16777216.0+1.0)-16777216.0"))
        macro = struct.unpack("<I", struct.pack("<f", constants.floating("(390.0f/300.0f)*6.0f")))[0]
        literal = struct.unpack("<I", struct.pack("<f", constants.floating("7.8f")))[0]
        self.assertEqual(1090099609, macro)
        self.assertEqual(1090099610, literal)
        for value in ("1/0", "run_function()", "__import__('os')", "1**2", "unknown_name"):
            with self.subTest(value=value), self.assertRaises(converter.ConversionError):
                constants.evaluate(value)

    def test_comments_are_removed_without_destroying_literals_or_line_numbers(self):
        original = 'int x=1;/* a\nb */\nchar *s="// literal /* stays */"; // comment\n'
        result = converter.strip_comments(original)
        self.assertEqual(original.count("\n"), result.count("\n"))
        self.assertIn('"// literal /* stays */"', result)
        self.assertNotIn("// comment", result)

    def test_unknown_opcode_and_missing_references_fail_with_identity(self):
        documents = dict(self.documents)
        documents["playbook"] = documents["playbook"].replace("DS_WAIT_SNAP,DS_WAIT,3", "999,DS_WAIT,3")
        with self.assertRaisesRegex(converter.ConversionError, "pr_fixture_beta.*999"):
            converter.convert_documents(documents, self.recipe)
        documents = dict(self.documents)
        documents["playbook"] = documents["playbook"].replace("pr_fixture_alpha,pr_fixture_beta,pr_fixture_alpha",
                                                              "missing_route,pr_fixture_beta,pr_fixture_alpha")
        with self.assertRaisesRegex(converter.ConversionError, "play_fixture_alpha"):
            converter.convert_documents(documents, self.recipe)

    def test_deduplication_wrong_count_or_null_pool_reference_is_rejected(self):
        documents = dict(self.documents)
        documents["selection"] = documents["selection"].replace("&play_fixture_alpha,&play_fixture_alpha", "&play_fixture_alpha")
        with self.assertRaisesRegex(converter.ConversionError, "weighted count"):
            converter.convert_documents(documents, self.recipe)
        documents["selection"] = self.documents["selection"].replace("&play_fixture_beta};", "NULL,&play_fixture_beta};")
        with self.assertRaisesRegex(converter.ConversionError, "non-play reference"):
            converter.convert_documents(documents, self.recipe)
        recipe = copy.deepcopy(self.recipe)
        recipe["expected_counts"]["routes"] = 5
        with self.assertRaisesRegex(converter.ConversionError, "inventory"):
            converter.convert_documents(self.documents, recipe)

    def test_source_byte_hash_or_missing_input_fails_before_any_output(self):
        recipe_path = self.directory / "synthetic-recipe.json"
        recipe_path.write_text(json.dumps(self.recipe))
        source = self.source / self.recipe["inputs"]["selection"]["path"]
        source.write_bytes(source.read_bytes() + b"\n")
        output = self.directory / "output"
        with mock.patch.object(converter, "RECIPE_PATH", recipe_path):
            with self.assertRaisesRegex(converter.ConversionError, "Source hash mismatch for playsel.c"):
                converter.convert(self.source, output)
        self.assertFalse(output.exists())
        source.unlink()
        with self.assertRaisesRegex(converter.ConversionError, "Missing pinned source input playsel.c"):
            converter.read_inputs(self.source, self.recipe)

    def test_explicit_verified_overrides_do_not_bypass_hashes(self):
        path = self.directory / "verified-selection.c"
        path.write_bytes(self.documents["selection"].encode("latin1"))
        (self.source / "playsel.c").write_text("bad cached playsel")
        result = converter.read_inputs(self.source, self.recipe, {"selection": path})
        self.assertEqual(self.documents["selection"], result["selection"])
        path.write_text("not the pinned blob")
        with self.assertRaisesRegex(converter.ConversionError, "Source hash mismatch"):
            converter.read_inputs(self.source, self.recipe, {"selection": path})

    def test_only_ignored_output_and_read_only_source_are_accepted(self):
        self.assertEqual(self.directory / "output", converter.validate_output(self.directory / "output", self.source))
        with self.assertRaisesRegex(converter.ConversionError, "Output must be a child"):
            converter.validate_output(HERE / "not-ignored-output", self.source)
        with self.assertRaisesRegex(converter.ConversionError, "source root"):
            converter.validate_output(self.source / "nested-output", self.source)
        outside = self.directory / "outside-source.c"
        outside.write_text("synthetic")
        source = self.source / "playbook.c"
        source.unlink()
        source.symlink_to(outside)
        with self.assertRaisesRegex(converter.ConversionError, "escapes"):
            converter.read_inputs(self.source, self.recipe)

    def test_reproducible_writer_emits_manifest_schema_fields_without_raw_tables(self):
        recipe_path = self.directory / "synthetic-recipe.json"
        recipe_path.write_text(json.dumps(self.recipe, sort_keys=True))
        output = self.directory / "output"
        with mock.patch.object(converter, "RECIPE_PATH", recipe_path):
            first = converter.convert(self.source, output)
            catalog = (output / "catalog.tsv").read_bytes()
            manifest = (output / "manifest.json").read_bytes()
            second = converter.convert(self.source, output)
        self.assertEqual(first, second)
        self.assertEqual(catalog, (output / "catalog.tsv").read_bytes())
        self.assertEqual(manifest, (output / "manifest.json").read_bytes())
        schema = json.loads((HERE / "manifest.schema.json").read_text())
        self.assertEqual(set(schema["required"]), set(first))
        self.assertFalse(first["full_gameplay_parity_claimed"])
        self.assertEqual(64, len(first["catalog"]["sha256"]))
        self.assertNotIn("routes", first)
        self.assertNotIn("formations", first)
        self.assertTrue((output / "catalog.tsv").is_file())


if __name__ == "__main__":
    unittest.main()
