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
SOURCE_CURRENT_CASE = "portable-cabinet-wide-pass-clearance-possession-12000"
SOURCE_CURRENT_FIXTURE = FIXTURE.with_name(SOURCE_CURRENT_CASE + ".jsonl.gz")
SOURCE_CURRENT_RECIPE = FIXTURE.with_name(SOURCE_CURRENT_CASE + ".recipe.json")
SOURCE_CURRENT_RECIPE_SHA256 = "21c71120c7dfe9e674f7223eb8618e08816e69379f52a7be186e2b1dd81d74ad"
SOURCE_TIMER_CASE = "portable-cabinet-wide-pass-clearance-selection-countdown-12000"
SOURCE_TIMER_FIXTURE = FIXTURE.with_name(SOURCE_TIMER_CASE + ".jsonl.gz")
SOURCE_TIMER_RECIPE = FIXTURE.with_name(SOURCE_TIMER_CASE + ".recipe.json")
SOURCE_TIMER_RECIPE_SHA256 = "a1b817e6842ef49be096fb2e34c2977203c6c8280413479021c5ac5acaf7918b"
PRESNAP_CASE = "portable-cabinet-wide-pass-clearance-presnap-switch-12000"
PRESNAP_FIXTURE = FIXTURE.with_name(PRESNAP_CASE + ".jsonl.gz")
PRESNAP_RECIPE = FIXTURE.with_name(PRESNAP_CASE + ".recipe.json")
CORE_CURRENT_CASE = "portable-cabinet-wide-pass-clearance-selection-countdown-v4-12000"
PRESNAP_CURRENT_CASE = "portable-cabinet-wide-pass-clearance-presnap-switch-v5-12000"
CURRENT_SOURCE_SHA256 = "bdb993db6b6d04c5bdbeb55ecbb44b9279ec3f1093ccddfc645075082e9267e2"
CURRENT_GENERATIONS = {
    CORE_CURRENT_CASE: ("source-selection-countdown/v4", False),
    PRESNAP_CURRENT_CASE: ("source-defensive-presnap-switch/v5", True),
}
CURRENT_METADATA = FIXTURE.with_name("current-recording-generations.json")
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
    SOURCE_CURRENT_CASE: {
        "fixture": SOURCE_CURRENT_FIXTURE,
        "public_sha256": "33a1686f07230f0af4994da14c2b68cc439debb5cd3de9086c8829b44a9b9f0e",
        "public_bytes": 8534524,
        "fixture_sha256": "a450facedfbda9300a83f9514815e7450bd8cae42f63c7f53961c74cdde2c668",
        "fixture_bytes": 65968,
    },
    SOURCE_TIMER_CASE: {
        "fixture": SOURCE_TIMER_FIXTURE,
        "public_sha256": "35366e32021226f7a9fd37bfa2c0123cca53cf96f7faeab2fe3becdb0d274331",
        "public_bytes": 8534544,
        "fixture_sha256": "c3b2c6afdcb3255e856e496f4b19891bb0d17d174a4f4023ad49ce6e763e5f53",
        "fixture_bytes": 65991,
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
SOURCE_CURRENT_EXPECTED_RECIPE = {
    **EXPECTED_RECIPE, "case": SOURCE_CURRENT_CASE,
    "recording_generation": "possession-process-suspension/v2",
    "policy": "approved-portable-public-policy/v1",
    "controller_sha256": "e123e5db745acb3d4952dc0f56a2379eea36515b90bcd5c375dc769b797b330d",
    "source_manifest_sha256": "dce8791c9f2b49f5952898ef9a5d8812d6bbaf71b483c0c5681964ce4d1f6993",
    "jsonl": {"file": SOURCE_CURRENT_CASE + ".jsonl",
              "bytes": RECORDINGS[SOURCE_CURRENT_CASE]["public_bytes"],
              "sha256": RECORDINGS[SOURCE_CURRENT_CASE]["public_sha256"]},
    "gzip": {"file": SOURCE_CURRENT_FIXTURE.name,
             "bytes": RECORDINGS[SOURCE_CURRENT_CASE]["fixture_bytes"],
             "sha256": RECORDINGS[SOURCE_CURRENT_CASE]["fixture_sha256"]},
}
SOURCE_TIMER_EXPECTED_RECIPE = {
    **SOURCE_CURRENT_EXPECTED_RECIPE, "case": SOURCE_TIMER_CASE,
    "recording_generation": "source-selection-countdown/v3",
    "source_manifest_sha256": "edba06404a8ee526d42c0877a6d8043f377dace84c9f76ed57c988a9dc20fa2b",
    "jsonl": {"file": SOURCE_TIMER_CASE + ".jsonl",
              "bytes": RECORDINGS[SOURCE_TIMER_CASE]["public_bytes"],
              "sha256": RECORDINGS[SOURCE_TIMER_CASE]["public_sha256"]},
    "gzip": {"file": SOURCE_TIMER_FIXTURE.name,
             "bytes": RECORDINGS[SOURCE_TIMER_CASE]["fixture_bytes"],
             "sha256": RECORDINGS[SOURCE_TIMER_CASE]["fixture_sha256"]},
}
RECIPES = {
    POSITIVE_CASE: (POSITIVE_RECIPE, EXPECTED_RECIPE, RECIPE_SHA256),
    SOURCE_CURRENT_CASE: (SOURCE_CURRENT_RECIPE, SOURCE_CURRENT_EXPECTED_RECIPE, SOURCE_CURRENT_RECIPE_SHA256),
    SOURCE_TIMER_CASE: (SOURCE_TIMER_RECIPE, SOURCE_TIMER_EXPECTED_RECIPE, SOURCE_TIMER_RECIPE_SHA256),
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
    if case in CURRENT_GENERATIONS:
        recipe = current_recipe(case)
        return dict(fixture=FIXTURE.with_name(case + ".jsonl.gz"),
                    public_sha256=recipe["jsonl"]["sha256"], public_bytes=recipe["jsonl"]["bytes"],
                    fixture_sha256=recipe["gzip"]["sha256"], fixture_bytes=recipe["gzip"]["bytes"])
    if case == PRESNAP_CASE:
        recipe = presnap_recipe()
        return dict(fixture=PRESNAP_FIXTURE,public_sha256=recipe["jsonl"]["sha256"],
                    public_bytes=recipe["jsonl"]["bytes"],fixture_sha256=recipe["gzip"]["sha256"],
                    fixture_bytes=recipe["gzip"]["bytes"])
    require(case in RECORDINGS, "unknown public recording identity")
    return RECORDINGS[case]


def make_presnap_recipe(source_manifest_sha256, jsonl, packed):
    return {
        **SOURCE_TIMER_EXPECTED_RECIPE, "case": PRESNAP_CASE,
        "recording_generation": "source-defensive-presnap-switch/v4",
        "c2_profile": "contact282-c2-m1-presnap-switch",
        "stable_profile": "original-teams-contact282-c2-m1-presnap-switch",
        "factory": "SourceC2Composition.CreatePreSnapSwitchSimulation",
        "station_layout": 4,
        "source_manifest_sha256": source_manifest_sha256,
        "jsonl": {"file": PRESNAP_CASE+".jsonl","sha256":sha(jsonl),"bytes":len(jsonl)},
        "gzip": {"file": PRESNAP_FIXTURE.name,"sha256":sha(packed),"bytes":len(packed)},
    }


def make_current_recipe(case, jsonl, packed):
    require(case in CURRENT_GENERATIONS, "exact current recording generation required")
    generation, presnap = CURRENT_GENERATIONS[case]
    return {
        **SOURCE_TIMER_EXPECTED_RECIPE, "case": case, "recording_generation": generation,
        "c2_profile": "contact282-c2-m1-presnap-switch" if presnap else "contact281-c2-m1",
        "stable_profile": "original-teams-contact282-c2-m1-presnap-switch" if presnap else
                          "original-teams-contact281-c2-m1",
        "factory": "SourceC2Composition.CreatePreSnapSwitchSimulation" if presnap else
                   "SourceC2Composition.CreateM1Simulation",
        "station_layout": 4 if presnap else 2,
        "source_manifest_sha256": CURRENT_SOURCE_SHA256,
        "jsonl": {"file": case + ".jsonl", "sha256": sha(jsonl), "bytes": len(jsonl)},
        "gzip": {"file": case + ".jsonl.gz", "sha256": sha(packed), "bytes": len(packed)},
    }


def validate_current_recipe(value, case):
    import re
    fixed = make_current_recipe(case, b"", b"")
    fields(value, set(fixed), "current recipe")
    for name in set(fixed) - {"jsonl", "gzip"}:
        exact_metadata(value[name], fixed[name], "current recipe." + name)
    for name, limit in (("jsonl", 32 * 1024 * 1024), ("gzip", 2 * 1024 * 1024)):
        record = value[name]
        fields(record, {"file", "bytes", "sha256"}, "current recipe." + name)
        require(record["file"] == fixed[name]["file"] and type(record["bytes"]) is int and
                0 < record["bytes"] <= limit and type(record["sha256"]) is str and
                re.fullmatch(r"[0-9a-f]{64}", record["sha256"]) is not None,
                "current recipe requires exact bounded computed " + name + " metadata")
    return value


def current_recipe(case):
    pins = current_recipe_hashes()
    require(case in CURRENT_GENERATIONS, "exact current recording generation required")
    path = FIXTURE.with_name(case + ".recipe.json")
    require(path.is_file() and not path.is_symlink() and path.stat().st_size <= 8192,
            "current source-versioned recipe is missing")
    raw = path.read_bytes()
    require(sha(raw) == pins[case], "immutable current recipe SHA256 differs")
    return validate_current_recipe(decode(raw), case)


def current_recipe_hashes():
    import re
    require(CURRENT_METADATA.is_file() and not CURRENT_METADATA.is_symlink() and
            CURRENT_METADATA.stat().st_size <= 8192,
            "current recording is not sealed; no archived-vector fallback")
    metadata = decode(CURRENT_METADATA.read_bytes())
    fields(metadata, {"schema", "source_manifest_sha256", "recordings"}, "current generations")
    require(metadata["schema"] == "source-current-public-input-generations/v1" and
            metadata["source_manifest_sha256"] == CURRENT_SOURCE_SHA256,
            "exact current input-generation source identity required")
    fields(metadata["recordings"], set(CURRENT_GENERATIONS), "current generation inventory")
    result = {}
    for case, (generation, _) in CURRENT_GENERATIONS.items():
        entry = metadata["recordings"][case]
        fields(entry, {"recording_generation", "recipe_sha256"}, "current generation entry")
        require(entry["recording_generation"] == generation and type(entry["recipe_sha256"]) is str and
                re.fullmatch(r"[0-9a-f]{64}", entry["recipe_sha256"]) is not None,
                "exact explicit current input generation/recipe hash required")
        result[case] = entry["recipe_sha256"]
    return result


def validate_presnap_recipe(value):
    import re
    fixed = make_presnap_recipe(value.get("source_manifest_sha256"),b"",b"")
    fields(value,set(fixed),"pre-snap-switch recipe")
    for name in set(fixed)-{"source_manifest_sha256","jsonl","gzip"}:
        exact_metadata(value[name],fixed[name],"pre-snap-switch recipe."+name)
    require(type(value["source_manifest_sha256"]) is str and
            re.fullmatch(r"[0-9a-f]{64}",value["source_manifest_sha256"]) is not None,
            "new recording requires its actual frozen source-manifest SHA256")
    for name,limit in (("jsonl",32*1024*1024),("gzip",2*1024*1024)):
        record=value[name]
        fields(record,{"file","bytes","sha256"},"pre-snap-switch "+name)
        require(record["file"]==fixed[name]["file"] and type(record["bytes"]) is int and
                0<record["bytes"]<=limit and type(record["sha256"]) is str and
                re.fullmatch(r"[0-9a-f]{64}",record["sha256"]) is not None,
                "new recording requires exact bounded computed "+name+" metadata")
    return value


def presnap_recipe():
    require(PRESNAP_RECIPE.is_file() and not PRESNAP_RECIPE.is_symlink() and
            PRESNAP_RECIPE.stat().st_size<=8192,
            "The new source-frozen pre-snap-switch recording/recipe is missing; no old-vector fallback.")
    return validate_presnap_recipe(decode(PRESNAP_RECIPE.read_bytes()))


def exact_metadata(value, expected, where):
    if type(expected) is dict:
        fields(value, set(expected), where)
        for key in expected:
            exact_metadata(value[key], expected[key], where + "." + key)
    else:
        require(type(value) is type(expected) and value == expected,
                where + " differs from registered recording")


def load_recipe(path, case=POSITIVE_CASE):
    if case in CURRENT_GENERATIONS:
        expected = current_recipe(case)
        path = Path(path)
        require(not path.is_symlink() and path.stat().st_size <= 8192,
                "current recipe requires bounded regular bytes")
        raw = path.read_bytes()
        require(sha(raw) == current_recipe_hashes()[case], "immutable current recipe SHA256 differs")
        exact_metadata(validate_current_recipe(decode(raw), case), expected, "current recipe")
        return
    if case == PRESNAP_CASE:
        expected=presnap_recipe()
        raw=Path(path).read_bytes()
        require(not Path(path).is_symlink() and len(raw)<=8192 and raw==PRESNAP_RECIPE.read_bytes(),
                "explicit new recipe differs from the actual separately named tracked recording")
        exact_metadata(validate_presnap_recipe(decode(raw)),expected,"pre-snap-switch recipe")
        return
    require(case in RECIPES, "recording has no portable recipe")
    _, expected, digest = RECIPES[case]
    path = Path(path)
    require(not path.is_symlink(), "recipe symlinks are forbidden")
    require(path.stat().st_size <= 8192, "public recipe exceeds bounded metadata size")
    raw = path.read_bytes()
    exact_metadata(decode(raw.decode("ascii")), expected, "recipe")
    require(sha(raw) == digest, "immutable public recipe SHA256 differs")


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
    if case in RECIPES or case == PRESNAP_CASE or case in CURRENT_GENERATIONS:
        registered = (FIXTURE.with_name(case + ".recipe.json") if case in CURRENT_GENERATIONS else
                      PRESNAP_RECIPE if case == PRESNAP_CASE else RECIPES[case][0])
        load_recipe(registered if recipe is None else recipe, case)
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
    author.add_argument("--source", type=Path, required=True, action="append")
    author.add_argument("--output", type=Path, action="append")
    for mode in ("verify", "prepare"):
        command = modes.add_parser(mode, help="Audit the tracked fixture without the mixed source artifact")
        command.add_argument("--case", choices=tuple(RECORDINGS)+(PRESNAP_CASE, *CURRENT_GENERATIONS), action="append")
        command.add_argument("--fixture", type=Path, action="append")
        command.add_argument("--recipe", type=Path, action="append")
        if mode == "prepare":
            command.add_argument("--output", type=Path, required=True, action="append")
    args = parser.parse_args()
    for name in ("source", "fixture", "recipe", "output"):
        values = getattr(args, name, None)
        if values is not None:
            if len(values) != 1:
                parser.error("duplicate --" + name)
            setattr(args, name, values[0])
    if args.mode == "export" and args.output is None:
        args.output = FIXTURE
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
                          "recipe_sha256": sha(PRESNAP_RECIPE.read_bytes()) if case == PRESNAP_CASE else
                              current_recipe_hashes()[case] if case in CURRENT_GENERATIONS else
                              RECIPES[case][2] if case in RECIPES else None,
                          "historical_unqualified": case == HISTORICAL_CASE,
                          "simulation_executed": False}))
        return 0
    except (ValueError, OSError, EOFError) as error:
        print("Public replay preparation failed: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
