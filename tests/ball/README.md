# Actual-source ball and input/cue caller regressions

These package-free C# 7.3/.NET 10 checks compile the **actual** Contracts,
Gameplay and pure AssetImport sources. They use the shared real factory in
`../parity/FullKernelInputs.cs`, never a geometry interface substitute, copied
physics implementation, generic clip fallback, or invented roster/formation.

## Clean CI / explicit original inputs

Requirements: selected .NET SDK, Python 3, GCC/Clang, and (only for optional
download) authenticated `gh` with public Git blob/tree read access. No Unity
Editor, native game executable, server or browser is launched.

From the repository root:

```sh
# Optional acquisition. No archive clone and no implicit network fallback.
python3 tests/parity/fetch_inputs.py --download \
  --output .squad/.scratch/SourceData/source-kernel-original

# Or supply your own exact original root, including complete pinned tree.json.
python3 tests/parity/prepare_inputs.py \
  --source-root .squad/.scratch/SourceData/source-kernel-original \
  --output .squad/.scratch/SourceData/source-kernel

python3 tests/parity/test_inputs.py
python3 tests/ball/test_runner.py
python3 tests/ball/run.py \
  --source-root .squad/.scratch/SourceData/source-kernel-original \
  --fixture-manifest .squad/.scratch/SourceData/source-kernel/fixture.json
```

See `../parity/README.md` for the companion original-C matrix/full-kernel host
adapter command. `--cache ROOT` and `--blob-file canonical/path=verified-file`
can provide read-only original inputs without download; bytes are still checked
against their exact Git blob pins. An existing isolated bad input is never
overwritten. The roster producer reconstructs the original Git tree hashes
before accepting its twenty-two source files.

The preparation orchestrator invokes the **permanent** animation, playbook and
roster producers. It also uses the permanent first-pose WAN reader and copies
only the two exact model/skeleton headers needed by the pure geometry consumer.
The canonical 117-clip/10,288-selected-frame manifest pin is
`f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274`;
historical `18af…`/`2baec…` manifests are **not aliases**. Complete animation
data, original matrices, catalog tables, headers, generated C, native binaries
and numeric cases remain ignored. No machine-specific source path is a default.

All output goes below this checkout's ignored `.squad/.scratch/SourceData/` or
`tests/ball/.artifacts/`. Existing prepared fixtures require explicit `--reuse`,
which verifies their bytes read-only. Missing/mismatched inputs fail rather than
silently replacing the library, geometry, roster or playbook.

## Evidence inventory

The default command requires these exact nonempty assertion inventories:

| Layer | Assertions / cases | Boundary |
| --- | --- | --- |
| Ball/catch numeric comparison | **470,666** assertions on **46,096** native-C cases | Catch decision prefix, complete deflection, fumble/bobble decisions and launch, ball-process physics prefix plus original LOS-crossing block |
| Input/camera/cue callers | **18,002** assertions, including **800** original-C `plyr_have_ball` and **12,544** original-C receiver cases | Real source raw input drain/transform, actual VM cue callers, scoped internal predicate/VM setups and public raw-B snap→center wrist→QB catch→selected throw/wrist flow |
| Original-pose geometry/lifecycle/control | **32** assertions | Fifteen original matrix-vs-quaternion pivot results; genuine wrist-only strict `<5` reach beyond pelvis `<14`; actual throw frame; PlayerDown/wobble ordering; focus/restart/raw49; direct actual catch/carry caller ownership and credit at ticks 0/17/18 |
| PAT/kickoff sentinel | **36** assertions plus one native original touchdown-assignment witness | Actual `Resolve`/free-PAT states retain `Down=-1` through `SourceAiFrame`; no clamp; existing normal 0..4 constructor range and invalid limits remain protected |

The native generator reads only exact pinned original blobs at
`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`. It extracts original active statements,
function windows and handicap tables **at runtime**, recording byte offsets,
line ranges and SHA-256. The authored `.c` files here contain host projections,
prescribed inputs and service observations—not upstream gameplay bodies or
original data tables. The archive's invalid `#ifdef 0 //DEBUG` diagnostic span
is omitted with its exact original span hash recorded; active statements are
never rewritten. This is explicit release-profile preprocessing, not a source
repair or an invented reference formula.

The C harness supplies projected host structures, host `sqrtf`, explicit RNG
draw vectors and symbolic external-service handles. Sound/effect callbacks are
outside the native ball prefix comparison. Real VM tests separately inspect
actual cue identity, signed arguments, shared draw context, singleton
`randrng(1)`, noncarrier shakers, detached batches and PlayerDown→ground→shaker
ordering. A PlayerDown cue is an observation token, never an instruction to
replay gameplay/effect lookup.

Original receiver equal-key **target libc `qsort`** order is unresolved.
Prescribed native cases compare ordinary selections; distinct selected-key ties
must fail explicitly in production, not pick an arbitrary actor. The test
records/protects this boundary instead of asserting host libc is the arcade.
Generator randomness is only deterministic input enumeration, not a claim about
the historical `random()`/`rand()` implementation.

Internal predicate, VM and catch/carry setups are clearly scoped test seams.
The raw-B flow separately uses public commands without state mutation. Neither
layer proves complete original match, scheduler, M1, C1/C2, presentation,
native-MIPS or browser parity.

## Real-source mutations and API profile

The default runner rejects **20 isolated actual-production mutants**: the
original 16 Core gates plus sentinel/range and immediate-control/credit gates.
Each mutated copy must compile, run and fail its named offending-field
assertion. Compilation failures, crashes, unrelated exceptions, skipped/empty
discovery, altered inventories and status-only green do not count. Baseline and
final baseline must pass, while all actual production, prepared input and
original-C input hashes/inventories remain unchanged. Only the runner's own
isolated source/build trees are removed; receipts/logs remain ignored.

For an author-only focused check, use explicit
`--section sentinel --no-mutations` (or `native`, `caller`, `geometry`).
That is reported as **not** the complete CI proof.

The executable's .NET 10 success alone does not certify Unity's API profile.
With an actual installed .NET Standard 2.1 reference:

```sh
python3 tests/ball/compile_profile.py \
  --reference /path/to/Unity/Editor/Data/NetStandard/ref/2.1.0/netstandard.dll
```

This directly compiles the complete actual pure closure with C# 7.3,
`-nostdlib+` and warnings-as-errors. It starts no Editor and is not an
IL2CPP/WebGL/native build. Optional `--expected-reference-sha256` pins the exact
reference bytes. Parent owns CI configuration and publication.
