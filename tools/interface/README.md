# Original HUD and cabinet preparation

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

The metadata/Editor owner creates these ignored assets after source freeze:

1. `Resources/BlitzSourceInterface/original-interface`: `SourceHudAsset`.
   Call `Initialize` with the exact manifest/pin, the three raw RGBA byte assets
   (`interface`, `playsel8`, `psarrow1`) and `SourceHud.shader`
   (`Hidden/Blitz/SourceHud`). Fonts use point sampling; the original diagram
   textures repeat. A missing shader/atlas is an error, not a generic texture.
2. `Resources/BlitzSourceInterface/original-match-services`:
   `SourceInterfaceServices`. Supply the **new** 117-clip library manifest
   `f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274`,
   the HUD, actual playbook catalog
   `b180291759fb22c8ae7b2de7d417867e15243f6071bb1a672f11bb2fb6da4bd3`,
   explicit match configuration and explicit source presentation settings.
   The 56-clip resource is immutable and is not overwritten.
3. `Resources/SourceData/movement`: the runtime-safe payload from
   `tools/movement/export.py`, SHA256
   `89c79007e04e9a17894c126c34dc66c199eb5a5961a4c749b1eb6b3e0e3951d6`.
   `SourceInterfaceMovementData` strictly checks its provenance/schema/pin and
   reinterprets binary32 bits. It preserves all constants, ordered selectors,
   friction and both 12×6 defense matrices. The old test `Responses` export is
   forbidden. `Movement` exposes the actual immutable `SourceMovementData`;
   Core owns its final injection and real callback integration.

Runtime passes the **same** loaded animation instance to Core, catch geometry and
the field. Its geometry uses all fifteen absolute imported reference pivots in
Unity basis/source units. The actual shared team asset supplies rosters. No raw
WAN, archive path or network request is used by WebGL.

The null RNG factory remains explicitly labeled ExternalUnverified preview RNG,
not the historical libc/scheduler/audio draw sequence.

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
`LateUpdate` or an interpolated transform. Exact raw source game mode and original
camera/effect/audio consumers remain required at integration.

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
