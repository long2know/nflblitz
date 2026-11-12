# Source possession-time consumer — authored, not executed

Component: `source-statistics-clock-consumer-time-of-possession/v1`.
Unity is the sole source author; Quality released only this document,
`SourceTimeOfPossessionChecks.cs`, and `possession_time.py` in this directory.
There is one future Quality gate, after separate parent execution admission.
No current test, syntax check, compiler, SDK, managed application, engine or
native execution is represented by this preparation. All new execution is **UNRUN**.

## Bounded production contract

`SourceClock.Step` already supplies a logical displayed-second credit on its
`Second` and `Minute` operations. The new immutable two-team view retains that
existing delta. It does not implement a new clock or a general statistics system.
The authenticated Lead contract is
`.squad/.scratch/source-following-active-p1/metadata/next-after-digital/next-contract.json`
(24,542 bytes; SHA256
`de8747154be9a87dc4732814a9c9b0269fa0079f4525bfb2634b3f8bcba40295`).
Its fixed-revision authority is `historicalsource/nflblitz2000`,
revision `89f840d58d152a2a9f230cb8d0cdffbd554c8ded`, `nfl.c`, physical LF
2916–2991, including the zero-fraction/current-offense credit predicate.
No original body, assets, binary or native scheduling evidence is included here.

The five production placements are exactly:

1. `Contracts/SourceMatchContracts.cs`: sealed `SourceTimeOfPossessionView`,
   get-only nonnegative `HomeSeconds`/`AwaySeconds`, a get-only match property,
   and the ledger-first overload. The complete original constructor, including
   its signature, defaults and body, is retained byte-for-byte. The new overload
   delegates to it before rejecting a null ledger and assigning the property.
2. New `Gameplay/SourceTimeOfPossession.cs`: pure `Accumulate` validates the
   prior view, Home/Away offense and delta 0 or 1, including on zero. Zero returns
   the same prior reference. One uses checked arithmetic on only the selected
   side; it neither alters the prior view nor clears the other team.
3. `Gameplay/SimulationState.cs`: a fresh 0/0 ledger immediately after
   `SourceClock`. A fresh model is the only reset.
4. The real `StepSourceClockProcess` in `GameSimulation.Possession.cs`: one
   assignment after successful process entry and `Step`, before clock state,
   public clock view and process completion, using current `Model.Possession`
   and exactly `clock.TimeOfPossessionDelta`.
5. `Gameplay/GameSnapshot.cs`: the actual source-only match construction gains
   the leading current-model ledger. Every old argument through
   `CaptureAudibles(model)` is preserved. Non-source `Match` remains null.

An old-constructor null ledger is **unobserved**, not a zero total. An observed
new source match starts at 0/0. Immutable retained match views cannot acquire
later credit. Credit units are logical displayed seconds, including a minute
rollover, not 57-Hz ticks, 60-Hz frames, wall seconds or packed-time subtraction.
Ownership changed before a clock call affects that credit; a later change can
affect only a later credit. Publication does not reattribute past totals.

`ResetPlayTime`, `DiscardFraction` and period/half/overtime clock recreation
retain the ledger. Paused, attract, fractional, not-due, exited and stopped
clock calls add nothing. The frozen actual dispatcher keeps game process,
clock, actor and ball order. Static source preservation supports these caller
and lifetime statements; it is not execution of `GameSimulation`, its constructor,
`Restart`, a turnover, terminal publication or a Unity scheduler.

## Exactly 563 new assertions, independently literal

The new executable entrypoint uses the actual unchanged `Checks` class in
`Program.cs`. It does not execute that old `Program.Main`. A separate retained
executable selects the unchanged old entrypoint and its unchanged three check
files. No old project, test, documentation, threshold or runner is edited.

| Partition | Cases/checkpoints | Fields | Assertions |
| --- | ---: | ---: | ---: |
| Clock rows, each Home and Away with prior 11/23 | 32 | 14 | 448 |
| Synthetic immutable sequence checkpoints S0–S13 | 14 | 6 | 84 |
| Guards G01–G31 | 31 | 1 | 31 |
| **New entrypoint** | | | **563** |
| Separate unchanged retained Program | | 511 + 1232 + 1 | **1744** |
| **Each initial/final runner pair** | | | **2307** |

There are no fixture/discovery assertions or extra counted assertions.
The C# rows reproduce the Lead contract literally; expected fields are not
computed from `Step`, `Accumulate`, an observed delta or a discovered output.

