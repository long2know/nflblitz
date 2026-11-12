import copy
import re
import unittest

import open_space_scenario as scenario
import prepare_public_replay as public


class OpenSpaceScenarioGuards(unittest.TestCase):
    def policy(self):
        return dict(schema="source-open-space-frozen-first-window/v1", name=scenario.NAME,
                    version=scenario.VERSION, case=scenario.CASE, recordingGeneration=scenario.GENERATION,
                    maxHosts=12000, maximumInitialWindows=1, seed=1,
                    providerProfile="source-sound-smoke/raw-vector-v1", rawVector=list(scenario.RAW),
                    sourceManifestSha256=scenario.SOURCE_SHA256,
                    configuration=scenario.current_configuration(True),
                    configurationSha256=scenario.CONFIGURATION_SHA256,
                    worktree=str(scenario.ROOT), authority=str(scenario.AUTHORITY),
                    startupObject="Program", noRetryOrExtension=True, controllerSource=scenario.CONTROLLER)

    def test_distinct_scenario_cannot_relabel_failed_v6_or_tune_window_provider_configuration(self):
        scenario.identity(self.policy())
        for field, value in (("recordingGeneration", "source-defensive-presnap-switch/v6"),
                             ("maxHosts", 12001), ("maximumInitialWindows", 2), ("seed", 2),
                             ("version", "approved-portable-public-policy/v1"), ("noRetryOrExtension", False),
                             ("authority", str(scenario.AUTHORITY.parent / "retry")),
                             ("rawVector", list(reversed(scenario.RAW)))):
            policy = self.policy()
            policy[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                scenario.identity(policy)
        policy = copy.deepcopy(self.policy())
        policy["configuration"]["opponents"] = [5, 5]
        with self.assertRaises(ValueError):
            scenario.identity(policy)

    def test_controller_only_receives_immutable_public_projection_and_never_uses_random_or_outcomes(self):
        source = (scenario.ROOT / scenario.CONTROLLER).read_text()
        controller = source[source.index("    private sealed class OpenSpaceController"):
                            source.index("    private static int OpenSpacePolicyChecks")]
        self.assertIn("Choose(OpenSpaceVisible view)", controller)
        for forbidden in ("GameSimulation", "IGameState", "SourceTick", "Animation", "Random", "rawCursor",
                          ".Model", "SourceJob", "SourceDrones", ".Step(", "FrameOrdinal",
                          "HomeScore", "AwayScore", "Clock"):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, controller)
        projection = source[source.index("internal static OpenSpaceVisible Project"):
                            source.index("    private sealed class OpenSpaceController")]
        self.assertEqual(set(re.findall(r"state\.([A-Za-z]+)", projection)),
                         {"Phase", "Possession", "ControlledPlayerId", "Ball", "Players", "LineOfScrimmage"})
        self.assertNotIn("p.Source", projection)
        self.assertNotIn("p.Animation", projection)
        self.assertIn("Array.AsReadOnly(players.ToArray())", source)

    def test_marker_precedes_only_normal_game_construction_and_single_fixed_window(self):
        source = (scenario.ROOT / scenario.CONTROLLER).read_text()
        author = source[source.index("    private static int AuthorOpenSpacePublicPolicy"):]
        self.assertLess(author.index("FileMode.CreateNew"), author.index("GameSimulation game = Create("))
        self.assertEqual(author.count("GameSimulation game = Create("), 1)
        self.assertEqual(author.count("game.Step(command)"), 1)
        self.assertIn("for (int host = 0; host < 12000; host++)", author)
        self.assertIn("if (faults.Count != 0) break;", author)
        self.assertIn("game.SourceTick <= 2299", author)
        self.assertIn("layer.FrameOrdinal != earlier.Item2 && pose != earlier.Item3", author)
        self.assertIn("Enumerable.Range(0, 15)", author)
        self.assertIn("RequirePublicSwitchWitness(host, game, sound, pendingPublicB)", author)
        checks = source[source.index("    private static int OpenSpacePolicyChecks"):
                        source.index("    private static int AuthorOpenSpacePublicPolicy")]
        self.assertNotIn("Create(", checks)
        self.assertNotIn(".Step(", checks)
        self.assertEqual(len(scenario.canonical.HARNESS), 12)

    def test_saved_source_driven_inputs_are_closed_and_cannot_relabel_source_controller_or_authority(self):
        recipe = public.open_space_recipe()
        data = public.load_fixture(public.recording(public.OPEN_SPACE_CASE)["fixture"], public.OPEN_SPACE_CASE)
        rows = public.audit_public(data, public.OPEN_SPACE_CASE)
        self.assertEqual(len(rows), 12000)
        self.assertEqual(set(rows[0]), {"host", "command"})
        for key, value in (("source_manifest_sha256", public.CURRENT_SOURCE_SHA256),
                           ("controller_sha256", public.SOURCE_TIMER_EXPECTED_RECIPE["controller_sha256"]),
                           ("recording_generation", "source-defensive-presnap-switch/v6"),
                           ("harness_count", 11), ("maximum_authoring_windows", 2),
                           ("policy", "approved-portable-public-policy/v1"), ("poses", [])):
            with self.subTest(key=key), self.assertRaises(ValueError):
                public.validate_open_space_recipe(dict(recipe, **{key: value}))


if __name__ == "__main__":
    unittest.main()
