using System;
using Blitz.Contracts;

namespace Blitz.Gameplay
{
    public sealed partial class GameSimulation
    {
        private void InitializeSource()
        {
            Model.SourceMode = true;
            Model.SourceFirstPossession = 0;
            Model.SourceClock = SourceClock.Create();
            Model.SourceRules = new SourceRulesState(0, 0, 1, 30, 100, SourcePlayType.Kickoff);
            PrepareSourceKickoff(true, false);
            UpdateSourceClockView();
        }

        private void SourceStep(PlayerCommand command)
        {
            switch (Model.Phase)
            {
                case GamePhase.PlaySelect:
                    if (command.Action == InputAction.SelectPlay)
                        SelectSourcePlay(command.Play);
                    break;
                case GamePhase.PreSnap:
                    if (Model.SourceRules.PlayType == SourcePlayType.Kickoff)
                    {
                        if (Model.PreSnapTicksRemaining-- < 1)
                        {
                            Model.PreSnapTicksRemaining = 0;
                            if (Model.SourceKickoffKind == SourceKickoffKind.Quick)
                                BeginSourceKick();
                            else
                                Model.SourceKickApproach = true;
                        }
                    }
                    else
                    {
                        Model.PreSnapTicksRemaining--;
                        bool allowed = Model.PreSnapTicksRemaining
                            < SourceClock.DefaultAutoHikeTicks - SourceClock.SourceTicksPerSecond;
                        // Active _drone_qbps, no-motion branch: test absolute
                        // process count, not a fabricated one-second CPU timer.
                        bool cpuSnap = Model.Possession == TeamId.Away && Model.SourceTick % 30 == 0;
                        bool humanSnap = Model.Possession == TeamId.Home
                            && (command.Action == InputAction.Snap || command.Action == InputAction.Pass);
                        if (Model.PreSnapTicksRemaining < 1 || allowed && (humanSnap || cpuSnap))
                            SnapSource(Model.PreSnapTicksRemaining == 0);
                    }
                    break;
                case GamePhase.LiveBall:
                    Model.LiveTicks++;
                    if (Model.SourceDisallowPassTicks > 0)
                        Model.SourceDisallowPassTicks--;
                    if (Model.SourcePendingControl >= 0 && Model.SourceTick - Model.CatchSourceTick >= 18)
                    {
                        Model.ControlledPlayerId = Model.SourcePendingControl;
                        Model.SourcePendingControl = -1;
                    }
                    if (Model.Possession == TeamId.Away && command.Action == InputAction.SwitchDefender)
                        Model.ControlledPlayerId = PlayerSimulation.NearestHome(Model, Model.ControlledPlayerId);
                    if (Model.Possession == TeamId.Home && command.Action == InputAction.Pass)
                        RequestSourcePass(command.TargetPlayerId, command.Turbo && Model.SourceHumanTurbo > 0f);
                    else if (Model.Possession == TeamId.Away)
                        SourceQuarterbackDecision();
                    break;
                case GamePhase.DeadBall:
                    if (command.Action == InputAction.Continue)
                        ContinueSource();
                    break;
                case GamePhase.GameOver:
                    break;
                default:
                    throw new NotSupportedException("Unported source game phase " + Model.Phase
                        + " at sourceTick=" + Model.SourceTick);
            }

            SourceClockStep clock = SourceClock.Step(Model.SourceClock,
                new SourceClockInput(Model.SourceTick, Model.ClockActive, Model.Quarter - 1));
            Model.SourceClock = clock.State;
            UpdateSourceClockView();
            SourcePlayerDriver.Update(Model, command, SourceDraw, ReleaseSourcePass, TrySourceCatch, SourcePlayerDown);
            if (Model.SourceKickApproach)
            {
                int kicker = Model.SourceKickingSide * SliceRules.PlayersPerTeam;
                if (PlayerSimulation.DistanceSquared(Model.Players[kicker].Position, Model.BallPosition)
                    * SourceTiming.SourceUnitsPerYard * SourceTiming.SourceUnitsPerYard < 64f)
                    BeginSourceKick();
            }
            if (Model.Phase == GamePhase.LiveBall)
            {
                SourceContactDriver.ResolveBodyCylinders(Model);
                AdvanceSourceBall();
                if (Model.BallMode == BallMode.Held)
                {
                    SynchronizeSourceHeldBall();
                    if (!ResolveSourceLocation())
                        SourceContacts(command);
                }
            }
            else if (Model.BallMode == BallMode.Held)
                SynchronizeSourceHeldBall();
        }

