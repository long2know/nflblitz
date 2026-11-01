# Actual-source contract regressions

The human-onside increment is **source authoring**, not compiler/native/whole-game
acceptance. Its C# counts and compiled controls below are frozen expectations,
**UNEXECUTED** until separately admitted SDK execution. Python runner guards are
asset-free static/harness checks, not C# or gameplay passes.

From the repository root, after separate execution admission, with the selected .NET SDK:

```sh
python3 tests/contracts/test_runner.py
python3 tests/contracts/run.py
```

The package-free runner compiles **actual** `Contracts/**/*.cs` and the actual
pure `SourceClock.cs`, `SourceMatchRules.cs`, and `SourceMatchFlow.cs`.
`model/PureContracts.csproj` targets **.NET Standard 2.1, C# 7.3**, with warnings
as errors; the executable targets .NET 10. No Unity assembly, shadow contract,
copied gameplay implementation, or geometry stand-in is provided.

## Assertion inventory

* **177 promoted DTO assertions**, including **65 exact named enum mappings**
  against the public pure Gameplay enums (also pinned to their numeric values).
  These preserve the finalized Lead harness: detached/read-only roster,
  configuration, input, quarter-score and station collections; unspecified
  defaults; original play/page/slot/weighted-pool identity; explicit captain
  teammate offset, not team; ordered four-station simultaneous held/pressed/
  released A/B/C inputs; legacy constructor compatibility; typed result/status/
  PAT/period/kicker distinctions; finite/ID/range guards; selected-ordinal to WAN
  authoring-index conversion; source action tags.
* **23 supplemental DTO guards**: ordinal metadata/map/current/next/blend
  validation and inclusive endpoint, remaining unspecified station/period
  identities, and immediate controller identity even at zero possession ticks.
* **189 presentation/flip DTO assertions**: nullable offensive flip (including
  explicit false and flip-only `SourceAction.None`); detached four-station custom
  heads, designated home **side** versus source team/static-record indices;
  whole signed source flags, player mode and `pup_bigball`; unspecified versus
  observed zero; original script/function/path/line/instance/opcode identity;
  actual CSI ordinals/count/raw draw/site, including `randrng(1)`; noncarrier
  shaker assignments and actual carrier-at-opcode; all three signed callback
  arguments, including `-1`; detached immutable cue batches with strict source
  tick and within-tick instruction order. These are synthetic DTO inputs, not
  execution traces or proof of actual callbacks, RNG or process scheduling.
* **122 promoted source-backed synthetic rules/flow assertions**: original
  packed/local clock expectations; result points/spot/turnover/no-op semantics;
  PAT decisions; opening/quick/onside kicks; expiry/current-play/PAT deferral;
  halftime and three-overtime boundaries; CPU helper draw requests/order and
  weighted indices; defensive-TD `BeginPlay` initialization and subsequent PAT.

The first three groups are **DTO boundary evidence**; with the unchanged 122
rules/flow checks they form the **511-assertion retained prefix**. The 65 enum
mappings are already included in 177, **not an additional 65 assertions**.
The new human-onside partition adds **88 cases × 14 fields = 1232 tuple assertions**
and **one separate null-configuration API assertion**, for **1233 new / 1744 total**.
The 65 original named numeric mappings remain unchanged. In particular, separately
supplied immediate controller identity and credit eligibility at ticks 17/18 do
not prove actual catch ownership or `CONTROL_TIME` behavior. Compiled
`IPendingSourceInput` and `ISourceActorGeometry` signatures do **not** prove focus
clearing, imported wrist geometry, or any adapter implementation. The explicitly
synthetic clip supplies only metadata and throws if conversion samples/advances.

The 122 expectations preserve the pure match-rules contributor's harness tied to
`historicalsource/nflblitz2000@89f840d58d152a2a9f230cb8d0cdffbd554c8ded`
(`nfl.c` blob `15b49474fb55e8bac280b6515fcf7bf59cdf8664`,
`playsel.c` blob `f5e87b921a4038d03f55174bbd5425241939e798`,
`result.c` blob `eaf1201d0531a9d06d8ce0b1e9e75463062e2923`).
They assert recorded literal results, not a second C# implementation of those
expressions. They are **not** a new original-C differential corpus. Keep using
[`../source-oracles/README.md`](../source-oracles/README.md) and
[`../parity/README.md`](../parity/README.md) for the independently executed C
pipeline. No raw originals, original art/motion, generated C or oracle corpus is
published by this suite.

## Actual offending-field mutations

