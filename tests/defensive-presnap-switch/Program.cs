using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal static class PreSnapChecks
{
    internal static int Assertions;
    internal static readonly List<object> Evidence = new List<object>();
    internal static SourceCinematicLibrary Impact;
    internal static void Check(bool value, string identity)
    {
        Assertions++;
        if (!value) throw new InvalidOperationException("PRE_SNAP_ASSERTION: " + identity);
    }
    internal static void Reject(Action action, string identity)
    {
        bool rejected = false;
        try { action(); }
        catch (ArgumentException) { rejected = true; }
        catch (SourceInterfaceException) { rejected = true; }
        catch (NotSupportedException) { rejected = true; }
        Check(rejected, identity);
    }
    internal static int[] Ints(JsonElement row, string key)
    { return row.GetProperty(key).EnumerateArray().Select(x => x.GetInt32()).ToArray(); }
    internal static int Int(JsonElement row, string key) { return row.GetProperty(key).GetInt32(); }
    internal static float Float(JsonElement value) { return value.GetSingle(); }
    internal static void Near(float actual, float expected, string field, float tolerance = .00001f)
    {
        Check(!float.IsNaN(actual) && Math.Abs(actual - expected) <= tolerance,
            field + ";actual=" + actual.ToString("R") + ";original-C=" + expected.ToString("R"));
    }
    private static int Main(string[] args)
    {
        string output = args.Length == 8 ? args[6] : null;
        var summary = new Dictionary<string, object> { { "schema", "defensive-presnap-switch-checks/v1" },
            { "status", "failed" }, { "whole_feature_or_native_qualified", false } };
        try
        {
            if (args.Length != 8) throw new ArgumentException("fixture SAFN new-profile old-nine old-M1 oracle.json output player-data");
            Directory.CreateDirectory(output);
            using (JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(args[7],"manifest.json"))))
            {
                string relative = manifest.RootElement.GetProperty("entrypoints").GetProperty("impact").GetString();
                Impact = SourceCinematicLibrary.Read(File.ReadAllBytes(Path.Combine(args[7],relative)),
                    SourceTeamResourceProfile.PreSnapSwitchImpactSha256);
            }
            FullKernelInputs actual = FullKernelInputs.Load(args[0], soundFunctionsPath: args[1],
                c2PreSnapSwitchInputsPath: args[2]);
            FullKernelInputs old = FullKernelInputs.Load(args[0], soundFunctionsPath: args[1], c2M1InputsPath: args[4]);
            FullKernelInputs nine = FullKernelInputs.Load(args[0], soundFunctionsPath: args[1], c2InputsPath: args[3]);
            Factory(actual, old, nine);
            GameSimulation.RunPreSnapHistoricalVm(old, "old281-M1");
            GameSimulation.RunPreSnapHistoricalVm(nine, "old272-nine");
            int stationCases = 0, callerCases = 0, legalRandomCases = 0;
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(args[5])))
            {
                foreach (JsonElement row in document.RootElement.GetProperty("cases").EnumerateArray())
                {
                    GameSimulation.RunPreSnapOriginalCase(actual, row);
                    if (Int(row, "kind") == 0) stationCases++;
                    else if (row.GetProperty("name").GetString().StartsWith("ordered-randrng10-", StringComparison.Ordinal)) legalRandomCases++;
                    else callerCases++;
                }
                summary["original_C_station_cases"] = stationCases;
                summary["original_C_caller_cases"] = callerCases;
                summary["legal_ordered_randrng10_values"] = legalRandomCases;
                GameSimulation.RunPreSnapRealVm(actual, document.RootElement.GetProperty("vm"));
                int interruptedOffsets = 0;
                foreach (JsonElement scenario in document.RootElement.GetProperty("vm_interrupted").EnumerateArray())
                {
                    GameSimulation.RunPreSnapRealVm(actual,scenario.GetProperty("rows"),
                        Int(scenario,"interrupt_offset"));
                    interruptedOffsets += scenario.GetProperty("rows").GetArrayLength();
                }
                summary["interrupted_VM_offsets"] = interruptedOffsets;
                GameSimulation.RunPreSnapRealVm(actual,document.RootElement.GetProperty("vm_secondary"),secondary:true);
                summary["secondary_VM_offsets"] = document.RootElement.GetProperty("vm_secondary").GetArrayLength();
            }
            GameSimulation.RunPreSnapSameWake(actual);
            summary["status"] = "passed";
            summary["original_C_station_cases"] = stationCases;
            summary["original_C_caller_cases"] = callerCases;
            summary["legal_ordered_randrng10_values"] = legalRandomCases;
            summary["actual_VM_offsets"] = 116;
            summary["root_boundary_offsets"] = new[] { 24,39,40,113,114,115 };
            summary["historical_VM_profiles"] = new[] { "old281-M1","old272-nine" };
            summary["profile"] = SourceTeamResourceProfile.PreSnapSwitchStableId;
            summary["factory"] = "SourceC2Composition.CreatePreSnapSwitchSimulation";
        }
        catch (Exception error)
        {
            summary["failed_assertion"] = error.Message;
            summary["exception"] = error.ToString();
        }
        summary["assertions"] = Assertions;
        if (output != null)
        {
            File.WriteAllText(Path.Combine(output, "focused-result.json"), JsonSerializer.Serialize(summary));
            File.WriteAllText(Path.Combine(output, "actual-caller-vm-pose-sound.json"),
                JsonSerializer.Serialize(Evidence, new JsonSerializerOptions { IncludeFields = true }));
        }
        Console.WriteLine(JsonSerializer.Serialize(summary));
        return (string)summary["status"] == "passed" ? 0 : 1;
    }

    internal static SourceMatchConfiguration Configuration(int layout = 4, int mask = 1, int side = 1, int captain = 0)
    {
        var stations = new List<SourceStationConfiguration>();
        for (int physical = 3; physical >= 0; physical--)
            if ((mask & (1 << physical)) != 0)
                stations.Add(new SourceStationConfiguration(physical, (TeamId)(physical % 2)));
        return new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz, (SourceStationLayout)layout,
            new SourceTeamSelection(0, new[] { 0,1,2,3,4,5,6 }), new SourceTeamSelection(1, new[] { 0,1,2,3,4,5,6 }),
            stations, (TeamId)side, (SourceCaptainSlot)captain);
    }
    internal static SourcePresentationSettings Presentation()
    { return new SourcePresentationSettings(TeamId.Home, new[] { -1,-1,-1,-1 }, 0); }
    internal static GameSimulation Create(FullKernelInputs actual, SourceMatchConfiguration configuration = null,
        Func<int, ISourceRandom> random = null)
    {
        return actual.Create(configuration: configuration ?? Configuration(), presentation: Presentation(),
            randomFactory: random ?? (seed => new SourceInjectedRandom(site => 0,
                new SourceRandomProvenance("defensive-presnap-switch/authored-rows",
                    "Bounded explicit host fixture; not native random", "defined original-C state row",
                    SourceRandomEvidence.ExternalUnverified))), camera: false);
    }
    private static void Factory(FullKernelInputs actual, FullKernelInputs old, FullKernelInputs nine)
    {
        int randomFactories = 0;
        Func<int, ISourceRandom> never = seed => { randomFactories++; throw new InvalidOperationException("PREFLIGHT_DREW"); };
        var bootstrap = SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(actual.C2Data);
        var oldBootstrap = SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(old.C2Data);
        var settings = new SourceMovementSettings(0,0,false,0,0,false);
        Action<ISourceAnimationLibrary,SourceC2Bootstrap,SourceMatchConfiguration,SourceAudioFunctionData> create =
            (library, entry, config, sound) => SourceC2Composition.CreatePreSnapSwitchSimulation(library,
                actual.ReferencePivots, actual.Playbook, config, actual.Rosters, Presentation(), actual.MovementRuntime,
                settings, sound, entry, never);
        Reject(() => create(old.Animations,bootstrap,Configuration(),actual.SoundFunctions),
            "factory rejects old281 library before first constructor draw/wake");
        Reject(() => create(actual.Animations,oldBootstrap,Configuration(),actual.SoundFunctions),
            "factory rejects old fourteen bindings before first constructor draw/wake");
        Reject(() => create(actual.Animations,SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(nine.C2Data),
            Configuration(),actual.SoundFunctions), "factory rejects old nine before first draw/wake");
        Reject(() => create(actual.Animations,bootstrap,null,actual.SoundFunctions),
            "factory requires explicit new configuration before draws");
        Reject(() => create(actual.Animations,bootstrap,Configuration(),null), "factory requires exact mandatory SAFN before draws");
        Reject(() => create(actual.Animations,bootstrap,Configuration(2,1),actual.SoundFunctions),
            "undefined TwoStation control[-1] rejected before constructor draws");
        Reject(() => SourceC2Composition.CreateM1Simulation(actual.Animations,actual.ReferencePivots,actual.Playbook,
            Configuration(),actual.Rosters,Presentation(),actual.MovementRuntime,settings,actual.SoundFunctions,bootstrap,never),
            "old M1 factory cannot promote new282 data");
        Reject(() => SourceC2Composition.CreateSimulation(actual.Animations,actual.ReferencePivots,actual.Playbook,
            Configuration(),actual.Rosters,Presentation(),actual.MovementRuntime,settings,actual.SoundFunctions,bootstrap,never),
            "old nine factory cannot promote new282 data");
        Check(randomFactories == 0, "all typed data/config failures precede the first randomFactory invocation");
        GameSimulation historical = Create(old);
        Check(!historical.SourceDefensivePreSnapSwitchEnabled && historical.SourceC2Enabled,
            "real explicit old M1 stays disabled with its unchanged library/bootstrap");
        Check(!typeof(GameSimulation).GetProperty("SourceDefensivePreSnapSwitchEnabled").CanWrite,
            "the enabled profile has no independently mutable flag");
    }

    internal sealed class RecordingOutput : ISourceSoundVmOutputConsumer
    {
        internal readonly List<SourceSoundVmDispatch> Calls = new List<SourceSoundVmDispatch>();
        public void ConsumeResolved(SourceSoundVmDispatch dispatch) { Calls.Add(dispatch); }
    }
    internal static void Transport(SourceSoundVmBatch batch, SourceCinematicLibrary impact, string identity)
    {
        var transport = new SourceSoundVmTransport();
        var output = new RecordingOutput();
        transport.BindOutput(output);
        transport.Drain(batch);
        var playback = new SourceCinematicPlayback(impact, new SourceCinematicSeed(
            new SourceVector3(-100,85,-40),new SourceVector3(0,0,0),150,0,-.4f));
        foreach (SourceSoundVmDispatch dispatch in batch.Dispatches)
        {
            Check(ReferenceEquals(transport.TakeForCue(dispatch.OriginalCue),dispatch), identity + " exact detached cue pairing");
            playback.ConsumeResolvedSourceSound(dispatch, transport.DeliverResolved);
        }
        Check(output.Calls.Count == batch.Dispatches.Count && output.Calls.Zip(batch.Dispatches,
            (a,b) => ReferenceEquals(a,b)).All(x => x), "missing-synchronous-resolved-callback");
        transport.CompletePresentationBatch();
        bool duplicate = false;
        try { transport.Drain(batch); } catch (InvalidOperationException) { duplicate = true; }
        Check(duplicate, identity + " duplicate drain is rejected, never repeated output");
        var missing = new SourceSoundVmTransport();
        missing.Drain(batch);
        bool absentConsumer = false;
        try {
            foreach (SourceSoundVmDispatch dispatch in batch.Dispatches)
            {
                missing.TakeForCue(dispatch.OriginalCue);
                missing.DeliverResolved(dispatch);
            }
        } catch (InvalidOperationException) { absentConsumer = true; }
        Check(absentConsumer, identity + " nonempty resolved output without a real consumer is not success");
        foreach (SourceSoundVmDispatch original in batch.Dispatches)
        {
            SourceC2SoundContext context = original.C2Context;
            var substitute = new SourceC2SoundContext(context.Kind,context.SourceTick,context.Process,
                context.ProcessOrder,context.ActorId,context.SourcePath,context.FunctionSymbol,context.SourceLine,
                context.Argument0,context.Argument1,context.Argument2,context.OriginalAnimationCue,
                context.InvocationId,context.FirstCallIndex);
            SourceAudioResolvedOriginalSoundCall actual = original.Result.Calls.Single();
            var changed = new SourceAudioResolvedOriginalSoundCall(substitute,actual.CalleeSourceSite,
                actual.SubcallOrder,actual.Bank,actual.PlaylistId,actual.Volume,actual.Pan,actual.PriorityWord,actual.RandomCalls);
            var forged = new SourceSoundVmDispatch(context,original.RandomContext,original.C2StateAtCall.Value,
                original.StateAtCall,SourceSoundVmStatus.Resolved,
                new SourceAudioFunctionResult(new List<SourceAudioResolvedOriginalSoundCall> { changed },
                    original.Result.RandomDraws.ToList()),null);
            int delivered = 0;
            bool rejected = false;
            var consumer = new SourceCinematicPlayback(impact,new SourceCinematicSeed(
                new SourceVector3(-100,85,-40),new SourceVector3(0,0,0),150,0,-.4f));
            try { consumer.ConsumeResolvedSourceSound(forged,dispatch => delivered++); }
            catch (InvalidOperationException error)
            { rejected = error.Message.Contains("exact OriginalCue and ordered subcalls"); }
            Check(rejected && delivered == 0 && !ReferenceEquals(substitute,context),
                "actual-hop-kind5-same-context-and-reject-equal-valued-substitute");
        }
    }
}

