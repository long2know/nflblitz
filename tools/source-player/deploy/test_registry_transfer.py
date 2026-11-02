"""Asset-free publisher tests: real OpenSSL, synthetic Docker archive, no remote writes."""

import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import secrets
import shutil
import tarfile
import unittest
from unittest import mock
from urllib.error import HTTPError
from urllib.parse import parse_qs, urlsplit


HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("registry_transfer", HERE / "registry_transfer.py")
transfer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(transfer)


class TransferTests(unittest.TestCase):
    def setUp(self):
        self.directory = HERE / (".registry-transfer-tests-" + secrets.token_hex(8))
        self.directory.mkdir()
        self.addCleanup(shutil.rmtree, self.directory)
        self.archive = self.directory / "accepted.tar.gz"
        self.cipher = self.directory / "accepted.enc"
        self.key = secrets.token_urlsafe(48)
        self.values = {
            "release_tag": "container-transfer-20261101-" + "a" * 32,
            "secret_name": "CONTAINER_TRANSFER_" + "A" * 32,
            "source_tag": "nflblitz-original:toy-amd64", "source_revision": "a" * 40,
            "cipher_sha256": "0" * 64, "archive_sha256": "0" * 64,
            "image_id": "sha256:" + "0" * 64, "build_sha256": "0" * 64,
            "source_digest": "0" * 64, "data_manifest_sha256": "0" * 64,
        }
        sources = [{"path": "synthetic-public.cs", "sha256": hashlib.sha256(b"public toy").hexdigest()}]
        self.values["source_digest"] = hashlib.sha256(json.dumps(sources, sort_keys=True).encode()).hexdigest()
        data = {"schema": "blitz-portable-source-player/v1", "profile": transfer.PROFILE,
                "factory": transfer.FACTORY,
                "files": [{"path": "synthetic-" + str(i), "bytes": 244325118 if i == 0 else 0}
                          for i in range(963)]}
        self.data_bytes = json.dumps(data).encode()
        self.values["data_manifest_sha256"] = hashlib.sha256(self.data_bytes).hexdigest()
        build = {"schema": "blitz-source-player-build/v1", "runtimeIdentifier": "browser-wasm",
                 "nativeUnityBuild": False, "profile": transfer.PROFILE, "factory": transfer.FACTORY,
                 "sources": sources, "sourceRootDigest": self.values["source_digest"],
                 "dataManifestSha256": self.values["data_manifest_sha256"]}
        self.build_bytes = json.dumps(build).encode()
        self.values["build_sha256"] = hashlib.sha256(self.build_bytes).hexdigest()
        self.config = {
            "os": "linux", "architecture": "amd64",
            "config": {"User": "101:101", "WorkingDir": "/opt/nflblitz",
                       "Entrypoint": ["nginx"], "Cmd": ["-g", "daemon off;"],
                       "Labels": {"io.nflblitz.runtime": "portable-csharp-browser-wasm",
                                  "org.opencontainers.image.revision": self.values["source_revision"],
                                  "org.opencontainers.image.version": "toy-amd64"}},
            "rootfs": {"type": "layers", "diff_ids": []},
        }
        layer = io.BytesIO()
        with tarfile.open(fileobj=layer, mode="w") as stream:
            self.add_member(stream, "public.txt", b"public synthetic image only\n")
        self.layer = layer.getvalue()
        self.config["rootfs"]["diff_ids"] = ["sha256:" + hashlib.sha256(self.layer).hexdigest()]
        self.save_archive()
        transfer.crypt(self.archive, self.cipher, self.key)
        self.values["cipher_sha256"] = transfer.sha(self.cipher)
        self.real_run = transfer.run

    @staticmethod
    def add_member(stream, name, contents):
        entry = tarfile.TarInfo(name)
        entry.size = len(contents)
        stream.addfile(entry, io.BytesIO(contents))

    def save_archive(self, *, update_id=True):
        contents = json.dumps(self.config, sort_keys=True).encode()
        image_id = "sha256:" + hashlib.sha256(contents).hexdigest()
        if update_id:
            self.values["image_id"] = image_id
        config_name = image_id.split(":")[1] + ".json"
        manifest = [{"Config": config_name, "RepoTags": [self.values["source_tag"]],
                     "Layers": ["layer/layer.tar"]}]
        with tarfile.open(self.archive, "w:gz") as stream:
            self.add_member(stream, config_name, contents)
            self.add_member(stream, "manifest.json", json.dumps(manifest).encode())
            self.add_member(stream, "layer/layer.tar", self.layer)
        self.values["archive_sha256"] = transfer.sha(self.archive)

    def encrypt_again(self):
        self.cipher.unlink()
        transfer.crypt(self.archive, self.cipher, self.key)
        self.values["cipher_sha256"] = transfer.sha(self.cipher)

    def environment(self):
        return dict(GITHUB_ACTIONS="true", GITHUB_EVENT_NAME="workflow_dispatch",
                    GITHUB_REPOSITORY=transfer.REPOSITORY, GITHUB_REF="refs/heads/main",
                    REGISTRY_TRANSFER_KEY=self.key, GH_TOKEN="synthetic-job-token",
                    **{"TRANSFER_" + key.upper(): value for key, value in self.values.items()})

    def publish(self, *, remote_id=None, package_visibility="private"):
        work = self.directory / "runner"
        work.mkdir()
        commands = []
        digest = "sha256:" + "d" * 64

        def fake_api(path, token=None):
            if "/releases/tags/" in path:
                return {"tag_name": self.values["release_tag"], "prerelease": True, "draft": False,
                        "assets": [{"name": transfer.ASSET}]}
            return {"visibility": package_visibility, "package_type": "container", "name": "nflblitz",
                    "owner": {"login": "long2know"}, "repository": {"full_name": transfer.REPOSITORY}}

        def command(argv, **kwargs):
            commands.append(argv)
            if argv[0] == "openssl":
                return self.real_run(argv, **kwargs)
            if argv[:3] == ["gh", "release", "download"]:
                shutil.copyfile(self.cipher, work / transfer.ASSET)
                return b""
            self.assertEqual("docker", argv[0])
            if argv[1:3] == ["image", "inspect"]:
                return json.dumps([{"Id": self.values["image_id"], "Os": self.config["os"],
                                    "Architecture": self.config["architecture"],
                                    "Config": self.config["config"]}]).encode()
            if argv[1] == "create":
                self.assertIn("--read-only", argv)
                self.assertEqual("/bin/false", argv[argv.index("--entrypoint") + 1])
                return ("c" * 64 + "\n").encode()
            if argv[1] == "cp":
                contents = self.build_bytes if argv[2].endswith("/build-info.json") else self.data_bytes
                Path(argv[3]).write_bytes(contents)
            if argv[1:4] == ["buildx", "imagetools", "inspect"]:
                if "--format" in argv:
                    return json.dumps({"digest": digest}).encode()
                return json.dumps({"schemaVersion": 2, "config": {"digest": remote_id or self.values["image_id"]},
                                   "layers": [{"digest": "sha256:" + "e" * 64}]}).encode()
            return b""

        with mock.patch.dict(os.environ, self.environment(), clear=True), \
                mock.patch.object(transfer, "private_directory", return_value=work), \
                mock.patch.object(transfer, "api", side_effect=fake_api), \
                mock.patch.object(transfer, "run", side_effect=command), \
                contextlib.redirect_stdout(io.StringIO()):
            try:
                transfer.publish(work)
            except ValueError as error:
                return commands, str(error)
        self.assertFalse(work.exists(), "Runner plaintext and credentials must be removed.")
        return commands, None

    def assert_no_docker_mutation(self, commands):
        self.assertFalse([cmd for cmd in commands if cmd[:2] in
                          (["docker", "load"], ["docker", "tag"], ["docker", "push"])], commands)

    def test_new_package_checks_anonymous_registry_not_authenticated_only_metadata_api(self):
        denial = HTTPError("https://ghcr.io/token", 403, "Forbidden", {},
                           io.BytesIO(b'{"errors":[{"code":"DENIED"}]}'))
        opener = mock.Mock()
        opener.open.side_effect = denial
        with mock.patch.object(transfer, "api", return_value=None) as api, \
                mock.patch.object(transfer, "build_opener", return_value=opener), \
                mock.patch.object(transfer, "run") as process:
            transfer.private_package("synthetic-job-token", allow_new=True)
        api.assert_called_once_with("/users/long2know/packages/container/nflblitz", "synthetic-job-token")
        request = opener.open.call_args.args[0]
        self.assertEqual("https", urlsplit(request.full_url).scheme)
        self.assertEqual("ghcr.io", urlsplit(request.full_url).hostname)
        self.assertEqual({"service": ["ghcr.io"], "scope": ["repository:long2know/nflblitz:pull"]},
                         parse_qs(urlsplit(request.full_url).query))
        self.assertNotIn("Authorization", request.headers)
        process.assert_not_called()

    def test_anonymous_public_registry_access_blocks_new_package_before_mutation(self):
        opener = mock.Mock()
        opener.open.return_value = contextlib.nullcontext(mock.Mock(status=200))
        with mock.patch.object(transfer, "api", return_value=None), \
                mock.patch.object(transfer, "build_opener", return_value=opener), \
                mock.patch.object(transfer, "run") as process, \
                self.assertRaisesRegex(ValueError, "Anonymous registry pull token"):
            transfer.private_package("synthetic-job-token", allow_new=True)
        process.assert_not_called()

    def test_unknown_anonymous_responses_never_authorize_new_package(self):
        for status, body in ((401, b'{"errors":[{"code":"UNAUTHORIZED"}]}'),
                             (403, b'{"errors":[{"code":"TOOMANYREQUESTS"}]}'),
                             (403, b'{}'), (503, b'{}')):
            opener = mock.Mock()
            opener.open.side_effect = HTTPError("https://ghcr.io/token", status, "Rejected", {},
                                               io.BytesIO(body))
            with self.subTest(status=status, body=body), \
                    mock.patch.object(transfer, "api", return_value=None), \
                    mock.patch.object(transfer, "build_opener", return_value=opener), \
                    mock.patch.object(transfer, "run") as process, \
                    self.assertRaisesRegex(ValueError, "Unknown anonymous registry"):
                transfer.private_package("synthetic-job-token", allow_new=True)
            process.assert_not_called()

    def test_untrusted_dispatch_inputs_and_wrong_context_are_rejected_before_io(self):
        attacks = {
            "release_tag": ["../payload", "container-transfer-20261399-" + "a" * 32],
            "secret_name": ["GITHUB_TOKEN", "CONTAINER_TRANSFER_" + "B" * 32],
            "source_tag": ["--privileged", "nflblitz-original:good;touch pwned", "other:latest"],
            "image_id": ["sha256:" + "F" * 64, "$(id)"],
            "archive_sha256": ["b" * 63, "0" * 64 + "\n"],
            "source_revision": ["main", "a" * 40 + ";id"],
        }
        for field, variants in attacks.items():
            for value in variants:
                env = self.environment()
                env["TRANSFER_" + field.upper()] = value
                with self.subTest(field=field, value=value), mock.patch.dict(os.environ, env, clear=True), \
                        mock.patch.object(transfer, "run") as process, \
                        mock.patch.object(transfer, "api") as api, \
                        self.assertRaises(ValueError):
                    transfer.publish(self.directory / "must-not-exist")
                process.assert_not_called()
                api.assert_not_called()
        for key, value in (("GITHUB_REF", "refs/heads/unreviewed"), ("GITHUB_EVENT_NAME", "pull_request"),
                           ("GITHUB_REPOSITORY", "fork/nflblitz"), ("GITHUB_ACTIONS", "false")):
            env = self.environment()
            env[key] = value
            with self.subTest(context=key), self.assertRaisesRegex(ValueError, "manual dispatch"):
                transfer.publication_context(env)

    def test_changed_ciphertext_is_rejected_before_openssl_or_docker_load(self):
        cipher = bytearray(self.cipher.read_bytes())
        cipher[-17] ^= 1
        self.cipher.write_bytes(cipher)
        commands, error = self.publish()
        self.assertIn("Ciphertext SHA256 mismatch", error)
        self.assertFalse([cmd for cmd in commands if cmd[0] == "openssl"])
        self.assert_no_docker_mutation(commands)

    def test_wrong_plaintext_digest_is_rejected_before_any_docker_mutation(self):
        self.values["archive_sha256"] = "f" * 64
        commands, error = self.publish()
        self.assertIn("Archive SHA256 mismatch", error)
        self.assertTrue([cmd for cmd in commands if cmd[0] == "openssl" and "-d" in cmd])
        self.assert_no_docker_mutation(commands)

    def test_wrong_image_config_and_root_runtime_are_rejected_before_load_tag_push(self):
        self.config["config"]["User"] = "0:0"
        self.save_archive(update_id=False)
        self.encrypt_again()
        commands, error = self.publish()
        self.assertIn("config identity differs", error)
        self.assert_no_docker_mutation(commands)
        self.save_archive()
        self.encrypt_again()
        commands, error = self.publish()
        self.assertIn("non-root static-server runtime", error)
        self.assert_no_docker_mutation(commands)

    def test_real_crypto_roundtrip_and_publisher_verify_before_promoting_latest(self):
        decoded = self.directory / "decoded.tar.gz"
        config = transfer.verify_transfer(self.cipher, decoded, self.values, self.key)
        self.assertEqual(self.archive.read_bytes(), decoded.read_bytes())
        self.assertEqual(self.config, config)
        commands, error = self.publish()
        self.assertIsNone(error)
        pushes = [cmd for cmd in commands if cmd[:2] == ["docker", "push"]]
        self.assertEqual([transfer.IMAGE + ":sha256-" + self.values["image_id"].split(":")[1],
                         transfer.IMAGE + ":latest"], [cmd[2] for cmd in pushes])
        first_push = commands.index(pushes[0])
        self.assertLess(next(i for i, cmd in enumerate(commands) if cmd[:2] == ["docker", "load"]), first_push)
        self.assertLess(max(i for i, cmd in enumerate(commands) if cmd[:2] == ["docker", "cp"]), first_push)
        self.assertFalse([cmd for cmd in commands if cmd[:2] in
                          (["docker", "build"], ["docker", "run"], ["docker", "start"])])
        remote_reads = [cmd for cmd in commands if cmd[:4] == ["docker", "buildx", "imagetools", "inspect"]]
        self.assertEqual(4, len(remote_reads))
        self.assertTrue(all(transfer.IMAGE + "@sha256:" in cmd[4] for cmd in remote_reads if "--raw" in cmd))

    def test_wrong_embedded_build_receipt_fails_before_tag_or_push(self):
        self.build_bytes += b"\n"
        commands, error = self.publish()
        self.assertIn("build-info SHA256 differs", error)
        self.assertTrue([cmd for cmd in commands if cmd[:2] == ["docker", "load"]])
        self.assertFalse([cmd for cmd in commands if cmd[:2] in (["docker", "tag"], ["docker", "push"])])

    def test_remote_config_substitution_cannot_promote_latest(self):
        commands, error = self.publish(remote_id="sha256:" + "f" * 64)
        self.assertIn("Remote manifest does not link", error)
        self.assertFalse([cmd for cmd in commands if cmd[:2] == ["docker", "push"] and cmd[2].endswith(":latest")])

    def test_public_or_unknown_package_never_reaches_transfer_or_push(self):
        commands, error = self.publish(package_visibility="public")
        self.assertIn("must be private", error)
        self.assertFalse([cmd for cmd in commands if cmd[0] in ("gh", "openssl")])
        self.assert_no_docker_mutation(commands)
        with mock.patch.object(transfer, "api", side_effect=[None, {"visibility": "public"}]), \
                self.assertRaisesRegex(ValueError, "visibility is unknown"):
            transfer.private_package("synthetic-job-token", allow_new=True)
        with mock.patch.object(transfer, "api", return_value=None), \
                self.assertRaisesRegex(ValueError, "visibility is unknown"):
            transfer.private_package("synthetic-job-token")
        with mock.patch.object(transfer, "api", side_effect=ValueError("HTTP 403")), \
                self.assertRaisesRegex(ValueError, "HTTP 403"):
            transfer.private_package("synthetic-job-token", allow_new=True)

    def test_prepare_uploads_ciphertext_only_and_keeps_secret_out_of_receipts_and_argv(self):
        output = self.directory / "prepared"
        output.mkdir()
        commands, captured_key = [], []

        def command(argv, **kwargs):
            commands.append(argv)
            if argv[0] == "openssl":
                return self.real_run(argv, **kwargs)
            if argv[1:3] == ["secret", "list"]:
                return b"[]"
            if argv[1:3] == ["secret", "set"]:
                captured_key.append(kwargs["input"].decode().strip())
            if argv[1] == "api":
                return ("a" * 40 + "\n").encode()
            return b""

        args = type("Arguments", (), {})()
        for field in transfer.INPUTS - {"release_tag", "secret_name", "cipher_sha256"}:
            setattr(args, field, self.values[field])
        args.archive, args.output_dir, args.qa_approved = self.archive, output, True
        with mock.patch.object(transfer, "private_directory", return_value=output), \
                mock.patch.object(transfer, "run", side_effect=command), \
                contextlib.redirect_stdout(io.StringIO()):
            transfer.prepare(args)
        self.assertEqual(1, len(captured_key))
        receipt = (output / "receipt.json").read_text()
        workflow_inputs = (output / "workflow-inputs.json").read_text()
        self.assertNotIn(captured_key[0], receipt + workflow_inputs + json.dumps(commands))
        upload = next(cmd for cmd in commands if cmd[:3] == ["gh", "release", "upload"])
        self.assertEqual(str(output / transfer.ASSET), upload[4])
        create = next(cmd for cmd in commands if cmd[:3] == ["gh", "release", "create"])
        self.assertIn("--prerelease", create)
        self.assertIn("--latest=false", create)
        self.assertFalse([cmd for cmd in commands if str(self.archive) in cmd and cmd[0] == "gh"])
        inputs = json.loads(workflow_inputs)
        decoded = self.directory / "prepared-roundtrip.tar.gz"
        transfer.verify_transfer(output / transfer.ASSET, decoded, inputs, captured_key[0])
        self.assertEqual(self.archive.read_bytes(), decoded.read_bytes())


if __name__ == "__main__":
    unittest.main()
