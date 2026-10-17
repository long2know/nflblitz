# Original WAN poses and selected-frame clips, offline

These tools are original Python-standard-library code, require Python 3.9+, and
operate only on explicit local input with an expected upstream **Git blob** pin.
Decoded original motion and source WANs stay in ignored `.squad/.scratch/`; only
tool code, metadata-only pins and original synthetic fixtures belong in publication.

## Reproduce the frozen 56-sequence library offline

`player_sequences.json` is the publishable metadata-only catalog for the verified
`edc6193` continuous-animation checkpoint: **56 sequences, 3,731 selected frames,
4,046 authoring frames across 11 WANs**. It preserves original case-sensitive
source paths, Git blob pins, runtime names, counts and selected boundaries.
It contains no animation values or decoded-file hashes tied to an old local path.
The source tree is `historicalsource/nflblitz2000` at
`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`; converter provenance remains the
`1792293aaaea00a919b59e8c05716978e85b4333` pin described below.

From the project root, set `SOURCE_ROOT` to your existing local pinned source tree
(the directory containing `anim/`). No ignored inventory, analysis helper,
network access, Git invocation, package installation or historical executable is
needed:

```sh
SOURCE_ROOT=/absolute/path/to/your/local/nflblitz2000
BUNDLE=.squad/.scratch/player-animation-edc6193

python3 tools/animation/prepare_player_sequences.py \
  --source-root "$SOURCE_ROOT" \
  --destination "$BUNDLE"

PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tools/animation/tests -q
```

The destination must be **new** and beneath ignored `.squad/.scratch/`. The batch
reads each required WAN once, caches its bytes, checks the Git blob identity and
decodes with the unchanged `full_clip` / `encode_clip` implementation. Actual
decoded names, counts and boundaries must match the catalog before any bundle
is created. Every source frame is validated by the existing reader, including
deselected frames. There is no resampling, new selection rule, inferred rate or
playback-policy change.

The bundle contains `clips/<runtime_name>.json` and `export-inventory.json`
(`original-wan-export-inventory/v1`). Manifest clip paths are relative to the
**bundle root**, with actual decoded-byte SHA-256, source blob/SHA-256, authoring
count and full selected-source-index map. Clip provenance retains both source
pins and uses the repository-relative WAN path for `local_input`; identical
inputs at different physical roots produce identical bytes and hashes. The old
single-clip CLI and its path-bearing output remain unchanged. Commit/path
linkage is caller-supplied; offline blob verification is not a check of checkout
HEAD.

An existing destination is never overwritten or repaired. Add `--reuse` only for
read-only verification of an **entire identical bundle**: the batch freshly
decodes the pinned source and compares every output byte, rejects missing,
changed or extra entries, and rejects symlinks and path escapes.

## Reproduce exactly the expanded B1/B2 117-sequence library

`ball_sequences.json` is an **explicit opt-in**, metadata-only superset of the
unchanged 56-sequence default: **117 sequences, 10,288 selected frames, 13,514
authoring frames across 17 WANs**. It is the exact sorted metadata projection of
Core's stable expanded inventory, SHA-256
`18af668aa0ff97985f91030f6c17d0552a9630fe25d40893dc5a7ac8381b7cdf`.
That inventory pin identifies the requirements, not decoded output bytes or a
final gameplay-code freeze. No ignored inventory is needed to run the recipe.

Use the **existing** `--catalog` option with the same explicit pinned local
source root. Omitting it still prepares only `player_sequences.json` (56 clips).
No decoder, selection, timing, runtime, importer or manifest-schema change is
needed:

```sh
SOURCE_ROOT=/absolute/path/to/your/local/nflblitz2000
BUNDLE=.squad/.scratch/player-animation-ball-recipe/bundle

python3 -B tools/animation/prepare_player_sequences.py \
  --source-root "$SOURCE_ROOT" \
  --catalog tools/animation/ball_sequences.json \
  --destination "$BUNDLE"

# Read-only verification of an already complete, identical bundle:
python3 -B tools/animation/prepare_player_sequences.py \
  --source-root "$SOURCE_ROOT" \
  --catalog tools/animation/ball_sequences.json \
  --destination "$BUNDLE" --reuse
```

Names, case-sensitive source paths, source pins and selected boundaries are
preserved exactly, including runtime aliases and sparsely selected sequences.
Full authoring-to-selected maps and motion come only from the unchanged reader,
not from new public fixtures. Relocating identical source WANs does not change
any emitted byte. Selecting another catalog, even with the same clips but
different metadata, cannot reuse this bundle's manifest.