        private void SelectSourcePlay(PlayId play)
        {
            bool homeOffense = Model.Possession == TeamId.Home;
            if (homeOffense ? play != PlayId.Run && play != PlayId.QuickPass
                : play != PlayId.Cover && play != PlayId.Blitz)
                return;
            if (Model.SourceRules.PlayType == SourcePlayType.FieldGoal || Model.SourceRules.PlayType == SourcePlayType.Punt)
                throw new NotSupportedException("Unported source formation/ball driver: play=" + Model.SourceRules.PlayType
                    + " side=" + Model.Possession + " sourceTick=" + Model.SourceTick);
            SourcePlayStart start = SourceMatchFlow.BeginPlay(Model.SourceRules, Model.Quarter - 1,
                Model.SourceClock.PackedTime);
            SetSourceRules(start.State);
            Model.SourceFlags = start.Flags;
            if (Model.SourceRules.PlayType == SourcePlayType.ExtraPoint)
            {
                SourcePatChoice choice = Model.Possession != TeamId.Home ? SourcePatChoice.Unselected
                    : play == PlayId.Run ? SourcePatChoice.FreeKick : SourcePatChoice.TwoPointPlay;
                SourcePatDecision pat = DecideSourcePat(choice);
                if (pat.Disposition == SourcePatDisposition.FreeKickComplete
                    || pat.Disposition == SourcePatDisposition.SkipUnnecessary)
                {
                    Model.HomeScore += pat.Side0Points;
                    Model.AwayScore += pat.Side1Points;
                    SetSourceRules(pat.State);
                    if (pat.CheckEndOfPeriod && AdvanceSourcePeriod())
                        return;
                    if (Model.SourceRules.PlayType == SourcePlayType.Kickoff)
                        PrepareSourceKickoff(false, Model.SourceHalfKickoff);
                    return;
                }
                if (pat.Disposition != SourcePatDisposition.RunTwoPointPlay)
                    throw new InvalidOperationException("Unresolved source PAT disposition " + pat.Disposition);
            }
            Model.HomePlay = play;
            // Menu adaptation is explicit and still P1-incomplete: Run -> ZIG
            // ZAG, QuickPass -> SPIDER LEGS, Cover -> SAFE COVER, Blitz ->
            // SUICIDE BLITZ. No approximate baseline formations/routes are used.
            Model.AwayPlay = homeOffense ? PlayId.Cover : PlayId.QuickPass;
            Model.SourcePendingTackleAttacker = Model.SourcePendingTackleVictim = -1;
            Model.SourcePendingControl = -1;
            SourcePlayerDriver.LineUp(Model, sourceAnimations, SourceDraw);
            Model.PreSnapTicksRemaining = SourceClock.DefaultAutoHikeTicks;
            Model.Phase = GamePhase.PreSnap;
            Emit(GameEventType.PlaySelected, TeamId.Home, -1, 0, Model.BallPosition);
        }

        private void SnapSource(bool automatic)
        {
            Model.Phase = GamePhase.LiveBall;
            Model.PreSnapTicksRemaining = 0;
            Model.PlayNumber++;
            Model.LiveTicks = 0;
            Model.ForwardPassUsed = false;
            Model.SourceDisallowPassTicks = automatic ? 30 : 0;
            Model.PassTicksElapsed = 0;
            Model.SourceBallDeflected = false;
            Model.PassTicksTotal = 0;
            Model.LastOutcome = PlayOutcome.None;
            Model.SnapLineOfScrimmage = Model.LineOfScrimmage;
            Model.CatchSourceTick = Model.SourceTick - 18;
            Model.ClockActive = Model.SourceRules.PlayType != SourcePlayType.ExtraPoint;
            Model.SourceClock = SourceClock.ResetPlayTime(Model.SourceClock);
            SourcePlayerDriver.Snap(Model, SourceDraw);
            SynchronizeSourceHeldBall();
            Emit(GameEventType.Snapped, Model.Possession, Model.CarrierId, 0, Model.BallPosition,
                Model.OffenseOffset + 3, Model.OffenseOffset + 1);
        }

        private void PrepareSourceKickoff(bool first, bool half)
        {
            SourceKickoffPlan plan = SourceMatchFlow.PrepareKickoff(new SourceKickoffInput(
                Model.SourceRules, Model.Quarter - 1, Model.SourceClock.PackedTime, first, half,
                Model.SourceFirstPossession, 0));
            Model.SourceKickingSide = plan.KickingSide;
            Model.SourceKickoffKind = plan.Kind;
            Model.SourceFlags = plan.Flags;
            Model.SourceKickApproach = false;
            Model.SourceFirstKickoff = false;
            Model.SourceHalfKickoff = false;
            Model.SourceBallFrozen = true;
            Model.ClockActive = false;
            Model.SourcePendingControl = -1;
            Model.SourcePendingTackleAttacker = Model.SourcePendingTackleVictim = -1;
            SetSourceRules(plan.State);
            SourcePlayerDriver.LineUp(Model, sourceAnimations, SourceDraw);
            Model.PreSnapTicksRemaining = plan.AutoHikeTicks;
            Model.Phase = GamePhase.PreSnap;
            Model.BallKind = SourceBallKind.Kickoff;
            Model.CarrierId = -1;
            Model.BallMode = BallMode.Dead;
            Model.ThrowerId = Model.SourceKickingSide * SliceRules.PlayersPerTeam;
            Model.TargetPlayerId = (1 - Model.SourceKickingSide) * SliceRules.PlayersPerTeam + 3;
            Model.BallPosition = new FieldVector(Model.LineOfScrimmage, 0f);
            Model.BallHeight = 0f;
            Model.SourceBallVelocity = default(FieldVector);
            Model.SourceBallVerticalVelocity = 0f;
            if (plan.Kind == SourceKickoffKind.Quick)
            {
                // nfl.c 1112-1133, neutral handicap row 5 in handicap.c 350-351.
                int catchYard = -5 + SourceDraw(16);
                float catchX = SourceMatchRules.WorldPosition(catchYard, plan.ReceivingSide);
                float catchZ = SourceDraw((int)(40f * SourceTiming.SourceUnitsPerYard))
                    - 20f * SourceTiming.SourceUnitsPerYard;
                float vx = (catchX + (plan.ReceivingSide == 1 ? -20f : 20f)
                    * SourceTiming.SourceUnitsPerYard) / 150f;
                float vz = catchZ / 150f;
                Model.PassTarget = new FieldVector(SourceMatchRules.AbsoluteYardsFromSourceWorld(catchX),
                    catchZ / SourceTiming.SourceUnitsPerYard);
                Model.Players[Model.TargetPlayerId].Position = Model.PassTarget;
                Model.Players[Model.TargetPlayerId].SnapPosition = Model.PassTarget;
                Model.BallPosition = new FieldVector(
                    SourceMatchRules.AbsoluteYardsFromSourceWorld(catchX - 40f * vx),
                    (catchZ - 40f * vz) / SourceTiming.SourceUnitsPerYard);
                Model.BallHeight = 176f / SourceTiming.SourceUnitsPerYard;
                Model.SourceBallVelocity = new FieldVector(vx, vz);
                Model.SourceBallVerticalVelocity = -2.8f;
            }
            else
            {
                int direction = Model.SourceKickingSide == 0 ? 1 : -1;
                Model.PassTarget = new FieldVector(Model.LineOfScrimmage
                    + direction * 2.83f * 175f / SourceTiming.SourceUnitsPerYard, 0f);
            }
            UpdateSourceBallVelocityView();
        }

