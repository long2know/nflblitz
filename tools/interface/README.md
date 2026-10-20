# Original HUD and cabinet preparation

## Original CAMBOT possession speech resource

The explicit `Contact281C2M1` runtime also requires the original interception
speech operands. Other service generations do not acquire this requirement.
Generate the pinned 40-byte payload locally:

```sh
python3 -B tools/interface/prepare_possession.py \
  --source-root PATH_TO_PINNED_NFL_ARCHIVE \
  --sound-header PATH_TO_PINNED_MIDWAYVIDEO_INCLUDE/goose/sound.h \
  --output .squad/.scratch/new-possession-speech
```

Its SHA256 is
`ac9bdf7c37c8f3d9d1e7184ca96e2d9c28aad2e157940d05194513b8f06da5df`.
With a legitimately entitled Editor, explicitly import it **before** importing
or verifying M1 match services, using these arguments:

```text
-executeMethod Blitz.Editor.SourceTeamInterfaceImporter.ImportPossessionSpeechFromCommandLine
-interfacePossessionSpeechRoot ABSOLUTE_AUTHORIZED_PREPARED_OUTPUT
-interfacePossessionSpeech possession-speech.bytes
-interfaceStableId original-cambot-message-data
```

Use a separate stable id from a new match-services import. The importer creates
`Resources/BlitzSourceInterface/original-interface-possession-speech.bytes` in
the ignored imported-assets tree. It rejects aliases, missing/changed bytes and
duplicate resource keys before writing; an existing canonical, byte-identical
TextAsset is reused without overwriting its GUID. This separate entrypoint also
adds the required resource beside already imported immutable M1 services.
The ordinary M1 importer checks it before creating any other output, and saved
M1 verification exercises the same runtime HUD binding.

The operands select original ordered sound metadata, not PCM/DSP playback.
API-only compilation and importer wiring/removal guards are not an executed
Editor import, a native player build or native pixel/scheduler qualification.
Original operands and imported resources remain ignored; never commit them.

The required `tests/interface/movement_bootstrap.py` clean-checkout gate carries
the importer and importer-check C# text into its isolated release shadow as
guard-only dependencies. They remain excluded from Runtime compilation. The
shadow runs the permanent importer guards without authoring state, original
assets or a Unity executable; missing guard inputs fail explicitly.

The Runtime camera provider still samples once per admitted source callback.
An incomplete original-normal continuation returns its retained heading without
publishing a new frame or advancing the last completed camera stamp. Otherwise
the provider reads the stamped pure frame and publishes the full Core transform
and explicit DIP in that same callback. The movement-stage guard protects both
exclusive paths and rejects ten actual-method source-text mutations; these
structural controls are not compiled or native execution evidence.

## Current synchronous sound-VM candidate

The sound-runtime candidate extends the published movement157 layer without
changing its animation/team/HUD/movement/bootstrap/DIP identities. Its services
are a **new** resource:
`BlitzSourceInterface/original-movement157-soundvm-match-services`.
The canonical SAFN `TextAsset` key is
`SourceData/sound-functions-safn1-4a557304`, exact1983 bytes/SHA256
`4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280`.
Use the [sound packaging entrypoint](packaging.md), including its three explicit
sound-root/data/hash arguments; original inputs/generated assets remain ignored.

The ten-argument services initializer retains the eight M1 arguments, then
requires `TextAsset soundVmFunctions,string trustedSoundVmSHA`. Serialized
`originalSoundVmFunctions`/`soundVmFunctionsSha256` must reference the canonical
resource, not merely matching bytes at another key. Actual M1 construction is
followed by `BindSourceSoundVmData(SoundVmData)` before the first executing57
wake. Old M1/B1 service identities are not relabeled or silently upgraded.

Core resolves exactly six source sound sites **inside ANI_CODE**, on its same
RNG stream. `TakeSourceSoundVmDispatches` is drained after each host Step and
before raw presentation cues, including catch-up/skips/partial-failure unwind.
The same `OriginalCue` pairs once; paired Resolved records replace the generic
callback path. Legitimate offscreen Resolved0 is handled normally. Unsupported,
direct speech and other FX remain explicit; MissingInput retains partial
calls/offsets and faults until Restart. No post-host selector/RNG/player_down
replay is installed.

`ISourceSoundVmOutputConsumer` accepts already-resolved full raw words only.
No real output implementation is bound: nonempty calls **fail visibly**, never
silence/noop/fakeclick/isolated-one-shot/Unity gain-pan approximation. See
[compiled transport/real Core tests](../../tests/audio/runtime/README.md).

