#!/usr/bin/env python3
"""Contract and optional Docker smoke tests; all players here are labeled stubs."""

import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import unittest
import urllib.error
import urllib.request
import uuid


ROOT = Path(__file__).resolve().parent.parent
WITH_DOCKER = "--docker" in sys.argv
if WITH_DOCKER:
    sys.argv.remove("--docker")


def run(args, **kwargs):
    return subprocess.run(args, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, **kwargs)


def write_generation_proof(site, build_id="1" * 32):
    artifacts = [site / "index.html"]
    for suffix in ("loader.js", "framework.js", "wasm", "data"):
        artifacts.append(next((site / "Build").glob("*." + suffix)))
    lines = [
        "format=blitz-webgl-build-v1", "build-id=" + build_id,
        "unity-version=6000.3.24f1", "build-target=WebGL",
        "build-result=Succeeded", "total-errors=0",
        "build-report-bytes=" + str(sum(path.stat().st_size for path in artifacts)),
    ]
    for path in artifacts:
        lines.append("sha256=" + hashlib.sha256(path.read_bytes()).hexdigest() +
                     " " + path.relative_to(site).as_posix())
    (site / "build-generation.txt").write_text("\n".join(lines) + "\n")


def create_site(site, hosting_stub=True):
    (site / "Build").mkdir(parents=True)
    (site / "TemplateData").mkdir()
    (site / "TemplateData/webmemd-icon.png").write_bytes(base64.b64decode(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jCuMAAAAASUVORK5CYII="
    ))
    if hosting_stub:
        (site / "hosting-test-stub.txt").write_text("HOSTING TEST ONLY, NOT A PLAYABLE GAME\n")
    label = "WEBGL_HOSTING_TEST_STUB" if hosting_stub else "SYNTHETIC_BUILD_CONTRACT_FIXTURE"
    (site / "index.html").write_text(
        '<!doctype html><html><head><meta charset="utf-8"><title>Hosting test only</title></head><body>'
        '<p>' + label + ': NOT A PLAYABLE GAME</p>'
        '<canvas id="unity-canvas" width="1280" height="720" style="width:1280px;height:720px"></canvas>'
        '<script src="Build/smoke.loader.js"></script>'
        '<script>const config = {frameworkUrl:"Build/smoke.framework.js",'
        'codeUrl:"Build/smoke.wasm",dataUrl:"Build/smoke.data"};</script></body></html>\n'
    )
    (site / "Build/smoke.loader.js").write_text(
        "// HOSTING STUB ONLY\nfunction createUnityInstance() { throw new Error('Not a game'); }\n"
    )
    (site / "Build/smoke.framework.js").write_text("// HOSTING STUB ONLY\n")
    (site / "Build/smoke.wasm").write_bytes(b"\0asm\x01\0\0\0")
    (site / "Build/smoke.data").write_bytes(b"HOSTING STUB ONLY, NOT UNITY GAME DATA\n")
    write_generation_proof(site)


def assert_full_viewport_style(test, source):
    match = re.search(r'<style id="webgl-responsive">([^<]+)</style>', source)
    test.assertIsNotNone(match, "the served page must contain the actual responsive host style")
    rules = dict(re.findall(r"([^{}]+)\{([^{}]*)\}", match[1].strip()))
    rules = {selector.strip(): declarations for selector, declarations in rules.items()}
    test.assertEqual(set(rules), {"html, body", "#unity-canvas"},
                     "full-window styling must be unconditional and focus suppression canvas-only")

    def properties(selector):
        result = {}
        for declaration in rules[selector].split(";"):
            if declaration.strip():
                name, value = declaration.split(":", 1)
                result.setdefault(name.strip(), []).append(value.strip())
        return result

    document = properties("html, body")
    for name, value in (("width", "100%"), ("height", "100%"), ("margin", "0"),
                        ("padding", "0"), ("border", "0"), ("overflow", "hidden")):
        test.assertEqual(document.get(name), [value], "document " + name)
    canvas = properties("#unity-canvas")
    for name, values in {
        "display": ["block"], "position": ["static !important"],
        "width": ["100vw !important"],
        "height": ["100vh !important", "100dvh !important"],
        "max-width": ["none !important"], "max-height": ["none !important"],
        "margin": ["0"], "padding": ["0"], "border": ["0"], "outline": ["none"], "outline-width": ["0"],
    }.items():
        test.assertEqual(canvas.get(name), values, "canvas " + name)


