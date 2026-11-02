# Actual source match lifecycle and post-result captain transition

The current declared-boundary graph has passed both exact2371-assertion
baselines, all143 cases, and all13 freshly compiled causal controls with explicit
SDK assets and one mutation worker. This is component qualification, not a
naturally played full match or native-hardware proof.

```sh
python3 -B tests/match-lifecycle/test_runner.py -q
BLITZ_CI_MUTATION_WORKERS=1 python3 -B tests/match-lifecycle/run.py \
  --fixture-manifest "$EXPLICIT_AUTHORIZED_CONTACT272_FIXTURE" \
  --sound-functions "$EXPLICIT_AUTHORIZED_SAFN" \
  --c2-m1-inputs "$EXPLICIT_AUTHORIZED_M1_INPUTS" \
  --output "$NEW_ADMITTED_IGNORED_OUTPUT" \
  --project-assets "$EXPLICIT_ADMITTED_PREPARED_PROJECT_ASSETS" --controls
```

Admission must name the immutable source/harness snapshot, the Contact272
manifest/root, SAFN and M1 roots, new output/intermediate roots, existing .NET10
SDK/reference-pack roots, `PATH`, `DOTNET_ROOT`, `DOTNET_CLI_HOME`,
`NUGET_PACKAGES`, all wrapper/compiler/candidate descendants, and process,
wall-time, CPU/memory/disk ceilings. No authority or unused slots are inherited
from earlier guarded campaigns. No private input roots are discovered here.

Choose a **new** ignored output in this worktree. Compilation remains C#7.3,
warnings as errors, `--no-restore`, single-node/nonpersistent MSBuild and
nonshared compilation. `--project-assets` is an optional explicit, already
prepared/admitted regular `project.assets.json` in ignored worktree scratch.
It is passed as `ProjectAssetsFile`; it does not prepare or restore anything.
With a fresh intermediate tree and no prepared assets, a missing-assets error
is a **stop**, not permission to restore/retry. Missing tools/packs, warnings,
startup errors, timeouts and unrelated failures also stop. No SDK query,
restore, install, cache preparation or network fallback is performed by this
runner. Resource preparation, if needed, needs its own explicit admission.
No Unity executable, DSP, browser, service or new fixture/package is required.
The dedicated CI preparation step restores only this SDK project against an
empty package feed into a named scratch directory, then passes that exact
assets file and a step-local single-worker setting to the unchanged strict
runner. Other suites retain their existing worker settings.

This dedicated executable links the actual `FullKernelInputs` M1 factory,
current kernel and entire production `FixedStepBridge`, not an extracted
terminal function or an `IGameSimulation` double. The existing shared
`production_paths()` inventory (currently103 files) plus the bridge is compiled
unchanged as a104-file production closure. The earlier79-path generation
remains historical; the current guard follows the existing expanded inventory,
without adding the bridge globally or changing production code.

## Retained partition — unchanged

The original71 **declared boundary fixtures** retain exactly593 assertions:
StrictSource and Portable unexpired/earlier quarters, tied overtime, pending late PAT,
both winners through regulation/overtime, maximum-overtime tie, explicit
cabinet decline and its priority, public Continue, actual PAT selection/result/
terminal ordering, tying PAT continuation, per-host event clearing, and
unchanged Legacy60. The real bridge must deliver the typed and legacy events
once in a batched frame, never replay them, then restart and deliver the next
match through the same subscriptions. The actual period producer consumes no
extra RNG. These cases do not claim a naturally played public full match.

Before declaring a PAT boundary, that fixture executes one complete real
60-host/57-source startup cycle to produce its actual camera/cache. Selection
can then retain a genuine prior output while CAMBOT is suspended, rather than
inventing a camera at source tick zero. The cycle is checked before setup and
preserves the next skipped host wake; no camera, source tick or RNG is assigned
to bypass a guard. The original71 cases,593 assertions and all nine controls
remain separate, unchanged partitions. The new grouped station configuration
does not replace the retained fixtures or public single-human defaults.
Rejected baselines report their actual runtime error as well as the exact
partition requirements.

`SourceEventType.MatchEnded` accompanies the committed `GameOver` /
`SourceMatchPhase.MatchEnded` state. The existing emitter supplies current
possession/play-kind context, not a winner or a synthesized final-play result.
Actor/target/station remain absent and `Value` stays0; consumers read final
scores from the snapshot. The legacy `GameEventType.GameEnded` still has its
original score-comparison value. Neither source bus replaces the other.

The nine original isolated production controls must **compile cleanly**, then fail their
exact causal assertions: omitted/duplicate/wrong typed event, missing legacy
event, invented actor payload, premature terminal, stale GameOver batch,
invented RNG and disconnected typed bridge dispatch. Compile errors, crashes,
unrelated errors or zero assertions never count as a rejection. A final
unchanged baseline runs; source/harness/runner/input hashes must stay fixed.

