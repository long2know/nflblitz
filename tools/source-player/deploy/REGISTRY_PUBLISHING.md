# Publish an already accepted private image

This lane **does not rebuild** the game. It transfers one independently accepted
`docker save` gzip archive and pushes that exact image to
`ghcr.io/long2know/nflblitz:latest`. Browser acceptance, source/PR review, and the
normal merge to `main` must finish first. A local archive or passing publisher
unit tests are not gameplay acceptance.

The workflow uses its own job-scoped `GITHUB_TOKEN` with `contents: read` and
`packages: write`. It never exports that token. PR validation is asset-free,
read-only, and secret-free; only a manual dispatch from this repository's `main`
can publish. Repository and package permissions are not changed.

## 1. Prepare after independent QA approval and merge

Run the helper from a private POSIX home directory, not a shared mount that
ignores `chmod`. The output directory must be new and its parent must exist.
Use the accepted archive's actual sidecar checksum and the exact accepted image,
build-info, compiled-source, original-data-manifest, and source-revision pins:

```sh
python3 /path/to/merged/repo/tools/source-player/deploy/registry_transfer.py prepare \
  --qa-approved \
  --archive /path/to/accepted-image.tar.gz \
  --output-dir ./nflblitz-registry-transfer \
  --archive-sha256 ACCEPTED_ARCHIVE_SHA256 \
  --image-id sha256:ACCEPTED_IMAGE_CONFIG_SHA256 \
  --source-tag nflblitz-original:ACCEPTED_TAG \
  --build-sha256 ACCEPTED_BUILD_INFO_SHA256 \
  --source-digest ACCEPTED_COMPILED_SOURCE_SHA256 \
  --data-manifest-sha256 ACCEPTED_DATA_MANIFEST_SHA256 \
  --source-revision ACCEPTED_SOURCE_COMMIT
```

`--qa-approved` is the owner's explicit attestation, not an automated QA result.
The helper verifies the plaintext archive and its saved image configuration,
generates a random 384-bit passphrase in memory, and encrypts with OpenSSL
AES-256-CBC, random salt, PBKDF2-HMAC-SHA256, and 600,000 iterations. The
passphrase goes through stdin only to OpenSSL and `gh secret set`. A unique
`CONTAINER_TRANSFER_<random>` repository Actions secret is created; existing
secrets are not overwritten.

Only `image.tar.gz.enc` is uploaded, to a unique ciphertext-only prerelease with
`--latest=false`, pinned to the current merged `main` commit. Neither the key nor
plaintext assets are attached to a release, committed, or uploaded as Actions
artifacts. `receipt.json` and `workflow-inputs.json` contain nonsecret pins and
the secret **name**, never its value. Do not lose or edit these trusted inputs:
CBC is not an authenticated-encryption format; the independently pinned
ciphertext SHA256 is checked **before decryption**, and the independently pinned
plaintext SHA256 is checked **before Docker load**.

## 2. Dispatch and verify

```sh
gh workflow run publish-container.yml --repo long2know/nflblitz --ref main \
  --json < ./nflblitz-registry-transfer/workflow-inputs.json
gh run list --repo long2know/nflblitz --workflow publish-container.yml --limit 5
gh run watch RUN_ID --repo long2know/nflblitz --exit-status
```

Before any push, authenticated package metadata must show an existing **private**
package linked to this repository; a new package may return 404, but an
anonymous public-package check must also return 404. Unauthorized, public, or
unknown metadata fails closed. First publication through Actions uses GHCR's
private default and automatic workflow-repository association. The workflow
never changes visibility or makes the package public.

The job validates the saved config identity, linux/amd64 platform, non-root
runtime, and source labels before load. After load it checks the exact image ID
and copies only build-info/data-manifest receipts from a stopped, network-disabled
container, without starting an entrypoint. All receipt hashes/source inventory
must match before any tag or push.

The content-addressed tag `sha256-<accepted image ID>` is pushed and its remote
manifest's config link is checked before pushing `latest`. Both remote manifest
digests must match, and package privacy is checked again. The Actions summary
contains the final nonsecret digest receipt. No registry success should be
claimed before those checks pass. Users pulling this private package still need
normal authorized GHCR authentication.

## 3. Remove the transfer after verified publication

Use the generated names from `receipt.json`, not unrelated repository secrets:

```sh
gh release delete RELEASE_TAG --repo long2know/nflblitz --yes --cleanup-tag
gh secret delete SECRET_NAME --repo long2know/nflblitz --app actions
```

Then remove the owned local transfer directory. The job removes runner-local
plaintext, ciphertext, and Docker credentials on success or failure; no payload
artifacts are retained. Cleanup is deliberately an owner action because the
publisher job has no repository write or secret-management permission.

## Asset-free checks

```sh
python3 -B tools/source-player/deploy/test_registry_transfer.py -v
```

These use real standard OpenSSL encryption and public synthetic data only.
Docker/GitHub mutations are mocked; tests prove rejection ordering, exact-image
and receipt checks, private-package gates, token/key non-disclosure, and immutable
remote-config verification. They do not prove a registry push or gameplay QA.
