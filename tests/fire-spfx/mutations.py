#!/usr/bin/env python3
"""Recompile mutated copies of ACTUAL production files. Compilation failure is never a killed mutant."""
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
RUNTIME = "Assets/Scripts/Runtime/SourceFireSpfxRuntime.cs"
MODEL = "Assets/Scripts/Presentation/SourceFireSpfxRenderModel.cs"
SHADER = "Assets/Scripts/Presentation/SourceFireSpfx.shader"
MUTANTS = [
    ("wrong-parent-delay", RUNTIME, "Sleep(p, p.Args.A2, 4552);", "Sleep(p, p.Args.A2 + 1, 4552);", "parent sleeps original delay"),
    ("wrong-frame-advance", RUNTIME, "current.Frame = p.Cursor; p.State = 1;", "current.Frame = Math.Max(0, p.Cursor - 1); p.State = 1;", ".rows"),
    ("wrong-entry-RNG-bound", RUNTIME, "Draw(5, Site(9540", "Draw(4, Site(9540", "ordered entry RNG bounds"),
    ("wrong-burst-child-order", RUNTIME, "SpawnPuff(p.Args.A0, 7, 4533); SpawnPuff(p.Args.A0, 11, 4534);",
     "SpawnPuff(p.Args.A0, 11, 4533); SpawnPuff(p.Args.A0, 7, 4534);", "childJoints"),
    ("wrong-cache-transpose", RUNTIME, "j.Z * m[8] + c.X", "j.Z * m[2] + c.X", "paired cache"),
    ("wrong-material-depth-write", MODEL, "public bool DepthWrite { get { return true; } }",
     "public bool DepthWrite { get { return false; } }", "depth/write/alpha-test"),
    ("wrong-color-combine", MODEL, "texel.R * texel.A, texel.G * texel.A", "texel.R, texel.G * texel.A", "texture times alpha precedes"),
    ("wrong-equal-priority-insertion", RUNTIME, "objects.Add(obj);", "objects.Insert(0, obj);", "equal-priority objects"),
    ("wrong-future-global-hide", RUNTIME, "Position = position, Frame = 0", "Position = position, Frame = 0, Hidden = objects.Exists(o => o.Hidden)", "global future-object latch"),
    ("wrong-sound-double-call", RUNTIME, 'PlayerSound(site, ref ordinal, 192, "standard_catch", 3605, actorId);',
     "ordinal++;", "two ordered calls"),
    ("omitted-pre-gate-RNG", RUNTIME, 'int divisor = Draw(4, site.At("maybe_plyr_fire", 4481',
     'if (site.OriginalPcount % 4 != 0) return;\n            int divisor = Draw(4, site.At("maybe_plyr_fire", 4481', "draws before failed pcount modulo"),
    ("wrong-resume-sleep", RUNTIME, "p.Sleep &= 0x7fff; Record(\"Resume\", command.Site, p, p.ObjectSerial, p.Sleep, \"preserve-short-continuation\");",
     "p.Sleep = 1; Record(\"Resume\", command.Site, p, p.ObjectSerial, p.Sleep, \"preserve-short-continuation\");", "clears only suspension bit"),
    ("wrong-process-head-order", RUNTIME, "bySerial.Add(serial, processes.AddFirst(p));",
     "bySerial.Add(serial, processes.AddLast(p));", "known head insertion reverses births"),
    ("wrong-projection-sign", MODEL, "256f - camera.X * (664f * reciprocal)",
     "256f + camera.X * (664f * reciprocal)", "geom.s horizontal projection +X raster512x384"),
    ("wrong-shader-projection-sign", SHADER, "float4(-2 * _SourceRaster.z * v.vertex.x",
     "float4(2 * _SourceRaster.z * v.vertex.x", "SourceFireSpfx.shader horizontal projection/raster512x384[0]"),
    ("wrong-global-lineup-flash-clear", RUNTIME,
     "for (int actor = scope.FirstActor; actor < scope.EndActorExclusive; actor++) WriteFlash(actor, false, command.Site);",
     "for (int actor = 0; actor < 14; actor++) WriteFlash(actor, false, command.Site);",
     "FLASH SIDE0 untouched other-team raw flags")
]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--manifest-sha256", required=True)
    parser.add_argument("--original-player", type=Path, required=True)
    parser.add_argument("--original-object", type=Path, required=True)
    parser.add_argument("--original-process", type=Path, required=True)
    parser.add_argument("--original-geom", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    assert output.is_relative_to(HERE / ".artifacts") and not output.exists()
    output.mkdir(parents=True)
    originals = {p.relative_to(ROOT): p.read_bytes() for p in (ROOT / "Assets/Scripts").rglob("*") if p.is_file()}
    rows = []
    for name, relative, before, after, required in MUTANTS:
        case = output / name; clone = case / "source"; clone.mkdir(parents=True)
        try:
            for path, raw in originals.items():
                target = clone / path; target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(raw)
            target = clone / relative; raw = target.read_text()
            assert raw.count(before) == 1, "Mutation target must be unique: " + name
            target.write_text(raw.replace(before, after, 1))
            command = [sys.executable, str(HERE / "run.py"), "--bundle", str(args.bundle.resolve()),
                       "--manifest-sha256", args.manifest_sha256, "--original-player", str(args.original_player.resolve()),
                       "--original-object", str(args.original_object.resolve()), "--original-process", str(args.original_process.resolve()),
                       "--original-geom", str(args.original_geom.resolve()),
                       "--source-root", str(clone), "--output", str(case / "proof")]
            env = dict(os.environ, TMPDIR=str(case), TMP=str(case), TEMP=str(case))
            result = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=240)
            (case / "runner.log").write_text(result.stdout + result.stderr)
            evidence = json.loads((case / "proof/evidence.json").read_text())
            log = (case / "proof/production.log").read_text() + result.stdout + result.stderr
            assert evidence.get("managedBuildExitCode") == 0, "Compile failure is NOT killed: " + name + "\n" + log[-3000:]
            assert result.returncode != 0 and required in log, "Mutant survived/failed for wrong cause: " + name + "\n" + log[-4000:]
            if name == "wrong-projection-sign":
                assert "actual=262.64 expected=249.36" in log, "The real CPU projection must fail its named numeric output."
            if name == "wrong-shader-projection-sign":
                shader = evidence["shaderProjection"]
                assert evidence["productionExitCode"] == 0 and shader["status"] == "failed"
                row = shader["rows"][0]
                assert row["actualShaderRaster"][0] > 256 and row["originalAsmEquationRaster"][0] < 256
                assert shader["actualConsumerWiringChecked"] is True
            rows.append({"mutant": name, "status": "killed-after-successful-compilation", "requiredObservedFailure": required,
                         "productionSource": relative, "mutantSha256": hashlib.sha256(target.read_bytes()).hexdigest(),
                         "originalSha256": hashlib.sha256(originals[Path(relative)]).hexdigest(), "evidence": str(case / "proof/evidence.json"),
                         "shaderProjection": evidence.get("shaderProjection") if name == "wrong-shader-projection-sign" else None})
            print("KILLED (compiled): " + name)
        finally:
            shutil.rmtree(clone)
    for path, raw in originals.items():
        assert (ROOT / path).read_bytes() == raw, "Actual shared production file changed during mutations: " + str(path)
    (output / "result.json").write_text(json.dumps({"status": "passed", "compiledKilled": len(rows), "productionUnchanged": True,
                                                  "mutants": rows, "invocation": [sys.executable, *sys.argv]}, indent=2) + "\n")


if __name__ == "__main__":
    main()