## Added captain partition — fixed before execution

Authority is Lead's byte-authenticated
`historicalsource/nflblitz2000@89f840d58d152a2a9f230cb8d0cdffbd554c8ded:nfl.c`:
107299 bytes, SHA256
`97013eed27a110e11f7180954ed7b7977ee7dc113c372d426392a686948ab434`,
Git blob `15b49474fb55e8bac280b6515fcf7bf59cdf8664`, matching
`tests/source-oracles/pins.json`. Physical LF lines1429 save the ending type;
1914–1920 complement the global captain before the result box at1928 iff:

1. The resolved next play is not kickoff.
2. The saved ending play was not kickoff.
3. Resolved offense differs from the play's initial offense.
4. Resolved offense equals configured first possession.

The existing predicate is unchanged. Its only new production invocation is in
the common `ApplySourcePlayEnd`, after rules/score/result commitment and before
event publication. Initialization and independent kickoff captain logic are
unchanged. No extra event, RNG call, classifier, profile gate or control setter
is introduced.

Exactly72 added IDs have the format
`captain/{profile}/{first-pos}/{initial}/{scenario}`:

- Profiles: `C1-Strict`, `M1-Strict`, `M1-Portable`.
- First possession: `Home`, `Away`; initial captain: `First`, `Second`.
- Scenarios:
  `turnover-on-downs-to-first-pos`,
  `established-possession-turnover-to-first-pos`,
  `next-play-kickoff-no-toggle`,
  `ending-play-kickoff-no-toggle`,
  `offense-equals-initial-no-toggle`,
  `resolved-offense-not-first-pos-no-toggle`.

The first two scenarios qualify (24 cases); the other four isolate one false
guard each (48 cases). Expected captain/result inputs are declared tables,
not values generated by `ToggleCaptainAfterResult`. At midfield, the down4
fixture turns the ball over; the established-possession fixture already has
the receiving side in possession at the declared boundary. The next-kickoff
guard is a safety in the new offense's own end zone. The ending-kickoff guard
is a receiving-side down resolving to normal play. The same-offense guard is
a nonturnover down; the not-first-pos guard establishes the opposite side.
These are **explicit boundary fixtures, not naturally played turnovers or
original execution traces**. Other spot conversions use the unchanged
production conversion functions, not a new numerical/native oracle.

Only these cases use grouped FourStation0/1 Home,2/3 Away,
`InitialPlayerId=-1`, two humans per side. C1 separately calls
`FullKernelInputs.Load(manifest, soundFunctionsPath: safn)` **without** a C2
argument; the existing M1 load remains unchanged for both M1 policies.
Every new case first executes the real60-host/57-source startup cycle and
checks the actual camera/cache; it never assigns source ticks, host
remainders, C2 epochs, captain, first possession or camera output.

The fixture then declares the live result inputs and calls the actual
`EndSourcePlay` entry. C1 commits synchronously and uses the existing
`Publish`. M1 publishes the real queued result without a captain change,
retains it through the actual skipped host wake, and commits on the next
actual `game_proc` wake. Published before/queue/commit/later captain,
host/source ticks, natural C2 epoch and original commit caller are recorded.
Later Publish, four host wakes and public Continue cannot replay the toggle.
The safety case keeps resolved offense equal to first possession, making the
**independent** later kickoff captain adjustment ineligible; it is not treated
as a second result toggle.

In all24 qualifying cases, public station play selection drives the existing
`FinishSourcePlaySelection -> LineUpSourceActors -> ConfigureSourceStations`.
Both teams' actual Captain/SecondPlayer formation control must bind to the
new global slot. No test-only replacement captain is supplied. Actual boundary
checks also preserve classification, both score/quarter-score fields,
spot/down/displayed lines/flags, fractional clock discard/stop, events,
dispatch/process identities and compatibility repairs. C1 checks the complete
immediate boundary. A queued M1 observation also includes the subsequent
original `ball_proc` wake: `player.c:10458-10464` sets `GF_BALL_CROSS_LOS`
(`include/GAME.H:83`, `0x4000`) for a non-kickoff ball at or beyond LOS.
At these declared LOS50 boundaries, this uses the signed actual held-ball
position, not the carrier origin; a wrist can lie on the other side of midfield.
Original input flags stay unchanged. Both the C# assertion and independent
receipt validation require the exact post-wake flags from that predicate and
record a finite ball X; missing or invented bits are rejected.

C1 checks the complete
inline RNG delta; M1 checks draws attributed to the actual committing
`game_proc`, retaining whole-wake counters separately. These are not PRNG,
native scheduler or natural full-match claims.
Typed `PlayResolved` retains the ending actor's team, even on a turnover;
the published possession/captain and legacy possession-change team are
separate fields. Both event buses' actual ticks, actor/target/value/context
are preserved, not rewritten to make the new captain visible.

