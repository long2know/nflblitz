using System.Text.Json;
using System.Collections.ObjectModel;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

namespace Blitz.SourcePlayer;

public sealed class SourceDirectSoundDelivery
{
    public long DeliverySequence { get; }
    public SourceDirectSoundCall Call { get; }
    public string Consumer => "PlayerHost ordered direct-sound recording consumer";
    public bool PcmOrDspPlayback => false;
    internal SourceDirectSoundDelivery(long sequence, SourceDirectSoundCall call)
    { DeliverySequence = sequence; Call = call; }
}

public sealed class SourceDirectSoundRecordingConsumer : ISourceDirectSoundOutputConsumer
{
    private readonly bool completeOriginal;
    public SourceDirectSoundRecordingConsumer(bool completeOriginal = false) { this.completeOriginal = completeOriginal; }
    private readonly Queue<SourceDirectSoundDelivery> receipts = new();
    private SourceDirectSoundCall previous;
    private long lastSequence;
    private long lastOutputSequence;
    public long DeliveredCount { get; private set; }
    public ReadOnlyCollection<SourceDirectSoundDelivery> Receipts => Array.AsReadOnly(receipts.ToArray());

    public void Consume(SourceDirectSoundCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        DataCatalog.Require(call.Sequence == lastSequence + 1 &&
            (previous == null || call.SourceTick > previous.SourceTick ||
                call.SourceTick == previous.SourceTick && (call.DispatchOrder > previous.DispatchOrder ||
                    call.DispatchOrder == previous.DispatchOrder && call.SubcallOrder > previous.SubcallOrder)),
            "SOURCE_DIRECT_SOUND_ORDER: missing, duplicate or reordered source direct call.");
        DataCatalog.Require(call.OutputSequence > lastOutputSequence,
            "SOURCE_DIRECT_SOUND_OUTPUT_SEQUENCE: a genuine positive increasing common producer ordinal is required.");
        DataCatalog.Require(call.Bank == (completeOriginal ? "cursors2" : "cursors") && call.PlaylistId is >= 100 and <= 102 &&
            call.Volume == 235 && call.Pan == (call.Team == TeamId.Home ? 0u : 255u) &&
            call.PriorityWord == 15 && call.RandomCalls == 0 && !call.PcmOrDspPlayback &&
            call.SourcePath == "player.c" && call.FunctionSymbol == "audible_check",
            "SOURCE_DIRECT_SOUND_FIELDS: the actual direct audible call is not a VM cue or invented sound.");
        var receipt = new SourceDirectSoundDelivery(DeliveredCount + 1, call);
        receipts.Enqueue(receipt);
        if (receipts.Count > 128) receipts.Dequeue();
        previous = call;
        lastSequence = call.Sequence;
        lastOutputSequence = call.OutputSequence;
        DeliveredCount++;
    }

    internal void Reset(bool newProducer = false)
    {
        receipts.Clear();
        previous = null;
        DeliveredCount = 0;
        if (newProducer) { lastSequence = 0; lastOutputSequence = 0; }
    }
}

public enum SourceSoundOutputKind { Direct, Vm, Process, Control }

public sealed class SourceSoundOutputOrder
{
    private readonly Queue<long> pending = new();
    private long lastAccepted;
    private string failure;
    public int PendingCount => pending.Count;
    public long NextSequence => pending.Count == 0 ? 0 : pending.Peek();

    public ReadOnlyCollection<(bool Direct, int Index, long OutputSequence)> Begin(
        IReadOnlyList<long> direct, IReadOnlyList<long> vm)
        => Array.AsReadOnly(Begin(direct, vm, Array.Empty<long>())
            .Select(value => (value.Kind == SourceSoundOutputKind.Direct, value.Index, value.OutputSequence)).ToArray());

    public ReadOnlyCollection<(SourceSoundOutputKind Kind, int Index, long OutputSequence)> Begin(
        IReadOnlyList<long> direct, IReadOnlyList<long> vm, IReadOnlyList<long> process)
        => Begin(direct, vm, process, Array.Empty<long>());

    public ReadOnlyCollection<(SourceSoundOutputKind Kind, int Index, long OutputSequence)> Begin(
        IReadOnlyList<long> direct, IReadOnlyList<long> vm, IReadOnlyList<long> process, IReadOnlyList<long> controls)
    {
        Healthy();
        Require(pending.Count == 0, "a preceding output batch has not completed");
        var streams = new[] { direct, vm, process, controls };
        foreach (var values in streams) Validate(values);
        var merged = new List<(SourceSoundOutputKind Kind, int Index, long OutputSequence)>();
        var positions = new int[streams.Length];
        while (true)
        {
            int selected = -1;
            long next = 0;
            for (int stream = 0; stream < streams.Length; stream++)
            {
                if (positions[stream] == streams[stream].Count) continue;
                long value = streams[stream][positions[stream]];
                Require(selected < 0 || value != next, "duplicate common producer ordinal across direct/VM/process queues");
                if (selected < 0 || value < next) { selected = stream; next = value; }
            }
            if (selected < 0) break;
            merged.Add(((SourceSoundOutputKind)selected, positions[selected]++, next));
        }
        foreach (var entry in merged) pending.Enqueue(entry.OutputSequence);
        if (merged.Count != 0) lastAccepted = merged[^1].OutputSequence;
        return Array.AsReadOnly(merged.ToArray());
    }

    public void Complete(long outputSequence)
    {
        Healthy();
        Require(pending.Count != 0 && pending.Peek() == outputSequence,
            "consumer completion is missing, duplicated or out of producer order");
        pending.Dequeue();
    }

    public void RequireComplete()
    {
        Healthy();
        Require(pending.Count == 0, "a genuine producer output has no completed consumer/observation boundary");
    }

    internal void ResetPending(bool newProducer = false)
    {
        pending.Clear();
        failure = null;
        if (newProducer) lastAccepted = 0;
    }

    private void Validate(IReadOnlyList<long> values)
    {
        Require(values != null, "missing producer ordinal list");
        long previous = lastAccepted;
        foreach (long value in values)
        {
            Require(value > 0 && value > previous,
                "missing, duplicate, stale or out-of-order common producer ordinal");
            previous = value;
        }
    }

    private void Healthy()
    {
        if (failure != null) throw new InvalidDataException(failure);
    }

    private void Require(bool condition, string detail)
    {
        if (condition) return;
        failure = "SOURCE_SOUND_OUTPUT_SEQUENCE: " + detail + ". Restart is required.";
        throw new InvalidDataException(failure);
    }
}

public sealed class SourceRenderActorReceipt
{
    public SourceActorPoseObservation PoseObservation { get; }
    public int ActorId => PoseObservation.ActorId;
    public int JointOffset => ActorId * SourceTiming.JointCount * 16;
    public int ModelId { get; }
    public int AppearanceId { get; }
    public SourceVector3 WorldRoot { get; }

    public SourceRenderActorReceipt(SourceActorPoseObservation poseObservation, SourceVector3 worldRoot,
        int modelId, int appearanceId)
    {
        PoseObservation = poseObservation ?? throw new ArgumentNullException(nameof(poseObservation));
        WorldRoot = worldRoot; ModelId = modelId; AppearanceId = appearanceId;
    }
}

public sealed class SourceRenderFrameReceipt
{
    public long Epoch { get; }
    public long Frame { get; }
    // This is the existing Frames ordinal of the sole Present, not a new successful-call counter.
    public long PresentSequence => Frame;
    public long PreviousHostTick { get; }
    public long CurrentHostTick { get; }
    public long? PreviousSourceTick { get; }
    public long? CurrentSourceTick { get; }
    public int? PreviousSourceTicksPerSecond { get; }
    public int? CurrentSourceTicksPerSecond { get; }
    public float RenderInterpolation01 { get; }
    public ReadOnlyCollection<SourceRenderActorReceipt> Actors { get; }
    public ReadOnlyCollection<double> Joints { get; }
    public ReadOnlyCollection<int> ModelIds { get; }
    public ReadOnlyCollection<int> AppearanceIds { get; }
    public string Status { get; }

    public SourceRenderFrameReceipt(long epoch, long frame, long previousHostTick, long currentHostTick,
        long? previousSourceTick, long? currentSourceTick, int? previousSourceTicksPerSecond,
        int? currentSourceTicksPerSecond, float renderInterpolation01, SourceRenderActorReceipt[] actors,
        double[] joints, int[] modelIds, int[] appearanceIds, string status)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(joints);
        ArgumentNullException.ThrowIfNull(modelIds);
        ArgumentNullException.ThrowIfNull(appearanceIds);
        if (actors.Length == 0 || actors.Length != modelIds.Length || actors.Length != appearanceIds.Length ||
            joints.Length != actors.Length * SourceTiming.JointCount * 16)
            throw new ArgumentException("A render receipt requires the same actor-indexed joint/model/appearance payload.");
        for (int i = 0; i < actors.Length; i++)
        {
            SourceRenderActorReceipt actor = actors[i];
            if (actor == null || actor.ActorId != i || actor.ModelId != modelIds[i] ||
                actor.AppearanceId != appearanceIds[i] || actor.PoseObservation.RenderInterpolation01 != renderInterpolation01)
                throw new ArgumentException("A render receipt must bind each actual pose to its outgoing actor slot and render alpha.");
        }
        Epoch = epoch; Frame = frame; PreviousHostTick = previousHostTick; CurrentHostTick = currentHostTick;
        PreviousSourceTick = previousSourceTick; CurrentSourceTick = currentSourceTick;
        PreviousSourceTicksPerSecond = previousSourceTicksPerSecond; CurrentSourceTicksPerSecond = currentSourceTicksPerSecond;
        RenderInterpolation01 = renderInterpolation01; Status = status;
        Actors = Array.AsReadOnly((SourceRenderActorReceipt[])actors.Clone());
        Joints = Array.AsReadOnly((double[])joints.Clone());
        ModelIds = Array.AsReadOnly((int[])modelIds.Clone());
        AppearanceIds = Array.AsReadOnly((int[])appearanceIds.Clone());
    }
}

