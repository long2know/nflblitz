using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blitz.AssetImport;

internal static class Program
{
    private static int checks;
    private static int originalPlaylists;
    private static int originalQualified;
    private static long originalSamples;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition)
            throw new Exception(message);
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        checks++;
        try { action(); }
        catch (T) { return; }
        throw new Exception("must reject " + message);
    }

    private static string Hash(byte[] bytes)
    {
        using (SHA256 hash = SHA256.Create())
            return Hex(hash.ComputeHash(bytes));
    }

    private static string Hex(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    private static byte[] Fixture(int endWait = 2, bool second = false)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
        {
            writer.Write(new byte[] { 83, 65, 85, 68, 1, 0, 0, 0 });
            writer.Write((byte)4);
            writer.Write(Encoding.ASCII.GetBytes("test"));
            writer.Write(new byte[20]);
            writer.Write(new byte[32]);
            writer.Write((ushort)8);
            writer.Write((ushort)(second ? 2 : 1));
            for (int id = 1; id <= (second ? 2 : 1); id++)
            {
                writer.Write((ushort)id);
                uint offset = (uint)(300 + id * 18);
                writer.Write(offset);
                writer.Write((ushort)18);
                uint sample = (uint)(2000 + id * 100);
                uint relative = sample - offset - 11;
                writer.Write(new byte[] { 1, 1, 0, 0, 7, 0, 90, 0, 0, 1, 0,
                    (byte)(relative >> 16), (byte)(relative >> 8), (byte)relative, 1,
                    (byte)(endWait >> 8), (byte)endWait, 0 });
                writer.Write(sample);
                writer.Write((ushort)1);
                var pcm = new byte[1440];
                for (int i = 0; i < 720; i++)
                {
                    short value = (short)(i == 0 ? -32768 : i == 1 ? 32767 : i - 360);
                    pcm[i * 2] = (byte)value;
                    pcm[i * 2 + 1] = (byte)(value >> 8);
                }
                writer.Write((uint)pcm.Length);
                using (SHA256 hash = SHA256.Create())
                    writer.Write(hash.ComputeHash(pcm));
                writer.Write(pcm);
            }
            writer.Flush();
            return stream.ToArray();
        }
    }

    private static void Bad(byte[] original, int offset, byte value, string message)
    {
        byte[] copy = (byte[])original.Clone();
        copy[offset] = value;
        Throws<InvalidDataException>(() => SourceAudioBankReader.Read(copy, Hash(copy)), message);
    }

    private static void Synthetic()
    {
        byte[] bytes = Fixture();
        string frozenHash = Hash(bytes);
        SourceAudioBank bank = SourceAudioBankReader.Read(bytes, frozenHash);
        Check(bank.BankName == "test" && bank.OriginalSlotCount == 8, "bank identity");
        Check(bank.Playlists.Count == 1 && bank.Playlists[0].Id == 1, "sparse original ID");
        Check(bank.PackageSha256 == frozenHash, "package identity retained");
        SourceAudioPlaylist entry = bank.GetPlaylist(1);
        Check(entry.OriginalProgramOffset == 318 && entry.OriginalStreamOffset == 2100, "relative stream address");
        Check(entry.OriginalStreamFrames == 1 && entry.EndWaitFrames == 2, "original frame/end waits");
        Check(entry.IsolatedPlaybackQualified, "qualified one-shot");
        Check(entry.MixingLevel == 90 && entry.RepeatCount == 1, "mix/repeat retained");
        Check(entry.Pcm.SampleCount == 720 && entry.Pcm.SampleRate == 31250 && entry.Pcm.Channels == 1, "PCM format");
        Check(entry.Pcm[0] == -32768 && entry.Pcm[1] == 32767, "PCM16 sign and endianness");
        float[] normalized = entry.Pcm.CopyNormalizedSamples();
        Check(normalized[0] == -1 && normalized[1] == 32767 / 32768.0f, "normalization endpoints");
        normalized[0] = 123;
        Check(entry.Pcm.CopyNormalizedSamples()[0] == -1, "normalized buffer immutable boundary");
        bytes[137] = 1;
        Check(entry.Pcm[0] == -32768, "package buffer immutable boundary");
        Throws<NotSupportedException>(() => bank.GetPlaylist(7), "unprepared original playlist");
        Throws<NotSupportedException>(() => ((IList<SourceAudioPlaylist>)bank.Playlists).Clear(), "immutable playlist inventory");
        Throws<ArgumentNullException>(() => SourceAudioBankReader.Read(null, frozenHash), "null package");
        Throws<ArgumentException>(() => SourceAudioBankReader.Read(Fixture(), null), "missing frozen pin");
        Throws<ArgumentException>(() => SourceAudioBankReader.Read(Fixture(), frozenHash.ToUpperInvariant()), "noncanonical pin");
        byte[] changedIdentity = Fixture();
        changedIdentity[13] = 1;
        Throws<InvalidDataException>(() => SourceAudioBankReader.Read(changedIdentity, frozenHash),
            "package hash independently of PCM/control validation");
        Throws<InvalidDataException>(() => SourceAudioBankReader.Read(bytes, frozenHash), "package hash gate");
        bytes = Fixture();
        Bad(bytes, 4, 2, "version");
        Bad(bytes, 9, 255, "bank ASCII");
        Bad(bytes, 65, 0, "zero slots");
        Bad(bytes, 67, 0, "empty inventory");
        Bad(bytes, 69, 8, "out of range playlist ID");
        Bad(bytes, 75, 17, "program length");
        foreach (int offset in new[] { 77, 78, 79, 80, 81, 82, 84, 85, 86, 87, 94 })
            Bad(bytes, offset, 255, "unknown control/channel/wait");
        Bad(bytes, 83, 255, "negative mixing level");
        Bad(bytes, 91, 0, "infinite repeat");
        Bad(bytes, 91, 2, "finite repeat");
        Bad(bytes, 88, 255, "relative stream address");
        Bad(bytes, 95, 255, "absolute stream address");
        Bad(bytes, 99, 0, "zero frames");
        Bad(bytes, 99, 2, "frame/PCM count mismatch");
        Bad(bytes, 101, 0, "PCM length");
        Bad(bytes, 105, 255, "PCM hash");
        Bad(bytes, 137, 255, "PCM integrity");
        byte[] nonstandard = Fixture(1);
        SourceAudioPlaylist nonstandardEntry = SourceAudioBankReader.Read(nonstandard, Hash(nonstandard)).GetPlaylist(1);
        Check(nonstandardEntry.EndWaitFrames == 1 && !nonstandardEntry.IsolatedPlaybackQualified,
            "nonstandard end timing must remain unqualified");
        byte[] truncated = new byte[bytes.Length - 1];
        Array.Copy(bytes, truncated, truncated.Length);
        Throws<InvalidDataException>(() => SourceAudioBankReader.Read(truncated, Hash(truncated)), "truncated PCM");
        byte[] trailing = new byte[bytes.Length + 1];
        Array.Copy(bytes, trailing, bytes.Length);
        Throws<InvalidDataException>(() => SourceAudioBankReader.Read(trailing, Hash(trailing)), "trailing bytes");
        byte[] silent = Fixture();
        Array.Clear(silent, 137, 1440);
        using (SHA256 hash = SHA256.Create())
            Array.Copy(hash.ComputeHash(new byte[1440]), 0, silent, 105, 32);
        Throws<InvalidDataException>(() => SourceAudioBankReader.Read(silent, Hash(silent)), "silent substitution");
        byte[] duplicate = Fixture(2, true);
        duplicate[1577] = 1;
        Throws<InvalidDataException>(() => SourceAudioBankReader.Read(duplicate, Hash(duplicate)), "duplicate original ID");
    }

    private static void Original(string manifestPath)
    {
        string root = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifestPath)))
        {
            foreach (JsonElement bankData in document.RootElement.GetProperty("banks").EnumerateArray())
            {
                string name = bankData.GetProperty("name").GetString();
                byte[] bytes = File.ReadAllBytes(Path.Combine(root, bankData.GetProperty("package").GetString()));
                SourceAudioBank bank = SourceAudioBankReader.Read(bytes, bankData.GetProperty("package_sha256").GetString());
                Check(bank.SourceGitBlob == bankData.GetProperty("source_git_blob").GetString(), name + " blob pin");
                Check(bank.SourceSha256 == bankData.GetProperty("source_sha256").GetString(), name + " original SHA-256");
                int count = 0;
                foreach (JsonElement entry in bankData.GetProperty("entries").EnumerateArray())
                {
                    if (!entry.GetProperty("prepared").GetBoolean())
                        continue;
                    count++;
                    SourceAudioPlaylist playlist = bank.GetPlaylist(entry.GetProperty("id").GetInt32());
                    Check(playlist.OriginalProgramOffset == entry.GetProperty("offset").GetUInt32(), name + " program offset");
                    Check(playlist.OriginalStreamOffset == entry.GetProperty("stream_offset").GetUInt32(), name + " sample offset");
                    Check(playlist.OriginalStreamFrames == entry.GetProperty("stream_frames").GetInt32(), name + " stream frames");
                    Check(playlist.EndWaitFrames == entry.GetProperty("end_wait_frames").GetInt32(), name + " exact wait");
                    Check(playlist.MixingLevel == entry.GetProperty("mix_signed").GetInt32(), name + " mix");
                    Check(playlist.OriginalProgramHex == entry.GetProperty("raw_program_hex").GetString(), name + " complete program bytes");
                    Check(playlist.IsolatedPlaybackQualified == entry.GetProperty("isolated_supported").GetBoolean(), name + " qualification");
                    JsonElement sample = entry.GetProperty("sample");
                    byte[] pcm = File.ReadAllBytes(Path.Combine(root, name, sample.GetProperty("pcm_file").GetString()));
                    Check(playlist.Pcm.Sha256 == sample.GetProperty("pcm_sha256").GetString(), name + " PCM hash");
                    Check(playlist.Pcm.SampleCount == sample.GetProperty("pcm_samples").GetInt32(), name + " PCM count");
                    Check(pcm.Length == playlist.Pcm.SampleCount * 2, name + " actual PCM bytes");
                    float[] normalized = playlist.Pcm.CopyNormalizedSamples();
                    for (int i = 0; i < normalized.Length; i++)
                    {
                        short expected = unchecked((short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)));
                        Check(playlist.Pcm[i] == expected, name + " exact original PCM16 sample");
                        Check(normalized[i] == expected / 32768.0f, name + " normalized original PCM sample");
                    }
                    originalSamples += playlist.Pcm.SampleCount;
                    originalPlaylists++;
                    if (playlist.IsolatedPlaybackQualified)
                        originalQualified++;
                }
                Check(bank.Playlists.Count == count, name + " prepared inventory");
            }
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            Synthetic();
            if (args.Length == 1)
                Original(args[0]);
            Console.WriteLine(JsonSerializer.Serialize(new {
                checks, originalPlaylists, originalQualified, originalSamples,
                execution = "SDK pure-reader tests, not Unity runtime validation"
            }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("AUDIO_ASSERTION: " + error.Message);
            return 1;
        }
    }
}
