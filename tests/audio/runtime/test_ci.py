#!/usr/bin/env python3
"""Asset-free workflow guards for historical Sound inputs and opt-in Contact/C2."""
import importlib.util
from pathlib import Path
import re
import shlex
import sys
import unittest

ROOT = Path(__file__).resolve().parents[3]
WORKFLOW = ROOT / ".github/workflows/game-build.yml"
ORIGINAL = ".squad/.scratch/SourceData/source-kernel-original"
FIXTURE = ".squad/.scratch/SourceData/source-kernel"
CURRENT = ".squad/.scratch/SourceData/source-kernel-contact272"
RESOURCES = ".squad/.scratch/SourceData/contact-profile"
EXTRA9 = ".squad/.scratch/SourceData/c2-extra9"
OLD9 = ".squad/.scratch/SourceData/c2-old9"
M1 = ".squad/.scratch/SourceData/c2-m1"
PUBLIC_FIXTURE = "tests/movement-callers/fixtures/canonical-public-12000.jsonl.gz"
PUBLIC_REPLAY = ".squad/.scratch/SourceData/m1-public-12000.jsonl"
FETCH_COMMAND = ("python3 tests/parity/fetch_inputs.py --profile contact272 --download "
                 "--cache tests/source-oracles/.artifacts/original --output " + ORIGINAL)
PREPARE_COMMANDS = (
    f"python3 -B tests/contact/prepare.py --source-root {ORIGINAL} --operand-root {ORIGINAL} --base-bundle {FIXTURE}/movement-animation --download",
    f"python3 -B tools/animation/prepare_contact_profile.py prepare --source-root {ORIGINAL} --base-bundle {FIXTURE}/movement-animation --destination {RESOURCES}/animation272",
    f"python3 -B tools/assets/prepare_cinematics.py --source-root {ORIGINAL} --destination {RESOURCES}/impact-base",
    f"python3 -B tools/animation/prepare_contact_impact.py --base-catalog {RESOURCES}/impact-base/impact-presentation.json --plyrseq {ORIGINAL}/plyrseq.c --contact-programs Assets/Scripts/Gameplay/SourceAnimationPrograms.Contact.cs --animation-manifest {RESOURCES}/animation272/export-inventory.json --destination {RESOURCES}/impact272",
    f"python3 -B tests/parity/prepare_contact_inputs.py --profile contact272 --legacy-fixture {FIXTURE}/fixture.json --production-bundle {RESOURCES}/animation272 --supplemental-bundle tests/contact/.artifacts/contact-extra --impact {RESOURCES}/impact272/impact-presentation.json --output {CURRENT}",
    f"python3 -B tools/animation/prepare_player_sequences.py --source-root {ORIGINAL} --catalog tools/animation/c2_sequences.json --destination {EXTRA9}",
    f"python3 -B tools/animation/prepare_c2_profile.py --source-root {ORIGINAL} --base-bundle {CURRENT}/contact-animation --extra-bundle {EXTRA9} --destination {OLD9}",
    f"python3 -B tools/animation/prepare_c2_impact.py --base-catalog {RESOURCES}/impact272/impact-presentation.json --callback-data {OLD9}/c2-callback-data.json --plyrseq {ORIGINAL}/plyrseq.c --programs Assets/Scripts/Gameplay/SourceAnimationPrograms.C2.cs --destination {RESOURCES}/impact-old9",
    f"python3 -B tools/animation/prepare_c2_profile.py --source-root {ORIGINAL} --base-bundle {CURRENT}/contact-animation --extra-bundle {EXTRA9} --profile contact281-c2-m1 --destination {M1}",
    f"python3 -B tools/animation/prepare_c2_impact.py --base-catalog {RESOURCES}/impact272/impact-presentation.json --callback-data {M1}/c2-callback-data.json --plyrseq {ORIGINAL}/plyrseq.c --programs Assets/Scripts/Gameplay/SourceAnimationPrograms.C2.cs --profile contact281-c2-m1 --destination {RESOURCES}/impact-m1",
)
REPLAY_COMMAND = (f"python3 -B tests/movement-callers/prepare_public_replay.py prepare "
                  f"--fixture {PUBLIC_FIXTURE} --output {PUBLIC_REPLAY}")