Only canonical `provenance.local_input` and the fixed explanatory
`provenance.source_linkage` may differ from older machine-path-bearing Core
exports; all other decoded data must agree. Use the **newly printed**
`manifest_sha256`, never the Core inventory pin or old decoded hashes, for
native import. This bounded preparation is not all-source-animation or
whole-gameplay completion.

## Explicit movement-complete extension: 156 unchanged clips plus R_SKIP

`movement_complete_sequences.json` is a **new composite catalog**, not a change
to any selected catalog. It pins `movement_sequences.json` and its immutable
156-clip manifest, then adds one explicitly qualified sequence: **157 clips,
13,490 selected frames, 18,174 authoring frames**. All 156 base inventory entries
and decoded clip bytes are copied unchanged after fresh, read-only verification
by the existing selected producer. The old 56/117/156 manifests, resources and
catalogs are never overwritten, repaired or aliased.

### The actual R_SKIP issue is an omitted record, not selection

The pinned original `wan2seq.c` defines `A_SELECTED=0x4000` and
`A_COMPRESSED=0x1`. The original `anim/misc99.wan` blob
`256acb7ddb838627312193ae19db66abd18ac146` contains `r_skip`, chunk 13, with raw
flags **0x4000: selected, uncompressed**. All 70 authoring frames are selected.
However, the chunk contains only fourteen matrix records: **record 5, the
right-hand rotation, is absent**. The unchanged ordinary readers correctly
reject this incomplete record set. Changing selection bits cannot fix it.

There is a deterministic **source-converter** result, distinct from a claim of
complete authored R_SKIP motion. The original converter's global
`frames[frame][limb]` buffer is not cleared between sequences
(`wan2seq.c:72,314–555`). Its immediately preceding selected chunk is
`Huffrun2`, chunk 12: a complete, uncompressed 90-frame sequence. Thus the
original converter retains its already-written limb-5 quaternions at authoring
indices 0–69 when exporting R_SKIP. This is a named, byte-addressed last-writer
dependency, **not a guessed hand pose, a whole-clip alias, identity fill or a
claim that R_SKIP contains those matrices**. Earlier compressed chunks cannot
influence these frames because the complete uncompressed writer replaces them.
Compressed retained writers/targets are deliberately not qualified; the
converter can mutate quaternion signs during compressed output.

`read_wan_replay.py` implements only this explicit policy. It validates the
actual omitted record, the last preceding selected writer, its exact name,
flags, chunk/record bounds and every required same-index frame. It validates
the complete writer using the existing reader, including frames after the last
retained frame. There is no default fallback or generic unselected export.
`prepare_movement_complete.py` additionally verifies pinned original
`plyrseq.c`, `player.c`, `anim/MAKEFILE` and `anim/seattle.mak` bytes and active
source-reference ranges before preparing anything.

The new clip uses `original-wan-converter-replay-clip/v1`. Its
`record_layout` truthfully contains **16 actual records** (flags, root and
fourteen limbs), while `converter_replay` separately identifies the retained
record outside the target chunk. All original names, flags, maps, roots and
matrix-derived rotations are retained. The inventory must carry the same
explicit qualification. The production `SourceAnimationLibrary.Read` supports
this narrowly pinned format and exposes immutable `SourceAnimationClip.ConverterReplay`
provenance; public standalone `ReadClip` still refuses an unqualified replay.
Ordinary v1 libraries remain compatible, with selected/unsupported flags checked.
No `Contracts`, sampling, actor pose, gameplay or native importer API is changed.

This is normalized binary32-WAN matrix conversion plus qualified converter-state
replay, **not verification against a shipped SEQ, its compression/quantization
or the original executable**. The catalog makes no such claim.

### Offline preparation and timing

Supply a local pinned tree containing both the case-sensitive WAN paths and the
four code/build reference files above. The selected bundle must already be the
complete immutable movement156 bundle with manifest SHA-256
`5e8fc7c5f2011303fd7b1aea87e6f957b1acdf4cbb15a43fd82f978d4b944b68`.
No source retrieval, historical makefiles/scripts or Unity process is run:

```sh
SOURCE_ROOT=/absolute/path/to/local/pinned/nflblitz2000
SELECTED_BUNDLE=.squad/.scratch/source-movement-integration/bundle156
NEW_BUNDLE=.squad/.scratch/source-rskip-export/bundle157

python3 -B tools/animation/prepare_movement_complete.py \
  --source-root "$SOURCE_ROOT" --selected-bundle "$SELECTED_BUNDLE" \
  --catalog tools/animation/movement_complete_sequences.json \
  --destination "$NEW_BUNDLE"
```

