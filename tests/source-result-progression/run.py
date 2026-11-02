#!/usr/bin/python3.12
"""One admitted, fail-closed result-progression component campaign."""
import argparse
import ast
import hashlib
import json
import os
from pathlib import Path
import re
import selectors
import signal
import subprocess
import sys
import time
from datetime import datetime, timezone

ROOT = Path("/data/source/nflblitz/.squad/.scratch/source-next-default-audibles-45f")
B = ROOT / ".squad/.scratch/source-following-active-p1"
OUT = B / "metadata/source-result-progression"
PREP = OUT / "preparation"
VALID = OUT / "validation"
PROC = OUT / "process-state"
PYTHON = "/usr/bin/python3.12"
DOTNET = "/home/long2know/.dotnet/dotnet"
CSC = "/home/long2know/.dotnet/sdk/10.0.100/Roslyn/bincore/csc.dll"
CONTRACT_PIN = (51640, "b4a998d4fafec3fd04b0e24c3ea0b1e2d7cfd9941a0307c3d633d186b2d83f36")
IMPLEMENTATION_PIN = (8550, "ef7fb6d9e5ce42b02eda99ec8638afb285af7ac2b4fe251f941d40e35e56c5a9")
EXECUTION_PIN = (10766, "8b196ec1422ace537022f537ec33aec00c4b131760fc319216e63cffe2b91476")
DRIVER_REL = "tests/source-result-progression/run.py"
HARNESS_REL = "tests/source-result-progression/Program.cs"
FIXTURE_REL = "tests/source-offensive-stats/Program.cs"
UNIT_REL = "tests/source-result-progression/test_runner.py"
README_REL = "tests/source-result-progression/README.md"
LIMITS = {"metadata": 3, "unit": 1, "driver": 1, "compiler": 21, "managed": 20}
MAX_BYTES = 67108864
CHILD_BYTES = 4194304
ARRAYS = ("        internal readonly int[] SourceFirstDowns = new int[2];\n"
          "        internal readonly int[] SourceFourthDownConversions = new int[2];\n")
ENV_KEYS = frozenset((
    "HOME", "TMP", "TEMP", "TMPDIR", "DOTNET_CLI_HOME", "NUGET_PACKAGES",
    "NUGET_HTTP_CACHE_PATH", "DOTNET_BUNDLE_EXTRACT_BASE_DIR", "PATH", "DOTNET_ROOT",
    "SHLVL", "LANG", "LC_ALL", "TZ", "DOTNET_CLI_TELEMETRY_OPTOUT",
    "DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "DOTNET_NOLOGO", "DOTNET_MULTILEVEL_LOOKUP",
    "DOTNET_EnableDiagnostics", "COMPlus_EnableDiagnostics", "CORECLR_ENABLE_PROFILING",
    "COR_ENABLE_PROFILING", "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE",
    "MSBUILDDISABLENODEREUSE", "DOTNET_CLI_USE_MSBUILD_SERVER",
    "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", "DOTNET_STARTUP_HOOKS", "PYTHONHASHSEED"
))
PHASE = None
ENTRY = None
DEADLINE = None


class ProtocolFailure(RuntimeError):
    def __init__(self, field, details=None):
        super().__init__(field)
        self.field = field
        self.details = details


def require(condition, field, details=None):
    if not condition:
        raise ProtocolFailure(field, details)


def utc():
    return datetime.now(timezone.utc).isoformat()


def digest(data):
    return hashlib.sha256(data).hexdigest()


def identity(path):
    path = Path(path)
    require(path.is_file() and not path.is_symlink() and path.absolute() == path.resolve(),
            "input.regular-unique-path", {"path": str(path)})
    data = path.read_bytes()
    return {"path": str(path), "bytes": len(data), "sha256": digest(data)}


def pinned(path, size, sha):
    row = identity(path)
    require((row["bytes"], row["sha256"]) == (size, sha), "input.pin", {"path": str(path)})
    return Path(path).read_bytes()


def unique_json(data):
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, "json.duplicate-key", {"key": key})
            result[key] = value
        return result
    return json.loads(data, object_pairs_hook=pairs)


def read_json(path):
    return unique_json(Path(path).read_bytes())


def artifact_bytes():
    total = 0
    for directory in (PREP, VALID, PROC):
        if directory.exists():
            for parent, dirs, files in os.walk(directory, followlinks=False):
                require(not any((Path(parent) / name).is_symlink() for name in dirs + files),
                        "output.no-symlinks")
                total += sum((Path(parent) / name).stat().st_size for name in files)
    handoff = OUT / "source-write-handoff.json"
    if handoff.exists():
        total += handoff.stat().st_size
    return total


def write_bytes(path, data):
    path = Path(path)
    require(path.is_relative_to(OUT), "output.only-admitted-root")
    require(artifact_bytes() + len(data) <= MAX_BYTES, "output.cumulative-cap")
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("xb") as stream:
        stream.write(data)
    return identity(path)


def write_json(path, value):
    return write_bytes(path, (json.dumps(value, indent=2, sort_keys=True) + "\n").encode())


