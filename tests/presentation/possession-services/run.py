#!/usr/bin/env python3
"""Compile actual shared possession services, compare unchanged C, kill compiled semantic mutants."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
GAME = "Assets/Scripts/Gameplay/GameSimulation.Possession.cs"
PLAYBACK = "Assets/Scripts/Presentation/SourceCinematicPlayback.cs"
SELECTOR = "Assets/Scripts/Runtime/SourceCabinetInputPlaySelection.cs"
INHERITED_CONTROLS = {
    "bounced-lateral-as-interception", "punt-flags-tested-as-any-bit", "missing-off-turnover-guard",
    "sleep39", "resume-caller-suspended", "suspend-destroys-low-bits", "resume-matches-only-one-hud",
    "message99", "punt-invented-sprite", "dropped-second-flasher", "flasher-delay3", "speech-before-sprites",
    "timeout-is-render-frames", "omitted-off-turnover-producer", "suspension-not-forwarded-to-Core",
    "suspension-not-forwarded-to-FX", "stale-camera-disguised-as-sleep",
}
LIFECYCLE_CONTROLS = {
    "source-input-observation-skipped", "selector-accepts-unexplained-gaps",
    "selector-runs-suspended-menu", "selector-skips-menu-sleep20",
}


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def require(value, message):
    if not value:
        raise AssertionError(message)

def required_inventory():
    cases, controls = fixtures(), mutations()
    require(len(cases) == len({c["name"] for c in cases}) == 40 and sum(c["ticks"] for c in cases) == 4760,
            "Exact40 original-C scenarios/4760 dispatches required; removed cases cannot be green.")
    require(len(controls) == len({c[0] for c in controls}) == 21 and
            {c[0] for c in controls} == INHERITED_CONTROLS | LIFECYCLE_CONTROLS,
            "All21 distinct compiled semantic controls (17 inherited +4 lifecycle) are required.")
    return cases, controls


def argument_parser():
    parser = argparse.ArgumentParser(description=__doc__)
    for label in ("fixture", "sound", "m1", "impact", "normal", "speech", "hud", "fire", "output", "authority"):
        parser.add_argument("--" + label, type=Path, required=True)
    parser.add_argument("--fire-sha256", required=True)
    parser.add_argument("--controls", action="store_true", required=True)
    return parser


def preflight(args):
    from oracle import verified_authority
    from prepare import HUD_SHA256, FIRE_SHA256, SPEECH_SHA256
    verified_authority(args.authority)
    expected = {
        args.hud: HUD_SHA256,
        args.speech: SPEECH_SHA256,
        args.normal: "43e9ccbf3c5162ac99b86d1c2f4925d834e5b7d78fb7b1631f4d33c431c4f3c4",
        args.fire / "manifest.json": FIRE_SHA256,
    }
    require(args.fire_sha256 == FIRE_SHA256, "Required pinned v3 FX manifest identity changed.")
    for path, pin in expected.items():
        require(path.is_file() and not path.is_symlink() and digest(path.read_bytes()) == pin,
                "Missing or changed required original prepared operand: " + str(path))
    for path in (args.fixture, args.sound, args.impact, args.m1 / "c2-callback-data.json",
                 args.m1 / "animation281/export-inventory.json", args.m1 / "m-qbfite-hit-extension.safn"):
        require(path.is_file() and not path.is_symlink(), "Missing required real kernel prerequisite: " + str(path))


def fixtures():
    def case(name, message=-1, **changes):
        row = dict(name=name, message=message, mode=2, type=0, ballFlags=0, flags=0, result=0,
                   off=1, last=1, turnover=False, state=2, ticks=110, ordinal=0)
        row.update(changes)
        return row
    rows = [
        case("live-intercept-pass", 8),
        case("live-intercept-bobble", 8, type=5),
        case("live-intercept-lateral", 8, type=6),
        case("bounced-lateral-turnover", 13, type=6, ballFlags=0x20),
        case("live-fumble-turnover", 13, type=1),
        case("bounced-fumble-turnover", 13, type=1, ballFlags=0x24),
        case("bounced-pass-no-message", ballFlags=0x20),
        case("bounced-bobble-no-message", type=5, ballFlags=0x20),
        case("punt-return-no-sprite", 15, type=2, flags=0x100),
        case("punt-unrelated-extra-flags", 15, type=2, flags=0x100 | 0x200 | 0x8000),
        case("punt-bobble-flag-control", type=2, flags=0x110),
        case("punt-fumble-flag-control", type=2, flags=0x900),
        case("punt-without-punted-flag", type=2),
        case("kickoff-is-not-punt-return", type=3, flags=0x200),
        case("attract-punt-control", type=2, flags=0x100, state=0),
        case("interception-does-not-test-attract", 8, state=0),
        case("unchanged-last-possession", last=0, off=0),
        case("last-possession-not-offense-predicate", 8, last=1, off=0),
        case("live-off-turnover-is-not-a-filter", 8, turnover=True),
        case("live-failed-conversion-is-not-a-filter", 8, result=10),
        case("punt-flag-not-ball-type-predicate", 15, type=4, flags=0x100),
        case("play-over-interception", 8, mode=3, last=0),
        case("play-over-bobble", 8, mode=3, last=0, type=5),
        case("play-over-lateral", 8, mode=3, last=0, type=6),
        case("play-over-failed-conversion-control", mode=3, last=0, result=10),
        case("play-over-off-turnover-control", mode=3, last=0, turnover=True),
        case("play-over-bounced-control", mode=3, last=0, ballFlags=0x20),
        case("play-over-fumble-control", mode=3, last=0, type=1),
        case("play-over-last-differs-control", mode=3),
        case("play-over-same-offense-control", mode=3, last=0, off=0),
        case("dead-message-before-entry", 8, killAt=3),
        case("dead-message-during-sleep", 8, killAt=10),
        case("dead-message-after-camera-resume", 8, killAt=43),
        case("actual-342-wake-timeout", 8, suspendAt=4, ticks=390),
        case("menu-mode-with-external-resumption", 13, type=1, menuAt=50, resumeAt=110, ticks=120),
        case("dead-parent-then-shared-object-id-cleanup", 8, killAt=10, secondAt=70, ticks=180),
    ]
    rows += [case("weighted-speech-ordinal-" + str(ordinal), 8, ordinal=ordinal) for ordinal in range(1, 5)]
    return rows


def mutations():
    return [
        ("bounced-lateral-as-interception", PLAYBACK,
         "if ((input.Ball.RawFlags & 0x20) == 0 &&\n                        (input.Ball.RawType",
         "if ((input.Ball.RawFlags & 0x20) == 0 ||\n                        (input.Ball.RawType",
         "exact active predicate selects"),
        ("punt-flags-tested-as-any-bit", PLAYBACK,
         "(input.RawGameFlags & (0x10 | 0x100 | 0x800)) == 0x100",
         "(input.RawGameFlags & 0x100) != 0", "exact active predicate selects"),
        ("missing-off-turnover-guard", PLAYBACK, "&& !input.OffenseTurnover &&", "&& true &&",
         "exact active predicate selects"),
        ("sleep39", PLAYBACK, "services.SleepCamera(40);", "services.SleepCamera(39);",
         "retained camera output requires its real pending sleep(40)"),
        ("resume-caller-suspended", GAME, "n.Identity.Serial != Caller && (n.Identity.Id & mask) == id",
         "(n.Identity.Id & mask) == id", "caller exception and sleeping low bits"),
        ("suspend-destroys-low-bits", GAME, "n.Sleep |= 0x8000;", "n.Sleep = 0x8001;",
         "caller exception and sleeping low bits"),
        ("resume-matches-only-one-hud", PLAYBACK, "services.ResumeMultiple(0x0f80, unchecked((int)0xfffffffe));",
         "services.ResumeMultiple(0x0f80, -1);", "allowed post-player and low-bit HUD continuation really execute"),
        ("message99", GAME, "Model.SourceProcesses.Sleep(100);", "Model.SourceProcesses.Sleep(99);",
         "actual Core actors animation and ball freeze"),
        ("punt-invented-sprite", GAME, "if (message.Number != 15)\n                            {\n                                AddSourceMessageSprite",
         "if (message.Number >= 0)\n                            {\n                                AddSourceMessageSprite", "original-C declared service trace"),
        ("dropped-second-flasher", GAME,
         'AddSourceMessageSprite(message, message.Number == 8 ? "msg_int_c1" : "msg_trn_c1");',
         ';', "original-C declared service trace"),
        ("flasher-delay3", GAME, "Model.SourceProcesses.Sleep(4);", "Model.SourceProcesses.Sleep(3);",
         "original-C declared service trace"),
        ("speech-before-sprites", GAME,
         """if (message.Number != 15)
                            {
                                AddSourceMessageSprite(message, message.Number == 8 ? "msg_int" : "msg_trn");
                                AddSourceMessageSprite(message, message.Number == 8 ? "msg_int_c1" : "msg_trn_c1");
                            }
                            if (message.Number == 8) ExecuteSourceInterceptionSpeech(message.Serial);""",
         """if (message.Number == 8) ExecuteSourceInterceptionSpeech(message.Serial);
                            if (message.Number != 15)
                            {
                                AddSourceMessageSprite(message, message.Number == 8 ? "msg_int" : "msg_trn");
                                AddSourceMessageSprite(message, message.Number == 8 ? "msg_int_c1" : "msg_trn_c1");
                            }""",
         "original-C declared service trace"),
        ("timeout-is-render-frames", PLAYBACK, "possessionTimeout = 6 * 57;", "possessionTimeout = 6 * 60;",
         "sleep continuation sets timeout after40"),
        ("omitted-off-turnover-producer", "Assets/Scripts/Gameplay/GameSimulation.Source.cs",
         "if (resolution.TurnoverOnDowns) Model.SourceOffenseTurnover = true;",
         "if (resolution.TurnoverOnDowns) Model.SourceOffenseTurnover = false;",
         "off_turnover is produced"),
        ("suspension-not-forwarded-to-Core", GAME,
         "            Model.SourceProcesses.SuspendMultiple(id, mask);",
         "            if (mask != 0x8000) Model.SourceProcesses.SuspendMultiple(id, mask);",
         "actual Core actors animation and ball freeze"),
        ("suspension-not-forwarded-to-FX", GAME,
         'FireControl(SourceFireOperation.SuspendMask, "cambot.c", "cambot_special", 2953, id, mask);',
         ';', "actual admitted FX nodes receive the same suspension mask"),
        ("stale-camera-disguised-as-sleep", GAME,
         "(p.Suspended || Model.SourceCameraPossessionContinuation)",
         "(p.Suspended || p.SleepRemaining > 0)",
         "normal sleep1 is not permission to retain"),
        ("source-input-observation-skipped", "Assets/Scripts/Gameplay/GameSimulation.Source.cs",
         """            if (Model.SourceProcesses.Require(GameProcess).LastWakeTick != Model.SourceTick)
                command = TransformSourceStationInput(command);
