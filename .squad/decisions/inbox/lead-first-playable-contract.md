### 2026-10-15: First-playable root project and kernel contract
**By:** Lead
**What:** The newly authorized local playable implementation uses root `Assets/`,
an engine-independent `Blitz.Gameplay.GameSimulation` implementing
`Blitz.Contracts.IGameSimulation`, and the v1 scopes/behavior in
`docs/architecture.md`. Source-backed targets and nonparity are recorded in
`docs/source-parity.md`; no whole-game fidelity is claimed. Unity Systems alone
writes Unity shared configuration and every `.meta`; Quality alone writes the
linked-source test project under `tests/`. Backend remains dormant.
**Why:** Parallel gameplay, presentation and build work need a stable boundary,
clear single writers, and a complete small loop rather than disconnected demos.
Implementation authorization supersedes setup-only scope, not publication limits.
