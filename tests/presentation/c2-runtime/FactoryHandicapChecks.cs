using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;

internal static partial class Program
{
    private const string HandicapReferenceSha256 = "b39bac891844f837e1ce91c9959f35e5d5f0a123f448ff1f08b93b5230a816ef";

    private static int[] FrozenConstructorReference(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Equal(Hash(bytes), HandicapReferenceSha256, "factory original-C reference transcript frozen identity");
        string[] lines = File.ReadAllLines(path);
        var matches = new List<int[]>();
        for (int i = 0; i + 1 < lines.Length; i += 2)
        {
            string[] fields = lines[i].Split(' ');
            if (fields.Length != 25 || fields[0] != "H") continue;
            int[] values = fields.Skip(1).Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            if (!values.Take(8).SequenceEqual(new[] { 0, 0, 0, 0, 1, 1, 0, 2 }) ||
                !values.Skip(8).Take(8).SequenceEqual(new[] { 0, 2, 1, 1, 0, 1, 1, 1 }) ||
                !values.Skip(19).SequenceEqual(new[] { -1, -1, -1, -1, -1 })) continue;
            matches.Add(lines[i + 1].Split(' ').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray());
        }
        Equal(matches.Count, 1, "one exact safe original-C raw-settings constructor case");
        Check(matches[0].Length == 9 && matches[0][0] >= 0 && matches[0][0] <= 11 &&
            matches[0][1] >= 0 && matches[0][1] <= 11, "original-defined handicap consumer rows only");
        return matches[0];
    }

