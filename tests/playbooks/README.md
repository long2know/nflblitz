# P1 original-source regressions

Permanent promotion of the frozen playbook/drone/random harness. Requirements:
Python 3.10+, GCC, and .NET SDK 10 with the .NET Standard 2.1 reference pack.
No Python packages, historical makefiles, Unity Editor, browser or service needed.

From a clean checkout:

```sh
# Optional network acquisition: only 14 pinned public blobs, decoded-byte hashes checked.
python3 tests/playbooks/fetch_inputs.py
python3 tests/playbooks/run.py
```

The fetcher uses `gh api` for metadata-pinned Git blob identities from
`historicalsource/nflblitz2000@89f840d58d152a2a9f230cb8d0cdffbd554c8ded`.
CI needs GitHub API network access and the usual `gh` authentication/rate budget.
It never clones/runs the historical game, fetches credentials, or overwrites a
wrong cache. `--cache path/to/canonical/archive` can be repeated to reuse read-only
verified inputs. Alternatively run entirely offline:

```sh
python3 tests/playbooks/run.py --source-root path/to/exact/pinned/archive
```

Canonical, case-sensitive paths and hashes are in `pins.json`. Missing, changed,
newline-normalized or escaping inputs fail; there is no implicit network or
scratch-path fallback. The runner is the recommended CI step, **after** the
explicit fetch step. Production/source-profile changes require deliberate pin
and matrix updates, not relaxed gates.

## What is proved

The runner snapshots all ten current actual Contracts sources (including
the existing actor/ball presentation and contact contracts plus
`SourceFireSpfxContracts.cs`, `SourceKickMeterContracts.cs` and
`SourceCameraContracts.cs`), the pure
SourceClock/MatchRules/MatchFlow dependencies, and the final three P1 helpers.
It compiles them as **C# 7.3 / netstandard2.1, warnings-as-errors**, then executes
a net10.0 comparison harness. A test-only friend-assembly attribute exposes the
internal fixture constructors and synthetic timer-state inputs; no production
logic or visibility is edited. Hot full-kernel/M1/presentation files are excluded.
Live production/producer inventory and hashes must remain unchanged throughout.
The model includes `Contracts/**/*.cs`; its dependency inventory guard requires
the complete named ten-file source set, without compiling presentation or the kernel.
Missing, extra or same-count renamed Contracts fail before compilation with the
offending missing/unexpected filenames. All ten actual files remain in the
snapshot/hash inventory and the model's recursive Contracts compile include;
no DTO substitute, omitted file or stale assembly is used.

* Execute the actual permanent producer and its eleven synthetic tests in an
  isolated byte-identical copy. Validate its actual metadata schema, pinned input
  provenance, catalog digest, full inventories and byte-for-byte reproducibility.
* Compile **53 selected original C functions** with normal 57 Hz /
  `USE_OLD_QB=FALSE` branches. The extractor removes includes and uses GCC's
  preprocessor; it does not rewrite decision expressions. Source/blob, selected
  function and translation-unit hashes, compiler flags and boundary metadata are
  recorded. Original functions/tables are fetched/extracted at runtime, never
  copied into these permanent fixtures.
* Compare 7,565 compiler-evaluated catalog records: all 88 plays, 29 formations,
  140 route programs, 13 CPU families (226 weighted entries **with duplicate
  ordinals**, plus six singletons), 257 quarter-sines, 49 directions and 34
  twelve-level handicap tables. Preserve formation scripts/roles/control masks,
  play references/flags, exact route words and CPU selection identity.
* Compare **21,141,803 assertions**: 20,000 numeric cases, 8,640 lineup inputs,
  1,200 receiver frames, 1,200 blocker frames, 300 full new-play initializations
  and **220,208 original-C decision ticks**. The frozen 219,776 ticks are retained;
  432 added switch-timing ticks cross side/stage/think-time/stick-only and the
  30/31-tick smoothing boundary. All 140 actual route programs run for 750 ticks
  on both sides. QB/DB/line/carrier, snap/captain/kicker stages and defined
  receiver-ball branches reuse the frozen harness matrices.
