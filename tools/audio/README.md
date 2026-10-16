# Original audio preparation (local only)

This is a bounded **compressed-stream extraction**, not a DCS3/DSP/game-audio
engine. It prepares original CURSORS IDs **19/23/60/63/64/67/68** and the exact
34 PLAYERS candidate IDs reached by the currently supplied impact callbacks.
No invented audio, silence fallback, inferred tackle event, or sound RNG exists here.

## Reproduce

Requirements: Python 3, `gh`, and GNU **g++ 13.3.0, x86_64-linux-gnu**, C++17.
Other native compiler profiles fail explicitly rather than claim untested PCM
reproducibility. Exact optimization/FP flags and the compiler identity are recorded.
No Python packages or upstream build
scripts are used. The pure reader tests additionally use the installed .NET SDK;
that execution is **not** Unity validation.

Asset-free CI needs no banks, decoder dependency, or Unity installation:

```sh
python3 tests/audio/run.py --mutate
```

Actual Unity API compilation is a separate, explicit option, with no
machine-specific default or silent reference fallback:

```sh
python3 tests/audio/run.py --unity-editor-data "$UNITY_EDITOR_DATA"
```

The receipt says `not_requested` when no Unity reference path is supplied.
Providing a missing or invalid reference directory fails rather than downgrading
to an SDK-only result.

```sh
python3 tools/audio/prepare.py fetch
python3 tools/audio/prepare.py verify-inputs
python3 tools/audio/prepare.py extract --name original
python3 tests/audio/run.py \
  --original tools/audio/.artifacts/decoded/original/manifest.json \
  --reproduce original-repeat --mutate
```

Use a fresh extraction name; existing output directories are never overwritten.
All original inputs, external decoder source/binaries, PCM/WAV, metadata manifests,
binary packages, and test outputs live in ignored `.artifacts/` directories.
Neither originals nor derivatives may be committed, uploaded, or relicensed.
Input bytes are checked against exact Git blob pins **before** native execution;
stale source caches are rejected. The native helper is an internal, pinned-input
extractor, **not** a sandbox for arbitrary adversarial compressed streams.

`inputs.json` pins the original source commit/tree and individual blobs, Goose
sound-driver evidence, and the build-only `Asure/DCSExplorer` dependency at
`1b7d0126cabb79c4c1365a90542806a60c9436f8`. GitHub repository metadata declares
BSD-3-Clause; the decoder source attributes Michael J Roberts (2023). No external
implementation is vendored into tracked files. Preserve upstream notices if
distributing its source/binaries. This does not confer rights to original banks.
No license files, credentials, or upstream makefiles/scripts are read or executed.

## What the bytes establish

The pinned banks identify themselves as **DCS 3.0G**. Goose `snd_load_bank`
establishes the 128-byte build header, LE16 sparse slot count, LE32 data size,
LE32 relative playlist table, and `FFFFFFFF` holes. The data base is
`134 + 4 * slots`, not a guessed fixed offset or 18-byte table stride.

Only an exact program profile is decoded: prefix `01 01`, zero-wait `07`
mix on channel byte 0, zero-wait `01` stream with a BE24 address **relative
to its own field**, repeat 1, then a BE16 end wait and `00`. Compressed
streams contain a BE16 frame count, 16-byte header and packed DCS data.
All original IDs/program bytes/end waits are retained, including unprepared
and unsupported inventory. In particular, three CURSORS programs contain
`13`/`14` controls absent from the pinned native interpreter; they stay opaque.
PLAYERS also contains short control-only programs, not all entries are samples.

The native OS95 standalone profile emits mono signed LE PCM16 at **31250 Hz**,
240 samples per DCS frame plus **two explicit pipeline/tail frames**. The
playlist's signed mixing level and default master setting 255 are baked into
each isolated output. JSON records offsets, compressed consumed ranges/header
bytes, frame/sample counts, exact waits, extrema, nonzero counts and hashes.
Do **not** read these files as raw Unity linear call-volume/pan/priority values.

