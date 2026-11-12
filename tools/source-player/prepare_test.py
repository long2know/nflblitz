import importlib.util
from pathlib import Path
import unittest


SPEC = importlib.util.spec_from_file_location("source_player_prepare", Path(__file__).with_name("prepare.py"))
prepare = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(prepare)


class DefaultAudiblesPreparationChecks(unittest.TestCase):
    def test_successor_metadata_is_explicit_and_old_defaults_are_unchanged(self):
        import inspect
        import build
        import prepare_default_audibles as audible
        self.assertEqual(build.PROFILE, "original-teams-contact282-c2-m1-presnap-switch")
        self.assertEqual(inspect.signature(prepare.Bundle.persist).parameters["profile"].default,
                         "original-teams-contact281-c2-m1")
        self.assertEqual(audible.PROFILE, "original-teams-contact282-c2-m1-default-audibles")
        self.assertEqual(audible.FEATURE_CONTRACT, "source-default-audibles/v1")
        self.assertEqual(audible.FACTORY, "SourceC2Composition.CreateDefaultAudiblesSimulation")

    def test_relabeling_without_feature_or_factory_is_rejected_before_payload_access(self):
        import json
        import prepare_default_audibles as audible
        document = dict(profile=audible.PROFILE)
        with self.assertRaisesRegex(ValueError, "featureContract"):
            audible.require_manifest(document, json.dumps(document).encode())
        document["featureContract"] = audible.FEATURE_CONTRACT
        with self.assertRaisesRegex(ValueError, "factory"):
            audible.require_manifest(document, json.dumps(document).encode())
        document["factory"] = "SourceC2Composition.CreatePreSnapSwitchSimulation"
        with self.assertRaisesRegex(ValueError, "factory"):
            audible.require_manifest(document, json.dumps(document).encode())

    def test_actual_metadata_bundle_and_manifest_mutations(self):
        import copy
        import json
        import os
        import build
        import prepare_default_audibles as audible
        selected = os.environ.get("SOURCE_PLAYER_TEST_DEFAULT_AUDIBLES_DATA")
        if not selected:
            self.skipTest("Explicit ignored default-audibles data is required; no substitute assets are generated.")
        root = Path(selected)
        self.assertTrue(root.is_dir(), "The explicitly selected actual metadata-only root is missing.")
        raw = (root / "manifest.json").read_bytes()
        document = json.loads(raw)
        audible.require_manifest(document, raw)
        build.verify_private_inputs(root, document)
        self.assertEqual(len(document["files"]), 563)
        self.assertEqual(document["entrypoints"]["settings"], "settings-presnap-switch.json")
        for field in ("featureContract", "factory", "files", "entrypoints"):
            altered = copy.deepcopy(document)
            if field in ("featureContract", "factory"):
                del altered[field]
            elif field == "files":
                altered[field][0]["sha256"] = "0" * 64
            else:
                altered[field]["settings"] = "settings.json"
            with self.subTest(field=field), self.assertRaises(ValueError):
                audible.require_manifest(altered, (json.dumps(altered, indent=2, sort_keys=True) + "\n").encode())


class Av306BuildAdmissionChecks(unittest.TestCase):
    def test_exact_caller_generations_do_not_alias(self):
        import build
        self.assertEqual(build.caller_drain_profile(build.SELECTED_WORLD_PROFILE, build.PRIMARY1114_MANIFEST_SHA256),
                         "aot-rooted-bounded-caller-drain/v1")
        self.assertEqual(build.caller_drain_profile(build.AV306_PROFILE, build.AV306_MANIFEST_SHA256),
                         "aot-rooted-bounded-caller-drain-av306/v1")
        for profile, pin in [(build.SELECTED_WORLD_PROFILE, build.AV306_MANIFEST_SHA256),
                             (build.AV306_PROFILE, build.PRIMARY1114_MANIFEST_SHA256),
                             (build.PROFILE, build.AV306_MANIFEST_SHA256)]:
            with self.subTest(profile=profile, pin=pin), self.assertRaises(ValueError):
                build.caller_drain_profile(profile, pin)

    def test_new_generation_cannot_reuse_old_aot_modules(self):
        import contextlib
        import io
        import build
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as result:
            build.main(["--data", "not-read", "--profile", build.AV306_PROFILE, "--aot",
                        "--caller-drain", "--reuse-aot-bundle", "not-read"])
        self.assertEqual(result.exception.code, 2)

    def test_old_default_and_diagnostic_modes_are_not_promoted(self):
        import build
        self.assertEqual(build.PROFILE, "original-teams-contact282-c2-m1-presnap-switch")
        import contextlib
        import io
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as result:
            build.main(["--data", "not-read", "--profile", build.AV306_PROFILE, "--caller-drain"])
        self.assertEqual(result.exception.code, 2)

    def test_actual_authenticated_av306_metadata_and_relabels(self):
        import os
        import json
        import copy
        import build
        selected = os.environ.get("SOURCE_PLAYER_TEST_AV306_MANIFEST")
        if not selected:
            self.skipTest("An explicit actual authenticated AV306 manifest is required; no synthetic accepted root.")
        raw = Path(selected).read_bytes()
        document = json.loads(raw)
        build.require_av306_manifest(document, raw)
        self.assertEqual(len(document["files"]), 1153)
        self.assertEqual(document["features"]["originalSelectedClipInputs"], 306)
        self.assertEqual(document["features"]["originalCompiledPlaylistPrograms"], 1205)
        for key in ("profile", "schema", "ancestorManifestSha256", "factory", "files"):
            altered = copy.deepcopy(document)
            if key == "files":
                altered["files"][0]["sha256"] = "0" * 64
            else:
                altered[key] = "not-the-authenticated-generation"
            with self.subTest(key=key), self.assertRaises(ValueError):
                build.require_av306_manifest(altered, json.dumps(altered).encode())


