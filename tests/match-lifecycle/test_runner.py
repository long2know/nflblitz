#!/usr/bin/env python3
"""Asset-free lifecycle runner guards; synthetic summaries never execute gameplay."""
import copy
import importlib.util
from pathlib import Path
import unittest

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("match_lifecycle_runner", HERE / "run.py")
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)


class LifecycleRunnerGuards(unittest.TestCase):
    def captain_record(self, identity):
        expected, configuration, caller, fields = RUNNER.captain_expectation(identity)
        queued = caller == "queued-game_proc"
        before = dict(host_tick=60, source_tick=57, captain=configuration["initial_captain"],
                      offense=expected["old_offense"], initial_offense=expected["initial_offense"],
                      last_possession=expected["last_possession"], first_possession=configuration["first_possession"],
                      play_type=expected["saved_ending_type"], phase="LiveBall", match_phase="LiveBall",
                      result_pending=False, end_epoch=0, home_score=7, away_score=3,
                      down=4 if identity.endswith(RUNNER.CAPTAIN_SCENARIOS[0]) else 2, los=30, first_down=60,
                      flags=expected["flags"], play_end_x=expected["raw_play_end_x"], line_position_z=0,
                      clock_packed=0x3c07, clock_local=0x3c07, clock_active=True, clock_play_time=0,
                      clock_next_wake_tick=58, dispatch_ordinal=24, game_proc_last_wake_tick=57,
                      process_serials=[1, 16, 17, 18], compatibility=configuration["compatibility"],
                      repair_count=0, rng_trace_count=10, rng_draws=10)
        home_points = 2 if expected["result"] == "Safety" and expected["resolved_offense"] == "Away" else 0
        away_points = 2 if expected["result"] == "Safety" and expected["resolved_offense"] == "Home" else 0
        commit = dict(before, host_tick=62 if queued else 60, source_tick=58 if queued else 57,
                      captain=expected["captain"], offense=expected["resolved_offense"], play_type=expected["next_type"],
                      phase="DeadBall", match_phase="PlayResult", end_epoch=1 if queued else 0,
                      home_score=7 + home_points, away_score=3 + away_points, down=expected["down"], los=expected["los"],
                      first_down=expected["first_down"], clock_packed=0x3c00, clock_active=False,
                      clock_next_wake_tick=59 if queued else 58, game_proc_last_wake_tick=58 if queued else 57)
        queue = dict(before, phase="DeadBall", match_phase="PlayResult", result_pending=True, end_epoch=1) if queued else None
        wakes = [dict(commit, host_tick=commit["host_tick"] + offset,
                      source_tick=(commit["host_tick"] + offset) * 57 // 60) for offset in range(1, 5)]
        continued = dict(wakes[-1], host_tick=wakes[-1]["host_tick"] + 2,
                         source_tick=(wakes[-1]["host_tick"] + 2) * 57 // 60,
                         phase="PreSnap" if expected["next_type"] == "Kickoff" else "PlaySelect")
        consumer = None
        if expected["predicate"]:
            consumer = dict(
                path="public station selection -> FinishSourcePlaySelection -> LineUpSourceActors -> ConfigureSourceStations",
                state=dict(continued, phase="PreSnap", host_tick=continued["host_tick"] + 2),
                stations=[dict(station=i, actor=(i // 2) * 7 + i % 2,
                               control=8 if i % 2 == (0 if expected["captain"] == "First" else 1) else 16)
                          for i in range(4)])
        actual = dict(
            startup=dict(before, phase="PreSnap"), before=before, queue=queue,
            skipped_host=dict(queue, host_tick=61) if queued else None, commit=commit,
            commit_c2_events=[dict(source_tick=58, epoch=1, process="game_proc", process_order=0,
                                  source_path="nfl.c", function="game_proc", line=1381)] if queued else [],
            result=dict(kind=expected["result"], ending_type=expected["saved_ending_type"], cause=expected["cause"],
                        status="Classified", home_points=home_points, away_points=away_points,
                        turnover_on_downs=identity.endswith(RUNNER.CAPTAIN_SCENARIOS[0]), first_down=False),
            source_events=["PlayResolved"],
            legacy_events=["TurnoverOnDowns", "PossessionChanged"] if identity.endswith(RUNNER.CAPTAIN_SCENARIOS[0]) else (
                ["Safety"] if expected["result"] == "Safety" else []),
            displayed_lines=[30, 60], commit_boundary_rng_calls=0, later_publish=dict(commit),
            later_host_wakes=wakes, continue_kickoff_policy="synthetic parser guard only",
            consumer=consumer, later=dict(consumer["state"] if consumer else continued))
        actual["continue"] = continued
        player = (7 if expected["old_offense"] == "Away" else 0) + 3
        actual["source_event_context"] = [dict(
            type="PlayResolved", team=expected["old_offense"], actor=player, target=-1, station=-1, value=0,
            play_kind=expected["saved_ending_type"], host_tick=commit["host_tick"], source_tick=commit["source_tick"])]
        legacy_team = expected["resolved_offense"] if identity.endswith(RUNNER.CAPTAIN_SCENARIOS[0]) else (
            "Away" if expected["old_offense"] == "Home" else "Home")
        actual["legacy_event_context"] = [dict(
            type=kind, team=legacy_team, player=player, actor=player, target=-1,
            value=2 if kind == "Safety" else 0, host_tick=commit["host_tick"], source_tick=commit["source_tick"])
            for kind in actual["legacy_events"]]
        return dict(id=identity, profile=identity.split("/")[1], declared_boundary_fixture=True, caller=caller,
                    configuration=configuration, expected=expected, assertions=len(fields),
                    invariants=dict.fromkeys(fields, True), actual=actual)

    def summary(self):
        retained = sorted(RUNNER.required_cases())
        added = RUNNER.ordered_captain_cases()
        return dict(
            schema="source-match-ended-caller-checks/v1", status="passed", assertions=2371,
            cases=retained + added, retained=dict(assertions=593, cases=retained),
            captain_transition=dict(
                schema="source-post-result-captain-checks/v1", contract="source-post-result-captain-transition/v1",
                assertions=1778, expected_assertions=1778, cases=added, records=[self.captain_record(identity) for identity in added],
                qualifying_cases=24, guard_negative_cases=48, whole_game_or_native_qualified=False),
            whole_game_or_native_qualified=False, declared_boundary_cases_not_public_full_match=True)

    def retained_failure(self, named="causal contract"):
        summary = self.summary()
        identities = summary["retained"]["cases"][:1]
        summary.update(status="failed", assertions=25, cases=identities, failed_assertion=named)
        summary["retained"] = dict(assertions=25, cases=identities)
        summary["captain_transition"].update(assertions=0, cases=[], records=[])
        return summary

    def captain_failure(self, mutation):
        summary = self.summary()
        ending = mutation[0] == "captain-wrong-ending-type"
        identities = RUNNER.ordered_captain_cases()[:4 if ending else 1]
        rows = [self.captain_record(identity) for identity in identities]
        failed = rows[-1]
        fields = set(RUNNER.CAPTAIN_COMMON_FIELDS[:4]) | {"immediate-EndSourcePlay/commit/caller", RUNNER.CAPTAIN_FIELD}
        failed.update(assertions=6, invariants=dict.fromkeys(fields, True))
        failed["invariants"][RUNNER.CAPTAIN_FIELD] = False
        failed["actual"]["commit"]["captain"] = "Second" if failed["expected"]["captain"] == "First" else "First"
        count = 77 if ending else 7
        summary["captain_transition"].update(assertions=count, cases=identities, records=rows)
        summary.update(status="failed", assertions=593 + count,
                       cases=summary["retained"]["cases"] + identities, failed_assertion=mutation[4])
        return summary

    def test_exact_positive_inventory_and_scope_are_required(self):
        RUNNER.require_summary(0, self.summary())
        for key, value in (
            ("assertions", 2370), ("status", "bounded-passed"), ("schema", "other"),
            ("whole_game_or_native_qualified", True), ("declared_boundary_cases_not_public_full_match", False),
            ("cases", self.summary()["cases"][:-1]), ("cases", ["substituted"] + self.summary()["cases"][1:]),
            ("cases", self.summary()["cases"][:-1] + self.summary()["cases"][:1]),
        ):
            summary = self.summary()
            summary[key] = value
            with self.subTest(key=key, value=value), self.assertRaises(RuntimeError):
                RUNNER.require_summary(0, summary)

    def test_empty_error_or_nonzero_process_cannot_be_positive(self):
        for code, summary in ((0, None), (0, {}), (1, self.summary()), (134, self.summary())):
            with self.subTest(code=code), self.assertRaises(RuntimeError):
                RUNNER.require_summary(code, summary)

    def test_rejected_baselines_surface_the_actual_runtime_error_without_accepting_it(self):
        summary = self.summary()
        identities = summary["retained"]["cases"][:1]
        summary.update(status="error", assertions=265, cases=identities, error="actual camera provider has no produced output")
        summary["retained"] = dict(assertions=265, cases=identities)
        summary["captain_transition"].update(assertions=0, cases=[], records=[])
        with self.assertRaisesRegex(RuntimeError, "actual camera provider has no produced output"):
            RUNNER.require_summary(1, summary)

    def test_PAT_setup_produces_real_camera_before_the_declared_boundary_without_assigning_one(self):
        source = (HERE / "Program.cs").read_text()
        start = source.index("private static void Pat(")
        pat = source[start:source.index("private static void Legacy()", start)]
        self.assertLess(pat.index("i < SliceRules.TicksPerSecond"), pat.index("Prepare(game,"))
        self.assertIn("game.Step(default(PlayerCommand));", pat)
        self.assertIn("game.SourceTick != SourceTiming.DefaultTicksPerSecond", pat)
        self.assertIn("game.Model.SourceClockRemainder != 0", pat)
        self.assertIn("game.Model.SourceMovementCameraTick != game.SourceTick", pat)
        self.assertNotIn("SourceMovementCamera =", pat.replace("SourceMovementCamera ==", ""))

    def test_controls_require_exact_named_runtime_failure_not_compile_or_unrelated_error(self):
        summary = self.retained_failure()
        RUNNER.require_summary(1, summary, "causal contract")
        for code, changes in ((0, {}), (2, {}), (134, {}), (1, {"status": "error"}),
                              (1, {"failed_assertion": "unrelated"}), (1, {"assertions": 0})):
            with self.subTest(code=code, changes=changes), self.assertRaises(RuntimeError):
                RUNNER.require_summary(code, dict(summary, **changes), "causal contract")

    def test_all_nine_controls_are_unique_actual_production_edits(self):
        self.assertEqual([
            "missing-source-notification", "duplicate-source-notification", "wrong-source-notification",
            "missing-legacy-notification", "invented-terminal-actor", "premature-nonterminal-notification",
            "stale-gameover-event-batch", "invented-terminal-RNG", "disconnected-source-bridge",
        ], [item[0] for item in RUNNER.RETAINED_MUTANTS])
        self.assertEqual(9, len({item[0] for item in RUNNER.RETAINED_MUTANTS}))
        for identity, name, old, new, expected in RUNNER.RETAINED_MUTANTS:
            with self.subTest(identity=identity):
                self.assertEqual(1, (RUNNER.ROOT / name).read_text().count(old))
                self.assertNotEqual(old, new)
                self.assertGreater(len(expected), 35)

    def test_real_bridge_is_local_to_this_compile_closure_not_a_global_inventory_rebase(self):
        original = RUNNER.production_paths()
        self.assertEqual(103, len(original))
        self.assertIn(RUNNER.ROOT / "Assets/Scripts/Gameplay/GameSimulation.Possession.cs", original)
        self.assertIn(RUNNER.ROOT / "Assets/Scripts/Gameplay/SourcePlaySelectionTimer.cs", original)
        self.assertIn(RUNNER.ROOT / "Assets/Scripts/AssetImport/SourceNormalCameraData.cs", original)
        self.assertIn(RUNNER.ROOT / "Assets/Scripts/Contracts/SourceCameraContracts.cs", original)
        self.assertIn(RUNNER.ROOT / "Assets/Scripts/AssetImport/SourceInterfaceData.DefaultAudibles.cs", original)
        self.assertNotIn(RUNNER.ROOT / RUNNER.BRIDGE, original)
        project = (HERE / "MatchLifecycleChecks.csproj").read_text()
        self.assertIn('$(BlitzSourceRoot)Assets/Scripts/Runtime/FixedStepBridge.cs', project)
        self.assertIn("../parity/FullKernelInputs.cs", project)
        self.assertIn("!match-lifecycle/MatchLifecycleChecks.csproj", (RUNNER.ROOT / "tests/.gitignore").read_text())
        self.assertEqual(71, len(RUNNER.required_cases()))

    def test_fixed_partitions_cannot_be_rebalanced_while_preserving_the_union(self):
        for retained_assertions, added_assertions in ((592, 1779), (594, 1777), (0, 2371), (2371, 0)):
            summary = self.summary()
            summary["retained"]["assertions"] = retained_assertions
            summary["captain_transition"]["assertions"] = added_assertions
            with self.subTest(retained=retained_assertions, added=added_assertions), self.assertRaises(RuntimeError):
                RUNNER.require_summary(0, summary)
        for partition in ("retained", "captain_transition"):
            summary = self.summary()
            del summary[partition]
            with self.subTest(partition=partition), self.assertRaises(RuntimeError):
                RUNNER.require_summary(0, summary)
        summary = self.summary()
        moved = summary["captain_transition"]["cases"].pop()
        summary["retained"]["cases"].append(moved)
        with self.assertRaises(RuntimeError):
            RUNNER.require_summary(0, summary)

    def test_declared_matrix_freezes_72_cases_24_qualifying_48_guards_and_1778_assertions(self):
        self.assertEqual(72, len(RUNNER.required_captain_cases()))
        self.assertEqual(143, len(RUNNER.required_cases() | RUNNER.required_captain_cases()))
        self.assertFalse(RUNNER.required_cases() & RUNNER.required_captain_cases())
        self.assertEqual((593, 1778, 2371), (RUNNER.RETAINED_ASSERTIONS, RUNNER.CAPTAIN_ASSERTIONS, RUNNER.TOTAL_ASSERTIONS))
        rows = [self.captain_record(identity) for identity in RUNNER.ordered_captain_cases()]
        self.assertEqual(24, sum(row["expected"]["predicate"] for row in rows))
        self.assertEqual(48, sum(not row["expected"]["predicate"] for row in rows))
        self.assertEqual(1778, 2 + sum(row["assertions"] for row in rows))
        for row in rows:
            expected = row["expected"]
            first = row["configuration"]["first_possession"]
            scenario = row["id"].split("/")[-1]
            with self.subTest(identity=row["id"]):
                self.assertEqual(scenario in RUNNER.CAPTAIN_SCENARIOS[:2], expected["predicate"])
                self.assertEqual(expected["predicate"], expected["next_type"] != "Kickoff"
                                 and expected["saved_ending_type"] != "Kickoff"
                                 and expected["resolved_offense"] != expected["initial_offense"]
                                 and expected["resolved_offense"] == first)
                self.assertEqual((22 if row["profile"] == "C1-Strict" else 25) + (2 if expected["predicate"] else 0),
                                 row["assertions"])

    def test_missing_duplicated_wrong_profile_and_substituted_captain_records_are_rejected(self):
        for action in ("missing", "duplicate", "unknown-id", "profile", "configuration", "table", "missing-field"):
            summary = self.summary()
            rows = summary["captain_transition"]["records"]
            if action == "missing":
                rows.pop()
            elif action == "duplicate":
                rows[-1] = copy.deepcopy(rows[0])
            elif action == "unknown-id":
                rows[0]["id"] = "captain/substituted"
            elif action == "profile":
                rows[0]["profile"] = "M1-Portable"
            elif action == "configuration":
                rows[0]["configuration"]["stations"][1]["team"] = "Away"
            elif action == "table":
                rows[0]["expected"]["predicate"] = False
            else:
                rows[0]["invariants"].pop("commit/no-added-boundary-RNG")
            with self.subTest(action=action), self.assertRaises(RuntimeError):
                RUNNER.require_summary(0, summary)

    def test_records_must_include_actual_queue_commit_later_and_next_play_consumer_fields(self):
        changes = (
            (0, ("commit", "captain"), "First"), (0, ("before", "captain"), "Second"),
            (0, ("commit", "down"), 4), (0, ("commit", "clock_packed"), 0),
            (0, ("commit", "repair_count"), 1), (0, ("commit_boundary_rng_calls",), 1),
            (0, ("source_events",), ["PlayResolved", "PlayResolved"]), (0, ("legacy_events",), []),
            (0, ("source_event_context", 0, "team"), "Home"), (0, ("legacy_event_context", 0, "value"), 2),
            (0, ("result", "ending_type"), "Kickoff"), (0, ("later", "captain"), "First"),
            (0, ("later_host_wakes",), []), (0, ("consumer",), None),
            (24, ("queue", "captain"), "Second"), (24, ("skipped_host", "result_pending"), False),
            (24, ("commit", "host_tick"), 61), (24, ("commit_c2_events",), []),
        )
        for index, path, value in changes:
            summary = self.summary()
            target = summary["captain_transition"]["records"][index]["actual"]
            for key in path[:-1]:
                target = target[key]
            target[path[-1]] = value
            with self.subTest(index=index, path=path), self.assertRaises(RuntimeError):
                RUNNER.require_summary(0, summary)
        summary = self.summary()
        summary["captain_transition"]["records"][0]["actual"]["consumer"]["stations"][0]["control"] = 8
        with self.assertRaises(RuntimeError):
            RUNNER.require_summary(0, summary)

    def test_all_four_added_controls_mutate_only_the_real_integration_and_fail_the_named_field(self):
        self.assertEqual([
            "captain-omitted-commit-call", "captain-wrong-ending-type",
            "captain-wrong-first-possession", "captain-double-commit-toggle",
        ], [item[0] for item in RUNNER.CAPTAIN_MUTANTS])
        self.assertEqual(13, len({item[0] for item in RUNNER.MUTANTS}))
        for mutation in RUNNER.CAPTAIN_MUTANTS:
            identity, name, old, new, expected = mutation
            with self.subTest(identity=identity):
                self.assertEqual(RUNNER.SOURCE, name)
                self.assertEqual(1, (RUNNER.ROOT / name).read_text().count(old))
                self.assertNotEqual(old, new)
                self.assertTrue(expected.endswith("/" + RUNNER.CAPTAIN_FIELD))
                summary = self.captain_failure(mutation)
                RUNNER.require_summary(1, summary, expected)
                for code, changes in ((0, {}), (134, {}), (1, {"status": "error"}),
                                      (1, {"failed_assertion": "unrelated startup/compile failure"})):
                    with self.subTest(code=code), self.assertRaises(RuntimeError):
                        RUNNER.require_summary(code, dict(summary, **changes), expected)
                summary["captain_transition"]["records"][-1]["actual"]["commit"]["captain"] = (
                    summary["captain_transition"]["records"][-1]["expected"]["captain"])
                with self.assertRaises(RuntimeError):
                    RUNNER.require_summary(1, summary, expected)

    def test_exact_9_plus_4_control_receipt_rejects_missing_duplicate_compile_and_wrong_partition(self):
        controls, runs = [], {}
        for mutation in RUNNER.MUTANTS:
            identity = mutation[0]
            added = mutation in RUNNER.CAPTAIN_MUTANTS
            summary = self.captain_failure(mutation) if added else self.retained_failure(mutation[4])
            runs[identity] = dict(exit_code=1, compiled_zero_diagnostics=True, summary=summary)
            controls.append(dict(id=identity, named_failure=mutation[4], compiled_zero_diagnostics=True,
                                 partition="captain" if added else "retained"))
        RUNNER.require_controls(controls, runs)
        for changed in (controls[:-1], controls[:-1] + controls[:1]):
            with self.assertRaises(RuntimeError):
                RUNNER.require_controls(changed, runs)
        for key, value in (("partition", "retained"), ("named_failure", "unrelated"), ("compiled_zero_diagnostics", False)):
            changed = copy.deepcopy(controls)
            changed[-1][key] = value
            with self.subTest(key=key), self.assertRaises(RuntimeError):
                RUNNER.require_controls(changed, runs)
        changed_runs = copy.deepcopy(runs)
        changed_runs[RUNNER.CAPTAIN_MUTANTS[0][0]]["compiled_zero_diagnostics"] = False
        with self.assertRaises(RuntimeError):
            RUNNER.require_controls(controls, changed_runs)

    def test_new_module_identity_cannot_be_an_old_core_proxy_or_unrelated_path(self):
        assembly = HERE / "synthetic-parser-only/MatchLifecycleChecks.dll"
        identity = dict(assembly="MatchLifecycleChecks, Version=1.0.0.0", location=str(assembly),
                        module="MatchLifecycleChecks.dll", same_assembly_as_harness=True,
                        mvid="00000000-0000-0000-0000-000000000001")
        RUNNER.require_compiled_identity(identity, assembly)
        for key, value in (
            ("mvid", ""), ("mvid", "00000000-0000-0000-0000-000000000000"),
            ("mvid", "119605f0-50aa-4181-9f2b-1b723b493270"), ("mvid", "7a6092bc-d697-4e54-ac18-089acbd93d25"),
            ("same_assembly_as_harness", False), ("module", "Core.dll"), ("assembly", "Other"),
            ("location", str(HERE / "wrong.dll")),
        ):
            with self.subTest(key=key, value=value), self.assertRaises(RuntimeError):
                RUNNER.require_compiled_identity(dict(identity, **{key: value}), assembly)

    def test_source_authorship_keeps_generations_real_and_never_assigns_admission_counters_or_captain(self):
        source = (HERE / "Program.cs").read_text()
        captain = source[source.index("private static void CaptainCase("):source.index("private static void Captains(")]
        self.assertLess(captain.index("i < SliceRules.TicksPerSecond"), captain.index("m.Phase = GamePhase.LiveBall"))
        self.assertIn('"EndSourcePlay"', captain)
        self.assertNotIn('"ApplySourcePlayEnd"', captain)
        self.assertNotRegex(captain, r"\b(?:m|game\.Model)\.(?:SourceTick|SourceC2EndEpoch|SourceClockRemainder|SourceCaptain|"
                            r"SourceFirstPossession|SourceMovementCamera(?:Tick)?)\s*=(?!=)")
        factory_load = source[source.index("private static void Captains("):source.index("private static int Main(")]
        self.assertIn("FullKernelInputs.Load(args[0], soundFunctionsPath: args[1]);", factory_load)
        self.assertIn("c2M1InputsPath: args[2]", source)
        production = (RUNNER.ROOT / RUNNER.SOURCE).read_text()
        self.assertEqual(1, production.count(RUNNER.CAPTAIN_INTEGRATION))
        commit = production[production.index("private void ApplySourcePlayEnd("):production.index("private void ContinueSource(")]
        self.assertLess(commit.index("Model.LastOutcome ="), commit.index(RUNNER.CAPTAIN_CALL))
        self.assertLess(commit.index(RUNNER.CAPTAIN_CALL), commit.index("if (attackerId >= 0)"))

    def test_future_execution_has_no_implicit_restore_retry_sdk_query_or_parallel_mutation_budget(self):
        source = (HERE / "run.py").read_text()
        self.assertNotIn('["dotnet", "restore"', source)
        self.assertNotIn('["dotnet", "--info"', source)
        self.assertNotIn("empty-feed", source)
        self.assertIn('"--no-restore"', source)
        self.assertIn('"-m:1", "-nr:false"', source)
        self.assertIn('os.environ.get("BLITZ_CI_MUTATION_WORKERS", "1") != "1"', source)
        self.assertIn('"-p:ProjectAssetsFile=" + str(assets)', source)

    def test_harness_identity_includes_guards_imported_helpers_and_original_authority_pin(self):
        self.assertEqual(15, len(RUNNER.HARNESSES))
        self.assertEqual(15, len(set(RUNNER.HARNESSES)))
        self.assertTrue({
            "tests/match-lifecycle/run.py", "tests/match-lifecycle/test_runner.py",
            "tests/parity/FullKernelInputs.cs", "tests/ci/isolated_mutations.py",
            "tests/parity/kernel_sources.py", "tests/parity/prepare_inputs.py",
            "tests/parity/contact_inputs.py", "tests/parity/fetch_inputs.py", "tests/source-oracles/pins.json",
        }.issubset(RUNNER.HARNESSES))


if __name__ == "__main__":
    unittest.main()