        private void BeginSourceKick()
        {
            bool quick = Model.SourceKickoffKind == SourceKickoffKind.Quick;
            int kicker = Model.SourceKickingSide * SliceRules.PlayersPerTeam;
            Model.SourceKickApproach = false;
            Model.SourceBallFrozen = false;
            Model.SourceFlags |= SourceGameFlags.BallKicked;
            Model.BallMode = BallMode.InFlight;
            Model.Phase = GamePhase.LiveBall;
            Model.PlayNumber++;
            Model.LiveTicks = 0;
            Model.PassTicksElapsed = 0;
            Model.ForwardPassUsed = false;
            Model.LastOutcome = PlayOutcome.None;
            Model.SnapLineOfScrimmage = Model.LineOfScrimmage;
            if (!quick)
            {
                int direction = Model.SourceKickingSide == 0 ? 1 : -1;
                Model.SourceBallVelocity = new FieldVector(direction * 2.83f, 0f);
                Model.SourceBallVerticalVelocity = 7f;
                ActorState actor = Model.Players[kicker];
                actor.Action = ActorAction.Kick;
                actor.AnimationState.FullBody("kickball_anim", actor, Model.SourceTick, SourceDraw,
                    AnimationSelectionEvidence.SourceVerified);
            }
            UpdateSourceBallVelocityView();
            Emit(GameEventType.Snapped, (TeamId)Model.SourceKickingSide, kicker, 0, Model.BallPosition,
                kicker, Model.TargetPlayerId);
        }

        private void RequestSourcePass(int targetId, bool turbo = false)
        {
            int quarterbackId = Model.OffenseOffset + 3;
            if (Model.BallMode != BallMode.Held || Model.CarrierId != quarterbackId || Model.ForwardPassUsed)
                return;
            if (Model.SourceRules.PlayType == SourcePlayType.Kickoff || Model.SourceDisallowPassTicks > 0)
                return;
            if (targetId < Model.OffenseOffset + 4 || targetId > Model.OffenseOffset + 6)
                return;
            ActorState quarterback = Model.Players[quarterbackId];
            if (quarterback.Role != ActorRole.Quarterback || quarterback.AnimationState.Busy || quarterback.PendingThrowTarget >= 0
                || Model.OffenseDirection * (quarterback.Position.X - Model.SnapLineOfScrimmage) > 0f)
                return;
            ActorState target = Model.Players[targetId];
            float distance = (float)Math.Sqrt(PlayerSimulation.DistanceSquared(quarterback.Position, target.Position));
            if (distance < 7f && Model.OffenseDirection * (target.Position.X - quarterback.Position.X) < 0f)
                throw new NotSupportedException("Source lateral throw branch unported: actor=" + quarterbackId
                    + " target=" + targetId + " sourceTick=" + Model.SourceTick);
            quarterback.PendingThrowTarget = targetId;
            quarterback.PendingThrowTurbo = turbo;
            quarterback.Action = ActorAction.Throw;
            int targetHeading = SourcePlayerDriver.Heading(PlayerSimulation.Subtract(target.Position, quarterback.Position));
            int bearing = Math.Abs(((targetHeading - quarterback.Facing + 512) & 1023) - 512);
            if (distance < 15f && Model.OffenseDirection * (target.Position.X - quarterback.Position.X) >= 0f
                && bearing <= 320)
            {
                quarterback.TargetUpperFacing = targetHeading;
                quarterback.AnimationState.UpperBody(quarterback.Flipped ? "qb_rthrow_sl_anim" : "qb_rthrow_sr_anim",
                    quarterback, Model.SourceTick, SourceDraw, AnimationSelectionEvidence.SourceVerified);
                return;
            }
            quarterback.TargetFacing = targetHeading;
            string script = distance >= 35f ? quarterback.Flipped ? "qb_throw_ll_anim" : "qb_throw_lr_anim"
                : quarterback.Flipped ? "qb_throw_sl_anim" : "qb_throw_sr_anim";
            quarterback.AnimationState.FullBody(script, quarterback, Model.SourceTick, SourceDraw,
                AnimationSelectionEvidence.SourceVerified);
        }

