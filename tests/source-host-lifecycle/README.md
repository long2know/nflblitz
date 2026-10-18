# Source host startup/restart boundary (FB-Q1)

```sh
python3 -B tests/source-host-lifecycle/run.py \
  --output tests/source-host-lifecycle/.artifacts/lifecycle --controls
```

Uses the existing .NET10 SDK compiler/reference pack, C#7.3 and warnings as
errors. No packages, restore, Unity executable, imported resources, browser or
service are required. Choose a new ignored output directory on each invocation.
The existing Game source checks workflow runs this managed campaign, including
all five compiled controls, before fetching any original game inputs.

The executable compiles the **entire current production** `BlitzRuntime.cs` and
`SourceFreeBallRenderer.cs`, not extracted/reimplemented lifecycle methods.
Managed collaborators supply input edges, resource lookup, call recording, and
fault injection at required asset `Load` and primary/alternate `CreateMaterial`.
The actual renderer initializer and host bootstrap/catch/failed-Update/public-R/
restart/focus/pause/enable/banner paths execute against those collaborators.
This is managed production-boundary execution, **not a Unity Awake/player test,
native engine execution, actual asset/material validation or shader execution**.
Actual Unity API compatibility is a separate reference-only compilation:

```sh
python3 -B tests/fire-spfx/api_compile.py \
  --output tests/fire-spfx/.artifacts/lifecycle-api \
  --unity-editor-data /path/to/6000.3.24f1/Editor/Data
python3 -B tests/test-unity-metadata.py
```

Startup becomes restartable only after its required consumers, HUD, event hooks
and first presentation complete. A failed bootstrap remains a visible fail-stop
until the host is recreated with valid required resources; `R` is not an asset
reload/reinitialization operation. Focus, pause and enable cannot activate an
unready or failed bridge. A fully initialized source host can still restart
normally or recover from a later runtime failure; explicit `actors=graybox`
Legacy diagnostic restart remains separate.

Nine named cases cover missing resources, throwing required data, primary and
alternate material initialization, later HUD/first-camera failures, ready source
restart and later-runtime-failure recovery, and explicit Legacy restart.
Assertions inspect the original failure/banner, absence of bridge restart or
activation/HUD/replay/consumer work after failed bootstrap, neutral input,
exactly-once hooks, initial cues, camera rewind order and ready ball consumption.
The simulation/camera/HUD collaborators are instrumented doubles: these checks
establish host call routing, not gameplay/RNG/replay-render fidelity.

`--controls` compiles five mutations of the **actual host source** in isolated
ignored output copies: bypassed readiness, prematurely completed bootstrap,
missing completion, focus/enable bypass and missing bridge rewind. Each must
compile with zero diagnostics and fail its named behavior assertion. An unrelated
compile failure never counts as a kill. An unchanged final baseline then runs.
Logs, response files, source/reference/assembly hashes and case/assertion counts
are retained in `result.json` and per-run directories. Live production files are
never mutated by the controls.

For an explicit read-only rejected snapshot, `--source-root /path/to/snapshot`
executes its two production files with the current tests and records their root
and hashes. The rejected host is expected to compile cleanly, then fail the
named retained-failure assertions; a failed compile is not a reproduction.
