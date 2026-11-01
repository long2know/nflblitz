#!/usr/bin/env python3
"""Prepared explicit-release runner. No project imports, discovery, restore or Game execution."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

WT = Path(__file__).resolve().parents[2]
TEST = WT / "tests/source-instructional-admission"
META = WT / ".squad/.scratch/source-following-active-p1/metadata/source-instruction-original-admission-01/implementation-01"
OUT = TEST / "validation-output-implementation-01"
DISPATCHER = "Assets/Scripts/Gameplay/SourceInstructionProcess.cs"
SOURCE = "Assets/Scripts/Gameplay/GameSimulation.Source.cs"
INSTRUCTIONS = "Assets/Scripts/Gameplay/GameSimulation.Instructions.cs"
POSSESSION = "Assets/Scripts/Gameplay/GameSimulation.Possession.cs"
FIRE = "Assets/Scripts/Gameplay/GameSimulation.FireSpfx.cs"

CASES = [
    "caller-context-and-legacy-manual", "flag-and-default-arm-short-circuit",
    "strict-experience-and-active-participation", "four-slot-source-order-and-live-status",
    "producer-deferred-entry-and-opaque-suffix", "producer-to-selection-to-final-delay",
    "initialization-and-fresh-dispatcher-reset", "shared-resource-pid-and-serial-restrictions",
    "provider-faults-are-not-progress", "producer-fault-is-not-completion",
    "admission-reentry-is-rejected", "coherent-backing-and-required-suffix",
]

MUTATIONS = [
    {
        "name": "caller-inverted", "case": CASES[0],
        "old": "expectedCaller <= 0 || host.Caller != expectedCaller",
        "new": "expectedCaller <= 0 || host.Caller == expectedCaller",
        "failure": "caller-aware creation must preserve valid executing caller",
    },
    {
        "name": "flag-gate-inverted", "case": CASES[1],
        "old": "binding.State.ReadIdiotBoxesShown() != 1",
        "new": "binding.State.ReadIdiotBoxesShown() == 1",
        "failure": "flag gate suppresses source admission",
    },
    {
        "name": "experience-includes-three", "case": CASES[2],
        "old": "binding.State.ReadGamesPlayed(slot) < 3",
        "new": "binding.State.ReadGamesPlayed(slot) <= 3",
        "failure": "strict experience boundary excludes three",
    },
    {
        "name": "wrong-slot-bit", "case": CASES[3],
        "old": "(binding.State.ReadPlayerStatus() & (1 << slot))",
        "new": "(binding.State.ReadPlayerStatus() & (1 << ((slot + 1) % 4)))",
        "failure": "eligible actual caller creates instructions",
    },
    {
        "name": "producer-entry-two", "case": CASES[4],
        "old": "process.Binding.State.WriteIdiotBoxesShown(1);",
        "new": "process.Binding.State.WriteIdiotBoxesShown(2);",
        "failure": "producer publishes 1 before opaque suffix factory",
    },
    {
        "name": "catch-reserves-one", "case": CASES[4],
        "old": "host.Create(serial, 0, KickoffFunction, 0);",
        "new": "binding.State.WriteIdiotBoxesShown(1);\n                host.Create(serial, 0, KickoffFunction, 0);",
        "failure": "catch creation must not reserve flag 1",
    },
    {
        "name": "human-initialization-one", "case": CASES[6],
        "old": "binding.State.ReadPlayerStatus() != 0 ? 0 : 2",
        "new": "binding.State.ReadPlayerStatus() != 0 ? 1 : 2",
        "failure": "source initialization writes 0 or 2, never 1",
    },
]


def digest(data):
    return hashlib.sha256(data).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def replace_once(text, old, new):
    require(text.count(old) == 1, "mutation must name one exact production occurrence")
    return text.replace(old, new, 1)


def method(text, marker):
    """Lexically bracket a named actual body; not a C# semantic/type checker."""
    start = text.index("{", text.index(marker))
    depth = 0
    i = start
    quote = None
    while i < len(text):
        c = text[i]
        if quote is not None:
            if c == "\\":
                i += 2
                continue
            if c == quote:
                quote = None
        elif text.startswith("//", i):
            i = text.index("\n", i)
            continue
        elif text.startswith("/*", i):
            i = text.index("*/", i + 2) + 2
            continue
        elif c in ('"', "'"):
            quote = c
        elif c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
        i += 1
    raise ValueError("unterminated STATIC method selection")


