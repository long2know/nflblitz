# Squad Team

> nflblitz

## Coordinator

| Name | Role | Notes |
|------|------|-------|
| Squad | Coordinator | Routes work, enforces handoffs and reviewer gates. |

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| Lead | Architect | [Charter](agents/lead/charter.md) | Active |
| Unity | Unity Gameplay Engineer | [Charter](agents/unity/charter.md) | Active |
| Unity Systems | Unity Rendering/Systems Engineer | [Charter](agents/unity-systems/charter.md) | Active |
| Frontend | Web/Build Engineer | [Charter](agents/frontend/charter.md) | Active |
| Backend | Backend/Storage Engineer | [Charter](agents/backend/charter.md) | Active |
| Quality | QA Engineer | [Charter](agents/quality/charter.md) | Active |
| Scribe | Decision Keeper | [Charter](agents/scribe/charter.md) | On accepted decisions only |
| Ralph | Work Monitor | [Charter](agents/ralph/charter.md) | Inactive; explicit activation only |
| Rai | Responsible-AI Reviewer | [Charter](agents/rai/charter.md) | On demand |
| Fact Checker | Verifier | [Charter](agents/fact-checker/charter.md) | On demand |

Six specialists are cast in the provenance registry. Scribe, Ralph, Rai, and Fact Checker
are fixed support identities, not specialist registry entries or work-routing destinations.

## Coding Agent

<!-- copilot-auto-assign: false -->

| Name | Role | Charter | Status |
|------|------|---------|--------|
| @copilot | Coding Agent | — | 🤖 Coding Agent |

### Capabilities

**🟢 Good fit — auto-route when enabled:**
- Bug fixes with clear reproduction steps
- Test coverage (adding missing tests, fixing flaky tests)
- Lint/format fixes and code style cleanup
- Dependency updates and version bumps
- Small isolated features with clear specs
- Boilerplate/scaffolding generation
- Documentation fixes and README updates

**🟡 Needs review — route to @copilot but flag for squad member PR review:**
- Medium features with clear specs and acceptance criteria
- Refactoring with existing test coverage
- API endpoint additions following established patterns
- Migration scripts with well-defined schemas

**🔴 Not suitable — route to squad member instead:**
- Architecture decisions and system design
- Multi-system integration requiring coordination
- Ambiguous requirements needing clarification
- Security-critical changes (auth, encryption, access control)
- Performance-critical paths requiring benchmarking
- Changes requiring cross-team discussion

## Project Context

- **Project:** nflblitz
- **Created:** 2026-10-15
- **Roster:** Approved by Stephen Long on 2026-10-15; descriptive names and stable lowercase-hyphen ids.
- **Goal:** Unity-based C# reimplementation of NFL Blitz, targeting browser play and container hosting.
- **Fidelity:** Full behavioral fidelity is the user's objective, not a verified outcome. Quality owns measurable parity evidence.
- **Reference source:** https://github.com/historicalsource/nflblitz2000 (user-provided reference; archive completeness, build dependencies, and source/asset permissions are unverified).
- **Build reference:** https://github.com/long2know/mahjong-unity (later read-only study of Unity Docker builds and browser serving; not imported or validated here).
- **Stack boundary:** Unity-compatible C# depends on the selected Unity version. Standalone modern .NET services are separate and only added for demonstrated needs.
- **Scope now:** Team configuration and project documentation only; no game implementation, asset import/conversion, Docker game scaffold, API, database, or React requirement.
- **Safety:** Never copy license files or credentials; Unity build activation must use supported entitlement and secret injection outside Git.
- **Coordination:** [Routing](routing.md) defines ownership and handoffs; [governance](governance.md) defines bounded review. Backend stays dormant until justified.
