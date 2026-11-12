using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.Presentation
{
    public static class SourceOriginalAudioFunctions
    {
        private sealed class Recorder : ISourceRandom
        {
            private readonly ISourceRandom source;
            internal readonly List<SourceRandomDraw> Draws = new List<SourceRandomDraw>();
            public SourceRandomProvenance Provenance { get { return source.Provenance; } }
            internal Recorder(ISourceRandom source) { this.source = source ?? throw new ArgumentNullException(nameof(source)); }
            public SourceRandomDraw Draw(SourceRandomSite site)
            {
                SourceRandomDraw draw = source.Draw(site); Draws.Add(draw); return draw;
            }
        }

        private static void Context(SourceC2SoundContext context, SourceRandomContext random)
        {
            if (context == null || context.SourceTick != random.SourceTick || context.ActorId != random.ActorId ||
                context.Process != random.Process || context.ProcessOrder != random.ProcessOrder ||
                context.FirstCallIndex != random.FirstCallIndex)
                throw new ArgumentException("Original sound selection requires its actual executing source process context.");
        }

        public static SourceAudioFunctionResult SelectAttack(SourceOriginalSoundBindings bindings,
            SourceCallbackCue cue, SourceAudioFunctionState state, ISourceRandom sameSourceRandom,
            SourceRandomContext randomContext)
        {
            if (bindings == null || bindings.Require(cue).FunctionSymbol != "attack_snd")
                throw new ArgumentException("An exact closed original attack_snd tuple is required.");
            if (cue.Context.SourceTick != randomContext.SourceTick || cue.Context.ActorPlayerId != randomContext.ActorId)
                throw new ArgumentException("Original attack sound cannot be reselected by an observer.");
            var calls = new List<SourceAudioResolvedOriginalSoundCall>();
            var recorder = new Recorder(sameSourceRandom);
            var cursor = new SourceRandomCursor(recorder, randomContext);
            if (!state.ActorStation.HasValue)
                throw new SourceAudioMissingInputException("actor.station", new SourceAudioFunctionResult(calls, recorder.Draws));
            int station = state.ActorStation.Value;
            if (station < -1 || station > 3) throw new ArgumentException("Actual original station0..3 or drone-1 is required.");
            uint priority = station == -1 ? 15u : 10u;
            Action<int, int> emit = (id, line) => {
                if (string.IsNullOrEmpty(state.CurrentPlayerBank))
                    throw new SourceAudioMissingInputException("plyr_bnk_str", new SourceAudioFunctionResult(calls, recorder.Draws));
                calls.Add(new SourceAudioResolvedOriginalSoundCall(cue,
                    new SourceAudioCalleeSite("f5ac3fd2cec5c20998bb613066a355e0d0d4d0e9", "attack_snd", line),
                    calls.Count, state.CurrentPlayerBank, id, 160, 127, priority, recorder.Draws.Count));
            };
            if (cue.Argument0 < 0)
            {
                SourceAudioWeightedTable table = bindings.Table("plr_attack_snds");
                emit(table.Values[cursor.Range(table.Values.Count, "sounds.c:attack_snd:1636")], 1636);
            }
            else if (cue.Argument0 > 0) emit(cue.Argument0, 1638);
            if (cue.Argument1 > 0) emit(cue.Argument1, 1641);
            if (cue.Argument2 > 0) emit(cue.Argument2, 1643);
            return new SourceAudioFunctionResult(calls, recorder.Draws);
        }

        public static SourceAudioFunctionResult SelectHut(SourceOriginalSoundBindings bindings,
            SourceCallbackCue cue, SourceAudioFunctionState state, ISourceRandom sameSourceRandom,
            SourceRandomContext randomContext)
        {
            if (bindings == null || bindings.Require(cue).FunctionSymbol != "hut_snd")
                throw new ArgumentException("An exact closed original hut_snd tuple is required.");
            if (cue.Context.SourceTick != randomContext.SourceTick || cue.Context.ActorPlayerId != randomContext.ActorId)
                throw new ArgumentException("Original hut sound cannot be reselected by an observer.");
            var calls = new List<SourceAudioResolvedOriginalSoundCall>();
            var recorder = new Recorder(sameSourceRandom);
            var cursor = new SourceRandomCursor(recorder, randomContext);
            int id = cue.Argument0, line = 1662;
            if (id == -1 || id == -2)
            {
                line = id == -1 ? 1658 : 1660;
                SourceAudioWeightedTable table = bindings.Table(id == -1 ? "hut_snds" : "qb_point_snds");
                id = table.Values[cursor.Range(table.Values.Count, "sounds.c:hut_snd:" + line)];
            }
            if (string.IsNullOrEmpty(state.CurrentPlayerBank))
                throw new SourceAudioMissingInputException("plyr_bnk_str", new SourceAudioFunctionResult(calls, recorder.Draws));
            calls.Add(new SourceAudioResolvedOriginalSoundCall(cue,
                new SourceAudioCalleeSite("f5ac3fd2cec5c20998bb613066a355e0d0d4d0e9", "hut_snd", line),
                0, state.CurrentPlayerBank, id, 160, 127, 15, recorder.Draws.Count));
            return new SourceAudioFunctionResult(calls, recorder.Draws);
        }

        public static SourceAudioFunctionResult SelectTaunt(SourceOriginalSoundBindings bindings,
            SourceC2CallbackData callbackData, SourceC2SoundContext context, SourceC2SoundState state,
            ISourceRandom sameSourceRandom, SourceRandomContext randomContext)
        {
            Context(context, randomContext);
            if (bindings == null || !context.HasOriginalAnimationCue ||
                bindings.Require(context.OriginalAnimationCue).FunctionSymbol != "taunt_speech" ||
                callbackData == null || !callbackData.HasCompleteProgramCapability)
                throw new ArgumentException("The exact complete-original taunt capability and original callback are required.");
            return SourceC2SoundFunctions.Select(callbackData, SourceC2SoundFunctionKind.TauntSpeech,
                context, state, sameSourceRandom, randomContext);
        }

        // Core owns the original eligibility, qcreate and sleep decisions before these exact call sites.
        public static SourceAudioFunctionResult SelectSpeechTable(SourceOriginalSoundBindings bindings,
            SourceC2SoundContext context, string actualBank, ISourceRandom sameSourceRandom,
            SourceRandomContext randomContext)
        {
            Context(context, randomContext);
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            string table, function;
            uint volume, priority;
            int line;
            if (context.HasOriginalAnimationCue &&
                bindings.Require(context.OriginalAnimationCue).FunctionSymbol == "plyr_open_sp")
            {
                table = "im_open_sp"; function = "plyr_open_sp"; line = 563; volume = 205; priority = 5;
            }
            else if (context.SourcePath == "sounds.c" &&
                (context.FunctionSymbol == "sacked_sp_proc" && context.SourceLine == 1407 ||
                 context.FunctionSymbol == "tackled2_sp_proc" && (context.SourceLine == 1461 || context.SourceLine == 1463)))
            {
                line = context.SourceLine; function = context.FunctionSymbol; volume = 255;
                table = line == 1407 ? "qb_sacked_sp" : line == 1461 ? "weak_tackle_sp" : "hard_tackle_sp";
                priority = line == 1407 ? 0xe281u : 0xe285u;
            }
            else throw new NotSupportedException("Unregistered complete-original native speech call site.");
            var calls = new List<SourceAudioResolvedOriginalSoundCall>();
            var recorder = new Recorder(sameSourceRandom);
            var cursor = new SourceRandomCursor(recorder, randomContext);
            SourceAudioWeightedTable values = bindings.Table(table);
            int id = values.Values[cursor.Range(values.Values.Count, "sounds.c:" + function + ":" + line)];
            if (string.IsNullOrEmpty(actualBank))
                throw new SourceAudioMissingInputException("actual source speech bank", new SourceAudioFunctionResult(calls, recorder.Draws));
            calls.Add(new SourceAudioResolvedOriginalSoundCall(context,
                new SourceAudioCalleeSite("f5ac3fd2cec5c20998bb613066a355e0d0d4d0e9", function, line),
                0, actualBank, id, volume, 127, priority, recorder.Draws.Count));
            return new SourceAudioFunctionResult(calls, recorder.Draws);
        }
    }
}