def static_wiring(sources):
    passed = []

    def guard(name, condition):
        require(condition, "STATIC " + name)
        passed.append(name)

    finish = method(sources[SOURCE], "private void FinishSourcePlaySelection(")
    hook = "AdmitSourceInstructionsAtSelectionReturn(instructionEnteringPlayType);"
    account = "Model.AccumulateSourceFourthDownAttemptAtSelectionReturn("
    lineup = "LineUpSourceActors();"
    guard("selection-order", finish.count(hook) == 1
          and finish.index(account) < finish.index(hook) < finish.index(lineup))
    capture = "SourcePlayType instructionEnteringPlayType = Model.SourceRules.PlayType;"
    guard("entering-play-type", capture in finish
          and finish.index(capture) < finish.index("SourceFireSelectionExit();")
          and finish.index(hook) < finish.index("SourcePlayType type ="))
    admission = method(sources[INSTRUCTIONS], "private void AdmitSourceInstructionsAtSelectionReturn(")
    guard("active-game-caller", "sourceInstructionDispatcher.AdmitAtPlaySelectionReturn(" in admission
          and "GameProcess, () => checked(sourceFireNextSerial + 1)" in admission
          and "enteringPlayType != SourcePlayType.ExtraPoint && enteringPlayType != SourcePlayType.Kickoff" in admission
          and "if (serial.HasValue) sourceFireNextSerial = serial.Value;" in admission)
    catch = method(sources[SOURCE], "private void TrySourceCatch(")
    catch_hook = "AdmitSourceKickoffInstructionProducer(actor);"
    guard("kickoff-catch-hook", catch.count(catch_hook) == 1
          and catch.index("Model.CaptureSourceKickReturnCatch(offense);") < catch.index(catch_hook)
          < catch.index("if (field < 2"))
    producer = method(sources[INSTRUCTIONS], "private void AdmitSourceKickoffInstructionProducer(")
    guard("actor-caller-contract", "Model.BallKind == SourceBallKind.Kickoff, 2 + actor.Id," in producer
          and "sourceInstructionDispatcher.AdmitKickoffFromCatch(" in producer
          and "WriteIdiotBoxesShown" not in producer)
    initialize = method(sources[POSSESSION], "private void InitializeSourceProcesses(")
    reset = method(sources[INSTRUCTIONS], "private void InitializeSourceInstructionAdmission(")
    guard("restart-binding", "InitializeSourceInstructionAdmission();" in initialize
          and initialize.index("new SourceInstructionDispatcher(host)") < initialize.index("InitializeSourceInstructionAdmission();")
          and "sourceInstructionDispatcher.InitializeAdmission(sourceInstructionAdmission);" in reset)
    registry = method(sources[INSTRUCTIONS], "private static bool IsSourceMessageFunction(")
    slots = method(sources[POSSESSION], "private List<SourceFireWakeSlot> SourceMessageSlots(")
    guard("shared-message-registration", "SourceInstructionDispatcher.Function" in registry
          and "SourceInstructionDispatcher.KickoffFunction" in registry
          and "IsSourceMessageFunction(p.Function)" in slots
          and "IsSourceMessageFunction(slot.Function)" in sources[FIRE])
    step = method(sources[SOURCE], "private void SourceStep(")
    guard("fire-nonfire-actual-game-context",
          "foreach (SourceFireWakeSlot slot in SourceMessageSlots()) DispatchSourceMessage(slot);" in step
          and "if (EnterSourceProcess(GameProcess))" in step
          and step.index("command = SourceGameProc(command);") < step.index("CompleteSourceProcess();")
          and "if (EnterSourceProcess(GameProcess))" in sources[FIRE]
          and "applied = SourceGameProc(command);" in sources[FIRE]
          and "CompleteSourceProcess();" in sources[FIRE])
    inert = "if (sourceInstructionAdmission == null) return;"
    guard("no-binding-and-fault-boundary", all(inert in body for body in (admission, producer, reset))
          and all("FailSourceInstructionAdmission(error)" in body for body in (admission, producer, reset))
          and 'Model.SourceAnimationFailure = "Source instructional admission: " + error.Message;' in sources[INSTRUCTIONS])
    return passed