After admission, the same default command builds **30 isolated mutations** of the
**actual source**: the unchanged 21 plus nine human-onside Gameplay mutants.
It retains all five original mutants:
roster-array alias, quarter-score-array alias, selected ordinal leaking into
authoring index, controller identity incorrectly delayed until credit, and
removed NaN guard. Sixteen additional mutants collapse explicit false/flip-only
commands, alias station heads, force the designated home side, normalize the
big-ball int, mask raw actor/ball flags, substitute actor role for source player
mode, alias cues, accept duplicate instruction order, shift the original source
location, accept the CSI count as an ordinal, collapse weighted CSI ordinals,
drop the bound-one raw draw, filter noncarrier shaker assignment, or clamp a
callback sentinel. Each must build successfully and fail its **named field-level
assertion**. A compile error, exception/crash, missing summary, unrelated failure,
zero checks, wrong inventory, or surviving mutant cannot pass. All 21 retained
IDs/labels must fail within the original 511-check prefix; increasing the total
does not relax that limit.

The live production tree is never mutated. Baseline and final baseline must
both pass; a SHA-256 **and inventory** guard covers every compiled production
source before/after and during mutation execution. Isolated source/build trees
are deleted in `finally`. Logs, commands, exact counts, mutated-source hashes and
receipts remain locally ignored at `tests/contracts/.artifacts/result.json`.
Runner-guard tests require exact counts, all actual production dependencies,
each unique real-source mutation anchor, literal matrix dimensions and separate
static caller controls; they are not gameplay evidence. Harness identity covers
the C#/project files plus `run.py`, `test_runner.py` and this README.

## Human held-gesture kickoff boundary

`SourceMatchFlow.PrepareKickoff(SourceKickoffInput, SourceMatchConfiguration,
SourceInputFrame)` consumes the existing typed Contracts. A null configuration
throws `ArgumentNullException`; a null/empty frame or unavailable `DigitalCurrent`
contributes no raw request. Except for first/half kickoffs, it examines actual
configured stations on `input.State.OffenseSide`, matches the same current-frame
station ID, and requires **one station holding all A|B|C with DigitalCurrent
exactly Up**. Either teammate qualifies, regardless of captain or actor assignment.
Remapped station IDs are valid; unconfigured IDs contribute nothing at this pure
boundary (this does not relax ingress rejection).

Pressed/Released alone, diagonals, axes, cross-station button/direction unions
and previous-frame gestures do not qualify. The raw request is ORed with the
stored/programmatic request, forwarding every other input argument to the
**unchanged one-argument plan implementation**. First/half suppression, captain,
flags and formation selection remain that implementation's responsibility.
There are no new DTOs, gates, latches, draws, events, schedules or frame mutations.
The old one-argument API and no-frame stored-request path remain available.

The sole `GameSimulation.Source.cs` edit passes `sourceConfiguration` and
`Model.SourceCurrentInput.StationInputs` at the existing `PrepareSourceKickoff`
plan call. That is the current **transformed game_proc** frame, assigned before
Continue/PAT phase dispatch, not pending/previous/render/host input. Initialization,
Continue, period and PAT callers, CPU loop/draw order (including half kickoffs),
the single-use request reset and prior captain result transition are preserved.
Correctly selecting Onside instead of Quick intentionally selects different
existing formation/launch/RNG branches; equal whole-match traces across changed
input are not an expectation. Legacy60 does not enter this source path.

Authority: the byte-authenticated Lead contract `source-human-onside-held-gesture/v1`
at `historicalsource/nflblitz2000@89f840d58d152a2a9f230cb8d0cdffbd554c8ded`,
`nfl.c` (107299 bytes, SHA-256
`97013eed27a110e11f7180954ed7b7977ee7dc113c372d426392a686948ab434`,
blob `15b49474fb55e8bac280b6515fcf7bf59cdf8664`), physical LF lines
907–924 first/Opening, 934–955 human CURRENT ABC/exact JOY_UP/either teammate,
958–961 CPU, 963–965 first/half suppression, 970–1001 Onside/Quick plans.
This consumes an existing decoded digital channel, **not** a new 49-to-8 decoder,
analog approximation, producer qualification or external SDK/numeric-JOY_UP proof.
No original body, asset or private proof is copied into this suite.

### Frozen tuples and receipts

The 64 core cases iterate Two/Four layouts, Home/Away kickers, First/Second
captains, Home/Away first-possession sides, then `held-up`, `pressed-only-up`,
`diagonal-up-left`, `receiver-only-up`. Canonical station fixtures are Two:
0 Home/1 Away; Four: 0/1 Home, 2/3 Away. All core inputs use q3/time `0x2000`
and first/half/stored false. Every expected 14-field tuple is a **literal**, not
a golden generated by the implementation or another rules helper.

