#!/usr/bin/env python3
"""Held runner: explicit release only; real components, STATIC Game, compile-only Core87."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

WT = Path(__file__).resolve().parents[2]
TEST = WT / "tests/source-kickoff-instruction-coordinator"
META = WT / ".squad/.scratch/source-following-active-p1/metadata/source-instruction-original-admission-01/producer-coordinator-02/validation-fix-02"
OUT = TEST / "validation-output-producer-coordinator-02-fix-02"
DISPATCHER = "Assets/Scripts/Gameplay/SourceInstructionProcess.cs"
INSTRUCTIONS = "Assets/Scripts/Gameplay/GameSimulation.Instructions.cs"
POSSESSION = "Assets/Scripts/Gameplay/GameSimulation.Possession.cs"
SOURCE = "Assets/Scripts/Gameplay/GameSimulation.Source.cs"

CASES = [
    "creation-and-deferred-entry", "version-read-after-two-attempts", "child-shape-and-order",
    "suspend-and-specific-resume", "existence-includes-foreign-live-pid", "suspended-alive-and-dead",
    "sleep-return-and-epilogue", "epilogue-live-reads-and-buttons", "child-explicit-external-lifecycle",
    "required-configuration-and-allocator", "child-callback-faults", "parent-provider-and-serial-faults",
    "shared-global-serial-interleaving", "coordinator-to-instruction-final-delay",
    "legacy-external-suffix-and-manual", "parent-fallthrough-not-native-retirement-and-reset",
]

MUTATIONS = [
    {
        "name": "version-read-before-second-child", "case": CASES[1],
        "old": "                CreateKickoffChild(process, serial, 1);\n                if (config.Inputs.ReadFourPlayerVersion() != 0)\n                {",
        "new": "                int fourPlayer = config.Inputs.ReadFourPlayerVersion();\n                CreateKickoffChild(process, serial, 1);\n                if (fourPlayer != 0)\n                {",
        "failure": "four-player read follows first two child creation attempts",
    },
    {
        "name": "wrong-first-child-slot", "case": CASES[2],
        "old": "CreateKickoffChild(process, serial, 0);",
        "new": "CreateKickoffChild(process, serial, 1);",
        "failure": "child slot source order",
    },
    {
        "name": "wrong-child-fourth-argument", "case": CASES[2],
        "old": "childSerial, config.ChildProcessId, slot, 0, 0, 0);",
        "new": "childSerial, config.ChildProcessId, slot, 0, 0, 1);",
        "failure": "child trailing arguments are all zero",
    },
    {
        "name": "wrong-suspension-mask", "case": CASES[3],
        "old": "host.SuspendMultiple(0, 32768);",
        "new": "host.SuspendMultiple(0, 16);",
        "failure": "parent suspends ordinary matching processes",
    },
    {
        "name": "resume-all-instead-of-children", "case": CASES[3],
        "old": "host.ResumeMultiple(config.ChildProcessId, -1);",
        "new": "host.ResumeAll();",
        "failure": "parent suspends ordinary matching processes",
    },
    {
        "name": "existence-with-zero-mask", "case": CASES[4],
        "old": "host.Exists(config.ChildProcessId, -1)",
        "new": "host.Exists(config.ChildProcessId, 0)",
        "failure": "full-PID existence excludes unrelated live neighbors",
    },
    {
        "name": "parent-sleeps-two", "case": CASES[6],
        "old": "                host.Sleep(1);\n                return;\n            }\n            host.Record(\"ResumeAllCall\", serial, 0, 0);",
        "new": "                host.Sleep(2);\n                return;\n            }\n            host.Record(\"ResumeAllCall\", serial, 0, 0);",
        "failure": "parent returns from exactly one sleep tick",
    },
    {
        "name": "epilogue-overwrites-current-buttons", "case": CASES[7],
        "old": "buttons.ReadCurrentButtons(player) | buttonMask",
        "new": "buttonMask",
        "failure": "epilogue OR preserves current button bits",
    },
    {
        "name": "epilogue-caches-old-control-index", "case": CASES[7],
        "old": "                    player = config.Inputs.ReadPlayerControl(slot);\n                    buttons.WriteOldButtons",
        "new": "                    buttons.WriteOldButtons",
        "failure": "epilogue OR preserves old button bits",
    },
    {
        "name": "catch-reserves-flag-one", "case": CASES[0],
        "old": "host.Create(serial, 0, KickoffFunction, 0);",
        "new": "binding.State.WriteIdiotBoxesShown(1);\n                host.Create(serial, 0, KickoffFunction, 0);",
        "failure": "catch creation must not reserve flag 1",
    },
    {
        "name": "child-fallthrough-auto-retires", "case": CASES[8],
        "old": "                    host.Record(\"ExternalChildFallthrough\", serial, process.Invocation.ProcessId, 0);",
        "new": "                    host.Kill(serial);\n                    host.Record(\"ExternalChildFallthrough\", serial, process.Invocation.ProcessId, 0);",
        "failure": "external child fallthrough must not auto-retire",
    },
    {
        "name": "child-commits-previous-serial", "case": CASES[12],
        "old": "kickoffCommitSerial(childSerial);",
        "new": "kickoffCommitSerial(childSerial - 1);",
        "failure": "shared serial coordinator child allocation succeeds",
    },
]


def digest(data):
    return hashlib.sha256(data).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def replace_once(text, old, new):
    require(text.count(old) == 1, "control must target one exact actual-source occurrence")
    return text.replace(old, new, 1)


def method(text, marker):
    """Bracket a literal C# body for STATIC checks, not semantic Game execution."""
    start = text.index("{", text.index(marker))
    depth, index, quote = 0, start, None
    while index < len(text):
        char = text[index]
        if quote is not None:
            if char == "\\":
                index += 2
                continue
            if char == quote:
                quote = None
        elif text.startswith("//", index):
            index = text.index("\n", index)
            continue
        elif text.startswith("/*", index):
            index = text.index("*/", index + 2) + 2
            continue
        elif char in ('"', "'"):
            quote = char
        elif char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
        index += 1
    raise ValueError("unterminated STATIC method")


