using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;

internal static class Program
{
    private static int checks, cases, lazyChecks, rawDraws, resolvedCalls;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("CONTRACT_ASSERTION: " + message);
    }
    private static void Throws<T>(Action action, string message) where T : Exception
    {
        checks++;
        try { action(); } catch (T) { return; }
        throw new Exception("CONTRACT_ASSERTION: must reject " + message);
    }
    private static T Succeed<T>(Func<T> action, string message)
    {
        try { return action(); }
        catch (Exception error) { throw new Exception("CONTRACT_ASSERTION: " + message, error); }
    }
    private static string Hash(byte[] bytes)
    {
        using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    private static SourceCallbackCue Cue(SourceAudioVmBinding binding, int actor, int order = 19)
    {
        return new SourceCallbackCue(new SourcePresentationContext(100, order, actor,
            binding.SourcePath, binding.ScriptSymbol, binding.SourceLine, 91, "ANI_CODE"),
            SourcePresentationCueKind.SoundCallback, binding.FunctionSymbol,
            binding.Argument0, binding.Argument1, binding.Argument2);
    }
    private static SourceRandomContext Context(SourceCallbackCue cue)
    {
        // Deliberately distinct process order vs total VM-cue order.
        return new SourceRandomContext(cue.Context.SourceTick, cue.Context.ActorPlayerId,
            "RL_PLAYER", 7, "sound-recorded-C", 3);
    }
    private static SourceInjectedRandom Tape(int[] raw, Action consumed = null)
    {
        int at = 0;
        return new SourceInjectedRandom(site => {
            if (at == raw.Length) throw new Exception("raw tape exhausted");
            if (consumed != null) consumed();
            return raw[at++];
        }, new SourceRandomProvenance("sound-recorded-C", "recorded raw C-function oracle",
            "explicit test tape; not a PRNG algorithm or production seed", SourceRandomEvidence.RecordedOracle));
    }
    private static SourceAudioFunctionResult Select(SourceAudioFunctionData data, SourceCallbackCue cue,
        SourceAudioFunctionState state, int[] raw = null)
    {
        return SourceAudioFunctions.Select(data, cue, state, Tape(raw ?? new[] { 0, 1, 2, 3, 4, 5 }), Context(cue));
    }
    private static SourceAudioFunctionState State(int? flags, int? mode, int? player, int? station,
        SourceAudioHitPartner hit, int? carrier, int? receiver, string bank = "players")
    {
        return new SourceAudioFunctionState(flags, mode, player, station, hit, carrier, receiver, bank);
    }

    private static void Boundaries(SourceAudioFunctionData data, byte[] payload)
    {
        Check(data.Tables.Any(t => t.Values.Distinct().Count() != t.Values.Count), "weighted duplicates must survive");
        SourceAudioVmBinding hit = data.VmBindings.First(x => x.FunctionSymbol == "get_hit_snd" && x.Argument0 < 0);
        SourceAudioVmBinding fixedHit = data.VmBindings.First(x => x.FunctionSymbol == "get_hit_snd" && x.Argument0 > 0);
        SourceAudioVmBinding ground = data.VmBindings.First(x => x.FunctionSymbol == "grnd_hit_snd" && x.Argument2 < 0);
        SourceCallbackCue cue = Cue(hit, 0);
        var unknown = default(SourceAudioHitPartner);
        var off = State(0, null, null, null, unknown, null, null, null);
        SourceAudioFunctionResult silent = Succeed(() => SourceAudioFunctions.Select(data, cue, off, null,
            default(SourceRandomContext)), "offscreen short-circuit requires no other fields or RNG");
        Check(silent.Calls.Count == 0 && silent.RandomCalls == 0, "offscreen must return before all state/RNG reads");
        Throws<SourceAudioMissingInputException>(() => Select(data, cue,
            State(null, null, null, null, unknown, null, null)), "unknown visibility");
        SourceAudioFunctionState nonline = State(data.OnscreenFlag, data.LineMode + 1, 0, null, unknown, 0, null);
        SourceAudioFunctionResult carrier = Succeed(() => Select(data, cue, nonline), "carrier OR short-circuits receiver");
        Check(carrier.RandomCalls == 2 && carrier.Calls.Count == 2, "carrier branch without hit partner/station/receiver");
        Check(carrier.Calls[0].Volume == data.Human.Volume, "carrier primary volume parameters");
        Check(carrier.Calls.Select(x => x.SubcallOrder).SequenceEqual(new[] { 0, 1 }), "subcall order starts at zero");
        SourceAudioFunctionResult other = Select(data, cue,
            State(data.OnscreenFlag, data.LineMode + 1, 0, null, unknown, -1, -1));
        Check(other.RandomCalls == 2 && other.Calls.Count == 2, "noncarrier retains two original draws");
        Throws<SourceAudioMissingInputException>(() => Select(data, cue,
            State(data.OnscreenFlag, data.LineMode + 1, 0, null, unknown, 0, null, null)), "unknown actual player bank");
        Throws<NotSupportedException>(() => Select(data, cue,
            State(data.OnscreenFlag, data.LineMode + 1, 0, null, unknown, 0, null, "other")), "unprepared actual player bank");
        Throws<SourceAudioMissingInputException>(() => Select(data, cue,
            State(data.OnscreenFlag, data.LineMode + 1, 0, null, unknown, -1, null)), "needed intended receiver");
        Throws<SourceAudioMissingInputException>(() => Select(data, cue,
            State(data.OnscreenFlag, data.LineMode, 0, null, unknown, 0, null)), "unknown lineman hit presence");
        Throws<InvalidOperationException>(() => Select(data, cue,
            State(data.OnscreenFlag, data.LineMode, 0, null, SourceAudioHitPartner.Absent, 0, null)), "actual null lineman hit");
        try
        {
            Select(data, cue, State(data.OnscreenFlag, data.LineMode, null, null,
                SourceAudioHitPartner.Present(data.LineMode, null), null, null));
            throw new Exception("CONTRACT_ASSERTION: missing actor number did not throw after lineman call");
        }
        catch (SourceAudioMissingInputException e)
        {
            Check(e.Field == "actor.plyrnum", "precise missing field");
            Check(e.PartialResult.Calls.Count == 1 && e.PartialResult.RandomCalls == 1,
                "additional lineman subcall/RNG occurs BEFORE missing carrier state");
        }
        SourceCallbackCue fixedCue = Cue(fixedHit, 0);
        SourceAudioFunctionResult fixedResult = SourceAudioFunctions.Select(data, fixedCue,
            nonline, null, default(SourceRandomContext));
        Check(fixedResult.RandomCalls == 0 && fixedResult.Calls[0].PlaylistId == fixedHit.Argument0,
            "fixed original call does not invent a draw");
        SourceAudioFunctionResult extra = Select(data, fixedCue, State(data.OnscreenFlag, data.LineMode,
            0, null, SourceAudioHitPartner.Present(data.LineMode, null), 0, null));
        Check(extra.RandomCalls == 1 && extra.Calls.Count == 2, "fixed hit still has additional lineman branch");
        SourceCallbackCue groundCue = Cue(ground, 0);
        var human = State(null, null, null, data.DroneStation + 1, unknown, null, null);
        SourceAudioFunctionResult humanResult = Succeed(() => Select(data, groundCue, human),
            "ground human OR short-circuits partner and all flags/modes/carrier fields");
        Check(humanResult.RandomCalls == 2 && humanResult.Calls[0].Volume == data.Human.Volume,
            "ground has no visibility read and human station short-circuits hit pointer");
        Check(humanResult.Calls[0].PriorityWord == data.Human.PrimaryPriority, "ground primary priority is not secondary");
        SourceAudioFunctionResult drone = Select(data, groundCue,
            State(null, null, null, data.DroneStation, SourceAudioHitPartner.Absent, null, null));
        Check(drone.Calls[0].Volume == data.Drone.Volume, "actual null partner follows drone branch");
        Throws<SourceAudioMissingInputException>(() => Select(data, groundCue,
            State(null, null, null, data.DroneStation, unknown, null, null)), "unknown vs absent ground hit");
        Throws<SourceAudioMissingInputException>(() => Select(data, groundCue,
            State(null, null, null, data.DroneStation, SourceAudioHitPartner.Present(null, null), null, null)),
            "needed partner station");
        var down = new SourceCallbackCue(cue.Context, SourcePresentationCueKind.PlayerDownCallback,
            "player_down", 0, 0, 0);
        Throws<ArgumentException>(() => SourceAudioFunctions.Select(data, down, off, null, Context(cue)), "PlayerDown is not sound");
        var changed = new SourceCallbackCue(cue.Context, cue.Kind, cue.FunctionSymbol, 0, 0, 0);
        Throws<NotSupportedException>(() => SourceAudioFunctions.Select(data, changed, off, null, Context(cue)), "unknown source tuple");
        Throws<NotSupportedException>(() => SourceAudioFunctions.Select(data,
            new SourceCallbackCue(new SourcePresentationContext(100, 19, 0, "plyrseq.c", "unrelated", 1),
                cue.Kind, cue.FunctionSymbol, cue.Argument0, cue.Argument1, cue.Argument2),
            off, null, Context(cue)), "wrong source site");
        int count = 0;
        SourceInjectedRandom shared = Tape(new[] { 7, 8, 9, 10, 11, 12, 13 }, () => count++);
        var before = new SourceRandomContext(100, 0, "RL_PLAYER", 7, "sound-recorded-C");
        for (int i = 0; i < 3; i++) shared.Draw(before.At(SourceRandomKind.RandRange, 101, "before ANI_CODE", i));
        SourceAudioFunctionResult same = Succeed(() => SourceAudioFunctions.Select(data, cue, nonline, shared, Context(cue)),
            "same RNG stream preserves existing Core call cursor");
        SourceRandomDraw after = shared.Draw(before.At(SourceRandomKind.RandRange, 101, "after ANI_CODE", 3 + same.RandomCalls));
        Check(count == 6 && after.Raw == 12 && same.RandomDraws[0].Raw == 10,
            "same provided RNG stream must preserve before/function/after state");
        Check(same.RandomDraws[0].Site.CallIndex == 3 && same.RandomDraws[1].Site.CallIndex == 4,
            "Core first-call offset is preserved");
        Check(same.Calls[0].OriginalCue == cue && same.Calls[0].OriginalCueContext.ProcessOrder == 19,
            "original cue context is not replaced by random process order");
        Throws<NotSupportedException>(() => ((IList<SourceAudioResolvedOriginalSoundCall>)same.Calls).Clear(), "immutable calls");
        Throws<NotSupportedException>(() => ((IList<int>)data.Tables[0].Values).Clear(), "immutable weighted table");
        byte[] changedPin = (byte[])payload.Clone(); changedPin[8] ^= 1;
        Throws<InvalidDataException>(() => SourceAudioFunctionData.Read(changedPin, Hash(payload)), "outer payload pin");
        byte[] badVersion = (byte[])payload.Clone(); badVersion[4] = 9;
        Throws<InvalidDataException>(() => SourceAudioFunctionData.Read(badVersion, Hash(badVersion)), "schema version");
        byte[] truncated = payload.Take(payload.Length - 1).ToArray();
        Throws<InvalidDataException>(() => SourceAudioFunctionData.Read(truncated, Hash(truncated)), "truncated function data");
        byte[] trailing = payload.Concat(new byte[] { 0 }).ToArray();
        Throws<InvalidDataException>(() => SourceAudioFunctionData.Read(trailing, Hash(trailing)), "trailing data");
    }

    private static void Compare(SourceAudioFunctionResult actual, JsonElement expected, SourceCallbackCue cue)
    {
        JsonElement[] draws = expected.GetProperty("random").EnumerateArray().ToArray();
        JsonElement[] calls = expected.GetProperty("calls").EnumerateArray().ToArray();
        Check(expected.GetProperty("stateUnchanged").GetBoolean(), "actual C mutated source state");
        Check(actual.RandomCalls == draws.Length && actual.RandomCalls == expected.GetProperty("rawConsumed").GetInt32(),
            "actual C ordered raw draw count");
        Check(actual.Calls.Count == calls.Length, "actual C subcall count");
        for (int i = 0; i < draws.Length; i++)
        {
            SourceRandomDraw a = actual.RandomDraws[i]; JsonElement e = draws[i];
            Check(a.Raw == e.GetProperty("raw").GetInt32() && a.Value == e.GetProperty("value").GetInt32(),
                "actual C raw/reduced draw");
            Check(a.Site.BoundOrModulus == e.GetProperty("bound").GetInt32() &&
                a.Site.Kind == SourceRandomKind.RandRange, "actual C RNG kind/bound");
            Check(a.Site.SourceIdentity.StartsWith("sounds.c:" + e.GetProperty("line").GetInt32() + ":",
                StringComparison.Ordinal), "actual C RNG callee source line");
            Check(a.Site.CallIndex == 3 + i && a.Site.SourceTick == 100 && a.Site.ActorId == cue.Context.ActorPlayerId &&
                a.Site.ProcessOrder == 7 && a.Site.Process == "RL_PLAYER" && a.Order == i, "provided Core RNG context/order");
        }
        for (int i = 0; i < calls.Length; i++)
        {
            SourceAudioResolvedOriginalSoundCall a = actual.Calls[i]; JsonElement e = calls[i];
            Check(a.SubcallOrder == i && a.PlaylistId == e.GetProperty("id").GetInt32() && a.Bank == "players",
                "actual C ordered bank/playlist");
            Check(a.Volume == e.GetProperty("volume").GetUInt32() && a.Pan == e.GetProperty("pan").GetUInt32() &&
                a.PriorityWord == e.GetProperty("priority").GetUInt32(), "actual C full volume/pan/priority word");
            Check(a.RandomCalls == e.GetProperty("randomCalls").GetInt32(), "actual C RNG/sound interleaving");
            Check(a.CalleeSourceSite.SourceLine == e.GetProperty("line").GetInt32() &&
                a.CalleeSourceSite.FunctionSymbol == cue.FunctionSymbol, "actual C emitted callee source site");
            Check(a.OriginalCue == cue && a.OriginalCueContext.ScriptInstanceId == 91 &&
                a.OriginalCueContext.ProcessOrder == 19, "actual cue context/instance/order retained");
        }
    }

    private static void Lazy(SourceAudioFunctionData data, SourceCallbackCue cue, SourceAudioFunctionState state)
    {
        bool hit = cue.FunctionSymbol == "get_hit_snd";
        bool visible = hit && (state.ActorFlags.Value & data.OnscreenFlag) != 0;
        bool line = visible && state.ActorRawPlayerMode == data.LineMode;
        bool drone = !hit && state.ActorStation == data.DroneStation;
        bool[] needed = { hit, visible, visible, !hit, line || drone,
            line, drone && state.HitPartner.Presence == SourceAudioHitPresence.Present,
            visible, visible && state.ActorPlayerNumber != state.CurrentBallCarrier };
        for (int field = 0; field < needed.Length; field++)
        {
            SourceAudioHitPartner partner = field == 4 ? default(SourceAudioHitPartner) :
                state.HitPartner.Presence == SourceAudioHitPresence.Absent ? SourceAudioHitPartner.Absent :
                SourceAudioHitPartner.Present(field == 5 ? null : state.HitPartner.RawPlayerMode,
                    field == 6 ? null : state.HitPartner.Station);
            var unknown = State(field == 0 ? null : state.ActorFlags, field == 1 ? null : state.ActorRawPlayerMode,
                field == 2 ? null : state.ActorPlayerNumber, field == 3 ? null : state.ActorStation, partner,
                field == 7 ? null : state.CurrentBallCarrier, field == 8 ? null : state.CurrentIntendedReceiver);
            if (needed[field]) Throws<SourceAudioMissingInputException>(() => Select(data, cue, unknown),
                "only C-read field " + field);
            else
            {
                SourceAudioFunctionResult actual = Select(data, cue, unknown);
                SourceAudioFunctionResult known = Select(data, cue, state);
                Check(actual.RandomCalls == known.RandomCalls && actual.Calls.Select(x => x.PlaylistId)
                    .SequenceEqual(known.Calls.Select(x => x.PlaylistId)), "unread unknown field must not change selection");
            }
            lazyChecks++;
        }
    }

    private static void Oracle(SourceAudioFunctionData data, string casesPath, string oraclePath)
    {
        string[] input = File.ReadAllLines(casesPath), oracle = File.ReadAllLines(oraclePath);
        Check(input.Length == oracle.Length, "C oracle case inventory");
        for (int n = 0; n < input.Length; n++)
        {
            int[] v = input[n].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
            SourceAudioVmBinding binding = data.VmBindings[v[15]];
            SourceCallbackCue cue = Cue(binding, v[4]);
            var state = State(v[2], v[3], v[4], v[5], v[6] == 0 ? SourceAudioHitPartner.Absent :
                SourceAudioHitPartner.Present(v[7], v[8]), v[9], v[10]);
            int[] raw = v.Skip(16).ToArray();
            SourceAudioFunctionResult actual = Succeed(() => Select(data, cue, state, raw), "actual C-valid invocation " + n);
            using (JsonDocument expected = JsonDocument.Parse(oracle[n]))
            {
                Check(expected.RootElement.GetProperty("id").GetInt32() == v[0], "C oracle case ID");
                Compare(actual, expected.RootElement, cue);
            }
            if (n % 29 == 0) Lazy(data, cue, state);
            rawDraws += actual.RandomCalls; resolvedCalls += actual.Calls.Count; cases++;
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(args[0]);
            SourceAudioFunctionData data = SourceAudioFunctionData.Read(bytes, args[1]);
            Boundaries(data, bytes);
            if (args.Length == 4) Oracle(data, args[2], args[3]);
            Console.WriteLine(JsonSerializer.Serialize(new { checks, cases, lazyChecks, rawDraws, resolvedCalls }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("AUDIO_FUNCTION_ASSERTION: " + error);
            return 1;
        }
    }
}
