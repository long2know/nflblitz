#!/bin/sh

runtime_directory() {
    printf '/tmp/nflblitz-%s\n' "$(id -u)"
}

check_runtime_directory() {
    if [ -L "$1" ] || [ ! -d "$1" ] ||
       [ "$(stat -c '%u:%a' "$1")" != "$(id -u):700" ]; then
        printf '%s\n' 'Unsafe nginx runtime directory: expected a private, UID-owned directory.' >&2
        return 1
    fi
}

prepare_runtime_directory() {
    # A sticky parent plus atomic mkdir prevents another UID from supplying our directory.
    mkdir -m 700 "$1" 2>/dev/null || check_runtime_directory "$1" || return 1
    check_runtime_directory "$1"
}

normalize_base_url() {
    nflblitz_base_url=${BASE_URL-/}
    case "$nflblitz_base_url" in
        ''|*[!A-Za-z0-9_./~-]*)
            printf '%s\n' 'Invalid BASE_URL: use an absolute, unencoded URL path.' >&2
            return 1
            ;;
        /*) ;;
        *)
            printf '%s\n' 'Invalid BASE_URL: an absolute URL path is required.' >&2
            return 1
            ;;
    esac
    case "$nflblitz_base_url" in
        *//*|*/.*)
            printf '%s\n' 'Invalid BASE_URL: empty, hidden and traversal path segments are forbidden.' >&2
            return 1
            ;;
    esac
    case "$nflblitz_base_url" in
        */) ;;
        *) nflblitz_base_url=$nflblitz_base_url/ ;;
    esac
    printf '%s\n' "$nflblitz_base_url"
}

render_nginx_config() {
    nflblitz_prefix=$(normalize_base_url) || return 1
    nflblitz_runtime=$(runtime_directory)
    if [ "$(grep -c '^[[:space:]]*# BASE_URL_BEGIN$' "$1")" != 1 ] ||
       [ "$(grep -c '^[[:space:]]*# BASE_URL_END$' "$1")" != 1 ] ||
       [ "$(grep -c '@RUNTIME_DIR@' "$1")" != 6 ]; then
        printf '%s\n' 'Invalid nginx runtime template.' >&2
        return 1
    fi
    awk -v prefix="$nflblitz_prefix" -v runtime="$nflblitz_runtime" '
        { gsub(/@RUNTIME_DIR@/, runtime) }
        /^[[:space:]]*# BASE_URL_BEGIN$/ {
            if (prefix == "/") { print; next }
            skip = 1
            bare = substr(prefix, 1, length(prefix) - 1)
            regex = ""
            for (i = 1; i <= length(prefix); i++) {
                character = substr(prefix, i, 1)
                regex = regex (character == "." ? "\\." : character)
            }
            printf "        location = %s {\n", bare
            printf "            return 308 %s$is_args$args;\n        }\n", prefix
            printf "        location = %sfavicon.ico { return 204; }\n", prefix
            printf "        location = %s { try_files /index.html =404; }\n", prefix
            printf "        location %s {\n", prefix
            printf "            rewrite ^%s(.*)$ /$1 break;\n", regex
            print "            try_files $uri =404;"
            print "            limit_except GET { deny all; }"
            print "        }"
            print "        location / { return 404; }"
            next
        }
        /^[[:space:]]*# BASE_URL_END$/ { skip = 0; print; next }
        !skip { print }
    ' "$1" > "$2"
}
