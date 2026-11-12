# Original audio preparation (local only)

The original `prepare.py`/SAUDv1 path below is a bounded **compressed-stream extraction**, not a DCS3/DSP/game-audio
engine. It prepares original CURSORS IDs **19/23/60/63/64/67/68** and the exact
34 PLAYERS candidate IDs reached by the currently supplied impact callbacks.
No invented audio, silence fallback, inferred tackle event, or sound RNG exists here.

## Separately qualified playlist and global-mixer preparation

`prepare_playlists.py` executes the pinned CPU playlist VM after exact original
BNK relative-address relocation. SAUDv2 retains raw programmes, real waits,
finite stops and byte-verified second/third infinite-loop cycles. It does not
relabel isolated one-shots or admit assigned unsupported opcodes as silence.

`prepare_mixer_playlists.py` extends the immutable49-bank generation with the
eight finite programmes needed by the complete original menu/known player
callers: CURSORS20/28/80, INTRO6/7/11/12 and PLAYERS192. The independent original
2.33 firmware/ISA reference establishes implicit-global13/14, update/transfer
and genuine00 stops. Source-requested `SND_OPSYS_0223` and the observed `0233`
runtime are distinct recorded facts, not renamed versions.

Original13/14 target **global stream0**; ordinary local stream0 operands follow
the programme's actual allocated channel. The helper executes both primary0
and rebased-primary1 contexts in the pinned CPU VM, lowering13/14 to their
source-equivalent global09/0B mixing operators. SAUDv3 preserves **both**
PCM variants and actual global/own mixer frame traces. They must not be
interchanged, silently discarded or treated as a timer-ID array.

The runtime combines live source-column contributions on their240-sample
frame clock, retaining fades/final snaps/00 or replacement resets; it rejects
the unqualified native16-bit overflow domain. The independent original
fixed1.15 transfer cases are separate from portable allocation, outer
gain/pan, master headroom and browser resampling. No whole-DSP/native PCM
equivalence follows from this narrower operator qualification.

All generations use create-new ignored outputs. The compiler/reference paths
must be the explicitly authenticated prepared roots; no ambient source,
firmware, bank, credential or license discovery is performed.

### AV306 assigned team programmes (November 10, 2026)

`prepare_mixer_playlists.py --team-speech` independently executes the assigned
original programmes0–3 in all31 team banks, in both primary0 and
rebased-primary1 contexts. These124 programmes are not sparse holes or aliases
for the previously prepared10–20 slots. The successor audio inventory is
`original-compiled-playlists/v4`,
`portable-dcs233-complete-source-mix-programs/v4`:49 banks,1205 programmes,
132 implicit-global programmes. Its banks retain the existing strict SAUDv3
codec and unchanged native mixer reference; no reader-level duck/stop
validation is relaxed.

The distinct `original-teams-complete-av306` data manifest is
`51e94caffcf5875e024206eb069dfd905ee13e134b09e7f5020ff68349461e5d`.
Its `audio/complete-av306-manifest.json` is
`c695b7c04befa93124e29b9454edb865d4fc8b1e981f2cf329485d7c0cbc01ee`.
All1114 ancestor payloads remain byte-identical. The new admission requires the
exact independently measured1221 unique PCM payloads/135773760 decoded bytes;
older64/128MiB admission limits are unchanged.

The focused production regression uses a declared fourth-period/score/carrier
placement fixture, then real guest input, `Capture`/`Advance`/`SourceStep`,
original out-of-bounds/result waits and the actual EOQ child. It never directly
calls an end-play, speech VM or sound producer. The raw original bank argument
remains `Cardinal`; only the existing original SDK lookup is case-insensitive.
The child is born at40, first wakes41, emits original `sounds.c:691` programme1
at46, reaches the same-record Host consumer and track5 with exact PCM SHA
`c24cbe7710c6b01ac8c91090a03624446178604bc75c5f3fda30d5d211428825`,
21120 samples, and completes at its exact finite boundary. Original
`TCNV(36.0f)` holds40 source ticks before WIN_THE_GAME142 at86. The real EOQ
camera and same-wake actor renderer are also consumed. Separately compiled
removals of the actual PCM consumer and the explicit306 EOQ consumer both fail
the production regression; rebuild the unchanged baseline after controls.

With an explicitly admitted **new** readonly dataset and installed SDK:

```sh
SOURCE_PLAYER_DOTNET="$DOTNET" SOURCE_PLAYER_DATA_ROOT="$DATA_ROOT" \
  bash tools/source-player/with-posix-build.sh bash -c '
    set -e
    bash tools/source-player/dotnet.sh build tools/source-player/SourcePlayer.Checks.csproj \
      --no-restore -v:q -p:UseSharedCompilation=false
    bash tools/source-player/dotnet.sh \
      tools/source-player/bin/SourcePlayer.Checks/Debug/net10.0/SourcePlayer.Checks.dll \
      "$SOURCE_PLAYER_DATA_ROOT" --original-period-audio-only
  '
```

This proves the named source-call/PCM/camera boundary, not a natural full match,
native DSP output, browser scheduling, pixel identity or statistics-to-career
acceptance. The original source tick and free-PAT exit helpers also preserve
the actual `Game.c:251`/`playsel.c:1518` operands; native wait ticks are gameplay
outputs, not invented instruction-effect receipts.

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
# Executed original playlist successor

`prepare_playlists.py` is an explicit, local-only SAUDv2 preparation path for
the complete-original303 composition. It authenticates every selected bank
against the pinned source receipt and the existing decoder dependencies before
compilation. `render_playlist.cpp` relocates original BNK relative stream
addresses into a bounded private memory image and executes the pinned CPU
playlist VM. Finite wait/stop timing is retained. Infinite loops require exact
second/third decoded cycle agreement; their verified sample range is stored.
This is not an original arcade DSP result.

V2 does not change `isolated_supported` or admit unknown controls into the old
v1 reader. It has a distinct package version, original program bytes and
rendered-frame/loop extents. Cross-channel controls, unknown DCS3 opcodes and
control-only programs remain explicitly reported, not skipped or replaced by
clicks/silence. The C# complete-profile factory freezes its manifest and all
bank/program inputs. Browser PCM uploads are lazy and use the existing real
WebAudio consumer, source output ordering and source-time clock.

The October30 user authorization permits this local original-resource
integration; it does not establish public redistribution permission. The old
extraction/profile bytes and qualification receipts stay unchanged.

## Original hut callbacks

The complete-original303 composition executes all seventeen `hut_snd` ANI_CODE
sites in pinned `plyrseq.c` (86–290 and 8470–8522). The instance-only animation
overlay preserves the original frame waits and branch targets; older animation
profiles are unchanged. `prepare_sound_bindings.py` authenticates the original
source blobs and includes these exact tuples and the two original sound tables.
The cinematic receiver admits the additional closed sites only with the exact
complete-original animation, callback and impact identities, not arbitrary
neighboring instructions.

Original `sounds.c:1653–1662` selects `hut_snds` for `-1`, `qb_point_snds` for
`-2`, and otherwise the explicit playlist. Each random branch consumes one draw
from the executing simulation stream; the explicit branch consumes none.
All branches use the actual current player bank, volume160, pan127 and
priority15 regardless of station/visibility. There is no separate pitch
operand. Missing bank state remains an explicit partial failure.

With an explicitly authorized prepared complete-original data directory:

```sh
bash tools/source-player/dotnet.sh build tools/source-player/SourcePlayer.Checks.csproj --no-restore
bash tools/source-player/dotnet.sh tools/source-player/bin/SourcePlayer.Checks/Debug/net10.0/SourcePlayer.Checks.dll \
  "$COMPLETE_ORIGINAL_DATA" --original-hut-sounds-only
```

The focused regression executes the actual scripts, checks all seventeen
callbacks, four raw-input tapes, exact initial callback ticks, shared RNG
identity/cursor, resolved raw calls, cinematic pairing, transport and nonzero
decoded PCM/play commands. It also checks missing-bank partial execution and
rejection of invented neighboring sites. This is directed portable execution,
not browser audition, native DSP equivalence or whole-game parity. Native
nonempty output still requires a real adapter; the isolated Unity preview is
not substituted for one.

## November 10 gameplay producers

The complete303 composition now executes original throw/lateral, catch,
bobble/bat-down/deflect, fumble and movement speech calls in their producing
processes, not an observer or a cue-only journal. Fumble speech precedes velocity
RNG. `catch_speech_proc` retains PID `0x0ccc`, first-wake `sleep(18)`,
current possession/fire/roster branches and the original optional `sleep(90)`.
Bat-down creates the separate `delay_deflect_proc`, with `sleep(30)`.
Both children require exact scheduler admission; unknown process names remain
unsupported. Historical profiles retain their previous policy.

```sh
bash tools/source-player/dotnet.sh build tools/source-player/SourcePlayer.Checks.csproj --no-restore
bash tools/source-player/dotnet.sh tools/source-player/bin/SourcePlayer.Checks/Debug/net10.0/SourcePlayer.Checks.dll \
  "$COMPLETE_ORIGINAL_DATA" --original-gameplay-sounds-only
```

