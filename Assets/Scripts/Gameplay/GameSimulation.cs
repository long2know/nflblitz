using System;
using System.Collections.Generic;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public enum SimulationProfile { LegacyBaseline60Hz, OriginalRelease57Hz }

    public interface ISourceCameraHeadingInput
    {
        int SourceCameraHeading { get; }
        long SourceCameraHeadingSampleTick { get; }
        void SetSourceCameraHeading(int heading, long sourceTick);
    }

    public interface ISourceWorldState
    {
        long SourceTick { get; }
        SourceVector3 SourceBallWorldPosition { get; }
        SourceVector3 SourceActorWorldPosition(int playerId);
        SourceVector3 SourceActorPelvisPosition(int playerId);
    }

    public sealed partial class GameSimulation : ISourcePresentationSimulation, ISourceCameraHeadingInput, ISourceWorldState
    {
        private readonly List<GameEvent> stepEvents = new List<GameEvent>();
        private readonly ISourceAnimationLibrary sourceAnimations;
        private readonly ISourceActorGeometry sourceGeometry;
        private readonly SourceMatchConfiguration sourceConfiguration;
        private readonly SourcePlaybook sourcePlaybook;
        private readonly ISourceTeamRoster sourceRosters;
        private readonly SourcePresentationSettings sourcePresentation;
        private readonly SourceMovementRuntimeData sourceMovementData;
        private readonly SourceMovementSettings sourceMovementSettings;
        private readonly SourceHandicapSettings sourceHandicapSettings;
        private readonly SourceKickMeterMetrics sourceKickMeterMetrics;
        private readonly Func<int, ISourceRandom> sourceRandomFactory;
        private ISourceRandom sourceRandom;
        private readonly List<SourceGameEvent> sourceStepEvents = new List<SourceGameEvent>();
        private readonly List<SourcePresentationCue> sourcePresentationCues = new List<SourcePresentationCue>();
        private readonly Queue<PlayerCommand> pendingSourceActions = new Queue<PlayerCommand>();
        private readonly Queue<SourceInputFrame> pendingSourceStationInputs = new Queue<SourceInputFrame>();
        private readonly SourceStationInput?[] sourceHeldStations = new SourceStationInput?[4];
        private readonly Queue<SourceCommand>[] pendingSourceStationCommands = {
            new Queue<SourceCommand>(), new Queue<SourceCommand>(),
            new Queue<SourceCommand>(), new Queue<SourceCommand>()
        };
        private bool sourceStationInputSeen;
        private int pendingSourceCameraHeading;
        private long pendingSourceCameraSampleTick;
        private bool executingSourceCamera;
        private uint randomState;

        public SimulationProfile Profile { get; }
        public SourceGameplayCompatibility Compatibility { get; }
        public string ReferenceProfile
        {
            get
            {
                if (sourceKickMeterMetrics != null)
                    return "Portable-authored Source57 human kick meters; authored image metrics, NOT original generated native metrics; "
                        + Compatibility;
                if (Profile == SimulationProfile.OriginalRelease57Hz && Compatibility == SourceGameplayCompatibility.Portable)
                    return "Portable Source57/M1: explicit consumer-index corrections; NOT original hardware/ABI/RNG equivalence";
                return Profile == SimulationProfile.OriginalRelease57Hz
                    ? "Original normal57 source VM/clock/rules/ball kernels; explicit RNG provenance; M1/C1/C2/I1/cinematic caller parity remains open"
                    : "Explicit approximate 60 Hz legacy baseline";
            }
        }
        internal SimulationState Model { get; private set; }
        public IGameState State { get; private set; }
        public IReadOnlyList<GameEvent> Events { get; private set; }
        public IReadOnlyList<SourceGameEvent> SourceEvents { get; private set; }
        public SourcePresentationCueBatch PresentationCues { get; private set; }
        public ISourceRandom SourceRandomService { get { return sourceRandom; } }
        public Func<IGameState, long, SourceInputFrame, SourceInputFrame> SourceStationInputTransform { get; set; }
        public Func<IGameState, long, int> SourceCameraHeadingProvider { get; set; }
        public int SourceCameraStageOrder { get; private set; } = -1;
        public int SourceCameraHeading { get { return Model.SourceCameraHeading; } }
        public long SourceCameraHeadingSampleTick { get { return Model.SourceCameraHeadingSampleTick; } }
        public int SourceCameraRawGameMode { get { return (int)MovementPhase; } }
        public long SourceTick { get { return Model.SourceMode ? Model.SourceTick : Model.Tick; } }
        public SourceVector3 SourceBallWorldPosition
        {
            get
            {
                if (!Model.SourceMode) throw new InvalidOperationException("Original world state requires the source profile.");
                return Model.SourceBallPosition;
            }
        }

        public GameSimulation(int seed = 1)
        {
            Profile = SimulationProfile.LegacyBaseline60Hz;
            Restart(seed);
        }

        public GameSimulation(ISourceAnimationLibrary animations, int seed = 1)
            : this(animations, null, null, null, seed)
        {
        }

        public GameSimulation(ISourceAnimationLibrary animations, ISourceActorGeometry geometry, int seed = 1)
            : this(animations, geometry, null, null, seed)
        {
        }

        public GameSimulation(ISourceAnimationLibrary animations, ISourceActorGeometry geometry,
            SourceMatchConfiguration configuration, Func<int, ISourceRandom> randomFactory, int seed = 1)
            : this(animations, geometry, null, configuration, randomFactory, seed)
        {
        }

        public GameSimulation(ISourceAnimationLibrary animations, ISourceActorGeometry geometry,
            SourcePlaybook playbook, SourceMatchConfiguration configuration = null,
            Func<int, ISourceRandom> randomFactory = null, int seed = 1, ISourceTeamRoster rosters = null,
            SourcePresentationSettings presentation = null, SourceMovementRuntimeData movementData = null,
            SourceMovementSettings movementSettings = null, SourceC2Bootstrap c2Bootstrap = null,
            SourceHandicapSettings handicapSettings = null,
            SourceGameplayCompatibility compatibility = SourceGameplayCompatibility.StrictSource,
            SourceFireGameplayConfiguration fireEffects = null, SourceKickMeterMetrics kickMeterMetrics = null)
        {
            if (compatibility != SourceGameplayCompatibility.StrictSource && compatibility != SourceGameplayCompatibility.Portable)
                throw new ArgumentOutOfRangeException(nameof(compatibility));
            if (compatibility == SourceGameplayCompatibility.Portable
                && (c2Bootstrap == null || c2Bootstrap.Data.Profile != Blitz.AssetImport.SourceC2CallbackData.M1Profile))
                throw new NotSupportedException("Portable gameplay requires the explicit actual M1 source bootstrap.");
            Compatibility = compatibility;
            if (fireEffects != null && (c2Bootstrap == null ||
                c2Bootstrap.Data.Profile != Blitz.AssetImport.SourceC2CallbackData.M1Profile))
                throw new NotSupportedException("Authored source-fire effects require the explicit actual M1 bootstrap.");
            sourceFireConfiguration = fireEffects;
            sourceAnimations = animations ?? throw new ArgumentNullException(nameof(animations));
            if (kickMeterMetrics != null)
            {
                Blitz.AssetImport.SourceAnimationLibrary actual = animations as Blitz.AssetImport.SourceAnimationLibrary;
                if (actual == null || actual.InventorySha256 != Blitz.AssetImport.SourceTeamResourceProfile.ContactAnimationSha256
                    && actual.InventorySha256 != Blitz.AssetImport.SourceTeamResourceProfile.C2AnimationSha256)
                    throw new NotSupportedException("Portable-authored kick meters require the explicit actual C1 or C2/M1 animation library.");
            }
            sourceKickMeterMetrics = kickMeterMetrics;
            sourceGeometry = geometry;
            sourcePlaybook = playbook;
            sourceRosters = rosters;
            sourcePresentation = presentation;
            sourceMovementData = movementData;
            sourceMovementSettings = movementSettings;
            sourceHandicapSettings = handicapSettings ?? new SourceHandicapSettings();
            sourceC2Bootstrap = c2Bootstrap;
            sourceConfiguration = configuration ?? DefaultSourceConfiguration();
            if (sourceConfiguration.Profile != SourceSimulationProfile.OriginalRelease57Hz)
                throw new ArgumentException("The source constructor requires the explicit 57 Hz profile.", nameof(configuration));
            sourceRandomFactory = randomFactory;
            Profile = SimulationProfile.OriginalRelease57Hz;
            Restart(seed);
        }

        public void Restart(int seed = 1)
        {
            randomState = unchecked((uint)seed) ^ 0x9E3779B9u;
            Model = new SimulationState();
            Model.SourceCompatibility = Compatibility;
            pendingSourceCameraHeading = 0;
            pendingSourceCameraSampleTick = 0;
            stepEvents.Clear();
            sourceStepEvents.Clear();
            sourcePresentationCues.Clear();
            sourceContactEvents.Clear();
            unresolvedContactSounds.Clear();
            sourceSoundVmPending.Clear();
            sourceSoundVmFailed = false;
            sourceC2Events.Clear();
            SourceCameraStageOrder = -1;
            ClearPendingInput();
            if (Profile == SimulationProfile.OriginalRelease57Hz)
            {
                sourceRandom = sourceRandomFactory == null
                    ? new SourceInjectedRandom(site => NextInt(16777216),
                        new SourceRandomProvenance("OriginalRelease57Hz/preview",
                            "Explicit legacy LCG preview provider; NOT the historical platform random()/rand() body",
                            "GameSimulation.Restart(" + seed + ")", SourceRandomEvidence.ExternalUnverified))
                    : sourceRandomFactory(seed);
                if (sourceRandom == null)
                    throw new InvalidOperationException("The source RNG factory returned no service.");
                InitializeSource();
            }
            else
            {
                PlayerSimulation.LineUp(Model);
                Model.BallHeight = 0f;
            }
            Publish();
        }

        public void Step(PlayerCommand command)
        {
            if (Profile == SimulationProfile.OriginalRelease57Hz && Model.SourceAnimationFailure != null)
                throw new InvalidOperationException("Original source execution failed; Restart is required, not a retry: "
                    + Model.SourceAnimationFailure);
            stepEvents.Clear();
            sourceStepEvents.Clear();
            sourcePresentationCues.Clear();
            sourceContactEvents.Clear();
            unresolvedContactSounds.Clear();
            sourceC2Events.Clear();
            SourceCameraStageOrder = -1;
            Model.Tick++;
            if (Profile == SimulationProfile.OriginalRelease57Hz)
            {
                if (command.Action != InputAction.None || HasSourceCommandEdge(command.Source))
                    pendingSourceActions.Enqueue(new PlayerCommand(action: command.Action, play: command.Play,
                        targetPlayerId: command.TargetPlayerId, source: command.Source));
                if (command.StationInputs != null)
                    CaptureSourceStationInputs(command.StationInputs);
                Model.SourceClockRemainder += SourceTiming.DefaultTicksPerSecond;
                if (Model.SourceClockRemainder >= SliceRules.TicksPerSecond)
                {
                    Model.SourceClockRemainder -= SliceRules.TicksPerSecond;
                    Model.SourceTick++;
                    PlayerCommand edge = pendingSourceActions.Count == 0 ? default(PlayerCommand)
                        : pendingSourceActions.Dequeue();
                    SourceStep(new PlayerCommand(PlayerSimulation.NormalizeInput(command.Move), command.Turbo,
                        edge.Action, edge.Play, edge.TargetPlayerId, edge.Source,
                        DrainSourceStationInputs()));
                }

                Publish();
                return;
            }
            switch (Model.Phase)
            {
                case GamePhase.PlaySelect:
                    if (command.Action == InputAction.SelectPlay)
                        SelectPlay(command.Play);
                    break;
                case GamePhase.PreSnap:
                    if (Model.Possession == TeamId.Home && command.Action == InputAction.Snap)
                        Snap();
                    else if (--Model.PreSnapTicksRemaining <= 0)
                        Snap();
                    break;
                case GamePhase.LiveBall:
                    LiveStep(command);
                    break;
                case GamePhase.DeadBall:
                    if (command.Action == InputAction.Continue)
                        Continue();
                    break;
            }
            Publish();
        }

        public void ClearPendingInput()
        {
            pendingSourceActions.Clear();
            pendingSourceStationInputs.Clear();
            Array.Clear(sourceHeldStations, 0, sourceHeldStations.Length);
            foreach (Queue<SourceCommand> commands in pendingSourceStationCommands) commands.Clear();
            sourceStationInputSeen = false;
            if (Model != null)
            {
                Model.SourceCurrentInput = default(PlayerCommand);
                foreach (ActorState actor in Model.Players)
                    if (actor.SourceControllerStation >= 0)
                    {
                        actor.SourceButtons = actor.SourceOldButtons = actor.SourceNewButtons = SourceDroneButtons.None;
                        actor.SourceReleasedButtons = SourceDroneButtons.None;
                        Array.Clear(actor.SourceButtonHistory, 0, actor.SourceButtonHistory.Length);
                        actor.NonTurboTicks = actor.SourceNonATicks = actor.SourceNonBTicks = 0;
                    }
            }

        }

        public void SetSourceCameraHeading(int heading, long sourceTick)
        {
            if (Profile != SimulationProfile.OriginalRelease57Hz)
                throw new InvalidOperationException("Source camera input belongs only to the explicit source profile.");
            if (heading < 0 || heading > 1023) throw new ArgumentOutOfRangeException(nameof(heading));
            if (sourceTick < 0 || sourceTick > Model.SourceTick) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            pendingSourceCameraHeading = heading;
            pendingSourceCameraSampleTick = sourceTick;
        }

        public int DrawSourceCameraRandom(int maximum, string sourceIdentity)
        {
            if (!executingSourceCamera)
                throw new InvalidOperationException("Camera RNG belongs inside the executing source camera-stage callback.");
            return SourceDraw(maximum, sourceIdentity);
        }

        public void SetSourceMovementCameraFrame(SourceMovementCameraFrame frame, long sourceTick)
        {
            if (Profile != SimulationProfile.OriginalRelease57Hz || !executingSourceCamera)
                throw new InvalidOperationException("Actual M1 cambot data belongs inside the executing source camera provider.");
            if (sourceTick != Model.SourceTick) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            Model.SourceMovementCamera = frame ?? throw new ArgumentNullException(nameof(frame));
            Model.SourceMovementCameraTick = sourceTick;
        }

        public SourceVector3 SourceActorWorldPosition(int playerId)
        {
            if (!Model.SourceMode) throw new InvalidOperationException("Original world state requires the source profile.");
            if (playerId < 0 || playerId >= Model.Players.Length) throw new ArgumentOutOfRangeException(nameof(playerId));
            return SourcePlayerDriver.WorldPosition(Model.Players[playerId]);
        }

        public SourceVector3 SourceActorPelvisPosition(int playerId)
        {
            SourceVector3 root = SourceActorWorldPosition(playerId);
            return new SourceVector3(root.X, root.Y + Model.Players[playerId].SourceRoot.Y, root.Z);
        }

        private void CaptureSourceStationInputs(SourceInputFrame frame)
        {
            var captured = new SourceStationInput[frame.Stations.Count];
            for (int i = 0; i < captured.Length; i++)
            {
                SourceStationInput input = frame.Stations[i];
                bool configured = false;
                foreach (SourceStationConfiguration station in sourceConfiguration.Stations)
                    if (station.StationId == input.StationId) { configured = true; break; }
                if (!configured)
                    throw new InvalidOperationException("Source input names unconfigured station " + input.StationId + ".");
                SourceButtons previous = sourceHeldStations[input.StationId].HasValue
                    ? sourceHeldStations[input.StationId].Value.Held : SourceButtons.None;
                captured[i] = new SourceStationInput(input.StationId, input.StickX, input.StickZ, input.Held,
                    input.Pressed | input.Held & ~previous, input.Released | previous & ~input.Held, input.Command);
                sourceHeldStations[input.StationId] = new SourceStationInput(input.StationId,
                    input.StickX, input.StickZ, input.Held);
            }
            sourceStationInputSeen = true;
            pendingSourceStationInputs.Enqueue(new SourceInputFrame(captured));
        }

        private static bool HasSourceCommandEdge(SourceCommand command)
        {
            return command.IsSpecified && (command.Action != SourceAction.None || command.OffenseFlipped.HasValue);
        }

        private SourceInputFrame DrainSourceStationInputs()
        {
            if (!sourceStationInputSeen && SourceStationInputTransform == null) return null;
            var pressed = new SourceButtons[4];
            var released = new SourceButtons[4];
            while (pendingSourceStationInputs.Count > 0)
            {
                foreach (SourceStationInput input in pendingSourceStationInputs.Dequeue().Stations)
                {
                    pressed[input.StationId] |= input.Pressed;
                    released[input.StationId] |= input.Released;
                    if (HasSourceCommandEdge(input.Command))
                        pendingSourceStationCommands[input.StationId].Enqueue(input.Command);
                }
            }
            var inputs = new SourceStationInput[sourceConfiguration.Stations.Count];
            for (int i = 0; i < inputs.Length; i++)
            {
                int station = sourceConfiguration.Stations[i].StationId;
                SourceStationInput held = sourceHeldStations[station] ?? new SourceStationInput(station);
                SourceCommand command = pendingSourceStationCommands[station].Count == 0
                    ? default(SourceCommand) : pendingSourceStationCommands[station].Dequeue();
                inputs[i] = new SourceStationInput(station, held.StickX, held.StickZ, held.Held,
                    pressed[station], released[station], command);
            }
            return new SourceInputFrame(inputs);
        }

        private void SelectPlay(PlayId play)
        {
            bool homeOffense = Model.Possession == TeamId.Home;
            if (homeOffense ? play != PlayId.Run && play != PlayId.QuickPass
                : play != PlayId.Cover && play != PlayId.Blitz)
                return;

            Model.HomePlay = play;
            Model.AwayPlay = homeOffense
                ? (NextInt(2) == 0 ? PlayId.Cover : PlayId.Blitz)
                : (NextInt(2) == 0 ? PlayId.Run : PlayId.QuickPass);
            Model.AwayPassDecisionTick = 36 + NextInt(30);
            Model.RunLane = NextInt(2) == 0 ? -1 : 1;
            PlayerSimulation.LineUp(Model);
            Model.PreSnapTicksRemaining = homeOffense ? SliceRules.PreSnapTicks : SliceRules.TicksPerSecond;
            Model.Phase = GamePhase.PreSnap;
            Emit(GameEventType.PlaySelected, TeamId.Home, -1, 0, Model.BallPosition);
        }

        private void Snap()
        {
            Model.Phase = GamePhase.LiveBall;
            Model.PreSnapTicksRemaining = 0;
            Model.PlayNumber++;
            Model.LiveTicks = 0;
            Model.ForwardPassUsed = false;
            Model.CatchProtectionTicks = 0;
            Model.PassTicksElapsed = 0;
            Model.PassTicksTotal = 0;
            Model.SnapLineOfScrimmage = Model.LineOfScrimmage;
            Model.LastOutcome = PlayOutcome.None;
            Model.CarrierId = Model.OffenseOffset + 3;
            Model.TargetPlayerId = -1;
            Model.BallMode = BallMode.Held;
            for (int id = 0; id < Model.Players.Length; id++)
                Model.Players[id].SnapPosition = Model.Players[id].Position;
            SynchronizeHeldBall();
            Emit(GameEventType.Snapped, Model.Possession, Model.CarrierId, 0, Model.BallPosition);
        }

        private void LiveStep(PlayerCommand command)
        {
            Model.LiveTicks++;
            if (Model.QuarterTicksRemaining > 0)
                Model.QuarterTicksRemaining--;
            if (Model.CatchProtectionTicks > 0)
                Model.CatchProtectionTicks--;

            if (Model.BallMode == BallMode.Held)
            {
                SynchronizeHeldBall();
                if (ResolveLocation())
                    return;
            }
            if (Model.Possession == TeamId.Away && command.Action == InputAction.SwitchDefender)
                Model.ControlledPlayerId = PlayerSimulation.NearestHome(Model, Model.ControlledPlayerId);

            bool flightAtEntry = Model.BallMode == BallMode.InFlight;
            PlayerSimulation.Update(Model, command);
            if (Model.BallMode == BallMode.Held)
            {
                SynchronizeHeldBall();
                if (ResolveLocation())
                    return;
            }

            if (Model.Possession == TeamId.Home && command.Action == InputAction.Pass)
                TryPass(command.TargetPlayerId);
            else if (Model.Possession == TeamId.Away)
                AwayPassingDecision();

            if (flightAtEntry)
                AdvancePass();
            if (Model.Phase != GamePhase.LiveBall || Model.BallMode != BallMode.Held)
                return;

            SynchronizeHeldBall();
            if (ResolveLocation())
                return;
            if (Model.Possession == TeamId.Away && command.Action == InputAction.Tackle)
            {
                ActorState defender = Model.Players[Model.ControlledPlayerId];
                if (PlayerSimulation.DistanceSquared(defender.Position, Model.BallPosition)
                    <= PlayerSimulation.ManualTackleReach * PlayerSimulation.ManualTackleReach)
                {
                    EndPlay(PlayOutcome.Tackled, Model.CarrierId, Model.BallPosition, defender.Id);
                    return;
                }
            }
            AutomaticTackles();
        }

        private bool ResolveLocation()
        {
            if (GameplayRules.IsOutside(Model.BallPosition))
            {
                EndPlay(PlayOutcome.OutOfBounds, Model.CarrierId, Model.BallPosition);
                return true;
            }
            if (GameplayRules.IsOpponentEndZone(Model.Possession, Model.BallPosition.X))
            {
                EndPlay(PlayOutcome.Touchdown, Model.CarrierId, Model.BallPosition);
                return true;
            }
            return false;
        }

        private void TryPass(int targetId)
        {
            int quarterbackId = Model.OffenseOffset + 3;
            if (Model.BallMode != BallMode.Held || Model.CarrierId != quarterbackId || Model.ForwardPassUsed
                || targetId < Model.OffenseOffset + 4 || targetId > Model.OffenseOffset + 6)
                return;
            ActorState quarterback = Model.Players[quarterbackId];
            if (Model.OffenseDirection * (quarterback.Position.X - Model.SnapLineOfScrimmage) > 0f)
                return;

            ActorState receiver = Model.Players[targetId];
            float distance = (float)Math.Sqrt(PlayerSimulation.DistanceSquared(quarterback.Position, receiver.Position));
            int flightTicks = (int)Math.Ceiling(distance / 32f * SliceRules.TicksPerSecond);
            flightTicks = Math.Max(22, Math.Min(66, flightTicks));
            FieldVector lead = PlayerSimulation.Add(receiver.Position,
                PlayerSimulation.Scale(PlayerSimulation.ReceiverLeadVelocity(Model, receiver),
                    (float)flightTicks / SliceRules.TicksPerSecond));
            lead = new FieldVector(GameplayRules.Clamp(lead.X, -7f, 107f),
                GameplayRules.Clamp(lead.Z, -SliceRules.FieldHalfWidthYards + 1f, SliceRules.FieldHalfWidthYards - 1f));
            if (Model.OffenseDirection * (lead.X - quarterback.Position.X) <= 0f)
                return;

            Model.ForwardPassUsed = true;
            Model.ThrowerId = quarterbackId;
            Model.BallMode = BallMode.InFlight;
            Model.CarrierId = -1;
            Model.TargetPlayerId = targetId;
            Model.PassStart = quarterback.Position;
            Model.PassTarget = lead;
            Model.PassTicksTotal = flightTicks;
            Model.PassTicksElapsed = 0;
            Model.BallPosition = quarterback.Position;
            Model.BallHeight = 1.1f;
            if (Model.Possession == TeamId.Home)
                Model.ControlledPlayerId = quarterbackId;
            Emit(GameEventType.PassThrown, Model.Possession, targetId, 0, quarterback.Position,
                quarterbackId, targetId);
        }

        private void AdvancePass()
        {
            Model.PassTicksElapsed++;
            float progress = Math.Min(1f, (float)Model.PassTicksElapsed / Model.PassTicksTotal);
            Model.BallPosition = PlayerSimulation.Add(Model.PassStart,
                PlayerSimulation.Scale(PlayerSimulation.Subtract(Model.PassTarget, Model.PassStart), progress));
            Model.BallHeight = 1.1f + 5f * (float)Math.Sin(Math.PI * progress);
            if (Model.PassTicksElapsed < Model.PassTicksTotal)
                return;

            int targetId = Model.TargetPlayerId;
            ActorState receiver = Model.Players[targetId];
            if (!GameplayRules.IsOutside(receiver.Position)
                && PlayerSimulation.DistanceSquared(receiver.Position, Model.PassTarget)
                    <= PlayerSimulation.CatchReach * PlayerSimulation.CatchReach)
            {
                Model.BallMode = BallMode.Held;
                Model.CarrierId = targetId;
                Model.TargetPlayerId = -1;
                Model.CatchProtectionTicks = 12;
                if (Model.Possession == TeamId.Home)
                    Model.ControlledPlayerId = targetId;
                SynchronizeHeldBall();
                Emit(GameEventType.PassCaught, Model.Possession, targetId, 0, Model.BallPosition,
                    targetId, Model.ThrowerId);
            }
            else
            {
                Model.BallHeight = 0f;
                EndPlay(PlayOutcome.Incomplete, targetId, Model.BallPosition);
            }
        }

        private void AwayPassingDecision()
        {
            if (Model.OffensivePlay != PlayId.QuickPass || Model.ForwardPassUsed
                || Model.BallMode != BallMode.Held || Model.CarrierId != Model.OffenseOffset + 3)
                return;
            bool pressure = Model.LiveTicks >= 20 && PlayerSimulation.NearestOpponentDistanceSquared(
                Model, Model.Players[Model.CarrierId]) < 36f;
            if (Model.LiveTicks < Model.AwayPassDecisionTick && !pressure)
                return;

            int bestId = Model.OffenseOffset + 4;
            float best = float.MinValue;
            for (int slot = 4; slot < SliceRules.PlayersPerTeam; slot++)
            {
                ActorState receiver = Model.Players[Model.OffenseOffset + slot];
                float separation = (float)Math.Sqrt(PlayerSimulation.NearestOpponentDistanceSquared(Model, receiver));
                float gain = Model.OffenseDirection * (receiver.Position.X - Model.SnapLineOfScrimmage);
                float score = separation + gain * 0.12f;
                if (score > best)
                {
                    best = score;
                    bestId = receiver.Id;
                }
            }
            TryPass(bestId);
        }

        private void AutomaticTackles()
        {
            if (Model.LiveTicks < 12 || Model.CatchProtectionTicks > 0)
                return;
            for (int id = 0; id < Model.Players.Length; id++)
            {
                ActorState defender = Model.Players[id];
                if (defender.Team == Model.Possession || id == Model.ControlledPlayerId
                    || defender.ContactCooldownTicks > 0)
                    continue;
                if (PlayerSimulation.DistanceSquared(defender.Position, Model.BallPosition)
                    <= PlayerSimulation.AutoTackleReach * PlayerSimulation.AutoTackleReach)
                {
                    EndPlay(PlayOutcome.Tackled, Model.CarrierId, Model.BallPosition, defender.Id);
                    return;
                }
            }
        }

        private void EndPlay(PlayOutcome cause, int affectedPlayerId, FieldVector position, int attackerId = -1)
        {
            if (Model.Phase != GamePhase.LiveBall)
                return;
            TeamId offense = Model.Possession;
            PlayResolution resolution = GameplayRules.Resolve(offense, Model.Down, Model.SnapLineOfScrimmage,
                Model.FirstDownLine, position.X, cause);
            if (cause == PlayOutcome.Tackled)
                Emit(GameEventType.Tackled, offense, affectedPlayerId, 0, position, attackerId, affectedPlayerId);
            else if (cause == PlayOutcome.OutOfBounds)
                Emit(GameEventType.OutOfBounds, offense, affectedPlayerId, 0, position);
            else if (cause == PlayOutcome.Incomplete)
                Emit(GameEventType.Incomplete, offense, affectedPlayerId, 0, position);

            Model.Phase = GamePhase.DeadBall;
            Model.LastOutcome = resolution.Outcome;
            Model.Possession = resolution.Possession;
            Model.Down = resolution.Down;
            Model.LineOfScrimmage = resolution.LineOfScrimmage;
            Model.FirstDownLine = resolution.FirstDownLine;
            Model.HomeScore += resolution.HomePoints;
            Model.AwayScore += resolution.AwayPoints;
            Model.BallMode = BallMode.Dead;
            Model.BallPosition = position;
            Model.BallHeight = 0f;
            Model.CarrierId = -1;
            Model.TargetPlayerId = -1;
            for (int id = 0; id < Model.Players.Length; id++)
                Model.Players[id].Velocity = default(FieldVector);

            FieldVector nextSpot = new FieldVector(Model.LineOfScrimmage, 0f);
            if (resolution.Outcome == PlayOutcome.FirstDown)
                Emit(GameEventType.FirstDown, offense, affectedPlayerId, 0, nextSpot);
            else if (resolution.Outcome == PlayOutcome.TurnoverOnDowns)
                Emit(GameEventType.TurnoverOnDowns, Model.Possession, -1, 0, nextSpot);
            else if (resolution.Outcome == PlayOutcome.Touchdown)
                Emit(GameEventType.Touchdown, offense, affectedPlayerId, SliceRules.TouchdownPoints, position);
            else if (resolution.Outcome == PlayOutcome.Safety)
                Emit(GameEventType.Safety, GameplayRules.Opponent(offense), affectedPlayerId, SliceRules.SafetyPoints, position);
            if (offense != Model.Possession)
                Emit(GameEventType.PossessionChanged, Model.Possession, -1, 0, nextSpot);
            Model.ControlledPlayerId = Model.Possession == TeamId.Home ? 3 : PlayerSimulation.NearestHome(Model, -1);
            if (Model.QuarterTicksRemaining == 0 && !Model.QuarterEndEmitted)
            {
                Model.QuarterEndEmitted = true;
                Emit(GameEventType.QuarterEnded, TeamId.Home, -1, Model.Quarter, default(FieldVector));
            }
        }

        private void Continue()
        {
            if (Model.QuarterTicksRemaining == 0)
            {
                if (Model.Quarter == SliceRules.QuarterCount)
                {
                    Model.Phase = GamePhase.GameOver;
                    int result = Model.HomeScore.CompareTo(Model.AwayScore);
                    Emit(GameEventType.GameEnded, TeamId.Home, -1, result, default(FieldVector));
                    return;
                }
                Model.Quarter++;
                Model.QuarterTicksRemaining = SliceRules.QuarterTicks;
                Model.QuarterEndEmitted = false;
            }
            Model.PreSnapTicksRemaining = 0;
            Model.Phase = GamePhase.PlaySelect;
            PlayerSimulation.LineUp(Model);
        }

        private void SynchronizeHeldBall()
        {
            Model.BallPosition = Model.Players[Model.CarrierId].Position;
            Model.BallHeight = 1.1f;
            Model.TargetPlayerId = -1;
        }

        private int NextInt(int maximum)
        {
            randomState = unchecked(randomState * 1664525u + 1013904223u);
            return (int)((randomState >> 8) % (uint)maximum);
        }

        private void Emit(GameEventType type, TeamId team, int playerId, int value, FieldVector position,
            int actorPlayerId = -1, int targetPlayerId = -1)
        {
            stepEvents.Add(new GameEvent(Model.Tick, type, team, playerId, value, position,
                actorPlayerId, targetPlayerId, Model.SourceMode ? Model.SourceTick : -1));
        }

        private void Publish()
        {
            State = new GameSnapshot(Model);
            Events = Array.AsReadOnly(stepEvents.ToArray());
            SourceEvents = Array.AsReadOnly(sourceStepEvents.ToArray());
            PresentationCues = new SourcePresentationCueBatch(sourcePresentationCues);
        }
    }
}
