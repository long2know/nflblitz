# Original-source differential and synthetic contract checks

## Focused stream-frame component regression

```sh
dotnet run --project tests/parity/ParityChecks.csproj --configuration Release -- \
  --stream-component
# Optional: only an already admitted decoded PSDLINE1 clip with the pinned hash:
dotnet run --project tests/parity/ParityChecks.csproj --configuration Release -- \
  --stream-component path/to/existing/PSDLINE1.json
```

This selector uses actual `SourceAnimationLayerState.Change/Advance` and standalone
`ActorState`, never game/host construction, source wakes, original-C/native execution,
or an original-C corpus. Synthetic checks also run in the existing default runner,
reported separately from the unchanged host-adapter contract count.

The source authority is `historicalsource/nflblitz2000` revision
`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`, `lib/ani3d/ANIM.C` blob
`97d7856d67a432937e249be274ef67a9ad354dc1`: `_stream_frame` 766-1001,
especially its strict `fabs(t) < 0.05f` frame-data **and** root selection at
862-881, plus stream-entry adjustment at 1790-1823. The branch does not round
the accumulated frame or snap fractions near one.

Toy mathematical roots/rotations check below/at/above the threshold, ordinary
and near-one interpolation, subsequent progress, duplicate ticks, initial
negative frames, final frames/completion, compressed/uncompressed flags, and
primary/secondary anchor behavior. They assert VM roots/poses and public
sample metadata/poses with an absolute `1e-6` portable tolerance, not native proof.

The optional clip must retain decoded SHA256
`486f4fe78f13bc2a6c316df521ee6f46b2359e47e5e76ea08f90ed0f1378468e`
and source WAN blob `5376f528be8489f8e19bec0082fc0fd1ad954769`. It exercises
only ticks 0-24 of the actual `lm_set1_anim` first `INTSTREAM 50,70` segment.
The independent source-derived cursor reaches selected ordinal 17 / authoring
frame 34 at tick 24; expected roots/poses come directly from that authenticated
clip, never generated numeric goldens or a production sampler. No clip data is
stored in Git; this optional private-input proof is separate from synthetic
public test coverage and is not whole-VM/game/Unity/native acceptance.

## Original-source differential runner

```sh
python3 tests/source-oracles/run.py --source-root path/to/verified/original/source --full-matrix
python3 tests/parity/run.py --cases tests/source-oracles/.artifacts/cases.json
# Current source-profile kernel and adapter, still no Unity:
python3 tests/parity/run.py --cases tests/source-oracles/.artifacts/cases.json --full-kernel \
  --fixture-manifest .squad/.scratch/SourceData/source-kernel/fixture.json
```

This C# 7.3 project links the **actual production** contracts, complete pure
gameplay kernel, and pure `AssetImport/*.cs` dependency in both modes.
The complete source inventory keeps adapter dependencies linked as the source
profile expands; a hand-maintained subset can omit required production types.
`--full-kernel` selects the host-adapter **checks**, not
substitute types or a different production-source subset. It does not
reference an old test binary, redefine production types, load Unity, or modify
the existing core/asset/browser test projects.

Expected rule and clock values are the explicit outputs of the independently
compiled original C in `../source-oracles/`. The runner requires a matching
original-C execution receipt and corpus hash. Every case is consumed; unknown,
missing, empty or incomplete inventories fail, rather than yielding zero-test
green.

Comparisons inspect result numbers, side changes, exact float bits, raw source
macro outputs, the original spotting/dispatch/clamp stage, packed clock/local
state, play time, possession-time changes, warning/adjustment observations and
sleep/wake timing. Paired traces validate that discarding the published fraction
does not overwrite a suspended clock process's local `gt`.
`SourceMatchFlow.CpuOnside` is compared with the unchanged original decision
function, including raw C `rand()` consumption, short circuits and resumable
random-request kind/modulus/order. No underlying platform RNG sequence is assumed.
`Resolve`'s later score/down/result-switch outputs and the other
`SourceMatchFlow` behavior are **not** claimed by this first corpus.

`fixtures/synthetic-contract-traces.json` is our own tiny synthetic data, not
upstream WAN motion or an original-animation golden. Its selected authoring
indices `[7, 31, 80]` deliberately differ from array ordinals. Executed contract
checks cover DTO and actual library-clip detachment/read-only poses/mappings,
legacy and explicit event identities, 57 Hz tagging, masks, selected-frame loops, same-source-tick idempotence,
independent two-part headings and captured interrupted transitions. They do not
claim exact original matrix/render parity.