public sealed class PlayerHost
{
    public const string RuntimeFlavor = "portable C# browser preview (not a Unity player build)";
    private readonly IRenderSink sink;
    private readonly SourceCabinetQuantizer quantizer;
    private SourceCabinetInputBuffer input;
    private readonly Func<int, ISourceRandom> randomFactory;
    private SourceCabinetPlaySelection selection;
    private readonly SourceHudComposer hud;
    private readonly SourceHudStatusAnimation hudClock = new();
    private readonly Queue<SourceCommand>[] commands = { new(), new(), new(), new() };
    private readonly Queue<object> events = new();
    private readonly Queue<object> contacts = new();
    private readonly Queue<object> dustCues = new();
    private readonly Queue<object> hopSounds = new();
    private readonly SourceSoundVmTransport hopSoundTransport = new();
    private readonly SourceDirectSoundRecordingConsumer directSoundRecording = new();
    private readonly ISourceDirectSoundOutputConsumer directSoundOutput;
    private readonly SourceSoundOutputOrder soundOutputOrder = new();
    private readonly Queue<(SourceDirectSoundCall Direct, SourceSoundVmDispatch Vm, SourceProcessSoundCall Process,
        SourceProcessSoundControl Control)> pendingSoundOutputs = new();
    private readonly Dictionary<SourceProcessSoundCall, PortableInstructionSound> instructionSoundObservations =
        new(ReferenceEqualityComparer.Instance);
    private readonly Queue<SourceProcessSoundCall> processSoundReceipts = new();
    public long BootSoundOutputsDelivered { get; private set; }
    public long SoundControlsDelivered { get; private set; }
    private bool bootAudioAwaitingContext;
    private ReadOnlyCollection<SourceBootSoundOutput> preparedBootSounds;
    private SourcePreGameSoundService preGameSounds;
    private SourcePreGameSoundHandoff gameSoundHandoff;
    private Func<int, ISourceRandom> cabinetRandomFactory;
    private bool initialPreGameHandoff;
    private double preGameAccumulator;
    private readonly SourceRealtimeFrameClock realtimeClock = new();
    private readonly SourceObservedInputTimeline observedInputs = new();
    private double observedHostTime, lastObservedFrameTime, preGameObservedSeconds;
    private long preGameInputOriginTick, gameInputOriginTick;
    private bool observedHostClockBound;
    private double pauseCarrySeconds;
    private bool resumeBridgeReady, preGameNeedsPhysicalCapture;
    private readonly Queue<(double[] Axes, int[] Buttons)> pausedPhysicalObservations = new();
    private bool resumeAwaitingPhysicalObservations;
    public long PreGameSoundCallsDelivered { get; private set; }
    public long PreGameSoundControlsDelivered { get; private set; }
    public SourcePreGameSoundService PreGameSounds => preGameSounds;
    private long lastProcessOutputSequence;
    private readonly Dictionary<SourceCallbackCue, SourceSoundVmDispatch> orderedVmCues =
        new(ReferenceEqualityComparer.Instance);
    private readonly Queue<object> orderedSoundDeliveries = new();
    private readonly Dictionary<string, long> eventCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> contactCounts = new(StringComparer.Ordinal);
    private readonly int[] lastButtons = new int[4];
    private readonly double[] lastAxes = new double[8];
    private readonly double[] joints = new double[14 * 15 * 16];
    private readonly int[] modelIds = new int[14], appearanceIds = new int[14];
    public PlayerData Data { get; }
    public RenderAssets Assets { get; }
    public GameSimulation Game { get; private set; }
    public FixedStepBridge Bridge { get; private set; }
    public PreviewCamera Camera { get; private set; }
    public OriginalHudProcess HudProcess { get; private set; }
    public PreviewOptions Options { get; }
    public SourceCabinetSession Session { get; }
    public PortableInstructionRuntime Instructions { get; private set; }
    public SourceMatchConfiguration MatchConfiguration => ((ISourceMatchState)Game.State).Match.Configuration;
    public int[] CabinetInputStations => Session == null ? MatchConfiguration.Stations.Select(s => s.StationId).ToArray() :
        new[] { 0, 1, 2, 3 };
    public FirePresentation Fire { get; private set; }
    public SourceWorldPresentation World { get; private set; }
    public long Epoch { get; private set; }
    public long Frames { get; private set; }
    public bool RenderObservationEnabled { get; }
    public SourceRenderFrameReceipt LatestRenderObservation { get; private set; }
    public long SourceWakes { get; private set; }
    public long InputSamples { get; private set; }
    public long InputChanges { get; private set; }
    public long SoundSelections { get; private set; }
    public long UnqualifiedSoundCalls { get; private set; }
    public long UnqualifiedVisualCallbacks { get; private set; }
    public long DustCallbacks { get; private set; }
    public long BoundDustCallbacks { get; private set; }
    public long PreSnapSwitchSoundSelections { get; private set; }
    public long SourceDirectSoundCallsTaken { get; private set; }
    public long SourceDirectSoundBatchesTaken { get; private set; }
    public long SourceProcessSoundCallsTaken { get; private set; }
    public long SourceProcessSoundCallsDelivered { get; private set; }
    public ReadOnlyCollection<SourceProcessSoundCall> ProcessSoundDeliveries => Array.AsReadOnly(processSoundReceipts.ToArray());
    public ReadOnlyCollection<SourceDirectSoundDelivery> DirectSoundDeliveries => directSoundRecording.Receipts;
    public SourceAudibleView CommittedAudibles => HudProcess.CommittedAudibles;
    public SourceAudioPlayback AudioPlayback { get; }
    private bool OrderedSoundEnabled => Game.Profile == SimulationProfile.OriginalRelease57Hz;
    public bool Active { get; private set; } = true;
    public string LastStatus { get; private set; } = "{}";
    private long lastSourceTick;
    private long soundDeliveryOrder;

    public PlayerHost(PlayerData data, IRenderSink sink, Action<string> progress = null, PreviewOptions options = null,
        Func<int, ISourceRandom> randomFactory = null, int seed = 1, SourcePlayerRecordStore recordStore = null)
        : this(data, sink, false, progress, options, randomFactory, seed, recordStore)
    {
    }

    public PlayerHost(PlayerData data, IRenderSink sink, bool observeRendering, Action<string> progress = null,
        PreviewOptions options = null, Func<int, ISourceRandom> randomFactory = null, int seed = 1,
        SourcePlayerRecordStore recordStore = null)
    {
        RenderObservationEnabled = observeRendering;
        Data = data;
        this.sink = sink;
        this.randomFactory = randomFactory;
        Options = options ?? PreviewOptions.FromUrl("", data.Catalog.Profile.StableId);
        DataCatalog.Require(Options.Profile == data.Catalog.Profile.StableId,
            "Host options must retain the authenticated built profile before asset or kernel construction.");
        DataCatalog.Require(Options.RecordsEnabled ? recordStore != null && data.OriginalPresentation?.HasRecordsArt == true :
            recordStore == null, "Record UI requires its explicit loaded store and authenticated original record/CAP resource family; legacy opens no DB.");
        if (Options.FireEnabled && data.Fire == null)
            throw new InvalidDataException("The selected fire opt-in has no authenticated original assets.");
        if (data.AudioAdmission != null && sink is not ISourceAudioSink)
            throw new InvalidDataException("SOURCE_AUDIO_RECEIVER_MISSING: explicitly admitted decoded audio requires a real PCM/command sink.");
        if (Options.WorldEnabled && sink is not ISourceWorldRenderSink)
            throw new InvalidDataException("SOURCE_WORLD_RECEIVER_MISSING: source-world=portable requires its actual GPU packet sink.");
        Assets = new RenderAssets(data, sink, progress);
        Fire = new FirePresentation(data.Fire, Options.FireEnabled, sink);
        if (Options.WorldEnabled) World = new SourceWorldPresentation(data, (ISourceWorldRenderSink)sink, Epoch);
        Session = Options.GameflowEnabled ? new SourceCabinetSession(data, Options.WorldEnabled, recordStore) : null;
        if (Options.PreGameSoundEnabled)
        {
            preGameSounds = new SourcePreGameSoundService(
                SourcePreGameSoundEntry.FromOriginalPermanentBanksAfterAcceptedStart(),
                randomFactory == null ? SourcePreGameSoundService.CreatePortableRandom(seed) : randomFactory(seed),
                Session);
            Session.BindPreGameInput(preGameSounds.CapturePhysical);
        }
        progress?.Invoke("Constructing " + KernelBinding.FactoryFor(data.Catalog.Profile) + ", fourteen actors and source camera");
        // The unadvanced tick-zero world is the menu backdrop, not a running match.
        // Every playable new configuration is constructed afresh at the deliberate start boundary.
        Game = KernelBinding.Create(data, Options, Options.PreGameSoundEnabled ? null : randomFactory,
            seed, Session?.CreateConfiguration());
        Camera = new PreviewCamera(data, Assets, Game);
        if (data.AudioAdmission != null)
        {
            AudioPlayback = new SourceAudioPlayback(data.AudioAdmission, (ISourceAudioSink)sink,
                ((ISourceGameState)Game.State).SourceTicksPerSecond);
            if (Options.PreGameSoundEnabled) AudioPlayback.PreparePreGameSamples();
            else
            {
                DeliverBootSounds();
                if (Session != null) AudioPlayback.SetActive(false);
            }
            AudioPlayback.Flush();
        }
        hopSoundTransport.BindOutput(new HopSoundObservation(this));
        directSoundRecording = new SourceDirectSoundRecordingConsumer(data.Catalog.Profile.IsCompleteOriginal303);
        directSoundOutput = directSoundRecording;
        quantizer = new SourceCabinetQuantizer(data.Hud.Input);
        input = new SourceCabinetInputBuffer(MatchConfiguration,
            preserveSourceDigitalChanges: Options.GameflowEnabled || data.Catalog.Profile.IsContact282C2M1DefaultAudibles);
        BindSelection();
        hud = new SourceHudComposer(data.Hud, data.OriginalPresentation);
        HudProcess = new OriginalHudProcess(data.Hud, Game);
        hudClock.Advance(Game.SourceTick, HudProcess.CommittedHud.ClockWarning);
        BindBridge();
        Capture(new double[8], new int[4]);
    }

    private void BindSelection()
    {
        selection = new SourceCabinetPlaySelection(Data.Hud,
            customPlaySelection: Game.SourceCustomPlaySession == null ? null : Game);
        selection.EventRaised += ObserveSelection;
        Game.SourceStationInputTransform = selection.ApplyAtSourceTick;
    }

    private void ObserveSelection(SourceCabinetMenuEvent value)
    {
        if (!value.IsRandomSelection) return;
        DataCatalog.Require(!value.SoundCall.HasValue || value.Output != null,
            "The actual random cabinet event must retain its genuine source process output.");
        Enqueue(events, new {
            kind = "cabinet-random-selection", sourceTick = value.SourceTick, station = value.StationId,
            team = value.Side.ToString(), stage = value.Kind.ToString(), page = value.PageIndex,
            slot = value.SlotIndex, flipped = value.OffenseFlipped, bank = value.SoundBank,
            playlist = value.SoundCall, volume = value.SoundVolume, pan = value.SoundPan,
            priority = value.SoundPriority, value.OutputSequence, pcmOrDspPlayback = false
        }, 128);
    }

    private void BindInstructions()
    {
        if (!Options.InstructionsEnabled) return;
        Instructions = new PortableInstructionRuntime(Game, Data, Session, sound => {
            DataCatalog.Require(instructionSoundObservations.TryAdd(sound.Output, sound),
                "The same executing instruction output cannot be observed twice.");
        });
        Instructions.Bind();
    }

    private SourceInputFrame NextHostInput()
    {
        observedInputs.Deliver(false, Game.State.Tick + 1, Capture);
        return input.NextHostFrame();
    }