        private void ReleaseSourcePass(int quarterbackId)
        {
            ActorState quarterback = Model.Players[quarterbackId];
            int targetId = quarterback.PendingThrowTarget;
            if (Model.Phase != GamePhase.LiveBall || Model.CarrierId != quarterbackId || targetId < 0)
                return;
            ActorState receiver = Model.Players[targetId];
            // The source intercept equation/gravity are used; wrist matrices,
            // canned catches and all target correction branches remain B1 work.
            float units = SourceTiming.SourceUnitsPerYard;
            float dx = (receiver.Position.X - quarterback.Position.X) * units;
            float dz = (receiver.Position.Z - quarterback.Position.Z) * units;
            float speed = quarterback.PendingThrowTurbo ? 5.70f : 5.50f;
            float vx = receiver.SourceVelocity.X;
            float vz = receiver.SourceVelocity.Z;
            float a = vx * vx + vz * vz - speed * speed;
            float b = 2f * (vx * dx + vz * dz);
            float c = dx * dx + dz * dz;
            float discriminant = Math.Max(0f, b * b - 4f * a * c);
            float first = (-b + (float)Math.Sqrt(discriminant)) / (2f * a);
            float second = (-b - (float)Math.Sqrt(discriminant)) / (2f * a);
            float ticks = Math.Max(first, second);
            if (!SourcePoseMath.IsFinite(ticks) || ticks <= 0f)
                ticks = 150f;
            float releaseHeight = quarterback.Height * units + quarterback.SourceRoot.Y;
            float vertical = (21f - releaseHeight) / ticks + 0.04f * ticks;
            float maximum = 0.08f * 60f * units / (2f * speed);
            if (vertical > maximum)
            {
                vertical = maximum;
                ticks = 60f * units / speed;
            }
            Model.BallPosition = quarterback.Position;
            Model.BallHeight = releaseHeight / units;
            Model.SourceBallVelocity = new FieldVector(dx / ticks + vx, dz / ticks + vz);
            Model.SourceBallVerticalVelocity = vertical;
            Model.PassTarget = PlayerSimulation.Add(Model.BallPosition,
                PlayerSimulation.Scale(Model.SourceBallVelocity, ticks / units));
            Model.PassTicksTotal = (int)(ticks + 0.5f);
            Model.PassTicksElapsed = 0;
            Model.BallMode = BallMode.InFlight;
            Model.BallKind = SourceBallKind.Pass;
            Model.ThrowerId = quarterbackId;
            Model.LastTouchPlayerId = quarterbackId;
            Model.CarrierId = -1;
            Model.TargetPlayerId = targetId;
            Model.ForwardPassUsed = true;
            Model.SourceFlags |= SourceGameFlags.PassThrown;
            Model.SourceFumbleX = SourceMatchRules.SourceWorldFromAbsoluteYards(quarterback.Position.X);
            UpdateSourceBallVelocityView();
            Emit(GameEventType.PassThrown, quarterback.Team, targetId, 0, Model.BallPosition,
                quarterbackId, targetId);
        }

        private void SourceQuarterbackDecision()
        {
            if (Model.CarrierId != Model.OffenseOffset + 3 || Model.ForwardPassUsed || Model.LiveTicks < 15
                || Model.SourceRules.PlayType == SourcePlayType.Kickoff)
                return;
            // Only the active QB's initial dropback is source-ported. This
            // pressure decision is explicitly provisional, not USE_OLD_QB.
            ActorState quarterback = Model.Players[Model.CarrierId];
            if (PlayerSimulation.NearestOpponentDistanceSquared(Model, quarterback)
                * SourceTiming.SourceUnitsPerYard * SourceTiming.SourceUnitsPerYard >= 625f)
                return;
            for (int slot = 4; slot < SliceRules.PlayersPerTeam; slot++)
            {
                int targetId = Model.OffenseOffset + slot;
                if (Model.OffenseDirection * (Model.Players[targetId].Position.X - quarterback.Position.X) > 0f)
                {
                    RequestSourcePass(targetId, quarterback.Turbo);
                    return;
                }
            }
        }

