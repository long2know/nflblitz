using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;

internal static partial class Program
{
    private static int additionalCases;

    private static void MetricAndProfileBoundaries(FullKernelInputs[] inputs, string fixture, string sound, string hudPath)
    {
        Check(typeof(SourceKickMeterMetrics).GetConstructors().Length == 0,
            "public consumers cannot bypass the existing authored reader with a fabricated dimension provider");
        JsonNode changed = JsonNode.Parse(File.ReadAllBytes(hudPath));
        JsonNode bar = changed["images"].AsArray().First(image => image["id"].GetValue<string>() == "k_bar");
        bar["width"] = bar["width"].GetValue<int>() + 1;
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(changed);
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        SourceInterfaceData other = SourceInterfaceData.Read(bytes, hash);
        bool rejected = false;
        try { other.CreatePortableAuthoredKickMeterMetrics(); }
        catch (SourceInterfaceException) { rejected = true; }
        Check(rejected, "changed authored generation cannot be used as the trusted portable kick-metric input");
        FullKernelInputs old = FullKernelInputs.Load(fixture, soundFunctionsPath: sound, legacyAnimationLibrary: true);
        rejected = false;
        try
        {
            new GameSimulation(old.Animations, old.Geometry, old.Playbook, Configuration(TeamId.Home),
                seed => new SourceInjectedRandom(site => 99, new SourceRandomProvenance("kick/unsupported-library",
                    "Declared unsupported-profile rejection", "old117", SourceRandomEvidence.ExternalUnverified)),
                rosters: old.Rosters, presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0),
                movementData: old.MovementRuntime, movementSettings: new SourceMovementSettings(0, 0, false, 0, 0, false),
                kickMeterMetrics: metrics);
        }
        catch (NotSupportedException) { rejected = true; }
        Check(rejected, "portable-authored feature does not promote the unsupported historical117 animation profile");
        var portableOnly = Factory(inputs[2], 2, Configuration(TeamId.Home), null, SourceGameplayCompatibility.Portable);
        var both = Factory(inputs[2], 2, Configuration(TeamId.Home), metrics, SourceGameplayCompatibility.Portable);
        Check(((ISourceKickMeterState)portableOnly.State).KickMeterMode == SourceKickMeterMode.Disabled
            && ((ISourceMatchState)portableOnly.State).Match.Compatibility == SourceGameplayCompatibility.Portable,
            "actual M1 Portable factory alone keeps the kick feature disabled");
        Check(((ISourceKickMeterState)both.State).KickMeterMode == SourceKickMeterMode.PortableAuthored
            && ((ISourceMatchState)both.State).Match.Compatibility == SourceGameplayCompatibility.Portable
            && !both.C2State.EntryProfile.Contains("kick"),
            "both independent explicit opt-ins retain the existing M1/C2 profile identity");
        additionalCases++;
    }

    private static void OtherSideCannotLock(FullKernelInputs inputs, int profile, TeamId side, bool punt)
    {
        var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(3, side),
                new SourceStationConfiguration(1, side == TeamId.Home ? TeamId.Away : TeamId.Home) },
            side, SourceCaptainSlot.First);
        GameSimulation game = Factory(inputs, profile, configuration, metrics);
        PrepareInitialMatchBoundary(game, side, punt, true);
        var cabinet = new Cabinet(game, configuration);
        cabinet.Select(punt ? "play_punt" : "play_field_goal", side, true);
        for (int tick = 0; tick < 12; tick++) cabinet.Wake();
        cabinet.OtherHeld = SourceButtons.B;
        cabinet.Wake();
        Check((game.Model.SourceFlags & SourceGameFlags.HikingBall) == 0
            && ((ISourceKickMeterState)game.State).KickMeter.Status == SourceKickMeterStatus.Tracking,
            "opposing human station B cannot lock the offensive human meter or snap its QB");
        Check(game.Model.Players[(int)side * 7 + 3].SourceControllerStation == 3,
            "original offensive QB ownership remains actual station3, not first station/team shortcut");
        cabinet.OtherHeld = SourceButtons.None;
        cabinet.Wake();
        cabinet.Wake(SourceButtons.B);
        cabinet.Wake();
        Check(((ISourceKickMeterState)game.State).KickMeter.Status == SourceKickMeterStatus.Locked
            && ((ISourceKickMeterState)game.State).KickMeter.Owner == side,
            "only the actual offensive QB input locks the same owned meter");
        additionalCases++;
    }

    private static void WindHud(FullKernelInputs inputs, TeamId side)
    {
        var configuration = Configuration(side);
        GameSimulation game = Factory(inputs, 0, configuration, metrics, windy: true);
        PrepareInitialMatchBoundary(game, side, false, true);
        var cabinet = new Cabinet(game, configuration);
        cabinet.Select("play_field_goal", side);
        cabinet.Wake();
        SourceKickMeterView meter = ((ISourceKickMeterState)game.State).KickMeter;
        Same(meter.Wind, -.7f, "actual original wind/sign input reaches source process");
        Check(meter.MarkerOffsetPixels == (int)(meter.Wind * -40f),
            "actual wind moves source marker using original float-to-int truncation");
        Check(meter.SpecialTeamsAttribute == inputs.Rosters.GetTeamData((int)side).Ratings.SpecialTeams,
            "actual selected-team TA_SPTEAMS controls meter speed, not handicap/difficulty/player rating");
        var composer = new SourceHudComposer(hud);
        foreach (int dpr in new[] { 1, 2, 3 })
        foreach (bool portrait in new[] { false, true })
        {
            int width = portrait ? 360 : 960, height = portrait ? 740 : 720;
            var viewport = new SourceHudViewport(width * dpr, height * dpr, width, height, dpr);
            var draw = composer.Compose(game.State, viewport, cabinet.Selection, false, false);
            Check(draw.Any(p => p.Element == "kick/fg/wind-text") && draw.Any(p => p.Element == "kick/fg/wind-arrow")
                && draw.Any(p => p.Element == "kick/fg/marker"),
                "actual HUD consumes authentic wind/marker images for both sides and viewport/DPR shapes");
            Check(draw.All(p => viewport.Content.Contains(p.Bounds)), "portable authored wind quads remain in real CSS viewport");
            SourceHudPrimitive arrow = draw.First(p => p.Element == "kick/fg/wind-arrow");
            double scale = Math.Min(width / 512d, height / 384d);
            float expectedWidth = 30f + (hud.Image("k_windarrow").Width - 30f) * Math.Abs(meter.Wind);
            Check(Math.Abs(arrow.Bounds.Width - expectedWidth * scale) < .00001,
                "wind arrow uses original fractional width from the actual authored art, not a generic indicator");
            Check(arrow.Vertices[0].S > arrow.Vertices[1].S,
                "negative source wind uses the original right-cropped horizontal-flip direction");
        }
        additionalCases++;
    }

    private static void AdditionalChecks(FullKernelInputs[] inputs, string fixture, string sound, string hudPath)
    {
        MetricAndProfileBoundaries(inputs, fixture, sound, hudPath);
        for (int profile = 0; profile < 3; profile++)
        foreach (TeamId side in new[] { TeamId.Home, TeamId.Away })
        foreach (bool punt in new[] { false, true }) OtherSideCannotLock(inputs[profile], profile, side, punt);
        foreach (TeamId side in new[] { TeamId.Home, TeamId.Away }) WindHud(inputs[0], side);
        Check(additionalCases == 15, "exact15 metric/profile/ownership/wind-HUD cases");
    }
}