def environment():
    value = read_json(PREP / "environment.json")
    require(set(value) == ENV_KEYS and all(isinstance(v, str) for v in value.values()),
            "environment.authoritative-keyset")
    return value


def safe_environment_key_names(names):
    return sorted(name if re.fullmatch(r"[A-Za-z_][A-Za-z_0-9]{0,127}", name)
                  else "KEY_NAME_REDACTED" for name in names)


def check_environment(actual, expected):
    names = set(actual)
    unknown = names - set(expected) - {"_"}
    if unknown:
        raise ProtocolFailure("environment.unknown-keys",
                              {"keys": safe_environment_key_names(unknown),
                               "value": "VALUE_NOT_PERSISTED", "accepted": False})
    missing = set(expected) - names
    require(not missing, "environment.missing-keys", {"keys": safe_environment_key_names(missing)})
    if "_" in names and actual["_"] != PYTHON:
        raise ProtocolFailure("environment.underscore",
                              {"value": "VALUE_NOT_PERSISTED", "accepted": False})
    mismatches = [key for key in expected if actual[key] != expected[key]]
    if mismatches:
        raise ProtocolFailure("environment.value-mismatch",
                              {"keys": safe_environment_key_names(mismatches),
                               "value": "VALUE_NOT_PERSISTED", "accepted": False})
    return {"expectedKeys": safe_environment_key_names(expected),
            "actualKeys": safe_environment_key_names(names), "exact": True,
            "underscorePresent": "_" in names}


def authority():
    contract = unique_json(pinned(OUT / "contract.json", *CONTRACT_PIN))
    implementation = unique_json(pinned(OUT / "implementation-admission.json", *IMPLEMENTATION_PIN))
    execution = unique_json(pinned(OUT / "execution-admission.json", *EXECUTION_PIN))
    require(implementation["approved"] is True and execution["approved"] is True, "authority.approved")
    require(all(item["worktree"] == str(ROOT) and item["baseB"] == str(B)
                for item in (contract, implementation, execution)), "authority.exact-roots")
    expected = environment()
    for key, suffix in execution["environmentOverride"]["allInsideThisOutcome"].items():
        require(expected[key] == str(OUT / suffix), "environment.outcome-local-" + key)
    require(expected["PATH"] == "/usr/bin:/bin" and expected["SHLVL"] == "1"
            and expected["DOTNET_ROOT"] == str(Path(DOTNET).parent), "environment.fixed-tools")
    for key, value in expected.items():
        if key in ("HOME", "TMP", "TEMP", "TMPDIR", "DOTNET_CLI_HOME", "NUGET_PACKAGES",
                   "NUGET_HTTP_CACHE_PATH", "DOTNET_BUNDLE_EXTRACT_BASE_DIR"):
            Path(value).mkdir(parents=True, exist_ok=True)
    check_environment(os.environ, expected)
    return contract, implementation, execution


def time_left():
    require(DEADLINE is not None and time.monotonic() < DEADLINE, "phase.2400-second-cap")
    return DEADLINE - time.monotonic()


def reserve(name, kind, argv, details=None):
    time_left()
    records = [read_json(path) for path in PROC.glob("*.reservation.json")
               if not path.name.startswith("outer-")]
    require(sum(row["kind"] == kind for row in records) < LIMITS[kind], "reservation." + kind + "-cap")
    row = {"id": name, "kind": kind, "reservedActualUtc": utc(), "pid": os.getpid(),
           "parentPid": os.getppid(), "argv": list(argv), "cwd": str(ROOT),
           "environment": environment(), "phaseDeadlineMonotonic": DEADLINE,
           "authorityPins": {"contract": CONTRACT_PIN, "implementation": IMPLEMENTATION_PIN,
                             "execution": EXECUTION_PIN}, "details": details}
    return write_json(PROC / (name + ".reservation.json"), row)


def enter(name, kind):
    global PHASE, ENTRY, DEADLINE
    started = utc()
    PHASE = name
    PROC.mkdir(parents=True, exist_ok=True)
    PREP.mkdir(parents=True, exist_ok=True)
    VALID.mkdir(parents=True, exist_ok=True)
    outer = read_json(PROC / "outer-M1.reservation.json")
    first_epoch = float(outer["utcEpochSeconds"])
    elapsed = datetime.now(timezone.utc).timestamp() - first_epoch
    require(0 <= elapsed < 2400, "phase.first-UTC-reservation")
    DEADLINE = time.monotonic() + 2400 - elapsed
    process_limit = 1800 if name == "D1" else 120
    DEADLINE = min(DEADLINE, time.monotonic() + process_limit)
    ENTRY = reserve(name, kind, [PYTHON, "-I", "-S", "-B"] + sys.argv,
                    {"entryActualUtcBeforeChecks": started, "outerReservation": identity(
                        PROC / ("outer-" + name + ".reservation.json")),
                     "safeActualEnvironmentKeyNames": safe_environment_key_names(os.environ)})
    def expired(signum, frame):
        raise ProtocolFailure("phase.process-timeout")
    signal.signal(signal.SIGALRM, expired)
    signal.setitimer(signal.ITIMER_REAL, time_left())
    context = authority()
    write_json(PROC / (name + ".entry.json"),
               {"actualUtc": started, "pid": os.getpid(), "parentPid": os.getppid(),
                "reservation": ENTRY, "environmentCheck": check_environment(os.environ, environment())})
    return context


