"""Focused image-packaging and real HTTP delivery checks; no gameplay claim."""

from concurrent.futures import ThreadPoolExecutor
import importlib.util
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import unittest
from unittest.mock import patch
from urllib.error import HTTPError
from urllib.parse import quote, urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener, urlopen
import uuid


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
spec = importlib.util.spec_from_file_location("container_package", HERE / "package.py")
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)
URL = os.environ.get("NFLBLITZ_TEST_URL")
RECEIPT = os.environ.get("NFLBLITZ_TEST_INVENTORY")
BUNDLE = os.environ.get("NFLBLITZ_TEST_BUNDLE")
WORKSPACE = Path(os.environ.get("NFLBLITZ_TEST_WORKSPACE", HERE / ".checks"))
HEADERS = {
    "Cross-Origin-Opener-Policy": "same-origin",
    "Cross-Origin-Embedder-Policy": "require-corp",
    "Cross-Origin-Resource-Policy": "same-origin",
    "X-Content-Type-Options": "nosniff",
    "Referrer-Policy": "no-referrer",
    "Cache-Control": "no-store",
}


class ConfigurationTests(unittest.TestCase):
    def setUp(self):
        self.directory = WORKSPACE / ("compose-" + uuid.uuid4().hex)
        self.directory.mkdir(parents=True)
        self.addCleanup(shutil.rmtree, self.directory)
        self.environment = {key: value for key, value in os.environ.items()
                            if not key.startswith("NFLBLITZ_") and key not in ("BASE_URL", "DOCKER_DATA")}
        self.environment["DOCKER_DATA"] = str(self.directory / "docker-data")

    def test_compose_runs_requested_nonroot_config_without_required_tmpfs(self):
        result = subprocess.check_output([
            "docker", "compose", "--env-file", os.devnull, "--project-name", "nflblitz-deployment-check",
            "-f", str(HERE / "compose.yaml"), "config", "--format", "json",
        ], cwd=ROOT, env=self.environment, text=True)
        config = json.loads(result)
        self.assertEqual({"game"}, set(config["services"]))
        service = config["services"]["game"]
        self.assertEqual("ghcr.io/long2know/nflblitz:latest", service["image"])
        self.assertEqual("linux/amd64", service["platform"])
        self.assertEqual("never", service["pull_policy"])
        self.assertEqual("1000:1000", service["user"])
        self.assertIn(service["ports"][0].get("host_ip"), (None, "", "0.0.0.0"))
        self.assertEqual("8920", service["ports"][0]["published"])
        self.assertEqual(8080, service["ports"][0]["target"])
        self.assertEqual({"BASE_URL": "/nflblitz", "PUID": "1000", "PGID": "1000"}, service["environment"])
        self.assertEqual(1, len(service["volumes"]))
        mount = service["volumes"][0]
        self.assertEqual("bind", mount["type"])
        self.assertEqual(str(self.directory / "docker-data/nflblitz"), mount["source"])
        self.assertEqual("/app/data", mount["target"])
        for forbidden in ("build", "depends_on", "env_file", "privileged", "read_only", "tmpfs"):
            self.assertNotIn(forbidden, service)

    def test_remote_proxy_compose_binds_the_lan_without_weakening_the_loaded_image(self):
        def compose(file):
            return json.loads(subprocess.check_output([
                "docker", "compose", "--env-file", os.devnull, "--project-name", "nflblitz-subpath-check",
                "-f", str(HERE / file), "config", "--format", "json",
            ], cwd=ROOT, env=self.environment, text=True))
        local = compose("compose.yaml")["services"]["game"]
        remote = compose("compose.lan.yaml")["services"]["game"]
        self.assertEqual("192.168.0.10", remote["ports"][0]["host_ip"])
        self.assertEqual("8920", remote["ports"][0]["published"])
        self.assertEqual(8080, remote["ports"][0]["target"])
        self.assertEqual({key: value for key, value in local.items() if key != "ports"},
                         {key: value for key, value in remote.items() if key != "ports"})

    def test_compose_keeps_explicit_root_nested_and_invalid_empty_values(self):
        for value in ("/", "/games/nflblitz", ""):
            with self.subTest(value=value):
                env = self.environment.copy()
                env.update(BASE_URL=value, NFLBLITZ_USER="101:101")
                result = subprocess.check_output([
                    "docker", "compose", "--env-file", os.devnull, "--project-name", "nflblitz-runtime-check",
                    "-f", str(HERE / "compose.yaml"), "config", "--format", "json",
                ], cwd=ROOT, env=env, text=True)
                service = json.loads(result)["services"]["game"]
                self.assertEqual(value, service["environment"]["BASE_URL"])
                self.assertEqual("101:101", service["user"])

    @unittest.skipUnless(BUNDLE, "Set NFLBLITZ_TEST_BUNDLE to an actual immutable AppBundle.")
    def test_actual_bundle_rejects_missing_changed_extra_and_symlinked_payloads(self):
        bundle = Path(BUNDLE)
        build = json.loads((bundle / "build-info.json").read_bytes())
        build_sha = package.sha(bundle / "build-info.json")
        scratch = WORKSPACE / ("bundle-" + uuid.uuid4().hex)
        try:
            mask = os.umask(0o077)
            try:
                staged = package.stage_bundle(bundle, scratch, build_sha, build["sourceRootDigest"], "0" * 40, True)
            finally:
                os.umask(mask)
            copied = scratch / "AppBundle"
            args = (build_sha, build["sourceRootDigest"])
            package.inspect_bundle(copied, *args)
            self.assertEqual(build["factory"], staged["factory"])
            for path in [copied, *copied.rglob("*")]:
                self.assertEqual(0o755 if path.is_dir() else 0o444, stat.S_IMODE(path.stat().st_mode),
                                 "Root-owned COPY must remain readable/traversable by any UID: " + str(path))
            for name in ("nginx.conf", "bundle-inventory.json", "deployment.json"):
                self.assertEqual(0o444, stat.S_IMODE((scratch / name).stat().st_mode), name)
            if build["profile"] in package.AV306_PROFILES:
                receipt_path = copied / "build-info.json"
                original_receipt = receipt_path.read_bytes()
                receipt_path.chmod(0o644)
                try:
                    for field, value, message in (
                        ("factory", package.FACTORY, "profile/factory/manifest"),
                        ("profile", package.SELECTED_PROFILE, "profile/factory/manifest"),
                        ("callerProfile", package.CALLER_PROFILE, "rooted-AOT"),
                        ("mode", "interpreter", "rooted-AOT"),
                        ("publishTrimmed", False, "rooted-AOT"),
                    ):
                        with self.subTest(field=field):
                            altered = json.loads(original_receipt)
                            target = altered if field in ("factory", "profile") else altered["execution"]
                            target[field] = value
                            receipt_path.write_text(json.dumps(altered))
                            with self.assertRaisesRegex(ValueError, message):
                                package.inspect_bundle(copied, package.sha(receipt_path), build["sourceRootDigest"])
                finally:
                    receipt_path.write_bytes(original_receipt)
                    receipt_path.chmod(0o444)
            manifest = json.loads((copied / "data/manifest.json").read_bytes())
            target = copied / "data" / next(entry["path"] for entry in manifest["files"] if entry["path"].endswith(".saud"))
            original = target.read_bytes()
            target.chmod(0o644)
            target.write_bytes(original[:-1] + bytes([original[-1] ^ 1]))
            with self.assertRaisesRegex(ValueError, "Missing/changed original payload"):
                package.inspect_bundle(copied, *args)
            target.write_bytes(original)
            target.unlink()
            with self.assertRaisesRegex(ValueError, "Missing/changed original payload"):
                package.inspect_bundle(copied, *args)
            target.write_bytes(original)
            extra = copied / "data/extra-asset.bin"
            extra.write_bytes(b"unexpected")
            with self.assertRaisesRegex(ValueError, "extra or missing"):
                package.inspect_bundle(copied, *args)
            extra.unlink()
            extra.symlink_to(target)
            with self.assertRaisesRegex(ValueError, "Symlinked bundle input"):
                package.inspect_bundle(copied, *args)
            extra.unlink()
            core = copied / "_framework/SourcePlayer.Core.wasm"
            core.unlink()
            with self.assertRaisesRegex(ValueError, "Compiled assembly identity changed"):
                package.inspect_bundle(copied, *args)
        finally:
            if scratch.exists():
                shutil.rmtree(scratch)


