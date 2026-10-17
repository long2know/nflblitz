using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Blitz.Contracts;

namespace Blitz.AssetImport
{
    public sealed class SourceC2VmBinding
    {
        public string SourcePath { get; }
        public string ScriptSymbol { get; }
        public string FunctionSymbol { get; }
        public SourceC2SoundFunctionKind FunctionKind { get; }
        public int SourceLine { get; }
        public int Argument0 { get; }
        public int Argument1 { get; }
        public int Argument2 { get; }
        internal SourceC2VmBinding(string script, string function, SourceC2SoundFunctionKind kind,
            int line, int argument)
        {
            SourcePath = "plyrseq.c"; ScriptSymbol = script; FunctionSymbol = function;
            FunctionKind = kind; SourceLine = line; Argument0 = argument;
        }

        internal bool Matches(SourceCallbackCue cue)
        {
            return cue.Context.SourcePath == SourcePath && cue.Context.ExecutingSymbol == ScriptSymbol &&
                cue.Context.SourceLine == SourceLine && cue.FunctionSymbol == FunctionSymbol &&
                cue.Argument0 == Argument0 && cue.Argument1 == Argument1 && cue.Argument2 == Argument2;
        }
    }

    public sealed class SourceC2SoundCallTemplate
    {
        public SourceAudioCalleeSite Site { get; }
        public string PlaylistSource { get; }
        public uint Volume { get; }
        public uint Pan { get; }
        public uint Priority { get; }
        internal SourceC2SoundCallTemplate(SourceAudioCalleeSite site, string source, int volume, int priority)
        { Site = site; PlaylistSource = source; Volume = (uint)volume; Pan = 127; Priority = (uint)priority; }
    }

    public sealed class SourceC2CallbackData
    {
        public const string Schema = "original-source-c2-callback-data/v1";
        public const string EntryProfile = "OriginalRelease57Hz-C2-InitSoundBanks-v1";
        public const string M1Profile = "OriginalRelease57Hz-C2-M1-InitSoundBanks-v1";
        private readonly Dictionary<string, ReadOnlyCollection<int>> speechTables;
        private readonly Dictionary<int, SourceC2SoundCallTemplate> soundCalls;
        public string PayloadSha256 { get; }
        public string Profile { get; }
        public string AnimationManifestSha256 { get; }
        public string LegacyAnimationInventorySha256 { get; }
        public string ExtraAnimationInventorySha256 { get; }
        public SourceAudioFunctionData HitExtensionData { get; }
        public ReadOnlyCollection<SourceC2VmBinding> Bindings { get; }
        public ReadOnlyCollection<string> AnnouncerBanks { get; }
        public ReadOnlyCollection<string> TauntBanks { get; }
        public ReadOnlyCollection<SourceC2SoundCallTemplate> CallTemplates { get; }
        public string PlayerGitBlob { get { return "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c"; } }
        public string SoundsGitBlob { get { return "f5ac3fd2cec5c20998bb613066a355e0d0d4d0e9"; } }

        private SourceC2CallbackData(string hash, string profile, string inventory, string legacy, string extra,
            SourceAudioFunctionData hit, List<SourceC2VmBinding> bindings,
            Dictionary<string, ReadOnlyCollection<int>> tables, string[] announcer, string[] taunt,
            List<SourceC2SoundCallTemplate> calls)
        {
            PayloadSha256 = hash; Profile = profile; AnimationManifestSha256 = inventory;
            LegacyAnimationInventorySha256 = legacy; ExtraAnimationInventorySha256 = extra;
            HitExtensionData = hit; Bindings = bindings.AsReadOnly(); speechTables = tables;
            AnnouncerBanks = Array.AsReadOnly(announcer); TauntBanks = Array.AsReadOnly(taunt);
            CallTemplates = calls.AsReadOnly(); soundCalls = new Dictionary<int, SourceC2SoundCallTemplate>();
            foreach (SourceC2SoundCallTemplate call in calls) soundCalls.Add(call.Site.SourceLine, call);
        }

        public SourceC2SoundCallTemplate Call(int originalLine)
        {
            SourceC2SoundCallTemplate result;
            if (!soundCalls.TryGetValue(originalLine, out result))
                throw new ArgumentException("Unknown original C2 snd_scall_bank source line.");
            return result;
        }