def static_variants(sources):
    hook = "            AdmitSourceInstructionsAtSelectionReturn(instructionEnteringPlayType);\n"
    no_hook = dict(sources)
    no_hook[SOURCE] = replace_once(no_hook[SOURCE], hook, "")
    yield "lost-selection-hook", no_hook, "selection-order", SOURCE
    late = dict(sources)
    before = method(late[SOURCE], "private void FinishSourcePlaySelection(")
    after = replace_once(before, hook, "")
    after = replace_once(after, "            LineUpSourceActors();\n",
                         "            LineUpSourceActors();\n" + hook)
    late[SOURCE] = replace_once(late[SOURCE], before, after)
    yield "after-lineup-hook", late, "selection-order", SOURCE
    registry = dict(sources)
    registry[INSTRUCTIONS] = replace_once(
        registry[INSTRUCTIONS], "\n                || function == SourceInstructionDispatcher.KickoffFunction", "")
    yield "lost-producer-registration", registry, "shared-message-registration", INSTRUCTIONS
    reset = dict(sources)
    reset[POSSESSION] = replace_once(reset[POSSESSION], "            InitializeSourceInstructionAdmission();\n", "")
    yield "lost-restart-reset", reset, "restart-binding", POSSESSION
    unbound = dict(sources)
    before = method(unbound[INSTRUCTIONS], "private void AdmitSourceInstructionsAtSelectionReturn(")
    after = replace_once(before, "            if (sourceInstructionAdmission == null) return;\n", "")
    unbound[INSTRUCTIONS] = replace_once(unbound[INSTRUCTIONS], before, after)
    yield "lost-no-binding-guard", unbound, "no-binding-and-fault-boundary", INSTRUCTIONS


def verify_row(row, base=WT):
    raw = (base / row["path"]).read_bytes()
    require(("bytes" not in row or len(raw) == row["bytes"])
            and digest(raw) == row["sha256"], "input pin drift: " + row["path"])


