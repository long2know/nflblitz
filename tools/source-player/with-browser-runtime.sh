#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
if (( $# == 0 )); then
  printf 'Usage: bash tools/source-player/with-browser-runtime.sh NODE-ARGUMENTS... | --command PROGRAM ARGUMENTS...\n' >&2
  exit 2
fi
mkdir -p tools/source-player/.runtime
owned="${SOURCE_PLAYER_BROWSER_WORKSPACE:-$PWD/tools/source-player/.runtime/browser-root-$$}"
if [[ -n "${SOURCE_PLAYER_BROWSER_WORKSPACE:-}" ]]; then
  [[ "$owned" == "$PWD/"* && -d "$owned" && ! -L "$owned" &&
     "$(realpath -- "$owned")" == "$owned" && "$(stat -c %u "$owned")" == "$(id -u)" &&
     -z "$(find "$owned" -mindepth 1 -maxdepth 1 -print -quit)" ]] || {
    printf 'SOURCE_PLAYER_BROWSER_WORKSPACE must be an owned empty physical project directory.\n' >&2
    exit 2
  }
else
  mkdir -- "$owned"
fi
node_binary="$(command -v node)"
export SOURCE_PLAYER_PARENT_USER_NAMESPACE="$(readlink /proc/self/ns/user)"
export SOURCE_PLAYER_PARENT_MNT_NAMESPACE="$(readlink /proc/self/ns/mnt)"
evidence="${SOURCE_PLAYER_EVIDENCE_ROOT:-}"
profile_root="${SOURCE_PLAYER_CHROME_RUNTIME_ROOT:-/r}"
[[ "$profile_root" == /r || "$profile_root" =~ ^/r/[A-Za-z0-9_-]+$ ]] || {
  printf 'SOURCE_PLAYER_CHROME_RUNTIME_ROOT must be /r or one named child of /r.\n' >&2
  exit 2
}
export SOURCE_PLAYER_CHROME_RUNTIME_ROOT="$profile_root"
if [[ -n "$evidence" ]]; then
  [[ -d "$evidence" && ! -L "$evidence" && "$(realpath -- "$evidence")" == "$evidence" ]] || {
    printf 'SOURCE_PLAYER_EVIDENCE_ROOT must be an existing physical absolute directory.\n' >&2
    exit 2
  }
fi
# The physical root/profile stays entirely in this owned project directory.
# A private mount/chroot namespace gives the existing strict runtime guard a
# short /r socket path without symlinks, relaxed0700 checks or system temp files.
unshare --user --map-auto --map-current-user --keep-caps --mount --propagation private bash -c '
  set -euo pipefail
  owned="$1"; node_binary="$2"; project="$3"; evidence="$4"; shift 4
  mount_helper="$project/tools/source-player/namespace_mount.py"
  mounts=()
  cleanup() {
    # Some inherited kernel submounts are locked in an unprivileged namespace.
    # Exiting this private namespace releases those; never unmount a host path.
    for (( i=${#mounts[@]}-1; i>=0; i-- )); do
      python3 "$mount_helper" unmount "${mounts[i]}" 2>> "$owned/unmount.log" || true
    done
  }
  trap cleanup EXIT
  python3 "$mount_helper" private-tmpfs "$owned" tmpfs mode=0700,size=32m
  mounts+=("$owned")
  mkdir -p "$owned"/{app,e,usr,bin,lib,lib64,opt/google/chrome,dev,proc,sys,etc/fonts,r,sandbox}
  touch "$owned/node"
  bind() {
    python3 "$mount_helper" bind "$2" "$1"; mounts+=("$2")
    if [[ "${3:-}" == ro ]]; then python3 "$mount_helper" readonly "$2" "$1"; fi
  }
  bind /usr "$owned/usr" ro
  bind /usr/bin "$owned/bin" ro
  bind /usr/lib "$owned/lib" ro
  bind /usr/lib64 "$owned/lib64" ro
  bind /opt/google/chrome "$owned/opt/google/chrome" ro
  # A private copy retains the real setuid helper contract in the mapped namespace.
  python3 "$mount_helper" sandbox-tmpfs "$owned/sandbox" tmpfs mode=0700,size=1m
  mounts+=("$owned/sandbox")
  cp -- "$owned/opt/google/chrome/chrome-sandbox" "$owned/sandbox/chrome-sandbox"
  chown 0:0 "$owned/sandbox/chrome-sandbox"
  chmod 4755 "$owned/sandbox/chrome-sandbox"
  bind "$owned/sandbox/chrome-sandbox" "$owned/opt/google/chrome/chrome-sandbox" ro
  for tree in dev proc sys; do
    python3 "$mount_helper" rbind "$owned/$tree" "/$tree"; mounts+=("$owned/$tree")
  done
  bind /etc/fonts "$owned/etc/fonts" ro
  for file in hosts resolv.conf nsswitch.conf; do
    touch "$owned/etc/$file"; bind "/etc/$file" "$owned/etc/$file" ro
  done
  bind "$node_binary" "$owned/node" ro
  bind "$project" "$owned/app"
  if [[ -n "$evidence" ]]; then bind "$evidence" "$owned/e"; fi
  python3 "$mount_helper" private-tmpfs "$owned/r" tmpfs mode=0700,size=512m
  mounts+=("$owned/r")
  mkdir -p "$owned/r"/{home,tmp,cache,config,data,xdg,dotnet-home,nuget}
  chroot "$owned" /usr/bin/setpriv --inh-caps=-all --ambient-caps=-all \
    /usr/bin/env CHROME_RUNTIME_ROOT="$SOURCE_PLAYER_CHROME_RUNTIME_ROOT" HOME=/r/home TMPDIR=/r/tmp \
      XDG_RUNTIME_DIR=/r/xdg XDG_CACHE_HOME=/r/cache XDG_CONFIG_HOME=/r/config \
      XDG_DATA_HOME=/r/data DOTNET_CLI_HOME=/r/dotnet-home NUGET_PACKAGES=/r/nuget \
      PATH=/usr/bin:/bin \
    /bin/bash -c '\''cd /app; if [[ "$1" == --command ]]; then shift; exec "$@"; fi; exec /node "$@"'\'' bash "$@"
' bash "$owned" "$node_binary" "$PWD" "$evidence" "$@"
if [[ -z "${SOURCE_PLAYER_BROWSER_WORKSPACE:-}" ]]; then
  find "$owned" -depth -type f -empty -delete
  find "$owned" -depth -type d -empty -delete
fi
