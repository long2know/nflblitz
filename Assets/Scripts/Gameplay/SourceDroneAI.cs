using System;
using System.Collections.Generic;

namespace Blitz.Gameplay
{
    [Flags]
    public enum SourceDroneFlags
    {
        None = 0, RunningYards = 0x1, WaitingAnimation = 0x2, GhostActive = 0x4,
        Blitz = 0x8, Turbo = 0x10, CannedCatch = 0x20, Ordered = 0x40, Waiting = 0x80,
        Motion = 0x100, WillIntercept = 0x200, WillAttackNow = 0x400,
        WillAttackLater = 0x800, WillPosition = 0x1000, ReachedTarget = 0x2000,
        Improvising = 0x4000, SmartIntercept = 0x8000, CanDive = 0x10000,
        QuarterbackRun = 0x20000, CanCheapShot = 0x100000, CanEvade = 0x200000,
        Debug = 0x400000, RushWeak = 0x800000, ZonePreSnap = 0x1000000,
        DelayBlock = 0x2000000, ThrowNext = 0x4000000
    }

    // Logical A/B/C/D, not the cabinet's station-shifted switch register.
    [Flags]
    public enum SourceDroneButtons { None = 0, A = 1, B = 2, C = 4, D = 8 }

    [Flags]
    public enum SourceAiActorFlags
    {
        None = 0, LowerUninterruptible = 1, UpperUninterruptible = 2, Reacting = 4,
        Puppet = 8, Puppeteer = 16, RunningPush = 32, Throwing = 64, ReverseRoute = 128,
        DiveAttack = 256
    }

    public sealed class SourceDirectionMath
    {
        private readonly float[] sines;
        private readonly int[] angles49;
        private readonly int[] sortedAngles;
        private readonly int[] sortedSticks;
        public string TrigBlob { get; }
        public string StickBlob { get; }

        public SourceDirectionMath(IEnumerable<float> quarterSines, IEnumerable<int> directions49,
            string trigBlob, string stickBlob)
        {
            if (quarterSines == null || directions49 == null) throw new ArgumentNullException(nameof(quarterSines));
            TrigBlob = SourceP1Guard.Identity(trigBlob, nameof(trigBlob));
            StickBlob = SourceP1Guard.Identity(stickBlob, nameof(stickBlob));
            sines = new List<float>(quarterSines).ToArray();
            angles49 = new List<int>(directions49).ToArray();
            if (sines.Length != 257 || sines[0] != 0f || sines[256] != 1f)
                throw new ArgumentException("Supply the converted 257-entry source TRIGS.H quarter table.", nameof(quarterSines));
            for (int i = 0; i < sines.Length; i++)
            {
                if (float.IsNaN(sines[i]) || sines[i] < 0f || sines[i] > 1f ||
                    (i != 0 && sines[i] <= sines[i - 1]))
                    throw new ArgumentException("Invalid source quarter-sine table.", nameof(quarterSines));
            }
            if (angles49.Length != 49 || angles49[24] != -1)
                throw new ArgumentException("Supply player.c's explicit 49-way direction table.", nameof(directions49));
            var byAngle = new SortedDictionary<int, int>();
            for (int i = 0; i < 49; i++)
            {
                if (i == 24) continue;
                if (angles49[i] < 0 || angles49[i] > 1023) throw new ArgumentOutOfRangeException(nameof(directions49));
                int previous;
                if (!byAngle.TryGetValue(angles49[i], out previous)) byAngle.Add(angles49[i], i);
                else if (Math.Max(Math.Abs(i / 7 - 3), Math.Abs(i % 7 - 3)) >
                    Math.Max(Math.Abs(previous / 7 - 3), Math.Abs(previous % 7 - 3)))
                    byAngle[angles49[i]] = i;
            }
            if (byAngle.Count != 32 || !byAngle.ContainsKey(0))
                throw new ArgumentException("Expected the source 32 distinct peripheral headings.", nameof(directions49));
            sortedAngles = new int[33];
            sortedSticks = new int[33];
            int at = 0;
            foreach (KeyValuePair<int, int> entry in byAngle)
            {
                sortedAngles[at] = entry.Key;
                sortedSticks[at++] = entry.Value;
            }
            sortedAngles[32] = 1024;
            sortedSticks[32] = sortedSticks[0];
        }

        public float Sin(int heading)
        {
            int t = heading & 1023;
            if (t <= 256) return sines[t];
            if (t <= 512) return sines[512 - t];
            if (t <= 768) return -sines[t - 512];
            return -sines[1024 - t];
        }

        public float Cos(int heading) { return Sin((heading & 1023) + 256); }
        public SourcePlanar Offset(float distance, int heading)
        {
            SourceP1Guard.Coordinate(distance, nameof(distance));
            return new SourcePlanar(distance * Sin(heading), distance * Cos(heading));
        }

        public int Heading(SourcePlanar from, SourcePlanar to)
        {
            float dx = to.X - from.X, dz = to.Z - from.Z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.01f) return 0;
            float cosine = dz / length;
            if (cosine < -1f || cosine > 1f)
                throw new InvalidOperationException("Source ptangle/iacos out-of-range branch (source release hangs).");
            int angle = 256 - Asin(cosine);
            return (dx < 0f ? -angle : angle) & 1023;
        }

        private int Asin(float value)
        {
            bool negative = value < 0f;
            float x = negative ? -value : value;
            int low = 0, high = 255;
            while (low < high)
            {
                int mid = (low + high) / 2;
                if (sines[mid + 1] < x) low = mid + 1;
                else high = mid;
            }
            float ld = sines[low] - x, hd = sines[low + 1] - x;
            int result = hd * hd < ld * ld ? low + 1 : low;
            return negative ? -result : result;
        }

        public int DirectionToStick(int heading)
        {
            int direction = heading & 1023;
            for (int i = 0; i < 32; i++)
                if (Math.Abs(sortedAngles[i] - direction) < Math.Abs(sortedAngles[i + 1] - direction))
                    return sortedSticks[i];
            return sortedSticks[32];
        }

        public int StickHeading(int stick)
        {
            if (stick < 0 || stick > 48) throw new ArgumentOutOfRangeException(nameof(stick));
            return angles49[stick];
        }

