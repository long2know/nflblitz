# Exact merged-menu public browser image

The current fixed successor is `.github/scripts/merged_menu_image_pins.json`:
image `sha256:e4cb1963…`, archive `d645a1cb…`, build receipt `f2d64550…`,
1349 application files/400151378 bytes and178 verified runtime resources.
Both Enter/numpadEnter and original YES/NO artwork/11-frame animation are included.
All163 source hashes match merged main
`f567d61d8c228aaeb5055c6b47ed916db8b44961` **and** the original compiler revision
`89f9ed0d93cc5aafc75823639b8074c162f6afe7`. The existing actual AOT bundle was
packaged unchanged through `package.py`; it was not rebuilt, relabeled as f567,
static-refreshed or given a `compiledAotReuse` fallback.

The prior latest must be exact manifest `2f14d981…`/config `dd755efb…`.
The original `fresh_main_image_pins.json` below remains unchanged historical
evidence; it is **not** the current release pointer or acceptance certificate.
Older view/cache/private lanes remain unchanged.

## New exact artifact gate

The new saved image requires its own independent Quality certificate. A pending
certificate prevents transfer preparation and workflow publication, even with an
owner `--qa-approved` attestation. Source PR57 approval does not certify this image.
Only after independent artifact approval and the focused publisher PR's normal
merge into main:

```sh
python3 .github/scripts/fresh_main_image.py prepare --qa-approved \
  --archive /private/path/nflblitz-merged-menu-89f9ed0-20261116-amd64.docker.tar.gz \
  --work-dir /private/owner-controlled/parent/new-transfer-directory
gh workflow run fresh-main-image.yml --repo long2know/nflblitz --ref main \
  --json < /private/owner-controlled/parent/new-transfer-directory/workflow-inputs.json
```

The existing encrypted-transfer, full archive/rootfs/config/source/data audit,
ordinary writable UID1000 `BASE_URL=/nflblitz` health/HTTP/header/range,
normal-sandbox C#/WebGL2/audio/cache/view, conditional latest promotion and
separate credential-free anonymous cold-pull checks remain mandatory.
Additionally, **both** publisher and consumer execute the merged
`records-choice.browser.mjs --after-only` against their actual saved/pulled image.
It uses trusted public keyboard/mouse actions, checks Enter and numpadEnter's
unchanged NO/YES results, rejects duplicate font labels and observes every authored
YES/NO flash frame1..11 with its three-source-tick timing. No input state, source
clock or frame is forged. No browser image, original asset or plaintext archive
is uploaded. Only the unique encrypted ciphertext release is transferred.

## Historical fresh270 generation (already delivered)

The previous lane delivered only image `sha256:dd755efb…`, saved archive
`1eee5950…`, compiled source `270c74c90e18b895a564b10b57324dfd08c84bb4`,
build receipt `b898d28e…`, and the existing public AV3061153 data manifest
`51e94caf…`. Full pins are in `.github/scripts/fresh_main_image_pins.json`.
No Unity, rebuild, static-refresh wrapper, arbitrary target/profile, original
asset upload, package visibility change or mutation of historical lanes occurs.
The descendant publisher commit never replaces the honest compiled270 identity.

Quality independently **CONDITIONALLY APPROVED** the exact artifact in
`quality-current-main-image-20261116/report.json`, SHA256
`f82ce0aa61c9065be9116e0a8972d8874de13c9321709caa29a0f671d0019804`.
That report explicitly does **not** authorize promotion. The outstanding ordinary
no-tmpfs runner acceptance is mandatory executable code, not a dispatch waiver.

## Approval, merge, encrypted transfer

Independent Quality acceptance of this exact image is required first. A
conditional acceptance may reserve the proper-runner ordinary-container proof
below; it does not waive it. Merge the reviewed publisher PR normally into
`main`. Only then may the owner attest approval and prepare a new private
POSIX transfer directory using the unchanged crypto/secret/release helper:

```sh
python3 .github/scripts/fresh_main_image.py prepare --qa-approved \
  --archive /private/path/nflblitz-main-270c74c-20261116-amd64.docker.tar.gz \
  --work-dir /private/owner-controlled/parent/new-transfer-directory
gh workflow run fresh-main-image.yml --repo long2know/nflblitz --ref main \
  --json < /private/owner-controlled/parent/new-transfer-directory/workflow-inputs.json
```

Do not pass `--qa-approved` based on author selfchecks. No transfer or workflow
dispatch is performed by opening the PR or running the asset-free tests.
The only uploaded release asset is encrypted ciphertext. The random key travels
through stdin to a unique Actions secret, never argv, logs, receipts or artifacts.
The new workflow accepts only the unique release/secret names and ciphertext
hash; all image/archive/source/build/data/native/module identities are fixed.

## Before any `latest` promotion

On the proper POSIX-backed GitHub runner, authenticate the existing package as
public and linked to this repository; reject changed/missing prior latest
`1e76482f…`/config `00723e58…`. Authenticate ciphertext before decrypting and the
exact saved archive before loading. Verify config, all12 rootfs digests, all1219
application files/338205172 bytes, ownership/modes, full source inventory against
both compiled270 and workflow main, original1153 data, rooted AOT and exact modules.

Run the unchanged image as UID1000 with `BASE_URL=/nflblitz`, an ordinary writable
layer, **no tmpfs, bind mounts, read-only root or relaxed security profile**.
Require real configured health, UID/GID/capability/seccomp/private0700/app0444
checks, every HTTP payload/header/range/prefix check, normal-sandbox Chrome cold
and warm verified cache, game-view/fullscreen layouts and trusted original audio
with actual C#/WebGL2 startup. The author host's optional-tmpfs workaround cannot
pass this gate. No global/native/full-regulation acceptance is claimed.

Only after these gates push the content-addressed immutable tag; verify its
anonymous manifest/config, re-read exact prior latest immediately before a
conditional same-manifest promotion, and stop on unexpected concurrent delivery.
A separate fresh job, without registry credentials, cold-pulls the immutable
digest, verifies current latest and repeats bytes/runtime/browser acceptance.
The workflow is not successfully delivered until this independent consumer
passes. Use its verified immutable manifest digest, not a local tag or PR green.

After terminal success, delete only the unique transfer release/tag and Actions
secret named in its receipt, following `REGISTRY_PUBLISHING.md`. Original
plaintext and Docker credentials are removed from runner work directories on
success or failure. No game payload or screenshot artifact is uploaded.
