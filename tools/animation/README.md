# One original WAN pose, offline

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

The reader rejects malformed lengths, missing/duplicate/unknown records,
inconsistent or excessive counts, unsupported flags/references, non-finite values,
reflections, and non-orthonormal rotations. Unknown ancillary chunks are bounded
and skipped, as in the original tool. Only the selected sequence's record payload
is interpreted; this is not validation of every animation in the file.

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
