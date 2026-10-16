import importlib.util
import io
import os
from pathlib import Path
import struct
import unittest
import wave

ROOT = Path(__file__).resolve().parents[2]
MODULE = Path(os.environ.get("SOURCE_AUDIO_MODULE", str(ROOT / "tools/audio/prepare.py")))
SPEC = importlib.util.spec_from_file_location("audio_prepare_under_test", MODULE)
audio = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(audio)


def bank_fixture(frames=1, end_wait=2, repeat=1, mix=90):
    slots = 4
    base = 134 + slots * 4
    program = bytes([1, 1, 0, 0, 7, 0, mix, 0, 0, 1, 0, 0, 0, 7, repeat,
                     end_wait >> 8, end_wait & 255, 0])
    payload = struct.pack(">H", frames) + bytes(16) + b"\x12\x34\x56\x78"
    data = bytearray(128)
    data[:9] = b"DCS 3.0G "
    data += struct.pack("<HI", slots, len(program) + len(payload))
    data += struct.pack("<4I", 0xFFFFFFFF, 0, 0xFFFFFFFF, 0xFFFFFFFF)
    data += program + payload
    return bytes(data), base


def change(data, offset, value):
    result = bytearray(data)
    result[offset:offset + len(value)] = value
    return bytes(result)


class PreparationTests(unittest.TestCase):
    def test_sparse_ids_and_actual_relative_address(self):
        data, base = bank_fixture()
        parsed = audio.parse_bank(data)
        self.assertEqual(parsed["slots"], 4)
        self.assertEqual(parsed["data_base"], 150)
        self.assertEqual([x["id"] for x in parsed["entries"]], [1])
        entry = parsed["entries"][0]
        self.assertEqual(entry["stream_offset"], base + 18)
        self.assertEqual(entry["stream_address_field_offset"], base + 11)
        self.assertEqual(entry["mix_signed"], 90)
        self.assertEqual(entry["repeat_count"], 1)
        self.assertTrue(entry["isolated_supported"])

    def test_every_truncated_header(self):
        data, _ = bank_fixture()
        for length in range(134):
            with self.subTest(length=length), self.assertRaises(audio.AudioError):
                audio.parse_bank(data[:length])

    def test_wrong_format(self):
        data, _ = bank_fixture()
        for version in [b"RIFF      ", b"DCS 2.0G ", b"DCS 3.0H "]:
            with self.subTest(version=version), self.assertRaises(audio.AudioError):
                audio.parse_bank(change(data, 0, version))

    def test_header_data_size(self):
        data, _ = bank_fixture()
        with self.assertRaisesRegex(audio.AudioError, "bank data size"):
            audio.parse_bank(change(data, 130, struct.pack("<I", 1)))

    def test_index_overflow(self):
        data, _ = bank_fixture()
        with self.assertRaises(audio.AudioError):
            audio.parse_bank(change(data, 128, b"\xff\xff"))

    def test_empty_index(self):
        data, _ = bank_fixture()
        with self.assertRaises(audio.AudioError):
            audio.parse_bank(change(data, 138, b"\xff\xff\xff\xff"))

    def test_index_address_outside_bank(self):
        data, _ = bank_fixture()
        with self.assertRaisesRegex(audio.AudioError, "address outside bank"):
            audio.parse_bank(change(data, 138, b"\xfe\xff\xff\xff"))

    def test_table_must_start_at_base(self):
        data, _ = bank_fixture()
        with self.assertRaises(audio.AudioError):
            audio.parse_bank(change(data, 138, struct.pack("<I", 1)))

    def test_overlapping_entry(self):
        data, _ = bank_fixture()
        with self.assertRaisesRegex(audio.AudioError, "overlapping"):
            audio.parse_bank(change(data, 142, struct.pack("<I", 1)))

    def test_relative_stream_address_overflow(self):
        data, base = bank_fixture()
        with self.assertRaisesRegex(audio.AudioError, "stream outside"):
            audio.parse_bank(change(data, base + 11, b"\xff\xff\xff"))

    def test_stream_cannot_point_into_program(self):
        data, base = bank_fixture()
        with self.assertRaises(audio.AudioError):
            audio.parse_bank(change(data, base + 11, b"\x00\x00\x00"))

    def test_zero_frames(self):
        data, base = bank_fixture()
        with self.assertRaisesRegex(audio.AudioError, "frame count"):
            audio.parse_bank(change(data, base + 18, b"\x00\x00"))

    def test_reserved_frame_count(self):
        data, base = bank_fixture()
        with self.assertRaises(audio.AudioError):
            audio.parse_bank(change(data, base + 18, b"\xff\xff"))

    def test_repeat_zero_is_not_play_once(self):
        for repeat in [0, 2, 255]:
            data, _ = bank_fixture(repeat=repeat)
            entry = audio.parse_bank(data)["entries"][0]
            self.assertFalse(entry["sample_decode_supported"])
            self.assertFalse(entry["isolated_supported"])
            self.assertEqual(entry["repeat_count"], repeat)

    def test_negative_mix_not_linearized(self):
        data, _ = bank_fixture(mix=255)
        entry = audio.parse_bank(data)["entries"][0]
        self.assertEqual(entry["mix_signed"], -1)
        self.assertFalse(entry["sample_decode_supported"])

    def test_nonstandard_end_is_preserved_not_faked(self):
        data, _ = bank_fixture(end_wait=1)
        entry = audio.parse_bank(data)["entries"][0]
        self.assertEqual(entry["end_wait_frames"], 1)
        self.assertTrue(entry["sample_decode_supported"])
        self.assertFalse(entry["isolated_supported"])

    def test_unknown_controls_remain_opaque(self):
        data, base = bank_fixture()
        original = change(data, base + 4, b"\x13")
        entry = audio.parse_bank(original)["entries"][0]
        self.assertFalse(entry["sample_decode_supported"])
        self.assertEqual(entry["raw_program_hex"], original[base:base + 18].hex())

    def test_nonzero_wait_not_silently_discarded(self):
        data, base = bank_fixture()
        entry = audio.parse_bank(change(data, base + 8, b"\x01"))["entries"][0]
        self.assertFalse(entry["sample_decode_supported"])

    def test_valid_blob_pin(self):
        data = b"independently authored synthetic bytes"
        self.assertEqual(audio.verify_blob(data, audio.git_blob(data), "fixture"), data)

    def test_stale_or_tampered_pin_rejected(self):
        data = b"independently authored synthetic bytes"
        with self.assertRaisesRegex(audio.AudioError, "Git blob mismatch"):
            audio.verify_blob(data + b"!", audio.git_blob(data), "fixture")

    def test_canonical_json_stable(self):
        self.assertEqual(audio.canonical({"b": 1, "a": 2}), audio.canonical({"a": 2, "b": 1}))

    def test_exact_toolchain_profile(self):
        expected = {"version": "13.3.0", "target": "x86_64-linux-gnu"}
        audio.validate_toolchain(dict(expected), expected)

    def test_other_compiler_profiles_rejected(self):
        expected = {"version": "13.3.0", "target": "x86_64-linux-gnu"}
        for actual in [{"version": "14.0.0", "target": "x86_64-linux-gnu"},
                       {"version": "13.3.0", "target": "aarch64-linux-gnu"}]:
            with self.subTest(actual=actual), self.assertRaisesRegex(audio.AudioError, "compiler profile"):
                audio.validate_toolchain(actual, expected)

    def test_pcm_count_and_bounds(self):
        pcm = struct.pack("<720h", -32768, 32767, *([1] * 718))
        result = audio.validate_pcm(pcm, 1)
        self.assertEqual(result["pcm_samples"], 720)
        self.assertEqual(result["minimum"], -32768)
        self.assertEqual(result["maximum"], 32767)
        self.assertEqual(result["nonzero_samples"], 720)

    def test_truncated_pcm(self):
        with self.assertRaisesRegex(audio.AudioError, "PCM length"):
            audio.validate_pcm(bytes(1436) + b"\x01\x00", 1)

    def test_silent_substitution(self):
        with self.assertRaisesRegex(audio.AudioError, "all zero"):
            audio.validate_pcm(bytes(1440), 1)

    def test_wav_exact_pcm_and_rate(self):
        pcm = struct.pack("<6h", -32768, -1, 0, 1, 2, 32767)
        with wave.open(io.BytesIO(audio.wav_bytes(pcm)), "rb") as wav:
            self.assertEqual(wav.getframerate(), 31250)
            self.assertEqual(wav.getnchannels(), 1)
            self.assertEqual(wav.getsampwidth(), 2)
            self.assertEqual(wav.getnframes(), 6)
            self.assertEqual(wav.readframes(6), pcm)

    def test_odd_pcm_rejected(self):
        with self.assertRaises(audio.AudioError):
            audio.wav_bytes(b"\x01")

    def test_listing_identity_and_count(self):
        text = "Bank name: TEST.BNK\nTotal number of playlists: 1\n 1 $0001 1 10.0 11.0 Synthetic fixture\n"
        self.assertEqual(audio.parse_listing(text, "TEST"), {1: "Synthetic fixture"})
        with self.assertRaises(audio.AudioError):
            audio.parse_listing(text, "OTHER")
        with self.assertRaises(audio.AudioError):
            audio.parse_listing(text.replace("$0001", "$0002"), "TEST")
        with self.assertRaises(audio.AudioError):
            audio.parse_listing(text + " 1 $0001 1 10.0 11.0 Duplicate\n", "TEST")
        with self.assertRaises(audio.AudioError):
            audio.parse_listing(text.replace("playlists: 1", "playlists: 2"), "TEST")

    def test_duplicate_rng_weights_preserved(self):
        config = {"impact_tables": ["hit_snds"], "impact_fixed_symbol": "P_FIXED"}
        header = "#define P_A 2\n#define P_B 9\n#define P_FIXED 7\n"
        source = "static int hit_snds[] = {P_A,P_A,P_B};"
        tables, candidates = audio.impact_candidates(source, header, config)
        self.assertEqual(tables["hit_snds"], [2, 2, 9])
        self.assertEqual(candidates, [2, 7, 9])
        with self.assertRaises(audio.AudioError):
            audio.impact_candidates(source.replace("P_B", "P_MISSING"), header, config)


if __name__ == "__main__":
    unittest.main()