B1_COMMAND = (f"python3 tests/ball/run.py --source-root {ORIGINAL} --input-profile contact272 "
              f"--fixture-manifest {CURRENT}/fixture.json --sound-functions {CURRENT}/audio-functions/sound-functions.safn")
M1_COMMAND = (f"python3 tests/movement-callers/run.py --source-root {ORIGINAL} --input-profile contact272 "
              f"--fixture-manifest {CURRENT}/fixture.json --sound-functions {CURRENT}/audio-functions/sound-functions.safn "
              f"--c2-m1-inputs {M1} --replay-commands {PUBLIC_REPLAY} --output .squad/.scratch/m1-ci-full-qualification "
              "--source-compatibility portable")
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
    "Prepare strict current Contact272 and explicit C2 M1 inputs",
    "Prepare exact public replay input without simulation",
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
CONTACT_STEPS = {
    "Test Contact272 SDK preflight and callers without original assets": (
        "python3 -B -m unittest discover -s tests/presentation/contact-runtime -p test_callers.py -q",
        "python3 -B tests/presentation/contact-runtime/run.py",
        "python3 -B tests/presentation/contact-runtime/mutations.py",
    ),
    "Test opt-in Contact281C2 SDK preflight and callers without original assets": (
        "python3 -B -m unittest discover -s tests/presentation/c2-runtime -p test_callers.py -q",
        "python3 -B tests/presentation/c2-runtime/run.py",
        "python3 -B tests/presentation/c2-runtime/mutations.py",
    ),
}
CONTACT_RUNNER_GUARDS = (
    "python3 tests/parity/test_contact_inputs.py",
    "python3 tests/contact/test_runner.py",
    "python3 tests/movement-callers/test_canonical.py",
)
TRIGGER_PATHS = (
    "Assets/**", "Assets.meta", "tests/**", "tools/assets/**", "tools/animation/**",
    "tools/rosters/**", "tools/playbooks/**", "tools/movement/**", "tools/interface/**",
    "tools/audio/**", "tools/environments/**", "scripts/**", "hosting/**", "Dockerfile",
    "compose.yaml", ".dockerignore", "README.md", "docs/build.md", "docs/source-parity.md",
    "docs/architecture.md", ".github/workflows/game-build.yml",
)


def require(value, message):
    if not value:
        raise AssertionError(message)


def step_commands(text):
    result = {}
    for block in re.split(r"^      - name: ", text, flags=re.M)[1:]:
        name, _, body = block.partition("\n")
        run = re.search(r"^        run: ([^\n]+)$", body, re.M)
        require(run is not None, "named CI stage must execute its checks: " + name)
        lines = (re.findall(r"^          (.*)$", body[run.end():], re.M)
                 if run[1] == "|" else [run[1]])
        result[name] = [shlex.split(line, comments=True) for line in lines if line.strip()]
    return result


