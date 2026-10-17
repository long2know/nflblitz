#!/usr/bin/env python3
"""Extract only the two pinned original actor sound functions into ignored data."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import sys

HERE = Path(__file__).resolve().parent
PINS = HERE / "function-inputs.json"
SOURCE = HERE / ".artifacts/source/nflblitz2000"
OUTPUT = HERE / ".artifacts/functions"
MAGIC = b"SAFN\x01\0\0\0"


class FunctionDataError(ValueError):
    pass


def digest(data):
    return hashlib.sha256(data).hexdigest()


def canonical(value):
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("ascii")


def strip_comments(text):
    return re.sub(r"//[^\r\n]*|/\*.*?\*/",
                  lambda m: "".join(c if c in "\r\n" else " " for c in m[0]),
                  text, flags=re.S)


def balanced(text, start, opening="(", closing=")"):
    if text[start] != opening:
        raise FunctionDataError("missing opening delimiter")
    depth = 0
    for at in range(start, len(text)):
        depth += text[at] == opening
        depth -= text[at] == closing
        if depth == 0:
            return at + 1
    raise FunctionDataError("unterminated source expression")


def split_arguments(text):
    result, start, depth = [], 0, 0
    for at, char in enumerate(text):
        depth += char in "(["
        depth -= char in ")]"
        if depth < 0:
            raise FunctionDataError("unbalanced argument")
        if char == "," and depth == 0:
            result.append(text[start:at].strip())
            start = at + 1
    if depth:
        raise FunctionDataError("unbalanced argument")
    result.append(text[start:].strip())
    return result


def integer(expression, definitions):
    expression = expression.strip()
    if expression in definitions:
        return integer(definitions[expression], {})
    match = re.fullmatch(r"(-?(?:0x[0-9a-fA-F]+|\d+))(?:\s*-\s*(\d+))?", expression)
    if not match:
        raise FunctionDataError("unsupported constant expression: " + expression)
    return int(match[1], 0) - int(match[2] or 0)


def definitions(text):
    return {name: value.strip() for name, value in re.findall(
        r"^\s*#define\s+(\w+)[ \t]+([^\r\n]+)", strip_comments(text), re.M)}


def function_source(source, name):
    clean = strip_comments(source)
    match = re.search(r"\bvoid\s+" + re.escape(name) + r"\s*\([^;{}]*\)\s*\{", clean)
    if not match:
        raise FunctionDataError("missing function: " + name)
    end = balanced(clean, match.end() - 1, "{", "}")
    return {
        "name": name, "line": source[:match.start()].count("\n") + 1,
        "source": source[match.start():end], "clean": clean[match.start():end],
    }


def call_sites(function):
    text = function["clean"]
    result = []
    for match in re.finditer(r"\bsnd_scall_bank\s*\(", text):
        opening = text.index("(", match.start())
        end = balanced(text, opening)
        args = split_arguments(text[opening + 1:end - 1])
        if len(args) != 5 or args[0] != "plyr_bnk_str":
            raise FunctionDataError("unsupported sound call shape")
        result.append({"line": function["line"] + text[:match.start()].count("\n"), "args": args})
    return result


def encode(model):
    result = bytearray(MAGIC)
    def u(value): result.extend(struct.pack("<I", value))
    def i(value): result.extend(struct.pack("<i", value))
    def h(value): result.extend(struct.pack("<H", value))
    def string(value):
        raw = value.encode("ascii")
        if len(raw) > 512:
            raise FunctionDataError("oversized identity")
        h(len(raw)); result.extend(raw)
    for name in ["sounds.c", "include/sndcalls.h", "include/player.h", "plyrseq.c"]:
        result.extend(bytes.fromhex(model["blobs"][name]))
    string(model["bank"])
    for name in ["onscreen_flag", "line_mode", "drone_station"]: i(model[name])
    for name in ["human", "drone"]:
        for field in ["volume", "primary_priority", "secondary_priority"]: u(model[name][field])
    h(len(model["tables"]))
    for table in model["tables"]:
        string(table["name"]); u(table["line"]); h(len(table["values"]))
        for value in table["values"]: i(value)
    h(len(model["sites"]))
    for site in model["sites"]:
        for field in ["role", "function"]: string(site[field])
        u(site["line"]); string(site["table"])
        i(site["argument_index"]); i(site["volume_mode"]); u(site["volume"])
        u(site["pan"]); i(site["priority_mode"]); u(site["priority"])
    h(len(model["bindings"]))
    for binding in model["bindings"]:
        for field in ["path", "script", "function"]: string(binding[field])
        u(binding["line"])
        for value in binding["arguments"]: i(value)
    return bytes(result)


def produce(source_root=SOURCE):
    config = json.loads(PINS.read_text())
    sources, receipts = {}, {}
    for name, expected in config["blobs"].items():
        raw = (source_root / name).read_bytes()
        actual = hashlib.sha1(b"blob " + str(len(raw)).encode("ascii") + b"\0" + raw).hexdigest()
        if actual != expected:
            raise FunctionDataError(f"{name}: canonical Git blob mismatch")
        sources[name] = raw.decode("ascii")
        receipts[name] = {"git_blob": expected, "sha256": digest(raw), "bytes": len(raw)}
    defs = definitions(sources["include/sndcalls.h"])
    player_defs = definitions(sources["include/player.h"])
    station = re.search(r"#define\s+ISDRONE\(x\)\s+\(\(x\)->station\s*==\s*(-?\d+)\)",
                        sources["include/player.h"])
    if not station:
        raise FunctionDataError("ISDRONE source shape changed")
    model = {
        "schema": "source-audio-functions/v1", "blobs": config["blobs"],
        "onscreen_flag": integer(player_defs["PF_ONSCREEN"], {}),
        "line_mode": integer(player_defs["MODE_LINE"], {}),
        "drone_station": int(station[1]), "tables": [], "sites": [], "bindings": [],
        "source_inputs": receipts,
    }
    functions, tables_needed = {}, set()
    for function_name, roles in config["function_roles"].items():
        function = function_source(sources["sounds.c"], function_name)
        prefix = sources["sounds.c"][:sources["sounds.c"].index(function["source"])]
        bank_notes = re.findall(r"NOTE:\s*'(\w+)\.BNK'\s+is assumed", prefix)
        if not bank_notes:
            raise FunctionDataError("missing original assumed-bank declaration")
        bank = bank_notes[-1].lower()
        if "bank" in model and model["bank"] != bank:
            raise FunctionDataError("inconsistent original bank assumptions")
        model["bank"] = bank
        functions[function_name] = function
        calls = call_sites(function)
        if len(calls) != len(roles):
            raise FunctionDataError("active original call inventory changed")
        for role, call in zip(roles, calls):
            _, sound, volume, pan, priority = call["args"]
            table_match = re.fullmatch(
                r"(\w+)\[randrng\(sizeof\(\1\)/sizeof\(int\)\)\]", re.sub(r"\s+", "", sound))
            if table_match:
                table, argument_index = table_match[1], -1
                tables_needed.add(table)
            elif re.fullmatch(r"snd[123]", sound):
                table, argument_index = "", int(sound[-1]) - 1
            else:
                raise FunctionDataError("unrecognized original playlist expression")
            volume_mode = 1 if volume == "volume" else 0
            priority_mode = {"priority": 1, "priority2": 2}.get(priority, 0)
            model["sites"].append({
                "role": role, "function": function_name, "line": call["line"],
                "table": table, "argument_index": argument_index,
                "volume_mode": volume_mode, "volume": 0 if volume_mode else integer(volume, defs),
                "pan": integer(pan, defs), "priority_mode": priority_mode,
                "priority": 0 if priority_mode else integer(priority, defs),
            })
    clean = strip_comments(sources["sounds.c"])
    for table in sorted(tables_needed):
        match = re.search(r"static\s+int\s+" + table + r"\[\]\s*=\s*\{([^}]+)\};", clean)
        if not match:
            raise FunctionDataError("missing weighted table: " + table)
        values = [integer(value, defs) for value in match[1].split(",") if value.strip()]
        if not values or min(values) < 1:
            raise FunctionDataError("invalid original playlist table")
        model["tables"].append({"name": table, "line": clean[:match.start()].count("\n") + 1,
                                "values": values, "declaration": sources["sounds.c"][match.start():match.end()]})
    ground = functions["grnd_hit_snd"]["clean"]
    values = {}
    for field in ["volume", "priority", "priority2"]:
        expressions = re.findall(r"\b" + field + r"\s*=\s*(\w+)\s*;", ground)
        if len(expressions) != 2:
            raise FunctionDataError("ground-hit branch assignments changed")
        values[field] = [integer(expression, defs) for expression in expressions]
    for index, branch in enumerate(["human", "drone"]):
        model[branch] = {"volume": values["volume"][index], "primary_priority": values["priority"][index],
                         "secondary_priority": values["priority2"][index]}
    vm = strip_comments(sources["plyrseq.c"])
    for script in config["vm_scripts"]:
        start = re.search(r"^" + re.escape(script) + r":[^\r\n]*", vm, re.M)
        if not start:
            raise FunctionDataError("missing actual VM script")
        next_global = re.search(r"^\s*\.globl\s+", vm[start.end():], re.M)
        end = start.end() + next_global.start() if next_global else len(vm)
        block = vm[start.start():end]
        for match in re.finditer(r"\.word\s+(get_hit_snd|grnd_hit_snd),([^\r\n]+)", block):
            args = split_arguments(match[2])
            if len(args) != 3:
                raise FunctionDataError("VM callback arity")
            model["bindings"].append({
                "path": "plyrseq.c", "script": script, "function": match[1],
                "line": vm[:start.start() + match.start()].count("\n") + 1,
                "arguments": [integer(value, defs) for value in args],
            })
    if len(model["bindings"]) != 6:
        raise FunctionDataError("expected exactly the current six original VM callback sites")
    model["bindings"].sort(key=lambda x: x["line"])
    return model, functions, definitions(sources["include/player.h"]), defs


def write(name, source_root=SOURCE):
    if not re.fullmatch(r"[A-Za-z0-9_-]+", name):
        raise FunctionDataError("unsafe output name")
    model, functions, player_defs, sound_defs = produce(source_root)
    output = OUTPUT / name
    if output.exists():
        raise FunctionDataError("choose a fresh output name")
    output.mkdir(parents=True)
    payload = encode(model)
    (output / "sound-functions.safn").write_bytes(payload)
    # Declarations and unchanged routines are data products, never tracked fixtures.
    oracle = "\n".join(table["declaration"] for table in model["tables"]) + "\n"
    oracle += "\n".join('#line ' + str(functions[name]["line"]) + ' "sounds.c"\n' +
                        functions[name]["source"] for name in ["get_hit_snd", "grnd_hit_snd"])
    constants = []
    identifiers = set(re.findall(r"\b(?:P_\w+|VOLUME\d+|PRIORITY_LVL\d+|PF_ONSCREEN|MODE_LINE)\b",
                                 strip_comments(oracle)))
    for identifier in sorted(identifiers):
        constants.append(f"#define {identifier} {integer(identifier, {**sound_defs, **player_defs})}")
    constants.append("#define ISDRONE(x) ((x)->station == " + str(model["drone_station"]) + ")")
    (output / "original-functions.inc").write_text("\n".join(constants) + "\n" + oracle, newline="")
    audit = dict(model)
    audit["rights"] = json.loads(PINS.read_text())["rights"]
    audit["payload_sha256"] = digest(payload)
    audit["producer_sha256"] = digest(Path(__file__).read_bytes())
    audit["pins_sha256"] = digest(PINS.read_bytes())
    audit["unchanged_function_sha256"] = {name: digest(value["source"].encode("ascii"))
                                          for name, value in functions.items()}
    audit["oracle_include_sha256"] = digest((output / "original-functions.inc").read_bytes())
    (output / "manifest.json").write_bytes(canonical(audit))
    print(json.dumps({"output": str(output.relative_to(HERE)), "payload_sha256": digest(payload),
                      "tables": len(model["tables"]), "sites": len(model["sites"]), "vm_bindings": len(model["bindings"])}))
    return output


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--name", default="original")
    parser.add_argument("--source-root", type=Path, default=SOURCE)
    args = parser.parse_args()
    try:
        write(args.name, args.source_root)
    except (FunctionDataError, OSError, KeyError) as error:
        print("source-audio-functions: " + str(error), file=sys.stderr)
        sys.exit(1)