def static_wiring(sources):
    passed = []

    def guard(name, condition):
        require(condition, "STATIC " + name)
        passed.append(name)

    game = sources[INSTRUCTIONS]
    opt_in = method(game, "public void BindSourceKickoffInstructionCoordinator(")
    bind = method(game, "public void BindSourceInstructionAdmission(")
    guard("complete-opt-in-routes-existing-binding",
          "BindSourceInstructionAdmission(" in opt_in
          and "SourceInstructionAdmissionBinding.WithKickoffCoordinator(invocation, state, coordinator)" in opt_in
          and "InitializeSourceInstructionAdmission(binding);" in bind
          and bind.index("InitializeSourceInstructionAdmission(binding);")
          < bind.index("sourceInstructionAdmission = binding;"))
    allocator = method(game, "private void InitializeSourceInstructionAdmission(SourceInstructionAdmissionBinding binding)")
    guard("allocator-uses-shared-global-counter",
          "sourceInstructionDispatcher.InitializeAdmission(binding," in allocator
          and "() => checked(sourceFireNextSerial + 1), serial => sourceFireNextSerial = serial);" in allocator)
    reset = method(game, "private void InitializeSourceInstructionAdmission()")
    initialize = method(sources[POSSESSION], "private void InitializeSourceProcesses(")
    guard("fresh-dispatcher-rebinds-coordinator",
          "InitializeSourceInstructionAdmission(sourceInstructionAdmission);" in reset
          and "sourceFireNextSerial = 32;" in initialize
          and "InitializeSourceInstructionAdmission();" in initialize
          and initialize.index("new SourceInstructionDispatcher(host)")
          < initialize.index("InitializeSourceInstructionAdmission();"))
    registry = method(game, "private static bool IsSourceMessageFunction(")
    slots = method(sources[POSSESSION], "private List<SourceFireWakeSlot> SourceMessageSlots(")
    guard("child-in-shared-message-registry",
          "SourceInstructionDispatcher.Function" in registry
          and "SourceInstructionDispatcher.KickoffFunction" in registry
          and "SourceInstructionDispatcher.KickoffChildFunction" in registry
          and "IsSourceMessageFunction(p.Function)" in slots)
    dispatch = method(sources[POSSESSION], "private void DispatchSourceMessage(")
    step = method(sources[SOURCE], "private void SourceStep(")
    guard("actual-message-path-reaches-dispatcher",
          "sourceInstructionDispatcher.Dispatch(slot.ProcessSerial," in dispatch
          and "function => BeginSourceProcess(function, -1, 0))) return;" in dispatch
          and "foreach (SourceFireWakeSlot slot in SourceMessageSlots()) DispatchSourceMessage(slot);" in step)
    select = method(game, "private void AdmitSourceInstructionsAtSelectionReturn(")
    catch = method(game, "private void AdmitSourceKickoffInstructionProducer(")
    guard("unbound-helpers-remain-inert",
          all("if (sourceInstructionAdmission == null) return;" in body for body in (select, catch, reset))
          and all("FailSourceInstructionAdmission(error)" in body for body in (select, catch, reset)))
    return passed


