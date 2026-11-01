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
using Blitz.Runtime;
using Blitz.SourcePlayer;

internal static partial class Program
{
    private sealed class PcmSink : ISourceAudioSink
    {
        internal readonly List<byte[]> Samples = new List<byte[]>();
        internal readonly List<JsonElement> Commands = new List<JsonElement>();
        internal bool Throw;
        public void AudioSample(string id, int rate, int channels, byte[] pcm, string metadata)
        {
            using (JsonDocument doc = JsonDocument.Parse(metadata))
            {
                string hash = PcmHash(pcm);
                Check(rate == 31250 && channels == 1 && id == "pcm16-" + hash &&
                    doc.RootElement.GetProperty("sha256").GetString() == hash &&
                    doc.RootElement.GetProperty("mixAlreadyBaked").GetBoolean(), "actual PCM upload format/hash/baked mix");
                Samples.Add((byte[])pcm.Clone());
            }
        }
        public void AudioCommands(string json)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement batch = doc.RootElement;
                Check(batch.GetProperty("schema").GetString() == "source-audio-playback/v1" &&
                    batch.GetProperty("sourceTicksPerSecond").GetInt32() == 57, "serialized source audio clock/contract");
                foreach (JsonElement command in batch.GetProperty("commands").EnumerateArray())
                    Commands.Add(command.Clone());
            }
            if (Throw) throw new InvalidOperationException("fixture sink partially consumed the batch");
        }
        internal JsonElement[] Kind(string kind)
        { return Commands.Where(c => c.GetProperty("kind").GetString() == kind).ToArray(); }
    }

    private static string PcmHash(byte[] bytes)
    {
        using (SHA256 hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static byte[] PcmBank(string name = "players", int endWait = 5, byte repeat = 1, params int[] ids)
    {
        if (ids.Length == 0) ids = new[] { 1, 2 };
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
        {
            writer.Write(new byte[] { 83, 65, 85, 68, 1, 0, 0, 0 });
            writer.Write((byte)name.Length); writer.Write(Encoding.ASCII.GetBytes(name));
            writer.Write(new byte[20]); writer.Write(new byte[32]);
            writer.Write((ushort)200); writer.Write((ushort)ids.Length);
            foreach (int id in ids)
            {
                writer.Write((ushort)id);
                uint offset = (uint)(1000 + id * 18), sample = 100000;
                uint relative = sample - offset - 11;
                writer.Write(offset); writer.Write((ushort)18);
                writer.Write(new byte[] { 1, 1, 0, 0, 7, 0, 90, 0, 0, 1, 0,
                    (byte)(relative >> 16), (byte)(relative >> 8), (byte)relative, repeat,
                    (byte)(endWait >> 8), (byte)endWait, 0 });
                writer.Write(sample); writer.Write((ushort)4);
                var pcm = new byte[6 * 240 * 2];
                for (int i = 0; i < pcm.Length / 2; i++)
                {
                    short value = (short)(i == 0 ? -32768 : i == 1 ? 32767 : i - 720);
                    pcm[i * 2] = (byte)value; pcm[i * 2 + 1] = (byte)(value >> 8);
                }
                writer.Write((uint)pcm.Length);
                using (SHA256 hash = SHA256.Create()) writer.Write(hash.ComputeHash(pcm));
                writer.Write(pcm);
            }
            writer.Flush(); return stream.ToArray();
        }
    }

    private static SourceAudioBankInput PcmInput(byte[] bytes, string name = "players")
    { return new SourceAudioBankInput(bytes, PcmHash(bytes), name, new string('0', 40), new string('0', 64)); }

    private static SourceAudioPlaybackAdmission PcmAdmission(uint reserved = 0)
    {
        return new SourceAudioPlaybackAdmission("generated-non-original-fixture", "test generator; not original-bank authorization",
            new[] { PcmInput(PcmBank()), PcmInput(PcmBank("cursors", 5, 1, 100), "cursors") },
            new[] { new SourceAudioProgramRequirement("players", 1), new SourceAudioProgramRequirement("players", 2),
                new SourceAudioProgramRequirement("cursors", 100) }, reserved);
    }

    private static SourceSoundVmDispatch PcmDispatch(long output, long tick = 1, uint priority = 15,
        string bank = "players", int id = 1, uint volume = 160, uint pan = 127, bool second = false)
    {
        SourceCallbackCue cue = Cue(tick, (int)output);
        var calls = new List<SourceAudioResolvedOriginalSoundCall> {
            new SourceAudioResolvedOriginalSoundCall(cue,
                new SourceAudioCalleeSite(new string('0', 40), "generated_fixture", 1),
                0, bank, id, volume, pan, priority, 0)
        };
        if (second) calls.Add(new SourceAudioResolvedOriginalSoundCall(cue, calls[0].CalleeSourceSite,
            1, bank, 2, uint.MaxValue, uint.MaxValue, 15, 0));
        return new SourceSoundVmDispatch(cue,
            new SourceRandomContext(tick, cue.Context.ActorPlayerId, "fixture_actor", (int)output, "fixture", 0),
            default(SourceAudioFunctionState), SourceSoundVmStatus.Resolved,
            new SourceAudioFunctionResult(calls, new List<SourceRandomDraw>()), null, output);
    }

    private static SourceSoundVmDispatch PcmNative(long output)
    {
        var context = new SourceC2SoundContext(SourceC2SoundFunctionKind.WhistleProc, 1, "whistle_proc", 3, -1,
            "player.c", "whistle_proc", 6018, 0, 0, 0, null);
        var calls = new List<SourceAudioResolvedOriginalSoundCall> {
            new SourceAudioResolvedOriginalSoundCall(context,
                new SourceAudioCalleeSite("player.c", new string('0', 40), "whistle_proc", 6018),
                0, "players", 2, 235, 0, 0xe281, 0)
        };
        return new SourceSoundVmDispatch(context, new SourceRandomContext(1, -1, "whistle_proc", 3, "fixture", 0),
            default(SourceC2SoundState), null, SourceSoundVmStatus.Resolved,
            new SourceAudioFunctionResult(calls, new List<SourceRandomDraw>()), null, output);
    }

    private static string PcmReject<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T error) { Check(true, message); return error.Message; }
        Check(false, message); return null;
    }

    private static void PlaybackTransport()
    {
        var sink = new PcmSink();
        var playback = new SourceAudioPlayback(PcmAdmission(35), sink, 57);
        playback.Flush();
        Check(sink.Samples.Count == 1 && sink.Samples[0].Length == 2880 &&
            sink.Samples[0][0] == 0 && sink.Samples[0][1] == 128 &&
            sink.Samples[0][2] == 255 && sink.Samples[0][3] == 127,
            "decoded sample bytes are emitted once, not per call/frame or a silent substitute");
        var transport = new SourceSoundVmTransport();
        transport.BindOutput(playback);
        SourceSoundVmDispatch vm = PcmDispatch(1, second: true), native = PcmNative(3);
        transport.Drain(Batch(vm, native));
        Reject(() => transport.DeliverNextNative(native), "native delivery cannot jump over an actual cue/direct boundary");
        transport.DeliverResolved(transport.TakeForCue(vm.OriginalCue));
        var direct = new SourceDirectSoundCall(1, 1, 1, "fixture_actor", 2, 4, 0, TeamId.Home,
            "player.c", "audible_check", 1, "cursors", 100, 235, 0, 15, 0, 2);
        playback.Consume(direct);
        transport.DeliverNextNative(native);
        transport.CompletePresentationBatch();
        playback.Flush(); playback.Flush();
        JsonElement[] plays = sink.Kind("play");
        Check(plays.Select(p => p.GetProperty("outputSequence").GetInt64()).SequenceEqual(new long[] { 1, 1, 2, 3 }) &&
            plays.Select(p => p.GetProperty("subcallOrder").GetInt32()).SequenceEqual(new[] { 0, 1, 0, 0 }),
            "actual transport, direct boundary and native dispatch reach output in original subcall order");
        Check(plays.Select(p => p.GetProperty("track").GetInt32()).SequenceEqual(new[] { 2, 3, 4, 5 }),
            "dynamic allocation excludes source reserved mask; explicit track5 can use its reservation");
        Check(plays.All(p => p.GetProperty("atSample").GetInt64() == 31250 / 57 &&
            p.GetProperty("sampleCount").GetInt32() == 1440 && !p.GetProperty("loop").GetBoolean()),
            "source tick scheduling retains the exact decoded tail and once-only program");
        Check(plays[0].GetProperty("portableGain").GetDouble() == 160 / 255.0 &&
            plays[0].GetProperty("portablePan").GetDouble() == 0 &&
            plays[1].GetProperty("rawVolume").GetUInt32() == uint.MaxValue &&
            plays[1].GetProperty("volumeByte").GetInt32() == 255 &&
            plays[1].GetProperty("portableGain").GetDouble() == 1 &&
            plays[1].GetProperty("portablePan").GetDouble() == 1 &&
            plays[2].GetProperty("portablePan").GetDouble() == -1, "raw source words/clamps and explicit portable projection");
        Check(vm.Result.RandomCalls == 0 && vm.Result.Calls.Count == 2 &&
            vm.Result.Calls[1].Volume == uint.MaxValue && sink.Samples.Count == 1 && playback.PendingCount == 0,
            "playback does not reselect, draw RNG, mutate raw operands or replay on a render-only flush");
        Reject(() => playback.Consume(direct), "duplicate common producer ordinal cannot replay PCM");
        Check(sink.Kind("play").Length == 4 && playback.Failure.Contains("SOURCE_AUDIO_ORDER"),
            "duplicate faults stop output rather than repeating a selected sound");
    }

    private static void PlaybackAdmissionFailures()
    {
        SourceAudioBankInput input = PcmInput(PcmBank());
        string missing = PcmReject<InvalidDataException>(() => new SourceAudioPlaybackAdmission("fixture", "generated",
            new[] { input }, new[] { new SourceAudioProgramRequirement("generic", 1) }), "missing bank preflight");
        Check(missing.Contains("generic:1"), "preflight names the exact missing bank/program");
        string absent = PcmReject<NotSupportedException>(() => new SourceAudioPlaybackAdmission("fixture", "generated",
            new[] { input }, new[] { new SourceAudioProgramRequirement("players", 127) }), "unprepared program preflight");
        Check(absent.Contains("players:127"), "preflight never invents or substitutes an unprepared program");
        string wait = PcmReject<NotSupportedException>(() => new SourceAudioPlaybackAdmission("fixture", "generated",
            new[] { PcmInput(PcmBank(endWait: 1)) }, new[] { new SourceAudioProgramRequirement("players", 1) }),
            "unsupported original end timing");
        Check(wait.Contains("endWait=1") && wait.Contains("program="), "unqualified timing retains exact program evidence");
        PcmReject<InvalidDataException>(() => PcmInput(PcmBank(repeat: 0)), "repeat0 is not a one-shot or an invented PCM loop");
        PcmReject<InvalidDataException>(() => PcmInput(PcmBank(repeat: 2)), "finite compressed repeats need their actual interpreter");
        PcmReject<InvalidDataException>(() => new SourceAudioBankInput(PcmBank(), PcmHash(PcmBank()), "players",
            new string('1', 40), new string('0', 64)), "package integrity does not bypass source-bank identity");

        var sink = new PcmSink();
        var playback = new SourceAudioPlayback(PcmAdmission(), sink, 57);
        Reject(() => playback.ConsumeResolved(PcmDispatch(1, id: 127)), "unadmitted actual source tuple fails");
        Check(playback.Failure.Contains("players:127") && sink.Kind("play").Length == 0 &&
            sink.Kind("stop-all").Length == 1 && !sink.Kind("active").Last().GetProperty("active").GetBoolean(),
            "missing actual input faults and stops the receiver before fake playback success");
        Reject(() => playback.ConsumeResolved(PcmDispatch(2)), "missing-input fault is sticky, never retry/reselection");
        playback.Reset(1);
        Reject(() => playback.ConsumeResolved(PcmDispatch(2, priority: 0x1000)), "unknown priority flags fail instead of being ignored");
        Check(playback.Failure.Contains("SOURCE_AUDIO_PRIORITY_UNSUPPORTED"), "unsupported priority word surfaced");
        playback.Reset(2);
        SourceSoundVmDispatch unsupported = Dispatch(Cue(1, 1), status: SourceSoundVmStatus.Unsupported);
        Reject(() => playback.ValidateDispatch(unsupported), "unsupported selector is not resolved silence");
        Check(playback.Failure.Contains("plyrseq.c:2448/get_hit_snd(57,0,0)"), "unsupported selector reports its real source tuple");
        playback.Reset(3);
        Reject(() => playback.RejectUnordered("timedown_play_select", 1, "players", 56),
            "legacy sound without a shared producer ordinal cannot invent ordering");
        Check(playback.Failure.Contains("players:56") && sink.Kind("play").Length == 0, "unordered missing producer surfaced by exact tuple");
    }

    private static void PlaybackLifecycle()
    {
        var sink = new PcmSink();
        var playback = new SourceAudioPlayback(PcmAdmission(35), sink, 57);
        playback.ConsumeResolved(PcmDispatch(1, priority: 1));
        playback.ConsumeResolved(PcmDispatch(2, priority: 5));
        playback.ConsumeResolved(PcmDispatch(3, priority: 10));
        playback.ConsumeResolved(PcmDispatch(4, priority: 15));
        playback.ConsumeResolved(PcmDispatch(5, priority: 0));
        playback.ConsumeResolved(PcmDispatch(6, priority: 0xe280)); // forced track5, equal priority is protected
        playback.ConsumeResolved(PcmDispatch(7, priority: 0xe280));
        playback.SetTrackVolume(1u << 5, 128); playback.SetTrackPan(1u << 5, 255);
        playback.Flush();
        JsonElement[] rejected = sink.Kind("rejected"), stops = sink.Kind("stop"), plays = sink.Kind("play");
        Check(rejected.Length == 2 && rejected.All(c => c.GetProperty("reason").GetString() == "track-priority") &&
            rejected.Select(c => c.GetProperty("outputSequence").GetInt64()).SequenceEqual(new long[] { 4, 7 }),
            "lower-priority and equal NOINT calls are explicitly rejected, not silently counted as played");
        Check(stops.Length == 1 && stops[0].GetProperty("track").GetInt32() == 4 &&
            stops[0].GetProperty("reason").GetString() == "preempted" &&
            plays.Last().GetProperty("track").GetInt32() == 5, "higher source priority preempts the worst eligible track");
        Check(plays.Last().GetProperty("volumeByte").GetInt32() == 160 &&
            sink.Kind("adjust").Last().GetProperty("volumeByte").GetInt32() == 128 &&
            sink.Kind("adjust").Last().GetProperty("portablePan").GetDouble() == 1,
            "queued play operands are immutable when later source volume/pan adjustments follow");
        playback.ConsumeResolved(PcmDispatch(8, priority: 0x8294));
        playback.Flush();
        Check(sink.Kind("play").Last().GetProperty("priorityWord").GetUInt32() == 0x8294 &&
            sink.Kind("stop").Last().GetProperty("track").GetInt32() == 5,
            "SND_PRI_SET overrides the explicit track even with a weaker incoming priority");
        playback.StopTracks(1u << 2); playback.Flush();
        Check(sink.Kind("stop").Last().GetProperty("track").GetInt32() == 2 &&
            sink.Kind("stop").Last().GetProperty("reason").GetString() == "source-stop-track",
            "source stop mask targets the exact active track/voice");
        int stopCount = sink.Kind("stop").Length;
        playback.DeleteBank("players"); playback.Flush();
        Check(sink.Kind("stop").Length == stopCount && sink.Kind("bank-delete").Length == 1,
            "Goose bank deletion only removes the mapping; it does not stop existing voices");
        playback.LoadBank("players");
        playback.SetActive(false); playback.SetActive(false); playback.Flush();
        Check(sink.Kind("active").Length == 1 && playback.SourceTick == 1, "duplicate pause/focus does not duplicate voices or advance source time");
        playback.SetActive(true); playback.AdvanceTo(4); playback.Flush();
        JsonElement[] completed = sink.Kind("stop").Where(c => c.GetProperty("reason").GetString() == "completed").ToArray();
        Check(completed.Length == 3 && completed.All(c => c.GetProperty("atSample").GetInt64() == 31250 / 57 + 1440),
            "natural release uses exact sample duration including decoder tail, not rounded source wakes");
        playback.StopAll(); playback.Flush();
        Check(playback.ReservedTracks == 0, "source stop-all resets reservations");
        playback.ConsumeResolved(PcmDispatch(9, tick: 4));
        int commandCount = sink.Commands.Count;
        playback.Reset(1, false); playback.Flush();
        Check(sink.Commands.Skip(commandCount).All(c => c.GetProperty("kind").GetString() != "play") &&
            sink.Commands.Skip(commandCount).First().GetProperty("kind").GetString() == "reset" &&
            !sink.Kind("reset").Last().GetProperty("active").GetBoolean() &&
            sink.Samples.Count == 1 && playback.PendingCount == 0,
            "new-match epoch discards unsent old plays, stops old voices and reuses uploaded PCM while paused");
        playback.SetActive(true);
        playback.ConsumeResolved(PcmDispatch(10));
        sink.Throw = true;
        Reject(playback.Flush, "partial receiver failure is sticky");
        int playsBefore = sink.Kind("play").Length;
        Reject(playback.Flush, "receiver partial delivery is not retried");
        Check(sink.Kind("play").Length == playsBefore && playback.Failure.Contains("SOURCE_AUDIO_SINK"),
            "a failing real sink cannot duplicate an already consumed batch");
        sink.Throw = false;
        playback.Reset(2); playback.Flush();
        Check(playback.Failure == null && sink.Kind("play").Length == playsBefore && sink.Samples.Count == 1,
            "explicit restart recovers output without stale playback or resource reupload");
    }

    private static void PlaybackProducerReset()
    {
        var sink = new PcmSink();
        var playback = new SourceAudioPlayback(PcmAdmission(), sink, 57);
        playback.ConsumeResolved(PcmDispatch(40));
        playback.Flush();
        JsonElement original = sink.Kind("play").Last();
        int uploads = sink.Samples.Count;

        playback.Reset(1);
        Reject(() => playback.ConsumeResolved(PcmDispatch(1)),
            "same-producer restart must retain its monotonic output ordinal guard");
        Check(playback.Failure.Contains("SOURCE_AUDIO_ORDER"), "same-producer ordinal replay faults explicitly");

        playback.Reset(2, false, newProducer: true);
        playback.Flush();
        Check(!playback.Active && !sink.Kind("reset").Last().GetProperty("active").GetBoolean(),
            "fresh-producer reset preserves the requested paused receiver state");
        playback.SetActive(true);
        playback.ConsumeResolved(PcmDispatch(1));
        playback.Flush();
        JsonElement fresh = sink.Kind("play").Last();
        Check(playback.Failure == null && fresh.GetProperty("outputSequence").GetInt64() == 1 &&
            fresh.GetProperty("epoch").GetInt64() == 2 &&
            fresh.GetProperty("voiceId").GetInt64() > original.GetProperty("voiceId").GetInt64() &&
            fresh.GetProperty("sequence").GetInt64() > original.GetProperty("sequence").GetInt64() &&
            sink.Samples.Count == uploads,
            "fresh producer accepts ordinal1 without rewinding receiver identity or uploading PCM again");

        Reject(() => playback.Reset(2, newProducer: true), "fresh producer cannot reuse the current host epoch");
        Reject(() => playback.Reset(1, newProducer: true), "fresh producer cannot rewind the host epoch");
        Reject(() => playback.ConsumeResolved(PcmDispatch(1)),
            "rejected epoch resets must not erase the fresh producer duplicate guard");
        playback.Reset(3);
        playback.ConsumeResolved(PcmDispatch(2));
        playback.Flush();
        Check(playback.Failure == null && sink.Kind("play").Last().GetProperty("outputSequence").GetInt64() == 2,
            "later same-producer rematches continue the new producer ordinal without stale replay");
    }
}
