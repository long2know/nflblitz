#!/usr/bin/env python3
"""Generate source-bank sparse-slot facts; never synthesize audio for an empty slot."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import prepare as audio


def prepare(source, manifest, output):
    profile = json.loads(manifest.read_bytes())
    rows = []
    for bank in profile["banks"]:
        raw = (source / bank["sourcePath"]).read_bytes()
        if audio.git_blob(raw) != bank["sourceGitBlob"] or audio.sha256(raw) != bank["sourceSha256"]:
            raise ValueError("Original sparse-bank identity differs.")
        slots = struct.unpack_from("<H", raw, 128)[0]
        empty = [n for n in range(slots) if struct.unpack_from("<I", raw, 134 + n * 4)[0] == 0xffffffff]
        rows.append(bank["sourceGitBlob"] + "|" + str(slots) + "|" + ",".join(map(str, empty)) + "\n")
    text = "".join(rows)
    pin = hashlib.sha256(text.encode("ascii")).hexdigest()
    literals = "\n".join('            "' + row.rstrip("\n") + r'\n"' + (" +" if n < len(rows) - 1 else ";")
                         for n, row in enumerate(rows))
    code = '''using System;
using System.Text;
using System.Globalization;
namespace Blitz.AssetImport
{
    // SDK sound.c:4969-4993 preserves FFFFFFFF as "no sound call loaded for this entry".
    public static class SourceAudioSparseSlots
    {
        public const string ContentSha256 = "HASH";
        private const string Rows =
ROWS
        public static bool IsUnassigned(SourceAudioBank bank, int id)
        {
            if (bank == null || id < 0 || id >= bank.OriginalSlotCount) return false;
            RosterGuard.Require(RosterGuard.Sha256(Encoding.ASCII.GetBytes(Rows)) == ContentSha256,
                "Frozen original sparse sound-call table metadata changed.");
            foreach (string row in Rows.Split(new[] { '\\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = row.Split('|');
                if (fields[0] != bank.SourceGitBlob) continue;
                RosterGuard.Require(int.Parse(fields[1], CultureInfo.InvariantCulture) == bank.OriginalSlotCount,
                    "Original sparse bank slot count differs.");
                foreach (string value in fields[2].Split(','))
                    if (value.Length > 0 && int.Parse(value, CultureInfo.InvariantCulture) == id) return true;
                return false;
            }
            return false;
        }
    }
}
'''.replace("HASH", pin).replace("ROWS", literals)
    output.write_text(code, encoding="utf-8")
    return dict(contentSha256=pin, banks=len(rows), source="SDK FFFFFFFF unassigned table; no PCM replacement")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("source", "manifest", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(prepare(args.source, args.manifest, args.output), indent=2))
