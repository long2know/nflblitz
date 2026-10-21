# Synchronous original sound-function VM hook

For explicit current281 testing, append
`--input-profile contact272 --c2-inputs "$CANONICAL281_INPUTS"` to the runner:
the unchanged fixture supplies non-animation/legacy controls, while the frozen
288-file C2 output supplies the actual281 library/bootstrap. Positive kernels
use real `SourceC2Composition`, its own sealed geometry, genuine constructor
bank draws and old SAFN before wake; intentionally unbound negative controls
remain separately identified. `--output` requires a new ignored child.

The resumed current run passes142 short checks,800,735 full VM assertions /
15,912 unchanged original-C cases, all11 successfully compiled named controls,
and the final unchanged original-C baseline. The eleventh `post-host-selection`
control uniquely anchors the actual `ExecuteSourceSoundVm` method entry through
`RequireSourceSoundVmBinding();`, before both the new pre-snap-switch tuple
validation and the C2 dispatch branch. It no longer assumes that those two
statements are adjacent. Its current-source identity is recorded under
`defensive-presnap-switch/binding-required/v1`; the shared mutation writer and
asset-free guards reject a stale source, dropped control/edits, or a retarget to
the native-sound method. Deferral remains after mandatory binding and before
selection, with the same named process-offset assertion. Legacy six-tuple SAFN,
old C2 and new fixed-hop callback/payload contracts are not relabelled or relaxed.
The prior10/11 stale-anchor failure remains preserved, not
counted as a kill. This is not whole-game/DSP/native acceptance.

This is a pure Core regression suite, not an audio player or Unity/DSP test.
It requires explicit prepared **read-only** real-input paths. It never downloads
inputs, starts Unity, reads license files, modifies those fixtures, or substitutes
synthetic geometry/animation or another PRNG algorithm.

Prerequisites: the existing .NET SDK10, Python 3.10+, GCC, and `gh` only for
explicit original acquisition. From a clean checkout:

```sh
python3 -B tests/parity/fetch_inputs.py --download \
  --output .squad/.scratch/SourceData/sound-original
python3 -B tests/parity/prepare_inputs.py \
  --source-root .squad/.scratch/SourceData/sound-original \
  --output .squad/.scratch/SourceData/sound-kernel
```

The union preserves all prior65 inputs (including the original camera closure),
reuses the existing **four-pin** sound acquisition helper, and adds only
`Game.c`/`mainproc.c` for actual bank/pointer initialization provenance:
**69 pinned blobs**, not an archive download. Preparation invokes the unchanged
permanent sound producer; its payload, audit manifest and original C include
are pinned and ignored beside the real animation/geometry inputs (**443 files**).
An explicit read-only cache may replace `--download`; nothing falls back to an
ignored Primary path.

With `ACTUAL_MOVEMENT_FIXTURE` and `PINNED_ORIGINAL_ROOT` set to those explicit
paths, and `VERIFIED_SOUND_FUNCTION_DIRECTORY` to its `audio-functions/` child:

```sh
python3 -B tests/audio/vm/run.py \
  --fixture-manifest "$ACTUAL_MOVEMENT_FIXTURE/fixture.json" \
  --functions "$VERIFIED_SOUND_FUNCTION_DIRECTORY" \
  --source-root "$PINNED_ORIGINAL_ROOT" \
  --mutations
python3 -B tests/audio/vm/test_runner.py
```

The function directory must contain the frozen `sound-functions.safn`, audit
manifest, and unchanged `original-functions.inc`. The exact SAFN pin is
`4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280`.
The fixture is produced by the existing `tests/parity` acquisition/preparation
commands and contains the canonical117 and distinct complete157 libraries.
No machine-specific source path is a permanent default.

Optional actual Unity **reference-only** compilation:

```sh
python3 -B tests/audio/vm/run.py \
  --fixture-manifest "$ACTUAL_MOVEMENT_FIXTURE/fixture.json" \
  --functions "$VERIFIED_SOUND_FUNCTION_DIRECTORY" \
  --source-root "$PINNED_ORIGINAL_ROOT" \
  --unity-reference "$UNITY_EDITOR_DATA/NetStandard/ref/2.1.0/netstandard.dll"
```

The required SDK is the repository's existing .NET SDK10. New project restore
occurs only after a missing-assets failure and uses an empty local package feed.
No package references are added. Native C comparisons require the existing GCC.

## Bootstrap and transport

1. Construct the actual mandatory M1 Core with its real geometry/library,
   playbook/rosters, movement data/settings, and source camera producer.
2. Before the first executed source wake, call
   `simulation.BindSourceSoundVmData(SourceAudioFunctionData.Read(bytes, trustedPin))`.
3. After a host step, call `simulation.TakeSourceSoundVmDispatches()` to drain
   immutable already-resolved results once. No selection/RNG occurs during
   delivery. Pending calls survive an empty/skipped host step until drained.