def validate_workflow(text):
    names = re.findall(r"^      - name: (.+)$", text, re.M)
    require(len(names) == len(set(names)), "CI step names must not hide duplicate or overwritten stages")
    require(set(BASELINE_STEPS).issubset(names), "every prior Core/fixture/camera/function/mutation/tool check is retained")
    jobs = re.findall(r"^  ([A-Za-z][A-Za-z0-9_-]*):\n    name:", text, re.M)
    require(jobs == ["core-and-tooling"], "sound closure retains the ordinary single job")
    require("timeout-minutes: 30" in text, "measured sound campaigns need bounded30-minute headroom")
    require(not re.search(r"^\s+if:", text, re.M), "required sound checks cannot be silently gated")
    require(not re.search(r"^\s+continue-on-error:", text, re.M), "required integration failures cannot be allowed green")
    require("/data/" not in text and "--unity-editor-data" not in text and "--unity-reference" not in text,
            "clean CI has no private authoring paths or Unity requirement")
    require("--no-mutations" not in text, "complete sound CI cannot disable mutation campaigns")
    require("--single-public-attempt" not in text, "required portable M1 must execute the complete control closure")
    require("upload-artifact" not in text and "gh release upload" not in text,
            "original source/data and private RNG/state proof must stay local; no proof upload")
    required = (
        FETCH_COMMAND,
        "python3 tests/parity/prepare_inputs.py --source-root " + ORIGINAL + " --output " + FIXTURE,
        "python3 tests/audio/vm/test_runner.py",
        f"python3 -B tests/audio/vm/run.py --input-profile contact272 --fixture-manifest {CURRENT}/fixture.json --functions {CURRENT}/audio-functions --source-root {ORIGINAL} --mutations",
        f"python3 -B tests/audio/runtime/run.py --fixture-manifest {CURRENT}/fixture.json --sound-functions {CURRENT}/audio-functions/sound-functions.safn",
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
            "strict preparation precedes mandatory sound consumers")
    require("fetch_function_inputs.py --download" not in text,
            "four sound blobs are reused from the strict verified input union, not downloaded redundantly")
    steps = step_commands(text)
    def exact(name, commands):
        require(steps.get(name) == [shlex.split(command) for command in commands],
                "actual current-profile preparation/gate command differs: " + name)
    exact("Fetch verified real source-kernel inputs", (FETCH_COMMAND,))
    exact("Prepare original animation geometry roster and playbook services", (
        f"python3 tests/parity/prepare_inputs.py --source-root {ORIGINAL} --output {FIXTURE}",))
    exact("Prepare strict current Contact272 and explicit C2 M1 inputs", PREPARE_COMMANDS)
    exact("Prepare exact public replay input without simulation", (REPLAY_COMMAND,))
    exact("Compare original ball and ordered source callers", (B1_COMMAND,))
    exact("Compare original movement callers and post-animation physics", (M1_COMMAND,))
    exact("Compare synchronous original sound VM and source mutants", (
        f"python3 -B tests/audio/vm/run.py --input-profile contact272 --fixture-manifest {CURRENT}/fixture.json --functions {CURRENT}/audio-functions --source-root {ORIGINAL} --mutations",))
    exact("Test resolved sound Runtime and actual Core batches", (
        f"python3 -B tests/audio/runtime/run.py --fixture-manifest {CURRENT}/fixture.json --sound-functions {CURRENT}/audio-functions/sound-functions.safn",))
    exact("Compare source gameplay and host adapter", (
        f"python3 tests/parity/run.py --cases tests/source-oracles/.artifacts/cases.json --full-kernel --input-profile legacy --fixture-manifest {FIXTURE}/fixture.json",))
    early = steps["Test the inventory failure gates"]
    for command in ("python3 tests/test-runner.py",
                    "python3 -B -m unittest discover -s tests/audio/runtime -p test_ci.py -q",
                    "python3 -B tests/movement-callers/test_public_replay.py -q"):
        require(shlex.split(command) in early, "early actual fixture/workflow failure guard missing: " + command)
    order = list(steps)
    require(order.index("Prepare original animation geometry roster and playbook services") <
            order.index("Prepare strict current Contact272 and explicit C2 M1 inputs") <
            order.index("Compare original ball and ordered source callers") <
            order.index("Compare original movement callers and post-animation physics"),
            "fresh strict current inputs must precede their actual public callers")
    require(order.index("Prepare exact public replay input without simulation") <
            order.index("Compare original movement callers and post-animation physics"),
            "exact tracked public input preparation must precede REQUIRED full M1")
    require("natural-new-entry.json" not in text and "prepare_public_replay.py export" not in text,
            "clean CI must never require the ignored mixed source artifact or regenerate inputs")
    for name, commands in CONTACT_STEPS.items():
        require(steps.get(name) == [shlex.split(command) for command in commands],
                "actual asset-free Contact stage/commands differ: " + name)
    runner_commands = steps["Test real-input fixture and ball-runner failure gates"]
    for command in CONTACT_RUNNER_GUARDS:
        require(shlex.split(command) in runner_commands, "explicit Contact runner guard missing: " + command)
    animation = steps["Test original-animation input reader without original assets"]
    require(animation == [shlex.split(command) for command in (
        "python3 -B -m unittest discover -s tools/animation/tests",
        "python3 -B tools/animation/prepare_contact_impact.py --help > /dev/null",
        "python3 -B tools/animation/prepare_c2_impact.py --help > /dev/null",
    )], "full producer discovery and both impact CLI/import smokes must execute without original data")
    for event in ("pull_request", "push"):
        block = re.search(r"^  " + event + r":\n(.*?)(?=^  [a-z_]+:|\Z)", text, re.M | re.S)
        require(block is not None, "required workflow event missing: " + event)
        paths = re.findall(r"^      - '([^']+)'$", block[1], re.M)
        require(paths == list(TRIGGER_PATHS), "source/tool/test and current documentation triggers differ: " + event)


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
            ("--functions " + CURRENT + "/audio-functions", "--functions missing-data"),
        )
        for old, new in mutations:
            with self.subTest(old=old):
                self.assertIn(old, original)
                with self.assertRaises(AssertionError):
                    validate_workflow(original.replace(old, new, 1))