class WorkDirectoryTest(unittest.TestCase):
    def setUp(self):
        if (ROOT / "Build").is_symlink():
            raise RuntimeError("Build must not be a symlink; generated test files stay in this checkout.")
        self.work = ROOT / "Build" / ("tooling-test-" + uuid.uuid4().hex)
        self.work.mkdir(parents=True)

    def tearDown(self):
        shutil.rmtree(self.work)


class ViewportStyleTests(WorkDirectoryTest):
    def test_real_host_style_fills_every_viewport_without_outer_borders(self):
        assert_full_viewport_style(self, (ROOT / "hosting/nginx.conf").read_text())

    def test_real_host_style_mutations_are_rejected(self):
        original = (ROOT / "hosting/nginx.conf").read_text()
        for label, old, new in (
            ("missing-full-width", "width: 100vw !important;", ""),
            ("restored-desktop-cap", "width: 100vw !important;",
             "width: min(100vw, calc(100vh * 16 / 9), 1280px) !important;"),
            ("missing-full-height", "height: 100vh !important;\n    height: 100dvh !important;", ""),
            ("restored-letterboxing", "height: 100dvh !important;",
             "height: min(100dvh, calc(100vw * 9 / 16), 720px) !important;"),
            ("restored-focus-border", "outline: none;", "outline: auto;"),
        ):
            with self.subTest(mutation=label):
                self.assertIn(old, original, "mutation must change the real hosting config")
                fixture = self.work / (label + ".conf")
                fixture.write_text(original.replace(old, new))
                with self.assertRaises(AssertionError, msg=label + " incorrectly passed the viewport gate"):
                    assert_full_viewport_style(self, fixture.read_text())