All8 public PM/DM images and original CRC deployment wrapping are verified, not
missing. Exact DSP playback remains unqualified: historical TARGET_SYS/localset/
build identity and the matching legitimate32768-byte `sound102.u95` bootstrap
(Seattle ADSP2115, SHA1 `db80aa4a645804a4574b07b9f34dec6b6b64190d`) or
`vegassio.bin` (Vegas ADSP2104, SHA1 `f6e8405cfa604528c0224401bc374a6df9caccef`)
are not established. A pinball sample decoder is not a full DCS2 board runner.
No target substitution or playback claim is made.

## Retained HUD/cabinet producer

This offline producer reads explicitly supplied, Git-blob-verified archives:

- `historicalsource/nflblitz2000` revision
  `89f840d58d152a2a9f230cb8d0cdffbd554c8ded`, tree
  `a7e0352125d46075b7d98e87253ab89b9160a4c7`;
- `historicalsource/midwayvideo` revision
  `1792293aaaea00a919b59e8c05716978e85b4333`.

`pins.json` identifies all 36 required byte streams. In particular, an inherited
stale `playsel.c` is rejected; the verified blob is
`f5e87b921a4038d03f55174bbd5425241939e798`. The tool does not fetch originals or
execute archive build scripts. It compiles the unchanged pinned `CVTMOD.C` with
the actual `models.mak` `-z -q -name plyslct` arguments for play-diagram geometry.
The portability include only supplies standard declarations and `pow2`.

```sh
PYTHONDONTWRITEBYTECODE=1 python3 tools/interface/prepare.py \
  --source-root PATH_TO_COMPLETE_PINNED_NFL_ARCHIVE \
  --tools-root PATH_TO_PINNED_MIDWAYVIDEO_ARCHIVE \
  --output tools/interface/.artifacts/local-interface
```

The destination must be **new, project-local and ignored**. Nothing is
overwritten. Original inputs, atlas pixels, diagrams, generated headers,
Resources/TextAssets, captures and builds must never enter Git.

The current complete output is:

| Artifact | Identity |
| --- | --- |
| `interface.json` | SHA256 `258d5f4361d7da852b1446e774c5aad7d389ed268daabf3d4971c68a29fd8435` |
| Atlas | 1024×1024 bottom-first RGBA, 381 original image records |
| Fonts | Actual `bast8t`, `bast10`, `bast13`, `bast18`, `bast25`, `clock12` mappings, widths, anchors and spacing |
| Selection | 104 original planes / 5,462 triangles, original page/slot/team-play identities |
| Input | Goose 256-entry raw Hall decode, 49-entry digital projection, original 49-heading table |
| Additional art | Original wind/meter images and all 15 firebar frames |

The earlier `ed6d5f...` bundle is not this complete identity.

## Evidence boundary

WIFF chunk lengths, image payloads, palette IDs/counts, dimensions, signed anchors
and references are checked. RGBA WIFF pixels are not mistaken for TGA BGR(A).
Only unselected `!` authoring drafts are excluded; active symbols cannot alias
or fall back to another image. The frozen World TGA decoder is reused read-only.

The pixels and font mappings are original authoring data, **not an Arial
recreation**. The atlas is repacked. The pinned archives do not contain
Composit `/video/tools/c.exe`, generated `include/sysfont.h` /
`include/plyrinfo.h`, or their generated runtime atlas set. Consequently original
Composit clipping and Texus RGB332/ARGB4444/ARGB1555 quantization/mip equivalence
are **not** established. Do not label authored extents as a verified measurement
of a missing generated runtime `image_info_t`. This matters especially for exact
manual kick-meter accuracy. Host `qsort`/float output is not MIPS equivalence.

Missing/changed source fonts, glyphs, textures or planes fail explicitly.
`?actors=graybox` is the explicit LegacyBaseline60Hz diagnostic profile; its
analytic HUD is not used as a source fallback.

## Compiled resource composition

The published **B1/final117** recipe is retained in the separate B1 source
snapshot. The current movement157 source profile uses the distinct resources
and mandatory bootstrap below; it never treats an old B1 asset as an alias.
The metadata/Editor owner creates the ignored assets after source freeze:

1. `Resources/BlitzSourceInterface/original-interface`: `SourceHudAsset`.
   Call `Initialize` with the exact manifest/pin, the three raw RGBA byte assets
   (`interface`, `playsel8`, `psarrow1`) and `SourceHud.shader`
   (`Hidden/Blitz/SourceHud`). Fonts use point sampling; the original diagram
   textures repeat. A missing shader/atlas is an error, not a generic texture.