With `--full-kernel`, the explicit 60-host/57-source trace executes the actual
`GameSimulation.Step`. It checks all sixty host steps, skipped source wakes,
detached snapshots, ordered queued action edges and restart queue clearing.
The action-order case explicitly sets up a normal-play internal test seam:
it does **not** claim to execute the opening kick or bypass the source manual-snap
grace period in a real match. The no-action timing trace retains the real opening
kickoff pre-snap. Receipts explicitly report whether this suite ran.

Synthetic runtime clips use the compressed/selected source flags `0x4001`,
verified on the actual active stance exports, but contain only our own toy poses.
An initial uncompressed-zero-skip fixture was not representative of that profile;
the original uncompressed `_ani_skipframes`/`_next_frame` path can also enter a
negative frame before `RUNTOEND`. No production code was changed to mask this
fixture/profile distinction, and no binary animation parity is claimed.

After a passing baseline, the Python runner copies actual production sources to
an ignored isolated tree using a freshly checked, coherent file inventory, changes the **real C#** `RoundPlayEndPosition` divisor
from 78 to 79, compiles that tree, and requires the original-C numeric comparison
to turn red on `play_end_pos_bits`. It then reruns the unmodified baseline, records
before/after production SHA-256s and removes the isolated tree. No live source is
mutated while Unity may be importing.

Receipts and complete diagnostic logs live in local `.artifacts/`:
`result.json` is focused, while `result-full-kernel.json` preserves the full
adapter run independently. Builds use unique ignored output directories so that
another agent's focused run cannot replace a full-kernel binary or receipt.
The runner
reports the 35 original-C geometry-only cases separately; it does not call them
C# comparisons. .NET native compilation is not Unity/IL2CPP/WebGL certification.
No browser or Unity Editor is started by these commands.

## Required actual full-kernel factory inputs

The full-kernel mode now loads the **actual** canonical 117-clip library,
original fifteen-node rig, actual converted playbook and 31x16 roster service.
The same library instance is passed to `GameSimulation` and production
`SourceCatchGeometry`; absolute imported `+X,+Y,-Z` pivots remain in source units.
Actor model scales come from the selected actual roster records, never 1.0
fallback. Optional presentation settings stay explicitly unknown unless a test
supplies named settings. Standalone synthetic VM tests remain synthetic and
separate; their toy clip mapping is never fed to the real simulation geometry.

The fixture also contains separately pinned selected156 and complete157
movement libraries plus the strict runtime movement export. The short host
adapter retains canonical117; longer public ball and movement callers explicitly
select complete157. No library identity is aliased. The factory supplies actual
movement data, named settings and a controlled 512×384 source-DIP camera frame
inside the pre-player callback. This is not the production camera or rendering
proof; those contracts are exercised separately by `tests/presentation/movement-stage`.

Prepare once with explicit pinned original inputs:

```sh
# Optional pinned public blob/tree acquisition, not a repository clone:
python3 tests/parity/fetch_inputs.py --download \
  --output .squad/.scratch/SourceData/source-kernel-original
python3 tests/parity/prepare_inputs.py \
  --source-root .squad/.scratch/SourceData/source-kernel-original \
  --output .squad/.scratch/SourceData/source-kernel
python3 tests/parity/test_inputs.py
```

Alternatively supply your own complete exact source root (including the pinned
recursive `tree.json`). `fetch_inputs.py` offers explicit read-only cache and
individual verified-file overrides; wrong blob bytes cannot become fixtures.
Its69-blob union preserves all65 prior original-C camera/gameplay inputs, reuses
the existing four-pin sound helper, and adds only `Game.c`/`mainproc.c` for
actual sound bank/pointer initialization provenance.
Preparation invokes the permanent `tools/animation/prepare_player_sequences.py`
with `ball_sequences.json`, `tools/playbooks/convert.py`, and
`tools/rosters/export_rosters.py`, the separate movement156/complete157 recipes,
the runtime-only movement exporter, the unchanged `tools/audio/prepare_functions.py`
SAFN1 producer, and the existing original pose/header reader.
No ignored Primary-worktree helper, absolute machine path, copied stand-in,
source artwork in Git, or Unity import is needed. All generated inputs remain
ignored in this checkout. Existing fixtures require explicit read-only `--reuse`.

