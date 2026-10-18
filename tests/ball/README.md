# Actual-source ball and input/cue caller regressions

## Fire-generation fixture inputs

Context: `2026-10-17T14:23:38.829-07:00`. The following is a fixture repair for
the new source fire/handicap engine, not a gameplay fallback or old-proof rebase.

`BallTestFactory` now explicitly supplies the existing neutral presentation
fixture to **C1 as well as C2**: designated Home, four absent special heads
(`-1`), and `pup_bigball=0`. C1 previously passed no presentation input and its
current-default trajectory reached the real `maybe_pop_helmet` consumer. The
production missing-`team_head` guard is unchanged; the test declares the input
instead of skipping the consumer, inventing production defaults, or changing
the catch/control assertions.

The old C2 FIFO trace is a separately named **original random-opponent,
neutral-row compatibility fixture**. Only `FlipCompatibilityFactory` supplies
raw `SourceHandicapSettings(randomDrones: true)`, before construction, through
the actual shared fixture helper and production factory. Base adjustment,
win streak, powerups, seed, random provider, heads and actor state are not
tuned. Other caller scenarios and current source defaults remain unchanged.

The first default-profile divergence is record16 at source0,
`init_drones_prelineup:blitz_time:7`: raw13614959 is unchanged, but original
correct default handicap11 selects `3*blitz_time_ht[11]=3`, value2, instead of
the historical row5 bound150/value59. Both traces have134 draws; this is not
a lost RNG draw or FIFO integration bug. `handicap.c:140-174` explicitly maps
`random_drones` to `TEAM_BEARS`, whose original base is5. Unchanged compiled
original C proves tie/zero-streak/zero-adjustment/no-repetition rows `[5,5]`
for that declared mode, while the selected nonrandom CPU team1 correctly
produces current defaults `[5,11]`.

The literal134 count, SHA-256
`913231dc9d3f40ce902a05a19619a9d5b2872bd7ee563f35d70810ac2f00a976`,
queue/packet assertions, and58/18,018/18,062 inventories remain intact. The
passive `flip-compatibility-prefix.json` records all raw/site/order rows and
the actual raw handicap inputs. It is not a rewritten expected vector.
Historical receipts remain untouched; the original134 witness is no longer
misrepresented as the new default-profile trajectory.

The repaired full C1 baseline/final pass sentinel36, native470666, caller18018,
geometry32 and all20 existing compiled controls. The full old-nine C2
baseline/final pass36/470666/18062/41 and all25 controls. Focused FIFO retains58
assertions/all3 controls; the original134 records are exactly equal. An
independent20-assertion actual C1/C2/M1 fixture probe proves raw settings
forwarding, unchanged defaults and rejection of absent required head metadata.
The delayed-possession-credit mutation only acquired its new inline live-guard
context; it still changes18 to19 and rejects the same original credit assertion.
No production byte, old case inventory, expected count or queue/hash predicate
changed. The qualification history below is retained for its prior generations;
these B1 results are not full-M1/native/game acceptance.

## Current two-profile catch contract

The later bounded catch-only authorization preserves the exact default-seed1
scenario as a separately named **negative standing BatDown**, not a tolerant
Catch-or-BatDown check. Real281 sealed geometry admits the original standing
actor11/handicap5/source1000/pass45 pose (left-wrist distance0/pelvis<14).
The actual first defensive inclusive-`>=0` draw is12<20. The real branch retains
carrier-1/noCatch20, Bobble6, target-1/thrower11, flags0x10, VY-0.6 and
PassDeflected. The47/51 raw-prefix hashes match the **first explicit** Core
witness53748623, not an older unrecorded golden.

Only the positive controller test uses the newly declared,
parent-approved `source-catch-controller/raw99-all-sites/v1`:
**raw99 at EVERY draw**, including both constructor bank draws, sound/AI/catch.
It is ExternalUnverified, a DIFFERENT controlled input, not target LCG/seed,
natural history or a canonical-M1 vector replacement. No height, handicap,
flags, clock, geometry or catch predicate is altered. Every original
Carrier11/controller/credit17/18 assertion remains literal.