Clock row IDs are `minute`, `second-normal`, `low-q1-odd`, `low-q3-even`,
`long-play-18`, `last-second`, `subtick`, `final-fraction`, `paused`, `attract`,
`not-due`, `exit`, `already-complete`, `bad-adjustment`, `read-failure`,
`discard-republish`. Each asserts operation, packed/local time, play time,
next wake, completion, delta, sleep, warning, adjustment read/fallback,
period expiry, then Home/Away totals, in that order.

The six sequence fields are packed time, local time, play time, next wake,
Home seconds and Away seconds. S0/S13 are fresh pure clock/ledger pairs;
S1/S2 include a repeated not-due call; S3/S6 consume exactly 24/20 fraction
wakes; S4/S5 fork before/after an ownership change; S8–S10 retain the ledger
through play-time reset, fractional discard, pause and local republication;
S11 recreates a quarter clock using retained totals; S12 synthetically stops it.
These are pure API calls, not a simulated actual process or executed restart.

The 31 guards cover zero construction, both negative inputs, null prior on
delta one and zero, invalid offenses even on zero, invalid deltas, selected-side
checked overflow and unchanged prior views, zero-reference retention at
`INT_MAX`, legacy unobserved/null versus specified ledger, new-overload null
rejection, retained match/S12 views, unselected-side `INT_MAX`, and negative
clock tick. Wrong exception types become named failed assertions.

## Eight compiled controls; seven different STATIC controls

Each compiled control changes one uniquely anchored whole actual source file,
then compiles the complete actual 19-source executable closure in an isolated
directory. It never uses extracted helpers, shadow types or a test-only
production helper.

| Actual-source compiled mutant | Required failed field |
| --- | --- |
| `credit-omitted` | `minute/Home/HomeSeconds` |
| `credit-opposite` | `minute/Home/HomeSeconds` |
| `credit-double` | `minute/Home/HomeSeconds` |
| `credit-erases-other` | `minute/Home/AwaySeconds` |
| `view-swapped` | `minute/Home/HomeSeconds` |
| `match-ledger-dropped` | `G15` |
| `second-delta-dropped` | `second-normal/Home/delta` |
| `minute-delta-dropped` | `minute/Home/delta` |

A kill requires a clean compile, normal assertion exit code 1, the exact new
JSON schema/field and the corresponding literal assertion position. Missing
inputs, compile/load/launch failures, crashes, timeouts, wrong fields/counts
and malformed receipts are **UNRUN/incomplete**, never semantic kills.

| Whole-file STATIC control | Required static field |
| --- | --- |
| `caller-credit-removed` | `caller.clock.credit-once` |
| `caller-owner-inverted` | `caller.clock.current-offense` |
| `caller-delta-replaced` | `caller.clock.exact-delta` |
| `caller-credit-duplicated` | `caller.clock.credit-once` |
| `caller-credit-moved` | `caller.clock.order` |
| `snapshot-ledger-replaced` | `caller.snapshot.current-ledger` |
| `model-ledger-nonzero` | `caller.model.zero-initialization` |

These seven controls inspect actual method tokens excluding comments/strings.
They check successful-entry containment, exactly one Step/credit, exact prior,
current offense and delta, credit/state/view/completion order, the source-only
snapshot argument and the immediate zero initializer. They are not compiled
`GameSimulation` mutations and provide zero gameplay-runtime evidence.

## Direct-CSC recipe — do not run without separate admission

Preparation root:
`.squad/.scratch/source-following-active-p1/human-possession-time-source-preparation`.
Parent handoff:
`.squad/.scratch/source-following-active-p1/metadata/human-possession-time-source/source-write-handoff.json`.
Both custody paths and all four new source/test/doc paths were absent before
authoring. Before/after/diffs cover all five production and three new test/doc
paths, with absence records for new paths. Four unique, contiguous **byte**
inverse records restore all four old production files to their entire original
pins. The contracts inverse encloses the three additive placements and preserves
the old constructor and all intervening bytes; separately recorded insertion
spans show that no bytes in between were edited. No ignored-region, tokenized,
prefix-only or rebaselined inverse is used.

The closure, tool, reference, mutation, future-CLI and archive manifests carry
exact sizes/SHA256 values. Reference manifests contain metadata only: no Unity
or BCL binary copies. FUSE mode 0777 is not a privacy boundary; all captured
material is public-port/synthetic source or public reference metadata.

