import importlib.util
from pathlib import Path
import struct
import sys
import unittest

TOOLS = Path(__file__).absolute().parents[2] / "tools/audio"
sys.path.insert(0, str(TOOLS))
spec = importlib.util.spec_from_file_location("original_playlist_preparation", TOOLS / "prepare_playlists.py")
playlists = importlib.util.module_from_spec(spec)
spec.loader.exec_module(playlists)


def original_shape(repeat=1, wait=4):
    program = bytes.fromhex("01010000070073") + b"\0\0\1\0" + b"\0\0\7" + bytes([repeat]) + struct.pack(">HB", wait, 0)
    return program + struct.pack(">H", 3) + bytes(16)


class PlaylistPreparationTests(unittest.TestCase):
    def test_finite_source_stop_timing_is_not_replaced_by_isolated_sample_duration(self):
        bank = original_shape(wait=2)
        program, instructions = playlists.parse_program(bank, 0, len(bank))
        self.assertEqual(playlists.timeline(instructions), {"endFrame": 2})
        self.assertEqual(program, bank[:-18])
        self.assertEqual(instructions[1]["stream"], len(program))

    def test_actual_infinite_stream_retains_its_repeat_and_wait_contract(self):
        _, instructions = playlists.parse_program(original_shape(repeat=0, wait=65535), 0, 36)
        self.assertEqual(playlists.timeline(instructions), {"loopStartFrame": 0, "loopFrames": 3})
        self.assertEqual(instructions[1]["repeat"], 0)

    def test_explicit_program_loop_uses_body_waits_and_rejects_a_zero_duration_spin(self):
        instructions = [
            dict(wait=0, opcode=14, operands="00"),
            dict(wait=7, opcode=13, operands=""),
            dict(wait=5, opcode=15, operands=""),
        ]
        self.assertEqual(playlists.timeline(instructions), {"loopStartFrame": 0, "loopFrames": 12})
        instructions[1]["wait"] = instructions[2]["wait"] = 0
        with self.assertRaisesRegex(ValueError, "zero-duration"):
            playlists.timeline(instructions)

    def test_unknown_dcs3_timers_and_invalid_relative_addresses_are_never_skipped(self):
        bank = bytearray(original_shape())
        bank[4] = 0x13
        with self.assertRaisesRegex(ValueError, "opcode0x13"):
            playlists.parse_program(bank, 0, len(bank))
        bank = bytearray(original_shape())
        bank[11:14] = b"\xff\xff\xff"
        with self.assertRaisesRegex(ValueError, "relative original stream"):
            playlists.parse_program(bank, 0, len(bank))

    def test_explicit_233_global_operators_keep_original_waits_and_three_byte_fade_operand(self):
        bank = bytes.fromhex("01010000130a003c140a003c003c00")
        with self.assertRaisesRegex(ValueError, "opcode0x13"):
            playlists.parse_program(bank, 0, len(bank))
        program, instructions = playlists.parse_program(bank, 0, len(bank), implicit_global=True)
        self.assertEqual(program, bank)
        self.assertEqual([(i["opcode"], i["operands"]) for i in instructions],
                         [(0x13, "0a"), (0x14, "0a003c"), (0, "")])
        self.assertEqual(playlists.timeline(instructions), {"endFrame": 120})
        with self.assertRaisesRegex(ValueError, "truncated original program operand"):
            playlists.parse_program(bytes.fromhex("01010000140a00"), 0, 7, implicit_global=True)


if __name__ == "__main__":
    unittest.main()