Focused `--section geometry --geometry-controls` retains all original32
geometry assertions and adds9 profile/negative assertions (41 total), followed
by a final baseline. Two new compiled controls reject `>=0`→`>0` and omission of
the standing-air guard; the existing controller-delay/drop control is reused.
The full current suite retains old20 controls, FIFO3 and these2 new negatives
(25 total), without duplicating the reused controller control.
The final current B1 baseline and final baseline pass sentinel36, scalar470666,
new-history caller18062 and geometry41; all25 isolated controls compile and
reject their named causal predicates. The prior failed stages remain preserved
below. This is component qualification, not full-M1/game/native equivalence.

Current caller output is separately enforced as
`17980 +14 +23 +1 +44 =18062`: pre-public-flow, retained FlipCases,
retained RawSnapAndPass, final original-inventory predicate, NEW FIFO additions.
The historical18017 expectation remains unchanged for old entries; exact
retained inventory18018 differs by one. Nothing is deleted or silently
rebased. Generated ordered snapshots remain ignored in `catch-two-profiles.json`.
The earlier blocked stages below are retained history, not current-input
equivalence or whole-game acceptance.

Current281 is explicit: add `--c2-inputs "$CANONICAL281_INPUTS"` and a new
`--output tests/ball/.artifacts/<name>` to the existing contact272-fixture command.
`BallTestFactory` supplies the existing controlled match/presentation settings
explicitly and delegates to `FullKernelInputs.Create` / real
`SourceC2Composition`; it does not construct replacement geometry, alter actor
vectors, or suppress the two actual bank draws. Old117/157/272 entries remain
separate controls.

Current281 qualification is **blocked, not passed**. Fresh native sentinel36
and ball470,666 assertions pass; the unchanged public caller then fails
“specified None flip-only command is a real retained menu edge.” Observed
before host462/source438: PlaySelect, Normal, side1, Down1/LOS17/FD47,
model/published flip=false. Submitted source None/offenseFlipped=true is
specified. After the existing step host463/source439, SourceCurrentInput is
still Continue and flip remains false; result pending is false at both points.
The passive historical trace records all134 draws (last is source438 whistle
chance). Owner diagnosis proves the flip is intact behind an older Continue,
not dropped.

A subsequently authorized **NEW FIFO compatibility history**, not an unchanged
complete original vector, follows that known head with no new action packet:
observed host464/source440 delivers the exact saved flip, empties the queue and
publishes true. All134 prefix records remain equal. Seventeen added checks also
cover empty-queue nullable false/true delivery, two ordered distinct menu
packets, exact-once and unadorned-None/no-starvation behavior. Queue observation
is read-only; no clear, coalescing, forced flip, new RNG/seed or production
change occurs.

The focused test then stops at the next **unchanged** lineup assertion:
normal host464/source440 remains PlaySelect, has consumed `play_spider`, and
retains `play_safe_cover` at the FIFO head; flipped host486/source461 is PreSnap
with an empty queue. No additional drain/wake, `pick` rewrite or RawSnapAndPass
adaptation was applied. Full callers/geometry, all20 old controls and proposed
FIFO-specific drop/filter/reorder controls remain unqualified. A focused
`flip-fifo` C# mode reproduces this bounded history and records both boundaries.

The next bounded authorization adds a coherent **C2-only menu-delivery
compatibility adapter** in FlipCases. Each offense/defense command is submitted
once; the saved flip is observed without resubmission. Its strict host bound is
`ceil((60 * known_packet_count - current_remainder) / 57)`. Every host checks the
queue's exact tuple order/count, and every executed wake checks the exact
predecessor/target delivered to SourceCurrentInput. Only default/no-action
observation frames follow submission, and observation stops exactly at target,
never at a desired phase/result. Old C1 first-wake code is unchanged.

