#!/usr/bin/env python3
"""One admitted, explicitly captured common-play-end component campaign."""
import argparse
import ast
import copy
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import struct
import subprocess
import sys
import time


ROOT = Path(__file__).resolve().parents[2]
B = ROOT / ".squad/.scratch/source-following-active-p1"
OUT = B / "metadata/source-remaining-play-end-stats"
PREP = OUT / "preparation"
LEDGER = PREP / "ledger"
VALIDATION = OUT / "validation/attempt-01"
SUITE = ROOT / "tests/source-remaining-play-end-stats"
STATE = "Assets/Scripts/Gameplay/SimulationState.cs"
CONTRACTS = "Assets/Scripts/Contracts/SourceMatchContracts.cs"
SNAPSHOT = "Assets/Scripts/Gameplay/GameSnapshot.cs"
CALLER = "Assets/Scripts/Gameplay/GameSimulation.Source.cs"
GUARD = "tests/contracts/run.py"
FLOW = "Assets/Scripts/Gameplay/SourceMatchFlow.cs"
FIXTURE = "tests/source-offensive-stats/Program.cs"
HARNESS = "tests/source-remaining-play-end-stats/Program.cs"
NEW_FILES = [HARNESS, "tests/source-remaining-play-end-stats/run.py",
             "tests/source-remaining-play-end-stats/test_runner.py",
             "tests/source-remaining-play-end-stats/README.md"]
AUTHORITY = {
    "contract.json": (65366, "bd09557804ffb680efd94fc3bbfe4c3a19c76f340bb00159244325b0fb45ce54"),
    "implementation-admission.json": (10134, "60a0b8636803ff79998cad84a168d19eb693885be95cdc2f60f0bf8382951d61"),
    "execution-admission.json": (10056, "696449ed53764da51768788f6a5d277543c1c34c37ca800eda42736d125b27b0"),
}
OLD = {
    STATE: (23383, "6d315ca95aa80dab60f729bf9ef0ecdcc612e17707ed1ec2c21ef786dd2a6815"),
    CONTRACTS: (89809, "3c5afaef724b40ff581503ae71b0b9df79efaf53a1364fe7c9ca4186696fcc85"),
    SNAPSHOT: (28563, "24e047331dcd5ec6955c76896b4bd3093d023049af3ce80d7ecf12263162d95b"),
    CALLER: (157386, "ddb34cac693331d566bdbca855f08fce92599952ef10a2751017a657188a73dd"),
    GUARD: (33083, "4111108696053c07106d4bf31c9bbf43df36ae090c58afd7210237d935d4217d"),
}
META_PINS = {
    "metadata/source-completed-pass-stats/parent-scoped-acceptance.json":
        (6483, "32e40e18454e2247892efe17688253b0bf4683c1804a015a31cb84c3ba9b76e6"),
    "metadata/source-completed-pass-stats/quality-review.json":
        (60499, "7225514a31c9d3d4e31d99601080498485ae85065108bd7c9b0ebb1267b5b63f"),
    "metadata/source-completed-pass-stats/preparation/production77-authored.json":
        (24527, "cef917214cd2f2152b88d6f2e228b3a5b4724386652257c970174bf90a977bc9"),
    "human-possession-time-source-preparation/tool-inputs.json":
        (524, "4ca33ccd83a123844d8851947907c8631d075ca0188c15836743beb8b42e2f24"),
    "human-possession-time-source-preparation/reference-inputs.json":
        (77089, "73826de6ae906013619081b38d6fc4419f55b388e502d6fcec35fbe932d39a8e"),
}
PYTHON = "/usr/bin/python3.12"
DOTNET = "/home/long2know/.dotnet/dotnet"
CSC = "/home/long2know/.dotnet/sdk/10.0.100/Roslyn/bincore/csc.dll"
LIMITS = {"metadata": 3, "units": 1, "drivers": 1, "csc": 33, "managed": 32, "ns21": 1}
TIMEOUTS = {"metadata": 120, "units": 180, "drivers": 1200, "csc": 120, "managed": 60}
STATS = ["returnYards", "kickReturns", "rushingYards", "rushes", "sackYards", "sacks",
         "totalYards", "passingYards", "completions", "passTouchdowns"]
UNITS = ["field-yards", "count", "field-yards", "count", "field-yards", "count",
         "field-yards", "field-yards", "count", "count"]
ZERO = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
RESULTS = {"Down": 0, "Touchdown": 1, "NewPossession": 4, "Incomplete": 5,
           "FumbledOutOfBounds": 6, "Conversion": 11}
SENTINELS = [
    ("tackles", [17, 19]), ("offensivePlays", [23, 29]), ("lostFumbles", [3, 4]),
    ("interceptions", [5, 6]), ("deflections", [7, 8]), ("fumbles", [9, 10]),
    ("bobbles", [11, 12]), ("lastReceiver", [4, 11]),
    ("consecutiveCompletions", [13, 14]), ("consecutiveLosses", [15, 16]),
]
PURE_CONSTANTS = {"CALLER_SOURCE", "FLOW_SOURCE", "CALLER_PREEDIT_SHA256",
                  "LEGACY_KICKOFF_BODY_SHA256", "LEGACY_CALL", "CURRENT_CALL",
                  "CPU_GUARD", "CSHARP_TOKEN"}
PURE_FUNCTIONS = {"static_check", "csharp_tokens", "token_values", "token_locations",
                  "matching_token", "method_region", "method_text", "plan_call",
                  "require_caller_wiring", "restore_play_end_stats_caller"}
ARRAY_INSERTION = (
    "        internal readonly int[] SourceReturnYards = new int[2];\n"
    "        internal readonly int[] SourceKickReturns = new int[2];\n"
    "        internal readonly int[] SourceRushingYards = new int[2];\n"
    "        internal readonly int[] SourceRushes = new int[2];\n"
    "        internal readonly int[] SourceSackYards = new int[2];\n"
    "        internal readonly int[] SourceSacks = new int[2];\n"
    "        internal int SourceKickReturnCatch;\n"
)
STAGE1_INSERTION = (
    '    static_check(hashlib.sha256(caller.encode("utf-8")).hexdigest() ==\n'
    '                 "6b200f1c377d6a3d7a9435541bcb3ee0a77f54ff61ec8a7fa1a0e35eef1c2884",\n'
    '                 "caller.completed-pass.exact-prior-whole-file")\n'
)
SNAPSHOT_INSERTION = (
    "                Match = new SourceMatchView(new SourcePlayEndStatsView(\n"
    "                    model.SourceReturnYards[(int)TeamId.Home],\n"
    "                    model.SourceReturnYards[(int)TeamId.Away],\n"
    "                    model.SourceKickReturns[(int)TeamId.Home],\n"
    "                    model.SourceKickReturns[(int)TeamId.Away],\n"
    "                    model.SourceRushingYards[(int)TeamId.Home],\n"
    "                    model.SourceRushingYards[(int)TeamId.Away],\n"
    "                    model.SourceRushes[(int)TeamId.Home],\n"
    "                    model.SourceRushes[(int)TeamId.Away],\n"
    "                    model.SourceSackYards[(int)TeamId.Home],\n"
    "                    model.SourceSackYards[(int)TeamId.Away],\n"
    "                    model.SourceSacks[(int)TeamId.Home],\n"
    "                    model.SourceSacks[(int)TeamId.Away]),\n"
    "                    new SourceCompletedPassStatsView(\n"
)
FIFTH_NAMES = (
    "completedPassStats passCompletions interceptions timeOfPossession configuration playKind phase "
    "period possession quarterScores stations result homePlay awayPlay selectionCountdown patChoice "
    "patStatus kickoff kickingTeam receivingTeam offenseFlipped homeGameplay awayGameplay "
    "forwardCatcherPlayerId dronesLose dronesWinning handicapRepetitionBonus compatibility "
    "compatibilityRepairCount compatibilityRepairs selectionTimer homeRandomSelection awayRandomSelection audibles"
).split()


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def utc():
    return datetime.now(timezone.utc).isoformat()


def digest(data):
    return hashlib.sha256(data).hexdigest()


