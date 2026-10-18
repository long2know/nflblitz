# Source57 fire and handicap state machines

This suite compiles the actual production `GameSimulation`, strict existing
roster/playbook/movement data readers, original animation VM, real fifteen-pivot
geometry, and actual C1/C2/M1 factories. It is a controlled component suite,
**not a public-input replay, whole game, Unity execution, or native acceptance**.
LegacyBaseline60Hz remains a separate source-free route.

## Explicit local inputs

Use the installed SDK and C compiler. All converted inputs must be the existing
strict local, ignored generations; there is no acquisition, download, synthetic
library, new table, profile alias, or resource-identity change.

```sh
python3 -B tests/fire-handicap/run.py \
  --source-root "$PINNED_ORIGINAL_ROOT" \
  --handicap-header "$PINNED_HANDICAP_HEADER" \
  --fixture-manifest "$CONTACT272_FIXTURE" \
  --sound-functions "$OLD_SIX_BINDING_SAFN" \
  --old-c2-inputs "$OLD_NINE_288_FILE_ROOT" \
  --c2-m1-inputs "$M1_288_FILE_ROOT" \
  --output .squad/.scratch/fire-handicap-local
```

`--handicap-header` is the explicit original `include/handicap.h` file, blob
`45eef32b32721196e4575e59a0fd2ce01e0fbbdb`; some existing smaller original roots
do not contain it. Every consumed original function/header/data blob is checked.
The output must be new and inside this worktree's ignored `.squad/.scratch`.
Missing SDK assets trigger an offline, empty-feed restore only after NETSDK1004;
there are no new package dependencies. Source/build mutation copies are removed,
while ignored reference provenance, transcripts, commands and receipts persist.

## Original reference, not a rewritten expected engine

`oracle.py` extracts active original C at runtime from revision
`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`:

- `player.c:2231-2350`: ordinary completion/control, pending landing and turnover;
  `4588-4617`, `11040-11073`: reset functions and receiver-owned completion.
- `player.c:5953-5984`: forced `player_down` control has **no** ordinary turnover
  fire reset.
- `nfl.c:1396-1503`, `1610-1891`, `1943-1953`: control restoration, rounding,
  classification, loss-yard fire, result-switch resets, result dismissal.
  The actual active `result.c` classifiers are compiled too.
- `handicap.c:34-210`: unchanged complete `set_handicaps`; original nine-row
  `teamdata.c:1061` difficulty data. Only `teaminfo.drone_base` is independently
  projected from its active original initializer. It is not copied from the
  production roster JSON or a hand-authored expected table.
- `nfl.c:1114-1135`: original quick-kick setup, original two ordered draws and
  actual original min/max tables. Both nonneutral receiving-side extremes are
  compared bit-for-bit through the actual kickoff caller.

The receipt records byte ranges, physical source lines, original blob/content
hashes, and the sole data projection. All extracted source, numeric data,
executables and reference outputs stay ignored/local. Platform sound/render
calls and spawned SPFX processes are excluded seams; the unrelated live
first-down sound predicate is disabled in the carrier reference. No target
memory/ABI, original platform RNG algorithm, renderer, audio DSP, or Goose
scheduler is emulated or qualified.

## Assertions and causal controls

The retained feature inventory has **683 original-C calls**: 258 fire/control/result cases,
415 handicap cases, and ten integrated reference calls. The exact per-category
inventory is in `run.py`. C1 has **10,614** assertions; explicit old-nine C2 and
M1 each have **10,623**, the extra nine checking their real deferred result
boundary. FH-01 adds the two cases and assertions below; these retained counts
and all fourteen original controls are not rebased or replaced.

### FH-01: incomplete fourth-down reset owner

Original `nfl.c:1694-1712` restores `off_init` inside `PR_INCOMPLETE` before
fourth-down fire resets, then flips possession. After opposite-side CONTROL_TIME
and bobble, the classified/last-possession side can differ from initial offense.
The reset owner must therefore change **only inside that incomplete turnover
branch**. Classified-side loss-yard processing and ordinary/fumbled-out-of-bounds
turnover resets retain their existing owner.

