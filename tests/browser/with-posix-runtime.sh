#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
if (( $# == 0 )); then
  printf 'Usage: bash tests/browser/with-posix-runtime.sh COMMAND [ARG...]\n' >&2
  exit 2
fi

mkdir -p tests/.artifacts
runtime="tests/.artifacts/r-$$"
mkdir -- "$runtime"
trap 'rmdir -- "$runtime"' EXIT
export CHROME_RUNTIME_ROOT="$PWD/$runtime"

unshare --user --map-root-user --mount --propagation private bash -c '
  set -euo pipefail
  mount -t tmpfs -o mode=0700,size=512m,nosuid,nodev tmpfs "$CHROME_RUNTIME_ROOT"
  trap '\''umount -- "$CHROME_RUNTIME_ROOT"'\'' EXIT
  "$@"
' bash "$@"
