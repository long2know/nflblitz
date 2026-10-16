using System;
using System.Collections.Generic;
using System.Linq;
using Blitz.Contracts;
using static Checks;

internal static class SourcePresentationChecks
{
    internal static void Run()
    {
        Flip();
        DrawMetadata();
        InstructionContext();
        CameraSelections();
        CuePayloads();
        OrderedBatches();
        OptionalInterfaces();
    }

    private static SourceMatchConfiguration Configuration()
    {
        return new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation,
            new SourceTeamSelection(30, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceTeamSelection(19, new[] { 9, 8, 7, 6, 5, 4, 3 }),
            new[]
            {
                new SourceStationConfiguration(3, TeamId.Away, 11),
                new SourceStationConfiguration(0, TeamId.Home, 3),
                new SourceStationConfiguration(2, TeamId.Away, 10),
                new SourceStationConfiguration(1, TeamId.Home, 4)
            }, TeamId.Home, SourceCaptainSlot.Second);
    }

    private static SourceMatchView Match(bool? flipped = null)
    {
        return new SourceMatchView(Configuration(), SourcePlayKind.Normal, SourceMatchPhase.PlaySelection,
            new SourcePeriodView(0),
            new SourcePossessionView(TeamId.Home, TeamId.Home, TeamId.Away, TeamId.Home, SourceCaptainSlot.Second),
            new SourceQuarterScore[0], new SourceStationView[0], offenseFlipped: flipped);
    }

    private static SourcePresentationContext At(long tick = 57, int order = 6, int actor = 3,
        int line = 15969, string script = "fall_04", string opcode = "ANI_SETCAMBOT")
    {
        return new SourcePresentationContext(tick, order, actor, "plyrseq.c", script, line, 42, opcode);
    }

    private static void Flip()
    {
        Check("default SourceCommand leaves OffenseFlipped unknown",
            !default(SourceCommand).IsSpecified && !default(SourceCommand).OffenseFlipped.HasValue);
        var on = new SourceCommand(SourceAction.None, offenseFlipped: true);
        Check("flip-only true command remains specified with SourceAction.None",
            on.IsSpecified && on.Action == SourceAction.None && on.OffenseFlipped == true);
        var off = new SourceCommand(SourceAction.None, offenseFlipped: false);
        Check("SourceCommand.OffenseFlipped explicit false is specified and not unknown",
            off.IsSpecified && off.Action == SourceAction.None && off.OffenseFlipped.HasValue && !off.OffenseFlipped.Value);
        Check("flip-only command invents no play target or audible",
            !on.Play.IsSpecified && on.TargetPlayerId == -1 && on.AudibleSlot == -1);
        var none = new SourceCommand(SourceAction.None);
        Check("SourceCommand explicit None stays specified without inventing flip",
            none.IsSpecified && !none.OffenseFlipped.HasValue);
        var selection = new SourcePlaySelection("synthetic_play", SourcePlaybookKind.Offense, 8, 2);
        var selected = new SourceCommand(SourceAction.SelectPlay, selection, 4, SourcePatOption.TwoPointPlay,
            SourceKickoffType.Onside, 2, false);
        Check("appended flip preserves complete positional SourceCommand constructor prefix",
            selected.Action == SourceAction.SelectPlay && selected.Play.PageIndex == 2 &&
            selected.Play.SlotIndex == 8 && selected.TargetPlayerId == 4 && selected.AudibleSlot == 2 &&
            selected.PatChoice == SourcePatOption.TwoPointPlay && selected.KickoffChoice == SourceKickoffType.Onside &&
            selected.OffenseFlipped == false);
        var legacy = new SourceCommand(SourceAction.SelectPlay, selection, 4, SourcePatOption.TwoPointPlay,
            SourceKickoffType.Onside, 2);
        Check("old positional SourceCommand constructor leaves flip unknown", !legacy.OffenseFlipped.HasValue);
        Check("PlayerCommand retains flip-only command", new PlayerCommand(source: on).Source.OffenseFlipped == true);
        var input = new SourceStationInput(3, command: off);
        Check("SourceStationInput retains explicit false flip-only command",
            input.Command.IsSpecified && input.Command.OffenseFlipped == false);
        Check("SourceMatchView legacy/default flip remains unknown", !Match().OffenseFlipped.HasValue);
        Check("SourceMatchView publishes actual true flip", Match(true).OffenseFlipped == true);
        var view = Match(false);
        Check("SourceMatchView publishes actual false flip not unknown",
            view.OffenseFlipped.HasValue && !view.OffenseFlipped.Value);
        Check("flip metadata does not rewrite source play or phase",
            view.Phase == SourceMatchPhase.PlaySelection && !view.HomePlay.IsSpecified && !view.AwayPlay.IsSpecified);
    }

