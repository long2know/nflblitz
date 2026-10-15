# Squad Decisions

## Active Decisions

### 2026-10-15: Approved initial team and project constraints

**Approved by:** Stephen Long; recorded by Lead during authorized initialization.

- Use the ten descriptive identities in [team.md](team.md), including Unity Systems.
  Cast only the six specialists; keep Scribe, Ralph, Rai and Fact Checker as fixed
  support identities. Preserve @copilot capabilities and auto-assignment false.
- Target a Unity C# reimplementation of NFL Blitz, browser-playable and
  container-hosted. Full behavioral fidelity is the objective, not a verified claim.
- The user-provided nflblitz2000 archive is a reference only: completeness,
  dependencies, source/asset permissions and redistribution rights remain unverified.
  Study mahjong-unity later for build/hosting patterns; do not copy its license files.
- Select a Unity version and compatible C# profile before implementation.
  Modern standalone .NET backend services are separate and conditional on need.
  No React, API, database or service layer is presumed necessary.
- Separate gameplay ownership (Unity) from presentation/runtime/scene ownership
  (Unity Systems); require explicit file/meta handoffs. Frontend owns outer browser,
  CI and container integration; Quality owns parity and browser-performance evidence.
- Prepare only team/docs now. No implementation, large source import, asset conversion,
  game Docker scaffold, commit, push, PR or monitoring loop during this stage.
  Future branches must use `stlong/`.

## Governance

- [governance.md](governance.md) defines ownership and bounded review; ordinary work
  has at most one material blocking gate, not all-agent consensus.
- Scribe merges only accepted, durable decisions from the inbox. Preserve decision
  history; do not manufacture session records or speculative work.
