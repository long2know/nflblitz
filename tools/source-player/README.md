# Portable C# browser preview (not a Unity player build)

## Explicit pre-snap-switch public default

`build.py --data "$PLAYER563"` now defaults to the closed stable profile
`original-teams-contact282-c2-m1-presnap-switch`. A missing/mismatched new bundle
fails before publish, never falls back to old M1. Historical builds require
`--profile original-teams-contact281-c2-m1`; historical `prepare.py` retains its
old output/defaults and `PreviewOptions.FromUrl(url)` retains its historical
contract. Browser startup authenticates the catalog before selecting options;
the host and factory default from that same catalog. Conflicting URL/profile
identities reject.

Prepare the successor without overwriting its complete558-file base:

```sh
python3 -B tools/source-player/prepare_presnap_switch.py \
  --input "$PINNED_PLAYER558" --input-manifest-sha256 "$BASE558_SHA256" \
  --profile-inputs "$PINNED_PROFILE282" --impact "$REBOUND_IMPACT" \
  --settings tools/source-player/match-settings.presnap-switch.json \
  --output "$NEW_PLAYER563"
```

All558 inherited pins remain present. Only the authentic hop, new animation
inventory, fifteen-binding callbacks, rebound482-site impact and explicit new
settings are added; four entrypoints redirect. The new default settings differ
from historical single-human defaults only in `station_layout:2 -> 4`, not in
station0/Home or a four-human policy. Original incomplete TwoStation
`plyr_control[-1]` remains a fail-stop boundary.

The complete563-file manifest contains **559 active inputs and four sealed
retained inputs**: `animation/export-inventory.json`,
`kernel/c2-callback-data.json`, `kernel/impact-presentation.json`, and
`settings.json`. The successor catalog authenticates each retained path, byte
count and frozen historical558 SHA256; `PlayerData` reads and verifies those
bytes without parsing/applying the superseded services. In particular the
retained `settings.json` is the approved four-human base configuration, not the
new single-human configuration. No retained entry can be an active entrypoint.
Historical profiles receive no retained-input exemption.

Both C# startup and the unchanged generated JavaScript require every declared
input to have been consumed before accepting the host. Unknown/missing/renamed
or corrupt inputs remain fatal; the builder also rejects unlisted files in the
private input tree before publishing. This does not delete or rewrite any of
the558 inherited files, add a profile alias, or change gameplay/recordings.

Run the real managed loader contract on each explicitly prepared bundle:

```sh
dotnet "$OWNED_CHECKS_DLL" "$PLAYER563" --loader-contract-only \
  --proof-output "$OWNED_NEW_PROOF"
dotnet "$OWNED_CHECKS_DLL" "$PINNED_PLAYER558" --loader-contract-only \
  --proof-output "$OWNED_OLD_PROOF"
```

Permanent regression coverage uses actual compiled C# `PlayerData`/`PlayerHost`,
the real563 manifest and **actual `build.py` output**, not a hand-built loader:

```sh
SOURCE_PLAYER_TEST_DATA="$PLAYER563" \
SOURCE_PLAYER_TEST_OLD_DATA="$PINNED_PLAYER558" \
SOURCE_PLAYER_TEST_CHECKS="$OWNED_CHECKS_DLL" \
SOURCE_PLAYER_TEST_BUNDLE="$OWNED_APPBUNDLE" \
SOURCE_PLAYER_TEST_OUTPUT="$NEW_PRIVATE_TEST_OUTPUT" \
python3 -B -m unittest discover -s tools/source-player -p prepare_test.py -v
```

The generated-JS boundary test replays the actual managed file-read ledger
through emitted `main.js`: valid data empties the input map, omitting precisely
the four retained reads reproduces the original fatal error, an unknown input
still fails, and all four missing/corrupt retained files fail by name. Its
stubbed runtime imports are boundary tests, **not** genuine browser startup or
World/PNG evidence. With no explicit private paths these tests skip rather
than generating substitute assets. Use isolated output/intermediate/SDK roots.