namespace Blitz.Gameplay
{
    public sealed partial class GameSimulation
    {
        private void PreparePreSnapRow(JsonElement row)
        {
            Model.SourceTick = 1000;
            Model.Possession = (TeamId)PreSnapChecks.Int(row,"side");
            Model.Phase = PreSnapChecks.Int(row,"live") == 1 ? GamePhase.LiveBall : GamePhase.PreSnap;
            Model.SourceFlags = PreSnapChecks.Int(row,"onside") == 1 ? SourceGameFlags.OnsideKick : SourceGameFlags.None;
            Model.PreSnapTicksRemaining = PreSnapChecks.Int(row,"counter");
            Model.SourceCaptainPlayers[0] = 4; Model.SourceCaptainPlayers[1] = 11;
            Array.Copy(PreSnapChecks.Ints(row,"controls"),Model.SourceStationPlayers,4);
            int[] modes = PreSnapChecks.Ints(row,"modes");
            for (int i=0;i<14;i++)
            {
                ActorState actor = Model.Players[i];
                actor.SourceMode = (SourceActorMode)modes[i];
                actor.SourceButtons = actor.SourceNewButtons = SourceDroneButtons.None;
                actor.SourceStick = actor.SourcePreviousStick = 24;
                actor.SourceFakeBTick = -1;
                actor.AnimationState.Primary.AddMode(SourceAnimationModes.Uninterruptible);
                SourcePlayerDriver.SetSourcePosition(actor,new SourceVector3(1000000,0,0));
                actor.ModelScale = 1f;
            }
            RecomputeSourceMovementStations();
            sourceStepEvents.Clear(); sourcePresentationCues.Clear(); sourceSoundVmPending.Clear();
        }
        internal static void RunPreSnapOriginalCase(FullKernelInputs actual, JsonElement row)
        {
            string name = row.GetProperty("name").GetString();
            var config = PreSnapChecks.Configuration(PreSnapChecks.Int(row,"layout"),PreSnapChecks.Int(row,"mask"),
                PreSnapChecks.Int(row,"side"),PreSnapChecks.Int(row,"captain"));
            GameSimulation game = null;
            SourceInjectedRandom ledger = null;
            object atDraw = null;
            int[] controlsAtDraw = null, stationsAtDraw = null;
            int?[] captainsAtDraw = null;
            int fakeBAtDraw = 0, callingStation = -1;
            game = PreSnapChecks.Create(actual,config,seed => ledger = new SourceInjectedRandom(site => {
                if (site.SourceIdentity == "player.c:_mode_dbps:2785")
                {
                    controlsAtDraw = (int[])game.Model.SourceStationPlayers.Clone();
                    stationsAtDraw = game.Model.Players.Select(p=>p.SourceControllerStation).ToArray();
                    captainsAtDraw = (int?[])game.Model.SourceCaptainPlayers.Clone();
                    fakeBAtDraw = (int)game.Model.Players[game.Model.SourceStationPlayers[callingStation]].SourceButtons;
                    atDraw = new { controls = (int[])game.Model.SourceStationPlayers.Clone(),
                        stations = game.Model.Players.Select(p => p.SourceControllerStation).ToArray(),
                        captains = (int?[])game.Model.SourceCaptainPlayers.Clone(),
                        buttons = game.Model.Players.Select(p => p.SourceButtons).ToArray(),
                        game.Model.SourceRandomProcess, game.Model.SourceRandomProcessOrder, game.Model.SourceRandomCallIndex };
                }
                return PreSnapChecks.Int(row,"value");
            },new SourceRandomProvenance("defensive-presnap-switch/defined-row","Explicit unfiltered authored fixture value",
                name,SourceRandomEvidence.ExternalUnverified)));
            game.PreparePreSnapRow(row);
            JsonElement expected = row.GetProperty("expected");
            if (PreSnapChecks.Int(row,"kind") == 0)
            {
                PreSnapChecks.Check(game.Model.Players.Select(p=>p.SourceControllerStation).SequenceEqual(PreSnapChecks.Ints(expected,"stations")),
                    name + " original all14 station links");
                PreSnapChecks.Check(game.Model.SourceCaptainPlayers.Select(x=>x.Value).SequenceEqual(PreSnapChecks.Ints(expected,"captains")),
                    name + " physical station/captain order and TwoStation early return");
                PreSnapChecks.Check(game.Model.SourceStationPlayers.SequenceEqual(PreSnapChecks.Ints(row,"controls")),
                    name + " original raw control table unchanged");
                return;
            }
            ActorState caller = game.Model.Players[PreSnapChecks.Int(row,"caller")];
            callingStation = caller.SourceControllerStation;
            caller.SourceMode = (SourceActorMode)PreSnapChecks.Int(row,"mode");
            caller.SourceButtons = caller.SourceNewButtons = PreSnapChecks.Int(row,"fresh") == 1
                ? SourceDroneButtons.B : SourceDroneButtons.None;
            int firstDraw = ledger.Trace.Count;
            game.BeginSourceProcess("player_proc",caller.Id,caller.Id+1);
            int callerOrder = game.Model.SourceRandomProcessOrder;
            game.ExecuteSourceMovementMode(caller,default(PlayerCommand));
            SourceRandomDraw[] draws = ledger.Trace.Skip(firstDraw).ToArray();
            PreSnapChecks.Check(game.Model.SourceStationPlayers.SequenceEqual(PreSnapChecks.Ints(expected,"controls")), name);
            PreSnapChecks.Check(game.Model.Players.Select(p=>p.SourceControllerStation).SequenceEqual(PreSnapChecks.Ints(expected,"stations")),
                name + " original all14 links after unconditional assignment");
            PreSnapChecks.Check(game.Model.SourceCaptainPlayers.Select(x=>x.Value).SequenceEqual(PreSnapChecks.Ints(expected,"captains")),
                name + " original captain recomputation");
            int count = PreSnapChecks.Int(expected,"rngCount");
            PreSnapChecks.Check(draws.Length == count, "missing-ordered-randrng10");
            SourceSoundVmBatch sounds = game.TakeSourceSoundVmDispatches();
            PreSnapChecks.Check(sounds.Dispatches.Count == PreSnapChecks.Int(expected,"soundCount"), "missing-synchronous-resolved-callback");
            if (count != 0)
            {
                SourceRandomDraw draw = draws.Single();
                PreSnapChecks.Check(draw.Site.SourceIdentity == "player.c:_mode_dbps:2785" &&
                    draw.Site.Kind == SourceRandomKind.RandRange && draw.Site.BoundOrModulus == 10 &&
                    draw.Value == PreSnapChecks.Int(row,"value") && draw.Site.Process == "player_proc" &&
                    draw.Site.ActorId == caller.Id && draw.Site.ProcessOrder == callerOrder && atDraw != null,
                    "missing-ordered-randrng10");
                PreSnapChecks.Check(controlsAtDraw.SequenceEqual(PreSnapChecks.Ints(expected,"controls")) &&
                    stationsAtDraw.SequenceEqual(PreSnapChecks.Ints(expected,"stations")) &&
                    captainsAtDraw.Select(v=>v.Value).SequenceEqual(PreSnapChecks.Ints(expected,"captains")) &&
                    fakeBAtDraw == PreSnapChecks.Int(expected,"rngFakeB"),
                    name + " original recomputation/fake-B precede the single ordered draw");
                SourceGameEvent transfer = game.sourceStepEvents.Single(e=>e.Type == SourceEventType.ControllerChanged);
                ActorState destination = game.Model.Players[transfer.ActorPlayerId];
                PreSnapChecks.Check(destination.AnimationState.Primary.ScriptSymbol == expected.GetProperty("program").GetString() &&
                    destination.AnimationState.Primary.SequenceName == "M_HOPCLP", "wrong-authentic-clip");
                PreSnapChecks.Check(destination.SourceFakeBTick == game.Model.SourceTick &&
                    (destination.SourceButtons & SourceDroneButtons.B) != 0,
                    "fake-B-external-edge-reinjection");
                SourceSoundVmDispatch sound = sounds.Dispatches.Single();
                PreSnapChecks.Check(sound.Status == SourceSoundVmStatus.Resolved &&
                    sound.C2Context.Kind == SourceC2SoundFunctionKind.AttackSoundFixed &&
                    sound.C2Context.ActorId == destination.Id && sound.C2Context.ProcessOrder == callerOrder &&
                    sound.C2Context.Process == "player_proc" && sound.C2Context.FirstCallIndex == draw.Site.CallIndex+1 &&
                    sound.Result.RandomCalls == 0 && sound.Result.Calls.Count == 1, "missing-synchronous-resolved-callback");
                SourceAudioResolvedOriginalSoundCall call = sound.Result.Calls.Single();
                PreSnapChecks.Check(call.Bank == expected.GetProperty("bank").GetString() &&
                    call.PlaylistId == PreSnapChecks.Int(expected,"playlist") && call.Volume == PreSnapChecks.Int(expected,"volume") &&
                    call.Pan == PreSnapChecks.Int(expected,"pan") && call.PriorityWord == (uint)PreSnapChecks.Int(expected,"priority") &&
                    call.CalleeSourceSite.SourcePath == "sounds.c" && call.CalleeSourceSite.SourceLine == 1638 &&
                    ReferenceEquals(call.C2Context,sound.C2Context) && ReferenceEquals(call.OriginalCue,sound.OriginalCue),
                    name + " exact original fixed sound call and immutable context");
                PreSnapChecks.Transport(sounds, ReadImpact(actual), name);
            }
            if (caller.SourceMode == SourceActorMode.DefensiveBackPreSnap || PreSnapChecks.Int(row,"live") == 1)
                PreSnapChecks.Check((int)caller.SourceMode == PreSnapChecks.Int(expected,"mode") &&
                    caller.SourceTurnRate == PreSnapChecks.Int(expected,"turnRate") &&
                    caller.SourceUpperTurnRate == PreSnapChecks.Int(expected,"upperTurnRate") &&
                    !caller.SourceReturnedFromMovementMode, name + " actual handler continues after transfer");
            PreSnapChecks.Evidence.Add(new { name, expected = expected.Clone(), actual = new {
                controls = game.Model.SourceStationPlayers, stations = game.Model.Players.Select(p=>p.SourceControllerStation),
                captains = game.Model.SourceCaptainPlayers, atDraw, draws, sounds = sounds.Dispatches.Select(s => new {
                    s.Status,s.C2Context,s.C2StateAtCall,calls=s.Result.Calls.Select(c=>new {
                        c.Bank,c.PlaylistId,c.Volume,c.Pan,c.PriorityWord,c.CalleeSourceSite }) }) } });
        }
        private static SourceCinematicLibrary ReadImpact(FullKernelInputs actual)
        {
            if (!actual.IsPreSnapSwitchProfile) throw new InvalidOperationException("Explicit new impact generation required.");
            return PreSnapChecks.Impact;
        }
        internal static void RunPreSnapHistoricalVm(FullKernelInputs inputs, string identity)
        {
            GameSimulation game = PreSnapChecks.Create(inputs);
            PreSnapChecks.Check(!game.SourceDefensivePreSnapSwitchEnabled, identity + " keeps the new VM flight disabled");
            game.Model.SourceTick = 1000; game.BeginSourceProcess("player_proc",3,4);
            ActorState actor = game.Model.Players[4];
            actor.ModelScale = 1f; actor.SourceRoot = default(SourceVector3); actor.Facing = 0;
            SourcePlayerDriver.SetSourcePosition(actor,new SourceVector3(100,0,100));
            SourceAnimationInstruction transition = SourceAnimationPrograms.Get("stance_anim_int",
                game.Model.SourceC2Programs).Instructions.First(i => i.Op == SourceAnimationOp.Interpolate);
            ISourceAnimationClip target = inputs.Animations.GetClip(transition.Text);
            float targetY = target.GetFrame(target.SelectedSourceFrames[transition.B]).ScaledRoot.Y;
            for (int offset=0;offset<32;offset++)
            {
                if (offset == 0)
                    actor.AnimationState.FullBody("stance_anim",actor,1000,game.SourceDraw,AnimationSelectionEvidence.SourceVerified);
                else if (offset == 9 || offset == 13)
                {
                    float from = actor.SourceRoot.Y;
                    actor.AnimationState.FullBody("stance_anim_int",actor,1000+offset,game.SourceDraw,
                        AnimationSelectionEvidence.SourceVerified);
                    float expected = from+(targetY-from)*(1f/transition.A);
                    PreSnapChecks.Check(actor.SourceRoot.Y == expected,
                        "historical VM retains actor-root origin "+identity+" offset"+offset+
                        ";actual="+actor.SourceRoot.Y.ToString("R")+";historical="+expected.ToString("R"));
                }
                else actor.AnimationState.Advance(actor,1000+offset,game.SourceDraw);
                SourceAnimationLayerState layer = actor.AnimationState.Primary;
                PreSnapChecks.Evidence.Add(new { kind="historical-VM",profile=identity,offset,layer.FrameOrdinal,
                    layer.Mode,layer.ScriptSymbol,layer.SequenceName,actor.SourceRoot,
                    world=SourcePlayerDriver.WorldPosition(actor),actor.SourceVelocity,actor.SourceVerticalVelocity,
                    pose=layer.EvaluatePose().Rotations });
            }
        }
        internal static void RunPreSnapRealVm(FullKernelInputs actual, JsonElement rows, int interruptOffset = -1,
            bool secondary = false)
        {
            GameSimulation game = PreSnapChecks.Create(actual);
            game.Model.SourceTick = 1000; game.BeginSourceProcess("player_proc",3,4);
            ActorState actor = game.Model.Players[4];
            actor.ModelScale = 1f; actor.SourceRoot = default(SourceVector3); actor.Facing = 0;
            actor.SourceVelocity = new FieldVector(3,4); actor.SourceVerticalVelocity = -2;
            SourcePlayerDriver.SetSourcePosition(actor,new SourceVector3(100,0,100));
            int before = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count;
            if (secondary) actor.AnimationState.Secondary.Change("m_hopclp_anim",actor,1000,game.SourceDraw);
            else actor.AnimationState.FullBody("m_hopclp_anim",actor,1000,game.SourceDraw,AnimationSelectionEvidence.SourceVerified);
            SourceAnimationLayerState layer = secondary ? actor.AnimationState.Secondary : actor.AnimationState.Primary;
            long instance = layer.InstanceId;
            foreach (JsonElement row in rows.EnumerateArray())
            {
                int offset = PreSnapChecks.Int(row,"offset");
                if (offset == interruptOffset)
                    actor.AnimationState.FullBody("stance_anim_int",actor,1000+offset,game.SourceDraw,
                        AnimationSelectionEvidence.SourceVerified);
                else if (offset != 0) layer.Advance(actor,1000+offset,game.SourceDraw);
                SourceAnimationLayer view = layer.View(SourceTiming.AllJoints,actor.Facing);
                PreSnapChecks.Evidence.Add(new { kind = "VM-before-assertion",secondary,interruptOffset,offset,
                    expected = row.Clone(),actual = new { layer.FrameOrdinal,layer.ScriptSymbol,layer.SequenceName,
                        layer.InstanceId,layer.Mode,actor.SourceRoot,actor.SourceVerticalVelocity,
                        world=SourcePlayerDriver.WorldPosition(actor),view.Transition01,view.Sample } });
                PreSnapChecks.Check(layer.InstanceId == instance+(interruptOffset >= 0 && offset >= interruptOffset ? 1 : 0),
                    "stance GOTO preserves the original instance");
                if (offset == 39 || offset == 40)
                    PreSnapChecks.Near(layer.FrameOrdinal,PreSnapChecks.Int(row,"targetOrdinal"),
                        "original VM interpolation hold target offset"+offset,0f);
                PreSnapChecks.Check(layer.FrameOrdinal == PreSnapChecks.Int(row,"targetOrdinal"),
                    "wrong-binary32-stream-timing;offset="+offset+";actual="+layer.FrameOrdinal+
                    ";original-target="+PreSnapChecks.Int(row,"targetOrdinal"));
                if (offset < 24)
                {
                    PreSnapChecks.Check(layer.SequenceName == "M_HOPCLP" && layer.ScriptSymbol == "m_hopclp_anim",
                        "wrong-authentic-clip");
                    PreSnapChecks.Near(view.Sample.Interpolation01,
                        row.GetProperty("floating").GetSingle()-PreSnapChecks.Int(row,"ordinal"),"wrong-binary32-stream-timing",.000002f);
                }
                else PreSnapChecks.Check(layer.ScriptSymbol == "stance_anim_int" && layer.SequenceName == "RUSH_C" &&
                    !layer.IsUninterruptible,"original offset24 GOTO/clear Uninterruptible");
                PreSnapChecks.Check(layer.Mode == unchecked((uint)PreSnapChecks.Int(row,"mode")),
                    "original VM mode/anchor offset"+offset+";actual="+layer.Mode+";original-C="+PreSnapChecks.Int(row,"mode"));
                PreSnapChecks.Near(view.Transition01,row.GetProperty("interpolation").GetSingle(),
                    "original VM interpolation ratio offset"+offset,0f);
                ISourceAnimationClip selected = actual.Animations.GetClip(layer.SequenceName);
                PreSnapChecks.Check(view.Sample.SourceFrame == selected.SelectedSourceFrames[PreSnapChecks.Int(row,"targetOrdinal")],
                    "original VM selected authoring frame offset"+offset);
                float[] root = row.GetProperty("root").EnumerateArray().Select(PreSnapChecks.Float).ToArray();
                PreSnapChecks.Near(actor.SourceRoot.X,root[0],"original VM root-X offset"+offset);
                PreSnapChecks.Near(actor.SourceRoot.Y,root[1],"original VM root-Y offset"+offset);
                PreSnapChecks.Near(actor.SourceRoot.Z,root[2],"original VM root-Z offset"+offset);
                PreSnapChecks.Near(actor.SourceVerticalVelocity,row.GetProperty("vy").GetSingle(),"original VM Y velocity offset"+offset);
                float[] velocity = row.GetProperty("velocity").EnumerateArray().Select(PreSnapChecks.Float).ToArray();
                PreSnapChecks.Near(actor.SourceVelocity.X,velocity[0],"original VM X velocity offset"+offset);
                PreSnapChecks.Near(actor.SourceVerticalVelocity,velocity[1],"original VM Y velocity vector offset"+offset);
                PreSnapChecks.Near(actor.SourceVelocity.Z,velocity[2],"original VM Z velocity offset"+offset);
                SourceVector3 world = SourcePlayerDriver.WorldPosition(actor);
                float[] position = row.GetProperty("position").EnumerateArray().Select(PreSnapChecks.Float).ToArray();
                PreSnapChecks.Near(world.X,position[0],"original VM world-X offset"+offset);
                PreSnapChecks.Near(world.Y,position[1],"original VM world-Y offset"+offset);
                PreSnapChecks.Near(world.Z,position[2],"original VM world-Z offset"+offset);
                SourceJointPose pose = layer.EvaluatePose();
                float[] q = row.GetProperty("pose").EnumerateArray().Select(PreSnapChecks.Float).ToArray();
                float[] fromPose = row.GetProperty("fromPose").EnumerateArray().Select(PreSnapChecks.Float).ToArray();
                PreSnapChecks.Check(pose.Rotations.Count == 15 && q.Length == 60,"actual original fifteen-limb pose");
                for (int joint=0;joint<15;joint++)
                {
                    SourceRotation r = pose.Rotations[joint];
                    double dot = r.X*q[joint*4]+r.Y*q[joint*4+1]+r.Z*q[joint*4+2]+r.W*q[joint*4+3];
                    PreSnapChecks.Check(Math.Abs(Math.Abs(dot)-1d) < .00001d,
                        "actual original fifteen-limb quaternion offset"+offset+"/joint"+joint);
                    if (fromPose.Length != 0)
                    {
                        PreSnapChecks.Check(view.TransitionFromPose != null,"original VM selected origin pose offset"+offset);
                        SourceRotation from = view.TransitionFromPose.Rotations[joint];
                        double fromDot = from.X*fromPose[joint*4]+from.Y*fromPose[joint*4+1]
                            +from.Z*fromPose[joint*4+2]+from.W*fromPose[joint*4+3];
                        PreSnapChecks.Check(Math.Abs(Math.Abs(fromDot)-1d) < .00001d,
                            "original VM selected origin quaternion offset"+offset+"/joint"+joint);
                    }
                }
                SourceVector3 rootBefore = actor.SourceRoot;
                uint modeBefore = layer.Mode;
                layer.Advance(actor,1000+offset,game.SourceDraw);
                SourceVector3 sameTickWorld = SourcePlayerDriver.WorldPosition(actor);
                PreSnapChecks.Check(layer.FrameOrdinal == PreSnapChecks.Int(row,"targetOrdinal") &&
                    sameTickWorld.X == world.X && sameTickWorld.Y == world.Y && sameTickWorld.Z == world.Z &&
                    actor.SourceRoot.X == rootBefore.X && actor.SourceRoot.Y == rootBefore.Y &&
                    actor.SourceRoot.Z == rootBefore.Z && layer.Mode == modeBefore,
                    "immediate-entry-double-advance");
                PreSnapChecks.Evidence.Add(new { secondary,interruptOffset,offset,instance,script=layer.ScriptSymbol,clip=layer.SequenceName,
                    layer.FrameOrdinal,actor.SourceRoot,world,actor.SourceVerticalVelocity,pose=pose.Rotations });
            }
            PreSnapChecks.Check(((SourceInjectedRandom)game.SourceRandomService).Trace.Count == before,
                "standalone original hop/callback/stance consumes zero additional RNG");
            PreSnapChecks.Transport(game.TakeSourceSoundVmDispatches(),ReadImpact(actual),"actual VM kind5 cinematic consumer");
        }
        internal static void RunPreSnapSameWake(FullKernelInputs actual)
        {
            foreach (int callerId in new[] { 3,6 })
            {
                GameSimulation game = PreSnapChecks.Create(actual);
                game.Model.SourceTick = 1000; game.Model.Phase = GamePhase.PreSnap;
                game.Model.PreSnapTicksRemaining = SourceClock.DefaultAutoHikeTicks-16;
                foreach (ActorState player in game.Model.Players)
                {
                    player.SourceMode = SourceActorMode.DefensiveBackPreSnap;
                    player.SourceStick = player.SourcePreviousStick = 24;
                    player.SourceNewButtons = player.SourceButtons = SourceDroneButtons.None;
                    player.AnimationState.Primary.AddMode(SourceAnimationModes.Uninterruptible);
                }
                game.Model.SourceStationPlayers[0] = callerId; game.RecomputeSourceMovementStations();
                int next = (callerId+1)%7;
                ActorState destination = game.Model.Players[next], caller = game.Model.Players[callerId];
                if (next < callerId) destination.AnimationState.Advance(destination,game.Model.SourceTick,game.SourceDraw);
                caller.SourceNewButtons = caller.SourceButtons = SourceDroneButtons.B;
                game.BeginSourceProcess("player_proc",callerId,callerId+1);
                game.ExecuteSourceMovementMode(caller,default(PlayerCommand));
                int ordinal = destination.AnimationState.Primary.FrameOrdinal;
                PreSnapChecks.Check(game.SourceMovementPressedButtons(destination,SourceButtons.A|SourceButtons.B|SourceButtons.C) ==
                    (SourceDroneButtons.A|SourceDroneButtons.C),"fake-B-external-edge-reinjection");
                destination.AnimationState.Advance(destination,game.Model.SourceTick,game.SourceDraw);
                PreSnapChecks.Check(destination.AnimationState.Primary.FrameOrdinal == ordinal,"immediate-entry-double-advance");
                game.Model.SourceTick++;
                PreSnapChecks.Check(game.SourceMovementPressedButtons(destination,SourceButtons.B) == SourceDroneButtons.B,
                    "later released-then-fresh B is never globally consumed");
            }
        }
    }
}
