using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Blitz.AssetImport
{
    public sealed class SourceInterfaceDefenseRow
    {
        public ReadOnlyCollection<float> Line { get; }
        public ReadOnlyCollection<float> Back { get; }
        public int Base { get; }
        public int Range { get; }
        public int Plow { get; }
        internal SourceInterfaceDefenseRow(float[] line, float[] back, int basis, int range, int plow)
        { Line = Array.AsReadOnly(line); Back = Array.AsReadOnly(back); Base = basis; Range = range; Plow = plow; }
    }

    public sealed class SourceInterfaceMovementData
    {
        public const string ResourceKey = "SourceData/movement";
        public const string ContentSha256 = "89c79007e04e9a17894c126c34dc66c199eb5a5961a4c749b1eb6b3e0e3951d6";
        public ReadOnlyDictionary<string, float> Constants { get; }
        public ReadOnlyDictionary<string, IReadOnlyList<string>> Tables { get; }
        public ReadOnlyCollection<float> Friction { get; }
        public ReadOnlyCollection<SourceInterfaceDefenseRow> Defense { get; }

        private SourceInterfaceMovementData(Dictionary<string, float> constants,
            Dictionary<string, IReadOnlyList<string>> tables, float[] friction, SourceInterfaceDefenseRow[] defense)
        {
            Constants = new ReadOnlyDictionary<string, float>(constants);
            Tables = new ReadOnlyDictionary<string, IReadOnlyList<string>>(tables);
            Friction = Array.AsReadOnly(friction); Defense = Array.AsReadOnly(defense);
        }

        public static SourceInterfaceMovementData Read(byte[] bytes, string trustedContentSha256)
        {
            Require(trustedContentSha256 == ContentSha256 && bytes != null && bytes.Length == 9520 &&
                RosterGuard.Sha256(bytes) == ContentSha256, "Original runtime movement resource content pin mismatch.");
            Dictionary<string, object> root = Object(RosterJson.Parse(bytes));
            Require(root.Count == 4 && Text(root, "schema") == "original-source-movement-runtime/v1" &&
                Text(root, "float_encoding") == "ieee754-binary32-uint32-bits", "Unknown runtime movement schema or fixture-only fields.");
            Dictionary<string, object> source = Object(Member(root, "source"));
            Require(source.Count == 5 && Text(source, "repository") == "historicalsource/nflblitz2000" &&
                Text(source, "revision") == SourceInterfaceData.SourceRevision && Text(source, "tree") == SourceInterfaceData.SourceTree &&
                Text(source, "profile") == "OriginalRelease57Hz", "Runtime movement provenance/profile mismatch.");
            Dictionary<string, object> blobs = Object(Member(source, "blobs"));
            Require(blobs.Count == 3 && Text(blobs, "player.c") == "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c" &&
                Text(blobs, "include/player.h") == "a86b1a5b52ba842b206cd7dd0bbb26521db6dcb8" &&
                Text(blobs, "handicap.c") == "871424596fb9786af3602a49cc9d20c466df8ca1", "Runtime movement original blob pins changed.");
            Dictionary<string, object> data = Object(Member(root, "data"));
            Require(data.Count == 4 && !data.ContainsKey("Responses"), "TEST-ONLY first-header Responses must never enter runtime movement.");
            Dictionary<string, object> rawConstants = Object(Member(data, "Constants"));
            Require(rawConstants.Count == 23, "Original runtime movement constant inventory changed.");
            var constants = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> entry in rawConstants)
            { RosterGuard.Identifier(entry.Key); constants.Add(entry.Key, FloatBits(entry.Value)); }
            Dictionary<string, object> rawTables = Object(Member(data, "Tables"));
            Require(rawTables.Count == 13, "Original runtime movement selector inventory changed.");
            var tables = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> entry in rawTables)
            {
                int count = TableCount(entry.Key);
                object[] raw = RosterJson.Array(entry.Value);
                Require(raw.Length == count, "Original movement selector dimensions changed: " + entry.Key);
                var copy = new string[raw.Length];
                for (int i = 0; i < copy.Length; i++)
                { copy[i] = RosterJson.Text(raw[i], 128, false); RosterGuard.Identifier(copy[i]); }
                tables.Add(entry.Key, Array.AsReadOnly(copy));
            }
            float[] friction = Floats(Member(data, "Friction"), 7);
            object[] rows = RosterJson.Array(Member(data, "Defense"));
            Require(rows.Length == 12, "Original defense handicap row count changed.");
            var defense = new SourceInterfaceDefenseRow[12];
            for (int i = 0; i < defense.Length; i++)
            {
                Dictionary<string, object> row = Object(rows[i]);
                Require(row.Count == 5, "Original defense row must retain Line/Back/Base/Range/Plow.");
                defense[i] = new SourceInterfaceDefenseRow(Floats(Member(row, "Line"), 6), Floats(Member(row, "Back"), 6),
                    RosterJson.Integer(Member(row, "Base"), 0, int.MaxValue), RosterJson.Integer(Member(row, "Range"), 0, int.MaxValue),
                    RosterJson.Integer(Member(row, "Plow"), 0, 100));
            }
            return new SourceInterfaceMovementData(constants, tables, friction, defense);
        }

        private static int TableCount(string name)
        {
            switch (name)
            {
                case "run_seqs":
                case "run_scripts":
                case "int_run_scripts":
                case "hurdle_tbl":
                case "turbo_hurdle_tbl": return 8;
                case "qb_runs":
                case "qb_seqs": return 24;
                case "running_taunt_tbl": return 4;
                case "push_anim_tbl":
                case "db_late_hits_tbl": return 3;
                case "intercept_anim_tbl":
                case "violent_db_late_hits_tbl": return 2;
                case "breath_seq_tbl": return 14;
                default: throw new SourceInterfaceException("Unknown original runtime movement selector: " + name);
            }
        }
        private static Dictionary<string, object> Object(object raw)
        {
            var value = raw as Dictionary<string, object>;
            Require(value != null, "Runtime movement requires a JSON object.");
            return value;
        }
        private static object Member(Dictionary<string, object> value, string key)
        {
            object raw;
            Require(value.TryGetValue(key, out raw), "Missing original runtime movement member: " + key);
            return raw;
        }
        private static string Text(Dictionary<string, object> value, string key)
        { return RosterJson.Text(Member(value, key), 256, false); }
        private static float[] Floats(object raw, int count)
        {
            object[] array = RosterJson.Array(raw);
            Require(array.Length == count, "Original runtime movement numeric dimensions changed.");
            var result = new float[count];
            for (int i = 0; i < count; i++) result[i] = FloatBits(array[i]);
            return result;
        }
        private static float FloatBits(object raw)
        {
            double value = RosterJson.Number(raw, 0, uint.MaxValue);
            Require(value == Math.Truncate(value), "Original movement binary32 bits must be integers.");
            uint bits = (uint)value;
            float decoded = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
            Require(!float.IsNaN(decoded) && !float.IsInfinity(decoded), "Nonfinite original runtime movement bit pattern.");
            return decoded;
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new SourceInterfaceException(message); }
    }
}