* Distinguish retained actor controls from newly written `CommandWritten` /
  `ButtonsWritten`, check switch timers/smoothing and ordered cross-defender
  effects, and compare RNG kind/bound/raw/value/global order plus caller-specified
  actor/process/tick/within-process order. Deterministic synthetic **input tapes**
  are passed explicitly to C; neither target nor host libc PRNG is substituted.
  Counts are exact gates: 19,779 wrapper calls, 192 retained stick commands,
  712 retained button commands and 493 cross-actor effects.
* Reject three real regressions in isolated copies: replace only ShortOffense
  ordinal 27 while retaining pool length, corrupt an actual route opcode to 999,
  and weaken the production `FindReceiver` five-yard `<=` exclusion to `<`.
  Build errors/crashes/unrelated assertions do not count as mutation kills.
  Re-run the complete unchanged baseline afterwards.

Fourteen small runner tests verify source/inventory/manifest/mutation/failure
gates; they are explicitly not gameplay evidence. There are no skips or
status-only success fallbacks. `.artifacts/result.json` contains the receipts,
counts, commands, timings and unchanged LiveSources hashes. Logs, fetched inputs
and converted catalogs remain ignored. Isolated copies/build/native artifacts
are removed after the run. The local ignore rules explicitly unignore both
permanent `.csproj` files from Unity's repository-wide generated-project rule;
runner tests verify they are publishable in the actual checkout.

## Explicit limits

“Native C” means **host-compiled functional oracle**, not Unity native, the
original MIPS binary or a full match. Actor trajectories, animation feedback,
service state and raw tapes are independent deterministic synthetic inputs to
unchanged original functions, not original physics/animation/scheduler traces.
The cached-geometry input/copy contract and explicit RNG-unavailable/negative/
zero-bound guards are synthetic seam tests, identified as such in the harness.

Target libc PRNG bodies, equal-key target `qsort` ordering, real scheduler/process
ordering, audible-side initialization and original undefined C branches remain
unverified. Equal-time blocker ordering is qualified, never treated as a parity
pass. Snap audibles are supplied explicitly. No original art/motion/assets,
full-game equivalence, P1 integration completion, or full-parity closure is
claimed by this necessary regression coverage.

## Finite public scheduled-route callers — November 11

The separate `LiveCallerChecks.csproj` compiles the **actual complete gameplay
kernel**, not the pure-helper model above. It reuses the authenticated
Contact272 full835-file tree, genuine M1288 inputs and unchanged catalog
`b180291759fb22c8ae7b2de7d417867e15243f6071bb1a672f11bb2fb6da4bd3`.
No catalog words, historical inventories or old oracle counts are rewritten.

The complete native rerun entrypoint is repository-delivered in `run.py` and
`live_callers.py`, alongside the unchanged managed project/checks. Set explicit
authenticated input roots and a **nonexistent output directory with an existing
physical parent**:

```sh
python3 -B tests/playbooks/run.py --live-callers \
  --source-root "$ORIGINAL_ROOT" \
  --fixture-manifest "$CONTACT272_ROOT/fixture.json" \
  --sound-functions "$CONTACT272_ROOT/audio-functions/sound-functions.safn" \
  --c2-m1-inputs "$GENUINE_M1_ROOT" \
  --output "$NEW_OUTPUT"
# Add --dotnet /path/to/existing/dotnet if the .NET10 SDK is not on PATH.
```

`ORIGINAL_ROOT` contains the exact14 canonical source paths in `pins.json`
(the existing explicit fetcher can acquire them). Contact272 requires its
complete835-file authenticated tree; M1 requires the genuine288-file profile.
The runner checks the fixture/SAFN hashes, original Git blob identities and
before/after input inventory/bytes. It does not prepare/download game assets,
read an evidence archive, assume a personal directory, or fall back to another
profile. Use an existing supported POSIX build environment if the checkout
filesystem cannot support .NET builds; `--output` may be on that owned storage.

The opt-in path snapshots only the actual managed compilation dependencies,
builds the native input/output protocol from the authenticated originals, runs
the exact finite partition, compiles an isolated **actual producer disconnect**,
requires defender12 flags0 versus native64, and forces a separately compiled
clean final. The final DLL, complete observations/events/RNG and native
transcript must be byte-identical to the initial clean run. Build errors,
crashes, skipped cases and status-only receipts cannot qualify. Only a genuine
missing-assets error permits restore, using a local empty package feed.
`NEW_OUTPUT/result.json` is a **new delivery-integration receipt**, not a
replacement for the frozen qualification below; logs, both clean reports,
the rejected control and native source/provenance remain in that fresh output.
The unchanged default `run.py` path still selects its original broad matrix;
**it is not executed by `--live-callers`**.

