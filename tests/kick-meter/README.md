# Explicit portable-authored human kick meters

This is an **independent opt-in interpretation**, not recovery of the missing
native `image_info_t` data. The default still rejects manual human FG/punt.
`source-compatibility=portable` does **not** enable kick meters.
`source-fire=portable` does not enable them either. The combined M1 factory
keeps its existing argument prefix, followed by `fireEffects` and
`kickMeterMetrics`; existing kick callers use the named metric argument.

For the existing C1, old-nine C2 or M1 resources, explicitly append:

```text
source-kick-meters=portable-authored
```

For example, combine it with the existing M1 selector and, independently, its
consumer-index option:

```text
?sourceprofile=original-teams-contact281-c2-m1&source-compatibility=portable&source-kick-meters=portable-authored
```

No resource, animation, sound/callback payload or profile identity is replaced.
Graybox, historical117, unknown modes and duplicate option keys fail explicitly.
Native generated dimensions remain unknown; these particular dimensions are
**not represented as separately user-approved native values**.

## Data and state

`SourceInterfaceData.CreatePortableAuthoredKickMeterMetrics()` reads the actual
`k_bar.Width`, `k_marker.Width` and `p_meter.Height` from the existing strict,
SHA-pinned authored HUD generation `258d5f4361d7da852b1446e774c5aad7d389ed268daabf3d4971c68a29fd8435`.
Its authored dimensions happen to be151/27/209; there is no second guessed
native metric table, asset or replacement bar. Changed generations cannot
silently supply a metric provider.

The immutable `ISourceKickMeterState` snapshot extension exposes the explicit
mode/provenance, process state and last actual source-unit launch. It is
implemented explicitly, leaving the historical public snapshot serialization
unchanged. The actual original HUD composer consumes this extension; it draws
the existing authored meter/ball/marker/wind/result images and glyphs, original
side positions and integer offsets, and suppresses turbo bars while the
original `showkmtr` lifetime is active. Authored-anchor and ordinary canvas/UV
clipping remain the existing portable HUD interpretation, **not native
Composit clipping or generated anchors**.

## Original behavior and real callers

Pinned source is `historicalsource/nflblitz2000`,
revision `89f840d58d152a2a9f230cb8d0cdffbd554c8ded`, tree
`a7e0352125d46075b7d98e87253ab89b9160a4c7`:

- `plyrinfo.c:678-1034`: active7.5 velocity base and actual selected team
  `TA_SPTEAMS`, source-wake advancement, directional FG clamp versus **unclamped
  punt overshoot**, signed wind, integer sprite offsets, exact accuracy/power
  arithmetic, fake/timeout and original30-source-tick result lifetime.
- `nfl.c:1155-1206`, `player.c:1595-1712`: per-play startup, strict ten-tick
  special-teams grace, fresh A/B (not C or a held button), real hiking flag,
  center animation and its later original HikeBall callback. The meter
  continuation observes that flag on its next pre-player source wake.
- `player.c:1733-1783`, `5734-5771`, `8078-8349`, `plyrseq.c:1325-1398`:
  actual snapped-ball catch, punt `ANI_CODE` after `RUNTOFRAME4`, previous
  player camera-joint cache/current camera conversion, original release flags,
  integer/binary32 velocity, mirrored X and original target/flight timer.
  **FG releases immediately in `_mode_kicker` within source8 of a kickable
  holder. It is not a fabricated later foot-contact animation callback.**

The existing lineup initialization/draw sequence is retained. The new process
first polls on the next executed source wake, before players; host skips and
repaints do not advance it. Startup/qsort/platform RNG remain host source-model
qualifications, not original machine scheduler/ABI evidence.

## Executable proof

Use existing read-only inputs, with byte-identical ignored worktree-local
prepared copies for the existing strict fixture-root guards:

```sh
python3 -B tests/kick-meter/run.py \
  --source-root "$PINNED_ORIGINAL_ROOT" \
  --interface-root "$PINNED_AUTHORED_INTERFACE_ROOT" \
  --fixture-manifest "$CONTACT272_FIXTURE" \
  --sound-functions "$UNCHANGED_SAFN" \
  --old-c2-inputs "$OLD_NINE_288_FILES" \
  --c2-m1-inputs "$M1_288_FILES" \
  --hud "$PINNED_EXISTING_INTERFACE_JSON" \
  --output .squad/.scratch/portable-kick-run
```

The runner compiles actual production and existing cabinet/HUD code, never
Unity stubs. SDK assets are restored from an empty offline feed **only after**
the selected no-restore compile reports them missing. Original source,
authored data, native reference code/executables, numeric observations, mutated
copies and transcripts remain ignored/local. Mutation source copies are
removed; baseline/final, source, harness and all consumed input hashes must
match.

The inventory is explicit:

| Proof | Cases |
| --- | ---: |
| Unchanged original-C meter functions as resumable continuations |648 |
| Unchanged original-C launch math |656 (432 punt,224 FG) |
| Actual cabinet page/stick/B selection → player → meter → physical launch |12: both kinds, both sides, C1/old-nine/M1 |
| Actual grace, hold-vs-edge, combined buttons, fake/auto, restart and paired non-kick defaults |39 |
| Changed/unsupported data/profile, two-human ownership and wind HUD/DPR |15 |
| Retained disabled native-metric boundary |817 assertions across the original12 caller cases plus Legacy select/snap |
| Compiled production causal controls |17 |

The actual raw launches are also shadowed by the original C meter and compared
bit-for-bit with original C velocity from the same observed camera-cache/ball
inputs. FG first-eligible wake, punt frame4, same-wake ball integration,
source-unit conversion, HUD coordinates and retained/expired state are named
assertions. Initial match boundaries/camera/raw99 or declared wind inputs are
honest component fixtures, **not newly qualified full public matches or M1
recordings**. No synthetic actor, injected kick, seed search or state rescue is
used to make a launch succeed.

`oracle.py` verifies every consumed original blob, compiles the complete active
meter functions and velocity routines, and records exact original byte windows.
Only observational probes are added before original sleep calls. Host
`ucontext`, opaque drawing/text/flasher handles and explicit authored image
metadata are declared seams; no original ABI, Goose scheduler, libc RNG,
Composit/Texus or decoded audio implementation is fabricated.

Run the asset-free source/inventory/producer guards separately:

```sh
python3 -B tests/kick-meter/test_runner.py
python3 -B tests/test-runner.py
```

## Boundaries

Original/native generated metric parity remains blocked. Original Glide color
combiner equivalence (`KickMeter.Highlighted`), timeout flasher pixels and
meter-bounce cursor audio/DSP are not newly qualified by this portable HUD.
Their exact typed state is available for the presentation owner; no invented
shader tint, click sound or particle effect is substituted.

Native Editor/import/build/browser, all16 full-game rows and full M1/R_SKIP
qualification remain separate. Actual archive-reference C#7.3/.NETStandard2.1
compilation is API compatibility evidence, not a playable/native build.

The [combined runtime suite](../fire-kick-integration/README.md) retains this
suite's exact458,125 new+817 disabled assertions and17 controls. It separately
executes all four fire/kick combinations and the stock frozen full M1 gate
with both features disabled, not a new all-feature public replay.
