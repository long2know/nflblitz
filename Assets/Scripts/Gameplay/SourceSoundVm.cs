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
        public SourceCallbackCue OriginalCue { get; }
        public SourcePresentationContext OriginalCueContext { get { return OriginalCue.Context; } }
        public SourceRandomContext RandomContext { get; }
        public SourceAudioFunctionState StateAtCall { get; }
        public SourceSoundVmStatus Status { get; }
        public SourceAudioFunctionResult Result { get; }
        public string Detail { get; }

        internal SourceSoundVmDispatch(SourceCallbackCue cue, SourceRandomContext context,
            SourceAudioFunctionState state, SourceSoundVmStatus status, SourceAudioFunctionResult result, string detail)
        {
            OriginalCue = cue; RandomContext = context; StateAtCall = state;
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
    }
}
