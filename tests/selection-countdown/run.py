#!/usr/bin/env python3
"""Compile the actual Core/cabinet/HUD in isolated trees; original inputs stay private."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import time
import xml.etree.ElementTree as ET
import sys
import importlib.util

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "tests/ci"))
from isolated_mutations import run_mutations

def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


oracle = load("source_selection_timer_oracle", HERE / "oracle.py")
controls_module = load("source_selection_timer_mutations", HERE / "mutations.py")
cases, generate, verified_authority = oracle.cases, oracle.generate, oracle.verified_authority
mutations = controls_module.mutations


def require(value, message):
    if not value:
        raise AssertionError(message)


def argument_parser():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("fixture", "sound", "m1", "impact", "normal", "speech", "hud", "fire", "authority", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--controls", action="store_true", required=True)
    return parser


def main():
    args = argument_parser().parse_args()
    output = args.output.absolute()
    require(not output.exists() and ROOT / ".squad/.scratch" in output.parents,
            "Use a NEW ignored worktree-local output.")
    output.mkdir(parents=True)
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output),
               DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", PYTHONDONTWRITEBYTECODE="1")
    names = set()
    for item in ET.parse(ROOT / "tools/source-player/ProductionSources.props").iter("Compile"):
        names.update(p.relative_to(ROOT).as_posix() for p in ROOT.glob(item.attrib["Include"].replace("$(BlitzSourceRoot)", "")))
    files = {name: (ROOT / name).read_bytes() for name in sorted(names)}
    files["Program.cs"] = (HERE / "Program.cs").read_bytes()
    files["FullKernelInputs.cs"] = (ROOT / "tests/parity/FullKernelInputs.cs").read_bytes()
    report = dict(schema="source-selection-countdown/v1", status="failed", command=list(os.sys.argv),
                  sourceHashes={n: hashlib.sha256(raw).hexdigest() for n, raw in files.items()}, runs=[])
    started = time.monotonic()
    try:
        sdk = subprocess.check_output(["dotnet", "--list-sdks"], env=env, text=True)
        version, prefix = max(re.findall(r"^(\d+\.\d+\.\d+)[^\n]*\[([^\]]+)\]$", sdk, re.M),
                              key=lambda p: tuple(map(int, p[0].split("."))))
        compiler = Path(prefix) / version / "Roslyn/bincore/csc.dll"
        refs = sorted((Path(prefix).parent / "packs/Microsoft.NETCore.App.Ref").glob("10.0.*/ref/net10.0"))[-1]
        verified_authority(args.authority)
        fixtures, controls = required_inventory()
        original_start = time.monotonic()
        original = generate(args.authority, output / "original-C", fixtures)
        report.update(originalCSeconds=round(time.monotonic() - original_start, 3),
                      originalCases=len(fixtures), originalDispatches=sum(c["ticks"] for c in fixtures),
                      nativeSchedulerQualified=False, nativeTutorialExecuted=False, pcmOrDspPlayback=False)
        fixture = output / "cases.json"
        fixture.write_text(json.dumps(fixtures, indent=2) + "\n")

        def execute(label, source_files):
            destination = output / label
            destination.mkdir()
            private_runtime = destination / "runtime"
            private_runtime.mkdir()
            local_env = dict(env, TMPDIR=str(private_runtime), TMP=str(private_runtime), TEMP=str(private_runtime))
            paths = []
            for name, raw in source_files.items():
                path = destination / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(raw)
                paths.append(path)
            assembly = destination / "Selection.Checks.dll"
            options = ["-nologo", "-nostdlib+", "-target:exe", "-langversion:7.3", "-warnaserror+",
                       "-deterministic+", "-out:" + str(assembly), "-main:SelectionCountdownChecks"]
            options += ["-r:" + str(p) for p in sorted(refs.glob("*.dll"))] + [str(p) for p in paths]
            response = destination / "compile.rsp"
            response.write_text("\n".join(json.dumps(v) for v in options) + "\n")
            compile_start = time.monotonic()
            build = subprocess.run(["dotnet", str(compiler), "-noconfig", "@" + str(response)], env=local_env,
                                   capture_output=True, text=True, timeout=180)
            (destination / "compile.log").write_text(build.stdout + build.stderr)
            require(build.returncode == 0 and not build.stdout + build.stderr,
                    "Compiler failure is never a semantic kill: " + build.stdout + build.stderr)
            compile_seconds = round(time.monotonic() - compile_start, 3)
            assembly.with_suffix(".runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
                "tfm": "net10.0", "framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"}}}))
            command = ["dotnet", str(assembly)] + [str(getattr(args, n).absolute()) for n in
                ("fixture", "sound", "m1", "impact", "normal", "speech", "hud")] + [str(destination), str(fixture), str(args.fire.absolute())]
            run_start = time.monotonic()
            run = subprocess.run(command, cwd=ROOT, env=local_env, capture_output=True, text=True, timeout=180)
            text = run.stdout + run.stderr
            row = dict(name=label, compile=build.returncode, exit=run.returncode, executionExit=run.returncode,
                       command=command, output=text, compileSeconds=compile_seconds,
                       executeSeconds=round(time.monotonic() - run_start, 3),
                       assemblySha256=hashlib.sha256(assembly.read_bytes()).hexdigest())
            compared = 0
            for case in fixtures:
                path = destination / (case["name"] + ".managed.json")
                if not path.is_file():
                    require(run.returncode != 0, "Successful Core execution omitted its original-C timer trace.")
                    break
                managed, expected = json.loads(path.read_text()), original[case["name"]]
                if managed != expected:
                    first = next((i for i, pair in enumerate(zip(managed, expected)) if pair[0] != pair[1]), min(len(managed), len(expected)))
                    row.update(exit=1, output="original-C timer trace differs: " + case["name"] + " row=" + str(first) +
                               "\nactual=" + json.dumps(managed[first:first + 1]) +
                               "\nexpected=" + json.dumps(expected[first:first + 1]) + "\n" + text)
                    break
                compared += 1
            row["originalComparedCases"] = compared
            (destination / "run.log").write_text(row["output"])
            print(label + ": " + row["output"][:1000], flush=True)
            return row

        baseline = execute("baseline", files)
        report["runs"].append(baseline)
        require(baseline["exit"] == 0 and baseline["originalComparedCases"] == len(fixtures),
                "Actual Core/cabinet/original-C timer baseline failed: " + baseline["output"])
        baseline_summary = json.loads(baseline["output"])
        require(len(baseline_summary["cases"]) == 28, "All17 C traces and11 actual cabinet cases are mandatory.")
        report["baselineSummary"] = baseline_summary

        def mutate(control):
            label, name, old, new, expected = control
            changed = dict(files)
            raw = changed[name].decode()
            require(raw.count(old) == 1, "Control no longer targets one actual production operand: " + label)
            changed[name] = raw.replace(old, new).encode()
            row = execute(label, changed)
            require(row["exit"] != 0 and expected in row["output"],
                    "Survived/wrong-failure compiled causal control " + label + ": " + row["output"])
            row["expectedAssertion"] = expected
            return row

        report["runs"].extend(run_mutations(controls, mutate))
        report["status"] = "passed"
    finally:
        report["seconds"] = round(time.monotonic() - started, 3)
        (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")


def required_inventory():
    fixtures, controls = cases(), mutations()
    require(len(fixtures) == len({c["name"] for c in fixtures}) == 17 and sum(c["ticks"] for c in fixtures) == 10417,
            "All17 exact original-C timer traces/10417 dispatches are required.")
    require(len(controls) == len({c[0] for c in controls}) == 21,
            "All21 distinct compiled causal timer/caller/cabinet/HUD/sound controls are required.")
    return fixtures, controls


if __name__ == "__main__":
    main()