    private static GameSimulation HandicapFactory(string profile, SourceC2CallbackData m1Data,
        SourceHandicapSettings settings, bool omitOptional)
    {
        var config = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz, SourceStationLayout.FourStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }), new SourceTeamSelection(2, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home, 3) }, TeamId.Home, SourceCaptainSlot.First,
            difficulty: 9);
        Func<int, ISourceRandom> random = seed => new SourceInjectedRandom(site => 99,
            new SourceRandomProvenance("factory-handicap-explicit-raw99", "Controlled original-C-qualified component input",
                "not public replay or target PRNG", SourceRandomEvidence.ExternalUnverified));
        var presentation = new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0);
        var movement = new SourceMovementSettings(0, 0, false, 0, 0, false);
        if (profile == "C1")
            return omitOptional
                ? SourceContactComposition.CreateSimulation(old272, Pivots(), real.Playbook, config, real.Rosters,
                    presentation, real.MovementRuntime, movement, real.SoundFunctions, random, 37)
                : SourceContactComposition.CreateSimulation(old272, Pivots(), real.Playbook, config, real.Rosters,
                    presentation, real.MovementRuntime, movement, real.SoundFunctions, random, 37, settings);
        SourceC2Bootstrap bootstrap = SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(profile == "M1" ? m1Data : data);
        if (profile == "old-nine")
            return omitOptional
                ? SourceC2Composition.CreateSimulation(library, Pivots(), real.Playbook, config, real.Rosters, presentation,
                    real.MovementRuntime, movement, real.SoundFunctions, bootstrap, random, 37)
                : SourceC2Composition.CreateSimulation(library, Pivots(), real.Playbook, config, real.Rosters, presentation,
                    real.MovementRuntime, movement, real.SoundFunctions, bootstrap, random, 37, settings);
        return omitOptional
            ? SourceC2Composition.CreateM1Simulation(library, Pivots(), real.Playbook, config, real.Rosters, presentation,
                real.MovementRuntime, movement, real.SoundFunctions, bootstrap, random, 37)
            : SourceC2Composition.CreateM1Simulation(library, Pivots(), real.Playbook, config, real.Rosters, presentation,
                real.MovementRuntime, movement, real.SoundFunctions, bootstrap, random, 37, settings);
    }

    private static string HandicapSnapshot(GameSimulation game)
    {
        SourceMatchView match = ((ISourceMatchState)game.State).Match;
        var values = new List<string> {
            match.HomeGameplay.Handicap.ToString(), match.AwayGameplay.Handicap.ToString(),
            match.HomeGameplay.OnFire.ToString(), match.AwayGameplay.OnFire.ToString(),
            match.DronesLose.ToString(), match.DronesWinning.ToString(), match.ForwardCatcherPlayerId.ToString(),
            game.Model.SourceAiParameters.Level(0).ToString(), game.Model.SourceAiParameters.Level(1).ToString(),
            game.Model.SourceNoAssist.ToString(), game.Model.SourceSuperDrones.ToString() };
        values.AddRange(game.State.Players.Select(actor => actor.Source.Handicap.ToString()));
        values.AddRange(((SourceInjectedRandom)game.SourceRandomService).Trace.Select(draw =>
            draw.Site.ToString() + "/" + draw.Raw + "/" + draw.Value));
        if (game.SourceC2Enabled) values.Add(game.C2State.AnnouncerBank + "/" + game.C2State.TauntBank + "/" + game.C2State.BankEpoch);
        return string.Join("|", values);
    }

    private static void FactoryHandicapChecks(string m1Root, string referencePath)
    {
        int[] expected = FrozenConstructorReference(referencePath);
        SourceC2CallbackData m1Data = SourceInterfacePackagePlan.ReadC2M1Callbacks(
            File.ReadAllBytes(Path.Combine(m1Root, "c2-callback-data.json")),
            File.ReadAllBytes(Path.Combine(m1Root, "m-qbfite-hit-extension.safn")));
        var settings = new SourceHandicapSettings(0, 2, true, 1, 0, 1, true, true);
        foreach (string profile in new[] { "C1", "old-nine", "M1" })
        {
            GameSimulation actual = HandicapFactory(profile, m1Data, settings, false);
            Check(ReferenceEquals(actual.Model.SourceHandicapSettings, settings),
                "factory " + profile + " forwards explicit raw settings to actual constructor");
            SourceMatchView match = ((ISourceMatchState)actual.State).Match;
            Equal(match.HomeGameplay.Handicap, expected[0], "factory " + profile + " original-C home row");
            Equal(match.AwayGameplay.Handicap, expected[1], "factory " + profile + " original-C away row");
            Equal(actual.Model.SourceAiParameters.Level(0), expected[0], "factory " + profile + " actual AI home row");
            Equal(actual.Model.SourceAiParameters.Level(1), expected[1], "factory " + profile + " actual AI away row");
            Check(actual.Model.SourceNoAssist && actual.Model.SourceSuperDrones,
                "factory " + profile + " explicit global flags reach real Core");
            Equal(match.DronesLose ? 1 : 0, expected[2], "factory " + profile + " original-C lose flag");
            Equal(match.DronesWinning ? 1 : 0, expected[3], "factory " + profile + " original-C winning flag");
            foreach (PlayerView actor in actual.State.Players)
            {
                Equal(actor.Source.Handicap, (int?)expected[(int)actor.Team], "factory " + profile + " immutable player handicap row");
                Equal(actual.Model.Players[actor.Id].SourceHandicap, expected[(int)actor.Team],
                    "factory " + profile + " actual cached player table index");
            }
            Check(match.Configuration.Difficulty == 9 && expected[0] != 9 && expected[1] != 9,
                "cabinet handicap globals are not generic Difficulty");
            ISourceAnimationLibrary selected = profile == "C1" ? old272 : library;
            Check(ReferenceEquals(Field(actual, "sourceAnimations"), selected) &&
                ReferenceEquals(Field(Field(actual, "sourceGeometry"), "library"), selected),
                "factory " + profile + " keeps SAME actual animation and geometry library");
            Check(ReferenceEquals(Field(actual, "sourceSoundVmData"), real.SoundFunctions),
                "factory " + profile + " keeps old-six SAFN bound before wake");
            if (profile != "C1")
            {
                SourceRandomDraw[] prefix = ((SourceInjectedRandom)actual.SourceRandomService).Trace.Take(2).ToArray();
                Check(prefix.Length == 2 && prefix[0].Site.SourceIdentity.Contains("InitGameProc:556") &&
                    prefix[1].Site.SourceIdentity.Contains("InitGameProc:560"),
                    "factory " + profile + " keeps bootstrap two-bank-before-AI ordering");
            }
            GameSimulation omitted = HandicapFactory(profile, m1Data, null, true);
            GameSimulation explicitNull = HandicapFactory(profile, m1Data, null, false);
            GameSimulation freshDefaults = HandicapFactory(profile, m1Data, new SourceHandicapSettings(), false);
            Equal(HandicapSnapshot(omitted), HandicapSnapshot(explicitNull), "factory " + profile + " omitted versus null defaults equal");
            Equal(HandicapSnapshot(omitted), HandicapSnapshot(freshDefaults), "factory " + profile + " fresh raw boot defaults equal");
            Check(HandicapSnapshot(omitted) != HandicapSnapshot(actual), "factory " + profile + " nondefault state causally differs");
            evidence.Add("Factory " + profile + ": original-C transcript row1359; nondefault raw settings reach immutable match/player and AI rows; omitted/null/fresh defaults equivalent.");
        }
    }
}
