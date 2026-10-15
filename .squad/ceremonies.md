# Ceremonies

> Team meetings that happen before or after work. Each squad configures their own.

## Design Review

| Field | Value |
|-------|-------|
| **Trigger** | on request |
| **When** | before |
| **Condition** | concrete cross-boundary contract or shared-file handoff is needed |
| **Facilitator** | lead |
| **Participants** | accountable owner and required bounded collaborator only |
| **Time budget** | focused |
| **Enabled** | ✅ yes |

**Agenda:**
1. Review the task and requirements
2. Agree on interfaces and contracts between components
3. Identify risks and edge cases
4. Assign action items

---

## Retrospective

| Field | Value |
|-------|-------|
| **Trigger** | on request |
| **When** | after |
| **Condition** | build failure, test failure, or reviewer rejection |
| **Facilitator** | lead |
| **Participants** | accountable owner and evidence holder only |
| **Time budget** | focused |
| **Enabled** | ✅ yes |

**Agenda:**
1. What happened? (facts only)
2. Root cause analysis
3. What should change?
4. Action items for next iteration


---

## Retrospective with Enforcement

| Field | Value |
|-------|-------|
| **Trigger** | disabled |
| **When** | not scheduled |
| **Condition** | requires a separate explicit request |
| **Facilitator** | lead |
| **Participants** | accountable owner and evidence holder only |
| **Time budget** | focused |
| **Enabled** | no — no scheduled enforcement or speculative issue creation |

**Agenda:**
1. What shipped this week? (closed issues, merged PRs)
2. What did not ship? (open issues, blockers)
3. Root cause on any failures
4. Report evidence-backed follow-ups; do not create issues without authorization

**Coordinator integration:**
Disabled at initialization. No retrospective worker or enforcement skill is
configured. Do not dispatch it or treat it as a blocking gate.

**Why GitHub Issues, not markdown:**
No project measurement has been made. Do not infer this project's outcomes from
installer examples. Review limits in governance.md apply to every ceremony.
