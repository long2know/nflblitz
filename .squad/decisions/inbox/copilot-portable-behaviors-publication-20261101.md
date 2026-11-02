### 2026-11-01: Publish the original-game implementation and Squad decisions
**By:** Copilot
**What:** The user explicitly authorized committing all authored implementation
and Squad data and completing a PR targeting `main`. This supersedes earlier
publication holds for authored source and team records. Original assets,
converted private resources, credentials, runtime caches and generated
validation copies remain excluded from Git.
**Why:** Preserve the actual active implementation, not stale source from the
primary worktree, and publish its test/build/deployment tooling together.

**What:** The explicit Portable runtime now uses independent TYPE3 `random()`
and 64-bit `rand()` state, and minimum-sentinel/insertion receiver ordering.
Pregame, VS and rematches retain the same stream owner. StrictSource guards
and explicitly injected test/oracle providers remain unchanged.
**Why:** Complete receiver ties without an exception and avoid incorrectly
sharing a 24-bit preview stream between the two native random APIs.

**Evidence:** The fresh original-C matrix and 141 additional portable behavior
assertions pass, as does a real named four-quarter managed match, durable
career/world commit, saved custom audibles and rematch/reload. Source algorithms
remain `ExternalUnverified`; no full arcade-hardware equivalence is asserted.
The previously accepted Docker image/archive remain immutable while a distinct
updated image is independently qualified.