    private void BindBridge()
    {
        Bridge = new FixedStepBridge(Game);
        Bridge.SourceEventRaised += value => {
            string type = value.Type.ToString();
            eventCounts[type] = eventCounts.GetValueOrDefault(type) + 1;
            Enqueue(events, new {
                sourceTick = value.SourceTick, hostTick = value.Tick, type,
                actor = value.ActorPlayerId, target = value.TargetPlayerId, station = value.StationId,
                team = value.Team?.ToString(), value = value.Value,
                result = value.Result.IsSpecified ? value.Result.Kind.ToString() : null,
                cause = value.Result.IsSpecified ? value.Result.Cause?.ToString() : null
            }, 128);
        };
        Bridge.AfterSimulationStep += ObserveSound;
        Bridge.SourcePresentationCueRaised += cue => {
            SourceSoundVmDispatch ordered = null;
            if (OrderedSoundEnabled && cue is SourceCallbackCue vmCue &&
                orderedVmCues.TryGetValue(vmCue, out ordered))
                RequireOrderedVm(ordered);
            if (Game.SourceDefensivePreSnapSwitchEnabled && cue is SourceCallbackCue sound &&
                sound.Context.SourcePath == "plyrseq.c" && sound.Context.ExecutingSymbol == "m_hopclp_anim")
            {
                SourceSoundVmDispatch dispatch = hopSoundTransport.TakeForCue(sound) ??
                    throw new InvalidDataException("The enabled hop has no actual synchronous resolved sound/cue pairing.");
                DataCatalog.Require(!OrderedSoundEnabled || ReferenceEquals(ordered, dispatch),
                    "SOURCE_SOUND_OUTPUT_SEQUENCE: the actual hop cue must retain its ordered VM dispatch.");
                Camera.Playback.ConsumeResolvedSourceSound(dispatch, hopSoundTransport.DeliverResolved);
            }
            else if (AudioPlayback != null && ordered != null)
            {
                SourceSoundVmDispatch dispatch = hopSoundTransport.TakeForCue(ordered.OriginalCue);
                DataCatalog.Require(ReferenceEquals(dispatch, ordered),
                    "SOURCE_AUDIO_VM_PAIR: decoded output requires the same drained original cue/dispatch.");
                hopSoundTransport.DeliverResolved(dispatch);
            }
            Camera.Cue(cue);
            if (cue.Kind == SourcePresentationCueKind.DustCallback)
            {
                DustCallbacks++;
                var callback = (SourceCallbackCue)cue;
                Enqueue(dustCues, new { sourceTick = cue.Context.SourceTick, order = cue.Context.ProcessOrder,
                    actor = cue.Context.ActorPlayerId, path = cue.Context.SourcePath, symbol = cue.Context.ExecutingSymbol,
                    line = cue.Context.SourceLine, callback.FunctionSymbol, callback.Argument0, callback.Argument1,
                    callback.Argument2, cue.Context.ScriptInstanceId, cue.Context.OpcodeSymbol }, 64);
                if (!Game.SourceFireEnabled) UnqualifiedVisualCallbacks++;
                else
                {
                    DataCatalog.Require(Game.SourceFireRecords.Any(r => r.Kind == "CreateProcess" &&
                        r.Detail == "dustcld" && r.Site.SourceTick == cue.Context.SourceTick &&
                        r.Site.ActorId == cue.Context.ActorPlayerId),
                        "DUST_CALLBACK_UNBOUND: an enabled real DustCallback did not queue its source dustcld children.");
                    BoundDustCallbacks++;
                }
            }
            if (ordered != null)
            {
                ObserveVmOutput(ordered);
                orderedVmCues.Remove(ordered.OriginalCue);
                CompleteSoundOutput(ordered.OutputSequence);
                FlushImmediateSoundOutputs();
            }
        };
        Bridge.AfterSourcePresentation += () => {
            hopSoundTransport.CompletePresentationBatch();
            if (OrderedSoundEnabled)
            {
                FlushImmediateSoundOutputs();
                soundOutputOrder.RequireComplete();
                DataCatalog.Require(pendingSoundOutputs.Count == 0 && orderedVmCues.Count == 0,
                    "SOURCE_SOUND_OUTPUT_SEQUENCE: ordered VM outputs lack actual presentation callbacks.");
            }
            DataCatalog.Require(SourceDirectSoundCallsTaken == directSoundRecording.DeliveredCount,
                "SOURCE_DIRECT_SOUND_DELIVERY: every actual taken producer call needs its completed consumer receipt.");
            DataCatalog.Require(SourceProcessSoundCallsTaken == SourceProcessSoundCallsDelivered &&
                instructionSoundObservations.Count == 0,
                "SOURCE_PROCESS_SOUND_DELIVERY: every canonical actorless output needs exactly one ordered consumer receipt.");
            AudioPlayback?.Flush();
        };
        Bridge.AfterHostStep += ObserveStep;
        Bridge.Restarted += ResetPresentation;
    }

    private void ObserveSound()
    {
        AudioPlayback?.AdvanceTo(Game.SourceTick);
        DataCatalog.Require(pendingSoundOutputs.Count == 0 && orderedVmCues.Count == 0,
            "SOURCE_SOUND_OUTPUT_SEQUENCE: a previous output batch was not completely delivered.");
        var direct = Game.TakeSourceDirectSoundCalls();
        var process = Game.TakeSourceProcessSoundCalls();
        var controls = Game.TakeSourceSoundControls();
        SourceProcessSoundCallsTaken += process.Count;
        var processSet = new HashSet<SourceProcessSoundCall>(process, ReferenceEqualityComparer.Instance);
        SourceDirectSoundBatchesTaken++;
        foreach (SourceDirectSoundCall call in direct)
        {
            DataCatalog.Require(Game.SourceDefaultAudiblesEnabled && call.SourceTick <= Game.SourceTick,
                "Only an admitted source-default-audibles producer may submit current direct output.");
            SourceDirectSoundCallsTaken++;
        }
        foreach (SourceSelectionTimerSound call in Game.TakeSourceSelectionTimerSounds())
        {
            DataCatalog.Require(call.Output != null && processSet.Contains(call.Output),
                "Selection-timer metadata must reference its canonical process queue object.");
            Enqueue(events, new { kind = "timedown_play_select", sourceTick = call.SourceTick,
                serial = call.ProcessSerial, number = call.Number, bank = call.Bank,
                playlist = call.PlaylistId, volume = call.Volume, pan = call.Pan,
                priority = call.PriorityWord, call.OutputSequence, pcmOrDspPlayback = call.PcmOrDspPlayback }, 64);
        }
        foreach (SourcePossessionSpeech speech in Game.SourcePossessionSpeechSubmissions)
        {
            DataCatalog.Require(speech.Output != null, "Possession metadata requires its genuine process output.");
            // The immutable speech view can survive a57/60 host skip; only the
            // canonical queue emits a new call, never a repeated observation.
            if (!processSet.Contains(speech.Output)) continue;
            Enqueue(events, new { kind = "intercepted_ball_sp", sourceTick = speech.SourceTick,
                processOrder = speech.ProcessOrder, serial = speech.ProcessSerial, bank = speech.Bank,
                ordinal = speech.SelectedOrdinal, playlist = speech.PlaylistId, volume = speech.Volume,
                pan = speech.Pan, priority = speech.PriorityWord, speech.OutputSequence, pcmOrDspPlayback = false }, 64);
        }
        SourceSoundVmBatch batch = Game.TakeSourceSoundVmDispatches();
        bool sharedOrder = OrderedSoundEnabled;
        var hops = new List<SourceSoundVmDispatch>();
        foreach (SourceSoundVmDispatch dispatch in batch.Dispatches)
        {
            AudioPlayback?.ValidateDispatch(dispatch, checkLoadedState: AudioPlayback.CompiledPrograms == false);
            if (!sharedOrder) SoundSelections++;
            if (dispatch.Status == SourceSoundVmStatus.MissingInput)
                throw new InvalidDataException("Required original sound-selection input is missing: " + dispatch.Detail);
            if (!sharedOrder && dispatch.Result != null) UnqualifiedSoundCalls += dispatch.Result.Calls.Count;
            if (Game.SourceDefensivePreSnapSwitchEnabled && dispatch.HasOriginalAnimationCue &&
                dispatch.OriginalCueContext.ExecutingSymbol == "m_hopclp_anim")
            {
                DataCatalog.Require(dispatch.C2Context?.Kind == SourceC2SoundFunctionKind.AttackSoundFixed &&
                    dispatch.Status == SourceSoundVmStatus.Resolved,
                    "The enabled hop must resolve its exact typed callback, never Unsupported or muted success.");
                hops.Add(dispatch);
            }
        }
        hopSoundTransport.Drain(AudioPlayback == null ? new SourceSoundVmBatch(hops) : batch);
        if (sharedOrder)
        {
            var order = soundOutputOrder.Begin(direct.Select(call => call.OutputSequence).ToArray(),
                batch.Dispatches.Select(dispatch => dispatch.OutputSequence).ToArray(),
                process.Select(call => call.OutputSequence).ToArray(), controls.Select(call => call.OutputSequence).ToArray());
            foreach (var item in order)
            {
                if (item.Kind == SourceSoundOutputKind.Direct) pendingSoundOutputs.Enqueue((direct[item.Index], null, null, null));
                else if (item.Kind == SourceSoundOutputKind.Process) pendingSoundOutputs.Enqueue((null, null, process[item.Index], null));
                else if (item.Kind == SourceSoundOutputKind.Control) pendingSoundOutputs.Enqueue((null, null, null, controls[item.Index]));
                else
                {
                    SourceSoundVmDispatch dispatch = batch.Dispatches[item.Index];
                    pendingSoundOutputs.Enqueue((null, dispatch, null, null));
                    if (dispatch.HasOriginalAnimationCue)
                        DataCatalog.Require(orderedVmCues.TryAdd(dispatch.OriginalCue, dispatch),
                            "SOURCE_SOUND_OUTPUT_SEQUENCE: one actual callback cannot identify two VM outputs.");
                }
            }
            FlushImmediateSoundOutputs();
        }
    }

    private void DeliverBootSounds()
    {
        var outputs = preparedBootSounds ?? Game.TakeSourceBootSoundOutputs();
        preparedBootSounds = null;
        if (outputs.Count == 0) return;
        DataCatalog.Require(Data.Catalog.Profile.IsCompleteOriginal303 && AudioPlayback?.CompiledPrograms == true,
            "Actual named boot sound output requires its explicit original decoded composition.");
        var order = soundOutputOrder.Begin(Array.Empty<long>(), Array.Empty<long>(), Array.Empty<long>(),
            outputs.Select(call => call.OutputSequence).ToArray());
        foreach (var item in order)
        {
            SourceBootSoundOutput call = outputs[item.Index];
            AudioPlayback.Consume(call);
            soundOutputOrder.Complete(call.OutputSequence);
            BootSoundOutputsDelivered++;
        }
        soundOutputOrder.RequireComplete();
    }