    private static void DrawMetadata()
    {
        SourcePresentationSettings missing = null;
        Check("unpublished presentation settings remain unavailable", missing == null);
        int[] heads = { -1, 0, 19, 7 };
        var settings = new SourcePresentationSettings(TeamId.Away, heads, 2);
        Check("SourcePresentationSettings.DesignatedHomeSide is source side1 not catalog index",
            settings.DesignatedHomeSide == TeamId.Away && Configuration().AwayTeam.TeamIndex == 19);
        Check("SourcePresentationSettings.PupBigBall retains whole source int",
            settings.PupBigBall == 2);
        Array.Fill(heads, 18);
        int[] expected = { -1, 0, 19, 7 };
        for (int station = 0; station < 4; station++)
            Check("SourcePresentationSettings.StationHeads detached station " + station,
                settings.StationHeads[station] == expected[station]);
        Check("known no custom head differs from original custom-head zero",
            settings.StationHeads[0] == -1 && settings.StationHeads[1] == 0);
        Throws<NotSupportedException>("SourcePresentationSettings.StationHeads read-only",
            () => ((IList<int>)settings.StationHeads)[3] = 0);
        Check("source home side0 remains distinct from side1",
            new SourcePresentationSettings(TeamId.Home, expected, 0).DesignatedHomeSide == TeamId.Home);
        foreach (int raw in new[] { 0, 1, 2, 4, 15, int.MinValue, int.MaxValue })
            Check("raw pup_bigball source int retained " + raw,
                new SourcePresentationSettings(TeamId.Home, expected, raw).PupBigBall == raw);
        Throws<ArgumentNullException>("missing four-station head table rejected",
            () => new SourcePresentationSettings(TeamId.Home, null, 0));
        foreach (int count in new[] { 0, 3, 5 })
            Throws<ArgumentException>("head table width rejected " + count,
                () => new SourcePresentationSettings(TeamId.Home, new int[count], 0));
        foreach (int side in new[] { -1, 2, 30 })
            Throws<ArgumentOutOfRangeException>("designated home is a source side not catalog " + side,
                () => new SourcePresentationSettings((TeamId)side, expected, 0));
        for (int station = 0; station < 4; station++)
        {
            foreach (int invalid in new[] { -2, 20 })
            {
                int[] bad = { -1, 0, 19, 7 };
                bad[station] = invalid;
                Throws<ArgumentOutOfRangeException>("head index rejected at station " + station + ": " + invalid,
                    () => new SourcePresentationSettings(TeamId.Home, bad, 0));
            }
        }

        var unknown = default(SourceActorPresentationView);
        Check("default actor presentation invents no source mode0",
            !unknown.IsSpecified && unknown.RawPlayerMode == -1 && unknown.RawFlags == 0);
        var zero = new SourceActorPresentationView(0, 0);
        Check("known source actor flags0 and MODE_LINEUP0 differ from unknown",
            zero.IsSpecified && zero.RawFlags == 0 && zero.RawPlayerMode == 0);
        int flags = int.MinValue | 0x00800005;
        var actual = new SourceActorPresentationView(flags, 5);
        Check("SourceActorPresentationView.RawFlags preserves helmet and unrelated signed bits",
            actual.RawFlags == flags && (actual.RawFlags & 0x00800000) != 0 &&
            (actual.RawFlags & 4) != 0 && (actual.RawFlags & 1) != 0);
        for (int mode = 0; mode <= 10; mode++)
            Check("SourceActorPresentationView retains original player mode " + mode,
                new SourceActorPresentationView(0, mode).RawPlayerMode == mode);
        foreach (int mode in new[] { -1, 11 })
            Throws<ArgumentOutOfRangeException>("unknown original player mode rejected " + mode,
                () => new SourceActorPresentationView(0, mode));
        for (int station = 0; station < 4; station++)
        {
            int actorId = station < 2 ? station + 3 : station + 8;
            TeamId side = station < 2 ? TeamId.Home : TeamId.Away;
            var source = new SourceActorView(station, new SourceRosterIdentity(station < 2 ? 30 : 19, 15 - station),
                3, new SourceActorPresentationView(flags, 7));
            var actor = new PlayerView(actorId, side, default(FieldVector), default(FieldVector), 0f,
                role: ActorRole.Quarterback, source: source);
            Check("station roster actor role and raw mode remain distinct " + station,
                actor.Source.ControllerStationId == station && actor.Source.Roster.RosterIndex == 15 - station &&
                actor.Source.Presentation.RawPlayerMode == 7 && actor.Role == ActorRole.Quarterback &&
                settings.StationHeads[actor.Source.ControllerStationId] == expected[station]);
        }
        var drone = new SourceActorView(-1, new SourceRosterIdentity(30, 4), 30, actual);
        Check("SourceActorView append preserves drone roster no-catch and presentation",
            drone.ControllerStationId == -1 && drone.Roster.TeamIndex == 30 && drone.NoCatchSourceTicks == 30 &&
            drone.Presentation.IsSpecified && drone.Presentation.RawPlayerMode == 5);
        Check("legacy SourceActorView leaves presentation unspecified",
            !new SourceActorView(0, new SourceRosterIdentity(30, 4), 30).Presentation.IsSpecified);
        Check("default SourceActorView leaves presentation unspecified", !default(SourceActorView).Presentation.IsSpecified);

        Check("default ball presentation differs from known flags0",
            !default(SourceBallPresentationView).IsSpecified && new SourceBallPresentationView(0).IsSpecified);
        var held = new BallView(BallMode.Held, default(FieldVector), 0f, 3, -1,
            presentation: new SourceBallPresentationView(0x800));
        Check("SourceBallPresentationView.RawFlags preserves phantom independently of BallMode",
            held.Presentation.IsSpecified && held.Presentation.RawFlags == 0x800 &&
            held.Mode == BallMode.Held && held.CarrierId == 3);
        var flight = new BallView(BallMode.InFlight, default(FieldVector), 0f, -1, 4,
            presentation: new SourceBallPresentationView(0));
        Check("in-flight ball does not invent BF_PHANTOM", flight.Presentation.RawFlags == 0);
        Check("ball raw flags preserve all signed source bits",
            new SourceBallPresentationView(int.MinValue | 0x800).RawFlags == (int.MinValue | 0x800));
        var control = new SourceBallControlView(0, 3);
        var ball = new BallView(BallMode.Held, default(FieldVector), 1f, 3, 4, SourceBallKind.Fumble,
            new FieldVector(2f, 3f), 4f, 5, control, new SourceBallPresentationView(0));
        Check("appended BallView presentation preserves every positional constructor argument",
            ball.Mode == BallMode.Held && ball.Height == 1f && ball.CarrierId == 3 && ball.TargetPlayerId == 4 &&
            ball.Kind == SourceBallKind.Fumble && ball.Velocity.X == 2f && ball.Velocity.Z == 3f &&
            ball.VerticalVelocity == 4f && ball.LastTouchPlayerId == 5 && ball.Control.ControllerPlayerId == 3 &&
            ball.Presentation.IsSpecified);
        Check("legacy BallView leaves presentation unspecified",
            !new BallView(BallMode.Held, default(FieldVector), 0f, 3, -1).Presentation.IsSpecified);
        Check("default BallView leaves presentation unspecified", !default(BallView).Presentation.IsSpecified);
    }