4. A `Resolved` dispatch pairs its actual original cue/context with immutable
   calls and real draw trace, including legitimate zero-call offscreen results.
   An `Unsupported` dispatch explicitly retains unimplemented function/SPFX
   tuples with no result; it is never a resolved/muted call.
5. A `MissingInput` dispatch retains `PartialResult`, actual consumed offsets,
   and missing field, then rethrows `SourceAudioMissingInputException`. The
   source wake is faulted until explicit restart; no retry/reset/rollback.
   Restart retains immutable bound data and clears pending execution transport.

The six supported original VM tuples alone use the frozen selector. PlayerDown
is already-executed gameplay and is never selected/replayed by this hook.
Raw bank, playlist IDs, full volume/pan/priority words and subcall order are
transported without interpreting gain, voice priority, mixing, timing or DSP
semantics.

## Evidence

* Actual unmodified `t_dshoul_anim`, `h_front1_anim`, and `fall_04` execute all
  six sites through initial `Change` and later frame/ground transitions.
* Additional real-VM probes isolate mid-program selection, following random
  opcode, two calls, actual collision receiver contexts, next actor, and
  current offset versus cue instruction order. The pose library remains real.
* 15,912 original C function cases pass through the actual Core VM hook, not
  only direct selector calls. PF_ANITICK is present because the real VM sets
  it before ANI_CODE. The native routine bodies and tables are unchanged.
* Actual lazy state, partial-failure offsets, queue detachment, explicit
  unsupported functions, once-only delivery, restart and raw fields are tested.
* Isolated mutations must compile and fail named offending-field assertions.
  Compilation errors, generic crashes, status-only success and fallback
  playback do not qualify.

The C1-integrated C# VM inventory is **142 short assertions**, then **800,735
assertions / 15,912 actual-C cases**. All **11** source mutants must fail their
individual named contracts, followed by another exact800,735 baseline. The
runner rejects wrong counts, missing tuples,117/156 aliases, untrusted SAFN,
unrelated assertions, crashes and compile failures. It hashes all actual
prepared files, explicit overrides, original state/function sources and the
complete compiled production inventory before/after. Twelve asset-free guards
exercise those gates; they are not counted as original-C cases.

`SourceSoundVmSources.props` explicitly links the unchanged pure
`SourceAudioFunctions.cs` alongside actual Core/Contracts/AssetImport. Existing
coupled projects import the same dependency; the selector is never omitted or
replaced merely to compile.

The inserted routine draws necessarily alter later source gameplay RNG compared
with the earlier sound-omitting M1/B1 source versions. Their historical qualified
receipts and assertions are retained, not relabelled or weakened into a new
whole-match claim. Controlled M1 physics/VM and canonical numeric comparisons
remain separate layers from the new sound-executing stream.

The complete sound-aware M1 regression is now
[`../../movement-callers/`](../../movement-callers/): **36,948 caller assertions**,
**5,312 original-C physics comparisons**, and all eight caller mutants. Its
unfiltered public smoke uses one explicit raw vector shared by *all* sites,
not a taunt-specific response or the old preview-LCG window. Real R_SKIP
ordinals0→1 execute at source2780→2781; all171 prior14×15 cache-frame checks are
retained (172 frames observed). The former fixed-window failure remains recorded
as historical failed evidence, not reclassified as parity.

The standalone function layer is independently reproducible from the same
explicit originals and prepared bytes:

```sh
python3 -B tools/audio/fetch_function_inputs.py \
  --source-root "$PINNED_ORIGINAL_ROOT" \
  --destination tools/audio/.artifacts/source/nflblitz2000
python3 -B tests/audio/functions/run.py \
  --original "$VERIFIED_SOUND_FUNCTION_DIRECTORY" \
  --reproduce sound-repeat --mutate
```

Use a fresh reproduction name. This retains15,912 C cases,267,367 checks,
4,941 lazy-state checks and14 compiled/rejected function/data mutants; the33
synthetic checks remain a separately labelled layer.

Original platform PRNG/state sharing, complete C1/C2/scheduler, other sound
functions/SPFX, DSP allocation/gain/pan/priority/mixing/playback, native/browser
and whole-game parity remain open.
## Current Contact272 generation

Select `--input-profile contact272` with the new
`source-contact-kernel-real-inputs/v1` fixture for current C1 callers. The actual
library pin is then `9cae962b…ede9` (272 clips/24,892 frames), never an old157
fixture relabeled current. `--functions` still names the immutable real
SAFN/audit/original-include directory. Legacy CLI defaults retain their old
library identity for historical diagnostics.
The exact full15912 original-C cases/800735 current-C1 VM assertions, six
synchronous tuples, shared RNG/Who/FIFO/restart contracts and all11 compiled
named mutants remain required. The142-assertion short is not a substitute.
Preparation or a package/pose preflight cannot qualify these campaigns, speech,
direct/deferred sound producers, audible output, DSP or full gameplay.