def method_bytes(data, name):
    match = re.search(rb"^        internal void " + name.encode() + rb"\([\s\S]*?^        \}\n", data, re.M)
    require(match is not None, "preservation.method-" + name)
    return match.group()


def inverse_production(contract):
    state_path, contracts_path, snapshot_path = [
        ROOT / row["path"] for row in contract["requestedExactFileReleaseNotGranted"]]
    state = state_path.read_bytes()
    tail = contract["proposedProducerTailExact"].encode()
    require(state.count(ARRAYS.encode()) == state.count(tail) == 1, "inverse.state.exact-additions")
    restored_state = state.replace(ARRAYS.encode(), b"").replace(tail, b"")
    text = contracts_path.read_bytes()
    dto = re.search(rb"^    public sealed class SourceResultProgressionStatsView\n[\s\S]*?^    \}\n\n", text, re.M)
    require(dto is not None, "inverse.contracts.new-dto")
    property_line = b"        public SourceResultProgressionStatsView ResultProgressionStats { get; }\n"
    ctor = re.search(rb"^        public SourceMatchView\(SourceResultProgressionStatsView[\s\S]*?^        \}\n\n",
                     text, re.M)
    require(ctor is not None and text.count(property_line) == 1, "inverse.contracts.new-property-constructor")
    restored_contracts = text.replace(dto.group(), b"").replace(property_line, b"").replace(ctor.group(), b"")
    prefix = (
        "                Match = new SourceMatchView(new SourceResultProgressionStatsView(\n"
        "                    model.SourceFirstDowns[(int)TeamId.Home],\n"
        "                    model.SourceFirstDowns[(int)TeamId.Away],\n"
        "                    model.SourceFourthDownConversions[(int)TeamId.Home],\n"
        "                    model.SourceFourthDownConversions[(int)TeamId.Away]),\n"
        "                    new SourcePlaceKickStatsView(\n").encode()
    snap = snapshot_path.read_bytes()
    require(snap.count(prefix) == 1, "inverse.snapshot.only-leading-operands")
    restored_snapshot = snap.replace(prefix, b"                Match = new SourceMatchView(new SourcePlaceKickStatsView(\n")
    results = []
    for row, data in zip(contract["requestedExactFileReleaseNotGranted"],
                         (restored_state, restored_contracts, restored_snapshot)):
        require((len(data), digest(data)) == (row["bytes"], row["sha256"]), "inverse.exact-" + row["path"])
        results.append({"path": row["path"], "restoredBytes": len(data), "restoredSha256": digest(data)})
    for key, name, data in (
            ("completionMethod", "AccumulateSourcePassCompletion", state),
            ("returnCaptureMethod", "CaptureSourceKickReturnCatch", state),
            ("stateCommonMethodBeforeAppend", "AccumulateSourcePlayEndStats", restored_state)):
        body = method_bytes(data, name)
        pin = contract["preservedBlocks"][key]
        require((len(body), digest(body)) == (pin["bytesWithTrailingLf"], pin["sha256"]),
                "preservation." + key)
    blocks = re.findall(rb"^        public SourceMatchView\([\s\S]*?^        \}", text, re.M)
    require(len(blocks) == 8, "preservation.eight-constructor-count")
    for block, pin in zip(blocks[:7], contract["preservedBlocks"]["sevenConstructorsNoTrailingLf"]):
        require((len(block), digest(block)) == (pin["bytes"], pin["sha256"]),
                "preservation.constructor-" + pin["firstType"])
    delegate = re.search(rb": this\(([\s\S]*?)\)\n        \{", blocks[7])
    require(delegate is not None, "preservation.eighth-delegation")
    names = re.sub(rb"\s+", b"", delegate.group(1)).decode().split(",")
    require(names == contract["publicApiPlan"]["exactDelegated36Names"], "preservation.exact36-forwarding")
    return results


def load_guard(contract):
    reuse = contract["unchangedLiveDependenciesAndCallerProof"]["pureStaticGuardReuse"]
    allowed = set(reuse["functions"] + reuse["definitions"])
    path = ROOT / "tests/contracts/run.py"
    tree = ast.parse(path.read_bytes(), filename=str(path))
    selected = []
    found = set()
    for node in tree.body:
        names = []
        if isinstance(node, (ast.FunctionDef, ast.ClassDef)):
            names = [node.name]
        elif isinstance(node, ast.Assign):
            names = [target.id for target in node.targets if isinstance(target, ast.Name)]
        if names and set(names) <= allowed:
            selected.append(node)
            found.update(names)
    require(found == allowed, "caller.pure-allowlist-complete")
    namespace = {"re": re, "hashlib": hashlib}
    exec(compile(ast.Module(body=selected, type_ignores=[]), str(path), "exec"), namespace)
    return namespace


