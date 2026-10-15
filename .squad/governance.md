# Team Governance

Approved initial team scope: 2026-10-15.

## Ownership

Assign one accountable specialist using [routing.md](routing.md). Collaborators
receive only bounded work with explicit file ownership. Support identities are not
specialist routing destinations. Scribe merges accepted decisions only. Ralph is
inactive unless explicitly requested; no ongoing monitoring loop is enabled.

## Review

- Ordinary work has **at most one material blocking gate**, only when actual risk
  warrants it. No mandatory all-agent consensus or routine pre-ship fan-out.
- Lead selects the qualified reviewer for the risk. Quality owns parity evidence
  and browser performance validation; Rai and Fact Checker are advisory unless
  selected as a gate.
- Exceptional high-risk work may have a primary gate plus one orthogonal specialist
  gate, with the risk and scope stated before dispatch.
- A blocker must identify evidence, the violated requirement, affected scope and
  the minimum approval condition. A rejecting reviewer cannot implement or pair
  on the immediate revision; the original author is locked out of that revision.
  Assign a different qualified revision owner, then perform focused rereview.
- Repository merge requirements, if later established in
  `.github/PR_REQUIREMENTS.md`, and actual remote branch protections also apply.
  The coordinator must verify current repository requirements before publication;
  this document does not itself approve a PR or make one optional.

## Scope and publication

Behavioral fidelity is an objective requiring source-backed, reproducible evidence.
Archive completeness, assets, permissions, trademarks and build dependencies remain
unverified. Do not import or redistribute material until rights are established.
Do not create speculative issues or unneeded React, API, database or service layers.

Publication requires coordinator verification and explicit user authorization
before any commit, push or PR. Future branch names use
`stlong/`, not installer `squad/` examples. Never commit secrets, Unity license/key
files, runtime sessions or machine-specific state.
