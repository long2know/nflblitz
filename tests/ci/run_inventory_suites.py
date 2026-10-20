#!/usr/bin/env python3
"""Run the fixed asset-free Stage7 suites, with one optional independent replay slot."""
import argparse
import ast
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import subprocess
import sys
import time

from isolated_mutations import run_mutations

ROOT = Path(__file__).resolve().parents[2]
COMMANDS = (
    "python3 -B tests/kick-meter/test_runner.py -q",
    "python3 -B tests/fire-kick-integration/test_runner.py -q",
    "python3 -B -m unittest discover -s tests/fire-producers -p 'test_*.py' -q",
    "python3 -B -m unittest discover -s tests/audio/runtime -p 'test_ci.py' -q",
    "python3 -B tests/movement-callers/test_public_replay.py -q",
    "python3 -B tests/movement-callers/test_replay_generations.py -q",
    "python3 -B tests/movement-callers/test_export_portable_public.py -q",
    "python3 -B tests/source-host-lifecycle/run.py --output tests/source-host-lifecycle/.artifacts/ci-managed --controls",
    "python3 -B tests/impact-dust/test_ci.py",
    "python3 -B tests/impact-dust/run.py --output .squad/.scratch/impact-dust-ci --controls",
    "python3 -B tests/impact-dust/js_controls.py --output .squad/.scratch/impact-dust-js-ci",
    "python3 -B tests/loose-helmet/test_ci.py",
    "python3 -B tests/loose-helmet/run.py --output .squad/.scratch/loose-helmet-ci --controls",
    "python3 -B tests/loose-helmet/js_controls.py --output .squad/.scratch/loose-helmet-js-ci",
)
BINDINGS_SHA256 = "d80e484dd40bfdcbf906e0ae54a0eb9cb464640e9be8a289d3e961f233b4db56"
CASE_SHA256 = (
    "2e8b5aeaa02d78ddec6dd4270e5d9a15c8e715b8cffd437afb37436ad6b3d9cb",
    "0a7c03bc893cfc8d850f1519d05818849131fcdafb7993d8372b97042641b7e8",
    "8dd3f77173898631141e11a730934dc3858a8adb22301d007d922cc57551b3bf",
    "2252e6e159ca6986887c60051a589aee076af4e063aad47af99efe3c3fd711b0",
    "b4eaa0d3153fdc7033e1c91c607e7ad7b1e250b982e953cfa655bfd07bc5acba",
    "d9d3fc014b3f30f727c2d6dfbfd631dd00bbc6362bbe9c05841af0addb0a9a37",
    "76f40bc3f9f66dd531f8cd704c59f6976eb1f15abfa429cbe490a26064b006b2",
    None,
    "b44e37988cb42d0a947e4a39a3bf219703ef446a9bcb277feaa7a2a98330f3ab",
    None, None,
    "dd4e1dd46d246e333f78b6cd74bcdda9629b09956f04ece23afe9e3e841f6323",
    None, None,
)
REPORT_SCHEMAS = {
    7: "source-host-lifecycle-managed/v1",
    9: "source-impact-dust-asset-free/v1",
    10: "source-impact-dust-executed-JS-controls/v1",
    12: "source-loose-helmet-asset-free/v1",
    13: "source-loose-helmet-executed-JS-controls/v1",
}
CONTROL_SHA256 = {
    7: "6cab1b62e5d3e84ab3636dd26c99018fe660b490d98cec139670bfeeab0e381b",
    9: "f76f86f87028fa25ba2556958887d82a904c8aaade6a978d638fdb06334a8f43",
    10: "7ce350aeb2ad4b4185a1607c0075035da5b0f8f360b56d90b2850f51490e2cbf",
    12: "98e4963e575db86caebe774d0d907b101649e6baf2edb8b3e7882a7d1e4ba756",
    13: "e5b92129e54a00859015801c78c229fdf302c3c4ce899bfdd1aad044852942a5",
}
PUBLIC_REPLAY = 4


def require(value, message):
    if not value:
        raise ValueError(message)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def bindings():
    return "\n".join(COMMANDS) + "\n"


def workers():
    value = os.environ.get("BLITZ_CI_MUTATION_WORKERS", "1")
    require(value in ("1", "2"), "BLITZ_CI_MUTATION_WORKERS must be exactly 1 or 2.")
    return int(value)


