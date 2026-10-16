# Original-source differential and synthetic contract checks

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
Preparation invokes the permanent `tools/animation/prepare_player_sequences.py`
with `ball_sequences.json`, `tools/playbooks/convert.py`, and
`tools/rosters/export_rosters.py`, plus the existing original pose/header reader.
No ignored Primary-worktree helper, absolute machine path, copied stand-in,
source artwork in Git, or Unity import is needed. All generated inputs remain
ignored in this checkout. Existing fixtures require explicit read-only `--reuse`.

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
