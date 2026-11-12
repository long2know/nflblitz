#!/usr/bin/python3.12
"""Admitted, append-only actual-source interception component campaign."""
import argparse
import collections
import datetime
import difflib
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import traceback


ROOT = Path(__file__).resolve().parents[2]
BASE = Path(".squad/.scratch/source-following-active-p1")
PUBLIC = BASE / "human-source-final-stats"
CAMPAIGN = Path("tests/source-final-stats/.artifacts/campaign-001")
METADATA = BASE / "metadata/source-final-stats"
CONTRACTS = "Assets/Scripts/Contracts/SourceMatchContracts.cs"
SNAPSHOT = "Assets/Scripts/Gameplay/GameSnapshot.cs"
SUITE_FILES = tuple("tests/source-final-stats/" + name for name in ("Program.cs", "run.py", "README.md"))
PYTHON = "/usr/bin/python3.12"
DOTNET = "/home/long2know/.dotnet/dotnet"
COMPILER = "/home/long2know/.dotnet/sdk/10.0.100/Roslyn/bincore/csc.dll"
ASSEMBLY = "Blitz.SourceFinalStats.Component.dll"
RUNNER = "SourceFinalStats.Runner.dll"
LIMITS = {"csc": 12, "managed": 5, "harnessCorrections": 3}
AUTHORITY_PINS = (
    ("metadata/source-final-stats/implementation-admission.json", 8209,
     "5de866fc7e243e1725927700998bdcf9aa31091cbd2c5f0db512012194df4d55"),
    ("metadata/source-final-stats/contract.json", 40695,
     "1148087a99a71c7d48d0d23fb5a24f5a078c46d4924c5b3dde2b4c8c3689acc3"),
    ("human-possession-time-source-preparation/tool-inputs.json", 524,
     "4ca33ccd83a123844d8851947907c8631d075ca0188c15836743beb8b42e2f24"),
    ("human-possession-time-source-preparation/reference-inputs.json", 77089,
     "73826de6ae906013619081b38d6fc4419f55b388e502d6fcec35fbe932d39a8e"),
)
CASE_IDS = {
    "dto/observed-zero", "dto/team-separation", "dto/cumulative-old-view",
    "match/new-overload-live", "match/new-overload-terminal",
    "match/reference-live", "match/reference-terminal", "match/null-interceptions",
    "match/new-null-time", "match/old-base-unobserved", "match/old-time-unobserved",
    "match/new-defaults", "match/retained-forwarded-values",
}
CONTROLS = (
    ("dto-home-zero", b"HomeInterceptionsMade = homeInterceptionsMade;",
     b"HomeInterceptionsMade = 0;",
     ("dto/team-separation", "HomeInterceptionsMade", "2", "0")),
    ("dto-away-from-home", b"AwayInterceptionsMade = awayInterceptionsMade;",
     b"AwayInterceptionsMade = homeInterceptionsMade;",
     ("dto/team-separation", "AwayInterceptionsMade", "5", "2")),
    ("match-omitted-assignment", b"            Interceptions = interceptions;\n", b"",
     ("match/new-overload-live", "Interceptions", "True", "False")),
)
RUNTIME_CONFIG = {
    "runtimeOptions": {
        "tfm": "net10.0",
        "framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"},
        "rollForward": "Disable",
    }
}


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def identity(path):
    path = Path(path)
    raw = path.read_bytes()
    return {"path": str(path), "bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}


def matches(path, expected):
    actual = identity(path)
    return all(actual[key] == expected[key] for key in ("bytes", "sha256"))


def put_bytes(path, raw):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("xb") as stream:
        stream.write(raw)


def put_json(path, value):
    put_bytes(path, (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8"))


def read_json(path):
    return json.loads(Path(path).read_bytes())


def persist_once(path, value):
    if Path(path).exists():
        require(read_json(path) == value, "Immutable preparation differs: " + str(path))
    else:
        put_json(path, value)
    return identity(path)


def method_span(raw, marker):
    start = raw.index(marker)
    opening = raw.index(b"{", start)
    cursor, depth = opening + 1, 1
    while depth:
        if raw[cursor] == 123:
            depth += 1
        elif raw[cursor] == 125:
            depth -= 1
        cursor += 1
    return start, raw[start:cursor]


def literal_expectations(program):
    text = program.decode("utf-8")
    direct = re.compile(r'Check\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*"([^"]*)"\s*,')
    rows = [{"caseId": case, "field": field, "expected": expected}
            for case, field, expected in direct.findall(text)]
    for helper in ("VerifyForwarded", "VerifyDefaults"):
        _, span = method_span(program, ("    private static void " + helper + "(").encode())
        fields = re.findall(r'Check\(caseId,\s*"([^"]+)",\s*"([^"]*)",', span.decode())
        callers = re.findall(r'\b' + helper + r'\("([^"]+)"', text)
        require(fields and callers, "Literal helper/call inventory missing: " + helper)
        rows.extend({"caseId": case, "field": field, "expected": expected}
                    for case in callers for field, expected in fields)
    keys = [(row["caseId"], row["field"]) for row in rows]
    require(len(set(keys)) == len(keys), "Duplicate named literal expectation")
    counts = dict(sorted(collections.Counter(row["caseId"] for row in rows).items()))
    require(set(counts) == CASE_IDS and len(rows) > 400, "Literal case/field inventory changed")
    return {"schema": "source-final-stats-literal-expectations/v1", "cases": counts,
            "caseCount": len(counts), "fieldAssertionCount": len(rows), "expectations": rows,
            "fixtures": "Only inline public DTO constructor literals; no external fixtures, model or events",
            "realGameSimulationObjects": 0, "realGameSimulationSteps": 0}


class Campaign:
    def __init__(self, args):
        require(Path.cwd() == ROOT, "Run only from the admitted worktree")
        require(sys.executable == PYTHON, "Use the pinned Python executable")
        require(sys.flags.isolated and sys.flags.no_site and sys.dont_write_bytecode,
                "Python must use -I -S -B")
        self.args = args
        self.resuming = args.resume
        if args.resume:
            require(CAMPAIGN.is_dir() and args.correction, "Resume needs an existing campaign and correction reason")
        else:
            require(not args.correction, "Correction requires --resume")
            CAMPAIGN.mkdir(parents=True, exist_ok=False)
        self.attempts = CAMPAIGN / "attempts"
        self.stages = CAMPAIGN / "stages"
        self.attempts.mkdir(exist_ok=True)
        self.stages.mkdir(exist_ok=True)
        self.contract = read_json(BASE / AUTHORITY_PINS[1][0])
        self.admission = read_json(BASE / AUTHORITY_PINS[0][0])
        self.tools = read_json(BASE / AUTHORITY_PINS[2][0])
        self.references = read_json(BASE / AUTHORITY_PINS[3][0])
        self.frozen = self.contract["preservation"]["frozen23"]
        self.released = {CONTRACTS, SNAPSHOT}
        self.pure = [path for path in self.contract["intendedNarrowSourceClosure"]["assetFreeComponentCompilationOnly"]
                     if not path.endswith("/SourceInterceptionsView.cs")]
        require(len(self.pure) == 13, "Actual co-located pure production closure must be thirteen")
        self.before72 = read_json(PUBLIC / "preflight-002/compatibility72-before.json")
        self.expectations = literal_expectations(Path(SUITE_FILES[0]).read_bytes())
        self.expected_map = {(row["caseId"], row["field"]): row["expected"]
                             for row in self.expectations["expectations"]}
        self.verify_inputs()
        initial_program = CAMPAIGN / "inputs/Program.cs"
        if initial_program.exists() and self.counters()["managed"]:
            require(Path(SUITE_FILES[0]).read_bytes() == initial_program.read_bytes(),
                    "The same unchanged runner is required after any managed reservation")
        if args.resume:
            number = self.counters()["harnessCorrections"] + 1
            require(number <= LIMITS["harnessCorrections"], "Harness correction cap exhausted")
            folder = CAMPAIGN / "corrections" / ("%03d" % number)
            folder.mkdir(parents=True, exist_ok=False)
            for name in SUITE_FILES:
                put_bytes(folder / Path(name).name, Path(name).read_bytes())
            put_json(folder / "correction.json", {
                "number": number, "reason": args.correction, "timestampUtc": now(),
                "outerShellId": args.outer_shell_id, "countersBeforeCorrection": self.counters(),
                "expectationsUnchanged": read_json(CAMPAIGN / "expectations.json") == self.expectations,
                "suite": [identity(p) for p in SUITE_FILES],
            })
        else:
            for name in SUITE_FILES:
                put_bytes(CAMPAIGN / "inputs" / Path(name).name, Path(name).read_bytes())
            put_json(CAMPAIGN / "expectations.json", self.expectations)
        require(read_json(CAMPAIGN / "expectations.json") == self.expectations,
                "Never tune literal expectations after observing output")
        self.expected_pin = identity(CAMPAIGN / "expectations.json")
        launch_id = len(list(CAMPAIGN.glob("python-launch-*.json"))) + 1
        put_json(CAMPAIGN / ("python-launch-%03d.json" % launch_id), {
            "startedUtc": now(), "outerShellId": args.outer_shell_id,
            "argv": [PYTHON, "-I", "-S", "-B"] + sys.argv,
            "environment": dict(os.environ), "suite": [identity(p) for p in SUITE_FILES],
            "tools": self.tools, "authority": self.authority_pins(),
            "limits": LIMITS, "countersBefore": self.counters(),
            "literalExpectations": self.expected_pin,
        })
        self.static = self.static_checks()
        self.static_pin = persist_once(CAMPAIGN / "static-checks.json", self.static)
        self.reference_pin = persist_once(CAMPAIGN / "verified-references.json", {
            "authority": identity(BASE / AUTHORITY_PINS[3][0]),
            "profiles": {name: self.references["profiles"][name]
                         for name in ("netstandard2.1", "net10.0")},
        })

    def authority_pins(self):
        return [identity(BASE / path) for path, _, _ in AUTHORITY_PINS]

    def counters(self):
        counts = {"csc": 0, "managed": 0, "harnessCorrections": 0}
        for path in self.attempts.glob("*/input-manifest.json"):
            record = read_json(path)
            counts[record["kind"]] += 1
        counts["harnessCorrections"] = len(list(CAMPAIGN.glob("corrections/*/correction.json")))
        return counts

    def verify_inputs(self):
        for path, size, sha in AUTHORITY_PINS:
            actual = identity(BASE / path)
            require((actual["bytes"], actual["sha256"]) == (size, sha), "Authority pin mismatch: " + path)
        for spec in self.tools.values():
            require(identity(spec["path"]) == spec, "Tool pin mismatch: " + spec["path"])
        for profile, count in (("netstandard2.1", 121), ("net10.0", 167)):
            files = self.references["profiles"][profile]["files"]
            require(len(files) == count, "Reference count mismatch: " + profile)
            for spec in files:
                require(identity(spec["path"]) == spec, "Reference pin mismatch: " + spec["path"])
        for spec in self.frozen:
            if spec["path"] not in self.released:
                require(identity(spec["path"]) == spec, "Unreleased frozen file changed: " + spec["path"])
        for spec in self.contract["currentAdditionalReadOnlySourcePins"]:
            require(identity(spec["path"]) == spec, "Read-only caller changed: " + spec["path"])
        for surface in read_json(PUBLIC / "preflight-002/explicit-compilation-surfaces.json")["inspected"]:
            require(identity(surface["path"]) == surface, "Existing compilation list changed")

    def static_checks(self):
        archive = PUBLIC / "preflight-001/before"
        old = (archive / CONTRACTS).read_bytes()
        current = Path(CONTRACTS).read_bytes()
        spans = []
        checks = []

        def check(name, actual):
            require(actual, "Static preservation check failed: " + name)
            checks.append({"id": name, "passed": True, "evidenceKind": "STATIC_NOT_RUNTIME"})

        for label, marker in (
            ("old-base", b"        public SourceMatchView(SourceMatchConfiguration configuration"),
            ("old-time", b"        public SourceMatchView(SourceTimeOfPossessionView timeOfPossession"),
        ):
            old_offset, before = method_span(old, marker)
            new_offset, after = method_span(current, marker)
            check(label + "-constructor-byte-identity", before == after)
            saved = archive / (label + "-constructor.txt")
            check(label + "-archive-byte-identity", saved.read_bytes() == before)
            spans.append({"id": label, "beforeByteOffset": old_offset, "afterByteOffset": new_offset,
                          "before": identity(saved),
                          "afterBytes": len(after), "afterSha256": hashlib.sha256(after).hexdigest()})
        _, dto = method_span(current, b"    public sealed class SourceInterceptionsView")
        expected_dto = b"""    public sealed class SourceInterceptionsView
    {
        public int HomeInterceptionsMade { get; }
        public int AwayInterceptionsMade { get; }

        public SourceInterceptionsView(int homeInterceptionsMade, int awayInterceptionsMade)
        {
            HomeInterceptionsMade = homeInterceptionsMade;
            AwayInterceptionsMade = awayInterceptionsMade;
        }
    }"""
        check("co-located-sealed-two-get-only-scalar-dto", dto == expected_dto)
        new_marker = b"        public SourceMatchView(SourceInterceptionsView interceptions,"
        _, new_ctor = method_span(current, new_marker)
        _, old_time = method_span(old, b"        public SourceMatchView(SourceTimeOfPossessionView timeOfPossession")
        new_signature = new_ctor[:new_ctor.index(b"            : this")]
        old_signature = old_time[:old_time.index(b"            : this")]
        check("new-overload-retains-all-existing-parameter-default-bytes",
              new_signature.replace(b"SourceInterceptionsView interceptions, ", b"", 1) == old_signature)
        new_delegate = re.search(rb": this\((.*?)\)\s*\{", new_ctor, re.S).group(1)
        old_delegate = re.search(rb": this\((.*?)\)\s*\{", old_time, re.S).group(1)
        new_names = re.findall(rb"\b\w+\b", new_delegate)
        old_names = re.findall(rb"\b\w+\b", old_delegate)
        check("new-overload-forwarding-argument-order", new_names == [b"timeOfPossession"] + old_names)
        check("forwarding-list-matches-selected-contract",
              [name.decode() for name in new_names] == self.contract["constructorForwardingContract"]["delegateArgumentOrder"])
        body = new_ctor[new_ctor.index(b"{"):]
        check("new-overload-only-null-guard-and-assignment", body == b"""{
            if (interceptions == null)
                throw new ArgumentNullException(nameof(interceptions));
            Interceptions = interceptions;
        }""")
        stripped = current.replace(dto + b"\n\n", b"", 1)
        stripped = stripped.replace(b"        public SourceInterceptionsView Interceptions { get; }\n", b"", 1)
        stripped = stripped.replace(new_ctor + b"\n\n", b"", 1)
        check("contracts-file-no-other-edits", stripped == old)
        old_snapshot = (archive / SNAPSHOT).read_bytes()
        new_snapshot = Path(SNAPSHOT).read_bytes()
        inserted = (b"new SourceInterceptionsView(model.SourceInterceptions[(int)TeamId.Home],\n"
                    b"                    model.SourceInterceptions[(int)TeamId.Away]),\n"
                    b"                    ")
        check("single-actual-source-match-constructor-call",
              old_snapshot.count(b"Match = new SourceMatchView(") == 1
              and new_snapshot.count(b"Match = new SourceMatchView(") == 1)
        check("snapshot-new-view-reads-real-home-away-counters", new_snapshot.count(inserted) == 1)
        check("snapshot-all-old-arguments-and-other-bytes-preserved",
              new_snapshot.replace(inserted, b"", 1) == old_snapshot)
        check("no-new-production-script-or-meta",
              not Path("Assets/Scripts/Contracts/SourceInterceptionsView.cs").exists()
              and not Path("Assets/Scripts/Contracts/SourceInterceptionsView.cs.meta").exists())
        check("thirteen-source-component-closure-ten-contracts",
              len(self.pure) == 13 and sum("/Contracts/" in path for path in self.pure) == 10)
        check("remaining-twenty-one-frozen-identities",
              sum(spec["path"] not in self.released and identity(spec["path"]) == spec
                  for spec in self.frozen) == 21)
        source_guard = next(spec for spec in self.frozen if spec["path"].endswith("/GameSimulation.Source.cs"))
        check("whole-source-reverse-call-sha-guard-unchanged", identity(source_guard["path"]) == source_guard)
        return {"schema": "source-final-stats-static-preservation/v1",
                "count": len(checks), "checks": checks, "oldConstructorSpans": spans,
                "beforeFiles": [identity(archive / path) for path in (CONTRACTS, SNAPSHOT)],
                "afterFiles": [identity(path) for path in (CONTRACTS, SNAPSHOT)],
                "unchanged21": [spec for spec in self.frozen if spec["path"] not in self.released],
                "wholeSourceGuard": source_guard,
                "explicitCompilationSurfaces": identity(PUBLIC / "preflight-002/explicit-compilation-surfaces.json"),
                "notSemanticControls": True}

    def new_attempt(self, kind, role):
        self.verify_inputs()
        counts = self.counters()
        require(counts[kind] < LIMITS[kind], "Execution cap exhausted: " + kind)
        if role == "compatibility-only":
            require(not any(read_json(path)["role"] == role
                            for path in self.attempts.glob("*/input-manifest.json")),
                    "The single compatibility compilation has already been reserved")
        number = len(list(self.attempts.glob("*/input-manifest.json"))) + 1
        folder = self.attempts / ("%03d" % number)
        folder.mkdir(exist_ok=False)
        for directory in ("home", "tmp", "cli", "nuget"):
            (folder / "environment" / directory).mkdir(parents=True)
        return folder

    def clean_environment(self, folder):
        scope = (ROOT / folder / "environment")
        return {
            "PATH": "/usr/bin:/bin", "HOME": str(scope / "home"),
            "TMP": str(scope / "tmp"), "TEMP": str(scope / "tmp"), "TMPDIR": str(scope / "tmp"),
            "DOTNET_CLI_HOME": str(scope / "cli"), "NUGET_PACKAGES": str(scope / "nuget"),
            "DOTNET_ROOT": "/home/long2know/.dotnet", "DOTNET_MULTILEVEL_LOOKUP": "0",
            "DOTNET_ROLL_FORWARD": "Disable", "DOTNET_NOLOGO": "1",
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
            "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "true",
            "DOTNET_EnableDiagnostics": "0", "COMPlus_EnableDiagnostics": "0",
            "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT": "1",
            "LANG": "C", "LC_ALL": "C", "TZ": "UTC",
        }

    def capture_sources(self, folder, paths, mutation=None):
        rows = []
        for path in paths:
            raw = Path(path).read_bytes()
            original = identity(path)
            if mutation is not None and path == CONTRACTS:
                name, old, new, failure = mutation
                require(raw.count(old) == 1, "Control edit must match exactly one actual production assignment")
                mutated = raw.replace(old, new, 1)
                put_json(folder / "mutation.json", {
                    "name": name, "productionSource": original,
                    "requiredFailure": dict(zip(("caseId", "field", "expected", "observed"), failure)),
                    "diff": "".join(difflib.unified_diff(
                        raw.decode().splitlines(True), mutated.decode().splitlines(True),
                        fromfile=path, tofile=path)),
                    "completeFileCopy": True,
                })
                raw = mutated
            captured = folder / "sources" / path
            put_bytes(captured, raw)
            rows.append({"productionPath": path, "productionIdentity": original,
                         "capturedIdentity": identity(captured)})
        put_json(folder / "source-identities.json", rows)
        return rows

    def launch(self, folder, kind, role, argv, sources, binaries, outputs, profile, response=None):
        self.verify_inputs()
        for row in sources:
            spec = row["capturedIdentity"]
            require(identity(spec["path"]) == spec, "Captured source changed before launch")
        require(literal_expectations(Path(SUITE_FILES[0]).read_bytes()) == self.expectations,
                "Named literal expectations changed before launch")
        counts = self.counters()
        after = dict(counts)
        after[kind] += 1
        require(after[kind] <= LIMITS[kind], "Reservation would exceed cap")
        timeout = 180 if kind == "csc" else 60
        environment = self.clean_environment(folder)
        source_catalog = identity(folder / "source-identities.json")
        manifest = {
            "schema": "source-final-stats-launch/v1", "number": int(folder.name),
            "kind": kind, "role": role, "reservedUtc": now(),
            "outerShellId": self.args.outer_shell_id, "cwd": str(ROOT), "argv": argv,
            "environment": environment, "timeoutSeconds": timeout,
            "countersBefore": counts, "countersReserved": after, "limits": LIMITS,
            "authority": self.authority_pins(), "toolIdentities": self.tools,
            "referenceProfile": profile, "referenceIdentities": self.references["profiles"][profile]["files"],
            "referenceCatalog": self.reference_pin, "sourceCatalog": source_catalog, "sources": sources,
            "suiteIdentities": [identity(path) for path in SUITE_FILES],
            "literalExpectations": self.expected_pin,
            "namedLiteralExpectations": self.expectations,
            "fixtureIdentity": identity(SUITE_FILES[0]), "externalFixtures": [],
            "binaryInputs": [identity(path) for path in binaries],
            "plannedOutputs": [{"path": str(path), "stateBeforeLaunch": "ABSENT"}
                               for path in outputs],
            "responseFile": identity(response) if response else None,
            "staticChecks": self.static_pin,
            "realGameSimulationObjects": 0, "realGameSimulationSteps": 0,
        }
        for path in outputs:
            require(not Path(path).exists(), "Never overwrite an output binary")
        put_json(folder / "input-manifest.json", manifest)
        started = now()
        stdout, stderr, exit_code, timed_out, error = b"", b"", None, False, None
        try:
            process = subprocess.run(argv, cwd=ROOT, env=environment, stdin=subprocess.DEVNULL,
                                     stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout)
            stdout, stderr, exit_code = process.stdout, process.stderr, process.returncode
        except subprocess.TimeoutExpired as failure:
            timed_out = True
            stdout, stderr = failure.stdout or b"", failure.stderr or b""
            error = "timeout"
        except OSError as failure:
            error = type(failure).__name__ + ": " + str(failure)
        put_bytes(folder / "stdout.txt", stdout)
        put_bytes(folder / "stderr.txt", stderr)
        receipt = {
            "schema": "source-final-stats-execution-receipt/v1", "startedUtc": started, "finishedUtc": now(),
            "outerShellId": self.args.outer_shell_id, "exitCode": exit_code,
            "timedOut": timed_out, "launchError": error, "timeoutSeconds": timeout,
            "inputManifest": identity(folder / "input-manifest.json"),
            "stdout": identity(folder / "stdout.txt"), "stderr": identity(folder / "stderr.txt"),
            "outputs": [identity(path) for path in outputs if Path(path).is_file()],
            "countersConsumed": self.counters(),
        }
        put_json(folder / "receipt.json", receipt)
        print("%s %s exit=%s csc=%d managed=%d" %
              (folder.name, role, exit_code, after["csc"], after["managed"]), flush=True)
        require(not timed_out and error is None, role + " launch/timeout failure; evidence retained")
        return manifest, receipt

    def compile(self, label, paths, mutation=None, runner_library=None, compatibility=False):
        stage = self.stages / (label + ".json")
        if stage.exists():
            result = read_json(stage)
            for output in result["outputs"]:
                require(identity(output["path"]) == output, "Previously compiled binary changed")
            return result
        role = "compatibility-only" if compatibility else label
        folder = self.new_attempt("csc", role)
        sources = self.capture_sources(folder, paths, mutation)
        profile = "net10.0" if runner_library else "netstandard2.1"
        output = folder / "bin" / (RUNNER if runner_library else
                                  "Blitz.SourceFinalStats.Compatibility.dll" if compatibility else ASSEMBLY)
        output.parent.mkdir()
        options = ["/nologo", "/target:exe" if runner_library else "/target:library",
                   "/langversion:7.3", "/warnaserror+", "/deterministic+", "/nostdlib+",
                   "/debug-", "/optimize+", "/out:" + str(ROOT / output),
                   "/pathmap:" + str(ROOT / folder / "sources") + "=/actual"]
        options.extend("/reference:" + entry["path"] for entry in self.references["profiles"][profile]["files"])
        binaries = [DOTNET, COMPILER]
        if runner_library:
            options.append("/reference:" + str(ROOT / runner_library))
            binaries.append(runner_library)
        options.extend(str(ROOT / row["capturedIdentity"]["path"]) for row in sources)
        response = folder / "compile.rsp"
        put_bytes(response, ("\n".join('"' + option + '"' for option in options) + "\n").encode())
        argv = [DOTNET, "exec", COMPILER, "/noconfig", "@" + str(ROOT / response)]
        _, receipt = self.launch(folder, "csc", role, argv, sources, binaries, [output], profile, response)
        good = receipt["exitCode"] == 0 and len(receipt["outputs"]) == 1
        put_json(folder / "qualification.json", {
            "kind": "COMPILATION_ONLY", "cleanCompilation": good,
            "semanticControlKill": False, "notRuntimeProjection": True,
        })
        if not good:
            print((folder / "stdout.txt").read_text() + (folder / "stderr.txt").read_text(), file=sys.stderr)
        require(good, label + " compilation failed; consumed reservation retained")
        result = {"label": label, "attempt": str(folder), "binary": str(output),
                  "sourceCatalog": identity(folder / "source-identities.json"),
                  "inputManifest": identity(folder / "input-manifest.json"),
                  "receipt": identity(folder / "receipt.json"), "outputs": receipt["outputs"]}
        put_json(stage, result)
        return result

    def execute(self, label, library, runner, required_failure=None):
        stage = self.stages / (label + ".json")
        if stage.exists():
            return read_json(stage)
        folder = self.new_attempt("managed", label)
        binary = folder / "bin"
        binary.mkdir()
        for source, name in ((library["binary"], ASSEMBLY), (runner["binary"], RUNNER)):
            put_bytes(binary / name, Path(source).read_bytes())
        config = binary / "SourceFinalStats.Runner.runtimeconfig.json"
        put_json(config, RUNTIME_CONFIG)
        sources = read_json(library["sourceCatalog"]["path"])
        put_json(folder / "source-identities.json", sources)
        argv = [DOTNET, "exec", "--runtimeconfig", str(ROOT / config), str(ROOT / binary / RUNNER)]
        _, receipt = self.launch(folder, "managed", label, argv, sources,
                                 [DOTNET, binary / ASSEMBLY, binary / RUNNER, config], [], "net10.0")
        qualification = self.qualify_output(folder, receipt, required_failure)
        put_json(folder / "qualification.json", qualification)
        require(qualification["qualified"], label + " did not meet its named literal assertions")
        result = {"label": label, "attempt": str(folder),
                  "inputManifest": identity(folder / "input-manifest.json"),
                  "receipt": identity(folder / "receipt.json"),
                  "qualification": identity(folder / "qualification.json"), **qualification}
        put_json(stage, result)
        return result

    def qualify_output(self, folder, receipt, required_failure):
        rows, summaries, errors = [], [], []
        try:
            for line in (folder / "stdout.txt").read_text().splitlines():
                item = json.loads(line)
                if item.get("kind") == "assertion":
                    rows.append(item)
                elif item.get("kind") == "summary":
                    summaries.append(item)
                else:
                    errors.append("Unexpected stdout record")
        except (ValueError, UnicodeError) as failure:
            errors.append("Invalid output: " + str(failure))
        failures, keys = [], []
        for row in rows:
            key = (row.get("caseId"), row.get("field"))
            keys.append(key)
            if key not in self.expected_map or row.get("expected") != self.expected_map.get(key):
                errors.append("Unexpected expectation: " + str(key))
            observed_pass = row.get("expected") == row.get("observed")
            if row.get("passed") != observed_pass:
                errors.append("Incorrect assertion pass flag")
            if not observed_pass:
                failures.append(row)
        if len(set(keys)) != len(keys):
            errors.append("Duplicate output assertion")
        if (folder / "stderr.txt").read_bytes():
            errors.append("Unexpected stderr")
        if len(summaries) != 1:
            errors.append("Expected exactly one summary")
        summary = summaries[0] if len(summaries) == 1 else {}
        if (summary.get("assertions") != len(rows)
                or summary.get("cases") != len({row.get("caseId") for row in rows})
                or summary.get("gameSimulationObjects") != 0 or summary.get("gameSimulationSteps") != 0):
            errors.append("Summary does not match actual named observations")
        if required_failure is None:
            good = (not errors and receipt["exitCode"] == 0 and not failures
                    and set(keys) == set(self.expected_map) and summary.get("status") == "pass")
        else:
            observed = tuple(failures[0].get(key) for key in ("caseId", "field", "expected", "observed")) if len(failures) == 1 else ()
            good = (not errors and receipt["exitCode"] == 1 and observed == required_failure
                    and summary.get("status") == "assertion-failure")
        return {"kind": "SEMANTIC_CONTROL" if required_failure else "COMPONENT_BASELINE",
                "qualified": good, "semanticControlKill": bool(required_failure and good),
                "expectedFailure": list(required_failure) if required_failure else None,
                "observedFailures": failures, "caseCountObserved": summary.get("cases"),
                "fieldAssertionCountObserved": len(rows),
                "passingFieldAssertions": len(rows) - len(failures), "errors": errors,
                "realGameSimulationObjects": 0, "realGameSimulationSteps": 0,
                "notActualSnapshotIntegration": True}

    def compatibility_sources(self):
        rows = []
        for before in self.before72["sources"]:
            current = identity(before["path"])
            if before["path"] not in self.released:
                require(current == before, "Compatibility source changed outside release: " + before["path"])
            rows.append({"before": before, "after": current,
                         "authorizedDescendant": before["path"] in self.released})
        require(len(rows) == 72 and sum(not row["authorizedDescendant"] for row in rows) == 70,
                "Compatibility inventory must be two descendants and seventy unchanged sources")
        persist_once(CAMPAIGN / "compatibility72-identities.json", {
            "historicalPathManifest": self.before72["manifest"], "sources": rows,
            "sourceCount": 72, "unchangedProductionCount": 70, "authorizedDescendantCount": 2,
            "purpose": "Compilation-only type compatibility; no simulation or old runner execution",
        })
        return [row["after"]["path"] for row in rows]

    def handoff(self, results):
        self.verify_inputs()
        require(self.static_checks() == self.static, "Preservation/source pins changed during execution")
        launch_rows = []
        binary_catalog = {}

        def binary_id(spec):
            key = spec["sha256"]
            entry = binary_catalog.setdefault(key, {"bytes": spec["bytes"], "sha256": key, "paths": []})
            require(entry["bytes"] == spec["bytes"], "Binary identity collision")
            if spec["path"] not in entry["paths"]:
                entry["paths"].append(spec["path"])
            return key

        full_launch_rows = []
        for path in sorted(self.attempts.glob("*/input-manifest.json")):
            manifest = read_json(path)
            receipt_path = path.parent / "receipt.json"
            receipt = read_json(receipt_path)
            full_launch_rows.append({
                "inputManifest": identity(path), "manifest": manifest,
                "receiptIdentity": identity(receipt_path), "receipt": receipt,
                "qualification": read_json(path.parent / "qualification.json")
                if (path.parent / "qualification.json").exists() else None,
            })
            launch_rows.append({
                "number": manifest["number"], "kind": manifest["kind"], "role": manifest["role"],
                "outerShellId": manifest["outerShellId"], "argv": manifest["argv"],
                "cwd": manifest["cwd"], "timeoutSeconds": manifest["timeoutSeconds"],
                "cleanEnvironmentAndAllInputIdentities": identity(path),
                "sourceCatalog": manifest["sourceCatalog"], "responseFile": manifest["responseFile"],
                "referenceProfile": manifest["referenceProfile"],
                "referenceCount": len(manifest["referenceIdentities"]),
                "inputBinarySha256": [binary_id(spec) for spec in manifest["binaryInputs"]],
                "outputBinarySha256": [binary_id(spec) for spec in receipt["outputs"]],
                "startedUtc": receipt["startedUtc"], "finishedUtc": receipt["finishedUtc"],
                "exitCode": receipt["exitCode"], "timedOut": receipt["timedOut"],
                "launchError": receipt["launchError"], "countersReserved": manifest["countersReserved"],
                "receipt": identity(receipt_path),
                "qualification": identity(path.parent / "qualification.json")
                if (path.parent / "qualification.json").exists() else None,
            })
        complete_catalog = persist_once(CAMPAIGN / "complete-execution-catalog.json", {
            "allLaunches": full_launch_rows, "componentAndCompatibilityResults": results,
            "purpose": "Full unchanged launch manifests/receipts and results; compact handoff indexes these exact records",
        })

        def result_summary(result):
            summary = {key: result[key] for key in (
                "label", "kind", "qualified", "semanticControlKill", "expectedFailure",
                "observedFailures", "caseCountObserved", "fieldAssertionCountObserved",
                "passingFieldAssertions", "errors") if key in result}
            summary["stageRecord"] = identity(self.stages / (result["label"] + ".json"))
            return summary

        archive = PUBLIC / "preflight-001/before"
        changes = []
        for path in (CONTRACTS, SNAPSHOT):
            before = next(spec for spec in self.frozen if spec["path"] == path)
            changes.append({"path": path, "before": before, "after": identity(path),
                            "exactPreEditArchive": identity(archive / path)})
        source_catalog = [identity(path) for path in self.pure]
        evidence = {
            "schema": "source-interceptions-projection-source-write-handoff/v1",
            "timestampUtc": now(), "accountableOwner": "Unity",
            "status": "IMPLEMENTED_COMPONENT_EVIDENCE_RECORDED_PENDING_INDEPENDENT_QUALITY",
            "materialSelfApproval": False, "nextMaterialReviewer": "Quality",
            "worktree": str(ROOT), "authority": self.authority_pins(),
            "productionChanges": changes, "newTrackedFiles": [identity(path) for path in SUITE_FILES],
            "implementation": {
                "view": "Co-located sealed get-only HomeInterceptionsMade/AwayInterceptionsMade scalar DTO",
                "observedMeaning": "Existing interceptions made by each indexed team, not thrown against it",
                "newOverload": "Leading interceptions/time; null interceptions guard; unchanged time overload delegation",
                "oldOverloads": "Both old constructor byte spans preserved; Interceptions remains null/unobserved",
                "snapshot": "One fresh scalar view reads existing model.SourceInterceptions[Home/Away]; no terminal filtering",
                "producerStorageTimingPolicyChanges": False,
                "newProductionScriptsOrMeta": 0, "additionalWiringOrMetaReleaseNeeded": None,
            },
            "preservation": {
                "all23BeforePinsMatched": True, "frozen23BeforeCatalog": identity(PUBLIC / "preflight-001/frozen23-before.json"),
                "authorizedDescendants": 2, "unchangedFrozenCount": 21,
                "unchanged21": self.static["unchanged21"], "oldConstructorSpans": self.static["oldConstructorSpans"],
                "wholeSourceReverseCallGuard": self.static["wholeSourceGuard"],
                "oldReceiptsAndCampaigns": "Not edited, executed, rerun, or requalified",
            },
            "staticEvidence": {
                "record": self.static_pin, "passedCheckCount": self.static["count"],
                "checks": self.static["checks"], "semanticKillsFromStaticChecks": 0,
            },
            "componentEvidence": {
                "productionSourceCount": 13, "actualProductionSources": source_catalog,
                "productionTarget": "netstandard2.1", "externalRunnerTarget": "net10.0",
                "caseCountPerCleanBaseline": self.expectations["caseCount"],
                "fieldAssertionCountPerCleanBaseline": self.expectations["fieldAssertionCount"],
                "caseFieldCounts": self.expectations["cases"], "literalManifest": self.expected_pin,
                "clean": result_summary(results["clean"]),
                "controls": [result_summary(result) for result in results["controls"]],
                "finalClean": result_summary(results["finalClean"]),
                "sameUnchangedRunner": results["sameRunner"], "sameCleanSourceCatalog": results["sameSources"],
                "componentConstructorPhases": ["LiveBall", "MatchEnded"],
                "notLiveSourceMatchExecution": True,
            },
            "compatibilityOnly": {
                "result": result_summary(results["compatibility"]), "actualProductionSources": 72,
                "authorizedDescendants": 2, "unchangedOtherProductionSources": 70,
                "currentAndHistoricalSourceIdentities": identity(CAMPAIGN / "compatibility72-identities.json"),
                "historicalManifest": self.before72["manifest"], "managedLaunchesOfThisLibrary": 0,
                "successfulRuntimeProjectionClaim": False,
            },
            "execution": {
                "limits": LIMITS, "consumed": self.counters(), "launchCount": len(launch_rows),
                "allLaunches": launch_rows, "toolIdentities": self.tools,
                "binaryIdentityCatalogBySha256": binary_catalog,
                "completeExecutionCatalog": complete_catalog,
                "allReferenceIdentities": self.reference_pin,
                "runtime": RUNTIME_CONFIG, "compileTimeoutSeconds": 180, "managedTimeoutSeconds": 60,
                "failures": [identity(p) for p in sorted(CAMPAIGN.glob("failures/*.json"))],
                "metadataInspections": [
                    {"outerShellId": "unity-final-stats-preflight-001", "exitCode": 0,
                     "record": identity(PUBLIC / "preflight-001/inspection-launch.json"),
                     "receipt": identity(PUBLIC / "preflight-001/preflight-receipt.json")},
                    {"outerShellId": "unity-final-stats-preflight-002", "exitCode": 0,
                     "record": identity(PUBLIC / "preflight-002/inspection-launch.json"),
                     "sourceCatalog": identity(PUBLIC / "preflight-002/compatibility72-before.json")},
                ],
                "metadataCorrection": "Preflight001 recursive manifest selector counted74 catalog entries; preflight002 selected exactly sources[72]. No csc/managed invocation or source write resulted from the first selection.",
                "pythonLaunchRecords": [identity(p) for p in sorted(CAMPAIGN.glob("python-launch-*.json"))],
                "harnessCorrectionRecords": [identity(p) for p in sorted(CAMPAIGN.glob("corrections/*/correction.json"))],
            },
            "unrunBoundaries": {
                "realGameSimulationObjects": 0, "realGameSimulationSteps": 0,
                "actualProducerStepTerminalRestartSnapshotIntegration": "UNRUN; legitimate source roster/audio inputs unavailable through unchanged guards",
                "integrationCases": [case["id"] for case in self.contract["acceptanceCases"]["requiredActualProducerProjectionCasesNotRepresentedByComponentPasses"]],
                "futureSnapshotTeamSwapRuntimeControl": "UNRUN; static wiring and compatibility compilation cannot kill it",
                "legacyBranch": "Statically byte-preserved; not executed in this campaign",
                "oldRuntimeOnsideCaptainTimeCampaigns": "UNRUN in this campaign; historical evidence retains its original input identities",
            },
            "excludedActivities": {
                "networkOrOriginalFetches": 0, "sdkQueryBuildRestoreInstall": 0,
                "engineNativeBrowserDeviceServicePorts": 0, "privateModelMutation": 0,
                "gitIndexBranchPublication": 0, "squadStateHistoryInboxMemory": 0, "nestedAgents": 0,
            },
            "claimsNotMade": ["native equivalence", "all statistics", "all16", "whole100", "rights", "publication", "task_complete"],
        }
        raw = (json.dumps(evidence, indent=2, sort_keys=True) + "\n").encode()
        require(len(raw) <= 65536, "Handoff exceeds admitted 65536-byte bound: " + str(len(raw)))
        destination = METADATA / "source-write-handoff.json"
        put_bytes(destination, raw)
        print("HANDOFF " + str(destination) + " bytes=" + str(len(raw)), flush=True)

    def run(self):
        clean_library = self.compile("clean-library", self.pure)
        runner = self.compile("external-runner", [SUITE_FILES[0]], runner_library=Path(clean_library["binary"]))
        clean = self.execute("clean-baseline", clean_library, runner)
        controls = []
        for mutation in CONTROLS:
            library = self.compile(mutation[0] + "-library", self.pure, mutation=mutation)
            controls.append(self.execute(mutation[0], library, runner, mutation[3]))
        final_library = self.compile("final-clean-library", self.pure)
        initial_sources = read_json(clean_library["sourceCatalog"]["path"])
        final_sources = read_json(final_library["sourceCatalog"]["path"])
        same_sources = ([row["productionIdentity"] for row in initial_sources]
                        == [row["productionIdentity"] for row in final_sources])
        require(same_sources, "Final clean production source identities differ from initial baseline")
        final = self.execute("final-clean-baseline", final_library, runner)
        managed = [read_json(path) for path in sorted(self.attempts.glob("*/input-manifest.json"))
                   if read_json(path)["kind"] == "managed"]
        runner_hashes = [next(spec["sha256"] for spec in row["binaryInputs"] if spec["path"].endswith("/" + RUNNER))
                         for row in managed]
        require(len(runner_hashes) == 5 and len(set(runner_hashes)) == 1, "All five launches require the same unchanged runner")
        compatible = self.compile("compatibility-library", self.compatibility_sources(), compatibility=True)
        self.handoff({"clean": clean, "controls": controls, "finalClean": final,
                      "compatibility": compatible, "sameRunner": True, "sameSources": same_sources})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--outer-shell-id", required=True)
    parser.add_argument("--resume", action="store_true")
    parser.add_argument("--correction")
    args = parser.parse_args()
    try:
        campaign = Campaign(args)
        campaign.run()
        return 0
    except Exception as failure:
        if CAMPAIGN.is_dir():
            folder = CAMPAIGN / "failures"
            folder.mkdir(exist_ok=True)
            number = len(list(folder.glob("*.json"))) + 1
            put_json(folder / ("%03d.json" % number), {
                "timestampUtc": now(), "outerShellId": args.outer_shell_id,
                "type": type(failure).__name__, "message": str(failure),
                "traceback": traceback.format_exc(), "suite": [identity(p) for p in SUITE_FILES if Path(p).is_file()],
                "allAttemptsPreserved": True, "noReservationRefund": True,
            })
        print("SOURCE FINAL STATS STOPPED: " + str(failure), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
