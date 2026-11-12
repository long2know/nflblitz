#if BLITZ_NATIVE_AUDIO_API
#nullable disable
#endif
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Runtime;
using UnityEngine;

namespace Blitz.Presentation
{
    public interface ISourceNativeSoundOutput : ISourceSoundVmOutputConsumer, ISourceDirectSoundOutputConsumer
    {
        string Failure { get; }
        void Consume(SourceProcessSoundCall call);
        void Consume(SourceProcessSoundControl call);
        void Consume(SourceBootSoundOutput call);
        void AdvanceTo(long sourceTick);
        void SetActive(bool active);
        void Reset(long epoch);
    }

    public sealed class SourceNativePcmReceipt
    {
        public string Kind { get; }
        public long OutputSequence { get; }
        public long AtSample { get; }
        public string Bank { get; }
        public int PlaylistId { get; }
        public int Track { get; }
        public uint Volume { get; }
        public uint Pan { get; }
        public uint PriorityWord { get; }

        internal SourceNativePcmReceipt(string kind, long output, long at, string bank, int id,
            int track, uint volume, uint pan, uint priority)
        {
            Kind = kind; OutputSequence = output; AtSample = at; Bank = bank; PlaylistId = id;
            Track = track; Volume = volume; Pan = pan; PriorityWord = priority;
        }
    }

    // The portable six-track profile renders admitted decoded programmes, not arcade DSP firmware.
    public sealed class SourceNativePcmMixer : ISourceNativeSoundOutput
    {
        public const string Profile = "portable-unity-pcm-six-track/v1";
        private sealed class Command
        {
            internal string Kind, Bank;
            internal long At, Output;
            internal int Id, Track, Priority;
            internal uint Volume, Pan, Word, Mask, Value;
            internal bool CheckPriority, NoEqual;
            internal SourceAudioPlaylist Program;
        }
        private sealed class Voice
        {
            internal SourceAudioPlaylist Program;
            internal SourceAudioPcm Pcm;
            internal Command Call;
            internal int Volume;
            internal float Left, Right;
        }
        private readonly object gate = new object();
        private readonly Dictionary<string, SourceAudioBank> banks = new Dictionary<string, SourceAudioBank>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<Command> commands = new Queue<Command>();
        private readonly List<SourceNativePcmReceipt> receipts = new List<SourceNativePcmReceipt>();
        private readonly Voice[] voices = new Voice[6];
        private readonly string[] initialBanks;
        private readonly uint initialReserved;
        private readonly long leadSamples;
        private uint reserved;
        private long sourceTick, lastOutput, cursor, origin, epoch = -1;
        private int bootOrder;
        private bool started, active = true;
        private string failure;
        public bool NativeDspEquivalent { get { return false; } }
        public string AuthorizationReference { get; }
        public string Failure { get { lock (gate) return failure; } }
        public long RenderedSampleFrames { get { lock (gate) return cursor; } }
        public long NonzeroSampleFrames { get; private set; }
        public long PlayedCalls { get; private set; }
        public long RejectedCalls { get; private set; }
        public ReadOnlyCollection<SourceNativePcmReceipt> Receipts
        { get { lock (gate) return Array.AsReadOnly(receipts.ToArray()); } }