Status passively exposes catalog/library/callback identity, actual factory and
enabled flag, real ownership/pose data, and resolved hop selections delivered
through the actual transport/cinematic recording consumer. It introduces no
JavaScript gameplay, browser command or private-state setter. This is not native
audio, a new Unity build, genuine World evidence, or whole-feature acceptance.
The v4 recording and exact focused/full commands are documented in
[`tests/defensive-presnap-switch`](../../tests/defensive-presnap-switch/README.md).
The v3 and older recording descriptions below remain historical, not aliases
for the new enabled profile.

This is a separate, static .NET 10 browser-WASM application. It compiles and
executes the **actual production C# M1 gameplay kernel**. JavaScript only delivers
cabinet samples and draws C#-supplied original assets/matrices/HUD commands with
WebGL2. There is no Blazor UI, React, game engine dependency, API, database,
JavaScript football simulation, canned replay, graybox or image fallback.

This preview does **not** satisfy native Unity build, full-game replica,
bit-exact arcade or complete M1 qualification. The existing Unity project,
Docker/hosting configuration, root page and default server are unchanged.

## Public recording generations and build evidence

The current required replay is explicitly named
`portable-cabinet-wide-pass-clearance-selection-countdown-12000`
(`source-selection-countdown/v3`), not the older possession or main38 input.
The unchanged approved C# cabinet authoring controller lives in
`tests/movement-callers/PortablePublicPolicy.cs`; its strict saved-capture
export and input-only preparation are documented in
[`tests/movement-callers`](../../tests/movement-callers/README.md#current-source-timer-generation--october-20-2026).
Preparation runs no simulation and imports no original data. Older input bytes
and their source-specific positive/stale classifications remain immutable.
This tooling/fixture revision changes no production or browser-build member,
so it does not require or claim a new browser build/capture campaign.
Full current Portable M1 qualification is separate from a browser, Unity,
native, target-PRNG, audio/DSP or whole-game acceptance claim.

## Required original normal-camera data

The explicit M1 profile now uses the shared original active CAMBOT normal/
tracking/integrator, eleven-entry kickoff/P2P and original impact-return stage.
There is no EMA/actor-envelope fallback. The unchanged older normal model is
legacy/diagnostic only. See
[`tests/presentation/normal-camera`](../../tests/presentation/normal-camera/README.md)
for34,084 executed original-C wakes,26 compiled semantic controls, raw Core
observation/feedback and native-service qualifications.

Keep the existing impact catalog and every approved original data pin:

```sh
python3 -B tools/assets/prepare_normal_camera.py \
  --source-root "$AUTHORIZED_ORIGINAL_ROOT" \
  --destination .squad/.scratch/camera-input/normal-camera-original.json
python3 -B tools/source-player/prepare_camera.py \
  --input "$READONLY_APPROVED_PLAYER_DATA" \
  --input-manifest-sha256 "$APPROVED_PLAYER_MANIFEST_SHA256" \
  --camera .squad/.scratch/camera-input/normal-camera-original.json \
  --output .squad/.scratch/camera-player-data
```

Apply this extension after the required ball/head/fire preparation. It adds one
camera file/entrypoint without a schema migration or rewriting historical
receipts; approved556 becomes557 with all556 old file pins unchanged. For the
native **API-only** integration, the explicit M1 team importer requires the same
JSON beside `impact-presentation.json` in `-teamImpactRoot`. This does not
establish a Unity import/player execution or original-data redistribution right.

### Required active possession-message operands

The same camera owner now executes the original interception/turnover2/punt-return
branch and its real portable sleep/suspension continuation. A prepared camera
bundle additionally needs the authenticated interception-speech operands:

```sh
python3 -B tools/interface/prepare_possession.py \
  --source-root "$AUTHORIZED_ORIGINAL_ROOT" \
  --sound-header "$AUTHORIZED_MIDWAY_ROOT/include/goose/sound.h" \
  --output .squad/.scratch/possession-speech
python3 -B tools/source-player/prepare_possession.py \
  --input .squad/.scratch/camera-player-data \
  --input-manifest-sha256 "$CAMERA_MANIFEST_SHA256" \
  --speech .squad/.scratch/possession-speech/possession-speech.bytes \
  --output .squad/.scratch/possession-player-data
python3 -B tools/source-player/build.py --data .squad/.scratch/possession-player-data
```

This adds one40-byte entry without changing the schema or any inherited file
pin: approved557 becomes558. It is required independently of the fire/meter
options; there is no invented playlist or muted fallback. Source-owned speech
submissions preserve the selected bank and actual ordered parameters but do not
play PCM/DSP audio. Raw sound metadata remains visible in host events.

`state.possessionServices` exposes current process sleep words/last wakes, the
actual camera-output producer tick, saved continuation, message objects,
timeout, and speech metadata. The camera retains its last output through the
real sleep40; it does not manufacture a frame per rendered update. Message
entry/sleep100/flashers/deletion and earlier-versus-later resumption are covered
by the40-scenario original-C /47-case managed campaign in the camera README.
Punt return intentionally has no overlay.

The atlas has the complete authored WIF image, not generated native `_c1`
geometry. The shared compositor uses that union once for the in-phase logical
pair; it never duplicates the image or invents native Composit packing. Its
derived white/chroma mask adds one runtime texture, not a new original payload.
Native component/Glide pixels, global scheduler qualification, new public-input
browser captures and the material parity gate remain separate.

The portable metadata exposes the actual pre-actor raw camera stage and shared
RNG request tail; GPU evidence reads the real projection uniform and full
content viewport. `normal-camera.browser.mjs` uses only an owned CDP target and
public four-cabinet controls/Escape/Continue, with stable paused normal-gameplay
PNG brackets. Its browser proof is separate from synthetic original-C branch
fixtures. Native/full-game/audio/PCM/DSP/performance and all16 rows remain open.

The explicit Portable kernel policy reports `CarrierlessDefensiveSwitch` in
`state.kernel.repairs` when a human requests a defensive switch during a
carrierless Pass/FieldGoal/Kickoff seeking branch. The actor/stations are
retained without inventing a receiver/angle, starting a B+C push or consuming
RNG. This authored continuity policy does not change the strict factory
default or claim defined original memory behavior. The real M1 caller,
defined original C comparisons and compiled controls are documented in
`../../tests/movement-callers/README.md`.

The source period producer also emits one typed `MatchEnded` event when the
match genuinely reaches its terminal boundary. The existing real bridge
delivers it to `state.eventCounts.MatchEnded` and `state.events`; a final
public restart clears those old-match observations. The payload's `value=0`
and possession context are not a winner indication; read final scores from
the snapshot. This does not add native game-over records/cabinet screens.
See `../../tests/match-lifecycle/README.md` for real producer/bridge/PAT and
Legacy-preservation checks.

## Original impact dust (existing `source-fire=portable` opt-in)

Actual animation `dust_cloud` callbacks now queue all original one/two/four
`dustcld` children in the existing SPFX scheduler. Raw callback identity/order
remains available in `state.visualCallbacks.cues`; `dust`, `boundDust`, and
`unqualified` distinguish a real consumer from fire-off observation. Entry
reads the retained canonical `jpos`/paired camera cache and original facing
table, copies velocity, and consumes the shared source `randrng(3)`. Rendering
does not follow later actor poses or advance processes. There is no turf gate.

Prepare an explicit v2 authoring bundle using the unchanged SDL/17-TRAIL pins
plus all ten exact DIRT TGA blobs:

```sh
python3 -B tools/fire-spfx/prepare.py --original-root "$AUTHORIZED_ORIGINAL_ROOT" \
  --output .squad/.scratch/fire-dust-v2 --authorization-reference "$LOCAL_AUTHORIZATION" --with-dust
python3 -B tools/source-player/prepare_features.py --base-data "$READONLY_V1_DATA" \
  --base-manifest-sha256 "$BASE_SHA256" --fire-bundle .squad/.scratch/fire-dust-v2 \
  --fire-manifest-sha256 "$V2_FIRE_SHA256" --replace-fire-with-dust \
  --output .squad/.scratch/player-dust-v2
```

Migration verifies the prior approved fire identity, preserves every non-fire
input and all existing fire pins, and writes a new output. The original TGA
developer directory/tag20/footer is authenticated before a declared nonpixel
adapter passes the pixel view to the existing strict decoder. No WMS/TEXUS or
generated-header equivalence is inferred. V1/v2 component reading is retained;
enabled Unity/browser composition now requires the v3 helmet extension below and fails visibly with
`SOURCE_DUST_DATA_REQUIRED` rather than substituting generic smoke. The stable
Unity resource key/GUIDs are unchanged; reimport the explicitly prepared bundle.

DIRT reuses the existing source SDL billboard, texture-alpha/RGB-times-alpha,
WRAP, separate source-alpha blend and ALWAYS/write-depth renderer. Effects
have no new flag and remain disabled by default. Target PRNG, native WBUFFER,
Glide/MIPS/FMA/qsort, audio, performance, full-game and all-sixteen acceptance
remain unqualified. `dust.browser.mjs` is a bounded private actual-build,
public-control/GPU campaign, not an asset-free CI test or fixture image.

## Original detached loose helmet (same opt-in)

The existing original `HelmetPopped` decision, actor flag, phead body
substitution, announcer and hard-hit calls are retained. With
`source-fire=portable`, its helper now kills PID0950 then deletes OID0450
**after mode/secret-head guards but before the noncarrier return**. A successful
existing decision queues `loosehlm` in the same head-inserted level-zero/sleep1
scheduler; it does not repeat either helper probability draw.

Entry copies canonical cached neck8 through its paired inverse camera,
including Y. It launches in world axes, consumes the real shared range100
direction choice and retains original integer spin/flight/bounce fields,
double-literal bounce stores, six-draw rebound order and per-moving-wake
range35 spin. Resting ground/counter work continues without a TTL, fade or
deadball retirement. Global `phi`, flags, flight counter, targets and bounce
time survive replacements; only explicit original entry fields reset.
Kill and object deletion remain independent: this global object has **no
dust exit alert**, and deleting it alone does not stop its process.
The actual new-play boundary performs the original `nfl.c:867–868` cleanup.

The mesh is original `models/phelm.sdl` / `limb_phelm_obj`, **not** phead,
medball, held-ball geometry or a sphere. Offline preparation follows the
`models/models.mak` factor1.2 recipe without recentering. Normalized printed
ST uses literal ratio4 /128×256 and the selected actor's captured
`odata.pdecal[2]` team atlas, including its actual skin/uniform selection.
The detached renderer reuses the verified free-ball rotation/shader machinery
without ball-carrier visibility or big-ball rules. It reads **current global
ball WHITE**, not helmet flags, with CC alpha, WRAP, LESS/write and separate
SRC_ALPHA/ONE_MINUS_SRC_ALPHA, ONE/ZERO blending.

Create new private outputs; never overwrite or relabel the v2 generation:

```sh
python3 -B tools/fire-spfx/prepare.py --original-root "$AUTHORIZED_ORIGINAL_ROOT" \
  --output .squad/.scratch/fire-helmet-v3 --authorization-reference "$LOCAL_AUTHORIZATION" --with-helmet
python3 -B tools/source-player/prepare_features.py --base-data "$READONLY_APPROVED_V2_DATA" \
  --base-manifest-sha256 "$V2_PLAYER_SHA256" --fire-bundle .squad/.scratch/fire-helmet-v3 \
  --fire-manifest-sha256 "$V3_FIRE_SHA256" --replace-fire-with-helmet \
  --output .squad/.scratch/player-helmet-v3
```

This explicitly authenticated migration retains every prior554 listed file
identity except the fire manifest and adds only PHELM SDL and prepared
geometry:556 listed files, unchanged17 TRAIL/10 DIRT frames and all non-fire
services. V1/v2 remain readable as components/default-off inputs;
enabled composition rejects missing PHELM with `SOURCE_HELMET_DATA_REQUIRED`.
Unity keeps its existing resource key and GUIDs and requires explicit v3
reimport with the existing authored CC-alpha/source-matrix shader.

See `../../tests/loose-helmet/README.md` for actual compiled-source/parsed-JS
causal controls, exact pinned-C float/int comparisons and original CVTMOD
expanded-face verification. `helmet.browser.mjs` is a separate, bounded,
actual-build public-key/GPU campaign using genuinely paused normal-render
PNGs, not a fixture image or CI asset fetch. Host epoch reset is explicit
portable semantics, **not** proof of native program/BSS lifetime. Signed
overflow and out-of-domain iasin stop rather than inventing native wrap/clamp.
Native `helm.h`, target ABI/PRNG/FMA/qsort/WBUFFER/Glide, Unity execution,
full-game, all16, audio and performance acceptance remain **OPEN**.

## Required source free-ball geometry/material/flight binding

The former recentered `hand001b` material6 extraction was a **held-ball**
substitute for the free draw path. The actual pinned `player.c:draw_ball` uses
`limb_medball_obj_big` from `models/medball2.sdl`, factor1 and its unchanged
authoring origin, against HEDSHU/HEDSHU2. See `../assets/README.md` and
`../assets/free_ball_inputs.json` for the strict original-input reconstruction.

Extend the existing verified private bundle without overwriting it:

```sh
python3 -B tools/source-player/prepare_ball.py \
  --base-data "$READONLY_PLAYER_DATA" --base-manifest-sha256 "$PLAYER_MANIFEST_SHA256" \
  --ball-bundle "$PREPARED_ORIGINAL_FREE_BALL" \
  --ball-manifest-sha256 3d7ab26acab8a968058c6644011e1ac503547bf6ae67705974e3848ee5e00549 \
  --output tools/source-player/data
```

Both decal pixels already belong to the original team catalog. The required
`freeBall` service adds only its pinned SDL, source-authored header and
calibration manifest. Missing/malformed data stops before simulation; there
is no legacy hand-mesh, sphere, recenter, texture or tint fallback.

`SourceFreeBallBinding` is a **read-only same-assembly projection** of Core's
already maintained source heading/elevation/roll/scale/position/flags/tick.
It does not change Core, reconstruct velocities, integrate spin or mutate
possession. `SourceFreeBallRenderModel` uses the actual playbook's original
257-entry TRIGS table and the exact source call order
`Y(fwd) * X(-fwd2) * Z(phi)`, scales only the basis (`pup_bigball != 0` adds1.5),
then composes camera times ball-relative-to-eye. Integer angles are not degrees.
WebGL receives the complete immutable matrix, source XYZ geometry, explicit
source321 culling, sole-decal raw0 mapping, constant main-ball alpha255,
raw `BF_ALT` and `BF_WHITE`. Held-ball actor geometry is unchanged.

The renderer rejects stale-tick/detached matrix packets. Read-only GPU proof
includes the actual bound model/projection uniforms and original texture
identity; state/GPU pairs are collected in one synchronous browser task.
Unmodified gameplay PNGs remain honestly source-tick **bracketed**.

Core retains the original standing kickoff heading/elevation during pre-snap
and the kicker's approach, before returning to the existing flight orientation.
The original spin celebration's one-wake elevation overshoot is also retained.
These are `ball_proc` lifecycle rules, not browser-side rotation substitutes;
the extracted original-C regression is documented in `../../tests/ball/README.md`.

Focused checks:

```sh
bash tools/source-player/dotnet.sh run --project tools/source-player/SourcePlayer.Checks.csproj \
  -c Release -- --free-ball-synthetic
bash tools/source-player/dotnet.sh run --project tools/source-player/SourcePlayer.Checks.csproj \
  -c Release -- tools/source-player/data --free-ball-only
node --test tools/source-player/free-ball.renderer.test.mjs
python3 -B tools/source-player/free_ball_mutations.py --data tools/source-player/data \
  --output tools/source-player/artifacts/free-ball-controls
```

`free_ball_reference.py --help` describes the explicit local original C
draw/math oracle. It compares actual uploaded geometry/UVs with the pinned SDL
and provides compiled `draw_ball/rotx/roty/rotz/mxm/isin/icos` matrix/material
comparisons with host FMA disabled. Seven C# controls must **compile first**
and fail named mesh/material/pivot/axis/order/spin-tick/packet assertions;
four executable actual-JS controls cover disconnected matrices, winding,
alternate decals and C# JSON-float versus exact marshalled-binary32 comparison.
The bridge compares **exactly in the actual float32 GPU domain**, not by a
presentation epsilon or a transform/physics adjustment.
Synthetic/public checks are not gameplay witnesses.

For a new actual candidate, leave8080/8082/8083/8084 untouched:

```sh
SOURCE_PLAYER_PORT=8085 SOURCE_PLAYER_BIND_ADDRESS=127.0.0.1 \
  docker compose --env-file /dev/null -p nflblitz-source-free-ball \
  -f tools/source-player/compose.yaml up -d --force-recreate
bash tools/source-player/with-browser-runtime.sh tools/source-player/free-ball.browser.mjs \
  --url http://127.0.0.1:8085/ --seconds 90 \
  --output tools/source-player/artifacts/free-ball-attempt1
```

The defined-domain source-authoring mesh/transform is not recovered native
`medball2.h`, native global render-counter/triangle-cache behavior, WBUFFER,
Texus/WMS quantization, native compiler/FMA or pixel equivalence. The consumer
uses actual current Core orientation fields; it does not certify all historical
`ball_proc` lifecycle branches. Main-ball alpha255 remains separate from the
source-entry football ghosts described below. Unity source/API compilation is
not an Editor execution or native Unity player qualification.

### Mechanical integrated-C34 build provenance

The following football-ghost section and its linked C33 evidence remain
unchanged. The separate integrated candidate uses actual merged C34
`b163938fe4ba494bb69a116d35c000d5caeb3447`, tree
`78107e2b2a442bf0fab98694bef15f4420ddee5c`, retaining its typed MatchEnded
producer/tests and exact40 CI guards. The fresh builder receipt is
`936682d6b2f054308b95af5db759717c02fa5128a842697fd5666112723eaffa`.
Its metadata-only C34 provenance annotation yields served receipt
`2716dc210f8fabccef4f10b75ecfeb4e2381ac09f353f87b698ab80d58398948`;
the HTTP build-info and both genuinely new WASM assemblies were authenticated.
No old receipt is relabelled as integrated output.

The unchanged bounded browser/artefact gates record the natural five-stage
ghost, exact source wakes177..181/deletion182, public-paused PNG [177,177]
and continuing PNG [297,314], ending315/epoch0 without reset/errors. Full
production/fire18,800, unchanged trail/fire-adapter routes and C34 terminal/
bridge regression pass separately. The known broad-preview station guard is
retained as a failure, not bypassed or called all green. Exact source/text/
binary/PNG hashes and remaining qualifications are in the integrated worktree's
`.squad/.scratch/ball-trails-integrated-proof/handoff.json`; publication and
integrated review remain separate.

### Original non-fire football ghosts

With the existing explicit `?source-fire=portable`, BTRAIL and PCLOUD now queue
the real `trail_ball_proc` football-mesh lifetime at default level0, rather than
only returning an unused selector. Each first wake copies actual source
XYZ/heading/elevation/roll. The copy stays static through the original five
alpha/scale stages and five sleeps, then deletes. It reuses the same genuine
medball2/HEDSHU2 mesh/decal, not the puff renderer or generic particles.
Current global carrier/Phantom/White/bigball and local ALT retain their distinct
source roles. Both RGB multiplication and blend alpha consume CC_ALPHA;
GLIDE3 White overrides it withFF.

The main20-value ball packet is unchanged; additional immutable ghost packets
follow it. Actual snapshots, GPU material/matrix/texture/depth readbacks,
normal-color/identity diagnostics and source-wake records expose the complete
typed lifecycle. A render frame is not assumed to represent every source wake.
See [the focused trail suite](../../tests/ball-trails/README.md) for extracted
original C, compiled controls, real four-human/browser proofs, retained failed
attempts, PNG brackets, safe own8093 commands and exact remaining limitations.

## Original popped-head input extension

The original native generated `head.h` is absent, **not the authoring asset**.
The pinned `models/phead.sdl` is now supported through the strict
source-authored pipeline in `../assets/README.md`. Extend an existing private
base518 or fire-enabled538 bundle without rewriting it:

```sh
python3 -B tools/source-player/prepare_head.py \
  --base-data "$READONLY_PLAYER_DATA" --base-manifest-sha256 "$PLAYER_MANIFEST_SHA256" \
  --head-bundle "$PREPARED_ORIGINAL_PHEAD" --head-manifest-sha256 "$PHEAD_MANIFEST_SHA256" \
  --output tools/source-player/data
```

The actual production reader validates the original SDL, calibrated source
header and explicit portable origin **before simulation**. The existing32
body models,20 station custom heads, all original uniforms/skin/BCD numbers,
source poses and raw flags remain unchanged. When Core actually publishes
`PF_POP_HELMET`, the actual neck8 draws original phead geometry against the
unchanged source bind pivot. Native raw texture code4 is logical HEDSHU slot1,
not logical slot4. Station custom heads still take priority.

The browser uploads this static mesh once and uses the normal real actor/joint
matrices. Read-only GPU proof additionally measures visible phead-only pixels
using the **same** original mesh/texture/depth/pose; torso or number pixels cannot
stand in for a visible head. No authored DTO/adapter fixture is a gameplay
witness. `--goal poppedHead` in `feature-browser.mjs` requires a real public-input
raw flag, matching original phead binding/GPU pixels and at least120 continuing
source ticks. It never writes Match/actor/flag/pose/RNG/ball state.

For a distinct candidate use8084 and its own Compose project:

```sh
SOURCE_PLAYER_PORT=8084 SOURCE_PLAYER_BIND_ADDRESS=127.0.0.1 \
  docker compose --env-file /dev/null -p nflblitz-original-popped-head \
  -f tools/source-player/compose.yaml up -d --force-recreate
```

Old8082/8083 projects/bundles must remain unchanged. The helper
`browser-suite.mjs` accepts an optional third URL argument for isolated
candidate regressions, e.g. `http://127.0.0.1:8084/`.
This remains a **portable C# preview, not a native Unity player build**.

Original-head checks (explicit private data required for the C# and emitted
artifact commands; they are not asset-free CI claims):

```sh
bash tools/source-player/dotnet.sh run \
  --project tools/source-player/SourcePlayer.Checks.csproj -c Release \
  -- tools/source-player/data --require-portable
python3 -B tools/source-player/popped_head_mutations.py --data tools/source-player/data \
  --output tools/source-player/artifacts/popped-head-controls-owned
node --test tools/source-player/popped-head.renderer.test.mjs
node --test tools/source-player/popped-head.artifacts.test.mjs
```

The compiled controls change the actual neck pivot, raw4/logical1 mapping,
head-service binding, helmet-mesh fallback and station-custom-head priority.
They must compile and fail named artifact assertions. The executable JS control
changes the actual scene head consumer. Emitted-frame controls reject cleared
flags, wrong original mesh/slot, body-only GPU pixels and stalled continuation.
The unmodified PNGs remain paired with their own CDP/source-tick/hash receipts.

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
The free ball separately follows the Core ball owner's original selected-Blink
`BF_WHITE` pulse on post-increment flight-time multiples of ten. Its original
texture returns on the next free/phantom wake; held balls and sticky Bobble
selection retain the source rules. This uses the same explicit fire opt-in,
not a renderer timer, extra particle or new default.
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

### Two or four local human cabinets

The default remains one Home human. To prepare an explicit two-human game, use
`--settings tools/source-player/match-settings.two-player.json`; for four humans,
use `--settings tools/source-player/match-settings.four-player.json` in the same
preparation command above. Choose a fresh private output and build against its
new manifest. Do not replace settings inside an existing accepted data bundle.

The two-human configuration assigns P1 Home and P2 Away. The four-human
configuration adds P3 Home and P4 Away, retaining the original seven actors per
team, ordinary rosters/settings and no custom-head or powerup override. These are
explicit free-play boot configurations, not original coin/join/menu emulation,
network multiplayer or permission to alter a running simulation.

| Human | Directions | A / B / C |
| --- | --- | --- |
| P1 | W / S / A / D | V / B / N |
| P2 | Arrow keys | Delete / End / Page Down |
| P3 | I / K / J / L | U / O / P |
| P4 | T / G / F / H | Z / X / C |

Each station can instead use its corresponding connected standard-mapped
gamepad. The configured first captain selects that side's play; teammates retain
their separate live controls. Escape pauses all local cabinets, focus loss clears
all held inputs, and Enter submits Continue for the first configured station.
Asset-free configuration/buffer and keyboard-focus regressions do not by
themselves qualify a multi-human browser session or all I1 multiplayer cases.

Separate real-data browser campaigns verified two and four actual human
controller bindings, simultaneous independent raw cabinet inputs, a real
focus-loss clock stop, neutral input recovery and fresh post-focus controls.
The four-human opening-play case also observed all four controlled actors move;
the two-human case observed P1 movement and P2 binding/input, not movement for
every station or every action. These are bounded local-keyboard cases, not
physical multi-gamepad, join-during-play, complete multiplayer-match or native
cabinet-accounting qualification.

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
controlled after the actual catch. Play selection retains its20-admitted-menu-wake
gate; normal snaps retain the original one-second gate. Keyboard edges are
captured on events, not just render frames, and flow through the production
cabinet buffer and60-host/57-source scheduler. Blur/hide/pause releases controls,
clears pending edges and prevents hidden-time catch-up. Physical controller
hardware qualification is separate from the tested standard mapping.

Possession-message suspension stops the actual gameplay processes, not source
input observation. Both production hosts bind the same selector once per
ordered57Hz dispatch, including dispatches without a `game_proc` wake.
The selector checks the current snapshot and consecutive ticks; only an actual
awake game/menu consumer may change pages/plays or consume sleep20. No gameplay
loop or RNG is run to fill the gap. Press/release pairs completed while frozen
are not replayed after cleanup. A still-held button is read on the first later
actor visit and compared with its last actor sample; earlier game/clock/status
visits resume on the next dispatch. Focus clears pending input and requires
neutral; full restart additionally clears the selector's ordering/menu epoch.
The persistent C#7.3 cabinet/Core/HUD regression runs in the required
`tests/presentation/possession-services` campaign with FX enabled and disabled.

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
- The explicit M1 profile uses the original active `SourceNormalCameraModel`
  inside the existing `SourceCinematicPlayback` lifecycle owner, including
  raw actor/ball lead endpoints, scalar/velocity integration, kickoff/P2P,
  GOPHER, original impact return and reachable field-goal dependency.
  The executing pre-player camera publishes the same source position/transform
  and explicit original DIP resolution into `SetSourceMovementCameraFrame`.
  WebGL and Unity call the same producer, not an inspection orbit or copied
  browser algorithm. The old `NormalCameraModel` remains historical legacy/
  diagnostic only. Active possession messages now use the same owned camera
  continuation and shared Core/HUD path. Native fast-trig/global-scheduler/
  initial-QB-box and generated-component pixels remain unqualified.
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
- Separate-ball geometry is the original source-authored `medball2` with its
  unchanged authoring origin and HEDSHU decals. Actual Core source angles feed
  the defined original draw matrix. Native header/global rendering/pixels remain
  unqualified; no procedural sphere or fabricated flight physics is used.
- Production sound selection still executes with its actual RNG ordering.
  **Audio output unqualified**: no PCM/DSP playback, synthesized substitute or
  claimed audible output. Dust remains unqualified. The independent portable
  fire opt-in now consumes the approved real source producer/render contract
  described above; this is not native WMS/TEXUS or arcade-pixel qualification.
- Native generated `head.h` is absent, while original `models/phead.sdl` is
  available. With the explicit head bundle above, actual `HelmetPopped` uses
  that original source-authored mesh. Without it, the required-data failure
  remains visible. The preview never clears the flag, supplies a generic head
  or changes gameplay/seed. Native qsort/header/pixel equivalence is unqualified.
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
node --test tools/source-player/popped-head.renderer.test.mjs
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
runs the four cabinet-input, five public renderer, and two source-head
renderer contract tests, the two named public preparation failure gates,
the four hosting guards, and workflow mutation guards. The source-head fixtures
are synthetic asset-free contracts, not genuine gameplay witnesses. Hosting
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
