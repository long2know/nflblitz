#!/usr/bin/env bash
set -euo pipefail
umask 077

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
PIN=6000.3.24f1
IMAGE='unityci/editor:ubuntu-6000.3.24f1-webgl-3.2.2@sha256:f3c6661a8c5bc975df168f114eb3882766546b2ad09765664f5b2bb186857ddb'
METHOD=Blitz.Editor.WebGlBuild.Build
MODE=docker
CHECK_ONLY=0

fail() { printf 'Unity WebGL build blocked: %s\n' "$*" >&2; exit 1; }
usage() {
    cat <<'USAGE'
Usage: bash scripts/build-webgl.sh [--host] [--check-toolchain]

Builds the root Unity 6000.3.24f1 project using Blitz.Editor.WebGlBuild.Build.
The editor method must check BuildReport success and emit an uncompressed,
non-development, single-threaded player to Build/WebGL. No fallback player exists.
Logs remain private in ignored Logs/; a version probe does not test activation.

Default Docker mode:
  Provision ONE external, already-valid UNITY_LICENSE_FILE=/private/Unity_lic.ulf
  or UNITY_LICENSE_DIR=/private/Unity. The wrapper checks metadata only and mounts
  it read-only; it never activates, copies or prints license/account material.
  Licensing must stay outside this checkout. No image supplies an entitlement.
  If the image is missing, the error prints the exact pinned docker pull command.

Existing licensed host editor:
  UNITY_EDITOR=/approved/6000.3.24f1/Editor/Unity bash scripts/build-webgl.sh --host
  The process inherits HOME/XDG_* activation configuration without copying it.

--check-toolchain checks the actual version and WebGL module without opening
the project or requiring a license mount. It does not prove build entitlement.
USAGE
}
while (($#)); do
    case "$1" in
        --host) MODE=host ;;
        --check-toolchain) CHECK_ONLY=1 ;;
        -h|--help) usage; exit 0 ;;
        *) usage >&2; fail "unknown argument: $1" ;;
    esac
    shift
done

for directory in Build Logs; do
    [[ ! -L "$ROOT/$directory" ]] || fail "$directory must not be a symbolic link"
done
WORK="$ROOT/Build/tool-work"
[[ ! -L "$WORK" ]] || fail "Build/tool-work must not be a symbolic link"
mkdir -p -- "$WORK" "$ROOT/Logs"
export TMPDIR="$WORK" TMP="$WORK" TEMP="$WORK"
cd -- "$ROOT"

