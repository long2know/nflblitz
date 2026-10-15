# First-playable regression evidence

These tests compile the **actual** `Assets/Scripts/Contracts/**/*.cs` and
`Assets/Scripts/Gameplay/**/*.cs`. There is no replacement simulation, Unity
assembly, public debug command, reflection-based field editing, or historical
source/asset import. Assertions target the bounded slice in
`docs/architecture.md` and `docs/source-parity.md`, **not whole-game parity**.

## Core suite

Prerequisite: .NET SDK 10.0.100 (the installed SDK). The test assembly targets
`net10.0`, compiles linked production sources as C# 7.3, and declares xUnit/Test
SDK versions in `Blitz.Core.Tests/Blitz.Core.Tests.csproj`. This does not certify
Unity's .NET Standard 2.1 API profile.

From the repository root:

```sh
python3 tests/run-core.py
python3 tests/test-runner.py
python3 tests/mutate-core.py
```

`run-core.py` runs real `dotnet test`, checks the exact method/theory-case
inventory in `Blitz.Core.Tests/cases.json`, rejects missing/extra/skipped cases,
and records all source SHA-256 hashes before/after execution. Source changes
during a run invalidate the result. A missing kernel, build failure, zero-test
run or missing TRX is a failure, never an apparent pass.

The conventional runner also works:

```sh
dotnet test tests/Blitz.Core.Tests/Blitz.Core.Tests.csproj --configuration Release
```

That command reports actual xUnit counts but does not enforce the additional
inventory/hash guard. For a focused author follow-up, use `--filter` with the
specific fully-qualified class/method, then rerun `run-core.py` for full evidence.
`--no-restore` is supported by the wrapper after dependencies are restored.

Results, full runner logs and TRX files are ignored under
`tests/.artifacts/core/<UTC timestamp>/`. Nothing is uploaded.

Verified October 15, 2026: **156 actual core cases passed, zero skipped**;
six result-inventory guard tests passed. The ten-yard real-source mutation
produced three expected failing assertions (including both directional rule
targets), while shared source hashes stayed unchanged. These are headless core
results, not a Unity editor, WebGL, container, browser-performance or full
historical-parity certification.

### Coverage

Every row below is implemented as executable assertions. The full runner must
actually pass before any row is reported tested on a particular source revision.
**No row has yet been browser-observed.**

| Contract | Representative tests |
| --- | --- |
| Fourteen stable IDs/teams/slots; initial own-20, 30-yard target | `InitialSnapshotHasCompleteStableActorsAndScrimmage`; `Check.ValidState` on every tested step |
| All five phases; role-invalid, unknown-enum and wrong-phase commands | `ContractTests`; `IrrelevantLiveOffenseActions...`; `WrongRoleAndUnknownActionsOnHomeDefense...`; final freeze tests |
| Entry-phase dispatch; Home 300/Away 60 subsequent auto-snap ticks | `HomeAutoSnapUsesThreeHundredSubsequentTicksNotSelectionTick`; `ManualSnapDoesNotAlsoMoveOrSpendLiveClock`; `HomeKeepsSevenStableDefenders...` |
| Unit/sub-unit/huge diagonal input; independently sanitized NaN/infinity | `DirectionIsNormalized...`; `SubUnitMovement...`; `NonfiniteMovementComponentsBecomeZeroIndependently` |
| Finite bounded turbo, actual drain/recharge and repeated play loop | `TurboDrainsRechargesAndStaysFiniteAndBoundedAcrossACompleteDrive` |
| Eligible receivers only; QB at/behind LOS; one forward pass | `OnlyOwnEligibleReceiver...`; `IneligibleAndOutOfRangePassTargetsAreIgnored`; `CrossingSnapLine...`; `PassAtSnapLos...` |
| Real flight/arc/catch/control transfer and forced geometric miss | `AutomaticCatchTransfersBallAndControl...`; `ActualIncompleteKeepsSnapLos...` |
| Playable Home defense, nearest-other/tie-break switch, bounded explicit tackle | `SwitchDefenderSelectsNearestOtherHomePlayer...`; `EqualDistanceDefenderSwitch...`; `ControlledHomeDefenderRequiresTackleActionWithinBoundedReach` |
| Thirty, not ten; exactly achieved/below target; capped goal-to-go | `RuleTests`; `ActualTerminalStepAtThirtyYardsPublishesCauseThenFirstDown` |
| Failed fourth and fourth-down incomplete in both directions, no mirrored spot | `ActualFailedFourthDown...`; `ActualIncompleteKeepsSnapLosAndFourthDownTransfersInBothDirections` |
| Midfield-directed whole-yard rounding and `1..99` clamp | `NonScoringSpotRoundsTowardMidfieldAndClampsToOneThroughNinetyNine` |
| TD six/safety two, correct team, receiving own-20 orientation | `InBoundsGoalCrossingScoresSixOnceForTheCorrectTeam`; `OwnEndZoneOutOfBounds...`; `RealContactInOwnEndZone...` |
| Both sideline corners/back lines; goal before tackle; no duplicate score | `SidelineAndBackBoundaryWinAmbiguousCorners...`; score freeze/event-order assertions |
| Clock zero finishes current run/pass, emits quarter once; four periods/tie/final/restart | `ScenarioPassAndClockTests`; `FourQuartersEndExactlyOnceAndFinalStateOnlyAdvancesTickUntilRestart` |
| Detached retained state/player collections **and** retained ordered event lists | `RetainedSnapshotsPlayersAndEventsDoNotChangeAfterStepOrRestart`; `EventsAreLastStepOnly...` |
| Same seed and command stream, every state/player/ball/event field **every tick** | Three full-match seeds in `SameSeedCommandStreamMatchesEveryStateAndOrderedEventFieldEachTick` |

Scenario fixtures use Unity's documented **internal** `GameSimulation.Model`
and `GameplayRules` seam. The production sources are linked into this test
assembly, so no public mutation API or reflection is needed. Scenarios enter
selection/snap through public commands; internal setup arranges the exact spot,
clock, receiver separation or defender reach. Public snapshots remain detached:
fixtures must not choose a new play from a snapshot published before setup.
Black-box interaction/full-match tests exercise the ordinary public loop.

### Real-source mutation

`mutate-core.py` copies the actual contract/gameplay sources and test project into
a unique ignored `tests/.artifacts/mutations/` subtree. It changes the real
`FirstDownYards` declaration from 30 to 10 **only in that isolated copy**, builds
and runs actual rule assertions, and requires a behavioral failure (a
constant-only failure or compilation error is insufficient). It restores the
copied bytes in `finally`, verifies shared production hashes are unchanged,
writes evidence/TRX, and deletes only its own isolated copy. The expected
mutation test exit is nonzero; the proof command exits zero only if the mutation
was caught and the shared sources were unchanged.

## Browser

See [`browser/README.md`](browser/README.md). Passing headless core tests or
browser-tool unit tests is **not** evidence that Unity built, a Docker container
served it, or gameplay rendered in a browser.
