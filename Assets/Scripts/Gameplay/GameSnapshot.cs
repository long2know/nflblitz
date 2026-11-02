using System;
using System.Collections.Generic;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal sealed class GameSnapshot : ISourcePresentationState, ISourceKickMeterState, ISourceCameraState, ISourcePossessionState,
        ISourceAudibleObservationState, ISourceLiveAnimationObservationState, ISourceCabinetFlowState
    {
        private readonly SourceKickMeterMetrics kickMeterMetrics;
        private readonly SourceKickMeterView kickMeter;
        private readonly SourceKickLaunchView kickLaunch;
        SourceKickMeterMode ISourceKickMeterState.KickMeterMode
        { get { return kickMeterMetrics == null ? SourceKickMeterMode.Disabled : SourceKickMeterMode.PortableAuthored; } }
        SourceKickMeterMetrics ISourceKickMeterState.KickMeterMetrics { get { return kickMeterMetrics; } }
        SourceKickMeterView ISourceKickMeterState.KickMeter { get { return kickMeter; } }
        SourceKickLaunchView ISourceKickMeterState.LastKickLaunch { get { return kickLaunch; } }
        public long Tick { get; }
        public long SourceTick { get; }
        public int SourceTicksPerSecond { get; }
        public uint PackedGameTime { get; }
        public bool ClockActive { get; }
        public int PlayNumber { get; }
        public GamePhase Phase { get; }
        public TeamId Possession { get; }
        public int OffenseDirection { get; }
        public int Quarter { get; }
        public int QuarterTicksRemaining { get; }
        public int PreSnapTicksRemaining { get; }
        public int Down { get; }
        public float LineOfScrimmage { get; }
        public float FirstDownLine { get; }
        public int HomeScore { get; }
        public int AwayScore { get; }
        public int ControlledPlayerId { get; }
        public PlayId HomePlay { get; }
        public PlayId AwayPlay { get; }
        public PlayOutcome LastOutcome { get; }
        public BallView Ball { get; }
        public IReadOnlyList<PlayerView> Players { get; }
        public SourceMatchView Match { get; }
        public SourceCabinetFlowView CabinetFlow { get; }
        public SourcePresentationSettings Presentation { get; }
        public SourceCameraBoundary CameraBoundary { get; }
        public System.Collections.ObjectModel.ReadOnlyCollection<SourceProcessView> Processes { get; }
        public SourcePossessionMessageView PossessionMessage { get; }
        public System.Collections.ObjectModel.ReadOnlyCollection<SourcePossessionMessageView> PossessionMessages { get; }
        public long CameraOutputSourceTick { get; }
        public bool CameraProcessSleeping { get; }
        private readonly SourceAudibleRetentionView audibleRetention;
        private readonly System.Collections.ObjectModel.ReadOnlyCollection<SourceAudibleReinitializationCheckpoint> audibleReinitializations;
        SourceAudibleRetentionView ISourceAudibleObservationState.AudibleRetention { get { return audibleRetention; } }
        System.Collections.ObjectModel.ReadOnlyCollection<SourceAudibleReinitializationCheckpoint>
            ISourceAudibleObservationState.AudibleReinitializations { get { return audibleReinitializations; } }
        private readonly System.Collections.ObjectModel.ReadOnlyCollection<SourceLiveAnimationWitness> liveAnimations;
        System.Collections.ObjectModel.ReadOnlyCollection<SourceLiveAnimationWitness>
            ISourceLiveAnimationObservationState.LiveAnimations { get { return liveAnimations; } }

        internal GameSnapshot(SimulationState model, int cameraProcessOrder = -1)
        {
            if (model.SourceDefaultAudiblesEnabled)
            {
                audibleRetention = CaptureAudibleRetention(model);
                audibleReinitializations = CaptureAudibleReinitializations(model);
            }
            kickMeterMetrics = model.SourceKickMeterMetrics;
            kickMeter = model.SourceKickMeter == null ? default(SourceKickMeterView) : model.SourceKickMeter.View;
            kickLaunch = model.SourceKickLaunch;
            Tick = model.Tick;
            SourceTick = model.SourceMode ? model.SourceTick : model.Tick;
            SourceTicksPerSecond = model.SourceMode ? SourceTiming.DefaultTicksPerSecond : SliceRules.TicksPerSecond;
            PackedGameTime = model.SourceMode ? model.PackedGameTime : 0u;
            ClockActive = model.SourceMode ? model.ClockActive : model.Phase == GamePhase.LiveBall;
            PlayNumber = model.PlayNumber;
            Phase = model.Phase;
            Possession = model.Possession;
            OffenseDirection = model.OffenseDirection;
            Quarter = model.Quarter;
            QuarterTicksRemaining = model.QuarterTicksRemaining;
            PreSnapTicksRemaining = model.PreSnapTicksRemaining;
            Down = model.Down;
            LineOfScrimmage = model.LineOfScrimmage;
            FirstDownLine = model.FirstDownLine;
            HomeScore = model.HomeScore;
            AwayScore = model.AwayScore;
            ControlledPlayerId = model.ControlledPlayerId;
            HomePlay = model.HomePlay;
            AwayPlay = model.AwayPlay;
            LastOutcome = model.LastOutcome;
            Presentation = model.SourceMode ? model.SourcePresentation : null;
            BallMode ballMode = (model.SourceBallFlags & SourceBallFlags.Phantom) != 0 ? BallMode.InFlight : model.BallMode;
            Ball = new BallView(ballMode, model.BallPosition, model.BallHeight,
                ballMode == BallMode.Held ? model.CarrierId : -1,
                ballMode == BallMode.InFlight ? model.TargetPlayerId : -1,
                model.BallKind, model.BallVelocity, model.BallVerticalVelocity, model.LastTouchPlayerId,
                model.SourceMode ? BallControl(model) : default(SourceBallControlView),
                model.SourceMode ? new SourceBallPresentationView(unchecked((int)model.SourceBallFlags))
                    : default(SourceBallPresentationView));

            var players = new PlayerView[model.Players.Length];
            for (int id = 0; id < players.Length; id++)
            {
                ActorState actor = model.Players[id];
                players[id] = model.SourceMode ? SourcePlayerView(actor, model.SourceTick)
                    : new PlayerView(actor.Id, actor.Team, actor.Position, actor.Velocity, actor.Turbo01,
                        actor.Height, role: actor.Role, modelScale: actor.ModelScale);
            }
            Players = Array.AsReadOnly(players);
            if (model.SourceDefaultAudiblesEnabled)
            {
                var witnesses = new SourceLiveAnimationWitness[players.Length];
                for (int id = 0; id < witnesses.Length; id++)
                    witnesses[id] = CaptureLiveAnimationWitness(model.Players[id], players[id].Animation);
                liveAnimations = Array.AsReadOnly(witnesses);
            }
            if (model.SourceMode)
            {
                Processes = model.SourceProcesses.Processes;
                PossessionMessage = model.SourcePossessionMessage;
                PossessionMessages = model.SourcePossessionMessages;
                CameraOutputSourceTick = model.SourceMovementCameraTick;
                CameraProcessSleeping = model.SourceCameraPossessionContinuation;
                CameraBoundary = CaptureCamera(model, cameraProcessOrder);
                var scores = new SourceQuarterScore[model.Quarter];
                for (int q = 0; q < scores.Length; q++)
                    scores[q] = new SourceQuarterScore(q, model.SourceQuarterScores[q, 0], model.SourceQuarterScores[q, 1]);
                var stations = new SourceStationView[model.SourceConfiguration.Stations.Count];
                for (int i = 0; i < stations.Length; i++)
                {
                    int station = model.SourceConfiguration.Stations[i].StationId;
                    stations[i] = new SourceStationView(station, model.SourceStationPlayers[station],
                        model.SourceStationTurbo[station] / 100f);
                }
                Match = new SourceMatchView(new SourcePlayActivityStatsView(
                    model.SourceOffensivePlays[(int)TeamId.Home],
                    model.SourceOffensivePlays[(int)TeamId.Away],
                    model.SourcePassAttempts[(int)TeamId.Home],
                    model.SourcePassAttempts[(int)TeamId.Away],
                    model.SourceTackles[(int)TeamId.Home],
                    model.SourceTackles[(int)TeamId.Away],
                    model.SourceFumbles[(int)TeamId.Home],
                    model.SourceFumbles[(int)TeamId.Away],
                    model.SourceLostFumbles[(int)TeamId.Home],
                    model.SourceLostFumbles[(int)TeamId.Away],
                    model.SourceBobbles[(int)TeamId.Home],
                    model.SourceBobbles[(int)TeamId.Away],
                    model.SourceDeflections[(int)TeamId.Home],
                    model.SourceDeflections[(int)TeamId.Away]),
                    new SourceScoringStatsView(
                    model.SourceSafeties[(int)TeamId.Home],
                    model.SourceSafeties[(int)TeamId.Away],
                    model.SourceTwoPointAttempts[(int)TeamId.Home],
                    model.SourceTwoPointAttempts[(int)TeamId.Away],
                    model.SourceTwoPointConversions[(int)TeamId.Home],
                    model.SourceTwoPointConversions[(int)TeamId.Away]),
                    new SourceTouchdownStatsView(
                    model.SourceTouchdowns[(int)TeamId.Home],
                    model.SourceTouchdowns[(int)TeamId.Away]),
                    new SourceFourthDownAttemptsView(
                    model.SourceFourthDownAttempts[(int)TeamId.Home],
                    model.SourceFourthDownAttempts[(int)TeamId.Away]),
                    new SourceResultProgressionStatsView(
                    model.SourceFirstDowns[(int)TeamId.Home],
                    model.SourceFirstDowns[(int)TeamId.Away],
                    model.SourceFourthDownConversions[(int)TeamId.Home],
                    model.SourceFourthDownConversions[(int)TeamId.Away]),
                    new SourcePlaceKickStatsView(
                    model.SourceFieldGoalAttempts[(int)TeamId.Home],
                    model.SourceFieldGoalAttempts[(int)TeamId.Away],
                    model.SourceFieldGoals[(int)TeamId.Home],
                    model.SourceFieldGoals[(int)TeamId.Away]),
                    new SourcePlayEndStatsView(
                    model.SourceReturnYards[(int)TeamId.Home],
                    model.SourceReturnYards[(int)TeamId.Away],
                    model.SourceKickReturns[(int)TeamId.Home],
                    model.SourceKickReturns[(int)TeamId.Away],
                    model.SourceRushingYards[(int)TeamId.Home],
                    model.SourceRushingYards[(int)TeamId.Away],
                    model.SourceRushes[(int)TeamId.Home],
                    model.SourceRushes[(int)TeamId.Away],
                    model.SourceSackYards[(int)TeamId.Home],
                    model.SourceSackYards[(int)TeamId.Away],
                    model.SourceSacks[(int)TeamId.Home],
                    model.SourceSacks[(int)TeamId.Away]),
                    new SourceCompletedPassStatsView(
                    model.SourcePassingYards[(int)TeamId.Home],
                    model.SourcePassingYards[(int)TeamId.Away],
                    model.SourceTotalYards[(int)TeamId.Home],
                    model.SourceTotalYards[(int)TeamId.Away],
                    model.SourcePassTouchdowns[(int)TeamId.Home],
                    model.SourcePassTouchdowns[(int)TeamId.Away]),
                    new SourcePassCompletionsView(model.SourcePassCompletions[(int)TeamId.Home],
                    model.SourcePassCompletions[(int)TeamId.Away]),
                    new SourceInterceptionsView(model.SourceInterceptions[(int)TeamId.Home],
                    model.SourceInterceptions[(int)TeamId.Away]),
                    model.SourceTimeOfPossession, model.SourceConfiguration, (SourcePlayKind)model.SourceRules.PlayType,
                    model.SourceMatchPhase, model.SourcePeriodView,
                    new SourcePossessionView(model.Possession, (TeamId)model.SourceRules.InitialOffenseSide,
                        (TeamId)model.SourceLastPossession, (TeamId)model.SourceFirstPossession,
                        model.SourceCaptain), scores, stations, model.SourceResultView,
                    model.SourceHomePlay, model.SourceAwayPlay, model.SourceSelectionCountdown,
                    model.SourcePatChoice, model.SourcePatStatus,
                    model.SourceRules.PlayType == SourcePlayType.Kickoff ? (SourceKickoffType?)model.SourceKickoffKind : null,
                    model.SourceRules.PlayType == SourcePlayType.Kickoff ? (TeamId?)model.SourceKickingSide : null,
                    model.SourceRules.PlayType == SourcePlayType.Kickoff ? (TeamId?)(1 - model.SourceKickingSide) : null,
                    model.SourceOffenseFlipped,
                    SourceTeamGameplay(model, TeamId.Home), SourceTeamGameplay(model, TeamId.Away),
                    model.SourceForwardCatcher, model.SourceDronesLose, model.SourceDronesWinning,
                    model.SourceHandicapRepetitionBonus, model.SourceCompatibility,
                    model.SourceCompatibilityRepairCount, model.SourceCompatibilityRepairs, model.SourceSelectionTimer,
                    model.SourceRandomSelections[0], model.SourceRandomSelections[1],
                    CaptureAudibles(model));
                CabinetFlow = model.SourceCabinetFlow;
            }
        }

        internal static SourceAudibleRetentionView CaptureAudibleRetention(SimulationState model)
        {
            return new SourceAudibleRetentionView(model.SourceStationTurbo, model.SourceStationPlayers, model.SourceKickMeter);
        }

        internal static System.Collections.ObjectModel.ReadOnlyCollection<SourceAudibleReinitializationCheckpoint>
            CaptureAudibleReinitializations(SimulationState model)
        {
            return Array.AsReadOnly(model.SourceAudibleReinitializations.ToArray());
        }

        private static SourceAudibleVmLayerCheckpoint? CaptureAudibleVmLayer(SourceAnimationLayerState layer)
        {
            if (layer == null) return null;
            string program = layer.ScriptSymbol;
            if (program == null) return null;
            return new SourceAudibleVmLayerCheckpoint(program, layer.LogicalInstructionIndex, layer.Mode,
                layer.RawTransitionSourceSequenceName, layer.RawTransitionSourceOrdinal,
                layer.RawTransitionCount, layer.RawTransitionTicks);
        }

        private static SourceLiveAnimationWitness CaptureLiveAnimationWitness(ActorState actor, ActorAnimationView animation)
        {
            SourceAnimationLayerState primary = actor.AnimationState == null ? null : actor.AnimationState.Primary;
            SourceAnimationLayerState secondary = actor.AnimationState == null ? null : actor.AnimationState.Secondary;
            SourceAudibleVmLayerCheckpoint? primaryVm = CaptureAudibleVmLayer(primary);
            SourceAudibleVmLayerCheckpoint? secondaryVm = CaptureAudibleVmLayer(secondary);
            long primaryTick = primaryVm.HasValue ? primary.SourceTick : -1;
            long secondaryTick = secondaryVm.HasValue ? secondary.SourceTick : -1;
            return new SourceLiveAnimationWitness(actor.Id, animation, primaryVm, secondaryVm,
                primaryTick >= 0 ? (long?)primaryTick : null,
                secondaryTick >= 0 ? (long?)secondaryTick : null);
        }

        internal static SourceAudibleReinitializationCheckpoint CaptureAudibleReinitialization(SimulationState model,
            SourceAudibleReinitializationPhase phase, int side, int actorPlayerId,
            SourceAudibleDbpsInput dbpsInput = null)
        {
            var actors = new SourceAudibleActorCheckpoint[14];
            var drones = new SourceAudibleDroneView[14];
            var witnesses = new SourceLiveAnimationWitness[14];
            for (int i = 0; i < 14; i++)
            {
                ActorState actor = model.Players[i];
                SourceRouteState drone = model.SourceDrones[i];
                SourceFormationSlot slot = model.SourceFormationSlots[i];
                PlayerView player = SourcePlayerView(actor, model.SourceTick);
                SourceLiveAnimationWitness witness = CaptureLiveAnimationWitness(actor, player.Animation);
                witnesses[i] = witness;
                actors[i] = new SourceAudibleActorCheckpoint(player,
                    SourcePlayerDriver.WorldPosition(actor), actor.SourceRoot,
                    new SourceVector3(actor.SourceVelocity.X, actor.SourceVerticalVelocity, actor.SourceVelocity.Z),
                    actor.SourceMove, actor.Facing, actor.TargetFacing, actor.SourceStick, (int)actor.SourceButtons,
                    actor.SourceTarget, (int)actor.SourceJob, actor.SourceCelebrateTime, actor.SourceOverrideTime,
                    (int)slot.Control, (int)slot.Mode, slot.Script, drone.Stick, drone.StickTicks,
                    witness.PrimaryVm, witness.SecondaryVm,
                    actor.SourceDroneFace);
                drones[i] = new SourceAudibleDroneView(i, drone.Program.Identity, drone.WordIndex,
                    drone.Target, (int)drone.Flags, (int)drone.Job, drone.ThinkTicks, drone.BlitzTicks,
                    drone.PassReactTicks, drone.FakeZoneTicks, drone.CatchTicks, drone.WaitTicks,
                    drone.BlockTicks, model.SourceNameCoordinateInvalidation[i], drone.Ended);
            }
            var stations = new SourceStationView[model.SourceConfiguration.Stations.Count];
            for (int i = 0; i < stations.Length; i++)
            {
                int station = model.SourceConfiguration.Stations[i].StationId;
                stations[i] = new SourceStationView(station, model.SourceStationPlayers[station],
                    model.SourceStationTurbo[station] / 100f);
            }
            SourceQuarterbackPlan plan = model.SourceQuarterbackPlan;
            var globals = new SourceAudibleGlobalView(model.SourceAnimationUniqueMasks, model.SourceBreathControl,
                model.SourceQbEvaded, plan.DropbackUnits, plan.GainSought, plan.RollDirection,
                plan.MotionDirection, plan.HurdlePass);
            return new SourceAudibleReinitializationCheckpoint(model.SourceTick,
                model.SourceProcesses.Require(GameSimulation.GameProcess).Serial,
                model.SourceAudibleReinitializations.Count, phase, side, actorPlayerId, model.SourceRandomDraws,
                model.SourceRandomProcess, model.SourceRandomActor, model.SourceRandomProcessOrder,
                model.SourceRandomCallIndex, actors, drones, globals, stations, CaptureAudibleRetention(model),
                dbpsInput == null ? null : new SourceAudibleDbpsCall(dbpsInput,
                    CaptureAudibleDbpsState(model, model.SourceDrones[actorPlayerId], actorPlayerId)), witnesses);
        }

        private static SourceAudibleDroneView CaptureAudibleDbpsDrone(SimulationState model, SourceRouteState state, int id)
        {
            return new SourceAudibleDroneView(id, state.Program.Identity, state.WordIndex, state.Target,
                (int)state.Flags, (int)state.Job, state.ThinkTicks, state.BlitzTicks, state.PassReactTicks,
                state.FakeZoneTicks, state.CatchTicks, state.WaitTicks, state.BlockTicks,
                model.SourceNameCoordinateInvalidation[id], state.Ended);
        }

        internal static SourceAudibleDbpsState CaptureAudibleDbpsState(SimulationState model, SourceRouteState state, int id)
        {
            ActorState actor = model.Players[id];
            return new SourceAudibleDbpsState(CaptureAudibleDbpsDrone(model, state, id), state.MoveTicks,
                new FieldVector(state.ZonePoint.X, state.ZonePoint.Z),
                new FieldVector(state.ZoneVelocity.X, state.ZoneVelocity.Z), state.ZoneLowX, state.ZoneHighX,
                state.ZoneLowZ, state.ZoneHighZ, state.Stick, state.OldStick, state.StickTicks, (int)state.Buttons,
                SourceActorFlags(actor), (int)actor.SourceJob, actor.SourceTarget, actor.SourceDroneFace,
                actor.SourceBackpedalTarget);
        }

        internal static SourceAudibleDbpsInput CaptureAudibleDbpsInput(SimulationState model,
            SourceRouteInput input, SourceRouteState state)
        {
            if (input.DroneStates == null || !input.FormationSlot.HasValue)
                throw new InvalidOperationException("An actual audible DBPS call requires its existing drone/formation inputs.");
            var actors = new SourceAudibleDbpsActorInput[14];
            var drones = new SourceAudibleDroneView[14];
            for (int i = 0; i < 14; i++)
            {
                SourceAiActor actor = input.Frame.Actors[i];
                actors[i] = new SourceAudibleDbpsActorInput(i,
                    new SourceVector3(actor.Position.X, actor.Height, actor.Position.Z),
                    new FieldVector(actor.Velocity.X, actor.Velocity.Z), (int)actor.Mode, (int)actor.Job,
                    actor.Target, (int)actor.Flags, actor.Station, actor.RawFacing, actor.Stick);
                drones[i] = CaptureAudibleDbpsDrone(model, input.DroneStates[i], i);
            }
            int carrier = input.Frame.Ball.Carrier;
            SourceFormationSlot slot = input.FormationSlot.Value;
            return new SourceAudibleDbpsInput(input.Frame.SourceTick, input.ActorId, input.Frame.CameraHeading,
                input.Frame.Rules.OffenseSide, input.Frame.Rules.LineOfScrimmage, (int)input.Frame.DefensePlayFlags,
                carrier, carrier < 0 ? (int?)null : input.Frame.Angle(input.ActorId, carrier),
                input.Parameters.Side0Handicap, input.Parameters.Side1Handicap, input.Frame.UsesSuppliedSourceGeometry,
                new FieldVector(slot.Offset.X, slot.Offset.Z), CaptureAudibleDbpsState(model, state, input.ActorId),
                actors, drones);
        }

        internal static SourceAudibleView CaptureAudibles(SimulationState model)
        {
            SourceAudibleState a = model.SourceAudibleState;
            if (!model.SourceDefaultAudiblesEnabled || a == null || a.OffenseSide != (int)model.Possession) return null;
            int off = (int)model.Possession, def = 1 - off;
            var sprites = new List<SourceAudibleSpriteView>();
            foreach (SourceAudibleSprite s in a.Objects)
            {
                SourceSpriteListMembership membership = (a.NonAlpha.Contains(s) ? SourceSpriteListMembership.NonAlpha : 0)
                    | (a.Alpha.Contains(s) ? SourceSpriteListMembership.Alpha : 0);
                sprites.Add(new SourceAudibleSpriteView(s.Serial, s.Team, s.Id, s.Parent, "aud_menu",
                    new SourceVector3(s.Team == TeamId.Home ? 70 : 442, 192, 1.4f), membership,
                    membership != 0, s.Color, s.Combiner, s.Chroma));
            }
            var children = new List<SourceAudibleFlashView>();
            foreach (SourceAudibleChild child in a.Children)
                children.Add(new SourceAudibleFlashView(child.Sprite.Team,
                    model.SourceProcesses.Contains(child.Serial) ? model.SourceProcesses.Require(child.Serial) : child.LastProcess,
                    child.Sprite.Serial, child.Stage, child.EntryTick, child.Cycles, child.Color, child.Combiner,
                    child.CheckTick, child.CheckExists));
            SourcePlaySelection currentOff = off == 0 ? model.SourceHomePlay : model.SourceAwayPlay;
            SourcePlaySelection currentDef = def == 0 ? model.SourceHomePlay : model.SourceAwayPlay;
            SourceAudibleDroneView[] drones = null;
            if (Array.TrueForAll(model.SourceDrones, state => state.Program != null))
            {
                drones = new SourceAudibleDroneView[model.SourceDrones.Length];
                for (int i = 0; i < drones.Length; i++)
                {
                    SourceRouteState state = model.SourceDrones[i];
                    drones[i] = new SourceAudibleDroneView(i, state.Program.Identity, state.WordIndex,
                        state.Target, (int)state.Flags, (int)state.Job, state.ThinkTicks, state.BlitzTicks,
                        state.PassReactTicks, state.FakeZoneTicks, state.CatchTicks, state.WaitTicks,
                        state.BlockTicks, model.SourceNameCoordinateInvalidation[i], state.Ended);
                }
            }
            SourceQuarterbackPlan plan = model.SourceQuarterbackPlan;
            var globals = new SourceAudibleGlobalView(model.SourceAnimationUniqueMasks, model.SourceBreathControl,
                model.SourceQbEvaded, plan.DropbackUnits, plan.GainSought, plan.RollDirection,
                plan.MotionDirection, plan.HurdlePass);
            var actorButtons = new int[model.Players.Length];
            for (int i = 0; i < actorButtons.Length; i++) actorButtons[i] = (int)model.Players[i].SourceButtons;
            return new SourceAudibleView(model.SourceTick,
                new SourceAudibleTeamView(TeamId.Home, a.Timers[0], a.Actions[0], a.Shown[0],
                    a.Backgrounds[0] == null ? (long?)null : a.Backgrounds[0].Serial),
                new SourceAudibleTeamView(TeamId.Away, a.Timers[1], a.Actions[1], a.Shown[1],
                    a.Backgrounds[1] == null ? (long?)null : a.Backgrounds[1].Serial),
                new SourceAudibleRoleView(SourceAudibleRole.Offense, (TeamId)off, a.Callers[off],
                    a.Choices[0], a.Pending[0], a.OriginalSelections[off], currentOff),
                new SourceAudibleRoleView(SourceAudibleRole.Defense, (TeamId)def, a.Callers[def],
                    a.Choices[1], a.Pending[1], a.OriginalSelections[def], currentDef),
                a.OriginalFlip, model.SourceOffenseFlipped, model.SourceProcesses.Require(GameSimulation.GameProcess),
                a.RestartStage, Array.FindAll(model.SourceDigitalScans, scan => scan.IsSpecified && scan.SourceTick == model.SourceTick),
                sprites, children, a.Calls, drones, globals, actorButtons);
        }

        internal static int RawCameraGameMode(SimulationState model)
        {
            int mode = RawMovementPhase(model);
            return mode == 5 && !model.SourceKickApproach ? 1 : mode;
        }

        internal static int RawMovementPhase(SimulationState model)
        {
            return model.Phase == GamePhase.LiveBall ? 2
                : model.Phase == GamePhase.DeadBall ? 3 : model.Phase == GamePhase.GameOver ? 4
                : model.Phase == GamePhase.PreSnap ? model.SourceRules.PlayType == SourcePlayType.Kickoff ? 5 : 1 : 0;
        }

        private static SourceCameraBoundary CaptureCamera(SimulationState model, int processOrder)
        {
            var actors = new SourceCameraActor[14];
            for (int id = 0; id < actors.Length; id++)
            {
                ActorState actor = model.Players[id];
                actors[id] = new SourceCameraActor(id, (int)actor.SourceMode, SourceActorFlags(actor), actor.Facing,
                    SourcePlayerDriver.WorldPosition(actor), model.SourceCameraGroundCenters[id]);
            }
            int stations = 0;
            foreach (SourceStationConfiguration station in model.SourceConfiguration.Stations)
                stations |= 1 << station.StationId;
            bool hh = model.SourceConfiguration.StationLayout == SourceStationLayout.FourStation &&
                ((stations & 3) == 3 || (stations & 12) == 12);
            return new SourceCameraBoundary(model.SourceTick, processOrder, RawCameraGameMode(model),
                (int)model.SourceFlags, (int)model.SourceRules.PlayType, (int)model.SourceLastResult, model.Quarter - 1,
                model.SourceRules.OffenseSide, model.SourceLastPossession, model.SourceRules.LineOfScrimmage,
                model.CarrierId, hh, model.SourcePresentation != null && model.SourcePresentation.PupPullback != 0,
                model.PreSnapTicksRemaining, model.SourceOffensePlay == null ? 0 : (int)model.SourceOffensePlay.Flags,
                new SourceCameraBall(model.SourceBallPosition,
                    new SourceVector3(model.SourceBallVelocity.X, model.SourceBallVerticalVelocity, model.SourceBallVelocity.Z),
                    model.SourceBallOrigin, model.SourceBallTarget, model.SourceCameraBallTarget,
                    (int)GameSimulation.SourceBallTypeFor(model.BallKind), unchecked((int)model.SourceBallFlags),
                    model.PassTicksElapsed, model.ThrowerId, model.TargetPlayerId), actors,
                model.SourceOffenseTurnover, model.SourceMatchPhase == SourceMatchPhase.MatchEnded ? 4
                    : model.SourceMatchPhase == SourceMatchPhase.PeriodTransition ? 3 : 2);
        }

        internal static PlayerView SourcePlayerView(ActorState actor, long tick, bool relativeToOrigin = false)
        {
            return new PlayerView(actor.Id, actor.Team, relativeToOrigin ? new FieldVector(50f, 0f) : actor.Position,
                actor.Velocity, actor.Turbo01, relativeToOrigin ? 0f : actor.Height,
                SourcePoseMath.Facing(actor.Facing), actor.Role, actor.AnimationState.View(actor, tick),
                actor.ModelScale, new SourceActorView(actor.SourceControllerStation, actor.SourceRoster, actor.NoCatchTicks,
                    new SourceActorPresentationView(SourceActorFlags(actor), (int)actor.SourceMode),
                    new SourceActorContactView(actor.SourceHitPartnerId, actor.SourcePuppetLinkId,
                        actor.SourcePuppetReleaseTick, actor.SourceAttackMode, actor.SourceAttackTime, actor.SourceBlockCount),
                    actor.SourceHandicap, actor.SourceRawHandicap));
        }

        private static SourceTeamGameplayView SourceTeamGameplay(SimulationState model, TeamId team)
        {
            int side = (int)team;
            return new SourceTeamGameplayView(team, model.SourceTeamFire[side], model.SourceLastReceiver[side],
                model.SourceConsecutiveCompletions[side], model.SourceConsecutiveLosses[side],
                model.SourceTeamHandicap[side], model.SourceEffectiveTeamHandicap[side]);
        }

        internal static int SourceActorFlags(ActorState actor)
        {
            const int tracked = 0x01 | 0x08 | 0x200 | 0x800 | 0x1000 | 0x2000
                | 0x20000 | 0x80000 | 0x200000 | 0x800000;
            int flags = actor.SourceRawFlags & ~tracked;
            if (actor.Flipped) flags |= 0x01;
            if (actor.Turbo) flags |= 0x08;
            if (actor.SourceReverseRoute) flags |= 0x200;
            if (actor.SourceBackpedal) flags |= 0x800;
            if (actor.SourceLateHittable) flags |= 0x1000;
            if (actor.SourceWaveArm) flags |= 0x2000;
            if (actor.SourceUnblockable) flags |= 0x20000;
            if (actor.SourceThrowing) flags |= 0x80000;
            if (actor.SourcePendingFireCheck) flags |= 0x200000;
            if (actor.SourceHelmetPopped) flags |= 0x800000;
            return flags;
        }

        private static SourceBallControlView BallControl(SimulationState model)
        {
            int carrier = model.CarrierId;
            long duration = carrier < 0 ? 0 : Math.Max(0L, model.SourceTick - model.CatchSourceTick);
            int ticks = (int)Math.Min(int.MaxValue, duration);
            return new SourceBallControlView(carrier < 0 ? -1 : model.Players[carrier].SourceControllerStation,
                carrier, (TeamId)model.SourceLastPossession, ticks,
                model.Phase == GamePhase.LiveBall && carrier >= 0 && ticks >= 18);
        }
    }
}