class RuntimeBaseUrlTests(unittest.TestCase):
    def setUp(self):
        self.directory = WORKSPACE / ("base-url-" + uuid.uuid4().hex)
        self.directory.mkdir(parents=True)
        self.addCleanup(shutil.rmtree, self.directory)

    def shell(self, base, *, render=False, template=None):
        env = os.environ.copy()
        env.pop("BASE_URL", None)
        if base is not None:
            env["BASE_URL"] = base
        output = self.directory / ("generated-" + uuid.uuid4().hex + ".conf")
        command = '. "$1"; render_nginx_config "$2" "$3"' if render else '. "$1"; normalize_base_url'
        result = subprocess.run(
            ["/bin/sh", "-eu", "-c", command, "base-url-test",
             str(HERE / "runtime-common.sh"), str(template or HERE / "nginx.conf"), str(output)],
            # The output stays on owned storage; no host runtime directory is created.
            env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        return result, output

    def test_default_and_absolute_nested_prefix_normalization(self):
        for value, expected in ((None, "/"), ("/", "/"), ("/nflblitz", "/nflblitz/"),
                                ("/nflblitz/", "/nflblitz/"),
                                ("/games/nflblitz", "/games/nflblitz/"),
                                ("/games/v1.2-game/", "/games/v1.2-game/")):
            with self.subTest(value=value):
                result, _ = self.shell(value)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual(expected, result.stdout.strip())

    def test_unsafe_base_values_fail_before_any_config_write(self):
        for value in ("", "nflblitz", "//nflblitz", "/a//b", "/a/..", "/a/../b", "/a/./b",
                      "/.hidden", "/a?query=1", "/a#fragment", "/a%2fb", "/a\\b", "/a\nb",
                      '/a";return 200;#', "/$uri", "/a { }", "/a\rb", "/a\tb",
                      "https://example.invalid/game", "/a\n"):
            with self.subTest(value=value):
                result, output = self.shell(value, render=True)
                self.assertNotEqual(0, result.returncode)
                self.assertIn("Invalid BASE_URL", result.stderr)
                self.assertFalse(output.exists(), "Invalid input must never write even a partial config.")

    def test_root_routes_unchanged_and_prefix_stripped_exactly_once(self):
        result, output = self.shell("/", render=True)
        self.assertEqual(0, result.returncode, result.stderr)
        expected = (HERE / "nginx.conf").read_text().replace("@RUNTIME_DIR@", f"/tmp/nflblitz-{os.getuid()}")
        self.assertEqual(expected, output.read_text())
        result, output = self.shell("/games/v1.2-game", render=True)
        self.assertEqual(0, result.returncode, result.stderr)
        text = output.read_text()
        self.assertIn("location = /games/v1.2-game {", text)
        self.assertIn("return 308 /games/v1.2-game/$is_args$args;", text)
        self.assertIn(r"rewrite ^/games/v1\.2-game/(.*)$ /$1 break;", text)
        self.assertIn("location / { return 404; }", text)
        self.assertNotIn("try_files $uri /index.html", text)
        for name, value in HEADERS.items():
            self.assertIn("add_header " + name + " " + value + " always;", text)

    def test_mutated_runtime_template_is_rejected_before_output(self):
        template = self.directory / "damaged-nginx.conf"
        template.write_text((HERE / "nginx.conf").read_text().replace("# BASE_URL_BEGIN", "# BROKEN_BEGIN"))
        result, output = self.shell("/games/nflblitz", render=True, template=template)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Invalid nginx runtime template", result.stderr)
        self.assertFalse(output.exists())

    def test_private_runtime_directory_reuse_and_collision_rejection(self):
        def prepare(path):
            return subprocess.run([
                "/bin/sh", "-eu", "-c", '. "$1"; prepare_runtime_directory "$2"', "runtime-directory-test",
                str(HERE / "runtime-common.sh"), str(path),
            ], text=True, capture_output=True)
        private = self.directory / "private"
        for _ in range(2):
            result = prepare(private)
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual(os.getuid(), private.stat().st_uid)
            self.assertEqual(0o700, stat.S_IMODE(private.stat().st_mode))
        symlink = self.directory / "symlink"
        symlink.symlink_to(private, target_is_directory=True)
        regular = self.directory / "regular"
        regular.write_text("not a directory")
        exposed = self.directory / "exposed"
        exposed.mkdir(mode=0o755)
        for path in (symlink, regular, exposed):
            with self.subTest(path=path.name):
                result = prepare(path)
                self.assertNotEqual(0, result.returncode)
                self.assertIn("Unsafe nginx runtime directory", result.stderr)
        self.assertEqual("not a directory", regular.read_text())

    def test_staged_runtime_modes_and_inventory_with_private_source_files(self):
        source, context = self.directory / "source", self.directory / "context"
        source.mkdir()
        context.mkdir()
        for name in (*package.DEPLOYMENT_FILES, ".dockerignore"):
            shutil.copyfile(HERE / name, source / name)
            (source / name).chmod(0o400)
        mask = os.umask(0o077)
        try:
            with patch.object(package, "HERE", source):
                digest = package.stage_deployment(context)
        finally:
            os.umask(mask)
        manifest = json.loads((context / "deployment.json").read_bytes())
        self.assertEqual(package.sha(context / "deployment.json"), digest)
        self.assertEqual(set(package.DEPLOYMENT_FILES) | {".dockerignore", "deployment.json"},
                         {path.name for path in context.iterdir()})
        self.assertEqual(set(package.DEPLOYMENT_FILES), {entry["path"] for entry in manifest["files"]})
        self.assertEqual("/tmp/nflblitz-${uid}/nginx.conf", manifest["generatedConfig"])
        self.assertEqual("/tmp", manifest["runtimeWritablePath"])
        self.assertEqual("0700", manifest["runtimeDirectoryMode"])
        self.assertIs(manifest["readOnlyRootRequired"], False)
        self.assertEqual([], manifest["requiredRuntimeMounts"])
        for entry in manifest["files"]:
            name = entry["path"]
            mode = 0o555 if name.endswith(".sh") else 0o444
            self.assertEqual(f"{mode:04o}", entry["mode"])
            self.assertEqual(package.DEPLOYMENT_FILES[name], entry["imagePath"])
            self.assertEqual(mode, stat.S_IMODE((context / name).stat().st_mode), name)
            self.assertEqual((HERE / name).read_bytes(), (context / name).read_bytes(), name)
            self.assertEqual(package.sha(context / name), entry["sha256"], name)
        self.assertEqual(0o444, stat.S_IMODE((context / "deployment.json").stat().st_mode))


@unittest.skipUnless(URL and RECEIPT, "Set NFLBLITZ_TEST_URL and NFLBLITZ_TEST_INVENTORY for actual image checks.")
class ImageHttpTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        parsed = urlsplit(URL)
        if parsed.scheme != "http" or parsed.hostname != "127.0.0.1" or not parsed.port or parsed.port <= 8097:
            raise ValueError("Image checks require an explicitly owned loopback port above8097.")
        cls.receipt = json.loads(Path(RECEIPT).read_bytes())

    def request(self, path, headers=None, method="GET"):
        request = Request(URL.rstrip("/") + path, headers=headers or {}, method=method)
        try:
            response = urlopen(request, timeout=20)
        except HTTPError as error:
            response = error
        with response:
            body = response.read()
            for name, value in HEADERS.items():
                self.assertEqual(value, response.headers.get(name), f"{path}: {name}")
            return response.status, response.headers, body

    def test_every_embedded_runtime_and_original_payload_matches_the_bundle(self):
        def verify(entry):
            status, headers, body = self.request("/" + quote(entry["path"]))
            self.assertEqual(200, status, entry["path"])
            self.assertEqual(entry["bytes"], len(body), entry["path"])
            import hashlib
            self.assertEqual(entry["sha256"], hashlib.sha256(body).hexdigest(), entry["path"])
            suffix = Path(entry["path"]).suffix.lower()
            expected = {".wasm": "application/wasm", ".js": "text/javascript", ".json": "application/json",
                        ".html": "text/html", ".saud": "application/octet-stream"}.get(suffix)
            if expected:
                self.assertEqual(expected, headers.get_content_type(), entry["path"])
        with ThreadPoolExecutor(max_workers=6) as pool:
            list(pool.map(verify, self.receipt["files"]))

    def test_root_security_error_and_cache_contract(self):
        status, _, body = self.request("/")
        self.assertEqual(200, status)
        self.assertIn(b"start-original-audio", body)
        self.assertIn(b"records-export", body)
        status, _, body = self.request("/favicon.ico")
        self.assertEqual(204, status)
        self.assertEqual(b"", body)
        for path in ("/_framework/missing.wasm", "/data/missing.json", "/missing-route", "/50x.html",
                     "/.stamp", "/.hidden-probe.txt", "/data/.hidden-probe.txt", "/%2ehidden-probe.txt"):
            with self.subTest(path=path):
                status, _, body = self.request(path)
                self.assertEqual(404, status, path)
                self.assertNotIn(b"start-original-audio", body, path)
        status, headers, body = self.request("/_framework/SourcePlayer.Core.wasm", {"Range": "bytes=0-3"})
        self.assertEqual(206, status)
        self.assertEqual(b"\0asm", body)
        self.assertTrue(headers["Content-Range"].startswith("bytes 0-3/"))
        status, headers, body = self.request("/main.js", method="HEAD")
        self.assertEqual(200, status)
        self.assertEqual(b"", body)
        self.assertEqual("text/javascript", headers.get_content_type())
        self.assertEqual(str(next(entry["bytes"] for entry in self.receipt["files"]
                                  if entry["path"] == "main.js")), headers["Content-Length"])
        status, _, _ = self.request("/main.js", method="POST")
        self.assertIn(status, (403, 405), "Only GET and HEAD may read game resources.")

    def test_configured_prefix_redirect_and_no_root_or_double_prefix_escape(self):
        parsed = urlsplit(URL)
        prefix = parsed.path.rstrip("/") + "/"
        if prefix == "/":
            self.skipTest("Root deployment has no slashless non-root prefix.")
        origin = f"{parsed.scheme}://{parsed.netloc}"
        class NoRedirect(HTTPRedirectHandler):
            def redirect_request(self, request, response, code, message, headers, newurl):
                return None
        for query in ("", "?keep=a%2Fb&empty="):
            with self.subTest(query=query), self.assertRaises(HTTPError) as caught:
                build_opener(NoRedirect()).open(origin + prefix.rstrip("/") + query, timeout=10)
            response = caught.exception
            try:
                self.assertEqual(308, response.code)
                self.assertEqual(prefix + query, response.headers["Location"])
            finally:
                response.close()
        for path in ("/", "/main.js", "/data/manifest.json", "/_framework/SourcePlayer.Core.wasm",
                     "/unrelated-game/", prefix + prefix.lstrip("/") + "main.js"):
            with self.subTest(path=path), self.assertRaises(HTTPError) as caught:
                urlopen(origin + path, timeout=10)
            response = caught.exception
            try:
                self.assertEqual(404, response.code)
                self.assertNotIn(b"start-original-audio", response.read())
            finally:
                response.close()


@unittest.skipUnless(URL and RECEIPT and urlsplit(URL).path not in ("", "/"),
                     "Set an owned prefixed proxy URL and exact image inventory.")
class SubpathHttpTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        parsed = urlsplit(URL)
        if parsed.scheme != "http" or parsed.hostname != "127.0.0.1" or not parsed.port or parsed.port <= 8097:
            raise ValueError("Subpath checks require an explicitly owned loopback HTTP proxy.")
        cls.origin = f"{parsed.scheme}://{parsed.netloc}"
        cls.prefix = parsed.path.rstrip("/") + "/"

    def test_slash_redirect_preserves_query_without_replacing_the_root_app(self):
        class NoRedirect(HTTPRedirectHandler):
            def redirect_request(self, request, response, code, message, headers, newurl):
                return None
        for query in ("", "?mode=prefix&encoded=a%2Fb&empty="):
            with self.subTest(query=query):
                with self.assertRaises(HTTPError) as caught:
                    build_opener(NoRedirect()).open(self.origin + self.prefix.rstrip("/") + query, timeout=10)
                response = caught.exception
                try:
                    self.assertEqual(308, response.code)
                    self.assertEqual(self.prefix + query, response.headers["Location"])
                finally:
                    response.close()
                with urlopen(self.origin + self.prefix.rstrip("/") + query, timeout=10) as response:
                    self.assertEqual(200, response.status)
                    self.assertEqual(URL + query, response.geturl())
                    self.assertIn(b"start-original-audio", response.read())

    def test_other_root_routes_remain_with_the_existing_app(self):
        for path in ("/", "/existing/deep-link?keep=query", "/_framework/root-app.wasm",
                     "/data/root-app.json", "/nflblitz-other", "/nflblitz.js"):
            with self.subTest(path=path), urlopen(self.origin + path, timeout=10) as response:
                self.assertEqual(200, response.status)
                self.assertEqual(("existing-root-app:" + path + "\n").encode(), response.read())
                self.assertIsNone(response.headers.get("Cross-Origin-Embedder-Policy"),
                                  "Game-only isolation must not leak into the existing root app.")

    def test_imported_modules_and_original_data_keep_their_exact_mime_and_bytes(self):
        receipt = json.loads(Path(RECEIPT).read_bytes())
        paths = ["_framework/dotnet.js", "_framework/dotnet.boot.js", "_framework/dotnet.native.js",
                 "_framework/dotnet.runtime.js", "renderer.js", "input.js", "audio.js", "records.js",
                 "build-info.json", "data/manifest.json"]
        paths += [next(entry["path"] for entry in receipt["files"] if entry["path"].endswith(".saud"))]
        entries = {entry["path"]: entry for entry in receipt["files"]}
        import hashlib
        for path in paths:
            with self.subTest(path=path), urlopen(URL + quote(path), timeout=20) as response:
                body = response.read()
                self.assertEqual(200, response.status)
                self.assertEqual(entries[path]["sha256"], hashlib.sha256(body).hexdigest(), path)
                self.assertEqual(entries[path]["bytes"], len(body), path)
                expected = "text/javascript" if path.endswith(".js") else (
                    "application/json" if path.endswith(".json") else "application/octet-stream")
                self.assertEqual(expected, response.headers.get_content_type(), path)
                for name, value in HEADERS.items():
                    self.assertEqual(value, response.headers.get(name), f"{path}: {name}")


if __name__ == "__main__":
    unittest.main()
