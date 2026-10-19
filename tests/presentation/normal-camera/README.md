# Active original CAMBOT normal camera

The explicit `original-teams-contact281-c2-m1` profile uses
`SourceNormalCameraModel` inside the existing **single**
`SourceCinematicPlayback` owner. Both Unity `PlayCamera` and portable
`PreviewCamera` call that owner with the same immutable pre-player Core
boundary. `NormalCameraModel` is unchanged and remains an explicitly historical
legacy/diagnostic approximation, not a source-profile fallback.

## Reproduce

Asset-free implementation checks and compiled/executed semantic controls:

```sh
python3 -B tests/presentation/normal-camera/test_ci.py
python3 -B tests/presentation/normal-camera/run.py \
  --output .squad/.scratch/raw-camera-checks --controls
```

With explicitly authorized original inputs, also execute the original C:

```sh
python3 -B tests/presentation/normal-camera/run.py \
  --source-root .squad/.scratch/SourceData/source-kernel-original \
  --output .squad/.scratch/raw-camera-original-checks --controls
```

Choose a new ignored output each time. The existing pinned source acquisition
union already contains the required headers, including `PlayStuf.h`; no
private-directory discovery or substitute source is used.

## What executes

The new oracle authenticates every input against the completely reconstructed
Git tree `a7e0352125d46075b7d98e87253ab89b9160a4c7` at archive revision
`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`. It compiles the unchanged active
`cambot_proc`, `TrackObjects`, `SetModeAngleTrgs`, normal/GOPHER, special
selector, field-goal dependency, zoom/spline/rail/P2P and integration routines.
An entry wrapper counts normal dispatches. Sleep1 emits one observation and
loads the next declared fixture; it is not a native scheduler implementation.

The retained **88 sequences / 31,332 source wakes** cover both possessions, raw modes/flags,
all eleven original opening kickoff ordinals, quick-kick equality versus extra
flags, onside/subsequent kickoffs, actual PRE_SNAP → PRE_KICKOFF observations,
QB/run/pass/offscreen endpoints, both raster DIPs, HH/pullback dimensions,
incomplete tracking, limits, all selected impact entries, natural/forced return,
GOPHER/resumed wakes, field-goal tracking/deflection and shared shaker requests.
Expected and observed sequences, exact request bounds/results, compiler flags,
source pins and maximum errors are retained privately. An additional **12 sequences /
2,752 wakes** exercise field-goal → field-goal and field-goal → normal new plays
on both possessions, continuously and with actual playback suspension/resumption
(319 → 520), plus interrupted tracking returns. The total is **100 sequences /
34,084 wakes**. Every dispatch clock, raw mode, tracked actor mask/count/mode,
target identity, RNG bound/result/order and feedback occurrence/value is compared
exactly, including inactive normal wakes. The declared scalar limits are
0.01 source units for positional/dynamic fields and 0.00004 for angular,
trigonometric and matrix fields. Measured host maxima were **0.000015258789
source units** and **0.0000007748604 matrix component** in this increment.

This is separate from the retained **15,242** historical special-camera ticks,
which still compare with zero measured error. Those older ticks are not
relabeled new normal coverage.

The actual initial `624f0e8` normal producer was compiled and executed against a
smaller declared original-C comparison before implementation: **1,434 / 1,440**
ticks differed at 0.005 source-unit tolerance. It used inferred targets, EMA and
actor fitting. This receipt remains historical; it is not a final candidate
receipt.

The current suite also compiles **26 independent production-source mutants**,
executes every one, and requires its named behavioral failure—not a compiler
error. Controls cover wrong raw roots/lead endpoints/modes, offscreen predicates,
missing state/velocity/buffers, EMA, HH, GOPHER/snap, omitted wakes, hidden normal
loops, return reseeding, kickoff requests/feedback/reversion, shaker persistence
and order, viewport substitution, immutable observation aliasing and the
kickoff camera/movement-mode distinction and preservation of unwrapped raw
actor facing. Real source actors can temporarily carry headings outside
0..1023; the new observation preserves that int and special modes consume it
before their original angle validation. The historical canonical-target API
retains its existing range guard.

