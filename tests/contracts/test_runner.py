#!/usr/bin/env python3
"""Guard tests for the actual-source regression runner; not gameplay evidence."""
import importlib.util
import os
from pathlib import Path
import re
import shutil
import unittest
from unittest.mock import patch
import uuid


SPEC = importlib.util.spec_from_file_location("contract_runner", Path(__file__).with_name("run.py"))
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


class ContractRunnerGuards(unittest.TestCase):
    def setUp(self):
        for name in ("run", "Popen"):
            blocked = patch.object(RUNNER.subprocess, name,
                                   side_effect=AssertionError("Guard-only tests must not launch children or SDK commands."))
            blocked.start()
            self.addCleanup(blocked.stop)

    def baseline(self):
        return dict(RUNNER.EXPECTED, schema="actual-source-contract-checks/v1",
                    status="passed", full_gameplay_parity_claimed=False,
                    human_onside_contract=RUNNER.HUMAN_ONSIDE_CONTRACT)

    def test_exact_assertion_inventory_is_required(self):
        RUNNER.require_baseline(0, self.baseline())
        for field in RUNNER.EXPECTED:
            for value in (0, RUNNER.EXPECTED[field] - 1, RUNNER.EXPECTED[field] + 1, None, True, 1.0):
                with self.subTest(field=field, value=value), self.assertRaises(RuntimeError):
                    summary = self.baseline()
                    summary[field] = value
                    RUNNER.require_baseline(0, summary)

    def test_status_only_zero_discovery_or_nonzero_exit_cannot_pass(self):
        for summary in (None, {}, {"status": "passed"}):
            with self.subTest(summary=summary), self.assertRaises(RuntimeError):
                RUNNER.require_baseline(0, summary)
        with self.assertRaises(RuntimeError):
            RUNNER.require_baseline(1, self.baseline())

    def test_error_or_full_parity_claim_cannot_pass(self):
        for field, value in (("status", "error"), ("schema", "different"), ("human_onside_contract", "different"),
                             ("full_gameplay_parity_claimed", True)):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                summary = self.baseline()
                summary[field] = value
                RUNNER.require_baseline(0, summary)

    def test_mutation_requires_the_actual_offending_field_assertion(self):
        for name, _, _, _, assertion in RUNNER.MUTATIONS:
            summary = {"schema": "actual-source-contract-checks/v1", "status": "failed",
                       "failed_assertion": assertion, "assertions": 1}
            with self.subTest(name=name):
                RUNNER.require_mutation(1, summary, assertion)
                summary["failed_assertion"] = "unrelated assertion"
                with self.assertRaises(RuntimeError):
                    RUNNER.require_mutation(1, summary, assertion)

    def test_compilation_failure_crash_or_surviving_mutant_is_not_a_kill(self):
        assertion = RUNNER.MUTATIONS[0][-1]
        for exit_code, summary in ((1, None), (1, {"status": "error"}), (0, self.baseline()),
                                   (134, {"schema": "actual-source-contract-checks/v1",
                                          "status": "failed", "failed_assertion": assertion, "assertions": 1}),
                                   (1, {"schema": "actual-source-contract-checks/v1", "status": "failed",
                                        "failed_assertion": assertion, "assertions": 0})):
            with self.subTest(exit_code=exit_code, summary=summary), self.assertRaises(RuntimeError):
                RUNNER.require_mutation(exit_code, summary, assertion)

    def test_all_twenty_one_anchors_name_unique_actual_source_fields(self):
        self.assertEqual(21, len(RUNNER.MUTATIONS))
        for name, filename, anchor, replacement, _ in RUNNER.MUTATIONS:
            with self.subTest(name=name):
                path = RUNNER.ROOT / "Assets/Scripts/Contracts" / filename
                self.assertEqual(1, path.read_text(encoding="utf-8").count(anchor))
                self.assertNotEqual(anchor, replacement)

    def test_snapshot_tracks_all_contracts_and_exact_pure_rule_sources(self):
        contents, hashes = RUNNER.snapshot()
        expected = {path.relative_to(RUNNER.ROOT).as_posix()
                    for path in (RUNNER.ROOT / "Assets/Scripts/Contracts").rglob("*.cs")}
        expected.update("Assets/Scripts/Gameplay/" + name for name in RUNNER.RULE_SOURCES)
        self.assertEqual(expected, set(contents))
        self.assertEqual(expected, set(hashes))
        self.assertEqual(hashes, RUNNER.hashes())

    def test_presentation_inventory_and_pure_model_include_are_required(self):
        self.assertEqual(189, RUNNER.EXPECTED["presentation_dto_assertions"])
        self.assertEqual(1744, RUNNER.EXPECTED["assertions"])
        self.assertEqual(511, RUNNER.EXPECTED["retained_assertions"])
        self.assertEqual(65, RUNNER.EXPECTED["enum_mappings"])
        path = RUNNER.ROOT / "Assets/Scripts/Contracts/SourcePresentationContracts.cs"
        self.assertIn(path, RUNNER.source_files())
        project = (RUNNER.SUITE / "model/PureContracts.csproj").read_text(encoding="utf-8")
        self.assertIn("Assets/Scripts/Contracts/**/*.cs", project)
        self.assertNotIn("Gameplay/**/*.cs", project)
        self.assertNotIn("SourceRandom.cs", project)
        self.assertNotIn("Presentation/*.cs", project)

    def test_actual_harness_snapshot_prunes_generated_trees_before_traversal(self):
        actual = RUNNER.harness_snapshot()
        self.assertTrue(actual)
        parent = Path(os.environ.get("BLITZ_CI_TEST_OUTPUT", RUNNER.ROOT / ".squad/.scratch/contract-harness-tests"))
        self.assertTrue(parent.resolve().is_relative_to(RUNNER.ROOT))
        work = parent / uuid.uuid4().hex
        work.mkdir(parents=True)
        self.addCleanup(shutil.rmtree, work)
        for name, data in actual.items():
            target = work / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        for name in (".artifacts/mutant/Program.cs", "bin/generated/Unexpected.csproj", "model/obj/Stale.cs"):
            target = work / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text("must not be a harness input")
        walk = os.walk
        visited = []
        def tracked(*args, **kwargs):
            for row in walk(*args, **kwargs):
                visited.append(Path(row[0]).relative_to(work))
                yield row
        with patch.object(RUNNER, "SUITE", work), patch.object(RUNNER.os, "walk", tracked):
            captured = RUNNER.harness_snapshot()
        self.assertEqual(captured, actual, "every actual harness/project byte is retained, without stale shadow inputs")
        self.assertFalse(any({".artifacts", "bin", "obj"} & set(path.parts) for path in visited),
                         "filtering after rglob would still race/traverse other workers' build trees")

    def test_frozen_human_onside_inventory_does_not_double_count_mappings(self):
        self.assertEqual({
            "promoted_dto_assertions": 177, "enum_mappings": 65, "additional_dto_assertions": 23,
            "presentation_dto_assertions": 189, "rules_flow_assertions": 122, "retained_assertions": 511,
            "human_onside_cases": 88, "human_onside_fields_per_case": 14,
            "human_onside_tuple_assertions": 1232, "human_onside_api_guard_assertions": 1,
            "human_onside_assertions": 1233, "assertions": 1744,
        }, RUNNER.EXPECTED)
        self.assertEqual(511, 177 + 23 + 189 + 122)
        self.assertEqual(1232, 88 * 14)
        self.assertEqual(1744, 511 + 1232 + 1)

    def test_nine_new_controls_resolve_unique_actual_gameplay_anchors(self):
        contents, _ = RUNNER.snapshot()
        self.assertEqual(9, len(RUNNER.ONSIDE_MUTATIONS))
        self.assertEqual(30, len(RUNNER.COMPILED_MUTATIONS))
        self.assertEqual(30, len({case[0] for case in RUNNER.COMPILED_MUTATIONS}))
        self.assertEqual(set(RUNNER.ONSIDE_FAILURE_COUNTS), {case[0] for case in RUNNER.ONSIDE_MUTATIONS})
        for name, filename, anchor, replacement, _ in RUNNER.COMPILED_MUTATIONS:
            with self.subTest(name=name):
                relative = RUNNER.mutation_source_path(filename, contents)
                expected_parent = "Assets/Scripts/Gameplay/" if name.startswith("onside-") else "Assets/Scripts/Contracts/"
                self.assertTrue(relative.startswith(expected_parent))
                self.assertEqual(1, contents[relative].decode("utf-8").count(anchor))
                self.assertNotEqual(anchor, replacement)
        self.assertEqual(RUNNER.FLOW_SOURCE, RUNNER.mutation_source_path("SourceMatchFlow.cs", contents))

    def test_mutation_path_resolution_rejects_missing_and_ambiguous_sources(self):
        with self.assertRaises(RuntimeError):
            RUNNER.mutation_source_path("SourceMatchFlow.cs", {})
        with self.assertRaises(RuntimeError):
            RUNNER.mutation_source_path("SourceMatchFlow.cs", {
                RUNNER.FLOW_SOURCE: b"actual", "Assets/Scripts/Contracts/SourceMatchFlow.cs": b"decoy"
            })

    def test_new_mutants_require_prefrozen_exact_partial_partitions(self):
        frozen = {
            "onside-held-replaced-by-pressed": (512, 1, 0),
            "onside-all-buttons-replaced-by-any": (1422, 911, 65),
            "onside-up-equality-replaced-by-inclusion": (540, 29, 2),
            "onside-kicking-team-filter-removed": (554, 43, 3),
            "onside-team-replaced-by-station-id": (1184, 673, 48),
            "onside-second-teammate-ignored": (1408, 897, 64),
            "onside-stored-request-discarded": (1576, 1065, 76),
            "onside-half-argument-discarded": (1660, 1149, 82),
            "onside-first-argument-discarded": (1618, 1107, 79),
        }
        self.assertEqual(frozen, RUNNER.ONSIDE_FAILURE_COUNTS)
        for name, _, _, _, assertion in RUNNER.ONSIDE_MUTATIONS:
            total, partial, completed = frozen[name]
            summary = self.baseline()
            summary.update(status="failed", failed_assertion=assertion, assertions=total,
                           human_onside_tuple_assertions=partial, human_onside_api_guard_assertions=0,
                           human_onside_assertions=partial, human_onside_cases=completed)
            with self.subTest(name=name):
                RUNNER.require_mutation(1, summary, assertion)
                for field in RUNNER.EXPECTED:
                    for value in (summary[field] + 1, bool(summary[field]), float(summary[field])):
                        bad = dict(summary, **{field: value})
                        with self.subTest(field=field, value=value), self.assertRaises(RuntimeError):
                            RUNNER.require_mutation(1, bad, assertion)
                with self.assertRaises(RuntimeError):
                    RUNNER.require_mutation(134, summary, assertion)
                with self.assertRaises(RuntimeError):
                    RUNNER.require_mutation(1, dict(summary, failed_assertion="unrelated assertion"), assertion)

    def test_old_mutants_cannot_fail_after_the_retained_511_prefix(self):
        assertion = RUNNER.MUTATIONS[0][-1]
        for count in (512, 1744, 1.0, True):
            with self.subTest(count=count), self.assertRaises(RuntimeError):
                RUNNER.require_mutation(1, {"schema": "actual-source-contract-checks/v1", "status": "failed",
                                          "failed_assertion": assertion, "assertions": count}, assertion)

    def test_actual_caller_wiring_and_legacy_body_are_static_only(self):
        flow = (RUNNER.ROOT / RUNNER.FLOW_SOURCE).read_bytes()
        caller = (RUNNER.ROOT / RUNNER.CALLER_SOURCE).read_bytes()
        result = RUNNER.require_caller_wiring(flow, caller)
        self.assertEqual("passed-static-only", result["status"])
        self.assertFalse(result["compiled"])
        self.assertFalse(result["simulation_executed"])
        self.assertEqual(RUNNER.CALLER_SOURCE, result["production_file"])
        self.assertEqual(RUNNER.CALLER_PREEDIT_SHA256, result["preedit_source_sha256"])

    def test_all_five_actual_byte_controls_reject_exact_static_fields(self):
        flow = (RUNNER.ROOT / RUNNER.FLOW_SOURCE).read_bytes()
        caller = (RUNNER.ROOT / RUNNER.CALLER_SOURCE).read_bytes()
        results = RUNNER.static_caller_controls(flow, caller)
        self.assertEqual(5, len(results))
        self.assertEqual([(case[0], case[-1]) for case in RUNNER.STATIC_CONTROLS],
                         [(row["id"], row["rejected_by_static_field"]) for row in results])
        for row in results:
            self.assertEqual("rejected-static-only", row["status"])
            for field in ("compiled", "behaviorally_rejected", "simulation_executed", "live_source_modified"):
                self.assertFalse(row[field])
        self.assertEqual(caller, (RUNNER.ROOT / RUNNER.CALLER_SOURCE).read_bytes())

    def test_comments_and_string_decoys_cannot_supply_actual_wiring(self):
        flow = (RUNNER.ROOT / RUNNER.FLOW_SOURCE).read_bytes()
        caller = (RUNNER.ROOT / RUNNER.CALLER_SOURCE).read_bytes()
        decoys = ("\n/* { } fake wiring:\n" + RUNNER.CURRENT_CALL + "\n" + RUNNER.CURRENT_ASSIGNMENT
                  + RUNNER.CPU_GUARD + '\n*/\nclass Decoy { const string Text = "SourceMatchFlow.PrepareKickoff({});"; }\n')
        for case in RUNNER.STATIC_CONTROLS:
            with self.subTest(name=case[0]):
                changed = RUNNER.mutate_static_caller(case, caller) + decoys.encode("utf-8")
                with self.assertRaises(RUNNER.StaticAssertionFailure) as error:
                    RUNNER.require_caller_wiring(flow, changed)
                self.assertEqual(case[-1], error.exception.field)

    def test_token_and_brace_parser_ignores_comments_and_literals(self):
        fixture = ('class Example {\n void Sample(int x)\n {\n'
                   ' string text = "} // SourceMatchFlow.PrepareKickoff({});"; /* } { */\n'
                   ' string verbatim = @"{ "" }"; char brace = \'}\';\n'
                   ' if (x > 0) { x--; }\n }\n void Other() { }\n}\n')
        actual = RUNNER.method_text(fixture, "Sample", "int x")
        self.assertIn("x--;", actual)
        self.assertNotIn("void Other", actual)
        self.assertEqual([], RUNNER.token_locations(RUNNER.csharp_tokens(actual), "SourceMatchFlow.PrepareKickoff("))

    def test_wrong_constructor_configuration_or_reset_fails_its_static_field(self):
        flow = (RUNNER.ROOT / RUNNER.FLOW_SOURCE).read_bytes()
        caller = (RUNNER.ROOT / RUNNER.CALLER_SOURCE).read_bytes()
        for anchor, replacement, field in (
            (RUNNER.CURRENT_CALL, RUNNER.CURRENT_CALL.replace("first, half,", "first, false,"),
             "caller.plan.original-input-arguments"),
            (RUNNER.CURRENT_CALL, RUNNER.CURRENT_CALL.replace("sourceConfiguration,", "Model.SourceConfiguration,"),
             "caller.plan.configured-stations"),
            ("Model.SourceRequestedOnside = false;", "Model.SourceRequestedOnside = true;",
             "caller.only-authorized-call-changed-captain-preserved"),
        ):
            with self.subTest(field=field), self.assertRaises(RUNNER.StaticAssertionFailure) as error:
                changed = RUNNER.replace_unique(caller.decode("utf-8"), anchor, replacement, field).encode("utf-8")
                RUNNER.require_caller_wiring(flow, changed)
            self.assertEqual(field, error.exception.field)
        changed_flow = RUNNER.replace_unique(flow.decode("utf-8"), "if (input.RequestedOnsideKick && !input.HalfKickoff)",
                                            "if (input.RequestedOnsideKick)", "legacy-body").encode("utf-8")
        with self.assertRaises(RUNNER.StaticAssertionFailure) as error:
            RUNNER.require_caller_wiring(changed_flow, caller)
        self.assertEqual("flow.legacy-one-argument-body-preserved", error.exception.field)

    def test_literal_matrix_has_64_plus_24_rows_and_fourteen_named_fields(self):
        source = (RUNNER.SUITE / "RulesFlowChecks.cs").read_text(encoding="utf-8")
        tokens = RUNNER.csharp_tokens(source)
        for name, count in (("coreTuples", 64), ("edgeTuples", 24)):
            locations = RUNNER.token_locations(tokens, name + " = {")
            self.assertEqual(1, len(locations))
            opening = locations[0] + 2
            closing = RUNNER.matching_token(tokens, opening, "{", "}")
            table = source[tokens[opening][1]:tokens[closing][2]]
            rows = re.findall(r"new object\[\] \{ ([^{}\n]+) \}", table)
            self.assertEqual(count, len(rows))
            for row in rows:
                fields = row.split(", ")
                self.assertEqual(14, len(fields))
                self.assertRegex(fields[0], r"^SourceKickoffKind\.(Opening|Quick|Onside)$")
                self.assertRegex(fields[1], r"^SourceGameFlags\.\w+( \| SourceGameFlags\.\w+)?$")
                self.assertTrue(all(re.fullmatch(r"\d+|true|false", value) for value in fields[2:]))
        edges = re.findall(r'"human-onside/edge/(\d\d)-[^"]+"', source)
        self.assertEqual(["%02d" % value for value in range(1, 25)], edges)
        self.assertEqual(list(range(24)), [int(value) for value in re.findall(r"edgeTuples\[(\d+)\]", source)])
        helper = RUNNER.method_text(source, "HumanOnsideTuple", "string label, SourceKickoffPlan actual, object[] expected")
        self.assertEqual([
            "Kind", "Flags", "KickingSide", "ReceivingSide", "Captain", "AutoHikeTicks", "LastPossession",
            "State.OffenseSide", "State.InitialOffenseSide", "State.Down", "State.LineOfScrimmage",
            "State.FirstDown", "State.NoFirstDown", "FirstKickoffAfterPreparation",
        ], re.findall(r'label \+ "/([^"]+)"', helper))
        self.assertEqual(14, len(RUNNER.token_locations(RUNNER.csharp_tokens(helper), "Eq(")))

    def test_null_configuration_guard_is_separate_and_uses_the_actual_api(self):
        source = (RUNNER.SUITE / "RulesFlowChecks.cs").read_text(encoding="utf-8")
        guard = RUNNER.method_text(source, "RunHumanOnsideApiGuard", "")
        tokens = RUNNER.csharp_tokens(guard)
        self.assertEqual(1, len(RUNNER.token_locations(tokens, "Checks.Throws<ArgumentNullException>(")))
        self.assertEqual(1, len(RUNNER.token_locations(tokens, "SourceMatchFlow.PrepareKickoff(")))
        self.assertNotIn("HumanOnsideTuple(", guard)
        program = (RUNNER.SUITE / "Program.cs").read_text(encoding="utf-8")
        self.assertLess(program.index("RulesFlowChecks.Run();"), program.index("RulesFlowChecks.RunHumanOnsideInputs();"))
        self.assertLess(program.index("RulesFlowChecks.RunHumanOnsideInputs();"), program.index("RulesFlowChecks.RunHumanOnsideApiGuard();"))

    def test_harness_identity_includes_python_guards_runner_and_direct_docs(self):
        snapshot = RUNNER.harness_snapshot()
        for name in ("run.py", "test_runner.py", "README.md", "Program.cs", "RulesFlowChecks.cs"):
            self.assertIn(Path(name), snapshot)


if __name__ == "__main__":
    unittest.main()