def no_duplicate_keys(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON key: " + key)
        result[key] = value
    return result


def decode(data):
    return json.loads(data, object_pairs_hook=no_duplicate_keys)


def regular_bytes(path):
    path = Path(path)
    mode = path.lstat().st_mode
    require(stat.S_ISREG(mode) and not stat.S_ISLNK(mode), "Non-regular/symlink input: " + str(path))
    return path.read_bytes()


def pin(path, data=None):
    data = regular_bytes(path) if data is None else data
    return {"path": str(path), "bytes": len(data), "sha256": digest(data)}


def check_pin(path, expected):
    data = regular_bytes(path)
    size, sha = (expected["bytes"], expected["sha256"]) if isinstance(expected, dict) else expected
    require((len(data), digest(data)) == (size, sha), "Pinned input mismatch: " + str(path))
    return data


def write_bytes(path, data, exclusive=True):
    path = Path(path)
    require(path.is_relative_to(OUT), "Output outside admitted roots: " + str(path))
    require(path.is_relative_to(PREP) or path.is_relative_to(VALIDATION)
            or path.is_relative_to(OUT / "process-state") or path == OUT / "source-write-handoff.json",
            "Coordinator-owned output: " + str(path))
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("xb" if exclusive else "wb") as stream:
        stream.write(data)
    return pin(path, data)


def write_json(path, document, exclusive=True):
    return write_bytes(path, (json.dumps(document, indent=2, sort_keys=True) + "\n").encode(), exclusive)


def read_json(path):
    return decode(regular_bytes(path))


def replace_unique(text, anchor, replacement):
    require(text.count(anchor) == 1, "Mutation/inverse anchor not unique: " + anchor[:100])
    return text.replace(anchor, replacement, 1)


def validate_sources(paths, expected):
    require(len(paths) == 77 and len(set(paths)) == 77 and set(paths) == set(expected),
            "The explicit production77 closure changed")


def validate_references(rows, profile):
    count = 167 if profile == "net10.0" else 121
    paths = [row["path"] for row in rows]
    require(len(paths) == count and len(set(paths)) == count, "Reference profile count/uniqueness")
    require(all(Path(path).suffix == ".dll" for path in paths), "Unexpected reference extension")
    name = "human-possession-time-source-preparation/reference-inputs.json"
    fixed = decode(check_pin(B / name, META_PINS[name]))["profiles"][profile]["files"]
    require([(row["path"], row["bytes"], row["sha256"]) for row in rows] ==
            [(row["path"], row["bytes"], row["sha256"]) for row in fixed],
            "Reference profile differs from the exact named pinned manifest")


def pure_guard(source):
    tree = ast.parse(source)
    nodes = []
    selected = set()
    for node in tree.body:
        names = {target.id for target in node.targets if isinstance(target, ast.Name)} if isinstance(node, ast.Assign) else set()
        if names and names <= PURE_CONSTANTS:
            nodes.append(node)
            selected.update(names)
        elif isinstance(node, ast.ClassDef) and node.name == "StaticAssertionFailure":
            nodes.append(node)
            selected.add(node.name)
        elif isinstance(node, ast.FunctionDef) and node.name in PURE_FUNCTIONS:
            nodes.append(node)
            selected.add(node.name)
    require(selected == PURE_CONSTANTS | PURE_FUNCTIONS | {"StaticAssertionFailure"}, "Pure guard allowlist incomplete")
    namespace = {"hashlib": hashlib, "re": re, "__name__": "bounded_current_static_guard"}
    exec(compile(ast.Module(body=nodes, type_ignores=[]), "bounded-current-static-guard", "exec",
                 dont_inherit=True), namespace)
    return namespace


def braced_span(text, prefix, guard, following_blank=True):
    tokens = guard["csharp_tokens"](text)
    locations = guard["token_locations"](tokens, prefix)
    require(len(locations) == 1, "New authored span is not unique: " + prefix)
    first = locations[0]
    opening = first
    while tokens[opening][0] != "{":
        opening += 1
    closing = guard["matching_token"](tokens, opening, "{", "}")
    start = text.rfind("\n", 0, tokens[first][1]) + 1
    end = tokens[closing][2]
    require(text[end:end + 1] == "\n", "Authored block terminal LF")
    end += 1
    if following_blank:
        require(text[end:end + 1] == "\n", "Authored block following blank LF")
        end += 1
    return start, end


def verify_inverses(bodies, guard_source, contract):
    guard = pure_guard(guard_source)
    state_text = bodies[STATE].decode()
    accepted_start, accepted_end, _ = guard["method_region"](
        state_text, "AccumulateSourcePassCompletion",
        "SourcePlayEndInput input, SourcePlayResolution resolution, int carrierId")
    accepted = state_text[accepted_start:accepted_end].encode()
    require((len(accepted), digest(accepted)) ==
            (1461, "47082185c5cc7dc25e574b99b02021a45b2db051ec1a9683914d3a041b0d3336"),
            "Accepted completion method drift")
    inverse_state = replace_unique(state_text, ARRAY_INSERTION, "")
    for prefix in ("internal void CaptureSourceKickReturnCatch", "internal void AccumulateSourcePlayEndStats"):
        start, end = braced_span(inverse_state, prefix, guard)
        inverse_state = inverse_state[:start] + inverse_state[end:]
    contracts_text = bodies[CONTRACTS].decode()
    constructors = []
    for row in contract["preservedBlockPins"]["fiveSourceMatchViewConstructors"]:
        prefix = "public SourceMatchView ( " + row["prefix"]
        start, end = braced_span(contracts_text, prefix, guard, False)
        block = contracts_text[start:end - 1].encode()
        require((len(block), digest(block)) == (row["bytes"], row["sha256"]), "Prior constructor drift")
        constructors.append({"prefix": row["prefix"], "bytes": len(block), "sha256": digest(block)})
    start, end = braced_span(contracts_text, "public SourceMatchView ( SourcePlayEndStatsView", guard)
    new_ctor = contracts_text[start:end]
    tokens = guard["csharp_tokens"](new_ctor)
    calls = guard["token_locations"](tokens, ": this (")
    require(len(calls) == 1, "Sixth constructor delegation")
    opening = calls[0] + 2
    closing = guard["matching_token"](tokens, opening, "(", ")")
    actual = [token[0] for token in tokens[opening + 1:closing]]
    expected = []
    for index, name in enumerate(FIFTH_NAMES):
        if index:
            expected.append(",")
        expected.append(name)
    require(actual == expected, "Sixth constructor exact forwarding order")
    inverse_contracts = contracts_text[:start] + contracts_text[end:]
    start, end = braced_span(inverse_contracts, "public sealed class SourcePlayEndStatsView", guard)
    inverse_contracts = inverse_contracts[:start] + inverse_contracts[end:]
    inverse_contracts = replace_unique(inverse_contracts, "        public SourcePlayEndStatsView PlayEndStats { get; }\n", "")
    inverse_snapshot = replace_unique(bodies[SNAPSHOT].decode(), SNAPSHOT_INSERTION,
        "                Match = new SourceMatchView(new SourceCompletedPassStatsView(\n")
    inverse_caller = guard["restore_play_end_stats_caller"](bodies[CALLER].decode())
    guard["require_caller_wiring"](bodies[FLOW], bodies[CALLER])
    tree = ast.parse(guard_source)
    helpers = [node for node in tree.body if isinstance(node, ast.FunctionDef)
               and node.name == "restore_play_end_stats_caller"]
    require(len(helpers) == 1, "Only one new guard helper")
    lines = guard_source.splitlines(keepends=True)
    helper = helpers[0]
    start = sum(map(len, lines[:helper.lineno - 1]))
    end = sum(map(len, lines[:helper.end_lineno]))
    require(guard_source[end:end + 2] == "\n\n", "Guard helper exact blank boundary")
    inverse_guard = guard_source[:start] + guard_source[end + 2:]
    inverse_guard = replace_unique(inverse_guard, "    caller = restore_play_end_stats_caller(caller)\n", "")
    inverse_guard = replace_unique(inverse_guard, STAGE1_INSERTION, "")
    restored = {STATE: inverse_state, CONTRACTS: inverse_contracts, SNAPSHOT: inverse_snapshot,
                CALLER: inverse_caller, GUARD: inverse_guard}
    rows = []
    for path, text in restored.items():
        data = text.encode()
        require((len(data), digest(data)) == OLD[path], "Whole-file surgical inverse failed: " + path)
        rows.append({"path": path, "inverseBytes": len(data), "inverseSha256": digest(data)})
    return {"wholeFileInverses": rows, "fiveConstructors": constructors,
            "completionMethod": {"bytes": len(accepted), "sha256": digest(accepted)},
            "callerThreeStages": ["ddb34cac693331d566bdbca855f08fce92599952ef10a2751017a657188a73dd",
                                  "6b200f1c377d6a3d7a9435541bcb3ee0a77f54ff61ec8a7fa1a0e35eef1c2884",
                                  "726f513c0549d8bf028734c7d7c449dfbff430f8f7c244053b4cf8fdc03b8091"],
            "sixthForwardingNames": FIFTH_NAMES, "qualified": "static-only"}


def ending(side=0, **changes):
    values = {"offense": side, "initial": side, "last": side, "down": 1, "los": 30,
              "first": 90, "type": 0, "cause": 1, "ball": 0, "flags": 0,
              "x": [-78, 78][side], "fumbleX": 0, "previous": 0,
              "modelPossession": side, "displayLos": 30, "carrier": [4, 11][side],
              "savedCatch": 30}
    for key, value in changes.items():
        if value == "O":
            value = [1, 0][side]
        elif value == "QB":
            value = [3, 10][side]
        elif value == "OQB":
            value = [10, 3][side]
        elif value == "OR":
            value = [11, 4][side]
        elif isinstance(value, list):
            value = value[side]
        values[key] = value
    return values


# Input fixtures only. Expected statistics come exclusively from the frozen contract tuples.
SPECS = {
    "ordinary-kicked": dict(type=1, ball=3, initial="O", flags=0x200),
    "ordinary-punted-negative": dict(type=3, ball=2, previous=4, initial="O", flags=0x100, x=[-234,234]),
    "ordinary-both-flags-zero": dict(type=1, ball=3, initial="O", flags=0x300, savedCatch=40),
    "ordinary-touchdown-no-return": dict(type=1, ball=3, initial="O", flags=0x200, cause=3, x=[0,0]),
    "ordinary-carrier-mismatch": dict(type=1, ball=3, initial="O", flags=0x200, carrier="OR"),
    "ordinary-missing-kick-flags": dict(type=1, ball=3, initial="O"),
    "ordinary-fumble-saved-end": dict(type=1, initial="O", flags=0x200, cause=5, ball=1,
                                      x=[0,0], fumbleX=[-156,156], savedCatch=10),
    "ordinary-classifier-owner": dict(type=1, ball=3, offense="O", initial="O",
                                      modelPossession="O", flags=0x200),
    "quick-new-possession": dict(type=1, ball=3, flags=0x1200),
    "quick-touchdown": dict(type=1, ball=3, flags=0x1200, cause=3, x=[0,0]),
    "quick-no-kicked-bit": dict(type=1, ball=3, flags=0x1000),
    "quick-forward-precedence": dict(type=1, ball=3, flags=0x1240),
    "quick-carrier-mismatch": dict(type=1, ball=3, flags=0x1200, carrier="OR"),
    "quick-conversion-excluded": dict(type=4, flags=0x1200, cause=3, x=[0,0]),
    "quick-changed-side-no-kick": dict(type=1, ball=3, initial="O", flags=0x1000),
    "sack-loss": dict(carrier="QB", x=[-234,234]),
    "sack-zero": dict(carrier="QB", x=[-156,156]),
    "sack-positive-gain": dict(carrier="QB"),
    "sack-other-team-qb": dict(carrier="OQB", x=[-234,234]),
    "sack-kicked-bit-allowed": dict(carrier="QB", flags=0x200, x=[-234,234]),
    "sack-incomplete-allowed": dict(carrier="QB", cause=4, x=[-234,234]),
    "sack-possession-bit-same-owner": dict(carrier="QB", flags=0x8, x=[-234,234]),
    "sack-punted-excluded": dict(carrier="QB", flags=0x100, x=[-234,234]),
    "sack-crossed-line-becomes-rush": dict(carrier="QB", flags=0x4000),
    "sack-touchdown-no-extra-filter": dict(carrier="QB", cause=3, x=[0,0]),
    "rush-positive": {},
    "rush-negative": dict(x=[-234,234]),
    "rush-zero": dict(x=[-156,156]),
    "rush-touchdown": dict(cause=3, x=[0,0]),
    "rush-conversion": dict(type=4, cause=3, x=[0,0]),
    "rush-fourth-down-original-owner": dict(down=4, displayLos=97),
    "rush-fumble-saved-end": dict(cause=5, ball=1, x=[0,0], fumbleX=[-156,156]),
    "rush-lower-bound": dict(x=[-390,390]),
    "rush-upper-bound": dict(x=[390,-390]),
    "rush-kicked-excluded": dict(flags=0x200),
    "rush-incomplete-excluded": dict(cause=4),
    "rush-changed-side-excluded": dict(initial="O"),
    "rush-punted-excluded": dict(flags=0x100),
    "rush-possession-bit-same-owner": dict(flags=0x8),
    "ordinary-same-initial-no-quick": dict(type=1, ball=3, flags=0x200),
    "rush-no-carrier": dict(carrier=-1),
}


def field(case, suffix, unit, value):
    case["fields"].append({"name": case["name"] + "." + suffix, "unit": unit, "value": value})


def vector_fields(case, prefix, pair, order=(0, 1), rush_first=False):
    indices = [3,0,1,2,4,5,6,7,8,9] if rush_first else range(10)
    for side in order:
        for index in indices:
            field(case, prefix + "." + ["home", "away"][side] + "." + STATS[index],
                  UNITS[index], pair[side][index])


def preservation_fields(case):
    for side in (0, 1):
        prefix = "sentinels." + ["home", "away"][side] + "."
        for index, (name, values) in enumerate(SENTINELS):
            field(case, prefix + name, "actor-id" if index == 7 else "count", values[side])
        field(case, prefix + "fire", "bool", [True, False][side])
        field(case, prefix + "possessionTime", "seconds", [37, 41][side])
    field(case, "sentinels.randomDraws", "count", 0)
    field(case, "sentinels.forwardCatcher", "actor-id", 4)
    for actor in range(14):
        field(case, "actors." + str(actor) + ".sameAnimation", "bool", True)
        field(case, "actors." + str(actor) + ".primaryFrame", "frame", 0)
        field(case, "actors." + str(actor) + ".secondaryFrame", "frame", 0)


def prior_fields(case):
    for suffix, unit, value in [
        ("home.interceptions", "count", 5), ("away.interceptions", "count", 6),
        ("home.possessionTime", "seconds", 37), ("away.possessionTime", "seconds", 41),
        ("home.fire", "bool", True), ("away.fire", "bool", False),
        ("home.consecutiveCompletions", "count", 13), ("away.consecutiveCompletions", "count", 14),
        ("home.consecutiveLosses", "count", 15), ("away.consecutiveLosses", "count", 16),
    ]:
        field(case, "prior." + suffix, unit, value)


def dispatch_fields(case, result, side, end, pair):
    field(case, "classification.result", "enum", RESULTS[result])
    field(case, "classification.side", "side", side)
    field(case, "resolution.fieldEnd", "field-yards", end)
    if case["checkRespot"]:
        field(case, "resolution.playEndX", "source-world", [-156, 156][side])
    if case["checkNext"]:
        field(case, "resolution.nextSide", "side", [1, 0][side])
        field(case, "resolution.nextLos", "field-yards", 60)
    vector_fields(case, "model", pair, case["modelOrder"])
    vector_fields(case, "snapshot", pair)
    vector_fields(case, "snapshot2", pair)
    preservation_fields(case)
    prior_fields(case)


def forwarding_fields(case):
    literals = [
        ("newGroupSame","bool",True), ("completedSame","bool",True), ("completionsSame","bool",True),
        ("interceptionsSame","bool",True), ("timeSame","bool",True), ("configurationSame","bool",True),
        ("playKind","enum",0), ("phase","enum",5), ("period","period",0),
        ("possession.offense","side",0), ("possession.initial","side",1), ("possession.last","side",0),
        ("possession.first","side",1), ("possession.captain","enum",1),
        ("score.home","points",11), ("score.away","points",13), ("stations","count",0),
        ("result.kind","enum",4), ("result.homePoints","points",2), ("result.awayPoints","points",3),
        ("result.firstDown","bool",True), ("result.turnover","bool",True),
        ("homePlaySpecified","bool",False), ("awayPlaySpecified","bool",False),
        ("selectionCountdown","ticks",17), ("patChoice","enum",2), ("patStatus","enum",4),
        ("kickoff","enum",1), ("kickingTeam","side",1), ("receivingTeam","side",0), ("flipped","bool",True),
        ("homeGameplay.handicap","index",5), ("awayGameplay.handicap","index",7),
        ("homeGameplay.effective","index",6), ("awayGameplay.effective","index",8),
        ("forwardCatcher","actor-id",4), ("dronesLose","bool",True), ("dronesWinning","bool",False),
        ("repetition","count",2), ("compatibility","enum",1), ("repairCount","count",0),
        ("repairs","count",0), ("selectionTimerNull","bool",True), ("audiblesNull","bool",True),
    ]
    for suffix, unit, value in literals:
        field(case, suffix, unit, value)


def build_inventory(contract):
    table = contract["literalTestInventory"]
    cases = []
    require(set(SPECS) == {row[0] for row in table["dispatchRows"]}, "Input fixture/catalogue mismatch")
    defense_first = {"sack-loss", "sack-zero", "sack-positive-gain", "sack-other-team-qb",
                     "sack-kicked-bit-allowed", "sack-incomplete-allowed",
                     "sack-possession-bit-same-owner", "sack-touchdown-no-extra-filter"}
    for stem, source, _, result, end, own, other in table["dispatchRows"]:
        for side in (0, 1):
            pair = [None, None]
            pair[side] = list(table["outcomes"][own])
            pair[1 - side] = list(table["outcomes"][other])
            case = {"name": stem + ["-home", "-away"][side], "kind": "dispatch", "source": source,
                    "input": ending(side, **SPECS[stem]), "fields": [],
                    "checkRespot": stem in ("ordinary-fumble-saved-end", "rush-fumble-saved-end"),
                    "checkNext": stem == "rush-fourth-down-original-owner",
                    "modelOrder": [1-side, side] if stem in defense_first else [0, 1]}
            dispatch_fields(case, result, side, end, pair)
            cases.append(case)
    for side, original in enumerate(table["explicitSentinelReturns"]):
        pair = [original["home"], original["away"]]
        case = {"name": original["name"], "kind": "dispatch", "source": original["source"],
                "input": ending(side, type=1, ball=3, initial="O", flags=0x200, carrier=-1),
                "fields": [], "checkRespot": False, "checkNext": False, "modelOrder": [0, 1]}
        dispatch_fields(case, "NewPossession", side, 40, pair)
        cases.append(case)
    capture_specs = [
        [(4, True, [-156,156])], [(4, False, [-156,156])], [(3, False, [-156,156])],
        [(3, True, [0,0])], [(2, False, [0,0])], [(7, False, [0,0])],
        [(1, False, [0,0])], [(6, False, [0,0])], [(5, False, [0,0])],
        [(4, False, [-76,76])], [(4, False, [-390,390])],
        [(4, False, [-156,156]), (2, False, [0,0]), (7, False, [0,0])],
    ]
    for index, (stem, _, saved, source) in enumerate(table["captureRows"]):
        for side in (0, 1):
            pair = [list(ZERO), list(ZERO)]
            commit = ending(side, type=1, ball=3, flags=0x1200) if index == 11 else None
            if index == 11:
                pair[side] = list(table["outcomes"]["return10"])
            case = {"name": stem + ["-home", "-away"][side], "kind": "capture", "source": source,
                    "side": side, "steps": [{"kind": kind, "offense": offense, "x": xs[side]}
                                           for kind, offense, xs in capture_specs[index]],
                    "commit": commit, "fields": [], "modelOrder": [0, 1]}
            field(case, "model.savedCatch", "field-yards", saved)
            vector_fields(case, "model", pair)
            vector_fields(case, "snapshot", pair)
            vector_fields(case, "snapshot2", pair)
            preservation_fields(case)
            prior_fields(case)
            cases.append(case)
    old_names = ["configuration", "time", "interceptions", "completions", "completed-pass"]
    null_names = {"api-sixth-null-new-group": (0, "playEndStats"),
                  "api-existing-null-completed-pass": (1, "completedPassStats"),
                  "api-existing-null-completions": (2, "passCompletions"),
                  "api-existing-null-interceptions": (3, "interceptions"),
                  "api-existing-null-time": (4, "timeOfPossession"),
                  "api-existing-null-configuration": (5, "configuration")}
    negative = {"api-negative-home-kick-returns": (2, "homeKickReturns"),
                "api-negative-away-kick-returns": (3, "awayKickReturns"),
                "api-negative-home-rushes": (6, "homeRushes"),
                "api-negative-away-rushes": (7, "awayRushes"),
                "api-negative-home-sacks": (10, "homeSacks"),
                "api-negative-away-sacks": (11, "awaySacks")}
    for original in table["apiCases"]:
        name = original["name"]
        case = {"name": name, "kind": "api", "fields": [], "source": "additive DTO/API contract"}
        old = [i for i, stem in enumerate(old_names, 1) if name == "api-old-" + stem + "-overload-null-group"]
        if old:
            overload = old[0]
            case.update(operation="old", overload=overload)
            for suffix, value in [("newGroupNull", True), ("completedSame", overload >= 5),
                                  ("completionsSame", overload >= 4), ("interceptionsSame", overload >= 3),
                                  ("timeSame", overload >= 2), ("configurationSame", True)]:
                field(case, suffix, "bool", value)
            field(case, "score.home", "points", 11)
            field(case, "score.away", "points", 13)
            field(case, "stations", "count", 0)
            field(case, "selectionCountdown", "ticks", -1)
        elif name in null_names:
            operand, parameter = null_names[name]
            case.update(operation="null", nullOperand=operand)
            field(case, "exception.parameter", "parameter", parameter)
            field(case, "exception.kind", "tag", "null")
        elif name in negative:
            index, parameter = negative[name]
            case.update(operation="negative", negativeIndex=index)
            field(case, "exception.parameter", "parameter", parameter)
            field(case, "exception.kind", "tag", "range")
        elif name == "api-signed-yard-values":
            case["operation"] = "signed"
            field(case, "accepts-negative-yards", "bool", True)
            for index, value in enumerate([-3,-4,-5,-6,-7,-8]):
                field(case, "yards." + str(index), "field-yards", value)
            for index in range(6):
                field(case, "counts." + str(index), "count", 0)
        elif name == "api-sixth-forwarding":
            case["operation"] = "forward"
            forwarding_fields(case)
        else:
            raise RuntimeError("Unmapped API case: " + name)
        cases.append(case)
    for original in table["supportCases"]:
        case = {"name": original["name"], "kind": "support", "fields": [], "modelOrder": [0, 1],
                "source": "actual state allocation/scalar-copy observation contract"}
        if case["name"] == "snapshot-copy-immutability":
            initial = [[9,2,-7,3,-5,4,101,11,5,1], [13,6,17,7,19,8,203,23,9,2]]
            changed = [[99,12,-77,13,-55,14,1001,111,15,11], [133,16,177,17,199,18,2003,233,19,12]]
            after = [[99,12,-87,14,-55,14,991,111,15,11], [133,16,177,17,199,18,2003,233,19,12]]
            case.update(operation="copy", initial=initial, changed=changed, input=ending(0, x=-234))
            vector_fields(case, "oldSnapshot", initial)
            vector_fields(case, "model", after)
            vector_fields(case, "snapshot", after)
            preservation_fields(case)
            prior_fields(case)
        elif case["name"] == "snapshot-no-observation-mutation":
            case["operation"] = "read"
            pair = [ZERO, ZERO]
            vector_fields(case, "model", pair, rush_first=True)
            vector_fields(case, "snapshot", pair)
            vector_fields(case, "snapshot2", pair)
            field(case, "model.savedCatch", "field-yards", 0)
            preservation_fields(case)
            prior_fields(case)
        elif case["name"] == "fresh-state-zero-distinct-storage":
            old_pair = [[1,1,1,1,1,1,10,0,0,0], [2,2,2,2,2,2,20,0,0,0]]
            case.update(operation="fresh", old=old_pair)
            for index in range(6):
                field(case, "fresh." + str(index) + ".length", "count", 2)
                field(case, "fresh." + str(index) + ".separateOld", "bool", True)
                for other in range(index + 1, 6):
                    field(case, "fresh." + str(index) + ".separate." + str(other), "bool", True)
            vector_fields(case, "freshModel", [ZERO, ZERO])
            vector_fields(case, "oldModel", old_pair)
            preservation_fields(case)
        elif case["name"] == "legacy-neutral-match-null":
            case["operation"] = "legacy"
            field(case, "legacy.matchNull", "bool", True)
            field(case, "legacy.players", "count", 14)
        cases.append(case)
    mixed = table["mixedLifecycle"]
    operations = [ending(0, type=1, ball=3, flags=0x1200),
                  ending(1, type=1, ball=3, initial="O", flags=0x200, x=234),
                  ending(0, x=-234), ending(1, carrier="QB", x=234),
                  ending(0, flags=0x40)]
    case = {"name": mixed["name"], "kind": "mixed", "fields": [], "modelOrder": [0, 1],
            "source": mixed["source"], "initial": [mixed["initialHome"], mixed["initialAway"]],
            "stages": [{"input": value} for value in operations]}
    retained = [case["initial"]]
    for index, stage in enumerate(mixed["literalStages"], 1):
        pair = [stage["home"], stage["away"]]
        field(case, "stage" + str(index) + ".result", "enum", [4,4,0,0,0][index - 1])
        field(case, "stage" + str(index) + ".fieldEnd", "field-yards", [40,20,20,20,40][index - 1])
        vector_fields(case, "stage" + str(index) + ".model", pair)
        vector_fields(case, "stage" + str(index) + ".snapshot", pair)
        retained.append(pair)
    for index, pair in enumerate(retained):
        vector_fields(case, "retained" + str(index), pair)
    preservation_fields(case)
    prior_fields(case)
    cases.append(case)
    names = [case["name"] for case in cases]
    require(len(names) == 132 and len(set(names)) == 132, "132 literal case-name inventory")
    for case in cases:
        require(len({row["name"] for row in case["fields"]}) == len(case["fields"]), "Duplicate literal field")
    return {"schema": "source-remaining-play-end-frozen-literals/v1", "cases": cases,
            "caseCount": 132, "scalarCount": sum(len(case["fields"]) for case in cases),
            "expectedData": "Literal contract tuple selection only; no actual observations/expected-stat algorithm."}


def mutation_specs():
    quick_tail = (
        "                && carrierId / SliceRules.PlayersPerTeam == side;\n"
        "            if (ordinaryReturn || quickReturn)"
    )
    sack_tail = (
        "                && carrierId % SliceRules.PlayersPerTeam == 3\n"
        "                && (flags & SourceGameFlags.BallCrossedLine) == 0)"
    )
    sack_write = "                SourceSacks[1 - side] = checked(SourceSacks[1 - side] + 1);\n"
    sack_yards = "                SourceSackYards[1 - side] = checked(SourceSackYards[1 - side] - sackGain);\n"
    sack_gain = "                int sackGain = checked(resolution.FieldEnd - input.State.LineOfScrimmage);\n"
    quick_result_tail = ("                && (result == SourcePlayResult.NewPossession || result == SourcePlayResult.Touchdown)\n"
                         + quick_tail)
    punt_guard = "                && (flags & SourceGameFlags.BallPunted) == 0\n"
    same_side = "                && side == input.State.InitialOffenseSide\n"
    sack_body = "\n            {\n" + sack_gain
    capture = "            if (BallKind == SourceBallKind.Kickoff || BallKind == SourceBallKind.Punt && !offense)"
    capture_write = "\n                SourceKickReturnCatch = SourceMatchRules.FieldPosition(SourceCatchX, (int)Possession);\n"
    capture_head = "        internal void CaptureSourceKickReturnCatch(bool offense)\n        {\n"
    return [
        (STATE, "int returnGain = checked(resolution.FieldEnd - SourceKickReturnCatch);",
         "int returnGain = checked(resolution.FieldEnd - input.State.LineOfScrimmage);"),
        (STATE, "                SourceTotalYards[side] = checked(SourceTotalYards[side] + returnGain);\n", ""),
        (STATE, "                SourceKickReturns[side] = checked(SourceKickReturns[side] + 1);\n", ""),
        (STATE, "                && result == SourcePlayResult.NewPossession\n",
         "                && (result == SourcePlayResult.NewPossession || result == SourcePlayResult.Touchdown)\n"),
        (STATE, "                && result == SourcePlayResult.NewPossession\n"
         "                && carrierId / SliceRules.PlayersPerTeam == side;",
         "                && result == SourcePlayResult.NewPossession;"),
        (STATE, quick_tail, quick_tail.replace(" == side;", " == side\n"
         "                && (flags & SourceGameFlags.ForwardPass) == 0;")),
        (STATE, quick_result_tail, quick_result_tail.replace(" == side;", " == side\n"
         "                && (flags & SourceGameFlags.BallKicked) != 0;")),
        (STATE, "int rushGain = checked(resolution.FieldEnd - input.State.LineOfScrimmage);",
         "int rushGain = Math.Max(0, checked(resolution.FieldEnd - input.State.LineOfScrimmage));"),
        (STATE, "                SourceTotalYards[side] = checked(SourceTotalYards[side] + rushGain);\n", ""),
        (STATE, "                SourceRushes[side] = checked(SourceRushes[side] + 1);\n", ""),
        (STATE, "SourceRushingYards[side] = checked(SourceRushingYards[side] + rushGain);",
         "SourceRushingYards[resolution.State.OffenseSide] = "
         "checked(SourceRushingYards[resolution.State.OffenseSide] + rushGain);"),
        (STATE, "                && (flags & SourceGameFlags.BallKicked) == 0\n", ""),
        (STATE, "                && (flags & SourceGameFlags.BallKicked) == 0\n"
         "                && result != SourcePlayResult.Incomplete)",
         "                && (flags & SourceGameFlags.BallKicked) == 0)"),
        (STATE, "SourceSackYards[1 - side] = checked(SourceSackYards[1 - side] - sackGain);",
         "SourceSackYards[1 - side] = checked(SourceSackYards[1 - side] + sackGain);"),
        (STATE, sack_gain + sack_yards, sack_gain + sack_yards.replace("[1 - side]", "[side]")),
        (STATE, sack_write, "                SourceConsecutiveLosses[1 - side] = "
         "checked(SourceConsecutiveLosses[1 - side] + 1);\n"),
        (STATE, sack_yards + sack_write, sack_yards + sack_write + "                SourceTotalYards[side] = "
         "checked(SourceTotalYards[side] + sackGain);\n"),
        (STATE, sack_tail, "                && carrierId % SliceRules.PlayersPerTeam == 3)"),
        (STATE, punt_guard + sack_tail, punt_guard + sack_tail[:-1] + "\n                && (flags & SourceGameFlags.BallKicked) == 0)"),
        (STATE, sack_tail + sack_body, sack_tail[:-1] + "\n                && result != SourcePlayResult.Incomplete)" + sack_body),
        (STATE, same_side + punt_guard + sack_tail, same_side + punt_guard + sack_tail[:-1]
         + "\n                && carrierId / SliceRules.PlayersPerTeam == side)"),
        (STATE, capture, capture.replace("!offense", "offense")),
        (STATE, capture + capture_write, "            if (BallKind == SourceBallKind.Punt && !offense)" + capture_write),
        (STATE, capture_head + capture, capture_head + capture[:-1] + " || BallKind == SourceBallKind.Fumble)"),
        (STATE, "SourceKickReturnCatch = SourceMatchRules.FieldPosition(SourceCatchX, (int)Possession);",
         "SourceKickReturnCatch = SourceMatchRules.FieldPosition(SourceCatchX, 1 - (int)Possession);"),
        (SNAPSHOT, "                    model.SourceReturnYards[(int)TeamId.Home],\n",
         "                    model.SourceRushingYards[(int)TeamId.Home],\n"),
        (CONTRACTS, "            if (homeKickReturns < 0)\n"
         "                throw new ArgumentOutOfRangeException(nameof(homeKickReturns));\n", ""),
        (CONTRACTS, "            if (playEndStats == null)\n"
         "                throw new ArgumentNullException(nameof(playEndStats));\n", ""),
        (SNAPSHOT, "                Match = new SourceMatchView(new SourcePlayEndStatsView(\n",
         "                model.SourceRushes[0]++;\n"
         "                Match = new SourceMatchView(new SourcePlayEndStatsView(\n"),
        (CONTRACTS, "            if (homeKickReturns < 0)\n",
         "            if (homeSackYards < 0)\n"
         "                throw new ArgumentOutOfRangeException(nameof(homeSackYards));\n"
         "            if (homeKickReturns < 0)\n"),
    ]


def mutate(source, path, anchor, replacement):
    require(path in {STATE, CONTRACTS, SNAPSHOT, CALLER}, "Mutation outside actual released production")
    return replace_unique(source, anchor, replacement)


def freeze_mutations(contract, inventory, bodies):
    declarations = contract["freshControlsProposal"]["controls"]
    specs = mutation_specs()
    require(len(declarations) == len(specs) == 30, "30 mutation declarations")
    cases = {case["name"]: case for case in inventory["cases"]}
    rows = []
    for declaration, (path, anchor, replacement) in zip(declarations, specs):
        identity, _, name, suffix, expected = declaration
        selected = cases[name]
        matches = [field for field in selected["fields"] if field["name"] == name + "." + suffix]
        require(len(matches) == 1 and matches[0]["value"] == expected, "Declared control literal/field mismatch")
        before = bodies[path].decode()
        after = mutate(before, path, anchor, replacement).encode()
        require(after != bodies[path], "Empty semantic mutation")
        rows.append({"id": identity, "source": path, "anchor": anchor, "replacement": replacement,
                     "sourceBefore": {"bytes": len(bodies[path]), "sha256": digest(bodies[path])},
                     "mutatedAfter": {"bytes": len(after), "sha256": digest(after)},
                     "case": name, "failure": name + "." + suffix, "expected": expected,
                     "firstFailureIndex": selected["fields"].index(matches[0])})
    require(len({row["id"] for row in rows}) == 30, "Duplicate mutation ids")
    require(len({(row["source"], row["anchor"]) for row in rows}) == 30, "Distinct30 contextual mutation anchors")
    return rows


def static_mutants(caller, guard):
    group_line = "            Model.AccumulateSourcePlayEndStats(endingInput, resolution, Model.CarrierId);\n"
    fire_line = "            ApplySourceFirePlayEnd(endingInput, resolution);\n"
    last_line = "            Model.SourceLastResult = resolution.Classification.Result;\n"
    commit = fire_line + group_line + last_line
    kick = ("            Model.SourceBallFrozen = false;\n"
            "            Model.SourceKickReturnCatch = -1;\n"
            "            Model.SourceFlags |= SourceGameFlags.BallKicked;\n")
    punt = ("            Model.SourceFlags |= SourceGameFlags.BallPunted;\n"
            "            Model.SourceKickReturnCatch = -1;\n")
    capture_line = "            Model.CaptureSourceKickReturnCatch(offense);\n"
    capture_field = "caller.play-end-stats.capture-post-possession"
    rows = [
        ("missing-grouped-call", replace_unique(caller, group_line, ""), "caller.play-end-stats.exact-common-commit"),
        ("wrong-common-location", replace_unique(caller, commit, group_line + fire_line + last_line),
         "caller.play-end-stats.exact-common-commit"),
        ("duplicate-grouped-call", replace_unique(caller, group_line, group_line + group_line),
         "caller.play-end-stats.exact-common-commit"),
        ("missing-kick-reset", replace_unique(caller, kick, kick.replace("            Model.SourceKickReturnCatch = -1;\n", "")),
         "caller.play-end-stats.kick-reset"),
        ("missing-punt-reset", replace_unique(caller, punt, punt.splitlines(keepends=True)[0]),
         "caller.play-end-stats.punt-reset"),
    ]
    catch = guard["method_text"](caller, "TrySourceCatch", "ActorState actor")
    moved = replace_unique(catch, capture_line, "")
    moved = replace_unique(moved, "            if (actor.Team != Model.Possession)\n",
                           capture_line + "            if (actor.Team != Model.Possession)\n")
    rows.append(("capture-before-possession", replace_unique(caller, catch, moved), capture_field))
    rows.append(("comment-only-capture", replace_unique(caller, capture_line,
                 "            // Model.CaptureSourceKickReturnCatch(offense);\n"), capture_field))
    rows.append(("unrelated-caller-edit", caller + "// bounded unrelated-edit negative control\n",
                 "caller.play-end-stats.only-four-authorized-edits"))
    return rows


def cli_entrypoint(data):
    def u16(offset):
        return struct.unpack_from("<H", data, offset)[0]
    def u32(offset):
        return struct.unpack_from("<I", data, offset)[0]
    pe = u32(0x3c)
    require(data[pe:pe + 4] == b"PE\0\0", "PE signature")
    sections, optional_size = u16(pe + 6), u16(pe + 20)
    optional = pe + 24
    magic = u16(optional)
    require(magic in (0x10b, 0x20b), "PE optional header")
    directory = optional + (96 if magic == 0x10b else 112)
    table = optional + optional_size
    def offset(rva):
        for index in range(sections):
            section = table + index * 40
            size, address, raw_size, raw = struct.unpack_from("<IIII", data, section + 8)
            if address <= rva < address + max(size, raw_size):
                return raw + rva - address
        raise RuntimeError("Unmapped PE RVA")
    cli = offset(u32(directory + 14 * 8))
    token = u32(cli + 20)
    require(token >> 24 == 6, "Managed MethodDef entrypoint required")
    metadata = offset(u32(cli + 8))
    require(data[metadata:metadata + 4] == b"BSJB", "CLI metadata signature")
    position = metadata + 16 + u32(metadata + 12)
    position = (position + 3) & ~3
    streams = u16(position + 2)
    position += 4
    heaps = {}
    for _ in range(streams):
        relative, size = struct.unpack_from("<II", data, position)
        name_start = position + 8
        name_end = data.index(0, name_start)
        name = data[name_start:name_end].decode("ascii")
        heaps[name] = (metadata + relative, size)
        position = (name_end + 4) & ~3
    tables = heaps["#~"][0] if "#~" in heaps else heaps["#-"][0]
    heap_flags = data[tables + 6]
    valid = struct.unpack_from("<Q", data, tables + 8)[0]
    counts, position = {}, tables + 24
    for index in range(64):
        if valid & (1 << index):
            counts[index] = u32(position)
            position += 4
    strings = 4 if heap_flags & 1 else 2
    guids = 4 if heap_flags & 2 else 2
    blobs = 4 if heap_flags & 4 else 2
    def index_size(number):
        return 4 if counts.get(number, 0) >= 65536 else 2
    def coded_size(numbers, bits):
        return 4 if max(counts.get(number, 0) for number in numbers) >= (1 << (16 - bits)) else 2
    resolution_scope = coded_size((0, 26, 35, 1), 2)
    type_ref = coded_size((2, 1, 27), 2)
    sizes = [2 + strings + 3 * guids, resolution_scope + 2 * strings,
             4 + 2 * strings + type_ref + index_size(4) + index_size(6),
             index_size(4), 2 + strings + blobs, index_size(6),
             8 + strings + blobs + index_size(8)]
    starts = {}
    for number, size in enumerate(sizes):
        starts[number] = position
        position += counts.get(number, 0) * size
    def ix(at, width):
        return u32(at) if width == 4 else u16(at)
    def string(at):
        start = heaps["#Strings"][0] + at
        return data[start:data.index(0, start)].decode("utf-8")
    row = token & 0xffffff
    require(1 <= row <= counts.get(6, 0), "Entrypoint row bounds")
    method = starts[6] + (row - 1) * sizes[6]
    method_name = string(ix(method + 8, strings))
    owner = None
    for number in range(counts.get(2, 0)):
        at = starts[2] + number * sizes[2]
        first_method = ix(at + 4 + 2 * strings + type_ref + index_size(4), index_size(6))
        next_method = counts.get(6, 0) + 1
        if number + 1 < counts.get(2, 0):
            next_method = ix(at + sizes[2] + 4 + 2 * strings + type_ref + index_size(4), index_size(6))
        if first_method <= row < next_method:
            owner = string(ix(at + 4, strings))
            break
    require((owner, method_name) == ("SourceRemainingPlayEndStatsChecks", "Main"),
            "Actual CLI entrypoint selected old/other Main")
    return {"methodDefToken": token, "declaringType": owner, "method": method_name,
            "qualification": "PE/CLI metadata inspection, not reflection or native execution"}


def environment():
    base = OUT / "process-state"
    result = {"HOME": str(base / "home"), "TMP": str(base / "files"),
              "TEMP": str(base / "files"), "TMPDIR": str(base / "files"),
              "DOTNET_CLI_HOME": str(base / "cli"), "NUGET_PACKAGES": str(base / "nuget"),
              "NUGET_HTTP_CACHE_PATH": str(base / "nuget-http"),
              "DOTNET_ROOT": "/home/long2know/.dotnet", "DOTNET_EnableDiagnostics": "0",
              "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
              "DOTNET_NOLOGO": "1", "DOTNET_MULTILEVEL_LOOKUP": "0", "LANG": "C.UTF-8",
              "LC_ALL": "C.UTF-8", "TZ": "UTC"}
    for key in ("HOME", "TMP", "TEMP", "TMPDIR", "DOTNET_CLI_HOME", "NUGET_PACKAGES", "NUGET_HTTP_CACHE_PATH"):
        Path(result[key]).mkdir(parents=True, exist_ok=True)
    return result


def setup_environment():
    incoming = dict(os.environ)
    require(set(incoming) <= {"LC_CTYPE"} and all(value == "C.UTF-8" for value in incoming.values()),
            "Outer Python environment was not explicitly empty")
    os.environ.clear()
    os.environ.update(environment())
    return incoming


def authorities():
    documents = {name: decode(check_pin(OUT / name, expected)) for name, expected in AUTHORITY.items()}
    require(documents["implementation-admission.json"]["approved"] is True
            and documents["execution-admission.json"]["approved"] is True, "Admission not approved")
    require(documents["execution-admission.json"]["worktree"] == str(ROOT), "Wrong admitted worktree")
    meta = {name: decode(check_pin(B / name, expected)) for name, expected in META_PINS.items()}
    return documents, meta


def counts_after(before, kind, ns21=False):
    require(kind in LIMITS, "Unknown launch category")
    after = dict(before)
    after[kind] += 1
    if ns21:
        after["ns21"] += 1
    require(all(after[key] <= LIMITS[key] for key in LIMITS), "Fresh launch ceiling exhausted")
    return after


def counters():
    return read_json(LEDGER / "counters.json")


def save_counters(document):
    write_json(LEDGER / "counters.json", document, exclusive=False)


def within_window():
    phase = read_json(PREP / "phase-start.json")
    elapsed = (datetime.now(timezone.utc) - datetime.fromisoformat(phase["firstReservedUtc"])).total_seconds()
    require(0 <= elapsed <= 1800, "Fresh1800-second execution window exceeded")


def output_size():
    total = 0
    files = 0
    for root in (PREP, VALIDATION, OUT / "process-state"):
        if not root.exists():
            continue
        for directory, dirs, names in os.walk(root, followlinks=False):
            require(not any((Path(directory) / name).is_symlink() for name in dirs), "Output symlink directory")
            for name in names:
                path = Path(directory) / name
                require(not path.is_symlink(), "Output symlink file")
                total += path.stat().st_size
                files += 1
    if (OUT / "source-write-handoff.json").exists():
        total += (OUT / "source-write-handoff.json").stat().st_size
        files += 1
    require(total <= 536870912, "New output512MiB ceiling exceeded")
    return {"bytes": total, "files": files, "maximumBytes": 536870912,
            "scope": "Only this outcome's admitted newly authored output roots"}


def outer_template(node, kind, state, manifest_pin):
    argv = [PYTHON, "-I", "-S", "-B"]
    if node == "U1":
        argv += [str(SUITE / "test_runner.py"), "-q"]
    else:
        argv += [str(SUITE / "run.py"), "--admission", str(OUT / "execution-admission.json")]
        if node == "D1":
            argv += ["--controls"]
        else:
            argv += [{"M2": "--inspect-results", "M3": "--handoff"}[node]]
    row = {"node": node, "kind": kind, "id": state["nextId"], "argv": argv,
           "cwd": str(ROOT), "environment": {}, "timeoutSeconds": TIMEOUTS[kind],
           "beforeCounts": state["counts"], "consumedCounts": counts_after(state["counts"], kind),
           "inputManifest": manifest_pin,
           "authorities": {name: {"bytes": value[0], "sha256": value[1]} for name, value in AUTHORITY.items()},
           "binaryAndRuntimeconfig": "Not applicable: outer isolated Python entrypoint, not a managed component",
           "expectedExit": 0, "stdout": str(PREP / ("outer-" + node + ".stdout")),
           "stderr": str(PREP / ("outer-" + node + ".stderr"))}
    write_json(PREP / ("launch-" + node + ".json"), row)
    return row


def claim_outer(node, kind):
    incoming = setup_environment()
    if node == "M1":
        path = LEDGER / "0001-M1-reservation.json"
        reservation = read_json(path)
        envelope = reservation["envelope"]
        require(envelope["node"] == "M1" and envelope["id"] == 1
                and envelope["kind"] == "metadata", "M1 bootstrap reservation")
        before = {key: 0 for key in LIMITS}
        require(envelope["beforeCounts"] == before
                and envelope["consumedCounts"] == counts_after(before, "metadata"), "M1 pre-consumed counts")
        require({row["path"] for row in envelope["authoredInputPins"]} ==
                {str(ROOT / name) for name in list(OLD) + NEW_FILES}, "M1 exact authored input identities")
        for row in envelope["authoredInputPins"]:
            check_pin(row["path"], row)
        state = {"counts": envelope["consumedCounts"], "nextId": 2}
        save_counters(state)
        write_json(PREP / "phase-start.json",
                   {"firstReservedUtc": reservation["reservedAtUtc"], "pythonStartedAtUtc": utc(),
                    "pid": os.getpid(), "parentPid": os.getppid(), "firstNode": "M1",
                    "meaning": "Actual new reservation clock, never cached human time"})
    else:
        state = counters()
        path = LEDGER / ("%04d-%s-reservation.json" % (state["nextId"], node))
        reservation = read_json(path)
        envelope = reservation["envelope"]
        require(envelope["node"] == node and envelope["kind"] == kind
                and envelope["id"] == state["nextId"], "Wrong outer reservation")
        require(envelope["beforeCounts"] == state["counts"], "Stale outer counts")
        require(envelope["consumedCounts"] == counts_after(state["counts"], kind), "Outer count reservation")
        state["counts"] = envelope["consumedCounts"]
        state["nextId"] += 1
        save_counters(state)
    require(reservation["pid"] == os.getpid() and reservation["parentPid"] == os.getppid(),
            "Actual outer/Python identity mismatch")
    require(envelope["argv"] == [sys.executable, "-I", "-S", "-B"] + sys.argv,
            "Reserved outer Python argv mismatch")
    require(envelope["cwd"] == str(ROOT) and Path.cwd() == ROOT, "Outer cwd mismatch")
    require(envelope["environment"] == {}, "Outer launch environment must be empty")
    require(sys.flags.isolated and sys.flags.no_site and sys.flags.dont_write_bytecode,
            "Required Python -I -S -B flags")
    authorities()
    if node != "M1":
        check_pin(envelope["inputManifest"]["path"], envelope["inputManifest"])
    within_window()
    write_json(LEDGER / ("%04d-%s-started.json" % (envelope["id"], node)),
               {"atUtc": utc(), "pid": os.getpid(), "parentPid": os.getppid(),
                "reservation": pin(path), "entrypoint": pin(sys.argv[0]),
                "launchEnvironment": {}, "pythonLocaleCoercion": incoming,
                "effectiveEnvironment": dict(os.environ), "counts": state["counts"]})
    return envelope


def finish_outer(envelope, exit_code, result=None, error=None):
    sys.stdout.flush()
    sys.stderr.flush()
    row = {"atUtc": utc(), "pid": os.getpid(), "parentPid": os.getppid(), "node": envelope["node"],
           "pythonExitIntent": exit_code, "result": result, "error": error, "counts": counters()["counts"],
           "stdout": pin(envelope["stdout"]), "stderr": pin(envelope["stderr"]),
           "outputBudget": output_size(),
           "actualOuterExitReceipt": str(LEDGER / ("%04d-%s-outer-exit.json" % (envelope["id"], envelope["node"]))),
           "instrumentationLimit": "Self-reported Python/child receipts plus enclosing shell exit receipt; not a kernel trace."}
    write_json(LEDGER / ("%04d-%s-completion.json" % (envelope["id"], envelope["node"])), row)


def filesystem_metadata(path):
    value = path.lstat()
    return {"device": value.st_dev, "inode": value.st_ino, "bytes": value.st_size,
            "mtimeNs": value.st_mtime_ns, "ctimeNs": value.st_ctime_ns, "mode": value.st_mode}


def prepare():
    documents, meta = authorities()
    contract = documents["contract.json"]
    original = meta["metadata/source-completed-pass-stats/preparation/production77-authored.json"]
    paths = [row["relativePath"] for row in original]
    validate_sources(paths, paths)
    counts = {}
    for path in paths:
        category = path.split("/")[2]
        counts[category] = counts.get(category, 0) + 1
    require(counts == {"Contracts": 10, "Gameplay": 38, "AssetImport": 27, "Presentation": 2},
            "Production closure category counts")
    require({path for path in paths if "/Presentation/" in path} ==
            {"Assets/Scripts/Presentation/SourceAudioFunctions.cs",
             "Assets/Scripts/Presentation/SourceC2SoundFunctions.cs"}, "Presentation dependency closure")
    tool_metadata = meta["human-possession-time-source-preparation/tool-inputs.json"]
    tool_rows = []
    for key, expected_path in (("python", PYTHON), ("dotnet", DOTNET), ("compiler", CSC)):
        row = tool_metadata[key]
        require(row["path"] == expected_path, "Wrong admitted tool")
        tool_rows.append(pin(expected_path, check_pin(expected_path, row)))
    ref_metadata = meta["human-possession-time-source-preparation/reference-inputs.json"]
    references = {}
    for profile in ("net10.0", "netstandard2.1"):
        rows = ref_metadata["profiles"][profile]["files"]
        validate_references(rows, profile)
        references[profile] = [pin(row["path"], check_pin(row["path"], row)) for row in rows]
    bodies, production = {}, []
    for row in original:
        relative = row["relativePath"]
        data = regular_bytes(ROOT / relative)
        if relative not in {STATE, CONTRACTS, SNAPSHOT, CALLER}:
            require((len(data), digest(data)) == (row["bytes"], row["sha256"]), "Unreleased production drift: " + relative)
        captured = PREP / "captured-production" / relative
        write_bytes(captured, data)
        bodies[relative] = data
        production.append(dict(pin(captured, data), relativePath=relative, livePath=str(ROOT / relative)))
    fixture = check_pin(ROOT / FIXTURE,
                        (21900, "07946c95ad09dfd8eda4eaeeec805f739d6e5218ce85c4680f9ba56e40b95b25"))
    harnesses = []
    for relative, data in ((HARNESS, regular_bytes(ROOT / HARNESS)), (FIXTURE, fixture)):
        captured = PREP / "captured-harness" / relative
        write_bytes(captured, data)
        harnesses.append(dict(pin(captured, data), relativePath=relative))
    require(len({row["path"] for row in harnesses}) == 2, "Harness paths must remain distinct")
    versions = []
    for relative in NEW_FILES:
        data = regular_bytes(ROOT / relative)
        target = PREP / "versions" / relative
        write_bytes(target, data)
        versions.append(dict(pin(target, data), livePath=str(ROOT / relative)))
    guard_source = regular_bytes(ROOT / GUARD).decode()
    proof = verify_inverses(bodies, guard_source, contract)
    write_json(PREP / "preservation-proof.json", proof)
    write_json(PREP / "production77-before.json", original)
    write_json(PREP / "production77-authored.json", production)
    write_json(PREP / "harness-authored.json", harnesses)
    acceptance = meta["metadata/source-completed-pass-stats/parent-scoped-acceptance.json"]
    review = meta["metadata/source-completed-pass-stats/quality-review.json"]
    frozen = acceptance["filesNowFrozen"] + review["current34PathCatalogue"]["other26"]
    require(len({row["path"] for row in frozen}) == len(frozen) == 34, "Exact current34 catalogue")
    frozen_rows = []
    for row in frozen:
        relative = row["path"]
        observed = filesystem_metadata(ROOT / relative)
        if relative not in OLD:
            require(observed["bytes"] == row["bytes"], "Unreleased frozen size drift: " + relative)
        frozen_rows.append({"path": relative, "beforeBytes": row["bytes"], "beforeSha256": row["sha256"],
                            "released": relative in OLD, "observedAtM1AfterPermittedAuthorship": observed,
                            "unopenedHistoricalBody": relative not in paths and relative not in (GUARD, FIXTURE)})
    write_json(PREP / "frozen34-before.json", frozen_rows)
    inventory = build_inventory(contract)
    inventory_pin = write_json(PREP / "expected-inventory.json", inventory)
    mutations = freeze_mutations(contract, inventory, bodies)
    mutation_pin = write_json(PREP / "mutations.json", mutations)
    guard = pure_guard(guard_source)
    static_rows = [{"id": name, "sourceBeforeSha256": digest(bodies[CALLER]),
                    "sourceAfterSha256": digest(text.encode()), "expectedField": failure}
                   for name, text, failure in static_mutants(bodies[CALLER].decode(), guard)]
    write_json(PREP / "static-controls.json", static_rows)
    public_lists = [
        ("tests/Blitz.Core.Tests/Blitz.Core.Tests.csproj",
         (1913, "98e5c0d60999d0f3799fa535572c13e0cdf2570493d147a2b4d3c965a81aeebd")),
        ("tests/audio/vm/SourceSoundVmSources.props",
         (926, "1daa33efc62632fe15a2cd05dbe5372872e3b1ec24637893ee7fe3b706e8dac5")),
    ]
    public_pins = [pin(ROOT / name, check_pin(ROOT / name, expected)) for name, expected in public_lists]
    manifest = {"schema": "source-remaining-play-end-captured-inputs/v1", "atUtc": utc(),
                "authorities": [pin(OUT / name) for name in AUTHORITY],
                "production": production, "harnesses": harnesses, "versions": versions,
                "guard": pin(ROOT / GUARD), "tools": tool_rows, "references": references,
                "publicSourceLists": public_pins, "inventory": inventory_pin, "mutations": mutation_pin,
                "caseCount": 132, "scalarCount": inventory["scalarCount"], "controls": 30}
    manifest_pin = write_json(PREP / "input-manifest.json", manifest)
    outer_template("U1", "units", counters(), manifest_pin)
    return {"status": "prepared", "inputManifest": manifest_pin, "inventory": inventory_pin,
            "cases": 132, "scalarCount": inventory["scalarCount"], "controls": 30, "staticControls": 8,
            "fiveWholeFileInverses": True, "fiveConstructors": True, "acceptedMethodPreserved": True,
            "noCompilerOrManagedLaunchYet": True}


def frozen_inputs():
    manifest = read_json(PREP / "input-manifest.json")
    for row in manifest["authorities"]:
        check_pin(row["path"], row)
    check_pin(manifest["inventory"]["path"], manifest["inventory"])
    check_pin(manifest["mutations"]["path"], manifest["mutations"])
    for row in manifest["versions"]:
        check_pin(row["livePath"], row)
    check_pin(manifest["guard"]["path"], manifest["guard"])
    return manifest


def validate_live_and_captured(manifest):
    for row in manifest["production"]:
        check_pin(row["path"], row)
        check_pin(row["livePath"], row)
    for row in manifest["harnesses"]:
        check_pin(row["path"], row)
        check_pin(ROOT / row["relativePath"], row)


def reserve_child(kind, argv, inputs, output, expected_exit, case=None, control=None, ns21=False):
    within_window()
    state = counters()
    number = state["nextId"]
    state["counts"] = counts_after(state["counts"], kind, ns21)
    state["nextId"] += 1
    row = {"id": number, "kind": kind, "reservedAtUtc": utc(), "parentPid": os.getpid(),
           "argv": argv, "cwd": str(ROOT), "environment": environment(), "timeoutSeconds": TIMEOUTS[kind],
           "inputs": inputs, "stdout": str(output / (kind + ".stdout")),
           "stderr": str(output / (kind + ".stderr")), "expectedExit": expected_exit,
           "selectedCase": case, "control": control, "countsAfterReservation": state["counts"],
           "ns21CompileOnly": ns21}
    reservation = LEDGER / ("%04d-%s-reservation.json" % (number, kind))
    write_json(reservation, row)
    save_counters(state)
    return row, reservation


def child(kind, argv, inputs, output, expected_exit=0, case=None, control=None, ns21=False):
    output.mkdir(parents=True, exist_ok=True)
    row, reservation = reserve_child(kind, argv, inputs, output, expected_exit, case, control, ns21)
    stdout, stderr = Path(row["stdout"]), Path(row["stderr"])
    timed_out = False
    with stdout.open("xb") as out, stderr.open("xb") as err:
        process = subprocess.Popen(argv, cwd=ROOT, env=row["environment"], stdin=subprocess.PIPE,
                                   stdout=out, stderr=err, shell=False)
        process.stdin.close()
        write_json(LEDGER / ("%04d-%s-started.json" % (row["id"], kind)),
                   {"atUtc": utc(), "pid": process.pid, "parentPid": os.getpid(), "reservation": pin(reservation)})
        try:
            exit_code = process.wait(timeout=row["timeoutSeconds"])
        except subprocess.TimeoutExpired:
            timed_out = True
            process.kill()
            exit_code = process.wait()
    receipt = {"id": row["id"], "kind": kind, "endedAtUtc": utc(), "pid": process.pid,
               "parentPid": os.getpid(), "exit": exit_code, "timedOut": timed_out,
               "reservation": pin(reservation), "stdout": pin(stdout), "stderr": pin(stderr),
               "counts": counters()["counts"], "outputBudget": output_size()}
    write_json(LEDGER / ("%04d-%s-completion.json" % (row["id"], kind)), receipt)
    require(not timed_out and exit_code == expected_exit,
            "Unexpected %s exit/control: %s exit=%s expected=%s" % (kind, control, exit_code, expected_exit))
    return receipt


def compile_argv(manifest, output, mutation=None, ns21=False):
    sources = []
    for row in manifest["production"]:
        sources.append(mutation["path"] if mutation and row["relativePath"] == mutation["source"] else row["path"])
    require(len(sources) == 77 and len(set(sources)) == 77, "Actual77 compilation inputs")
    profile = "netstandard2.1" if ns21 else "net10.0"
    references = manifest["references"][profile]
    validate_references(references, profile)
    binary = output / ("compatibility.dll" if ns21 else "component.dll")
    argv = [DOTNET, "exec", CSC, "/nologo", "/noconfig", "/nostdlib+", "/langversion:7.3",
            "/nullable:disable", "/warnaserror+", "/deterministic+", "/optimize+", "/debug-",
            "/target:library" if ns21 else "/target:exe"]
    if not ns21:
        argv += ["/main:SourceRemainingPlayEndStatsChecks"]
        sources += [row["path"] for row in manifest["harnesses"]]
        require(len(sources) == len(set(sources)) == 79, "Distinct79 compilation inputs")
    argv += ["/out:" + str(binary)]
    argv += ["/reference:" + row["path"] for row in references]
    argv += sources
    return argv, binary, sources, references


def compile_component(manifest, output, mutation=None, ns21=False):
    validate_live_and_captured(manifest)
    argv, binary, sources, references = compile_argv(manifest, output, mutation, ns21)
    for row in manifest["tools"] + references:
        check_pin(row["path"], row)
    require(not binary.exists(), "Compiler output already exists")
    runtimeconfig = None
    if not ns21:
        runtimeconfig = write_json(output / "component.runtimeconfig.json",
            {"runtimeOptions": {"tfm": "net10.0", "framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"},
                                "rollForward": "Disable"}})
    source_pins = [pin(path) for path in sources]
    inputs = {"sourceInputs": source_pins, "sourceCount": len(sources), "tools": manifest["tools"],
              "references": references, "referenceCount": len(references),
              "authorities": manifest["authorities"], "inventory": manifest["inventory"],
              "mutation": mutation, "binaryBeforeLaunch": {"path": str(binary), "exists": False},
              "runtimeconfigBeforeLaunch": runtimeconfig}
    receipt = child("csc", argv, inputs, output, control=None if mutation is None else mutation["id"], ns21=ns21)
    binary_pin = pin(binary)
    entrypoint = None if ns21 else cli_entrypoint(regular_bytes(binary))
    return {"binary": binary_pin, "runtimeconfig": runtimeconfig, "entrypoint": entrypoint, "compilerReceipt": receipt}


