#!/usr/bin/env python3
"""Asset-free actual workflow/input-closure guards; no CI or source simulation substitute."""
import importlib.util
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[3]
WORKFLOW = ROOT / ".github/workflows/game-build.yml"
ORIGINAL = ".squad/.scratch/SourceData/source-kernel-original"
FIXTURE = ".squad/.scratch/SourceData/source-kernel"
BASELINE_STEPS = (
    "Select the tested SDK",
    "Core tests with exact case inventory and source hash guard",
    "Test the inventory failure gates",
    "Test detached source match and input contracts",
    "Test offline roster exporter without original data",
    "Test immutable roster reader against synthetic input",
    "Test original playbook runner failure gates",
    "Fetch verified original playbook and AI inputs",
    "Compare original playbooks and AI decisions",
    "Compare original movement and evasion without Unity",
    "Test original environment conversion without original assets",
    "Fetch verified original C oracle inputs",
    "Test pinned-source oracle extraction",
    "Execute original C reference cases",
    "Fetch verified real source-kernel inputs",
    "Prepare original animation geometry roster and playbook services",
    "Test real-input fixture and ball-runner failure gates",
    "Compare source gameplay and host adapter",
    "Compare original ball and ordered source callers",
    "Compare original movement callers and post-animation physics",
    "Test source-player preparation without original assets",
    "Test original-animation input reader without original assets",
    "Test generated-player parsing and geometry",
    "Test source-camera behavior",
    "Test source team and impact helpers without original assets",
    "Test original HUD and cabinet interface without original assets",
    "Test complete interface caller and inventory guards",
    "Test actual movement bootstrap and source-frame binding",
    "Test original HUD resource preflight without assets or Unity",
    "Compare original source-camera matrices and complete animation identity",
    "Test source-camera frame and resource mutations",
    "Test original audio package reader without banks or Unity",
    "Test actor sound input preparation and SDK boundaries",
    "Compare original actor sound selectors and source mutants",
    "Test browser assertions and isolated runtime",
    "Validate Unity metadata and script references",
    "Build-tool unit checks",
    "Build and hosting shell syntax",
)


def require(value, message):
    if not value:
        raise AssertionError(message)


def validate_workflow(text):
    names = re.findall(r"^      - name: (.+)$", text, re.M)
    require(len(names) == len(set(names)), "CI step names must not hide duplicate or overwritten stages")
    require(set(BASELINE_STEPS).issubset(names), "every prior Core/fixture/camera/function/mutation/tool check is retained")
    jobs = re.findall(r"^  ([A-Za-z][A-Za-z0-9_-]*):\n    name:", text, re.M)
    require(jobs == ["core-and-tooling"], "sound closure retains the ordinary single job")
    require("timeout-minutes: 30" in text, "measured sound campaigns need bounded30-minute headroom")
    require(not re.search(r"^\s+if:", text, re.M), "required sound checks cannot be silently gated")
    require("/data/" not in text and "--unity-editor-data" not in text and "--unity-reference" not in text,
            "clean CI has no private authoring paths or Unity requirement")
    require("--no-mutations" not in text, "complete sound CI cannot disable mutation campaigns")
    required = (
        "python3 tests/parity/fetch_inputs.py --download --cache tests/source-oracles/.artifacts/original --output " + ORIGINAL,
        "python3 tests/parity/prepare_inputs.py --source-root " + ORIGINAL + " --output " + FIXTURE,
        "python3 tests/audio/vm/test_runner.py",
        "python3 -B tests/audio/vm/run.py --fixture-manifest " + FIXTURE + "/fixture.json --functions " +
        FIXTURE + "/audio-functions --source-root " + ORIGINAL + " --mutations",
        "python3 -B tests/audio/runtime/run.py --fixture-manifest " + FIXTURE +
        "/fixture.json --sound-functions " + FIXTURE + "/audio-functions/sound-functions.safn",
        "python3 -B tests/presentation/sound-runtime/run.py",
        "python3 -B tests/presentation/sound-runtime/mutations.py",
        "python3 tools/audio/fetch_function_inputs.py --source-root " + ORIGINAL,
        "python3 tools/audio/prepare_functions.py --name publication-original",
        "python3 tests/audio/functions/run.py --original tools/audio/.artifacts/functions/publication-original --reproduce publication-repeat --mutate",
        "python3 tests/presentation/movement-stage/run.py --original-source-root " + ORIGINAL +
        " --bundle157-root " + FIXTURE + "/movement-animation",
    )
    for command in required:
        require(command in text, "actual clean sound/fixture/camera/function command missing: " + command)
    require(text.index("Prepare original animation geometry") < text.index("Compare synchronous original sound VM"),
            "real69-pin fixture preparation precedes mandatory sound consumers")
    require("fetch_function_inputs.py --download" not in text,
            "four sound blobs are reused from verified69 input union, not downloaded redundantly")


class SoundWorkflow(unittest.TestCase):
    def test_actual_workflow_retains_complete_check_inventory(self):
        validate_workflow(WORKFLOW.read_text())

    def test_actual_input_union_preserves65_and_adds_only4_sound_inputs(self):
        spec = importlib.util.spec_from_file_location("actual_sound_ci_inputs", ROOT / "tests/parity/fetch_inputs.py")
        producer = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(producer)
        pins = producer.input_pins()
        self.assertEqual(69, len(pins))
        previous = set(pins) - {"sounds.c", "include/sndcalls.h", "Game.c", "mainproc.c"}
        self.assertEqual(65, len(previous))
        self.assertTrue(set(producer.CAMERA_INPUTS).issubset(previous))
        self.assertEqual("256acb7ddb838627312193ae19db66abd18ac146", pins["anim/misc99.wan"])

    def test_isolated_workflow_regressions_are_rejected(self):
        original = WORKFLOW.read_text()
        mutations = (
            ("timeout-minutes: 30", "timeout-minutes: 20"),
            (" --mutations\n", "\n"),
            ("python3 -B tests/presentation/sound-runtime/mutations.py", "true"),
            ("python3 -B tests/audio/runtime/run.py --fixture-manifest", "python3 -B tests/audio/runtime/run.py --no-mutations --fixture-manifest"),
            ("Test source-camera frame and resource mutations", "Dropped original camera checks"),
            ("--cache tests/source-oracles/.artifacts/original", "--cache /data/private-inputs"),
            ("fetch_function_inputs.py --source-root " + ORIGINAL, "fetch_function_inputs.py --download"),
            ("--functions " + FIXTURE + "/audio-functions", "--functions missing-data"),
        )
        for old, new in mutations:
            with self.subTest(old=old):
                self.assertIn(old, original)
                with self.assertRaises(AssertionError):
                    validate_workflow(original.replace(old, new, 1))


if __name__ == "__main__":
    unittest.main()
