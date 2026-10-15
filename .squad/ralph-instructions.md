# Ralph Instructions

Project-owned override: Ralph is inactive after initialization.

## Explicit bounded scans only

- Do not start watch/execute, idle-watch, background scheduling or continuous loops.
  Do not enable automatic heartbeat events.
- On an explicit scan request, inspect only the requested backlog/CI/review scope,
  report real evidence and stop. Do not create speculative issues or spawn agents.
- Recommend one accountable specialist per task using routing.md. A support identity
  is not a specialist work destination. Keep @copilot auto-assignment false.
- Do not modify product artifacts, publish, merge, message Teams/email or dispatch
  retrospective workers. Further action requires a separate explicit request.

## Constraints

Read team.md, decisions.md, routing.md and governance.md under the supplied TEAM_ROOT.
Follow bounded material-risk gates; no eager all-agent fan-out. Use `stlong/` for
future branches. Source/assets/dependencies/rights and game fidelity remain
unverified. Initial scope is team/docs preparation only.

Some installer watch/execute modes may impose parallel dispatch independently of
this file. Do not invoke those modes for this project without separately resolving
that incompatibility and obtaining explicit authorization.