def validate_bindings(text):
    require(sha(bindings().encode()) == BINDINGS_SHA256 and text == bindings(),
            "Every fixed original suite command must be bound exactly once, unchanged and in order.")


def new_output(path):
    output = path.absolute()
    require(not output.exists() and output.resolve() == output and ROOT / ".squad/.scratch" in output.parents,
            "Use a NEW ignored worktree-local output without symlinked parents.")
    return output


def source_inventory(index):
    argv = shlex.split(COMMANDS[index])
    files = (sorted((ROOT / argv[argv.index("-s") + 1]).glob(argv[argv.index("-p") + 1]))
             if "-m" in argv else [ROOT / next(part for part in argv if part.endswith(".py"))])
    identifiers = []
    for path in files:
        for node in ast.parse(path.read_text()).body:
            if isinstance(node, ast.ClassDef):
                identifiers.extend(path.relative_to(ROOT).as_posix() + "::" + node.name + "." + item.name
                    for item in node.body if isinstance(item, ast.FunctionDef) and item.name.startswith("test_"))
    identifiers.sort()
    if CASE_SHA256[index] is not None:
        require(sha(json.dumps(identifiers, separators=(",", ":")).encode()) == CASE_SHA256[index],
                "The exact original named unittest inventory changed: " + COMMANDS[index])
    return identifiers, {path.relative_to(ROOT).as_posix(): sha(path.read_bytes()) for path in files}


def child_environment(directory):
    temporary = directory / "tmp"
    temporary.mkdir()
    return dict(os.environ, TMPDIR=str(temporary), TMP=str(temporary), TEMP=str(temporary),
                BLITZ_CI_MUTATION_WORKERS="1", BLITZ_CI_INVENTORY_CHILD="1",
                PYTHONDONTWRITEBYTECODE="1", DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1",
                MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_USE_MSBUILD_SERVER="0")


def campaign_controls(index):
    # Read literal identities/behaviors, without executing source-mutating campaign helpers.
    argv = shlex.split(COMMANDS[index])
    path = ROOT / next(part for part in argv if part.endswith(".py"))
    function = next(node for node in ast.parse(path.read_text()).body
                    if isinstance(node, ast.FunctionDef) and
                    node.name == ("controls" if index == 7 else "main" if index == 10 else "mutations"))
    if index == 10:
        value = next(node.value for node in function.body if isinstance(node, ast.Assign) and
                     any(isinstance(target, ast.Name) and target.id == "mutations" for target in node.targets))
    else:
        value = next(node.value for node in function.body if isinstance(node, ast.Return))
    controls = [(ast.literal_eval(row.elts[0]), ast.literal_eval(row.elts[-1])) for row in value.elts]
    require(sha(json.dumps(controls, separators=(",", ":")).encode()) == CONTROL_SHA256[index],
            "The exact original campaign control/behavior inventory changed: " + str(path))
    return controls


