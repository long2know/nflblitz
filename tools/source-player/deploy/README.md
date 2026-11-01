# Self-contained original303 game image

This deployment is **not** the parent directory's bind-mounted preview. All
compiled browser/WASM modules and all963 original payload files are inside the
image. The host needs Docker, not Unity, .NET, converters, a source checkout, a
database, or an asset/CDN connection. It is the portable C# browser game, not a
native Unity/arcade-hardware-equivalence claim.
The1158 packaged files include every runtime dependency. The sole omitted
upstream file is `.stamp`, the zero-byte .NET SDK publish marker, not game data.

For the already occupied `emulation.long2know.com` origin, use the
**HTTPS subpath** section below and `compose.lan.yaml`; the following direct-play
examples intentionally bind loopback only.

## Load and play — linux/amd64

Transfer `nflblitz-original-20261030-amd64.tar.gz` and its `.sha256` file to
the host. From that directory:

```sh
sha256sum -c nflblitz-original-20261030-amd64.tar.gz.sha256
docker load -i nflblitz-original-20261030-amd64.tar.gz
docker run -d --name nflblitz-original --pull=never --platform=linux/amd64 \
  --user 101:101 --read-only --cap-drop ALL \
  --security-opt no-new-privileges:true --pids-limit 64 \
  --tmpfs /var/cache/nginx:rw,nosuid,nodev,noexec,size=16m,uid=101,gid=101,mode=0700 \
  --restart unless-stopped -p 127.0.0.1:18098:8080 \
  nflblitz-original:20261030-original303-amd64
docker inspect --format '{{.State.Health.Status}}' nflblitz-original
```

Open `http://127.0.0.1:18098/` locally. Click **START ORIGINAL GAME WITH
SOUND**, then use the visible cabinet controls. Audio intentionally requires
that real user gesture. There is no hidden startup or asset-download step.

Alternatively, after loading, put the supplied `compose.yaml` beside the
archive and run:

```sh
docker compose --env-file /dev/null -f compose.yaml up -d --wait
```

Do not run both examples on the same port. Compose never pulls an image and
mounts no host files. Override `NFLBLITZ_PORT` explicitly if the port is occupied.

### Optional linux/arm64 archive

The separately tagged `nflblitz-original-20261030-arm64.tar.gz` is a
**build-only** variant; the delivery host is amd64 and cannot runtime-qualify
arm64. Do not select it for an x86 host or mistake the build for an arm64 play test.
On an actual arm64 Docker host:

```sh
sha256sum -c nflblitz-original-20261030-arm64.tar.gz.sha256
docker load -i nflblitz-original-20261030-arm64.tar.gz
NFLBLITZ_PLATFORM=linux/arm64 NFLBLITZ_IMAGE=nflblitz-original:20261030-original303-arm64 \
  docker compose --env-file /dev/null -f compose.yaml up -d --wait
```

Both archives are ordinary `docker image save` tar archives compressed with
gzip, directly accepted by `docker load`, not registry-only OCI layouts.

## HTTPS subpath with the existing root app preserved

For `https://emulation.long2know.com/nflblitz/`, use the supplied
`compose.lan.yaml` on the **Docker host192.168.0.10**, after loading the
unchanged amd64 archive:

```sh
docker image inspect --format '{{.Id}}' nflblitz-original:20261030-original303-amd64
# Must be sha256:131d585e5aff6486a96d0a3f586e3c40938c392a321d684f15546fa8ea6807e7
docker compose --env-file /dev/null -f compose.lan.yaml up -d --wait
```

This standalone Compose file publishes `192.168.0.10:18098:8080`, retaining
read-only/nonroot/cap-drop/tmpfs/restart and the image's existing healthcheck.
The original `compose.yaml` remains a **local-only127.0.0.1** example; that
address is not reachable by nginx on another machine. Use one Compose file,
not both. Allow TCP18098 from the reverse proxy's LAN address only; do not
forward it publicly. If nginx shares the Docker host, loopback is preferable,
and its new upstream must then be `http://127.0.0.1:18098/`.

`nginx-subpath.conf` is a complete **external** server-block example using
the supplied domain/certificate paths. Merge its two `/nflblitz` locations
into the existing HTTPS server, or replace that server block with the example;
do not add a duplicate server. Keep the old root upstream and its existing
include unchanged:

```nginx
location = /nflblitz {
    absolute_redirect off;
    return 308 /nflblitz/$is_args$args;
}
location ^~ /nflblitz/ {
    proxy_pass http://192.168.0.10:18098/;
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header Connection "";
    proxy_cache off;
    proxy_buffering off;
    proxy_intercept_errors off;
    proxy_redirect off;
    gzip off;
}
location / {
    proxy_pass http://192.168.0.10:8910;
    include ./nginx-proxy-common.conf;
}
```

