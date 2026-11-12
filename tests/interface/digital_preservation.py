#!/usr/bin/env python3
"""Future, separately admitted direct-csc checks; never discovers tools or runs an SDK build."""

import argparse
import hashlib
import json
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path


ADAPTER = "Assets/Scripts/Runtime/SourceCabinetInputUnity.cs"
RUNTIME = "Assets/Scripts/Runtime/BlitzRuntime.cs"
BUFFER = "Assets/Scripts/Runtime/SourceCabinetInput.cs"
CHECKS = "tests/interface/SourceDigitalPreservationChecks.cs"
MODEL_PROJECT = "tests/interface/InterfaceModel.csproj"
UNITY_PROJECT = "tests/interface/UnityAPICompile.csproj"
RETAINED = ["tests/interface/Program.cs", "tests/interface/MovementBootstrapChecks.cs"]
GROUPS = {
    "fixture": 20, "D1": 508, "D2": 344, "D3": 508, "D4": 1608,
    "D5": 3984, "D6": 5172, "D7": 620, "D8": 298980,
}
TOTAL = 311744
VARIANTS = ("UNITY_EDITOR", "UNITY_WEBGL")
UNITY_NAMES = (
    "UnityEngine.CoreModule.dll", "UnityEngine.IMGUIModule.dll",
    "UnityEngine.InputLegacyModule.dll",
)
CALL = (
    "sourceInput=newSourceCabinetUnityInput(((ISourceMatchState)simulation.State).Match.Configuration,"
    "sourceServices.Hud.Data.Input,sourceServices.NativeGamepadsEnabled,"
    "preserveSourceDigitalChanges:profile.IsContact282C2M1DefaultAudibles);"
)
CALL_PATTERN = r"\bsourceInput\s*=\s*new\s+SourceCabinetUnityInput\s*\([^;]*;"
AWAKE = r"\bprivate\s+void\s+Awake\s*\(\s*\)"
CTOR3 = (
    r"\bpublic\s+SourceCabinetUnityInput\s*\(\s*SourceMatchConfiguration\s+configuration\s*,"
    r"\s*SourceInterfaceInputTables\s+tables\s*,\s*bool\s+nativeGamepadsEnabled\s*\)"
)
CTOR4 = (
    r"\bpublic\s+SourceCabinetUnityInput\s*\(\s*SourceMatchConfiguration\s+configuration\s*,"
    r"\s*SourceInterfaceInputTables\s+tables\s*,\s*bool\s+nativeGamepadsEnabled\s*,"
    r"\s*bool\s+preserveSourceDigitalChanges\s*\)"
)
EXPECTED_CTOR3 = (
    "publicSourceCabinetUnityInput(SourceMatchConfigurationconfiguration,"
    "SourceInterfaceInputTablestables,boolnativeGamepadsEnabled)"
    ":this(configuration,tables,nativeGamepadsEnabled,false){}"
)


class BoundaryError(Exception):
    pass


class StaticFailure(BoundaryError):
    def __init__(self, field):
        self.field = field
        super().__init__(field)


def require(condition, message):
    if not condition:
        raise BoundaryError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def identity(path):
    data = path.read_bytes()
    return {"bytes": len(data), "sha256": sha(data)}


def verify(path, pin):
    actual = identity(path)
    require(actual == {"bytes": pin["bytes"], "sha256": pin["sha256"]},
            "Identity mismatch: " + str(path))
    return actual


def read_json(path, expected_sha):
    raw = path.read_bytes()
    require(sha(raw) == expected_sha, "Manifest SHA256 mismatch: " + str(path))
    return json.loads(raw)


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def relative_file(root, name):
    path = Path(name)
    require(not path.is_absolute() and ".." not in path.parts, "Non-relative source: " + name)
    result = root / path
    require(result.resolve().is_relative_to(root), "Source escapes admitted root: " + name)
    return result


def mask(text):
    """Preserve coordinates, but erase comments and literal contents before inspecting C# code."""
    chars = list(text)
    i = 0
    while i < len(text):
        start = i
        if text.startswith("//", i):
            end = text.find("\n", i)
            i = len(text) if end < 0 else end
        elif text.startswith("/*", i):
            end = text.find("*/", i + 2)
            require(end >= 0, "Unterminated C# comment")
            i = end + 2
        elif text.startswith('@"', i):
            i += 2
            while i < len(text):
                if text.startswith('""', i):
                    i += 2
                elif text[i] == '"':
                    i += 1
                    break
                else:
                    i += 1
            else:
                raise BoundaryError("Unterminated C# verbatim literal")
        elif text[i] in "\"'":
            quote = text[i]
            i += 1
            while i < len(text):
                if text[i] == "\\":
                    i += 2
                elif text[i] == quote:
                    i += 1
                    break
                else:
                    i += 1
            else:
                raise BoundaryError("Unterminated C# literal")
        else:
            i += 1
            continue
        for j in range(start, i):
            if chars[j] not in "\r\n":
                chars[j] = " "
    return "".join(chars)


def compact(text):
    return re.sub(r"\s+", "", mask(text))


def method(text, signature):
    code = mask(text)
    matches = list(re.finditer(signature, code))
    require(len(matches) == 1, "Expected one method/constructor: " + signature)
    start = matches[0].start()
    opening = code.find("{", matches[0].end())
    require(opening >= 0, "Missing method body")
    depth = 1
    end = opening + 1
    while end < len(code) and depth:
        depth += (code[end] == "{") - (code[end] == "}")
        end += 1
    require(depth == 0, "Unbalanced C# method body")
    return start, end, text[start:end]


