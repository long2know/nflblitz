import copy
import json
import os
from pathlib import Path
import subprocess
import unittest


HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[1]
COMPOSE = HERE / "compose.yaml"
IMAGE = "nginx:1.30.6-alpine-slim@sha256:3028fc1419f99b95c1c69f844bbaf49dc1b881750c51b497fc721ac4f02a14e6"
BUNDLE = HERE / "bin/SourcePlayer.Browser/Release/net10.0/browser-wasm/AppBundle"


def normalize_bind_creation(config, compose_major):
    assert compose_major in (2, 5), "Unknown Compose JSON bind-flag dialect; do not guess its missing-value semantics."
    for service in config["services"].values():
        for mount in service.get("volumes", []):
            if mount["type"] == "bind":
                # Compose 2 omits false; Compose 5 omits its true default.
                mount.setdefault("bind", {}).setdefault("create_host_path", compose_major == 5)
    return config


def configuration(path=COMPOSE, overrides=None):
    source = path.read_text()
    for target in ("/usr/share/nginx/html", "/etc/nginx/nginx.conf"):
        required = ("        target: " + target + "\n        read_only: true\n"
                    "        bind:\n          create_host_path: false")
        assert required in source, "Explicit read-only/no-create mount declaration required: " + target
    env = os.environ.copy()
    env.pop("SOURCE_PLAYER_BIND_ADDRESS", None)
    env.pop("SOURCE_PLAYER_PORT", None)
    env.update(overrides or {})
    version = subprocess.check_output(["docker", "compose", "version", "--short"],
                                      cwd=PROJECT, env=env, text=True).strip()
    major = int(version.lstrip("v").split(".", 1)[0])
    config = json.loads(subprocess.check_output([
        "docker", "compose", "--env-file", os.devnull, "--project-name", "nflblitz-source-preview",
        "--project-directory", str(HERE), "-f", str(path), "config", "--format", "json",
    ], cwd=PROJECT, env=env, text=True))
    return normalize_bind_creation(config, major)


def require_isolated(config, address="127.0.0.1", port=8082):
    assert config["name"] == "nflblitz-source-preview"
    assert set(config["services"]) == {"preview"}, "Do not start the root Unity service."
    service = config["services"]["preview"]
    assert service["image"] == IMAGE, "Reuse the repository's pinned Nginx runtime, not an SDK image."
    assert service["entrypoint"] == ["nginx", "-g", "daemon off;"]
    assert service["user"] == "101:101" and service["read_only"] is True
    assert service["cap_drop"] == ["ALL"]
    assert "no-new-privileges:true" in service["security_opt"]
    assert service["ports"] == [{
        "mode": "ingress", "host_ip": address, "target": 8080, "published": str(port), "protocol": "tcp",
    }], "Exposure requires explicit preview-specific opt-in; default must remain loopback."
    mounts = {mount["target"]: mount for mount in service["volumes"]}
    assert set(mounts) == {"/usr/share/nginx/html", "/etc/nginx/nginx.conf"}
    for target, source in [("/usr/share/nginx/html", BUNDLE), ("/etc/nginx/nginx.conf", HERE / "nginx.conf")]:
        mount = mounts[target]
        assert mount["type"] == "bind" and mount["source"] == str(source)
        assert mount.get("read_only", False) is True, "Private emitted inputs must remain read-only."
        assert mount.get("bind", {}).get("create_host_path", True) is False, "A missing build must not become an empty auto-created mount."
    assert service["healthcheck"]["test"] == [
        "CMD", "wget", "-q", "--spider", "http://127.0.0.1:8080/build-info.json",
    ]
    assert not any(key in service for key in ("build", "depends_on", "env_file", "environment"))


class HostingTests(unittest.TestCase):
    def test_default_service_is_isolated_readonly_loopback(self):
        require_isolated(configuration())
        self.assertIn("FROM " + IMAGE, (PROJECT / "Dockerfile").read_text())
        nginx = (HERE / "nginx.conf").read_text()
        self.assertIn("application/wasm wasm;", nginx)
        self.assertIn("try_files $uri $uri/ =404;", nginx)
        self.assertNotIn("sub_filter", nginx)
        self.assertNotIn("Build/WebGL", nginx)

    def test_only_explicit_preview_variables_change_exposure(self):
        require_isolated(configuration(overrides={"WEBGL_BIND_ADDRESS": "0.0.0.0", "WEBGL_PORT": "80"}))
        require_isolated(configuration(overrides={
            "SOURCE_PLAYER_BIND_ADDRESS": "0.0.0.0", "SOURCE_PLAYER_PORT": "18082",
        }), address="0.0.0.0", port=18082)

    def test_bind_flag_dialects_do_not_enable_or_coerce_missing_values(self):
        config = configuration()
        for mount in config["services"]["preview"]["volumes"]:
            mount.setdefault("bind", {}).pop("create_host_path", None)
        require_isolated(normalize_bind_creation(copy.deepcopy(config), 2))
        with self.assertRaisesRegex(AssertionError, "missing build"):
            require_isolated(normalize_bind_creation(copy.deepcopy(config), 5))
        with self.assertRaisesRegex(AssertionError, "Unknown Compose"):
            normalize_bind_creation(copy.deepcopy(config), 3)
        for value in (True, None, 0, "false"):
            with self.subTest(value=value):
                changed = normalize_bind_creation(copy.deepcopy(config), 2)
                changed["services"]["preview"]["volumes"][0]["bind"]["create_host_path"] = value
                with self.assertRaisesRegex(AssertionError, "missing build"):
                    require_isolated(changed)

    def test_mutating_actual_compose_turns_hosting_guards_red(self):
        original = COMPOSE.read_text()
        directory = HERE / "artifacts/hosting-mutations"
        directory.mkdir(parents=True, exist_ok=False)
        try:
            for name, old, new in [
                ("public-default", "${SOURCE_PLAYER_BIND_ADDRESS:-127.0.0.1}", "${SOURCE_PLAYER_BIND_ADDRESS:-0.0.0.0}"),
                ("writable-data", "target: /usr/share/nginx/html\n        read_only: true", "target: /usr/share/nginx/html\n        read_only: false"),
                ("empty-mount", "create_host_path: false", "create_host_path: true"),
                ("missing-no-create", "        bind:\n          create_host_path: false\n", ""),
                ("unpinned-image", IMAGE, "nginx:latest"),
            ]:
                with self.subTest(name=name):
                    self.assertIn(old, original)
                    path = directory / (name + ".yaml")
                    path.write_text(original.replace(old, new, 1))
                    try:
                        with self.assertRaises(AssertionError):
                            require_isolated(configuration(path))
                    finally:
                        path.unlink()
        finally:
            directory.rmdir()


if __name__ == "__main__":
    unittest.main()