The extended protocol reuses the unchanged53 original-C functions and adds the unchanged native
`abort_drone_route` bridge. The extension supplies actual before-state,
ordered actor observations, retained detector matrices and production raw RNG;
it does not rewrite a decision expression. The unextended old executable
does not understand this protocol. The original qualification's private
`harness/run_once.py` and `harness/native_projection.py` remain historical
artifacts, **not dependencies of the repository entrypoint**.

The finite partition has **15 named cases /511 actual source wakes /
277,768 exact assertions**, with6,132 scheduled actor-to-native comparisons
and124 native RNG records. It executes public typed play choices, real
formation/coverage admission and ordinary pre-player/player-process callers:

- Hail Mary near/far: facing, LOS thresholds, run-distance start/hold/finish,
  spin/juke start/wait/finish/skip, turbo, delay-block, fake and improvisation.
- Upper Cut near/far: wave-arm start/finish/skip, including the actual
  upper-body animation consumer.
- Real `MOVE_DEFENDER`: produced90 reaches the later scheduled defender as89
  in the same source wake, then88 on the next wake.
- Real delayed block: produced60 expires naturally, becomes `JOB_BLOCK`,
  then consumes the next actual pre-player assignment.
- Terminal receiver block and actual backpedal; native formation targets,
  man/zone/rush, zone continuation and selected blitz expiry.
- Real fake-zone92→0→man coverage and the original source-authored
  probability256 Middle Pick fake/ghost/defender-timer consumer. The separate
  probability50 case retains rejected raw66/77; its seed is not retuned.

The exact catalog contains18 positive opcode classes, but only17 are reachable
here:1–15,17,20. The only `WAIT16` is `pr_QB_sweep` word1, after the original
**non-advancing terminal `BLOCK`** at word0. A declared controller-handoff case
runs three real wakes and retains word0; no PC/timer assignment invents WAIT
execution. End0, SetFlag18, CoverWeak19 and negative custom controls are not
present in active catalog instructions. Catalog membership is not execution.

Two clean compiled actual-source disconnects go red: removing cross-actor
effect consumption produces defender12 flags0 versus native64; dropping only
the turbo switch write produces actor4 buttons0 versus native4. A forced fresh
final has an identical DLL, complete observations/events/RNG and native
transcript. All511 assignment frames have **zero equal-time qsort ties**.
Every archive's71 regular members were freshly host-read before namespace
teardown. Existing15 asset-free P1 guard tests also pass, with their owned
scratch output relocated to POSIX storage only.

The new source epoch explicitly binds approved
`GameSimulation.Source.cs` `9e747e64…`, `SourceCatchGeometry.cs` `7508d895…`,
sampling `00cf9341…`, animation state `d8de8248…` and programmes `eb68dd56…`.
No production change or route ownership override was needed.
Receipt `P1-SCHEDULED-ROUTES-QUALIFIED-20261111-01.json`
(`7a4f8c0b…`) is under the owned Nov11 `p1-live-routes-20261111-01`
evidence root; final archive SHA256 is
`df3573c88deb044586ba4e2df1837c460a1af2beaf68deca06298e07cae5cd88`.

These are **Home offense / FourStation with two participating humans** and
explicit held-QB/live, noncollision and one-time coordinate-threshold fixtures,
not natural matches or raw-menu evidence. Read-only frame reflection observes
inputs; it never invokes a private route dispatcher. Human player `JOB`
ownership and native gradual boundary→abort behavior are explicitly modeled.
Failed observer/fixture assumptions remain archived, not called production
defects. The assertion count does not repeat the220K synthetic campaign.
Other side/station matrices, custom/audible reinitialization, free-ball/pass
defense branches, native scheduler/ABI/RNG and the guarded CPU-QB/q0 boundary
remain separate; this is not full P1-row closure or an image-delivery gate.