def active_call(text):
    start, _, body = method(text, AWAKE)
    calls = list(re.finditer(CALL_PATTERN, mask(body)))
    if len(calls) != 1:
        raise StaticFailure("Static.Awake.AdapterCallCount")
    call = calls[0]
    return start + call.start(), start + call.end(), body[call.start():call.end()]


def static_gate(adapter, runtime, reference_adapter, reference_runtime):
    _, _, old = method(adapter, CTOR3)
    if compact(old) != EXPECTED_CTOR3:
        raise StaticFailure("Static.Constructor.LegacyFalseAbi")
    _, _, new = method(adapter, CTOR4)
    _, _, reference_new = method(reference_adapter, CTOR4)
    forwarding = "buffer=newSourceCabinetInputBuffer(configuration,preserveSourceDigitalChanges);"
    if compact(new).count(forwarding) != 1 or compact(new) != compact(reference_new):
        raise StaticFailure("Static.Constructor.ExactForwarding")
    first, last, call = active_call(runtime)
    if compact(call) != CALL:
        raise StaticFailure("Static.Awake.ExactResolvedProfileArgument")
    a, z, awake = method(runtime, AWAKE)
    af, al, _ = active_call(reference_runtime)
    aa, az, _ = method(reference_runtime, AWAKE)
    actual_context = runtime[a:first] + "SOURCE_INPUT_PLACEMENT;" + runtime[last:z]
    reference_context = (reference_runtime[aa:af] + "SOURCE_INPUT_PLACEMENT;" +
                         reference_runtime[al:az])
    if compact(actual_context) != compact(reference_context):
        raise StaticFailure("Static.Awake.ExecutingValidatedContext")
    code = compact(awake)
    resolved = (
        "SourceTeamResourceProfileprofile=SourceTeamResourceProfile.SelectRuntimeProfile("
        "Application.absoluteURL,sourceResourceProfile);"
    )
    validated = "if(sourceServices.Profile.StableId!=profile.StableId)"
    if not (code.count(resolved) == 1 and code.count(validated) == 1 and
            code.index(resolved) < code.index(validated) < code.index(CALL)):
        raise StaticFailure("Static.Awake.ResolutionValidationOrder")
    for actual, approved, signatures in (
        (adapter, reference_adapter, (
            r"\bpublic\s+void\s+Poll\s*\(\s*SourceMatchView\s+match\s*\)",
            r"\bpublic\s+void\s+KeyboardEvent\s*\(\s*Event\s+value\s*\)",
            r"\bprivate\s+void\s+Capture\s*\(\s*\)",
            r"\bpublic\s+void\s+Clear\s*\(\s*bool\s+requireNeutral\s*=\s*true\s*\)",
        )),
        (runtime, reference_runtime, (
            r"\bprivate\s+void\s+Update\s*\(\s*\)",
            r"\binternal\s+void\s+ReadSourceKeyboardEvent\s*\(\s*Event\s+value\s*\)",
            r"\bprivate\s+void\s+ResetFocusInput\s*\(\s*\)",
            r"\bprivate\s+void\s+OnApplicationFocus\s*\(\s*bool\s+value\s*\)",
            r"\bprivate\s+void\s+OnApplicationPause\s*\(\s*bool\s+value\s*\)",
            r"\bprivate\s+void\s+OnDisable\s*\(\s*\)",
            r"\b(?:internal|public|private)\s+void\s+RestartMatch\s*\(\s*\)",
        )),
    ):
        for signature in signatures:
            if compact(method(actual, signature)[2]) != compact(method(approved, signature)[2]):
                raise StaticFailure("Static.PreservedInputLifecycleWiring")
    return {
        "status": "passed", "classification": "method-scoped comment/literal-stripped static only",
        "executed_awake_poll_event_input_native": False,
        "resolved_exact_predicate": "profile.IsContact282C2M1DefaultAudibles",
    }


def static_controls(runtime):
    first, last, call = active_call(runtime)
    predicate = "profile.IsContact282C2M1DefaultAudibles"
    require(call.count(predicate) == 1, "Nonunique static caller predicate")
    omitted, count = re.subn(
        r",\s*preserveSourceDigitalChanges\s*:\s*profile\.IsContact282C2M1DefaultAudibles", "", call)
    require(count == 1, "Nonunique fourth argument")
    cases = [
        ("omit-fourth-with-comment-decoy", omitted + "\n// " + " ".join(call.split())),
        ("hardcode-false", call.replace(predicate, "false")),
        ("hardcode-true", call.replace(predicate, "true")),
        ("invert-exact-predicate", call.replace(predicate, "!" + predicate)),
        ("use-UsesM1Services", call.replace(predicate, "profile.UsesM1Services")),
        ("use-UsesPreSnapSwitchPayload", call.replace(predicate, "profile.UsesPreSnapSwitchPayload")),
        ("dead-correct-call-decoy", "sourceInput = null;\nif (false) {\n" + call + "\n}"),
    ]
    return [(name, runtime[:first] + changed + runtime[last:],
             "Static.Awake.ExecutingValidatedContext" if name == "dead-correct-call-decoy"
             else "Static.Awake.ExactResolvedProfileArgument") for name, changed in cases]


