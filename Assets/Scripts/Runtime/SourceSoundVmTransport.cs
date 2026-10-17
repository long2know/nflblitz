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
        private ISourceSoundVmOutputConsumer output;
        private string failure;
        private long lastTick = -1;
        private int lastOrder = -1;

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
                if (dispatch == null || dispatch.OriginalCue == null)
                    throw new InvalidOperationException("A source sound dispatch must retain its actual original cue.");
                if (dispatch.OriginalCue.Kind == SourcePresentationCueKind.PlayerDownCallback)
                    throw new InvalidOperationException("PlayerDown is an already-executed ordering token, never a sound dispatch.");
                SourcePresentationContext context = dispatch.OriginalCueContext;
                if (context.SourceTick < lastTick || context.SourceTick == lastTick && context.ProcessOrder <= lastOrder)
                    throw new InvalidOperationException("Source sound dispatches must retain executing tick/instruction FIFO order.");
                if (!received.Add(dispatch.OriginalCue))
                    throw new InvalidOperationException("A source sound dispatch cannot be drained twice.");
                ValidateResult(dispatch);
                pending.Enqueue(dispatch);
                lastTick = context.SourceTick; lastOrder = context.ProcessOrder;
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
            if (paired.ContainsKey(cue))
                throw new InvalidOperationException("An original sound callback cannot be consumed twice.");
            if (pending.Count == 0) return null;
            if (!ReferenceEquals(pending.Peek().OriginalCue, cue))
            {
                foreach (SourceSoundVmDispatch later in pending)
                    if (ReferenceEquals(later.OriginalCue, cue))
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
            if (dispatch.Status != SourceSoundVmStatus.Resolved || dispatch.Result == null ||
                !paired.TryGetValue(dispatch.OriginalCue, out actual) || !ReferenceEquals(actual, dispatch) ||
                !delivered.Add(dispatch.OriginalCue))
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
            awaitingOutput.Remove(dispatch.OriginalCue);
            failure = null; FailedDispatch = null;
        }

        public void CompletePresentationBatch()
        {
            RequireHealthy();
            if (pending.Count != 0)
                throw new InvalidOperationException("Drained source sound records have no matching original presentation callbacks.");
            if (awaitingOutput.Count != 0)
                throw new InvalidOperationException("A nonempty resolved sound was paired but never delivered to a real output consumer.");
        }

        public void Reset()
        {
            pending.Clear(); received.Clear(); paired.Clear(); delivered.Clear(); awaitingOutput.Clear();
            failure = null; lastTick = -1; lastOrder = -1; LastBatch = null; FailedDispatch = null;
        }

        private void RequireHealthy()
        {
            if (failure != null) throw new InvalidOperationException(failure);
        }

        private static void ValidateResult(SourceSoundVmDispatch dispatch)
        {
            if (dispatch.Status == SourceSoundVmStatus.Unsupported)
            {
                if (dispatch.Result != null)
                    throw new InvalidOperationException("Unsupported sound is not a resolved or silent output.");
                return;
            }
            if (dispatch.Status != SourceSoundVmStatus.Resolved && dispatch.Status != SourceSoundVmStatus.MissingInput ||
                dispatch.Result == null)
                throw new InvalidOperationException("Completed or partial source sound records must retain the actual result.");
            for (int i = 0; i < dispatch.Result.Calls.Count; i++)
            {
                if (!ReferenceEquals(dispatch.Result.Calls[i].OriginalCue, dispatch.OriginalCue) ||
                    dispatch.Result.Calls[i].SubcallOrder != i)
                    throw new InvalidOperationException("Raw original sound subcalls must retain exact cue identity and order.");
            }
        }
    }
}