        public ReadOnlyCollection<int> SpeechTable(string symbol)
        {
            ReadOnlyCollection<int> result;
            if (!speechTables.TryGetValue(symbol, out result))
                throw new ArgumentException("Unknown original taunt speech table: " + symbol);
            return result;
        }

        public SourceC2VmBinding RequireBinding(SourceCallbackCue cue)
        {
            if (cue == null) throw new ArgumentNullException(nameof(cue));
            if (cue.Kind != SourcePresentationCueKind.SoundCallback)
                throw new ArgumentException("An actual sound ANI_CODE is required.");
            foreach (SourceC2VmBinding binding in Bindings)
                if (binding.Matches(cue)) return binding;
            throw new NotSupportedException("Unregistered C2 sound function/source/script/argument tuple.");
        }

        public static SourceC2CallbackData Read(byte[] utf8Json, string trustedSha256,
            SourceAudioFunctionData hitExtensionData)
        {
            if (utf8Json == null) throw new ArgumentNullException(nameof(utf8Json));
            if (hitExtensionData == null) throw new ArgumentNullException(nameof(hitExtensionData));
            RosterGuard.Hex(trustedSha256, 64, "C2 trusted payload");
            RosterGuard.Require(utf8Json.Length >= 256 && utf8Json.Length <= 128 * 1024, "Bounded C2 JSON payload required.");
            byte[] bytes = (byte[])utf8Json.Clone();
            string hash = RosterGuard.Sha256(bytes);
            RosterGuard.Require(hash == trustedSha256, "C2 payload SHA256 mismatch.");
            RosterObject root = RosterJson.Object(RosterJson.Parse(bytes), "C2");
            Exact(root, "schema", Schema);
            string profile = root.Text("profile");
            RosterGuard.Require(profile == EntryProfile || profile == M1Profile, "Unknown explicit C2 binding profile.");
            bool m1 = profile == M1Profile;
            RosterObject source = root.Object("source");
            Exact(source, "repository", "historicalsource/nflblitz2000");
            Exact(source, "revision", "89f840d58d152a2a9f230cb8d0cdffbd554c8ded");
            Exact(source, "tree", "a7e0352125d46075b7d98e87253ab89b9160a4c7");
            RosterObject blobs = source.Object("blobs");
            Exact(blobs, "player.c", "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c");
            Exact(blobs, "plyrseq.c", "28cef0d1082f2b0c9241930fdc8886de4c8f5438");
            Exact(blobs, "sounds.c", "f5ac3fd2cec5c20998bb613066a355e0d0d4d0e9");
            Exact(blobs, "Game.c", "a005a9b44f032080f3dd6341dd4f5eb6104d9cd7");
            Exact(blobs, "nfl.c", "15b49474fb55e8bac280b6515fcf7bf59cdf8664");
            Exact(blobs, "include/sndcalls.h", "69f6c483502fb689eb1991169e0e31858378c763");
            blobs.Done(); source.ExactInteger("source_hz", 57); source.ExactInteger("use_old_qb", 0); source.Done();
            RosterObject scheduler = root.Object("format_source");
            Exact(scheduler, "repository", "historicalsource/midwayvideo");
            Exact(scheduler, "revision", "1792293aaaea00a919b59e8c05716978e85b4333");
            Exact(scheduler, "process_blob", "967ca53910f24ddfa48a14485113a8fd0c989959");
            Exact(scheduler, "dispatcher_blob", "7b44637c3d59e2256ad099226b7ea17f108907ef");
            string legacy = Pin(scheduler, "legacy_animation_manifest_sha256");
            string extra = Pin(scheduler, "extra_animation_manifest_sha256");
            scheduler.ExactInteger("clip_count", 281); scheduler.ExactInteger("selected_frame_count", 26230);
            scheduler.Done();
            string inventory = Pin(root, "animation_manifest_sha256");
            RosterGuard.Require(legacy == "9cae962bbb861bffe4d379bc30919cf34c0238ed35be82e605253d3059cfede9" &&
                extra == "3e69a29f11c15c5d318f486824b1a3bba1f9489905e20d4c9ce76742af7a8c5f",
                "C2 requires the separately frozen real272 and real9 exports.");
            Exact(root, "legacy_sound_payload_sha256", "4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280");
            RosterGuard.Require(Pin(root, "hit_extension_payload_sha256") == hitExtensionData.PayloadSha256,
                "C2 hit-extension payload pin mismatch.");
            RosterGuard.Require(hitExtensionData.SoundsGitBlob == "f5ac3fd2cec5c20998bb613066a355e0d0d4d0e9" &&
                hitExtensionData.Bank == "players" && hitExtensionData.VmBindings.Count == 1,
                "C2 hit extension must preserve the original selector and single new ANI_CODE.");
            SourceAudioVmBinding h = hitExtensionData.VmBindings[0];
            RosterGuard.Require(h.SourcePath == "plyrseq.c" && h.ScriptSymbol == "m_qbfite_anim" &&
                h.FunctionSymbol == "get_hit_snd" && h.SourceLine == 7538 &&
                h.Argument0 == 70 && h.Argument1 == 0 && h.Argument2 == 0, "Wrong C2 hit-extension tuple.");
            RosterObject banks = root.Object("banks");
            string[] announcer = banks.Strings("announcer", 2, 2), taunt = banks.Strings("taunt", 2, 2);
            RosterGuard.Require(announcer[0] == "tkbank3" && announcer[1] == "tkbank4" &&
                taunt[0] == "taunts3" && taunt[1] == "taunts4", "Original swapped-bank order changed.");
            banks.Done();
            object[] tables = root.Array("speech_tables");
            RosterGuard.Require(tables.Length == 4, "C2 requires all four original taunt tables.");
            var speech = new Dictionary<string, ReadOnlyCollection<int>>(StringComparer.Ordinal);
            Table(tables[0], speech, "short_taunt_sp", 336, new[] { 0, 1, 2, 3, 4, 53, 51 });
            Table(tables[1], speech, "long_taunt_sp", 337, new[] { 20, 21, 22, 23, 25, 26, 50, 51, 52, 53 });
            Table(tables[2], speech, "laugh_taunt_sp", 338, new[] { 40, 41, 42 });
            Table(tables[3], speech, "histep_taunt_sp", 339, new[] { 12, 13, 4 });
            RosterObject calls = root.Object("sound_calls");
            calls.ExactInteger("taunt_volume", 235); calls.ExactInteger("pan", 127);
            calls.ExactInteger("taunt_priority", 5); calls.ExactInteger("histep_priority", 1);
            calls.ExactInteger("whistle_base", 139); calls.ExactInteger("whistle_volume", 205);
            calls.ExactInteger("whistle_priority", 1); calls.ExactInteger("immediate_whistle_volume", 235);
            calls.ExactInteger("immediate_whistle_priority", 0); calls.ExactInteger("whistle_chance", 25);
            calls.ExactInteger("whistle_sleep", 30);
            object[] rawCalls = calls.Array("sites");
            RosterGuard.Require(rawCalls.Length == 9, "Complete C2 taunt/whistle callee-site inventory required.");
            int[] callLines = { 511, 513, 515, 519, 524, 527, 6018, 1839, 2434 };
            string[] playlists = { "short_taunt_sp", "long_taunt_sp", "laugh_taunt_sp", "histep_taunt_sp",
                "11", "sp", "WHISTLE1_SND+randrng(2)", "WHISTLE1_SND+randrng(2)", "WHISTLE1_SND+randrng(2)" };
            var callTemplates = new List<SourceC2SoundCallTemplate>();
            for (int n = 0; n < rawCalls.Length; n++)
            {
                RosterObject call = RosterJson.Object(rawCalls[n], "C2.sound_calls.sites");
                string path = n < 6 ? "sounds.c" : "player.c";
                string function = n < 6 ? "taunt_speech" : n == 6 ? "whistle_proc" : n == 7 ? "_mode_qb" : "_mode_wpn";
                int volume = n == 6 ? 205 : 235, priority = n == 3 || n == 6 ? 1 : n > 6 ? 0 : 5;
                Exact(call, "path", path); Exact(call, "function", function);
                call.ExactInteger("line", callLines[n]); Exact(call, "playlist_source", playlists[n]);
                call.ExactInteger("volume", volume); call.ExactInteger("pan", 127); call.ExactInteger("priority", priority); call.Done();
                callTemplates.Add(new SourceC2SoundCallTemplate(new SourceAudioCalleeSite(path,
                    n < 6 ? "f5ac3fd2cec5c20998bb613066a355e0d0d4d0e9" : "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c",
                    function, callLines[n]), playlists[n], volume, priority));
            }
            calls.Done();
            string[] scripts = root.Strings("programs", 9, 9);
            string[] expected = { "taunt_1_anim", "taunt_2_anim", "taunt_3_anim", "taunt_4_anim",
                "m_laugh1_anim", "m_qbwhat_anim", "m_qbfite_anim", "m_mscat1_anim", "m_mscat2_anim" };
            for (int n = 0; n < scripts.Length; n++)
                RosterGuard.Require(scripts[n] == expected[n], "C2 script closure/order mismatch.");
            object[] rawBindings = root.Array("vm_bindings");
            RosterGuard.Require(rawBindings.Length == (m1 ? 14 : 9),
                "C2 requires its exact nine or explicit M1 fourteen source bindings, never implicit promotion.");
            string[] bindingScripts = { "taunt_1_anim", "taunt_2_anim", "taunt_3_anim", "taunt_4_anim",
                "m_laugh1_anim", "m_qbwhat_anim", "m_qbfite_anim", "m_mscat1_anim", "m_mscat2_anim",
                "r_histep_anim", "r_skip_anim", "r_hihand_anim", "r_bktau2_anim", "r_ezsprint_anim" };
            int[] lines = { 3144, 3169, 3194, 3219, 3307, 7568, 7538, 7459, 7484, 6373, 6401, 6428, 6480, 6508 };
            int[] arguments = { -2, -2, -2, -1, -3, 16, 70, 47, 47, -4, -4, -4, -4, -5 };
            var bindings = new List<SourceC2VmBinding>();
            for (int n = 0; n < rawBindings.Length; n++)
            {
                RosterObject binding = RosterJson.Object(rawBindings[n], "C2.vm_bindings");
                string function = n == 6 ? "get_hit_snd" : "taunt_speech";
                SourceC2SoundFunctionKind kind = n == 6 ? SourceC2SoundFunctionKind.HitFunctionExtension : SourceC2SoundFunctionKind.TauntSpeech;
                Exact(binding, "path", "plyrseq.c"); Exact(binding, "script", bindingScripts[n]);
                Exact(binding, "function", function); Exact(binding, "kind", kind.ToString());
                binding.ExactInteger("line", lines[n]);
                int[] args = binding.Integers("arguments", 3, -5, 70);
                RosterGuard.Require(args[0] == arguments[n] && args[1] == 0 && args[2] == 0, "C2 ANI_CODE argument tuple changed.");
                binding.Done();
                bindings.Add(new SourceC2VmBinding(bindingScripts[n], function, kind, lines[n], arguments[n]));
            }
            RosterObject producer = root.Object("end_producer_sites");
            producer.ExactInteger("whistle_qcreate_line", 5948); producer.ExactInteger("pid", 0);
            int[] processArgs = producer.Integers("arguments", 4, 0, 0);
            producer.ExactInteger("chance_line", 6014); producer.ExactInteger("sleep_line", 6017);
            producer.ExactInteger("emit_line", 6018); producer.ExactInteger("celebrate_line", 6007);
            producer.ExactInteger("celebrate_time", 60); producer.Done();
            root.Done();
            return new SourceC2CallbackData(hash, profile, inventory, legacy, extra, hitExtensionData, bindings,
                speech, announcer, taunt, callTemplates);
        }

        private static void Exact(RosterObject obj, string key, string value)
        { RosterGuard.Require(obj.Text(key) == value, "C2 original field mismatch: " + key); }
        private static string Pin(RosterObject obj, string key)
        { string value = obj.Text(key, 64); RosterGuard.Hex(value, 64, key); return value; }
        private static void Table(object raw, Dictionary<string, ReadOnlyCollection<int>> result,
            string symbol, int line, int[] expected)
        {
            RosterObject table = RosterJson.Object(raw, "C2.speech_tables");
            Exact(table, "symbol", symbol); table.ExactInteger("source_line", line);
            int[] values = table.Integers("values", expected.Length, 0, 255); table.Done();
            for (int n = 0; n < values.Length; n++)
                RosterGuard.Require(values[n] == expected[n], "C2 source-weighted table changed: " + symbol);
            result.Add(symbol, Array.AsReadOnly(values));
        }
    }
}
