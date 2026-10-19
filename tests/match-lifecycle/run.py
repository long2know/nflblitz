#!/usr/bin/env python3
"""Declared actual-M1 terminal producer and real bridge checks, not a public match."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(ROOT / "tests/parity"))
from kernel_sources import canonical_hashes, production_paths
from prepare_inputs import fixture_hashes

SOURCE = "Assets/Scripts/Gameplay/GameSimulation.Source.cs"
BRIDGE = "Assets/Scripts/Runtime/FixedStepBridge.cs"
HARNESSES = (
    "tests/match-lifecycle/Directory.Build.props",
    "tests/match-lifecycle/MatchLifecycleChecks.csproj",
    "tests/match-lifecycle/Program.cs",
    "tests/parity/FullKernelInputs.cs", "tests/parity/FullKernelSources.props",
    "tests/audio/vm/SourceSoundVmSources.props",
)
EMIT = "                EmitSource(SourceEventType.MatchEnded);"
MUTANTS = (
    ("missing-source-notification", SOURCE, EMIT, "",
     "actual M1 terminal source caller publishes exactly one typed MatchEnded notification"),
    ("duplicate-source-notification", SOURCE, EMIT, EMIT + "\n" + EMIT,
     "actual M1 terminal source caller publishes exactly one typed MatchEnded notification"),
    ("wrong-source-notification", SOURCE, EMIT, EMIT.replace("MatchEnded", "PeriodChanged"),
     "actual M1 terminal source caller publishes exactly one typed MatchEnded notification"),
    ("missing-legacy-notification", SOURCE,
     "                Emit(GameEventType.GameEnded, TeamId.Home, -1, Model.HomeScore.CompareTo(Model.AwayScore),\n"
     "                    default(FieldVector));", "",
     "legacy GameEnded remains exactly once with its unchanged score-comparison payload"),
    ("invented-terminal-actor", SOURCE, EMIT, EMIT.replace("MatchEnded);", "MatchEnded, actor: 0);"),
     "typed terminal metadata has no invented winner value, actor, target, station or result"),
    ("premature-nonterminal-notification", SOURCE,
     "            if (decision.MatchEnded)\n",
     "            EmitSource(SourceEventType.MatchEnded);\n            if (decision.MatchEnded)\n",
     "unexpired, earlier-quarter, tied overtime and pending PAT boundaries publish no terminal notification"),
    ("stale-gameover-event-batch", "Assets/Scripts/Gameplay/GameSimulation.cs",
     "        public void Step(PlayerCommand command)\n        {\n",
     "        public void Step(PlayerCommand command)\n        {\n"
     "            if (Model.Phase == GamePhase.GameOver) { Model.Tick++; Publish(); return; }\n",
     "later GameOver host steps and queued Continue cannot replay either terminal event"),
    ("invented-terminal-RNG", SOURCE, EMIT,
     "                SourceDraw(100);\n" + EMIT,
     "typed terminal notification adds no RNG or source-game-over reward draws"),
    ("disconnected-source-bridge", BRIDGE,
     "if (handler != null) handler(sourceEvents[i]);",
     "if (handler != null && sourceEvents[i].Type != SourceEventType.MatchEnded) handler(sourceEvents[i]);",
     "actual FixedStepBridge delivers typed and legacy terminal notifications exactly once in a batched frame"),
)


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def required_cases():
    cases = set()
    for mode in ("StrictSource", "Portable"):
        cases.add(f"{mode}/nonterminal/q3/7-0/15360/Normal")
        cases.update(f"{mode}/nonterminal/q{q}/7-0/0/Normal" for q in range(3))
        cases.update(f"{mode}/nonterminal/q{q}/14-14/0/Normal" for q in range(3, 6))
        cases.update(f"{mode}/nonterminal/q{q}/7-0/0/ExtraPoint" for q in range(3, 7))
        for q in range(3, 7):
            cases.update((f"{mode}/terminal/q{q}/7-0/gateTrue", f"{mode}/terminal/q{q}/0-14/gateTrue"))
        cases.add(f"{mode}/terminal/q6/14-14/gateTrue")
        cases.update(f"{mode}/terminal/q{q}/14-14/gateFalse" for q in range(6))
        cases.add(f"{mode}/terminal/q3/7-0/gateFalse")
        cases.update((f"{mode}/actual-bridge/gateTrue", f"{mode}/actual-bridge/gateFalse",
                      f"{mode}/actual-period-producer-no-RNG"))
        cases.update(f"{mode}/actual-PAT/q{q}/{home}-{away}"
                     for q, home, away in ((3, 8, 6), (6, 7, 6), (3, 6, 8), (3, 7, 6)))
    cases.update("legacy60/terminal/" + str(sign) for sign in (-1, 0, 1))
    return cases


def require_summary(code, summary, expected_failure=None):
    if (not isinstance(summary, dict) or summary.get("schema") != "source-match-ended-caller-checks/v1"
            or summary.get("whole_game_or_native_qualified") is not False
            or summary.get("declared_boundary_cases_not_public_full_match") is not True):
        raise RuntimeError("Actual declared M1/bridge scope and summary are required.")
    cases = summary.get("cases", [])
    if not isinstance(cases, list) or len(cases) != len(set(cases)) or not set(cases).issubset(required_cases()):
        raise RuntimeError("Declared lifecycle case identities cannot be missing, duplicated or substituted.")
    if expected_failure is not None:
        if (code != 1 or summary.get("status") != "failed" or summary.get("assertions", 0) < 1
                or summary.get("failed_assertion") != expected_failure):
            raise RuntimeError("A compiled lifecycle control must reject its exact causal assertion.")
    elif (code or summary.get("status") != "passed" or summary.get("assertions") != 593
          or len(cases) != 71 or set(cases) != required_cases()):
        raise RuntimeError("All593 assertions across the exact71 real-M1/bridge/PAT/legacy cases are required.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("fixture-manifest", "sound-functions", "c2-m1-inputs", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--controls", action="store_true")
    args = parser.parse_args()
    out = args.output.absolute()
    if out.exists() or not out.is_relative_to(ROOT / ".squad/.scratch"):
        parser.error("Choose a NEW ignored output directory under this worktree's .squad/.scratch.")
    out.mkdir(parents=True)
    work = out / "work"
    (work / "empty-feed").mkdir(parents=True)
    env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
               DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", PYTHONDONTWRITEBYTECODE="1",
               MSBUILDDISABLENODEREUSE="1")
    paths = production_paths() + [ROOT / BRIDGE]
    sources = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in paths}
    harness = {name: (ROOT / name).read_bytes() for name in HARNESSES}
    before = {name: digest(raw) for name, raw in sources.items()}
    inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
    inputs.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
    result = dict(schema="source-match-ended-caller-run/v1", status="failed", runs={}, commands=[], controls=[],
                  declared_boundary_cases_not_public_full_match=True, whole_game_or_native_qualified=False,
                  production_before=before, harness_before={name: digest(raw) for name, raw in harness.items()},
                  runner_sha256=digest(Path(__file__).read_bytes()), inputs_before=inputs,
                  complete_compiled_control_closure_executed=False)

    def command(label, argv):
        result["commands"].append(argv)
        process = subprocess.run(argv, env=env, cwd=ROOT, capture_output=True, text=True, timeout=240)
        (out / (label + ".log")).write_text(process.stdout + process.stderr)
        return process

    def execute(label, mutation=None):
        copy = out / "copies" / label
        for base, entries in ((copy / "source", sources), (copy / "harness", harness)):
            for name, raw in entries.items():
                path = base / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(raw)
        if mutation is not None:
            identity, filename, old, new, expected = mutation
            path = copy / "source" / filename
            raw = path.read_text()
            if raw.count(old) != 1:
                raise RuntimeError("Lifecycle control requires one exact production anchor: " + identity)
            path.write_text(raw.replace(old, new))
        project = copy / "harness/tests/match-lifecycle/MatchLifecycleChecks.csproj"
        build = out / "build" / label
        common = ["-p:BlitzSourceRoot=" + str(copy / "source") + "/",
                  "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
                  "-p:BaseOutputPath=" + str(build / "bin") + "/"]
        argv = ["dotnet", "build", str(project), "--no-restore", "-c", "Release", "-v", "quiet", *common]
        process = command(label + "-compile", argv)
        if process.returncode and "NETSDK1004" in process.stdout + process.stderr:
            restored = command(label + "-missing-assets", ["dotnet", "restore", str(project), "--source",
                str(work / "empty-feed"), "-p:NuGetAudit=false", "-v", "quiet", *common])
            if restored.returncode:
                raise RuntimeError("Offline missing-asset restore failed: " + label)
            process = command(label + "-compile", argv)
        if process.returncode or "0 Warning(s)" not in process.stdout or "0 Error(s)" not in process.stdout:
            raise RuntimeError("Compile errors/warnings cannot qualify a lifecycle control: " + label)
        process = command(label, ["dotnet", str(build / "bin/Release/net10.0/MatchLifecycleChecks.dll"),
            str(args.fixture_manifest.absolute()), str(args.sound_functions.absolute()), str(args.c2_m1_inputs.absolute())])
        summaries = [json.loads(line) for line in process.stdout.splitlines() if line.startswith('{"schema":')]
        summary = summaries[0] if len(summaries) == 1 else None
        result["runs"][label] = dict(exit_code=process.returncode, summary=summary, compiled_zero_diagnostics=True)
        require_summary(process.returncode, summary, mutation[4] if mutation else None)
        if mutation:
            result["controls"].append(dict(id=label, named_failure=summary["failed_assertion"]))
        shutil.rmtree(copy)
        print("PASS " + label, flush=True)

    try:
        execute("baseline")
        if args.controls:
            for mutation in MUTANTS:
                execute(mutation[0], mutation)
        execute("final-baseline")
        result["complete_compiled_control_closure_executed"] = args.controls
        result["status"] = "passed-declared-real-M1-terminal-and-bridge" if args.controls else "passed-component-only-controls-not-run"
    except (OSError, RuntimeError, ValueError, subprocess.TimeoutExpired) as error:
        result["error"] = str(error)
        print("MATCH LIFECYCLE FAILED: " + str(error), file=sys.stderr)
    finally:
        result["production_after"] = {p.relative_to(ROOT).as_posix(): digest(p.read_bytes()) for p in paths}
        result["inputs_after"] = {name: digest(Path(name).read_bytes()) for name in inputs}
        result["harness_after"] = {name: digest((ROOT / name).read_bytes()) for name in HARNESSES}
        if (result["production_after"] != before or result["inputs_after"] != inputs
                or result["harness_after"] != result["harness_before"]
                or digest(Path(__file__).read_bytes()) != result["runner_sha256"]):
            result["status"] = "inconclusive-generation-changed"
        (out / "result.json").write_text(json.dumps(result, indent=2) + "\n")
    return 0 if result["status"].startswith("passed-") else 1


if __name__ == "__main__":
    raise SystemExit(main())