The 24 ordered edges cover: 01 noncaptain teammate; 02 split AB/C; 03 separate
chord/Up teammates; 04/05/06 missing C/B/A; 07 Up|Right; 08 Down; 09 neutral
digital plus a Released-only teammate; 10 null digital with upward axes;
11 empty and 12 null frames; 13/14 stored true with null/neutral raw input;
15 unconfigured ID; 16/17 first kickoff with stored false/true; 18/19 half
kickoff with raw chord/stored request; 20/21 CPU decision false/true with only
receiving humans; 22 remapped 3 Home/0 Away; 23 current neutral after prior chord;
24 current chord after prior neutral with Down3/NoFirstDown true.
The 16/17 inputs use q0/full clock; 18/19 use q2/full clock, preserving
OpeningKick alongside QuickKick. Other defaults are Four/Home/First/first-pos
Home/q3/time `0x2000`.

Each case checks, in order: Kind, Flags, KickingSide, ReceivingSide, Captain,
AutoHikeTicks, LastPossession, State.OffenseSide, State.InitialOffenseSide,
State.Down, State.LineOfScrimmage, State.FirstDown, State.NoFirstDown,
FirstKickoffAfterPreparation. Onside retains the kicker/LOS30; Quick uses receiver
offense/initial/last and LOS40; first-down100, hike3/90, captain truth table and
Down/NoFirstDown are explicit literals. The API guard is separately labeled
`human-onside/api/null-configuration`, **not** a 89th tuple or a replacement field.

The unchanged CLI schema adds `human_onside_contract`, `retained_assertions`,
`human_onside_cases`, `human_onside_fields_per_case`,
`human_onside_tuple_assertions`, `human_onside_api_guard_assertions` and
`human_onside_assertions`. On a tuple failure, partial counts include the failing
field, completed cases exclude that case, and API guard count remains zero.

Nine new controls mutate captured **actual SourceMatchFlow.cs**, resolved to
Gameplay rather than the old Contracts-only filename assumption. The frozen first
failure is each listed case's `/Kind` label with `: expected X, actual Y` appended:

| Compiled control | First case | Expected → mutant | Total / tuple checks / completed cases |
|---|---|---|---|
| `onside-held-replaced-by-pressed` | core/Two/Home/First/first-pos-Home/held-up | Onside → Quick | 512 / 1 / 0 |
| `onside-all-buttons-replaced-by-any` | edge/02-four-split-buttons-AB-and-C | Quick → Onside | 1422 / 911 / 65 |
| `onside-up-equality-replaced-by-inclusion` | core/Two/Home/First/first-pos-Home/diagonal-up-left | Quick → Onside | 540 / 29 / 2 |
| `onside-kicking-team-filter-removed` | core/Two/Home/First/first-pos-Home/receiver-only-up | Quick → Onside | 554 / 43 / 3 |
| `onside-team-replaced-by-station-id` | core/Four/Away/First/first-pos-Home/held-up | Onside → Quick | 1184 / 673 / 48 |
| `onside-second-teammate-ignored` | edge/01-four-second-teammate-noncaptain-held-up | Onside → Quick | 1408 / 897 / 64 |
| `onside-stored-request-discarded` | edge/13-legacy-stored-true-null-frame | Onside → Quick | 1576 / 1065 / 76 |
| `onside-half-argument-discarded` | edge/19-half-kickoff-raw-neutral-stored-true | Quick → Onside | 1660 / 1149 / 82 |
| `onside-first-argument-discarded` | edge/16-first-kickoff-raw-chord-stored-false | Opening → Quick | 1618 / 1107 / 79 |

All labels begin `human-onside/`. Unique source anchors and exact partial
partitions are frozen in `run.py` before execution, not inferred from a pass.
Compile/start failures, timeouts, unrelated assertions and inventory failures
are never behavioral kills.

### Separate static actual-caller proof

Token/brace-aware checks ignore comment/string decoys and examine actual methods,
the three-argument plan call, all original constructor arguments, configuration,
current transformed frame, assignment order and converging callers. They hash the
legacy one-argument body and reverse **only** the authorized new call to require
the exact pre-edit Source.cs hash
`726f513c0549d8bf028734c7d7c449dfbff430f8f7c244053b4cf8fdc03b8091`.
This also preserves the CPU loop/half draw behavior, reset, all other methods and
the existing captain patch. Caller identity is recorded separately from the
compiled pure-source inventory.

Five controls mutate captured actual caller bytes, **without compiling or
executing GameSimulation**, and must fail these exact static fields:

| Static-only control | Required rejection field |
|---|---|
| `caller-overload-omitted` | `caller.plan.three-argument` |
| `caller-frame-null` | `caller.plan.current-frame-not-null` |
| `caller-frame-stale` | `caller.plan.transformed-current-frame` |
| `caller-current-assignment-late` | `caller.current-input.before-phase-dispatch` |
| `caller-CPU-half-shortcut` | `caller.cpu.guard-without-half-shortcut` |