Focused `--section flip-fifo --fifo-controls` now passes58 assertions
(44 separately counted FIFO additions) and all three compiled filter/drop/reorder
causal controls, including a final positive baseline. Normal/flipped lineups are
compared only after both saved offense and defense packets are consumed;
original play/coordinate/route predicates remain intact. This is an explicitly
NEW source-synchronized compatibility history, not the original host vector.

The resumed **full B1** subsequently stops at a different, unleased geometry
assertion: “actual original-pose catch caller establishes new carrier.”
Sentinel36 and scalar470,666 passed; caller18062 completed, but geometry and
full20+3 controls/final baseline did not. The caller count differs by one from
the provisional18017+44 inventory and is not silently rebased. No catch
state/predicate/geometry or RawSnapAndPass rewrite is authorized; deeper catch
state was not captured by the current failed baseline. Historical5e9/84e/935
receipts remain unchanged.

These package-free C# 7.3/.NET 10 checks compile the **actual** Contracts,
Gameplay and pure AssetImport sources. They use the shared real factory in
`../parity/FullKernelInputs.cs`, never a geometry interface substitute, copied
physics implementation, generic clip fallback, or invented roster/formation.

## Clean CI / explicit original inputs

Requirements: selected .NET SDK, Python 3.10+, GCC/Clang, and (only for optional
download) authenticated `gh` with public Git blob/tree read access. No Unity
Editor, native game executable, server or browser is launched.

From the repository root:

```sh
# Optional acquisition. No archive clone and no implicit network fallback.
python3 tests/parity/fetch_inputs.py --download \
  --output .squad/.scratch/SourceData/source-kernel-original

# Or supply your own exact original root, including complete pinned tree.json.
python3 tests/parity/prepare_inputs.py \
  --source-root .squad/.scratch/SourceData/source-kernel-original \
  --output .squad/.scratch/SourceData/source-kernel

python3 tests/parity/test_inputs.py
python3 tests/ball/test_runner.py
python3 tests/ball/run.py \
  --source-root .squad/.scratch/SourceData/source-kernel-original \
  --fixture-manifest .squad/.scratch/SourceData/source-kernel/fixture.json
```

See `../parity/README.md` for the companion original-C matrix/full-kernel host
adapter command. `--cache ROOT` and `--blob-file canonical/path=verified-file`
can provide read-only original inputs without download; bytes are still checked
against their exact Git blob pins. An existing isolated bad input is never
overwritten. The roster producer reconstructs the original Git tree hashes
before accepting its twenty-two source files.

The preparation orchestrator invokes the **permanent** animation, playbook and
roster producers. It also uses the permanent first-pose WAN reader and copies
only the two exact model/skeleton headers needed by the pure geometry consumer.
The canonical 117-clip/10,288-selected-frame manifest pin is
`f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274`;
historical `18af…`/`2baec…` manifests are **not aliases**. Complete animation
data, original matrices, catalog tables, headers, generated C, native binaries
and numeric cases remain ignored. No machine-specific source path is a default.

The shared fixture additionally prepares strict movement data and distinct
selected156/complete157 libraries. Numeric ball/original-pose cases keep
canonical117. The longer public raw-B caller campaign explicitly uses complete157
because actual movement can reach clips absent from117; its receipt records both
manifest identities. Simulation and geometry always share the same selected
library. Mandatory movement settings and a named source-DIP test camera are
injected without claiming production camera/render parity.

The next sound-integrated fixture also carries real SAFN1 data, its pinned audit
and unchanged original C include (443 prepared files from69 original inputs).
The shared factory binds that immutable production datum before the first source
wake; it never substitutes muted/zero sound. The explicit `--sound-functions`
override remains available and is byte-guarded. All **488,751** B1 assertions and
**20** named mutants remain unchanged; actual synchronous sound VM/RNG proof is
separately documented in `../audio/vm/README.md`, not counted as ball/DSP parity.

