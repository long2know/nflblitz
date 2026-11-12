#!/bin/sh
set -eu
export LC_ALL=C
umask 077

. /opt/nflblitz/deploy/runtime-common.sh
BASE_URL=$(normalize_base_url)
export BASE_URL
runtime=$(runtime_directory)
prepare_runtime_directory "$runtime"
config=$runtime/nginx.conf
if [ -L "$config" ] || { [ -e "$config" ] && [ ! -f "$config" ]; }; then
    printf '%s\n' 'Unsafe generated nginx configuration path.' >&2
    exit 1
fi
rm -f "$config"
render_nginx_config /etc/nginx/nginx.conf "$config"
nginx -t -q -c "$config"
exec nginx -c "$config" "$@"
