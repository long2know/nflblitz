# WebGL build and static hosting

This increment publishes build/hosting **tooling**, not a Unity player. The root
Unity project (`Packages/manifest.json`, `ProjectSettings/ProjectVersion.txt`) and
editor method `Blitz.Editor.WebGlBuild.Build` belong to the separate presentation/
project increment. Without them, the wrapper fails rather than substituting a
player. Actual Unity compilation and browser gameplay are not yet proven.

## Pinned toolchain and external activation

- Unity **6000.3.24f1**, WebGL support, Linux amd64 editor image:
  `unityci/editor:ubuntu-6000.3.24f1-webgl-3.2.2@sha256:f3c6661a8c5bc975df168f114eb3882766546b2ad09765664f5b2bb186857ddb`.
- Runtime-only image:
  `nginx:1.30.6-alpine-slim@sha256:3028fc1419f99b95c1c69f844bbaf49dc1b881750c51b497fc721ac4f02a14e6`.
- Bash, Docker with Compose, and an accessible daemon are required for the
  container route. A matching installed licensed editor can instead build on host.
- Provision supported entitlement/activation **outside this checkout**. Images
  provide tools, not licenses. Never commit, copy, print or pass account credentials
  to these scripts. Set exactly one external `UNITY_LICENSE_FILE` (absolute,
  non-symlink `.ulf`) or `UNITY_LICENSE_DIR` (pre-provisioned directory) for Docker;
  the wrapper checks metadata and bind-mounts it read-only.

From the repository root, acquire the pinned editor and probe its version/module:

```sh
docker pull --platform linux/amd64 \
  unityci/editor:ubuntu-6000.3.24f1-webgl-3.2.2@sha256:f3c6661a8c5bc975df168f114eb3882766546b2ad09765664f5b2bb186857ddb
bash scripts/build-webgl.sh --check-toolchain
```

A probe does not open the project or establish activation. Registry pins were
verified, and the installed host editor's actual version/WebGL module were probed;
neither is proof of player compilation or a runnable downloaded editor container.

After the Unity project/build method is integrated, build with external activation:

```sh
UNITY_LICENSE_FILE="${EXTERNAL_UNITY_LICENSE_FILE:?set an external absolute .ulf path}" \
  bash scripts/build-webgl.sh
```

Alternatively, use an existing supported, activated host installation:

```sh
UNITY_EDITOR="${APPROVED_UNITY_EDITOR:?set an absolute editor executable path}" \
  bash scripts/build-webgl.sh --host
```

Host mode inherits the existing process-scoped `HOME`/`XDG_*` activation setup;
configure those externally when necessary. It neither activates nor copies
licensing material. Add `--check-toolchain` to the host command for a probe only.
Do not run two editors/builds against the same root `Library/`.

## Validate and serve an emitted player

The editor must emit a non-development, single-threaded, uncompressed player in
`Build/WebGL`: `index.html` and one non-empty loader JS, framework JS, WASM and data
file under `Build/`. The wrapper requires fresh output and successful verification;
a zero editor exit without valid output is not success. Logs and build receipts
remain ignored under `Logs/` and `Build/`; keep diagnostics private.

```sh
WEBGL_ALLOW_TEST_STUB=0 sh scripts/verify-webgl.sh Build/WebGL
bash scripts/serve-webgl.sh
```

Serving builds only the static runtime image and runs in the foreground at
`http://127.0.0.1:8080/`; Ctrl-C stops it. `WEBGL_BIND_ADDRESS` and `WEBGL_PORT`
override the binding. The script deliberately supplies an empty Compose env file
instead of loading repository `.env` files. A health response at `/healthz` proves
HTTP readiness, not gameplay.

The runtime is non-root/read-only, contains no editor or source tree, serves WASM
as `application/wasm`, disables compression, revalidates with ETags and returns
plain-text 404s without an index fallback. A complete real player is required;
synthetic hosting fixtures are rejected by default.

## Bounded tooling evidence

```sh
PYTHONDONTWRITEBYTECODE=1 python3 scripts/test-webgl-tooling.py --docker
```

Recorded October 15, 2026: **12/12 passed**—eleven contract tests with mocked
editor/Docker commands where appropriate, plus actual pinned-nginx Docker
integration using an explicitly labeled **synthetic, non-playable** artifact.
Checks cover artifact rejection, build failures/stale output, external read-only
activation mounts, MIME, 304/range responses, 404s and filtered runtime context.
They do not compile Unity or validate game loading, controls, rendering, browser
performance or historical parity. Real-player acceptance remains
[the pending browser smoke](../tests/browser/README.md).
