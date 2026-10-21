using System;
using System.Collections.Generic;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.Runtime
{
    public interface ISourceSoundVmOutputConsumer
    {
        void ConsumeResolved(SourceSoundVmDispatch dispatch);
    }

    public sealed class SourceSoundVmTransport
    {
        private readonly Queue<SourceSoundVmDispatch> pending = new Queue<SourceSoundVmDispatch>();
        private readonly HashSet<SourceCallbackCue> received = new HashSet<SourceCallbackCue>();
        private readonly Dictionary<SourceCallbackCue, SourceSoundVmDispatch> paired =
            new Dictionary<SourceCallbackCue, SourceSoundVmDispatch>();
        private readonly HashSet<SourceCallbackCue> delivered = new HashSet<SourceCallbackCue>();
        private readonly HashSet<SourceCallbackCue> awaitingOutput = new HashSet<SourceCallbackCue>();
        private readonly HashSet<SourceSoundVmDispatch> receivedDispatches = new HashSet<SourceSoundVmDispatch>();
        private readonly HashSet<SourceSoundVmDispatch> nativePaired = new HashSet<SourceSoundVmDispatch>();
        private readonly HashSet<SourceSoundVmDispatch> nativeDelivered = new HashSet<SourceSoundVmDispatch>();
        private ISourceSoundVmOutputConsumer output;
        private string failure;
        private long lastTick = -1;
        private int lastOrder = -1;
        private int lastCallIndex = -1, lastAnimationOrder = -1;
        private long lastAnimationTick = -1;

        public SourceSoundVmBatch LastBatch { get; private set; }
        public SourceSoundVmDispatch FailedDispatch { get; private set; }
        public int PendingCount { get { return pending.Count; } }
        public string Failure { get { return failure; } }

        public void BindOutput(ISourceSoundVmOutputConsumer consumer)
        {
            if (consumer == null) throw new ArgumentNullException(nameof(consumer));
            if (output != null && !ReferenceEquals(output, consumer))
                throw new InvalidOperationException("A real source raw-output consumer is already bound.");
            output = consumer;
        }

        public void Drain(SourceSoundVmBatch batch)
        {
            if (batch == null) throw new ArgumentNullException(nameof(batch));
            LastBatch = batch;
            foreach (SourceSoundVmDispatch dispatch in batch.Dispatches)
            {
                if (dispatch == null || !receivedDispatches.Add(dispatch))
                    throw new InvalidOperationException("A source sound dispatch cannot be null or drained twice.");
                long tick = dispatch.HasOriginalAnimationCue ? dispatch.OriginalCueContext.SourceTick :
                    RequireNativeContext(dispatch).SourceTick;
                int order = dispatch.RandomContext.ProcessOrder, callIndex = dispatch.RandomContext.FirstCallIndex;
                if (tick < lastTick || tick == lastTick &&
                    (order < lastOrder || order == lastOrder && callIndex < lastCallIndex))
                    throw new InvalidOperationException("Source sound dispatches must retain executing tick/instruction FIFO order.");
                if (dispatch.HasOriginalAnimationCue)
                {
                    SourcePresentationContext context = dispatch.OriginalCueContext;
                    if (dispatch.OriginalCue.Kind == SourcePresentationCueKind.PlayerDownCallback ||
                        !received.Add(dispatch.OriginalCue) || context.SourceTick < lastAnimationTick ||
                        context.SourceTick == lastAnimationTick && context.ProcessOrder <= lastAnimationOrder)
                        throw new InvalidOperationException("Actual animation sound cues retain unique instruction order; PlayerDown is not a sound dispatch.");
                    lastAnimationTick = context.SourceTick; lastAnimationOrder = context.ProcessOrder;
                }
                ValidateResult(dispatch);
                pending.Enqueue(dispatch);
                lastTick = tick; lastOrder = order; lastCallIndex = callIndex;
                if (dispatch.Status == SourceSoundVmStatus.MissingInput)
                {
                    failure = "Original sound selection failed after partial execution: " + dispatch.Detail + ". Restart is required.";
                    FailedDispatch = dispatch;
                }
            }
        }

        public SourceSoundVmDispatch TakeForCue(SourceCallbackCue cue)
        {
            if (cue == null) throw new ArgumentNullException(nameof(cue));
            RequireHealthy();
            FlushNativePrefix();
            if (paired.ContainsKey(cue))
                throw new InvalidOperationException("An original sound callback cannot be consumed twice.");
            if (pending.Count == 0) return null;
            if (!ReferenceEquals(pending.Peek().OriginalCue, cue))
            {
                foreach (SourceSoundVmDispatch later in pending)
                    if (later.HasOriginalAnimationCue && ReferenceEquals(later.OriginalCue, cue))
                        throw new InvalidOperationException("Raw presentation callbacks cannot reorder resolved sound dispatches.");
                return null;
            }
            SourceSoundVmDispatch dispatch = pending.Dequeue();
            paired.Add(cue, dispatch);
            if (dispatch.Status == SourceSoundVmStatus.Resolved && dispatch.Result.Calls.Count != 0)
                awaitingOutput.Add(cue);
            return dispatch;
        }

        public void DeliverResolved(SourceSoundVmDispatch dispatch)
        {
            if (dispatch == null) throw new ArgumentNullException(nameof(dispatch));
            RequireHealthy();
            SourceSoundVmDispatch actual;
            bool pairedOnce = dispatch.HasOriginalAnimationCue ?
                paired.TryGetValue(dispatch.OriginalCue, out actual) && ReferenceEquals(actual, dispatch) &&
                    delivered.Add(dispatch.OriginalCue) :
                nativePaired.Contains(dispatch) && nativeDelivered.Add(dispatch);
            if (dispatch.Status != SourceSoundVmStatus.Resolved || dispatch.Result == null ||
                !pairedOnce)
                throw new InvalidOperationException("Only one paired, actually resolved sound record may reach raw output.");
            if (dispatch.Result.Calls.Count == 0) return;
            if (output == null)
            {
                failure = "Resolved original sound has nonempty raw calls but no REAL DSP3/raw-output consumer is bound. Nothing was played.";
                FailedDispatch = dispatch;
                throw new InvalidOperationException(failure);
            }
            failure = "Real source raw-output delivery did not complete. No retry/reselection is allowed; restart is required.";
            FailedDispatch = dispatch;
            output.ConsumeResolved(dispatch);
            if (dispatch.HasOriginalAnimationCue) awaitingOutput.Remove(dispatch.OriginalCue);
            failure = null; FailedDispatch = null;
        }

        public void FlushNativePrefix()
        {
            RequireHealthy();
            while (pending.Count != 0 && !pending.Peek().HasOriginalAnimationCue)
            {
                SourceSoundVmDispatch dispatch = pending.Dequeue();
                RequireNativeContext(dispatch);
                if (dispatch.Status != SourceSoundVmStatus.Resolved)
                {
                    failure = "Native original process sound is not a resolved output: " + dispatch.Detail;
                    FailedDispatch = dispatch;
                    throw new InvalidOperationException(failure);
                }
                nativePaired.Add(dispatch);
                DeliverResolved(dispatch);
            }
        }

        public static int ActorIdOf(SourceSoundVmDispatch dispatch)
        {
            if (dispatch == null) throw new ArgumentNullException(nameof(dispatch));
            return dispatch.HasOriginalAnimationCue ? dispatch.OriginalCueContext.ActorPlayerId :
                RequireNativeContext(dispatch).ActorId;
        }

        public void CompletePresentationBatch()
        {
            RequireHealthy();
            FlushNativePrefix();
            if (pending.Count != 0)
                throw new InvalidOperationException("Drained source sound records have no matching original presentation callbacks.");
            if (awaitingOutput.Count != 0)
                throw new InvalidOperationException("A nonempty resolved sound was paired but never delivered to a real output consumer.");
        }

        public void Reset()
        {
            pending.Clear(); received.Clear(); paired.Clear(); delivered.Clear(); awaitingOutput.Clear();
            receivedDispatches.Clear(); nativePaired.Clear(); nativeDelivered.Clear();
            failure = null; lastTick = -1; lastOrder = -1; LastBatch = null; FailedDispatch = null;
            lastCallIndex = -1; lastAnimationTick = -1; lastAnimationOrder = -1;
        }

        private void RequireHealthy()
        {
            if (failure != null) throw new InvalidOperationException(failure);
        }

        private static void ValidateResult(SourceSoundVmDispatch dispatch)
        {
            SourceC2SoundContext c2 = dispatch.C2Context;
            if (c2 != null && (c2.SourceTick != dispatch.RandomContext.SourceTick ||
                c2.Process != dispatch.RandomContext.Process || c2.ProcessOrder != dispatch.RandomContext.ProcessOrder ||
                c2.ActorId != dispatch.RandomContext.ActorId || c2.FirstCallIndex != dispatch.RandomContext.FirstCallIndex))
                throw new InvalidOperationException("C2 sound retains its actual source process/call cursor, not a presentation clock.");
            if (c2 != null && dispatch.HasOriginalAnimationCue &&
                (!c2.HasOriginalAnimationCue || !ReferenceEquals(c2.OriginalAnimationCue, dispatch.OriginalCue) ||
                    c2.Kind != SourceC2SoundFunctionKind.TauntSpeech && c2.Kind != SourceC2SoundFunctionKind.HitFunctionExtension &&
                    c2.Kind != SourceC2SoundFunctionKind.AttackSoundFixed))
                throw new InvalidOperationException("C2 animation sound must retain its real cue and exact typed discriminator.");
            if (c2 != null && c2.Kind == SourceC2SoundFunctionKind.HitFunctionExtension &&
                (!dispatch.HasOriginalAnimationCue || dispatch.OriginalCueContext.SourceLine != 7538 ||
                    dispatch.OriginalCue.FunctionSymbol != "get_hit_snd" || dispatch.OriginalCue.Argument0 != 70))
                throw new InvalidOperationException("C2 HitFunctionExtension retains actual7538/get_hit_snd70.");
            bool hop = dispatch.HasOriginalAnimationCue &&
                dispatch.OriginalCueContext.SourcePath == "plyrseq.c" &&
                dispatch.OriginalCueContext.ExecutingSymbol == "m_hopclp_anim";
            bool attack = c2 != null && c2.Kind == SourceC2SoundFunctionKind.AttackSoundFixed;
            if (hop || attack)
            {
                if (!hop || !attack || dispatch.OriginalCueContext.SourceLine != 3409 ||
                    !dispatch.OriginalCueContext.ScriptInstanceId.HasValue ||
                    dispatch.OriginalCueContext.OpcodeSymbol != "ANI_CODE" ||
                    dispatch.OriginalCue.Kind != SourcePresentationCueKind.SoundCallback ||
                    dispatch.OriginalCue.FunctionSymbol != "attack_snd" || dispatch.OriginalCue.Argument0 != 127 ||
                    dispatch.OriginalCue.Argument1 != 0 || dispatch.OriginalCue.Argument2 != 0 ||
                    dispatch.Status == SourceSoundVmStatus.Unsupported)
                    throw new InvalidOperationException("Original hop requires its exact3409/attack_snd127,0,0 resolved typed cue, never Unsupported/legacy-ground fallback.");
                if (dispatch.Status == SourceSoundVmStatus.Resolved)
                {
                    if (dispatch.Result == null || dispatch.Result.RandomCalls != 0 ||
                        dispatch.Result.Calls.Count != 1 || !dispatch.C2StateAtCall.HasValue)
                        throw new InvalidOperationException("Fixed attack sound requires exactly one actual call and zero RNG.");
                    Blitz.Presentation.SourceAudioResolvedOriginalSoundCall call = dispatch.Result.Calls[0];
                    SourceC2SoundState state = dispatch.C2StateAtCall.Value;
                    if (call == null || !state.ActorStation.HasValue || state.ActorStation.Value < -1 ||
                        state.ActorStation.Value > 3 || string.IsNullOrEmpty(state.CurrentPlayerBank) ||
                        call.Bank != state.CurrentPlayerBank || call.PlaylistId != 127 || call.Volume != 160 ||
                        call.Pan != 127 || call.PriorityWord != (state.ActorStation.Value != -1 ? 10u : 15u) ||
                        call.RandomCalls != 0 || call.CalleeSourceSite == null ||
                        call.CalleeSourceSite.SourcePath != "sounds.c" ||
                        call.CalleeSourceSite.SourceGitBlob != "f5ac3fd2cec5c20998bb613066a355e0d0d4d0e9" ||
                        call.CalleeSourceSite.FunctionSymbol != "attack_snd" || call.CalleeSourceSite.SourceLine != 1638)
                        throw new InvalidOperationException("Fixed attack output must retain actual bank/station and original1638/127/160/127/human10-or-drone15 selection.");
                }
            }
            if (dispatch.Status == SourceSoundVmStatus.Unsupported)
            {
                if (dispatch.Result != null)
                    throw new InvalidOperationException("Unsupported sound is not a resolved or silent output.");
                return;
            }
            if (dispatch.Status != SourceSoundVmStatus.Resolved && dispatch.Status != SourceSoundVmStatus.MissingInput ||
                dispatch.Result == null)
                throw new InvalidOperationException("Completed or partial source sound records must retain the actual result.");
            if (attack && (dispatch.Result.RandomCalls != 0 ||
                dispatch.Status == SourceSoundVmStatus.MissingInput && dispatch.Result.Calls.Count != 0))
                throw new InvalidOperationException("Fixed attack partial failure retains zero draws and no calls before missing station/bank input.");
            for (int i = 0; i < dispatch.Result.Calls.Count; i++)
            {
                Blitz.Presentation.SourceAudioResolvedOriginalSoundCall call = dispatch.Result.Calls[i];
                bool identity = dispatch.HasOriginalAnimationCue ?
                    call.HasOriginalAnimationCue && ReferenceEquals(call.OriginalCue, dispatch.OriginalCue) :
                    !call.HasOriginalAnimationCue && ReferenceEquals(call.C2Context, RequireNativeContext(dispatch));
                if (!identity || call.SubcallOrder != i || c2 != null &&
                    (c2.Kind == SourceC2SoundFunctionKind.HitFunctionExtension ? call.C2Context != null :
                        !ReferenceEquals(call.C2Context, c2)))
                    throw new InvalidOperationException("Raw original sound subcalls must retain exact cue identity and order.");
            }
        }

        private static SourceC2SoundContext RequireNativeContext(SourceSoundVmDispatch dispatch)
        {
            SourceC2SoundContext c2 = dispatch.C2Context;
            if (c2 == null || c2.HasOriginalAnimationCue ||
                (c2.Kind == SourceC2SoundFunctionKind.WhistleProc ? c2.ActorId != -1 :
                    c2.Kind != SourceC2SoundFunctionKind.WhistleImmediate || c2.ActorId < 0 || c2.ActorId > 13))
                throw new InvalidOperationException("Native process sound needs its actual typed context; actorless whistle is -1, never actor0 or a fake animation cue.");
            return c2;
        }
    }
}