def main():
    require(sys.argv[1:] == ["--parent-authorized"], "Requires a new explicit parent execution release.")
    require(Path.cwd().resolve() == WT, "Run from the exact admitted worktree.")
    proposal_path = META / "validation-proposal.json"
    proposal_bytes = proposal_path.read_bytes()
    proposal = json.loads(proposal_bytes)
    release = json.loads((META / "parent-execution-release.json").read_bytes())
    require(release["schema"] == "source-instruction-admission-execution-release-1", "wrong execution release")
    require(release["proposal_sha256"] == digest(proposal_bytes), "release does not admit this proposal")
    require(release["authorize"] == ["component", "static", "core-typecheck"], "wrong execution scope")
    require(release["bounds"] == proposal["requested_bounds"], "execution bounds differ")
    require(not OUT.exists(), "fresh validation footprint already consumed")
    manifest_bytes = (META / "change-manifest.json").read_bytes()
    require(digest(manifest_bytes) == proposal["manifest_sha256"], "manifest not admitted by proposal")
    manifest = json.loads(manifest_bytes)
    for row in manifest["authored_validation_inputs"]:
        verify_row(row)
    core_selection = manifest["core87_selection"]
    verify_row(core_selection["metadata"])
    core_plan = json.loads((WT / core_selection["metadata"]["path"]).read_bytes())
    core_paths = core_plan["argv"][10:]
    core_rows = []
    for path in core_paths:
        raw = (WT / path).read_bytes()
        core_rows.append([path, len(raw), digest(raw)])
    encoded_rows = json.dumps(core_rows, ensure_ascii=True, separators=(",", ":")).encode("ascii")
    require(len(core_rows) == 87 and sum(r[1] for r in core_rows) == core_selection["after_bytes"]
            and digest(encoded_rows) == core_selection["after_ordered_sha256"], "actual Core87 source pin drift")
    for row in proposal["static_source_roster"]:
        verify_row(row)
    reference = proposal["reference_roster"][0]
    require(release["reference_roster"] == proposal["reference_roster"], "reference is not separately admitted")
    verify_row(reference, Path("/"))
    tools = proposal["tool_roster"]
    admitted_tools = release["tool_roster"]
    require([r["path"] for r in admitted_tools] == [r["path"] for r in tools], "tool roster differs")
    for row in admitted_tools:
        require(len(row["sha256"]) == 64 and row["bytes"] > 0, "genuine tool identity required")
        verify_row(row, Path("/"))
    require(release["framework"] == proposal["framework"], "framework/runtime availability is not admitted")
    OUT.mkdir()
    process_work = OUT / "process-work"
    process_work.mkdir()
    environment = dict(os.environ)
    environment.update(proposal["environment_overrides"])
    environment.update({key: str(process_work) for key in
                        ("TMPDIR", "TMP", "TEMP", "DOTNET_CLI_HOME", "DOTNET_BUNDLE_EXTRACT_BASE_DIR")})
    result = {"scope": "COMPONENT_AND_STATIC_AND_LIBRARY_TYPECHECK_NOT_GAME",
              "commands": [], "static": {}, "success": False}

    def write_json(path, value):
        path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")

    def command(argv, directory, phase, timeout):
        p = subprocess.run(argv, cwd=WT, env=environment, stdout=subprocess.PIPE,
                           stderr=subprocess.PIPE, timeout=timeout, check=False)
        cap = proposal["requested_bounds"]["capture_bytes_per_stream"]
        for kind, data in (("stdout", p.stdout), ("stderr", p.stderr)):
            (directory / (phase + "." + kind + ".txt")).write_bytes(data[:cap])
        (directory / (phase + ".exit.txt")).write_text(str(p.returncode) + "\n", encoding="ascii")
        result["commands"].append({"argv": argv, "phase": phase, "exit": p.returncode,
                                   "stdout_bytes": len(p.stdout), "stderr_bytes": len(p.stderr),
                                   "capture_cap": cap, "truncated": len(p.stdout) > cap or len(p.stderr) > cap})
        return p

    def compiler(sources, output, target):
        return [tools[0]["path"], tools[1]["path"], "/nologo", "/noconfig", "/nostdlib+",
                "/langversion:7.3", "/warnaserror+", "/deterministic+", "/target:" + target,
                "/out:" + str(output.relative_to(WT)), "/reference:" + reference["path"]] + sources

    try:
        static_sources = {r["path"]: (WT / r["path"]).read_text(encoding="utf-8")
                          for r in proposal["static_source_roster"]}
        result["static"]["baseline_guards"] = static_wiring(static_sources)
        result["static"]["mutants"] = []
        for name, variant, expected, path in static_variants(static_sources):
            directory = OUT / "static" / name
            directory.mkdir(parents=True)
            (directory / Path(path).name).write_text(variant[path], encoding="utf-8")
            try:
                static_wiring(variant)
            except ValueError as error:
                require(str(error) == "STATIC " + expected, "wrong STATIC mutation failure")
                result["static"]["mutants"].append({"name": name, "classification": "STATIC_NOT_GAME",
                                                    "guard": expected, "rejected": True})
            else:
                raise ValueError("STATIC mutant survived: " + name)
        component = proposal["component_source_paths"]
        for mutation in [None] + MUTATIONS:
            name = "baseline" if mutation is None else mutation["name"]
            directory = OUT / name
            directory.mkdir()
            sources = list(component)
            if mutation is not None:
                changed = replace_once((WT / DISPATCHER).read_text(encoding="utf-8"),
                                       mutation["old"], mutation["new"])
                planned = next(row for row in proposal["production_mutants"] if row["name"] == name)
                require(len(changed.encode("utf-8")) == planned["source_bytes"]
                        and digest(changed.encode("utf-8")) == planned["source_sha256"],
                        "production mutation proposal pin differs: " + name)
                copy = directory / "SourceInstructionProcess.cs"
                copy.write_text(changed, encoding="utf-8")
                sources[sources.index(DISPATCHER)] = str(copy.relative_to(WT))
            executable = directory / "instruction-admission.dll"
            p = command(compiler(sources, executable, "exe"), directory, "compile", 60)
            require(p.returncode == 0 and not p.stdout and not p.stderr, "non-clean component compile: " + name)
            write_json(directory / "instruction-admission.runtimeconfig.json", {
                "runtimeOptions": {"tfm": "net10.0", "rollForward": "Disable",
                                   "framework": proposal["framework"]}})
            argv = [tools[0]["path"], str(executable.relative_to(WT))]
            if mutation is not None:
                argv += ["--case", mutation["case"]]
            p = command(argv, directory, "run", 30)
            output = p.stdout.decode("utf-8", errors="strict")
            if mutation is None:
                expected = ["PASS " + name for name in CASES] + ["SUMMARY component_cases=12 failures=0 Game=0 Unity=0"]
                require(p.returncode == 0 and not p.stderr and output.splitlines() == expected,
                        "baseline named production component cases differ")
            else:
                require(p.returncode == 1 and not p.stderr
                        and output.startswith("FAIL " + mutation["case"] + ": ")
                        and mutation["failure"] in output and "ERROR " not in output,
                        "production semantic mutant did not fail its intended assertion: " + name)
        directory = OUT / "core-typecheck"
        directory.mkdir()
        library = directory / "SourceInstructionAdmission.Core87.dll"
        core_argv = compiler(core_paths, library, "library")
        require(digest(json.dumps(core_argv, ensure_ascii=True, separators=(",", ":")).encode("ascii"))
                == proposal["expanded_core_argv_sha256"], "expanded full-Core argv differs")
        p = command(core_argv,
                    directory, "compile", 120)
        require(p.returncode == 0 and not p.stdout and not p.stderr, "non-clean actual Core87 library typecheck")
        result["core_library_executed"] = False
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