class TwoStationRecordsBuildAdmissionChecks(unittest.TestCase):
    def test_exact_caller_rejects_all_legacy_data_aliases(self):
        import build
        self.assertEqual(build.caller_drain_profile(build.TWO_STATION_RECORDS_PROFILE,
                         build.TWO_STATION_RECORDS_MANIFEST_SHA256),
                         "aot-rooted-bounded-caller-drain-av306-two-station-records/v1")
        for profile, pin in [
                (build.TWO_STATION_RECORDS_PROFILE, build.AV306_MANIFEST_SHA256),
                (build.TWO_STATION_RECORDS_PROFILE, build.PRIMARY1114_MANIFEST_SHA256),
                (build.AV306_PROFILE, build.TWO_STATION_RECORDS_MANIFEST_SHA256),
                (build.SELECTED_WORLD_PROFILE, build.TWO_STATION_RECORDS_MANIFEST_SHA256)]:
            with self.subTest(profile=profile, pin=pin), self.assertRaises(ValueError):
                build.caller_drain_profile(profile, pin)

    def test_new_named_factory_requires_fresh_aot_and_explicit_caller_mode(self):
        import build
        import contextlib
        import io
        for options in [
                ["--aot", "--caller-drain", "--reuse-aot-bundle", "not-read"],
                ["--caller-drain"]]:
            with self.subTest(options=options), contextlib.redirect_stderr(io.StringIO()), \
                    self.assertRaises(SystemExit) as result:
                build.main(["--data", "not-read", "--profile", build.TWO_STATION_RECORDS_PROFILE, *options])
            self.assertEqual(result.exception.code, 2)

    def test_actual_two_station_settings_and_immutable_ancestor_inventory(self):
        import os
        import json
        import copy
        import build
        selected = os.environ.get("SOURCE_PLAYER_TEST_TWO_STATION_RECORDS_DATA")
        if not selected:
            self.skipTest("An explicit actual authenticated named TwoStation root is required.")
        root = Path(selected)
        raw = (root / "manifest.json").read_bytes()
        document = json.loads(raw)
        build.require_two_station_records_manifest(document, raw)
        build.verify_private_inputs(root, document)
        settings = json.loads((root / document["entrypoints"]["settings"]).read_bytes())
        self.assertEqual(settings["station_layout"], 2)
        self.assertEqual(settings["stations"], [
            dict(id=0, initial_player_id=-1, profile=None, team=0),
            dict(id=1, initial_player_id=-1, profile=None, team=1)])
        self.assertEqual(len(document["files"]), 1154)
        self.assertNotIn("settings-presnap-switch.json", document["entrypoints"].values())
        for key in ("profile", "factory", "ancestorManifestSha256", "settings"):
            altered = copy.deepcopy(document)
            if key == "settings":
                altered["entrypoints"]["settings"] = "settings-presnap-switch.json"
            else:
                altered[key] = "not-the-authenticated-two-station-generation"
            with self.subTest(key=key), self.assertRaises(ValueError):
                build.require_two_station_records_manifest(altered, json.dumps(altered).encode())