class ArtifactContractTests(WorkDirectoryTest):
    def setUp(self):
        super().setUp()
        self.site = self.work / "WebGL"
        create_site(self.site)

    def verify(self, allow=True, expected_id=None):
        env = dict(os.environ, WEBGL_ALLOW_TEST_STUB="1" if allow else "0")
        args = ["sh", str(ROOT / "scripts/verify-webgl.sh"), str(self.site)]
        if expected_id is not None:
            args.append(expected_id)
        return run(args, env=env)

    def test_complete_labeled_stub_requires_explicit_opt_in(self):
        self.assertEqual(self.verify().returncode, 0)
        rejected = self.verify(allow=False)
        self.assertNotEqual(rejected.returncode, 0)
        self.assertIn("hosting-only test stub is not a Unity player", rejected.stdout)

    def test_each_required_artifact_must_be_nonempty_and_unique(self):
        for suffix in ("loader.js", "framework.js", "wasm", "data"):
            with self.subTest(suffix=suffix):
                path = self.site / ("Build/smoke." + suffix)
                original = path.read_bytes()
                path.write_bytes(b"")
                result = self.verify()
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("Build/*." + suffix, result.stdout)
                path.write_bytes(original)
                duplicate = self.site / ("Build/duplicate." + suffix)
                duplicate.write_bytes(original)
                self.assertNotEqual(self.verify().returncode, 0)
                duplicate.unlink()

    def test_mutating_real_wasm_bytes_is_rejected(self):
        (self.site / "Build/smoke.wasm").write_bytes(b"not wasm")
        result = self.verify()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("invalid WebAssembly magic/version", result.stdout)

    def test_index_must_reference_real_artifacts(self):
        index = self.site / "index.html"
        index.write_text(index.read_text().replace("smoke.data", "missing.data"))
        result = self.verify()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("index.html does not reference Build/smoke.data", result.stdout)

    def test_symlinks_and_compressed_output_are_rejected(self):
        for name in ("unexpected.gz", "unexpected.br", "unexpected.unityweb"):
            with self.subTest(name=name):
                path = self.site / name
                path.write_bytes(b"compressed-contract-canary")
                self.assertNotEqual(self.verify().returncode, 0)
                path.unlink()
        link = self.site / "linked-artifact"
        link.symlink_to(self.site / "Build/smoke.wasm")
        self.assertNotEqual(self.verify().returncode, 0)

    def test_cached_old_mtimes_pass_only_with_current_generation_proof(self):
        for path in self.site.rglob("*"):
            if path.is_file():
                os.utime(path, (1, 1))
        self.assertEqual(self.verify(expected_id="1" * 32).returncode, 0)
        result = self.verify(expected_id="2" * 32)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("stale output is not a successful build", result.stdout)
        (self.site / "index.html").touch()
        self.assertNotEqual(self.verify(expected_id="2" * 32).returncode, 0)

    def test_missing_failed_or_malformed_build_report_proof_is_rejected(self):
        proof = self.site / "build-generation.txt"
        original = proof.read_text()
        proof.unlink()
        self.assertIn("missing current BuildReport generation proof", self.verify().stdout)
        for malformed in (
            original.replace("build-result=Succeeded", "build-result=Failed"),
            original.replace("total-errors=0", "total-errors=1"),
            original.replace("build-report-bytes=", "not-a-report="),
            original.replace("build-report-bytes=" + original.split("build-report-bytes=")[1].splitlines()[0],
                             "build-report-bytes=0"),
            original + "sha256=" + "0" * 64 + " ../../outside-data\n",
        ):
            with self.subTest(proof=malformed[:80]):
                proof.write_text(malformed)
                self.assertNotEqual(self.verify().returncode, 0)

    def test_current_proof_detects_nonempty_payload_mutation(self):
        data = self.site / "Build/smoke.data"
        data.write_bytes(data.read_bytes() + b"mutation")
        result = self.verify()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("generation hash mismatch: Build/smoke.data", result.stdout)

    def test_linked_output_or_parent_cannot_escape_generation(self):
        alias = self.work / "alias"
        alias.symlink_to(self.site, target_is_directory=True)
        result = run(["sh", str(ROOT / "scripts/verify-webgl.sh"), str(alias)])
        self.assertNotEqual(result.returncode, 0)
        parent = self.work / "parent"
        parent.symlink_to(self.work, target_is_directory=True)
        result = run(["sh", str(ROOT / "scripts/verify-webgl.sh"), str(parent / "WebGL")])
        self.assertNotEqual(result.returncode, 0)