        public SourcePlanar Movement(int stick, int cameraHeading)
        {
            int angle = StickHeading(stick);
            return angle < 0 ? default(SourcePlanar) : Offset(1f, angle + cameraHeading);
        }
    }

    public readonly struct SourceAiActor
    {
        public int Id { get; }
        public int Side { get { return Id / 7; } }
        public SourcePlanar Position { get; }
        public SourcePlanar Velocity { get; }
        public float Height { get; }
        public SourceActorMode Mode { get; }
        public SourceActorJob Job { get; }
        public int Target { get; }
        public int Facing { get; }
        public int Stick { get; }
        public int Station { get; }
        public bool IsHuman { get { return Station != -1; } }
        public SourceAiActorFlags Flags { get; }
        public int AttackTarget { get; }

        public SourceAiActor(int id, SourcePlanar position, SourcePlanar velocity, SourceActorMode mode,
            SourceActorJob job = SourceActorJob.None, int target = -1, SourceAiActorFlags flags = SourceAiActorFlags.None,
            int station = -1, int facing = 0, int stick = 24, int attackTarget = -1, float height = 0f)
        {
            SourceP1Guard.Actor(id);
            if (mode < SourceActorMode.Lineup || mode > SourceActorMode.Kicker) throw new ArgumentOutOfRangeException(nameof(mode));
            if (job < SourceActorJob.None || job > SourceActorJob.Zone) throw new ArgumentOutOfRangeException(nameof(job));
            if (target < -1 || target > 13 || attackTarget < -1 || attackTarget > 13)
                throw new ArgumentOutOfRangeException(nameof(target));
            if (station < -1 || station > 3) throw new ArgumentOutOfRangeException(nameof(station));
            if (facing < 0 || facing > 1023) throw new ArgumentOutOfRangeException(nameof(facing));
            if (stick < 0 || stick > 48) throw new ArgumentOutOfRangeException(nameof(stick));
            SourceP1Guard.Coordinate(height, nameof(height));
            Id = id; Position = position; Velocity = velocity; Mode = mode; Job = job; Target = target;
            Flags = flags; Station = station; Facing = facing; Stick = stick; AttackTarget = attackTarget; Height = height;
        }
    }

    public readonly struct SourceAiBall
    {
        public SourceBallType Type { get; }
        public SourcePlanar Position { get; }
        public SourcePlanar Velocity { get; }
        public SourcePlanar Target { get; }
        public float Height { get; }
        public float VerticalVelocity { get; }
        public int Carrier { get; }
        public int IntendedReceiver { get; }
        public int Thrower { get; }
        public int FlightTicks { get; }
        public int TotalTicks { get; }
        public bool Bounced { get; }

        public SourceAiBall(SourceBallType type, SourcePlanar position, SourcePlanar velocity, SourcePlanar target,
            int carrier, int intendedReceiver = -1, int thrower = -1, int flightTicks = 0, int totalTicks = 0,
            float height = 0f, float verticalVelocity = 0f, bool bounced = false)
        {
            if (carrier < -1 || carrier > 13 || intendedReceiver < -1 || intendedReceiver > 13 || thrower < -1 || thrower > 13)
                throw new ArgumentOutOfRangeException(nameof(carrier));
            if (flightTicks < 0 || totalTicks < 0 || flightTicks > 1000000 || totalTicks > 1000000)
                throw new ArgumentOutOfRangeException(nameof(flightTicks));
            if (type < SourceBallType.Pass || type > SourceBallType.Lateral) throw new ArgumentOutOfRangeException(nameof(type));
            SourceP1Guard.Coordinate(height, nameof(height));
            SourceP1Guard.Coordinate(verticalVelocity, nameof(verticalVelocity));
            Type = type; Position = position; Velocity = velocity; Target = target; Carrier = carrier;
            IntendedReceiver = intendedReceiver; Thrower = thrower; FlightTicks = flightTicks;
            TotalTicks = totalTicks; Height = height; VerticalVelocity = verticalVelocity; Bounced = bounced;
        }
    }

    public sealed class SourceAiFrame
    {
        private readonly float[,] distances = new float[14, 14];
        private readonly int[,] angles = new int[14, 14];
        public IReadOnlyList<SourceAiActor> Actors { get; }
        public SourceDirectionMath Directions { get; }
        public SourceRulesState Rules { get; }
        public SourceAiBall Ball { get; }
        public long SourceTick { get; }
        public long SnapTick { get; }
        public int CameraHeading { get; }
        public int HumanTeamMask { get; }
        public bool PlayOver { get; }
        public bool InPlay { get; }
        public SourceGameFlags GameFlags { get; }
        public bool OffenseFire { get; }
        public int FireReceiver { get; }
        public SourcePlayFlags OffensePlayFlags { get; }
        public SourcePlayFlags DefensePlayFlags { get; }
        public bool UsesSuppliedSourceGeometry { get; }

        public SourceAiFrame(IEnumerable<SourceAiActor> actors, SourceDirectionMath directions, SourceRulesState rules,
            SourceAiBall ball, long sourceTick, long snapTick = 0, int cameraHeading = 0, int humanTeamMask = 0,
            bool playOver = false, SourceGameFlags gameFlags = SourceGameFlags.None, bool offenseFire = false,
            int fireReceiver = -1, SourcePlayFlags offensePlayFlags = SourcePlayFlags.None,
            SourcePlayFlags defensePlayFlags = SourcePlayFlags.None, bool inPlay = true,
            float[,] sourceDistances = null, int[,] sourceAngles = null)
        {
            if (actors == null) throw new ArgumentNullException(nameof(actors));
            Directions = directions ?? throw new ArgumentNullException(nameof(directions));
            SourceP1Guard.Side(rules.OffenseSide);
            bool originalPatOrKickoffDown = rules.Down == -1 &&
                (rules.PlayType == SourcePlayType.ExtraPoint || rules.PlayType == SourcePlayType.Kickoff);
            if ((rules.Down < 0 && !originalPatOrKickoffDown) || rules.Down > 4 || rules.LineOfScrimmage < 0 || rules.LineOfScrimmage > 100 ||
                rules.FirstDown < 0 || rules.FirstDown > 110)
                throw new ArgumentOutOfRangeException(nameof(rules));
            if (sourceTick < 0 || snapTick < 0 || snapTick > sourceTick) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            if (cameraHeading < 0 || cameraHeading > 1023) throw new ArgumentOutOfRangeException(nameof(cameraHeading));
            if ((humanTeamMask & ~3) != 0) throw new ArgumentOutOfRangeException(nameof(humanTeamMask));
            if (fireReceiver < -1 || fireReceiver > 13) throw new ArgumentOutOfRangeException(nameof(fireReceiver));
            SourceAiActor[] copy = new List<SourceAiActor>(actors).ToArray();
            if (copy.Length != 14) throw new ArgumentException("Supply all fourteen source actors in source index order.", nameof(actors));
            for (int i = 0; i < 14; i++)
                if (copy[i].Id != i) throw new ArgumentException("Source actor index mismatch.", nameof(actors));
            Actors = Array.AsReadOnly(copy);
            Rules = rules; Ball = ball; SourceTick = sourceTick; SnapTick = snapTick; CameraHeading = cameraHeading;
            HumanTeamMask = humanTeamMask; PlayOver = playOver; InPlay = inPlay && !playOver; GameFlags = gameFlags;
            OffenseFire = offenseFire; FireReceiver = fireReceiver; OffensePlayFlags = offensePlayFlags;
            DefensePlayFlags = defensePlayFlags;
            if ((sourceDistances == null) != (sourceAngles == null))
                throw new ArgumentException("Supply both original cached geometry matrices, or neither.", nameof(sourceDistances));
            if (sourceDistances != null)
            {
                if (sourceDistances.GetLength(0) != 14 || sourceDistances.GetLength(1) != 14 ||
                    sourceAngles.GetLength(0) != 14 || sourceAngles.GetLength(1) != 14)
                    throw new ArgumentException("Source distance/angle matrices must be fourteen by fourteen.", nameof(sourceDistances));
                for (int i = 0; i < 14; i++)
                    for (int j = 0; j < 14; j++)
                    {
                        float distance = sourceDistances[i, j];
                        if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0f || distance > 4000000f ||
                            sourceAngles[i, j] < 0 || sourceAngles[i, j] > 1023)
                            throw new ArgumentOutOfRangeException(nameof(sourceDistances));
                        distances[i, j] = distance; angles[i, j] = sourceAngles[i, j];
                    }
                UsesSuppliedSourceGeometry = true;
                return;
            }
            for (int i = 0; i < 14; i++)
                for (int j = i + 1; j < 14; j++)
                {
                    distances[i, j] = distances[j, i] = copy[i].Position.DistanceTo(copy[j].Position);
                    angles[i, j] = directions.Heading(copy[i].Position, copy[j].Position);
                    // detect.c computes only the upper triangle, then the reciprocal, even for coincident actors.
                    angles[j, i] = (angles[i, j] + 512) & 1023;
                }
        }

        public float Distance(int from, int to) { SourceP1Guard.Actor(from); SourceP1Guard.Actor(to); return distances[from, to]; }
        public int Angle(int from, int to) { SourceP1Guard.Actor(from); SourceP1Guard.Actor(to); return angles[from, to]; }
        public int Bearing(int from, int to)
        {
            int bearing = (1024 + Actors[from].Facing - Angle(from, to)) % 1024;
            return bearing > 512 ? 1024 - bearing : bearing;
        }
        public bool IsHumanTeam(int side) { SourceP1Guard.Side(side); return (HumanTeamMask & (1 << side)) != 0; }
        public int FieldX(float x) { return SourcePlaybook.FieldX(x, Rules.OffenseSide); }
        public float WorldX(int yards) { return SourcePlaybook.WorldX(yards, Rules.OffenseSide); }
        public int StickToward(SourcePlanar from, SourcePlanar to)
        {
            return Directions.DirectionToStick(Directions.Heading(from, to) - CameraHeading);
        }
    }

    public sealed class SourceAiParameters
    {
        public SourcePlaybook Playbook { get; }
        public int Side0Handicap { get; }
        public int Side1Handicap { get; }
        public SourceAiParameters(SourcePlaybook playbook, int side0Handicap, int side1Handicap)
        {
            Playbook = playbook ?? throw new ArgumentNullException(nameof(playbook));
            if (side0Handicap < 0 || side0Handicap > 11 || side1Handicap < 0 || side1Handicap > 11)
                throw new ArgumentOutOfRangeException(nameof(side0Handicap));
            Side0Handicap = side0Handicap; Side1Handicap = side1Handicap;
        }
        public float Value(string table, int side)
        {
            SourceP1Guard.Side(side);
            return Playbook.HandicapValue(table, side == 0 ? Side0Handicap : Side1Handicap);
        }
        public int Int(string table, int side)
        {
            float value = Value(table, side);
            if (value != (int)value) throw new InvalidOperationException("Expected integer handicap field " + table + ".");
            return (int)value;
        }
        public int Level(int side) { SourceP1Guard.Side(side); return side == 0 ? Side0Handicap : Side1Handicap; }
    }

    public readonly struct SourceQuarterbackPlan
    {
        public float DropbackUnits { get; }
        public int GainSought { get; }
        public int RollDirection { get; }
        public int MotionDirection { get; }
        public bool HurdlePass { get; }
        public int RandomCalls { get; }
        public SourceQuarterbackPlan(float dropbackUnits, int gainSought, int rollDirection,
            int motionDirection, bool hurdlePass, int randomCalls = 0)
        {
            SourceP1Guard.Coordinate(dropbackUnits, nameof(dropbackUnits));
            if (dropbackUnits <= 0f) throw new ArgumentOutOfRangeException(nameof(dropbackUnits));
            if (rollDirection < -2 || rollDirection > 1 || motionDirection < -1 || motionDirection > 1)
                throw new ArgumentOutOfRangeException(nameof(rollDirection));
            DropbackUnits = dropbackUnits; GainSought = gainSought; RollDirection = rollDirection;
            MotionDirection = motionDirection; HurdlePass = hurdlePass; RandomCalls = randomCalls;
        }
        internal SourceQuarterbackPlan WithRoll(int direction)
        {
            return new SourceQuarterbackPlan(DropbackUnits, GainSought, direction, MotionDirection, HurdlePass, RandomCalls);
        }
    }

    public readonly struct SourceDroneSnapContext
    {
        public int ActiveStationMask { get; }
        public bool FourStationVersion { get; }
        public int OffenseAudibleAction { get; }
        public int DefenseAudibleAction { get; }
        public int MotionReceiverStick { get; }
        public SourceDroneSnapContext(int activeStationMask, bool fourStationVersion, int offenseAudibleAction,
            int defenseAudibleAction, int motionReceiverStick)
        {
            if ((activeStationMask & ~15) != 0) throw new ArgumentOutOfRangeException(nameof(activeStationMask));
            if (motionReceiverStick < 0 || motionReceiverStick > 48) throw new ArgumentOutOfRangeException(nameof(motionReceiverStick));
            ActiveStationMask = activeStationMask; FourStationVersion = fourStationVersion;
            OffenseAudibleAction = offenseAudibleAction; DefenseAudibleAction = defenseAudibleAction;
            MotionReceiverStick = motionReceiverStick;
        }
    }

    public enum SourceKickerStage { Other, PreSnap, PreKickoff, InPlay }
    public readonly struct SourceDroneKickerContext
    {
        public SourceKickerStage Stage { get; }
        public SourceDroneButtons CaptainButtons { get; }
        public SourceDroneKickerContext(SourceKickerStage stage, SourceDroneButtons captainButtons)
        {
            if (stage < SourceKickerStage.Other || stage > SourceKickerStage.InPlay) throw new ArgumentOutOfRangeException(nameof(stage));
            Stage = stage; CaptainButtons = captainButtons;
        }
    }

    public sealed class SourceDroneInitialization
    {
        public IReadOnlyList<SourceRouteState> States { get; internal set; }
        public IReadOnlyList<bool> UnblockableByActor { get; internal set; }
        public SourceQuarterbackPlan QuarterbackPlan { get; internal set; }
        public int RandomCalls { get; internal set; }
    }

    public readonly struct SourceCoverageAssignment
    {
        public SourceRouteState Defender { get; }
        public SourceRouteState Receiver { get; }
        internal SourceCoverageAssignment(SourceRouteState defender, SourceRouteState receiver)
        { Defender = defender; Receiver = receiver; }
    }

    public struct SourceRouteState
    {
        public SourceRouteProgram Program { get; internal set; }
        public int WordIndex { get; internal set; }
        public SourceDroneFlags Flags { get; internal set; }
        public int Stick { get; internal set; }
        public int OldStick { get; internal set; }
        public SourceDroneButtons Buttons { get; internal set; }
        public SourceDroneButtons OldButtons { get; internal set; }
        public int StickTicks { get; internal set; }
        public int ButtonTicks { get; internal set; }
        public int Target { get; internal set; }
        public SourceActorJob Job { get; internal set; }
        public SourcePlanar Mark { get; internal set; }
        public SourcePlanar Ghost { get; internal set; }
        public SourcePlanar GhostVelocity { get; internal set; }
        public int FakeTicks { get; internal set; }
        public int WaitTicks { get; internal set; }
        public int BlockTicks { get; internal set; }
        public int PassReactTicks { get; internal set; }
        public int CatchTicks { get; internal set; }
        public string PendingCatch { get; internal set; }
        public int EvadeTicks { get; internal set; }
        public int ThinkTicks { get; internal set; }
        public int BlitzTicks { get; internal set; }
        public int OrderTicks { get; internal set; }
        public int OrderStick { get; internal set; }
        public int MoveTicks { get; internal set; }
        public float ZoneLowX { get; internal set; }
        public float ZoneHighX { get; internal set; }
        public float ZoneLowZ { get; internal set; }
        public float ZoneHighZ { get; internal set; }
        public SourcePlanar ZonePoint { get; internal set; }
        public SourcePlanar ZoneVelocity { get; internal set; }
        public int FakeZoneTicks { get; internal set; }
        public int LastThrowTarget { get; internal set; }
        public SourceQuarterbackPlan QuarterbackPlan { get; internal set; }
        public long LastSourceTick { get; internal set; }
        public bool Ended { get; internal set; }

        public static SourceRouteState Begin(SourceRouteProgram program, int target = -1,
            SourceDroneFlags flags = SourceDroneFlags.None)
        {
            if (program == null) throw new ArgumentNullException(nameof(program));
            if (target < -1 || target > 13) throw new ArgumentOutOfRangeException(nameof(target));
            return new SourceRouteState { Program = program, Target = target, Flags = flags,
                Stick = 24, OldStick = 24, StickTicks = 1000, ButtonTicks = 1000,
                LastSourceTick = -1, LastThrowTarget = -1 };
        }
        public SourceRouteState WithTarget(int target)
        {
            if (target < -1 || target > 13) throw new ArgumentOutOfRangeException(nameof(target));
            var copy = this; copy.Target = target; return copy;
        }
        public SourceRouteState WithFlags(SourceDroneFlags flags) { var copy = this; copy.Flags = flags; return copy; }
        public SourceRouteState WithQuarterbackPlan(SourceQuarterbackPlan plan) { var copy = this; copy.QuarterbackPlan = plan; return copy; }
        public SourceRouteState WithBlitzTime(int ticks)
        {
            if (ticks < 0 || ticks > 1000000) throw new ArgumentOutOfRangeException(nameof(ticks));
            var copy = this; copy.BlitzTicks = ticks; return copy;
        }
        public SourceRouteState WithCatch(string script, int ticks)
        {
            SourceP1Guard.Identity(script, nameof(script));
            if (ticks < 0 || ticks > 1000000) throw new ArgumentOutOfRangeException(nameof(ticks));
            var copy = this; copy.PendingCatch = script; copy.CatchTicks = ticks;
            copy.Flags |= SourceDroneFlags.CannedCatch; return copy;
        }
        public SourceRouteState Apply(SourceDefenderEffect effect)
        {
            var copy = this;
            if (effect.Kind == SourceDefenderEffectKind.Fake) copy.FakeTicks = effect.Ticks;
            else
            {
                copy.OrderTicks = effect.Ticks; copy.OrderStick = effect.Stick;
                copy.Flags |= SourceDroneFlags.Ordered;
            }
            return copy;
        }
    }

    public enum SourceDefenderEffectKind { Fake, OrderedMovement }
    public readonly struct SourceDefenderEffect
    {
        public int ActorId { get; }
        public SourceDefenderEffectKind Kind { get; }
        public int Ticks { get; }
        public int Stick { get; }
        internal SourceDefenderEffect(int actor, SourceDefenderEffectKind kind, int ticks, int stick = 24)
        { ActorId = actor; Kind = kind; Ticks = ticks; Stick = stick; }
    }

    public sealed class SourceRouteInput
    {
        public SourceAiFrame Frame { get; }
        public int ActorId { get; }
        public SourceAiParameters Parameters { get; }
        public SourceRandomContext RandomContext { get; }
        public bool StickOnly { get; }
        public int MotionDirection { get; }
        public int QuarterbackStick { get; }
        public SourceDroneButtons HumanNewButtons { get; }
        public int HurdleCount { get; }
        public int SpinCount { get; }
        public bool DronesLose { get; }
        public bool DronesWinning { get; }
        public SourceBlockAssignments Blocking { get; }
        public IReadOnlyList<SourceReceiverAssessment> Receivers { get; }
        public IReadOnlyList<SourceRouteState> DroneStates { get; }
        public SourceFormationSlot? FormationSlot { get; }
        public int TargetFacing { get; }
        public SourceDroneSnapContext? SnapContext { get; }
        public SourceDroneKickerContext? KickerContext { get; }

        public SourceRouteInput(SourceAiFrame frame, int actorId, SourceAiParameters parameters,
            SourceRandomContext randomContext, bool stickOnly = false, int motionDirection = 1,
            int quarterbackStick = 24, SourceDroneButtons humanNewButtons = SourceDroneButtons.None,
            int hurdleCount = 0, int spinCount = 0, bool dronesLose = false, bool dronesWinning = false,
            SourceBlockAssignments blocking = null, IReadOnlyList<SourceReceiverAssessment> receivers = null,
            IReadOnlyList<SourceRouteState> droneStates = null, SourceFormationSlot? formationSlot = null,
            int targetFacing = -1, SourceDroneSnapContext? snapContext = null, SourceDroneKickerContext? kickerContext = null)
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            SourceP1Guard.Actor(actorId);
            if (randomContext.ActorId != actorId || randomContext.SourceTick != frame.SourceTick)
                throw new ArgumentException("RNG context must name the executing source actor/tick.", nameof(randomContext));
            if (motionDirection < -1 || motionDirection > 1 || quarterbackStick < 0 || quarterbackStick > 48)
                throw new ArgumentOutOfRangeException(nameof(motionDirection));
            if (hurdleCount < 0 || spinCount < 0) throw new ArgumentOutOfRangeException(nameof(hurdleCount));
            if (targetFacing < -1 || targetFacing > 1023) throw new ArgumentOutOfRangeException(nameof(targetFacing));
            if (droneStates != null && droneStates.Count != 14) throw new ArgumentException("Expected fourteen drone states.", nameof(droneStates));
            ActorId = actorId; RandomContext = randomContext; StickOnly = stickOnly; MotionDirection = motionDirection;
            QuarterbackStick = quarterbackStick; HumanNewButtons = humanNewButtons; HurdleCount = hurdleCount;
            SpinCount = spinCount; DronesLose = dronesLose; DronesWinning = dronesWinning; Blocking = blocking;
            Receivers = receivers == null ? null : Array.AsReadOnly(new List<SourceReceiverAssessment>(receivers).ToArray());
            DroneStates = droneStates == null ? null : Array.AsReadOnly(new List<SourceRouteState>(droneStates).ToArray());
            FormationSlot = formationSlot;
            TargetFacing = targetFacing == -1 ? frame.Actors[actorId].Facing : targetFacing;
            SnapContext = snapContext; KickerContext = kickerContext;
        }
    }

    public sealed class SourceRouteStep
    {
        public SourceRouteState State { get; internal set; }
        public SourcePlanar DesiredMovement { get; internal set; }
        public int Facing { get; internal set; }
        public string Script { get; internal set; }
        public bool WaveArm { get; internal set; }
        public bool Backpedal { get; internal set; }
        public int BackpedalTarget { get; internal set; }
        public int ThrowTarget { get; internal set; }
        public bool ResetNonAButtonTime { get; internal set; }
        public bool ForceNonTurboOneTick { get; internal set; }
        public bool CommandWritten { get; internal set; }
        public bool ButtonsWritten { get; internal set; }
        public int RandomCalls { get; internal set; }
        public string SourceBranch { get; internal set; }
        public IReadOnlyList<SourceDefenderEffect> DefenderEffects { get; internal set; }
    }

    public readonly struct SourcePursuitDecision
    {
        public bool WriteCommand { get; }
        public int Stick { get; }
        public SourcePlanar Aim { get; }
        public int InterceptTicks { get; }
        internal SourcePursuitDecision(bool write, int stick, SourcePlanar aim, int ticks)
        { WriteCommand = write; Stick = stick; Aim = aim; InterceptTicks = ticks; }
    }

    public sealed class SourceBlockAssignments
    {
        public int ProtectedActor { get; internal set; }
        public IReadOnlyList<int> Rushers { get; internal set; }
        public IReadOnlyList<int> Blockers { get; internal set; }
        public IReadOnlyList<int> UrgencyIndices { get; internal set; }
        public IReadOnlyList<int> RushTimes { get; internal set; }
        public IReadOnlyList<int> TargetsByActor { get; internal set; }
        public bool HasUnverifiedEqualTimeOrder { get; internal set; }
        internal int[,] InterceptTimes { get; set; }
    }

    public readonly struct SourceReceiverAssessment
    {
        public int ActorId { get; }
        public int Coverage { get; }
        public int LikelyYards { get; }
        public int Gain { get; }
        public float ThrowDistance { get; }
        public float BallTicks { get; }
        public SourcePlanar CatchPosition { get; }
        internal SourceReceiverAssessment(int actor, int coverage, int likelyYards, int gain,
            float distance, float ballTicks, SourcePlanar catchPosition)
        { ActorId = actor; Coverage = coverage; LikelyYards = likelyYards; Gain = gain;
            ThrowDistance = distance; BallTicks = ballTicks; CatchPosition = catchPosition; }
    }

    public static class SourceDroneAI
    {
        public const float UnitsPerYard = 7.8f;
        private const float FieldWidth = 182.85f;
        private const SourceAiActorFlags BlockedModes =
            SourceAiActorFlags.Reacting | SourceAiActorFlags.Puppet | SourceAiActorFlags.Puppeteer;

        private sealed class Execution
        {
            internal SourceRouteState State;
            internal readonly SourceRouteInput Input;
            internal readonly SourceRandomCursor Random;
            internal readonly SourceRouteStep Output;
            internal readonly List<SourceDefenderEffect> Effects = new List<SourceDefenderEffect>();
            internal SourceAiFrame Frame { get { return Input.Frame; } }
            internal SourceAiActor Actor { get { return Frame.Actors[Input.ActorId]; } }
            internal Execution(SourceRouteState state, SourceRouteInput input, ISourceRandom random)
            {
                State = state; Input = input; Random = new SourceRandomCursor(random, input.RandomContext);
                Output = new SourceRouteStep { Facing = -1, ThrowTarget = -1, BackpedalTarget = -1, CommandWritten = true };
            }
            internal void Aim(SourcePlanar point) { State.Stick = Frame.StickToward(Actor.Position, point); }
            internal void Hold() { State.Stick = 24; State.Buttons = SourceDroneButtons.None; }
            internal void Pursue(SourcePursuitDecision decision)
            {
                if (decision.WriteCommand) { State.Stick = decision.Stick; State.Buttons = SourceDroneButtons.None; }
            }
            internal InvalidOperationException Missing(string branch)
            {
                return new InvalidOperationException("Required source branch " + branch + " at actor " +
                    Input.ActorId + " sourceTick " + Frame.SourceTick + ", route " + State.Program.Identity + ".");
            }
        }

        public static SourceRouteStep StepRoute(SourceRouteState state, SourceRouteInput input, ISourceRandom random = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (state.Program == null) throw new ArgumentException("Begin the explicit source route first.", nameof(state));
            if (state.LastSourceTick >= input.Frame.SourceTick)
                throw new InvalidOperationException("A drone process must execute at most once per supplied source tick.");
            var e = new Execution(state, input, random);
            e.State.LastSourceTick = input.Frame.SourceTick;
            e.State.Job = e.Actor.Job;
            e.State.EvadeTicks = Math.Max(0, e.State.EvadeTicks - 1);
            if (e.State.BlitzTicks != 0 && e.Frame.InPlay && e.Actor.Side != e.Frame.Rules.OffenseSide &&
                --e.State.BlitzTicks == 0) e.State.Flags |= SourceDroneFlags.Blitz;
            if (--e.State.ThinkTicks >= 0)
            {
                e.State.StickTicks++;
                e.State.ButtonTicks++;
                e.Output.CommandWritten = false;
                e.Output.SourceBranch = "get_drone_switches:think_time";
            }
            else
            {
                Dispatch(e);
                Smooth(e);
            }
            e.Output.State = e.State;
            e.Output.ButtonsWritten = e.Output.CommandWritten && !input.StickOnly;
            e.Output.DesiredMovement = e.Frame.Directions.Movement(e.State.Stick, e.Frame.CameraHeading);
            e.Output.RandomCalls = e.Random.Count;
            e.Output.DefenderEffects = e.Effects.AsReadOnly();
            return e.Output;
        }

        private static bool IsLive(SourceAiFrame frame)
        {
            return !frame.PlayOver && (frame.GameFlags & SourceGameFlags.PlayEnding) == 0;
        }

        private static void Dispatch(Execution e)
        {
            e.Output.SourceBranch = "drone.c:" + e.Actor.Mode;
            switch (e.Actor.Mode)
            {
                case SourceActorMode.Lineup:
                case SourceActorMode.Dead:
                case SourceActorMode.LinePreSnap: e.Hold(); return;
                case SourceActorMode.ReceiverPreSnap: ReceiverPreSnap(e); return;
                case SourceActorMode.Receiver: Receiver(e); return;
                case SourceActorMode.DefensiveBackPreSnap: DefensePreSnap(e); return;
                case SourceActorMode.Quarterback: Quarterback(e); return;
                case SourceActorMode.DefensiveBack: DefensiveBack(e); return;
                case SourceActorMode.Line: Line(e); return;
                case SourceActorMode.QuarterbackPreSnap: QuarterbackPreSnap(e); return;
                case SourceActorMode.Kicker: Kicker(e); return;
                default: throw e.Missing("unknown source player mode");
            }
        }

        private static void QuarterbackPreSnap(Execution e)
        {
            if (!e.Input.SnapContext.HasValue) throw e.Missing("_drone_qbps:explicit station/audible snap context");
            SourceDroneSnapContext snap = e.Input.SnapContext.Value;
            e.State.Stick = 24;
            int humans = 0, max = snap.FourStationVersion ? 4 : 2;
            for (int i = 0; i < max; i++) if ((snap.ActiveStationMask & (1 << i)) != 0) humans++;
            if (humans == 1 && (snap.OffenseAudibleAction == 1 || snap.DefenseAudibleAction == 1)) return;
            if (e.Input.MotionDirection != 0 && snap.MotionReceiverStick != 24) return;
            if (((e.Frame.SourceTick % 30 != 0) && e.Input.MotionDirection == 0) || (e.Frame.SourceTick & 1) != 0)
                e.State.Buttons = SourceDroneButtons.None;
            else e.State.Buttons = SourceDroneButtons.B | SourceDroneButtons.C;
        }

        private static void Kicker(Execution e)
        {
            if (e.Frame.Rules.PlayType != SourcePlayType.Kickoff && (e.Frame.OffensePlayFlags & SourcePlayFlags.FieldGoal) == 0) return;
            if (!e.Input.KickerContext.HasValue) throw e.Missing("_drone_kicker:explicit captain/pre-kickoff context");
            switch (e.Input.KickerContext.Value.Stage)
            {
                case SourceKickerStage.PreSnap:
                    e.State.Stick = 24; e.State.Buttons = e.Input.KickerContext.Value.CaptainButtons; break;
                case SourceKickerStage.PreKickoff: e.Aim(e.Frame.Ball.Target); e.State.Buttons = SourceDroneButtons.None; break;
                case SourceKickerStage.InPlay:
                    if (e.Frame.Ball.Carrier != -1)
                    {
                        if (e.Frame.Ball.IntendedReceiver < 0) throw e.Missing("_drone_kicker:holder int_receiver=-1");
                        SourcePlanar holder = e.Frame.Actors[e.Frame.Ball.IntendedReceiver].Position;
                        e.Aim(new SourcePlanar(holder.X, holder.Z + (e.Frame.Rules.OffenseSide == 1 ? 11f : -11f)));
                        e.State.Buttons = SourceDroneButtons.None;
                    }
                    break;
                default: e.Hold(); break;
            }
        }

        private static void Smooth(Execution e)
        {
            int change = Math.Max(Math.Abs(e.State.Stick / 7 - e.State.OldStick / 7),
                Math.Abs(e.State.Stick % 7 - e.State.OldStick % 7));
            if (e.State.Stick == e.State.OldStick) e.State.StickTicks++;
            else if (e.State.StickTicks <= 30 && change <= 1 && e.Actor.Mode != SourceActorMode.Quarterback)
            { e.State.Stick = e.State.OldStick; e.State.StickTicks++; }
            else { e.State.OldStick = e.State.Stick; e.State.StickTicks = 0; }
            if (!e.Input.StickOnly)
            {
                if (e.State.Buttons == e.State.OldButtons) e.State.ButtonTicks++;
                else { e.State.OldButtons = e.State.Buttons; e.State.ButtonTicks = 0; }
            }
        }

        private static void ReceiverPreSnap(Execution e)
        {
            if ((e.State.Flags & SourceDroneFlags.Motion) != 0)
            {
                if (e.Frame.IsHumanTeam(e.Actor.Side))
                { e.State.Stick = e.Input.QuarterbackStick; e.State.Buttons = SourceDroneButtons.None; }
                else
                {
                    e.State.Stick = 24 + e.Input.MotionDirection * 3;
                    e.State.Buttons = e.State.Stick == 24 ? SourceDroneButtons.None : SourceDroneButtons.C;
                }
                return;
            }
            if (e.Frame.Rules.PlayType == SourcePlayType.Kickoff && e.Frame.Ball.Type == SourceBallType.Kickoff &&
                Math.Abs(e.Frame.Actors[e.Frame.Rules.OffenseSide * 7].Position.X) < Math.Abs(e.Actor.Position.X))
            {
                e.State.Stick = e.Frame.Directions.DirectionToStick((e.Frame.Rules.OffenseSide == 0 ? 256 : 768) - e.Frame.CameraHeading);
                e.State.Buttons = SourceDroneButtons.None;
                return;
            }
            e.Hold();
        }

        private static void Receiver(Execution e)
        {
            SourceAiFrame f = e.Frame;
            SourceAiBall ball = f.Ball;
            if (f.PlayOver) { e.Hold(); return; }
            if (ball.Carrier == e.Actor.Id) { Carrier(e); return; }
            if (f.Rules.PlayType == SourcePlayType.Kickoff && e.Actor.Side == f.Rules.InitialOffenseSide &&
                ball.Carrier == -1 && ball.Type == SourceBallType.Kickoff && !ball.Bounced)
            {
                e.Aim(new SourcePlanar(e.Actor.Position.X + (e.Actor.Side == 0 ? 10f : -10f), e.Actor.Position.Z));
                e.State.Buttons = SourceDroneButtons.C; return;
            }
            if ((e.Actor.Flags & SourceAiActorFlags.LowerUninterruptible) == 0 && f.IsHumanTeam(e.Actor.Side) &&
                ball.Type == SourceBallType.Pass && ball.Carrier == -1 && !e.Input.StickOnly &&
                ball.IntendedReceiver == e.Actor.Id && ball.TotalTicks - ball.FlightTicks < 50 &&
                (e.Input.HumanNewButtons & SourceDroneButtons.A) != 0)
            {
                e.Output.Script = "c_jmpint_anim";
                e.State.Buttons = SourceDroneButtons.D | ((e.State.Flags & SourceDroneFlags.Turbo) != 0 ? SourceDroneButtons.C : 0);
                return;
            }
            if (ball.Carrier == -1)
            {
                if (ball.FlightTicks == 1)
                {
                    if (ball.Type == SourceBallType.Pass)
                        e.State.PassReactTicks = 3 + e.Random.Range(24, "_drone_wpn:pass_react:0") +
                            e.Random.Range(24, "_drone_wpn:pass_react:1") + e.Random.Range(24, "_drone_wpn:pass_react:2");
                    if (ball.Type == SourceBallType.Fumble)
                        e.State.PassReactTicks = 3 + e.Random.Range(24, "_drone_wpn:fumble_react");
                    if (f.FieldX(ball.Target.X) < f.Rules.LineOfScrimmage && ball.Type != SourceBallType.Fumble)
                        e.State.PassReactTicks = 10000;
                    if (ball.IntendedReceiver == e.Actor.Id) e.State.PassReactTicks = 0;
                }
                else if (ball.FlightTicks > 1)
                {
                    if (ball.TotalTicks - ball.FlightTicks == e.State.CatchTicks && ball.IntendedReceiver == e.Actor.Id &&
                        (e.State.Flags & SourceDroneFlags.CannedCatch) != 0 && ball.Type == SourceBallType.Pass)
                    {
                        e.Output.Script = SourceP1Guard.Identity(e.State.PendingCatch, "pending catch");
                        e.State.Buttons = SourceDroneButtons.D | ((e.State.Flags & SourceDroneFlags.Turbo) != 0 ? SourceDroneButtons.C : 0);
                        e.State.Flags &= ~SourceDroneFlags.CannedCatch;
                        return;
                    }
                    if (e.State.PassReactTicks != 0) e.State.PassReactTicks = Math.Max(0, e.State.PassReactTicks - 1);
                    else
                    {
                        float distance = e.Actor.Position.DistanceTo(ball.Target);
                        if (distance < 2f)
                        {
                            e.Hold();
                            if ((e.State.Flags & SourceDroneFlags.CannedCatch) == 0 && f.FieldX(e.Actor.Position.X) > f.Rules.LineOfScrimmage)
                            {
                                e.State.Flags |= SourceDroneFlags.CannedCatch;
                                e.State.Job = SourceActorJob.Catch; e.State.CatchTicks = 10; e.State.PendingCatch = "c_no_vel1_anim";
                                return;
                            }
                        }
                        else
                        {
                            e.Aim(ball.Target);
                            float speed = (float)Math.Sqrt(ball.Velocity.X * ball.Velocity.X + ball.Velocity.Z * ball.Velocity.Z);
                            float remaining = ball.Position.DistanceTo(ball.Target);
                            float ballTime = remaining == 0f && speed == 0f ? 1000000f : remaining / speed;
                            e.State.Buttons = distance / 1.15f <= ballTime ? SourceDroneButtons.None : SourceDroneButtons.C;
                            e.State.Job = SourceActorJob.Catch; return;
                        }
                    }
                }
            }
            // C reads player_blocks[-1] here on some in-flight paths. Never emulate that undefined memory read.
            if (ball.Carrier >= 0 && f.Actors[ball.Carrier].Mode == SourceActorMode.Receiver)
            {
                if ((e.State.Flags & SourceDroneFlags.DelayBlock) != 0 && --e.State.BlockTicks <= 0)
                    e.State.Flags &= ~SourceDroneFlags.DelayBlock;
                if ((e.State.Flags & SourceDroneFlags.DelayBlock) == 0)
                {
                    e.State.Job = SourceActorJob.Block;
                    if (e.State.Target >= 0)
                    {
                        e.Output.Backpedal = true; e.Output.BackpedalTarget = e.State.Target;
                        e.Pursue(MoveToBlock(f, e.Actor.Id, e.State.Target, ball.Carrier, 1.15f));
                    }
                    else e.Hold();
                    return;
                }
            }
            InterpretReceiver(e);
            int nearest;
            if (NearestDefender(f, e.Actor.Id, out nearest) < 15.6f && !f.IsHumanTeam(e.Actor.Side) &&
                (e.Actor.Flags & SourceAiActorFlags.RunningPush) == 0 && f.Bearing(e.Actor.Id, nearest) <= 128)
                e.State.Buttons = SourceDroneButtons.B | SourceDroneButtons.C;
            if ((e.State.Flags & SourceDroneFlags.GhostActive) != 0)
                e.State.Ghost = new SourcePlanar(e.State.Ghost.X + e.State.GhostVelocity.X, e.State.Ghost.Z + e.State.GhostVelocity.Z);
        }

        private static void InterpretReceiver(Execution e)
        {
            if (e.State.Ended) { e.Hold(); return; }
            for (int budget = 0; budget < 4096; budget++)
            {
                SourceRouteProgram p = e.State.Program;
                int at = e.State.WordIndex, raw = p.WordAt(at);
                int arg = SourceRouteProgram.InstructionWidth(raw) > 1 ? p.WordAt(at + 1) : 0;
                switch ((SourceRouteOpcode)raw)
                {
                    case SourceRouteOpcode.End: e.State.Ended = true; e.Hold(); return;
                    case SourceRouteOpcode.NoOperation: e.State.WordIndex++; break;
                    case SourceRouteOpcode.Restart: e.State.WordIndex = 0; break;
                    case SourceRouteOpcode.Branch:
                        if (!p.IsInstruction(arg)) throw p.Error(at, "branch target is not an instruction boundary");
                        e.State.WordIndex = arg; break;
                    case SourceRouteOpcode.WaitSnap: e.State.WordIndex++; break;
                    case SourceRouteOpcode.DelayBlock:
                        e.State.Flags |= SourceDroneFlags.DelayBlock; e.State.BlockTicks = arg; e.State.WordIndex += 2; break;
                    case SourceRouteOpcode.Face:
                        e.State.Stick = RouteStick(e, arg);
                        e.State.OldStick = e.State.Stick; e.State.WordIndex += 2; break;
                    case SourceRouteOpcode.GotoLosPlus:
                        if (e.Frame.FieldX(e.Actor.Position.X) >= e.Frame.Rules.LineOfScrimmage + arg) e.State.WordIndex += 2;
                        else { e.State.Job = SourceActorJob.None; return; }
                        break;
                    case SourceRouteOpcode.RunYards:
                        if ((e.State.Flags & SourceDroneFlags.RunningYards) != 0)
                        {
                            if (e.State.Mark.DistanceTo(e.Actor.Position) < UnitsPerYard * arg) return;
                            e.State.Flags &= ~SourceDroneFlags.RunningYards; e.State.WordIndex += 2;
                        }
                        else
                        {
                            e.State.Flags |= SourceDroneFlags.RunningYards; e.State.Mark = e.Actor.Position;
                            e.State.Job = SourceActorJob.None; return;
                        }
                        break;
                    case SourceRouteOpcode.Improvise:
                        if ((e.State.Flags & SourceDroneFlags.Improvising) == 0)
                        { ChooseImprovisation(e); e.State.Flags |= SourceDroneFlags.Improvising; }
                        if (e.State.Mark.DistanceTo(e.Actor.Position) <= 2f) ChooseImprovisation(e);
                        e.Aim(e.State.Mark); e.State.Buttons = SourceDroneButtons.None; return;
                    case SourceRouteOpcode.Block:
                        e.Output.Backpedal = true; e.Output.BackpedalTarget = e.State.Target;
                        e.State.Buttons = SourceDroneButtons.None; e.State.Job = SourceActorJob.Block;
                        e.Pursue(MoveToBlock(e.Frame, e.Actor.Id, e.State.Target, e.Frame.Ball.Carrier, 1.15f)); return;
                    case SourceRouteOpcode.CoverTarget:
                    case SourceRouteOpcode.RushQuarterback: e.State.Job = SourceActorJob.Block; return;
                    case SourceRouteOpcode.Juke:
                    case SourceRouteOpcode.Spin:
                        if ((e.State.Flags & SourceDroneFlags.WaitingAnimation) != 0)
                        {
                            if ((e.Actor.Flags & SourceAiActorFlags.LowerUninterruptible) != 0) return;
                            e.State.WordIndex++; e.State.Flags &= ~SourceDroneFlags.WaitingAnimation;
                            e.State.Buttons = SourceDroneButtons.None;
                        }
                        else
                        {
                            int unused;
                            if (NearestDefender(e.Frame, e.Actor.Id, out unused) > (8f * 6f * (390f / 300f))) e.State.WordIndex++;
                            else
                            {
                                e.Output.Script = raw == (int)SourceRouteOpcode.Juke ? "juke_c_anim" : "spin_anim";
                                e.State.Buttons = SourceDroneButtons.D; e.State.Flags |= SourceDroneFlags.WaitingAnimation;
                                e.State.Job = SourceActorJob.None; return;
                            }
                        }
                        break;
                    case SourceRouteOpcode.FakeOut:
                        SellFake(e, arg, p.WordAt(at + 2), p.WordAt(at + 3)); e.State.WordIndex += 4; break;
                    case SourceRouteOpcode.TurboOn:
                        e.State.Flags |= SourceDroneFlags.Turbo; e.State.Buttons |= SourceDroneButtons.C; e.State.WordIndex++; break;
                    case SourceRouteOpcode.TurboOff:
                        e.State.Flags &= ~SourceDroneFlags.Turbo; e.State.Buttons &= ~SourceDroneButtons.C; e.State.WordIndex++; break;
                    case SourceRouteOpcode.WaveArm:
                        if ((e.State.Flags & SourceDroneFlags.WaitingAnimation) != 0)
                        {
                            if ((e.Actor.Flags & SourceAiActorFlags.LowerUninterruptible) != 0) { e.State.Job = SourceActorJob.None; return; }
                            e.State.WordIndex++; e.State.Flags &= ~SourceDroneFlags.WaitingAnimation;
                        }
                        else
                        {
                            int unused;
                            if (NearestDefender(e.Frame, e.Actor.Id, out unused) < (3f * 6f * (390f / 300f))) e.State.WordIndex++;
                            else
                            {
                                e.Output.WaveArm = true; e.State.Flags |= SourceDroneFlags.WaitingAnimation;
                                e.State.Job = SourceActorJob.None; return;
                            }
                        }
                        break;
                    case SourceRouteOpcode.MoveDefender:
                        if (!(e.Frame.Ball.Carrier == -1 && (e.Frame.GameFlags & SourceGameFlags.HikingBall) == 0) &&
                            !(e.Frame.Ball.Carrier >= 0 && e.Frame.Actors[e.Frame.Ball.Carrier].Mode == SourceActorMode.Receiver) &&
                            e.State.Target != -1)
                            e.Effects.Add(new SourceDefenderEffect(e.State.Target, SourceDefenderEffectKind.OrderedMovement,
                                p.WordAt(at + 2), RouteStick(e, arg)));
                        e.State.WordIndex += 3; break;
                    case SourceRouteOpcode.Wait:
                        if ((e.State.Flags & SourceDroneFlags.Waiting) != 0)
                        {
                            if (--e.State.WaitTicks != 0) return;
                            e.State.Flags &= ~SourceDroneFlags.Waiting; e.State.WordIndex += 2;
                        }
                        else
                        {
                            e.State.Flags |= SourceDroneFlags.Waiting; e.State.WaitTicks = arg; e.Hold(); return;
                        }
                        break;
                    default: throw p.Error(at, "opcode " + raw + " is not reachable in active _drone_wpn");
                }
            }
            throw e.State.Program.Error(e.State.WordIndex, "non-yielding branch/restart loop exceeded the instruction budget");
        }

        private static int RouteStick(Execution e, int relativeAngle)
        {
            if ((e.Actor.Flags & SourceAiActorFlags.ReverseRoute) != 0) relativeAngle = -relativeAngle;
            return e.Frame.Directions.DirectionToStick(relativeAngle + (e.Frame.Rules.OffenseSide == 0 ? 256 : 768) - e.Frame.CameraHeading);
        }

        private static void ChooseImprovisation(Execution e)
        {
            int min = Math.Max(e.Frame.Rules.LineOfScrimmage + 6, e.Frame.FieldX(e.Actor.Position.X) - 20);
            int max = Math.Min(e.Frame.Rules.LineOfScrimmage + 50, 108);
            // The original can pass zero/negative ranges after a very deep route.
            // C remainder keeps the nonnegative dividend's sign; do not clamp or swap these bounds.
            int yards = min + e.Random.Range(max - min + 1, "choose_wr_imp_point:x");
            float z = (float)Math.Sqrt(e.Random.Range((int)(FieldWidth * FieldWidth) + 1, "choose_wr_imp_point:z"));
            z = Math.Min(FieldWidth - UnitsPerYard, z);
            e.State.Mark = new SourcePlanar(e.Frame.WorldX(yards), e.Actor.Position.Z > 0f ? -z : z);
        }

        private static void SellFake(Execution e, int angle, int probability, int forcedTicks)
        {
            int target = e.State.Target, defense = 1 - e.Frame.Rules.OffenseSide;
            if (target < 0 || e.Frame.Actors[target].Job != SourceActorJob.Cover) return;
            int chance = (int)(probability * e.Input.Parameters.Value("bozo_cookie_ht", defense));
            if (e.Random.Range(100, "sell_fake:bite") > chance) return;
            int ticks = forcedTicks != 0 ? forcedTicks : e.Input.Parameters.Int("bozo_base_ht", defense) +
                e.Random.Range(e.Input.Parameters.Int("bozo_rand_ht", defense), "sell_fake:time");
            e.Effects.Add(new SourceDefenderEffect(target, SourceDefenderEffectKind.Fake, ticks));
            if ((e.Actor.Flags & SourceAiActorFlags.ReverseRoute) != 0) angle = -angle;
            angle += e.Frame.Rules.OffenseSide == 0 ? 256 : 768;
            e.State.Ghost = e.Actor.Position; e.State.GhostVelocity = e.Frame.Directions.Offset(1.15f, angle);
            e.State.Flags |= SourceDroneFlags.GhostActive;
        }

        public static float NearestDefender(SourceAiFrame frame, int actor, out int defender)
        {
            SourceP1Guard.Actor(actor);
            float min = 1000f; defender = -1;
            int start = (1 - frame.Actors[actor].Side) * 7 + 3;
            for (int i = start; i < start + 4; i++)
                if (frame.Distance(actor, i) < min) { min = frame.Distance(actor, i); defender = i; }
            return min;
        }

        public static SourceRouteState AbortRoute(SourceRouteState state, SourcePlaybook playbook)
        {
            if (playbook == null) throw new ArgumentNullException(nameof(playbook));
            state.Program = playbook.GetRoute("ds_improvise"); state.WordIndex = 0; state.Ended = false;
            return state;
        }

        public static SourceRouteState OnPossessionChanged(SourceRouteState state, int actorSide,
            int newOffenseSide, SourcePlaybook playbook)
        {
            if (playbook == null) throw new ArgumentNullException(nameof(playbook));
            SourceP1Guard.Side(actorSide); SourceP1Guard.Side(newOffenseSide);
            state.Program = playbook.GetRoute(actorSide == newOffenseSide ? "ds_block" : "db_rush_qb");
            state.WordIndex = 0; state.Ended = false; return state;
        }

        public static SourceCoverageAssignment AssignCoverage(int defenderActor, SourceRouteState defender,
            int receiverActor, SourceRouteState receiver, bool defendingTeamIsHuman,
            int lastReceiver, int consecutiveCompletions)
        {
            SourceP1Guard.Actor(defenderActor); SourceP1Guard.Actor(receiverActor);
            if (lastReceiver < -1 || lastReceiver > 13 || consecutiveCompletions < 0)
                throw new ArgumentOutOfRangeException(nameof(lastReceiver));
            defender.Target = receiverActor; receiver.Target = defenderActor;
            if (lastReceiver == receiverActor && consecutiveCompletions == 2 && !defendingTeamIsHuman)
                defender.Flags |= SourceDroneFlags.CanCheapShot;
            return new SourceCoverageAssignment(defender, receiver);
        }

        public static SourceRouteStep InitializePreSnapDecision(SourceRouteState state, SourceRouteInput input,
            ISourceRandom random = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (state.Program == null) throw new ArgumentException("Begin the explicit source route first.", nameof(state));
            var e = new Execution(state, input, random);
            e.State.Job = e.Actor.Job;
            Dispatch(e);
            e.Output.State = e.State;
            e.Output.CommandWritten = false; e.Output.ButtonsWritten = false;
            e.Output.RandomCalls = e.Random.Count;
            e.Output.DefenderEffects = e.Effects.AsReadOnly();
            return e.Output;
        }

        public static float InterceptTime(SourcePlanar pursuer, float pursuerSpeed, SourcePlanar target,
            SourcePlanar targetVelocity, float distanceAdjustment, bool highestRoot = false)
        {
            SourceP1Guard.Coordinate(pursuerSpeed, nameof(pursuerSpeed));
            SourceP1Guard.Coordinate(distanceAdjustment, nameof(distanceAdjustment));
            if (pursuerSpeed < 0f || distanceAdjustment < 0f) throw new ArgumentOutOfRangeException(nameof(pursuerSpeed));
            float dx = pursuer.X - target.X, dz = pursuer.Z - target.Z;
            float a = targetVelocity.X * targetVelocity.X + targetVelocity.Z * targetVelocity.Z - pursuerSpeed * pursuerSpeed;
            float b = -2f * (dx * targetVelocity.X + dz * targetVelocity.Z + pursuerSpeed * distanceAdjustment);
            float c = dx * dx + dz * dz - distanceAdjustment * distanceAdjustment;
            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f) return 100000f;
            if (c < 0.001f) return 0f;
            if (Math.Abs(b) < 0.001f && Math.Abs(a) < 0.001f) return 200000f;
            if (Math.Abs(a) < 0.001f) { float r = -c / b; return r < 0f ? 400000f : r; }
            if (Math.Abs(b) < 0.001f) return -c / a < 0f ? 300000f : (float)Math.Sqrt(-c / a);
            float sqrt = (float)Math.Sqrt(discriminant);
            float r1 = (-b + sqrt) / (2f * a), r2 = (-b - sqrt) / (2f * a);
            if (r1 < 0f && r2 < 0f) return 300000f;
            if (r1 >= 0f && r2 >= 0f) return highestRoot ? Math.Max(r1, r2) : Math.Min(r1, r2);
            return Math.Max(r1, r2);
        }

        public static int PursuitInterceptTicks(SourcePlanar pursuer, SourcePlanar target,
            SourcePlanar targetVelocity, float speed)
        {
            SourceP1Guard.Coordinate(speed, nameof(speed));
            if (speed < 0f) throw new ArgumentOutOfRangeException(nameof(speed));
            float dx = pursuer.X - target.X, dz = pursuer.Z - target.Z;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            if (distance < 0.01f) return 1;
            float adjusted = Math.Max(0f, distance - 8f);
            dx = dx / distance * adjusted; dz = dz / distance * adjusted;
            float a = targetVelocity.X * targetVelocity.X + targetVelocity.Z * targetVelocity.Z - speed * speed;
            float b = -2f * (targetVelocity.X * dx + targetVelocity.Z * dz);
            float c = dx * dx + dz * dz;
            if (Math.Abs(a) < 0.01f)
            {
                if (b >= -0.0001f) return 1000;
                return Math.Min(1000, (int)(-c / b));
            }
            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f) return 1000;
            float sqrt = (float)Math.Sqrt(discriminant);
            int r1 = (int)((-b + sqrt) / (2f * a)), r2 = (int)((-b - sqrt) / (2f * a));
            if (r1 < 0 && r2 < 0) return 1000;
            if (r1 < 0) return r2;
            if (r2 < 0) return r1;
            return Math.Min(r1, r2);
        }

        public static SourcePursuitDecision MoveToIntercept(SourceAiFrame frame, int attacker, int target, float speed)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            SourceP1Guard.Actor(attacker); SourceP1Guard.Actor(target);
            SourceAiActor a = frame.Actors[attacker], t = frame.Actors[target];
            int ticks = PursuitInterceptTicks(a.Position, t.Position, t.Velocity, speed);
            int aimTicks = ticks >= 1000 ? 0 : ticks;
            var aim = new SourcePlanar(t.Position.X + aimTicks * t.Velocity.X, t.Position.Z + aimTicks * t.Velocity.Z);
            return new SourcePursuitDecision(true, frame.StickToward(a.Position, aim), aim, ticks);
        }

        public static SourcePursuitDecision MoveToBlock(SourceAiFrame frame, int blocker, int rusher, int target, float speed)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (blocker == -1 || rusher == -1 || target == -1) return new SourcePursuitDecision(false, 24, default(SourcePlanar), 0);
            SourceP1Guard.Actor(blocker); SourceP1Guard.Actor(rusher); SourceP1Guard.Actor(target);
            float blockDistance = frame.Rules.PlayType == SourcePlayType.Kickoff ? 11f * UnitsPerYard : 7f * UnitsPerYard;
            float distance = frame.Distance(rusher, target);
            if (distance < blockDistance) return MoveToIntercept(frame, blocker, rusher, speed);
            SourcePlanar p = frame.Actors[target].Position, r = frame.Actors[rusher].Position;
            var aim = new SourcePlanar(p.X + blockDistance / distance * (r.X - p.X),
                p.Z + blockDistance / distance * (r.Z - p.Z));
            float remaining = frame.Actors[blocker].Position.DistanceTo(aim);
            int stick = (remaining < 9f && frame.Actors[blocker].Stick == 24) || remaining < 3f
                ? 24 : frame.StickToward(frame.Actors[blocker].Position, aim);
            return new SourcePursuitDecision(true, stick, aim, 0);
        }

        public static SourceBlockAssignments AssignBlockers(SourceAiFrame frame, Comparison<int> equalTimeActorOrder = null)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            int offense = frame.Rules.OffenseSide;
            int carrier = frame.Ball.Carrier >= 0 ? frame.Ball.Carrier
                : frame.Ball.IntendedReceiver >= 0 ? frame.Ball.IntendedReceiver : offense * 7 + 3;
            var rushers = new List<int>();
            var blockers = new List<int>();
            var targets = new int[14];
            for (int i = 0; i < 14; i++) targets[i] = frame.Actors[i].Target;
            for (int i = (1 - offense) * 7; i < (1 - offense) * 7 + 7; i++)
            {
                SourceAiActor actor = frame.Actors[i];
                if ((actor.Flags & BlockedModes) != 0) continue;
                if (actor.Job == SourceActorJob.Rush || frame.Actors[carrier].Mode == SourceActorMode.Receiver ||
                    (actor.Job == SourceActorJob.Cover && actor.Target == frame.Ball.Carrier) ||
                    (actor.IsHuman && frame.FieldX(actor.Position.X) < frame.Rules.LineOfScrimmage + 5))
                    rushers.Add(i);
            }
            for (int i = offense * 7; i < offense * 7 + 7; i++)
                if (i != carrier && (frame.Actors[i].Flags & SourceAiActorFlags.LowerUninterruptible) == 0 &&
                    frame.Actors[i].Job == SourceActorJob.Block) blockers.Add(i);
            var times = new int[rushers.Count];
            var urgency = new int[rushers.Count];
            var intTimes = new int[rushers.Count, blockers.Count];
            var weighted = new int[rushers.Count, blockers.Count];
            for (int i = 0; i < rushers.Count; i++)
            {
                SourceAiActor rusher = frame.Actors[rushers[i]], protectedActor = frame.Actors[carrier];
                float speed = Math.Max(rusher.Mode == SourceActorMode.Line ? 1.5f : 1.15f, Speed(rusher.Velocity));
                times[i] = (int)InterceptTime(rusher.Position, speed, protectedActor.Position, protectedActor.Velocity, 8f);
                if (times[i] >= 10000) times[i] += (int)frame.Distance(rushers[i], carrier);
                urgency[i] = i;
            }
            bool unverifiedTies = false;
            // qsort's comparator leaves equal times equal. An oracle may supply the target libc tie order.
            for (int i = 1; i < urgency.Length; i++)
            {
                int item = urgency[i], j = i - 1;
                while (j >= 0)
                {
                    int comparison = times[urgency[j]].CompareTo(times[item]);
                    if (comparison == 0)
                    {
                        if (equalTimeActorOrder == null) unverifiedTies = true;
                        else comparison = equalTimeActorOrder(rushers[urgency[j]], rushers[item]);
                    }
                    if (comparison <= 0) break;
                    urgency[j + 1] = urgency[j--];
                }
                urgency[j + 1] = item;
            }
            if (rushers.Count != 0 && blockers.Count != 0)
            {
                for (int i = 0; i < rushers.Count; i++)
                    for (int j = 0; j < blockers.Count; j++)
                    {
                        SourceAiActor blocker = frame.Actors[blockers[j]], rusher = frame.Actors[rushers[i]];
                        float speed = Math.Max(blocker.Mode == SourceActorMode.Line ? 1.5f : 1.15f, Speed(blocker.Velocity));
                        intTimes[i, j] = (int)InterceptTime(blocker.Position, speed, rusher.Position, rusher.Velocity, 8f);
                        weighted[i, j] = intTimes[i, j] - (blocker.Target == rusher.Id ? 10 : 0);
                    }
                for (int i = 0; i < rushers.Count; i++)
                    for (int j = 0; j < blockers.Count; j++)
                        weighted[urgency[i], j] = (int)(weighted[urgency[i], j] * ((9f - i) / 3f));
                var assignedIndices = new int[blockers.Count];
                for (int j = 0; j < blockers.Count; j++) assignedIndices[j] = urgency[j % rushers.Count];
                bool changed;
                do
                {
                    changed = false;
                    for (int i = 0; i < blockers.Count && !changed; i++)
                        for (int j = i + 1; j < blockers.Count && !changed; j++)
                        {
                            int a = assignedIndices[i], b = assignedIndices[j];
                            if (weighted[a, i] + weighted[b, j] <= weighted[b, i] + weighted[a, j]) continue;
                            assignedIndices[i] = b; assignedIndices[j] = a; changed = true;
                        }
                } while (changed);
                for (int j = 0; j < blockers.Count; j++) targets[blockers[j]] = rushers[assignedIndices[j]];
            }
            return new SourceBlockAssignments { ProtectedActor = carrier, Rushers = rushers.AsReadOnly(),
                Blockers = blockers.AsReadOnly(), UrgencyIndices = Array.AsReadOnly(urgency),
                RushTimes = Array.AsReadOnly(times), TargetsByActor = Array.AsReadOnly(targets),
                HasUnverifiedEqualTimeOrder = unverifiedTies, InterceptTimes = intTimes };
        }

        public static int LikelyGain(SourceAiFrame frame, int actor)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            SourceP1Guard.Actor(actor);
            if (frame.Actors[actor].Side != frame.Rules.OffenseSide) return 0;
            int result = 110 - frame.FieldX(frame.Actors[actor].Position.X);
            int first = (1 - frame.Rules.OffenseSide) * 7;
            for (int i = first; i < first + 7; i++)
            {
                int angle = (1024 + frame.Angle(actor, i)) % 1024;
                angle = angle > 512 ? 1024 - angle : angle;
                float factor = 0.5f + angle / 256f;
                result = Math.Min(result, (int)(factor * frame.Distance(actor, i) / UnitsPerYard));
            }
            return result;
        }

        public static IReadOnlyList<SourceReceiverAssessment> UpdateReceiverData(SourceAiFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            var result = new List<SourceReceiverAssessment>();
            if (frame.IsHumanTeam(frame.Rules.OffenseSide) || frame.Ball.Carrier < 0 ||
                frame.Actors[frame.Ball.Carrier].Mode != SourceActorMode.Quarterback) return result.AsReadOnly();
            int offense = frame.Rules.OffenseSide, first = offense * 7;
            var defenders = new List<int>();
            for (int i = (1 - offense) * 7; i < (1 - offense) * 7 + 7; i++)
                if (frame.Actors[i].IsHuman || frame.Actors[i].Job == SourceActorJob.Cover || frame.Actors[i].Job == SourceActorJob.Zone)
                    defenders.Add(i);
            float throwSpeed = frame.OffenseFire ? 7.5f : 5.7f;
            int fireman = frame.OffenseFire ? frame.FireReceiver : -1;
            for (int i = first; i < first + 7; i++)
            {
                SourceAiActor receiver = frame.Actors[i];
                if (receiver.Job == SourceActorJob.Block || receiver.Mode != SourceActorMode.Receiver || i == frame.Ball.Carrier) continue;
                float distance = frame.Distance(i, frame.Ball.Carrier), ticks = distance / throwSpeed;
                var catchPoint = new SourcePlanar(receiver.Position.X + ticks * receiver.Velocity.X,
                    receiver.Position.Z + ticks * receiver.Velocity.Z);
                int coverage = 0;
                foreach (int defender in defenders)
                {
                    float arrive = Math.Max(0f, frame.Actors[defender].Position.DistanceTo(catchPoint) - 12f) / 1.85f;
                    if (i == fireman) arrive += 10f;
                    if (arrive <= ticks) coverage++;
                }
                int yards = coverage != 0 ? 0 : LikelyGain(frame, i) + (i == fireman ? 10 : 0);
                result.Add(new SourceReceiverAssessment(i, coverage, yards,
                    frame.FieldX(receiver.Position.X) - frame.Rules.LineOfScrimmage + yards, distance, ticks, catchPoint));
            }
            return result.AsReadOnly();
        }

        // Active USE_OLD_QB=FALSE compares requested 'yards', not candidate gain, on its second comparison.
        public static int FindReceiver(IReadOnlyList<SourceReceiverAssessment> candidates, int cover, int yards)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            int best = -1, bestYards = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                SourceReceiverAssessment receiver = candidates[i];
                if (yards == -1 || receiver.Gain < yards || receiver.Coverage > cover ||
                    receiver.ThrowDistance <= UnitsPerYard * 5f) continue;
                if (best == -1 || yards > bestYards) { best = i; bestYards = yards; }
            }
            return best;
        }

        public static SourceQuarterbackPlan InitializeQuarterback(SourceAiFrame frame, SourceRandomContext context,
            ISourceRandom random)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (context.SourceTick != frame.SourceTick) throw new ArgumentException("Initialization tick mismatch.", nameof(context));
            var calls = new SourceRandomCursor(random, context);
            return InitializeQuarterback(frame, calls);
        }

        private static SourceQuarterbackPlan InitializeQuarterback(SourceAiFrame frame, SourceRandomCursor calls)
        {
            float drop = calls.Range(4, "init_drones_postlineup:qb_dropback") != 0 || frame.Rules.LineOfScrimmage <= 20
                ? 8f * UnitsPerYard : 12f * UnitsPerYard;
            calls.Range(3, "init_drones_postlineup:motion_dir"); // Shipping source immediately overwrites this with +1.
            int gain = Math.Min(20, 1 + (frame.Rules.FirstDown - frame.Rules.LineOfScrimmage) /
                (5 - Math.Min(4, frame.Rules.Down + 1)));
            bool hurdle = calls.Range(3, "init_drones_postlineup:qb_hurdle") == 0;
            return new SourceQuarterbackPlan(drop, gain, -2, 1, hurdle, calls.Count);
        }

        public static SourceDroneInitialization InitializePreLineup(SourceAiFrame frame, SourcePlayDefinition offense,
            SourcePlayDefinition defense, SourceAiParameters parameters, int quarterIndex,
            SourceRandomContext context, ISourceRandom random)
        {
            if (frame == null || offense == null || defense == null || parameters == null) throw new ArgumentNullException(nameof(frame));
            if (quarterIndex < 0 || quarterIndex > 6) throw new ArgumentOutOfRangeException(nameof(quarterIndex));
            if (context.SourceTick != frame.SourceTick) throw new ArgumentException("Initialization tick mismatch.", nameof(context));
            var calls = new SourceRandomCursor(random, context);
            var states = new SourceRouteState[14];
            int defenseSide = 1 - frame.Rules.OffenseSide;
            int baseTime = parameters.Int("blitz_time_ht", defenseSide);
            for (int i = 0; i < 14; i++)
            {
                int side = i / 7;
                SourcePlayDefinition play = side == frame.Rules.OffenseSide ? offense : defense;
                SourceDroneFlags flags = SourceDroneFlags.None;
                int blitzTime = 0;
                if ((side == defenseSide && frame.Rules.PlayType != SourcePlayType.Kickoff) || !frame.IsHumanTeam(side))
                {
                    blitzTime = 3 * baseTime + calls.Range(3 * baseTime, "init_drones_prelineup:blitz_time:" + i);
                    if ((play.Flags & SourcePlayFlags.Blitz) != 0)
                        blitzTime = quarterIndex != 0
                            ? 1 + calls.Range(baseTime, "init_drones_prelineup:blitz_play:" + i)
                            : 20 + calls.Range(20 + baseTime, "init_drones_prelineup:blitz_play_q0:" + i);
                }
                if ((play.Formation.Slots[i % 7].Control & SourceFormationControl.Motion) != 0) flags |= SourceDroneFlags.Motion;
                if (calls.Range(2, "init_drones_prelineup:smart_int:" + i) != 0) flags |= SourceDroneFlags.SmartIntercept;
                if ((play.Flags & SourcePlayFlags.Blitz) != 0 &&
                    calls.Range(100, "init_drones_prelineup:blitz_cheap:" + i) < parameters.Int("db_cheapshot_blitz_ht", side))
                    flags |= SourceDroneFlags.CanCheapShot;
                if (frame.IsHumanTeam(side))
                {
                    if (calls.Range(100, "init_drones_prelineup:human_dive:" + i) < parameters.Int("def_hteam_dive_ht", side))
                        flags |= SourceDroneFlags.CanDive;
                }
                else if (side == defenseSide)
                {
                    if (calls.Range(100, "init_drones_prelineup:drone_dive:" + i) < parameters.Int("def_drone_dive_ht", side))
                        flags |= SourceDroneFlags.CanDive;
                    if ((play.Flags & SourcePlayFlags.Blitz) == 0 &&
                        calls.Range(100, "init_drones_prelineup:drone_cheap:" + i) < parameters.Int("db_cheapshot_ht", side))
                        flags |= SourceDroneFlags.CanCheapShot;
                }
                if ((defense.Flags & SourcePlayFlags.CheapShot) != 0 &&
                    calls.Range(100, "init_drones_prelineup:play_cheap:" + i) < 35)
                    flags |= SourceDroneFlags.CanCheapShot | SourceDroneFlags.CanDive;
                if ((defense.Flags & SourcePlayFlags.SmartIntercept) != 0) flags |= SourceDroneFlags.SmartIntercept;
                states[i] = SourceRouteState.Begin(play.Routes[i % 7], -1, flags).WithBlitzTime(blitzTime);
            }
            return new SourceDroneInitialization { States = Array.AsReadOnly(states), RandomCalls = calls.Count };
        }

        // Run the defensive pre-snap script initialization BEFORE this source post-lineup random phase.
        public static SourceDroneInitialization InitializePostLineupRandom(SourceAiFrame frame, SourceAiParameters parameters,
            SourceRandomContext context, ISourceRandom random)
        {
            if (frame == null || parameters == null) throw new ArgumentNullException(nameof(frame));
            if (context.SourceTick != frame.SourceTick) throw new ArgumentException("Initialization tick mismatch.", nameof(context));
            var calls = new SourceRandomCursor(random, context);
            var unblockable = new bool[14];
            for (int i = 0; i < 14; i++)
                if (!frame.IsHumanTeam(i / 7) && calls.Range(100, "init_drones_postlineup:superman:" + i) <
                    parameters.Int("def_superman_ht", i / 7)) unblockable[i] = true;
            SourceQuarterbackPlan plan = InitializeQuarterback(frame, calls);
            return new SourceDroneInitialization { UnblockableByActor = Array.AsReadOnly(unblockable),
                QuarterbackPlan = plan, RandomCalls = calls.Count };
        }

        public static SourceRouteStep DecideQuarterback(SourceRouteState state, SourceRouteInput input, ISourceRandom random = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.Frame.Actors[input.ActorId].Mode != SourceActorMode.Quarterback)
                throw new ArgumentException("Expected active source MODE_QB.", nameof(input));
            return StepRoute(state, input, random);
        }

        private static int ChooseRoll(Execution e)
        {
            if (e.Random.Range(10, "choose_roll_dir:random_override") == 0)
                return e.Random.Range(3, "choose_roll_dir:override_side") - 1;
            int left = 0, right = 0, leftHuman = 0, rightHuman = 0;
            float factor = e.Frame.Rules.OffenseSide == 0 ? 1f : -1f;
            int first = (1 - e.Frame.Rules.OffenseSide) * 7;
            for (int i = first; i < first + 7; i++)
            {
                SourceAiActor defender = e.Frame.Actors[i];
                if (defender.Mode != SourceActorMode.DefensiveBackPreSnap) continue;
                if (defender.Position.Z * factor < -3.9f)
                {
                    left++;
                    if (defender.IsHuman && e.Frame.FieldX(defender.Position.X) <
                        e.Frame.Rules.LineOfScrimmage + 1 + e.Random.Range(5, "choose_roll_dir:left_human:" + i)) leftHuman++;
                }
                else if (defender.Position.Z * factor > 3.9f)
                {
                    right++;
                    if (defender.IsHuman && e.Frame.FieldX(defender.Position.X) <
                        e.Frame.Rules.LineOfScrimmage + 1 + e.Random.Range(5, "choose_roll_dir:right_human:" + i)) rightHuman++;
                }
            }
            if (leftHuman != rightHuman) return leftHuman > rightHuman ? 1 : -1;
            if (left != right) return left > right ? 1 : -1;
            return e.Random.Range(3, "choose_roll_dir:tie") - 1;
        }

        private static void Quarterback(Execution e)
        {
            if (e.State.QuarterbackPlan.DropbackUnits <= 0f)
                throw e.Missing("init_drones_postlineup:explicit quarterback plan");
            if (e.State.QuarterbackPlan.RollDirection == -2)
                e.State.QuarterbackPlan = e.State.QuarterbackPlan.WithRoll(ChooseRoll(e));
            if ((e.State.Flags & SourceDroneFlags.ThrowNext) != 0)
            {
                e.State.Flags &= ~SourceDroneFlags.ThrowNext; e.State.Stick = 24;
                e.State.Buttons = SourceDroneButtons.B | SourceDroneButtons.C; return;
            }
            if ((e.Actor.Flags & SourceAiActorFlags.Throwing) != 0) return;
            if (!IsLive(e.Frame) || e.Frame.Rules.PlayType == SourcePlayType.Kickoff) { e.Hold(); return; }
            if (e.Frame.SourceTick - e.Frame.SnapTick < 15)
            {
                e.State.Stick = e.Frame.Directions.DirectionToStick((e.Actor.Side == 0 ? 768 : 256) +
                    128 * e.State.QuarterbackPlan.RollDirection - e.Frame.CameraHeading);
                e.State.StickTicks = 60; e.State.Buttons = SourceDroneButtons.C; return;
            }
            SourceBlockAssignments blocks = e.Input.Blocking ?? AssignBlockers(e.Frame);
            IReadOnlyList<SourceReceiverAssessment> receivers = e.Input.Receivers ?? UpdateReceiverData(e.Frame);
            int rushYards = LikelyGain(e.Frame, e.Actor.Id);
            if (rushYards >= e.State.QuarterbackPlan.GainSought) e.State.Flags |= SourceDroneFlags.QuarterbackRun;
            if ((e.State.Flags & SourceDroneFlags.QuarterbackRun) != 0)
            {
                int candidate = FindReceiver(receivers, 0, rushYards + 1);
                if (candidate >= 0) { ThrowAt(e, receivers[candidate].ActorId); return; }
            }
            float friction = Math.Max(20, 60 - e.State.StickTicks * 2);
            float ffactor = 1f - 0.01f * Math.Max(0, 20 - e.State.StickTicks);
            if ((e.Actor.Flags & SourceAiActorFlags.LowerUninterruptible) == 0)
            {
                int diver = FindDiver(e.Frame, e.Actor.Id);
                int attacker = diver == -1 ? FindAttacker(e.Frame, e.Actor.Id, blocks) : -1;
                if (diver != -1 || attacker != -1)
                {
                    // Shipping code passes attacker (-1 when diver exists), not enemy, to evade_dir.
                    if (attacker == -1) throw e.Missing("_drone_qb:diver -> evade_dir(attacker=-1), undefined C p2p_angles read");
                    e.State.Stick = EvadeDirection(e, attacker);
                    int one = e.Random.Range(1, "_drone_qb:evade_kind");
                    int enemy = diver == -1 ? attacker : diver;
                    if (one != 0 || enemy == attacker)
                    {
                        if ((e.State.Flags & SourceDroneFlags.ThrowNext) == 0)
                        { e.State.Buttons = SourceDroneButtons.A | SourceDroneButtons.C; e.Output.ResetNonAButtonTime = true; }
                    }
                    else { e.State.Buttons = SourceDroneButtons.D; e.Output.Script = "spin_anim"; return; }
                }
            }
            if ((e.State.Flags & SourceDroneFlags.QuarterbackRun) != 0)
            {
                MinimizeUnhappiness(e, 1.6f, 1.45f, 1f, friction, friction / 2f,
                    (point, stick) => CarrierUnhappiness(e.Frame, e.Actor.Id, point));
                e.State.Buttons |= SourceDroneButtons.C; return;
            }
            MinimizeUnhappiness(e, 1.6f, 1.45f, ffactor, friction, friction / 2f,
                (point, stick) => QuarterbackUnhappiness(e, blocks, point));
            e.State.Buttons = SourceDroneButtons.C;
            int handicap = e.Input.Parameters.Level(e.Actor.Side);
            float panicDistance = UnitsPerYard * (handicap < 6 ? 4f : handicap < 10 ? 5f : 6f);
            int best;
            if (blocks.Rushers.Count != 0 && e.Frame.Distance(e.Actor.Id,
                blocks.Rushers[blocks.UrgencyIndices[0]]) < panicDistance)
            {
                bool desperate = e.Frame.Rules.Down == 4 || e.Frame.FieldX(e.Actor.Position.X) <= 0;
                best = FindReceiver(receivers, 0, 0);
                if (best == -1 && desperate) best = FindReceiver(receivers, 1, 0);
                if (best == -1 && desperate) best = FindReceiver(receivers, 10, 0);
                if (best == -1) { e.State.Flags |= SourceDroneFlags.QuarterbackRun; return; }
            }
            else best = FindReceiver(receivers, 0, e.State.QuarterbackPlan.GainSought);
            if (best >= 0) ThrowAt(e, receivers[best].ActorId);
        }

        private static void ThrowAt(Execution e, int target)
        {
            e.State.LastThrowTarget = target;
            e.Output.ThrowTarget = target;
            int unused;
            if (NearestDefender(e.Frame, e.Actor.Id, out unused) <= 25f || e.State.QuarterbackPlan.HurdlePass)
            {
                e.State.Buttons = SourceDroneButtons.A | SourceDroneButtons.C;
                e.State.Flags |= SourceDroneFlags.ThrowNext; e.Output.ResetNonAButtonTime = true;
            }
            else e.State.Buttons = SourceDroneButtons.B | SourceDroneButtons.C;
        }

        private static int FindDiver(SourceAiFrame frame, int actor)
        {
            int best = -1, start = (1 - frame.Actors[actor].Side) * 7;
            for (int i = start; i < start + 7; i++)
                if ((frame.Actors[i].Flags & SourceAiActorFlags.DiveAttack) != 0 &&
                    frame.Actors[i].AttackTarget == actor && frame.Distance(actor, i) <= 40f &&
                    (best == -1 || frame.Distance(actor, i) < frame.Distance(actor, best))) best = i;
            return best;
        }

        private static int FindAttacker(SourceAiFrame frame, int actor, SourceBlockAssignments blocks)
        {
            int best = -1;
            for (int i = 0; i < blocks.Rushers.Count; i++)
            {
                if (frame.Distance(actor, blocks.Rushers[i]) > 20f ||
                    (frame.Actors[blocks.Rushers[i]].Flags & SourceAiActorFlags.LowerUninterruptible) != 0) continue;
                // active update_receiver_data never writes the file-static blocked[]; it stays zero.
                if (best == -1 || frame.Distance(actor, blocks.Rushers[i]) < frame.Distance(actor, best)) best = i;
            }
            return best; // Source returns a rusher INDEX, not the actor id; retain that quirk.
        }

        private static int EvadeDirection(Execution e, int attacker)
        {
            int angle = e.Frame.Angle(e.Actor.Id, attacker);
            int d1 = angle + 384, d2 = angle - 384;
            SourcePlanar a = e.Frame.Directions.Offset(5f, d1), b = e.Frame.Directions.Offset(5f, d2);
            bool ok1 = Math.Abs(e.Actor.Position.X + a.X) <= 390f && Math.Abs(e.Actor.Position.Z + a.Z) <= FieldWidth;
            bool ok2 = Math.Abs(e.Actor.Position.X + b.X) <= 390f && Math.Abs(e.Actor.Position.Z + b.Z) <= FieldWidth;
            if (ok1 && !ok2) return e.Frame.Directions.DirectionToStick(d1 - e.Frame.CameraHeading);
            if (ok2 && !ok1) return e.Frame.Directions.DirectionToStick(d2 - e.Frame.CameraHeading);
            // The original returns the BOOLEAN ok1/ok2, not dir1/dir2.
            return (e.Random.Range(2, "evade_dir:tie") != 0 ? ok1 : ok2) ? 1 : 0;
        }

        private static void MinimizeUnhappiness(Execution e, float forwardSpeed, float backwardSpeed, float factor,
            float staticFriction, float movingFriction, Func<SourcePlanar, int, float> unhappiness)
        {
            var ring = new int[25];
            int at = 0;
            for (int i = 0; i < 7; i++) ring[at++] = i;
            for (int i = 13; i <= 48; i += 7) ring[at++] = i;
            for (int i = 47; i >= 42; i--) ring[at++] = i;
            for (int i = 35; i >= 7; i -= 7) ring[at++] = i;
            ring[24] = 24;
            var values = new float[25];
            values[24] = unhappiness(e.Actor.Position, 24);
            if (e.State.OldStick == 24) values[24] = values[24] * factor - staticFriction;
            int adj = Math.Max(0, e.Frame.Directions.StickHeading(e.Actor.Stick));
            adj = (adj + e.Frame.CameraHeading - 256 + 512 * e.Actor.Side) & 1023;
            float speed = adj > 338 && adj < 692 ? backwardSpeed : forwardSpeed;
            for (int i = 0; i < 24; i++)
            {
                SourcePlanar offset = e.Frame.Directions.Offset(speed, e.Frame.Directions.StickHeading(ring[i]) + e.Frame.CameraHeading);
                var point = new SourcePlanar(e.Actor.Position.X + offset.X, e.Actor.Position.Z + offset.Z);
                values[i] = unhappiness(point, ring[i]);
                if (ring[i] == e.State.OldStick) values[i] = values[i] * factor - movingFriction;
                // abs((i-1)%24) at i=0 really is index 1 in the C source, not 23.
                if (ring[(i + 1) % 24] == e.State.OldStick || ring[Math.Abs((i - 1) % 24)] == e.State.OldStick)
                    values[i] = values[i] * (1f - 0.66f * (1f - factor)) - 0.66f * movingFriction;
            }
            int best = 24;
            for (int i = 0; i < 24; i++) if (values[best] > values[i]) best = i;
            if (values[24] < movingFriction || (values[24] < staticFriction && e.State.OldStick == 24)) best = 24;
            e.State.Stick = ring[best];
        }

        private static float QuarterbackUnhappiness(Execution e, SourceBlockAssignments blocks, SourcePlanar point)
        {
            float un = 0f;
            foreach (int rusher in blocks.Rushers)
            {
                float distance = point.DistanceTo(e.Frame.Actors[rusher].Position);
                if (distance < 8f * UnitsPerYard) un += Square(8f * UnitsPerYard - distance);
                if (distance < 12f * UnitsPerYard) un += Square(12f * UnitsPerYard - distance);
            }
            float dx = Math.Abs(e.Frame.WorldX(e.Frame.Rules.LineOfScrimmage) - point.X);
            if (dx > e.State.QuarterbackPlan.DropbackUnits)
            {
                float extra = dx - e.State.QuarterbackPlan.DropbackUnits;
                un += 10f * (extra * extra * extra);
            }
            if (Math.Abs(point.Z) >= FieldWidth - UnitsPerYard && Math.Abs(point.Z) > Math.Abs(e.Actor.Position.Z)) un += 1000f;
            float dz = Math.Abs(FieldWidth - Math.Abs(point.Z));
            if (dx > dz) un += Square(dx - dz);
            if (e.Frame.SourceTick - e.Frame.SnapTick < 3 * 57 && e.State.QuarterbackPlan.RollDirection != 0)
            {
                float side = (e.Actor.Side == 0 ? 1f : -1f) * e.State.QuarterbackPlan.RollDirection;
                if (side * point.Z < 10f * UnitsPerYard) un += Square(10f * UnitsPerYard - side * point.Z);
            }
            return un;
        }

        private static float CarrierUnhappiness(SourceAiFrame frame, int actor, SourcePlanar point)
        {
            SourceAiActor carrier = frame.Actors[actor];
            float side = carrier.Side == 0 ? 1f : -1f;
            float un = 600f + 300f * side * (carrier.Position.X - point.X);
            int first = (1 - carrier.Side) * 7;
            for (int i = first; i < first + 7; i++)
            {
                float distance = point.DistanceTo(frame.Actors[i].Position);
                if (distance < 8f * UnitsPerYard) un += Square(8f * UnitsPerYard - distance);
                if ((frame.Actors[i].Flags & BlockedModes) == 0 && distance < 10f * UnitsPerYard)
                    un += Square(10f * UnitsPerYard - distance);
            }
            for (int i = carrier.Side * 7; i < carrier.Side * 7 + 7; i++)
                if (i != actor && point.DistanceTo(frame.Actors[i].Position) <= 12f) un += 100f;
            if (side * point.X < side * carrier.Position.X) un += 10000f;
            if (Math.Abs(point.Z) >= FieldWidth - UnitsPerYard && Math.Abs(point.Z) > Math.Abs(carrier.Position.Z)) un += 10000f;
            return un;
        }

        private static void Carrier(Execution e)
        {
            float friction = Math.Max(20, 60 - e.State.StickTicks * 2);
            MinimizeUnhappiness(e, 1.35f, 1.35f, 1f, friction, friction / 2f,
                (point, stick) => CarrierUnhappiness(e.Frame, e.Actor.Id, point));
            e.State.Buttons = SourceDroneButtons.None;
            if (e.State.Stick == 24) e.State.Stick = 3;
            if ((e.Actor.Flags & (SourceAiActorFlags.LowerUninterruptible | SourceAiActorFlags.UpperUninterruptible)) != 0 ||
                e.State.EvadeTicks != 0 || (e.Input.DronesLose && e.Input.DronesWinning)) return;
            SourceBlockAssignments blocks = e.Input.Blocking ?? AssignBlockers(e.Frame);
            int nearest = -1;
            for (int i = 0; i < blocks.Rushers.Count; i++)
                if (nearest == -1 || blocks.RushTimes[i] < blocks.RushTimes[nearest]) nearest = i;
            if (nearest == -1 || blocks.RushTimes[nearest] >= 20) return;
            int target = blocks.Rushers[nearest];
            if ((e.State.Flags & SourceDroneFlags.CanEvade) == 0)
            {
                if (e.Random.Range(100, "_drone_carrier:evade_permission") >= e.Input.Parameters.Int("drn_evade_ht", e.Actor.Side))
                { e.State.EvadeTicks = 40; return; }
                e.State.Flags |= SourceDroneFlags.CanEvade;
            }
            if (e.Frame.Bearing(e.Actor.Id, target) <= 160)
            {
                if (blocks.RushTimes[nearest] < 10)
                { e.State.Buttons = SourceDroneButtons.B | SourceDroneButtons.C; e.State.Flags &= ~SourceDroneFlags.CanEvade; }
                return;
            }
            if (e.Frame.Actors[target].Height > 0f)
            {
                if (e.Input.HurdleCount >= e.Input.Parameters.Int("hurdle_cap_ht", e.Actor.Side)) return;
                e.State.Buttons = SourceDroneButtons.A | SourceDroneButtons.C;
            }
            else
            {
                if (e.Input.SpinCount >= e.Input.Parameters.Int("spin_cap_ht", e.Actor.Side)) return;
                e.State.Buttons = SourceDroneButtons.C; e.Output.ForceNonTurboOneTick = true;
            }
            e.State.Flags &= ~SourceDroneFlags.CanEvade;
        }

        private static void Line(Execution e)
        {
            if (!IsLive(e.Frame)) { e.Hold(); return; }
            if (e.Actor.Side == e.Frame.Rules.OffenseSide)
            {
                e.State.Job = SourceActorJob.Block;
                if (e.State.Target >= 0)
                {
                    e.Output.Backpedal = true; e.Output.BackpedalTarget = e.State.Target;
                    e.Pursue(MoveToBlock(e.Frame, e.Actor.Id, e.State.Target, e.Frame.Ball.Carrier, 1.5f));
                    e.State.Buttons = SourceDroneButtons.None;
                }
                else e.Hold();
                return;
            }
            e.State.Buttons = (e.State.Flags & SourceDroneFlags.Blitz) != 0 ? SourceDroneButtons.C : SourceDroneButtons.None;
            int carrier = e.Frame.Ball.Carrier;
            if (carrier >= 0)
            {
                SourcePlanar target = e.Frame.Actors[carrier].Position;
                if (Math.Abs(e.Actor.Position.Z - target.Z) > Math.Abs(e.Actor.Position.X - target.X)) e.State.Buttons = SourceDroneButtons.C;
            }
            bool turbo = (e.State.Buttons & SourceDroneButtons.C) != 0;
            MinimizeUnhappiness(e, turbo ? 1.5f : 1.25f, turbo ? 1.6f : 1.25f, 1f, 20f, 40f,
                (point, stick) => LineUnhappiness(e, point));
            if (carrier >= 0 && e.Frame.Distance(e.Actor.Id, carrier) < 30f && CleanShot(e.Frame, e.Actor.Id, carrier) &&
                ((e.State.Flags & SourceDroneFlags.CanDive) != 0 || (e.Frame.DefensePlayFlags & SourcePlayFlags.LineDive) != 0))
                e.State.Buttons = SourceDroneButtons.A | SourceDroneButtons.C;
            e.State.Job = SourceActorJob.Rush;
        }

        private static bool CleanShot(SourceAiFrame frame, int actor, int target)
        {
            int nearest = -1, start = (1 - frame.Actors[actor].Side) * 7;
            for (int i = start; i < start + 7; i++)
                if (frame.Bearing(actor, i) <= 128 && (nearest == -1 || frame.Distance(actor, i) < frame.Distance(actor, nearest)))
                    nearest = i;
            return nearest == target;
        }

        private static float LineUnhappiness(Execution e, SourcePlanar point)
        {
            float un = 0f;
            for (int i = e.Actor.Side * 7; i < e.Actor.Side * 7 + 7; i++)
            {
                if (i == e.Actor.Id) continue;
                float distance = point.DistanceTo(e.Frame.Actors[i].Position);
                if (distance < 3f * UnitsPerYard) un += Square(3f * UnitsPerYard - distance);
            }
            int target = e.Frame.Ball.Carrier == -1 ? e.Frame.Ball.IntendedReceiver : e.Frame.Ball.Carrier;
            if (target >= 0)
            {
                float dx = point.X - e.Frame.Actors[target].Position.X, dz = point.Z - e.Frame.Actors[target].Position.Z;
                un += dx * dx + (e.Frame.Ball.Carrier >= 0 && e.Frame.Actors[target].Mode == SourceActorMode.Quarterback ? 6f : 1f) * dz * dz;
            }
            if (Math.Abs(point.Z) >= FieldWidth - UnitsPerYard && Math.Abs(point.Z) > Math.Abs(e.Actor.Position.Z)) un += 10000f;
            return un;
        }

        private static void DefensePreSnap(Execution e)
        {
            bool done = false;
            for (int budget = 0; !done && budget < 4096; budget++)
            {
                SourceRouteProgram program = e.State.Program;
                int at = e.State.WordIndex;
                switch ((SourceRouteOpcode)program.WordAt(at))
                {
                    case SourceRouteOpcode.WaitSnap: e.State.WordIndex++; break;
                    case SourceRouteOpcode.CoverWeak:
                        int trips = TripsLineup(e);
                        if (trips == 0) e.State.WordIndex++;
                        else
                        {
                            e.State.Program = e.Input.Parameters.Playbook.GetRoute(trips == -1 ? "db_zone_sl3" : "db_zone_sr3");
                            e.State.WordIndex = 0; e.State.Flags = SourceDroneFlags.ZonePreSnap;
                        }
                        break;
                    case SourceRouteOpcode.RushQuarterback: e.State.Job = SourceActorJob.Rush; done = true; break;
                    case SourceRouteOpcode.Block: e.State.Job = SourceActorJob.Block; done = true; break;
                    case SourceRouteOpcode.CoverTarget: e.State.Job = SourceActorJob.Cover; done = true; break;
                    case SourceRouteOpcode.SetFlag:
                        e.State.Flags |= (SourceDroneFlags)program.WordAt(at + 1); e.State.WordIndex += 2; break;
                    case SourceRouteOpcode.Zone:
                        if ((e.Frame.DefensePlayFlags & SourcePlayFlags.FakeZone) != 0)
                            e.State.FakeZoneTicks = 30 + e.Random.Range(180, "_drone_dbps:fake_zone_time");
                        if (e.State.Job == SourceActorJob.Zone) done = true;
                        else { ConfigureZone(e, program.WordAt(at + 1), program.WordAt(at + 2), program.WordAt(at + 3)); e.State.Job = SourceActorJob.Zone; }
                        break;
                    case SourceRouteOpcode.End:
                        e.State.Ended = true; e.State.Job = SourceActorJob.None; done = true; break;
                    case SourceRouteOpcode.NoOperation: e.State.WordIndex++; break;
                    case SourceRouteOpcode.Branch: e.State.WordIndex = program.WordAt(at + 1); break;
                    case SourceRouteOpcode.Restart: e.State.WordIndex = 0; break;
                    default:
                        // Known receiver commands are intentional JOB_NONE no-ops in _drone_dbps (e.g. kickoff).
                        if (SourceRouteProgram.InstructionWidth(program.WordAt(at)) == 0)
                            throw program.Error(at, "unknown defensive pre-snap opcode");
                        e.State.Job = SourceActorJob.None; done = true; break;
                }
            }
            if (!done) throw e.State.Program.Error(e.State.WordIndex, "non-yielding defensive program");
            e.Hold();
            if (e.State.Target != -1 && e.State.Job != SourceActorJob.Zone &&
                (OtherDrone(e, e.State.Target).Flags & SourceDroneFlags.Motion) != 0)
            {
                float friction = Math.Max(20, 60 - e.State.StickTicks * 2);
                MinimizeUnhappiness(e, 1.25f, 1.25f, 1f, friction * 2f, friction / 2f,
                    (point, stick) => DefensePreSnapUnhappiness(e, point));
                if (e.State.Stick != 24) e.State.ThinkTicks = -100000;
                e.State.ThinkTicks += 6 + e.Random.Range(40, "_drone_dbps:motion_think") -
                    (e.Input.Parameters.Level(1 - e.Frame.Rules.OffenseSide) >> 1);
            }
            else if ((e.State.Flags & SourceDroneFlags.ZonePreSnap) != 0) DoZone(e);
        }

        private static SourceRouteState OtherDrone(Execution e, int actor)
        {
            if (e.Input.DroneStates == null) throw e.Missing("explicit fourteen-actor drone state/ghost/order snapshot");
            SourceP1Guard.Actor(actor);
            return e.Input.DroneStates[actor];
        }

        private static int TripsLineup(Execution e)
        {
            int qb = e.Frame.Rules.OffenseSide * 7 + 3, le = 0, ge = 0;
            for (int i = qb + 1; i < qb + 4; i++)
            {
                bool motion = (OtherDrone(e, i).Flags & SourceDroneFlags.Motion) != 0;
                if (e.Frame.Actors[i].Position.Z <= e.Frame.Actors[qb].Position.Z || motion) le++;
                if (e.Frame.Actors[i].Position.Z >= e.Frame.Actors[qb].Position.Z || motion) ge++;
            }
            return le == 3 ? -1 : ge == 3 ? 1 : 0;
        }

        private static void ConfigureZone(Execution e, int width, int near, int far)
        {
            float low, high;
            switch (width)
            {
                case 0: low = -(FieldWidth + UnitsPerYard); high = -(FieldWidth / 3f - UnitsPerYard); break;
                case 1: low = -(FieldWidth / 3f + UnitsPerYard); high = FieldWidth / 3f + UnitsPerYard; break;
                case 2: low = FieldWidth / 3f - UnitsPerYard; high = FieldWidth + UnitsPerYard; break;
                case 3: low = -(FieldWidth + UnitsPerYard); high = UnitsPerYard; break;
                case 4: low = -UnitsPerYard; high = FieldWidth + UnitsPerYard; break;
                case 5: low = -(FieldWidth + UnitsPerYard); high = FieldWidth + UnitsPerYard; break;
                default: throw e.State.Program.Error(e.State.WordIndex, "invalid DZ zone width");
            }
            if (near < -10 || (far != -1 && far <= near))
                throw e.State.Program.Error(e.State.WordIndex, "invalid depth would mutate original shared C route memory");
            float lowX = e.Frame.WorldX(Math.Min(100, e.Frame.Rules.LineOfScrimmage + near));
            float highX = e.Frame.WorldX(far == -1 ? 110 : e.Frame.Rules.LineOfScrimmage + far);
            if (e.Frame.Rules.OffenseSide == 1)
            {
                float swap = low; low = -high; high = -swap;
                swap = lowX; lowX = highX; highX = swap;
            }
            e.State.ZoneLowX = lowX; e.State.ZoneHighX = highX;
            e.State.ZoneLowZ = low; e.State.ZoneHighZ = high;
        }

        private static float DefensePreSnapUnhappiness(Execution e, SourcePlanar point)
        {
            float un = 0f;
            if (e.State.Target >= 0)
            {
                float distance = Math.Abs(point.Z - e.Frame.Actors[e.State.Target].Position.Z);
                un += distance * Math.Min(distance, 35f);
            }
            for (int i = e.Actor.Side * 7; i < e.Actor.Side * 7 + 7; i++)
            {
                if (i == e.Actor.Id) continue;
                float dx = Math.Abs(point.X - e.Frame.Actors[i].Position.X), dz = Math.Abs(point.Z - e.Frame.Actors[i].Position.Z);
                if (dx + dz / 2f < 10f) un += 1000f * (10f - (dx + dz / 2f));
            }
            float side = e.Actor.Side == 0 ? -1f : 1f;
            // Formation selection is supplied by the actual script's play, never inferred from a menu PlayId.
            SourceFormationSlot formation = FindFormationSlot(e);
            float ix = e.Frame.WorldX(e.Frame.Rules.LineOfScrimmage) - side * formation.Offset.X;
            un += (point.X * side < ix * side ? 100f : 10f) * Math.Abs(ix - point.X);
            return un;
        }

        private static SourceFormationSlot FindFormationSlot(Execution e)
        {
            if (e.Input.FormationSlot.HasValue) return e.Input.FormationSlot.Value;
            throw e.Missing("_drone_dbps:actual selected formation slot input");
        }

        private static void DoZone(Execution e)
        {
            float factor = e.Frame.Rules.OffenseSide == 0 ? 1f : -1f;
            e.Output.Backpedal = true;
            e.State.MoveTicks = Math.Max(0, e.State.MoveTicks - 1);
            SourcePlanar point;
            if (e.State.MoveTicks > 0)
            {
                point = new SourcePlanar(e.State.ZonePoint.X + e.State.ZoneVelocity.X, e.State.ZonePoint.Z + e.State.ZoneVelocity.Z);
                e.State.ZonePoint = point;
            }
            else
            {
                var targets = new List<int>();
                int qb = e.Frame.Rules.OffenseSide * 7 + 3, deepest = 0;
                for (int i = qb; i < qb + 4; i++)
                {
                    SourcePlanar p = e.Frame.Actors[i].Position;
                    if (p.X >= e.State.ZoneLowX && p.X <= e.State.ZoneHighX && p.Z >= e.State.ZoneLowZ && p.Z <= e.State.ZoneHighZ)
                        targets.Add(i);
                }
                for (int i = 1; i < targets.Count; i++)
                    if (e.Frame.Actors[targets[i]].Position.X * factor > e.Frame.Actors[targets[deepest]].Position.X * factor) deepest = i;
                float x = 0f, z = 0f, vx = 0f, vz = 0f;
                if (targets.Count == 0)
                {
                    z = (e.State.ZoneHighZ + e.State.ZoneLowZ) / 2f;
                    x = (e.State.ZoneHighX - e.State.ZoneLowX) / UnitsPerYard <= 10f
                        ? (e.State.ZoneHighX + e.State.ZoneLowX) / 2f
                        : e.Frame.Rules.OffenseSide == 0 ? e.State.ZoneLowX + 5f * UnitsPerYard : e.State.ZoneHighX - 5f * UnitsPerYard;
                    e.Output.BackpedalTarget = e.Frame.Ball.Carrier;
                }
                else if (targets.Count == 1)
                {
                    SourceAiActor target = e.Frame.Actors[targets[0]];
                    x = target.Position.X + factor * UnitsPerYard * 4f; z = target.Position.Z;
                    vx = target.Velocity.X; vz = target.Velocity.Z; e.Output.BackpedalTarget = target.Id;
                }
                else
                {
                    foreach (int target in targets)
                    {
                        x += e.Frame.Actors[target].Position.X; z += e.Frame.Actors[target].Position.Z;
                        vx += e.Frame.Actors[target].Velocity.X; vz += e.Frame.Actors[target].Velocity.Z;
                    }
                    SourceAiActor deep = e.Frame.Actors[targets[deepest]];
                    x += deep.Position.X; z += deep.Position.Z; vx += deep.Velocity.X; vz += deep.Velocity.Z;
                    x /= targets.Count + 1f; z /= targets.Count + 1f; vx /= targets.Count + 1f; vz /= targets.Count + 1f;
                    x += factor * UnitsPerYard * 4f;
                    if (x * factor < 2f * UnitsPerYard + deep.Position.X * factor)
                        x = deep.Position.X + 2f * UnitsPerYard; // Source omits factor here.
                    e.Output.BackpedalTarget = deep.Id;
                }
                point = new SourcePlanar(x, z); e.State.ZonePoint = point; e.State.ZoneVelocity = new SourcePlanar(vx, vz);
                int defense = 1 - e.Frame.Rules.OffenseSide;
                int min = e.Input.Parameters.Int("zone_thinkmin_ht", defense), max = e.Input.Parameters.Int("zone_thinkmax_ht", defense);
                e.State.MoveTicks = min + e.Random.Range(max + 1 - min, "do_zone:move_time");
            }
            e.State.Buttons = e.Actor.Mode == SourceActorMode.DefensiveBackPreSnap ? SourceDroneButtons.C : SourceDroneButtons.None;
            float distanceToPoint = e.Actor.Position.DistanceTo(point);
            if (distanceToPoint < 3f)
            {
                e.State.Stick = 24;
                if (e.Frame.Ball.Carrier >= 0) e.Output.Facing = e.Frame.Angle(e.Actor.Id, e.Frame.Ball.Carrier);
            }
            else if (distanceToPoint > UnitsPerYard || e.State.StickTicks > 30) e.Aim(point);
            if ((e.Frame.DefensePlayFlags & SourcePlayFlags.FakeZone) != 0 && e.State.FakeZoneTicks > 0 && --e.State.FakeZoneTicks == 0)
                e.State.Job = SourceActorJob.Cover;
        }

        public static SourceRouteStep DecideDefensiveBack(SourceRouteState state, SourceRouteInput input, ISourceRandom random = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.Frame.Actors[input.ActorId].Mode != SourceActorMode.DefensiveBack)
                throw new ArgumentException("Expected active source MODE_DB.", nameof(input));
            return StepRoute(state, input, random);
        }

        private static void DefensiveBack(Execution e)
        {
            SourceAiBall ball = e.Frame.Ball;
            if (!IsLive(e.Frame)) { e.Hold(); return; }
            if ((e.Actor.Flags & SourceAiActorFlags.RunningPush) != 0)
            {
                AimAtActor(e, e.State.Target); e.State.Buttons = SourceDroneButtons.B | SourceDroneButtons.C; return;
            }
            if (e.Frame.Rules.PlayType == SourcePlayType.Kickoff && ball.Type == SourceBallType.Kickoff && ball.Carrier == -1)
            {
                if ((e.Frame.GameFlags & SourceGameFlags.OnsideKick) == 0) e.Hold();
                else AimAtLooseBall(e);
                return;
            }
            if (ball.Carrier >= 0)
            {
                if (e.Frame.Actors[ball.Carrier].Mode == SourceActorMode.Quarterback) CoverOrRush(e);
                else PursueCarrier(e);
                return;
            }
            if ((e.Frame.GameFlags & SourceGameFlags.HikingBall) != 0) { CoverOrRush(e); return; }
            if (ball.IntendedReceiver == -1 || ball.Type == SourceBallType.Fumble ||
                ball.Type == SourceBallType.Punt || ball.Type == SourceBallType.Bobble) { AimAtLooseBall(e); return; }
            if (ball.FlightTicks == 0) { CoverOrRush(e); return; }
            if (ball.FlightTicks == 1)
            {
                int baseTime = e.Input.Parameters.Int("db_preact_ht", e.Actor.Side);
                if (e.State.Job == SourceActorJob.Rush) e.State.PassReactTicks = e.Random.Range(baseTime, "_drone_db:rush_react") / 3;
                else if (e.State.Job == SourceActorJob.Zone) e.State.PassReactTicks = e.Random.Range(baseTime, "_drone_db:zone_react");
                else if (ball.IntendedReceiver == e.State.Target)
                {
                    // GAME.H GREATER(x,y) is a macro: its y expression is evaluated AGAIN on the false arm.
                    int compared = e.Random.Range(baseTime, "_drone_db:target_react:compare") * 2 / 3;
                    e.State.PassReactTicks = e.State.FakeTicks > compared ? e.State.FakeTicks
                        : e.Random.Range(baseTime, "_drone_db:target_react:selected") * 2 / 3;
                }
                else e.State.PassReactTicks = e.Random.Range(baseTime, "_drone_db:other_react:0") + e.Random.Range(baseTime, "_drone_db:other_react:1");
                if (e.Frame.FieldX(ball.Target.X) < e.Frame.Rules.LineOfScrimmage &&
                    (e.Frame.GameFlags & SourceGameFlags.BallCrossedLine) == 0 &&
                    (e.State.Job != SourceActorJob.Cover || e.State.Target != ball.IntendedReceiver))
                    e.State.PassReactTicks = 10000;
                CoverOrRush(e); return;
            }
            if (e.State.PassReactTicks != 0) { e.State.PassReactTicks--; CoverOrRush(e); return; }
            if ((e.State.Flags & SourceDroneFlags.WillIntercept) != 0)
            {
                if (--e.State.CatchTicks <= 200) { e.State.Stick = e.State.OldStick; e.State.Buttons = SourceDroneButtons.A; }
                else { e.Aim(e.State.Mark); e.State.Buttons = SourceDroneButtons.None; }
                return;
            }
            if ((e.State.Flags & SourceDroneFlags.WillAttackNow) != 0)
            {
                if (e.Frame.Distance(e.Actor.Id, ball.IntendedReceiver) > 12f)
                { e.Pursue(MoveToIntercept(e.Frame, e.Actor.Id, ball.IntendedReceiver, 1.15f)); }
                else { AimAtActor(e, ball.IntendedReceiver); e.State.Buttons = SourceDroneButtons.B | SourceDroneButtons.C; }
                e.State.Flags &= ~SourceDroneFlags.WillAttackNow; return;
            }
            if ((e.State.Flags & SourceDroneFlags.WillAttackLater) != 0)
            {
                if (e.Actor.Position.DistanceTo(ball.Target) > 12f) { e.Aim(ball.Target); e.State.Buttons = SourceDroneButtons.None; }
                else if (ball.TotalTicks - ball.FlightTicks > 18) e.Hold();
                else { AimAtActor(e, ball.IntendedReceiver); e.State.Buttons = SourceDroneButtons.B | SourceDroneButtons.C; }
                return;
            }
            if ((e.State.Flags & SourceDroneFlags.WillPosition) != 0) { AimAtLooseBall(e); return; }
            ChooseDefense(e);
            // Shipping _drone_db executes C2a1 once even when db_choose_defense selected another future job.
            e.Aim(ball.Target); e.State.Buttons = SourceDroneButtons.None;
        }

        private static void CoverOrRush(Execution e)
        {
            if (e.State.Job == SourceActorJob.Cover && e.State.Target != -1 && e.State.Target != e.Frame.Ball.Carrier)
            {
                if ((e.State.Flags & SourceDroneFlags.Ordered) != 0 && e.State.OrderTicks > 0)
                {
                    e.State.Stick = e.State.OrderStick;
                    if (--e.State.OrderTicks == 0) e.State.Flags &= ~SourceDroneFlags.Ordered;
                    e.State.Buttons = SourceDroneButtons.None; return;
                }
                SourcePlanar point;
                if ((OtherDrone(e, e.State.Target).Flags & SourceDroneFlags.GhostActive) != 0 && e.State.FakeTicks > 0)
                { e.State.FakeTicks--; point = OtherDrone(e, e.State.Target).Ghost; }
                else
                {
                    point = e.Frame.Actors[e.State.Target].Position;
                    e.Output.Backpedal = true; e.Output.BackpedalTarget = e.State.Target;
                }
                if (TryCheapShot(e)) return;
                point = new SourcePlanar(point.X + (e.Frame.Rules.OffenseSide == 0 ? 4f : -4f) * UnitsPerYard, point.Z);
                if (e.Actor.Position.DistanceTo(point) > UnitsPerYard || e.State.StickTicks > 30) e.Aim(point);
                e.State.Buttons = SourceDroneButtons.None; return;
            }
            if (e.State.Job == SourceActorJob.Zone)
            { if (!TryCheapShot(e)) DoZone(e); return; }
            int target = e.Frame.Ball.Carrier == -1 ? e.Frame.Ball.IntendedReceiver : e.Frame.Ball.Carrier;
            if (target < 0) throw e.Missing("_drone_db:carrier_dir with neither carrier nor intended receiver");
            AimAtActor(e, target);
            e.State.Buttons = (e.State.Flags & SourceDroneFlags.Blitz) != 0
                ? e.Frame.Distance(e.Actor.Id, target) < 36f ? SourceDroneButtons.A | SourceDroneButtons.C : SourceDroneButtons.C
                : SourceDroneButtons.None;
            e.State.ThinkTicks = e.Random.Range(60, "_drone_db:rush_think_kind") == 0
                ? 20 + e.Random.Range(30, "_drone_db:rush_think_long")
                : 2 + e.Random.Range(5, "_drone_db:rush_think_short");
        }

        private static bool TryCheapShot(Execution e)
        {
            if (e.State.Target < 0 || e.Frame.Distance(e.Actor.Id, e.State.Target) >= 34f ||
                (e.State.Flags & SourceDroneFlags.CanCheapShot) == 0) return false;
            AimAtActor(e, e.State.Target); e.State.Buttons = SourceDroneButtons.B | SourceDroneButtons.C;
            e.State.Flags ^= SourceDroneFlags.CanCheapShot; return true;
        }

        private static void PursueCarrier(Execution e)
        {
            int carrier = e.Frame.Ball.Carrier;
            e.State.Job = SourceActorJob.Rush;
            bool fast = e.Frame.FieldX(e.Actor.Position.X) <= e.Frame.FieldX(e.Frame.Ball.Position.X) ||
                (e.State.Flags & SourceDroneFlags.Blitz) != 0;
            if ((e.State.Flags & SourceDroneFlags.SmartIntercept) != 0)
                e.Pursue(MoveToIntercept(e.Frame, e.Actor.Id, carrier, fast ? 1.85f : 1.15f));
            else AimAtActor(e, carrier);
            e.State.Buttons = fast ? SourceDroneButtons.C : SourceDroneButtons.None;
            if (e.Frame.Distance(e.Actor.Id, carrier) < 32f &&
                (!e.Frame.IsHumanTeam(e.Actor.Side) || e.Random.Range(10, "_drone_db:human_team_dive") == 0) &&
                ((e.State.Flags & SourceDroneFlags.CanDive) != 0 || e.Random.Range(100, "_drone_db:rare_dive") == 0))
                e.State.Buttons = SourceDroneButtons.A | SourceDroneButtons.C;
        }

        private static void AimAtLooseBall(Execution e)
        {
            int toBall = e.Frame.Directions.Heading(e.Actor.Position, e.Frame.Ball.Position);
            float distance = e.Actor.Position.DistanceTo(e.Frame.Ball.Target);
            if (Math.Abs(toBall - e.Input.TargetFacing) < 64 && distance <= 4f) e.Hold();
            else if (distance <= 2f)
            {
                e.State.Stick = e.Frame.Directions.DirectionToStick(toBall - e.Frame.CameraHeading);
                e.State.Buttons = SourceDroneButtons.None;
            }
            else { e.Aim(e.Frame.Ball.Target); e.State.Buttons = SourceDroneButtons.None; }
        }

        private static void AimAtActor(Execution e, int target)
        {
            if (target < 0) throw e.Missing("p2p_angles target -1");
            e.State.Stick = e.Frame.Directions.DirectionToStick(e.Frame.Angle(e.Actor.Id, target) - e.Frame.CameraHeading);
        }

        private static void ChooseDefense(Execution e)
        {
            if (e.Frame.IsHumanTeam(e.Actor.Side)) { e.State.Flags |= SourceDroneFlags.WillPosition; return; }
            SourceAiActor receiver = e.Frame.Actors[e.Frame.Ball.IntendedReceiver];
            int playerTicks = (int)InterceptTime(e.Actor.Position, 1.15f, receiver.Position, receiver.Velocity, 8f);
            float low = InterceptTime(e.Actor.Position, 1.15f, e.Frame.Ball.Position, e.Frame.Ball.Velocity, 6f);
            float high = InterceptTime(e.Actor.Position, 1.15f, e.Frame.Ball.Position, e.Frame.Ball.Velocity, 6f, true);
            float lowY = e.Frame.Ball.Height + e.Frame.Ball.VerticalVelocity * low + 0.5f * -0.08f * low * low;
            float highY = e.Frame.Ball.Height + e.Frame.Ball.VerticalVelocity * high + 0.5f * -0.08f * high * high;
            int ballTicks = (int)(highY < lowY && highY > 0f ? high : low);
            float ballY = highY < lowY && highY > 0f ? highY : lowY;
            int remaining = e.Frame.Ball.TotalTicks - e.Frame.Ball.FlightTicks;
            bool canIntercept = ballTicks < remaining && ballY <= 35f, canAttack = playerTicks < 18 + remaining;
            int result = !canIntercept && !canAttack ? 3 : !canIntercept ? 0 : !canAttack ? 2 : 4;
            int intercepting = 0, positioning = 0;
            for (int i = e.Actor.Side * 7; i < e.Actor.Side * 7 + 7; i++)
            {
                SourceDroneFlags flags = i == e.Actor.Id ? e.State.Flags : OtherDrone(e, i).Flags;
                if ((flags & SourceDroneFlags.WillIntercept) != 0) intercepting++;
                if ((flags & SourceDroneFlags.WillPosition) != 0) positioning++;
            }
            if (positioning == 0) result = 3;
            if (result == 2 && intercepting != 0) result = canAttack ? 0 : 3;
            if (result == 4) result = intercepting != 0 ? 0 : 2;
            // randrng is evaluated BEFORE result==ATT_NOW; even position-only choices consume this draw.
            if (e.Random.Range(100, "db_choose_defense:early_hit") > e.Input.Parameters.Int("early_hit_ht", e.Actor.Side) && result == 0) result = 1;
            if (result != 3 && e.Random.Range(100, "db_choose_defense:attack_permission") >
                e.Input.Parameters.Int("drone_db_att_ht", e.Actor.Side)) result = 3;
            switch (result)
            {
                case 0: e.State.Flags |= SourceDroneFlags.WillAttackNow; break;
                case 1: e.State.Flags |= SourceDroneFlags.WillAttackLater; break;
                case 2:
                    e.State.CatchTicks = ballTicks - 1; e.State.Flags |= SourceDroneFlags.WillIntercept;
                    e.State.Mark = new SourcePlanar(e.Frame.Ball.Position.X + ballTicks * e.Frame.Ball.Velocity.X,
                        e.Frame.Ball.Position.Z + ballTicks * e.Frame.Ball.Velocity.Z);
                    break;
                default: e.State.Flags |= SourceDroneFlags.WillPosition; break;
            }
        }

        private static float Speed(SourcePlanar velocity) { return (float)Math.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z); }
        private static float Square(float value) { return value * value; }
    }
}
