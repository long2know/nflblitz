using System;
using System.Collections.Generic;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;

namespace Blitz.Presentation
{
    public static class SourceC2SoundFunctions
    {
        public static SourceAudioFunctionResult Select(SourceC2CallbackData data, SourceC2SoundFunctionKind kind,
            SourceC2SoundContext context, SourceC2SoundState state, ISourceRandom sameSourceRandom,
            SourceRandomContext randomContext)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (kind != context.Kind || context.SourceTick != randomContext.SourceTick ||
                context.ActorId != randomContext.ActorId || context.Process != randomContext.Process ||
                context.ProcessOrder != randomContext.ProcessOrder || context.FirstCallIndex != randomContext.FirstCallIndex)
                throw new ArgumentException("C2 sound must execute synchronously in its actual source process.");
            if (kind == SourceC2SoundFunctionKind.HitFunctionExtension)
            {
                SourceC2VmBinding binding = data.RequireBinding(context.OriginalAnimationCue);
                if (binding.FunctionKind != kind) throw new ArgumentException("Wrong C2 hit function kind.");
                SourceAudioHitPartner hit = !state.HasHitPartner.HasValue ? default(SourceAudioHitPartner)
                    : !state.HasHitPartner.Value ? SourceAudioHitPartner.Absent
                    : SourceAudioHitPartner.Present(state.HitPartnerRawPlayerMode, state.HitPartnerStation);
                return SourceAudioFunctions.Select(data.HitExtensionData, context.OriginalAnimationCue,
                    new SourceAudioFunctionState(state.ActorFlags, state.ActorRawPlayerMode, state.ActorPlayerNumber,
                        state.ActorStation, hit, state.CurrentBallCarrier, state.CurrentIntendedReceiver,
                        state.CurrentPlayerBank), sameSourceRandom, randomContext);
            }
            var execution = new Execution(data, context, state, sameSourceRandom, randomContext);
            if (kind == SourceC2SoundFunctionKind.TauntSpeech)
            {
                if (!context.HasOriginalAnimationCue || context.FunctionSymbol != "taunt_speech" ||
                    context.OriginalAnimationCue.FunctionSymbol != "taunt_speech" ||
                    context.Argument0 != context.OriginalAnimationCue.Argument0 ||
                    context.Argument1 != context.OriginalAnimationCue.Argument1 ||
                    context.Argument2 != context.OriginalAnimationCue.Argument2)
                    throw new ArgumentException("Actual original taunt_speech arguments/cue are required.");
                execution.Taunt();
            }
            else if (kind == SourceC2SoundFunctionKind.WhistleProc || kind == SourceC2SoundFunctionKind.WhistleImmediate)
                execution.Whistle();
            else throw new NotSupportedException("Unqualified C2 sound function.");
            return execution.Result();
        }

        private sealed class Recorder : ISourceRandom
        {
            private readonly ISourceRandom source;
            internal readonly List<SourceRandomDraw> Draws = new List<SourceRandomDraw>();
            internal Recorder(ISourceRandom source) { this.source = source; }
            public SourceRandomProvenance Provenance { get { return source.Provenance; } }
            public SourceRandomDraw Draw(SourceRandomSite site)
            {
                if (source == null) throw new InvalidOperationException("Original C2 sound requires Core's same source RNG.");
                SourceRandomDraw value = source.Draw(site);
                Draws.Add(value);
                return value;
            }
        }

        private sealed class Execution
        {
            private readonly SourceC2CallbackData data;
            private readonly SourceC2SoundContext context;
            private readonly SourceC2SoundState state;
            private readonly Recorder recorder;
            private readonly SourceRandomCursor cursor;
            private readonly List<SourceAudioResolvedOriginalSoundCall> calls = new List<SourceAudioResolvedOriginalSoundCall>();
            internal Execution(SourceC2CallbackData data, SourceC2SoundContext context, SourceC2SoundState state,
                ISourceRandom random, SourceRandomContext randomContext)
            {
                this.data = data; this.context = context; this.state = state;
                recorder = new Recorder(random); cursor = new SourceRandomCursor(recorder, randomContext);
            }
            internal SourceAudioFunctionResult Result() { return new SourceAudioFunctionResult(calls, recorder.Draws); }
            private string Bank(string value, string field)
            {
                if (string.IsNullOrEmpty(value)) throw new SourceAudioMissingInputException(field, Result());
                return value;
            }
            private int Draw(int count, string function, int line)
            { return cursor.Range(count, (function == "taunt_speech" ? "sounds.c:" : "player.c:") + function + ":" + line); }
            private void Emit(string bank, int id, int line)
            {
                SourceC2SoundCallTemplate call = data.Call(line);
                calls.Add(new SourceAudioResolvedOriginalSoundCall(context, call.Site, calls.Count, bank, id,
                    call.Volume, call.Pan, call.Priority, recorder.Draws.Count));
            }

            internal void Taunt()
            {
                int sp = context.Argument0;
                string table = sp == -1 ? "short_taunt_sp" : sp == -2 ? "long_taunt_sp" :
                    sp == -3 ? "laugh_taunt_sp" : sp == -4 ? "histep_taunt_sp" : null;
                int line = sp == -1 ? 511 : sp == -2 ? 513 : sp == -3 ? 515 : sp == -4 ? 519 :
                    sp == -5 ? 524 : 527;
                if (sp == -4 && Draw(100, "taunt_speech", 518) >= 10 ||
                    sp == -5 && Draw(100, "taunt_speech", 523) >= 10) return;
                int id = table != null ? data.SpeechTable(table)[Draw(data.SpeechTable(table).Count,
                    "taunt_speech", line)] : sp == -5 ? 11 : sp;
                string bank = Bank(state.CurrentTauntBank, "taunt_bnk_str");
                Emit(bank, id, line);
            }

            internal void Whistle()
            {
                bool deferred = context.Kind == SourceC2SoundFunctionKind.WhistleProc;
                if (context.SourcePath != "player.c" ||
                    deferred && (context.FunctionSymbol != "whistle_proc" || context.SourceLine != 6018 || context.ActorId != -1) ||
                    !deferred && !(context.FunctionSymbol == "_mode_qb" && context.SourceLine == 1839 ||
                        context.FunctionSymbol == "_mode_wpn" && context.SourceLine == 2434))
                    throw new ArgumentException("Only the actual qualified original whistle call sites are supported.");
                int id = 139 + Draw(2, context.FunctionSymbol, context.SourceLine);
                string bank = Bank(state.CurrentPlayerBank, "plyr_bnk_str");
                Emit(bank, id, context.SourceLine);
            }
        }
    }
}
