# nflblitz

A Unity/C# source-based port targeting the **original NFL Blitz 2000 look and
game logic**, running client-side in a browser. The procedural graybox
is a diagnostic baseline, **not the visual target**. This is an incomplete port,
not a finished or whole-game-parity recreation.

## Checked-in source versus the recorded live player

The checked-in **Contact/C2 source increment** is a bounded portable-source and
resource-binding milestone, **not a new playable build or whole-game acceptance**.
The absent/empty runtime selector still chooses C1
`original-teams-contact272` (**272 clips / 24,892 selected frames**).
`original-teams-contact281-c2` (**281 clips / 26,230 frames**) is explicit opt-in:
`?actors=source&sourceprofile=original-teams-contact281-c2`.
Conflicting built/URL selections and missing or mismatched resources fail
visibly; there is no automatic promotion or old-asset fallback.

Both profiles require real, pinned animation/team/impact/HUD/match/movement/DIP
inputs. C2 additionally requires its own callback JSON, ONE7538 hit SAFN and
**477-site** impact catalog, retaining the separate unchanged six-binding SAFN.
See the [current preparation and resource contract](docs/build.md#current-contactc2-source-mode-resources)
and [strict-input versus asset-free checks](tests/README.md#current-contactc2-gates).
Historical 56/117/156/157/272 identities remain distinct; preserving them does
not make every historical composition runnable on the new engine.
Nonempty sound output without a real DSP consumer fails visibly—**nothing is
claimed played**. Source/pure/reference-API checks are not native import, game
pixels, a Unity build or browser proof. Three legitimate Editor attempts exited
198 **before import** because headless entitlement was unavailable.

**Portability disclosure:** the parent autonomously selected a narrow
no-eligible-candidate return before original `maybe_taunt` calls
`bearing(me,-1)`; **no explicit user approval was captured**.
`PORTABILITY_REPAIR:NO_ELIGIBLE_CANDIDATE` records that repair. Eligible paths
remain unchanged and invalid/unknown inputs still error. This is not
target-memory, instruction or ABI emulation.

**Recorded live-generation proof, October 16, 2026 (PDT):** original player
mesh/rig/textures run with **continuous original animation: 56 clips / 3,731
selected source frames**.
Unity **6000.3.24f1** host build `0000063700005f540000316b000003e7` is promoted
at Docker `8080/?actors=source`. Native vertex comparisons and a real
**219.875-second browser recording** prove the bounded increment: twelve actors
with non-rigid motion, eight observed actions and zero runtime errors.
That is the older live preview, not evidence that the new Contact/C2 source
has been imported, built or deployed. Its payload and captures are not replaced
or relabeled by the new headless/API results. **All sixteen full-parity rows
remain OPEN.** The ledger also retains the October 15 static-pose/title history.

## Import, build and run

Paths are relative to the **project root**. Offline preparation can use the
checkout; native source imports/builds require the approved isolated candidate
described by the packaging recipe.

1. Install Unity **6000.3.24f1** with WebGL Build Support and provide an existing
   externally activated editor environment. Docker with Compose serves the result.
2. Follow the [current Contact/C2 preparation/import order](docs/build.md#current-contactc2-source-mode-resources)
   in an approved, isolated candidate using explicit pinned original inputs:
   - Prepare [Contact272 from the unchanged true157 base](tools/animation/README.md#c1-production-successor-explicit-contact272).
     The base retains `R_SKIP`'s qualified converter-state replay, not a guessed
     missing-hand pose. For C2, explicitly supply the pinned nine-clip bundle
     and use the [canonical281 and impact producers](tools/animation/README.md#explicit-c2-production281-generation).
     These commands do not retrieve original data or manufacture absent bundles.
   - Prepare/import [all 31 original teams and the selected profile's impact data](tools/assets/README.md),
     including actual rosters, model/skin/uniform/BCD-number bindings. Use the
     matching Contact272 or Contact281C2 importer, never a relabeled 157 resource.
   - Prepare the [original HUD/cabinet data](tools/interface/README.md), then
     [package the HUD and match services](tools/interface/packaging.md) with the
     actual P1 catalog and an explicitly chosen, SHA-pinned configuration,
     movement bootstrap and unchanged old six-binding SAFN.
     Teams, seven roster records/side, stations, possession, captain, home side,
     heads and power-up settings are not inferred defaults. M1 additionally
     requires pinned `SourceData/movement` and a separate
     `original-source-movement-bootstrap/v1` file: explicit field0..6,
     three team masks0..3, global speed/violence booleans and original
     DIP512×256 or512×384. Browser dimensions are not source resolution.
     C2's saved initializer additionally requires its two distinct canonical
     callback/hit TextAssets; an old service resource is not silently upgraded.
   - Import the [original world](tools/environments/README.txt) separately when
     requiring `environment=source`.

   A clean checkout has **no original art, compiled Resources or player build**.
   Original inputs and derivatives stay ignored/local. The old
   [single-player import](docs/build.md#prepare-and-import-a-local-source-player),
   [56-clip recipe](docs/build.md#prepare-and-import-continuous-original-animation)
   and [frame-0 pose](docs/build.md#apply-qbstance-frame-0) remain diagnostic/
   historical controls, not the new source-mode setup. The
   [movement157](docs/build.md#current-movement157-source-mode-resources) and
   [B1/final117](docs/build.md#current-b1-source-mode-resources) recipes remain
   separate immutable historical source compositions.
3. **Only after** legitimate isolated imports and resource verification, build
   and serve from that complete candidate's project root:

   ```sh
   export UNITY_EDITOR="/absolute/path/to/6000.3.24f1/Editor/Unity"
   bash scripts/build-webgl.sh --host
   bash scripts/serve-webgl.sh
   ```

   The existing wrapper calls ordinary `WebGlBuild.Build`, retaining the C1
   default; it has no C2 selector flag. Explicitly select the installed C2
   resources by URL below, or use the separately documented future fixed-C2
   Editor entrypoint. Host mode preserves the caller's `HOME`/`XDG_*`
   activation environment.
   Serving builds the static runtime image locally and runs in the foreground;
   Ctrl-C stops it. See [build, activation and generation proof](docs/build.md).
4. For a newly built ordinary candidate, `http://127.0.0.1:8080/?actors=source` **requires**
   the complete Contact272 resources. Explicit C2 uses
   `http://127.0.0.1:8080/?actors=source&sourceprofile=original-teams-contact281-c2`
   and its complete, separately keyed C2 resources. Missing or
   invalid inputs show `SOURCE MODE ERROR`; no generic font, matching-uniform,
   static-player or graybox substitute is installed.
   `http://127.0.0.1:8080/?actors=graybox` explicitly selects LegacyBaseline60Hz.
   Without a parameter, installed original-team resources may select source,
   but missing resources do **not** implicitly select legacy. Resource/code
   changes require a new build; an older exported player cannot load them.

The resource set is necessary, not a finished-game guarantee: original audio/FX
consumers remain unbound and fail explicitly when reached. Do not bind noops,
silence or fake clicks. Canonical M1 observed 12,000 host/11,400 source ticks but
**zero `R_SKIP` frames**; the required multi-ordinal/pose witness remains open.
B1's FIFO compatibility and unfiltered raw99-at-every-site positive catch are
separately declared controlled inputs, not unchanged original trajectories or
target PRNG. The seed1 standing-BatDown negative remains. See the
[current evidence and limits](docs/source-parity.md#current-contactc2-boundary).

For interactive editing, open `Assets/Scenes/Boot.unity`. Never run concurrent
editors/imports/builds against the same project and `Library/`.

### Controls

#### Source cabinet controls

Focus the canvas. Bindings are raw original **switches**, not universal named
game actions. Only configured stations participate; station ID, team side,
controlled player and captain slot are different identities.

| Station ID | Up / left / down / right | A / B / C |
| --- | --- | --- |
| 0 | W / A / S / D | V / B / N |
| 1 | arrows | Delete / End / PageDown |
| 2 | I / J / K / L | U / O / P |
| 3 | T / F / G / H | Z / X / C |

During original play selection, the configured side's captain uses the stick
for the cursor, **C** for the next page, **B** to select, and **A** to flip
offense. The original initial twenty-source-tick sleep is preserved.
Simultaneous buttons retain all held/pressed/released bits; combined newly
pressed menu masks take the original no-action branch, not button priority.
Live-play interpretation belongs to the source Core, not the legacy table below.

WebGL standard-mapped gamepad index equals station ID: axes 0/1, buttons
0→A, 1→B, 5→C. Non-standard mappings fail explicitly. Native gamepads require
the explicit enabled setting and verified `SourceStation{1..4}X/Y` axes; they
are not enabled merely by connecting a controller. See the complete
[49-way host adapter and binding contract](tools/interface/README.md#cabinet-bindings).
Footer A/B/C regions emit raw mouse pulses; there is no full touch-movement
controller. **R** is an administrative restart, not a cabinet switch.
Focus loss/disable/reset clears pending input and requires physical neutral
before rearming, without replaying held actions.

#### Explicit graybox / legacy controls

These fixed semantic controls apply only to `?actors=graybox`, not the new source
cabinet profile. The older live56 recording also predates the new cabinet adapter.

| Input | Legacy action |
| --- | --- |
| 1 / 2 during selection | Run / Quick Pass on offense; Cover / Blitz on defense |
| Space | Snap Home offense or continue after a dead ball; Away offense auto-snaps |
| WASD / arrows | Move; W / Up = +X, D / Right = +Z |
| Hold Shift | Turbo |
| 1 / 2 / 3 during live Home offense | Pass to eligible receiver 4 / 5 / 6 |
| Q / F on defense | Switch Home defender / tackle |
| R | Restart with seed 1 from any phase |

Legacy contextual buttons use those legacy commands.

## Run the tests

Headless prerequisites: Python 3.10+ and .NET SDK **10.0.100**. From the repository
root, with no importer/editor writing assets:

```sh
python3 tests/run-core.py
python3 tests/assets/run.py
python3 tests/camera/run.py
python3 tests/test-unity-metadata.py
python3 -B -m unittest discover -s tools/assets -p 'test_*.py'
python3 -B -m unittest discover -s tools/animation/tests
python3 -B tests/interface/run.py
python3 -B tests/interface/stage_followup.py
python3 -B tests/presentation/run.py
python3 -B tests/interface/movement_bootstrap.py
python3 -B tests/audio/runtime/run.py
python3 -B -m unittest discover -s tests/audio/runtime -p 'test_ci.py' -q
python3 -B tests/presentation/contact-runtime/run.py
python3 -B tests/presentation/c2-runtime/run.py
```

See [core/metadata coverage](tests/README.md), [asset regressions](tests/assets/README.md),
[camera regressions](tests/camera/README.md),
[HUD/cabinet checks](tests/interface/README.md),
[complete-owned-UI caller/API guards](tests/interface/STAGE.md),
[team/impact checks](tests/presentation/README.md), and the
[browser harness and recorded evidence](tests/browser/README.md). Browser tests
need Node 24, Chrome and Tesseract. The older orthographic/static-player smoke,
including the `qbstance-frame0` marker, is not continuous-animation acceptance;
see the [recorded56 boundary](docs/build.md#continuous-animation-evidence--october-16-2026).
Additional [original-C oracle and production comparisons](tests/source-oracles/README.md)
require explicitly pinned source inputs and GCC/Clang; their bounded clock/rule
coverage is separate from animation/browser acceptance.

## What remains

- The old live56 generation reuses **one normal-body Chicago home /
  `PC_WHITE` variant** and its approximate HUD. Checked-in source now has
  31-team/20-head/86-texture presentation and original bitmap HUD/selection
  composition, but no new native or browser proof of those combined resources.
  Complete original action/selector/root-transition parity remains open.
  Throw/GetUp clips have old native checks, not observed actions in that recording.
- The [original field/stadium adapter](tools/environments/README.txt) has separate
  original-C, native and world-only WebGL proof; actual game integration,
  lighting and combined-game visual acceptance remain open. The source reference
  is arcade code;
  edition equivalence with the supplied N64 screenshot is unverified.
- Thirty yards in four downs, six-point touchdowns and two-point safeties have
  source-traced tests. The injected source profile adds packed clock/rules/
  PAT/kick/period helpers and a 60-host/57-source adapter; complete caller flow,
  AI/routes/RNG, contact/ball behavior and full playbooks remain open. Fixed
  live-time quarters and own-20 restarts describe the separate legacy baseline.
- Full browser match/final restart, new cabinet/focus/DPR layout in the actual
  game, original audio/FX consumers and representative hardware performance
  remain unqualified. Pure/API success is not new pixels or a fresh deployment.

Current component evidence includes all 61 Contact/C2 causal controls,
34,570 kernel assertions, 963 actual package assertions/14 compiled controls,
and all five actual archived Unity6000.3.24 C#7.3/.NET Standard2.1 reference
profiles over 112 sources (99 runtime/13 separate Editor), zero diagnostics.
These are recorded source/component results, not full-M1, native/DSP,
all-sixteen-row or new playable-build acceptance.

The [source-parity ledger](docs/source-parity.md) records the pinned references,
provenance boundary and exact evidence limits. Original inputs, converted art,
pose/clip data, builds and captures stay local and ignored, but persist on disk.
The [architecture contract](docs/architecture.md) documents the Unity runtime and
single-writer boundaries. Docker serves static files; no React, API or backend is
required. Team ownership is in the [roster](.squad/team.md) and
[routing rules](.squad/routing.md).
