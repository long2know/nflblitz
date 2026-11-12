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

## Nov11 public special-team caller sets

`PublicSpecialTeamsChecks.cs` adds two separately qualified, finite entry points
to the existing project. They do **not** replace the preceding legacy inventory
or run its458K campaign again:

```sh
dotnet build tests/kick-meter/KickMeterChecks.csproj --no-restore -c Release
dotnet tests/kick-meter/bin/Release/net10.0/KickMeterChecks.dll \
  "$CONTACT272_FIXTURE" "$UNCHANGED_SAFN" "$OLD_NINE_288_FILES" \
  "$M1_288_FILES" "$PINNED_EXISTING_INTERFACE_JSON" \
  "$NEW_RECEIPT" public-s2-final "$ORIGINAL_KICK_COMPONENTS"
# Use a different new receipt for the four full return continuations:
# ... "$NEW_RETURN_RECEIPT" public-s2-return-results "$ORIGINAL_KICK_COMPONENTS"
```

`ORIGINAL_KICK_COMPONENTS` is the executable produced by this suite's unchanged
`oracle.py`; preserve its provenance and complete pinned source/header inputs.
In particular, `include/handicap.h` must be actual blob
`45eef32b32721196e4575e59a0fd2ce01e0fbbdb`, not an invented missing-cache
replacement. Use the existing approved POSIX build wrapper when the checkout
filesystem cannot build; keep SDK/intermediate/output paths project-local
inside that namespace and export only compact evidence.

The Nov11 captures explicitly bind animation state `d8de8248…` **and**
original-sound programme `eb68dd56…`; older `b9`/`6b83` captures are not promoted.

| Separate set | Actual executing caller windows |
| --- | --- |
|33 cases /37,786 assertions /2,443 original-C requests | Four physical-menu fake punt/FG continuations through raw B/VM throws; eight same-team ABC/exact-UP onside/quick positives and negatives; thirteen real kick/block/recovery/return/miss/down/OOB/scoring callers; four real safety→quick40/onside30 restarts; four held-chord opening/halftime/first-OT input cases |
|Four continuations /18,477 assertions /1,224 original-C requests | The **same** FG-block/punt-return prefixes, both directions, continue through the ordinary-running wall, fresh raw A hurdle, actual carrier-OOB, exactly one `NewPossession`, and receiving-side `Continue`→normal play selection |

These are not37 independent full plays. The four continuations share prefixes
with the33-case set. Every actual meter wake and all thirteen initial-set kick
launches are compared with unchanged original C using the **actual production**
raw draws and their exact bounds/order. The protocol's unused padding is
explicitly checked as unconsumed. RNG is the declared unfiltered portable
library seeded1, **not** recovered target libc.

Three clean compiled controls disconnect actual onside input, FG first-touch
deflection, and punt-down contact; each fails its public caller. A separate
compiled control wrongly applies the running wall during `MODE_UNINT` and
fails all four full returns. Both sets have fresh identical baseline/final
DLLs, complete observations/events/RNG and native transcripts, unchanged
inputs, and fresh-host archive-member readback before namespace teardown.

Fixtures remain explicit: initial play/result/final-subtick boundaries,
controlled camera, a blocker positioned once at the real holder with
`PF_NOCOLL` body isolation, and declared punt coverage/late-ball positions.
Safety cases reuse the qualified declared loose-fumble/EZ geometry and then
execute the actual recovery, control18, OOB, result and restart. `Continue` is
an actual station-command seam, not a claim about a browser key binding.

Source quirks are retained, not repaired into NFL conventions:

- `player.c:1013–1110` prevents ordinary running past `FIELDW-4` unless
  `MODE_UNINT`/non-SPIN or the original powerup permits it. `_mode_wpn` tests
  the **player root**, not the held ball's wrist/drawing position. Real raw A
  hurdles provide the qualified legal exit; no clamp or flag is weakened.
- Grounding a punt does not automatically end it. The Home downed-EZ case
  actually produces `DownedPunt` with placement20; the Away field98 case yields2.
- Punt Z velocity is not universally mirrored. The tested Away sideline input
  is an explicit **inward/inbounds negative**, not Away-OOB qualification.
- The13-wake FG case legitimately hits the upright and scores; the separately
  defined11-wake manual attempts miss and actually place the opponent at40.

Durable scoped receipts are
`S2-qualified-callers-20261111-01.json` (`41a8c58e…`) and
`S2-qualified-complete-returns-20261111-01.json` (`36d152f2…`) under the owned
Nov11 `unity-s2-public-20261111-01` evidence root. Failed fixture expectations
remain archived. The original180-assertion block stream is still stream-only;
later durable cases are distinct captures, not a recovered/reminted archive.

The separate asset-free guard now checks the exact named kernel inventory and
the actual `twoStationRecordProfile` factory-to-constructor forwarding. Both
old expectations were stale because of **our earlier changes in this same
task**, not unrelated base failures: `454bb8` contains103 kernel files, while
the current closure adds only `SourceAnimationPrograms.OriginalSound.cs`;
the new named TwoStation factory also adds the constructor capability.
The check preserves every base path, admits that exact added path, and
requires the dedicated factory capability, normal-entry isolation, admission
guard and non-null forwarding rather than merely changing a count/string.

Strict native manual meters remain blocked by the exact original
Composit→generated-header producer, **not absence of source timing arithmetic**.
`img2d.mak` invokes
`c -newpack -quiet_batch_file -clipon -lfile plyrinfo.lod`; the complete
`ingame` pack consumes all four WIFF inputs. `MIDST OFF`, authored151/27/209
and texture dimensions do not prove the clipped runtime `image_info_t`.
The original producer/dependencies or a bound generated `plyrinfo.h` are still
required (Lead's scoped packet `7e16bf46…`). The default native guard is intact.
Neither set claims natural full-match positioning, untouched far-endline punt
touchback, target ABI/RNG, browser/native GPU, DSP or pixel equivalence.

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