This focused check uses actual guest/team entry, public cabinet input, source
wakes, throw release and catch producers, original decoded PCM/playback commands
and fourteen-actor render matrices. Ball/actor placement is an explicit fixture,
not a natural match. Compiled mutations removing catch-child admission and
restoring composite animation origins must fail. It does not close whole A1/V1,
native audio-device, DSP, pixel or distribution-rights acceptance.

## Executed contact and helmet audio

The complete-original303 composition also resolves the 35 closed original
contact/helmet call sites from pinned `detect.c` and `player.c`. Other profiles
retain their original explicit unresolved records. The generator now requires
the pinned `detect.c`, `player.c` and `ADJUST.C` alongside its previous inputs;
its metadata report is `closed-original-sound-tuples/v2`. No SAUD, animation,
callback JSON or private input manifest is regenerated.

Actual collision dispatch executes the original crowd tables synchronously,
including their duplicate weights, before subsequent detector RNG. Monkey
speech retains its chance/table draws, taunt versus announcer bank and original
pan38 branch. Duck speech and direct hit/bowling/helmet calls retain exact
source operands. The canonical `SourceProcessSoundCall` carries the original
contact cue, callee blob/line and actual shared output ordinal; the existing
portable mixer consumes this same object, not a post-Step selection.

Original receiver-hit, late-hit and plow processes run through the existing
source process host. They sleep19 (`CONTROL_TIME+1`),20 and50 ticks from their
first real wake, respectively; qcreate itself plays nothing. Receiver eligibility
is read at emission. Late-hit speech preserves its per-play latch, sequential
counter and original clean-language table. `BindSourceOriginalBadLanguageAdjustment`
accepts an actual operator value before the first source wake and preserves it
across a same-game restart. The explicit default is the pinned `ADJUST.C`
factory `SWEAR_ADJ=1`, not evidence of a particular cabinet's adjustment.

The existing prepared complete-original directory runs the real collision,
immediate selectors, deferred children and normal/fire/post-play helmet paths:

```sh
bash tools/source-player/dotnet.sh build tools/source-player/SourcePlayer.Checks.csproj --no-restore
bash tools/source-player/dotnet.sh tools/source-player/bin/SourcePlayer.Checks/Debug/net10.0/SourcePlayer.Checks.dll \
  "$COMPLETE_ORIGINAL_DATA" --original-contact-sounds-only
```

## Opt-in native Unity PCM output

`BlitzRuntime.BindSourcePortablePcmOutput` binds a separate
`SourceUnityPcmSoundOutput`, never the isolated preview. Supply actual
`SourceAudioBankReader`-decoded admitted banks, an established authorization
reference, the known initially loaded bank list and reserved-track mask before
the first source wake. The runtime merges actual source0 boot, VM, direct,
process and control FIFOs by their common producer ordinals. Animation output
still pairs with its exact original cue.

Profile `portable-unity-pcm-six-track/v1` renders actual decoded PCM through a
Unity streaming stereo `AudioClip`/`AudioSource` callback at31250Hz. It supports
source57Hz sample scheduling, six-track overlap/reservation/priority/preemption,
raw call gain and equal-power pan projection, verified playlist loop regions,
bank lifecycle, track controls and the qualified original global duck/restore
timelines. Its explicit default lookahead is12 source ticks; late delivery
faults rather than clamping, reselection or replay. Unsupported/unadmitted
programmes and unassigned original slots remain distinct explicit outcomes.
Public pause freezes the sample frontier; reset requires a fresh epoch.

To link the native renderer into the existing directed checks using actual
Unity6000.3.24 reference APIs:

```sh
bash tools/source-player/dotnet.sh build tools/source-player/SourcePlayer.Checks.csproj --no-restore \
  -p:NativeUnityEditorData="$UNITY_EDITOR_DATA"
bash tools/source-player/dotnet.sh tools/source-player/bin/SourcePlayer.Checks/Debug/net10.0/SourcePlayer.Checks.dll \
  "$COMPLETE_ORIGINAL_DATA" --original-contact-sounds-only
```

These checks execute actual collision/helmet/deferred raw calls through both
portable command output and native PCM rendering. Additional actual-bank
boundary checks cover scheduling, gain/pan, overlap, NOINT/forced priority,
reservation, unassigned slots, original music looping and global duck/restore
against the existing portable mixer. Actual Unity API compilation is
C#7.3/.NET Standard2.1. No Unity Editor or audio device is executed by these
headless checks; neither browser hearing nor native arcade DSP equivalence is
claimed. Without an explicit native consumer, nonempty output remains an error.