def static_variants(sources):
    registry = dict(sources)
    registry[INSTRUCTIONS] = replace_once(
        registry[INSTRUCTIONS], "\n                || function == SourceInstructionDispatcher.KickoffChildFunction", "")
    yield "lost-child-registration", registry, "child-in-shared-message-registry", INSTRUCTIONS
    allocator = dict(sources)
    allocator[INSTRUCTIONS] = replace_once(
        allocator[INSTRUCTIONS], "serial => sourceFireNextSerial = serial", "serial => sourceFireNextSerial = serial - 1")
    yield "lost-shared-serial-commit", allocator, "allocator-uses-shared-global-counter", INSTRUCTIONS
    opt_in = dict(sources)
    before = method(opt_in[INSTRUCTIONS], "public void BindSourceKickoffInstructionCoordinator(")
    opt_in[INSTRUCTIONS] = replace_once(
        opt_in[INSTRUCTIONS], before, "{ throw new InvalidOperationException(\"STATIC control: opt-in disconnected\"); }")
    yield "disconnected-complete-opt-in", opt_in, "complete-opt-in-routes-existing-binding", INSTRUCTIONS
    reset = dict(sources)
    reset[POSSESSION] = replace_once(reset[POSSESSION], "            InitializeSourceInstructionAdmission();\n", "")
    yield "lost-fresh-dispatcher-rebind", reset, "fresh-dispatcher-rebinds-coordinator", POSSESSION
    inert = dict(sources)
    before = method(inert[INSTRUCTIONS], "private void AdmitSourceKickoffInstructionProducer(")
    after = replace_once(before, "            if (sourceInstructionAdmission == null) return;\n", "")
    inert[INSTRUCTIONS] = replace_once(inert[INSTRUCTIONS], before, after)
    yield "lost-unbound-catch-guard", inert, "unbound-helpers-remain-inert", INSTRUCTIONS


def verify_row(row, base=WT):
    raw = (base / row["path"]).read_bytes()
    require(("bytes" not in row or len(raw) == row["bytes"]) and digest(raw) == row["sha256"],
            "input pin drift: " + row["path"])


def compact(value):
    return json.dumps(value, ensure_ascii=True, separators=(",", ":")).encode("ascii")


