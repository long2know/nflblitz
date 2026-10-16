#!/usr/bin/env python3
"""Convert explicitly supplied, pinned functional playbook fields; never execute C."""

import argparse
import ast
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import sys


HERE = Path(__file__).resolve().parent
REPOSITORY = HERE.parents[1]
RECIPE_PATH = HERE / "recipe.json"
OPCODE_WIDTHS = {
    1: 1, 2: 2, 3: 2, 4: 2, 5: 1, 6: 1, 7: 1, 8: 1, 9: 1, 10: 4,
    11: 1, 12: 1, 13: 1, 14: 1, 15: 3, 16: 2, 17: 4, 18: 2, 19: 1, 20: 2,
}
COMMENT_OR_LITERAL = re.compile(
    r'"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*.*?\*/', re.S
)
CONSTANT_TOKEN = re.compile(
    r"\s*(?:(0[xX][0-9a-fA-F]+[uUlL]*)|"
    r"((?:\d+\.\d*|\.\d+|\d+)(?:[eE][+-]?\d+)?[fFuUlL]*)|"
    r"([A-Za-z_][A-Za-z_0-9]*)|(<<|>>|[()+\-*/|&~]))"
)


class ConversionError(ValueError):
    pass


def blob_sha1(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()


def f32(value):
    try:
        result = struct.unpack("<f", struct.pack("<f", value))[0]
    except (OverflowError, struct.error) as error:
        raise ConversionError("Constant is outside binary32.") from error
    if not math.isfinite(result):
        raise ConversionError("Non-finite functional constant.")
    return result


def strip_comments(text):
    def replace(match):
        value = match[0]
        return re.sub(r"[^\n]", " ", value) if value.startswith(("//", "/*")) else value
    return COMMENT_OR_LITERAL.sub(replace, text)


class Number:
    def __init__(self, value, kind="int"):
        self.kind = kind
        self.value = f32(value) if kind == "float" else value
        if not isinstance(self.value, int) and not math.isfinite(self.value):
            raise ConversionError("Non-finite constant expression.")


class Constants:
    """Small arithmetic whitelist with C integer division and float promotions."""

    def __init__(self):
        self.symbols = {}

    def evaluate(self, expression):
        expression = expression.strip()
        tokens, literals, at = [], {}, 0
        while at < len(expression):
            match = CONSTANT_TOKEN.match(expression, at)
            if match is None:
                raise ConversionError("Unsupported constant syntax: " + expression)
            hexadecimal, decimal, identifier, operator = match.groups()
            if hexadecimal is not None:
                literal = Number(int(re.sub(r"[uUlL]+$", "", hexadecimal), 16))
            elif decimal is not None:
                suffix = re.search(r"[fFuUlL]+$", decimal)
                suffix = suffix[0] if suffix else ""
                value = decimal[:-len(suffix)] if suffix else decimal
                is_float = "." in value or "e" in value.lower() or "f" in suffix.lower()
                literal = Number(float(value), "float" if "f" in suffix.lower() else "double") if is_float else Number(int(value))
            else:
                tokens.append(identifier or operator)
                at = match.end()
                continue
            key = "__literal_" + str(len(literals))
            literals[key] = literal
            tokens.append(key)
            at = match.end()
        try:
            root = ast.parse(" ".join(tokens), mode="eval").body
        except SyntaxError as error:
            raise ConversionError("Malformed constant: " + expression) from error

        def walk(node):
            if isinstance(node, ast.Name):
                if node.id in literals:
                    return literals[node.id]
                if node.id in self.symbols:
                    return self.symbols[node.id]
                raise ConversionError("Unknown functional constant: " + node.id)
            if isinstance(node, ast.UnaryOp) and isinstance(node.op, (ast.USub, ast.UAdd, ast.Invert)):
                operand = walk(node.operand)
                if isinstance(node.op, ast.Invert):
                    if operand.kind != "int":
                        raise ConversionError("Bitwise float constant.")
                    return Number(~operand.value)
                return Number(-operand.value if isinstance(node.op, ast.USub) else operand.value, operand.kind)
            if isinstance(node, ast.BinOp):
                left, right = walk(node.left), walk(node.right)
                kind = "double" if "double" in (left.kind, right.kind) else "float" if "float" in (left.kind, right.kind) else "int"
                a, b = left.value, right.value
                if isinstance(node.op, ast.Add):
                    value = a + b
                elif isinstance(node.op, ast.Sub):
                    value = a - b
                elif isinstance(node.op, ast.Mult):
                    value = a * b
                elif isinstance(node.op, ast.Div):
                    if b == 0:
                        raise ConversionError("Division by zero in functional constant.")
                    value = (abs(a) // abs(b)) * (-1 if (a < 0) != (b < 0) else 1) if kind == "int" else a / b
                elif isinstance(node.op, (ast.BitOr, ast.BitAnd, ast.LShift, ast.RShift)):
                    if kind != "int":
                        raise ConversionError("Bitwise float constant.")
                    if isinstance(node.op, ast.BitOr):
                        value = a | b
                    elif isinstance(node.op, ast.BitAnd):
                        value = a & b
                    else:
                        if b < 0 or b > 31:
                            raise ConversionError("Invalid 32-bit constant shift.")
                        value = a << b if isinstance(node.op, ast.LShift) else a >> b
                else:
                    raise ConversionError("Unsupported constant operator: " + expression)
                return Number(value, kind)
            raise ConversionError("Only numeric fields, not calls or executable C, are accepted.")
        return walk(root)

    def integer(self, expression):
        value = int(self.evaluate(expression).value)
        if value < -2147483648 or value > 2147483647:
            raise ConversionError("Functional integer is outside the runtime's signed 32-bit field.")
        return value

    def floating(self, expression):
        return f32(self.evaluate(expression).value)


def parse_constants(documents):
    constants, pending = Constants(), []
    for role in ("playstuf_header", "player_header"):
        pending.extend(re.findall(
            r"^\s*#define\s+((?:DS|DF|DZ|LU|PLF|MODE)_[A-Za-z_0-9]+)\s+([^\n]+)",
            strip_comments(documents[role]), flags=re.M
        ))
    while pending:
        remaining = []
        for key, expression in pending:
            try:
                constants.symbols[key] = constants.evaluate(expression)
            except ConversionError:
                remaining.append((key, expression))
        if len(remaining) == len(pending):
            break
        pending = remaining
    return constants


def _unique(mapping, key, value):
    if key in mapping:
        raise ConversionError("Duplicate source identity: " + key)
    mapping[key] = value


def _line(text, offset):
    return text[:offset].count("\n") + 1


def _safe_field(value):
    if not value or len(value) > 512 or any(character in value for character in "\t\r\n"):
        raise ConversionError("Invalid converted identity/display field.")
    return value


def _row(kind, *values):
    return "\t".join((kind, *(str(value) for value in values)))


def _float_text(value):
    return format(value, ".9g")


def convert_documents(documents, recipe):
    """Pure producer seam; tests use explicitly synthetic documents and pins."""
    required = set(recipe["inputs"])
    if set(documents) != required:
        raise ConversionError("Source input roles do not match the recipe.")
    constants = parse_constants(documents)
    playbook, selection, slots = (strip_comments(documents[key]) for key in ("playbook", "selection", "slots"))
    pins = {key: value["blob_sha1"] for key, value in recipe["inputs"].items()}
    records = [_row("source-playbook/v1", recipe["source_revision"], recipe["profile"]["identity"])]
    display_names = {}
    for name, symbol in re.findall(r'\{\s*"([^"]+)"\s*,\s*&(\w+)', slots):
        display_names.setdefault(symbol, _safe_field(name))
    trigs_match = re.search(r"\bfloat\s+trigs\[\]\s*=\s*\{([^{}]*)\}", strip_comments(documents["trigs"]), re.S)
    directions_match = re.search(r"\bint\s+dir49_table\[\]\s*=\s*\{([^{}]*)\}", strip_comments(documents["player"]), re.S)
    if trigs_match is None or directions_match is None:
        raise ConversionError("Missing source trigs[] or dir49_table[].")
    trigs = [constants.floating(value) for value in trigs_match[1].split(",") if value.strip()]
    directions = [constants.integer(value) for value in directions_match[1].split(",") if value.strip()]
    if len(trigs) != 257 or trigs[0] != 0 or trigs[-1] != 1 or any(a >= b for a, b in zip(trigs, trigs[1:])):
        raise ConversionError("Expected the original monotone 257-entry quarter-sine table.")
    if len(directions) != 49 or directions[24] != -1 or any(value < 0 or value > 1023 for i, value in enumerate(directions) if i != 24):
        raise ConversionError("Expected the original 49-way direction fields.")
    if len(set(value for i, value in enumerate(directions) if i != 24)) != 32 or 0 not in directions:
        raise ConversionError("Expected the source's 32 distinct nonneutral directions.")
    records.append(_row("trig", pins["trigs"], pins["player"], ",".join(map(_float_text, trigs)), ",".join(map(str, directions))))
    handicap, formations, routes, plays, pools, opcode_counts = {}, {}, {}, {}, {}, {}
    text = strip_comments(documents["handicap"])
    for match in re.finditer(r"\b(int|float)\s+(\w+_ht)\[12\]\s*=\s*\{([^{}]*)\};", text, re.S):
        kind, identity, body = match.groups()
        parse = constants.integer if kind == "int" else constants.floating
        values = [parse(value) for value in body.split(",") if value.strip()]
        if len(values) != 12:
            raise ConversionError("Expected twelve handicap levels in " + identity)
        _unique(handicap, identity, values)
        records.append(_row("handicap", identity, pins["handicap"], _line(text, match.start()),
                            ",".join(_float_text(value) for value in values)))
    for match in re.finditer(r"\bfline_t\s+(\w+)\[\]\s*=\s*\{(.*?)\};", playbook, re.S):
        identity, body = match.groups()
        entries = []
        for row in re.findall(r"\{([^{}]+)\}", body):
            fields = [value.strip() for value in row.split(",")]
            if len(fields) != 5 or not re.fullmatch(r"[A-Za-z_][A-Za-z_0-9]*", fields[2]):
                raise ConversionError("Malformed formation slot: " + identity)
            entries.append((constants.floating(fields[0]), constants.floating(fields[1]), fields[2],
                            constants.integer(fields[3]), constants.integer(fields[4])))
            if any(abs(value) > 1000000 for value in entries[-1][:2]) or not 0 <= entries[-1][3] <= 10 or entries[-1][4] & ~0x1df:
                raise ConversionError("Invalid source coordinate, role or control mask in " + identity)
        if len(entries) != 7:
            raise ConversionError("Expected seven source formation roles in " + identity)
        _unique(formations, identity, entries)
        encoded = ";".join(",".join(( _float_text(row[0]), _float_text(row[1]), row[2], str(row[3]), str(row[4]))) for row in entries)
        records.append(_row("formation", identity, pins["playbook"], _line(playbook, match.start()), encoded))
    for match in re.finditer(r"\bint\s+(\w+)\[\]\s*=\s*\{([^{}]*)\};", playbook, re.S):
        identity, body = match.groups()
        try:
            words = [constants.integer(value) for value in body.split(",") if value.strip()]
            at = 0
            if not words or len(words) > 16384:
                raise ConversionError("Empty or oversized route.")
            while at < len(words):
                opcode = words[at]
                if opcode not in OPCODE_WIDTHS or at + OPCODE_WIDTHS[opcode] > len(words):
                    raise ConversionError("Unknown/truncated opcode " + str(opcode) + " at word " + str(at))
                opcode_counts[str(opcode)] = opcode_counts.get(str(opcode), 0) + 1
                at += OPCODE_WIDTHS[opcode]
        except ConversionError as error:
            raise ConversionError(identity + ":" + str(_line(playbook, match.start())) + ": " + str(error)) from error
        _unique(routes, identity, words)
        records.append(_row("route", identity, pins["playbook"], _line(playbook, match.start()), ",".join(map(str, words))))
    for match in re.finditer(r"\bplay_t\s+(\w+)\s*=\s*\{\s*(\w+)\s*,([^{}]*),\s*\{([^{}]*)\}\s*\};", playbook, re.S):
        identity, formation, flags, route_fields = match.groups()
        route_names = [value.strip() for value in route_fields.split(",") if value.strip()]
        if formation not in formations or len(route_names) != 7 or any(name not in routes for name in route_names):
            raise ConversionError("Missing formation/route identity in " + identity)
        side = ("Defense" if formation.startswith("def_") else "KickReturn" if "kick_return" in formation
                else "Kickoff" if "kickoff" in formation else "Offense")
        play_flags = constants.integer(flags)
        if play_flags & ~0xffff:
            raise ConversionError("Unknown play flags in " + identity)
        _unique(plays, identity, (formation, side, play_flags, route_names))
        records.append(_row("play", identity, display_names.get(identity, identity), pins["playbook"],
                            _line(playbook, match.start()), side, play_flags, formation, ",".join(route_names)))
    families = {value["symbol"]: (key, value["count"]) for key, value in recipe["weighted_pools"].items()}
    for match in re.finditer(r"static\s+play_t\s*\*(\w+)\[\]\s*=\s*\{([^{}]*)\};", selection, re.S):
        symbol, body = match.groups()
        if symbol not in families:
            continue
        family, count = families[symbol]
        references = [value.strip() for value in body.split(",") if value.strip()]
        if any(re.fullmatch(r"&[A-Za-z_][A-Za-z_0-9]*", value) is None for value in references):
            raise ConversionError("Unsupported non-play reference in " + symbol)
        entries = [value[1:] for value in references]
        if len(entries) != count or any(identity not in plays for identity in entries):
            raise ConversionError("Wrong weighted count or missing original play in " + symbol)
        _unique(pools, family, entries)
        records.append(_row("pool", family, symbol, pins["selection"], _line(selection, match.start()), ",".join(entries)))
    if set(pools) != set(recipe["weighted_pools"]):
        raise ConversionError("Missing original CPU weighted pool.")
    for family, identity in recipe["singleton_pools"].items():
        match = re.search(r"game_info\.team_play\[pnum\]\s*=\s*&" + re.escape(identity) + r"\b", selection)
        if identity not in plays or match is None:
            raise ConversionError("Missing original CPU singleton " + family + ":" + identity)
        _unique(pools, family, [identity])
        records.append(_row("pool", family, "cpu_pick_play:" + identity, pins["selection"], _line(selection, match.start()), identity))
    counts = {"formations": len(formations), "routes": len(routes), "plays": len(plays),
              "handicap_tables": len(handicap), "quarter_sines": len(trigs), "stick_directions": len(directions)}
    if counts != recipe["expected_counts"]:
        raise ConversionError("Parsed inventory differs from pinned metadata: " + json.dumps(counts, sort_keys=True))
    for family, entries in pools.items():
        offense = family in ("ShortOffense", "LongOffense", "Punt", "FakePunt", "FieldGoal")
        if any(plays[identity][1] != ("Offense" if offense else "Defense") for identity in entries):
            raise ConversionError("Wrong offense/defense family in " + family)
    counts["pool_entries"] = {family: len(entries) for family, entries in pools.items()}
    catalog = ("\n".join(records) + "\n").encode("utf-8")
    return catalog, counts, opcode_counts


def _under(path, parent):
    try:
        path.relative_to(parent)
        return True
    except ValueError:
        return False


def validate_output(output, source_root):
    output, source_root = Path(output).resolve(), Path(source_root).resolve()
    allowed = (REPOSITORY / ".squad/.scratch", HERE / ".artifacts")
    if not any(_under(output, root) and output != root for root in allowed):
        raise ConversionError("Output must be a child of ignored .squad/.scratch/ or tools/playbooks/.artifacts/.")
    if _under(output, source_root):
        raise ConversionError("Output must not modify the explicitly provided source root.")
    if output.exists() and not output.is_dir():
        raise ConversionError("Output must be an ignored directory, not a file.")
    return output


def read_inputs(source_root, recipe, overrides=None):
    source_root = Path(source_root).resolve()
    if not source_root.is_dir():
        raise ConversionError("Source root is not a directory: " + str(source_root))
    overrides = overrides or {}
    if set(overrides) - {"selection", "trigs"}:
        raise ConversionError("Only explicitly provided playsel/trigs path overrides are supported.")
    documents = {}
    for role, pin in recipe["inputs"].items():
        path = Path(overrides[role]) if role in overrides else source_root / pin["path"]
        if path.is_symlink() or (role not in overrides and not _under(path.resolve(), source_root)):
            raise ConversionError("Input escapes the explicit source root: " + pin["path"])
        if not path.is_file():
            raise ConversionError("Missing pinned source input " + pin["path"] + ": " + str(path))
        data = path.read_bytes()
        observed = blob_sha1(data)
        if observed != pin["blob_sha1"]:
            raise ConversionError("Source hash mismatch for " + pin["path"] + ": expected " +
                                  pin["blob_sha1"] + ", observed " + observed)
        documents[role] = data.decode("latin1")
    return documents


def convert(source_root, output, overrides=None):
    recipe_bytes = RECIPE_PATH.read_bytes()
    recipe = json.loads(recipe_bytes)
    output = validate_output(output, source_root)
    documents = read_inputs(source_root, recipe, overrides)
    catalog, counts, opcodes = convert_documents(documents, recipe)
    manifest = {
        "schema": "source-playbook-conversion/v1", "source_revision": recipe["source_revision"],
        "source_tree": recipe["source_tree"], "profile": recipe["profile"], "source_inputs": recipe["inputs"],
        "recipe_sha256": hashlib.sha256(recipe_bytes).hexdigest(),
        "converter_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "catalog": {"path": "catalog.tsv", "sha256": hashlib.sha256(catalog).hexdigest(), "format": "source-playbook/v1"},
        "counts": counts, "opcode_occurrences": opcodes,
        "data_boundary": [
            "Explicitly supplied source inputs, verified as original Git blob bytes; source root is never modified.",
            "Functional play/formation/route/handicap/direction fields only; no artwork, executable C or license files.",
            "Weighted entries retain every duplicate and original ordinal.",
            "Generated catalog stays ignored; no C compilation/evaluation, network fetch or asset import.",
            "Platform random()/rand() implementations and scheduler ordering are not supplied or guessed."
        ],
        "full_gameplay_parity_claimed": False,
    }
    output.mkdir(parents=True, exist_ok=True)
    (output / "catalog.tsv").write_bytes(catalog)
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return manifest


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True, help="Read-only pinned archive root.")
    parser.add_argument("--output", type=Path, required=True, help="Explicit ignored output directory.")
    parser.add_argument("--playsel", type=Path, help="Explicit verified playsel.c override; original blob hash is still mandatory.")
    parser.add_argument("--trigs", type=Path, help="Explicit TRIGS.H override; original blob hash is still mandatory.")
    args = parser.parse_args(argv)
    overrides = {role: path for role, path in (("selection", args.playsel), ("trigs", args.trigs)) if path is not None}
    try:
        manifest = convert(args.source_root, args.output, overrides)
    except (ConversionError, OSError, KeyError, json.JSONDecodeError) as error:
        print("Playbook conversion failed: " + str(error), file=sys.stderr)
        return 1
    print(json.dumps({"output": str(args.output), "counts": manifest["counts"], "catalog_sha256": manifest["catalog"]["sha256"]}, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
