# First-playable regression evidence

These tests compile the **actual** `Assets/Scripts/Contracts/**/*.cs` and
`Assets/Scripts/Gameplay/**/*.cs`, including the actual pure
`Assets/Scripts/AssetImport/*.cs` dependencies used by the source runtime.
There is no replacement simulation, Unity
assembly, public debug command, reflection-based field editing, or historical
source/asset import. Assertions target the bounded slice in
`docs/architecture.md` and `docs/source-parity.md`, **not whole-game parity**.

## Core suite

Prerequisite: .NET SDK 10.0.100 (the installed SDK). The test assembly targets
`net10.0`, compiles linked production sources as C# 7.3, and declares xUnit/Test
SDK versions in `Blitz.Core.Tests/Blitz.Core.Tests.csproj`. This does not certify
Unity's .NET Standard 2.1 API profile. The independent
[`contracts/`](contracts/README.md) suite additionally compiles its actual
Contracts/pure-match sources as C# 7.3 against .NET Standard 2.1; that is not a
whole-kernel or Unity build certification.

From the repository root:

```sh
python3 tests/run-core.py
python3 tests/test-runner.py
python3 tests/mutate-core.py
```

`run-core.py` runs real `dotnet test`, checks the exact method/theory-case
inventory in `Blitz.Core.Tests/cases.json`, rejects missing/extra/skipped cases,
and records Contracts, Gameplay **and AssetImport** source SHA-256 hashes and
inventories before/after execution. Source changes
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

`mutate-core.py` copies the actual Contracts/Gameplay/AssetImport sources and test project into
a unique ignored `tests/.artifacts/mutations/` subtree. It changes the real
`FirstDownYards` declaration from 30 to 10 **only in that isolated copy**, builds
and runs actual rule assertions, and requires a behavioral failure (a
constant-only failure or compilation error is insufficient). It restores the
copied bytes in `finally`, verifies shared production hashes are unchanged,
writes evidence/TRX, and deletes only its own isolated copy. The expected
mutation test exit is nonzero; the proof command exits zero only if the mutation
was caught and the shared sources were unchanged.

## Actual source-match contracts

See [`contracts/README.md`](contracts/README.md): 177 promoted DTO assertions /
65 enum mappings, 23 supplemental guards, 122 source-backed synthetic pure
rules/flow assertions, and five isolated actual-source offending-field mutants.
This independent project excludes the actively integrated full simulation;
interface/DTO green is not behavior, original-C corpus, or full-game parity.

## Unity metadata

No editor or third-party Python package is needed:

```sh
python3 tests/test-unity-metadata.py
```

This runs stdlib guard tests, then checks the **actual** Assets tree, including
file/folder metadata and `Assets.meta`. Every asset needs metadata, every metadata
file needs an asset, nonempty metadata must end in a final **LF byte** (CRLF is
accepted), and each GUID must be a unique nonzero 32-hex value. The LF regression
preserves a valid GUID and removes only the terminator after an empty importer
field, matching the owner-confirmed native EOF rejection/retry. Scene
and prefab `m_Script` GUIDs must resolve to existing local `.cs` assets; malformed,
null and unresolved references fail. This is a local text-serialization guard,
not Unity importer/compilation or package-script resolution certification.

The same command also copies the actual `NormalCameraChecks.cs.meta` into a
small isolated metadata fixture under ignored `tests/.artifacts/`, appends the
regressing 33rd hex digit, and requires rejection for that precise reason.
The shared metadata is never edited; its bytes must remain unchanged, and only
the checker’s own isolated fixture is deleted.

Coordinate an owner-complete snapshot when importers/builds are writing Assets.
Inventory changes during checking fail rather than silently omitting new files.
`python3 tests/test-unity-metadata.py --unit-only` is available while awaiting
that handoff; it explicitly reports the live inventory/mutation **NOT RUN** and
does not constitute a passing live gate. CI should use the default command.

The earlier October 15, 2026 GUID-only owner-paused snapshot passed **11 guard tests**;
the actual inventory contained **34 assets, 34 metadata files, 34 unique GUIDs**
and one resolving scene script reference. The actual camera metadata's isolated
33-hex mutation was rejected for the expected rule, shared source bytes stayed
unchanged, and isolated fixtures were removed. These counts describe that
snapshot, not a hard-coded allowlist, final-LF check or Unity compile/import
certification.

The **LF-extended default command subsequently passed** on the coordinated
post-import/post-repeat snapshot: **12 guard tests**, **68 assets / 68 metadata
files / 68 unique GUIDs**, and **two resolving scene/prefab script references**.
The valid-GUID/no-final-LF regression rejects the native EOF failure; LF and
CRLF are accepted. The actual 33-hex camera mutation was killed again, shared
bytes stayed unchanged, and all fixtures were deleted. Counts remain dynamic
snapshot evidence; ignored local imported assets need not exist in a clean CI
checkout.

The later owner-released QBSTANCE/posed snapshot also passed the default command:
**12 guards**, **73 assets / 73 metadata files / 73 unique GUIDs**, three resolving
scene/prefab script references, and the actual 33-hex mutation killed again with
unchanged shared source. This metadata-only result does not authorize deployment
or certify the posed browser output.

## Browser

See [`browser/README.md`](browser/README.md). Passing headless core tests or
browser-tool unit tests is **not** evidence that Unity built, a Docker container
served it, or gameplay rendered in a browser.
