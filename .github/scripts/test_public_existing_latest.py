"""Focused closed-public alias tests. All network/Docker adapters are mocked; no legacy game suite."""
import base64
import contextlib
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
from urllib.error import HTTPError, URLError

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("closed_public", HERE / "public_existing_latest.py")
p = importlib.util.module_from_spec(spec)
spec.loader.exec_module(p)
# Public manifest metadata only: no layers, game bytes or credentials in this fixture.
RAW = base64.b64decode(
    "ewogICAic2NoZW1hVmVyc2lvbiI6IDIsCiAgICJtZWRpYVR5cGUiOiAiYXBwbGljYXRpb24vdm5kLmRvY2tlci5kaXN0cmlidXRpb24ubWFuaWZlc3QudjIranNvbiIsCiAgICJjb25maWciOiB7CiAgICAgICJtZWRpYVR5cGUiOiAiYXBwbGljYXRpb24vdm5kLmRvY2tlci5jb250YWluZXIuaW1hZ2UudjEranNvbiIsCiAgICAgICJzaXplIjogMTU0MDcsCiAgICAgICJkaWdlc3QiOiAic2hhMjU2Ojc0MTliNjJiNzI4OTQ1ZmE1OWU5MDU5MGRmZTRlN2VlZWM5YTBlZWQ5NmE5YjUzZGFlYmMyMjViMzFmYWU2Y2IiCiAgIH0sCiAgICJsYXllcnMiOiBbCiAgICAgIHsKICAgICAgICAgIm1lZGlhVHlwZSI6ICJhcHBsaWNhdGlvbi92bmQuZG9ja2VyLmltYWdlLnJvb3Rmcy5kaWZmLnRhci5nemlwIiwKICAgICAgICAgInNpemUiOiAzODQ5NzM4LAogICAgICAgICAiZGlnZXN0IjogInNoYTI1NjplMmRlOTY1MTNiYTllYjUzYjQzMTc4N2VjOGE2NWNkZGUzODBhYzQ3NzJhM2U0YzRiNzE0ZGNmZGUyYTEwMmI1IgogICAgICB9LAogICAgICB7CiAgICAgICAgICJtZWRpYVR5cGUiOiAiYXBwbGljYXRpb24vdm5kLmRvY2tlci5pbWFnZS5yb290ZnMuZGlmZi50YXIuZ3ppcCIsCiAgICAgICAgICJzaXplIjogNDM1ODQ4MywKICAgICAgICAgImRpZ2VzdCI6ICJzaGEyNTY6YjQwZDA0YWM0OGY3OTQ2Yjk4YzkzNmNjYmIwN2RhZjYyY2Q2YjRjNjNjNDFiY2JhMDE5NmQwMjg2NjE2M2JiZSIKICAgICAgfSwKICAgICAgewogICAgICAgICAibWVkaWFUeXBlIjogImFwcGxpY2F0aW9uL3ZuZC5kb2NrZXIuaW1hZ2Uucm9vdGZzLmRpZmYudGFyLmd6aXAiLAogICAgICAgICAic2l6ZSI6IDYyOCwKICAgICAgICAgImRpZ2VzdCI6ICJzaGEyNTY6YTliYWZlYWI0ZjE2NTU3YjQwYmVkOGFmOGJjYjRlM2QyYTUwYTNlYmY5MjJkOGE0NzU1Mjk5N2E5YzZhN2QwYSIKICAgICAgfSwKICAgICAgewogICAgICAgICAibWVkaWFUeXBlIjogImFwcGxpY2F0aW9uL3ZuZC5kb2NrZXIuaW1hZ2Uucm9vdGZzLmRpZmYudGFyLmd6aXAiLAogICAgICAgICAic2l6ZSI6IDk2NCwKICAgICAgICAgImRpZ2VzdCI6ICJzaGEyNTY6MjU4ZDhkM2FjYzNlMmEyNmJiYTA3NGMzOTk0ZDlkZWQ3MWFjOTVhNTUwZmY3NTY5ODNlMTJiN2NjOGM4NjAyNSIKICAgICAgfSwKICAgICAgewogICAgICAgICAibWVkaWFUeXBlIjogImFwcGxpY2F0aW9uL3ZuZC5kb2NrZXIuaW1hZ2Uucm9vdGZzLmRpZmYudGFyLmd6aXAiLAogICAgICAgICAic2l6ZSI6IDQwNCwKICAgICAgICAgImRpZ2VzdCI6ICJzaGEyNTY6N2I1MjAyOTcxY2ZlOWQ3ZWEzZDVlNTBjMjcwNDJhMmViYWM2MzE5NGQ2NjkxNTA2Y2I5ZDNhOGMwZWE4MWU0YyIKICAgICAgfSwKICAgICAgewogICAgICAgICAibWVkaWFUeXBlIjogImFwcGxpY2F0aW9uL3ZuZC5kb2NrZXIuaW1hZ2Uucm9vdGZzLmRpZmYudGFyLmd6aXAiLAogICAgICAgICAic2l6ZSI6IDEyMDksCiAgICAgICAgICJkaWdlc3QiOiAic2hhMjU2OmY2NTkxMDgwZThkZDkwNzY4ZDE2NGIyZDM1ZTQ0MGUwMGJmOTRkY2E5NGE2ZTQyOWE1N2M0ZmRkZDE5YmI1ZDkiCiAgICAgIH0sCiAgICAgIHsKICAgICAgICAgIm1lZGlhVHlwZSI6ICJhcHBsaWNhdGlvbi92bmQuZG9ja2VyLmltYWdlLnJvb3Rmcy5kaWZmLnRhci5nemlwIiwKICAgICAgICAgInNpemUiOiAxMzk2LAogICAgICAgICAiZGlnZXN0IjogInNoYTI1Njo5YWFiMDkxM2YzMDQ0ZDAyMWI2OWM3Yjk5OGY1MzczOTk0MzM2NzY1ZGQ1MTAyNTZjMDJlOTBlNWNjYzM0N2RlIgogICAgICB9LAogICAgICB7CiAgICAgICAgICJtZWRpYVR5cGUiOiAiYXBwbGljYXRpb24vdm5kLmRvY2tlci5pbWFnZS5yb290ZnMuZGlmZi50YXIuZ3ppcCIsCiAgICAgICAgICJzaXplIjogOTA4LAogICAgICAgICAiZGlnZXN0IjogInNoYTI1NjoxMjk2ZDM1ODkxMTE5NGI0MzkzYzQ3ZWNhMWZkMWE0NWI5NGI2OTEzZTRiYTg4NzkzODM4OWM0ZGViZWMxNDQxIgogICAgICB9LAogICAgICB7CiAgICAgICAgICJtZWRpYVR5cGUiOiAiYXBwbGljYXRpb24vdm5kLmRvY2tlci5pbWFnZS5yb290ZnMuZGlmZi50YXIuZ3ppcCIsCiAgICAgICAgICJzaXplIjogNTM3NDYsCiAgICAgICAgICJkaWdlc3QiOiAic2hhMjU2OjE3MjAzYWJkYzdmYmE1ODEyZmNlNGJlNDczNGU3YjBkZmFiNjJmOWU5MmY4NTlkYjQ5YzljMmNkNzI4ZmRkMGQiCiAgICAgIH0sCiAgICAgIHsKICAgICAgICAgIm1lZGlhVHlwZSI6ICJhcHBsaWNhdGlvbi92bmQuZG9ja2VyLmltYWdlLnJvb3Rmcy5kaWZmLnRhci5nemlwIiwKICAgICAgICAgInNpemUiOiA3MjAsCiAgICAgICAgICJkaWdlc3QiOiAic2hhMjU2Ojc2YmRiNTg3NDEwYzJkYmQ1YjZlMmIzM2QxYWNmYzY2NzAzOGQyNDYwNzMyODE0NTFlZTg3OWQ1NThlM2Y5NGUiCiAgICAgIH0sCiAgICAgIHsKICAgICAgICAgIm1lZGlhVHlwZSI6ICJhcHBsaWNhdGlvbi92bmQuZG9ja2VyLmltYWdlLnJvb3Rmcy5kaWZmLnRhci5nemlwIiwKICAgICAgICAgInNpemUiOiAxNTQ5LAogICAgICAgICAiZGlnZXN0IjogInNoYTI1Njo0ZWI3NDRkOTU2YzZjNzcwNzA5OTdiZjU0OGZlOTNlNmI4MTNmZmEzMjNiMDRhMmYwZmY3OWNiZWYwZjUzMjk0IgogICAgICB9LAogICAgICB7CiAgICAgICAgICJtZWRpYVR5cGUiOiAiYXBwbGljYXRpb24vdm5kLmRvY2tlci5pbWFnZS5yb290ZnMuZGlmZi50YXIuZ3ppcCIsCiAgICAgICAgICJzaXplIjogMjEyMzQyNzQwLAogICAgICAgICAiZGlnZXN0IjogInNoYTI1Njo5Zjc4ZGQ2YWQ4NTFlMzVjNzdlMzExNTAzOGJjMTZhYjhjNDE4ZmQ3ZGQwNDBlNjc4ODQ5N2NlMjhmYTRlZTU3IgogICAgICB9CiAgIF0KfQ==")


