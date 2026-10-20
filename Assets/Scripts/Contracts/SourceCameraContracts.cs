using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.Contracts
{
    public readonly struct SourceCameraActor
    {
        public int PlayerId { get; }
        public int RawMode { get; }
        public int RawFlags { get; }
        public int Heading { get; }
        public SourceVector3 Position { get; }
        public SourceVector3 GroundCameraPosition { get; }

        public SourceCameraActor(int playerId, int rawMode, int rawFlags, int heading,
            SourceVector3 position, SourceVector3 groundCameraPosition)
        {
            SourceContractGuard.Range(playerId, 0, 13, nameof(playerId));
            SourceContractGuard.Range(rawMode, 0, 10, nameof(rawMode));
            SourceCameraBoundary.RequireFinite(position);
            SourceCameraBoundary.RequireFinite(groundCameraPosition);
            PlayerId = playerId; RawMode = rawMode; RawFlags = rawFlags; Heading = heading;
            Position = position; GroundCameraPosition = groundCameraPosition;
        }
    }

    public readonly struct SourceCameraBall
    {
        public SourceVector3 Position { get; }
        public SourceVector3 Velocity { get; }
        public SourceVector3 Origin { get; }
        public SourceVector3 Target { get; }
        public SourceVector3 TargetCameraPosition { get; }
        public int RawType { get; }
        public int RawFlags { get; }
        public int FlightTime { get; }
        public int Handler { get; }
        public int Receiver { get; }

        public SourceCameraBall(SourceVector3 position, SourceVector3 velocity, SourceVector3 origin,
            SourceVector3 target, SourceVector3 targetCameraPosition, int rawType, int rawFlags,
            int flightTime, int handler, int receiver)
        {
            SourceCameraBoundary.RequireFinite(position); SourceCameraBoundary.RequireFinite(velocity);
            SourceCameraBoundary.RequireFinite(origin); SourceCameraBoundary.RequireFinite(target);
            SourceCameraBoundary.RequireFinite(targetCameraPosition);
            SourceContractGuard.Range(rawType, 0, 6, nameof(rawType));
            SourceContractGuard.Range(handler, -1, 13, nameof(handler));
            SourceContractGuard.Range(receiver, -1, 13, nameof(receiver));
            if (flightTime < 0) throw new ArgumentOutOfRangeException(nameof(flightTime));
            Position = position; Velocity = velocity; Origin = origin; Target = target;
            TargetCameraPosition = targetCameraPosition; RawType = rawType; RawFlags = rawFlags;
            FlightTime = flightTime; Handler = handler; Receiver = receiver;
        }
    }

    // Source-world observations at RL_CAMBOT, before every player and the ball process.
    // Cached ground/target coordinates belong to the preceding source draw, not this render sample.
    public sealed class SourceCameraBoundary
    {
        public long SourceTick { get; }
        public int ProcessOrder { get; }
        public int RawGameMode { get; }
        public int RawGameFlags { get; }
        public int RawPlayType { get; }
        public int RawPlayResult { get; }
        public int QuarterIndex { get; }
        public int OffenseSide { get; }
        public int LastPossession { get; }
        public int LineOfScrimmage { get; }
        public int Carrier { get; }
        public bool HumanTeammates { get; }
        public bool Pullback { get; }
        public int AutoHikeTicks { get; }
        public int OffensePlayFlags { get; }
        public bool OffenseTurnover { get; }
        public int RawGameState { get; }
        public SourceCameraBall Ball { get; }
        public ReadOnlyCollection<SourceCameraActor> Actors { get; }

        public SourceCameraBoundary(long sourceTick, int processOrder, int rawGameMode, int rawGameFlags,
            int rawPlayType, int rawPlayResult, int quarterIndex, int offenseSide, int lastPossession,
            int lineOfScrimmage, int carrier, bool humanTeammates, bool pullback, int autoHikeTicks,
            int offensePlayFlags, SourceCameraBall ball, IReadOnlyList<SourceCameraActor> actors,
            bool offenseTurnover = false, int rawGameState = 2)
        {
            if (sourceTick < 0 || processOrder < -1) throw new ArgumentOutOfRangeException(nameof(sourceTick));
            SourceContractGuard.Range(rawGameMode, 0, 6, nameof(rawGameMode));
            SourceContractGuard.Range(rawPlayType, 0, 4, nameof(rawPlayType));
            SourceContractGuard.Range(rawPlayResult, 0, 15, nameof(rawPlayResult));
            SourceContractGuard.Range(quarterIndex, 0, 31, nameof(quarterIndex));
            SourceContractGuard.Range(offenseSide, 0, 1, nameof(offenseSide));
            SourceContractGuard.Range(lastPossession, 0, 1, nameof(lastPossession));
            SourceContractGuard.Range(lineOfScrimmage, 0, 100, nameof(lineOfScrimmage));
            SourceContractGuard.Range(carrier, -1, 13, nameof(carrier));
            if (actors == null || actors.Count != 14)
                throw new ArgumentException("CAMBOT requires all fourteen actual source actors.", nameof(actors));
            var copy = new SourceCameraActor[14];
            for (int id = 0; id < copy.Length; id++)
            {
                if (actors[id].PlayerId != id)
                    throw new ArgumentException("CAMBOT actor identities must retain source slot order.", nameof(actors));
                copy[id] = actors[id];
            }
            SourceTick = sourceTick; ProcessOrder = processOrder; RawGameMode = rawGameMode;
            RawGameFlags = rawGameFlags; RawPlayType = rawPlayType; RawPlayResult = rawPlayResult;
            QuarterIndex = quarterIndex; OffenseSide = offenseSide; LastPossession = lastPossession;
            LineOfScrimmage = lineOfScrimmage; Carrier = carrier; HumanTeammates = humanTeammates;
            Pullback = pullback; AutoHikeTicks = autoHikeTicks; OffensePlayFlags = offensePlayFlags;
            OffenseTurnover = offenseTurnover; RawGameState = rawGameState;
            Ball = ball; Actors = Array.AsReadOnly(copy);
        }

        internal static void RequireFinite(SourceVector3 value)
        {
            if (float.IsNaN(value.X) || float.IsInfinity(value.X) ||
                float.IsNaN(value.Y) || float.IsInfinity(value.Y) ||
                float.IsNaN(value.Z) || float.IsInfinity(value.Z))
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    public interface ISourceCameraState
    {
        SourceCameraBoundary CameraBoundary { get; }
    }

    public interface ISourceCameraProcessServices
    {
        bool Exists(int id, int mask);
        void SuspendMultiple(int id, int mask);
        void ResumeMultiple(int id, int mask);
        void ResumeAll();
        void CreateMessage(int originalMessageNumber);
        void SleepCamera(int sourceTicks);
    }

    public readonly struct SourceProcessView
    {
        public long Serial { get; }
        public int Id { get; }
        public string Function { get; }
        public int RunLevel { get; }
        public long BirthTick { get; }
        public ushort SleepWord { get; }
        public long LastWakeTick { get; }
        public bool Suspended { get { return (SleepWord & 0x8000) != 0; } }
        public int SleepRemaining { get { return SleepWord & 0x7fff; } }
        public SourceProcessView(long serial, int id, string function, int runLevel, long birthTick, ushort sleepWord,
            long lastWakeTick = -1)
        {
            Serial = serial; Id = id; Function = function; RunLevel = runLevel;
            BirthTick = birthTick; SleepWord = sleepWord;
            LastWakeTick = lastWakeTick;
        }
    }

    public readonly struct SourceCameraFeedback
    {
        public long SourceTick { get; }
        public bool TargetOffscreen { get; }
        public int? OpeningAutoHikeTicks { get; }
        public int? FieldGoalLastCarrier { get; }
        public bool CameraCompleted { get; }
        public int CameraSleepTicks { get; }
        public SourceCameraFeedback(long sourceTick, bool targetOffscreen, int? openingAutoHikeTicks = null,
            int? fieldGoalLastCarrier = null, bool cameraCompleted = true, int cameraSleepTicks = 0)
        {
            if (sourceTick < 0 || openingAutoHikeTicks.HasValue && openingAutoHikeTicks.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(sourceTick));
            SourceTick = sourceTick; TargetOffscreen = targetOffscreen;
            OpeningAutoHikeTicks = openingAutoHikeTicks;
            if (fieldGoalLastCarrier.HasValue)
                SourceContractGuard.Range(fieldGoalLastCarrier.Value, 0, 13, nameof(fieldGoalLastCarrier));
            FieldGoalLastCarrier = fieldGoalLastCarrier;
            if (cameraSleepTicks < 0 || cameraCompleted && cameraSleepTicks != 0)
                throw new ArgumentOutOfRangeException(nameof(cameraSleepTicks));
            CameraCompleted = cameraCompleted; CameraSleepTicks = cameraSleepTicks;
        }
    }
}
