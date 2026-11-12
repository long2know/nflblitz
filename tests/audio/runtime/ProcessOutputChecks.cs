using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;
using Blitz.SourcePlayer;

internal static partial class Program
{
    private static object processOutputEvidence;
    private sealed class OrderingInputs
    {
        private readonly string root;
        private readonly Dictionary<string, string> pins = new Dictionary<string, string>();
        private readonly Dictionary<string, string> entries = new Dictionary<string, string>();
        internal readonly SourceAnimationLibrary Animations;
        internal readonly SourceVector3[] Pivots;
        internal readonly SourcePlaybook Plays;
        internal readonly SourceTeamRosterLibrary Rosters;
        internal readonly SourceMovementRuntimeData Movement;
        internal readonly SourceAudioFunctionData Sound;
        internal readonly SourceC2CallbackData Callbacks;
        internal readonly SourcePossessionSoundData Speech;
        internal readonly SourceInterfaceData Hud;

        internal OrderingInputs(string root)
        {
            this.root = Path.GetFullPath(root);
            byte[] manifest = File.ReadAllBytes(Path.Combine(root, "manifest.json"));
            Check(PcmHash(manifest) == "150409eb5a4b76272747d332e6387d68452161bf395eafb8f409c1c3656ac2e6",
                "producer tests read the unchanged approved563 input ledger, not prepared replacement data");
            using (JsonDocument doc = JsonDocument.Parse(manifest))
            {
                foreach (JsonElement file in doc.RootElement.GetProperty("files").EnumerateArray())
                    pins.Add(file.GetProperty("path").GetString(), file.GetProperty("sha256").GetString());
                foreach (JsonProperty entry in doc.RootElement.GetProperty("entrypoints").EnumerateObject())
                    entries.Add(entry.Name, entry.Value.GetString());
            }
            Animations = SourceAnimationLibrary.Read(Read(entries["animations"]), path => Read("animation/" + path));
            SourceTeamResourceProfile.Contact282C2M1PreSnapSwitch.RequireLibrary(Animations);
            SourcePlayer player = GeneratedPlayerReader.Read(Encoding.UTF8.GetString(Read(entries["model"])),
                "models/plyr266.h", Encoding.UTF8.GetString(Read(entries["skeleton"])), "models/skeleton.h",
                new PlayerSymbolSelection("plyr266_limbs", "fbplyr_skel", "model266_limbs", "plyr266_textures"));
            var conversion = new PlayerCoordinateConversion(SignedSourceAxis.PositiveX, SignedSourceAxis.PositiveY,
                SignedSourceAxis.NegativeZ, SourceTriangleOrder.Renderer321);
            Pivots = player.Nodes.Select(node => {
                AssetVector3 pivot = conversion.Convert(node.BindPivot);
                return new SourceVector3(pivot.X, pivot.Y, pivot.Z);
            }).ToArray();
            Rosters = SourceTeamRosterLibrary.Read(Read(entries["rosters"]), pins[entries["rosters"]]);
            using (var reader = new StringReader(Encoding.UTF8.GetString(Read(entries["playbook"]))))
                Plays = SourcePlaybook.LoadConverted(reader);
            SourceInterfaceMovementData movement = SourceInterfaceMovementData.Read(Read(entries["movement"]), pins[entries["movement"]]);
            Movement = new SourceMovementRuntimeData(new SourceMovementData(movement.Constants, movement.Tables),
                movement.Friction, movement.Defense.Select(row => new SourceMovementDefenseRow(
                    row.Line, row.Back, row.Base, row.Range, row.Plow)).ToArray());
            Sound = SourceAudioFunctionData.Read(Read(entries["sound"]), GameSimulation.SourceSoundVmDataSha256);
            Callbacks = SourceInterfacePackagePlan.ReadPreSnapSwitchCallbacks(Read(entries["callbacks"]), Read(entries["hit"]));
            Speech = SourcePossessionSoundData.Read(Read(entries["possessionSound"]));
            Hud = SourceInterfaceData.Read(Read(entries["hud"]), SourceInterfacePackagePlan.HudSha256);
        }

