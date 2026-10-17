using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Blitz.Contracts;

namespace Blitz.AssetImport
{
    public enum SourceAudioSoundSite
    {
        HitLine, HitCarrierRandom, HitCarrierExplicit, HitCarrierFlyRandom,
        HitCarrierFlyExplicit, HitCarrierThird, HitOtherRandom, HitOtherExplicit,
        HitOtherFlyRandom, HitOtherFlyExplicit, HitOtherThird,
        GroundRandom, GroundExplicit, GroundNeckRandom, GroundNeckExplicit,
        GroundGruntRandom, GroundGruntExplicit
    }

    public sealed class SourceAudioCalleeSite
    {
        public string SourcePath { get { return "sounds.c"; } }
        public string SourceGitBlob { get; }
        public string FunctionSymbol { get; }
        public int SourceLine { get; }
        internal SourceAudioCalleeSite(string blob, string function, int line)
        {
            SourceGitBlob = blob; FunctionSymbol = function; SourceLine = line;
        }
    }

    public sealed class SourceAudioWeightedTable
    {
        public string Symbol { get; }
        public int SourceLine { get; }
        public ReadOnlyCollection<int> Values { get; }
        internal SourceAudioWeightedTable(string symbol, int line, int[] values)
        {
            Symbol = symbol; SourceLine = line; Values = Array.AsReadOnly(values);
        }
    }

    public readonly struct SourceAudioBranchParameters
    {
        public uint Volume { get; }
        public uint PrimaryPriority { get; }
        public uint SecondaryPriority { get; }
        internal SourceAudioBranchParameters(uint volume, uint primary, uint secondary)
        {
            Volume = volume; PrimaryPriority = primary; SecondaryPriority = secondary;
        }
    }

    public sealed class SourceAudioVmBinding
    {
        public string SourcePath { get; }
        public string ScriptSymbol { get; }
        public string FunctionSymbol { get; }
        public int SourceLine { get; }
        public int Argument0 { get; }
        public int Argument1 { get; }
        public int Argument2 { get; }
        internal SourceAudioVmBinding(string path, string script, string function, int line, int a, int b, int c)
        {
            SourcePath = path; ScriptSymbol = script; FunctionSymbol = function;
            SourceLine = line; Argument0 = a; Argument1 = b; Argument2 = c;
        }
        internal bool Matches(SourceCallbackCue cue)
        {
            return cue.Context.SourcePath == SourcePath && cue.Context.SourceLine == SourceLine &&
                cue.FunctionSymbol == FunctionSymbol && cue.Argument0 == Argument0 &&
                cue.Argument1 == Argument1 && cue.Argument2 == Argument2;
        }
    }

    internal sealed class SourceAudioCallTemplate
    {
        internal readonly SourceAudioSoundSite Role;
        internal readonly SourceAudioCalleeSite Site;
        internal readonly SourceAudioWeightedTable Table;
        internal readonly int ArgumentIndex;
        internal readonly int VolumeMode;
        internal readonly uint Volume;
        internal readonly uint Pan;
        internal readonly int PriorityMode;
        internal readonly uint Priority;
        internal SourceAudioCallTemplate(SourceAudioSoundSite role, SourceAudioCalleeSite site,
            SourceAudioWeightedTable table, int argument, int volumeMode, uint volume, uint pan,
            int priorityMode, uint priority)
        {
            Role = role; Site = site; Table = table; ArgumentIndex = argument;
            VolumeMode = volumeMode; Volume = volume; Pan = pan;
            PriorityMode = priorityMode; Priority = priority;
        }
    }

    public sealed class SourceAudioFunctionData
    {
        private readonly Dictionary<SourceAudioSoundSite, SourceAudioCallTemplate> sites;
        public string PayloadSha256 { get; }
        public string SoundsGitBlob { get; }
        public string SoundHeaderGitBlob { get; }
        public string PlayerHeaderGitBlob { get; }
        public string VmGitBlob { get; }
        public string Bank { get; }
        public int OnscreenFlag { get; }
        public int LineMode { get; }
        public int DroneStation { get; }
        public SourceAudioBranchParameters Human { get; }
        public SourceAudioBranchParameters Drone { get; }
        public ReadOnlyCollection<SourceAudioWeightedTable> Tables { get; }
        public ReadOnlyCollection<SourceAudioVmBinding> VmBindings { get; }