    private void RequireOrderedVm(SourceSoundVmDispatch dispatch)
    {
        DataCatalog.Require(pendingSoundOutputs.TryPeek(out var entry) && ReferenceEquals(entry.Vm, dispatch) &&
            soundOutputOrder.NextSequence == dispatch.OutputSequence,
            "SOURCE_SOUND_OUTPUT_SEQUENCE: actual VM callbacks cannot reorder common producer outputs.");
    }

    private void FlushImmediateSoundOutputs()
    {
        while (pendingSoundOutputs.TryPeek(out var entry))
        {
            if (entry.Vm != null && entry.Vm.HasOriginalAnimationCue) return;
            long sequence;
            if (entry.Direct != null)
            {
                sequence = entry.Direct.OutputSequence;
                DeliverDirectSound(entry.Direct);
            }
            else if (entry.Process != null)
            {
                sequence = entry.Process.OutputSequence;
                DeliverProcessSound(entry.Process);
            }
            else if (entry.Control != null)
            {
                sequence = entry.Control.OutputSequence;
                AudioPlayback?.Consume(entry.Control);
                SoundControlsDelivered++;
            }
            else
            {
                sequence = entry.Vm.OutputSequence;
                if (AudioPlayback != null) hopSoundTransport.DeliverNextNative(entry.Vm);
                ObserveVmOutput(entry.Vm);
            }
            CompleteSoundOutput(sequence);
        }
    }

    private void CompleteSoundOutput(long outputSequence)
    {
        soundOutputOrder.Complete(outputSequence);
        pendingSoundOutputs.Dequeue();
    }

    private void ObserveVmOutput(SourceSoundVmDispatch dispatch)
    {
        SoundSelections++;
        if (AudioPlayback == null && dispatch.Result != null) UnqualifiedSoundCalls += dispatch.Result.Calls.Count;
    }

    private void DeliverProcessSound(SourceProcessSoundCall call)
    {
        DataCatalog.Require(call.OutputSequence > lastProcessOutputSequence,
            "SOURCE_PROCESS_SOUND_REPLAY: process output must retain a fresh genuine source ordinal.");
        bool instruction = call.Kind is SourceProcessSoundKind.InstructionTick or SourceProcessSoundKind.InstructionPageAdvance
            or SourceProcessSoundKind.KickoffInstruction or SourceProcessSoundKind.PlaySelectionIntroduction;
        bool observed = instructionSoundObservations.Remove(call, out PortableInstructionSound receipt);
        DataCatalog.Require(instruction == observed && (!observed || ReferenceEquals(receipt.Output, call)),
            "The actual instruction callback and canonical queue must retain exactly the SAME source record.");
        AudioPlayback?.Consume(call);
        processSoundReceipts.Enqueue(call);
        if (processSoundReceipts.Count > 128) processSoundReceipts.Dequeue();
        lastProcessOutputSequence = call.OutputSequence;
        SourceProcessSoundCallsDelivered++;
        SoundSelections++;
        if (AudioPlayback == null) UnqualifiedSoundCalls++;
        Enqueue(orderedSoundDeliveries, new { order = ++soundDeliveryOrder, kind = "source-process",
            call.Kind, call.OutputSequence, call.SourceTick, call.ProcessSerial, call.Process,
            processOrder = call.DispatchOrder, call.SubcallOrder, call.Bank, call.PlaylistId,
            call.Volume, call.Pan, call.PriorityWord, call.RandomCalls }, 128);
        if (observed)
            Enqueue(events, new { kind = "source-instruction-sound", receipt.SourceTick, receipt.ProcessSerial,
                receipt.Process, receipt.DispatchOrder, receipt.SubcallOrder, receipt.OutputSequence,
                receipt.Operand, receipt.Bank, receipt.Playlist, receipt.Volume, receipt.Pan,
                receipt.Priority, receipt.Direct, receipt.PcmOrDspPlayback,
                output = "same actual source-stamped process record; recording-only unless separately admitted PCM" }, 128);
    }

    private void DeliverDirectSound(SourceDirectSoundCall call)
    {
        AudioPlayback?.Consume(call);
        long before = directSoundRecording.DeliveredCount;
        directSoundOutput.Consume(call);
        DataCatalog.Require(directSoundRecording.DeliveredCount == before + 1 &&
            ReferenceEquals(directSoundRecording.Receipts.Last().Call, call),
            "SOURCE_DIRECT_SOUND_DELIVERY: invoking an interface/no-op is not a successful recording-consumer receipt.");
        SoundSelections++;
        if (AudioPlayback == null) UnqualifiedSoundCalls++;
        Enqueue(orderedSoundDeliveries, new {
            order = ++soundDeliveryOrder, kind = "source-direct", call.SourceTick,
            processOrder = call.DispatchOrder, call.SubcallOrder, call.Sequence, call.OutputSequence,
            deliverySequence = directSoundRecording.DeliveredCount
        }, 128);
    }

    private sealed class HopSoundObservation : ISourceSoundVmOutputConsumer
    {
        private readonly PlayerHost host;
        internal HopSoundObservation(PlayerHost host) { this.host = host; }
        public void ConsumeResolved(SourceSoundVmDispatch dispatch)
        {
            host.AudioPlayback?.ConsumeResolved(dispatch);
            if (host.AudioPlayback != null && dispatch.C2Context?.Kind != SourceC2SoundFunctionKind.AttackSoundFixed)
                return;
            DataCatalog.Require(dispatch.C2Context?.Kind == SourceC2SoundFunctionKind.AttackSoundFixed &&
                dispatch.Status == SourceSoundVmStatus.Resolved && dispatch.Result?.Calls.Count == 1,
                "The recording consumer accepts only the actual resolved fixed attack call.");
            var call = dispatch.Result.Calls[0];
            host.PreSnapSwitchSoundSelections++;
            Enqueue(host.orderedSoundDeliveries, new {
                order = ++host.soundDeliveryOrder, kind = "resolved-hop",
                dispatch.C2Context.SourceTick, processOrder = dispatch.C2Context.ProcessOrder, call.SubcallOrder,
                dispatch.OutputSequence
            }, 128);
            Enqueue(host.hopSounds, new {
                kind = dispatch.C2Context.Kind.ToString(), sourceTick = dispatch.C2Context.SourceTick,
                actor = dispatch.C2Context.ActorId, station = dispatch.C2StateAtCall.Value.ActorStation,
                process = dispatch.C2Context.Process, processOrder = dispatch.C2Context.ProcessOrder,
                firstCallIndex = dispatch.C2Context.FirstCallIndex,
                script = dispatch.OriginalCueContext.ExecutingSymbol, instance = dispatch.OriginalCueContext.ScriptInstanceId,
                path = dispatch.OriginalCueContext.SourcePath, line = dispatch.OriginalCueContext.SourceLine,
                function = dispatch.OriginalCue.FunctionSymbol, arguments = new[] {
                    dispatch.OriginalCue.Argument0, dispatch.OriginalCue.Argument1, dispatch.OriginalCue.Argument2 },
                call.Bank, call.PlaylistId, call.Volume, call.Pan, call.PriorityWord, call.SubcallOrder,
                randomCalls = dispatch.Result.RandomCalls, callSite = new {
                    path = call.CalleeSourceSite.SourcePath, function = call.CalleeSourceSite.FunctionSymbol,
                    line = call.CalleeSourceSite.SourceLine, blob = call.CalleeSourceSite.SourceGitBlob },
                sameCue = ReferenceEquals(call.OriginalCue, dispatch.OriginalCue),
                sameContext = ReferenceEquals(call.C2Context, dispatch.C2Context),
                output = host.AudioPlayback == null
                    ? "actual transport/cinematic recording consumer; PCM/DSP playback unqualified"
                    : "actual transport/qualified PCM command sink; hearing and native DSP parity not verified",
                pcmOrDspPlayback = false
            }, 32);
        }
    }

    private void ObserveStep(IGameState state)
    {
        Instructions?.AfterSourceStep();
        HudProcess.Observe(Game);
        if (Game.SourceTick > lastSourceTick)
        {
            DataCatalog.Require(Game.SourceTick == lastSourceTick + 1,
                "The host skipped a source wake; source HUD/camera ordering cannot be reconstructed per render.");
            SourceWakes++;
            lastSourceTick = Game.SourceTick;
            hudClock.Advance(lastSourceTick, HudProcess.CommittedHud.ClockWarning,
                HudProcess.CommittedHud.StatusProcessSuspended);
        }
        Camera.ObserveHostStep(state);
        Fire.Observe(Game);
        foreach (var value in Game.ContactEvents)
        {
            string kind = value.Kind.ToString();
            contactCounts[kind] = contactCounts.GetValueOrDefault(kind) + 1;
            Enqueue(contacts, new {
                sourceTick = value.SourceTick, processOrder = value.ProcessOrder,
                kind, actor = value.ActorPlayerId, other = value.OtherPlayerId,
                script = value.ScriptSymbol, attackMode = value.AttackMode
            }, 64);
        }
        if (Session?.Observe(HudProcess.CommittedState) == true)
        {
            Bridge.ClearTransientInput();
            input.Clear();
            selection.ClearPendingInput();
            foreach (var pending in commands) pending.Clear();
            AudioPlayback?.SetActive(false);
            AudioPlayback?.Flush();
        }
    }

    public void Capture(double[] axes, int[] buttons)
    {
        DataCatalog.Require(axes is { Length: 8 } && buttons is { Length: 4 } &&
            axes.All(double.IsFinite) && axes.All(value => value is >= -1 and <= 1) &&
            buttons.All(value => value is >= 0 and <= 7),
            "All four raw browser cabinet slots need finite normalized axes and A/B/C masks.");
        if (Session != null && (!Session.Playing || !Active || Session.Records?.Active == true))
        {
            if (Active) Session.Capture(axes, buttons, quantizer);
            RecordInput(axes, buttons);
            return;
        }
        var stations = MatchConfiguration.Stations;
        var samples = new SourceCabinetSample[stations.Count];
        for (int i = 0; i < samples.Length; i++)
        {
            int id = stations[i].StationId;
            var stick = quantizer.Analog((float)axes[id * 2], (float)axes[id * 2 + 1]);
            SourceCommand command = !input.WaitingForNeutral && commands[id].Count > 0 ?
                commands[id].Dequeue() : default;
            samples[i] = new SourceCabinetSample(id, stick, (SourceButtons)buttons[id], command);
        }
        if (Options.InstructionsEnabled)
        {
            var physical = new SourceStationInput[4];
            for (int id = 0; id < physical.Length; id++)
            {
                var stick = quantizer.Analog((float)axes[id * 2], (float)axes[id * 2 + 1]);
                physical[id] = new SourceStationInput(id, stick.StickX, stick.StickZ, (SourceButtons)buttons[id],
                    digitalCurrent: (SourceDigitalDirections)stick.DigitalDirections);
            }
            input.Capture(samples, new SourcePhysicalSwitchFrame(physical));
        }
        else input.Capture(samples);
        RecordInput(axes, buttons);
    }

