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

The retained math campaign deliberately supplies `idiot_boxes_shown=2`,
`off_turnover=1`, and `GS_GAME_MODE`; it still aborts if message services are
reached. Its managed boundary now names those same declared fixture values
explicitly. It is not the possession-service qualification below. Initial-QB
instructional boxes remain excluded. Attract/debug/unselected SPECIAL_MODES are
outside this in-game increment. The `csi_resume` table and replay path are under `#if0`;
active playback is false. No dormant replay/slow motion is enabled.

## Active possession messages and portable process continuation

`GameSimulation.Possession.cs` supplies the bounded portable process projection
used by the **same** playback owner and both production camera callers.
`cambot.c:2886-2970` selects interception8, turnover2/13 or punt-return15 from
the actual raw predicates, including bounced lateral, extra flags, play-over
failed conversion and the real `nfl.c:1662/1715` turnover-on-downs producer.
Result-box teardown clears that latch; it is not an unexplained camera constant.

Suspension preserves the signed-short high bit and sleeping low bits, excludes
the actual caller, resumes the exact `0xfffffffe` HUD pair and post-player ID,
and queues a sleep1 message rather than running it inline. Camera preparation
occurs before sleep40; integration, FG work and pending X/Z shaker draws resume
from that saved continuation, not from another normal-camera loop. During the
gap the existing output and its real producer tick are retained explicitly.
An ordinary sleep1 or missing frame cannot authorize stale-camera retention.

Message entry preserves the original paired objects/IDs and two child flashers,
four white/color cycles with delay4, interception speech ordering, sleep100,
global object-ID deletion, texture-group release event and process death.
Punt return has **no sprites or invented overlay**. A killed parent does not
automatically delete its objects; a later same-ID message cleanup removes both
generations. Earlier level-zero game/clock/status slots resume next dispatch;
later players/cursors/post-player/detect/ball slots can resume on the current
camera wake. `LastWakeTick`, not the final suspension bit, governs HUD status
animation. Existing FX retain their own nodes and receive the same controls;
their order/RNG and existing menu behavior are not replaced by a global scheduler.

The separate `possession-services` runner executes **40 original-C scenarios /
4,760 declared dispatches**, **50 managed cases /2,240 assertions**, and
**21 zero-diagnostic compiled semantic controls**, followed by an identical
final baseline. It covers missing/dead messages, actual342-wake timeout,
creation order, both sleep boundaries, overlapping orphan cleanup, actual Core
actor/ball/clock/FX freezing, allowed continuations, pending shaker order,
same-wake physical input, menus/new plays/new matches, and sound-data failstop.
All47 original cases and17 controls are retained. The original Core cases now
bind the real cabinet selector; three additional cases bind the real cabinet
buffer/bridge/selector/Core/camera/HUD with FX on/off and exercise strict
source ordering and suspended menu admission. Input observation continues on
every source dispatch independently of frozen game/actor/clock/FX processes.
It cannot consume menu sleep20 or replay transient frozen button edges.
The source-tick guard is not weakened; four new compiled mutants protect
observation, unexplained gaps, menu admission and sleep20.
The retained math campaign still has all100 sequences/34,084 wakes and26
compiled controls; its extra asset-free process-mask case does not replace any
historical camera case.

The C side injects the authenticated selected branch/functions into ignored
output: `cambot.c`, `plyrinfo.c`, `Game.c:flash_obj_proc`,
`lib/select/select.c:flash_obj_white`, `sounds.c:intercepted_ball_sp`,
and the actual Goose suspend/resume/exists bodies. A declared `ucontext`
continuation host supplies sleep/head insertion and low-address logical sprite
handles; only the address of static `tout` is exposed as read-only telemetry.
Fixed host-slot adaptors are not original actor physics, MIPS pointers/stacks,
qsort ties, hardware pcount, native texture cache, or a native global scheduler.

Reproduce with explicit authorized inputs (new ignored output each time):

```sh
python3 -B tools/interface/prepare_possession.py \
  --source-root "$AUTHORIZED_ORIGINAL_ROOT" \
  --sound-header "$AUTHORIZED_MIDWAY_ROOT/include/goose/sound.h" \
  --output .squad/.scratch/possession-speech
python3 -B tests/presentation/possession-services/run.py \
  --fixture "$CONTACT272_FIXTURE/fixture.json" \
  --sound "$CONTACT272_FIXTURE/audio-functions/sound-functions.safn" \
  --m1 "$CANONICAL_M1_288" --impact "$M1_IMPACT_JSON" \
  --normal "$ORIGINAL_NORMAL_CAMERA_JSON" \
  --speech .squad/.scratch/possession-speech/possession-speech.bytes \
  --hud "$PINNED_HUD/interface.json" \
  --fire "$PINNED_V3_FIRE" --fire-sha256 "$V3_FIRE_MANIFEST_SHA256" \
  --authority "$EXPLICIT_FLAT_SERVICE_AUTHORITY" \
  --output .squad/.scratch/possession-service-proof --controls
```

The authority's exact names/hashes are in `possession-services/oracle.py`.
NFL files retain their names; map `lib/select/select.c` to `select.c`, Goose
`lib/goose/{process,mthread}.c` to their basenames, and
`include/goose/sound.h` to `sound.h`. No fallback/private-directory discovery is
used. Existing full fixture, sound, M1, HUD and v3 FX inputs remain required.

Interception speech uses Core's existing RNG and selected announcer bank.
Its independently pinned40-byte operand file is
`ac9bdf7c37c8f3d9d1e7184ca96e2d9c28aad2e157940d05194513b8f06da5df`.
The earlier SAFN/C2 selector does not cover this function; the new narrow
immutable submission preserves the actual playlist/volume/pan/priority and
source process, **not PCM/DSP playback**. The table is exported from originals
into ignored data, never copied into a tracked numeric table.

The existing HUD atlas contains complete original WIF images, **not**
Composit-generated `_c1` rectangles. The producer retains both logical objects
and independent flashers. The shared consumer draws their complete authored
union once while both component states agree; unequal states fail visibly.
It does not duplicate the base image, guess256-pixel component cuts, or claim
native `flash.h`/TEXUS/Glide packing. White/chroma output derives from the same
authenticated pixels. Native generated component geometry/pixels remain open.
Unity additionally needs the exported operand TextAsset at
`Resources/BlitzSourceInterface/original-interface-possession-speech.bytes`;
the runtime fails explicitly if absent. No scenes/settings/imports or Editor
execution are established by API compilation.

Real public-cabinet WASM/WebGL gameplay, GPU uniforms/frustum, stable paused
1024×768 PNG brackets and continuing four-human controls are a separate
integration proof, not these synthetic branch fixtures. All sixteen full-game
rows, native/full-game/audio/PCM/DSP/performance acceptance remain **OPEN**.
