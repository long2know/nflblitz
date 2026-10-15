# Source evidence and first-playable gaps

**Checked October 15, 2026.** Reference: public
[`historicalsource/nflblitz2000`](https://github.com/historicalsource/nflblitz2000).
Pinned default-branch revision:
[`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`](https://github.com/historicalsource/nflblitz2000/commit/89f840d58d152a2a9f230cb8d0cdffbd554c8ded),
commit timestamp October 18, 2023, 18:44:47 UTC. The GitHub tree response was not
truncated. This is a source reading, not a successful original-game build or a
claim about every shipped version/configuration.

All links below pin that revision; line numbers refer to the raw source's physical
lines. Small source-only reference files and metadata are cached under ignored
`.squad/.scratch/reference/nflblitz2000/`. No clone, assets, original C translation
unit, or historical data table is included in the game.

## Current implementation evidence

The original C# kernel and contracts are implemented. On October 15, 2026,
`python3 tests/run-core.py --no-restore` compiled the exact production files as
C# 7.3 against .NET 10 and passed **156/156 cases, zero skipped**, with the expected
case inventory and unchanged production SHA-256 hashes. The assertions cover the
bounded rule/data portions of the table below; see [coverage and commands](../tests/README.md).
This is not Unity's .NET Standard 2.1 profile certification.

| Evidence layer | Observed result | Not established |
| --- | --- | --- |
| Headless rule/interaction/replay suite | Fourteen stable actors, phase timing, passing/catch, defensive control/tackle, thirty-yard/four-down rules in both directions, TD/safety, spotting, four periods/final/restart, detached snapshots and ordered deterministic events pass. | Historical AI/physics parity, rendered primitive actors, Unity compilation or browser behavior. |
| Separate public-API-only match smoke, seed 1 | Source-owner smoke reached `GameOver` after 42,522 ticks/four ended quarters, observed scores and Home defensive play, then verified `Restart`. No internal clock mutation or artificial match cutoff. | A rendered match, keyboard controls, browser final/restart or historical score balance. |
| Test-tool checks | Six result-inventory guard tests and eight browser-helper tests passed. An isolated actual-source thirty-to-ten-yard mutation produced expected behavioral failures without changing shared sources. | Running the preparatory browser driver against a real player. |
| Unity/WebGL/browser integration | Pending a real build and observed browser result; presentation/project files are not part of the core increment. | Playable-browser, hosting-gameplay, performance or whole-game fidelity claims. |

## Verified source facts → bounded implemented rules

Rule/data targets below are **implemented and headless-tested** within this slice.
Presentation, original-game equivalence and browser observation remain separate;
for example, fourteen tested actor records do not prove fourteen rendered players.

| Behavior | Primary evidence: exact path, symbols and lines | First-playable target / qualification |
| --- | --- | --- |
| Seven per side | [`include/GAME.H` L218–226](https://github.com/historicalsource/nflblitz2000/blob/89f840d58d152a2a9f230cb8d0cdffbd554c8ded/include/GAME.H#L218-L226): fourteen actors divided by two. | Fourteen original primitive actors; not eleven-a-side NFL. |
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

Only the bounded normal-play classifiers/rule transitions above were traced.
Archive completeness, hardware/library dependencies, asset provenance and
redistribution permission are unverified. Some source headers contain restrictive
notices; a public GitHub repository is not a permission finding. Study behavior,
write original C#, use original primitives/colors/text, and do not import original
C, play tables, logos, textures, models, animation, audio or license material.
This is a risk boundary, not legal advice.

Keep **implemented**, **headless-tested**, and **browser-observed** separate with
reproducible cases. No row is yet browser-observed. Do not relabel this bounded
kernel as full game/source parity because its rule suite passes.