    private static void InstructionContext()
    {
        var unknown = default(SourcePresentationContext);
        Check("default presentation context invents no source tick actor instruction or provenance",
            !unknown.IsSpecified && unknown.SourceTick == -1 && unknown.ProcessOrder == -1 &&
            unknown.ActorPlayerId == -1 && unknown.SourceLine == -1 && unknown.SourcePath == null &&
            unknown.ExecutingSymbol == null && !unknown.ScriptInstanceId.HasValue && unknown.OpcodeSymbol == null);
        var context = At();
        Check("SourcePresentationContext preserves actual source tick and instruction order",
            context.IsSpecified && context.SourceTick == 57 && context.ProcessOrder == 6);
        Check("SourcePresentationContext preserves actual actor", context.ActorPlayerId == 3);
        Check("SourcePresentationContext preserves exact executed script source location",
            context.SourcePath == "plyrseq.c" && context.ExecutingSymbol == "fall_04" && context.SourceLine == 15969);
        Check("SourcePresentationContext preserves actual script instance and original opcode token",
            context.ScriptInstanceId == 42 && context.OpcodeSymbol == "ANI_SETCAMBOT");
        var function = new SourcePresentationContext(57, 8, 10, "detect.c", "evade_vs_tackle", 2494);
        Check("function context does not forge animation instance or opcode",
            function.SourcePath == "detect.c" && function.ExecutingSymbol == "evade_vs_tackle" &&
            !function.ScriptInstanceId.HasValue && function.OpcodeSymbol == null);
        var first = new SourcePresentationContext(0, 0, 0, "plyrseq.c", "fall_04", 15969, 0, "ANI_SETCAMBOT");
        Check("known source tick order actor and instance0 remain specified",
            first.IsSpecified && first.SourceTick == 0 && first.ProcessOrder == 0 &&
            first.ActorPlayerId == 0 && first.ScriptInstanceId.HasValue && first.ScriptInstanceId.Value == 0);
        Throws<ArgumentOutOfRangeException>("negative source cue tick rejected", () => At(tick: -1));
        Throws<ArgumentOutOfRangeException>("negative within-tick instruction order rejected", () => At(order: -1));
        foreach (int actor in new[] { -1, 14 })
            Throws<ArgumentOutOfRangeException>("cue actor bounds rejected " + actor, () => At(actor: actor));
        foreach (int line in new[] { -1, 0 })
            Throws<ArgumentOutOfRangeException>("source line bounds rejected " + line, () => At(line: line));
        foreach (string invalid in new[] { null, "", " " })
        {
            Throws<ArgumentException>("source cue path required: " + invalid,
                () => new SourcePresentationContext(0, 0, 0, invalid, "fall_04", 1));
            Throws<ArgumentException>("executing script or function required: " + invalid,
                () => new SourcePresentationContext(0, 0, 0, "plyrseq.c", invalid, 1));
        }
        Throws<ArgumentOutOfRangeException>("negative actual script instance rejected",
            () => new SourcePresentationContext(0, 0, 0, "plyrseq.c", "fall_04", 1, -1));
        foreach (string invalid in new[] { "", " " })
            Throws<ArgumentException>("provided original opcode cannot be blank: " + invalid,
                () => new SourcePresentationContext(0, 0, 0, "plyrseq.c", "fall_04", 1, opcodeSymbol: invalid));
    }

