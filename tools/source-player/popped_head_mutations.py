#!/usr/bin/env python3
"""Compile then execute actual original-head asset/adapter counterfactuals; never mutate input trees."""
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
    ("wrong-neck-pivot", "Assets/Scripts/AssetImport/SourceTeamGeneratedModels.cs",
     "referenceModel.Nodes[SourceTeamPoppedHeadData.HeadJoint].BindPivot", "referenceModel.Nodes[0].BindPivot",
     "Actual mapped phead geometry must use original bind pivot"),
    ("raw-code-is-not-logical-slot", "Assets/Scripts/AssetImport/SourceTeamPoppedHeadData.cs",
     "LogicalTextureSlot = RawTextureCode / 4;", "LogicalTextureSlot = RawTextureCode;",
     "Popped-head raw4/logical HEDSHU1 mapping mismatch"),
    ("unbound-original-head", "tools/source-player/Runtime/RenderAssets.cs",
     "actor, state.Ball.CarrierId, draw, data.PoppedHead);", "actor, state.Ball.CarrierId, draw, null);",
     "Original popped-head SDL reconstruction data is required"),
    ("helmet-mesh-fallback", "tools/source-player/Runtime/RenderAssets.cs",
     "headParts = poppedHeadParts;", "headParts = null;",
     "Actual render binding must select phead geometry/HEDSHU1 at joint8"),
    ("wrong-custom-head-priority", "Assets/Scripts/AssetImport/SourceTeamPresentationModel.cs",
     "selectedPoppedHead = head == null && draw.HelmetPopped ? poppedHead : null;",
     "selectedPoppedHead = draw.HelmetPopped ? poppedHead : null;",
     "Every original station custom head must retain active-C priority"),
]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.absolute()
    if not output.is_relative_to(HERE / "artifacts") or output.exists():
        parser.error("Choose a new owned ignored source-player artifacts directory.")
    output.mkdir(parents=True)
    scripts = sorted((ROOT / "Assets/Scripts").rglob("*.cs"))
    preview = [p for p in HERE.rglob("*") if p.is_file() and
               not any(part in {"artifacts", "data", "bin", "obj", ".runtime", "__pycache__"}
                       for part in p.relative_to(HERE).parts)]
    sources = scripts + preview
    before = {str(p.relative_to(ROOT)): digest(p) for p in sources}
    rows = []
    for name, relative, old, new, expected in CASES:
        case = output / name
        clone = case / "source"
        for path in sources:
            target = clone / path.relative_to(ROOT)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, target)
        path = clone / relative
        text = path.read_text()
        assert text.count(old) == 1, name
        path.write_text(text.replace(old, new, 1))
        wrapper = clone / "tools/source-player/dotnet.sh"
        project = clone / "tools/source-player/SourcePlayer.Checks.csproj"
        environment = dict(os.environ, SOURCE_PLAYER_DOTNET=str(HERE / ".runtime/dotnet/dotnet"),
                           DOTNET_ROOT=str(HERE / ".runtime/dotnet"), NUGET_PACKAGES=str(HERE / ".runtime/nuget"))
        command = ["bash", str(wrapper), "build", str(project), "-c", "Release", "--nologo", "-v", "quiet"]
        build = subprocess.run(command, cwd=clone, env=environment, capture_output=True, text=True, timeout=180)
        (case / "build.log").write_text(build.stdout + build.stderr)
        assert build.returncode == 0, "A compilation failure is not a killed original-head control: " + name
        assembly = clone / "tools/source-player/bin/SourcePlayer.Checks/Release/net10.0/SourcePlayer.Checks.dll"
        run = subprocess.run(["bash", str(wrapper), str(assembly), str(args.data.absolute()), "--require-portable"],
                             cwd=clone, env=environment, capture_output=True, text=True, timeout=180)
        log = run.stdout + run.stderr
        (case / "run.log").write_text(log)
        assert run.returncode != 0 and expected in log, "Control survived or failed at the wrong contract: " + name + "\n" + log[-5000:]
        rows.append({"name": name, "compileExit": build.returncode, "runExit": run.returncode,
                     "namedFailure": expected, "actualMutatedSourceSha256": digest(path)})
        shutil.rmtree(clone)
        print("KILLED after successful compilation:", name, flush=True)
    case = output / "renderer-ignores-original-head"
    case.mkdir()
    path = case / "renderer.mjs"
    original = (HERE / "wwwroot/renderer.js").read_text()
    old = "const parts = joint === appearance.headJoint && appearance.headParts ? appearance.headParts : model[joint].parts;"
    assert original.count(old) == 1
    path.write_text(original.replace(old, "const parts = model[joint].parts;", 1))
    parsed = subprocess.run(["node", "--check", str(path)], capture_output=True, text=True, timeout=30)
    assert parsed.returncode == 0, "Parser failure is not a renderer control."
    run = subprocess.run(["node", "--test", str(HERE / "popped-head.renderer.test.mjs")],
                         env=dict(os.environ, SOURCE_PLAYER_RENDERER=str(path)), capture_output=True, text=True, timeout=30)
    log = run.stdout + run.stderr
    (case / "run.log").write_text(log)
    expected = "Actual scene draw must replace only the neck with phead"
    assert run.returncode != 0 and expected in log, log
    renderer = {"name": case.name, "parseExit": parsed.returncode, "runExit": run.returncode,
                "namedFailure": expected, "actualMutatedSourceSha256": digest(path)}
    path.unlink()
    assert before == {str(p.relative_to(ROOT)): digest(p) for p in sources}, "Actual current sources drifted during controls."
    (output / "result.json").write_text(json.dumps({"status": "passed", "compiledAssetControls": len(rows),
        "rendererControls": 1, "actualSourceUnchanged": True, "controls": rows, "renderer": renderer,
        "invocation": ["python3", *os.sys.argv]}, indent=2) + "\n")


if __name__ == "__main__":
    main()