Two mirrored, defined original-C cases exercise actual
`SourceContactComposition.CreateSimulation`, old-nine
`SourceC2Composition.CreateSimulation`, and `CreateM1Simulation`, followed by
the real `EndSourcePlay` caller. C2 tests assert the captured result is queued,
fire is unchanged before commit, and the actual next-source-tick
`CommitSourceC2PlayEnd` clears the queue. Native reference inputs match both
review vectors exactly, including their source-world coordinates. Both mirrors
failed the named receiving-fire/state assertion on rejected production through
all three factories before the correction.

Each profile now requires **685 original-C calls (683 retained +2 FH-01)**.
The extra cases compare all22 reference fields, including both fire bits,
completion/loss counters, last receivers, final possession/down/LOS and result.
They add **48 C1 assertions** or **54 old-nine/M1 assertions** (the extra six
are queue boundaries), for totals **10,662 / 10,677 / 10,677**. The fifteenth
compiled causal control restores the wrong classified-side reset owner and
must fail `FH01 receiving fire/state survives incomplete original-offense
restoration`, after consuming original C, not at build or input loading.
The compiled check executable also accepts focused `FH01`, `FH01-home`, and
`FH01-away` sections with the same seven explicit arguments; these are
controlled result fixtures, not public replay or scheduler evidence.

The first author run exposed an important fixture boundary: source world29 can
round toward midfield to classified field30, so it is not automatically a loss
against LOS30. Sixteen field20 loss vectors were added while retaining that
boundary, adding 368 assertions/16 C calls to the new suite. The original
comparison and compiled loss-trigger mutation, not a rewritten expected rule,
discriminate the corrected test inputs. No existing suite count was changed.
An additional already-lit re-entry is compared with original C after the real
third-completion sequence, before the contact consumer (+17 assertions/one C
call). Thus even its early-failing causal control consumes the independent
reference; the runner's nonempty-reference requirement was not weakened.

Coverage includes source18 versus17/19, both teams, third completion versus
second, receiver versus controller, behind/on/across LOS, bobble/lateral
noncompletion, airborne pending credit after play-over, ordinary versus forced
possession, kickoff exception, bounce identity, classified-side ownership,
strict loss versus nonloss, second loss versus already-passed counter, first
down/TD/FG/miss/PAT/fourth-down cases, and a defined `INT_MAX-1` counter input.
It never executes original signed overflow or an out-of-bounds original read.

Real `Step` wakes, catches/controller transfers, PlayerDown callbacks, queued
game-proc commits, contact loss decisions, pass velocity/flags, play selection,
lineup, AI parameters, actor/match snapshots, result dismissal, periods,
program-boot restart and quick kick consumers are tested. Pure handicap cases
also cover all31 team records, human/CPU combinations, quarter weighting,
no-assist/powerups, random-opponent base, win-streak boundaries and the original
five-play identity history.

Fourteen isolated, **compiled zero-diagnostic** mutations alter real production
trigger/order/read paths: completion and loss thresholds, control time,
post-update LOS/final-side ownership, forced-control reset, pending landing,
premature result clearing, history producer, actor/AI scalar reads, contact fire
read, neutral quick-kick row and an invented consumer clamp. Each must fail its
named field assertion; a build error or status-only failure is not a kill.

Five vectors intentionally obtain original, defined `set_handicaps` outputs
outside its twelve subsequent consumer rows, including the active drone-base19
record and unclamped repetition adjustments. The raw values must match C, then
production must reject the undefined table consumption. A below-three base is
separately rejected **without** executing the original negative-index read.
These are disclosed domain limits, not corrected-semantics approval.

The existing six-int `bc_adjust[6]` failure, NoEligible disclosure, positive
R_SKIP gate, immutable profile/data identities, CI/input acquisition and all
sixteen whole-game/native rows remain unchanged and open.
