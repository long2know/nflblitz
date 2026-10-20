#!/usr/bin/env python3
"""Mandatory additive CI, immutable source authority and nonoptional regression/control guards."""
import argparse
from contextlib import redirect_stderr
import hashlib
import importlib.util
import io
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
BASE_WORKFLOW_SHA256 = "1016fb32a3a24b2f020c1cd75bb8e348dd92c789bf70378f85afffd992681e06"
GUARD_COMMAND = ("python3 -B tests/selection-countdown/test_ci.py "
    "--authority .squad/.scratch/SourceData/possession-service-original --output .squad/.scratch/selection-countdown-ci-guards")
RUN_COMMAND = ("python3 -B tests/selection-countdown/run.py "
    "--fixture .squad/.scratch/SourceData/source-kernel-contact272/fixture.json "
    "--sound .squad/.scratch/SourceData/source-kernel-contact272/audio-functions/sound-functions.safn "
    "--m1 .squad/.scratch/SourceData/c2-m1 "
    "--impact .squad/.scratch/SourceData/contact-profile/impact-m1/impact-presentation.json "
    "--normal .squad/.scratch/raw-camera-ci/original-C/normal.json "
    "--speech .squad/.scratch/SourceData/possession-services/speech/possession-speech.bytes "
    "--hud .squad/.scratch/SourceData/possession-services/hud/interface.json "
    "--fire .squad/.scratch/SourceData/possession-services/fire "
    "--authority .squad/.scratch/SourceData/possession-service-original "
    "--output .squad/.scratch/selection-countdown-ci --controls")
TIMER_STEP = ("      - name: Compare original selection countdown and actual cabinet timeout\n"
    "        run: |\n          " + GUARD_COMMAND + "\n          " + RUN_COMMAND + "\n")
POSSESSION_CASE = "portable-cabinet-wide-pass-clearance-possession-12000"
TIMER_CASE = "portable-cabinet-wide-pass-clearance-selection-countdown-12000"
TIMER_PREPARE_STEP = ("      - name: Prepare source-timer public replay input without simulation\n"
    "        run: python3 -B tests/movement-callers/prepare_public_replay.py prepare "
    f"--case {TIMER_CASE} --fixture tests/movement-callers/fixtures/{TIMER_CASE}.jsonl.gz "
    f"--recipe tests/movement-callers/fixtures/{TIMER_CASE}.recipe.json "
    f"--output .squad/.scratch/SourceData/{TIMER_CASE}.jsonl\n")
BASE_GENERATION_COMMAND = ("python3 -B tests/movement-callers/check_replay_generations.py "
    "--baseline-ref cf3f2b5db3cc94ac3f51e01dae94dd74d90ebc02 "
    "--fixture-manifest .squad/.scratch/SourceData/source-kernel-contact272/fixture.json "
    "--sound-functions .squad/.scratch/SourceData/source-kernel-contact272/audio-functions/sound-functions.safn "
    "--c2-m1-inputs .squad/.scratch/SourceData/c2-m1 "
    "--replay-commands .squad/.scratch/SourceData/portable-cabinet-wide-pass-clearance-12000.jsonl "
    "--output .squad/.scratch/public-replay-generation-ci")
GENERATION_COMMAND = (BASE_GENERATION_COMMAND +
    " --previous-ref e539258b6b6160f0dcdc072a05c4887e6946423e "
    f"--possession-replay-commands .squad/.scratch/SourceData/{POSSESSION_CASE}.jsonl")
EXPORT_UNIT_LINE = "          python3 -B tests/movement-callers/test_export_portable_public.py -q\n"
GENERATION_DELTA = (
    ("", TIMER_PREPARE_STEP),
    ("", EXPORT_UNIT_LINE),
    (BASE_GENERATION_COMMAND, GENERATION_COMMAND),
    (f"--replay-commands .squad/.scratch/SourceData/{POSSESSION_CASE}.jsonl",
     f"--replay-commands .squad/.scratch/SourceData/{TIMER_CASE}.jsonl"),
)


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