## Nonempty CAP/audible production callers

This opt-in partition does **not** rerun the stock15/511 or default220K matrix:

```sh
python3 -B tests/playbooks/run.py --custom-callers \
  --source-root "$ORIGINAL_ROOT" \
  --fixture-manifest "$CONTACT272_ROOT/fixture.json" \
  --sound-functions "$CONTACT272_ROOT/audio-functions/sound-functions.safn" \
  --presnap-inputs "$AUTHENTICATED_PRESNAP282_ROOT" \
  --hud-inputs "$AUTHENTICATED_PLAYER563_ROOT" \
  --output "$NEW_OUTPUT"
```

The explicit original root additionally supplies authenticated `cap.c`,
`include/cap.h`, `PlayStuf.c`, `pmisc.c` and `include/GAME.H`. No network,
personal directory or evidence archive is a dependency. The genuine282/M1
callback tree, unchanged563 HUD/bootstrap/impact inputs and explicitly admitted
Portable default-audible factory are **not AV306/image evidence**.

Four public cases cross Home/Away offense and one-human/cooperative
FourStation configurations. Existing editor/storage and numeric CAP proofs
are reused as upstream scope, not repeated. The new cases supply source-valid
nonempty grids through saved sessions, progress through the real opening
kickoff/result, choose custom page4 with raw C/B inputs, and exercise signed
custom audibles, ORIGINAL, cooperative motion/controller reassignment, real
A/center-VM snaps and ordinary live CPU-receiver callers. No private dispatcher,
script/PC/flip/tick setter or outcome-specific RNG tape drives the game.
The portable production random streams keep their default seed; authentic C
receives their observed raw values, not an alleged target PRNG.

Native expectations compile63 unchanged selected function bodies, including
the actual CAP converter, `PlaySel__MakeChoice`, audible getters/reset and
`off_captain`/`update_stations`. The active `game_proc` AUDIBLE_RESTART control
span is compiled without expression rewrites. A single **read-only hook in an
isolated source copy** captures the actual focused receiver entry; it does not
alter canonical production. Every-source-wake actor/route/ball/control state,
RNG and event JSONL must match the uninstrumented canonical run exactly.
A copied actual CAP secondary-controller producer disconnect must fail its
original-C control field; a separately compiled clean final must reproduce
the canonical DLL and every state/RNG/event byte.

The native receiver `player_blocks[ball_carrier]` read at `drone.c:1677` can
index−1 on loose/hike inputs. Those **actual entries are recorded explicitly
unqualified and are not executed or counted as native matches**; no host layout
is invented as arcade ABI. Later held-ball entries retain exact comparisons.
The result reports captured, compared and ABI-unqualified counts separately.

The November11 delivery executes four cases /3,320 actual source wakes per
positive variant. Canonical baseline/fresh each pass6,573 assertions; the
read-only entry-capture variant passes33,270. These are **coupled variants,
not independent counts to add together**. All639 actual focused entries are
accounted for:635 exact defined-C caller comparisons /14 observed route RNG
records, plus four ABI-unqualified carrier−1 hike entries (Home ticks651/713,
Away597/591). Every source wake's state/RNG/events matches across canonical,
capture and freshly compiled final. The four clean/controller builds have
zero warnings/errors; the protected CAP control276→260 disconnect goes red.
New `p1-cap-callers-20261111-01/delivery02/DELIVERY.json` and matching host
readback bind this generation; all65 export members were read before namespace
exit. The stock15/511 receipts and production9e747/7508/00cf+d8/eb68 remain
unchanged. No gameplay repair was required for these defined custom callers.

The actual CAP receiver producer emits only1,2,4,5,9,10,11,12,14. It never
produces WAIT16, SetFlag18, CoverWeak19 or negative controls. Its shared line
route is `ds_none`/IMPROVISE5 and QB route is terminal `pr_QB_block`/BLOCK8.
Stock `pr_QB_sweep`'s WAIT16 after non-advancing BLOCK remains unreachable;
switch-arm presence is not a custom producer. Original audible closing leaves
`audible_shown` until snap: a closed menu cannot be reopened in that same play.
The signed live continuation therefore uses the still-open original menu,
not a fabricated reopening or a flip setter.
All nine CAP-emittable receiver classes are actually observed across this
finite matrix (not every class in every cell). The next exact native boundary
is the four recorded center-hike carrier−1 entries above: `player.c:306`
declares the array, but source structs do not establish the shipped MIPS
address/preceding-storage contents needed to define `player_blocks[-1]`.
That missing release ABI/global-layout fact is not a missing custom opcode.

