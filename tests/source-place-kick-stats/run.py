#!/usr/bin/env python3
"""Run the specifically admitted place-kick component graph, without old campaigns."""
import argparse
import ast
import copy
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import time


ROOT = Path(__file__).resolve().parents[2]
B = ROOT / ".squad/.scratch/source-following-active-p1"
AUTH = B / "metadata/source-place-kick-stats"
PREP = AUTH / "preparation/continuation-03"
VALIDATION = AUTH / "validation/continuation-03"
PROCESS = AUTH / "process-state"
JOURNAL = PROCESS / "continuation-03"
WINDOW_START = PROCESS / "continuation-02/phase-start.json"
CONTINUATION = AUTH / "execution-continuation-03.json"
REVISION_ROOT = AUTH / "preparation/continuation-04"
UNIT_RESULT = AUTH / "validation/continuation-04/unit-result.json"
ENV_UTILITY = "/usr/bin/env"
ENV_SHA256 = "886e5fa8be716b03eeda48856e0f04493f2969fc148fb7812594ffac647fa050"
PYTHON = "/usr/bin/python3.12"
DOTNET = "/home/long2know/.dotnet/dotnet"
CSC = "/home/long2know/.dotnet/sdk/10.0.100/Roslyn/bincore/csc.dll"
FIXTURE = "tests/source-offensive-stats/Program.cs"
HARNESS = "tests/source-place-kick-stats/Program.cs"
CONTROLLERS = (
    "tests/source-place-kick-stats/run.py",
    "tests/source-place-kick-stats/test_runner.py",
    "tests/source-place-kick-stats/README.md",
)
GUARD = "tests/contracts/run.py"
STATE = "Assets/Scripts/Gameplay/SimulationState.cs"
CONTRACTS = "Assets/Scripts/Contracts/SourceMatchContracts.cs"
SNAPSHOT = "Assets/Scripts/Gameplay/GameSnapshot.cs"
CALLER = "Assets/Scripts/Gameplay/GameSimulation.Source.cs"
RULES = "Assets/Scripts/Gameplay/SourceMatchRules.cs"
C2 = "Assets/Scripts/Gameplay/SourceC2Callbacks.cs"
FLOW = "Assets/Scripts/Gameplay/SourceMatchFlow.cs"
AUTHORITY_PINS = {
    "contract.json": (65322, "3c21a47528ebb0aefcd60670be25abc3636464d859889ee8f801d0cefabfc212"),
    "implementation-admission.json": (
        11925, "567dcbe122df01a56ef4968b2d47742c127114bd9ed6704db921d37ea2095dfc"),
    "execution-admission.json": (
        9808, "0825c1707fa32fc9d78ea4b94e33616be92a1970c166f1bbd0629f3c7fa73164"),
    "environment-diagnostic-admission.json": (
        5663, "9580adae26202f828b96cabffd8d711bef679820a741b0d9e2132e37dbde9d9c"),
    "execution-continuation-01.json": (
        9425, "fd9dd1a2e84c2514ba9b8285150228f74893bbd3d61e8472640e440a825ccf62"),
    "execution-continuation-02.json": (
        9079, "77cdf9ec4a164cad0edf53468a5f547fa862013120ec505d56a6dc80335a90a7"),
    "execution-continuation-03.json": (
        7799, "9235ac3a85be2bf33af91ab7b3a4f8b7e76fde03d5cb32df2a13a42748141889"),
}
DIRECT_BEFORE = (
    "            SourcePlayEndInput endingInput = pending == null ? new SourcePlayEndInput(\n"
    "                Model.SourceRules, cause, SourceBallTypeFor(Model.BallKind), Model.SourceFlags,\n"
)
DIRECT_AFTER = (
    "            SourcePlayEndInput endingInput = pending == null ? new SourcePlayEndInput(\n"
    "                Model.SourcePlaybook, Model.SourceOffensePlay, Model.SourceRules, cause,\n"
    "                SourceBallTypeFor(Model.BallKind), Model.SourceFlags,\n"
)
PENDING_BEFORE = (
    "            Input = new SourcePlayEndInput(model.SourceRules, cause, ballType, model.SourceFlags,\n"
)
PENDING_AFTER = (
    "            Input = new SourcePlayEndInput(model.SourcePlaybook, model.SourceOffensePlay,\n"
    "                model.SourceRules, cause, ballType, model.SourceFlags,\n"
)
NEW_ARRAYS = (
    "        internal readonly int[] SourceFieldGoalAttempts = new int[2];\n"
    "        internal readonly int[] SourceFieldGoals = new int[2];\n"
)
NEW_TAIL = (
    "\n"
    "            int initialSide = input.State.InitialOffenseSide;\n"
    "            SourcePlayDefinition canonicalFieldGoal;\n"
    "            if (input.SelectedPlaybook != null\n"
    '                && input.SelectedPlaybook.Plays.TryGetValue("play_field_goal", out canonicalFieldGoal)\n'
    "                && ReferenceEquals(input.InitialOffensePlay, canonicalFieldGoal))\n"
    "                SourceFieldGoalAttempts[initialSide] = checked(SourceFieldGoalAttempts[initialSide] + 1);\n"
    "            if (result == SourcePlayResult.FieldGoal)\n"
    "                SourceFieldGoals[initialSide] = checked(SourceFieldGoals[initialSide] + 1);\n"
)
SNAPSHOT_BEFORE = "                Match = new SourceMatchView(new SourcePlayEndStatsView(\n"
SNAPSHOT_AFTER = (
    "                Match = new SourceMatchView(new SourcePlaceKickStatsView(\n"
    "                    model.SourceFieldGoalAttempts[(int)TeamId.Home],\n"
    "                    model.SourceFieldGoalAttempts[(int)TeamId.Away],\n"
    "                    model.SourceFieldGoals[(int)TeamId.Home],\n"
    "                    model.SourceFieldGoals[(int)TeamId.Away]),\n"
    "                    new SourcePlayEndStatsView(\n"
)
INPUT_PROPERTIES = (
    "        public SourcePlaybook SelectedPlaybook { get; }\n"
    "        public SourcePlayDefinition InitialOffensePlay { get; }\n"
)
INPUT_NULLS = "            SelectedPlaybook = null;\n            InitialOffensePlay = null;\n"
PURE_FUNCTIONS = (
    "static_check", "csharp_tokens", "token_values", "token_locations", "matching_token",
    "method_region", "method_text", "plan_call", "restore_place_kick_stats_caller",
    "restore_play_end_stats_caller", "require_caller_wiring",
)
PURE_CONSTANTS = (
    "CSHARP_TOKEN", "CALLER_SOURCE", "FLOW_SOURCE", "CALLER_PREEDIT_SHA256",
    "LEGACY_KICKOFF_BODY_SHA256", "LEGACY_CALL", "CURRENT_CALL", "CURRENT_ASSIGNMENT",
    "LATE_ASSIGNMENT_ANCHOR", "CPU_GUARD",
)
FIXTURE_EXPECTED = {
    "origin": "SYNTHETIC/NON-ORIGINAL; no original animation or source gameplay ticks",
    "clipKey": "SYNTHETIC_COMPLETION_SNAPSHOT_FRAME",
    "programKey": "synthetic_completion_snapshot_frame_only",
    "sourceFrames": 1,
    "selectedFrame": 0,
    "joints": 15,
    "actorsInitialized": 14,
    "layersInitialized": 28,
    "initializationStepsPerLayer": 1,
    "program": "NewSequence,NextFrame(1),End",
    "randomRequests": 0,
    "originalProvenanceSupplied": False,
}
CEILINGS = {"metadata": 6, "unit": 2, "driver": 1, "csc": 20, "managed": 19}


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def utc():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON key: " + key)
        result[key] = value
    return result


def read_json(path):
    return json.loads(Path(path).read_bytes(), object_pairs_hook=unique_object)


def record(path):
    path = Path(path)
    raw = path.read_bytes()
    return {"path": str(path), "bytes": len(raw), "sha256": digest(raw)}


def pinned(path, size, sha):
    raw = Path(path).read_bytes()
    require(len(raw) == size and digest(raw) == sha, "Pinned input changed: " + str(path))
    return raw


def write_json(path, value):
    path = Path(path)
    require(path.is_relative_to(AUTH), "Output outside admitted root: " + str(path))
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("xb") as stream:
        stream.write((json.dumps(value, indent=2, ensure_ascii=True) + "\n").encode())
    path.chmod(0o444)
    return record(path)


def write_bytes(path, raw):
    path = Path(path)
    require(path.is_relative_to(AUTH), "Output outside admitted root")
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("xb") as stream:
        stream.write(raw)
    path.chmod(0o444)
    return record(path)


def authorities():
    values = {}
    for name, (size, sha) in AUTHORITY_PINS.items():
        values[name] = json.loads(pinned(AUTH / name, size, sha), object_pairs_hook=unique_object)
    for name in ("implementation-admission.json", "execution-admission.json",
                 "environment-diagnostic-admission.json", "execution-continuation-01.json",
                 "execution-continuation-02.json", "execution-continuation-03.json"):
        require(values[name]["approved"] is True, "Admission not approved")
        require(values[name]["worktree"] == str(ROOT) and values[name]["baseB"] == str(B),
                "Admission worktree/baseB mismatch")
    return values


def clean_environment(reservation=None, phase=None):
    roots = {
        "HOME": PROCESS / "home",
        "TMP": PROCESS / "scratch",
        "TEMP": PROCESS / "scratch",
        "TMPDIR": PROCESS / "scratch",
        "DOTNET_CLI_HOME": PROCESS / "cli",
        "NUGET_PACKAGES": PROCESS / "nuget/packages",
        "NUGET_HTTP_CACHE_PATH": PROCESS / "nuget/http",
        "DOTNET_BUNDLE_EXTRACT_BASE_DIR": PROCESS / "bundle",
    }
    for path in roots.values():
        path.mkdir(parents=True, exist_ok=True)
    env = {name: str(path) for name, path in roots.items()}
    env.update({
        "TZ": "UTC", "LANG": "C", "LC_ALL": "C", "SHLVL": "1",
        "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_MULTILEVEL_LOOKUP": "0",
        "DOTNET_NOLOGO": "1",
        "DOTNET_EnableDiagnostics": "0",
        "COMPlus_EnableDiagnostics": "0",
        "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "true",
        "DOTNET_CLI_USE_MSBUILD_SERVER": "0",
        "MSBUILDDISABLENODEREUSE": "1",
        "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH": "0",
        "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT": "1",
        "DOTNET_ROLL_FORWARD": "Disable",
        "DOTNET_ROLL_FORWARD_TO_PRERELEASE": "0",
        "CORECLR_ENABLE_PROFILING": "0",
        "DOTNET_STARTUP_HOOKS": "",
        "DOTNET_ADDITIONAL_DEPS": "",
        "DOTNET_SHARED_STORE": "",
    })
    if reservation is not None:
        env["BLITZ_PLACE_KICK_RESERVATION"] = str(reservation)
    if phase is not None:
        env["BLITZ_PLACE_KICK_PHASE"] = phase
    return env


