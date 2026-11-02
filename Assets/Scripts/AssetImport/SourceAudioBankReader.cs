using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Blitz.AssetImport
{
    public sealed class SourceAudioPcm
    {
        private readonly byte[] samples;
        public int SampleCount { get { return samples.Length / 2; } }
        public int SampleRate { get { return 31250; } }
        public int Channels { get { return 1; } }
        public string Sha256 { get; }
        public short this[int index]
        {
            get
            {
                if (index < 0 || index >= SampleCount) throw new IndexOutOfRangeException();
                return unchecked((short)(samples[index * 2] | samples[index * 2 + 1] << 8));
            }
        }

        internal SourceAudioPcm(byte[] pcm, string sha256)
        {
            // Reader-owned immutable PCM stays packed until an actually used program reaches the sink.
            samples = pcm;
            Sha256 = sha256;
        }

        internal byte[] CopyPcm16LittleEndian() { return (byte[])samples.Clone(); }

        public float[] CopyNormalizedSamples()
        {
            var copy = new float[SampleCount];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = this[i] / 32768.0f;
            return copy;
        }
    }

    public sealed class SourceAudioPlaylist
    {
        public int Id { get; }
        public uint OriginalProgramOffset { get; }
        public uint OriginalStreamOffset { get; }
        public int OriginalStreamFrames { get; }
        public int EndWaitFrames { get; }
        public int MixingLevel { get; }
        public int RepeatCount { get { return LoopEndSample > 0 ? 0 : 1; } }
        public bool IsolatedPlaybackQualified { get { return !CompiledPlaybackQualified && EndWaitFrames == OriginalStreamFrames + 1; } }
        public bool CompiledPlaybackQualified { get; }
        public int RenderedFrames { get; }
        public int LoopStartSample { get; }
        public int LoopEndSample { get; }
        public string OriginalProgramHex { get; }
        public SourceAudioPcm Pcm { get; }
        public SourceAudioMixerProgram GlobalMixer { get; }
        public int? ConstantOwnMixingLevel { get; }

        internal SourceAudioPlaylist(int id, uint offset, uint streamOffset, int frames,
            byte[] program, SourceAudioPcm pcm)
        {
            Id = id;
            OriginalProgramOffset = offset;
            OriginalStreamOffset = streamOffset;
            OriginalStreamFrames = frames;
            EndWaitFrames = (program[15] << 8) | program[16];
            MixingLevel = program[6];
            OriginalProgramHex = SourceAudioBankReader.Hex(program);
            Pcm = pcm;
            ConstantOwnMixingLevel = MixingLevel << 6;
        }

        internal SourceAudioPlaylist(int id, uint offset, byte[] program, int frames, int loopStart,
            int loopEnd, SourceAudioPcm pcm, SourceAudioMixerProgram globalMixer = null)
        {
            Id = id; OriginalProgramOffset = offset; OriginalProgramHex = SourceAudioBankReader.Hex(program);
            Pcm = pcm; RenderedFrames = frames; LoopStartSample = loopStart; LoopEndSample = loopEnd;
            CompiledPlaybackQualified = true;
            GlobalMixer = globalMixer;
            ConstantOwnMixingLevel = globalMixer == null ? SourceAudioBankReader.ConstantOwnMix(program) : null;
        }
    }

    public sealed class SourceAudioBank
    {
        private readonly Dictionary<int, SourceAudioPlaylist> byId;
        public string BankName { get; }
        public string SourceGitBlob { get; }
        public string SourceSha256 { get; }
        public string PackageSha256 { get; }
        public int OriginalSlotCount { get; }
        public int PackageVersion { get; }
        public ReadOnlyCollection<SourceAudioPlaylist> Playlists { get; }

        internal SourceAudioBank(string name, string blob, string sourceHash, string packageHash,
            int slots, List<SourceAudioPlaylist> playlists, int version = 1)
        {
            BankName = name;
            SourceGitBlob = blob;
            SourceSha256 = sourceHash;
            PackageSha256 = packageHash;
            OriginalSlotCount = slots;
            PackageVersion = version;
            Playlists = playlists.AsReadOnly();
            byId = new Dictionary<int, SourceAudioPlaylist>();
            foreach (SourceAudioPlaylist playlist in playlists)
                byId.Add(playlist.Id, playlist);
        }

        public SourceAudioPlaylist GetPlaylist(int originalPlaylistId)
        {
            SourceAudioPlaylist playlist;
            if (!byId.TryGetValue(originalPlaylistId, out playlist))
                throw new NotSupportedException("Original playlist " + BankName + ":" +
                    originalPlaylistId + " has not been decoded/qualified; no fallback is allowed.");
            return playlist;
        }
    }

    // Reads the strict binary companion of tools/audio's audit JSON, not a BNK.
    // All PCM is isolated, once-only, with original playlist mix already baked in.
    public static class SourceAudioBankReader
    {
        private static readonly byte[] Magic = { 83, 65, 85, 68, 1, 0, 0, 0 };

        public static SourceAudioBank Read(byte[] package, string expectedPackageSha256)
        {
            if (package == null)
                throw new ArgumentNullException(nameof(package));
            if (package.Length < 65 || package.Length > 64 * 1024 * 1024)
                throw new InvalidDataException("Source audio package size is outside the bounded profile.");
            if (!IsHash(expectedPackageSha256))
                throw new ArgumentException("Supply the frozen lower-case SHA-256 package pin.", nameof(expectedPackageSha256));
            string packageHash = Hash(package);
            if (!String.Equals(packageHash, expectedPackageSha256, StringComparison.Ordinal))
                throw new InvalidDataException("Source audio package SHA-256 mismatch.");
            using (var stream = new MemoryStream(package, false))
            using (var reader = new BinaryReader(stream, Encoding.ASCII, false))
            {
                byte[] magic = Exact(reader, Magic.Length, "magic");
                for (int i = 0; i < Magic.Length; i++)
                    if (i == 4 ? magic[i] != 1 && magic[i] != 2 && magic[i] != 3 : magic[i] != Magic[i])
                        throw new InvalidDataException("Unsupported source audio package version.");
                int version = magic[4];
                int nameLength = reader.ReadByte();
                if (nameLength < 1 || nameLength > 32)
                    throw new InvalidDataException("Invalid bank name length.");
                byte[] nameBytes = Exact(reader, nameLength, "bank name");
                foreach (byte value in nameBytes)
                    if (!(value >= 97 && value <= 122) && !(value >= 48 && value <= 57) && value != 95)
                        throw new InvalidDataException("Invalid ASCII bank name.");
                string name = Encoding.ASCII.GetString(nameBytes);
                string sourceBlob = Hex(Exact(reader, 20, "source blob"));
                string sourceHash = Hex(Exact(reader, 32, "source hash"));
                int slots = UInt16(reader, "slot count");
                int count = UInt16(reader, "playlist count");
                if (slots == 0 || count == 0 || count > slots)
                    throw new InvalidDataException("Invalid playlist inventory.");
                var playlists = new List<SourceAudioPlaylist>(count);
                int previousId = -1;
                for (int i = 0; i < count; i++)
                {
                    int encoding = version == 3 ? reader.ReadByte() : version;
                    if (encoding != 1 && encoding != 2 && encoding != 3 || version == 3 && encoding == 1)
                        throw new InvalidDataException("Invalid explicit source programme representation.");
                    int id = UInt16(reader, "playlist id");
                    uint offset = UInt32(reader, "program offset");
                    int programLength = UInt16(reader, "program length");
                    if (id <= previousId || id >= slots)
                        throw new InvalidDataException("Playlist IDs must be unique, ordered original sparse-table IDs.");
                    previousId = id;
                    if (encoding >= 2)
                    {
                        if (programLength < 5 || programLength > 8192)
                            throw new InvalidDataException("Invalid bounded original compiled playlist program length.");
                        byte[] compiled = Exact(reader, programLength, "compiled original program");
                        bool programLoops = ValidateCompiledProgram(compiled, encoding == 3);
                        uint frameCount = UInt32(reader, "rendered frames");
                        uint loopStart = UInt32(reader, "loop start sample"), loopEnd = UInt32(reader, "loop end sample");
                        uint length = UInt32(reader, "compiled PCM bytes");
                        if (frameCount < 1 || frameCount > 32768 || length != frameCount * 480 ||
                            programLoops != (loopEnd > 0) || loopStart % 240 != 0 || loopEnd % 240 != 0 ||
                            (loopEnd == 0 ? loopStart != 0 : loopStart >= loopEnd || loopEnd != length / 2))
                            throw new InvalidDataException("Original compiled playlist frame/loop/PCM extents disagree.");
                        string hash = Hex(Exact(reader, 32, "compiled PCM hash"));
                        byte[] rendered = Exact(reader, (int)length, "compiled PCM");
                        ValidatePcm(rendered, hash);
                        SourceAudioMixerProgram mixer = null;
                        if (encoding == 3)
                        {
                            uint ownLength = UInt32(reader, "primary-zero PCM bytes");
                            if (loopEnd != 0 || ownLength != length)
                                throw new InvalidDataException("The qualified implicit-global profile requires two finite equal-extent primary contexts.");
                            string ownHash = Hex(Exact(reader, 32, "primary-zero PCM hash"));
                            byte[] own = Exact(reader, (int)ownLength, "primary-zero PCM");
                            ValidatePcm(own, ownHash);
                            uint stop = UInt32(reader, "actual programme stop frame");
                            if (stop + 2 != frameCount || Hex(Exact(reader, 32, "native mixer reference")) !=
                                SourceAudioMixerProgram.NativeReferenceSha256)
                                throw new InvalidDataException("Actual finite00/tail or independent2.33 mixer-reference identity differs.");
                            List<SourceAudioMixerPoint> global = MixerPoints(reader, (int)stop);
                            List<SourceAudioMixerPoint> primary = MixerPoints(reader, (int)stop);
                            if (global[0].Level >= 0 || global[global.Count - 1].Level != 0 ||
                                primary[primary.Count - 1].Level != 0)
                                throw new InvalidDataException("The actual global0 duck/restore and00 matrix reset must be retained.");
                            mixer = new SourceAudioMixerProgram(new SourceAudioPcm(own, ownHash), (int)stop, global, primary);
                        }
                        playlists.Add(new SourceAudioPlaylist(id, offset, compiled, (int)frameCount,
                            (int)loopStart, (int)loopEnd, new SourceAudioPcm(rendered, hash), mixer));
                        continue;
                    }
                    if (programLength != 18)
                        throw new InvalidDataException("Only the qualified one-shot program is supported.");
                    byte[] program = Exact(reader, programLength, "program");
                    uint sampleOffset = UInt32(reader, "sample offset");
                    int frames = UInt16(reader, "stream frames");
                    uint pcmLength = UInt32(reader, "PCM length");
                    ValidateProgram(program, offset, sampleOffset, frames);
                    long expectedLength = ((long)frames + 2) * 240 * 2;
                    if (pcmLength != expectedLength)
                        throw new InvalidDataException("PCM length disagrees with original stream frames and decoder tail.");
                    string pcmHash = Hex(Exact(reader, 32, "PCM hash"));
                    byte[] pcm = Exact(reader, (int)pcmLength, "PCM");
                    ValidatePcm(pcm, pcmHash);
                    playlists.Add(new SourceAudioPlaylist(id, offset, sampleOffset, frames, program,
                        new SourceAudioPcm(pcm, pcmHash)));
                }
                if (stream.Position != stream.Length)
                    throw new InvalidDataException("Trailing bytes in source audio package.");
                return new SourceAudioBank(name, sourceBlob, sourceHash, packageHash, slots, playlists, version);
            }
        }

        private static void ValidatePcm(byte[] pcm, string hash)
        {
            if (Hash(pcm) != hash) throw new InvalidDataException("PCM SHA-256 mismatch.");
            bool nonzero = false;
            foreach (byte value in pcm)
                if (value != 0) { nonzero = true; break; }
            if (!nonzero) throw new InvalidDataException("Silent substitution is not an original decoded sample.");
        }

        private static List<SourceAudioMixerPoint> MixerPoints(BinaryReader reader, int stop)
        {
            int count = UInt16(reader, "mixer point count"), previous = -1;
            if (count < 1 || count > 8192) throw new InvalidDataException("Invalid bounded mixer point inventory.");
            var result = new List<SourceAudioMixerPoint>(count);
            for (int i = 0; i < count; i++)
            {
                uint frame = UInt32(reader, "mixer frame");
                int level = unchecked((short)UInt16(reader, "signed mixer level"));
                if (frame > stop || frame <= previous || i == 0 && frame != 0 || level < -8191 || level > 8191)
                    throw new InvalidDataException("Original executed mixer points are not ordered, bounded frame/level values.");
                result.Add(new SourceAudioMixerPoint((int)frame, level)); previous = (int)frame;
            }
            return result;
        }

        private static int OperandCount(int op, bool implicitGlobal)
        {
            switch (op)
            {
                case 0: case 13: case 15: return 0;
                case 1: return 5;
                case 2: case 14: return 1;
                case 7: case 8: case 9: case 16: return 2;
                case 10: case 11: case 12: case 17: case 18: return 4;
                case 19: if (implicitGlobal) return 1; break;
                case 20: if (implicitGlobal) return 3; break;
            }
            throw new InvalidDataException("Unqualified original compiled playlist opcode " + op + ".");
        }

        internal static int? ConstantOwnMix(byte[] program)
        {
            int? level = null;
            for (int pc = 2; pc + 3 <= program.Length;)
            {
                int wait = program[pc] << 8 | program[pc + 1], op = program[pc + 2];
                pc += 3;
                int count = OperandCount(op, false);
                if (op >= 8 && op <= 12) return null;
                if (op == 7)
                {
                    int next = unchecked((sbyte)program[pc + 1]) * 64;
                    if (level.HasValue && level.Value != next) return null;
                    level = next;
                }
                if (op == 1 && !level.HasValue) return null;
                pc += count;
                if (op == 0 || wait == 65535) break;
            }
            return level;
        }

        private static bool ValidateCompiledProgram(byte[] p, bool implicitGlobal = false)
        {
            if (p[0] != 1 || p[1] != 1) throw new InvalidDataException("Unsupported compiled original program header.");
            int pc = 2, instructions = 0, lastRepeat = 1;
            bool terminal = false, loopsForever = false, global = false;
            var loops = new List<int>();
            while (pc + 3 <= p.Length)
            {
                if (++instructions > 1024) throw new InvalidDataException("Original compiled instruction budget exceeded.");
                int wait = p[pc] << 8 | p[pc + 1], op = p[pc + 2]; pc += 3;
                int count = OperandCount(op, implicitGlobal);
                global |= op == 19 || op == 20;
                if (pc + count > p.Length) throw new InvalidDataException("Truncated compiled original playlist operands.");
                if ((op == 1 || op == 2 || op >= 7 && op <= 12 || op >= 16 && op <= 18) && p[pc] != 0)
                    throw new InvalidDataException("Concurrent cross-channel original playlist is not isolated compilation.");
                if (op == 1) lastRepeat = p[pc + 4];
                if (op == 2) lastRepeat = 1;
                if (op == 14) loops.Add(p[pc]);
                if (op == 15)
                {
                    if (loops.Count == 0) throw new InvalidDataException("Original compiled loop stack underflow.");
                    loopsForever = loops[loops.Count - 1] == 0;
                    loops.RemoveAt(loops.Count - 1);
                    terminal = loopsForever;
                }
                if (loops.Count > 32) throw new InvalidDataException("Original compiled loop stack budget exceeded.");
                if (wait == 65535) { terminal = true; loopsForever = lastRepeat == 0; }
                else if (op == 0) { terminal = true; loopsForever = false; }
                pc += count;
                if (terminal && pc != p.Length)
                    throw new InvalidDataException("Unreachable trailing bytes in original compiled program.");
            }
            if (pc != p.Length || !terminal) throw new InvalidDataException("Truncated or unterminated original compiled instruction.");
            if (implicitGlobal && (!global || loopsForever))
                throw new InvalidDataException("This explicit global-mixer extension requires its genuine finite13/14 programme.");
            return loopsForever;
        }

        private static void ValidateProgram(byte[] p, uint offset, uint sampleOffset, int frames)
        {
            if (p[0] != 1 || p[1] != 1 || p[2] != 0 || p[3] != 0 ||
                p[4] != 7 || p[5] != 0 || p[6] > 127 || p[7] != 0 ||
                p[8] != 0 || p[9] != 1 || p[10] != 0 || p[14] != 1 || p[17] != 0)
                throw new InvalidDataException("Unsupported original program control, channel, mix, wait, or repeat.");
            if (frames < 1 || frames >= 65535)
                throw new InvalidDataException("Invalid original stream frame count.");
            uint relative = ((uint)p[11] << 16) | ((uint)p[12] << 8) | p[13];
            ulong address = (ulong)offset + 11 + relative;
            if (address > System.UInt32.MaxValue || address != sampleOffset ||
                (ulong)sampleOffset < (ulong)offset + 18)
                throw new InvalidDataException("Original relative stream address mismatch.");
        }

        private static byte[] Exact(BinaryReader reader, int length, string field)
        {
            if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Truncated source audio " + field + ".");
            byte[] result = reader.ReadBytes(length);
            if (result.Length != length)
                throw new InvalidDataException("Truncated source audio " + field + ".");
            return result;
        }

        private static int UInt16(BinaryReader reader, string field)
        {
            byte[] bytes = Exact(reader, 2, field);
            return bytes[0] | (bytes[1] << 8);
        }

        private static uint UInt32(BinaryReader reader, string field)
        {
            byte[] bytes = Exact(reader, 4, field);
            return (uint)bytes[0] | ((uint)bytes[1] << 8) | ((uint)bytes[2] << 16) | ((uint)bytes[3] << 24);
        }

        private static bool IsHash(string value)
        {
            if (value == null || value.Length != 64)
                return false;
            foreach (char c in value)
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f'))
                    return false;
            return true;
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
                return Hex(hash.ComputeHash(bytes));
        }

        internal static string Hex(byte[] bytes)
        {
            var result = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
                result.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return result.ToString();
        }
    }
}
