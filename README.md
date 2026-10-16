# nflblitz

A Unity/C# source-based port targeting the **original NFL Blitz 2000 look and
game logic**, running client-side in a browser. The procedural graybox
is a diagnostic baseline, **not the visual target**. This is an incomplete port,
not a finished or whole-game-parity recreation.

**Verified October 16, 2026 (PDT):** original player mesh/rig/textures now run
with **continuous original animation: 56 clips / 3,731 selected source frames**.
Unity **6000.3.24f1** host build `0000063700005f540000316b000003e7` is promoted
at Docker `8080/?actors=source`. Native vertex comparisons and a real
**219.875-second browser recording** prove the bounded increment: twelve actors
with non-rigid motion, eight observed actions and zero runtime errors.
**Full gameplay parity remains OPEN.** The evidence ledger retains the
October 15 static-pose/title milestones as history, not current animation state.

## Import, build and run

Run commands from the **repository root**, which is the Unity project.

1. Install Unity **6000.3.24f1** with WebGL Build Support and provide an existing
   externally activated editor environment. Docker with Compose serves the result.
2. Follow [offline source preparation and native import](docs/build.md#prepare-and-import-a-local-source-player)
   with your supplied pinned original source tree. Python 3.9+ prepares the
   ignored local player; a clean checkout contains **no original art or player
   build**. Then complete the required
   [continuous-animation preparation/import](docs/build.md#prepare-and-import-continuous-original-animation)
   from the same pinned source root before building. The optional
   [QBSTANCE frame-0 diagnostic](docs/build.md#apply-qbstance-frame-0) reproduces
   only the historical native single-pose check, not the current animation setup.
   The identity/authoring bind pose remains debug-only.
3. Build and serve:

   ```sh
   export UNITY_EDITOR="/absolute/path/to/6000.3.24f1/Editor/Unity"
   bash scripts/build-webgl.sh --host
   bash scripts/serve-webgl.sh
   ```

   Host mode preserves the caller's `HOME`/`XDG_*` activation environment.
   Serving builds the static runtime image locally and runs in the foreground;
   Ctrl-C stops it. See [build, activation and generation proof](docs/build.md).
4. Open `http://127.0.0.1:8080/?actors=source` to **require** the local source
   prefab **and animation library**. Missing/invalid source assets fail explicitly;
   there is no static or primitive fallback.
   `http://127.0.0.1:8080/?actors=graybox` selects the baseline.
   Without a parameter, an installed source prefab takes precedence; otherwise
   the baseline runs. Player/animation changes require a new build before serving.

For interactive editing, open `Assets/Scenes/Boot.unity`. Never run concurrent
editors/imports/builds against the same project and `Library/`.

### Controls

Focus the game canvas for keyboard input. Source mode begins with an opening
kickoff before normal selection. The recorded animation actions and historical
static controls smoke are bounded evidence, not complete defensive, all-action,
focus-release geometry or full-match qualification.

| Input | Action |
| --- | --- |
| 1 / 2 during selection | Run / Quick Pass on offense; Cover / Blitz on defense |
| Space | Snap Home offense or continue after a dead ball; Away offense auto-snaps |
| WASD / arrows | Move; W / Up = +X, D / Right = +Z |
| Hold Shift | Turbo |
| 1 / 2 / 3 during live Home offense | Pass to eligible receiver 4 / 5 / 6 |
| Q / F on defense | Switch Home defender / tackle |
| R | Restart with seed 1 from any phase |

Contextual in-player buttons use the same commands; there is no full mobile
touch-movement controller.

## Run the tests

Headless prerequisites: Python 3.9+ and .NET SDK **10.0.100**. From the repository
root, with no importer/editor writing assets:

```sh
python3 tests/run-core.py
python3 tests/assets/run.py
python3 tests/camera/run.py
python3 tests/test-unity-metadata.py
python3 -B -m unittest discover -s tools/assets -p 'test_*.py'
python3 -B -m unittest discover -s tools/animation/tests
```

See [core/metadata coverage](tests/README.md), [asset regressions](tests/assets/README.md),
[camera regressions](tests/camera/README.md), and the
[browser harness and recorded evidence](tests/browser/README.md). Browser tests
need Node 24, Chrome and Tesseract. The older orthographic/static-player smoke,
including the `qbstance-frame0` marker, is not continuous-animation acceptance;
see the [current recording boundary](docs/build.md#continuous-animation-evidence--october-16-2026).
Additional [original-C oracle and production comparisons](tests/source-oracles/README.md)
require explicitly pinned source inputs and GCC/Clang; their bounded clock/rule
coverage is separate from animation/browser acceptance.

## What remains

- All fourteen source-mode actors reuse **one normal-body Chicago home /
  `PC_WHITE` variant**. Jersey-number overlays and other rosters/skins are not
  ported. Continuous clips are implemented, but complete original action/
  selector/root-transition parity is not. Throw/GetUp clips have native checks,
  but those actions were not observed in the recorded browser run.
- The field/stadium, lighting and HUD remain approximations, not original assets
  or N64 pixel fidelity. The source reference is arcade code; edition equivalence
  with the supplied N64 screenshot is unverified.
- Thirty yards in four downs, six-point touchdowns and two-point safeties have
  source-traced tests. The injected source profile adds packed clock/rules/
  PAT/kick/period helpers and a 60-host/57-source adapter; complete caller flow,
  AI/routes/RNG, contact/ball behavior and full playbooks remain open. Fixed
  live-time quarters and own-20 restarts describe the separate legacy baseline.
- Full browser match/final restart, perspective focus-release geometry and
  representative hardware performance remain unqualified.

The [source-parity ledger](docs/source-parity.md) records the pinned references,
provenance boundary and exact evidence limits. Original inputs, converted art,
pose/clip data, builds and captures stay local and ignored, but persist on disk.
The [architecture contract](docs/architecture.md) documents the Unity runtime and
single-writer boundaries. Docker serves static files; no React, API or backend is
required. Team ownership is in the [roster](.squad/team.md) and
[routing rules](.squad/routing.md).
