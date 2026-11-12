#!/usr/bin/env python3
"""Generate closed source-call tuple integration metadata, not audio/animation assets."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct

import prepare_functions as functions

PINS = {
    "plyrseq.c": "28cef0d1082f2b0c9241930fdc8886de4c8f5438",
    "sounds.c": "f5ac3fd2cec5c20998bb613066a355e0d0d4d0e9",
    "include/sndcalls.h": "69f6c483502fb689eb1991169e0e31858378c763",
    "include/sndcalls.equ": "0096278ce26295856c57b8dc371937359d71348a",
    "detect.c": "68c239fa2e617988107fbebeb4dfeff5bbfe9233",
    "player.c": "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c",
    "ADJUST.C": "986a1e63ca3e25c03b33afa233e9b3fe34f69c9b",
}
KINDS = ("attack_snd", "get_hit_snd", "grnd_hit_snd", "taunt_speech", "tackle_spch", "plyr_open_sp", "hut_snd")


def prepare(source, output, report):
    sources = {}
    for path, pin in PINS.items():
        raw = (source / path).read_bytes()
        actual = hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()
        if actual != pin:
            raise ValueError("Original sound binding source blob differs: " + path)
        sources[path] = raw.decode("ascii")
    definitions = functions.definitions(sources["include/sndcalls.h"])
    definitions.update({name: value.strip() for name, value in re.findall(
        r"^\s*\.equ\s+(\w+)\s*,\s*([^;\r\n]+)", sources["include/sndcalls.equ"], re.M)})
    text = functions.strip_comments(sources["plyrseq.c"])
    pending_script, script, tuples = False, None, []
    for number, line in enumerate(text.splitlines(), 1):
        if line.lstrip().startswith(("#", ";")):
            continue
        if re.search(r"\.byte\s+SCRIPT\b", line):
            pending_script = True
        label = re.match(r"^([A-Za-z_]\w*):", line)
        if label and pending_script:
            script, pending_script = label[1], False
        call = re.search(r"\.word\s+(" + "|".join(KINDS) + r"),([^;\r\n]+)", line)
        if not call:
            continue
        args = functions.split_arguments(call[2])
        if script is None or len(args) != 3:
            raise ValueError("Original callback requires its SCRIPT root and exact three operands.")
        values = [functions.integer(value, definitions) for value in args]
        tuples.append(dict(script=script, function=call[1], line=number, arguments=values))
    tables = []
    c_definitions = functions.definitions(sources["include/sndcalls.h"])
    for name in ("plr_attack_snds", "qb_sacked_sp", "weak_tackle_sp", "hard_tackle_sp", "im_open_sp", "disappointed_sp",
                 "hut_snds", "qb_point_snds", "crowd_cheers_snds", "tackle_crowd_cheers_snds",
                 "monkey_tackle_sp", "qb_duck_sp", "hit_receiver_sp", "head_plow_sp", "late_hit_sp",
                 "no_swear_late_hit_sp", "hurdle_sp", "throw_ball_snds", "throw_short_sp",
                 "throw_jump_sp", "throw_med_sp", "throw_long_sp", "catch_fire_sp", "catch_bobble_sp",
                 "catch_short_sp", "catch_long_sp", "fumble_sp", "bobble_sp", "deflected_sp",
                 "clock_sp", "critical_sp", "field_goal_sp"):
        match = re.search(r"static\s+int\s+" + name + r"\[\]\s*=\s*\{(.*?)\};", sources["sounds.c"], re.S)
        if not match:
            raise ValueError("Missing exact original sound table: " + name)
        values = [functions.integer(value.strip(), c_definitions) for value in match[1].split(",") if value.strip()]
        tables.append(dict(symbol=name, line=sources["sounds.c"][:match.start()].count("\n") + 1, values=values))
    duration_source = functions.strip_comments(sources["sounds.c"])
    duration = re.search(r"static\s+float\s+team_name_sp_lengths\[NUM_TEAMS\]\s*=\s*\{(.*?)\};",
                         duration_source, re.S)
    if not duration or "#define TCNV(x)\t(((int)((float)(x) * 1.046f)) +1 +1 +1)" not in duration_source:
        raise ValueError("Original team-name duration table/TCNV expression changed.")
    f32 = lambda value: struct.unpack("<f", struct.pack("<f", value))[0]
    ticks = [int(f32(f32(float(value.strip().removesuffix("f"))) * f32(1.046))) + 3
             for value in duration[1].split(",") if value.strip()]
    if len(ticks) != 31:
        raise ValueError("Original team-name speech durations require all31 source teams.")
    tables.append(dict(symbol="team_name_sp_ticks",
                       line=duration_source[:duration.start()].count("\n") + 1, values=ticks))
    encoded = "".join(f"{e['script']}|{e['function']}|{e['line']}|" + "|".join(map(str, e["arguments"])) + "\n"
                      for e in tuples)
    table_rows = "".join(f"{t['symbol']}|{t['line']}|" + ",".join(map(str, t["values"])) + "\n" for t in tables)
    contacts = []
    contact_functions = {
        "detect.c": {"att_push", "att_dive", "att_kick", "att_late_hit", "att_spear_head", "att_head", "evade_vs_tackle"},
        "player.c": {"maybe_pop_helmet"},
    }
    contact_kinds = ("snd_scall_bank", "crowd_cheer_snds", "crowd_tackle_snds", "monkey_on_back_sp",
                     "qb_duck_speech", "hit_intended_receiver_sp", "late_hit_speech", "qcreate")
    for path, names in contact_functions.items():
        text = functions.strip_comments(sources[path])
        for definition in re.finditer(r"^\s*(?:static\s+)?(?:int|void)\s+(\w+)\s*\([^;{}]*\)\s*\{", text, re.M):
            if definition[1] not in names:
                continue
            start = definition.end()
            end, depth = start, 1
            while end < len(text) and depth:
                depth += (text[end] == "{") - (text[end] == "}")
                end += 1
            if depth:
                raise ValueError("Unclosed original contact function: " + definition[1])
            body = text[start:end - 1]
            for call in re.finditer(r"\b(" + "|".join(contact_kinds) + r")\s*\(([^;]*?)\)\s*;", body, re.S):
                args = [re.sub(r"\s+", "", value).strip('"') for value in functions.split_arguments(call[2])]
                if call[1] == "qcreate" and (len(args) != 7 or args[2] != "delay_plow_proc"):
                    continue
                contacts.append(dict(path=path, caller=definition[1], line=text[:start + call.start()].count("\n") + 1,
                                     function=call[1], arguments=args))
    if not re.search(r"\{\s*SWEAR_ADJ\s*,\s*0\s*,\s*1\s*,\s*1\s*\}", sources["ADJUST.C"]):
        raise ValueError("Original bad-language factory adjustment changed.")
    contact_rows = "".join(f"{e['path']}|{e['caller']}|{e['line']}|{e['function']}|" +
                           ",".join(e["arguments"]) + "\n" for e in contacts)
    pin = hashlib.sha256((encoded + table_rows + contact_rows).encode()).hexdigest()
    data = "\n".join('            "' + line + r'\n"' + (" +" if n < len(tuples) - 1 else ";")
                     for n, line in enumerate(encoded.splitlines()))
    table_data = "\n".join('            "' + line + r'\n"' + (" +" if n < len(tables) - 1 else ";")
                           for n, line in enumerate(table_rows.splitlines()))
    contact_data = "\n".join('            "' + line + r'\n"' + (" +" if n < len(contacts) - 1 else ";")
                             for n, line in enumerate(contact_rows.splitlines()))
    code = '''using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Blitz.Contracts;

namespace Blitz.AssetImport
{
    // Closed integration metadata generated from the pinned original ANI_CODE operands.
    public sealed class SourceOriginalSoundBindings
    {
        public const string ContentSha256 = "HASH";
        public const string SourceVmGitBlob = "28cef0d1082f2b0c9241930fdc8886de4c8f5438";
        public const string ContactGitBlob = "68c239fa2e617988107fbebeb4dfeff5bbfe9233";
        public const string PlayerGitBlob = "5744c51f30b625b9f80b92388ab49ff0ebc5bb2c";
        public const bool DefaultBadLanguageEnabled = true;
        private readonly Dictionary<int, SourceAudioVmBinding> byLine = new Dictionary<int, SourceAudioVmBinding>();
        private readonly Dictionary<string, SourceAudioWeightedTable> tables = new Dictionary<string, SourceAudioWeightedTable>();
        private readonly Dictionary<string, string[]> contactSites = new Dictionary<string, string[]>();
        public ReadOnlyCollection<SourceAudioVmBinding> Bindings { get; }
        private const string Tuples =
DATA
        private const string TableRows =
TABLE_DATA
        private const string ContactRows =
CONTACT_DATA

        private SourceOriginalSoundBindings()
        {
            RosterGuard.Require(RosterGuard.Sha256(Encoding.ASCII.GetBytes(Tuples + TableRows + ContactRows)) == ContentSha256,
                "The independently frozen original sound tuple metadata changed.");
            var bindings = new List<SourceAudioVmBinding>();
            foreach (string row in Tuples.Split(new[] { '\\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = row.Split('|');
                RosterGuard.Require(fields.Length == 6, "Invalid closed original sound tuple metadata.");
                var binding = new SourceAudioVmBinding("plyrseq.c", fields[0], fields[1],
                    int.Parse(fields[2], CultureInfo.InvariantCulture), int.Parse(fields[3], CultureInfo.InvariantCulture),
                    int.Parse(fields[4], CultureInfo.InvariantCulture), int.Parse(fields[5], CultureInfo.InvariantCulture));
                byLine.Add(binding.SourceLine, binding); bindings.Add(binding);
            }
            Bindings = bindings.AsReadOnly();
            foreach (string row in TableRows.Split(new[] { '\\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = row.Split('|'), values = fields[2].Split(',');
                var ids = new int[values.Length];
                for (int i = 0; i < ids.Length; i++) ids[i] = int.Parse(values[i], CultureInfo.InvariantCulture);
                tables.Add(fields[0], new SourceAudioWeightedTable(fields[0],
                    int.Parse(fields[1], CultureInfo.InvariantCulture), ids));
            }
            foreach (string row in ContactRows.Split(new[] { '\\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = row.Split('|');
                RosterGuard.Require(fields.Length == 5, "Invalid closed original contact sound metadata.");
                contactSites.Add(fields[0] + ":" + fields[2], fields);
            }
        }

        public void RequireContact(SourceContactSoundCue cue)
        {
            string[] fields;
            if (cue == null || !contactSites.TryGetValue(cue.Context.SourcePath + ":" +
                cue.Context.SourceLine.ToString(CultureInfo.InvariantCulture), out fields) ||
                fields[1] != cue.Context.ExecutingSymbol || fields[3] != cue.FunctionSymbol)
                throw new NotSupportedException("Unregistered original contact sound call site.");
            string[] arguments = fields[4].Length == 0 ? new string[0] : fields[4].Split(',');
            if (arguments.Length != cue.OriginalArguments.Count)
                throw new NotSupportedException("Original contact sound operand count changed.");
            for (int i = 0; i < arguments.Length; i++)
                if (arguments[i] != cue.OriginalArguments[i])
                    throw new NotSupportedException("Original contact sound operand changed.");
        }

        public SourceAudioWeightedTable Table(string symbol)
        {
            SourceAudioWeightedTable value;
            if (!tables.TryGetValue(symbol, out value))
                throw new NotSupportedException("Unregistered complete-original source sound table.");
            return value;
        }

        public static SourceOriginalSoundBindings ForCompleteOriginal303(ISourceAnimationLibrary library)
        {
            SourceTeamResourceProfile.CompleteOriginal303.RequireLibrary(library);
            return new SourceOriginalSoundBindings();
        }

        public static SourceOriginalSoundBindings ForCompleteAv306(ISourceAnimationLibrary library)
        {
            SourceTeamResourceProfile.CompleteAv306.RequireLibrary(library);
            return new SourceOriginalSoundBindings();
        }

        internal static SourceOriginalSoundBindings ForCompleteOriginalPresentation(SourceCinematicLibrary library)
        {
            bool original = library != null &&
                (library.ContentSha256 == SourceTeamResourceProfile.CompleteOriginalImpactSha256 ||
                 library.ContentSha256 == SourceTeamResourceProfile.CompleteOriginalFinalImpactSha256) &&
                library.ContactAnimationManifestSha256 == SourceTeamResourceProfile.CompleteOriginalAnimationSha256 &&
                library.C2CallbackDataSha256 == SourceTeamResourceProfile.CompleteOriginalCallbackSha256;
            bool av = library != null && library.ContentSha256 == SourceTeamResourceProfile.CompleteAv306ImpactSha256 &&
                library.ContactAnimationManifestSha256 == SourceTeamResourceProfile.CompleteAv306AnimationSha256 &&
                library.C2CallbackDataSha256 == SourceTeamResourceProfile.CompleteAv306CallbackSha256;
            if (!original && !av)
                throw new NotSupportedException("Original hut presentation requires its exact complete-original admission.");
            return new SourceOriginalSoundBindings();
        }

        public bool TryGet(SourceCallbackCue cue, out SourceAudioVmBinding binding)
        {
            binding = null;
            if (cue == null || cue.Kind != SourcePresentationCueKind.SoundCallback ||
                !byLine.TryGetValue(cue.Context.SourceLine, out binding)) return false;
            if (binding.ScriptSymbol == cue.Context.ExecutingSymbol && binding.Matches(cue)) return true;
            binding = null; return false;
        }

        public SourceAudioVmBinding Require(SourceCallbackCue cue)
        {
            SourceAudioVmBinding binding;
            if (!TryGet(cue, out binding))
                throw new NotSupportedException("Unregistered complete-original script/function/source/argument tuple.");
            return binding;
        }
    }
}
'''.replace("HASH", pin).replace("CONTACT_DATA", contact_data).replace("TABLE_DATA", table_data).replace("DATA", data)
    output.write_text(code, encoding="utf-8")
    report.write_text(json.dumps(dict(schema="closed-original-sound-tuples/v2", contentSha256=pin,
                                     sourcePins=PINS, tuples=tuples, tables=tables, contacts=contacts), indent=2) + "\n")
    return dict(contentSha256=pin, tuples=len(tuples), contacts=len(contacts), output=str(output), report=str(report))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(prepare(args.source, args.output, args.report), indent=2))


if __name__ == "__main__":
    main()
