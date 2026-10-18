using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal static partial class Program
{
    private static int lifecycleCases;

    private static void BoundaryAndReset(FullKernelInputs inputs, int profile, TeamId side, bool punt)
    {
        var configuration = Configuration(side);
        GameSimulation game = Factory(inputs, profile, configuration, metrics);
        PrepareInitialMatchBoundary(game, side, punt, true);
        var cabinet = new Cabinet(game, configuration);
        cabinet.Select(punt ? "play_punt" : "play_field_goal", side);
        for (int tick = 0; tick < 8; tick++) cabinet.Wake();
        Check(game.Model.PreSnapTicksRemaining == 276, "actual special-teams fixture is just before the strict grace threshold");
        cabinet.Wake(SourceButtons.A);
        Check(game.Model.PreSnapTicksRemaining == 275 && (game.Model.SourceFlags & SourceGameFlags.HikingBall) == 0,
            "new A at exactly MAX_HIKE_TIME-10 cannot snap");
        cabinet.Wake(SourceButtons.A);
        Check(game.Model.PreSnapTicksRemaining == 274 && (game.Model.SourceFlags & SourceGameFlags.HikingBall) == 0,
            "holding early A past the threshold does not synthesize another edge");
        cabinet.Wake();
        cabinet.Wake(SourceButtons.C);
        Check((game.Model.SourceFlags & SourceGameFlags.HikingBall) == 0,
            "C/turbo is not a meter lock even after the source grace period");
        cabinet.Wake();
        long tickBefore = game.SourceTick;
        cabinet.Wake(SourceButtons.A | SourceButtons.B);
        Check(game.Model.SourceSnapTick == tickBefore + 1 && game.Model.PlayNumber == 1,
            "simultaneous new A+B produces ONE real source snap request");
        cabinet.Wake();
        SourceKickMeterView retained = ((ISourceKickMeterState)game.State).KickMeter;
        Check(retained.Status == SourceKickMeterStatus.Locked, "combined A+B locks in actual meter continuation");
        game.ClearPendingInput(); cabinet.Buffer.Clear();
        cabinet.Wake(SourceButtons.B);
        Check(((ISourceKickMeterState)game.State).KickMeter.AccuracyPercent == retained.AccuracyPercent
            && ((ISourceKickMeterState)game.State).KickMeter.PuntStrength == retained.PuntStrength,
            "focus/input drain cannot relock a committed meter or overwrite original result");
        game.Restart(1);
        ISourceKickMeterState restarted = (ISourceKickMeterState)game.State;
        Check(game.SourceTick == 0 && restarted.KickMeterMode == SourceKickMeterMode.PortableAuthored
            && !restarted.KickMeter.IsSpecified && !restarted.LastKickLaunch.IsSpecified,
            "actual Restart clears the old process/result but preserves explicit opt-in provider");
        Check(retained.Status == SourceKickMeterStatus.Locked && retained.CreatedSourceTick > 0,
            "old immutable snapshot survives restart without mutation");
        lifecycleCases++;
    }

    private static void AutomaticAndFake(FullKernelInputs inputs, int profile, TeamId side, bool punt, bool fake)
    {
        var configuration = Configuration(side);
        GameSimulation game = Factory(inputs, profile, configuration, metrics);
        PrepareInitialMatchBoundary(game, side, punt, true);
        var cabinet = new Cabinet(game, configuration);
        cabinet.Select(fake ? punt ? "play_fake_punt" : "play_fake_field_goal" : punt ? "play_punt" : "play_field_goal", side);
        if (fake)
        {
            for (int tick = 0; tick < 12; tick++) cabinet.Wake();
            cabinet.Wake(SourceButtons.A);
            cabinet.Wake();
            SourceKickMeterView result = ((ISourceKickMeterState)game.State).KickMeter;
            Check(result.Fake && result.Status == SourceKickMeterStatus.Complete && !result.ShowMeterFlag,
                "actual fake play exits its meter immediately, without genuine power/accuracy hold");
            Check(game.Model.SourcePuntStrength == 99 && game.Model.SourceKickMeterPercent == 0f
                && !((ISourceKickMeterState)game.State).LastKickLaunch.IsSpecified,
                "fake meter does not inject a real kick or override the original initial strength");
        }
        else
        {
            long request = -1;
            for (int tick = 0; tick < 286 && request < 0; tick++)
            {
                cabinet.Wake();
                if ((game.Model.SourceFlags & SourceGameFlags.HikingBall) != 0) request = game.SourceTick;
            }
            Check(request >= 0 && game.Model.SourceHikeCounterAtSnap == 0,
                "real neutral human cabinet reaches original automatic hike, not an injected request");
            cabinet.Wake();
            SourceKickMeterView expired = ((ISourceKickMeterState)game.State).KickMeter;
            Check(expired.Status == SourceKickMeterStatus.TimeExpired && expired.SourceTick == request + 1,
                "timeout is classified by meter continuation one source wake after real auto-hike");
            Check(punt ? expired.PuntStrength == 99 : expired.AccuracyPercent == 84f,
                "original automatic branch retains random punt initial power or performs exact ordered FG draws");
            var viewport = new SourceHudViewport(1024, 768, 1024, 768, 1);
            var composer = new SourceHudComposer(hud);
            var timeoutDraw = composer.Compose(game.State, viewport, cabinet.Selection, false, false);
            Check(timeoutDraw.Any(p => p.Element == "kick/timeout/art") && timeoutDraw.Any(p => p.Element == "kick/timeout/text"),
                "real TIME EXPIRED HUD consumes actual timeout state and original art/font");
            SourceKickLaunchView launch = default(SourceKickLaunchView);
            for (int tick = 0; tick < 100 && !launch.IsSpecified; tick++)
            {
                cabinet.Wake();
                launch = ((ISourceKickMeterState)game.State).LastKickLaunch;
            }
            Check(launch.IsSpecified && ((ISourceKickMeterState)game.State).KickMeter.Status == SourceKickMeterStatus.TimeExpired,
                "auto-hiked human kick actually launches while original timeout process waits for play-over");
        }
        lifecycleCases++;
    }

    private static void NonKickAndDefaults(FullKernelInputs inputs, int profile)
    {
        var configuration = Configuration(TeamId.Home);
        var plain = Factory(inputs, profile, configuration, null);
        var opted = Factory(inputs, profile, configuration, metrics);
        PrepareInitialMatchBoundary(plain, TeamId.Home, true, true);
        PrepareInitialMatchBoundary(opted, TeamId.Home, true, true);
        var p = new Cabinet(plain, configuration);
        var k = new Cabinet(opted, configuration);
        for (int tick = 0; tick < 24; tick++) { p.Wake(); k.Wake(); }
        Check(!((ISourceKickMeterState)plain.State).KickMeter.IsSpecified
            && ((ISourceKickMeterState)plain.State).KickMeterMode == SourceKickMeterMode.Disabled,
            "disabled state is explicit, not implicitly inherited from gameplay compatibility");
        p.Wake(SourceButtons.B); k.Wake(SourceButtons.B);
        Check(plain.Model.SourceOffensePlay.Identity == opted.Model.SourceOffensePlay.Identity
            && plain.Model.SourceOffensePlay.Flags == SourcePlayFlags.None,
            "paired real cabinet non-kick selection uses the same original play");
        for (int tick = 0; tick < 40; tick++)
        {
            p.Wake(); k.Wake();
            Check(JsonSerializer.Serialize(plain.State, plain.State.GetType()) == JsonSerializer.Serialize(opted.State, opted.State.GetType()),
                "enabled non-kicking profile leaves historical public snapshot byte representation unchanged");
            Check(JsonSerializer.Serialize(((SourceInjectedRandom)plain.SourceRandomService).Trace)
                == JsonSerializer.Serialize(((SourceInjectedRandom)opted.SourceRandomService).Trace),
                "enabled non-kicking profile leaves every ordered shared random draw unchanged");
        }
        Check(!JsonSerializer.Serialize(plain.State, plain.State.GetType()).Contains("\"KickMeter"),
            "new immutable extension does not alter disabled historical snapshot serialization");
        lifecycleCases++;
    }

    private static void LifecycleChecks(FullKernelInputs[] inputs)
    {
        for (int profile = 0; profile < 3; profile++)
        {
            NonKickAndDefaults(inputs[profile], profile);
            foreach (TeamId side in new[] { TeamId.Home, TeamId.Away })
            foreach (bool punt in new[] { false, true })
            {
                BoundaryAndReset(inputs[profile], profile, side, punt);
                AutomaticAndFake(inputs[profile], profile, side, punt, true);
                AutomaticAndFake(inputs[profile], profile, side, punt, false);
            }
        }
        Check(lifecycleCases == 39, "exact39 real lifecycle/default-preservation cases");
    }
}
