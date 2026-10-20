#!/usr/bin/env python3
"""Declared strict/portable real-factory cases and compiled controls; never a public replay."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

from canonical import HARNESS, ROOT
sys.path.insert(0, str(ROOT / "tests/ci"))
from isolated_mutations import run_mutations
from kernel_sources import production_paths, canonical_hashes
from prepare_inputs import fixture_hashes

GAME = "Assets/Scripts/Gameplay/"
MUTANTS = (
    ("wrong-handicap-last-row", GAME + "SimulationState.cs",
     "Math.Min(length - 1, raw)", "Math.Min(length - 2, raw)",
     "portable raw arithmetic preserved; consumer index saturates11"),
    ("mutate-raw-handicap", GAME + "GameSimulation.FireHandicap.cs",
     "            for (int i = 0; i < levels.Length; i++)",
     "            if (Compatibility == Blitz.Contracts.SourceGameplayCompatibility.Portable) levels[1] = Math.Min(11, levels[1]);\n"
     "            for (int i = 0; i < levels.Length; i++)",
     "portable raw arithmetic preserved; consumer index saturates11"),
    ("wrong-block-last-row", GAME + "SourceContactDriver.Dispatch.cs",
     "                int adjustment = adjustments[blockIndex];",
     "                if (m.SourceCompatibility == SourceGameplayCompatibility.Portable && rusher.SourceBlockCount >= 6) blockIndex = 0;\n"
     "                int adjustment = adjustments[blockIndex];",
     "portable bc consumer uses last existing row with identical draws/reactions"),
    ("mutate-raw-block-count", GAME + "SourceContactDriver.Dispatch.cs",
     "rusher.SourceBlockCount++;", "rusher.SourceBlockCount = blockIndex + 1;",
     "actual raw block count increments without saturation"),
    ("silent-mode-promotion", "Assets/Scripts/Runtime/SourceC2Composition.cs",
     "movementData, movementSettings, c2Bootstrap, handicapSettings, compatibility,\n                fireEffects, kickMeterMetrics);",
     "movementData, movementSettings, c2Bootstrap, handicapSettings, SourceGameplayCompatibility.Portable,\n                fireEffects, kickMeterMetrics);",
     "actual M1 omitted option stays strict"),
    ("extra-portable-RNG", GAME + "GameSimulation.Source.cs",
     "            Model.SourceHandicapSettings = sourceHandicapSettings;",
     "            Model.SourceHandicapSettings = sourceHandicapSettings;\n"
     "            if (Compatibility == SourceGameplayCompatibility.Portable) SourceDraw(100);",
     "in-domain RNG identical with no compatibility draw"),
    ("silent-repair", GAME + "SimulationState.cs",
     "            SourceCompatibilityRepairs.Add(new SourceCompatibilityRepair(",
     "            if (effective == raw) SourceCompatibilityRepairs.Add(new SourceCompatibilityRepair(",
     "portable repair is not silent"),
    ("strict-running-push-promoted", GAME + "SourceDroneAI.cs",
     "e.State.Target == -1 && e.Input.Compatibility == SourceGameplayCompatibility.Portable",
     "e.State.Target == -1",
     "Strict running-push missing target remains a visible exact source failure"),
    ("running-push-stick-invented", GAME + "SourceDroneAI.cs",
     "e.Output.RetainedTargetlessRunningPush = true;",
     "e.State.Stick = 24; e.Output.RetainedTargetlessRunningPush = true;",
     "portable targetless running push retains all49 valid sticks and source buttons without retargeting"),
    ("running-push-invented-RNG", GAME + "SourceDroneAI.cs",
     "e.Output.RetainedTargetlessRunningPush = true;",
     'e.Random.Range(100, "portable-running-push-mutant"); e.Output.RetainedTargetlessRunningPush = true;',
     "portable targetless running push has zero RNG and explicit source provenance"),
    ("running-push-option-disconnected", GAME + "GameSimulation.Source.cs",
     "compatibility: Compatibility), sourceRandom);",
     "compatibility: SourceGameplayCompatibility.StrictSource), sourceRandom);",
     "actual M1 caller forwards Portable compatibility into the running-push route"),
    ("running-push-silent", GAME + "GameSimulation.Source.cs",
     "if (step.RetainedTargetlessRunningPush)",
     "if (step.RetainedTargetlessRunningPush && actor.Id < 0)",
     "actual running-push repair is never silent or duplicated"),
)

CARRIERLESS_SOURCE = GAME + "SourceMovementIntegration.cs"
CARRIERLESS_MUTANTS = (
    ("carrierless-strict-promoted", CARRIERLESS_SOURCE,
     "Compatibility == SourceGameplayCompatibility.Portable && Model.CarrierId == -1)\n"
     "                {\n                    Model.RecordCompatibilityRepair(SourceCompatibilityRepairKind.CarrierlessDefensiveSwitch,",
     "Model.CarrierId == -1)\n"
     "                {\n                    if (Compatibility == SourceGameplayCompatibility.Portable) Model.RecordCompatibilityRepair(SourceCompatibilityRepairKind.CarrierlessDefensiveSwitch,",
     "strict carrierless B and B+C both fail at the original boundary"),
    ("carrierless-option-disconnected", CARRIERLESS_SOURCE,
     "Compatibility == SourceGameplayCompatibility.Portable && Model.CarrierId == -1",
     "Compatibility == SourceGameplayCompatibility.Portable && actor.Id < 0 && Model.CarrierId == -1",
     "portable actual four-station defensive caller defers carrierless switching without a source fault"),
    ("carrierless-deferred-is-closest", CARRIERLESS_SOURCE,
     "return SourceDefenseSwitchDeferred;", "return -1;",
     "portable carrierless B and B+C retain every station, actor, ball, animation and RNG value"),
    ("carrierless-BC-fallthrough", CARRIERLESS_SOURCE,
     "if (switchResult == SourceDefenseSwitchDeferred) return;",
     "if (switchResult == SourceDefenseSwitchDeferred && actor.Id < 0) return;",
     "portable carrierless switch records exactly one repair per attempted source call"),
    ("carrierless-silent-repair", CARRIERLESS_SOURCE,
     "                    Model.RecordCompatibilityRepair(SourceCompatibilityRepairKind.CarrierlessDefensiveSwitch,",
     "                    if (actor.Id < 0) Model.RecordCompatibilityRepair(SourceCompatibilityRepairKind.CarrierlessDefensiveSwitch,",
     "portable carrierless switch records exactly one repair per attempted source call"),
    ("carrierless-invented-transfer", CARRIERLESS_SOURCE,
     "                    return SourceDefenseSwitchDeferred;",
     "                    TransferSourceMovementStation(actor, Model.Players[actor.Team == TeamId.Home ? 0 : 7]);\n"
     "                    return SourceDefenseSwitchDeferred;",
     "portable carrierless B and B+C retain every station, actor, ball, animation and RNG value"),
    ("carrierless-invented-RNG", CARRIERLESS_SOURCE,
     "                    return SourceDefenseSwitchDeferred;",
     "                    SourceDraw(100);\n                    return SourceDefenseSwitchDeferred;",
     "portable carrierless B and B+C retain every station, actor, ball, animation and RNG value"),
    ("carrierless-wrong-subject", CARRIERLESS_SOURCE,
     'actor.Id, -1, -1, "player.c:db_change_player carrier -1;',
     'actor.Id + 1, -1, -1, "player.c:db_change_player carrier -1;',
     "carrierless switch metadata identifies the retained actor and no invented carrier or target"),
    ("carrierless-invented-effective-index", CARRIERLESS_SOURCE,
     'actor.Id, -1, -1, "player.c:db_change_player carrier -1;',
     'actor.Id, -1, 0, "player.c:db_change_player carrier -1;',
     "carrierless switch metadata identifies the retained actor and no invented carrier or target"),
    ("carrierless-dead-ball-exit-lost", CARRIERLESS_SOURCE,
     "if (Model.Phase == GamePhase.DeadBall && (actor.SourceRawFlags & 0x2) != 0 && Model.CarrierId < 0) return -1;",
     "if (actor.Id < 0 && Model.Phase == GamePhase.DeadBall && (actor.SourceRawFlags & 0x2) != 0 && Model.CarrierId < 0) return -1;",
     "existing drone, play-over onscreen, no-new-B and running-push exits precede any repair"),
    ("carrierless-eager-pushHere", CARRIERLESS_SOURCE,
     "int switchResult = pushHere ? -1 : ChangeSourceDefensePlayer(actor, false);",
     "int switchResult = ChangeSourceDefensePlayer(actor, false);",
     "defined pushHere short circuit never calls the carrierless switch or records a repair"),
    ("carrierless-nonseeking-deferred", CARRIERLESS_SOURCE,
     "            if (selected == actor.Id) return -1;",
     "            if (!seeking) selected = actor.Id;\n            if (selected == actor.Id) return -1;",
     "defined switch preview retains original closest-player results without transferring control"),
    ("carrierless-invalid-negative-promoted", CARRIERLESS_SOURCE,
     "Compatibility == SourceGameplayCompatibility.Portable && Model.CarrierId == -1",
     "Compatibility == SourceGameplayCompatibility.Portable",
     "carrierless deferral does not normalize any unsupported negative carrier index"),
)
PLAYER_SHA = "61fb1be76029663294630e8633574ed065496da1ef6329071817e80622870b52"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def carrierless_oracle(source, out, command):
    raw = source.read_bytes()
    if digest(raw) != PLAYER_SHA:
        raise ValueError("Carrierless defined-domain oracle requires the exact original player.c.")
    text = raw.decode("utf-8").replace("\r\n", "\n")
    begin = text.index("static int db_change_player( fbplyr_data *ppdata, int do_change )\n{")
    end = text.index("\n\t// set new control", begin)
    prefix = text[begin:end]
    prelude = r"""