def safe_live(relative):
    require(isinstance(relative, str) and not Path(relative).is_absolute(),
            "Source must be relative")
    require(".." not in Path(relative).parts, "Parent source path")
    path = ROOT
    for part in Path(relative).parts:
        path /= part
        require(not path.is_symlink(), "Symlink source: " + relative)
    require(path.is_file(), "Missing source: " + relative)
    return path


def replace_once(text, before, after):
    require(text.count(before) == 1, "Nonunique actual-source anchor")
    require(before != after, "Mutation/inverse did not change bytes")
    return text.replace(before, after)


def block(text, prefix, trailing_lf=False):
    require(text.count(prefix) == 1, "Nonunique block: " + prefix)
    start = text.index(prefix)
    opening = text.index("\n        {", start)
    end = text.index("\n        }", opening) + len("\n        }")
    if trailing_lf:
        require(text[end:end + 1] == "\n", "Block lacks exact LF")
        end += 1
    return start, end, text[start:end]


def load_pure_guard(raw):
    tree = ast.parse(raw.decode("utf-8"), filename=GUARD)
    selected = []
    seen = set()
    for node in tree.body:
        name = None
        if isinstance(node, (ast.FunctionDef, ast.ClassDef)):
            name = node.name
        elif isinstance(node, ast.Assign) and len(node.targets) == 1:
            if isinstance(node.targets[0], ast.Name):
                name = node.targets[0].id
        if name in PURE_FUNCTIONS + PURE_CONSTANTS + ("StaticAssertionFailure",):
            require(name not in seen, "Duplicate pure guard definition")
            seen.add(name)
            selected.append(node)
    require(seen == set(PURE_FUNCTIONS + PURE_CONSTANTS + ("StaticAssertionFailure",)),
            "Pure guard allowlist incomplete")
    module = ast.Module(body=selected, type_ignores=[])
    namespace = {"hashlib": hashlib, "re": re}
    exec(compile(ast.fix_missing_locations(module), GUARD + ":pure-only", "exec"), namespace)
    return namespace


def preservation(contents, guard_raw, contract, implementation):
    text = {name: raw.decode("utf-8") for name, raw in contents.items()}
    inverse = {}
    inverse[STATE] = replace_once(replace_once(text[STATE], NEW_ARRAYS, ""), NEW_TAIL, "")
    old = text[CONTRACTS]
    start = old.index("    public sealed class SourcePlaceKickStatsView\n")
    end = old.index("    public sealed class SourcePlayEndStatsView\n", start)
    new_dto = old[start:end]
    require(new_dto.count("public int ") == 4 and new_dto.count("throw new ArgumentOutOfRangeException") == 4,
            "Four-scalar count DTO/guards changed")
    old = old[:start] + old[end:]
    old = replace_once(old, "        public SourcePlaceKickStatsView PlaceKickStats { get; }\n", "")
    prefix = "        public SourceMatchView(SourcePlaceKickStatsView placeKickStats,"
    start, end, new_constructor = block(old, prefix)
    require(old[end:end + 2] == "\n\n", "New constructor separator changed")
    inverse[CONTRACTS] = old[:start] + old[end + 2:]
    inverse[SNAPSHOT] = replace_once(text[SNAPSHOT], SNAPSHOT_AFTER, SNAPSHOT_BEFORE)
    inverse[CALLER] = replace_once(text[CALLER], DIRECT_AFTER, DIRECT_BEFORE)
    old = replace_once(replace_once(text[RULES], INPUT_PROPERTIES, ""), INPUT_NULLS, "")
    prefix = "        public SourcePlayEndInput(SourcePlaybook selectedPlaybook,"
    start, end, _ = block(old, prefix, trailing_lf=True)
    require(old[start - 1:start] == "\n", "New input overload separator changed")
    inverse[RULES] = old[:start - 1] + old[end:]
    inverse[C2] = replace_once(text[C2], PENDING_AFTER, PENDING_BEFORE)
    guard_text = guard_raw.decode("utf-8")
    start = guard_text.index("def restore_place_kick_stats_caller(caller):\n")
    end = guard_text.index("def restore_play_end_stats_caller(caller):\n", start)
    guard_helper = guard_text[start:end]
    restored_guard = guard_text[:start] + guard_text[end:]
    inverse[GUARD] = replace_once(
        restored_guard, "    caller = restore_place_kick_stats_caller(caller)\n", "")
    rows = []
    for admitted in implementation["sevenPreciseReleases"]:
        relative = admitted["path"]
        raw = inverse[relative].encode("utf-8")
        require(len(raw) == admitted["beforeBytes"] and digest(raw) == admitted["beforeSha256"],
                "Whole-file inverse failed: " + relative)
        rows.append({
            "path": relative, "inverseBytes": len(raw), "inverseSha256": digest(raw),
            "beforePinMatched": True,
        })
    constructor_rows = []
    starts = list(re.finditer(r"^        public SourceMatchView\(", text[CONTRACTS], re.M))
    require(len(starts) == 7, "Expected seven constructors")
    for match, expected in zip(starts[:6], contract["preservedBlocks"]["sixCurrentConstructors"]):
        start = match.start()
        opening = text[CONTRACTS].index("\n        {", start)
        end = text[CONTRACTS].index("\n        }", opening) + len("\n        }")
        raw = text[CONTRACTS][start:end].encode()
        require(len(raw) == expected["bytesNoTrailingLf"] and digest(raw) == expected["sha256"],
                "Existing constructor block changed")
        constructor_rows.append({"bytes": len(raw), "sha256": digest(raw), "matched": True})
    pure = load_pure_guard(guard_raw)
    tokens = pure["csharp_tokens"](new_constructor)
    call = pure["token_locations"](tokens, "this(")
    require(len(call) == 1, "New constructor must delegate once")
    opening = call[0] + 1
    closing = pure["matching_token"](tokens, opening, "(", ")")
    names = [token for token, _, _ in tokens[opening + 1:closing] if token != ","]
    require(names == contract["preservedBlocks"]["newSeventhForwardingVector"],
            "Exact35-name delegation changed")
    _, _, completion = block(text[STATE], "        internal void AccumulateSourcePassCompletion(",
                             trailing_lf=True)
    expected = contract["preservedBlocks"]["completionMethod"]
    require(len(completion.encode()) == expected["bytesIncludingTrailingLf"]
            and digest(completion.encode()) == expected["sha256"], "Completion method changed")
    _, _, current_chain = block(text[STATE], "        internal void AccumulateSourcePlayEndStats(",
                                trailing_lf=True)
    _, _, prior_chain = block(inverse[STATE], "        internal void AccumulateSourcePlayEndStats(",
                              trailing_lf=True)
    require(current_chain.count(NEW_TAIL) == 1, "Independent tail missing")
    require(current_chain.split(NEW_TAIL)[0] == prior_chain[:-len("        }\n")],
            "Prior chain prefix changed")
    require(text[C2].count(PENDING_AFTER) == 1, "Pending input capture not unique")
    require("Model.SourceFumbleX, Model.SourceLastResult) : pending.Input;" in text[CALLER],
            "Pending input no longer used")
    caller_result = pure["require_caller_wiring"](contents[FLOW], contents[CALLER])
    return {
        "schema": "source-place-kick-statistics-preservation/v1",
        "wholeFileInverses": rows,
        "oldConstructors": constructor_rows,
        "delegatedNames": names,
        "delegatedArgumentCount": len(names),
        "completion": {"bytes": len(completion.encode()), "sha256": digest(completion.encode())},
        "priorChainPrefix": {
            "bytes": len(prior_chain[:-len("        }\n")].encode()),
            "sha256": digest(prior_chain[:-len("        }\n")].encode()),
            "unchanged": True,
        },
        "newDto": {"bytes": len(new_dto.encode()), "sha256": digest(new_dto.encode())},
        "newGuardHelper": {"bytes": len(guard_helper.encode()), "sha256": digest(guard_helper.encode())},
        "caller": caller_result,
        "c2ConstructorInverse": True,
        "genuineCommonOrSchedulerExecuted": False,
    }


def literal_inventory(contract):
    source = contract["literalInventoryFrozenBeforeCompile"]
    return {
        "schema": "source-place-kick-statistics-literals/v1",
        "contract": {"bytes": AUTHORITY_PINS["contract.json"][0],
                     "sha256": AUTHORITY_PINS["contract.json"][1]},
        "producerNames": source["standardOutputFieldsInOrder"],
        "producerUnits": source["standardOutputUnitsInOrder"],
        "inputNames": source["standardInputColumns"],
        "inputUnits": source["standardInputUnits"],
        "producerCases": source["producerCases"],
        "apiCases": source["apiAllocationSnapshotCases"],
        "caseCount": 32, "fieldCount": 603,
        "fixtureExpected": FIXTURE_EXPECTED,
    }


def flattened(inventory):
    result = []
    for row in inventory["producerCases"] + inventory["apiCases"]:
        names = row.get("fields", inventory["producerNames"])
        units = row.get("units", inventory["producerUnits"])
        require(len(names) == len(units) == len(row["expected"]), "Literal tuple size changed")
        for name, unit, value in zip(names, units, row["expected"]):
            require(type(value) is int, "Expected values must be literal ints")
            result.append({"case": row["id"], "field": row["id"] + "." + name,
                           "unit": unit, "expected": value})
    return result


def validate_inventory(inventory, contract):
    require(inventory == literal_inventory(contract), "Literal inventory differs from frozen contract")
    rows = inventory["producerCases"] + inventory["apiCases"]
    require(len(rows) == 32 and len({row["id"] for row in rows}) == 32, "Case count/order changed")
    require(len(inventory["producerCases"]) == 26 and len(inventory["producerNames"]) == 22,
            "Producer literal shape changed")
    require(all(len(row["input"]) == 15 and len(row["expected"]) == 22
                for row in inventory["producerCases"]), "Producer operands changed")
    require(len(flattened(inventory)) == 603, "Literal field count changed")


