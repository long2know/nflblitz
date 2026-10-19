#!/usr/bin/env python3
"""Execute the actual renderer source and parsed JS mutants against named draw behavior."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    assert output.is_relative_to(ROOT / ".squad/.scratch") and not output.exists()
    output.mkdir(parents=True)
    production = ROOT / "tools/source-player/wwwroot/renderer.js"
    raw = production.read_bytes()
    original = raw.decode()
    report = {"schema": "source-impact-dust-executed-JS-controls/v1", "status": "failed",
              "sourceSha256": hashlib.sha256(raw).hexdigest(), "runs": [],
              "boundary": "Actual parsed/executed production JS methods; managed mock GL calls, not a GPU/native witness."}
    mutations = [
        ("reject-original-dirt-slots", "t < 27 && this.textures.has", "t < 17 && this.textures.has",
         "All ten original DIRT slots must be accepted by the actual renderer."),
        ("wrong-dirt-texture", "this.textures.get(`fire/${fx.textures[i]}`).texture",
         "this.textures.get('fire/0').texture",
         "Every actual dust draw must bind its original DIRT frame, never TRAIL0 or latest-only."),
        ("latest-only-dust", "for (let i = 0; i < fx.textures.length; i++) {",
         "for (let i = fx.textures.length - 1; i < fx.textures.length; i++) {",
         "Every actual dust draw must bind its original DIRT frame, never TRAIL0 or latest-only."),
        ("dust-no-depth-write", "gl.depthFunc(gl.ALWAYS); gl.depthMask(true);",
         "gl.depthFunc(gl.ALWAYS); gl.depthMask(false);", "Original dust draw must write depth."),
        ("wrong-alpha-blend", "gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);",
         "gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ONE, gl.ONE_MINUS_SRC_ALPHA);",
         "Original dust draw must apply SRC_ALPHA to both RGB and alpha blend sources."),
    ]
    try:
        def run(label, source):
            path = output / (label + ".mjs"); path.write_text(source)
            parsed = subprocess.run(["node", "--check", str(path)], capture_output=True, text=True, timeout=30)
            assert parsed.returncode == 0, "A syntax/compiler failure is not a semantic control: " + parsed.stderr
            env = dict(os.environ, SOURCE_PLAYER_RENDERER=str(path))
            command = ["node", "--test", str(ROOT / "tools/source-player/dust.renderer.test.mjs")]
            executed = subprocess.run(command, env=env, capture_output=True, text=True, timeout=60)
            text = executed.stdout + executed.stderr
            (output / (label + ".log")).write_text(text)
            row = dict(name=label, parsedExit=parsed.returncode, exit=executed.returncode,
                       sourceSha256=hashlib.sha256(source.encode()).hexdigest(), command=command, output=text)
            report["runs"].append(row)
            return row
        assert run("baseline", original)["exit"] == 0
        for label, before, after, expected in mutations:
            assert original.count(before) == 1, "Unique actual JS mutation anchor: " + label
            row = run(label, original.replace(before, after, 1))
            row["requiredNamedFailure"] = expected
            assert row["exit"] != 0 and expected in row["output"], "Executed JS control survived or failed for wrong behavior: " + label
        assert run("final-baseline", original)["exit"] == 0
        assert production.read_bytes() == raw, "Production JS changed during proof."
        report.update(status="passed", semanticControlsKilled=len(mutations))
        print("Actual parsed/executed JS: baseline passed; " + str(len(mutations)) + " named semantic controls killed; final baseline passed.")
        return 0
    except (AssertionError, OSError, subprocess.SubprocessError) as error:
        report["error"] = str(error); print(str(error), file=sys.stderr); return 1
    finally:
        (output / "result.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    raise SystemExit(main())
