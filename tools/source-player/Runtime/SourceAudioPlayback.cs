using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Blitz.AssetImport;
using Blitz.Contracts;
using Blitz.Gameplay;
using Blitz.Presentation;
using Blitz.Runtime;

namespace Blitz.SourcePlayer
{
    public interface ISourceAudioSink
    {
        void AudioSample(string id, int sampleRate, int channels, byte[] pcm16Le, string metadata);
        void AudioCommands(string commands);
    }

    public interface ISourceAudioClockWitness
    {
        // Render-frontier diagnostic only, never simulation/input elapsed time.
        double ReadSourceElapsed(long epoch);
    }

    public sealed class SourceAudioBankInput
    {
        public SourceAudioBank Bank { get; }

        public SourceAudioBankInput(byte[] package, string packageSha256, string bankName,
            string sourceGitBlob, string sourceSha256)
        {
            Bank = SourceAudioBankReader.Read(package, packageSha256);
            if (Bank.BankName != bankName || Bank.SourceGitBlob != sourceGitBlob ||
                Bank.SourceSha256 != sourceSha256)
                throw new InvalidDataException("SOURCE_AUDIO_PROVENANCE: bank/source identity mismatch for " + bankName + ".");
        }
    }

    public sealed class SourceAudioProgramRequirement
    {
        public string Bank { get; }
        public int PlaylistId { get; }
        public SourceAudioProgramRequirement(string bank, int playlistId)
        {
            if (string.IsNullOrEmpty(bank) || playlistId < 0)
                throw new ArgumentException("An exact bank:playlist identity is required.");
            Bank = bank; PlaylistId = playlistId;
        }
    }

    public sealed class SourceAudioProgramBinding
    {
        public SourceAudioBank Bank { get; }
        public SourceAudioPlaylist Program { get; }
        public string SampleId { get { return "pcm16-" + Program.Pcm.Sha256; } }
        internal SourceAudioProgramBinding(SourceAudioBank bank, SourceAudioPlaylist program)
        { Bank = bank; Program = program; }
    }

    public sealed class SourceAudioPlaybackAdmission
    {
        public const string Policy = "portable-source-qualified-pcm-one-shot/v1";
        public const string CompiledPolicy = "portable-compiled-original-playlists/v2";
        public const string PreGamePolicy = "portable-original-pregame-continuous-source/v4";
        public const string CompleteOriginalManifestSha256 = "bb3cc14f6c1797dff79a92e192aab3f7cf1d4be58f39a99facbbcd56a598794a";
        public const string CompleteSourceBanksManifestSha256 = "a36dd1938a305114bb8699bec70395466ea2707fcd8f8d2aa8ccb356b0e9e823";
        public const string CompletePrematchManifestSha256 = "37cb399381bc1592ea1156504d3be3eddd8494d28783b3bf285f5d413065e487";
        private readonly Dictionary<string, SourceAudioProgramBinding> programs =
            new Dictionary<string, SourceAudioProgramBinding>(StringComparer.Ordinal);
        public string Name { get; }
        public string AuthorizationReference { get; }
        public uint InitialReservedTracks { get; }
        public ReadOnlyCollection<SourceAudioBank> Banks { get; }
        public ReadOnlyCollection<SourceAudioProgramBinding> Programs { get; }
        public bool CompiledPrograms { get; }
        public bool OriginalPreGamePrograms => CompiledPrograms &&
            Name == SourceTeamResourceProfile.CompleteOriginalPrematchStableId;
        public string PlaybackPolicy => OriginalPreGamePrograms ? PreGamePolicy : CompiledPrograms ? CompiledPolicy : Policy;
        public ReadOnlyCollection<string> InitialBanks { get; }