def validate_campaign_output(index, path, report):
    error = "Missing/corrupt/failed actual campaign result: " + str(path)
    require(isinstance(report, dict) and report.get("schema") == REPORT_SCHEMAS[index] and
            report.get("status") == "passed", error)
    controls = campaign_controls(index)
    expected = [("baseline", None), *controls, ("final-baseline", None)]
    runs = report.get("runs")
    require(isinstance(runs, list) and all(isinstance(row, dict) for row in runs) and
            [row.get("name") for row in runs] == [name for name, _ in expected],
            error + ": exact baseline/control/final-baseline inventory required")
    for field in ("compiled_causal_controls_killed",) if index == 7 else ("semanticControlsKilled",) if index == 10 else ():
        require(type(report.get(field)) is int and report[field] == len(controls), error + ": " + field)
    for row, (name, behavior) in zip(runs, expected):
        context = error + ": " + name
        control = behavior is not None
        directory = path.parent / name
        log = (path.parent / (name + ".log")) if index in (10, 13) else directory / "run.log"
        text = log.read_text()
        require(bool(text.strip()) and (index == 13 or row.get("output") == text), context + ": execution log")
        exit_field = "run_exit" if index == 7 else "executionExit" if index == 13 else "exit"
        require(type(row.get(exit_field)) is int and row[exit_field] == int(control), context + ": execution exit")
        if index in (7, 9, 12):
            compile_field = "compile_exit" if index == 7 else "compile"
            command_field = "compile_command" if index == 7 else "compileCommand"
            command = row.get(command_field)
            require(type(row.get(compile_field)) is int and row[compile_field] == 0 and
                    (directory / "build.log").read_bytes() == b"" and
                    (index != 7 or row.get("compile_diagnostics") == ""), context + ": zero compile diagnostics required")
            require(isinstance(command, list) and len(command) == 4 and command[0] == "dotnet" and
                    isinstance(command[1], str) and command[2:] == ["-noconfig", "@" + str(directory / "compile.rsp")],
                    context + ": compile command")
            assembly = directory / {7: "HostLifecycle.Checks.dll", 9: "Dust.Checks.dll", 12: "Helmet.Checks.dll"}[index]
            require(row.get("assembly_sha256" if index == 7 else "assemblySha256") == sha(assembly.read_bytes()),
                    context + ": assembly hash")
        else:
            parse_field = "parsedExit" if index == 10 else "parseExit"
            require(type(row.get(parse_field)) is int and row[parse_field] == 0, context + ": parse exit")
            require(row.get("sourceSha256" if index == 10 else "sha256") ==
                    sha((path.parent / (name + ".mjs")).read_bytes()), context + ": parsed source hash")
            pairs = re.findall(r"^(?:# |ℹ )?(tests|pass|fail|cancelled|skipped|todo) (\d+)\s*$", text, re.M)
            counts = {key: int(value) for key, value in pairs}
            total = 3 if index == 10 else 7
            require(len(pairs) == len(counts) == 6 and counts["tests"] == total and
                    counts["pass"] + counts["fail"] == total and bool(counts["fail"]) == control and
                    all(counts[key] == 0 for key in ("cancelled", "skipped", "todo")),
                    context + ": complete executed JS test summary required")
        if index == 7:
            require(type(row.get("cases")) is int and row["cases"] == 11 and
                    type(row.get("assertions")) is int and row["assertions"] > 0 and
                    type(row.get("failures")) is int and 0 <= row["failures"] <= 11 and
                    bool(row["failures"]) == control and
                    re.findall(r"^RESULT cases=(\d+) assertions=(\d+) failures=(\d+)$", text, re.M) ==
                    [(str(row["cases"]), str(row["assertions"]), str(row["failures"]))] and
                    row.get("run_command") == ["dotnet", str(assembly)], context + ": managed case summary")
            if control:
                require(row.get("required_named_behavior_failure") == behavior and
                        row.get("killed_by_named_behavior") is True and behavior in text, context + ": named behavior")
        elif index == 9:
            summaries = re.findall(r"^DUST RESULT assertions=(\d+) failures=(\d+)$", text, re.M)
            require(len(summaries) == 1 and int(summaries[0][0]) > 0 and summaries[0][1] == str(int(control)),
                    context + ": managed assertion summary")
            if control:
                require(row.get("requiredNamedFailure") == behavior and "DUST ASSERTION: " + behavior in text,
                        context + ": named behavior")
        elif index == 12:
            summary = row.get("summary")
            require(isinstance(summary, dict) and summary == json.loads(text) and
                    summary.get("status") == ("failed" if control else "passed") and
                    type(summary.get("assertions")) is int and summary["assertions"] > 0,
                    context + ": managed status/assertion summary")
            if control:
                require(row.get("requiredNamedFailure") == behavior and
                        isinstance(summary.get("error"), str) and
                        "HELMET ASSERTION: " + behavior in summary["error"], context + ": named behavior")
        else:
            if index == 13:
                require("expectedFailedTest" in row and row["expectedFailedTest"] == behavior, context + ": named test")
            if control:
                require(behavior in text and ("AssertionError" in text or "ERR_ASSERTION" in text),
                        context + ": named behavior")


def validate_child_output(index, text, identities):
    require(bool(text.strip()), "Missing child output: " + COMMANDS[index])
    if CASE_SHA256[index] is not None:
        summaries = re.findall(r"^Ran (\d+) tests? in [0-9.]+s$", text, re.M)
        require(summaries == [str(len(identities))] and text.rstrip().endswith("\nOK"),
                "Missing/corrupt/incomplete unittest result (skips are not passing): " + COMMANDS[index])
        return None
    argv = shlex.split(COMMANDS[index])
    path = ROOT / argv[argv.index("--output") + 1] / "result.json"
    raw = path.read_bytes()
    report = json.loads(raw)
    validate_campaign_output(index, path, report)
    return dict(path=str(path), sha256=sha(raw), schema=report["schema"])


