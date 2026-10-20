#!/usr/bin/env python3
"""Execute the shared raw CAMBOT producer and compiled semantic controls; optionally unchanged original C."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "tests/ci"))
from isolated_mutations import run_mutations
MODEL = "Assets/Scripts/Presentation/SourceNormalCameraModel.cs"
PLAYBACK = "Assets/Scripts/Presentation/SourceCinematicPlayback.cs"


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def require(value, message):
    if not value:
        raise AssertionError(message)


def mutations():
    return [
        ("public-ball-instead-of-source-root", MODEL, "x = actor.Position.X; z = actor.Position.Z;",
         "x = ball.Position.X; z = ball.Position.Z;", "normal must track actual actor root"),
        ("inferred-QB-mode", MODEL, "else if (carrierMode == 5)\n", "else if (carrierMode == 4)\n",
         "actual MODE_QB retains original lateral root scaling"),
        ("receiver-instead-of-raw-endpoint", MODEL, "x = ball.Target.X; z = ball.Target.Z; limit = 14;",
         "x = input.Actors[ball.Receiver].Position.X; z = input.Actors[ball.Receiver].Position.Z; limit = 14;",
         "original offscreen span uses actual ball.tx"),
        ("offscreen-OR-instead-of-BOTH", MODEL,
         "Math.Abs(receiver.Y / receiver.Z) > yLimit && Math.Abs(target.Y / target.Z) > yLimit",
         "Math.Abs(receiver.Y / receiver.Z) > yLimit || Math.Abs(target.Y / target.Z) > yLimit",
         "one visible cached point prevents"),
        ("omitted-slow-distance-integrator", MODEL, "distanceRate = .0005f;", "distanceRate = .08f;",
         "offscreen carrierless distance uses original slow"),
        ("dropped-scalar-velocity", MODEL, "velocity = SourceCinematicMath.Scale(velocity, .35f); dv *= .35f;",
         "velocity = default(SourceVector3); dv *= .35f;", "normal must retain original scalar velocity"),
        ("approximate-EMA-focus", MODEL, "focus = SourceCinematicMath.Add(focus, velocity); dv += da; d += dv;",
         "focus = SourceCinematicMath.Add(focus, SourceCinematicMath.Scale(SourceCinematicMath.Subtract(plan.Target, focus), .12f)); dv += da; d += dv;",
         "one source wake integrates once"),
        ("removed-original-buffer", MODEL, "float buffer = 2f;", "float buffer = 0f;",
         "normal must retain original scalar velocity"),
        ("wrong-HH-distance-dimension", MODEL,
         "input.HumanTeammates || input.Pullback ? 27f : 19f", "input.Pullback ? 27f : 19f",
         "actual HH team uses original camera parameter dimension"),
        ("omitted-GOPHER", MODEL, "if (WasPlayOver > 1)", "if (WasPlayOver > 7)",
         "was_playover2 executes original GOPHER"),
        ("omitted-snap-velocity-reset", MODEL, "velocity = default(SourceVector3); dv = tv = pv = 0f;",
         "dv = tv = pv = 0f;", "pre-snap snap resets original velocity"),
        ("omitted-unrotation", MODEL, "if ((flags & Unrotate) != 0 && unrotateTargets)",
         "if ((flags & Unrotate) != 0 && !unrotateTargets)", "normal must track actual actor root"),
        ("normal-loop-behind-special", PLAYBACK,
         "flags = camera.PrepareStep(RawTarget(input), false, out frame);\n                    if (flags < 0)",
         "originalNormal.Prepare(camera.State, input, horizontalResolution, verticalResolution, actualRandom);\n"
         "                    flags = camera.PrepareStep(RawTarget(input), false, out frame);\n                    if (flags < 0)",
         "original normal state must not advance behind a special"),
        ("return-reseeded-angles", PLAYBACK, "actualRandom, snapReturn, snapReturn);",
         "actualRandom, true, snapReturn);", "travel return must integrate actual transient angular state"),
        ("wrong-opening-request-bound", PLAYBACK, 'RequireDraw(actualRandom, 11, "cambot.c:3378/select-csi-kickoff")',
         'RequireDraw(actualRandom, 10, "cambot.c:3378/select-csi-kickoff")',
         "opening kickoff must retain all weighted ordinals"),
        ("omitted-opening-auto-hike-feedback", PLAYBACK,
         "openingAutoHike = kickoff.AutoHikeTicks;", "openingAutoHike = 0;",
         "opening kickoff must retain all weighted ordinals"),
        ("omitted-caught-kickoff-reversion", MODEL,
         "return transient && (caughtKick || BeginsNewPlay(input));", "return transient && BeginsNewPlay(input);",
         "actual carrier/bounce ends kickoff before dispatch"),
        ("render-only-shaker", PLAYBACK,
         "camera.ApplyOriginalShakeOffset(shaker.Step(x, z));", "shaker.Step(x, z);",
         "original shaker affects the persistent focus/eye state"),
        ("shaker-results-swapped", PLAYBACK,
         "camera.ApplyOriginalShakeOffset(shaker.Step(x, z));", "camera.ApplyOriginalShakeOffset(shaker.Step(z, x));",
         "original shaker affects the persistent focus/eye state"),
        ("missing-exact-wake-guard", PLAYBACK,
         "lastCameraTick >= 0 && input.SourceTick != lastCameraTick + 1 && !allowResumedTickGap",
         "lastCameraTick >= 0 && input.SourceTick < lastCameraTick && !allowResumedTickGap",
         "omitted source wake rejected"),
        ("browser-viewport-as-source-raster", PLAYBACK,
         "SourceCameraFrameClock.RequireOriginalRasterAspect(horizontalResolution, verticalResolution);\n            Ordered(input.SourceTick",
         "horizontalResolution = 512f; verticalResolution = 384f;\n            Ordered(input.SourceTick",
         "browser content viewport must not replace"),
        ("aliased-immutable-boundary", "Assets/Scripts/Contracts/SourceCameraContracts.cs",
         "Actors = Array.AsReadOnly(copy);", "Actors = Array.AsReadOnly((SourceCameraActor[])actors);",
         "camera boundary must detach every raw actor root"),
        ("kickoff-camera-uses-movement-approximation", "Assets/Scripts/Gameplay/GameSnapshot.cs",
         "return mode == 5 && !model.SourceKickApproach ? 1 : mode;", "return mode;",
         "camera raw kickoff mode is PRE_SNAP before actual approach"),
        ("raw-facing-normalized", "Assets/Scripts/Presentation/SourceCinematicModel.cs",
         "Position = rawActor.Position; Facing = rawActor.Heading;", "Position = rawActor.Position; Facing = rawActor.Heading & 1023;",
         "actual Core camera preserves raw unwrapped actor facing"),
        ("new-play-destroys-dispatch-lifetime", PLAYBACK,
         "camera.RevertToNormal();\n                    resumingNormal = false;",
         "camera.Reset(camera.State); rawSequence = 0; targetActor = -1;\n                    resumingNormal = false;",
         "field-goal new-play retains source dispatch clock/target"),
        ("normal-plan-destroys-dispatch-lifetime", PLAYBACK,
         "actualRandom, snapReturn, snapReturn);\n                    camera.SetState(plan.State);",
         "actualRandom, snapReturn, snapReturn);\n                    camera.Reset(plan.State);",
         "original zoom expiry increments tick_cnt after ModeRevert"),
    ]


def caller_guards(source=ROOT):
    preview = (source / "tools/source-player/Runtime/PreviewCamera.cs").read_text()
    unity = (source / "Assets/Scripts/Presentation/PlayCamera.cs").read_text()
    core = (source / "Assets/Scripts/Gameplay/GameSimulation.Source.cs").read_text()
    snapshot = (source / "Assets/Scripts/Gameplay/GameSnapshot.cs").read_text()
    runtime = (source / "Assets/Scripts/Runtime/BlitzRuntime.cs").read_text()
    require("NormalCameraModel" not in preview and "SourceTransientReturnTarget" not in preview,
            "portable source player must not execute the legacy EMA/envelope producer")
    require("new SourceCinematicPlayback(data.Impact, data.NormalCamera," in preview and
            "Playback.CameraSourceTick(input," in preview and "((ISourceCameraState)state).CameraBoundary" in preview,
            "portable source player calls the same pure original producer with exact immutable Core data")
    require("if (rawSourceCamera)\n                return AdvanceOriginalSourceStage(" in unity and
            "SourcePlayback.CameraSourceTick(source.CameraBoundary," in unity and
            "sourceCameraRandom, sourceCameraProcesses);" in unity and "sourceCameraFeedback(SourcePlayback.CameraFeedback);" in unity,
            "Unity executes the same pure original producer/RNG/feedback, not a copied algorithm")
    require("simulation.SourceActorWorldPosition, simulation.ApplySourceCameraFeedback, simulation.SourceCameraProcesses);" in runtime,
            "actual Unity composition binds the Core-only feedback owner")
    require("new GameSnapshot(Model, SourceCameraStageOrder)" in core and
            core.index("SourceCameraHeadingProvider(new GameSnapshot") < core.index("UpdateSourceMovementPlayers(command);") <
            core.index("AdvanceSourceBall();") and "CacheSourceCameraBallTarget();" in core,
            "pre-player camera observes actual Core ordering and preceding source target-cache draw")
    require("SourceBallOrigin, model.SourceBallTarget, model.SourceCameraBallTarget" in snapshot and
            "SourcePlayerDriver.WorldPosition(actor), model.SourceCameraGroundCenters[id]" in snapshot,
            "boundary retains canonical raw roots, origin/lead endpoint, and source ground caches")
    require(digest((source / "Assets/Scripts/Presentation/NormalCameraModel.cs").read_bytes()) ==
            "4df6d0ce1bfa11322aa0e1e640f1e12bc043468e3d1ccd905deed376d411fb88",
            "historical diagnostic/default model remains byte-identical, not silently relabelled original")
    return 7


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--source-root", type=Path, help="Explicit authenticated original C root; no asset fallback.")
    parser.add_argument("--controls", action="store_true")
    args = parser.parse_args()
    output = args.output.absolute()
    require(not output.exists() and (ROOT / ".squad/.scratch") in output.parents,
            "Choose a new ignored camera proof output inside this worktree.")
    output.mkdir(parents=True)
    names = set()
    for entry in ET.parse(ROOT / "tools/source-player/ProductionSources.props").iter("Compile"):
        names.update(p.relative_to(ROOT).as_posix() for p in ROOT.glob(entry.attrib["Include"].replace("$(BlitzSourceRoot)", "")))
    sources = {name: (ROOT / name).read_bytes() for name in sorted(names)}
    sources["Program.cs"] = (HERE / "Program.cs").read_bytes()
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output), DOTNET_NOLOGO="1",
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true")
    report = dict(schema="original-normal-camera-checks/v1", status="failed", command=sys.argv,
                  sources={name: digest(raw) for name, raw in sources.items()}, runs=[], nativeQualified=False)
    try:
        report["callerGuards"] = caller_guards()
        actual = "-"
        if args.source_root:
            from source_oracle import generate
            actual = str(generate(args.source_root, output / "original-C"))
        sdks = subprocess.check_output(["dotnet", "--list-sdks"], env=env, text=True)
        version, prefix = max(re.findall(r"^(\d+\.\d+\.\d+)[^\n]*\[([^\]]+)\]$", sdks, re.M),
                              key=lambda row: tuple(map(int, row[0].split("."))))
        compiler = Path(prefix) / version / "Roslyn/bincore/csc.dll"
        references = sorted((Path(prefix).parent / "packs/Microsoft.NETCore.App.Ref").glob("10.0.*/ref/net10.0"))[-1]

        def run(label, files, original=False):
            destination = output / label
            destination.mkdir()
            scratch = destination / "runtime"
            scratch.mkdir()
            execute_env = dict(env, TMPDIR=str(scratch), TMP=str(scratch), TEMP=str(scratch))
            paths = []
            for name, raw in files.items():
                path = destination / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(raw)
                paths.append(path)
            assembly = destination / "RawCamera.Checks.dll"
            options = ["-nologo", "-nostdlib+", "-target:exe", "-langversion:7.3", "-warnaserror+", "-deterministic+",
                       "-out:" + str(assembly)]
            options += ["-r:" + str(p) for p in sorted(references.glob("*.dll"))] + [str(p) for p in paths]
            response = destination / "compile.rsp"
            response.write_text("\n".join(json.dumps(o) for o in options) + "\n")
            command = ["dotnet", str(compiler), "-noconfig", "@" + str(response)]
            built = subprocess.run(command, cwd=ROOT, env=execute_env, capture_output=True, text=True, timeout=120)
            (destination / "build.log").write_text(built.stdout + built.stderr)
            row = dict(name=label, compile=built.returncode, compileCommand=command)
            report["runs"].append(row)
            require(built.returncode == 0 and not built.stdout + built.stderr,
                    "Compiler diagnostics never count as semantic control kills:\n" + built.stdout + built.stderr)
            assembly.with_suffix(".runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
                "tfm": "net10.0", "framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"}}}))
            arguments = ["--active", actual] if original and actual != "-" else []
            command = ["dotnet", str(assembly)] + arguments
            result = subprocess.run(command, cwd=ROOT, env=execute_env, capture_output=True, text=True, timeout=180)
            text = result.stdout + result.stderr
            (destination / "run.log").write_text(text)
            row.update(exit=result.returncode, output=text, command=command, assemblySha256=digest(assembly.read_bytes()))
            print(label + ": " + text[:1600])
            return row

        baseline = run("baseline", sources, True)
        require(baseline["exit"] == 0, "Actual raw camera producer failed.")
        summary = json.loads(baseline["output"])
        require(len(summary["cases"]) == (14 if actual != "-" else 13) and summary["assertions"] >= 100,
                "Exact raw camera case inventory changed; no skipped cases.")
        if args.controls:
            def mutation(case):
                label, name, before, after, expected = case
                text = sources[name].decode()
                require(text.count(before) == 1, "Unique real-source semantic mutation anchor: " + label)
                altered = dict(sources)
                altered[name] = text.replace(before, after, 1).encode()
                row = run(label, altered)
                row["requiredNamedFailure"] = expected
                require(row["exit"] == 1 and expected in row["output"],
                        "Compiled raw camera mutant survived or failed for the wrong cause: " + label)
                return row
            controls = run_mutations(mutations(), mutation)
            final = run("final-baseline", sources)
            require(final["exit"] == 0, "Final raw-camera baseline failed.")
            report["runs"] = [baseline, *controls, final]
        require(all((ROOT / name).read_bytes() == raw for name, raw in sources.items() if name != "Program.cs"),
                "Camera production sources changed during qualification.")
        report["status"] = "passed"
        return 0
    except (OSError, AssertionError, ValueError, subprocess.SubprocessError, RuntimeError) as error:
        report["error"] = str(error)
        print(str(error), file=sys.stderr)
        return 1
    finally:
        (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    raise SystemExit(main())