def fixture_check(document):
    fixture = document["fixture"]
    expected = {"clipKey": "SYNTHETIC_COMPLETION_SNAPSHOT_FRAME",
                "programKey": "synthetic_completion_snapshot_frame_only", "sourceFrames": 1,
                "selectedFrame": 0, "joints": 15, "actorsInitialized": 14, "layersInitialized": 28,
                "initializationStepsPerLayer": 1, "program": "NewSequence,NextFrame(1),End",
                "randomRequests": 0, "originalProvenanceSupplied": False}
    require(all(fixture.get(key) == value for key, value in expected.items()), "Synthetic fixture invariant failure")
    require(document["genuineSimulationExecuted"] is False, "False genuine-execution claim")


def verify_result(document, inventory, selected=None, control=None, clean_case=None):
    fixture_check(document)
    cases = inventory["cases"] if selected is None else [case for case in inventory["cases"] if case["name"] == selected]
    require(cases and document["cases"] == [case["name"] for case in cases], "Exact named case inventory mismatch")
    expected = [row for case in cases for row in case["fields"]]
    observed = document["fields"]
    require(document["fieldCount"] == len(observed), "Recorded field count mismatch")
    if control is None:
        require(document["status"] == "passed" and document["failure"] is None and len(observed) == len(expected),
                "Baseline semantic status/scalar count")
        limit = len(expected)
    else:
        require(document["status"] == "assertion-failed" and document["failure"] == control["failure"],
                "Control did not fail its exact declared named semantic assertion")
        require(len(observed) == control["firstFailureIndex"] + 1, "Wrong first-failure field/order")
        limit = len(observed)
    for index, actual in enumerate(observed):
        literal = expected[index]
        require(actual["name"] == literal["name"] and actual["unit"] == literal["unit"]
                and actual["expected"] == literal["value"], "Frozen field/unit/value/order drift")
        if control is not None and index == limit - 1:
            require(actual["name"] == control["failure"] and actual["expected"] == control["expected"]
                    and actual["actual"] != literal["value"], "Not the declared differing semantic value")
        else:
            require(actual["actual"] == literal["value"], "Literal semantic mismatch: " + actual["name"])
    if control is not None:
        require(clean_case is not None and observed[:-1] == clean_case[:len(observed) - 1],
                "Control does not preserve identical clean case prefix")
    return {"cases": len(cases), "observedFields": len(observed), "status": document["status"],
            "failure": document["failure"]}