run = load("selection_countdown_runner_guards", HERE / "run.py")


def validate_workflow(text):
    if text.count(TIMER_STEP) != 1:
        raise AssertionError("Required actual source countdown/cabinet C, production and compiled controls stage changed.")
    inherited = text
    for old, new in GENERATION_DELTA:
        if inherited.count(new) != 1:
            raise AssertionError("Required exact source-timer recording, both generation boundaries and exporter guards changed.")
        inherited = inherited.replace(new, old, 1)
    inherited = inherited.replace(TIMER_STEP, "", 1)
    if hashlib.sha256(inherited.encode()).hexdigest() != BASE_WORKFLOW_SHA256:
        raise AssertionError("Preserve EVERY main39 command, inventory, pin, campaign and exact40-minute budget unchanged.")
    if text.index(TIMER_STEP) < text.index("--output .squad/.scratch/possession-services-ci --controls"):
        raise AssertionError("Authenticated original source, current kernel, camera, HUD/FX/speech must precede the timer suite.")
    if not (text.index(TIMER_PREPARE_STEP) < text.index(GENERATION_COMMAND) <
            text.index(f"--replay-commands .squad/.scratch/SourceData/{TIMER_CASE}.jsonl")):
        raise AssertionError("Explicit current timer preparation and both historical/current boundaries precede full current M1.")
    return inherited