        private SourceAudioFunctionData(string hash, string[] blobs, string bank, int flag, int lineMode,
            int droneStation, SourceAudioBranchParameters human, SourceAudioBranchParameters drone,
            List<SourceAudioWeightedTable> tables, Dictionary<SourceAudioSoundSite, SourceAudioCallTemplate> calls,
            List<SourceAudioVmBinding> bindings)
        {
            PayloadSha256 = hash; SoundsGitBlob = blobs[0]; SoundHeaderGitBlob = blobs[1];
            PlayerHeaderGitBlob = blobs[2]; VmGitBlob = blobs[3]; Bank = bank; OnscreenFlag = flag;
            LineMode = lineMode; DroneStation = droneStation; Human = human; Drone = drone;
            Tables = tables.AsReadOnly(); sites = calls; VmBindings = bindings.AsReadOnly();
        }

        public SourceAudioVmBinding RequireBinding(SourceCallbackCue cue)
        {
            if (cue == null) throw new ArgumentNullException(nameof(cue));
            if (cue.Kind != SourcePresentationCueKind.SoundCallback)
                throw new ArgumentException("Only an actually executed SoundCallback can select audio.", nameof(cue));
            foreach (SourceAudioVmBinding binding in VmBindings)
                if (binding.Matches(cue)) return binding;
            throw new NotSupportedException("Unregistered original sound function/location/argument tuple.");
        }

        internal SourceAudioCallTemplate Call(SourceAudioSoundSite role) { return sites[role]; }