def mutation_inventory(contents, contract, inventory):
    attempt = "                SourceFieldGoalAttempts[initialSide] = checked(SourceFieldGoalAttempts[initialSide] + 1);\n"
    made = "                SourceFieldGoals[initialSide] = checked(SourceFieldGoals[initialSide] + 1);\n"
    identity = "                && ReferenceEquals(input.InitialOffensePlay, canonicalFieldGoal))\n"
    success = "            if (result == SourcePlayResult.FieldGoal)\n"
    completion = (
        "            else if ((flags & SourceGameFlags.ForwardPass) != 0)\n"
        "                AccumulateSourcePassCompletion(input, resolution, carrierId);\n"
    )
    changes = [
        (attempt, attempt.replace("+ 1", "+ 0")),
        (attempt, attempt.replace("[initialSide]", "[side]")),
        (identity, "                && ReferenceEquals(input.InitialOffensePlay, canonicalFieldGoal)\n"
         "                && result == SourcePlayResult.FieldGoal)\n"),
        (identity, "                && canonicalFieldGoal != null && input.State.PlayType == SourcePlayType.FieldGoal)\n"),
        (identity, "                && canonicalFieldGoal != null && input.InitialOffensePlay != null\n"
         "                && (input.InitialOffensePlay.Flags & SourcePlayFlags.FieldGoal) != 0)\n"),
        (identity, "                && canonicalFieldGoal != null && input.InitialOffensePlay != null\n"
         "                && input.InitialOffensePlay.Identity == canonicalFieldGoal.Identity)\n"),
        (made, made.replace("+ 1", "+ 0")),
        (made, made.replace("[initialSide]", "[side]")),
        (success, "            if (result == SourcePlayResult.FieldGoal && input.InitialOffensePlay != null\n"
         '                && input.InitialOffensePlay.Identity == "play_field_goal")\n'),
        (success, "            if ((flags & SourceGameFlags.ThroughUprights) != 0)\n"),
        (completion, "            else if ((flags & SourceGameFlags.ForwardPass) != 0)\n"
         "            {\n"
         "                AccumulateSourcePassCompletion(input, resolution, carrierId);\n"
         "                return;\n"
         "            }\n"),
        ("                    model.SourceFieldGoalAttempts[(int)TeamId.Home],\n",
         "                    model.SourceFieldGoalAttempts[(int)TeamId.Away],\n"),
        ("                    model.SourceFieldGoals[(int)TeamId.Away]),\n",
         "                    model.SourceFieldGoals[(int)TeamId.Home]),\n"),
        (PENDING_AFTER, PENDING_AFTER.replace("model.SourceOffensePlay", "null")),
        ("            SelectedPlaybook = selectedPlaybook;\n", "            SelectedPlaybook = null;\n"),
        ("            HomeFieldGoals = homeFieldGoals;\n", "            HomeFieldGoals = awayFieldGoals;\n"),
        (PENDING_AFTER, PENDING_AFTER.replace("model.SourcePlaybook", "null")),
    ]
    controls = contract["freshCompiledSemanticControls"]["controls"]
    require(len(changes) == len(controls) == 17, "Control count changed")
    fields = flattened(inventory)
    cases = [row["id"] for row in inventory["producerCases"] + inventory["apiCases"]]
    result = []
    for control, (before, after) in zip(controls, changes):
        raw = contents[control["path"]]
        changed = replace_once(raw.decode(), before, after).encode()
        prefix = control["prefixFieldsIncludingFailure"]
        require(fields[prefix - 1]["field"] == control["failedField"], "Control global prefix changed")
        require(cases[control["completedCases"]] == fields[prefix - 1]["case"],
                "Control completed-case count changed")
        result.append(dict(control, anchor=before, replacement=after,
                           capturedSourceSha256=digest(raw), mutatedSourceSha256=digest(changed)))
    return {"schema": "source-place-kick-statistics-mutations/v1", "controls": result}


def static_negatives(guard_raw, flow_raw, caller_raw):
    pure = load_pure_guard(guard_raw)
    caller = caller_raw.decode()
    start, end, _ = pure["method_region"](caller, "ReleaseSourcePunt", "int actorId")
    punt = replace_once(caller[start:end], "Model.SourceKickReturnCatch = -1;",
                        "Model.SourceKickReturnCatch = -2;")
    changes = [
        replace_once(caller, DIRECT_AFTER, DIRECT_BEFORE),
        replace_once(caller, DIRECT_AFTER, DIRECT_AFTER.replace("Model.SourceOffensePlay",
                                                              "Model.SourceDefensePlay")),
        replace_once(caller, DIRECT_AFTER, "            /*\n" + DIRECT_AFTER + "            */\n" + DIRECT_BEFORE),
        caller + "\n" + DIRECT_AFTER,
        caller + "\n",
        caller[:start] + punt + caller[end:],
    ]
    plan = authorities()["contract.json"]["freshBoundedExecutionGraphProposal"]["phases"][2]["staticNegativeControls"]
    require(len(changes) == len(plan) == 6, "Static control count changed")
    results = []
    for case, changed in zip(plan, changes):
        try:
            pure["require_caller_wiring"](flow_raw, changed.encode())
        except pure["StaticAssertionFailure"] as error:
            require(error.field == case["expectedFailure"], "Wrong static negative failure: " + case["id"])
            results.append(dict(case, actualFailure=error.field, changedSha256=digest(changed.encode()),
                                compiled=False, semanticKill=False, liveSourceModified=False))
        else:
            raise RuntimeError("Static negative survived: " + case["id"])
    return results


def verify_fixture(summary):
    fixture = summary["fixture"]
    for name, expected in FIXTURE_EXPECTED.items():
        require(fixture.get(name) == expected and type(fixture.get(name)) is type(expected),
                "Fixture diagnostic mismatch: " + name)
    require(summary["entryPoint"] == "SourcePlaceKickStatsChecks.Main", "Wrong selected entrypoint")
    for name in ("genuineCommonOrSchedulerExecuted", "originalAssetsAdmitted", "wholeGameParityClaimed"):
        require(summary[name] is False, "Unqualified claim: " + name)


def verify_fields(actual, expected, all_equal):
    require(len(actual) == len(expected), "Observation prefix length changed")
    for index, (observed, literal) in enumerate(zip(actual, expected)):
        for key, value in literal.items():
            require(observed.get(key) == value and type(observed.get(key)) is type(value),
                    "Name/unit/expected/order changed")
        if all_equal or index < len(expected) - 1:
            require(type(observed.get("actual")) is int and observed["actual"] == literal["expected"],
                    "Clean prefix changed")
        else:
            require(type(observed.get("actual")) is int and observed["actual"] != literal["expected"],
                    "Failed field did not have a literal mismatch")


def verify_pass(summary, inventory, exit_code):
    require(exit_code == 0 and summary["schema"] == "source-place-kick-statistics-component/v1"
            and summary["status"] == "passed" and summary["failedField"] is None
            and summary["error"] is None, "Clean component did not pass normally")
    require(summary["completedCases"] == 32 and summary["fieldCount"] == 603, "Clean counts changed")
    cases = inventory["producerCases"] + inventory["apiCases"]
    require(summary["completedCaseIds"] == [row["id"] for row in cases], "Case order changed")
    verify_fields(summary["fields"], flattened(inventory), True)
    verify_fixture(summary)


def verify_control(summary, inventory, control, exit_code):
    require(exit_code == 2 and summary["schema"] == "source-place-kick-statistics-component/v1"
            and summary["status"] == "assertion-failed" and summary["error"] is None,
            "Technical failure is not a semantic kill")
    require(summary["failedField"] == control["failedField"], "Wrong named semantic failure")
    require(summary["fieldCount"] == control["prefixFieldsIncludingFailure"]
            and summary["completedCases"] == control["completedCases"], "Wrong semantic prefix")
    cases = inventory["producerCases"] + inventory["apiCases"]
    require(summary["completedCaseIds"] == [row["id"] for row in cases[:control["completedCases"]]],
            "Control case prefix changed")
    verify_fields(summary["fields"], flattened(inventory)[:control["prefixFieldsIncludingFailure"]], False)
    verify_fixture(summary)


def closure_shape(rows):
    names = [row["relativePath"] for row in rows]
    require(len(names) == len(set(names)) == 77, "Exact77 distinct source paths required")
    categories = {}
    for name in names:
        parts = Path(name).parts
        require(parts[:2] == ("Assets", "Scripts") and name.endswith(".cs"), "Unexpected source selector")
        require(".." not in parts and not Path(name).is_absolute(), "Invalid source path")
        categories[parts[2]] = categories.get(parts[2], 0) + 1
    require(categories == {"AssetImport": 27, "Contracts": 10, "Gameplay": 38, "Presentation": 2},
            "Source closure categories changed")
    require([name for name in names if "/Presentation/" in name] == [
        "Assets/Scripts/Presentation/SourceAudioFunctions.cs",
        "Assets/Scripts/Presentation/SourceC2SoundFunctions.cs"], "Presentation selector changed")


def contents_from_manifest(manifest):
    return {row["relativePath"]: pinned(row["path"], row["bytes"], row["sha256"])
            for row in manifest["production"]}


def custody_row(path):
    stat = Path(path).stat()
    return {
        "size": stat.st_size, "mode": stat.st_mode, "device": stat.st_dev, "inode": stat.st_ino,
        "mtimeNs": stat.st_mtime_ns, "ctimeNs": stat.st_ctime_ns,
    }


def input_manifest():
    base = json.loads(pinned(PREP / "admitted-inputs.json", 161594,
                             "29bc11f825fa82e16a61972a27da31dea317b69df64890336117ee240de93b6d"),
                      object_pairs_hook=unique_object)
    revision = read_json(REVISION_ROOT / "controller-input-revision.json")
    require(revision["baseManifest"] == record(PREP / "admitted-inputs.json")
            and revision["authority"] == record(CONTINUATION),
            "Explicit controller revision binding changed")
    require(revision["failedUnit"]["sha256"] ==
            "8ef2a59ef1b39f9c3329519c71372c8d0d34c88b37af7a540fb7727a73cd052d",
            "Prior failed unit cannot be hidden")
    require([row["relativePath"] for row in revision["controllers"]] ==
            [row["relativePath"] for row in base["controllers"]], "Controller revision paths changed")
    result = copy.deepcopy(base)
    result["controllers"] = revision["controllers"]
    require({key: value for key, value in result.items() if key != "controllers"} ==
            {key: value for key, value in base.items() if key != "controllers"},
            "Game/preparation sections must remain identical")
    return result


