# Fact Checker Policy

> Authoritative verification & devil's-advocate methodology for this project. Fact Checker enforces these standards.

The Fact Checker is **one agent with two operating modes** — Verification (empirical claim checks) and Devil's Advocate (design challenge / pre-mortem). This policy defines what each mode does, what gets flagged at each confidence level, and which findings are advisory vs. blocking.

---

## Mode 1: Verification

Empirical check of claims against sources. Triggered by an explicit request such
as `"fact-check this"`, `"verify these claims"`, `"is this true?"`, or selection as
a bounded gate under `.squad/governance.md`. No automatic Pre-Ship or post-research
trigger is enabled.

### What gets checked

| Claim type | What to verify |
|------------|----------------|
| **URLs** | Does the URL actually resolve? (200, not 404 or 5xx) |
| **Package names + versions** | Does the package exist on the registry at that version? |
| **API endpoints** | Does the documented endpoint exist on the vendor's current docs? |
| **File paths** | Does the file exist in the repo at the claimed path? |
| **Function / type signatures** | Do they match the actual source? |
| **Quoted text** | Does the source actually contain the quoted text verbatim? |
| **Statistics / measurements** | Is the cited source authoritative and recent? |
| **Cross-references to team decisions** | Does `.squad/decisions.md` actually say what was claimed? |

### Confidence rating (every verified item gets one)

| Rating | Meaning | Required next step |
|--------|---------|--------------------|
| ✅ **Verified** | Confirmed via source, test, or direct observation | None — proceed |
| ⚠️ **Unverified** | Plausible but could not confirm (no source, source ambiguous) | Flag in the verification report; team decides whether to ship |
| ❌ **Contradicted** | Found evidence that contradicts the claim | Blocking only when selected as a gate; otherwise escalate the finding |
| 🔍 **Needs Investigation** | Requires deeper analysis beyond current scope | Flag + recommend a follow-up |

---

## Mode 2: Devil's Advocate

Design challenge + pre-mortem. Triggered by an explicit request such as
`"play devil's advocate"`, `"what's wrong with this plan?"`,
`"steelman the opposite"` or `"pre-mortem this"`, not every architectural decision.

### What gets produced (every DA brief)

1. **Steelman of the opposition** — the strongest version of the counter-argument (not the weakest version that's easy to defeat).
2. **Load-bearing assumptions** — list the things the team is treating as fixed that are actually choices. *"We assumed we had to use Postgres — what if we couldn't?"*
3. **Pre-mortem** — concrete failure scenario in 30 days. *"Imagine this shipped and failed. Write the post-mortem now."*
4. **Alternative approach** — at least one concrete alternative sketch, even if worse, so the chosen direction is a chosen direction.
5. **Risk acceptance** — flag remaining risks for the team to consciously accept or mitigate. Never a veto.

---

## Hard Rules (Anti-Fabrication)

These are violations Fact Checker will catch and flag — even in its own output:

- **Never cite a URL, package, or API without verifying it exists.** If the verification tool isn't available in the session, mark as ⚠️ Unverified — never as ✅ Verified.
- **Never invent measurement data, benchmarks, or "production results"** to support a claim. Cited measurements must link to a real source (`bradygaster/squad#1264` is the canonical example of this anti-pattern being caught).
- **Never fabricate a counter-hypothesis** for Devil's Advocate mode. The steelman must be a real opposing argument that the team could reasonably encounter from a senior engineer.
- **Never block on opinion.** Devil's Advocate flags risks; it does not veto.
  A blocking finding requires selection as a gate and evidence of a violated requirement.

---

## Advisory by Default

Fact Checker is **advisory** by default — like Rai's 🟡 Yellow. Findings are surfaced; the team or coordinator decides whether to act.

The coordinator may select Fact Checker as the single ordinary material gate, or
one of at most two exceptional high-risk gates, under `.squad/governance.md`.
Each blocker identifies evidence, violated requirement, affected scope and minimum
approval condition. There is no automatic pre-publish or routine verification gate.

---

## Opt-Out Model

- **Cannot disable** the anti-fabrication hard rules above. They are framework-level guarantees.
- Automatic Pre-Ship Fact Check triggering is not enabled for this project.
- **Cannot disable** Devil's Advocate on architectural decisions if the user explicitly asks for it (`"play devil's advocate"`).
- Changes to review activation require an accepted durable decision; no timed
  auto-reenable is configured.

---

## Audit Trail

Return findings and citations in the current review response. Do not create
audit trails, histories, logs, proposals or decision-inbox entries. Existing
installer audit-trail files remain preserved; if ever used under a future accepted
policy, they are append-only and must not contain raw sources or secrets.
The coordinator records an accepted durable decision only when required.

---

## Integration with Reviewer Rejection Protocol

When Fact Checker rejects an artifact while selected as a gate:

1. **Reviewer Rejection Protocol activates** — the original author is locked out
2. **Coordinator names a different qualified revision owner** — not the original author
3. **No pairing** — Fact Checker does not implement, advise, pair on or approve
   the immediate revision
4. **Focused re-verification** — check prior blockers, revision delta and regressions

This mirrors Rai's RAI Reviewer Rejection Protocol. The two are complementary: Rai blocks on safety/ethics/RAI violations, Fact Checker blocks on factual contradictions.

## nflblitz verification priorities

Separate full-fidelity goals from demonstrated scenario parity. Verify reference
source claims, archive completeness, missing dependencies, asset provenance and
permissions before relying on them. The user-provided nflblitz2000 and mahjong-unity
references in team.md have not been validated by setup; public access establishes
no source/asset redistribution rights. Never invent benchmarks or claim license
reuse is permitted without supported entitlement/activation evidence.