The final slash on `proxy_pass` is essential: only `/nflblitz/` is stripped.
The origin-relative redirect also works when the outer proxy's listening and
published ports differ.
`/nflblitz?x=1` redirects to `/nflblitz/?x=1`; the immutable page, imports,
.NET boot/WASM, original data/audio and record adapter resolve under the prefix.
Do **not** replace the image's embedded `nginx.conf`, rewrite the origin's
root, route `/_framework` or `/data` globally to the game, or use an SPA fallback.

The exact accepted image was qualified behind a local prefix-stripping nginx
with a separate old-root fixture, including normal sandboxed Chrome startup,
original audio/WebGL/source wakes, real IndexedDB load/reload, query redirect,
MIME/security headers, ranges and missing-resource404. The supplied live domain,
LAN reachability, real certificates and unknown `nginx-proxy-common.conf` were
**not** accessed or tested. That include remains only in the old root location;
the game block's required settings are inline. On the actual proxy host,
validate the merged configuration with `nginx -t` before reloading it, and
check that no other global proxy/header/cache rules override these responses.

Preserve response headers and relative paths; do not add a CDN or cache old files.
The image supplies `COOP: same-origin`, `COEP: require-corp`,
`CORP: same-origin`, `nosniff`, and `Cache-Control: no-store`, including errors.
No CORS exception is required for these same-origin resources. The image returns
empty204 for its `/favicon.ico` (externally `/nflblitz/favicon.ico`), not fabricated
original art. A browser-owned origin-root `/favicon.ico` probe stays with the old
root app; it is not a game dependency. Missing game framework/data resources
still return404.

Use **HTTPS outside localhost**: cryptographic asset checks, gamepads and browser
audio rely on browser secure-context rules. Plain HTTP on a public IP is not a
validated deployment. The qualified prefix is `/nflblitz/`, not an arbitrary
base-path rewrite of either application.
If the proxy runs in another container, connect it on a Docker network rather
than assuming its127.0.0.1 reaches this container. Publicly binding the HTTP port
requires explicit `NFLBLITZ_BIND_ADDRESS=0.0.0.0` and does not provide TLS.

For an otherwise unused origin, root hosting can instead proxy `location /`
to `http://192.168.0.10:18098`. Do not move the current root app to `/emulators/`
unless that app independently supports and is tested at that base path.

## Saves and updates

Named records, career/world results, custom plays and audibles live in the
browser's real IndexedDB (`nflblitz-original-records`), **not in this container**.
Restart/recreate/update the container on the same browser origin
(scheme + hostname + port) to retain saves. Changing origin, clearing site
data, using private browsing or browser storage eviction may lose access.
Use the game's explicit local export/import controls to back up or transfer
records. There are no cloud saves, server accounts, or container save volumes.
The path is not part of the IndexedDB origin: moving this game from `/` to
`/nflblitz/` on the same HTTPS hostname/port retains its store. Do not run
simultaneous copies expecting separate path-scoped saves.

## Rebuilding only from an accepted immutable AppBundle

`package.py` refuses incomplete/changed data, modules, Webcil assemblies,
boot-resource hashes, symlinks and unrelated files. Supply the actual accepted
build-info hash and compiled-source digest, not a source directory for rebuilding:

```sh
python3 -B tools/source-player/deploy/package.py --help
python3 -B tools/source-player/deploy/test_deployment.py
```

Its new ignored context contains only the validated AppBundle, the pinned
Dockerfile/config and an exact file/identity receipt. Build that context with
`docker buildx build --builder default --platform linux/amd64 --load`; no
runtime build or network asset fetch is performed. `--preflight-only` records
an explicitly non-play-qualified server probe, never a release.

For real image HTTP checks, set `NFLBLITZ_TEST_URL` to the owned loopback URL
and `NFLBLITZ_TEST_INVENTORY` to its staged `bundle-inventory.json`. Set
`NFLBLITZ_TEST_BUNDLE` to exercise damaged/missing/extra/symlinked actual-bundle
rejections. These checks cover all served payload bytes and headers, not only
container health; browser gameplay acceptance is separate. The actual normal
Chrome driver is `tests/records/cabinet/browser.mjs`; its external-origin
recreation gate retains its exclusive browser profile while the deployment
owner replaces only the owned container on the same origin. It uses public
cabinet keys, trusted audio startup and read-only observations, never a private
clock/score/RNG setter.

Focused proxy checks reuse this runner with an **owned loopback** proxy and
the exact image's extracted `bundle-inventory.json`:

```sh
NFLBLITZ_TEST_URL=http://127.0.0.1:YOUR_OWNED_PORT/nflblitz/ \
NFLBLITZ_TEST_INVENTORY=YOUR_OWNED_INVENTORY \
python3 -B tools/source-player/deploy/test_deployment.py \
  ConfigurationTests \
  ImageHttpTests.test_root_security_error_and_cache_contract SubpathHttpTests
```

`SubpathHttpTests` expects a separate old-root fixture returning
`existing-root-app:$request_uri` plus newline, not the user's live application.
`subpath.browser.mjs` performs the bounded normal-Chrome prefix/records/source
wake check; it never plays a full regulation campaign.
