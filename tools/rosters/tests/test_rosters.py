import copy
import os
import pathlib
import subprocess
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import export_rosters as exporter
from c_source import CData, Expressions, Macro, Scalar, SourceError, f32, preprocess, without_comments
from fixtures import synthetic_dataset, team_sources


class ExpressionTests(unittest.TestCase):
    def test_scale_uses_binary32_not_guessed_one(self):
        data = CData("#define SC(x) (1.05f * x)\nfloat s[]={SC(1.0f),SC(2.0f)};\n")
        values, _ = data.array("s")
        self.assertEqual(data.number(values[0]), f32(1.05))
        self.assertNotEqual(data.number(values[0]), 1.0)
        self.assertEqual(data.number(values[1]), f32(f32(1.05) * f32(2)))

    def test_alias_enum_and_signed_integer_semantics(self):
        data = CData("#define ALIAS SECOND\nenum { FIRST=4, SECOND, THIRD=0x10 };\nint a[]={ALIAS,-7/3,-7%3,010,THIRD};").enums()
        values, _ = data.array("a")
        self.assertEqual([data.number(value) for value in values], [5, -2, -1, 8, 16])

    def test_macro_expansion_is_bounded_and_not_eval(self):
        for expression in ('__import__("os")', 'system("x")', '(int)1', '1 ? 2 : 3', 'a[0]', '0xGG', '1;2', '08'):
            with self.subTest(expression=expression), self.assertRaises(SourceError):
                Expressions().evaluate(expression)
        with self.assertRaisesRegex(SourceError, "cycle"):
            Expressions({"A": Macro("B"), "B": Macro("A")}).evaluate("A")
        with self.assertRaisesRegex(SourceError, "token limit"):
            Expressions().evaluate("!" * 300 + "1")

    def test_original_define_undef_scope_is_retained(self):
        data = CData("#define x 3\nint a[]={x};\n#undef x\n#define x 9\nint b[]={x};\n")
        self.assertEqual(data.number(data.array("a")[0][0]), 3)
        self.assertEqual(data.number(data.array("b")[0][0]), 9)

    def test_preprocessor_qualified_branches_and_platform(self):
        source = """#if 0
int a[]={99};
#elif defined(SEATTLE)
int a[]={7};
#else
int a[]={9};
#endif
"""
        self.assertEqual(CData(source, {"SEATTLE": Macro("1")}).number(CData(source, {"SEATTLE": Macro("1")}).array("a")[0][0]), 7)
        other = CData(source)
        self.assertEqual(other.number(other.array("a")[0][0]), 9)

    def test_comments_do_not_create_data_or_damage_strings(self):
        data = CData('/* int fake[]={1}; */\nchar *a[]={"A//B", "C/*D*/", "escaped\\\\text"}; // ignored\n')
        values, _ = data.array("a")
        self.assertEqual([data.string(value) for value in values], ["A//B", "C/*D*/", "escaped\\text"])
        with self.assertRaisesRegex(SourceError, "Unterminated"):
            without_comments("/* broken")

    def test_duplicate_active_declaration_and_designator_fail(self):
        with self.assertRaisesRegex(SourceError, "one active"):
            CData("int a[]={1};int a[]={2};").array("a")
        with self.assertRaisesRegex(SourceError, "designator"):
            CData("int a[]={.member=1};").array("a")
        for source in ("#endif\n", "#if 1\n", "#if 1\n#else\n#else\n#endif\n", "#error invalid\n"):
            with self.subTest(source=source), self.assertRaises(SourceError):
                preprocess(source)

    def test_function_scope_arrays_keep_physical_source_lines(self):
        data = CData("int a[]={0};\n\nvoid f(void){\nint a[]={5};\n}\n")
        values, _ = data.array("a", data.function("f"))
        self.assertEqual(values[0].line, 4)
        self.assertEqual(data.number(values[0]), 5)


