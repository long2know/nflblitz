# Project Context

- **Project:** nflblitz — planned Unity/C# NFL Blitz reimplementation.
- **Created:** 2026-10-15
- **Identity:** Unity Systems (`unity-systems`), Unity Rendering/Systems Engineer.
- **Targets:** Browser play and container hosting; not implemented or benchmarked yet.
- **References:** nflblitz2000 and mahjong-unity, linked in team.md;
  source/asset completeness, dependencies and permissions remain unverified.

## Learnings

- 2026-10-15: Added to the approved roster to separate rendering/runtime from
  Unity's gameplay ownership. Own future Presentation/Runtime scripts and assets.
- Single writer for shared scenes, prefabs, settings and packages; metadata follows
  each asset. Explicit file handoffs prevent GUID and scene conflicts.
- Frontend owns outer browser/CI/container delivery; Quality measures browser
  performance. No initial asset conversion, import or game build scaffolding.
