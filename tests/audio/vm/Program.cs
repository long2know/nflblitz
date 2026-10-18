using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;

internal sealed class SoundVmAssertion : Exception
{
    internal SoundVmAssertion(string message) : base(message) { }
}

internal static partial class Program
{
    private static FullKernelInputs real;
    private static SourceAudioFunctionData functions;
    private static int assertions;
    private static int nativeCases;
    private static string output;

    private static void Check(bool condition, string identity)
    {
        assertions++;
        if (!condition) throw new SoundVmAssertion(identity);
    }

    private static void Equal<T>(T expected, T actual, string identity)
    {
        Check(EqualityComparer<T>.Default.Equals(expected, actual),
            identity + " expected=" + expected + " actual=" + actual);
    }

    private static T Bind<T>(GameSimulation game, string name)
    {
        return (T)(object)typeof(GameSimulation).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic,
            null, typeof(T).GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray(), null)
            .CreateDelegate(typeof(T), game);
    }

    private static int Main(string[] args)
    {
        var result = new Dictionary<string, object> {
            { "schema", "source-sound-vm-checks/v1" }, { "status", "error" },
            { "DSP_playback_SPFX_or_target_PRNG_qualified", false }
        };
        try
        {
            string c2Inputs = null;
            if (args.Length >= 2 && args[args.Length - 2] == "--c2-inputs")
            {
                c2Inputs = args[args.Length - 1];
                args = args.Take(args.Length - 2).ToArray();
            }
            if (args.Length < 3 || args.Length > 5)
                throw new ArgumentException("SourceSoundVm.Checks fixture.json sound-functions.safn output-dir [actual-C-cases actual-C-results]");
            output = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(output);
            real = FullKernelInputs.Load(args[0], movementLibrary: true, soundFunctionsPath: args[1], c2InputsPath: c2Inputs);
            functions = real.SoundFunctions;
            CurrentInputState();
            BootstrapAndTransport();
            StreamAndFailures();
            RealSourcePrograms();
            if (args.Length == 5) CompareActualC(args[3], args[4]);
            result["status"] = "passed";
            result["assertions"] = assertions;
            result["actual_C_cases"] = nativeCases;
            result["actual_implemented_VM_tuples"] = functions.VmBindings.Count;
            result["actual_M1_library"] = real.Animations.InventorySha256;
            result["actual_SAFN"] = functions.PayloadSha256;
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 0;
        }
        catch (SoundVmAssertion error)
        {
            result["status"] = "failed";
            result["assertions"] = assertions;
            result["failed_assertion"] = error.Message;
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 1;
        }
        catch (Exception error)
        {
            result["assertions"] = assertions;
            result["exception"] = error.ToString();
            Console.WriteLine(JsonSerializer.Serialize(result));
            return 2;
        }
    }

    private sealed class InputStream
    {
        internal readonly List<int> Raw = new List<int>();
        internal bool Armed;
        internal int At;
        internal SourceInjectedRandom Random;
        internal InputStream()
        { NewRandom(); }
        internal ISourceRandom NewRandom()
        {
            Armed = false; At = 0;
            Random = new SourceInjectedRandom(site =>
            {
                if (!Armed) return 1234567;
                if (At >= Raw.Count) throw new SoundVmAssertion("actual shared stream exhausted");
                return Raw[At++];
            }, new SourceRandomProvenance("sound-VM/explicit-raw",
                "Named test inputs, NOT historical libc PRNG", "ordered original-C raw tape",
                SourceRandomEvidence.RecordedOracle));
            return Random;
        }
        internal void Start(params int[] raw)
        { Raw.Clear(); Raw.AddRange(raw); At = 0; Armed = true; }
    }

    private static GameSimulation Create(InputStream stream, bool bind = true)
    {
        GameSimulation game;
        if (bind && real.IsC2Profile)
        {
            var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.TwoStation, new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
                new[] { new SourceStationConfiguration(0, TeamId.Home) }, TeamId.Home, SourceCaptainSlot.First);
            game = real.Create(configuration: configuration,
                presentation: new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0),
                randomFactory: seed => stream.NewRandom());
        }
        else if (bind) game = real.Create(randomFactory: seed => stream.NewRandom());
        else
        {
            game = new GameSimulation(real.Animations, real.Geometry, real.Playbook, seed: 1,
                randomFactory: seed => stream.NewRandom(), rosters: real.Rosters, movementData: real.MovementRuntime,
                movementSettings: new SourceMovementSettings(0, 0, false, 0, 0, false));
            game.SourceCameraHeadingProvider = (state, tick) => FullKernelInputs.SetControlledMovementCamera(game, state, tick);
        }
        game.TakeSourceSoundVmDispatches();
        return game;
    }

    private static Dictionary<string, SourceAnimationProgram> Programs
    {
        get { return (Dictionary<string, SourceAnimationProgram>)typeof(SourceAnimationPrograms)
            .GetField("Programs", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null); }
    }

    private static SourceAnimationInstruction Sound(SourceAudioVmBinding binding)
    {
        return new SourceAnimationInstruction(SourceAnimationOp.PresentationCallback,
            binding.Argument0, binding.Argument1, binding.FunctionSymbol, c: binding.Argument2,
            sourceLine: binding.SourceLine);
    }

    private static SourceAnimationProgram Probe(SourceAudioVmBinding binding, bool followingRandom = true)
    {
        var list = new List<SourceAnimationInstruction> {
            new SourceAnimationInstruction(SourceAnimationOp.SetMode),
            new SourceAnimationInstruction(SourceAnimationOp.NewSequence, text: "BREATH"),
            new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1),
            Sound(binding),
        };
        if (followingRandom) list.Add(new SourceAnimationInstruction(SourceAnimationOp.SkipFrames, -1, 11));
        list.Add(new SourceAnimationInstruction(SourceAnimationOp.NextFrame, 1));
        list.Add(new SourceAnimationInstruction(SourceAnimationOp.End));
        return new SourceAnimationProgram(binding.ScriptSymbol, list.ToArray());
    }

    private static void ActorInputs(GameSimulation game, ActorState actor, int flags, int mode, int station,
        int? partner, int partnerMode = 3, int partnerStation = -1, int carrier = -1, int receiver = -1)
    {
        actor.SourceControllerStation = -1;
        SourceMovementState state = Bind<Func<ActorState, SourceMovementState>>(game, "CaptureSourceMovement")(actor);
        state.Flags = (SourceMovementFlags)flags;
        Bind<Action<ActorState, SourceMovementState>>(game, "ApplySourceMovement")(actor, state);
        actor.SourceMode = (SourceActorMode)mode;
        actor.SourceControllerStation = station;
        actor.SourceHitPartnerId = partner;
        if (partner.HasValue && partner.Value >= 0)
        {
            game.Model.Players[partner.Value].SourceMode = (SourceActorMode)partnerMode;
            game.Model.Players[partner.Value].SourceControllerStation = partnerStation;
        }
        game.Model.CarrierId = carrier;
        game.Model.TargetPlayerId = receiver;
        game.Model.SourceCurrentPlayerBank = "players";
    }

    private static void Begin(GameSimulation game, ActorState actor, long tick, int dispatch = 4, string process = "player_proc")
    {
        game.Model.SourceTick = tick;
        game.Model.SourceDispatchOrdinal = dispatch;
        game.Model.SourcePresentationOrder = 80;
        Bind<Action<string, int, int>>(game, "BeginSourceProcess")(process, actor.Id, 0);
    }

    private static SourceSoundVmDispatch ExecuteProbe(GameSimulation game, InputStream stream,
        SourceAudioVmBinding binding, ActorState actor, int prefixCalls, int[] raw, bool followingRandom = true)
    {
        SourceAnimationProgram original = Programs[binding.ScriptSymbol];
        Programs[binding.ScriptSymbol] = Probe(binding, followingRandom);
        try
        {
            long tick = Math.Max(1, game.Model.SourceTick + 2);
            Begin(game, actor, tick - 1);
            actor.AnimationState.FullBody(binding.ScriptSymbol, actor, tick - 1,
                Bind<Func<int, int>>(game, "SourceDraw"), AnimationSelectionEvidence.SourceVerified);
            Begin(game, actor, tick);
            stream.Start(Enumerable.Repeat(123, prefixCalls).Concat(raw).ToArray());
            for (int i = 0; i < prefixCalls; i++)
                Bind<Func<int, int>>(game, "SourceDraw")(97);
            int before = stream.Random.Trace.Count;
            long totalBefore = game.Model.SourceRandomDraws;
            try { actor.AnimationState.Advance(actor, tick, Bind<Func<int, int>>(game, "SourceDraw")); }
            catch (InvalidOperationException error)
            {
                if (!error.Message.Contains("Source RNG dispatch/call order regressed")) throw;
                Check(false, "actual current RNG process/order/call offset must precede following VM opcode: " + error.Message);
            }
            SourceSoundVmBatch batch = game.TakeSourceSoundVmDispatches();
            Equal(1, batch.Dispatches.Count, "one exact VM dispatch, not post-host replay");
            SourceSoundVmDispatch item = batch.Dispatches[0];
            Equal(SourceSoundVmStatus.Resolved, item.Status, "actual registered tuple resolved synchronously");
            Equal(prefixCalls, item.RandomContext.FirstCallIndex, "current source process call offset enters selector");
            Equal(game.Model.SourceRandomProcessOrder, item.RandomContext.ProcessOrder, "actual RNG process order retained");
            Check(item.RandomContext.ProcessOrder != item.OriginalCueContext.ProcessOrder,
                "RNG process order is not total cue instruction order");
            Equal(item.Result.RandomCalls + (followingRandom ? 1 : 0), stream.Random.Trace.Count - before,
                "no extra consumption and following opcode runs after sound");
            Equal((long)item.Result.RandomCalls + (followingRandom ? 1L : 0L),
                game.Model.SourceRandomDraws - totalBefore, "actual global Core draw accounting advances once");
            Equal(prefixCalls + item.Result.RandomCalls + (followingRandom ? 1 : 0),
                game.Model.SourceRandomCallIndex, "actual sound calls advance subsequent Core cursor exactly");
            if (followingRandom)
            {
                SourceRandomDraw next = stream.Random.Trace.Last();
                Equal(11, next.Site.BoundOrModulus, "real following random VM opcode");
                Equal(raw[item.Result.RandomCalls], next.Raw.Value, "following opcode uses next SAME stream value");
                Equal(prefixCalls + item.Result.RandomCalls, next.Site.CallIndex, "following opcode sees advanced offset");
            }
            return item;
        }
        finally { Programs[binding.ScriptSymbol] = original; }
    }

    private static void CurrentInputState()
    {
        GameSimulation game = Create(new InputStream());
        ActorState actor = game.Model.Players[4];
        ActorInputs(game, actor, 0, 3, 0, null, carrier: 4, receiver: 8);
        actor.Role = ActorRole.Defender;
        SourceAudioFunctionState actual = Bind<Func<ActorState, SourceAudioFunctionState>>(game, "SourceSoundState")(actor);
        Equal(0, actual.ActorFlags.Value, "actual original raw flags, not ActorRole/default onscreen");
        Equal(3, actual.ActorRawPlayerMode.Value, "actual original MODE_LINE value, not ActorRole");
        Equal(SourceAudioHitPresence.Unknown, actual.HitPartner.Presence, "unknown producer pointer never becomes default actual null");
        Equal(4, actual.CurrentBallCarrier.Value, "actual current process carrier");
        Equal(8, actual.CurrentIntendedReceiver.Value, "actual current process intended receiver");
    }

    private static void BootstrapAndTransport()
    {
        var stream = new InputStream();
        GameSimulation game = Create(stream, false);
        game.Step(default(PlayerCommand));
        bool blocked = false;
        try { game.Step(default(PlayerCommand)); }
        catch (InvalidOperationException error) { blocked = error.Message.Contains("before the first source wake"); }
        Check(blocked, "missing sound data fails before first source execution, no muted fallback");
        game.Restart();
        game.BindSourceSoundVmData(functions);
        ActorState actor = game.Model.Players[4];
        ActorInputs(game, actor, 2, 7, 0, null, carrier: 4);
        SourceAudioVmBinding binding = functions.VmBindings.First(b => b.SourceLine == 5544);
        SourceSoundVmDispatch retained = ExecuteProbe(game, stream, binding, actor, 3, new[] { 31, 47, 53, 67, 71 });
        Equal(2, retained.Result.Calls.Count, "carrier hit and fly are both resolved");
        Equal("players", retained.Result.Calls[0].Bank, "actual Game.c global bank, not data-note fallback");
        Equal(0, game.TakeSourceSoundVmDispatches().Dispatches.Count, "drain is once-only");
        int oldCount = retained.Result.Calls.Count;
        stream.Armed = false;
        game.Restart();
        Equal(oldCount, retained.Result.Calls.Count, "retained immutable envelope survives restart without mutation");
        Equal(0, game.TakeSourceSoundVmDispatches().Dispatches.Count, "restart clears pending transport");
        Equal(-1, game.Model.Players[4].SourceHitPartnerId.Value, "new-game original memset is known actual null");
        SourceContactDriver.RecordCollision(game.Model.Players[4], game.Model.Players[8]);
        Equal(8, game.Model.Players[4].SourceHitPartnerId.Value, "original do_collision records actual defender");
        Equal(4, game.Model.Players[8].SourceHitPartnerId.Value, "original do_collision records actual attacker");
        QueueAndRestart(game, stream);
    }

    private static void QueueAndRestart(GameSimulation game, InputStream stream)
    {
        ActorState actor = game.Model.Players[4];
        game.Model.Phase = GamePhase.LiveBall;
        ActorInputs(game, actor, 2, 7, 0, 8, carrier: 4);
        Begin(game, actor, 1);
        stream.Start(233, 239, 241, 251, 257);
        actor.AnimationState.FullBody("fall_04", actor, 1, Bind<Func<int, int>>(game, "SourceDraw"),
            AnimationSelectionEvidence.SourceVerified);
        int selected = stream.Random.Trace.Count;
        game.Step(default(PlayerCommand));
        Equal(selected, stream.Random.Trace.Count, "after-host transport never performs selection on a skipped wake");
        SourceSoundVmBatch retained = game.TakeSourceSoundVmDispatches();
        Equal(1, retained.Dispatches.Count, "already resolved calls survive later empty host-step transport");
        Begin(game, actor, 2);
        actor.AnimationState.FullBody("fall_04", actor, 2, Bind<Func<int, int>>(game, "SourceDraw"),
            AnimationSelectionEvidence.SourceVerified);
        game.Restart();
        Equal(0, game.TakeSourceSoundVmDispatches().Dispatches.Count, "restart clears undelivered pending calls once");
        Equal(2, retained.Dispatches[0].Result.Calls.Count, "retained batch is detached from queue/restart");
    }
}
