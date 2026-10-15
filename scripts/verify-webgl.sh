#!/bin/sh
set -eu

fail() { printf 'WebGL artifact error: %s\n' "$*" >&2; exit 1; }

[ "$#" -ge 1 ] && [ "$#" -le 2 ] || fail "usage: verify-webgl.sh OUTPUT_DIRECTORY [BUILD_START_MARKER]"
root=$1
since=${2:-}
[ -d "$root" ] && [ ! -L "$root" ] || fail "missing or linked output directory: $root"
[ -d "$root/Build" ] && [ ! -L "$root/Build" ] || fail "missing or linked player directory: $root/Build"
[ -s "$root/index.html" ] && [ ! -L "$root/index.html" ] || fail "missing, empty or linked index.html; run scripts/build-webgl.sh"
if [ -n "$since" ]; then
    [ -f "$since" ] || fail "build start marker is missing"
    [ "$root/index.html" -nt "$since" ] || fail "index.html was not produced by this build; stale output is not a successful build"
fi

if [ -e "$root/hosting-test-stub.txt" ] || grep -q 'WEBGL_HOSTING_TEST_STUB' "$root/index.html"; then
    [ "${WEBGL_ALLOW_TEST_STUB:-0}" = 1 ] || fail "hosting-only test stub is not a Unity player"
    printf '%s\n' 'WARNING: validating a HOSTING TEST STUB, not a playable game.' >&2
fi

unsafe=$(find "$root" \( -type l -o -name '*.gz' -o -name '*.br' -o -name '*.unityweb' \
    -o -name '*.ulf' -o -name '*.alf' -o -name '.env' -o -name '.env.*' \
    -o -name '*.pem' -o -name '*.key' -o -name '*.pfx' -o -name '*.p12' \) -print -quit)
[ -z "$unsafe" ] || fail "linked, compressed or private material is not allowed in the uncompressed player: $unsafe"

for suffix in loader.js framework.js wasm data; do
    set -- "$root/Build/"*."$suffix"
    [ "$#" -eq 1 ] && [ -s "$1" ] && [ ! -L "$1" ] || fail "expected exactly one non-empty Build/*.$suffix"
    file=$1
    name=${file##*/}
    case "$name" in
        *[!A-Za-z0-9_.-]*) fail "unsupported artifact filename: $name" ;;
    esac
    grep -Fq "$name" "$root/index.html" || fail "index.html does not reference Build/$name"
    case "$suffix" in
        loader.js)
            grep -q 'createUnityInstance' "$file" || fail "Build/$name is not a Unity browser loader"
            ;;
        wasm)
            magic=$(od -An -tx1 -N8 "$file" | tr -d '[:space:]')
            [ "$magic" = 0061736d01000000 ] || fail "Build/$name has invalid WebAssembly magic/version"
            ;;
    esac
done

printf 'Verified uncompressed WebGL artifact set: %s\n' "$root"