def run_component(manifest, compiled, output, selected=None, control=None):
    check_pin(manifest["inventory"]["path"], manifest["inventory"])
    config = compiled["runtimeconfig"]
    check_pin(config["path"], config)
    check_pin(compiled["binary"]["path"], compiled["binary"])
    for row in manifest["tools"] + manifest["references"]["net10.0"]:
        check_pin(row["path"], row)
    argv = [DOTNET, "exec", "--runtimeconfig", config["path"], compiled["binary"]["path"],
            "--inventory", manifest["inventory"]["path"]]
    if selected is not None:
        argv += ["--case", selected]
    inputs = {"binary": compiled["binary"], "runtimeconfig": config, "entrypoint": compiled["entrypoint"],
              "authorities": manifest["authorities"], "inventory": manifest["inventory"],
              "tools": manifest["tools"], "compilerReceipt": compiled["compilerReceipt"],
              "harnesses": manifest["harnesses"], "references": manifest["references"]["net10.0"]}
    receipt = child("managed", argv, inputs, output, expected_exit=2 if control else 0,
                    case=selected, control=None if control is None else control["id"])
    document = decode(regular_bytes(output / "managed.stdout"))
    result_pin = write_json(output / "semantic-result.json", document)
    return document, {"managedReceipt": receipt, "result": result_pin}