def append_controller_revision():
    values = authorities()
    admitted = values["execution-continuation-03.json"]
    base = json.loads(pinned(PREP / "admitted-inputs.json", 161594,
                             "29bc11f825fa82e16a61972a27da31dea317b69df64890336117ee240de93b6d"),
                      object_pairs_hook=unique_object)
    verify_input_pins(base, live=False)
    failed = json.loads(pinned(VALIDATION / "unit-result.json", 3822,
                               "8ef2a59ef1b39f9c3329519c71372c8d0d34c88b37af7a540fb7727a73cd052d"),
                        object_pairs_hook=unique_object)
    require(failed["status"] == "failed" and failed["testsRun"] == 8
            and len(failed["failures"]) == 1 and not failed["errors"]
            and len(failed["staticNegativeControls"]) == 6, "Exact previous unit failure required")
    before = {}
    before_rows = []
    for item in admitted["onlyPermittedEdits"]:
        path = REVISION_ROOT / "before" / Path(item["path"]).name
        raw = pinned(path, item["beforeBytes"], item["beforeSha256"])
        before[item["path"]] = raw.decode()
        before_rows.append(record(path))
    unit_name = "tests/source-place-kick-stats/test_runner.py"
    current_unit = safe_live(unit_name).read_bytes().decode()
    old_tree = ast.parse(before[unit_name])
    new_tree = ast.parse(current_unit)
    old_class = next(node for node in old_tree.body if isinstance(node, ast.ClassDef))
    new_class = next(node for node in new_tree.body if isinstance(node, ast.ClassDef))
    for method in old_class.body:
        if isinstance(method, ast.FunctionDef) and method.name.startswith("test_"):
            other = next(node for node in new_class.body if isinstance(node, ast.FunctionDef)
                         and node.name == method.name)
            expected = ast.get_source_segment(before[unit_name], method)
            if method.name.startswith("test_08"):
                expected = replace_once(expected,
                    '"actual": "2",\n                           "actualValuePersisted": True',
                    '"actual": "VALUE_NOT_PERSISTED",\n                           "actualValuePersisted": False')
            require(expected == ast.get_source_segment(current_unit, other),
                    "Only the exact diagnostic expectation may change")
    driver_name = "tests/source-place-kick-stats/run.py"
    current_driver = safe_live(driver_name).read_bytes().decode()
    for name in ("clean_environment", "environment_boundary"):
        old_node = next(node for node in ast.parse(before[driver_name]).body
                        if isinstance(node, ast.FunctionDef) and node.name == name)
        new_node = next(node for node in ast.parse(current_driver).body
                        if isinstance(node, ast.FunctionDef) and node.name == name)
        require(ast.get_source_segment(before[driver_name], old_node) ==
                ast.get_source_segment(current_driver, new_node), "Environment/privacy formatter changed")
    start = "        observed_env = dict(os.environ)\n"
    end = "        self.started_at = time.time()\n"
    require(before[driver_name].split(start, 1)[1].split(end, 1)[0] ==
            current_driver.split(start, 1)[1].split(end, 1)[0], "Strict environment block changed")
    for row in base["production"] + base["harnesses"] + [base["guard"]]:
        pinned(safe_live(row["relativePath"]), row["bytes"], row["sha256"])
    controllers = []
    for relative in CONTROLLERS:
        live = safe_live(relative)
        item = write_bytes(REVISION_ROOT / "captured-controller" / relative, live.read_bytes())
        controllers.append(dict(item, relativePath=relative, livePath=str(live)))
    return write_json(REVISION_ROOT / "controller-input-revision.json", {
        "schema": "source-place-kick-explicit-controller-input-revision/v1",
        "actualUtc": utc(), "authority": record(CONTINUATION),
        "baseManifest": record(PREP / "admitted-inputs.json"),
        "literalFreeze": record(PREP / "literal-freeze.json"),
        "expectedInventory": record(PREP / "expected-inventory.json"),
        "mutationInventory": record(PREP / "mutation-inventory.json"),
        "failedUnit": record(VALIDATION / "unit-result.json"),
        "beforeVersions": before_rows, "controllers": controllers,
        "onlyControllerRowsReplaced": True, "allGamePreparationSectionsIdentical": True,
        "environmentStrictnessAndRedactionUnchanged": True,
        "unitStartupBookkeepingNotMetadataInvocation": True,
    })


def verify_input_pins(manifest, live=False):
    for name, (size, sha) in AUTHORITY_PINS.items():
        pinned(AUTH / name, size, sha)
    rows = manifest["production"] + manifest["harnesses"] + manifest["controllers"] + [manifest["guard"]]
    for row in rows:
        pinned(row["path"], row["bytes"], row["sha256"])
        if live:
            pinned(safe_live(row["relativePath"]), row["bytes"], row["sha256"])
    for row in manifest["immutableMetadata"]:
        pinned(row["path"], row["bytes"], row["sha256"])
    for row in manifest["frozenCustody"]:
        require(custody_row(ROOT / row["relativePath"]) == row["stat"], "Frozen custody changed")


def verify_tool_references(manifest, profile):
    for tool in manifest["tools"].values():
        pinned(tool["path"], tool["bytes"], tool["sha256"])
    for row in manifest["references"][profile]:
        pinned(row["path"], row["bytes"], row["sha256"])


def prior_history():
    values = authorities()
    continuation = values["execution-continuation-01.json"]
    latest = values["execution-continuation-02.json"]
    require(latest["cumulativeCeilings"]["metadataAlreadyConsumed"] == 3
            and latest["cumulativeCeilings"]["metadataStartsIncludingAllFailuresAndDiagnostic"] == 6,
            "Latest cumulative authority changed")
    require(continuation["cumulativeLimitsAfterContinuation"]["metadataAlreadyConsumed"] == 2
            and continuation["cumulativeLimitsAfterContinuation"]["metadataPythonStartsIncludingFailureAndDiagnostic"] == 5,
            "Explicit cumulative metadata authority changed")
    diagnosis = continuation["diagnosis"]
    diagnostic_path = AUTH / diagnosis["file"]
    diagnostic = json.loads(pinned(diagnostic_path, diagnosis["bytes"], diagnosis["sha256"]),
                            object_pairs_hook=unique_object)
    require(diagnostic["status"] == "diagnosed"
            and diagnostic["missingExpectedNames"] == []
            and diagnostic["differingAdmittedNonsecretValues"] == []
            and diagnostic["unexpectedNamesExcludingExistingUnderscoreRule"] == ["SHLVL"]
            and diagnostic["knownAdmittedExtraValues"] == {"SHLVL": "1"},
            "Only the proved SHLVL correction is admitted")
    records = [record(diagnostic_path)]
    for item in diagnostic["failureArtifactStatOnly"]:
        path = Path(item["path"])
        stat = path.stat()
        actual = {
            "bytes": stat.st_size, "mode": stat.st_mode, "inode": stat.st_ino,
            "device": stat.st_dev, "mtimeNs": stat.st_mtime_ns, "ctimeNs": stat.st_ctime_ns,
        }
        require(all(actual[key] == item[key] for key in actual),
                "Original failure artifact custody changed")
        records.append(record(path))
    failure = values["environment-diagnostic-admission.json"]["preservedFailure"]
    for key in ("stdout", "outerExit"):
        item = failure[key]
        path = AUTH / item["file"]
        require(digest(path.read_bytes()) == item["sha256"], "Immutable failed-M1 evidence changed")
    observed_failure = read_json(AUTH / failure["outerExit"]["file"])
    require(observed_failure["observedChildExit"] == 1
            and observed_failure["childPid"] == 2433916
            and observed_failure["outerPid"] == 2433912, "Failed-M1 process accounting changed")
    directory = AUTH / "preparation/continuation-01"
    completion_path = directory / "environment-diagnostic-completion.json"
    completion = json.loads(pinned(completion_path, 933,
                                   "c71ca778f6662ae7f9f689ee8c63932c88e6677832e99c6b888a62a380990ccd"),
                            object_pairs_hook=unique_object)
    require(completion["status"] == "diagnosed", "Successful diagnostic required")
    started = completion["started"]
    started_value = json.loads(pinned(started["path"], started["bytes"], started["sha256"]),
                               object_pairs_hook=unique_object)
    for key in ("reservation", "script"):
        item = started_value[key]
        pinned(item["path"], item["bytes"], item["sha256"])
        records.append(item)
    outer_path = directory / "environment-diagnostic-outer.json"
    outer = read_json(outer_path)
    require(outer["observedChildExit"] == 0 and outer["diagnosticPid"] == 2453576
            and outer["outerPid"] == 2453571, "Diagnostic process accounting changed")
    records.extend([record(completion_path), record(Path(started["path"])), record(outer_path),
                    record(directory / "environment-diagnostic.stdout"),
                    record(directory / "environment-diagnostic.stderr")])
    require(diagnosis["alreadyConsumed"] == {
        "metadata": 2, "unit": 0, "driver": 0, "csc": 0, "managed": 0,
    }, "Prior consumption cannot be refunded")
    second = latest["secondPreservedPreparationFailure"]
    second_exit_path = AUTH / second["outerExit"]["file"]
    require(digest(second_exit_path.read_bytes()) == second["outerExit"]["sha256"],
            "Second failed-M1 evidence changed")
    second_exit = read_json(second_exit_path)
    require(second_exit["observedChildExit"] == 1 and second_exit["childPid"] == 2480330
            and second_exit["outerPid"] == 2480325, "Second failure process identity changed")
    for name in ("phase-start.json", "readmission-plan.json", "0001-M1-outer-command.txt",
                 "0001-M1-outer-reservation.json", "0002-M1-python-reservation.json",
                 "0002-M1-python-entered.json", "0002-M1-python-stdout.txt",
                 "0002-M1-python-stderr.txt", "0001-M1-outer-exit.json"):
        records.append(record(PROCESS / "continuation-02" / name))
    require(second["cumulativeConsumed"] == {
        "metadata": 3, "unit": 0, "driver": 0, "csc": 0, "managed": 0,
    }, "Second failed metadata start cannot be refunded")
    window = read_json(WINDOW_START)
    require(window["actualUtc"] == "2026-10-25T09:56:34.046876434Z",
            "Carried prospective window origin changed")
    return {
        "consumed": dict(second["cumulativeConsumed"], outer=3),
        "records": records, "diagnostic": diagnostic,
        "originalExactStartUtc": None, "originalExactStartConversionProved": False,
        "knownFailureUtc": "2026-10-25T09:27:04.846421+00:00",
        "knownOriginalOuterExitUtc": "2026-10-25T09:27:04Z",
        "prospectiveRemainingWorkSeconds": 2400,
        "carriedWindow": record(WINDOW_START),
        "windowRestartedOrExtended": False,
        "secondFailureDifference": "UNKNOWN",
        "explicitLauncherCorrectionAdmissions": 2,
        "compilerRuntimeCorrectionCampaigns": 0,
        "originalRecordsRewritten": False,
    }


