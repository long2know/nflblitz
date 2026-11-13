"""Focused metadata-only reuse controls; synthetic metadata, no original assets, SDK, Docker or API."""
import importlib.util
import io
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import tarfile
import unittest
from unittest import mock
import refresh_static_bundle as refresh

HERE = Path(__file__).resolve().parent


class StaticRefreshTests(unittest.TestCase):
    def setUp(self):
        self.root = HERE / (".static-refresh-tests-" + secrets.token_hex(6))
        self.root.mkdir(mode=0o700)
        self.addCleanup(shutil.rmtree, self.root)
        project = b"<Project><PropertyGroup><PublishTrimmed>false</PublishTrimmed></PropertyGroup></Project>\n"
        script = self.root / "tools/source-player"
        (script / "wwwroot").mkdir(parents=True)
        (script / "SourcePlayer.Browser.csproj").write_bytes(project)
        (script / "wwwroot/main.js").write_bytes(b"new main loader")
        (script / "wwwroot/verified-assets.js").write_bytes(b"new verified cache module")
        self.sources = [
            {"origin": "production", "path": "Assets/Scripts/Gameplay/Unchanged.cs", "sha256": refresh.sha(b"old C#")},
            {"origin": "preview", "path": "tools/source-player/SourcePlayer.Browser.csproj", "sha256": refresh.sha(project)},
            {"origin": "preview", "path": "tools/source-player/wwwroot/main.js", "sha256": refresh.sha(b"old main")},
        ]
        compiled = self.root / self.sources[0]["path"]
        compiled.parent.mkdir(parents=True)
        compiled.write_bytes(b"old C#")
        self.old = {"schema": "blitz-source-player-build/v1", "sourceRootDigest": refresh.BASE_SOURCE_DIGEST,
                    "profile": "original-teams-complete-av306", "sources": self.sources,
                    "assemblies": [{"path": "actual existing Core", "sha256": "1" * 64}],
                    "execution": {"nativeRuntimeSha256": refresh.BASE_NATIVE_SHA,
                                  "applicationObjects": [{"assembly": "SourcePlayer.Core", "sha256": "2" * 64}]}}
        self.inventory = {"files": [
            {"path": "main.js", "bytes": 8, "sha256": refresh.sha(b"old main")},
            {"path": "build-info.json", "bytes": 3, "sha256": "0" * 64},
            {"path": "_framework/dotnet.native.wasm", "bytes": 4, "sha256": refresh.BASE_NATIVE_SHA},
            {"path": "data/immutable", "bytes": 7, "sha256": refresh.sha(b"old C#")},
        ]}
        self.old_bytes = refresh.encoded(self.old)
        self.inventory_bytes = refresh.encoded(self.inventory)

    def run_refresh(self, revision=None, **options):
        with mock.patch.object(refresh, "BASE_BUILD_SHA", refresh.sha(self.old_bytes)), \
                mock.patch.object(refresh, "BASE_INVENTORY_SHA", refresh.sha(self.inventory_bytes)):
            return refresh.refresh(self.old_bytes, self.inventory_bytes, self.root, revision, **options)

    def test_only_new_static_bytes_change_and_actual_compiler_native_assemblies_are_retained(self):
        value, static, inventory = self.run_refresh("a" * 40)
        self.assertEqual(value["assemblies"], self.old["assemblies"])
        self.assertEqual(value["execution"]["applicationObjects"], self.old["execution"]["applicationObjects"])
        self.assertEqual(value["execution"]["nativeRuntimeSha256"], refresh.BASE_NATIVE_SHA)
        self.assertEqual(value["execution"]["compiledAotReuse"]["originalCompiledSourceDigest"], refresh.BASE_SOURCE_DIGEST)
        self.assertEqual(value["execution"]["compiledAotReuse"]["staticSourceRevision"], "a" * 40)
        self.assertNotEqual(value["sourceRootDigest"], refresh.BASE_SOURCE_DIGEST)
        self.assertEqual(static["verified-assets.js"], b"new verified cache module")
        self.assertEqual(next(row for row in inventory["files"] if row["path"] == "data/immutable"),
                         self.inventory["files"][-1])

    def test_physical_compile_project_or_trimmer_change_is_rejected(self):
        path = self.root / "tools/source-player/SourcePlayer.Browser.csproj"
        path.write_bytes(path.read_bytes().replace(b"false", b"true"))
        with self.assertRaisesRegex(ValueError, "compiled project"):
            self.run_refresh()

    def test_game_view_opt_in_pins_index_and_new_module_without_changing_legacy_refresh(self):
        prefix = "tools/source-player/wwwroot/"
        self.old["sources"].append({"origin": "preview", "path": prefix + "index.html",
                                    "sha256": refresh.sha(b"old index")})
        self.old_bytes = refresh.encoded(self.old)
        (self.root / (prefix + "index.html")).write_bytes(b"new view controls index")
        (self.root / (prefix + "view-controls.js")).write_bytes(b"new view controls module")
        with self.assertRaisesRegex(ValueError, "other compiled/browser source"):
            self.run_refresh()
        value, static, inventory = self.run_refresh(view_controls=True)
        self.assertEqual(static["index.html"], b"new view controls index")
        self.assertEqual(static["view-controls.js"], b"new view controls module")
        self.assertEqual(value["execution"]["compiledAotReuse"]["changedStaticSources"],
                         [prefix + name for name in ("main.js", "verified-assets.js", "index.html", "view-controls.js")])
        pin = next(row for row in inventory["files"] if row["path"] == "view-controls.js")
        self.assertEqual(pin["sha256"], refresh.sha(static["view-controls.js"]))
        self.assertEqual(value["execution"]["nativeRuntimeSha256"], refresh.BASE_NATIVE_SHA)
        (self.root / self.sources[0]["path"]).write_bytes(b"changed C#")
        with self.assertRaisesRegex(ValueError, "other compiled/browser source"):
            self.run_refresh(view_controls=True)

    def test_physical_any_other_compiled_source_change_is_rejected(self):
        path = self.root / self.sources[0]["path"]
        path.write_bytes(b"changed C#")
        with self.assertRaisesRegex(ValueError, "other compiled/browser source"):
            self.run_refresh()

    def test_old_build_or_complete_base_inventory_substitution_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "exact verified7419"):
            refresh.refresh(self.old_bytes + b" ", self.inventory_bytes, self.root, None)

    def test_missing_compiled_source_or_different_native_generation_is_rejected(self):
        path = self.root / self.sources[0]["path"]
        path.unlink()
        with self.assertRaisesRegex(ValueError, "omit or change"):
            self.run_refresh()
        path.write_bytes(b"old C#")
        self.old["execution"]["nativeRuntimeSha256"] = "0" * 64
        self.old_bytes = refresh.encoded(self.old)
        with self.assertRaisesRegex(ValueError, "genuinely compiled"):
            self.run_refresh()

    def test_uncommitted_candidate_is_explicit_not_a_fabricated_main_revision(self):
        value, _, _ = self.run_refresh()
        self.assertTrue(value["execution"]["compiledAotReuse"]["staticSourceUncommittedCandidate"])
        self.assertIsNone(value["execution"]["compiledAotReuse"]["staticSourceRevision"])

    def test_invalid_source_revision_rejected_before_publication_metadata(self):
        with self.assertRaisesRegex(ValueError, "actual Git commit"):
            self.run_refresh("pretend-main")


class CacheReleaseGuardTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path = HERE.parents[1] / ".github/scripts/verified_cache_image.py"
        spec = importlib.util.spec_from_file_location("cache_release", path)
        cls.release = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.release)

    def test_foreign_latest_is_rejected_but_missing_base_or_same_candidate_is_allowed(self):
        module = self.release
        for value in (None, {"digest": module.base.DIGEST}, {"digest": "sha256:" + "a" * 64}):
            with mock.patch.object(module, "manifest", return_value=value):
                self.assertEqual(value, module.latest_guard("sha256:" + "a" * 64))
        with mock.patch.object(module, "manifest", return_value={"digest": "sha256:" + "f" * 64}), \
                self.assertRaisesRegex(ValueError, "Foreign latest"):
            module.latest_guard("sha256:" + "a" * 64)

    def test_new_image_may_not_replace_or_rebuild_the_base_native_layer_graph(self):
        module = self.release
        wrong = {"RootFS": {"Layers": ["sha256:" + "0" * 64] * 14}}
        with self.assertRaisesRegex(ValueError, "two static overlay layers"):
            module.verify_image(lambda *_: json.dumps([wrong]).encode(),
                                "synthetic", "sha256:" + "a" * 64, "b" * 64, "c" * 64, HERE, HERE)

    def test_actual_docker_inspect_legacy_defaults_do_not_hide_nondefault_runtime_changes(self):
        module = self.release
        saved = {"User": "101:101", "Cmd": ["-g", "daemon off;"], "Labels": {"source": "exact"},
                 "Healthcheck": {"Test": ["CMD", "/opt/nflblitz/deploy/healthcheck.sh"]}}
        inspected = dict(saved, AttachStderr=False, AttachStdin=False, AttachStdout=False,
                         Domainname="", Hostname="", Image="", OpenStdin=False,
                         StdinOnce=False, Tty=False, Volumes=None)
        self.assertNotEqual(saved, inspected)
        self.assertEqual(module.canonical_config(saved), module.canonical_config(inspected))
        self.assertEqual(module.canonical_config(dict(saved, Image="sha256:" + "a" * 64)),
                         module.canonical_config(inspected))
        for key, value in (("User", "0:0"), ("Hostname", "foreign"),
                           ("Volumes", {"/unexpected": {}}), ("AttachStdin", True),
                           ("Healthcheck", {"Test": ["NONE"]})):
            self.assertNotEqual(module.canonical_config(saved), module.canonical_config(dict(inspected, **{key: value})))

    def test_buildkit_omitted_onbuild_and_docker28_inspect_null_are_the_same_runtime(self):
        module = self.release
        saved = {"User": "101:101", "Entrypoint": ["/opt/nflblitz/deploy/entrypoint.sh"],
                 "Cmd": ["-g", "daemon off;"]}
        inspected = dict(saved, OnBuild=None)
        self.assertEqual(module.canonical_config(saved), module.canonical_config(inspected))
        self.assertNotEqual(module.canonical_config(saved),
                            module.canonical_config(dict(inspected, OnBuild=["RUN unexpected"])))
        ids = ["sha256:" + format(i, "064x") for i in range(14)]
        config, actual = {"rootfs": {"diff_ids": ids}, "config": saved}, {"RootFS": {"Layers": ids}, "Config": inspected}
        layers = ["layer-" + str(i) for i in range(14)]
        module.verify_exported_metadata(config, actual, layers)
        with self.assertRaisesRegex(ValueError, "changedValueKeys.*User"):
            module.verify_exported_metadata(dict(config, config=dict(saved, User="0:0")), actual, layers)
        with self.assertRaisesRegex(ValueError, "all14 rootfs"):
            module.verify_exported_metadata(dict(config, rootfs={"diff_ids": ids[:-1]}), actual, layers)
        with self.assertRaisesRegex(ValueError, "exactly all14 layer"):
            module.verify_exported_metadata(config, actual, layers[:-1])

    @unittest.skipUnless(os.environ.get("GITHUB_ACTIONS") == "true",
                         "Actual Docker serialization probe is hosted-only; never retry the known local cgroup boundary.")
    def test_actual_hosted_docker_export_inspect_roundtrip_with_asset_free_fourteen_layers(self):
        module = self.release
        root = HERE / (".docker-config-probe-" + secrets.token_hex(8))
        root.mkdir()
        self.addCleanup(shutil.rmtree, root)
        tag = "nflblitz-cache-config-probe:" + root.name.rsplit("-", 1)[1]
        env = dict(os.environ, DOCKER_CONFIG=str(root / "empty-docker-config"))

        def docker(*args):
            return subprocess.check_output(["docker", *args], env=env, stderr=subprocess.STDOUT, timeout=120)

        text = ('FROM scratch\nUSER 101:101\nENV BASE_URL=/\nWORKDIR /\n'
                'LABEL io.nflblitz.probe="asset-free-config-serialization-only"\n'
                'ENTRYPOINT ["/opt/nflblitz/deploy/entrypoint.sh"]\nCMD ["-g","daemon off;"]\n'
                'HEALTHCHECK --interval=10s --timeout=2s --start-period=1s --retries=2 '
                'CMD ["/opt/nflblitz/deploy/healthcheck.sh"]\n')
        for i in range(14):
            name = "marker-" + str(i)
            (root / name).write_text("Asset-free serialization probe " + str(i) + "\n")
            text += "COPY " + name + " /proof/" + name + "\n"
        (root / "Dockerfile").write_text(text)
        created = False
        try:
            docker("build", "--network", "none", "--pull=false", "--tag", tag, str(root))
            created = True
            actual = json.loads(docker("image", "inspect", tag))[0]
            with tarfile.open(fileobj=io.BytesIO(docker("image", "save", tag)), mode="r:*") as stream:
                record = json.load(stream.extractfile("manifest.json"))[0]
                config = json.load(stream.extractfile(record["Config"]))
            module.verify_exported_metadata(config, actual, record["Layers"])
            before_saved = module.canonical_config(config["config"])
            before_actual = module.canonical_config(actual["Config"])
            for value, source in ((before_saved, config["config"]), (before_actual, actual["Config"])):
                if "OnBuild" in source:
                    value["OnBuild"] = source["OnBuild"]
            print(json.dumps({"schema": "actual-asset-free-docker-config-roundtrip/v1",
                              "savedOnBuildPresent": "OnBuild" in config["config"],
                              "inspectOnBuildPresent": "OnBuild" in actual["Config"],
                              "inspectOnBuildValue": actual["Config"].get("OnBuild"),
                              "exactOldGuardWouldReject": before_saved != before_actual,
                              "newProductionMetadataGuardPassed": True, "actualExportedLayerCount": len(record["Layers"]),
                              "gameBytes": 0, "registryWrites": 0}, sort_keys=True), flush=True)
        finally:
            if created:
                docker("image", "rm", "--force", tag)

    def test_published_manifest_cannot_replace_the_original_compressed_layers(self):
        module = self.release
        layers = [{"digest": "sha256:" + format(i, "064x")} for i in range(14)]
        original = {"layers": layers[:12]}
        candidate = {"layers": layers}
        with mock.patch.object(module, "manifest", side_effect=[candidate, original]):
            self.assertEqual(module.candidate_manifest("latest", "unused"), candidate)
        candidate = {"layers": [{"digest": "sha256:" + "f" * 64}] + layers[1:]}
        with mock.patch.object(module, "manifest", side_effect=[candidate, original]), \
                self.assertRaisesRegex(ValueError, "all12 original compressed layers"):
            module.candidate_manifest("latest", "unused")

    def test_alias_race_or_permission_error_can_never_reach_the_fallback_writer(self):
        module = self.release
        candidate = {"digest": "sha256:" + "a" * 64, "config": "sha256:" + "b" * 64}
        old = {"digest": module.base.DIGEST}
        with mock.patch.object(module, "candidate_manifest", return_value=candidate), \
                mock.patch.object(module.base, "public_package"), \
                mock.patch.object(module.base, "registry_token", return_value="not-a-secret"), \
                mock.patch.object(module, "latest_guard", side_effect=[old, None]), \
                mock.patch.object(module, "alias_put") as put, \
                self.assertRaisesRegex(ValueError, "Latest changed"):
            module.promote_candidate(candidate, "not-a-secret", HERE)
        put.assert_not_called()
        for status in (401, 403, 409, 412):
            with self.subTest(status=status), \
                    mock.patch.object(module, "candidate_manifest", return_value=candidate), \
                    mock.patch.object(module.base, "public_package"), \
                    mock.patch.object(module.base, "registry_token", return_value="not-a-secret"), \
                    mock.patch.object(module, "latest_guard", return_value=old), \
                    mock.patch.object(module, "alias_put", side_effect=module.HTTPError(
                        "https://ghcr.io/", status, "denied", {}, None)) as put, \
                    self.assertRaisesRegex(ValueError, "no permission bypass"):
                module.promote_candidate(candidate, "not-a-secret", HERE)
            self.assertEqual(put.call_count, 1)

    def test_transport_fallback_is_the_same_manifest_with_the_original_conditional_guard(self):
        module = self.release
        candidate = {"digest": "sha256:" + "a" * 64, "config": "sha256:" + "b" * 64}
        old = {"digest": module.base.DIGEST}
        with mock.patch.object(module, "candidate_manifest", return_value=candidate), \
                mock.patch.object(module.base, "public_package"), \
                mock.patch.object(module.base, "registry_token", return_value="not-a-secret"), \
                mock.patch.object(module, "latest_guard", return_value=old), \
                mock.patch.object(module, "alias_put", side_effect=[module.URLError("transport"), None]) as put:
            self.assertEqual(module.promote_candidate(candidate, "not-a-secret", HERE),
                             "identical-new-cache-manifest-curl-fallback")
        self.assertEqual(put.call_args_list[0].args, put.call_args_list[1].args)
        self.assertTrue(put.call_args_list[1].kwargs["curl"])
        self.assertEqual(module.alias_headers(old), {"If-Match": '"' + module.base.DIGEST + '"'})
        self.assertEqual(module.alias_headers(None), {"If-None-Match": "*"})

    def test_physical_compiled_source_mutation_stops_release_before_image_work(self):
        module = self.release
        root = HERE / (".release-source-tests-" + secrets.token_hex(6))
        root.mkdir()
        self.addCleanup(shutil.rmtree, root)
        (root / "Core.cs").write_bytes(b"changed")
        value = {"sourceRevision": "a" * 40,
                 "requiredRemoteMainFiles": [{"path": "Core.cs", "sha256": refresh.sha(b"old")}]}
        with mock.patch.object(module, "ROOT", root), \
                mock.patch.object(module.base, "pins", return_value=value), \
                mock.patch.dict(os.environ, {"GITHUB_SHA": "b" * 40}), \
                mock.patch.object(module.subprocess, "check_output", return_value="b" * 40 + "\n"), \
                mock.patch.object(module.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)), \
                self.assertRaisesRegex(ValueError, "C#/native/input/records/project/runtime source"):
            module.source_guard()


if __name__ == "__main__":
    unittest.main()
