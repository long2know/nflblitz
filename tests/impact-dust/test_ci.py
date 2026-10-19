"""Keep the actual pure dust regression and causal controls required in asset-free CI."""
from pathlib import Path
import importlib.util
import json
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
COMMANDS = (
    "python3 -B tests/impact-dust/test_ci.py",
    "python3 -B tests/impact-dust/run.py --output .squad/.scratch/impact-dust-ci --controls",
    "python3 -B tests/impact-dust/js_controls.py --output .squad/.scratch/impact-dust-js-ci",
)


def validate(text):
    for command in COMMANDS:
        if not re.search(r"^          " + re.escape(command) + r"$", text, re.M):
            raise AssertionError("Required actual dust asset-free command missing: " + command)
    if "    timeout-minutes: 40\n" not in text:
        raise AssertionError("Preserve the measured existing exact40-minute Game budget.")
    if re.search(r"^\s+(?:continue-on-error|if):", text, re.M):
        raise AssertionError("Dust and retained Game campaigns cannot be skipped or allowed green.")


class DustCI(unittest.TestCase):
    def test_catalog_retains_eighteen_fire_pins_and_adds_exact_ten_dirt_names(self):
        catalog = json.loads((ROOT / "tools/fire-spfx/inputs.json").read_text())
        self.assertEqual(18, len(catalog["inputs"]))
        self.assertEqual(["models/spfx.sdl"] + [f"img3d/trail{i}.tga" for i in range(1, 18)],
                         [p["path"] for p in catalog["inputs"]])
        self.assertEqual([f"img3d/dirt{i}.tga" for i in range(1, 11)], [p["path"] for p in catalog["dustInputs"]])
        self.assertTrue(all(p["bytes"] == 4458 and len(p["gitBlobSha1"]) == 40 and len(p["sha256"]) == 64
                            for p in catalog["dustInputs"]))
        self.assertFalse(catalog["nativeConvertedTextureEquivalent"])
        self.assertFalse(catalog["generatedSpfxHeaderEquivalent"])

    def test_nonpixel_adapter_never_accepts_unverified_trailing_bytes(self):
        spec = importlib.util.spec_from_file_location("dust_prepare_ci", ROOT / "tools/fire-spfx/prepare.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        self.assertEqual(b"synthetic untouched legacy view", module.tga_pixel_view(b"synthetic untouched legacy view"))
        for bad in (b"", bytes(4114), bytes(4458), bytes(4459)):
            with self.assertRaisesRegex(ValueError, "Original DIRT nonpixel developer directory/tag20/footer"):
                module.tga_pixel_view(bad, True)

    def test_real_workflow_requires_pure_dust_and_compiled_controls(self):
        validate((ROOT / ".github/workflows/game-build.yml").read_text())

    def test_removing_each_real_command_or_lowering_budget_turns_guard_red(self):
        text = (ROOT / ".github/workflows/game-build.yml").read_text()
        for command in COMMANDS:
            with self.assertRaises(AssertionError):
                validate(text.replace(command, "true", 1))
        for old, new in (("timeout-minutes: 40", "timeout-minutes: 30"),
                         ("--controls", "--no-controls"),
                         ("    steps:\n", "    continue-on-error: true\n    steps:\n")):
            changed = text.replace(old, new, 1) if old != "--controls" else text.replace(COMMANDS[1], COMMANDS[1].replace(old, new), 1)
            with self.assertRaises(AssertionError):
                validate(changed)


if __name__ == "__main__":
    unittest.main()
