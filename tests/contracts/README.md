# Actual-source contract regressions

From the repository root, with the selected .NET SDK:

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

The first three groups are **DTO boundary evidence**, for **511 total assertions**.
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

The same default command builds **21 isolated mutations** of the **actual source**.
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
zero checks, wrong inventory, or surviving mutant cannot pass.

The live production tree is never mutated. Baseline and final baseline must
both pass; a SHA-256 **and inventory** guard covers every compiled production
source before/after and during mutation execution. Isolated source/build trees
are deleted in `finally`. Logs, commands, exact counts, mutated-source hashes and
receipts remain locally ignored at `tests/contracts/.artifacts/result.json`.
Eight runner-guard tests require exact counts, all actual production dependencies,
and each unique real-source mutation anchor; they are not gameplay evidence.

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
