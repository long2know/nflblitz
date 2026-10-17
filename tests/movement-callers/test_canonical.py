import hashlib
import unittest
from pathlib import Path

import canonical


class CanonicalObservationGuards(unittest.TestCase):
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
