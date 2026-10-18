# Portable C# browser preview (not a Unity player build)

This is a separate, static .NET 10 browser-WASM application. It compiles and
executes the **actual production C# M1 gameplay kernel**. JavaScript only delivers
cabinet samples and draws C#-supplied original assets/matrices/HUD commands with
WebGL2. There is no Blazor UI, React, game engine dependency, API, database,
JavaScript football simulation, canned replay, graybox or image fallback.

This preview does **not** satisfy native Unity build, full-game replica,
bit-exact arcade or complete M1 qualification. The existing Unity project,
Docker/hosting configuration, root page and default server are unchanged.

## Independent original fire and kick-meter opt-ins

Both new features are **disabled by default**, independently of the existing
Portable gameplay compatibility setting. Select them explicitly:

```text
?source-fire=portable
?source-kick-meters=portable-authored
?source-fire=portable&source-kick-meters=portable-authored
```

The actual Core parsers reject duplicate, unknown, native-metric and conflicting
profile options. This browser bundle remains the explicit original281/M1
generation; no C1/old-nine alias is substituted. The actual M1 factory receives
`fireEffects` and `kickMeterMetrics` by name. Fire assets and the strict authored
HUD metric service are validated before constructing the simulation.
Field-goal151, marker27 and punt209 are **portable-authored dimensions, not
recovered native Composit/generated metrics**.

Extend an existing verified private preview bundle without changing it:

```sh
python3 -B tools/source-player/prepare_features.py \
  --base-data "$READONLY_BASE_DATA" --base-manifest-sha256 "$BASE_MANIFEST_SHA256" \
  --fire-bundle "$APPROVED_FIRE_BUNDLE" \
  --fire-manifest-sha256 8bca6ef9e1f089209aca7201e30d281d54444b6cc5b621c42f339394ef04fe88 \
  --output tools/source-player/data
```

The approved original SDL/factor4 and all17 original TRAIL TGAs remain private.
The actual production `SourceFireSpfxFrameConsumer` consumes each immutable Core
after-source frame once. The browser draws those exact camera-space vertices,
UVs, selected original textures, inherited cull and source-alpha color/alpha
blend, with always-depth/write and explicitly portable reciprocal depth.
There is no JS particle simulation, renderer clock/RNG, OnFire-edge emitter,
re-posed attachment, fabricated site, or private RNG.
The source's negative664 horizontal raster projection is shared by world,
players, ball, indicators and fire; camera/cache/world coordinates are not
reflected to compensate. A restart creates a new presentation epoch without
re-uploading static textures or accepting the prior epoch's tick stream.

Actor raw0x400 selects the approved source
`DECAL_TEXTURE_ADD_CCALPHA/0xc0000000` white combiner; other bits and team-fire
state do not synthesize white. The original uniforms remain bound underneath.
The real `SourceKickMeterView.Highlighted` selects the same source center
combiner on the field-goal ball or punt bar (original `plyrinfo.c`814–815 /
959–965); restoration follows the actual snapshot. The production
`SourceHudComposer` supplies meter art, cursor position, wind, timeout text and
source lifetime. Native `flash_text_proc` phase/pixels and DSP output are still
unqualified; no render-frame timer or synthetic click is invented.

Special-team choices remain the **original public fourth-down/field-position
menu options**, not arbitrary first-down page shortcuts. Use WASD/N/B to select
the actually offered FG/punt play, then B to lock its real source meter. The
actual animation release/physical ball state and `LastKickLaunch` come from Core.
The adapter does not change downs, field position, seed, assistance, actors,
ball state, flags or plays to obtain a witness.

Eligible actual CSI, shaker and return-to-normal cues already use production
`SourceCinematicPlayback`. This adapter retains that source-stage ordering and
adds a bounded read-only cue/frame journal (actual process order, source tick,
table/ordinal, shaker draws and return), not random drama or slow motion.

For a distinct Docker candidate, leave existing projects/ports alone:

```sh
SOURCE_PLAYER_PORT=8083 SOURCE_PLAYER_BIND_ADDRESS=127.0.0.1 \
  docker compose --env-file /dev/null -p nflblitz-source-fire-kick \
  -f tools/source-player/compose.yaml up -d --force-recreate
```

Run **after publish**: publish replaces the AppBundle directory, so force
recreation rebinds only this candidate's fresh directory inode. The existing
8082 preview must not be recreated or rebuilt.

Fresh browser evidence uses the repository's guarded browser/CDP helpers:

```sh
bash tools/source-player/with-browser-runtime.sh tools/source-player/feature-browser.mjs \
  --goal spectacle --seconds 300 --url 'http://127.0.0.1:8083/?source-fire=portable&source-kick-meters=portable-authored' \
  --output tools/source-player/artifacts/spectacle-attempt1
```

`--goal punt` / `--goal fieldGoal` target the actual public menu and release.
Attempts are bounded, and unmet witnesses/errors are recorded explicitly.
The fresh profile is an owned project-contained POSIX tmpfs in a private
mount/chroot namespace, exposed there as short `/r` for the unchanged0700,
identity and socket guards. No system temporary profile or shared Chrome
profile/page is used. GPU flags request a backend only; evidence records the
actual WebGL renderer. Only owned Chrome PIDs/profile files are cleaned.

Adapter controls:

```sh
python3 -B tools/source-player/feature_mutations.py --data tools/source-player/data \
  --output tools/source-player/artifacts/adapter-controls-01
```

The five actual C# adapter mutations must compile, then fail named assertions for
omitted fire/meter binding, wrong projection sign, stale fire epoch and the wrong
meter-highlight target. Two additional executable actual-JS renderer controls
reject wrong HUD additive intensity and an incorrect linked player shader.
`node --test tools/source-player/renderer.test.mjs` runs five asset-free consumer
tests. These isolated renderer fixtures are not gameplay witnesses or GPU/native
pixel qualification. Core/component gates remain frozen.

## Required production dependency

The accepted portable Core API must be present before compiling this consumer:

```csharp
SourceC2Composition.CreateM1Simulation(
    /* actual281 animation library, original pivots/playbook/match/rosters,
       presentation/movement/SAFN and original14-binding M1 bootstrap */,
    compatibility: SourceGameplayCompatibility.Portable);
```

`GameSimulation.Compatibility` and `ISourceMatchState.Match.Compatibility` must
both report `Portable`. There is no reflection, guessed API, adapter-local
handicap clamp, new table row or strict-source fallback. The original factory
default remains `StrictSource`; this **separate preview explicitly opts in**.
After the accepted Core dependency is integrated, the default source root is the
current checkout. Before integration, use an explicitly frozen source override
and handoff pin as shown below. A checkout without that API fails explicitly.

## Reproducible build

Run from the checkout root. The installed supported SDK used for this preview is
**10.0.100**, targeting `net10.0` / `browser-wasm` with official
`[JSImport]` / `[JSExport]`. `AllowUnsafeBlocks` permits generated interop only;
the preview contains no unsafe C#/original-C memory execution.

First attempt the chosen real build:

```sh
bash tools/source-player/dotnet.sh publish \
  tools/source-player/SourcePlayer.Browser.csproj -c Release
```

If it reports missing `wasm-tools` (`NETSDK1147`), install the supported SDK and
workload **project-locally**, without changing a shared/global SDK:

```sh
python3 -B tools/source-player/setup-sdk.py
```

The installer verifies the official release-metadata SHA512, pins SDK10.0.100,
and installs `wasm-tools` beneath ignored `tools/source-player/.runtime/`.
The wrapper confines SDK scratch/CLI/NuGet files to that directory. An already
installed appropriate SDK can instead be selected with
`SOURCE_PLAYER_DOTNET=/absolute/path/to/dotnet`. No Unity Editor, entitlement,
activation, credentials, environment files or firmware are inspected.
Select an **owned writable** SDK: .NET can register workload installation
bookkeeping even when the workload packs already exist. Reuse a local copy,
not another preview's read-only SDK directory.

### Explicit private original inputs

Original data is **not** distributed with the code. Reuse authorized, verified
outputs from the existing preparation tools; `prepare.py` never downloads,
requantizes or fabricates source artwork/animation.

Supply these explicit private locations:

- `KERNEL_FIXTURE`: prepared Contact272 `fixture.json`, identifying the actual
  model/skeleton, original31×16 rosters, playbook and runtime movement resource.
- `M1_INPUTS`: the frozen successor's `animation281/`, `c2-callback-data.json`
  and `m-qbfite-hit-extension.safn`.
- `M1_IMPACT`: the exact successor `impact-presentation.json`.
- `WORLD`: prepared original `field2/stadpie` directory with `environment.json`,
  meshbin files and original texture payloads.
- `TEAMS`: prepared original31-team directory with `team-presentation.json`,
  verified generated model headers and original uniform/skin/head TGAs.
- `HUD`: complete original `interface.json` and its three authored atlas payloads.

Example, with each variable set to its explicit private input location:

```sh
python3 -B tools/source-player/prepare.py \
  --kernel-fixture "$KERNEL_FIXTURE" \
  --m1-inputs "$M1_INPUTS" --m1-impact "$M1_IMPACT" \
  --world "$WORLD" --world-sha256 "$WORLD_MANIFEST_SHA256" \
  --teams "$TEAMS" --teams-sha256 "$TEAM_CATALOG_SHA256" \
  --hud "$HUD" --output tools/source-player/data
```

`--sound-functions` can explicitly select the unchanged1983-byte legacy SAFN.
`--settings` and `--bootstrap` select explicit application configurations, with
the supplied public defaults matching production's teams0/1, ordinary settings,
single Home cabinet, no powerup/assist override, and original512×384 DIP raster.
Their byte identities are included in the private data manifest.

The runtime independently verifies every file and then uses the production
readers' own content/provenance checks. A missing clip, model, atlas, texture,
provider or service is fatal: no placeholder can become ready. Preparation is
idempotent only for byte-identical data; choose a new ignored output for changed
inputs. Symlinks, path traversal and unlisted private publish files are rejected.
The page installs its terminal-error boundary before importing `main.js` or any
required runtime/render/input module. The first failure remains visible as
`SOURCE PLAYER STOPPED`, naming the resource when loading fails. Outstanding
downloads cannot replace it with progress, resume startup or earn Ready.

Publish the static app and explicit pinned data:

```sh
# After the accepted portable Core dependency is integrated in this checkout:
python3 -B tools/source-player/build.py --data tools/source-player/data

# Local development against a separately frozen, accepted source snapshot:
python3 -B tools/source-player/build.py --data tools/source-player/data \
  --source-root "$ACCEPTED_CORE_ROOT" \
  --source-handoff "$CORE_HANDOFF" \
  --source-handoff-sha256 "$CORE_HANDOFF_SHA256"
```

An external root must match the exact frozen handoff source hashes. Actual
production and preview inputs are hashed before and after publish. The emitted
`build-info.json` pins the production source closure, private data manifest and
both actual C# Webcil assemblies. `BlitzSourceRoot` is the underlying MSBuild
override; no machine path is embedded in source.

### Explicit Docker preview (separate from Unity hosting)

After the real build above, run from the checkout root:

```sh
docker compose --project-name nflblitz-source-preview \
  -f tools/source-player/compose.yaml up -d
docker compose --project-name nflblitz-source-preview \
  -f tools/source-player/compose.yaml ps
```

Open **http://127.0.0.1:8082/**. This standalone, explicitly selected service does
not start or change the root Unity Compose service, its default profile, or8080.
Check that the project name and port are unused before starting; never replace
another user's container. It reuses the root Dockerfile's digest-pinned Nginx
runtime image without an image build or SDK in the container. The complete local
AppBundle, including its private `data/`, is bind-mounted **read-only**; missing
mount paths are rejected. Keep that built directory available while serving.
The container runs non-root with a read-only filesystem, and health checks request
the actual `build-info.json`. HTML/JS/JSON/WASM are served unchanged with explicit
MIME types; missing files return404, never a Unity page or fallback gameplay.

The default host binding is loopback only. A different port or access from other
machines requires an explicit preview-specific override:

```sh
SOURCE_PLAYER_BIND_ADDRESS=0.0.0.0 SOURCE_PLAYER_PORT=8083 \
  docker compose --project-name nflblitz-source-preview \
  -f tools/source-player/compose.yaml up -d
```

Only expose private original inputs to an authorized network. `WEBGL_*` settings
for the existing Unity service do not select or expose this preview.
Stop only this service when finished:

```sh
docker compose --project-name nflblitz-source-preview \
  -f tools/source-player/compose.yaml stop preview
```

Do not publish the private data or generated bundle in an image or Git.

### Foreground static serving without touching the default server

On an **unused** loopback port, for example8082:

```sh
python3 -m http.server 8082 --bind 127.0.0.1 \
  --directory tools/source-player/bin/SourcePlayer.Browser/Release/net10.0/browser-wasm/AppBundle
```

Open `http://127.0.0.1:8082/`. This is a foreground, static-only server; stop it
when finished. Do not replace an existing8080 service. Original assets can also
be served at the app's `data/` location using an explicit private volume.
Keep the data directory, generated bundle and raw proofs private and out of Git.
Share reviewed gameplay screenshots only with explicit publication authorization.

## Play

- P1: **WASD** cabinet stick, **V=A**, **B=B**, **N=C**.
- A: menu flip, jump/dive/tackle branches as determined by production gameplay.
- B: menu select, snap/pass, receiver/defender switching or lateral branches.
- C: menu page / turbo.
- **Enter** submits the actual station's `SourceAction.Continue` at a result.
- **Esc** pauses; **R** uses the actual simulation restart.
- Standard controller: left stick/D-pad, button0=A, button1=B, right-shoulder5=C.
  Unmapped controllers fail rather than guessing.

