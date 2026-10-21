import importlib.util
from pathlib import Path
import unittest


SPEC = importlib.util.spec_from_file_location("source_player_prepare", Path(__file__).with_name("prepare.py"))
prepare = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(prepare)


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
            with patch.object(build.subprocess,"run",side_effect=AssertionError("No publish may run for mismatched data")):
                with self.assertRaises(SystemExit) as failure:build.main(["--data",str(root)])
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
  let failure, startCalls = 0, inputConstructed = 0, scheduled = 0;
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
            reads.push(path);
          }
        },
        Frame() { throw new Error('Boundary regression must not fake gameplay frames.'); },
        Capture() {}, SetActive() {},
      } } } };
    },
  };
  class Renderer { constructor() { this.device = {}; } }
  class Input {
    constructor(api, target, ids) { inputConstructed++; stations.push(...ids); }
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