        private void TrySourceCatch(ActorState actor)
        {
            if (Model.Phase != GamePhase.LiveBall || Model.BallMode != BallMode.InFlight || Model.SourceBallFrozen)
                return;
            if ((actor.AnimationState.Primary.Mode & SourceAnimationModes.NoCatch) != 0)
            {
                // Active neutral handicap row is zero, but the original call
                // still consumes randrng(100) before rejecting NOCATCH.
                SourceDraw(100);
                return;
            }
            if (OutsideSourceField(actor.Position))
                return;
            if (actor.Id == Model.ThrowerId && Model.PassTicksElapsed <= 10)
                return;
            if (actor.Team == Model.Possession
                && actor.Id != Model.TargetPlayerId && Model.PassTicksElapsed <= 15)
                return;
            if (actor.NoCatchTicks > 0)
                return;
            if ((Model.BallKind == SourceBallKind.Fumble || Model.BallKind == SourceBallKind.Bobble)
                && Model.PassTicksElapsed <= 30)
                return;
            float units = SourceTiming.SourceUnitsPerYard;
            float dx = (actor.Position.X - Model.BallPosition.X) * units;
            float dz = (actor.Position.Z - Model.BallPosition.Z) * units;
            float dy = actor.Height * units + actor.SourceRoot.Y - Model.BallHeight * units;
            // player.c standard_catch: strict pelvis <14. Wrist <5 is not
            // replaced with a fabricated larger sphere.
            if (dx * dx + dy * dy + dz * dz >= 14f * 14f)
                return;
            if (Model.BallKind == SourceBallKind.Pass || Model.BallKind == SourceBallKind.Bobble)
            {
                bool offense = actor.Team == Model.Possession;
                int lead = Model.Possession == TeamId.Home ? Model.HomeScore - Model.AwayScore
                    : Model.AwayScore - Model.HomeScore;
                int field = SourceMatchRules.FieldPosition(
                    SourceMatchRules.SourceWorldFromAbsoluteYards(actor.Position.X), (int)Model.Possession);
                // player.c 3329-3341, handicap.c of_bobble_ht neutral row 5.
                if ((actor.Team == TeamId.Away || lead > 3 && field > Model.SourceRules.LineOfScrimmage)
                    && SourceDraw(100) < 2)
                    throw new NotSupportedException("Unported source bobble outcome: actor=" + actor.Id
                        + " sourceTick=" + Model.SourceTick);
                if ((actor.AnimationState.Primary.Mode & SourceAnimationModes.Reacting) != 0)
                    throw new NotSupportedException("Unported source reacting-catch distribution: actor=" + actor.Id
                        + " sourceTick=" + Model.SourceTick);
                // player.c 3376-3384: early defenders cannot intercept. They
                // deflect 90% or miss 10%, rather than becoming fake carriers.
                if (!offense && Model.PassTicksElapsed <= 30)
                {
                    actor.NoCatchTicks = 20;
                    if (SourceDraw(10) != 0)
                        DeflectSourcePass(actor);
                    return;
                }
                if (!offense && Model.SourceRules.Down == 4 && field >= Model.SourceRules.LineOfScrimmage + 5)
                    throw new NotSupportedException("Unported source fourth-down batdown: actor=" + actor.Id
                        + " sourceTick=" + Model.SourceTick);
                if (!offense && SourceDraw(100) < 20)
                    throw new NotSupportedException("Unported source batdown outcome: actor=" + actor.Id
                        + " sourceTick=" + Model.SourceTick);
                if (!offense && actor.Height == 0f && (Model.SourceFlags & SourceGameFlags.Bobble) == 0
                    && SourceDraw(100) < 20)
                    throw new NotSupportedException("Unported source defensive bobble: actor=" + actor.Id
                        + " sourceTick=" + Model.SourceTick);
            }
            int thrower = Model.ThrowerId;
            TeamId before = Model.Possession;
            Model.BallMode = BallMode.Held;
            Model.CarrierId = actor.Id;
            Model.LastTouchPlayerId = actor.Id;
            Model.TargetPlayerId = -1;
            Model.CatchSourceTick = Model.SourceTick;
            Model.SourceHumanTurbo = Math.Max(50f, Model.SourceHumanTurbo);
            actor.NonTurboTicks = 500;
            if (Model.BallKind == SourceBallKind.Kickoff)
            {
                Model.SourceClock = SourceClock.ResetPlayTime(Model.SourceClock);
                if ((Model.SourceFlags & SourceGameFlags.QuickKick) != 0)
                    Model.SourceFlags |= SourceGameFlags.PossessionChange;
            }
            if (actor.Team != Model.Possession)
            {
                Model.SourceHumanTurbo = 100f;
                Model.Possession = actor.Team;
                Model.SourceRules = new SourceRulesState((int)actor.Team, Model.SourceRules.InitialOffenseSide,
                    Model.SourceRules.Down, Model.SourceRules.LineOfScrimmage, Model.SourceRules.FirstDown,
                    Model.SourceRules.PlayType, Model.SourceRules.NoFirstDown);
                Model.SourceFlags |= SourceGameFlags.PossessionChange;
            }
            Model.ClockActive = Model.SourceRules.PlayType != SourcePlayType.ExtraPoint;
            Model.SourcePendingControl = actor.Team == TeamId.Home ? actor.Id : PlayerSimulation.NearestHome(Model, -1);
            SourcePlayerDriver.StartCatch(Model, actor, SourceDraw);
            SynchronizeSourceHeldBall();
            Emit(GameEventType.PassCaught, actor.Team, actor.Id, 0, Model.BallPosition, actor.Id, thrower);
            if (before != Model.Possession)
                Emit(GameEventType.PossessionChanged, Model.Possession, actor.Id, 0, Model.BallPosition,
                    actor.Id, thrower);
        }

        private void DeflectSourcePass(ActorState actor)
        {
            // pmisc.c 304-368, blob 50426d40ecd03515cecd295fb80a63ffa0cb0683.
            float vx = Model.SourceBallVelocity.X;
            float vy = Model.SourceBallVerticalVelocity;
            float vz = Model.SourceBallVelocity.Z;
            float magnitude = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (magnitude < 0.001f)
                return;
            vx /= magnitude;
            vy /= magnitude;
            vz /= magnitude;
            magnitude *= (65 + SourceDraw(31)) / 100f;
            int transfer = SourceDraw(4) != 0 ? 10 + SourceDraw(41) : 50 + SourceDraw(41);
            float loss = transfer / 100f;
            // Preserve the original v[0]+v[1] expression, not a corrected
            // horizontal-magnitude substitute.
            vy += loss * (vx + vy);
            vx *= 1f - loss;
            vz *= 1f - loss;
            int rotation = SourceDraw(257) - SourceDraw(257);
            Model.SourceBallVelocity = SourceAnimationLayerState.Rotate(vx * magnitude, vz * magnitude, rotation);
            Model.SourceBallVerticalVelocity = Math.Min(vy * magnitude, 2.7f);
            Model.SourceBallDeflected = true;
            Model.LastTouchPlayerId = actor.Id;
            float height = Model.BallHeight * SourceTiming.SourceUnitsPerYard;
            float vertical = Model.SourceBallVerticalVelocity;
            float ground = height < 0.001f ? 0f
                : (vertical + (float)Math.Sqrt(vertical * vertical + 0.16f * height)) / 0.08f;
            Model.PassTarget = PlayerSimulation.Add(Model.BallPosition,
                PlayerSimulation.Scale(Model.SourceBallVelocity, ground / SourceTiming.SourceUnitsPerYard));
            UpdateSourceBallVelocityView();
        }

