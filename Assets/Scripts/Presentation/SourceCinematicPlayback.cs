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
        private SourceNormalCameraModel originalNormal;
        private readonly SourceNormalCameraData originalNormalData;
        private int rawSequence;
        private int fieldGoalMode;
        private SourceVector3 fieldGoalBase, fieldGoalTarget;
        private float fieldGoalBaseDistance;
        private sealed class PossessionContinuation
        {
            internal SourceCinematicEntry ReturningEntry;
            internal SourceCinematicFrame Frame;
            internal SourceNormalCameraPlan Plan;
            internal int Flags;
            internal bool Returned, SnapReturn, NormalDispatch;
            internal long SleepTick;
        }
        private PossessionContinuation possessionContinuation;
        private int possessionOffensePrevious, possessionTimeout;
        public bool IsPossessionSleeping { get { return possessionContinuation != null; } }
        public int PossessionOffensePrevious { get { return possessionOffensePrevious; } }
        public int PossessionTimeout { get { return possessionTimeout; } }
        public SourceNormalCameraModel OriginalNormal { get { return originalNormal; } }
        public bool UsesOriginalNormalCamera { get { return originalNormalData != null; } }
        public SourceCameraFeedback CameraFeedback { get; private set; }
        public SourceCameraBoundary LastCameraBoundary { get; private set; }
        public SourceCinematicSeed SourceState { get { return camera.State; } }
        public int RawMode { get { return camera.IsActive && camera.Entry != null ? camera.Entry.RawMode : 0; } }
        internal int SourceTargetKind { get { return targetActor >= 0 ? 1 : rawSequence == 1 ? 2 : rawSequence == 2 ? 3 : 0; } }

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

        public SourceCinematicPlayback(SourceCinematicLibrary library, SourceNormalCameraData normalData,
            SourceCameraBoundary initialBoundary)
            : this(library, SourceNormalCameraModel.Initial(initialBoundary))
        {
            originalNormalData = normalData ?? throw new ArgumentNullException(nameof(normalData));
            originalNormal = new SourceNormalCameraModel();
        }

        public void SetNormalState(SourceCinematicSeed actualCurrentCamera)
        {
            if (UsesOriginalNormalCamera)
                throw new InvalidOperationException("The raw source profile cannot accept a legacy/render normal seed.");
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
            if (UsesOriginalNormalCamera)
                throw new InvalidOperationException("The raw source profile requires its exact immutable Core boundary, never legacy normal/return seeds.");
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

        public SourceCinematicFrame CameraSourceTick(SourceCameraBoundary input, float horizontalResolution,
            float verticalResolution, Func<int, string, int> actualRandom,
            ISourceCameraProcessServices processServices = null)
        {
            if (!UsesOriginalNormalCamera || input == null || actualRandom == null)
                throw new InvalidOperationException("Bind original normal data and the exact typed pre-actor camera boundary/RNG.");
            if (cameraProcessSuspended || input.ProcessOrder < 0 || input.SourceTick <= lastCameraTick ||
                lastCameraTick >= 0 && input.SourceTick != lastCameraTick + 1 && !allowResumedTickGap &&
                possessionContinuation == null)
                throw new InvalidOperationException("Execute CAMBOT exactly once per source wake; only actual process suspension permits a gap.");
            SourceCameraFrameClock.RequireOriginalRasterAspect(horizontalResolution, verticalResolution);
            Ordered(input.SourceTick, input.ProcessOrder, -1);
            SourceCinematicEntry returningEntry = null;
            int flags = -1;
            bool returned = false, snapReturn = false, normalDispatch = !camera.IsActive;
            SourceCinematicFrame frame = default(SourceCinematicFrame);
            SourceNormalCameraPlan plan = default(SourceNormalCameraPlan);
            if (possessionContinuation != null)
            {
                PossessionContinuation saved = possessionContinuation;
                if (processServices == null || input.SourceTick < saved.SleepTick + 40)
                    throw new InvalidOperationException("Resume only the actual CAMBOT sleep(40) continuation.");
                returningEntry = saved.ReturningEntry; flags = saved.Flags; returned = saved.Returned;
                snapReturn = saved.SnapReturn; normalDispatch = saved.NormalDispatch; frame = saved.Frame; plan = saved.Plan;
                possessionContinuation = null;
                possessionTimeout = 6 * 57;
                possessionOffensePrevious = -1;
            }
            else
            {
                if (originalNormal.BeginWake(input, camera.IsActive))
                {
                    camera.RevertToNormal();
                    resumingNormal = false;
                }
                normalDispatch = !camera.IsActive;
                if (camera.IsActive)
                {
                    flags = camera.PrepareStep(RawTarget(input), false, out frame);
                    if (flags < 0)
                    {
                        returned = frame.ReturnedToNormal; snapReturn = frame.SnapOnReturn;
                        returningEntry = camera.Entry;
                        normalDispatch = true;
                    }
                }
                if (normalDispatch)
                {
                    plan = originalNormal.Prepare(camera.State, input, horizontalResolution, verticalResolution,
                        actualRandom, snapReturn, snapReturn);
                    camera.SetState(plan.State);
                }
                if (ApplyOriginalPossession(input, processServices))
                {
                    possessionContinuation = new PossessionContinuation { ReturningEntry = returningEntry,
                        Flags = flags, Returned = returned, SnapReturn = snapReturn, NormalDispatch = normalDispatch,
                        Frame = frame, Plan = plan, SleepTick = input.SourceTick };
                    CameraFeedback = new SourceCameraFeedback(input.SourceTick, originalNormal.TargetOffscreen,
                        cameraCompleted: false, cameraSleepTicks: 40);
                    return Frame;
                }
            }
            int? fieldGoalLastCarrier = ApplyOriginalFieldGoal(input, horizontalResolution, verticalResolution, actualRandom,
                ref normalDispatch, ref plan, ref flags, ref frame);
            int? openingAutoHike = null;
            if (originalNormal.WantsOpeningKickoff(input))
            {
                int selected = RequireDraw(actualRandom, 11, "cambot.c:3378/select-csi-kickoff");
                SourceKickoffCameraDefinition kickoff = originalNormalData.Kickoff[selected];
                SourceCinematicSeed s = camera.State;
                int sign = input.LastPossession != 0 ? 1 : -1;
                camera.Reset(SourceNormalCameraModel.Copy(s,
                    new SourceVector3(sign * kickoff.Eye.X, kickoff.Eye.Y, sign * kickoff.Eye.Z),
                    new SourceVector3(input.LastPossession != 0 ? -331f : 331f, 6f * (390f / 50f / 3f), 0f),
                    s.Distance, s.Theta, s.Phi, s.Velocity, s.DistanceVelocity, s.ThetaVelocity, s.PhiVelocity,
                    s.ThetaTarget, s.PhiTarget));
                camera.Select(kickoff.WithDistance(originalNormal.MinimumDistance(input, false)));
                targetActor = -1; rawSequence = 1;
                originalNormal.StartedOpeningKickoff(); openingAutoHike = kickoff.AutoHikeTicks;
                flags = camera.PrepareStep(RawTarget(input), false, out frame);
                normalDispatch = false;
            }
            if (normalDispatch)
            {
                originalNormal.CommitPlan(plan);
                camera.SetState(originalNormal.Integrate(plan));
                frame = new SourceCinematicFrame(camera.State, false, returned, snapReturn, camera.ElapsedTicks, returningEntry);
            }
            else
            {
                if (flags < 0) throw new InvalidOperationException("A newly selected original camera did not execute its first mode tick.");
                frame = camera.CompleteStep(flags);
            }
            if (shaker.NeedsSourceRandomDraws)
            {
                int x = RequireDraw(actualRandom, 3, "cambot.c:1009/shaker-X");
                int z = RequireDraw(actualRandom, 3, "cambot.c:1010/shaker-Z");
                camera.ApplyOriginalShakeOffset(shaker.Step(x, z));
                frame = new SourceCinematicFrame(camera.State, frame.Active, frame.ReturnedToNormal,
                    frame.SnapOnReturn, frame.ElapsedTicks, normalDispatch ? returningEntry : camera.Entry);
            }
            Frame = frame; lastCameraTick = input.SourceTick; allowResumedTickGap = false;
            resumingNormal = returned && !snapReturn;
            HasRenderFrame = true;
            CurrentSourceCamera = SourceCinematicTransform.FromState(camera.State);
            executingSourceCamera.Publish(input.SourceTick, CurrentSourceCamera);
            CameraFeedback = new SourceCameraFeedback(input.SourceTick, originalNormal.TargetOffscreen, openingAutoHike,
                fieldGoalLastCarrier);
            LastCameraBoundary = input;
            originalNormal.CompleteWake(input);
            return frame;
        }

        private bool ApplyOriginalPossession(SourceCameraBoundary input, ISourceCameraProcessServices services)
        {
            // cambot.c:2886-2970. This is inside cambot_special, AFTER mode
            // preparation/unrotation and BEFORE any integrator or field-goal work.
            if (input.RawGameMode != 2 && input.RawGameMode != 3)
            { possessionOffensePrevious = input.LastPossession; return false; }
            int message = -1;
            if (possessionOffensePrevious != input.LastPossession)
            {
                if (possessionOffensePrevious < 0)
                {
                    if (services == null) throw new InvalidOperationException("The active possession wait requires real process services.");
                    if (!services.Exists(0x0f15, -1) || --possessionTimeout == 0)
                    {
                        services.ResumeAll();
                        possessionOffensePrevious = input.RawGameMode == 2 ? input.LastPossession : input.OffenseSide;
                    }
                }
                else if (input.RawGameMode == 2)
                {
                    if ((input.Ball.RawFlags & 0x20) == 0 &&
                        (input.Ball.RawType == 0 || input.Ball.RawType == 5 || input.Ball.RawType == 6))
                        message = 8;
                    else if (input.Ball.RawType == 1 || input.Ball.RawType == 6) message = 13;
                    else if ((input.RawGameFlags & (0x10 | 0x100 | 0x800)) == 0x100 && input.RawGameState != 0)
                        message = 15;
                    else possessionOffensePrevious = input.LastPossession;
                }
            }
            else if (input.RawGameMode == 3 && possessionOffensePrevious != input.OffenseSide)
            {
                if ((input.Ball.RawFlags & 0x20) == 0 && input.RawPlayResult != 10 && !input.OffenseTurnover &&
                    (input.Ball.RawType == 0 || input.Ball.RawType == 5 || input.Ball.RawType == 6))
                    message = 8;
                else possessionOffensePrevious = input.OffenseSide;
            }
            if (message < 0) return false;
            if (services == null)
                throw new InvalidOperationException("The active original possession message cannot run without actual shared process services.");
            services.SuspendMultiple(0, 0x8000);
            services.ResumeMultiple(0x0f80, unchecked((int)0xfffffffe));
            services.ResumeMultiple(0x0502, -1);
            services.CreateMessage(message);
            services.SleepCamera(40);
            return true;
        }

        private SourceCinematicTarget RawTarget(SourceCameraBoundary input)
        {
            if (targetActor >= 0)
                return SourceCinematicTarget.FromRawActor(input.Actors[targetActor]);
            if (rawSequence == 1)
            {
                if (camera.Entry != null && camera.Entry.RawMode == 19 && input.Carrier >= 0)
                    return SourceCinematicTarget.FromRawActor(input.Actors[input.Carrier]);
                return new SourceCinematicTarget(input.Ball.Position, 0);
            }
            if (rawSequence == 2) return new SourceCinematicTarget(fieldGoalTarget, 0);
            throw new InvalidOperationException("Original transient has no actual source target identity.");
        }

        private static int RequireDraw(Func<int, string, int> random, int bound, string site)
        {
            int value = random(bound, site);
            if (value < 0 || value >= bound) throw new InvalidOperationException("Source camera randrng result is outside its actual request.");
            return value;
        }

        public void Reset(SourceCameraBoundary initialBoundary)
        {
            if (!UsesOriginalNormalCamera) throw new InvalidOperationException("This camera has no original normal producer.");
            Reset(SourceNormalCameraModel.Initial(initialBoundary));
            originalNormal = new SourceNormalCameraModel(); rawSequence = 0;
            fieldGoalMode = 0; fieldGoalBase = fieldGoalTarget = default(SourceVector3); fieldGoalBaseDistance = 0f;
            LastCameraBoundary = null; CameraFeedback = default(SourceCameraFeedback);
        }

        private int? ApplyOriginalFieldGoal(SourceCameraBoundary input, float horizontal, float vertical,
            Func<int, string, int> random, ref bool normalDispatch, ref SourceNormalCameraPlan plan,
            ref int flags, ref SourceCinematicFrame frame)
        {
            int? lastCarrier = null;
            bool selected = (input.OffensePlayFlags & (4 | 16)) != 0, fieldGoal = (input.OffensePlayFlags & 4) != 0;
            if (selected)
            {
                if (fieldGoalMode == 0)
                {
                    if (input.RawGameMode == 1 || fieldGoal && (input.RawGameFlags & 0x200) == 0)
                    {
                        if (input.RawGameMode == 1)
                        {
                            float x = (390f / 300f * 6f) *
                                (input.OffenseSide != 0 ? 100 - input.LineOfScrimmage : input.LineOfScrimmage) - 390f;
                            fieldGoalBase = new SourceVector3(x + (input.LastPossession != 0 ? 1f : -1f) *
                                (2.5f * (390f / 50f)), 0f, plan.Target.Z);
                            fieldGoalBaseDistance = plan.DistanceTarget;
                        }
                        if (normalDispatch) plan = plan.WithTarget(fieldGoalBase, fieldGoalBaseDistance);
                        else camera.OverrideDesired(fieldGoalBase);
                    }
                    else if (fieldGoal)
                    {
                        float travel = Math.Abs((input.LastPossession != 0 ? -460f : 460f) - fieldGoalBase.X);
                        if (input.Ball.Velocity.X == 0f)
                            throw new InvalidOperationException("Original field-goal camera travel time has an undefined zero-X-velocity domain.");
                        int time = (int)(travel / Math.Abs(input.Ball.Velocity.X) + .5f);
                        if (time <= 0) throw new InvalidOperationException("Original field-goal spline needs a positive source travel count.");
                        SourceCinematicEntry definition = originalNormalData.FieldGoal[
                            RequireDraw(random, 2, "cambot.c:3001/select-csi-fg-track")];
                        SourceCinematicSeed s = camera.State;
                        camera.Reset(SourceNormalCameraModel.Copy(s, s.Eye,
                            new SourceVector3(input.LastPossession != 0 ? -460f : 460f, (103.5f + 27f) * .5f, 0f),
                            s.Distance, s.Theta, s.Phi, s.Velocity, s.DistanceVelocity, s.ThetaVelocity, s.PhiVelocity,
                            s.ThetaTarget, s.PhiTarget));
                        camera.Select(new SourceCinematicEntry(definition.Table, definition.Ordinal, definition.SourceLine,
                            definition.RawMode, definition.ModeSymbol, definition.ModeTicks, time, definition.ThetaDegrees,
                            definition.PhiDegrees, definition.Distance, definition.ControlPoints), -(103.5f + 27f) * .5f);
                        rawSequence = 2; targetActor = -1; fieldGoalMode = 1; normalDispatch = false;
                    }
                }
                if (fieldGoalMode == 1)
                {
                    if ((input.Ball.RawFlags & 0x200) != 0)
                    {
                        bool snap = camera.Entry != null && camera.Entry.TravelTicks < 2;
                        // cambot_special re-enters ModeRevert AFTER cambot_proc's unrotation block.
                        plan = originalNormal.Prepare(camera.State, input, horizontal, vertical, random, snap, snap, false);
                        camera.RevertToNormal(); camera.SetState(plan.State);
                        normalDispatch = true; fieldGoalMode = -1;
                    }
                    else
                    {
                        if ((input.Ball.RawFlags & 0x1000) == 0)
                            fieldGoalTarget = new SourceVector3(input.Ball.Position.X - input.Ball.Origin.X + fieldGoalBase.X,
                                input.Ball.Position.Y / .4f * .7f,
                                input.Ball.Position.Z - input.Ball.Origin.Z + fieldGoalBase.Z);
                        if (camera.Entry != null && camera.ElapsedTicks - camera.Entry.TravelTicks > 30)
                        {
                            SelectRawTable("csi_zm_endplayfg", random, "cambot.c:3031/select-csi-fg-endpan");
                            SourceCinematicSeed s = camera.State;
                            float y = s.Focus.Y - s.Eye.Y;
                            float x = s.Focus.X - s.Eye.X, z = s.Focus.Z - s.Eye.Z;
                            float horizontal2 = x * x + z * z, d = (float)Math.Sqrt(y * y + horizontal2);
                            float sine = Math.Max(-1f, Math.Min(1f, y / d));
                            float phi = (float)Math.Asin(sine);
                            SourceCinematicSeed changed = new SourceCinematicSeed(s.Eye, s.Focus, d, s.Theta, phi, s.Velocity,
                                s.DistanceVelocity, s.ThetaVelocity, s.PhiVelocity, s.ThetaTarget, s.PhiTarget,
                                new SourceVector3(s.SinTheta, s.CosTheta, sine), (float)Math.Sqrt(horizontal2) / d,
                                s.Acceleration, s.DistanceAcceleration, s.ThetaAcceleration, s.PhiAcceleration);
                            SourceCinematicEntry selectedEntry = camera.Entry;
                            camera.Reset(changed); camera.Select(selectedEntry);
                            targetActor = input.Carrier >= 0 ? input.Carrier : input.Ball.Handler;
                            RequireActor(targetActor);
                            if (input.Carrier < 0) lastCarrier = targetActor;
                            rawSequence = 0; fieldGoalMode = 2; normalDispatch = false;
                        }
                    }
                }
                if (fieldGoalMode == 2 && camera.Entry != null && camera.ElapsedTicks >= camera.Entry.TravelTicks)
                {
                    if (!SelectRawTable("csi_fs_endplay", random, "cambot.c:3061/select-csi-fg-endplay"))
                        fieldGoalMode = -1;
                    else { fieldGoalMode++; normalDispatch = false; }
                }
                if (fieldGoalMode > 0 && camera.ElapsedTicks == 0)
                {
                    flags = camera.PrepareStep(RawTarget(input), false, out frame);
                    normalDispatch = false;
                }
            }
            if (originalNormal.WasPlayOver == 1) fieldGoalMode = 0;
            return lastCarrier;
        }

        private bool SelectRawTable(string table, Func<int, string, int> random, string site)
        {
            var entries = library.Tables[table];
            return camera.Select(entries[RequireDraw(random, entries.Count, site)]);
        }

        public void Reset(SourceCinematicSeed normalCamera)
        {
            camera.Reset(normalCamera); shaker.Reset();
            lastEventTick = lastCameraTick = -1; lastOrder = -1; targetActor = -1;
            resumingNormal = false; HasRenderFrame = false; Frame = default(SourceCinematicFrame);
            cameraProcessSuspended = allowResumedTickGap = false; LastObservedPlayerDown = default(SourcePresentationContext);
            CurrentSourceCamera = SourceCinematicTransform.FromState(normalCamera);
            executingSourceCamera.Reset();
            possessionContinuation = null; possessionOffensePrevious = possessionTimeout = 0;
        }

        private bool Select(string table, int actualOrdinal, int actor)
        {
            if (!camera.Select(library.Selected(table, actualOrdinal))) return false;
            targetActor = actor; rawSequence = 0; resumingNormal = false; return true;
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