class RosterTests(unittest.TestCase):
    def parse(self, text=None, game=None, **options):
        original, original_game = team_sources(**options)
        game = original_game if game is None else game
        return exporter.parse_teams(CData(original if text is None else text, game.expressions.macros), game)

    def test_all_original_indices_and_bcd_fields_preserved(self):
        teams = self.parse()
        self.assertEqual([team["id"] for team in teams], list(range(31)))
        players = [player for team in teams for player in team["players"]]
        self.assertEqual([player["source_static_id"] for player in players], list(range(496)))
        self.assertEqual(len({player["id"] for player in players}), 496)
        self.assertEqual(players[0]["statistics"], list(range(6)))
        self.assertEqual(players[0]["number_packed_bcd"], 0x54)
        self.assertEqual(players[0]["jersey_number"], 54)
        self.assertEqual(players[0]["jersey_digit_indices"], [4, 3])
        self.assertEqual(players[0]["scale"], f32(1.05))
        self.assertIsNone(players[0]["height_inches"])

    def test_invalid_team_count_and_record_count_are_not_padded(self):
        with self.assertRaisesRegex(SourceError, "31"):
            self.parse(team_count=30)
        with self.assertRaisesRegex(SourceError, "sixteen"):
            self.parse(record_count=15)

    def test_duplicate_team_id_fails(self):
        text, game = team_sources()
        broken = CData(game.text + "\n#define TEAM_FIXTURE_XAB 0\n", game.expressions.macros)
        with self.assertRaisesRegex(SourceError, "Duplicate original team ID"):
            self.parse(text, broken)

    def test_missing_roster_reference_fails(self):
        text, game = team_sources()
        text = text.replace('"XAA","XAA",spd_xaa', '"XAA","XAA",spd_missing')
        with self.assertRaises(SourceError):
            self.parse(text, game)

    def test_duplicate_roster_reference_fails(self):
        text, game = team_sources()
        text = text.replace('"XAB","XAB",spd_xab', '"XAB","XAB",spd_xaa')
        with self.assertRaisesRegex(SourceError, "Duplicate/missing"):
            self.parse(text, game)

    def test_changed_team_order_not_silently_relabelled(self):
        text, game = team_sources()
        text = text.replace('"XAA","XAA",spd_xaa', '"XAA","XAB",spd_xab')
        with self.assertRaisesRegex(SourceError, "order"):
            self.parse(text, game)

    def test_invalid_bcd_and_unknown_enum_fail(self):
        text, game = team_sources()
        with self.assertRaisesRegex(SourceError, "BCD"):
            self.parse(text.replace("0x54", "0x5A", 1), game)
        with self.assertRaisesRegex(SourceError, "Undefined"):
            self.parse(text.replace("PS_NORM,", "PS_UNKNOWN,", 1), game)

    def test_unknown_extra_roster_requires_separate_catalog(self):
        text, game = team_sources()
        text += "const struct st_plyr_data spd_extra[] = {};\n"
        with self.assertRaisesRegex(SourceError, "separate catalog"):
            self.parse(text, game)

    def test_synthetic_output_is_detached_and_deterministic(self):
        first, second = synthetic_dataset(), synthetic_dataset()
        self.assertEqual(exporter.json_bytes(first), exporter.json_bytes(second))
        first["teams"][0]["players"][0]["raw_fields"][0] = "mutated"
        self.assertEqual(second["teams"][0]["players"][0]["raw_fields"][0], "0")

    def test_missing_reference_and_missing_list_mutations_fail(self):
        doc = synthetic_dataset()
        # Cross-check names are intentionally only present in the actual-source verification path.
        doc["teams"][5]["players"][3].update(firstname="Cade", lastname="McNown",
                                            position={"raw": "PP_QB", "symbol": "PP_QB", "value": 0})
        exporter.validate_dataset(doc)
        damaged = copy.deepcopy(doc)
        damaged["uniforms"][0]["slot_asset_ids"][0] = "missing/binding"
        with self.assertRaisesRegex(SourceError, "Missing asset binding"):
            exporter.validate_dataset(damaged)
        damaged = copy.deepcopy(doc)
        damaged["missing_asset_ids"].pop()
        with self.assertRaisesRegex(SourceError, "Missing-asset"):
            exporter.validate_dataset(damaged)


