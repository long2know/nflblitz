using System.Collections.Generic;

namespace Blitz.Contracts
{
    public static class SliceRules
    {
        public const int TicksPerSecond = 60;
        public const int PlayersPerTeam = 7;
        public const int FirstDownYards = 30;
        public const int MaximumDown = 4;
        public const int TouchdownPoints = 6;
        public const int SafetyPoints = 2;
        public const int QuarterCount = 4;
        public const int QuarterTicks = 120 * TicksPerSecond;
        public const int PreSnapTicks = 5 * TicksPerSecond;
        public const float FieldLengthYards = 100f;
        public const float EndZoneDepthYards = 10f;
        public const float FieldHalfWidthYards = 182.85f / 7.8f;
    }

    public enum TeamId { Home = 0, Away = 1 }
    public enum GamePhase { PlaySelect, PreSnap, LiveBall, DeadBall, GameOver }
    public enum PlayId { Run, QuickPass, Cover, Blitz }
    public enum InputAction { None, SelectPlay, Snap, Pass, Tackle, SwitchDefender, Continue }
    public enum BallMode { Held, InFlight, Dead }

    public enum PlayOutcome
    {
        None,
        Tackled,
        OutOfBounds,
        Incomplete,
        FirstDown,
        TurnoverOnDowns,
        Touchdown,
        Safety
    }

    public enum GameEventType
    {
        PlaySelected,
        Snapped,
        PassThrown,
        PassCaught,
        Tackled,
        OutOfBounds,
        Incomplete,
        FirstDown,
        TurnoverOnDowns,
        PossessionChanged,
        Touchdown,
        Safety,
        QuarterEnded,
        GameEnded
    }

    public readonly struct FieldVector
    {
        public float X { get; }
        public float Z { get; }

        public FieldVector(float x, float z)
        {
            X = x;
            Z = z;
        }
    }

    public readonly struct PlayerCommand
    {
        public FieldVector Move { get; }
        public bool Turbo { get; }
        public InputAction Action { get; }
        public PlayId Play { get; }
        public int TargetPlayerId { get; }

        public PlayerCommand(
            FieldVector move = default(FieldVector),
            bool turbo = false,
            InputAction action = InputAction.None,
            PlayId play = PlayId.Run,
            int targetPlayerId = -1)
        {
            Move = move;
            Turbo = turbo;
            Action = action;
            Play = play;
            TargetPlayerId = targetPlayerId;
        }
    }

    public readonly struct PlayerView
    {
        public int Id { get; }
        public TeamId Team { get; }
        public FieldVector Position { get; }
        public FieldVector Velocity { get; }
        public float Turbo01 { get; }

        public PlayerView(int id, TeamId team, FieldVector position, FieldVector velocity, float turbo01)
        {
            Id = id;
            Team = team;
            Position = position;
            Velocity = velocity;
            Turbo01 = turbo01;
        }
    }

    public readonly struct BallView
    {
        public BallMode Mode { get; }
        public FieldVector Position { get; }
        public float Height { get; }
        public int CarrierId { get; }
        public int TargetPlayerId { get; }

        public BallView(BallMode mode, FieldVector position, float height, int carrierId, int targetPlayerId)
        {
            Mode = mode;
            Position = position;
            Height = height;
            CarrierId = carrierId;
            TargetPlayerId = targetPlayerId;
        }
    }

    public readonly struct GameEvent
    {
        public long Tick { get; }
        public GameEventType Type { get; }
        public TeamId Team { get; }
        public int PlayerId { get; }
        public int Value { get; }
        public FieldVector Position { get; }

        public GameEvent(
            long tick,
            GameEventType type,
            TeamId team,
            int playerId = -1,
            int value = 0,
            FieldVector position = default(FieldVector))
        {
            Tick = tick;
            Type = type;
            Team = team;
            PlayerId = playerId;
            Value = value;
            Position = position;
        }
    }

    public interface IGameState
    {
        long Tick { get; }
        int PlayNumber { get; }
        GamePhase Phase { get; }
        TeamId Possession { get; }
        int OffenseDirection { get; }
        int Quarter { get; }
        int QuarterTicksRemaining { get; }
        int PreSnapTicksRemaining { get; }
        int Down { get; }
        float LineOfScrimmage { get; }
        float FirstDownLine { get; }
        int HomeScore { get; }
        int AwayScore { get; }
        int ControlledPlayerId { get; }
        PlayId HomePlay { get; }
        PlayId AwayPlay { get; }
        PlayOutcome LastOutcome { get; }
        BallView Ball { get; }
        IReadOnlyList<PlayerView> Players { get; }
    }

    public interface IGameSimulation
    {
        IGameState State { get; }
        IReadOnlyList<GameEvent> Events { get; }
        void Step(PlayerCommand command);
        void Restart(int seed = 1);
    }
}
