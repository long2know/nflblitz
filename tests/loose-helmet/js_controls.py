#!/usr/bin/env python3
"""Parse and execute semantic controls against the actual helmet renderer, not hand-built JS copies."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "tools/source-player/wwwroot/renderer.js"
TEST = ROOT / "tools/source-player/helmet.renderer.test.mjs"


def mutations():
    return [
        ("disconnected-effect-consumer", "const helmets = SourceRenderer.prototype.helmetPackets.call(this, styles);",
         "const helmets = [];", "authoritative original PHELM and captured team decal2 must reach the effect frame"),
        ("missing-original-mesh-pin", "asset?.mesh === 'original/loose-helmet/limb_phelm_obj' &&",
         "true &&", "authoritative original PHELM and captured team decal2 must reach the effect frame"),
        ("wrong-ratio-accepted", "h.integerAspect === 4 &&", "true &&",
         "reject wrong team atlas, ratio, integer fields and detached/stale matrix components"),
        ("detached-matrix-ignored",
         "h.worldMatrix?.length === 12 && h.worldMatrix.every((v, i) => Number.isFinite(Math.fround(v)) &&\n"
         "          Math.fround(v) === Math.fround(packet[4 + (i % 4) * 4 + Math.floor(i / 4)])),",
         "true,", "reject wrong team atlas, ratio, integer fields and detached/stale matrix components"),
        ("helmet-ttl", "const helmets = this.fx?.helmets || [];",
         "const helmets = (this.fx?.helmets || []).filter(h => h.metadata.flightTime < 30);",
         "detached original mesh draws while ball is held, with immutable flight matrix and source321"),
        ("skip-real-scene", "    SourceRenderer.prototype.drawLooseHelmets.call(this, identityPass, poppedHeadIdentityPass, observeMaterial);",
         "", "detached original mesh draws while ball is held, with immutable flight matrix and source321"),
        ("helmet-identity-matrix", "this.draw(this.fx.helmetAsset.mesh, h.texture, helmet.matrix,",
         "this.draw(this.fx.helmetAsset.mesh, h.texture, identity,",
         "detached original mesh draws while ball is held, with immutable flight matrix and source321"),
        ("helmet-own-white", "const white = Boolean(this.fx.metadata.globalBallFlags & 1);",
         "const white = Boolean(h.rawFlags & 1);",
         "WHITE is current global ball flags, never helmet own flags or a renderer-side phase"),
        ("dust-depth-for-helmet", "gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.LESS); gl.depthMask(true);",
         "gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.ALWAYS); gl.depthMask(true);",
         "CC alpha, depth LESS/write and separate alpha blend must not use dust state"),
        ("wrong-actor-texture-accepted", "this.appearances.get(appearances[helmet.metadata.actor])?.textures[2] === helmet.metadata.texture,",
         "true,", "current selected actor texture and global ball state remain authenticated at scene submission"),
    ]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    assert output.is_relative_to(ROOT / ".squad/.scratch") and not output.exists(), "Choose a new owned ignored JS control output."
    output.mkdir(parents=True)
    original = SOURCE.read_text()
    report = {"schema": "source-loose-helmet-executed-JS-controls/v1", "status": "failed", "runs": [],
              "rendererSha256": hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
              "testsSha256": hashlib.sha256(TEST.read_bytes()).hexdigest(), "GPUExecuted": False, "nativeQualified": False}
    try:
        for label, before, after, expected in [("baseline", None, None, None), *mutations(), ("final-baseline", None, None, None)]:
            path = output / (label + ".mjs")
            if before is not None:
                assert original.count(before) == 1, "Unique actual JS semantic mutation anchor required: " + label
            path.write_text(original if before is None else original.replace(before, after, 1))
            parsed = subprocess.run(["node", "--check", str(path)], capture_output=True, text=True, cwd=ROOT, timeout=30)
            assert parsed.returncode == 0 and not (parsed.stdout + parsed.stderr), "Parser failures never count as semantic controls."
            env = dict(os.environ, SOURCE_PLAYER_RENDERER=str(path))
            run = subprocess.run(["node", "--test", str(TEST)], env=env, cwd=ROOT, capture_output=True, text=True, timeout=45)
            text = run.stdout + run.stderr
            (output / (label + ".log")).write_text(text)
            row = {"name": label, "parseExit": parsed.returncode, "executionExit": run.returncode,
                   "expectedFailedTest": expected, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
            report["runs"].append(row)
            if expected is None:
                assert run.returncode == 0 and "tests 7" in text and "pass 7" in text, text
            else:
                assert run.returncode == 1 and expected in text and ("AssertionError" in text or "ERR_ASSERTION" in text), text
            print(label + ": " + ("passed" if expected is None else "semantic control killed"))
        assert SOURCE.read_text() == original, "Actual renderer changed during bounded JS proof."
        report["status"] = "passed"
        return 0
    except (AssertionError, OSError, subprocess.SubprocessError) as error:
        report["error"] = str(error)
        print(str(error))
        return 1
    finally:
        (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    raise SystemExit(main())
