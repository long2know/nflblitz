#!/usr/bin/env python3
"""Behavior and integrity checks executed against the compiled original C."""
import argparse
import json
from pathlib import Path
import shutil
import struct
import unittest

from cases import clock_input, command, dispatch_input
from oracle import Oracle, OracleError, PINS, SUITE, verified_sources


def bits(value):
    return struct.unpack("<I", struct.pack("<f", value))[0]


class OriginalOracleTests(unittest.TestCase):
    oracle = None

    @classmethod
    def setUpClass(cls):
        if cls.oracle is None:
            cls.oracle = Oracle(SUITE / ".artifacts" / "original")

    def clock(self, **kwargs):
        return self.oracle.execute([command("clock", clock_input(**kwargs))])[0]

    def classify(self, **kwargs):
        return self.oracle.execute([command("classify", dispatch_input(**kwargs))])[0]

    def test_01_exact_blobs_and_unmodified_original_function_bytes(self):
        self.assertEqual(len(verified_sources(self.oracle.source_root)), len(PINS["blobs"]))
        unit = self.oracle.unit.read_bytes()
        data = self.oracle.sources["result.c"]
        self.assertIn(data[data.index(b"static void _norm_car_oob( void );"):], unit)
        self.assertTrue(all(selection["unmodified_original_bytes"] for selection in self.oracle.selections))
        self.assertEqual(self.oracle.execute(["meta"])[0]["types"], 5)
        self.assertEqual(self.oracle.execute(["meta"])[0]["causes"], 6)

    def test_02_wrong_blob_fails_closed_before_compilation(self):
        copied = self.oracle.artifacts / "pin-negative"
        try:
            for name in PINS["blobs"]:
                destination = copied / name
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(self.oracle.source_root / name, destination)
            original = copied / "nfl.c"
            original.write_bytes(original.read_bytes() + b"\n")
            with self.assertRaisesRegex(OracleError, "Source blob mismatch: nfl.c"):
                verified_sources(copied)
        finally:
            if copied.exists():
                shutil.rmtree(copied)

    def test_03_missing_external_source_is_failure_not_skip(self):
        with self.assertRaisesRegex(OracleError, "Required user-supplied original source is missing"):
            verified_sources(self.oracle.artifacts / "absent-source")

    def test_04_original_initialization_and_five_reference_targets(self):
        cases = [
            (dict(packed=0, initialize=True), 0x13b18),
            (dict(), 0x1e14),
            (dict(quarter=1), 0x1e22),
            (dict(quarter=3), 0x1e22),
            (dict(packed=0x2800, play_time=17), 0x2730),
            (dict(adjustment=-1), 0x1e12),
        ]
        for data, expected in cases:
            with self.subTest(data=data):
                output = self.clock(**data)
                self.assertEqual(output["packed"], expected)
                self.assertEqual(output["play_time"], data.get("play_time", 0) + 1)
                self.assertEqual(output["time_of_possession"], [14, 19])

    def test_05_original_adjustment_table_and_error_fallback(self):
        for adjustment, reload_value in enumerate([30, 25, 20, 18, 16]):
            with self.subTest(adjustment=adjustment):
                output = self.clock(adjustment=adjustment)
                self.assertEqual(output["packed"], 0x1e00 | reload_value)
                self.assertEqual(output["events"], ["get_adjustment:TIME_ADJ", "sleep:1"])
        for data in [dict(adjustment=-1), dict(adjustment=5), dict(adjustment=2, read_error=True)]:
            self.assertEqual(self.clock(**data)["packed"], 0x1e12)

    def test_06_inactive_and_attract_clocks_sleep_without_mutating(self):
        for data in [dict(active=False), dict(attract=True), dict(active=False, attract=True)]:
            output = self.clock(**data)
            self.assertEqual(output, {
                "packed": 0x1f00, "play_time": 0,
                "time_of_possession": [13, 19], "events": ["sleep:1"],
            })

    def test_07_subtick_neither_charges_possession_nor_reads_cmos(self):
        self.assertEqual(self.clock(packed=0x1f14, side=1), {
            "packed": 0x1f13, "play_time": 0,
            "time_of_possession": [13, 19], "events": ["sleep:1"],
        })

    def test_08_half_warning_precedes_long_play_and_alternates(self):
        odd = self.clock(quarter=1, play_time=18)
        even = self.clock(packed=0x1e00, quarter=1, play_time=18)
        self.assertEqual(odd["packed"], 0x1e22)
        self.assertEqual(even["packed"], 0x1d22)
        self.assertEqual(odd["events"], [
            "sound:cursor:CLOCK_LOW_SND:VOLUME4:127:PRIORITY_LVL5", "sleep:1",
        ])
        self.assertEqual(even["events"], ["sleep:1"])
        self.assertEqual(self.clock(packed=0x2000, quarter=1, play_time=18)["packed"], 0x1f30)
        self.assertEqual(self.clock(quarter=4)["packed"], 0x1e14)

    def test_09_zero_process_exits_without_sleep_and_final_subtick_sleeps(self):
        self.assertEqual(self.clock(packed=0)["events"], [])
        final = self.clock(packed=1)
        self.assertEqual(final["packed"], 0)
        self.assertEqual(final["events"], ["sleep:1"])

    def test_10_original_fraction_discard_is_byte_mask(self):
        self.assertEqual(self.oracle.execute(["discard 0x13b18", "discard 255", "discard 0x00ffffff"]),
                         [{"packed": 0x13b00}, {"packed": 0}, {"packed": 0x00ffff00}])

    def test_11_every_table_entry_and_intentional_empty_rows(self):
        expected_rows = [
            [0, 0, 2, 1, 5, 5],
            [4, 4, 2, 1, 4, 14],
            [-123] * 6, [-123] * 6,
            [10, 10, 3, 11, 10, 10],
        ]
        for play_type, row in enumerate(expected_rows):
            for cause, expected in enumerate(row):
                ball = 3 if play_type == 1 and cause == 5 else 0
                with self.subTest(play_type=play_type, cause=cause):
                    actual = self.classify(play_type=play_type, cause=cause, ball=ball)
                    self.assertEqual(actual["play_result"], expected)
                    self.assertEqual(actual["play_result_flags"], 0x1234)

    def test_12_endzone_possession_flag_crossproduct(self):
        for flags in range(16):
            retained_recovery = bool(flags & 1) and not bool(flags & 2)
            for play_type in [0, 1, 4]:
                touchback = retained_recovery and (play_type == 1 or bool(flags & 8))
                expected = (10 if touchback else 3) if play_type == 4 else (15 if touchback else 2)
                with self.subTest(flags=flags, play_type=play_type):
                    self.assertEqual(self.classify(play_type=play_type, cause=2, flags=flags)["play_result"], expected)

    def test_13_fumble_relocation_does_not_recompute_original_fpend(self):
        actual = self.classify(cause=5, ball=1, field_end=90, end_x=312, fumble_x=-312)
        self.assertEqual(actual["play_result"], 6)
        self.assertEqual(actual["play_end_pos_bits"], bits(-312))
        self.assertEqual(actual["fpend"], 90)
        changed = self.classify(cause=5, ball=1, field_end=100)
        self.assertEqual(changed["play_result"], 15)
        self.assertEqual(changed["off_side"], 1)

    def test_14_original_spot_double_truncation_and_non_td_clamp(self):
        commands = [
            "spot 0 1 0 0 0 0 -389.99 0 -123",
            "spot 0 1 0 1 0 0 -389.99 0 -123",
            "spot 0 1 0 0 0 0 -0.99999 0 -123",
            "spot 0 1 0 0 0 0 468 0 -123",
        ]
        a, b, center, outside = self.oracle.execute(commands)
        self.assertEqual(a["play_end_pos_bits"], 3284081050)
        self.assertEqual(a["fpend"], 1)
        self.assertEqual(b["fpend"], 99)
        self.assertEqual(center["play_end_pos_bits"], bits(0))
        self.assertEqual(center["fpend"], 50)
        self.assertEqual(outside["fpend"], 99)

    def test_15_touchdown_and_conversion_override_spot_to_100(self):
        output = self.oracle.execute([
            "spot 0 3 0 0 0 0 390 0 -123",
            "spot 4 3 0 1 0 0 -390 0 -123",
            "spot 0 1 0 0 0 0 -468 0 -123",
        ])
        self.assertEqual([row["fpend"] for row in output], [100, 100, 1])
        self.assertEqual([row["play_result"] for row in output[:2]], [1, 11])

    def test_16_point_projection_is_unclamped_and_degenerate_is_nan(self):
        output = self.oracle.execute([
            "point 0 0 0 10 0 0 5 3 0",
            "point 0 0 0 10 0 0 -5 0 0",
            "point 0 0 0 10 0 0 15 0 0",
            "point 0 0 0 0 0 0 1 0 0",
        ])
        self.assertEqual([row["value"] for row in output], [0.5, -0.5, 1.5, "nan"])

    def test_17_original_parallel_segment_quirk_is_not_idealized_geometry(self):
        output = self.oracle.execute([
            "line 0 0 0 1 0 0 10 0 0 11 0 0 0",
            "line 0 0 0 10 0 0 0 2 0 10 2 0 1.9999",
            "line 0 0 0 10 0 0 0 2 0 10 2 0 2",
        ])
        self.assertEqual([row["intersects"] for row in output], [True, False, True])

    def test_18_release_infinite_loop_domains_fail_without_entering_them(self):
        with self.assertRaisesRegex(OracleError, "Original C oracle failed \\(2\\)"):
            self.oracle.execute(["classify 0 5 0 0 3 50 0 0 -123"])

    def test_19_native_layout_and_exact_source_macro_values_are_recorded(self):
        meta = self.oracle.execute(["meta"])[0]
        self.assertEqual(meta["int_bytes"], 4)
        self.assertEqual(meta["float_bytes"], 4)
        self.assertEqual(meta["float_mantissa_bits"], 24)
        self.assertEqual(meta["quarter_time"], 0x20000)
        self.assertEqual(meta["default_auto_hike_ticks"], 285)
        self.assertEqual(meta["source_units_per_yard_bits"], 1090099609)
        self.assertNotEqual(meta["source_units_per_yard_bits"], bits(7.8))

    def test_20_published_discard_does_not_rewrite_suspended_process_local_gt(self):
        output = self.oracle.execute(["clock-pair 0x1f14 0 0 0 1 2 2 0 13 19 2"])[0]
        self.assertEqual(output["first"]["packed"], 0x1f13)
        self.assertEqual(output["published_after_discard"], 0x1f00)
        self.assertEqual(output["second"]["packed"], 0x1f12)
        self.assertEqual(output["completed_iterations"], 2)
        self.assertEqual(output["second"]["events"], ["sleep:1"])

    def test_21_original_onside_rng_short_circuit_and_strict_time_boundary(self):
        rows = self.oracle.execute([
            "onside 0 0x20000 0 20 0 0 1",
            "onside 3 0 0 0 40 1 0",
            "onside 2 0 0 0 40 1 1",
            "onside 3 0x3bff 1 20 21 1 1",
            "onside 3 0x3c00 1 20 21 1 1",
            "onside 4 0x20000 1 20 28 1 1",
        ])
        self.assertEqual([row["onside"] for row in rows], [True, False, False, True, False, True])
        self.assertEqual(rows[0]["draws"], [0])
        self.assertEqual(rows[0]["events"], ["rand:0"])
        self.assertEqual(rows[1]["draws"], [1, 0])
        self.assertEqual(rows[1]["events"], ["rand:1", "rand:0"])
        self.assertEqual(rows[3]["draws"], [1, 1])


def run_tests(oracle):
    OriginalOracleTests.oracle = oracle
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(OriginalOracleTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    if result.testsRun != 21 or result.skipped or not result.wasSuccessful():
        raise OracleError(f"Original C self-checks failed; executed={result.testsRun}, skipped={len(result.skipped)}")
    return result.testsRun


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, required=True)
    args = parser.parse_args()
    count = run_tests(Oracle(args.source_root))
    print(json.dumps({"status": "passed", "original_c_test_groups": count, "skipped": 0}))
