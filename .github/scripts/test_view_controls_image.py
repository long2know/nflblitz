"""Closed UI publisher tests: real released source bytes, no original assets, SDK or registry access."""
import copy
import importlib.util
import io
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import sys
import tarfile
import unittest
from unittest import mock

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
spec = importlib.util.spec_from_file_location("view_publisher", HERE / "view_controls_image.py")
ui = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ui)


class TypedUiPublisherTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        sys.path.insert(0, str(ROOT / "tools/source-player"))
        spec = importlib.util.spec_from_file_location("actual_ui_build", ROOT / "tools/source-player/build.py")
        build = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(build)
        cls.sources = build.source_inventory(ROOT)
        if ui.cache.refresh.sha(json.dumps(cls.sources, sort_keys=True).encode()) != ui.UI_SOURCE_DIGEST:
            raise AssertionError("Tests must run on the actual assembled released UI source, never the old695 tree.")

    def build_info(self):
        objects = [{"assembly": "SourcePlayer.Core", "sha256":
                    "fcc6bc23172dcafcb8ad369b8aa4ee825d9420cca3c9e231ac6bb743f1c46ad8"}]
        return {"schema": "blitz-source-player-build/v1", "sourceRootDigest": ui.UI_SOURCE_DIGEST,
                "sources": copy.deepcopy(self.sources), "execution": {
                    "applicationObjects": objects, "compiledAotReuse": {
                        "schema": "unchanged-core-wasm-loader-static-refresh/v1",
                        "originalBuildInfoSha256": ui.cache.refresh.BASE_BUILD_SHA,
                        "originalCompiledSourceDigest": ui.cache.refresh.BASE_SOURCE_DIGEST,
                        "originalCompiledHead": "6ad57cbd7969f3e0fe2473cebe6d076f6d0c7189",
                        "staticSourceRevision": "a" * 40, "staticSourceUncommittedCandidate": False,
                        "changedStaticSources": ui.STATIC_PATHS.copy(),
                        "projectChange": "None; all C#/compile/trimmer/project bytes identical",
                        "applicationObjects": copy.deepcopy(objects),
                        "nativeRuntimeSha256": ui.cache.refresh.BASE_NATIVE_SHA}},
                "verifiedAssetCache": {"schema": "persistent-verified-content-cache/v1",
                    "key": "normalized-application-base-and-sha256", "verifyCacheHitsAndDownloads": True,
                    "fallback": "explicit-warning-and-verified-network-only"},
                "verifiedRuntimeResources": {"schema": "source-verified-runtime-resources/v1", "files": [{}] * 48}}

    def test_actual_ui_declaration_and_order_are_the_optin_without_an_invented_top_field(self):
        info = self.build_info()
        ui.validate_build(info, "a" * 40)
        self.assertNotIn("viewControls", info)
        for mutation in ("missing-view", "order", "uncommitted", "native", "hit-integrity", "runtime-pin"):
            altered = copy.deepcopy(info)
            if mutation == "missing-view":
                altered["sources"] = [x for x in altered["sources"] if not x["path"].endswith("/view-controls.js")]
            elif mutation == "order":
                altered["execution"]["compiledAotReuse"]["changedStaticSources"].reverse()
            elif mutation == "uncommitted":
                altered["execution"]["compiledAotReuse"]["staticSourceUncommittedCandidate"] = True
            elif mutation == "native":
                altered["execution"]["compiledAotReuse"]["nativeRuntimeSha256"] = "0" * 64
            elif mutation == "hit-integrity":
                altered["verifiedAssetCache"]["verifyCacheHitsAndDownloads"] = False
            else:
                altered["verifiedRuntimeResources"]["files"].pop()
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                ui.validate_build(altered, "a" * 40)

    def test_publication_requires_actual_revision_and_exact_static_bytes(self):
        info = self.build_info()
        static = {name: (ROOT / "tools/source-player/wwwroot" / name).read_bytes() for name in ui.STATIC_NAMES}
        static["build-info.json"] = ui.cache.refresh.encoded(info)
        ui.validate_build(info, "a" * 40, static)
        with self.assertRaises(ValueError):
            ui.validate_build(info, None, static)
        for mutation in ("corrupt-view", "extra-file"):
            altered = dict(static)
            if mutation == "corrupt-view":
                altered["view-controls.js"] += b"\n// changed after UI gate"
            else:
                altered["private-data.bin"] = b"not admitted"
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                ui.validate_build(info, "a" * 40, altered)

    def test_production_refresh_caller_passes_released_true_keyword_and_requires1219(self):
        info = self.build_info()
        static = {name: (ROOT / "tools/source-player/wwwroot" / name).read_bytes() for name in ui.STATIC_NAMES}
        static["build-info.json"] = ui.cache.refresh.encoded(info)
        inventory = {"files": [{}] * 1219, "bundleFiles": 1219, "sourceBaseCommit": "a" * 40,
                     "compiledSourceDigest": ui.UI_SOURCE_DIGEST,
                     "buildInfoSha256": ui.cache.refresh.sha(static["build-info.json"]),
                     "actualCompiledSourceDigest": ui.cache.refresh.BASE_SOURCE_DIGEST,
                     "actualCompiledHead": "6ad57cbd7969f3e0fe2473cebe6d076f6d0c7189"}
        with mock.patch.object(ui.cache.refresh, "refresh", return_value=(info, static, inventory)) as method:
            self.assertEqual(ui.refresh_ui(b"original-bbec", b"original-b2f69", "a" * 40), (info, static, inventory))
            method.assert_called_once_with(b"original-bbec", b"original-b2f69", ui.ROOT, "a" * 40, view_controls=True)
        inventory["files"].pop()
        with mock.patch.object(ui.cache.refresh, "refresh", return_value=(info, static, inventory)), \
                self.assertRaisesRegex(ValueError, "exact1219"):
            ui.refresh_ui(b"original-bbec", b"original-b2f69", "a" * 40)

    def test_missing_old_original_or_foreign_latest_cannot_replace_the_delivered_cache(self):
        prior = {"digest": ui.PARENT_DIGEST, "config": ui.PARENT_CONFIG}
        candidate = {"digest": "sha256:" + "a" * 64, "config": "sha256:" + "b" * 64}
        for value in (prior, candidate):
            with mock.patch.object(ui.cache, "manifest", return_value=value):
                self.assertEqual(ui.latest_guard(candidate), value)
        for value in (None, {"digest": ui.cache.base.DIGEST, "config": ui.cache.base.CONFIG},
                      dict(prior, config="sha256:" + "f" * 64),
                      {"digest": "sha256:" + "f" * 64, "config": ui.PARENT_CONFIG}):
            with self.subTest(value=value), mock.patch.object(ui.cache, "manifest", return_value=value), \
                    self.assertRaisesRegex(ValueError, "missing/foreign"):
                ui.latest_guard(candidate)
        with mock.patch.object(ui.cache, "manifest", return_value=candidate), self.assertRaises(ValueError):
            ui.latest_guard()

    def test_actual_cache_guard_rejects_startup_only_or_a_warm_network_regression(self):
        proof = {"schema": "persistent-verified-cache-actual-browser/v1", "status": "passed", "normalSandbox": True,
                 "stages": [{"assetBodyBytes": 300324289}, {"assetRequests": 0, "assetBodyBytes": 0,
                     "binaryRequests": 0, "cache": {"downloadedBytes": 0}, "buildInfoRequests": 1,
                     "manifestRequests": 1, "allWireBytes": 920000}]}
        ui.validate_cache_browser(proof)
        with self.assertRaisesRegex(ValueError, "Startup diagnostics"):
            ui.validate_cache_browser({"schema": "normal-sandbox-chrome-profile-startup-only/v1",
                                       "status": "startup-probe-passed", "normalSandbox": True, "stages": []})
        proof["stages"][1]["assetRequests"] = 1
        with self.assertRaisesRegex(ValueError, "warm-cache"):
            ui.validate_cache_browser(proof)

    def test_alias_race_or_permission_error_stops_before_a_fallback_writer(self):
        prior = {"digest": ui.PARENT_DIGEST, "config": ui.PARENT_CONFIG}
        candidate = {"digest": "sha256:" + "a" * 64, "config": "sha256:" + "b" * 64}
        with mock.patch.object(ui.cache.base, "public_package"), \
                mock.patch.object(ui.cache.base, "registry_token", return_value="not-a-secret"), \
                mock.patch.object(ui.cache, "candidate_manifest", return_value=candidate), \
                mock.patch.object(ui, "latest_guard", side_effect=[prior, candidate]), \
                mock.patch.object(ui.cache, "alias_put") as put, self.assertRaisesRegex(ValueError, "raced"):
            ui.promote(candidate, "not-a-secret", ROOT)
        put.assert_not_called()
        for status in (401, 403, 409, 412):
            with self.subTest(status=status), mock.patch.object(ui.cache.base, "public_package"), \
                    mock.patch.object(ui.cache.base, "registry_token", return_value="not-a-secret"), \
                    mock.patch.object(ui.cache, "candidate_manifest", return_value=candidate), \
                    mock.patch.object(ui, "latest_guard", return_value=prior), \
                    mock.patch.object(ui.cache, "alias_put", side_effect=ui.HTTPError(
                        "https://ghcr.io/", status, "denied", {}, None)) as put, \
                    self.assertRaisesRegex(ValueError, "no bypass"):
                ui.promote(candidate, "not-a-secret", ROOT)
            self.assertEqual(put.call_count, 1)

    def test_transport_fallback_keeps_the_same_candidate_and_prior_conditional_header(self):
        prior = {"digest": ui.PARENT_DIGEST, "config": ui.PARENT_CONFIG}
        candidate = {"digest": "sha256:" + "a" * 64, "config": "sha256:" + "b" * 64}
        with mock.patch.object(ui.cache.base, "public_package"), \
                mock.patch.object(ui.cache.base, "registry_token", return_value="not-a-secret"), \
                mock.patch.object(ui.cache, "candidate_manifest", return_value=candidate), \
                mock.patch.object(ui, "latest_guard", return_value=prior), \
                mock.patch.object(ui.cache, "alias_put", side_effect=[ui.URLError("transport"), None]) as put:
            self.assertEqual(ui.promote(candidate, "not-a-secret", ROOT), "identical-ui-manifest-curl-fallback")
        self.assertEqual(put.call_args_list[0].args, put.call_args_list[1].args)
        self.assertTrue(put.call_args_list[1].kwargs["curl"])

    def test_ui_runtime_production_caller_keeps_uid1000_prefix_and_container_alive_for_browser_then_cleans(self):
        commands = []
        healthy = {"State": {"Running": True, "Health": {"Status": "healthy"}},
                   "Config": {"User": "1000:1000"},
                   "HostConfig": {"ReadonlyRootfs": False, "Privileged": False, "Tmpfs": None, "SecurityOpt": None}}

        def docker(command):
            commands.append(command)
            return json.dumps([healthy]).encode() if command[0] == "inspect" else b"owned"

        def browser_call(url, work):
            self.assertEqual(commands[-1][0], "inspect", "The live owned image must still be running for UI acceptance.")
            self.assertFalse(any(command[0] == "rm" for command in commands))
            return {"accepted": True}

        with mock.patch.dict(os.environ, {"GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "1"}), \
                mock.patch.object(ui, "ui_browser", side_effect=browser_call) as browser:
            self.assertEqual(ui.actual_ui_runtime(docker, "exact-ui", ROOT), {"accepted": True})
        create = commands[0]
        self.assertEqual(create[create.index("--user") + 1], "1000:1000")
        self.assertIn("BASE_URL=/nflblitz", create)
        self.assertNotIn("--tmpfs", create)
        self.assertEqual(commands[-1], ["rm", "--force", "view-image-123-1"])
        browser.assert_called_once_with("http://127.0.0.1:8920/nflblitz/", ROOT)

    def test_actual_publish_caller_cannot_upload_or_promote_without_ui_runtime_acceptance(self):
        root = ROOT / (".ui-publish-caller-" + secrets.token_hex(5))
        root.mkdir()
        self.addCleanup(shutil.rmtree, root)
        info = self.build_info()
        static = {name: (ROOT / "tools/source-player/wwwroot" / name).read_bytes() for name in ui.STATIC_NAMES}
        static["build-info.json"] = ui.cache.refresh.encoded(info)
        inventory = {"files": [{}] * 1219}
        archive = io.BytesIO()
        with tarfile.open(fileobj=archive, mode="w") as stream:
            for name, raw in (("config.json", b"{}"), ("manifest.json", b'[{"Config":"config.json"}]')):
                member = tarfile.TarInfo(name)
                member.size = len(raw)
                stream.addfile(member, io.BytesIO(raw))
        commands = []

        def docker(command, **_kwargs):
            commands.append(command)
            return archive.getvalue() if command[:2] == ["image", "save"] else b""

        def original_proof(_pins, _docker, work):
            (work / "actual-payload").mkdir()
            (work / "actual-payload/build-info.json").write_bytes(b"original-bbec")
            (work / "bundle-inventory.json").write_bytes(b"original-b2f69")

        cache_proof = {"schema": "persistent-verified-cache-actual-browser/v1", "status": "passed",
                       "normalSandbox": True, "actualBrowserReceiptSha256": "c" * 64,
                       "stages": [{"assetBodyBytes": 300324289}, {"assetRequests": 0, "assetBodyBytes": 0,
                           "binaryRequests": 0, "cache": {"downloadedBytes": 0}, "buildInfoRequests": 1,
                           "manifestRequests": 1, "allWireBytes": 920000}]}
        with mock.patch.dict(os.environ, {"GITHUB_SHA": "a" * 40}), \
                mock.patch.object(ui.cache.base, "public_package"), mock.patch.object(ui, "latest_guard"), \
                mock.patch.object(ui, "verify_prior", return_value={"execution": info["execution"]}), \
                mock.patch.object(ui.cache.base, "pins", return_value={"sourceTag": "original:qualified"}), \
                mock.patch.object(ui.cache.base, "verify_full_payload", side_effect=original_proof), \
                mock.patch.object(ui, "refresh_ui", return_value=(info, static, inventory)) as refresh, \
                mock.patch.object(ui.cache, "extract_new_site", return_value=(root / "site", root / "inventory")), \
                mock.patch.object(ui, "verify_image"), \
                mock.patch.object(ui.cache, "actual_user_cache", return_value=cache_proof), \
                mock.patch.object(ui, "actual_ui_runtime", return_value={"accepted": False}) as browser, \
                mock.patch.object(ui, "promote") as promote, self.assertRaisesRegex(ValueError, "UI candidate"):
            ui.publish(root, docker, "not-a-secret")
        browser.assert_called_once()
        refresh.assert_called_once_with(b"original-bbec", b"original-b2f69", "a" * 40)
        promote.assert_not_called()
        self.assertFalse(any(command[0] in ("push", "login") for command in commands))

    def test_physical_compiled_source_or_released_index_change_stops_before_image_work(self):
        root = ROOT / (".ui-publisher-source-test-" + secrets.token_hex(5))
        root.mkdir()
        self.addCleanup(shutil.rmtree, root)
        (root / "Core.cs").write_bytes(b"changed")
        with mock.patch.object(ui, "ROOT", root), mock.patch.object(ui, "SOURCE_PINS", {}), \
                mock.patch.dict(os.environ, {"GITHUB_SHA": "a" * 40}), \
                mock.patch.object(ui.subprocess, "check_output", return_value="a" * 40 + "\n"), \
                mock.patch.object(ui.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)), \
                mock.patch.object(ui.cache.base, "pins", return_value={"requiredRemoteMainFiles": [
                    {"path": "Core.cs", "sha256": ui.cache.refresh.sha(b"old")}]}), \
                self.assertRaisesRegex(ValueError, "compiled/input/records"):
            ui.source_guard()
        (root / "index.html").write_bytes(b"unreviewed")
        with mock.patch.object(ui, "ROOT", root), \
                mock.patch.object(ui, "SOURCE_PINS", {"index.html": ui.MODULES["index.html"]}), \
                mock.patch.dict(os.environ, {"GITHUB_SHA": "a" * 40}), \
                mock.patch.object(ui.subprocess, "check_output", return_value="a" * 40 + "\n"), \
                mock.patch.object(ui.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)), \
                mock.patch.object(ui.cache.base, "pins", return_value={"requiredRemoteMainFiles": []}), \
                self.assertRaisesRegex(ValueError, "Released UI/cache"):
            ui.source_guard()

    @unittest.skipUnless(os.environ.get("GITHUB_ACTIONS") == "true",
                         "Only the hosted CI launcher; no local Docker/cgroup retry or original game asset load.")
    def test_actual_normal_sandbox_ui_publisher_launcher_uses_owned_relative_tmp_and_cleans(self):
        root = ROOT / (".ui-launcher-probe-" + secrets.token_hex(5))
        root.mkdir(mode=0o700)
        self.addCleanup(shutil.rmtree, root)
        proof = ui.ui_browser("http://127.0.0.1:8920/nflblitz/", root, startup_only=True)
        self.assertTrue(proof["normalSandbox"])
        self.assertTrue(proof["startupOnly"])
        self.assertFalse(proof["cacheAcceptance"])
        self.assertFalse((root / "ui-profile").exists())
        self.assertFalse(os.path.isabs(proof["temporaryDirectory"]))
        print(json.dumps({**proof, "schema": "actual-ui-publisher-normal-chrome-launcher/v1",
                          "ownedProfileRemoved": True, "gameAssets": 0}, sort_keys=True), flush=True)


if __name__ == "__main__":
    unittest.main()