    public void ObserveHostTime(double seconds)
    {
        DataCatalog.Require(double.IsFinite(seconds) && seconds >= observedHostTime,
            "SOURCE_OBSERVED_HOST_CLOCK: actual monotonic active host time is required.");
        observedHostTime = seconds; observedHostClockBound = true;
    }

    public void ObserveFrameHostTime(double seconds)
    {
        ObserveHostTime(seconds);
        lastObservedFrameTime = seconds;
    }

    public int AdvanceObserved(double nominalElapsed, double seconds, int width, int height,
        double cssWidth, double cssHeight, double dpr)
    {
        double elapsed = observedHostClockBound ? seconds - lastObservedFrameTime : nominalElapsed;
        ObserveFrameHostTime(seconds);
        return Advance(Options.PreGameSoundEnabled ? elapsed : nominalElapsed, width, height, cssWidth, cssHeight, dpr);
    }

    public void CaptureObserved(double[] axes, int[] buttons, double seconds)
    {
        DataCatalog.Require(axes is { Length: 8 } && buttons is { Length: 4 } &&
            axes.All(value => double.IsFinite(value) && value is >= -1 and <= 1) &&
            buttons.All(value => value is >= 0 and <= 7),
            "Complete measured four-slot physical inputs are required.");
        if (Options.PreGameSoundEnabled && gameSoundHandoff != null && Session.Playing &&
            (!Active || resumeAwaitingPhysicalObservations))
        {
            ObserveHostTime(seconds);
            if (pausedPhysicalObservations.Count == 2) pausedPhysicalObservations.Dequeue();
            pausedPhysicalObservations.Enqueue(((double[])axes.Clone(), (int[])buttons.Clone()));
            RecordInput(axes, buttons);
            if (Active && resumeAwaitingPhysicalObservations && pausedPhysicalObservations.Count == 2)
            {
                DeliverActualResumeObservations();
                AudioPlayback.SetActive(true); AudioPlayback.Flush();
            }
            return;
        }
        if (!Options.PreGameSoundEnabled || !Active || gameSoundHandoff != null && !Session.Playing)
        { Capture(axes, buttons); return; }
        if (!observedHostClockBound) lastObservedFrameTime = seconds;
        ObserveHostTime(seconds);
        bool preGame = gameSoundHandoff == null;
        long current = preGame ? preGameSounds.HostTicks : Game.State.Tick;
        double measured = (preGame ? preGameObservedSeconds : realtimeClock.ObservedSeconds) + pauseCarrySeconds +
            Math.Max(0, seconds - lastObservedFrameTime);
        long target = preGame && preGameNeedsPhysicalCapture ||
            !preGame && (bootAudioAwaitingContext || Game.State.Tick == 0) ? current + 1 :
            SourceObservedInputTimeline.TargetTick(measured, 0,
                preGame ? preGameInputOriginTick : gameInputOriginTick, current);
        observedInputs.Add(preGame, target, axes, buttons);
        if (preGame) preGameNeedsPhysicalCapture = false;
    }

    private void RecordInput(double[] axes, int[] buttons)
    {
        InputSamples++;
        if (!lastAxes.SequenceEqual(axes) || !lastButtons.SequenceEqual(buttons)) InputChanges++;
        Array.Copy(axes, lastAxes, axes.Length);
        Array.Copy(buttons, lastButtons, buttons.Length);
    }

    public void Continue(int stationId)
    {
        if (Session != null)
        {
            SourceCabinetQuantizer.ValidateStationId(stationId);
            if (!Active) return;
            if (!Session.Playing || Session.Records?.Active == true) { Session.Continue(stationId); return; }
            if (!MatchConfiguration.Stations.Any(s => s.StationId == stationId)) return;
        }
        DataCatalog.Require(MatchConfiguration.Stations.Any(s => s.StationId == stationId),
            "Continue must belong to an actual configured cabinet station.");
        DataCatalog.Require(commands[stationId].Count < 8, "Cabinet command backlog exceeded.");
        commands[stationId].Enqueue(new SourceCommand(SourceAction.Continue));
    }

    public void SetActive(bool active)
    {
        if (Active == active) return;
        bool coordinatedGame = Options.PreGameSoundEnabled && gameSoundHandoff != null &&
            Session?.Playing == true && !bootAudioAwaitingContext;
        if (coordinatedGame && !active && !resumeAwaitingPhysicalObservations)
        {
            if (resumeBridgeReady)
            {
                DataCatalog.Require(Bridge.AdvanceSource(0, NextHostInput) == 0,
                    "SOURCE_REALTIME_RESUME: the actual resume boundary cannot invent a source wake.");
                resumeBridgeReady = false;
            }
            realtimeClock.Advance(Math.Max(0, observedHostTime - lastObservedFrameTime),
                delta => Bridge.AdvanceSource(delta, NextHostInput));
            gameSoundHandoff.ObserveGameClock(Game);
        }
        else if (!coordinatedGame && Options.PreGameSoundEnabled && !active)
            pauseCarrySeconds += (gameSoundHandoff == null ? preGameAccumulator : realtimeClock.PendingSeconds) +
                Math.Max(0, observedHostTime - lastObservedFrameTime);
        Active = active;
        if (!active)
        {
            pausedPhysicalObservations.Clear();
            resumeAwaitingPhysicalObservations = false;
        }
        input.Clear();
        Session?.ResetControls();
        selection.ClearPendingInput();
        foreach (var pending in commands) pending.Clear();
        Bridge.SetActive(active);
        if (coordinatedGame) realtimeClock.PreservePendingAcrossInputClear();
        else realtimeClock.Clear();
        observedInputs.Clear();
        lastObservedFrameTime = observedHostTime; preGameObservedSeconds = 0;
        preGameInputOriginTick = preGameSounds?.HostTicks ?? 0;
        if (!coordinatedGame) gameInputOriginTick = Game.State.Tick;
        resumeBridgeReady = active && Options.PreGameSoundEnabled && gameSoundHandoff != null;
        if (preGameSounds != null && gameSoundHandoff == null)
        {
            preGameSounds.SetActive(active);
            preGameSounds.ClearPendingInput();
            preGameAccumulator = 0;
            preGameNeedsPhysicalCapture = true;
        }
        if (coordinatedGame && active)
        {
            resumeAwaitingPhysicalObservations = pausedPhysicalObservations.Count < 2;
            if (!resumeAwaitingPhysicalObservations) DeliverActualResumeObservations();
        }
        AudioPlayback?.SetActive(active && !resumeAwaitingPhysicalObservations && (Session == null || Session.Playing ||
            preGameSounds?.OriginalEntryActivated == true && gameSoundHandoff == null));
        AudioPlayback?.Flush();
        Array.Clear(lastButtons);
        Array.Clear(lastAxes);
    }

    private void DeliverActualResumeObservations()
    {
        DataCatalog.Require(pausedPhysicalObservations.Count == 2,
            "SOURCE_RESUME_PHYSICAL: two actual captured physical observations, not synthetic neutral, are required.");
        foreach (var value in pausedPhysicalObservations)
            observedInputs.Add(false, Game.State.Tick + 1, value.Axes, value.Buttons);
        pausedPhysicalObservations.Clear();
        resumeAwaitingPhysicalObservations = false;
    }

    public void Restart()
    {
        if (Session?.Records != null && !Session.Records.CanAdministrativeRestart) return;
        if (Options.PreGameSoundEnabled)
        {
            if (gameSoundHandoff == null || !Session.Playing) return;
            CreateSessionGame(MatchConfiguration);
            return;
        }
        Session?.AdministrativeRestart();
        Bridge.Restart();
    }

    private void ResetPresentation() => ResetPresentation(false);

    private void ResetPresentation(bool newProducer)
    {
        Epoch++;
        LatestRenderObservation = null;
        input.Clear();
        selection.Clear();
        hudClock.Clear();
        HudProcess = new OriginalHudProcess(Data.Hud, Game);
        hudClock.Advance(0, HudProcess.CommittedHud.ClockWarning);
        Camera = new PreviewCamera(Data, Assets, Game);
        Fire = new FirePresentation(Data.Fire, Options.FireEnabled, sink, Epoch, uploadTextures: false);
        if (Options.WorldEnabled) World = new SourceWorldPresentation(Data, (ISourceWorldRenderSink)sink, Epoch,
            Session?.Playing == true ? Session.WorldUserFog : null);
        lastSourceTick = 0;
        realtimeClock.Clear();
        pauseCarrySeconds = 0; resumeBridgeReady = false;
        pausedPhysicalObservations.Clear(); resumeAwaitingPhysicalObservations = false;
        observedInputs.Clear();
        lastObservedFrameTime = observedHostTime; gameInputOriginTick = 0;
        events.Clear();
        contacts.Clear();
        dustCues.Clear();
        eventCounts.Clear();
        contactCounts.Clear();
        SourceWakes = SoundSelections = UnqualifiedSoundCalls = UnqualifiedVisualCallbacks = 0;
        DustCallbacks = BoundDustCallbacks = 0;
        PreSnapSwitchSoundSelections = 0;
        hopSounds.Clear();
        hopSoundTransport.Reset();
        if (AudioPlayback != null && Data.Catalog.Profile.IsCompleteOriginal303)
        {
            preparedBootSounds = Game.TakeSourceBootSoundOutputs();
            // PCM decoding/upload belongs before the fresh epoch wall-clock anchor,
            // not inside its source-zero play deadline.
            AudioPlayback.PrepareBootSamples(preparedBootSounds);
        }
        if (Options.PreGameSoundEnabled)
        {
            AudioPlayback.BeginVersusEpoch(AudioPlayback.Epoch + 1,
                initialPreGameHandoff ? gameSoundHandoff : null, Active);
            initialPreGameHandoff = false;
        }
        else AudioPlayback?.Reset(Epoch, Active && (Session == null || Session.Playing), newProducer);
        pendingSoundOutputs.Clear();
        instructionSoundObservations.Clear();
        processSoundReceipts.Clear();
        SourceProcessSoundCallsTaken = SourceProcessSoundCallsDelivered = 0;
        if (newProducer) lastProcessOutputSequence = 0;
        orderedVmCues.Clear();
        soundOutputOrder.ResetPending(newProducer);
        bootAudioAwaitingContext = AudioPlayback != null && Data.Catalog.Profile.IsCompleteOriginal303;
        directSoundRecording.Reset(newProducer);
        orderedSoundDeliveries.Clear();
        SourceDirectSoundCallsTaken = SourceDirectSoundBatchesTaken = soundDeliveryOrder = 0;
        if (!newProducer)
        {
            if (Instructions != null) Instructions.OnSourceProgramReset();
            else if (Options.InstructionsEnabled && Session.Playing) BindInstructions();
        }
        foreach (var pending in commands) pending.Clear();
        Array.Clear(lastButtons);
        Array.Clear(lastAxes);
    }

