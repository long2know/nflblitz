using System;
using System.Linq;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;

internal static partial class Program
{
    private static void StreamAndFailures()
    {
        var stream = new InputStream();
        GameSimulation game = Create(stream);
        ActorState actor = game.Model.Players[4];
        var hit = functions.VmBindings.First(b => b.SourceLine == 5544);
        var ground = functions.VmBindings.First(b => b.SourceLine == 5555);
        ActorInputs(game, actor, 0, 3, -1, null);
        game.Model.SourceCurrentPlayerBank = null;
        SourceSoundVmDispatch offscreen = ExecuteProbe(game, stream, hit, actor, 4, new[] { 71, 73 });
        Equal(0, offscreen.Result.RandomCalls, "offscreen get_hit is legitimately resolved with zero draws");
        Equal(0, offscreen.Result.Calls.Count, "zero-call resolution is not unsupported/silence fallback");
        Equal(null, offscreen.StateAtCall.CurrentPlayerBank, "offscreen unused unknown bank stays unknown");
        Equal(SourceAudioHitPresence.Unknown, offscreen.StateAtCall.HitPartner.Presence,
            "offscreen unused unknown partner is not default actual null");

        ActorInputs(game, actor, 0, 7, 0, null);
        SourceSoundVmDispatch human = ExecuteProbe(game, stream, ground, actor, 5, new[] { 83, 89, 97, 101 });
        Equal(2, human.Result.Calls.Count, "ground call plus grunt preserved");
        Equal(functions.Human.Volume, human.Result.Calls[0].Volume,
            "human station short-circuits unknown partner, no onscreen/mode inference");
        Equal(SourceAudioHitPresence.Unknown, human.StateAtCall.HitPartner.Presence,
            "ground human leaves unused partner unknown");

        ActorInputs(game, actor, 2, 7, -1, -1);
        SourceSoundVmDispatch absent = ExecuteProbe(game, stream, ground, actor, 2, new[] { 103, 107, 109 });
        Equal(functions.Drone.Volume, absent.Result.Calls[0].Volume, "known actual null partner selects original drone parameters");
        ActorInputs(game, actor, 2, 7, -1, 8, partnerStation: 0);
        SourceSoundVmDispatch partnerHuman = ExecuteProbe(game, stream, ground, actor, 2, new[] { 113, 127, 131 });
        Equal(functions.Human.Volume, partnerHuman.Result.Calls[0].Volume,
            "actual present human partner selects human parameters for a drone actor");

        MultipleCalls(game, stream, actor, hit);
        CollisionProcess(game, stream);
        PartialFailure();
        UnsupportedTuple();
    }

