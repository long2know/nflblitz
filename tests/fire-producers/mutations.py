#!/usr/bin/env python3
"""Compile isolated mutations of real producer/order/cache/gate paths; never replay a match."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).parent
GAME = "Assets/Scripts/Gameplay/"
MUTANTS = (
    ("wrong-level0-order", "Assets/Scripts/Runtime/SourceFireSpfxRuntime.cs",
     '            foreach (Process p in processes)\n'
     '                slots.Add(new SourceFireWakeSlot(SourceFireSlotKind.EffectProcess, p.Serial, Function(p.Entry)));\n'
     '            foreach (SourceFireWakeSlot slot in declaredHostSlots)\n'
     '            {\n'
     '                SourceFireGuard.Require(slot.Kind == SourceFireSlotKind.HostContinuation, "Supply only declared real Core continuations.");\n'
     '                slots.Add(slot);\n'
     '            }',
     '            foreach (SourceFireWakeSlot slot in declaredHostSlots) slots.Add(slot);\n'
     '            foreach (Process p in processes)\n'
     '                slots.Add(new SourceFireWakeSlot(SourceFireSlotKind.EffectProcess, p.Serial, Function(p.Entry)));',
     "authored actual level0 effect entries precede individual C2 whistle"),
    ("missing-QB-recurrence", GAME + "SourceMovementIntegration.cs",
     'if (mode == SourceActorMode.Quarterback) MaybeSourcePlayerFire(actor, "_mode_qb", 2088);',
     'if (mode == SourceActorMode.Quarterback && Model.SourceTick <= 1) MaybeSourcePlayerFire(actor, "_mode_qb", 2088);',
     "actual QB caller recurs across source wakes"),
    ("edge-only-emitter", GAME + "GameSimulation.FireSpfx.cs",
     'private void MaybeSourcePlayerFire(ActorState actor, string function, int line)\n        {\n            if (!SourceFireEnabled) return;',
     'private void MaybeSourcePlayerFire(ActorState actor, string function, int line)\n        {\n'
     '            if (!SourceFireEnabled || actor.SourcePendingFireCheck) return;\n            actor.SourcePendingFireCheck = true;',
     "actual QB caller recurs across source wakes"),
    ("missing-real-cache-producer", GAME + "SourceMovementIntegration.cs",
     'RetainSourceFireCache(id, joints, frame);', 'if (id != 3) RetainSourceFireCache(id, joints, frame);',
     "No actual paired actor15/cambot cache"),
    ("wrong-lineup-side", GAME + "GameSimulation.Source.cs",
     'FireLineupScope(-1);', 'FireLineupScope(0);',
     "actual complete lineup owns exactly one explicit all-player clear"),
    ("missing-catch-producer", GAME + "GameSimulation.Source.cs",
     'if (SourceFireEnabled && offense)', 'if (SourceFireEnabled && !offense)',
     "actual third-potential catch submits two original192"),
    ("ball-preincrement", GAME + "GameSimulation.Source.cs",
     'Model.SourceTeamFire[(int)Model.Possession], Model.PassTicksElapsed));',
     'Model.SourceTeamFire[(int)Model.Possession], Math.Max(0, Model.PassTicksElapsed - 1)));',
     "actual postincrement even ball caller queues ordered three PLAYER children"),
    ("missing-lineup-latch-reset", GAME + "GameSimulation.Source.cs",
     'FireControl(SourceFireOperation.ResetLineupLatches, "nfl.c", "lineup_players", 3369);',
     'if (Model.SourceTick < 0) FireControl(SourceFireOperation.ResetLineupLatches, "nfl.c", "lineup_players", 3369);',
     "real lineup resets shared presnap latches separately"),
    ("disabled-extra-RNG", GAME + "GameSimulation.Source.cs",
     'if (SourceFireEnabled) command = DispatchSourceFireLevelZero(command);',
     'if (!SourceFireEnabled) SourceDraw(2);\n            if (SourceFireEnabled) command = DispatchSourceFireLevelZero(command);',
     "empty FX and disabled use identical shared draw/site/order"),
    ("wrong-selection-kill-group", GAME + "GameSimulation.FireSpfx.cs",
     'FireControl(SourceFireOperation.KillProcessGroup, "playsel.c", "play_select", 852, SourceFireIds.PlayerFlash);',
     'FireControl(SourceFireOperation.KillProcessGroup, "playsel.c", "play_select", 852, SourceFireIds.Spfx);',
     "menu SPFX continuations remain independent"),
    ("ball-blink-dropped", GAME + "GameSimulation.Source.cs",
     'Model.SourceBallFlags |= SourceBallFlags.White;',
     'Model.SourceBallFlags |= SourceBallFlags.None;',
     "original ball blink owner kickoff-ten BF_WHITE"),
    ("ball-blink-wrong-cadence", GAME + "GameSimulation.Source.cs",
     'SourceBallEffectSelection == SourceFireBallEffect.Blink && Model.PassTicksElapsed % 10 == 0',
     'SourceBallEffectSelection == SourceFireBallEffect.Blink && Model.PassTicksElapsed % 9 == 0',
     "original ball blink owner kickoff-nine-clears BF_WHITE"),
    ("ball-blink-ignores-sticky-bobble", GAME + "GameSimulation.Source.cs",
     'SourceBallEffectSelection == SourceFireBallEffect.Blink && Model.PassTicksElapsed % 10 == 0',
     'SourceBallEffectSelection == SourceFireBallEffect.Blink && Model.BallKind != SourceBallKind.Bobble && Model.PassTicksElapsed % 10 == 0',
     "original ball blink owner bobble-sticky-ten BF_WHITE"),
    ("ball-white-not-cleared", GAME + "SourceBallDriver.cs",
     'model.SourceBallFlags &= ~SourceBallFlags.White;',
     'model.SourceBallFlags &= ~SourceBallFlags.None;',
     "original ball blink owner kickoff-nine-clears BF_WHITE"),
    ("ball-blink-writes-held", GAME + "GameSimulation.Source.cs",
     '                if (SourceBallEffectSelection == SourceFireBallEffect.Blink && Model.PassTicksElapsed % 10 == 0)\n'
     '                    Model.SourceBallFlags |= SourceBallFlags.White;\n            }',
     '            }\n            if (SourceFireEnabled && SourceBallEffectSelection == SourceFireBallEffect.Blink && Model.PassTicksElapsed % 10 == 0)\n'
     '                Model.SourceBallFlags |= SourceBallFlags.White;',
     "original ball blink owner held-dark-retained BF_WHITE"),
)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    for name in ("fixture", "sound", "m1", "bundle", "manifest-sha256", "reference-proof", "output"):
        p.add_argument("--" + name, required=True)
    args = p.parse_args()
    out = Path(args.output).absolute()
    assert out.is_relative_to(HERE / ".artifacts") and not out.exists()
    out.mkdir(parents=True)
    snapshot = {f.relative_to(ROOT): f.read_bytes() for f in (ROOT / "Assets/Scripts").rglob("*") if f.is_file()}
    rows = []
    try:
        for name, path, old, new, failure in MUTANTS:
            case = out / name
            clone = case / "source"
            for relative, data in snapshot.items():
                f = clone / relative
                f.parent.mkdir(parents=True, exist_ok=True)
                f.write_bytes(data)
            f = clone / path
            text = f.read_text()
            assert text.count(old) == 1, name
            f.write_text(text.replace(old, new))
            command = [sys.executable, "-B", str(HERE / "run.py"), "--source-root", str(clone),
                       "--output", str(case / "proof")]
            for key in ("fixture", "sound", "m1", "bundle", "manifest_sha256", "reference_proof"):
                command += ["--" + key.replace("_", "-"), getattr(args, key)]
            env = dict(os.environ, TMPDIR=str(case), TMP=str(case), TEMP=str(case))
            result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=240)
            (case / "run.log").write_text(result.stdout + result.stderr)
            evidence = json.loads((case / "proof/result.json").read_bytes())
            log = (case / "proof/callers.log").read_text()
            assert evidence["compile_exit"] == 0, "Compilation is not a control kill: " + name
            assert result.returncode != 0 and evidence["caller_exit"] != 0 and failure in log, (name, log[-5000:])
            rows.append(dict(id=name, command=command, compiled=True, expected_named_failure=failure,
                             mutated_sha256=hashlib.sha256(f.read_bytes()).hexdigest(),
                             evidence=str(case / "proof/result.json")))
            shutil.rmtree(clone)
            print("KILLED (compiled): " + name, flush=True)
        for relative, data in snapshot.items():
            assert (ROOT / relative).read_bytes() == data, str(relative)
        status = "passed"
    except (AssertionError, OSError, ValueError, subprocess.TimeoutExpired) as error:
        status = "failed"
        print(str(error), flush=True)
    (out / "result.json").write_text(json.dumps(dict(status=status, controls=rows, no_public_replays=True), indent=2) + "\n")
    return 0 if status == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
