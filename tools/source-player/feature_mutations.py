#!/usr/bin/env python3
"""Recompile only actual browser adapter mutations; Core/gameplay and private inputs remain readonly."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
CASES = [
    ("unbound-fire", "Runtime/KernelBinding.cs", "fireEffects: fire,", "fireEffects: null,",
     "Both independent named options must reach actual M1 factory unchanged."),
    ("unbound-meter", "Runtime/KernelBinding.cs", "kickMeterMetrics: meters", "kickMeterMetrics: null",
     "Both independent named options must reach actual M1 factory unchanged."),
    ("wrong-source-projection-sign", "Runtime/FrameMath.cs", "-2 * NormalCameraModel.ReferenceFocalPixels / width",
     "2 * NormalCameraModel.ReferenceFocalPixels / width", "World and fire GPU projection must share active geom.s negative X"),
    ("stale-fire-epoch", "Runtime/PlayerHost.cs",
     "Fire = new FirePresentation(Data.Fire, Options.FireEnabled, sink, Epoch, uploadTextures: false);",
     "// Deliberately retain prior source fire stream.",
     "Restart must create a new source frame epoch"),
    ("wrong-meter-highlight-target", "Runtime/FirePresentation.cs",
     'SourceKickMeterKind.Punt => "kick/punt/bar",',
     'SourceKickMeterKind.Punt => "kick/fg/marker",',
     "Source meter presentation predicate must target only FG ball/punt bar"),
]
JS_CASES = [
    ("wrong-hud-additive-intensity", "highlight ? 192 / 255 : 0", "highlight ? 96 / 255 : 0",
     "Actual HUD batching must preserve source additive192 highlight"),
    ("wrong-player-flash-shader", "color.rgb+playerFlashAdd", "color.rgb*playerFlashAdd",
     "Actual player shader must add source CCALPHA192"),
]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    if not output.is_relative_to(HERE / "artifacts") or output.exists():
        parser.error("Choose a new owned ignored mutation output.")
    output.mkdir(parents=True)
    public = [p for p in HERE.rglob("*") if p.is_file() and
              not any(part in {"bin", "obj", "data", "artifacts", ".runtime", "__pycache__"} for part in p.relative_to(HERE).parts)]
    before = {str(p.relative_to(HERE)): hashlib.sha256(p.read_bytes()).hexdigest() for p in public}
    rows = []
    for name, relative, old, new, expected in CASES:
        case = output / name
        clone = case / "source/tools/source-player"
        clone.mkdir(parents=True)
        for source in public:
            target = clone / source.relative_to(HERE)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
        source = clone / relative
        text = source.read_text()
        assert text.count(old) == 1, name
        source.write_text(text.replace(old, new, 1))
        environment = dict(os.environ, SOURCE_PLAYER_DOTNET=str(HERE / ".runtime/dotnet/dotnet"),
                           DOTNET_ROOT=str(HERE / ".runtime/dotnet"),
                           NUGET_PACKAGES=str(HERE / ".runtime/nuget"))
        build = ["bash", str(clone / "dotnet.sh"), "build", str(clone / "SourcePlayer.Checks.csproj"),
                 "-c", "Release", "-p:BlitzSourceRoot=" + str(ROOT) + "/", "--nologo", "-v", "quiet"]
        compiled = subprocess.run(build, cwd=clone.parents[1], env=environment, capture_output=True, text=True, timeout=180)
        (case / "build.log").write_text(compiled.stdout + compiled.stderr)
        assert compiled.returncode == 0, "Compilation failure is not a killed control: " + name
        assembly = clone / "bin/SourcePlayer.Checks/Release/net10.0/SourcePlayer.Checks.dll"
        run = subprocess.run(["bash", str(clone / "dotnet.sh"), str(assembly), str(args.data.resolve()), "--require-portable"],
                             cwd=clone.parents[1], env=environment, capture_output=True, text=True, timeout=180)
        log = run.stdout + run.stderr
        (case / "run.log").write_text(log)
        assert run.returncode != 0 and expected in log, "Control survived or failed for the wrong reason: " + name + "\n" + log[-4000:]
        rows.append({"name": name, "compileExit": compiled.returncode, "runExit": run.returncode,
                     "namedFailure": expected, "mutatedSourceSha256": hashlib.sha256(source.read_bytes()).hexdigest()})
        shutil.rmtree(case / "source")
        print("KILLED after compilation:", name)
    javascript = []
    for name, old, new, expected in JS_CASES:
        case = output / name
        case.mkdir()
        source = case / "renderer.mjs"
        text = (HERE / "wwwroot/renderer.js").read_text()
        assert text.count(old) == 1, name
        source.write_text(text.replace(old, new, 1))
        parsed = subprocess.run(["node", "--check", str(source)], cwd=ROOT, capture_output=True, text=True, timeout=30)
        (case / "parse.log").write_text(parsed.stdout + parsed.stderr)
        assert parsed.returncode == 0, "A parse failure is not a killed renderer control: " + name
        run = subprocess.run(["node", "--test", str(HERE / "renderer.test.mjs")], cwd=ROOT,
                             env=dict(os.environ, SOURCE_PLAYER_RENDERER=str(source)),
                             capture_output=True, text=True, timeout=30)
        log = run.stdout + run.stderr
        (case / "run.log").write_text(log)
        assert run.returncode != 0 and expected in log, "Renderer control survived or failed for the wrong reason: " + name
        javascript.append({"name": name, "parseExit": parsed.returncode, "runExit": run.returncode,
                           "namedFailure": expected, "mutatedSourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
                           "boundary": "Actual renderer JS/shader-source consumer control; not a GPU/native-pixel qualification."})
        source.unlink()
        print("KILLED after actual renderer execution:", name)
    assert before == {str(p.relative_to(HERE)): hashlib.sha256(p.read_bytes()).hexdigest() for p in public}
    (output / "result.json").write_text(json.dumps({"status": "passed", "compiledControls": len(rows),
        "rendererControls": len(javascript), "actualAdapterSourceUnchanged": True, "controls": rows,
        "javascriptControls": javascript, "invocation": ["python3", *os.sys.argv]}, indent=2) + "\n")


if __name__ == "__main__":
    main()
