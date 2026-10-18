#!/usr/bin/env python3
"""Versioned, input-only preparation of explicitly named immutable public recordings."""
import argparse
import gzip
import hashlib
import io
import json
import math
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
FIXTURE = ROOT / "tests/movement-callers/fixtures/canonical-public-12000.jsonl.gz"
SCHEMA = "source-public-replay-inputs/v1"
SOURCE_SHA256 = "2a028f90b9b8f9342fe67546c2fb4424d2ccc045bfcdef9363e5e4d20f5360d1"
PUBLIC_SHA256 = "a33b1765aaa6d980f4d2b18b4feec8db65102c1b42c0d97c520903770129cce6"
FIXTURE_SHA256 = "7ffc168a44fe7599982b7ab369ec63194b2dda1d3fccd3688121f4b3f8d20fd4"
HOSTS = 12000
PUBLIC_BYTES = 4494350
FIXTURE_BYTES = 43395
HISTORICAL_CASE = "canonical-public-12000"
POSITIVE_CASE = "portable-cabinet-wide-pass-clearance-12000"
POSITIVE_FIXTURE = FIXTURE.with_name(POSITIVE_CASE + ".jsonl.gz")
POSITIVE_RECIPE = FIXTURE.with_name(POSITIVE_CASE + ".recipe.json")
RECIPE_SHA256 = "1f82c109e2988215b1258590e135cd62bd7c38116901cfaaf29e76ee00ed4bbb"
RECORDINGS = {
    HISTORICAL_CASE: {
        "fixture": FIXTURE, "public_sha256": PUBLIC_SHA256, "public_bytes": PUBLIC_BYTES,
        "fixture_sha256": FIXTURE_SHA256, "fixture_bytes": FIXTURE_BYTES,
    },
    POSITIVE_CASE: {
        "fixture": POSITIVE_FIXTURE,
        "public_sha256": "830e7badd16a1712553bbb9ffc96c98000e1b5422d3dbbcaa55714fd5142eb3e",
        "public_bytes": 8534249,
        "fixture_sha256": "802bb9958e14cb3a053988057ac6e4dfd03d203a761c0a9cc3c0495f9d523aa9",
        "fixture_bytes": 66078,
    },
}
EXPECTED_RECIPE = {
    "schema": "source-public-replay-recording/v1", "case": POSITIVE_CASE,
    "epoch": "OriginalRelease57Hz", "input_profile": "contact272",
    "c2_profile": "contact281-c2-m1", "source_compatibility": "portable",
    "seed": 1, "ai_opponents": {"home": 5, "away": 11},
    "provider_profile": "source-sound-smoke/raw-vector-v1",
    "hosts": HOSTS, "commands_only": True, "native_equivalent": False,
    "jsonl": {"file": POSITIVE_CASE + ".jsonl", "bytes": RECORDINGS[POSITIVE_CASE]["public_bytes"],
              "sha256": RECORDINGS[POSITIVE_CASE]["public_sha256"]},
    "gzip": {"file": POSITIVE_FIXTURE.name, "bytes": RECORDINGS[POSITIVE_CASE]["fixture_bytes"],
             "sha256": RECORDINGS[POSITIVE_CASE]["fixture_sha256"]},
}
COMMAND_FIELDS = {"Move", "Turbo", "Action", "Play", "TargetPlayerId", "Source", "StationInputs"}
SOURCE_FIELDS = {"IsSpecified", "Action", "Play", "TargetPlayerId", "PatChoice",
                 "KickoffChoice", "AudibleSlot", "OffenseFlipped"}
PLAY_FIELDS = {"IsSpecified", "PlaySymbol", "Playbook", "SlotIndex", "PageIndex", "Pool", "PoolIndex"}
STATION_FIELDS = {"StationId", "StickX", "StickZ", "Held", "Pressed", "Released", "Command"}


def require(value, message):
    if not value:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "duplicate JSON field: " + key)
        result[key] = value
    return result


def nonfinite(value):
    raise ValueError("nonfinite JSON number: " + value)


def decode(text):
    return json.loads(text, object_pairs_hook=unique_object, parse_constant=nonfinite)


def fields(value, expected, where):
    require(type(value) is dict, where + " must be a public object")
    require(set(value) == expected, where + " public field set differs: missing=" +
            str(sorted(expected - set(value))) + ", extra=" + str(sorted(set(value) - expected)))