2. `Resources/BlitzSourceInterface/original-match-services`:
   the historical B1 `SourceInterfaceServices` identity. Its **117-clip** manifest is
   `f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274`,
   the HUD, actual playbook catalog
   `b180291759fb22c8ae7b2de7d417867e15243f6071bb1a672f11bb2fb6da4bd3`,
   explicit match configuration and explicit source presentation settings.
   The 56/117 resources are immutable and are not overwritten. This key is not
   the current movement157 service key.
3. `Resources/SourceData/movement`: the runtime-safe payload from
   `tools/movement/export.py`, SHA256
   `89c79007e04e9a17894c126c34dc66c199eb5a5961a4c749b1eb6b3e0e3951d6`.
   `SourceInterfaceMovementData` strictly checks its provenance/schema/pin and
   reinterprets binary32 bits. It preserves all constants, ordered selectors,
   friction and both 12×6 defense matrices. The old test `Responses` export is
   forbidden. `Movement` exposes the actual immutable `SourceMovementData`;
   Current movement157 composition injects the actual typed data into Core;
   B1 kept this accessor lazy and did not require it.

Runtime passes the **same** loaded animation instance to Core, catch geometry and
the field. Its geometry uses all fifteen absolute imported reference pivots in
Unity basis/source units. The actual shared team asset supplies rosters. No raw
WAN, archive path or network request is used by WebGL.

The null RNG factory remains explicitly labeled ExternalUnverified preview RNG,
not the historical libc/scheduler/audio draw sequence.

### Current movement157 bootstrap

The canonical animation manifest is
`f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e`:
**157 clips / 13,490 selected frames**. Use the permanent
`tools/animation/prepare_movement_complete.py` recipe, not the56/117 or earlier156
catalog. The original `R_SKIP` is **selected (`0x4000`) and uncompressed**
(`A_COMPRESSED=0x1`). Its missing right-hand record is reproduced by the exact
original converter-global buffer left by `Huffrun2`; no header flags, forced
selection, motion alias or running-table filtering are used.

Required identities are:

| Resource | Exact key |
| --- | --- |
| Team | `BlitzSourcePlayers/original-teams-movement157/team-presentation` |
| Animation | `SourceData/local-plyr266-movement157-f0ae9395` |
| Impact | `BlitzSourcePlayers/original-teams-movement157/impact-library` |
| HUD, unchanged bitmap data | `BlitzSourceInterface/original-interface` |
| M1 match services | `BlitzSourceInterface/original-movement157-match-services` |
| Actual runtime-safe movement | `SourceData/movement`, exact89c790 SHA above |

Follow the [explicit movement157 packaging recipe](packaging.md). In addition
to the existing explicit team/roster/station/possession/captain/home/head/
`pup_bigball` configuration, supply a separate UTF-8 bootstrap and its trusted
SHA256. `SourceInterfaceMovementBootstrap.Read` accepts exactly these fields:

| Field | Required original meaning/range |
| --- | --- |
| `schema` | `original-source-movement-bootstrap/v1` |
| `field_index` | Selected original friction field0..6 |
| `infinite_turbo_team_mask` | Whole raw team mask0..3, not a boolean |
| `speed_powerup` | Global boolean, not team-mask-derived |
| `fast_turbo_team_mask` | Whole raw team mask0..3 |
| `run_out_of_bounds_team_mask` | Whole raw team mask0..3 |
| `violence_powerup` | Explicit boolean |
| `source_hres` | Explicit original DIP value512 |
| `source_vres` | Explicit original DIP value256 or384 |

There are no guessed settings, browser dimensions, physical-home/station
shortcuts or mask-to-boolean conversions. Missing/null/duplicate/unknown members,
trailing JSON, coercions, invalid ranges and changed bytes/pins fail. The
bootstrap records a deliberately selected source DIP mode, not a default
implied by the schema. Original bootstrap and generated assets stay ignored.

The importer uses the eight-argument `SourceInterfaceServices.Initialize`
overload: existing animation/HUD/playbook/configuration/presentation/native-pad
arguments, then `TextAsset movementBootstrapText` and
`string trustedMovementBootstrapSha256`. The old six-argument prefix remains
callable but fails explicitly in this M1 source; it does not manufacture the
missing bootstrap. `MovementData` and `MovementSettings` are mandatory real Core
constructor inputs after presentation. The same actual immutable movement
instance, seven friction values and twelve Line/Back/acceleration/plow rows are
mapped without tuning or test-only `Responses`.
The serialized animation asset must be the canonical157 Resource at the exact
key above, not merely another asset with the same manifest. Startup verifies the
same loaded animation instance is used by Core, catch geometry, Team and renderer.