def caller_proof(contract):
    for row in contract["unchangedLiveDependenciesAndCallerProof"]["livePins"]:
        pinned(ROOT / row["path"], row["bytes"], row["sha256"])
    guard = load_guard(contract)
    flow = (ROOT / "Assets/Scripts/Gameplay/SourceMatchFlow.cs").read_bytes()
    caller = (ROOT / "Assets/Scripts/Gameplay/GameSimulation.Source.cs").read_bytes()
    result = guard["require_caller_wiring"](flow, caller)
    require(result["status"] == "passed-static-only", "caller.static-result")
    return result


def literal_inventory(contract):
    plan = contract["literalPlanFrozenBeforeCompile"]
    cases = []
    base = {}
    for row in plan["tableCases"]:
        require(len(row["input"]) == 16 and len(row["expected"]) == 22, "inventory.table-shape")
        item = {"id": row["id"], "fields": list(plan["fieldOrder"]), "expected": list(row["expected"])}
        base[item["id"]] = item
        cases.append(item)
    for row in plan["specialCases"]:
        if row["id"] == "pending.saved-input":
            basis = base[row["baseExpectedCase"]]
            names = basis["fields"] + row["additionalFields"]
            values = basis["expected"] + row["additionalExpected"]
        else:
            names, values = row["fields"], row["expected"]
        cases.append({"id": row["id"], "fields": list(names), "expected": list(values)})
    require(len(cases) == 38 and len({r["id"] for r in cases}) == 38
            and sum(len(r["fields"]) for r in cases) == 854, "inventory.38-cases-854-fields")
    require(all(len(r["fields"]) == len(r["expected"]) and len(set(r["fields"])) == len(r["fields"])
                for r in cases), "inventory.unique-fields")
    return cases


def control_expectations(control, inventory):
    case = next(row for row in inventory if row["id"] == control["case"])
    index = case["fields"].index(control["field"])
    require(case["expected"][index] == control["expected"]
            and case["expected"][:index] == control["expectedPrefix"]
            and control["actual"] != control["expected"], "control.frozen-prefix-" + control["id"])
    return case, index


def validate_report(report, inventory, returncode, control=None):
    require(isinstance(report, dict) and isinstance(report.get("fields"), list)
            and isinstance(report.get("cases"), list), "report.complete-shape")
    if control is None:
        require(returncode == 0 and report.get("status") == "passed", "report.clean-normal-exit")
        require(report["cases"] == [row["id"] for row in inventory], "report.clean-case-order")
        expected_rows = [(row["id"], field, value, value) for row in inventory
                         for field, value in zip(row["fields"], row["expected"])]
    else:
        case, index = control_expectations(control, inventory)
        require(returncode == 2 and report.get("status") == "assertion-failed"
                and report["cases"] == [], "report.control-normal-assertion-exit")
        expected_rows = [(case["id"], case["fields"][i], case["expected"][i], case["expected"][i])
                         for i in range(index)]
        expected_rows.append((case["id"], control["field"], control["expected"], control["actual"]))
        require(report.get("failure") == dict(zip(("case", "field", "expected", "actual"),
                                                expected_rows[-1])), "report.control-first-literal")
    actual_rows = []
    for row in report["fields"]:
        require(isinstance(row, dict) and set(row) == {"case", "field", "expected", "actual"},
                "report.field-shape")
        require(type(row["expected"]) in (int, float) and type(row["actual"]) in (int, float),
                "report.field-numbers")
        actual_rows.append((row["case"], row["field"], row["expected"], row["actual"]))
    require(actual_rows == expected_rows, "report.exact-ordered-literals")
    return {"status": "verified", "cases": len(report["cases"]), "fields": len(actual_rows),
            "selectedCase": None if control is None else control["case"],
            "firstFailure": None if control is None else report["failure"]}


def stat_record(path):
    value = Path(path).stat()
    return {"bytes": value.st_size, "device": value.st_dev, "inode": value.st_ino,
            "mode": value.st_mode, "mtimeNs": value.st_mtime_ns, "ctimeNs": value.st_ctime_ns}