        // The caller must establish authorization separately. Hashes prove identity, not rights.
        public SourceAudioPlaybackAdmission(string name, string authorizationReference,
            IReadOnlyList<SourceAudioBankInput> inputs, IReadOnlyList<SourceAudioProgramRequirement> requiredPrograms,
            uint initialReservedTracks = 0, bool compiledOriginalPrograms = false,
            IReadOnlyList<string> initialBanks = null)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(authorizationReference))
                throw new ArgumentException("Decoded audio requires a named explicit admission and an established authorization reference.");
            int maxBanks = compiledOriginalPrograms && (name == "original-teams-complete-original303-source-banks" ||
                name == "original-teams-complete-original303-records" ||
                name == "original-teams-complete-original303-final" ||
                name == SourceTeamResourceProfile.CompleteOriginalPrematchStableId) ? 64 :
                compiledOriginalPrograms ? 24 : 16;
            if (inputs == null || inputs.Count == 0 || inputs.Count > maxBanks ||
                requiredPrograms == null || requiredPrograms.Count == 0 || requiredPrograms.Count > 4096 ||
                (initialReservedTracks & ~63u) != 0)
                throw new ArgumentException("Decoded audio requires a bounded bank/program inventory and six-track mask.");
            Name = name; AuthorizationReference = authorizationReference; InitialReservedTracks = initialReservedTracks;
            CompiledPrograms = compiledOriginalPrograms;
            var banks = new List<SourceAudioBank>();
            var byName = new Dictionary<string, SourceAudioBank>(StringComparer.Ordinal);
            foreach (SourceAudioBankInput input in inputs)
            {
                if (input == null || byName.ContainsKey(input.Bank.BankName))
                    throw new ArgumentException("Decoded bank inputs must be nonnull and unique.");
                byName.Add(input.Bank.BankName, input.Bank);
                banks.Add(input.Bank);
            }
            var bindings = new List<SourceAudioProgramBinding>();
            var samples = new HashSet<string>(StringComparer.Ordinal);
            long bytes = 0;
            foreach (SourceAudioProgramRequirement requirement in requiredPrograms)
            {
                SourceAudioBank bank;
                if (requirement == null || !byName.TryGetValue(requirement.Bank, out bank))
                    throw new InvalidDataException("SOURCE_AUDIO_BANK_MISSING: " +
                        (requirement == null ? "<null>" : requirement.Bank + ":" + requirement.PlaylistId) + ".");
                string key = Key(requirement.Bank, requirement.PlaylistId);
                if (programs.ContainsKey(key)) throw new ArgumentException("Duplicate audio program admission: " + key);
                SourceAudioPlaylist program = bank.GetPlaylist(requirement.PlaylistId);
                if (compiledOriginalPrograms)
                {
                    if ((bank.PackageVersion != 2 && !(OriginalPreGamePrograms && bank.PackageVersion == 3)) ||
                        !program.CompiledPlaybackQualified)
                        throw new NotSupportedException("The compiled-original admission requires real independently executed v2 playlist programs.");
                }
                else RequireQualified(bank.BankName, program);
                var binding = new SourceAudioProgramBinding(bank, program);
                programs.Add(key, binding); bindings.Add(binding);
                if (samples.Add(binding.SampleId)) bytes += (long)program.Pcm.SampleCount * 2;
                if (program.GlobalMixer != null && samples.Add("pcm16-" + program.GlobalMixer.PrimaryZeroPcm.Sha256))
                    bytes += (long)program.GlobalMixer.PrimaryZeroPcm.SampleCount * 2;
                if (bytes > (compiledOriginalPrograms ? 128L : 64L) * 1024 * 1024)
                    throw new InvalidDataException("Admitted PCM exceeds its explicit bounded decoded profile.");
            }
            Banks = banks.AsReadOnly(); Programs = bindings.AsReadOnly();
            string[] loaded = initialBanks == null ? banks.Select(bank => bank.BankName).ToArray() : initialBanks.ToArray();
            if (loaded.Distinct(StringComparer.Ordinal).Count() != loaded.Length || loaded.Any(bank => !byName.ContainsKey(bank)))
                throw new ArgumentException("Initial bank lifecycle requires unique actually admitted source banks.");
            InitialBanks = Array.AsReadOnly(loaded);
        }

        public SourceAudioProgramBinding RequireProgram(string bank, int id)
        {
            if (CompiledPrograms && bank != null) bank = bank.ToLowerInvariant();
            SourceAudioProgramBinding binding;
            if (bank == null || !programs.TryGetValue(Key(bank, id), out binding))
                throw new NotSupportedException("SOURCE_AUDIO_PROGRAM_NOT_ADMITTED: " + bank + ":" + id +
                    ". Supply its explicitly authorized, pinned, qualified SAUD program; no fallback is allowed.");
            return binding;
        }

        internal SourceAudioBank UnassignedBank(string bank, int id)
        {
            if (!CompiledPrograms) return null;
            foreach (SourceAudioBank input in Banks)
                if (string.Equals(input.BankName, bank, StringComparison.OrdinalIgnoreCase) &&
                    SourceAudioSparseSlots.IsUnassigned(input, id)) return input;
            return null;
        }

        private static string Key(string bank, int id)
        { return bank + ":" + id.ToString(System.Globalization.CultureInfo.InvariantCulture); }

        private static void RequireQualified(string bank, SourceAudioPlaylist program)
        {
            if (!program.IsolatedPlaybackQualified || program.RepeatCount != 1)
                throw new NotSupportedException("SOURCE_AUDIO_PROGRAM_UNSUPPORTED: " + bank + ":" + program.Id +
                    " program=" + program.OriginalProgramHex + ", endWait=" + program.EndWaitFrames +
                    ", streamFrames=" + program.OriginalStreamFrames +
                    ". Only qualified once-only programs are admitted; loops/control timing need a real interpreter.");
        }
    }

    public sealed class SourceAudioPlaybackCommand
    {
        public long Sequence { get; internal set; }
        public long Epoch { get; internal set; }
        public string Kind { get; internal set; }
        public long SourceTick { get; internal set; }
        public long AtSample { get; internal set; }
        public long OutputSequence { get; internal set; }
        public int SubcallOrder { get; internal set; }
        public long VoiceId { get; internal set; }
        public int Track { get; internal set; } = -1;
        public uint TrackMask { get; internal set; }
        public string Bank { get; internal set; }
        public int PlaylistId { get; internal set; } = -1;
        public string PackageSha256 { get; internal set; }
        public string SourceGitBlob { get; internal set; }
        public string ProgramHex { get; internal set; }
        public string SampleId { get; internal set; }
        public int SampleCount { get; internal set; }
        public bool Loop { get; internal set; }
        [JsonIgnore] public int LoopStartSample { get; internal set; }
        [JsonIgnore] public int LoopEndSample { get; internal set; }
        public uint RawVolume { get; internal set; }
        public uint RawPan { get; internal set; }
        public uint PriorityWord { get; internal set; }
        public int VolumeByte { get; internal set; }
        public int PanByte { get; internal set; }
        public double PortableGain { get; internal set; }
        public double PortablePan { get; internal set; }
        public bool Active { get; internal set; }
        public string Reason { get; internal set; }
        [JsonIgnore] public ReadOnlyCollection<SourceAudioMixPoint> MixPoints { get; internal set; } =
            Array.AsReadOnly(Array.Empty<SourceAudioMixPoint>());
    }

    public sealed class SourceAudioMixPoint
    {
        public long AtSample { get; }
        public double Gain { get; }
        internal SourceAudioMixPoint(long sample, double gain) { AtSample = sample; Gain = gain; }
    }

    public sealed class SourceAudioPlaybackBatch
    {
        public string Schema { get; internal set; } = "source-audio-playback/v1";
        public string Policy { get; internal set; } = SourceAudioPlaybackAdmission.Policy;
        public int SampleRate { get { return 31250; } }
        public int SourceTicksPerSecond { get; internal set; }
        public long Epoch { get; internal set; }
        public long SourceTick { get; internal set; }
        public ReadOnlyCollection<SourceAudioPlaybackCommand> Commands { get; internal set; }
    }

    public sealed class SourceAudioPlayback : ISourceSoundVmOutputConsumer, ISourceDirectSoundOutputConsumer
    {
        private sealed class Voice
        {
            internal SourceAudioPlaybackCommand Play;
            internal long EndSample;
            internal int Priority;
            internal int VolumeByte, PanByte;
            internal SourceAudioPlaylist Program;
            internal IReadOnlyList<SourceAudioMixPoint> MusicEnvelope;
        }

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        private readonly SourceAudioPlaybackAdmission admission;
        private readonly ISourceAudioSink sink;
        private readonly List<SourceAudioPlaybackCommand> pending = new List<SourceAudioPlaybackCommand>();
        private readonly HashSet<string> loaded = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> uploadedSamples = new HashSet<string>(StringComparer.Ordinal);
        private readonly Voice[] tracks = new Voice[6];
        private long sequence, voiceSequence, lastOutputSequence;
        private int nextBootOrder;
        private int originalDomain;
        private bool clockNeedsStart;
        public int SourceTicksPerSecond { get; }
        public long SourceTick { get; private set; }
        public long Epoch { get; private set; } = -1;
        public uint ReservedTracks { get; private set; }
        public bool Active { get; private set; } = true;
        public string Failure { get; private set; }
        public long SubmittedCommands { get; private set; }
        public long ScheduledCalls { get; private set; }
        public long RejectedCalls { get; private set; }
        public int PendingCount { get { return pending.Count; } }
        public string AdmissionName { get { return admission.Name; } }
        public string PlaybackPolicy { get { return admission.PlaybackPolicy; } }
        public bool CompiledPrograms { get { return admission.CompiledPrograms; } }
        public long LastProducerOutputSequence => lastOutputSequence;
        public ReadOnlyCollection<string> LoadedBanks => Array.AsReadOnly(loaded.ToArray());
        public bool IsBankLoaded(string bank) { return loaded.Contains(bank); }

        public SourceAudioPlayback(SourceAudioPlaybackAdmission admission, ISourceAudioSink sink,
            int sourceTicksPerSecond, long epoch = 0)
        {
            this.admission = admission ?? throw new ArgumentNullException(nameof(admission));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            if (sourceTicksPerSecond <= 0 || sourceTicksPerSecond > 1000)
                throw new ArgumentOutOfRangeException(nameof(sourceTicksPerSecond));
            SourceTicksPerSecond = sourceTicksPerSecond;
            foreach (SourceAudioProgramBinding binding in admission.Programs)
            {
                if (!admission.CompiledPrograms) Upload(binding);
            }
            if (admission.OriginalPreGamePrograms)
                ResetClock(epoch, 0, Array.Empty<string>(), 0, false, false, "unjoined-portable-menu");
            else Reset(epoch);
        }

        private void Upload(SourceAudioProgramBinding binding, bool primaryZero = false)
        {
            SourceAudioPcm pcm = primaryZero && binding.Program.GlobalMixer != null ?
                binding.Program.GlobalMixer.PrimaryZeroPcm : binding.Program.Pcm;
            string id = "pcm16-" + pcm.Sha256;
            if (!uploadedSamples.Add(id)) return;
            byte[] bytes = pcm.CopyPcm16LittleEndian();
            sink.AudioSample(id, pcm.SampleRate, pcm.Channels, bytes,
                JsonSerializer.Serialize(new { schema = "source-audio-sample/v1", format = "pcm-s16le",
                    sha256 = pcm.Sha256, sampleCount = pcm.SampleCount, mixAlreadyBaked = true,
                    admission = admission.Name, nativeDspEquivalent = false }, Json));
        }

        public void PrepareBootSamples(IReadOnlyList<SourceBootSoundOutput> outputs)
        {
            Healthy();
            if (!admission.CompiledPrograms || outputs == null)
                throw new InvalidOperationException("Only actual original versus boot outputs may prime their immutable PCM resources.");
            for (int i = 0; i < outputs.Count; i++)
            {
                SourceBootSoundOutput output = outputs[i];
                if (output == null || output.EntryKind != SourceC2EntryKind.CompleteOriginalVersusSoundBanks ||
                    output.SourceTick != 0 || output.ProcessSerial != 0 || output.BootOrder != i)
                    throw new InvalidOperationException("Boot sample preparation requires the actual once-drained source-zero FIFO.");
                if (output.Kind == SourceBootSoundOutputKind.Control) continue;
                if (admission.UnassignedBank(output.Bank, output.PlaylistId.Value) == null)
                {
                    SourceAudioProgramBinding binding = admission.RequireProgram(output.Bank, output.PlaylistId.Value);
                    Upload(binding); Upload(binding, true);
                }
            }
        }

        public void PreparePreGameSamples()
        {
            Healthy();
            if (!admission.OriginalPreGamePrograms)
                throw new InvalidOperationException("Only the explicitly bounded original menu programme family may be primed.");
            foreach (int id in new[] { 17, 20, 27, 28, 30, 43, 80, 83, 85, 86, 87, 89, 91, 95, 96, 97 })
            {
                SourceAudioProgramBinding binding = admission.RequireProgram("cursors", id);
                Upload(binding); Upload(binding, true);
            }
            foreach (int id in new[] { 6, 7, 9, 10, 11, 12 })
            {
                SourceAudioProgramBinding binding = admission.RequireProgram("intro", id);
                Upload(binding); Upload(binding, true);
            }
        }

        public void ValidateDispatch(SourceSoundVmDispatch dispatch, bool checkLoadedState = true)
        {
            Healthy();
            if (dispatch == null) throw new ArgumentNullException(nameof(dispatch));
            if (dispatch.Status != SourceSoundVmStatus.Resolved || dispatch.Result == null)
                Fail("SOURCE_AUDIO_SELECTOR_UNSUPPORTED: " + Site(dispatch) + ": " + dispatch.Status + ": " + dispatch.Detail);
            foreach (SourceAudioResolvedOriginalSoundCall call in dispatch.Result.Calls)
                RequirePlayable(call.Bank, call.PlaylistId, call.PriorityWord, checkLoadedState);
        }

        public void ConsumeResolved(SourceSoundVmDispatch dispatch)
        {
            ValidateDispatch(dispatch);
            Accept(dispatch.OutputSequence, dispatch.RandomContext.SourceTick);
            foreach (SourceAudioResolvedOriginalSoundCall call in dispatch.Result.Calls)
                Play(call.Bank, call.PlaylistId, call.Volume, call.Pan, call.PriorityWord,
                    dispatch.OutputSequence, call.SubcallOrder);
        }

        public void Consume(SourceDirectSoundCall call)
        {
            Healthy();
            if (call == null) throw new ArgumentNullException(nameof(call));
            RequirePlayable(call.Bank, call.PlaylistId, call.PriorityWord);
            Accept(call.OutputSequence, call.SourceTick);
            Play(call.Bank, call.PlaylistId, call.Volume, call.Pan, call.PriorityWord,
                call.OutputSequence, call.SubcallOrder);
        }

        public void Consume(SourceProcessSoundCall call)
        {
            Healthy();
            if (call == null) throw new ArgumentNullException(nameof(call));
            RequirePlayable(call.Bank, call.PlaylistId, call.PriorityWord);
            Accept(call.OutputSequence, call.SourceTick);
            Play(call.Bank, call.PlaylistId, call.Volume, call.Pan, call.PriorityWord,
                call.OutputSequence, call.SubcallOrder);
        }

        public void Consume(SourceProcessSoundControl call)
        {
            Healthy();
            if (call == null) throw new ArgumentNullException(nameof(call));
            Accept(call.OutputSequence, call.SourceTick);
            Control(call.Kind, call.Bank, call.TrackMask, call.Value);
        }

        public void Consume(SourceBootSoundOutput call)
        {
            Healthy();
            if (call == null || !admission.CompiledPrograms ||
                call.EntryKind != SourceC2EntryKind.CompleteOriginalVersusSoundBanks ||
                call.SourceTick != 0 || call.ProcessSerial != 0 || call.BootOrder != nextBootOrder++)
                Fail("SOURCE_AUDIO_BOOT_IDENTITY: actual named source0 entry and once-only FIFO boot order required.");
            Accept(call.OutputSequence, 0);
            if (call.Kind == SourceBootSoundOutputKind.Control)
                Control(call.ControlKind.Value, call.Bank, call.TrackMask, call.Value);
            else Play(call.Bank, call.PlaylistId.Value, call.Volume, call.Pan, call.PriorityWord, call.OutputSequence, 0);
        }

        private void Control(SourceProcessSoundControlKind kind, string bank, uint mask, uint value)
        {
            if (admission.CompiledPrograms && kind == SourceProcessSoundControlKind.LoadBank && loaded.Contains(bank))
            {
                Queue(new SourceAudioPlaybackCommand { Kind = "bank-load-rejected", Bank = bank,
                    Reason = "original-sdk-already-loaded" });
                return;
            }
            if (admission.CompiledPrograms && kind == SourceProcessSoundControlKind.DeleteBank && !loaded.Contains(bank))
            {
                if (!admission.Banks.Any(b => string.Equals(b.BankName, bank, StringComparison.OrdinalIgnoreCase)))
                    Fail("SOURCE_AUDIO_BANK_NOT_ADMITTED: " + bank);
                Queue(new SourceAudioPlaybackCommand { Kind = "bank-delete-rejected", Bank = bank,
                    Reason = "original-sdk-bank-not-found" });
                return;
            }
            switch (kind)
            {
                case SourceProcessSoundControlKind.LoadBank: LoadBank(bank); break;
                case SourceProcessSoundControlKind.DeleteBank: DeleteBank(bank); break;
                case SourceProcessSoundControlKind.StopTracks: StopTracks(mask); break;
                case SourceProcessSoundControlKind.StopAll: StopAll(); break;
                case SourceProcessSoundControlKind.ReservedTracks: SetReservedTracks(mask); break;
                case SourceProcessSoundControlKind.TrackVolume: SetTrackVolume(mask, value); break;
                case SourceProcessSoundControlKind.TrackPan: SetTrackPan(mask, value); break;
                default: Fail("SOURCE_AUDIO_CONTROL_UNSUPPORTED: " + kind); break;
            }
        }

        public void RejectUnordered(string sourceSite, long tick, string bank, int playlist)
        {
            Fail("SOURCE_AUDIO_PRODUCER_ORDER_MISSING: " + sourceSite + " at tick " + tick + " " +
                bank + ":" + playlist + " lacks the shared direct/VM output ordinal. No guessed ordering or muted success.");
        }

        public void AdvanceTo(long tick)
        {
            Healthy();
            if (tick < SourceTick || !Active && tick != SourceTick)
                Fail("SOURCE_AUDIO_CLOCK: source time cannot rewind or advance while suspended.");
            if (tick > SourceTick) StartClock();
            SourceTick = tick;
            long sample = At(tick);
            while (true)
            {
                int next = -1;
                for (int i = 0; i < tracks.Length; i++)
                    if (tracks[i] != null && tracks[i].EndSample <= sample &&
                        (next == -1 || tracks[i].EndSample < tracks[next].EndSample)) next = i;
                if (next == -1) return;
                Stop(next, "completed", tracks[next].EndSample);
            }
        }

        public void SetReservedTracks(uint mask)
        {
            Healthy(); Mask(mask); ReservedTracks = mask;
            Queue(new SourceAudioPlaybackCommand { Kind = "reserved", TrackMask = mask });
        }

        public void StopTracks(uint mask)
        {
            Healthy(); Mask(mask);
            for (int i = 0; i < tracks.Length; i++)
                if ((mask & (1u << i)) != 0 && tracks[i] != null)
                    Stop(i, "source-stop-track", At(SourceTick), rebuildMusic: false);
            RebuildMusicEnvelope(At(SourceTick));
        }

        public void StopAll()
        {
            Healthy();
            StopTracks(63);
            ReservedTracks = 0; // Goose sound.c: snd_set_reserved documents stop-all resetting this mask.
            Queue(new SourceAudioPlaybackCommand { Kind = "stop-all", TrackMask = 63 });
        }

        public void SetTrackVolume(uint mask, uint volume)
        { Adjust(mask, volume, true); }

        public void SetTrackPan(uint mask, uint pan)
        { Adjust(mask, pan, false); }

        public void LoadBank(string bank)
        {
            Healthy();
            SourceAudioBank found = null;
            foreach (SourceAudioBank candidate in admission.Banks)
                if (string.Equals(candidate.BankName, bank, admission.CompiledPrograms ?
                    StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) found = candidate;
            if (found == null) Fail("SOURCE_AUDIO_BANK_NOT_ADMITTED: " + bank);
            if (!loaded.Add(bank)) throw new InvalidOperationException("Decoded bank is already loaded: " + bank);
            Queue(new SourceAudioPlaybackCommand { Kind = "bank-load", Bank = bank,
                PackageSha256 = found.PackageSha256, SourceGitBlob = found.SourceGitBlob });
        }

        public void DeleteBank(string bank)
        {
            Healthy();
            if (!loaded.Remove(bank)) Fail("SOURCE_AUDIO_BANK_NOT_LOADED: " + bank);
            // snd_bank_delete only removes the host mapping; it does not stop DSP tracks.
            Queue(new SourceAudioPlaybackCommand { Kind = "bank-delete", Bank = bank });
            if (admission.CompiledPrograms)
                foreach (var binding in admission.Programs.Where(binding =>
                    string.Equals(binding.Bank.BankName, bank, StringComparison.OrdinalIgnoreCase)))
                    foreach (string sample in SampleIds(binding))
                        if (!admission.Programs.Any(other => SampleIds(other).Contains(sample) && loaded.Any(name =>
                            string.Equals(name, other.Bank.BankName, StringComparison.OrdinalIgnoreCase))))
                            uploadedSamples.Remove(sample);
        }

        private static IEnumerable<string> SampleIds(SourceAudioProgramBinding binding)
        {
            yield return binding.SampleId;
            if (binding.Program.GlobalMixer != null)
                yield return "pcm16-" + binding.Program.GlobalMixer.PrimaryZeroPcm.Sha256;
        }

        public void SetActive(bool active)
        {
            if (Active == active) return;
            Healthy(); Active = active;
            Queue(new SourceAudioPlaybackCommand { Kind = "active", Active = active,
                Reason = "portable-browser-pause-focus-policy" });
        }

        public void Reset(long epoch, bool active = true, bool newProducer = false)
        {
            if (admission.OriginalPreGamePrograms)
                throw new InvalidOperationException("The pregame composition requires its actual accepted-start or versus transition.");
            ResetClock(epoch, 0, admission.InitialBanks, admission.InitialReservedTracks, active, newProducer,
                "new-match-discard-stale-voices-and-queue");
        }

        public void BeginPreGameEpoch(long epoch, SourcePreGameSoundActivation activation, bool active = true)
        {
            if (!admission.OriginalPreGamePrograms || originalDomain != 0 || activation == null ||
                activation.SourceTick <= 0 || activation.InputEpoch <= 0 ||
                activation.ParticipatingStationMask <= 0 || activation.ParticipatingStationMask > 15 ||
                activation.Entry.ReservedTracks != 35 || activation.Entry.CurrentCursorBank != "cursors" ||
                !activation.Entry.LoadedBanks.SequenceEqual(admission.InitialBanks, StringComparer.Ordinal))
                throw new InvalidOperationException("A fresh actual accepted cabinet start and exact known permanent-bank entry are required.");
            ResetClock(epoch, activation.SourceTick, activation.Entry.LoadedBanks, activation.Entry.ReservedTracks,
                active, true, "original-mainproc-accepted-start-stop-all");
            originalDomain = 1;
        }

        public void BeginVersusEpoch(long epoch, SourcePreGameSoundHandoff handoff, bool active = true)
        {
            if (!admission.OriginalPreGamePrograms || originalDomain == 0 || pending.Count != 0)
                throw new InvalidOperationException("Deliver actual pregame/game output before beginning the source versus stop-all boundary.");
            if (originalDomain == 1 && (handoff == null || handoff.SourceTick != SourceTick ||
                handoff.LastOutputSequence != lastOutputSequence || handoff.ReservedTracks != ReservedTracks ||
                !loaded.SetEquals(handoff.LoadedBanks) || !loaded.Contains(handoff.CurrentCursorBank)) ||
                originalDomain == 2 && handoff != null)
                throw new InvalidOperationException("The versus transition must retain this actual completed producer and known bank state.");
            string[] banks = loaded.ToArray();
            uint reserved = ReservedTracks;
            ResetClock(epoch, 0, banks, reserved, active, true, "original-versus-first-stop-all");
            originalDomain = 2;
        }

        private void ResetClock(long epoch, long sourceTick, IReadOnlyList<string> banks, uint reserved,
            bool active, bool newProducer, string reason)
        {
            if (epoch <= Epoch) throw new InvalidOperationException("Audio restart requires a fresh host epoch.");
            // A new simulation restarts producer ordinals; restarting the same simulation does not.
            if (newProducer) lastOutputSequence = 0;
            nextBootOrder = 0;
            pending.Clear(); Array.Clear(tracks, 0, tracks.Length); loaded.Clear();
            Epoch = epoch; SourceTick = sourceTick; Failure = null; Active = active; ReservedTracks = 0;
            clockNeedsStart = admission.CompiledPrograms;
            Queue(new SourceAudioPlaybackCommand { Kind = "reset", TrackMask = 63, Active = active,
                Reason = reason });
            foreach (string bank in banks) LoadBank(bank);
            SetReservedTracks(reserved);
        }

        public void Flush()
        {
            Healthy();
            try { SendPending(); }
            catch (Exception error) { Fail("SOURCE_AUDIO_SINK: " + error.Message + ". No command retry is allowed."); }
        }

        private void Accept(long outputSequence, long tick)
        {
            if (outputSequence <= lastOutputSequence || outputSequence <= 0)
                Fail("SOURCE_AUDIO_ORDER: missing, duplicate or reordered positive common output ordinal " + outputSequence + ".");
            StartClock();
            AdvanceTo(tick);
            if (!Active) Fail("SOURCE_AUDIO_SUSPENDED: a source call cannot execute during portable suspension.");
            lastOutputSequence = outputSequence;
        }

        private void StartClock()
        {
            if (!clockNeedsStart) return;
            Queue(new SourceAudioPlaybackCommand {
                Kind = "clock-start", Reason = "first-source-wake-or-output-after-ready-boundary"
            });
            clockNeedsStart = false;
        }

        private SourceAudioProgramBinding RequirePlayable(string bank, int id, uint priority, bool checkLoadedState = true)
        {
            try
            {
                SourceAudioBank unassigned = admission.UnassignedBank(bank, id);
                if (unassigned != null)
                {
                    if (checkLoadedState && !loaded.Contains(bank)) Fail("SOURCE_AUDIO_BANK_NOT_LOADED: " + bank + ":" + id);
                    Priority(priority, out _, out _, out _, out _);
                    return null;
                }
                SourceAudioProgramBinding binding = admission.RequireProgram(bank, id);
                if (checkLoadedState && !loaded.Contains(bank)) Fail("SOURCE_AUDIO_BANK_NOT_LOADED: " + bank + ":" + id);
                int track, level;
                bool check, noEqual;
                Priority(priority, out track, out level, out check, out noEqual);
                return binding;
            }
            catch (Exception error) when (error is NotSupportedException || error is InvalidDataException)
            {
                Fail(error.Message);
                throw;
            }
        }

        private void Play(string bank, int id, uint volume, uint pan, uint word, long outputSequence, int subcall)
        {
            SourceAudioProgramBinding binding = RequirePlayable(bank, id, word);
            if (binding == null)
            {
                SourceAudioBank empty = admission.UnassignedBank(bank, id);
                var absent = new SourceAudioPlaybackCommand {
                    Kind = "unassigned-playlist", Bank = bank, PlaylistId = id,
                    PackageSha256 = empty.PackageSha256, SourceGitBlob = empty.SourceGitBlob,
                    OutputSequence = outputSequence, SubcallOrder = subcall,
                    RawVolume = volume, RawPan = pan, PriorityWord = word,
                    VolumeByte = (int)Math.Min(volume, 255u), PanByte = (int)Math.Min(pan, 255u),
                    Reason = "original-sdk-unassigned-ffffffff-slot"
                };
                Projection(absent); RejectedCalls++; Queue(absent); return;
            }
            int track, level;
            bool check, noEqual;
            Priority(word, out track, out level, out check, out noEqual);
            if (track < 0)
            {
                // Lowest eligible slot resolves allocator ties in this PORTABLE profile, not a DSP3 parity claim.
                for (int i = 0; i < tracks.Length; i++)
                {
                    if ((ReservedTracks & (1u << i)) != 0) continue;
                    if (tracks[i] == null) { track = i; break; }
                    if (track < 0 || tracks[i].Priority > tracks[track].Priority) track = i;
                }
            }
            SourceAudioPcm selectedPcm = track == 0 && binding.Program.GlobalMixer != null ?
                binding.Program.GlobalMixer.PrimaryZeroPcm : binding.Program.Pcm;
            Upload(binding, track == 0);
            var command = new SourceAudioPlaybackCommand {
                Kind = "play", Bank = bank, PlaylistId = id, PackageSha256 = binding.Bank.PackageSha256,
                SourceGitBlob = binding.Bank.SourceGitBlob, ProgramHex = binding.Program.OriginalProgramHex,
                SampleId = "pcm16-" + selectedPcm.Sha256, SampleCount = selectedPcm.SampleCount,
                Loop = binding.Program.LoopEndSample > 0, LoopStartSample = binding.Program.LoopStartSample,
                LoopEndSample = binding.Program.LoopEndSample,
                OutputSequence = outputSequence, SubcallOrder = subcall, Track = track,
                RawVolume = volume, RawPan = pan, PriorityWord = word,
                VolumeByte = (int)Math.Min(volume, 255u), PanByte = (int)Math.Min(pan, 255u)
            };
            Projection(command);
            if (track < 0 || check && tracks[track] != null &&
                (level > tracks[track].Priority || noEqual && level == tracks[track].Priority))
            {
                command.Kind = "rejected"; command.Reason = track < 0 ? "all-tracks-reserved" : "track-priority";
                RejectedCalls++; Queue(command); return;
            }
            if (tracks[track] != null) Stop(track, "preempted", At(SourceTick), rebuildMusic: false);
            command.VoiceId = checked(++voiceSequence);
            Queue(command);
            tracks[track] = new Voice { Play = command, EndSample = command.Loop ? long.MaxValue :
                checked(At(SourceTick) + command.SampleCount),
                Priority = level, VolumeByte = command.VolumeByte, PanByte = command.PanByte,
                Program = binding.Program,
                MusicEnvelope = track == 0 ? new[] { new SourceAudioMixPoint(command.AtSample, command.PortableGain) } : null };
            ScheduledCalls++;
            // Preemption removes the old column even when the new programme has no global controls.
            RebuildMusicEnvelope(At(SourceTick));
        }

        private static void Priority(uint word, out int track, out int level, out bool check, out bool noEqual)
        {
            uint flags = word & 0xe000;
            track = flags == 0 ? -1 : (int)((word >> 7) & 7);
            level = (int)(word & 127);
            check = flags != 0x8000; noEqual = flags == 0xe000;
            if ((word & ~0xe3ffu) != 0 || flags != 0 && flags != 0x8000 && flags != 0xe000 ||
                flags == 0 && (word & 0x380) != 0 || track >= 6)
                throw new NotSupportedException("SOURCE_AUDIO_PRIORITY_UNSUPPORTED: 0x" + word.ToString("x") +
                    ". Only dynamic, SND_PRI_SET and SND_PRI_SET_NOINT six-track words are qualified.");
        }

        private void Adjust(uint mask, uint value, bool volume)
        {
            Healthy(); Mask(mask);
            if (value > 255) Fail("SOURCE_AUDIO_TRACK_PARAMETER: track volume/pan must be an actual byte.");
            for (int i = 0; i < tracks.Length; i++)
            {
                if ((mask & (1u << i)) == 0 || tracks[i] == null) continue;
                Voice voice = tracks[i];
                var command = new SourceAudioPlaybackCommand { Kind = "adjust", Track = i, VoiceId = voice.Play.VoiceId,
                    VolumeByte = volume ? (int)value : voice.VolumeByte, PanByte = volume ? voice.PanByte : (int)value };
                Projection(command);
                voice.VolumeByte = command.VolumeByte; voice.PanByte = command.PanByte;
                Queue(command);
                if (i == 0)
                {
                    // The receiver's adjust writes the unducked gain now but retains future automation.
                    var adjusted = new List<SourceAudioMixPoint> {
                        new SourceAudioMixPoint(command.AtSample, command.PortableGain)
                    };
                    if (voice.MusicEnvelope != null)
                        foreach (SourceAudioMixPoint point in voice.MusicEnvelope)
                            if (point.AtSample > command.AtSample && adjusted[adjusted.Count - 1].Gain != point.Gain)
                                adjusted.Add(point);
                    voice.MusicEnvelope = adjusted;
                }
            }
            if ((mask & 1) != 0) RebuildMusicEnvelope(At(SourceTick));
        }

        private static int MixingMultiplier(int level, int channelVolume = 255)
        {
            level = Math.Max(-8191, Math.Min(8191, level));
            int exponent = (((level >> 6) & 0x3ff) + 0x80) & 0xffff;
            uint multiplier = (uint)channelVolume << 7, product = 0x7c94;
            for (int bit = 1; bit <= 128; bit <<= 1)
            {
                if ((exponent & bit) == 0) multiplier = (multiplier * product >> 15) & 0xffff;
                product = (product * product >> 15) & 0xffff;
            }
            return (int)(multiplier << 1 & 0xffff);
        }

        private void RebuildMusicEnvelope(long now)
        {
            if (!admission.OriginalPreGamePrograms || tracks[0] == null) return;
            Voice music = tracks[0];
            var samples = new SortedSet<long> { now };
            if (music.Program.GlobalMixer != null)
                foreach (SourceAudioMixerPoint point in music.Program.GlobalMixer.PrimaryZeroLevels)
                {
                    long sample = checked(music.Play.AtSample + (long)point.Frame * 240);
                    if (sample > now && sample < music.EndSample) samples.Add(sample);
                }
            for (int track = 1; track < tracks.Length; track++)
            {
                Voice voice = tracks[track];
                if (voice == null || voice.Program.GlobalMixer == null) continue;
                foreach (SourceAudioMixerPoint point in voice.Program.GlobalMixer.GlobalMusic)
                {
                    long sample = checked(voice.Play.AtSample + (long)point.Frame * 240);
                    if (sample > now && sample < music.EndSample) samples.Add(sample);
                }
            }
            if (samples.Count > 1024) Fail("SOURCE_AUDIO_MIXER_BUDGET: the actual six-column envelope exceeds its bounded domain.");
            var points = new List<SourceAudioMixPoint>();
            foreach (long sample in samples)
            {
                int baseline = music.Program.GlobalMixer != null ?
                    music.Program.GlobalMixer.PrimaryZeroLevelAt(sample - music.Play.AtSample) :
                    music.Program.ConstantOwnMixingLevel ?? 0;
                int sum = baseline;
                for (int track = 1; track < tracks.Length; track++)
                {
                    Voice voice = tracks[track];
                    if (voice == null || voice.Program.GlobalMixer == null || sample >= voice.EndSample) continue;
                    int contribution = voice.Program.GlobalMixer.GlobalLevelAt(sample - voice.Play.AtSample);
                    if (contribution != 0 && !music.Program.ConstantOwnMixingLevel.HasValue && music.Program.GlobalMixer == null)
                        Fail("SOURCE_AUDIO_MIXER_BASE: live global0 control requires its actual qualified primary mixing timeline.");
                    sum = checked(sum + contribution);
                }
                if (sum < short.MinValue || sum > short.MaxValue)
                    Fail("SOURCE_AUDIO_MIXER_OVERFLOW: native16-bit aggregate overflow is outside this qualified domain.");
                int original = MixingMultiplier(baseline, music.VolumeByte), mixed = MixingMultiplier(sum, music.VolumeByte);
                if (original == 0 && mixed != 0)
                    Fail("SOURCE_AUDIO_MIXER_SILENT_BASE: a silent flattened base cannot reconstruct a newly audible native mix.");
                double gain = music.VolumeByte / 255.0 * (original == 0 ? 1 : (double)mixed / original);
                if (gain < 0 || gain > 1 || double.IsNaN(gain) || double.IsInfinity(gain))
                    Fail("SOURCE_AUDIO_MIXER_DOMAIN: only the source-qualified duck/restore range is admitted.");
                if (points.Count == 0 || points[points.Count - 1].Gain != gain)
                    points.Add(new SourceAudioMixPoint(sample, gain));
            }
            if (SameMusicFuture(music.MusicEnvelope, points, now)) return;
            music.MusicEnvelope = points.AsReadOnly();
            Queue(new SourceAudioPlaybackCommand {
                Kind = "program-mix", VoiceId = music.Play.VoiceId, Track = 0,
                Bank = music.Play.Bank, PlaylistId = music.Play.PlaylistId,
                Reason = "original-dcs233-global0-mixer-envelope", MixPoints = points.AsReadOnly()
            }, now);
        }

        private static bool SameMusicFuture(IReadOnlyList<SourceAudioMixPoint> scheduled,
            IReadOnlyList<SourceAudioMixPoint> replacement, long now)
        {
            if (scheduled == null || scheduled.Count == 0 || replacement.Count == 0) return false;
            int current = 0;
            while (current + 1 < scheduled.Count && scheduled[current + 1].AtSample <= now) current++;
            if (scheduled[current].Gain != replacement[0].Gain ||
                scheduled.Count - current != replacement.Count) return false;
            // Both schedules are canonical step envelopes (adjacent equal gains are omitted).
            for (int i = 1; i < replacement.Count; i++)
            {
                SourceAudioMixPoint before = scheduled[current + i], after = replacement[i];
                if (before.AtSample != after.AtSample || before.Gain != after.Gain) return false;
            }
            return true;
        }

        private static void Projection(SourceAudioPlaybackCommand command)
        {
            // Source establishes bytes/endpoints, not the arcade DSP transfer function.
            command.PortableGain = command.VolumeByte / 255.0;
            command.PortablePan = (command.PanByte - 127) / (command.PanByte <= 127 ? 127.0 : 128.0);
        }

        private void Stop(int track, string reason, long atSample, bool rebuildMusic = true)
        {
            SourceAudioPlaybackCommand play = tracks[track].Play;
            Queue(new SourceAudioPlaybackCommand { Kind = "stop", Track = track, VoiceId = play.VoiceId,
                Bank = play.Bank, PlaylistId = play.PlaylistId, Reason = reason }, atSample);
            tracks[track] = null;
            if (rebuildMusic && reason != "completed") RebuildMusicEnvelope(atSample);
        }

        private long At(long tick)
        { return checked(tick * 31250 / SourceTicksPerSecond); }

        private void Queue(SourceAudioPlaybackCommand command, long? atSample = null)
        {
            if (pending.Count >= 512) Fail("SOURCE_AUDIO_QUEUE_FULL: receiver did not consume the bounded ordered output.");
            command.Sequence = checked(++sequence); command.Epoch = Epoch; command.SourceTick = SourceTick;
            command.AtSample = atSample ?? At(SourceTick);
            pending.Add(command);
        }

        private void SendPending()
        {
            if (pending.Count == 0) return;
            var batch = new SourceAudioPlaybackBatch { Epoch = Epoch, SourceTick = SourceTick,
                SourceTicksPerSecond = SourceTicksPerSecond, Commands = Array.AsReadOnly(pending.ToArray()) };
            pending.Clear(); // Never retry a receiver that might have partially consumed the batch.
            if (admission.CompiledPrograms)
            {
                batch.Schema = admission.OriginalPreGamePrograms ? "source-audio-playback/v4" : "source-audio-playback/v2";
                batch.Policy = admission.PlaybackPolicy;
                var json = JsonSerializer.SerializeToNode(batch, Json).AsObject();
                var commands = json["commands"].AsArray();
                for (int i = 0; i < commands.Count; i++)
                {
                    commands[i]["loopStartSample"] = batch.Commands[i].LoopStartSample;
                    commands[i]["loopEndSample"] = batch.Commands[i].LoopEndSample;
                    if (admission.OriginalPreGamePrograms)
                        commands[i]["mixPoints"] = JsonSerializer.SerializeToNode(batch.Commands[i].MixPoints, Json);
                }
                sink.AudioCommands(json.ToJsonString());
            }
            else sink.AudioCommands(JsonSerializer.Serialize(batch, Json));
            SubmittedCommands += batch.Commands.Count;
        }

        private void Mask(uint mask)
        {
            if ((mask & ~63u) != 0) Fail("SOURCE_AUDIO_TRACK_MASK: only original tracks0..5 exist.");
        }

        private void Healthy()
        {
            if (Failure != null) throw new InvalidOperationException(Failure);
        }

        private void Fail(string detail)
        {
            if (Failure == null)
            {
                Failure = detail + " Restart is required.";
                pending.Clear(); Array.Clear(tracks, 0, tracks.Length); Active = false;
                Queue(new SourceAudioPlaybackCommand { Kind = "stop-all", TrackMask = 63, Reason = "failure" });
                Queue(new SourceAudioPlaybackCommand { Kind = "active", Active = false, Reason = "failure" });
                try { SendPending(); }
                catch (Exception error) { Failure += " Stop-output failure: " + error.Message; }
            }
            throw new InvalidOperationException(Failure);
        }

        private static string Site(SourceSoundVmDispatch dispatch)
        {
            return dispatch.HasOriginalAnimationCue
                ? dispatch.OriginalCueContext.SourcePath + ":" + dispatch.OriginalCueContext.SourceLine + "/" +
                    dispatch.OriginalCue.FunctionSymbol + "(" + dispatch.OriginalCue.Argument0 + "," +
                    dispatch.OriginalCue.Argument1 + "," + dispatch.OriginalCue.Argument2 + ")"
                : dispatch.C2Context.SourcePath + ":" + dispatch.C2Context.SourceLine + "/" + dispatch.C2Context.FunctionSymbol;
        }
    }
}
