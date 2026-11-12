#!/usr/bin/env python3
"""Direct-CSC recipe. Authoring this file does not admit its execution."""

import argparse
import base64
import hashlib
import json
import re
import subprocess
import sys
from datetime import datetime
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / ".squad/.scratch/source-following-active-p1"
PREPARATION = BASE / "human-possession-time-source-preparation"
HANDOFF = BASE / "metadata/human-possession-time-source/source-write-handoff.json"
OUTPUT = ROOT / "tests/contracts/.artifacts/possession-time"
OUTPUT_CREATED = False
COMPONENT = "source-statistics-clock-consumer-time-of-possession/v1"
CALLER = "Assets/Scripts/Gameplay/GameSimulation.Possession.cs"
SNAPSHOT = "Assets/Scripts/Gameplay/GameSnapshot.cs"
MODEL = "Assets/Scripts/Gameplay/SimulationState.cs"
CONTRACTS = "Assets/Scripts/Contracts/SourceMatchContracts.cs"
DISPATCHER = "Assets/Scripts/Gameplay/GameSimulation.Source.cs"
FROZEN_RUNNER = "tests/contracts/run.py"
DISPATCHER_SHA = "6b200f1c377d6a3d7a9435541bcb3ee0a77f54ff61ec8a7fa1a0e35eef1c2884"
FROZEN_RUNNER_SHA = "9737f696354fb9f11676c4eb5e9d9f76b873210f821184c9dab082f7803ce54f"
PRE_ONSIDE_SHA = "726f513c0549d8bf028734c7d7c449dfbff430f8f7c244053b4cf8fdc03b8091"
LEGACY_CALL = (
    "SourceMatchFlow.PrepareKickoff(new SourceKickoffInput(\n"
    "                Model.SourceRules, Model.Quarter - 1, Model.SourceClock.PackedTime, first, half,\n"
    "                Model.SourceFirstPossession, (int)Model.SourceCaptain, onside))"
)
CURRENT_CALL = (
    "SourceMatchFlow.PrepareKickoff(new SourceKickoffInput(\n"
    "                Model.SourceRules, Model.Quarter - 1, Model.SourceClock.PackedTime, first, half,\n"
    "                Model.SourceFirstPossession, (int)Model.SourceCaptain, onside), "
    "sourceConfiguration, Model.SourceCurrentInput.StationInputs)"
)
COMPILED_FIELDS = {
    "credit-omitted": ("minute/Home/HomeSeconds", 13),
    "credit-opposite": ("minute/Home/HomeSeconds", 13),
    "credit-double": ("minute/Home/HomeSeconds", 13),
    "credit-erases-other": ("minute/Home/AwaySeconds", 14),
    "view-swapped": ("minute/Home/HomeSeconds", 13),
    "match-ledger-dropped": ("G15", 547),
    "second-delta-dropped": ("second-normal/Home/delta", 35),
    "minute-delta-dropped": ("minute/Home/delta", 7),
}
STATIC_FIELDS = {
    "caller-credit-removed": "caller.clock.credit-once",
    "caller-owner-inverted": "caller.clock.current-offense",
    "caller-delta-replaced": "caller.clock.exact-delta",
    "caller-credit-duplicated": "caller.clock.credit-once",
    "caller-credit-moved": "caller.clock.order",
    "snapshot-ledger-replaced": "caller.snapshot.current-ledger",
    "model-ledger-nonzero": "caller.model.zero-initialization",
}
CSHARP_TOKEN = re.compile(
    r"(?P<space>\s+)|(?P<comment>//[^\n]*|/\*[\s\S]*?\*/)"
    r'|(?P<literal>\$?@"(?:""|[^"])*"|(?:\$)?"(?:\\[\s\S]|[^"\\])*"'
    r"|'(?:\\[\s\S]|[^'\\])*')"
    r"|(?P<token>[A-Za-z_][A-Za-z_0-9]*|0[xX][0-9A-Fa-f]+|\d+|==|!=|<=|>=|&&|\|\||=>|\S)"
)


class Incomplete(RuntimeError):
    pass


class StaticFailure(RuntimeError):
    def __init__(self, field):
        super().__init__(field)
        self.field = field


def require(condition, message):
    if not condition:
        raise Incomplete(message)


def static_require(condition, field):
    if not condition:
        raise StaticFailure(field)