class BuildEntrypointTests(WorkDirectoryTest):
    def setUp(self):
        super().setUp()
        self.project = self.work / "project"
        for directory in ("scripts", "Assets", "Packages", "ProjectSettings"):
            (self.project / directory).mkdir(parents=True)
        for name in ("build-webgl.sh", "verify-webgl.sh"):
            shutil.copy2(ROOT / "scripts" / name, self.project / "scripts" / name)
        (self.project / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 6000.3.24f1\n")
        self.editor = self.work / "Editor/Unity"
        (self.editor.parent / "Data/PlaybackEngines/WebGLSupport").mkdir(parents=True)
        self.editor.write_text(
            "#!/usr/bin/env python3\n"
            "import os, runpy, shutil, sys\nfrom pathlib import Path\n"
            "if sys.argv[1:] == ['-version']:\n"
            " print(os.environ.get('TEST_UNITY_VERSION','6000.3.24f1')); sys.exit(0)\n"
            "Path(os.environ['TEST_INVOCATION']).write_text('\\n'.join(sys.argv[1:])+'\\n')\n"
            "if os.environ.get('TEST_FIXTURE'):\n"
            " output=Path(sys.argv[sys.argv.index('-projectPath')+1])/'Build/WebGL'\n"
            " shutil.copytree(os.environ['TEST_FIXTURE'],output,dirs_exist_ok=True)\n"
            " if os.environ.get('TEST_ATTEST') == '1':\n"
            "  runpy.run_path(os.environ['TEST_HELPER'])['write_generation_proof'](output,os.environ['BLITZ_WEBGL_BUILD_ID'])\n"
            "sys.exit(int(os.environ.get('TEST_UNITY_EXIT','0')))\n"
        )
        self.editor.chmod(0o755)
        self.invocation = self.work / "editor-arguments.txt"
        self.env = dict(os.environ, UNITY_EDITOR=str(self.editor), TEST_INVOCATION=str(self.invocation),
                        TEST_HELPER=str(ROOT / "scripts/test-webgl-tooling.py"))

    def build(self, **env):
        return run(
            ["bash", str(self.project / "scripts/build-webgl.sh"), "--host"],
            env=dict(self.env, **env),
            cwd=ROOT / "scripts",
        )

    def test_editor_failure_propagates_without_fallback_and_uses_exact_flags(self):
        result = self.build(TEST_UNITY_EXIT="23")
        self.assertEqual(result.returncode, 23, result.stdout)
        self.assertIn("Inspect private Logs/webgl-build.log", result.stdout)
        self.assertEqual(self.invocation.read_text().splitlines(), [
            "-batchmode", "-nographics", "-quit", "-projectPath", str(self.project),
            "-executeMethod", "Blitz.Editor.WebGlBuild.Build", "-buildTarget", "WebGL",
            "-logFile", str(self.project / "Logs/webgl-build.log"),
        ])
        self.assertFalse((self.project / "Build/WebGL/build-receipt.json").exists())

    def test_zero_editor_exit_with_stale_output_still_fails(self):
        site = self.project / "Build/WebGL"
        create_site(site)
        receipt = site / "build-receipt.json"
        receipt.write_text('{"obsolete":true}\n')
        os.utime(site / "index.html", (1, 1))
        result = self.build()
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(receipt.exists())
        archived = list((self.project / "Build").glob(".webgl-previous-*/build-receipt.json"))
        self.assertEqual(len(archived), 1)
        self.assertEqual(archived[0].read_text(), '{"obsolete":true}\n')
        self.assertFalse((site / "index.html").exists())

    def test_cached_payload_copy_with_current_report_passes_without_touching_mtimes(self):
        fixture = self.work / "cached-export"
        create_site(fixture, hosting_stub=False)
        for path in fixture.rglob("*"):
            if path.is_file():
                os.utime(path, (1, 1))
        result = self.build(TEST_FIXTURE=str(fixture), TEST_ATTEST="1")
        self.assertEqual(result.returncode, 0, result.stdout)
        site = self.project / "Build/WebGL"
        self.assertEqual((site / "index.html").stat().st_mtime, 1)
        self.assertEqual((site / "Build/smoke.wasm").stat().st_mtime, 1)
        receipt = json.loads((site / "build-receipt.json").read_text())
        self.assertEqual(receipt["schemaVersion"], 2)
        self.assertIn("build-id=" + receipt["buildId"], (site / "build-generation.txt").read_text())

    def test_zero_exit_replayed_old_report_or_incomplete_output_cannot_pass(self):
        fixture = self.work / "cached-export"
        create_site(fixture, hosting_stub=False)
        replay = self.build(TEST_FIXTURE=str(fixture))
        self.assertNotEqual(replay.returncode, 0)
        self.assertIn("build invocation does not match", replay.stdout)
        self.assertFalse((self.project / "Build/WebGL/build-generation.txt").exists())
        (fixture / "Build/smoke.data").unlink()
        partial = self.build(TEST_FIXTURE=str(fixture))
        self.assertNotEqual(partial.returncode, 0)
        self.assertIn("Build/*.data", partial.stdout)
        self.assertFalse((self.project / "Build/WebGL/build-receipt.json").exists())

    def test_editor_failure_invalidates_even_a_current_generation(self):
        fixture = self.work / "cached-export"
        create_site(fixture, hosting_stub=False)
        result = self.build(TEST_FIXTURE=str(fixture), TEST_ATTEST="1", TEST_UNITY_EXIT="23")
        self.assertEqual(result.returncode, 23)
        self.assertFalse((self.project / "Build/WebGL/build-generation.txt").exists())
        self.assertFalse((self.project / "Build/WebGL/build-receipt.json").exists())

    def test_output_and_build_symlinks_rejected_without_touching_targets(self):
        outside = self.work / "untouched"
        outside.mkdir()
        sentinel = outside / "sentinel.txt"
        sentinel.write_text("unchanged")
        (self.project / "Build").mkdir()
        (self.project / "Build/WebGL").symlink_to(outside, target_is_directory=True)
        self.assertNotEqual(self.build().returncode, 0)
        self.assertFalse(self.invocation.exists())
        (self.project / "Build/WebGL").unlink()
        shutil.rmtree(self.project / "Build")
        (self.project / "Build").symlink_to(outside, target_is_directory=True)
        self.assertNotEqual(self.build().returncode, 0)
        self.assertFalse(self.invocation.exists())
        self.assertEqual(list(outside.iterdir()), [sentinel])
        self.assertEqual(sentinel.read_text(), "unchanged")

    def test_exact_editor_version_is_enforced(self):
        result = self.build(TEST_UNITY_VERSION="6000.3.0f1")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("editor version mismatch", result.stdout)
        self.assertFalse(self.invocation.exists())

    def docker_build(self, **env):
        tools = self.work / "tools"
        tools.mkdir(exist_ok=True)
        docker = tools / "docker"
        docker.write_text(
            '#!/bin/sh\ncase "$1" in info|image) exit 0;; esac\n'
            'for arg do if [ "$arg" = -c ]; then echo 6000.3.24f1; exit 0; fi; done\n'
            'printf "%s\\n" "$@" > "$TEST_INVOCATION"\nexit 37\n'
        )
        docker.chmod(0o755)
        return run(
            ["bash", str(self.project / "scripts/build-webgl.sh")],
            env=dict(self.env, PATH=str(tools) + os.pathsep + os.environ["PATH"],
                     **dict({"UNITY_LICENSE_FILE": "", "UNITY_LICENSE_DIR": ""}, **env)),
        )

    def test_docker_requires_external_activation_without_reading_or_copying_it(self):
        result = self.docker_build()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Docker supplies tools, not entitlement", result.stdout)
        private = self.project / "private.ulf"
        private.write_text("SYNTHETIC_TEST_PLACEHOLDER_NOT_A_LICENSE\n")
        result = self.docker_build(UNITY_LICENSE_FILE=str(private))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("licensing must remain outside the checkout", result.stdout)
        self.assertFalse(self.invocation.exists())

    def test_docker_uses_readonly_license_mount_and_host_uid_without_credentials(self):
        private = self.work / "external-activation/Unity_lic.ulf"
        private.parent.mkdir()
        private.write_text("SYNTHETIC_TEST_PLACEHOLDER_NOT_A_LICENSE\n")
        result = self.docker_build(UNITY_LICENSE_FILE=str(private))
        self.assertEqual(result.returncode, 37, result.stdout)
        args = self.invocation.read_text().splitlines()
        self.assertIn(str(os.getuid()) + ":" + str(os.getgid()), args)
        self.assertIn(
            "type=bind,src=" + str(private) +
            ",dst=/workspace/Build/tool-work/home/.local/share/unity3d/Unity/Unity_lic.ulf,readonly", args,
        )
        for arg in args:
            self.assertNotIn("src=" + str(self.project / ".git"), arg)
            self.assertNotIn("src=" + str(self.project / ".squad"), arg)
        self.assertNotIn("-password", args)
        self.assertNotIn("-serial", args)
        self.assertFalse((self.project / "Build/WebGL/build-receipt.json").exists())


class ServeEntrypointTests(WorkDirectoryTest):
    def setUp(self):
        super().setUp()
        self.project = self.work / "project"
        (self.project / "scripts").mkdir(parents=True)
        for name in ("serve-webgl.sh", "verify-webgl.sh"):
            shutil.copy2(ROOT / "scripts" / name, self.project / "scripts" / name)
        shutil.copy2(ROOT / "compose.yaml", self.project / "compose.yaml")
        create_site(self.project / "Build/WebGL")
        tools = self.work / "tools"
        tools.mkdir()
        docker = tools / "docker"
        docker.write_text(
            '#!/bin/sh\nif [ "$1" = compose ] && [ "$2" = version ]; then exit 0; fi\n'
            'printf "%s\\n" "${BUILDX_BUILDER:-unset}" > "$TEST_BUILDER"\n'
            'printf "%s\\n" "$@" > "$TEST_INVOCATION"\n'
        )
        docker.chmod(0o755)
        self.builder = self.work / "builder.txt"
        self.invocation = self.work / "compose-arguments.txt"
        self.env = dict(os.environ, PATH=str(tools) + os.pathsep + os.environ["PATH"],
                        WEBGL_ALLOW_TEST_STUB="1", TEST_BUILDER=str(self.builder),
                        TEST_INVOCATION=str(self.invocation))
        self.env.pop("BUILDX_BUILDER", None)

    def serve(self, **env):
        return run(["bash", str(self.project / "scripts/serve-webgl.sh"), "--no-build"],
                   env=dict(self.env, **env), cwd=ROOT / "scripts")

    def test_default_builder_is_command_local_and_compose_ignores_dotenv(self):
        result = self.serve()
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.builder.read_text().strip(), "default")
        args = self.invocation.read_text().splitlines()
        self.assertEqual(args[:3], ["compose", "--env-file", "/dev/null"])
        self.assertIn(str(self.project / "compose.yaml"), args)
        self.assertEqual(args[-5:], ["up", "--no-build", "--abort-on-container-exit", "--exit-code-from", "web"])

    def test_explicit_builder_override_is_preserved(self):
        result = self.serve(BUILDX_BUILDER="operator-selected-builder")
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertEqual(self.builder.read_text().strip(), "operator-selected-builder")