class PreparationChecks(unittest.TestCase):
    def test_presnap_default_changes_only_existing_station_layout_type(self):
        import json
        old=json.loads((prepare.HERE/"match-settings.json").read_bytes())
        new=json.loads((prepare.HERE/"match-settings.presnap-switch.json").read_bytes())
        self.assertEqual(old["station_layout"],2)
        self.assertEqual(new["station_layout"],4)
        self.assertEqual({**new,"station_layout":2},old)
        self.assertEqual(new["stations"],old["stations"])

    def test_build_rejects_historical_data_under_new_default_before_publish(self):
        import json
        import shutil
        import uuid
        import build
        from unittest.mock import patch
        root=prepare.PROJECT/".squad/.scratch"/("presnap-build-negative-"+uuid.uuid4().hex)
        root.mkdir(parents=True)
        try:
            (root/"manifest.json").write_text(json.dumps(dict(schema="blitz-portable-source-player/v1",
                profile=build.OLD_PROFILE,files=[])))
            for options in ([], ["--no-restore"]):
                with self.subTest(options=options), patch.object(build.subprocess,"run",
                        side_effect=AssertionError("No publish may run for mismatched data")):
                    with self.assertRaises(SystemExit) as failure:
                        build.main(["--data",str(root),*options])
                    self.assertEqual(failure.exception.code,2)
        finally:shutil.rmtree(root)

    def test_build_rejects_unlisted_private_file_before_publish(self):
        import build
        import json
        import shutil
        from unittest.mock import patch
        root, source, _, _ = self.camera_fixture()
        try:
            (source / "unlisted.bytes").write_bytes(b"Authored unlisted input, not original data.")
            with patch.object(build.subprocess, "run", side_effect=AssertionError("Invalid inputs must not publish")):
                with self.assertRaises(SystemExit) as failure:
                    build.main(["--data", str(source), "--profile", build.OLD_PROFILE])
                self.assertEqual(failure.exception.code, 2)
            manifest = json.loads((source / "manifest.json").read_bytes())
            with self.assertRaisesRegex(ValueError, "unlisted.bytes"):
                build.verify_private_inputs(source, manifest)
        finally:
            shutil.rmtree(root)

    def test_build_offline_publish_is_explicit_and_preserves_input_gate(self):
        import build
        import shutil
        from unittest.mock import patch
        root, source, _, _ = self.camera_fixture()
        try:
            for offline in (False, True):
                with self.subTest(offline=offline):
                    options = ["--no-restore"] if offline else []
                    with patch.object(build.subprocess, "run",
                                      side_effect=RuntimeError("Captured actual SDK publish")) as publish:
                        with self.assertRaisesRegex(RuntimeError, "Captured actual SDK publish"):
                            build.main(["--data", str(source), "--profile", build.OLD_PROFILE, *options])
                        command = publish.call_args.args[0]
                        self.assertIn("publish", command)
                        self.assertEqual(int(offline), command.count("--no-restore"))
            (source / "unlisted.bytes").write_bytes(b"Unlisted synthetic input.")
            with patch.object(build.subprocess, "run") as publish:
                with self.assertRaises(SystemExit) as failure:
                    build.main(["--data", str(source), "--profile", build.OLD_PROFILE, "--no-restore"])
                self.assertEqual(2, failure.exception.code)
                publish.assert_not_called()
        finally:
            shutil.rmtree(root)

    def test_build_aot_explicit_rooted_trim_does_not_change_default(self):
        import build
        import shutil
        from unittest.mock import patch
        root, source, _, _ = self.camera_fixture()
        try:
            for aot in (False, True):
                with self.subTest(aot=aot), patch.object(build.subprocess, "run",
                        side_effect=RuntimeError("Captured actual SDK publish")) as publish:
                    options = ["--aot"] if aot else []
                    with self.assertRaisesRegex(RuntimeError, "Captured actual SDK publish"):
                        build.main(["--data", str(source), "--profile", build.OLD_PROFILE,
                                    "--no-restore", *options])
                    command = publish.call_args.args[0]
                    self.assertIn("publish", command)
                    self.assertIn("--no-restore", command)
                    self.assertEqual(aot, "-p:RunAOTCompilation=true" in command)
                    self.assertEqual(aot, "-p:WasmBuildNative=true" in command)
                    self.assertEqual(aot, "-p:PublishTrimmed=true" in command)
                    self.assertEqual(aot, "-p:JsonSerializerIsReflectionEnabledByDefault=true" in command)
                    self.assertFalse(any(value.startswith("-p:TrimMode=") for value in command))
            (source / "unlisted.bytes").write_bytes(b"Unlisted synthetic input.")
            with patch.object(build.subprocess, "run") as publish, self.assertRaises(SystemExit):
                build.main(["--data", str(source), "--profile", build.OLD_PROFILE, "--aot"])
            publish.assert_not_called()
        finally:
            shutil.rmtree(root)

    def test_republish_replaces_only_generated_data_and_rejects_input_overlap(self):
        import build
        import json
        import shutil
        root, source, _, _ = self.camera_fixture()
        try:
            manifest = json.loads((source / "manifest.json").read_bytes())
            output = root / "AppBundle"
            (output / "data/world").mkdir(parents=True)
            (output / "data/world/old-venue.bytes").write_bytes(b"stale generated world")
            (output / "main.js").write_bytes(b"keep emitted loader")
            before = {p.name: p.read_bytes() for p in source.iterdir()}
            build.stage_private_inputs(source, manifest, output)
            self.assertEqual({"manifest.json", "first.bytes", "second.bytes"},
                             {p.relative_to(output / "data").as_posix()
                              for p in (output / "data").rglob("*") if p.is_file()})
            self.assertEqual(b"keep emitted loader", (output / "main.js").read_bytes())
            self.assertEqual(before, {p.name: p.read_bytes() for p in source.iterdir()})
            with self.assertRaisesRegex(ValueError, "overlap"):
                build.stage_private_inputs(output / "data", manifest, output)
            self.assertEqual(before, {p.name: p.read_bytes() for p in (output / "data").iterdir()})
        finally:
            shutil.rmtree(root)

    def test_source_step_cost_facade_preserves_terminal_string_and_host_rejections(self):
        import subprocess
        probe = r"""
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
const code = await readFile(process.argv[1], 'utf8');
const start = code.lastIndexOf("Object.defineProperty(globalThis, 'sourcePlayer',");
assert.ok(start >= 0);
const packet = '{"schema":"source-player-source-step-cpu/v1","enabled":true}';
let reads = 0, frames = 0;
const context = { ready: false, failed: true, api: {
  SourceStepCosts: () => { reads++; return packet; },
  Frame: () => { frames++; throw new Error('Diagnostics cannot advance a frame'); },
}};
vm.runInNewContext(code.slice(start), context);
assert.equal(context.sourcePlayer.sourceStepCosts(), packet);
assert.equal(reads, 1);
assert.equal(frames, 0);
const active = new Error('Actual host requires paused or terminal inspection');
context.api.SourceStepCosts = () => { throw active; };
assert.throws(() => context.sourcePlayer.sourceStepCosts(), error => error === active);
context.api = undefined;
assert.throws(() => context.sourcePlayer.sourceStepCosts(), /not initialized/);
context.callerTrace = { snapshot: () => ({ schema: 'source-player-bounded-caller-drain-diagnostics/v1' }) };
context.callerDrain = { generation: 4, pending: null, closed: false };
context.callerProfile = 'aot-rooted-bounded-caller-drain/v1';
context.input = { paused: false }; context.failed = false;
assert.throws(() => context.sourcePlayer.callerDiagnostics(), /public pause/);
context.input.paused = true;
const diagnostic = context.sourcePlayer.callerDiagnostics();
assert.equal(typeof diagnostic, 'string');
assert.equal(JSON.parse(diagnostic).generation, 4);
context.input.paused = false; context.failed = true;
assert.equal(JSON.parse(context.sourcePlayer.callerDiagnostics()).enabled, true);
assert.equal(frames, 0);
"""
        result = subprocess.run(["node", "--input-type=module", "-e", probe,
                                 str(prepare.HERE / "wwwroot/main.js")],
                                capture_output=True, text=True, timeout=30)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_caller_drain_is_explicit_and_never_uses_an_interpreted_or_other_data_profile(self):
        import build
        import contextlib
        import io
        from unittest.mock import patch
        for options in (["--caller-drain"], ["--aot", "--caller-drain"],
                        ["--reuse-aot-bundle", "not-a-bundle", "--reuse-aot-build-sha256", "0" * 64]):
            with self.subTest(options=options), contextlib.redirect_stderr(io.StringIO()), \
                    patch.object(build.subprocess, "run") as process, self.assertRaises(SystemExit) as failure:
                build.main(["--data", "not-a-private-input", *options])
            self.assertEqual(2, failure.exception.code)
            process.assert_not_called()

    def test_js_only_aot_reuse_rejects_changed_compiled_csharp_before_copy_or_sdk(self):
        import build
        import json
        import shutil
        from unittest.mock import patch
        root, source, _, _ = self.camera_fixture()
        try:
            receipt = dict(execution=dict(mode="aot", runAOTCompilation=True, jsonReflectionEnabled=True),
                           profile=build.SELECTED_WORLD_PROFILE,
                           sources=[dict(path="Assets/Scripts/Gameplay/GameSimulation.cs", sha256="1" * 64)])
            raw = json.dumps(receipt).encode()
            (source / "build-info.json").write_bytes(raw)
            with patch.object(build.shutil, "copytree") as copy, self.assertRaisesRegex(ValueError, "cannot change C#"):
                build.reuse_aot_bundle(source, build.hashlib.sha256(raw).hexdigest(), root / "new-output",
                    [dict(path="Assets/Scripts/Gameplay/GameSimulation.cs", sha256="2" * 64)])
            copy.assert_not_called()
        finally:
            shutil.rmtree(root)

    def test_bundle_persist_default_remains_historical_and_new_profile_is_explicit(self):
        import inspect
        self.assertEqual(inspect.signature(prepare.Bundle.persist).parameters["profile"].default,
                         "original-teams-contact281-c2-m1")
        import prepare_presnap_switch
        self.assertEqual(prepare_presnap_switch.PROFILE,"original-teams-contact282-c2-m1-presnap-switch")
        self.assertEqual(len(prepare_presnap_switch.NEW_ENTRIES),4)

    def possession_fixture(self):
        import json
        root, source, _, _ = self.camera_fixture()
        manifest = json.loads((source / "manifest.json").read_bytes())
        manifest["entrypoints"]["normalCamera"] = "first.bytes"
        (source / "manifest.json").write_text(json.dumps(manifest))
        speech = root / "authored-speech.bytes"
        speech.write_bytes(b"authored synthetic operand input, not original numeric data")
        import prepare_possession
        return root, source, speech, prepare_possession

    def test_possession_extension_preserves_all_original_pins_and_profile(self):
        import json
        import shutil
        root, source, speech, module = self.possession_fixture()
        old_pin = module.SPEECH_SHA256
        try:
            module.SPEECH_SHA256 = prepare.digest(speech.read_bytes())
            original = (source / "manifest.json").read_bytes()
            before = {p.name: p.read_bytes() for p in source.iterdir()}
            receipt = module.prepare(source, prepare.digest(original), speech, root / "extended")
            extended = json.loads((root / "extended/manifest.json").read_bytes())
            self.assertTrue(receipt["originalPinsPreserved"])
            self.assertFalse(receipt["formatMigration"])
            self.assertEqual(receipt["fileCount"], 3)
            self.assertEqual(extended["entrypoints"]["possessionSound"], "camera/possession-speech.bytes")
            self.assertEqual(extended["profile"], json.loads(original)["profile"])
            self.assertEqual(json.loads(original)["files"], [pin for pin in extended["files"] if pin["path"] != "camera/possession-speech.bytes"])
            self.assertEqual(before, {p.name: p.read_bytes() for p in source.iterdir()})
        finally:
            module.SPEECH_SHA256 = old_pin
            shutil.rmtree(root)

    def test_possession_extension_rejects_bad_source_hash_operand_and_changed_inherited_file(self):
        import shutil
        root, source, speech, module = self.possession_fixture()
        old_pin = module.SPEECH_SHA256
        try:
            expected = prepare.digest((source / "manifest.json").read_bytes())
            with self.assertRaisesRegex(ValueError, "manifest changed"):
                module.prepare(source, "0" * 64, speech, root / "bad-manifest")
            with self.assertRaisesRegex(ValueError, "authored-speech.bytes"):
                module.prepare(source, expected, speech, root / "bad-operands")
            module.SPEECH_SHA256 = prepare.digest(speech.read_bytes())
            (source / "first.bytes").write_bytes(b"changed inherited synthetic asset")
            with self.assertRaisesRegex(ValueError, "first.bytes"):
                module.prepare(source, expected, speech, root / "bad-old-asset")
        finally:
            module.SPEECH_SHA256 = old_pin
            shutil.rmtree(root)

    def camera_fixture(self):
        import json
        import uuid
        root = prepare.PROJECT / ".squad/.scratch" / ("camera-data-fixture-" + uuid.uuid4().hex)
        source = root / "base"
        source.mkdir(parents=True)
        payloads = {"first.bytes": b"authored synthetic first input", "second.bytes": b"authored synthetic second input"}
        records = []
        for name, raw in payloads.items():
            (source / name).write_bytes(raw)
            records.append(dict(path=name, bytes=len(raw), sha256=prepare.digest(raw)))
        manifest = dict(schema="blitz-portable-source-player/v1", profile="original-teams-contact281-c2-m1",
                        entrypoints={"first": "first.bytes"}, files=records, origins={"synthetic": "not original data"})
        (source / "manifest.json").write_text(json.dumps(manifest))
        camera = root / "authored-camera.json"
        camera.write_bytes(b'{"synthetic":"not original numeric data"}')
        import prepare_camera
        return root, source, camera, prepare_camera

    def test_camera_extension_preserves_every_inherited_pin_without_format_migration(self):
        import json
        import shutil
        root, source, camera, module = self.camera_fixture()
        old_pin = module.CAMERA_SHA256
        try:
            module.CAMERA_SHA256 = prepare.digest(camera.read_bytes())
            original = (source / "manifest.json").read_bytes()
            before = {p.name: p.read_bytes() for p in source.iterdir()}
            receipt = module.prepare(source, prepare.digest(original), camera, root / "extended")
            extended = json.loads((root / "extended/manifest.json").read_bytes())
            self.assertEqual(extended["schema"], "blitz-portable-source-player/v1")
            self.assertFalse(receipt["formatMigration"])
            self.assertEqual(receipt["fileCount"], 3)
            self.assertEqual(json.loads(original)["files"], [pin for pin in extended["files"] if not pin["path"].startswith("camera/")])
            self.assertEqual(before, {p.name: p.read_bytes() for p in source.iterdir()})
        finally:
            module.CAMERA_SHA256 = old_pin
            shutil.rmtree(root)

    def test_camera_extension_rejects_wrong_manifest_camera_and_changed_old_asset(self):
        import shutil
        root, source, camera, module = self.camera_fixture()
        old_pin = module.CAMERA_SHA256
        try:
            expected = prepare.digest((source / "manifest.json").read_bytes())
            with self.assertRaisesRegex(ValueError, "manifest changed"):
                module.prepare(source, "0" * 64, camera, root / "bad-manifest")
            with self.assertRaisesRegex(ValueError, "authored-camera.json"):
                module.prepare(source, expected, camera, root / "bad-camera")
            module.CAMERA_SHA256 = prepare.digest(camera.read_bytes())
            (source / "first.bytes").write_bytes(b"changed authored fixture")
            with self.assertRaisesRegex(ValueError, "first.bytes"):
                module.prepare(source, expected, camera, root / "changed-old-asset")
        finally:
            module.CAMERA_SHA256 = old_pin
            shutil.rmtree(root)

    def test_actual_prepared_inputs_have_complete_verified_inventory(self):
        import os
        explicit = os.environ.get("SOURCE_PLAYER_TEST_OLD_DATA")
        root = Path(explicit) if explicit else Path(__file__).resolve().parent / "data"
        if not root.exists():
            if explicit:
                self.fail("The explicitly selected historical private data root is missing.")
            self.skipTest("Explicit ignored original data is not installed; no placeholder data is generated.")
        import json
        manifest = json.loads((root / "manifest.json").read_bytes())
        self.assertEqual(manifest["schema"], "blitz-portable-source-player/v1")
        self.assertEqual(manifest["profile"], "original-teams-contact281-c2-m1")
        self.assertEqual(manifest["sourceRevision"], prepare.SOURCE)
        self.assertGreater(len(manifest["files"]), 300)
        for record in manifest["files"]:
            data = prepare.read(root, record["path"], record["sha256"])
            self.assertEqual(len(data), record["bytes"], record["path"])
        for entrypoint in manifest["entrypoints"].values():
            self.assertIn(entrypoint, {record["path"] for record in manifest["files"]})

    def test_relative_explicit_input_roots_are_normalized_without_allowing_traversal(self):
        here = Path(__file__).resolve().parent
        actual = prepare.read(here / "../source-player", "match-settings.json",
                              prepare.digest((here / "match-settings.json").read_bytes()))
        self.assertEqual(actual, (here / "match-settings.json").read_bytes())
        for invalid in ("../match-settings.json", "/match-settings.json", "a//b", "a\\b", "a:b", "a/./b"):
            with self.assertRaises(ValueError, msg=invalid):
                prepare.portable(invalid)

    def test_real_source_artifact_mutation_is_rejected_by_name(self):
        here = Path(__file__).resolve().parent
        with self.assertRaisesRegex(ValueError, "match-settings.json"):
            prepare.read(here, "match-settings.json", "0" * 64)


