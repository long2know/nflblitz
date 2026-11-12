#!/usr/bin/python3.12
"""Fresh, admitted production-component campaign; never an authentic runtime."""
import argparse
import datetime
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / ".squad/.scratch/source-following-active-p1"
META = BASE / "metadata/source-completed-pass-stats"
PREP = META / "preparation"
OUT = META / "validation"
SUITE = ROOT / "tests/source-completed-pass-stats"
PYTHON = "/usr/bin/python3.12"
ADMISSION = META / "execution-admission.json"
STATE = "Assets/Scripts/Gameplay/SimulationState.cs"
CONTRACTS = "Assets/Scripts/Contracts/SourceMatchContracts.cs"
SNAPSHOT = "Assets/Scripts/Gameplay/GameSnapshot.cs"
CALLER = "Assets/Scripts/Gameplay/GameSimulation.Source.cs"
GUARD = "tests/contracts/run.py"
OLD_PROGRAM = "tests/source-offensive-stats/Program.cs"
NEW_PROGRAM = "tests/source-completed-pass-stats/Program.cs"
HARNESS_SOURCES = (NEW_PROGRAM, OLD_PROGRAM)
HARNESS_FILES = tuple("tests/source-completed-pass-stats/" + x for x in
                      ("Program.cs", "run.py", "test_runner.py", "README.md"))
CONTRACT_PIN = (65016, "8be915c12dad96812d7aa65f41c5e16fabb262c7acc2df59da6b134ec17194fa")
OLD_REVERSE = "726f513c0549d8bf028734c7d7c449dfbff430f8f7c244053b4cf8fdc03b8091"
CALL_BEFORE = ("            ApplySourceFirePlayEnd(endingInput, resolution);\n"
               "            Model.SourceLastResult = resolution.Classification.Result;\n")
CALL_AFTER = CALL_BEFORE.replace("            Model.SourceLastResult",
    "            Model.AccumulateSourcePassCompletion(endingInput, resolution, Model.CarrierId);\n"
    "            Model.SourceLastResult")
ARRAYS = (
    "        internal readonly int[] SourcePassingYards = new int[2];\n"
    "        internal readonly int[] SourceTotalYards = new int[2];\n"
    "        internal readonly int[] SourcePassTouchdowns = new int[2];\n"
)
YARD_UPDATES = (
    "            int gain = checked(resolution.FieldEnd - input.State.LineOfScrimmage);\n"
    "            SourcePassingYards[side] = checked(SourcePassingYards[side] + gain);\n"
    "            SourceTotalYards[side] = checked(SourceTotalYards[side] + gain);\n"
)
TD_UPDATE = ("            if (result == SourcePlayResult.Touchdown)\n"
             "                SourcePassTouchdowns[side] = checked(SourcePassTouchdowns[side] + 1);\n")
OLD_SNAPSHOT = "                Match = new SourceMatchView(new SourcePassCompletionsView("
NEW_SNAPSHOT = (
    "                Match = new SourceMatchView(new SourceCompletedPassStatsView(\n"
    "                    model.SourcePassingYards[(int)TeamId.Home],\n"
    "                    model.SourcePassingYards[(int)TeamId.Away],\n"
    "                    model.SourceTotalYards[(int)TeamId.Home],\n"
    "                    model.SourceTotalYards[(int)TeamId.Away],\n"
    "                    model.SourcePassTouchdowns[(int)TeamId.Home],\n"
    "                    model.SourcePassTouchdowns[(int)TeamId.Away]),\n"
    "                    new SourcePassCompletionsView("
)
TUPLE_NAMES = ("passing-yards", "total-yards", "completions", "pass-touchdowns")
TUPLE_UNITS = ("signed-source-field-yards", "signed-source-field-yards", "completions", "pass-touchdowns")
NEXT_LOS = (50,50,50,50,90,90,90,50,1,99,30,50,50,50,50,50,90,50,50,30,50,50,50,50,90,20)
LIFECYCLE = (
    ("fresh.state", (0,0,0,0,0,0,0,0)),
    ("snapshot.s0", (0,0,0,0,0,0,0,0)),
    ("first.state", (20,20,1,0,0,0,0,0)),
    ("snapshot.s1", (20,20,1,0,0,0,0,0)),
    ("second.state", (10,10,2,0,0,0,0,0)),
    ("retained.first.s1", (20,20,1,0,0,0,0,0)),
    ("third.state", (10,10,3,0,0,0,0,0)),
    ("fourth.state", (10,10,3,0,70,70,1,1)),
    ("snapshot.s2", (10,10,3,0,70,70,1,1)),
    ("fifth.state", (20,20,4,0,70,70,1,1)),
    ("sixth.state", (-9,-9,5,0,70,70,1,1)),
    ("snapshot.final", (-9,-9,5,0,70,70,1,1)),
    ("retained.final.s0", (0,0,0,0,0,0,0,0)),
    ("retained.final.s1", (20,20,1,0,0,0,0,0)),
    ("retained.final.s2", (10,10,3,0,70,70,1,1)),
    ("phase.select", (-9,-9,5,0,70,70,1,1)),
    ("phase.dead", (-9,-9,5,0,70,70,1,1)),
    ("guarded.state", (-9,-9,5,0,70,70,1,1)),
    ("reset.fresh", (0,0,0,0,0,0,0,0)),
    ("reset.old", (-9,-9,5,0,70,70,1,1)),
    ("reset.retained.s1", (20,20,1,0,0,0,0,0)),
)
API_FIELDS = (
    ("dto.home.passing",-7), ("dto.away.passing",9), ("dto.home.total",-12),
    ("dto.away.total",6), ("dto.home.td",1), ("dto.away.td",2),
    ("dto.zero.home",0), ("dto.zero.away",0),
    ("dto.negative-home-rejected",1), ("dto.negative-away-rejected",1),
    ("new.group-retained",1), ("new.completions-retained",1),
    ("new.interceptions-retained",1), ("new.time-retained",1),
    ("forward.configuration",1), ("forward.kind",0), ("forward.phase",5),
    ("forward.result",1), ("forward.points",6), ("forward.first-down",1),
    ("forward.turnover",1), ("forward.period",0), ("forward.first-possession",1),
    ("forward.captain",1), ("forward.scores.home",3), ("forward.scores.away",6),
    ("forward.stations",0), ("forward.home-play",1), ("forward.away-play",1),
    ("forward.countdown",19), ("forward.pat-choice",2), ("forward.pat-status",4),
    ("forward.kickoff",1), ("forward.kicking",0), ("forward.receiving",1),
    ("forward.flipped",1), ("forward.home-gameplay",2), ("forward.away-gameplay",3),
    ("forward.catcher",4), ("forward.drones-lose",1), ("forward.drones-winning",1),
    ("forward.bonus",3), ("forward.compatibility",1), ("forward.repairs",3),
    ("forward.repair-list",0), ("forward.timer",1), ("forward.home-random",12),
    ("forward.away-random",13), ("forward.audibles",1),
    ("new.null-group-rejected",1), ("new.inherited-null-rejected",1),
)


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def alarm_timeout(signum, frame):
    raise TimeoutError("Admitted outer command timeout")


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def data(path):
    path = Path(path)
    require(path.is_file() and not path.is_symlink(), "Explicit regular input required: " + str(path))
    return path.read_bytes()


