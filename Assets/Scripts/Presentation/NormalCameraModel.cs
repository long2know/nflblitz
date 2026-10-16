using System;
using Blitz.Contracts;

namespace Blitz.Presentation
{
    internal readonly struct NormalCameraFrame
    {
        internal readonly FieldVector Focus;
        internal readonly FieldVector Eye;
        internal readonly float EyeHeight;
        internal readonly float Distance;
        internal readonly float VerticalFieldOfView;
        internal readonly float Aspect;
        internal readonly int Direction;

        internal NormalCameraFrame(FieldVector focus, float distance, float aspect, int direction)
        {
            Focus = focus;
            Distance = distance;
            Aspect = aspect;
            Direction = direction;
            double pitch = NormalCameraModel.PitchDegrees * Math.PI / 180d;
            Eye = new FieldVector(focus.X - direction * distance * (float)Math.Cos(pitch), focus.Z);
            EyeHeight = NormalCameraModel.FocusHeightYards + distance * (float)Math.Sin(pitch);
            double verticalTangent = Math.Max(192d / NormalCameraModel.ReferenceFocalPixels,
                256d / NormalCameraModel.ReferenceFocalPixels / aspect);
            VerticalFieldOfView = (float)(2d * Math.Atan(verticalTangent) * 180d / Math.PI);
        }
    }

    // Normal mode only. Source paths/lines and the contract adaptations are recorded in the camera handoff.
    internal sealed class NormalCameraModel
    {
        internal const float ArchiveUnitsPerYard = 390f / 50f;
        internal const float ReferenceFocalPixels = 664f;
        internal const float PitchDegrees = 25f;
        internal const float FocusHeightYards = 6f / 3f;
        internal const float RunnerDistanceYards = 19f;
        internal const float QuarterbackDistanceYards = 30f;
        internal const float QuarterbackLeadYards = 6f;
        internal const float QuarterbackLateralScale = 0.68f * 0.82f;
        internal const float QuarterbackLateralDistance = 0.40f;
        internal const float TrackingBufferYards = 2f / ArchiveUnitsPerYard;
        internal const float SafeFrame = 0.88f;
        private const float MaximumFrameSeconds = 0.1f;
        private const float BackLimitYards = 468f / ArchiveUnitsPerYard + 6f / 3f;
        private static readonly double Pitch = PitchDegrees * Math.PI / 180d;
        private static readonly float SinPitch = (float)Math.Sin(Pitch);
        private static readonly float CosPitch = (float)Math.Cos(Pitch);
        private static readonly double Alpha = 20.556d / 1.3d * Math.PI / 180d;
        private static readonly float SinAlpha = (float)Math.Sin(Alpha);
        private static readonly float SinDelta = (float)Math.Sin(Math.PI - Alpha - Pitch);
        // Dominant pole of the source's acceleration=.08, velocity retention=.35 update at 57 Hz.
        // A non-overshooting real-time response replaces the hardware scheduler, not gameplay time.
        private static readonly double FollowRate = -57d * Math.Log(
            (1d - 0.08d + 0.35d + Math.Sqrt(Math.Pow(1d - 0.08d + 0.35d, 2d) - 4d * 0.35d)) / 2d);

        private readonly float actorHeight;
        private readonly float actorRadius;
        private FieldVector focus;
        private float distance;
        private int direction = 1;
        private int lastPassTarget = -1;
        private long lastTick;
        private int lastPlay;
        private GamePhase lastPhase;
        private bool initialized;

        internal NormalCameraModel(float actorHeightYards, float actorRadiusYards)
        {
            if (!Finite(actorHeightYards) || actorHeightYards <= 0f
                || !Finite(actorRadiusYards) || actorRadiusYards <= 0f)
                throw new ArgumentOutOfRangeException("actorHeightYards", "Camera envelopes must be positive finite yard measurements.");
            actorHeight = actorHeightYards;
            actorRadius = actorRadiusYards;
        }

