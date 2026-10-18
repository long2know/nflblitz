using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal static partial class Program
{
    private static int assertions;
    private static int disabledAssertions;
    private static SourceInterfaceData hud;
    private static SourceKickMeterMetrics metrics;
    private static readonly List<object> observations = new List<object>();
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };
    internal static void Check(bool value, string identity)
    {
        assertions++;
        if (!value) throw new InvalidOperationException("KICK_METER_ASSERTION: " + identity);
    }

    internal static SourceMatchConfiguration Configuration(TeamId side)
    {
        return new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(3, side) }, side, SourceCaptainSlot.First);
    }

    internal static GameSimulation Factory(FullKernelInputs inputs, int profile, SourceMatchConfiguration configuration,
        SourceKickMeterMetrics kickMetrics, SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
        bool windy = false)
    {
        var presentation = new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0);
        Func<int, ISourceRandom> random = seed => new SourceInjectedRandom(site =>
                windy && site.SourceIdentity == "plyrinfo.c:kick_meter_proc:wind" ? 7
                : windy && site.SourceIdentity == "plyrinfo.c:kick_meter_proc:wind_sign" ? 0 : 99,
            new SourceRandomProvenance(windy ? "kick-meter/declared-wind7-sign0" : "kick-meter/controlled-raw99",
                "Declared wind branch when selected; otherwise existing raw99 component fixture; NOT historical RNG/public full-game qualification",
                "actual source meter caller", SourceRandomEvidence.ExternalUnverified));
        var settings = new SourceMovementSettings(0, 0, false, 0, 0, false);
        GameSimulation game = profile == 0
            ? SourceContactComposition.CreateSimulation(inputs.Animations, inputs.ReferencePivots, inputs.Playbook,
                configuration, inputs.Rosters, presentation, inputs.MovementRuntime, settings, inputs.SoundFunctions,
                random, compatibility: compatibility, kickMeterMetrics: kickMetrics)
            : profile == 1
            ? SourceC2Composition.CreateSimulation(inputs.Animations, inputs.ReferencePivots, inputs.Playbook,
                configuration, inputs.Rosters, presentation, inputs.MovementRuntime, settings, inputs.SoundFunctions,
                SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(inputs.C2Data), random,
                compatibility: compatibility, kickMeterMetrics: kickMetrics)
            : SourceC2Composition.CreateM1Simulation(inputs.Animations, inputs.ReferencePivots, inputs.Playbook,
                configuration, inputs.Rosters, presentation, inputs.MovementRuntime, settings, inputs.SoundFunctions,
                SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(inputs.C2Data), random,
                compatibility: compatibility, kickMeterMetrics: kickMetrics);
        game.SourceCameraHeadingProvider = (snapshot, tick) => FullKernelInputs.SetControlledMovementCamera(game, snapshot, tick);
        return game;
    }

    internal static void PrepareInitialMatchBoundary(GameSimulation game, TeamId side, bool punt, bool publicMenu = false)
    {
        // One declared initial match boundary; no actor/ball/meter/velocity rescue during execution.
        game.Model.Phase = GamePhase.PlaySelect;
        game.Model.SourceMatchPhase = SourceMatchPhase.PlaySelection;
        game.Model.Possession = side;
        game.Model.SourceLastPossession = (int)side;
        game.Model.SourceRules = new SourceRulesState((int)side, (int)side, publicMenu ? 4 : 1,
            punt ? 30 : 89, 100, SourcePlayType.Normal);
        if (publicMenu)
        {
            game.Model.Down = 4;
            game.Model.LineOfScrimmage = SourceMatchRules.AbsoluteYardsFromSourceWorld(
                SourceMatchRules.WorldPosition(game.Model.SourceRules.LineOfScrimmage, (int)side));
            game.Model.FirstDownLine = SourceMatchRules.AbsoluteYardsFromSourceWorld(
                SourceMatchRules.WorldPosition(100, (int)side));
        }
        game.Model.SourceFlags = SourceGameFlags.None;
        game.Model.TargetPlayerId = -1;
    }

    private sealed class Cabinet
    {
        internal readonly GameSimulation Game;
        internal readonly SourceCabinetPlaySelection Selection;
        internal readonly SourceCabinetInputBuffer Buffer;
        private readonly SourceCabinetQuantizer quantizer;
        private readonly SourceMatchConfiguration configuration;
        internal long Executed;
        internal Action<SourceKickMeterView> Observe = null;
        internal SourceButtons OtherHeld { get; set; }
        internal Cabinet(GameSimulation game, SourceMatchConfiguration configuration)
        {
            Game = game; this.configuration = configuration; Selection = new SourceCabinetPlaySelection(hud);
            game.SourceStationInputTransform = Selection.ApplyAtSourceTick;
            Buffer = new SourceCabinetInputBuffer(configuration);
            quantizer = new SourceCabinetQuantizer(hud.Input);
        }
        internal void Host(SourceButtons held = SourceButtons.None, int x = 0, int y = 0)
        {
            long before = Game.SourceTick;
            Buffer.Capture(configuration.Stations.Select(station => new SourceCabinetSample(station.StationId,
                quantizer.SevenLevel(station.StationId == 3 ? x : 0, station.StationId == 3 ? y : 0),
                station.StationId == 3 ? held : OtherHeld)).ToArray());
            Game.Step(new PlayerCommand(stationInputs: Buffer.NextHostFrame()));
            if (Game.SourceTick != before)
            {
                Executed++;
                if (Observe != null) Observe(((ISourceKickMeterState)Game.State).KickMeter);
            }
        }
        internal void Wake(SourceButtons held = SourceButtons.None, int x = 0, int y = 0)
        {
            long before = Game.SourceTick;
            Host(held, x, y);
            if (Game.SourceTick == before) Host(held, x, y);
            Check(Game.SourceTick == before + 1, "raw cabinet advances exactly one executing source wake");
        }
        internal void Pulse(SourceButtons button) { Wake(button); Wake(); }
        internal void Move(int x, int y) { Wake(x: x, y: y); Wake(); }
        internal void Select(string symbol, TeamId side, bool twoHumans = false)
        {
            for (int tick = 0; tick < 24; tick++) Wake();
            int page = -1, slot = -1;
            for (int p = 0; p < 4; p++)
            for (int s = 0; s < 9; s++)
                if (Selection.Slot(Game.State, side, p, s).PlaySymbol == symbol && page < 0) { page = p; slot = s; }
            Check(page >= 0 && slot >= 0, "actual authored original cabinet page contains " + symbol);
            for (int p = 0; p < page; p++) Pulse(SourceButtons.C);
            for (int column = 0; column < slot % 3; column++) Move(3, 0);
            for (int row = 0; row < slot / 3; row++) Move(0, 3);
            Check(Selection.Page(side) == page && Selection.Cursor(side) == slot,
                "actual cabinet C/stick input reaches original page/slot");
            Wake(SourceButtons.B);
            if (twoHumans)
            {
                Check(Game.Model.Phase == GamePhase.PlaySelect && Selection.Selected(side),
                    "real two-human menu waits for actual opposite-side selection");
                OtherHeld = SourceButtons.B;
                Wake();
                OtherHeld = SourceButtons.None;
                Wake();
            }
            Check(Game.Model.Phase == GamePhase.PreSnap && Selection.Selected(side),
                "public raw cabinet B selects actual special-teams play through source menu transform");
        }
    }

    private static void Options()
    {
        Check(SourceKickMeterOptions.FromUrl(null) == SourceKickMeterMode.Disabled, "no URL is disabled");
        Check(SourceKickMeterOptions.FromUrl("http://local/?source-compatibility=portable") == SourceKickMeterMode.Disabled,
            "M1 Portable index corrections DO NOT enable kick meters");
        Check(SourceKickMeterOptions.FromUrl("http://local/?source-kick-meters=portable-authored")
            == SourceKickMeterMode.PortableAuthored, "explicit separate portable-authored kick option");
        Check(SourceKickMeterOptions.FromUrl("http://local/#?source-kick-meters=portable-authored") == SourceKickMeterMode.Disabled,
            "URL fragments do not activate gameplay");
        bool duplicate = false, native = false;
        try { SourceKickMeterOptions.FromUrl("?source-kick-meters=disabled&source-kick-meters=portable-authored"); }
        catch (ArgumentException) { duplicate = true; }
        try { SourceKickMeterOptions.FromUrl("?source-kick-meters=strict-native"); }
        catch (NotSupportedException) { native = true; }
        Check(duplicate && native, "duplicate/unknown native mode fails explicitly");
        Check(metrics.Mode == SourceKickMeterMode.PortableAuthored && !metrics.NativeGeneratedMetricsKnown
            && metrics.ContentSha256 == hud.ContentSha256, "explicit authored provenance is observable and native unknown remains false");
        Check(metrics.FieldGoalBarWidth == hud.Image("k_bar").Width
            && metrics.FieldGoalMarkerWidth == hud.Image("k_marker").Width
            && metrics.PuntMeterHeight == hud.Image("p_meter").Height,
            "metric provider reads the actual pinned authored image axes");
    }

    private static void Caller(FullKernelInputs inputs, int profile, TeamId side, bool punt)
    {
        SourceMatchConfiguration configuration = Configuration(side);
        GameSimulation game = Factory(inputs, profile, configuration, metrics);
        PrepareInitialMatchBoundary(game, side, punt, true);
        var cabinet = new Cabinet(game, configuration);
        cabinet.Select(punt ? "play_punt" : "play_field_goal", side);
        var enabled = (ISourceKickMeterState)game.State;
        Check(enabled.KickMeterMode == SourceKickMeterMode.PortableAuthored
            && ReferenceEquals(enabled.KickMeterMetrics, metrics), "actual factory passes the independently enabled metric provider");
        long created = game.SourceTick;
        Check(enabled.KickMeter.Status == SourceKickMeterStatus.Starting && !enabled.KickMeter.ShowMeterFlag,
            "qcreate-style meter creation is not an invented immediate first poll or kick");
        SourceKickMeterView immutable = enabled.KickMeter;
        for (int tick = 0; tick < 12; tick++) cabinet.Wake(SourceButtons.B);
        Check((game.Model.SourceFlags & SourceGameFlags.HikingBall) == 0 && game.Model.Phase == GamePhase.PreSnap,
            "holding menu B through the ten-tick gate never invents a new snap edge");
        Check(immutable.Status == SourceKickMeterStatus.Starting && immutable.PositionFixed10000 == 0,
            "retained kick snapshot does not mutate when the process advances");
        cabinet.Wake();
        SourceMovementCameraFrame releaseCamera = null;
        SourceVector3 releaseRightAnkle = default(SourceVector3), releaseLeftAnkle = default(SourceVector3);
        SourceVector3 releaseBall = default(SourceVector3), releaseTarget = default(SourceVector3);
        long firstEligibleGoalTick = -1;
        bool shadowStarted = false;
        int shadowMode = 0, shadowFlags = 0, shadowCounter = 0;
        game.SourceCameraHeadingProvider = (snapshot, tick) => {
            int heading = FullKernelInputs.SetControlledMovementCamera(game, snapshot, tick);
            releaseCamera = game.Model.SourceMovementCamera;
            int punter = (int)side * 7 + 3;
            releaseRightAnkle = game.Model.SourceCameraJoints[punter, 11];
            releaseLeftAnkle = game.Model.SourceCameraJoints[punter, 14];
            releaseBall = game.Model.SourceBallPosition;
            releaseTarget = game.Model.SourceBallTarget;
            shadowMode = game.Model.Phase == GamePhase.PreSnap ? 1 : game.Model.Phase == GamePhase.LiveBall ? 2 : 3;
            shadowFlags = (int)game.Model.SourceFlags;
            shadowCounter = (game.Model.SourceFlags & SourceGameFlags.HikingBall) != 0
                || game.Model.SourceSnapTick >= created ? game.Model.SourceHikeCounterAtSnap : game.Model.PreSnapTicksRemaining;
            int carrier = game.Model.CarrierId;
            if (!punt && game.Model.Phase == GamePhase.LiveBall && carrier >= 0)
            {
                string sequence = game.Model.Players[carrier].AnimationState.Primary.SequenceName;
                SourceVector3 root = SourcePlayerDriver.WorldPosition(game.Model.Players[(int)side * 7 + 6]);
                float dx = root.X - releaseBall.X, dz = root.Z - releaseBall.Z;
                if ((sequence == "M_KHHOLD" || sequence == "M_KHHUT") && (float)Math.Sqrt(dx * dx + dz * dz) < 8f
                    && firstEligibleGoalTick < 0) firstEligibleGoalTick = tick;
            }
            return heading;
        };
        if (reference != null)
        {
            Check(reference.Ask("M", punt ? 2 : 1, (int)side,
                ((ISourceKickMeterState)game.State).KickMeter.SpecialTeamsAttribute, 0, 0, 0, 99, 99, 99) == "READY",
                "start original-C shadow for the actual raw production caller");
            SourceKickMeterView last = ((ISourceKickMeterState)game.State).KickMeter;
            for (int tick = 1; tick <= last.SourceTick - created; tick++)
                reference.Row("T", tick, 1, 0, 285 - tick);
            shadowStarted = true;
            cabinet.Observe = meter => CompareMeter(meter,
                reference.Row("T", game.SourceTick - created, shadowMode, shadowFlags, shadowCounter), created,
                "actual-raw-" + profile + "-" + side + "-" + (punt ? "punt" : "FG"));
        }
        var hudComposer = new SourceHudComposer(hud);
        SourceKickMeterView tracking = ((ISourceKickMeterState)game.State).KickMeter;
        Check(tracking.Owner == side && tracking.Kind == (punt ? SourceKickMeterKind.Punt : SourceKickMeterKind.FieldGoal)
            && tracking.Status == SourceKickMeterStatus.Tracking && tracking.CreatedSourceTick == created,
            "source process remains owned by the actual offensive human side and play");
        var viewport = new SourceHudViewport(1024, 768, 1024, 768, 1);
        IReadOnlyList<SourceHudPrimitive> draw = hudComposer.Compose(game.State, viewport, cabinet.Selection, false, false);
        Check(draw.Any(p => p.Element == (punt ? "kick/punt/bar" : "kick/fg/bar"))
            && draw.Any(p => p.Element == (punt ? "kick/punt/ball" : "kick/fg/ball"))
            && !draw.Any(p => p.Element.EndsWith("/turbo-frame", StringComparison.Ordinal)),
            "real HUD consumes typed meter and actual source art, suppressing original turbo while showkmtr is set");
        Check(draw.All(p => viewport.Content.Contains(p.Bounds)), "actual meter draw commands stay in measured canvas");
        SourceHudPrimitive drawnBall = draw.First(p => p.Element == (punt ? "kick/punt/ball" : "kick/fg/ball"));
        SourceHudPrimitive drawnBar = draw.First(p => p.Element == (punt ? "kick/punt/bar" : "kick/fg/bar"));
        double observedOffset = punt ? drawnBall.Bounds.Y - drawnBar.Bounds.Y : drawnBall.Bounds.X - drawnBar.Bounds.X;
        double expectedOffset = punt
            ? (hud.Image("p_meter").AnchorY - hud.Image("k_ball").AnchorY - tracking.SpriteOffsetPixels) * 2d
            : (hud.Image("k_bar").AnchorX - hud.Image("k_ball").AnchorX + tracking.SpriteOffsetPixels) * 2d;
        Check(Math.Abs(observedOffset - expectedOffset) < .000001,
            "actual HUD ball coordinate consumes original INTEGER meter offset on the correct axis/direction");
        long beforeRender = game.SourceTick, beforeRandom = game.Model.SourceRandomDraws;
        int beforeRenderPosition = tracking.PositionFixed10000;
        for (int render = 0; render < 3; render++) hudComposer.Compose(game.State, viewport, cabinet.Selection, false, false);
        Check(game.SourceTick == beforeRender && game.Model.SourceRandomDraws == beforeRandom
            && ((ISourceKickMeterState)game.State).KickMeter.PositionFixed10000 == beforeRenderPosition,
            "HUD repaint is a consumer, never another meter tick or random owner");
        cabinet.Wake(profile == 1 ? SourceButtons.A : SourceButtons.B);
        long request = game.SourceTick;
        Check(game.Model.Phase == GamePhase.PreSnap && (game.Model.SourceFlags & SourceGameFlags.HikingBall) != 0
            && !game.SourceEvents.Any(e => e.Type == SourceEventType.Snapped),
            "fresh public A/B requests snap but does not bypass actual center animation");
        Check(((ISourceKickMeterState)game.State).KickMeter.Status == SourceKickMeterStatus.Tracking,
            "meter does not lock prematurely inside QB input before its own next continuation");
        SourceKickMeterView atRequest = ((ISourceKickMeterState)game.State).KickMeter;
        cabinet.Wake();
        SourceKickMeterView locked = ((ISourceKickMeterState)game.State).KickMeter;
        Check(locked.Status == SourceKickMeterStatus.Locked && locked.SourceTick == request + 1
            && locked.PositionFixed10000 == (punt ? Math.Max(-atRequest.LimitFixed10000,
                Math.Min(atRequest.LimitFixed10000, atRequest.PositionFixed10000)) : atRequest.PositionFixed10000),
            "actual meter continuation locks exactly one source wake after real snap request");
        Check(locked.HideSourceTick == locked.SourceTick + 30, "manual result retains original thirty-source-tick lifetime");
        int playNumber = game.Model.PlayNumber;
        cabinet.Wake(SourceButtons.B);
        Check(game.Model.SourceSnapTick == request && game.Model.PlayNumber == playNumber
            && !game.SourceEvents.Any(e => e.Type == SourceEventType.Snapped),
            "another raw press during hiking cannot reenter snap or restart center/meter");
        SourceKickLaunchView launch = default(SourceKickLaunchView);
        long caught = -1;
        for (int tick = 0; tick < 150 && !launch.IsSpecified; tick++)
        {
            cabinet.Wake();
            if (game.Model.CarrierId == (int)side * 7 + 3 && caught < 0) caught = game.SourceTick;
            launch = ((ISourceKickMeterState)game.State).LastKickLaunch;
        }
        Check(launch.IsSpecified, "public cabinet input actually launches original " + (punt ? "punt" : "FG")
            + " via current player/animation/ball callers");
        Check(launch.SourceTick > request && launch.ActorPlayerId / 7 == (int)side,
            "actual launch belongs to the same offense, not a synthetic actor");
        Check(game.Model.CarrierId == -1 && game.Model.BallMode == BallMode.InFlight
            && game.Model.BallKind == (punt ? SourceBallKind.Punt : SourceBallKind.FieldGoal),
            "real release changes actual carrier/ball kind/mode");
        Check(launch.Velocity.Y > 0f && (side == TeamId.Home ? launch.Velocity.X > 0f : launch.Velocity.X < 0f),
            "actual special-teams launch has source upward and mirrored forward velocity");
        if (punt)
            Check(launch.AnimationOrdinal == 4, "punt physics is released at original ANI_CODE frame4");
        else
        {
            float dx = launch.KickerPosition.X - launch.Position.X, dz = launch.KickerPosition.Z - launch.Position.Z;
            Check((float)Math.Sqrt(dx * dx + dz * dz) < 8f,
                "FG physics releases immediately in real _mode_kicker within strict source8 units");
            Check(launch.SourceTick == firstEligibleGoalTick,
                "FG launch occurs on FIRST kickable-holder source wake, not an invented later animation contact");
        }
        if (reference != null && shadowStarted)
        {
            int actorId = launch.ActorPlayerId;
            float[] original = punt
                ? PuntReference(side, game.Model.SourceRules.LineOfScrimmage, locked.PuntStrength,
                    game.Model.Players[actorId].Flipped,
                    game.Model.Players[actorId].Flipped ? releaseLeftAnkle : releaseRightAnkle, releaseCamera, Raw99)
                : GoalReference(side, game.Model.SourceRules.LineOfScrimmage, game.Model.SourceHikeCounterAtSnap,
                    locked.AccuracyPercent, releaseBall, releaseTarget, game.Model.Players[actorId].SourceHandicap,
                    game.Model.Quarter, game.Model.SourceClock.PackedTime,
                    side == TeamId.Home ? game.Model.HomeScore : game.Model.AwayScore,
                    side == TeamId.Home ? game.Model.AwayScore : game.Model.HomeScore,
                    game.Model.SourceTeamFire[(int)side], game.Model.SourcePerfectFieldGoal,
                    game.Model.SourceNoAssist, game.Model.SourceDronesLose, true, Raw99);
            CompareBall(launch.Position, launch.Velocity, launch.Target, launch.TotalSourceTicks, original,
                "actual-raw-launch-" + profile + "-" + side + "-" + (punt ? "punt" : "FG"));
            Same(game.Model.SourceBallVelocity.X, original[3], "actual launched X velocity reaches ball_proc");
            Same(game.Model.SourceBallVelocity.Z, original[5], "actual launched Z velocity reaches ball_proc");
            Same(game.Model.SourceBallVerticalVelocity, original[4] + SourceBallDriver.Gravity,
                "actual ball_proc advances the launched vertical velocity once in the SAME source wake");
            Same(game.State.Ball.Velocity.X, original[3] * (SourceTiming.DefaultTicksPerSecond / SourceTiming.SourceUnitsPerYard),
                "public ball view preserves source-to-gameplay velocity units");
        }
        Check(((ISourceKickMeterState)game.State).KickMeter.ShowMeterFlag == punt,
            "original hold remains after this punt but expires before this later FG approach; release does not own HUD lifetime");
        var afterLaunch = hudComposer.Compose(game.State, viewport, cabinet.Selection, false, false);
        Check(afterLaunch.Any(p => p.Element == (punt ? "kick/punt/bar" : "kick/fg/bar")) == punt,
            "actual HUD consumes retained/expired process state after physical release");
        observations.Add(new { profile, side = side.ToString(), punt, created, request, caught, locked, launch,
            hud_elements = draw.Where(p => p.Element.StartsWith("kick/", StringComparison.Ordinal)).Select(p => p.Element).ToArray(),
            originalC_shadow_and_launch_compared = reference != null,
            controlled_initial_boundary_NOT_full_public_match = true });
    }

    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(args[5]);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        try
        {
            var inputs = new[] {
                FullKernelInputs.Load(args[0], soundFunctionsPath: args[1]),
                FullKernelInputs.Load(args[0], soundFunctionsPath: args[1], c2InputsPath: args[2]),
                FullKernelInputs.Load(args[0], soundFunctionsPath: args[1], c2M1InputsPath: args[3])
            };
            hud = SourceInterfaceData.Read(File.ReadAllBytes(args[4]), SourceKickMeterMetrics.AuthoredInterfaceSha256);
            metrics = hud.CreatePortableAuthoredKickMeterMetrics();
            string section = args.Length > 6 ? args[6] : "all";
            if (!new[] { "all", "meters", "physics", "callers", "lifecycle", "additional", "disabled", "options" }.Contains(section))
                throw new ArgumentException("Select a declared kick-meter check section.");
            if (args.Length > 7 && args[7] != "-")
                reference = new NativeReference(Path.GetFullPath(args[7]), output + ".original-C.txt");
            if (reference != null && (section == "all" || section == "meters"))
                MeterReferenceChecks();
            if (reference != null && (section == "all" || section == "physics"))
                PhysicsReferenceChecks(inputs[0]);
            if (section == "all" || section == "disabled") disabledAssertions = DisabledCallerChecks.Run(inputs);
            if (section == "all" || section == "options")
                Options();
            if (section == "all" || section == "callers")
            {
                for (int profile = 0; profile < 3; profile++)
                foreach (TeamId side in new[] { TeamId.Home, TeamId.Away })
                foreach (bool punt in new[] { false, true }) Caller(inputs[profile], profile, side, punt);
            }
            if (section == "all" || section == "lifecycle")
                LifecycleChecks(inputs);
            if (section == "all" || section == "additional")
                AdditionalChecks(inputs, args[0], args[1], args[4]);
            if (reference != null) { reference.Dispose(); reference = null; }
            File.WriteAllText(output, JsonSerializer.Serialize(new {
                status = "passed", assertions, disabledAssertions, observations, meterFlows, physicsCases, referenceCalls, lifecycleCases, additionalCases,
                strict_native_generated_metrics_UNKNOWN = true, native_or_browser_proof = false
            }, Json));
            Console.WriteLine("KICK_METER_PASS assertions=" + assertions + " disabled=" + disabledAssertions
                + " actualLaunches=" + observations.Count);
            return 0;
        }
        catch (Exception error)
        {
            if (reference != null) { reference.Dispose(); reference = null; }
            File.WriteAllText(output, JsonSerializer.Serialize(new {
                status = "failed", assertions, disabledAssertions, meterFlows, physicsCases, referenceCalls, lifecycleCases, additionalCases,
                error = error.ToString(), observations
            }, Json));
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
