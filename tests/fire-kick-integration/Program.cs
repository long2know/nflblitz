using System;
using System.Collections.Generic;
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
    private static SourceFireSpfxBundle combinedAssets;
    private static readonly List<object> combinedCases = new List<object>();
    private static int combinedRawCases;
    private static int combinedLaunches;
    private static int combinedDisabledGuards;
    private static int combinedRestarts;

    private static void CombinedCheck(bool value, string identity)
    {
        assertions++;
        if (!value) throw new InvalidOperationException("FIRE_KICK_INTEGRATION_ASSERTION: " + identity);
    }

    private static GameSimulation CombinedFactory(FullKernelInputs inputs, TeamId side, bool fire, bool kick,
        SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.Portable)
    {
        GameSimulation game = inputs.Create(configuration: Configuration(side),
            presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0),
            randomFactory: seed => new SourceInjectedRandom(site => 99,
                new SourceRandomProvenance("fire-kick/existing-raw99-component",
                    "Retained kick component provider, not a native RNG or public replay",
                    "real combined M1 factories", SourceRandomEvidence.ExternalUnverified)),
            compatibility: compatibility,
            fireEffects: fire ? new SourceFireGameplayConfiguration(new SourceFireSpfxRuntime(combinedAssets), 0, SourceFireCull.Off) : null,
            kickMeterMetrics: kick ? metrics : null);
        CombinedCheck(game.SourceFireEnabled == fire, "factory forwards fire independently of kick metrics");
        CombinedCheck(((ISourceKickMeterState)game.State).KickMeterMode ==
            (kick ? SourceKickMeterMode.PortableAuthored : SourceKickMeterMode.Disabled),
            "factory forwards kick metrics independently of fire");
        CombinedCheck(ReferenceEquals(((ISourceKickMeterState)game.State).KickMeterMetrics, kick ? metrics : null),
            "factory retains the exact explicit authored metric provider");
        CombinedCheck(game.Compatibility == compatibility, "combined options never promote consumer-index compatibility");
        return game;
    }

    private static void CombinedOptionsAndProfiles(FullKernelInputs inputs, string fixture, string sound)
    {
        foreach (bool fire in new[] { false, true })
        foreach (bool kick in new[] { false, true })
        {
            string url = "?sourceprofile=original-teams-contact281-c2-m1&source-fire="
                + (fire ? "portable" : "disabled") + "&source-kick-meters="
                + (kick ? "portable-authored" : "disabled");
            CombinedCheck(SourceFireGameplayOptions.PortableRequested(url) == fire
                && SourceKickMeterOptions.FromUrl(url) == (kick ? SourceKickMeterMode.PortableAuthored : SourceKickMeterMode.Disabled)
                && SourceGameplayCompatibilityOptions.FromUrl(url) == SourceGameplayCompatibility.StrictSource,
                "combined URL options are independent and do not imply portable consumer-index corrections");
            GameSimulation game = CombinedFactory(inputs, TeamId.Home, fire, kick, SourceGameplayCompatibility.StrictSource);
            game.Step(default(PlayerCommand)); game.Step(default(PlayerCommand));
            CombinedFrame(game, fire);
        }
        FullKernelInputs c1 = FullKernelInputs.Load(fixture, soundFunctionsPath: sound);
        bool rejected = false;
        try
        {
            c1.Create(fireEffects: new SourceFireGameplayConfiguration(new SourceFireSpfxRuntime(combinedAssets),
                0, SourceFireCull.Off), kickMeterMetrics: metrics);
        }
        catch (NotSupportedException e) { rejected = e.Message.Contains("actual M1"); }
        CombinedCheck(rejected, "combined kick opt-in cannot widen the fire feature beyond actual M1");
        var legacy = new GameSimulation(1);
        CombinedCheck(!legacy.SourceFireEnabled && ((ISourceKickMeterState)legacy.State).KickMeterMode == SourceKickMeterMode.Disabled,
            "legacy keeps both features disabled without a source constructor");
    }

    private static Cabinet CombinedCabinet(GameSimulation game, TeamId side, bool punt)
    {
        var cabinet = new Cabinet(game, Configuration(side));
        cabinet.Wake();
        // Reuse the reviewed kick suite's initial match boundary, after a genuine camera/cache wake.
        PrepareInitialMatchBoundary(game, side, punt, true);
        cabinet.Select(punt ? "play_punt" : "play_field_goal", side);
        return cabinet;
    }

    private static void CombinedFirePreconditions(GameSimulation game, TeamId side)
    {
        // The reviewed H component predicates, never injected FX commands, bones or a replacement fixture.
        game.Model.SourceTeamFire[1 - (int)side] = true;
        ActorState receiver = game.Model.Players.FirstOrDefault(a => a.Team == side
            && a.SourceMode == SourceActorMode.ReceiverPreSnap);
        if (receiver != null)
        {
            game.Model.SourceLastReceiver[(int)side] = receiver.Id;
            game.Model.SourceConsecutiveCompletions[(int)side] = 2;
        }
    }

    private static void CombinedFrame(GameSimulation game, bool fire)
    {
        if (!fire)
        {
            CombinedCheck(game.SourceFireFrame == null && game.SourceFireProcesses == null
                && game.SourceFireRecords == null, "disabled fire has no runtime frames or process inventory");
            return;
        }
        CombinedCheck(game.SourceFireFrame != null && game.SourceFireFrame.SourceTick == game.SourceTick,
            "completed combined source frame matches the actual source wake");
        CombinedCheck(game.SourceFireFrame.Basis.Camera.SourceTick == game.Model.SourceCameraJointTick
            && game.Model.SourceCameraJointTick == game.SourceTick,
            "combined frame keeps the actual post-player camera/cache ordering");
    }

    private static void CombinedReset(GameSimulation game, bool fire, bool kick, Cabinet cabinet)
    {
        ISourceKickMeterState old = (ISourceKickMeterState)game.State;
        SourceKickMeterView oldMeter = old.KickMeter;
        SourceFireRenderFrame oldFrame = game.SourceFireFrame;
        long oldTick = oldFrame == null ? -1 : oldFrame.SourceTick;
        game.Restart(1);
        cabinet.Selection.Clear();
        cabinet.Buffer.Clear();
        ISourceKickMeterState reset = (ISourceKickMeterState)game.State;
        CombinedCheck(game.SourceTick == 0 && game.SourceFireEnabled == fire
            && game.SourceFireFrame == null, "restart clears the combined fire epoch but retains the independent option");
        CombinedCheck(reset.KickMeterMode == (kick ? SourceKickMeterMode.PortableAuthored : SourceKickMeterMode.Disabled)
            && !reset.KickMeter.IsSpecified && !reset.LastKickLaunch.IsSpecified,
            "restart clears combined meter and physical-launch history without dropping its opt-in");
        CombinedCheck(old.KickMeter.Status == oldMeter.Status && old.KickMeter.SourceTick == oldMeter.SourceTick
            && (oldFrame == null || oldFrame.SourceTick == oldTick),
            "old kick snapshots and fire frames remain immutable across a combined restart");
        if (fire)
            CombinedCheck(game.SourceFireProcesses.Count == 0 && game.SourceBallEffectSelection == SourceFireBallEffect.None,
                "restart removes old mixed processes and sticky ball selection");
        combinedRestarts++;
    }

    private static void CombinedRawCaller(FullKernelInputs inputs, TeamId side, bool punt, bool fire, bool kick)
    {
        GameSimulation game = CombinedFactory(inputs, side, fire, kick);
        Cabinet cabinet = CombinedCabinet(game, side, punt);
        CombinedCheck(((ISourceKickMeterState)game.State).KickMeter.IsSpecified == kick,
            "actual raw cabinet selection creates the meter even when fire is enabled");
        CombinedFirePreconditions(game, side);
        bool particles = false, white = false;
        for (int tick = 0; tick < 12; tick++)
        {
            cabinet.Wake(SourceButtons.B);
            CombinedFrame(game, fire);
            particles |= fire && game.SourceFireFrame.Particles.Count > 0;
            white |= game.State.Players.Any(a => (a.Source.Presentation.RawFlags & 0x400) != 0);
        }
        CombinedCheck((game.Model.SourceFlags & SourceGameFlags.HikingBall) == 0,
            "held menu button cannot silently lock a mixed-feature meter");
        if (fire)
        {
            CombinedCheck(particles && ((SourceInjectedRandom)game.SourceRandomService).Trace.Any(
                d => d.Site.SourceIdentity.Contains("plyr_fire_trail_proc")),
                "actual presnap fire producers reach particles and the SAME Core RNG during kick tracking");
            CombinedCheck(white, "actual mixed presnap caller exposes the shared raw white bit");
        }
        else CombinedCheck(!particles && !white, "kick option alone cannot enable fire rendering or player white");
        cabinet.Wake();
        var viewport = new SourceHudViewport(1024, 768, 1024, 768, 1);
        var composer = new SourceHudComposer(hud);
        IReadOnlyList<SourceHudPrimitive> draw = composer.Compose(game.State, viewport, cabinet.Selection, false, false);
        CombinedCheck(draw.Any(p => p.Element == (punt ? "kick/punt/bar" : "kick/fg/bar")) == kick
            && draw.Any(p => p.Element.EndsWith("/turbo-frame", StringComparison.Ordinal)) != kick,
            "existing HUD uses only the explicit meter lifetime with either fire setting");
        long draws = game.Model.SourceRandomDraws, sourceTick = game.SourceTick;
        SourceKickMeterView beforeRead = ((ISourceKickMeterState)game.State).KickMeter;
        if (fire)
        {
            var consumer = new SourceFireSpfxFrameConsumer(combinedAssets);
            consumer.Consume(game.SourceFireFrame);
            consumer.Consume(game.SourceFireFrame);
        }
        composer.Compose(game.State, viewport, cabinet.Selection, false, false);
        CombinedCheck(game.Model.SourceRandomDraws == draws && game.SourceTick == sourceTick
            && ((ISourceKickMeterState)game.State).KickMeter.PositionFixed10000 == beforeRead.PositionFixed10000,
            "combined HUD and fire consumers cannot advance either producer or shared RNG");
        if (!kick)
        {
            bool rejected = false;
            try { cabinet.Wake(SourceButtons.A); }
            catch (NotSupportedException e) { rejected = e.Message.Contains("Manual source kick/punt meter"); }
            CombinedCheck(rejected, "disabled meter still rejects the real human kick even when fire is enabled");
            combinedDisabledGuards++;
        }
        else
        {
            cabinet.Wake(SourceButtons.A);
            long request = game.SourceTick;
            SourceKickMeterView atRequest = ((ISourceKickMeterState)game.State).KickMeter;
            CombinedCheck(game.Model.Phase == GamePhase.PreSnap
                && (game.Model.SourceFlags & SourceGameFlags.HikingBall) != 0
                && atRequest.Status == SourceKickMeterStatus.Tracking,
                "combined raw snap still precedes the meter's NEXT pre-player continuation");
            cabinet.Wake();
            SourceKickMeterView locked = ((ISourceKickMeterState)game.State).KickMeter;
            CombinedCheck(locked.Status == SourceKickMeterStatus.Locked && locked.SourceTick == request + 1
                && locked.HideSourceTick == locked.SourceTick + 30
                && locked.PositionFixed10000 == (punt ? Math.Max(-atRequest.LimitFixed10000,
                    Math.Min(atRequest.LimitFixed10000, atRequest.PositionFixed10000)) : atRequest.PositionFixed10000),
                "combined lock consumes the same real pre-player meter position and lifetime");
            SourceKickLaunchView launch = default(SourceKickLaunchView);
            for (int tick = 0; tick < 150 && !launch.IsSpecified; tick++)
            {
                cabinet.Wake();
                CombinedFrame(game, fire);
                launch = ((ISourceKickMeterState)game.State).LastKickLaunch;
            }
            CombinedCheck(launch.IsSpecified && launch.ActorPlayerId / 7 == (int)side
                && launch.Kind == (punt ? SourceKickMeterKind.Punt : SourceKickMeterKind.FieldGoal),
                "actual combined cabinet/player/animation path releases the real physical ball");
            CombinedCheck(game.Model.CarrierId == -1 && game.Model.BallMode == BallMode.InFlight
                && game.Model.BallKind == (punt ? SourceBallKind.Punt : SourceBallKind.FieldGoal)
                && game.Model.PassTicksElapsed == 1,
                "combined release reaches real ball_proc in the SAME source wake");
            CombinedCheck(BitConverter.SingleToInt32Bits(game.Model.SourceBallVelocity.X) == BitConverter.SingleToInt32Bits(launch.Velocity.X)
                && BitConverter.SingleToInt32Bits(game.Model.SourceBallVerticalVelocity) ==
                    BitConverter.SingleToInt32Bits(launch.Velocity.Y + SourceBallDriver.Gravity),
                "combined immutable launch retains the actual pre-ball-proc source-unit velocities");
            CombinedCheck(!punt || launch.AnimationOrdinal == 4, "combined punt retains the original frame4 release callback");
            CombinedCheck(((ISourceKickMeterState)game.State).KickMeter.ShowMeterFlag == punt,
                "combined physical release cannot replace the original meter HUD lifetime");
            combinedLaunches++;
        }
        combinedCases.Add(new { fire, kick, side = side.ToString(), punt, particles, white,
            tick = game.SourceTick, meter = ((ISourceKickMeterState)game.State).KickMeter,
            launch = ((ISourceKickMeterState)game.State).LastKickLaunch });
        CombinedReset(game, fire, kick, cabinet);
        combinedRawCases++;
    }

    private static void CombinedEmptyAndSkips(FullKernelInputs inputs)
    {
        var games = new List<GameSimulation>();
        foreach (bool fire in new[] { false, true })
        foreach (bool kick in new[] { false, true })
            games.Add(CombinedFactory(inputs, TeamId.Home, fire, kick));
        bool skipped = false;
        for (int host = 0; host < 45; host++)
        {
            long before = games[3].SourceTick;
            SourceFireRenderFrame frame = games[3].SourceFireFrame;
            foreach (GameSimulation game in games) game.Step(default(PlayerCommand));
            if (games[3].SourceTick == before)
            {
                CombinedCheck(ReferenceEquals(frame, games[3].SourceFireFrame),
                    "skipped combined host holds the exact immutable fire frame");
                skipped = true;
            }
            foreach (GameSimulation game in games.Skip(1))
            {
                CombinedCheck(JsonSerializer.Serialize(games[0].State, games[0].State.GetType())
                    == JsonSerializer.Serialize(game.State, game.State.GetType()),
                    "all four empty feature combinations preserve historical public snapshot bytes");
                CombinedCheck(JsonSerializer.Serialize(((SourceInjectedRandom)games[0].SourceRandomService).Trace)
                    == JsonSerializer.Serialize(((SourceInjectedRandom)game.SourceRandomService).Trace),
                    "all four empty combinations preserve EVERY shared RNG draw and process/site order");
            }
        }
        CombinedCheck(skipped, "combined sixty/fifty-seven bridge really exercises host skips");
    }

    private static void CombinedLiveResetAndMenu(FullKernelInputs inputs)
    {
        GameSimulation game = CombinedFactory(inputs, TeamId.Home, true, true);
        Cabinet cabinet = CombinedCabinet(game, TeamId.Home, true);
        CombinedFirePreconditions(game, TeamId.Home);
        for (int tick = 0; tick < 3; tick++) cabinet.Wake();
        CombinedCheck(game.SourceFireFrame.Particles.Count > 0 && game.SourceFireProcesses.Count > 0
            && ((ISourceKickMeterState)game.State).KickMeter.Status == SourceKickMeterStatus.Tracking,
            "mixed reset begins with BOTH live effects and an actually tracking meter");
        SourceFireRenderFrame live = game.SourceFireFrame;
        var retained = (ISourceKickMeterState)game.State;
        bool skipped = false;
        for (int host = 0; host < 25 && !skipped; host++)
        {
            long tick = game.SourceTick;
            SourceFireRenderFrame frame = game.SourceFireFrame;
            SourceKickMeterView meter = ((ISourceKickMeterState)game.State).KickMeter;
            cabinet.Host();
            if (game.SourceTick == tick)
            {
                CombinedCheck(ReferenceEquals(frame, game.SourceFireFrame)
                    && ((ISourceKickMeterState)game.State).KickMeter.SourceTick == meter.SourceTick
                    && ((ISourceKickMeterState)game.State).KickMeter.PositionFixed10000 == meter.PositionFixed10000,
                    "a skipped host cannot age the simultaneous live meter or effects");
                skipped = true;
            }
        }
        CombinedCheck(skipped, "live mixed lifecycle observes an actual skipped host");
        CombinedReset(game, true, true, cabinet);
        var fresh = CombinedFactory(inputs, TeamId.Home, true, true);
        for (int tick = 0; tick < 8; tick++)
        {
            game.Step(default(PlayerCommand)); fresh.Step(default(PlayerCommand));
            CombinedCheck(JsonSerializer.Serialize(game.State, game.State.GetType()) ==
                JsonSerializer.Serialize(fresh.State, fresh.State.GetType())
                && JsonSerializer.Serialize(((SourceInjectedRandom)game.SourceRandomService).Trace) ==
                    JsonSerializer.Serialize(((SourceInjectedRandom)fresh.SourceRandomService).Trace),
                "mixed Restart repeats a genuinely fresh factory's exact state and shared RNG trace");
        }
        CombinedCheck(live.Particles.Count > 0 && retained.KickMeter.Status == SourceKickMeterStatus.Tracking,
            "retained live feature artifacts survive later restart/steps without mutation");
        PrepareInitialMatchBoundary(game, TeamId.Home, true, true);
        var menu = new Cabinet(game, Configuration(TeamId.Home));
        menu.Wake();
        long camera = game.SourceFireFrame.Basis.Camera.SourceTick;
        menu.Wake();
        CombinedCheck(game.Model.Phase == GamePhase.PlaySelect
            && game.SourceFireFrame.SourceTick == game.SourceTick
            && game.SourceFireFrame.Basis.Camera.SourceTick == camera,
            "combined menu snapshots retain the genuine suspended camera instead of adding a pose producer");
    }

    internal static int CombinedMain(string[] args)
    {
        try
        {
            FullKernelInputs inputs = FullKernelInputs.Load(args[0], soundFunctionsPath: args[1], c2M1InputsPath: args[2]);
            hud = SourceInterfaceData.Read(File.ReadAllBytes(args[3]), SourceKickMeterMetrics.AuthoredInterfaceSha256);
            metrics = hud.CreatePortableAuthoredKickMeterMetrics();
            combinedAssets = SourceFireSpfxReader.ReadAuthorized(args[4], "manifest.json", args[5]);
            CombinedOptionsAndProfiles(inputs, args[0], args[1]);
            foreach (bool fire in new[] { false, true })
            foreach (bool kick in new[] { false, true })
            foreach (TeamId side in new[] { TeamId.Home, TeamId.Away })
            foreach (bool punt in new[] { false, true })
                CombinedRawCaller(inputs, side, punt, fire, kick);
            CombinedEmptyAndSkips(inputs);
            CombinedLiveResetAndMenu(inputs);
            CombinedCheck(combinedRawCases == 16 && combinedLaunches == 8
                && combinedDisabledGuards == 8 && combinedRestarts == 17,
                "exact four-option/mirrored-kind caller and mixed-reset inventory");
            var result = new { status = "passed", assertions, combinedRawCases, combinedLaunches,
                combinedDisabledGuards, combinedRestarts, cases = combinedCases,
                publicReplay = false, nativeOrUnityOrBrowserProof = false };
            File.WriteAllText(args[6], JsonSerializer.Serialize(result, Json));
            Console.WriteLine("PASS combined runtime: " + assertions + " assertions;16 raw caller cases/8 launches/8 disabled guards/17 resets.");
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(args[6], JsonSerializer.Serialize(new { status = "failed", assertions, error = error.ToString() }, Json));
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}

internal static class IntegrationEntry
{
    private static int Main(string[] args) { return Program.CombinedMain(args); }
}