    private static void CameraSelections()
    {
        var unknown = default(SourceCameraSelection);
        Check("default camera selection invents no CSI ordinal0 or ready provenance",
            !unknown.IsSpecified && unknown.SelectedOrdinal == -1 && unknown.EntryCount == 0 &&
            unknown.TableSymbol == null && !unknown.RawRandomValue.HasValue && unknown.RandomSiteContext == null);
        const string site = "synthetic DTO tape: select_csi_ptr RandRange(1) tick=57 actor=3 process=player_proc/3 call=0";
        var single = new SourceCameraSelection("csi_rs_suplex", 0, 1, 7391, site);
        Check("actual CSI table and selected ordinal0 retained",
            single.IsSpecified && single.TableSymbol == "csi_rs_suplex" && single.SelectedOrdinal == 0 && single.EntryCount == 1);
        Check("randrng1 raw draw is not discarded because selected ordinal is0",
            single.RawRandomValue.HasValue && single.RawRandomValue.Value == 7391);
        Check("actual camera RNG site context is retained verbatim", single.RandomSiteContext == site);
        var untraced = new SourceCameraSelection("csi_rs_suplex", 0, 1);
        Check("selected CSI without supplied trace does not forge RNG provenance",
            untraced.IsSpecified && !untraced.RawRandomValue.HasValue && untraced.RandomSiteContext == null);
        Check("raw random0 remains known rather than absent",
            new SourceCameraSelection("csi_rs_suplex", 0, 1, 0, site).RawRandomValue == 0);
        var weight0 = new SourceCameraSelection("csi_fs_endplay", 5, 8, 13, "synthetic weighted site");
        var weight1 = new SourceCameraSelection("csi_fs_endplay", 6, 8, 14, "synthetic weighted site");
        Check("weighted CSI ordinals remain distinct without deduplication",
            weight0.TableSymbol == weight1.TableSymbol && weight0.SelectedOrdinal == 5 && weight1.SelectedOrdinal == 6);
        foreach (int count in new[] { -1, 0 })
            Throws<ArgumentOutOfRangeException>("nonpositive actual CSI count rejected " + count,
                () => new SourceCameraSelection("csi_rs_suplex", 0, count));
        Throws<ArgumentOutOfRangeException>("negative CSI ordinal rejected",
            () => new SourceCameraSelection("csi_rs_suplex", -1, 1));
        Throws<ArgumentOutOfRangeException>("SourceCameraSelection rejects ordinal at actual entry count",
            () => new SourceCameraSelection("csi_rs_suplex", 1, 1));
        Throws<ArgumentException>("missing CSI table identity rejected", () => new SourceCameraSelection(" ", 0, 1));
        Throws<ArgumentOutOfRangeException>("negative raw camera draw rejected",
            () => new SourceCameraSelection("csi_rs_suplex", 0, 1, -1, site));
        Throws<ArgumentException>("camera raw draw without actual site rejected",
            () => new SourceCameraSelection("csi_rs_suplex", 0, 1, 7391));
        Throws<ArgumentException>("camera site without raw draw rejected",
            () => new SourceCameraSelection("csi_rs_suplex", 0, 1, randomSiteContext: site));
        Throws<ArgumentException>("blank supplied camera RNG site rejected",
            () => new SourceCameraSelection("csi_rs_suplex", 0, 1, 7391, " "));
        Throws<ArgumentException>("actual camera draw must agree with selected CSI ordinal",
            () => new SourceCameraSelection("csi_fs_endplay", 0, 8, 5, "synthetic weighted site"));
    }

