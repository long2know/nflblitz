#!/bin/sh
set -eu

fail() { printf 'WebGL artifact error: %s\n' "$*" >&2; exit 1; }

[ "$#" -ge 1 ] && [ "$#" -le 2 ] || fail "usage: verify-webgl.sh OUTPUT_DIRECTORY [EXPECTED_BUILD_ID]"
root=$1
expected_id=${2:-}
case "$root" in /*) ancestor=$root ;; *) ancestor="$PWD/$root" ;; esac
while [ "$ancestor" != / ] && [ "${ancestor%/}" != "$ancestor" ]; do ancestor=${ancestor%/}; done
while :; do
    [ ! -L "$ancestor" ] || fail "linked output directory/ancestor is not accepted: $ancestor"
    parent=$(dirname -- "$ancestor")
    [ "$parent" != "$ancestor" ] || break
    ancestor=$parent
done
[ -d "$root" ] && [ ! -L "$root" ] || fail "missing or linked output directory: $root"
[ -d "$root/Build" ] && [ ! -L "$root/Build" ] || fail "missing or linked player directory: $root/Build"
[ -s "$root/index.html" ] && [ ! -L "$root/index.html" ] || fail "missing, empty or linked index.html; run scripts/build-webgl.sh"

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

proof="$root/build-generation.txt"
[ -s "$proof" ] && [ ! -L "$proof" ] || fail "missing current BuildReport generation proof"
[ "$(wc -l < "$proof" | tr -d '[:space:]')" = 12 ] || fail "invalid generation proof shape"
for field in format=blitz-webgl-build-v1 unity-version=6000.3.24f1 build-target=WebGL build-result=Succeeded total-errors=0; do
    [ "$(grep -Fxc "$field" "$proof" || true)" = 1 ] || fail "invalid BuildReport proof field: $field"
done
build_id=$(sed -n 's/^build-id=//p' "$proof")
case "$build_id" in ''|*[!0-9a-f]*) fail "invalid generation build ID" ;; esac
[ "${#build_id}" = 32 ] || fail "invalid generation build ID length"
if [ -n "$expected_id" ]; then
    [ "$build_id" = "$expected_id" ] || fail "build invocation does not match; stale output is not a successful build"
fi
report_bytes=$(sed -n 's/^build-report-bytes=//p' "$proof")
case "$report_bytes" in ''|*[!0-9]*) fail "invalid BuildReport byte count" ;; esac
[ "$report_bytes" -gt 0 ] || fail "BuildReport has no output bytes"
verify_hash() {
    relative=$1
    digest=$(sha256sum -- "$root/$relative")
    digest=${digest%% *}
    [ "$(grep -Fxc "sha256=$digest $relative" "$proof" || true)" = 1 ] ||
        fail "generation hash mismatch: $relative"
}
verify_hash index.html
for suffix in loader.js framework.js wasm data; do
    set -- "$root/Build/"*."$suffix"
    verify_hash "Build/${1##*/}"
done

printf 'Verified uncompressed WebGL artifact set: %s\n' "$root"
