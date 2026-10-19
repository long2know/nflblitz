#!/usr/bin/env python3
"""Compile actual source-effect/Core callers with defined synthetic inputs, never original assets or Unity."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def mutations():
    runtime = "Assets/Scripts/Runtime/SourceFireSpfxRuntime.cs"
    helper = "Assets/Scripts/Gameplay/GameSimulation.Contact.cs"
    model = "Assets/Scripts/Presentation/SourceFreeBallRenderModel.cs"
    return [
        ("missing-admission", helper, "            AdmitSourceLooseHelmet(actor, fire ? 9198 : 9205);", "",
         "guarded calls retain prior helmet, but kill then delete must precede the noncarrier return"),
        ("cleanup-after-carrier", helper,
         '            ClearSourceLooseHelmet("player.c", "maybe_pop_helmet", 9188, 9189, actor.Id);\n            if (actor.Id != Model.CarrierId) return;',
         '            if (actor.Id != Model.CarrierId) return;\n            ClearSourceLooseHelmet("player.c", "maybe_pop_helmet", 9188, 9189, actor.Id);',
         "guarded calls retain prior helmet, but kill then delete must precede the noncarrier return"),
        ("duplicate-helper-rng", helper, '            int first = SourceDraw(100, "player.c:maybe_pop_helmet:9177");',
         '            SourceDraw(100, "synthetic-bad-extra");\n            int first = SourceDraw(100, "player.c:maybe_pop_helmet:9177");',
         "helper must draw first100 before mode/head guards and never duplicate either probability draw"),
        ("helper-first-draw-too-late", helper,
         '            int first = SourceDraw(100, "player.c:maybe_pop_helmet:9177");\n            if (Model.Phase != GamePhase.LiveBall) return;',
         '            if (Model.Phase != GamePhase.LiveBall) return;\n            int first = SourceDraw(100, "player.c:maybe_pop_helmet:9177");',
         "helper must draw first100 before mode/head guards and never duplicate either probability draw"),
        ("missing-secret-head-guard", helper, "                if (sourcePresentation.StationHeads[actor.SourceControllerStation] != -1) return;",
         "", "guarded calls retain prior helmet, but kill then delete must precede the noncarrier return"),
        ("wrong-announcer-call", helper, 'fire ? "165" : "166"', 'fire ? "166" : "165"',
         "retained helper flag/event and exact original announcer/hard-hit arguments must not be substituted"),
        ("wrong-neck", runtime, "SourceVector3 j = cache.CameraJoints[8], c = cache.Camera.Position;",
         "SourceVector3 j = cache.CameraJoints[0], c = cache.Camera.Position;",
         "first wake must invert cached neck8 with its paired camera and launch in world axes"),
        ("ground-clamped-entry", runtime, "helmet.Y = j.X * m[1] + j.Y * m[5] + j.Z * m[9] + c.Y;",
         "helmet.Y = 0f;", "first wake must invert cached neck8 with its paired camera and launch in world axes"),
        ("separate-launch-rng", runtime, "if (Draw(100, Site(9234, p.Args.A0)) < 45)",
         "if (Draw(99, Site(9234, p.Args.A0)) < 45)",
         "first wake must invert cached neck8 with its paired camera and launch in world axes"),
        ("opposite-z-launch", runtime, "helmet.Vx *= -1; helmet.Vz *= -1;", "helmet.Vx *= -1;",
         "one shared range100<45 must negate BOTH world-axis launch velocities"),
        ("float-bounce-loss", runtime, "helmet.Vy = (float)(helmet.Vy - 0.10d);", "helmet.Vy = helmet.Vy - 0.10f;",
         "every source wake must preserve original float stores, integer fields, bounce tape and spin"),
        ("swapped-bounce-tape", runtime, "theta += Draw(201, Site(9298, p.Args.A0));",
         "theta += Draw(157, Site(9298, p.Args.A0));",
         "every source wake must preserve original float stores, integer fields, bounce tape and spin"),
        ("missing-spin-draw", runtime, "Draw(35, Site(9350, p.Args.A0)) + 30", "30",
         "first wake must invert cached neck8 with its paired camera and launch in world axes"),
        ("wrong-rest-heading", runtime, "if (vx == 0f && vz == 0f) helmet.Elevation = 0;",
         "if (vx == 0f && vz == 0f) { helmet.Elevation = 0; helmet.Heading = 0; }",
         "every source wake must preserve original float stores, integer fields, bounce tape and spin"),
        ("reset-global-phi", runtime, "helmet.Alpha = 255; helmet.Pitch = 0;", "helmet.Phi = 0; helmet.Alpha = 255; helmet.Pitch = 0;",
         "every source wake must preserve original float stores, integer fields, bounce tape and spin"),
        ("reset-global-flight", runtime, "helmet.Alpha = 255; helmet.Pitch = 0;", "helmet.FlightTime = 0; helmet.Alpha = 255; helmet.Pitch = 0;",
         "replacement entry must carry global phi/flight_time instead of resetting ball_data"),
        ("reset-global-flags", runtime, "helmet.Alpha = 255; helmet.Pitch = 0;", "helmet.Flags = 0; helmet.Alpha = 255; helmet.Pitch = 0;",
         "every source wake must preserve original float stores, integer fields, bounce tape and spin"),
        ("wrong-velocity-pitch-branch", runtime, "if ((helmet.Flags & 2) != 0)", "if ((helmet.Flags & 2) == 0)",
         "every source wake must preserve original float stores, integer fields, bounce tape and spin"),
        ("too-many-bounce-sounds", runtime, "if (previousBounces < 3)", "if (previousBounces <= 3)",
         "only first three postincrement bounces must journal original BALL_HIT_GRND cue arguments"),
        ("raw-phi-normalized", runtime, '            Record("LooseHelmetStep", Site(9352, p.Args.A0), p, p.ObjectSerial, helmet.FlightTime,',
         '            helmet.Phi %= 1024;\n            Record("LooseHelmetStep", Site(9352, p.Args.A0), p, p.ObjectSerial, helmet.FlightTime,',
         "every source wake must preserve original float stores, integer fields, bounce tape and spin"),
        ("invented-ttl", runtime, '            Record("LooseHelmetStep", Site(9352, p.Args.A0), p, p.ObjectSerial, helmet.FlightTime,',
         '            if (helmet.FlightTime == 200) { Retire(p, "ReturnDie", Site(9352, p.Args.A0), 1); return; }\n'
         '            Record("LooseHelmetStep", Site(9352, p.Args.A0), p, p.ObjectSerial, helmet.FlightTime,',
         "every source wake must preserve original float stores, integer fields, bounce tape and spin"),
        ("automatic-helmet-object-delete", runtime, '            Record(kind, site, p, p.ObjectSerial, cause, kind == "KillProcess" ?',
         '            if (p.Entry == SourceFireEntry.LooseHelmet) helmets.RemoveAll(o => o.Owner == p.Serial);\n'
         '            Record(kind, site, p, p.ObjectSerial, cause, kind == "KillProcess" ?',
         "killall must not invent a dust-style exit alert or delete the globally backed helmet object"),
        ("cancel-deleted-global", runtime,
         "p.ObjectSerial != 0 && p.Entry != SourceFireEntry.LooseHelmet &&",
         "p.ObjectSerial != 0 && (p.Entry != SourceFireEntry.LooseHelmet || helmets.Count == 0) &&",
         "delete_multiple_objects alone must not kill or cancel a process that only writes global ball_data"),
        ("missing-new-play-cleanup", "Assets/Scripts/Gameplay/GameSimulation.Source.cs",
         '            Model.Phase = GamePhase.PlaySelect;\n            ClearSourceLooseHelmet("nfl.c", "game_proc", 867, 868);',
         '            Model.Phase = GamePhase.PlaySelect;',
         "actual new-play continuation must kill then delete helmet at nfl867/868 before selection"),
        ("helmet-own-white", model, "helmet.Scale, globalBallFlags & 1, 0", "helmet.Scale, helmet.RawFlags & 1, 0",
         "helmet draws must use current GLOBAL ball WHITE and captured team decal2/ratio4 without ball-carrier or bigball gates"),
    ]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--controls", action="store_true")
    args = parser.parse_args()
    output = args.output.resolve()
    require(output.is_relative_to(ROOT / ".squad/.scratch") and not output.exists(), "Choose a new owned ignored helmet proof output.")
    output.mkdir(parents=True)
    names = set()
    for entry in ET.parse(ROOT / "tools/source-player/ProductionSources.props").iter("Compile"):
        path = entry.attrib["Include"].replace("$(BlitzSourceRoot)", "")
        names.update(p.relative_to(ROOT).as_posix() for p in ROOT.glob(path))
    files = {name: (ROOT / name).read_bytes() for name in sorted(names)}
    files["Program.cs"] = (HERE / "Program.cs").read_bytes()
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output), DOTNET_NOLOGO="1",
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="true")
    report = {"schema": "source-loose-helmet-asset-free/v1", "status": "failed", "command": sys.argv,
              "sources": {p: digest(b) for p, b in files.items()}, "runs": [],
              "syntheticInputs": True, "originalAssetsRequired": False, "UnityExecuted": False, "nativeQualified": False}
    try:
        sdks = subprocess.check_output(["dotnet", "--list-sdks"], env=env, text=True)
        version, prefix = max(re.findall(r"^(\d+\.\d+\.\d+)[^\n]*\[([^\]]+)\]$", sdks, re.M),
                              key=lambda row: tuple(map(int, row[0].split("."))))
        compiler = Path(prefix) / version / "Roslyn/bincore/csc.dll"
        refs = sorted((Path(prefix).parent / "packs/Microsoft.NETCore.App.Ref").glob("10.0.*/ref/net10.0"))[-1]

        def run(label, sources):
            directory = output / label
            directory.mkdir()
            paths = []
            for name, raw in sources.items():
                path = directory / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(raw)
                paths.append(path)
            assembly = directory / "Helmet.Checks.dll"
            options = ["-nologo", "-nostdlib+", "-target:exe", "-langversion:7.3", "-warnaserror+", "-deterministic+",
                       "-out:" + str(assembly)]
            options += ["-r:" + str(p) for p in sorted(refs.glob("*.dll"))] + [str(p) for p in paths]
            response = directory / "compile.rsp"
            response.write_text("\n".join(json.dumps(o) for o in options) + "\n")
            command = ["dotnet", str(compiler), "-noconfig", "@" + str(response)]
            built = subprocess.run(command, env=env, cwd=ROOT, text=True, capture_output=True, timeout=120)
            (directory / "build.log").write_text(built.stdout + built.stderr)
            row = {"name": label, "compile": built.returncode, "compileCommand": command}
            report["runs"].append(row)
            require(built.returncode == 0 and not (built.stdout + built.stderr), "Compiler diagnostics never count as behavioral control kills:\n" + built.stdout + built.stderr)
            assembly.with_suffix(".runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
                "tfm": "net10.0", "framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"}}}))
            result = subprocess.run(["dotnet", str(assembly)], env=env, cwd=ROOT, text=True, capture_output=True, timeout=90)
            text = result.stdout + result.stderr
            (directory / "run.log").write_text(text)
            row.update(exit=result.returncode, output=text, assemblySha256=digest(assembly.read_bytes()))
            row["summary"] = json.loads(result.stdout)
            print(label + ": " + text[:1800], end="\n")
            return row

        require(run("baseline", files)["exit"] == 0, "Actual compiled source-helmet behavior failed.")
        if args.controls:
            for label, name, before, after, expected in mutations():
                text = files[name].decode()
                require(text.count(before) == 1, "Unique actual-source mutation anchor required: " + label)
                sources = dict(files)
                sources[name] = text.replace(before, after, 1).encode()
                row = run(label, sources)
                row["requiredNamedFailure"] = expected
                require(row["exit"] == 1 and "HELMET ASSERTION: " + expected in row["summary"].get("error", ""),
                        "Compiled semantic control survived or failed for wrong cause: " + label)
            require(run("final-baseline", files)["exit"] == 0, "Final actual baseline failed.")
        require(all((ROOT / name).read_bytes() == raw for name, raw in files.items() if name != "Program.cs"),
                "Production files changed during bounded qualification.")
        report["status"] = "passed"
        return 0
    except (AssertionError, OSError, subprocess.SubprocessError) as error:
        report["error"] = str(error)
        print(str(error), file=sys.stderr)
        return 1
    finally:
        (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    raise SystemExit(main())
