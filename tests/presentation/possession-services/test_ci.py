#!/usr/bin/env python3
"""Required workflow selection and real authenticated input/removal gates; no skipped original-C substitute."""
import argparse
from contextlib import redirect_stderr
import hashlib
import io
import importlib.util
import json
from pathlib import Path
import re
import shutil
import sys
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
sys.path.insert(0, str(HERE))
import fetch_inputs
import oracle
import run

BASE_WORKFLOW_SHA256 = "8c63133cdc6ace8613ce8872be4d89a61beeb9dfd6bfb325a7926101562a6d36"
FETCH_COMMAND = "python3 -B tests/presentation/possession-services/fetch_inputs.py --output .squad/.scratch/SourceData/possession-service-original"
PREPARE_COMMAND = "python3 -B tests/presentation/possession-services/prepare.py --original .squad/.scratch/SourceData/possession-service-original --output .squad/.scratch/SourceData/possession-services"
GUARD_COMMAND = "python3 -B tests/presentation/possession-services/test_ci.py --original .squad/.scratch/SourceData/possession-service-original --prepared .squad/.scratch/SourceData/possession-services --output .squad/.scratch/possession-service-ci-guards"
RUN_COMMAND = "python3 -B tests/presentation/possession-services/run.py --fixture .squad/.scratch/SourceData/source-kernel-contact272/fixture.json --sound .squad/.scratch/SourceData/source-kernel-contact272/audio-functions/sound-functions.safn --m1 .squad/.scratch/SourceData/c2-m1 --impact .squad/.scratch/SourceData/contact-profile/impact-m1/impact-presentation.json --normal .squad/.scratch/raw-camera-ci/original-C/normal.json --speech .squad/.scratch/SourceData/possession-services/speech/possession-speech.bytes --hud .squad/.scratch/SourceData/possession-services/hud/interface.json --fire .squad/.scratch/SourceData/possession-services/fire --fire-sha256 90a134af37fe6d43f13977f7903315ee0d9a7f37f700669b365f45dd7c941d56 --authority .squad/.scratch/SourceData/possession-services/authority --output .squad/.scratch/possession-services-ci --controls"
FETCH_STEP = """      - name: Fetch authenticated active possession service inputs
        env:
          GH_TOKEN: ${{ github.token }}
        run: """ + FETCH_COMMAND + "\n"
COMPARE_STEP = """      - name: Compare active original possession messages and process services
        run: |
          """ + PREPARE_COMMAND + "\n          " + GUARD_COMMAND + "\n          " + RUN_COMMAND + "\n"
OLD_PUBLIC_CASE = "portable-cabinet-wide-pass-clearance-12000"
CURRENT_PUBLIC_CASE = "portable-cabinet-wide-pass-clearance-possession-12000"
OLD_PUBLIC_REPLAY = ".squad/.scratch/SourceData/" + OLD_PUBLIC_CASE + ".jsonl"
CURRENT_PUBLIC_REPLAY = ".squad/.scratch/SourceData/" + CURRENT_PUBLIC_CASE + ".jsonl"
OLD_REPLAY_COMMAND = ("python3 -B tests/movement-callers/prepare_public_replay.py prepare "
                      f"--case {OLD_PUBLIC_CASE} --fixture tests/movement-callers/fixtures/{OLD_PUBLIC_CASE}.jsonl.gz "
                      f"--recipe tests/movement-callers/fixtures/{OLD_PUBLIC_CASE}.recipe.json --output {OLD_PUBLIC_REPLAY}")
CURRENT_REPLAY_COMMAND = OLD_REPLAY_COMMAND.replace(OLD_PUBLIC_CASE, CURRENT_PUBLIC_CASE)
CURRENT_REPLAY_STEP = ("      - name: Prepare source-current public replay input without simulation\n"
                       "        run: " + CURRENT_REPLAY_COMMAND + "\n")
GENERATION_COMMAND = ("python3 -B tests/movement-callers/check_replay_generations.py "
                      "--baseline-ref cf3f2b5db3cc94ac3f51e01dae94dd74d90ebc02 "
                      "--fixture-manifest .squad/.scratch/SourceData/source-kernel-contact272/fixture.json "
                      "--sound-functions .squad/.scratch/SourceData/source-kernel-contact272/audio-functions/sound-functions.safn "
                      "--c2-m1-inputs .squad/.scratch/SourceData/c2-m1 "
                      f"--replay-commands {OLD_PUBLIC_REPLAY} --output .squad/.scratch/public-replay-generation-ci")
GENERATION_STEP = ("      - name: Check historical and stale public replay generations\n"
                   "        run: " + GENERATION_COMMAND + "\n")
