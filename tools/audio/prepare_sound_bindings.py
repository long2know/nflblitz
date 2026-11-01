#!/usr/bin/env python3
"""Generate closed source-call tuple integration metadata, not audio/animation assets."""

import argparse
import hashlib
import json
from pathlib import Path
import re

import prepare_functions as functions

PINS = {
    "plyrseq.c": "28cef0d1082f2b0c9241930fdc8886de4c8f5438",
    "sounds.c": "f5ac3fd2cec5c20998bb613066a355e0d0d4d0e9",
    "include/sndcalls.h": "69f6c483502fb689eb1991169e0e31858378c763",
    "include/sndcalls.equ": "0096278ce26295856c57b8dc371937359d71348a",
}
KINDS = ("attack_snd", "get_hit_snd", "grnd_hit_snd", "taunt_speech", "tackle_spch", "plyr_open_sp")


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
    for name in ("plr_attack_snds", "qb_sacked_sp", "weak_tackle_sp", "hard_tackle_sp", "im_open_sp", "disappointed_sp"):
        match = re.search(r"static\s+int\s+" + name + r"\[\]\s*=\s*\{(.*?)\};", sources["sounds.c"], re.S)
        if not match:
            raise ValueError("Missing exact original sound table: " + name)
        values = [functions.integer(value.strip(), c_definitions) for value in match[1].split(",") if value.strip()]
        tables.append(dict(symbol=name, line=sources["sounds.c"][:match.start()].count("\n") + 1, values=values))
    encoded = "".join(f"{e['script']}|{e['function']}|{e['line']}|" + "|".join(map(str, e["arguments"])) + "\n"
                      for e in tuples)
    table_rows = "".join(f"{t['symbol']}|{t['line']}|" + ",".join(map(str, t["values"])) + "\n" for t in tables)
    pin = hashlib.sha256((encoded + table_rows).encode()).hexdigest()
    data = "\n".join('            "' + line + r'\n"' + (" +" if n < len(tuples) - 1 else ";")
                     for n, line in enumerate(encoded.splitlines()))
    table_data = "\n".join('            "' + line + r'\n"' + (" +" if n < len(tables) - 1 else ";")
                           for n, line in enumerate(table_rows.splitlines()))
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
        private readonly Dictionary<int, SourceAudioVmBinding> byLine = new Dictionary<int, SourceAudioVmBinding>();
        private readonly Dictionary<string, SourceAudioWeightedTable> tables = new Dictionary<string, SourceAudioWeightedTable>();
        public ReadOnlyCollection<SourceAudioVmBinding> Bindings { get; }
        private const string Tuples =
DATA
        private const string TableRows =
TABLE_DATA

        private SourceOriginalSoundBindings()
        {
            RosterGuard.Require(RosterGuard.Sha256(Encoding.ASCII.GetBytes(Tuples + TableRows)) == ContentSha256,
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
'''.replace("HASH", pin).replace("TABLE_DATA", table_data).replace("DATA", data)
    output.write_text(code, encoding="utf-8")
    report.write_text(json.dumps(dict(schema="closed-original-sound-tuples/v1", contentSha256=pin,
                                     sourcePins=PINS, tuples=tuples, tables=tables), indent=2) + "\n")
    return dict(contentSha256=pin, tuples=len(tuples), output=str(output), report=str(report))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(prepare(args.source, args.output, args.report), indent=2))


if __name__ == "__main__":
    main()
