# Portable C# game container

The application is .NET browser-WASM plus direct WebGL2, **not a Unity build**.
The image includes its compiled modules, runtime and original data; the host
does not need .NET, Unity, converters, ROM mounts, a backend or a database.
Deployment configuration never rebuilds or changes an accepted AppBundle.

These runtime instructions require an image labelled
`io.nflblitz.deployment=runtime-base-url/v1`. Historical root-only archives do
not acquire `BASE_URL` support merely by setting an environment variable.
Examples are not a publication receipt: use the publisher's verified registry
digest and acceptance evidence to establish which image is actually available.

## Run with your existing Compose services

After publication, pull the image explicitly and add this service:

```yaml
services:
  nflblitz:
    image: ghcr.io/long2know/nflblitz:latest
    platform: linux/amd64
    pull_policy: never
    restart: unless-stopped
    user: "1000:1000"
    environment:
      PUID: "1000"
      PGID: "1000"
      BASE_URL: /nflblitz
    ports:
      - "8920:8080"
    volumes:
      - "${DOCKER_DATA}/nflblitz:/app/data"
```

```sh
docker pull ghcr.io/long2know/nflblitz:latest
docker compose up -d nflblitz
docker inspect --format '{{.State.Health.Status}}' nflblitz
```

Open `http://127.0.0.1:8920/nflblitz/` locally. The user's proxy forwards that
prefix unchanged, so this configuration needs `BASE_URL=/nflblitz`.
For a different deployment, omitting the variable defaults the image to root.
Click **START ORIGINAL GAME
WITH SOUND**, then use the visible cabinet controls. Audio requires that real
user gesture. Use HTTPS for non-loopback hosting.

The internal port remains **8080**, so the requested host port is **8920:8080**,
not `8920:8000`. The image's default user remains **101:101** for compatibility;
Docker's `user: "1000:1000"` directly selects the runtime identity without root,
gosu, `PUID` or `PGID`. Those two environment variables are unused, not UID/GID
configuration for this image.

**No volume, `read_only` option or tmpfs is required by the image.** The optional
`/app/data` mount above matches the requested Compose layout and is unused.
Set `DOCKER_DATA` explicitly to your chosen host directory, or remove `volumes`;
the supplied file rejects an unset value rather than creating an unintended
host-root directory. Ephemeral nginx config,
PID and temporary files use the ordinary writable container layer under
`/tmp/nflblitz-<effective-uid>/`, privately owned with mode0700. Existing
symlinks, foreign-owned or permissive runtime directories cause visible startup
failure instead of being followed or made world-writable. Application files
remain0444 and directories0755; no permission changes are made to the payload.

Neither `/a` nor `/app/data` is used. Mounting either does **not** save game
records. Saves are in the browser's real IndexedDB, not the container. A private
registry package may require a host-side `docker login ghcr.io`; never put
registry credentials in Compose.

The supplied `compose.yaml` uses the requested ordinary writable-container
layout, user1000 and port8920, and explicitly selects `/nflblitz` for the
prefix-preserving proxy. The image itself still defaults to root without the
environment variable.
It accepts the unused `PUID`/`PGID` and `/app/data` mount without trying to chown,
initialize or write that mount. Its `NFLBLITZ_USER`, `NFLBLITZ_IMAGE`,
`NFLBLITZ_PLATFORM` and `NFLBLITZ_PORT` overrides are explicit. For example,
after setting `DOCKER_DATA` (or removing the unused mount):

```sh
BASE_URL=/games/nflblitz/ docker compose --env-file /dev/null -f compose.yaml up -d --wait
BASE_URL=/ NFLBLITZ_USER=101:101 docker compose --env-file /dev/null -f compose.yaml up -d --wait
```

Use one configuration on a given port, not both examples simultaneously.
`compose.lan.yaml` instead defaults to `192.168.0.10:8920` for a proxy on another
host. Restrict that HTTP port to the proxy's LAN address; do not forward it
publicly. A proxy container should use a Docker network rather than assuming
its127.0.0.1 reaches this game.

### Optional read-only hardening

Only if you independently want a read-only container root, add both:

```yaml
    read_only: true
    tmpfs:
      - /tmp:rw,noexec,nosuid,nodev,size=64m,mode=1777
```

The tmpfs is **only for this optional mode**, not for game data or saves. The
same UID-private runtime directory works for default101 and override1000.
Do not mount the old101-owned `/var/cache/nginx` layout or make the application
writable. No host security settings need to be weakened for either mode.

## Runtime BASE_URL

The image default is `/`. A non-root path may omit its final slash:

| Runtime value | Browser address |
|---|---|
| unset or `/` | `/` |
| `/nflblitz` or `/nflblitz/` | `/nflblitz/` |
| `/games/nflblitz` or `/games/nflblitz/` | `/games/nflblitz/` |

This is a **path prefix**, never an external origin. Use absolute, unencoded
ASCII paths made of letters, digits, `_`, `-`, `~`, `/` and interior dots.
Empty values, relative paths, duplicate slashes, hidden/dot/traversal segments,
percent escapes, query strings, fragments, whitespace/newlines and nginx
syntax are rejected before writing a generated configuration. An invalid value
fails startup and health visibly; it never silently falls back to root.

`/games/nflblitz?mode=a%2Fb&empty=` redirects308 to
`/games/nflblitz/?mode=a%2Fb&empty=` on the same origin. HTML, modules, .NET
boot/WASM, data, original audio, input and records modules keep relative URLs
under that prefix. Other paths and missing subresources return404, **not an
SPA/index fallback**. A double-prefixed URL is not accepted. The healthcheck
normalizes the same value and checks build-info, the data manifest and Core
WASM at the configured prefix on internal8080.