def identity(path):
    path = Path(path)
    raw = data(path)
    return {"path": str(path), "bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}


def pin(path, size, sha):
    item = identity(path)
    require((item["bytes"], item["sha256"]) == (size, sha), "Pin mismatch: " + str(path))
    return item


def read_json(path):
    return json.loads(data(path))


def exclusive(path, raw):
    path = Path(path)
    require(path.is_relative_to(META), "Output outside admitted root")
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("xb") as stream:
        stream.write(raw)


def record(path, value):
    exclusive(path, (json.dumps(value, indent=2, sort_keys=True) + "\n").encode())
    return identity(path)


def authorities():
    pin(META / "contract.json", *CONTRACT_PIN)
    implementation = read_json(META / "implementation-admission.json")
    execution = read_json(ADMISSION)
    require(implementation["schema"] == "source-completed-forward-pass-statistics-implementation-admission/v1"
            and execution["schema"] == "source-completed-forward-pass-statistics-execution-admission/v1",
            "Exact new admission schemas required")
    for admission in (implementation, execution):
        require(admission["approved"] is True and admission["accountableOwner"] == "Unity"
                and admission["requester"] == "Stephen Long (@stlong_microsoft)"
                and admission["worktree"] == str(ROOT) and admission["baseB"] == str(BASE)
                and admission["publicationAuthorized"] is False, "Admission ownership/root mismatch")
    require((execution["contractBytes"], execution["contractSha256"]) == CONTRACT_PIN,
            "Execution contract binding")
    require((implementation["contract"]["bytes"], implementation["contract"]["sha256"]) == CONTRACT_PIN,
            "Implementation contract binding")
    require([r["path"] for r in implementation["frozenFileReleases"]] == [STATE, CONTRACTS, SNAPSHOT, OLD_PROGRAM],
            "Exact four frozen releases")
    require(execution["sourceClosure"]["harnessSources"] == list(HARNESS_SOURCES), "Exact two harness paths")
    return {name: identity(META / name) for name in
            ("contract.json", "implementation-admission.json", "execution-admission.json")}


def source_paths():
    execution = read_json(ADMISSION)
    for row in execution["sourceClosure"]["publicSourceLists"]:
        pin(ROOT / row["path"], row["bytes"], row["sha256"])
    project = ROOT / execution["sourceClosure"]["publicSourceLists"][0]["path"]
    includes = [x.attrib["Include"] for x in ET.fromstring(data(project)).iter("Compile")]
    require(set(includes) == {"$(BlitzSourceRoot)Assets/Scripts/Contracts/**/*.cs",
        "$(BlitzSourceRoot)Assets/Scripts/Gameplay/**/*.cs",
        "$(BlitzSourceRoot)Assets/Scripts/AssetImport/*.cs"}, "Public source list changed")
    result = []
    for folder, pattern, count in (("Contracts","**/*.cs",10), ("Gameplay","**/*.cs",38), ("AssetImport","*.cs",27)):
        paths = sorted((ROOT / "Assets/Scripts" / folder).glob(pattern))
        require(len(paths) == count, "Actual source count: " + folder)
        result.extend(paths)
    props = ROOT / execution["sourceClosure"]["publicSourceLists"][1]["path"]
    extra = [x.attrib["Include"].replace("$(BlitzSourceRoot)", "") for x in ET.fromstring(data(props)).iter("Compile")]
    require(extra == ["Assets/Scripts/Presentation/SourceAudioFunctions.cs",
                      "Assets/Scripts/Presentation/SourceC2SoundFunctions.cs"], "Pure selector list changed")
    result.extend(ROOT / x for x in extra)
    require(len(result) == len(set(result)) == 77, "Actual77 distinct sources")
    return sorted(result)


def load_guard():
    spec = importlib.util.spec_from_file_location("completed_pass_static_guard", ROOT / GUARD)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def stat_only(path):
    path = Path(path)
    require(not path.is_symlink(), "Frozen path symlink")
    s = path.stat(follow_symlinks=False)
    return {"device": s.st_dev, "inode": s.st_ino, "bytes": s.st_size,
            "mtimeNs": s.st_mtime_ns, "ctimeNs": s.st_ctime_ns}


def block(text, prefix):
    require(text.count(prefix) == 1, "Unique block: " + prefix)
    start = text.index(prefix)
    at = text.index("{", start)
    depth, end = 1, at + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


def tool_inputs():
    selected = read_json(ADMISSION)["tools"]
    tool_file = BASE / selected["toolManifestRelativeToB"]
    ref_file = BASE / selected["referenceManifestRelativeToB"]
    pin(tool_file, selected["toolManifestBytes"], selected["toolManifestSha256"])
    pin(ref_file, selected["referenceManifestBytes"], selected["referenceManifestSha256"])
    tools, refs = read_json(tool_file), read_json(ref_file)
    for name in ("python", "dotnet", "compiler"):
        row = tools[name]
        require(row["path"] == selected[name], "Exact admitted tool path")
        pin(row["path"], row["bytes"], row["sha256"])
    for profile, count in (("net10.0",167), ("netstandard2.1",121)):
        rows = refs["profiles"][profile]["files"]
        require(refs["profiles"][profile]["count"] == len(rows) == count
                and len({r["path"] for r in rows}) == count, "Explicit reference inventory")
        for row in rows:
            pin(row["path"], row["bytes"], row["sha256"])
    return tools, refs


def expected_inventory():
    contract = read_json(META / "contract.json")
    cases, fields = [], []

    def add(case, suffix, expected, unit="boolean-or-source-enum-int"):
        fields.append({"case": case, "field": case + "." + suffix, "expected": expected, "unit": unit})

    def tuples(case, prefix, values):
        for side, team in enumerate(("home", "away")):
            for i, name in enumerate(TUPLE_NAMES):
                add(case, prefix + "." + team + "." + name, values[side * 4 + i], TUPLE_UNITS[i])

    result_ids = {"Down":0, "Touchdown":1, "Safety":2, "NewPossession":4, "Incomplete":5,
                  "FumbledOutOfBounds":6, "FailedConversion":10, "Conversion":11, "Touchback":15}
    x_literals = {
        "pass.lower-bound":(-468,468,-468,468), "pass.upper-bound":(468,-468,468,-468),
        "pass.safety":(-468,468,-468,468), "pass.fumbled-oob-keeps-fpend":(0,0,-78,78),
        "guard.classifier-side-flip":(468,-468,468,-468),
    }
    flips = {"pass.fourth-down", "guard.derived-possession", "guard.classifier-side-flip"}
    for index, row in enumerate(contract["componentAcceptance"]["literalRows"]):
        for side, team in enumerate(("home", "away")):
            case = row["id"] + "." + team
            cases.append(case)
            tuples(case, "state", sum(row["homeCaseExpected" if side == 0 else "awayCaseExpected"], []))
            owner = 1 - side if row.get("classificationOwner") == "1-s" else side
            values = (
                ("classification.owner",owner), ("classification.result",result_ids[row["result"]]),
                ("classification.status",1 if row.get("classificationStatus") == "IntentionalNoOp" else 0),
                ("resolved.possession-change",int(row.get("resolvedPossessionChange",False))),
                ("raw-end",row.get("rawEnd",50)), ("field-end",row["fieldEnd"]),
                ("next-owner",1-side if row["id"] in flips else side), ("next-los",NEXT_LOS[index]),
                ("rounded-x",x_literals.get(row["id"],(0,0,0,0))[side]),
                ("classified-x",x_literals.get(row["id"],(0,0,0,0))[side+2]),
                ("preserve.offensive.home",8), ("preserve.offensive.away",13),
                ("preserve.consecutive.home",1), ("preserve.consecutive.away",4),
                ("preserve.interceptions.home",7), ("preserve.interceptions.away",9),
                ("preserve.time.home",11), ("preserve.time.away",17),
                ("preserve.fire.home",1), ("preserve.fire.away",0), ("preserve.forward-catcher",4),
            )
            for suffix, value in values:
                add(case, suffix, value)
    case = "pass.lifecycle"
    cases.append(case)
    for prefix, values in LIFECYCLE:
        tuples(case, prefix, values)
    for suffix in ("passing-distinct","total-distinct","completions-distinct","td-distinct"):
        add(case, "reset.arrays." + suffix, 1)
    case = "pass.api"
    cases.append(case)
    for suffix, value in API_FIELDS[:10]:
        add(case, suffix, value)
    for i in range(4):
        for suffix in ("group-null", "time", "interceptions", "completions"):
            add(case, "old" + str(i) + "." + suffix, 1)
    for suffix, value in API_FIELDS[10:]:
        add(case, suffix, value)
    case = "pass.legacy-neutral"
    cases.append(case)
    add(case, "match-null", 1)
    tuples(case, "state", (0,0,0,0,0,0,0,0))
    require(len(cases) == len(set(cases)) == 55 and len({r["field"] for r in fields}) == len(fields),
            "Unique complete literal inventory")
    return {"schema":"completed-pass-literal-inventory/v1", "cases":cases, "fields":fields,
            "caseCount":len(cases), "fieldCount":len(fields), "controls":contract["semanticControls"]["controls"],
            "oracle":"Independent frozen contract literals; no production/helper-generated goldens.",
            "scope":"Synthetic production components; real GameSimulation/C2/Step/Restart UNRUN."}


def counts():
    result = {"metadata":0, "units":0, "drivers":0, "csc":0, "managed":0, "ns21":0, "corrections":0}
    for path in sorted((PREP / "ledger").glob("*.json")):
        row = read_json(path)
        result[row["kind"]] += 1
        if row.get("ns21"):
            result["ns21"] += 1
    return result


def check_window():
    if (PREP / "phase-start.json").exists():
        start = datetime.datetime.fromisoformat(read_json(PREP / "phase-start.json")["atUtc"])
        elapsed = (datetime.datetime.now(datetime.timezone.utc) - start).total_seconds()
        require(0 <= elapsed <= 1800, "Fresh1800-second phase ceiling: " + str(elapsed))
    total = sum(p.stat().st_size for p in META.rglob("*") if p.is_file())
    require(total <= 536870912, "Fresh512MiB ceiling")
    return total


def reserve(kind, argv, cwd, environment, timeout, expectation, inputs, ns21=False):
    authorities()
    check_window()
    if not (PREP / "phase-start.json").exists():
        record(PREP / "phase-start.json", {"atUtc":now(), "pid":os.getpid(),
            "meaning":"Actual first reservation; no inherited window or fabricated timestamp."})
    consumed = counts()
    consumed[kind] += 1
    if ns21:
        consumed["ns21"] += 1
    limits = {"metadata":8,"units":2,"drivers":2,"csc":25,"managed":24,"ns21":1,"corrections":2}
    require(all(consumed[k] <= limits[k] for k in limits), "Fresh launch ceiling")
    serial = len(list((PREP / "ledger").glob("*.json"))) + 1
    path = PREP / "ledger" / (str(serial).zfill(4) + "-" + kind + ".json")
    return record(path, {"atUtc":now(), "kind":kind, "ns21":ns21,
        "reservedBeforeTargetLaunch":True, "reservingPid":os.getpid(), "parentPid":os.getppid(),
        "outerShellIdentity":os.environ.get("BLITZ_OUTER_SHELL_ID"),
        "argv":argv, "cwd":str(cwd), "environment":environment, "timeoutSeconds":timeout,
        "expected":expectation, "inputs":inputs, "authority":authorities(),
        "consumed":consumed, "remaining":{k:limits[k]-consumed[k] for k in limits}})


def entry(kind, argv):
    timeout = 180 if kind == "units" else 1200 if kind == "drivers" else 180
    reservation = reserve(kind, argv, os.getcwd(), dict(os.environ), timeout,
        {"exit":0,"kind":kind}, [identity(Path(__file__)), identity(PYTHON)])
    tag = str(len(list((PREP / "ledger").glob("*.json")))).zfill(4)
    started = {"atUtc":now(),"pid":os.getpid(),"parentPid":os.getppid(),
               "outerShellIdentity":os.environ.get("BLITZ_OUTER_SHELL_ID"),
               "argv":argv,"cwd":os.getcwd(),"environment":dict(os.environ),"reservation":reservation}
    record(PREP / ("outer-" + tag + "-entry.json"), started)
    return tag, started


def finish_entry(tag, started, exit_code, summary):
    return record(PREP / ("outer-" + tag + "-result.json"),
        {"startedAtUtc":started["atUtc"],"completedAtUtc":now(),"pid":os.getpid(),
         "parentPid":os.getppid(),"outerShellIdentity":started["outerShellIdentity"],
         "exit":exit_code,"consumed":counts(),"summary":summary})


def prepare_before():
    authority = authorities()
    contract = read_json(META / "contract.json")
    acceptance = contract["acceptedGeneration"]["firstRead"]
    parent = read_json(BASE / acceptance["relativeToB"])
    pin(BASE / acceptance["relativeToB"], acceptance["bytes"], acceptance["sha256"])
    handoff_pin = contract["acceptedGeneration"]["handoff"]
    handoff = read_json(BASE / handoff_pin["relativeToB"])
    pin(BASE / handoff_pin["relativeToB"], handoff_pin["bytes"], handoff_pin["sha256"])
    frozen = parent["filesNowFrozen"] + handoff["preservation"]["other21FrozenCatalogue"]
    require(len(frozen) == len({r["path"] for r in frozen}) == 30, "Current frozen30 union")
    for row in parent["filesNowFrozen"]:
        pin(ROOT / row["path"], row["bytes"], row["sha256"])
    frozen_records = [dict(r, stat=stat_only(ROOT / r["path"])) for r in frozen]
    record(PREP / "frozen30-before.json", frozen_records)
    production = []
    for path in source_paths():
        rel = path.relative_to(ROOT).as_posix()
        raw = data(path)
        exclusive(PREP / "before/production" / rel, raw)
        production.append(dict(identity(path), relativePath=rel))
    record(PREP / "production77-before.json", production)
    exclusive(PREP / "before/harness" / OLD_PROGRAM, data(ROOT / OLD_PROGRAM))
    tools, refs = tool_inputs()
    record(PREP / "tool-reference-validation.json", {"tools":tools,
        "profiles":{p:{"count":len(refs["profiles"][p]["files"])} for p in ("net10.0","netstandard2.1")},
        "authority":authority,"adjacentOldMetadataOrHelpersRead":False})
    before = data(ROOT / CONTRACTS).decode()
    spans = []
    for expected in contract["preservation"]["fourCurrentMatchConstructorBlocks"]:
        value = block(before, "        public " + expected["signaturePrefix"])
        require((len(value.encode()), hashlib.sha256(value.encode()).hexdigest())
                == (expected["bytes"], expected["sha256"]), "Current constructor pin")
        spans.append(dict(expected))
    record(PREP / "constructors-before.json", spans)
    record(PREP / "before-complete.json", {"atUtc":now(),"production":77,"frozen":30,
        "released":4,"actualOriginalAcquisitions":0,"compilers":0,"managed":0})


def verify_unreleased():
    released = {r["path"] for r in read_json(META / "implementation-admission.json")["frozenFileReleases"]}
    for row in read_json(PREP / "frozen30-before.json"):
        if row["path"] not in released:
            require(stat_only(ROOT / row["path"]) == row["stat"], "Frozen metadata changed: " + row["path"])
    for row in read_json(PREP / "production77-before.json"):
        if row["relativePath"] not in released:
            pin(ROOT / row["relativePath"], row["bytes"], row["sha256"])


def replace_once(text, before, after):
    require(text.count(before) == 1, "Unique exact inverse anchor: " + before[:100])
    return text.replace(before, after)


def static_preservation():
    verify_unreleased()
    contract = read_json(META / "contract.json")
    releases = read_json(META / "implementation-admission.json")["frozenFileReleases"]
    inverses = {}
    state = data(ROOT / STATE).decode()
    state = replace_once(state, ARRAYS, "")
    state = replace_once(state, YARD_UPDATES, "")
    state = replace_once(state, TD_UPDATE, "")
    inverses[STATE] = state.encode()
    contracts = data(ROOT / CONTRACTS).decode()
    old_contracts = data(PREP / "before/production" / CONTRACTS).decode()
    for expected in contract["preservation"]["fourCurrentMatchConstructorBlocks"]:
        value = block(contracts, "        public " + expected["signaturePrefix"])
        require((len(value.encode()), hashlib.sha256(value.encode()).hexdigest())
                == (expected["bytes"], expected["sha256"]), "Preserved constructor pin")
    require(block(contracts,"    public sealed class SourcePassCompletionsView")
            == block(old_contracts,"    public sealed class SourcePassCompletionsView"),
            "Existing completion DTO unchanged")
    new_view = block(contracts,"    public sealed class SourceCompletedPassStatsView")
    new_ctor = block(contracts,"        public SourceMatchView(SourceCompletedPassStatsView")
    delegation = (": this(passCompletions, interceptions, timeOfPossession, configuration, playKind, phase, period, possession,\n"
        "                quarterScores, stations, result, homePlay, awayPlay, selectionCountdown, patChoice, patStatus, kickoff,\n"
        "                kickingTeam, receivingTeam, offenseFlipped, homeGameplay, awayGameplay,\n"
        "                forwardCatcherPlayerId, dronesLose, dronesWinning, handicapRepetitionBonus,\n"
        "                compatibility, compatibilityRepairCount, compatibilityRepairs, selectionTimer,\n"
        "                homeRandomSelection, awayRandomSelection, audibles)")
    require(delegation in new_ctor, "Exact fifth-constructor forwarding")
    contracts = replace_once(contracts, new_view + "\n\n", "")
    contracts = replace_once(contracts, "        public SourceCompletedPassStatsView CompletedPassStats { get; }\n", "")
    contracts = replace_once(contracts, new_ctor + "\n\n", "")
    inverses[CONTRACTS] = contracts.encode()
    snapshot = replace_once(data(ROOT / SNAPSHOT).decode(), NEW_SNAPSHOT, OLD_SNAPSHOT)
    inverses[SNAPSHOT] = snapshot.encode()
    program = data(ROOT / OLD_PROGRAM).decode()
    for change in contract["readyImplementation"]["requestedFrozenReleases"][-1]["onlyExactReplacements"]:
        program = replace_once(program, change["after"], change["before"])
    inverses[OLD_PROGRAM] = program.encode()
    for row in releases:
        raw = inverses[row["path"]]
        require((len(raw), hashlib.sha256(raw).hexdigest()) == (row["beforeBytes"], row["beforeSha256"]),
                "Whole-file inverse mismatch: " + row["path"])
    guard = load_guard()
    caller = data(ROOT / CALLER)
    result = guard.require_caller_wiring(data(ROOT / "Assets/Scripts/Gameplay/SourceMatchFlow.cs"), caller)
    first = replace_once(caller.decode(), CALL_AFTER, CALL_BEFORE)
    require(hashlib.sha256(first.encode()).hexdigest()
            == "6b200f1c377d6a3d7a9435541bcb3ee0a77f54ff61ec8a7fa1a0e35eef1c2884", "Exact caller stage1")
    require(hashlib.sha256(first.replace(guard.CURRENT_CALL, guard.LEGACY_CALL).encode()).hexdigest()
            == guard.CALLER_PREEDIT_SHA256 == OLD_REVERSE, "Exact caller stage2")
    return {"status":"passed-static-only","fourWholeFileInverses":{k:hashlib.sha256(v).hexdigest()
        for k,v in inverses.items()},"constructors":4,"existingCompletionViewUnchanged":True,
        "caller":result,"simulationExecuted":False,"other26":"Parent hashes plus unchanged filesystem metadata",
        "actual77UnreleasedBytes":"unchanged"}


def freeze_authored():
    require((PREP / "before-complete.json").exists(), "Before capture required")
    static = static_preservation()
    production = []
    for path in source_paths():
        rel = path.relative_to(ROOT).as_posix()
        exclusive(PREP / "authored/production" / rel, data(path))
        production.append(dict(identity(path), relativePath=rel))
    record(PREP / "production77-authored.json", production)
    harness = []
    for rel in dict.fromkeys(HARNESS_FILES + (OLD_PROGRAM,)):
        exclusive(PREP / "authored/harness" / rel, data(ROOT / rel))
        harness.append(dict(identity(ROOT / rel), relativePath=rel))
    record(PREP / "harness-authored.json", harness)
    inventory = record(PREP / "expected-inventory.json", expected_inventory())
    record(PREP / "static-preservation.json", static)
    record(PREP / "authored-complete.json", {"atUtc":now(),"production":77,"harnessSources":list(HARNESS_SOURCES),
        "entryPoint":"CompletedPassStatsChecks","expectedInventory":inventory,"authority":authorities(),
        "sourceCatalogue":identity(PREP / "production77-authored.json"),
        "harnessCatalogue":identity(PREP / "harness-authored.json"),"compilers":0,"managed":0})
    print("Frozen55 literal cases/" + str(expected_inventory()["fieldCount"]) + " fields;20 controls; actual77 and two distinct harness sources.")


def verify_authored():
    authorities()
    for file in ("production77-authored.json","harness-authored.json"):
        for row in read_json(PREP / file):
            pin(ROOT / row["relativePath"], row["bytes"], row["sha256"])
            tree = "production" if file.startswith("production") else "harness"
            pin(PREP / "authored" / tree / row["relativePath"], row["bytes"], row["sha256"])
    require(read_json(PREP / "expected-inventory.json") == expected_inventory(), "Frozen literal inventory")
    verify_unreleased()


def qualify_control(exit_code, observed, control, expected):
    selected = [r for r in expected["fields"] if r["case"] == control["case"]]
    index = next(i for i,r in enumerate(selected) if r["field"] == control["requiredAssertion"])
    fields = observed.get("fields", [])
    require(exit_code == 2 and observed.get("status") == "assertion-failed"
            and observed.get("selectedCase") == control["case"]
            and observed.get("failedAssertion") == control["requiredAssertion"]
            and observed.get("cases") == [] and observed.get("assertions") == index + 1
            and len(fields) == index + 1, "Exact normal selected semantic failure required")
    for i, row in enumerate(fields):
        require(all(row.get(k) == selected[i][k] for k in ("case","field","expected")),
                "Unchanged literal assertion prefix")
        require((row["actual"] == row["expected"]) == (i < index), "Clean prefix then real difference required")


def fixture_check(observed, selected=None):
    fixture = observed["fixture"]
    require(fixture["randomRequests"] == 0 and fixture["originalProvenanceSupplied"] is False
            and fixture["initializationStepsPerLayer"] == 1 and fixture["sourceFrames"] == 1
            and fixture["selectedFrame"] == 0 and fixture["joints"] == 15, "Strict synthetic fixture")
    expected = 14 if selected is None or selected == "pass.lifecycle" else 0
    require(fixture["actorsInitialized"] == expected and fixture["layersInitialized"] == expected * 2,
            "Actual all14/28 fixture or no initialization")


def isolated_environment(directory):
    locations = {name:directory/"process-files"/name for name in ("home","process","cli","packages")}
    for path in locations.values():
        path.mkdir(parents=True, exist_ok=False)
    return {
        "HOME":str(locations["home"]), "TMP":str(locations["process"]),
        "TEMP":str(locations["process"]), "TMPDIR":str(locations["process"]),
        "DOTNET_CLI_HOME":str(locations["cli"]), "NUGET_PACKAGES":str(locations["packages"]),
        "DOTNET_ROOT":"/home/long2know/.dotnet",
        "DOTNET_CLI_TELEMETRY_OPTOUT":"1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE":"1",
        "DOTNET_NOLOGO":"1", "DOTNET_EnableDiagnostics":"0", "COMPlus_EnableDiagnostics":"0",
        "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT":"1", "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE":"true",
    }


class Campaign:
    def __init__(self):
        self.tools, self.refs = tool_inputs()
        self.expected = read_json(PREP / "expected-inventory.json")
        self.root = OUT / "attempt-01"
        self.root.mkdir(parents=True, exist_ok=False)
        self.jobs = []

    def capture(self, name, control=None):
        directory = self.root / name
        directory.mkdir()
        manifest, sources = [], []
        for row in read_json(PREP / "production77-authored.json"):
            rel = row["relativePath"]
            raw = data(PREP / "authored/production" / rel)
            if control is not None and rel == control["file"]:
                before, after = control["before"].encode(), control["after"].encode()
                require(raw.count(before) == 1, "Unique actual-source control anchor")
                changed = raw.replace(before, after)
                record(directory / "mutation.json", dict(control, beforeSha256=hashlib.sha256(raw).hexdigest(),
                    afterSha256=hashlib.sha256(changed).hexdigest(), liveSourceModified=False, expectationsChanged=False))
                raw = changed
            target = directory / "sources" / rel
            exclusive(target, raw)
            sources.append(target)
            manifest.append(dict(identity(target), relativePath=rel))
        for rel in HARNESS_SOURCES:
            exclusive(directory / "harness" / rel, data(PREP / "authored/harness" / rel))
        record(directory / "source-capture.json", manifest)
        record(directory / "component.runtimeconfig.json", {"runtimeOptions":read_json(ADMISSION)["tools"]["runtime"]})
        return directory, sources

    def launch(self, directory, kind, argv, expectation, ns21=False, binary=None):
        job = directory / kind
        job.mkdir()
        environment = isolated_environment(job)
        timeout = 120 if kind == "csc" else 60
        for name in ("python","dotnet","compiler"):
            row = self.tools[name]
            pin(row["path"], row["bytes"], row["sha256"])
        verify_authored()
        profile = "netstandard2.1" if ns21 else "net10.0"
        for row in self.refs["profiles"][profile]["files"]:
            pin(row["path"], row["bytes"], row["sha256"])
        inputs = {"sourceCapture":identity(directory / "source-capture.json"),
                  "harness":{rel:identity(directory / "harness" / rel) for rel in HARNESS_SOURCES},
                  "tools":self.tools,"referenceProfile":profile,
                  "references":self.refs["profiles"][profile]["files"],
                  "runtimeConfig":identity(directory / "component.runtimeconfig.json"),
                  "inventory":identity(PREP / "expected-inventory.json"),"binary":binary}
        reservation = reserve(kind, argv, directory, environment, timeout, expectation, inputs, ns21)
        record(job / "prelaunch.json", {"reservation":reservation,"expected":expectation,"inputs":inputs})
        started = now()
        timed_out = False
        with (job/"stdout.txt").open("xb") as stdout, (job/"stderr.txt").open("xb") as stderr:
            process = subprocess.Popen(argv, cwd=directory, env=environment, stdout=stdout, stderr=stderr,
                                       start_new_session=True)
            record(job/"started.json", {"atUtc":now(),"pid":process.pid,"parentPid":os.getpid(),
                "outerShellIdentity":os.environ.get("BLITZ_OUTER_SHELL_ID"),"reservation":reservation})
            try:
                code = process.wait(timeout=timeout)
            except (subprocess.TimeoutExpired, TimeoutError):
                timed_out = True
                process.kill()
                code = process.wait()
        result = {"startedAtUtc":started,"completedAtUtc":now(),"pid":process.pid,"parentPid":os.getpid(),
            "outerShellIdentity":os.environ.get("BLITZ_OUTER_SHELL_ID"),"kind":kind,
            "exit":code,"timedOut":timed_out,"reservation":reservation,"consumed":counts(),
            "stdout":identity(job/"stdout.txt"),"stderr":identity(job/"stderr.txt")}
        receipt = record(job/"completed.json", result)
        self.jobs.append(receipt)
        require(not timed_out, "Timeout is not semantic evidence")
        return result, data(job/"stdout.txt"), data(job/"stderr.txt")

    def compile(self, directory, sources, ns21=False):
        profile = "netstandard2.1" if ns21 else "net10.0"
        target = directory / ("compatibility.dll" if ns21 else "component.dll")
        argv = [self.tools["dotnet"]["path"],"exec",self.tools["compiler"]["path"],
            "/noconfig","/nostdlib+","/langversion:7.3","/deterministic+","/warnaserror+",
            "/target:library" if ns21 else "/target:exe", "/out:"+str(target)]
        if not ns21:
            argv.append("/main:CompletedPassStatsChecks")
        argv.extend("/reference:"+row["path"] for row in self.refs["profiles"][profile]["files"])
        argv.extend(str(path) for path in sources)
        if not ns21:
            argv.extend(str(directory/"harness"/rel) for rel in HARNESS_SOURCES)
        result, stdout, stderr = self.launch(directory, "csc", argv,
            {"exit":0,"productionSources":77,"harnessSources":0 if ns21 else 2,
             "references":121 if ns21 else 167,"profile":profile,"warningsAsErrors":True}, ns21)
        require(result["exit"] == 0, "Compilation failed, never a semantic kill:\n"
                + (stdout+stderr).decode(errors="replace")[-16000:])
        binary = identity(target)
        record(directory/"binary.json", {"binary":binary,"compilerReceipt":self.jobs[-1],
            "profile":profile,"executed":False,"runtimeConfig":identity(directory/"component.runtimeconfig.json")})
        return binary

    def execute(self, directory, binary, control=None):
        pin(binary["path"], binary["bytes"], binary["sha256"])
        argv = [self.tools["dotnet"]["path"],"exec",binary["path"]]
        if control:
            argv.extend(("--case",control["case"]))
        result, stdout, stderr = self.launch(directory, "managed", argv,
            {"exit":2 if control else 0,"selectedCase":control["case"] if control else None,
             "failure":control["requiredAssertion"] if control else None,
             "fullCases":55 if not control else None}, binary=binary)
        require(not stderr, "Unexpected managed stderr")
        observed = json.loads(stdout)
        if control:
            qualify_control(result["exit"], observed, control, self.expected)
        else:
            expected = [{k:r[k] for k in ("case","field","expected")} for r in self.expected["fields"]]
            actual = [{k:r[k] for k in ("case","field","expected")} for r in observed["fields"]]
            require(result["exit"] == 0 and observed["status"] == "passed"
                and observed["cases"] == self.expected["cases"]
                and observed["assertions"] == self.expected["fieldCount"] and actual == expected
                and all(r["actual"] == r["expected"] for r in observed["fields"]),
                "Complete clean55-case frozen literal inventory required")
        fixture_check(observed, control["case"] if control else None)
        return record(directory/"semantic-result.json", {"status":"qualified-component-control" if control else "passed-components",
            "control":control,"managedReceipt":self.jobs[-1],"observations":observed,
            "realGameSimulationExecuted":False,"originalAnimationAdmission":False})

    def run(self):
        verify_authored()
        require(read_json(PREP/"unit-result.json")["exit"] == 0, "New Python units must pass first")
        directory, sources = self.capture("baseline")
        baseline = self.execute(directory, self.compile(directory, sources))
        controls = []
        for i, control in enumerate(self.expected["controls"], 1):
            directory, sources = self.capture("control-"+str(i).zfill(2), control)
            controls.append(self.execute(directory, self.compile(directory, sources), control))
        directory, sources = self.capture("final")
        final = self.execute(directory, self.compile(directory, sources))
        directory, sources = self.capture("ns21-compatibility-only")
        ns21 = self.compile(directory, sources, True)
        verify_authored()
        static = static_preservation()
        return record(self.root/"campaign-result.json", {"status":"PASSED_BOUNDED_COMPONENTS_AND_API_COMPATIBILITY",
            "baseline":baseline,"controls":controls,"final":final,"ns21CompileOnly":ns21,
            "jobs":self.jobs,"consumed":counts(),"inventory":identity(PREP/"expected-inventory.json"),
            "staticPreservation":static,"newOutputBytes":check_window(),
            "scope":"Synthetic actual components, not real GameSimulation/common caller/C2/Step/Restart/native."})


def package_handoff():
    verify_authored()
    result = read_json(OUT/"attempt-01/campaign-result.json")
    require(result["status"] == "PASSED_BOUNDED_COMPONENTS_AND_API_COMPATIBILITY", "Complete campaign required")
    inventory = read_json(PREP/"expected-inventory.json")
    released = [dict(identity(ROOT/r["path"]), relativePath=r["path"])
        for r in read_json(META/"implementation-admission.json")["frozenFileReleases"]]
    output = {"schema":"source-completed-pass-statistics-write-handoff/v1",
        "status":"IMPLEMENTED_FRESH_COMPONENT_EVIDENCE_AWAITING_QUALITY",
        "accountableAuthor":"Unity","soleMaterialReviewer":"Quality","requester":"Stephen Long (@stlong_microsoft)",
        "worktree":str(ROOT),"baseB":str(BASE),"atActualUtc":now(),"authority":authorities(),
        "contract":identity(META/"contract.json"),"releasedFourAuthored":released,
        "newFourTests":[identity(ROOT/p) for p in HARNESS_FILES],
        "actual77Before":identity(PREP/"production77-before.json"),
        "actual77Authored":identity(PREP/"production77-authored.json"),
        "harnessAuthored":identity(PREP/"harness-authored.json"),
        "frozen30Before":identity(PREP/"frozen30-before.json"),
        "preservation":static_preservation(),"fourConstructorPins":read_json(PREP/"constructors-before.json"),
        "literalInventory":identity(PREP/"expected-inventory.json"),
        "freshCasesEachBaseline":inventory["caseCount"],"freshFieldsEachBaseline":inventory["fieldCount"],
        "actualSourceSemanticControls":20,"campaign":identity(OUT/"attempt-01/campaign-result.json"),
        "baseline":result["baseline"],"controls":result["controls"],"final":result["final"],
        "ns21CompileOnly":result["ns21CompileOnly"],
        "unitReceipt":identity(PREP/"unit-result.json"),
        "allLaunchReservations":[identity(p) for p in sorted((PREP/"ledger").glob("*.json"))],
        "outerProcessReceipts":[identity(p) for p in sorted(PREP.glob("outer-*-result.json"))],
        "freshConsumedIncludingThisMetadataReservation":counts(),
        "phaseStart":read_json(PREP/"phase-start.json"),
        "failures":"Every reserved launch and immutable completed receipt retained; no refunds or replay.",
        "syntheticFixture":"Reused unchanged-body typed helpers only; all14 actors/28 genuine layers, one initialization each, zero RNG, false original provenance. No old Main/controller.",
        "qualifiedBehavior":"Signed saved bounded FieldEnd minus original LOS, classifier-owned passing/independent total accumulation, existing completion once, Touchdown-only pass TD, immutable copies/old-constructor nulls/fresh-state allocation.",
        "unqualified":["Real GameSimulation/common caller/C2/Step/natural terminal-idle exactly-once/public Restart",
            "Authentic ANI/original admission/native/Unity/browser/final screen",
            "Attempts/rush/return/sack/first-down/turnover/full total-yard producers or whole-game parity"],
        "oldCampaignsRerun":False,"oldResultsUsedAsNewEvidence":False,
        "publicationAuthorized":False,"selfApproval":False,"wholeTaskComplete":False}
    raw = (json.dumps(output,indent=2,sort_keys=True)+"\n").encode()
    require(len(raw) <= 65536, "Bounded handoff size")
    exclusive(META/"source-write-handoff.json", raw)
    print("Fresh handoff written; independent Quality required. "
          + str(inventory["caseCount"])+" cases/"+str(inventory["fieldCount"])+" fields;20 controls; NS2.1 compile-only.")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--admission", type=Path)
    modes = parser.add_mutually_exclusive_group(required=True)
    modes.add_argument("--prepare-before", action="store_true")
    modes.add_argument("--freeze-authored", action="store_true")
    modes.add_argument("--package-handoff", action="store_true")
    modes.add_argument("--controls", action="store_true")
    args = parser.parse_args()
    require(args.admission is not None and args.admission.resolve() == ADMISSION, "Exact new admission path")
    authorities()
    kind = "drivers" if args.controls else "metadata"
    tag, started = entry(kind, [PYTHON,"-I","-S","-B"]+sys.argv)
    signal.signal(signal.SIGALRM, alarm_timeout)
    signal.alarm(1200 if args.controls else 180)
    code, summary = 0, {}
    try:
        if args.prepare_before:
            prepare_before()
            summary = {"preparedBefore":True}
        elif args.freeze_authored:
            freeze_authored()
            summary = {"frozenAuthored":True}
        elif args.package_handoff:
            package_handoff()
            summary = {"packaged":True,"handoff":identity(META/"source-write-handoff.json")}
        else:
            summary = {"campaign":Campaign().run()}
    except Exception as error:
        code = 1
        summary = {"error":type(error).__name__,"message":str(error)}
        print(json.dumps(summary), file=sys.stderr)
    finish_entry(tag, started, code, summary)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
