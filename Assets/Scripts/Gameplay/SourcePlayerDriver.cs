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
        // playbook.c formation_sidei; the four legacy menu values are only
        // adapters to named source plays, not a claim of a complete playbook.
        private static readonly FieldVector[] SideI = {
            new FieldVector(-12f, -17.5f), new FieldVector(-12f, 0f),
            new FieldVector(-12f, 17.5f), new FieldVector(-35f, 0f),
            new FieldVector(-45f, 33f), new FieldVector(-15f, 55f),
            new FieldVector(-30.5f, 55f)
        };
        private static readonly FieldVector[] Cover = {
            new FieldVector(11f, -17.5f), new FieldVector(11f, 0f),
            new FieldVector(11f, 17.5f), new FieldVector(25f, 33f),
            new FieldVector(60f, 0f), new FieldVector(25f, 55f),
            new FieldVector(25f, 55f)
        };
        private static readonly string[] QbUpper = {
            "qb_run_anim", "qb_runl_anim", "qb_runb_anim",
            "qb_runb_anim", "qb_runb_anim", "qb_runr_anim"
        };
        private static readonly string[] QbLower = {
            "qb_run_anim", "qb_run_anim", "qb_runl45_anim",
            "qb_runb_anim", "qb_runr45_anim", "qb_run_anim"
        };
        private static readonly FieldVector[] Kickoff = {
            new FieldVector(-117f, 0f), new FieldVector(-78f, -156f), new FieldVector(-78f, -97.5f),
            new FieldVector(-78f, -39f), new FieldVector(-78f, 39f), new FieldVector(-78f, 97.5f),
            new FieldVector(-78f, 156f)
        };
        private static readonly FieldVector[] KickReturn = {
            new FieldVector(-387f, -78f), new FieldVector(-367.5f, 0f), new FieldVector(-387f, 78f),
            new FieldVector(-487.5f, 0f), new FieldVector(-441f, -52f),
            new FieldVector(-507.5f, -45f), new FieldVector(-441f, 52f)
        };
        private static readonly FieldVector[] ShortKickoff = {
            new FieldVector(-234f, 0f), new FieldVector(-39f, -156f), new FieldVector(-39f, -97.5f),
            new FieldVector(-39f, -39f), new FieldVector(-39f, 39f), new FieldVector(-39f, 97.5f),
            new FieldVector(-39f, 156f)
        };
        private static readonly FieldVector[] ShortReturn = {
            new FieldVector(-39f, -78f), new FieldVector(-39f, 0f), new FieldVector(-39f, 78f),
            new FieldVector(-273f, 0f), new FieldVector(-117f, -52f),
            new FieldVector(-293f, 50f), new FieldVector(-117f, 52f)
        };

        internal static void LineUp(SimulationState model, ISourceAnimationLibrary library, Func<int, int> random)
        {
            model.SourceHumanTurbo = 100f;
            for (int id = 0; id < model.Players.Length; id++)
            {
                ActorState actor = model.Players[id];
                int slot = id % SliceRules.PlayersPerTeam;
                bool offense = actor.Team == model.Possession;
                bool kickoff = model.SourceRules.PlayType == SourcePlayType.Kickoff;
                bool kicking = (int)actor.Team == model.SourceKickingSide;
                bool quickKick = kickoff && model.SourceKickoffKind == SourceKickoffKind.Quick;
                FieldVector placement = offense ? SideI[slot] : Cover[slot];
                if (!offense && model.DefensivePlay == PlayId.Blitz && slot >= 3)
                    placement = new FieldVector(25f, (slot - 4.5f) * 26f);
                if (kickoff)
                    placement = kicking ? quickKick ? ShortKickoff[slot] : Kickoff[slot]
                        : quickKick ? ShortReturn[slot] : KickReturn[slot];
                int placementDirection = kickoff ? actor.Team == TeamId.Home ? 1 : -1
                    : offense ? model.OffenseDirection : -model.OffenseDirection;
                if (!kickoff && !offense)
                    placement = new FieldVector(-placement.X, -placement.Z);
                actor.Position = new FieldVector(model.LineOfScrimmage + placementDirection * placement.X / UnitsPerYard,
                    model.SourceLinePosition + placementDirection * placement.Z / UnitsPerYard);
                actor.SnapPosition = actor.Position;
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
                actor.PendingThrowTarget = -1;
                actor.PendingThrowTurbo = false;
                actor.Down = false;
                actor.Turbo = false;
                actor.NonTurboTicks = 0;
                actor.Flipped = false;
                actor.SourceInPlay = false;
                actor.SourceBallKicked = false;
                // teamdata.c SC(1.0f), active SC(x) = 1.05f*x for this roster.
                actor.ModelScale = 1.05f;
                actor.Role = slot < 3 ? ActorRole.Lineman
                    : offense ? slot == 3 ? ActorRole.Quarterback : ActorRole.Receiver : ActorRole.Defender;
                if (kickoff)
                    actor.Role = kicking && slot == 0 && !quickKick ? ActorRole.Kicker : ActorRole.Receiver;
                actor.Facing = offense ? model.OffenseDirection > 0 ? 256 : 768
                    : model.OffenseDirection > 0 ? 768 : 256;
                actor.UpperFacing = actor.TargetFacing = actor.TargetUpperFacing = actor.Facing;
                actor.AnimationState = new SourceAnimationState(library, id);
                actor.Action = ActorAction.PreSnap;
                string script = slot < 3 ? slot == 1 && offense ? "center_set_anim" : "stance3pt_anim"
                    : offense ? slot == 3 ? "qbset_anim" : "wr_set_anim"
                    : "stance2pt2_anim_rnd";
                if (kickoff)
                    script = !kicking && slot == 3 ? quickKick ? "c_standing_anim" : "c_standing2_anim"
                        : quickKick && (kicking || slot >= 4) ? "run_hold_anim"
                        : slot == 0 && kicking || slot == 3 || slot == 6 ? "stance2pt_anim_rnd" : "stance2pt2_anim_rnd";
                actor.AnimationState.FullBody(script, actor, model.SourceTick, random, AnimationSelectionEvidence.Approximate);
                // Placement follows initial change_anim, as in the lineup path;
                // the established animation reference point is retained.
                actor.Position = actor.SnapPosition;
            }
            model.CarrierId = model.SourceRules.PlayType == SourcePlayType.Kickoff ? -1 : model.OffenseOffset + 3;
            model.LastTouchPlayerId = model.CarrierId;
            model.TargetPlayerId = -1;
            model.BallMode = model.CarrierId < 0 ? BallMode.Dead : BallMode.Held;
            model.BallKind = model.CarrierId < 0 ? SourceBallKind.Kickoff : SourceBallKind.Unspecified;
            model.BallPosition = model.CarrierId < 0 ? new FieldVector(model.LineOfScrimmage, 0f)
                : model.Players[model.CarrierId].Position;
            model.BallHeight = 0f;
            model.BallVelocity = default(FieldVector);
            model.BallVerticalVelocity = 0f;
            model.ControlledPlayerId = model.Possession == TeamId.Home ? 3 : PlayerSimulation.NearestHome(model, -1);
        }

        internal static void Snap(SimulationState model, Func<int, int> random)
        {
            for (int id = 0; id < model.Players.Length; id++)
            {
                ActorState actor = model.Players[id];
                actor.SnapPosition = actor.Position;
                actor.ModeChangedTick = model.SourceTick;
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
                    actor.AnimationState.FullBody(actor.Id == model.CarrierId ? "qb_stance_anim"
                        : actor.Id == model.ControlledPlayerId ? "safty2pt_anim_rnd" : "breath2_anim",
                        actor, model.SourceTick, random, AnimationSelectionEvidence.SourceVerified);
                }
            }
        }

        internal static void Update(SimulationState model, PlayerCommand command, Func<int, int> random,
            Action<int> releaseBall, Action<ActorState> checkCatch, Action<int> playerDown)
        {
            // plyrinfo.c:1713-1735: station resource, not a per-actor timer.
            if (model.Phase == GamePhase.LiveBall && model.ControlledPlayerId >= 0)
            {
                if (command.Turbo)
                    model.SourceHumanTurbo = Math.Max(0f, model.SourceHumanTurbo - 0.35f);
                else
                    model.SourceHumanTurbo = Math.Min(100f, model.SourceHumanTurbo + 0.35f
                        + (model.Players[model.ControlledPlayerId].Team != model.Possession ? 0.15f : 0f));
            }
            for (int id = 0; id < model.Players.Length; id++)
            {
                ActorState actor = model.Players[id];
                FieldVector before = actor.Position;
                bool live = model.Phase == GamePhase.LiveBall;
                if (actor.NoCatchTicks > 0)
                    actor.NoCatchTicks--;
                actor.SourceInPlay = live;
                actor.SourceBallKicked = (model.SourceFlags & SourceGameFlags.BallKicked) != 0;
                actor.SourceHasBall = model.BallMode == BallMode.Held && model.CarrierId == id;
                bool kickoffInFlight = model.SourceRules.PlayType == SourcePlayType.Kickoff && model.CarrierId < 0;
                bool human = id == model.ControlledPlayerId && !kickoffInFlight;
                actor.Turbo01 = id == model.ControlledPlayerId ? model.SourceHumanTurbo / 100f : 1f;
                bool intendedReceiver = model.BallMode == BallMode.InFlight && model.TargetPlayerId == id;
                FieldVector move = live ? human && !intendedReceiver ? Quantize(command.Move)
                    : PreviewDroneMove(model, actor) : default(FieldVector);
                bool approaching = model.SourceKickApproach && actor.Role == ActorRole.Kicker;
                if (approaching)
                    move = Toward(actor.Position, model.BallPosition);
                if (live)
                    checkCatch(actor);
                bool turbo = live && (human ? command.Turbo : actor.Turbo);
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
                Turn(actor, 64, 48);
                if ((live || approaching) && !actor.AnimationState.Busy && actor.PendingThrowTarget < 0
                    && actor.ModeChangedTick != model.SourceTick)
                    Movement(model, actor, move, random);
                else if (!live && !approaching && !actor.AnimationState.Busy && actor.Action != ActorAction.PreSnap)
                {
                    actor.SourceVelocity = default(FieldVector);
                    SelectIdle(model, actor, random);
                }

                SourceAnimationSignal signals = actor.AnimationState.Advance(actor, model.SourceTick, random);
                if ((signals & SourceAnimationSignal.ReleaseBall) != 0)
                    releaseBall(id);
                if ((signals & SourceAnimationSignal.PlayerDown) != 0)
                    playerDown(id);
                if ((signals & SourceAnimationSignal.GeneralRun) != 0)
                {
                    actor.Down = false;
                    actor.PendingThrowTarget = -1;
                    if (live)
                        Movement(model, actor, move, random, true);
                    else
                        SelectIdle(model, actor, random, true);
                }
                if (live && !human && actor.ModeChangedTick != model.SourceTick && actor.Action != ActorAction.Kick)
                    actor.AnimationState.Evidence = AnimationSelectionEvidence.Approximate;
                Physics(actor);
                actor.Velocity = PlayerSimulation.Scale(PlayerSimulation.Subtract(actor.Position, before),
                    SourceTiming.DefaultTicksPerSecond);
                if (!SourcePoseMath.IsFinite(actor.Position.X) || !SourcePoseMath.IsFinite(actor.Position.Z)
                    || !SourcePoseMath.IsFinite(actor.Height))
                    throw new InvalidOperationException("Non-finite source player state: actor=" + id
                        + " sourceTick=" + model.SourceTick + " script=" + actor.AnimationState.Primary.ScriptSymbol);
            }
        }

        internal static void StartCatch(SimulationState model, ActorState actor, Func<int, int> random)
        {
            if (actor.AnimationState.Busy)
                return;
            actor.Action = ActorAction.Catch;
            actor.AnimationState.FullBody("c_run1_anim", actor, model.SourceTick, random,
                AnimationSelectionEvidence.Approximate);
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
                SelectIdle(model, actor, random, force);
                return;
            }
            int heading = Heading(move);
            actor.TargetFacing = actor.TargetUpperFacing = heading;
            bool quarterback = actor.Role == ActorRole.Quarterback && actor.Id == model.CarrierId
                && !model.ForwardPassUsed
                && model.OffenseDirection * (actor.Position.X - model.SnapLineOfScrimmage) <= 0f;
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
                    actor.AnimationState.FullBody(script, actor, model.SourceTick, random,
                        carrier && model.OffenseDirection * (actor.Position.X - 50f) > 30f
                            ? AnimationSelectionEvidence.Approximate : AnimationSelectionEvidence.SourceVerified);
                }
            }
            actor.SourceVelocity = SourceAnimationLayerState.Rotate(0f, speed, heading);
        }

        private static string TurboScript(string normal, bool turbo)
        {
            return turbo ? normal.Replace("qb_", "qb_t_") : normal;
        }

        private static void SelectIdle(SimulationState model, ActorState actor, Func<int, int> random, bool force = false)
        {
            bool quarterback = actor.Role == ActorRole.Quarterback && actor.Id == model.CarrierId && !model.ForwardPassUsed;
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

        private static FieldVector PreviewDroneMove(SimulationState model, ActorState actor)
        {
            // The active _drone_qb is drone.c:1163, NOT its #if USE_OLD_QB
            // predecessor. Only its initial fifteen-tick dropback is ported here.
            // Remaining pursuit/route decisions stay explicitly Approximate.
            if (model.SourceRules.PlayType == SourcePlayType.Kickoff && model.CarrierId < 0)
                return Toward(actor.Position, model.PassTarget);
            if (actor.Team == model.Possession)
            {
                int slot = actor.Id % SliceRules.PlayersPerTeam;
                if (actor.Id == model.CarrierId)
                {
                    if (slot == 3 && model.LiveTicks < 15 && !model.ForwardPassUsed)
                    {
                        actor.Turbo = true;
                        return new FieldVector(-model.OffenseDirection, 0f);
                    }
                    return new FieldVector(model.OffenseDirection, 0f);
                }
                if (slot < 3)
                {
                    int opponent = (int)GameplayRules.Opponent(model.Possession) * SliceRules.PlayersPerTeam + slot;
                    return Toward(actor.Position, model.Players[opponent].Position);
                }
                if (model.BallMode == BallMode.InFlight && model.TargetPlayerId == actor.Id)
                    return Toward(actor.Position, model.PassTarget);
                return new FieldVector(model.OffenseDirection, slot == 4 && model.LiveTicks < 48 ? -0.35f : 0f);
            }
            if (model.BallMode == BallMode.InFlight && actor.Id % SliceRules.PlayersPerTeam >= 3)
            {
                int receiver = model.OffenseOffset + 4 + (actor.Id % SliceRules.PlayersPerTeam - 3) % 3;
                return Toward(actor.Position, model.Players[receiver].Position);
            }
            return Toward(actor.Position, model.BallMode == BallMode.Held
                ? model.Players[model.CarrierId].Position : model.BallPosition);
        }

        private static FieldVector Toward(FieldVector from, FieldVector to)
        {
            FieldVector difference = PlayerSimulation.Subtract(to, from);
            if (PlayerSimulation.DistanceSquared(from, to) < 0.01f)
                return default(FieldVector);
            return Quantize(PlayerSimulation.NormalizeInput(difference));
        }

        internal static FieldVector Quantize(FieldVector move)
        {
            FieldVector finite = PlayerSimulation.NormalizeInput(move);
            int x = (int)Math.Round(finite.X * 3f, MidpointRounding.AwayFromZero);
            int z = (int)Math.Round(finite.Z * 3f, MidpointRounding.AwayFromZero);
            if (x == 0 && z == 0)
                return default(FieldVector);
            int index = (3 - z) * 7 + (3 - x);
            return SourcePoseMath.Facing(Directions49[index]);
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

        private static void Physics(ActorState actor)
        {
            actor.Position = PlayerSimulation.Add(actor.Position,
                PlayerSimulation.Scale(actor.SourceVelocity, 1f / UnitsPerYard));
            actor.Height += (actor.SourceVerticalVelocity - 0.04f) / UnitsPerYard;
            actor.SourceVerticalVelocity -= 0.08f;
            if (actor.Height <= 0f)
            {
                actor.Height = 0f;
                actor.SourceVerticalVelocity = 0f;
            }
            if (actor.SourceFriction > 0f)
            {
                float speed = (float)Math.Sqrt(PlayerSimulation.DistanceSquared(actor.SourceVelocity, default(FieldVector)));
                if (speed < 0.0001f || actor.SourceFriction >= speed)
                {
                    actor.SourceVelocity = default(FieldVector);
                    actor.SourceFriction = 0f;
                }
                else
                    actor.SourceVelocity = PlayerSimulation.Scale(actor.SourceVelocity,
                        (speed - actor.SourceFriction) / speed);
            }
        }
    }
}
