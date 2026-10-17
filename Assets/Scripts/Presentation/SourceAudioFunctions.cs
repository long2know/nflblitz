using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.Presentation
{
    public enum SourceAudioHitPresence { Unknown, Absent, Present }

    public readonly struct SourceAudioHitPartner
    {
        public SourceAudioHitPresence Presence { get; }
        public int? RawPlayerMode { get; }
        public int? Station { get; }
        private SourceAudioHitPartner(SourceAudioHitPresence presence, int? mode, int? station)
        {
            Presence = presence; RawPlayerMode = mode; Station = station;
        }
        public static SourceAudioHitPartner Absent
        { get { return new SourceAudioHitPartner(SourceAudioHitPresence.Absent, null, null); } }
        public static SourceAudioHitPartner Present(int? rawPlayerMode, int? station)
        { return new SourceAudioHitPartner(SourceAudioHitPresence.Present, rawPlayerMode, station); }
    }

    public readonly struct SourceAudioFunctionState
    {
        public int? ActorFlags { get; }
        public int? ActorRawPlayerMode { get; }
        public int? ActorPlayerNumber { get; }
        public int? ActorStation { get; }
        public SourceAudioHitPartner HitPartner { get; }
        public int? CurrentBallCarrier { get; }
        public int? CurrentIntendedReceiver { get; }
        public string CurrentPlayerBank { get; }
        public SourceAudioFunctionState(int? actorFlags, int? actorRawPlayerMode, int? actorPlayerNumber,
            int? actorStation, SourceAudioHitPartner hitPartner,
            int? currentBallCarrier, int? currentIntendedReceiver, string currentPlayerBank = null)
        {
            ActorFlags = actorFlags; ActorRawPlayerMode = actorRawPlayerMode;
            ActorPlayerNumber = actorPlayerNumber; ActorStation = actorStation; HitPartner = hitPartner;
            CurrentBallCarrier = currentBallCarrier; CurrentIntendedReceiver = currentIntendedReceiver;
            CurrentPlayerBank = currentPlayerBank;
        }
    }

    public sealed class SourceAudioResolvedOriginalSoundCall
    {
        public SourceCallbackCue OriginalCue { get; }
        public SourcePresentationContext OriginalCueContext { get { return OriginalCue.Context; } }
        public SourceAudioCalleeSite CalleeSourceSite { get; }
        public int SubcallOrder { get; }
        public string Bank { get; }
        public int PlaylistId { get; }
        public uint Volume { get; }
        public uint Pan { get; }
        public uint PriorityWord { get; }
        public int RandomCalls { get; }
        internal SourceAudioResolvedOriginalSoundCall(SourceCallbackCue cue, SourceAudioCalleeSite site,
            int order, string bank, int id, uint volume, uint pan, uint priority, int randomCalls)
        {
            OriginalCue = cue; CalleeSourceSite = site; SubcallOrder = order; Bank = bank;
            PlaylistId = id; Volume = volume; Pan = pan; PriorityWord = priority; RandomCalls = randomCalls;
        }
    }

    public sealed class SourceAudioFunctionResult
    {
        public ReadOnlyCollection<SourceAudioResolvedOriginalSoundCall> Calls { get; }
        public ReadOnlyCollection<SourceRandomDraw> RandomDraws { get; }
        public int RandomCalls { get { return RandomDraws.Count; } }
        internal SourceAudioFunctionResult(List<SourceAudioResolvedOriginalSoundCall> calls,
            List<SourceRandomDraw> draws)
        {
            Calls = Array.AsReadOnly(calls.ToArray()); RandomDraws = Array.AsReadOnly(draws.ToArray());
        }
    }

    public sealed class SourceAudioMissingInputException : InvalidOperationException
    {
        public string Field { get; }
        public SourceAudioFunctionResult PartialResult { get; }
        internal SourceAudioMissingInputException(string field, SourceAudioFunctionResult partial)
            : base("Original sound routine requires actual " + field + "; unknown is not zero or null.")
        {
            Field = field; PartialResult = partial;
        }
    }

    public static class SourceAudioFunctions
    {
        // Must execute at ANI_CODE in the actor process, using Core's SAME stream.
        // This is not a post-Step presentation event subscriber or playback engine.
        public static SourceAudioFunctionResult Select(SourceAudioFunctionData data, SourceCallbackCue cue,
            SourceAudioFunctionState state, ISourceRandom sameSourceRandom, SourceRandomContext randomContext)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            data.RequireBinding(cue);
            var execution = new Execution(data, cue, state, sameSourceRandom, randomContext);
            if (cue.FunctionSymbol == "get_hit_snd") execution.Hit();
            else execution.Ground();
            return execution.Result();
        }

        private sealed class Recorder : ISourceRandom
        {
            private readonly ISourceRandom source;
            private readonly SourceCallbackCue cue;
            internal readonly List<SourceRandomDraw> Draws = new List<SourceRandomDraw>();
            public SourceRandomProvenance Provenance { get { return source.Provenance; } }
            internal Recorder(ISourceRandom source, SourceCallbackCue cue) { this.source = source; this.cue = cue; }
            public SourceRandomDraw Draw(SourceRandomSite site)
            {
                if (site.SourceTick != cue.Context.SourceTick || site.ActorId != cue.Context.ActorPlayerId)
                    throw new ArgumentException("Original sound RNG tick/actor differs from the executing cue.");
                if (source == null) throw new InvalidOperationException("The same Core source RNG is required at " + site);
                SourceRandomDraw draw = source.Draw(site);
                Draws.Add(draw);
                return draw;
            }
        }

        private sealed class Execution
        {
            private readonly SourceAudioFunctionData data;
            private readonly SourceCallbackCue cue;
            private readonly SourceAudioFunctionState state;
            private readonly Recorder recorder;
            private readonly SourceRandomCursor cursor;
            private readonly List<SourceAudioResolvedOriginalSoundCall> calls = new List<SourceAudioResolvedOriginalSoundCall>();
            internal Execution(SourceAudioFunctionData data, SourceCallbackCue cue, SourceAudioFunctionState state,
                ISourceRandom random, SourceRandomContext context)
            {
                this.data = data; this.cue = cue; this.state = state;
                recorder = new Recorder(random, cue);
                cursor = new SourceRandomCursor(recorder, context);
            }
            internal SourceAudioFunctionResult Result() { return new SourceAudioFunctionResult(calls, recorder.Draws); }
            private int Need(int? value, string field)
            {
                if (!value.HasValue) throw new SourceAudioMissingInputException(field, Result());
                return value.Value;
            }
            private void NeedHit()
            {
                if (state.HitPartner.Presence == SourceAudioHitPresence.Unknown)
                    throw new SourceAudioMissingInputException("pobj_hit presence", Result());
            }
            private void Emit(SourceAudioSoundSite role, SourceAudioBranchParameters branch = default(SourceAudioBranchParameters))
            {
                SourceAudioCallTemplate template = data.Call(role);
                int id;
                if (template.Table != null)
                {
                    string identity = template.Site.SourcePath + ":" + template.Site.SourceLine + ":" +
                        template.Site.FunctionSymbol + ":randrng:" + template.Table.Symbol;
                    int index = cursor.Range(template.Table.Values.Count, identity);
                    id = template.Table.Values[index];
                }
                else id = template.ArgumentIndex == 0 ? cue.Argument0 :
                    template.ArgumentIndex == 1 ? cue.Argument1 : cue.Argument2;
                if (state.CurrentPlayerBank == null)
                    throw new SourceAudioMissingInputException("plyr_bnk_str", Result());
                if (state.CurrentPlayerBank != data.Bank)
                    throw new NotSupportedException("Current player bank differs from the pinned original function bank.");
                uint volume = template.VolumeMode == 0 ? template.Volume : branch.Volume;
                uint priority = template.PriorityMode == 0 ? template.Priority :
                    template.PriorityMode == 1 ? branch.PrimaryPriority : branch.SecondaryPriority;
                calls.Add(new SourceAudioResolvedOriginalSoundCall(cue, template.Site, calls.Count,
                    state.CurrentPlayerBank, id, volume, template.Pan, priority, cursor.Count));
            }
            internal void Hit()
            {
                if ((Need(state.ActorFlags, "actor.flags") & data.OnscreenFlag) == 0) return;
                if (Need(state.ActorRawPlayerMode, "actor.plyrmode") == data.LineMode)
                {
                    NeedHit();
                    if (state.HitPartner.Presence == SourceAudioHitPresence.Absent)
                        throw new InvalidOperationException("Original get_hit_snd dereferences null pobj_hit for a lineman.");
                    if (Need(state.HitPartner.RawPlayerMode, "pobj_hit.plyrmode") == data.LineMode)
                        Emit(SourceAudioSoundSite.HitLine);
                }
                int player = Need(state.ActorPlayerNumber, "actor.plyrnum");
                bool primary = player == Need(state.CurrentBallCarrier, "game_info.ball_carrier") ||
                    player == Need(state.CurrentIntendedReceiver, "ball_obj.int_receiver");
                if (primary)
                {
                    if (cue.Argument0 < 0) Emit(SourceAudioSoundSite.HitCarrierRandom);
                    else if (cue.Argument0 > 0) Emit(SourceAudioSoundSite.HitCarrierExplicit);
                    if (cue.Argument1 < 0) Emit(SourceAudioSoundSite.HitCarrierFlyRandom);
                    else if (cue.Argument1 > 0) Emit(SourceAudioSoundSite.HitCarrierFlyExplicit);
                    if (cue.Argument2 > 0) Emit(SourceAudioSoundSite.HitCarrierThird);
                }
                else
                {
                    if (cue.Argument0 < 0) Emit(SourceAudioSoundSite.HitOtherRandom);
                    else if (cue.Argument0 > 0) Emit(SourceAudioSoundSite.HitOtherExplicit);
                    if (cue.Argument1 < 0) Emit(SourceAudioSoundSite.HitOtherFlyRandom);
                    else if (cue.Argument1 > 0) Emit(SourceAudioSoundSite.HitOtherFlyExplicit);
                    if (cue.Argument2 > 0) Emit(SourceAudioSoundSite.HitOtherThird);
                }
            }
            internal void Ground()
            {
                bool human = Need(state.ActorStation, "actor.station") != data.DroneStation;
                if (!human)
                {
                    NeedHit();
                    human = state.HitPartner.Presence == SourceAudioHitPresence.Present &&
                        Need(state.HitPartner.Station, "pobj_hit.station") != data.DroneStation;
                }
                SourceAudioBranchParameters branch = human ? data.Human : data.Drone;
                if (cue.Argument0 < 0) Emit(SourceAudioSoundSite.GroundRandom, branch);
                else if (cue.Argument0 > 0) Emit(SourceAudioSoundSite.GroundExplicit, branch);
                if (cue.Argument1 < 0) Emit(SourceAudioSoundSite.GroundNeckRandom, branch);
                else if (cue.Argument1 > 0) Emit(SourceAudioSoundSite.GroundNeckExplicit, branch);
                if (cue.Argument2 < 0) Emit(SourceAudioSoundSite.GroundGruntRandom, branch);
                else if (cue.Argument2 > 0) Emit(SourceAudioSoundSite.GroundGruntExplicit, branch);
            }
        }
    }
}