    private static void CuePayloads()
    {
        var selection = new SourceCameraSelection("csi_rs_suplex", 0, 1);
        var camera = new SourceCameraCue(At(), SourcePresentationCueKind.CameraOpcode, selection, 3);
        Check("camera cue retains executing instance opcode and actual carrier",
            camera.Kind == SourcePresentationCueKind.CameraOpcode && camera.Context.ScriptInstanceId == 42 &&
            camera.Context.OpcodeSymbol == "ANI_SETCAMBOT" && camera.CarrierPlayerId == 3);
        var contact = new SourceCameraCue(new SourcePresentationContext(57, 9, 10, "detect.c", "evade_vs_tackle", 2494),
            SourcePresentationCueKind.ContactCameraSelection, new SourceCameraSelection("csi_fs_endplay", 0, 8), 10);
        Check("detect endplay selection stays distinct from animation camera opcode",
            contact.Kind == SourcePresentationCueKind.ContactCameraSelection && contact.Context.SourcePath == "detect.c" &&
            contact.Selection.TableSymbol == "csi_fs_endplay" && !contact.Context.ScriptInstanceId.HasValue);
        Check("camera carrier unavailable differs from explicitly no carrier",
            !new SourceCameraCue(At(), SourcePresentationCueKind.CameraOpcode, selection).CarrierPlayerId.HasValue &&
            new SourceCameraCue(At(), SourcePresentationCueKind.CameraOpcode, selection, -1).CarrierPlayerId == -1);
        Check("camera actor and known carrier0 need not match",
            new SourceCameraCue(At(), SourcePresentationCueKind.CameraOpcode, selection, 0).CarrierPlayerId == 0);
        Throws<ArgumentException>("camera cue cannot publish default context",
            () => new SourceCameraCue(default(SourcePresentationContext), SourcePresentationCueKind.CameraOpcode, selection));
        Throws<ArgumentException>("camera cue cannot publish default CSI selection",
            () => new SourceCameraCue(At(), SourcePresentationCueKind.CameraOpcode, default(SourceCameraSelection)));
        foreach (SourcePresentationCueKind kind in new[] { SourcePresentationCueKind.Unspecified, SourcePresentationCueKind.ShakerOpcode })
            Throws<ArgumentOutOfRangeException>("camera cue rejects unrelated boundary tag " + kind,
                () => new SourceCameraCue(At(), kind, selection));
        foreach (int carrier in new[] { -2, 14 })
            Throws<ArgumentOutOfRangeException>("camera carrier bounds rejected " + carrier,
                () => new SourceCameraCue(At(), SourcePresentationCueKind.CameraOpcode, selection, carrier));

        var noncarrier = new SourceShakerCue(At(line: 16000, opcode: "ANI_SHAKER"), 2, 10, 10);
        Check("noncarrier shaker retains pre-guard amplitude total and actual carrier",
            noncarrier.Amplitude == 2 && noncarrier.TotalTicks == 10 && noncarrier.CarrierPlayerId == 10 &&
            noncarrier.Context.ActorPlayerId == 3 && noncarrier.Kind == SourcePresentationCueKind.ShakerOpcode);
        var noCarrier = new SourceShakerCue(At(line: 5560, script: "h_front1_anim", opcode: "ANI_SHAKER"), 3, 10, -1);
        Check("shaker without carrier retains original assignments and h_front1 location",
            noCarrier.Amplitude == 3 && noCarrier.TotalTicks == 10 && noCarrier.CarrierPlayerId == -1 &&
            noCarrier.Context.SourceLine == 5560 && noCarrier.Context.ExecutingSymbol == "h_front1_anim");
        var max = new SourceShakerCue(At(), 255, int.MaxValue, 3);
        Check("shaker byte amplitude and original word total retain inclusive widths",
            max.Amplitude == 255 && max.TotalTicks == int.MaxValue);
        var zero = new SourceShakerCue(At(), 0, 0, 3);
        Check("known shaker zero assignments are not filtered", zero.Amplitude == 0 && zero.TotalTicks == 0);
        foreach (int amplitude in new[] { -1, 256 })
            Throws<ArgumentOutOfRangeException>("shaker byte bounds rejected " + amplitude,
                () => new SourceShakerCue(At(), amplitude, 10, 3));
        Throws<ArgumentOutOfRangeException>("negative shaker total rejected", () => new SourceShakerCue(At(), 2, -1, 3));
        foreach (int carrier in new[] { -2, 14 })
            Throws<ArgumentOutOfRangeException>("shaker carrier bounds rejected " + carrier,
                () => new SourceShakerCue(At(), 2, 10, carrier));
        Throws<ArgumentException>("shaker cannot publish default context",
            () => new SourceShakerCue(default(SourcePresentationContext), 2, 10, 3));

        var sound = new SourceCallbackCue(At(line: 15998, opcode: "ANI_CODE"),
            SourcePresentationCueKind.SoundCallback, "grnd_hit_snd", -1, 0, -1, 3);
        Check("original grnd_hit_snd arguments preserve both minus1 sentinels",
            sound.FunctionSymbol == "grnd_hit_snd" && sound.Argument0 == -1 &&
            sound.Argument1 == 0 && sound.Argument2 == -1);
        Check("sound callback retains original execution site and carrier",
            sound.Context.SourceLine == 15998 && sound.Context.ExecutingSymbol == "fall_04" &&
            sound.Context.OpcodeSymbol == "ANI_CODE" && sound.CarrierPlayerId == 3);
        var wide = new SourceCallbackCue(At(), SourcePresentationCueKind.SoundCallback, "get_hit_snd",
            int.MinValue, -1, int.MaxValue);
        Check("original callback word arguments retain full signed width",
            wide.Argument0 == int.MinValue && wide.Argument1 == -1 && wide.Argument2 == int.MaxValue);
        Check("callback does not invent an unavailable carrier", !wide.CarrierPlayerId.HasValue);
        foreach (SourcePresentationCueKind kind in new[] { SourcePresentationCueKind.DustCallback,
            SourcePresentationCueKind.SoundCallback, SourcePresentationCueKind.PlayerDownCallback })
            Check("typed original callback kind retained " + kind,
                new SourceCallbackCue(At(), kind, "synthetic_callback", 0, 0, 0).Kind == kind);
        var dust = new SourceCallbackCue(At(line: 13877, script: "c_dive_for_ball_anim", opcode: "ANI_CODE"),
            SourcePresentationCueKind.DustCallback, "dust_cloud", 0, 0, 0);
        Check("dust cue retains actual script function and source location",
            dust.Context.ExecutingSymbol == "c_dive_for_ball_anim" && dust.Context.SourceLine == 13877 &&
            dust.FunctionSymbol == "dust_cloud" && dust.Argument0 == 0);
        Throws<ArgumentException>("callback function identity required",
            () => new SourceCallbackCue(At(), SourcePresentationCueKind.SoundCallback, " ", -1, 0, -1));
        foreach (SourcePresentationCueKind kind in new[] { SourcePresentationCueKind.Unspecified, SourcePresentationCueKind.CameraOpcode,
            SourcePresentationCueKind.ShakerOpcode, (SourcePresentationCueKind)7 })
            Throws<ArgumentOutOfRangeException>("callback rejects unrelated boundary tag " + kind,
                () => new SourceCallbackCue(At(), kind, "grnd_hit_snd", -1, 0, -1));
        Throws<ArgumentException>("callback cannot publish default context",
            () => new SourceCallbackCue(default(SourcePresentationContext), SourcePresentationCueKind.SoundCallback,
                "grnd_hit_snd", -1, 0, -1));
        foreach (int carrier in new[] { -2, 14 })
            Throws<ArgumentOutOfRangeException>("callback carrier bounds rejected " + carrier,
                () => new SourceCallbackCue(At(), SourcePresentationCueKind.SoundCallback, "grnd_hit_snd", -1, 0, -1, carrier));
    }

