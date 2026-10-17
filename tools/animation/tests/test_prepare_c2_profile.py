"""C2 registry/producer trust gates without original payloads or numeric tables."""
import importlib.util
from pathlib import Path
import shutil
import unittest
import uuid

SPEC = importlib.util.spec_from_file_location("c2_production_preparer", Path(__file__).resolve().parents[1] / "prepare_c2_profile.py")
producer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(producer)


class C2ProducerChecks(unittest.TestCase):
    def test_explicit_union_data_and_old_identities(self):
        self.assertEqual(producer.ANIMATION_SHA, "e87401c8cf3e348cc2d9c7255a1f18f43e41b1694ff7dff45c4bf025a5ecce4f")
        self.assertEqual(producer.DATA_SHA, "9d2cb7ac5d92c3c2178fc1970a68ccad778fb7d319513b1787fe3ba3fe905f99")
        self.assertEqual(producer.HIT_SHA, "bb978454ab57b09357e422f048e4035bea01a4d5932f453dd89be39838c6e904")
        self.assertEqual(producer.BASE_SHA, "9cae962bbb861bffe4d379bc30919cf34c0238ed35be82e605253d3059cfede9")
        self.assertEqual(producer.EXTRA_SHA, "3e69a29f11c15c5d318f486824b1a3bba1f9489905e20d4c9ce76742af7a8c5f")
        self.assertEqual(len(set(producer.SCRIPTS)), 9)
        self.assertEqual(len(producer.SOURCE_PINS), 6)

    def test_wrong_originals_fail_before_any_output(self):
        root = producer.ROOT / ".squad/.scratch" / ("c2-producer-guard-" + uuid.uuid4().hex)
        try:
            source, base, extra, output = (root / n for n in ("source", "base", "extra", "output"))
            for path in (source, base, extra):
                path.mkdir(parents=True)
            for name in producer.SOURCE_PINS:
                path = source / name; path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"explicit synthetic wrong original, not a table or motion payload")
            with self.assertRaisesRegex(ValueError, "source blob differs"):
                producer.prepare(source, base, extra, output)
            self.assertFalse(output.exists())
        finally:
            if root.exists():
                shutil.rmtree(root)


if __name__ == "__main__":
    unittest.main()
