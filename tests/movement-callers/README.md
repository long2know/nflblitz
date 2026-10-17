# Actual M1 caller, VM, cache and post-animation physics regressions

This suite promotes the frozen Core caller checks, not a new movement
implementation. It compiles the actual gameplay/helpers/Contracts/AssetImport
closure and the existing pure cinematic-transform producer. No production
source, runtime/presentation composition, original flags or data selectors are
changed by these tests.

## Reproduce from a clean checkout

Prerequisites: the repository's .NET SDK, Python 3.10+, GCC/Clang, and optional
authenticated `gh` only for explicit pinned-original acquisition.

```sh
# Optional acquisition of only public metadata-pinned original blobs/tree:
python3 tests/parity/fetch_inputs.py --download \
  --output .squad/.scratch/SourceData/movement-original

# Or supply your own complete, exact pinned source root with tree.json.
python3 tests/parity/prepare_inputs.py \
  --source-root .squad/.scratch/SourceData/movement-original \
  --output .squad/.scratch/SourceData/movement-fixture

python3 tests/movement-callers/test_runner.py
python3 tests/parity/test_inputs.py
python3 tests/test-runner.py
python3 tests/movement-callers/run.py \
  --source-root .squad/.scratch/SourceData/movement-original \
  --fixture-manifest .squad/.scratch/SourceData/movement-fixture/fixture.json
```

Explicit read-only `--cache ROOT`/`--blob-file canonical/path=verified-file`
inputs are available instead of download. No Primary-worktree path is a
default. Data, raw headers/motion, generated catalogs/C, executables and
receipts remain ignored in this checkout. Existing prepared output requires
explicit `--reuse`, which verifies its bytes read-only.

Preparation uses the existing permanent producers:

* Canonical B1 `ball_sequences.json`: **117 clips / 10,288 frames**, manifest
  `f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274`.
* Immutable selected movement base: **156 clips / 13,420 frames**, manifest
  `5e8fc7c5f2011303fd7b1aea87e6f957b1acdf4cbb15a43fd82f978d4b944b68`.
* Explicit complete movement extension via
  `tools/animation/prepare_movement_complete.py`: **157 clips / 13,490 frames**,
  manifest `f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e`.
* Runtime-safe `tools/movement/export.py` payload
  `89c79007e04e9a17894c126c34dc66c199eb5a5961a4c749b1eb6b3e0e3951d6`.
* Actual playbook, 31×16 roster and original fifteen-node model/skeleton
  producers already used by the shared B1 factory.

These are distinct identities, **not aliases**. All 156 old clip bytes remain
unchanged in the new composite library. B1 numeric/original-pose tests and the
short host adapter retain canonical117. The longer public B1 caller campaign
now reaches M1 `stiff_arm_f_anim`/STIFARMF, so it explicitly constructs the
separately identified157 runtime pair rather than pretending that clip exists
in117. The same selected actual library is always passed to simulation and
`SourceCatchGeometry`.

## R_SKIP correction and evidence layer

Pinned original wan2seq defines **A_SELECTED=0x4000** and
**A_COMPRESSED=0x0001**. Original R_SKIP flags0x4000 are selected/uncompressed.
The older handoff's “unselected” premise was wrong. The real source gap is
missing limb5/right_hand, not a missing selected bit.

The opt-in `wan2seq-retained-limb/v1` replay producer reconstructs original
converter buffer state in exact source processing order. R_SKIP retains
right-hand authoring indices0..69 from the preceding selected uncompressed
Huffrun2 writer. It is neither an arbitrary pose/clip fallback nor flag rewriting.
The production loader accepts the separately named replay schema only with
explicit matching inventory qualification. Default incomplete-clip rejection
and old56/117/156 identities remain intact.

The default caller command requires actual157 and an **unfiltered** smoke
beyond the former source-tick2299 asset stop. It must not treat a missing clip,
filtered taunt, alias, forced header flag or substituted pose as success.
`--bounded156-diagnostic` separately preserves the exact known-missing-R_SKIP
control; its receipt says `bounded-passed` and never complete publication green.
That historical diagnostic is not the default CI qualification.

Replay demonstrates pinned **source-converter** behavior, not the missing
artist-authored right hand or shipped SEQ/quantization/binary identity.

## What is actually checked

The frozen caller inventory is **36,732 assertions**:

* All **16 real hurdle VMs**, all **seven** source MaybeDive callback windows
  each, real root/Y/velocity/landing changes, one immediate GeneralRun transition.