    public int Advance(double elapsedSeconds, int bufferWidth, int bufferHeight, double cssWidth, double cssHeight, double dpr)
    {
        if (Options.PreGameSoundEnabled && gameSoundHandoff == null && Active)
        {
            double elapsed = elapsedSeconds + pauseCarrySeconds;
            pauseCarrySeconds = 0;
            AdvancePreGame(elapsed);
        }
        bool newMatch = Active && Session?.HasMatchRequest == true &&
            (!Options.PreGameSoundEnabled || gameSoundHandoff != null);
        if (newMatch) BeginSessionMatch();
        bool bootBoundary = Active && !newMatch && bootAudioAwaitingContext;
        if (bootBoundary)
        {
            DeliverBootSounds();
            bootAudioAwaitingContext = false;
            if (Options.PreGameSoundEnabled)
            {
                DataCatalog.Require(Game.SourceTick == 0 && Bridge.AdvanceSource(0, NextHostInput) == 0,
                    "SOURCE_REALTIME_BOOT: the actual source-zero ready frame may clear its transient-input boundary, never wake the game.");
                if (observedHostClockBound) lastObservedFrameTime = observedHostTime;
            }
        }
        if (resumeBridgeReady && Active && !resumeAwaitingPhysicalObservations &&
            Session?.Playing == true && !newMatch && !bootBoundary)
        {
            DataCatalog.Require(Bridge.AdvanceSource(0, NextHostInput) == 0,
                "SOURCE_REALTIME_RESUME: the actual ready resume frame must clear transient input without an invented source wake.");
            resumeBridgeReady = false;
        }
        double gameplayElapsed = elapsedSeconds;
        if (Options.PreGameSoundEnabled && Active && !newMatch && !bootBoundary && Session?.Playing == true)
        {
            gameplayElapsed += pauseCarrySeconds;
            pauseCarrySeconds = 0;
        }
        int steps = Active && !resumeAwaitingPhysicalObservations && !newMatch && !bootBoundary && (Session == null || Session.Playing) ?
            Options.PreGameSoundEnabled ? realtimeClock.Advance(gameplayElapsed,
                elapsed => Bridge.AdvanceSource(elapsed, NextHostInput)) :
                Bridge.AdvanceSource(elapsedSeconds, NextHostInput) : 0;
        if (gameSoundHandoff != null) gameSoundHandoff.ObserveGameClock(Game);
        // Do not add a full presentation to whole-step source debt. The next normal
        // callback still uses the same bounded bridge and delivers every audio command.
        if (!Options.PreGameSoundEnabled || !Active || resumeAwaitingPhysicalObservations ||
            Session?.Playing != true || realtimeClock.PendingSeconds < FixedStepBridge.StepSeconds)
            Render(new SourceHudViewport(bufferWidth, bufferHeight, cssWidth, cssHeight, dpr));
        AudioPlayback?.Flush();
        return steps;
    }

    private void AdvancePreGame(double elapsed)
    {
        DataCatalog.Require(double.IsFinite(elapsed) && elapsed >= 0,
            "The original pregame requires a finite nonnegative actual host interval.");
        preGameAccumulator += elapsed;
        preGameObservedSeconds += elapsed;
        int hostSteps = 0;
        while (hostSteps < FixedStepBridge.MaximumStepsPerFrame && preGameAccumulator >= 1d / 60)
        {
            preGameAccumulator -= 1d / 60;
            observedInputs.Deliver(true, preGameSounds.HostTicks + 1, Capture);
            preGameSounds.AdvanceHostTick();
            hostSteps++;
            if (preGameSounds.TryTakeEntryActivation(out SourcePreGameSoundActivation activation))
            {
                AudioPlayback.BeginPreGameEpoch(AudioPlayback.Epoch + 1, activation, Active);
                break;
            }
            if (!preGameSounds.OriginalEntryActivated) continue;
            AudioPlayback.AdvanceTo(preGameSounds.SourceTick);
            var process = preGameSounds.TakeSourceProcessSoundCalls();
            var controls = preGameSounds.TakeSourceSoundControls();
            SourceProcessSoundCallsTaken += process.Count;
            var order = soundOutputOrder.Begin(Array.Empty<long>(), Array.Empty<long>(),
                process.Select(call => call.OutputSequence).ToArray(),
                controls.Select(call => call.OutputSequence).ToArray());
            foreach (var item in order)
            {
                if (item.Kind == SourceSoundOutputKind.Process)
                {
                    DeliverProcessSound(process[item.Index]);
                    PreGameSoundCallsDelivered++;
                }
                else
                {
                    AudioPlayback.Consume(controls[item.Index]);
                    PreGameSoundControlsDelivered++;
                }
                soundOutputOrder.Complete(item.OutputSequence);
            }
            soundOutputOrder.RequireComplete();
            AudioPlayback.Flush();
            if (!preGameSounds.TryTakeGameHandoff(out SourcePreGameSoundHandoff handoff)) continue;
            gameSoundHandoff = handoff;
            cabinetRandomFactory = handoff.CreateGameRandomFactory();
            initialPreGameHandoff = true;
            break;
        }
    }

    private void BeginSessionMatch()
    {
        bool rematch = Session.RematchRequested;
        SourceMatchConfiguration configuration = Session.TakeMatchRequest();
        if (rematch && Session.Records == null)
        {
            DataCatalog.Require(ReferenceEquals(configuration, MatchConfiguration),
                "A cabinet rematch retains the SAME immutable team/human configuration.");
            Bridge.Restart();
            return;
        }
        CreateSessionGame(configuration);
    }

    private void CreateSessionGame(SourceMatchConfiguration configuration)
    {
        Instructions?.Dispose();
        Instructions = null;
        if (gameSoundHandoff != null && !initialPreGameHandoff) gameSoundHandoff.ObserveGameClock(Game);
        Game = KernelBinding.Create(Data, Options, cabinetRandomFactory ?? randomFactory, 1, configuration, Session.Continuation,
            newPlayerSession: Session.Records == null, soundBankState: gameSoundHandoff?.BankState);
        if (Session.Records != null)
        {
            Game.BindSourceCustomPlaySession(new SourceCustomPlaySession(Session.Records.BindMatch(configuration)), Data.Hud);
            Game.BindSourceRecordCommitBarrier(Session.Records);
        }
        BindSelection();
        input = new SourceCabinetInputBuffer(configuration, preserveSourceDigitalChanges: true);
        BindBridge();
        Bridge.SetActive(Active);
        ResetPresentation(newProducer: true);
        gameSoundHandoff?.ObserveGameClock(Game);
        // Keep Bridge attached to this actual fresh Game even if admission
        // fails. Its real Restart must be able to recover the prepared consumer.
        BindInstructions();
    }

