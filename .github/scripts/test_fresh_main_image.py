"""Asset-free tests for the exact merged-menu successor, never a gameplay approval."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest
from unittest import mock
from types import SimpleNamespace

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
spec = importlib.util.spec_from_file_location("merged_menu", HERE / "fresh_main_image.py")
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class FreshImageTests(unittest.TestCase):
    def values(self):
        return release.transfer_values({
            "TRANSFER_RELEASE_TAG": "container-transfer-20261116-" + "a" * 32,
            "TRANSFER_SECRET_NAME": "CONTAINER_TRANSFER_" + "A" * 32,
            "TRANSFER_CIPHER_SHA256": "b" * 64})

    def build(self):
        p = release.pins()
        return {"profile": "original-teams-complete-av306",
                "factory": "SourceC2Composition.CreateCompleteAv306Simulation",
                "dataManifestSha256": p["transfer"]["data_manifest_sha256"],
                "execution": copy.deepcopy(p["execution"]), "assemblies": copy.deepcopy(p["assemblies"]),
                "verifiedRuntimeResources": {"files": [{}] * 178}}

    def test_only_three_transport_inputs_can_vary_not_image_source_or_data(self):
        values = self.values()
        env = {"TRANSFER_" + k.upper(): v for k, v in values.items()}
        for field in ("image_id", "source_revision", "source_digest", "data_manifest_sha256", "source_tag"):
            altered = dict(env, **{"TRANSFER_" + field.upper(): "foreign"})
            self.assertEqual(values, release.transfer_values(altered))
        for field in ("release_tag", "secret_name", "cipher_sha256"):
            altered = dict(env, **{"TRANSFER_" + field.upper(): "bad"})
            with self.subTest(field=field), self.assertRaises(ValueError):
                release.transfer_values(altered)

    def test_fresh_core_native_and_exact_product_cannot_be_replaced_by_static_refresh(self):
        release.validate_build(self.build())
        for change in ("native", "core", "profile", "reuse", "caller", "runtime"):
            build = self.build()
            if change == "native":
                build["execution"]["nativeRuntimeSha256"] = "0" * 64
            elif change == "core":
                build["assemblies"][1]["sha256"] = "0" * 64
            elif change == "profile":
                build["profile"] += "-two-station-records-v1"
            elif change == "reuse":
                build["execution"]["compiledAotReuse"] = {}
            elif change == "caller":
                build["execution"]["callerProfile"] = "aot-rooted-bounded-caller-drain/v1"
            else:
                build["verifiedRuntimeResources"]["files"].pop()
            with self.subTest(change=change), self.assertRaises(ValueError):
                release.validate_build(build)

    def test_prepare_requires_real_owner_attestation_before_any_api_or_crypto(self):
        with mock.patch.object(release.frozen, "run") as run, self.assertRaisesRegex(ValueError, "Quality"):
            release.prepare(SimpleNamespace(qa_approved=False))
        run.assert_not_called()

    def test_new_artifact_cannot_borrow_old_certificate_or_prepare_while_quality_pending(self):
        candidate = copy.deepcopy(release.pins())
        candidate["independentArtifactQuality"] = {"reviewer": "Quality", "verdict": "PENDING"}
        with mock.patch.object(release, "pins", return_value=candidate), \
                mock.patch.object(release.frozen, "run") as run, \
                self.assertRaisesRegex(ValueError, "own independent Quality"):
            release.prepare(SimpleNamespace(qa_approved=True))
        run.assert_not_called()

    def test_existing_released_helper_and_browser_bytes_are_pinned_without_old_epoch_source_guard(self):
        rows = release.pins()["helperSources"]
        self.assertGreater(len(rows), 10)
        for row in rows:
            self.assertEqual(row["sha256"], release.frozen.sha(ROOT / row["path"]), row["path"])

    def test_real_compiled_inventory_rejects_a_changed_camera_source_after_compilation(self):
        sys.path.insert(0, str(ROOT / "tools/source-player"))
        spec = importlib.util.spec_from_file_location("actual_fresh_builder", ROOT / "tools/source-player/build.py")
        builder = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(builder)
        build = {"sources": builder.source_inventory(ROOT),
                 "sourceRootDigest": release.pins()["transfer"]["source_digest"]}
        head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
        original_sha = release.frozen.sha
        with mock.patch.dict(os.environ, GITHUB_SHA=head):
            release.source_guard(build)
            with mock.patch.object(release.frozen, "sha", side_effect=lambda path:
                    "0" * 64 if str(path).endswith("/SourceNormalCameraModel.cs") else original_sha(path)), \
                    self.assertRaisesRegex(ValueError, "SourceNormalCameraModel.cs"):
                release.source_guard(build)

    def test_latest_allows_only_exact_prior_or_same_verified_candidate(self):
        p = release.pins()
        prior = {"digest": p["priorManifest"], "config": p["priorConfig"]}
        candidate = {"digest": "sha256:" + "a" * 64, "config": p["transfer"]["image_id"]}
        for value in (prior, candidate):
            with mock.patch.object(release, "manifest", return_value=value):
                self.assertEqual(value, release.latest_guard(candidate))
        for value in (dict(prior, config="foreign"), dict(prior, digest="foreign")):
            with mock.patch.object(release, "manifest", return_value=value), self.assertRaises(ValueError):
                release.latest_guard(candidate)
        with mock.patch.object(release, "manifest", return_value=candidate), self.assertRaises(ValueError):
            release.latest_guard()

    def test_immutable_requires_exact_config_and_all_twelve_layers(self):
        good = {"config": self.values()["image_id"], "layers": [{}] * 12}
        with mock.patch.object(release, "manifest", return_value=good):
            self.assertEqual(good, release.candidate_manifest("latest"))
        for bad in (dict(good, config="foreign"), dict(good, layers=[{}] * 14)):
            with mock.patch.object(release, "manifest", return_value=bad), self.assertRaises(ValueError):
                release.candidate_manifest("latest")

    def test_race_and_permission_rejection_never_uses_an_alternate_writer(self):
        candidate = {"digest": "sha256:" + "a" * 64, "config": self.values()["image_id"]}
        prior = {"digest": "old"}
        with mock.patch.object(release.cache.base, "public_package"), \
                mock.patch.object(release.cache.base, "registry_token", return_value="synthetic"), \
                mock.patch.object(release, "candidate_manifest", return_value=candidate), \
                mock.patch.object(release, "latest_guard", side_effect=[prior, candidate]), \
                mock.patch.object(release.cache, "alias_put") as put, self.assertRaisesRegex(ValueError, "raced"):
            release.promote(candidate, "synthetic", ROOT)
        put.assert_not_called()
        with mock.patch.object(release.cache.base, "public_package"), \
                mock.patch.object(release.cache.base, "registry_token", return_value="synthetic"), \
                mock.patch.object(release, "candidate_manifest", return_value=candidate), \
                mock.patch.object(release, "latest_guard", return_value=prior), \
                mock.patch.object(release.cache, "alias_put", side_effect=ValueError("denied")) as put, \
                self.assertRaisesRegex(ValueError, "denied"):
            release.promote(candidate, "synthetic", ROOT)
        self.assertEqual(1, put.call_count)

    def test_failure_of_normal_runtime_blocks_all_registry_login_push_and_latest_promotion(self):
        release_record = {"tag_name": self.values()["release_tag"], "prerelease": True,
                          "draft": False, "assets": [{"name": release.frozen.ASSET}]}
        docker = mock.Mock()
        with mock.patch.object(release.cache.base, "public_package"), \
                mock.patch.object(release, "latest_guard"), \
                mock.patch.object(release.frozen, "api", return_value=release_record), \
                mock.patch.object(release.frozen, "run"), \
                mock.patch.object(release.frozen, "receive_and_load"), \
                mock.patch.object(release, "verify_image", return_value=(ROOT, ROOT / "inventory")), \
                mock.patch.object(release, "runtime", side_effect=ValueError("normal runtime failed")), \
                mock.patch.object(release, "promote") as promote, self.assertRaisesRegex(ValueError, "normal runtime"):
            release.publish(ROOT, docker, "synthetic", "synthetic", self.values())
        docker.assert_not_called()
        promote.assert_not_called()

    def test_successful_publication_orders_exact_payload_and_normal_browser_before_any_push(self):
        events = []
        release_record = {"tag_name": self.values()["release_tag"], "prerelease": True,
                          "draft": False, "assets": [{"name": release.frozen.ASSET}]}
        candidate = {"digest": "sha256:" + "a" * 64}
        def verify(*args):
            events.append("all1219")
            return ROOT, ROOT / "inventory"
        def runtime(*args):
            events.append("normal-browser")
            return {"ordinaryUid1000NoTmpfsPrefixHealthFullHttp": True}
        def docker(command, **kwargs):
            events.append(command[0])
            return b""
        with mock.patch.object(release.cache.base, "public_package"), \
                mock.patch.object(release, "latest_guard"), \
                mock.patch.object(release.frozen, "api", return_value=release_record), \
                mock.patch.object(release.frozen, "run"), \
                mock.patch.object(release.frozen, "receive_and_load"), \
                mock.patch.object(release, "verify_image", side_effect=verify), \
                mock.patch.object(release, "runtime", side_effect=runtime), \
                mock.patch.object(release, "manifest", return_value=None), \
                mock.patch.object(release, "candidate_manifest", return_value=candidate), \
                mock.patch.object(release, "promote", side_effect=lambda *args: events.append("latest")):
            result = release.publish(ROOT, docker, "synthetic", "synthetic", self.values())
        self.assertTrue(result["published"])
        self.assertLess(events.index("all1219"), events.index("normal-browser"))
        self.assertLess(events.index("normal-browser"), events.index("login"))
        self.assertLess(events.index("push"), events.index("latest"))

    def test_ciphertext_release_inventory_rejects_plaintext_before_download(self):
        with mock.patch.object(release.cache.base, "public_package"), \
                mock.patch.object(release, "latest_guard"), \
                mock.patch.object(release.frozen, "api", return_value={
                    "tag_name": self.values()["release_tag"], "prerelease": True, "draft": False,
                    "assets": [{"name": "game.tar.gz"}]}), \
                mock.patch.object(release.frozen, "run") as run, self.assertRaisesRegex(ValueError, "ciphertext-only"):
            release.publish(ROOT, mock.Mock(), "synthetic", "synthetic", self.values())
        run.assert_not_called()

    def test_normal_container_rejects_host_filesystem_workaround_before_browser_or_http(self):
        actual = {"State": {"Health": {"Status": "healthy"}}, "Image": self.values()["image_id"],
                  "Config": {"User": "1000:1000"}, "HostConfig": {
                      "ReadonlyRootfs": False, "Privileged": False, "Tmpfs": {"/tmp": "rw"}}}
        docker = mock.Mock(side_effect=[b"id", b"", json.dumps([actual]).encode(), b""])
        with mock.patch.dict(os.environ, GITHUB_RUN_ID="123", GITHUB_RUN_ATTEMPT="1"), \
                mock.patch.object(release.frozen, "run") as run, self.assertRaisesRegex(ValueError, "mounts"):
            release.runtime(docker, ROOT, self.values()["source_tag"], ROOT / "inventory")
        run.assert_not_called()
        self.assertEqual(["rm", "--force", "merged-menu-123-1"], docker.call_args.args[0])

    def test_anonymous_cold_pull_has_no_authenticated_fallback(self):
        candidate = {"digest": "sha256:" + "a" * 64}
        docker = mock.Mock(return_value=b"cached-image")
        with mock.patch.dict(os.environ, FRESH_EXPECTED_MANIFEST=candidate["digest"]), \
                mock.patch.object(release, "candidate_manifest", return_value=candidate), \
                self.assertRaisesRegex(ValueError, "Fresh anonymous"):
            release.anonymous(ROOT, docker)
        docker.assert_called_once_with(["image", "ls", "--quiet", release.IMAGE])

    def test_workflow_pr_is_asset_free_and_dispatch_is_main_only_with_read_only_anonymous_job(self):
        text = (ROOT / ".github/workflows/fresh-main-image.yml").read_text()
        self.assertEqual(2, text.count("github.event_name == 'workflow_dispatch'"))
        self.assertEqual(2, text.count("github.ref == 'refs/heads/main'"))
        self.assertEqual(3, text.count("persist-credentials: false\n          fetch-depth: 0"),
                         "Every source-identity caller, including asset-free PR validation, needs compiled89 history.")
        anonymous = text.split("  independent-anonymous-consumer:", 1)[1]
        self.assertNotIn("packages: write", anonymous)
        self.assertNotIn("GH_TOKEN:", anonymous)
        self.assertNotIn("REGISTRY_TRANSFER_KEY:", anonymous)
        self.assertNotIn("upload-artifact", text)
        self.assertNotIn("--no-sandbox", (HERE / "fresh_main_image.browser.mjs").read_text())

    def choices(self):
        return {
            "schema": "original-record-choice-browser/v1", "status": "passed", "errors": [],
            "actualCommandLine": {"arguments": ["chrome", "--headless=new"]},
            "input": [{"name": key, "down": True} for key in ("Enter", "NumpadEnter")],
            "runs": [{"label": label,
                      "frames": [{"textures": ["hud/original/" + prefix + f"{index:02d}"
                                              for index in range(1, 12)]}],
                      "state": {"state": {"records": {"stations": [{"step": result}]}}}}
                     for label, prefix, result in (("after", "nglwl", "Selected"),
                                                   ("after-yes", "yglwl", "Name"))]}

    def test_choice_certificate_rejects_missing_frame_input_wrong_result_baseline_or_unsafe_chrome(self):
        release.validate_choices(self.choices())
        for mutation in ("frame", "input", "result", "baseline", "sandbox", "autoplay", "errors"):
            proof = self.choices()
            if mutation == "frame":
                proof["runs"][1]["frames"][0]["textures"].pop()
            elif mutation == "input":
                proof["input"].pop()
            elif mutation == "result":
                proof["runs"][0]["state"]["state"]["records"]["stations"][0]["step"] = "Name"
            elif mutation == "baseline":
                proof["runs"][0]["label"] = "before"
            elif mutation == "sandbox":
                proof["actualCommandLine"]["arguments"].append("--no-sandbox")
            elif mutation == "autoplay":
                proof["actualCommandLine"]["arguments"].append("--autoplay-policy=no-user-gesture-required")
            else:
                proof["errors"].append("actual player failure")
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                release.validate_choices(proof)

    def test_old_270_artifact_remains_historical_and_cannot_pass_successor_build_admission(self):
        historical = json.loads((HERE / "fresh_main_image_pins.json").read_bytes())
        self.assertNotEqual(historical["transfer"]["image_id"], release.pins()["transfer"]["image_id"])
        self.assertEqual(historical["transfer"]["image_id"], release.pins()["priorConfig"])
        for field in ("execution", "assemblies"):
            build = self.build()
            build[field] = historical[field]
            with self.subTest(field=field), self.assertRaises(ValueError):
                release.validate_build(build)

    def test_real_browser_after_only_keeps_both_choices_and_all_source_frame_assertions(self):
        caller = (ROOT / "tools/source-player/records-choice.browser.mjs").read_text()
        self.assertIn("const variants = afterOnly ? [] :", caller)
        self.assertIn("['after', after, false], ['after-yes', after, true]", caller)
        self.assertIn("for (let i = 1; i <= 11; i++)", caller)
        self.assertIn("await tap(answer ? 'NumpadEnter' : 'Enter')", caller)
        self.assertNotIn("Input.dispatchKeyEvent", (HERE / "fresh_main_image.py").read_text())


if __name__ == "__main__":
    unittest.main()