The command prints the **new actual manifest hash and resource identity**
`SourceData/local-plyr266-movement157-<manifest-first-eight>`. Do not use the old
156 identity or silently replace an existing Unity library. Native asset
creation, GUID ownership and runtime resource binding are separate handoffs.
An existing destination requires `--reuse`, which freshly verifies every output
byte without changing it. Producer source hashes participate in the new manifest;
changing a producer requires a new bundle identity, not reuse of old output.
The original selected producers/readers are unchanged.

`source_playback_evidence` records the active `r_skip_anim -> r_skip_cyc`
program, not an invented universal playback policy. At the normal 57 Hz
profile, its steady cycle advances one exported frame per tick (57 FPS, 70
frames). Entry from `R_TRB_WB`/`RUN_WB` clears the mode before the four-tick
proportional transition; the other branch enters the cycle with one-shot
`MODE_ANCHORXZ`. Runtime code applies and clears that anchor. The exporter does
not zero root axes, resample, add a closing pose or replace the existing script.
The clip's general playback fields remain explicitly runtime-bound.

### Actual-data and independent checks

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tools/animation/tests -q

python3 -B tools/animation/tests/run_movement_complete_checks.py \
  --bundle "$NEW_BUNDLE" --selected-bundle "$SELECTED_BUNDLE" \
  --source-root "$SOURCE_ROOT" \
  --converter-source .squad/.scratch/reference/midwayvideo/tools/wan2seq/wan2seq.c \
  --model .squad/.scratch/reference/nflblitz2000/models/plyr266.h \
  --skeleton .squad/.scratch/reference/nflblitz2000/models/skeleton.h \
  --baseline56 .squad/.scratch/player-animation-edc6193-recipe \
  --baseline117 .squad/.scratch/player-animation-ball-recipe/bundle \
  --output .squad/.scratch/source-rskip-export/verification
```

The check output directory must be new. Python synthetic tests require no
original motion. The actual-data check requires the local inputs above, GCC and
the existing .NET SDK: actual production sources compile as **C# 7.3 /
netstandard2.1**, with a separate .NET 10 check executable and no Unity types.
It reads all three old libraries and the new library using the production
loader; compares every old frame, inventory entry and clip byte; and exercises
malformed flags/payloads and explicit qualification guards on actual emitted JSON.

An independent bounded C WAN parser replays the original pinned C
`mat2quat`/`adjust_quat` functions, extracted only into ignored local outputs,
and records every frame's actual last writer and raw matrix address. All 1,050
R_SKIP quaternions and all roots are checked against that path. Separately, an
independent C# raw-matrix hierarchy evaluates every actual model vertex at all
70 frames, three headings/scales, against the production
`SourceActorPoseSampler`/`SourceAnimationMatrices` path. That hierarchy does not
decode quaternions or reuse the Python exporter. Synthetic self-roundtrips are
not described as independent original-data evidence.

Four isolated production-source mutations (qualification agreement, qualified
record identity, raw flags and pose basis), plus two well-formed, repinned actual
artifact mutations (retained pose and root), must fail their named assertions,
not compilation. Isolated source/artifact copies are removed; original inputs
and production sources are checked unchanged. These checks are not an Editor
import, browser run, public gameplay replay or whole-M1 acceptance. All decoded
motion, raw source and oracle outputs remain ignored and local-only; no asset
redistribution rights are implied.

## Existing Unity importer arguments

The printed `manifest_sha256` is the actual bundle manifest pin. With the
existing local `local-plyr266` rig imported, pass the bundle root and portable
manifest to the existing native Editor importer:

```sh
MANIFEST_SHA256="$(python3 -c 'import hashlib, pathlib, sys; print(hashlib.sha256(pathlib.Path(sys.argv[1]).read_bytes()).hexdigest())' "$BUNDLE/export-inventory.json")"
UNITY_EDITOR=/absolute/path/to/Unity/Editor/Unity

"$UNITY_EDITOR" -batchmode -nographics -quit -projectPath "$PWD" \
  -executeMethod Blitz.Editor.SourceAnimationImporter.ImportFromCommandLine \
  -animationAssetRoot "$PWD/$BUNDLE" \
  -animationManifest export-inventory.json \
  -animationManifestSha256 "$MANIFEST_SHA256" \
  -animationStableId local-plyr266