def execute_child(index, directory):
    directory.mkdir()
    identities, sources = source_inventory(index)
    argv = shlex.split(COMMANDS[index])
    env = child_environment(directory)
    row = dict(index=index, argv=argv, status="failed", childWorkers="1", temporary=env["TMPDIR"],
               caseIds=identities, sourceHashes=sources, started=time.monotonic())
    try:
        child = subprocess.Popen(argv, cwd=ROOT, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        row["pid"] = child.pid
        try:
            raw, _ = child.communicate()
        finally:
            child.wait()
        row["exit"] = child.returncode
        text = raw.decode("utf-8")
        (directory / "run.log").write_text(text)
        row["logSha256"] = sha(text.encode())
        require(type(child.returncode) is int and child.returncode == 0,
                "Suite child failed/crashed: " + COMMANDS[index] + " exit=" + str(child.returncode))
        row["campaign"] = validate_child_output(index, text, identities)
        require(source_inventory(index) == (identities, sources), "Suite source/case inventory changed during execution.")
        row["status"] = "passed"
    except Exception as error:
        row["error"] = str(error)
        raise
    finally:
        row["finished"] = time.monotonic()
        (directory / "result.json").write_text(json.dumps(row, indent=2) + "\n")
    return row


def execute_plan(execute):
    count = workers()
    if count == 1:
        return run_mutations(tuple((str(i), i) for i in range(len(COMMANDS))), lambda case: execute(case[1]))
    # Replay writes only its UUID fixtures; all other suite/build/cache users remain in one serial chain.
    chains = (("public-replay", (PUBLIC_REPLAY,)),
              ("remaining-serial", tuple(i for i in range(len(COMMANDS)) if i != PUBLIC_REPLAY)))
    rows = run_mutations(chains, lambda chain: [execute(index) for index in chain[1]])
    return sorted((row for chain in rows for row in chain), key=lambda row: row["index"])


def read_results(output):
    rows = []
    for index, command in enumerate(COMMANDS):
        directory = output / str(index)
        row = json.loads((directory / "result.json").read_text())
        require(row.get("index") == index and row.get("argv") == shlex.split(command) and
                row.get("status") == "passed" and type(row.get("exit")) is int and row["exit"] == 0 and
                row.get("childWorkers") == "1" and row.get("temporary") == str(directory / "tmp"),
                "Missing/corrupt/substituted/failed suite receipt: " + command)
        identities, sources = source_inventory(index)
        require(row.get("caseIds") == identities and row.get("sourceHashes") == sources,
                "Suite source/case receipt differs from the original inventory.")
        text = (directory / "run.log").read_text()
        require(row.get("logSha256") == sha(text.encode()), "Missing/corrupt child log.")
        require(row.get("campaign") == validate_child_output(index, text, identities), "Campaign result changed.")
        rows.append(row)
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = None
    report = dict(schema="stage7-inventory-suites/v1", status="failed", command=list(sys.argv), runs=[])
    started = time.monotonic()
    try:
        require(not os.environ.get("BLITZ_CI_INVENTORY_CHILD"), "Nested inventory-suite scheduling is forbidden.")
        report["workers"] = workers()
        validate_bindings(sys.stdin.read())
        output = new_output(args.output)
        output.mkdir(parents=True)
        execute_plan(lambda index: execute_child(index, output / str(index)))
        report["runs"] = read_results(output)
        require([row["index"] for row in report["runs"]] == list(range(len(COMMANDS))),
                "Every original suite must execute once; no missing, duplicate or reordered receipts.")
        report["status"] = "passed"
    except Exception as error:
        report["error"] = str(error)
        print("INVENTORY SUITES FAILED: " + str(error), file=sys.stderr)
    finally:
        report["seconds"] = round(time.monotonic() - started, 6)
        if output is not None and output.exists():
            if report["status"] != "passed":
                for index in range(len(COMMANDS)):
                    path = output / str(index) / "result.json"
                    if path.is_file():
                        try:
                            report["runs"].append(json.loads(path.read_text()))
                        except (OSError, ValueError) as error:
                            report["runs"].append(dict(index=index, status="corrupt", error=str(error)))
                report["missing"] = [index for index in range(len(COMMANDS))
                                     if not (output / str(index) / "result.json").is_file()]
            (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")
    if report["status"] == "passed":
        for row in report["runs"]:
            print("suite " + str(row["index"]) + ": " + shlex.join(row["argv"]) + " passed", flush=True)
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
