"""Focused image-packaging and real HTTP delivery checks; no gameplay claim."""

from concurrent.futures import ThreadPoolExecutor
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import unittest
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
HEADERS = {
    "Cross-Origin-Opener-Policy": "same-origin",
    "Cross-Origin-Embedder-Policy": "require-corp",
    "Cross-Origin-Resource-Policy": "same-origin",
    "X-Content-Type-Options": "nosniff",
    "Referrer-Policy": "no-referrer",
    "Cache-Control": "no-store",
}


class ConfigurationTests(unittest.TestCase):
    def test_compose_runs_only_the_loaded_hardened_image_without_source_mounts(self):
        env = os.environ.copy()
        for name in ("NFLBLITZ_IMAGE", "NFLBLITZ_PLATFORM", "NFLBLITZ_PORT", "NFLBLITZ_BIND_ADDRESS"):
            env.pop(name, None)
        result = subprocess.check_output([
            "docker", "compose", "--env-file", os.devnull, "--project-name", "nflblitz-deployment-check",
            "-f", str(HERE / "compose.yaml"), "config", "--format", "json",
        ], cwd=ROOT, env=env, text=True)
        config = json.loads(result)
        self.assertEqual({"game"}, set(config["services"]))
        service = config["services"]["game"]
        self.assertEqual("nflblitz-original:20261030-original303-amd64", service["image"])
        self.assertEqual("linux/amd64", service["platform"])
        self.assertEqual("never", service["pull_policy"])
        self.assertEqual("101:101", service["user"])
        self.assertIs(service["read_only"], True)
        self.assertEqual(["ALL"], service["cap_drop"])
        self.assertIn("no-new-privileges:true", service["security_opt"])
        self.assertEqual("127.0.0.1", service["ports"][0]["host_ip"])
        self.assertEqual("18098", service["ports"][0]["published"])
        self.assertEqual(8080, service["ports"][0]["target"])
        self.assertEqual(["/var/cache/nginx:rw,nosuid,nodev,noexec,size=16m,uid=101,gid=101,mode=0700"],
                         service["tmpfs"])
        for forbidden in ("volumes", "build", "depends_on", "env_file", "privileged"):
            self.assertNotIn(forbidden, service)

    def test_remote_proxy_compose_binds_the_lan_without_weakening_the_loaded_image(self):
        def compose(file):
            return json.loads(subprocess.check_output([
                "docker", "compose", "--env-file", os.devnull, "--project-name", "nflblitz-subpath-check",
                "-f", str(HERE / file), "config", "--format", "json",
            ], cwd=ROOT, env={key: value for key, value in os.environ.items()
                            if not key.startswith("NFLBLITZ_")}, text=True))
        local = compose("compose.yaml")["services"]["game"]
        remote = compose("compose.lan.yaml")["services"]["game"]
        self.assertEqual("192.168.0.10", remote["ports"][0]["host_ip"])
        self.assertEqual("18098", remote["ports"][0]["published"])
        self.assertEqual(8080, remote["ports"][0]["target"])
        self.assertEqual({key: value for key, value in local.items() if key != "ports"},
                         {key: value for key, value in remote.items() if key != "ports"})

    @unittest.skipUnless(BUNDLE, "Set NFLBLITZ_TEST_BUNDLE to an actual immutable AppBundle.")
    def test_actual_bundle_rejects_missing_changed_extra_and_symlinked_payloads(self):
        bundle = Path(BUNDLE)
        build = json.loads((bundle / "build-info.json").read_bytes())
        build_sha = package.sha(bundle / "build-info.json")
        scratch = ROOT / ".squad/.scratch/docker-delivery-20261030" / ("tests-" + uuid.uuid4().hex)
        try:
            package.stage_bundle(bundle, scratch, build_sha, build["sourceRootDigest"], "0" * 40, True)
            copied = scratch / "AppBundle"
            args = (build_sha, build["sourceRootDigest"])
            package.inspect_bundle(copied, *args)
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


@unittest.skipUnless(URL and RECEIPT and urlsplit(URL).path == "/nflblitz/",
                     "Set the owned /nflblitz/ proxy URL and exact image inventory.")
class SubpathHttpTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        parsed = urlsplit(URL)
        if parsed.scheme != "http" or parsed.hostname != "127.0.0.1" or not parsed.port or parsed.port <= 8097:
            raise ValueError("Subpath checks require an explicitly owned loopback HTTP proxy.")
        cls.origin = f"{parsed.scheme}://{parsed.netloc}"

    def test_slash_redirect_preserves_query_without_replacing_the_root_app(self):
        class NoRedirect(HTTPRedirectHandler):
            def redirect_request(self, request, response, code, message, headers, newurl):
                return None
        for query in ("", "?mode=prefix&encoded=a%2Fb&empty="):
            with self.subTest(query=query):
                with self.assertRaises(HTTPError) as caught:
                    build_opener(NoRedirect()).open(self.origin + "/nflblitz" + query, timeout=10)
                response = caught.exception
                try:
                    self.assertEqual(308, response.code)
                    self.assertEqual("/nflblitz/" + query, response.headers["Location"])
                finally:
                    response.close()
                with urlopen(self.origin + "/nflblitz" + query, timeout=10) as response:
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