All output goes below this checkout's ignored `.squad/.scratch/SourceData/` or
`tests/ball/.artifacts/`. Existing prepared fixtures require explicit `--reuse`,
which verifies their bytes read-only. Missing/mismatched inputs fail rather than
silently replacing the library, geometry, roster or playbook.

## Evidence inventory

The default command requires these exact nonempty assertion inventories:

| Layer | Assertions / cases | Boundary |
| --- | --- | --- |
| Ball/catch numeric comparison | **470,666** assertions on **46,096** native-C cases | Catch decision prefix, complete deflection, fumble/bobble decisions and launch, ball-process physics prefix plus original LOS-crossing block |
| Input/camera/cue callers | **18,017** assertions, including **800** original-C `plyr_have_ball` and **12,544** original-C receiver cases | Real source raw input drain/transform, actual VM cue callers, scoped internal predicate/VM setups and public raw-B snap→center wrist→QB catch→selected throw/wrist flow |
| Original-pose geometry/lifecycle/control | **32** assertions | Fifteen original matrix-vs-quaternion pivot results; genuine wrist-only strict `<5` reach beyond pelvis `<14`; actual throw frame; PlayerDown/wobble ordering; focus/restart/raw49; direct actual catch/carry caller ownership and credit at ticks 0/17/18 |
| PAT/kickoff sentinel | **36** assertions plus one native original touchdown-assignment witness | Actual `Resolve`/free-PAT states retain `Down=-1` through `SourceAiFrame`; no clamp; existing normal 0..4 constructor range and invalid limits remain protected |

The native generator reads only exact pinned original blobs at
`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`. It extracts original active statements,
function windows and handicap tables **at runtime**, recording byte offsets,
line ranges and SHA-256. The authored `.c` files here contain host projections,
prescribed inputs and service observations—not upstream gameplay bodies or
original data tables. The archive's invalid `#ifdef 0 //DEBUG` diagnostic span
is omitted with its exact original span hash recorded; active statements are
never rewritten. This is explicit release-profile preprocessing, not a source
repair or an invented reference formula.

The C harness supplies projected host structures, host `sqrtf`, explicit RNG
draw vectors and symbolic external-service handles. Sound/effect callbacks are
outside the native ball prefix comparison. Real VM tests separately inspect
actual cue identity, signed arguments, shared draw context, singleton
`randrng(1)`, noncarrier shakers, detached batches and PlayerDown→ground→shaker
ordering. A PlayerDown cue is an observation token, never an instruction to
replay gameplay/effect lookup.

Original receiver equal-key **target libc `qsort`** order is unresolved.
Prescribed native cases compare ordinary selections; distinct selected-key ties
must fail explicitly in production, not pick an arbitrary actor. The test
records/protects this boundary instead of asserting host libc is the arcade.
Generator randomness is only deterministic input enumeration, not a claim about
the historical `random()`/`rand()` implementation.

Internal predicate, VM and catch/carry setups are clearly scoped test seams.
The raw-B flow separately uses public commands without state mutation. Neither
layer proves complete original match, scheduler, M1, C1/C2, presentation,
native-MIPS or browser parity.

### Active M1 fresh-press causality

The complete inventory is now **488,751 assertions**, up **15** from488,736;
the other layers and all prior caller assertions remain.

Pinned `player.c` blob `5744c51f30b625b9f80b92388ab49ff0ebc5bb2c`
stores prior physical buttons/history at lines832–840, acquires current
switches at878–885, and computes `but_new = but_cur & ~but_old` at900.
Its release `_mode_qbps` branch at1641–1650 decrements the auto-hike counter
and permits A/B **new** buttons only after the grace period; expiration is a
separate automatic branch.