        private byte[] Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, path));
            if (PcmHash(bytes) != pins[path]) throw new InvalidDataException("Changed approved source input: " + path);
            return bytes;
        }

        internal GameSimulation Create(Func<int, ISourceRandom> randomFactory = null)
        {
            var configuration = new SourceMatchConfiguration(SourceSimulationProfile.OriginalRelease57Hz,
                SourceStationLayout.FourStation, new SourceTeamSelection(0, Enumerable.Range(0, 7).ToArray()),
                new SourceTeamSelection(1, Enumerable.Range(0, 7).ToArray()),
                new[] { new SourceStationConfiguration(0, TeamId.Home, 3) }, TeamId.Away, SourceCaptainSlot.First);
            Func<int, ISourceRandom> factory = randomFactory ?? (seed => new SourceInjectedRandom(site => 17,
                new SourceRandomProvenance("producer-ordering/test",
                    "explicit unfiltered constant raw input; not target PRNG", "constant17", SourceRandomEvidence.RecordedOracle)));
            GameSimulation game = SourceC2Composition.CreatePreSnapSwitchSimulation(Animations, Pivots, Plays,
                configuration, Rosters, new SourcePresentationSettings(TeamId.Home, new[] { -1, -1, -1, -1 }, 0),
                Movement, new SourceMovementSettings(0, 0, false, 0, 0, false), Sound,
                SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(Callbacks),
                factory,
                compatibility: SourceGameplayCompatibility.Portable);
            game.BindSourcePossessionSoundData(Speech);
            game.SourceCameraHeadingProvider = (state, tick) => FullKernelInputs.SetControlledMovementCamera(game, state, tick);
            return game;
        }
    }

    private sealed class OrderingRig
    {
        internal readonly GameSimulation Game;
        internal readonly FixedStepBridge Bridge;
        internal readonly List<SourceProcessSoundCall> Calls = new List<SourceProcessSoundCall>();
        internal readonly List<SourceSoundVmDispatch> Vm = new List<SourceSoundVmDispatch>();
        internal readonly List<long> EmissionOrder = new List<long>();
        internal OrderingRig(GameSimulation game)
        {
            Game = game; Bridge = new FixedStepBridge(game);
            Game.SourceProcessSoundRaised += call => {
                CaptureVm();
                Check(call.OutputSequence > (EmissionOrder.Count == 0 ? 0 : EmissionOrder.Last()),
                    "actual executing process output follows all earlier native/VM source emissions");
                EmissionOrder.Add(call.OutputSequence); Calls.Add(call);
            };
            Bridge.AfterSimulationStep += CaptureVm;
        }
        private void CaptureVm()
        {
            foreach (SourceSoundVmDispatch dispatch in Game.TakeSourceSoundVmDispatches().Dispatches)
            {
                Check(dispatch.OutputSequence > (EmissionOrder.Count == 0 ? 0 : EmissionOrder.Last()),
                    "actual native/VM producer remains in the same common process-output order");
                Vm.Add(dispatch); EmissionOrder.Add(dispatch.OutputSequence);
            }
        }
        internal void Step(SourceStationInput station = default(SourceStationInput))
        {
            if (!station.IsSpecified) station = new SourceStationInput(0);
            long tick = Game.SourceTick;
            for (int host = 0; host < 3 && Game.SourceTick == tick; host++)
                Bridge.AdvanceSource(FixedStepBridge.StepSeconds, () => new SourceInputFrame(new[] { station }));
            Check(Game.SourceTick == tick + 1, "ordering rig advances one actual source wake, including host57/60 skips");
        }
        internal void Until(Func<bool> done, int limit = 4000)
        {
            for (int i = 0; i < limit && !done(); i++) Step();
            Check(done(), "bounded actual source process reached its output boundary: sourceTick=" + Game.SourceTick
                + ", phase=" + Game.State.Phase + ", calls=" + Calls.Count
                + ", last=" + (Calls.Count == 0 ? "none" : Calls.Last().Kind.ToString()));
        }
        internal void Restart()
        {
            Bridge.Restart();
            Calls.Clear(); Vm.Clear(); EmissionOrder.Clear();
            Bridge.AdvanceSource(FixedStepBridge.StepSeconds, () => new SourceInputFrame(new[] { new SourceStationInput(0) }));
        }
    }

    // Synthetic resource/input boundaries execute the real instruction bodies.
    // No plate pixels, native address, original PCM or full gameplay event is inferred.
    private sealed class OrderingInstructions : ISourceInstructionInputs, ISourceInstructionEffects,
        ISourceInstructionAdmissionState, ISourceKickoffInstructionChildInputs, ISourceKickoffInstructionChildEffects,
        ISourceKickoffInstructionPlayerButtons, ISourceKickoffCurrentSwitches, ISourceKickoffPlayerObjects,
        ISourceKickoffInstructionChildReturnBinding, ISourceKickoffInstructionFlashReturnBinding
    {
        private sealed class Plate : ISourceInstructionPlate { }
        private sealed class Text : ISourceInstructionText { }
        private sealed class Flasher : ISourceInstructionFlasher { }
        private readonly GameSimulation game;
        private int ordinal;
        internal int Flag, ChildSounds, Gate;
        internal bool ThrowSound;
        internal readonly List<SourceProcessSoundCall> PageSounds = new List<SourceProcessSoundCall>();
        internal OrderingInstructions(GameSimulation game) { this.game = game; }
        internal SourceInstructionInvocation Invocation()
        {
            ordinal = 0;
            return new SourceInstructionInvocation(unchecked((int)0xf0f0f0f0u),
                new SourceInstructionProcessIds(0x0f9b, 0x0502, 0x1400, 0x0f10), this, this);
        }
        internal void Bind()
        {
            game.BindSourceKickoffInstructionOwnedHelpers(Invocation(), this, 0x0f9a, 4, 1, 2, this, this,
                new SourceKickoffInstructionChildOperands(false, 0, 0, 0, 1), this,
                new SourceKickoffInstructionOwnedHelpers(0x10, 0x20, 0x40, SourceFireIds.FlashWhite, this, this, this), this);
        }
        public int ReadPlayerSwitches() { return 0x10; }
        public int ReadShowPlyselIdiotBox() { return Gate; }
        public int ReadTsec() { return 57; }
        public int ReadIsLowResolution() { return 0; }
        public int ReadIdiotBoxesShown() { return Flag; }
        public void WriteIdiotBoxesShown(int value) { Flag = value; }
        public int ReadPlayerStatus() { return 1; }
        public int ReadGamesPlayed(int slot) { return 0; }
        public ISourceInstructionPlate BeginPlate(string image, string texture, SourceInstructionPosition position) { return new Plate(); }
        public ISourceInstructionPlate BeginPlate(string image, string texture, SourceInstructionPoint point) { return new Plate(); }
        public void SetPlateWidthScale(ISourceInstructionPlate plate, float scale) { }
        public void SetPlateHeightScale(ISourceInstructionPlate plate, float scale) { }
        public void SetPlateId(ISourceInstructionPlate plate, string id) { }
        public void GeneratePlateVertices(ISourceInstructionPlate plate) { }
        public void SetTextFont(SourceInstructionFont font) { }
        public void SetStringId(int id) { }
        public void SetTextPosition(SourceInstructionPosition position) { }
        public void SetTextColor(SourceInstructionColor color) { }
        public ISourceInstructionText Print(SourceInstructionTextRequest request) { return new Text(); }
        public void StoreMessage(int slot, ISourceInstructionText text) { }
        public ISourceInstructionFlasher CreateTextFlasher(string name, int id, ISourceInstructionText first,
            ISourceInstructionText second, int third, int fourth) { return new Flasher(); }
        public void CreatePlateFlasher(string name, int id, ISourceInstructionPlate plate, int flashes, int delay, int fourth) { }
        public void PlaySound(SourceInstructionSound sound)
        {
            long serial = game.Model.SourceProcesses.Caller;
            var context = new SourceInstructionSoundContext(game.SourceTick, serial, ordinal++);
            Reject(() => game.TakeExecutingSourceInstructionSound(sound,
                new SourceInstructionSoundContext(game.SourceTick - 1, serial, context.CallOrdinal)), "stale instruction source tick is rejected");
            Reject(() => game.TakeExecutingSourceInstructionSound(sound,
                new SourceInstructionSoundContext(game.SourceTick, serial + 1, context.CallOrdinal)), "wrong instruction caller is rejected");
            Reject(() => game.TakeExecutingSourceInstructionSound(sound,
                new SourceInstructionSoundContext(game.SourceTick, serial, context.CallOrdinal + 1)), "wrong instruction subcall is rejected");
            Reject(() => game.TakeExecutingSourceKickoffInstructionSound(SourceKickoffInstructionChildSound.Ricochet),
                "page process cannot impersonate the source kickoff callback");
            Reject(() => game.TakeExecutingSourceInstructionSound(null, context), "unknown instruction sound operand is rejected");
            SourceProcessSoundCall call = game.TakeExecutingSourceInstructionSound(sound, context);
            Check(call.SourceTick == game.SourceTick && call.ProcessSerial == serial && call.Process == "instructions_proc",
                "typed callback returns the genuine current executing source process record");
            Reject(() => game.TakeExecutingSourceInstructionSound(sound, context), "instruction callback cannot allocate or observe duplicate output");
            PageSounds.Add(call);
            if (ThrowSound) throw new InvalidOperationException("actual source instruction recording failed");
        }
        public void DeleteStrings(int id, int mask) { }
        public void KillTextFlasher(ISourceInstructionFlasher flasher, int signal) { }
        public void DeletePlate(ISourceInstructionPlate plate) { }
        public int ReadFourPlayerVersion() { return 1; }
        public int ReadPlayerControl(int slot) { return game.Model.SourceStationPlayers[slot]; }
        public int ReadBallCarrier() { return game.Model.CarrierId; }
        public int ReadPlayerTeam(int player) { return (int)game.Model.Players[player].Team; }
        public int ReadOffensiveSide() { return (int)game.Model.Possession; }
        public int ReadCurrentButtons(int player) { return (int)game.Model.Players[player].SourceButtons; }
        public void WriteCurrentButtons(int player, int value) { game.Model.Players[player].SourceButtons = (SourceDroneButtons)value; }
        public int ReadOldButtons(int player) { return (int)game.Model.Players[player].SourceOldButtons; }
        public void WriteOldButtons(int player, int value) { game.Model.Players[player].SourceOldButtons = (SourceDroneButtons)value; }
        public uint ReadCurrentSwitchWord() { return 0x10; }
        public void SetTextZIncrement(float increment) { }
        public void SetPlateZAngle(ISourceInstructionPlate plate, float angle) { }
        public void SetPlateHScale(ISourceInstructionPlate plate, float scale) { }
        public void SetPlateObjectId(ISourceInstructionPlate plate, string id, int slot) { }
        public void SetTextFont(SourceKickoffInstructionChildFont font) { }
        public void SetTextColor(SourceKickoffInstructionChildColor color) { }
        public void SetTextPosition(SourceInstructionPoint point) { }
        public void Print(SourceKickoffInstructionChildTextRequest request) { }
        public void PlaySound(SourceKickoffInstructionChildSound sound)
        {
            SourceProcessSoundCall call = game.TakeExecutingSourceKickoffInstructionSound(sound);
            Check(call.Kind == SourceProcessSoundKind.KickoffInstruction && call.Process == "plr_ko_idiot_box_proc"
                && call.SourceTick == game.SourceTick && call.ProcessSerial == game.Model.SourceProcesses.Caller
                && call.Bank == "players" && call.PlaylistId == 148 && call.Volume == 115 && call.Pan == 127
                && call.PriorityWord == 1, "actual source kickoff child retains all original sound operands and live identity");
            Reject(() => game.TakeExecutingSourceKickoffInstructionSound(sound), "kickoff callback cannot observe duplicate output");
            ChildSounds++;
        }
        public void DeleteStrings(int id, uint mask) { }
        public void DeletePlateClass(string id, int slot, uint mask) { }
        public SourceKickoffInstructionChildReturnAction ReturnFromSourceBody(SourceKickoffInstructionChildInvocation invocation,
            SourceKickoffInstructionChildReturnReason reason) { return SourceKickoffInstructionChildReturnAction.ExternallyRetire; }
        public SourceKickoffInstructionFlashReturnAction ReturnFromSourceBody(SourceKickoffInstructionFlashInvocation invocation)
        { return SourceKickoffInstructionFlashReturnAction.ExternallyRetire; }
        public ISourceKickoffPlayerObject ReadPlayerBlock(int player) { return new PlayerObject(game.Model.Players[player]); }
        private sealed class PlayerObject : ISourceKickoffPlayerObject
        {
            private readonly ActorState actor;
            internal PlayerObject(ActorState actor) { this.actor = actor; }
            public uint ReadFlags() { return unchecked((uint)actor.SourceRawFlags); }
            public void WriteFlags(uint flags) { actor.SourceRawFlags = unchecked((int)flags); }
        }
    }

    private static void ActualProcessOutputs(string dataRoot)
    {
        int start = assertions;
        var inputs = new OrderingInputs(dataRoot);
        var rig = new OrderingRig(inputs.Create());
        var effects = new OrderingInstructions(rig.Game);
        effects.Bind();
        Reject(() => rig.Game.TakeExecutingSourceKickoffInstructionSound(SourceKickoffInstructionChildSound.Ricochet),
            "out-of-process kickoff sound cannot manufacture a common ordinal");
        rig.Until(() => effects.ChildSounds == 1);
        Check(rig.Calls.Count(c => c.Kind == SourceProcessSoundKind.KickoffInstruction) == 1,
            "natural original kickoff catch executes and stamps the real child sound once");
        SourceProcessSoundCall naturalKickoff = rig.Calls.Single(c => c.Kind == SourceProcessSoundKind.KickoffInstruction);
        rig.Game.InvokeSourceInstructions(effects.Invocation());
        rig.Until(() => rig.Game.SourceInstructionProgram.Stage == SourceInstructionStage.Fallthrough);
        Check(effects.Flag == 2 && effects.PageSounds.Count == 8,
            "actual four-page process preserves gate-controlled tick then page output and source flag completion");
        for (int i = 0; i < effects.PageSounds.Count; i += 2)
        {
            SourceProcessSoundCall tick = effects.PageSounds[i], page = effects.PageSounds[i + 1];
            Check(tick.Kind == SourceProcessSoundKind.InstructionTick && tick.Bank == "cursors" && tick.PlaylistId == 17
                && tick.Volume == 115 && tick.Pan == 127 && tick.PriorityWord == 1 && tick.SubcallOrder == 0
                && page.Kind == SourceProcessSoundKind.InstructionPageAdvance && page.Bank == "players"
                && page.PlaylistId == 148 && page.Volume == 160 && page.Pan == 127 && page.PriorityWord == 0
                && page.SubcallOrder == 1 && page.SourceTick == tick.SourceTick && page.DispatchOrder == tick.DispatchOrder
                && page.OutputSequence == tick.OutputSequence + 1,
                "actual direct tick and page call stamp adjacent common ordinals inside one executing source wake");
        }
        Check(rig.EmissionOrder.First() == 1 && rig.EmissionOrder.Distinct().Count() == rig.EmissionOrder.Count,
            "fresh actual Game factory starts the shared source producer once without duplicate ordinals");
        Check(rig.Game.TakeSourceProcessSoundCalls().SequenceEqual(rig.Calls)
            && rig.Game.TakeSourceProcessSoundCalls().Count == 0,
            "actual actorless producer queue retains emission identity and drains once without sorting");
        SourceProcessSoundCall retained = effects.PageSounds.Last();
        SourceProcessSoundCall[] originalPages = effects.PageSounds.ToArray();
        int nativeCount = rig.Vm.Count(v => !v.HasOriginalAnimationCue);
        long previous = rig.EmissionOrder.Last();
        rig.Restart();
        effects.PageSounds.Clear();
        rig.Game.InvokeSourceInstructions(effects.Invocation());
        rig.Until(() => effects.PageSounds.Count == 2);
        Check(effects.PageSounds[0].OutputSequence > previous && retained.OutputSequence <= previous,
            "same Game restart clears pending output but never reuses or rewinds its common producer counter");
        var fresh = new OrderingRig(inputs.Create());
        var freshEffects = new OrderingInstructions(fresh.Game);
        fresh.Game.InvokeSourceInstructions(freshEffects.Invocation());
        fresh.Until(() => freshEffects.PageSounds.Count == 2);
        Check(fresh.EmissionOrder.First() == 1 && freshEffects.PageSounds[0].OutputSequence < effects.PageSounds[0].OutputSequence,
            "new Game factory is a genuinely new source producer, not a same-producer counter reset");

        object menuEvidence = ActualTimerSpeechAndRandom(inputs);
        ActualProcessPlayback(rig, fresh, retained);
        object emitterEvidence = ActualEmitterInterleave(inputs);
        SourceProcessSoundCall[] pendingFresh = fresh.Calls.ToArray();
        fresh.Game.Restart();
        Check(fresh.Game.TakeSourceProcessSoundCalls().Count == 0 && pendingFresh.All(c => c.OutputSequence > 0),
            "actual restart discards unsent prior process outputs without changing retained immutable records");
        var gated = new OrderingRig(inputs.Create());
        var gatedEffects = new OrderingInstructions(gated.Game) { Gate = 1 };
        gated.Game.InvokeSourceInstructions(gatedEffects.Invocation());
        gated.Until(() => gated.Game.SourceInstructionProgram.Stage == SourceInstructionStage.Fallthrough);
        Check(gatedEffects.Flag == 2 && gatedEffects.PageSounds.Count == 4
            && gatedEffects.PageSounds.All(c => c.Kind == SourceProcessSoundKind.InstructionPageAdvance),
            "actual nonzero instruction gate suppresses direct tick without reserving or fabricating its output");
        var failed = new OrderingRig(inputs.Create());
        var failedEffects = new OrderingInstructions(failed.Game) { ThrowSound = true };
        failed.Game.InvokeSourceInstructions(failedEffects.Invocation());
        Reject(() => failed.Until(() => failedEffects.PageSounds.Count != 0),
            "actual instruction output failure propagates from its executing process");
        SourceProcessSoundCall[] partial = failed.Game.TakeSourceProcessSoundCalls().ToArray();
        long consumed = failed.Game.Model.SourceRandomDraws;
        Reject(() => failed.Step(), "failed actual instruction callback cannot retry its source output");
        Check(partial.Length == 1 && partial[0].OutputSequence > 0
            && failed.Game.TakeSourceProcessSoundCalls().Count == 0 && failed.Game.Model.SourceRandomDraws == consumed,
            "failed source process retains exactly its already emitted call without duplicate IDs, RNG rollback or retry");
        var missingBank = new OrderingRig(inputs.Create());
        var missingEffects = new OrderingInstructions(missingBank.Game);
        missingBank.Game.SourceProcessSoundRaised += call => {
            if (call.Kind == SourceProcessSoundKind.InstructionTick) missingBank.Game.Model.SourceCurrentPlayerBank = null;
        };
        missingBank.Game.InvokeSourceInstructions(missingEffects.Invocation());
        Reject(() => missingBank.Until(() => missingEffects.PageSounds.Count > 1),
            "missing actual source-selected instruction bank fails rather than substituting players or decoded output");
        Check(missingBank.Calls.Count == 1 && missingBank.Calls[0].Kind == SourceProcessSoundKind.InstructionTick,
            "missing source bank retains only the actual preceding direct call, not a fabricated page sound");
        processOutputEvidence = new {
            naturalKickoff = naturalKickoff, originalInstructionCalls = originalPages,
            retainedInstruction = retained, sameGameFirst = effects.PageSounds.First(), freshGameFirst = fresh.Calls.First(),
            priorCommonOrdinal = previous, freshCommonFirst = fresh.EmissionOrder.First(), currentSourceNativeDispatches = nativeCount,
            menus = menuEvidence, managedEmitterFixture = emitterEvidence,
            failedCall = partial[0], originalAudioAdmitted = false
        };
        actualAssertions += assertions - start;
    }

    private static object ActualTimerSpeechAndRandom(OrderingInputs inputs)
    {
        var rig = new OrderingRig(inputs.Create());
        var selector = new SourceCabinetPlaySelection(inputs.Hud);
        var cabinet = new List<SourceCabinetMenuEvent>();
        var timers = new List<SourceSelectionTimerSound>();
        var speech = new List<SourcePossessionSpeech>();
        selector.EventRaised += cabinet.Add;
        rig.Game.SourceSelectionTimerSoundRaised += timers.Add;
        rig.Game.SourcePossessionSpeechRaised += speech.Add;
        rig.Game.SourceStationInputTransform = selector.ApplyAtSourceTick;
        bool messageCreated = false;
        rig.Game.SourceCameraHeadingProvider = (state, tick) => {
            // Labelled CAMBOT message-admission fixture, not a fabricated interception.
            if (!messageCreated) { rig.Game.SourceCameraProcesses.CreateMessage(8); messageCreated = true; }
            return FullKernelInputs.SetControlledMovementCamera(rig.Game, state, tick);
        };
        rig.Until(() => rig.Game.State.Phase == GamePhase.DeadBall && !rig.Game.C2State.ResultPending);
        Check(speech.Count == 1 && speech[0].OutputSequence > 0 && speech[0].Output.Process == "show_message_proc"
            && speech[0].Output.SourceTick == speech[0].SourceTick && speech[0].Output.Bank == speech[0].Bank
            && speech[0].Output.PlaylistId == speech[0].PlaylistId && speech[0].Output.Volume == speech[0].Volume
            && speech[0].Output.Pan == speech[0].Pan && speech[0].Output.PriorityWord == speech[0].PriorityWord
            && speech[0].Output.RandomCalls == 1, "actual message process stamps the original selected possession speech and raw operands");
        var random = (SourceInjectedRandom)rig.Game.SourceRandomService;
        Check(random.Trace.Count(d => d.Site.SourceIdentity == "sounds.c:intercepted_ball_sp:831") == 1,
            "possession speech keeps exactly one original bound5 draw without reselection or retry");
        rig.Step(new SourceStationInput(0, command: new SourceCommand(SourceAction.Continue)));
        Check(rig.Game.State.Phase == GamePhase.PlaySelect, "public Continue enters the actual selection-timer producer");
        long entry = rig.Game.SourceTick;
        while (rig.Game.SourceTick < entry + 21) rig.Step();
        Check(timers.Count == 1 && timers[0].OutputSequence > 0 && timers[0].Output.Process == "timedown_play_select"
            && timers[0].Output.SourceTick == timers[0].SourceTick && timers[0].Output.PlaylistId == 36
            && timers[0].Output.Volume == 235 && timers[0].Output.Pan == 127 && timers[0].Output.PriorityWord == 5,
            "actual timer process stamps the original first beep while it executes");
        SourceCabinetStick left = new SourceCabinetQuantizer(inputs.Hud.Input).SevenLevel(-3, 0);
        rig.Step(new SourceStationInput(0, left.StickX, left.StickZ,
            digitalCurrent: (SourceDigitalDirections)left.DigitalDirections));
        rig.Step();
        rig.Step(new SourceStationInput(0, left.StickX, left.StickZ, held: SourceButtons.C, pressed: SourceButtons.C,
            digitalCurrent: (SourceDigitalDirections)left.DigitalDirections));
        rig.Until(() => selector.Selected(TeamId.Home), 100);
        SourceCabinetMenuEvent[] audible = cabinet.Where(c => c.IsRandomSelection && c.SoundCall.HasValue).ToArray();
        Check(audible.Length > 2 && audible.All(c => c.OutputSequence > 0 && c.Output != null
            && c.Output.Kind == SourceProcessSoundKind.CabinetSelection && c.Output.Process == "game_proc"
            && c.Output.SourceTick == c.SourceTick && c.Output.Bank == c.SoundBank && c.Output.PlaylistId == c.SoundCall
            && c.Output.Volume == c.SoundVolume && c.Output.Pan == c.SoundPan && c.Output.PriorityWord == c.SoundPriority),
            "actual cabinet random gesture/controller sound records carry the common producer order and unchanged raw operands");
        Check(random.Trace.Count(d => d.Site.SourceIdentity.StartsWith("playsel.c:play_select_cntrl:", StringComparison.Ordinal)) == 30,
            "ordering consumes no additional random-selection draws: fifteen original direction/button pairs remain");
        Check(timers[0].OutputSequence < audible[0].OutputSequence && audible.Zip(audible.Skip(1),
            (a, b) => a.OutputSequence < b.OutputSequence).All(value => value),
            "real timer and random source callbacks retain their actual process/subcall emission order");
        Reject(() => ((ISourceCabinetSelectionSoundProducer)rig.Game).SubmitSourceCabinetSelectionSound(
            rig.Game.SourceTick, 0, TeamId.Home, SourceCabinetSelectionSound.Moved),
            "retained cabinet producer cannot be used outside its executing input callback");
        Check(new SourceSelectionTimerSound(1, 2, 9).OutputSequence == 0
            && new SourcePossessionSpeech(1, 2, 3, "unbound", 0, 1, 1, 1, 1).OutputSequence == 0,
            "old constructor metadata stays explicitly unbound rather than manufacturing positive output IDs");
        return new {
            timer = timers[0], speech = speech[0],
            randomSounds = audible.Select(c => new { c.Kind, c.SourceTick, c.OutputSequence, c.Output }).ToArray(),
            randomControllerDraws = 30, possessionSpeechDraws = 1,
            nativeDispatches = rig.Vm.Count(v => !v.HasOriginalAnimationCue)
        };
    }

    private static void InvokeSourceEmitter(GameSimulation game, string method, params object[] arguments)
    {
        try { typeof(GameSimulation).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, arguments); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }

    private static object ActualEmitterInterleave(OrderingInputs inputs)
    {
        GameSimulation game = inputs.Create();
        ActorState actor = game.Model.Players[3];
        SourceActorMode mode = actor.SourceMode;
        // Explicit managed process/emitter fixture, not default-audibles admission
        // or a claimed public-input OOB play. No output IDs are supplied by the test.
        game.Model.SourceTick = 1;
        game.Model.SourceProcesses.BeginTick(1);
        Check(game.Model.SourceProcesses.Visit(2 + actor.Id), "actual player process enters the declared emitter fixture");
        game.Model.SourceAudibleState = new SourceAudibleState();
        InvokeSourceEmitter(game, "BeginSourceProcess", "player_proc", actor.Id, 0x300 + actor.Id);
        long draws = game.Model.SourceRandomDraws;
        InvokeSourceEmitter(game, "SubmitSourceAudibleSound", actor, 100, 11278);
        actor.SourceMode = SourceActorMode.Receiver;
        InvokeSourceEmitter(game, "SourceC2ImmediateWhistle", actor);
        InvokeSourceEmitter(game, "SubmitSourceAudibleSound", actor, 101, 11342);
        actor.SourceMode = mode;
        game.Model.SourceProcesses.Complete();
        SourceDirectSoundCall[] direct = game.TakeSourceDirectSoundCalls().ToArray();
        game.Model.SourceAudibleState = null;
        SourceSoundVmDispatch native = game.TakeSourceSoundVmDispatches().Dispatches.Single();
        Check(direct.Length == 2 && native.Status == SourceSoundVmStatus.Resolved && !native.HasOriginalAnimationCue
            && direct[0].OutputSequence + 1 == native.OutputSequence && native.OutputSequence + 1 == direct[1].OutputSequence
            && direct[0].ProcessSerial == 2 + actor.Id && direct[1].ProcessSerial == 2 + actor.Id
            && native.Result.RandomCalls == 1 && game.Model.SourceRandomDraws == draws + native.Result.RandomCalls,
            "actual direct/native/direct emitters interleave the existing counter once with only the original whistle draw and no supplied DTO ordinals");
        var rig = new OrderingRig(game);
        var effects = new OrderingInstructions(game);
        game.InvokeSourceInstructions(effects.Invocation());
        rig.Until(() => effects.PageSounds.Count == 2);
        Check(effects.PageSounds[0].OutputSequence > direct[1].OutputSequence,
            "new executing instruction boundary continues the same actual direct/native producer, not an independent counter");
        var tuples = direct.Select(c => new { c.Bank, c.PlaylistId }).Concat(native.Result.Calls.Select(c => new { c.Bank, c.PlaylistId }))
            .Concat(effects.PageSounds.Select(c => new { c.Bank, c.PlaylistId })).Distinct().ToArray();
        var banks = tuples.GroupBy(c => c.Bank).Select(group =>
            PcmInput(PcmBank(group.Key, 5, 1, group.Select(c => c.PlaylistId).OrderBy(id => id).ToArray()), group.Key)).ToArray();
        var sink = new PcmSink();
        var playback = new SourceAudioPlayback(new SourceAudioPlaybackAdmission("generated-interleaved-emitter-fixture",
            "existing generated non-original PCM only, not original bank authorization or hearing",
            banks, tuples.Select(c => new SourceAudioProgramRequirement(c.Bank, c.PlaylistId)).ToArray()), sink, 57);
        var transport = new SourceSoundVmTransport();
        transport.BindOutput(playback);
        transport.Drain(Batch(native));
        playback.Consume(direct[0]);
        transport.DeliverNextNative(native);
        playback.Consume(direct[1]);
        foreach (SourceProcessSoundCall call in effects.PageSounds) playback.Consume(call);
        transport.CompletePresentationBatch(); playback.Flush();
        long[] observed = sink.Commands.Where(c => c.GetProperty("outputSequence").GetInt64() > 0)
            .Select(c => c.GetProperty("outputSequence").GetInt64()).Distinct().ToArray();
        Check(observed.SequenceEqual(new[] { direct[0].OutputSequence, native.OutputSequence, direct[1].OutputSequence,
            effects.PageSounds[0].OutputSequence, effects.PageSounds[1].OutputSequence }),
            "existing C6 transport consumes actual direct/native/new instruction records in producer order without fake DTO sorting");
        return new { direct = direct.Select(c => new { c.OutputSequence, c.SourceTick, c.ProcessSerial, c.Process, c.PlaylistId }),
            native = new { native.OutputSequence, native.C2Context.SourceTick, native.C2Context.Process },
            instructions = effects.PageSounds, suppliedOutputOrdinals = false, targetAbiOrAudibleClaimed = false };
    }

    private static void ActualProcessPlayback(OrderingRig restarted, OrderingRig fresh, SourceProcessSoundCall retained)
    {
        SourceProcessSoundCall[] calls = new[] { retained }.Concat(restarted.Calls).Concat(fresh.Calls).ToArray();
        SourceAudioBankInput[] banks = calls.GroupBy(c => c.Bank).Select(group =>
            PcmInput(PcmBank(group.Key, 5, 1, group.Select(c => c.PlaylistId).Distinct().OrderBy(id => id).ToArray()), group.Key)).ToArray();
        var requirements = calls.Select(c => new { c.Bank, c.PlaylistId }).Distinct()
            .Select(c => new SourceAudioProgramRequirement(c.Bank, c.PlaylistId)).ToArray();
        var sink = new PcmSink();
        var playback = new SourceAudioPlayback(new SourceAudioPlaybackAdmission("generated-process-order-fixture",
            "existing synthetic PCM generator only; NOT original bank authorization or sound qualification", banks, requirements), sink, 57);
        playback.Consume(retained); playback.Flush();
        long priorCommand = sink.Commands.Last().GetProperty("sequence").GetInt64();
        long priorVoice = sink.Kind("play").Last().GetProperty("voiceId").GetInt64();
        playback.Reset(1);
        foreach (SourceProcessSoundCall call in restarted.Calls) playback.Consume(call);
        playback.Flush();
        Check(playback.Failure == null && sink.Commands.Last().GetProperty("sequence").GetInt64() > priorCommand
            && sink.Kind("play").Last().GetProperty("voiceId").GetInt64() > priorVoice,
            "existing strict C6 accepts genuine same-Game producer outputs after restart with monotonic command/voice identities");
        playback.Reset(2);
        Reject(() => playback.Consume(retained), "existing C6 same-producer restart rejects retained actual process-output replay");
        playback.Reset(3, newProducer: true);
        long draws = fresh.Game.Model.SourceRandomDraws;
        foreach (SourceProcessSoundCall call in fresh.Calls) playback.Consume(call);
        playback.Flush();
        Check(playback.Failure == null && fresh.Game.Model.SourceRandomDraws == draws && sink.Samples.Count == 1,
            "existing C6 new-producer reset accepts actual fresh Game ordinals without reupload, reselection or RNG");
        Reject(() => playback.Consume(fresh.Calls.Last()), "existing C6 rejects duplicate genuine instruction output");
        var missing = new SourceAudioPlayback(PcmAdmission(), new PcmSink(), 57);
        Reject(() => missing.Consume(fresh.Calls.First()), "ordered actual instruction output still requires its exact admitted bank/program");
        Check(missing.Failure.Contains("cursors:17") && !missing.Failure.Contains("PRODUCER_ORDER_MISSING"),
            "source ordering repair never converts absent original program data into decoded or muted success");
    }
}