#include <math.h>
#include <stdio.h>
#define LB_PASS 0
#define LB_FIELD_GOAL 1
#define LB_KICKOFF 2
#define LB_PUNT 3
#define LB_FUMBLE 4
#define LB_BOBBLE 5
#define LB_LATERAL 6
#define TRUE 1
#define FALSE 0
#define GM_PLAY_OVER 1
#define PF_ONSCREEN 2
#define MODE_UNINT 1
#define SEQ_SPIN_A 10
#define SEQ_BREATH 20
#define ISDRONE(p) ((p)->station == -1)
#define fsqrt sqrtf
typedef struct { int seq_index, animode; } anim_data;
typedef struct { float x, z; int flags; anim_data adata[1]; } obj_3d;
typedef struct { obj_3d odata; int plyrnum, team, station; } fbplyr_data;
static fbplyr_data player_blocks[14];
static int p2p_angles[14][14];
static struct { int ball_carrier, off_side, game_mode; } game_info;
static struct { int type, int_receiver; float tx, tz; } ball_obj;
"""
    driver = r"""
int main(void)
{
    int count, actor, team, station, play_over, on_screen;
    if (scanf("%d", &count) != 1) return 2;
    for (int row = 0; row < count; row++)
    {
        if (scanf("%d%d%d%d%d%d%d%d%d%f%f", &actor, &team, &station,
            &game_info.ball_carrier, &ball_obj.type, &game_info.off_side,
            &play_over, &on_screen, &ball_obj.int_receiver, &ball_obj.tx, &ball_obj.tz) != 11) return 3;
        game_info.game_mode = play_over ? GM_PLAY_OVER : 0;
        for (int i = 0; i < 14; i++)
        {
            fbplyr_data *p = &player_blocks[i];
            int unint;
            if (scanf("%f%f%d%d", &p->odata.x, &p->odata.z, &p->station, &unint) != 4) return 4;
            p->plyrnum = i; p->team = i / 7;
            p->odata.flags = i == actor && on_screen ? PF_ONSCREEN : 0;
            p->odata.adata[0].seq_index = 0;
            p->odata.adata[0].animode = unint ? MODE_UNINT : 0;
        }
        for (int i = 0; i < 14; i++)
            for (int j = 0; j < 14; j++)
                if (scanf("%d", &p2p_angles[i][j]) != 1) return 5;
        if (player_blocks[actor].team != team || player_blocks[actor].station != station) return 6;
        printf("%d\n", db_change_player(&player_blocks[actor], 0));
    }
    return 0;
}
"""
    wrapper = out / "original-defined-switch.c"
    wrapper.write_text(prelude + prefix + "\n    (void)do_change;\n    return new;\n}\n" + driver)
    executable = out / "original-defined-switch"
    compiled = command("original-defined-switch-compile",
                       ["cc", "-std=c11", "-O0", "-fno-fast-math", str(wrapper), "-lm", "-o", str(executable)])
    if compiled.returncode:
        raise RuntimeError("Defined-domain original C switch oracle must compile successfully.")
    return executable, dict(original_player_sha256=digest(raw), original_selection_prefix_sha256=digest(prefix.encode()),
        scope="Exact pre-transfer original selection prefix; authored structure/constants adapter; defined inputs only. "
              "Appended return exposes selected identity; not full native, target memory, or undefined C evidence.")


def check_carrierless_oracle(executable, cases_path, command, label):
    cases = json.loads(cases_path.read_bytes())
    if len(cases) != 88:
        raise RuntimeError("Exactly88 defined source-switch cases are required.")
    kinds = ["Pass", "FieldGoal", "Kickoff", "Punt", "Fumble", "Bobble", "Lateral"]
    rows = [str(len(cases))]
    signatures = []
    for case in cases:
        row = case["input"]
        if (not 0 <= row["actor"] < 14 or row["team"] != row["actor"] // 7
                or not 0 <= row["station"] < 4 or not -1 <= row["carrier"] < 14
                or row["offenseSide"] not in (0, 1)):
            raise ValueError("Original switch oracle rejects unsupported actor, team, station, carrier or side indices.")
        if row["carrier"] < 0 and row["ballKind"] in kinds[:3]:
            raise ValueError("Undefined original carrierless seeking input cannot enter the C oracle.")
        signatures.append((row["station"], row["ballKind"], row["carrier"] >= 0, case["selected"] == row["actor"]))
        values = [row["actor"], row["team"], row["station"], row["carrier"], kinds.index(row["ballKind"]),
                  row["offenseSide"], int(row["playOver"]), int(row["onScreen"]), row["intendedReceiver"], row["tx"], row["tz"]]
        if len(row["players"]) != 14 or len(row["angles"]) != 196:
            raise ValueError("Defined switch oracle needs all14 actual actor inputs and14x14 cached angles.")
        for player in row["players"]:
            if player["unint"]:
                raise ValueError("This declared C adapter does not qualify original uninterruptible sequence ranges.")
            values += [player["x"], player["z"], player["station"], 0]
        values += row["angles"]
        rows.append(" ".join(format(value, ".9g") for value in values))
    required = {(station, kind, held, closest) for station in range(4) for kind in kinds
                for held in (False, True) for closest in (False, True) if held or kind not in kinds[:3]}
    if len(signatures) != len(set(signatures)) or set(signatures) != required:
        raise ValueError("Original switch oracle requires the exact four-station held/free and closest/transfer inventory.")
    observed = command(label + "-original-defined-switch", [str(executable)], "\n".join(rows) + "\n")
    expected = [str(case["selected"]) for case in cases]
    actual = observed.stdout.splitlines()
    if observed.returncode or len(actual) != len(cases):
        raise RuntimeError("Original C switch oracle must return exactly one result per declared case.")
    actual = [str(case["input"]["actor"]) if value == "-1" else value for case, value in zip(cases, actual)]
    if actual != expected:
        raise RuntimeError("Defined db_change_player selections differ from the exact original C prefix.")
    return dict(cases=len(cases), actual_input_sha256=digest(cases_path.read_bytes()), status="passed-defined-domain-only")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ("fixture-manifest", "sound-functions", "c2-m1-inputs", "old-c2-inputs", "output"):
        parser.add_argument("--" + key, type=Path, required=True)
    parser.add_argument("--retained-caller-controls-only", action="store_true",
                        help="Run the unchanged8 declared caller controls under StrictSource,not a public replay.")
    parser.add_argument("--carrierless-switch-only", action="store_true",
                        help="Run separate carrierless caller checks and controls; never execute undefined original C.")
    parser.add_argument("--source-root", type=Path, help="Exact original source directory for the defined-domain C switch oracle.")
    args = parser.parse_args()
    if args.carrierless_switch_only and (args.retained_caller_controls_only or not args.source_root):
        parser.error("Carrierless checks require --source-root and cannot select retained-caller controls.")
    out = args.output.absolute()
    if out.exists() or not out.is_relative_to(ROOT / ".squad/.scratch"):
        parser.error("Choose a NEW ignored output under this worktree's .squad/.scratch.")
    out.mkdir(parents=True)
    work = out / "work"
    (work / "empty-feed").mkdir(parents=True)
    env = dict(os.environ, TMPDIR=str(work), TMP=str(work), TEMP=str(work),
               DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", MSBUILDDISABLENODEREUSE="1",
               PYTHONDONTWRITEBYTECODE="1")
    sources = {p.relative_to(ROOT).as_posix(): p.read_bytes() for p in production_paths()}
    harness = {name: (ROOT / name).read_bytes() for name in HARNESS}
    pins = {name: digest(data) for name, data in sources.items()}
    inputs = fixture_hashes(args.fixture_manifest, args.sound_functions)
    inputs.update(canonical_hashes(args.c2_m1_inputs, "contact281-c2-m1"))
    inputs.update(canonical_hashes(args.old_c2_inputs))
    if args.carrierless_switch_only:
        inputs[str((args.source_root / "player.c").absolute())] = PLAYER_SHA
    result = dict(schema="source-portable-compatibility-checks/v1", status="failed", commands=[], controls=[],
                  no_public_replays=True, original_hardware_equivalence=False, production_before=pins,
                  inputs_before=inputs, runs={})
    section = "carrierless-switch" if args.carrierless_switch_only else "controlled" if args.retained_caller_controls_only else "compatibility"
    result["section"] = section
    spec = importlib.util.spec_from_file_location("retained_M1_controls", ROOT / "tests/movement-callers/run.py")
    retained = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(retained)

    def command(label, argv, stdin=None):
        result["commands"].append(argv)
        scratch = out / "runtime" / label
        scratch.mkdir(parents=True, exist_ok=True)
        execute_env = dict(env, TMPDIR=str(scratch), TMP=str(scratch), TEMP=str(scratch))
        p = subprocess.run(argv, input=stdin, env=execute_env, cwd=ROOT, text=True, capture_output=True, timeout=240)
        (out / (label + ".log")).write_text(p.stdout + p.stderr)
        return p

    def execute(label, mutation=None):
        copy = out / "copies" / label
        for base, files in ((copy / "source", sources), (copy / "harness", harness)):
            for name, data in files.items():
                p = base / name
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_bytes(data)
        if mutation:
            identity, path, old, new, expected = mutation
            p = copy / "source" / path
            data = p.read_text()
            if data.count(old) != 1:
                raise RuntimeError("Mutation anchor must be unique: " + identity)
            p.write_text(data.replace(old, new))
        project = copy / "harness/tests/movement-callers/MovementCallerChecks.csproj"
        build = out / "build" / label
        properties = ["-p:BlitzSourceRoot=" + str(copy / "source") + "/",
                      "-p:BaseIntermediateOutputPath=" + str(build / "obj") + "/",
                      "-p:BaseOutputPath=" + str(build / "bin") + "/"]
        argv = ["dotnet", "build", str(project), "--no-restore", "--configuration", "Release",
                "--verbosity", "quiet", *properties]
        p = command(label + "-compile", argv)
        if p.returncode and "NETSDK1004" in p.stdout + p.stderr:
            restored = command(label + "-missing-assets", ["dotnet", "restore", str(project),
                "--source", str(work / "empty-feed"), "--verbosity", "quiet", "-p:NuGetAudit=false", *properties])
            if restored.returncode:
                raise RuntimeError("Offline missing-asset restore failed: " + label)
            p = command(label + "-compile", argv)
        if p.returncode or "0 Warning(s)" not in p.stdout or "0 Error(s)" not in p.stdout:
            raise RuntimeError("Controls require successful zero-diagnostic compilation: " + label)
        p = command(label, ["dotnet", str(build / "bin/Release/net10.0/MovementCallerChecks.dll"),
            str(args.fixture_manifest.absolute()), str(out / label), section,
            str(args.sound_functions.absolute()), "--c2-m1-inputs", str(args.c2_m1_inputs.absolute()),
            "--compatibility-old-inputs", str(args.old_c2_inputs.absolute())])
        summaries = [json.loads(line) for line in p.stdout.splitlines() if line.startswith('{"schema":')]
        summary = summaries[-1] if len(summaries) == 1 else {}
        result["runs"][label] = dict(exit_code=p.returncode, summary=summary, compiled_zero_diagnostics=True)
        control = None
        if mutation:
            if (p.returncode != 1 or summary.get("status") != "failed"
                    or mutation[4] not in summary.get("failed_assertion", "")):
                raise RuntimeError("Control did not reject its exact causal contract: " + label)
            control = dict(id=label, named_failure=summary["failed_assertion"])
        elif args.retained_caller_controls_only:
            retained.require_summary(p.returncode, summary, "controlled", real_animation_sha)
        elif p.returncode or summary.get("status") != "passed" or summary.get("assertions") != (1142 if args.carrierless_switch_only else 325):
            raise RuntimeError("Exact declared strict/portable component assertions required: " + label)
        if args.carrierless_switch_only and not mutation:
            result["runs"][label]["original_defined_switch"] = check_carrierless_oracle(
                original_switch, out / label / "carrierless-defined.json", command, label)
        shutil.rmtree(copy)
        print("PASS " + label, flush=True)
        return control

    try:
        real_animation_sha = "e87401c8cf3e348cc2d9c7255a1f18f43e41b1694ff7dff45c4bf025a5ecce4f"
        if args.carrierless_switch_only:
            original_switch, result["original_switch_oracle"] = carrierless_oracle(args.source_root / "player.c", out, command)
        execute("baseline")
        mutations = [(identity, GAME + filename, old, new, expected)
                     for identity, filename, old, new, expected in retained.MUTANTS] if args.retained_caller_controls_only else CARRIERLESS_MUTANTS if args.carrierless_switch_only else MUTANTS
        result["controls"] = run_mutations(mutations, lambda mutation: execute(mutation[0], mutation))
        execute("final-baseline")
        result["status"] = "passed-focused-strict-portable-not-full-M1"
    except (RuntimeError, OSError, ValueError, subprocess.TimeoutExpired) as error:
        result["error"] = str(error)
        print(str(error), flush=True)
    finally:
        result["production_after"] = {p.relative_to(ROOT).as_posix(): digest(p.read_bytes()) for p in production_paths()}
        result["inputs_after"] = {name: digest(Path(name).read_bytes()) for name in inputs}
        if result["production_after"] != pins or result["inputs_after"] != inputs:
            result["status"] = "inconclusive-generation-changed"
        (out / "result.json").write_text(json.dumps(result, indent=2) + "\n")
    return 0 if result["status"] == "passed-focused-strict-portable-not-full-M1" else 1


if __name__ == "__main__":
    raise SystemExit(main())