The active Core path is now `UpdateSourceMovementPlayers`, not the compatibility
`SourcePlayerDriver.Update`. `SourceMovement.BeginTick` recomputes current/old/
new fields from acquired held state; the consumed host-latched press must be
merged into **`state.NewButtons` after BeginTick and before ApplySourceMovement**.
The `raw-new-pressed` mutant deletes precisely that production wire. Mutating the
old compatibility `actor.SourceNewButtons` merge no longer exercises this path.

The public scenario now verifies first held B, repeated held B, physical release,
old/history fields and the original early-grace rejection. It drives two
identically seeded real simulations by the same public commands until a complete
B press/release straddles a skipped source wake. The paired no-tap control stays
pre-snap at the same source tick/countdown. The real tap must produce fresh B
with **held zero**, unchanged physical held history, exactly one `Snapped` event
on the first executed wake and a **positive** manual-hike counter, then clear
the fresh edge without replay. No forced state or automatic-hike timing can
stand in for the press.

Ignored `native/raw-B-input-causality.json` records actual source ticks,
countdown, physical/fresh button fields and the negative control. Native cases,
real117/157 library identities and the unfiltered VM programs are unchanged.

## Real-source mutations and API profile

The default runner rejects **20 isolated actual-production mutants**: the
original 16 Core gates plus sentinel/range and immediate-control/credit gates.
Each mutated copy must compile, run and fail its named offending-field
assertion. Compilation failures, crashes, unrelated exceptions, skipped/empty
discovery, altered inventories and status-only green do not count. Baseline and
final baseline must pass, while all actual production, prepared input and
original-C input hashes/inventories remain unchanged. Only the runner's own
isolated source/build trees are removed; receipts/logs remain ignored.

For an author-only focused check, use explicit
`--section sentinel --no-mutations` (or `native`, `caller`, `geometry`).
That is reported as **not** the complete CI proof.

The executable's .NET 10 success alone does not certify Unity's API profile.
With an actual installed .NET Standard 2.1 reference:

```sh
python3 tests/ball/compile_profile.py \
  --reference /path/to/Unity/Editor/Data/NetStandard/ref/2.1.0/netstandard.dll
```

This directly compiles the complete actual pure closure with C# 7.3,
`-nostdlib+` and warnings-as-errors. It starts no Editor and is not an
IL2CPP/WebGL/native build. Optional `--expected-reference-sha256` pins the exact
reference bytes. Parent owns CI configuration and publication.
## Current Contact272 generation

For the C1+Sound generation, add `--input-profile contact272` and supply the new
`source-contact-kernel-real-inputs/v1` fixture prepared by
[`../parity/prepare_contact_inputs.py`](../parity/prepare_contact_inputs.py).
Both numeric/geometry and actual public callers then use the real production
272-clip/24,892-frame library (`9cae962b…ede9`), not a renamed117/157 or union
adapter. Legacy defaults remain historical controls. Exact existing assertion
counts and all20 named source mutants remain required; a production C2/software
fault is a failure to report, not permission to omit or soften a scenario.
The strict reference compiler now includes the actual pure production
`Runtime/SourceContactComposition.cs`; this is not a native Unity build.

The current full-generation attempt is **blocked**, not a20-mutant pass.
In the unchanged public kickoff flow, actual `tie2c_v_anim` TURNHACK512 changes
actor8 facing446 to-66 at source443; the following source444 AI-frame
construction rejects `facing`. Pinned `ANIM.C:2353–2364` also uses signed
remainder here. Do not normalize the VM or change inputs merely to conceal the
failure. The Core owner must resolve the source-faithful raw-heading/AI-boundary
contract before complete B1 acceptance. Ignored
`native/first-invalid-heading-producer.json`,
`native/first-public-caller-failure.json` and
`native/actual-caller-before-public-flow.json` retain actual producer, consumer,
raw-stream and partial caller evidence. Existing callbacks are invoked exactly
once by the passive instruction witness; no game state or RNG value is changed.
