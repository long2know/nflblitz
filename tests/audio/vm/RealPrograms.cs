using System;
using System.Collections.Generic;
using System.Linq;
using Blitz.Contracts;
using Blitz.Gameplay;

internal static partial class Program
{
    private static void RealSourcePrograms()
    {
        var observed = new HashSet<int>();
        foreach (string script in new[] { "t_dshoul_anim", "h_front1_anim", "fall_04" })
        {
            var stream = new InputStream();
            GameSimulation game = Create(stream);
            ActorState actor = game.Model.Players[4];
            game.Model.Phase = GamePhase.DeadBall;
            ActorInputs(game, actor, 2, 7, 0, 8, carrier: actor.Id);
            Begin(game, actor, 1);
            int before = stream.Random.Trace.Count;
            actor.AnimationState.FullBody(script, actor, 1, Bind<Func<int, int>>(game, "SourceDraw"),
                AnimationSelectionEvidence.SourceVerified);
            SourceSoundVmBatch initial = game.TakeSourceSoundVmDispatches();
            Check(initial.Dispatches.Count > 0 && initial.Dispatches[0].Status == SourceSoundVmStatus.Resolved,
                "real source Change executes first ANI_CODE before returning " + script);
            foreach (SourceSoundVmDispatch item in initial.Dispatches) observed.Add(item.OriginalCueContext.SourceLine);
            if (script != "t_dshoul_anim")
                Check(stream.Random.Trace.Count > before, "real initial hit selection consumes shared RNG synchronously " + script);
            for (int tick = 2; tick < 150; tick++)
            {
                Begin(game, actor, tick);
                actor.AnimationState.Advance(actor, tick, Bind<Func<int, int>>(game, "SourceDraw"));
                Bind<Action<ActorState>>(game, "IntegrateSourceMovementPlayer")(actor);
                SourceSoundVmBatch batch = game.TakeSourceSoundVmDispatches();
                foreach (SourceSoundVmDispatch item in batch.Dispatches)
                    if (item.Status == SourceSoundVmStatus.Resolved)
                    {
                        observed.Add(item.OriginalCueContext.SourceLine);
                        Equal(item.OriginalCueContext.SourceTick, item.RandomContext.SourceTick, "real callee tick equals actual VM call tick");
                        Equal(actor.Id, item.RandomContext.ActorId, "real callee actual actor identity");
                        Check(ReferenceEquals(item.OriginalCue, item.Result.Calls[0].OriginalCue),
                            "immutable envelope is paired with actual original cue instance, not reconstructed later");
                    }
                if (observed.Contains(functions.VmBindings.First(b => b.ScriptSymbol == script
                    && b.FunctionSymbol == "grnd_hit_snd").SourceLine)) break;
            }
        }
        Equal(6, observed.Count, "all six actual unmodified source VM tuples reach synchronous integration");
        foreach (var binding in functions.VmBindings)
            Check(observed.Contains(binding.SourceLine), "actual reached six-site inventory " + binding.SourceLine);
    }
}