        public SourceNativePcmMixer(IReadOnlyList<SourceAudioBank> admittedBanks, string authorizationReference,
            IReadOnlyList<string> initiallyLoadedBanks, uint reservedTracks, int latencySourceTicks = 12)
        {
            if (string.IsNullOrWhiteSpace(authorizationReference) || admittedBanks == null ||
                admittedBanks.Count == 0 || admittedBanks.Count > 64 || initiallyLoadedBanks == null ||
                (reservedTracks & ~63u) != 0 || latencySourceTicks < 2 || latencySourceTicks > 114)
                throw new ArgumentException("Native portable PCM requires explicit authorized banks, known lifecycle and bounded source-clock latency.");
            long bytes = 0;
            var samples = new HashSet<string>(StringComparer.Ordinal);
            foreach (SourceAudioBank bank in admittedBanks)
            {
                if (bank == null || banks.ContainsKey(bank.BankName))
                    throw new ArgumentException("Native admitted banks must be nonnull and unique.");
                banks.Add(bank.BankName, bank);
                foreach (SourceAudioPlaylist program in bank.Playlists)
                {
                    if (!program.CompiledPlaybackQualified && !program.IsolatedPlaybackQualified)
                        throw new NotSupportedException("Native PCM cannot execute an unqualified original playlist.");
                    if (samples.Add(program.Pcm.Sha256)) bytes += (long)program.Pcm.SampleCount * 2;
                    if (program.GlobalMixer != null && samples.Add(program.GlobalMixer.PrimaryZeroPcm.Sha256))
                        bytes += (long)program.GlobalMixer.PrimaryZeroPcm.SampleCount * 2;
                }
            }
            if (bytes > 128L * 1024 * 1024) throw new ArgumentException("Native admitted PCM exceeds its bounded decoded profile.");
            AuthorizationReference = authorizationReference; initialReserved = reservedTracks;
            initialBanks = new string[initiallyLoadedBanks.Count];
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < initialBanks.Length; i++)
            {
                string bank = initiallyLoadedBanks[i];
                if (bank == null || !banks.ContainsKey(bank) || !unique.Add(bank))
                    throw new ArgumentException("Initial native bank state must identify actual unique admitted banks.");
                initialBanks[i] = bank;
            }
            leadSamples = At(latencySourceTicks);
            Reset(0);
        }

        public void AdvanceTo(long tick)
        {
            lock (gate)
            {
                Healthy();
                if (tick < sourceTick) Fail("Native source audio time cannot rewind.");
                if (!started) { origin = checked(cursor + leadSamples - At(tick)); started = true; }
                sourceTick = tick;
            }
        }

        public void SetActive(bool value) { lock (gate) { if (value) Healthy(); active = value; } }
        public void Reset(long nextEpoch)
        {
            lock (gate)
            {
                if (nextEpoch <= epoch) throw new InvalidOperationException("Native PCM reset requires a fresh epoch.");
                epoch = nextEpoch; failure = null; commands.Clear(); Array.Clear(voices, 0, voices.Length);
                loaded.Clear(); foreach (string bank in initialBanks) loaded.Add(bank);
                reserved = initialReserved; sourceTick = 0; cursor = 0; started = false; active = true; bootOrder = 0;
            }
        }

        public void ConsumeResolved(SourceSoundVmDispatch dispatch)
        {
            lock (gate)
            {
                Healthy();
                if (dispatch == null || dispatch.Status != SourceSoundVmStatus.Resolved || dispatch.Result == null)
                    throw new ArgumentException("Native PCM accepts only the actual resolved VM record.");
                var pending = new List<Command>();
                foreach (SourceAudioResolvedOriginalSoundCall call in dispatch.Result.Calls)
                    pending.Add(Play(call.Bank, call.PlaylistId, call.Volume, call.Pan, call.PriorityWord));
                Accept(dispatch.OutputSequence, dispatch.RandomContext.SourceTick);
                foreach (Command command in pending) Queue(command);
            }
        }
        public void Consume(SourceDirectSoundCall call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            Submit(call.OutputSequence, call.SourceTick, call.Bank, call.PlaylistId, call.Volume, call.Pan, call.PriorityWord);
        }
        public void Consume(SourceProcessSoundCall call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            Submit(call.OutputSequence, call.SourceTick, call.Bank, call.PlaylistId, call.Volume, call.Pan, call.PriorityWord);
        }
        private void Submit(long output, long tick, string bank, int id, uint volume, uint pan, uint priority)
        {
            lock (gate)
            {
                Healthy(); Command command = Play(bank, id, volume, pan, priority);
                Accept(output, tick); Queue(command);
            }
        }
        public void Consume(SourceBootSoundOutput call)
        {
            lock (gate)
            {
                Healthy();
                if (call == null || call.SourceTick != 0 || call.BootOrder != bootOrder ||
                    call.EntryKind != SourceC2EntryKind.CompleteOriginalVersusSoundBanks)
                    throw new ArgumentException("Native boot audio requires its exact source0 versus FIFO.");
                if (call.Kind == SourceBootSoundOutputKind.Control)
                {
                    Accept(call.OutputSequence, 0); Control(call.ControlKind.Value, call.Bank, call.TrackMask, call.Value);
                }
                else
                {
                    Command command = Play(call.Bank, call.PlaylistId.Value, call.Volume, call.Pan, call.PriorityWord);
                    Accept(call.OutputSequence, 0); Queue(command);
                }
                bootOrder++;
            }
        }
        public void Consume(SourceProcessSoundControl call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            lock (gate) { Healthy(); Accept(call.OutputSequence, call.SourceTick); Control(call.Kind, call.Bank, call.TrackMask, call.Value); }
        }

