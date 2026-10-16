using System;

namespace Blitz.Gameplay
{
    public static class SourceEvasion
    {
        public static bool SlowerSpin(int teamSpinCount) { return teamSpinCount > 2; }

        // Run from the actual maybe_dive script callback, not on every tick of a hurdle.
        public static SourceMovementResult MaybeDive(SourceMovementState state, SourceMovementContext context,
            ISourceMovementBoundary boundary, ISourceRandom random, SourceRandomContext randomContext)
        {
            var e = new SourceMovementExecution(state, context, null, boundary, random, randomContext);
            if (context.Tick.Execute && state.NonATime == -1)
            {
                if (state.PlayerId == (context.OffenseSide != 0 ? 10 : 3))
                    e.Apply(SourceMovementCommandKind.FullBody, "qb_slide_anim");
                else
                {
                    e.State.Flags &= ~SourceMovementFlags.Flip;
                    e.Apply(SourceMovementCommandKind.FullBody, "r_diveoh_anim");
                }
            }
            return e.Finish();
        }

        // This precedes the WPN !UNINT test, so a hurdle out of the head-plow can start while UNINT.
        public static SourceEvasionResult WeaponBeforeRun(SourceMovementState state, SourceMovementContext context,
            SourceMovementData data, ISourceMovementBoundary boundary, ISourceRandom random,
            SourceRandomContext randomContext)
        {
            var e = new SourceMovementExecution(state, context, data, boundary, random, randomContext);
            int teamSpinCount = state.TeamSpinCount, teamHurdleCount = state.TeamHurdleCount;
            if (context.Tick.Execute && context.Carrier == state.PlayerId &&
                (state.Flags & SourceMovementFlags.DraggingPlayer) == 0 && state.UpperSequence == "M_PLOWHD" &&
                (state.UpperMode & SourceMovementModes.Uninterruptible) != 0 &&
                (state.NewButtons & SourceDroneButtons.A) != 0 && (state.Buttons & SourceDroneButtons.C) != 0)
            {
                teamHurdleCount++;
                e.State.TeamHurdleCount = teamHurdleCount;
                e.Apply(SourceMovementCommandKind.FullBody, e.Pick("turbo_hurdle_tbl", "player.c:_mode_wpn:plow-hurdle"));
                e.Apply(SourceMovementCommandKind.HurdleSpeech);
            }
            return new SourceEvasionResult(e.Finish(), teamSpinCount, teamHurdleCount);
        }