    private static void MultipleCalls(GameSimulation game, InputStream stream, ActorState actor, Blitz.AssetImport.SourceAudioVmBinding binding)
    {
        ActorInputs(game, actor, 2, 7, 0, null, carrier: actor.Id);
        SourceAnimationProgram saved = Programs[binding.ScriptSymbol];
        Programs[binding.ScriptSymbol] = new SourceAnimationProgram(binding.ScriptSymbol,
            new SourceAnimationInstruction(SourceAnimationOp.SetMode),
            new SourceAnimationInstruction(SourceAnimationOp.NewSequence, text: "BREATH"),
            new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1), Sound(binding), Sound(binding),
            new SourceAnimationInstruction(SourceAnimationOp.SkipFrames, -1, 11),
            new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1), new SourceAnimationInstruction(SourceAnimationOp.End));
        try
        {
            long tick = game.SourceTick + 2;
            Begin(game, actor, tick - 1);
            actor.AnimationState.FullBody(binding.ScriptSymbol, actor, tick - 1, Bind<Func<int, int>>(game, "SourceDraw"),
                AnimationSelectionEvidence.SourceVerified);
            Begin(game, actor, tick);
            stream.Start(137, 139, 149, 151, 157, 163, 167);
            Bind<Func<int, int>>(game, "SourceDraw")(97);
            actor.AnimationState.Advance(actor, tick, Bind<Func<int, int>>(game, "SourceDraw"));
            SourceSoundVmBatch batch = game.TakeSourceSoundVmDispatches();
            Equal(2, batch.Dispatches.Count, "multiple ANI_CODE calls in one real VM iteration");
            Equal(1, batch.Dispatches[0].RandomContext.FirstCallIndex, "first call enters existing process offset");
            Equal(3, batch.Dispatches[1].RandomContext.FirstCallIndex, "second call sees first selection's consumed draws");
            Equal(5, stream.Random.Trace.Last().Site.CallIndex, "following opcode sees both calls before execution");
            Equal(6, game.Model.SourceRandomCallIndex, "no double consumption or accounting reset");
            Check(batch.Dispatches[0].OriginalCueContext.ProcessOrder < batch.Dispatches[1].OriginalCueContext.ProcessOrder,
                "actual original cue execution order retained");
            Equal(0, game.TakeSourceSoundVmDispatches().Dispatches.Count, "multiple-call delivery is once only");
        }
        finally { Programs[binding.ScriptSymbol] = saved; }
    }

    private static void CollisionProcess(GameSimulation game, InputStream stream)
    {
        long tick = game.SourceTick + 2;
        stream.Armed = false;
        foreach (ActorState value in game.Model.Players)
        {
            value.SourceRawFlags |= 0x20;
            value.SourceAttackMode = value.SourceAttackTime = 0;
            value.SourcePuppetLinkId = -1;
        }
        ActorState attacker = game.Model.Players[5], victim = game.Model.Players[9];
        ActorInputs(game, attacker, 2, 9, 0, null, carrier: victim.Id);
        ActorInputs(game, victim, 2, 7, -1, null, carrier: victim.Id);
        game.Model.Phase = GamePhase.LiveBall;
        game.Model.Possession = TeamId.Away;
        game.Model.SourceLastPossession = 1;
        game.Model.SourceRules = new SourceRulesState(1, 1, 1, 30, 100, SourcePlayType.Normal);
        game.Model.SourceOffensePlay = real.Playbook.GetPlay("play_spider");
        game.Model.SourceDefensePlay = real.Playbook.GetPlay("play_safe_cover");
        game.Model.SourceFlags = SourceGameFlags.None;
        game.Model.SourceNoFumbles = true;
        game.Model.SourceSpinCount[0] = game.Model.SourceSpinCount[1] = 0;
        game.Model.CatchSourceTick = tick - 100;
        game.Model.BallMode = BallMode.Held;
        game.Model.BallKind = SourceBallKind.Pass;
        game.Model.SourceTick = tick;
        attacker.Facing = attacker.TargetFacing = 640;
        victim.Facing = victim.TargetFacing = 896;
        attacker.AnimationState.FullBody("run_anim", attacker, tick,
            Bind<Func<int, int>>(game, "SourceDraw"), AnimationSelectionEvidence.SourceVerified);
        victim.AnimationState.FullBody("run_wb_anim", victim, tick,
            Bind<Func<int, int>>(game, "SourceDraw"), AnimationSelectionEvidence.SourceVerified);
        SourcePlayerDriver.SetSourcePosition(attacker, new SourceVector3(9f, 0f, 0f));
        SourcePlayerDriver.SetSourcePosition(victim, new SourceVector3(0f, 0f, 0f));
        attacker.Turbo = true;
        var focus = new SourceVector3(0f, 14f, 0f);
        var eye = new SourceVector3(-120f, 85f, -40f);
        float theta = (float)Math.Atan2(focus.X - eye.X, focus.Z - eye.Z);
        SourceCinematicTransform camera = SourceCinematicTransform.FromState(
            new SourceCinematicSeed(eye, focus, 150f, theta, -.4f));
        game.Model.SourceMovementCamera = new SourceMovementCameraFrame(camera.Position, camera.Transform, 512f, 384f);
        game.Model.SourceMovementCameraTick = tick;
        Bind<Action>(game, "CacheSourceGeometry")();
        Bind<Action>(game, "CacheSourcePlayerCameraJoints")();
        game.TakeSourceSoundVmDispatches();
        game.Model.SourceDispatchOrdinal = 10;
        game.Model.SourcePresentationOrder = 200;
        Bind<Action<string, int, int>>(game, "BeginSourceProcess")("detect_proc", -1, 0x500);
        stream.Start(173, 99, 99, 3, 0, 179, 181, 99, 0, 191, 193);
        Bind<Func<int, int>>(game, "SourceDraw")(97);
        Bind<Action>(game, "SourceContacts")();
        Check(attacker.AnimationState.Primary.ScriptSymbol == "t_dshoul_anim"
            && victim.AnimationState.Primary.ScriptSymbol == "fall_04",
            "real C1 side/turbo tables, capsules and actor mutations replace provisional Contact in sound regression");
        SourceSoundVmBatch batch = game.TakeSourceSoundVmDispatches();
        Equal(2, batch.Dispatches.Count, "real collision executes attacker and victim sound ANI_CODE synchronously");
        Equal(SourceAudioHitPresence.Present, batch.Dispatches[0].StateAtCall.HitPartner.Presence,
            "do_collision assignment precedes attacker's actual get_hit ANI_CODE");
        Equal((int)victim.SourceMode, batch.Dispatches[0].StateAtCall.HitPartner.RawPlayerMode.Value,
            "actual hit partner mode, not ActorRole or inferred target");
        Equal("detect_proc", batch.Dispatches[0].RandomContext.Process, "source collision process name preserved");
        Equal("detect_proc", batch.Dispatches[1].RandomContext.Process, "same actual collision process, not cue-order process");
        Equal(5, batch.Dispatches[0].RandomContext.FirstCallIndex, "attacker preserves prefix and four actual C1 selection draws");
        Equal(5, batch.Dispatches[1].RandomContext.FirstCallIndex, "zero-draw explicit hit does not consume/reset offset");
        Check(batch.Dispatches[0].RandomContext.ProcessOrder < batch.Dispatches[1].RandomContext.ProcessOrder,
            "existing Core receiver subdispatch order distinguishes actual actors without cue-order substitution");
        Equal(9, game.Model.SourceRandomCallIndex, "victim sound accounting precedes actual C1 fumble and CSI calls");
        Bind<Func<int, int>>(game, "SourceDraw")(101);
        Equal(9, stream.Random.Trace.Last().Site.CallIndex, "following collision function sees advanced cursor");
        Begin(game, game.Model.Players[10], tick, 30);
        Bind<Func<int, int>>(game, "SourceDraw")(103);
        Equal(0, stream.Random.Trace.Last().Site.CallIndex, "next actual actor begins its own process cursor only at source dispatch");
        Equal(193, stream.Random.Trace.Last().Raw.Value, "next actor still uses same underlying shared stream");
    }

    private static void PartialFailure()
    {
        var stream = new InputStream();
        GameSimulation game = Create(stream);
        ActorState actor = game.Model.Players[4];
        ActorInputs(game, actor, 2, 3, -1, 8, partnerMode: 3);
        game.Model.SourceCurrentPlayerBank = null;
        var binding = functions.VmBindings.First(b => b.SourceLine == 5544);
        SourceAnimationProgram saved = Programs[binding.ScriptSymbol];
        Programs[binding.ScriptSymbol] = Probe(binding);
        try
        {
            Begin(game, actor, 1);
            actor.AnimationState.FullBody(binding.ScriptSymbol, actor, 1, Bind<Func<int, int>>(game, "SourceDraw"),
                AnimationSelectionEvidence.SourceVerified);
            Begin(game, actor, 2);
            stream.Start(197, 199, 211, 223, 227);
            Bind<Func<int, int>>(game, "SourceDraw")(97);
            Bind<Func<int, int>>(game, "SourceDraw")(97);
            int before = stream.Random.Trace.Count;
            long totalBefore = game.Model.SourceRandomDraws;
            SourceAudioMissingInputException failure = null;
            try { actor.AnimationState.Advance(actor, 2, Bind<Func<int, int>>(game, "SourceDraw")); }
            catch (SourceAudioMissingInputException error) { failure = error; }
            Check(failure != null && failure.Field == "plyr_bnk_str", "needed late unknown is rethrown as actual missing input");
            Equal(1, stream.Random.Trace.Count - before, "partial failure retains actual early lineman draw, no following opcode");
            Equal(3, game.Model.SourceRandomCallIndex, "partial failure advances existing Core offset before rethrow");
            Equal(1L, game.Model.SourceRandomDraws - totalBefore, "partial failure retains actual global Core draw accounting");
            SourceSoundVmDispatch partial = game.TakeSourceSoundVmDispatches().Dispatches.Single();
            Equal(SourceSoundVmStatus.MissingInput, partial.Status, "partial failure transport is explicit, never resolved silence");
            Equal(1, partial.Result.RandomCalls, "partial transport retains actual draws");
            Equal(211, partial.Result.RandomDraws[0].Raw.Value, "partial actual shared raw value retained");
            Equal(0, partial.Result.Calls.Count, "no envelope emitted before unknown bank is supplied");
            game.Model.SourceCurrentPlayerBank = "players";
            bool retryBlocked = false;
            try { game.Step(default(PlayerCommand)); game.Step(default(PlayerCommand)); }
            catch (InvalidOperationException error)
            { retryBlocked = game.Model.SourceAnimationFailure != null && error.Message.Contains("Restart is required"); }
            Check(retryBlocked, "partially advanced execution cannot be retried/reset/muted on another source wake");
            Equal(1, stream.Random.Trace.Count - before, "failed source wake never consumes another draw");
            game.Restart();
            Equal(0, game.TakeSourceSoundVmDispatches().Dispatches.Count, "restart clears pending, not detached previous results");
        }
        finally { Programs[binding.ScriptSymbol] = saved; }
    }

    private static void UnsupportedTuple()
    {
        var stream = new InputStream();
        GameSimulation game = Create(stream);
        ActorState actor = game.Model.Players[4];
        var binding = functions.VmBindings.First(b => b.SourceLine == 5544);
        SourceAnimationProgram saved = Programs[binding.ScriptSymbol];
        Programs[binding.ScriptSymbol] = new SourceAnimationProgram(binding.ScriptSymbol,
            new SourceAnimationInstruction(SourceAnimationOp.NewSequence, text: "BREATH"),
            new SourceAnimationInstruction(SourceAnimationOp.PresentationCallback, text: "dust_cloud", sourceLine: 5558),
            new SourceAnimationInstruction(SourceAnimationOp.Callback, (int)SourceAnimationSignal.PlayerDown,
                text: "player_down", sourceLine: 5573),
            new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1));
        try
        {
            Begin(game, actor, 1);
            stream.Start(229);
            int before = stream.Random.Trace.Count;
            actor.AnimationState.FullBody(binding.ScriptSymbol, actor, 1, Bind<Func<int, int>>(game, "SourceDraw"),
                AnimationSelectionEvidence.SourceVerified);
            SourceSoundVmBatch batch = game.TakeSourceSoundVmDispatches();
            Equal(1, batch.Dispatches.Count, "PlayerDown is not selected or replayed as a sound function");
            Equal(SourceSoundVmStatus.Unsupported, batch.Dispatches[0].Status, "unimplemented SPFX is explicit unsupported");
            Equal(null, batch.Dispatches[0].Result, "unsupported never masquerades as a resolved zero-call result");
            Equal(before, stream.Random.Trace.Count, "unsupported tuple does not use a sound-table fallback");
        }
        finally { Programs[binding.ScriptSymbol] = saved; }

        const string wrongScript = "t_dshoul_anim";
        saved = Programs[wrongScript];
        Programs[wrongScript] = new SourceAnimationProgram(wrongScript,
            new SourceAnimationInstruction(SourceAnimationOp.NewSequence, text: "BREATH"),
            Sound(binding), new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1));
        try
        {
            Begin(game, actor, 2);
            int before = stream.Random.Trace.Count;
            actor.AnimationState.FullBody(wrongScript, actor, 2, Bind<Func<int, int>>(game, "SourceDraw"),
                AnimationSelectionEvidence.SourceVerified);
            SourceSoundVmDispatch dispatch = game.TakeSourceSoundVmDispatches().Dispatches.Single();
            Equal(SourceSoundVmStatus.Unsupported, dispatch.Status,
                "same function/line/args in wrong actual script is unsupported");
            Equal(wrongScript, dispatch.OriginalCueContext.ExecutingSymbol, "wrong-script original identity is not rewritten");
            Equal(null, dispatch.Result, "wrong-script tuple never resolves through a similar supported site");
            Equal(before, stream.Random.Trace.Count, "wrong-script tuple consumes no selector draws");
        }
        finally { Programs[wrongScript] = saved; }
    }
}