These five are **not** part of the 30 compiled controls. Python guards only verify
source shapes, captured-byte static rejections and synthetic receipt rejection.
They do not build C#, invoke `run.py`'s main, launch children or access original
assets/proofs. Their fixture writes are project-local; `-B` and explicit
project-local TMPDIR/TMP/TEMP are required for the bounded authoring admission.
Actual C# identities, binary hashes/MVID and compiled case results remain unknown
until future admitted execution. Neither static proof nor pure typed fixtures
prove a natural Simulation kickoff, kick/recovery/clock, host/controller/Unity,
native/Goose/DSP, target PRNG, performance, art/rights or whole-game fidelity.

## Additive presentation boundary

The existing constructor prefixes and five previous Contracts remain compatible.
`SourceCommand` and `SourceMatchView` append nullable `OffenseFlipped`. Null means
not supplied/published; false is an actual unflipped choice. A constructed
`new SourceCommand(SourceAction.None, offenseFlipped: false)` remains specified.
The DTO does not implement the source menu switch, lineup reversal or snap logic.

`ISourcePresentationState : ISourceMatchState` optionally publishes immutable
`SourcePresentationSettings`. Its `DesignatedHomeSide` is original `home_team`
(side0/1, **not** catalog team index); `StationHeads` copies all four original
`team_head` entries (`-1` known no head, `0..19` original head ordinals).
`PupBigBall` retains the entire original signed int, without boolean normalization.
Absent settings are null, not a fabricated home side or no-head table.
`PlayerView.Source.Presentation` carries `SourceActorPresentationView` with the
whole signed `obj_3d.flags` and raw `plyrmode` (`0..10`, `MODE_QB=5`, not
`ActorRole`). `BallView.Presentation` carries the whole signed ball flags.
Their default structs remain unspecified; explicitly published zero is known.
Relevant original bits are `PF_FLIP=0x1`, `PF_HICOUNT=0x4`,
`PF_POP_HELMET=0x00800000`, and `BF_PHANTOM=0x800`; unrelated bits are retained.
Controller station and roster identity remain in their existing fields.

`ISourcePresentationSimulation : ISourceMatchSimulation` optionally publishes
a detached `SourcePresentationCueBatch` through `PresentationCues`. Its copied
`Cues` retain all executed source ticks from the last host step. An empty batch
is known empty, whereas null is unavailable. Never mutate a retained batch,
sort callbacks into a new order, or replay the last batch at render rate.

The small typed camera/shaker/callback carriers share `SourcePresentationContext`:
actual source tick, total within-tick instruction/callback `ProcessOrder`, actor,
source path, executing script/function and original line; optional actual script
instance and original opcode token. The order is the producer's actual execution
ordinal accepted by `SourceCinematicPlayback`, **not** an actor ID or invented
camera-stage position. Default context has no actual tick/actor/location.
Cue-kind tags are boundary identifiers, not numeric original opcodes.
`SourceCameraSelection` preserves actual table, selected ordinal, entry count
and optionally paired actual raw draw/site text; it validates the ordinal and
`raw % count`, but does not draw RNG or manufacture provenance. Gameplay's
`SourceRandomDraw` cannot be a Contracts dependency: the producer projects
its `Raw` and `Site.ToString()` verbatim. A single-entry table still needs the
actual source draw; DTO acceptance alone does not prove it was consumed.
Camera/callback carrier identity is nullable (unknown), with `-1` an observed
absence; shaker requires the actual carrier because original amplitude/total
assignment precedes its carrier guard. Callback arguments retain all three
signed source words, with no rejection or clamping of legitimate `-1` values.

Source anchors at the pinned archive revision above: `GAME.H:498,542`,
`ani3d.h:282`, `player.h:66,72,116,206-216,271,344`, `Pup.h:40`,
`teamdata.c:1090-1134`, `nfl.c:2537-2557`, `player.c:6831-6925,10574-10620`,
and the active verified `playsel.c:1940-1987` exact newly-pressed A/B/C switch.
The reached `fall_04` sites are camera15969, `player_down`15995,
`grnd_hit_snd(-1,0,-1)`15998, shaker(2,10)16000; contact context includes
`detect.c:2494` in `evade_vs_tackle`. Bodies/assets are not copied here.

Core owns publishing actual fields/cues and applying flip; Runtime/Presentation
own consuming them with the original catalog. This change adds no camera-stage
or heading callback, camera tick, audio/effect execution, source RNG producer,
replay/slow-motion API, or Unity dependency. Those callers still require their
owners' source-stage integration and evidence. P1's pure project/snapshot includes
all six Contracts; no P1 logic or oracle expectations are changed.

To run just the conventional assertions while authoring (without mutation/hash
guard evidence):

```sh
dotnet run --project tests/contracts/ContractChecks.csproj --configuration Release
```

These checks do not complete full match/AI/native/browser/art/motion parity.
