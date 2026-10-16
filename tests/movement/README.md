# Source movement/evasion helpers

This suite owns only new `SourceMovement*.cs`, `SourceEvasion*.cs`, and
`tests/movement/**`. Runtime-data preparation lives in `tools/movement/**`.
Neither path wires Core, changes legacy simulation, starts
Unity, builds WebGL, creates metadata, or qualifies browser/integrated gameplay.

## Asset-free CI (no Unity Editor)

From the repository root, with Python 3.10+, .NET SDK 10, its
`NETStandard.Library.Ref` 2.1 reference pack, and `gcc` or `clang`:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 tests/movement/run.py --fetch-inputs --sdk-netstandard21
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover \
  -s tests/movement -p 'test_publication.py' -q
```

This is an **explicit SDK-only API validation mode, NOT Unity validated**.
It compiles the actual production sources with C# 7.3 against the SDK
`netstandard2.1` target, records the actual reference-pack assembly paths/hashes,
then runs the same actual-C functional corpus and all sixteen implementation
mutants. It does not find, install, or launch an Editor and never silently
falls back between SDK and Unity references. SDK restore is limited to the
normal SDK project/reference-pack requirements.

SDK receipts: `.artifacts/result-sdk-netstandard21.json`,
`.artifacts/sdk-netstandard21-compile.json` and
`.artifacts/SourceMovement.sdk-netstandard21.dll`. Their profile flags explicitly
say `unity_reference_validated=false`. Both functional and API compilation
share the recursive **complete current Contracts** inventory through
`MovementSources.props`; there is no fixed five-file list.

## Actual Unity-reference API validation

With an installed Unity editor's **actual** reference assembly, retain:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 tests/movement/run.py --fetch-inputs \
  --unity-reference "$UNITY_EDITOR_DATA/NetStandard/ref/2.1.0/netstandard.dll"
```

This mode validates Unity's reference **APIs only**, not Editor/native/browser
execution. Passing an SDK pack to `--unity-reference` is rejected. A profile
argument is mandatory, and requesting both profiles is rejected.

`--fetch-inputs` first reuses byte-verified local caches. Only missing, bounded,
public Git blobs pinned in `pins.json` are fetched through `gh api`. No upstream
makefiles/scripts are executed. A mismatched cache is never trusted or overwritten.
Generated original C, converted source tables, fixtures, binaries, logs and receipts
remain in ignored `.artifacts/`; all process scratch paths remain inside this project.
Omit `--fetch-inputs` for an already prepared offline run.

The runner:

1. Hash-checks every original byte stream against its Git blob pin.
2. Extracts and compiles unchanged original C functions/blocks, not C# formulas
   reimplemented in Python.
3. Obtains expected state, ordered script/control/cue requests, callback-time
   shared counts, RNG sites/bounds/raw draws, defense assignments, and leap
   results by executing that original C.
4. Compiles the actual production helpers and actual complete P1/Contracts
   dependencies with C# 7.3, warnings-as-errors, against the **explicitly selected**
   SDK or Unity API profile. SDK success is never labeled Unity validation.
5. Compares actual production C# to the C outputs, including binary32 bits,
   source input/timer/action state, source-frame gates and callback ordering.
6. Compiles isolated copies containing realistic production implementation
   mutations; requires an offending state/action/numeric assertion, not merely
   compilation failure. Rechecks the baseline and live helper hashes, then
   removes isolated workspaces.

`result.json`/`result-sdk-netstandard21.json`, the profile compilation receipts,
`cases.json`, and `source-movement-data.json` are durable ignored evidence. The latter contains
explicit original constants, selector/sequence tables, friction factors and
the twelve handicap speed/delay rows; it is **not** redistributable public data
or an authorized Resources import.

## Runtime-safe data preparation

```sh
PYTHONDONTWRITEBYTECODE=1 python3 tools/movement/export.py --fetch-inputs
```

The independent minimal producer exports
`tools/movement/.artifacts/source-movement-runtime.json` and its manifest using
only three pinned source blobs. Schema `original-source-movement-runtime/v1`
contains **only** `data.Constants`, `Tables`, `Friction`, and `Defense`.
Both 12×6 defense speed tables, exact integer delay fields, selector repetitions
and all binary32 bits are preserved. **No `Responses`, first-header stub,
animation fixtures, or runtime loader are present.**

The full M1 runner gates equality between that runtime-safe export and all four
actual-C data fields. See `tools/movement/README.md` for trusted content pins,
typed bit-decoding/`SourceMovementData` construction and the separate Runtime/UI
resource-binding handoff. The old test-only data export must never be packaged
as a production animation implementation.

## Production interfaces

All interfaces are in `Blitz.Gameplay`. Positions/velocities are **centered
original source units / source units per source tick**, not yards or seconds.
Heading 0 points along +Z; X uses `isin`, Z uses `icos`; headings wrap at 1024.
Use P1's real `SourceDirectionMath`, `ISourceRandom` and `SourceRandomContext`.
There is no .NET/libc/BSD PRNG fallback.

* `SourceMovement.BeginTick`: original input history, backpedal persistence,
  timer expiry, PF_TURBO/PF_OLD_TURBO, and exact `0 < releasedTicks < 9`
  turbo/A/B double-press handling. `HumanControls` gives the source
  kickoff/onside/intended pass-or-lateral drone-control eligibility. Caller/P1
  still supplies the **actual acquired switches**, including fake-drone behavior.
* `SourceMovement.StationTurbo`: run once for the corresponding active
  `plyrinfo_proc` station iteration, **not once per actor**. Fire/infinite turbo
  freezes the resource, rather than fabricating a refill. Defense recharge has
  two separately clamped binary32 additions.