def digest(data):
    return {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def relative_path(name):
    path = Path(name)
    require(not path.is_absolute() and ".." not in path.parts, "Non-relative custody/source path.")
    return ROOT / path


def read_pin(record, capture=False):
    path = relative_path(record["capture"] if capture else record["path"])
    try:
        raw = path.read_bytes()
    except OSError as error:
        raise Incomplete("Missing declared input: " + str(path)) from error
    require(digest(raw) == {key: record[key] for key in ("bytes", "sha256")},
            "Input identity mismatch: " + str(path))
    return raw


def write(path, raw):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(raw)


def save_json(path, value):
    write(path, (json.dumps(value, indent=2, ensure_ascii=True) + "\n").encode("utf-8"))


def output_path(name):
    path = Path(name)
    require(path.is_absolute() and OUTPUT in path.parents, "Output escaped the admitted absent root.")
    return path


def tokens(text):
    return [match.group() for match in CSHARP_TOKEN.finditer(text) if match.lastgroup == "token"]


def locations(haystack, needle):
    needle = tokens(needle) if isinstance(needle, str) else needle
    return [i for i in range(len(haystack) - len(needle) + 1)
            if haystack[i:i + len(needle)] == needle]


def single(haystack, needle, field):
    found = locations(haystack, needle)
    static_require(len(found) == 1, field)
    return found[0]


def closing(sequence, start, left="(", right=")"):
    depth = 0
    for position in range(start, len(sequence)):
        if sequence[position] == left:
            depth += 1
        elif sequence[position] == right:
            depth -= 1
            if depth == 0:
                return position
    raise StaticFailure("caller.tokens.unclosed-delimiter")


def arguments(sequence, opening):
    end = closing(sequence, opening)
    result, start, depth = [], opening + 1, 0
    for position in range(start, end):
        token = sequence[position]
        if token in ("(", "[", "{"):
            depth += 1
        elif token in (")", "]", "}"):
            depth -= 1
        elif token == "," and depth == 0:
            result.append(sequence[start:position])
            start = position + 1
    result.append(sequence[start:end])
    return result, end


def static_fields(texts):
    source = tokens(texts[CALLER])
    signature = "private void StepSourceClockProcess()"
    start = single(source, signature, "caller.clock.method") + len(tokens(signature))
    static_require(source[start] == "{", "caller.clock.method")
    body = source[start + 1:closing(source, start, "{", "}")]
    entry = tokens("if (!EnterSourceProcess(ClockProcess)) return;")
    step = single(body, "SourceClock.Step(", "caller.clock.step-once")
    credits = locations(body, "SourceTimeOfPossession.Accumulate(")
    assignments = locations(body, "Model.SourceTimeOfPossession =")
    static_require(len(credits) == len(assignments) == 1, "caller.clock.credit-once")
    credit = credits[0]
    opening = credit + len(tokens("SourceTimeOfPossession.Accumulate"))
    args, credit_end = arguments(body, opening)
    static_require(len(args) == 3 and args[0] == tokens("Model.SourceTimeOfPossession"),
                   "caller.clock.previous-ledger")
    static_require(args[1] == tokens("Model.Possession"), "caller.clock.current-offense")
    static_require(args[2] == tokens("clock.TimeOfPossessionDelta"), "caller.clock.exact-delta")
    state = single(body, "Model.SourceClock = clock.State;", "caller.clock.state-once")
    view = single(body, "UpdateSourceClockView();", "caller.clock.view-once")
    complete = single(body, "CompleteSourceProcess();", "caller.clock.complete-once")
    step_open = step + len(tokens("SourceClock.Step"))
    step_end = closing(body, step_open)
    static_require(body[:len(entry)] == entry and len(entry) < step < step_end
                   < assignments[0] < credit < credit_end < state < view < complete
                   and complete + len(tokens("CompleteSourceProcess();")) == len(body),
                   "caller.clock.order")
    snapshot = tokens(texts[SNAPSHOT])
    match = single(snapshot, "Match = new SourceMatchView(", "caller.snapshot.match-once")
    match_args, _ = arguments(snapshot, match + len(tokens("Match = new SourceMatchView")))
    static_require(match_args[0] == tokens("model.SourceTimeOfPossession"),
                   "caller.snapshot.current-ledger")
    branch = single(snapshot, "if (model.SourceMode) {", "caller.snapshot.source-only")
    branch_open = branch + len(tokens("if (model.SourceMode)"))
    static_require(branch_open < match < closing(snapshot, branch_open, "{", "}"),
                   "caller.snapshot.source-only")
    model = tokens(texts[MODEL])
    initializer = ("internal SourceTimeOfPossessionView SourceTimeOfPossession"
                   " = new SourceTimeOfPossessionView(0, 0);")
    single(model, initializer, "caller.model.zero-initialization")
    single(model, "internal SourceClockState SourceClock; " + initializer,
           "caller.model.after-source-clock")
    dto = tokens(texts[CONTRACTS])
    single(dto, "public sealed class SourceTimeOfPossessionView", "api.ledger.sealed")
    dto_start = single(dto, "public sealed class SourceTimeOfPossessionView {", "api.ledger.type")
    dto_open = dto_start + len(tokens("public sealed class SourceTimeOfPossessionView"))
    dto_body = dto[dto_open + 1:closing(dto, dto_open, "{", "}")]
    single(dto_body, "public int HomeSeconds { get; }", "api.ledger.HomeSeconds.get-only")
    single(dto_body, "public int AwaySeconds { get; }", "api.ledger.AwaySeconds.get-only")
    static_require("set" not in dto_body, "api.ledger.immutable")
    single(dto, "public SourceTimeOfPossessionView TimeOfPossession { get; }",
           "api.match.TimeOfPossession.get-only")
    return {"status": "passed-static-only", "compiled": False, "simulation_executed": False}


def mutate(raw, mutation):
    anchor = base64.b64decode(mutation["anchorBase64"], validate=True)
    replacement = base64.b64decode(mutation["replacementBase64"], validate=True)
    require(raw.count(anchor) == 1, "Mutation anchor not unique: " + mutation["id"])
    changed = raw.replace(anchor, replacement, 1)
    require(digest(changed) == mutation["mutatedIdentity"],
            "Mutation whole-file identity mismatch: " + mutation["id"])
    return changed


def load_custody(handoff_sha):
    raw = HANDOFF.read_bytes()
    require(hashlib.sha256(raw).hexdigest() == handoff_sha, "Parent-bound handoff SHA256 mismatch.")
    handoff = json.loads(raw)
    require(handoff["component"] == COMPONENT and handoff["status"] == "AUTHORED_ONLY_ALL_EXECUTION_UNRUN",
            "Wrong component or authoring handoff.")
    documents = {}
    for name, record in handoff["identities"].items():
        contents = read_pin(record)
        if name in ("initialCustody", "executionInputs", "sourceClosure", "referenceInputs",
                    "toolInputs", "futureCli", "mutations", "inverseManifest", "archiveIndex"):
            documents[name] = json.loads(contents)
    for record in documents["archiveIndex"]["files"]:
        read_pin(record)
    inputs = documents["executionInputs"]
    source_bytes = {}
    for record in inputs["files"]:
        live = read_pin(record)
        require(read_pin(record, capture=True) == live, "Captured source differs from actual source.")
        source_bytes[record["path"]] = live
    partitions = inputs["partitions"]
    for name, expected in (("pure14", 14), ("whole70", 70), ("unityApi3", 3),
                           ("retainedRunner4", 4), ("runner19", 19), ("frozen12", 12),
                           ("working78", 78)):
        require(len(partitions[name]) == len(set(partitions[name])) == expected,
                "Wrong exact source partition: " + name)
    helper = "Assets/Scripts/Gameplay/SourceTimeOfPossession.cs"
    require(partitions["whole70"] == documents["initialCustody"]["model69Order"] + [helper],
            "Whole70 must be exactly the released closed69 plus the new helper.")
    require(partitions["pure14"] == documents["initialCustody"]["model69Order"][:10] + [
        "Assets/Scripts/Gameplay/SourceClock.cs", "Assets/Scripts/Gameplay/SourceMatchRules.cs",
        "Assets/Scripts/Gameplay/SourceMatchFlow.cs", helper], "Pure14 source inventory differs.")
    require(partitions["runner19"] == partitions["pure14"] + partitions["retainedRunner4"] + [
        "tests/contracts/SourceTimeOfPossessionChecks.cs"], "Actual shared runner19 inventory differs.")
    require(set(partitions["working78"]) == set(
        partitions["whole70"] + partitions["unityApi3"] + partitions["runner19"]),
        "Working78 contains missing or extra sources.")
    for name, pin in documents["initialCustody"]["initialFilePins"].items():
        if name not in inputs["existingProduction4"]:
            require(digest(source_bytes[name]) == pin, "Frozen actual source/project changed: " + name)
    inverses = documents["inverseManifest"]["files"]
    require(len(inverses) == 4 and {item["source"] for item in inverses}
            == set(inputs["existingProduction4"]), "Need exactly four whole-byte file inverses.")
    for record in inverses:
        inverse = json.loads(read_pin(record["inverse"]))
        name = inverse["source"]
        current = source_bytes[name]
        cut = inverse["slice"]
        remove = base64.b64decode(cut["removeBase64"], validate=True)
        insert = base64.b64decode(cut["insertBase64"], validate=True)
        offset = cut["afterOffset"]
        require(current.count(remove) == 1 and current[offset:offset + len(remove)] == remove,
                "Non-unique or misplaced exact byte inverse: " + name)
        restored = current[:offset] + insert + current[offset + len(remove):]
        before = read_pin(inverse["before"])
        require(restored == before and digest(restored) == inverse["restoredIdentity"],
                "Whole original byte pin not recovered: " + name)
    constructor = inputs["oldConstructor"]
    old_file = read_pin(constructor["before"])
    old_body = old_file[constructor["offset"]:constructor["offset"] + constructor["bytes"]]
    require(hashlib.sha256(old_body).hexdigest() == constructor["sha256"]
            and source_bytes[CONTRACTS].count(old_body) == 1, "Old constructor not byte-exact.")
    tail = inputs["originalParameterTail"]
    original_tail = old_file[tail["beforeOffset"]:tail["beforeOffset"] + tail["bytes"]]
    new_tail = source_bytes[CONTRACTS][tail["newOverloadOffset"]:tail["newOverloadOffset"] + tail["bytes"]]
    require(original_tail == new_tail and hashlib.sha256(original_tail).hexdigest() == tail["sha256"],
            "Ledger-first overload must retain the entire original parameter tail/defaults.")
    require(hashlib.sha256(source_bytes[DISPATCHER]).hexdigest() == DISPATCHER_SHA
            and hashlib.sha256(source_bytes[FROZEN_RUNNER]).hexdigest() == FROZEN_RUNNER_SHA,
            "Frozen whole Source.cs/run.py guard changed.")
    dispatcher = source_bytes[DISPATCHER].decode("utf-8")
    require(dispatcher.count(CURRENT_CALL) == 1
            and hashlib.sha256(dispatcher.replace(CURRENT_CALL, LEGACY_CALL).encode("utf-8")).hexdigest()
            == PRE_ONSIDE_SHA, "Existing complete onside/captain inverse changed.")
    mutations = documents["mutations"]
    require([(m["id"], m["field"]) for m in mutations["compiled"]]
            == [(key, value[0]) for key, value in COMPILED_FIELDS.items()],
            "Eight exact-field compiled controls differ.")
    require([(m["id"], m["field"]) for m in mutations["static"]]
            == list(STATIC_FIELDS.items()), "Seven exact-field STATIC controls differ.")
    for mutation in mutations["compiled"] + mutations["static"]:
        mutate(source_bytes[mutation["source"]], mutation)
    return documents, source_bytes


def verify_external(record):
    path = Path(record["path"])
    try:
        actual = digest(path.read_bytes())
    except OSError as error:
        raise Incomplete("Declared read-only prerequisite unavailable: " + str(path)) from error
    require(actual == {key: record[key] for key in ("bytes", "sha256")},
            "Declared read-only prerequisite identity mismatch: " + str(path))


def prerequisite(action):
    try:
        action()
        return None
    except (Incomplete, OSError) as error:
        return str(error)


def verify_references(profile):
    require(len(profile["files"]) == profile["count"], "Wrong reference metadata count.")
    for record in profile["files"]:
        verify_external(record)


def prepare_sources(paths, destination, source_bytes, mutation=None):
    identities = []
    for name in paths:
        raw = source_bytes[name]
        if mutation and name == mutation["source"]:
            raw = mutate(raw, mutation)
        path = destination / name
        write(path, raw)
        identities.append({"path": name, **digest(raw)})
    return identities


class Execution:
    def __init__(self, plan):
        self.plan = plan
        self.commands = {command["id"]: command for command in plan["commands"]}
        require(len(self.commands) == len(plan["commands"]) == 28, "Expected exactly 28 planned processes.")
        require(sum(c["kind"] == "compiler" for c in self.commands.values()) == 16
                and sum(c["kind"] == "managed" for c in self.commands.values()) == 12,
                "Expected exactly sixteen compiles and twelve managed launches if complete.")
        self.events = []
        self.used = set()

    def command(self, command_id):
        require(command_id not in self.used, "No command retries or unplanned extra launches.")
        command = self.commands[command_id]
        self.used.add(command_id)
        argv = command["argv"]
        dotnet = self.plan["dotnetPath"]
        compiler = self.plan["compilerPath"]
        require(argv[:2] == [dotnet, "exec"], "Only direct fixed dotnet exec commands are permitted.")
        if command["kind"] == "compiler":
            require(argv[2:4] == [compiler, "/noconfig"] and len(argv) == 5
                    and argv[4] == "@" + command["responseFile"]["path"],
                    "Compiler command differs from direct-CSC plan.")
            response = command["responseFile"]
            for source in command["sourceInputs"]:
                require(digest(output_path(source["path"]).read_bytes())
                        == {key: source[key] for key in ("bytes", "sha256")},
                        "Actual whole working-source input changed: " + source["path"])
            write(output_path(response["path"]), ("\n".join(response["lines"]) + "\n").encode("utf-8"))
            output_path(command["emittedAssembly"]).parent.mkdir(parents=True, exist_ok=True)
        else:
            require(len(argv) == 3 and output_path(argv[2]).suffix == ".dll",
                    "Managed launch is not an admitted emitted runner.")
            config = command["runtimeConfig"]
            save_json(output_path(config["path"]), config["value"])
        log = output_path(command["logPrefix"])
        event = {"id": command_id, "kind": command["kind"], "argv": argv,
                 "cwd": str(ROOT), "timeoutSeconds": command["timeoutSeconds"], "status": "UNRUN"}
        try:
            completed = subprocess.run(argv, cwd=ROOT, env=self.plan["environment"],
                                       capture_output=True, timeout=command["timeoutSeconds"], check=False)
            stdout, stderr = completed.stdout, completed.stderr
            event.update(status="exited", exitCode=completed.returncode)
        except subprocess.TimeoutExpired as error:
            stdout, stderr = error.stdout or b"", error.stderr or b""
            event.update(status="UNRUN_INCOMPLETE_TIMEOUT")
            write(Path(str(log) + ".stdout"), stdout)
            write(Path(str(log) + ".stderr"), stderr)
            self.events.append(event)
            raise Incomplete("Timeout is not a semantic rejection: " + command_id) from error
        except OSError as error:
            event.update(status="UNRUN_INCOMPLETE_LAUNCH", error=str(error))
            self.events.append(event)
            raise Incomplete("Launch/load prerequisite incomplete: " + command_id) from error
        write(Path(str(log) + ".stdout"), stdout)
        write(Path(str(log) + ".stderr"), stderr)
        self.events.append(event)
        if command["kind"] == "compiler":
            require(completed.returncode == 0 and output_path(command["emittedAssembly"]).is_file(),
                    "Compile failure is not a mutant kill: " + command_id)
            event["sourceInputs"] = command["sourceInputs"]
            event["emittedAssembly"] = {"path": command["emittedAssembly"],
                                      **digest(output_path(command["emittedAssembly"]).read_bytes())}
            return {"typecheck_only": True, "source_count": len(command["sourceInputs"]),
                    "emittedAssembly": event["emittedAssembly"]}
        try:
            receipt = json.loads(stdout.decode("utf-8"))
        except (ValueError, UnicodeError) as error:
            raise Incomplete("No normal single JSON assertion receipt: " + command_id) from error
        require(isinstance(receipt, dict) and not stderr, "Abnormal runner output: " + command_id)
        return completed.returncode, receipt


def validate_new(observed, mutation=None):
    exit_code, receipt = observed
    require(receipt.get("schema") == "source-possession-time-checks/v1"
            and receipt.get("component") == COMPONENT, "Wrong new-entrypoint receipt.")
    for name, expected in (("actual_data_assertions", 0), ("game_simulation_executed", False),
                           ("full_statistics_claimed", False), ("whole_game_fidelity_claimed", False)):
        require(name in receipt and receipt[name] == expected, "Wrong managed scope: " + name)
    require("exception_type" not in receipt and "error" not in receipt, "Managed error/load failure is not a kill.")
    if mutation:
        field, assertion = COMPILED_FIELDS[mutation["id"]]
        require(exit_code == 1 and receipt.get("status") == "failed"
                and receipt.get("failed_field") == field and receipt.get("assertions") == assertion
                and receipt.get("failed_assertion", "").startswith(field + ": expected "),
                "Wrong field/exit/schema/count is UNRUN/incomplete, never a semantic kill: " + mutation["id"])
    else:
        expected = {"clock_cases": 32, "clock_assertions": 448, "sequence_checkpoints": 14,
                    "sequence_assertions": 84, "guard_assertions": 31, "assertions": 563}
        require(exit_code == 0 and receipt.get("status") == "passed"
                and all(receipt.get(key) == value for key, value in expected.items()),
                "New baseline is not exactly 32x14 + 14x6 + 31 = 563.")
    return receipt


def validate_retained(observed):
    exit_code, receipt = observed
    expected = {"assertions": 1744, "retained_assertions": 511,
                "human_onside_tuple_assertions": 1232, "human_onside_cases": 88,
                "human_onside_api_guard_assertions": 1, "human_onside_assertions": 1233}
    require(exit_code == 0 and receipt.get("schema") == "actual-source-contract-checks/v1"
            and receipt.get("status") == "passed"
            and receipt.get("full_gameplay_parity_claimed") is False
            and all(receipt.get(key) == value for key, value in expected.items()),
            "Unchanged Program is not exactly retained 511 + 1232 + 1 = 1744.")
    return receipt


def partition(action):
    try:
        return {"status": "passed", "evidence": action()}
    except (Incomplete, StaticFailure, OSError, ValueError, KeyError) as error:
        return {"status": "UNRUN_INCOMPLETE", "reason": str(error)}


def unavailable(reason):
    return {"status": "UNRUN_INCOMPLETE", "reason": reason}


def static_partition(documents, source_bytes):
    texts = {name: source_bytes[name].decode("utf-8") for name in (CALLER, SNAPSHOT, MODEL, CONTRACTS)}
    baseline = static_fields(texts)
    controls = []
    for mutation in documents["mutations"]["static"]:
        changed = dict(texts)
        changed[mutation["source"]] = mutate(source_bytes[mutation["source"]], mutation).decode("utf-8")
        destination = OUTPUT / "static-controls" / mutation["id"] / "sources"
        prepare_sources([CALLER, SNAPSHOT, MODEL, CONTRACTS], destination, source_bytes, mutation)
        try:
            static_fields(changed)
        except StaticFailure as failure:
            require(failure.field == mutation["field"], "STATIC control failed at wrong field: " + mutation["id"])
        else:
            raise Incomplete("STATIC control was not rejected: " + mutation["id"])
        controls.append({"id": mutation["id"], "status": "rejected-at-exact-static-field",
                         "field": mutation["field"], "compiled": False, "simulation_executed": False})
    save_json(OUTPUT / "static-controls/receipt.json", {"baseline": baseline, "controls": controls})
    return {"baseline": baseline, "controls": controls, "static_controls": 7,
            "compiled_game_simulation_mutants": 0, "simulation_executed": False}


def archive_baseline(stage, documents, source_bytes):
    plan = documents["futureCli"]
    destination = OUTPUT / "baselines" / stage
    for item in plan["baselineArchive"]:
        source = output_path(item["workingPath"])
        write(destination / item["archivePath"], source.read_bytes())
    identities = []
    for name in documents["executionInputs"]["partitions"]["runner19"]:
        working = OUTPUT / "work/sources" / name
        raw = working.read_bytes()
        require(raw == source_bytes[name], "Actual clean baseline working source changed: " + name)
        write(destination / "sources" / name, raw)
        identities.append({"path": name, "workingPath": str(working), **digest(raw)})
    save_json(destination / "sources.json", identities)
    verify_references(documents["referenceInputs"]["profiles"]["net10.0"])
    for record in documents["toolInputs"].values():
        verify_external(record)
    save_json(destination / "references.json", documents["referenceInputs"]["profiles"]["net10.0"])
    save_json(destination / "tools.json", documents["toolInputs"])
    index = []
    for path in sorted(destination.rglob("*")):
        if path.is_file():
            index.append({"path": str(path.relative_to(destination)), **digest(path.read_bytes())})
    save_json(OUTPUT / "baselines" / (stage + "-index.json"), index)
    return index


def baseline_pair(stage, execution, documents, source_bytes):
    receipts = {}
    for runner, validator in (("new", validate_new), ("retained", validate_retained)):
        def run(runner=runner, validator=validator):
            execution.command(stage + "-" + runner + "-compile")
            return validator(execution.command(stage + "-" + runner + "-run"))
        receipts[runner] = partition(run)
    if not all(value["status"] == "passed" for value in receipts.values()):
        return {"status": "UNRUN_INCOMPLETE", "runners": receipts}
    index = archive_baseline(stage, documents, source_bytes)
    return {"status": "passed", "runners": receipts, "sum_assertions": 2307, "archiveIndex": index}


def execute(admission_id, handoff_sha):
    global OUTPUT_CREATED
    documents, source_bytes = load_custody(handoff_sha)
    require(not OUTPUT.exists(), "Future output root must be absent; never overwrite a prior attempt.")
    plan = documents["futureCli"]
    execution = Execution(plan)
    tool_records = documents["toolInputs"]
    require(Path(sys.executable).resolve() == Path(tool_records["python"]["path"]).resolve(),
            "Wrong parent-declared Python executable.")
    verify_external(tool_records["python"])
    tool_problem = prerequisite(lambda: [verify_external(tool_records[name]) for name in ("dotnet", "compiler")])
    references = documents["referenceInputs"]
    ns_problem = prerequisite(lambda: verify_references(references["profiles"]["netstandard2.1"]))
    net_problem = prerequisite(lambda: verify_references(references["profiles"]["net10.0"]))
    unity_records = [{"path": str(Path(references["unity"]["directory"]) / item["name"]),
                      "bytes": item["bytes"], "sha256": item["sha256"]}
                     for item in references["unity"]["files"]]
    unity_problem = prerequisite(lambda: [verify_external(item) for item in unity_records])
    require(references["profiles"]["netstandard2.1"]["count"] == 121
            and references["profiles"]["net10.0"]["count"] == 167 and len(unity_records) == 3,
            "Wrong declared reference partitions.")
    OUTPUT.mkdir(parents=True)
    OUTPUT_CREATED = True
    for path in plan["processDirectories"]:
        output_path(path).mkdir(parents=True, exist_ok=True)
    inputs = documents["executionInputs"]
    working_sources = OUTPUT / "work/sources"
    prepare_sources(inputs["partitions"]["working78"], working_sources, source_bytes)
    result = {
        "schema": "source-possession-time-execution/v1", "component": COMPONENT,
        "status": "UNRUN_INCOMPLETE", "admission_id": admission_id, "parent_bound_handoff_sha256": handoff_sha,
        "started_at": datetime.now().astimezone().isoformat(),
        "actual_data_assertions": 0, "game_simulation_constructor_or_step_executed": False,
        "unity_engine_or_input_executed": False, "native_executed": False,
        "full_statistics_claimed": False, "whole_game_fidelity_approved": False,
        "publication_authorized": False, "quality_gate": "PENDING_SEPARATE_ONE_QUALITY_GATE",
        "partitions": {},
    }
    partitions = result["partitions"]
    partitions["static"] = partition(lambda: static_partition(documents, source_bytes))
    ns_reason = tool_problem or ns_problem
    net_reason = tool_problem or net_problem
    for key, command_id in (("pure14_ns21", "pure-ns21"), ("whole70_ns21", "whole-ns21")):
        partitions[key] = unavailable(ns_reason) if ns_reason else partition(
            lambda command_id=command_id: execution.command(command_id))
    for key, command_id in (("unity_editor_api3", "unity-editor"), ("unity_webgl_api3", "unity-webgl")):
        reason = ns_reason or unity_problem
        if partitions["whole70_ns21"]["status"] != "passed":
            reason = reason or "Whole70 typecheck did not emit its required model reference."
        partitions[key] = unavailable(reason) if reason else partition(
            lambda command_id=command_id: execution.command(command_id))
    if net_reason:
        partitions["initial"] = unavailable(net_reason)
    else:
        partitions["initial"] = partition(
            lambda: baseline_pair("initial", execution, documents, source_bytes))
        if partitions["initial"]["status"] == "passed":
            partitions["initial"] = partitions["initial"]["evidence"]
    mutation_results = []
    initial_passed = partitions["initial"]["status"] == "passed"
    for mutation in documents["mutations"]["compiled"]:
        if net_reason or not initial_passed:
            mutation_results.append({"id": mutation["id"], **unavailable(
                net_reason or "Both clean initial runners are required before any compiled mutant.")})
            continue
        def run_mutant(mutation=mutation):
            destination = OUTPUT / "mutants" / mutation["id"] / "sources"
            identities = prepare_sources(inputs["partitions"]["runner19"], destination, source_bytes, mutation)
            save_json(destination.parent / "sources.json", identities)
            prefix = "mutant-" + mutation["id"]
            execution.command(prefix + "-compile")
            receipt = validate_new(execution.command(prefix + "-run"), mutation)
            return {"field": mutation["field"], "receipt": receipt,
                    "proof": "compiled-actual-whole-source-normal-assertion-exit"}
        mutation_results.append({"id": mutation["id"], **partition(run_mutant)})
    partitions["compiled_mutants"] = {
        "status": "passed" if all(item["status"] == "passed" for item in mutation_results) else "UNRUN_INCOMPLETE",
        "controls": mutation_results,
        "semantic_kills": sum(item["status"] == "passed" for item in mutation_results),
    }
    if net_reason:
        partitions["final"] = unavailable(net_reason)
    else:
        partitions["final"] = partition(lambda: baseline_pair("final", execution, documents, source_bytes))
        if partitions["final"]["status"] == "passed":
            partitions["final"] = partitions["final"]["evidence"]
    def final_preservation():
        load_custody(handoff_sha)
        require(partitions["initial"]["status"] == partitions["final"]["status"] == "passed",
                "Both initial/final runner pairs must be clean.")
        require(partitions["initial"]["archiveIndex"] == partitions["final"]["archiveIndex"],
                "Same-path clean assemblies/config/stdout/sources/references are not hash-identical.")
        static_fields({name: source_bytes[name].decode("utf-8") for name in (CALLER, SNAPSHOT, MODEL, CONTRACTS)})
        if not ns_reason:
            verify_references(references["profiles"]["netstandard2.1"])
        if not unity_problem:
            for record in unity_records:
                verify_external(record)
        return {"four_byte_inverses": 4, "frozen_component_pins": 12, "same_path_archives_hash_identical": True}
    partitions["final_preservation"] = partition(final_preservation)
    result["processes"] = execution.events
    result["compiler_commands_completed"] = sum(
        item["kind"] == "compiler" and item["status"] == "exited" for item in execution.events)
    result["managed_launches_completed"] = sum(
        item["kind"] == "managed" and item["status"] == "exited" for item in execution.events)
    if all(item["status"] == "passed" for item in partitions.values()):
        require(result["compiler_commands_completed"] == 16 and result["managed_launches_completed"] == 12,
                "A complete attempt requires exactly sixteen compiles and twelve managed launches.")
        result["status"] = "passed-bounded-managed-static-and-typecheck-only"
    result["finished_at"] = datetime.now().astimezone().isoformat()
    save_json(OUTPUT / "receipt.json", result)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute-after-parent-admission", action="store_true")
    parser.add_argument("--admission-id", required=True, help="Public non-secret identifier; not permission by itself.")
    parser.add_argument("--handoff-sha256", required=True, help="Exact SHA256 separately bound by parent admission.")
    args = parser.parse_args()
    if not args.execute_after_parent_admission:
        parser.error("Separate parent admission is required; this switch does not grant it.")
    if not re.fullmatch(r"[A-Za-z0-9_.:/-]{1,160}", args.admission_id):
        parser.error("Use only a public non-secret admission identifier.")
    if not re.fullmatch(r"[0-9a-f]{64}", args.handoff_sha256):
        parser.error("The parent must bind the exact authored handoff SHA256.")
    try:
        result = execute(args.admission_id, args.handoff_sha256)
    except (Incomplete, OSError, ValueError, KeyError, StaticFailure) as error:
        result = {"schema": "source-possession-time-execution/v1", "component": COMPONENT,
                  "status": "UNRUN_INCOMPLETE", "error": str(error),
                  "semantic_rejection_claimed": False, "game_simulation_executed": False,
                  "whole_game_fidelity_approved": False, "publication_authorized": False}
        if OUTPUT_CREATED:
            save_json(OUTPUT / "incomplete.json", result)
    print(json.dumps(result, ensure_ascii=True))
    return 0 if result["status"] == "passed-bounded-managed-static-and-typecheck-only" else 1


if __name__ == "__main__":
    sys.exit(main())