@unittest.skipUnless(WITH_DOCKER, "pass --docker for actual static-runtime/container/context validation")
class DockerHostingTests(WorkDirectoryTest):
    def docker(self, *args, check=True):
        result = run(["docker", *args], cwd=ROOT,
                     env=dict(os.environ, BUILDX_BUILDER=os.environ.get("BUILDX_BUILDER") or "default"))
        if check:
            self.assertEqual(result.returncode, 0, result.stdout[-5000:])
        return result

    def test_real_container_mime_cache_missing_files_hardening_and_filtered_context(self):
        context = self.work / "context"
        site = context / "Build/WebGL"
        create_site(site)
        for path in ("Dockerfile", ".dockerignore", "hosting/nginx.conf",
                     "hosting/entrypoint.sh", "scripts/verify-webgl.sh"):
            target = context / path
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / path, target)
        for path in ("Assets/Source.cs", ".git/history-canary", ".env",
                     "Build/tool-work/cache-canary", "Build/WebGL/private.ulf",
                     "hosting/private-canary", "scripts/private-canary"):
            target = context / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text("SYNTHETIC_CONTEXT_EXCLUSION_CANARY_NOT_A_SECRET\n")
        export_file = self.work / "Context.Dockerfile"
        export_file.write_text("FROM scratch\nCOPY . /\n")
        exported = self.work / "filtered-context"
        self.docker("build", "--quiet", "-f", str(export_file), "--output", "type=local,dest=" + str(exported), str(context))
        for path in ("Assets", ".git", ".env", "Build/tool-work", "Build/WebGL/private.ulf",
                     "hosting/private-canary", "scripts/private-canary"):
            self.assertFalse((exported / path).exists(), "private/source context path leaked: " + path)
        self.assertEqual((exported / "Build/WebGL/Build/smoke.wasm").read_bytes(), b"\0asm\x01\0\0\0")
        image = "nflblitz-hosting-smoke:" + uuid.uuid4().hex
        container = None
        try:
            rejected = self.docker("build", "--progress=plain", "-t", image, str(context), check=False)
            self.assertNotEqual(rejected.returncode, 0)
            self.assertIn("hosting-only test stub is not a Unity player", rejected.stdout)
            self.docker("build", "--progress=plain", "--build-arg", "WEBGL_ALLOW_TEST_STUB=1", "-t", image, str(context))
            startup_rejected = self.docker("run", "--rm", image, check=False)
            self.assertNotEqual(startup_rejected.returncode, 0)
            self.assertIn("hosting-only test stub is not a Unity player", startup_rejected.stdout)
            container = self.docker(
                "run", "--rm", "-d", "--read-only", "--user", "101:101", "--cap-drop", "ALL",
                "--security-opt", "no-new-privileges", "--env", "WEBGL_ALLOW_TEST_STUB=1",
                "--tmpfs", "/var/cache/nginx:rw,nosuid,nodev,noexec,uid=101,gid=101,mode=0700",
                "--health-interval", "1s", "--publish", "127.0.0.1::8080", image,
            ).stdout.strip()
            binding = self.docker("port", container, "8080/tcp").stdout.strip().splitlines()[0]
            base = "http://" + binding

            def request(path, headers=None):
                try:
                    with urllib.request.urlopen(urllib.request.Request(base + path, headers=headers or {}), timeout=5) as response:
                        return response.status, response.headers, response.read()
                except urllib.error.HTTPError as error:
                    return error.code, error.headers, error.read()

            for _ in range(40):
                try:
                    if request("/healthz")[0] == 200:
                        break
                except (OSError, urllib.error.URLError):
                    pass
                time.sleep(0.25)
            else:
                self.fail("static host did not become responsive: " + self.docker("logs", container).stdout[-4000:])
            status, headers, body = request("/")
            self.assertEqual(status, 200)
            self.assertIn(b"NOT A PLAYABLE GAME", body)
            self.assertIn(b'<meta name="viewport"', body)
            assert_full_viewport_style(self, body.decode())
            self.assertIn(b'rel="icon" type="image/png"', body)
            self.assertEqual(headers["Cache-Control"], "no-cache")
            self.assertEqual(headers["X-Content-Type-Options"], "nosniff")
            for suffix, mime in (("loader.js", "application/javascript"), ("framework.js", "application/javascript"),
                                 ("wasm", "application/wasm"), ("data", "application/octet-stream")):
                status, headers, body = request("/Build/smoke." + suffix, {"Accept-Encoding": "gzip, br"})
                self.assertEqual(status, 200, suffix)
                self.assertEqual(headers.get_content_type(), mime, suffix)
                self.assertNotIn("Content-Encoding", headers, suffix)
                self.assertTrue(body, suffix)
                etag = headers["ETag"]
                self.assertEqual(request("/Build/smoke." + suffix, {"If-None-Match": etag})[0], 304)
            self.assertEqual(request("/Build/smoke.wasm", {"Range": "bytes=0-7"})[::2], (206, b"\0asm\x01\0\0\0"))
            icon_status, icon_headers, icon_body = request("/favicon.ico")
            self.assertEqual(icon_status, 200)
            self.assertEqual(icon_headers.get_content_type(), "image/png")
            self.assertTrue(icon_body.startswith(b"\x89PNG\r\n\x1a\n"))
            self.assertEqual(icon_body, request("/TemplateData/webmemd-icon.png")[2])
            for path in ("/Build/missing.wasm", "/Build/missing.loader.js", "/not-a-route",
                         "/Build/smoke.wasm.gz", "/.env", "/Assets/Source.cs"):
                status, headers, body = request(path)
                self.assertEqual(status, 404, path)
                self.assertEqual(headers.get_content_type(), "text/plain", path)
                self.assertNotIn(b"WEBGL_HOSTING_TEST_STUB", body, path)
            settings = json.loads(self.docker("inspect", container).stdout)[0]
            self.assertEqual(settings["Config"]["User"], "101:101")
            self.assertTrue(settings["HostConfig"]["ReadonlyRootfs"])
            self.assertIn("ALL", settings["HostConfig"]["CapDrop"])
            self.docker("exec", container, "sh", "-c", "test ! -e /workspace && test ! -e /opt/unity && test ! -e /usr/share/nginx/html/private.ulf")
        finally:
            if container is not None:
                self.docker("rm", "-f", container, check=False)
            self.docker("image", "rm", image, check=False)


if __name__ == "__main__":
    unittest.main(verbosity=2)