        private void AdvanceSourceBall()
        {
            if (Model.BallMode != BallMode.InFlight || Model.SourceBallFrozen)
                return;
            const float units = SourceTiming.SourceUnitsPerYard;
            Model.BallPosition = PlayerSimulation.Add(Model.BallPosition,
                PlayerSimulation.Scale(Model.SourceBallVelocity, 1f / units));
            Model.BallHeight += (Model.SourceBallVerticalVelocity - 0.04f) / units;
            Model.SourceBallVerticalVelocity -= 0.08f;
            Model.PassTicksElapsed++;
            if (Model.BallKind == SourceBallKind.Pass
                && Model.OffenseDirection * (Model.BallPosition.X - Model.SnapLineOfScrimmage) >= 0f)
                Model.SourceFlags |= SourceGameFlags.ForwardPass;
            UpdateSourceBallVelocityView();
            if (OutsideSourceField(Model.BallPosition))
                EndSourcePlay(SourcePlayEndCause.BallOutOfBounds, -1, Model.BallPosition);
            else if (Model.BallHeight <= 0f)
            {
                Model.BallHeight = 0f;
                if (Model.BallKind == SourceBallKind.Pass)
                    EndSourcePlay(SourcePlayEndCause.DeadBallInBounds, Model.TargetPlayerId, Model.BallPosition);
                else
                    throw new NotSupportedException("Unported source loose-ball bounce: kind=" + Model.BallKind
                        + " actor=" + Model.LastTouchPlayerId + " sourceTick=" + Model.SourceTick
                        + " position=" + Model.BallPosition.X + "," + Model.BallPosition.Z);
            }
        }

        private void SynchronizeSourceHeldBall()
        {
            if (Model.CarrierId < 0)
                throw new InvalidOperationException("Source held ball has no carrier at sourceTick=" + Model.SourceTick);
            ActorState actor = Model.Players[Model.CarrierId];
            Model.BallPosition = actor.Position;
            Model.BallHeight = actor.Height + actor.SourceRoot.Y / SourceTiming.SourceUnitsPerYard;
            Model.BallVelocity = actor.Velocity;
            Model.BallVerticalVelocity = actor.SourceVerticalVelocity
                * SourceTiming.DefaultTicksPerSecond / SourceTiming.SourceUnitsPerYard;
        }

        private bool ResolveSourceLocation()
        {
            if (OutsideSourceField(Model.BallPosition))
            {
                EndSourcePlay(SourcePlayEndCause.CarrierOutOfBounds, Model.CarrierId, Model.BallPosition);
                return true;
            }
            // td_check/over_def_ez: one source yard inside the end zone, with
            // eighteen ticks of control. Do not reuse the legacy goal-line test.
            float directed = Model.OffenseDirection
                * SourceMatchRules.SourceWorldFromAbsoluteYards(Model.BallPosition.X);
            if (directed >= 390f + SourceTiming.SourceUnitsPerYard && directed <= 468f
                && Model.SourceTick - Model.CatchSourceTick >= 18)
            {
                EndSourcePlay(SourcePlayEndCause.CarrierTouchdown, Model.CarrierId, Model.BallPosition);
                return true;
            }
            return false;
        }

        private void SourceContacts(PlayerCommand command)
        {
            if (Model.SourcePendingTackleVictim >= 0 || Model.BallMode != BallMode.Held)
                return;
            ActorState victim = Model.Players[Model.CarrierId];
            for (int id = 0; id < Model.Players.Length; id++)
            {
                ActorState actor = Model.Players[id];
                if (actor.Team == victim.Team || actor.Down || actor.AnimationState.Busy)
                    continue;
                float distance = PlayerSimulation.DistanceSquared(actor.Position, victim.Position)
                    * SourceTiming.SourceUnitsPerYard * SourceTiming.SourceUnitsPerYard;
                // detect.c BODY_RADIUS=5, ordinary strict cylinder broad phase.
                // Attack-joint cylinders/evade/contact tables remain C1 work.
                if (distance >= 100f)
                    continue;
                if (id == Model.ControlledPlayerId && command.Action != InputAction.Tackle)
                    continue;
                Model.SourcePendingTackleAttacker = id;
                Model.SourcePendingTackleVictim = victim.Id;
                SourcePlayerDriver.Contact(Model, id, victim.Id, SourceDraw);
                return;
            }
        }

        private void SourcePlayerDown(int playerId)
        {
            if (Model.Phase != GamePhase.LiveBall || Model.SourcePendingTackleVictim != playerId)
                return;
            ActorState actor = Model.Players[playerId];
            float directed = Model.OffenseDirection * SourceMatchRules.SourceWorldFromAbsoluteYards(actor.Position.X);
            EndSourcePlay(directed < -390f ? SourcePlayEndCause.CarrierDownInOwnEndZone
                : SourcePlayEndCause.CarrierTackledInBounds, playerId, actor.Position,
                Model.SourcePendingTackleAttacker);
        }

