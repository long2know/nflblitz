import gzip
import hashlib
import json
from pathlib import Path
import sys
import unittest

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/"tests/movement-callers"))
import prepare_public_replay as replay
import export_portable_public as exporter
sys.path.remove(str(ROOT/"tests/movement-callers"))


class RecordingTests(unittest.TestCase):
    def test_all_three_historical_recordings_recipes_and_controller_remain_identical(self):
        for case in (replay.POSITIVE_CASE,replay.SOURCE_CURRENT_CASE,replay.SOURCE_TIMER_CASE):
            data=replay.load_fixture(replay.RECORDINGS[case]["fixture"],case)
            self.assertEqual(len(data.splitlines()),12000)
            replay.load_recipe(replay.RECIPES[case][0],case)
        self.assertEqual(hashlib.sha256((ROOT/"tests/movement-callers/PortablePublicPolicy.cs").read_bytes()).hexdigest(),
                         "e123e5db745acb3d4952dc0f56a2379eea36515b90bcd5c375dc769b797b330d")

    def test_new_recipe_is_closed_and_cannot_promote_old_profile_factory_or_layout(self):
        recipe=replay.make_presnap_recipe("a"*64,b"authored synthetic input",b"authored synthetic gzip")
        replay.validate_presnap_recipe(recipe)
        for field,value in (("c2_profile","contact281-c2-m1"),("factory","SourceC2Composition.CreateM1Simulation"),
                            ("station_layout",2),("seed",2),("recording_generation","source-selection-countdown/v3"),
                            ("source_manifest_sha256",None),("rawVector",[1])):
            with self.assertRaises(ValueError,msg=field):
                replay.validate_presnap_recipe({**recipe,field:value})

    def test_new_source_fault_incomplete_or_missing_B_witness_never_exports(self):
        source=(ROOT/"tests/movement-callers/Canonical.cs").read_text()
        self.assertIn("FileMode.CreateNew",source)
        self.assertIn('"STOP-new-source-fault"',source)
        self.assertIn('"UNMET-public-R_SKIP"',source)
        self.assertIn('"UNMET-fresh-B-switch"',source)
        self.assertIn("new PortablePublicController()",source)
        self.assertIn("new PortableCabinetPackets()",source)
        self.assertNotIn("seed++",source)
        self.assertIn("preSnapSwitchWitnesses",exporter.export_presnap.__code__.co_consts)


if __name__=="__main__":unittest.main()
