using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;

internal static partial class Program
{
    private static readonly List<object> soundEvidence = new List<object>();
    private static readonly HashSet<int> observedSoundTupleLines = new HashSet<int>();

    private static void SoundIntegrationChecks()
    {
        VerifyPrograms();
        GameSimulation unbound = Create(bindSound: false);
        unbound.Step(default(PlayerCommand));
        Check(unbound.SourceTick == 0, "empty host frame does not manufacture a source sound binding");
        bool rejected = false;
        try { unbound.Step(default(PlayerCommand)); }
        catch (InvalidOperationException e) { rejected = e.Message.Contains("Bind the exact original SourceAudioFunctionData"); }
        Check(rejected, "actual C1 generation retains mandatory pinned SAFN before first executing wake");
        Check(real.SoundFunctions.PayloadSha256 == GameSimulation.SourceSoundVmDataSha256
            && real.SoundFunctions.VmBindings.Count == 6, "actual immutable SAFN reader supplies exactly the frozen synchronous six");
        TurboSideContactSound();
        FrontContactSound();
        ActualContactPartialFailure();
        Check(observedSoundTupleLines.SetEquals(new[] { 2448, 2461, 5544, 5555, 15955, 15998 }),
            "actual two source C1 contacts cover exactly all six frozen synchronous tuples, not injected output labels");
        File.WriteAllText(Path.Combine(output, "sound-integration-scenarios.json"),
            JsonSerializer.Serialize(soundEvidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static int ContactRaw(SourceRandomSite site, Random outside, bool turbo)
    {
        if (site.SourceIdentity.StartsWith("sounds.c:", StringComparison.Ordinal)) return 7;
        if (site.SourceIdentity == "plyrseq.c:h_front1_anim" && site.BoundOrModulus == 100) return 0;
        if (site.SourceIdentity == "detect.c:evade_vs_tackle:2429") return 3;
        if (site.SourceIdentity == "detect.c:evade_vs_tackle:2432") return 0;
        if (site.SourceIdentity.StartsWith("detect.c:", StringComparison.Ordinal))
            return !turbo && site.BoundOrModulus == 7 ? 2 : 99;
        return outside.Next(0, 0x1000000);
    }

    private static void RecordSound(string name, GameSimulation game, IReadOnlyList<SourceSoundVmDispatch> dispatches)
    {
        foreach (SourceSoundVmDispatch d in dispatches.Where(d => d.Status == SourceSoundVmStatus.Resolved))
            observedSoundTupleLines.Add(d.OriginalCueContext.SourceLine);
        soundEvidence.Add(new {
            name, source_tick = game.SourceTick, phase = game.Model.Phase.ToString(),
            dispatches = dispatches.Select(d => new {
                status = d.Status.ToString(), line = d.OriginalCueContext.SourceLine,
                script = d.OriginalCueContext.ExecutingSymbol, function = d.OriginalCue.FunctionSymbol,
                actor = d.OriginalCueContext.ActorPlayerId, d.RandomContext.SourceTick,
                d.RandomContext.Process, d.RandomContext.ProcessOrder, d.RandomContext.FirstCallIndex,
                actor_mode = d.StateAtCall.ActorRawPlayerMode, actor_flags = d.StateAtCall.ActorFlags,
                hit_presence = d.StateAtCall.HitPartner.Presence.ToString(),
                partner_mode = d.StateAtCall.HitPartner.RawPlayerMode,
                partner_station = d.StateAtCall.HitPartner.Station, d.Detail,
                random_calls = d.Result == null ? -1 : d.Result.RandomCalls,
                calls = d.Result == null ? null : d.Result.Calls.Select(c => new {
                    c.Bank, c.PlaylistId, c.Volume, c.Pan, c.PriorityWord, c.RandomCalls }).ToArray()
            }).ToArray(),
            contact_events = game.ContactEvents.Select(c => new {
                kind = c.Kind.ToString(), c.ActorPlayerId, c.OtherPlayerId, c.AttackMode, c.ScriptSymbol }).ToArray()
        });
    }

    private static void TurboSideContactSound()
    {
        var outside = new Random(711);
        GameSimulation game = Create(site => ContactRaw(site, outside, true));
        PairFixture(game, separation: 9f);
        ActorState victim = game.Model.Players[4], attacker = game.Model.Players[11];
        Stand(game, attacker);
        victim.Facing = victim.TargetFacing = victim.UpperFacing = victim.TargetUpperFacing = 384;
        attacker.Facing = attacker.TargetFacing = attacker.UpperFacing = attacker.TargetUpperFacing = 640;
        game.TakeSourceSoundVmDispatches();
        Wake(game, RawPair(away: SourceButtons.C));
        Check(attacker.AnimationState.Primary.ScriptSymbol == "t_dshoul_anim"
            && victim.AnimationState.Primary.ScriptSymbol == "fall_04",
            "actual C1 turbo SIDE probability table produces real T_DSHOUL/FALL_04, not old Contact");
        SourceSoundVmBatch firstBatch = game.TakeSourceSoundVmDispatches();
        SourceSoundVmDispatch[] first = firstBatch.Dispatches.Where(d => d.Status == SourceSoundVmStatus.Resolved).ToArray();
        Check(first.Length == 2 && first[0].OriginalCueContext.SourceLine == 2448
            && first[1].OriginalCueContext.SourceLine == 15955,
            "actual C1 immediate attacker/victim ANI_CODE resolves synchronous source six before leaving contact");
        Check(first[0].RandomContext.Process == "detect_proc" && first[1].RandomContext.Process == "detect_proc"
            && first[0].RandomContext.ActorId == 11 && first[1].RandomContext.ActorId == 4
            && first[0].RandomContext.ProcessOrder < first[1].RandomContext.ProcessOrder,
            "real preball collision preserves same process and receiving actor subdispatch ordering");
        Check(first[0].RandomContext.FirstCallIndex == 4 && first[1].RandomContext.FirstCallIndex == 4
            && first[0].Result.RandomCalls == 0 && first[1].Result.RandomCalls == 2,
            "four actual C1 draws precede zero-draw explicit hit then victim's two same-stream draws without cursor reset");
        Check(first[0].StateAtCall.HitPartner.Presence == SourceAudioHitPresence.Present
            && first[0].StateAtCall.HitPartner.RawPlayerMode == (int)victim.SourceMode
            && first[1].StateAtCall.HitPartner.Presence == SourceAudioHitPresence.Present
            && first[1].StateAtCall.HitPartner.RawPlayerMode == (int)attacker.SourceMode,
            "real bilateral source hit pointers precede BOTH immediate Who snapshots");
        SourceRandomDraw[] contact = ((SourceInjectedRandom)game.SourceRandomService).Trace
            .Where(d => d.Site.SourceTick == game.SourceTick && d.Site.Process == "detect_proc").ToArray();
        Check(contact.Select(d => d.Site.CallIndex).SequenceEqual(Enumerable.Range(0, 8))
            && contact[6].Site.SourceIdentity == "detect.c:fumble_or_bobble"
            && contact[7].Site.SourceIdentity.Contains("select_csi_item"),
            "actual fumble and CSI caller consume after sound on SAME ordered source stream, before ball_proc");
        Check(game.TakeSourceSoundVmDispatches().Dispatches.Count == 0, "real C1 sound FIFO drains once, without reselect");
        var all = new List<SourceSoundVmDispatch>(firstBatch.Dispatches);
        var lines = new HashSet<int>(first.Select(d => d.OriginalCueContext.SourceLine));
        for (int i = 0; i < 90 && !(lines.Contains(2461) && lines.Contains(15998)); i++)
        {
            Wake(game, RawPair());
            SourceSoundVmBatch batch = game.TakeSourceSoundVmDispatches();
            all.AddRange(batch.Dispatches);
            foreach (SourceSoundVmDispatch d in batch.Dispatches.Where(d => d.Status == SourceSoundVmStatus.Resolved))
                lines.Add(d.OriginalCueContext.SourceLine);
        }
        Check(lines.IsSupersetOf(new[] { 2448, 2461, 15955, 15998 }),
            "actual selected T_DSHOUL/FALL_04 VM and physics reach both original ground sound sites");
        Check(firstBatch.Dispatches.Count == 2 && firstBatch.Dispatches[1].Result.RandomCalls == 2,
            "detached resolved impact FIFO survives later VM/physics/down progression without mutation");
        RecordSound("actual-C1-turbo-side-impacts-and-ground", game, all);
        Observe("bound-sound-real-turbo-side-contact", game, 4, 11);
    }

    private static void FrontContactSound()
    {
        var outside = new Random(712);
        GameSimulation game = Create(site => ContactRaw(site, outside, false));
        PairFixture(game);
        Stand(game, game.Model.Players[11]);
        game.TakeSourceSoundVmDispatches();
        Wake(game, RawPair());
        Check(game.Model.Players[4].AnimationState.Primary.ScriptSymbol == "h_front1_anim",
            "real no-A non-turbo body contact produces original front reaction with mandatory SAFN");
        var all = new List<SourceSoundVmDispatch>(game.TakeSourceSoundVmDispatches().Dispatches);
        for (int i = 0; i < 120 && !all.Any(d => d.Status == SourceSoundVmStatus.Resolved
            && d.OriginalCueContext.SourceLine == 5555); i++)
        {
            Wake(game, RawPair());
            all.AddRange(game.TakeSourceSoundVmDispatches().Dispatches);
        }
        SourceSoundVmDispatch[] front = all.Where(d => d.Status == SourceSoundVmStatus.Resolved
            && d.OriginalCueContext.ExecutingSymbol == "h_front1_anim").ToArray();
        Check(front.Select(d => d.OriginalCueContext.SourceLine).SequenceEqual(new[] { 5544, 5555 }),
            "real H_FRONT1 progression reaches remaining frozen two sound tuples exactly once and in source order");
        Check(front[1].StateAtCall.CurrentBallCarrier == 4 && front[1].StateAtCall.HitPartner.Presence == SourceAudioHitPresence.Present,
            "ground selector captures actual current carrier/Who after source down progression, never replayed hit state");
        RecordSound("actual-C1-front-impact-and-ground", game, all);
        Observe("bound-sound-real-front-contact", game, 4, 11);
    }

    private static void ActualContactPartialFailure()
    {
        var outside = new Random(713);
        GameSimulation game = Create(site => ContactRaw(site, outside, false));
        PairFixture(game);
        Stand(game, game.Model.Players[11]);
        game.Model.SourceCurrentPlayerBank = null;
        game.TakeSourceSoundVmDispatches();
        SourceAudioMissingInputException missing = null;
        try { Wake(game, RawPair()); }
        catch (SourceAudioMissingInputException e) { missing = e; }
        Check(missing != null && missing.Field == "plyr_bnk_str" && missing.PartialResult.RandomCalls == 1,
            "actual C1 callback rethrows precise late SAFN missing input AFTER its real primary draw");
        Check(game.Model.SourceRandomCallIndex == 4
            && ((SourceInjectedRandom)game.SourceRandomService).Trace.Last().Site.CallIndex == 3,
            "actual C1 partial sound accounting keeps three preceding collision draws plus consumed selector draw");
        int drawCount = ((SourceInjectedRandom)game.SourceRandomService).Trace.Count;
        long tick = game.SourceTick;
        bool blocked = false;
        try { game.Step(default(PlayerCommand)); }
        catch (InvalidOperationException e) { blocked = e.Message.Contains("Restart is required"); }
        Check(blocked && game.SourceTick == tick
            && ((SourceInjectedRandom)game.SourceRandomService).Trace.Count == drawCount,
            "partial C1+Sound execution cannot retry, mute, rewind, or consume another draw");
        SourceSoundVmBatch batch = game.TakeSourceSoundVmDispatches();
        Check(batch.Dispatches.Count == 2
            && batch.Dispatches[0].Status == SourceSoundVmStatus.Unsupported
            && batch.Dispatches[0].OriginalCueContext.ActorPlayerId == 11
            && batch.Dispatches[0].OriginalCueContext.SourceLine == 2530
            && batch.Dispatches[0].Result == null
            && batch.Dispatches[1].Status == SourceSoundVmStatus.MissingInput
            && batch.Dispatches[1].OriginalCueContext.ActorPlayerId == 4
            && batch.Dispatches[1].OriginalCueContext.SourceLine == 5544
            && batch.Dispatches[1].Result.RandomCalls == 1
            && batch.Dispatches[1].RandomContext.FirstCallIndex == 3
            && batch.Dispatches[0].OriginalCueContext.ProcessOrder < batch.Dispatches[1].OriginalCueContext.ProcessOrder,
            "partial FIFO preserves actual preceding T_STSHLD attack_snd Unsupported THEN H_FRONT1 missing-input result in order");
        RecordSound("actual-C1-late-sound-input-fault", game, batch.Dispatches);
        game.Restart();
        Check(game.Model.SourceAnimationFailure == null && game.Model.SourceCurrentPlayerBank == "players"
            && game.TakeSourceSoundVmDispatches().Dispatches.Count == 0,
            "real Restart clears both C1 and Sound faults/queues and reinitializes bank/Who without losing SAFN binding");
        game.Step(default(PlayerCommand)); game.Step(default(PlayerCommand));
        Check(game.SourceTick == 1 && batch.Dispatches[1].Result.RandomCalls == 1,
            "restarted mandatory-bound real kernel wakes, detached partial result remains unchanged");
    }
}
