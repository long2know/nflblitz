#!/bin/sh
set -eu
export LC_ALL=C

. /opt/nflblitz/deploy/runtime-common.sh
prefix=$(normalize_base_url)
runtime=$(runtime_directory)
check_runtime_directory "$runtime"
if [ -L "$runtime/nginx.conf" ] || [ ! -f "$runtime/nginx.conf" ]; then
    printf '%s\n' 'Missing safe runtime nginx configuration.' >&2
    exit 1
fi
for resource in build-info.json data/manifest.json _framework/SourcePlayer.Core.wasm; do
    wget -q --spider "http://127.0.0.1:8080${prefix}${resource}" || exit 1
done