        // Caller must retain the pre-run eligibility result. standard_run/change_anim may alter UNINT.
        public static SourceEvasionResult WeaponAfterRun(SourceMovementState state, SourceMovementContext context,
            SourceMovementData data, ISourceMovementBoundary boundary, ISourceRandom random,
            SourceRandomContext randomContext, bool enteredControlBlock)
        {
            var e = new SourceMovementExecution(state, context, data, boundary, random, randomContext);
            int teamSpinCount = state.TeamSpinCount, teamHurdleCount = state.TeamHurdleCount;
            if (!context.Tick.Execute || !enteredControlBlock) return new SourceEvasionResult(e.Finish(), teamSpinCount, teamHurdleCount);
            bool dragging = (e.State.Flags & SourceMovementFlags.DraggingPlayer) != 0;
            if (e.State.NonTurboTime == -1 && !dragging && context.Phase != SourceMovementPhase.PreKickoff &&
                context.Phase != SourceMovementPhase.PreSnap)
            {
                teamSpinCount++;
                e.State.TeamSpinCount = teamSpinCount;
                e.Apply(SourceMovementCommandKind.FullBody, "spin_anim");
                if (context.Carrier == e.State.PlayerId) e.Apply(SourceMovementCommandKind.SpinSpeech);
            }
            if (context.Carrier == e.State.PlayerId)
            {
                if (dragging && (e.State.NewButtons & SourceDroneButtons.B) != 0 &&
                    (e.State.Buttons & SourceDroneButtons.C) != 0 && (e.State.UpperMode & SourceMovementModes.StiffArm) == 0)
                    e.State.UpperMode |= SourceMovementModes.StiffArm;
                if (!dragging)
                {
                    if ((e.State.NewButtons & SourceDroneButtons.A) != 0)
                    {
                        teamHurdleCount++;
                        e.State.TeamHurdleCount = teamHurdleCount;
                        bool turbo = (e.State.Buttons & SourceDroneButtons.C) != 0;
                        e.Apply(SourceMovementCommandKind.FullBody, e.Pick(turbo ? "turbo_hurdle_tbl" : "hurdle_tbl",
                            "player.c:_mode_wpn:hurdle"));
                        e.Apply(SourceMovementCommandKind.HurdleSpeech);
                    }
                    if ((e.State.NewButtons & SourceDroneButtons.B) != 0 && (e.State.Buttons & SourceDroneButtons.C) != 0 &&
                        SourceMovement.Speed(e.State) > 1f && (e.State.UpperMode & SourceMovementModes.StiffArm) == 0 &&
                        e.State.LowerSequence != "SPIN" && e.State.LowerSequence != "HURDLE" &&
                        e.State.LowerSequence != "R_HURDSP" && e.State.LowerSequence != "HURDLE1" && e.State.LowerSequence != "HURDLE2")
                        e.State.UpperMode |= SourceMovementModes.StiffArm;
                }
                // Lateral/fire/possession/contact consequences remain Core-owned caller stages.
            }
            else if (e.State.Job == SourceActorJob.Block)
            {
                if ((e.State.NewButtons & SourceDroneButtons.B) != 0 && context.PlayType != SourcePlayType.Kickoff &&
                    e.Apply(SourceMovementCommandKind.ChangeWeaponPlayer) != 0)
                    return new SourceEvasionResult(e.Finish(), teamSpinCount, teamHurdleCount, true);
                if ((e.State.NewButtons & SourceDroneButtons.A) != 0)
                {
                    if (context.Carrier == -1 && context.Phase != SourceMovementPhase.PlayOver)
                    {
                        if ((e.State.Buttons & SourceDroneButtons.C) != 0)
                        {
                            Face(ref e.State, context, false);
                            e.Apply(SourceMovementCommandKind.FullBody, Loose(context) ? "c_dive_for_ball_anim" : "t_crzdiv_anim");
                        }
                        else if (Loose(context)) e.Apply(SourceMovementCommandKind.FullBody, "c_dive_for_ball_anim");
                        else if (context.BallTicksLeft > 10) Intercept(e);
                    }
                    else
                    {
                        Face(ref e.State, context, false);
                        e.Apply(SourceMovementCommandKind.FullBody, "t_crzdiv_anim");
                    }
                }
                Push(e);
            }
            else
            {
                if ((e.State.NewButtons & SourceDroneButtons.B) != 0 && (e.State.Buttons & SourceDroneButtons.C) == 0 &&
                    e.Apply(SourceMovementCommandKind.ChangeWeaponPlayer) != 0)
                    return new SourceEvasionResult(e.Finish(), teamSpinCount, teamHurdleCount, true);
                if ((e.State.NewButtons & SourceDroneButtons.A) != 0)
                {
                    if (context.Carrier == -1)
                    {
                        if (Loose(context) && context.BallDistance / 7.8f <= 8f && context.Phase != SourceMovementPhase.PlayOver)
                            e.Apply(SourceMovementCommandKind.FullBody, context.BallY < 20f ? "c_dive_for_ball_anim"
                                : e.Pick("intercept_anim_tbl", "player.c:_mode_wpn:loose-intercept"));
                        else if (context.BallType == SourceBallType.Pass && context.IntendedReceiver == e.State.PlayerId)
                            e.Apply(SourceMovementCommandKind.FullBody, "c_jmpint_anim");
                    }
                    else e.Apply(SourceMovementCommandKind.FullBody, "t_crzdiv_anim");
                }
                Push(e);
            }
            if ((e.State.NewButtons & SourceDroneButtons.D) != 0 && !e.State.IsHuman)
            {
                if (string.IsNullOrWhiteSpace(context.DroneScript))
                    throw new InvalidOperationException("Source drone D-button animation requires the actual drone_script identity.");
                if (context.DroneScriptFullBody) e.Apply(SourceMovementCommandKind.FullBody, context.DroneScript);
                else { e.BeginTwoPart(); e.Apply(SourceMovementCommandKind.UpperBody, context.DroneScript); }
            }
            return new SourceEvasionResult(e.Finish(), teamSpinCount, teamHurdleCount);
        }

        public static SourceEvasionResult QuarterbackAfterRun(SourceMovementState state, SourceMovementContext context,
            ISourceMovementBoundary boundary, ISourceRandom random, SourceRandomContext randomContext,
            bool enteredControlBlock)
        {
            var e = new SourceMovementExecution(state, context, null, boundary, random, randomContext);
            int teamSpinCount = state.TeamSpinCount, teamHurdleCount = state.TeamHurdleCount;
            if (!context.Tick.Execute || !enteredControlBlock) return new SourceEvasionResult(e.Finish(), teamSpinCount, teamHurdleCount);
            if ((state.NonTurboTime == -1 || (state.NewButtons & SourceDroneButtons.D) != 0) &&
                (e.State.UpperMode & SourceMovementModes.Uninterruptible) == 0)
            {
                teamSpinCount++;
                e.State.TeamSpinCount = teamSpinCount;
                e.Apply(SourceMovementCommandKind.FullBody, "spin_anim");
                if (context.Carrier == e.State.PlayerId) e.Apply(SourceMovementCommandKind.SpinSpeech);
            }
            if ((e.State.NewButtons & SourceDroneButtons.A) != 0 && (e.State.UpperMode & SourceMovementModes.Uninterruptible) == 0)
            {
                Face(ref e.State, context, false);
                bool turbo = (e.State.Buttons & SourceDroneButtons.C) != 0 && context.TeamIndexedTurbo > 2f;
                if (turbo) { teamHurdleCount++; e.State.TeamHurdleCount = teamHurdleCount; }
                e.Apply(SourceMovementCommandKind.FullBody, turbo ? "turbo_hurdle_anim" : "hurdle_anim");
            }
            return new SourceEvasionResult(e.Finish(), teamSpinCount, teamHurdleCount);
        }