| Added partition | Cases | Qualifying | Per negative / qualifying case | Assertions |
| --- | ---: | ---: | ---: | ---: |
| C1-Strict |24|8|22 /24|544|
| M1-Strict |24|8|25 /27|616|
| M1-Portable |24|8|25 /27|616|
| Generation + exact-union global checks |—|—|—|2|
| **Added total** |**72**|**24**|—|**1778**|

The required unchanged baselines therefore have **593 retained +1778 added
=2371 assertions**, and exactly **71 retained +72 added =143 unique cases**.
Counts are fixed before execution, never learned from a pass or weakened to
minimums. Every row has an exact field-specific invariant inventory.

Four additional controls edit only the real production integration:

| ID | Mutation | Required runtime field failure |
| --- | --- | --- |
| `captain-omitted-commit-call` | Remove only the integration | First qualifying C1 commit publishes old rather than complemented captain |
| `captain-wrong-ending-type` | Supply resolved next type instead of saved ending type | Kickoff-ending guard incorrectly complements captain |
| `captain-wrong-first-possession` | Invert only first-possession argument | Configured-first-pos qualifying commit retains old captain |
| `captain-double-commit-toggle` | Apply integration twice | Exactly-once commit publishes old captain |

The exact first failing IDs/field paths and partial assertion partitions are
frozen in `run.py`: the three qualifying failures have retained593 and added7
assertions (600 total); wrong-ending has retained593 and added77 (670 total).
A named throw without the corresponding wrong published captain is rejected.
Clean compilation with zero diagnostics precedes every kill; compile, start,
timeout or unrelated failures are never kills.

## Future evidence and bounded execution graph

The required command selects `--controls`: baseline +9 retained controls
+4 captain controls + final unchanged baseline = **15 compilations and15
candidate executions**. `test_runner.py` is a separate Python guard process;
`run.py` is the Python campaign wrapper. Its mutation helper is forced to one
worker and adds no process of its own. The30 direct `dotnet` invocations are
not the entire process tree: nonshared Roslyn compilation and any SDK/MSBuild
descendants also need explicit admission/external accounting. Per-child
240-second timeouts are containment only; they neither admit the job nor
prove intrinsic original bounds. No retry, refund or unused-slot assumption
exists. Without `--controls`, two baselines remain explicitly component-only.

All new build intermediates are under
`OUTPUT/build/{label}/obj`, binaries under `OUTPUT/build/{label}/bin`,
source/harness snapshots under `OUTPUT/copies/{label}`, and process scratch
under `OUTPUT/runtime/{label[-compile]}` (wrapper scratch `OUTPUT/work`).
The optional prepared project assets remain read-only and separately hashed.
SDK/reference packs and caches must already be explicitly admitted; the
runner does not choose or populate them.

The existing `result.json` receipt gains `captain_transition`: exact source,
harness/runner/input hashes and original authority; fixed case/assertion/
control partitions and exact IDs; actual commands/status/exit codes only
when attempted; actual per-run closure/DLL hashes and generated module
identity/MVID reported by the executing production assembly. Production and
harness share the newly compiled module. Neither the old genuine Core MVID
nor its four-shadow guarded MVID is accepted as this snapshot's identity.
Copied sources remain with their PDB paths; no guarded substitute is built.
Before/after hashes protect the canonical sources, harness and inputs.
Harness identity also includes the existing imported Python mutation,
source-inventory and input-hash helpers and the original authority pin file;
copying/hash-listing them adds no C# compilation input or project.

Only after both baselines pass, `captain-cases.json` contains exactly72 actual
final-baseline rows: configuration, declared old/initial/resolved offense,
saved ending/next types, original expected predicate/captain, actual
before/queue/skipped-host/commit/later observations, host/source/caller
identity and invariant outcomes. C1 queue fields are null, not fabricated.
Baseline and final observations also remain in the receipt. Parser guard
fixtures are synthetic in-memory summaries only, never gameplay artifacts.
No actual captain case artifact or execution receipt is created by source
authoring.

Exact pre-edit bytes of all five changed canonical files are preserved under
the routed ignored captain-transition preparation archives, including
`GameSimulation.Source.cs` SHA256
`1a9632c03549dfe70839259cc3065b844e400695148325f65d603ea5db9e885e`.
Its old PDB/source approval remains reviewable; it is intentionally superseded
at the canonical source path, not retroactively approved for this fix.

This is managed real-producer/real-consumer contract evidence, not native
Unity execution, target ABI/PRNG, PCM/DSP or whole-game acceptance. All broader
100%/16-area claims remain open. Transferred test ownership returns to Quality
on the sealed source/harness hash handoff; actual compilation/runtime and the single
focused Quality material gate remain pending separate admission.