def prepare(contract, implementation, execution):
    inverses = inverse_production(contract)
    caller = caller_proof(contract)
    accepted = contract["acceptanceReadFirst"]
    pinned(B / "metadata/source-place-kick-stats/parent-scoped-acceptance.json",
           accepted["bytes"], accepted["sha256"])
    catalogue_pin = accepted["currentFrozenCatalogue"]
    catalogue = unique_json(pinned(B / "metadata/source-place-kick-stats/preparation/continuation-03/current44-catalogue.json",
                                  catalogue_pin["bytes"], catalogue_pin["sha256"]))
    released = {row["path"] for row in implementation["threePreciseFrozenReleases"]}
    inherited = []
    for row in catalogue["files"]:
        if row["path"] not in released:
            path = ROOT / row["path"]
            require(path.is_file() and not path.is_symlink() and path.stat().st_size == row["bytes"],
                    "catalogue.inherited-metadata")
            inherited.append({"authority": row, "stat": stat_record(path)})
    require(len(inherited) == 41, "catalogue.41-inherited")
    write_json(PREP / "inherited41-custody.json", inherited)
    write_json(PREP / "three-inverses.json", inverses)
    write_json(PREP / "caller-proof.json", caller)
    closure = execution["sourceClosure"]
    before = unique_json(pinned(B / closure["beforeCatalogueRelativeToB"],
                               closure["beforeCatalogueBytes"], closure["beforeCatalogueSha256"]))
    vector = [row["relativePath"] for row in before]
    require(len(vector) == len(set(vector)) == 77
            and digest(json.dumps(vector, separators=(",", ":")).encode()) ==
            "0af699ae1c3420fde4ee50412efbb7b0fbe195edc8af49a0583448d7b500c470", "capture.exact77-vector")
    sources = []
    for row in before:
        live = ROOT / row["relativePath"]
        pin = identity(live)
        if row["relativePath"] not in released:
            require((pin["bytes"], pin["sha256"]) == (row["bytes"], row["sha256"]),
                    "capture.74-unchanged")
        captured = PREP / "captured-production" / row["relativePath"]
        new_pin = write_bytes(captured, live.read_bytes())
        sources.append(dict(new_pin, relativePath=row["relativePath"], livePath=str(live)))
    require(len({r["path"] for r in sources}) == 77, "capture.unique-full-paths")
    fixture_pin = contract["fixtureProspectiveExceptionRequested"]
    pinned(ROOT / FIXTURE_REL, fixture_pin["bytes"], fixture_pin["sha256"])
    harnesses = [
        dict(write_bytes(PREP / "captured-harness/Program.cs", (ROOT / HARNESS_REL).read_bytes()),
             relativePath=HARNESS_REL, livePath=str(ROOT / HARNESS_REL)),
        dict(identity(ROOT / FIXTURE_REL), relativePath=FIXTURE_REL, livePath=str(ROOT / FIXTURE_REL),
             copied=False)
    ]
    controllers = [identity(ROOT / rel) for rel in (DRIVER_REL, UNIT_REL, README_REL)]
    captured_contract = write_bytes(PREP / "captured-contract.json", (OUT / "contract.json").read_bytes())
    inventory = literal_inventory(contract)
    inventory_pin = write_json(PREP / "literal-inventory.json", inventory)
    controls = []
    for item in contract["freshSemanticControlsFrozen"]["controls"]:
        control_expectations(item, inventory)
        source = next(row for row in sources if Path(row["relativePath"]).name == item["target"])
        data = Path(source["path"]).read_bytes()
        anchor = item["anchor"].encode()
        require(data.count(anchor) == 1, "control.unique-real-anchor-" + item["id"])
        replacement = item["replacement"].encode()
        mutant = data.replace(anchor, replacement)
        pin = write_bytes(PREP / "mutants" / item["id"] / source["relativePath"], mutant)
        controls.append(dict(item, source=source, mutated=pin, byteOffset=data.index(anchor)))
    require(len(controls) == 18 and len({r["id"] for r in controls}) == 18, "control.exact18")
    control_pin = write_json(PREP / "mutation-inventory.json", controls)
    frozen = {"frozenActualUtc": utc(), "sources": sources, "harnesses": harnesses,
              "controllers": controllers, "contract": captured_contract, "inventory": inventory_pin,
              "controls": control_pin, "environment": identity(PREP / "environment.json"),
              "authorities": [identity(OUT / name) for name in
                              ("contract.json", "implementation-admission.json", "execution-admission.json")]}
    write_json(PREP / "frozen-before-tool-bodies.json", frozen)
    tool_meta = unique_json(pinned(B / execution["tools"]["toolManifestRelativeToB"],
                                  execution["tools"]["toolManifestBytes"], execution["tools"]["toolManifestSha256"]))
    ref_meta = unique_json(pinned(B / execution["tools"]["referenceManifestRelativeToB"],
                                 execution["tools"]["referenceManifestBytes"],
                                 execution["tools"]["referenceManifestSha256"]))
    tools = []
    for key, fixed in (("python", PYTHON), ("dotnet", DOTNET), ("compiler", CSC)):
        row = tool_meta[key]
        require(row["path"] == fixed, "tool.fixed-" + key)
        pinned(fixed, row["bytes"], row["sha256"])
        tools.append(row)
    launcher = identity("/usr/bin/env")
    require(launcher["sha256"] == execution["tools"]["envLauncherSha256"], "tool.env-launcher")
    tools.append(launcher)
    references = {}
    for profile, count in (("net10.0", 167), ("netstandard2.1", 121)):
        rows = ref_meta["profiles"][profile]["files"]
        require(len(rows) == len({r["path"] for r in rows}) == count, "references.exact-" + profile)
        for row in rows:
            pinned(row["path"], row["bytes"], row["sha256"])
        references[profile] = rows
    write_json(PREP / "verified-tools-references.json", {"tools": tools, "references": references,
                                                       "verifiedAfterFreeze": identity(
                                                           PREP / "frozen-before-tool-bodies.json")})
    write_json(VALID / "M1-result.json", {"status": "prepared", "actualUtc": utc(),
               "frozen": identity(PREP / "frozen-before-tool-bodies.json"), "caseCount": 38,
               "literalFields": 854, "controls": 18, "originalRetrievals": 0,
               "inheritedCustody": "41 pinned authority rows plus unchanged metadata; old campaign bodies unopened"})


