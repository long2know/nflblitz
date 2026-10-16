#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
BUILD_FLAG=--build
case "${1:-}" in
    "") ;;
    --no-build) BUILD_FLAG=--no-build ;;
    -h|--help)
        printf '%s\n' \
            'Usage: bash scripts/serve-webgl.sh [--no-build]' \
            'Validates Build/WebGL, then runs the static runtime in the foreground.' \
            'Default URL: http://127.0.0.1:8080 (WEBGL_BIND_ADDRESS / WEBGL_PORT override).' \
            'Builds use the daemon builder default unless BUILDX_BUILDER is explicitly set.' \
            'Ctrl-C stops the service. No license or Unity editor is present at runtime.' \
            'Compose receives an empty env file; repository .env files are never loaded.'
        exit 0
        ;;
    *) printf 'Unknown argument: %s\n' "$1" >&2; exit 2 ;;
esac
[[ "$#" -le 1 ]] || { printf '%s\n' 'Only one serving option is accepted.' >&2; exit 2; }
sh "$ROOT/scripts/verify-webgl.sh" "$ROOT/Build/WebGL"
command -v docker >/dev/null 2>&1 || { printf '%s\n' 'Docker with Compose is required.' >&2; exit 127; }
docker compose version >/dev/null
export BUILDX_BUILDER="${BUILDX_BUILDER:-default}"
exec docker compose --env-file /dev/null --project-directory "$ROOT" \
    --project-name nflblitz -f "$ROOT/compose.yaml" \
    up "$BUILD_FLAG" --abort-on-container-exit --exit-code-from web