        private void EndSourcePlay(SourcePlayEndCause cause, int playerId, FieldVector position, int attackerId = -1)
        {
            if (Model.Phase != GamePhase.LiveBall)
                return;
            TeamId offense = Model.Possession;
            SourcePlayResolution resolution = SourceMatchRules.Resolve(new SourcePlayEndInput(
                Model.SourceRules, cause, SourceBallTypeFor(Model.BallKind), Model.SourceFlags,
                (int)Model.Possession, SourceMatchRules.SourceWorldFromAbsoluteYards(position.X),
                position.Z * SourceTiming.SourceUnitsPerYard, Model.SourceFumbleX, Model.SourceLastResult));
            if (!resolution.TransitionApplied)
                throw new NotSupportedException("Invalid source result branch: play=" + Model.SourceRules.PlayType
                    + " cause=" + cause + " kind=" + Model.BallKind + " sourceTick=" + Model.SourceTick);
            Model.SourceLastResult = resolution.Classification.Result;
            SetSourceRules(resolution.State);
            Model.SourceLinePosition = resolution.LinePositionZ / SourceTiming.SourceUnitsPerYard;
            Model.SourceFlags = resolution.Flags;
            Model.HomeScore += resolution.Side0Points;
            Model.AwayScore += resolution.Side1Points;
            Model.LastOutcome = resolution.JustGotFirstDown ? PlayOutcome.FirstDown
                : resolution.TurnoverOnDowns ? PlayOutcome.TurnoverOnDowns
                : resolution.Classification.Result == SourcePlayResult.Touchdown ? PlayOutcome.Touchdown
                : resolution.Classification.Result == SourcePlayResult.Safety
                    || resolution.Classification.Result == SourcePlayResult.PatSafety ? PlayOutcome.Safety
                : resolution.Classification.Result == SourcePlayResult.Incomplete ? PlayOutcome.Incomplete
                : cause == SourcePlayEndCause.CarrierOutOfBounds || cause == SourcePlayEndCause.BallOutOfBounds
                    ? PlayOutcome.OutOfBounds
                : cause == SourcePlayEndCause.CarrierTackledInBounds || cause == SourcePlayEndCause.CarrierDownInOwnEndZone
                    ? PlayOutcome.Tackled : PlayOutcome.None;
            if (attackerId >= 0)
                Emit(GameEventType.Tackled, offense, playerId, 0, position, attackerId, playerId);
            else if (cause == SourcePlayEndCause.CarrierOutOfBounds || cause == SourcePlayEndCause.BallOutOfBounds)
                Emit(GameEventType.OutOfBounds, offense, playerId, 0, position, playerId);
            if (resolution.Classification.Result == SourcePlayResult.Incomplete)
                Emit(GameEventType.Incomplete, offense, playerId, 0, position, Model.ThrowerId, playerId);
            if (resolution.JustGotFirstDown)
                Emit(GameEventType.FirstDown, Model.Possession, playerId, 0, position, playerId);
            if (resolution.TurnoverOnDowns)
                Emit(GameEventType.TurnoverOnDowns, Model.Possession, playerId, 0, position, playerId);
            if (resolution.Classification.Result == SourcePlayResult.Touchdown)
                Emit(GameEventType.Touchdown, offense, playerId, 6, position, playerId);
            if (resolution.Classification.Result == SourcePlayResult.Safety)
                Emit(GameEventType.Safety, GameplayRules.Opponent(offense), playerId, 2, position, playerId);
            if (offense != Model.Possession)
                Emit(GameEventType.PossessionChanged, Model.Possession, playerId, 0, position, playerId);
            Model.Phase = GamePhase.DeadBall;
            Model.ClockActive = false;
            Model.SourceClock = SourceClock.DiscardFraction(Model.SourceClock);
            UpdateSourceClockView();
            Model.BallMode = BallMode.Dead;
            Model.BallPosition = position;
            Model.BallHeight = Math.Max(0f, Model.BallHeight);
            Model.CarrierId = -1;
            Model.TargetPlayerId = -1;
            Model.SourcePendingTackleAttacker = Model.SourcePendingTackleVictim = -1;
            if (Model.SourceClock.PackedTime == 0 && Model.SourceRules.PlayType != SourcePlayType.ExtraPoint
                && !Model.QuarterEndEmitted)
            {
                Model.QuarterEndEmitted = true;
                Emit(GameEventType.QuarterEnded, TeamId.Home, -1, Model.Quarter, default(FieldVector));
            }
        }

        private void ContinueSource()
        {
            if (AdvanceSourcePeriod())
                return;
            if (Model.SourceRules.PlayType == SourcePlayType.Kickoff)
            {
                PrepareSourceKickoff(false, Model.SourceHalfKickoff);
                return;
            }
            Model.Phase = GamePhase.PlaySelect;
            Model.PreSnapTicksRemaining = 0;
            SourcePlayerDriver.LineUp(Model, sourceAnimations, SourceDraw);
        }

