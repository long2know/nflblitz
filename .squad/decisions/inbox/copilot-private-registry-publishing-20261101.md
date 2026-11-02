### 2026-11-01: Publish the accepted private image inside GitHub Actions

**By:** Copilot
**What:** Use a manual, main-only Actions publishing job with scoped
`packages: write`. The job publishes the already accepted image; it does not
rebuild it or export its workflow token to the local machine. Original assets
remain excluded from Git and plaintext public release artifacts.
**Why:** The local OAuth credential can publish source/workflows but lacks
package scope. Its device refresh timed out. The standard job token provides
the requested registry workflow without another local credential handoff.

The accepted archive is transferred as ciphertext only. The generated
passphrase goes through memory/stdin to a unique Actions secret, never to Git,
arguments, receipts or logs. Trusted ciphertext/plaintext hashes, image config,
platform/nonroot runtime and embedded build/source/data receipts are checked
before publication. `latest` and the immutable tag must resolve to the same
accepted config and manifest, with private package visibility.

The first publisher was rejected because anonymous GitHub package metadata
requires authentication even for a missing package. An independent revision
uses the real GHCR anonymous token contract instead: only an explicit
403/DENIED allows a new-package attempt; public token issuance or unknown
responses fail closed. Twelve real-crypto/failure-gate tests pass; the new
regression rejects the original implementation. Actual missing/private and
public namespace responses and real Docker manifest parsing were verified.

Prepare/upload/dispatch remain gated on independent exact-image QA, hosted
checks and normal main merge. No permission, trust, sandbox or CI bypass is
authorized. No native hardware equivalence is asserted.
