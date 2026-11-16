# Exact fresh270 public browser image

This separate lane delivers only image `sha256:dd755efb…`, saved archive
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
