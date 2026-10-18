#!/usr/bin/env python3
"""Compile actual fire production C#, compare extracted C fields/traces; no Unity executable."""
import argparse
import ast
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import subprocess
import sys
from xml.sax.saxutils import escape

import lineup_oracle

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).parent
spec = importlib.util.spec_from_file_location("source_fire_oracle", HERE / "oracle.py")
oracle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(oracle)


def managed_project(output):
    # F30's project was an ignored local file. Emit the exact small real-source
    # check project in the owned output so fresh worktrees can run these checks.
    sources = ["$(BlitzSourceRoot)Assets/Scripts/Contracts/*.cs"] + [
        "$(BlitzSourceRoot)Assets/Scripts/" + name for name in (
            "AssetImport/AuthorizedAssetFiles.cs", "AssetImport/GeneratedPlayerData.cs",
            "AssetImport/SourceFireSpfxData.cs", "Runtime/SourceFireSpfxRuntime.cs",
            "Presentation/SourceFireSpfxRenderModel.cs")] + [str(HERE / "Program.cs")]
    project = output / "FireSpfx.Checks.csproj"
    project.write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><LangVersion>7.3</LangVersion>
<EnableDefaultCompileItems>false</EnableDefaultCompileItems><ImplicitUsings>disable</ImplicitUsings>
<Nullable>disable</Nullable><UseSharedCompilation>false</UseSharedCompilation>
</PropertyGroup><ItemGroup>''' + "".join('<Compile Include="' + escape(name, {'"': "&quot;"}) + '" />' for name in sources) +
                      "</ItemGroup></Project>\n")
    return project


def shader_projection(source_root, points, reference):
    shader = source_root / "Assets/Scripts/Presentation/SourceFireSpfx.shader"
    renderer = source_root / "Assets/Scripts/Presentation/SourceFireSpfxRenderer.cs"
    text = shader.read_text()
    import re
    match = re.search(r"o\.position\s*=\s*float4\(([^,]+),([^,]+),\s*portableDepth\.z\s*,\s*depth\s*\)", text)
    assert match, "Actual SourceFireSpfx.shader position/portable-depth consumer changed."
    for token in ("SourceVector3 p = draw.CameraVertices[i]", "vertices[i] = new Vector3(p.X, p.Y, p.Z)",
                  "new Vector4(512f, frame.Basis.VerticalResolution, 664f",
                  'properties.SetVector("_SourceRaster", raster)', "commands.DrawMesh(mesh, Matrix4x4.identity, material"):
        assert token in renderer.read_text(), "Actual shader/packet consumer wiring missing: " + token
    expressions = []
    for expression in match.group(1, 2):
        for field, name in (("v.vertex.x", "cx"), ("v.vertex.y", "cy"), ("_SourceRaster.x", "width"),
                            ("_SourceRaster.y", "height"), ("_SourceRaster.z", "focal"), ("_SourceRaster.w", "yscale")):
            expression = expression.replace(field, name)
        expressions.append(ast.parse(expression.strip(), mode="eval").body)

    def scalar(node, values):
        if isinstance(node, ast.Constant) and type(node.value) in (int, float):
            return node.value
        if isinstance(node, ast.Name) and node.id in values:
            return values[node.id]
        if isinstance(node, ast.UnaryOp) and isinstance(node.op, (ast.UAdd, ast.USub)):
            value = scalar(node.operand, values)
            return -value if isinstance(node.op, ast.USub) else value
        if isinstance(node, ast.BinOp) and isinstance(node.op, (ast.Add, ast.Sub, ast.Mult, ast.Div)):
            left, right = scalar(node.left, values), scalar(node.right, values)
            if isinstance(node.op, ast.Add): return left + right
            if isinstance(node.op, ast.Sub): return left - right
            if isinstance(node.op, ast.Mult): return left * right
            return left / right
        raise AssertionError("Unsupported actual shader projection arithmetic; no guessed shader replacement.")

    rows, failures = [], []
    for point, expected in zip(points, reference):
        x, y, z = point["camera"]; height = point["verticalResolution"]
        values = dict(cx=x, cy=y, width=512, height=height, focal=664, yscale=2 / 3 if height == 256 else 1)
        actual = [(scalar(expressions[0], values) / z + 1) * 256,
                  (scalar(expressions[1], values) / z + 1) * height / 2, 1 / z]
        row = dict(point, actualShaderRaster=actual, originalAsmEquationRaster=expected)
        rows.append(row)
        try:
            compare(actual, expected, "SourceFireSpfx.shader horizontal projection/raster512x" + str(height))
        except AssertionError as error:
            failures.append(str(error))
    return dict(status="failed" if failures else "passed", shaderSha256=hashlib.sha256(shader.read_bytes()).hexdigest(),
                rendererSha256=hashlib.sha256(renderer.read_bytes()).hexdigest(), actualClipExpressions=list(match.group(1, 2)),
                actualConsumerWiringChecked=True, rows=rows, failures=failures, GPUOrShaderPixelsExecuted=False)


def compare(actual, expected, path=""):
    if isinstance(expected, dict):
        if set(actual) != set(expected):
            raise AssertionError(f"{path}: field inventory differs: {set(actual)} != {set(expected)}")
        for key in expected:
            compare(actual[key], expected[key], path + "." + key)
    elif isinstance(expected, list):
        if len(actual) != len(expected):
            raise AssertionError(f"{path}: count{len(actual)} != {len(expected)}")
        for index, (a, b) in enumerate(zip(actual, expected)):
            compare(a, b, path + f"[{index}]")
    elif isinstance(expected, float):
        if not math.isclose(actual, expected, rel_tol=2e-7, abs_tol=2e-5):
            raise AssertionError(f"{path}: actual{actual} != C{expected}")
    elif actual != expected:
        raise AssertionError(f"{path}: actual{actual!r} != C{expected!r}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--manifest-sha256", required=True)
    parser.add_argument("--original-player", type=Path, required=True)
    parser.add_argument("--original-object", type=Path, required=True)
    parser.add_argument("--original-process", type=Path, required=True)
    parser.add_argument("--original-geom", type=Path, required=True)
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    output = args.output or HERE / ".artifacts" / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    output = output.resolve()
    if not output.is_relative_to(HERE / ".artifacts") or output.exists():
        raise SystemExit("Choose a new ignored fire-spfx artifact directory.")
    output.mkdir(parents=True)
    env = dict(os.environ, TMPDIR=str(output), TMP=str(output), TEMP=str(output),
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    source_root = args.source_root.resolve()
    source_hashes = {p.relative_to(source_root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                     for p in (source_root / "Assets/Scripts").rglob("*.cs")}
    report = {"schema": "source-fire-spfx-production-proof/v1", "status": "failed", "sourceHashes": source_hashes,
              "wholeGameQualified": False, "nativeQualified": False, "UnityExecuted": False, "DSPQualified": False,
              "invocation": [sys.executable, *sys.argv], "bundleRoot": str(args.bundle.resolve()),
              "manifestSha256": args.manifest_sha256,
              "originalInputs": {"player": str(args.original_player.resolve()), "object": str(args.original_object.resolve()),
                                "process": str(args.original_process.resolve()), "geom": str(args.original_geom.resolve())}}
    try:
        project = managed_project(output)
        report["managedProjectSha256"] = hashlib.sha256(project.read_bytes()).hexdigest()
        command = ["dotnet", "build", str(project), "--no-restore", "-c", "Release",
                   "-o", str(output / "managed"), "-p:BlitzSourceRoot=" + str(source_root) + "/",
                   "-p:BaseIntermediateOutputPath=" + str(output / "obj") + "/", "--verbosity", "quiet", "--nologo"]
        build = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
        report["managedBuildCommand"] = command
        report["managedBuildExitCode"] = build.returncode
        (output / "build.log").write_text(build.stdout + build.stderr)
        if build.returncode and "NETSDK1004" in build.stdout + build.stderr:
            (output / "build-missing-assets.log").write_text(build.stdout + build.stderr)
            feed = output / "empty-feed"
            feed.mkdir()
            restore_command = ["dotnet", "restore", str(project), "--source", str(feed),
                               "-p:BaseIntermediateOutputPath=" + str(output / "obj") + "/",
                               "-p:NuGetAudit=false", "--verbosity", "quiet"]
            restored = subprocess.run(restore_command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
            report["offlineRestoreCommand"] = restore_command
            report["offlineRestoreExitCode"] = restored.returncode
            (output / "restore-missing-assets.log").write_text(restored.stdout + restored.stderr)
            if restored.returncode:
                raise AssertionError(restored.stdout + restored.stderr)
            build = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, text=True, timeout=180)
            report["managedBuildExitCode"] = build.returncode
            (output / "build.log").write_text(build.stdout + build.stderr)
        if build.returncode:
            raise AssertionError(build.stdout + build.stderr)
        samples = [dict(camera=[side, 2, 100], verticalResolution=height)
                   for height in (384, 256) for side in (1, -1)]
        reference = oracle.projection_reference(args.original_geom.resolve(), samples, output / "shader-equation")
        report["shaderProjection"] = shader_projection(source_root, samples, reference)
        for failure in report["shaderProjection"]["failures"]:
            print("ACTUAL SHADER PROJECTION FAILURE: " + failure, file=sys.stderr)
        traces = output / "production.json"
        run = subprocess.run(["dotnet", str(output / "managed/FireSpfx.Checks.dll"), str(args.bundle.resolve()),
                              args.manifest_sha256, str(HERE / "cases.json"), str(traces), str(source_root)],
                             cwd=ROOT, env=env, capture_output=True, text=True, timeout=120)
        report["productionExitCode"] = run.returncode
        (output / "production.log").write_text(run.stdout + run.stderr)
        print((run.stdout + run.stderr)[-6000:], end="")
        if run.returncode:
            raise AssertionError("Actual production C# assertions failed.")
        actual = json.loads(traces.read_text())
        points = actual["render"]["projection"]
        reference = oracle.projection_reference(args.original_geom.resolve(), points, output / "packet-equation")
        compare([point["raster"] for point in points], reference, "geom.s.actual-consumer-projection")
        report["consumerProjectionCases"] = len(points)
        report["consumerShaderProjection"] = shader_projection(source_root, points, reference)
        if report["shaderProjection"]["status"] != "passed" or report["consumerShaderProjection"]["status"] != "passed":
            raise AssertionError("Actual SourceFireSpfx.shader horizontal projection differs from active geom.s: " +
                                 "; ".join(report["shaderProjection"]["failures"] + report["consumerShaderProjection"]["failures"]))
        report["activeGeomSha256"] = oracle.GEOM_SHA256
        report["nativeProjectionPixelsQualified"] = False
        if (actual["retainedAssertions"] != 18302 or actual["projectionRevisionAssertions"] != 418 or
                actual["lineupScopeAssertions"] != 80 or
                actual["assertions"] != actual["retainedAssertions"] + actual["projectionRevisionAssertions"] + actual["lineupScopeAssertions"]):
            raise AssertionError("Measured assertion categories must retain18302, add418 projection, add80 scoped FLASH.")
        lineup = lineup_oracle.run(args.original_player.resolve().parent, output / "lineup-oracle")
        compare(actual["lineupClears"], lineup["cases"], "FLASH.original-lineup-side-write-set")
        expected = oracle.run(args.original_player.resolve(), HERE / "cases.json",
                              args.bundle.resolve() / "geometry.bytes", args.original_object.resolve(),
                              args.original_process.resolve(), output / "oracle")
        for case in actual["cases"]:
            reference = expected["cases"][case["name"]]
            compare(case["rows"], reference["rows"], case["name"] + ".rows")
            compare(case["random"], reference["random"], case["name"] + ".random")
            compare(case["childLines"], reference["childLines"], case["name"] + ".childLines")
            if reference["returnCause"] != 1:
                raise AssertionError("Original case did not reach normal shell return.")
            compare(case["returnCauses"], [reference["returnCause"]], case["name"] + ".returnDieCause")
        compare(actual["render"]["cameraVertices"], expected["render"]["cameraVertices"], "draw_spfx.cameraVertices")
        compare(actual["render"]["symbolicStates"], expected["render"]["symbolicStates"], "draw_spfx.actualStateCallOrder")
        for emitter in actual["emitters"]:
            reference = expected["emitters"][emitter["name"]]
            compare({k: emitter[k] for k in ("requests", "random")}, reference, "maybe_plyr_fire." + emitter["name"])
        compare(actual["ballSequence"], expected["ballSequence"], "ball_proc.original-selector-body-and-fire-triplet")
        compare(actual["controls"], expected["controls"], "original-Goose-object-process-controls")
        report.update(status="passed", assertions=actual["assertions"], comparedSourceCases=len(actual["cases"]),
                      retainedAssertions=actual["retainedAssertions"], projectionRevisionAssertions=actual["projectionRevisionAssertions"],
                      lineupScopeAssertions=actual["lineupScopeAssertions"],
                      comparedOriginalLineupScopes=len(lineup["cases"]), lineupSourceSha256=lineup["source_sha256"],
                      comparedOriginalEmitterCases=len(actual["emitters"]), comparedOriginalBallSelectorSteps=len(actual["ballSequence"]),
                      comparedOriginalControlStages=len(actual["controls"]), originalObjectSourceSha256=expected["objectSourceSha256"],
                      originalProcessSourceSha256=expected["processSourceSha256"],
                      originalSourceSha256=expected["sourceSha256"], fieldsCompared="all sleep/frame/xyz/vy/rawflags/children/RNG bound+line+value+tick; actual draw_spfx cm/state calls")
        print("EXTRACTED-C PASS: " + str(len(actual["cases"])) + " cases; exact production object fields, sleep, ordered draws, children, basis/material.")
        return 0
    except (AssertionError, OSError, subprocess.SubprocessError, KeyError, ValueError) as error:
        report["error"] = str(error); print("FIRE PROOF FAILED: " + str(error), file=sys.stderr); return 1
    finally:
        (output / "evidence.json").write_text(json.dumps(report, indent=2) + "\n")
        print("Evidence: " + str(output.relative_to(ROOT)))


if __name__ == "__main__":
    raise SystemExit(main())
