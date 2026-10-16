# Source parity and port evidence

**Checked October 15, 2026 (PDT).** Reference: public
[`historicalsource/nflblitz2000`](https://github.com/historicalsource/nflblitz2000).
Pinned default-branch revision:
[`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`](https://github.com/historicalsource/nflblitz2000/commit/89f840d58d152a2a9f230cb8d0cdffbd554c8ded),
with a non-truncated source tree. The target is the original NFL Blitz 2000
appearance and logic, not a generic football graybox. Current code combines
original C# rules/runtime with a source-derived camera and local original-data
conversion. It is not a complete port of the original C engine or every shipped
version/configuration.

Archive-source links below pin that revision; line numbers refer to the raw source's
physical lines. Original inputs and converted art stay in ignored local storage;
the [reproducible preparation/import path](build.md#prepare-and-import-a-local-source-player)
uses permanent tools, not scratch-only helpers.

## Current implementation evidence

These are distinct evidence layers, not interchangeable parity claims.
Headless suites compile actual production inputs as C# 7.3 under .NET 10;
the separate actual Unity 6000.3.24f1 host runs establish the selected Unity
profile/build path. Results are owner-recorded evidence, not new builds or
rendering performed for this documentation update.

| Evidence layer | Observed result | Not established |
| --- | --- | --- |
| Rules/interaction/replay | **156 core cases**, zero skipped; six runner guards. Actual-source thirty-to-ten-yard mutation was rejected. Fourteen IDs, phase/rule transitions, passing, defense, scoring, final/restart and detached deterministic state/events pass. | Historical AI, physics, timing or rendered match equivalence. |
| Pure asset parser/mesh/pose | Permanent [asset suite](../tests/assets/README.md): **2,751 assertions** = original **419** parser/mesh + **2,332** original synthetic pose checks; **eight rejected mutations**, **seven production-file hash guards**, including `SourcePlayerPose.cs`. Clean-source and missing/zero/stale-count gates passed. | No original pose/art fixtures; native Editor/runtime adapters and actual-source vertex comparison remain separate. |
| Normal camera | Permanent [camera suite](../tests/camera/README.md): **3,568 pure assertions**, six rejected mutations; actual native Unity checks subsequently passed **3,595 assertions**. | Exact original scheduler, special camera modes or N64 pixel composition. |
| Offline player preparation | **35 synthetic tests**; pinned original inputs and all 32,768 selected runtime-atlas level-0 pixels verified. | Other team/skin variants, original lighting or lower-mip reproduction. |
| Native original player | Actual 15-limb/15-node player imported: **181 source vertices, 279 faces, 343 UV/material-split vertices, five materials**. Saved texture inputs matched hashes. | One Chicago home / `PC_WHITE` variant; no continuous animation, jersey-number overlays or full roster coverage. |
| WAN first pose | **12 synthetic reader tests** and actual `Qb.wan` → `QBSTANCE` frame 0: 15 normalized XYZW rotations, maximum matrix reconstruction error **8.77×10⁻⁸**. | One stationary QB ball-holding frame, not continuous animation or universal original pre-snap behavior. |
| Static source-pose integration | Actual native apply and independent saved-prefab reload passed; all **343 split vertices** matched the original matrices within **2.79×10⁻⁶ source units**. Original basis/hierarchy/anchored root retained, no invented Y flip or substitute pose. | Native Editor/runtime adapters are separate from pure synthetic tests; all fourteen actors use this one static stance. |
| Metadata | **12 guard tests** and current **73 assets / 73 metadata / 73 unique GUIDs**, terminal LF and three resolving scene/prefab script references passed; actual copied 33-hex mutation rejected without shared-source changes. | Owner-stable observed inventory, not a fixed count or native import/build certification. |
| Host WebGL / Docker | Two historical bind generations passed fresh-generation proof; standing generation **`0000067c000032dd00007938000039fe`** then built/promoted with matching served receipt/proof and all four payload hashes/size/MIME. **21 generation-tooling tests / eight actual artifact checks** separately passed. See [build evidence](build.md#bounded-evidence-recorded-october-15-2026). | No successful Docker-editor build; historical bind generations are not current standing posture. |
| Standing source-player browser | **Six checks / zero errors**, Docker `8080/?actors=source`, desktop 1280×800 and portrait 360×740: actual upright textured players, initial HUD, selection/snap/pass-catch/dead-ball/next-play/restart. Exact QBSTANCE marker and served receipt/proof/four hashes matched the standing generation; **30 browser-tool tests** separately passed. | Previous public label. No continuous animation, full-match/final restart, perspective focus-release geometry or hardware-performance qualification. |
| Latest identity/legend correction | Build **`00000688000072cf00002aa300002ef9`**, validated **`2026-10-16T02:44:09Z`** (October 15 PDT), passed host build, matching Docker promotion and **six fresh controls checks / zero errors**. Current desktop/portrait images show `NFL BLITZ 2000`, its in-progress subtitle and field-axis legend; those details were visually reviewed. **33 browser-tool tests** separately pass. | The strict tiny-font legend OCR diagnostic failed and remains recorded; manual image review is not an automated OCR pass. No full-match, perspective-focus or original-game fidelity claim. |

The passed standing subset and screenshot manifest are local
`tests/browser/artifacts/game-1792117872816/evidence.json` and `screenshots.json`
in that directory, including `source-qbstance-frame0-desktop-play-select.png`.
Actual upright PNGs were reviewed by the source-pose owner, Quality and
coordinator. This is not reused bind/isolated-preview evidence.
The earlier bind subset remains historical evidence at
`tests/browser/artifacts/game-1792116188060/evidence.json`; the
[browser README](../tests/browser/README.md#current-evidence-boundary) records the
scope and retained failed predecessors. Current identity-generation HTTP proof is
`Build/final-player-hosting-readiness.json`, verified `2026-10-16T02:48:24Z`
(October 15 PDT). That promotion does not invalidate the standing proof or make
its older-label PNGs new-title evidence.
The current final-title captures and controls evidence are
`tests/browser/artifacts/game-1792119716569/evidence.json` and `screenshots.json`,
including `source-qbstance-frame0-desktop-play-select.png` and the portrait
counterpart. `manual-presentation-review.json` records the actual visual review;
the failed strict legend OCR run at `game-1792119015841` is retained separately.
The separate public-API-only core match smoke reached four-quarter `GameOver`
and restart; it is not a rendered full match.

## Verified source facts → bounded implemented rules

Rule/data targets below are **implemented and headless-tested** within this slice.
The bounded browser subset does not certify every row or original-game equivalence.

| Behavior | Primary evidence: exact path, symbols and lines | First-playable target / qualification |
| --- | --- | --- |
| Seven per side | [`include/GAME.H` L218–226](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/include/GAME.H#L218-L226): fourteen actors divided by two. | Fourteen actors; source mode currently reuses one original player variant for both sides, not fourteen distinct roster models. |
| Thirty-yard series | [`include/GAME.H` L279–281](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/include/GAME.H#L279-L281), `FDYARDS`; [`nfl.c` L1626–1640](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/nfl.c#L1626-L1640), result handling. | New target = spot + thirty in offense-relative yards, capped at the goal. The separate ten-yard constant at [`include/GAME.H` L242](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/include/GAME.H#L242) is **not** the value used in this first-down transition. |
| Four downs, possession reversal | [`nfl.c` L1654–1690](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/nfl.c#L1654-L1690): failed fourth down changes sides; other failures increment the down. | Same world spot, opposite direction, down one/new target. |
| Incompletion preserves LOS and costs a down | [`nfl.c` L1693–1722](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/nfl.c#L1693-L1722), incomplete-result branch; [`result.c` L135–165](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/result.c#L135-L165), pass classified incomplete. | Return to snap LOS; fourth-down incomplete also changes possession. |
| Ordinary tackle/OOB, own-end-zone safety, enemy-end-zone touchdown | [`result.c` L86–131](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/result.c#L86-L131), normal-play classifiers. | Keep those outcomes. Possession-change/end-zone-recovery exceptions are deferred with interceptions/fumbles. |
| Scoring values | [`nfl.c` L1742–1776](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/nfl.c#L1742-L1776), safety and touchdown branches. | Opponent gets two for safety; offense gets six for TD. No automatic seventh point. |
| Field and end zones | [`include/GAME.H` L254–274](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/include/GAME.H#L254-L274), field extents; [L384–408](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/include/GAME.H#L384-L408), coordinate/unit conversions. | Normalize goal lines to X=0/100. Derived longitudinal scale is 7.8 archive units/yard; end-zone depth is (468−390)/7.8 = ten yards. Lateral half-extent is 182.85/7.8 ≈23.4423 normalized yards. This is a geometry derivation, **not** verified collision-edge or nominal regulation-width parity. |
| Spotting | [`nfl.c` L1422–1443](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/nfl.c#L1422-L1443), midfield-directed rounding and non-TD `1..99` clamp. | Whole-yard spot toward midfield and clamp; exact cast/float edge behavior not reproduced. |
| Separate pre-snap/live/dead phases | [`include/GAME.H` L105–111](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/include/GAME.H#L105-L111); [`nfl.c` L1281–1353](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/nfl.c#L1281-L1353), wait-for-snap/end flow. | Explicit selection, pre-snap, live, dead and final phases. Selection is not live gameplay. |
| Default auto-hike | [`include/GAME.H` L361–363](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/include/GAME.H#L361-L363), `MAX_HIKE_TIME`; [`nfl.c` L1155–1172](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/nfl.c#L1155-L1172), normal-play counter assignment. | Five seconds to auto-snap Home offense. This is the header default, potentially build-overridable; CPU's one-second readiness is an original slice choice. |
| Four regulation quarters, displayed 2:00 start | [`include/GAME.H` L218–226](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/include/GAME.H#L218-L226). | Four 120-second live-time periods in the slice; no overtime. Starting display/count supported; elapsed-time policy is simplified below. |

## Verified source features deliberately not carried over

- **Clock speed is not simply two wall-clock minutes.** `game_clock_proc` and
  `time_table` in [`nfl.c` L2868–2993](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/nfl.c#L2868-L2993)
  use adjustable countdown rates plus late-half/long-play slowdowns.
  [`ADJUST.C` L21](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/ADJUST.C#L21)
  provides the default setting; [L82–90](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/ADJUST.C#L82-L90)
  contains approximate timing descriptions. Comments and scheduler assumptions do
  not establish an exact real-time mapping. Use fixed 60 Hz live-time ticks, no
  late-half acceleration/deceleration or fractional-second discard.
- **Selection clock is not the snap clock.**
  [`playsel.c` L1045–1053](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/playsel.c#L1045-L1053)
  starts a different countdown with initial values nine/five by quarter.
  That helper's full timing was not traced; the slice has no selection timeout.
- **TD is not universally seven points.** Source enters an extra-point state
  ([`nfl.c` L1769–1776](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/nfl.c#L1769-L1776)).
  Free-point selection and contextual misses appear in
  [`playsel.c` L1239–1260](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/playsel.c#L1239-L1260),
  [L1342–1367](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/playsel.c#L1342-L1367),
  and [L1489–1502](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/playsel.c#L1489-L1502);
  two-point conversions/field goals add two/three in
  [`nfl.c` L1800–1818](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/nfl.c#L1800-L1818).
  All conversion/kick choices are deferred; this slice awards only TD/safety points.
- **Restarts are not all own-20 possessions.** The archive uses kickoff/quick-kick
  flow, including the quick-kick spot at forty in
  [`nfl.c` L986–1005](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/nfl.c#L986-L1005).
  Its touchback sets twenty at [L1724–1730](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/nfl.c#L1724-L1730);
  reusing twenty for initial/post-score setup is a **slice substitute**, not kickoff parity.

## Explicit unverified/deferred fidelity

Original deterministic routes, pursuit, blocking, movement speeds, turbo economy,
geometric tackle reach, ball arc/lead and automatic catch are slice tuning, not
measurements of source physics or AI. No claim covers historical pass eligibility,
catch-control timing, interceptions, fumbles, bobbles, laterals, dives, spins,
hurdles, audibles, on-fire/handicap systems, difficulty, penalties, exact forward
progress, collision order at corners, special teams, overtime, halftime
possession, multiplayer, coin/buy-in flow, replay, records, rosters or presentation.

Source field/stadium, original HUD art/layout, audio, jersey-number overlay
geometry, other rosters/skins, original lighting and continuous animation are not
ported. The field/HUD, Unity normals/material cutoff and simple animation in
explicit graybox mode remain approximations. Keep **source-traced**,
**headless-tested**, **native-verified**, **Unity-built**, and **browser-observed**
separate; none alone establishes whole-game parity.

## Visual target correction and asset audit

The original Yard Rush procedural presentation did not meet the requested
appearance. It remains available only as `?actors=graybox`; the current source
path is real mesh/rig/texture conversion, not renamed primitives. The field and
analytic HUD still do not meet the original visual target.

The user's [actual reference image](https://www.nintendo64ever.com/photos/jeux/Photo-Jeu-Nintendo-64-Fiche234,10244.png)
was viewed: close perspective, large helmeted/textured articulated players with
uniform numbers, field/stadium backdrop, compact upper-left score, large return
banner and bottom player bars. The distant minifigures and analytic HUD do not
match it. Its gallery identifies Nintendo 64; this archive's makefile targets
Seattle/Vegas arcade hardware. Identical edition/art/tuning is **not** established.

### Actual player, material and pose data

- **Geometry/rig:** [`models/plyr266.h`](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/models/plyr266.h#L997-L1173)
  and [`models/skeleton.h`](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/models/skeleton.h#L180-L197)
  now produce actual Unity rigid-limb meshes and pivots. Bind the pelvis-first
  `model266_limbs` mapping, not the hand-first generated array. Packed
  vertex/texture offsets, UV reuse, seams and signed-axis/winding conversion are
  explicit; this is not a guessed skinned mesh.
- **Textures:** `player.c:load_uniform` L10574–10624 maps the selected normal-body
  Chicago home / `PC_WHITE` player to `ARMHND.WMS`, `HEDSHU.WMS`, then the **same
  `BEAHLOD.WMS` atlas** for the three uniform slots. The local recipe uses the
  two verified authored skin TGAs plus the decoded actual runtime atlas, not
  arbitrary standalone uniform TGAs. Skin UVs divide S/T by 256; uniform UVs
  divide S by 128 and T by 256, with explicit V flip after honoring TGA origin.
  Bilinear repeat/no mipmaps and Unity atlas cutoff 0.01 are disclosed preview
  choices; inherited original alpha cutoff, lighting and lower mips are not
  reproduced. `draw_player` L6915–6928 separately draws jersey numbers; the five
  base materials alone do **not** render that overlay.
- **Authoring bind pose:** identity rotations already place the head below the
  raised feet in source-space geometry, before Unity conversion. The historical
  bind WebGL captures prove real mesh/texture visibility, **not correct standing**.
  That posture is unacceptable for gameplay; the preserved authoring-bind prefab
  is debug-only. It is not evidence for changing the global axis convention.
- **Authentic first pose:** `anim/Qb.wan`, Git blob
  `a5bed4bc6699a88281e888660e31e349d4009d3f`, contains `qbstance.bip` / `QBSTANCE`.
  `player.c` L4397–4416 and `plyrseq.c` L1074–1084 trace it to a neutral-stick,
  stationary QB. Frame 0 places the head above both feet using the original
  hierarchy. Native apply/reload now matches all 343 split vertices to the
  original matrices within 2.79×10⁻⁶ source units. Matching promoted WebGL
  captures visibly show heads above bodies and feet below, without an invented
  axis flip or pose. The [required apply/verify step](build.md#apply-qbstance-frame-0)
  reproduces this standing preview. It is one ball-holding stance, **not verified
  pre-snap for all roles or an animation system**; all fourteen actors still
  share the same Bears variant/static frame.

The WAN schema comes from the original
[`historicalsource/midwayvideo` converter](https://github.com/historicalsource/midwayvideo/blob/1792293aaaea00a919b59e8c05716978e85b4333/tools/wan2seq/wan2seq.c),
commit `1792293aaaea00a919b59e8c05716978e85b4333`, blob
`33c7e489d9b7a40d3b0cd435236db8c357d143fe`, not float-pattern guessing.
The [permanent reader](../tools/animation/README.md) documents big-endian WIFF
chunks, little-endian animation records and normalized XYZW conversion.
The original converter discards per-limb authoring offsets; rotations compose
about model-reference skeleton pivots. Root translation scale 0.12898 precedes
runtime anchoring. No historical converter executable or compression lookup
table is needed for this first full-quaternion pose.

### Source-derived normal camera

`NormalCameraModel` ports the normal tracking math, not just an aesthetic FOV:

- [`camdefs.h`](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/include/camdefs.h#L26-L64)
  and `cambot.c:TrackObjects/SetModeAngleTrgs/cambot_normal` establish behind-offense
  theta 90/270°, pitch −25°, two-yard focus height, 19-yard runner and 30-yard
  QB/setup distances, six-yard QB lead, lateral pan and ground/sideline limits.
- The active [`geom.s` projection](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/lib/ani3d/geom.s#L115-L153)
  uses reciprocal depth with a **664-pixel focal constant**. At the 512×384
  reference aperture, horizontal/vertical FOVs are approximately 42.1675°/32.2552°;
  the camera heuristic `ALPHA` is not the projection half-FOV.
- Archive scale 390 units per 50 yards becomes 7.8 units/yard. Gameplay remains
  `(X, Height, Z)` → Unity `(X - 50, Height, Z)`. Mesh/pose basis conversion stays
  explicit at the import boundary.
- Missing archive QB-mode/pass-endpoint/offscreen flags require inferred QB mode
  and the current receiver position for pass span. Dead/final direction and
  incomplete targets are remembered across the kernel's early rule resolution.
- Smoothing adapts the source .08/.35 response at 57 Hz to real time; it is not
  the original scheduler/velocity integrator. The simulation remains fixed
  60 Hz. Focus loss/restart/rewind/long gaps realign without advancing gameplay.
- HUD-aware viewport and conservative silhouette fitting may pull farther back
  than the source baselines. Aspect expansion and Unity projection are browser
  adaptations. Special/replay/attract/cinematic modes, shake and exact hardware
  clipping/timing are not ported; N64 pixel orientation is not certified.

### Available but not ported

`models/field2.sdl` and `models/stadpie.sdl` contain original stadium geometry;
missing generated headers do not mean missing art. `img2d` WIF bundles and
manifests exist, but original HUD sprites are not decoded/installed. The archive
contains additional WAN/audio payloads, not a completed Unity animation/audio
pipeline. A complete arcade rebuild still needs external MIPS/Goose/Glide/sound
dependencies and generated outputs. The bounded Unity conversion does not
require reconstructing that executable.

## Provenance and local data

The repository's publication surface is code, conversion recipes and original
synthetic tests. Original source/art, converted textures/meshes, decoded poses,
builds and captures remain ignored under the source cache, `.squad/.scratch/`,
`Assets/Imported/` and local artifact directories; they persist on disk. The
preparation tool retains pinned hashes and upstream notices, including the
restrictive notice in `cambot.c` L6–12. Public repository availability is **not an
original-art license**. No license declaration or automatic art/image upload is
part of this port path.