class ContactWorkflow(unittest.TestCase):
    def test_actual_steps_execute_assetfree_contracts_and_trigger_on_current_docs(self):
        validate_workflow(WORKFLOW.read_text())

    def test_actual_contact_commands_and_triggers_cannot_be_removed_or_disguised(self):
        original = WORKFLOW.read_text()
        mutations = (
            ("python3 tests/contact/test_runner.py", "true # python3 tests/contact/test_runner.py"),
            ("python3 tests/parity/test_contact_inputs.py", "true"),
            ("python3 tests/movement-callers/test_canonical.py", "true"),
            ("python3 -B tests/presentation/c2-runtime/run.py",
             "echo python3 -B tests/presentation/c2-runtime/run.py"),
            ("python3 -B tests/presentation/contact-runtime/mutations.py", "true"),
            ("python3 -B tests/presentation/c2-runtime/mutations.py", "true"),
            ("-s tests/presentation/contact-runtime -p 'test_callers.py'", "-s tests/presentation/contact-runtime -p 'absent.py'"),
            ("python3 -B tests/presentation/c2-runtime/run.py",
             "python3 -B tests/presentation/c2-runtime/run.py --inputs missing-original-data"),
            ("-s tools/animation/tests", "-s tools/animation/tests -p test_read_wan_pose.py"),
            ("python3 -B tools/animation/prepare_contact_impact.py --help > /dev/null", "true"),
            ("python3 -B tools/animation/prepare_c2_impact.py --help > /dev/null", "true"),
            ("      - 'tools/animation/**'\n", ""),
            ("      - 'tests/**'\n", ""),
            ("      - 'Assets/**'\n", ""),
            ("      - 'README.md'\n", ""),
            ("      - 'docs/build.md'\n", ""),
            ("      - 'docs/source-parity.md'\n", ""),
            ("      - 'docs/architecture.md'\n", ""),
        )
        for old, new in mutations:
            with self.subTest(old=old):
                self.assertIn(old, original)
                with self.assertRaises(AssertionError):
                    validate_workflow(original.replace(old, new, 1))
        with self.assertRaises(AssertionError):
            validate_workflow(original.replace("      - 'docs/build.md'\n", "", 2))

    def test_actual78_profile_covers_tracked_extra9_without_changing_legacy69(self):
        previous = list(sys.path)
        try:
            sys.path.insert(0, str(ROOT / "tests/parity"))
            spec = importlib.util.spec_from_file_location("current_contact_ci_inputs", ROOT / "tests/parity/fetch_inputs.py")
            producer = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(producer)
            legacy = producer.input_pins()
            current = producer.input_pins("contact272")
        finally:
            sys.path[:] = previous
        self.assertEqual(len(legacy), 69)
        self.assertEqual(len(current), 78)
        self.assertEqual(legacy, {name: current[name] for name in legacy})
        import json
        catalog = json.loads((ROOT / "tools/animation/c2_sequences.json").read_text())
        self.assertEqual(len(catalog["clips"]), 9)
        self.assertEqual(sum(row["selected_frames"] for row in catalog["clips"]), 1338)
        for row in catalog["clips"]:
            self.assertEqual(current[row["source_path"]], row["source_git_blob_sha1"])

    def test_actual_profile_preparation_replay_and_required_gate_regressions_fail(self):
        original = WORKFLOW.read_text()
        mutations = (
            ("--profile contact272 --download", "--download"),
            (PREPARE_COMMANDS[0], "true"),
            (PREPARE_COMMANDS[4], "true"),
            ("--catalog tools/animation/c2_sequences.json", "--catalog tools/animation/player_sequences.json"),
            ("--profile contact281-c2-m1", "--profile contact281-c2"),
            (REPLAY_COMMAND, "true"),
            ("prepare_public_replay.py prepare", "prepare_public_replay.py export"),
            (PUBLIC_FIXTURE, ".squad/.scratch/private-natural-new-entry.json"),
            ("python3 -B tests/movement-callers/test_public_replay.py -q", "true"),
            (B1_COMMAND, B1_COMMAND.replace(CURRENT + "/fixture.json", FIXTURE + "/fixture.json")),
            (M1_COMMAND, M1_COMMAND.replace("--c2-m1-inputs", "--c2-inputs")),
            (M1_COMMAND, M1_COMMAND.replace("--replay-commands " + PUBLIC_REPLAY, "")),
            (M1_COMMAND, M1_COMMAND + " || true"),
            (M1_COMMAND, M1_COMMAND + " --no-mutations"),
            (M1_COMMAND, M1_COMMAND + " --bounded156-diagnostic"),
            (M1_COMMAND, "echo " + M1_COMMAND),
            ("    timeout-minutes: 30", "    timeout-minutes: 30\n    continue-on-error: true"),
        )
        for old, new in mutations:
            with self.subTest(mutation=old):
                self.assertIn(old, original)
                with self.assertRaises(AssertionError):
                    validate_workflow(original.replace(old, new, 1))
        prepare = "      - name: Prepare exact public replay input without simulation\n        run: " + REPLAY_COMMAND + "\n"
        self.assertIn(prepare, original)
        with self.assertRaises(AssertionError):
            validate_workflow(original.replace(prepare, "") + prepare)

    def test_required_M1_is_exact_explicit_portable_full_gate_with_no_private_upload(self):
        original = WORKFLOW.read_text()
        command, = step_commands(original)["Compare original movement callers and post-animation physics"]
        self.assertEqual(command, shlex.split(M1_COMMAND))
        self.assertEqual(command.count("--source-compatibility"), 1)
        self.assertEqual(command[command.index("--source-compatibility") + 1], "portable")
        self.assertNotIn("--single-public-attempt", command)
        self.assertIn("--c2-m1-inputs", command)
        self.assertIn("--replay-commands", command)
        for changed in (
            M1_COMMAND.replace(" --source-compatibility portable", ""),
            M1_COMMAND.replace("--source-compatibility portable", "--source-compatibility strict"),
            M1_COMMAND + " --source-compatibility strict",
            M1_COMMAND + " --source-compatibility=portable",
            M1_COMMAND + " --single-public-attempt",
            M1_COMMAND + " || true",
        ):
            with self.subTest(command=changed), self.assertRaises(AssertionError):
                validate_workflow(original.replace(M1_COMMAND, changed, 1))
        for upload in ("      - uses: actions/upload-artifact@unapproved\n",
                       "      - name: Upload private proof\n        run: gh release upload tag private-rng.json\n"):
            with self.subTest(upload=upload), self.assertRaises(AssertionError):
                validate_workflow(original + upload)


if __name__ == "__main__":
    unittest.main()
