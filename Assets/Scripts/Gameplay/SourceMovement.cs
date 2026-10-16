using System;
using System.Collections.Generic;

namespace Blitz.Gameplay
{
    public static class SourceMovement
    {
        public static bool HumanControls(SourceMovementState state, SourceMovementContext context)
        {
            state.Validate();
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!state.IsHuman) return false;
            if (context.PlayType == SourcePlayType.Kickoff && context.Carrier == -1 &&
                (context.GameFlags & SourceGameFlags.OnsideKick) == 0 && context.BallType == SourceBallType.Kickoff)
                return false;
            if ((context.GameFlags & SourceGameFlags.OnsideKick) != 0 && context.Phase == SourceMovementPhase.PreKickoff)
                return false;
            return !(context.Carrier == -1 && context.IntendedReceiver == state.PlayerId &&
                (context.BallType == SourceBallType.Pass || context.BallType == SourceBallType.Lateral));
        }

        public static SourceMovementState BeginTick(SourceMovementState state, SourceMovementSwitches switches,
            SourceMovementContext context)
        {
            state.Validate();
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!context.Tick.Execute) return state;
            state.StickTime = state.Stick == state.PreviousStick ? unchecked(state.StickTime + 1) : 0;
            state.PreviousStick = state.Stick;
            state.OldButtons = state.Buttons;
            bool wasBackpedal = (state.Flags & SourceMovementFlags.Backpedal) != 0;
            state.Flags &= ~SourceMovementFlags.Backpedal;
            state.Stick = switches.Stick; state.Buttons = switches.Buttons;
            if (switches.Backpedal)
            {
                state.Flags |= SourceMovementFlags.Backpedal;
                state.BackpedalExpireTime = 20;
            }
            else if (wasBackpedal && unchecked(--state.BackpedalExpireTime) != 0)
                state.Flags |= SourceMovementFlags.Backpedal;
            state.NewButtons = state.Buttons & ~state.OldButtons;
            if (state.AttackTime != 0 && --state.AttackTime == 0) state.AttackMode = 0;
            if (state.CelebrateTime != 0 && --state.CelebrateTime == 0) state.Flags &= ~SourceMovementFlags.Celebrate;
            // A zero chosen by set_defense_speeds never enters this branch (an original quirk).
            if (state.OverrideTime != 0 && --state.OverrideTime == 0) state.Flags |= SourceMovementFlags.SpeedOverride;
            state.NoCatchTime = Math.Max(unchecked(state.NoCatchTime - 1), 0);
            if ((state.Flags & SourceMovementFlags.Turbo) != 0) state.Flags |= SourceMovementFlags.OldTurbo;
            else state.Flags &= ~SourceMovementFlags.OldTurbo;
            bool passReceiver = context.Carrier == -1 && context.BallType == SourceBallType.Pass &&
                context.IntendedReceiver == state.PlayerId;
            if ((state.Buttons & SourceDroneButtons.C) != 0 &&
                (!state.IsHuman || passReceiver || switches.StationTurbo > 0f))
            {
                state.Flags |= SourceMovementFlags.Turbo;
                state.NonTurboTime = DoublePress(state.NonTurboTime);
            }
            else
            {
                state.Flags &= ~SourceMovementFlags.Turbo;
                state.NonTurboTime = unchecked(state.NonTurboTime + 1);
            }
            state.NonATime = (state.Buttons & SourceDroneButtons.A) != 0
                ? DoublePress(state.NonATime) : unchecked(state.NonATime + 1);
            state.NonBTime = (state.Buttons & SourceDroneButtons.B) != 0
                ? DoublePress(state.NonBTime) : unchecked(state.NonBTime + 1);
            return state;
        }

        private static int DoublePress(int releasedTicks)
        {
            return releasedTicks > 0 && releasedTicks < 9 ? -1 : 0;
        }

        public static float StationTurbo(SourceMovementTick tick, float turbo, bool active, bool hasControlledActor,
            SourceMovementPhase phase, bool stationTeamFire, bool infiniteTurbo, bool turboHeld, bool controlledActorDefends)
        {
            SourceMovementGuard.Finite(turbo, nameof(turbo));
            if (turbo < 0f || turbo > 100f) throw new ArgumentOutOfRangeException(nameof(turbo));
            if (!tick.Execute || !active || !hasControlledActor || phase != SourceMovementPhase.InPlay ||
                stationTeamFire || infiniteTurbo) return turbo;
            if (turboHeld)
            {
                turbo -= 0.35f;
                if (turbo < 0f) turbo = 0f;
            }
            else
            {
                turbo += 0.35f;
                if (turbo > 100f) turbo = 100f;
                if (controlledActorDefends)
                {
                    turbo += 0.15f;
                    if (turbo > 100f) turbo = 100f;
                }
            }
            return turbo;
        }

        public static SourceMovementState Turn(SourceMovementState state, SourceMovementTick tick)
        {
            state.Validate();
            if (!tick.Execute) return state;
            state.Heading = TurnHeading(state.Heading, state.TargetHeading, state.TurnRate);
            state.UpperHeading = TurnHeading(state.UpperHeading, state.UpperTargetHeading, state.UpperTurnRate);
            return state;
        }

        private static int TurnHeading(int heading, int target, int rate)
        {
            int distance = target - heading;
            if (distance == 0) return heading;
            if (Math.Abs(distance) <= rate) heading = target;
            else if ((distance > -1024 && distance < -512) || (distance > 0 && distance < 513)) heading += rate;
            else heading -= rate;
            return heading & 1023;
        }

        public static bool ModeAllowsRun(SourceMovementState state, SourceMovementContext context)
        {
            state.Validate();
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!context.Tick.Execute) return false;
            if (state.Mode == SourceActorMode.Quarterback)
                return (state.LowerMode & SourceMovementModes.Uninterruptible) == 0 &&
                    (context.OffensePlayFlags & (SourcePlayFlags.FieldGoal | SourcePlayFlags.Punt)) == 0;
            if (state.Mode == SourceActorMode.Receiver)
                return ((state.LowerMode | state.UpperMode) & SourceMovementModes.Uninterruptible) == 0 ||
                    (context.Carrier == state.PlayerId && (state.UpperMode & SourceMovementModes.TwoPart) != 0 &&
                    (state.UpperSequence == "M_WAVQB" || state.UpperSequence == "M_WAVQBF"));
            return (state.LowerMode & SourceMovementModes.Uninterruptible) == 0;
        }

        public static SourceMovementResult StandardRun(SourceMovementState state, SourceMovementContext context,
            SourceMovementData data, ISourceMovementBoundary boundary, ISourceRandom random,
            SourceRandomContext randomContext, uint breathControl)
        {
            var e = new SourceMovementExecution(state, context, data, boundary, random, randomContext, breathControl);
            if (!context.Tick.Execute) return e.Finish();
            if (data == null) throw new ArgumentNullException(nameof(data));
            StandardRun(e);
            return e.Finish();
        }

        internal static void StandardRun(SourceMovementExecution e)
        {
            SourceMovementContext c = e.Context;
            bool skipUninterruptible = false;
            if (e.State.Stick != 24)
            {
                int direction = FaceDirection(e.State.Stick, c);
                if (e.State.Mode == SourceActorMode.ReceiverPreSnap && c.PlayType != SourcePlayType.Kickoff &&
                    (c.OffensePlayFlags & SourcePlayFlags.FakeFieldGoal) == 0)
                {
                    if (direction <= 128 || direction >= 896) direction = 0;
                    else if (direction < 384) e.State.Stick = 24;
                    else if (direction <= 640) direction = 512;
                    else e.State.Stick = 24;
                    if (e.State.Z < c.LineZ - 60f && direction == 512) e.State.Stick = 24;
                    if (e.State.Z > c.LineZ + 60f && direction == 0) e.State.Stick = 24;
                }
                if (e.State.Stick != 24)
                {
                    e.State.TargetHeading = direction;
                    int sequence = c.Carrier == e.State.PlayerId ? 2 : 0;
                    if ((e.State.Flags & SourceMovementFlags.Turbo) != 0) sequence++;
                    if ((c.GameFlags & SourceGameFlags.OnsideKick) != 0 && c.Phase == SourceMovementPhase.PreKickoff)
                        sequence &= ~1;
                    bool backpedal = false;
                    if ((e.State.Flags & SourceMovementFlags.Backpedal) != 0 && c.BackpedalTargetHeading >= 0)
                    {
                        int viewMove = (1024 + direction - c.BackpedalTargetHeading) % 1024;
                        if (viewMove > 512) viewMove = 1024 - viewMove;
                        if (c.Carrier != e.State.PlayerId && !e.State.IsHuman && sequence <= 1 && viewMove >= 384)
                        {
                            sequence += 4; backpedal = true;
                        }
                    }
                    int speedIndex = sequence + (int)e.State.Mode * 6;
                    if (e.State.Mode == SourceActorMode.ReceiverPreSnap && e.State.IsHuman) speedIndex += 6;
                    if (e.State.Side != c.OffenseSide && e.State.IsHuman) speedIndex += 2;
                    if (e.State.Side == c.OffenseSide && e.State.IsHuman && c.Carrier != -1 &&
                        c.Carrier != e.State.PlayerId && c.CarrierMode == SourceActorMode.Receiver &&
                        e.State.Mode == SourceActorMode.Receiver) speedIndex += 14;
                    float speed = e.Data.Speed(speedIndex);
                    if (e.State.Side != c.OffenseSide && e.State.Mode == SourceActorMode.Line && !backpedal)
                        sequence = (e.State.Flags & SourceMovementFlags.Turbo) != 0 ? 7 : 6;
                    if (!e.State.IsHuman && (e.State.Flags & SourceMovementFlags.SpeedOverride) != 0)
                        speed = Math.Max(speed, e.State.OverrideSpeed);
                    if (e.State.Side != c.OffenseSide && (e.State.Flags & SourceMovementFlags.OnScreen) == 0)
                        speed *= c.Carrier == -1 ? 1.5f : 2.5f;
                    if (c.PlayType == SourcePlayType.Kickoff && (c.GameFlags & SourceGameFlags.OnsideKick) == 0 &&
                        c.BallType == SourceBallType.Kickoff && c.Carrier == -1 && e.State.Mode == SourceActorMode.Receiver)
                        speed *= 1.38f;
                    if (e.State.Side != c.OffenseSide && e.State.IsHuman && c.Carrier != -1 &&
                        FieldX(e.State.X, c.OffenseSide) < FieldX(c.BallX, c.OffenseSide)) speed *= 1.20f;
                    // pup_speed is global in this source, not a per-side mask.
                    if (c.SpeedPowerup) speed *= 1.3f;
                    if (c.FastTurboPowerup && (e.State.Flags & SourceMovementFlags.Turbo) != 0 && !c.SpeedPowerup)
                        speed *= 1.1f;
                    if (speed < 1.45f) sequence &= ~1;
                    if ((e.State.Flags & SourceMovementFlags.DraggingPlayer) != 0) speed *= 0.85f;
                    Velocity(ref e.State, c.Directions, 0f, speed, e.State.TargetHeading);
                    bool carrier = c.Carrier == e.State.PlayerId;
                    bool dragging = (e.State.Flags & SourceMovementFlags.DraggingPlayer) != 0;
                    if (carrier && dragging && (e.State.UpperMode & SourceMovementModes.StiffArm) != 0 &&
                        (e.State.UpperMode & SourceMovementModes.TwoPart) == 0)
                    {
                        skipUninterruptible = true; e.BeginTwoPart();
                        e.Apply(SourceMovementCommandKind.UpperBody,
                            e.Draw(100, "player.c:standard_run:drag-stiff-arm") < 50 ? "stiff_arm_anim" : "stiff_arm_f_anim");
                        e.Apply(SourceMovementCommandKind.StiffArmSpeech);
                    }
                    if (carrier && !dragging)
                    {
                        if (e.State.UpperSequence != "M_PLOWHD" && (e.State.UpperMode & SourceMovementModes.TwoPart) == 0 &&
                            (e.State.Buttons & SourceDroneButtons.C) != 0 &&
                            e.State.LowerSequence == e.Data.Script("run_seqs", sequence) &&
                            Speed(e.State) > 1.20f && (c.OffenseSide != 0 ? -e.State.VelocityX : e.State.VelocityX) > 0f)
                        {
                            int count = 0;
                            foreach (SourceMovementNeighbor neighbor in c.Neighbors)
                            {
                                if (neighbor.PlayerId == e.State.PlayerId || neighbor.PlayerId / 7 == e.State.Side) continue;
                                // The source draws BEFORE the distance/bearing tests, even for distant opponents.
                                if (e.Draw(100, "player.c:standard_run:plow") > c.PlowProbability) continue;
                                if (neighbor.Distance / 7.8f > 8f) continue;
                                if (neighbor.Bearing < 64 && ++count >= 2)
                                {
                                    e.BeginTwoPart(); e.Apply(SourceMovementCommandKind.UpperBody, "running_plow_anim"); break;
                                }
                            }
                        }
                        if ((e.State.UpperMode & SourceMovementModes.StiffArm) != 0 &&
                            (e.State.UpperMode & SourceMovementModes.TwoPart) == 0)
                        {
                            skipUninterruptible = true; e.BeginTwoPart();
                            string script = c.TeamIndexedTurbo > 5f
                                ? e.Draw(100, "player.c:standard_run:stiff-arm") < 50 ? "stiff_arm_anim" : "stiff_arm_f_anim"
                                : "stiff_arm_nt_anim";
                            e.Apply(SourceMovementCommandKind.UpperBody, script);
                            e.Apply(SourceMovementCommandKind.StiffArmSpeech);
                        }
                        else if (FieldX(e.State.X, c.OffenseSide) > 80 &&
                            (e.State.UpperMode & SourceMovementModes.TwoPart) == 0 && !IsRunningTaunt(e.State.LowerSequence))
                        {
                            skipUninterruptible = true;
                            e.Apply(SourceMovementCommandKind.FullBody,
                                e.Pick("running_taunt_tbl", "player.c:standard_run:taunt"));
                            foreach (SourceMovementNeighbor neighbor in c.Neighbors)
                                if (neighbor.PlayerId != e.State.PlayerId && neighbor.PlayerId / 7 != e.State.Side &&
                                    neighbor.Distance / 7.8f < 6f)
                                { e.Apply(SourceMovementCommandKind.FullBody, "r_ezsprint_anim"); break; }
                        }
                        else if (FieldX(e.State.X, c.OffenseSide) <= 80 &&
                            e.State.LowerSequence != e.Data.Script("run_seqs", sequence) &&
                            (e.State.UpperMode & SourceMovementModes.TwoPart) == 0 &&
                            (e.State.LowerMode & SourceMovementModes.RotationOnly) == 0)
                            RunScript(e, sequence);
                    }
                    else if (!carrier)
                    {
                        if ((e.State.UpperMode & SourceMovementModes.RunningPush) != 0 &&
                            (e.State.UpperMode & SourceMovementModes.TwoPart) == 0)
                        {
                            skipUninterruptible = true; e.BeginTwoPart();
                            e.Apply(SourceMovementCommandKind.UpperBody, "t_running_push_anim");
                        }
                        int quarterback = c.OffenseSide != 0 ? 10 : 3;
                        if ((c.DefensePlayFlags & SourcePlayFlags.Blitz) != 0 &&
                            (e.State.UpperMode & SourceMovementModes.TwoPart) == 0 &&
                            (e.State.LowerMode & SourceMovementModes.Uninterruptible) == 0 &&
                            FieldX(e.State.X, c.OffenseSide) < c.LineOfScrimmage && e.State.Side != c.OffenseSide &&
                            e.State.Mode == SourceActorMode.Line && c.Neighbors[quarterback].Distance / 7.8f < 8f)
                        {
                            skipUninterruptible = true; e.BeginTwoPart();
                            e.Apply(SourceMovementCommandKind.UpperBody, "r_crzbl2_anim");
                        }
                        if ((e.State.Flags & SourceMovementFlags.WaveArm) != 0 && Speed(e.State) > 1f)
                        {
                            e.State.Flags &= ~SourceMovementFlags.WaveArm;
                            skipUninterruptible = true; e.BeginTwoPart();
                            e.Apply(SourceMovementCommandKind.UpperBody,
                                (c.OffenseSide != 0 ? -e.State.VelocityZ : e.State.VelocityZ) > 0f ? "m_wavqb_anim" : "m_wavqbf_anim");
                        }
                        else if (e.State.LowerSequence != e.Data.Script("run_seqs", sequence) &&
                            (e.State.LowerMode & SourceMovementModes.RotationOnly) == 0) RunScript(e, sequence);
                        if (backpedal) e.State.TargetHeading = c.BackpedalTargetHeading;
                    }
                }
            }
            if (e.State.Stick == 24 && !(c.Carrier == e.State.PlayerId &&
                (e.State.Flags & SourceMovementFlags.DraggingPlayer) != 0))
            {
                e.State.VelocityX = e.State.VelocityZ = 0f;
                if (e.State.PreviousStick != 24 || (e.State.LowerMode & SourceMovementModes.Uninterruptible) != 0)
                {
                    if (c.Carrier == e.State.PlayerId) e.Apply(SourceMovementCommandKind.FullBody, "stance_wb_anim_int");
                    else if (e.State.Mode == SourceActorMode.ReceiverPreSnap && c.PlayType != SourcePlayType.Kickoff)
                    {
                        e.State.TargetHeading = c.OffenseSide != 0 ? 768 : 256;
                        e.Apply(SourceMovementCommandKind.FullBody, "wr_set_anim_int");
                        e.State.X = c.MotionSnapX;
                    }
                    else if (c.Phase != SourceMovementPhase.PlayOver)
                        e.Apply(SourceMovementCommandKind.FullBody,
                            e.State.Side != c.OffenseSide && c.DefenseFire && c.Phase == SourceMovementPhase.PreSnap
                            ? "m_dblood_anim" : e.State.IsHuman ? "safty2pt_anim_rnd" : "breath2_anim");
                    else
                    {
                        int count = e.Data.Count("breath_seq_tbl");
                        if (count > 32) throw new InvalidOperationException("Original unique_random control is a 32-bit mask.");
                        int selected = Unique(e, count, e.BreathControl);
                        e.BreathControl |= 1u << selected;
                        e.Apply(SourceMovementCommandKind.FullBody, e.Data.Script("breath_seq_tbl", selected));
                    }
                }
                if ((e.State.Flags & SourceMovementFlags.DroneFace) != 0)
                {
                    e.State.Flags &= ~SourceMovementFlags.DroneFace;
                    e.State.TargetHeading = e.State.DroneFace;
                }
            }
            if (!skipUninterruptible) e.State.LowerMode &= ~SourceMovementModes.Uninterruptible;
        }

        private static bool IsRunningTaunt(string sequence)
        {
            return sequence == "R_HISTEP" || sequence == "R_HIHAND" || sequence == "R_SKIP" ||
                sequence == "R_BKTAU2" || sequence == "R_INBTA2" || sequence == "R_EZSPRI";
        }

        private static int Unique(SourceMovementExecution e, int range, uint control)
        {
            int attempts = 0, selected;
            while (true)
            {
                selected = e.Draw(range, "player.c:unique_random");
                bool done = (control & (1u << selected)) == 0;
                if (++attempts > (range << 1))
                {
                    selected = -1;
                    for (int i = 0; i < range; i++)
                        if ((control & (1u << i)) == 0) { selected = i; break; }
                    if (selected == -1) selected = e.Draw(range, "player.c:unique_random:fallback");
                    return selected;
                }
                if (done) return selected;
            }
        }

        private static void RunScript(SourceMovementExecution e, int index)
        {
            e.Apply(SourceMovementCommandKind.FullBody,
                e.Data.Script((e.State.LowerMode & SourceMovementModes.PrepareTransition) != 0
                    ? "int_run_scripts" : "run_scripts", index));
        }

        public static SourceMovementResult QuarterbackRun(SourceMovementState state, SourceMovementContext context,
            SourceMovementData data, ISourceMovementBoundary boundary, ISourceRandom random, SourceRandomContext randomContext)
        {
            var e = new SourceMovementExecution(state, context, data, boundary, random, randomContext);
            if (!context.Tick.Execute) return e.Finish();
            if (data == null) throw new ArgumentNullException(nameof(data));
            QuarterbackRun(e);
            return e.Finish();
        }

        internal static void QuarterbackRun(SourceMovementExecution e)
        {
            if ((e.State.UpperMode & SourceMovementModes.Uninterruptible) != 0) return;
            SourceMovementContext c = e.Context;
            int face = FaceDirection(e.State.Stick, c);
            int adjusted = (face - 256 + 512 * e.State.Side) & 1023;
            int sector = 0;
            float speed = e.Data.Constant("QB_SPEED"), turbo = e.Data.Constant("QB_TURBO_SPEED");
            if (adjusted > 128 && adjusted <= 332) { sector = 1; e.State.Flags |= SourceMovementFlags.Flip; }
            if (adjusted > 332 && adjusted < 460) { sector = 2; e.State.Flags |= SourceMovementFlags.Flip; }
            if (adjusted >= 460 && adjusted <= 564) sector = 3;
            if (adjusted > 564 && adjusted < 692) { sector = 4; e.State.Flags &= ~SourceMovementFlags.Flip; }
            if (adjusted >= 692 && adjusted < 896) { sector = 5; e.State.Flags &= ~SourceMovementFlags.Flip; }
            if (sector >= 2 && sector <= 4)
            {
                speed = e.Data.Constant("QB_BACKP_SPEED"); turbo = e.Data.Constant("QB_BACKP_TURBO_SPEED");
            }
            int index = sector * 4 + ((e.State.Flags & SourceMovementFlags.Turbo) != 0 ? 1 : 0);
            if (FieldX(e.State.X, c.OffenseSide) < c.LineOfScrimmage - 18) { speed /= 1.50f; turbo /= 1.50f; }
            if (e.State.Stick != 24)
            {
                Velocity(ref e.State, c.Directions, 0f, (e.State.Flags & SourceMovementFlags.Turbo) != 0 ? turbo : speed, face);
                e.BeginTwoPart();
                e.State.TargetHeading = face;
                if (adjusted > 332 && adjusted < 692) e.State.TargetHeading = c.OffenseSide != 0 ? 768 : 256;
                e.State.UpperTargetHeading = e.State.TargetHeading;
                e.State.TurnRate = 64; e.State.UpperTurnRate = 48;
                if (e.Data.Script("qb_seqs", index) != e.State.UpperSequence &&
                    (e.State.LowerMode & SourceMovementModes.RotationOnly) == 0)
                {
                    if (e.State.UpperHeading != e.State.TargetHeading)
                    {
                        e.State.UpperMode |= SourceMovementModes.TurnHack;
                        e.State.UpperTurnHack = e.State.UpperHeading - e.State.TargetHeading;
                        e.State.UpperHeading = e.State.TargetHeading;
                        e.State.UpperTargetHeading = e.State.TargetHeading;
                    }
                    e.State.UpperVariableFrame = -1; e.State.UpperVariableCount = 4;
                    e.Apply(SourceMovementCommandKind.UpperBody, e.Data.Script("qb_runs", index));
                }
                if (e.Data.Script("qb_seqs", index + 2) != e.State.LowerSequence &&
                    (e.State.LowerMode & SourceMovementModes.RotationOnly) == 0)
                {
                    e.State.LowerVariableFrame = -1; e.State.LowerVariableCount = 2;
                    e.Apply(SourceMovementCommandKind.LowerBody, e.Data.Script("qb_runs", index + 2));
                }
            }
            else
            {
                e.State.VelocityX = e.State.VelocityY = e.State.VelocityZ = 0f;
                if (e.State.PreviousStick != 24 || (e.State.LowerMode & SourceMovementModes.Uninterruptible) != 0)
                {
                    e.BeginTwoPart();
                    e.State.TargetHeading = c.OffenseSide != 0 ? 768 : 256;
                    e.State.UpperMode |= SourceMovementModes.TurnHack;
                    e.State.UpperTurnHack = e.State.UpperHeading - e.State.TargetHeading;
                    e.State.UpperTargetHeading = e.State.TargetHeading;
                    e.State.Heading = e.State.UpperHeading = e.State.TargetHeading;
                    e.Apply(SourceMovementCommandKind.FullBody,
                        (e.State.Flags & SourceMovementFlags.Flip) != 0 ? "qb_flip_stance_anim" : "qb_stance_anim");
                }
            }
        }

        public static SourceMovementResult GeneralRun(SourceMovementState state, SourceMovementContext context,
            SourceMovementData data, ISourceMovementBoundary boundary, ISourceRandom random,
            SourceRandomContext randomContext, uint breathControl)
        {
            return state.Mode == SourceActorMode.Quarterback && context.Phase != SourceMovementPhase.PlayOver
                ? QuarterbackRun(state, context, data, boundary, random, randomContext)
                : StandardRun(state, context, data, boundary, random, randomContext, breathControl);
        }

        public static SourceMovementResult Integrate(SourceMovementState state, SourceMovementContext context,
            float fieldFrictionMultiplier, ISourceMovementBoundary boundary, SourceRandomContext randomContext)
        {
            var e = new SourceMovementExecution(state, context, null, boundary, null, randomContext);
            SourceMovementGuard.Finite(fieldFrictionMultiplier, nameof(fieldFrictionMultiplier));
            if (fieldFrictionMultiplier < 0f) throw new ArgumentOutOfRangeException(nameof(fieldFrictionMultiplier));
            if (!context.Tick.Execute) return e.Finish();
            if ((e.State.LowerMode & SourceMovementModes.Puppet) == 0)
            {
                e.State.Z = Math.Max(Math.Min(e.State.Z, 182.85f + 120f), -(182.85f + 120f));
                e.State.X = Math.Max(Math.Min(e.State.X, 468f + 35f), -(468f + 35f));
                if (((e.State.LowerMode & SourceMovementModes.Uninterruptible) == 0 || e.State.LowerSequence == "SPIN") &&
                    context.Phase == SourceMovementPhase.InPlay && !context.RunOutOfBoundsPowerup)
                {
                    bool corrected = false;
                    CorrectBoundary(ref e.State.Z, ref e.State.VelocityZ, 182.85f - 4f, ref corrected);
                    CorrectBoundary(ref e.State.X, ref e.State.VelocityX, 468f - 5f, ref corrected);
                    if (corrected && e.State.Mode == SourceActorMode.Receiver) e.Apply(SourceMovementCommandKind.AbortRoute);
                }
                e.State.X += e.State.VelocityX;
                e.State.Y += e.State.VelocityY + -0.08f / 2f;
                e.State.Z += e.State.VelocityZ;
                e.State.VelocityY += -0.08f;
                if (e.State.Y <= 0f) e.State.Y = e.State.VelocityY = 0f;
            }
            if (e.State.Friction != 0f)
            {
                float speed = Speed(e.State);
                if (speed < 0.0001f)
                    e.State.VelocityX = e.State.VelocityZ = e.State.Friction = 0f;
                else
                {
                    float x, z;
                    if (e.State.Friction > speed)
                    { x = e.State.VelocityX; z = e.State.VelocityZ; e.State.Friction = 0f; }
                    else
                    {
                        x = e.State.Friction * e.State.VelocityX / speed;
                        z = e.State.Friction * e.State.VelocityZ / speed;
                        x *= fieldFrictionMultiplier; z *= fieldFrictionMultiplier;
                    }
                    e.State.VelocityX -= x; e.State.VelocityZ -= z;
                }
            }
            e.State.Flags |= SourceMovementFlags.InBounds;
            if (Math.Abs(e.State.X) > 468f || Math.Abs(e.State.Z) > 182.85f)
                e.State.Flags ^= SourceMovementFlags.InBounds;
            return e.Finish();
        }

        private static void CorrectBoundary(ref float position, ref float velocity, float limit, ref bool corrected)
        {
            if (position + velocity > limit)
            {
                corrected = true;
                if (velocity > 0f || velocity > -0.5f) velocity = -0.5f;
                if (position + velocity <= limit) velocity = limit - position;
            }
            if (position + velocity < -limit)
            {
                corrected = true;
                if (velocity < 0f || velocity < 0.5f) velocity = 0.5f;
                if (position + velocity >= -limit) velocity = -limit - position;
            }
        }

        public static IReadOnlyList<SourceMovementDefenseSpeed> AssignDefenseSpeeds(SourceMovementTick tick,
            int offenseSide, IReadOnlyList<SourceActorMode> defenderModes, IReadOnlyList<float> lineSpeeds,
            IReadOnlyList<float> backSpeeds, int accelerationBase, int accelerationRange, bool redZone,
            ISourceRandom random, SourceRandomContext randomContext)
        {
            SourceMovementGuard.Side(offenseSide);
            if (defenderModes == null || lineSpeeds == null || backSpeeds == null)
                throw new ArgumentNullException(nameof(defenderModes));
            if (defenderModes.Count != 7 || lineSpeeds.Count != 6 || backSpeeds.Count != 6)
                throw new ArgumentException("Supply seven defense modes and the actual selected six-column handicap rows.");
            if (accelerationBase < 0 || accelerationRange < 0) throw new ArgumentOutOfRangeException(nameof(accelerationRange));
            var result = new List<SourceMovementDefenseSpeed>();
            if (!tick.Execute) return result.AsReadOnly();
            if (random == null) throw new InvalidOperationException("set_defense_speeds requires an explicit recorded source RNG.");
            if (randomContext.SourceTick != tick.SourceTick)
                throw new ArgumentException("Defense-speed RNG context has a different source tick.", nameof(randomContext));
            var draws = new SourceRandomCursor(random, randomContext);
            for (int i = 0; i < 7; i++)
            {
                int column = draws.Range(6, "pmisc.c:set_defense_speeds:column");
                float speed = defenderModes[i] == SourceActorMode.Line ? lineSpeeds[column] : backSpeeds[column];
                SourceMovementGuard.Finite(speed, nameof(speed));
                int delay = draws.Range(redZone ? 20 : accelerationRange, "pmisc.c:set_defense_speeds:delay");
                if (!redZone) delay += accelerationBase;
                result.Add(new SourceMovementDefenseSpeed((1 - offenseSide) * 7 + i, speed, delay));
            }
            return result.AsReadOnly();
        }

        internal static int FaceDirection(int stick, SourceMovementContext context)
        {
            int direction = context.Directions.StickHeading(stick);
            return ((direction < 0 ? 0 : direction) + context.CameraHeading) & 1023;
        }

        internal static int FieldX(float x, int offenseSide)
        {
            return (int)((390f + (offenseSide != 0 ? -1f * x : x)) / (1.3f * 6f));
        }

        internal static float Speed(SourceMovementState state)
        {
            return (float)Math.Sqrt(state.VelocityX * state.VelocityX + state.VelocityZ * state.VelocityZ);
        }

        internal static void Velocity(ref SourceMovementState state, SourceDirectionMath directions, float x, float z, int heading)
        {
            state.VelocityX = z * directions.Sin(heading) + x * directions.Cos(heading);
            state.VelocityZ = z * directions.Cos(heading) - x * directions.Sin(heading);
        }
    }

    public readonly struct SourceMovementDefenseSpeed
    {
        public int PlayerId { get; }
        public float Speed { get; }
        public int OverrideTime { get; }
        public SourceMovementDefenseSpeed(int playerId, float speed, int overrideTime)
        {
            PlayerId = playerId; Speed = speed; OverrideTime = overrideTime;
        }
    }
}