def campaign():
    manifest = frozen_inputs()
    inventory = read_json(manifest["inventory"]["path"])
    mutations = read_json(manifest["mutations"]["path"])
    require(read_json(PREP / "unit-result.json")["successful"] is True, "U1 must pass before D1")
    clean_dir = VALIDATION / "clean"
    clean_compiled = compile_component(manifest, clean_dir)
    clean, clean_evidence = run_component(manifest, clean_compiled, clean_dir)
    clean_summary = verify_result(clean, inventory)
    clean_fields = {case["name"]: [row for row in clean["fields"] if row["name"].startswith(case["name"] + ".")]
                    for case in inventory["cases"]}
    controls = []
    by_relative = {row["relativePath"]: row for row in manifest["production"]}
    for mutation in mutations:
        directory = VALIDATION / "controls" / mutation["id"]
        baseline = check_pin(by_relative[mutation["source"]]["path"], mutation["sourceBefore"]).decode()
        changed = mutate(baseline, mutation["source"], mutation["anchor"], mutation["replacement"]).encode()
        require((len(changed), digest(changed)) ==
                (mutation["mutatedAfter"]["bytes"], mutation["mutatedAfter"]["sha256"]), "Frozen mutant drift")
        target = directory / "mutated" / mutation["source"]
        write_bytes(target, changed)
        selected_mutation = dict(mutation, path=str(target))
        compiled = compile_component(manifest, directory, selected_mutation)
        result, evidence = run_component(manifest, compiled, directory, mutation["case"], mutation)
        summary = verify_result(result, inventory, mutation["case"], mutation, clean_fields[mutation["case"]])
        controls.append({"id": mutation["id"], "mutation": mutation, "compile": compiled,
                         "observation": summary, "evidence": evidence})
    ns21 = compile_component(manifest, VALIDATION / "ns21-compatibility-only", ns21=True)
    final_dir = VALIDATION / "final"
    final_compiled = compile_component(manifest, final_dir)
    final, final_evidence = run_component(manifest, final_compiled, final_dir)
    final_summary = verify_result(final, inventory)
    require(clean["fields"] == final["fields"] and clean["cases"] == final["cases"],
            "Clean/final exact literal observation mismatch")
    require(counters()["counts"] == {"metadata": 1, "units": 1, "drivers": 1,
                                    "csc": 33, "managed": 32, "ns21": 1}, "Normal graph exact counts")
    result = {"status": "passed-new-component-only", "clean": {"compile": clean_compiled, "summary": clean_summary,
               "evidence": clean_evidence}, "controls": controls, "ns21CompileOnly": ns21,
              "final": {"compile": final_compiled, "summary": final_summary, "evidence": final_evidence},
              "inputManifest": pin(PREP / "input-manifest.json"), "counts": counters()["counts"]}
    write_json(PREP / "campaign-result.json", result)
    outer_template("M2", "metadata", counters(), pin(PREP / "input-manifest.json"))
    return {"status": result["status"], "casesEach": 132, "scalarFieldsEach": inventory["scalarCount"],
            "actualSourceSemanticControls": len(controls), "counts": counters()["counts"]}