The pinned upstream `BnkExtract` entrypoint is independently built and compared
byte-for-byte for every prepared playlist. It shares the same decoder core:
this comparison is **not an independent DSP/audible oracle**. No original DSP3
execution, native Editor, audible playback, WebGL or browser acceptance is claimed.

## Immutable reader and limited Unity boundary

`SourceAudioBankReader.Read(bytes, frozenPackageSha256)` reads the strict `.saud`
binary companion to the audit JSON. It verifies version, size, original sparse
IDs/order, relative addresses, program profile, PCM count/hash, and absence of
trailing bytes. Its data and copied normalization buffers cannot mutate the bank.
Unprepared IDs throw; unsupported controls/loops are not approximated.

`SourceAudioIsolatedPlayer` leases an explicitly dedicated, empty `AudioSource`.
It previews only a qualified one-shot at neutral Unity settings with baked
mixing, rejecting overlap and unqualified end timing. Nonstandard waits remain
in the immutable data, **not** silently replaced. In particular, decoded PLAYERS
57 is available as data but is not a qualified whole-playlist preview.
This class never subscribes to `SourceCallback` or implements `snd_scall_bank`.
It is compiled against **actual Unity 6000.6.5f1 AudioModule/CoreModule and
netstandard2.1 references**, C#7.3, separately for editor/WebGL defines.
It has not been executed inside Unity. No `.meta`, scenes/settings or resources
are created here; the designated metadata owner must supply the two script metas.

`.saud` v1 is little-endian: magic `53 41 55 44 01 00 00 00`; byte-length ASCII
bank name; 20-byte original Git blob; 32-byte original SHA256; UInt16 original
slot count; UInt16 prepared count. Each entry is UInt16 ID, UInt32 program offset,
UInt16 program length, original program bytes, UInt32 stream offset, UInt16
compressed frame count, UInt32 PCM byte length, 32-byte PCM SHA256, PCM bytes.
All lengths/counts are validated before allocations. The JSON is the fuller
audit manifest, not a generic runtime JSON contract.

## Required synchronous Core seam (not implemented here)

Original `sounds.c:1501-1619` runs inside `ANI_CODE` actor execution:

* `get_hit_snd` returns immediately if `PF_ONSCREEN` is absent. If both actor
  and hit partner have `MODE_LINE`, it draws `randrng(5)` and emits an
  **additional** lineman sound; execution continues. Carrier/intended-receiver
  status then chooses carrier or non-carrier hit tables. For `(-1,-1,0)`,
  ordered draws are optional 5, then 5 (carrier) or 11, then 5.
* Fixed `get_hit_snd(57,0,0)` still needs visibility, actor/hit modes and
  carrier/intended-receiver state. The lineman branch can still draw RNG first.
* `grnd_hit_snd` has **no** onscreen guard. Actor/hit `station != -1`
  selects human-priority/volume settings. `(-1,0,-1)` draws bounds **3 then 11**;
  `(-1,0,0)` draws only 3. Ground table `[108,117,117]` and lineman table
  `[13,11,12,13,122]` retain duplicate probability weights.
* `ISDRONE` is precisely `station == -1` in pinned `include/player.h:61`.
  Needed synchronous values: signed arguments, original context/order, actor
  number/flags/mode/station, nullable hit partner with mode/station, current
  ball carrier, and current ball intended receiver. Use Core's **same shared
  original RNG in that process**, not render RNG or a new seeded stream.
* Emit already-resolved `{bank, playlistId, volumeByte, panByte,
  priorityWord, originalContext, subcallOrder}` records after selection. Preserve
  the original side-effect/RNG order even if DSP allocation rejects playback.
  Gameplay `PlayerDownCallback` is an observed ordering token, never sound/FX.

`snd_scall_direct` only clamps/transports volume/pan and the raw priority word.
This host code is not proof of DSP3 gain/panning/arbitration. Existing levels
are volume 160/205/235 and priorities 15/10/5/1 (not Unity's priority scale).
Binding post-Step callbacks to a selector would move RNG past actors and break
global ordering. Parent/Core owns that separately routed integration; decoded
PCM readiness is not caller parity or all-game completion.