```

The importer is immutable: an existing animation library with a different
manifest pin is refused, not replaced. Run this against the intended clean
import target. Preparing the bundle alone does not launch Unity or alter any
project asset, runtime source, timing, matrix convention or source selection.

## Complete selected-frame clip

`read_wan_clip.py` exports **every selected frame of one sequence**, in the
original converter's selected export order, as compact deterministic JSON. It
does not download files, execute historical tools, synthesize keyframes, pick a
gameplay action, or guess a playback rate.

```sh
python3 tools/animation/read_wan_clip.py \
  .squad/.scratch/reference/nflblitz2000/anim/Qb.wan \
  --sequence QBSTANCE \
  --source-repository historicalsource/nflblitz2000 \
  --source-commit 89f840d58d152a2a9f230cb8d0cdffbd554c8ded \
  --source-path anim/Qb.wan \
  --expected-blob a5bed4bc6699a88281e888660e31e349d4009d3f \
  --output .squad/.scratch/original-clips/QBSTANCE.json

PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tools/animation/tests -q
```

The API is `full_clip(data, sequence, provenance)` followed by `encode_clip(clip)`.
Required provenance fields are `repository` (`owner/name`), `commit` (lowercase
40-digit SHA-1), `path` (relative repository path), and `git_blob_sha1` (lowercase
40-digit **blob**, not file, SHA-1). Commit/path linkage remains the caller's
responsibility: verify that metadata against the pinned source tree; the reader
verifies content against the blob pin offline. Optional caller metadata is
retained. CLI output must be explicitly specified beneath ignored scratch, and
cannot overwrite the input, including a symlink or hardlink alias.

### `original-wan-clip/v1`

| Field | Contract |
|---|---|
| `provenance` | Source repository/commit/path/blob, actual byte length/SHA-256, verified content pin, original converter source pin. |
| `sequence` | Actual authoring/runtime names, flags, chunk bounds/number/reference, source/selected frame counts, `selected_source_frame_indices`. |
| `joints` | Exactly 15 `{name, fbplyr_skel_index, source_limb_id, parent_index}` entries, same mapping as the first-pose reader. |
| `frames` | Every selected frame, once, in export order. Each has `exported_frame_index`, `source_frame_index`, original `source_frame_flags`, `root_translation`, and `rotations_xyzw`. |
| `frames[].root_translation` | `authoring_xyz` and `scaled_runtime_xyz`, each three finite numbers. Scale is `root_translation.runtime_scale` at clip level (`0.12898`). |
| `frames[].rotations_xyzw` | Fifteen normalized four-number arrays, indexed by `joints`, not WXYZ or world rotations. |
| `record_layout` | Each record's ID, count, stride and record/data byte offsets. A source frame's address is `data_offset + source_frame_index * stride`. |
| `validation` | All source frames, including deselected frames, are checked. Matrix orthogonality/determinant, quaternion norm, continuity and validated-frame summaries. |
| `loop_boundary` | First/last source indices, last-to-first quaternion dots and root delta; diagnostics only, not permission to loop an action. |
| `playback` | Explicitly unresolved tick rate, FPS, frame advance, loop and anchor mode. A separate **source-backed action/script binding** is required. |

Deselected authoring frames are validated and participate in quaternion sign
continuity, but are never emitted as runtime frames. Runtime script frame numbers
address **exported indices**, not authoring indices. A partially selected sequence
may therefore have different source and runtime counts. Original flags are
preserved, not repurposed as invented release/contact events.

Conversion follows the original converter's matrix convention and sign
adjustments through preceding authoring frames. This gives normalized Python
double quaternions for the original binary32 matrices. It deliberately does **not**
reproduce lossy, lookup-table-based SEQ delta compression or claim bit identity
with historical C float output. Frame zero exactly matches `first_pose` for the
same input/sequence/provenance. At an interpolated sample, especially a loop seam
or a gap in selection, use quaternion sign equivalence and shortest-arc
interpolation; do not manufacture a closing keyframe.

### Playback is not the WAN frame count

Trace the active script in `plyrseq.c`, its selection in `player.c` / `detect.c`,
and `lib/ani3d/ANIM.C`, rather than assuming 30 or 60 FPS:

- `change_anim` immediately advances to the first frame break and sets
  `PF_ANITICK`; `player_proc` avoids a second animation update in that tick, then
  sleeps one process tick (`ANIM.C:325–360`; `player.c:1001–1006,1160–1161`).
- `_animate_block` decrements the hold counter once per animation update.
  `_next_frame` holds its new exported frame for the supplied tick count.
  `ANI_RUNTOEND,1` is consequently one exported frame per process tick
  (`ANIM.C:479–570,1621–1648`).
- `ANI_INTSTREAM` and `ANI_STREAMTOEND` have explicit frame/tick ratios,
  integer rounding, special frame-minus-one entry behavior and fractional-frame
  truncation. `AA_TOGROUND` derives duration from current height/vertical velocity
  and gravity; it is not a fixed FPS (`ANIM.C:766–836,1715–1823,2076–2128`).
- The ordinary release graphics configurations explicitly use 57 Hz, while
  debug configurations include other rates (`main.c:258–338,424–440,966–972`).
  Keep the selected source configuration explicit; camera rate alone is not
  proof of animation playback.
- `MODE_ANCHORXZ` / `MODE_ANCHORY` are **one-shot next-frame anchoring bits**,
  cleared when applied. They do not mean "discard root translation forever."
  Secondary animations ignore translation. World drift, facing, model scale,
  physics and anchoring are runtime responsibilities
  (`include/ani3d.h:408–422`; `ANIM.C:578–625,961–1003`).

`QBSTANCE`, for example, has a verified looping `ANI_RUNTOEND,1` script. A throw,
catch, tackle or getup may instead skip entry frames, interpolate into a specific
exported frame, execute several differently timed segments, or stop before the
last selected frame. The exporter still preserves the **complete** selected
clip. Do not loop these actions or apply one idle/rate to all roles.

## Backward-compatible one-pose reader

`read_wan_pose.py` is an original Python-standard-library reader for **one
first-exported pose** in the observed Williams `WIFF` / `3AN ` format. Python 3.9+
is required. It is not an animation player, an archive-wide converter, or a
standing-state detector. Supply a sequence whose gameplay meaning has been traced.
There is no network retrieval and no execution of historical tools.

## Local use

Pass an explicit local file and the upstream **Git blob** SHA-1, not a plain-file
SHA-1. Commit/path metadata comes from the caller's pinned tree; the reader checks
the actual bytes against the blob pin offline. Output is confined to the existing
ignored `.squad/.scratch/` area. Do not commit or publish that output or source WANs.

```sh
python3 tools/animation/read_wan_pose.py \
  .squad/.scratch/reference/nflblitz2000/anim/Qb.wan \
  --sequence QBSTANCE \
  --source-repository historicalsource/nflblitz2000 \
  --source-commit 89f840d58d152a2a9f230cb8d0cdffbd554c8ded \
  --source-path anim/Qb.wan \
  --expected-blob a5bed4bc6699a88281e888660e31e349d4009d3f

PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tools/animation/tests -v
```

The JSON retains the source frame index, first-exported frame index, sequence
name, chunk number/bounds, record/frame byte offsets, file hashes, all 15 named
rotations, original matrices, original evidence offsets, and root translation
before/after the original `0.12898` scale. Output has no timestamps and is
deterministic for identical inputs/metadata.

## Format evidence

The schema is established by the **original tool source**, not float-pattern
guessing: `historicalsource/midwayvideo`, commit
`1792293aaaea00a919b59e8c05716978e85b4333`,
`tools/wan2seq/wan2seq.c`, Git blob
`33c7e489d9b7a40d3b0cd435236db8c357d143fe`.

- Lines 29–53, 207–277: 8-byte big-endian WIFF header; 16-byte big-endian
  chunk header. Chunk size includes number/reference but excludes tag/size.
  Chunks end on even byte boundaries.
- Lines 314–555: `3AN ` body is **little-endian**, using 32-bit longs/floats.
  Its header has a 20-byte name, flags, limb count and ten spare longs.
  Records are signed ID, frame count, then data: `-1` frame flags, `-2` XYZ root
  translations, IDs `1..15` twelve floats per limb/frame (three offsets and a
  row-major 3×3 rotation). ID `0` terminates without a count.
- Selection is `0x4000` on both sequence and frames. `0x1` requests compression
  in the *generated SEQ*, not compressed matrices in this WAN. The first selected
  pose is emitted uncompressed even by the original converter. No compression
  lookup table is needed for this task.
- Lines 585–631 establish normalized **XYZW** quaternions. Hemisphere continuity
  is preserved through preceding deselected authoring frames.

The multiframe reader reuses the unchanged pose reader's bounded container,
record and rotation helpers. It validates all root translations, limb offsets
and matrices throughout the requested sequence, including after the last
selected frame. Other sequences' payloads are not decoded. The pose reader's
CLI, schema, pretty-printed encoding and first-frame results remain unchanged.

The reader rejects malformed lengths, missing/duplicate/unknown records,
inconsistent or excessive counts, unsupported flags/references, non-finite values,
reflections, and non-orthonormal rotations. Unknown ancillary chunks are bounded
and skipped, as in the original tool. Only the selected sequence's record payload
is interpreted; this is not validation of every animation in the file.

## Synthetic regression and mutation evidence

The tests build multiframe WANs independently in memory, with shuffled record
order, nonzero ignored offsets, sparse selection, varying roots and identity /
90° / 180° / hemisphere-crossing rotations. They check first/middle/last selected
frames, every output rotation against its source bytes, exact exported ordering,
root scaling, names/parents, full provenance, legacy first-frame equality,
deterministic CLI artifacts and sign-correct interpolation inputs.

Negative coverage includes malformed container/record boundaries, duplicate
sequences/records, invalid or inconsistent counts, references, unsupported
flags/limb counts, invalid names, corrupt content pins, and non-finite/improper
rotations in **deselected frames after the last exported frame**. Output escape
and input aliases are rejected. Four isolated in-memory mutations of the actual
clip decoder (dropped middle frame, reversed frame order, broken hemisphere
adjustment, wrong root scale) must fail the same artifact assertions. Production
files and original cached inputs are never mutated by the tests.

Batch tests independently construct multiple selected sequences in one WAN plus
a second WAN and a long authoring-name/runtime-alias case. They inspect explicit
catalog selection versus the unchanged default, counts, names, sparse maps, computed content pins,
canonical provenance, one-read caching, relocated-root byte equality and
new-only/read-only-reuse behavior. Corrupt, missing, extra and symlinked emitted
artifacts must fail reuse without being repaired. Catalog symlinks/traversal and
incorrect source-path case fail visibly. Both public catalogs' entire metadata
projections are fingerprinted, not just their aggregate frame totals. Mutations
of the actual expanded metadata and generated synthetic bundle must fail the
same catalog/path/source-pin/selection/output-identity/hash assertions.

`tests/CheckPreparedLibrary.cs` is an opt-in native check entry point for a
C# 7.3 check project linked to the **actual production** `AssetImport` and
`Contracts` sources, with no Unity dependency. Compile those production sources
against .NET Standard 2.1 and reference them from the runnable check project;
do not substitute Unity API stubs or a decoder copy. Its three legacy arguments
are bundle root, baseline inventory file, and baseline inventory root; that
form still requires exactly 56 clips / 3,731 selected / 4,046 authoring frames.
An optional **fourth argument**, the explicit catalog path, checks its baseline
inventory pin, emitted manifest/catalog identity and every catalog entry, and
uses that catalog's exact totals (117 / 10,288 / 13,514 for `ball_sequences.json`).
It invokes the real `SourceAnimationLibrary.Read` and compares all clips' runtime source revisions,
original source SHA-256/blob/path identities, selected maps and every selected
frame value against the local baseline. With the fourth argument it also checks
the actual contract's selected-ordinal mapping at first/middle/last frames.
No original motion is embedded in the checker; this actual-data check is separate
from the synthetic Python suite.

## Space and gameplay meaning

In `historicalsource/nflblitz2000` at the pinned commit above:

- `anim/MAKEFILE:51–57` establishes the translation scale.
- `models/skeleton.h:1–78,180–197` establishes the 15-node `fbplyr_skel` and
  pelvis-first `model266_limbs` mapping.
- `lib/ani3d/pipe3D.c:113–200,753–767` consumes XYZW rotations, composed
  recursively about the skeleton's model-reference pivots. These are **not**
  already-world-space joint orientations or conventional parent-relative pivot
  translations. The WAN's three per-limb offsets are preserved only as evidence;
  the original converter explicitly skips them.
- `plyrseq.c:1074–1084` selects `SEQ_QBSTANCE` in `qb_stance_anim`.
  `player.c:4397–4416` uses it for a stationary, neutral-stick quarterback.
  This is a standing QB ball-holding stance, **not a verified pre-snap stance**
  and not a universal pose for all football roles.

No Unity handedness conversion, anchoring, model placement, pivot rewrite, or
renderer change occurs here. The runtime adapter must respect those original
semantics. Tests generate only original synthetic data; no source animation,
mesh, texture, or reference pivot coordinates are embedded in this tool/tests.