def verify_outer_exit(number, node):
    path = LEDGER / ("%04d-%s-outer-exit.json" % (number, node))
    row = read_json(path)
    reservation = read_json(LEDGER / ("%04d-%s-reservation.json" % (number, node)))
    require(row["pythonExit"] == 0 and row["pythonPid"] == reservation["pid"]
            and row["outerPid"] == reservation["parentPid"], "Actual outer exit/identity failure: " + node)
    return pin(path)


def inspect_results():
    manifest = frozen_inputs()
    validate_live_and_captured(manifest)
    inventory = read_json(manifest["inventory"]["path"])
    campaign_result = read_json(PREP / "campaign-result.json")
    require(campaign_result["status"] == "passed-new-component-only"
            and len(campaign_result["controls"]) == 30, "Incomplete new campaign")
    exits = [verify_outer_exit(1, "M1"), verify_outer_exit(2, "U1"), verify_outer_exit(3, "D1")]
    receipts = []
    compile_count = managed_count = 0
    for number in range(4, 69):
        kind = "csc" if number % 2 == 0 or number == 67 else "managed"
        if number == 66:
            kind = "csc"
        if number == 68:
            kind = "managed"
        reservation_path = LEDGER / ("%04d-%s-reservation.json" % (number, kind))
        receipt_path = LEDGER / ("%04d-%s-completion.json" % (number, kind))
        reservation, receipt = read_json(reservation_path), read_json(receipt_path)
        require(receipt["exit"] == reservation["expectedExit"] and receipt["timedOut"] is False,
                "Child receipt actual exit mismatch")
        for key in ("stdout", "stderr"):
            check_pin(receipt[key]["path"], receipt[key])
        if kind == "csc":
            compile_count += 1
            inputs = reservation["inputs"]
            require(inputs["sourceCount"] == (77 if reservation["ns21CompileOnly"] else 79),
                    "Actual compiler source count")
            require(inputs["referenceCount"] == (121 if reservation["ns21CompileOnly"] else 167),
                    "Actual compiler reference count")
        else:
            managed_count += 1
            inputs = reservation["inputs"]
            check_pin(inputs["binary"]["path"], inputs["binary"])
            check_pin(inputs["runtimeconfig"]["path"], inputs["runtimeconfig"])
            cli_entrypoint(regular_bytes(inputs["binary"]["path"]))
        receipts.append({"reservation": pin(reservation_path), "completion": pin(receipt_path)})
    require((compile_count, managed_count) == (33, 32), "Actual33/32 receipt counts")
    clean_pin = campaign_result["clean"]["evidence"]["result"]
    final_pin = campaign_result["final"]["evidence"]["result"]
    clean = decode(check_pin(clean_pin["path"], clean_pin))
    final = decode(check_pin(final_pin["path"], final_pin))
    verify_result(clean, inventory)
    verify_result(final, inventory)
    require(clean["fields"] == final["fields"], "M2 clean/final scalar mismatch")
    for control in campaign_result["controls"]:
        result_pin = control["evidence"]["result"]
        result = decode(check_pin(result_pin["path"], result_pin))
        selected = control["mutation"]["case"]
        prefix = [row for row in clean["fields"] if row["name"].startswith(selected + ".")]
        verify_result(result, inventory, selected, control["mutation"], prefix)
    summary = {"status": "passed", "atUtc": utc(), "compilerInvocations": compile_count,
               "managedInvocations": managed_count, "casesEachCleanFinal": 132,
               "scalarFieldsEachCleanFinal": inventory["scalarCount"], "semanticControls": 30,
               "outerExitReceipts": exits, "childReceipts": receipts, "campaign": pin(PREP / "campaign-result.json"),
               "counts": counters()["counts"], "outputBudget": output_size(),
               "instrumentationLimit": "Bounded explicit subprocess/PE/CLI/literal audit, not kernel/native/game simulation proof."}
    result_pin = write_json(PREP / "result-inspection.json", summary)
    outer_template("M3", "metadata", counters(), pin(PREP / "input-manifest.json"))
    return {"status": "results-inspected", "inspection": result_pin, "counts": counters()["counts"]}