        internal NormalCameraFrame Update(IGameState state, float aspect, float deltaSeconds, bool immediate)
        {
            if (state == null)
                throw new ArgumentNullException("state");
            if (!Finite(aspect) || aspect <= 0f)
                aspect = 512f / 384f;
            aspect = Math.Max(0.01f, aspect);
            bool setup = IsSetup(state.Phase);
            bool stopped = state.Phase == GamePhase.DeadBall || state.Phase == GamePhase.GameOver;
            bool restarted = initialized && (state.Tick < lastTick || state.PlayNumber < lastPlay);
            int previousDirection = direction;
            int nextDirection = state.OffenseDirection < 0 ? -1 : 1;
            if (!initialized || restarted)
            {
                // Dead-ball rule fields already describe the NEXT possession in the slice.
                bool possessionChanged = state.LastOutcome == PlayOutcome.Touchdown
                    || state.LastOutcome == PlayOutcome.Safety || state.LastOutcome == PlayOutcome.TurnoverOnDowns;
                direction = stopped && possessionChanged ? -nextDirection : nextDirection;
                lastPassTarget = -1;
            }
            else if (!stopped)
                direction = nextDirection;
            bool reset = immediate || !initialized || restarted
                || (setup && !IsSetup(lastPhase)) || (setup && nextDirection != previousDirection);
            if (!Finite(deltaSeconds) || deltaSeconds < 0f)
                deltaSeconds = 0f;
            if (deltaSeconds > MaximumFrameSeconds)
            {
                deltaSeconds = 0f;
                reset = true;
            }
            if (setup)
                lastPassTarget = -1;
            else if (state.Ball.Mode == BallMode.InFlight)
                lastPassTarget = state.Ball.TargetPlayerId;

            float desiredDistance;
            FieldVector desired = DesiredFocus(state, setup, stopped, out desiredDistance);
            if (reset)
            {
                focus = desired;
                distance = desiredDistance;
            }
            else
            {
                float blend = (float)(1d - Math.Exp(-FollowRate * deltaSeconds));
                focus = new FieldVector(Follow(focus.X, desired.X, blend), Follow(focus.Z, desired.Z, blend));
                distance = Follow(distance, desiredDistance, blend);
            }

            // The historical camera assumes its own formations, actor bounds and fixed arcade viewport.
            // Fit the current slice's envelopes in the HUD-free viewport; do not frame every distant defender.
            distance = Math.Max(distance, RequiredDistance(state, setup, aspect));
            initialized = true;
            lastTick = state.Tick;
            lastPlay = state.PlayNumber;
            lastPhase = state.Phase;
            return new NormalCameraFrame(focus, distance, aspect, direction);
        }

        private FieldVector DesiredFocus(IGameState state, bool setup, bool stopped, out float desiredDistance)
        {
            PlayerView player;
            FieldVector ball = Center(state.Ball.Position);
            if (setup)
            {
                int quarterbackId = (int)state.Possession * SliceRules.PlayersPerTeam + 3;
                float z = TryPlayer(state, quarterbackId, out player) ? player.Position.Z : ball.Z;
                desiredDistance = QuarterbackDistanceYards;
                return new FieldVector(state.LineOfScrimmage - 50f, z);
            }

            FieldVector primary = ball;
            if (stopped && state.LastOutcome == PlayOutcome.Incomplete && TryPlayer(state, lastPassTarget, out player))
                primary = Center(player.Position);
            bool quarterback = !stopped && state.Ball.Mode == BallMode.Held
                && state.Ball.CarrierId == (int)state.Possession * SliceRules.PlayersPerTeam + 3
                && direction * (state.Ball.Position.X - state.LineOfScrimmage) <= 0f;
            bool pass = !stopped && state.Ball.Mode == BallMode.InFlight;
            FieldVector desired = primary;
            desiredDistance = RunnerDistanceYards;
            if (quarterback)
            {
                desired = new FieldVector(primary.X + direction * QuarterbackLeadYards,
                    primary.Z * QuarterbackLateralScale);
                desiredDistance = QuarterbackDistanceYards + Math.Abs(primary.Z) * QuarterbackLateralDistance;
            }
            else if (pass)
            {
                if (TryPlayer(state, state.Ball.TargetPlayerId, out player))
                {
                    // The contract has the receiver's current position, not the archive's ball.tx/tz lead point.
                    FieldVector receiver = Center(player.Position);
                    FieldVector biasedBall = new FieldVector(ball.X * 0.75f + receiver.X * 0.25f,
                        ball.Z * 0.75f + receiver.Z * 0.25f);
                    desired = SpanFocus(biasedBall, receiver);
                }
                primary = desired;
                desired = new FieldVector(desired.X + direction * QuarterbackLeadYards, desired.Z);
                desiredDistance = QuarterbackDistanceYards + Math.Abs(ball.Z) * QuarterbackLateralDistance;
            }
            return LimitFocus(desired, primary, desiredDistance, quarterback || pass);
        }