        public static SourceAudioFunctionData Read(byte[] bytes, string frozenPayloadSha256)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length < 128 || bytes.Length > 1024 * 1024)
                throw new InvalidDataException("Function-data size is outside the bounded schema.");
            if (frozenPayloadSha256 == null || !System.Text.RegularExpressions.Regex.IsMatch(
                frozenPayloadSha256, "^[0-9a-f]{64}$"))
                throw new ArgumentException("Supply the frozen payload SHA256, not a hash of untrusted bytes.");
            bytes = (byte[])bytes.Clone();
            string hash = Hash(bytes);
            if (hash != frozenPayloadSha256) throw new InvalidDataException("Function-data SHA256 mismatch.");
            var r = new DataReader(bytes);
            byte[] magic = r.Bytes(8);
            if (Encoding.ASCII.GetString(magic, 0, 4) != "SAFN" ||
                magic[4] != 1 || magic[5] != 0 || magic[6] != 0 || magic[7] != 0)
                throw new InvalidDataException("Unsupported function-data version.");
            var blobs = new string[4];
            for (int n = 0; n < blobs.Length; n++) blobs[n] = Hex(r.Bytes(20));
            string bank = r.Text();
            if (bank.Length == 0) throw new InvalidDataException("Missing original bank identity.");
            int flag = r.Int(), mode = r.Int(), station = r.Int();
            if (flag == 0) throw new InvalidDataException("Missing original visibility mask.");
            var human = r.Branch();
            var drone = r.Branch();
            int tableCount = r.Count(32);
            var tables = new List<SourceAudioWeightedTable>(tableCount);
            var byName = new Dictionary<string, SourceAudioWeightedTable>(StringComparer.Ordinal);
            for (int n = 0; n < tableCount; n++)
            {
                string symbol = r.Text();
                int line = r.Line();
                int length = r.Count(1024);
                var values = new int[length];
                for (int j = 0; j < length; j++)
                {
                    values[j] = r.Int();
                    if (values[j] <= 0) throw new InvalidDataException("Invalid weighted original playlist ID.");
                }
                var table = new SourceAudioWeightedTable(symbol, line, values);
                if (byName.ContainsKey(symbol)) throw new InvalidDataException("Duplicate weighted table.");
                byName.Add(symbol, table); tables.Add(table);
            }
            int siteCount = r.Count(64);
            var sites = new Dictionary<SourceAudioSoundSite, SourceAudioCallTemplate>();
            for (int n = 0; n < siteCount; n++)
            {
                string roleName = r.Text();
                SourceAudioSoundSite role;
                if (!Enum.TryParse(roleName, false, out role) || !Enum.IsDefined(typeof(SourceAudioSoundSite), role))
                    throw new InvalidDataException("Unknown original sound call role.");
                string function = r.Text();
                if (function != "get_hit_snd" && function != "grnd_hit_snd")
                    throw new InvalidDataException("Unknown original sound function.");
                var site = new SourceAudioCalleeSite(blobs[0], function, r.Line());
                string tableName = r.Text();
                SourceAudioWeightedTable table = null;
                if (tableName.Length != 0 && !byName.TryGetValue(tableName, out table))
                    throw new InvalidDataException("Unknown weighted table reference.");
                int argument = r.Int(), volumeMode = r.Int();
                uint volume = r.Volume(), pan = r.Volume();
                int priorityMode = r.Int();
                uint priority = r.Priority();
                if ((table == null ? argument < 0 || argument > 2 : argument != -1) ||
                    volumeMode < 0 || volumeMode > 1 || priorityMode < 0 || priorityMode > 2 ||
                    sites.ContainsKey(role))
                    throw new InvalidDataException("Invalid or duplicate call template.");
                sites.Add(role, new SourceAudioCallTemplate(role, site, table, argument,
                    volumeMode, volume, pan, priorityMode, priority));
            }
            if (sites.Count != Enum.GetValues(typeof(SourceAudioSoundSite)).Length)
                throw new InvalidDataException("Incomplete original two-function call inventory.");
            int bindingCount = r.Count(128);
            var bindings = new List<SourceAudioVmBinding>(bindingCount);
            var bindingLines = new HashSet<string>(StringComparer.Ordinal);
            for (int n = 0; n < bindingCount; n++)
            {
                string path = r.Text(), script = r.Text(), function = r.Text();
                int line = r.Line(), a = r.Int(), b = r.Int(), c = r.Int();
                if ((function != "get_hit_snd" && function != "grnd_hit_snd") ||
                    !bindingLines.Add(path + ":" + line))
                    throw new InvalidDataException("Invalid or duplicate VM source binding.");
                bindings.Add(new SourceAudioVmBinding(path, script, function, line, a, b, c));
            }
            if (!r.End) throw new InvalidDataException("Trailing function-data bytes.");
            return new SourceAudioFunctionData(hash, blobs, bank, flag, mode, station,
                human, drone, tables, sites, bindings);
        }

        private static string Hash(byte[] data)
        {
            using (SHA256 hash = SHA256.Create()) return Hex(hash.ComputeHash(data));
        }
        private static string Hex(byte[] bytes)
        {
            var s = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) s.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return s.ToString();
        }
        private sealed class DataReader
        {
            private readonly byte[] data;
            private int at;
            internal DataReader(byte[] data) { this.data = data; }
            internal bool End { get { return at == data.Length; } }
            internal byte[] Bytes(int count)
            {
                if (count < 0 || count > data.Length - at) throw new InvalidDataException("Truncated function data.");
                var result = new byte[count]; Array.Copy(data, at, result, 0, count); at += count; return result;
            }
            internal int Int() { return unchecked((int)UInt()); }
            private uint UInt()
            {
                byte[] p = Bytes(4);
                return (uint)p[0] | ((uint)p[1] << 8) | ((uint)p[2] << 16) | ((uint)p[3] << 24);
            }
            private int Word() { byte[] p = Bytes(2); return p[0] | (p[1] << 8); }
            internal int Count(int maximum)
            {
                int n = Word();
                if (n < 1 || n > maximum) throw new InvalidDataException("Invalid function-data count.");
                return n;
            }
            internal int Line()
            {
                uint n = UInt();
                if (n == 0 || n > int.MaxValue) throw new InvalidDataException("Invalid original source line.");
                return (int)n;
            }
            internal uint Volume()
            {
                uint n = UInt();
                if (n > byte.MaxValue) throw new InvalidDataException("Invalid original volume/pan parameter.");
                return n;
            }
            internal uint Priority()
            {
                uint n = UInt();
                if (n > ushort.MaxValue) throw new InvalidDataException("Invalid original priority word.");
                return n;
            }
            internal SourceAudioBranchParameters Branch()
            {
                return new SourceAudioBranchParameters(Volume(), Priority(), Priority());
            }
            internal string Text()
            {
                int count = Word();
                if (count > 512) throw new InvalidDataException("Oversized original identity.");
                byte[] value = Bytes(count);
                foreach (byte b in value)
                    if (b < 32 || b > 126) throw new InvalidDataException("Non-ASCII original identity.");
                return Encoding.ASCII.GetString(value);
            }
        }
    }
}
