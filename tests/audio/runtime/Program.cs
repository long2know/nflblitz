using System;
using System.Collections.Generic;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal static partial class Program
{
    private static int assertions;
    private static int actualAssertions;
    private static void Check(bool value, string name)
    {
        assertions++;
        if (!value) throw new InvalidOperationException("ASSERT: " + name);
    }
    private static void Reject(Action action, string name)
    {
        bool failed = false;
        try { action(); }
        catch (InvalidOperationException) { failed = true; }
        Check(failed, name);
    }

    private static SourceCallbackCue Cue(long tick, int order, int actor = 4)
    {
        return new SourceCallbackCue(new SourcePresentationContext(tick, order, actor,
            "plyrseq.c", "t_dshoul_anim", 2448), SourcePresentationCueKind.SoundCallback,
            "get_hit_snd", 57, 0, 0);
    }
    private static SourceSoundVmDispatch Dispatch(SourceCallbackCue cue, int calls = 0,
        SourceSoundVmStatus status = SourceSoundVmStatus.Resolved)
    {
        var output = new List<SourceAudioResolvedOriginalSoundCall>();
        for (int i = 0; i < calls; i++)
            output.Add(new SourceAudioResolvedOriginalSoundCall(cue,
                new SourceAudioCalleeSite(new string('0', 40), "synthetic_sound", 10 + i),
                i, "SYNTHETIC", 50 + i, uint.MaxValue - (uint)i, 0x87654321u, 0xfedcba98u, i));
        return new SourceSoundVmDispatch(cue,
            new SourceRandomContext(cue.Context.SourceTick, cue.Context.ActorPlayerId, "synthetic_actor", 3, "synthetic", 7),
            new SourceAudioFunctionState(2, 7, 4, -1, SourceAudioHitPartner.Absent, 4, -1, "SYNTHETIC"),
            status, status == SourceSoundVmStatus.Unsupported ? null :
                new SourceAudioFunctionResult(output, new List<SourceRandomDraw>()),
            status == SourceSoundVmStatus.MissingInput ? "synthetic missing input" : null);
    }
    private static SourceSoundVmBatch Batch(params SourceSoundVmDispatch[] records)
    { return new SourceSoundVmBatch(records); }

    private sealed class RecordingOutput : ISourceSoundVmOutputConsumer
    {
        internal readonly List<SourceSoundVmDispatch> Received = new List<SourceSoundVmDispatch>();
        internal bool Throw;
        public void ConsumeResolved(SourceSoundVmDispatch dispatch)
        {
            Received.Add(dispatch);
            if (Throw) throw new InvalidOperationException("synthetic output fault");
        }
    }

    private static void Transport()
    {
        var transport = new SourceSoundVmTransport();
        SourceCallbackCue zero = Cue(1, 10), second = Cue(1, 20), third = Cue(3, 1);
        SourceSoundVmDispatch first = Dispatch(zero), nonempty = Dispatch(second, 2), later = Dispatch(third);
        SourceSoundVmBatch retained = Batch(first, nonempty);
        transport.Drain(retained);
        transport.Drain(Batch());
        Check(transport.PendingCount == 2, "empty/skipped drain cannot discard pending records");
        Check(transport.TakeForCue(Cue(1, 10)) == null, "context-equivalent reconstructed cue is not OriginalCue");
        Reject(() => transport.TakeForCue(second), "raw callbacks cannot reorder FIFO dispatches");
        Check(ReferenceEquals(transport.TakeForCue(zero), first), "first exact original cue pairs once");
        bool acceptedZero = true;
        try { transport.DeliverResolved(first); }
        catch (InvalidOperationException) { acceptedZero = false; }
        Check(acceptedZero && transport.Failure == null, "legitimate resolved zero calls need no output consumer");
        Reject(() => transport.TakeForCue(zero), "duplicate original raw cue cannot replay");
        Check(ReferenceEquals(transport.TakeForCue(second), nonempty), "second exact original cue retains record");
        Reject(() => transport.CompletePresentationBatch(), "paired nonempty sound cannot be silently undelivered");
        Reject(() => transport.DeliverResolved(Dispatch(second)), "forged dispatch with same cue cannot replace Core result");
        Reject(() => transport.DeliverResolved(nonempty), "nonempty calls without real output fail explicitly");
        Check(ReferenceEquals(transport.FailedDispatch, nonempty), "failed raw output retains exact resolved record");
        var output = new RecordingOutput();
        transport.BindOutput(output);
        Reject(() => transport.DeliverResolved(nonempty), "binding output after fault cannot retry old calls");
        Check(output.Received.Count == 0, "failed output cannot replay after late binding");
        transport.Reset();
        transport.Drain(Batch(later));
        transport.DeliverResolved(transport.TakeForCue(third));
        transport.CompletePresentationBatch();
        Check(transport.PendingCount == 0 && transport.Failure == null, "explicit restart clears pending/fault state");
        Check(retained.Dispatches.Count == 2 && ReferenceEquals(retained.Dispatches[1], nonempty),
            "retained Core batch unchanged after drain and restart");

        transport.Reset();
        SourceCallbackCue pair = Cue(5, 0);
        SourceSoundVmDispatch raw = Dispatch(pair, 2);
        transport.Drain(Batch(raw));
        transport.DeliverResolved(transport.TakeForCue(pair));
        transport.CompletePresentationBatch();
        Check(output.Received.Count == 1 && ReferenceEquals(output.Received[0], raw),
            "already selected raw dispatch delivered once without reconstruction");
        Check(raw.Result.Calls[0].Volume == uint.MaxValue && raw.Result.Calls[1].Volume == uint.MaxValue - 1 &&
            raw.Result.Calls[0].Pan == 0x87654321u && raw.Result.Calls[0].PriorityWord == 0xfedcba98u &&
            raw.Result.Calls[1].PlaylistId == 51, "all raw output words and subcall order remain exact");
        Reject(() => transport.DeliverResolved(raw), "resolved output cannot be delivered twice");
        Reject(() => transport.BindOutput(new RecordingOutput()), "real output consumer cannot be silently replaced");
        transport.BindOutput(output);

        transport.Reset();
        SourceSoundVmDispatch unsupported = Dispatch(Cue(6, 0), status: SourceSoundVmStatus.Unsupported);
        transport.Drain(Batch(unsupported));
        Check(transport.TakeForCue(unsupported.OriginalCue).Status == SourceSoundVmStatus.Unsupported,
            "unsupported retains explicit status rather than resolved silence");
        Reject(() => transport.DeliverResolved(unsupported), "unsupported is not a resolved output");
        Check(output.Received.Count == 1, "unsupported never reaches raw output");

        transport.Reset();
        SourceSoundVmDispatch partial = Dispatch(Cue(7, 0), 2, SourceSoundVmStatus.MissingInput);
        transport.Drain(Batch(partial));
        transport.Drain(Batch());
        Check(ReferenceEquals(transport.FailedDispatch, partial) && partial.Result.Calls.Count == 2,
            "partial failure retains calls and context across empty drains");
        Reject(() => transport.TakeForCue(partial.OriginalCue), "partial selection faults until explicit restart");
        Check(output.Received.Count == 1, "partial selector result is never played as resolved output");

        transport.Reset();
        output.Throw = true;
        SourceSoundVmDispatch throwing = Dispatch(Cue(8, 0), 1);
        transport.Drain(Batch(throwing));
        transport.TakeForCue(throwing.OriginalCue);
        Reject(() => transport.DeliverResolved(throwing), "real output failure propagates");
        Reject(() => transport.DeliverResolved(throwing), "failed output remains sticky and cannot retry");
        Check(output.Received.Count == 2 && ReferenceEquals(transport.FailedDispatch, throwing),
            "failed output executed only once and retained exact record");
        output.Throw = false;
        transport.Reset();
        transport.Drain(Batch(Dispatch(Cue(9, 3))));
        Reject(() => transport.CompletePresentationBatch(), "unpaired drained record is visible failure");
        Reject(() => transport.Drain(Batch(Dispatch(Cue(9, 2)))), "Core dispatch source/instruction order is preserved");
        transport.Reset();
        var badCue = Cue(10, 0);
        var badCall = new SourceAudioResolvedOriginalSoundCall(Cue(10, 0), null, 0, "SYNTHETIC", 1, 1, 1, 1, 0);
        var bad = new SourceSoundVmDispatch(badCue, default(SourceRandomContext), default(SourceAudioFunctionState),
            SourceSoundVmStatus.Resolved, new SourceAudioFunctionResult(new List<SourceAudioResolvedOriginalSoundCall> { badCall },
                new List<SourceRandomDraw>()), null);
        Reject(() => transport.Drain(Batch(bad)), "raw subcall cannot replace exact OriginalCue identity");
        var badOrder = new SourceAudioResolvedOriginalSoundCall(badCue, null, 1, "SYNTHETIC", 1, 1, 1, 1, 0);
        var unordered = new SourceSoundVmDispatch(badCue, default(SourceRandomContext), default(SourceAudioFunctionState),
            SourceSoundVmStatus.Resolved, new SourceAudioFunctionResult(new List<SourceAudioResolvedOriginalSoundCall> { badOrder },
                new List<SourceRandomDraw>()), null);
        transport.Reset();
        Reject(() => transport.Drain(Batch(unordered)), "raw subcalls must retain zero-based order");
    }

    private sealed class BridgeFixture : IGameSimulation, ISourcePresentationSimulation
    {
        private readonly GameSimulation legacy = new GameSimulation(1);
        private readonly List<SourceSoundVmDispatch> records = new List<SourceSoundVmDispatch>();
        internal bool Fail;
        internal int Drains;
        public IGameState State { get { return legacy.State; } }
        public IReadOnlyList<GameEvent> Events { get { return legacy.Events; } }
        public IReadOnlyList<SourceGameEvent> SourceEvents { get { return new SourceGameEvent[0]; } }
        public void ClearPendingInput() { legacy.ClearPendingInput(); }
        public SourcePresentationCueBatch PresentationCues { get; private set; } =
            new SourcePresentationCueBatch(new SourcePresentationCue[0]);
        public void Step(PlayerCommand command)
        {
            legacy.Step(command);
            if (State.Tick % 2 == 1 || Fail)
            {
                SourceCallbackCue cue = Cue(State.Tick, 0);
                records.Add(Dispatch(cue, Fail ? 1 : 0, Fail ? SourceSoundVmStatus.MissingInput : SourceSoundVmStatus.Resolved));
                PresentationCues = new SourcePresentationCueBatch(new[] { cue });
            }
            else PresentationCues = new SourcePresentationCueBatch(new SourcePresentationCue[0]);
            if (Fail) throw new InvalidOperationException("synthetic producer fault after partial record");
        }
        public SourceSoundVmBatch Take()
        {
            Drains++;
            var batch = new SourceSoundVmBatch(records);
            records.Clear();
            return batch;
        }
        public void Restart(int seed)
        { legacy.Restart(seed); records.Clear(); Fail = false; }
    }

    private static void Bridge()
    {
        var producer = new BridgeFixture();
        var transport = new SourceSoundVmTransport();
        var bridge = new FixedStepBridge(producer);
        var order = new List<string>();
        bridge.AfterSimulationStep += () => { order.Add("drain" + producer.State.Tick); transport.Drain(producer.Take()); };
        bridge.SourcePresentationCueRaised += cue => {
            order.Add("cue" + cue.Context.SourceTick);
            SourceSoundVmDispatch record = transport.TakeForCue((SourceCallbackCue)cue);
            Check(record != null && record.Status == SourceSoundVmStatus.Resolved, "bridge drain precedes exact raw callback");
            transport.DeliverResolved(record);
        };
        bridge.AfterSourcePresentation += transport.CompletePresentationBatch;
        bridge.AfterHostStep += state => order.Add("host" + state.Tick);
        bridge.Restarted += transport.Reset;
        Check(bridge.Advance(.1, default(FieldVector), false) == 6, "host catchup executes six ordered steps");
        Check(producer.Drains == 6, "sound batch is drained after every host step including source skips");
        for (int tick = 1; tick <= 6; tick++)
        {
            Check(order.IndexOf("drain" + tick) < order.IndexOf("host" + tick), "drain precedes host observer");
            if (tick % 2 == 1)
                Check(order.IndexOf("drain" + tick) < order.IndexOf("cue" + tick) &&
                    order.IndexOf("cue" + tick) < order.IndexOf("host" + tick), "each catchup raw cue follows its own drain");
        }
        Check(bridge.Advance(0, default(FieldVector), false) == 0 && producer.Drains == 6,
            "render without host step cannot replay a sound batch");
        producer.Fail = true;
        Reject(() => bridge.Advance(FixedStepBridge.StepSeconds, default(FieldVector), false),
            "partial producer failure propagates through bridge");
        Check(producer.Drains == 7 && transport.FailedDispatch != null &&
            transport.FailedDispatch.Result.Calls.Count == 1, "finally drain retains partial record on failed Step");
        SourceSoundVmBatch retained = transport.LastBatch;
        bridge.Restart();
        Check(transport.Failure == null && transport.PendingCount == 0 && producer.State.Tick == 0,
            "actual bridge restart clears transport without selection replay");
        Check(retained.Dispatches[0].Result.Calls.Count == 1, "detached partial batch survives restart");
        Check(bridge.Advance(.1, default(FieldVector), false) == 0 && producer.Drains == 7,
            "restart discard frame cannot replay sound");
        var core = new GameSimulation(1);
        SourceSoundVmBatch first = core.TakeSourceSoundVmDispatches();
        core.Step(default(PlayerCommand));
        SourceSoundVmBatch second = core.TakeSourceSoundVmDispatches();
        core.Restart(1);
        Check(first.Dispatches.Count == 0 && second.Dispatches.Count == 0 && core.TakeSourceSoundVmDispatches().Dispatches.Count == 0,
            "actual legacy Core empty FIFO batches remain detached across step/restart");
    }

    private static int Main(string[] args)
    {
        try
        {
            Transport(); Bridge();
            if (args.Length != 0) ActualCore(args[0], args[1]);
            Console.WriteLine(JsonSerializer.Serialize(new { schema = "source-sound-runtime-checks/v1",
                status = "passed", assertions = assertions, actual_assertions = actualAssertions,
                native_Unity_DSP_or_audible_claimed = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { schema = "source-sound-runtime-checks/v1",
                status = "failed", assertions = assertions, failed_assertion = error.Message }));
            return 1;
        }
    }
}
