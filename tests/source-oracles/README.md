# Original C source oracles

These are **native executions of pinned original C**, not a second implementation
of the new C# rules. The upstream source is an external input. No original C files,
WAN motion, generated translation units, executables, or numeric golden outputs
belong in the published test fixtures.

## Run

Requirements: Python 3, an installed GCC or Clang with binary32 `float` and 32-bit
`int`. No Python packages are required. The C# comparison additionally uses the
already selected .NET SDK; it does not run Unity.

From the repository root, either provide the exact original files listed in
[`pins.json`](pins.json), or optionally obtain only those public Git blobs:

```sh
python3 tests/source-oracles/fetch_inputs.py
# An existing cache may be supplied; incorrect bytes are rejected, never overwritten:
python3 tests/source-oracles/fetch_inputs.py --cache .squad/.scratch/reference/nflblitz2000
python3 tests/source-oracles/run.py --source-root tests/source-oracles/.artifacts/original --full-matrix
python3 tests/parity/run.py --cases tests/source-oracles/.artifacts/cases.json
# Also exercise the current source-profile host adapter:
python3 tests/parity/run.py --cases tests/source-oracles/.artifacts/cases.json --full-kernel
```

For separately supplied original files:

```sh
python3 tests/source-oracles/run.py --source-root path/to/verified/original/source --full-matrix
```

The input root must contain the original, case-sensitive relative paths.
`fetch_inputs.py` uses `gh api` and checks the Git blob hash **after** base64
decoding; it does not trust a raw-URL cache or clone the repository.
`run.py` has no network fallback: missing or mismatched inputs fail closed.
Omit `--full-matrix` for the smaller, still behavioral input enumeration.
For just the original-C checks:

```sh
python3 tests/source-oracles/test_oracle.py --source-root tests/source-oracles/.artifacts/original
```

## What executes

At `historicalsource/nflblitz2000@89f840d58d152a2a9f230cb8d0cdffbd554c8ded`:

* `result.c`: the unchanged function-pointer table, all thirty original functions,
  and their original declarations. Includes the twelve deliberately empty
  field-goal/punt entries. Release branches that intentionally loop forever on
  invalid ball/type combinations are rejected by the harness, not reinterpreted.
* `nfl.c`: the unchanged complete `game_clock_proc` for initialization and the
  entire unchanged `while` body for controlled-state wakes, plus its actual
  `time_table`. A sleep stub stops after one original wake.
* Paired clock traces resume that same original loop after the first `sleep(1)`.
  The original game-process fractional-discard statement runs while it is
  suspended. This exposes the crucial difference between published
  `game_info.game_time` and the clock process's local `gt`.
* The exact contiguous `nfl.c` spot-rounding → `FIELDX` → result-table dispatch →
  touchdown/conversion override or non-touchdown clamp block.
* The original `FIELDX`, `WORLDX`, timing and game-flag/result macros, and original
  `game_info_t`/`tdata_t` declarations, extracted from their verified headers.
* `nfl.c:cpu_do_onside`: the entire unchanged original decision function. Raw
  nonnegative C `rand()` results are explicitly prescribed and actual consumed
  draws/order recorded. `compute_score` returns supplied team totals; scoring
  aggregation and a platform RNG algorithm are not fabricated.
* `detect.c`: unchanged `point2line_d` and `line_int_check`, including the latter's
  actual `vxv`, `magv`, and `det` implementations from `lib/ani3d/pipe3D.c`.
  The platform square-root primitive is explicitly supplied by host `sqrtf`.

The generator copies **byte-identical selected slices**, without rewriting C
expressions. It records each symbol/window's byte offsets, line range, SHA-256,
and verified full-file Git blob hash. Wrappers only provide external state,
service observations, and an iteration stop. The permanent C files are our own
input/output and external-service harness, not upstream gameplay logic.

## Evidence and enumeration

All generated material is under the local ignored `.artifacts/` directory:

* `cases.json`: explicit inputs and expected outputs obtained from the compiled
  original, with source/config/compiler/layout/window identity.
* `result.json`: executed case and test-group counts plus the corpus SHA-256.
* `run/original-unit.c`, executable and compiler log: local reproducibility
  artifacts, **not redistribution fixtures**.
* `original-stderr.log`: preserves real original CMOS fallback diagnostics.

The bounded matrix covers all five play types, six causes, sixteen combinations
of the four classifier-relevant flag bits, both sides, seven ball types, seven
field-end boundary values, and forward/backward fumble positions where relevant.
It samples unrelated flag bits separately. Spotting uses binary32 neighbors at
goals, midfield, integer and near-zero boundaries in both directions. Clock inputs
cross adjustment success/error, pause/attract, half/overtime indices, long-play
thresholds, reload counters and minute boundaries. The onside matrix crosses
quarter, strict clock boundary, score deficit and raw-draw short circuits.
No implicit RNG sequence is used; prescribed raw draws and case order are fixed
in `cases.py`, and only values actually requested by the original are consumed.

Notable directly executed source behavior:

* Initial clock `0x00020000` becomes `0x00013b18`, not a conventional NFL timer.
* Default second, late-half, long-play and bad-adjustment reloads differ.
* `FIELDX` uses `(390f / 300f) * 6f` (binary32 bits `1090099609`);
  spotting multiplies by literal `7.8f` (bits `1090099610`). Coalescing these
  constants changes boundary results.
* A fumble can replace `play_end_pos` without recalculating `fpend`.
* Parallel disjoint collinear segments can pass the original `line_int_check`;
  the oracle deliberately does not substitute idealized geometry.

## Limits

This is **native original-source parity**, not original-MIPS-binary certification.
Compiler/version/flags, host pointer width, field offsets, float format and
configuration are in each corpus. FP contraction and fast math are disabled.
The original full game structs use the host ABI; the opaque `play_t` pointer and
projected `ball_obj.type` are not an arcade binary-layout claim.

The normal 57 Hz profile leaves `DEBUG`, `NODRONES`, `NOKICKOFF`, `NOCLOCK`,
`SHORT_QUARTERS`, `Q_GAME`, and `NO_FIRE` undefined. Audio and adjustment constants
are named boundary handles, not original sound-bank or CMOS-number emulation.
The actual original warning/query/sleep order is observed, not a Goose scheduler.

The original result switch, complete PAT/kick/period/CPU/RNG paths, locomotion,
contact and AI, original animation interpreter, real-clip matrix traces,
multiplayer/input, full source data/audio and complete browser matches still need
independent evidence. The 35 geometry cases have no production C# comparison seam
in this suite. Counts here never establish whole-game parity.
