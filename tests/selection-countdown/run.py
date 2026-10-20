#!/usr/bin/env python3
"""Compile the actual Core/cabinet/HUD in isolated trees; original inputs stay private."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import os
from pathlib import Path
import re
import resource
import subprocess
import time
import xml.etree.ElementTree as ET
import importlib.util

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
CALLER_CASES = (
    "actual-timeout-zero-grace-resumption", "actual-timeout-zero-grace-resumption-FX",
    "actual-early-side-and-highlight", "actual-early-exit-reset-new-menu",
    "actual-instruction-input-not-tutorial-ui", "actual-timeout-then-CPU-order",
    "actual-no-human-instant-caller-order", "actual-active-menu-restart",
    "actual-timeout-dependency-failure", "actual-missing-timeout-consumer", "actual-timer-output-fail-closed",
)
LIFECYCLE_CASES = ("random-pause-render", "random-restart", "random-dependency-failure", "random-output-failure")
LIFECYCLE_CONTROLS = {
    "random-restart-retains-continuation": "random-restart",
    "random-output-failure-swallowed": "random-output-failure",
}
CONTROL_INVENTORY_SHA256 = "d2334d1977457f6efb67f9a6efecd8a32ec1d1b6e65358c4ad546dd79d87c207"
CONTROL_SCOPES = {
    "host60-clock": "timer-traces",
    "zero-grace24": "timer-traces",
    "first-quarter8": "timer-traces",
    "later-quarter6": "timer-traces",
    "caller-quarter-off-by-one": "actual-timeout-zero-grace-resumption",
    "instruction-strictly-greater9": "timer-traces",
    "ignored-instruction-hold": "timer-traces",
    "initial-two-tick-delay": "timer-traces",
    "flasher-sleep6": "timer-traces",
    "suspended-timer-still-runs": "timer-traces",
    "premature-current-wake-choice": "actual-timeout-zero-grace-resumption",
    "force-page0-not-highlight": "actual-early-side-and-highlight",
    "force-overwrites-selected": "actual-early-side-and-highlight",
    "Core-overwrites-selected": "actual-early-side-and-highlight",
    "duplicate-forced-choice": "actual-timeout-zero-grace-resumption",
    "caller-does-not-finish": "actual-timeout-zero-grace-resumption",
    "exit-leaves-timer-alive": "actual-timeout-zero-grace-resumption",
    "muted-timer-beep": "actual-timeout-zero-grace-resumption",
    "wrong-sound-volume": "timer-traces",
    "sound-failure-swallowed": "actual-timer-output-fail-closed",
    "HUD-static-white-bast10": "actual-timeout-zero-grace-resumption",
    "random-start-omitted": "random-offense-raw0",
    "random-start-slot4": "random-offense-raw0",
    "random-wrong-captain": "random-both-four-captain1",
    "random-direction-bound3": "random-offense-raw0",
    "random-extra-start-draw": "random-offense-raw0",
    "random-final-button-draw-omitted": "random-offense-raw0",
    "random-extra-sleep1": "random-offense-raw0",
    "random-flip-cap3": "random-offense-raw1",
    "random-page-cap5": "random-offense-raw0",
    "random-one-iteration-short": "random-offense-raw0",
    "random-defense-zero-pages": "random-defense-raw1",
    "random-shared-side-order": "random-both-two-station",
    "random-physical-direction": "random-offense-raw0",
    "random-visual-only-flip": "random-from-page3-preflipped",
    "random-muted-selection": "random-offense-raw0",
    "random-premature-parent": "random-timeout-start577",
    "random-force-parent-omitted": "random-timeout-start577",
    "random-input-scan-as-consumer": "random-suspended-real-consumer",
    "random-draw-from-skipped-consumer": "random-suspended-real-consumer",
    "random-restart-retains-continuation": "random-restart",
    "random-output-failure-swallowed": "random-output-failure",
}
EXPECTED_CAUSES = {
    "host60-clock": "original-C timer trace differs: quarter0-full row=0",
    "zero-grace24": "original-C timer trace differs: quarter0-full row=513",
    "first-quarter8": "original-C timer trace differs: quarter0-full row=0",
    "later-quarter6": "original-C timer trace differs: later-quarter-full row=0",
    "caller-quarter-off-by-one": "SELECTION ASSERTION: first-quarter actual menu starts at source argument9 with an unexecuted born process",
    "instruction-strictly-greater9": "original-C timer trace differs: instruction-threshold-9 row=0",
    "ignored-instruction-hold": "original-C timer trace differs: instruction-threshold-9 row=0",
    "initial-two-tick-delay": "original-C timer trace differs: quarter0-full row=0",
    "flasher-sleep6": "original-C timer trace differs: quarter0-full row=1",
    "suspended-timer-still-runs": "original-C timer trace differs: real-suspended-timer-only row=10",
    "premature-current-wake-choice": "SELECTION ASSERTION: expiry deletes number and flasher before force1; no already-passed controller is replayed",
    "force-page0-not-highlight": "SELECTION ASSERTION: timeout selects the actual highlighted play/page/slot/flip and never overwrites the already chosen side",
    "force-overwrites-selected": "SELECTION ASSERTION: timeout selects the actual highlighted play/page/slot/flip and never overwrites the already chosen side",
    "Core-overwrites-selected": "SELECTION ASSERTION: timeout selects the actual highlighted play/page/slot/flip and never overwrites the already chosen side",
    "duplicate-forced-choice": "SELECTION ASSERTION: next admitted actual consumer forces CURRENT highlights once and finishes the caller's pending sleep1",
    "caller-does-not-finish": "SELECTION ASSERTION: next admitted actual consumer forces CURRENT highlights once and finishes the caller's pending sleep1",
    "exit-leaves-timer-alive": "SELECTION ASSERTION: caller finish continuation tears down timer before resuming the real camera and lineup",
    "muted-timer-beep": "SELECTION ASSERTION: timer immediately displays red9 and submits one source sound on its first admitted wake",
    "wrong-sound-volume": "SELECTION ASSERTION: production timer submits exact authenticated sound operands",
    "sound-failure-swallowed": "SELECTION ASSERTION: actual timer sound consumer failure propagates from its admitted process",
    "HUD-static-white-bast10": "SELECTION ASSERTION: real composed countdown uses one global source BAST18 glyph and LT_RED, never per-side static white bast10",
    "random-start-omitted": "original-C random controller differs: random-offense-raw0 field=draws row=0",
    "random-start-slot4": "original-C random controller differs: random-offense-raw0 field=updates row=0",
    "random-wrong-captain": "SELECTION ASSERTION: Random selection must start once in the actual offensive/defensive captain's menu consumer.",
    "random-direction-bound3": "original-C random controller differs: random-offense-raw0 field=draws row=0",
    "random-extra-start-draw": "original-C random controller differs: random-offense-raw0 field=draws row=0",
    "random-final-button-draw-omitted": "original-C random controller differs: random-offense-raw0 field=draws row=29",
    "random-extra-sleep1": "original-C random controller differs: random-offense-raw0 field=draws row=0",
    "random-flip-cap3": "original-C random controller differs: random-offense-raw1 field=sounds row=4",
    "random-page-cap5": "original-C random controller differs: random-offense-raw0 field=sounds row=7",
    "random-one-iteration-short": "original-C random controller differs: random-offense-raw0 field=draws row=28",
    "random-defense-zero-pages": "original-C random controller differs: random-defense-raw1 field=sounds row=1",
    "random-shared-side-order": "original-C random controller differs: random-both-two-station field=draws row=0",
    "random-physical-direction": "original-C random controller differs: random-offense-raw0 field=sounds row=1",
    "random-visual-only-flip": "original-C random controller differs: random-from-page3-preflipped field=flipState row=27",
    "random-muted-selection": "original-C random controller differs: random-offense-raw0 field=sounds row=8",
    "random-premature-parent": "original-C random controller differs: random-timeout-start577 field=draws row=24",
    "random-force-parent-omitted": "original-C random controller differs: random-timeout-start577 field=draws row=24",
    "random-input-scan-as-consumer": "SELECTION ASSERTION: Random menu operations require this captain's current admitted Core controller wake.",
    "random-draw-from-skipped-consumer": "SELECTION ASSERTION: Random selection draws require the actual admitted game_proc menu consumer.",
    "random-restart-retains-continuation": "SELECTION ASSERTION: new menu cannot inherit a prior random continuation without its public gesture",
    "random-output-failure-swallowed": "SELECTION ASSERTION: random dependency/output failure propagates after the original final pair without a substitute play",
}

def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


oracle = load("source_selection_timer_oracle", HERE / "oracle.py")
controls_module = load("source_selection_timer_mutations", HERE / "mutations.py")
random_oracle = load("source_cabinet_random_oracle", HERE / "random_oracle.py")
compatibility = load("source_cabinet_default_compatibility", HERE / "compatibility.py")
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
    parser.add_argument("--workers", type=int, choices=(1, 2), default=1,
                        help="Independent compiled control workers (default: serial; maximum: 2).")
    return parser


def new_output_path(path):
    output = path.absolute()
    require(not output.exists() and output.resolve() == output and ROOT / ".squad/.scratch" in output.parents,
            "Use a NEW ignored worktree-local output without symlinked parents.")
    return output


def run_controls(controls, execute, workers=1):
    controls = tuple(controls)
    labels = [c[0] for c in controls]
    require(type(workers) is int and workers in (1, 2), "Control workers must be exactly 1 or 2.")
    require(len(labels) == len(set(labels)) and all(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9-]*", n) for n in labels),
            "Each control requires a distinct, nonescaping source/build/output identity.")
    if workers == 1:
        rows = [execute(control) for control in controls]
    else:
        # Drain workers before returning or propagating failure; no child campaign is scheduled here.
        with ThreadPoolExecutor(max_workers=workers) as pool:
            rows = list(pool.map(execute, controls))
    require([r["name"] for r in rows] == labels, "Missing/substituted/out-of-order compiled control receipt.")
    return rows


def control_scope(control, random_fixtures):
    require(control[0] in CONTROL_SCOPES and control[4] in EXPECTED_CAUSES[control[0]],
            "Unknown/empty/substituted causal control binding.")
    scope = CONTROL_SCOPES[control[0]]
    require(scope in ("timer-traces", *CALLER_CASES, *LIFECYCLE_CASES) or
            scope in {c["name"] for c in random_fixtures}, "Unknown/empty/substituted control scope.")
    return scope


def validate_control_inventory(controls):
    require([c[0] for c in controls] == list(CONTROL_SCOPES) == list(EXPECTED_CAUSES) and
            hashlib.sha256(json.dumps(controls, sort_keys=True, separators=(",", ":")).encode()).hexdigest() ==
            CONTROL_INVENTORY_SHA256, "All42 exact immutable control identities/expressions/expected failures are required.")


def validate_binding(control, scope):
    expected = "all" if control in ("baseline", "final-baseline") else CONTROL_SCOPES.get(control)
    require(expected is not None and scope == expected, "Unknown/omitted/wrong control/scope binding.")


def execution_plan(scope, fixtures, random_fixtures):
    if scope == "all":
        return ([c["name"] for c in fixtures] + ["actual-random-gesture"] + list(LIFECYCLE_CASES) +
                [c["name"] for c in random_fixtures] + list(CALLER_CASES))
    if scope == "timer-traces":
        return [c["name"] for c in fixtures]
    require(scope in CALLER_CASES + LIFECYCLE_CASES or scope in {c["name"] for c in random_fixtures},
            "Unknown/empty execution scope.")
    return [scope]


def validate_execution_scope(receipt, control, scope, fixtures, random_fixtures, execution_exit):
    validate_binding(control, scope)
    planned = execution_plan(scope, fixtures, random_fixtures)
    require(receipt.get("schema") == "selection-execution-scope/v2" and receipt.get("admitted") is True and
            receipt.get("control") == control and receipt.get("scope") == scope and
            receipt.get("requestedCases") == planned,
            "Execution omitted/substituted its exact control/scope/case binding.")
    begun, completed = receipt.get("startedCases"), receipt.get("completedCases")
    if execution_exit == 0:
        require(receipt.get("status") == "passed" and begun == completed == planned,
                "Successful execution omitted/substituted actual executed cases.")
    else:
        require(execution_exit == 1 and receipt.get("status") == "failed" and isinstance(begun, list) and
                0 < len(begun) <= len(planned) and begun == planned[:len(begun)] and completed == begun[:-1],
                "Failure did not execute its exact causal case/prefix.")
    return receipt


def validate_causal_failure(control, row):
    label, expected = control[0], control[4]
    require(row["name"] == label and row["scope"] == CONTROL_SCOPES[label] and row["compilerInvoked"] is True and
            row["compile"] == 0 and row["exit"] == 1 and expected in row["output"] and
            row["executionExit"] == (1 if EXPECTED_CAUSES[label].startswith("SELECTION ASSERTION: ") else 0) and
            row["output"].splitlines()[0] == EXPECTED_CAUSES[label],
            "Survived/wrong-failure compiled causal control " + label + ": " + row["output"])
    if EXPECTED_CAUSES[label].startswith("original-C timer trace"):
        require(row["executionExit"] == 0 and row["originalTimerComparedCases"] == 17,
                "All17 production timer traces must execute/compare before the exact original-C timer kill.")


def validate_summary(text, scope, fixtures, random_fixtures):
    summary = json.loads(text)
    execution_plan(scope, fixtures, random_fixtures)
    timer_cases = ([c["name"] for c in fixtures] + list(CALLER_CASES) if scope == "all" else
                   [c["name"] for c in fixtures] if scope == "timer-traces" else [scope] if scope in CALLER_CASES else [])
    random_cases = [c["name"] for c in random_fixtures] if scope == "all" else (
        [scope] if scope in {c["name"] for c in random_fixtures} else [])
    lifecycle_cases = list(LIFECYCLE_CASES) if scope == "all" else ([scope] if scope in LIFECYCLE_CASES else [])
    require(summary.get("status") == "passed" and summary.get("scope") == scope and
            type(summary.get("assertions")) is int and summary["assertions"] > 0 and
            (scope != "all" or summary["assertions"] == 42424) and
            summary.get("cases") == timer_cases and summary.get("randomCases") == random_cases and
            summary.get("randomLifecycleCases") == lifecycle_cases,
            "Successful execution omitted/substituted its exact required cases or mislabelled a partial scope.")
    return summary


def validate_child(run):
    require(run.returncode in (0, 1), "Unknown child status is never a semantic kill: " + str(run.returncode))
    if run.returncode == 1:
        require(not run.stdout and run.stderr.startswith("SELECTION ASSERTION: "),
                "Child failure without actual named assertion evidence is never a semantic kill.")
    else:
        require(not run.stderr, "Successful child emitted unexpected diagnostics: " + run.stderr)


def reuse_baseline_assembly(baseline, source, destination):
    require(baseline["name"] == "baseline" and baseline["scope"] == "all" and
            baseline["compile"] == baseline["exit"] == 0,
            "Only this run's fully qualified unchanged baseline compilation may be reused.")
    raw = source.read_bytes()
    require(hashlib.sha256(raw).hexdigest() == baseline["assemblySha256"],
            "Fresh baseline assembly changed before final execution.")
    destination.write_bytes(raw)


def post_checks(execute_final, execute_compatibility, workers):
    require(type(workers) is int and workers in (1, 2), "Post-check workers must be exactly 1 or 2.")
    with ThreadPoolExecutor(max_workers=workers) as pool:
        final = pool.submit(execute_final)
        default = pool.submit(execute_compatibility)
        return final.result(), default.result()


def main():
    args = argument_parser().parse_args()
    output = new_output_path(args.output)
    output.mkdir(parents=True)
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output),
               DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", PYTHONDONTWRITEBYTECODE="1")
    names = set()
    for item in ET.parse(ROOT / "tools/source-player/ProductionSources.props").iter("Compile"):
        names.update(p.relative_to(ROOT).as_posix() for p in ROOT.glob(item.attrib["Include"].replace("$(BlitzSourceRoot)", "")))
    files = {name: (ROOT / name).read_bytes() for name in sorted(names)}
    files["Program.cs"] = (HERE / "Program.cs").read_bytes()
    files["RandomChecks.cs"] = (HERE / "RandomChecks.cs").read_bytes()
    files["FullKernelInputs.cs"] = (ROOT / "tests/parity/FullKernelInputs.cs").read_bytes()
    report = dict(schema="source-selection-countdown/v1", status="failed", command=list(os.sys.argv),
                  sourceHashes={n: hashlib.sha256(raw).hexdigest() for n, raw in files.items()}, runs=[],
                  workers=args.workers, defaultWorkers=1, optimizedCompilation=True,
                  schedule=["baseline-and-originals", "compiled-controls", "final-baseline-and-default-compatibility"])
    started = time.monotonic()
    resources = resource.getrusage(resource.RUSAGE_CHILDREN)
    try:
        sdk = subprocess.check_output(["dotnet", "--list-sdks"], env=env, text=True)
        version, prefix = max(re.findall(r"^(\d+\.\d+\.\d+)[^\n]*\[([^\]]+)\]$", sdk, re.M),
                              key=lambda p: tuple(map(int, p[0].split("."))))
        compiler = Path(prefix) / version / "Roslyn/bincore/csc.dll"
        refs = sorted((Path(prefix).parent / "packs/Microsoft.NETCore.App.Ref").glob("10.0.*/ref/net10.0"))[-1]
        verified_authority(args.authority)
        fixtures, controls = required_inventory()
        random_fixtures, random_controls = required_random_inventory()
        validate_control_inventory(controls + random_controls)
        report.update(negativeScopeContractVersion=2, controlInventorySha256=CONTROL_INVENTORY_SHA256,
                      controlScopes=CONTROL_SCOPES, expectedCauses=EXPECTED_CAUSES)
        original_start = time.monotonic()
        original = generate(args.authority, output / "original-C", fixtures)
        report.update(originalCSeconds=round(time.monotonic() - original_start, 3),
                      originalCases=len(fixtures), originalDispatches=sum(c["ticks"] for c in fixtures),
                      nativeSchedulerQualified=False, nativeTutorialExecuted=False, pcmOrDspPlayback=False)
        fixture = output / "cases.json"
        fixture.write_text(json.dumps(fixtures, indent=2) + "\n")
        random_fixture = output / "random-cases.json"
        random_fixture.write_text(json.dumps(random_fixtures, indent=2) + "\n")
        original_random = {}

        def compare_random(row, destination, selected=None):
            requested = [c for c in random_fixtures if selected is None or c["name"] in selected]
            require(requested and (selected is None or {c["name"] for c in requested} == set(selected)),
                    "Random comparison requires its exact nonempty declared scope.")
            for case in requested:
                actual = json.loads((destination / (case["name"] + ".random.json")).read_text())
                if actual != original_random[case["name"]]:
                    expected = original_random[case["name"]]
                    field = next(k for k in expected if actual.get(k) != expected[k])
                    a, e = actual.get(field), expected[field]
                    first = next((i for i, pair in enumerate(zip(a, e)) if pair[0] != pair[1]),
                                 min(len(a), len(e))) if isinstance(a, list) else None
                    row.update(exit=1, randomDifference=dict(case=case["name"], field=field, row=first),
                        output="original-C random controller differs: " + case["name"] + " field=" + field +
                        " row=" + str(first) + "\nactual=" + json.dumps(a[first:first + 1] if first is not None else a) +
                        "\nexpected=" + json.dumps(e[first:first + 1] if first is not None else e) + "\n" + row["output"])
                    break
            else:
                row["originalRandomComparedCases"] = len(requested)

        def execute(label, source_files, group="all", reuse=None):
            validate_binding(label, group)
            execution_start = time.monotonic()
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
                       "-deterministic+", "-optimize+", "-out:" + str(assembly), "-main:SelectionCountdownChecks"]
            options += ["-r:" + str(p) for p in sorted(refs.glob("*.dll"))] + [str(p) for p in paths]
            response = destination / "compile.rsp"
            response.write_text("\n".join(json.dumps(v) for v in options) + "\n")
            compile_start = time.monotonic()
            if reuse is None:
                build = subprocess.run(["dotnet", str(compiler), "-noconfig", "@" + str(response)], env=local_env,
                                       capture_output=True, text=True, timeout=180)
                (destination / "compile.log").write_text(build.stdout + build.stderr)
                require(build.returncode == 0 and not build.stdout + build.stderr,
                        "Compiler failure is never a semantic kill: " + build.stdout + build.stderr)
                compile_exit = build.returncode
            else:
                require(label == "final-baseline" and group == "all" and source_files == files,
                        "Mutated/partial compilations may never be reused.")
                reuse_baseline_assembly(reuse, output / "baseline/Selection.Checks.dll", assembly)
                (destination / "compile.log").write_text("")
                compile_exit = reuse["compile"]
            compile_seconds = round(time.monotonic() - compile_start, 3)
            assembly.with_suffix(".runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
                "tfm": "net10.0", "framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"}}}))
            command = ["dotnet", str(assembly)] + [str(getattr(args, n).absolute()) for n in
                ("fixture", "sound", "m1", "impact", "normal", "speech", "hud")] + [
                    str(destination), str(fixture), str(args.fire.absolute()), str(random_fixture), group, label]
            run_start = time.monotonic()
            run = subprocess.run(command, cwd=ROOT, env=local_env, capture_output=True, text=True, timeout=180)
            validate_child(run)
            text = run.stdout + run.stderr
            row = dict(name=label, compile=compile_exit, compilerInvoked=reuse is None,
                       compilationReusedFrom=None if reuse is None else str(output / "baseline/Selection.Checks.dll"),
                       exit=run.returncode, executionExit=run.returncode,
                       command=command, output=text, compileSeconds=compile_seconds,
                       executeSeconds=round(time.monotonic() - run_start, 3),
                       assemblySha256=hashlib.sha256(assembly.read_bytes()).hexdigest(),
                       scope=group, buildRoot=str(destination), runtimeRoot=str(private_runtime),
                       startSeconds=round(execution_start - started, 6))
            row["executionScope"] = validate_execution_scope(
                json.loads((destination / "execution-scope.json").read_text()),
                label, group, fixtures, random_fixtures, run.returncode)
            if run.returncode == 0:
                row["summary"] = validate_summary(run.stdout, group, fixtures, random_fixtures)
            compared = attempted = 0
            differences = []
            for case in fixtures if group in ("all", "timer-traces") else ():
                path = destination / (case["name"] + ".managed.json")
                if not path.is_file():
                    require(run.returncode != 0, "Successful Core execution omitted its original-C timer trace.")
                    break
                managed, expected = json.loads(path.read_text()), original[case["name"]]
                attempted += 1
                if managed != expected:
                    first = next((i for i, pair in enumerate(zip(managed, expected)) if pair[0] != pair[1]), min(len(managed), len(expected)))
                    differences.append(dict(case=case["name"], row=first))
                    if len(differences) == 1:
                        row.update(exit=1, output="original-C timer trace differs: " + case["name"] + " row=" + str(first) +
                                   "\nactual=" + json.dumps(managed[first:first + 1]) +
                                   "\nexpected=" + json.dumps(expected[first:first + 1]) + "\n" + text)
                else:
                    compared += 1
            row["originalComparedCases"] = compared
            row["originalTimerComparedCases"] = attempted
            row["timerDifferences"] = differences
            if original_random and run.returncode == 0 and (group == "all" or group in {c["name"] for c in random_fixtures}):
                compare_random(row, destination, None if group == "all" else group.split(","))
            (destination / "run.log").write_text(row["output"])
            row["endSeconds"] = round(time.monotonic() - started, 6)
            return row

        baseline = execute("baseline", files)
        report["runs"].append(baseline)
        print("baseline: " + baseline["output"][:1000], flush=True)
        require(baseline["exit"] == 0 and baseline["originalComparedCases"] == len(fixtures),
                "Actual Core/cabinet/original-C timer baseline failed: " + baseline["output"])
        baseline_summary = baseline["summary"]
        report["baselineSummary"] = baseline_summary
        random_original_start = time.monotonic()
        original_random = random_oracle.generate(args.authority, output / "original-random-C", random_fixtures,
            json.loads((output / "baseline/random-boundaries.json").read_text()))
        report["originalRandomCSeconds"] = round(time.monotonic() - random_original_start, 3)
        compare_random(baseline, output / "baseline")
        (output / "baseline/run.log").write_text(baseline["output"])
        require(baseline["exit"] == 0, baseline["output"])
        report["originalRandomCases"] = len(random_fixtures)

        def mutate(control):
            label, name, old, new, expected = control[:5]
            group = control_scope(control, random_fixtures)
            changed = dict(files)
            raw = changed[name].decode()
            require(raw.count(old) == 1, "Control no longer targets one actual production operand: " + label)
            changed[name] = raw.replace(old, new).encode()
            row = execute(label, changed, group)
            validate_causal_failure(control, row)
            row["expectedAssertion"] = expected
            row["expectedExactCausalLine"] = EXPECTED_CAUSES[label]
            row["caseSelection"] = group
            row["mutatedSource"] = dict(path=name, beforeSha256=hashlib.sha256(files[name]).hexdigest(),
                                       afterSha256=hashlib.sha256(changed[name]).hexdigest())
            return row

        controls_start = time.monotonic()
        rows = run_controls(controls + random_controls, mutate, args.workers)
        report["controlsSeconds"] = round(time.monotonic() - controls_start, 3)
        report["runs"].extend(rows)
        for row in rows:
            print(row["name"] + ": " + row["output"][:1000], flush=True)
        production = {n: b for n, b in files.items() if n.startswith("Assets/")}
        post_start = time.monotonic()
        final, default = post_checks(lambda: execute("final-baseline", files, reuse=baseline),
            lambda: compatibility.generate(args, output / "default-compatibility", production,
                compiler, sorted(refs.glob("*.dll"))), args.workers)
        report["postChecksSeconds"] = round(time.monotonic() - post_start, 3)
        report["runs"].append(final)
        print("final-baseline: " + final["output"][:1000], flush=True)
        require(final["exit"] == 0 and final["originalComparedCases"] == len(fixtures) and
                final["originalRandomComparedCases"] == len(random_fixtures) and final["summary"] == baseline_summary,
                "Final unchanged actual random controller baseline failed: " + final["output"])
        report["compiledCausalControls"] = len(controls) + len(random_controls)
        report["defaultCompatibility"] = default
        validate_default_compatibility(report["defaultCompatibility"])
        report["status"] = "passed"
    finally:
        report["seconds"] = round(time.monotonic() - started, 3)
        used = resource.getrusage(resource.RUSAGE_CHILDREN)
        report["resources"] = dict(childUserSeconds=round(used.ru_utime - resources.ru_utime, 3),
                                  childSystemSeconds=round(used.ru_stime - resources.ru_stime, 3),
                                  maximumChildRssKiB=used.ru_maxrss, logicalCpus=os.cpu_count(),
                                  availableCpus=len(os.sched_getaffinity(0)))
        (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")


def required_inventory():
    fixtures, controls = cases(), mutations()
    require(len(fixtures) == len({c["name"] for c in fixtures}) == 17 and sum(c["ticks"] for c in fixtures) == 10417,
            "All17 exact original-C timer traces/10417 dispatches are required.")
    require(len(controls) == len({c[0] for c in controls}) == 21,
            "All21 distinct compiled causal timer/caller/cabinet/HUD/sound controls are required.")
    return fixtures, controls


def required_random_inventory():
    fixtures, controls = random_oracle.cases(), controls_module.random_mutations()
    require(len(fixtures) == len({c["name"] for c in fixtures}) == 33,
            "All33 distinct unchanged original-controller/public-input cases are required.")
    require(len(controls) == len({c[0] for c in controls}) == 21,
            "All21 compiled random controller/caller/consumer/lifecycle controls are required.")
    return fixtures, controls


def validate_default_compatibility(receipt):
    require(receipt.get("status") == "passed" and receipt.get("base") == compatibility.BASE and
            receipt.get("tree") == compatibility.TREE and receipt.get("originalTargetRandomQualified") is False and
            receipt.get("fullPublicReplayRegenerated") is False and receipt.get("hostedOrFullM1Campaign") is False,
            "Required byte compatibility is pinned main40/current execution, never a regenerated replay/native claim.")
    require([r["name"] for r in receipt["runs"]] == ["main40", "current"] and
            all(r["compile"] == r["exit"] == 0 and not r["diagnostics"] and
                r["summary"]["hosts"] == 1200 and r["summary"]["rawCalls"] > 0 and
                r["summary"]["SourceTick"] == 1140 and r["summary"]["rawCalls"] == 491 and
                r["summary"]["selections"] == 4 and r["summary"]["defaultSeed"] == 1 and
                r["summary"]["rawVectorFiltered"] is False for r in receipt["runs"]),
            "Both actual default Core builds must execute the complete unfiltered seed1 input/RNG trace.")
    require(set(receipt["compared"]) == {"default-host.jsonl", "default-rng.json", "default-menu.json"} and
            all(r["bytes"] > 0 and re.fullmatch(r"[0-9a-f]{64}", r["sha256"]) and r["byteIdentical"] is True
                for r in receipt["compared"].values()),
            "All nonempty actual host/player/process/HUD, menu and raw RNG byte comparisons are required.")


if __name__ == "__main__":
    main()