## Cabinet saved and known-none custom selection

The separate bounded managed checks use an **existing approved pre-snap-switch563**
data root, without preparing assets or promoting it to the default-audibles profile:

```sh
tools/source-player/dotnet.sh build tests/playbooks/model/CustomSelectionProduction.csproj --no-restore -c Release
tools/source-player/dotnet.sh run --project tests/playbooks/CustomSelectionChecks.csproj --no-restore -c Release -- /path/to/approved563/data
```

Restore the selected project only if its assets file is missing. Production is
also compiled as C#7.3/netstandard2.1. The checks exercise the actual quantizer,
input buffer, game_proc, timer, shared RNG and source selection using authenticated
HUD/catalog/M1 inputs. A declared post-result entry and injected custom4 highlight
cover the otherwise unreachable timeout branch; known-none ordinary page cycling
skips custom4. These are not browser, native or whole-match acceptance.

Only deliberately new-player sessions may call
`game.BindSourceCustomPlaySession(SourceCustomPlaySession.KnownNone(configuration), data.Hud)`
before the first wake, then construct
`new SourceCabinetPlaySelection(data.Hud, customPlaySelection: game)`.
The session must retain the same actual FourStation configuration and Portable
pre-snap-switch composition. Omitted/unavailable inventory is not empty. A missing manual custom
choice produces no selection or RNG; a valid forced known-none choice consumes
one `PlaySel__MakeChoice:2847` draw and resolves the actual current first-page
team/FG/punt/PAT slot without changing the displayed page or cursor. Custom
audibles do not acquire a timeout fallback.

For actually loaded records, bind
`new SourceCustomPlaySession(actualSavedRecordSession)` instead. Core converts
the source grid through `SourceCustomPlayCodec` into private per-Game plays and
routes. Typed `SourcePlaySelection.SavedOrigin` retains station/slot/session
identity without adding strings to the authenticated static catalog. Saved
captain page4, requested/first-existing forced selection, actual live receiver
routes, signed audibles and original saved-play/flip restoration are exercised.
Zero audible rows retain the real `select.c:960` defaults. The separate audible
factory authenticates its own catalog input; the base profile is not promoted.
The read-only `SourceCabinetPlaySelection.CustomPlay` supplies the actual
captain's grid for custom-card rendering, never a stock-play replacement.

Bindings are immutable and survive administrative reset. Changed records require
a fresh Game/session. `BindSourceRecordCommitBarrier` is optional and requires
the actual saved session and cabinet-flow bindings before wake1. At the original
full-game save guard, Core captures `SourceFinalRecordCommit` before final stats;
Pending/Failed sleeps the real game process, Committed/explicit Declined resumes
once, and reset cannot bypass the wait. Runtime owns IO, durable match IDs and
retry. Partial declined games retain their actual period count and never acquire
a fabricated four-period save. These are bounded source-process fixtures, not a
claim of browser persistence, natural full-match or native equivalence.

### Versioned CI source binding

Current CI admits an exact seventeen-contract closure: the retained fourteen
plus the named introduction, pre-game-sound and saved-record declarations.
The historical thirteen/fourteen inventories and `pins.json` production hashes
remain unchanged. Current pure-helper hashes are a separate source generation;
they never certify the old source identity.

The same frozen public input blobs, producer/catalog pins, original-C matrix,
expected assertion partitions and all three causal controls still apply.
Current results explicitly report `historical_source_identity_requalified=false`.

The reviewed interior-cache continuation has its own exact
`reviewed-sdk-interior-p1-current/v1` identity, admitting only the approved
`SourceDroneAI` hash `1d2de05b...`. The previous
`complete-original-p1-current/v1` helper hashes remain separately selectable;
neither identity admits the other's AI bytes. Historical `pins.json`, producer
inputs, case expectations and controls are unchanged. This source-admission
update does not qualify a new recording, native target runtime or full match.
