using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Presentation;

namespace Blitz.Gameplay
{
    public enum SourceSoundVmStatus { Resolved, Unsupported, MissingInput }

    public sealed class SourceSoundVmDispatch
    {
        private readonly SourceCallbackCue originalCue;
        private readonly SourceAudioFunctionState? stateAtCall;
        public bool HasOriginalAnimationCue { get { return originalCue != null; } }
        public SourceCallbackCue OriginalCue
        {
            get
            {
                if (originalCue == null) throw new InvalidOperationException("Native process sound has no original animation cue.");
                return originalCue;
            }
        }
        public SourcePresentationContext OriginalCueContext { get { return OriginalCue.Context; } }
        public SourceC2SoundContext C2Context { get; }
        public SourceC2SoundState? C2StateAtCall { get; }
        public SourceRandomContext RandomContext { get; }
        public SourceAudioFunctionState StateAtCall
        {
            get
            {
                if (!stateAtCall.HasValue) throw new InvalidOperationException("Native process sound has no actor function state.");
                return stateAtCall.Value;
            }
        }
        public SourceSoundVmStatus Status { get; }
        public SourceAudioFunctionResult Result { get; }
        public string Detail { get; }

        internal SourceSoundVmDispatch(SourceCallbackCue cue, SourceRandomContext context,
            SourceAudioFunctionState state, SourceSoundVmStatus status, SourceAudioFunctionResult result, string detail)
        {
            originalCue = cue; RandomContext = context; stateAtCall = state;
            Status = status; Result = result; Detail = detail;
        }

        internal SourceSoundVmDispatch(SourceC2SoundContext c2Context, SourceRandomContext context,
            SourceC2SoundState state, SourceAudioFunctionState? actorState, SourceSoundVmStatus status,
            SourceAudioFunctionResult result, string detail)
        {
            C2Context = c2Context; C2StateAtCall = state; RandomContext = context; stateAtCall = actorState;
            originalCue = c2Context.HasOriginalAnimationCue ? c2Context.OriginalAnimationCue : null;
            Status = status; Result = result; Detail = detail;
        }
    }

    public sealed class SourceSoundVmBatch
    {
        public ReadOnlyCollection<SourceSoundVmDispatch> Dispatches { get; }
        internal SourceSoundVmBatch(IReadOnlyList<SourceSoundVmDispatch> dispatches)
        {
            var copy = new SourceSoundVmDispatch[dispatches.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = dispatches[i];
            Dispatches = Array.AsReadOnly(copy);
        }
    }

    public sealed partial class GameSimulation
    {
        public const string SourceSoundVmDataSha256 =
            "4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280";

        private SourceAudioFunctionData sourceSoundVmData;
        private readonly List<SourceSoundVmDispatch> sourceSoundVmPending = new List<SourceSoundVmDispatch>();
        private bool sourceSoundVmFailed;

        public void BindSourceSoundVmData(SourceAudioFunctionData data)
        {
            if (Profile != SimulationProfile.OriginalRelease57Hz)
                throw new InvalidOperationException("Original sound-function data belongs to the explicit source profile.");
            if (Model.SourceTick != 0)
                throw new InvalidOperationException("Bind immutable source sound-function data before the first source wake.");
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.PayloadSha256 != SourceSoundVmDataSha256)
                throw new ArgumentException("The source VM requires the exact frozen original sound-function payload.", nameof(data));
            if (sourceSoundVmData != null && !ReferenceEquals(sourceSoundVmData, data))
                throw new InvalidOperationException("Original sound-function data is already bound.");
            sourceSoundVmData = data;
        }

        public SourceSoundVmBatch TakeSourceSoundVmDispatches()
        {
            var batch = new SourceSoundVmBatch(sourceSoundVmPending);
            sourceSoundVmPending.Clear();
            return batch;
        }

        private void InitializeSourceSoundState()
        {
            // Game.c:83 (a005a9b4...), and nfl.c:677's new-game player_blocks memset.
            Model.SourceCurrentPlayerBank = "players";
            foreach (ActorState actor in Model.Players) actor.SourceHitPartnerId = -1;
        }

        private void RequireSourceSoundVmBinding()
        {
            if (sourceSoundVmFailed)
                throw new InvalidOperationException("A source sound function failed after actual execution; restart is required, never retry or rewind it.");
            if (sourceSoundVmData == null)
                throw new InvalidOperationException("Bind the exact original SourceAudioFunctionData before the first source wake; no muted or post-host fallback exists.");
        }