def correction_preservation(history):
    values = authorities()
    continuation = values["execution-continuation-02.json"]
    versions = []
    before = {}
    for item in continuation["permittedMinimalChanges"]:
        path = PREP / "before" / Path(item["path"]).name
        raw = path.read_bytes()
        require(digest(raw) == item["beforeSha256"], "Latest before-version changed")
        versions.append(dict(record(path), relativePath=item["path"]))
        before[item["path"]] = raw.decode()
    earlier_versions = []
    for item in values["execution-continuation-01.json"]["minimumPermittedCorrections"]:
        path = AUTH / "preparation/continuation-02/before" / Path(item["path"]).name
        pinned(path, item["beforeBytes"], item["beforeSha256"])
        earlier_versions.append(dict(record(path), relativePath=item["path"]))
    corrected = {item["path"] for item in continuation["permittedMinimalChanges"]}
    unchanged = []
    for row in history["diagnostic"]["authoredElevenPins"]:
        if row["relativePath"] not in corrected:
            path = safe_live(row["relativePath"])
            pinned(path, row["bytes"], row["sha256"])
            unchanged.append(dict(record(path), relativePath=row["relativePath"]))
    require(len(unchanged) == 8, "Eight protected authored inputs must be unchanged")
    driver_name = "tests/source-place-kick-stats/run.py"
    current = safe_live(driver_name).read_bytes().decode()
    start = "        observed_env = dict(os.environ)\n"
    end = "        self.started_at = time.time()\n"
    old_check = before[driver_name].split(start, 1)[1].split(end, 1)[0]
    new_check = current.split(start, 1)[1].split(end, 1)[0]
    require(old_check == new_check, "Strict equality/underscore block must remain byte-identical")
    require(clean_environment()["SHLVL"] == "1", "SHLVL must be an explicit required value")
    unit_name = "tests/source-place-kick-stats/test_runner.py"
    current_unit = safe_live(unit_name).read_bytes().decode()
    old_tree = ast.parse(before[unit_name])
    new_tree = ast.parse(current_unit)
    old_class = next(node for node in old_tree.body if isinstance(node, ast.ClassDef)
                     and node.name == "PlaceKickMetadataTests")
    new_class = next(node for node in new_tree.body if isinstance(node, ast.ClassDef)
                     and node.name == "PlaceKickMetadataTests")
    for method in old_class.body:
        if isinstance(method, ast.FunctionDef) and method.name.startswith("test_0") and not method.name.startswith("test_08"):
            other = next(node for node in new_class.body if isinstance(node, ast.FunctionDef)
                         and node.name == method.name)
            require(ast.get_source_segment(before[unit_name], method)
                    == ast.get_source_segment(current_unit, other), "Only existing test08 may strengthen")
    return {
        "beforeVersions": versions + earlier_versions, "eightUnchangedAuthoredInputs": unchanged,
        "strictComparisonBlockSha256": digest((start + new_check).encode()),
        "strictComparisonAndUnderscoreRuleUnchanged": True,
        "explicitRequiredSHLVL": "1", "originalExactStartUtc": None,
        "originalExactStartConversionProved": False,
        "clockAddendum": record(VALIDATION / "clock-addendum.json"),
        "continuation": record(CONTINUATION), "cumulativeMetadataSeed": 3,
        "cumulativeMetadataCeiling": 6,
        "explicitLauncherCorrectionAdmissions": 2, "compilerRuntimeCorrectionCampaigns": 0,
    }


def environment_boundary(expected, actual_names, getter):
    actual_names = set(actual_names)
    expected_names = set(expected)
    differences = []
    for name in sorted(expected_names & actual_names):
        actual = getter(name)
        if actual != expected[name]:
            safe = (actual == "" or actual in {
                "0", "1", "true", "false", "Disable", "UTC", "C", "M1", "U1", "D1", "M2", "M3",
            } or actual.startswith(str(ROOT) + "/"))
            differences.append({
                "name": name, "expected": expected[name],
                "actual": actual if safe else "VALUE_NOT_PERSISTED",
                "actualValuePersisted": safe,
            })
    marker = getter("_") if "_" in actual_names else None
    return {
        "expectedKeyNames": sorted(expected_names),
        "actualKeyNames": sorted(actual_names),
        "missingExpectedNames": sorted(expected_names - actual_names),
        "unknownKeyNames": sorted(actual_names - expected_names - {"_"}),
        "knownAdmittedDifferences": differences,
        "existingUnderscoreMatches": marker in (None, PYTHON),
        "underscoreValue": marker if marker in (None, PYTHON) else "VALUE_NOT_PERSISTED",
        "unknownKeyValuesReadHashedOrPersisted": False,
    }


def output_budget():
    total = 0
    files = 0
    for root in (AUTH / "preparation", AUTH / "validation", PROCESS):
        if root.exists():
            for path in root.rglob("*"):
                require(not path.is_symlink(), "Symlink output")
                if path.is_file():
                    total += path.stat().st_size
                    files += 1
    require(total <= 536870912, "New output budget exceeded")
    return {"bytes": total, "files": files, "maximumBytes": 536870912}


class Ledger:
    def __init__(self, phase):
        entered_utc = utc()
        self.phase = phase
        reservation_name = os.environ.get("BLITZ_PLACE_KICK_RESERVATION")
        require(reservation_name is not None, "An enclosing-shell reservation is mandatory")
        self.reservation_path = Path(reservation_name)
        require(self.reservation_path.parent == JOURNAL, "Reservation outside continuation-03")
        self.reservation = read_json(self.reservation_path)
        require(self.reservation.get("continuation") == str(CONTINUATION)
                and self.reservation.get("continuationSha256") == AUTHORITY_PINS["execution-continuation-03.json"][1],
                "Every remaining launch must bind the explicit supplement")
        prefix = self.reservation_path.name.removesuffix("-reservation.json")
        self.entered = write_json(JOURNAL / (prefix + "-entered.json"), {
            "actualUtc": entered_utc, "pid": os.getpid(), "parentPid": os.getppid(),
            "argv": sys.orig_argv, "cwd": os.getcwd(), "continuation": record(CONTINUATION),
            "recordedBeforeEnvironmentComparison": True,
        })
        require(self.reservation["phase"] == phase, "Wrong reserved phase")
        require(self.reservation["argv"] == sys.orig_argv, "Reserved Python argv changed")
        require(self.reservation["outerPid"] == os.getppid(), "Outer process identity changed")
        require(self.reservation["cwd"] == str(ROOT) == os.getcwd(), "Wrong execution cwd")
        expected_env = clean_environment(self.reservation_path, phase)
        wrapper_argv = [ENV_UTILITY, "-i"] + [
            name + "=" + value for name, value in expected_env.items()
        ] + sys.orig_argv
        boundary = environment_boundary(expected_env, os.environ, os.environ.__getitem__)
        self.boundary = write_json(JOURNAL / (prefix + "-environment-boundary.json"), dict(
            boundary, actualUtc=utc(), pid=os.getpid(), parentPid=os.getppid(),
            argv=sys.orig_argv, cwd=os.getcwd(), continuation=record(CONTINUATION),
            reservedWrapperArgv=self.reservation.get("wrapperArgv"),
            expectedWrapperArgv=wrapper_argv,
            reservedWrapperSha256=self.reservation.get("wrapperSha256"),
            wrapperBodyHashNotYetClaimed=True,
        ))
        require(not boundary["unknownKeyNames"], "Unknown environment key names; values not read")
        require(self.reservation.get("wrapperArgv") == wrapper_argv
                and self.reservation.get("wrapperSha256") == ENV_SHA256,
                "Exact fixed env-i wrapper binding changed")
        observed_env = dict(os.environ)
        underscore = observed_env.pop("_", None)
        require(underscore in (None, PYTHON), "Unadmitted shell executable marker")
        require(observed_env == expected_env, "Python environment is not the minimal admitted environment")
        self.started_at = time.time()
        self.wrapper = record(Path(ENV_UTILITY))
        require(self.wrapper["sha256"] == ENV_SHA256, "Fixed admitted env utility changed")
        self.prefix = self.reservation_path.name.removesuffix("-reservation.json")
        self.check_counts()
        self.deadline()
        signal.signal(signal.SIGALRM, self.expired)
        remaining = 2400 - (time.time() - read_json(WINDOW_START)["actualEpoch"])
        signal.setitimer(signal.ITIMER_REAL, min(1800 if phase == "D1" else 120, remaining))
        self.started = write_json(JOURNAL / (self.prefix + "-started.json"), {
            "schema": "source-place-kick-process-start/v1",
            "actualUtc": utc(), "pid": os.getpid(), "parentPid": os.getppid(),
            "argv": sys.orig_argv, "cwd": os.getcwd(), "environment": dict(os.environ),
            "reservation": record(self.reservation_path),
            "bootstrapInputPinsDeferredToM1BeforeCompiler": phase == "M1",
            "entered": self.entered, "continuation": record(CONTINUATION),
            "safeBoundary": self.boundary, "verifiedExecWrapper": self.wrapper,
        })

    @staticmethod
    def expired(signum, frame):
        raise TimeoutError("Reserved phase timeout; no retry authorized")

    def rows(self):
        rows = []
        for path in sorted(JOURNAL.glob("????-*-reservation.json")):
            value = read_json(path)
            value["_path"] = str(path)
            rows.append(value)
        return rows

    def counts(self):
        result = prior_history()["consumed"].copy()
        for row in self.rows():
            require(row["category"] in result, "Unadmitted execution category")
            result[row["category"]] += 1
        return result

    def check_counts(self):
        counts = self.counts()
        for name, ceiling in CEILINGS.items():
            require(counts[name] <= ceiling, "Consumed execution ceiling: " + name)
        return counts

    def deadline(self):
        start = read_json(WINDOW_START)
        elapsed = time.time() - start["actualEpoch"]
        require(0 <= elapsed <= 2400, "Execution phase wall ceiling reached")
        return min(120, 2400 - elapsed)

    def finish(self, outcome, value=None):
        completion = {
            "schema": "source-place-kick-python-completion/v1",
            "actualUtc": utc(), "pid": os.getpid(), "parentPid": os.getppid(),
            "phase": self.phase, "functionOutcome": outcome,
            "actualProcessExit": "Observed subsequently by the enclosing shell, not invented here.",
            "reservation": record(self.reservation_path), "started": self.started,
            "consumed": self.check_counts(), "outputBudgetAtCompletion": output_budget(),
            "result": value,
        }
        return write_json(JOURNAL / (self.prefix + "-completion.json"), completion)

    def launch(self, category, label, argv, inputs, expected_exit, expected_prefix=None):
        require(category in ("csc", "managed"), "Only admitted compiler/component children")
        counts = self.check_counts()
        require(counts[category] < CEILINGS[category], "No remaining child execution")
        rows = self.rows()
        number = max(row["sequence"] for row in rows) + 1
        prefix = "%04d-%s" % (number, label)
        timeout = self.deadline()
        require(time.time() - self.started_at < 1800, "Driver wall ceiling reached")
        timeout = min(timeout, 1800 - (time.time() - self.started_at))
        env = clean_environment()
        stdout_path = JOURNAL / (prefix + "-stdout.txt")
        stderr_path = JOURNAL / (prefix + "-stderr.txt")
        reservation = write_json(JOURNAL / (prefix + "-reservation.json"), {
            "schema": "source-place-kick-child-reservation/v1",
            "sequence": number, "category": category, "phase": "D1",
            "actualUtc": utc(), "parentPid": os.getpid(), "outerPid": os.getppid(),
            "argv": argv, "cwd": str(ROOT), "environment": env, "timeoutSeconds": timeout,
            "inputs": inputs, "expectedExit": expected_exit, "expectedPrefix": expected_prefix,
            "stdoutPath": str(stdout_path), "stderrPath": str(stderr_path),
            "consumedBefore": counts,
            "continuation": record(CONTINUATION),
        })
        process = None
        timed_out = False
        started = None
        try:
            with stdout_path.open("xb") as stdout, stderr_path.open("xb") as stderr:
                process = subprocess.Popen(argv, cwd=ROOT, env=env, shell=False,
                                           stdin=subprocess.PIPE, stdout=stdout, stderr=stderr)
                process.stdin.close()
                started = write_json(JOURNAL / (prefix + "-started.json"), {
                    "actualUtc": utc(), "pid": process.pid, "parentPid": os.getpid(),
                    "outerPid": os.getppid(), "argv": argv, "reservation": reservation,
                })
                try:
                    exit_code = process.wait(timeout=timeout)
                except subprocess.TimeoutExpired:
                    timed_out = True
                    process.kill()
                    exit_code = process.wait()
        except BaseException as error:
            if process is not None and process.poll() is None:
                process.kill()
                process.wait()
            write_json(JOURNAL / (prefix + "-launch-error.json"), {
                "actualUtc": utc(), "pid": None if process is None else process.pid,
                "parentPid": os.getpid(), "error": repr(error), "reservation": reservation,
                "consumed": self.check_counts(), "refund": False,
            })
            raise
        completion = {
            "schema": "source-place-kick-child-completion/v1",
            "actualUtc": utc(), "pid": process.pid, "parentPid": os.getpid(),
            "outerPid": os.getppid(), "actualExit": exit_code, "timedOut": timed_out,
            "reservation": reservation, "started": started,
            "stdout": record(stdout_path), "stderr": record(stderr_path),
            "consumed": self.check_counts(), "refund": False,
        }
        receipt = write_json(JOURNAL / (prefix + "-completion.json"), completion)
        require(not timed_out, "Child timeout consumes its reservation: " + label)
        require(exit_code == expected_exit, "Unexpected child exit consumes reservation: " + label)
        return {"receipt": receipt, "completion": completion, "stdout": stdout_path, "stderr": stderr_path}