""", "", "Source play selection must execute once for every ordered SOURCE tick"),
        ("selector-accepts-unexplained-gaps", SELECTOR,
         "if (sourceTick <= lastSourceTick || lastSourceTick >= 0 && sourceTick != lastSourceTick + 1)",
         "if (sourceTick <= lastSourceTick)", "strict cabinet contract rejects every ordered SOURCE tick"),
        ("selector-runs-suspended-menu", SELECTOR,
         "if (!MenuConsumerAwake(state, sourceTick)) return frame;",
         "if (MenuConsumerAwake(state, sourceTick)) { }",
         "input scans cannot execute a suspended actual menu consumer"),
        ("selector-skips-menu-sleep20", SELECTOR,
         "else if (menuSleepRemaining > 0) menuSleepRemaining--;",
         "else if (menuSleepRemaining > 0) menuSleepRemaining = 0;",
         "new menu sleep20 cannot inherit frozen or early cabinet B presses"),
    ]


def main():
    args = argument_parser().parse_args()
    cases, controls = required_inventory()
    output = args.output.absolute()
    require(not output.exists() and ROOT / ".squad/.scratch" in output.parents, "Use a NEW ignored worktree-local output.")
    output.mkdir(parents=True)
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output), DOTNET_NOLOGO="1",
               DOTNET_CLI_TELEMETRY_OPTOUT="1", PYTHONDONTWRITEBYTECODE="1")
    names = set()
    for entry in ET.parse(ROOT / "tools/source-player/ProductionSources.props").iter("Compile"):
        names.update(p.relative_to(ROOT).as_posix() for p in ROOT.glob(entry.attrib["Include"].replace("$(BlitzSourceRoot)", "")))
    sources = {name: (ROOT / name).read_bytes() for name in sorted(names)}
    sources["Program.cs"] = (HERE / "Program.cs").read_bytes()
    sources["CabinetLifecycle.cs"] = (HERE / "CabinetLifecycle.cs").read_bytes()
    sources["FullKernelInputs.cs"] = (ROOT / "tests/parity/FullKernelInputs.cs").read_bytes()
    fixture_file = output / "cases.json"
    fixture_file.write_text(json.dumps(cases, indent=2) + "\n")
    report = dict(schema="source-possession-services/v1", status="failed", sourceHashes={n: digest(v) for n, v in sources.items()},
                  command=sys.argv, cases=cases, runs=[], nativeSchedulerQualified=False, pcmOrDspPlayback=False,
                  originalCRequired=True, allCompiledControlsRequired=True)
    started = time.monotonic()
    try:
        preflight(args)
        sdk = subprocess.check_output(["dotnet", "--list-sdks"], text=True, env=env)
        version, prefix = max(re.findall(r"^(\d+\.\d+\.\d+)[^\n]*\[([^\]]+)\]$", sdk, re.M),
                              key=lambda p: tuple(map(int, p[0].split("."))))
        compiler = Path(prefix) / version / "Roslyn/bincore/csc.dll"
        references = sorted((Path(prefix).parent / "packs/Microsoft.NETCore.App.Ref").glob("10.0.*/ref/net10.0"))[-1]
        from oracle import generate
        original_start = time.monotonic()
        original = generate(args.authority, output / "original-C", cases)
        require(set(original) == {c["name"] for c in cases} and sum(len(v) for v in original.values()) == 4760,
                "Original-C execution is mandatory for every declared scenario/dispatch.")
        report["originalCSeconds"] = round(time.monotonic() - original_start, 3)
        report["originalCExecuted"] = dict(cases=len(original), dispatches=4760)

        def execute(label, files):
            destination = output / label
            destination.mkdir()
            paths = []
            for name, raw in files.items():
                p = destination / name
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_bytes(raw); paths.append(p)
            assembly = destination / "Possession.Checks.dll"
            options = ["-nologo", "-nostdlib+", "-target:exe", "-langversion:7.3", "-warnaserror+", "-deterministic+",
                       "-out:" + str(assembly), "-main:PossessionChecks"]
            options += ["-r:" + str(p) for p in sorted(references.glob("*.dll"))] + [str(p) for p in paths]
            response = destination / "compile.rsp"
            response.write_text("\n".join(json.dumps(flag) for flag in options) + "\n")
            build_start = time.monotonic()
            build = subprocess.run(["dotnet", str(compiler), "-noconfig", "@" + str(response)], env=env,
                                   capture_output=True, text=True, timeout=180)
            (destination / "compile.log").write_text(build.stdout + build.stderr)
            require(build.returncode == 0 and not build.stdout + build.stderr,
                    "Compiler failure is never a semantic control kill:\n" + build.stdout + build.stderr)
            assembly.with_suffix(".runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
                "tfm": "net10.0", "framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"}}}))
            command = ["dotnet", str(assembly), str(args.fixture.absolute()), str(args.sound.absolute()), str(args.m1.absolute()),
                       str(args.impact.absolute()), str(args.normal.absolute()), str(args.speech.absolute()), str(destination),
                       str(fixture_file), str(args.hud.absolute()), str(args.fire.absolute()), args.fire_sha256]
            compile_seconds = round(time.monotonic() - build_start, 3)
            run_start = time.monotonic()
            run = subprocess.run(command, env=env, cwd=ROOT, capture_output=True, text=True, timeout=240)
            text = run.stdout + run.stderr
            row = dict(name=label, compile=build.returncode, exit=run.returncode, executionExit=run.returncode,
                       executionOutput=text, output=text, command=command,
                       assemblySha256=digest(assembly.read_bytes()), compileSeconds=compile_seconds,
                       executeSeconds=round(time.monotonic() - run_start, 3))
            report["runs"].append(row)
            emitted = [destination / (case["name"] + ".managed.json") for case in cases]
            row["managedTraceFiles"] = sum(path.is_file() for path in emitted)
            if run.returncode == 0 or all(path.is_file() for path in emitted):
                compared = 0
                for case in cases:
                    path = destination / (case["name"] + ".managed.json")
                    require(path.is_file(), "Successful execution omitted its required original-C comparison artifact: " + case["name"])
                    managed = json.loads(path.read_text())
                    expected = original[case["name"]]
                    if managed != expected:
                        first = next((i for i, pair in enumerate(zip(managed, expected)) if pair[0] != pair[1]), -1)
                        row.update(exit=1, output="original-C declared service trace differs: " + case["name"] +
                                   " row=" + str(first) + "\nactual=" + json.dumps(managed[first]) +
                                   "\nexpected=" + json.dumps(expected[first]) + "\n" + text)
                        break
                    compared += 1
                row["originalCComparedCases"] = compared
            (destination / "run.log").write_text(row["output"])
            print(label + ": " + row["output"][:1400], flush=True)
            return row

        baseline = execute("baseline", sources)
        require(baseline["exit"] == 0, "Actual possession service baseline failed: " + baseline["output"])
        summary = json.loads(baseline["output"])
        expected_cases = {row["name"] for row in cases} | {
            "original-short-process-controls", "actual-Core-freeze-and-resumption-boundary",
            "actual-Core-FX-freeze-and-resumption-boundary", "no-unqualified-stale-camera-retention",
            "message-speech-pin-missing-input-and-failstop", "actual-nfl-off-turnover-producer-and-teardown",
            "shared-authored-HUD-pair-flasher-and-punt-control",
            "actual-cabinet-Core-selector-lifecycle", "actual-cabinet-Core-FX-selector-lifecycle",
            "strict-cabinet-order-and-actual-menu-admission"}
        require(set(summary["cases"]) == expected_cases and len(summary["cases"]) == 50 and summary["assertions"] == 2240,
                "Exact50-case possession/cabinet/Core/HUD inventory or retained assertion categories changed.")
        report["baselineSummary"] = summary
        for name, path, before, after, failure in controls:
            text = sources[path].decode()
            require(text.count(before) == 1, "Unique real-source semantic mutation anchor: " + name)
            altered = dict(sources); altered[path] = text.replace(before, after, 1).encode()
            row = execute(name, altered)
            row["requiredNamedFailure"] = failure
            require(row["exit"] == 1 and failure in row["output"],
                    "Compiled control survived or failed the wrong assertion: " + name + "\n" + row["output"])
        final = execute("final-baseline", sources)
        require(final["exit"] == 0 and json.loads(final["output"]) == summary, "Final original-C baseline inventory changed.")
        require(len(report["runs"]) == 23 and baseline["originalCComparedCases"] == final["originalCComparedCases"] == 40,
                "No original-C comparison/control/final-baseline selection may be omitted.")
        require(all((ROOT / name).read_bytes() == raw for name, raw in sources.items() if name not in ("Program.cs", "CabinetLifecycle.cs", "FullKernelInputs.cs")),
                "Production changed during proof.")
        report["status"] = "passed"
        return 0
    except (OSError, ValueError, AssertionError, subprocess.SubprocessError, RuntimeError) as error:
        report["error"] = str(error)
        print(str(error), file=sys.stderr)
        return 1
    finally:
        report["seconds"] = round(time.monotonic() - started, 3)
        (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    raise SystemExit(main())
