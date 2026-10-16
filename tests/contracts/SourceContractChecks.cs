using System;
using System.Collections.Generic;
using System.Linq;
using Blitz.Contracts;
using Blitz.Gameplay;
using static Checks;

internal static class SourceContractChecks
{
    internal static int EnumMappings { get; private set; }

    private static void Map<TContract, TGameplay>(params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            int contract = Convert.ToInt32(Enum.Parse(typeof(TContract), names[i]));
            int gameplay = Convert.ToInt32(Enum.Parse(typeof(TGameplay), names[i]));
            Check(typeof(TContract).Name + "." + names[i] + " exact production mapping",
                contract == gameplay && contract == i);
            EnumMappings++;
        }
    }

    internal static void RunPromoted()
    {
        Map<SourcePlayKind, SourcePlayType>("Normal", "Kickoff", "FieldGoal", "Punt", "ExtraPoint");
        Map<SourceResultKind, SourcePlayResult>("Down", "Touchdown", "Safety", "PatSafety",
            "NewPossession", "Incomplete", "FumbledOutOfBounds", "FieldGoal", "ExtraPoint",
            "MissedKick", "FailedConversion", "Conversion", "PuntOutOfBounds", "DownedPunt",
            "KickoffOutOfBounds", "Touchback");
        Map<SourceResultStatus, SourceClassifierStatus>("Classified", "IntentionalNoOp", "InvalidSourceBranch");
        Map<SourcePlayEndKind, SourcePlayEndCause>("CarrierOutOfBounds", "CarrierTackledInBounds",
            "CarrierDownInOwnEndZone", "CarrierTouchdown", "DeadBallInBounds", "BallOutOfBounds");
        Map<SourcePatOption, SourcePatChoice>("Unselected", "FreeKick", "TwoPointPlay");
        Map<SourcePatStatus, SourcePatDisposition>("NotExtraPoint", "SkipUnnecessary", "AwaitHumanSelection",
            "AwaitRandom", "RunTwoPointPlay", "FreeKickComplete");
        Map<SourceKickoffType, SourceKickoffKind>("Opening", "Quick", "Onside");
        Map<SourcePeriodStatus, SourcePeriodDisposition>("NotExpired", "FinishCurrentPlay",
            "DeferredForExtraPoint", "AttractMode", "AwaitOvertimeDraw", "NextQuarter", "HalftimeKickoff",
            "OvertimeKickoff", "MatchEnded", "CabinetContinuationDeclined");
        Map<SourcePlayPool, SourceCpuPlayFamily>("Punt", "FakePunt", "FieldGoal", "ShortOffense",
            "LongOffense", "PuntReturn", "BlockFieldGoal", "GoalLineDefense", "BlitzDefense",
            "ShortDefense", "LongDefense", "ShortDefenseCustom", "LongDefenseCustom");

        Check("pinned cardinalities", SourceMatchLimits.TeamCount == 31 &&
            SourceMatchLimits.RosterRecordsPerTeam == 16 && SourceMatchLimits.MaximumHumanStations == 4 &&
            SourceMatchLimits.MaximumPeriodCount == 7);
        var roster0 = default(SourceRosterIdentity);
        Check("default roster does not invent team/player zero",
            !roster0.IsSpecified && roster0.TeamIndex == -1 && roster0.RosterIndex == -1);
        var play0 = default(SourcePlaySelection);
        Check("default play has unspecified identity/ordinals",
            !play0.IsSpecified && play0.PageIndex == -1 && play0.SlotIndex == -1 && play0.PoolIndex == -1);
        var command0 = default(SourceCommand);
        Check("default source command has no target/audible",
            !command0.IsSpecified && command0.TargetPlayerId == -1 && command0.AudibleSlot == -1 &&
            command0.Action == SourceAction.None);
        var result0 = default(SourceMatchResult);
        Check("default result is not classified Down", !result0.IsSpecified && !result0.Status.HasValue);
        var actor0 = default(SourceActorView);
        Check("default actor has no human station", !actor0.IsSpecified && actor0.ControllerStationId == -1);
        var control0 = default(SourceBallControlView);
        Check("default ball grants no controller/credit", !control0.IsSpecified &&
            control0.ControllerPlayerId == -1 && control0.ControllerStationId == -1 &&
            !control0.LastPossession.HasValue && !control0.PossessionCreditEligible);
        var event0 = default(SourceGameEvent);
        Check("default event invents no actor or station zero", !event0.IsSpecified &&
            event0.ActorPlayerId == -1 && event0.TargetPlayerId == -1 && event0.StationId == -1);

        var selected = new SourcePlaySelection("synthetic_play", SourcePlaybookKind.Offense,
            8, 2, SourcePlayPool.LongOffense, 5);
        var repeatedWeight = new SourcePlaySelection("synthetic_play", SourcePlaybookKind.Offense,
            8, 2, SourcePlayPool.LongOffense, 6);
        Check("original play symbol/page/slot/pool retained", selected.PlaySymbol == "synthetic_play" &&
            selected.PageIndex == 2 && selected.SlotIndex == 8 && selected.Pool.Value == SourcePlayPool.LongOffense &&
            selected.PoolIndex == 5);
        Check("duplicate weighted play keeps ordinal identity",
            selected.PlaySymbol == repeatedWeight.PlaySymbol && selected.PoolIndex != repeatedWeight.PoolIndex);
        Throws<ArgumentException>("blank play symbol rejected",
            () => new SourcePlaySelection(" ", SourcePlaybookKind.Offense));
        Throws<ArgumentException>("pool without ordinal rejected",
            () => new SourcePlaySelection("synthetic", SourcePlaybookKind.Offense, pool: SourcePlayPool.Punt));
        Throws<ArgumentException>("ordinal without pool rejected",
            () => new SourcePlaySelection("synthetic", SourcePlaybookKind.Offense, poolIndex: 0));

        int[] indices = Enumerable.Range(0, 7).ToArray();
        var home = new SourceTeamSelection(0, indices);
        indices[0] = 15;
        Check("SourceTeamSelection.RosterIndices detached", home.RosterIndices[0] == 0);
        Throws<NotSupportedException>("SourceTeamSelection.RosterIndices read-only",
            () => ((IList<int>)home.RosterIndices)[0] = 1);
        Check("duplicate static records not forbidden",
            new SourceTeamSelection(30, Enumerable.Repeat(3, 7).ToArray()).RosterIndices[6] == 3);
        var away = new SourceTeamSelection(30, Enumerable.Range(0, 7).ToArray());
        var profiles = Enumerable.Range(0, 16).Select(i => new SourceRosterProfile(
            new SourceRosterIdentity(0, i), "synthetic_roster:" + i, 0x12, 1.2f, "Synthetic", "Fixture")).ToArray();
        var roster = new SourceTeamRoster(0, "synthetic_roster", profiles);
        profiles[0] = new SourceRosterProfile(new SourceRosterIdentity(0, 0), "replacement:0", 0x34, 1.4f);
        Check("SourceTeamRoster.Players detached", roster.Players[0].SourcePlayerId == "synthetic_roster:0");
        Check("packed jersey/source scale retained",
            roster.Players[0].PackedJerseyNumber == 0x12 && roster.Players[0].ModelScale == 1.2f);
        Throws<NotSupportedException>("SourceTeamRoster.Players read-only",
            () => ((IList<SourceRosterProfile>)roster.Players)[0] = profiles[0]);
        Throws<ArgumentException>("fifteen records rejected",
            () => new SourceTeamRoster(0, "synthetic_roster", profiles.Take(15).ToArray()));
        Throws<ArgumentException>("shuffled static indices rejected",
            () => new SourceTeamRoster(0, "synthetic_roster", profiles.Reverse().ToArray()));
        var duplicates = Enumerable.Range(0, 16).Select(i => new SourceRosterProfile(
            new SourceRosterIdentity(0, i), "duplicate", 0x12, 1.2f)).ToArray();
        Throws<ArgumentException>("duplicate source player keys rejected",
            () => new SourceTeamRoster(0, "synthetic_roster", duplicates));

        var stationChoices = new[]
        {
            new SourceStationConfiguration(3, TeamId.Away, 11, new SourceRosterIdentity(30, 4)),
            new SourceStationConfiguration(0, TeamId.Home, 3, new SourceRosterIdentity(0, 3)),
            new SourceStationConfiguration(2, TeamId.Away, 10),
            new SourceStationConfiguration(1, TeamId.Home, 4)
        };
        var cfg = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.FourStation, home, away, stationChoices, TeamId.Away,
            SourceCaptainSlot.First, homePlay: selected);
        stationChoices[0] = new SourceStationConfiguration(1, TeamId.Home);
        Check("SourceMatchConfiguration.Stations detached/order retained",
            cfg.Stations[0].StationId == 3 && cfg.Stations[0].Profile.RosterIndex == 4);
        Check("FirstPossession/Captain are explicit",
            cfg.FirstPossession == TeamId.Away && cfg.Captain == SourceCaptainSlot.First);
        Check("profile/adjustment/difficulty/play choices retained",
            cfg.Profile == SourceSimulationProfile.OriginalRelease57Hz && cfg.TimeAdjustment == 2 &&
            cfg.Difficulty == 5 && cfg.HomePlay.PoolIndex == 5);
        Throws<NotSupportedException>("SourceMatchConfiguration.Stations read-only",
            () => ((IList<SourceStationConfiguration>)cfg.Stations)[0] = stationChoices[0]);
        var sparse = new[]
        {
            new SourceStationConfiguration(0, TeamId.Home), new SourceStationConfiguration(3, TeamId.Away)
        };
        var two = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
            SourceStationLayout.TwoStation, home, away, sparse, TeamId.Home, SourceCaptainSlot.Second);
        Check("two-station mode does not assume source ID/team remapping",
            two.Stations[1].StationId == 3 && two.Stations[1].Team == TeamId.Away);
        Throws<ArgumentException>("three humans in two-station layout rejected",
            () => new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.TwoStation, home, away, cfg.Stations.Take(3).ToArray(),
                TeamId.Home, SourceCaptainSlot.Second));
        Throws<ArgumentException>("duplicate configured stations rejected",
            () => new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.FourStation, home, away, new[] { cfg.Stations[0], cfg.Stations[0] },
                TeamId.Home, SourceCaptainSlot.Second));
        Throws<ArgumentException>("wrong source-team profile rejected",
            () => new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.FourStation, home, away,
                new[] { new SourceStationConfiguration(0, TeamId.Home, 3, new SourceRosterIdentity(30, 3)) },
                TeamId.Home, SourceCaptainSlot.Second));

        var typedCommand = new SourceCommand(SourceAction.SelectPlay, selected, 4,
            SourcePatOption.TwoPointPlay, SourceKickoffType.Onside, 2);
        var inputs = new[]
        {
            new SourceStationInput(3, 3, -3, SourceButtons.A | SourceButtons.B | SourceButtons.C,
                SourceButtons.A | SourceButtons.C, SourceButtons.B, typedCommand),
            new SourceStationInput(0, -3, 3),
            new SourceStationInput(2),
            new SourceStationInput(1, 1, -1)
        };
        var frame = new SourceInputFrame(inputs);
        inputs[0] = new SourceStationInput(0);
        Check("SourceInputFrame.Stations detached with four-station order", frame.Stations.Count == 4 &&
            frame.Stations[0].StationId == 3 && frame.Stations[1].StationId == 0 &&
            frame.Stations[2].StationId == 2 && frame.Stations[3].StationId == 1);
        Check("49-way coordinates and simultaneous A/B/C edge states retained",
            frame.Stations[0].StickX == 3 && frame.Stations[0].StickZ == -3 &&
            (int)frame.Stations[0].Held == 7 && (int)frame.Stations[0].Pressed == 5 &&
            (int)frame.Stations[0].Released == 2);
        Throws<NotSupportedException>("SourceInputFrame.Stations read-only",
            () => ((IList<SourceStationInput>)frame.Stations)[0] = inputs[0]);
        Throws<ArgumentException>("duplicate input stations rejected",
            () => new SourceInputFrame(new[] { new SourceStationInput(0), new SourceStationInput(0) }));
        Throws<ArgumentException>("default input station rejected",
            () => new SourceInputFrame(new[] { default(SourceStationInput) }));
        Throws<ArgumentException>("five input stations rejected",
            () => new SourceInputFrame(Enumerable.Repeat(new SourceStationInput(0), 5).ToArray()));
        Check("empty source input frame accepted",
            new SourceInputFrame(new SourceStationInput[0]).Stations.Count == 0);
        var command = new PlayerCommand(source: typedCommand, stationInputs: frame);
        Check("PlayerCommand carries original play/PAT/kick/audible/stations",
            command.Source.Play.PoolIndex == 5 && command.Source.PatChoice == SourcePatOption.TwoPointPlay &&
            command.Source.KickoffChoice.Value == SourceKickoffType.Onside && command.Source.AudibleSlot == 2 &&
            command.StationInputs.Stations.Count == 4);
        var legacyCommand = new PlayerCommand(new FieldVector(1f, 0f), true, InputAction.Pass, PlayId.QuickPass, 4);
        Check("legacy PlayerCommand constructor/default extensions preserved", legacyCommand.Move.X == 1f &&
            legacyCommand.Turbo && legacyCommand.Action == InputAction.Pass && legacyCommand.Play == PlayId.QuickPass &&
            legacyCommand.TargetPlayerId == 4 && !legacyCommand.Source.IsSpecified && legacyCommand.StationInputs == null);

        var freePat = new SourceMatchResult(SourcePlayKind.ExtraPoint, SourceResultKind.ExtraPoint, homePoints: 1);
        Check("free PAT is typed without fabricated classifier call", freePat.IsSpecified &&
            freePat.Kind == SourceResultKind.ExtraPoint && freePat.HomePoints == 1 && !freePat.Status.HasValue);
        var noOp = new SourceMatchResult(SourcePlayKind.Punt, SourceResultKind.MissedKick,
            SourceResultStatus.IntentionalNoOp);
        Check("intentional punt no-op preserves retained result/status",
            noOp.Status.Value == SourceResultStatus.IntentionalNoOp && noOp.Kind == SourceResultKind.MissedKick);
        var invalid = new SourceMatchResult(SourcePlayKind.FieldGoal, SourceResultKind.Down,
            SourceResultStatus.InvalidSourceBranch);
        Check("invalid classifier branch remains visible", invalid.Status.Value == SourceResultStatus.InvalidSourceBranch);
        var scores = new[] { new SourceQuarterScore(0, 6, 0), new SourceQuarterScore(1, 1, 2) };
        var stationViews = new[] { new SourceStationView(3, 11, 0.25f), new SourceStationView(0, 4, 0.5f) };
        var possession = new SourcePossessionView(TeamId.Home, TeamId.Away, TeamId.Away,
            TeamId.Away, SourceCaptainSlot.First);
        var period = new SourcePeriodView(1, SourcePeriodStatus.DeferredForExtraPoint, 0, true);
        var view = new SourceMatchView(cfg, SourcePlayKind.ExtraPoint, SourceMatchPhase.FreePointResult,
            period, possession, scores, stationViews, result: freePat, homePlay: selected, selectionCountdown: 5,
            patChoice: SourcePatOption.FreeKick, patStatus: SourcePatStatus.FreeKickComplete);
        scores[1] = new SourceQuarterScore(1, 99, 99);
        stationViews[0] = new SourceStationView(3, 12, 1f);
        Check("SourceMatchView.QuarterScores detached per-period deltas",
            view.QuarterScores[1].HomeScore == 1 && view.QuarterScores[1].AwayScore == 2);
        Check("SourceMatchView.Stations detached",
            view.Stations[0].ControlledPlayerId == 11 && view.Stations[0].Turbo01 == 0.25f);
        Check("PAT/period/possession do not collapse into legacy outcome",
            view.PatChoice == SourcePatOption.FreeKick && view.PatStatus == SourcePatStatus.FreeKickComplete &&
            view.Period.Status == SourcePeriodStatus.DeferredForExtraPoint &&
            view.Possession.InitialOffense == TeamId.Away && view.Result.Kind == SourceResultKind.ExtraPoint);
        Throws<NotSupportedException>("SourceMatchView.QuarterScores read-only",
            () => ((IList<SourceQuarterScore>)view.QuarterScores)[0] = scores[0]);
        Throws<NotSupportedException>("SourceMatchView.Stations read-only",
            () => ((IList<SourceStationView>)view.Stations)[0] = stationViews[0]);
        var kickoff = new SourceMatchView(cfg, SourcePlayKind.Kickoff, SourceMatchPhase.PreSnap,
            new SourcePeriodView(4, SourcePeriodStatus.OvertimeKickoff), possession,
            new SourceQuarterScore[0], new SourceStationView[0], kickoff: SourceKickoffType.Quick,
            kickingTeam: TeamId.Away, receivingTeam: TeamId.Home);
        Check("quick kick keeps explicit kicker/receiver independent from offense/OT",
            kickoff.Kickoff.Value == SourceKickoffType.Quick && kickoff.KickingTeam.Value == TeamId.Away &&
            kickoff.ReceivingTeam.Value == TeamId.Home && kickoff.Possession.Offense == TeamId.Home &&
            kickoff.Period.QuarterIndex == 4);
        Func<IReadOnlyList<SourceQuarterScore>, IReadOnlyList<SourceStationView>, SourceMatchView> makeView =
            (ss, vs) => new SourceMatchView(cfg, SourcePlayKind.Normal, SourceMatchPhase.PlayResult,
                period, possession, ss, vs);
        Throws<ArgumentException>("duplicate quarter score rejected", () => makeView(
            new[] { new SourceQuarterScore(1, 0, 0), new SourceQuarterScore(1, 0, 0) }, new SourceStationView[0]));
        Throws<ArgumentException>("shuffled quarter score rejected", () => makeView(
            new[] { new SourceQuarterScore(1, 0, 0), new SourceQuarterScore(0, 0, 0) }, new SourceStationView[0]));
        Throws<ArgumentException>("default quarter score rejected",
            () => makeView(new[] { default(SourceQuarterScore) }, new SourceStationView[0]));
        Throws<ArgumentException>("duplicate station views rejected", () => makeView(new SourceQuarterScore[0],
            new[] { new SourceStationView(0, 3, 1f), new SourceStationView(0, 4, 1f) }));
        Throws<ArgumentException>("station view cannot control other team's actor",
            () => makeView(new SourceQuarterScore[0], new[] { new SourceStationView(0, 10, 1f) }));
        Throws<ArgumentException>("unconfigured station view rejected",
            () => new SourceMatchView(two, SourcePlayKind.Normal, SourceMatchPhase.PreSnap, period,
                possession, new SourceQuarterScore[0], new[] { new SourceStationView(1, 3, 1f) }));

        var control = new SourceBallControlView(0, 4, TeamId.Away, 17, false);
        Check("controller ownership independent from last_pos/credit duration",
            control.ControllerStationId == 0 && control.ControllerPlayerId == 4 &&
            control.LastPossession.Value == TeamId.Away && control.PossessionTicks == 17 &&
            !control.PossessionCreditEligible);
        var credited = new SourceBallControlView(0, 4, TeamId.Home, 18, true);
        Check("credit can change without delaying controller ownership",
            credited.ControllerPlayerId == control.ControllerPlayerId &&
            credited.PossessionCreditEligible && credited.PossessionTicks == 18);
        Check("DTO does not infer credit from time alone",
            !new SourceBallControlView(-1, -1, TeamId.Home, 30, false).PossessionCreditEligible);
        var actor = new PlayerView(4, TeamId.Home, new FieldVector(20f, 0f), new FieldVector(0f, 0f),
            0.5f, source: new SourceActorView(0, new SourceRosterIdentity(0, 4), 30));
        var ball = new BallView(BallMode.Held, new FieldVector(20f, 0f), 0f, 4, -1, control: control);
        Check("actor and ball expose source control/profile/no-catch fields",
            actor.Source.ControllerStationId == 0 && actor.Source.Roster.RosterIndex == 4 &&
            actor.Source.NoCatchSourceTicks == 30 && ball.Control.ControllerPlayerId == 4);
        var legacyActor = new PlayerView(3, TeamId.Home, new FieldVector(20f, 0f), new FieldVector(0f, 0f), 1f);
        var legacyBall = new BallView(BallMode.Held, new FieldVector(20f, 0f), 0f, 3, -1);
        Check("legacy actor/ball defaults preserved", !legacyActor.Source.IsSpecified &&
            legacyActor.ModelScale == 1f && !legacyBall.Control.IsSpecified &&
            legacyBall.Control.ControllerPlayerId == -1);
        var deflected = new SourceGameEvent(2L, 1L, SourceEventType.PassDeflected, TeamId.Away, 13, 4, 3,
            new FieldVector(50f, 2f));
        Check("deflection event carries actual tick/actor/target/station", deflected.IsSpecified &&
            deflected.SourceTick == 1L && deflected.ActorPlayerId == 13 && deflected.TargetPlayerId == 4 &&
            deflected.StationId == 3);
        var patEvent = new SourceGameEvent(3L, 2L, SourceEventType.FreePointResult, TeamId.Home, value: 1,
            result: freePat, patChoice: SourcePatOption.FreeKick);
        Check("free-point event carries actual award and choice",
            patEvent.Value == 1 && patEvent.Result.HomePoints == 1 && patEvent.PatChoice.Value == SourcePatOption.FreeKick);
        var legacyEvent = new GameEvent(1L, GameEventType.PassThrown, TeamId.Home, 4);
        Check("legacy PassThrown receiver identity stays unchanged", legacyEvent.PlayerId == 4 &&
            legacyEvent.ActorPlayerId == -1 && legacyEvent.TargetPlayerId == -1 && legacyEvent.SourceTick == -1L);

        var clip = new MapClip(new[] { 1, 3, 5 });
        var sample = SourceAnimationSample.FromSelectedOrdinals(clip, 1, 2, 0.25f);
        Check("selected ordinals 1/2 map to authoring indices 3/5",
            sample.SourceFrame == 3 && sample.NextSourceFrame == 5 && sample.Interpolation01 == 0.25f);
        var heldSample = SourceAnimationSample.FromSelectedOrdinals(clip, 2, 2);
        Check("held selected ordinal uses equal authoring indices",
            heldSample.SourceFrame == 5 && heldSample.NextSourceFrame == 5);
        Throws<ArgumentOutOfRangeException>("authoring index not silently treated as selected ordinal",
            () => SourceAnimationSample.FromSelectedOrdinals(clip, 5, 5));
        foreach (float f in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Throws<ArgumentOutOfRangeException>("non-finite station turbo rejected",
                () => new SourceStationView(0, 3, f));
            Throws<ArgumentOutOfRangeException>("non-finite model scale rejected",
                () => new SourceRosterProfile(new SourceRosterIdentity(0, 0), "synthetic", 0, f));
            Throws<ArgumentOutOfRangeException>("non-finite source event X rejected",
                () => new SourceGameEvent(1L, 1L, SourceEventType.Kicked, position: new FieldVector(f, 0f)));
            Throws<ArgumentOutOfRangeException>("non-finite source event Z rejected",
                () => new SourceGameEvent(1L, 1L, SourceEventType.Kicked, position: new FieldVector(0f, f)));
            Throws<ArgumentOutOfRangeException>("non-finite ordinal blend rejected",
                () => SourceAnimationSample.FromSelectedOrdinals(clip, 0, 0, f));
        }
        foreach (int team in new[] { -1, 31 })
            Throws<ArgumentOutOfRangeException>("team bounds rejected", () => new SourceRosterIdentity(team, 0));
        foreach (int record in new[] { -1, 16 })
            Throws<ArgumentOutOfRangeException>("record bounds rejected", () => new SourceRosterIdentity(0, record));
        foreach (int station in new[] { -1, 4 })
            Throws<ArgumentOutOfRangeException>("station bounds rejected", () => new SourceStationInput(station));
        foreach (int player in new[] { -2, 14 })
            Throws<ArgumentOutOfRangeException>("controller actor bounds rejected",
                () => new SourceBallControlView(-1, player));
        foreach (int quarter in new[] { -1, 7 })
            Throws<ArgumentOutOfRangeException>("quarter bounds rejected", () => new SourcePeriodView(quarter));
        foreach (int stick in new[] { -4, 4 })
        {
            Throws<ArgumentOutOfRangeException>("49-way X bounds rejected", () => new SourceStationInput(0, stick, 0));
            Throws<ArgumentOutOfRangeException>("49-way Z bounds rejected", () => new SourceStationInput(0, 0, stick));
        }
        Throws<ArgumentOutOfRangeException>("unknown source button bits rejected",
            () => new SourceStationInput(0, held: (SourceButtons)8));
        Throws<ArgumentOutOfRangeException>("unknown source action rejected", () => new SourceCommand((SourceAction)21));
        Throws<ArgumentOutOfRangeException>("negative possession time rejected",
            () => new SourceBallControlView(-1, -1, possessionTicks: -1));
        Throws<ArgumentOutOfRangeException>("negative no-catch time rejected",
            () => new SourceActorView(-1, noCatchSourceTicks: -1));
        Throws<ArgumentOutOfRangeException>("negative period score rejected", () => new SourceQuarterScore(0, -1, 0));
        Throws<ArgumentOutOfRangeException>("negative source event clock rejected",
            () => new SourceGameEvent(0L, -1L, SourceEventType.Kicked));
        Throws<ArgumentException>("initial actor must belong to station team",
            () => new SourceStationConfiguration(0, TeamId.Home, 10));
        Throws<ArgumentOutOfRangeException>("unknown requested profile rejected",
            () => new SourceMatchConfiguration((SourceSimulationProfile)2, SourceStationLayout.FourStation,
                home, away, new SourceStationConfiguration[0], TeamId.Home, SourceCaptainSlot.Second));
        Throws<ArgumentOutOfRangeException>("invalid time adjustment rejected",
            () => new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.FourStation, home, away, new SourceStationConfiguration[0],
                TeamId.Home, SourceCaptainSlot.Second, timeAdjustment: 5));
        Throws<ArgumentOutOfRangeException>("invalid source difficulty rejected",
            () => new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.FourStation, home, away, new SourceStationConfiguration[0],
                TeamId.Home, SourceCaptainSlot.Second, difficulty: 11));
        Check("captain is source teammate offset 0/1, not a team",
            (int)SourceCaptainSlot.First == 0 && (int)SourceCaptainSlot.Second == 1);
        var kickInput = new SourceKickoffInput(new SourceRulesState(0, 0, 1, 40, 70),
            0, 0x00020000, true, false, 0, (int)SourceCaptainSlot.Second);
        Check("captain slot casts to the existing pure flow int unchanged", kickInput.Captain == 1);
        Throws<ArgumentOutOfRangeException>("invalid captain slot in configuration rejected",
            () => new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.FourStation, home, away, new SourceStationConfiguration[0],
                TeamId.Home, (SourceCaptainSlot)2));
        Throws<ArgumentOutOfRangeException>("invalid captain slot in possession rejected",
            () => new SourcePossessionView(TeamId.Home, TeamId.Home, TeamId.Home, TeamId.Home, (SourceCaptainSlot)2));
        foreach (SourceAction action in new[] { SourceAction.Jump, SourceAction.Swat, SourceAction.Catch, SourceAction.StiffArm })
            Check("source action " + action + " retained without legacy projection", new SourceCommand(action).Action == action);
    }

    internal static void RunAdditionalGuards()
    {
        var clip = new MapClip(new[] { 1, 3, 5 });
        Throws<ArgumentNullException>("ordinal conversion requires actual clip",
            () => SourceAnimationSample.FromSelectedOrdinals(null, 0, 0));
        Throws<ArgumentException>("ordinal conversion requires named clip",
            () => SourceAnimationSample.FromSelectedOrdinals(new MapClip(new[] { 1 }, " "), 0, 0));
        Throws<ArgumentException>("ordinal conversion requires selected map",
            () => SourceAnimationSample.FromSelectedOrdinals(new MapClip(null), 0, 0));
        Throws<ArgumentException>("ordinal conversion rejects empty selected map",
            () => SourceAnimationSample.FromSelectedOrdinals(new MapClip(new int[0]), 0, 0));
        Throws<ArgumentException>("ordinal conversion rejects zero authoring count",
            () => SourceAnimationSample.FromSelectedOrdinals(new MapClip(new[] { 0 }, count: 0), 0, 0));
        Throws<ArgumentException>("ordinal conversion rejects negative authoring count",
            () => SourceAnimationSample.FromSelectedOrdinals(new MapClip(new[] { 0 }, count: -1), 0, 0));
        Throws<ArgumentOutOfRangeException>("negative current ordinal rejected",
            () => SourceAnimationSample.FromSelectedOrdinals(clip, -1, 0));
        Throws<ArgumentOutOfRangeException>("current ordinal equal to selected count rejected",
            () => SourceAnimationSample.FromSelectedOrdinals(clip, 3, 0));
        Throws<ArgumentOutOfRangeException>("negative next ordinal rejected",
            () => SourceAnimationSample.FromSelectedOrdinals(clip, 0, -1));
        Throws<ArgumentOutOfRangeException>("next ordinal equal to selected count rejected",
            () => SourceAnimationSample.FromSelectedOrdinals(clip, 0, 3));
        Throws<ArgumentOutOfRangeException>("negative mapped current authoring index rejected",
            () => SourceAnimationSample.FromSelectedOrdinals(new MapClip(new[] { -1, 3 }), 0, 1));
        Throws<ArgumentOutOfRangeException>("mapped current authoring index at count rejected",
            () => SourceAnimationSample.FromSelectedOrdinals(new MapClip(new[] { 6, 3 }), 0, 1));
        Throws<ArgumentOutOfRangeException>("negative mapped next authoring index rejected",
            () => SourceAnimationSample.FromSelectedOrdinals(new MapClip(new[] { 3, -1 }), 0, 1));
        Throws<ArgumentOutOfRangeException>("mapped next authoring index at count rejected",
            () => SourceAnimationSample.FromSelectedOrdinals(new MapClip(new[] { 3, 6 }), 0, 1));
        Throws<ArgumentOutOfRangeException>("negative ordinal blend rejected",
            () => SourceAnimationSample.FromSelectedOrdinals(clip, 0, 0, -0.01f));
        Throws<ArgumentOutOfRangeException>("ordinal blend above one rejected",
            () => SourceAnimationSample.FromSelectedOrdinals(clip, 0, 0, 1.01f));
        var end = SourceAnimationSample.FromSelectedOrdinals(clip, 1, 2, 1f);
        Check("inclusive ordinal blend endpoint retains sequence and authoring indices",
            end.SequenceName == "SYNTHETIC" && end.SourceFrame == 3 && end.NextSourceFrame == 5 && end.Interpolation01 == 1f);
        var period = default(SourcePeriodView);
        Check("default period invents no period zero",
            !period.IsSpecified && period.QuarterIndex == -1 && !period.CompletedQuarterIndex.HasValue);
        var score = default(SourceQuarterScore);
        Check("default quarter score invents no period zero", !score.IsSpecified && score.QuarterIndex == -1);
        var input = default(SourceStationInput);
        Check("default station input invents no station zero", !input.IsSpecified && input.StationId == -1 &&
            input.Held == SourceButtons.None && input.Pressed == SourceButtons.None && input.Released == SourceButtons.None);
        var station = default(SourceStationView);
        Check("default station view invents no station or actor zero",
            !station.IsSpecified && station.StationId == -1 && station.ControlledPlayerId == -1);
        var configuration = default(SourceStationConfiguration);
        Check("default station configuration invents no station or actor zero",
            !configuration.IsSpecified && configuration.StationId == -1 && configuration.InitialPlayerId == -1);
        var immediate = new SourceBallControlView(3, 13, TeamId.Away, 0, false);
        Check("DTO retains immediate controller even at zero possession ticks",
            immediate.ControllerStationId == 3 && immediate.ControllerPlayerId == 13 &&
            immediate.PossessionTicks == 0 && !immediate.PossessionCreditEligible);
    }

    // These compile the shared interfaces; they do not prove clearing or wrist geometry behavior.
    private static void ClearTypedMatchInput(ISourceMatchSimulation simulation)
    {
        ((IPendingSourceInput)simulation).ClearPendingInput();
    }

    private static SourceVector3 TypedRightWrist(Blitz.Contracts.ISourceActorGeometry geometry, PlayerView actor)
    {
        return geometry.WorldJoint(actor, 4);
    }

    private sealed class MapClip : ISourceAnimationClip
    {
        public string SequenceName { get; private set; }
        public string SourceRevision { get { return "synthetic"; } }
        public string SourcePath { get { return "synthetic"; } }
        public string SourceGitBlobSha1 { get { return "synthetic"; } }
        public string SourceSha256 { get { return "synthetic"; } }
        public string DecodedSha256 { get { return "synthetic"; } }
        public uint SourceFlags { get { return 0; } }
        public int SourceFrameCount { get; private set; }
        public IReadOnlyList<int> SelectedSourceFrames { get; private set; }

        internal MapClip(IReadOnlyList<int> selected, string name = "SYNTHETIC", int count = 6)
        {
            SelectedSourceFrames = selected;
            SequenceName = name;
            SourceFrameCount = count;
        }

        public SourceAnimationFrame GetFrame(int sourceFrame)
        {
            throw new InvalidOperationException("Ordinal conversion must not sample/advance.");
        }
    }
}