    private void Render(SourceHudViewport viewport)
    {
        IGameState state = Game.State, previous = Bridge.PreviousState;
        DataCatalog.Require(state.Players.Count == 14 && state.Players.Select(a => a.Id).Distinct().Count() == 14 &&
            state.Players.Count(a => a.Team == TeamId.Home) == 7 && state.Players.Count(a => a.Team == TeamId.Away) == 7,
            "A live preview requires all fourteen actual production actors on both teams.");
        var actorStatus = new object[14];
        var observedActors = RenderObservationEnabled ? new SourceRenderActorReceipt[14] : null;
        var indicators = new List<(int Station, SourceVector3 Head)>();
        foreach (PlayerView actor in state.Players)
        {
            PlayerView old = previous.Players.First(p => p.Id == actor.Id);
            var binding = Assets.Bind(actor, state);
            var pose = SourceActorPoseSampler.Evaluate(Data.Animations, Data.ReferencePivots,
                old, actor, Bridge.RenderInterpolation01, RenderObservationEnabled, out SourceActorPoseObservation observation);
            SourceVector3 root = SourceActorPoseSampler.CanInterpolate(old, actor) ?
                SourceAnimationSampling.Lerp(FrameMath.Root(old), Game.SourceActorWorldPosition(actor.Id), Bridge.RenderInterpolation01) :
                Game.SourceActorWorldPosition(actor.Id);
            for (int joint = 0; joint < pose.Joints.Count; joint++)
                FrameMath.Joint(joints, (actor.Id * 15 + joint) * 16, pose.Joints[joint], root, pose.ModelScale);
            modelIds[actor.Id] = binding.Appearance.Model.Index;
            appearanceIds[actor.Id] = binding.Id;
            if (RenderObservationEnabled)
                observedActors[actor.Id] = new SourceRenderActorReceipt(observation, root, modelIds[actor.Id], appearanceIds[actor.Id]);
            if (actor.Source.ControllerStationId >= 0)
            {
                var head = pose.Joints[Data.Rosters.ActorModels.HeadJointIndex].Position;
                indicators.Add((actor.Source.ControllerStationId,
                    new SourceVector3(root.X + head.X, root.Y + head.Y, root.Z - head.Z)));
            }
            actorStatus[actor.Id] = new {
                id = actor.Id, team = actor.Team.ToString(), sourcePlayerId = binding.Appearance.SourcePlayerId,
                rosterTeam = actor.Source.Roster.TeamIndex, rosterRecord = actor.Source.Roster.RosterIndex,
                position = new[] { actor.Position.X, actor.Height, actor.Position.Z },
                model = binding.Appearance.Model.Index, appearance = binding.Id,
                uniform = binding.Appearance.UniformIdentity,
                uniformHash = Data.Teams.Textures.First(t => t.Id == binding.Appearance.Uniform.Slots[2].Id).Sha256,
                textureSlots = binding.Appearance.Uniform.Slots.Select(slot => slot.Id).ToArray(),
                number = binding.Appearance.Player.Profile.PackedJerseyNumber,
                script = actor.Animation.Primary.ScriptSymbol, clip = actor.Animation.Primary.Sample.SequenceName,
                frame = actor.Animation.Primary.Sample.SourceFrame, animationSourceTick = actor.Animation.SourceTick,
                worldRoot = new[] { root.X, root.Y, root.Z }, modelScale = pose.ModelScale,
                worldRootMatrix = joints.Skip(actor.Id * 15 * 16).Take(16).ToArray(),
                secondary = actor.Animation.Secondary.ScriptSymbol, action = actor.Animation.Action.ToString(),
                poseDigest = PoseDigest(pose), handicap = actor.Source.Handicap, rawHandicap = actor.Source.RawHandicap,
                controller = actor.Source.ControllerStationId, tint = "none",
                rawFlags = actor.Source.Presentation.RawFlags,
                flashWhite = new SourceFirePlayerFlashState(actor.Source.Presentation.RawFlags).White,
                headVariant = binding.Appearance.CustomHead != null ? "original-station-custom" :
                    binding.Appearance.PoppedHead != null ? "original-phead-source-authored" : "original-helmeted",
                headSourceSha256 = binding.Appearance.PoppedHead != null ? SourceTeamPoppedHeadData.SourceSha256 : null,
                headGeometrySha256 = binding.Appearance.PoppedHead != null ? SourceTeamPoppedHeadData.HeaderSha256 : null
            };
        }
        bool recordScreen = Session?.Records?.Active == true;
        var hudDraw = new List<SourceHudPrimitive>(recordScreen ? hud.Records(Session.Records.View(), viewport) :
            hud.Compose(HudProcess.CommittedState, viewport, selection,
                hudClock.RedClock, !Active, Session != null && !Session.Playing ? Session.View() : null));
        foreach (var indicator in indicators)
        {
            if (!recordScreen && (Session == null || Session.Playing) && (state.Phase is GamePhase.PreSnap or GamePhase.LiveBall) &&
                Project(Camera.ViewProjection, indicator.Head, viewport, out double x, out double y))
                hudDraw.AddRange(hud.Indicator(indicator.Station, x, y, viewport, MatchConfiguration.Stations.Count));
        }
        if (!recordScreen && Instructions != null && Session.Playing)
            hudDraw.AddRange(hud.Instructions(Instructions.Frame(), Instructions.Resources.ReadRaster(), viewport));
        var hudVertices = new double[hudDraw.Sum(p => p.Vertices.Count) * 4];
        int cursor = 0;
        foreach (var draw in hudDraw)
        foreach (var vertex in draw.Vertices)
        {
            hudVertices[cursor++] = vertex.X;
            hudVertices[cursor++] = vertex.Y;
            hudVertices[cursor++] = vertex.S;
            hudVertices[cursor++] = vertex.T;
        }
        SourceVector3 ball = Game.SourceBallWorldPosition;
        SourceFreeBallDraw ballDraw = SourceFreeBallBinding.Capture(Game);
        double[] ballPacket = FrameMath.FreeBall(ballDraw);
        var ballTrails = SourceFreeBallBinding.CaptureTrails(Game);
        var match = ((ISourceMatchState)state).Match;
        var source = (ISourceGameState)state;
        DataCatalog.Require(hudDraw.Where(p => p.Audibles != null).All(p =>
            ReferenceEquals(p.Audibles, HudProcess.CommittedAudibles)),
            "The actual HUD must consume the SAME committed audible object, not a parallel view/clock.");
        Frames++;
        World?.Send(state, joints, Camera.Current, Camera.LastSourceTick, Bridge.RenderInterpolation01, Frames);
        Fire.Send(state, Camera.CameraSpaceProjection, hudDraw, HudProcess.CommittedHud.KickMeter);
        var kicking = (ISourceKickMeterState)state;
        LastStatus = JsonSerializer.Serialize(new {
            schema = "blitz-portable-source-player-frame/v1", runtimeFlavor = RuntimeFlavor,
            kernel = KernelBinding.Describe(Game, Data.Catalog.Profile), profile = Data.Animations.InventorySha256,
            resourceProfile = Data.Catalog.Profile.StableId,
            animationLibrary = new { sha256 = Data.Animations.InventorySha256,
                clips = Data.Animations.SequenceNames.Count, frames = Data.Animations.FrameCount },
            callbacks = new { profile = Data.Callbacks.Profile, sha256 = Data.Callbacks.PayloadSha256,
                bindings = Data.Callbacks.Bindings.Count },
            defensivePreSnapSwitchEnabled = Game.SourceDefensivePreSnapSwitchEnabled,
            defaultAudiblesEnabled = Game.SourceDefaultAudiblesEnabled,
            manifestSha256 = Data.Catalog.ManifestSha256,
            realtimeClock = !Options.PreGameSoundEnabled ? null : new {
                realtimeClock.ObservedSeconds, realtimeClock.HostSteps, realtimeClock.PendingSeconds,
                pauseCarrySeconds,
                measuredInputs = new {
                    observedInputs.Captures, observedInputs.Delivered, observedInputs.PendingCount,
                    observedInputs.LifecycleCleared
                },
                maximumStepsPerCallback = FixedStepBridge.MaximumStepsPerFrame,
                policy = "Retain actual elapsed debt across bounded callbacks; no source tick/RNG setter, dropped-time clamp or audio clock reanchor."
            },
            preGameAudio = preGameSounds == null ? null : new {
                phase = preGameSounds.ActivePhase.ToString(), preGameSounds.SourceTick,
                preGameSounds.HostTicks, preGameSounds.InputEpoch, preGameSounds.OriginalEntryActivated,
                preGameSounds.ReadyForGame, preGameSounds.PendingPhysicalSamples,
                callsDelivered = PreGameSoundCallsDelivered, controlsDelivered = PreGameSoundControlsDelivered,
                sharedGameDomain = gameSoundHandoff != null,
                qualification = "Actual source menu processes and immutable programme controls; portable board/RNG/audio projection, not native cabinet certification."
            },
            hostTick = state.Tick, sourceTick = Game.SourceTick, sourceTicksPerSecond = source.SourceTicksPerSecond,
            epoch = Epoch,
            cabinetSession = Session?.Describe(),
            records = Session?.Records?.Describe(),
            instructions = Instructions?.Describe(),
            cabinetFlow = HudProcess.CommittedHud.CabinetFlow,
            menu = MenuState(state),
            sourceWakes = SourceWakes, frames = Frames, phase = state.Phase.ToString(),
            matchPhase = match.Phase.ToString(), playKind = match.PlayKind.ToString(),
            playNumber = state.PlayNumber, possession = state.Possession.ToString(),
            selectedPlays = new { home = match.HomePlay, away = match.AwayPlay, match.OffenseFlipped },
            lastOutcome = state.LastOutcome.ToString(),
            teams = new { home = match.HomeGameplay, away = match.AwayGameplay },
            clock = HudProcess.CommittedHud.Clock, packedClock = ((ISourceMatchState)state).PackedGameTime,
            homeScore = state.HomeScore, awayScore = state.AwayScore, down = state.Down,
            lineOfScrimmage = state.LineOfScrimmage, firstDownLine = state.FirstDownLine, offenseDirection = state.OffenseDirection,
            actors = actorStatus, actorCount = state.Players.Count, jointsPerActor = 15,
            ball = new { mode = state.Ball.Mode.ToString(), carrier = state.Ball.CarrierId, target = state.Ball.TargetPlayerId,
                position = new[] { ball.X, ball.Y, ball.Z }, kind = state.Ball.Kind.ToString(),
                sourceTick = ballDraw.State.SourceTick, visible = ballDraw.State.Visible,
                heading = ballDraw.State.Heading, elevation = ballDraw.State.Elevation, roll = ballDraw.State.Roll,
                scale = ballDraw.State.Scale, pupBigBall = ballDraw.State.PupBigBall, rawFlags = ballDraw.State.RawFlags,
                alpha = ballDraw.Alpha, alternate = ballDraw.Alternate, white = ballDraw.White,
                worldMatrix = ballDraw.WorldMatrix, packet = ballPacket,
                sourceOrigin = SourceFreeBallData.Origin },
            ballTrails = ballTrails.Select(draw => new {
                objectSerial = draw.Trail.Value.ObjectSerial, processSerial = draw.Trail.Value.ProcessSerial,
                stage = draw.Trail.Value.Stage, hidden = draw.Trail.Value.Hidden,
                objectId = draw.Trail.Value.ObjectId, drawOrder = draw.Trail.Value.DrawOrder,
                localFlags = draw.Trail.Value.LocalFlags,
                sourceTick = draw.State.SourceTick, visible = draw.State.Visible,
                position = new[] { draw.State.Position.X, draw.State.Position.Y, draw.State.Position.Z },
                heading = draw.State.Heading, elevation = draw.State.Elevation, roll = draw.State.Roll,
                scale = draw.State.Scale, pupBigBall = draw.State.PupBigBall, rawFlags = draw.State.RawFlags,
                alpha = draw.Alpha, effectiveAlpha = draw.EffectiveAlpha, white = draw.White, alternate = draw.Alternate,
                entry = new {
                    sourceTick = draw.Trail.Value.EntrySample.SourceTick,
                    position = new[] { draw.Trail.Value.EntrySample.WorldPosition.X,
                        draw.Trail.Value.EntrySample.WorldPosition.Y, draw.Trail.Value.EntrySample.WorldPosition.Z },
                    heading = draw.Trail.Value.EntrySample.Heading, elevation = draw.Trail.Value.EntrySample.Elevation,
                    roll = draw.Trail.Value.EntrySample.Roll, scale = draw.Trail.Value.EntrySample.Scale,
                    rawFlags = draw.Trail.Value.EntrySample.RawFlags, provenance = draw.Trail.Value.EntrySample.Provenance
                },
                worldMatrix = draw.WorldMatrix, packet = FrameMath.FreeBall(draw),
                sourceOrigin = SourceFreeBallData.Origin
            }).ToArray(),
            camera = new {
                sourceTick = Camera.LastSourceTick, suspended = Camera.Suspended,
                sourceHeading = Game.SourceCameraHeading, headingSampleTick = Game.SourceCameraHeadingSampleTick,
                position = new[] { Camera.Current.Position.X, Camera.Current.Position.Y, Camera.Current.Position.Z },
                transform = Camera.Current.Transform, cueCount = Camera.CueCount,
                transient = Camera.Playback.IsTransientActive, sourceHres = Data.Bootstrap.HorizontalResolution,
                sourceVres = Data.Bootstrap.VerticalResolution, cameraTrace = Camera.Trace,
                transientTable = Camera.Playback.Frame.Table, transientOrdinal = Camera.Playback.Frame.Ordinal,
                transientElapsed = Camera.Playback.Frame.ElapsedTicks, resuming = Camera.Playback.IsResumingNormal,
                needsShakeDraws = Camera.Playback.NeedsShakeDraws,
                producer = "original-active-CAMBOT-normal-kickoff-impact",
                normalWakes = Camera.Playback.OriginalNormal.NormalWakes,
                rawGameMode = Camera.Playback.LastCameraBoundary == null ? -1 : Camera.Playback.LastCameraBoundary.RawGameMode,
                focus = new[] { Camera.Playback.SourceState.Focus.X, Camera.Playback.SourceState.Focus.Y,
                    Camera.Playback.SourceState.Focus.Z }, distance = Camera.Playback.SourceState.Distance,
                viewProjection = Camera.ViewProjection, cameraSpaceProjection = Camera.CameraSpaceProjection
            },
            possessionServices = new {
                ordering = "portable declared Core/FX slots; not a native global scheduler witness",
                cameraSleeping = ((ISourcePossessionState)state).CameraProcessSleeping,
                retainedOutputTick = ((ISourcePossessionState)state).CameraOutputSourceTick,
                previousOffense = Camera.Playback.PossessionOffensePrevious,
                timeout = Camera.Playback.PossessionTimeout,
                message = ((ISourcePossessionState)state).PossessionMessage,
                messages = ((ISourcePossessionState)state).PossessionMessages,
                processes = ((ISourcePossessionState)state).Processes,
                speech = Game.SourcePossessionSpeechSubmissions,
                hudRepresentation = "exact logical pair; complete original WIF union while both flash states agree; native Composit packing unqualified"
            },
            sourceRandom = new {
                evidence = Game.SourceRandomService.Provenance.Evidence.ToString(),
                tail = Game.SourceRandomService is SourceInjectedRandom ledger ?
                    ledger.Trace.Skip(Math.Max(0, ledger.Trace.Count - 64)).Select(draw => new {
                        order = draw.Order, sourceTick = draw.Site.SourceTick, actor = draw.Site.ActorId,
                        process = draw.Site.Process, processOrder = draw.Site.ProcessOrder,
                        callIndex = draw.Site.CallIndex, site = draw.Site.SourceIdentity,
                        bound = draw.Site.BoundOrModulus, value = draw.Value
                    }).ToArray() : Array.Empty<object>()
            },
            hud = new { primitives = hudDraw.Count, vertices = hudVertices.Length / 4,
                origin = "original authored atlas/font/draw composer; portable raster origin",
                committedSourceTick = HudProcess.CommittedSourceTick, resultPending = HudProcess.ResultPending,
                pendingResultWakes = HudProcess.PendingResultWakes,
                process = HudProcess.ResultPending ? "Actual C2 result queued; last committed authored HUD held for one source wake" : "Actual committed original HUD state",
                elements = hudDraw.Where(p => !p.PrivateContent).Select(p => p.Element).Distinct().ToArray(),
                text = hudDraw.Where(p => !p.PrivateContent && p.TextContent != null).Select(p => new { p.Element, text = p.TextContent }).Distinct().ToArray(),
                statistics = HudProcess.CommittedHud.Statistics,
                audibles = hudDraw.Where(p => !p.PrivateContent && p.Audibles != null).Select(p => new {
                    p.Element, p.SourceDepth, p.SourceImageSymbol, p.Texture, p.Color, p.Bounds,
                    sourceTick = p.Audibles.SourceTick, sameCommittedView = ReferenceEquals(p.Audibles, HudProcess.CommittedAudibles),
                    sprite = p.AudibleSprite, vertices = p.Vertices
                }).ToArray() },
            audibles = new {
                contract = Game.SourceDefaultAudiblesEnabled ? SourceAudibleView.Contract : null,
                core = match.Audibles, committed = HudProcess.CommittedAudibles,
                producerCallsTaken = SourceDirectSoundCallsTaken, producerBatchesTaken = SourceDirectSoundBatchesTaken,
                deliveredCalls = directSoundRecording.DeliveredCount, consumerBoundBeforeAdvance = directSoundOutput != null,
                deliveries = DirectSoundDeliveries,
                qualification = "Finite actual producer/recording-consumer and committed HUD/World observations; not integrated audible or browser/native/DSP qualification."
            },
            assets = new { meshes = Assets.MeshCount, textures = Assets.TextureCount,
                worldDraws = Assets.WorldDrawCount, appearances = Assets.AppearanceCount,
                poppedHead = Data.PoppedHead == null ? null : new {
                    origin = SourceTeamPoppedHeadData.Origin, sourceSha256 = SourceTeamPoppedHeadData.SourceSha256,
                    geometrySha256 = SourceTeamPoppedHeadData.HeaderSha256, manifestSha256 = Data.PoppedHead.ContentSha256,
                    headJoint = SourceTeamPoppedHeadData.HeadJoint, rawTextureCode = SourceTeamPoppedHeadData.RawTextureCode,
                    logicalTextureSlot = SourceTeamPoppedHeadData.LogicalTextureSlot, nativeHeaderEquivalent = false
                } },
            input = new { samples = InputSamples, changes = InputChanges, pending = input.PendingFrames,
                waitingForNeutral = input.WaitingForNeutral, buttons = recordScreen ? new int[4] : lastButtons,
                axes = recordScreen ? new double[8] : lastAxes },
            active = Active, events = events.ToArray(), eventCounts, contacts = contacts.ToArray(), contactCounts,
            audio = new { output = AudioPlayback == null ? "Audio output unqualified" :
                    "Explicit qualified PCM command output; browser hearing/native DSP equivalence unverified",
                decoded = AudioPlayback == null ? null : new {
                    admission = AudioPlayback.AdmissionName, policy = AudioPlayback.PlaybackPolicy,
                    epoch = AudioPlayback.Epoch, sourceTick = AudioPlayback.SourceTick,
                    commandsSubmitted = AudioPlayback.SubmittedCommands, scheduledCalls = AudioPlayback.ScheduledCalls,
                    rejectedCalls = AudioPlayback.RejectedCalls, pending = AudioPlayback.PendingCount,
                    failure = AudioPlayback.Failure, audibleVerified = false, nativeDspEquivalent = false
                },
                selections = SoundSelections,
                unplayedRawCalls = UnqualifiedSoundCalls, preSnapSwitchSelections = PreSnapSwitchSoundSelections,
                resolvedHopSelections = hopSounds.ToArray(), orderedDeliveries = orderedSoundDeliveries.ToArray(),
                directDeliveries = DirectSoundDeliveries, processDeliveries = ProcessSoundDeliveries,
                processCallsTaken = SourceProcessSoundCallsTaken, processCallsDelivered = SourceProcessSoundCallsDelivered },
            effects = Fire.Describe(),
            visualCallbacks = new { dust = DustCallbacks, boundDust = BoundDustCallbacks,
                unqualified = UnqualifiedVisualCallbacks,
                cues = dustCues.ToArray(),
                qualification = "Original source processes and authored DIRT rendering; not native pixels or target PRNG." },
            kick = new { mode = kicking.KickMeterMode.ToString(), meter = kicking.KickMeter,
                lastLaunch = kicking.LastKickLaunch, metrics = kicking.KickMeterMetrics,
                origin = "portable-authored HUD metrics; NOT native Composit/generated dimensions" },
            limitations = new[] {
                "Not a Unity player build, native Unity qualification or bit-exact arcade acceptance.",
                "Authored TGA/portable HUD raster origin; missing Texus/WMS quantization is not reconstructed.",
                "Original active CAMBOT normal/tracking/integrator, kickoff and special-return source stages; " +
                    "host comparisons are separate from native fast trig/global scheduler/initial QB-box/possession-message services.",
                "Original medball2 SDL/HEDSHU and source-entry football ghosts; native generated header/FMA/WBUFFER/pixels/global scheduler remain unqualified.",
                "Production default preview RNG is explicitly unverified against the historical platform PRNG.",
                "No PCM/DSP output or synthetic sound. Dust callbacks are not substituted with generic particles.",
                KernelBinding.IsPortable(Game) ? "Portable repairs are explicit; StrictSource is not silently changed." :
                    "The required portable factory option is not bound: not player-ready."
            }
        });
        if (World != null) LastStatus = World.Packet.AttachStatus(LastStatus);
        var receipt = RenderObservationEnabled ? new SourceRenderFrameReceipt(Epoch, Frames, previous.Tick, state.Tick,
            (previous as ISourceGameState)?.SourceTick, (state as ISourceGameState)?.SourceTick,
            (previous as ISourceGameState)?.SourceTicksPerSecond, (state as ISourceGameState)?.SourceTicksPerSecond,
            observedActors[0].PoseObservation.RenderInterpolation01, observedActors, joints, modelIds, appearanceIds, LastStatus) : null;
        sink.Present(Camera.ViewProjection, joints, modelIds, appearanceIds,
            FrameMath.FreeBalls(ballDraw, ballTrails),
            hudVertices, hudDraw.Select(p => unchecked((int)p.Color)).ToArray(),
            hudDraw.Select(p => "hud/" + p.Texture).ToArray(), hudDraw.Select(p => p.Vertices.Count).ToArray(), LastStatus);
        if (receipt != null && receipt.Epoch == Epoch) LatestRenderObservation = receipt;
    }

