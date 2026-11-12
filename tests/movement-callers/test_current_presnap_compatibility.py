import copy
import json
import unittest

import canonical
import current_presnap_compatibility as compatibility
import prepare_public_replay as replay


class IntegratedV5BoundaryTests(unittest.TestCase):
    def stale(self):
        return dict(profile=compatibility.PROFILE, factory=compatibility.FACTORY,
            sourceData=canonical.PRESNAP_DATA_SHA, observed=False, historical=False,
            replayFileSha256=replay.recording(replay.PRESNAP_CURRENT_CASE)["public_sha256"],
            gameplayCompatibility="Portable", originalPublicVectorRetainedWithoutStateConditionedRegeneration=True,
            completedHosts=4913, SourceTick=4667, rawCursor=8643, rSkipFrames=0, positive=None,
            failure=dict(host=4912, before=4666, SourceTick=4667,
                error=compatibility.generations.SIDE_ERROR + "\n" + compatibility.STALE_GUARD + "\nsynthetic stack"))

    def test_only_the_actual_current_v5_first_side_guard_is_a_stale_negative(self):
        summary=dict(status="error",assertions=1)
        receipt=self.stale()
        self.assertEqual(compatibility.classify_v5_stale(2,summary,receipt),
                         "rejected-integrated-current-v5-stale")
        for field,value in (("completedHosts",4912),("SourceTick",4666),("rawCursor",8644),
                            ("profile","original-teams-contact281-c2-m1"),("factory","historical"),
                            ("observed",True),("positive",{"actor":12}),("replayFileSha256","0"*64)):
            with self.subTest(field=field),self.assertRaises(ValueError):
                compatibility.classify_v5_stale(2,summary,dict(receipt,**{field:value}))
        for field,value in (("host",4913),("before",4665),("error","PRE_SNAP_ASSERTION: numeric mismatch"),
                            ("error",compatibility.generations.SIDE_ERROR+"\n"+compatibility.STALE_GUARD.replace("playbook=0","playbook=1"))):
            changed=copy.deepcopy(receipt);changed["failure"][field]=value
            with self.subTest(failure_field=field),self.assertRaises(ValueError):
                compatibility.classify_v5_stale(2,summary,changed)

    def test_compiler_status_only_or_success_is_never_the_required_stale_receipt(self):
        for code,summary in ((0,dict(status="passed",assertions=1)),(2,dict(status="error",assertions=0)),
                             (1,dict(status="failed",assertions=1)),(2,None)):
            with self.subTest(code=code,summary=summary),self.assertRaises(ValueError):
                compatibility.classify_v5_stale(code,summary,self.stale())

    def test_reviewed_q11_wrong_side_boundary_cannot_replace_the_original_integrated_boundary(self):
        receipt=self.stale()
        receipt.update(completedHosts=1524,SourceTick=1447,rawCursor=2444,
            failure=dict(host=1523,before=1446,SourceTick=1447,
                error=compatibility.generations.SIDE_ERROR+"\n"+compatibility.REVIEWED_Q11_STALE_GUARD))
        summary=dict(status="error",assertions=1)
        self.assertEqual(compatibility.classify_reviewed_q11_v5_stale(2,summary,receipt),
                         "rejected-reviewed-q11-old-v5-wrong-side")
        for field,value in (("completedHosts",4913),("SourceTick",4667),("rawCursor",8643),
                            ("observed",True),("positive",{"actor":12})):
            with self.subTest(field=field),self.assertRaises(ValueError):
                compatibility.classify_reviewed_q11_v5_stale(2,summary,dict(receipt,**{field:value}))
        with self.assertRaises(ValueError):
            compatibility.classify_v5_stale(2,summary,receipt)
        with self.assertRaises(ValueError):
            compatibility.classify_reviewed_q11_v5_stale(2,summary,self.stale())

    def test_original_integrated_source_is_archived_not_substituted_from_live_q11(self):
        files,generation=compatibility.integrated_snapshot()
        harness={name:files[name] for name in canonical.HARNESS}
        production={name:raw for name,raw in files.items() if name not in harness}
        self.assertEqual(canonical.observer_binding(production,harness),canonical.CURRENT_OBSERVER_GENERATION)
        name="Assets/Scripts/Gameplay/SourceDroneAI.cs"
        changed=dict(files);changed[name]=(canonical.ROOT/name).read_bytes()
        with self.assertRaisesRegex(ValueError,"exact pinned pure source"):
            compatibility.generations.validate_snapshot(changed,generation["source_count"],
                generation["source_manifest_sha256"],generation["harness_manifest_sha256"])

    def test_reviewed_report_cannot_omit_current_boundary_by_downgrading_its_schema(self):
        source=json.loads(replay.reviewed_q11_metadata_paths()["source"].read_bytes())
        report=dict(schema="source-integrated-presnap-v5-compatibility/v1",
                    source_sha256=source,status="passed-separate-v5-compatibility-boundaries",
                    production_harness_unchanged=True,inputs_unchanged=True,
                    runs={"historical-v5-positive":{},"integrated-current-v5-stale":{}})
        with self.assertRaisesRegex(ValueError,"cannot omit"):
            compatibility.validate_report(report)

    def test_compatibility_status_cannot_replace_both_actual_source_views(self):
        for report in ({},dict(schema="source-integrated-presnap-v5-compatibility/v1",
                              status="passed-separate-v5-compatibility-boundaries",
                              production_harness_unchanged=True,inputs_unchanged=True,
                              runs={"integrated-current-v5-stale":{}})):
            with self.assertRaises(ValueError):
                compatibility.validate_report(report)


if __name__ == "__main__":
    unittest.main()