GENERATION_UNIT_LINE = "          python3 -B tests/movement-callers/test_replay_generations.py -q\n"
MUTATION_WORKER_ADDITIONS = (
    "      BLITZ_CI_MUTATION_WORKERS: '2'\n",
    "          python3 -B tests/ci/test_mutation_workers.py -q\n",
)
MUTATION_WORKER_MUTATIONS = (
    *((addition, "") for addition in MUTATION_WORKER_ADDITIONS),
    (MUTATION_WORKER_ADDITIONS[0], MUTATION_WORKER_ADDITIONS[0].replace("'2'", "'1'")),
    (MUTATION_WORKER_ADDITIONS[0], MUTATION_WORKER_ADDITIONS[0].replace("'2'", "'3'")),
    (MUTATION_WORKER_ADDITIONS[1], MUTATION_WORKER_ADDITIONS[1].replace("python3 -B", "echo python3 -B")),
    (MUTATION_WORKER_ADDITIONS[1], MUTATION_WORKER_ADDITIONS[1].replace(" -q\n", " -q || true\n")),
)
OLD_M1_COMMAND = ("python3 tests/movement-callers/run.py --source-root .squad/.scratch/SourceData/source-kernel-original "
                  "--input-profile contact272 --fixture-manifest .squad/.scratch/SourceData/source-kernel-contact272/fixture.json "
                  "--sound-functions .squad/.scratch/SourceData/source-kernel-contact272/audio-functions/sound-functions.safn "
                  f"--c2-m1-inputs .squad/.scratch/SourceData/c2-m1 --replay-commands {OLD_PUBLIC_REPLAY} "
                  "--output .squad/.scratch/m1-ci-full-qualification --source-compatibility portable")
CURRENT_M1_COMMAND = OLD_M1_COMMAND.replace(OLD_PUBLIC_REPLAY, CURRENT_PUBLIC_REPLAY)
CURRENT_M1_STEP = ("      - name: Compare original movement callers and post-animation physics\n"
                   "        # REQUIRED full OPT-IN PORTABLE gate, not native parity; faults/UNMET remain nonzero.\n"
                   "        run: " + CURRENT_M1_COMMAND + "\n")
TIMER_PUBLIC_CASE = "portable-cabinet-wide-pass-clearance-selection-countdown-v4-12000"
TIMER_M1_COMMAND = CURRENT_M1_COMMAND.replace(CURRENT_PUBLIC_CASE, TIMER_PUBLIC_CASE)
TIMER_M1_STEP = CURRENT_M1_STEP.replace(CURRENT_M1_COMMAND, TIMER_M1_COMMAND)
REVISION_GENERATION_COMMAND = (GENERATION_COMMAND +
    " --previous-ref e539258b6b6160f0dcdc072a05c4887e6946423e "
    f"--possession-replay-commands {CURRENT_PUBLIC_REPLAY}")
REVISION_GENERATION_STEP = GENERATION_STEP.replace(GENERATION_COMMAND, REVISION_GENERATION_COMMAND)
GENERATION_MUTATIONS = (
    (CURRENT_REPLAY_STEP, ""), (REVISION_GENERATION_STEP, ""), (TIMER_M1_STEP, ""),
    (OLD_REPLAY_COMMAND, "true"), (CURRENT_REPLAY_COMMAND, "echo " + CURRENT_REPLAY_COMMAND),
    (GENERATION_COMMAND, "true"), (GENERATION_UNIT_LINE, ""),
    (TIMER_M1_COMMAND, TIMER_M1_COMMAND + " --single-public-attempt"),
    (TIMER_M1_COMMAND, TIMER_M1_COMMAND.replace(TIMER_PUBLIC_CASE, OLD_PUBLIC_CASE)),
    (GENERATION_COMMAND, GENERATION_COMMAND.replace("cf3f2b5db3cc94ac3f51e01dae94dd74d90ebc02", "HEAD")),
    (GENERATION_COMMAND, GENERATION_COMMAND.replace(OLD_PUBLIC_REPLAY, CURRENT_PUBLIC_REPLAY)),
    (GENERATION_COMMAND, GENERATION_COMMAND + " --no-mutations"),
)