def integer(value, where, nullable=False):
    require(nullable and value is None or type(value) is int and -(2**31) <= value < 2**31,
            where + " must be an int32" + (" or null" if nullable else ""))


def boolean(value, where, nullable=False):
    require(type(value) is bool or nullable and value is None, where + " must be boolean")


def source_command(value, where):
    fields(value, SOURCE_FIELDS, where)
    boolean(value["IsSpecified"], where + ".IsSpecified")
    for name in ("Action", "TargetPlayerId", "PatChoice", "AudibleSlot"):
        integer(value[name], where + "." + name)
    if value["IsSpecified"]:
        require(0 <= value["Action"] <= 20, where + ".Action must be0..20 when specified")
    integer(value["KickoffChoice"], where + ".KickoffChoice", nullable=True)
    boolean(value["OffenseFlipped"], where + ".OffenseFlipped", nullable=True)
    play = value["Play"]
    fields(play, PLAY_FIELDS, where + ".Play")
    boolean(play["IsSpecified"], where + ".Play.IsSpecified")
    require(play["PlaySymbol"] is None or type(play["PlaySymbol"]) is str, "public PlaySymbol must be string/null")
    for name in ("Playbook", "SlotIndex", "PageIndex", "PoolIndex"):
        integer(play[name], where + ".Play." + name)
    integer(play["Pool"], where + ".Play.Pool", nullable=True)


def public_row(row, host):
    fields(row, {"host", "command"}, "row")
    require(type(row["host"]) is int and row["host"] == host, "contiguous host identity differs at " + str(host))
    command = row["command"]
    fields(command, COMMAND_FIELDS, "command")
    fields(command["Move"], {"X", "Z"}, "command.Move")
    for name, value in command["Move"].items():
        require(type(value) in (int, float) and math.isfinite(value), "public Move." + name + " must be finite")
    boolean(command["Turbo"], "command.Turbo")
    for name in ("Action", "Play", "TargetPlayerId"):
        integer(command[name], "command." + name)
    source_command(command["Source"], "command.Source")
    frame = command["StationInputs"]
    if frame is not None:
        fields(frame, {"Stations"}, "command.StationInputs")
        require(type(frame["Stations"]) is list and len(frame["Stations"]) <= 4, "public Stations must be a list of at most4")
        identities = set()
        for station in frame["Stations"]:
            fields(station, STATION_FIELDS, "station")
            for name in STATION_FIELDS - {"Command"}:
                integer(station[name], "station." + name)
            require(0 <= station["StationId"] < 4 and station["StationId"] not in identities,
                    "public station identity must be unique0..3")
            identities.add(station["StationId"])
            for name in ("StickX", "StickZ"):
                require(-3 <= station[name] <= 3, "station." + name + " must be-3..3")
            for name in ("Held", "Pressed", "Released"):
                require(0 <= station[name] <= 7, "station." + name + " must be ABC mask0..7")
            source_command(station["Command"], "station.Command")


def encode(rows):
    return b"".join((json.dumps(row, sort_keys=True, separators=(",", ":"), ensure_ascii=True,
                               allow_nan=False) + "\n").encode("ascii") for row in rows)


def recording(case):
    require(case in RECORDINGS, "unknown public recording identity")
    return RECORDINGS[case]


def exact_metadata(value, expected, where):
    if type(expected) is dict:
        fields(value, set(expected), where)
        for key in expected:
            exact_metadata(value[key], expected[key], where + "." + key)
    else:
        require(type(value) is type(expected) and value == expected,
                where + " differs from registered recording")


def load_recipe(path):
    path = Path(path)
    require(not path.is_symlink(), "recipe symlinks are forbidden")
    require(path.stat().st_size <= 8192, "public recipe exceeds bounded metadata size")
    raw = path.read_bytes()
    exact_metadata(decode(raw.decode("ascii")), EXPECTED_RECIPE, "recipe")
    require(sha(raw) == RECIPE_SHA256, "immutable public recipe SHA256 differs")


def audit_public(data, case=HISTORICAL_CASE):
    identity = recording(case)
    require(len(data) <= identity["public_bytes"] + 65536, "public replay exceeds bounded input size")
    require(data.endswith(b"\n"), "public JSONL must end in LF")
    rows = [decode(line) for line in data.decode("ascii").splitlines()]
    require(len(rows) == HOSTS, "exact12000 public command count required")
    for host, row in enumerate(rows):
        public_row(row, host)
    canonical = encode(rows)
    require(sha(canonical) == identity["public_sha256"], "immutable public command identity SHA256 differs")
    require(data == canonical and len(data) == identity["public_bytes"],
            "public JSONL is not the exact canonical serialization")
    return rows