class ClosedPublicTests(unittest.TestCase):
    def setUp(self):
        self.directory = HERE / (".closed-public-tests-" + secrets.token_hex(6))
        self.directory.mkdir(mode=0o700)
        self.addCleanup(shutil.rmtree, self.directory)
        self.assertEqual(p.DIGEST, "sha256:" + hashlib.sha256(RAW).hexdigest())
        self.env = {"GITHUB_ACTIONS": "true", "GITHUB_EVENT_NAME": "workflow_dispatch",
                    "GITHUB_REPOSITORY": "long2know/nflblitz", "GITHUB_REF": "refs/heads/main",
                    "GITHUB_SHA": "a" * 40, "GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "1"}

    def alias(self, states, *, first_error=None):
        with mock.patch.object(p, "public_package") as metadata, \
                mock.patch.object(p, "anonymous_manifest", side_effect=states), \
                mock.patch.object(p, "registry_token", return_value="synthetic-bearer"), \
                mock.patch.object(p, "urllib_alias_put", side_effect=first_error) as put, \
                mock.patch.object(p, "curl_alias_put") as fallback:
            try:
                method = p.promote_alias(RAW, "synthetic-job-token", self.directory)
                return method, None, put.call_args_list, fallback.call_args_list, metadata.call_count
            except ValueError as error:
                return None, str(error), put.call_args_list, fallback.call_args_list, metadata.call_count

    def test_exact_missing_latest_uses_only_exact_manifest_put(self):
        method, error, calls, fallback, checks = self.alias([RAW, None, None, RAW])
        self.assertIsNone(error)
        self.assertEqual("exact-registry-manifest-put", method)
        self.assertEqual([(RAW, "synthetic-bearer")], [c.args for c in calls])
        self.assertEqual([], fallback)
        self.assertGreaterEqual(checks, 3)

    def test_existing_same_digest_is_idempotent_no_writer(self):
        method, error, calls, fallback, _ = self.alias([RAW, RAW])
        self.assertIsNone(error)
        self.assertEqual("already-exact-idempotent", method)
        self.assertEqual([], calls + fallback)

    def test_foreign_latest_and_preput_race_stop_without_writer(self):
        for states in ([RAW, b"foreign"], [RAW, None, b"foreign"]):
            with self.subTest(states=states):
                _, error, calls, fallback, _ = self.alias(states)
                self.assertIn("Foreign latest", error)
                self.assertEqual([], calls + fallback)

    def test_changed_source_stops_before_alias(self):
        _, error, calls, fallback, _ = self.alias([RAW + b" "])
        self.assertIn("source bytes changed", error)
        self.assertEqual([], calls + fallback)

    def test_safe_alternative_preserves_identical_bytes_and_rechecks_race(self):
        method, error, calls, fallback, _ = self.alias(
            [RAW, None, None, None, RAW, None, RAW], first_error=URLError("synthetic transport issue"))
        self.assertIsNone(error)
        self.assertEqual("same-manifest-byte-curl-fallback", method)
        self.assertEqual(RAW, fallback[0].args[0])

    def test_forbidden_or_conflict_is_not_bypassed_by_alternative(self):
        for status in (401, 403, 409, 412):
            with self.subTest(status=status):
                _, error, _, fallback, _ = self.alias(
                    [RAW, None, None, None], first_error=HTTPError(p.REGISTRY + "latest", status, "", {}, None))
                self.assertIn("no permission bypass", error)
                self.assertEqual([], fallback)

    def test_fallback_foreign_race_stops_instead_of_overwrite(self):
        _, error, _, fallback, _ = self.alias(
            [RAW, None, None, None, RAW, b"foreign"], first_error=URLError("synthetic"))
        self.assertIn("changed before", error)
        self.assertEqual([], fallback)

    def test_wire_manifest_mutation_wrong_config_or_foreign_latest_is_rejected(self):
        wrong = RAW.replace(p.CONFIG.encode(), ("sha256:" + "f" * 64).encode())
        for raw, digest in ((wrong, p.DIGEST), (RAW + b" ", p.DIGEST), (RAW, "sha256:" + "f" * 64)):
            with self.subTest(raw=raw[:30], digest=digest):
                response = mock.MagicMock()
                response.read.return_value = raw
                response.headers.get.return_value = digest
                response.headers.get_content_type.return_value = p.MEDIA_TYPE
                opener = mock.MagicMock()
                opener.open.return_value.__enter__.return_value = response
                with mock.patch.object(p, "registry_token", return_value="anonymous"), \
                        mock.patch.object(p, "build_opener", return_value=opener), \
                        self.assertRaisesRegex(ValueError, "Foreign or changed public"):
                    p.anonymous_manifest("latest")

    def test_latest_absence_is_only_named404(self):
        for status, code, okay in ((404, "MANIFEST_UNKNOWN", True), (404, "OTHER", False), (500, "OTHER", False)):
            opener = mock.Mock()
            opener.open.side_effect = HTTPError(p.REGISTRY + "latest", status, "", {},
                                               io.BytesIO(json.dumps({"errors": [{"code": code}]}).encode()))
            with mock.patch.object(p, "registry_token", return_value="anonymous"), \
                    mock.patch.object(p, "build_opener", return_value=opener):
                if okay:
                    self.assertIsNone(p.anonymous_manifest("latest", missing_ok=True))
                else:
                    with self.assertRaises(ValueError):
                        p.anonymous_manifest("latest", missing_ok=True)

    def test_exact_put_endpoint_body_and_conditional_header(self):
        response = mock.MagicMock()
        response.status = 201
        response.headers = {"Docker-Content-Digest": p.DIGEST}
        opener = mock.MagicMock()
        opener.open.return_value.__enter__.return_value = response
        with mock.patch.object(p, "build_opener", return_value=opener):
            p.urllib_alias_put(RAW, "synthetic")
        request = opener.open.call_args.args[0]
        self.assertEqual(p.REGISTRY + "latest", request.full_url)
        self.assertEqual("PUT", request.method)
        self.assertEqual(RAW, request.data)
        self.assertEqual("*", request.get_header("If-none-match"))

    def test_curl_fallback_keeps_authorization_only_on_stdin(self):
        with mock.patch.object(p.frozen, "run", return_value=b"201") as run:
            p.curl_alias_put(RAW, "synthetic-secret-marker", self.directory)
        self.assertEqual(["curl", "--config", "-"], run.call_args.args[0])
        self.assertIn(b"synthetic-secret-marker", run.call_args.kwargs["input"])
        self.assertTrue(all(b"synthetic-secret-marker" not in path.read_bytes()
                            for path in self.directory.iterdir() if path.is_file()))

    def test_public_package_identity_and_visibility_are_closed_not_private_guard_changes(self):
        good = {"visibility": "public", "name": "nflblitz", "package_type": "container",
                "owner": {"login": "long2know", "type": "User"}, "repository": {"full_name": "long2know/nflblitz"}}
        for key, value in (("visibility", "private"), ("name", "nflblitz-private"), ("owner", {"login": "foreign"}),
                           ("package_type", "npm"), ("repository", {"full_name": "foreign/repo"})):
            bad = dict(good, **{key: value})
            with mock.patch.object(p.frozen, "api", return_value=bad), self.assertRaisesRegex(ValueError, "actual PUBLIC"):
                p.public_package("synthetic")

    def test_actual_payload_file_mutation_rejected_against_complete_inventory(self):
        rows = [{"path": str(i) + ".txt", "bytes": 0, "sha256": hashlib.sha256(b"").hexdigest()} for i in range(1217)]
        raw = json.dumps({"files": rows}).encode()
        def docker(args):
            if args[0] == "create":
                return ("a" * 64).encode()
            if args[0] == "cp" and args[1].endswith("bundle-inventory.json"):
                Path(args[2]).write_bytes(raw)
            elif args[0] == "cp":
                root = Path(args[2])
                root.mkdir()
                for row in rows:
                    (root / row["path"]).write_bytes(b"")
                (root / rows[0]["path"]).write_bytes(b"physical changed byte")
            return b""
        with mock.patch.object(p.identity, "verify_loaded"), \
                mock.patch.object(p, "INVENTORY_SHA", hashlib.sha256(raw).hexdigest()), \
                self.assertRaisesRegex(ValueError, "all1217 payload bytes differ"):
            p.verify_full_payload({"sourceTag": "synthetic"}, docker, self.directory)

    def test_wrong_payload_at_production_entrypoint_prevents_any_alias(self):
        with mock.patch.dict(os.environ, dict(self.env, GH_TOKEN="synthetic"), clear=True), \
                mock.patch.object(p, "local_main_source"), mock.patch.object(p, "public_package"), \
                mock.patch.object(p, "anonymous_manifest", side_effect=[RAW, None]), \
                mock.patch.object(p, "verify_full_payload", side_effect=ValueError("physical payload mismatch")), \
                mock.patch.object(p.frozen, "run", return_value=b""), \
                mock.patch.object(p, "promote_alias") as alias, \
                self.assertRaisesRegex(ValueError, "payload mismatch"):
            p.execute("promote", self.directory / "run")
        alias.assert_not_called()

    def test_anonymous_production_caller_pulls_latest_without_token_login_or_push(self):
        commands = []
        def run(args, **kwargs):
            commands.append((args, kwargs))
            self.assertNotIn("GH_TOKEN", kwargs["env"])
            return b""
        with mock.patch.dict(os.environ, self.env, clear=True), \
                mock.patch.object(p, "local_main_source"), \
                mock.patch.object(p, "anonymous_manifest", return_value=RAW), \
                mock.patch.object(p, "verify_full_payload", return_value=self.directory / "inventory.json"), \
                mock.patch.object(p, "run_standard_user") as standard, \
                mock.patch.object(p.frozen, "run", side_effect=run), \
                mock.patch.object(p.subprocess, "run", return_value=subprocess.CompletedProcess([], 1)), \
                contextlib.redirect_stdout(io.StringIO()):
            result = p.execute("anonymous-check", self.directory / "run")
        self.assertEqual(p.IMAGE + ":latest", next(c[0][-1] for c in commands if c[0][1] == "pull"))
        self.assertFalse(any(c[0][1] in ("push", "login", "build") for c in commands))
        standard.assert_called_once()
        self.assertTrue(result["normalUID1000PrefixHealthFullHttpPassed"])

    def test_branch_or_token_bearing_anonymous_job_is_rejected_before_registry(self):
        for env in (dict(self.env, GITHUB_REF="refs/heads/not-main"), dict(self.env, GH_TOKEN="not-anonymous")):
            with mock.patch.dict(os.environ, env, clear=True), \
                    mock.patch.object(p, "local_main_source"), mock.patch.object(p, "anonymous_manifest") as manifest, \
                    self.assertRaises(ValueError):
                p.execute("anonymous-check", self.directory / "run")
            manifest.assert_not_called()


if __name__ == "__main__":
    unittest.main()
