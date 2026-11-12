#!/usr/bin/python3.12
"""Isolated, actual-source completion components; never a Source runtime harness."""
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
META = BASE / "metadata/source-offensive-stats"
ORIGINAL_PREP = META / "preparation"
PREP = ORIGINAL_PREP / "continuation-01"
OUT = META / "validation"
SUITE = ROOT / "tests/source-offensive-stats"
PYTHON = "/usr/bin/python3.12"
ADMISSION = META / "execution-admission.json"
HARNESS = ("Program.cs", "run.py", "test_runner.py", "README.md")
PINS = {
    "contract.json": (51106, "32ef8b094f4903bed077789aefd2aa8fbb9371682a51578ce15dfa022cbf6bc2"),
    "implementation-admission.json": (6505, "b2f6dd34be1263c6b12c7767269da7a800e5f53039780ed5a5f2b1d54eec2342"),
    "execution-admission.json": (7538, "cbfa18b35919592d5e74456078da8594d9d90ba1dca4b8e26d3e6a57e6a7eb5a"),
    "fixture-continuation-admission.json": (9528, "971b3340aae0ff9e73d4ef2532b5754df0a34ca11d4e223d234af0c6e7515d0b"),
}
BEFORE_CALL = (
    "            ApplySourceFirePlayEnd(endingInput, resolution);\n"
    "            Model.SourceLastResult = resolution.Classification.Result;\n"
)
AFTER_CALL = BEFORE_CALL.replace(
    "            Model.SourceLastResult",
    "            Model.AccumulateSourcePassCompletion(endingInput, resolution, Model.CarrierId);\n"
    "            Model.SourceLastResult",
)
OLD_REVERSE = "726f513c0549d8bf028734c7d7c449dfbff430f8f7c244053b4cf8fdc03b8091"
STATE = "Assets/Scripts/Gameplay/SimulationState.cs"
SNAPSHOT = "Assets/Scripts/Gameplay/GameSnapshot.cs"
CALLER = "Assets/Scripts/Gameplay/GameSimulation.Source.cs"
CONTRACTS = "Assets/Scripts/Contracts/SourceMatchContracts.cs"
GUARD = "tests/contracts/run.py"
TABLE = (
    ("completion.gain", 1, 0, 50, False),
    ("completion.loss", 1, 0, 50, False),
    ("completion.zero-gain", 1, 0, 50, False),
    ("completion.before-next-possession", 1, 0, 50, True),
    ("completion.touchdown", 1, 1, 100, False),
    ("completion.conversion", 1, 11, 100, False),
    ("guard.pass-thrown-is-not-forward", 0, 0, 50, False),
    ("guard.no-forward-flag", 0, 0, 50, False),
    ("guard.incomplete", 0, 5, 50, False),
    ("guard.possession-change", 0, 4, 50, False),
    ("guard.established-side-not-initial", 0, 4, 50, True),
    ("guard.dead-ball-independent", 0, 0, 50, False),
    ("guard.quick-return-new-possession", 0, 4, 50, False),
    ("guard.quick-return-touchdown", 0, 1, 100, False),
    ("completion.quick-bit-not-blanket-veto", 1, 0, 50, False),
    ("completion.kick-bits-not-blanket-veto", 1, 0, 50, False),
)
CONTROLS = (
    ("completion-increment-omitted", STATE,
     "checked(SourcePassCompletions[side] + 1)", "checked(SourcePassCompletions[side] + 0)",
     "completion.gain.home", "completion.gain.home.counter.home"),
    ("completion-overwrite-instead-of-accumulate", STATE,
     "SourcePassCompletions[side] = checked(SourcePassCompletions[side] + 1);",
     "SourcePassCompletions[side] = 1;",
     "completion.lifecycle", "completion.lifecycle.second-home-credit"),
    ("completion-next-possession-owner", STATE,
     "int side = resolution.Classification.OffenseSide;", "int side = resolution.State.OffenseSide;",
     "completion.before-next-possession.home", "completion.before-next-possession.home.counter.home"),
    ("completion-pass-thrown-for-forward", STATE,
     "(flags & SourceGameFlags.ForwardPass) == 0", "(flags & SourceGameFlags.PassThrown) == 0",
     "guard.pass-thrown-is-not-forward.home", "guard.pass-thrown-is-not-forward.home.counter.home"),
    ("completion-incomplete-guard-omitted", STATE,
     "result == SourcePlayResult.Incomplete ||", "false ||",
     "guard.incomplete.home", "guard.incomplete.home.counter.home"),
    ("completion-possession-guard-omitted", STATE,
     "(flags & SourceGameFlags.PossessionChange) != 0 ||", "false ||",
     "guard.possession-change.home", "guard.possession-change.home.counter.home"),
    ("completion-deadball-guard-omitted", STATE,
     "input.Cause == SourcePlayEndCause.DeadBallInBounds ||", "false ||",
     "guard.dead-ball-independent.home", "guard.dead-ball-independent.home.counter.home"),
    ("completion-quick-return-precedence-omitted", STATE,
     "if (quickReturn ||", "if ((quickReturn && false) ||",
     "guard.quick-return-touchdown.home", "guard.quick-return-touchdown.home.counter.home"),
    ("completion-snapshot-team-swap", SNAPSHOT,
     "new SourcePassCompletionsView(model.SourcePassCompletions[(int)TeamId.Home],\n"
     "                    model.SourcePassCompletions[(int)TeamId.Away])",
     "new SourcePassCompletionsView(model.SourcePassCompletions[(int)TeamId.Away],\n"
     "                    model.SourcePassCompletions[(int)TeamId.Home])",
     "completion.lifecycle", "completion.lifecycle.snapshot-s1.home"),
)


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def data(path):
    path = Path(path)
    require(not path.is_symlink() and path.is_file(), "Regular explicit file required: " + str(path))
    return path.read_bytes()


