using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal static class Program
{
    private static int assertions;
    private static SourceAnimationLibrary library, old272;
    private static SourceC2CallbackData data;
    private static SourceCinematicLibrary impact;
    private static FullKernelInputs real;
    private static readonly List<string> evidence = new List<string>();
    private const string Animation = "e87401c8cf3e348cc2d9c7255a1f18f43e41b1694ff7dff45c4bf025a5ecce4f";
    private const string DataHash = "9d2cb7ac5d92c3c2178fc1970a68ccad778fb7d319513b1787fe3ba3fe905f99";
    private const string HitHash = "bb978454ab57b09357e422f048e4035bea01a4d5932f453dd89be39838c6e904";
    private const string ImpactHash = "9e82809268e18ae5d792f6e1614f0ea949a04b579bf4a93ea81b7833be4ce8ac";
    private const string Config = "{\"schema\":\"original-source-match-package/v1\",\"profile\":1,\"station_layout\":4," +
        "\"home_team\":{\"team\":0,\"records\":[0,1,2,3,4,5,6]},\"away_team\":{\"team\":1,\"records\":[0,1,2,3,4,5,6]}," +
        "\"stations\":[],\"first_possession\":1,\"captain\":1,\"time_adjustment\":4,\"difficulty\":7," +
        "\"continue_after_cabinet_gate\":false,\"home_play\":null,\"away_play\":null," +
        "\"presentation\":{\"designated_home_side\":1,\"station_heads\":[-1,1,18,19],\"pup_bigball\":-7},\"native_gamepads_enabled\":false}";
    private const string Bootstrap = "{\"schema\":\"original-source-movement-bootstrap/v1\",\"field_index\":6," +
        "\"infinite_turbo_team_mask\":2,\"speed_powerup\":true,\"fast_turbo_team_mask\":1,\"run_out_of_bounds_team_mask\":3," +
        "\"violence_powerup\":false,\"source_hres\":512,\"source_vres\":256}";

    private static int Main(string[] args)
    {
        try
        {
            Identity();
            if (args.Length != 0)
            {
                Check(args.Length == 7, "explicit current C2/local historical/package input closure");
                Load(args);
                Factory();
                Selectors();
                VmTiming();
                NativeTransport();
                InterleavedFifo();
                PartialAndRestart();
                OldSix();
                Package(args);
                File.WriteAllLines(Path.Combine(args[6], "bounded-evidence.txt"), evidence);
            }
            Console.WriteLine("PASS C2 production binding: " + assertions + " assertions; actual=" + (args.Length != 0) +
                "; no original-C/native/DSP/full-game proof");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("C2 ASSERTION: " + error.Message); Console.Error.WriteLine(error); return 1; }
    }

    private static void Identity()
    {
        SourceTeamResourceProfile p = SourceTeamResourceProfile.Contact281C2;
        Equal(p.StableId, "original-teams-contact281-c2", "explicit C2 stable profile");
        Equal(p.AnimationResourceKey, "SourceData/local-plyr266-contact281-c2-e87401c8", "C2 animation not272 alias");
        Equal(p.AnimationSha256, Animation, "canonical actual281 manifest");
        Equal(p.ClipCount, 281, "full nine-script281 closure not277");
        Equal(p.FrameCount, 26230, "full actual26230 selected frames");
        Equal(SourceTeamResourceProfile.C2ImpactSha256, ImpactHash, "explicit477 impact generation");
        Equal(SourceInterfacePackagePlan.C2CallbackSha256, DataHash, "exact canonical C2 JSON");
        Equal(SourceInterfacePackagePlan.C2HitSha256, HitHash, "ONE7538 exact separate SAFN");
        Equal(SourceInterfacePackagePlan.C2CallbackResourceKey, "SourceData/source-c2-callbacks-9d2cb7ac", "C2 JSON resource identity");
        Equal(SourceInterfacePackagePlan.C2HitResourceKey, "SourceData/sound-functions-c2-hit7538-bb978454", "C2 hit resource identity");
        Equal(SourceInterfacePackagePlan.C2ServicesResourceKey, "BlitzSourceInterface/original-contact281-c2-match-services", "C2 services explicit key");
        Equal(SourceTeamResourceProfile.SelectRuntimeProfile(null), SourceTeamResourceProfile.Contact272, "default C1 entry unchanged");
        Equal(SourceTeamResourceProfile.SelectRuntimeProfile(null, ""), SourceTeamResourceProfile.Contact272, "empty historical serialized selector retains C1 default");
        Equal(SourceTeamResourceProfile.SelectRuntimeProfile("?sourceprofile=original-teams-contact281-c2"), p, "URL explicitly selects C2");
        Equal(SourceTeamResourceProfile.SelectRuntimeProfile(null, p.StableId), p, "build recipe explicitly selects C2");
        Reject<SourceInterfaceException>(() => SourceTeamResourceProfile.SelectRuntimeProfile("?sourceprofile=original-teams-contact281-c2", SourceTeamResourceProfile.ContactStableId), "no conflicting built/URL entry");
        Reject<SourceInterfaceException>(() => SourceTeamResourceProfile.SelectRuntimeProfile("?sourceprofile=original-teams-contact281-c2&sourceprofile=original-teams-contact281-c2"), "no repeated entry selection");
        Reject<SourceInterfaceException>(() => p.RequireInventory(Animation, 277, 26230), "277 not full C2");
        Reject<SourceInterfaceException>(() => p.RequireInventory(SourceTeamResourceProfile.ContactAnimationSha256, 281, 26230), "old272 hash not281");
        Equal(SourceTeamResourceProfile.Contact272.AnimationSha256, "9cae962bbb861bffe4d379bc30919cf34c0238ed35be82e605253d3059cfede9", "old272 manifest unchanged");
        Equal(SourceTeamResourceProfile.Movement157.AnimationSha256, "f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e", "old157 manifest unchanged");
        Equal(SourceTeamResourceProfile.B1Final117.AnimationSha256, "f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274", "old117 manifest unchanged");
        Equal(SourceInterfacePackagePlan.SoundVmFunctionsSha256, "4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280", "old6 SAFN identity unchanged");
        Equal(SourceInterfacePackagePlan.ContactServicesResourceKey, "BlitzSourceInterface/original-contact272-soundvm-match-services", "old272 service key unchanged");
        Check(p.UsesMovementData && p.IsContact281C2 && !p.IsContact272 && !p.IsMovement157, "C2 is distinct and retains actual movement data");
        Reject<SourceInterfaceException>(() => SourceInterfacePackagePlan.ReadC2Callbacks(null, null), "no missing bootstrap payload");
        Reject<SourceInterfaceException>(() => SourceInterfacePackagePlan.WithContact281C2(null, ".", "x", "y"), "no implicit legacy data inputs");
    }

    private static SourceAnimationLibrary Library(string root)
    { return SourceAnimationLibrary.Read(File.ReadAllBytes(Path.Combine(root, "export-inventory.json")), n => File.ReadAllBytes(Path.Combine(root, n))); }
    private static void Load(string[] args)
    {
        library = Library(Path.Combine(args[0], "animation281")); old272 = Library(args[1]);
        SourceTeamResourceProfile.Contact281C2.RequireLibrary(library);
        Equal(library.FrameCount, 26230, "actual281 frames");
        Reject<SourceInterfaceException>(() => SourceTeamResourceProfile.Contact281C2.RequireLibrary(old272), "actual old272 rejected");
        byte[] callbacks = File.ReadAllBytes(Path.Combine(args[0], "c2-callback-data.json"));
        byte[] hit = File.ReadAllBytes(Path.Combine(args[0], "m-qbfite-hit-extension.safn"));
        data = SourceInterfacePackagePlan.ReadC2Callbacks(callbacks, hit);
        Equal(data.Bindings.Count, 9, "actual nine C2 tuples");
        Equal(data.HitExtensionData.VmBindings.Count, 1, "actual ONE7538 separate extension");
        Equal(data.SpeechTable("short_taunt_sp")[0], 0, "playlist0 is real original data");
        real = FullKernelInputs.Load(args[2], movementLibrary: true);
        Equal(real.SoundFunctions.VmBindings.Count, 6, "actual old6 remain separate");
        try { impact = SourceCinematicLibrary.Read(File.ReadAllBytes(args[3]), ImpactHash); }
        catch (Exception e) { throw new InvalidOperationException("actual477 impact catalog must load", e); }
        SourceTeamResourceProfile.Contact281C2.RequireImpact(impact);
        Equal(impact.AnimationCues.Count, 477, "actual477 original sites");
        foreach (SourceC2VmBinding binding in data.Bindings)
        {
            try
            {
                SourceCinematicCueDefinition site = impact.RequireExecutedOpcode(binding.ScriptSymbol, binding.SourceLine, "callback");
                Check(site.Arguments.SequenceEqual(new[] { binding.FunctionSymbol, binding.Argument0.ToString(),
                    binding.Argument1.ToString(), binding.Argument2.ToString() }), "exact9 C2 catalog tuples");
            }
            catch (Exception e) { throw new InvalidOperationException("all8 speech and hit7538 catalog bindings", e); }
        }
        SourceCinematicLibrary old = SourceCinematicLibrary.Read(File.ReadAllBytes(args[4]), SourceTeamResourceProfile.ContactImpactSha256);
        Reject<SourceInterfaceException>(() => SourceTeamResourceProfile.Contact281C2.RequireImpact(old), "old469 cannot be new477");
        foreach (SourceCinematicCueDefinition site in old.AnimationCues)
            Check(impact.RequireExecutedOpcode(site.Script, site.Line, site.Kind).Arguments.SequenceEqual(site.Arguments), "all469 old sites unchanged");
        foreach (string name in old272.SequenceNames)
            Equal(library.GetClip(name).DecodedSha256, old272.GetClip(name).DecodedSha256, "all272 clip bytes preserved");
        JsonNode node = JsonNode.Parse(File.ReadAllBytes(Path.Combine(args[0], "animation281/export-inventory.json")));
        JsonArray clips = node["clips"].AsArray(); while (clips.Count > 277) clips.RemoveAt(clips.Count - 1);
        SourceAnimationLibrary partial = SourceAnimationLibrary.Read(Encoding.UTF8.GetBytes(node.ToJsonString()),
            n => File.ReadAllBytes(Path.Combine(args[0], "animation281", n)));
        Equal(partial.SequenceNames.Count, 277, "real well-formed277 negative inventory");
        Reject<SourceInterfaceException>(() => SourceTeamResourceProfile.Contact281C2.RequireLibrary(partial), "actual277 cannot alias281");
        byte[] corrupted = (byte[])callbacks.Clone(); corrupted[corrupted.Length / 2] ^= 1;
        Reject<Exception>(() => SourceInterfacePackagePlan.ReadC2Callbacks(corrupted, hit), "changed real C2 JSON pin fails");
        Reject<Exception>(() => SourceInterfacePackagePlan.ReadC2Callbacks(callbacks, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(args[2]), "audio-functions/sound-functions.safn"))), "old6 cannot become ONE7538");
    }

    private static SourceVector3[] Pivots()
    { return real.ReferencePivots; }
    private static T Bind<T>(GameSimulation game, string name)
    {
        return (T)(object)typeof(GameSimulation).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic,
            null, typeof(T).GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray(), null)
            .CreateDelegate(typeof(T), game);
    }
    private static object Field(object obj, string name)
    { return obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj); }
    private static GameSimulation Game(Func<SourceRandomSite, int> raw = null, ISourceAnimationLibrary animations = null)
    {
        Func<int, ISourceRandom> random = seed => {
            int draw = 0;
            return new SourceInjectedRandom(site => draw++ == 0 ? 0 : draw == 2 ? 1 : raw == null ? 0 : raw(site),
                new SourceRandomProvenance("explicit-C2-presentation-fixture", "Controlled raw inputs, not historical libc",
                    "bounded production resource tests", SourceRandomEvidence.ExternalUnverified));
        };
        var config = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz, SourceStationLayout.TwoStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }), new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new[] { new SourceStationConfiguration(0, TeamId.Home) }, TeamId.Away, SourceCaptainSlot.First);
        return SourceC2Composition.CreateSimulation(animations ?? library, Pivots(), real.Playbook, config, real.Rosters,
            new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0), real.MovementRuntime,
            new SourceMovementSettings(0, 0, false, 0, 0, false), real.SoundFunctions,
            SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(data), random);
    }
    private static void Factory()
    {
        Reject<SourceInterfaceException>(() => Game(animations: old272), "factory rejects old272");
        Reject<SourceInterfaceException>(() => Game(animations: real.Animations), "factory rejects old157");
        var legacyConfig = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz, SourceStationLayout.TwoStation,
            new SourceTeamSelection(0, new[] { 0, 1, 2, 3, 4, 5, 6 }), new SourceTeamSelection(1, new[] { 0, 1, 2, 3, 4, 5, 6 }),
            new SourceStationConfiguration[0], TeamId.Home, SourceCaptainSlot.First);
        GameSimulation c1 = SourceContactComposition.CreateSimulation(old272, Pivots(), real.Playbook, legacyConfig,
            real.Rosters, new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0), real.MovementRuntime,
            new SourceMovementSettings(0, 0, false, 0, 0, false), real.SoundFunctions);
        Check(!c1.SourceC2Enabled, "historical C1 factory does not silently opt into new C2 bank draws");
        GameSimulation game = Game();
        Check(game.SourceC2Enabled, "C2 bootstrap supplied before constructor bank RNG and AI");
        Check(ReferenceEquals(Field(game, "sourceAnimations"), library), "Core SAME281 instance");
        Check(ReferenceEquals(Field(Field(game, "sourceGeometry"), "library"), library), "geometry SAME281 instance");
        Check(ReferenceEquals(Field(game, "sourceSoundVmData"), real.SoundFunctions), "legacy SAFN bound before first wake");
        SourceRandomDraw[] draws = ((SourceInjectedRandom)game.SourceRandomService).Trace.ToArray();
        Check(draws.Length > 2 && draws[0].Site.SourceIdentity.Contains("InitGameProc:556") &&
            draws[1].Site.SourceIdentity.Contains("InitGameProc:560"), "bank2 draws precede initial AI, not post-constructor binding");
        Equal(game.C2State.AnnouncerBank, "tkbank3", "first real bank draw selects announcer");
        Equal(game.C2State.TauntBank, "taunts4", "second real bank draw selects taunt");
        int prefix = draws.Length;
        Bind<Action<string, int, int>>(game, "BeginSourceProcess")("game_proc", -1, 0);
        Bind<Action<bool>>(game, "ReloadSourceC2Banks")(false);
        draws = ((SourceInjectedRandom)game.SourceRandomService).Trace.Skip(prefix).ToArray();
        Check(draws.Length == 2 && draws[0].Site.SourceIdentity.Contains("start_new_quarter:2662") &&
            draws[1].Site.SourceIdentity.Contains("start_new_quarter:2666"), "quarter bank reload uses same ordered source stream");
        game.Restart();
        Check(game.C2State.BankEpoch == 1 && game.C2State.Taunted == false, "real restart restores explicit bank-entry epoch");
    }

    private static SourceSoundVmDispatch Selected(SourceC2VmBinding binding, int argument, Func<SourceRandomSite, int> raw)
    {
        var cue = new SourceCallbackCue(new SourcePresentationContext(7, 20, 4, "plyrseq.c", binding.ScriptSymbol, binding.SourceLine),
            SourcePresentationCueKind.SoundCallback, binding.FunctionSymbol, argument, binding.Argument1, binding.Argument2);
        var context = new SourceC2SoundContext(binding.FunctionKind, 7, "player_proc", 3, 4, "plyrseq.c",
            binding.FunctionSymbol, binding.SourceLine, argument, binding.Argument1, binding.Argument2, cue);
        var execution = new SourceRandomContext(7, 4, "player_proc", 3, "explicit-selector-fixture");
        var random = new SourceInjectedRandom(raw, new SourceRandomProvenance("explicit-selector-fixture",
            "Controlled raw input", "not original PRNG", SourceRandomEvidence.ExternalUnverified));
        var state = new SourceC2SoundState("taunts4", "players", real.SoundFunctions.OnscreenFlag,
            (int)SourceActorMode.Receiver, 4, 0, false, currentBallCarrier: 4, currentIntendedReceiver: 4);
        SourceAudioFunctionResult result = SourceC2SoundFunctions.Select(data, binding.FunctionKind, context, state, random, execution);
        return new SourceSoundVmDispatch(context, execution, state, null, SourceSoundVmStatus.Resolved, result, null);
    }
    private sealed class RawRecorder : ISourceSoundVmOutputConsumer
    {
        internal readonly List<SourceSoundVmDispatch> Values = new List<SourceSoundVmDispatch>();
        public void ConsumeResolved(SourceSoundVmDispatch dispatch) { Values.Add(dispatch); }
    }
    private static SourceCinematicPlayback Playback()
    { return new SourceCinematicPlayback(impact, new SourceCinematicSeed(new SourceVector3(-120, 90, -20), new SourceVector3(0, 15, 0), 150, 1, -.4f)); }
    private static void ConsumeAnimation(SourceSoundVmTransport transport, SourceCinematicPlayback playback, SourceSoundVmDispatch dispatch)
    {
        Check(dispatch.HasOriginalAnimationCue, "only actual animation cues enter cinematic consumer");
        SourceSoundVmDispatch paired = transport.TakeForCue(dispatch.OriginalCue);
        Check(ReferenceEquals(paired, dispatch), "single FIFO pairs exact OriginalCue once");
        try { playback.ConsumeResolvedSourceSound(paired, transport.DeliverResolved); }
        catch (Exception e) { throw new InvalidOperationException("correct C2 taunt/hit kind reaches resolved consumer", e); }
        transport.FlushNativePrefix();
    }
    private static void Selectors()
    {
        foreach (SourceC2VmBinding binding in data.Bindings.Where(b => b.Argument0 == 16 || b.Argument0 == 47))
        {
            SourceSoundVmDispatch d = Selected(binding, binding.Argument0, site => { throw new InvalidOperationException("literal helper must not draw RNG"); });
            Equal(d.Result.RandomCalls, 0, "positive16/47 no helper RNG");
            Equal(d.Result.Calls.Single().PlaylistId, binding.Argument0, "literal16/47 raw playlist retained");
            var recorder = new RawRecorder(); var transport = new SourceSoundVmTransport(); transport.BindOutput(recorder);
            transport.Drain(new SourceSoundVmBatch(new[] { d })); ConsumeAnimation(transport, Playback(), d); transport.CompletePresentationBatch();
            Equal(recorder.Values.Count, 1, "C2 literal recorded once, not audio playback proof");
        }
        SourceC2VmBinding shortSite = data.Bindings.Single(b => b.SourceLine == 3219);
        SourceSoundVmDispatch zero = Selected(shortSite, -1, site => 0);
        Equal(zero.Result.Calls.Single().PlaylistId, 0, "actual short-table playlist0 retained");
        var sink = new RawRecorder(); var fifo = new SourceSoundVmTransport(); fifo.BindOutput(sink);
        fifo.Drain(new SourceSoundVmBatch(new[] { zero })); ConsumeAnimation(fifo, Playback(), zero);
        Equal(sink.Values.Count, 1, "playlist0 is nonempty raw output, never muted");
        fifo.CompletePresentationBatch();
        Reject<InvalidOperationException>(() => fifo.TakeForCue(zero.OriginalCue), "same cue cannot pair twice");
        var unbound = new SourceSoundVmTransport(); unbound.Drain(new SourceSoundVmBatch(new[] { zero }));
        unbound.TakeForCue(zero.OriginalCue);
        Reject<InvalidOperationException>(() => unbound.DeliverResolved(zero), "no DSP backend remains visible, no fake audible output");
        Check(unbound.Failure.Contains("Nothing was played"), "unbound delivery discloses no playback");
        SourceSoundVmDispatch hit = Selected(data.Bindings.Single(b => b.SourceLine == 7538), 70, site => 0);
        Check(hit.C2Context.Kind == SourceC2SoundFunctionKind.HitFunctionExtension &&
            hit.Result.Calls.Any(c => c.PlaylistId == 70) &&
            hit.Result.Calls.All(c => c.C2Context == null && c.HasOriginalAnimationCue && ReferenceEquals(c.OriginalCue, hit.OriginalCue)),
            "hit70 original algorithm retains legacy call context and actual7538 dispatch discriminator");
        var hitFifo = new SourceSoundVmTransport(); hitFifo.BindOutput(new RawRecorder()); hitFifo.Drain(new SourceSoundVmBatch(new[] { hit }));
        ConsumeAnimation(hitFifo, Playback(), hit); hitFifo.CompletePresentationBatch();
    }

    private static void VmTiming()
    {
        foreach (SourceC2VmBinding binding in data.Bindings)
        {
            GameSimulation game = Game(); ActorState actor = game.Model.Players[4];
            actor.SourceMode = SourceActorMode.Receiver; actor.SourceHitPartnerId = -1;
            actor.SourceRawFlags |= real.SoundFunctions.OnscreenFlag; game.Model.CarrierId = 4;
            actor.AnimationState.FullBody(binding.ScriptSymbol, actor, 0, Bind<Func<int, int>>(game, "SourceDraw"), AnimationSelectionEvidence.SourceVerified);
            var batch = new List<SourceSoundVmDispatch>(game.TakeSourceSoundVmDispatches().Dispatches);
            bool immediate = binding.ScriptSymbol.StartsWith("m_mscat", StringComparison.Ordinal);
            Check(immediate ? batch.Count == 1 : batch.Count == 0, "actual ANI interpolation boundary controls callback timing");
            int tick = 0;
            while (batch.Count == 0 && tick < 20)
            {
                game.Model.SourceTick = ++tick;
                actor.AnimationState.Primary.Advance(actor, tick, Bind<Func<int, int>>(game, "SourceDraw"));
                batch.AddRange(game.TakeSourceSoundVmDispatches().Dispatches);
            }
            SourceSoundVmDispatch d = batch.Single();
            Check(d.HasOriginalAnimationCue && d.C2Context.Kind == binding.FunctionKind &&
                d.OriginalCueContext.SourceLine == binding.SourceLine && d.Status == SourceSoundVmStatus.Resolved,
                "real nine-program VM emits actual typed tuple, not fake cue");
            var fifo = new SourceSoundVmTransport(); var sink = new RawRecorder(); fifo.BindOutput(sink);
            fifo.Drain(new SourceSoundVmBatch(batch)); ConsumeAnimation(fifo, Playback(), d); fifo.CompletePresentationBatch();
            PlayerView current = GameSnapshot.SourcePlayerView(actor, tick);
            SourceEvaluatedPose pose = SourceActorPoseSampler.Evaluate(library, Pivots(), current, current, 1);
            Equal(pose.Joints.Count, 15, "actual new VM frame is consumed as real fifteen-bone pose");
            evidence.Add(binding.ScriptSymbol + ":" + binding.SourceLine + " actual callback tick=" + tick +
                " selectedClip=" + current.Animation.Primary.Sample.SequenceName + " rawCalls=" + d.Result.Calls.Count);
        }
    }

    private static int Actor(SourceSoundVmDispatch d)
    {
        try { return SourceSoundVmTransport.ActorIdOf(d); }
        catch (Exception e) { throw new InvalidOperationException("native getter discriminator must precede guarded legacy access", e); }
    }
    private static void NativeTransport()
    {
        GameSimulation game = Game(site => 0);
        Bind<Action>(game, "SpawnSourceC2Whistle")(); Bind<Action>(game, "SpawnSourceC2Whistle")();
        var fifo = new SourceSoundVmTransport(); var sink = new RawRecorder(); fifo.BindOutput(sink);
        for (int tick = 1; tick <= 31; tick++)
        {
            game.Model.SourceTick = tick; game.Model.SourceDispatchOrdinal = 0;
            Bind<Action>(game, "DispatchSourceC2Whistles")();
            SourceSoundVmBatch batch = game.TakeSourceSoundVmDispatches();
            if (tick < 31) Equal(batch.Dispatches.Count, 0, "native source sleep30 has no fabricated early cue");
            foreach (SourceSoundVmDispatch d in batch.Dispatches)
            {
                Check(!d.HasOriginalAnimationCue && !d.Result.Calls[0].HasOriginalAnimationCue, "native whistle has no ANI cue");
                Equal(Actor(d), -1, "native actorless whistle stays -1, not actor0");
                Reject<InvalidOperationException>(() => GC.KeepAlive(d.OriginalCue), "native dispatch legacy getter guarded");
                Reject<InvalidOperationException>(() => GC.KeepAlive(d.Result.Calls[0].OriginalCueContext), "native call legacy getter guarded");
                Reject<InvalidOperationException>(() => Playback().ConsumeResolvedSourceSound(d), "native whistle is not an animation camera callback");
            }
            fifo.Drain(batch); fifo.FlushNativePrefix(); fifo.CompletePresentationBatch();
        }
        Equal(sink.Values.Count, 2, "both actual deferred native outputs retained in single FIFO");
        Check(sink.Values[0].C2Context.InvocationId > sink.Values[1].C2Context.InvocationId, "actual qcreate head insertion retained");
        Reject<InvalidOperationException>(() => fifo.Drain(new SourceSoundVmBatch(sink.Values)), "native dispatch cannot drain twice");
        GameSimulation immediate = Game(); ActorState actor = immediate.Model.Players[4]; actor.SourceMode = SourceActorMode.Receiver;
        Bind<Action<string, int, int>>(immediate, "BeginSourceProcess")("player_proc", 4, 0);
        Bind<Action<ActorState>>(immediate, "SourceC2ImmediateWhistle")(actor);
        SourceSoundVmDispatch call = immediate.TakeSourceSoundVmDispatches().Dispatches.Single();
        Equal(Actor(call), 4, "immediate whistle keeps real actor4 without fake ANI cue");
        Equal(call.C2Context.SourceLine, 2434, "immediate original receiver source site");
        var missing = new SourceSoundVmTransport(); missing.Drain(new SourceSoundVmBatch(new[] { call }));
        Reject<InvalidOperationException>(() => missing.FlushNativePrefix(), "native no-backend error visible");
        Check(missing.FailedDispatch == call && missing.Failure.Contains("Nothing was played"), "native raw output remains observable and not called audible");
    }

    private static void PartialAndRestart()
    {
        GameSimulation game = Game(); ActorState actor = game.Model.Players[4];
        actor.SourceRawFlags |= real.SoundFunctions.OnscreenFlag; actor.SourceMode = SourceActorMode.Receiver; actor.SourceHitPartnerId = -1;
        Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", 4, 0);
        var good = new SourceCallbackCue(new SourcePresentationContext(0, 0, 4, "plyrseq.c", "t_dshoul_anim", 2448),
            SourcePresentationCueKind.SoundCallback, "get_hit_snd", 57, 0, 0);
        Bind<Action<ActorState, SourceCallbackCue>>(game, "ExecuteSourceSoundVm")(actor, good);
        game.Model.SourceTauntBank = null;
        var bad = new SourceCallbackCue(new SourcePresentationContext(0, 1, 4, "plyrseq.c", "taunt_1_anim", 3144),
            SourcePresentationCueKind.SoundCallback, "taunt_speech", -2, 0, 0);
        int prefix = game.Model.SourceRandomCallIndex;
        Reject<SourceAudioMissingInputException>(() => Bind<Action<ActorState, SourceCallbackCue>>(game, "ExecuteSourceSoundVm")(actor, bad), "actual missing taunt bank partial failure");
        SourceSoundVmBatch batch = game.TakeSourceSoundVmDispatches();
        Check(batch.Dispatches.Count == 2 && batch.Dispatches[0].C2Context == null &&
            batch.Dispatches[1].C2Context != null && batch.Dispatches[1].Status == SourceSoundVmStatus.MissingInput &&
            game.Model.SourceRandomCallIndex == prefix + 1, "single old/new FIFO retains actual partial draw prefix");
        var fifo = new SourceSoundVmTransport(); fifo.Drain(batch);
        Check(ReferenceEquals(fifo.LastBatch, batch) && ReferenceEquals(fifo.FailedDispatch, batch.Dispatches[1]), "partial raw status remains detached/observable");
        Reject<InvalidOperationException>(() => fifo.TakeForCue(good), "partial transport cannot silently retry");
        game.Restart(); fifo.Reset();
        Check(game.C2State.Taunted == false && game.C2State.TauntBank != null && game.C2State.DeferredWhistles == 0 &&
            game.TakeSourceSoundVmDispatches().Dispatches.Count == 0 && fifo.Failure == null, "restart clears real old/new FIFO and recreates bank entry");
        Equal(batch.Dispatches[1].Result.RandomCalls, 1, "detached partial result remains unchanged after restart");
    }
    private static void InterleavedFifo()
    {
        GameSimulation game = Game(); ActorState actor = game.Model.Players[4];
        actor.SourceMode = SourceActorMode.Receiver; actor.SourceHitPartnerId = -1;
        actor.SourceRawFlags |= real.SoundFunctions.OnscreenFlag;
        game.Model.SourceTick = 5;
        Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", 4, 0);
        var first = new SourceCallbackCue(new SourcePresentationContext(5, 0, 4, "plyrseq.c", "t_dshoul_anim", 2448),
            SourcePresentationCueKind.SoundCallback, "get_hit_snd", 57, 0, 0);
        var second = new SourceCallbackCue(new SourcePresentationContext(5, 1, 4, "plyrseq.c", "m_qbwhat_anim", 7568),
            SourcePresentationCueKind.SoundCallback, "taunt_speech", 16, 0, 0);
        var last = new SourceCallbackCue(new SourcePresentationContext(5, 2, 4, "plyrseq.c", "m_mscat1_anim", 7459),
            SourcePresentationCueKind.SoundCallback, "taunt_speech", 47, 0, 0);
        Action<ActorState, SourceCallbackCue> execute = Bind<Action<ActorState, SourceCallbackCue>>(game, "ExecuteSourceSoundVm");
        execute(actor, first); execute(actor, second);
        Bind<Action<ActorState>>(game, "SourceC2ImmediateWhistle")(actor);
        execute(actor, last);
        SourceSoundVmBatch batch = game.TakeSourceSoundVmDispatches();
        Equal(batch.Dispatches.Count, 4, "one real FIFO contains old/new/native/new prefix");
        var transport = new SourceSoundVmTransport(); var sink = new RawRecorder(); transport.BindOutput(sink); transport.Drain(batch);
        try { transport.TakeForCue(last); Check(false, "interleaved reorder must reject"); }
        catch (InvalidOperationException e) { Check(e.Message.Contains("cannot reorder"), "reorder search skips guarded native getter"); }
        var playback = Playback();
        ConsumeAnimation(transport, playback, batch.Dispatches[0]);
        ConsumeAnimation(transport, playback, batch.Dispatches[1]);
        Check(sink.Values.Count == 3 && !sink.Values[2].HasOriginalAnimationCue, "native middle dispatch emits after earlier cue and before later cue");
        ConsumeAnimation(transport, playback, batch.Dispatches[3]);
        transport.CompletePresentationBatch();
        Check(sink.Values.SequenceEqual(batch.Dispatches), "single actual FIFO output order retained without duplicated native/cue queues");
        evidence.Add("Actual old2448/new7568/native2434/new7459 FIFO delivered exactly once to a recording test sink, not DSP.");
    }
    private static void OldSix()
    {
        foreach (SourceAudioVmBinding binding in real.SoundFunctions.VmBindings)
        {
            GameSimulation game = Game(); ActorState actor = game.Model.Players[4];
            actor.SourceMode = SourceActorMode.Receiver; actor.SourceHitPartnerId = -1; actor.SourceRawFlags |= real.SoundFunctions.OnscreenFlag;
            Bind<Action<string, int, int>>(game, "BeginSourceProcess")("player_proc", 4, 0);
            var cue = new SourceCallbackCue(new SourcePresentationContext(0, 0, 4, binding.SourcePath, binding.ScriptSymbol, binding.SourceLine),
                SourcePresentationCueKind.SoundCallback, binding.FunctionSymbol, binding.Argument0, binding.Argument1, binding.Argument2);
            Bind<Action<ActorState, SourceCallbackCue>>(game, "ExecuteSourceSoundVm")(actor, cue);
            SourceSoundVmDispatch d = game.TakeSourceSoundVmDispatches().Dispatches.Single();
            Check(d.C2Context == null && d.HasOriginalAnimationCue && d.Status == SourceSoundVmStatus.Resolved, "every old6 remains original exact dispatch kind");
            var fifo = new SourceSoundVmTransport(); fifo.BindOutput(new RawRecorder()); fifo.Drain(new SourceSoundVmBatch(new[] { d }));
            ConsumeAnimation(fifo, Playback(), d); fifo.CompletePresentationBatch();
        }
    }
    private static void Package(string[] args)
    {
        string output = args[6], fixtureRoot = Path.GetDirectoryName(args[2]); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "configuration.json"), Config, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "bootstrap.json"), Bootstrap, new UTF8Encoding(false));
        SourceInterfacePackagePlan m1 = SourceInterfacePackagePlan.CreateMovement157(args[5], "interface.json", SourceInterfacePackagePlan.HudSha256,
            Path.Combine(fixtureRoot, "playbook"), "catalog.tsv", SourceInterfacePackagePlan.PlaybookSha256,
            output, "configuration.json", Hash(Encoding.UTF8.GetBytes(Config)), fixtureRoot, "movement-runtime.json",
            SourceInterfaceMovementData.ContentSha256, output, "bootstrap.json", Hash(Encoding.UTF8.GetBytes(Bootstrap)));
        SourceInterfacePackagePlan oldSound = SourceInterfacePackagePlan.WithSourceSoundVm(m1, Path.Combine(fixtureRoot, "audio-functions"),
            "sound-functions.safn", SourceInterfacePackagePlan.SoundVmFunctionsSha256);
        SourceInterfacePackagePlan c1 = SourceInterfacePackagePlan.WithContact272(oldSound);
        SourceInterfacePackagePlan c2 = SourceInterfacePackagePlan.WithContact281C2(c1, args[0], "c2-callback-data.json", "m-qbfite-hit-extension.safn");
        Check(c2.HasC2 && c2.HasSoundVm && c2.Profile.IsContact281C2, "actual package selects full new C2 profile");
        Equal(c2.SelectedServicesResourceKey, SourceInterfacePackagePlan.C2ServicesResourceKey, "actual new services key");
        Equal(c1.SelectedServicesResourceKey, SourceInterfacePackagePlan.ContactServicesResourceKey, "old272 plan not mutated");
        Equal(oldSound.SelectedServicesResourceKey, SourceInterfacePackagePlan.SoundVmServicesResourceKey, "old157 Sound plan not mutated");
        Equal(m1.SelectedServicesResourceKey, SourceInterfacePackagePlan.MovementServicesResourceKey, "oldM1 plan not mutated");
        Check(c2.CopyManifest().SequenceEqual(c1.CopyManifest()) && c2.CopyConfiguration().SequenceEqual(c1.CopyConfiguration()) &&
            c2.CopyPlaybook().SequenceEqual(c1.CopyPlaybook()) && c2.CopyMovement().SequenceEqual(c1.CopyMovement()) &&
            c2.CopyBootstrap().SequenceEqual(c1.CopyBootstrap()) && c2.CopySoundVmFunctions().SequenceEqual(c1.CopySoundVmFunctions()),
            "HUD/P1/roster/head/pup/movement/DIP/old6 bytes preserved");
        Equal(c2.C2Data.PayloadSha256, DataHash, "package binds canonical C2 data");
        Equal(c2.C2Data.AnimationManifestSha256, library.InventorySha256, "package/bootstrap/Core/geometry use same actual281 identity");
        byte[] detached = c2.CopyC2Callbacks(); detached[0] ^= 1;
        Equal(Hash(c2.CopyC2Callbacks()), DataHash, "C2 data copy cannot mutate resource");
        Reject<SourceInterfaceException>(() => c1.CopyC2Callbacks(), "historical272 has no inferred C2 data");
    }
    private static string Hash(byte[] bytes)
    { using (SHA256 algorithm = SHA256.Create()) return string.Concat(algorithm.ComputeHash(bytes).Select(b => b.ToString("x2"))); }
    private static void Check(bool ok, string label) { assertions++; if (!ok) throw new InvalidOperationException(label); }
    private static void Equal<T>(T a, T b, string label) { Check(EqualityComparer<T>.Default.Equals(a, b), label); }
    private static void Reject<T>(Action action, string label) where T : Exception
    { bool rejected = false; try { action(); } catch (T) { rejected = true; } Check(rejected, label + " must reject"); }
}
