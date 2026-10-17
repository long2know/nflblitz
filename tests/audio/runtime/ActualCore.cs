using System;
using System.Linq;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

internal static partial class Program
{
    private static GameSimulation Actual(FullKernelInputs inputs)
    {
        return inputs.Create(randomFactory: seed => new SourceInjectedRandom(site => 17,
            new SourceRandomProvenance("runtime-transport/test", "explicit functional tape, not target PRNG",
                "constant17", SourceRandomEvidence.RecordedOracle)));
    }

    private static void ActualCore(string fixture, string soundFunctions)
    {
        int start = assertions;
        FullKernelInputs inputs = FullKernelInputs.Load(fixture, movementLibrary: true, soundFunctionsPath: soundFunctions);
        foreach (bool onscreen in new[] { false, true })
        {
            GameSimulation game = Actual(inputs);
            game.TakeSourceSoundVmDispatches();
            game.Model.SourceTick = 10; game.Model.SourcePresentationOrder = 0;
            ActorState actor = game.Model.Players[4];
            actor.SourceMode = SourceActorMode.Receiver;
            actor.SourceHitPartnerId = -1;
            actor.SourceRawFlags = onscreen ? inputs.SoundFunctions.OnscreenFlag : 0;
            game.Model.CarrierId = actor.Id;
            actor.AnimationState.FullBody("t_dshoul_anim", actor, 10, maximum => 0,
                AnimationSelectionEvidence.SourceVerified);
            SourceSoundVmBatch batch = game.TakeSourceSoundVmDispatches();
            Check(batch.Dispatches.Count == 1 && batch.Dispatches[0].Status == SourceSoundVmStatus.Resolved,
                "actual unchanged Core/ANI_CODE emits one detached resolved dispatch");
            SourceSoundVmDispatch dispatch = batch.Dispatches[0];
            Check(dispatch.OriginalCueContext.SourceLine == 2448 && dispatch.OriginalCue.Argument0 == 57,
                "actual VM tuple and explicit original operands retained");
            Check(dispatch.Result.Calls.Count == (onscreen ? 1 : 0),
                "actual onscreen output versus legitimate offscreen zero result distinguished");
            var transport = new SourceSoundVmTransport();
            var sink = new RecordingOutput();
            transport.BindOutput(sink);
            transport.Drain(batch);
            Check(ReferenceEquals(transport.TakeForCue(dispatch.OriginalCue), dispatch),
                "real Core batch pairs by the actual OriginalCue object");
            transport.DeliverResolved(dispatch);
            transport.CompletePresentationBatch();
            Check(sink.Received.Count == (onscreen ? 1 : 0), "actual raw output handed off once, never synthesized");
            Check(game.TakeSourceSoundVmDispatches().Dispatches.Count == 0, "actual Core FIFO is drained once only");
            game.Restart(1);
            Check(batch.Dispatches.Count == 1 && ReferenceEquals(batch.Dispatches[0], dispatch),
                "real retained Core batch survives restart unchanged");
            Check(game.TakeSourceSoundVmDispatches().Dispatches.Count == 0, "actual Core restart clears pending transport");
            game.Step(default(PlayerCommand)); game.Step(default(PlayerCommand));
            Check(game.SourceTick > 0, "Core restart retains immutable sound binding without rebind");
        }

        GameSimulation partial = Actual(inputs);
        partial.TakeSourceSoundVmDispatches();
        partial.Model.SourceTick = 20; partial.Model.SourcePresentationOrder = 0;
        ActorState line = partial.Model.Players[4], partner = partial.Model.Players[8];
        line.SourceMode = partner.SourceMode = SourceActorMode.Line;
        line.SourceHitPartnerId = partner.Id;
        line.SourceRawFlags = inputs.SoundFunctions.OnscreenFlag;
        partial.Model.SourceCurrentPlayerBank = null;
        long before = partial.Model.SourceRandomDraws;
        Reject(() => line.AnimationState.FullBody("t_dshoul_anim", line, 20, maximum => 0,
            AnimationSelectionEvidence.SourceVerified), "actual sound selector missing input faults in ANI_CODE");
        SourceSoundVmBatch fault = partial.TakeSourceSoundVmDispatches();
        Check(fault.Dispatches.Count == 1 && fault.Dispatches[0].Status == SourceSoundVmStatus.MissingInput,
            "actual Core retains missing-input dispatch");
        Check(fault.Dispatches[0].Result.RandomCalls == 1 && partial.Model.SourceRandomDraws == before + 1,
            "actual partial sound draw and consumed offset are retained once");
        var stopped = new SourceSoundVmTransport();
        stopped.Drain(fault);
        Reject(() => stopped.TakeForCue(fault.Dispatches[0].OriginalCue), "actual partial result cannot be retried or played");
        long consumed = partial.Model.SourceRandomDraws;
        bool rejected = false;
        for (int i = 0; i < 3 && !rejected; i++)
            try { partial.Step(default(PlayerCommand)); }
            catch (InvalidOperationException) { rejected = true; }
        Check(rejected && partial.Model.SourceRandomDraws == consumed,
            "actual Core remains faulted without RNG rollback/retry until Restart");
        partial.Restart(1); stopped.Reset();
        partial.Step(default(PlayerCommand)); partial.Step(default(PlayerCommand));
        Check(partial.SourceTick > 0 && fault.Dispatches[0].Result.RandomCalls == 1,
            "actual restart retains binding while old detached partial result is immutable");

        GameSimulation host = Actual(inputs);
        var bridge = new FixedStepBridge(host);
        var queued = new SourceSoundVmTransport();
        var recorded = new RecordingOutput();
        queued.BindOutput(recorded);
        int drains = 0;
        long drainedTick = -1;
        bridge.AfterSimulationStep += () => {
            drains++; queued.Drain(host.TakeSourceSoundVmDispatches()); drainedTick = host.SourceTick;
        };
        bridge.SourcePresentationCueRaised += cue => {
            Check(drainedTick == host.SourceTick, "actual Core FIFO drain precedes its raw presentation batch");
            SourceCallbackCue callback = cue as SourceCallbackCue;
            if (callback == null || callback.Kind == SourcePresentationCueKind.PlayerDownCallback) return;
            SourceSoundVmDispatch item = queued.TakeForCue(callback);
            if (item != null && item.Status == SourceSoundVmStatus.Resolved) queued.DeliverResolved(item);
        };
        bridge.AfterSourcePresentation += queued.CompletePresentationBatch;
        bridge.Restarted += queued.Reset;
        Func<SourceInputFrame> neutral = () => new SourceInputFrame(
            ((ISourceMatchState)host.State).Match.Configuration.Stations.Select(station =>
                new SourceStationInput(station.StationId)).ToArray());
        Check(bridge.AdvanceSource(.1, neutral) == 6 && drains == 6,
            "actual57 Core catchup drains every host step including first source skip");
        Check(host.SourceTick == 5, "actual60-to57 source adapter is not replaced by sound transport clock");
        Check(bridge.AdvanceSource(0, neutral) == 0 && drains == 6,
            "actual Core no-step render does not drain/replay sound");
        Check(queued.PendingCount == 0 && host.TakeSourceSoundVmDispatches().Dispatches.Count == 0,
            "actual Core transport ends with no unmatched or undrained VM record");
        bridge.Restart();
        Check(queued.PendingCount == 0 && queued.Failure == null && host.SourceTick == 0,
            "actual Core/bridge restart clears transport and retains immutable binding");
        Check(bridge.AdvanceSource(.1, neutral) == 0 && drains == 6,
            "actual source restart discarded host frame cannot replay");
        actualAssertions += assertions - start;
    }
}