## HTTPS proxy alongside other games

**For the user's prefix-preserving proxy**, keep the public prefix intact,
set `BASE_URL=/nflblitz`, and merge these locations:

```nginx
location = /nflblitz {
    absolute_redirect off;
    return 308 /nflblitz/$is_args$args;
}
location ^~ /nflblitz/ {
    proxy_pass http://192.168.0.10:8920;
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
# Keep your existing location / and its upstream unchanged.
```

There is **no final slash/URI on `proxy_pass`** in this mode. For nested
hosting, change the two locations to `/games/nflblitz` and `/games/nflblitz/`
and set `BASE_URL=/games/nflblitz`.
`nginx-subpath.conf` supplies the existing-domain server-block example; its LAN,
certificate paths and old root upstream are examples, not claims of live-host
verification. Validate your merged configuration with `nginx -t` before reload.

**Strip-prefix alternative needs no BASE_URL:** omit the variable (or use `/`)
and use `proxy_pass http://192.168.0.10:8920/;`. The final slash strips the
matching public prefix and relative asset URLs resolve correctly in the
browser. Whether the game redirects to other sites is not the determining
factor. Do not combine stripping with a non-root `BASE_URL`; it correctly404s
rather than guessing another route. Historical root-only images support
stripping, but are **not suitable for the user's prefix-preserving proxy**.

Keep isolation/cache policy scoped to the game, not every application on the
domain. Preserve upstream MIME, ranges,404 and these existing headers:
COOP `same-origin`, COEP `require-corp`, CORP `same-origin`, `nosniff`,
`Referrer-Policy: no-referrer`, and `Cache-Control: no-store`, including errors.
No global `/_framework` or `/data` routes, CORS exception, SPA fallback or CDN
cache are needed. Game `/favicon.ico` is empty204 at the configured prefix;
an unsolicited browser-origin `/favicon.ico` probe remains outside a nested
game and may belong to the existing root app or404.

## Saves and updates

Records, career/world results, custom plays and audibles live in browser
IndexedDB (`nflblitz-original-records`). Recreating/updating the container on
the same browser origin (scheme + hostname + port) retains that store.
Changing origin, clearing site data, private browsing or storage eviction can
lose access. Use the visible local export/import controls for backup/transfer.
The URL path is not a storage boundary: two game paths on the same origin
share the store, not separate path-scoped saves.

## Package only an accepted immutable AppBundle

`package.py` preserves the separate historical963/1114 and AV3061153/named1154
profile/factory/manifest/execution pins. It validates all data, boot resources,
Webcil/WASM, source-module receipts and source digests, rejecting changed,
missing, extra or symlinked inputs. Do not blend an unbuilt source change into
an accepted artifact's identity.

The staged context contains only the AppBundle, the deployment allowlist,
`deployment.json` and `bundle-inventory.json`. App hashes stay unchanged.
The separate deployment manifest inventories the Dockerfile, nginx template and
three runtime scripts, with hashes, image paths and exact0444/0555 modes. Source
files may be private0400: staging makes root-owned COPY inputs readable and
scripts executable for any ordinary nonroot UID. No legacy-builder-incompatible
`COPY --chmod` is required.

Use the returned `deploymentSha256` as the build argument
`DEPLOYMENT_SHA256`; the image label binds that manifest independently of the
application receipt. Keep the existing accepted `VERSION` and exact
`SOURCE_REVISION` inputs. Build/save/reload is not container or browser proof.
`--preflight-only` is explicitly a static-server probe, not release acceptance.
The publisher owns registry transfer, source promotion and publication.

## Focused validation

```sh
NFLBLITZ_TEST_WORKSPACE="$PWD/.checks" \
python3 -B tools/source-player/deploy/test_deployment.py ConfigurationTests RuntimeBaseUrlTests
```

The asset-free tests cover Compose, normalization/rejection, real runtime
template mutation, safe private-directory handling and staging modes/inventory.
For an owned live endpoint, set `NFLBLITZ_TEST_URL` (including its base path) and
`NFLBLITZ_TEST_INVENTORY` to the exact staged receipt, then run `ImageHttpTests`
for all served file hashes/MIME, headers, HEAD, ranges, redirects and404.
`SubpathHttpTests` additionally expects an old-root fixture returning
`existing-root-app:$request_uri` plus newline; never aim it at a live root app.
When using that fixture, select the image tests for prefixed resources rather
than the standalone-image assertion that unrelated root paths must404.

`subpath.browser.mjs <owned loopback URL ending /> <new receipt.json> --smoke`
checks bounded normal-sandbox Chrome startup, trusted Start, original audio,
public cabinet input, four animation frames, WebGL2 and prefix-contained
network requests. Supply `NFLBLITZ_EXPECTED_BUILD_SHA256` explicitly.
Create a new private0700 short `CHROME_RUNTIME_ROOT` matching
`$HOME/.bsp-1234abcd` and an owned0700 `NFLBLITZ_BROWSER_EVIDENCE_ROOT` before
running; runtime/profile processes are removed afterward. Mark
`NFLBLITZ_BROWSER_SERVING_MODE=extracted-nginx` when testing extracted nginx:
that is **not a successful container run**. Omitting `--smoke` retains the longer
public record/reload/source-wake scenario; no full regulation replay is needed
for this routing change.