The original kickoff occurs first. Its in-flight stick suppression is an
original production rule, not broken keyboard input: movement becomes human
controlled after the actual catch. Play selection retains its20-source-wake
gate; normal snaps retain the original one-second gate. Keyboard edges are
captured on events, not just render frames, and flow through the production
cabinet buffer and60-host/57-source scheduler. Blur/hide/pause releases controls,
clears pending edges and prevents hidden-time catch-up. Physical controller
hardware qualification is separate from the tested standard mapping.

## Authentic inputs and honest renderer boundaries

- Production `SourceTeamPresentationModel`, `SourceTeamGeneratedModels`,
  `GeneratedPlayerReader`, `BindPoseMeshBuilder` and `SourceActorPoseSampler`
  supply all14 actors,32 original model selectors, actual15-joint poses, original
  roster scales, exact home/away uniform/skin textures and BCD number meshes.
  Static geometry is uploaded once; runtime animation changes matrices. There is
  no player RGB/tint substitution and no inferred joint animation.
- The complete original `field2/stadpie` world comes from
  `SourceEnvironmentReader`. Source draw tables, original texture bindings,
  sampling, alpha/depth/blend states and original geometry are consumed. The
  prepared world has fixed original Chicago/Bears field artwork; selecting a
  different roster does not pretend to replace those field decals.
- Existing pure `NormalCameraModel` supplies source-derived normal framing.
  `SourceCinematicPlayback` consumes actual eligible source camera/shaker cues.
  The executing pre-player camera publishes the same source position/transform
  and explicit original DIP resolution into `SetSourceMovementCameraFrame`.
  WebGL uses that camera, not an inspection orbit. Normal framing is an existing
  production adaptation, not a bit-exact native normal-camera claim.
- `SourceHudComposer` supplies actual original authored atlas/font/play-plane
  draw commands. The surrounding HTML status is explicitly adapter UI, not
  original HUD. Authored-TGA and portable-authored-raster origins do not prove
  missing native Texus/WMS quantization or original hardware pixels.
  `OriginalHudProcess` consumes the real typed `C2State.ResultPending`: C2 queues
  play-over before the next `game_proc` wake classifies it. During that one wake,
  the last committed authored HUD is held and explicitly labelled pending;
  actors/ball/camera and source timing continue. The next real commit replaces
  it. An indefinite hold, missing result outside that typed interval, missing
  asset or provider failure still stops execution—no fake result or catch-based
  placeholder is rendered.
- Separate-ball geometry is the original hand001b material6 ball triangles,
  recentered for the real C# ball state. Native free-ball orientation is
  unqualified; there is no procedural sphere or fabricated flight physics.
- Production sound selection still executes with its actual RNG ordering.
  **Audio output unqualified**: no PCM/DSP playback, synthesized substitute or
  claimed audible output. Dust remains unqualified. The independent portable
  fire opt-in now consumes the approved real source producer/render contract
  described above; this is not native WMS/TEXUS or arcade-pixel qualification.
- The pinned original team archive has no qualifying popped-head limb.
  A real `HelmetPopped` activation therefore stops at the strict production
  presentation reader, visibly naming that missing original. The preview never
  clears the flag, supplies a helmeted/generic head, or changes gameplay/seed
  to conceal the input gap. A bounded successful witness is not a claim that
  every later original-data branch can render.
- The explicit Portable Core mode preserves raw handicap/block counts while
  selecting bounded original consumer indices. The actual immutable mode,
  lifetime repair count, tail64 typed repairs and raw/effective team/actor values
  are visible in the runtime details/read-only evidence. These repairs are not
  original-hardware behavior. No original seed, assist setting, clip or gameplay
  state is tuned for a visual result.

## Validation

No original data or status-only fixture is checked in:

```sh
# Actual production loader/pose/matrix/cabinet/57Hz and causal input checks:
bash tools/source-player/dotnet.sh run \
  --project tools/source-player/SourcePlayer.Checks.csproj -c Release \
  -- tools/source-player/data --require-portable
# For the frozen-source development route, also pass:
# -p:BlitzSourceRoot="$ACCEPTED_CORE_ROOT/"

node --test tools/source-player/input.test.mjs
node --test tools/source-player/renderer.test.mjs
python3 -B -m unittest discover -s tools/source-player -p 'prepare_test.py'
# The four hosting guards use the real Docker Compose parser and mutate the
# actual Compose configuration to reject public/writable/empty/unpinned defaults.
python3 -B tools/source-player/hosting_test.py

# Real browser: an explicitly allowed existing CDP endpoint, owned NEW tab only:
node tools/source-player/browser-smoke.mjs \
  --url http://127.0.0.1:8082/ --cdp-port "$ALLOWED_CDP_PORT"

# Real emitted-resource failures, isolated to newly owned tabs:
node tools/source-player/loader.browser.test.mjs \
  --url http://127.0.0.1:8082/ --cdp-port "$ALLOWED_CDP_PORT"

# Isolated fresh-browser feature modes, epochs, and terminal option errors:
bash tools/source-player/with-browser-runtime.sh tools/source-player/browser-suite.mjs \
  features tools/source-player/artifacts/features-current

# Mutate real emitted browser-frame artifacts; run after browser evidence exists:
node --test tools/source-player/assertions.test.mjs
# Current feature witness facts, independent options and explicit incomplete-gate rejection:
node --test tools/source-player/feature-artifacts.test.mjs
```

The driver reuses the repository's browser/CDP/PNG helpers, creates and closes
only its own tab, and never stops the shared browser or another target. It
verifies actual assembly requests/hashes/MIME, original GPU-rendered actors from
both teams, actual original-geometry GPU ball pixels during C# flight, current source
poses/camera/HUD, causal cabinet movement, a real human
Home snap/pass (a prior CPU pass cannot satisfy it), classified carrier tackle
and possession progress, pause, actual hidden-tab input release, and portrait layout. Contact evidence
does not falsely attribute a CPU tackle exclusively to the A key.

Actual WebGL renderer strings and measured load/frame timings are recorded; GPU
hardware is never inferred from launch flags. A separate offscreen geometry-ID
pass uses the **same** real meshes, alpha textures, poses, camera and depth to
measure actor visibility. It is not substituted for game screenshots.
Readiness requires a live Portable C# kernel and real successful WebGL frames.
Static inspection, missing actors, source tick stalls, unbound portable mode,
wrong-team atlases or tints fail the gates. Mutation tests change actual emitted
artifacts and original input bytes to prove those assertions turn red.
The loader regression tests block one manifest-listed original model while five
other download workers are pending, release those real requests, and verify the
same actionable terminal error after all workers settle. They separately block
the emitted `main.js`, `_framework/dotnet.js`, `renderer.js` and `input.js` and
require visible terminal failure without Ready. Interception is page-local;
neither the shared browser nor its other tabs are modified.

These are browser-input/render functional tests, **not** a rerun or claim of the
parent's fullM1/source traces, R_SKIP coverage or native arcade qualification.
All evidence stays in ignored `tools/source-player/artifacts/`.

### Asset-free pull-request CI

`.github/workflows/source-player.yml` independently covers `tools/source-player/**`,
itself and its public source/Docker dependencies. Its read-only, ten-minute job
uses the same pinned checkout/Node/.NET10.0.100 setup as `game-build.yml`. It
compiles the actual native Checks/Core projects with an empty local NuGet feed,
runs the existing three cabinet-input tests, the two named public preparation
failure gates, the four hosting guards, and workflow mutation guards. Hosting
checks use the existing Compose CLI parser with an unavailable daemon endpoint;
the job does not install Docker, Chrome or browser-WASM workloads.

Hosting guards normalize the known Compose 2/5 JSON bind-flag dialects: omitted
means `false` in Compose 2 and the `true` default in Compose 5. Enabled, malformed,
removed no-create settings and unknown dialects remain rejected. Both authored
mounts must retain their explicit read-only/no-create declaration. The checks explicitly
disable implicit dotenv loading. The Compose service and live bundle are unchanged.

This is **not** host3479, Node10, data3, loader5, GPU/gameplay or Unity qualification.
Host3479 requires the complete private `data/manifest.json` and its518 original
inputs. Seven Node artifact tests require saved `artifacts/portable-player/evidence.json`.
The third preparation test verifies private data and otherwise skips, so CI
explicitly selects only its other two public cases rather than reporting that
skip as asset coverage. Full private-data and emitted-browser checks retain their
documented local commands above; no private inputs, live bundle, screenshots,
credentials or source-download steps are supplied to this workflow.

## Supported platform documentation

- [Microsoft: WebAssembly browser app withJSImport/JSExport](https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/wasm-browser-app?view=aspnetcore-10.0)
- [Microsoft: .NET WebAssembly JavaScript interop](https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/?view=aspnetcore-10.0)
- [.NET runtime browser-WASM features](https://github.com/dotnet/runtime/blob/main/src/mono/wasm/features.md)
