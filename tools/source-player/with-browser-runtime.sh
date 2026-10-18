#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
if (( $# == 0 )); then
  printf 'Usage: bash tools/source-player/with-browser-runtime.sh NODE-ARGUMENTS...\n' >&2
  exit 2
fi
mkdir -p tools/source-player/.runtime
owned="$PWD/tools/source-player/.runtime/browser-root-$$"
mkdir -- "$owned"
node_binary="$(command -v node)"
# The physical root/profile stays entirely in this owned project directory.
# A private mount/chroot namespace gives the existing strict runtime guard a
# short /r socket path without symlinks, relaxed0700 checks or system temp files.
unshare --user --map-root-user --mount --propagation private bash -c '
  set -euo pipefail
  owned="$1"; node_binary="$2"; project="$3"; shift 3
  mkdir -p "$owned"/{app,usr,bin,lib,lib64,opt/google/chrome,dev,proc,sys,etc/fonts,r}
  touch "$owned/node"
  mounts=()
  cleanup() {
    # Some inherited kernel submounts are locked in an unprivileged namespace.
    # Exiting this private namespace releases those; never unmount a host path.
    for (( i=${#mounts[@]}-1; i>=0; i-- )); do
      umount --recursive -- "${mounts[i]}" 2>> "$owned/unmount.log" || true
    done
  }
  trap cleanup EXIT
  bind() {
    mount --bind "$1" "$2"; mounts+=("$2")
    if [[ "${3:-}" == ro ]]; then mount -o remount,bind,ro "$2"; fi
  }
  bind /usr "$owned/usr" ro
  bind /usr/bin "$owned/bin" ro
  bind /usr/lib "$owned/lib" ro
  bind /usr/lib64 "$owned/lib64" ro
  bind /opt/google/chrome "$owned/opt/google/chrome" ro
  for tree in dev proc sys; do
    mount --rbind "/$tree" "$owned/$tree"; mounts+=("$owned/$tree")
  done
  bind /etc/fonts "$owned/etc/fonts" ro
  for file in hosts resolv.conf nsswitch.conf; do
    touch "$owned/etc/$file"; bind "/etc/$file" "$owned/etc/$file" ro
  done
  bind "$node_binary" "$owned/node" ro
  bind "$project" "$owned/app"
  mount -t tmpfs -o mode=0700,size=512m,nosuid,nodev tmpfs "$owned/r"; mounts+=("$owned/r")
  chroot "$owned" /usr/bin/env CHROME_BIN=/app/tools/source-player/chrome-gpu.sh CHROME_RUNTIME_ROOT=/r \
    /bin/bash -c '\''cd /app; exec /node "$@"'\'' bash "$@"
' bash "$owned" "$node_binary" "$PWD" "$@"
find "$owned" -depth -type f -empty -delete
find "$owned" -depth -type d -empty -delete