    private static bool Project(double[] matrix, SourceVector3 point, SourceHudViewport viewport, out double x, out double y)
    {
        double w = matrix[3] * point.X + matrix[7] * point.Y + matrix[11] * point.Z + matrix[15];
        x = y = 0;
        if (w <= 0) return false;
        double nx = (matrix[0] * point.X + matrix[4] * point.Y + matrix[8] * point.Z + matrix[12]) / w;
        double ny = (matrix[1] * point.X + matrix[5] * point.Y + matrix[9] * point.Z + matrix[13]) / w;
        if (Math.Abs(nx) > 1 || Math.Abs(ny) > 1) return false;
        x = (nx + 1) * viewport.ContentWidth / 2;
        y = (1 - ny) * viewport.ContentHeight / 2;
        return true;
    }

    private object MenuState(IGameState state)
    {
        if (state.Phase != GamePhase.PlaySelect) return null;
        SourceMatchView match = ((ISourceMatchState)state).Match;
        object Side(TeamId side) => new {
            page = selection.Page(side), cursor = selection.Cursor(side), selected = selection.Selected(side),
            slots = Enumerable.Range(0, 9).Select(i => {
               if (side == match.Possession.Offense && selection.Page(side) == 4)
               {
                   var custom = selection.CustomPlay(state, side, i);
                   return (object)new { index = i, name = custom == null ? null : custom.Name1 + " " + custom.Name2,
                       symbol = (string)null, customAvailable = custom != null, sourceSlot = i };
               }
               var slot = selection.Slot(state, side, selection.Page(side), i);
               return (object)new { index = i, name = slot.Name, symbol = slot.PlaySymbol };
            }).ToArray()
        };
        return new { home = Side(TeamId.Home), away = Side(TeamId.Away),
            countdown = match.SelectionCountdown, timer = match.SelectionTimer };
    }

    private static string PoseDigest(SourceEvaluatedPose pose)
    {
        ulong hash = 14695981039346656037;
        if (pose.IsRawAffine)
        {
            hash = unchecked((hash ^ 1u) * 1099511628211);
            hash = unchecked((hash ^ BitConverter.SingleToUInt32Bits(pose.ModelScale)) * 1099511628211);
            foreach (var joint in pose.Joints)
            {
                SourceAffineBasis basis = joint.AffineBasis;
                foreach (float value in new[] { joint.Position.X, joint.Position.Y, joint.Position.Z,
                    basis.M00, basis.M01, basis.M02, basis.M10, basis.M11, basis.M12,
                    basis.M20, basis.M21, basis.M22 })
                    hash = unchecked((hash ^ BitConverter.SingleToUInt32Bits(value)) * 1099511628211);
            }
            return hash.ToString("x16");
        }
        foreach (var joint in pose.Joints)
            foreach (float value in new[] { joint.Position.X, joint.Position.Y, joint.Position.Z,
                joint.Rotation.X, joint.Rotation.Y, joint.Rotation.Z, joint.Rotation.W })
                hash = unchecked((hash ^ BitConverter.SingleToUInt32Bits(value)) * 1099511628211);
        return hash.ToString("x16");
    }

    private static void Enqueue(Queue<object> queue, object value, int maximum)
    {
        queue.Enqueue(value);
        while (queue.Count > maximum) queue.Dequeue();
    }
}