Runtime configures `PlayCamera(transform,prefab)` with the bootstrap DIP raster
**before** the five-argument `ConfigureSourceStage` using Core's actual
`SourceCameraRawGameMode`. Inside the existing pre-player source provider it
samples once, calls `RequireSourceCameraAtTick(executingTick)`, then immediately
passes that pure frame's position/all12 transform floats and the pinned DIP
resolution to `SetSourceMovementCameraFrame`. No renderer readback, identity
matrix, fresh `WorldJoint`, second source clock or post-host injection is used.
Core owns the previous post14-actor/15-joint cache and current-frame consumption.
One cue consumer and one host/restart lifecycle remain; focus clearing does not
advance/reset the match clock or replay held input.

Actual native resource import/build/browser proof remains blocked separately
by licensing198. Source audio/FX consumers, target RNG/scheduler, full normal
camera/C1 and all16 full-parity rows remain open; no silence/noop is installed.

## Cabinet bindings

These are host bindings to original **switches**, not guessed game actions:

| Station | Up / left / down / right | A / B / C |
| --- | --- | --- |
| 0 | W / A / S / D | V / B / N |
| 1 | arrows | Delete / End / PageDown |
| 2 | I / J / K / L | U / O / P |
| 3 | T / F / G / H | Z / X / C |

The administrative **R** restart is not a cabinet action. Focus/disable/reset
clears held/queued input and requires physical neutral; a focus wake cannot
replay a held restart.

WebGL uses the browser's explicitly **standard-mapped** gamepad at the same
station index: axes 0/1, physical buttons 0→A, 1→B, 5→C. Other mappings fail with
a binding diagnostic. Analog axes are clamped independently and rounded to
seven Hall levels, with half-level thresholds at ±1/6, ±1/2, ±5/6; the actual
Goose table then performs its original central-nine collapse. These analog
thresholds are a disclosed host adapter, not historical cabinet ADC timing.
Keyboard axes use extreme Hall positions. Neither path replaces all sticks
with eight directions. Simultaneous A/B/C masks remain simultaneous.

Native gamepads are disabled until the settings owner explicitly installs and
verifies these eight legacy axes:
`SourceStation{1..4}X/Y`, joystick number `{1..4}`, type JoystickAxis, zero-based
axis `0/1`, gravity `0`, dead `0`, sensitivity `1`, snap/invert `false`.
The native binding assumes buttons `0/1/5` on that verified device. This task
does not edit `InputManager.asset`.

Footer thirds provide A/B/C mouse pulses through the same ordered raw producer.
Actual station/configuration ownership is used; station 0 need not mean Home.
Captain is a teammate slot, not a station ID. Configuration order defines
teammate order for this explicit host configuration; powerup-driven captain
overrides/custom pages require their actual settings.

## Source ticks, menus and viewport

`SourceStationInputTransform` runs once **inside the existing Core source wake**.
The UI creates no second 57-Hz accumulator. It retains raw inputs and existing
commands, then adds only original menu commands. Shipping `playsel.c` uses an
exact newly-pressed switch: C changes page, B selects, A flips offense;
combinations take the default/no-action branch, including on defense.
The initial `sleep(20)`, original page/slot/play identity and nullable flip
transport are preserved. Custom/powerup pages, random-select source-process RNG
and manual kick-meter consumer state are explicit remaining requirements.

`FixedStepBridge` forwards every source event and presentation instruction after
each host `Step`, including every catch-up step. Its lifecycle calls typed
`IPendingSourceInput.ClearPendingInput`, never private-buffer reflection.
Camera advancement belongs to the verified in-Core RL_CAMBOT stage, not
`LateUpdate` or an interpolated transform. Current M1 uses the actual raw Core
mode and same-tick camera frame as described above; full original normal-camera
branches and actual effect/audio consumers remain separately qualified.

HUD layout uses actual canvas CSS dimensions, drawing-buffer dimensions and DPR.
Original glyph quads and sprite/plane bounds are checked against CSS content,
including 360×740 at DPR 2. Browser adaptation anchors/reflows the overlay without
resizing a fixed framebuffer or requesting fullscreen. Source camera composition
must keep the full viewport; portrait selection displays the last-interacted
side while both sides' actual input continues.

Audio cue identities are recorded, not synthesized: active `cursors` bank,
page 23, flip 19, hidden 60, cursor 63/64 and hidden edge 67/68.
`sounds/CURSORS.BNK` blob
`515154b9bb6c1e719a5cce018982480d5d764f0d` exists; decoded PCM/playlist-to-AudioClip
binding does not. Full bank decoding is outside this bounded task.