def verify_frozen():
    frozen = read_json(PREP / "frozen-before-tool-bodies.json")
    for row in (frozen["sources"] + frozen["harnesses"] + frozen["controllers"] +
                frozen["authorities"] + [frozen["contract"], frozen["inventory"],
                                          frozen["controls"], frozen["environment"]]):
        pinned(row["path"], row["bytes"], row["sha256"])
        if "livePath" in row:
            pinned(row["livePath"], row["bytes"], row["sha256"])
    admitted = read_json(PREP / "verified-tools-references.json")
    for row in admitted["tools"] + admitted["references"]["net10.0"] + admitted["references"]["netstandard2.1"]:
        pinned(row["path"], row["bytes"], row["sha256"])
    return frozen, admitted


def run_child(name, kind, argv, timeout, details):
    frozen, admitted = verify_frozen()
    timeout = min(timeout, time_left())
    require(artifact_bytes() + CHILD_BYTES + 2097152 < MAX_BYTES, "output.child-headroom")
    record = reserve(name, kind, argv, dict(details, timeoutSeconds=timeout,
                     sourcesManifest=identity(PREP / "frozen-before-tool-bodies.json"),
                     toolsReferencesManifest=identity(PREP / "verified-tools-references.json")))
    started = utc()
    child = subprocess.Popen(argv, cwd=str(ROOT), env=environment(), stdin=subprocess.DEVNULL,
                             stdout=subprocess.PIPE, stderr=subprocess.PIPE, shell=False)
    write_json(PROC / (name + ".launched.json"),
               {"actualUtc": utc(), "pid": child.pid, "parentPid": os.getpid(), "reservation": record})
    selector = selectors.DefaultSelector()
    buffers = {"stdout": bytearray(), "stderr": bytearray()}
    for label, stream in (("stdout", child.stdout), ("stderr", child.stderr)):
        os.set_blocking(stream.fileno(), False)
        selector.register(stream, selectors.EVENT_READ, label)
    end = time.monotonic() + timeout
    technical = None
    try:
        while selector.get_map():
            if time.monotonic() >= end:
                technical = "timeout"
                child.kill()
                break
            for key, _ in selector.select(min(0.25, max(0, end - time.monotonic()))):
                data = os.read(key.fileobj.fileno(), 65536)
                if not data:
                    selector.unregister(key.fileobj)
                    key.fileobj.close()
                    continue
                buffers[key.data].extend(data)
                if sum(map(len, buffers.values())) > CHILD_BYTES:
                    technical = "output-cap"
                    child.kill()
                    break
            if technical:
                break
    except Exception as error:
        technical = error.field if isinstance(error, ProtocolFailure) else type(error).__name__
        if child.poll() is None:
            child.kill()
    selector.close()
    if technical:
        for stream in (child.stdout, child.stderr):
            if not stream.closed:
                stream.close()
    returncode = child.wait(timeout=5)
    stdout = write_bytes(PROC / (name + ".stdout"), bytes(buffers["stdout"]))
    stderr = write_bytes(PROC / (name + ".stderr"), bytes(buffers["stderr"]))
    result = {"id": name, "kind": kind, "startedActualUtc": started, "completedActualUtc": utc(),
              "pid": child.pid, "parentPid": os.getpid(), "waitedReturncode": returncode,
              "technicalFailure": technical, "stdout": stdout, "stderr": stderr,
              "reservation": record, "artifactBytes": artifact_bytes()}
    write_json(PROC / (name + ".completion.json"), result)
    require(technical is None, "child." + str(technical), {"id": name, "returncode": returncode})
    require(artifact_bytes() <= MAX_BYTES, "output.cumulative-cap")
    return result, bytes(buffers["stdout"])


def compile_one(label, sources, references, library=False):
    directory = PREP / "binaries" / label
    directory.mkdir(parents=True, exist_ok=False)
    binary = directory / ("production.dll" if library else "SourceResultProgressionChecks.dll")
    runtime = None
    if not library:
        runtime = write_json(directory / "SourceResultProgressionChecks.runtimeconfig.json",
                             {"runtimeOptions": {"tfm": "net10.0", "framework":
                              {"name": "Microsoft.NETCore.App", "version": "10.0.0"}, "rollForward": "Disable"}})
    argv = [DOTNET, "exec", CSC, "/noconfig", "/nostdlib+", "/langversion:7.3",
            "/warnaserror+", "/deterministic+", "/target:" + ("library" if library else "exe"),
            "/out:" + str(binary)]
    if not library:
        argv.append("/main:SourceResultProgressionChecks")
    argv.extend("/reference:" + row["path"] for row in references)
    argv.extend(row["path"] for row in sources)
    result, _ = run_child("c-" + label, "compiler", argv, 120,
                          {"sources": sources, "references": references, "expectedReturncode": 0,
                           "outputPathNotYetProduced": str(binary), "runtimeConfig": runtime,
                           "ns21ProductionOnly": library})
    require(result["waitedReturncode"] == 0, "compiler.warning-clean-" + label)
    return identity(binary), runtime