        private void Control(SourceProcessSoundControlKind kind, string bank, uint mask, uint value)
        {
            if (kind == SourceProcessSoundControlKind.LoadBank || kind == SourceProcessSoundControlKind.DeleteBank)
            {
                if (bank == null || !banks.ContainsKey(bank)) Fail("Native source bank was not admitted: " + bank);
                bool changed = kind == SourceProcessSoundControlKind.LoadBank ? loaded.Add(bank) : loaded.Remove(bank);
                Record(changed ? kind.ToString() : "bank-operation-rejected", null, -1);
                return; // Original bank deletion does not stop already running DSP voices.
            }
            if ((mask & ~63u) != 0 || ((kind == SourceProcessSoundControlKind.TrackVolume ||
                kind == SourceProcessSoundControlKind.TrackPan) && value > 255))
                Fail("Native track controls require actual six-track masks and byte parameters.");
            Queue(new Command { Kind = kind.ToString(), Mask = mask, Value = value });
        }
        private Command Play(string bank, int id, uint volume, uint pan, uint word)
        {
            SourceAudioBank admitted = null;
            if (bank == null || !banks.TryGetValue(bank, out admitted) || !loaded.Contains(bank))
                Fail("Native source bank is not admitted/loaded: " + bank + ":" + id);
            uint flags = word & 0xe000;
            int track = flags == 0 ? -1 : (int)((word >> 7) & 7);
            if ((word & ~0xe3ffu) != 0 || flags != 0 && flags != 0x8000 && flags != 0xe000 ||
                flags == 0 && (word & 0x380) != 0 || track >= 6)
                Fail("Native PCM does not implement this original priority word: " + word);
            SourceAudioPlaylist program = SourceAudioSparseSlots.IsUnassigned(admitted, id) ? null : admitted.GetPlaylist(id);
            return new Command { Kind = program == null ? "unassigned-playlist" : "play",
                Bank = bank, Id = id, Program = program, Volume = volume, Pan = pan, Word = word,
                Track = track, Priority = (int)(word & 127), CheckPriority = flags != 0x8000, NoEqual = flags == 0xe000 };
        }
        private void Accept(long output, long tick)
        {
            if (output <= lastOutput || output <= 0) Fail("Native source output was duplicated or reordered.");
            AdvanceTo(tick); lastOutput = output;
        }
        private void Queue(Command command)
        {
            long at = checked(origin + At(sourceTick));
            if (!active || at < cursor) Fail("Native source PCM missed its render frontier or was submitted while suspended.");
            if (commands.Count >= 2048) Fail("Native source PCM command queue was not consumed.");
            command.At = at; command.Output = lastOutput; commands.Enqueue(command);
        }