The prepared443-file inventory includes `audio-functions/sound-functions.safn`,
its exact audit manifest and unchanged original C include. The mandatory SAFN
identity is `4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280`.
`FullKernelInputs.Create` binds the real typed data before the first executed
source wake; a missing datum is not muted or replaced. The existing explicit
`--sound-functions` override remains supported with the same pin and
before/after byte guards. No waveform/player/DSP functionality is implied.
The separate117/156/157 animation identities and the332 host-adapter assertions
are unchanged. See [`../audio/vm/README.md`](../audio/vm/README.md) for actual
synchronous sound/original-C/mutation commands.

## Explicit Contact272 + Sound generation

`tests/parity/prepare_contact_inputs.py --profile contact272` prepares the new
`source-contact-kernel-real-inputs/v1` schema from explicit read-only inputs:
the frozen443-file Sound fixture (`8167196e…8876`), the **production**
Contact272 bundle (`9cae962b…ede9`), the separate115 reference
(`907e2d36…5fcd`) and the Contact272 impact catalog (`738d712b…f23a`).
The new fixture has834 pinned files; the original fixture bytes, all old data
entries and identities are retained. This is preparation, not regression acceptance.
See [`../contact/README.md`](../contact/README.md) for the full commands.

The original `FullKernelInputs.Load` signature prefix/defaults are unchanged for
legacy fixtures: canonical117, explicit movement157 and explicit selected156.
The new schema selects the actual typed production272 library for numeric,
adapter, ball, movement and Sound campaigns. Geometry is constructed with that
same instance and all15 imported pivots; SAFN is bound before any executing wake.
`legacyAnimationLibrary: true` names the old117/157 control explicitly.
`selectedMovementLibrary: true` still selects the known156 negative control,
never272 with rewritten counts. No union adapter masquerades as a resource.

All current-generation runners require explicit `--input-profile contact272`
alongside `--fixture-manifest`; forgetting that selection rejects the new schema
instead of using a hidden legacy/Primary fallback. The legacy default remains
available for historical diagnostics, not new-generation acceptance. Example:

```sh
python3 -B tests/parity/run.py --cases tests/source-oracles/.artifacts/cases.json \
  --full-kernel --input-profile contact272 \
  --fixture-manifest .squad/.scratch/SourceData/contact-kernel272/fixture.json
python3 -B -m unittest discover -s tests/parity -p 'test_*.py'
```

The new original-input acquisition profile preserves all69 pins exactly and adds
only the three actual C1 ANI operands and six required contact WAN files. It
checks every78 canonical path against the reconstructed complete tree and each
Git blob identity. Case changes, extras, omissions, changed clip bytes or selected
indices, and metadata-only fixtures fail. Guard tests are not gameplay evidence.

Full campaigns require a frozen production generation and complete original/input
maps before and after execution. Existing exact counts remain requirements until
a concrete source-causal delta is measured and documented; switching profile
does not authorize dropping cases, R_SKIP, public raw traces or accepting an
unsupported C2 producer as green. No Unity Editor/browser/device/DSP runs occur.

The canonical loaded animation manifest is pinned to
`f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274`,
not historical `18af…`/`2baec…` identities. C# readers inspect original metadata,
selected authoring maps, all clip hashes, geometry headers, roster and catalog
pins. The runner guards the complete real-input inventory before/after its
baseline, isolated rounding mutant and final baseline.

`--full-kernel` without `--fixture-manifest`, or a missing/wrong real input, is
an error—not a skipped adapter. All existing 60-host/57-source steps, skipped
ticks 1/21/41, retained snapshot, two ordered selections and restart assertions
remain executable. The bounded internal normal-play-selection seam is not
relabeled an opening kickoff/full-match proof.

See [`../ball/README.md`](../ball/README.md) for permanent actual B1/input/cue
caller regressions, original-C extraction and actual-source mutations.

For independently compiled DTO and pure match checks while the complete kernel
is being edited, use [`../contracts/README.md`](../contracts/README.md).
Compiling actual playbook/drone/ball dependencies here does not establish their
AI, catch, motion, or full-match behavior.
