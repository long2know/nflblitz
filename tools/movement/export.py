#!/usr/bin/env python3
"""Prepare pinned runtime movement tables only; no animation/header stub or runtime network loading."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

TOOL = Path(__file__).resolve().parent
ROOT = TOOL.parents[1]
ARTIFACTS = TOOL / ".artifacts"
RECIPE_PATH = TOOL / "recipe.json"
RECIPE = json.loads(RECIPE_PATH.read_text())
SCHEMA = RECIPE["output_schema"]
FLOAT_ENCODING = "ieee754-binary32-uint32-bits"
RUNTIME_FIELDS = {"Constants", "Tables", "Friction", "Defense"}


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def blob_sha1(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode("ascii") + b"\0" + data).hexdigest()


def ignored_output(path):
    path = Path(path).resolve()
    allowed = (TOOL / ".artifacts", ROOT / "tests/movement/.artifacts")
    if not path.is_relative_to(ROOT) or not any(
            not parent.is_symlink() and path.is_relative_to(parent.resolve()) and path != parent.resolve() for parent in allowed):
        raise ValueError("Converted source output must remain in tools/movement/.artifacts or tests/movement/.artifacts.")
    if path.suffix != ".json":
        raise ValueError("Runtime output must be an ignored .json file.")
    return path


def fetch_inputs():
    ignored_output(ARTIFACTS / "input-receipt.json")
    records = []
    for name, expected in RECIPE["inputs"].items():
        destination = ARTIFACTS / "original" / name
        candidates = (
            destination,
            ROOT / "tests/movement/.artifacts/original" / name,
            ROOT / "tests/source-oracles/.artifacts/original" / name,
            ROOT / ".squad/.scratch/reference/nflblitz2000" / name,
        )
        selected = None
        rejected = []
        for path in candidates:
            if not path.is_file():
                continue
            data = path.read_bytes()
            actual = blob_sha1(data)
            if actual == expected:
                selected = data
                origin = path.relative_to(ROOT).as_posix()
                break
            rejected.append({"path": path.relative_to(ROOT).as_posix(), "observed_blob": actual})
        if selected is None:
            command = ["gh", "api", "repos/" + RECIPE["source"]["repository"] + "/git/blobs/" + expected]
            process = subprocess.run(command, capture_output=True, text=True, check=True, timeout=60)
            response = json.loads(process.stdout)
            if response.get("sha") != expected or response.get("encoding") != "base64":
                raise RuntimeError("Unexpected pinned public Git blob response: " + name)
            selected = base64.b64decode(response["content"])
            origin = "Pinned public Git blob API"
        if blob_sha1(selected) != expected:
            raise RuntimeError("Runtime movement input failed its Git blob pin: " + name)
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(selected)
        records.append({"path": name, "blob": expected, "bytes": len(selected), "origin": origin, "rejected": rejected})
    receipt = {"schema": "source-movement-runtime-inputs/v1", "source": RECIPE["source"], "inputs": records}
    (ARTIFACTS / "input-receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return receipt


def verified_sources(source_root):
    source_root = Path(source_root).resolve()
    if not source_root.is_relative_to(ROOT):
        raise ValueError("Read pinned source inputs from a project-local reference cache.")
    result = {}
    for name, expected in RECIPE["inputs"].items():
        path = source_root / name
        if not path.is_file():
            raise RuntimeError("Missing pinned movement input " + name + "; use --fetch-inputs or --source-root.")
        data = path.read_bytes()
        if blob_sha1(data) != expected:
            raise RuntimeError("Pinned runtime movement source mismatch: " + name)
        result[name] = data
    return result


def array_span(data, symbol):
    matches = list(re.finditer(
        rb"(?m)^[ \t]*(?:static\s+)?(?:const\s+)?(?:char\s*\*|int|float)\s*" +
        symbol.encode("ascii") + rb"\s*(?:\[\d*\])+\s*=\s*\{", data))
    if len(matches) != 1:
        raise RuntimeError("Expected one pinned source array " + symbol)
    start = matches[0].start()
    opening = data.index(b"{", start, matches[0].end())
    depth = 0
    for token in re.finditer(rb"/\*.*?\*/|//[^\r\n]*|[{}]", data[opening:], re.S):
        if token.group() == b"{":
            depth += 1
        elif token.group() == b"}":
            depth -= 1
            if depth == 0:
                end = opening + token.end()
                if data[end:end + 1] != b";":
                    raise RuntimeError("Missing pinned array terminator: " + symbol)
                return start, end + 1
    raise RuntimeError("Unbalanced pinned movement array: " + symbol)


def selector_tables(player):
    tables = {}
    for name, rule in RECIPE["tables"].items():
        start, end = array_span(player, name)
        selected = player[start:end]
        code = re.sub(rb"/\*.*?\*/|//[^\r\n]*", b"", selected, flags=re.S)
        initializer = code[code.index(b"{") + 1:code.rindex(b"}")]
        values = re.findall(rb"\b[A-Za-z_]\w*\b", initializer)
        remainder = re.sub(rb"\b[A-Za-z_]\w*\b|[\s,]", b"", initializer)
        if remainder or len(values) != rule["count"]:
            raise RuntimeError("Unexpected pinned selector initializer: " + name)
        strings = [value.decode("ascii") for value in values]
        if rule["kind"] == "sequence":
            if any(not value.startswith("SEQ_") for value in strings):
                raise RuntimeError("Expected original sequence symbols in " + name)
            strings = [value[4:] for value in strings]
        tables[name] = strings
    return tables


def finite_bits(value):
    return type(value) is int and 0 <= value <= 0xffffffff and (value & 0x7f800000) != 0x7f800000


def validate(payload):
    if set(payload) != {"schema", "source", "float_encoding", "data"} or payload["schema"] != SCHEMA:
        raise ValueError("Unexpected runtime movement root/schema; no fixture fields are accepted.")
    expected_source = dict(RECIPE["source"], blobs=RECIPE["inputs"])
    if payload["source"] != expected_source or payload["float_encoding"] != FLOAT_ENCODING:
        raise ValueError("Runtime movement provenance or binary32 encoding mismatch.")
    data = payload["data"]
    if set(data) != RUNTIME_FIELDS:
        raise ValueError("Runtime movement data accepts only Constants/Tables/Friction/Defense; Responses/header stubs are forbidden.")
    if len(data["Constants"]) != RECIPE["constants"]["count"] or not all(finite_bits(v) for v in data["Constants"].values()):
        raise ValueError("Invalid runtime movement constants/binary32 bits.")
    if set(data["Tables"]) != set(RECIPE["tables"]):
        raise ValueError("Runtime movement selector inventory mismatch.")
    for name, rule in RECIPE["tables"].items():
        values = data["Tables"][name]
        if not isinstance(values, list) or len(values) != rule["count"] or any(
                not isinstance(value, str) or re.fullmatch(r"[A-Za-z_]\w*", value) is None for value in values):
            raise ValueError("Runtime movement table shape/identity mismatch: " + name)
    dimensions = RECIPE["dimensions"]
    if len(data["Friction"]) != dimensions["field_friction"] or not all(finite_bits(v) for v in data["Friction"]):
        raise ValueError("Runtime movement friction shape/bits mismatch.")
    if len(data["Defense"]) != dimensions["handicap_levels"]:
        raise ValueError("Runtime movement requires all twelve original handicap rows.")
    for row in data["Defense"]:
        if set(row) != {"Line", "Back", "Base", "Range", "Plow"}:
            raise ValueError("Runtime defense rows require both speed tables and exact delay/plow inputs.")
        for key in ("Line", "Back"):
            if len(row[key]) != dimensions["speed_columns"] or not all(finite_bits(v) for v in row[key]):
                raise ValueError("Runtime defense speed table must retain six binary32 columns.")
        if any(type(row[key]) is not int for key in ("Base", "Range", "Plow")):
            raise ValueError("Runtime delay/plow fields must remain source integers.")


def export(source_root, output):
    output = ignored_output(output)
    sources = verified_sources(source_root)
    selected = []
    declarations = []
    constants = []
    header = sources[RECIPE["constants"]["path"]]
    for match in re.finditer(rb"(?m)^#define[ \t]+(\w*SPEED)[ \t]+[^\r\n]*", header):
        constants.append(match.group(1).decode("ascii"))
        declarations.append(header[match.start():match.end()] + b"\n")
        selected.append({"path": RECIPE["constants"]["path"], "symbol": constants[-1],
                         "sha256": sha256(header[match.start():match.end()]), "unmodified_original_bytes": True})
    if len(constants) != RECIPE["constants"]["count"] or len(set(constants)) != len(constants):
        raise RuntimeError("Pinned movement constant inventory changed.")
    for entry in RECIPE["numeric_arrays"]:
        start, end = array_span(sources[entry["path"]], entry["symbol"])
        data = sources[entry["path"]][start:end]
        declarations.append(data + b"\n")
        selected.append({"path": entry["path"], "symbol": entry["symbol"],
                         "sha256": sha256(data), "unmodified_original_bytes": True})
    lines = [
        "#include <stdio.h>\n#include <stdint.h>\n#include <string.h>\n",
        *[value.decode("latin1") for value in declarations],
        "static uint32_t bits(float value){uint32_t result;memcpy(&result,&value,4);return result;}\n",
        'int main(void){int first=1;printf("{\\"Constants\\":{");\n',
    ]
    for name in constants:
        lines.append('if(!first)printf(",");first=0;printf("\\"%s\\":%%u",bits(%s));\n' % (name, name))
    lines.append(r'''printf("},\"Friction\":[");
for(int i=0;i<7;i++)printf("%s%u",i?",":"",bits(field_friction_mult_factor[i]));
printf("],\"Defense\":[");
for(int h=0;h<12;h++){if(h)printf(",");printf("{\"Line\":[");
for(int i=0;i<6;i++)printf("%s%u",i?",":"",bits(dline_speed_ht[h][i]));
printf("],\"Back\":[");for(int i=0;i<6;i++)printf("%s%u",i?",":"",bits(dback_speed_ht[h][i]));
printf("],\"Base\":%d,\"Range\":%d,\"Plow\":%d}",def_acceltime_ht[h][0],def_acceltime_ht[h][1],plow_ht[h]);}
printf("]}\n");return 0;}
''')
    work = output.parent / (output.stem + "-export")
    work.mkdir(parents=True, exist_ok=True)
    unit = work / "numeric-data.c"
    unit.write_text("".join(lines), encoding="latin1")
    compiler = shutil.which("gcc") or shutil.which("clang")
    if compiler is None:
        raise RuntimeError("A C compiler is required to retain actual source binary32 initialization.")
    executable = work / "numeric-data"
    scratch = work / "work"
    scratch.mkdir(exist_ok=True)
    environment = dict(os.environ, TMPDIR=str(scratch), TMP=str(scratch), TEMP=str(scratch))
    command = [compiler, "-std=c11", "-O0", "-fno-fast-math", "-ffp-contract=off",
               "-fexcess-precision=standard", str(unit), "-o", str(executable)]
    built = subprocess.run(command, env=environment, cwd=ROOT, capture_output=True, text=True, timeout=60)
    if built.returncode:
        raise RuntimeError("Pinned movement numeric initialization failed:\n" + built.stdout + built.stderr)
    values = subprocess.run([str(executable)], env=environment, cwd=ROOT, capture_output=True, text=True, check=True, timeout=30)
    data = json.loads(values.stdout)
    data["Tables"] = selector_tables(sources["player.c"])
    payload = {
        "schema": SCHEMA, "source": dict(RECIPE["source"], blobs=RECIPE["inputs"]),
        "float_encoding": FLOAT_ENCODING, "data": data,
    }
    validate(payload)
    encoded = (json.dumps(payload, sort_keys=True, indent=2) + "\n").encode("utf-8")
    output.write_bytes(encoded)
    manifest = output.with_suffix(".manifest.json")
    receipt = {
        "schema": "source-movement-runtime-manifest/v1", "runtime_schema": SCHEMA,
        "output_sha256": sha256(encoded), "data_sha256": sha256(json.dumps(data, sort_keys=True, separators=(",", ":")).encode()),
        "source": payload["source"], "recipe_sha256": sha256(RECIPE_PATH.read_bytes()),
        "producer_sha256": sha256(Path(__file__).read_bytes()), "source_selections": selected,
        "compiler_command": command, "compiler_diagnostics": built.stdout + built.stderr,
        "numeric_unit_sha256": sha256(unit.read_bytes()), "runtime_fields": sorted(RUNTIME_FIELDS),
        "animation_fixture_included": False, "raw_source_or_arrays_committed": False,
        "runtime_packaging_or_loading_implemented": False, "full_gameplay_parity_claimed": False,
    }
    manifest.write_text(json.dumps(receipt, indent=2) + "\n")
    return payload, receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fetch-inputs", action="store_true", help="Reuse verified public caches/fetch only three missing pinned public Git blobs.")
    parser.add_argument("--source-root", type=Path, default=ARTIFACTS / "original", help="Read-only project-local pinned original cache.")
    parser.add_argument("--output", type=Path, default=ARTIFACTS / "source-movement-runtime.json",
                        help="Must remain in an owned ignored .artifacts directory.")
    args = parser.parse_args()
    if args.fetch_inputs:
        fetch_inputs()
    payload, receipt = export(args.source_root, args.output)
    print(json.dumps({
        "schema": payload["schema"], "source_revision": payload["source"]["revision"],
        "output": str(args.output), "output_sha256": receipt["output_sha256"],
        "manifest": str(args.output.with_suffix(".manifest.json")),
        "runtime_fields": receipt["runtime_fields"], "animation_fixture_included": False,
        "runtime_packaging_or_loading_implemented": False,
    }, separators=(",", ":")))


if __name__ == "__main__":
    main()