def replace_once(text, old, new, label):
    require(text.count(old) == 1, "Mutation target is not unique: " + label)
    return text.replace(old, new, 1)


def replace_method_once(text, signature, old, new, label):
    first, last, body = method(text, signature)
    changed = replace_once(body, old, new, label)
    return text[:first] + changed + text[last:]


MUTANTS = (
    {"id": "M1-four-argument-force-false", "path": ADAPTER, "partition": "actual-constructor",
     "field": "D1.preserve.two.home.captured.PendingFrames",
     "group": "D1.", "suffix": ".PendingFrames", "signature": CTOR4,
     "old": "new SourceCabinetInputBuffer(configuration, preserveSourceDigitalChanges)",
     "new": "new SourceCabinetInputBuffer(configuration, false)"},
    {"id": "M2-legacy-force-true", "path": ADAPTER, "partition": "actual-constructor",
     "field": "D2.legacy.two.home.coalesced.PendingFrames",
     "group": "D2.legacy.", "suffix": ".PendingFrames", "signature": CTOR3,
     "old": ": this(configuration, tables, nativeGamepadsEnabled, false)",
     "new": ": this(configuration, tables, nativeGamepadsEnabled, true)"},
    {"id": "M3-four-argument-force-true", "path": ADAPTER, "partition": "actual-constructor",
     "field": "D2.explicit-false.two.home.coalesced.PendingFrames",
     "group": "D2.explicit-false.", "suffix": ".PendingFrames", "signature": CTOR4,
     "old": "new SourceCabinetInputBuffer(configuration, preserveSourceDigitalChanges)",
     "new": "new SourceCabinetInputBuffer(configuration, true)"},
    {"id": "M4-remove-digital-queue", "path": BUFFER, "partition": "pure",
     "field": "D1.preserve.two.home.captured.PendingFrames",
     "group": "D1.", "suffix": ".PendingFrames", "signature": r"\bpublic\s+void\s+Capture\s*\(",
     "old": "preserveSourceDigitalChanges && digitalChanged",
     "new": "preserveSourceDigitalChanges && digitalChanged && sample.StationId < 0"},
    {"id": "M5-enqueue-equal-digital", "path": BUFFER, "partition": "pure",
     "field": "D3.preserve.two.home.repeated-up.PendingFrames",
     "group": "D3.", "suffix": ".PendingFrames", "signature": r"\bpublic\s+void\s+Capture\s*\(",
     "old": "preserveSourceDigitalChanges && digitalChanged",
     "new": "preserveSourceDigitalChanges && (digitalChanged || !digitalChanged)"},
    {"id": "M6-lose-queued-DigitalCurrent", "path": BUFFER, "partition": "pure",
     "field": "D1.preserve.two.home.up.station[0].DigitalCurrentSpecified",
     "group": "D1.", "suffix": ".DigitalCurrentSpecified", "signature": r"\bpublic\s+void\s+Capture\s*\(",
     "old": "sample.Held, pressed, released, sample.Command, digital);",
     "new": "sample.Held, pressed, released, sample.Command);"},
    {"id": "M7-clear-retains-queue", "path": BUFFER, "partition": "pure",
     "field": "D6.preserve.two.home.clear-true.PendingFrames",
     "group": "D6.", "suffix": "clear-true.PendingFrames",
     "signature": r"\bpublic\s+void\s+Clear\s*\(\s*bool\s+requireNeutral\s*=\s*true\s*\)",
     "old": "ordered.Clear();", "new": ""},
)