        private SourceRandomContext SourceAnimationRandomContext(ActorState actor)
        {
            if (Model.SourceRandomActor != actor.Id)
            {
                int offset = Model.SourceRandomCallIndex;
                string process = Model.SourceRandomProcess;
                BeginSourceProcess(process, actor.Id, Model.SourceRandomProcessOrder);
                Model.SourceRandomCallIndex = offset;
            }
            return CurrentSourceRandomContext();
        }

        private SourceAudioFunctionState SourceSoundState(ActorState actor)
        {
            SourceAudioHitPartner hit = default(SourceAudioHitPartner);
            if (actor.SourceHitPartnerId.HasValue)
            {
                int id = actor.SourceHitPartnerId.Value;
                if (id == -1) hit = SourceAudioHitPartner.Absent;
                else if (id >= 0 && id < Model.Players.Length)
                {
                    ActorState partner = Model.Players[id];
                    hit = SourceAudioHitPartner.Present((int)partner.SourceMode, partner.SourceControllerStation);
                }
                else throw new InvalidOperationException("Invalid actual source pobj_hit actor identity.");
            }
            return new SourceAudioFunctionState(GameSnapshot.SourceActorFlags(actor), (int)actor.SourceMode,
                actor.Id, actor.SourceControllerStation, hit, Model.CarrierId, Model.TargetPlayerId,
                Model.SourceCurrentPlayerBank);
        }

        private void ExecuteSourceSoundVm(ActorState actor, SourceCallbackCue cue)
        {
            RequireSourceSoundVmBinding();
            bool preSnapSwitchSound = SourceDefensivePreSnapSwitchEnabled
                && cue.Context.SourcePath == "plyrseq.c" && cue.Context.ExecutingSymbol == "m_hopclp_anim";
            if (preSnapSwitchSound && (cue.Kind != SourcePresentationCueKind.SoundCallback
                || cue.Context.SourceLine != 3409 || cue.FunctionSymbol != "attack_snd"
                || cue.Argument0 != 127 || cue.Argument1 != 0 || cue.Argument2 != 0))
                throw new NotSupportedException("The enabled original hop supports only its exact attack_snd(127,0,0) cue.");
            if (SourceC2Enabled)
            {
                foreach (SourceC2VmBinding candidate in sourceC2Bootstrap.Data.Bindings)
                {
                    if (!candidate.Matches(cue)) continue;
                    if (preSnapSwitchSound && candidate.FunctionKind != SourceC2SoundFunctionKind.AttackSoundFixed)
                        throw new NotSupportedException("The enabled original hop requires its exact fixed attack-sound binding.");
                    // Immediate hop entry belongs to the caller's existing ordered process, not a new destination wake.
                    SourceRandomContext execution = preSnapSwitchSound
                        ? new SourceRandomContext(Model.SourceTick, actor.Id, Model.SourceRandomProcess,
                            Model.SourceRandomProcessOrder, sourceRandom.Provenance.Profile, Model.SourceRandomCallIndex)
                        : SourceAnimationRandomContext(actor);
                    var c2Context = new SourceC2SoundContext(candidate.FunctionKind, Model.SourceTick, execution.Process,
                        execution.ProcessOrder, actor.Id, cue.Context.SourcePath, cue.FunctionSymbol,
                        cue.Context.SourceLine, cue.Argument0, cue.Argument1, cue.Argument2, cue,
                        firstCallIndex: execution.FirstCallIndex);
                    SourceAudioFunctionState actorState = SourceSoundState(actor);
                    ExecuteSourceC2Sound(c2Context, C2SoundState(actorState), actorState, execution);
                    return;
                }
            }
            if (preSnapSwitchSound)
            {
                sourceSoundVmFailed = true;
                throw new NotSupportedException("The enabled original hop has no authenticated fixed attack-sound binding; "
                    + "an Unsupported or legacy ground selector is not a successful fallback.");
            }
            SourceAudioVmBinding binding = null;
            foreach (SourceAudioVmBinding candidate in sourceSoundVmData.VmBindings)
                if (candidate.SourcePath == cue.Context.SourcePath && candidate.ScriptSymbol == cue.Context.ExecutingSymbol
                    && candidate.SourceLine == cue.Context.SourceLine && candidate.FunctionSymbol == cue.FunctionSymbol
                    && candidate.Argument0 == cue.Argument0 && candidate.Argument1 == cue.Argument1
                    && candidate.Argument2 == cue.Argument2) { binding = candidate; break; }
            if (binding == null)
            {
                sourceSoundVmPending.Add(new SourceSoundVmDispatch(cue, CurrentSourceRandomContext(),
                    SourceSoundState(actor), SourceSoundVmStatus.Unsupported, null,
                    "This original function/source tuple has no implemented sound/SPFX selector; the original cue is retained."));
                return;
            }
            SourceRandomContext context = SourceAnimationRandomContext(actor);
            SourceAudioFunctionState state = SourceSoundState(actor);
            try
            {
                SourceAudioFunctionResult result = SourceAudioFunctions.Select(sourceSoundVmData, cue, state,
                    sourceRandom, context);
                AdvanceSourceSoundRandom(context, result);
                sourceSoundVmPending.Add(new SourceSoundVmDispatch(cue, context, state,
                    SourceSoundVmStatus.Resolved, result, null));
            }
            catch (SourceAudioMissingInputException error)
            {
                AdvanceSourceSoundRandom(context, error.PartialResult);
                sourceSoundVmPending.Add(new SourceSoundVmDispatch(cue, context, state,
                    SourceSoundVmStatus.MissingInput, error.PartialResult, error.Field));
                sourceSoundVmFailed = true;
                throw;
            }
        }

