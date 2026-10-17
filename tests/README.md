# Regression evidence: legacy and source components

The core suites compile the **actual** `Assets/Scripts/Contracts/**/*.cs` and
`Assets/Scripts/Gameplay/**/*.cs`, including the actual pure
`Assets/Scripts/AssetImport/*.cs` dependencies used by the source runtime.
There is no replacement simulation, Unity
assembly, public debug command, reflection-based field editing, or historical
source/asset import. Assertions target the bounded slice in
`docs/architecture.md` and `docs/source-parity.md`, **not whole-game parity**.

The checked-in Contact/C2 source is distinct from the recorded live56 browser
player and all historical 56/117/156/157/272 test-entry identities.
Default/empty selection remains `original-teams-contact272`; the281/26,230-frame
`original-teams-contact281-c2` is **explicit opt-in**. Use the
[current resource/preparation recipe](../docs/build.md#current-contactc2-source-mode-resources),
not an old library relabeled as new. Teams/rosters, stations/possession/captain,
designated home side, heads, power-ups, movement data/settings and original DIP
raster remain explicit pinned inputs.
`?actors=graybox` preserves legacy controls; source uses
[raw cabinet bindings](../README.md#source-cabinet-controls) and rejects missing
resources or unbound original audio/FX instead of substituting them.
All sixteen [full-parity rows](../docs/source-parity.md#full-source-completion-matrix--executable-acceptance)
remain **OPEN**.

Original-data campaign counts below are recorded owner evidence, **not new
campaign executions for this CI/documentation update**. The asset-free Contact/C2
commands below were run locally on October 17, 2026. Pure tests, actual
Unity-reference API compilation,
native import/pixels, a Unity-built player and browser-observed gameplay are
different evidence layers.

## Current Contact/C2 gates

From the repository root, Python 3.10+ and the existing .NET 10.0.100 SDK suffice
for these **asset-free** checks; no Unity, original data or third-party Python
package is required:

```sh
python3 -B -m unittest discover -s tests/audio/runtime -p 'test_ci.py' -q
python3 -B tests/parity/test_contact_inputs.py
python3 -B tests/contact/test_runner.py
python3 -B tests/movement-callers/test_canonical.py
python3 -B -m unittest discover -s tools/animation/tests
python3 -B tools/animation/prepare_contact_impact.py --help > /dev/null
python3 -B tools/animation/prepare_c2_impact.py --help > /dev/null
python3 -B -m unittest discover -s tests/presentation/contact-runtime -p 'test_callers.py' -q
python3 -B tests/presentation/contact-runtime/run.py
python3 -B tests/presentation/contact-runtime/mutations.py
python3 -B -m unittest discover -s tests/presentation/c2-runtime -p 'test_callers.py' -q
python3 -B tests/presentation/c2-runtime/run.py
python3 -B tests/presentation/c2-runtime/mutations.py
```

The existing single `game-build.yml` job runs these gates alongside the retained
Core, ball/M1/VM/P1 runner guards and all prior checks. Its `Assets/**`,
`tests/**` and `tools/animation/**` triggers already include every new source,
reader, factory, producer and test; README/build/parity/architecture changes
also trigger it. The workflow guard inspects actual event paths, named steps
and executed command tokens, and rejects in-memory removals, echo/comment-only
commands, missing mutations and accidental original-data flags.

The new preflights compile actual pure production code separately as
C#7.3/SDK netstandard2.1 and execute on .NET10. Locally they passed 42 C1 and 27
C2 assertions, six/four compiled causal controls, and each suite's twelve
caller guards/seven in-memory seam removals. Animation discovery passed 62
tests, including both new profile producers. These defaults **do not** load
original281 motion or qualify the actual-data factory. Local preflight runs
used fresh `--output` children to preserve earlier receipts. The existing original
CI campaigns deliberately retain the legacy69-pin/443-file preparation and
historical entry semantics; they are not promoted to current281 acceptance.

### Explicit original-data preparation and package checks

Supply authorized original files, not a checkout-relative directory assumed
to exist. The existing `tests/parity/fetch_inputs.py` default remains 69 pins.
Its explicit `--profile contact272` verifies 78 case-exact pins (all old69 plus
three operand files/six WANs), using explicit read-only caches or separately
authorized `--download`; there is no silent retrieval or synthetic fallback.
The [legacy preparer](parity/README.md#required-actual-full-kernel-factory-inputs)
produces the unchanged443-file fixture. Then, with actual272 and C1 impact from
the [build recipe](../docs/build.md#offline-preparation-with-explicitly-supplied-inputs)
and an explicitly supplied pinned115 supplemental bundle:

```sh
python3 -B tests/parity/prepare_contact_inputs.py --profile contact272 \
  --legacy-fixture "$LEGACY443_FIXTURE" \
  --production-bundle "$BUNDLE272" --supplemental-bundle "$SUPPLEMENTAL115" \
  --impact "$CONTACT_ROOT/impact272/impact-presentation.json" \
  --output .squad/.scratch/SourceData/contact-local272
FIXTURE272=".squad/.scratch/SourceData/contact-local272/fixture.json"

# All six roots/files must be explicit, real and local to this worktree.
C2_ARGS=(
  --inputs "$C2_INPUTS" --old272 "$BUNDLE272"
  --fixture .squad/.scratch/SourceData/contact-local272/legacy-fixture.json
  --impact "$CONTACT_ROOT/impact281/impact-presentation.json"
  --old-impact "$CONTACT_ROOT/impact272/impact-presentation.json"
  --hud "$HUD_ROOT"
)
python3 -B tests/presentation/c2-runtime/run.py "${C2_ARGS[@]}" \
  --output tests/presentation/c2-runtime/.artifacts/local-original
python3 -B tests/presentation/c2-runtime/mutations.py "${C2_ARGS[@]}" \
  --output tests/presentation/c2-runtime/.artifacts/local-original-mutants
```

Use new ignored output children to preserve earlier receipts. Fixture `--reuse`
is read-only verification, not permission to overwrite inputs. The834-file
Contact272 fixture preserves the actual model/15 pivots, rosters, P1, movement,
legacy animation and old SAFN. C2 adds the **separate 288-file** canonical producer
closure via `--c2-inputs`; there is no invented281 fixture schema or promoted
default. Actual package mode requires **all six** flags together, verifies real
data and canonical identities, and never substitutes the asset-free checks.
The [Contact272 suite](presentation/contact-runtime/README.md) documents its
distinct six-flag actual-data mode; do not reuse C2 flag names there.

### Original-source campaigns and reference-only APIs

The original-C runner needs GCC/Clang and exact pinned originals. For the
current numeric/host adapter, first generate its real corpus/receipt:

```sh
python3 -B tests/source-oracles/run.py --source-root "$ORIGINAL_SOURCE" --full-matrix
python3 -B tests/parity/run.py --cases tests/source-oracles/.artifacts/cases.json \
  --full-kernel --input-profile contact272 --fixture-manifest "$FIXTURE272" \
  --c2-inputs "$C2_INPUTS" --output tests/parity/.artifacts/local-current281
python3 -B tests/movement-callers/canonical.py \
  --fixture-manifest "$FIXTURE272" --sound-functions "$SAFN" \
  --c2-inputs "$C2_INPUTS" --source-root "$ORIGINAL_SOURCE" \
  --output tests/movement-callers/.artifacts/local-current281
```

`SAFN` is the unchanged4a557304 payload in the fixture. The
[contact runner](contact/README.md) additionally needs explicit operand,
camera, exact original13 movement/original14 route roots and the separately
pinned Midway process originals for C2. Its tracked `prepare_c2.py` delegates
the canonical producer and tracked original-C/defined-negative-target oracles;
ignored Core scratch producers are not dependencies.
[Ball](ball/README.md) and [SoundVM](audio/vm/README.md) add
`--c2-inputs "$C2_INPUTS"` and a fresh `--output` to their explicit
`--input-profile contact272 --fixture-manifest "$FIXTURE272"` commands.
Historical null-bootstrap/old-first-wake paths are separate controls, not
unchanged C2 trajectories. Original-data campaigns were **not rerun** for this
CI/docs integration.

For actual Unity6000.3.24f1 references, independently of original data:

```sh
python3 -B tests/presentation/c2-runtime/api_compile.py \
  --unity-editor-data "$UNITY_EDITOR_DATA"
```

This existing optional command compiles all current 99 runtime/13 separate
Editor sources in five C#7.3/actual netstandard2.1 API shapes with
warnings-as-errors and before/after source hashes. CI does not presume those
references exist. No Editor/activation/import/native/WebGL execution follows
from compilation. The recorded all112/zero-diagnostic result and 963 actual
package assertions/14 controls are prior owner evidence, not newly executed
here. Full M1, all16 rows and DSP/browser acceptance remain open; see the
[current limits and autonomous portability disclosure](../docs/source-parity.md#current-contactc2-boundary).

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
65 enum mappings, 23 supplemental guards, 189 presentation/flip assertions,
122 source-backed synthetic pure rules/flow assertions, and 21 isolated
actual-source offending-field mutants. Presentation settings, raw draw flags
and ordered opcode/callback cues are detached contracts, not implementations
of the simulation producers or camera stage.
This independent project excludes the actively integrated full simulation;
interface/DTO green is not behavior, original-C corpus, or full-game parity.

## Actual ball and input/cue callers

See [`ball/README.md`](ball/README.md) for runtime-extracted pinned original-C
ball/receiver/predicate cases, real original-pose factory inputs, actual
input/camera/VM cue callers, strict geometry/control-time boundaries, the
source-backed PAT/kickoff `Down=-1` guard regression, and isolated production
mutations. See [`parity/README.md`](parity/README.md) for the full-kernel
factory command: it requires an explicit real-input manifest and retains all
existing host/source adapter assertions. Neither suite is all-row/full-match,
original-platform RNG/scheduler, native-build or browser qualification.

The frozen B1 campaign records **488,736 assertions / twenty rejected actual
production mutants**, retaining 156 legacy cases. It uses the actual canonical117
library, real fifteen-pivot catch geometry, P1 playbook and team rosters, not a
single-player56 fixture or invented joint positions.

## Original HUD/cabinet, stage caller and team/impact presentation

### Historical movement157 runtime gate

These recorded bootstrap results precede Contact/C2. Its original native
resource composition requires the matching historical source revision;
preserved157 keys are not a current-engine fallback.

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -B tests/interface/movement_bootstrap.py
# Optional complete real engine APIs and actual movement-data composition:
PYTHONDONTWRITEBYTECODE=1 python3 -B tests/interface/movement_bootstrap.py \
  --unity-editor-data "$UNITY_EDITOR_DATA" \
  --movement tools/movement/.artifacts/source-movement-runtime.json
```

The default is current-checkout/asset-free: **33,921 pure assertions / ten
compiled bootstrap source mutants**, plus33 separate structural Runtime/service
mutants and complete-owned-file/clean-checkout guards. It validates all selected
fields, team masks, global power booleans and both original DIP modes without
inventing defaults.

The actual-data option executes the actual production Runtime assembly's pure
composition helper: **10,941 assertions / four compiled Runtime mutants** for
every real89c790 friction/Line/Back/acceleration/plow value and original settings.
Both actual Unity6000.3.24 C#7.3/.NET Standard2.1 API profiles compile **85 runtime
sources including all16 owned UI**, zero diagnostics. No fake Unity types,
native engine objects or copied test factory are used.

Actual Runtime calls these helpers and injects mandatory M1 data/settings.
Its existing pre-player callback samples the real source camera, requires the
same executing tick's pure frame, then injects position/all12 matrix floats/
original DIP into Core before returning the heading. Raw game mode, restart,
focus, one cue owner and one host lifecycle are guarded. This is not a new
native/browser/full-M1 qualification; QA/Team source campaigns remain separate.
The selected, uncompressed `R_SKIP` is now supplied through exact named original
converter-state replay, not the old mistaken unselected-asset stop.

### Retained B1/Team/HUD evidence

These independent asset-free commands compile real production sources without
launching an Editor or requiring original art:

```sh
python3 -B tests/interface/run.py
python3 -B tests/interface/stage_followup.py
python3 -B tests/presentation/run.py
python3 -B tests/presentation/interface-packaging/run.py \
  --b1-root . --ui-root . --team-root .
```

The three packaging roots explicitly name the same coherent, stable checkout;
there is no implicit Primary/hot-M1 or ignored-snapshot fallback.
See [HUD/cabinet checks](interface/README.md),
[complete-owned-UI stage/API checks](interface/STAGE.md),
[team/impact checks](presentation/README.md), and the
[packaging inputs/API commands](../tools/interface/packaging.md#sourcepureapi-checks-without-launching-an-editor)
for explicit original-data and real Unity-reference modes. Original-data tests
require the pinned local inputs; they do not silently replace them with mocks.

| Recorded layer | Passing evidence | Limit |
| --- | --- | --- |
| Team/impact | **680,731 assertions / 14 production mutants**; all 31 teams, 20 heads, 86 texture identities | Source/data/host-functional camera proof, not native uniforms, impact-game pixels or target MIPS/scheduler parity. |
| Base HUD/cabinet | **16,215 assertions / 13 production mutants**, nine converter/four interop tests; 381 images/six fonts/104 planes | Original glyph/layout/state/raw49/focus/CSS-DPR model checks, not a newly rendered browser HUD or generated Composit/Texus pixel identity. |
| Complete-owned-UI stage caller | **80 runtime sources including all 16 owned UI**, zero diagnostics for actual Unity 6000.3.24 C# 7.3/.NET Standard 2.1 `UNITY_EDITOR` and `UNITY_WEBGL`; 21 caller/scope/inventory guards | Includes `BlitzRuntime`, `PlayHud` and `FixedStepBridge`; runtime-with-defines is not separate Editor-assembly proof. |
| B1 packaging | **107 actual-data assertions / 18 production mutants**; later integrated **82 runtime / separate 5 Editor** API snapshot, zero diagnostics | No asset import/save/reload, GPU atlas pixels, fresh game build or browser proof. |

The earlier three-adapter `UnityAPICompile.csproj` did not include the existing
Runtime/HUD files; its historical green result is not the complete-owned-UI proof.
The later stage inventory closes that omission. Separate-assembly packaging
validation additionally includes the parent **visibility-only public
`DecodeUtf8` repair** and **four explicit serialized-null initializers**.
The old internal-access and CS0649 reports are resolved for the target 6000.3.24
API; the frozen recipe notes retain their pre-repair history. There are no fake
Unity APIs, warning suppressions or removed methods.

Those API checks do not start Unity. Two archived 6000.3.24 and one
installed 6000.6.5 native attempts exited 198 before import because of
licensing404/missing headless entitlement. Installed 6000.6.5 also has a separate
frozen-Team `GetInstanceID` incompatibility; the target remains 6000.3.24f1.
No new native/game/browser/performance evidence is inferred. Old live56 evidence
is retained in [build history](../docs/build.md#continuous-animation-evidence--october-16-2026),
not reused as new Team/HUD captures.

The stage caller registers the real pre-actor camera producer before the first
wake and forwards each ordered cue once, including catch-up steps. This bounded
semantic B1 lifecycle does not close all raw normal/kickoff/gopher camera modes.
Original actual audio/FX consumers remain unbound and fail explicitly; no silence,
fake-click or noop consumer is evidence. M1/156 animation/camera/settings
injection was **not** part of that frozen B1 resource composition. Current
movement157 source wiring is recorded separately above.

## Original movement and evasion helpers

See [`movement/README.md`](movement/README.md). Run the explicit SDK profile
before its generated-receipt guards:

```sh
python3 tests/movement/run.py --fetch-inputs --sdk-netstandard21
python3 -B -m unittest discover -s tests/movement -p 'test_publication.py' -q
```

The frozen pure helpers pass 14,047 original-C cases, 813,702 production
assertions, sixteen implementation mutants and fifteen publication guards.
The SDK .NET Standard 2.1 API check is explicitly **not Unity validation**;
the separate actual Unity-reference API mode is retained. Immediate animation
boundaries are test fixtures, not a production VM or completed Core integration.
The [runtime-safe exporter](../tools/movement/README.md) preserves exact static
data without the test-only `Responses` fixture; its output stays ignored.

## Original audio packages

### Retained synchronous sound Runtime

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -B tests/audio/runtime/run.py
```

See [Runtime transport/actual Core tests](audio/runtime/README.md) for explicit
original157 fixture/SAFN and real Unity6.3 API options. The asset-free default
compiles production Core/bridge/transport, tests exact FIFO/cue/order/zero versus
Unsupported/missing-consumer/partial-fault/restart semantics, and rejects
sixteen compiled actual-source mutants. Optional real Core cases execute the
unchanged VM with named test state; they are not a public full-match claim.
No post-host selector, fake audio player or real DSP consumer is installed.

All8 PM/DM images and CRC deployment wrapping exist and are verified. The
remaining playback boundary is exact historical target/build identity, matched
legitimate Seattle/Vegas bootstrap bytes and full DCS2 board qualification—not
missing PM/DM images or sample-decoder success. Nonempty output still fails
explicitly without a real consumer; all sixteen parity rows remain OPEN.

### Retained offline decoder/reader

The separate [`audio`](../tools/audio/README.md) preparation/reader layer has an
asset-free regression command:

```sh
python3 tests/audio/run.py --mutate
```

This executes the actual immutable reader and synthetic parser/package failure
gates without original banks, vendor code or Unity. Actual Unity-reference API
compilation requires explicit `--unity-editor-data`. Decoded original samples
and an isolated one-shot adapter do not implement source sound-selection RNG,
DSP3 mixing or game-audio playback.

The separate [original actor sound selectors](../tools/audio/FUNCTIONS.md)
preserve `get_hit_snd` and `grnd_hit_snd` call parameters, lazy state reads and
the caller's shared source RNG. Their asset-free SDK command is:

```sh
python3 tests/audio/functions/run.py
```

This default covers 33 synthetic reader/API checks, not the original-C oracle.
The linked recipe explicitly prepares pinned original inputs for the 15,912-case
oracle and fourteen production-source mutants. Synchronous VM integration,
target PRNG qualification and DSP3/audio playback remain separate requirements;
post-host selection and silent/no-op consumers are not substitutes.

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