def validate_workflow(text):
    spec = importlib.util.spec_from_file_location("selection_ci",
        ROOT / "tests/selection-countdown/test_ci.py")
    selection_ci = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(selection_ci)
    text = selection_ci.validate_workflow(text)
    for block in (FETCH_STEP, COMPARE_STEP):
        if text.count(block) != 1:
            raise AssertionError("Required actual possession CI stage missing/changed; no optional/echo/substitute selection.")
    for block in (CURRENT_REPLAY_STEP, GENERATION_STEP, GENERATION_UNIT_LINE, CURRENT_M1_COMMAND):
        if text.count(block) != 1:
            raise AssertionError("Required exact source-current full gate AND historical recording generation checks missing/changed.")
    inherited = text.replace(FETCH_STEP, "", 1).replace(COMPARE_STEP, "", 1)
    inherited = inherited.replace(CURRENT_REPLAY_STEP, "", 1).replace(GENERATION_STEP, "", 1)
    inherited = inherited.replace(GENERATION_UNIT_LINE, "", 1).replace(CURRENT_M1_COMMAND, OLD_M1_COMMAND, 1)
    for addition in MUTATION_WORKER_ADDITIONS:
        if text.count(addition) != 1:
            raise AssertionError("Required bounded isolated mutation workers and failure guard missing/changed.")
        inherited = inherited.replace(addition, "", 1)
    if hashlib.sha256(inherited.encode()).hexdigest() != BASE_WORKFLOW_SHA256:
        raise AssertionError("Retain EVERY inherited campaign/setup/filter and exact40-minute Game budget/minimum30.")
    if re.search(r"^\s*(if|continue-on-error):", text, re.M):
        raise AssertionError("Required possession and inherited campaigns cannot be skipped or suppress failures.")
    if "    timeout-minutes: 40\n" not in text:
        raise AssertionError("Do not change the existing exact40-minute job budget.")
    if text.index(FETCH_STEP) < text.index("--output .squad/.scratch/raw-camera-ci --controls"):
        raise AssertionError("Original normal-camera/kernel/M1 prerequisites must execute first.")
    if not (text.index(OLD_REPLAY_COMMAND) < text.index(CURRENT_REPLAY_STEP) <
            text.index(GENERATION_STEP) < text.index(CURRENT_M1_COMMAND)):
        raise AssertionError("Both immutable recordings and historical/stale generation checks precede the full current gate.")
    normal_guard = (ROOT / "tests/presentation/normal-camera/test_ci.py").read_text()
    if "possession_ci.validate_workflow(" not in normal_guard:
        raise AssertionError("The independently inherited normal-camera guard must select possession workflow/removal validation.")