GENERATED_ACCOUNTING_CHECK = r"""
import assert from 'node:assert/strict';
import { createHash, webcrypto } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import vm from 'node:vm';

const [bundle, proofFile, mutation] = process.argv.slice(2);
const proof = JSON.parse(await readFile(proofFile, 'utf8'));
const originalSource = await readFile(resolve(bundle, 'main.js'), 'utf8');
const unusedGuard = 'if (files.size !== 0) throw new Error(`The real source loaders left ${files.size} unconsumed private input files.`);';
assert(originalSource.includes(unusedGuard), 'The actual emitted exact-unused-input guard is required.');
const source = mutation === '--omit-unused-guard' ? originalSource.replace(unusedGuard, '') : originalSource;
const originalManifest = await readFile(resolve(bundle, 'data/manifest.json'));
const originalReceipt = JSON.parse(await readFile(resolve(bundle, 'build-info.json'), 'utf8'));
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const retained = proof.retainedInputs.map(pin => pin.path);
const unexpectedPath = 'private/unexpected-loader-input.bytes';
const unexpected = Buffer.from('Authored unknown input, not original data.');
const cases = ['valid', 'original-four-unconsumed', 'unknown-unconsumed',
  ...retained.map(path => 'corrupt:' + path), ...retained.map(path => 'missing:' + path)];
const results = [];
for (const variant of cases) {
  const manifest = JSON.parse(originalManifest);
  if (variant === 'unknown-unconsumed')
    manifest.files.push({ path: unexpectedPath, bytes: unexpected.length, sha256: hash(unexpected) });
  const manifestBytes = variant === 'unknown-unconsumed' ? Buffer.from(JSON.stringify(manifest)) : originalManifest;
  const receipt = { ...originalReceipt, dataManifestSha256: hash(manifestBytes) };
  const errors = [], reads = [], stations = [];
  let failure, cabinetStations, startCalls = 0, inputConstructed = 0, scheduled = 0;
  const canvas = { addEventListener() {}, focus() {} };
  const runtime = {
    withDiagnosticTracing() { return this; }, async create() { return this; },
    setModuleImports(name, imports) { assert.equal(name, 'source-player'); this.imports = imports; },
    getConfig() { return { mainAssemblyName: 'SourcePlayer.Browser' }; },
    async runMain() {},
    async getAssemblyExports() {
      return { Blitz: { SourcePlayer: { Browser: {
        Start(bytes) {
          startCalls++;
          assert.equal(hash(bytes), hash(manifestBytes));
          for (const path of proof.consumedPaths) {
            if (variant === 'original-four-unconsumed' && retained.includes(path)) continue;
            const raw = runtime.imports.readFile(path);
            const pin = manifest.files.find(pin => pin.path === path);
            assert.equal(raw.length, pin.bytes); assert.equal(hash(raw), pin.sha256);
            if (path === manifest.entrypoints.settings)
              cabinetStations = JSON.parse(new TextDecoder().decode(raw)).stations.map(station => station.id);
            reads.push(path);
          }
        },
        Frame() { throw new Error('Boundary regression must not fake gameplay frames.'); },
        CabinetStations() {
          assert(cabinetStations, 'Station export must use the actual verified settings consumed at the Browser boundary.');
          return Int32Array.from(cabinetStations);
        },
        Capture() {}, SetActive() {},
      } } } };
    },
  };
  class Renderer { constructor() { this.device = {}; } }
  class Input {
    constructor(api, target, ids) {
      assert(Array.isArray(ids), 'The real typed station export must be normalized before SourceInput flatMap.');
      inputConstructed++; stations.push(...ids);
    }
    focus() {} close() {}
  }
  const context = vm.createContext({
    URL, TextDecoder, Uint8Array, performance, structuredClone, crypto: webcrypto,
    console: { info() {} },
    document: { querySelector: selector => selector === '#game' ? canvas : {} },
    location: { href: 'http://127.0.0.1:8096/' },
    requestAnimationFrame() { scheduled++; return 1; }, cancelAnimationFrame() {},
    sourcePlayerLoader: {
      onFailure(callback) { failure = callback; }, progress() {},
      fail(error) { errors.push(String(error)); failure?.(); },
    },
    fetch: async url => {
      const path = new URL(url).pathname.slice(1);
      let bytes;
      if (path === 'build-info.json') bytes = Buffer.from(JSON.stringify(receipt));
      else if (path === 'data/manifest.json') bytes = manifestBytes;
      else if (path === 'data/' + unexpectedPath && variant === 'unknown-unconsumed') bytes = unexpected;
      else {
        if (variant.startsWith('missing:') && path === 'data/' + variant.slice(8))
          return { ok: false, status: 404 };
        bytes = Buffer.from(await readFile(resolve(bundle, path)));
        if (variant.startsWith('corrupt:') && path === 'data/' + variant.slice(8)) bytes[bytes.length >> 1] ^= 1;
      }
      return { ok: true, headers: { get() { return 'application/octet-stream'; } },
        async arrayBuffer() { return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength); } };
    },
  });
  const modules = new Map([
    ['./_framework/dotnet.js', { dotnet: runtime }],
    ['./renderer.js', { SourceRenderer: Renderer }],
    ['./input.js', { SourceInput: Input }],
  ]);
  const module = new vm.SourceTextModule(source, {
    context, identifier: pathToFileURL(resolve(bundle, 'main.js')).href,
    initializeImportMeta(meta) { meta.url = 'http://127.0.0.1:8096/main.js'; },
    async importModuleDynamically(specifier) {
      assert(modules.has(specifier), 'Only the actual main.js runtime imports are stubbed.');
      const values = modules.get(specifier);
      const imported = new vm.SyntheticModule(Object.keys(values), function () {
        for (const [key, value] of Object.entries(values)) this.setExport(key, value);
      }, { context });
      await imported.link(() => { throw new Error('Unexpected nested import'); });
      await imported.evaluate();
      return imported;
    },
  });
  await module.link(() => { throw new Error('Unexpected static import'); });
  await module.evaluate();
  const snapshot = context.sourcePlayer.snapshot();
  if (variant === 'valid') {
    assert.deepEqual(errors, []); assert.equal(startCalls, 1); assert.equal(inputConstructed, 1);
    assert.equal(scheduled, 1); assert.deepEqual(stations, [0]); assert.equal(snapshot.failed, false);
    assert.equal(reads.length, manifest.files.length);
  } else {
    assert.equal(snapshot.failed, true, 'The emitted unused-input guard must reject ' + variant);
    assert.equal(snapshot.ready, false);
    assert.equal(inputConstructed, 0); assert.equal(scheduled, 0);
    if (variant === 'original-four-unconsumed') {
      assert.match(errors.join('\n'), /The real source loaders left 4 unconsumed private input files\./);
      assert.deepEqual(manifest.files.map(pin => pin.path).filter(path => !reads.includes(path)).sort(), [...retained].sort());
    } else if (variant === 'unknown-unconsumed') {
      assert.match(errors.join('\n'), /The real source loaders left 1 unconsumed private input files\./);
      assert.deepEqual(manifest.files.map(pin => pin.path).filter(path => !reads.includes(path)), [unexpectedPath]);
    } else {
      assert.equal(startCalls, 0); assert(errors.some(error => error.includes(variant.split(':')[1])));
    }
  }
  results.push({ variant, status: 'passed', errors, startCalls, inputConstructed, scheduled, readCount: reads.length });
}
console.log(JSON.stringify({ schema: 'generated-source-player-accounting-regression/v1',
  mainJsSha256: hash(source), manifestSha256: hash(originalManifest), files: proof.files,
  active: proof.active, retained: proof.retained, cases: results,
  realBrowserStartupQualified: false }));
"""


class LoaderAccountingChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        import json
        import os
        import subprocess
        import uuid
        names = ("SOURCE_PLAYER_TEST_DATA", "SOURCE_PLAYER_TEST_CHECKS", "SOURCE_PLAYER_TEST_BUNDLE")
        values = [os.environ.get(name) for name in names]
        if not any(values):
            raise unittest.SkipTest("Explicit private563 data, compiled real Checks and actual build output are required.")
        if not all(values):
            raise AssertionError("All three SOURCE_PLAYER_TEST_DATA/CHECKS/BUNDLE paths are required.")
        cls.data, checks, cls.bundle = (Path(value).resolve() for value in values)
        cls.keep_output = bool(os.environ.get("SOURCE_PLAYER_TEST_OUTPUT"))
        cls.output = Path(os.environ.get("SOURCE_PLAYER_TEST_OUTPUT") or
                          prepare.PROJECT / ".squad/.scratch" / ("loader-accounting-" + uuid.uuid4().hex))
        cls.output.mkdir(parents=True, exist_ok=False)
        cls.manifest = json.loads((cls.data / "manifest.json").read_bytes())
        if cls.manifest["profile"] != "original-teams-contact282-c2-m1-presnap-switch":
            raise AssertionError("This regression requires the real closed successor profile.")
        cls.command = [os.environ.get("SOURCE_PLAYER_TEST_DOTNET", "dotnet"), str(checks), str(cls.data),
                       "--loader-contract-only", "--proof-output", str(cls.output / "managed")]
        result = subprocess.run(cls.command, capture_output=True, text=True, timeout=240)
        (cls.output / "managed.log").write_text(result.stdout + result.stderr)
        if result.returncode:
            raise AssertionError("Actual managed loader regression failed: " + result.stdout[-1500:] + result.stderr[-1500:])
        cls.proof_path = cls.output / "managed/loader-contract.json"
        cls.proof = json.loads(cls.proof_path.read_bytes())

    @classmethod
    def tearDownClass(cls):
        import shutil
        if not cls.keep_output:
            shutil.rmtree(cls.output)

    def test_actual_managed_loader_authenticates_retained_identities_not_just_manifest_hashes(self):
        expected = ("animation/export-inventory.json", "kernel/c2-callback-data.json",
                    "kernel/impact-presentation.json", "settings.json")
        proof = self.proof
        self.assertEqual((proof["files"], proof["active"], proof["retained"]), (563, 559, 4))
        self.assertEqual([pin["path"] for pin in proof["retainedInputs"]], list(expected))
        self.assertEqual(set(proof["consumedPaths"]), {pin["path"] for pin in self.manifest["files"]})
        self.assertEqual(len(proof["consumedPaths"]), 563)
        self.assertEqual(proof["factory"], "SourceC2Composition.CreatePreSnapSwitchSimulation")
        self.assertTrue(proof["enabled"])
        self.assertEqual((proof["stationLayout"], proof["stations"]), (4, 1))
        for path in expected:
            for control in ("substituted-name", "changed-manifest-hash", "changed-byte-count",
                            "missing-manifest-entry", "missing-bytes", "corrupt-bytes", "self-consistent-substitution"):
                self.assertIn("retained-" + control + ":" + path, [case["name"] for case in proof["controls"]])
        self.assertGreater(proof["worldDraws"], 0)
        self.assertGreaterEqual(proof["sourceTick"], 2)

    def test_actual_builder_output_and_original_js_guard_with_real_managed_read_ledger(self):
        import json
        import subprocess
        main = self.bundle / "main.js"
        self.assertEqual(main.read_bytes(), (prepare.HERE / "wwwroot/main.js").read_bytes())
        self.assertEqual((self.bundle / "data/manifest.json").read_bytes(), (self.data / "manifest.json").read_bytes())
        receipt = json.loads((self.bundle / "build-info.json").read_bytes())
        self.assertEqual(receipt["dataManifestSha256"], prepare.digest((self.data / "manifest.json").read_bytes()))
        self.assertEqual(receipt["factory"], self.proof["factory"])
        for pin in self.manifest["files"]:
            self.assertEqual(prepare.digest((self.bundle / "data" / pin["path"]).read_bytes()), pin["sha256"], pin["path"])
        result = subprocess.run(["node", "--experimental-vm-modules", "--input-type=module", "-",
                                 str(self.bundle), str(self.proof_path)], input=GENERATED_ACCOUNTING_CHECK,
                                capture_output=True, text=True, timeout=240)
        (self.output / "generated-js.log").write_text(result.stdout + result.stderr)
        self.assertEqual(result.returncode, 0, result.stdout[-1500:] + result.stderr[-1500:])
        proof = json.loads(result.stdout)
        self.assertEqual(proof["mainJsSha256"], prepare.digest(main.read_bytes()))
        self.assertEqual(len(proof["cases"]), 11)
        self.assertTrue(all(case["status"] == "passed" for case in proof["cases"]))
        (self.output / "generated-js.json").write_text(json.dumps(proof, indent=2) + "\n")
        mutated = subprocess.run(["node", "--experimental-vm-modules", "--input-type=module", "-",
                                  str(self.bundle), str(self.proof_path), "--omit-unused-guard"],
                                 input=GENERATED_ACCOUNTING_CHECK, capture_output=True, text=True, timeout=240)
        (self.output / "generated-js-omit-guard.log").write_text(mutated.stdout + mutated.stderr)
        self.assertEqual(mutated.returncode, 1, "Removing the actual emitted guard must kill this regression.")
        self.assertIn("unused-input guard must reject original-four-unconsumed", mutated.stderr)

    def test_actual_builder_validates_complete_real563_tree(self):
        import build
        build.verify_private_inputs(self.data, self.manifest)
        build.verify_private_inputs(self.bundle / "data", self.manifest)


if __name__ == "__main__":
    unittest.main()