        // The A-button portion of _mode_db. B-button switching/push and full late-hit consequences
        // are separate contact/controller stages; chosen late-hit script identity is still exact.
        public static SourceMovementResult DefensiveBackJump(SourceMovementState state, SourceMovementContext context,
            SourceMovementData data, ISourceMovementBoundary boundary, ISourceRandom random,
            SourceRandomContext randomContext, bool enteredControlBlock)
        {
            var e = new SourceMovementExecution(state, context, data, boundary, random, randomContext);
            if (!context.Tick.Execute || !enteredControlBlock || (state.NewButtons & SourceDroneButtons.A) == 0 ||
                (state.UpperMode & SourceMovementModes.RunningPush) != 0) return e.Finish();
            if (context.Carrier == -1 && context.Phase != SourceMovementPhase.PlayOver)
            {
                if ((e.State.Buttons & SourceDroneButtons.C) != 0)
                {
                    if (Loose(context)) e.Apply(SourceMovementCommandKind.FullBody, "c_dive_for_ball_anim");
                    else
                    {
                        Face(ref e.State, context, true);
                        e.Apply(SourceMovementCommandKind.FullBody, "t_crzdiv_anim");
                    }
                }
                else if (Loose(context)) e.Apply(SourceMovementCommandKind.FullBody, "c_dive_for_ball_anim");
                else if (context.BallTicksLeft > 10) Intercept(e);
            }
            else
            {
                if (context.Carrier == -1)
                    throw new InvalidOperationException("Original _mode_db indexes player_blocks[-1] for A on a loose dead ball; caller must not invent a carrier.");
                if (context.CarrierLateHittable && context.CarrierDistance / 7.8f < 5f)
                    e.Apply(SourceMovementCommandKind.FullBody,
                        e.Pick(context.ViolencePowerup ? "violent_db_late_hits_tbl" : "db_late_hits_tbl", "player.c:_mode_db:late-hit"));
                else
                {
                    Face(ref e.State, context, false, true);
                    e.Apply(SourceMovementCommandKind.FullBody, "t_crzdiv_anim");
                }
            }
            return e.Finish();
        }

        private static void Intercept(SourceMovementExecution e)
        {
            e.Apply(SourceMovementCommandKind.FullBody, !e.State.IsHuman ? "c_jmpint3_anim"
                : e.Pick("intercept_anim_tbl", "player.c:mode:intercept"));
        }

        private static void Push(SourceMovementExecution e)
        {
            if ((e.State.NewButtons & SourceDroneButtons.B) == 0 || (e.State.Buttons & SourceDroneButtons.C) == 0) return;
            if ((e.State.UpperMode & SourceMovementModes.RunningPush) == 0 && SourceMovement.Speed(e.State) > 1.10f)
                e.State.UpperMode |= SourceMovementModes.RunningPush;
            else if ((e.State.LowerMode & SourceMovementModes.Uninterruptible) == 0)
                e.Apply(SourceMovementCommandKind.FullBody, e.Pick("push_anim_tbl", "player.c:_mode_wpn:push"));
        }

        private static bool Loose(SourceMovementContext context)
        {
            return context.BallType == SourceBallType.Bobble || context.BallType == SourceBallType.Fumble;
        }

        private static void Face(ref SourceMovementState state, SourceMovementContext context, bool addCameraToNeutral,
            bool preserveNeutral = false)
        {
            int face = state.Stick == 24 && (addCameraToNeutral || preserveNeutral)
                ? (state.Heading + (addCameraToNeutral ? context.CameraHeading : 0)) & 1023
                : SourceMovement.FaceDirection(state.Stick, context);
            state.Heading = state.TargetHeading = face;
        }
    }

    public sealed class SourceEvasionResult
    {
        public SourceMovementResult Movement { get; }
        public int TeamSpinCount { get; }
        public int TeamHurdleCount { get; }
        public bool ReturnFromMode { get; }
        internal SourceEvasionResult(SourceMovementResult movement, int teamSpinCount, int teamHurdleCount,
            bool returnFromMode = false)
        {
            Movement = movement; TeamSpinCount = teamSpinCount; TeamHurdleCount = teamHurdleCount;
            ReturnFromMode = returnFromMode;
        }
    }
}
