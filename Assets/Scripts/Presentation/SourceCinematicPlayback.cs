using System;
using System.Collections.Generic;
using System.Globalization;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.Presentation
{
    public sealed class SourceCinematicProcessClock
    {
        public int WasPlayOver { get; private set; } = 1;
        public bool BeginsNewPlay(int actualGameMode)
        {
            RequireMode(actualGameMode);
            return WasPlayOver != 0 && (actualGameMode == 0 || actualGameMode == 1);
        }
        public void CompleteCameraTick(int actualGameMode)
        {
            RequireMode(actualGameMode);
            if (actualGameMode == 3 || actualGameMode == 4) WasPlayOver = 2;
            else if (WasPlayOver != 0) WasPlayOver--;
        }
        public bool BeginsSourcePhase(GamePhase actualSourcePhase)
        {
            if (actualSourcePhase == GamePhase.PlaySelect)
                throw new InvalidOperationException("Original source menu suspends CAMBOT; no camera wake is executed.");
            return WasPlayOver != 0 && actualSourcePhase == GamePhase.PreSnap;
        }
        public void CompleteSourcePhase(GamePhase actualSourcePhase)
        {
            if (actualSourcePhase == GamePhase.PlaySelect)
                throw new InvalidOperationException("Suspended source menu ticks do not complete a camera wake.");
            if (actualSourcePhase == GamePhase.DeadBall || actualSourcePhase == GamePhase.GameOver) WasPlayOver = 2;
            else if (WasPlayOver != 0) WasPlayOver--;
        }
        public void Reset() { WasPlayOver = 1; }
        private static void RequireMode(int value)
        { if (value < 0 || value > 6) throw new ArgumentOutOfRangeException(nameof(value)); }
    }

    public sealed class SourceImpactCallback
    {
        public long SourceTick { get; }
        public int ProcessOrder { get; }
        public int ActorId { get; }
        public SourceCinematicCueDefinition Definition { get; }
        public SourceCallbackCue ActualSourceCue { get; }
        internal SourceImpactCallback(long tick, int order, int actor, SourceCinematicCueDefinition definition)
        { SourceTick = tick; ProcessOrder = order; ActorId = actor; Definition = definition; }
        internal SourceImpactCallback(SourceCallbackCue cue, SourceCinematicCueDefinition definition)
            : this(cue.Context.SourceTick, cue.Context.ProcessOrder, cue.Context.ActorPlayerId, definition)
        { ActualSourceCue = cue; }
    }

    public sealed class SourceCinematicPlayback
    {
        private readonly SourceCinematicLibrary library;
        private readonly SourceCinematicModel camera;
        private readonly SourceImpactShaker shaker;
        private long lastEventTick = -1, lastCameraTick = -1;
        private int lastOrder = -1, targetActor = -1;
        private bool resumingNormal;
        private bool cameraProcessSuspended, allowResumedTickGap;
        public SourceCinematicFrame Frame { get; private set; }
        public bool HasRenderFrame { get; private set; }
        public bool IsTransientActive { get { return camera.IsActive; } }
        public bool IsResumingNormal { get { return resumingNormal; } }
        public bool NeedsShakeDraws { get { return shaker.NeedsSourceRandomDraws; } }
        public int TargetActorId { get { return targetActor; } }
        public long LastCameraTick { get { return lastCameraTick; } }
        public SourcePresentationContext LastObservedPlayerDown { get; private set; }
        public SourceCinematicTransform CurrentSourceCamera { get; private set; }
        private readonly SourceCameraFrameClock executingSourceCamera = new SourceCameraFrameClock();

        public SourceCinematicTransform RequireSourceCameraAtTick(long executingSourceTick)
        {
            if (lastCameraTick != executingSourceTick)
                throw new InvalidOperationException("The source camera process has not executed the requested movement tick.");
            return executingSourceCamera.RequireAtTick(executingSourceTick, cameraProcessSuspended);
        }
        public int SourceHeading1024 { get { return (int)(camera.State.Theta * (1024f / SourceCinematicMath.TwoPi)) & 1023; } }
        public event Action<SourceImpactCallback> SourceCallback;

        public void ObserveCameraProcessSuspension(bool actualSuspended)
        {
            if (cameraProcessSuspended && !actualSuspended) allowResumedTickGap = true;
            cameraProcessSuspended = actualSuspended;
        }

        public void Execute(SourcePresentationCueBatch batch)
        {
            if (batch == null) throw new InvalidOperationException("Source presentation cue availability is unknown; an empty known batch is required.");
            foreach (SourcePresentationCue cue in batch.Cues)
            {
                SourcePresentationContext context = cue.Context;
                SourceCameraCue cameraCue = cue as SourceCameraCue;
                if (cameraCue != null)
                {
                    SourceCinematicCueDefinition definition;
                    if (cue.Kind == SourcePresentationCueKind.CameraOpcode)
                    {
                        RequirePath(context, "plyrseq.c");
                        definition = library.RequireExecutedOpcode(context.ExecutingSymbol, context.SourceLine, "camera");
                    }
                    else
                    {
                        RequirePath(context, "detect.c");
                        definition = library.RequireContactSelection(context.ExecutingSymbol, context.SourceLine);
                    }
                    if (definition.Arguments[0] != cameraCue.Selection.TableSymbol ||
                        library.Tables[definition.Arguments[0]].Count != cameraCue.Selection.EntryCount)
                        throw new InvalidOperationException("Actual executed CSI table/count differs from original source location.");
                    if (cue.Kind == SourcePresentationCueKind.CameraOpcode)
                        ExecuteCameraOpcode(context.SourceTick, context.ProcessOrder, context.ActorPlayerId,
                            context.ExecutingSymbol, context.SourceLine, cameraCue.Selection.SelectedOrdinal);
                    else ExecuteContactSelection(context.SourceTick, context.ProcessOrder, context.ActorPlayerId,
                        context.ExecutingSymbol, context.SourceLine, cameraCue.Selection.SelectedOrdinal);
                    continue;
                }
                SourceShakerCue shakerCue = cue as SourceShakerCue;
                if (shakerCue != null)
                {
                    RequirePath(context, "plyrseq.c");
                    SourceCinematicCueDefinition definition = library.RequireExecutedOpcode(context.ExecutingSymbol, context.SourceLine, "shaker");
                    if (int.Parse(definition.Arguments[0], CultureInfo.InvariantCulture) != shakerCue.Amplitude ||
                        int.Parse(definition.Arguments[1], CultureInfo.InvariantCulture) != shakerCue.TotalTicks ||
                        !cue.CarrierPlayerId.HasValue)
                        throw new InvalidOperationException("Actual source shaker operands/carrier-at-opcode disagree with source.");
                    ExecuteShakerOpcode(context.SourceTick, context.ProcessOrder, context.ActorPlayerId, cue.CarrierPlayerId.Value,
                        context.ExecutingSymbol, context.SourceLine);
                    continue;
                }
                SourceCallbackCue callback = cue as SourceCallbackCue;
                if (callback == null) throw new InvalidOperationException("Unknown original presentation instruction type.");
                RequirePath(context, "plyrseq.c");
                if (callback.Kind == SourcePresentationCueKind.PlayerDownCallback)
                {
                    if (callback.FunctionSymbol != "player_down")
                        throw new InvalidOperationException("PlayerDownCallback must retain its actual original function identity.");
                    Ordered(context.SourceTick, context.ProcessOrder, context.ActorPlayerId);
                    LastObservedPlayerDown = context;
                    continue;
                }
                SourceCinematicCueDefinition original = RequireOriginalCallback(callback);
                Ordered(context.SourceTick, context.ProcessOrder, context.ActorPlayerId);
                Action<SourceImpactCallback> listener = SourceCallback;
                if (listener == null) throw new InvalidOperationException("No original effect/audio callback consumer is bound for " +
                    callback.FunctionSymbol + "; no generic substitute is created.");
                listener(new SourceImpactCallback(callback, original));
            }
        }

        public void ConsumeResolvedSourceSound(SourceSoundVmDispatch dispatch,
            Action<SourceSoundVmDispatch> actualRawOutputConsumer = null)
        {
            if (dispatch == null || !dispatch.HasOriginalAnimationCue)
                throw new InvalidOperationException("An actual detached source sound VM dispatch and its original cue are required.");
            SourceCallbackCue cue = dispatch.OriginalCue;
            if (cue.Kind != SourcePresentationCueKind.SoundCallback)
                throw new InvalidOperationException("Only an already-resolved sound callback belongs here; PlayerDown/SPFX are not replayed.");
            RequireOriginalCallback(cue);
            if (dispatch.C2Context != null)
            {
                SourceC2SoundContext c2Context = dispatch.C2Context;
                if (!c2Context.HasOriginalAnimationCue || !ReferenceEquals(c2Context.OriginalAnimationCue, cue) ||
                    c2Context.Kind != SourceC2SoundFunctionKind.TauntSpeech &&
                    c2Context.Kind != SourceC2SoundFunctionKind.HitFunctionExtension)
                    throw new InvalidOperationException("Only actual C2 animation sound kinds use the cinematic cue path; native whistles have no fake ANI cue.");
                if (c2Context.Kind == SourceC2SoundFunctionKind.HitFunctionExtension &&
                    (cue.FunctionSymbol != "get_hit_snd" || cue.Context.SourceLine != 7538 || cue.Argument0 != 70))
                    throw new InvalidOperationException("The C2 hit extension retains actual7538/get_hit_snd70, not a ground/taunt fallback.");
            }
            SourcePresentationContext context = cue.Context;
            Ordered(context.SourceTick, context.ProcessOrder, context.ActorPlayerId);
            if (dispatch.Status != SourceSoundVmStatus.Resolved || dispatch.Result == null)
                throw new InvalidOperationException("Unsupported/missing original sound input is not resolved silence: " + dispatch.Detail);
            for (int i = 0; i < dispatch.Result.Calls.Count; i++)
            {
                SourceAudioResolvedOriginalSoundCall call = dispatch.Result.Calls[i];
                if (call == null || !call.HasOriginalAnimationCue || !ReferenceEquals(call.OriginalCue, cue) ||
                    call.SubcallOrder != i || dispatch.C2Context != null &&
                    (dispatch.C2Context.Kind == SourceC2SoundFunctionKind.TauntSpeech
                        ? !ReferenceEquals(call.C2Context, dispatch.C2Context)
                        : call.C2Context != null))
                    throw new InvalidOperationException("Already-resolved source sound output must retain the exact OriginalCue and ordered subcalls.");
            }
            if (dispatch.Result.Calls.Count == 0) return;
            if (actualRawOutputConsumer == null)
                throw new InvalidOperationException("Resolved original sound has raw output but no actual DSP3 consumer is bound; no silence/isolated preview substitute.");
            actualRawOutputConsumer(dispatch);
        }

        private SourceCinematicCueDefinition RequireOriginalCallback(SourceCallbackCue callback)
        {
            SourcePresentationContext context = callback.Context;
            RequirePath(context, "plyrseq.c");
            SourceCinematicCueDefinition original = library.RequireExecutedOpcode(context.ExecutingSymbol, context.SourceLine, "callback");
            if (original.Arguments[0] != callback.FunctionSymbol)
                throw new InvalidOperationException("Actual source effect/audio callback function differs from its executed source site.");
            int[] values = { callback.Argument0, callback.Argument1, callback.Argument2 };
            for (int i = 0; i < values.Length; i++)
            {
                int literal;
                if (int.TryParse(original.Arguments[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out literal) &&
                    literal != values[i])
                    throw new InvalidOperationException("Actual source callback integer/sentinel operand changed.");
            }
            return original;
        }

        public SourceCinematicPlayback(SourceCinematicLibrary library, SourceCinematicSeed initialCamera)
        {
            if (library == null) throw new ArgumentNullException(nameof(library));
            this.library = library;
            camera = new SourceCinematicModel(initialCamera);
            shaker = new SourceImpactShaker(library.ShakerExponent);
            CurrentSourceCamera = SourceCinematicTransform.FromState(initialCamera);
        }

        public void SetNormalState(SourceCinematicSeed actualCurrentCamera)
        {
            if (camera.IsActive || resumingNormal)
                throw new InvalidOperationException("An active source transient/resume cannot be replaced by a render-only normal camera seed.");
            camera.Reset(actualCurrentCamera);
            CurrentSourceCamera = SourceCinematicTransform.FromState(actualCurrentCamera);
            executingSourceCamera.Reset();
        }

        public bool ExecuteCameraOpcode(long sourceTick, int processOrder, int actorId,
            string executingScript, int sourceLine, int actualSelectedOrdinal)
        {
            RequireActor(actorId);
            Ordered(sourceTick, processOrder, actorId);
            SourceCinematicCueDefinition cue = library.RequireExecutedOpcode(executingScript, sourceLine, "camera");
            return Select(cue.Arguments[0], actualSelectedOrdinal, actorId);
        }

        public bool ExecuteContactSelection(long sourceTick, int processOrder, int actorId,
            string executingFunction, int sourceLine, int actualSelectedOrdinal)
        {
            RequireActor(actorId);
            Ordered(sourceTick, processOrder, actorId);
            SourceCinematicCueDefinition cue = library.RequireContactSelection(executingFunction, sourceLine);
            return Select(cue.Arguments[0], actualSelectedOrdinal, actorId);
        }

        public void ExecuteShakerOpcode(long sourceTick, int processOrder, int actorId, int actualCarrierAtOpcode,
            string executingScript, int sourceLine)
        {
            RequireActor(actorId);
            Ordered(sourceTick, processOrder, actorId);
            SourceCinematicCueDefinition cue = library.RequireExecutedOpcode(executingScript, sourceLine, "shaker");
            shaker.ExecuteOpcode(int.Parse(cue.Arguments[0], CultureInfo.InvariantCulture),
                int.Parse(cue.Arguments[1], CultureInfo.InvariantCulture), actorId, actualCarrierAtOpcode);
        }

        public void ExecuteCallback(long sourceTick, int processOrder, int actorId, string executingScript, int sourceLine)
        {
            RequireActor(actorId);
            Ordered(sourceTick, processOrder, actorId);
            SourceCinematicCueDefinition cue = library.RequireExecutedOpcode(executingScript, sourceLine, "callback");
            Action<SourceImpactCallback> listener = SourceCallback;
            if (listener == null)
                throw new InvalidOperationException("No original effect/audio callback consumer is bound for " +
                    cue.Arguments[0] + " at plyrseq.c:" + sourceLine + ". No generic particle/sound substitute is created.");
            listener(new SourceImpactCallback(sourceTick, processOrder, actorId, cue));
        }

        public SourceCinematicFrame CameraSourceTick(long sourceTick, int processOrder,
            IReadOnlyList<SourceCinematicTarget> actualActorTargets, SourceCinematicSeed actualNormalTarget,
            bool newPlayAfterPlayOver, int? actualShakerRandrng3X = null, int? actualShakerRandrng3Z = null,
            SourceCinematicSeed? actualNormalState = null)
        {
            if (lastCameraTick >= 0 && sourceTick <= lastCameraTick)
                throw new InvalidOperationException("Source camera must execute at most once per source tick, not once per rendered frame.");
            if (cameraProcessSuspended)
                throw new InvalidOperationException("Original CAMBOT process is explicitly suspended; menu source ticks do not execute camera.");
            if (lastCameraTick >= 0 && sourceTick != lastCameraTick + 1 &&
                (camera.IsActive || resumingNormal || shaker.NeedsSourceRandomDraws) && !allowResumedTickGap)
                throw new InvalidOperationException("Missing intermediate actual source camera/actor samples; no velocity extrapolation is substituted.");
            if (actualActorTargets == null || actualActorTargets.Count != 14)
                throw new ArgumentException("Supply all fourteen actual original actor targets in actor-ID order.", nameof(actualActorTargets));
            if (shaker.NeedsSourceRandomDraws != (actualShakerRandrng3X.HasValue && actualShakerRandrng3Z.HasValue) ||
                actualShakerRandrng3X.HasValue != actualShakerRandrng3Z.HasValue)
                throw new InvalidOperationException("Source camera shaker needs exactly its two actual ordered randrng(3) results while active, and none while inactive.");
            Ordered(sourceTick, processOrder, -1);
            lastCameraTick = sourceTick;
            allowResumedTickGap = false;
            bool wasActive = camera.IsActive;
            SourceCinematicFrame frame;
            if (wasActive)
            {
                frame = camera.Step(actualActorTargets[targetActor], newPlayAfterPlayOver);
                if (frame.ReturnedToNormal)
                {
                    bool snap = frame.SnapOnReturn || newPlayAfterPlayOver;
                    SourceCinematicFrame resumed = camera.ResumeNormal(actualNormalTarget, snap);
                    frame = new SourceCinematicFrame(resumed.State, false, true, snap, frame.ElapsedTicks,
                        library.Selected(frame.Table, frame.Ordinal));
                    resumingNormal = !snap;
                }
            }
            else if (resumingNormal)
            {
                frame = camera.ResumeNormal(actualNormalTarget, newPlayAfterPlayOver);
                if (newPlayAfterPlayOver) resumingNormal = false;
            }
            else
            {
                camera.Reset(actualNormalState ?? actualNormalTarget);
                frame = camera.Step(actualActorTargets[0]);
            }
            bool shake = shaker.NeedsSourceRandomDraws;
            if (shake)
            {
                camera.ApplyOriginalShakeOffset(shaker.Step(actualShakerRandrng3X.Value, actualShakerRandrng3Z.Value));
                frame = new SourceCinematicFrame(camera.State, frame.Active, frame.ReturnedToNormal,
                    frame.SnapOnReturn, frame.ElapsedTicks,
                    frame.Table == null ? null : library.Selected(frame.Table, frame.Ordinal));
            }
            Frame = frame;
            CurrentSourceCamera = SourceCinematicTransform.FromState(camera.State);
            executingSourceCamera.Publish(sourceTick, CurrentSourceCamera);
            HasRenderFrame = wasActive || resumingNormal || shake;
            return frame;
        }

        public void Reset(SourceCinematicSeed normalCamera)
        {
            camera.Reset(normalCamera); shaker.Reset();
            lastEventTick = lastCameraTick = -1; lastOrder = -1; targetActor = -1;
            resumingNormal = false; HasRenderFrame = false; Frame = default(SourceCinematicFrame);
            cameraProcessSuspended = allowResumedTickGap = false; LastObservedPlayerDown = default(SourcePresentationContext);
            CurrentSourceCamera = SourceCinematicTransform.FromState(normalCamera);
            executingSourceCamera.Reset();
        }

        private bool Select(string table, int actualOrdinal, int actor)
        {
            if (!camera.Select(library.Selected(table, actualOrdinal))) return false;
            targetActor = actor; resumingNormal = false; return true;
        }

        private static void RequireActor(int actor)
        {
            if (actor < 0 || actor >= 14) throw new ArgumentOutOfRangeException(nameof(actor));
        }

        private static void RequirePath(SourcePresentationContext context, string path)
        {
            if (context.SourcePath != path)
                throw new InvalidOperationException("Original source presentation location path must be " + path + ".");
        }

        private void Ordered(long tick, int order, int actor)
        {
            if (tick < 0 || order < 0 || actor < -1 || actor >= 14)
                throw new ArgumentOutOfRangeException(nameof(tick));
            if (tick < lastEventTick || tick == lastEventTick && order <= lastOrder)
                throw new InvalidOperationException("Original presentation events/camera must preserve source tick and within-tick process order; duplicates are not replayed.");
            lastEventTick = tick; lastOrder = order;
        }
    }
}