class PinTests(unittest.TestCase):
    def test_source_pin_verification_checks_git_blob_not_plain_sha1(self):
        data = b"synthetic original fixture\n"
        blob = exporter.git_hash("blob", data)
        pin = {"path": "fixture.c", "bytes": len(data), "git_blob_sha1": blob, "sha256": exporter.digest(data)}
        entry = {"type": "blob", "mode": "100644", "sha": blob}
        exporter.verify_source_bytes(data, pin, entry)
        with self.assertRaisesRegex(SourceError, "content pin"):
            exporter.verify_source_bytes(data.replace(b"original", b"mutated!"), pin, entry)
        damaged = dict(entry, sha="0" * 40)
        with self.assertRaisesRegex(SourceError, "linkage"):
            exporter.verify_source_bytes(data, pin, damaged)

    def test_manifest_itself_is_pinned_before_reading_inputs(self):
        data = exporter.INPUT_MANIFEST.read_bytes()
        self.assertEqual(exporter.digest(data), exporter.MANIFEST_SHA256)
        with self.assertRaisesRegex(SourceError, "manifest SHA256"):
            exporter.SourceSet(exporter.ROOT, data + b" ")
        changed = exporter.read_json(data)
        changed["commit"] = "0" * 40
        with self.assertRaisesRegex(SourceError, "manifest SHA256"):
            exporter.SourceSet(exporter.ROOT, exporter.json_bytes(changed))

    def test_tree_metadata_is_reconstructed_not_trusted(self):
        blob = exporter.git_hash("blob", b"fixture\n")
        payload = b"100644 fixture.c\0" + bytes.fromhex(blob)
        tree = exporter.git_hash("tree", payload)
        metadata = {"sha": tree, "truncated": False, "tree": [
            {"path": "fixture.c", "sha": blob, "type": "blob", "mode": "100644"}]}
        self.assertEqual(exporter.verify_tree(metadata, tree)["fixture.c"]["sha"], blob)
        changed = copy.deepcopy(metadata)
        changed["tree"][0]["sha"] = "0" * 40
        with self.assertRaisesRegex(SourceError, "content pin"):
            exporter.verify_tree(changed, tree)
        changed = copy.deepcopy(metadata)
        changed["tree"].append(dict(changed["tree"][0]))
        with self.assertRaisesRegex(SourceError, "Duplicate"):
            exporter.verify_tree(changed, tree)

    def test_duplicate_json_and_untrusted_output_path_fail(self):
        with self.assertRaisesRegex(SourceError, "Duplicate JSON"):
            exporter.read_json('{"schema":1,"schema":2}')
        with self.assertRaisesRegex(SourceError, "Nonfinite"):
            exporter.read_json('{"value":NaN}')
        for path in ("Assets/rosters.json", "tools/rosters/source_inputs.json", "../outside.json"):
            with self.subTest(path=path), self.assertRaisesRegex(SourceError, "ignored"):
                exporter.output_path(path)

    def test_offline_cli_failure_has_no_partial_dataset(self):
        destination = exporter.ROOT / "tools/rosters/SourceData/rejected-cli.json"
        self.assertFalse(destination.exists())
        result = subprocess.run([sys.executable, str(exporter.ROOT / "tools/rosters/export_rosters.py"),
                                 "--source-root", str(exporter.ROOT / "tools/rosters/no-such-source"),
                                 "--output", str(destination)], capture_output=True, text=True, check=False)
        self.assertEqual(result.returncode, 2)
        self.assertIn("roster export rejected", result.stderr)
        self.assertFalse(destination.exists())

    def test_output_replaces_entry_without_truncating_hardlinked_input(self):
        root = exporter.ROOT / "tools/rosters/SourceData"
        root.mkdir(parents=True, exist_ok=True)
        source = root / ("synthetic-write-source-" + str(os.getpid()) + ".bin")
        output = root / ("synthetic-write-output-" + str(os.getpid()) + ".json")
        created = []
        try:
            with source.open("xb") as stream:
                created.append(source)
                stream.write(b"original synthetic input")
            os.link(source, output)
            created.append(output)
            exporter.write_output(output, b'{"synthetic":"output"}\n')
            self.assertEqual(source.read_bytes(), b"original synthetic input")
            self.assertEqual(output.read_bytes(), b'{"synthetic":"output"}\n')
            self.assertFalse(source.samefile(output))
        finally:
            for path in created:
                path.unlink()


if __name__ == "__main__":
    unittest.main()
