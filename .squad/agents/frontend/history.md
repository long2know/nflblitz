# Project Context

- **Project:** nflblitz — planned Unity/C# NFL Blitz reimplementation.
- **Created:** 2026-10-15
- **Identity:** Frontend (`frontend`), Web/Build Engineer.
- **Targets:** Browser play and container hosting; no current build/deployment proof.
- **References:** nflblitz2000 and mahjong-unity, linked in team.md;
  the latter is for later read-only build/hosting study, not validated reuse.

## Learnings

- 2026-10-15: Own outer browser integration, CI workflows, Docker builds and
  container hosting. Unity Systems owns engine runtime and shared settings.
- No React/API/database prerequisite; keep hosting minimal.
- Unity entitlement/activation must be supported; inject secrets externally.
  Never read or copy actual Unity license files. No Docker game scaffold now.