def execute_one(label, binary, runtime, contract, inventory, control=None):
    argv = [DOTNET, binary["path"], "--contract", contract["path"]]
    if control is not None:
        argv += ["--case", control["case"]]
    result, stdout = run_child("r-" + label, "managed", argv, 30,
                              {"binary": binary, "runtimeConfig": runtime, "contract": contract,
                               "expectedReturncode": 0 if control is None else 2,
                               "expectedSelectedCaseControl": control})
    report = unique_json(stdout)
    observed = validate_report(report, inventory, result["waitedReturncode"], control)
    write_json(VALID / (label + ".json"), observed)
    return observed


def driver():
    require(read_json(PROC / "outer-U1.exit.json")["waitedReturncode"] == 0, "driver.unit-outer-exit")
    unit = read_json(VALID / "U1-result.json")
    require(unit["testsRun"] == 8 and unit["passed"] is True and len(unit["staticNegatives"]) == 6,
            "driver.unit-receipt")
    frozen, admitted = verify_frozen()
    inventory = read_json(frozen["inventory"]["path"])
    controls = read_json(frozen["controls"]["path"])
    sources = frozen["sources"] + frozen["harnesses"]
    references = admitted["references"]["net10.0"]
    binary, runtime = compile_one("clean", sources, references)
    execute_one("clean", binary, runtime, frozen["contract"], inventory)
    for control in controls:
        mutated = control["mutated"]
        pinned(mutated["path"], mutated["bytes"], mutated["sha256"])
        modified_sources = [mutated if row["path"] == control["source"]["path"] else row for row in sources]
        require(sum(row["path"] == mutated["path"] for row in modified_sources) == 1,
                "control.one-actual-source")
        binary, runtime = compile_one(control["id"], modified_sources, references)
        execute_one(control["id"], binary, runtime, frozen["contract"], inventory, control)
    compile_one("ns21-production-only", frozen["sources"], admitted["references"]["netstandard2.1"], True)
    binary, runtime = compile_one("final", sources, references)
    execute_one("final", binary, runtime, frozen["contract"], inventory)
    verify_frozen()
    write_json(VALID / "D1-result.json", {"status": "passed-declared-components", "actualUtc": utc(),
               "clean": {"cases": 38, "fields": 854}, "final": {"cases": 38, "fields": 854},
               "controls": 18, "compilerInvocations": 21, "managedInvocations": 20,
               "ns21ProductionOnlyCompiles": 1, "sourceCount": 77, "harnessCount": 2,
               "nativeOrGenuineCommonExecution": False})


def completed_records(kind):
    result = []
    for path in sorted(PROC.glob("*.completion.json")):
        row = read_json(path)
        if row.get("kind") == kind:
            result.append(row)
    return result


def inspect_results():
    require(read_json(PROC / "outer-D1.exit.json")["waitedReturncode"] == 0, "inspection.driver-outer-exit")
    frozen, _ = verify_frozen()
    inventory = read_json(frozen["inventory"]["path"])
    controls = read_json(frozen["controls"]["path"])
    for label in ("clean", "final"):
        result = read_json(PROC / ("r-" + label + ".completion.json"))
        validate_report(read_json(result["stdout"]["path"]), inventory, result["waitedReturncode"])
    for control in controls:
        result = read_json(PROC / ("r-" + control["id"] + ".completion.json"))
        validate_report(read_json(result["stdout"]["path"]), inventory, result["waitedReturncode"], control)
    compilers, managed = completed_records("compiler"), completed_records("managed")
    require(len(compilers) == 21 and all(r["waitedReturncode"] == 0 and r["technicalFailure"] is None
                                       for r in compilers), "inspection.21-clean-compiles")
    require(len(managed) == 20 and sum(r["waitedReturncode"] == 0 for r in managed) == 2
            and sum(r["waitedReturncode"] == 2 for r in managed) == 18
            and all(r["technicalFailure"] is None for r in managed), "inspection.20-normal-managed")
    unit = read_json(VALID / "U1-result.json")
    require(unit["testsRun"] == 8 and unit["passed"] and len(unit["staticNegatives"]) == 6,
            "inspection.units")
    time_left()
    write_json(VALID / "M2-result.json", {"status": "inspected-no-replay", "actualUtc": utc(),
               "compilerInvocations": len(compilers), "managedInvocations": len(managed),
               "caseCountEach": 38, "literalFieldsEach": 854, "semanticControls": 18,
               "unitTests": 8, "staticNegatives": 6, "outputBytesAtInspection": artifact_bytes()})


