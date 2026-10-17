using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    internal static class SourcePlayerDriver
    {
        private const float UnitsPerYard = SourceTiming.SourceUnitsPerYard;
        private static readonly int[] Directions49 = {
            128, 96, 52, 0, 972, 928, 896,
            160, 128, 76, 0, 948, 896, 864,
            204, 180, 128, 0, 896, 844, 820,
            256, 256, 256, -1, 768, 768, 768,
            308, 332, 384, 512, 640, 692, 716,
            352, 384, 436, 512, 588, 640, 672,
            384, 416, 460, 512, 564, 608, 640
        };
        private static readonly string[] QbUpper = {
            "qb_run_anim", "qb_runl_anim", "qb_runb_anim",
            "qb_runb_anim", "qb_runb_anim", "qb_runr_anim"
        };
        private static readonly string[] QbLower = {
            "qb_run_anim", "qb_run_anim", "qb_runl45_anim",
            "qb_runb_anim", "qb_runr45_anim", "qb_run_anim"
        };

        internal static void LineUp(SimulationState model, ISourceAnimationLibrary library, Func<int, int> random,
            SourceFormationLayout layout, Action<int> beginActor, Action<ActorState> bindAnimation)
        {
            if (layout == null)
                throw new InvalidOperationException("An explicit original playbook formation layout is required.");
            model.SourceHumanTurbo = 100f;
            Array.Clear(model.SourceAnimationUniqueMasks, 0, model.SourceAnimationUniqueMasks.Length);
            model.SourceFormationSlots = new SourceFormationSlot[model.Players.Length];
            for (int id = 0; id < model.Players.Length; id++)
            {
                beginActor(id);
                ActorState actor = model.Players[id];
                SourceFormationActor placed = layout.Actors[id];
                model.SourceFormationSlots[id] = placed.Slot;
                actor.Position = new FieldVector(SourceMatchRules.AbsoluteYardsFromSourceWorld(placed.Position.X),
                    placed.Position.Z / UnitsPerYard);
                SetSourcePosition(actor, new SourceVector3(placed.Position.X, 0f, placed.Position.Z));
                actor.SnapPosition = actor.Position;
                actor.SourceMode = placed.Slot.Mode;
                actor.SourceTarget = placed.CoverageTarget;
                actor.SourceReverseRoute = false;
                actor.SourceJob = SourceActorJob.None;
                actor.SourceStick = 24;
                actor.SourcePreviousStick = 24;
                actor.SourceButtons = actor.SourceOldButtons = SourceDroneButtons.None;
                actor.SourceNewButtons = actor.SourceReleasedButtons = SourceDroneButtons.None;
                Array.Clear(actor.SourceButtonHistory, 0, actor.SourceButtonHistory.Length);
                actor.SourceDroneThrowTarget = -1;
                actor.SourceNonATicks = actor.SourceNonBTicks = 0;
                actor.SourceUnblockable = false;
                actor.SourceWaveArm = false;
                actor.SourceHandicap = model.SourceConfiguration.Difficulty;
                actor.SourceDirections = model.SourcePlaybook.Directions;
                actor.Velocity = default(FieldVector);
                actor.SourceVelocity = default(FieldVector);
                actor.SourceVerticalVelocity = 0f;
                actor.SourceFriction = 0f;
                actor.Height = 0f;
                actor.SourceRoot = default(SourceVector3);
                actor.SourceMove = default(FieldVector);
                actor.ContactCooldownTicks = 0;
                actor.BlockCooldownTicks = 0;
                actor.NoCatchTicks = 0;
                actor.SourcePendingCatch = null;
                actor.SourceRequestedScript = null;
                actor.SourcePendingLateral = false;
                actor.SourceLateHittable = false;
                actor.SourcePendingFireCheck = false;
                actor.SourceHelmetPopped = false;
                actor.SourcePlayOver = false;
                actor.PendingThrowTarget = -1;
                actor.PendingThrowTurbo = false;
                actor.Down = false;
                actor.Turbo = false;
                actor.NonTurboTicks = 0;
                actor.Flipped = false;
                actor.SourceInPlay = false;
                actor.SourceBallKicked = false;
                actor.ModelScale = model.SourceRosters.GetTeam(actor.SourceRoster.TeamIndex)
                    .Players[actor.SourceRoster.RosterIndex].ModelScale;
                actor.Role = placed.Slot.Mode == SourceActorMode.Kicker ? ActorRole.Kicker
                    : placed.Slot.Mode == SourceActorMode.QuarterbackPreSnap ? ActorRole.Quarterback
                    : placed.Slot.Mode == SourceActorMode.LinePreSnap ? ActorRole.Lineman
                    : placed.Slot.Mode == SourceActorMode.DefensiveBackPreSnap ? ActorRole.Defender : ActorRole.Receiver;
                actor.Facing = placed.Facing;
                actor.UpperFacing = actor.TargetFacing = actor.TargetUpperFacing = actor.Facing;
                actor.AnimationState = new SourceAnimationState(library, id);
                actor.AnimationState.SetScriptSelector((name, value, draw) => SelectInitialAnimation(model, name, value, draw));
                actor.AnimationState.SetStandardRandom(model.SourceCStandardRandom);
                bindAnimation(actor);
                actor.Action = ActorAction.PreSnap;
                actor.AnimationState.FullBody(placed.Slot.Script, actor, model.SourceTick, random,
                    AnimationSelectionEvidence.SourceVerified);
                // Placement follows initial change_anim, as in the lineup path;
                // the established animation reference point is retained.
                SetSourcePosition(actor, new SourceVector3(placed.Position.X, 0f, placed.Position.Z));
            }
            model.CarrierId = model.SourceRules.PlayType == SourcePlayType.Kickoff ? -1 : model.OffenseOffset + 1;
            model.LastTouchPlayerId = model.CarrierId;
            model.BallMode = model.CarrierId < 0 ? BallMode.Dead : BallMode.Held;
            if (model.CarrierId < 0) model.BallKind = SourceBallKind.Kickoff;
            model.BallPosition = model.CarrierId < 0 ? new FieldVector(model.LineOfScrimmage, 0f)
                : model.Players[model.CarrierId].Position;
            model.BallHeight = 0f;
            model.BallVelocity = default(FieldVector);
            model.BallVerticalVelocity = 0f;
            model.ControlledPlayerId = -1;
        }

        internal static void Snap(SimulationState model, Func<int, int> random)
        {
            for (int id = 0; id < model.Players.Length; id++)
            {
                ActorState actor = model.Players[id];
                actor.SnapPosition = actor.Position;
                if (actor.Team == model.Possession && id % SliceRules.PlayersPerTeam >= 4)
                {
                    float z = WorldPosition(actor).Z;
                    float line = model.SourceLinePosition * UnitsPerYard;
                    actor.SourceReverseRoute = model.Possession == TeamId.Home ? z < line - 2f : z > line + 2f;
                }
                actor.ModeChangedTick = model.SourceTick;
                if (actor.SourceMode == SourceActorMode.LinePreSnap) actor.SourceMode = SourceActorMode.Line;
                else if (actor.SourceMode == SourceActorMode.QuarterbackPreSnap) actor.SourceMode = SourceActorMode.Quarterback;
                else if (actor.SourceMode == SourceActorMode.ReceiverPreSnap) actor.SourceMode = SourceActorMode.Receiver;
                else if (actor.SourceMode == SourceActorMode.DefensiveBackPreSnap) actor.SourceMode = SourceActorMode.DefensiveBack;
                int slot = id % SliceRules.PlayersPerTeam;
                if (slot < 3)
                {
                    string script = actor.Team != model.Possession ? "charge_anim"
                        : slot == 1 ? "center_snap_anim" : "stance3pt_jumpback_anim";
                    actor.Action = ActorAction.Block;
                    actor.AnimationState.FullBody(script, actor, model.SourceTick, random,
                        AnimationSelectionEvidence.SourceVerified);
                }
                else
                {
                    actor.Action = ActorAction.Idle;
                    actor.AnimationState.FullBody(actor.Role == ActorRole.Quarterback ? "qb_stance_anim"
                        : actor.Id == model.ControlledPlayerId ? "safty2pt_anim_rnd" : "breath2_anim",
                        actor, model.SourceTick, random, AnimationSelectionEvidence.SourceVerified);
                }
            }
        }

        internal static void Update(SimulationState model, PlayerCommand command, Func<int, int> random,
            Action<int, SourceAnimationSignal> animationEffect, Action<ActorState> checkCatch,
            Action<int> beginActor, Action<ActorState> carrierCheck,
            Func<ActorState, PlayerCommand, FieldVector> droneMove,
            Action<ActorState, PlayerCommand> actorActions, Action<ActorState> droneAnimation)
        {
            // plyrinfo.c:1713-1735: station resource, not a per-actor timer.
            if (model.Phase == GamePhase.LiveBall)
            {
                foreach (SourceStationConfiguration station in model.SourceConfiguration.Stations)
                {
                    int controlled = model.SourceStationPlayers[station.StationId];
                    if (controlled < 0) continue;
                    PlayerCommand input = ActorCommand(model, model.Players[controlled], command);
                    float turbo = model.SourceStationTurbo[station.StationId];
                    model.SourceStationTurbo[station.StationId] = input.Turbo ? Math.Max(0f, turbo - 0.35f)
                        : Math.Min(100f, turbo + 0.35f + (station.Team != model.Possession ? 0.15f : 0f));
                }
                if (model.SourceConfiguration.Stations.Count > 0)
                    model.SourceHumanTurbo = model.SourceStationTurbo[model.SourceConfiguration.Stations[0].StationId];
            }
            for (int id = 0; id < model.Players.Length; id++)
            {
                ActorState actor = model.Players[id];
                beginActor(id);
                FieldVector before = actor.Position;
                bool live = model.Phase == GamePhase.LiveBall;
                if (actor.NoCatchTicks > 0)
                    actor.NoCatchTicks--;
                actor.SourceInPlay = live;
                actor.SourcePlayOver = model.Phase == GamePhase.DeadBall || model.Phase == GamePhase.GameOver;
                actor.SourceBallKicked = (model.SourceFlags & SourceGameFlags.BallKicked) != 0;
                actor.SourceHasBall = actor.SourceIsCarrier = model.CarrierId == id;
                actor.SourceSpinCount = model.SourceSpinCount[(int)actor.Team];
                actor.SourceBallTarget = model.PassTarget;
                actor.SourceBallLocation = model.CarrierId >= 0 ? model.BallPosition : new FieldVector(
                    SourceMatchRules.AbsoluteYardsFromSourceWorld(model.SourceBallOrigin.X),
                    model.SourceBallOrigin.Z / UnitsPerYard);
                actor.SourceBallTimeRemaining = model.PassTicksTotal - model.PassTicksElapsed;
                actor.SourcePreviousStick = actor.SourceStick;
                actor.SourceForcedMoveHeading = -1;
                actor.SourceRawFlags = actor.Turbo ? actor.SourceRawFlags | 0x10 : actor.SourceRawFlags & ~0x10;
                actor.SourceOldButtons = actor.SourceButtons;
                for (int history = actor.SourceButtonHistory.Length - 1; history > 0; history--)
                    actor.SourceButtonHistory[history] = actor.SourceButtonHistory[history - 1];
                actor.SourceButtonHistory[0] = actor.SourceButtons;
                actor.SourceReleasedButtons = SourceDroneButtons.None;
                bool kickoffInFlight = model.SourceRules.PlayType == SourcePlayType.Kickoff && model.CarrierId < 0
                    && (model.SourceFlags & SourceGameFlags.OnsideKick) == 0;
                actor.Turbo01 = actor.SourceControllerStation < 0 ? 1f
                    : model.SourceStationTurbo[actor.SourceControllerStation] / 100f;
                bool intendedReceiver = model.CarrierId < 0 && model.TargetPlayerId == id
                    && (model.BallKind == SourceBallKind.Pass || model.BallKind == SourceBallKind.Lateral);
                bool human = actor.SourceControllerStation >= 0 && !kickoffInFlight && !intendedReceiver;
                PlayerCommand actorCommand = ActorCommand(model, actor, command);
                if (actor.SourceControllerStation >= 0)
                {
                    actor.SourceButtons = actorCommand.Turbo ? SourceDroneButtons.C : SourceDroneButtons.None;
                    bool raw = false;
                    if (actorCommand.StationInputs != null)
                        foreach (SourceStationInput input in actorCommand.StationInputs.Stations)
                            if (input.StationId == actor.SourceControllerStation)
                            { actor.SourceButtons = (SourceDroneButtons)(int)input.Held; raw = true; break; }
                    if (!raw)
                    {
                        SourceAction action = actorCommand.Source.IsSpecified ? actorCommand.Source.Action
                            : actorCommand.Action == InputAction.Pass || actorCommand.Action == InputAction.Snap
                                || actorCommand.Action == InputAction.SwitchDefender ? SourceAction.Pass
                            : actorCommand.Action == InputAction.Tackle ? SourceAction.Tackle : SourceAction.None;
                        if (action == SourceAction.Pass || action == SourceAction.Snap || action == SourceAction.Lateral
                            || action == SourceAction.SwitchPlayer) actor.SourceButtons |= SourceDroneButtons.B;
                        if (action == SourceAction.Catch || action == SourceAction.Jump || action == SourceAction.Hurdle
                            || action == SourceAction.Dive || action == SourceAction.Tackle) actor.SourceButtons |= SourceDroneButtons.A;
                    }
                    actor.SourceNewButtons = actor.SourceButtons & ~actor.SourceOldButtons;
                    if (raw)
                        foreach (SourceStationInput input in actorCommand.StationInputs.Stations)
                            if (input.StationId == actor.SourceControllerStation)
                            {
                                actor.SourceNewButtons |= (SourceDroneButtons)(int)input.Pressed;
                                actor.SourceReleasedButtons = (SourceDroneButtons)(int)input.Released;
                                break;
                            }
                    actor.SourceReleasedButtons |= actor.SourceOldButtons & ~actor.SourceButtons;
                }
                bool approaching = model.SourceKickApproach && actor.Role == ActorRole.Kicker;
                if (human) actor.SourceStick = ActorStick(actor, actorCommand);
                FieldVector move = live || model.Phase == GamePhase.PreSnap ? human ? MovementForStick(
                    actor.SourceStick, actor.SourceDirections, model.SourceCameraHeading)
                    : droneMove(actor, actorCommand) : default(FieldVector);
                if (model.Phase == GamePhase.PreSnap && actor.SourceMode == SourceActorMode.ReceiverPreSnap
                    && model.SourceRules.PlayType != SourcePlayType.Kickoff
                    && (model.SourceOffensePlay.Flags & SourcePlayFlags.FakeFieldGoal) == 0 && actor.SourceStick != 24)
                {
                    int direction = (actor.SourceDirections.StickHeading(actor.SourceStick) + model.SourceCameraHeading) & 1023;
                    if (direction <= 128 || direction >= 896) direction = 0;
                    else if (direction < 384) actor.SourceStick = 24;
                    else if (direction <= 640) direction = 512;
                    else actor.SourceStick = 24;
                    float z = WorldPosition(actor).Z;
                    float line = model.SourceLinePosition * UnitsPerYard;
                    if (z < line - 60f && direction == 512 || z > line + 60f && direction == 0)
                        actor.SourceStick = 24;
                    if (actor.SourceStick == 24) move = default(FieldVector);
                    else
                    {
                        actor.SourceForcedMoveHeading = direction;
                        move = new FieldVector(actor.SourceDirections.Sin(direction), actor.SourceDirections.Cos(direction));
                    }
                }
                bool waitingHike = actor.SourceMode == SourceActorMode.Quarterback
                    && (model.SourceFlags & SourceGameFlags.HikingBall) != 0;
                if (waitingHike) move = default(FieldVector);
                actor.SourceNonATicks = ButtonTime(actor.SourceNonATicks,
                    (actor.SourceButtons & SourceDroneButtons.A) != 0);
                actor.SourceNonBTicks = ButtonTime(actor.SourceNonBTicks,
                    (actor.SourceButtons & SourceDroneButtons.B) != 0);
                bool turbo = live && (human ? actorCommand.Turbo : actor.Turbo);
                if (turbo && (actor.Turbo01 > 0f || !human))
                {
                    actor.Turbo = true;
                    actor.NonTurboTicks = actor.NonTurboTicks > 0 && actor.NonTurboTicks < 9 ? -1 : 0;
                }
                else
                {
                    actor.Turbo = false;
                    actor.NonTurboTicks++;
                }
                actor.SourceMove = move;
                if (live && actor.SourceMode == SourceActorMode.Quarterback
                    && (model.SourceFlags & SourceGameFlags.BallCrossedLine) != 0)
                {
                    if (!actor.AnimationState.Primary.IsUninterruptible)
                        actor.AnimationState.FullBody(model.CarrierId == id ? "run_wb_anim" : "run_anim", actor,
                            model.SourceTick, random, AnimationSelectionEvidence.SourceVerified);
                    actor.SourceMode = SourceActorMode.Receiver;
                    actor.Role = ActorRole.Receiver;
                    actor.ModeChangedTick = model.SourceTick;
                }
                else if (live && actor.SourceMode == SourceActorMode.Line && model.CarrierId >= 0
                    && model.Players[model.CarrierId].SourceMode == SourceActorMode.Receiver)
                {
                    actor.SourceMode = actor.Team == model.Possession ? SourceActorMode.Receiver : SourceActorMode.DefensiveBack;
                    actor.Role = actor.Team == model.Possession ? ActorRole.Receiver : ActorRole.Defender;
                    actor.ModeChangedTick = model.SourceTick;
                }
                Turn(actor, 64, 48);
                if (live)
                {
                    checkCatch(actor);
                    carrierCheck(actor);
                    live = model.Phase == GamePhase.LiveBall;
                }
                bool motion = model.Phase == GamePhase.PreSnap && actor.SourceMode == SourceActorMode.ReceiverPreSnap
                    && (model.SourceDrones[id].Flags & SourceDroneFlags.Motion) != 0;
                if ((live || approaching || motion) && !waitingHike && !actor.AnimationState.Busy && actor.PendingThrowTarget < 0
                    && actor.ModeChangedTick != model.SourceTick)
                    Movement(model, actor, move, random);
                else if (!live && !approaching && !actor.AnimationState.Busy && actor.Action != ActorAction.PreSnap)
                {
                    actor.SourceVelocity = default(FieldVector);
                    SelectIdle(model, actor, random);
                }
                actorActions(actor, actorCommand);
                if (live) droneAnimation(actor);

                SourceAnimationSignal signals = actor.AnimationState.Advance(actor, model.SourceTick, random, signal =>
                {
                    animationEffect(id, signal);
                    actor.SourceIsCarrier = actor.SourceHasBall = model.CarrierId == id;
                    actor.SourcePlayOver = model.Phase == GamePhase.DeadBall || model.Phase == GamePhase.GameOver;
                    actor.SourceInPlay = model.Phase == GamePhase.LiveBall;
                });
                if ((signals & SourceAnimationSignal.GeneralRun) != 0)
                {
                    actor.Down = false;
                    actor.PendingThrowTarget = -1;
                    if (model.Phase == GamePhase.LiveBall)
                        Movement(model, actor, move, random, true);
                    else
                        SelectIdle(model, actor, random, true);
                }
                Physics(actor);
                actor.Velocity = PlayerSimulation.Scale(PlayerSimulation.Subtract(actor.Position, before),
                    SourceTiming.DefaultTicksPerSecond);
                if (!SourcePoseMath.IsFinite(actor.Position.X) || !SourcePoseMath.IsFinite(actor.Position.Z)
                    || !SourcePoseMath.IsFinite(actor.Height))
                    throw new InvalidOperationException("Non-finite source player state: actor=" + id
                        + " sourceTick=" + model.SourceTick + " script=" + actor.AnimationState.Primary.ScriptSymbol);
            }
        }

        internal static void Contact(SimulationState model, int attackerId, int victimId, Func<int, int> random)
        {
            ActorState attacker = model.Players[attackerId];
            ActorState victim = model.Players[victimId];
            attacker.Facing = Heading(PlayerSimulation.Subtract(victim.Position, attacker.Position));
            attacker.TargetFacing = attacker.Facing;
            attacker.Action = ActorAction.Contact;
            attacker.AnimationState.FullBody(attacker.Turbo ? "t_dshoul_anim" : "t_stshld_anim",
                attacker, model.SourceTick, random, AnimationSelectionEvidence.Approximate);
            victim.Action = ActorAction.Fall;
            victim.PendingThrowTarget = -1;
            // FALL_04 is an explicit provisional contact selection. Unlike the
            // H_FRONT1 ball-holder branch it does not skip a required wobble clip.
            victim.AnimationState.FullBody("fall_04", victim, model.SourceTick, random,
                AnimationSelectionEvidence.Approximate);
        }

        private static void Movement(SimulationState model, ActorState actor, FieldVector move,
            Func<int, int> random, bool force = false)
        {
            if (PlayerSimulation.DistanceSquared(move, default(FieldVector)) < 0.00001f)
            {
                actor.SourceVelocity = default(FieldVector);
                if (force || actor.SourcePreviousStick != 24 || actor.AnimationState.Primary.IsUninterruptible)
                    SelectIdle(model, actor, random, force);
                return;
            }
            int heading = actor.SourceDirections == null ? Heading(move)
                : (actor.SourceDirections.StickHeading(actor.SourceStick) + model.SourceCameraHeading) & 1023;
            if (actor.SourceForcedMoveHeading >= 0) heading = actor.SourceForcedMoveHeading;
            if (heading < 0) heading = 0;
            actor.TargetFacing = actor.TargetUpperFacing = heading;
            bool quarterback = actor.SourceMode == SourceActorMode.Quarterback && actor.Id == model.CarrierId
                && (model.SourceFlags & SourceGameFlags.BallCrossedLine) == 0;
            float speed;
            actor.Action = actor.Turbo ? ActorAction.TurboRun : ActorAction.Run;
            if (quarterback)
            {
                int relative = (heading - (actor.Team == TeamId.Home ? 256 : 768)) & 1023;
                int index = relative <= 128 || relative >= 896 ? 0 : relative <= 332 ? 1
                    : relative < 460 ? 2 : relative <= 564 ? 3 : relative < 692 ? 4 : 5;
                actor.Flipped = index == 1 || index == 2 ? true : index == 4 || index == 5 ? false : actor.Flipped;
                bool backward = index >= 2 && index <= 4;
                speed = actor.Turbo ? backward ? 1.45f : 1.60f : 1.30f;
                if (model.OffenseDirection * (actor.Position.X - model.SnapLineOfScrimmage) < -18f)
                    speed /= 1.50f;
                if (backward)
                    actor.TargetFacing = actor.TargetUpperFacing = actor.Team == TeamId.Home ? 256 : 768;
                string lower = TurboScript(QbLower[index], actor.Turbo);
                string upper = TurboScript(QbUpper[index], actor.Turbo);
                actor.AnimationState.Split(lower, upper, actor, model.SourceTick, random);
                actor.AnimationState.Evidence = AnimationSelectionEvidence.SourceVerified;
            }
            else
            {
                bool carrier = actor.Id == model.CarrierId && model.BallMode == BallMode.Held;
                bool defender = actor.Team != model.Possession;
                bool human = actor.Id == model.ControlledPlayerId;
                if (carrier) speed = actor.Turbo ? 1.65f : 1.35f;
                else if (defender && human) speed = actor.Turbo ? 1.85f : 1.45f;
                else if (actor.Role == ActorRole.Kicker) speed = 1.35f;
                else if (actor.Role == ActorRole.Lineman) speed = actor.Turbo ? 1.50f : 1.25f;
                else if (defender) speed = actor.Turbo ? 1.85f : 1.15f;
                else speed = actor.Turbo ? 1.45f : 1.15f;
                bool useTurboAnimation = actor.Turbo && speed >= 1.45f;
                string script = carrier ? useTurboAnimation ? "run_t_wb_anim" : "run_wb_anim"
                    : defender && actor.Role == ActorRole.Lineman ? useTurboAnimation ? "r_t_huff_anim" : "r_huff_anim"
                    : useTurboAnimation ? "run_t_anim" : "run_anim";
                string sequence = carrier ? useTurboAnimation ? "R_TRB_WB" : "RUN_WB"
                    : defender && actor.Role == ActorRole.Lineman ? useTurboAnimation ? "HUFFRUN" : "HUFFRUN2"
                    : useTurboAnimation ? "R_TRB" : "RUN";
                if (force || actor.AnimationState.Primary.SequenceName != sequence || actor.AnimationState.TwoPart)
                {
                    if ((actor.AnimationState.Primary.Mode & SourceAnimationModes.PrepareTransition) != 0
                        && script.StartsWith("run_", StringComparison.Ordinal))
                        script += "_int";
                    if (!force && actor.AnimationState.TwoPart
                        && (actor.AnimationState.Secondary.Mode & SourceAnimationModes.TwoPart) != 0)
                    {
                        if (actor.AnimationState.Primary.SequenceName != sequence)
                            actor.AnimationState.LowerBody(script, actor, model.SourceTick, random);
                    }
                    else
                        actor.AnimationState.FullBody(script, actor, model.SourceTick, random,
                            carrier && model.OffenseDirection * (actor.Position.X - 50f) > 30f
                                ? AnimationSelectionEvidence.Approximate : AnimationSelectionEvidence.SourceVerified);
                }
            }
            actor.SourceVelocity = SourceAnimationLayerState.Rotate(0f, speed, heading, actor.SourceDirections);
        }

        private static string TurboScript(string normal, bool turbo)
        {
            return turbo ? normal.Replace("qb_", "qb_t_") : normal;
        }

        private static void SelectIdle(SimulationState model, ActorState actor, Func<int, int> random, bool force = false)
        {
            bool quarterback = actor.SourceMode == SourceActorMode.Quarterback && actor.Id == model.CarrierId
                && (model.SourceFlags & SourceGameFlags.BallCrossedLine) == 0;
            string script = quarterback ? actor.Flipped ? "qb_flip_stance_anim" : "qb_stance_anim"
                : actor.Id == model.CarrierId && model.BallMode == BallMode.Held ? "stance_wb_anim_int"
                : actor.Id == model.ControlledPlayerId && model.Phase != GamePhase.DeadBall ? "safty2pt_anim_rnd"
                : model.Phase == GamePhase.DeadBall ? "breath_anim" : "breath2_anim";
            if (force || actor.AnimationState.Primary.ScriptSymbol != script || actor.AnimationState.TwoPart)
            {
                actor.Action = ActorAction.Idle;
                actor.AnimationState.FullBody(script, actor, model.SourceTick, random,
                    model.Phase == GamePhase.DeadBall ? AnimationSelectionEvidence.Approximate
                        : AnimationSelectionEvidence.SourceVerified);
            }
        }

        internal static PlayerCommand ActorCommand(SimulationState model, ActorState actor, PlayerCommand command)
        {
            if (actor.SourceControllerStation < 0)
                return default(PlayerCommand);
            if (command.StationInputs != null)
            {
                foreach (SourceStationInput station in command.StationInputs.Stations)
                {
                    if (station.StationId != actor.SourceControllerStation) continue;
                    bool primary = actor.Id == model.ControlledPlayerId;
                    SourceCommand source = station.Command.IsSpecified ? station.Command
                        : primary ? command.Source : default(SourceCommand);
                    return new PlayerCommand(new FieldVector(station.StickX / 3f, station.StickZ / 3f),
                        (station.Held & SourceButtons.C) != 0,
                        primary ? command.Action : InputAction.None, command.Play,
                        primary ? command.TargetPlayerId : -1, source,
                        stationInputs: command.StationInputs);
                }
                return default(PlayerCommand);
            }
            return actor.Id == model.ControlledPlayerId ? command : default(PlayerCommand);
        }

        private static int ButtonTime(int previous, bool held)
        {
            return held ? previous > 0 && previous < 9 ? -1 : 0 : previous + 1;
        }

        internal static FieldVector Quantize(FieldVector move)
        {
            return Quantize(move, null);
        }

        internal static FieldVector Quantize(FieldVector move, SourceDirectionMath directions)
        {
            int stick = StickIndex(move);
            return MovementForStick(stick, directions);
        }

        internal static int ActorStick(ActorState actor, PlayerCommand command)
        {
            if (command.StationInputs != null)
                foreach (SourceStationInput input in command.StationInputs.Stations)
                    if (input.StationId == actor.SourceControllerStation)
                        return (3 - input.StickZ) * 7 + 3 - input.StickX;
            return StickIndex(command.Move);
        }

        private static FieldVector MovementForStick(int stick, SourceDirectionMath directions, int cameraHeading = 0)
        {
            if (stick == 24) return default(FieldVector);
            int angle = (directions == null ? Directions49[stick] : directions.StickHeading(stick)) + cameraHeading;
            return directions == null ? SourcePoseMath.Facing(angle)
                : new FieldVector(directions.Sin(angle), directions.Cos(angle));
        }

        internal static int StickIndex(FieldVector move)
        {
            FieldVector finite = PlayerSimulation.NormalizeInput(move);
            int x = (int)Math.Round(finite.X * 3f, MidpointRounding.AwayFromZero);
            int z = (int)Math.Round(finite.Z * 3f, MidpointRounding.AwayFromZero);
            if (x == 0 && z == 0)
                return 24;
            return (3 - z) * 7 + (3 - x);
        }

        internal static int Heading(FieldVector direction)
        {
            if (PlayerSimulation.DistanceSquared(direction, default(FieldVector)) < 0.00001f)
                return 0;
            return (int)Math.Round(Math.Atan2(direction.X, direction.Z) * 512d / Math.PI) & 1023;
        }

        private static void Turn(ActorState actor, int lowerRate, int upperRate)
        {
            actor.Facing = TurnToward(actor.Facing, actor.TargetFacing, lowerRate);
            actor.UpperFacing = TurnToward(actor.UpperFacing, actor.TargetUpperFacing, upperRate);
        }

        private static int TurnToward(int current, int target, int rate)
        {
            int difference = ((target - current + 512) & 1023) - 512;
            return (current + Math.Max(-rate, Math.Min(rate, difference))) & 1023;
        }

        private static string SelectInitialAnimation(SimulationState model, string symbol,
            ActorState actor, Func<int, int> random)
        {
            string[] linemen = { "lm_set1_anim", "lm_set2_anim", "lm_set3_anim", "lm_set4_anim" };
            string[] man = { "shift_02", "shift_10", "dline_01c", "dline_02c", "dline_03a", "dline_04b" };
            string[] zone = { "shift_01", "shift_02", "shift_04", "shift_03", "shift_08", "shift_09" };
            string[] defense = { "stance2pt2_anim_rnd", "dline_01a", "dline_01b", "dline_01c",
                "dline_02a", "dline_02b", "dline_02c", "safty2pt_anim_rnd", "dline_03a", "dline_03b",
                "dline_03c", "dline_04a", "dline_04b", "dline_04c" };
            string[] blood = { "m_dblood_anim", "m_dblood2_anim", "m_dblood3_anim",
                "dline_03b", "dline_01a", "dline_04c", "dline_02a" };
            switch (symbol)
            {
                case "lm_random_anim": return UniqueAnimation(model, linemen, 0, random);
                case "wr_random_anim":
                    return UniqueAnimation(model, new[] { "wr_sprint1_anim", "stance2pt_anim_rnd", "wr_sprint2_anim",
                        "wr_set_anim", "wr_sprint3_anim", "wr_set2_anim", "wr_sprint4_anim" }, 1, random);
                case "qb_random_anim":
                    return new[] { "qbset_anim", "qb_claps_set_anim", "qb_rubs_knee_set_anim" }[random(3)];
                case "qb_random_sg_anim":
                    return new[] { "shotgun_anim", "qb_pumps_crowd_set_anim", "qb_claps_set_anim", "qb_rubs_knee_set_anim" }[random(4)];
                case "db_man2man_random_anim":
                    random(100);
                    if (model.SourceTeamFire[1 - (int)model.Possession]) return blood[random(blood.Length)];
                    return random(100) < 40 ? UniqueAnimation(model, man, 2, random)
                        : UniqueAnimation(model, defense, 4, random);
                case "db_zone_random_anim":
                    random(100);
                    if (model.SourceTeamFire[1 - (int)model.Possession]) return blood[random(blood.Length)];
                    return random(100) < 80 ? UniqueAnimation(model, zone, 3, random)
                        : UniqueAnimation(model, defense, 4, random);
                case "db_random_anim":
                    random(100);
                    if (model.SourceTeamFire[1 - (int)model.Possession]) return blood[random(blood.Length)];
                    return actor.SourceControllerStation >= 0 ? "safty2pt_anim_rnd"
                        : UniqueAnimation(model, defense, 4, random);
                default:
                    throw new NotSupportedException("Unported source random animation selector " + symbol);
            }
        }

        private static string UniqueAnimation(SimulationState model, string[] table, int control, Func<int, int> random)
        {
            int value, attempts = 0;
            uint mask = model.SourceAnimationUniqueMasks[control];
            do
            {
                value = random(table.Length);
                attempts++;
                if (attempts > (table.Length << 1))
                {
                    value = -1;
                    for (int i = 0; i < table.Length; i++)
                        if ((mask & (1u << i)) == 0) { value = i; break; }
                    if (value == -1) value = random(table.Length);
                    break;
                }
            } while ((mask & (1u << value)) != 0);
            model.SourceAnimationUniqueMasks[control] |= 1u << value;
            return table[value];
        }

        private static void Physics(ActorState actor)
        {
            EnsureSourcePosition(actor);
            actor.SourcePosition = new FieldVector(actor.SourcePosition.X + actor.SourceVelocity.X,
                actor.SourcePosition.Z + actor.SourceVelocity.Z);
            actor.SourceHeight += actor.SourceVerticalVelocity - 0.04f;
            actor.SourceVerticalVelocity -= 0.08f;
            if (actor.SourceHeight <= 0f)
            {
                actor.SourceHeight = 0f;
                actor.SourceVerticalVelocity = 0f;
            }
            if (actor.SourceFriction > 0f)
            {
                float speed = (float)Math.Sqrt(PlayerSimulation.DistanceSquared(actor.SourceVelocity, default(FieldVector)));
                if (speed < 0.0001f)
                {
                    actor.SourceVelocity = default(FieldVector);
                    actor.SourceFriction = 0f;
                }
                else if (actor.SourceFriction > speed)
                {
                    actor.SourceVelocity = default(FieldVector);
                    actor.SourceFriction = 0f;
                }
                else
                {
                    float x = actor.SourceFriction * actor.SourceVelocity.X / speed;
                    float z = actor.SourceFriction * actor.SourceVelocity.Z / speed;
                    actor.SourceVelocity = new FieldVector(actor.SourceVelocity.X - x, actor.SourceVelocity.Z - z);
                }
            }
            PublishSourcePosition(actor);
        }

        internal static SourceVector3 WorldPosition(ActorState actor)
        {
            return actor.SourceCoordinates ? new SourceVector3(actor.SourcePosition.X, actor.SourceHeight, actor.SourcePosition.Z)
                : SourceBallDriver.SourcePosition(actor.Position, actor.Height);
        }

        internal static void SetSourcePosition(ActorState actor, SourceVector3 source)
        {
            actor.SourceCoordinates = true;
            actor.SourcePosition = new FieldVector(source.X, source.Z);
            actor.SourceHeight = source.Y;
            PublishSourcePosition(actor);
        }

        internal static void MoveSource(ActorState actor, FieldVector delta)
        {
            EnsureSourcePosition(actor);
            actor.SourcePosition = new FieldVector(actor.SourcePosition.X + delta.X, actor.SourcePosition.Z + delta.Z);
            PublishSourcePosition(actor);
        }

        internal static void MoveSourceY(ActorState actor, float delta)
        {
            EnsureSourcePosition(actor);
            actor.SourceHeight = Math.Max(0f, actor.SourceHeight + delta);
            PublishSourcePosition(actor);
        }

        private static void EnsureSourcePosition(ActorState actor)
        {
            if (!actor.SourceCoordinates)
                SetSourcePosition(actor, SourceBallDriver.SourcePosition(actor.Position, actor.Height));
        }

        private static void PublishSourcePosition(ActorState actor)
        {
            actor.Position = new FieldVector(SourceMatchRules.AbsoluteYardsFromSourceWorld(actor.SourcePosition.X),
                actor.SourcePosition.Z / UnitsPerYard);
            actor.Height = actor.SourceHeight / UnitsPerYard;
        }
    }
}