* `Turn`, `ModeAllowsRun`, `StandardRun`, `QuarterbackRun`, `GeneralRun`:
  actual turn direction/tie choices, role/table indexing, restricted pre-snap
  motion, carrier/human/backpedal speeds, overrides, offscreen/kickoff/chase/
  powerup/drag branches, source scripts and proportional 4-tick upper /
  2-tick lower QB transitions. Supply `SourceMovementData` with actual converted
  constants/selector tables. No source arrays or invented tuning are embedded.
* `SourceMovement.AssignDefenseSpeeds`: consume the actual selected six-column
  `dline_speed_ht`/`dback_speed_ht` rows plus the selected
  `def_acceltime_ht` base/range. It returns seven source-index-ordered assignments
  and consumes fourteen explicit draws. Keep the original zero-delay quirk:
  stored zero does **not** subsequently expire into PF_SPEED_OVERRIDE.
* `SourceEvasion.WeaponBeforeRun`, `WeaponAfterRun`, `QuarterbackAfterRun`,
  `DefensiveBackJump`, `MaybeDive`, `SlowerSpin`: actual source evasion,
  stiff-arm/push/jump/dive script eligibility and side counts. `ReturnFromMode`
  explicitly preserves a successful weapon control-change early return.
* `SourceMovement.Integrate`: original safety clamps, in-play correction
  (including SPIN's exception), route abort, position-before-friction ordering,
  gravity/landing, and explicit selected field-friction factor. Puppet actors
  skip position/gravity but still receive friction.
* `SourceEvasionLeap.ResolveVictim` / `LeapAttack`: actual ANI_LEAPATT
  fallback/alias behavior, camera-joint transformation, truncated AA_TOGROUND
  time, four facing iterations, planar limits and the original **second**
  target-Y prediction. The minimum-speed return intentionally bypasses the Y
  cap. Negative-discriminant roots retain the source one-tick choice. Return
  values include the flight tick count and consumed 36-byte argument span.

The literal static roster `at_spd` is **not read** by the pinned run functions.
The corpus varies that original C field without inventing a modern player-speed
multiplier. Actual handicap state/rows and fire state are explicit caller inputs;
their C2 state machines are not implemented here.

## Required Core boundary and ordering

`ISourceMovementBoundary.Apply` is mandatory for every reachable animation,
control-change, route-abort or cue request. It must:

* Execute the real corresponding source animation/control operation immediately
  to its frame break, then return an updated `SourceMovementState`.
* Apply `BeginTwoPart` to the actual pose/sequence/headings and modes. A no-op
  is rejected; no generic clip or static-pose fallback is supplied.
* Copy `TeamSpinCount`/`TeamHurdleCount` into shared side state **before**
  executing scripts. The third spin predicate must see the incremented count.
* Return the exact additional RNG call count. Animation/audio/control calls must
  consume the same explicit ordered provider/context; caller advances
  `FirstCallIndex` by `RandomCalls`. A missing provider fails visibly.

Core must perform its original snap/catch/possession/mode-transition/early-return
stages before calling these seams. `ModeAllowsRun` is the animation/control-block
predicate **inside** a reached source mode, not a replacement for the whole mode.
For WPN: turn, Core catch/carrier checks, `WeaponBeforeRun`, capture eligibility,
`StandardRun`, then `WeaponAfterRun` with that **captured** eligibility.
Do not recompute eligibility after `standard_run` changes animation modes.
For QB/DB, preserve their distinct upper/lower eligibility and original ordering.
Respect `ReturnFromMode` for remaining mode stages, but still execute the outer
player animation/physics iteration.

Call `MaybeDive` only at the actual script callback windows (seven initial
one-tick callback opportunities in the hurdle family), not on every airborne
tick. Existing Core animation programs must execute the complete selected
hurdle/spin/dive/slide scripts, anchors, root drift, friction and callbacks.
Pass their post-animation state to `Integrate`; convert X/Z to public yards
exactly once. Core owns PF_ANITICK clearing, actual process/actor order, and
the existing 60-to-57 adapter.

`TeamIndexedTurbo` deliberately means original `pdata_blocks[ppdata->team]`,
**not** necessarily the controlling station in a four-player game.
`Neighbors` must be all fourteen original cached distxz/bearing values in actor
order; backpedal uses the explicit cached p2p heading. The leap input requires
the actual chosen target's cached **camera-space** joint, twelve-element
`cambot.xform`, camera position, target velocity and actual script operands.
A world-joint/render position is not interchangeable. Mark
`TargetIsExecutingActor` when the resolved victim aliases the attacker; the
last Y prediction then reads its updated velocity, as the original pointer does.

## Evidence boundary

The oracle compiles original `standard_run`, `qb_run`, `general_run`,
`turn_toward_tgt`, `unique_random`, `maybe_dive`, `slower_spin`,
`set_defense_speeds`, ANI_LEAPATT and original trig/angle functions. It also
extracts original player input/timer/physics and QB/WPN/DB action/eligibility
blocks, plus the station turbo block.

Animation is narrowly bounded by original `change_anim/1/2` and
`begin_twopart_mode` with a source-first-header `_animate_block` fixture.
This is **not** a complete original animation VM/clip/root oracle. Consecutive
airborne fixtures use real script launch operands and preceding C physics
outputs, not a claimed complete animated trajectory.

Original contact target selection, spear-head/attack branches, collision
consequences, fire/handicap state machines, complete audio RNG ordering,
platform random()/rand() bodies, equal-level Goose scheduling, packaging,
Core wiring, clip availability, and native/browser qualification remain their
owners' work. The original `_mode_db` A/loose-dead-ball branch can index
`player_blocks[-1]`; the pure helper rejects that undefined branch explicitly
instead of inventing a carrier. Passing this suite does not close integrated
M1 or any of the sixteen full-game matrix rows.