def finalize(contract):
    require(read_json(PROC / "outer-M2.exit.json")["waitedReturncode"] == 0, "handoff.inspection-outer-exit")
    verify_frozen()
    inverses = inverse_production(contract)
    caller_proof(contract)
    inherited = read_json(PREP / "inherited41-custody.json")
    for row in inherited:
        require(stat_record(ROOT / row["authority"]["path"]) == row["stat"], "handoff.inherited-custody")
    changed = []
    for rel in [row["path"] for row in contract["requestedExactFileReleaseNotGranted"]] + [
            HARNESS_REL, DRIVER_REL, UNIT_REL, README_REL]:
        changed.append(dict(identity(ROOT / rel), relativePath=rel))
    catalogue = [{"path": r["relativePath"], "bytes": r["bytes"], "sha256": r["sha256"],
                  "authority": "new authored descendant; independent Quality pending"} for r in changed]
    catalogue.extend(row["authority"] for row in inherited)
    require(len(catalogue) == len({r["path"] for r in catalogue}) == 48, "handoff.exact48-catalogue")
    catalogue_pin = write_json(VALID / "current48-catalogue.json", {"files": catalogue,
                              "reviewedDescendantsRequested": 7, "inheritedUnchangedMetadata": 41,
                              "oldCampaignBodiesUnopened": True})
    handoff = {"schema": "source-result-progression-statistics-handoff/v1",
               "status": "IMPLEMENTED_COMPONENT_EVIDENCE_AWAITING_INDEPENDENT_QUALITY",
               "accountableOwner": "Unity", "soleMaterialReviewer": "Quality", "actualUtc": utc(),
               "contract": identity(OUT / "contract.json"),
               "implementationAdmission": identity(OUT / "implementation-admission.json"),
               "executionAdmission": identity(OUT / "execution-admission.json"),
               "files": changed, "currentFrozenCatalogue": catalogue_pin,
               "inverses": inverses, "source": contract["freshPrimaryInspection"]["files"],
               "behavior": contract["producerOwnershipAndOrder"], "api": contract["publicApiPlan"],
               "evidence": {name: identity(VALID / (name + "-result.json"))
                            for name in ("M1", "U1", "D1", "M2")},
               "counts": {"casesEachCleanFinal": 38, "literalFieldsEachCleanFinal": 854,
                          "semanticControls": 18, "unitTests": 8, "staticNegatives": 6,
                          "metadataInvocationsIncludingThisM3": 3, "unitInvocations": 1,
                          "driverInvocations": 1, "compilerInvocations": 21, "managedInvocations": 20,
                          "ns21ProductionOnlyCompiles": 1, "oldCampaignInvocations": 0},
               "preservation": "Three exact whole-file inverses; seven old constructors, completion/capture/"
                               "recovered common body; unchanged caller/Rules/C2/Flow/guard. Inherited41 "
                               "custody uses pinned authority plus unchanged filesystem metadata, not old body rehash.",
               "limits": contract["qualificationLimits"],
               "adjacentNotImplemented": contract["adjacentFindingsDoNotImplement"],
               "m3Boundary": "This immutable handoff precedes M3 return. Actual waited Python and outer "
                             "shell exits are separate process-state/outer-M3.exit.json, written by "
                             "the waiting shell afterward; no future exit is asserted here.",
               "selfApproved": False, "publicationAuthorized": False, "wholeTaskComplete": False}
    pin = write_json(OUT / "source-write-handoff.json", handoff)
    require(read_json(OUT / "source-write-handoff.json") == handoff, "handoff.persisted-readback")
    write_json(VALID / "M3-result.json", {"status": "handoff-persisted-readback", "actualUtc": utc(),
               "handoff": pin, "catalogue": catalogue_pin, "artifactBytes": artifact_bytes(),
               "awaitingIndependentQuality": True, "actualOuterExitNotYetObserved": True})


def finish_return(returncode):
    write_json(PROC / (PHASE + ".return.json"),
               {"actualUtc": utc(), "pid": os.getpid(), "pythonReturnIntent": returncode,
                "notAWaitedProcessExit": True, "outerWaitReceipt": "outer-" + PHASE + ".exit.json",
                "artifactBytes": artifact_bytes()})


def fail(error):
    details = {"status": "failed-stop-no-retry", "actualUtc": utc(),
               "field": error.field if isinstance(error, ProtocolFailure) else "technical." + type(error).__name__,
               "details": error.details if isinstance(error, ProtocolFailure) else None,
               "pid": os.getpid(), "parentPid": os.getppid(), "futureExecutionAuthorized": False}
    if PHASE is not None:
        write_json(VALID / (PHASE + "-failure.json"), details)
    print(json.dumps(details, sort_keys=True))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--admission", required=True)
    phases = parser.add_mutually_exclusive_group(required=True)
    phases.add_argument("--prepare", action="store_true")
    phases.add_argument("--controls", action="store_true")
    phases.add_argument("--inspect", action="store_true")
    phases.add_argument("--finalize", action="store_true")
    args = parser.parse_args()
    require((ROOT / args.admission).absolute() == OUT / "execution-admission.json",
            "entry.exact-admission-argument")
    phase = "M1" if args.prepare else "D1" if args.controls else "M2" if args.inspect else "M3"
    try:
        contract, implementation, execution = enter(phase, "driver" if phase == "D1" else "metadata")
        if phase == "M1":
            prepare(contract, implementation, execution)
        elif phase == "D1":
            driver()
        elif phase == "M2":
            inspect_results()
        else:
            finalize(contract)
        finish_return(0)
        print(json.dumps({"phase": phase, "status": "returning-success-await-outer-wait", "actualUtc": utc()}))
        return 0
    except Exception as error:
        fail(error)
        return 1


if __name__ == "__main__":
    sys.exit(main())