def prepare():
    values = authorities()
    contract = values["contract.json"]
    implementation = values["implementation-admission.json"]
    execution = values["execution-admission.json"]
    history = prior_history()
    correction = correction_preservation(history)
    correction_record = write_json(PREP / "continuation-preservation.json", correction)
    history_record = write_json(PREP / "prior-history.json", history)
    require(len(implementation["sevenPreciseReleases"]) == 7, "Seven releases required")
    before = execution["sourceClosure"]
    before_path = B / before["beforeCatalogueRelativeToB"]
    catalogue = json.loads(pinned(before_path, before["beforeCatalogueBytes"], before["beforeCatalogueSha256"]),
                           object_pairs_hook=unique_object)
    closure_shape(catalogue)
    accepted = contract["acceptanceReadFirst"]
    acceptance_path = B / "metadata/source-remaining-play-end-stats/parent-scoped-acceptance.json"
    pinned(acceptance_path, accepted["bytes"], accepted["sha256"])
    frozen_path = B / "metadata/source-remaining-play-end-stats/quality-review-preparation/current38-catalogue.json"
    frozen = json.loads(pinned(frozen_path, accepted["frozenCatalogue"]["bytes"],
                              accepted["frozenCatalogue"]["sha256"]), object_pairs_hook=unique_object)
    require(len(frozen["files"]) == len({row["path"] for row in frozen["files"]}) == 38,
            "Current38 catalogue required")
    released = {row["path"]: row for row in implementation["sevenPreciseReleases"]}
    production_released = set(released) - {GUARD}
    require(len(production_released) == 6, "Six production releases required")
    contents = {}
    production = []
    for prior in catalogue:
        relative = prior["relativePath"]
        live = safe_live(relative)
        raw = live.read_bytes()
        if relative in production_released:
            require(prior["bytes"] == released[relative]["beforeBytes"]
                    and prior["sha256"] == released[relative]["beforeSha256"],
                    "Released production ancestor pin differs")
            require(digest(raw) != prior["sha256"], "Released production was not authored")
        else:
            require(len(raw) == prior["bytes"] and digest(raw) == prior["sha256"],
                    "One of71 unreleased production inputs changed")
        captured = write_bytes(PREP / "captured-production" / relative, raw)
        production.append(dict(captured, relativePath=relative, livePath=str(live)))
        contents[relative] = raw
    harnesses = []
    for relative in (HARNESS, FIXTURE):
        live = safe_live(relative)
        raw = live.read_bytes()
        if relative == FIXTURE:
            expected = contract["fixtureProspectiveExtensionRequested"]
            require(len(raw) == expected["bytes"] and digest(raw) == expected["sha256"],
                    "Unchanged fixture pin differs")
        captured = write_bytes(PREP / "captured-harness" / relative, raw)
        harnesses.append(dict(captured, relativePath=relative, livePath=str(live)))
    controllers = []
    for relative in CONTROLLERS:
        live = safe_live(relative)
        captured = write_bytes(PREP / "captured-controller" / relative, live.read_bytes())
        controllers.append(dict(captured, relativePath=relative, livePath=str(live)))
    guard_raw = safe_live(GUARD).read_bytes()
    guard = dict(write_bytes(PREP / "captured-guard" / GUARD, guard_raw), relativePath=GUARD)
    preserved = preservation(contents, guard_raw, contract, implementation)
    preservation_record = write_json(PREP / "preservation.json", preserved)
    inventory = literal_inventory(contract)
    validate_inventory(inventory, contract)
    literal_record = write_json(PREP / "expected-inventory.json", inventory)
    flat_record = write_json(PREP / "expected-flat-fields.json", flattened(inventory))
    mutations = mutation_inventory(contents, contract, inventory)
    mutation_record = write_json(PREP / "mutation-inventory.json", mutations)
    freeze_record = write_json(PREP / "literal-freeze.json", {
        "actualUtc": utc(), "literalInventory": literal_record, "flatFields": flat_record,
        "mutations": mutation_record, "cases": 32, "fields": 603, "controls": 17,
        "compilerOrReferenceBodiesOpenedBeforeThisFreeze": False,
        "preservation": preservation_record,
    })
    tools_path = B / execution["tools"]["toolManifestRelativeToB"]
    refs_path = B / execution["tools"]["referenceManifestRelativeToB"]
    tools = json.loads(pinned(tools_path, execution["tools"]["toolManifestBytes"],
                              execution["tools"]["toolManifestSha256"]), object_pairs_hook=unique_object)
    refs = json.loads(pinned(refs_path, execution["tools"]["referenceManifestBytes"],
                             execution["tools"]["referenceManifestSha256"]), object_pairs_hook=unique_object)
    require(set(tools) == {"python", "dotnet", "compiler"}, "Only three named tools")
    require(tools["python"]["path"] == PYTHON and tools["dotnet"]["path"] == DOTNET
            and tools["compiler"]["path"] == CSC, "Fixed tool paths changed")
    references = {}
    for profile, count in (("net10.0", 167), ("netstandard2.1", 121)):
        rows = refs["profiles"][profile]["files"]
        require(len(rows) == len({row["path"] for row in rows}) == count,
                "Exact reference profile vector changed")
        references[profile] = rows
    verification_manifest = {"tools": tools, "references": references}
    verify_tool_references(verification_manifest, "net10.0")
    verify_tool_references(verification_manifest, "netstandard2.1")
    tool_record = write_json(PREP / "verified-tools-and-references.json", {
        "actualUtc": utc(), "literalFreezeBeforeOpening": freeze_record,
        "tools": tools, "references": references,
    })
    immutable = [literal_record, flat_record, mutation_record, freeze_record, preservation_record, tool_record,
                 record(before_path), record(acceptance_path), record(frozen_path),
                 record(tools_path), record(refs_path), correction_record, history_record,
                 record(VALIDATION / "clock-addendum.json")]
    immutable.extend(history["records"])
    immutable.extend(correction["beforeVersions"])
    for item in contract["exactDependencyReuseProposal"]["publicSourceLists"]:
        live = safe_live(item["path"])
        pinned(live, item["bytes"], item["sha256"])
        immutable.append(record(live))
    frozen_custody = []
    changed_frozen = set(released) & {row["path"] for row in frozen["files"]}
    require(len(changed_frozen) == 5, "Exactly five released current38 paths")
    for row in frozen["files"]:
        if row["path"] not in changed_frozen:
            frozen_custody.append({
                "relativePath": row["path"], "inheritedPin": row,
                "stat": custody_row(safe_live(row["path"])),
                "historicalBodyOpenedForRequalification": False,
            })
    require(len(frozen_custody) == 33, "Exactly33 inherited frozen paths")
    manifest = {
        "schema": "source-place-kick-statistics-admitted-inputs/v1",
        "actualUtc": utc(), "worktree": str(ROOT), "baseB": str(B),
        "authorityPins": AUTHORITY_PINS,
        "production": production, "harnesses": harnesses, "controllers": controllers, "guard": guard,
        "tools": tools, "references": references, "immutableMetadata": immutable,
        "literalFreeze": freeze_record, "frozenCustody": frozen_custody,
        "actualProductionCount": 77, "executableSourceCount": 79,
        "sourceCaptureIncludesOriginalAssets": False,
        "continuation": record(CONTINUATION), "priorHistory": history_record,
        "correction": correction_record, "cumulativeMetadataSeed": 3,
    }
    verify_input_pins(manifest, live=True)
    manifest_record = write_json(PREP / "admitted-inputs.json", manifest)
    production_record = write_json(PREP / "production77-authored.json", production)
    return {
        "status": "prepared", "inputs": manifest_record, "production77": production_record,
        "literalInventory": literal_record, "mutationInventory": mutation_record,
        "preservation": preservation_record, "literalFreeze": freeze_record,
        "outputBudget": output_budget(),
    }