def compress(data):
    target = io.BytesIO()
    with gzip.GzipFile(fileobj=target, mode="wb", filename="", mtime=0, compresslevel=9) as stream:
        stream.write(data)
    return target.getvalue()


def load_fixture(path, case=HISTORICAL_CASE, recipe=None):
    identity = recording(case)
    if case == POSITIVE_CASE:
        load_recipe(POSITIVE_RECIPE if recipe is None else recipe)
    else:
        require(recipe is None, "historical unqualified recording has no portable recipe")
    path = Path(path)
    require(not path.is_symlink(), "fixture symlinks are forbidden")
    require(path.stat().st_size <= identity["fixture_bytes"] + 65536, "fixture exceeds bounded gzip size")
    packed = path.read_bytes()
    require(len(packed) <= identity["fixture_bytes"] + 65536, "fixture exceeds bounded gzip size")
    with gzip.GzipFile(fileobj=io.BytesIO(packed), mode="rb") as stream:
        data = stream.read(identity["public_bytes"] + 65537)
    audit_public(data, case)
    require(sha(packed) == identity["fixture_sha256"] and len(packed) == identity["fixture_bytes"],
            "tracked fixture container SHA256 differs")
    return data


def output_path(path, fixture=False):
    path = Path(path).absolute()
    allowed = (ROOT / ".squad/.scratch", ROOT / "tests/movement-callers/.artifacts")
    require(".." not in path.parts and (fixture and path == FIXTURE or
            any(path.is_relative_to(base) and path != base for base in allowed)),
            "output must be the one tracked fixture or a fresh own ignored output")
    for component in (path, *path.parents):
        require(not component.is_symlink(), "output symlinks are forbidden")
        if component == ROOT:
            break
    return path


def write_new_or_verify(path, content):
    if path.exists():
        require(path.is_file() and path.read_bytes() == content, "existing output differs; never overwrite")
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("xb") as stream:
        stream.write(content)


def export(source, output):
    source = Path(source)
    require(not source.is_symlink(), "source symlinks are forbidden")
    raw = source.read_bytes()
    require(sha(raw) == SOURCE_SHA256, "exact reviewed mixed source SHA256 required")
    recorded = decode(raw)["commands"]
    rows = [{"host": row["host"], "command": row["command"]} for row in recorded]
    data = encode(rows)
    audit_public(data)
    packed = compress(data)
    require(sha(packed) == FIXTURE_SHA256, "deterministic gzip identity differs")
    write_new_or_verify(output_path(output, fixture=True), packed)
    return data


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_subparsers(dest="mode", required=True)
    author = modes.add_parser("export", help="Author-only exact projection; never run the simulation")
    author.add_argument("--source", type=Path, required=True)
    author.add_argument("--output", type=Path, default=FIXTURE)
    for mode in ("verify", "prepare"):
        command = modes.add_parser(mode, help="Audit the tracked fixture without the mixed source artifact")
        command.add_argument("--case", choices=RECORDINGS, action="append")
        command.add_argument("--fixture", type=Path)
        command.add_argument("--recipe", type=Path)
        if mode == "prepare":
            command.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    cases = getattr(args, "case", None) or [HISTORICAL_CASE]
    if len(cases) != 1:
        parser.error("duplicate --case argument")
    case = cases[0]
    identity = recording(case)
    try:
        data = (export(args.source, args.output) if args.mode == "export" else
                load_fixture(args.fixture or identity["fixture"], case, args.recipe))
        if args.mode == "prepare":
            require(args.output.suffix == ".jsonl", "prepared output must be .jsonl for ReadPublicReplay")
            write_new_or_verify(output_path(args.output), data)
        print(json.dumps({"schema": SCHEMA, "case": case, "status": "verified-public-input-only", "hosts": HOSTS,
                          "public_sha256": sha(data), "public_bytes": len(data),
                          "fixture_sha256": identity["fixture_sha256"],
                          "recipe_sha256": RECIPE_SHA256 if case == POSITIVE_CASE else None,
                          "historical_unqualified": case == HISTORICAL_CASE,
                          "simulation_executed": False}))
        return 0
    except (ValueError, OSError, EOFError) as error:
        print("Public replay preparation failed: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