        private bool AdvanceSourcePeriod()
        {
            SourcePeriodInput input = new SourcePeriodInput(Model.SourceRules, Model.Quarter - 1,
                Model.SourceClock.PackedTime, Model.HomeScore, Model.AwayScore,
                Model.SourceFirstPossession, Model.SourceOvertimeKickoff);
            SourcePeriodDecision decision = SourceMatchFlow.AdvancePeriod(input);
            if (decision.RandomRequest.IsRequired)
            {
                decision = SourceMatchFlow.AdvancePeriod(new SourcePeriodInput(Model.SourceRules, Model.Quarter - 1,
                    Model.SourceClock.PackedTime, Model.HomeScore, Model.AwayScore,
                    Model.SourceFirstPossession, Model.SourceOvertimeKickoff,
                    overtimeFirstDraw: SourceDraw(decision.RandomRequest.BoundOrModulus)));
            }
            if (decision.MatchEnded)
            {
                Model.Phase = GamePhase.GameOver;
                Model.ClockActive = false;
                Emit(GameEventType.GameEnded, TeamId.Home, -1, Model.HomeScore.CompareTo(Model.AwayScore),
                    default(FieldVector));
                return true;
            }
            if (decision.RestartClockProcess)
            {
                Model.Quarter = decision.QuarterIndex + 1;
                Model.SourceClock = SourceClock.Create(decision.PackedTime, firstWakeTick: Model.SourceTick + 1);
                Model.SourceOvertimeKickoff = decision.OvertimeKickoff;
                Model.SourceHalfKickoff = decision.HalfKickoff;
                Model.QuarterEndEmitted = false;
                SetSourceRules(decision.State);
                UpdateSourceClockView();
                if (decision.State.PlayType == SourcePlayType.Kickoff)
                {
                    PrepareSourceKickoff(false, decision.HalfKickoff);
                    return true;
                }
            }
            return false;
        }

        private SourcePatDecision DecideSourcePat(SourcePatChoice choice)
        {
            int offense = Model.Possession == TeamId.Home ? Model.HomeScore : Model.AwayScore;
            int defense = Model.Possession == TeamId.Home ? Model.AwayScore : Model.HomeScore;
            var input = new SourcePatInput(Model.SourceRules, Model.Quarter - 1, Model.SourceClock.PackedTime,
                offense, defense, Model.Possession == TeamId.Home, choice);
            SourcePatDecision result = SourceMatchFlow.SelectPat(input);
            int? two = null;
            int? free = null;
            for (int i = 0; result.RandomRequest.IsRequired && i < 2; i++)
            {
                int draw = SourceDraw(result.RandomRequest.BoundOrModulus);
                if (result.RandomRequest.Call == SourceRandomCall.GoForTwo) two = draw;
                else if (result.RandomRequest.Call == SourceRandomCall.FreePat) free = draw;
                else throw new NotSupportedException("Unknown source PAT RNG request " + result.RandomRequest.Call);
                result = SourceMatchFlow.SelectPat(new SourcePatInput(Model.SourceRules, Model.Quarter - 1,
                    Model.SourceClock.PackedTime, offense, defense, Model.Possession == TeamId.Home, choice, two, free));
            }
            return result;
        }

        private void SetSourceRules(SourceRulesState rules)
        {
            Model.SourceRules = rules;
            Model.Possession = (TeamId)rules.OffenseSide;
            Model.Down = rules.Down;
            Model.LineOfScrimmage = SourceMatchRules.AbsoluteYardsFromSourceWorld(
                SourceMatchRules.WorldPosition(rules.LineOfScrimmage, rules.OffenseSide));
            Model.FirstDownLine = SourceMatchRules.AbsoluteYardsFromSourceWorld(
                SourceMatchRules.WorldPosition(rules.FirstDown, rules.OffenseSide));
        }

        private void UpdateSourceClockView()
        {
            Model.PackedGameTime = unchecked((uint)Model.SourceClock.PackedTime);
            Model.QuarterTicksRemaining = SourceClock.WholeSeconds(Model.SourceClock.PackedTime) * SliceRules.TicksPerSecond;
        }

        private void UpdateSourceBallVelocityView()
        {
            Model.BallVelocity = PlayerSimulation.Scale(Model.SourceBallVelocity,
                SourceTiming.DefaultTicksPerSecond / SourceTiming.SourceUnitsPerYard);
            Model.BallVerticalVelocity = Model.SourceBallVerticalVelocity
                * SourceTiming.DefaultTicksPerSecond / SourceTiming.SourceUnitsPerYard;
        }

        private int SourceDraw(int maximum)
        {
            if (maximum <= 0)
                return 0;
            // The platform random() body is not yet established. Preserve call
            // bounds/order and expose the draw count internally; this seeded
            // profile is deliberately not represented as RNG/source parity.
            Model.SourceRandomDraws++;
            return NextInt(maximum);
        }

        private static bool OutsideSourceField(FieldVector position)
        {
            return Math.Abs(position.Z * SourceTiming.SourceUnitsPerYard) > 182.85f
                || Math.Abs(SourceMatchRules.SourceWorldFromAbsoluteYards(position.X)) > 468f;
        }

        private static SourceBallType SourceBallTypeFor(SourceBallKind kind)
        {
            switch (kind)
            {
                case SourceBallKind.Unspecified:
                case SourceBallKind.Pass: return SourceBallType.Pass;
                case SourceBallKind.Kickoff: return SourceBallType.Kickoff;
                case SourceBallKind.Fumble: return SourceBallType.Fumble;
                case SourceBallKind.Bobble: return SourceBallType.Bobble;
                case SourceBallKind.FieldGoal: return SourceBallType.FieldGoal;
                case SourceBallKind.Punt: return SourceBallType.Punt;
                case SourceBallKind.Lateral: return SourceBallType.Lateral;
                default: throw new NotSupportedException("Unported source ball type: " + kind);
            }
        }
    }
}