def launch_inputs(manifest, sources, profile, binary=None, runtime_config=None):
    return {
        "admissions": {name: record(AUTH / name) for name in AUTHORITY_PINS},
        "admittedInputs": record(PREP / "admitted-inputs.json"),
        "literalInventory": record(PREP / "expected-inventory.json"),
        "mutationInventory": record(PREP / "mutation-inventory.json"),
        "tools": manifest["tools"], "references": manifest["references"][profile],
        "sources": sources, "binary": binary, "runtimeConfig": runtime_config,
    }


def compile_component(ledger, manifest, label, source_rows, profile="net10.0"):
    verify_input_pins(manifest, live=True)
    verify_tool_references(manifest, profile)
    for row in source_rows:
        pinned(row["path"], row["bytes"], row["sha256"])
    directory = PREP / "assemblies" / label
    directory.mkdir(parents=True, exist_ok=False)
    binary_path = directory / (label + ".dll")
    runtime_record = None
    if profile == "net10.0":
        runtime_record = write_json(directory / (label + ".runtimeconfig.json"), {
            "runtimeOptions": {
                "tfm": "net10.0",
                "framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"},
                "rollForward": "Disable",
            }
        })
    expected_sources = 79 if profile == "net10.0" else 77
    require(len(source_rows) == len({row["path"] for row in source_rows}) == expected_sources,
            "Compiler source vector changed")
    argv = [DOTNET, "exec", CSC, "/noconfig", "/nostdlib+", "/langversion:7.3",
            "/warnaserror+", "/deterministic+", "/nologo",
            "/target:exe" if profile == "net10.0" else "/target:library",
            "/out:" + str(binary_path)]
    if profile == "net10.0":
        argv.append("/main:SourcePlaceKickStatsChecks")
    argv.extend("/reference:" + row["path"] for row in manifest["references"][profile])
    argv.extend(row["path"] for row in source_rows)
    launch = ledger.launch("csc", label + "-csc", argv,
                           launch_inputs(manifest, source_rows, profile, runtime_config=runtime_record), 0)
    binary_record = record(binary_path)
    require(binary_record["bytes"] > 0, "Compiler omitted its actual binary")
    return {
        "label": label, "profile": profile, "compile": launch["receipt"],
        "binary": binary_record, "runtimeConfig": runtime_record, "sources": source_rows,
        "executed": False,
    }


def run_component(ledger, manifest, compiled, inventory, control=None):
    verify_input_pins(manifest, live=True)
    verify_tool_references(manifest, "net10.0")
    pinned(compiled["binary"]["path"], compiled["binary"]["bytes"], compiled["binary"]["sha256"])
    runtime = compiled["runtimeConfig"]
    pinned(runtime["path"], runtime["bytes"], runtime["sha256"])
    literal_path = PREP / "expected-inventory.json"
    argv = [DOTNET, "exec", compiled["binary"]["path"], "--inventory", str(literal_path)]
    expected_prefix = None if control is None else {
        "field": control["failedField"], "fieldsIncludingFailure": control["prefixFieldsIncludingFailure"],
        "completedCases": control["completedCases"],
    }
    launch = ledger.launch("managed", compiled["label"] + "-managed", argv,
                           launch_inputs(manifest, compiled["sources"], "net10.0",
                                         compiled["binary"], runtime),
                           0 if control is None else 2, expected_prefix)
    summary = read_json(launch["stdout"])
    if control is None:
        verify_pass(summary, inventory, launch["completion"]["actualExit"])
    else:
        verify_control(summary, inventory, control, launch["completion"]["actualExit"])
    result = dict(compiled, executed=True, managed=launch["receipt"],
                  observations=record(launch["stdout"]), status=summary["status"],
                  completedCases=summary["completedCases"], fields=summary["fieldCount"])
    if control is not None:
        result.update(controlId=control["id"], failedField=summary["failedField"],
                      semanticKill=True, compileExit=0, assertionExit=2)
    return result


def campaign(ledger):
    authorities()
    require(read_json(UNIT_RESULT)["status"] == "passed", "Explicit additional U1 required before D1")
    require(read_json(JOURNAL / "0005-U1-outer-exit.json")["observedChildExit"] == 0,
            "Actual enclosing U1 exit required")
    manifest = input_manifest()
    inventory = read_json(PREP / "expected-inventory.json")
    controls = read_json(PREP / "mutation-inventory.json")["controls"]
    validate_inventory(inventory, authorities()["contract.json"])
    base_sources = manifest["production"] + manifest["harnesses"]
    clean = run_component(ledger, manifest,
                          compile_component(ledger, manifest, "clean", base_sources), inventory)
    results = []
    for control in controls:
        sources = list(base_sources)
        index = next(index for index, row in enumerate(sources) if row["relativePath"] == control["path"])
        original = sources[index]
        raw = pinned(original["path"], original["bytes"], original["sha256"])
        changed = replace_once(raw.decode(), control["anchor"], control["replacement"]).encode()
        require(digest(raw) == control["capturedSourceSha256"]
                and digest(changed) == control["mutatedSourceSha256"], "Frozen mutation changed")
        mutant = write_bytes(PREP / "mutations" / control["id"] / control["path"], changed)
        sources[index] = dict(mutant, relativePath=control["path"], mutatedFrom=original)
        require(sum(a["sha256"] != b["sha256"] for a, b in zip(base_sources, sources)) == 1,
                "Exactly one captured actual production mutation required")
        compiled = compile_component(ledger, manifest, control["id"], sources)
        results.append(run_component(ledger, manifest, compiled, inventory, control))
    compatibility = compile_component(ledger, manifest, "netstandard21",
                                      manifest["production"], "netstandard2.1")
    final = run_component(ledger, manifest,
                          compile_component(ledger, manifest, "final", base_sources), inventory)
    verify_input_pins(manifest, live=True)
    result = {
        "schema": "source-place-kick-statistics-campaign/v1", "status": "passed",
        "actualUtc": utc(), "clean": clean, "controls": results,
        "compatibilityCompileOnly": compatibility, "final": final,
        "counts": ledger.check_counts(), "outputBudget": output_budget(),
        "wholeGameParityClaimed": False, "oldCampaignsExecuted": 0,
    }
    result_record = write_json(PREP / "campaign-result.json", result)
    return {"status": "passed", "campaign": result_record, "controls": len(results),
            "cleanCases": 32, "cleanFields": 603, "counts": ledger.check_counts()}


def inspect_results(ledger):
    require(read_json(JOURNAL / "0007-D1-outer-exit.json")["observedChildExit"] == 0,
            "Actual enclosing D1 exit required")
    manifest = input_manifest()
    for row in manifest["immutableMetadata"]:
        pinned(row["path"], row["bytes"], row["sha256"])
    inventory = read_json(PREP / "expected-inventory.json")
    controls = read_json(PREP / "mutation-inventory.json")["controls"]
    result = read_json(PREP / "campaign-result.json")
    require(result["status"] == "passed" and len(result["controls"]) == 17,
            "Incomplete campaign result")
    clean = read_json(result["clean"]["observations"]["path"])
    final = read_json(result["final"]["observations"]["path"])
    verify_pass(clean, inventory, 0)
    verify_pass(final, inventory, 0)
    require(clean["fields"] == final["fields"] and clean["completedCaseIds"] == final["completedCaseIds"],
            "Clean/final literal observations differ")
    for actual, expected in zip(result["controls"], controls):
        require(actual["controlId"] == expected["id"] and actual["semanticKill"] is True,
                "Control order/qualification changed")
        verify_control(read_json(actual["observations"]["path"]), inventory, expected, 2)
    unit = read_json(UNIT_RESULT)
    require(unit["status"] == "passed" and unit["testsRun"] == 8
            and len(unit["staticNegativeControls"]) == 6, "Fresh unit evidence incomplete")
    compilers = []
    managed = []
    for reservation in ledger.rows():
        category = reservation["category"]
        if category not in ("csc", "managed"):
            continue
        path = Path(reservation["_path"])
        prefix = path.name.removesuffix("-reservation.json")
        completed = read_json(JOURNAL / (prefix + "-completion.json"))
        require(completed["timedOut"] is False, "Timed out child in successful graph")
        require(completed["actualExit"] == reservation["expectedExit"], "Actual child exit mismatch")
        for key in ("stdout", "stderr"):
            item = completed[key]
            pinned(item["path"], item["bytes"], item["sha256"])
        argv = reservation["argv"]
        require(argv[:2] == [DOTNET, "exec"] and completed["pid"] > 0,
                "Child executable/identity changed")
        sources = reservation["inputs"]["sources"]
        require(len({row["path"] for row in sources}) == len(sources), "Duplicate source input")
        if category == "csc":
            require(argv[2] == CSC and "/warnaserror+" in argv and "/langversion:7.3" in argv
                    and "/deterministic+" in argv and "/noconfig" in argv and "/nostdlib+" in argv,
                    "Compiler mode changed")
            library = "/target:library" in argv
            require(len(sources) == (77 if library else 79), "Compiler closure size changed")
            require(len(reservation["inputs"]["references"]) == (121 if library else 167),
                    "Compiler reference vector changed")
            require(("/main:SourcePlaceKickStatsChecks" in argv) is not library,
                    "Selected Main/library boundary changed")
            compilers.append(record(path))
        else:
            require(len(sources) == 79 and argv[-2] == "--inventory"
                    and argv[-1] == str(PREP / "expected-inventory.json"),
                    "Full ordered harness invocation changed")
            require("--case" not in argv and "--selected-case" not in argv,
                    "Selected-case-only substitute")
            managed.append(record(path))
    require(len(compilers) == 20 and len(managed) == 19, "Exact20/19 graph incomplete")
    compatibility = result["compatibilityCompileOnly"]
    require(compatibility["profile"] == "netstandard2.1" and compatibility["executed"] is False
            and compatibility["runtimeConfig"] is None and len(compatibility["sources"]) == 77,
            "NS2.1 boundary changed")
    counts = ledger.check_counts()
    require(all(counts[name] == value for name, value in
                {"metadata": 5, "unit": 2, "driver": 1, "csc": 20, "managed": 19}.items()),
            "M2 execution accounting mismatch")
    inspected = {
        "schema": "source-place-kick-statistics-result-inspection/v1",
        "status": "passed", "actualUtc": utc(), "campaign": record(PREP / "campaign-result.json"),
        "unitResult": record(UNIT_RESULT),
        "failedUnitResult": record(VALIDATION / "unit-result.json"),
        "controllerInputRevision": record(REVISION_ROOT / "controller-input-revision.json"),
        "cleanCases": 32, "finalCases": 32, "cleanFields": 603, "finalFields": 603,
        "cleanAndFinalLiteralRowsIdentical": True,
        "compiledActualSourceControls": 17, "compilerReservations": compilers,
        "managedReservations": managed, "unitTests": 8, "staticNegatives": 6,
        "ns21ProductionCompileOnly": True, "actualCountsAtM2": counts,
        "reviewRuntimeReplay": False, "oldCampaignsRequalified": False,
        "outputBudget": output_budget(),
    }
    return {"status": "passed", "inspection": write_json(VALIDATION / "result-inspection.json", inspected)}