        public void ReadStereo(float[] destination)
        {
            if (destination == null || destination.Length % 2 != 0)
                throw new ArgumentException("Native PCM requires complete stereo frames.", nameof(destination));
            lock (gate)
            {
                Healthy(); Array.Clear(destination, 0, destination.Length);
                if (!active || !started) return;
                for (int frame = 0; frame < destination.Length / 2; frame++, cursor++)
                {
                    for (int track = 0; track < voices.Length; track++)
                        if (voices[track] != null && voices[track].Program.LoopEndSample == 0 &&
                            cursor - voices[track].Call.At >= voices[track].Pcm.SampleCount) voices[track] = null;
                    while (commands.Count != 0 && commands.Peek().At <= cursor) Apply(commands.Dequeue());
                    double musicGain = MusicGain();
                    float left = 0, right = 0;
                    for (int track = 0; track < voices.Length; track++)
                    {
                        Voice voice = voices[track];
                        if (voice == null) continue;
                        long sample = cursor - voice.Call.At;
                        if (voice.Program.LoopEndSample > 0 && sample >= voice.Program.LoopEndSample)
                            sample = voice.Program.LoopStartSample + (sample - voice.Program.LoopStartSample) %
                                (voice.Program.LoopEndSample - voice.Program.LoopStartSample);
                        if (sample < 0 || sample >= voice.Pcm.SampleCount) Fail("Native PCM escaped its actual decoded programme.");
                        float value = voice.Pcm[(int)sample] / 32768f *
                            (float)(voice.Volume / 255.0 * (track == 0 ? musicGain : 1));
                        left += value * voice.Left; right += value * voice.Right;
                    }
                    destination[frame * 2] = left; destination[frame * 2 + 1] = right;
                    if (left != 0 || right != 0) NonzeroSampleFrames++;
                }
            }
        }
        private void Apply(Command command)
        {
            if (command.Kind == "unassigned-playlist") { RejectedCalls++; Record(command.Kind, command, -1); return; }
            if (command.Kind != "play")
            {
                if (command.Kind == "ReservedTracks") reserved = command.Mask;
                else if (command.Kind == "StopAll") { Array.Clear(voices, 0, voices.Length); reserved = 0; }
                else if (command.Kind == "StopTracks" || command.Kind == "TrackVolume" || command.Kind == "TrackPan")
                {
                    for (int track = 0; track < voices.Length; track++)
                        if ((command.Mask & (1u << track)) != 0 && voices[track] != null)
                        {
                            if (command.Kind == "StopTracks") voices[track] = null;
                            else if (command.Kind == "TrackVolume") voices[track].Volume = (int)command.Value;
                            else Pan(voices[track], (int)command.Value);
                        }
                }
                else Fail("Unimplemented native source track control: " + command.Kind);
                Record(command.Kind, command, -1); return;
            }
            int selected = command.Track;
            if (selected < 0)
                for (int track = 0; track < voices.Length; track++)
                {
                    if ((reserved & (1u << track)) != 0) continue;
                    if (voices[track] == null) { selected = track; break; }
                    if (selected < 0 || voices[track].Call.Priority > voices[selected].Call.Priority) selected = track;
                }
            if (selected < 0 || command.CheckPriority && voices[selected] != null &&
                (command.Priority > voices[selected].Call.Priority || command.NoEqual && command.Priority == voices[selected].Call.Priority))
            { RejectedCalls++; Record("priority-rejected", command, selected); return; }
            Voice voice = new Voice { Call = command, Program = command.Program,
                Pcm = selected == 0 && command.Program.GlobalMixer != null ? command.Program.GlobalMixer.PrimaryZeroPcm : command.Program.Pcm,
                Volume = (int)Math.Min(command.Volume, 255u) };
            Pan(voice, (int)Math.Min(command.Pan, 255u)); voices[selected] = voice; PlayedCalls++; Record("play", command, selected);
        }
        private static void Pan(Voice voice, int pan)
        {
            double normalized = (pan - 127) / (pan <= 127 ? 127.0 : 128.0);
            double angle = (normalized + 1) * Math.PI / 4;
            voice.Left = (float)Math.Cos(angle); voice.Right = (float)Math.Sin(angle);
        }
        private double MusicGain()
        {
            Voice music = voices[0];
            if (music == null) return 1;
            int baseline = music.Program.GlobalMixer != null ?
                music.Program.GlobalMixer.PrimaryZeroLevelAt(cursor - music.Call.At) : music.Program.ConstantOwnMixingLevel ?? 0;
            int sum = baseline;
            for (int track = 1; track < voices.Length; track++)
                if (voices[track] != null && voices[track].Program.GlobalMixer != null)
                {
                    int delta = voices[track].Program.GlobalMixer.GlobalLevelAt(cursor - voices[track].Call.At);
                    if (delta != 0 && !music.Program.ConstantOwnMixingLevel.HasValue && music.Program.GlobalMixer == null)
                        Fail("Native global mixing requires the original qualified primary timeline.");
                    sum = checked(sum + delta);
                }
            if (sum < short.MinValue || sum > short.MaxValue) Fail("Native global mixing overflow is outside the qualified profile.");
            int original = MixingMultiplier(baseline, music.Volume), mixed = MixingMultiplier(sum, music.Volume);
            if (original == 0 && mixed != 0) Fail("Native global mixing cannot reconstruct an audible flattened silent base.");
            double gain = original == 0 ? 1 : (double)mixed / original;
            double projected = music.Volume / 255.0 * gain;
            if (projected < 0 || projected > 1 || double.IsNaN(projected) || double.IsInfinity(projected))
                Fail("Native global mixing exceeds its qualified duck/restore range.");
            return gain;
        }
        private static int MixingMultiplier(int level, int volume)
        {
            level = Math.Max(-8191, Math.Min(8191, level));
            int exponent = (((level >> 6) & 0x3ff) + 0x80) & 0xffff;
            uint multiplier = (uint)volume << 7, product = 0x7c94;
            for (int bit = 1; bit <= 128; bit <<= 1)
            {
                if ((exponent & bit) == 0) multiplier = (multiplier * product >> 15) & 0xffff;
                product = (product * product >> 15) & 0xffff;
            }
            return (int)(multiplier << 1 & 0xffff);
        }
        private void Record(string kind, Command command, int track)
        {
            if (receipts.Count == 256) receipts.RemoveAt(0);
            receipts.Add(new SourceNativePcmReceipt(kind, command == null ? lastOutput : command.Output,
                command == null ? checked(origin + At(sourceTick)) : command.At, command == null ? null : command.Bank,
                command == null ? -1 : command.Id, track, command == null ? 0 : command.Volume,
                command == null ? 0 : command.Pan, command == null ? 0 : command.Word));
        }
        private static long At(long tick) { return checked(tick * 31250 / 57); }
        private void Healthy() { if (failure != null) throw new InvalidOperationException(failure); }
        private void Fail(string detail)
        {
            failure = detail + " Native PCM is faulted; explicit restart is required.";
            commands.Clear(); Array.Clear(voices, 0, voices.Length); active = false;
            throw new InvalidOperationException(failure);
        }
    }

    public sealed class SourceUnityPcmSoundOutput : ISourceNativeSoundOutput, IDisposable
    {
        private readonly AudioSource source;
        private readonly AudioClip clip;
        private bool disposed;
        private volatile string callbackFailure;
        public SourceNativePcmMixer Mixer { get; }
        public string Failure { get { return callbackFailure ?? Mixer.Failure; } }

        public SourceUnityPcmSoundOutput(GameObject owner, IReadOnlyList<SourceAudioBank> admittedBanks,
            string authorizationReference, IReadOnlyList<string> initiallyLoadedBanks, uint reservedTracks,
            int latencySourceTicks = 12)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            Mixer = new SourceNativePcmMixer(admittedBanks, authorizationReference, initiallyLoadedBanks, reservedTracks, latencySourceTicks);
            source = owner.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 0; source.panStereo = 0;
            source.volume = 1; source.pitch = 1; source.loop = true;
            clip = AudioClip.Create("Original decoded six-track PCM", 31250, 2, 31250, true, Read);
            if (clip == null) { UnityEngine.Object.Destroy(source); throw new InvalidOperationException("Unity failed to allocate the real streaming PCM output."); }
            source.clip = clip; source.Play();
        }
        private void Healthy()
        {
            if (disposed || source == null || clip == null) throw new ObjectDisposedException(nameof(SourceUnityPcmSoundOutput));
            if (Failure != null) { source.Stop(); throw new InvalidOperationException(Failure); }
        }
        private void Read(float[] data)
        {
            try { Mixer.ReadStereo(data); }
            catch (InvalidOperationException error)
            {
                callbackFailure = error.Message;
                Array.Clear(data, 0, data.Length);
            }
        }
        public void ConsumeResolved(SourceSoundVmDispatch dispatch) { Healthy(); Mixer.ConsumeResolved(dispatch); }
        public void Consume(SourceDirectSoundCall call) { Healthy(); Mixer.Consume(call); }
        public void Consume(SourceProcessSoundCall call) { Healthy(); Mixer.Consume(call); }
        public void Consume(SourceProcessSoundControl call) { Healthy(); Mixer.Consume(call); }
        public void Consume(SourceBootSoundOutput call) { Healthy(); Mixer.Consume(call); }
        public void AdvanceTo(long tick) { Healthy(); Mixer.AdvanceTo(tick); }
        public void SetActive(bool active)
        {
            if (active) Healthy();
            else if (disposed || source == null) throw new ObjectDisposedException(nameof(SourceUnityPcmSoundOutput));
            Mixer.SetActive(active);
            if (active) source.UnPause(); else source.Pause();
        }
        public void Reset(long epoch)
        {
            if (disposed || source == null) throw new ObjectDisposedException(nameof(SourceUnityPcmSoundOutput));
            source.Stop(); callbackFailure = null; Mixer.Reset(epoch); source.Play();
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (source != null) { source.Stop(); source.clip = null; UnityEngine.Object.Destroy(source); }
            if (clip != null) UnityEngine.Object.Destroy(clip);
        }
    }
}
