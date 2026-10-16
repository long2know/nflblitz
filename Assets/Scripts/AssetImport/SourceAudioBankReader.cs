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
        private readonly short[] samples;
        public int SampleCount { get { return samples.Length; } }
        public int SampleRate { get { return 31250; } }
        public int Channels { get { return 1; } }
        public string Sha256 { get; }
        public short this[int index] { get { return samples[index]; } }

        internal SourceAudioPcm(byte[] pcm, string sha256)
        {
            samples = new short[pcm.Length / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = unchecked((short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)));
            Sha256 = sha256;
        }

        public float[] CopyNormalizedSamples()
        {
            var copy = new float[samples.Length];
            for (int i = 0; i < samples.Length; i++)
                copy[i] = samples[i] / 32768.0f;
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
        public int RepeatCount { get { return 1; } }
        public bool IsolatedPlaybackQualified { get { return EndWaitFrames == OriginalStreamFrames + 1; } }
        public string OriginalProgramHex { get; }
        public SourceAudioPcm Pcm { get; }

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
        public ReadOnlyCollection<SourceAudioPlaylist> Playlists { get; }

        internal SourceAudioBank(string name, string blob, string sourceHash, string packageHash,
            int slots, List<SourceAudioPlaylist> playlists)
        {
            BankName = name;
            SourceGitBlob = blob;
            SourceSha256 = sourceHash;
            PackageSha256 = packageHash;
            OriginalSlotCount = slots;
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
                    if (magic[i] != Magic[i])
                        throw new InvalidDataException("Unsupported source audio package version.");
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
                    int id = UInt16(reader, "playlist id");
                    uint offset = UInt32(reader, "program offset");
                    int programLength = UInt16(reader, "program length");
                    if (id <= previousId || id >= slots)
                        throw new InvalidDataException("Playlist IDs must be unique, ordered original sparse-table IDs.");
                    previousId = id;
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
                    if (!String.Equals(Hash(pcm), pcmHash, StringComparison.Ordinal))
                        throw new InvalidDataException("PCM SHA-256 mismatch.");
                    bool nonzero = false;
                    foreach (byte value in pcm)
                        nonzero |= value != 0;
                    if (!nonzero)
                        throw new InvalidDataException("Silent substitution is not an original decoded sample.");
                    playlists.Add(new SourceAudioPlaylist(id, offset, sampleOffset, frames, program,
                        new SourceAudioPcm(pcm, pcmHash)));
                }
                if (stream.Position != stream.Length)
                    throw new InvalidDataException("Trailing bytes in source audio package.");
                return new SourceAudioBank(name, sourceBlob, sourceHash, packageHash, slots, playlists);
            }
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