def main():
    require(sys.argv[1:] == ["--parent-authorized"], "Requires a fresh exact parent execution release.")
    require(Path.cwd().resolve() == WT, "Run from the exact admitted worktree.")
    proposal_bytes = (META / "validation-proposal.json").read_bytes()
    proposal = json.loads(proposal_bytes)
    release = json.loads((META / "parent-execution-release.json").read_bytes())
    require(release["schema"] == "source-kickoff-instruction-coordinator-execution-release-1", "wrong release schema")
    require(release["proposal_sha256"] == digest(proposal_bytes), "release does not admit current proposal")
    require(release["authorize"] == ["component", "static", "core-typecheck"], "wrong execution scope")
    bounds = proposal["requested_bounds"]
    require(release["bounds"] == bounds, "execution bounds differ")
    require(not OUT.exists(), "validation footprint already consumed")
    manifest_bytes = (META / "change-manifest.json").read_bytes()
    require(len(manifest_bytes) == proposal["manifest_bytes"]
            and digest(manifest_bytes) == proposal["manifest_sha256"], "manifest not admitted")
    manifest = json.loads(manifest_bytes)
    for row in manifest["authored_validation_inputs"]:
        verify_row(row)
    for row in proposal["static_source_roster"]:
        verify_row(row)
    selection = manifest["core87_selection"]
    verify_row(selection["metadata"])
    core_metadata = json.loads((WT / selection["metadata"]["path"]).read_bytes())
    core_paths = core_metadata["argv"][10:]
    require(core_paths == proposal["core_typecheck"]["compiler_argv"][11:], "literal Core87 roster differs")
    core_rows = []
    for path in core_paths:
        raw = (WT / path).read_bytes()
        core_rows.append([path, len(raw), digest(raw)])
    require(len(core_rows) == 87 and sum(row[1] for row in core_rows) == selection["after_bytes"]
            and digest(compact(core_rows)) == selection["after_ordered_sha256"], "current actual Core87 pin drift")
    require(CASES == proposal["expected_component_cases"]
            and len(MUTATIONS) == bounds["compiled_production_mutants"], "case/control inventory differs")
    reference = proposal["reference_roster"][0]
    require(release["reference_roster"] == proposal["reference_roster"], "genuine reference not separately admitted")
    tools = proposal["tool_roster"]
    admitted_tools = release["tool_roster"]
    require([row["path"] for row in admitted_tools] == [row["path"] for row in tools], "tool roster differs")
    require(release["framework"] == proposal["framework"], "genuine runtime not admitted")
    # No tool/reference binary is read until its exact fresh release has been checked.
    verify_row(reference, Path("/"))
    for row in admitted_tools:
        require(row["bytes"] > 0 and len(row["sha256"]) == 64, "genuine fresh tool identity required")
        verify_row(row, Path("/"))

    OUT.mkdir()
    process_work = OUT / "process-work"
    process_work.mkdir()
    environment = dict(os.environ)
    environment.update(proposal["environment_overrides"])
    environment.update({key: str(process_work) for key in
                        ("TMPDIR", "TMP", "TEMP", "DOTNET_CLI_HOME", "DOTNET_BUNDLE_EXTRACT_BASE_DIR")})
    result = {
        "scope": "REAL_PRODUCTION_COMPONENT_STATIC_GAME_LIBRARY_TYPECHECK_ONLY",
        "commands": [], "static": {"classification": "STATIC_NOT_GAME"},
        "core_library_executed": False, "success": False,
    }

    def write_json(path, value):
        path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")

    def compiler(sources, output, target):
        return [tools[0]["path"], tools[1]["path"], "/nologo", "/noconfig", "/nostdlib+",
                "/langversion:7.3", "/warnaserror+", "/deterministic+", "/target:" + target,
                "/out:" + str(output.relative_to(WT)), "/reference:" + reference["path"]] + sources

    def command(argv, directory, phase, timeout):
        require(len(result["commands"]) < bounds["compiler_starts"] + bounds["managed_harness_starts"],
                "process bound exhausted")
        timed_out = False
        try:
            process = subprocess.run(argv, cwd=WT, env=environment, stdout=subprocess.PIPE,
                                     stderr=subprocess.PIPE, timeout=timeout, check=False)
            code, stdout, stderr = process.returncode, process.stdout, process.stderr
        except subprocess.TimeoutExpired as error:
            timed_out = True
            code, stdout, stderr = None, error.stdout or b"", error.stderr or b""
        cap = bounds["capture_bytes_per_stream"]
        for name, data in (("stdout", stdout), ("stderr", stderr)):
            (directory / (phase + "." + name + ".txt")).write_bytes(data[:cap])
        (directory / (phase + ".exit.txt")).write_text(
            "TIMEOUT\n" if timed_out else str(code) + "\n", encoding="ascii")
        result["commands"].append({
            "argv": argv, "phase": phase, "exit": code, "timed_out": timed_out,
            "stdout_bytes": len(stdout), "stderr_bytes": len(stderr),
            "capture_cap": cap, "truncated": len(stdout) > cap or len(stderr) > cap,
        })
        require(not timed_out, "bounded command timed out")
        require(len(stdout) <= cap and len(stderr) <= cap, "capture exceeded cap; no overflow recovery")
        return code, stdout, stderr

    try:
        static_sources = {row["path"]: (WT / row["path"]).read_text(encoding="utf-8")
                          for row in proposal["static_source_roster"]}
        result["static"]["baseline_guards"] = static_wiring(static_sources)
        require(result["static"]["baseline_guards"] == proposal["static_expected_guards"], "STATIC guard roster differs")
        result["static"]["mutants"] = []
        for name, variant, expected, path in static_variants(static_sources):
            plan = next(row for row in proposal["static_mutants"] if row["name"] == name)
            raw = variant[path].encode("utf-8")
            require(plan["source"] == path and plan["expected_guard"] == expected
                    and len(raw) == plan["source_bytes"] and digest(raw) == plan["source_sha256"],
                    "STATIC control pin differs")
            directory = OUT / "static" / name
            directory.mkdir(parents=True)
            (directory / Path(path).name).write_bytes(raw)
            try:
                static_wiring(variant)
            except ValueError as error:
                require(str(error) == "STATIC " + expected, "wrong STATIC control failure")
            else:
                raise ValueError("STATIC control survived: " + name)
            result["static"]["mutants"].append({
                "name": name, "guard": expected, "rejected": True, "classification": "STATIC_NOT_GAME",
            })

        for mutation in [None] + MUTATIONS:
            name = "baseline" if mutation is None else mutation["name"]
            plan = next(row for row in proposal["component_commands"] if row["name"] == name)
            directory = OUT / name
            directory.mkdir()
            sources = list(proposal["component_source_paths"])
            if mutation is not None:
                changed = replace_once((WT / DISPATCHER).read_text(encoding="utf-8"),
                                       mutation["old"], mutation["new"]).encode("utf-8")
                require(len(changed) == plan["source_bytes"] and digest(changed) == plan["source_sha256"],
                        "production control pin differs: " + name)
                (directory / "SourceInstructionProcess.cs").write_bytes(changed)
                sources[sources.index(DISPATCHER)] = str((directory / "SourceInstructionProcess.cs").relative_to(WT))
            compiler_argv = compiler(sources, directory / "coordinator.dll", "exe")
            require(digest(compact(compiler_argv)) == plan["compiler_argv_sha256"],
                    "component compiler argv differs: " + name)
            managed_argv = [tools[0]["path"], str((directory / "coordinator.dll").relative_to(WT))]
            if mutation is not None:
                managed_argv += ["--case", mutation["case"]]
            require(digest(compact(managed_argv)) == plan["managed_argv_sha256"],
                    "component managed argv differs: " + name)
            if mutation is None:
                require(compiler_argv == proposal["baseline_compiler_argv"]
                        and managed_argv == proposal["baseline_managed_argv"], "literal baseline argv differs")
            code, stdout, stderr = command(compiler_argv, directory, "compile",
                                           bounds["component_compile_timeout_seconds_each"])
            require(code == 0 and not stdout and not stderr, "non-clean component compile: " + name)
            write_json(directory / "coordinator.runtimeconfig.json", {
                "runtimeOptions": {"tfm": "net10.0", "rollForward": "Disable", "framework": proposal["framework"]},
            })
            code, stdout, stderr = command(managed_argv, directory, "run",
                                           bounds["managed_timeout_seconds_each"])
            text = stdout.decode("utf-8", errors="strict")
            if mutation is None:
                expected = ["PASS " + case for case in CASES] + [
                    "SUMMARY component_cases=16 failures=0 Game=0 Unity=0 native=0"]
                require(code == 0 and not stderr and text.splitlines() == expected, "baseline named cases differ")
            else:
                require(code == 1 and not stderr and text.startswith("FAIL " + mutation["case"] + ": ")
                        and mutation["failure"] in text and "ERROR " not in text,
                        "production mutant missed intended semantic assertion: " + name)
        directory = OUT / "core-typecheck"
        directory.mkdir()
        core_argv = proposal["core_typecheck"]["compiler_argv"]
        require(core_argv == compiler(core_paths, directory / "KickoffCoordinator.Core87.dll", "library")
                and digest(compact(core_argv)) == proposal["core_typecheck"]["argv_sha256"], "Core87 argv pin differs")
        code, stdout, stderr = command(core_argv, directory, "compile", bounds["full_core_compile_timeout_seconds"])
        require(code == 0 and not stdout and not stderr, "non-clean actual Core87 library typecheck")
        require(len(result["commands"]) == bounds["compiler_starts"] + bounds["managed_harness_starts"],
                "successful command inventory differs")
        result["success"] = True
    except Exception as error:
        result["failure"] = type(error).__name__ + ": " + str(error)
    finally:
        write_json(OUT / "result.json", result)
        (OUT / "validation.exit.txt").write_text("0\n" if result["success"] else "1\n", encoding="ascii")
    return 0 if result["success"] else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print("HELD/FAILED " + type(error).__name__ + ": " + str(error), file=sys.stderr)
        sys.exit(2)