def handoff():
    documents, _ = authorities()
    manifest = frozen_inputs()
    validate_live_and_captured(manifest)
    bodies = {row["relativePath"]: regular_bytes(row["path"]) for row in manifest["production"]}
    proof = verify_inverses(bodies, regular_bytes(ROOT / GUARD).decode(), documents["contract.json"])
    original_proof = read_json(PREP / "preservation-proof.json")
    require(proof == original_proof, "Final exact preservation proof drift")
    frozen = read_json(PREP / "frozen34-before.json")
    current_frozen = []
    for row in frozen:
        relative = row["path"]
        current = filesystem_metadata(ROOT / relative)
        require(current == row["observedAtM1AfterPermittedAuthorship"], "Frozen/authored filesystem metadata drift: " + relative)
        actual = pin(ROOT / relative) if relative in OLD else None
        current_frozen.append({"path": relative,
                               "bytes": actual["bytes"] if actual else row["beforeBytes"],
                               "sha256": actual["sha256"] if actual else row["beforeSha256"],
                               "authority": "Current authored bytes" if actual else "Inherited current-generation pin + unchanged M1/M3 filesystem metadata",
                               "unopenedHistoricalBody": row["unopenedHistoricalBody"]})
    require(counters()["counts"] == {"metadata": 3, "units": 1, "drivers": 1,
                                    "csc": 33, "managed": 32, "ns21": 1}, "Final exact fresh graph counts")
    verify_outer_exit(69, "M2")
    inspection = read_json(PREP / "result-inspection.json")
    require(inspection["status"] == "passed", "M2 inspection did not pass")
    new_files = [pin(ROOT / relative) for relative in NEW_FILES]
    handoff_document = {
        "schema": "source-remaining-common-play-end-statistics-write-handoff/v1",
        "status": "IMPLEMENTED_FRESH_COMPONENT_EVIDENCE_AWAITING_QUALITY",
        "accountableOwner": "Unity", "soleMaterialReviewer": "Quality",
        "atActualUtc": utc(), "worktree": str(ROOT), "baseB": str(B),
        "authorities": manifest["authorities"], "authoredFive": [pin(ROOT / path) for path in OLD],
        "newFourTests": new_files, "currentFrozenCatalogue": current_frozen,
        "actual77": pin(PREP / "production77-authored.json"), "harnesses": manifest["harnesses"],
        "inventory": manifest["inventory"], "casesEachCleanFinal": 132,
        "scalarFieldsEachCleanFinal": manifest["scalarCount"], "semanticControls": 30,
        "staticControlsWithinOneUnitInvocation": 8, "pythonTests": 12,
        "counts": counters()["counts"], "fiveWholeFileInversesAndPreservedBlocks": proof,
        "unitEvidence": pin(PREP / "unit-result.json"), "campaign": pin(PREP / "campaign-result.json"),
        "resultInspection": pin(PREP / "result-inspection.json"),
        "phaseStart": pin(PREP / "phase-start.json"), "outputBudget": output_size(),
        "qualified": "Actual captured production resolver/state return-origin capture/grouped return-sack-rush producers and immutable snapshot/DTO components; exact current caller three-stage static proof.",
        "preserved": "Accepted completion method and all five old constructors byte-identical; shared independent total, prior counters/fire/time/interceptions, actors/RNG, profiles and API defaults preserved.",
        "fixture": "Unchanged typed explicit synthetic fixture; one ComponentState/all14 genuine actors28layers initialized once per managed process,0RNG,null original hashes/false original provenance. Old Program.Main not selected.",
        "adjacentNotImplemented": "Independent nfl1603-1604 selected-pointer field-goal attempts, following result-switch counters/transitions, preceding fire producer.",
        "unqualified": documents["execution-admission.json"]["unqualified"],
        "publicationAuthorized": False, "selfApproval": False, "wholeTaskComplete": False,
        "parentPinnedHeadAndIndex": "Parent HEAD45f045612ab52611edea31a782696478cde1aca6/empty staging; no author git/index/branch access or mutation.",
        "finalOuterObservation": {
            "reservation": pin(LEDGER / "0070-M3-reservation.json"),
            "completionPath": str(LEDGER / "0070-M3-completion.json"),
            "actualExitReceiptPath": str(LEDGER / "0070-M3-outer-exit.json"),
            "qualification": "M3 completion and enclosing-shell actual exit are finalized after this file; no invented future exit/hash or self-referential receipt claim."
        }
    }
    handoff_pin = write_json(OUT / "source-write-handoff.json", handoff_document)
    return {"status": handoff_document["status"], "handoff": handoff_pin, "counts": counters()["counts"]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--admission", required=True)
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--controls", action="store_true")
    parser.add_argument("--inspect-results", action="store_true")
    parser.add_argument("--handoff", action="store_true")
    args = parser.parse_args()
    require(Path(args.admission) == OUT / "execution-admission.json", "Only the exact coordinator admission is accepted")
    selected = [args.prepare, args.controls, args.inspect_results, args.handoff]
    require(sum(selected) == 1, "Exactly one admitted phase must be selected")
    node, kind, work = ("M1", "metadata", prepare) if args.prepare else (
        ("D1", "drivers", campaign) if args.controls else (
            ("M2", "metadata", inspect_results) if args.inspect_results else ("M3", "metadata", handoff)))
    envelope = claim_outer(node, kind)
    try:
        result = work()
        print(json.dumps(result, sort_keys=True), flush=True)
        finish_outer(envelope, 0, result=result)
        return 0
    except Exception as error:
        message = str(error)
        print(json.dumps({"status": "failed-stop-needs-precise-continuation", "node": node,
                          "error": message, "counts": counters()["counts"]}), flush=True)
        finish_outer(envelope, 1, error=message)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
