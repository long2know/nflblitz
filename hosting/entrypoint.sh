#!/bin/sh
set -eu

/usr/local/bin/verify-webgl /usr/share/nginx/html
exec nginx -g 'daemon off;'
