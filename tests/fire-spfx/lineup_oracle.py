"""Bounded original lineup flag-write window; no match, process scheduler or target ABI."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess

NFL_BLOB = "15b49474fb55e8bac280b6515fcf7bf59cdf8664"
NFL_SHA = "97013eed27a110e11f7180954ed7b7977ee7dc113c372d426392a686948ab434"
PLAYER_HEADER_BLOB = "a86b1a5b52ba842b206cd7dd0bbb26521db6dcb8"
GAME_HEADER_BLOB = "09d65c92971c32c44b1dc49fb4e4e98b371b1cd3"


def blob(raw):
    return hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()


def run(original_root, output):
    original_root, output = Path(original_root), Path(output)
    output.mkdir()
    raw = (original_root / "nfl.c").read_bytes()
    assert blob(raw) == NFL_BLOB and hashlib.sha256(raw).hexdigest() == NFL_SHA
    headers = {}
    for name, expected in (("include/player.h", PLAYER_HEADER_BLOB), ("include/GAME.H", GAME_HEADER_BLOB)):
        p = original_root / name
        assert not any(q.is_symlink() for q in (p, *p.parents))
        data = p.read_bytes()
        assert blob(data) == expected
        headers[name] = data.decode("ascii")
    text = raw.decode("ascii")
    start = re.search(r"(?m)^static void lineup_players\(\s*int side\s*\)\s*\{", text).end()
    tail = text[start:]
    patterns = (
        r"(?m)^[ \t]*int i = [^\r\n]+",
        r"(?m)^[ \t]*int j = [^\r\n]+",
        r"(?m)^[ \t]*for\([ \t]*; i < j; i\+\+ \)",
        r"(?m)^[ \t]*ppdata = player_blocks \+ i;",
        r"(?m)^[ \t]*ppdata->odata.flags &= ~\([^\r\n]*PF_FLASHWHITE[^\r\n]+",
    )
    selected, windows = [], []
    for pattern in patterns:
        match = re.search(pattern, tail)
        assert match, pattern
        selected.append(match[0])
        first, last = start + match.start(), start + match.end()
        windows.append(dict(first_line=text[:first].count("\n") + 1,
                            last_line=text[:last].count("\n") + 1,
                            start_byte=first, end_byte_exclusive=last,
                            sha256=hashlib.sha256(raw[first:last]).hexdigest(), unchanged_bytes=True))
    names = ["NUM_PERTEAM", "NUM_PLAYERS"] + re.findall(r"\bPF_\w+", selected[4])
    definitions = []
    for name in names:
        hits = []
        for header in headers.values():
            hits += re.findall(r"(?m)^#define[ \t]+" + name + r"\b[^\r\n]*", header)
        assert len(hits) == 1, (name, hits)
        definitions += hits
    code = r"""
#include <stdio.h>
#include <stdlib.h>
/* ORIGINAL_DEFINITIONS */
typedef struct { struct { unsigned flags; } odata; } fbplyr_data;
static fbplyr_data player_blocks[NUM_PLAYERS];
static int reads[NUM_PLAYERS],writes[NUM_PLAYERS],nr,nw;
static void original_lineup_flag_window(int side) {
    fbplyr_data *ppdata;
/* ORIGINAL_I */
/* ORIGINAL_J */
/* ORIGINAL_FOR */
    {
/* ORIGINAL_ADDRESS */
        reads[nr++]=i;
/* ORIGINAL_CLEAR */
        writes[nw++]=i;
    }
}
int main(int argc,char **argv) {
    if(argc!=2)return 3;
    int side=atoi(argv[1]); if(side < -1 || side > 1)return 4;
    for(int i=0;i<NUM_PLAYERS;i++)player_blocks[i].odata.flags=0x440;
    original_lineup_flag_window(side);
    printf("{\"side\":%d,\"reads\":[",side);
    for(int i=0;i<nr;i++)printf("%s%d",i?",":"",reads[i]);
    printf("],\"writes\":[");
    for(int i=0;i<nw;i++)printf("%s%d",i?",":"",writes[i]);
    printf("],\"flags\":[");
    for(int i=0;i<NUM_PLAYERS;i++)printf("%s%u",i?",":"",player_blocks[i].odata.flags);
    printf("],\"rngDelta\":0}\n");return 0;
}
"""
    for marker, value in zip(("ORIGINAL_I", "ORIGINAL_J", "ORIGINAL_FOR", "ORIGINAL_ADDRESS", "ORIGINAL_CLEAR"), selected):
        code = code.replace("/* " + marker + " */", value)
    code = code.replace("/* ORIGINAL_DEFINITIONS */", "\n".join(definitions))
    source, binary = output / "original-lineup-window.c", output / "original-lineup-window"
    source.write_text(code)
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output))
    command = ["gcc", "-std=c99", "-O0", "-pipe", "-fsanitize=undefined,bounds",
               "-fno-sanitize-recover=all", str(source), "-o", str(binary)]
    compiled = subprocess.run(command, env=env, capture_output=True, text=True, timeout=120)
    (output / "compile.log").write_text(compiled.stdout + compiled.stderr)
    if compiled.returncode:
        raise AssertionError("Original lineup window did not compile: " + compiled.stderr)
    cases = []
    for side in (0, 1, -1):
        result = subprocess.run([str(binary), str(side)], env=env, capture_output=True, text=True, timeout=20)
        if result.returncode or result.stderr:
            raise AssertionError("Original lineup window failed: " + result.stderr)
        cases.append(json.loads(result.stdout))
    report = dict(schema="original-lineup-flash-window/v1", source_path=str(original_root / "nfl.c"),
                  source_git_blob=NFL_BLOB, source_sha256=NFL_SHA, windows=windows,
                  command=command, compiler_exit=compiled.returncode, cases=cases,
                  generated_source_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                  qualification="Unchanged original loop bounds/address/flag-mask statement with actual pinned flag defines. "
                  "Declared all14flags0x440 isolates white clearing. Test records only read/write actors; no RNG/other lineup work. "
                  "Struct contains only the accessed flag field; not target layout or full match execution.")
    (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")
    return report
