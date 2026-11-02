### 2026-11-02: Bind inherited checks to the actual compiled source generation
**By:** Lead integration
**What:** Keep the accepted R2 game and all historical replay identities unchanged.
Current compiler/mutation helpers must include Storage and all three actual audio
selectors, follow the extracted process host, and preserve output ordering,
selected banks, instruction-reset hooks and exact source provenance.
**Why:** Late inherited checks exposed stale source inventories and ambiguous
mutation anchors, not a reason to weaken runtime or replay guards.

The interface ancestry gate explicitly admits the twelve current profile
identities and their exact boolean ancestry. Original non-M1 exclusions remain
required; dropped successor links and broad-profile promotion are rejected.
Four unreferenced malformed 33-hex Unity GUIDs are repaired and two missing
script metadata files are supplied. Compiled R2 C# and browser assets do not change.

Standalone workflow guards use the existing immutable inherited-workspace
interface; their historical command/hash contracts are not rewritten.
Hosted CI, normal main merge and private registry publication remain separate
release gates. These checks do not certify native arcade or pixel equivalence.