class PossessionCI(unittest.TestCase):
    original = prepared = output = None

    def test_required_workflow_preserves_all_old_commands_and_exact_new_selection(self):
        validate_workflow((ROOT / ".github/workflows/game-build.yml").read_text())

    def test_removing_each_actual_stage_operand_controls_or_failure_policy_turns_guard_red(self):
        original = (ROOT / ".github/workflows/game-build.yml").read_text()
        mutations = [
            (FETCH_STEP, ""), (COMPARE_STEP, ""), (FETCH_COMMAND, "true"),
            (PREPARE_COMMAND, "echo " + PREPARE_COMMAND), (GUARD_COMMAND, "true"),
            (RUN_COMMAND, RUN_COMMAND.replace(" --controls", "")),
            (RUN_COMMAND, RUN_COMMAND.replace(" --authority .squad/.scratch/SourceData/possession-services/authority", "")),
            ("timeout-minutes: 40", "timeout-minutes: 41"),
            ("timeout-minutes: 40", "timeout-minutes: 29"),
            ("    steps:\n", "    continue-on-error: true\n    steps:\n"),
            ("      - name: Compare active original possession messages", "      - if: false\n        name: Compare active original possession messages"),
            ("python3 tests/source-oracles/run.py", "echo python3 tests/source-oracles/run.py"),
            ("python3 tests/movement-callers/run.py", "echo python3 tests/movement-callers/run.py"),
        ] + list(GENERATION_MUTATIONS) + list(MUTATION_WORKER_MUTATIONS)
        for old, new in mutations:
            with self.subTest(mutation=old):
                self.assertEqual(original.count(old), 1)
                with self.assertRaises(AssertionError):
                    validate_workflow(original.replace(old, new, 1))

    def test_authority_and_all_twenty_one_controls_are_required_arguments(self):
        values = RUN_COMMAND.split()[3:]
        parsed = run.argument_parser().parse_args(values)
        self.assertTrue(parsed.controls)
        for option, count in (("--authority", 2), ("--controls", 1)):
            changed = list(values)
            start = changed.index(option)
            del changed[start:start + count]
            with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as failure:
                run.argument_parser().parse_args(changed)
            self.assertEqual(failure.exception.code, 2)

    def test_actual_case_or_control_removal_cannot_produce_a_green_inventory(self):
        cases, controls = run.required_inventory()
        self.assertEqual((len(cases), sum(c["ticks"] for c in cases), len(controls)), (40, 4760, 21))
        with patch.object(run, "fixtures", return_value=cases[:-1]), self.assertRaisesRegex(AssertionError, "Exact40"):
            run.required_inventory()
        with patch.object(run, "mutations", return_value=controls[:-1]), self.assertRaisesRegex(AssertionError, "All21"):
            run.required_inventory()
        for inherited in sorted(run.INHERITED_CONTROLS):
            with self.subTest(inherited=inherited), patch.object(run, "mutations",
                    return_value=[c for c in controls if c[0] != inherited]), self.assertRaisesRegex(AssertionError, "All21"):
                run.required_inventory()

    def test_every_actual_original_and_goose_input_is_authenticated_and_not_local_cache_only(self):
        recipe, records = fetch_inputs.verify_directory(self.original)
        receipt = json.loads((self.original / "receipt.json").read_bytes())
        self.assertEqual((len(records), receipt["downloadedGitBlobs"], receipt["localCacheUsed"]), (76, 76, False))
        self.assertEqual(len(recipe["authority"]), 13)
        self.assertEqual(set(oracle.PINS), {p["authority"] for p in recipe["authority"]})
        for pin in recipe["authority"]:
            self.assertEqual(pin["sha256"], oracle.PINS[pin["authority"]])
        self.assertEqual(recipe["repositories"]["midway"]["tree"], "fe8f9ea36e656d2529e73966af675a7b310f844f")

    def test_removing_each_actual_source_input_fails_before_preparation(self):
        copied = self.output / "missing-source"
        shutil.copytree(self.original, copied)
        recipe, catalog = fetch_inputs.catalog()
        for repo, name in sorted(catalog):
            path = copied / repo / name
            raw = path.read_bytes()
            path.unlink()
            with self.subTest(input=repo + "/" + name), self.assertRaisesRegex(ValueError, "inventory differs: missing="):
                fetch_inputs.verify_directory(copied)
            path.write_bytes(raw)

    def test_actual_c_and_goose_bytes_mutation_and_each_authority_removal_fail(self):
        copied = self.output / "authority"
        shutil.copytree(self.prepared / "authority", copied)
        oracle.verified_authority(copied)
        for name in oracle.PINS:
            path = copied / name
            raw = path.read_bytes()
            path.unlink()
            with self.subTest(missing=name), self.assertRaisesRegex(ValueError, "Exact13"):
                oracle.verified_authority(copied)
            path.write_bytes(bytes([raw[0] ^ 1]) + raw[1:])
            with self.subTest(changed=name), self.assertRaisesRegex(ValueError, "Wrong authenticated"):
                oracle.verified_authority(copied)
            path.write_bytes(raw)
        extra = copied / "unselected.txt"
        extra.write_text("No unselected source may hide an omitted declared operand.\n")
        with self.assertRaisesRegex(ValueError, "unexpected="):
            oracle.verified_authority(copied)

    def test_prepared_hud_fx_and_speech_pins_are_actual_reproduced_values(self):
        from prepare import HUD_SHA256, FIRE_SHA256, SPEECH_SHA256
        for name, pin in (("hud/interface.json", HUD_SHA256), ("fire/manifest.json", FIRE_SHA256),
                          ("speech/possession-speech.bytes", SPEECH_SHA256)):
            self.assertEqual(fetch_inputs.digest((self.prepared / name).read_bytes()), pin)
        receipt = json.loads((self.prepared / "receipt.json").read_bytes())
        self.assertEqual((receipt["status"], len(receipt["files"])), ("passed", 50))
        self.assertEqual((self.prepared / "speech/possession-speech.bytes").stat().st_size, 40)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original", type=Path, required=True)
    parser.add_argument("--prepared", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    PossessionCI.original = args.original.absolute()
    PossessionCI.prepared = args.prepared.absolute()
    PossessionCI.output = fetch_inputs.new_output(args.output)
    PossessionCI.output.mkdir(parents=True)
    results = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(PossessionCI))
    report = dict(schema="possession-service-ci-guards/v1", status="passed" if results.wasSuccessful() else "failed",
                  tests=results.testsRun, skipped=len(results.skipped), failures=len(results.failures), errors=len(results.errors),
                  actualMissingInputControls=76, actualAuthorityMissingAndChangedControls=26,
                  workflowRemovalSelectionControls=13 + len(GENERATION_MUTATIONS) + len(MUTATION_WORKER_MUTATIONS),
                  cases=40, managedCases=50, compiledControls=21,
                  inheritedCompiledControls=17, addedLifecycleControls=4)
    (PossessionCI.output / "result.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report))
    return 0 if results.wasSuccessful() and results.testsRun == 8 and not results.skipped else 1


if __name__ == "__main__":
    raise SystemExit(main())