if [[ "$MODE" == host ]]; then
    [[ "${UNITY_EDITOR:-}" = /* && -f "${UNITY_EDITOR:-}" && -x "${UNITY_EDITOR:-}" ]] ||
        fail "--host requires UNITY_EDITOR to be an existing absolute executable path"
    EDITOR="$(readlink -f -- "$UNITY_EDITOR")"
    [[ -d "${EDITOR%/*}/Data/PlaybackEngines/WebGLSupport" ]] ||
        fail "the configured host editor has no WebGLSupport module"
    VERSION="$("$EDITOR" -version 2>&1)" || fail "the configured host editor could not report its version"
else
    command -v docker >/dev/null 2>&1 || fail "Docker is required (or use --host with an approved licensed editor)"
    docker info >/dev/null 2>&1 || fail "Docker daemon/context is not accessible"
    if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
        fail "pinned editor image is not cached. Acquire it with: docker pull --platform linux/amd64 $IMAGE"
    fi
    VERSION="$(docker run --rm --pull=never --platform linux/amd64 \
        --user "$(id -u):$(id -g)" --entrypoint /bin/bash \
        "$IMAGE" -c 'test -d /opt/unity/Editor/Data/PlaybackEngines/WebGLSupport &&
            /opt/unity/Editor/Unity -version' 2>&1)" ||
        fail "the pinned editor image could not execute its WebGL-capable editor"
    EDITOR=/opt/unity/Editor/Unity
fi
printf '%s\n' "$VERSION" | grep -Eq "^${PIN//./\\.}([[:space:]]|$|\\()" ||
    fail "editor version mismatch; require $PIN"
printf 'Actual Unity %s executable and WebGL module verified (%s); activation is not established by this probe.\n' "$PIN" "$MODE"
if ((CHECK_ONLY)); then exit 0; fi

[[ -d "$ROOT/Assets" && -f "$ROOT/Packages/manifest.json" && -f "$ROOT/ProjectSettings/ProjectVersion.txt" ]] ||
    fail "the root Unity project is missing (Assets, Packages/manifest.json, ProjectSettings/ProjectVersion.txt)"
PROJECT_VERSION="$(sed -n 's/^m_EditorVersion: //p' "$ROOT/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
[[ "$PROJECT_VERSION" == "$PIN" ]] || fail "ProjectVersion.txt must select $PIN, not $PROJECT_VERSION"
for directory in Assets Packages ProjectSettings Library Temp UserSettings; do
    [[ ! -L "$ROOT/$directory" ]] || fail "linked Unity project/cache directory is not accepted: $directory"
done
[[ ! -L "$ROOT/Build/WebGL" ]] || fail "Build/WebGL must not be a symbolic link"
[[ ! -L "$ROOT/Logs/webgl-build.log" && ! -L "$ROOT/Logs/webgl-console.log" &&
    ! -L "$ROOT/Build/webgl-build-started" && ! -L "$ROOT/Build/WebGL/build-receipt.json" ]] ||
    fail "linked build logs, receipt or start marker are not accepted"

ARGS=(-batchmode -nographics -quit -projectPath "$ROOT" -executeMethod "$METHOD"
    -buildTarget WebGL -logFile "$ROOT/Logs/webgl-build.log")
COMMAND=("$EDITOR")
if [[ "$MODE" == docker ]]; then
    [[ -z "${UNITY_LICENSE_FILE:-}" || -z "${UNITY_LICENSE_DIR:-}" ]] ||
        fail "set only ONE of UNITY_LICENSE_FILE or UNITY_LICENSE_DIR"
    if [[ -n "${UNITY_LICENSE_FILE:-}" ]]; then
        [[ "$UNITY_LICENSE_FILE" = /* && "$UNITY_LICENSE_FILE" = *.ulf &&
            -f "$UNITY_LICENSE_FILE" && ! -L "$UNITY_LICENSE_FILE" ]] ||
            fail "UNITY_LICENSE_FILE must be an existing absolute external .ulf file, not a symlink; contents are never read"
        PRIVATE_SOURCE="$(cd -- "${UNITY_LICENSE_FILE%/*}" && pwd -P)/${UNITY_LICENSE_FILE##*/}"
        PRIVATE_TARGET=/workspace/Build/tool-work/home/.local/share/unity3d/Unity/Unity_lic.ulf
    elif [[ -n "${UNITY_LICENSE_DIR:-}" ]]; then
        [[ "$UNITY_LICENSE_DIR" = /* && -d "$UNITY_LICENSE_DIR" && ! -L "$UNITY_LICENSE_DIR" ]] ||
            fail "UNITY_LICENSE_DIR must be an existing absolute external pre-provisioned licensing directory"
        PRIVATE_SOURCE="$(cd -- "$UNITY_LICENSE_DIR" && pwd -P)"
        PRIVATE_TARGET=/workspace/Build/tool-work/home/.local/share/unity3d/Unity
    else
        fail "Docker supplies tools, not entitlement. Set external UNITY_LICENSE_FILE or UNITY_LICENSE_DIR, or use --host with existing supported HOME/XDG_* activation. No credentials are accepted or activated automatically."
    fi
    [[ "$PRIVATE_SOURCE" != "$ROOT" && "$PRIVATE_SOURCE" != "$ROOT/"* ]] ||
        fail "licensing must remain outside the checkout"
    [[ "$PRIVATE_SOURCE" != *,* && "$ROOT" != *,* ]] ||
        fail "Docker bind-mount source paths cannot contain commas"
    mkdir -p -- "$ROOT/Library" "$ROOT/Temp" "$ROOT/UserSettings" \
        "$WORK/home/.local/share/unity3d/Unity" "$WORK/home/.config" "$WORK/home/.cache"
    COMMAND=(docker run --rm --pull=never --init --platform linux/amd64
        --user "$(id -u):$(id -g)" --security-opt no-new-privileges --cap-drop ALL
        --workdir /workspace --shm-size=1g
        --env HOME=/workspace/Build/tool-work/home
        --env XDG_CONFIG_HOME=/workspace/Build/tool-work/home/.config
        --env XDG_DATA_HOME=/workspace/Build/tool-work/home/.local/share
        --env XDG_CACHE_HOME=/workspace/Build/tool-work/home/.cache
        --env TMPDIR=/workspace/Build/tool-work --env TMP=/workspace/Build/tool-work
        --env TEMP=/workspace/Build/tool-work)
    # Only Unity project inputs and disposable outputs enter the editor container.
    for directory in Assets Packages ProjectSettings Library Temp UserSettings Build Logs; do
        COMMAND+=(--mount "type=bind,src=$ROOT/$directory,dst=/workspace/$directory")
    done
    COMMAND+=(--mount "type=bind,src=$PRIVATE_SOURCE,dst=$PRIVATE_TARGET,readonly"
        --entrypoint "$EDITOR" "$IMAGE")
    ARGS=(-batchmode -nographics -quit -projectPath /workspace -executeMethod "$METHOD"
        -buildTarget WebGL -logFile /workspace/Logs/webgl-build.log)
fi

rm -f -- "$ROOT/Build/WebGL/build-receipt.json"
touch -- "$ROOT/Build/webgl-build-started"
: > "$ROOT/Logs/webgl-build.log"
printf 'Building %s to Build/WebGL. Private diagnostics: Logs/webgl-build.log and Logs/webgl-console.log\n' "$METHOD"
if "${COMMAND[@]}" "${ARGS[@]}" >"$ROOT/Logs/webgl-console.log" 2>&1 </dev/null; then
    :
else
    code=$?
    printf 'Unity exited with code %s. Inspect private Logs/webgl-build.log / Logs/webgl-console.log locally. If entitlement failed, provision supported activation externally; no license files were copied.\n' "$code" >&2
    exit "$code"
fi
WEBGL_ALLOW_TEST_STUB=0 sh "$ROOT/scripts/verify-webgl.sh" "$ROOT/Build/WebGL" "$ROOT/Build/webgl-build-started"
BUILT_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
printf '{\n  "schemaVersion": 1,\n  "unityVersion": "%s",\n  "editorMode": "%s",\n  "buildMethod": "%s",\n  "target": "WebGL",\n  "compression": "disabled",\n  "validatedAtUtc": "%s"\n}\n' \
    "$PIN" "$MODE" "$METHOD" "$BUILT_AT" >"$ROOT/Build/WebGL/build-receipt.json"
printf '%s\n' 'WebGL build artifacts verified. Start static hosting with: bash scripts/serve-webgl.sh'
