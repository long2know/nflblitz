#!/usr/bin/env python3
"""Compile actual free-ball counterfactuals, then require named behavioral failures; Core stays unchanged."""
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
    ("wrong-held-mesh", "tools/source-player/Runtime/RenderAssets.cs",
     "BindPoseLimbMesh mesh = data.FreeBall.BuildSourceMesh();", "BindPoseLimbMesh mesh = models[8][4];",
     "Actual renderer must upload the original medball2 free-ball mesh"),
    ("wrong-ball-material", "tools/source-player/Runtime/RenderAssets.cs",
     '"teams/" + SourceFreeBallData.TextureId,', '"teams/runtime/BALL.WMS",',
     "Actual free-ball renderer must bind HEDSHU/HEDSHU2 sole decals"),
    ("wrong-pivot", "Assets/Scripts/AssetImport/SourceFreeBallData.cs",
     "SNODE free_ball_rig[]={{0,0,0,0,{0,0,0}}};", "SNODE free_ball_rig[]={{1,0,0,0,{0,0,0}}};",
     "Actual free-ball draw must retain original medball2 geometry and unchanged source pivot"),
    ("wrong-pitch-axis", "Assets/Scripts/Presentation/SourceFreeBallRenderModel.cs",
     "directions.Sin(-state.Elevation)", "directions.Sin(state.Elevation)",
     "Source rotation must retain Y * X(-elevation) * Z(roll)"),
    ("wrong-rotation-order", "Assets/Scripts/Presentation/SourceFreeBallRenderModel.cs",
     "Multiply(Multiply(y, x), z)", "Multiply(Multiply(z, x), y)",
     "Source rotation must retain Y * X(-elevation) * Z(roll)"),
    ("future-spin-tick", "Assets/Scripts/Runtime/SourceFreeBallBinding.cs",
     "model.SourceBallRoll, model.SourceBallScale", "model.SourceBallRoll + 60, model.SourceBallScale",
     "Actual free-ball spin must copy Core roll at its actual source tick"),
    ("disconnected-ball-packet", "tools/source-player/Runtime/FrameMath.cs",
     "packet[4 + column * 4 + row] = draw.WorldMatrix[row * 4 + column];",
     "packet[4 + column * 4 + row] = column == row ? 1d : column == 3 ? draw.WorldMatrix[row * 4 + column] : 0d;",
     "Actual ball GPU packet must bind every original rotation/scale matrix component"),
]
JS_CASES = [
    ("scene-ignores-flight-matrix", "const matrix = frame.ballMatrix;", "const matrix = new Float32Array(identity);",
     "Actual scene must submit the C# source flight/spin matrix"),
    ("scene-wrong-ball-winding", "gl.frontFace(gl.CCW);", "gl.frontFace(gl.CW);",
     "Actual free-ball source321 winding must reach GPU culling"),
    ("scene-ignores-alternate-decal",
     "const texture = frame.ball[2] & 0x40 ? this.ball.alternateTexture : this.ball.texture;",
     "const texture = this.ball.texture;",
     "Actual free-ball alternate bit must select the original HEDSHU2 decal"),
    ("wrong-json-float-domain",
     "Math.fround(v) === Math.fround(ball[4 + (i % 4) * 4 + Math.floor(i / 4)])",
     "v === ball[4 + (i % 4) * 4 + Math.floor(i / 4)]",
     "JSON shortest-roundtrip C# float and exact double-marshalled binary32 must compare in the actual GPU domain"),
]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.absolute()
    assert output.is_relative_to(HERE / "artifacts") and not output.exists(), "Choose a new ignored owned mutation output."
    output.mkdir(parents=True)
    sources = sorted((ROOT / "Assets/Scripts").rglob("*.cs")) + sorted((HERE / "Runtime").glob("*.cs"))
    sources += [p for p in HERE.iterdir() if p.is_file() and p.suffix in (".cs", ".props", ".csproj")]
    sources.append(HERE / "dotnet.sh")
    before = {p.relative_to(ROOT).as_posix(): digest(p) for p in sources}
    rows = []
    for name, relative, old, new, expected in CASES:
        case, clone = output / name, output / name / "source"
        for path in sources:
            target = clone / path.relative_to(ROOT)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, target)
        path = clone / relative
        text = path.read_text()
        assert text.count(old) == 1, name
        path.write_text(text.replace(old, new, 1))
        wrapper = clone / "tools/source-player/dotnet.sh"
        environment = dict(os.environ, SOURCE_PLAYER_DOTNET=str(HERE / ".runtime/dotnet/dotnet"),
                           DOTNET_ROOT=str(HERE / ".runtime/dotnet"), NUGET_PACKAGES=str(HERE / ".runtime/nuget"))
        command = ["bash", str(wrapper), "build", str(clone / "tools/source-player/SourcePlayer.Checks.csproj"),
                   "-c", "Release", "--nologo", "-v", "quiet", "-warnaserror"]
        build = subprocess.run(command, cwd=clone, env=environment, capture_output=True, text=True, timeout=180)
        (case / "build.log").write_text(build.stdout + build.stderr)
        assert build.returncode == 0, "Compile failure is not a killed behavioral control: " + name
        assembly = clone / "tools/source-player/bin/SourcePlayer.Checks/Release/net10.0/SourcePlayer.Checks.dll"
        run = subprocess.run(["bash", str(wrapper), str(assembly), str(args.data.absolute()), "--free-ball-only"],
                             cwd=clone, env=environment, capture_output=True, text=True, timeout=180)
        log = run.stdout + run.stderr
        (case / "run.log").write_text(log)
        assert run.returncode != 0 and expected in log, "Control survived or failed for the wrong reason: " + name + "\n" + log[-3000:]
        rows.append({"name": name, "compileExit": build.returncode, "runExit": run.returncode,
                     "namedFailure": expected, "actualMutatedSourceSha256": digest(path)})
        shutil.rmtree(clone)
        print("KILLED after compilation:", name, flush=True)
    javascript = []
    for name, old, new, expected in JS_CASES:
        case = output / name
        case.mkdir()
        path = case / "renderer.mjs"
        text = (HERE / "wwwroot/renderer.js").read_text()
        assert text.count(old) == 1, name
        path.write_text(text.replace(old, new, 1))
        parse = subprocess.run(["node", "--check", str(path)], capture_output=True, text=True, timeout=30)
        assert parse.returncode == 0, "JS parse failure is not a killed behavioral control."
        run = subprocess.run(["node", "--test", str(HERE / "free-ball.renderer.test.mjs")],
                             env=dict(os.environ, SOURCE_PLAYER_RENDERER=str(path)),
                             capture_output=True, text=True, timeout=30)
        log = run.stdout + run.stderr
        (case / "run.log").write_text(log)
        assert run.returncode != 0 and expected in log, "Renderer control survived/wrong failure: " + name + "\n" + log
        javascript.append({"name": name, "parseExit": parse.returncode, "runExit": run.returncode,
                           "namedFailure": expected, "actualMutatedSourceSha256": digest(path)})
        path.unlink()
        print("KILLED after actual JS execution:", name, flush=True)
    assert before == {p.relative_to(ROOT).as_posix(): digest(p) for p in sources}
    (output / "result.json").write_text(json.dumps({"status": "passed", "compiledControls": rows,
        "actualRendererControls": javascript, "actualSourcesUnchanged": True,
        "noCompileFailureUsedAsMutantProof": True}, indent=2) + "\n")


if __name__ == "__main__":
    main()