    private static void OrderedBatches()
    {
        var camera = new SourceCameraCue(At(), SourcePresentationCueKind.CameraOpcode,
            new SourceCameraSelection("csi_rs_suplex", 0, 1));
        var down = new SourceCallbackCue(At(tick: 58, order: 0, line: 15995, opcode: "ANI_CODE"),
            SourcePresentationCueKind.PlayerDownCallback, "player_down", 0, 0, 0);
        var sound = new SourceCallbackCue(At(tick: 58, order: 1, line: 15998, opcode: "ANI_CODE"),
            SourcePresentationCueKind.SoundCallback, "grnd_hit_snd", -1, 0, -1);
        var shaker = new SourceShakerCue(At(tick: 58, order: 2, line: 16000, opcode: "ANI_SHAKER"), 2, 10, 10);
        var next = new SourceCallbackCue(At(tick: 59, order: 0, actor: 13, line: 2464, script: "t_dshoul_anim", opcode: "ANI_CODE"),
            SourcePresentationCueKind.DustCallback, "dust_cloud", 0, 0, 0);
        SourcePresentationCue[] source = { camera, down, sound, shaker, next };
        var batch = new SourcePresentationCueBatch(source);
        source[0] = next;
        Check("SourcePresentationCueBatch.Cues detached from caller array",
            ReferenceEquals(batch.Cues[0], camera) && batch.Cues[0].Context.SourceTick == 57);
        Check("source ground player_down sound shaker ordering retained",
            batch.Cues[1].Kind == SourcePresentationCueKind.PlayerDownCallback &&
            batch.Cues[2].Kind == SourcePresentationCueKind.SoundCallback &&
            batch.Cues[3].Kind == SourcePresentationCueKind.ShakerOpcode);
        Check("batch keeps every executed catch-up source tick and reset within-tick order",
            batch.Cues.Count == 5 && batch.Cues[1].Context.SourceTick == 58 &&
            batch.Cues[4].Context.SourceTick == 59 && batch.Cues[4].Context.ProcessOrder == 0);
        Check("ordered cue actor does not replace actual carrier identity",
            batch.Cues[3].Context.ActorPlayerId == 3 && batch.Cues[3].CarrierPlayerId == 10 &&
            batch.Cues[4].Context.ActorPlayerId == 13);
        Throws<NotSupportedException>("SourcePresentationCueBatch.Cues read-only",
            () => ((IList<SourcePresentationCue>)batch.Cues)[0] = next);
        var list = new List<SourcePresentationCue> { camera, down };
        var copied = new SourcePresentationCueBatch(list);
        list.Clear();
        Check("SourcePresentationCueBatch copies non-array source collections", copied.Cues.Count == 2);
        var later = new SourcePresentationCueBatch(new SourcePresentationCue[] { next });
        Check("retained cue batch unchanged by publication of next batch",
            batch.Cues.Count == 5 && batch.Cues[0].Context.ScriptInstanceId == 42 &&
            later.Cues.Count == 1 && later.Cues[0].Context.SourceTick == 59);
        Check("known empty presentation batch is not unavailable",
            new SourcePresentationCueBatch(new SourcePresentationCue[0]).Cues.Count == 0);
        Throws<ArgumentNullException>("missing cue collection rejected", () => new SourcePresentationCueBatch(null));
        Throws<ArgumentException>("null cue item rejected",
            () => new SourcePresentationCueBatch(new SourcePresentationCue[] { null }));
        var duplicateOrder = new SourceShakerCue(At(actor: 4), 3, 10, 3);
        Throws<ArgumentException>("SourcePresentationCueBatch rejects duplicate within-tick instruction order",
            () => new SourcePresentationCueBatch(new SourcePresentationCue[] { camera, duplicateOrder }));
        Throws<ArgumentException>("same cue cannot be replayed in a batch",
            () => new SourcePresentationCueBatch(new SourcePresentationCue[] { camera, camera }));
        Throws<ArgumentException>("source cue tick regression rejected rather than sorted",
            () => new SourcePresentationCueBatch(new SourcePresentationCue[] { next, camera }));
        var earlier = new SourceShakerCue(At(order: 5), 2, 10, 3);
        Throws<ArgumentException>("source instruction order regression rejected rather than sorted",
            () => new SourcePresentationCueBatch(new SourcePresentationCue[] { camera, earlier }));
        var sameSiteLater = new SourceCameraCue(At(order: 7), SourcePresentationCueKind.CameraOpcode, camera.Selection);
        Check("same source location may execute again with a distinct actual instruction order",
            new SourcePresentationCueBatch(new SourcePresentationCue[] { camera, sameSiteLater }).Cues.Count == 2);
    }