        private FieldVector SpanFocus(FieldVector first, FieldVector second)
        {
            float firstDepth = direction * first.X;
            float secondDepth = direction * second.X;
            float firstSide = -direction * first.Z;
            float secondSide = -direction * second.Z;
            float interceptMin = Math.Min(firstSide + firstDepth * CosPitch / 2f, secondSide + secondDepth * CosPitch / 2f);
            float interceptMax = Math.Max(firstSide - firstDepth * CosPitch / 2f, secondSide - secondDepth * CosPitch / 2f);
            float minimumDepth = Math.Min(firstDepth, secondDepth);
            float depth = minimumDepth + Math.Abs(firstDepth - secondDepth) * SinPitch / 2f;
            return new FieldVector(direction * depth, -direction * (interceptMin + interceptMax) / 2f);
        }

        private FieldVector LimitFocus(FieldVector desired, FieldVector primary, float desiredDistance, bool quarterbackOrPass)
        {
            float depth = direction * desired.X;
            float side = -direction * desired.Z;
            float targetToGround = FocusHeightYards / SinPitch;
            float lowerGroundExtent = (desiredDistance + targetToGround) * SinAlpha / SinDelta - targetToGround * CosPitch;
            depth = Math.Max(depth, lowerGroundExtent - BackLimitYards);
            float sideLimit = SliceRules.FieldHalfWidthYards + (quarterbackOrPass ? 14f : 8f) / 3f;
            if (quarterbackOrPass)
                sideLimit += CosPitch * (depth - direction * primary.X) / 2f;
            float centerLimit = Math.Max(0f, sideLimit - desiredDistance / 2f);
            side = Math.Max(-centerLimit, Math.Min(centerLimit, side));
            return new FieldVector(direction * depth, -direction * side);
        }

        private float RequiredDistance(IGameState state, bool setup, float aspect)
        {
            float tanVertical = Math.Max(192f / ReferenceFocalPixels, 256f / ReferenceFocalPixels / aspect);
            float tanHorizontal = tanVertical * aspect;
            float required = 0f;
            float ballHeight = Math.Max(state.Ball.Mode == BallMode.Held ? 1.15f : 0.3f, state.Ball.Height);
            IncludeBox(Center(state.Ball.Position), ballHeight - 0.4f, ballHeight + 0.4f,
                state.Ball.Mode == BallMode.Held ? actorRadius : 0.5f, tanHorizontal, tanVertical, ref required);
            for (int i = 0; i < state.Players.Count; i++)
            {
                PlayerView player = state.Players[i];
                float dx = player.Position.X - state.Ball.Position.X;
                float dz = player.Position.Z - state.Ball.Position.Z;
                bool relevant = setup ? player.Team == state.Possession
                    : dx * dx + dz * dz <= RunnerDistanceYards * RunnerDistanceYards / 4f
                        || (state.Ball.Mode == BallMode.InFlight && player.Id == state.Ball.TargetPlayerId);
                if (relevant)
                    IncludeBox(Center(player.Position), 0f, actorHeight, actorRadius, tanHorizontal, tanVertical, ref required);
            }
            return required;
        }

        private void IncludeBox(FieldVector center, float bottom, float top, float radius,
            float tanHorizontal, float tanVertical, ref float required)
        {
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    for (int y = 0; y < 2; y++)
                    {
                        float dx = center.X + x * radius - focus.X;
                        float dz = center.Z + z * radius - focus.Z;
                        float dy = (y == 0 ? bottom : top) - FocusHeightYards;
                        float forward = direction * dx * CosPitch - dy * SinPitch;
                        float up = direction * dx * SinPitch + dy * CosPitch;
                        float horizontal = Math.Abs(dz) / (tanHorizontal * SafeFrame) - forward;
                        float vertical = Math.Abs(up) / (tanVertical * SafeFrame) - forward;
                        required = Math.Max(required, Math.Max(horizontal, Math.Max(vertical, 0.5f - forward)));
                    }
        }

        private static float Follow(float current, float target, float blend)
        {
            float difference = target - current;
            float excess = Math.Max(0f, Math.Abs(difference) - TrackingBufferYards);
            return current + Math.Sign(difference) * excess * blend;
        }

        private static bool TryPlayer(IGameState state, int id, out PlayerView player)
        {
            if (id >= 0 && id < state.Players.Count)
            {
                player = state.Players[id];
                return player.Id == id;
            }
            player = default(PlayerView);
            return false;
        }

        private static FieldVector Center(FieldVector point)
        {
            return new FieldVector(point.X - 50f, point.Z);
        }

        private static bool IsSetup(GamePhase phase)
        {
            return phase == GamePhase.PlaySelect || phase == GamePhase.PreSnap;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
