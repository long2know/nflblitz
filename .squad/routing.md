# Work Routing

Assign exactly one accountable specialist. Add a bounded collaborator only for a
concrete dependency; names, ids, and charter paths come from [team.md](team.md).

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Architecture, scope, implementation review | Lead | Source-parity scope, interfaces, Unity version decisions, integration review |
| Unity gameplay and source parity | Unity | C# simulation, rules, AI, controls, scoring, deterministic game state |
| Unity rendering and runtime systems | Unity Systems | Rendering, animation, asset integration, scenes, Unity browser runtime |
| Browser integration, CI and container builds | Frontend | Web loader, Docker Unity build automation, container hosting, HTTP delivery |
| Backend services and storage | Backend | Justified standalone .NET services, persistence contracts, save-data format |
| Gameplay accuracy, regression tests and browser performance | Quality | Reference scenarios, parity evidence, regression suites, browser frame/memory/loading measurements |

Only these six specialists are work-routing destinations and registry entries.
Their stable ids are `lead`, `unity`, `unity-systems`, `frontend`, `backend`, and
`quality`, respectively.

## Unity ownership and handoffs

The following are ownership boundaries for future files, not claims those files exist.

- **Unity:** `Assets/Scripts/Gameplay/**`, rules, AI, input interpretation and simulation tests.
  Keep the simulation independent of presentation where practical; publish state/event contracts.
- **Unity Systems:** `Assets/Scripts/Presentation/**`, `Assets/Scripts/Runtime/**`,
  imported assets, materials, animation controllers, scenes and prefabs. Own Unity
  browser runtime behavior, graphics/memory choices and engine-side loader interface.
- **Frontend:** outer browser page/loader, HTTP headers/compression, Dockerfiles,
  hosting configuration and CI workflows. Do not edit Unity scenes or rendering code.
- **Backend:** save-data contracts and any justified persistence/service code.
  A Unity-side save adapter must use the selected Unity C# profile, not assume
  standalone .NET APIs. Coordinate its exact path before implementation.
- **Quality:** test fixtures, parity traces and performance evidence. Coordinate
  engine-side test harness changes with the owning specialist before edits.
- A `.meta` file follows its asset owner. Never regenerate another owner's GUIDs.
  Unity Systems is the single writer for shared `.unity`, `.prefab`, `ProjectSettings/**`
  and `Packages/**` files; others request a bounded change/handoff.
- Before a cross-boundary task, Lead records exact files, interfaces, acceptance
  criteria and writer. A handoff releases ownership explicitly; no concurrent scene,
  prefab, asset/meta or package/settings edits. One accountable specialist remains
  responsible for the integrated outcome; collaborators do not start speculative work.

## Support activation (not specialist routing)

- **Scribe (`scribe`):** silently merge accepted durable decisions only; no automatic
  session logging, history rewriting or commits.
- **Ralph (`ralph`):** inactive at initialization. Explicitly requested bounded backlog
  scans only; do not start a watch, heartbeat loop or ongoing execution by default.
- **Rai (`rai`):** targeted safety/privacy review on request or when selected as the
  bounded material-risk gate; not an automatic ship gate.
- **Fact Checker (`fact-checker`):** source claims, asset provenance and assumption
  verification on request or as the selected gate. Unverified is not verified.

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Lead |
| `squad:{specialist-id}` | Own the explicitly scoped issue | Named specialist |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, the **Lead** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a specialist's `squad:{member}` label is applied, that specialist owns the
   scoped work. Support labels do not authorize implementation; use support
   activation above.
3. Reassign ownership explicitly before removing one specialist label and adding
   another; never leave multiple accountable owners.
4. The `squad` label is the "inbox" — untriaged issues waiting for Lead review.

## Rules

1. One accountable specialist per task. No eager fan-out, anticipatory downstream
   work, routine support-agent spawn, or automatic issue creation.
2. "Team" is not permission to spawn everyone. Limit collaboration to bounded,
   named outputs required for the current task.
3. Quick factual/status answers can be direct; do not create an agent for a lookup.
4. Apply [governance.md](governance.md): ordinary work has at most one material
   blocking gate, selected for actual risk; advisory collaboration is not a gate.
5. Issue labels use stable ids, e.g. `squad:unity-systems`. Lead triages `squad`;
   specialist labels identify the accountable owner. Support labels are not
   permission for autonomous implementation. @copilot auto-assignment stays false.
6. Track fidelity through bounded reference scenarios, expected/observed behavior,
   and reproducible tests; never claim whole-game parity from partial evidence.
7. Preserve current work. Preparation needs no new branch; future branches must
   begin `stlong/`. Publish only on explicit authorization.