Two lifecycle controls reintroduce the real premature new-play reset and the
normal-plan reset separately. Both must compile and fail the named shared
production playback assertion: field-goal clock/target retention and the
post-return zoom clock, respectively. The historical special test now distinguishes
the original zoom expiry increment from an extra active special wake. Asset-free cases use authored synthetic camera
parameters; the original-C cases above use authenticated private originals.
New-match coverage independently proves that a full reset still clears the
transient, target, dispatch clock, shaker, frame generation and normal state.

## Exact observation and process contracts

- `SourceCameraBoundary` detaches all fourteen canonical source roots, raw
  modes/flags/headings, previous ground/ball-target camera caches and ball
  position/velocity/origin/tx/tz/handler/receiver/flight data.
- Camera kickoff mode is PRE_SNAP until the real Core approach, then
  GM_PRE_KICKOFF. The existing actor movement adapter is **not** changed to mask
  a camera issue. Contact-oracle exports continue naming their actual movement
  context, not this separate observation.
- Core retains the original ground-center and ball-target-cursor observations
  once per source wake. They are not reconstructed from a last browser render.
  This is an explicit portable scheduling adaptation of the original draw cache.
- Feedback is restricted to one executing pre-player wake: original
  `BF_TGT_OFFSCRN`, opening `auto_hike_cntr` and field-goal `last_carrier`.
  No actor/ball integration, rules, AI or RNG implementation is redesigned.
- Normal state advances only on original normal dispatch/return. A transient
  does not run a second hidden normal loop. Shake mutates persistent camera
  focus/eye after mode integration.
- Mode-only return preserves the original CSI operands, `tick_cnt` and
  `pmodeobj`; only the mode and target-Y adjustment change. Normal plan/integrator
  state writes do not reset that lifetime. Field-goal static state clears later,
  at the original `was_playover == 1` boundary, after special dispatch. A zoom
  expiry increments its clock after `ModeRevert`; spline/rail/P2P return before
  that increment. Explicit new-match reset remains a separate full reset.
- The conditional normal sideline draw precedes X/Z shaker draws. Opening
  selection requests eleven; reachable FG selections retain their actual
  two/four/table-count bounds. All use Core's existing shared source RNG.
- DIP512×384/512×256, the inherited664-pixel optical projection (including
  low-DIP vertical scaling), and the actual browser content viewport are
  distinct. No SafeFrame, actor-envelope or HUD padding enters raw normal
  targeting.

## Original data and qualification

The immutable seven-table/25-entry impact catalog and old data pins do not
change. Generate the additional original camera data locally with
`tools/assets/prepare_normal_camera.py`; use `tools/source-player/prepare_camera.py`
to extend an existing prepared bundle without changing any inherited file.
For the approved556-file data this produces557 files, the same manifest schema
and all556 inherited pins unchanged. Unity's explicit M1 team import requires
`normal-camera-original.json` beside the original impact catalog and stores it
in that existing camera resource. Missing data stops; no EMA fallback exists.

Original C/art/numeric data, generated C harnesses, conversions, binaries,
recordings and PNGs remain ignored. Source availability is not a rights grant.

Host libm/float stores and `-ffp-contract=off` are declared adapters. Native
fast trig/FMA, global clock/qsort/ABI/BSS/scheduler and pixels are unqualified.
Undefined original domains (invalid receiver indices, zero FG X velocity,
degenerate spline/P2P directions or zero point-stream endpoint) are excluded,
not silently clamped.

The original initial-QB instructional-box globals and possession-message global
suspension services are not supplied by current Core. The C host aborts if those
native services are reached; these fixture traces do not qualify them or invent
stand-in UI/suspension. Attract/debug/unselected SPECIAL_MODES are outside this
in-game increment. The `csi_resume` table and replay path are under `#if0`;
active playback is false. No dormant replay/slow motion is enabled.

Real public-cabinet WASM/WebGL gameplay, GPU uniforms/frustum, stable paused
1024×768 PNG brackets and continuing four-human controls are a separate
integration proof, not these synthetic branch fixtures. All sixteen full-game
rows, native/full-game/audio/PCM/DSP/performance acceptance remain **OPEN**.