class Campaign:
    def __init__(self, args, inputs, references):
        self.args = args
        self.inputs = inputs
        self.references = references
        self.root = Path(args.source_root).resolve()
        self.output = Path(args.output).resolve()
        require(self.root == Path(inputs["worktreeRoot"]), "Wrong admitted worktree")
        require(self.output.parent == self.root / "tests/interface/.artifacts" and
                re.fullmatch(r"digital-preservation(?:-[a-z0-9]+(?:-[a-z0-9]+)*)?", self.output.name) is not None,
                "Output must be an explicitly admitted digital-preservation campaign sibling")
        require(self.output == relative_file(self.root, inputs["outputRequiresSeparateAdmission"]),
                "Output must match the exact campaign path in the pinned source manifest")
        require(not self.output.exists(), "Output must be absent/fresh; no resume or overwrite")
        require(Path(args.dotnet) == Path(references["dotnet"]["path"]), "Unexpected dotnet path")
        require(Path(args.csc) == Path(references["compiler"]["path"]), "Unexpected compiler path")
        self.dotnet = Path(args.dotnet)
        self.csc = Path(args.csc)
        self.file_pins = {item["path"]: item for item in inputs["files"]}
        self.model_extra = inputs["modelAdditionalSources"]
        require(len(self.model_extra) == 45 and len(set(self.model_extra)) == 45 and
                "Assets/Scripts/Gameplay/GameSimulation.Possession.cs" in self.model_extra and
                not set(self.model_extra).intersection(inputs["projects"][MODEL_PROJECT]["sources"]),
                "The closed whole-source sound dependency list must augment, not replace, the model project")
        require(all(name.startswith("Assets/Scripts/") and name.endswith(".cs") and
                    name in self.file_pins and name in inputs["captureFiles"] for name in self.model_extra),
                "Every additional model dependency must be a whole pinned public-source capture")
        self.verify_live()
        verify(self.dotnet, references["dotnet"])
        verify(self.csc, references["compiler"])
        self.ns = self.reference_profile("netstandard2.1", args.netstandard_references, 121)
        self.net = self.reference_profile("net10.0", args.net10_references, 167)
        self.unity = []
        self.unity_error = None
        try:
            directory = Path(args.unity_managed)
            require(directory.is_absolute() and str(directory) == references["unity"]["directory"],
                    "Genuine Unity root must match the explicit supplied metadata")
            require([item["name"] for item in references["unity"]["files"]] == list(UNITY_NAMES),
                    "Exactly the three supplied Unity metadata files are permitted")
            for item in references["unity"]["files"]:
                path = directory / item["name"]
                verify(path, item)
                self.unity.append(path)
        except (BoundaryError, OSError) as error:
            self.unity = []
            self.unity_error = str(error)
        self.output.mkdir(parents=True)
        for name in ("home", "dotnet-home", "runtime-scratch"):
            (self.output / name).mkdir()
        self.environment = {
            "DOTNET_ROOT": str(self.dotnet.parent), "DOTNET_CLI_HOME": str(self.output / "dotnet-home"),
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
            "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "true", "DOTNET_NOLOGO": "1",
            "HOME": str(self.output / "home"), "LANG": "C.UTF-8",
            "TMPDIR": str(self.output / "runtime-scratch"), "TMP": str(self.output / "runtime-scratch"),
            "TEMP": str(self.output / "runtime-scratch"),
        }
        self.report = {
            "schema": "source-digital-preservation-execution/v1", "status": "running",
            "material_review_performed": False, "material_approval": False,
            "expected": {"groups": GROUPS, "assertions_per_pure_or_constructor_run": TOTAL,
                         "static_controls": 7, "unique_source_mutants": 7,
                         "mutation_runs_if_all_dependencies_load": 10,
                         "compiler_commands_if_complete": 37, "managed_runs_if_complete": 18,
                         "retained_synthetic_minimum": 1500, "optional_actual_data_minimum": 14000},
            "input_manifest": {"path": args.inputs, "sha256": args.inputs_sha256},
            "reference_manifest": {"path": args.references, "sha256": args.references_sha256},
            "tools": {"dotnet": references["dotnet"], "compiler": references["compiler"]},
            "reference_profiles": references["profiles"], "unity_metadata": references["unity"],
            "unity_dependency_error": self.unity_error, "commands": [], "baselines": {},
            "static": {"status": "unrun", "controls": []}, "typecheck": [], "mutants": [],
            "retained": {"status": "unrun", "original_program_and_helper_unchanged": True},
            "optional_actual_data": {"status": "unrun", "assets_supplied": False, "minimum_untouched": 14000},
            "dependency_augmented_typecheck": {
                "classification": "DEPENDENCY-AUGMENTED DIRECT MODEL/API TYPECHECK",
                "original_model_sources": inputs["projects"][MODEL_PROJECT]["sources"],
                "additional_whole_model_sources": self.model_extra,
                "original_unity_api_sources": inputs["projects"][UNITY_PROJECT]["sources"],
                "sdk_project_build_or_game_simulation_execution_claimed": False,
            },
            "limits": [
                "D7 uses actual transport payload with independent literal synthetic SOURCE priors/ticks.",
                "No GameSimulation decode/history/scheduler, audible commit, physical sampling or native claim.",
                "Constructor execution is managed net10 with genuine read-only Unity metadata, not Unity execution.",
                "Static Awake/lifecycle controls are not compiled or executed Runtime evidence.",
                "Accepted onside/captain/global/whole-game/native/assets remain outside this component.",
            ],
        }
        self.save()

    def save(self):
        compilers = [item for item in self.report["commands"] if len(item["argv"]) > 2 and item["argv"][1] == "exec"]
        runners = [item for item in self.report["commands"] if item["label"].endswith("-run")]
        self.report["observed_command_counts"] = {
            "compiler_started": len(compilers),
            "compiler_completed": sum(item["status"] == "completed" for item in compilers),
            "clean_compiles": sum(item.get("clean_compile") is True for item in compilers),
            "managed_started": len(runners),
            "managed_completed": sum(item["status"] == "completed" for item in runners),
            "timeouts": sum(item["status"] == "timeout" for item in self.report["commands"]),
            "launch_errors": sum(item["status"] == "launch-error" for item in self.report["commands"]),
        }
        write_json(self.output / "result.json", self.report)

    def verify_live(self):
        for name, pin in self.file_pins.items():
            verify(relative_file(self.root, name), pin)

    def reference_profile(self, name, directory, expected_count):
        profile = self.references["profiles"][name]
        require(profile["count"] == expected_count and len(profile["files"]) == expected_count,
                "Wrong full reference count: " + name)
        root = Path(directory)
        require(root.is_absolute() and str(root) == profile["directory"], "Reference root substitution: " + name)
        result = []
        for item in profile["files"]:
            path = Path(item["path"])
            require(path.parent == root and path.suffix == ".dll", "Unexpected reference member: " + str(path))
            verify(path, item)
            result.append(path)
        require(len(set(result)) == expected_count, "Duplicate reference member: " + name)
        return result

    def capture(self):
        capture = self.output / "capture"
        for name in self.inputs["captureFiles"]:
            source = relative_file(self.root, name)
            verify(source, self.file_pins[name])
            destination = relative_file(capture, name)
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(source.read_bytes())
        for project in (MODEL_PROJECT, UNITY_PROJECT):
            actual = []
            for node in ET.fromstring((capture / project).read_bytes()).findall(".//Compile"):
                include = node.attrib["Include"]
                require(include.startswith("$(BlitzSourceRoot)"), "Unexpected Compile include: " + include)
                pattern = include[len("$(BlitzSourceRoot)"):]
                matches = sorted(capture.glob(pattern)) if "*" in pattern else [capture / pattern]
                actual.extend(path.relative_to(capture).as_posix() for path in matches)
            require(actual == self.inputs["projects"][project]["sources"], "Expanded source closure drift: " + project)
        self.report["captured_sources"] = [
            {"path": name, **identity(capture / name)} for name in self.inputs["captureFiles"]
        ]
        self.save()
        return capture

    def command(self, label, command, cwd):
        directory = self.output / "commands" / label
        directory.mkdir(parents=True)
        receipt = {"label": label, "argv": [str(x) for x in command], "cwd": str(cwd),
                   "started_unix_ns": time.time_ns(), "timeout_seconds": self.args.command_timeout,
                   "status": "started", "exit": None}
        self.report["commands"].append(receipt)
        write_json(directory / "command.json", receipt)
        self.save()
        started = time.monotonic()
        try:
            result = subprocess.run(command, cwd=cwd, env=self.environment, capture_output=True,
                                    timeout=self.args.command_timeout, check=False)
            stdout, stderr = result.stdout, result.stderr
            receipt.update(status="completed", exit=result.returncode)
        except subprocess.TimeoutExpired as error:
            stdout, stderr = error.stdout or b"", error.stderr or b""
            receipt.update(status="timeout")
        except OSError as error:
            stdout, stderr = b"", str(error).encode("utf-8")
            receipt.update(status="launch-error")
        receipt.update(finished_unix_ns=time.time_ns(), elapsed_seconds=time.monotonic() - started)
        (directory / "stdout.log").write_bytes(stdout)
        (directory / "stderr.log").write_bytes(stderr)
        receipt["stdout"] = identity(directory / "stdout.log")
        receipt["stderr"] = identity(directory / "stderr.log")
        write_json(directory / "command.json", receipt)
        self.save()
        return receipt, stdout

    def compile(self, label, sources, references, output, defines=(), executable=False):
        output.parent.mkdir(parents=True, exist_ok=True)
        def quote(value):
            text = str(value)
            require(not any(char in text for char in '"\r\n'), "Unsupported response-file path")
            return '"' + text + '"'
        arguments = [
            "/nostdlib+", "/langversion:7.3", "/warnaserror+", "/deterministic+",
            "/optimize+", "/debug-", "/utf8output", "/target:" + ("exe" if executable else "library"),
            "/out:" + quote(output), "/pathmap:" + quote(str(self.output / "capture") + "=/public-port"),
        ]
        if defines:
            arguments.append("/define:" + ";".join(defines))
        arguments.extend("/reference:" + quote(path) for path in references)
        arguments.extend(quote(path) for path in sources)
        rsp = output.parent / (output.stem + ".rsp")
        rsp.write_text("\n".join(arguments) + "\n", encoding="utf-8")
        receipt, _ = self.command(label, [str(self.dotnet), "exec", str(self.csc), "/noconfig", "@" + str(rsp)],
                                  output.parent)
        archived_rsp = self.output / "commands" / label / "compiler.rsp"
        shutil.copyfile(rsp, archived_rsp)
        receipt["response_file"] = {"path": str(archived_rsp), "working_path": str(rsp), **identity(archived_rsp)}
        receipt["sources"] = [{"path": str(path), **identity(path)} for path in sources]
        receipt["references"] = [{"path": str(path), **identity(path)} for path in references]
        clean = receipt["status"] == "completed" and receipt["exit"] == 0 and output.is_file()
        receipt["clean_compile"] = clean
        if clean:
            archived_assembly = self.output / "commands" / label / output.name
            shutil.copyfile(output, archived_assembly)
            receipt["assembly"] = {
                "path": str(archived_assembly), "working_path": str(output), **identity(archived_assembly),
            }
        write_json(self.output / "commands" / label / "command.json", receipt)
        self.save()
        return clean

    def model(self, capture, directory, label):
        sources = [capture / name for name in self.inputs["projects"][MODEL_PROJECT]["sources"] + self.model_extra]
        output = directory / "model/DigitalModel.dll"
        return output if self.compile(label + "-model", sources, self.ns, output) else None

    def run(self, label, runner, unity=False, retained=False):
        config = {"runtimeOptions": {"tfm": "net10.0", "framework": {
            "name": "Microsoft.NETCore.App", "version": "10.0.0"}, "rollForward": "Disable"}}
        write_json(runner.with_suffix(".runtimeconfig.json"), config)
        command = [str(self.dotnet), str(runner)]
        if unity:
            command.append(self.args.unity_managed)
        receipt, stdout = self.command(label + "-run", command, runner.parent)
        summaries = []
        for line in stdout.decode("utf-8", errors="replace").splitlines():
            try:
                value = json.loads(line)
            except json.JSONDecodeError:
                continue
            if isinstance(value, dict) and "schema" in value:
                summaries.append(value)
        expected_schema = "source-interface-checks/v1" if retained else "source-digital-preservation-checks/v1"
        summary = summaries[0] if len(summaries) == 1 and summaries[0].get("schema") == expected_schema else None
        return {"status": "observed" if summary is not None else "unrun-or-error",
                "command": receipt, "summary": summary}

    def digital(self, capture, directory, model, label, variant=None):
        if model is None:
            return {"status": "unrun", "reason": "model did not compile"}
        directory.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(model, directory / "DigitalModel.dll")
        references = self.net + [directory / "DigitalModel.dll"]
        defines = []
        if variant is not None:
            if not self.unity:
                self.report["typecheck"].append({
                    "label": label, "variant": variant, "status": "unrun", "reason": self.unity_error,
                })
                self.save()
                return {"status": "unrun", "reason": self.unity_error}
            api = directory / "DigitalUnity.dll"
            clean = self.compile(label + "-unity-api",
                                 [capture / name for name in self.inputs["projects"][UNITY_PROJECT]["sources"]],
                                 self.ns + self.unity + [directory / "DigitalModel.dll"], api, [variant])
            self.report["typecheck"].append({
                "label": label, "variant": variant, "status": "passed" if clean else "failed",
                "scope": "Whole actual UnityAPICompile.csproj closure with dependency-augmented direct model; not SDK build, BlitzRuntime/Awake or GameSimulation execution",
            })
            self.save()
            if not clean:
                return {"status": "unrun", "reason": "whole actual Unity API closure did not cleanly compile"}
            references += [api] + self.unity
            defines = ["DIGITAL_UNITY", variant]
        runner = directory / "DigitalChecks.dll"
        if not self.compile(label + "-checks", [capture / CHECKS], references, runner, defines, executable=True):
            return {"status": "unrun", "reason": "check runner did not cleanly compile"}
        result = self.run(label, runner, unity=variant is not None)
        result["requested_partition"] = "actual-constructor" if variant else "pure"
        result["requested_variant"] = variant or "none"
        summary = result.get("summary") or {}
        if variant is not None and summary.get("status") not in ("passed", "failed"):
            result["status"] = "unrun"
            result["reason"] = "Actual constructor did not complete; inspect load/launch diagnostics, not a semantic kill"
        return result

    def retained(self, capture, directory, model, label):
        if model is None:
            return {"status": "unrun", "reason": "model did not compile"}
        directory.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(model, directory / "DigitalModel.dll")
        runner = directory / "RetainedChecks.dll"
        if not self.compile(label + "-checks", [capture / name for name in RETAINED],
                            self.net + [directory / "DigitalModel.dll"], runner, executable=True):
            return {"status": "unrun", "reason": "unchanged Program/helper did not cleanly compile"}
        return self.run(label, runner, retained=True)

    def passed(self, result, retained=False):
        summary = result.get("summary") or {}
        command = result.get("command") or {}
        if not (command.get("status") == "completed" and command.get("exit") == 0 and summary.get("status") == "passed"):
            return False
        if retained:
            return (summary.get("assertions", 0) >= 1500 and summary.get("actual_assertions") == 0 and
                    summary.get("original_bundle_supplied") is False)
        if (summary.get("partition") != result.get("requested_partition") or
                summary.get("variant") != result.get("requested_variant") or
                summary.get("actual_game_simulation_history_claimed") is not False or
                summary.get("source_decode_scheduler_native_or_physical_sampling_claimed") is not False):
            return False
        loaded = summary.get("unity_loaded")
        if not isinstance(loaded, list):
            return False
        if result.get("requested_partition") == "actual-constructor":
            names = [item.get("name") for item in loaded]
            if "UnityEngine.CoreModule" not in names or len(set(names)) != len(names):
                return False
            pins = {item["name"][:-4]: item for item in self.references["unity"]["files"]}
            for item in loaded:
                if item.get("name") not in pins:
                    return False
                pin = pins[item["name"]]
                path = Path(self.args.unity_managed) / pin["name"]
                if item.get("path") != str(path):
                    return False
                verify(path, pin)
        elif loaded:
            return False
        return (summary.get("assertions") == TOTAL and summary.get("expected_assertions") == TOTAL and
                summary.get("groups") == GROUPS and summary.get("expected_groups") == GROUPS and
                set(summary.get("completed_groups", [])) == set(GROUPS))

    def baseline(self, capture, name):
        archive = self.output / name
        archive.mkdir()
        directory = self.output / "baseline-build"
        directory.mkdir(exist_ok=True)
        model = self.model(capture, directory, name)
        results = {"pure": {"status": "unrun"}, "retained": {"status": "unrun"}, "constructors": {}}
        self.report["baselines"][name] = results
        results["pure"] = self.digital(capture, directory / "pure", model, name + "-pure")
        self.save()
        results["retained"] = self.retained(capture, directory / "retained", model, name + "-retained")
        self.save()
        for variant in VARIANTS:
            results["constructors"][variant] = self.digital(
                capture, directory / variant, model, name + "-" + variant, variant)
            self.save()
        artifacts = []
        if model is not None:
            artifacts.append("model/DigitalModel.dll")
        for part, result, runner in (
            ("pure", results["pure"], "DigitalChecks"),
            ("retained", results["retained"], "RetainedChecks"),
        ):
            if "command" in result:
                artifacts.extend(part + "/" + filename for filename in (
                    "DigitalModel.dll", runner + ".dll", runner + ".runtimeconfig.json"))
        for variant in VARIANTS:
            if "command" in results["constructors"][variant]:
                artifacts.extend(variant + "/" + filename for filename in (
                    "DigitalModel.dll", "DigitalUnity.dll", "DigitalChecks.dll", "DigitalChecks.runtimeconfig.json"))
        results["archived_emitted_artifacts"] = []
        for name_in_archive in artifacts:
            destination = archive / name_in_archive
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(directory / name_in_archive, destination)
            results["archived_emitted_artifacts"].append({"path": name_in_archive, **identity(destination)})
        results["baseline_build_paths_reused_but_all_assemblies_recompiled"] = True
        self.report["baselines"][name] = results
        self.save()
        return results, model

    def static(self, capture, label, controls=False):
        adapter = (capture / ADAPTER).read_text(encoding="utf-8")
        runtime = (capture / RUNTIME).read_text(encoding="utf-8")
        reference_adapter = relative_file(self.root, self.inputs["authoredAfter"][ADAPTER]["path"]).read_text(encoding="utf-8")
        reference_runtime = relative_file(self.root, self.inputs["authoredAfter"][RUNTIME]["path"]).read_text(encoding="utf-8")
        result = static_gate(adapter, runtime, reference_adapter, reference_runtime)
        self.report["static"][label] = result
        if controls:
            for name, changed, expected_field in static_controls(runtime):
                destination = self.output / "static-controls" / name / RUNTIME
                destination.parent.mkdir(parents=True)
                destination.write_text(changed, encoding="utf-8")
                rejected = False
                field = None
                try:
                    static_gate(adapter, changed, reference_adapter, reference_runtime)
                except StaticFailure as error:
                    field = error.field
                    rejected = field == expected_field
                self.report["static"]["controls"].append({
                    "id": name, "status": "rejected-static" if rejected else "not-discriminated",
                    "expected_exact_field": expected_field, "failed_field": field,
                    "source": {"path": str(destination), **identity(destination)},
                    "compiled_or_executed": False,
                })
                self.save()
                require(rejected, "Static caller control did not fail its named Awake field: " + name)
        self.report["static"]["status"] = "passed-static-only"
        self.save()

    def mutations(self, capture, initial):
        for mutant in MUTANTS:
            record = {"id": mutant["id"], "path": mutant["path"], "expected_group_prefix": mutant["group"],
                      "expected_field_suffix": mutant["suffix"], "expected_exact_field": mutant["field"],
                      "variants": {}, "status": "unrun"}
            self.report["mutants"].append(record)
            variants = VARIANTS if mutant["partition"] == "actual-constructor" else (None,)
            eligible = [variant for variant in variants if self.passed(
                initial["constructors"][variant] if variant else initial["pure"])]
            if not eligible:
                record["reason"] = "Corresponding clean behavioral baseline unavailable; never a killed mutant"
                self.save()
                continue
            directory = self.output / "mutants" / mutant["id"]
            mutant_capture = directory / "source"
            shutil.copytree(capture, mutant_capture)
            path = mutant_capture / mutant["path"]
            path.write_text(replace_method_once(path.read_text(encoding="utf-8"), mutant["signature"],
                                               mutant["old"], mutant["new"], mutant["id"]), encoding="utf-8")
            record["source"] = {"path": str(path), **identity(path)}
            record["whole_source_copy"] = True
            record["captured_sources"] = [
                {"path": name, **identity(mutant_capture / name)} for name in self.inputs["captureFiles"]
            ]
            for item in record["captured_sources"]:
                if item["path"] != mutant["path"]:
                    require({"bytes": item["bytes"], "sha256": item["sha256"]} ==
                            identity(capture / item["path"]), "Mutation changed an unrelated captured source")
            model = self.model(mutant_capture, directory, mutant["id"])
            for variant in variants:
                key = variant or "pure"
                if variant not in eligible:
                    record["variants"][key] = {"status": "unrun", "reason": "No passing matching initial baseline"}
                    continue
                result = self.digital(mutant_capture, directory / key, model, mutant["id"] + "-" + key, variant)
                summary = result.get("summary") or {}
                command = result.get("command") or {}
                field = summary.get("failed_field") or ""
                killed = (command.get("status") == "completed" and command.get("exit") == 1 and
                          summary.get("status") == "failed" and
                          summary.get("exception_type", "").endswith("+FieldFailure") and
                          field == mutant["field"] and
                          field.startswith(mutant["group"]) and field.endswith(mutant["suffix"]))
                result["status"] = "rejected-semantic" if killed else "invalid-or-not-discriminated"
                record["variants"][key] = result
                self.save()
            record["status"] = ("rejected-semantic" if len(record["variants"]) == len(variants) and
                                all(item["status"] == "rejected-semantic" for item in record["variants"].values())
                                else "incomplete")
            self.save()

    def compare(self, initial, final):
        require(self.passed(initial["pure"]) and self.passed(final["pure"]), "Pure baseline incomplete")
        require(self.passed(initial["retained"], True) and self.passed(final["retained"], True),
                "Retained unchanged synthetic Program baseline incomplete")
        stable = []
        for relative in ("model/DigitalModel.dll", "pure/DigitalModel.dll", "pure/DigitalChecks.dll",
                         "pure/DigitalChecks.runtimeconfig.json", "retained/DigitalModel.dll",
                         "retained/RetainedChecks.dll", "retained/RetainedChecks.runtimeconfig.json"):
            first, last = self.output / "initial" / relative, self.output / "final" / relative
            require(first.read_bytes() == last.read_bytes(), "Byte-different baseline: " + relative)
            stable.append({"path": relative, **identity(first)})
        for key in ("pure", "retained"):
            require(initial[key]["command"]["stdout"] == final[key]["command"]["stdout"],
                    "Initial/final stdout differs: " + key)
        constructors_complete = True
        for variant in VARIANTS:
            a, z = initial["constructors"][variant], final["constructors"][variant]
            if not (self.passed(a) and self.passed(z)):
                constructors_complete = False
                continue
            require(a["command"]["stdout"] == z["command"]["stdout"], "Constructor baseline stdout differs: " + variant)
            for filename in ("DigitalModel.dll", "DigitalUnity.dll", "DigitalChecks.dll", "DigitalChecks.runtimeconfig.json"):
                relative = variant + "/" + filename
                first, last = self.output / "initial" / relative, self.output / "final" / relative
                require(first.read_bytes() == last.read_bytes(), "Constructor baseline bytes differ: " + relative)
                stable.append({"path": relative, **identity(first)})
        self.report["baseline_identity"] = {
            "pure_and_retained_byte_identical": True, "constructors_byte_identical": constructors_complete,
            "artifacts": stable, "compiler_logs_not_treated_as_byte-identical-baselines": True,
        }
        self.report["retained"] = {"status": "passed-synthetic-only", "minimum": 1500,
                                   "initial": initial["retained"]["summary"],
                                   "final": final["retained"]["summary"], "unchanged_sources": RETAINED}
        return constructors_complete

    def execute(self):
        capture = self.capture()
        self.static(capture, "initial", controls=True)
        initial, _ = self.baseline(capture, "initial")
        require(self.passed(initial["pure"]), "Pure initial baseline failed; no mutant evidence is admissible")
        self.mutations(capture, initial)
        final, _ = self.baseline(capture, "final")
        self.static(capture, "final")
        constructors_complete = self.compare(initial, final)
        complete = (constructors_complete and len(self.report["mutants"]) == 7 and
                    all(item["status"] == "rejected-semantic" for item in self.report["mutants"]) and
                    len(self.report["static"]["controls"]) == 7)
        self.report["status"] = "checks-completed-awaiting-quality" if complete else "incomplete"
        self.report["integrated_checks_completed_not_approval"] = complete
        self.report["compiler_command_count"] = sum("-model" in item["label"] or item["label"].endswith(
            ("-checks", "-unity-api")) for item in self.report["commands"])
        self.report["managed_run_count"] = sum(item["label"].endswith("-run") for item in self.report["commands"])
        if complete:
            require(self.report["compiler_command_count"] == 37 and self.report["managed_run_count"] == 18,
                    "Declared command graph count mismatch")
        self.save()
        return 0 if complete else 2


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute-authorized", action="store_true",
                        help="Requires separate parent admission of execution AND fresh artifact writes.")
    for name in ("source-root", "inputs", "inputs-sha256", "references", "references-sha256",
                 "dotnet", "csc", "netstandard-references", "net10-references", "unity-managed", "output"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--command-timeout", type=int, default=180)
    args = parser.parse_args()
    require(args.execute_authorized, "AUTHORING ONLY: separate execution/output admission is required")
    require(args.command_timeout > 0, "Command timeout must be positive")
    inputs = read_json(Path(args.inputs), args.inputs_sha256)
    references = read_json(Path(args.references), args.references_sha256)
    require(inputs["schema"] == "source-digital-execution-inputs/v1", "Wrong source manifest")
    require(references["schema"] == "source-digital-reference-inputs/v1", "Wrong reference manifest")
    campaign = Campaign(args, inputs, references)
    exit_code = 2
    try:
        exit_code = campaign.execute()
    except Exception as error:
        campaign.report["status"] = "incomplete"
        campaign.report["integrated_checks_completed_not_approval"] = False
        campaign.report["error"] = {"type": type(error).__name__, "message": str(error)}
    finally:
        try:
            campaign.verify_live()
            verify(campaign.dotnet, references["dotnet"])
            verify(campaign.csc, references["compiler"])
            campaign.reference_profile("netstandard2.1", args.netstandard_references, 121)
            campaign.reference_profile("net10.0", args.net10_references, 167)
            for item in references["unity"]["files"]:
                if campaign.unity:
                    verify(Path(args.unity_managed) / item["name"], item)
            for item in inputs["captureFiles"]:
                verify(campaign.output / "capture" / item, campaign.file_pins[item])
            campaign.report["live_sources_references_and_immutable_capture_unchanged"] = True
        except Exception as error:
            campaign.report["status"] = "incomplete"
            campaign.report["integrated_checks_completed_not_approval"] = False
            campaign.report["preservation_error"] = str(error)
            exit_code = 2
        campaign.save()
    if campaign.report["status"] != "checks-completed-awaiting-quality":
        exit_code = 2
    print(json.dumps({"status": campaign.report["status"], "result": str(campaign.output / "result.json"),
                      "material_approval": False, "exit": exit_code}, sort_keys=True))
    return exit_code


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (BoundaryError, OSError, ValueError, ET.ParseError) as error:
        print("Not executed/completed: " + str(error), file=sys.stderr)
        sys.exit(2)