def finalize(ledger):
    require(read_json(JOURNAL / "0048-M2-outer-exit.json")["observedChildExit"] == 0,
            "Actual enclosing M2 exit required")
    values = authorities()
    contract = values["contract.json"]
    implementation = values["implementation-admission.json"]
    manifest = input_manifest()
    verify_input_pins(manifest, live=True)
    contents = contents_from_manifest(manifest)
    guard_raw = pinned(manifest["guard"]["path"], manifest["guard"]["bytes"],
                       manifest["guard"]["sha256"])
    preserved = preservation(contents, guard_raw, contract, implementation)
    require(preserved == read_json(PREP / "preservation.json"), "Final preservation observations changed")
    result = read_json(PREP / "campaign-result.json")
    require(read_json(VALIDATION / "result-inspection.json")["status"] == "passed",
            "M2 inspection required")
    frozen_path = B / "metadata/source-remaining-play-end-stats/quality-review-preparation/current38-catalogue.json"
    frozen = read_json(frozen_path)["files"]
    authored_names = [row["path"] for row in implementation["sevenPreciseReleases"]]
    authored_names += implementation["newTestsOnly"]
    require(len(authored_names) == len(set(authored_names)) == 11, "Eleven authored descendants required")
    authored = []
    for name in authored_names:
        item = record(safe_live(name))
        authored.append(dict(item, relativePath=name))
    replacements = {row["relativePath"]: row for row in authored}
    catalogue = []
    for inherited in frozen:
        name = inherited["path"]
        if name in replacements:
            item = replacements[name]
            catalogue.append({
                "path": name, "bytes": item["bytes"], "sha256": item["sha256"],
                "authority": "Current admitted authored descendant",
                "unopenedHistoricalBody": False,
            })
        else:
            catalogue.append(dict(inherited, preservation="M1/M3 unchanged filesystem custody; inherited pin"))
    present = {row["path"] for row in catalogue}
    for name in authored_names:
        if name not in present:
            item = replacements[name]
            catalogue.append({
                "path": name, "bytes": item["bytes"], "sha256": item["sha256"],
                "authority": "Newly released producer dependency or new test",
                "unopenedHistoricalBody": False,
            })
    require(len(catalogue) == len({row["path"] for row in catalogue}) == 44,
            "Current44 catalogue required")
    require(len(set(replacements) & {row["path"] for row in frozen}) == 5,
            "Exactly five current38 replacements")
    catalogue_record = write_json(PREP / "current44-catalogue.json", {
        "schema": "source-place-kick-statistics-current44/v1",
        "definition": "current38 with5 replacements plus2 production dependencies plus4 new tests",
        "authoredDescendants": 11, "unchangedInherited": 33,
        "files": catalogue, "prohibitedHistoricalCampaignBodyHashing": False,
    })
    final_preservation = write_json(VALIDATION / "final-preservation.json", {
        "actualUtc": utc(), "preservation": preserved,
        "liveSourceAndControllerPinsUnchangedSinceM1": True,
        "frozenCustody": manifest["frozenCustody"],
        "authorities": {name: record(AUTH / name) for name in AUTHORITY_PINS},
    })
    counts = ledger.check_counts()
    require(all(counts[name] == limit for name, limit in CEILINGS.items()),
            "Final exact graph consumption differs")
    handoff = {
        "schema": "source-place-kick-statistics-source-write-handoff/v1",
        "status": "IMPLEMENTED_FRESH_COMPONENT_EVIDENCE_AWAITING_QUALITY",
        "actualUtc": utc(), "accountableOwner": "Unity", "soleMaterialReviewer": "Quality",
        "worktree": str(ROOT), "baseB": str(B),
        "authorities": {name: record(AUTH / name) for name in AUTHORITY_PINS},
        "authoredEleven": authored, "currentFrozenCatalogue": catalogue_record,
        "preservation": final_preservation, "sevenWholeFileInverses": preserved["wholeFileInverses"],
        "production77": record(PREP / "production77-authored.json"),
        "literalInventory": record(PREP / "expected-inventory.json"),
        "mutationInventory": record(PREP / "mutation-inventory.json"),
        "campaign": record(PREP / "campaign-result.json"),
        "resultInspection": record(VALIDATION / "result-inspection.json"),
        "unitResult": record(UNIT_RESULT),
        "failedUnitResult": record(VALIDATION / "unit-result.json"),
        "controllerInputRevision": record(REVISION_ROOT / "controller-input-revision.json"),
        "priorFailureAndDiagnostic": record(PREP / "prior-history.json"),
        "correctionVersions": record(PREP / "continuation-preservation.json"),
        "clockAddendum": record(VALIDATION / "clock-addendum.json"),
        "qualified": [
            "Two actual captured-source count producers: canonical selected-object attempts then independent FieldGoal-result made goals, both saved initial-side owned.",
            "Actual same-catalog reference identity and saved Input/pending-constructor lifetime through deliberately changed later model/catalog/owner.",
            "Actual immutable grouped four-count DTO and snapshot/detachment, count/null guards, six unchanged legacy constructor defaults and35-name forwarding.",
            "Exact static common/C2 capture placement and four-generation caller restoration; no second callback.",
            "Exact77-production-source NS2.1 compilation only.",
        ],
        "cleanAndFinalCasesEach": 32, "cleanAndFinalLiteralFieldsEach": 603,
        "compiledActualSourceSemanticControls": 17, "pythonUnitTests": 8,
        "staticNegativesWithinUnits": 6, "consumed": counts,
        "ns21ProductionCompileOnly": result["compatibilityCompileOnly"]["executed"] is False,
        "fixture": "Unchanged explicitly synthetic typed helper,14 genuine actors/28 layers/init1each/0RNG/false original provenance; two plain synthetic catalogs, no original data admission.",
        "unqualified": contract["adjacentFindingsAndHonestLimits"]["componentProofWouldNotQualify"],
        "nativeOrdering": contract["proposedArchitecture"]["singleStateOwnedDispatch"]["orderingQualification"],
        "originalReadsClosed": True, "oldCampaignsRequalified": False,
        "finalization": {
            "reservation": record(ledger.reservation_path),
            "completionPath": str(JOURNAL / (ledger.prefix + "-completion.json")),
            "enclosingExitPath": str(JOURNAL / "0050-M3-outer-exit.json"),
            "qualification": "Immutable handoff written before M3 completion/enclosing-shell observed child exit. No future completion hash or exit is asserted here; parent tool observes shell termination separately.",
        },
        "outputBudgetBeforeHandoff": output_budget(),
        "selfApproval": False, "publicationAuthorized": False, "wholeTaskComplete": False,
    }
    handoff_record = write_json(AUTH / "source-write-handoff.json", handoff)
    return {"status": handoff["status"], "handoff": handoff_record,
            "current44": catalogue_record, "counts": counts}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--admission", default=str(AUTH / "execution-admission.json"))
    parser.add_argument("--continuation", required=True)
    parser.add_argument("--controls", action="store_true")
    parser.add_argument("--phase", choices=("prepare", "campaign", "inspect", "finalize"), default="campaign")
    args = parser.parse_args()
    require(Path(args.admission).absolute() == AUTH / "execution-admission.json",
            "Only the pinned exact execution admission")
    require(Path(args.continuation).absolute() == CONTINUATION,
            "Only the pinned explicit continuation authority")
    phases = {"prepare": "M1", "campaign": "D1", "inspect": "M2", "finalize": "M3"}
    ledger = None
    try:
        ledger = Ledger(phases[args.phase])
        if args.phase == "prepare":
            result = prepare()
        elif args.phase == "campaign":
            require(args.controls, "The full17-control graph is mandatory")
            result = campaign(ledger)
        elif args.phase == "inspect":
            result = inspect_results(ledger)
        else:
            result = finalize(ledger)
        completion = ledger.finish("passed", result)
        print(json.dumps(dict(result, pythonCompletion=completion), sort_keys=True))
        return 0
    except BaseException as error:
        failure = {"schema": "source-place-kick-statistics-failure/v1",
                   "actualUtc": utc(), "phase": phases[args.phase], "error": repr(error),
                   "retryAuthorized": False, "technicalFailureIsSemanticKill": False}
        if ledger is not None:
            failure["consumed"] = ledger.check_counts()
            failure["completion"] = ledger.finish("failed", failure.copy())
        print(json.dumps(failure, sort_keys=True))
        return 1


if __name__ == "__main__":
    sys.exit(main())
