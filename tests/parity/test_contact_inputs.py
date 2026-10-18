#!/usr/bin/env python3
"""Input/runner guards only; no synthetic fixture qualifies production animations or gameplay."""
import copy
import importlib.util
import json
from pathlib import Path
import shutil
import unittest
import uuid
import xml.etree.ElementTree as ET

import contact_inputs
import fetch_inputs
import prepare_contact_inputs
import prepare_inputs


class ContactInputGuards(unittest.TestCase):
    def setUp(self):
        self.work = fetch_inputs.ROOT / "tests/parity/.artifacts" / ("contact-guard-" + uuid.uuid4().hex)
        self.work.mkdir(parents=True)

    def tearDown(self):
        shutil.rmtree(self.work)

    def metadata(self):
        return {
            "schema": contact_inputs.SCHEMA, "source_revision": fetch_inputs.REVISION,
            "production_profile": dict(contact_inputs.PRODUCTION_PROFILE),
            "original_source_pins": fetch_inputs.input_pins("contact272"),
        }

    def test_all_69_old_pins_are_unchanged_in_the_case_exact_78_input_closure(self):
        old = fetch_inputs.input_pins()
        current = fetch_inputs.input_pins("contact272")
        self.assertEqual(69, len(old))
        self.assertEqual(78, len(current))
        self.assertEqual(old, {name: current[name] for name in old})
        self.assertEqual(contact_inputs.SOURCE_PINS, {name: pin for name, pin in current.items() if name not in old})
        self.assertEqual(30, sum(name.lower().endswith(".wan") for name in current))
        self.assertIn("anim/HIT99.WAN", current)
        self.assertNotIn("anim/hit99.wan", current)
        self.assertNotIn("anim/hits2.wan", current)
        self.assertNotIn("anim/puppet2k.wan", current)

    def test_new_profile_is_explicit_and_legacy_still_means_legacy(self):
        self.assertEqual("contact272", contact_inputs.profile_metadata(self.metadata()))
        self.assertEqual("legacy", contact_inputs.profile_metadata({
            "schema": "source-kernel-real-inputs/v1", "source_revision": fetch_inputs.REVISION}))
        path = self.work / "metadata.json"
        path.write_text(json.dumps(self.metadata()))
        with self.assertRaisesRegex(ValueError, "differs from fixture"):
            contact_inputs.fixture_profile(path, "legacy")

    def test_every_actual_production_profile_field_is_required_not_just_count_or_name(self):
        for field in contact_inputs.PRODUCTION_PROFILE:
            row = copy.deepcopy(self.metadata())
            row["production_profile"][field] = "not-the-actual-production-input"
            with self.subTest(field=field), self.assertRaisesRegex(ValueError, "exact explicit production profile"):
                contact_inputs.profile_metadata(row)

    def test_a_157_fixture_cannot_be_relabelled_contact272(self):
        row = self.metadata()
        row["schema"] = "source-kernel-real-inputs/v1"
        with self.assertRaisesRegex(ValueError, "disguise Contact272"):
            contact_inputs.profile_metadata(row)
        row = self.metadata()
        row.pop("production_profile")
        with self.assertRaisesRegex(ValueError, "exact explicit production profile"):
            contact_inputs.profile_metadata(row)

    def test_source_pin_case_omission_or_addition_is_not_accepted(self):
        for change in ("case", "remove", "add"):
            row = self.metadata()
            pins = row["original_source_pins"]
            if change == "case":
                pins["anim/hit99.wan"] = pins.pop("anim/HIT99.WAN")
            elif change == "remove":
                pins.pop("include/ani3d.equ")
            else:
                pins["unnecessary-original.c"] = "0" * 40
            with self.subTest(change=change), self.assertRaisesRegex(ValueError, "case-exact"):
                contact_inputs.profile_metadata(row)

    def test_fixture_dispatch_does_not_accept_metadata_only_without_actual_pinned_data(self):
        path = self.work / "fixture.json"
        row = self.metadata()
        row["legacy_fixture"] = {"path": "legacy-fixture.json", "sha256": contact_inputs.LEGACY_FIXTURE_SHA}
        path.write_text(json.dumps(row))
        with self.assertRaises(FileNotFoundError):
            prepare_inputs.fixture_hashes(path)
        (self.work / "legacy-fixture.json").write_text("our own invalid input guard")
        with self.assertRaisesRegex(ValueError, "content pin changed"):
            prepare_inputs.fixture_hashes(path)

    def test_union_compares_actual_named_clip_fields_and_rejects_overlap_or_changed_bytes(self):
        basis = [{"runtime_name": "OWN_BASE_" + str(i), "sha256": "owned-base", "selected_source_frame_indices": [3, 9]}
                 for i in range(157)]
        extra = [{"runtime_name": "OWN_EXTRA_" + str(i), "sha256": "owned-extra", "selected_source_frame_indices": [2, 7]}
                 for i in range(115)]
        production = copy.deepcopy(basis + extra)
        contact_inputs.require_disjoint_union(production, basis, extra)
        for field, value in (("sha256", "changed"), ("selected_source_frame_indices", [3, 8])):
            changed = copy.deepcopy(production)
            changed[0][field] = value
            with self.subTest(field=field), self.assertRaisesRegex(ValueError, "OWN_BASE_0." + field):
                contact_inputs.require_disjoint_union(changed, basis, extra)
        extra[0]["runtime_name"] = basis[0]["runtime_name"]
        with self.assertRaisesRegex(ValueError, "exact disjoint"):
            contact_inputs.require_disjoint_union(production, basis, extra)

    def test_preparer_cannot_write_outside_ignored_fixture_root(self):
        with self.assertRaisesRegex(ValueError, "ignored"):
            prepare_contact_inputs.prepare("unused.json", "unused", "unused", "unused.json",
                                           fetch_inputs.ROOT / "Assets/unwanted-contact")

    def test_actual_runtime_factory_and_real_selector_are_mandatory_in_contact_mutant_compiles(self):
        project = ET.parse(fetch_inputs.ROOT / "tests/contact/ContactChecks.csproj")
        self.assertIn("$(BlitzSourceRoot)Assets/Scripts/Runtime/SourceContactComposition.cs",
                      [row.attrib["Include"] for row in project.findall(".//Compile")])
        self.assertTrue(any(row.attrib.get("Project", "").endswith("SourceSoundVmSources.props")
                            for row in project.findall(".//Import")))
        guard = project.find(".//Target[@Name='RequireActualContactComposition']/Error")
        self.assertIn("SourceContactComposition.cs", guard.attrib["Condition"])
        spec = importlib.util.spec_from_file_location("profile_adapter_guard", fetch_inputs.ROOT / "tests/parity/run.py")
        runner = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(runner)
        row = {"status": "passed", "full_kernel_host_adapter_exercised": True, "contract_assertions": 332,
               "real_animation_library_sha256": contact_inputs.ANIMATION_SHA}
        runner.require_full_kernel(row, contact_inputs.ANIMATION_SHA)
        for field, value in (("contract_assertions", 331), ("full_kernel_host_adapter_exercised", False),
                             ("real_animation_library_sha256", prepare_inputs.MOVEMENT_ANIMATION_SHA)):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                runner.require_full_kernel(dict(row, **{field: value}), contact_inputs.ANIMATION_SHA)


if __name__ == "__main__":
    unittest.main()
