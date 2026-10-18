using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;

internal static partial class Program
{
    private static readonly List<string> physicsCases = new List<string>();

    private static T Bind<T>(GameSimulation game, string name)
    {
        return (T)(object)typeof(GameSimulation).GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            typeof(T).GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray(), null)
            .CreateDelegate(typeof(T), game);
    }

    private static SourceHandicapSettings HistoricalC1CacheHandicapSettings()
    {
        // handicap.c random_drones selects TEAM_BEARS; the source474 golden predates dynamic rows.
        return new SourceHandicapSettings(randomDrones: true);
    }

    private static GameSimulation NormalFixture(int selection)
    {
        var random = new Random(800 + selection);
        GameSimulation game = Create(random: seed => new SourceInjectedRandom(site =>
            site.SourceIdentity.Contains("hurdle") ? selection
            : site.SourceIdentity == "player.c:maybe_spear_head" ? 0
            : site.SourceIdentity == "player.c:standard_run:taunt" ? 0 : random.Next(int.MaxValue),
            new SourceRandomProvenance("M1/controlled-actual-VM", "Controlled source-case inputs, NOT platform PRNG",
                "selector=" + selection, SourceRandomEvidence.ExternalUnverified)));
        game.Model.Phase = GamePhase.LiveBall;
        game.Model.SourceRules = new SourceRulesState(0, 0, 1, 30, 100, SourcePlayType.Normal);
        game.Model.Possession = TeamId.Home;
        game.Model.SourceOffensePlay = playbook.GetPlay("play_spider");
        game.Model.SourceDefensePlay = playbook.GetPlay("play_safe_cover");
        game.Model.SourceFlags = SourceGameFlags.None;
        game.Model.CarrierId = 4;
        game.Model.BallMode = BallMode.Held;
        foreach (ActorState actor in game.Model.Players) actor.SourceControllerStation = -1;
        ActorState carrier = game.Model.Players[4];
        carrier.SourceControllerStation = 0; carrier.SourceMode = SourceActorMode.Receiver; carrier.Role = ActorRole.Receiver;
        carrier.SourceStick = carrier.SourcePreviousStick = 24;
        game.Model.ControlledPlayerId = 4;
        game.Model.SourceStationPlayers[0] = 4;
        SourcePlayerDriver.SetSourcePosition(carrier, new SourceVector3(-140f, 0f, 0f));
        game.Model.SourceBallPosition = new SourceVector3(-140f, 14f, 0f);
        game.Model.SourceBallTarget = game.Model.SourceBallPosition;
        carrier.AnimationState.FullBody("run_wb_anim", carrier, 0,
            Bind<Func<int, int>>(game, "SourceDraw"), AnimationSelectionEvidence.SourceVerified);
        SetFixtureCamera(game, 0);
        Bind<Action>(game, "CacheSourceGeometry")();
        Bind<Action>(game, "CacheSourcePlayerCameraJoints")();
        return game;
    }

    // Named internal caller setup with a moving, nonidentity source camera; not a renderer/world-joint substitute.
    private static void SetFixtureCamera(GameSimulation game, long tick)
    {
        var focus = new SourceVector3(-90f + tick * .1f, 8f, 0f);
        var eye = new SourceVector3(focus.X - 120f, 85f, -40f);
        float theta = (float)Math.Atan2(focus.X - eye.X, focus.Z - eye.Z);
        SourceCinematicTransform frame = SourceCinematicTransform.FromState(
            new SourceCinematicSeed(eye, focus, 150f, theta, -.4f));
        game.Model.SourceMovementCamera = new SourceMovementCameraFrame(frame.Position, frame.Transform, 512f, 384f);
        game.Model.SourceMovementCameraTick = tick;
        game.Model.SourceCameraHeading = (int)(theta * 1024f / SourceCinematicMath.TwoPi) & 1023;
    }

    private static void ControlledCallers()
    {
        var programs = new List<object>();
        for (int turbo = 0; turbo < 2; turbo++)
        for (int selected = 0; selected < 8; selected++)
        {
            GameSimulation game = NormalFixture(selected);
            ActorState actor = game.Model.Players[4];
            game.Model.SourceTick = 1;
            SetFixtureCamera(game, 1);
            actor.SourceButtons = SourceDroneButtons.A | (turbo != 0 ? SourceDroneButtons.C : SourceDroneButtons.None);
            actor.SourceNewButtons = SourceDroneButtons.A;
            actor.Turbo = turbo != 0;
            game.Model.SourceCurrentInput = Raw((SourceButtons)(int)actor.SourceButtons, SourceButtons.A);
            Bind<Action<ActorState, PlayerCommand>>(game, "ExecuteSourceMovementMode")(actor, game.Model.SourceCurrentInput);
            string expected = movement.Movement.Script(turbo != 0 ? "turbo_hurdle_tbl" : "hurdle_tbl", selected);
            Check(actor.AnimationState.Primary.ScriptSymbol == expected,
                "actual Core selector/boundary uses exact original hurdle table index " + expected);
            Check(game.Model.SourceHurdleCount[0] == 1, "shared hurdle count committed before actual script " + expected);
            Check(actor.SourceCapturedControlBlock, "actual pre-run control eligibility captured " + expected);
            float top = 0f;
            int callbacksAtStart = actor.SourceMaybeDiveCallbacks;
            long last = 1;
            for (int tick = 1; tick < 160; tick++)
            {
                game.Model.SourceTick = tick;
                SetFixtureCamera(game, tick);
                if (tick > 1)
                    Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", actor.Id, actor.Id + 1);
                actor.AnimationState.Advance(actor, tick, Bind<Func<int, int>>(game, "SourceDraw"));
                RecordPhysics(game, actor);
                top = Math.Max(top, actor.SourceHeight);
                Bind<Action>(game, "CacheSourcePlayerCameraJoints")();
                geometry.WorldJoint(GameSnapshot.SourcePlayerView(actor, tick), 4);
                last = tick;
                if (actor.SourceImmediateGeneralRuns > 0) break;
            }
            Check(actor.SourceMaybeDiveCallbacks - callbacksAtStart == 7, "only seven actual source callback windows " + expected);
            Check(top > 0f, "real selected animation/script launches source object Y " + expected);
            Check(actor.SourceImmediateGeneralRuns == 1, "actual script completes through one immediate GeneralRun " + expected);
            Check(actor.AnimationState.Primary.ScriptSymbol != expected, "real original transition/root state changes, not a fixed clip " + expected);
            programs.Add(new { script = expected, last, top, maybeDiveCallbacks = actor.SourceMaybeDiveCallbacks,
                game.Model.SourceSpinCount, game.Model.SourceHurdleCount });
        }
        SpinCountBeforeCallback();
        QuarterbackLayers();
        CapturedEligibility();
        WeaponReturn();
        ActualLeap();
        if (!mutationRun) File.WriteAllLines(Path.Combine(outputRoot, "actual-caller-physics.tsv"), physicsCases);
        Save("controlled-script-proof.json", JsonSerializer.Serialize(programs));
    }

    private static void RecordPhysics(GameSimulation game, ActorState actor)
    {
        SourceMovementState before = Bind<Func<ActorState, SourceMovementState>>(game, "CaptureSourceMovement")(actor);
        Bind<Action<ActorState>>(game, "IntegrateSourceMovementPlayer")(actor);
        SourceMovementState after = Bind<Func<ActorState, SourceMovementState>>(game, "CaptureSourceMovement")(actor);
        if ((before.LowerMode & SourceMovementModes.Puppet) == 0)
        {
            float y = before.Y + (before.VelocityY + -.08f / 2f);
            float velocity = before.VelocityY + -.08f;
            if (y <= 0f) y = velocity = 0f;
            Check(B(after.Y) == B(y) && B(after.VelocityY) == B(velocity),
                "actual post-VM Core physics applies original source-unit Y/gravity once, without yards conversion");
        }
        string[] input = new[] { B(before.X), B(before.Y), B(before.Z), B(before.VelocityX), B(before.VelocityY),
            B(before.VelocityZ), B(before.Friction), ((uint)before.LowerMode).ToString(), ((uint)before.Flags).ToString(),
            ((int)before.Mode).ToString(), before.LowerSequence == "SPIN" ? "1" : "0",
            game.Model.Phase == GamePhase.LiveBall ? "1" : "0", "0", B(movement.Friction[0]),
            B(after.X), B(after.Y), B(after.Z), B(after.VelocityX), B(after.VelocityY), B(after.VelocityZ),
            B(after.Friction), ((uint)after.Flags).ToString() };
        physicsCases.Add(string.Join("\t", input));
    }

    private static string B(float value)
    {
        return unchecked((uint)BitConverter.SingleToInt32Bits(value)).ToString();
    }

    private static void SpinCountBeforeCallback()
    {
        GameSimulation game = NormalFixture(0);
        ActorState actor = game.Model.Players[4];
        game.Model.SourceTick = 1;
        game.Model.SourceSpinCount[0] = 2;
        actor.NonTurboTicks = -1;
        actor.SourceButtons = SourceDroneButtons.C;
        actor.SourceNewButtons = SourceDroneButtons.None;
        game.Model.SourceCurrentInput = Raw(SourceButtons.C);
        Bind<Action<ActorState, PlayerCommand>>(game, "ExecuteSourceMovementMode")(actor, game.Model.SourceCurrentInput);
        Check(game.Model.SourceSpinCount[0] == 3 && actor.SourceLastSpinPredicateCount == 3,
            "actual third-spin script predicate sees shared count increment BEFORE immediate VM callback");
        Check(actor.AnimationState.Primary.ScriptSymbol == "spin_anim", "actual spin not a first-header boundary stub");
    }

    private static void QuarterbackLayers()
    {
        GameSimulation game = NormalFixture(0);
        ActorState actor = game.Model.Players[3];
        game.Model.Players[4].SourceControllerStation = -1;
        actor.SourceControllerStation = 0;
        game.Model.SourceStationPlayers[0] = game.Model.ControlledPlayerId = 3;
        game.Model.CarrierId = 3;
        actor.SourceMode = SourceActorMode.Quarterback; actor.Role = ActorRole.Quarterback;
        SourcePlayerDriver.SetSourcePosition(actor, new SourceVector3(-170f, 0f, 0f));
        actor.Facing = actor.TargetFacing = actor.UpperFacing = actor.TargetUpperFacing = 128;
        actor.AnimationState.FullBody("qb_stance_anim", actor, 0, Bind<Func<int, int>>(game, "SourceDraw"),
            AnimationSelectionEvidence.SourceVerified);
        game.Model.SourceTick = 1;
        SetFixtureCamera(game, 1);
        actor.SourceStick = playbook.Directions.DirectionToStick(656 - game.Model.SourceCameraHeading);
        actor.SourcePreviousStick = 24;
        actor.SourceButtons = SourceDroneButtons.C; actor.SourceNewButtons = SourceDroneButtons.None;
        actor.Turbo = true;
        game.Model.SourceCurrentInput = Raw(SourceButtons.C);
        Bind<Action<ActorState, PlayerCommand>>(game, "ExecuteSourceMovementMode")(actor, game.Model.SourceCurrentInput);
        Check(actor.AnimationState.TwoPart && (actor.AnimationState.Secondary.Mode & 0x4u) != 0,
            "real Core begin_twopart_mode copies actual pose/PC before independent QB layers");
        Check(actor.AnimationState.Primary.VariableCount == 2 && actor.AnimationState.Secondary.VariableCount == 4
            && actor.AnimationState.Primary.VariableFrame == -1 && actor.AnimationState.Secondary.VariableFrame == -1,
            "actual source QB lower2/upper4 proportional-frame transition variables reach real VM");
        Check(actor.AnimationState.Primary.SequenceName == "QBRL45_T"
            && actor.AnimationState.Secondary.SequenceName == "QBRUNB_T",
            "actual original backward-left QB sector selects distinct lower/upper sequences");
        Check(actor.SourceUpperTurnHack == -128, "actual previous upper facing supplies source turn-hack value");
        geometry.WorldJoint(GameSnapshot.SourcePlayerView(actor, game.SourceTick), 7);
    }

    private static void CapturedEligibility()
    {
        GameSimulation game = NormalFixture(0);
        ActorState actor = game.Model.Players[4];
        game.Model.SourceTick = 1;
        actor.SourceStick = 3;
        actor.SourceButtons = SourceDroneButtons.A | SourceDroneButtons.C;
        actor.SourceNewButtons = SourceDroneButtons.A;
        actor.Turbo = true;
        actor.AnimationState.Secondary.AddMode(0x400u);
        game.Model.SourceCurrentInput = Raw(SourceButtons.A | SourceButtons.C, SourceButtons.A, z: 3);
        Bind<Action<ActorState, PlayerCommand>>(game, "ExecuteSourceMovementMode")(actor, game.Model.SourceCurrentInput);
        Check(actor.SourceCapturedControlBlock && actor.AnimationState.Primary.ScriptSymbol == "turbo_hurdle_anim",
            "captured eligibility survives real StandardRun->BeginTwoPart->UNINT stiff-arm->hurdle ordering");
        Check(game.Model.SourceHurdleCount[0] == 1, "later actual control block was not skipped after real callback changed modes");
        Bind<Action>(game, "Publish")();
        Check(game.PresentationCues.Cues.OfType<SourceCallbackCue>().Any(cue =>
            cue.FunctionSymbol == "stiff_arm_speech" && cue.Context.SourcePath == "player.c"
            && cue.Context.ExecutingSymbol == "standard_run"),
            "actual direct movement speech cue preserves source function context, never a fake VM sound site");
    }

    private static void WeaponReturn()
    {
        GameSimulation game = NormalFixture(0);
        ActorState actor = game.Model.Players[4], target = game.Model.Players[6];
        game.Model.SourceTick = 1; game.Model.CarrierId = 6;
        target.SourceMode = SourceActorMode.Receiver; target.SourceControllerStation = -1;
        target.AnimationState.ClearUninterruptible();
        SourcePlayerDriver.SetSourcePosition(target, new SourceVector3(-130f, 0f, 0f));
        actor.SourceButtons = actor.SourceNewButtons = SourceDroneButtons.B;
        game.Model.SourceCurrentInput = Raw(SourceButtons.B, SourceButtons.B);
        Bind<Action<ActorState, PlayerCommand>>(game, "ExecuteSourceMovementMode")(actor, game.Model.SourceCurrentInput);
        Check(actor.SourceReturnedFromMovementMode && game.Model.SourceStationPlayers[0] == target.Id,
            "actual weapon control boundary transfer produces ReturnFromMode early return");
        Check(actor.SourceControllerStation == -1 && target.SourceControllerStation == 0
            && (target.SourceButtons & SourceDroneButtons.B) != 0,
            "actual station identities and new actor's original fake B are repaired immediately");
        actor.AnimationState.Advance(actor, 1, Bind<Func<int, int>>(game, "SourceDraw"));
        RecordPhysics(game, actor);
        Check(actor.AnimationState.Primary.SourceTick == 1, "ReturnFromMode still permits outer animation/physics iteration");
    }

    private static void ActualLeap()
    {
        GameSimulation game = NormalFixture(0);
        ActorState victim = game.Model.Players[4], attacker = game.Model.Players[10];
        victim.SourceControllerStation = -1;
        attacker.SourceControllerStation = 0; game.Model.SourceStationPlayers[0] = 10; game.Model.ControlledPlayerId = 10;
        attacker.SourceMode = SourceActorMode.DefensiveBack; attacker.Role = ActorRole.Defender;
        attacker.SourceStick = attacker.SourcePreviousStick = 24;
        attacker.Facing = attacker.TargetFacing = 256;
        attacker.SourceButtons = attacker.SourceNewButtons = SourceDroneButtons.A;
        SourcePlayerDriver.SetSourcePosition(attacker, new SourceVector3(-170f, 0f, 0f));
        attacker.AnimationState.FullBody("run_anim", attacker, 0, Bind<Func<int, int>>(game, "SourceDraw"),
            AnimationSelectionEvidence.SourceVerified);
        Bind<Action>(game, "CacheSourceGeometry")();
        SetFixtureCamera(game, 0);
        Bind<Action>(game, "CacheSourcePlayerCameraJoints")();
        game.Model.SourceTick = 1;
        SetFixtureCamera(game, 1);
        game.Model.SourceCurrentInput = Raw(SourceButtons.A, SourceButtons.A);
        int leapAssertions = 0;
        Action<ActorState, SourceAnimationInstruction> execute = attacker.AnimationState.Primary.MovementLeap;
        attacker.AnimationState.Primary.MovementLeap = (actor, instruction) =>
        {
            SourceMovementState before = Bind<Func<ActorState, SourceMovementState>>(game, "CaptureSourceMovement")(actor);
            SourceEvasionLeapResult expected = SourceEvasionLeap.LeapAttack(before,
                new SourceMovementTick(game.SourceTick, true), playbook.Directions,
                new SourceEvasionLeapInput(game.Model.SourceCameraJoints[4, 1],
                    new SourceVector3(victim.SourceVelocity.X, victim.SourceVerticalVelocity, victim.SourceVelocity.Z),
                    game.Model.SourceMovementCamera.Transform, game.Model.SourceMovementCamera.Position,
                    new SourceVector3(0f, -4.4f, 0f), 22, .70f, 2.2f, 1.65f, false));
            execute(actor, instruction);
            SourceMovementState actual = Bind<Func<ActorState, SourceMovementState>>(game, "CaptureSourceMovement")(actor);
            Check(B(actual.VelocityX) == B(expected.State.VelocityX)
                && B(actual.VelocityY) == B(expected.State.VelocityY)
                && B(actual.VelocityZ) == B(expected.State.VelocityZ)
                && actual.TargetHeading == expected.State.TargetHeading,
                "actual Core LEAPATT input is preceding real camera joint/current transform and exact original9 operands");
            leapAssertions++;
        };
        Bind<Action<ActorState, PlayerCommand>>(game, "ExecuteSourceMovementMode")(attacker, game.Model.SourceCurrentInput);
        Check(attacker.AnimationState.Primary.ScriptSymbol == "t_crzdiv_anim",
            "actual DB A/target/spear source branches select real crazy-dive program");
        for (int tick = 1; tick <= 12; tick++)
        {
            game.Model.SourceTick = tick;
            SetFixtureCamera(game, tick);
            Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", attacker.Id, attacker.Id + 1);
            attacker.AnimationState.Advance(attacker, tick, Bind<Func<int, int>>(game, "SourceDraw"));
            RecordPhysics(game, attacker);
            if (attacker.SourceLastLeapBytesConsumed != 0)
            {
                Check(attacker.SourceLastLeapBytesConsumed == 36 && attacker.SourceLastLeapCacheTick == tick - 1,
                    "real ANI_LEAPATT consumes 36-byte operands and preceding source camera-joint generation");
                Check(attacker.SourceLastLeapFlightTicks == 22 && attacker.SourceHeight > 0f,
                    "real dive uses original22-tick attack operand and post-callback source physics");
                break;
            }
            Bind<Action>(game, "CacheSourcePlayerCameraJoints")();
        }
        Check(attacker.SourceLastLeapBytesConsumed == 36, "actual original dive reaches bound camera-cache LEAPATT");
        Check(leapAssertions == 1, "one real VM LEAPATT call, not an unconsumed staged operand/fresh world-joint path");
    }
}
