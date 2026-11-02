import gzip
from pathlib import Path
import shutil
import unittest
import uuid

import replay_evidence


class LosslessPrivateReplayTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(__file__).resolve().parents[2] / ".squad/.scratch" / ("replay-retention-" + uuid.uuid4().hex)
        self.root.mkdir(parents=True)

    def tearDown(self):
        shutil.rmtree(self.root)

    def test_archives_keep_all_three_exact_streams_across_chunk_boundary_and_detect_tampering(self):
        first, second = self.root / "first", self.root / "second"
        first.mkdir(); second.mkdir()
        for index, name in enumerate(replay_evidence.FILES):
            payload = (bytes([65 + index]) * (1024 * 1024 + 13)) + b"\n"
            (first / name).write_bytes(payload)
            (second / name).write_bytes(payload)
        records = replay_evidence.archive(first)
        replay_evidence.verify(first, records)
        for name in replay_evidence.FILES:
            self.assertFalse((first / name).exists())
            self.assertTrue(replay_evidence.same(first, second, name))
        name = replay_evidence.FILES[0]
        saved = records[name]["bytes"]
        records[name]["bytes"] = float(saved)
        with self.assertRaisesRegex(ValueError, "record"):
            replay_evidence.verify(first, records)
        records[name]["bytes"] = saved
        shadow = first / name
        shadow.write_bytes(b"fake equality shadow\n")
        with self.assertRaisesRegex(ValueError, "Ambiguous"):
            replay_evidence.same(first, second, name)
        shadow.unlink()
        target = first / (replay_evidence.FILES[0] + ".gz")
        target.write_bytes(gzip.compress(b"changed actual retained stream\n", mtime=0))
        self.assertFalse(replay_evidence.same(first, second, replay_evidence.FILES[0]))
        with self.assertRaisesRegex(ValueError, "container"):
            replay_evidence.verify(first, records)


if __name__ == "__main__":
    unittest.main()
