using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public sealed partial class GameSimulation
    {
        private void InitializeSource()
        {
            if (sourceMovementData == null || sourceMovementSettings == null)
                throw new InvalidOperationException("Original source M1 requires actual runtime-safe SourceMovementRuntimeData "
                    + "and explicit field/powerup SourceMovementSettings. No tuned legacy movement fallback is used.");
            if (!(sourceGeometry is ISourceActorCameraGeometry))
                throw new InvalidOperationException("Original M1 requires real fifteen-bone camera-cache geometry, not WorldJoint substitution.");
            if (sourceGeometry == null)
                throw new InvalidOperationException("The source profile requires Contracts.ISourceActorGeometry backed "
                    + "by the actual original pose library and imported reference pivots.");
            if (sourceRosters == null)
                throw new InvalidOperationException("The source profile requires the original ISourceTeamRoster "
                    + "for actor-slot/static-record identities and actual model scales; no uniform scale fallback is used.");
            Model.SourceMode = true;
            Model.SourceKickMeterMetrics = sourceKickMeterMetrics;
            InitializeSourceSoundState();
            BeginSourceProcess("game_proc", -1, 0);
            InitializeSourceC2();
            Model.SourceConfiguration = sourceConfiguration;
            Model.SourcePresentation = sourcePresentation;
            Model.SourceMovementData = sourceMovementData;
            Model.SourceMovementSettings = sourceMovementSettings;
            Model.SourceRosters = sourceRosters;
            Model.SourcePlaybook = sourcePlaybook;
            Model.SourceHandicapSettings = sourceHandicapSettings;
            Model.SourceNoAssist = sourceHandicapSettings.NoAssist;
            Model.SourceSuperDrones = sourceHandicapSettings.SuperDrones;
            ResetSourceTeamFire(0);
            ResetSourceTeamFire(1);
            Model.SourceCStandardRandom = bound => SourceDraw(bound, "player.c:plyr_rand",
                SourceRandomKind.CStandardRand) % bound;
            Model.SourceFirstPossession = (int)sourceConfiguration.FirstPossession;
            Model.SourceCaptain = sourceConfiguration.Captain;
            Model.SourceHomePlay = sourceConfiguration.HomePlay;
            Model.SourceAwayPlay = sourceConfiguration.AwayPlay;
            Model.SourcePeriodView = new SourcePeriodView(0);
            Model.TargetPlayerId = 0;
            Model.ThrowerId = 0;
            Model.SourceMatchPhase = SourceMatchPhase.PreSnap;
            Model.SourceClock = SourceClock.Create();
            int firstSide = (int)sourceConfiguration.FirstPossession;
            Model.SourceRules = new SourceRulesState(firstSide, firstSide, 1, 30, 100, SourcePlayType.Kickoff);
            for (int id = 0; id < Model.Players.Length; id++)
            {
                SourceTeamSelection team = id < 7 ? sourceConfiguration.HomeTeam : sourceConfiguration.AwayTeam;
                Model.Players[id].SourceRoster = new SourceRosterIdentity(team.TeamIndex, team.RosterIndices[id % 7]);
            }
            InitializeSourceFire();
            PrepareSourceKickoff(true, false);
            UpdateSourceClockView();
        }

        private PlayerCommand SourceGameProc(PlayerCommand command)
        {
            BeginSourceProcess("game_proc", -1, 0);
            CommitSourceC2PlayEnd();
            if (SourceStationInputTransform != null)
            {
                SourceInputFrame transformed = SourceStationInputTransform(new GameSnapshot(Model),
                    Model.SourceTick, command.StationInputs);
                if (transformed == null)
                    throw new InvalidOperationException("The executing source station transform returned no input frame.");
                command = new PlayerCommand(command.Move, command.Turbo, command.Action, command.Play,
                    command.TargetPlayerId, command.Source, transformed);
            }
            Model.SourceCurrentInput = command;
            if (command.Source.OffenseFlipped.HasValue && Model.Phase == GamePhase.PlaySelect)
                Model.SourceOffenseFlipped = command.Source.OffenseFlipped.Value;
            SourceAction action = SourceActionFor(command);
            switch (Model.Phase)
            {
                case GamePhase.PlaySelect:
                    SourceFireSelectionEntry();
                    if (action == SourceAction.SelectPat && Model.SourceRules.PlayType == SourcePlayType.ExtraPoint)
                        ApplySourcePat(DecideSourcePat((SourcePatChoice)command.Source.PatChoice));
                    if (action == SourceAction.SelectPlay)
                        SelectSourcePlay(command.Play, command.Source.Play);
                    ApplySourceStationMenuCommands(command);
                    break;
                case GamePhase.PreSnap:
                    if (Model.SourceRules.PlayType == SourcePlayType.Kickoff)
                    {
                        if (Model.PreSnapTicksRemaining-- < 1)
                        {
                            Model.PreSnapTicksRemaining = 0;
                            if (Model.SourceKickoffKind == SourceKickoffKind.Quick)
                                BeginSourceKick();
                            else
                                Model.SourceKickApproach = true;
                        }
                    }
                    break;
                case GamePhase.LiveBall:
                    Model.LiveTicks++;
                    break;
                case GamePhase.DeadBall:
                    if (action == SourceAction.SelectKickoff && command.Source.KickoffChoice.HasValue)
                        Model.SourceRequestedOnside = command.Source.KickoffChoice == SourceKickoffType.Onside;
                    if (action == SourceAction.Continue)
                        ContinueSource();
                    ApplySourceStationMenuCommands(command);
                    break;
                case GamePhase.GameOver:
                    break;
                default:
                    throw new NotSupportedException("Unported source game phase " + Model.Phase
                        + " at sourceTick=" + Model.SourceTick);
            }

            return command;
        }

        private void SourceStep(PlayerCommand command)
        {
            RequireSourceSoundVmBinding();
            Model.SourceDispatchOrdinal = 0;
            Model.SourcePresentationOrder = 0;
            if (SourceFireEnabled) command = DispatchSourceFireLevelZero(command);
            else
            {
                DispatchSourceC2Whistles();
                command = SourceGameProc(command);
            }
            StepSourceKickMeter();
            // Verified playsel.c 848-854/1530 suspends gameplay processes while
            // the selection process runs, then resumes them after selection.
            if (Model.Phase == GamePhase.PlaySelect)
            {
                CaptureSourceFireFrame();
                return;
            }

            UpdateSourceStationMovementResources(command);
            // id.h RL_CAMBOT=0x100 precedes RL_PLAYER-1 and all actors.
            if (SourceCameraHeadingProvider != null)
            {
                BeginSourceProcess("cambot_proc", -1, 0x100);
                SourceCameraStageOrder = Model.SourcePresentationOrder++;
                executingSourceCamera = true;
                int heading;
                try { heading = SourceCameraHeadingProvider(new GameSnapshot(Model), Model.SourceTick); }
                finally { executingSourceCamera = false; }
                SetSourceCameraHeading(heading, Model.SourceTick);
            }
            Model.SourceCameraHeading = pendingSourceCameraHeading;
            Model.SourceCameraHeadingSampleTick = pendingSourceCameraSampleTick;
            if (SourceCameraHeadingProvider == null || Model.SourceMovementCameraTick != Model.SourceTick)
                throw new InvalidOperationException("M1 requires actual cambot position/xform/resolution from the executing source camera provider.");

            SourceClockStep clock = SourceClock.Step(Model.SourceClock,
                new SourceClockInput(Model.SourceTick, Model.ClockActive, Model.Quarter - 1,
                    sourceConfiguration.TimeAdjustment));
            Model.SourceClock = clock.State;
            UpdateSourceClockView();
            foreach (ActorState actor in Model.Players) actor.SourceRawFlags &= ~0x40;
            CacheSourceGeometry();
            Model.SourceAiFrame = BuildSourceAiFrame();
            if (Model.Phase == GamePhase.LiveBall)
            {
                Model.SourceAiFrame = BuildSourceAiFrame();
                Model.SourceBlocking = SourceDroneAI.AssignBlockers(Model.SourceAiFrame);
                foreach (int blocker in Model.SourceBlocking.Blockers)
                {
                    int target = Model.SourceBlocking.TargetsByActor[blocker];
                    Model.Players[blocker].SourceTarget = target;
                    Model.Players[blocker].SourceJob = SourceActorJob.Block;
                    Model.SourceDrones[blocker] = Model.SourceDrones[blocker].WithTarget(target);
                }
                Model.SourceAiFrame = BuildSourceAiFrame();
                Model.SourceReceivers = SourceDroneAI.UpdateReceiverData(Model.SourceAiFrame);
            }
            UpdateSourceMovementPlayers(command);
            CacheSourcePlayerCameraJoints();
            // Active last_player_proc clears HICOUNT for all fourteen actors.
            foreach (ActorState actor in Model.Players) actor.SourceRawFlags &= ~0x04;
            if (Model.SourceKickApproach)
            {
                int kicker = Model.SourceKickingSide * SliceRules.PlayersPerTeam;
                if (PlayerSimulation.DistanceSquared(Model.Players[kicker].Position, Model.BallPosition)
                    * SourceTiming.SourceUnitsPerYard * SourceTiming.SourceUnitsPerYard < 64f)
                    BeginSourceKick();
            }
            // id.h:234-235; detect.c's wake is not conditional on a carrier,
            // live play, human A, Down, or MODE_UNINT.
            BeginSourceProcess("detect_proc", -1, 0x500);
            SourceContacts();
            BeginSourceProcess("ball_proc", -1, 0x600);
            if (Model.Phase == GamePhase.LiveBall)
            {
                AdvanceSourceBall();
                if (Model.BallMode == BallMode.Held && (Model.SourceBallFlags & SourceBallFlags.Phantom) == 0)
                    SynchronizeSourceHeldBall();
            }
            else if (Model.CarrierId >= 0 && (Model.SourceBallFlags & SourceBallFlags.Phantom) == 0)
                SynchronizeSourceHeldBall();
            else AdvanceSourceBall();
            CaptureSourceFireFrame();
        }

        private void SelectSourcePlay(PlayId play, SourcePlaySelection original = default(SourcePlaySelection),
            TeamId? requestingTeam = null)
        {
            TeamId selectingSide = requestingTeam ?? (original.IsSpecified
                ? original.Playbook == SourcePlaybookKind.Offense ? Model.Possession : GameplayRules.Opponent(Model.Possession)
                : sourceConfiguration.Stations.Count == 0 ? TeamId.Home : sourceConfiguration.Stations[0].Team);
            bool selectingOffense = selectingSide == Model.Possession;
            if (!original.IsSpecified && (selectingOffense ? play != PlayId.Run && play != PlayId.QuickPass
                : play != PlayId.Cover && play != PlayId.Blitz))
                return;
            if (original.IsSpecified && (original.Playbook == SourcePlaybookKind.Offense) != selectingOffense)
                throw new ArgumentException("The selected original playbook does not belong to the submitting source side.");
            RequireSourcePlaybook();
            BeginSourcePlay();
            if (Model.SourceRules.PlayType == SourcePlayType.ExtraPoint && Model.SourcePatStatus != SourcePatStatus.RunTwoPointPlay)
            {
                SourcePatChoice choice = !SourceContactDriver.IsHumanTeam(Model, Model.Possession)
                    ? SourcePatChoice.Unselected : original.IsSpecified ? SourcePatChoice.TwoPointPlay
                    : play == PlayId.Run ? SourcePatChoice.FreeKick : SourcePatChoice.TwoPointPlay;
                SourcePatDecision pat = DecideSourcePat(choice);
                if (ApplySourcePat(pat)) return;
                if (pat.Disposition != SourcePatDisposition.RunTwoPointPlay)
                    throw new InvalidOperationException("Unresolved source PAT disposition " + pat.Disposition);
            }
            if (selectingSide == TeamId.Home) Model.HomePlay = play;
            else Model.AwayPlay = play;
            SourcePlaySelection selected = original.IsSpecified ? original : new SourcePlaySelection(
                selectingOffense ? play == PlayId.Run ? "play_zigzag" : "play_spider"
                    : play == PlayId.Blitz ? "play_suicide_blitz" : "play_safe_cover",
                selectingOffense ? SourcePlaybookKind.Offense : SourcePlaybookKind.Defense);
            SourcePlayDefinition chosen = sourcePlaybook.GetPlay(selected.PlaySymbol);
            StoreSourceSelection(selectingSide, selected);
            TeamId other = GameplayRules.Opponent(selectingSide);
            if (!SourceContactDriver.IsHumanTeam(Model, other))
                StoreSourceSelection(other, ChooseSourceCpuPlay(other, chosen));
            else if (!Model.SourcePlaySelectionReady[(int)other])
            {
                SourcePlaySelection configured = other == TeamId.Home ? sourceConfiguration.HomePlay : sourceConfiguration.AwayPlay;
                if (configured.IsSpecified && (configured.Playbook == SourcePlaybookKind.Offense) == (other == Model.Possession))
                    StoreSourceSelection(other, configured);
            }
            if (!Model.SourcePlaySelectionReady[0] || !Model.SourcePlaySelectionReady[1])
                return;
            SourceFireSelectionExit();
            Model.SourceOffensePlay = sourcePlaybook.GetPlay(Model.Possession == TeamId.Home
                ? Model.SourceHomePlay.PlaySymbol : Model.SourceAwayPlay.PlaySymbol);
            Model.SourceDefensePlay = sourcePlaybook.GetPlay(Model.Possession == TeamId.Home
                ? Model.SourceAwayPlay.PlaySymbol : Model.SourceHomePlay.PlaySymbol);
            SourcePlayType type = (Model.SourceOffensePlay.Flags & SourcePlayFlags.Punt) != 0 ? SourcePlayType.Punt
                : (Model.SourceOffensePlay.Flags & SourcePlayFlags.FieldGoal) != 0 ? SourcePlayType.FieldGoal
                : Model.SourceRules.PlayType == SourcePlayType.ExtraPoint ? SourcePlayType.ExtraPoint : SourcePlayType.Normal;
            if (type != Model.SourceRules.PlayType)
                SetSourceRules(new SourceRulesState(Model.SourceRules.OffenseSide, Model.SourceRules.InitialOffenseSide,
                    Model.SourceRules.Down, Model.SourceRules.LineOfScrimmage, Model.SourceRules.FirstDown, type,
                    Model.SourceRules.NoFirstDown));
            Model.SourcePendingTackleAttacker = Model.SourcePendingTackleVictim = -1;
            Model.Phase = GamePhase.PreSnap;
            LineUpSourceActors();
            Model.PreSnapTicksRemaining = SourceClock.DefaultAutoHikeTicks;
            Model.Phase = GamePhase.PreSnap;
            Model.SourceMatchPhase = SourceMatchPhase.PreSnap;
            Emit(GameEventType.PlaySelected, selectingSide, -1, 0, Model.BallPosition);
        }

        private void StoreSourceSelection(TeamId side, SourcePlaySelection selection)
        {
            if (side == TeamId.Home) Model.SourceHomePlay = selection;
            else Model.SourceAwayPlay = selection;
            Model.SourcePlaySelectionReady[(int)side] = true;
            sourceStepEvents.Add(new SourceGameEvent(Model.Tick, Model.SourceTick, SourceEventType.PlaySelected,
                side, selection: selection, playKind: (SourcePlayKind)Model.SourceRules.PlayType));
        }

        private void ApplySourceStationMenuCommands(PlayerCommand command)
        {
            if (command.StationInputs == null) return;
            foreach (SourceStationInput input in command.StationInputs.Stations)
            {
                if (!input.Command.IsSpecified) continue;
                TeamId? team = null;
                foreach (SourceStationConfiguration station in sourceConfiguration.Stations)
                    if (station.StationId == input.StationId) { team = station.Team; break; }
                if (!team.HasValue)
                    throw new InvalidOperationException("Source input names unconfigured station " + input.StationId + ".");
                SourceCommand source = input.Command;
                if (Model.Phase == GamePhase.PlaySelect)
                {
                    if (source.OffenseFlipped.HasValue)
                    {
                        if (team.Value != Model.Possession)
                            throw new InvalidOperationException("Only the offensive side can submit original off_flip.");
                        Model.SourceOffenseFlipped = source.OffenseFlipped.Value;
                    }
                    if (source.Action == SourceAction.SelectPlay)
                        SelectSourcePlay(command.Play, source.Play, team);
                    else if (source.Action == SourceAction.SelectPat && team.Value == Model.Possession
                        && Model.SourceRules.PlayType == SourcePlayType.ExtraPoint)
                        ApplySourcePat(DecideSourcePat((SourcePatChoice)source.PatChoice));
                }
                else if (Model.Phase == GamePhase.DeadBall)
                {
                    if (source.Action == SourceAction.SelectKickoff && source.KickoffChoice.HasValue)
                        Model.SourceRequestedOnside = source.KickoffChoice.Value == SourceKickoffType.Onside;
                    else if (source.Action == SourceAction.Continue) ContinueSource();
                }
            }
        }

        private void SnapSource(bool automatic)
        {
            if (Model.Phase != GamePhase.PreSnap || (Model.SourceFlags & SourceGameFlags.HikingBall) != 0)
                return;
            if (!automatic && SourceContactDriver.IsHumanTeam(Model, Model.Possession)
                && (Model.SourceOffensePlay.Flags & (SourcePlayFlags.FieldGoal | SourcePlayFlags.Punt)) != 0
                && sourceKickMeterMetrics == null)
                throw new NotSupportedException("Manual source kick/punt meter needs plyrinfo.c k_bar/k_marker widths, "
                    + "p_meter height and TA_SPTEAMS meter scheduling. No guessed meter value is substituted. "
                    + "The original auto-hike branch remains executable.");
            if (!automatic && sourceKickMeterMetrics != null
                && SourceContactDriver.IsHumanTeam(Model, Model.Possession)
                && (Model.SourceOffensePlay.Flags & (SourcePlayFlags.FieldGoal | SourcePlayFlags.Punt)) != 0
                && (Model.SourceKickMeter == null || Model.SourceKickMeter.View.Owner != Model.Possession
                    || Model.SourceKickMeter.View.Status != SourceKickMeterStatus.Tracking))
                throw new InvalidOperationException("The actual offensive portable-authored kick meter is not tracking at snap.");
            Model.SourceHikeCounterAtSnap = automatic ? 0 : Model.PreSnapTicksRemaining;
            if (automatic && sourceKickMeterMetrics == null && SourceContactDriver.IsHumanTeam(Model, Model.Possession)
                && (Model.SourceOffensePlay.Flags & SourcePlayFlags.FieldGoal) != 0)
            {
                int percent = 55 + SourceDrawFor("plyrinfo.c:kick_meter_proc:auto_hike_percent")(35);
                int sign = (SourceDrawFor("plyrinfo.c:kick_meter_proc:auto_hike_sign")(2) << 1) - 1;
                Model.SourceKickMeterPercent = percent * sign;
            }
            Model.PreSnapTicksRemaining = 0;
            Model.PlayNumber++;
            Model.LiveTicks = 0;
            Model.SourceSnapTick = Model.SourceTick;
            Model.ForwardPassUsed = false;
            Model.SourceDisallowPassTicks = automatic ? 30 : 0;
            Model.PassTicksElapsed = 0;
            Model.SourceBallDeflected = false;
            Model.SourceBallFlags &= ~(SourceBallFlags.Bounce | SourceBallFlags.Bobble | SourceBallFlags.Deflected
                | SourceBallFlags.FieldGoalBounce | SourceBallFlags.HitPost | SourceBallFlags.Phantom);
            Model.PassTicksTotal = 0;
            Model.LastOutcome = PlayOutcome.None;
            Model.SnapLineOfScrimmage = Model.LineOfScrimmage;
            Model.CatchSourceTick = Model.SourceTick - 18;
            Model.ClockActive = Model.SourceRules.PlayType != SourcePlayType.ExtraPoint;
            Model.SourceClock = SourceClock.ResetPlayTime(Model.SourceClock);
            Model.SourceFlags |= SourceGameFlags.HikingBall;
            Model.SourceBallBounces = 0;
            SourcePlayerDriver.Snap(Model, SourceDraw);
            if (sourceGeometry != null)
                SynchronizeSourceHeldBall();
        }

        private void PrepareSourceKickoff(bool first, bool half)
        {
            RequireSourcePlaybook();
            SourceFireSelectionExit();
            SourcePlayStart start = SourceMatchFlow.BeginPlay(Model.SourceRules, Model.Quarter - 1,
                Model.SourceClock.PackedTime, first);
            if (!Model.SourceBeginPlayInitialized)
                Model.SourceOffensivePlays[start.OffensivePlayCounterSide]++;
            SetSourceRules(start.State);
            bool onside = Model.SourceRequestedOnside;
            if (!first && !SourceContactDriver.IsHumanTeam(Model, Model.Possession))
            {
                int? surprise = null, suppress = null;
                SourceRandomDecision decision;
                do
                {
                    decision = SourceMatchFlow.CpuOnside(new SourceCpuOnsideInput(Model.Quarter - 1,
                        Model.SourceClock.PackedTime,
                        Model.Possession == TeamId.Home ? Model.HomeScore : Model.AwayScore,
                        Model.Possession == TeamId.Home ? Model.AwayScore : Model.HomeScore, surprise, suppress));
                    if (!decision.RandomRequest.IsRequired) break;
                    SourceRandomRequest request = decision.RandomRequest;
                    int raw = SourceDraw(request.BoundOrModulus, "SourceMatchFlow." + request.Call, request.Kind);
                    if (request.CallIndex == 0) surprise = raw;
                    else suppress = raw;
                } while (!decision.IsResolved);
                onside = decision.Value;
            }
            SourceKickoffPlan plan = SourceMatchFlow.PrepareKickoff(new SourceKickoffInput(
                Model.SourceRules, Model.Quarter - 1, Model.SourceClock.PackedTime, first, half,
                Model.SourceFirstPossession, (int)Model.SourceCaptain, onside));
            Model.SourceCaptain = (SourceCaptainSlot)plan.Captain;
            Model.SourceRequestedOnside = false;
            Model.SourceKickingSide = plan.KickingSide;
            Model.SourceKickoffKind = plan.Kind;
            Model.SourceFlags = plan.Flags;
            Model.SourceLastPossession = plan.LastPossession;
            Model.SourceFumblesOnPlay = 0;
            Model.SourceBeginPlayInitialized = true;
            Model.SourceKickApproach = false;
            Model.SourceFirstKickoff = false;
            Model.SourceHalfKickoff = false;
            Model.SourceBallFlags = plan.Kind == SourceKickoffKind.Quick ? SourceBallFlags.Freeze : SourceBallFlags.None;
            Model.ClockActive = false;
            Model.SourcePendingTackleAttacker = Model.SourcePendingTackleVictim = -1;
            SetSourceRules(plan.State);
            if (plan.Kind == SourceKickoffKind.Quick)
            {
                Model.SourceOffensePlay = sourcePlaybook.GetPlay("play_kick_return_short");
                Model.SourceDefensePlay = sourcePlaybook.GetPlay("play_kickoff_short");
            }
            else
            {
                Model.SourceOffensePlay = sourcePlaybook.GetPlay(plan.Kind == SourceKickoffKind.Onside
                    ? "play_onside_313" : plan.KickingSide == 1 ? "play_kickoff2_313" : "play_kickoff_313");
                Model.SourceDefensePlay = sourcePlaybook.GetPlay(plan.Kind == SourceKickoffKind.Onside
                    ? "play_onside_kick_return" : "play_kick_return");
            }
            Model.Phase = GamePhase.PreSnap;
            Model.BallKind = SourceBallKind.Kickoff;
            LineUpSourceActors();
            FireControl(SourceFireOperation.DeleteObjectGroup, "nfl.c", "game_proc", 1109, SourceFireIds.PuffObject);
            FireControl(SourceFireOperation.KillProcessGroup, "nfl.c", "game_proc", 1110, SourceFireIds.Spfx);
            Model.PreSnapTicksRemaining = plan.AutoHikeTicks;
            Model.Phase = GamePhase.PreSnap;
            Model.SourceMatchPhase = SourceMatchPhase.PreSnap;
            Model.BallKind = SourceBallKind.Kickoff;
            Model.CarrierId = -1;
            Model.BallMode = BallMode.Dead;
            Model.ThrowerId = Model.SourceKickingSide * SliceRules.PlayersPerTeam;
            Model.SourceQuickReturnActor = (1 - Model.SourceKickingSide) * SliceRules.PlayersPerTeam + 3;
            Model.BallPosition = new FieldVector(Model.LineOfScrimmage, 0f);
            Model.BallHeight = 0f;
            Model.SourceBallVelocity = default(FieldVector);
            Model.SourceBallVerticalVelocity = 0f;
            if (plan.Kind == SourceKickoffKind.Quick)
            {
                int level = Model.SourceEffectiveTeamHandicap[plan.ReceivingSide];
                int minimum = (int)sourcePlaybook.HandicapValue("min_ko_catch_ht", level);
                int maximum = (int)sourcePlaybook.HandicapValue("max_ko_catch_ht", level);
                int catchYard = minimum + SourceDraw(maximum - minimum + 1);
                float catchX = SourceMatchRules.WorldPosition(catchYard, plan.ReceivingSide);
                float catchZ = SourceDraw((int)(40f * SourceTiming.SourceUnitsPerYard))
                    - 20f * SourceTiming.SourceUnitsPerYard;
                float vx = (catchX + (plan.ReceivingSide == 1 ? -20f : 20f)
                    * SourceTiming.SourceUnitsPerYard) / 150f;
                float vz = catchZ / 150f;
                Model.PassTarget = new FieldVector(SourceMatchRules.AbsoluteYardsFromSourceWorld(catchX),
                    catchZ / SourceTiming.SourceUnitsPerYard);
                SourcePlayerDriver.SetSourcePosition(Model.Players[Model.SourceQuickReturnActor], new SourceVector3(catchX, 0f, catchZ));
                Model.Players[Model.SourceQuickReturnActor].SnapPosition = Model.PassTarget;
                Model.BallPosition = new FieldVector(
                    SourceMatchRules.AbsoluteYardsFromSourceWorld(catchX - 40f * vx),
                    (catchZ - 40f * vz) / SourceTiming.SourceUnitsPerYard);
                Model.BallHeight = 176f / SourceTiming.SourceUnitsPerYard;
                Model.SourceBallVelocity = new FieldVector(vx, vz);
                Model.SourceBallVerticalVelocity = -2.8f;
                Model.SourceBallPitch = 256;
                SourceBallDriver.SetPosition(Model, new SourceVector3(catchX - 40f * vx, 176f, catchZ - 40f * vz));
                SourceBallDriver.SetTarget(Model, catchX, catchZ);
            }
            else
            {
                Model.PassTarget = Model.BallPosition;
            }
            UpdateSourceBallVelocityView();
            if (plan.Kind != SourceKickoffKind.Quick)
            {
                SourceBallDriver.SetPosition(Model, SourceBallDriver.SourcePosition(Model.BallPosition, Model.BallHeight));
                SourceBallDriver.SetTarget(Model, SourceMatchRules.SourceWorldFromAbsoluteYards(Model.PassTarget.X),
                    Model.PassTarget.Z * SourceTiming.SourceUnitsPerYard);
            }
            Model.SourceBallBounces = 0;
            Model.SourceBallBounceTick = 0;
        }

        private void BeginSourceKick()
        {
            bool quick = Model.SourceKickoffKind == SourceKickoffKind.Quick;
            int kicker = Model.SourceKickingSide * SliceRules.PlayersPerTeam;
            Model.SourceKickApproach = false;
            Model.SourceBallFrozen = false;
            Model.SourceFlags |= SourceGameFlags.BallKicked;
            Model.BallMode = BallMode.InFlight;
            Model.Phase = GamePhase.LiveBall;
            Model.SourceMatchPhase = SourceMatchPhase.LiveBall;
            Model.PlayNumber++;
            Model.LiveTicks = 0;
            Model.PassTicksElapsed = 0;
            Model.ForwardPassUsed = false;
            Model.LastOutcome = PlayOutcome.None;
            Model.SnapLineOfScrimmage = Model.LineOfScrimmage;
            if (!quick)
            {
                int direction = Model.SourceKickingSide == 0 ? 1 : -1;
                if (Model.SourceKickoffKind == SourceKickoffKind.Onside)
                {
                    float x = (25 + SourceDrawFor("player.c:_mode_kicker:onside_x")(9)) / 10f;
                    float y = 1.2f + SourceDrawFor("player.c:_mode_kicker:onside_y")(5) / 10f;
                    float z = (SourceDrawFor("player.c:_mode_kicker:onside_z")(21) - 10) / 10f;
                    Model.SourceBallVelocity = new FieldVector(direction * x, z);
                    Model.SourceBallVerticalVelocity = y;
                }
                else
                {
                    Model.SourceBallVelocity = new FieldVector(direction * 2.83f, 0f);
                    Model.SourceBallVerticalVelocity = 7f;
                }
                ActorState actor = Model.Players[kicker];
                actor.Action = ActorAction.Kick;
                actor.AnimationState.FullBody("kickball_anim", actor, Model.SourceTick, SourceDraw,
                    AnimationSelectionEvidence.SourceVerified);
            }
            Model.Players[kicker].SourceMode = SourceActorMode.Receiver;
            UpdateSourceBallVelocityView();
            Model.SourceBallPitchRate = -45;
            Model.SourceBallFlags &= ~(SourceBallFlags.VelocityPitch | SourceBallFlags.FieldGoalBounce | SourceBallFlags.HitPost);
            if (!quick)
            {
                float time = -2f * Model.SourceBallVerticalVelocity / SourceBallDriver.Gravity;
                SourceBallDriver.SetTarget(Model, Model.SourceBallPosition.X + time * Model.SourceBallVelocity.X,
                    Model.SourceBallPosition.Z + time * Model.SourceBallVelocity.Z);
            }
            Emit(GameEventType.Snapped, (TeamId)Model.SourceKickingSide, kicker, 0, Model.BallPosition,
                kicker, Model.TargetPlayerId);
            EmitSource(SourceEventType.Kicked, kicker, Model.TargetPlayerId);
        }

        private void RequestSourcePass(int targetId, bool turbo = false)
        {
            int quarterbackId = Model.CarrierId;
            if (quarterbackId < 0)
                return;
            if (Model.BallMode != BallMode.Held || Model.CarrierId != quarterbackId)
                return;
            if (Model.SourceRules.PlayType == SourcePlayType.Kickoff || Model.SourceDisallowPassTicks > 0)
                return;
            ActorState quarterback = Model.Players[quarterbackId];
            string lowerSequence = quarterback.AnimationState.Primary.SequenceName;
            bool upperBusy = (quarterback.AnimationState.Secondary.Mode & SourceAnimationModes.Uninterruptible) != 0;
            bool lowerThrow = !upperBusy && (lowerSequence == "HURDLE" || lowerSequence == "SPIN" || lowerSequence == "QB_STIFF");
            if (quarterback.Role != ActorRole.Quarterback || (quarterback.AnimationState.Busy && !lowerThrow) || quarterback.PendingThrowTarget >= 0
                || (Model.SourceFlags & SourceGameFlags.BallCrossedLine) != 0)
                return;
            if (targetId == -1 && quarterback.SourceControllerStation >= 0)
                targetId = SourcePassDriver.PickReceiver(Model, quarterback,
                    SourcePlayerDriver.ActorStick(quarterback,
                        SourcePlayerDriver.ActorCommand(Model, quarterback, Model.SourceCurrentInput)));
            if (targetId < Model.OffenseOffset + 3 || targetId > Model.OffenseOffset + 6 || targetId == quarterbackId)
                return;
            ActorState target = Model.Players[targetId];
            SourceVector3 from = SourcePlayerDriver.WorldPosition(quarterback), to = SourcePlayerDriver.WorldPosition(target);
            float distance = Model.SourceDistances[quarterbackId, targetId] / SourceTiming.SourceUnitsPerYard;
            int fromField = SourceMatchRules.FieldPosition(from.X, (int)Model.Possession);
            int toField = SourceMatchRules.FieldPosition(to.X, (int)Model.Possession);
            if (distance < 7f && fromField > toField)
            {
                BeginSourceLateral(quarterback, target);
                return;
            }
            quarterback.PendingThrowTarget = targetId;
            quarterback.SourceThrowing = true;
            quarterback.PendingThrowTurbo = turbo;
            quarterback.Action = ActorAction.Throw;
            int targetHeading = Model.SourceAngles[quarterbackId, targetId];
            int bearing = Math.Abs(((targetHeading - quarterback.Facing + 512) & 1023) - 512);
            if (quarterback.AnimationState.Primary.IsUninterruptible && lowerThrow)
            {
                quarterback.AnimationState.BeginTwoPart(quarterback);
                quarterback.TargetFacing = quarterback.TargetUpperFacing = targetHeading;
                quarterback.AnimationState.UpperBody(quarterback.Flipped ? "qb_rthrow_sl_anim" : "qb_rthrow_sr_anim",
                    quarterback, Model.SourceTick, SourceDraw, AnimationSelectionEvidence.SourceVerified);
                return;
            }
            if (distance < 15f && fromField <= toField
                && bearing <= 320)
            {
                quarterback.TargetUpperFacing = targetHeading;
                quarterback.SourceUpperTurnRate = bearing / 4;
                quarterback.AnimationState.UpperBody(quarterback.Flipped ? "qb_rthrow_sl_anim" : "qb_rthrow_sr_anim",
                    quarterback, Model.SourceTick, SourceDraw, AnimationSelectionEvidence.SourceVerified);
                return;
            }
            quarterback.TargetFacing = targetHeading;
            quarterback.SourceTurnRate = bearing / 4;
            string script = distance >= 35f ? quarterback.Flipped ? "qb_throw_ll_anim" : "qb_throw_lr_anim"
                : quarterback.Flipped ? "qb_throw_sl_anim" : "qb_throw_sr_anim";
            quarterback.AnimationState.FullBody(script, quarterback, Model.SourceTick, SourceDraw,
                AnimationSelectionEvidence.SourceVerified);
        }

        private void ReleaseSourcePass(int quarterbackId)
        {
            ActorState quarterback = Model.Players[quarterbackId];
            int targetId = quarterback.PendingThrowTarget;
            if (Model.Phase != GamePhase.LiveBall || Model.CarrierId != quarterbackId || targetId < 0)
                return;
            ActorState receiver = Model.Players[targetId];
            SourceVector3 wrist = SourceWorldJoint(quarterback, quarterback.Flipped ? 7 : 4);
            SourcePassDriver.Pass(Model, quarterback, receiver, wrist, SourceDrawFor("player.c:set_ball_vels"));
            Model.BallMode = BallMode.InFlight;
            Model.BallKind = SourceBallKind.Pass;
            Model.SourceBallFlags &= ~(SourceBallFlags.FieldGoalBounce | SourceBallFlags.HitPost);
            Model.SourceBallFlags |= SourceBallFlags.VelocityPitch;
            if (quarterback.PendingThrowTurbo) Model.SourceBallFlags |= SourceBallFlags.TurboPass;
            else Model.SourceBallFlags &= ~SourceBallFlags.TurboPass;
            if (Model.SourceTeamFire[(int)quarterback.Team]) Model.SourceBallFlags |= SourceBallFlags.FirePass;
            Model.ThrowerId = quarterbackId;
            Model.LastTouchPlayerId = quarterbackId;
            Model.CarrierId = -1;
            Model.TargetPlayerId = targetId;
            Model.ForwardPassUsed = true;
            quarterback.Role = ActorRole.Receiver;
            quarterback.SourceMode = SourceActorMode.Receiver;
            quarterback.SourceThrowing = false;
            if (receiver.SourcePendingCatch != null)
                Model.SourceDrones[targetId] = Model.SourceDrones[targetId].WithCatch(receiver.SourcePendingCatch,
                    receiver.SourceCatchLaunchTime);
            Model.SourceFlags |= SourceGameFlags.PassThrown;
            Model.SourceFumbleX = SourcePlayerDriver.WorldPosition(quarterback).X;
            UpdateSourceBallVelocityView();
            Emit(GameEventType.PassThrown, quarterback.Team, targetId, 0, Model.BallPosition,
                quarterbackId, targetId);
            EmitSource(SourceEventType.PassReleased, quarterbackId, targetId);
        }

        private void TrySourceCatch(ActorState actor)
        {
            if (Model.Phase == GamePhase.LiveBall && actor.Role == ActorRole.Quarterback
                && (Model.SourceFlags & SourceGameFlags.HikingBall) != 0 && Model.CarrierId == -1)
            {
                SourceVector3 hikeBody = SourcePlayerDriver.WorldPosition(actor);
                float dx = hikeBody.X - Model.SourceBallPosition.X, dz = hikeBody.Z - Model.SourceBallPosition.Z;
                if ((float)Math.Sqrt(dx * dx + dz * dz) < 8f && Model.SourceBallPosition.Y >= hikeBody.Y
                    && Model.SourceBallPosition.Y < hikeBody.Y + actor.SourceRoot.Y + 12f)
                {
                    Model.SourceFlags &= ~SourceGameFlags.HikingBall;
                    Model.CarrierId = Model.SourceLastCarrierId = actor.Id;
                    Model.BallMode = BallMode.Held;
                    EmitSource(SourceEventType.PassCaught, actor.Id, Model.ThrowerId);
                    if ((Model.SourceOffensePlay.Flags & SourcePlayFlags.Punt) != 0)
                    {
                        actor.Action = ActorAction.Punt;
                        actor.SourceMode = SourceActorMode.Receiver;
                        actor.AnimationState.FullBody("punt_ball_anim", actor, Model.SourceTick, SourceDraw,
                            AnimationSelectionEvidence.SourceVerified);
                        actor.ModeChangedTick = Model.SourceTick;
                    }
                    else if ((Model.SourceOffensePlay.Flags & SourcePlayFlags.FieldGoal) != 0)
                    {
                        actor.Action = ActorAction.Catch;
                        actor.AnimationState.FullBody("qbhut_place_ball_anim", actor, Model.SourceTick, SourceDraw,
                            AnimationSelectionEvidence.SourceVerified);
                        actor.ModeChangedTick = Model.SourceTick;
                    }
                }
                return;
            }
            Func<int, int> random = SourceDrawFor("player.c:standard_catch");
            if (!SourceCatchRules.ModeEligible(Model, actor, random))
                return;
            SourceVector3 body = SourcePlayerDriver.WorldPosition(actor);
            SourceVector3 pelvis = new SourceVector3(body.X, body.Y + actor.SourceRoot.Y, body.Z);
            if (!SourceCatchRules.InCatchDistance(Model.SourceBallPosition, pelvis,
                SourceWorldJoint(actor, 7), SourceWorldJoint(actor, 4)))
                return;
            SourceCatchResult result = SourceCatchRules.Evaluate(Model, actor, random);
            if (result != SourceCatchResult.Catch)
            {
                if (result == SourceCatchResult.None)
                    return;
                actor.NoCatchTicks = 20;
                if (result == SourceCatchResult.DownPunt)
                {
                    Model.SourceFlags |= SourceGameFlags.PlayEnding | SourceGameFlags.PossessionChange;
                    Model.SourcePlayEndX = Model.SourceBallPosition.X;
                    EndSourcePlay(SourcePlayEndCause.DeadBallInBounds, actor.Id, Model.BallPosition);
                    return;
                }
                if (result == SourceCatchResult.Deflect)
                {
                    SourceBallDriver.Deflect(Model, actor, SourceDrawFor("pmisc.c:deflect_pass"));
                    EmitSource(SourceEventType.PassDeflected, actor.Id, Model.TargetPlayerId);
                }
                else if (result == SourceCatchResult.Bobble || result == SourceCatchResult.BatDown)
                {
                    if (result == SourceCatchResult.Bobble)
                    {
                        Model.SourceBallFlags &= ~SourceBallFlags.FirePass;
                        TransferSourceCatchControl(actor, actor.Team == Model.Possession, false);
                    }
                    SourceBallDriver.Bobble(Model, actor, SourceWorldJoint(actor, 7), 0f,
                        result == SourceCatchResult.BatDown ? -2f : 0f, 0f, SourceDrawFor("player.c:bobble_pass"));
                    if (result == SourceCatchResult.BatDown
                        && (actor.AnimationState.Primary.SequenceName == "C_JMPINT"
                            || actor.AnimationState.Primary.SequenceName == "C_JMPIN3"))
                        actor.AnimationState.FullBody("air_swat_anim", actor, Model.SourceTick, SourceDraw,
                            AnimationSelectionEvidence.SourceVerified);
                    EmitSource(result == SourceCatchResult.Bobble ? SourceEventType.Bobbled : SourceEventType.PassDeflected,
                        actor.Id);
                }
                else EmitSource(SourceEventType.PassMissed, actor.Id, Model.TargetPlayerId);
                return;
            }
            int thrower = Model.ThrowerId;
            TeamId before = Model.Possession;
            bool offense = actor.Team == before;
            Model.BallMode = BallMode.Held;
            Model.CarrierId = actor.Id;
            Model.SourceLastCarrierId = actor.Id;
            Model.LastTouchPlayerId = actor.Id;
            Model.CatchSourceTick = Model.SourceTick;
            Model.SourceCatchX = SourcePlayerDriver.WorldPosition(actor).X;
            Model.SourceHumanTurbo = Math.Max(50f, Model.SourceHumanTurbo);
            for (int station = 0; station < Model.SourceStationTurbo.Length; station++)
                Model.SourceStationTurbo[station] = Math.Max(50f, Model.SourceStationTurbo[station]);
            Model.SourceFlags &= ~SourceGameFlags.HikingBall;
            TransferSourceCatchControl(actor, offense, true);
            actor.NonTurboTicks = 500;
            if (Model.BallKind == SourceBallKind.Kickoff)
            {
                Model.SourceClock = SourceClock.ResetPlayTime(Model.SourceClock);
                if ((Model.SourceFlags & SourceGameFlags.OnsideKick) == 0)
                {
                    Model.SourceLastPossession = (int)actor.Team;
                    if (Model.SourceLastPossession != Model.SourceRules.InitialOffenseSide
                        || (Model.SourceFlags & SourceGameFlags.QuickKick) != 0)
                        Model.SourceFlags |= SourceGameFlags.PossessionChange;
                }
            }
            if (actor.Team != Model.Possession)
            {
                Model.SourceHumanTurbo = 100f;
                for (int station = 0; station < Model.SourceStationTurbo.Length; station++)
                    Model.SourceStationTurbo[station] = 100f;
                Model.Possession = actor.Team;
                Model.SourceRules = new SourceRulesState((int)actor.Team, Model.SourceRules.InitialOffenseSide,
                    Model.SourceRules.Down, Model.SourceRules.LineOfScrimmage, Model.SourceRules.FirstDown,
                    Model.SourceRules.PlayType, Model.SourceRules.NoFirstDown);
                for (int id = 0; id < Model.Players.Length; id++)
                {
                    Model.Players[id].Role = Model.Players[id].Team == actor.Team
                        ? ActorRole.Receiver : ActorRole.Defender;
                    Model.Players[id].SourceMode = Model.Players[id].Team == actor.Team
                        ? SourceActorMode.Receiver : SourceActorMode.DefensiveBack;
                    Model.SourceDrones[id] = SourceDroneAI.OnPossessionChanged(Model.SourceDrones[id],
                        (int)Model.Players[id].Team, (int)actor.Team, sourcePlaybook);
                }
            }
            Model.ClockActive = Model.SourceRules.PlayType != SourcePlayType.ExtraPoint;
            int field = SourceMatchRules.FieldPosition(Model.SourceCatchX, (int)Model.Possession);
            if (field < 2 && (!offense || Model.BallKind == SourceBallKind.Kickoff))
            {
                Model.SourceFlags |= SourceGameFlags.RecoveredInEndZone;
                Model.SourceFlags &= ~SourceGameFlags.LeftEndZone;
            }
            if (actor.Height > 0f) Model.SourceFlags |= SourceGameFlags.AirCatch;
            else Model.SourceFlags &= ~SourceGameFlags.AirCatch;
            actor.Action = offense ? ActorAction.Catch : ActorAction.Intercept;
            if (offense && field < Model.SourceRules.LineOfScrimmage
                && (int)actor.Team == Model.SourceRules.InitialOffenseSide
                && Model.SourceRules.PlayType != SourcePlayType.Kickoff
                && (Model.SourceFlags & SourceGameFlags.BallCrossedLine) == 0)
                actor.Role = ActorRole.Quarterback;
            if (actor.Role == ActorRole.Quarterback) actor.SourceMode = SourceActorMode.Quarterback;
            if (SourceFireEnabled && offense)
                FireRuntime.SuccessfulOffensiveCatch(FireSite("player.c", "standard_catch", 3604, actor.Id),
                    actor.Id, Model.SourceLastReceiver[(int)actor.Team],
                    Model.SourceConsecutiveCompletions[(int)actor.Team], Model.SourceTeamFire[(int)actor.Team] ? 1 : 0,
                    SourceMatchRules.FieldPosition(SourcePlayerDriver.WorldPosition(actor).X, (int)Model.Possession)
                        >= Model.SourceRules.LineOfScrimmage);
            Model.SourceAiFrame = BuildSourceAiFrame();
            SynchronizeSourceHeldBall();
            Emit(GameEventType.PassCaught, actor.Team, actor.Id, 0, Model.BallPosition, actor.Id, thrower);
            EmitSource(SourceEventType.PassCaught, actor.Id, thrower);
            if (before != Model.Possession)
                Emit(GameEventType.PossessionChanged, Model.Possession, actor.Id, 0, Model.BallPosition,
                    actor.Id, thrower);
        }

        private void DeflectSourcePass(ActorState actor)
        {
            SourceBallDriver.Deflect(Model, actor, SourceDrawFor("pmisc.c:deflect_pass"));
        }

        private void AdvanceSourceBall()
        {
            bool freeOrPhantom = Model.CarrierId < 0 || (Model.SourceBallFlags & SourceBallFlags.Phantom) != 0;
            SourceBallStep step = SourceBallDriver.Advance(Model, SourceDrawFor("player.c:ball_proc"));
            if (step.EndPlay)
            {
                if (step.Cause == SourcePlayEndCause.BallOutOfBounds)
                {
                    Model.SourcePlayEndX = Model.SourceBallPosition.X;
                    Model.SourcePlayEndZ = Model.SourceBallPosition.Z;
                }
                EndSourcePlay(step.Cause, Model.TargetPlayerId, Model.BallPosition, sourceSpot: Model.SourceBallPosition);
            }
            if (SourceFireEnabled && freeOrPhantom)
            {
                SourceBallEffectSelection = FireRuntime.BallFreeOrPhantomStep(FireSite("player.c", "ball_proc", 10311),
                    new SourceFireBallEmitterInput(Model.BallKind,
                        (Model.SourceBallFlags & SourceBallFlags.FirePass) != 0,
                        (Model.SourceBallFlags & SourceBallFlags.TurboPass) != 0,
                        (Model.SourceBallFlags & SourceBallFlags.Bounce) != 0,
                        Model.SourceTeamFire[(int)Model.Possession], Model.PassTicksElapsed));
                if (SourceBallEffectSelection == SourceFireBallEffect.Blink && Model.PassTicksElapsed % 10 == 0)
                    Model.SourceBallFlags |= SourceBallFlags.White;
            }
        }

        private void SynchronizeSourceHeldBall()
        {
            if (Model.CarrierId < 0)
                throw new InvalidOperationException("Source held ball has no carrier at sourceTick=" + Model.SourceTick);
            ActorState actor = Model.Players[Model.CarrierId];
            SourceBallDriver.SetPosition(Model, SourceWorldJoint(actor, 7));
            Model.SourceLastCarrierId = actor.Id;
            Model.BallVelocity = actor.Velocity;
            Model.BallVerticalVelocity = actor.SourceVerticalVelocity
                * SourceTiming.DefaultTicksPerSecond / SourceTiming.SourceUnitsPerYard;
            if (Model.SourceRules.PlayType != SourcePlayType.Kickoff
                && SourceMatchRules.FieldPosition(Model.SourceBallPosition.X, (int)Model.Possession)
                    >= Model.SourceRules.LineOfScrimmage)
                Model.SourceFlags |= SourceGameFlags.BallCrossedLine;
        }

        private bool ResolveSourceLocation()
        {
            if (Model.CarrierId < 0 || Model.Phase != GamePhase.LiveBall)
                return false;
            ActorState carrier = Model.Players[Model.CarrierId];
            float ballDirected = Model.OffenseDirection * Model.SourceBallPosition.X;
            SourceVector3 carrierRoot = SourcePlayerDriver.WorldPosition(carrier);
            float carrierDirected = Model.OffenseDirection * carrierRoot.X;
            bool ballOver = ballDirected >= 390f + SourceTiming.SourceUnitsPerYard && ballDirected <= 468f
                && Math.Abs(Model.SourceBallPosition.Z) <= 182.85f;
            bool carrierOver = carrierDirected >= 390f + SourceTiming.SourceUnitsPerYard && carrierDirected <= 468f
                && Math.Abs(carrierRoot.Z) <= 182.85f;
            if ((ballOver || carrierOver) && (Model.SourceFlags & SourceGameFlags.AirCatch) == 0
                && Model.SourceTick - Model.CatchSourceTick >= 18)
            {
                EndSourcePlay(SourcePlayEndCause.CarrierTouchdown, Model.CarrierId, carrier.Position, sourceSpot: carrierRoot);
                return true;
            }
            if (!SourceBallDriver.InBounds(carrierRoot))
            {
                if (SourceC2Enabled) SourceC2ImmediateWhistle(carrier);
                else SourceDrawFor("player.c:_mode_wpn:whistle")(2);
                SourcePlayEndCause cause = (Model.SourceFlags & SourceGameFlags.AirCatch) != 0
                    || Model.SourceTick - Model.CatchSourceTick < 18 ? SourcePlayEndCause.BallOutOfBounds
                    : carrierDirected < -390f ? SourcePlayEndCause.CarrierDownInOwnEndZone
                    : SourcePlayEndCause.CarrierOutOfBounds;
                Model.SourcePlayEndX = carrierRoot.X;
                Model.SourcePlayEndZ = carrierRoot.Z;
                EndSourcePlay(cause, Model.CarrierId, carrier.Position, sourceSpot: carrierRoot);
                return true;
            }
            return false;
        }

        private void SourcePlayerDown(int playerId)
        {
            if (Model.Phase != GamePhase.LiveBall || Model.CarrierId != playerId)
                return;
            ActorState actor = Model.Players[playerId];
            if (SourceC2Enabled)
            {
                SpawnSourceC2Whistle();
                Model.Phase = GamePhase.DeadBall;
            }
            CreditSourcePossession(actor, false);
            SourceVector3 root = SourcePlayerDriver.WorldPosition(actor);
            SourceVector3 sourceSpot = Model.SourceHasContactSpot ? Model.SourceContactSourcePosition : root;
            if (SourceC2Enabled)
            {
                float originalSpot = Math.Abs(Model.SourcePlayEndX) > 600f
                    ? SourceMatchRules.WorldPosition(-10, (int)Model.Possession) : Model.SourcePlayEndX;
                sourceSpot = new SourceVector3(originalSpot, sourceSpot.Y, sourceSpot.Z);
            }
            int spotField = SourceMatchRules.FieldPosition(sourceSpot.X, (int)Model.Possession);
            int landedField = SourceMatchRules.FieldPosition(root.X, (int)Model.Possession);
            if (spotField < landedField) sourceSpot = root;
            if (spotField >= 100 && landedField < 100)
                sourceSpot = new SourceVector3(SourceMatchRules.WorldPosition(99, (int)Model.Possession), 0f, sourceSpot.Z);
            int final = SourceMatchRules.FieldPosition(sourceSpot.X, (int)Model.Possession);
            Model.SourcePlayEndX = sourceSpot.X;
            Model.SourcePlayEndZ = sourceSpot.Z;
            var spot = new FieldVector(SourceMatchRules.AbsoluteYardsFromSourceWorld(sourceSpot.X),
                sourceSpot.Z / SourceTiming.SourceUnitsPerYard);
            SourcePlayEndCause cause = final >= 100 ? SourcePlayEndCause.CarrierTouchdown
                : final <= 0 ? SourcePlayEndCause.CarrierDownInOwnEndZone
                : SourcePlayEndCause.CarrierTackledInBounds;
            if (SourceC2Enabled)
            {
                QueueSourceC2PlayEnd(cause, playerId, spot, Model.SourcePendingTackleAttacker);
                actor.SourceRawFlags |= 0x100;
                actor.SourceCelebrateTime = 60;
            }
            else EndSourcePlay(cause, playerId, spot, Model.SourcePendingTackleAttacker, sourceSpot);
        }

        private void EndSourcePlay(SourcePlayEndCause cause, int playerId, FieldVector position, int attackerId = -1,
            SourceVector3? sourceSpot = null)
        {
            if (Model.Phase != GamePhase.LiveBall)
                return;
            if (SourceC2Enabled)
            {
                QueueSourceC2PlayEnd(cause, playerId, position, attackerId);
                return;
            }
            ApplySourcePlayEnd(cause, playerId, position, attackerId);
        }

        private void ApplySourcePlayEnd(SourcePlayEndCause cause, int playerId, FieldVector position,
            int attackerId, SourceC2PendingEnd pending = null)
        {
            TeamId offense = pending == null ? Model.Possession : pending.Offense;
            SourcePlayType endingType = pending == null ? Model.SourceRules.PlayType : pending.Input.State.PlayType;
            float displayLos = pending == null ? Model.LineOfScrimmage : pending.DisplayLos;
            float displayFirstDown = pending == null ? Model.FirstDownLine : pending.DisplayFirstDown;
            Model.ClockActive = false;
            Model.SourceClock = SourceClock.DiscardFraction(Model.SourceClock);
            UpdateSourceClockView();
            SourcePlayEndInput endingInput = pending == null ? new SourcePlayEndInput(
                Model.SourceRules, cause, SourceBallTypeFor(Model.BallKind), Model.SourceFlags,
                Model.SourceLastPossession, Model.SourcePlayEndX, Model.SourcePlayEndZ,
                Model.SourceFumbleX, Model.SourceLastResult) : pending.Input;
            SourcePlayResolution resolution = SourceMatchRules.Resolve(endingInput);
            if (!resolution.TransitionApplied)
                throw new NotSupportedException("Invalid source result branch: play=" + Model.SourceRules.PlayType
                    + " cause=" + cause + " kind=" + Model.BallKind + " sourceTick=" + Model.SourceTick);
            ApplySourceFirePlayEnd(endingInput, resolution);
            Model.SourceLastResult = resolution.Classification.Result;
            Model.SourcePlayEndX = resolution.PlayEndX;
            SetSourceRules(resolution.State);
            if (resolution.PreserveDisplayedLines)
            {
                Model.LineOfScrimmage = displayLos;
                Model.FirstDownLine = displayFirstDown;
            }
            Model.SourceLinePosition = resolution.LinePositionZ / SourceTiming.SourceUnitsPerYard;
            Model.SourceFlags = resolution.Flags;
            AddSourcePoints(resolution.Side0Points, resolution.Side1Points);
            Model.SourceResultView = new SourceMatchResult((SourcePlayKind)endingType,
                (SourceResultKind)resolution.Classification.Result, (SourceResultStatus)resolution.Classification.Status,
                (SourcePlayEndKind)cause, resolution.Side0Points, resolution.Side1Points,
                resolution.JustGotFirstDown, resolution.TurnoverOnDowns);
            Model.LastOutcome = resolution.JustGotFirstDown ? PlayOutcome.FirstDown
                : resolution.TurnoverOnDowns ? PlayOutcome.TurnoverOnDowns
                : resolution.Classification.Result == SourcePlayResult.Touchdown ? PlayOutcome.Touchdown
                : resolution.Classification.Result == SourcePlayResult.Safety
                    || resolution.Classification.Result == SourcePlayResult.PatSafety ? PlayOutcome.Safety
                : resolution.Classification.Result == SourcePlayResult.Incomplete ? PlayOutcome.Incomplete
                : cause == SourcePlayEndCause.CarrierOutOfBounds || cause == SourcePlayEndCause.BallOutOfBounds
                    ? PlayOutcome.OutOfBounds
                : cause == SourcePlayEndCause.CarrierTackledInBounds || cause == SourcePlayEndCause.CarrierDownInOwnEndZone
                    ? PlayOutcome.Tackled : PlayOutcome.None;
            if (attackerId >= 0)
                Emit(GameEventType.Tackled, offense, playerId, 0, position, attackerId, playerId);
            else if (cause == SourcePlayEndCause.CarrierOutOfBounds || cause == SourcePlayEndCause.BallOutOfBounds)
                Emit(GameEventType.OutOfBounds, offense, playerId, 0, position, playerId);
            if (resolution.Classification.Result == SourcePlayResult.Incomplete)
                Emit(GameEventType.Incomplete, offense, playerId, 0, position, Model.ThrowerId, playerId);
            if (resolution.JustGotFirstDown)
                Emit(GameEventType.FirstDown, Model.Possession, playerId, 0, position, playerId);
            if (resolution.TurnoverOnDowns)
                Emit(GameEventType.TurnoverOnDowns, Model.Possession, playerId, 0, position, playerId);
            if (resolution.Classification.Result == SourcePlayResult.Touchdown)
                Emit(GameEventType.Touchdown, offense, playerId, 6, position, playerId);
            if (resolution.Classification.Result == SourcePlayResult.Safety)
                Emit(GameEventType.Safety, GameplayRules.Opponent(offense), playerId, 2, position, playerId);
            if (offense != Model.Possession)
                Emit(GameEventType.PossessionChanged, Model.Possession, playerId, 0, position, playerId);
            Model.Phase = GamePhase.DeadBall;
            Model.SourceMatchPhase = SourceMatchPhase.PlayResult;
            Model.SourceBeginPlayInitialized = false;
            Model.ClockActive = false;
            Model.SourceClock = SourceClock.DiscardFraction(Model.SourceClock);
            UpdateSourceClockView();
            Model.BallMode = BallMode.Dead;
            Model.BallPosition = position;
            Model.BallHeight = Math.Max(0f, Model.BallHeight);
            Model.SourcePendingTackleAttacker = Model.SourcePendingTackleVictim = -1;
            for (int id = 0; id < Model.Players.Length; id++)
            {
                Model.Players[id].SourcePlayOver = true;
                Model.Players[id].SourceInPlay = false;
            }
            EmitSource(SourceEventType.PlayResolved, playerId, attackerId, Model.SourceResultView);
            if (Model.SourceClock.PackedTime == 0 && Model.SourceRules.PlayType != SourcePlayType.ExtraPoint
                && !Model.QuarterEndEmitted)
            {
                Model.QuarterEndEmitted = true;
                Emit(GameEventType.QuarterEnded, TeamId.Home, -1, Model.Quarter, default(FieldVector));
            }
        }

        private void ContinueSource()
        {
            // nfl.c:1943-1953 runs after the result box, before the period gate.
            if (Model.SourceForwardCatcher == -1)
                Model.SourceConsecutiveCompletions[(int)Model.Possession] = 0;
            if (AdvanceSourcePeriod())
                return;
            if (Model.SourceRules.PlayType == SourcePlayType.Kickoff)
            {
                PrepareSourceKickoff(false, Model.SourceHalfKickoff);
                return;
            }
            Model.Phase = GamePhase.PlaySelect;
            SourceFireSelectionEntry();
            Model.SourceOffenseFlipped = false;
            Model.SourceMatchPhase = Model.SourceRules.PlayType == SourcePlayType.ExtraPoint
                ? SourceMatchPhase.PatSelection : SourceMatchPhase.PlaySelection;
            Model.PreSnapTicksRemaining = 0;
            BeginSourcePlay();
            Model.SourceSelectionCountdown = SourceMatchFlow.PlaySelectionCountdownStart(Model.Quarter - 1);
            if (Model.SourceRules.PlayType == SourcePlayType.ExtraPoint
                && !SourceContactDriver.IsHumanTeam(Model, Model.Possession))
                ApplySourcePat(DecideSourcePat(SourcePatChoice.Unselected));
        }

        private bool AdvanceSourcePeriod()
        {
            SourcePeriodInput input = new SourcePeriodInput(Model.SourceRules, Model.Quarter - 1,
                Model.SourceClock.PackedTime, Model.HomeScore, Model.AwayScore,
                Model.SourceFirstPossession, Model.SourceOvertimeKickoff,
                continueAfterCabinetGate: sourceConfiguration.ContinueAfterCabinetGate);
            SourcePeriodDecision decision = SourceMatchFlow.AdvancePeriod(input);
            if (decision.RandomRequest.IsRequired)
            {
                decision = SourceMatchFlow.AdvancePeriod(new SourcePeriodInput(Model.SourceRules, Model.Quarter - 1,
                    Model.SourceClock.PackedTime, Model.HomeScore, Model.AwayScore,
                    Model.SourceFirstPossession, Model.SourceOvertimeKickoff,
                    continueAfterCabinetGate: sourceConfiguration.ContinueAfterCabinetGate,
                    overtimeFirstDraw: SourceDraw(decision.RandomRequest.BoundOrModulus,
                        "SourceMatchFlow." + decision.RandomRequest.Call, decision.RandomRequest.Kind)));
            }
            if (decision.MatchEnded)
            {
                Model.Phase = GamePhase.GameOver;
                Model.SourceMatchPhase = SourceMatchPhase.MatchEnded;
                Model.SourcePeriodView = new SourcePeriodView(decision.QuarterIndex,
                    (SourcePeriodStatus)decision.Disposition, decision.CompletedQuarterIndex,
                    decision.CabinetGateRequired);
                Model.ClockActive = false;
                Emit(GameEventType.GameEnded, TeamId.Home, -1, Model.HomeScore.CompareTo(Model.AwayScore),
                    default(FieldVector));
                EmitSource(SourceEventType.MatchEnded);
                return true;
            }
            if (decision.RestartClockProcess)
            {
                ReloadSourceC2Banks(false);
                Model.Quarter = decision.QuarterIndex + 1;
                Model.SourceClock = SourceClock.Create(decision.PackedTime, firstWakeTick: Model.SourceTick + 1);
                Model.SourceOvertimeKickoff = decision.OvertimeKickoff;
                Model.SourceHalfKickoff = decision.HalfKickoff;
                Model.SourcePeriodView = new SourcePeriodView(decision.QuarterIndex,
                    (SourcePeriodStatus)decision.Disposition, decision.CompletedQuarterIndex,
                    decision.CabinetGateRequired);
                EmitSource(SourceEventType.PeriodChanged);
                Model.QuarterEndEmitted = false;
                SetSourceRules(decision.State);
                UpdateSourceClockView();
                if (decision.State.PlayType == SourcePlayType.Kickoff)
                {
                    PrepareSourceKickoff(false, decision.HalfKickoff);
                    return true;
                }
            }
            return false;
        }

        private SourcePatDecision DecideSourcePat(SourcePatChoice choice)
        {
            int offense = Model.Possession == TeamId.Home ? Model.HomeScore : Model.AwayScore;
            int defense = Model.Possession == TeamId.Home ? Model.AwayScore : Model.HomeScore;
            var input = new SourcePatInput(Model.SourceRules, Model.Quarter - 1, Model.SourceClock.PackedTime,
                offense, defense, SourceContactDriver.IsHumanTeam(Model, Model.Possession), choice);
            SourcePatDecision result = SourceMatchFlow.SelectPat(input);
            int? two = null;
            int? free = null;
            for (int i = 0; result.RandomRequest.IsRequired && i < 2; i++)
            {
                int draw = SourceDraw(result.RandomRequest.BoundOrModulus,
                    "SourceMatchFlow." + result.RandomRequest.Call, result.RandomRequest.Kind);
                if (result.RandomRequest.Call == SourceRandomCall.GoForTwo) two = draw;
                else if (result.RandomRequest.Call == SourceRandomCall.FreePat) free = draw;
                else throw new NotSupportedException("Unknown source PAT RNG request " + result.RandomRequest.Call);
                result = SourceMatchFlow.SelectPat(new SourcePatInput(Model.SourceRules, Model.Quarter - 1,
                    Model.SourceClock.PackedTime, offense, defense,
                    SourceContactDriver.IsHumanTeam(Model, Model.Possession), choice, two, free));
            }
            return result;
        }

        private bool ApplySourcePat(SourcePatDecision pat)
        {
            Model.SourcePatChoice = (SourcePatOption)pat.Choice;
            Model.SourcePatStatus = (SourcePatStatus)pat.Disposition;
            sourceStepEvents.Add(new SourceGameEvent(Model.Tick, Model.SourceTick, SourceEventType.PatSelected,
                Model.Possession, playKind: SourcePlayKind.ExtraPoint, patChoice: (SourcePatOption)pat.Choice));
            if (pat.Disposition != SourcePatDisposition.FreeKickComplete
                && pat.Disposition != SourcePatDisposition.SkipUnnecessary)
                return false;
            AddSourcePoints(pat.Side0Points, pat.Side1Points);
            if (pat.Disposition == SourcePatDisposition.FreeKickComplete)
            {
                Model.SourceResultView = new SourceMatchResult(SourcePlayKind.ExtraPoint,
                    pat.KickGood ? SourceResultKind.ExtraPoint : SourceResultKind.MissedKick,
                    homePoints: pat.Side0Points, awayPoints: pat.Side1Points);
                sourceStepEvents.Add(new SourceGameEvent(Model.Tick, Model.SourceTick, SourceEventType.FreePointResult,
                    Model.Possession, result: Model.SourceResultView, playKind: SourcePlayKind.ExtraPoint,
                    patChoice: (SourcePatOption)pat.Choice));
            }
            SetSourceRules(pat.State);
            if (pat.CheckEndOfPeriod && AdvanceSourcePeriod()) return true;
            if (Model.SourceRules.PlayType == SourcePlayType.Kickoff)
                PrepareSourceKickoff(false, Model.SourceHalfKickoff);
            return true;
        }

        private void SetSourceRules(SourceRulesState rules)
        {
            Model.SourceRules = rules;
            Model.Possession = (TeamId)rules.OffenseSide;
            Model.Down = rules.Down;
            Model.LineOfScrimmage = SourceMatchRules.AbsoluteYardsFromSourceWorld(
                SourceMatchRules.WorldPosition(rules.LineOfScrimmage, rules.OffenseSide));
            Model.FirstDownLine = SourceMatchRules.AbsoluteYardsFromSourceWorld(
                SourceMatchRules.WorldPosition(rules.FirstDown, rules.OffenseSide));
        }

        private void UpdateSourceClockView()
        {
            Model.PackedGameTime = unchecked((uint)Model.SourceClock.PackedTime);
            Model.QuarterTicksRemaining = SourceClock.WholeSeconds(Model.SourceClock.PackedTime) * SliceRules.TicksPerSecond;
        }

        private void UpdateSourceBallVelocityView()
        {
            Model.BallVelocity = PlayerSimulation.Scale(Model.SourceBallVelocity,
                SourceTiming.DefaultTicksPerSecond / SourceTiming.SourceUnitsPerYard);
            Model.BallVerticalVelocity = Model.SourceBallVerticalVelocity
                * SourceTiming.DefaultTicksPerSecond / SourceTiming.SourceUnitsPerYard;
        }

        private int SourceDraw(int maximum)
        {
            string identity = Model.SourceRandomActor < 0 ? "player.c:game_proc"
                : "plyrseq.c:" + (Model.Players[Model.SourceRandomActor].AnimationState.Primary.ScriptSymbol ?? "lineup");
            return SourceDraw(maximum, identity);
        }

        private int SourceDraw(int maximum, string identity, SourceRandomKind kind = SourceRandomKind.RandRange)
        {
            SourceRandomDraw draw = DrawSourceRandom(maximum, identity, kind);
            return kind == SourceRandomKind.CStandardRand ? draw.Raw.Value : draw.Value;
        }

        private SourceRandomDraw DrawSourceRandom(int maximum, string identity,
            SourceRandomKind kind = SourceRandomKind.RandRange)
        {
            var site = new SourceRandomSite(kind, maximum, Model.SourceTick, Model.SourceRandomActor,
                Model.SourceRandomProcess, Model.SourceRandomProcessOrder, Model.SourceRandomCallIndex++,
                sourceRandom.Provenance.Profile, identity);
            SourceRandomDraw draw = sourceRandom.Draw(site);
            Model.SourceRandomDraws++;
            return draw;
        }

        private Func<int, int> SourceDrawFor(string identity)
        {
            return maximum => SourceDraw(maximum, identity);
        }

        private void BeginSourceProcess(string process, int actor, int order)
        {
            Model.SourceRandomProcess = process;
            Model.SourceRandomActor = actor;
            Model.SourceRandomProcessOrder = Model.SourceDispatchOrdinal++;
            Model.SourceRandomCallIndex = 0;
        }

        private static SourceMatchConfiguration DefaultSourceConfiguration()
        {
            return new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.TwoStation,
                new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                new[] { new SourceStationConfiguration(0, TeamId.Home) }, TeamId.Home, SourceCaptainSlot.First);
        }

        private static SourceAction SourceActionFor(PlayerCommand command)
        {
            if (command.Source.IsSpecified)
                return command.Source.Action;
            switch (command.Action)
            {
                case InputAction.None: return SourceAction.None;
                case InputAction.SelectPlay: return SourceAction.SelectPlay;
                case InputAction.Snap: return SourceAction.Snap;
                case InputAction.Pass: return SourceAction.Pass;
                case InputAction.Tackle: return SourceAction.Tackle;
                case InputAction.SwitchDefender: return SourceAction.SwitchPlayer;
                case InputAction.Continue: return SourceAction.Continue;
                default: throw new NotSupportedException("Unported source input " + command.Action);
            }
        }

        private void BeginSourcePlay()
        {
            if (Model.SourceBeginPlayInitialized)
                return;
            SourcePlayStart start = SourceMatchFlow.BeginPlay(Model.SourceRules, Model.Quarter - 1,
                Model.SourceClock.PackedTime);
            SetSourceRules(start.State);
            Model.SourceFlags = start.Flags;
            Model.SourceLastPossession = start.LastPossession;
            Model.SourceOffensivePlays[start.OffensivePlayCounterSide]++;
            Model.SourceFumblesOnPlay = 0;
            Model.SourceHasContactSpot = false;
            Model.SourceBeginPlayInitialized = true;
            Model.SourceResultView = default(SourceMatchResult);
            Model.SourcePatChoice = SourcePatOption.Unselected;
            Model.SourcePatStatus = SourcePatStatus.NotExtraPoint;
            Model.SourcePlaySelectionReady[0] = Model.SourcePlaySelectionReady[1] = false;
        }

        private void RequireSourcePlaybook()
        {
            if (sourcePlaybook == null || sourcePlaybook.Directions == null)
                throw new InvalidOperationException("The source profile requires the converted pinned SourcePlaybook "
                    + "with formations, routes, weighted pools and TRIGS.H data. Supply it to GameSimulation; "
                    + "no preview route, formation or RNG fallback is selected.");
        }

        private SourceRandomContext CurrentSourceRandomContext()
        {
            return new SourceRandomContext(Model.SourceTick, Model.SourceRandomActor, Model.SourceRandomProcess,
                Model.SourceRandomProcessOrder, sourceRandom.Provenance.Profile, Model.SourceRandomCallIndex);
        }

        private SourcePlaySelection ChooseSourceCpuPlay(TeamId side, SourcePlayDefinition opponent)
        {
            int?[] draws = new int?[3];
            SourceOpponentPlay opponentKind = (opponent.Flags & SourcePlayFlags.Punt) != 0 ? SourceOpponentPlay.Punt
                : (opponent.Flags & SourcePlayFlags.FieldGoal) != 0 ? SourceOpponentPlay.FieldGoal
                : (opponent.Flags & SourcePlayFlags.Custom) != 0 ? SourceOpponentPlay.Custom : SourceOpponentPlay.Other;
            SourceCpuPlayDecision decision;
            for (;;)
            {
                decision = SourceMatchFlow.CpuPickPlay(new SourceCpuPlayInput(Model.SourceRules, (int)side,
                    Model.Quarter - 1, Model.SourceClock.PackedTime,
                    side == TeamId.Home ? Model.HomeScore : Model.AwayScore,
                    side == TeamId.Home ? Model.AwayScore : Model.HomeScore, opponentPlay: opponentKind,
                    draws: new SourceRandomDraws(draws[0], draws[1], draws[2])));
                if (decision.IsResolved) break;
                SourceRandomRequest request = decision.RandomRequest;
                if (!request.IsRequired || request.CallIndex < 0 || request.CallIndex >= draws.Length
                    || draws[request.CallIndex].HasValue)
                    throw new InvalidOperationException("Source CPU play choice did not advance its explicit draw request.");
                draws[request.CallIndex] = SourceDraw(request.BoundOrModulus,
                    "SourceMatchFlow." + request.Call, request.Kind);
            }
            SourcePlayDefinition selected = sourcePlaybook.ResolveCpuPlay(decision);
            PlayId legacyView = side == Model.Possession ? PlayId.QuickPass
                : (selected.Flags & SourcePlayFlags.Blitz) != 0 ? PlayId.Blitz : PlayId.Cover;
            if (side == TeamId.Home) Model.HomePlay = legacyView;
            else Model.AwayPlay = legacyView;
            return new SourcePlaySelection(selected.Identity,
                side == Model.Possession ? SourcePlaybookKind.Offense : SourcePlaybookKind.Defense,
                pool: decision.PoolIndex < 0 ? (SourcePlayPool?)null : (SourcePlayPool)decision.Family,
                poolIndex: decision.PoolIndex);
        }

        private void LineUpSourceActors()
        {
            RequireSourcePlaybook();
            // nfl.c:1038 precedes both drone initialization passes and lineup.
            UpdateSourceHandicaps();
            Model.SourcePlayEndX = SourceMatchRules.WorldPosition(-100, (int)Model.Possession);
            SourceDroneInitialization before = SourceDroneAI.InitializePreLineup(BuildSourceAiFrame(),
                Model.SourceOffensePlay, Model.SourceDefensePlay, Model.SourceAiParameters, Model.Quarter - 1,
                CurrentSourceRandomContext(), sourceRandom);
            Model.SourceRandomCallIndex += before.RandomCalls;
            Model.SourceRandomDraws += before.RandomCalls;
            for (int i = 0; i < Model.SourceDrones.Length; i++) Model.SourceDrones[i] = before.States[i];
            SourceFormationLayout layout = Model.SourceKickoffKind == SourceKickoffKind.Quick
                && Model.SourceRules.PlayType == SourcePlayType.Kickoff ? LineUpSourceQuickKick()
                : SourcePlaybook.LineUp(Model.SourceOffensePlay, Model.SourceDefensePlay,
                    new SourceLineupInput((int)Model.Possession, Model.SourceRules.LineOfScrimmage,
                        Model.SourceLinePosition * SourceTiming.SourceUnitsPerYard, Model.SourceOffenseFlipped));
            SourcePlayerDriver.LineUp(Model, sourceAnimations, SourceDraw, layout,
                id => BeginSourceProcess("player_lineup", id, id + 1), BindSourceAnimation);
            FireLineupScope(-1);
            FireControl(SourceFireOperation.ResetLineupLatches, "nfl.c", "lineup_players", 3369);
            for (int id = 0; id < Model.Players.Length; id++)
                Model.SourceDrones[id] = Model.SourceDrones[id].WithTarget(layout.Actors[id].CoverageTarget);
            for (int id = 0; id < Model.Players.Length; id++)
            {
                int receiver = layout.Actors[id].CoverageTarget;
                if (Model.Players[id].Team == Model.Possession || receiver < 0) continue;
                SourceCoverageAssignment coverage = SourceDroneAI.AssignCoverage(id, Model.SourceDrones[id],
                    receiver, Model.SourceDrones[receiver],
                    SourceContactDriver.IsHumanTeam(Model, Model.Players[id].Team),
                    Model.SourceLastReceiver[(int)Model.Possession], Model.SourceConsecutiveCompletions[(int)Model.Possession]);
                Model.SourceDrones[id] = coverage.Defender;
                Model.SourceDrones[receiver] = coverage.Receiver;
            }
            ConfigureSourceStations();
            CacheSourceGeometry();
            Model.SourceAiFrame = BuildSourceAiFrame();
            for (int id = 0; id < Model.Players.Length; id++)
            {
                if (Model.Players[id].Team == Model.Possession) continue;
                BeginSourceProcess("init_drones_dbps", id, id + 1);
                SourceRouteStep initial = SourceDroneAI.InitializePreSnapDecision(Model.SourceDrones[id],
                    new SourceRouteInput(Model.SourceAiFrame, id, Model.SourceAiParameters,
                        CurrentSourceRandomContext(), droneStates: Model.SourceDrones,
                        formationSlot: layout.Actors[id].Slot), sourceRandom);
                ApplySourceDroneStep(Model.Players[id], initial);
            }
            BeginSourceProcess("init_drones_postlineup", -1, 15);
            SourceDroneInitialization after = SourceDroneAI.InitializePostLineupRandom(BuildSourceAiFrame(),
                Model.SourceAiParameters, CurrentSourceRandomContext(), sourceRandom);
            Model.SourceRandomCallIndex += after.RandomCalls;
            Model.SourceRandomDraws += after.RandomCalls;
            Model.SourceQuarterbackPlan = after.QuarterbackPlan;
            Model.SourceMotionDirection = after.QuarterbackPlan.MotionDirection;
            for (int id = 0; id < Model.Players.Length; id++)
            {
                Model.Players[id].SourceUnblockable = after.UnblockableByActor[id];
                Model.SourceDrones[id] = Model.SourceDrones[id].WithQuarterbackPlan(after.QuarterbackPlan);
            }
            Array.Clear(Model.SourceSpinCount, 0, Model.SourceSpinCount.Length);
            Array.Clear(Model.SourceHurdleCount, 0, Model.SourceHurdleCount.Length);
            Model.SourceForwardCatcher = -1;
            Model.SourceAiFrame = null;
            if (SourceContactDriver.IsHumanTeam(Model, Model.Possession)
                && (Model.SourceOffensePlay.Flags & (SourcePlayFlags.FieldGoal | SourcePlayFlags.FakeFieldGoal)) != 0)
            {
                BeginSourceProcess("kick_meter_proc", -1, 16);
                float wind = SourceDrawFor("plyrinfo.c:kick_meter_proc:wind")(11) * 0.1f;
                if (wind != 0f)
                {
                    float sign = SourceDrawFor("plyrinfo.c:kick_meter_proc:wind_sign")(2) != 0 ? -1f : 1f;
                    if (sign > 0f) wind = -wind;
                }
                Model.SourceKickWind = wind;
            }
            BeginSourceProcess("punt_meter_proc", -1, 17);
            Model.SourcePuntStrength = 50 + SourceDrawFor("plyrinfo.c:punt_meter_proc:initial_strength")(50);
            InitializeSourceKickMeter();
            BeginSourceProcess("game_proc_after_lineup", -1, 16);
        }

        private SourceFormationLayout LineUpSourceQuickKick()
        {
            // nfl.c 3243+: quick kick swaps off_side before ordinary lineup.
            // Its offense is therefore the return formation, not a normal
            // SourcePlaySide.Offense/Kickoff accepted by the pure playbook API.
            var actors = new SourceFormationActor[14];
            float line = SourceMatchRules.WorldPosition(Model.SourceRules.LineOfScrimmage, (int)Model.Possession);
            float forward = Model.OffenseDirection;
            for (int id = 0; id < actors.Length; id++)
            {
                bool offense = id / 7 == (int)Model.Possession;
                SourceFormationSlot slot = (offense ? Model.SourceOffensePlay : Model.SourceDefensePlay).Formation.Slots[id % 7];
                float x = line + slot.Offset.X * (id < 7 ? 1f : -1f);
                x = Math.Max(-(468f - 7.8f), Math.Min(468f - 7.8f, x));
                float z = Model.SourceLinePosition * SourceTiming.SourceUnitsPerYard + slot.Offset.Z * forward;
                actors[id] = new SourceFormationActor(id, new SourcePlanar(x, z), slot, -1, false);
            }
            return new SourceFormationLayout(actors);
        }

        private void CacheSourceGeometry()
        {
            SourceAiFrame frame = BuildSourceAiFrame(false);
            Model.SourceDistances = new float[14, 14];
            Model.SourceAngles = new int[14, 14];
            for (int i = 0; i < 14; i++)
                for (int j = 0; j < 14; j++)
                {
                    Model.SourceDistances[i, j] = frame.Distance(i, j);
                    Model.SourceAngles[i, j] = frame.Angle(i, j);
                }
        }

        private SourceAiFrame BuildSourceAiFrame(bool useCachedGeometry = true)
        {
            RequireSourcePlaybook();
            var actors = new SourceAiActor[14];
            for (int id = 0; id < actors.Length; id++)
            {
                ActorState actor = Model.Players[id];
                SourceVector3 root = SourcePlayerDriver.WorldPosition(actor);
                uint lower = actor.AnimationState == null ? 0u : actor.AnimationState.Primary.Mode;
                uint upper = actor.AnimationState == null ? 0u : actor.AnimationState.Secondary.Mode;
                SourceAiActorFlags flags = SourceAiActorFlags.None;
                if ((lower & SourceAnimationModes.Uninterruptible) != 0) flags |= SourceAiActorFlags.LowerUninterruptible;
                if ((upper & SourceAnimationModes.Uninterruptible) != 0) flags |= SourceAiActorFlags.UpperUninterruptible;
                if ((lower & SourceAnimationModes.Reacting) != 0) flags |= SourceAiActorFlags.Reacting;
                if ((lower & (uint)SourceMovementModes.Puppet) != 0) flags |= SourceAiActorFlags.Puppet;
                if ((lower & (uint)SourceMovementModes.Puppeteer) != 0) flags |= SourceAiActorFlags.Puppeteer;
                if (actor.SourceReverseRoute) flags |= SourceAiActorFlags.ReverseRoute;
                if (actor.SourceThrowing) flags |= SourceAiActorFlags.Throwing;
                if ((upper & 0x1000u) != 0) flags |= SourceAiActorFlags.RunningPush;
                actors[id] = new SourceAiActor(id,
                    new SourcePlanar(root.X, root.Z),
                    new SourcePlanar(actor.SourceVelocity.X, actor.SourceVelocity.Z), actor.SourceMode,
                    actor.SourceJob, actor.SourceTarget, flags, actor.SourceControllerStation, actor.Facing & 1023,
                    actor.SourceStick, height: root.Y, rawFacing: actor.Facing);
            }
            int humans = 0;
            foreach (SourceStationConfiguration station in sourceConfiguration.Stations) humans |= 1 << (int)station.Team;
            return new SourceAiFrame(actors, sourcePlaybook.Directions, Model.SourceRules,
                new SourceAiBall(SourceBallTypeFor(Model.BallKind),
                    new SourcePlanar(Model.SourceBallPosition.X, Model.SourceBallPosition.Z),
                    new SourcePlanar(Model.SourceBallVelocity.X, Model.SourceBallVelocity.Z),
                    new SourcePlanar(Model.SourceBallTarget.X, Model.SourceBallTarget.Z), Model.CarrierId,
                    Model.TargetPlayerId, Model.ThrowerId, Model.PassTicksElapsed, Model.PassTicksTotal,
                    Model.SourceBallPosition.Y, Model.SourceBallVerticalVelocity,
                    (Model.SourceBallFlags & SourceBallFlags.Bounce) != 0), Model.SourceTick, Model.SourceSnapTick,
                cameraHeading: Model.SourceCameraHeading,
                humanTeamMask: humans, playOver: Model.Phase != GamePhase.LiveBall && Model.Phase != GamePhase.PreSnap,
                gameFlags: Model.SourceFlags, offenseFire: Model.SourceTeamFire[(int)Model.Possession],
                fireReceiver: Model.SourceLastReceiver[(int)Model.Possession],
                offensePlayFlags: Model.SourceOffensePlay == null ? SourcePlayFlags.None : Model.SourceOffensePlay.Flags,
                defensePlayFlags: Model.SourceDefensePlay == null ? SourcePlayFlags.None : Model.SourceDefensePlay.Flags,
                inPlay: Model.Phase == GamePhase.LiveBall,
                sourceDistances: useCachedGeometry ? Model.SourceDistances : null,
                sourceAngles: useCachedGeometry ? Model.SourceAngles : null);
        }

        private FieldVector SourceDroneMove(ActorState actor, PlayerCommand command)
        {
            if (Model.SourceDrones[actor.Id].LastSourceTick == Model.SourceTick)
                return actor.SourceMove;
            SourceAiFrame frame = Model.SourceAiFrame ?? BuildSourceAiFrame();
            if (actor.SourceMode == SourceActorMode.Kicker)
                frame = CurrentSourceKickerVariables(frame);
            int active = 0;
            foreach (SourceStationConfiguration station in sourceConfiguration.Stations) active |= 1 << station.StationId;
            SourceKickerStage kickerStage = Model.Phase == GamePhase.LiveBall ? SourceKickerStage.InPlay
                : Model.SourceKickApproach ? SourceKickerStage.PreKickoff : SourceKickerStage.PreSnap;
            bool stickOnly = actor.SourceControllerStation >= 0 && Model.CarrierId == -1
                && Model.TargetPlayerId == actor.Id
                && (Model.BallKind == SourceBallKind.Pass || Model.BallKind == SourceBallKind.Lateral);
            SourceDroneButtons humanButtons = actor.SourceControllerStation >= 0 ? actor.SourceNewButtons
                : Model.ThrowerId >= 0 ? Model.Players[Model.ThrowerId].SourceNewButtons : SourceDroneButtons.None;
            ActorState quarterback = Model.Players[Model.OffenseOffset + 3];
            PlayerCommand quarterbackInput = SourcePlayerDriver.ActorCommand(Model, quarterback, Model.SourceCurrentInput);
            int quarterbackStick = SourcePlayerDriver.ActorStick(quarterback, quarterbackInput);
            int motionStick = 24;
            foreach (ActorState player in Model.Players)
                if ((Model.SourceDrones[player.Id].Flags & SourceDroneFlags.Motion) != 0) { motionStick = player.SourceStick; break; }
            SourceDroneButtons captainButtons = SourceDroneButtons.None;
            foreach (ActorState player in Model.Players)
                if (player.Team == Model.Possession && Model.SourceFormationSlots != null
                    && (Model.SourceFormationSlots[player.Id].Control & SourceFormationControl.Captain) != 0)
                { captainButtons = player.SourceButtons; break; }
            SourceRouteStep step = SourceDroneAI.StepRoute(Model.SourceDrones[actor.Id],
                new SourceRouteInput(frame, actor.Id, Model.SourceAiParameters, CurrentSourceRandomContext(),
                    stickOnly: stickOnly, motionDirection: Model.SourceMotionDirection, quarterbackStick: quarterbackStick,
                    humanNewButtons: humanButtons,
                    spinCount: Model.SourceSpinCount[(int)actor.Team], dronesLose: Model.SourceDronesLose,
                    dronesWinning: Model.SourceDronesWinning, blocking: Model.SourceBlocking, receivers: Model.SourceReceivers,
                    droneStates: Model.SourceDrones, formationSlot: Model.SourceFormationSlots[actor.Id],
                    targetFacing: actor.TargetFacing & 1023, rawTargetFacing: actor.TargetFacing,
                    snapContext: new SourceDroneSnapContext(active,
                        sourceConfiguration.StationLayout == SourceStationLayout.FourStation, 0, 0, motionStick),
                    kickerContext: new SourceDroneKickerContext(kickerStage, captainButtons),
                    compatibility: Compatibility), sourceRandom);
            if (step.RetainedTargetlessRunningPush)
                Model.RecordCompatibilityRepair(SourceCompatibilityRepairKind.TargetlessRunningPush, actor.Id,
                    step.State.Target, step.State.Target, "drone.c:_drone_db:DB_H target -1; retained prior valid stick; no angle read",
                    step.State.Stick);
            ApplySourceDroneStep(actor, step);
            if (step.ThrowTarget >= 0) actor.SourceDroneThrowTarget = step.ThrowTarget;
            actor.SourceRequestedScript = step.Script != null
                && (actor.SourceButtons & ~actor.SourceOldButtons & SourceDroneButtons.D) != 0 ? step.Script : null;
            if (!step.CommandWritten) return actor.SourceMove;
            return new FieldVector(step.DesiredMovement.X, step.DesiredMovement.Z);
        }

        private SourceAiFrame CurrentSourceKickerVariables(SourceAiFrame captured)
        {
            // _drone_kicker reads current ball globals/holder position in its process,
            // not a pre-player ball snapshot. The original geometry caches stay intact.
            var actors = new SourceAiActor[14];
            for (int id = 0; id < actors.Length; id++) actors[id] = captured.Actors[id];
            var distances = new float[14, 14];
            var angles = new int[14, 14];
            for (int from = 0; from < 14; from++)
                for (int to = 0; to < 14; to++)
                {
                    distances[from, to] = captured.Distance(from, to);
                    angles[from, to] = captured.Angle(from, to);
                }
            int holder = Model.TargetPlayerId;
            if (holder >= 0 && Model.CarrierId >= 0)
            {
                SourceAiActor previous = actors[holder];
                SourceVector3 root = SourcePlayerDriver.WorldPosition(Model.Players[holder]);
                actors[holder] = new SourceAiActor(previous.Id, new SourcePlanar(root.X, root.Z),
                    previous.Velocity, previous.Mode, previous.Job, previous.Target, previous.Flags,
                    previous.Station, previous.Facing, previous.Stick, previous.AttackTarget, previous.Height,
                    previous.RawFacing);
            }
            var ball = new SourceAiBall(SourceBallTypeFor(Model.BallKind),
                new SourcePlanar(Model.SourceBallPosition.X, Model.SourceBallPosition.Z),
                new SourcePlanar(Model.SourceBallVelocity.X, Model.SourceBallVelocity.Z),
                new SourcePlanar(Model.SourceBallTarget.X, Model.SourceBallTarget.Z), Model.CarrierId,
                Model.TargetPlayerId, Model.ThrowerId, Model.PassTicksElapsed, Model.PassTicksTotal,
                Model.SourceBallPosition.Y, Model.SourceBallVerticalVelocity,
                (Model.SourceBallFlags & SourceBallFlags.Bounce) != 0);
            return new SourceAiFrame(actors, captured.Directions, captured.Rules, ball, Model.SourceTick,
                Model.SourceSnapTick, captured.CameraHeading, captured.HumanTeamMask,
                Model.Phase != GamePhase.LiveBall && Model.Phase != GamePhase.PreSnap,
                Model.SourceFlags, captured.OffenseFire, captured.FireReceiver,
                captured.OffensePlayFlags, captured.DefensePlayFlags, Model.Phase == GamePhase.LiveBall,
                distances, angles);
        }

        private void ApplySourceDroneAnimation(ActorState actor)
        {
            string script = actor.SourceRequestedScript;
            actor.SourceRequestedScript = null;
            if (script == null || actor.SourceControllerStation >= 0 || actor.AnimationState.Primary.IsUninterruptible)
                return;
            actor.Action = script.StartsWith("c", StringComparison.Ordinal) ? ActorAction.Catch
                : script.IndexOf("spin", StringComparison.Ordinal) >= 0 ? ActorAction.Spin : ActorAction.Juke;
            if (script.StartsWith("c2p_", StringComparison.Ordinal))
                actor.AnimationState.UpperBody(script, actor, Model.SourceTick, SourceDraw,
                    AnimationSelectionEvidence.SourceVerified);
            else actor.AnimationState.FullBody(script, actor, Model.SourceTick, SourceDraw,
                AnimationSelectionEvidence.SourceVerified);
            actor.ModeChangedTick = Model.SourceTick;
            actor.SourcePendingCatch = null;
        }

        private void ApplySourceDroneStep(ActorState actor, SourceRouteStep step)
        {
            Model.SourceRandomCallIndex += step.RandomCalls;
            Model.SourceRandomDraws += step.RandomCalls;
            Model.SourceDrones[actor.Id] = step.State;
            actor.SourceJob = step.State.Job;
            actor.SourceTarget = step.State.Target;
            if (step.CommandWritten) actor.SourceStick = step.State.Stick;
            if (step.ButtonsWritten) actor.SourceButtons = step.State.Buttons;
            actor.SourceNewButtons = actor.SourceButtons & ~actor.SourceOldButtons;
            if (actor.SourceControllerStation >= 0 && Model.SourceCurrentInput.StationInputs != null)
                foreach (SourceStationInput input in Model.SourceCurrentInput.StationInputs.Stations)
                    if (input.StationId == actor.SourceControllerStation)
                    {
                        actor.SourceNewButtons |= (SourceDroneButtons)(int)input.Pressed;
                        actor.SourceReleasedButtons |= (SourceDroneButtons)(int)input.Released;
                        break;
                    }
            actor.SourceReleasedButtons |= actor.SourceOldButtons & ~actor.SourceButtons;
            actor.Turbo = (actor.SourceButtons & SourceDroneButtons.C) != 0;
            if (step.ResetNonAButtonTime) actor.SourceNonATicks = 0;
            if (step.ForceNonTurboOneTick) actor.NonTurboTicks = 1;
            if (step.WaveArm) actor.SourceWaveArm = true;
            actor.SourceBackpedal = step.Backpedal;
            actor.SourceBackpedalTarget = step.BackpedalTarget;
            if (step.Facing >= 0) actor.TargetFacing = step.Facing;
            foreach (SourceDefenderEffect effect in step.DefenderEffects)
                Model.SourceDrones[effect.ActorId] = Model.SourceDrones[effect.ActorId].Apply(effect);
        }

        private void ConfigureSourceStations()
        {
            for (int id = 0; id < Model.Players.Length; id++)
                Model.Players[id].SourceControllerStation = -1;
            for (int i = 0; i < Model.SourceStationPlayers.Length; i++)
            {
                Model.SourceStationPlayers[i] = -1;
                Model.SourceStationTurbo[i] = 100f;
            }
            foreach (SourceStationConfiguration station in sourceConfiguration.Stations)
            {
                int chosen = station.InitialPlayerId;
                if (chosen < 0)
                {
                    int offset = (int)station.Team * SliceRules.PlayersPerTeam;
                    int count = 0, index = -1;
                    foreach (SourceStationConfiguration teammate in sourceConfiguration.Stations)
                    {
                        if (teammate.Team != station.Team) continue;
                        if (teammate.StationId == station.StationId) index = count;
                        count++;
                    }
                    SourceFormationControl required = count == 1 || index == (int)Model.SourceCaptain
                        ? SourceFormationControl.Captain : SourceFormationControl.SecondPlayer;
                    for (int id = offset; id < offset + SliceRules.PlayersPerTeam; id++)
                        if ((Model.SourceFormationSlots[id].Control & required) != 0) { chosen = id; break; }
                    if (chosen < 0)
                        throw new InvalidOperationException("Source formation has no " + required + " actor for station "
                            + station.StationId + "; no nearest-player fallback is used.");
                }
                if (Model.Players[chosen].SourceControllerStation >= 0)
                    throw new InvalidOperationException("Two source stations cannot own actor " + chosen + ".");
                Model.SourceStationPlayers[station.StationId] = chosen;
                Model.Players[chosen].SourceControllerStation = station.StationId;
            }
            Model.ControlledPlayerId = sourceConfiguration.Stations.Count == 0 ? -1
                : Model.SourceStationPlayers[sourceConfiguration.Stations[0].StationId];
        }

        private void TransferSourceCatchControl(ActorState actor, bool offense, bool clearGroundedUninterruptible)
        {
            if (actor.SourceControllerStation >= 0 || !SourceContactDriver.IsHumanTeam(Model, actor.Team))
                return;
            int station = -1;
            if (!clearGroundedUninterruptible || sourceConfiguration.StationLayout == SourceStationLayout.FourStation)
            {
                if (offense && Model.ThrowerId >= 0
                    && (Model.BallKind == SourceBallKind.Lateral || Model.BallKind == SourceBallKind.Pass))
                    station = Model.Players[Model.ThrowerId].SourceControllerStation;
                if (station < 0)
                {
                    float farthest = 0f;
                    foreach (ActorState teammate in Model.Players)
                    {
                        if (teammate.Team != actor.Team || teammate.SourceControllerStation < 0) continue;
                        float distance = PlayerSimulation.DistanceSquared(actor.Position, teammate.Position);
                        if (distance <= farthest) continue;
                        farthest = distance;
                        station = teammate.SourceControllerStation;
                    }
                }
            }
            else
                foreach (SourceStationConfiguration configured in sourceConfiguration.Stations)
                    if (configured.Team == actor.Team) { station = configured.StationId; break; }
            if (station < 0)
                throw new InvalidOperationException("Source catch could not resolve a human station for actor " + actor.Id + ".");
            int old = Model.SourceStationPlayers[station];
            if (old >= 0) Model.Players[old].SourceControllerStation = -1;
            Model.SourceStationPlayers[station] = actor.Id;
            actor.SourceControllerStation = station;
            actor.Turbo01 = Model.SourceStationTurbo[station] / 100f;
            actor.SourceButtons |= SourceDroneButtons.A | SourceDroneButtons.B;
            if (sourceConfiguration.Stations.Count > 0 && sourceConfiguration.Stations[0].StationId == station)
                Model.ControlledPlayerId = actor.Id;
            if (clearGroundedUninterruptible && actor.Height == 0f)
                actor.AnimationState.ClearUninterruptible();
            EmitSource(SourceEventType.ControllerChanged, actor.Id, old, station: station);
        }

        private void SourceCarrierControl(ActorState actor)
        {
            if (Model.CarrierId != actor.Id)
                return;
            int side = (int)actor.Team;
            long held = Model.SourceTick - Model.CatchSourceTick;
            if (side == Model.SourceLastPossession && Model.BallKind == SourceBallKind.Pass && held == 18
                && Model.Phase == GamePhase.LiveBall
                && !Model.SourceTeamFire[side]
                && SourceMatchRules.FieldPosition(Model.SourceCatchX, (int)Model.Possession) >= Model.SourceRules.LineOfScrimmage)
            {
                if ((Model.SourceFlags & SourceGameFlags.AirCatch) != 0)
                    actor.SourcePendingFireCheck = true;
                else CheckSourceReceiverFire(actor);
            }
            if (side != Model.SourceLastPossession && held >= 18 && Model.Phase == GamePhase.LiveBall)
                CreditSourcePossession(actor);
            bool inbounds = SourceBallDriver.InBounds(SourcePlayerDriver.WorldPosition(actor));
            int field = SourceMatchRules.FieldPosition(SourcePlayerDriver.WorldPosition(actor).X,
                (int)Model.Possession);
            if ((Model.SourceFlags & SourceGameFlags.AirCatch) != 0
                && (Model.SourceFlags & SourceGameFlags.RecoveredInEndZone) == 0 && inbounds && field < 2)
            {
                Model.SourceFlags |= SourceGameFlags.RecoveredInEndZone;
                Model.SourceFlags &= ~SourceGameFlags.LeftEndZone;
            }
            if ((Model.SourceFlags & SourceGameFlags.AirCatch) != 0 && inbounds && actor.Height == 0f)
            {
                Model.SourceFlags &= ~SourceGameFlags.AirCatch;
                if (actor.SourcePendingFireCheck)
                {
                    actor.SourcePendingFireCheck = false;
                    CheckSourceReceiverFire(actor);
                }
            }
            if (inbounds && field > 2 && (Model.SourceFlags & SourceGameFlags.RecoveredInEndZone) != 0)
                Model.SourceFlags |= SourceGameFlags.LeftEndZone;
            ResolveSourceLocation();
        }

        private void CreditSourcePossession(ActorState actor, bool resetFire = true)
        {
            if (Model.SourceLastPossession == (int)Model.Possession)
                return;
            bool intercepted = Model.BallKind == SourceBallKind.Pass || Model.BallKind == SourceBallKind.Bobble
                || Model.BallKind == SourceBallKind.Lateral && (Model.SourceBallFlags & SourceBallFlags.Bounce) == 0;
            if (intercepted)
            {
                Model.SourceInterceptions[(int)actor.Team]++;
                EmitSource(SourceEventType.Intercepted, actor.Id, Model.ThrowerId);
            }
            else if (Model.BallKind == SourceBallKind.Fumble || Model.BallKind == SourceBallKind.Lateral)
                Model.SourceLostFumbles[1 - (int)actor.Team]++;
            Model.SourceLastPossession = (int)Model.Possession;
            Model.SourceFlags |= SourceGameFlags.PossessionChange;
            // player_down establishes control without _mode_wpn's fire resets.
            if (resetFire && Model.BallKind != SourceBallKind.Kickoff)
            {
                ResetSourceTeamFire(1 - (int)Model.Possession);
            }
            if (resetFire && !Model.SourceTeamFire[(int)Model.Possession])
            {
                ResetSourceTeamFire((int)Model.Possession);
            }
            EmitSource(SourceEventType.PossessionChanged, actor.Id, Model.ThrowerId);
        }

        private void CheckSourceReceiverFire(ActorState actor)
        {
            int side = (int)actor.Team;
            Model.SourceForwardCatcher = actor.Id;
            if (Model.SourceLastReceiver[side] == actor.Id)
            {
                Model.SourceConsecutiveCompletions[side] = checked(Model.SourceConsecutiveCompletions[side] + 1);
                if (Model.SourceConsecutiveCompletions[side] > 2)
                    Model.SourceTeamFire[side] = true;
            }
            else
            {
                Model.SourceLastReceiver[side] = actor.Id;
                Model.SourceConsecutiveCompletions[side] = 1;
            }
        }

        private SourceVector3 SourceWorldJoint(ActorState actor, int joint)
        {
            if (sourceGeometry == null)
                throw new InvalidOperationException("Source gameplay requires ISourceActorGeometry with the actual imported "
                    + "fifteen reference pivots; actor=" + actor.Id + " sourceTick=" + Model.SourceTick
                    + " joint=" + joint + ". No fabricated wrist or renderer readback is used.");
            // Sample the rigid pose at the origin, then place it once using the
            // canonical source root, without a source -> yards -> source roundtrip.
            SourceVector3 relative = sourceGeometry.WorldJoint(GameSnapshot.SourcePlayerView(actor, Model.SourceTick, true), joint);
            SourceVector3 root = SourcePlayerDriver.WorldPosition(actor);
            return new SourceVector3(root.X + relative.X, root.Y + relative.Y, root.Z + relative.Z);
        }

        private void ReleaseSourceBall(int actorId)
        {
            if (Model.Players[actorId].SourcePendingLateral)
                ReleaseSourceLateral(actorId);
            else ReleaseSourcePass(actorId);
        }

        private void SourceAnimationSideEffect(int actorId, SourceAnimationSignal signal)
        {
            if ((signal & SourceAnimationSignal.GeneralRun) != 0) SourceGeneralRun(Model.Players[actorId]);
            if ((signal & SourceAnimationSignal.ReleaseBall) != 0) ReleaseSourceBall(actorId);
            if ((signal & SourceAnimationSignal.PlayerDown) != 0) SourcePlayerDown(actorId);
            if ((signal & SourceAnimationSignal.PuntBall) != 0) ReleaseSourcePunt(actorId);
            if ((signal & SourceAnimationSignal.DropBall) != 0)
                SourceBallDriver.Drop(Model, Model.Players[actorId],
                    Model.SourceRandomProcess == "detect_proc"
                        ? ContactCachedWorldJoint(Model.Players[actorId], Model.Players[actorId].Flipped ? 7 : 4)
                        : SourceWorldJoint(Model.Players[actorId], Model.Players[actorId].Flipped ? 7 : 4),
                    SourceDrawFor("player.c:drop_ball"));
            if ((signal & SourceAnimationSignal.SpinBall) != 0)
                SourceBallDriver.Spin(Model, SourceWorldJoint(Model.Players[actorId], 4));
            if ((signal & (SourceAnimationSignal.SpikeBall | SourceAnimationSignal.FlingBall)) != 0)
                SourceBallDriver.Spike(Model, Model.Players[actorId], SourceWorldJoint(Model.Players[actorId], 4),
                    (signal & SourceAnimationSignal.FlingBall) != 0, SourceDrawFor("player.c:set_spike_ball_vels"));
            if ((signal & SourceAnimationSignal.HikeBall) != 0)
            {
                ActorState actor = Model.Players[actorId];
                int target = Model.OffenseOffset + 3;
                Model.CarrierId = -1;
                Model.Phase = GamePhase.LiveBall;
                Model.SourceMatchPhase = SourceMatchPhase.LiveBall;
                Model.BallMode = BallMode.InFlight;
                Model.TargetPlayerId = target;
                Model.BallKind = SourceBallKind.Lateral;
                Model.SourceBallFlags |= SourceBallFlags.VelocityPitch;
                Model.ThrowerId = Model.LastTouchPlayerId = actorId;
                Model.SourceFumbleX = SourcePlayerDriver.WorldPosition(actor).X;
                bool fieldGoal = (Model.SourceOffensePlay.Flags & SourcePlayFlags.FieldGoal) != 0;
                float zOffset = fieldGoal ? (((actor.Flipped ? 1 : 0) ^ (int)Model.Possession) != 0 ? 4f : -4f) : 0f;
                SourcePassDriver.Lateral(Model, Model.Players[target], SourceWorldJoint(actor, actor.Flipped ? 7 : 4),
                    fieldGoal, 5.40f, zOffset);
                Model.SourceBallFlags &= ~(SourceBallFlags.Bounce | SourceBallFlags.Bobble | SourceBallFlags.FirePass
                    | SourceBallFlags.TurboPass | SourceBallFlags.FieldGoalBounce | SourceBallFlags.HitPost);
                Emit(GameEventType.Snapped, Model.Possession, actorId, 0, Model.BallPosition,
                    target, actorId);
                EmitSource(SourceEventType.Snapped, target, actorId);
            }
        }

        private void BindSourceAnimation(ActorState actor)
        {
            actor.AnimationState.BindMovementExecution(SourceMovementAnimationCode, SourceMovementLeapAttack);
            if (SourceC2Enabled) actor.AnimationState.BindC2VoidExecution(ExecuteSourceC2Void);
            actor.AnimationState.BindContactExecution(SetSourcePuppetPosition, SourceContactHitHeading, SourceContactGroundTime,
                failure => Model.SourceAnimationFailure = failure.Message);
            actor.AnimationState.BindExecution(() => Model.SourcePresentationOrder++,
                SynchronizeSourceAnimationActor, SourceAnimationPresentation,
                signal =>
                {
                    SourceAnimationSideEffect(actor.Id, signal);
                    SynchronizeSourceAnimationActor(actor);
                });
        }

        private void SynchronizeSourceAnimationActor(ActorState actor)
        {
            actor.SourceIsCarrier = actor.SourceHasBall = Model.CarrierId == actor.Id;
            actor.SourceInPlay = Model.Phase == GamePhase.LiveBall;
            actor.SourcePlayOver = Model.Phase == GamePhase.DeadBall || Model.Phase == GamePhase.GameOver;
        }

        private void SourceAnimationPresentation(SourceAnimationLayerState layer, ActorState actor,
            SourceAnimationInstruction instruction, int order)
        {
            string opcode = instruction.Op == SourceAnimationOp.CameraCue ? "ANI_SETCAMBOT"
                : instruction.Op == SourceAnimationOp.ShakerCue ? "ANI_SHAKER" : "ANI_CODE";
            var context = new SourcePresentationContext(layer.SourceTick, order, actor.Id, "plyrseq.c",
                layer.ScriptSymbol, instruction.SourceLine, layer.InstanceId, opcode);
            if (instruction.Op == SourceAnimationOp.CameraCue)
            {
                SourceRandomDraw draw = DrawSourceRandom(instruction.A,
                    "lib/ani3d/ANIM.C:select_csi_ptr/plyrseq.c:" + instruction.SourceLine);
                sourcePresentationCues.Add(new SourceCameraCue(context, SourcePresentationCueKind.CameraOpcode,
                    new SourceCameraSelection(instruction.Text, draw.Value, instruction.A, draw.Raw, draw.Site.ToString()),
                    Model.CarrierId));
            }
            else if (instruction.Op == SourceAnimationOp.ShakerCue)
                sourcePresentationCues.Add(new SourceShakerCue(context, instruction.A, instruction.B, Model.CarrierId));
            else
            {
                SourcePresentationCueKind kind = instruction.Text == "player_down"
                    ? SourcePresentationCueKind.PlayerDownCallback
                    : instruction.Text == "dust_cloud" ? SourcePresentationCueKind.DustCallback
                    : SourcePresentationCueKind.SoundCallback;
                var cue = new SourceCallbackCue(context, kind, instruction.Text,
                    instruction.A, instruction.B, instruction.C, Model.CarrierId);
                sourcePresentationCues.Add(cue);
                if (kind == SourcePresentationCueKind.DustCallback) AdmitSourceDust(actor, instruction.A);
                if (kind != SourcePresentationCueKind.PlayerDownCallback) ExecuteSourceSoundVm(actor, cue);
            }
        }

        private void RequestSourceLateral(int actorId)
        {
            if (actorId < 0 || Model.CarrierId != actorId || Model.Phase != GamePhase.LiveBall)
                return;
            ActorState actor = Model.Players[actorId];
            if ((Model.SourceFlags & SourceGameFlags.PlayEnding) != 0
                || actor.AnimationState.Primary.SequenceName == "M_LATERR"
                || actor.AnimationState.Primary.SequenceName == "M_LATERL")
                return;
            int field = SourceMatchRules.FieldPosition(SourcePlayerDriver.WorldPosition(actor).X,
                (int)Model.Possession);
            foreach (ActorState teammate in Model.Players)
            {
                if (teammate.Id == actorId || teammate.Team != actor.Team
                    || teammate.AnimationState.Primary.IsUninterruptible) continue;
                SourceVector3 from = SourcePlayerDriver.WorldPosition(actor), to = SourcePlayerDriver.WorldPosition(teammate);
                int targetField = SourceMatchRules.FieldPosition(to.X, (int)Model.Possession);
                float x = to.X - from.X, z = to.Z - from.Z;
                if (field < targetField || (float)Math.Sqrt(x * x + z * z) / SourceTiming.SourceUnitsPerYard > 10f) continue;
                BeginSourceLateral(actor, teammate);
                return;
            }
        }

        private void BeginSourceLateral(ActorState actor, ActorState target)
        {
            actor.PendingThrowTarget = target.Id;
            actor.SourcePendingLateral = true;
            actor.Action = ActorAction.Throw;
            float targetZ = target.Position.Z * SourceTiming.SourceUnitsPerYard + target.SourceVelocity.Z * 70f;
            bool left = Model.Possession == TeamId.Away
                ? actor.Position.Z * SourceTiming.SourceUnitsPerYard > targetZ
                : actor.Position.Z * SourceTiming.SourceUnitsPerYard <= targetZ;
            actor.AnimationState.FullBody(left ? "m_laterall_anim" : "m_lateralr_anim", actor, Model.SourceTick,
                SourceDraw, AnimationSelectionEvidence.SourceVerified);
        }

        private void ReleaseSourceLateral(int actorId)
        {
            ActorState actor = Model.Players[actorId];
            int target = actor.PendingThrowTarget;
            if (Model.Phase != GamePhase.LiveBall || Model.CarrierId != actorId || target < 0)
                return;
            Model.CarrierId = -1;
            Model.TargetPlayerId = target;
            Model.BallKind = SourceBallKind.Lateral;
            Model.BallMode = BallMode.InFlight;
            Model.SourceBallFlags &= ~SourceBallFlags.VelocityPitch;
            Model.SourceBallPitchRate = 60;
            Model.ThrowerId = Model.LastTouchPlayerId = actorId;
            Model.SourceFumbleX = SourcePlayerDriver.WorldPosition(actor).X;
            SourcePassDriver.Lateral(Model, Model.Players[target], SourceWorldJoint(actor, actor.Flipped ? 7 : 4), false);
            Model.SourceBallFlags &= ~(SourceBallFlags.Bounce | SourceBallFlags.Bobble | SourceBallFlags.FirePass
                | SourceBallFlags.TurboPass | SourceBallFlags.FieldGoalBounce | SourceBallFlags.HitPost);
            actor.Role = ActorRole.Receiver;
            actor.SourcePendingLateral = false;
            EmitSource(SourceEventType.LateralReleased, actorId, target);
        }

        private void RequestSourceLooseBallAction(int actorId, bool dive)
        {
            if (actorId < 0 || Model.CarrierId >= 0)
                return;
            ActorState actor = Model.Players[actorId];
            if (actor.AnimationState.Busy) return;
            string script = null;
            if ((Model.BallKind == SourceBallKind.Fumble || Model.BallKind == SourceBallKind.Bobble)
                && (float)Math.Sqrt(PlayerSimulation.DistanceSquared(actor.Position, Model.BallPosition)) <= 8f)
                script = Model.SourceBallPosition.Y < 20f || dive ? "c_dive_for_ball_anim"
                    : SourceDrawFor("player.c:intercept_anim_tbl")(2) == 0 ? "c_jmpint_anim" : "c_jmpint2_anim";
            else if (Model.BallKind == SourceBallKind.Pass && Model.TargetPlayerId == actorId)
                script = "c_jmpint_anim";
            else if (actor.Team != Model.Possession && Model.PassTicksTotal - Model.PassTicksElapsed > 10)
                script = SourceDrawFor("player.c:intercept_anim_tbl")(2) == 0 ? "c_jmpint_anim" : "c_jmpint2_anim";
            if (script == null) return;
            actor.SourceBallTarget = Model.PassTarget;
            actor.SourceBallLocation = new FieldVector(SourceMatchRules.AbsoluteYardsFromSourceWorld(Model.SourceBallOrigin.X),
                Model.SourceBallOrigin.Z / SourceTiming.SourceUnitsPerYard);
            actor.Action = dive ? ActorAction.Dive : actor.Team == Model.Possession ? ActorAction.Catch : ActorAction.Intercept;
            actor.AnimationState.FullBody(script, actor, Model.SourceTick, SourceDraw, AnimationSelectionEvidence.SourceVerified);
            actor.ModeChangedTick = Model.SourceTick;
        }

        private void SourceActorActions(ActorState actor, PlayerCommand command)
        {
            if (actor.SourceMode == SourceActorMode.Kicker && Model.Phase == GamePhase.LiveBall
                && (Model.SourceOffensePlay.Flags & SourcePlayFlags.FieldGoal) != 0
                && (Model.SourceFlags & SourceGameFlags.BallKicked) == 0)
            {
                if (Model.CarrierId == -1 && Model.BallKind != SourceBallKind.Lateral)
                {
                    actor.SourceMode = SourceActorMode.Receiver;
                    return;
                }
                SourceVector3 kickRoot = SourcePlayerDriver.WorldPosition(actor);
                float kickX = kickRoot.X - Model.SourceBallPosition.X, kickZ = kickRoot.Z - Model.SourceBallPosition.Z;
                float kickDistance = !UsesPortableHumanKick(SourceKickMeterKind.FieldGoal, actor.Team)
                    ? (float)Math.Sqrt(PlayerSimulation.DistanceSquared(actor.Position, Model.BallPosition)) * SourceTiming.SourceUnitsPerYard
                    : (float)Math.Sqrt(kickX * kickX + kickZ * kickZ);
                if (kickDistance < 8f && Model.CarrierId >= 0)
                {
                    ActorState holder = Model.Players[Model.CarrierId];
                    string sequence = holder.AnimationState.Primary.SequenceName;
                    if (sequence != "M_KHHOLD" && sequence != "M_KHHUT")
                    {
                        actor.SourceMode = SourceActorMode.Receiver;
                        return;
                    }
                    ReleaseSourceFieldGoal(actor, holder);
                }
            }
            SourceAction action = SourceActionFor(command);
            if (Model.Phase == GamePhase.PreSnap && actor.SourceMode == SourceActorMode.QuarterbackPreSnap
                && !actor.AnimationState.Primary.IsUninterruptible && Model.PreSnapTicksRemaining
                    < SourceClock.DefaultAutoHikeTicks - SourceClock.SourceTicksPerSecond)
            {
                bool snap = actor.SourceControllerStation >= 0
                    ? action == SourceAction.Snap || action == SourceAction.Pass
                        || (actor.SourceNewButtons & (SourceDroneButtons.A | SourceDroneButtons.B)) != 0
                    : (actor.SourceNewButtons & (SourceDroneButtons.A | SourceDroneButtons.B)) != 0;
                if (snap)
                {
                    SnapSource(false);
                    return;
                }
            }
            if (actor.SourceControllerStation < 0)
            {
                if (actor.SourceMode == SourceActorMode.Quarterback
                    && (actor.SourceNewButtons & SourceDroneButtons.B) != 0)
                    RequestSourcePass(actor.SourceDroneThrowTarget, (actor.SourceButtons & SourceDroneButtons.C) != 0);
                return;
            }

            if (Model.Phase != GamePhase.LiveBall)
                return;
            if (action == SourceAction.None && command.StationInputs != null)
            {
                // Keep the source bitmask: A and B are separate mode branches.
                if (actor.SourceMode == SourceActorMode.Quarterback)
                {
                    if ((actor.SourceNewButtons & SourceDroneButtons.B) != 0 && Model.CarrierId == actor.Id)
                        RequestSourcePass(-1, actor.Turbo);
                }
                else
                {
                    if ((actor.SourceNewButtons & SourceDroneButtons.A) != 0 && Model.CarrierId < 0)
                        RequestSourceLooseBallAction(actor.Id, (actor.SourceButtons & SourceDroneButtons.C) != 0);
                    if ((actor.SourceNewButtons & SourceDroneButtons.B) != 0
                        && (actor.SourceButtons & SourceDroneButtons.C) == 0 && Model.CarrierId == actor.Id)
                        RequestSourceLateral(actor.Id);
                }
                return;
            }
            if (action == SourceAction.Pass && Model.CarrierId == actor.Id)
                RequestSourcePass(command.Source.IsSpecified ? command.Source.TargetPlayerId : command.TargetPlayerId,
                    command.Turbo && actor.Turbo01 > 0f);
            else if (action == SourceAction.Lateral)
                RequestSourceLateral(actor.Id);
            else if (action == SourceAction.Catch || action == SourceAction.Jump || action == SourceAction.Hurdle
                || action == SourceAction.Dive)
                RequestSourceLooseBallAction(actor.Id, action == SourceAction.Dive);
            else if ((action == SourceAction.Spin || action == SourceAction.Juke) && !actor.AnimationState.Busy)
            {
                if (action == SourceAction.Spin && Model.CarrierId == actor.Id)
                    actor.SourceSpinCount = ++Model.SourceSpinCount[(int)actor.Team];
                actor.Action = action == SourceAction.Spin ? ActorAction.Spin : ActorAction.Juke;
                actor.AnimationState.FullBody(action == SourceAction.Spin ? "spin_anim" : "juke_c_anim", actor,
                    Model.SourceTick, SourceDraw, AnimationSelectionEvidence.SourceVerified);
                actor.ModeChangedTick = Model.SourceTick;
            }
        }

        private void ReleaseSourcePunt(int actorId)
        {
            ActorState actor = Model.Players[actorId];
            if (Model.Phase != GamePhase.LiveBall || Model.CarrierId != actorId) return;
            SourceVector3 ankle = !UsesPortableHumanKick(SourceKickMeterKind.Punt, actor.Team) ? SourceWorldJoint(actor, actor.Flipped ? 14 : 11)
                : SourceKickCachedJoint(actor, actor.Flipped ? 14 : 11);
            Model.CarrierId = -1;
            Model.BallMode = BallMode.InFlight;
            Model.BallKind = SourceBallKind.Punt;
            Model.ThrowerId = Model.LastTouchPlayerId = actorId;
            Model.TargetPlayerId = -1;
            Model.SourceFumbleX = SourcePlayerDriver.WorldPosition(actor).X;
            Model.SourceFlags |= SourceGameFlags.BallPunted;
            Model.SourceBallFlags &= ~(SourceBallFlags.VelocityPitch | SourceBallFlags.Bounce | SourceBallFlags.Bobble
                | SourceBallFlags.FirePass | SourceBallFlags.TurboPass | SourceBallFlags.FieldGoalBounce | SourceBallFlags.HitPost);
            SourceBallDriver.Punt(Model, actor, ankle, SourceDrawFor("player.c:set_punt_ball_vels"));
            CaptureSourceKickLaunch(actor, SourceKickMeterKind.Punt);
            actor.SourceMode = SourceActorMode.DefensiveBack;
            EmitSource(SourceEventType.Punted, actorId);
        }

        private void ReleaseSourceFieldGoal(ActorState actor, ActorState holder)
        {
            actor.Action = ActorAction.Kick;
            actor.AnimationState.FullBody("kickball_anim", actor, Model.SourceTick, SourceDraw,
                AnimationSelectionEvidence.SourceVerified);
            holder.AnimationState.FullBody("qbhut_unhold_ball_anim", holder, Model.SourceTick, SourceDraw,
                AnimationSelectionEvidence.SourceVerified);
            Model.CarrierId = -1;
            Model.BallMode = BallMode.InFlight;
            Model.BallKind = SourceBallKind.FieldGoal;
            Model.ThrowerId = Model.LastTouchPlayerId = actor.Id;
            Model.TargetPlayerId = -1;
            Model.SourceFumbleX = SourcePlayerDriver.WorldPosition(actor).X;
            Model.SourceFlags |= SourceGameFlags.BallKicked;
            Model.SourceBallPitchRate = 60;
            Model.SourceBallFlags &= ~(SourceBallFlags.VelocityPitch | SourceBallFlags.Bounce | SourceBallFlags.Bobble
                | SourceBallFlags.FirePass | SourceBallFlags.TurboPass | SourceBallFlags.FieldGoalBounce | SourceBallFlags.HitPost);
            SourceBallDriver.FieldGoal(Model, actor, SourceDrawFor("player.c:set_field_goal_ball_vels"));
            CaptureSourceKickLaunch(actor, SourceKickMeterKind.FieldGoal);
            actor.SourceMode = SourceActorMode.Receiver;
            EmitSource(SourceEventType.Kicked, actor.Id);
        }

        private void AddSourcePoints(int home, int away)
        {
            Model.HomeScore += home;
            Model.AwayScore += away;
            Model.SourceQuarterScores[Model.Quarter - 1, 0] += home;
            Model.SourceQuarterScores[Model.Quarter - 1, 1] += away;
        }

        private void EmitSource(SourceEventType type, int actor = -1, int target = -1,
            SourceMatchResult result = default(SourceMatchResult), int station = -1)
        {
            sourceStepEvents.Add(new SourceGameEvent(Model.Tick, Model.SourceTick, type,
                actor >= 0 ? (TeamId?)Model.Players[actor].Team : Model.Possession,
                actor, target, station, Model.BallPosition, result: result,
                playKind: result.IsSpecified ? result.PlayKind : (SourcePlayKind)Model.SourceRules.PlayType));
        }

        private static bool OutsideSourceField(FieldVector position)
        {
            return Math.Abs(position.Z * SourceTiming.SourceUnitsPerYard) > 182.85f
                || Math.Abs(SourceMatchRules.SourceWorldFromAbsoluteYards(position.X)) > 468f;
        }

        private bool OverSourceDefensiveEndZone(SourceVector3 position)
        {
            float directed = Model.OffenseDirection * position.X;
            return directed >= 390f + SourceTiming.SourceUnitsPerYard && directed <= 468f
                && Math.Abs(position.Z) <= 182.85f;
        }

        private static SourceBallType SourceBallTypeFor(SourceBallKind kind)
        {
            switch (kind)
            {
                case SourceBallKind.Unspecified:
                case SourceBallKind.Pass: return SourceBallType.Pass;
                case SourceBallKind.Kickoff: return SourceBallType.Kickoff;
                case SourceBallKind.Fumble: return SourceBallType.Fumble;
                case SourceBallKind.Bobble: return SourceBallType.Bobble;
                case SourceBallKind.FieldGoal: return SourceBallType.FieldGoal;
                case SourceBallKind.Punt: return SourceBallType.Punt;
                case SourceBallKind.Lateral: return SourceBallType.Lateral;
                default: throw new NotSupportedException("Unported source ball type: " + kind);
            }
        }
    }
}