class SelectionCI(unittest.TestCase):
    authority = output = None

    def test_exact_additive_workflow_and_every_inherited_campaign(self):
        validate_workflow((ROOT / ".github/workflows/game-build.yml").read_text())
        possession = (ROOT / "tests/presentation/possession-services/test_ci.py").read_text()
        self.assertIn("selection_ci.validate_workflow(text)", possession)

    def test_deletion_substitution_optional_execution_budget_or_replay_changes_are_red(self):
        text = (ROOT / ".github/workflows/game-build.yml").read_text()
        changes = [(TIMER_STEP, ""), (RUN_COMMAND, "echo " + RUN_COMMAND), (GUARD_COMMAND, "true"),
            (RUN_COMMAND, RUN_COMMAND.replace(" --controls", "")),
            ("timeout-minutes: 40", "timeout-minutes: 41"),
            ("BLITZ_CI_MUTATION_WORKERS: '2'", "BLITZ_CI_MUTATION_WORKERS: '1'"),
            ("      - name: Compare original selection countdown", "      - if: false\n        name: Compare original selection countdown"),
            ("    steps:\n", "    continue-on-error: true\n    steps:\n"),
            ("python3 tests/movement-callers/run.py", "echo python3 tests/movement-callers/run.py"),
            ("--baseline-ref cf3f2b5db3cc94ac3f51e01dae94dd74d90ebc02", "--baseline-ref HEAD")]
        changes += [
            (TIMER_PREPARE_STEP, ""), (GENERATION_COMMAND, BASE_GENERATION_COMMAND),
            (GENERATION_COMMAND, "true"), (EXPORT_UNIT_LINE, ""),
            (EXPORT_UNIT_LINE, EXPORT_UNIT_LINE.replace("python3 -B", "echo python3 -B")),
            (GENERATION_COMMAND, GENERATION_COMMAND + " || true"),
            (GENERATION_COMMAND, GENERATION_COMMAND + " --single-public-attempt"),
            ("--previous-ref e539258b6b6160f0dcdc072a05c4887e6946423e", "--previous-ref HEAD"),
            (f"--possession-replay-commands .squad/.scratch/SourceData/{POSSESSION_CASE}.jsonl",
             f"--possession-replay-commands .squad/.scratch/SourceData/{TIMER_CASE}.jsonl"),
            (f"--replay-commands .squad/.scratch/SourceData/{TIMER_CASE}.jsonl",
             f"--replay-commands .squad/.scratch/SourceData/{POSSESSION_CASE}.jsonl"),
        ]
        for option in ("--authority", "--fire", "--normal", "--hud", "--speech", "--fixture", "--m1"):
            tokens = RUN_COMMAND.split()
            offset = tokens.index(option)
            changed = tokens[:offset] + tokens[offset + 2:]
            changes.append((RUN_COMMAND, " ".join(changed)))
        for old, new in changes:
            with self.subTest(changed=old):
                self.assertEqual(text.count(old), 1)
                with self.assertRaises(AssertionError):
                    validate_workflow(text.replace(old, new, 1))

    def test_actual_inputs_and_all_controls_are_required_arguments(self):
        tokens = RUN_COMMAND.split()[3:]
        self.assertTrue(run.argument_parser().parse_args(tokens).controls)
        for option in ("--controls", "--authority", "--fire", "--normal", "--hud", "--speech", "--fixture", "--m1"):
            changed = list(tokens)
            start = changed.index(option)
            del changed[start:start + (1 if option == "--controls" else 2)]
            with self.subTest(option=option), redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
                run.argument_parser().parse_args(changed)

    def test_no_case_or_compiled_control_can_disappear(self):
        fixtures, controls = run.required_inventory()
        self.assertEqual((len(fixtures), sum(c["ticks"] for c in fixtures), len(controls)), (17, 10417, 21))
        for case in fixtures:
            with self.subTest(case=case["name"]), patch.object(run, "cases",
                    return_value=[c for c in fixtures if c != case]), self.assertRaisesRegex(AssertionError, "All17"):
                run.required_inventory()
        for control in controls:
            with self.subTest(control=control[0]), patch.object(run, "mutations",
                    return_value=[c for c in controls if c != control]), self.assertRaisesRegex(AssertionError, "All21"):
                run.required_inventory()

    def test_every_timer_dependency_is_in_the_inherited_pinned_acquisition_union(self):
        fetch = load("selection_inherited_acquisition", ROOT / "tests/presentation/possession-services/fetch_inputs.py")
        recipe, inputs = fetch.catalog()
        self.assertEqual(len(inputs), 76)
        self.assertEqual(recipe["repositories"]["nfl"]["revision"], "89f840d58d152a2a9f230cb8d0cdffbd554c8ded")
        self.assertEqual(recipe["repositories"]["midway"]["revision"], "1792293aaaea00a919b59e8c05716978e85b4333")
        for name, pin in run.oracle.PINS.items():
            repo, path = name.split("/", 1)
            self.assertEqual(inputs[repo, path]["gitBlobSha1"], pin)
        self.assertEqual(len(run.oracle.verified_authority(self.authority)), 13)

    def test_each_missing_changed_or_symlinked_original_is_a_meaningful_failure(self):
        copied = self.output / "authority"
        copied.mkdir()
        for name in run.oracle.PINS:
            path = copied / name
            path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(self.authority / name, path)
        for name in run.oracle.PINS:
            path = copied / name
            raw = path.read_bytes()
            path.unlink()
            with self.subTest(missing=name), self.assertRaisesRegex(ValueError, "Missing/escaping timer authority"):
                run.oracle.verified_authority(copied)
            path.write_bytes(bytes([raw[0] ^ 1]) + raw[1:])
            with self.subTest(changed=name), self.assertRaisesRegex(ValueError, "Wrong authenticated timer Git blob"):
                run.oracle.verified_authority(copied)
            path.unlink()
            path.symlink_to(self.authority / name)
            with self.subTest(symlink=name), self.assertRaisesRegex(ValueError, "Missing/escaping timer authority"):
                run.oracle.verified_authority(copied)
            path.unlink()
            path.write_bytes(raw)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--authority", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args, rest = parser.parse_known_args()
    output = args.output.absolute()
    if output.exists() or ROOT / ".squad/.scratch" not in output.parents or output.resolve() != output:
        parser.error("Use a NEW ignored worktree-local guard output.")
    output.mkdir(parents=True)
    SelectionCI.authority, SelectionCI.output = args.authority.absolute(), output
    return not unittest.TextTestRunner(verbosity=1).run(unittest.defaultTestLoader.loadTestsFromTestCase(SelectionCI)).wasSuccessful()


if __name__ == "__main__":
    raise SystemExit(main())