    private static void OptionalInterfaces()
    {
        Check("presentation snapshot is optional additive source-match extension",
            typeof(ISourceMatchState).IsAssignableFrom(typeof(ISourcePresentationState)));
        Check("presentation simulation is optional additive source-match extension",
            typeof(ISourceMatchSimulation).IsAssignableFrom(typeof(ISourcePresentationSimulation)));
        Check("presentation snapshot property uses detached typed settings",
            typeof(ISourcePresentationState).GetProperty("Presentation").PropertyType == typeof(SourcePresentationSettings));
        Check("presentation simulation property uses immutable cue batch",
            typeof(ISourcePresentationSimulation).GetProperty("PresentationCues").PropertyType == typeof(SourcePresentationCueBatch));
        Check("existing source-match interfaces gain no mandatory presentation property",
            typeof(ISourceMatchState).GetProperty("Presentation") == null &&
            typeof(ISourceMatchSimulation).GetProperty("PresentationCues") == null);
        foreach (Type type in new[] { typeof(SourcePresentationSettings), typeof(SourceCameraCue),
            typeof(SourceShakerCue), typeof(SourceCallbackCue), typeof(SourcePresentationCueBatch) })
            Check("immutable presentation carrier is sealed " + type.Name, type.IsSealed);
        foreach (Type type in new[] { typeof(SourceCommand), typeof(SourceMatchView), typeof(SourceActorView),
            typeof(BallView), typeof(SourcePresentationSettings), typeof(SourceActorPresentationView),
            typeof(SourceBallPresentationView), typeof(SourcePresentationContext), typeof(SourceCameraSelection),
            typeof(SourcePresentationCue), typeof(SourceCameraCue), typeof(SourceShakerCue),
            typeof(SourceCallbackCue), typeof(SourcePresentationCueBatch) })
            Check("presentation properties have no public mutation surface " + type.Name,
                type.GetProperties().All(property => property.GetSetMethod() == null));
    }
}
