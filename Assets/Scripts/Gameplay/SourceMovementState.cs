using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Gameplay
{
    [Flags]
    public enum SourceMovementFlags
    {
        None = 0, Flip = 0x1, OnScreen = 0x2, HighCount = 0x4, Turbo = 0x8,
        OldTurbo = 0x10, NoCollision = 0x20, AnimationTick = 0x40, InBounds = 0x80,
        Celebrate = 0x100, ReverseRoute = 0x200, FlashWhite = 0x400,
        Backpedal = 0x800, LateHittable = 0x1000, WaveArm = 0x2000,
        Dizzy = 0x4000, SpeedOverride = 0x8000, DroneTwoPart = 0x10000,
        Unblockable = 0x20000, DroneFace = 0x40000, Throwing = 0x80000,
        DraggingPlayer = 0x100000, PendingFireCheck = 0x200000,
        FlashRed = 0x400000, PopHelmet = 0x800000
    }

    [Flags]
    public enum SourceMovementModes
    {
        None = 0, End = 0x1, Uninterruptible = 0x2, TwoPart = 0x4,
        Streaming = 0x8, PrepareTransition = 0x10, RotationOnly = 0x20,
        Zero = 0x40, Reacting = 0x80, CanBlock = 0x100, NoCylinder = 0x200,
        StiffArm = 0x400, NoCatch = 0x800, RunningPush = 0x1000,
        AnchorY = 0x10000, AnchorXZ = 0x20000, Secondary = 0x40000,
        Interpolating = 0x80000, AnimationDebug = 0x100000,
        TurnHack = 0x200000, Puppeteer = 0x400000, Puppet = 0x800000
    }

    public enum SourceMovementPhase
    {
        LiningUp = 0, PreSnap = 1, InPlay = 2, PlayOver = 3,
        GameOver = 4, PreKickoff = 5, Initializing = 6
    }

    public readonly struct SourceMovementTick
    {
        public long SourceTick { get; }
        public bool Execute { get; }

        public SourceMovementTick(long sourceTick, bool execute)
        {
            if (sourceTick < 0) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            SourceTick = sourceTick;
            Execute = execute;
        }
    }

    // Player fields plus the executing side's shared action counts; no alternative actor or animation clock.
    public struct SourceMovementState
    {
        public int PlayerId;
        public int Station;
        public SourceActorMode Mode;
        public SourceActorJob Job;
        public SourceMovementFlags Flags;
        public SourceMovementModes LowerMode;
        public SourceMovementModes UpperMode;
        public string LowerSequence;
        public string UpperSequence;
        public float X, Y, Z;
        public float VelocityX, VelocityY, VelocityZ;
        public float Friction;
        public int Heading, TargetHeading, UpperHeading, UpperTargetHeading;
        public int TurnRate, UpperTurnRate;
        public int UpperTurnHack, LowerVariableFrame, LowerVariableCount, UpperVariableFrame, UpperVariableCount;
        public int Stick, PreviousStick, StickTime;
        public SourceDroneButtons Buttons, OldButtons, NewButtons;
        public int NonTurboTime, NonATime, NonBTime;
        public int BackpedalExpireTime;
        public int AttackTime, AttackMode, CelebrateTime, OverrideTime, NoCatchTime;
        public int TeamSpinCount, TeamHurdleCount;
        public float OverrideSpeed;
        public int DroneFace;

        public int Side { get { return PlayerId / 7; } }
        public bool IsHuman { get { return Station != -1; } }

        public SourceMovementState(int playerId, int station, SourceActorMode mode)
        {
            this = default(SourceMovementState);
            PlayerId = playerId;
            Station = station;
            Mode = mode;
            Stick = PreviousStick = 24;
            LowerSequence = UpperSequence = "";
            Validate();
        }

        internal void Validate()
        {
            if (PlayerId < 0 || PlayerId > 13) throw new ArgumentOutOfRangeException(nameof(PlayerId));
            if (Station < -1 || Station > 3) throw new ArgumentOutOfRangeException(nameof(Station));
            if (Mode < SourceActorMode.Lineup || Mode > SourceActorMode.Kicker)
                throw new ArgumentOutOfRangeException(nameof(Mode));
            if (Stick < 0 || Stick > 48 || PreviousStick < 0 || PreviousStick > 48)
                throw new ArgumentOutOfRangeException(nameof(Stick));
            SourceMovementGuard.Finite(X, nameof(X)); SourceMovementGuard.Finite(Y, nameof(Y));
            SourceMovementGuard.Finite(Z, nameof(Z)); SourceMovementGuard.Finite(VelocityX, nameof(VelocityX));
            SourceMovementGuard.Finite(VelocityY, nameof(VelocityY)); SourceMovementGuard.Finite(VelocityZ, nameof(VelocityZ));
            SourceMovementGuard.Finite(Friction, nameof(Friction)); SourceMovementGuard.Finite(OverrideSpeed, nameof(OverrideSpeed));
            if (TurnRate < 0 || UpperTurnRate < 0) throw new ArgumentOutOfRangeException(nameof(TurnRate));
        }
    }

    public readonly struct SourceMovementSwitches
    {
        public int Stick { get; }
        public SourceDroneButtons Buttons { get; }
        public bool Backpedal { get; }
        public float StationTurbo { get; }

        public SourceMovementSwitches(int stick, SourceDroneButtons buttons, bool backpedal, float stationTurbo)
        {
            if (stick < 0 || stick > 48) throw new ArgumentOutOfRangeException(nameof(stick));
            SourceMovementGuard.Finite(stationTurbo, nameof(stationTurbo));
            if (stationTurbo < 0f || stationTurbo > 100f) throw new ArgumentOutOfRangeException(nameof(stationTurbo));
            Stick = stick; Buttons = buttons; Backpedal = backpedal; StationTurbo = stationTurbo;
        }
    }

    public readonly struct SourceMovementNeighbor
    {
        public int PlayerId { get; }
        public float Distance { get; }
        public int Bearing { get; }

        public SourceMovementNeighbor(int playerId, float distance, int bearing)
        {
            if (playerId < 0 || playerId > 13) throw new ArgumentOutOfRangeException(nameof(playerId));
            SourceMovementGuard.Finite(distance, nameof(distance));
            if (distance < 0f) throw new ArgumentOutOfRangeException(nameof(distance));
            // pmisc.c:bearing keeps signed remainder for source fwd[-1023,1023]/angle[0,1023].
            if (bearing < -1022 || bearing > 512) throw new ArgumentOutOfRangeException(nameof(bearing));
            PlayerId = playerId; Distance = distance; Bearing = bearing;
        }
    }

    // All values must come from the current source process, including its cached distxz/p2p_angles.
    public sealed class SourceMovementContext
    {
        public SourceMovementTick Tick { get; }
        public SourceDirectionMath Directions { get; }
        public int OffenseSide { get; }
        public SourceMovementPhase Phase { get; }
        public SourceGameFlags GameFlags { get; }
        public SourcePlayType PlayType { get; }
        public SourcePlayFlags OffensePlayFlags { get; }
        public SourcePlayFlags DefensePlayFlags { get; }
        public int Carrier { get; }
        public SourceActorMode CarrierMode { get; }
        public SourceBallType BallType { get; }
        public int IntendedReceiver { get; }
        public int BallTicksLeft { get; }
        public float BallX { get; }
        public float BallY { get; }
        public float BallDistance { get; }
        public int CameraHeading { get; }
        public int LineOfScrimmage { get; }
        public float LineZ { get; }
        public float MotionSnapX { get; }
        public int BackpedalTargetHeading { get; }
        public float TeamIndexedTurbo { get; }
        public bool DefenseFire { get; }
        public bool SpeedPowerup { get; }
        public bool FastTurboPowerup { get; }
        public bool RunOutOfBoundsPowerup { get; }
        public bool ViolencePowerup { get; }
        public bool CarrierLateHittable { get; }
        public float CarrierDistance { get; }
        public float BallTargetX { get; }
        public string DroneScript { get; }
        public bool DroneScriptFullBody { get; }
        public IReadOnlyList<SourceMovementNeighbor> Neighbors { get; }
        public int PlowProbability { get; }

        public SourceMovementContext(SourceMovementTick tick, SourceDirectionMath directions,
            int offenseSide, SourceMovementPhase phase, SourceGameFlags gameFlags, SourcePlayType playType,
            SourcePlayFlags offensePlayFlags, SourcePlayFlags defensePlayFlags, int carrier,
            SourceActorMode carrierMode, SourceBallType ballType, int intendedReceiver, int ballTicksLeft,
            float ballX, float ballY, float ballDistance, int cameraHeading, int lineOfScrimmage,
            float lineZ, float motionSnapX, int backpedalTargetHeading, float teamIndexedTurbo,
            bool defenseFire, bool speedPowerup, bool fastTurboPowerup, bool runOutOfBoundsPowerup,
            bool violencePowerup, bool carrierLateHittable, float carrierDistance, float ballTargetX,
            string droneScript, bool droneScriptFullBody, IEnumerable<SourceMovementNeighbor> neighbors,
            int plowProbability)
        {
            Directions = directions ?? throw new ArgumentNullException(nameof(directions));
            SourceMovementGuard.Side(offenseSide);
            if (carrier < -1 || carrier > 13 || intendedReceiver < -1 || intendedReceiver > 13)
                throw new ArgumentOutOfRangeException(nameof(carrier));
            if (phase < SourceMovementPhase.LiningUp || phase > SourceMovementPhase.Initializing)
                throw new ArgumentOutOfRangeException(nameof(phase));
            if (backpedalTargetHeading < -1 || backpedalTargetHeading > 1023)
                throw new ArgumentOutOfRangeException(nameof(backpedalTargetHeading));
            SourceMovementGuard.Finite(ballX, nameof(ballX)); SourceMovementGuard.Finite(ballY, nameof(ballY));
            SourceMovementGuard.Finite(ballDistance, nameof(ballDistance)); SourceMovementGuard.Finite(lineZ, nameof(lineZ));
            SourceMovementGuard.Finite(motionSnapX, nameof(motionSnapX));
            SourceMovementGuard.Finite(teamIndexedTurbo, nameof(teamIndexedTurbo));
            SourceMovementGuard.Finite(carrierDistance, nameof(carrierDistance)); SourceMovementGuard.Finite(ballTargetX, nameof(ballTargetX));
            if (teamIndexedTurbo < 0f || teamIndexedTurbo > 100f || plowProbability < 0 || plowProbability > 100)
                throw new ArgumentOutOfRangeException(nameof(teamIndexedTurbo));
            if (neighbors == null) throw new ArgumentNullException(nameof(neighbors));
            var copy = new List<SourceMovementNeighbor>(neighbors);
            if (copy.Count != 14) throw new ArgumentException("Supply all fourteen cached source neighbors in actor order.", nameof(neighbors));
            for (int i = 0; i < copy.Count; i++)
                if (copy[i].PlayerId != i) throw new ArgumentException("Neighbors must be source-index ordered.", nameof(neighbors));
            Tick = tick; OffenseSide = offenseSide; Phase = phase; GameFlags = gameFlags; PlayType = playType;
            OffensePlayFlags = offensePlayFlags; DefensePlayFlags = defensePlayFlags; Carrier = carrier;
            CarrierMode = carrierMode; BallType = ballType; IntendedReceiver = intendedReceiver;
            BallTicksLeft = ballTicksLeft; BallX = ballX; BallY = ballY; BallDistance = ballDistance;
            CameraHeading = cameraHeading; LineOfScrimmage = lineOfScrimmage; LineZ = lineZ;
            MotionSnapX = motionSnapX; BackpedalTargetHeading = backpedalTargetHeading;
            TeamIndexedTurbo = teamIndexedTurbo; DefenseFire = defenseFire; SpeedPowerup = speedPowerup;
            FastTurboPowerup = fastTurboPowerup; RunOutOfBoundsPowerup = runOutOfBoundsPowerup;
            ViolencePowerup = violencePowerup; CarrierLateHittable = carrierLateHittable;
            CarrierDistance = carrierDistance; BallTargetX = ballTargetX; DroneScript = droneScript;
            DroneScriptFullBody = droneScriptFullBody; Neighbors = copy.AsReadOnly(); PlowProbability = plowProbability;
        }
    }

    public sealed class SourceMovementData
    {
        private readonly Dictionary<string, float> constants;
        private readonly Dictionary<string, ReadOnlyCollection<string>> tables;

        public SourceMovementData(IDictionary<string, float> sourceConstants,
            IDictionary<string, IReadOnlyList<string>> sourceScriptTables)
        {
            if (sourceConstants == null || sourceScriptTables == null) throw new ArgumentNullException(nameof(sourceConstants));
            constants = new Dictionary<string, float>(sourceConstants, StringComparer.Ordinal);
            foreach (KeyValuePair<string, float> entry in constants) SourceMovementGuard.Finite(entry.Value, entry.Key);
            tables = new Dictionary<string, ReadOnlyCollection<string>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, IReadOnlyList<string>> entry in sourceScriptTables)
            {
                if (entry.Value == null || entry.Value.Count == 0) throw new ArgumentException("Empty source script table " + entry.Key + ".");
                var copy = new List<string>(entry.Value);
                foreach (string identity in copy)
                    if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("Missing source script identity in " + entry.Key + ".");
                tables.Add(entry.Key, copy.AsReadOnly());
            }
        }

        public float Constant(string identity)
        {
            float value;
            if (!constants.TryGetValue(identity, out value))
                throw new InvalidOperationException("Missing explicit converted movement constant " + identity + ".");
            return value;
        }

        public int Count(string table)
        {
            ReadOnlyCollection<string> values;
            if (!tables.TryGetValue(table, out values)) throw new InvalidOperationException("Missing explicit source selector table " + table + ".");
            return values.Count;
        }

        public string Script(string table, int index)
        {
            int count = Count(table);
            if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index), table + " selection outside original table.");
            return tables[table][index];
        }

        internal float Speed(int index)
        {
            if (index < 0 || index >= 66) throw new InvalidOperationException("Original standard_run speed index is outside its 66-entry table: " + index + ".");
            int mode = index / 6, variant = index % 6;
            string key;
            switch (mode)
            {
                case 2:
                case 3:
                    if (mode == 2 && (variant == 2 || variant == 3)) return 0f;
                    key = variant == 0 ? "LINE_SPEED" : variant == 1 ? "LINE_TURBO_SPEED"
                        : variant == 2 ? "HUMAN_DEF_SPEED" : variant == 3 ? "HUMAN_DEF_TURBO_SPEED"
                        : variant == 4 ? "LINE_BACKP_SPEED" : "LINE_BACKP_TURBO_SPEED";
                    break;
                case 6:
                    if (variant > 1) return 0f;
                    key = variant == 0 ? "WPNPS_SPEED" : "WPN_TURBO_SPEED";
                    break;
                case 7:
                    key = variant == 0 ? "WPN_SPEED" : variant == 1 ? "WPN_TURBO_SPEED"
                        : variant == 2 ? "CARRIER_SPEED" : variant == 3 ? "CARRIER_TURBO_SPEED"
                        : variant == 4 ? "WPN_BACKP_SPEED" : "WPN_BACKP_TURBO_SPEED";
                    break;
                case 8:
                case 9:
                    key = variant == 0 ? mode == 8 ? "DBPS_SPEED" : "DB_SPEED"
                        : variant == 1 ? mode == 8 ? "DBPS_TURBO_SPEED" : "DB_TURBO_SPEED"
                        : variant == 2 ? "HUMAN_DEF_SPEED" : variant == 3 ? "HUMAN_DEF_TURBO_SPEED"
                        : variant == 4 ? "DB_BACKP_SPEED" : "DB_BACKP_TURBO_SPEED";
                    break;
                case 10:
                    if (variant > 1) return 0f;
                    key = "WPNPS_SPEED";
                    break;
                default: return 0f;
            }
            return Constant(key);
        }
    }

    public enum SourceMovementCommandKind
    {
        FullBody, LowerBody, UpperBody, BeginTwoPart, AbortRoute,
        SpinSpeech, HurdleSpeech, StiffArmSpeech, ChangeWeaponPlayer
    }

    public readonly struct SourceMovementCommand
    {
        public SourceMovementCommandKind Kind { get; }
        public string Script { get; }
        public SourceMovementCommand(SourceMovementCommandKind kind, string script = null)
        {
            Kind = kind; Script = script;
        }
    }

    public readonly struct SourceMovementBoundaryResult
    {
        public SourceMovementState State { get; }
        public int RandomCalls { get; }
        public int Value { get; }
        public SourceMovementBoundaryResult(SourceMovementState state, int randomCalls, int value = 0)
        {
            if (randomCalls < 0) throw new ArgumentOutOfRangeException(nameof(randomCalls));
            State = state; RandomCalls = randomCalls; Value = value;
        }
    }

    // change_anim executes to a frame break immediately. Return the ACTUAL post-callback fields
    // and consumed RNG count, including any animation/audio draws, before the next selector runs.
    public interface ISourceMovementBoundary
    {
        SourceMovementBoundaryResult Apply(SourceMovementState state, SourceMovementCommand command,
            SourceRandomContext randomContext, ISourceRandom random);
    }

    public sealed class SourceMovementResult
    {
        public SourceMovementState State { get; }
        public IReadOnlyList<SourceMovementCommand> Commands { get; }
        public int RandomCalls { get; }
        // Null means this seam did not touch the caller's shared unique-breath mask.
        public uint? BreathControl { get; }
        internal SourceMovementResult(SourceMovementState state, List<SourceMovementCommand> commands,
            int randomCalls, uint? breathControl)
        {
            State = state; Commands = commands.AsReadOnly(); RandomCalls = randomCalls; BreathControl = breathControl;
        }
    }

    internal sealed class SourceMovementExecution
    {
        internal SourceMovementState State;
        internal readonly SourceMovementContext Context;
        internal readonly SourceMovementData Data;
        internal uint BreathControl;
        internal int RandomCalls;
        private readonly bool carriesBreathControl;
        private readonly ISourceMovementBoundary boundary;
        private readonly ISourceRandom random;
        private readonly SourceRandomContext randomContext;
        private readonly List<SourceMovementCommand> commands = new List<SourceMovementCommand>();

        internal SourceMovementExecution(SourceMovementState state, SourceMovementContext context,
            SourceMovementData data, ISourceMovementBoundary boundary, ISourceRandom random,
            SourceRandomContext randomContext, uint? breathControl = null)
        {
            state.Validate();
            Context = context ?? throw new ArgumentNullException(nameof(context));
            State = state; Data = data; this.boundary = boundary; this.random = random;
            this.randomContext = randomContext;
            carriesBreathControl = breathControl.HasValue; BreathControl = breathControl.GetValueOrDefault();
            if (context.Tick.Execute && (randomContext.SourceTick != context.Tick.SourceTick ||
                randomContext.ActorId != state.PlayerId))
                throw new ArgumentException("Movement RNG context must identify the actual source tick and actor.", nameof(randomContext));
        }

        internal int Draw(int bound, string identity)
        {
            if (random == null) throw new InvalidOperationException("Explicit source RNG required at " + identity + ".");
            SourceRandomSite site = randomContext.At(SourceRandomKind.RandRange, bound, identity, RandomCalls);
            SourceRandomDraw draw = random.Draw(site);
            if (!draw.Site.SameAs(site)) throw new InvalidOperationException("Movement RNG returned a different source site.");
            RandomCalls++;
            return draw.Value;
        }

        internal int Apply(SourceMovementCommandKind kind, string script = null)
        {
            if (boundary == null)
                throw new InvalidOperationException("Actual source animation/control boundary is required for " + kind + " " + script + ".");
            var command = new SourceMovementCommand(kind, script);
            commands.Add(command);
            var next = new SourceRandomContext(randomContext.SourceTick, randomContext.ActorId,
                randomContext.Process, randomContext.ProcessOrder, randomContext.Profile,
                checked(randomContext.FirstCallIndex + RandomCalls));
            SourceMovementBoundaryResult result = boundary.Apply(State, command, next, random);
            if (result.State.PlayerId != State.PlayerId)
                throw new InvalidOperationException("Movement callback changed the executing actor identity.");
            if (result.RandomCalls != 0 && random == null)
                throw new InvalidOperationException("Movement callback consumed source random draws without an explicit source RNG.");
            State = result.State;
            State.Validate();
            RandomCalls = checked(RandomCalls + result.RandomCalls);
            return result.Value;
        }

        internal void BeginTwoPart()
        {
            if ((State.UpperMode & SourceMovementModes.TwoPart) != 0) return;
            Apply(SourceMovementCommandKind.BeginTwoPart);
            if ((State.UpperMode & SourceMovementModes.TwoPart) == 0)
                throw new InvalidOperationException("The source begin_twopart_mode boundary did not copy the active lower layer into a two-part upper layer.");
        }

        internal string Pick(string table, string site)
        {
            if (Data == null) throw new InvalidOperationException("Missing explicit movement selector data for " + table + " at " + site + ".");
            return Data.Script(table, Draw(Data.Count(table), site));
        }

        internal SourceMovementResult Finish()
        {
            return new SourceMovementResult(State, commands, RandomCalls, carriesBreathControl ? (uint?)BreathControl : null);
        }
    }

    internal static class SourceMovementGuard
    {
        internal static void Finite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(name);
        }
        internal static void Side(int side)
        {
            if (side != 0 && side != 1) throw new ArgumentOutOfRangeException(nameof(side));
        }
    }
}