def identity(path):
    raw = data(path)
    return {"path": str(path), "bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}


def check_pin(path, size, sha):
    result = identity(path)
    require((result["bytes"], result["sha256"]) == (size, sha), "Pin mismatch: " + str(path))
    return result


def exclusive(path, raw):
    path = Path(path)
    require(path.is_relative_to(META), "Output outside admitted metadata root")
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("xb") as stream:
        stream.write(raw)


def record(path, value):
    exclusive(path, (json.dumps(value, indent=2, sort_keys=True) + "\n").encode())
    return identity(path)


def read_json(path):
    return json.loads(data(path))


def load_guard():
    spec = importlib.util.spec_from_file_location("completion_existing_static_guard", ROOT / GUARD)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def isolated_environment(root):
    folders = {name: root / name for name in ("home", "tmp", "cli", "packages")}
    for path in folders.values():
        path.mkdir(parents=True, exist_ok=True)
    return {
        "HOME": str(folders["home"]), "TMP": str(folders["tmp"]),
        "TEMP": str(folders["tmp"]), "TMPDIR": str(folders["tmp"]),
        "DOTNET_CLI_HOME": str(folders["cli"]), "NUGET_PACKAGES": str(folders["packages"]),
        "DOTNET_ROOT": "/home/long2know/.dotnet",
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
        "DOTNET_NOLOGO": "1", "DOTNET_EnableDiagnostics": "0",
        "COMPlus_EnableDiagnostics": "0", "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT": "1",
        "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "true",
    }


def authorities():
    return {name: check_pin(META / name, *pin) for name, pin in PINS.items()}


def source_paths():
    project = ROOT / "tests/Blitz.Core.Tests/Blitz.Core.Tests.csproj"
    props = ROOT / "tests/audio/vm/SourceSoundVmSources.props"
    includes = [x.attrib["Include"] for x in ET.fromstring(data(project)).iter("Compile")]
    require(set(includes) == {
        "$(BlitzSourceRoot)Assets/Scripts/Contracts/**/*.cs",
        "$(BlitzSourceRoot)Assets/Scripts/Gameplay/**/*.cs",
        "$(BlitzSourceRoot)Assets/Scripts/AssetImport/*.cs",
    }, "Public Core compile list changed")
    sources = []
    for directory, pattern, count in (("Contracts", "**/*.cs", 10), ("Gameplay", "**/*.cs", 38),
                                      ("AssetImport", "*.cs", 27)):
        selected = sorted((ROOT / "Assets/Scripts" / directory).glob(pattern))
        require(len(selected) == count, "Unexpected public source inventory: " + directory)
        sources.extend(selected)
    extras = [x.attrib["Include"].replace("$(BlitzSourceRoot)", "") for x in ET.fromstring(data(props)).iter("Compile")]
    require(extras == ["Assets/Scripts/Presentation/SourceAudioFunctions.cs",
                       "Assets/Scripts/Presentation/SourceC2SoundFunctions.cs"], "Pure selector list changed")
    sources.extend(ROOT / path for path in extras)
    require(len(set(sources)) == len(sources) == 77, "Actual77 closure required")
    return sorted(sources)


def stat_only(path):
    result = path.stat(follow_symlinks=False)
    require(not path.is_symlink(), "No source symlinks")
    return {"device": result.st_dev, "inode": result.st_ino, "bytes": result.st_size,
            "mtimeNs": result.st_mtime_ns, "ctimeNs": result.st_ctime_ns}


def constructor_spans(raw):
    text = raw.decode()
    result = []
    for signature in ("SourceMatchConfiguration", "SourceTimeOfPossessionView", "SourceInterceptionsView"):
        start = text.index("        public SourceMatchView(" + signature)
        index = text.index("{", start)
        depth, end = 1, index + 1
        while depth:
            depth += (text[end] == "{") - (text[end] == "}")
            end += 1
        value = text[start:end].encode()
        result.append({"signature": signature, "bytes": len(value),
                       "sha256": hashlib.sha256(value).hexdigest(), "raw": value})
    return result


def capture_before():
    authority = authorities()
    entry = record(PREP / "0001-entry.json", {
        "atUtc": now(), "pid": os.getpid(), "parentPid": os.getppid(),
        "argv": [PYTHON, "-I", "-S", "-B", str(SUITE / "run.py"), "--capture-before"],
        "cwd": str(ROOT), "inheritedWorkloadEnvironment": dict(os.environ),
        "outerPolicy": "The tool shell exec -c replaces itself; Python starts with an empty environment.",
        "reservation": identity(PREP / "0001-reserved.json"),
        "consumed": {"metadataInspections": 1, "drivers": 0, "compilers": 0, "managed": 0, "units": 0},
    })
    impl, execution = read_json(META / "implementation-admission.json"), read_json(ADMISSION)
    old = read_json(BASE / "metadata/source-final-stats/source-write-handoff.json")
    accepted = read_json(BASE / "metadata/source-final-stats/parent-scoped-acceptance.json")
    catalog = old["preservation"]["unchanged21"] + accepted["filesNowFrozen"]
    require(len({x["path"] for x in catalog}) == 26, "Current26 catalogue required")
    frozen = []
    for row in catalog:
        frozen.append(dict(row, beforeStat=stat_only(ROOT / row["path"])))
    record(PREP / "frozen26-before.json", frozen)
    before = []
    for path in source_paths():
        raw = data(path)
        rel = path.relative_to(ROOT).as_posix()
        exclusive(PREP / "before/production" / rel, raw)
        before.append(dict(identity(path), relativePath=rel))
    record(PREP / "production77-before.json", before)
    for item in impl["releasedExistingPaths"]:
        path = ROOT / item["path"]
        check_pin(path, item["beforeBytes"], item["beforeSha256"])
        exclusive(PREP / "before/released" / item["path"], data(path))
    spans = constructor_spans(data(ROOT / CONTRACTS))
    for span in spans:
        exclusive(PREP / "before/constructors" / (span["signature"] + ".txt"), span.pop("raw"))
    record(PREP / "constructors-before.json", spans)
    tools_path = BASE / execution["tools"]["toolManifestRelativeToBase"]
    refs_path = BASE / execution["tools"]["referenceManifestRelativeToBase"]
    check_pin(tools_path, execution["tools"]["toolManifestBytes"], execution["tools"]["toolManifestSha256"])
    check_pin(refs_path, execution["tools"]["referenceManifestBytes"], execution["tools"]["referenceManifestSha256"])
    exclusive(PREP / "tool-inputs.json", data(tools_path))
    exclusive(PREP / "reference-inputs.json", data(refs_path))
    tools, refs = read_json(tools_path), read_json(refs_path)
    verified = []
    for row in tools.values():
        verified.append(check_pin(row["path"], row["bytes"], row["sha256"]))
    for name, count in (("net10.0", 167), ("netstandard2.1", 121)):
        profile = refs["profiles"][name]
        require(profile["count"] == len(profile["files"]) == count, "Reference inventory mismatch")
        for row in profile["files"]:
            verified.append(check_pin(row["path"], row["bytes"], row["sha256"]))
    record(PREP / "verified-tools-and-references.json", verified)
    record(PREP / "0001-completed.json", {
        "atUtc": now(), "pid": os.getpid(), "exit": 0, "entry": entry,
        "authority": authority, "productionSources": 77, "references": [167, 121],
        "frozen26": "Parent10259 verified the26 hashes; metadata captured for all26 without reading forbidden harness contents.",
        "sourceOrTestExecutions": 0,
    })
    print("Captured actual77 before-sources, five released archives, three constructors, current26 metadata and pinned tools/references.")


def expected_inventory():
    result, cases = [], []

    def add(case, suffix, value, unit):
        result.append({"case": case, "field": case + "." + suffix, "expected": value, "unit": unit})

    for stem, credit, classification, field_end, changes in TABLE:
        for side, team in enumerate(("home", "away")):
            case = stem + "." + team
            cases.append(case)
            add(case, "counter.home", 3 if credit and side == 0 else 2, "completions")
            add(case, "counter.away", 6 if credit and side == 1 else 5, "completions")
            owner = 1 - side if stem == "guard.established-side-not-initial" else side
            add(case, "classification.owner", owner, "team-side")
            add(case, "classification.result", classification, "source-result-enum")
            add(case, "next-owner", 1 - side if changes else side, "team-side")
            add(case, "field-end", field_end, "field-yards")
            for suffix, value, unit in (
                ("preserve.offensive-plays.home", 8, "plays"), ("preserve.offensive-plays.away", 13, "plays"),
                ("preserve.consecutive.home", 1, "receiver-fire-streak"),
                ("preserve.consecutive.away", 4, "receiver-fire-streak"),
                ("preserve.interceptions.home", 7, "interceptions-made"),
                ("preserve.interceptions.away", 9, "interceptions-made"),
                ("preserve.time.home", 11, "seconds"), ("preserve.time.away", 17, "seconds"),
                ("preserve.fire.home", 1, "boolean-int"), ("preserve.fire.away", 0, "boolean-int"),
            ):
                add(case, suffix, value, unit)
    case = "completion.lifecycle"
    cases.append(case)
    for suffix, value in (
        ("fresh.home", 0), ("fresh.away", 0), ("snapshot-s0.home", 0), ("snapshot-s0.away", 0),
        ("first-home-credit", 1), ("first-away-zero", 0), ("snapshot-s1.home", 1), ("snapshot-s1.away", 0),
        ("second-home-credit", 2), ("second-away-zero", 0),
        ("retained-s0.home", 0), ("retained-s0.away", 0), ("retained-s1.home", 1), ("retained-s1.away", 0),
        ("away-credit.home", 2), ("away-credit.away", 1), ("snapshot-s2.home", 2), ("snapshot-s2.away", 1),
        ("capture-not-producer.home", 2), ("capture-not-producer.away", 1),
        ("fresh-state-reset.home", 0), ("fresh-state-reset.away", 0),
        ("old-state-retained.home", 2), ("old-state-retained.away", 1),
        ("retained-after-reset.home", 1), ("retained-after-reset.away", 0),
    ):
        add(case, suffix, value, "completions")
    case = "completion.legacy-neutral"
    cases.append(case)
    for suffix, value, unit in (("match-null", 1, "boolean-int"), ("counter.home", 0, "completions"),
                                ("counter.away", 0, "completions")):
        add(case, suffix, value, unit)
    case = "completion.additive-api"
    cases.append(case)
    for suffix, value, unit in (
        ("dto.home", 2, "completions"), ("dto.away", 5, "completions"),
        ("negative-home-rejected", 1, "boolean-int"), ("negative-away-rejected", 1, "boolean-int"),
        ("old-base.unobserved", 1, "boolean-int"), ("old-time.unobserved", 1, "boolean-int"),
        ("old-interceptions.unobserved", 1, "boolean-int"),
        ("new-view-retained", 1, "boolean-int"), ("new-interceptions-retained", 1, "boolean-int"),
        ("new-time-retained", 1, "boolean-int"), ("null-new-view-rejected", 1, "boolean-int"),
        ("old-base.time-null", 1, "boolean-int"), ("old-base.interceptions-null", 1, "boolean-int"),
        ("old-time.time-retained", 1, "boolean-int"), ("old-time.interceptions-null", 1, "boolean-int"),
        ("old-interceptions.time-retained", 1, "boolean-int"),
        ("old-interceptions.interceptions-retained", 1, "boolean-int"),
    ):
        add(case, suffix, value, unit)
    require(len(cases) == 35 and len(result) == 558, "Declared35 cases/558 fields required")
    require(len({row["field"] for row in result}) == 558, "Unique semantic fields required")
    return {"cases": cases, "caseCount": 35, "fieldCount": 558, "fields": result,
            "controls": [{"id": c[0], "case": c[4], "requiredFailure": c[5]} for c in CONTROLS],
            "scope": "Synthetic internal production components; real GameSimulation/C2/Step/Restart UNRUN."}


def verify_sources():
    before = read_json(PREP / "production77-before.json")
    released = {r["path"] for r in read_json(META / "implementation-admission.json")["releasedExistingPaths"]}
    paths = source_paths()
    require([p.relative_to(ROOT).as_posix() for p in paths] == [r["relativePath"] for r in before],
            "Public77 paths changed")
    for row in before:
        if row["relativePath"] not in released:
            check_pin(ROOT / row["relativePath"], row["bytes"], row["sha256"])
    return before


def static_preservation():
    verify_sources()
    contract = read_json(META / "contract.json")
    caller = data(ROOT / CALLER)
    text = caller.decode()
    require(text.count(AFTER_CALL) == 1, "Exact unique new caller span")
    require(text.replace(AFTER_CALL, BEFORE_CALL).encode() == data(PREP / "before/released" / CALLER),
            "Caller exact inverse mismatch")
    check_pin(ROOT / CALLER, 157386, "ddb34cac693331d566bdbca855f08fce92599952ef10a2751017a657188a73dd")
    expected = {row["signature"]: row for row in read_json(PREP / "constructors-before.json")}
    for row in constructor_spans(data(ROOT / CONTRACTS)):
        require((row["bytes"], row["sha256"]) == (expected[row["signature"]]["bytes"], expected[row["signature"]]["sha256"]),
                "Existing constructor changed: " + row["signature"])
    snapshot = next(row for row in contract["minimumOwnershipRelease"]["existingFrozenPaths"] if row["path"] == SNAPSHOT)
    raw = data(ROOT / SNAPSHOT)
    require(raw.count(snapshot["exactAfter"].encode()) == 1, "Snapshot new prefix unique")
    require(raw.replace(snapshot["exactAfter"].encode(), snapshot["exactBefore"].encode())
            == data(PREP / "before/released" / SNAPSHOT), "Snapshot has unrelated changes")
    guard = load_guard()
    guard_result = guard.require_caller_wiring(data(ROOT / "Assets/Scripts/Gameplay/SourceMatchFlow.cs"), caller)
    require(guard.CALLER_PREEDIT_SHA256 == OLD_REVERSE, "Existing reverse hash changed")
    inverses = {}
    state = data(ROOT / STATE).decode()
    array = "        internal readonly int[] SourcePassCompletions = new int[2];\n"
    require(state.count(array) == 1, "One new ordinary stat array")
    first = state.index("        internal void AccumulateSourcePassCompletion(")
    last = state.index("        internal SimulationState()\n", first)
    restored_state = state[:first] + state[last:]
    restored_state = restored_state.replace(array, "")
    require(restored_state.encode() == data(PREP / "before/released" / STATE), "State has unrelated changes")
    inverses[STATE] = hashlib.sha256(restored_state.encode()).hexdigest()
    contracts = data(ROOT / CONTRACTS).decode()
    first = contracts.index("    public sealed class SourcePassCompletionsView\n")
    last = contracts.index("    public sealed class SourceInterceptionsView\n", first)
    contracts = contracts[:first] + contracts[last:]
    property_line = "        public SourcePassCompletionsView PassCompletions { get; }\n"
    require(contracts.count(property_line) == 1, "One additive Match property")
    contracts = contracts.replace(property_line, "")
    first = contracts.index("        public SourceMatchView(SourcePassCompletionsView")
    last = contracts.index("        public SourceRandomSelectionView RandomSelection(", first)
    contracts = contracts[:first] + contracts[last:]
    require(contracts.encode() == data(PREP / "before/released" / CONTRACTS), "Contracts have unrelated changes")
    inverses[CONTRACTS] = hashlib.sha256(contracts.encode()).hexdigest()
    guard_text = data(ROOT / GUARD).decode()
    first = guard_text.index("    completion_before = (\n")
    last = guard_text.index("    restored = caller.replace(CURRENT_CALL, LEGACY_CALL)\n", first)
    require("    caller = caller.replace(completion_after, completion_before)\n" in guard_text[first:last],
            "Exact guarded normalization required")
    guard_inverse = (guard_text[:first] + guard_text[last:]).encode()
    require(guard_inverse == data(PREP / "before/released" / GUARD), "Old guard code/fixtures changed")
    inverses[GUARD] = hashlib.sha256(guard_inverse).hexdigest()
    for row in read_json(PREP / "frozen26-before.json"):
        if row["path"] not in {CALLER, STATE, CONTRACTS, SNAPSHOT, GUARD}:
            require(stat_only(ROOT / row["path"]) == row["beforeStat"], "Other frozen file metadata changed: " + row["path"])
    return {"status": "passed-static-only", "realSimulationExecuted": False, "caller": guard_result,
            "other21": "All21 metadata identities unchanged from parent10259 hash-bound baseline; no forbidden harness body read.",
            "originalConstructorBlocks": 3, "callerInverseSha256": "6b200f1c377d6a3d7a9435541bcb3ee0a77f54ff61ec8a7fa1a0e35eef1c2884",
            "otherSurgicalInverseSha256": inverses, "reverseSha256": OLD_REVERSE}


def prepare_campaign():
    authorities()
    record(PREP / "0002-entry.json", {"atUtc": now(), "pid": os.getpid(), "parentPid": os.getppid(),
                                    "argv": sys.argv, "environment": dict(os.environ),
                                    "consumedMetadataInspections": 2})
    static = static_preservation()
    sources = []
    for path in source_paths():
        rel = path.relative_to(ROOT).as_posix()
        exclusive(PREP / "authored/production" / rel, data(path))
        sources.append(dict(identity(path), relativePath=rel))
    record(PREP / "production77-authored.json", sources)
    harness = []
    for name in HARNESS:
        exclusive(PREP / "authored/harness" / name, data(SUITE / name))
        harness.append(identity(SUITE / name))
    record(PREP / "harness-authored.json", harness)
    record(PREP / "expected-inventory.json", expected_inventory())
    record(PREP / "static-preservation.json", static)
    common = {"atUtc": now(), "cwd": str(ROOT), "environment": {}, "launcher": "tool shell exec -c",
              "authority": authorities(), "sources": identity(PREP / "production77-authored.json"),
              "harness": identity(PREP / "harness-authored.json"),
              "references": identity(PREP / "reference-inputs.json"), "tools": identity(PREP / "tool-inputs.json"),
              "expectedInventory": identity(PREP / "expected-inventory.json"),
              "actualClockAndPid": "Bound by a separate exclusive entry/outcome record at process entry, never a human reference."}
    record(PREP / "0003-unit-prelaunch.json", dict(common,
        argv=[PYTHON, "-I", "-S", "-B", str(SUITE / "test_runner.py"), "-q"], timeoutSeconds=180,
        reservedCounters={"metadataInspections": 2, "units": 1, "drivers": 0, "compilers": 0, "managed": 0}))
    record(PREP / "0004-driver-prelaunch.json", dict(common,
        argv=[PYTHON, "-I", "-S", "-B", str(SUITE / "run.py"), "--admission", str(ADMISSION), "--controls"],
        timeoutSeconds=1200,
        reservedCounters={"metadataInspections": 2, "units": 1, "drivers": 1, "compilers": 0, "managed": 0}))
    record(PREP / "0002-completed.json", {"atUtc": now(), "exit": 0, "pid": os.getpid(),
                                        "cases": 35, "fields": 558, "controls": 9, "compilations": 0})
    print("Frozen actual77 sources, four-file harness,35 cases/558 fields,9 controls and static preservation before first compile.")


def verify_frozen_authored():
    verify_readonly_production()
    for row in read_json(PREP / "production77-authored.json") + read_json(PREP / "harness-authored.json"):
        check_pin(row["path"], row["bytes"], row["sha256"])


def verify_readonly_production():
    check_pin(PREP / "previous-handoff.json", 26492,
              "400fb3956a0611c04ae913f6448188b30ce6f579e8d41c954d22cff1bd55dff0")
    for row in read_json(PREP / "previous-handoff.json")["fiveExactDescendants"]:
        check_pin(ROOT / row["path"], row["afterBytes"], row["afterSha256"])
    check_pin(SUITE / "test_runner.py", 6819,
              "caf7482c8e67d68ab22cecfa16eb04ab2a41f34efc58b25f584bc5e94d15ce42")


def prepare_continuation():
    authorities()
    verify_readonly_production()
    entry = record(PREP / "freeze-entry.json", {
        "atUtc": now(), "pid": os.getpid(), "parentPid": os.getppid(), "argv": sys.argv,
        "cwd": str(ROOT), "environment": dict(os.environ),
        "cumulative": {"csc": 1, "managed": 1, "units": 1, "drivers": 1, "metadata": 7, "corrections": 1},
        "prelaunch": identity(PREP / "freeze-prelaunch.json"),
    })
    for name in ("production77-before.json", "constructors-before.json", "frozen26-before.json",
                 "tool-inputs.json", "reference-inputs.json", "expected-inventory.json"):
        exclusive(PREP / name, data(ORIGINAL_PREP / name))
    for folder in ("before/released", "before/constructors"):
        for path in sorted((ORIGINAL_PREP / folder).rglob("*")):
            if path.is_file():
                exclusive(PREP / folder / path.relative_to(ORIGINAL_PREP / folder), data(path))
    check_pin(PREP / "expected-inventory.json", 104066,
              "ca8738a5dc26c735a7e729d91af531755d52d041a52aabccd90c3dda9312c919")
    require(expected_inventory() == read_json(PREP / "expected-inventory.json"),
            "Original35-case/558-field literal expectations must remain identical")
    static = static_preservation()
    sources = []
    for path in source_paths():
        rel = path.relative_to(ROOT).as_posix()
        exclusive(PREP / "authored/production" / rel, data(path))
        sources.append(dict(identity(path), relativePath=rel))
    record(PREP / "production77-authored.json", sources)
    harness = []
    for name in HARNESS:
        exclusive(PREP / "authored/harness" / name, data(SUITE / name))
        harness.append(identity(SUITE / name))
    record(PREP / "harness-authored.json", harness)
    record(PREP / "static-preservation.json", static)
    program = data(SUITE / "Program.cs")
    first = program.index(b"    private const string SyntheticClipKey")
    last = program.index(b"    private static void Check(", first)
    fixture = record(PREP / "fixture-declaration.json", {
        "origin": "SYNTHETIC/NON-ORIGINAL component-only values; NOT original WAN, pose or source ticks",
        "clipKey": "SYNTHETIC_COMPLETION_SNAPSHOT_FRAME",
        "programKey": "synthetic_completion_snapshot_frame_only",
        "frameCount": 1, "selectedAuthoringIndices": [0], "jointCount": 15,
        "jointRotations": "Each literal (0,0,0,1)", "authoringAndScaledRoots": [0, 0, 0],
        "programOps": ["NewSequence(declared synthetic key)", "NextFrame(1)", "End"],
        "actors": 14, "layersPerActor": 2, "actualInitializationCallsPerLayer": 1,
        "maximumPermittedStepsPerLayer": 4, "callbacksSoundFxGameplayDispatch": False,
        "randomRequests": "Delegate throws on every request; no program opcode requests RNG",
        "unknownClipOrFrameRequests": "Specific KeyNotFoundException; no fallback data",
        "sourceRevision": "SYNTHETIC_NON_ORIGINAL_COMPONENT_VALUES",
        "sourcePath": "synthetic-component-values/not-a-WAN",
        "originalHashes": None, "verifiedOriginalFlags": False,
        "fixtureCodeBytes": last - first, "fixtureCodeSha256": hashlib.sha256(program[first:last]).hexdigest(),
        "completeFixtureAndInitializerIdentity": identity(SUITE / "Program.cs"),
        "semanticInventoryUnchanged": identity(PREP / "expected-inventory.json"),
    })
    shared = {
        "atUtc": now(), "cwd": str(ROOT), "initialEnvironment": {},
        "effectiveEnvironmentBeforeWork": isolated_environment(PREP),
        "outerLauncher": "builtin cd followed by exec -c exact pinned Python; module import activates the recorded local environment",
        "authority": authorities(), "sourceCatalogue": identity(PREP / "production77-authored.json"),
        "harnessCatalogue": identity(PREP / "harness-authored.json"),
        "actualGuardDependency": identity(ROOT / GUARD),
        "references": identity(PREP / "reference-inputs.json"), "tools": identity(PREP / "tool-inputs.json"),
        "expectedInventory": identity(PREP / "expected-inventory.json"), "fixture": fixture,
        "window": identity(PREP / "window.json"),
        "priorFailure": identity(OUT / "attempt-01/outer-failure.json"),
    }
    record(PREP / "0003-unit-prelaunch.json", dict(shared,
        argv=[PYTHON, "-I", "-S", "-B", str(SUITE / "test_runner.py"), "-q"], timeoutSeconds=180,
        cumulativeReserved={"csc": 1, "managed": 1, "units": 2, "drivers": 1, "metadata": 7, "corrections": 1}))
    record(PREP / "0004-driver-prelaunch.json", dict(shared,
        argv=[PYTHON, "-I", "-S", "-B", str(SUITE / "run.py"), "--admission", str(ADMISSION),
              "--continuation-admission", str(META / "fixture-continuation-admission.json"), "--controls"],
        timeoutSeconds=1200,
        cumulativeReserved={"csc": 1, "managed": 1, "units": 2, "drivers": 2, "metadata": 7, "corrections": 1}))
    record(PREP / "freeze-completed.json", {
        "atUtc": now(), "pid": os.getpid(), "exit": 0, "entry": entry,
        "sources": 77, "cases": 35, "fields": 558, "controls": 9, "fixture": fixture,
        "compilerOrManagedWork": 0, "productionChanges": 0,
    })
    print("Continuation correction1 frozen: unchanged77 production,35/558 goldens, strict synthetic14-actor fixture, versioned unit/driver reservations.")


def qualify_control(exit_code, observed, control, expected):
    require(exit_code == 2 and observed["status"] == "assertion-failed"
            and observed["failedAssertion"] == control[5] and observed["selectedCase"] == control[4],
            "Control requires exact normal semantic assertion: " + control[0])
    fields = observed["fields"]
    wanted = [row for row in expected["fields"] if row["case"] == control[4]]
    require(fields and len(fields) <= len(wanted) and observed["assertions"] == len(fields)
            and observed["cases"] == [], "Selected control must fail normally inside its one declared case")
    for index, row in enumerate(fields):
        declared = wanted[index]
        require(all(row[key] == declared[key] for key in ("case", "field", "expected")),
                "Control expectations or field order changed")
        require((row["actual"] == row["expected"]) == (index != len(fields) - 1),
                "Only the named final field may fail")
    require(fields[-1]["field"] == control[5], "Unrelated assertion is not a kill")


class Campaign:
    def __init__(self):
        self.execution = read_json(ADMISSION)
        self.tools = read_json(PREP / "tool-inputs.json")
        self.refs = read_json(PREP / "reference-inputs.json")
        self.expected = read_json(PREP / "expected-inventory.json")
        self.start = time.monotonic()
        old = read_json(OUT / "attempt-01/outer-failure.json")
        require(old["counts"] == {"compilers": 1, "managed": 1, "ns21Attempts": 0},
                "Exact prior consumed counts must carry forward")
        self.counts = dict(old["counts"])
        self.jobs = []
        self.run_root = OUT / "attempt-02"
        self.run_root.mkdir(parents=True, exist_ok=False)
        self.env = isolated_environment(self.run_root)
        self.shared = {
            "sourceCatalogue": identity(PREP / "production77-authored.json"),
            "harnessCatalogue": identity(PREP / "harness-authored.json"),
            "expectedInventory": identity(PREP / "expected-inventory.json"),
            "toolManifest": identity(PREP / "tool-inputs.json"),
            "referenceManifest": identity(PREP / "reference-inputs.json"),
            "fixture": identity(PREP / "fixture-declaration.json"),
            "continuationAuthority": identity(META / "fixture-continuation-admission.json"),
            "window": identity(PREP / "window.json"),
            "actualGuardDependency": identity(ROOT / GUARD),
            "priorFailure": identity(OUT / "attempt-01/outer-failure.json"),
        }
        self.entry = record(self.run_root / "entry.json", {
            "atUtc": now(), "pid": os.getpid(), "parentPid": os.getppid(), "argv": sys.argv,
            "cwd": str(ROOT), "entryEnvironment": dict(os.environ), "childEnvironment": self.env,
            "prelaunch": identity(PREP / "0004-driver-prelaunch.json"), "identities": self.shared,
            "reservedCounts": {"driverStarts": 2, "units": 2, "metadataInspections": 7, "corrections": 1},
        })

    def launch(self, directory, kind, argv, expectation, binary=None):
        require(time.monotonic() - self.start < 1200, "Focused driver wall limit")
        window = read_json(PREP / "window.json")
        clock = datetime.datetime.now(datetime.timezone.utc)
        require(datetime.datetime.fromisoformat(window["startUtc"]) <= clock
                < datetime.datetime.fromisoformat(window["endUtc"]), "Explicit prospective continuation window")
        verify_readonly_production()
        for name, row in self.tools.items():
            check_pin(row["path"], row["bytes"], row["sha256"])
        key, ceiling, timeout = ("compilers", 16, 180) if kind == "compiler" else ("managed", 14, 90)
        require(self.counts[key] < ceiling, "Invocation ceiling reached")
        self.counts[key] += 1
        job = directory / (kind + "-" + str(self.counts[key]).zfill(2))
        job.mkdir(parents=True, exist_ok=False)
        source_manifest = identity(directory / "source-capture.json")
        for row in read_json(directory / "source-capture.json"):
            check_pin(row["path"], row["bytes"], row["sha256"])
        prelaunch = record(job / "prelaunch.json", {
            "atUtc": now(), "parentPid": os.getpid(), "argv": argv, "cwd": str(ROOT),
            "environment": self.env, "timeoutSeconds": timeout, "countsConsumedIncludingReservation": dict(self.counts),
            "remaining": {"compilers": 16 - self.counts["compilers"], "managed": 14 - self.counts["managed"]},
            "identities": self.shared, "sourceCapture": source_manifest, "inputBinary": binary,
            "programInput": identity(directory / "Program.cs"),
            "literalExpectation": expectation,
        })
        start = now()
        code, pid, timed_out = None, None, False
        with (job / "stdout.txt").open("xb") as stdout, (job / "stderr.txt").open("xb") as stderr:
            process = subprocess.Popen(argv, cwd=ROOT, env=self.env, stdout=stdout, stderr=stderr)
            pid = process.pid
            record(job / "started.json", {"atUtc": now(), "pid": pid, "parentPid": os.getpid(), "prelaunch": prelaunch})
            try:
                code = process.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                timed_out = True
                process.kill()
                code = process.wait()
        result = {"kind": kind, "pid": pid, "parentPid": os.getpid(), "startedAtUtc": start,
                  "completedAtUtc": now(), "exit": code, "timedOut": timed_out, "prelaunch": prelaunch,
                  "stdout": identity(job / "stdout.txt"), "stderr": identity(job / "stderr.txt"),
                  "countsConsumed": dict(self.counts)}
        record(job / "completed.json", result)
        self.jobs.append(identity(job / "completed.json"))
        require(not timed_out, "Timed-out workload is not a semantic result")
        return result, data(job / "stdout.txt"), data(job / "stderr.txt")

    def capture(self, name, control=None):
        directory = self.run_root / name
        directory.mkdir()
        manifest, files = [], []
        for row in read_json(PREP / "production77-authored.json"):
            rel = row["relativePath"]
            raw = data(PREP / "authored/production" / rel)
            if control and rel == control[1]:
                before, after = control[2].encode(), control[3].encode()
                require(raw.count(before) == 1, "Unique actual-source mutation anchor: " + control[0])
                changed = raw.replace(before, after)
                record(directory / "mutation.json", {
                    "id": control[0], "actualProductionPath": rel, "before": control[2], "after": control[3],
                    "beforeSha256": hashlib.sha256(raw).hexdigest(), "afterSha256": hashlib.sha256(changed).hexdigest(),
                    "testExpectationsChanged": False,
                })
                raw = changed
            target = directory / "sources" / rel
            exclusive(target, raw)
            files.append(target)
            manifest.append(dict(identity(target), relativePath=rel))
        record(directory / "source-capture.json", manifest)
        exclusive(directory / "Program.cs", data(PREP / "authored/harness/Program.cs"))
        exclusive(directory / "component.runtimeconfig.json", (
            '{\n  "runtimeOptions": {\n    "tfm": "net10.0",\n'
            '    "framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"},\n'
            '    "rollForward": "Disable"\n  }\n}\n').encode())
        return directory, files

    def compile(self, directory, files, ns21=False):
        profile = "netstandard2.1" if ns21 else "net10.0"
        refs = self.refs["profiles"][profile]["files"]
        for row in refs:
            check_pin(row["path"], row["bytes"], row["sha256"])
        if ns21:
            self.counts["ns21Attempts"] += 1
            require(self.counts["ns21Attempts"] <= 2, "NS2.1 attempt ceiling")
        target = directory / ("compatibility.dll" if ns21 else "component.dll")
        argv = [self.tools["dotnet"]["path"], "exec", self.tools["compiler"]["path"],
                "/noconfig", "/nostdlib+", "/langversion:7.3", "/deterministic+", "/warnaserror+",
                "/target:library" if ns21 else "/target:exe", "/out:" + str(target)]
        argv.extend("/reference:" + row["path"] for row in refs)
        argv.extend(str(path) for path in files)
        if not ns21:
            argv.append(str(directory / "Program.cs"))
        result, stdout, stderr = self.launch(directory, "compiler", argv,
            {"exit": 0, "actualProductionSources": 77, "references": len(refs), "profile": profile,
             "programIncluded": not ns21, "warningsAsErrors": True})
        require(result["exit"] == 0, "Compilation failed, never a control kill: " + str(directory)
                + "\n" + (stdout + stderr).decode(errors="replace")[-16000:])
        binary = identity(target)
        record(directory / "binary.json", {"binary": binary, "compilerReceipt": self.jobs[-1],
                                           "runtimeConfig": identity(directory / "component.runtimeconfig.json"),
                                           "profile": profile, "executed": False if ns21 else "Separate managed receipt required"})
        return binary

    def execute(self, directory, binary, control=None):
        selected = control[4] if control else None
        argv = [self.tools["dotnet"]["path"], "exec", binary["path"]]
        if selected:
            argv.extend(("--case", selected))
        expectation = {"exit": 2 if control else 0,
                       "case": selected, "requiredFailure": control[5] if control else None,
                       "inventory": self.shared["expectedInventory"], "normalNamedAssertionOnly": bool(control)}
        result, stdout, stderr = self.launch(directory, "managed", argv, expectation, binary)
        require(not stderr, "Unexpected managed diagnostics, not a control kill")
        observed = json.loads(stdout)
        if control:
            qualify_control(result["exit"], observed, control, self.expected)
        else:
            fields = [{"case": r["case"], "field": r["field"], "expected": r["expected"]}
                      for r in self.expected["fields"]]
            actual = [{"case": r["case"], "field": r["field"], "expected": r["expected"]}
                      for r in observed["fields"]]
            require(result["exit"] == 0 and observed["status"] == "passed"
                    and observed["cases"] == self.expected["cases"]
                    and observed["assertions"] == 558 and actual == fields
                    and all(r["actual"] == r["expected"] for r in observed["fields"]),
                    "Clean baseline must match every frozen35-case/558-field literal")
        fixture = observed.get("fixture")
        require(fixture is not None and fixture["randomRequests"] == 0
                and fixture["originalProvenanceSupplied"] is False, "Strict synthetic fixture diagnostics required")
        if not control or selected == "completion.lifecycle":
            require(fixture["actorsInitialized"] == 14 and fixture["layersInitialized"] == 28
                    and fixture["initializationStepsPerLayer"] == 1,
                    "Actual snapshot requires all14 genuine actors with finite specified layers")
        record(directory / "semantic-result.json", {
            "status": "qualified-component-control" if control else "passed-component-baseline",
            "selectedCase": selected, "failure": observed.get("failedAssertion"),
            "managedReceipt": self.jobs[-1], "assertions": observed["assertions"], "observations": observed,
            "realGameSimulationExecuted": False,
            "syntheticFixtureDiagnostics": fixture,
        })
        return identity(directory / "semantic-result.json")

    def run(self):
        authorities()
        verify_frozen_authored()
        require(read_json(PREP / "unit-result.json")["exit"] == 0, "Focused unit command must pass first")
        baseline_dir, files = self.capture("baseline")
        baseline = self.execute(baseline_dir, self.compile(baseline_dir, files))
        controls = []
        for index, control in enumerate(CONTROLS, 1):
            directory, files = self.capture("control-" + str(index).zfill(2), control)
            controls.append(self.execute(directory, self.compile(directory, files), control))
        final_dir, files = self.capture("final")
        final = self.execute(final_dir, self.compile(final_dir, files))
        ns_dir, files = self.capture("ns21-compatibility-only")
        ns = self.compile(ns_dir, files, ns21=True)
        verify_frozen_authored()
        static = static_preservation()
        total = sum(p.stat().st_size for p in META.rglob("*") if p.is_file())
        require(total <= 536870912, "New output ceiling")
        result = {
            "status": "PASSED_BOUNDED_COMPONENT_AND_COMPATIBILITY_CAMPAIGN",
            "started": self.entry, "completedAtUtc": now(), "pid": os.getpid(), "exit": 0,
            "counts": self.counts, "baseline": baseline, "controls": controls, "final": final,
            "ns21CompatibilityOnly": ns, "staticPreservation": static, "jobs": self.jobs,
            "expectedInventory": self.shared["expectedInventory"], "newOutputBytes": total,
            "scope": "Synthetic INTERNAL MODEL components and static caller; public GameSimulation/C2commit/Step/Restart/natural match UNRUN.",
        }
        record(self.run_root / "result.json", result)
        print(json.dumps({"status": result["status"], "casesEach": 35, "fieldsEach": 558,
                          "controls": 9, "counts": self.counts, "exit": 0}))
        return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--capture-before", action="store_true")
    parser.add_argument("--prepare-campaign", action="store_true")
    parser.add_argument("--prepare-continuation", action="store_true")
    parser.add_argument("--continuation-admission", type=Path)
    parser.add_argument("--admission", type=Path)
    parser.add_argument("--controls", action="store_true")
    args = parser.parse_args()
    signal.alarm(180 if args.prepare_continuation else 1200)
    require(not args.capture_before and not args.prepare_campaign, "Original controller entrypoints remain historical")
    if args.prepare_continuation:
        prepare_continuation()
    else:
        require(args.admission == ADMISSION and args.controls, "Exact new admission and complete controls required")
        require(args.continuation_admission == META / "fixture-continuation-admission.json",
                "Explicit fixture-continuation authority required")
        campaign = Campaign()
        try:
            campaign.run()
        except Exception as error:
            record(campaign.run_root / "outer-failure.json", {
                "atUtc": now(), "pid": os.getpid(), "exit": 1, "error": str(error),
                "counts": campaign.counts, "jobs": campaign.jobs, "automaticRetry": False,
            })
            print(str(error), file=sys.stderr)
            return 1
    return 0


os.environ.clear()
os.environ.update(isolated_environment(PREP))

if __name__ == "__main__":
    raise SystemExit(main())
