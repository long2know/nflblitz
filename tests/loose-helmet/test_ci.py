"""Require actual asset-free helmet execution/causal controls without changing old CI budgets or commands."""
import importlib.util
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
COMMANDS = (
    "python3 -B tests/loose-helmet/test_ci.py",
    "python3 -B tests/loose-helmet/run.py --output .squad/.scratch/loose-helmet-ci --controls",
    "python3 -B tests/loose-helmet/js_controls.py --output .squad/.scratch/loose-helmet-js-ci",
)


def validate(text):
    for command in COMMANDS:
        if not re.search(r"^          " + re.escape(command) + r"$", text, re.M):
            raise AssertionError("Required actual asset-free helmet command missing: " + command)
    if "    timeout-minutes: 40\n" not in text:
        raise AssertionError("Retain exact existing40-minute Game budget; no invented new budget.")
    if re.search(r"^\s+(?:if|continue-on-error):", text, re.M):
        raise AssertionError("Required new and retained campaigns cannot be skipped or allowed green.")


class HelmetCI(unittest.TestCase):
    def test_real_workflow_requires_compiled_and_executed_helmet_controls(self):
        validate((ROOT / ".github/workflows/game-build.yml").read_text())

    def test_removing_each_command_lowering_budget_or_allowing_failure_turns_guard_red(self):
        original = (ROOT / ".github/workflows/game-build.yml").read_text()
        for command in COMMANDS:
            with self.assertRaises(AssertionError):
                validate(original.replace(command, "true", 1))
        for old, new in (("timeout-minutes: 40", "timeout-minutes: 30"),
                         ("    steps:\n", "    continue-on-error: true\n    steps:\n"),
                         (COMMANDS[1], COMMANDS[1].replace("--controls", ""))):
            with self.assertRaises(AssertionError):
                validate(original.replace(old, new, 1))

    def test_helmet_preparer_is_offline_pinned_and_rejects_other_geometry(self):
        spec = importlib.util.spec_from_file_location("helmet_prepare_ci", ROOT / "tools/fire-spfx/prepare.py")
        prepare = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(prepare)
        self.assertEqual("d086f6944207a0ff2fa906ef7fc9692dcfb289ec", prepare.HELMET_PIN["gitBlobSha1"])
        self.assertEqual(19258, prepare.HELMET_PIN["bytes"])
        self.assertEqual("blitz-source-fire-spfx-portable/v3", prepare.HELMET_SCHEMA)
        for wrong in (b"", b"version=8.2; polyset phead_obj();", b"version=8.2; polyset phelm_obj(active=OFF);"):
            with self.assertRaisesRegex(ValueError, "original phelm_obj|unsupported original PHELM"):
                prepare.helmet_geometry_from_sdl(wrong)

    def test_source_player_selects_the_new_real_JS_suite_without_private_inputs(self):
        workflow = (ROOT / ".github/workflows/source-player.yml").read_text()
        self.assertIn(" tools/source-player/helmet.renderer.test.mjs\n", workflow)
        self.assertNotIn("original.py", workflow)
        self.assertNotIn("loose-helmet-proof", workflow)


if __name__ == "__main__":
    unittest.main()