Only after parent admission binds the exact handoff SHA256, these fixed inputs
and the still-absent output root may the predeclared command be used:

```sh
/usr/bin/python3.12 tests/contracts/possession_time.py \
  --execute-after-parent-admission \
  --admission-id PARENT_PUBLIC_ADMISSION_ID \
  --handoff-sha256 PARENT_ADMITTED_EXACT_HANDOFF_SHA256
```

The flag and public identifier are not authorization. Do not invoke/import this
recipe, run `--help`, syntax-check it, or create/populate
`tests/contracts/.artifacts/possession-time` during authoring.
The recipe refuses an existing output root and has no retries or alternate
dependency fallback.

If every prerequisite is available, the authored future CLI has exactly
**16 compiler commands and 12 managed launches**:

* 1 typecheck of actual ten Contracts plus `SourceClock`, `SourceMatchRules`,
  `SourceMatchFlow` and the new helper: **14 sources**, NS2.1.
* 1 compatibility-only typecheck of the existing closed 69 whole public
  sources plus the helper: **70 sources**, including every actual simulation
  partial. This is a fresh closure identity, not requalification of an old
  69-source receipt.
* 2 compatibility-only typechecks of the actual unchanged **3 UnityAPI**
  sources, under `UNITY_EDITOR` and `UNITY_WEBGL`, against that new model and
  exactly **3 genuine read-only Unity modules**.
* 2 initial and 2 final compilations/launches: separately selected new563 and
  retained1744 entrypoints using the actual **19 shared sources** (14 model,
  four unchanged runner/check files, one new entrypoint).
* 8 isolated actual whole-source mutant compilations/launches.

Every compiler command is direct fixed `dotnet exec csc.dll /noconfig @rsp`,
C#7.3, warnings-as-errors, deterministic, no standard-library autodiscovery,
using the declared **121 NS2.1** or **167 net10.0** references. Both managed
entrypoints request framework **10.0.0**, `rollForward: Disable`. No SDK
query/build/restore/install, package/network, helper process, engine, native,
asset, device, port, service or private oracle is part of this recipe.

Known tools are `/usr/bin/python3.12`,
`/home/long2know/.dotnet/dotnet`, and
`/home/long2know/.dotnet/sdk/10.0.100/Roslyn/bincore/csc.dll`;
their exact pins are in `tool-inputs.json`. BCL-only NS2.1 references are exactly
the declared paths under
`/dev/shm/ap.bweV08/t18/pkg/netstandard.library.ref/2.1.0/ref/netstandard2.1`;
no siblings/helpers are read. Net10 references are exactly the declared
`Microsoft.NETCore.App.Ref/10.0.0/ref/net10.0` set. Genuine Unity module paths
and pins are copied only as metadata from the authenticated prior
`reference-inputs.json`. A missing path leaves its actual partition
UNRUN/incomplete; there is no search, permission probe, binary copy or fake fallback.

Initial/final baselines use the **same** source, response-file, emitted-assembly,
runtimeconfig and log working paths. Both stages archive actual emitted
assemblies, configuration, stdout/stderr, responses, sources and reference/tool
identities. The future recipe requires those archived bytes/hashes to be
identical, rechecks custody/inverses/frozen pins, and never modifies actual source.
Only public managed runners run; the 70-source model and UnityAPI libraries are
never loaded as simulation/engine applications. `GameSimulation` construction,
`Awake`, `Poll`, events, engine input and native calls remain UNRUN.

## Frozen work and open claims

All five I1 files and all seven onside files retain their current accepted
whole-file pins. `GameSimulation.Source.cs` and `tests/contracts/run.py`, including
the whole-source/captain guard and existing current-call-to-legacy-call inverse,
remain exact. The new inverse applies only to four **other** production files.
No I1 writer, old 30-control suite, old static-control suite or private/native
qualification is consulted or rerun. The unchanged retained Program1744 is a
separate public-data-free rerun, not reexecution of old mutation suites.

Old counts/receipts are historical. This preparation claims no passing new
tests, compiled compatibility, fresh mutation kills, constructor/runtime
integration, native final-statistics lifecycle, records/UI, Unity/browser/render
or device/performance result. Actual-data assertions are zero. Full statistics
and the user's whole-100 fidelity objective remain open. Publication, commits,
pushes, PRs and CI are not authorized.
