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
import subprocess
import tarfile
import textwrap
import unittest
from unittest import mock
from urllib.error import HTTPError
from urllib.parse import parse_qs, urlsplit


HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("registry_transfer", HERE / "registry_transfer.py")
transfer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(transfer)


class PublicationSummaryTests(unittest.TestCase):
    def workflow_job(self):
        workflow = (HERE.parents[2] / ".github/workflows/publish-container.yml").read_text()
        return workflow.split("\n  publication-status:\n", 1)[1]

    def summary(self, validation, publisher):
        job = self.workflow_job()
        self.assertIn("    if: always()\n    needs: [validate, publish]\n", job)
        self.assertIn("    permissions: {}\n", job)
        self.assertIn("VALIDATION_RESULT: ${{ needs.validate.result }}", job)
        self.assertIn("PUBLISH_RESULT: ${{ needs.publish.result }}", job)
        script = textwrap.dedent(job.split("        run: |\n", 1)[1])
        directory = HERE.parents[2] / ".squad/.scratch" / (".registry-summary-tests-" + secrets.token_hex(8))
        directory.mkdir(parents=True)
        self.addCleanup(shutil.rmtree, directory)
        output = directory / "summary.txt"
        subprocess.run(["bash", "-e", "-o", "pipefail", "-c", script], check=True,
                       env=dict(PATH=os.environ["PATH"], VALIDATION_RESULT=validation,
                                PUBLISH_RESULT=publisher, GITHUB_STEP_SUMMARY=str(output)))
        return output.read_text()

    def test_green_validation_with_skipped_publisher_is_not_publication(self):
        for validation in ("success", "failure", "cancelled"):
            with self.subTest(validation=validation):
                summary = self.summary(validation, "skipped")
                self.assertIn("Asset-free validation: `" + validation + "`", summary)
                self.assertIn("Publisher job: `skipped`", summary)
                self.assertIn("**Not published by this run.**", summary)
                self.assertIn("authorized manual dispatch", summary)
                self.assertNotIn("Publisher verification succeeded", summary)

    def test_failed_or_cancelled_publisher_never_claims_verified_delivery(self):
        for publisher in ("failure", "cancelled"):
            with self.subTest(publisher=publisher):
                summary = self.summary("success", publisher)
                self.assertIn("Publisher job: `" + publisher + "`", summary)
                self.assertIn("**No successful publication receipt.**", summary)
                self.assertNotIn("Publisher verification succeeded", summary)
                self.assertNotIn("**Not published by this run.**", summary)

    def test_success_points_to_verified_receipt_without_inventing_a_digest(self):
        summary = self.summary("success", "success")
        self.assertIn("verified registry digest receipt in the publish job summary", summary)
        self.assertIn("not a new gameplay acceptance", summary)
        self.assertNotIn("sha256:", summary)

    def test_actual_workflow_validation_only_green_mutation_is_rejected(self):
        mutated = self.workflow_job().replace(
            "PUBLISH_RESULT: ${{ needs.publish.result }}",
            "PUBLISH_RESULT: ${{ needs.validate.result }}")
        with mock.patch.object(self, "workflow_job", return_value=mutated):
            with self.assertRaisesRegex(AssertionError, "PUBLISH_RESULT"):
                self.summary("success", "skipped")


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
        general = transfer.AV306_PROFILES[transfer.LATEST_PROFILE]
        data = {"schema": "blitz-portable-source-player/v3", "profile": transfer.LATEST_PROFILE,
                "factory": general["factory"],
                "files": [{"path": "synthetic-" + str(i), "bytes": general["bytes"] if i == 0 else 0}
                          for i in range(general["files"])]}
        self.data_bytes = json.dumps(data).encode()
        self.values["data_manifest_sha256"] = hashlib.sha256(self.data_bytes).hexdigest()
        build = {"schema": "blitz-source-player-build/v1", "runtimeIdentifier": "browser-wasm",
                 "nativeUnityBuild": False, "profile": transfer.LATEST_PROFILE, "factory": general["factory"],
                 "sources": sources, "sourceRootDigest": self.values["source_digest"],
                 "dataManifestSha256": self.values["data_manifest_sha256"],
                 "execution": {"mode": "aot", "runAOTCompilation": True, "publishTrimmed": True,
                               "jsonReflectionEnabled": True, "callerProfile": general["caller"]}}
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
        self.base_url_runtime()

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
                    GITHUB_SHA="a" * 40,
                    REGISTRY_TRANSFER_KEY=self.key, GH_TOKEN="synthetic-job-token",
                    **{"TRANSFER_" + key.upper(): value for key, value in self.values.items()})

    def publish(self, *, remote_id=None, package_visibility="private", runtime_id=None, exported_bytes=None,
                source_relation="identical"):
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
            if argv[:2] == ["gh", "api"] and "/compare/" in argv[2]:
                return (source_relation + "\n").encode()
            self.assertEqual("docker", argv[0])
            if argv[1:3] == ["image", "inspect"]:
                return json.dumps([{"Id": runtime_id or self.values["image_id"], "Os": self.config["os"],
                                    "Architecture": self.config["architecture"],
                                    "Config": self.config["config"]}]).encode()
            if argv[1:3] == ["image", "save"]:
                return exported_bytes or self.archive.read_bytes()
            if argv[1] == "create":
                self.assertIn("--read-only", argv)
                self.assertEqual("/bin/false", argv[argv.index("--entrypoint") + 1])
                return ("c" * 64 + "\n").encode()
            if argv[1] == "cp":
                path = argv[2].split(":", 1)[1]
                contents = (self.build_bytes if path.endswith("/build-info.json") else self.data_bytes
                            if path.endswith("/data/manifest.json") else self.extra_payloads[path])
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

    def base_url_runtime(self):
        self.extra_payloads = {}
        files = []
        for name, image_path in transfer.BASE_URL_DEPLOYMENT_FILES.items():
            contents = (HERE / name).read_bytes()
            files.append({"path": name, "imagePath": image_path, "bytes": len(contents),
                          "sha256": hashlib.sha256(contents).hexdigest(),
                          "mode": "0555" if name.endswith(".sh") else "0444"})
            if image_path is not None:
                self.extra_payloads[image_path] = contents
        deployment = json.dumps({"schema": "nflblitz-runtime-base-url/v1", "files": files,
                                 "defaultBaseUrl": "/", "generatedConfig": "/tmp/nflblitz-${uid}/nginx.conf",
                                 "runtimeWritablePath": "/tmp", "runtimeDirectoryMode": "0700",
                                 "readOnlyRootRequired": False, "requiredRuntimeMounts": []}).encode()
        self.extra_payloads["/opt/nflblitz/deployment.json"] = deployment
        runtime = self.config["config"]
        runtime.update(Entrypoint=["/opt/nflblitz/deploy/entrypoint.sh"], Env=["BASE_URL=/"],
                       Healthcheck={"Test": ["CMD", "/opt/nflblitz/deploy/healthcheck.sh"]})
        runtime["Labels"].update({
            "io.nflblitz.deployment": "runtime-base-url/v1",
            "io.nflblitz.deployment-sha256": hashlib.sha256(deployment).hexdigest()})
        self.save_archive()
        self.encrypt_again()

    def test_base_url_runtime_verifies_actual_scripts_before_any_promotion(self):
        self.base_url_runtime()
        commands, error = self.publish()
        self.assertIsNone(error)
        first_push = next(i for i, cmd in enumerate(commands) if cmd[:2] == ["docker", "push"])
        copied = [cmd[2] for cmd in commands[:first_push] if cmd[:2] == ["docker", "cp"]]
        self.assertTrue(any(path.endswith("/deploy/runtime-common.sh") for path in copied))
        self.assertTrue(any(path.endswith("/deployment.json") for path in copied))

    def test_changed_base_url_script_is_rejected_before_tag_or_push(self):
        self.base_url_runtime()
        self.extra_payloads["/opt/nflblitz/deploy/runtime-common.sh"] += b"\n# changed\n"
        commands, error = self.publish()
        self.assertIn("Changed runtime BASE_URL entrypoint/config/healthcheck", error)
        self.assertFalse([cmd for cmd in commands if cmd[:2] in (["docker", "tag"], ["docker", "push"])])

    def test_container_user_override_does_not_weaken_the_default_image_user_gate(self):
        self.base_url_runtime()
        self.config["config"]["User"] = "1000:1000"
        self.save_archive()
        self.encrypt_again()
        commands, error = self.publish()
        self.assertIn("accepted non-root static-server runtime", error)
        self.assert_no_docker_mutation(commands)

    def test_explicit_base_url_prototype_is_not_a_published_candidate(self):
        self.base_url_runtime()
        self.config["config"]["Labels"]["io.nflblitz.release-state"] = "prototype"
        self.save_archive()
        self.encrypt_again()
        commands, error = self.publish()
        self.assertIn("prototype must be finalized", error)
        self.assert_no_docker_mutation(commands)

    def test_root_only_image_cannot_replace_the_requested_base_url_latest(self):
        labels = self.config["config"]["Labels"]
        labels.pop("io.nflblitz.deployment")
        labels.pop("io.nflblitz.deployment-sha256")
        self.config["config"]["Entrypoint"] = ["nginx"]
        self.save_archive()
        self.encrypt_again()
        commands, error = self.publish()
        self.assertIn("latest publisher requires the BASE_URL general", error)
        self.assertFalse([cmd for cmd in commands if cmd[:2] in (["docker", "tag"], ["docker", "push"])])

    def test_deployment_cannot_choose_arbitrary_files_to_copy(self):
        self.base_url_runtime()
        path = "/opt/nflblitz/deployment.json"
        deployment = json.loads(self.extra_payloads[path])
        next(row for row in deployment["files"] if row["path"] == "nginx.conf")["imagePath"] = "/etc/shadow"
        raw = json.dumps(deployment).encode()
        self.extra_payloads[path] = raw
        self.config["config"]["Labels"]["io.nflblitz.deployment-sha256"] = hashlib.sha256(raw).hexdigest()
        self.save_archive()
        self.encrypt_again()
        commands, error = self.publish()
        self.assertIn("Unexpected deployment image path", error)
        self.assertFalse(any(cmd[:2] == ["docker", "cp"] and cmd[2].endswith(":/etc/shadow")
                             for cmd in commands))
        self.assertFalse([cmd for cmd in commands if cmd[:2] in (["docker", "tag"], ["docker", "push"])])

    def test_oci_descriptor_id_requires_original_config_and_all_saved_layers(self):
        self.base_url_runtime()
        commands, error = self.publish(runtime_id="sha256:" + "d" * 64)
        self.assertIsNone(error)
        self.assertTrue(any(cmd[:3] == ["docker", "image", "save"] for cmd in commands))

    def test_changed_config_in_runtime_export_cannot_be_accepted_as_an_oci_alias(self):
        self.base_url_runtime()
        original, values = self.archive.read_bytes(), self.values.copy()
        self.config["config"]["User"] = "0:0"
        self.save_archive(update_id=False)
        altered = self.archive.read_bytes()
        self.archive.write_bytes(original)
        self.values = values
        self.config["config"]["User"] = "101:101"
        commands, error = self.publish(runtime_id="sha256:" + "d" * 64, exported_bytes=altered)
        self.assertIn("config identity differs", error)
        self.assertFalse([cmd for cmd in commands if cmd[:2] in (["docker", "tag"], ["docker", "push"])])

    def test_single_platform_registry_index_retains_original_config_binding(self):
        calls = []
        top, child = "sha256:" + "a" * 64, "sha256:" + "b" * 64
        def docker(args):
            calls.append(args)
            if "--format" in args:
                return json.dumps({"digest": top}).encode()
            if args[3] == transfer.IMAGE + "@" + top:
                return json.dumps({"schemaVersion": 2, "manifests": [
                    {"digest": child, "platform": {"os": "linux", "architecture": "amd64"}}]}).encode()
            self.assertEqual(transfer.IMAGE + "@" + child, args[3])
            return json.dumps({"schemaVersion": 2, "config": {"digest": self.values["image_id"]},
                               "layers": [{"digest": "sha256:" + "c" * 64}]}).encode()
        self.assertEqual(top, transfer.remote_image(transfer.IMAGE + ":latest", self.values["image_id"], docker))
        self.assertEqual(3, len(calls))

    def test_registry_index_cannot_hide_extra_images_or_changed_config(self):
        for extra in (True, False):
            def docker(args):
                if "--format" in args:
                    return json.dumps({"digest": "sha256:" + "a" * 64}).encode()
                if args[3] == transfer.IMAGE + "@sha256:" + "a" * 64:
                    children = [{"digest": "sha256:" + "b" * 64}]
                    return json.dumps({"schemaVersion": 2, "manifests": children * (2 if extra else 1)}).encode()
                return json.dumps({"schemaVersion": 2, "config": {"digest": "sha256:" + "f" * 64},
                                   "layers": [{"digest": "sha256:" + "c" * 64}]}).encode()
            with self.subTest(extra=extra), self.assertRaises(ValueError):
                transfer.remote_image(transfer.IMAGE + ":latest", self.values["image_id"], docker)

    def test_unmerged_source_commit_stops_before_transfer_load_or_publication(self):
        for relation in ("behind", "diverged"):
            with self.subTest(relation=relation):
                commands, error = self.publish(source_relation=relation)
                self.assertIn("must already be merged", error)
                self.assertFalse(any(cmd[:3] == ["gh", "release", "download"] for cmd in commands))
                self.assert_no_docker_mutation(commands)

    def av306_receipts(self, profile, factory, caller, count, size):
        data = {"schema": "blitz-portable-source-player/v3", "profile": profile, "factory": factory,
                "files": [{"path": "synthetic-" + str(i), "bytes": size if i == 0 else 0}
                          for i in range(count)]}
        self.data_bytes = json.dumps(data).encode()
        self.values["data_manifest_sha256"] = hashlib.sha256(self.data_bytes).hexdigest()
        build = json.loads(self.build_bytes)
        build.update(profile=profile, factory=factory,
                     dataManifestSha256=self.values["data_manifest_sha256"],
                     execution={"mode": "aot", "runAOTCompilation": True, "publishTrimmed": True,
                                "jsonReflectionEnabled": True, "callerProfile": caller})
        self.build_bytes = json.dumps(build).encode()
        self.values["build_sha256"] = hashlib.sha256(self.build_bytes).hexdigest()

    def test_latest_preserves_general_family_and_rejects_named_profile_alias(self):
        cases = (
            ("original-teams-complete-av306", "SourceC2Composition.CreateCompleteAv306Simulation",
             "aot-rooted-bounded-caller-drain-av306/v1", 1153, 300324289),
            ("original-teams-complete-av306-two-station-records-v1",
             "SourceC2Composition.CreateCompleteAv306TwoStationRecordsSimulation",
             "aot-rooted-bounded-caller-drain-av306-two-station-records/v1", 1154, 300324845),
        )
        for case in cases:
            with self.subTest(profile=case[0]):
                self.av306_receipts(*case)
                commands, error = self.publish()
                if case[0] == transfer.LATEST_PROFILE:
                    self.assertIsNone(error)
                    first_push = next(i for i, cmd in enumerate(commands) if cmd[:2] == ["docker", "push"])
                    self.assertLess(max(i for i, cmd in enumerate(commands) if cmd[:2] == ["docker", "cp"]),
                                    first_push)
                else:
                    self.assertIn("named TwoStation must use a separate immutable-only lane", error)
                    self.assertFalse([cmd for cmd in commands if cmd[:2] in
                                      (["docker", "tag"], ["docker", "push"])])

    def test_current_profile_caller_factory_and_payload_substitution_cannot_promote(self):
        for field in ("factory", "profile", "callerProfile", "mode", "count"):
            with self.subTest(field=field):
                self.av306_receipts(
                    "original-teams-complete-av306-two-station-records-v1",
                    "SourceC2Composition.CreateCompleteAv306TwoStationRecordsSimulation",
                    "aot-rooted-bounded-caller-drain-av306-two-station-records/v1", 1154, 300324845)
                build, data = json.loads(self.build_bytes), json.loads(self.data_bytes)
                if field == "factory":
                    build["factory"] = "SourceC2Composition.CreateCompleteAv306Simulation"
                elif field == "profile":
                    build["profile"] = "original-teams-complete-original303-selected-world-v2"
                elif field == "callerProfile":
                    build["execution"]["callerProfile"] = "aot-rooted-bounded-caller-drain-av306/v1"
                elif field == "mode":
                    build["execution"]["mode"] = "interpreter"
                else:
                    data["files"].pop()
                self.data_bytes = json.dumps(data).encode()
                self.values["data_manifest_sha256"] = hashlib.sha256(self.data_bytes).hexdigest()
                build["dataManifestSha256"] = self.values["data_manifest_sha256"]
                self.build_bytes = json.dumps(build).encode()
                self.values["build_sha256"] = hashlib.sha256(self.build_bytes).hexdigest()
                commands, error = self.publish()
                self.assertIsNotNone(error)
                self.assertFalse([cmd for cmd in commands if cmd[:2] in
                                  (["docker", "tag"], ["docker", "push"])])

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
        opener = mock.Mock()
        opener.open.return_value = contextlib.nullcontext(mock.Mock(status=200))
        with mock.patch.object(transfer, "api", return_value=None), \
                mock.patch.object(transfer, "build_opener", return_value=opener), \
                self.assertRaisesRegex(ValueError, "Anonymous registry pull token"):
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
                if "/compare/" in argv[2]:
                    return b"identical\n"
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
