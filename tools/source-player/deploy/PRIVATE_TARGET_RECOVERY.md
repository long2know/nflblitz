# Explicit private-target recovery

The selected private destination is **`ghcr.io/long2know/nflblitz-private`**.
It is not an alias for, or a delivery claim about, the original public
`ghcr.io/long2know/nflblitz` package. This workflow never pushes to, deletes,
overwrites or changes visibility on that original package.

The fixed recovery pins bind the already-runtime-tested image config `7419b62b…`,
its immutable source manifest, all twelve rootfs diffIDs, all178 source/runtime
Git blobs, genuine PUBLIC6ad Core/WASM receipts, final Quality approval and
accepted UID101/1000 runtime proof. No AOT, image or local gameplay campaign is
rebuilt. The frozen `registry_transfer.py` identity/archive/payload checks remain
unchanged.

Run `recover-private-container.yml` manually on normal reviewed `main`.
PR runs validate only. The main recovery job uses the repository job token with
`packages: write`; a separate fresh cold-pull job has only `packages: read`.
There are no arbitrary target or source-image inputs.

Before any original game blob is pulled or pushed, the selected destination must
be verified by the actual package API as private, owned by `long2know`, named
`nflblitz-private`, type `container`, linked to `long2know/nflblitz`, and denied to
anonymous pulls. A404 is not proof of global absence: it permits only an
asset-free, nonexecutable scratch bootstrap at a unique non-`latest` tag.
Unknown/public/unlinked metadata after that bootstrap stops the job without any
game transfer or visibility change.

An existing destination immutable tag or `latest` must resolve to the exact
accepted image and all verified layers; a different image is never overwritten.
Privacy, association and anonymous denial are rechecked before and after the
private immutable and `latest` pushes. Success requires the independent cold
pull, not validation or recovery alone.

Only after that terminal success should consumers select the new image name:

```yaml
image: ghcr.io/long2know/nflblitz-private:latest
platform: linux/amd64
user: "1000:1000"
environment:
  PUID: "1000"
  PGID: "1000"
  BASE_URL: /nflblitz
ports:
  - "8920:8080"
volumes:
  - "${DOCKER_DATA}/nflblitz:/app/data"
restart: unless-stopped
```

The existing runtime remains unchanged: normal writable container layer,
optional unused data mount, ignored `PUID`/`PGID`, browser-origin local saves,
and prefix-preserving routing through `BASE_URL=/nflblitz`. No mandatory
read-only root, user-container tmpfs or relaxed security profile is introduced.
