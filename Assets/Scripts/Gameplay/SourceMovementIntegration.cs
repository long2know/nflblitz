using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public sealed class SourceMovementDefenseRow
    {
        public ReadOnlyCollection<float> LineSpeeds { get; }
        public ReadOnlyCollection<float> BackSpeeds { get; }
        public int AccelerationBase { get; }
        public int AccelerationRange { get; }
        public int PlowProbability { get; }

        public SourceMovementDefenseRow(IReadOnlyList<float> lineSpeeds, IReadOnlyList<float> backSpeeds,
            int accelerationBase, int accelerationRange, int plowProbability)
        {
            LineSpeeds = Copy(lineSpeeds, 6, nameof(lineSpeeds));
            BackSpeeds = Copy(backSpeeds, 6, nameof(backSpeeds));
            if (accelerationBase < 0 || accelerationRange < 0 || plowProbability < 0 || plowProbability > 100)
                throw new ArgumentOutOfRangeException(nameof(accelerationBase));
            AccelerationBase = accelerationBase; AccelerationRange = accelerationRange;
            PlowProbability = plowProbability;
        }

        internal static ReadOnlyCollection<float> Copy(IReadOnlyList<float> values, int count, string name)
        {
            if (values == null || values.Count != count)
                throw new ArgumentException("Supply all original " + count + " entries.", name);
            var copy = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (!SourcePoseMath.IsFinite(values[i])) throw new ArgumentOutOfRangeException(name);
                copy[i] = values[i];
            }
            return Array.AsReadOnly(copy);
        }
    }

    public sealed class SourceMovementRuntimeData
    {
        public SourceMovementData Movement { get; }
        public ReadOnlyCollection<float> Friction { get; }
        public ReadOnlyCollection<SourceMovementDefenseRow> Defense { get; }

        public SourceMovementRuntimeData(SourceMovementData movement, IReadOnlyList<float> friction,
            IReadOnlyList<SourceMovementDefenseRow> defense)
        {
            Movement = movement ?? throw new ArgumentNullException(nameof(movement));
            Friction = SourceMovementDefenseRow.Copy(friction, 7, nameof(friction));
            if (defense == null || defense.Count != 12)
                throw new ArgumentException("Supply all twelve actual handicap rows.", nameof(defense));
            var copy = new SourceMovementDefenseRow[12];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = defense[i] ?? throw new ArgumentException("Missing actual handicap row " + i + ".", nameof(defense));
            Defense = Array.AsReadOnly(copy);
        }
    }

    public sealed class SourceMovementSettings
    {
        public int FieldIndex { get; }
        public int InfiniteTurboTeamMask { get; }
        public bool SpeedPowerup { get; }
        public int FastTurboTeamMask { get; }
        public int RunOutOfBoundsTeamMask { get; }
        public bool ViolencePowerup { get; }
        public int BlockTeamMask { get; }
        public int BlitzersTeamMask { get; }

        public SourceMovementSettings(int fieldIndex, int infiniteTurboTeamMask, bool speedPowerup,
            int fastTurboTeamMask, int runOutOfBoundsTeamMask, bool violencePowerup,
            int blockTeamMask = 0, int blitzersTeamMask = 0)
        {
            if (fieldIndex < 0 || fieldIndex >= 7) throw new ArgumentOutOfRangeException(nameof(fieldIndex));
            if ((infiniteTurboTeamMask & ~3) != 0) throw new ArgumentOutOfRangeException(nameof(infiniteTurboTeamMask));
            if ((fastTurboTeamMask & ~3) != 0) throw new ArgumentOutOfRangeException(nameof(fastTurboTeamMask));
            if ((runOutOfBoundsTeamMask & ~3) != 0) throw new ArgumentOutOfRangeException(nameof(runOutOfBoundsTeamMask));
            if ((blockTeamMask & ~3) != 0) throw new ArgumentOutOfRangeException(nameof(blockTeamMask));
            if ((blitzersTeamMask & ~3) != 0) throw new ArgumentOutOfRangeException(nameof(blitzersTeamMask));
            FieldIndex = fieldIndex; InfiniteTurboTeamMask = infiniteTurboTeamMask;
            SpeedPowerup = speedPowerup; FastTurboTeamMask = fastTurboTeamMask;
            RunOutOfBoundsTeamMask = runOutOfBoundsTeamMask; ViolencePowerup = violencePowerup;
            BlockTeamMask = blockTeamMask; BlitzersTeamMask = blitzersTeamMask;
        }
    }

    public sealed class SourceMovementCameraFrame
    {
        public SourceVector3 Position { get; }
        public ReadOnlyCollection<float> Transform { get; }
        public float HorizontalResolution { get; }
        public float VerticalResolution { get; }

        public SourceMovementCameraFrame(SourceVector3 position, IReadOnlyList<float> transform,
            float horizontalResolution, float verticalResolution)
        {
            if (!SourcePoseMath.IsFinite(position.X) || !SourcePoseMath.IsFinite(position.Y)
                || !SourcePoseMath.IsFinite(position.Z)) throw new ArgumentOutOfRangeException(nameof(position));
            Position = position;
            Transform = SourceMovementDefenseRow.Copy(transform, 12, nameof(transform));
            if (!SourcePoseMath.IsFinite(horizontalResolution) || !SourcePoseMath.IsFinite(verticalResolution)
                || horizontalResolution <= 0f || verticalResolution <= 0f)
                throw new ArgumentOutOfRangeException(nameof(horizontalResolution));
            HorizontalResolution = horizontalResolution; VerticalResolution = verticalResolution;
        }
    }

    public interface ISourceActorCameraGeometry
    {
        IReadOnlyList<SourceVector3> CameraJoints(PlayerView actor, SourceVector3 canonicalRoot,
            SourceMovementCameraFrame camera);
    }

    public sealed partial class GameSimulation
    {
        private const int SourceDefenseSwitchDeferred = -2;
        private int sourceMovementEvasionStage;
        private SourceMovementTick MovementTick { get { return new SourceMovementTick(Model.SourceTick, true); } }

        private SourceMovementPhase MovementPhase
        {
            get
            {
                return Model.Phase == GamePhase.LiveBall ? SourceMovementPhase.InPlay
                    : Model.Phase == GamePhase.DeadBall ? SourceMovementPhase.PlayOver
                    : Model.Phase == GamePhase.GameOver ? SourceMovementPhase.GameOver
                    : Model.Phase == GamePhase.PreSnap ? Model.SourceRules.PlayType == SourcePlayType.Kickoff
                        ? SourceMovementPhase.PreKickoff : SourceMovementPhase.PreSnap : SourceMovementPhase.LiningUp;
            }
        }

        private SourceMovementState CaptureSourceMovement(ActorState actor)
        {
            SourceVector3 root = SourcePlayerDriver.WorldPosition(actor);
            return new SourceMovementState(actor.Id, actor.SourceControllerStation, actor.SourceMode) {
                Job = actor.SourceJob, Flags = (SourceMovementFlags)GameSnapshot.SourceActorFlags(actor),
                LowerMode = (SourceMovementModes)actor.AnimationState.Primary.Mode,
                UpperMode = (SourceMovementModes)actor.AnimationState.Secondary.Mode,
                LowerSequence = actor.AnimationState.Primary.SequenceName ?? "",
                UpperSequence = actor.AnimationState.Secondary.SequenceName ?? "",
                X = root.X, Y = root.Y, Z = root.Z,
                VelocityX = actor.SourceVelocity.X, VelocityY = actor.SourceVerticalVelocity,
                VelocityZ = actor.SourceVelocity.Z, Friction = actor.SourceFriction,
                Heading = actor.Facing, TargetHeading = actor.TargetFacing,
                UpperHeading = actor.UpperFacing, UpperTargetHeading = actor.TargetUpperFacing,
                TurnRate = actor.SourceTurnRate, UpperTurnRate = actor.SourceUpperTurnRate,
                UpperTurnHack = actor.SourceUpperTurnHack,
                LowerVariableFrame = actor.AnimationState.Primary.VariableFrame,
                LowerVariableCount = actor.AnimationState.Primary.VariableCount,
                UpperVariableFrame = actor.AnimationState.Secondary.VariableFrame,
                UpperVariableCount = actor.AnimationState.Secondary.VariableCount,
                Stick = actor.SourceStick, PreviousStick = actor.SourcePreviousStick, StickTime = actor.SourceStickTime,
                Buttons = actor.SourceButtons, OldButtons = actor.SourceOldButtons, NewButtons = actor.SourceNewButtons,
                NonTurboTime = actor.NonTurboTicks, NonATime = actor.SourceNonATicks, NonBTime = actor.SourceNonBTicks,
                BackpedalExpireTime = actor.SourceBackpedalExpireTime,
                AttackTime = actor.SourceAttackTime, AttackMode = actor.SourceAttackMode,
                CelebrateTime = actor.SourceCelebrateTime, OverrideTime = actor.SourceOverrideTime,
                NoCatchTime = actor.NoCatchTicks, OverrideSpeed = actor.SourceOverrideSpeed,
                DroneFace = actor.SourceDroneFace,
                TeamSpinCount = Model.SourceSpinCount[(int)actor.Team],
                TeamHurdleCount = Model.SourceHurdleCount[(int)actor.Team]
            };
        }

        private void ApplySourceMovement(ActorState actor, SourceMovementState state)
        {
            SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(state.X, state.Y, state.Z));
            actor.SourceControllerStation = state.Station; actor.SourceMode = state.Mode; actor.SourceJob = state.Job;
            actor.SourceRawFlags = (int)state.Flags;
            actor.Flipped = (state.Flags & SourceMovementFlags.Flip) != 0;
            actor.Turbo = (state.Flags & SourceMovementFlags.Turbo) != 0;
            actor.SourceReverseRoute = (state.Flags & SourceMovementFlags.ReverseRoute) != 0;
            actor.SourceBackpedal = (state.Flags & SourceMovementFlags.Backpedal) != 0;
            actor.SourceLateHittable = (state.Flags & SourceMovementFlags.LateHittable) != 0;
            actor.SourceWaveArm = (state.Flags & SourceMovementFlags.WaveArm) != 0;
            actor.SourceUnblockable = (state.Flags & SourceMovementFlags.Unblockable) != 0;
            actor.SourceThrowing = (state.Flags & SourceMovementFlags.Throwing) != 0;
            actor.SourcePendingFireCheck = (state.Flags & SourceMovementFlags.PendingFireCheck) != 0;
            actor.SourceHelmetPopped = (state.Flags & SourceMovementFlags.PopHelmet) != 0;
            actor.AnimationState.Primary.SetMovementMode((uint)state.LowerMode);
            actor.AnimationState.Secondary.SetMovementMode((uint)state.UpperMode);
            actor.SourceVelocity = new FieldVector(state.VelocityX, state.VelocityZ);
            actor.SourceVerticalVelocity = state.VelocityY; actor.SourceFriction = state.Friction;
            actor.Facing = state.Heading; actor.TargetFacing = state.TargetHeading;
            actor.UpperFacing = state.UpperHeading; actor.TargetUpperFacing = state.UpperTargetHeading;
            actor.SourceTurnRate = state.TurnRate; actor.SourceUpperTurnRate = state.UpperTurnRate;
            actor.SourceUpperTurnHack = state.UpperTurnHack;
            actor.AnimationState.Secondary.SetMovementTurnHack(state.UpperTurnHack);
            actor.AnimationState.Primary.VariableFrame = state.LowerVariableFrame;
            actor.AnimationState.Primary.VariableCount = state.LowerVariableCount;
            actor.AnimationState.Secondary.VariableFrame = state.UpperVariableFrame;
            actor.AnimationState.Secondary.VariableCount = state.UpperVariableCount;
            actor.SourceStick = state.Stick; actor.SourcePreviousStick = state.PreviousStick;
            actor.SourceStickTime = state.StickTime;
            actor.SourceButtons = state.Buttons; actor.SourceOldButtons = state.OldButtons;
            actor.SourceNewButtons = state.NewButtons; actor.NonTurboTicks = state.NonTurboTime;
            actor.SourceNonATicks = state.NonATime; actor.SourceNonBTicks = state.NonBTime;
            actor.SourceBackpedalExpireTime = state.BackpedalExpireTime;
            actor.SourceAttackTime = state.AttackTime; actor.SourceAttackMode = state.AttackMode;
            actor.SourceCelebrateTime = state.CelebrateTime; actor.SourceOverrideTime = state.OverrideTime;
            actor.NoCatchTicks = state.NoCatchTime; actor.SourceOverrideSpeed = state.OverrideSpeed;
            actor.SourceDroneFace = state.DroneFace;
            actor.SourceSpinCount = Model.SourceSpinCount[(int)actor.Team] = state.TeamSpinCount;
            Model.SourceHurdleCount[(int)actor.Team] = state.TeamHurdleCount;
        }

        private SourceMovementContext MovementContext(ActorState actor)
        {
            if (Model.SourceDistances == null || Model.SourceAngles == null)
                throw new InvalidOperationException("M1 requires the actual pre-player distxz/p2p cache.");
            var neighbors = new SourceMovementNeighbor[14];
            for (int id = 0; id < 14; id++)
            {
                int bearing = (1024 + actor.Facing - Model.SourceAngles[actor.Id, id]) % 1024;
                if (bearing > 512) bearing = 1024 - bearing;
                neighbors[id] = new SourceMovementNeighbor(id, Model.SourceDistances[actor.Id, id], bearing);
            }
            int back = actor.SourceBackpedalTarget;
            SourceVector3 root = SourcePlayerDriver.WorldPosition(actor);
            float dx = Model.SourceBallPosition.X - root.X, dz = Model.SourceBallPosition.Z - root.Z;
            return new SourceMovementContext(MovementTick, sourcePlaybook.Directions, (int)Model.Possession,
                MovementPhase, Model.SourceFlags, Model.SourceRules.PlayType, Model.SourceOffensePlay.Flags,
                Model.SourceDefensePlay.Flags, Model.CarrierId,
                Model.CarrierId < 0 ? SourceActorMode.Dead : Model.Players[Model.CarrierId].SourceMode,
                SourceBallTypeFor(Model.BallKind), Model.TargetPlayerId, Model.PassTicksTotal - Model.PassTicksElapsed,
                Model.SourceBallPosition.X, Model.SourceBallPosition.Y, (float)Math.Sqrt(dx * dx + dz * dz),
                Model.SourceCameraHeading, Model.SourceRules.LineOfScrimmage,
                Model.SourceLinePosition * SourceTiming.SourceUnitsPerYard, actor.SourceMotionSnapX,
                back < 0 ? -1 : Model.SourceAngles[actor.Id, back],
                Model.SourceStationTurbo[(int)actor.Team], Model.SourceTeamFire[1 - (int)Model.Possession],
                sourceMovementSettings.SpeedPowerup, (sourceMovementSettings.FastTurboTeamMask & (1 << (int)actor.Team)) != 0,
                (sourceMovementSettings.RunOutOfBoundsTeamMask & (1 << (int)actor.Team)) != 0, sourceMovementSettings.ViolencePowerup,
                Model.CarrierId >= 0 && Model.Players[Model.CarrierId].SourceLateHittable,
                Model.CarrierId < 0 ? 0f : Model.SourceDistances[actor.Id, Model.CarrierId], Model.SourceBallTarget.X,
                actor.SourceRequestedScript, actor.SourceRequestedScript == null
                    || !actor.SourceRequestedScript.StartsWith("c2p_", StringComparison.Ordinal),
                neighbors, sourceMovementData.Defense[actor.SourceHandicap].PlowProbability);
        }

        private SourceRandomContext MovementRandomContext(ActorState actor)
        {
            if (Model.SourceRandomActor != actor.Id)
                BeginSourceProcess(Model.SourceRandomProcess + "/animation", actor.Id, Model.SourceRandomProcessOrder);
            return CurrentSourceRandomContext();
        }

        private void CompleteSourceMovement(ActorState actor, SourceMovementResult result,
            SourceRandomContext context, long drawsBefore)
        {
            ApplySourceMovement(actor, result.State);
            Model.SourceRandomCallIndex = checked(context.FirstCallIndex + result.RandomCalls);
            Model.SourceRandomDraws = drawsBefore + result.RandomCalls;
            if (result.BreathControl.HasValue) Model.SourceBreathControl = result.BreathControl.Value;
        }

        private SourceMovementResult RunSourceMovement(ActorState actor, bool general = false)
        {
            SourceRandomContext random = MovementRandomContext(actor);
            long draws = Model.SourceRandomDraws;
            SourceMovementResult result = general ? SourceMovement.GeneralRun(CaptureSourceMovement(actor),
                MovementContext(actor), sourceMovementData.Movement, new MovementBoundary(this), sourceRandom,
                random, Model.SourceBreathControl)
                : actor.SourceMode == SourceActorMode.Quarterback ? SourceMovement.QuarterbackRun(CaptureSourceMovement(actor),
                    MovementContext(actor), sourceMovementData.Movement, new MovementBoundary(this), sourceRandom, random)
                : SourceMovement.StandardRun(CaptureSourceMovement(actor), MovementContext(actor),
                    sourceMovementData.Movement, new MovementBoundary(this), sourceRandom, random, Model.SourceBreathControl);
            CompleteSourceMovement(actor, result, random, draws);
            return result;
        }

        private SourceEvasionResult RunSourceEvasion(ActorState actor, int stage, bool entered)
        {
            sourceMovementEvasionStage = stage;
            SourceRandomContext random = MovementRandomContext(actor);
            long draws = Model.SourceRandomDraws;
            SourceMovementState state = CaptureSourceMovement(actor);
            SourceMovementContext context = MovementContext(actor);
            var boundary = new MovementBoundary(this);
            SourceEvasionResult result = stage == 0 ? SourceEvasion.WeaponBeforeRun(state, context,
                sourceMovementData.Movement, boundary, sourceRandom, random)
                : stage == 1 ? SourceEvasion.WeaponAfterRun(state, context, sourceMovementData.Movement,
                    boundary, sourceRandom, random, entered)
                : SourceEvasion.QuarterbackAfterRun(state, context, boundary, sourceRandom, random, entered);
            CompleteSourceMovement(actor, result.Movement, random, draws);
            return result;
        }

        private sealed class MovementBoundary : ISourceMovementBoundary
        {
            private readonly GameSimulation game;
            internal MovementBoundary(GameSimulation game) { this.game = game; }
            public SourceMovementBoundaryResult Apply(SourceMovementState state, SourceMovementCommand command,
                SourceRandomContext context, ISourceRandom random)
            {
                if (!ReferenceEquals(random, game.sourceRandom) && random != null)
                    throw new InvalidOperationException("M1 boundary must use the actual shared source RNG.");
                ActorState actor = game.Model.Players[state.PlayerId];
                game.ApplySourceMovement(actor, state);
                game.Model.SourceRandomCallIndex = context.FirstCallIndex;
                switch (command.Kind)
                {
                    case SourceMovementCommandKind.BeginTwoPart:
                        actor.AnimationState.BeginTwoPart(actor);
                        break;
                    case SourceMovementCommandKind.FullBody:
                        actor.Action = command.Script.IndexOf("hurd", StringComparison.Ordinal) >= 0 ? ActorAction.Hurdle
                            : command.Script.IndexOf("spin", StringComparison.Ordinal) >= 0 ? ActorAction.Spin
                            : command.Script.IndexOf("div", StringComparison.Ordinal) >= 0
                                || command.Script.IndexOf("slide", StringComparison.Ordinal) >= 0 ? ActorAction.Dive
                            : actor.SourceStick == 24 ? ActorAction.Idle : actor.Turbo ? ActorAction.TurboRun : ActorAction.Run;
                        actor.AnimationState.FullBody(command.Script, actor, game.Model.SourceTick, game.SourceDraw,
                            AnimationSelectionEvidence.SourceVerified);
                        break;
                    case SourceMovementCommandKind.LowerBody:
                        actor.AnimationState.LowerBody(command.Script, actor, game.Model.SourceTick, game.SourceDraw);
                        break;
                    case SourceMovementCommandKind.UpperBody:
                        actor.AnimationState.UpperBody(command.Script, actor, game.Model.SourceTick, game.SourceDraw,
                            AnimationSelectionEvidence.SourceVerified);
                        break;
                    case SourceMovementCommandKind.AbortRoute:
                        game.Model.SourceDrones[actor.Id] = SourceDroneAI.AbortRoute(game.Model.SourceDrones[actor.Id], game.sourcePlaybook);
                        break;
                    case SourceMovementCommandKind.ChangeWeaponPlayer:
                        int value = game.ChangeSourceWeaponPlayer(actor);
                        return new SourceMovementBoundaryResult(game.CaptureSourceMovement(actor),
                            game.Model.SourceRandomCallIndex - context.FirstCallIndex, value);
                    case SourceMovementCommandKind.SpinSpeech:
                    case SourceMovementCommandKind.HurdleSpeech:
                    case SourceMovementCommandKind.StiffArmSpeech:
                        game.EmitSourceMovementSpeech(actor, command.Kind);
                        break;
                    default: throw new NotSupportedException("Unbound original M1 command " + command.Kind);
                }
                return new SourceMovementBoundaryResult(game.CaptureSourceMovement(actor),
                    game.Model.SourceRandomCallIndex - context.FirstCallIndex);
            }
        }

        private void UpdateSourceStationMovementResources(PlayerCommand command)
        {
            foreach (SourceStationConfiguration station in sourceConfiguration.Stations)
            {
                int id = Model.SourceStationPlayers[station.StationId];
                bool held = id >= 0 && SourcePlayerDriver.ActorCommand(Model, Model.Players[id], command).Turbo;
                Model.SourceStationTurbo[station.StationId] = SourceMovement.StationTurbo(MovementTick,
                    Model.SourceStationTurbo[station.StationId], true, id >= 0, MovementPhase,
                    Model.SourceTeamFire[(int)station.Team],
                    (sourceMovementSettings.InfiniteTurboTeamMask & (1 << (int)station.Team)) != 0,
                    held, id >= 0 && Model.Players[id].Team != Model.Possession);
            }
            if (sourceConfiguration.Stations.Count > 0)
                Model.SourceHumanTurbo = Model.SourceStationTurbo[sourceConfiguration.Stations[0].StationId];
        }

        private void CacheSourcePlayerCameraJoints()
        {
            var geometry = sourceGeometry as ISourceActorCameraGeometry;
            SourceMovementCameraFrame frame = Model.SourceMovementCamera;
            if (geometry == null || frame == null || Model.SourceMovementCameraTick != Model.SourceTick)
                throw new InvalidOperationException("Missing actual current source camera geometry/cache generation.");
            for (int id = 0; id < 14; id++)
            {
                ActorState actor = Model.Players[id];
                SourceVector3 root = SourcePlayerDriver.WorldPosition(actor);
                IReadOnlyList<SourceVector3> joints = geometry.CameraJoints(
                    GameSnapshot.SourcePlayerView(actor, Model.SourceTick, true), root, frame);
                if (joints == null || joints.Count != 15)
                    throw new InvalidOperationException("The original post-player camera cache requires all fifteen actual joints.");
                for (int joint = 0; joint < 15; joint++) Model.SourceCameraJoints[id, joint] = joints[joint];
                RetainSourceFireCache(id, joints, frame);
                float rx = root.X - frame.Position.X;
                float ry = root.Y + actor.SourceRoot.Y - frame.Position.Y;
                float rz = root.Z - frame.Position.Z;
                ReadOnlyCollection<float> cam = frame.Transform;
                float x = cam[0] * rx + cam[1] * ry + cam[2] * rz + cam[3];
                float y = cam[4] * rx + cam[5] * ry + cam[6] * rz + cam[7];
                float z = cam[8] * rx + cam[9] * ry + cam[10] * rz + cam[11];
                float xf = z * ((0.5f + 120f / frame.HorizontalResolution) / 1.3f);
                float yf = z * ((frame.VerticalResolution + 2f * 120f) / (2f * frame.HorizontalResolution * 1.3f));
                if (z < 28f || Math.Abs(x) > xf || Math.Abs(y) > yf) actor.SourceRawFlags &= ~0x2;
                else actor.SourceRawFlags |= 0x2;
            }
            Model.SourceCameraJointTick = Model.SourceTick;
        }

        private void UpdateSourceMovementPlayers(PlayerCommand command)
        {
            for (int id = 0; id < Model.Players.Length; id++)
            {
                BeginSourceProcess("player_proc", id, id + 1);
                ActorState actor = Model.Players[id];
                FieldVector before = actor.Position;
                SynchronizeSourceAnimationActor(actor);
                actor.SourceBallKicked = (Model.SourceFlags & SourceGameFlags.BallKicked) != 0;
                actor.SourceBallTarget = Model.PassTarget;
                actor.SourceBallLocation = new FieldVector(SourceMatchRules.AbsoluteYardsFromSourceWorld(Model.SourceBallOrigin.X),
                    Model.SourceBallOrigin.Z / SourceTiming.SourceUnitsPerYard);
                actor.SourceBallTimeRemaining = Model.PassTicksTotal - Model.PassTicksElapsed;
                SourceMovementState previous = CaptureSourceMovement(actor);
                for (int h = actor.SourceButtonHistory.Length - 1; h > 0; h--)
                    actor.SourceButtonHistory[h] = actor.SourceButtonHistory[h - 1];
                actor.SourceButtonHistory[0] = actor.SourceButtons;
                actor.SourceOldButtons = actor.SourceButtons;
                actor.SourcePreviousStick = actor.SourceStick;
                actor.SourceBackpedal = false;
                PlayerCommand input = SourcePlayerDriver.ActorCommand(Model, actor, command);
                SourceMovementContext context = MovementContext(actor);
                bool human = SourceMovement.HumanControls(previous, context);
                if (actor.SourceControllerStation >= 0)
                {
                    actor.SourceButtons = input.Turbo ? SourceDroneButtons.C : SourceDroneButtons.None;
                    bool raw = false;
                    if (input.StationInputs != null)
                        foreach (SourceStationInput station in input.StationInputs.Stations)
                            if (station.StationId == actor.SourceControllerStation)
                            { actor.SourceButtons = (SourceDroneButtons)(int)station.Held; raw = true; break; }
                    if (!raw)
                    {
                        SourceAction action = SourceActionFor(input);
                        if (action == SourceAction.Pass || action == SourceAction.Snap || action == SourceAction.Lateral
                            || action == SourceAction.SwitchPlayer) actor.SourceButtons |= SourceDroneButtons.B;
                        if (action == SourceAction.Catch || action == SourceAction.Jump || action == SourceAction.Hurdle
                            || action == SourceAction.Dive || action == SourceAction.Tackle) actor.SourceButtons |= SourceDroneButtons.A;
                    }
                    actor.SourceNewButtons = actor.SourceButtons & ~actor.SourceOldButtons;
                    if (input.StationInputs != null)
                        foreach (SourceStationInput station in input.StationInputs.Stations)
                            if (station.StationId == actor.SourceControllerStation)
                                actor.SourceNewButtons |= (SourceDroneButtons)(int)station.Pressed;
                }
                if (human) actor.SourceStick = SourcePlayerDriver.ActorStick(actor, input);
                else SourceDroneMove(actor, input);
                SourceMovementSwitches acquired = new SourceMovementSwitches(actor.SourceStick, actor.SourceButtons,
                    actor.SourceBackpedal, actor.SourceControllerStation < 0 ? 100f
                        : Model.SourceStationTurbo[actor.SourceControllerStation]);
                SourceMovementState state = CaptureSourceMovement(actor);
                state.Stick = previous.Stick; state.PreviousStick = previous.PreviousStick;
                state.Buttons = previous.Buttons;
                state.Flags = (state.Flags & ~(SourceMovementFlags.Turbo | SourceMovementFlags.Backpedal))
                    | (previous.Flags & (SourceMovementFlags.Turbo | SourceMovementFlags.Backpedal));
                state = SourceMovement.BeginTick(state, acquired, MovementContext(actor));
                actor.SourceReleasedButtons = previous.Buttons & ~acquired.Buttons;
                if (actor.SourceControllerStation >= 0 && input.StationInputs != null)
                    foreach (SourceStationInput station in input.StationInputs.Stations)
                        if (station.StationId == actor.SourceControllerStation)
                        {
                            state.NewButtons |= (SourceDroneButtons)(int)station.Pressed;
                            actor.SourceReleasedButtons |= (SourceDroneButtons)(int)station.Released;
                        }
                ApplySourceMovement(actor, state);
                actor.Turbo01 = actor.SourceControllerStation < 0 ? 1f
                    : Model.SourceStationTurbo[actor.SourceControllerStation] / 100f;
                SourcePlanar move = sourcePlaybook.Directions.Movement(actor.SourceStick, Model.SourceCameraHeading);
                actor.SourceMove = new FieldVector(move.X, move.Z);
                ExecuteSourceMovementMode(actor, input);
                actor.AnimationState.Advance(actor, Model.SourceTick, SourceDraw);
                IntegrateSourceMovementPlayer(actor);
                actor.Velocity = PlayerSimulation.Scale(PlayerSimulation.Subtract(actor.Position, before),
                    SourceTiming.DefaultTicksPerSecond);
            }
        }

        private void IntegrateSourceMovementPlayer(ActorState actor)
        {
            SourceRandomContext random = MovementRandomContext(actor);
            long draws = Model.SourceRandomDraws;
            SourceMovementResult physics = SourceMovement.Integrate(CaptureSourceMovement(actor), MovementContext(actor),
                sourceMovementData.Friction[sourceMovementSettings.FieldIndex], new MovementBoundary(this), random);
            CompleteSourceMovement(actor, physics, random, draws);
        }

        private void ExecuteSourceMovementMode(ActorState actor, PlayerCommand input)
        {
            actor.SourceReturnedFromMovementMode = false;
            SourceActorMode mode = actor.SourceMode;
            if (mode == SourceActorMode.Lineup || mode == SourceActorMode.Dead) return;
            if (mode == SourceActorMode.LinePreSnap)
            {
                if (Model.Phase == GamePhase.LiveBall)
                {
                    if (actor.Team != Model.Possession || actor.Id % 7 != 1)
                        actor.AnimationState.FullBody(actor.Team == Model.Possession ? "stance3pt_jumpback_anim" : "charge_anim",
                            actor, Model.SourceTick, SourceDraw, AnimationSelectionEvidence.SourceVerified);
                    actor.SourceMode = SourceActorMode.Line;
                }
                return;
            }
            if (mode == SourceActorMode.QuarterbackPreSnap)
            {
                if (actor.AnimationState.Primary.IsUninterruptible) return;
                Model.PreSnapTicksRemaining--;
                bool kick = (Model.SourceOffensePlay.Flags & (SourcePlayFlags.FieldGoal | SourcePlayFlags.FakeFieldGoal
                    | SourcePlayFlags.Punt | SourcePlayFlags.FakePunt)) != 0;
                int delay = SourceClock.DefaultAutoHikeTicks - (kick ? 10 : SourceClock.SourceTicksPerSecond);
                if (Model.PreSnapTicksRemaining < 1 || (actor.SourceNewButtons & (SourceDroneButtons.A | SourceDroneButtons.B)) != 0
                    && Model.PreSnapTicksRemaining < delay) SnapSource(Model.PreSnapTicksRemaining == 0);
                return;
            }
            if (mode == SourceActorMode.ReceiverPreSnap && Model.Phase == GamePhase.LiveBall)
                actor.SourceMode = SourceActorMode.Receiver;
            if (mode == SourceActorMode.DefensiveBackPreSnap && Model.Phase == GamePhase.LiveBall)
                actor.SourceMode = SourceActorMode.DefensiveBack;
            if (mode == SourceActorMode.Quarterback && Model.Phase == GamePhase.LiveBall)
            {
                bool awaitingHike = (Model.SourceFlags & SourceGameFlags.HikingBall) != 0
                    && (Model.CarrierId < 0 || Model.SourceOffensePlay.Flags != SourcePlayFlags.None);
                if (awaitingHike)
                {
                    TrySourceCatch(actor);
                    if (Model.CarrierId != actor.Id || actor.ModeChangedTick == Model.SourceTick) return;
                    if (actor.SourceStick == 24 && actor.SourcePreviousStick == 24)
                    { actor.AnimationState.Primary.AddMode(SourceAnimationModes.Uninterruptible); RunSourceMovement(actor); }
                }
                if ((Model.SourceFlags & SourceGameFlags.BallCrossedLine) != 0)
                {
                    if (!actor.AnimationState.Primary.IsUninterruptible)
                        actor.AnimationState.FullBody(Model.CarrierId == actor.Id ? "run_wb_anim" : "run_anim",
                            actor, Model.SourceTick, SourceDraw, AnimationSelectionEvidence.SourceVerified);
                    actor.SourceMode = SourceActorMode.Receiver; actor.Role = ActorRole.Receiver;
                    return;
                }
            }
            if (mode == SourceActorMode.Line && actor.Team == Model.Possession && Model.CarrierId >= 0
                && Model.Players[Model.CarrierId].SourceMode == SourceActorMode.Receiver)
            { actor.SourceMode = SourceActorMode.Receiver; actor.Role = ActorRole.Receiver; return; }
            if (mode != SourceActorMode.Quarterback)
            {
                actor.SourceTurnRate = 64;
                if (mode != SourceActorMode.Kicker) actor.SourceUpperTurnRate = 48;
            }
            if (mode == SourceActorMode.DefensiveBackPreSnap)
            {
                SourceVector3 root = SourcePlayerDriver.WorldPosition(actor);
                if (SourceMatchRules.FieldPosition(root.X, (int)Model.Possession) < 1 + Model.SourceRules.LineOfScrimmage)
                    SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(
                        SourceMatchRules.WorldPosition(1 + Model.SourceRules.LineOfScrimmage, (int)Model.Possession),
                        root.Y, root.Z));
                if (actor.SourceStick == 24) actor.TargetFacing = Model.Possession == TeamId.Away ? 256 : 768;
            }
            if (mode == SourceActorMode.Receiver)
            {
                if (actor.SourceControllerStation >= 0 && Model.CarrierId >= 0
                    && Model.Players[Model.CarrierId].SourceMode == SourceActorMode.Receiver)
                    actor.SourceJob = SourceActorJob.Block;
                if (Model.CarrierId == actor.Id && (Model.SourceFlags & SourceGameFlags.BallCrossedLine) != 0
                    && (Model.SourceFlags & SourceGameFlags.OverLineSpeedsSet) == 0) AssignSourceDefenseMovement(actor);
            }
            ApplySourceMovement(actor, SourceMovement.Turn(CaptureSourceMovement(actor), MovementTick));
            if (mode == SourceActorMode.Receiver)
            {
                TrySourceCatch(actor);
                SourceCarrierControl(actor);
                RunSourceEvasion(actor, 0, false);
                if (Model.CarrierId == actor.Id) MaybeSourcePlayerFire(actor, "_mode_wpn", 2489);
            }
            else if (mode == SourceActorMode.Quarterback) SourceCarrierControl(actor);
            bool eligible = SourceMovement.ModeAllowsRun(CaptureSourceMovement(actor), MovementContext(actor));
            if (mode == SourceActorMode.ReceiverPreSnap && Model.SourceRules.PlayType != SourcePlayType.Kickoff
                && (Model.SourceFormationSlots[actor.Id].Control & SourceFormationControl.Motion) == 0) eligible = false;
            actor.SourceCapturedControlBlock = eligible;
            if (eligible) RunSourceMovement(actor);
            if (mode == SourceActorMode.Receiver)
            {
                SourceEvasionResult after = RunSourceEvasion(actor, 1, eligible);
                actor.SourceRequestedScript = null;
                if (after.ReturnFromMode) { actor.SourceReturnedFromMovementMode = true; return; }
                if ((actor.SourceNewButtons & SourceDroneButtons.B) != 0 && Model.CarrierId != actor.Id
                    && (actor.AnimationState.Secondary.Mode & 0x1000u) == 0) ChangeSourceWeaponPlayer(actor);
            }
            else if (mode == SourceActorMode.Quarterback)
            {
                RunSourceEvasion(actor, 2, eligible);
                if (eligible && Model.SourceDisallowPassTicks > 0) Model.SourceDisallowPassTicks--;
            }
            else if (mode == SourceActorMode.DefensiveBack || mode == SourceActorMode.Line)
            {
                SourceRandomContext random = MovementRandomContext(actor);
                long draws = Model.SourceRandomDraws;
                SourceMovementResult jump = SourceEvasion.DefensiveBackJump(CaptureSourceMovement(actor), MovementContext(actor),
                    sourceMovementData.Movement, new MovementBoundary(this), sourceRandom, random, eligible);
                CompleteSourceMovement(actor, jump, random, draws);
                SourceDefensiveMovementButtons(actor);
            }
            SourceActorActions(actor, input);
            if (mode == SourceActorMode.Quarterback) MaybeSourcePlayerFire(actor, "_mode_qb", 2088);
            if (SourceFireEnabled && mode == SourceActorMode.ReceiverPreSnap && actor.SourceMode == SourceActorMode.ReceiverPreSnap)
                FireRuntime.WeaponPreSnapTail(FireSite("player.c", "_mode_wpnps", 2153, actor.Id), actor.Id,
                    Model.SourceLastReceiver[(int)actor.Team], Model.SourceConsecutiveCompletions[(int)actor.Team],
                    Model.SourceTeamFire[(int)actor.Team], (Model.SourceFlags & SourceGameFlags.OnsideKick) != 0,
                    Model.SourceRules.PlayType == SourcePlayType.Kickoff);
            if (SourceFireEnabled && mode == SourceActorMode.DefensiveBackPreSnap && actor.SourceMode == SourceActorMode.DefensiveBackPreSnap)
                FireRuntime.DefensivePreSnapTail(FireSite("player.c", "_mode_dbps", 2836, actor.Id),
                    actor.Id, Model.SourceTeamFire[(int)actor.Team]);
            if (mode == SourceActorMode.DefensiveBack || mode == SourceActorMode.Line)
                TrySourceCatch(actor);
        }

        private void AssignSourceDefenseMovement(ActorState carrier)
        {
            var modes = new SourceActorMode[7];
            int offset = (1 - (int)Model.Possession) * 7;
            for (int i = 0; i < modes.Length; i++) modes[i] = Model.Players[offset + i].SourceMode;
            SourceMovementDefenseRow row = sourceMovementData.Defense[Model.Players[offset].SourceHandicap];
            SourceRandomContext random = MovementRandomContext(carrier);
            IReadOnlyList<SourceMovementDefenseSpeed> assignments = SourceMovement.AssignDefenseSpeeds(
                MovementTick, (int)Model.Possession, modes, row.LineSpeeds, row.BackSpeeds,
                row.AccelerationBase, row.AccelerationRange, (Model.SourceDefensePlay.Flags & SourcePlayFlags.RedZone) != 0,
                sourceRandom, random);
            Model.SourceRandomCallIndex += 14; Model.SourceRandomDraws += 14;
            foreach (SourceMovementDefenseSpeed value in assignments)
            {
                ActorState defender = Model.Players[value.PlayerId];
                defender.SourceOverrideSpeed = value.Speed;
                defender.SourceOverrideTime = value.OverrideTime;
            }
            Model.SourceFlags |= SourceGameFlags.OverLineSpeedsSet;
        }

        private int ChangeSourceWeaponPlayer(ActorState actor)
        {
            if (actor.SourceControllerStation < 0 || Model.CarrierId == actor.Id
                || (Model.SourceFlags & SourceGameFlags.HikingBall) != 0
                || Model.CarrierId < 0 && (Model.BallKind == SourceBallKind.Pass || Model.BallKind == SourceBallKind.Bobble)
                    && Model.TargetPlayerId == actor.Id
                || Model.Phase == GamePhase.LiveBall && Model.CarrierId >= 0
                    && Model.Players[Model.CarrierId].SourceMode == SourceActorMode.Quarterback) return 0;
            SourceVector3 target = Model.CarrierId < 0 ? Model.SourceBallTarget
                : SourcePlayerDriver.WorldPosition(Model.Players[Model.CarrierId]);
            int selected = actor.Id;
            float best = 100000f;
            foreach (ActorState candidate in Model.Players)
            {
                if (candidate.Team != actor.Team || candidate.AnimationState.Primary.IsUninterruptible
                    || candidate.SourceControllerStation >= 0 && candidate.Id != actor.Id) continue;
                SourceVector3 point = SourcePlayerDriver.WorldPosition(candidate);
                float dx = target.X - point.X, dz = target.Z - point.Z;
                float distance = (float)Math.Sqrt(dx * dx + dz * dz);
                if (distance < best) { best = distance; selected = candidate.Id; }
            }
            if (selected == actor.Id) return 0;
            TransferSourceMovementStation(actor, Model.Players[selected]);
            Model.Players[selected].SourceWaveArm = false;
            return 1;
        }

        private void TransferSourceMovementStation(ActorState previous, ActorState next)
        {
            int station = previous.SourceControllerStation;
            previous.SourceControllerStation = -1; next.SourceControllerStation = station;
            Model.SourceStationPlayers[station] = next.Id;
            next.SourceButtons |= SourceDroneButtons.B;
            if (sourceConfiguration.Stations.Count > 0 && sourceConfiguration.Stations[0].StationId == station)
                Model.ControlledPlayerId = next.Id;
            EmitSource(SourceEventType.ControllerChanged, next.Id, previous.Id, station: station);
        }

        private void EmitSourceMovementSpeech(ActorState actor, SourceMovementCommandKind kind)
        {
            string function = kind == SourceMovementCommandKind.SpinSpeech ? "spin_move_speech"
                : kind == SourceMovementCommandKind.HurdleSpeech ? "hurdle_speech" : "stiff_arm_speech";
            int line = kind == SourceMovementCommandKind.SpinSpeech ? actor.SourceMode == SourceActorMode.Quarterback ? 1888 : 2518
                : kind == SourceMovementCommandKind.HurdleSpeech ? sourceMovementEvasionStage == 0 ? 2484 : 2546
                : (actor.SourceRawFlags & 0x100000) != 0 ? 4013
                : Model.SourceStationTurbo[(int)actor.Team] > 5f ? 4078 : 4083;
            var context = new SourcePresentationContext(Model.SourceTick, Model.SourcePresentationOrder++, actor.Id,
                "player.c", kind == SourceMovementCommandKind.StiffArmSpeech ? "standard_run"
                    : actor.SourceMode == SourceActorMode.Quarterback ? "_mode_qb" : "_mode_wpn",
                line, opcodeSymbol: "source-function-call");
            sourcePresentationCues.Add(new SourceCallbackCue(context, SourcePresentationCueKind.SoundCallback,
                function, 0, 0, 0, Model.CarrierId));
        }

        private void SourceDefensiveMovementButtons(ActorState actor)
        {
            if ((actor.SourceNewButtons & SourceDroneButtons.B) == 0
                || (actor.AnimationState.Secondary.Mode & 0x1000u) != 0) return;
            if ((actor.SourceButtons & SourceDroneButtons.C) == 0 && actor.SourceControllerStation >= 0)
            { ChangeSourceDefensePlayer(actor, true); return; }
            if ((actor.SourceButtons & SourceDroneButtons.C) == 0) return;
            SourceVector3 root = SourcePlayerDriver.WorldPosition(actor);
            bool pushHere = Model.CarrierId >= 0
                ? Model.Players[Model.CarrierId].SourceMode == SourceActorMode.Quarterback
                    && SourceMatchRules.FieldPosition(root.X, (int)Model.Possession) >= Model.SourceRules.LineOfScrimmage - 1
                : Model.BallKind == SourceBallKind.Pass
                    && SourceMatchRules.FieldPosition(Model.SourceBallTarget.X, (int)Model.Possession) <= Model.SourceRules.LineOfScrimmage
                    && SourceMatchRules.FieldPosition(root.X, (int)Model.Possession) >= Model.SourceRules.LineOfScrimmage - 1;
            int switchResult = pushHere ? -1 : ChangeSourceDefensePlayer(actor, false);
            if (switchResult == SourceDefenseSwitchDeferred) return;
            bool closest = switchResult == -1;
            if (!closest && !(Model.CarrierId < 0 && (Model.SourceFlags & SourceGameFlags.HikingBall) != 0))
            { ChangeSourceDefensePlayer(actor, true); return; }
            float speed = (float)Math.Sqrt(actor.SourceVelocity.X * actor.SourceVelocity.X + actor.SourceVelocity.Z * actor.SourceVelocity.Z);
            if (speed > 1.10f && (!pushHere || !actor.AnimationState.Primary.IsUninterruptible))
                actor.AnimationState.Secondary.AddMode(0x1000u);
            else if (!actor.AnimationState.Primary.IsUninterruptible)
            {
                int selected = SourceDraw(sourceMovementData.Movement.Count("push_anim_tbl"), "player.c:_mode_db:push");
                actor.AnimationState.FullBody(sourceMovementData.Movement.Script("push_anim_tbl", selected), actor,
                    Model.SourceTick, SourceDraw, AnimationSelectionEvidence.SourceVerified);
            }
        }

        private int ChangeSourceDefensePlayer(ActorState actor, bool change)
        {
            if (actor.SourceControllerStation < 0) return -1;
            bool seeking = Model.CarrierId >= 0 || Model.BallKind == SourceBallKind.Pass
                || Model.BallKind == SourceBallKind.FieldGoal || Model.BallKind == SourceBallKind.Kickoff;
            if (Model.Phase == GamePhase.DeadBall && (actor.SourceRawFlags & 0x2) != 0 && Model.CarrierId < 0) return -1;
            if (seeking && Model.CarrierId < 0)
            {
                if (Compatibility == SourceGameplayCompatibility.Portable && Model.CarrierId == -1)
                {
                    Model.RecordCompatibilityRepair(SourceCompatibilityRepairKind.CarrierlessDefensiveSwitch,
                        actor.Id, -1, -1, "player.c:db_change_player carrier -1; deferred switch; retained actor and stations; no angle read");
                    return SourceDefenseSwitchDeferred;
                }
                throw new NotSupportedException("player.c:db_change_player seeking-player branch indexes "
                    + "p2p_angles[game_info.ball_carrier==-1]; original undefined C is not replaced with a target.");
            }
            SourceVector3 target = Model.CarrierId >= 0 ? SourcePlayerDriver.WorldPosition(Model.Players[Model.CarrierId])
                : Model.SourceBallTarget;
            int selected = actor.Id;
            float best = 100000f;
            foreach (ActorState candidate in Model.Players)
            {
                if (candidate.Team != actor.Team || candidate.SourceControllerStation >= 0 && candidate.Id != actor.Id) continue;
                if (candidate.AnimationState.Primary.IsUninterruptible)
                {
                    // This bounded Core has no original block/puppet sequence programs.
                    // Those C1 programs require their actual SEQ_SPIN_A..SEQ_PUSH_BL3 ordering/breakout inputs.
                    continue;
                }
                SourceVector3 point = SourcePlayerDriver.WorldPosition(candidate);
                float dx = target.X - point.X, dz = target.Z - point.Z;
                float distance = (float)Math.Sqrt(dx * dx + dz * dz);
                if (seeking)
                {
                    int bearing = (1024 + (Model.Possession == TeamId.Away ? 768 : 256)
                        - Model.SourceAngles[Model.CarrierId, candidate.Id]) % 1024;
                    if (bearing > 512) bearing = 1024 - bearing;
                    bearing = Math.Max(bearing - 128, 0);
                    distance += distance * (bearing * 2f / 512f);
                }
                if (distance < best) { best = distance; selected = candidate.Id; }
            }
            if (selected == actor.Id) return -1;
            if (change) TransferSourceMovementStation(actor, Model.Players[selected]);
            return 0;
        }

        private void SourceGeneralRun(ActorState actor)
        {
            actor.SourceImmediateGeneralRuns++;
            actor.Down = false;
            actor.PendingThrowTarget = -1;
            RunSourceMovement(actor, true);
        }

        private int SourceMovementAnimationCode(ActorState actor, string function, int argument0, int argument1, int argument2)
        {
            int contact = SourceContactAnimationCode(actor, function, argument0);
            if (contact >= 0) return contact;
            if (function == "set_flag" || function == "clear_flag")
            {
                SourceMovementState flags = CaptureSourceMovement(actor);
                flags.Flags = function == "set_flag" ? flags.Flags | (SourceMovementFlags)argument0
                    : flags.Flags & ~(SourceMovementFlags)argument0;
                ApplySourceMovement(actor, flags);
                return 0;
            }
            if (function == "maybe_dive")
            {
                actor.SourceMaybeDiveCallbacks++;
                actor.SourceLastMaybeDiveTick = Model.SourceTick;
                SourceRandomContext random = MovementRandomContext(actor);
                long draws = Model.SourceRandomDraws;
                SourceMovementResult result = SourceEvasion.MaybeDive(CaptureSourceMovement(actor), MovementContext(actor),
                    new MovementBoundary(this), sourceRandom, random);
                CompleteSourceMovement(actor, result, random, draws);
                return 0;
            }
            if (function == "general_run") { SourceGeneralRun(actor); return 0; }
            if (function == "plyr_have_ball")
                return Model.Phase != GamePhase.LiveBall && (actor.SourceHelmetPopped
                    || Model.CarrierId == actor.Id && SourceDraw(100, "player.c:plyr_have_ball") < 90) ? 1 : 0;
            if (function == "set_unint_lowerhalf")
            { actor.AnimationState.Primary.AddMode(SourceAnimationModes.Uninterruptible); return 0; }
            if (function == "face_throw_target")
            {
                int target = actor.PendingThrowTarget;
                if (target < 0) return 1;
                int bearing = (1024 + actor.Facing - Model.SourceAngles[actor.Id, target]) % 1024;
                if (bearing > 512) bearing = 1024 - bearing;
                if (bearing > argument0) return 1;
                actor.TargetFacing = Model.SourceAngles[actor.Id, target];
                return 0;
            }
            if (function == "pick_random_mill")
            {
                int count = sourceMovementData.Movement.Count("breath_seq_tbl");
                int attempts = 0, selected;
                while (true)
                {
                    selected = SourceDraw(count, "player.c:unique_random");
                    bool available = (Model.SourceBreathControl & (1u << selected)) == 0;
                    if (++attempts > (count << 1))
                    {
                        selected = -1;
                        for (int i = 0; i < count; i++)
                            if ((Model.SourceBreathControl & (1u << i)) == 0) { selected = i; break; }
                        if (selected < 0) selected = SourceDraw(count, "player.c:unique_random:fallback");
                        break;
                    }
                    if (available) break;
                }
                Model.SourceBreathControl |= 1u << selected;
                actor.AnimationState.FullBody(sourceMovementData.Movement.Script("breath_seq_tbl", selected),
                    actor, Model.SourceTick, SourceDraw, AnimationSelectionEvidence.SourceVerified);
                return 0;
            }
            if (function == "rotate_torso")
            {
                if (argument0 != 0) { actor.UpperFacing = actor.TargetUpperFacing = actor.Facing; return 0; }
                int target = -1;
                float best = 1000f;
                SourceVector3 origin = SourcePlayerDriver.WorldPosition(actor);
                foreach (ActorState opponent in Model.Players)
                {
                    if (opponent.Team == actor.Team || (opponent.AnimationState.Primary.Mode & SourceAnimationModes.Reacting) != 0)
                        continue;
                    SourceVector3 point = SourcePlayerDriver.WorldPosition(opponent);
                    float dx = point.X - origin.X, dz = point.Z - origin.Z;
                    float distance = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (distance < best) { best = distance; target = opponent.Id; }
                }
                if (target < 0)
                    throw new NotSupportedException("player.c:rotate_torso original player_blocks[pnum==-1] branch; no invented opponent.");
                SourceVector3 destination = SourcePlayerDriver.WorldPosition(Model.Players[target]);
                if (SourceMatchRules.FieldPosition(origin.X, (int)Model.Possession) - 3
                    <= SourceMatchRules.FieldPosition(destination.X, (int)Model.Possession))
                {
                    int bearing = 1024 - (1024 + actor.Facing - Model.SourceAngles[actor.Id, target]) % 1024;
                    if (argument1 != 0 && SourceDraw(100, "player.c:rotate_torso:plow") < 35
                        && (Model.Possession == TeamId.Away ? -actor.SourceVelocity.X : actor.SourceVelocity.X) > 0f
                        && (bearing <= 64 || bearing >= 960)) return 1;
                    if (bearing >= 192 && bearing <= 512) bearing = 192;
                    if (bearing >= 513 && bearing <= 832) bearing = 832;
                    actor.UpperFacing = (int)(actor.UpperFacing + bearing + 13f);
                    actor.TargetUpperFacing = actor.UpperFacing;
                }
                return 0;
            }
            if (function == "slower_spin")
            {
                actor.SourceLastSpinPredicateCount = Model.SourceSpinCount[(int)actor.Team];
                return SourceEvasion.SlowerSpin(Model.SourceSpinCount[(int)actor.Team]) ? 1 : 0;
            }
            if (function == "my_choose_target" || function == "choose_target")
            {
                int selected = -1;
                float best = 10000f;
                foreach (ActorState candidate in Model.Players)
                {
                    if (candidate.Id == actor.Id || (function == "my_choose_target" || argument0 == 1) && candidate.Team == actor.Team)
                        continue;
                    int bearing = (1024 + actor.TargetFacing - Model.SourceAngles[actor.Id, candidate.Id]) % 1024;
                    if (bearing > 512) bearing = 1024 - bearing;
                    if (bearing > 256) continue;
                    float distance = Model.SourceDistances[actor.Id, candidate.Id] * (1f + bearing / 256f * 2f);
                    if (distance > 12f * SourceTiming.SourceUnitsPerYard) continue;
                    if ((function == "my_choose_target" || argument0 == 1) && Model.CarrierId == candidate.Id)
                    { selected = candidate.Id; best = 0f; }
                    if ((function == "my_choose_target" || argument0 == 1) && Model.TargetPlayerId == candidate.Id)
                    { selected = candidate.Id; best = 1f; }
                    if (distance < best) { best = distance; selected = candidate.Id; }
                }
                actor.PendingThrowTarget = selected;
                if (selected >= 0 && function == "my_choose_target") actor.TargetFacing = Model.SourceAngles[actor.Id, selected];
                return selected < 0 ? 1 : 0;
            }
            if (function == "maybe_spear_head")
            {
                if (actor.Team == Model.Possession) return 1;
                if (actor.PendingThrowTarget == -1 || actor.PendingThrowTarget == Model.CarrierId)
                    return SourceDraw(100, "player.c:maybe_spear_head") < 55 ? 0 : 1;
                return 1;
            }
            throw new NotSupportedException("Unbound original M1 animation callback " + function + ".");
        }

        private void SourceMovementLeapAttack(ActorState actor, SourceAnimationInstruction instruction)
        {
            int target = SourceEvasionLeap.ResolveVictim(instruction.A, Model.CarrierId,
                actor.PendingThrowTarget, (int)Model.Possession);
            if (Model.SourceCameraJointTick != Model.SourceTick - 1 || Model.SourceMovementCameraTick != Model.SourceTick)
                throw new InvalidOperationException("ANI_LEAPATT requires the actual preceding post-player camera-joint cache "
                    + "and this executing camera transform; no fresh WorldJoint substitute is used.");
            ActorState victim = Model.Players[target];
            SourceEvasionLeapResult result = SourceEvasionLeap.LeapAttack(CaptureSourceMovement(actor), MovementTick,
                sourcePlaybook.Directions, new SourceEvasionLeapInput(Model.SourceCameraJoints[target, instruction.B],
                    new SourceVector3(victim.SourceVelocity.X, victim.SourceVerticalVelocity, victim.SourceVelocity.Z),
                    Model.SourceMovementCamera.Transform, Model.SourceMovementCamera.Position,
                    new SourceVector3(instruction.X, instruction.Y, instruction.Z), instruction.C,
                    instruction.MaximumYVelocity, instruction.MaximumPlanarVelocity, instruction.MinimumPlanarVelocity,
                    target == actor.Id));
            if (!result.Executed || result.ScriptBytesConsumed != 36)
                throw new InvalidOperationException("Actual ANI_LEAPATT did not consume its nine 32-bit operands.");
            ApplySourceMovement(actor, result.State);
            actor.SourceLastLeapCacheTick = Model.SourceCameraJointTick;
            actor.SourceLastLeapBytesConsumed = result.ScriptBytesConsumed;
            actor.SourceLastLeapFlightTicks = result.FlightTicks;
        }
    }
}
