# Source parity and port evidence

**Contract checked October 16, 2026 (PDT).** Reference: public
[`historicalsource/nflblitz2000`](https://github.com/historicalsource/nflblitz2000).
Pinned default-branch revision:
[`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`](https://github.com/historicalsource/nflblitz2000/commit/89f840d58d152a2a9f230cb8d0cdffbd554c8ded),
with a non-truncated source tree. The target is the original NFL Blitz 2000
appearance and logic, not a generic football graybox. Checked-in **movement157 +
Team + HUD/cabinet** source extends the frozen B1/canonical117 layer with
mandatory real M1 data/settings and same-tick source-camera cache composition.
It retains actual ball/input/cue services, all31-team presentation, original
bitmap HUD/selection and raw two/four-station input. Source/pure/API evidence is separate from the **older
live56 WebGL generation**. No combined native resource import, game build,
new pixels/browser session or actual game-audio consumer is established.
It is not a complete port of the original C engine or every shipped
version/configuration. **Full source gameplay parity remains OPEN; all sixteen
completion-matrix rows are still required.** The static-player milestone and
simplified kernel below are historical baseline evidence, not an acceptable
stopping point. No deferred feature below is removed from the completion target.

Archive-source links below pin that revision; line numbers refer to the raw source's
physical lines. Original inputs and converted art stay in ignored local storage;
the [current movement157 resource contract](build.md#current-movement157-source-mode-resources)
uses explicit local inputs. The older single-player/56 recipes are retained as
historical controls, not sufficient source-mode setup. Original motion is not a
published test fixture.

## Current movement157 bootstrap boundary

The [complete animation producer](../tools/animation/README.md#explicit-movement-complete-extension-156-unchanged-clips-plus-r_skip)
pins the new157 manifest
`f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e`
and **13,490 selected frames**. The old claim that `R_SKIP` was unselected was
incorrect: `A_SELECTED=0x4000`, `A_COMPRESSED=0x1`. The selected uncompressed
sequence omits limb5; the named original converter-global last writer
`Huffrun2` supplies the retained quaternion record under a narrowly pinned
replay policy. This is not authored missing-hand data, identity fill,
whole-clip alias, altered flags or shipped-SEQ/target-bit proof.

The [movement157 resource/bootstrap contract](../tools/interface/README.md#current-movement157-bootstrap)
uses new `original-teams-movement157` team/impact keys, animation
`SourceData/local-plyr266-movement157-f0ae9395`, service
`BlitzSourceInterface/original-movement157-match-services` and mandatory
`SourceData/movement` hash89c790. Old56/117/156 assets remain immutable.
The operator must separately pin all movement field/masks/global booleans and
the actual512×256 or512×384 DIP mode, retaining every existing explicit
match/team/station/home/head/pup selection. No browser/physical-home/default
inference is a source setting.

Actual `SourceInterfaceServices.CreateSimulation` injects the real typed data
and settings. Inside the existing camera provider, the actual source sample
precedes the tick-guarded pure frame read and Core frame injection; raw Core
game mode is used rather than a semantic phase cast. Core retains ownership of
previous post14-actor/15-joint geometry. No render matrix, WorldJoint or
second57-Hz accumulator is substituted. Source/pure/API evidence below does not
constitute native resource import, game pixels or all-M1/full-normal/C1/RNG/
scheduler parity.

## Current B1 resource and activation boundary

**Retained previous B1/final117 source/revision only.** This contract/evidence is
not relabeled as movement157; the current M1 bootstrap is above.

The [exact117 recipe](../tools/animation/README.md#reproduce-exactly-the-expanded-b1b2-117-sequence-library)
uses `--catalog tools/animation/ball_sequences.json` and must reproduce manifest
`f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274`.
The [all31-team/impact importer](../tools/assets/README.md) installs the distinct
`original-teams` resources; the [HUD producer](../tools/interface/README.md) and
[B1 packager](../tools/interface/packaging.md) supply
`BlitzSourceInterface/original-interface` and `original-match-services`.
The shared117 instance, actual fifteen-pivot geometry, real P1 catalog and team
rosters are required. Home/away roster selection, stations/captain/possession,
designated home side, heads and power-up settings must be explicit, not guessed
from team colors, actor roles or example/default configuration.

`?actors=source` fails visibly on missing resources; the old single Chicago
prefab/56 library cannot substitute. Only explicit `?actors=graybox` enables the
legacy semantic controls/analytic HUD. New source bindings are the
[raw cabinet switches](../README.md#source-cabinet-controls), including concurrent
A/B/C edges and actual station ownership. Original sound/FX callbacks also fail
when unbound; an offline decoder or silent/noop consumer is not game audio.
No M1/156 resource or camera/settings injection is included in this B1 layer.

Two archived6000.3.24 and one installed6000.6.5 attempts exited198 before native
import (licensing404/missing headless entitlement). The target remains
**6000.3.24f1**. Installed6000.6.5 `GetInstanceID` incompatibility is separate.
The parent visibility-only `DecodeUtf8` and four serialized-null repairs resolve
the earlier compile blockers, **not** licensing or native acceptance. Historical
qualification notes in frozen recipes predate those repairs.

## Current implementation evidence

These are distinct evidence layers, not interchangeable parity claims.
Headless suites compile actual production inputs as C# 7.3 under .NET 10;
actual Unity-reference .NET Standard2.1 API checks are another layer. The
recorded Unity6000.3.24f1 host runs establish the **older** selected build path,
not the new source composition. Results are owner-recorded evidence; no suite,
build or rendering was rerun for this documentation update.

| Evidence layer | Observed result | Not established |
| --- | --- | --- |
| Recorded56 continuous original animation — October 16 | **56 original clips / 3,731 selected source frames** loaded by the shared immutable library. **26,745** pure clip/sampling checks and **13 rejected renderer mutations**; actual native first/middle/last/loop-diagnostic and independent heading/scale/transition samples made **77,861 vertex comparisons**, maximum error **1.1716899280145299×10⁻⁵ source units**. | Selected clips and verified rendering of the older preview, not the current117/Team/HUD composition, every source selector/script/root branch, historical SEQ quantization or original fast-trig/float equivalence. Loop diagnostics do not make every action a loop. |
| Older animated host WebGL / live Docker — October 16 | Build **`0000063700005f540000316b000003e7`**, validated **`2026-10-16T17:43:03Z`** (October 16 PDT), is promoted at `8080/?actors=source`. A **219.875-second real browser recording** contains **12 actors with non-rigid same-clip frame/joint changes**, **eight observed actions** and **zero browser runtime errors**. | Not deployment of the new B1/Team/HUD source, fourteen independently proven moving actors, all-action coverage, a complete rendered match, full focus-release geometry or representative hardware performance. Throw/GetUp clips are native-verified but those actions were not observed in this recording. |
| Source clock/rules helpers and original-C oracle — October 16 | **21 groups / 123,039 original-C cases**; **123,004 production C# comparisons / 864,156 assertions**, including all **332 contract/host-adapter assertions**, zero failures. Covers packed clock wakes/discard, result classification, exact spotting/dispatch/clamp, field conversion and CPU onside decisions. The actual 60-host/57-source adapter now uses the canonical117 library, fifteen real reference pivots and actual roster/playbook services with `--full-kernel --fixture-manifest`. | **35 geometry cases are C-only**, not C# comparisons. Later result-switch outputs, other PAT/kick/period callers, complete animation interpreter, RNG/Goose scheduling and whole-game behavior are not certified by this corpus. |
| Original ball/input/cue callers — October 16 | **488,736 assertions / twenty rejected actual-source mutants**: 470,666 ball comparisons, 18,002 raw-input/camera/cue caller assertions, 32 actual-pose/control checks and 36 PAT/kickoff sentinel checks. Permanent preparation reproduces the actual117 library and real geometry/roster/playbook inputs; all332 adapter assertions and156 legacy cases are retained. Actual Unity-reference C#7.3/.NET Standard2.1 API compilation passes separately. | Host-C projections and named internal seams, with a separate public raw-B snap/wrist/catch/throw flow; not an original full match. Target `qsort` ties, platform RNG/scheduler, complete M1/C1/C2, human kick meters and sound/FX consumers remain open. No Editor, native game or new browser rendering is established. |
| Original team/impact source — October 16 | [Presentation checks](../tests/presentation/README.md): **680,731 assertions / fourteen rejected production mutants**, all **31 teams, 20 heads and 86 selected texture identities**. Actual roster/uniform-clash/skin/model/BCD-number bindings and seven source camera tables are exercised; 15,242 original-C camera ticks have zero measured state/matrix error in the host functional comparison. | Not native imported assets, opposing-team pixels or a game impact event. Missing Texus-generated WMS quantization for explicitly identified authoring inputs, jersey raster/depth behavior, MIPS fast-trig, full normal-camera modes and target RNG/scheduler remain qualified. |
| Original HUD/cabinet source — October 16 | [Interface checks](../tests/interface/README.md): **16,215 assertions / thirteen rejected production mutants**, nine converter and four interop tests. Complete data has **381 images / six fonts / 104 selection planes**. Actual bitmap metrics, packed source HUD state, raw49 A/B/C edges, station/captain mapping, focus clearing and CSS/DPR layouts including360×740/DPR2 are checked. | Repacked authored art is not missing Composit/Texus runtime-pixel equivalence. No native/browser HUD pixels, physical cabinet ADC timing, full custom/random/power-up selection, manual kick meters or game-audio proof. |
| B1 stage caller / complete-owned-UI API — October 16 | [Stage follow-up](../tests/interface/STAGE.md): **80 runtime sources including all16 owned UI files**, actual Unity6000.3.24 C#7.3/.NET Standard2.1, zero diagnostics for both `UNITY_EDITOR` and `UNITY_WEBGL`, plus **21 caller/scope/inventory guards**. Source camera is registered before the first wake; cue/lifecycle forwarding has one owner. | Runtime-with-defines is not a separate Editor assembly or native execution. The earlier three-adapter-only API project excluded existing Runtime/HUD files and was not complete UI proof. Semantic B1 camera lifecycle is not all original raw modes. |
| HUD/match-service packaging — October 16 | **107 actual-data assertions / eighteen rejected production mutants**. The final integrated API snapshot passes **82 runtime sources / separate5 Editor sources**, actual6000.3.24 references, zero diagnostics after parent public-`DecodeUtf8` and four-null-initializer repairs. | No new native import/save/reload, atlas GPU pixels, game build or browser proof. Pre-repair internal-access/CS0649 reports are historical, not current blockers; licensing198 and actual audio/FX consumers remain open. |
| Current movement157 Runtime bootstrap — October 16 | **33,921 pure assertions / ten compiled production bootstrap mutants**; **10,941 actual89c790 production Runtime-composition assertions / four compiled Runtime mutants**. All field/team-mask/global-power/DIP combinations, every original friction/Line/Back/acceleration/plow value and real constructor/helper wiring are exercised or structurally guarded. **33 separate structural Runtime/service mutants** protect frame order, raw mode, mandatory settings/resources, canonical shared animation identity and lifecycle. Actual Unity6000.3.24 C#7.3/.NET Standard2.1 profiles compile **85 runtime sources / all16 owned UI**, zero diagnostics for Editor/WebGL. | The actual compiled Runtime's pure helper executes under .NET, not Unity native. Structural caller guards are not physics execution or target-MIPS proof. This does not replace QA's Core caller or Team's camera campaigns, provide imported157 assets/game/browser proof, or close M1/C1/full normal camera/audio/RNG/scheduler. |
| Legacy rules/interaction/replay | **156 core cases**, zero skipped; six runner guards. Actual-source thirty-to-ten-yard mutation was rejected. Fourteen IDs, phase/rule transitions, passing, defense, scoring, final/restart and detached deterministic state/events pass. | These preserve the asset-free legacy constructor, not original AI, physics, timing or a rendered full match. |
| Original movement/evasion helpers — October 16 | **14,047 original-C cases / 813,702 production assertions**, **sixteen rejected implementation mutants** and **fifteen publication guards**. The explicit SDK C# 7.3/.NET Standard 2.1 profile passes; a separate actual Unity-reference API mode is preserved. The runtime-safe producer retains exact constants, selectors, friction and both 12×6 defense tables without test-only animation responses. | Pure helpers and static data, not completed Core callers, source animation/control callbacks, cached camera-joint integration, target RNG/scheduling or native/browser gameplay. SDK API success is explicitly not Unity validation. All sixteen matrix rows remain open. |
| Pure asset parser/mesh/pose | Permanent [asset suite](../tests/assets/README.md): **2,751 assertions** = original **419** parser/mesh + **2,332** original synthetic pose checks; **eight rejected mutations**, **seven production-file hash guards**, including `SourcePlayerPose.cs`. Clean-source and missing/zero/stale-count gates passed. | No original pose/art fixtures; native Editor/runtime adapters and actual-source vertex comparison remain separate. |
| Normal camera | Permanent [camera suite](../tests/camera/README.md): **3,568 pure assertions**, six rejected mutations; actual native Unity checks subsequently passed **3,595 assertions**. | Exact original scheduler, special camera modes or N64 pixel composition. |
| Offline one-player preparation control | **35 synthetic tests**; pinned original inputs and all 32,768 selected runtime-atlas level-0 pixels verified. | This row is the unchanged one-variant control, not all-team import, original lighting or lower-mip reproduction. |
| Historical56 offline animation preparation — October 16 | Permanent metadata-only catalog and batch recipe reproduce **56 clips / 3,731 selected frames** from **11 pinned WANs**. **38 Python tests** passed in both worktrees; all **57 output-file hashes** matched across primary, relocated and publication inputs. **67,386 assertions** through the frozen C# 7.3 typed library validated unchanged source identities, selected maps and frame values. | Portable provenance changes the old machine-dependent decoded/manifest hashes. Recipe checks did not execute Unity or regenerate the recorded WebGL build; the117 catalog is a separate explicit selection. |
| Historical native original player | Actual 15-limb/15-node player imported: **181 source vertices, 279 faces, 343 UV/material-split vertices, five materials**. Saved texture inputs matched hashes. | One Chicago home / `PC_WHITE` variant; no native jersey-number/all-team proof. Import-only proof is separate from animation proof above and from the newer Team source layer. |
| Historical WAN first pose — October 15 | **12 synthetic reader tests** and actual `Qb.wan` → `QBSTANCE` frame 0: 15 normalized XYZW rotations, maximum matrix reconstruction error **8.77×10⁻⁸**. | That milestone proved one stationary QB ball-holding frame, not continuous animation or universal original pre-snap behavior. |
| Historical static source-pose integration — October 15 | Actual native apply and independent saved-prefab reload passed; all **343 split vertices** matched the original matrices within **2.79×10⁻⁶ source units**. Original basis/hierarchy/anchored root retained, no invented Y flip or substitute pose. | All fourteen actors used that one static stance in the historical preview, not in the later56 animated generation. |
| Historical metadata snapshot — October 15 | **12 guard tests** and **73 assets / 73 metadata / 73 unique GUIDs**, terminal LF and three resolving scene/prefab script references passed; actual copied 33-hex mutation rejected without shared-source changes. | Owner-stable observed inventory, not a fixed count or native import/build certification. |
| Historical host WebGL / Docker — October 15 | Two bind generations passed fresh-generation proof; standing generation **`0000067c000032dd00007938000039fe`** then built/promoted with matching served receipt/proof and all four payload hashes/size/MIME. **21 generation-tooling tests / eight actual artifact checks** separately passed. See [build evidence](build.md#bounded-evidence-recorded-october-15-2026). | No successful Docker-editor build; these are not the current animated generation. |
| Historical standing source-player browser — October 15 | **Six checks / zero errors**, Docker `8080/?actors=source`, desktop 1280×800 and portrait 360×740: actual upright textured players, initial HUD, selection/snap/pass-catch/dead-ball/next-play/restart. Exact QBSTANCE marker and served receipt/proof/four hashes matched the standing generation; **30 browser-tool tests** separately passed. | Previous public label and static frame only. No full-match/final restart, perspective focus-release geometry or hardware-performance qualification. |
| Historical identity/legend correction — October 15 | Build **`00000688000072cf00002aa300002ef9`**, validated **`2026-10-16T02:44:09Z`** (October 15 PDT), passed host build, matching Docker promotion and **six fresh controls checks / zero errors**. Its desktop/portrait title and field-axis legend were visually reviewed. **33 browser-tool tests** separately passed. | The strict tiny-font legend OCR diagnostic failed and remains recorded; manual image review is not an automated OCR pass. Not the current animated generation. |

Recorded56 live-animation proof is local
`.squad/.scratch/continuous-animation-candidate-proof.json`; native/input and
recording paths/hashes are in `.squad/.scratch/continuous-runtime-handoff.json`.
The recording observed `PreSnap`, `Run`, `Kick`, `Idle`, `Catch`, `TurboRun`,
`Fall` and `Contact`, not every reachable action. The candidate proof's
`current8080` field records the **pre-promotion** static generation; the
coordinator subsequently promoted the verified animated build above.

The following **October 15 historical** standing subset and screenshot manifest
are local
`tests/browser/artifacts/game-1792117872816/evidence.json` and `screenshots.json`
in that directory, including `source-qbstance-frame0-desktop-play-select.png`.
Actual upright PNGs were reviewed by the source-pose owner, Quality and
coordinator. This is not reused bind/isolated-preview evidence.
The earlier bind subset remains historical evidence at
`tests/browser/artifacts/game-1792116188060/evidence.json`; the
[browser README](../tests/browser/README.md#current-evidence-boundary) records the
scope and retained failed predecessors. Historical identity-generation HTTP proof is
`Build/final-player-hosting-readiness.json`, verified `2026-10-16T02:48:24Z`
(October 15 PDT). That promotion does not invalidate the standing proof or make
its older-label PNGs new-title evidence.
The historical final-title captures and controls evidence are
`tests/browser/artifacts/game-1792119716569/evidence.json` and `screenshots.json`,
including `source-qbstance-frame0-desktop-play-select.png` and the portrait
counterpart. `manual-presentation-review.json` records the actual visual review;
the failed strict legend OCR run at `game-1792119015841` is retained separately.
The separate public-API-only core match smoke reached four-quarter `GameOver`
and restart; it is not a rendered full match.

## Verified source facts → bounded implemented rules

This is the **October 15 legacy first-playable ledger**, retained for the
asset-free `GameSimulation(int seed)` compatibility profile. Its rule/data
targets are headless-tested, not an account of every behavior of the injected
57 Hz source profile. The current source helpers and their narrower independent
oracle coverage are recorded above; neither evidence set closes a full matrix row.

| Behavior | Primary evidence: exact path, symbols and lines | First-playable target / qualification |
| --- | --- | --- |
| Seven per side | [`include/GAME.H` L218–226](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/include/GAME.H#L218-L226): fourteen actors divided by two. | Fourteen actors; the historical56 preview reused one original variant for both sides. Current Team source coverage is recorded separately above, not inferred from that preview. |
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

## Baseline substitutions to replace, not scope exclusions

- **Clock speed is not simply two wall-clock minutes.** `game_clock_proc` and
  `time_table` in [`nfl.c` L2868–2993](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/nfl.c#L2868-L2993)
  use adjustable countdown rates plus late-half/long-play slowdowns.
  [`ADJUST.C` L21](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/ADJUST.C#L21)
  provides the default setting; [L82–90](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/ADJUST.C#L82-L90)
  contains approximate timing descriptions. Comments and scheduler assumptions do
  not establish an exact real-time mapping. The baseline uses fixed 60 Hz
  live-time ticks without those adjustments. The injected source profile now
  uses the packed `SourceClock` and 60-to-57 adapter; original scheduler and
  process-to-vblank equivalence remain unverified.
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
  The legacy slice awards only TD/safety points. The current source profile
  integrates PAT/scoring helpers, not complete independently verified conversion
  and special-teams flow.
- **Restarts are not all own-20 possessions.** The archive uses kickoff/quick-kick
  flow, including the quick-kick spot at forty in
  [`nfl.c` L986–1005](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/nfl.c#L986-L1005).
  Its touchback sets twenty at [L1724–1730](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/nfl.c#L1724-L1730);
  reusing twenty for initial/post-score setup is a **legacy slice substitute**,
  not kickoff parity. The source profile now starts an opening kickoff and
  integrates kick/period helpers; complete caller/motion coverage remains open.

## Baseline unverified fidelity, still required

Legacy deterministic routes, pursuit, blocking, movement speeds, turbo economy,
geometric tackle reach, ball arc/lead and automatic catch are slice tuning, not
measurements of source physics or AI. The source profile adds bounded source
movement/action/release logic, still with provisional selectors, AI, RNG and
contact branches. No complete-equivalence claim covers historical pass eligibility,
catch-control timing, interceptions, fumbles, bobbles, laterals, dives, spins,
hurdles, audibles, on-fire/handicap systems, difficulty, penalties, exact forward
progress, collision order at corners, special teams, overtime, halftime
possession, multiplayer, coin/buy-in flow, replay, records, rosters or presentation.

The source field/stadium converter, reader and importer have separate
original-C, native and world-only WebGL evidence. That does not prove the new
combined game; see the [environment recipe](../tools/environments/README.txt).
Checked-in Team/HUD adapters now consume actual roster/draw metadata, BCD
numbers, opposing uniforms and original bitmap HUD/selection data rather than
merely exposing a roster library. Native Resources and actual game pixels for
that combined layer remain unverified. Lighting and actual audio/FX consumers
remain open. Continuous original animation is implemented for the selected
libraries, not all source action/selector/root-transition branches. Explicit
graybox field/HUD/animation remain diagnostics. Keep **source-traced**,
**headless-tested**, **native-verified**, **Unity-built**, and **browser-observed**
separate; none alone establishes whole-game parity.

## Full-source completion matrix — executable acceptance

All paths/symbols in this section refer to the pinned archive revision above.
On October 16 the cached metadata's **entries** matched the commit's actual tree
`a7e0352125d46075b7d98e87253ab89b9160a4c7`; its top-level `sha` labels the commit,
not that tree. Cached `nfl.c` and `playsel.c` bytes failed their Git blob checks.
Rules below were read from the Git blob API and verified against
`15b49474fb55e8bac280b6515fcf7bf59cdf8664` and
`f5e87b921a4038d03f55174bbd5425241939e798`, respectively. Do not use raw-cache
line numbers or raw-URL freshness as content verification.

**Status vocabulary:** **V/P** = source expectation verified / full implementation
and parity tests pending; **partial** = a bounded subset exists, including the
October 16 animation/helper increment where stated.
Verification covers the facts listed, not untraced callees or every branch of
the group. No row currently has full parity acceptance. L = pure logic, D = source data,
V = visual/audio/runtime. Quality records expected **and observed** state, ordered
events, decisions and source ticks for each named case; a header constant alone
does not close a row.

| ID / kind / status | Active source targets | Minimum discriminating cases / reference expectations |
| --- | --- | --- |
| A1 animation clock, poses, transitions — L/D/V, V/P; continuous increment partial | `player.c:player_proc` L750–1177; `plyrseq.c:qb_stance_anim` L1075–1084, `qb_run_anim/qb_t_run_anim` L306–343; `lib/ani3d/ANIM.C:_animate_block/_next_frame/_ani_inttoseq/_ani_runtoend/_ani_intstream/_ani_streamtoend`; `models/skeleton.h` | Different role stances and moving/action clips on fourteen actors; stance loops at one selected frame/tick; turbo QB streaming uses **0.81 ticks/frame**, not a universal FPS. Verify first/middle/last/loop frames, hold counts, interrupted transition, two-part torso heading, root anchors and no double drift. Authentic clip + approximate selector is still partial. |
| M1 locomotion/turbo/evasion — L/D, V/P; helpers/callers/bootstrap partial | `player.c:standard_run/qb_run/_mode_qb/_mode_wpn/_mode_db/maybe_dive`; `include/player.h` L175–203; `playbook.c` route opcodes | Neutral/49-way stick, reverse/backpedal, turbo exhaustion/recharge, spin, juke, hurdle, dive and field friction. Reference base constants are archive units/tick: QB **1.30**, carrier **1.35**, human defender **1.45**, turbo defender **1.85**; apply actual stat/handicap/friction branches, not those constants alone. Turbo re-press with `0 < non_turbo_time < 9` flags the double tap (`player.c` L952–974). |
| C1 blocking/contact/get-up — L/D/V, V/P | `detect.c:player_detect_proc/do_collision/evade_vs_tackle/block_vs_rush/fumble_or_bobble`; `player.c:player_down`, `plyrseq.c` attack/get-up scripts | Attacker/victim order, block vs rush, dive vs spin/hurdle, uninterruptible/puppet/contact windows, landing/get-up, late hit and helmet/ball consequences. Compare attack volumes and source joint positions, not the legacy 1.15/2.4-yard tackle radii. Offscreen actors must still have collision matrices (`player.c` L6634–6675). |
| C2 on-fire/tuning — L/D/V, V/P | `player.c:check_receiver_fire/reset_team_fire/maybe_plyr_fire`; **active** `nfl.c` L1445–1512; `handicap.c:set_handicaps`; `ADJUST.C` | Same receiver's **third** consecutive completion enables offense fire; two qualifying loss-of-yard results enable defense fire; reset/turnover/loss and on-fire gameplay effects must match. `detect.c:update_onfire_stuff` is inside `#if 0` and is **not** the defense oracle. Default difficulty **5**, allowed **0..10**; compare seeded decisions across settings. |
| B1 throw/receiver/catch/control — L/D/V, V/P | `player.c:_mode_qb/pick_receiver/qb_release_ball/set_ball_vels/standard_catch/wpn_change_player/db_change_player`; `plyrseq.c` throw callbacks; `include/GAME.H:CONTROL_TIME` | Intent vs actual release tick; normal/turbo throws, receiver selection and exact human-control versus possession-credit/TD ordering (**CONTROL_TIME=18** ticks; not an assumed controller-transfer delay). Active catch proximity is **<14 source units** to pelvis or **<5** to a wrist, not the nearby commented 12. Reject self-catch at flight time **<=10**, other offensive receiver **<=15**, fumble/bobble **<=30**; test each boundary and actual catch/deflect/interception outcome. |
| B2 loose ball/possession — L/D/V, V/P | `player.c:ball_proc/fumble_ball/lateral_ball/standard_catch`; `include/ani3d.h:GRAVITY`; `detect.c:fumble_or_bobble`; `result.c:_norm_oobball/_norm_car_oez` | Pass, bobble, fumble, lateral, interception, recovery by either team, return, out-of-bounds and end-zone exceptions. Gravity is **−0.08 source units/tick²**, with the source half-step position update. Recovery in own end zone without leaving gives touchback only under the source possession/recovery flags; leaving then returning can give safety. Forward fumble OOB cannot improve the spot past the fumble point. Preserve source RNG consumption/probability tables. |
| R1 downs/spotting — L, V/P; partial | `nfl.c` L1422–1443, L1626–1730; `result.c:result_functions/_norm_*` | Four downs, **30** yards, goal-to-go, incompletion/turnover both directions. Verify the actual source-space rounding expression (integer cast **before** `*10/78`, integer division) and `1..99` clamp, including negative and near-zero positions. That helper now has original-C comparisons; full caller/down/result-switch parity remains pending. Legacy yard-space ceil/floor is not exact. |
| T1 clocks/snap — L, V/P | `main.c:graphics_init/get_tsec`; `nfl.c:game_clock_proc/time_table` L2868–2993, fractional discard L1387–1388; `ADJUST.C` L21; `player.c:_mode_qbps`; `playsel.c:timedown_play_select` caller L1045–1053 | Reference gameplay profile **57 ticks/s**; max auto-hike **5*tsec = 285**. Packed start **0x00020000**, first active clock iteration **0x00013b18**. Rates **[30,25,20,18,16]**, default index **2**, bad adjustment fallback **3**. At packed `0x00001f00`, quarter index 1/3 yields `0x00001e22`; quarter index 0/default yields `0x00001e14`; play-time increment reaching 18 uses low byte `0x30` unless late-half branch wins. Stop clock, discard low byte at play end, separate selection timer **9** first quarter / **5** later; trace its helper, not seconds guessed from the labels. |
| T2 periods/halftime/overtime — L/V, V/P | `nfl.c:eoq_handling/start_new_quarter/game_over`, L1982–2101, L2448–2463; `include/GAME.H` L218–238 | Finish the live play and any required conversion after clock expiry. At halftime the other initial-possession team receives a kickoff. Tied regulation enters OT; maximum **three** overtimes, tied third OT may end the game. Test conversion skip only under L875–883's time/quarter/lead conditions, tied/untied final and OT kickoff alternation; do not import conventional NFL sudden-death rules. |
| S1 scoring/PAT/2pt — L/D/V, V/P; TD/safety partial | `nfl.c` L1732–1818, L875–903; `playsel.c:play_select` L1241–1255, L1342–1368, L1489–1502; `result.c:_pat_*` | TD **6**, safety **2**, conversion **2**, FG **3**, PAT safety **1** to opponent. Free PAT adds **1** unless source miss branch: human lead >14, quarter index >2, draw <2/100; CPU lead >8, index >1, draw <3/100. Include failed conversion, defensive possession and period-ending exceptions, not automatic seven. |
| S2 kick/punt/restarts — L/D/V, V/P | `nfl.c` kickoff branch L905–1173, result branches L1811–1891; `player.c:punt_ball/kick_ball/set_punt_ball_vels/set_field_goal_ball_vels/ball_proc`; `result.c:_norm_deadball/_norm_oobball/_kick_*` | Opening/post-score/halftime/OT kickoffs, quick kick, onside, punt/fake, FG/fake/block/miss/return, touchback and safety restart. Quick-kick setup **40**, touchback **20**, untouched kickoff OOB receiving LOS **40**. Verify actual dispatch: empty `_fg_*`/`_punt_*` result handlers are not invented kick logic; normal-play ball type classifies those outcomes. No universal own-20 restart. |
| P1 playbooks/routes/defense/audibles — L/D/V, V/P | `PlayStuf.c:off_slots/def_slots/play_pages/PlayStuf__SetupCurrentPlayPages/PlayStuf__ResetAudibles`; `playsel.c:PlaySel__MakeChoice/cpu_pick_play`; `playbook.c`; active `drone.c:_drone_qb` **L1163**, `_drone_wpn/_drone_db/assign_blockers/drone_cover_assign` | Enumerate reachable source pages/slots, including specials/custom/audibles; run every formation/route opcode and man/zone/blitz assignment with pinned inputs/RNG. Example `formation_stack` first lineman **(-12,-17.5)** source units and `off_slots[0]` **ZIG ZAG** → `play_zigzag`; Run/QuickPass/Cover/Blitz are not source play IDs. Earlier `_drone_qb` at L940 is disabled. |
| D1 teams/rosters/stats/materials — L/D/V, V/P; all31 source/data partial | `include/GAME.H` L220–223; `teamdata.c`; `player.c:load_uniform/draw_player` L6816–6928; `plyrinfo.c`; `nfl.c` stats updates | **31 teams**, **16** static player records/team, seven on-field actors/side; not 32 teams or one Bears body for everyone. Verify all reachable roster assignments, ratings/scale/body/skin/home-away uniform, held-ball model and one/two-digit jersey overlay. Number digits come from packed nibbles, with zero mapped to atlas slot 9. Match gameplay and final statistical accounting, not just appearance. |
| I1 input/multiplayer — L/V, V/P; raw cabinet adapter partial | `player.c:player_proc` input edge logic, `_mode_*`, `audible_check`; `include/GAME.H:MAX_PLYRS/plyr_control`; `playsel.c:play_select` | Two/four-station configuration up to **four humans**, same-side cooperation, defensive/receiver switching, A/B/C simultaneous held/edge state and double taps. Preserve source command order; the new raw adapter and menu checks do not close every live-action/browser case. Browser focus-release behavior remains mandatory; network play is not inferred from cabinet stations. |
| V1 field/camera/HUD/audio — D/V and coupled L, V/P; Team/HUD/impact source partial, world separately inspected | `field.c`; `cambot.c` active mode table; `player.c:last_player_proc`; `plyrinfo.c`; `sounds.c:catch_sounds/fumble_ball_sounds/field_goal_speech`; source `models/`, `img2d/`, `img3d/`, `sounds/` | Original field/stadium/materials, full role/action animation, active source camera transitions and special plays, clock/score/play pages/markers/turbo bars/messages and event-timed music/crowd/announcer. Validate audio ID/bank dispatch plus actual decoded playback. Dormant replay data is not an active feature: `CamData.c` places `csi_replay1` under `#if 0`, and active `cambot.c` playback is false. Do not invent generic tackle replay/slow motion. N64 screenshots guide appearance; they do not establish identical arcade edition/pixels. |
| F1 gameflow/records/configuration — L/D/V, V/P | `nfl.c:InitGameProc/eoq_handling/game_over`, `mainproc.c`, `PlayStuf.c`, `include/game_cfg.h`; source record/replay/cap call sites | Team/roster/play selection → complete match → intermissions/stats/OT/final → next match, source user configuration, reachable custom plays/power-ups/records/replay. Trace the actual callees before marking their cases verified. Exclude cabinet accounting, disk audits, diagnostics and hardware drivers; retain their gameplay-facing settings, human participation and continuation decisions with a documented free-play adapter. |

### Execution order and closing evidence

1. **Recorded live increment, October 16:** the selected56
   `original-wan-clip/v1` library, injected source simulation and explicit
   actor-facing animation inputs feed the continuous renderer through the
   [shared boundary](architecture.md#full-source-animationgameplay-boundary).
   The56/browser increment is partial A1 evidence, not full animation parity.
   The newer B1/canonical117, Team/impact and HUD/cabinet source layers and
   packager have the separate pure/API evidence above; they have not replaced
   that live generation or obtained combined native/browser acceptance.
2. **Remaining core integration:** complete the packed clock/snap,
   `result.c` classification and `nfl.c` spotting/downs/scoring/PAT/restart/period
   caller transitions (T1/T2/R1/S1/S2) around the now-integrated helpers, then
   movement/contact/catch/loose-ball and source
   AI/playbooks (M1/C1/C2/B1/B2/P1). Data/UI/audio/flow work remains required and
   may run in its owner's independent files; this ordering is not a scope cut.
3. Quality's permanent [original-C oracle](../tests/source-oracles/README.md)
   now executes the unchanged verified result table, exact spotting block,
   clock wakes/discard, CPU onside and geometry seams. Extend independent
   expectations to the remaining result switch, PAT/kick/period callers,
   animation interpreter and actor/ball/AI branches. Preserve pinned source
   hashes/line spans and explicit adjustment/RNG inputs; scheduler stubs must
   preserve sleep/order and record calls, not stand in for an original engine run.
4. Each row closes only with linked source path/blob/symbol/build flags,
   discriminating expected/observed traces, actual production C# tests (no
   skipped/empty inventory), mutation rejection, and native/browser evidence
   where relevant. Trace source tick, process/actor order, state, RNG draws,
   decision/script/frame, ball/possession and ordered events. Declare float
   tolerances per measured operation; do not replace exact decisions with
   generous position tolerances.
5. **Full completion requires every row implemented/tested**, no silent
   approximate/static fallback, and a fresh Docker-served browser complete
   match plus special-case sessions covering defense, turnovers, all scoring/
   kicks, halftime, OT and final restart, multiplayer/focus and actual
   animation/audio/HUD. Retain build receipt/payload hashes, frame/video and
   trace evidence. Neither the legacy 156 core tests nor the new bounded
   oracle/animation evidence certifies this target.

### Concrete engine/tool dependencies, not a missing-data waiver

`nfl.lnk` links `lib/select.a`, `lib/ani3d.a`, `/video/lib/libc.a`,
`goose.a`, `glide.a` and `sound.a`; the root makefile names `ccmips`,
`asmmips` and `psylink` plus platform sound libraries. These are not Unity dependencies:
port the reachable Goose process/sleep/run-level/input/RNG semantics to the
deterministic C# scheduler, use pure source joint transforms for gameplay,
adapt Glide drawing to Unity, and map source sound calls to decoded bank cues.
Never require a historical MIPS executable to run in the browser.

`anim/MAKEFILE` actively runs `wan2seq -s 0.12898` and `seq2ani`; the pinned
external `historicalsource/midwayvideo` revision above exposes `tools/wan2seq`,
`tools/seq2ani`, `tools/3df2wms`, `tools/mwms2wms`, `tools/sdl2seq` and
`tools/reseq`. Use those original sources as offline format/schedule oracles;
regenerate selected frame lists/sequence identities from the appropriate
`anim/seattle.mak` or Vegas list, rather than guessing absent generated
`genseq.h/.ani` or delta lookup data. Full-quaternion WAN export already avoids
the compressed-runtime lookup dependency.

The same tools revision also contains `lib/goose/process.c`, `randper.c`,
`switch.c`, `include/goose`, `include/glide` and `lib/sound/sound.c`.
Verified `process.c` blob `967ca53910f24ddfa48a14485113a8fd0c989959`
sorts ascending run level; `include/id.h` orders camera `0x100`, audibles
`0x200`, players `0x300`, cursors `0x400`, collisions `0x500`, ball `0x600`
and replay `0x900`. Equal-level insertion/dispatch still needs tracing.
Verified `randper.c` blob `56fcf55f38cf1421a4206e6a101f98586dc430d5`
implements `randrng` through the platform `random()` remainder (zero range
returns zero). Current B1 composition labels its null-factory legacy-LCG preview
RNG **ExternalUnverified**; it is not a verified platform `random()` sequence.

No demonstrated blocker prevents continuing the remaining source port.
Exact Goose equal-run-level ordering/RNG, other platform animation selections,
complete source asset conversion and gameplay `.BNK/.SND` dispatch/DSP3 playback remain
**unverified dependencies**, not proven absent data and not accepted substitutes.
The offline audio decoder/reader is a bounded preparation layer, not the missing
source sound-selection RNG, actor-stage timing or actual game-audio consumer.
The owner must pin a specific missing symbol/format/call and attempted original
tool path before calling it unresolvable; missing audio would block **V1/full
acceptance**, not clocks, rules or animation development.

## Visual target correction and asset audit

The original Yard Rush procedural presentation did not meet the requested
appearance. It remains available only as `?actors=graybox`; the current source
path is real mesh/rig/texture conversion, not renamed primitives. The field and
analytic HUD in the older live preview still do not meet the original visual
target. New authored-HUD/team source code does not constitute new rendered proof.

The user's [actual reference image](https://www.nintendo64ever.com/photos/jeux/Photo-Jeu-Nintendo-64-Fiche234,10244.png)
was viewed: close perspective, large helmeted/textured articulated players with
uniform numbers, field/stadium backdrop, compact upper-left score, large return
banner and bottom player bars. The distant minifigures and analytic HUD do not
match it. Its gallery identifies Nintendo 64; this archive's makefile targets
Seattle/Vegas arcade hardware. Identical edition/art/tuning is **not** established.

### Actual player, material and pose data

The following mesh/material/native-pose facts describe the historical
single-player control and older56 live generation. Current all31 Team/117 source
requirements and their distinct evidence are recorded above.

- **Geometry/rig:** [`models/plyr266.h`](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/models/plyr266.h#L997-L1173)
  and [`models/skeleton.h`](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/models/skeleton.h#L180-L197)
  now produce actual Unity rigid-limb meshes and pivots. Bind the pelvis-first
  `model266_limbs` mapping, not the hand-first generated array. Packed
  vertex/texture offsets, UV reuse, seams and signed-axis/winding conversion are
  explicit; this is not a guessed skinned mesh.
- **Textures:** `player.c:load_uniform` L10574–10624 maps the selected normal-body
  Chicago home / `PC_WHITE` player to `ARMHND.WMS`, `HEDSHU.WMS`, then the **same
  `BEAHLOD.WMS` atlas** for the three uniform slots. The one-player control recipe uses the
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
  historical captures visibly show heads above bodies and feet below, without
  an invented axis flip or pose. The [frame-0 diagnostic](build.md#apply-qbstance-frame-0)
  reproduces the native single-pose check, not that historical browser generation;
  current source mode requires the canonical117 composition. At the October 15
  milestone it was one ball-holding stance applied to all fourteen actors,
  **not verified pre-snap for all roles**.
- **Recorded56 continuous original animation, October 16:** 56 content-pinned selected
  clips drive explicit source-clock script/frame/action state, independent
  lower/upper headings and resolved root offsets. The renderer consumes that
  state, not velocity-selected generic motion or one static QB pose. That older
  generation reuses the one Chicago / `PC_WHITE` visual variant. Complete selection,
  transition/root and source-numeric equivalence remain open.

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

The following baseline limitations describe the older normal-camera/live-preview
layer. The new source-stage/impact extension is qualified separately below.

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
  the original scheduler/velocity integrator. The host bridge remains fixed
  60 Hz; the injected source simulation executes 57 source ticks per 60 calls.
  Focus loss/restart/rewind/long gaps realign without advancing gameplay.
- HUD-aware viewport and conservative silhouette fitting may pull farther back
  than the source baselines. Aspect expansion and Unity projection are browser
  adaptations. Special/replay/attract/cinematic modes, shake and exact hardware
  clipping/timing are not ported; N64 pixel orientation is not certified.

The checked-in Team extension now samples the normal/impact producer once at
the actual pre-actor camera stage, preserving Core's instruction ordinal,
canonical source roots and ordered shared X-then-Z shaker draws. All catch-up
events/cues are forwarded; presentation has one lifecycle owner. Source-stage
rendering uses cached camera state and the full content viewport, not the old
HUD-reserved rect or a second render clock. Original active special tables and
shaker math have source/pure evidence, but full raw normal/kickoff/gopher branches,
target scheduler/RNG, native pixels and hardware cadence remain open.

### Available but not fully integrated

`models/field2.sdl` and `models/stadpie.sdl` have a strict original-world adapter
and isolated native/browser inspection. The current field adapter can load those
Resources, but the new combined B1/Team/HUD game has no native/browser proof.
Missing generated headers do not mean missing art: original HUD authoring pixels,
fonts and selection planes are now decoded and the explicit packager exists.
Their compiled resource import/reload/GPU-pixel acceptance remains unexecuted.
The archive contains WANs beyond canonical117 and original audio payloads; the
offline audio decoder is not a completed archive-wide/game-audio pipeline.
A complete arcade rebuild
still needs external MIPS/Goose/Glide/sound
dependencies and generated outputs. The bounded Unity conversion does not
require reconstructing that executable.

## Provenance and local data

The repository's publication surface is code, conversion recipes and original
synthetic tests. Original source/art, converted textures/meshes, decoded poses,
clips/inventories, builds and captures remain ignored under the source cache,
`.squad/.scratch/`,
`Assets/Imported/` and local artifact directories; they persist on disk. The
preparation tool retains pinned hashes and upstream notices, including the
restrictive notice in `cambot.c` L6–12. Public repository availability is **not an
original-art license**. No license declaration or automatic art/image upload is
part of this port path.
