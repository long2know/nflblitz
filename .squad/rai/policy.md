# RAI Policy

> Responsible AI policy for this project. Rai enforces these standards.

Activation and blocking authority follow `.squad/governance.md` and Rai's charter.
There is no automatic review, audit logging or mandatory all-agent approval.
Checks are targeted to the explicitly requested scope or selected material-risk gate.

## Principles

1. **Safety first** — No output should cause harm to individuals or groups.
2. **Transparency** — Users should know when they're interacting with AI-generated content.
3. **Fairness** — Systems should not discriminate based on protected characteristics.
4. **Privacy** — Personal data must be handled with minimal exposure and explicit consent.
5. **Accountability** — Every decision has an owner; every finding has a remediation path.

## Critical Violations (🔴 — Always Blocked)

These CANNOT be shipped. No opt-out. No exceptions.

### Credentials & Secrets
- Hardcoded API keys, tokens, passwords, connection strings
- Private keys committed to source control
- Secrets in environment variable defaults or config templates

### Injection Vulnerabilities
- SQL injection (unsanitized user input in queries)
- Command injection (user input in shell commands)
- Path traversal (user input in file paths without validation)

### Harmful Content
- Hate speech, slurs, or derogatory language targeting groups
- Content promoting violence or self-harm
- Sexually explicit content without appropriate context/gating

### Deceptive Patterns
- Ungrounded factual claims presented as authoritative
- Hallucinated citations, references, or statistics
- Instructions that bypass AI safety guidelines or content filters

## Advisory Concerns (🟡 — Flagged, Not Blocked)

These are recommendations. Work proceeds with suggestions attached.

### Privacy & Data
- PII (names, emails, phone numbers) in logs or responses
- Overly broad data collection without stated purpose
- Missing data retention or deletion policies

### Bias & Fairness
- Algorithms using demographic features (age, gender, race) without justification
- Proxy attributes that correlate with protected characteristics
- Training data with known representation gaps

### Inclusive Language
- Gendered terms where neutral alternatives exist (e.g., "guys" → "everyone")
- Ableist language (e.g., "blind spot" → "oversight", "sanity check" → "validation")
- Culturally assumptive terms (e.g., assuming Western holidays, naming conventions)

### Security Posture
- Missing rate limiting on user-facing endpoints
- Overly permissive CORS or authentication policies
- Insufficient input validation on public interfaces

### Accessibility
- Missing alt text on images
- Insufficient color contrast
- Missing ARIA labels on interactive elements

## Terminology Standards

| Avoid | Prefer | Reason |
|-------|--------|--------|
| whitelist/blacklist | allowlist/blocklist | Racial connotation |
| master/slave | primary/replica | Racial connotation |
| sanity check | validation, smoke test | Ableist |
| dummy value | placeholder, sample | Potentially offensive |
| guys | everyone, team, folks | Gendered |
| man-hours | person-hours, effort | Gendered |

## Review Scope by Change Type

| Change Type | Review Level | Rationale |
|-------------|-------------|-----------|
| Source code (new features) | Full check suite | Highest risk surface |
| Source code (bug fixes) | Credential + injection checks | Targeted risk |
| Documentation | Content + terminology only | Lower risk |
| Test files | Credential checks only | Minimal risk |
| Dependency updates | Skip (fast-path) | No authored content |
| Configuration | Credential checks only | Secret exposure risk |

## Escalation Path

1. **🟢 Green** — No action needed. Work proceeds.
2. **🟡 Yellow** — Suggestions attached to work output. Author decides.
3. **🔴 Red** — Report a critical finding with evidence. Blocking only when Rai is
   the selected gate; otherwise escalate to the coordinator. On a gated rejection:
   - Original author locked out of revision
   - Coordinator assigns a different qualified revision owner
   - Rai does not implement, advise or pair on the immediate revision
   - Focused rereview checks prior blockers, revision delta and regressions

## Policy Updates

This policy evolves. Changes require:
- A justified proposal in the current response; coordinator records accepted
  durable decisions through the normal decisions inbox flow
- No retroactive enforcement (new rules apply forward only)

## nflblitz constraints

Never read live `.env` files or actual Unity license/key material. Never store
credentials, player data or machine-specific paths in team memory. Supported Unity
activation/entitlement and external secret injection are prerequisites for future
licensed builds, not claims already satisfied. No telemetry/accounts/service
infrastructure is assumed necessary. Source and asset rights remain unverified.
