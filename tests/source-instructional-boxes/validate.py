"""Prepared validation only. Invocation requires a new, explicit parent release."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys


ROOT = Path(".squad/.scratch/source-next-default-audibles-45f")
HERE = ROOT / "tests/source-instructional-boxes"
OUT = HERE / "validation-output"
METADATA = ROOT / ".squad/.scratch/source-following-active-p1/metadata/source-instruction-production-port-01"
DOTNET = "/home/long2know/.dotnet/dotnet"
CSC = "/home/long2know/.dotnet/sdk/10.0.100/Roslyn/bincore/csc.dll"
REFERENCE = "/dev/shm/ap.bweV08/t18/pkg/netstandard.library.ref/2.1.0/ref/netstandard2.1/netstandard.dll"
SOURCES = (
    "Assets/Scripts/Contracts/SourcePortableProcessContracts.cs",
    "Assets/Scripts/Contracts/SourceInstructionContracts.cs",
    "Assets/Scripts/Gameplay/SourcePortableProcessHost.cs",
    "Assets/Scripts/Gameplay/SourceAnyButtonWait.cs",
    "Assets/Scripts/Gameplay/SourceInstructionProcess.cs",
    "tests/source-instructional-boxes/Program.cs",
)
WIRING = (
    "Assets/Scripts/Gameplay/GameSimulation.Instructions.cs",
    "Assets/Scripts/Gameplay/GameSimulation.Possession.cs",
    "Assets/Scripts/Gameplay/GameSimulation.FireSpfx.cs",
    "Assets/Scripts/Gameplay/GameSimulation.Source.cs",
)
WAIT = SOURCES[3]
PROCESS = SOURCES[4]
MUTATIONS = (
    ("poll-in-delay", WAIT, "                delayRemaining--;",
     "                inputs.ReadPlayerSwitches();\n                delayRemaining--;",
     "no polling in initial delay"),
    ("mask-returned-sample", WAIT, "                    result = sample;",
     "                    result = sample & allAbcdMask;", "entire accepted sample"),
    ("skip-hints", PROCESS, "if (pageIndex < 3)", "if (pageIndex < 2)",
     "source order: text-kill:Hints before plate:delete"),
    ("early-completion", PROCESS, "                            stage = SourceInstructionStage.FinalDelay;",
     "                            PublishFlag(2);\n                            stage = SourceInstructionStage.FinalDelay;",
     "cleanup/resume is not completion"),
    ("omit-final-rejected-sleep", WAIT,
     "                result = 0;\n                sleep(1);\n                return false;",
     "                result = 0;\n                Completed = true;\n                return true;",
     "rejection always yields"),
    ("invert-tick-gate", WAIT, "if (inputs.ReadShowPlyselIdiotBox() == 0)",
     "if (inputs.ReadShowPlyselIdiotBox() != 0)", "delay/accept call trace"),
)


def replace_once(text, before, after):
    if text.count(before) != 1:
        raise ValueError("production mutation anchor must occur exactly once")
    return text.replace(before, after, 1)


def static_wiring(texts):
    instruction, possession, fire, source = (texts[name] for name in WIRING)
    guards = []

    def require(name, condition):
        if not condition:
            raise ValueError(name)
        guards.append(name)

    require("explicit-required-invocation",
            "public long InvokeSourceInstructions(SourceInstructionInvocation invocation)" in instruction
            and "if (!Model.SourceMode || sourceInstructionDispatcher == null)" in instruction
            and "sourceInstructionDispatcher.Admit(serial, invocation);" in instruction)
    require("dispatcher-reset-with-host",
            "sourceInstructionDispatcher = new SourceInstructionDispatcher(host);" in possession)
    require("registry-enumerates-instructions",
            "|| function == SourceInstructionDispatcher.Function;" in instruction
            and "if (IsSourceMessageFunction(p.Function))" in possession)
    start = possession.index("private void DispatchSourceMessage(")
    end = possession.index("if (!EnterSourceProcess(", start)
    prefix = possession[start:end]
    require("actual-dispatcher-invocation",
            "sourceInstructionDispatcher.Dispatch(slot.ProcessSerial," in prefix
            and "function => BeginSourceProcess(function, -1, 0)" in prefix)
    require("nonfire-shared-dispatch",
            "foreach (SourceFireWakeSlot slot in SourceMessageSlots()) DispatchSourceMessage(slot);" in source)
    require("fire-shared-dispatch",
            "IsSourceMessageFunction(ordered[index].Function)" in fire
            and "if (IsSourceMessageFunction(slot.Function))\n                        DispatchSourceMessage(slot);" in fire)
    require("no-invented-automatic-initial-qb-trigger",
            all("InvokeSourceInstructions(" not in text for text in (possession, fire, source)))
    return guards


def command(kind, folder, argv, timeout, report):
    completed = subprocess.run(argv, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout, check=False)
    row = {"kind": kind, "argv": argv, "exit": completed.returncode}
    for label, payload in (("stdout", completed.stdout), ("stderr", completed.stderr)):
        destination = folder / (kind + "." + label + ".txt")
        destination.write_bytes(payload[:5000])
        row[label] = str(destination)
        row[label + "_truncated"] = len(payload) > 5000
    (folder / (kind + ".exit.txt")).write_text(str(completed.returncode) + "\n")
    report["commands"].append(row)
    return completed


def compile_and_run(name, replacements, report):
    folder = OUT / name
    folder.mkdir()
    paths = []
    for source in SOURCES:
        if source in replacements:
            destination = folder / Path(source).name
            destination.write_text(replacements[source], encoding="utf-8")
            paths.append(str(destination))
        else:
            paths.append(str(ROOT / source))
    assembly = folder / "instruction-regressions.dll"
    (folder / "instruction-regressions.runtimeconfig.json").write_text(json.dumps({
        "runtimeOptions": {"tfm": "net10.0", "rollForward": "Disable",
                           "framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"}}
    }, separators=(",", ":")) + "\n", encoding="utf-8")
    argv = [DOTNET, CSC, "/noconfig", "/nostdlib+", "/langversion:7.3",
            "/target:exe", "/deterministic+", "/out:" + str(assembly), "/reference:" + REFERENCE] + paths
    compile_result = command("compile", folder, argv, 60, report)
    if compile_result.returncode != 0:
        raise ValueError(name + ": compiler failure is not a killed mutation or a behavioral result")
    return command("run", folder, [DOTNET, str(assembly)], 30, report)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--parent-authorized", action="store_true", required=True,
                        help="Only supply after a NEW parent release, including reference/runtime admission.")
    parser.parse_args()
    if OUT.exists():
        raise ValueError("Reserved validation-output already exists; do not reuse consumed footprints.")
    manifest = json.loads((METADATA / "change-manifest.json").read_text())
    expected = {entry["path"]: entry["after_sha256"] for entry in manifest["files"]}
    for name in SOURCES + WIRING + ("tests/source-instructional-boxes/validate.py",):
        if hashlib.sha256((ROOT / name).read_bytes()).hexdigest() != expected[name]:
            raise ValueError("Parent-visible production/test pin changed: " + name)
    OUT.mkdir()
    report = {"status": "incomplete", "commands": [], "behavioral_cases_passed": 0, "static_wiring": [], "static_mutations": [],
              "production_mutations": [], "reference": REFERENCE,
              "scope": "conditional portable program only; no Game/Core/Unity execution"}
    exit_code = 1
    try:
        wiring = {name: (ROOT / name).read_text() for name in WIRING}
        report["static_wiring"] = static_wiring(wiring)
        wire_mutations = (
            ("remove-instruction-registry", WIRING[0],
             "|| function == SourceInstructionDispatcher.Function;", ";", "registry-enumerates-instructions"),
            ("disconnect-dispatcher", WIRING[1],
             "sourceInstructionDispatcher.Dispatch(slot.ProcessSerial,", "DisconnectedDispatcher(slot.ProcessSerial,",
             "actual-dispatcher-invocation"),
            ("disconnect-explicit-invocation", WIRING[0],
             "sourceInstructionDispatcher.Admit(serial, invocation);", "DisconnectedAdmission(serial, invocation);",
             "explicit-required-invocation"),
        )
        for name, path, before, after, wanted in wire_mutations:
            changed = dict(wiring)
            changed[path] = replace_once(changed[path], before, after)
            rejected = None
            try:
                static_wiring(changed)
            except ValueError as failure:
                rejected = str(failure)
            if rejected != wanted:
                raise ValueError(name + ": static wiring mutant survived or failed the wrong guard")
            report["static_mutations"].append({"name": name, "rejected_by": wanted,
                                               "mutated_production_sha256": hashlib.sha256(changed[path].encode()).hexdigest()})
        baseline = compile_and_run("baseline", {}, report)
        text = baseline.stdout.decode("utf-8", errors="replace")
        if baseline.returncode != 0 or "PASS behavioral-cases=11" not in text or text.count("PASS ") != 12:
            raise ValueError("Baseline did not prove all eleven behavioral cases")
        report["behavioral_cases_passed"] = 11
        for name, path, before, after, wanted in MUTATIONS:
            mutated = replace_once((ROOT / path).read_text(), before, after)
            completed = compile_and_run(name, {path: mutated}, report)
            diagnostic = completed.stderr.decode("utf-8", errors="replace")
            if completed.returncode != 1 or "FAIL " not in diagnostic or wanted not in diagnostic:
                raise ValueError(name + ": production mutant survived or failed for the wrong reason")
            report["production_mutations"].append({"name": name, "source": path, "rejected_by": wanted,
                                                   "mutated_production_sha256": hashlib.sha256(mutated.encode()).hexdigest()})
        report["status"] = "passed"
        exit_code = 0
    except ValueError as failure:
        report["error"] = str(failure)
    except (OSError, subprocess.TimeoutExpired) as failure:
        report["error"] = str(failure)
        exit_code = 2
    finally:
        if exit_code:
            report["status"] = "failed"
            if "error" not in report:
                report["error"] = str(sys.exc_info()[1])
        (OUT / "result.json").write_text(json.dumps(report, indent=2) + "\n")
        (OUT / "validation.exit.txt").write_text(str(exit_code) + "\n")
    print(json.dumps({"status": report["status"], "behavioral_cases_passed": report["behavioral_cases_passed"],
                      "production_mutants_killed": len(report["production_mutations"]),
                      "static_wiring_guards": len(report["static_wiring"]),
                      "static_wiring_mutants": len(report["static_mutations"])}))
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
