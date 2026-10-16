"""Compiler-evaluated original tables/scan expressions; not a cabinet or full game run."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/interface"))
import prepare


def generate(source_root, tools_root, output):
    pins = json.loads((ROOT / "tools/interface/pins.json").read_bytes())
    texts, hashes = {}, {}
    for group, root, path in (("tools", tools_root, "lib/goose/switch.c"), ("source", source_root, "player.c")):
        data = prepare.assets.read_file(root, path)
        prepare.require(prepare.blob(data) == pins[group][path], "Original input oracle source blob mismatch: " + path)
        texts[path] = data.decode("ascii")
        hashes[str(root / path)] = prepare.sha256(data)
    switch = texts["lib/goose/switch.c"]
    expressions = []
    for name in ("p1234sw_FIFO[1]", "p1234sw_FIFO[0]", "p1234sw_cur", "p1234sw_close", "p1234sw_open"):
        matches = re.findall(r"^\s*" + re.escape(name) + r"\s*=\s*[^;\n]+;", switch, re.M)
        selected = [line for line in matches if ("p1234" in line and "FIFO" in line) or name == "p1234sw_cur" and "stuck_psw" in line]
        prepare.require(len(selected) == 1, "Ambiguous unchanged original switch scan expression: " + name)
        expressions.append(selected[0].strip())
    output.mkdir(parents=True, exist_ok=True)
    program = r"""
#include <stdio.h>
#include <stdint.h>
static unsigned char raw49[] = { RAW };
static unsigned char digital8[] = { DIGITAL };
static int directions[] = { DIRECTIONS };
static uint32_t p1234sw_cur = UINT32_MAX, p1234sw_FIFO[2] = { UINT32_MAX, UINT32_MAX };
static uint32_t p1234sw_close, p1234sw_open, stuck_psw = UINT32_MAX;
int main(void) {
  int i;
  printf("{\"raw49\":[");
  for (i=0;i<256;i++) printf("%s%d",i?",":"",raw49[i]);
  printf("],\"digital8\":[");
  for (i=0;i<49;i++) printf("%s%d",i?",":"",digital8[i]);
  printf("],\"directions\":[");
  for (i=0;i<49;i++) printf("%s%d",i?",":"",directions[i]);
  printf("],\"scans\":[");
  for (i=0;i<256;i++) {
    uint32_t p1234 = ~((uint32_t)((i*17u)%8u) << (4+8*(i%4)));
    SCAN
    printf("%s[%u,%u,%u,%u]",i?",":"",p1234,~p1234sw_cur,p1234sw_close,p1234sw_open);
  }
  printf("]}\n");
  return 0;
}
"""
    program = program.replace("RAW", prepare.array(switch, "xlat_49way")).replace(
        "DIGITAL", prepare.array(switch, "xlat_49to8")).replace(
        "DIRECTIONS", prepare.array(texts["player.c"], "dir49_table")).replace("SCAN", "\n".join(expressions))
    source = output / "original-input.c"
    executable = output / "original-input"
    source.write_text(program, encoding="ascii")
    environment = dict(os.environ, TMPDIR=str(output.absolute()), TMP=str(output.absolute()), TEMP=str(output.absolute()))
    compiled = subprocess.run(["gcc", "-pipe", "-std=c99", "-O0", "-Wall", "-Wextra", "-Werror", str(source), "-o", str(executable)],
                              capture_output=True, text=True, timeout=60, env=environment)
    prepare.require(compiled.returncode == 0, "Unchanged original input oracle build failed: " + compiled.stderr)
    run = subprocess.run([str(executable)], capture_output=True, text=True, timeout=60, check=True)
    value = json.loads(run.stdout)
    prepare.require(len(value["raw49"]) == 256 and len(value["scans"]) == 256, "Original C input oracle was empty.")
    oracle = output / "oracle.json"
    oracle.write_bytes(prepare.assets.json_bytes(value))
    for path, digest in hashes.items():
        prepare.require(prepare.sha256(Path(path).read_bytes()) == digest, "Original input source changed during oracle execution.")
    (output / "receipt.json").write_bytes(prepare.assets.json_bytes({
        "source_revision": pins["source_revision"], "tools_revision": pins["tools_revision"],
        "source_sha256": hashes, "cases": {"raw_bytes": 256, "directions": 49, "scan_steps": 256},
        "boundary": "Unchanged actual original table initializers and scan assignment expressions; synthetic active-low register tape. Not GPIO, MIPS, player physics or Goose process order."
    }))
    return oracle
