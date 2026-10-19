#!/usr/bin/env python3
"""Compile actual production counterfactuals; require their named behavioral failures, never compiler kills."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
RUNTIME = "Assets/Scripts/Runtime/SourceFireSpfxRuntime.cs"
CONTRACT = "Assets/Scripts/Contracts/SourceFireSpfxContracts.cs"
MODEL = "Assets/Scripts/Presentation/SourceFreeBallRenderModel.cs"
CASES = [
    ("missing-btrail-birth", RUNTIME, [
        ("if (BallEffect == SourceFireBallEffect.PlayerCloud || BallEffect == SourceFireBallEffect.BallTrail)",
         "if (BallEffect == SourceFireBallEffect.PlayerCloud)")],
     "BTRAIL must enqueue the original balltrl football-mesh process"),
    ("pcloud-does-not-fall-through", RUNTIME, [
        ("if (BallEffect == SourceFireBallEffect.PlayerCloud || BallEffect == SourceFireBallEffect.BallTrail)",
         "if (BallEffect == SourceFireBallEffect.BallTrail)")],
     "PCLOUD/BTRAIL queue every selected wake"),
    ("queue-time-copy", RUNTIME, [
        ("internal SourceFireArgs Args;", "internal SourceFireArgs Args;\n            internal SourceFireBallRenderSample QueuedBall;"),
        ("Args = command.Args, Group = command.GroupId", "Args = command.Args, Group = command.GroupId,\n" +
         "                    QueuedBall = command.Entry == SourceFireEntry.BallTrail ? host.ReadBallRender() : default(SourceFireBallRenderSample)"),
        ("SourceFireBallRenderSample sample = host.ReadBallRender();", "SourceFireBallRenderSample sample = p.QueuedBall;")],
     "Queue time must not capture a ball or create a draw"),
    ("position-only-fallback", RUNTIME, [
        ("SourceFireBallRenderSample sample = host.ReadBallRender();",
         "SourceFireBallRenderSample sample = new SourceFireBallRenderSample(tick.SourceTick, host.ReadBallWorld().WorldPosition," +
         " 0, 0, 0, 1, 0, \"position-only compiled control\");")],
     "Entry must read XYZ and all angles AFTER queue time"),
    ("live-tracking-copy", RUNTIME, [
        ("obj.Stage = p.Cursor;", "obj.Stage = p.Cursor;\n                obj.EntrySample = host.ReadBallRender();")],
     "Trail XYZ/fwd/fwd2/phi must stay static"),
    ("two-tick-stage-sleep", RUNTIME, [
        ("Sleep(p, 1, 10557 + 3 * p.Cursor);", "Sleep(p, 2, 10557 + 3 * p.Cursor);")],
     "Every visible stage, including155/1.85, must perform one sleep1"),
    ("delete-before-fifth-sleep", RUNTIME, [
        ("if (p.Cursor == p.End)\n                {", "if (p.Cursor == p.End - 1)\n                {")],
     "The sixth unsuspended entry wake must delete"),
    ("age-while-suspended", RUNTIME, [
        ("if ((short)p.Sleep <= 0) continue;", "if ((short)p.Sleep <= 0) p.Sleep &= 0x7fff;")],
     "Exactly five original alpha/scale stages must advance only on unsuspended source wakes"),
    ("inherit-local-flags", CONTRACT, [
        ("public int LocalFlags { get { return 0x40; } }", "public int LocalFlags { get { return EntrySample.RawFlags | 0x40; } }")],
     "Copy all draw fields at entry, then overwrite local flags/scale"),
    ("snapshot-white-not-global", MODEL, [
        ("trail.LocalFlags | (globalBallFlags & 1), pupBigBall", "trail.LocalFlags | (entry.RawFlags & 1), pupBigBall")],
     "draw_ball must use current GLOBAL carrier/Phantom/White"),
    ("ignore-global-phantom", MODEL, [
        ("currentCarrier == -1 || (globalBallFlags & 0x800) != 0", "currentCarrier == -1")],
     "draw_ball must use current GLOBAL carrier/Phantom/White"),
    ("ignore-draw-time-bigball", MODEL, [
        ("trail.LocalFlags | (globalBallFlags & 1), pupBigBall", "trail.LocalFlags | (globalBallFlags & 1), 0")],
     "GLOBAL pup_bigball is evaluated at draw time"),
    ("retain-trails-on-reset", RUNTIME, [
        ("trails.Clear();", "if (trails.Count < 0) trails.Clear();")],
     "A real host restart must clear processes, objects, boundaries and selector"),
]
JS_CASES = [
    ("skip-ghost-draws", "if (!trail.ball[0]) continue;", "if (trail.ball[0] >= 0) continue;",
     "The actual scene must draw the main football and every source football ghost"),
    ("wrong-ghost-decal", "const texture = this.ball.alternateTexture, white", "const texture = this.ball.texture, white",
     "Every actual ghost must bind original HEDSHU2"),
    ("ignore-ghost-matrix", "this.draw(this.ball.mesh, texture, trail.matrix, identityPass",
     "this.draw(this.ball.mesh, texture, identity, identityPass",
     "Every ghost must submit its retained source matrix"),
    ("opaque-ghosts", "white ? 1 : trail.ball[3] / 255", "white ? 1 : 1",
     "Actual trail material must use its constant source alpha stage"),
    ("invent-white-fade", "white ? 1 : trail.ball[3] / 255", "trail.ball[3] / 255",
     "GLIDE3 global White must overwrite CC_ALPHA withFF"),
    ("blend-only-no-rgb-alpha", "if(textureTimesAlpha) color.rgb*=color.a;",
     "if(textureTimesAlpha) color.rgb*=1.0;",
     "Native API contract is texture-times-CC-alpha THEN SRC_ALPHA blend"),
]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--manifest-sha256", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    assert output.is_relative_to(HERE / ".artifacts") and not output.exists(), "Choose new owned control output."
    output.mkdir(parents=True)
    sources = sorted((ROOT / "Assets/Scripts").rglob("*.cs"))
    before = {str(p.relative_to(ROOT)): sha(p) for p in sources}
    renderer = ROOT / "tools/source-player/wwwroot/renderer.js"
    renderer_before = sha(renderer)
    rows, js_rows = [], []
    report = {"schema": "source-ball-trails-compiled-controls/v1", "status": "failed",
              "compiledControls": rows, "rendererControls": js_rows, "noCompileFailureUsedAsKill": True,
              "baselineProductionSha256": before, "baselineRendererSha256": renderer_before,
              "testSha256": sha(HERE / "Program.cs")}
    try:
        for name, relative, changes, failure in CASES:
            case = output / name
            clone = case / "source"
            for path in sources:
                target = clone / path.relative_to(ROOT)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(path, target)
            target = clone / relative
            text = target.read_text()
            for old, new in changes:
                assert text.count(old) == 1, "Control anchor not unique: " + name
                text = text.replace(old, new, 1)
            target.write_text(text)
            command = ["python3", "-B", str(HERE / "run.py"), "--bundle", str(args.bundle.resolve()),
                       "--manifest-sha256", args.manifest_sha256, "--source-root", str(clone), "--output", str(case / "proof")]
            result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, timeout=180)
            (case / "control.log").write_text(result.stdout + result.stderr)
            evidence = json.loads((case / "proof/result.json").read_bytes())
            assert evidence.get("compiled") is True, "Compilation failure is not a killed behavioral control: " + name
            log = (case / "proof/production.log").read_text()
            assert result.returncode != 0 and failure in log, "Control survived or failed for wrong reason: " + name + "\n" + log[-2500:]
            rows.append({"name": name, "compiled": True, "runExit": result.returncode, "namedFailure": failure,
                         "mutatedSourceSha256": sha(target), "proofSha256": sha(case / "proof/result.json")})
            shutil.rmtree(clone)
            print("KILLED after actual compilation:", name, flush=True)
        for name, old, new, failure in JS_CASES:
            case = output / name
            case.mkdir()
            path = case / "renderer.mjs"
            text = (ROOT / "tools/source-player/wwwroot/renderer.js").read_text()
            assert text.count(old) == 1, "Actual JS control anchor not unique: " + name
            path.write_text(text.replace(old, new, 1))
            parsed = subprocess.run(["node", "--check", str(path)], capture_output=True, text=True, timeout=30)
            assert parsed.returncode == 0, "Parse failure is not a killed renderer control: " + name
            result = subprocess.run(["node", "--test", str(ROOT / "tools/source-player/ball-trails.renderer.test.mjs")],
                                    env=dict(os.environ, SOURCE_PLAYER_RENDERER=str(path)), cwd=ROOT,
                                    capture_output=True, text=True, timeout=30)
            log = result.stdout + result.stderr
            (case / "run.log").write_text(log)
            assert result.returncode != 0 and failure in log, "Renderer control survived/wrong failure: " + name + "\n" + log[-3000:]
            js_rows.append({"name": name, "parseExit": parsed.returncode, "runExit": result.returncode,
                            "namedFailure": failure, "mutatedSourceSha256": sha(path)})
            path.unlink()
            print("KILLED after actual renderer execution:", name, flush=True)
        assert before == {str(p.relative_to(ROOT)): sha(p) for p in sources}
        assert renderer_before == sha(renderer)
        report.update(status="passed", actualProductionUnchanged=True)
        return 0
    finally:
        (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    raise SystemExit(main())