        private void AdvanceSourceSoundRandom(SourceRandomContext context, SourceAudioFunctionResult result)
        {
            Model.SourceRandomCallIndex = checked(context.FirstCallIndex + result.RandomCalls);
            Model.SourceRandomDraws += result.RandomCalls;
        }

        private SourceC2SoundState C2SoundState(SourceAudioFunctionState actorState)
        {
            SourceAudioHitPartner hit = actorState.HitPartner;
            bool? present = hit.Presence == SourceAudioHitPresence.Unknown ? (bool?)null
                : hit.Presence == SourceAudioHitPresence.Present;
            return new SourceC2SoundState(Model.SourceTauntBank, Model.SourceCurrentPlayerBank,
                actorState.ActorFlags, actorState.ActorRawPlayerMode, actorState.ActorPlayerNumber,
                actorState.ActorStation, present, hit.RawPlayerMode, hit.Station,
                actorState.CurrentBallCarrier, actorState.CurrentIntendedReceiver);
        }

        private void ExecuteSourceC2NativeSound(SourceC2SoundContext context)
        {
            RequireSourceSoundVmBinding();
            ExecuteSourceC2Sound(context, new SourceC2SoundState(Model.SourceTauntBank, Model.SourceCurrentPlayerBank),
                null, CurrentSourceRandomContext());
        }

        private void ExecuteSourceC2Sound(SourceC2SoundContext context, SourceC2SoundState state,
            SourceAudioFunctionState? actorState, SourceRandomContext execution)
        {
            try
            {
                SourceAudioFunctionResult result = SourceC2SoundFunctions.Select(sourceC2Bootstrap.Data,
                    context.Kind, context, state, sourceRandom, execution);
                if (context.Kind == SourceC2SoundFunctionKind.AttackSoundFixed &&
                    (result == null || result.RandomCalls != 0 || result.Calls.Count != 1 ||
                        result.Calls[0] == null || result.Calls[0].SubcallOrder != 0 ||
                        result.Calls[0].RandomCalls != 0 || !ReferenceEquals(result.Calls[0].C2Context, context) ||
                        !result.Calls[0].HasOriginalAnimationCue ||
                        !ReferenceEquals(result.Calls[0].OriginalCue, context.OriginalAnimationCue)))
                {
                    if (result != null) AdvanceSourceSoundRandom(execution, result);
                    sourceSoundVmFailed = true;
                    throw new InvalidOperationException("Fixed hop sound requires one real cue-paired original call and zero random draws.");
                }
                AdvanceSourceSoundRandom(execution, result);
                sourceSoundVmPending.Add(new SourceSoundVmDispatch(context, execution, state, actorState,
                    SourceSoundVmStatus.Resolved, result, null));
            }
            catch (SourceAudioMissingInputException error)
            {
                AdvanceSourceSoundRandom(execution, error.PartialResult);
                sourceSoundVmPending.Add(new SourceSoundVmDispatch(context, execution, state, actorState,
                    SourceSoundVmStatus.MissingInput, error.PartialResult, error.Field));
                sourceSoundVmFailed = true;
                throw;
            }
        }
    }
}