* Shared side counts written **before** synchronous script predicates;
  captured pre-run eligibility survives actual UNINT/two-part changes.
* Actual WPN control/station/fake-B repair and `ReturnFromMode` skip only the
  remaining mode body, not the outer animation/physics.
* Real QB lower2/upper4 proportional transitions, pose/PC cloning, distinct
  backward-left sequences and upper turnhack.
* Actual DB target/spear→T_CRZDIV→ANI_LEAPATT: previous post-player camera-joint
  cache, current camera xform, exact nine operands/**36 bytes** and22 flight ticks.
  No new WorldJoint or identity-transform substitute.
* Real post-all-14-actors **14×15** camera-space cache compared at source wakes.
* Public no-state-mutation raw gestures complete **2,400 host / 2,280 source**
  ticks; actual A-release-A hurdle→slide/reach-dive, airborne trajectory/landing.
* Strict global `speedPowerup` versus three executing-team masks0..3;
  field0..6, exact station fire/infinite freeze/drain/defense recharge.
* Existing raw A/B/C queued tap, focus/restart and once-per-source-transform
  behavior remains exercised.

From those real VM executions, the suite captures **664 actual post-animation
Core rows**. `compare_physics.py` extracts unchanged pinned `player_proc` active
physics and `in_bounds` at runtime and requires **5,312 exact** binary32/flag
comparisons. The authored C file contains only host projections and observation
code. No first-header animation `Responses` or second C# physics formula is the
native reference.

The strict runtime-only loader accepts only `Constants`, `Tables`, seven
friction values and12×6 defense rows. `Responses` and test first-header state
are absent; selector repeats are preserved. `SourceMovementSettings` uses
`(fieldIndex, infiniteTurboTeamMask, globalSpeedBool, fastTurboTeamMask,
runOutOfBoundsTeamMask, violenceBool)`—the team masks are not booleans.

## Camera fixture and B1 preservation

The shared factory registers its explicit **512×384 source-DIP test camera**
before the first wake. It uses the actual pure cinematic transform for a named
moving eye/focus seed; it is not browser resolution, identity geometry or an
original normal-camera/render qualification. Current position/12-float xform/
source resolutions are submitted **inside** the existing pre-player
`SourceCameraHeadingProvider` at the executing source tick.

The internal leap cases retain their existing explicitly named controlled setup
and real previous cache/current matrix. Public raw campaigns separately avoid
state mutation. Both evidence layers are qualified, not conflated.

All prior full-kernel **332** contract/adapter assertions remain: legacy
comparison, every60-host/57-source step, skipped1/21/41, detachment, ordered
two-choice edges and restart. The separate synthetic two-part test now uses the
source **running throw** (`qb_rthrow_sr_anim`), which retains MODE_TWOPART; its
old planted throw deliberately clears that mode. Masks, headings, distinct
layers, interrupted origins and assertion inventory are not relaxed or dropped.

For affected B1 preservation, using the prepared fixture:

```sh
python3 tests/source-oracles/run.py \
  --source-root .squad/.scratch/SourceData/movement-original --full-matrix
python3 tests/parity/run.py --cases tests/source-oracles/.artifacts/cases.json \
  --full-kernel --fixture-manifest .squad/.scratch/SourceData/movement-fixture/fixture.json
python3 tests/ball/run.py \
  --source-root .squad/.scratch/SourceData/movement-original \
  --fixture-manifest .squad/.scratch/SourceData/movement-fixture/fixture.json --no-mutations
python3 tests/run-core.py
```

## Mutations, hashes and API qualification

The default suite proves all **eight real-source caller mutants** fail their
specific offending assertions: callback-time shared count, captured eligibility,
WPN return, wrong camera cache, wrong leap operand, deferred GeneralRun,
yards/source-unit physics and team-mask collapse. A compile error, crash,
unrelated failure, empty/missing summary or changed inventory is **not** a kill.
Live production and original/prepared input hashes/inventories must stay stable;
only the runner's own isolated source/build copies are removed.

The executable targets .NET10 and compiles C#7.3. That alone is not Unity API
qualification. The coupled pure closure can additionally use:

```sh
python3 tests/ball/compile_profile.py \
  --reference /path/to/Unity/Editor/Data/NetStandard/ref/2.1.0/netstandard.dll
```

This only compiles against actual .NET Standard2.1 APIs, without starting an
Editor/native/IL2CPP/WebGL/browser process. No complete M1/C1/C2/normal-camera,
sound-function RNG/SPFX, target libc/scheduler or all-row/full-match claim is made.
