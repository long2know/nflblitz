"""Asset-free checks for the fixed new private target; no real API, registry or Docker operations."""
import contextlib
import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import unittest
from unittest import mock
from urllib.error import HTTPError

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("private_recovery", HERE / "private_target_recovery.py")
recovery = importlib.util.module_from_spec(spec)
spec.loader.exec_module(recovery)


class PrivateTargetRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.directory = HERE / (".private-target-tests-" + secrets.token_hex(8))
        self.directory.mkdir(mode=0o700)
        self.addCleanup(shutil.rmtree, self.directory)
        self.pins = recovery.load_pins()
        self.private = {"name": "nflblitz-private", "package_type": "container", "visibility": "private",
                        "owner": {"login": "long2know", "type": "User"},
                        "repository": {"full_name": "long2know/nflblitz"}}
        self.env = {"GITHUB_ACTIONS": "true", "GITHUB_EVENT_NAME": "workflow_dispatch",
                    "GITHUB_REPOSITORY": "long2know/nflblitz", "GITHUB_REF": "refs/heads/main",
                    "GITHUB_SHA": "a" * 40, "GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "1",
                    "GH_TOKEN": "synthetic-job-token"}

    def execute(self, *, packages=None, existing=None, bad_payload=False, action="recover", expected=None,
                manifest_values=None, source_digest=None):
        commands = []
        packages = copy.deepcopy(packages if packages is not None else [self.private])

        def metadata(_token):
            return packages.pop(0) if len(packages) > 1 else packages[0]

        def run(argv, **kwargs):
            commands.append(argv)
            self.assertEqual("docker", argv[0])
            if argv[1] == "build":
                context = Path(argv[-1])
                self.assertEqual(["Dockerfile"], [p.name for p in context.iterdir()])
                self.assertEqual(
                    'FROM scratch\nLABEL org.opencontainers.image.source="https://github.com/long2know/nflblitz" '
                    'io.nflblitz.bootstrap-only="no-original-assets-nonexecutable"\nUSER 101:101\n',
                    (context / "Dockerfile").read_text())
            return b""

        def verified(*_args):
            if bad_payload:
                raise ValueError("actual accepted config/layer/payload differs")

        manifest = mock.Mock(side_effect=manifest_values or ["sha256:" + "d" * 64, None, "sha256:" + "d" * 64])
        with mock.patch.dict(os.environ, self.env, clear=True), \
                mock.patch.object(recovery, "verify_main"), \
                mock.patch.object(recovery, "package_metadata", side_effect=metadata), \
                mock.patch.object(recovery, "anonymous_denied"), \
                mock.patch.object(recovery, "check_no_other_image", side_effect=existing or [{}, {}]), \
                mock.patch.object(recovery, "verify_loaded", side_effect=verified), \
                mock.patch.object(recovery, "verify_existing_images") as existing_verifier, \
                mock.patch.object(recovery, "target_manifest", manifest), \
                mock.patch.object(recovery, "build_opener", side_effect=AssertionError("Unexpected real network in asset-free tests")), \
                mock.patch.object(recovery.frozen, "remote_image",
                                  return_value=source_digest or self.pins["sourceManifestDigest"]), \
                mock.patch.object(recovery.frozen, "run", side_effect=run), \
                mock.patch.object(recovery.subprocess, "run", return_value=subprocess.CompletedProcess([], 1)), \
                contextlib.redirect_stdout(io.StringIO()):
            try:
                receipt = recovery.execute(action, self.directory / "run", expected)
                return commands, receipt, None, existing_verifier.call_count
            except ValueError as error:
                return commands, None, str(error), existing_verifier.call_count

    def assert_no_game_transfer(self, commands):
        self.assertFalse([c for c in commands if c[1] == "pull"], commands)
        self.assertTrue(all(":bootstrap-private-owner-check-" in c[2]
                            for c in commands if c[1] == "push"), commands)

    def test_existing_public_unlinked_foreign_or_unknown_target_blocks_before_game(self):
        for field, value in (("visibility", "public"), ("name", "nflblitz"),
                             ("owner", {"login": "another", "type": "User"}),
                             ("repository", {"full_name": "another/repo"}),
                             ("package_type", "npm"), ("repository", None)):
            with self.subTest(field=field, value=value):
                package = copy.deepcopy(self.private)
                package[field] = value
                commands, _, error, _ = self.execute(packages=[package])
                self.assertIn("must actually be private", error)
                self.assert_no_game_transfer(commands)

    def test_404_allows_only_asset_free_unique_nonlatest_bootstrap_then_requires_private(self):
        public = copy.deepcopy(self.private)
        public["visibility"] = "public"
        commands, _, error, _ = self.execute(packages=[None, public])
        self.assertIn("must actually be private", error)
        self.assert_no_game_transfer(commands)
        pushes = [c[2] for c in commands if c[1] == "push"]
        self.assertEqual(["ghcr.io/long2know/nflblitz-private:bootstrap-private-owner-check-123-1"], pushes)

    def test_bootstrap_jobtoken_denied_fails_without_reading_game(self):
        commands = []
        def denied(args):
            commands.append(args)
            if args[0] == "push":
                raise ValueError("registry denied inaccessible target")
            return b""
        with mock.patch.dict(os.environ, self.env, clear=True), \
                mock.patch.object(recovery, "package_metadata", return_value=None), \
                mock.patch.object(recovery, "anonymous_denied"):
            with self.assertRaisesRegex(ValueError, "registry denied"):
                recovery.ensure_private_target("synthetic", denied, self.directory)
        self.assertFalse([c for c in commands if c[0] == "pull"])
        self.assertTrue(all("bootstrap-private-owner-check-" in c[1] for c in commands if c[0] == "push"))

    def test_private_verified_bootstrap_precedes_all_game_access_and_no_old_namespace_push(self):
        commands, receipt, error, _ = self.execute(packages=[None, self.private])
        self.assertIsNone(error)
        bootstrap = next(i for i, c in enumerate(commands) if c[1] == "push" and "bootstrap-" in c[2])
        game_pull = next(i for i, c in enumerate(commands) if c[1] == "pull")
        self.assertLess(bootstrap, game_pull)
        self.assertEqual(self.pins["imageId"], receipt["image_id"])
        self.assertEqual("ghcr.io/long2know/nflblitz-private:latest", receipt["latest"])
        self.assertTrue(all(c[2].startswith("ghcr.io/long2know/nflblitz-private:")
                            for c in commands if c[1] == "push"))
        self.assertTrue(any(c[1] == "pull" and c[-1] ==
                            "ghcr.io/long2know/nflblitz@" + self.pins["sourceManifestDigest"] for c in commands))

    def test_foreign_existing_latest_or_immutable_stops_before_source_pull_or_overwrite(self):
        commands, _, error, _ = self.execute(existing=ValueError("Selected private latest identifies another image"))
        self.assertIn("another image", error)
        self.assert_no_game_transfer(commands)

    def test_existing_same_id_requires_full_existing_layer_verification_not_tag_only(self):
        calls = []
        def docker(args):
            calls.append(args)
            return b""
        with mock.patch.object(recovery, "verify_loaded", side_effect=ValueError("wrong actual rootfs layer")):
            with self.assertRaisesRegex(ValueError, "wrong actual rootfs"):
                recovery.verify_existing_images({"latest": "sha256:" + "d" * 64},
                                                self.pins, docker, self.directory)
        self.assertTrue(any(c[0] == "pull" for c in calls))
        self.assertFalse(any(c[0] == "push" for c in calls))

    def test_wrong_readonly_source_manifest_stops_before_private_game_push(self):
        commands, _, error, _ = self.execute(source_digest="sha256:" + "e" * 64)
        self.assertIn("source manifest/config changed", error)
        self.assert_no_game_transfer(commands)

    def test_changed_config_layer_payload_stops_before_any_game_push(self):
        commands, _, error, _ = self.execute(bad_payload=True)
        self.assertIn("config/layer/payload differs", error)
        self.assertFalse([c for c in commands if c[1] == "push"], commands)

    def test_visibility_flip_after_verified_pull_stops_before_private_game_push(self):
        public = copy.deepcopy(self.private)
        public["visibility"] = "public"
        commands, _, error, _ = self.execute(packages=[self.private, public])
        self.assertIn("must actually be private", error)
        self.assertFalse([c for c in commands if c[1] == "push"], commands)

    def test_same7419_recovery_is_idempotent_only_and_checks_existing_images_twice(self):
        commands, receipt, error, checks = self.execute(existing=[{"latest": "sha256:" + "d" * 64},
                                                                {"latest": "sha256:" + "d" * 64}],
                                                       manifest_values=["sha256:" + "d" * 64] * 3)
        self.assertIsNone(error)
        self.assertEqual(2, checks)
        self.assertEqual(self.pins["imageId"], receipt["image_id"])
        self.assertEqual(2, len([c for c in commands if c[1] == "push"]))

    def test_cold_pull_has_no_build_push_or_tag_to_remote_namespace(self):
        digest = "sha256:" + "d" * 64
        commands, receipt, error, _ = self.execute(action="cold-pull", expected=digest,
                                                  manifest_values=[digest, digest])
        self.assertIsNone(error)
        self.assertEqual(digest, receipt["manifest_digest"])
        self.assertFalse([c for c in commands if c[1] in ("build", "push")])
        self.assertEqual(1, len([c for c in commands if c[1] == "pull"]))

    def test_cold_pull_wrong_digest_rejected_before_pull(self):
        commands, _, error, _ = self.execute(action="cold-pull", expected="sha256:" + "e" * 64,
                                             manifest_values=["sha256:" + "d" * 64] * 2)
        self.assertIn("cold-pull latest/immutable/config identity differs", error)
        self.assertFalse([c for c in commands if c[1] == "pull"])

    def test_branch_wrong_repository_or_nonmanual_context_cannot_recover(self):
        for key, value in (("GITHUB_REF", "refs/heads/diagnostic"), ("GITHUB_REPOSITORY", "another/repo"),
                           ("GITHUB_EVENT_NAME", "pull_request")):
            with self.subTest(key=key):
                env = dict(self.env, **{key: value})
                with self.assertRaisesRegex(ValueError, "actual repository main manual"):
                    recovery.context(env)

    def test_physical_pin_file_change_rejected_without_any_network_or_image_operation(self):
        copied = self.directory / "private_target_recovery_pins.json"
        copied.write_bytes((HERE / "private_target_recovery_pins.json").read_bytes() + b" ")
        with mock.patch.object(recovery, "HERE", self.directory), \
                self.assertRaisesRegex(ValueError, "approved recovery/source/runtime/Quality pins changed"):
            recovery.load_pins()

    def test_actual_main_file_disconnect_remains_rejected(self):
        rows = [{"path": row["path"], "sha": row["gitBlobSha1"], "mode": "100644", "type": "blob"}
                for row in self.pins["requiredRemoteMainFiles"]]
        rows[0]["sha"] = "0" * 40
        with mock.patch.dict(os.environ, self.env, clear=True), \
                mock.patch.object(recovery, "github_json", side_effect=[
                    {"status": "ahead"}, {"tree": {"sha": "a" * 40}},
                    {"sha": "a" * 40, "truncated": False, "tree": rows}]), \
                self.assertRaisesRegex(ValueError, "main147/161/17 bytes differ"):
            recovery.verify_main(self.pins, "synthetic")

    def test_anonymous_public_token_unknown_response_and_valid_denial_are_distinct(self):
        for status, contents, okay in (
                (403, b'{"errors":[{"code":"DENIED"}]}', True),
                (404, b'{"errors":[{"code":"DENIED"}]}', False),
                (403, b'{"errors":[{"code":"OTHER"}]}', False)):
            with self.subTest(status=status, contents=contents):
                opener = mock.MagicMock()
                opener.open.side_effect = HTTPError("https://ghcr.io/token", status, "", {}, io.BytesIO(contents))
                with mock.patch.object(recovery, "build_opener", return_value=opener):
                    if okay:
                        recovery.anonymous_denied()
                    else:
                        with self.assertRaises(ValueError):
                            recovery.anonymous_denied()
                self.assertIn("nflblitz-private", opener.open.call_args.args[0].full_url)
        opener = mock.MagicMock()
        opener.open.return_value.__enter__.return_value = object()
        with mock.patch.object(recovery, "build_opener", return_value=opener), \
                self.assertRaisesRegex(ValueError, "Anonymous target pull token is available"):
            recovery.anonymous_denied()

    def test_real_private_manifest_parser_rejects_foreign_config_or_hidden_images(self):
        wrong = {"schemaVersion": 2, "config": {"digest": "sha256:" + "f" * 64},
                 "layers": [{"digest": "sha256:" + "a" * 64}]}
        hidden = {"schemaVersion": 2, "manifests": [{"digest": "sha256:" + "a" * 64},
                                                  {"digest": "sha256:" + "b" * 64}]}
        for manifest in (wrong, hidden):
            with self.subTest(manifest=manifest):
                raw = json.dumps(manifest).encode()
                response = mock.MagicMock()
                response.read.return_value = raw
                response.headers = {"Docker-Content-Digest": "sha256:" + hashlib.sha256(raw).hexdigest()}
                opener = mock.MagicMock()
                opener.open.return_value.__enter__.return_value = response
                with mock.patch.object(recovery, "target_pull_token", return_value="synthetic"), \
                        mock.patch.object(recovery, "build_opener", return_value=opener), \
                        self.assertRaises(ValueError):
                    recovery.target_manifest("latest", "synthetic")

    def test_real_private_manifest_absence_accepts_only_named404_not_unknown_failures(self):
        for status, code, okay in ((404, "MANIFEST_UNKNOWN", True), (404, "OTHER", False), (500, "OTHER", False)):
            with self.subTest(status=status, code=code):
                opener = mock.Mock()
                opener.open.side_effect = HTTPError(
                    "https://ghcr.io/v2/long2know/nflblitz-private/manifests/latest",
                    status, "", {}, io.BytesIO(json.dumps({"errors": [{"code": code}]}).encode()))
                with mock.patch.object(recovery, "target_pull_token", return_value="synthetic"), \
                        mock.patch.object(recovery, "build_opener", return_value=opener):
                    if okay:
                        self.assertIsNone(recovery.target_manifest("latest", "synthetic", absent_ok=True))
                    else:
                        with self.assertRaises(ValueError):
                            recovery.target_manifest("latest", "synthetic", absent_ok=True)


if __name__ == "__main__":
    unittest.main()
