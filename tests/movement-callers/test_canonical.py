import hashlib
import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

import canonical


class CanonicalObservationGuards(unittest.TestCase):
    def test_full_m1_terminal_report_is_persisted_and_failures_are_not_silent(self):
        for status in ("passed-full-M1-public-caller-gate", "UNMET-public-R_SKIP",
                       "failed", "inconclusive-generation-changed",
                       "passed-scoped-entry-and-cache-only"):
            with self.subTest(status=status), tempfile.TemporaryDirectory() as work:
                output = Path(work)
                result = {"status": status, "runs": {"unlogged-proof": "not-for-console"}}
                stdout, stderr = io.StringIO(), io.StringIO()
                with redirect_stdout(stdout), redirect_stderr(stderr):
                    canonical._write_m1_result(output, result)
                self.assertEqual(json.loads((output / "result.json").read_text()), result)
                success = status == "passed-full-M1-public-caller-gate"
                visible, empty = (stdout, stderr) if success else (stderr, stdout)
                self.assertEqual(empty.getvalue(), "")
                self.assertIn(status, visible.getvalue())
                self.assertIn(str(output / "result.json"), visible.getvalue())
                self.assertNotIn("not-for-console", visible.getvalue())
                if not success:
                    self.assertIn("M1 REGRESSION FAILED:", visible.getvalue())
                if status == "UNMET-public-R_SKIP":
                    self.assertIn(canonical.M1_COVERAGE_ASSERTION, visible.getvalue())

    def test_snap_hike_root_is_explicit_and_controls_target_actual_producer_context(self):
        source = (canonical.ROOT / "Assets/Scripts/Gameplay/GameSimulation.Source.cs").read_text()
        request = source[source.index("        private void SnapSource("):
                         source.index("        private void PrepareSourceKickoff(")]
        self.assertNotIn("Model.Phase = GamePhase.LiveBall", request)
        self.assertIn("SourceGameFlags.HikingBall", request)
        self.assertIn("SourcePlayerDriver.Snap(Model, SourceDraw)", request)
        release = source[source.index("            if ((signal & SourceAnimationSignal.HikeBall) != 0)"):
                         source.index("        private void BindSourceAnimation(")]
        self.assertIn("Model.Phase = GamePhase.LiveBall", release)
        self.assertIn("Model.TargetPlayerId = target", release)
        self.assertIn("EmitSource(SourceEventType.Snapped", release)
        self.assertEqual(len(canonical.SNAP_HIKE_CONTROLS), 3)
        for identity, old, new, assertion in canonical.SNAP_HIKE_CONTROLS:
            with self.subTest(identity=identity):
                self.assertEqual(source.count(old), 1)
                self.assertNotEqual(old, new)
                self.assertGreater(len(assertion), 20)
        self.assertIn("tests/movement-callers/SnapHikeChecks.cs", canonical.HARNESS)

    def test_successor_full_gate_cannot_relabel_the_old_zero_window(self):
        receipt = {
            "failure": None, "completedHosts": 12000, "SourceTick": 11400,
            "profile": "original-teams-contact281-c2-m1",
            "factory": "REAL SourceC2Composition.CreateM1Simulation",
            "sourceData": "b0c02e5516f4f932625e1f8ccb3ff0fd53ae222657f899eb8c9d0b0c2e74460a",
            "originalPublicVectorRetainedWithoutStateConditionedRegeneration": True,
            "positive": None,
        }
        summary = {"status": "failed", "failed_assertion": canonical.M1_COVERAGE_ASSERTION}
        self.assertEqual("unmet-public-R_SKIP", canonical.require_m1_replay(1, summary, receipt))
        with self.assertRaises(RuntimeError):
            canonical.require_m1_replay(0, {"status": "passed", "public_R_SKIP_gate_qualified": True}, receipt)
        for field, value in (("factory", "direct constructor"), ("profile", "original-teams-contact281-c2"),
                             ("sourceData", "old-nine"), ("completedHosts", 11999),
                             ("originalPublicVectorRetainedWithoutStateConditionedRegeneration", False),
                             ("failure", {"source": "unrelated fault"})):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                canonical.require_m1_replay(1, summary, dict(receipt, **{field: value}))
        positive = dict(receipt, positive={"actor": 11, "sameInstance": 9, "firstOrdinal": 0, "secondOrdinal": 1})
        self.assertEqual("positive", canonical.require_m1_replay(0,
            {"status": "passed", "public_R_SKIP_gate_qualified": True}, positive))

    def test_successor_uses_actual_factory_and_retains_full_source_R_SKIP_gate(self):
        inputs = (canonical.ROOT / "tests/parity/FullKernelInputs.cs").read_text()
        self.assertIn("SourceC2Composition.CreateM1Simulation(Animations, ReferencePivots", inputs)
        self.assertIn("c2M1InputsPath", inputs)
        source = (canonical.HERE / "Canonical.cs").read_text()
        self.assertIn("PlayerCommand[] commands = ReadPublicReplay(replayPath);", source)
        self.assertIn("PlayerCommand command = commands[host];", source)
        self.assertIn(canonical.M1_COVERAGE_ASSERTION, source)
        self.assertIn("witness.Captured(actor.Id)", source)
        self.assertNotIn("SetValue(", source)
        runner = (canonical.HERE / "canonical.py").read_text()
        self.assertIn("for spec in CANONICAL_CACHE_MUTANTS:", runner)
        self.assertIn("for identity, filename, old, new, expected in controls.MUTANTS:", runner)
        self.assertIn("_write_m1_result(output, result)", runner[runner.index("def m1_main("):])
        self.assertIn('return 0 if result["status"] == "passed-full-M1-public-caller-gate" else 1', runner)

    def test_exact_frozen_unique_call_site_and_byte_normalization(self):
        original = (canonical.ROOT / canonical.CACHE_SOURCE).read_bytes()
        self.assertEqual(hashlib.sha256(original).hexdigest(), canonical.CACHE_SOURCE_SHA)
        observed = canonical.inject(original)
        self.assertEqual(observed.count(b"geometry.CameraJoints("), original.count(b"geometry.CameraJoints("))
        self.assertEqual(observed.count(b"CanonicalCacheCapture.Record("), 1)
        self.assertEqual(observed.replace(canonical.OBSERVED_CALL.encode(), canonical.CALL.encode()), original)

    def test_hash_change_cannot_be_observed_as_same_generation(self):
        original = (canonical.ROOT / canonical.CACHE_SOURCE).read_bytes()
        with self.assertRaisesRegex(ValueError, "exact frozen original production hash"):
            canonical.inject(original + b"\n")

    def test_canonical_factory_and_entry_are_actual(self):
        inputs = (canonical.ROOT / "tests/parity/FullKernelInputs.cs").read_text()
        self.assertIn("SourceC2Composition.CreateSimulation(Animations, ReferencePivots", inputs)
        self.assertIn("SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(C2Data)", inputs)
        source = (canonical.HERE / "Canonical.cs").read_text()
        self.assertNotIn(".CameraJoints(", source)
        self.assertNotIn("GetField(", source)
        self.assertIn("source-sound-smoke/raw-vector-v1", source)
        self.assertIn("host < 12000", source)
        self.assertIn("SourceActorPoseSampler.Evaluate(library, real.ReferencePivots", source)
        self.assertIn("ReferenceRows == witness.CurrentRows", source)

    def test_required_actual_production_compile_closure_is_complete(self):
        paths = {p.relative_to(canonical.ROOT).as_posix() for p in canonical.production_paths()}
        for name in (
            "Assets/Scripts/Runtime/SourceC2Composition.cs",
            "Assets/Scripts/Runtime/SourceContactComposition.cs",
            "Assets/Scripts/Presentation/SourceC2SoundFunctions.cs",
            "Assets/Scripts/Presentation/SourceAudioFunctions.cs",
            "Assets/Scripts/Gameplay/SourceCatchGeometry.cs",
            "Assets/Scripts/AssetImport/SourceC2CallbackData.cs",
        ):
            self.assertIn(name, paths)
        self.assertEqual(len(paths), 66)


if __name__ == "__main__":
    unittest.main()
