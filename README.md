# nflblitz

A Unity/C# source-based port targeting the **original NFL Blitz 2000 look and
game logic**, running client-side in a browser. The procedural graybox
is a diagnostic baseline, **not the visual target**. This is an incomplete port,
not a finished or whole-game-parity recreation.

**Verified October 15, 2026 (PDT):** original player mesh, skeleton and textures,
with authentic static `QBSTANCE` frame 0, passed native apply/reload and a
bounded Docker-served desktop/portrait standing-player visual-controls check.
The source-derived normal camera and Unity **6000.3.24f1** host WebGL builds also
passed. The latest `NFL BLITZ 2000` / `UNITY SOURCE PORT / IN PROGRESS`
generation has fresh desktop/portrait controls captures and a visual review of
the current title and field-axis legend. The evidence ledger separates those
results from the exact tiny-font legend OCR diagnostic, which remains a harness
limitation.

## Import, build and run

Run commands from the **repository root**, which is the Unity project.

1. Install Unity **6000.3.24f1** with WebGL Build Support and provide an existing
   externally activated editor environment. Docker with Compose serves the result.
2. Follow [offline source preparation and native import](docs/build.md#prepare-and-import-a-local-source-player)
   with your supplied pinned original source tree. Python 3.9+ prepares the
   ignored local player; a clean checkout contains **no original art or player
   build**. Complete the **required**
   [QBSTANCE frame-0 apply/verify step](docs/build.md#apply-qbstance-frame-0)
   before building to reproduce the correct standing preview. The identity/
   authoring bind pose is debug-only, not acceptable game posture.
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
   prefab. Missing/invalid source assets fail explicitly; there is no fake
   primitive fallback. `http://127.0.0.1:8080/?actors=graybox` selects the baseline.
   Without a parameter, an installed source prefab takes precedence; otherwise
   the baseline runs. Import/pose changes require a new build before serving.

For interactive editing, open `Assets/Scenes/Boot.unity`. Never run concurrent
editors/imports/builds against the same project and `Library/`.

### Controls

Focus the game canvas for keyboard input. The bounded source-player smoke covers
selection, snap, pass/catch, dead ball/next play and restart; it is not
complete defensive, focus-release or full-match qualification.

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
need Node 24, Chrome and Tesseract; use the documented perspective/source-player
subset rather than treating the older orthographic smoke as current acceptance.

## What remains

- All fourteen source-mode actors reuse **one normal-body Chicago home /
  `PC_WHITE` variant**. Jersey-number overlays, other rosters/skins and continuous
  source animation are not ported. One QB pose is not universal pre-snap behavior.
- The field/stadium, lighting and HUD remain approximations, not original assets
  or N64 pixel fidelity. The source reference is arcade code; edition equivalence
  with the supplied N64 screenshot is unverified.
- Thirty yards in four downs, six-point touchdowns and two-point safeties have
  source-traced tests. AI, physics, two plays per role, fixed live-time quarters
  and own-20 post-score restarts remain simplified.
- Full browser match/final restart, perspective focus-release geometry and
  representative hardware performance remain unqualified.

The [source-parity ledger](docs/source-parity.md) records the pinned references,
provenance boundary and exact evidence limits. Original inputs, converted art,
pose data, builds and captures stay local and ignored, but persist on disk.
The [architecture contract](docs/architecture.md) documents the Unity runtime and
single-writer boundaries. Docker serves static files; no React, API or backend is
required. Team ownership is in the [roster](.squad/team.md) and
[routing rules](.squad/routing.md).
