# WebGL build and static hosting

The root is the Unity project. The verified route is a **Unity 6000.3.24f1 host
editor**, followed by a **Docker static runtime**. Original player mesh/textures
and authentic static `QBSTANCE` frame 0 have passed native apply/reload, host
build and bounded standing-player browser checks in that runtime. The later
identity/field-axis legend correction has also passed a host build; its matching
Docker promotion is verified. Only its fresh final-title browser capture remains
pending for that evidence delta, not the standing-pose integration.
See the [source-parity ledger](source-parity.md) for the visual/gameplay limits.

## Pinned toolchain and external activation

- Unity **6000.3.24f1**, WebGL support, Linux amd64 editor image:
  `unityci/editor:ubuntu-6000.3.24f1-webgl-3.2.2@sha256:f3c6661a8c5bc975df168f114eb3882766546b2ad09765664f5b2bb186857ddb`.
- Runtime-only image:
  `nginx:1.30.6-alpine-slim@sha256:3028fc1419f99b95c1c69f844bbaf49dc1b881750c51b497fc721ac4f02a14e6`.
- Bash, Docker with Compose, and an accessible daemon are required for the
  static runtime and optional container-editor route. Python **3.9+**, standard
  library only, prepares the local source player.
- Provision supported Unity activation **outside this checkout**. Host commands
  inherit the caller's `HOME`/`XDG_*`; the scripts neither activate Unity nor
  read/copy repository `.env` or license contents. Never put secrets in commands
  or this tree. Do not run concurrent editors/imports/builds against the same
  project/`Library/`.

## Prepare and import a local source player

Start from a checkout of this project and a **user-supplied physical source
directory** containing these exact inputs from `historicalsource/nflblitz2000`
revision `89f840d58d152a2a9f230cb8d0cdffbd554c8ded`:

- `models/plyr266.h` and `models/skeleton.h`;
- `img3d/armhnd.tga`, `img3d/hedshu.tga` and `img3d/BEAHLOD.wms`;
- `cambot.c`, used solely to preserve its source notice.

The pinned [input manifest](../tools/assets/player_inputs.json) checks lengths,
SHA-256 and Git blob identities offline. No ignored helper, analysis script,
network fetch, legacy converter or full original-game build is required.
The source and destination must be trusted, quiescent physical directories;
symlink/traversal guards do not protect against hostile concurrent OS replacement.

Run all following snippets from the project root in the same Bash session.
Set placeholders to your existing paths; do not copy activation material:

```sh
SOURCE_ROOT="/absolute/path/to/nflblitz2000"
PREPARED_ROOT="$PWD/.squad/.scratch/prepared-player"
export UNITY_EDITOR="/absolute/path/to/6000.3.24f1/Editor/Unity"

python3 -B tools/assets/prepare_player.py \
  --source-root "$SOURCE_ROOT" \
  --destination "$PREPARED_ROOT" \
  --confirm-local-use \
  --authorization-reference "local-source-preview"

mkdir -p Logs Build/tool-work
export TMPDIR="$PWD/Build/tool-work" TMP="$PWD/Build/tool-work" TEMP="$PWD/Build/tool-work"
"$UNITY_EDITOR" -batchmode -nographics -quit \
  -projectPath "$PWD" -buildTarget WebGL \
  -executeMethod Blitz.Editor.PlayerAssetImporter.ImportAuthorizedBindPoseFromCommandLine \
  -playerAssetRoot "$PREPARED_ROOT" \
  -playerAssetRequest "local-plyr266.import.json" \
  -logFile "$PWD/Logs/player-local-import.log"
```

`--authorization-reference` is a non-secret provenance label.
Preparation copies the selected model/rig and two direct TGAs, decodes the actual
`BEAHLOD.wms` level-0 ARGB1555 pixels, and writes the importer request, material
manifest and provenance/notices. It is not a recolored or reconstructed atlas.
Use `python3 -B tools/assets/prepare_player.py --help` for the exact CLI.

Preparation requires a **new** destination. `--reuse-identical` only accepts an
entire byte-identical existing bundle read-only; changed, extra or partial output
fails. Native import creates only a new `Assets/Imported/local-plyr266/` and
refuses existing output or orphan metadata. Invalid format, missing texture,
wrong hash or unsupported input fails loudly. Failed partial output is retained
for inspection; do not delete whole asset/cache trees to force a retry.

The generated prefab is
`Assets/Imported/local-plyr266/Resources/BlitzSourcePlayers/local-plyr266/player.prefab`.
Original inputs, prepared derivatives and imported art remain ignored local
files, **not disposable in-memory data**. A clean checkout must reproduce them
with this recipe; the [provenance boundary](source-parity.md#provenance-and-local-data)
explains what is kept out of publication.

Importing identity rotations yields the original **authoring bind pose** with
raised/upward limbs, not a valid standing idle or acceptable game posture. That
shape is present before Unity; retain it only for debugging, not as the game
preview. Do not hide it with an invented axis flip or call it animation.

## Apply QBSTANCE frame 0

**Required after import and before building the standing preview.** Authentic
frame-0 native application and independent saved-prefab reload both passed;
all **343 split vertices** matched the original source matrices within
**2.79×10⁻⁶ source units**. The resulting WebGL generation passed the bounded
desktop/portrait standing-player browser subset. Skipping this step reproduces
only the unacceptable authoring-bind debug posture, not the verified preview.

Also supply `anim/Qb.wan` from the same pinned source revision. The offline
[WAN reader documentation](../tools/animation/README.md) traces the original
`wan2seq` schema and gameplay meaning. Decode one authentic frame into the same
physical root that the importer will read:

```sh
POSE_RELATIVE="QBSTANCE-frame0.pose.json"
POSE_SHA256="51a48cb5d21ea27bc8b43591c8b41e26da300ac78cd434a03f2c4110540d0f72"
python3 -B tools/animation/read_wan_pose.py "$SOURCE_ROOT/anim/Qb.wan" \
  --sequence QBSTANCE \
  --expected-blob a5bed4bc6699a88281e888660e31e349d4009d3f \
  --source-repository historicalsource/nflblitz2000 \
  --source-commit 89f840d58d152a2a9f230cb8d0cdffbd554c8ded \
  --source-path anim/Qb.wan \
  --output "$PREPARED_ROOT/$POSE_RELATIVE"

"$UNITY_EDITOR" -batchmode -nographics -quit \
  -projectPath "$PWD" -buildTarget WebGL \
  -executeMethod Blitz.Editor.PlayerAssetImporter.ApplyAuthorizedStaticPoseFromCommandLine \
  -playerAssetRoot "$PREPARED_ROOT" \
  -playerAssetRequest "local-plyr266.import.json" \
  -playerPose "$POSE_RELATIVE" -playerPoseSha256 "$POSE_SHA256" \
  -logFile "$PWD/Logs/player-local-pose-apply.log"

"$UNITY_EDITOR" -batchmode -nographics -quit \
  -projectPath "$PWD" -buildTarget WebGL \
  -executeMethod Blitz.Editor.PlayerAssetImporter.VerifyAuthorizedStaticPoseFromCommandLine \
  -playerAssetRoot "$PREPARED_ROOT" \
  -playerAssetRequest "local-plyr266.import.json" \
  -playerPose "$POSE_RELATIVE" -playerPoseSha256 "$POSE_SHA256" \
  -logFile "$PWD/Logs/player-local-pose-verify.log"
```

The expected SHA-256 is the recorded deterministic result of this exact decode;
a mismatch is a failure, not a reason to replace the expected pin. The added pose
file means the prepared directory is no longer an identical preparation-only
bundle for `--reuse-identical`.

This is a narrow pose-only update of the matching generated prefab, not general
overwrite permission: it preserves the debug authoring-bind prefab and protects
mesh/UV/material/texture provenance. Repeating an identical apply verifies the
installed pose; different or inconsistent data fails. The adapter composes
normalized source XYZW rotations through the original hierarchy/basis and uses
the source anchored root policy, not unbounded world-space animation motion.
`QBSTANCE` is a stationary neutral-stick QB ball-holding pose, **not verified
universal pre-snap behavior**. No continuous animation is installed.

## Host build: verified route

From the repository root, use an existing supported, activated installation with
the matching editor and WebGL module:

```sh
UNITY_EDITOR="${APPROVED_UNITY_EDITOR:?set an absolute editor executable path}" \
  bash scripts/build-webgl.sh --host
```

Host mode inherits the existing process-scoped `HOME`/`XDG_*` activation setup.
Where an existing Hub installation uses a separate home, the successful build
route can be reproduced with external placeholders:

```sh
env \
  HOME="${EXTERNAL_UNITY_HOME:?set the existing externally activated Hub home}" \
  XDG_CONFIG_HOME="${EXTERNAL_UNITY_HOME}/.config" \
  XDG_DATA_HOME="${EXTERNAL_UNITY_HOME}/.local/share" \
  XDG_CACHE_HOME="${EXTERNAL_UNITY_HOME}/.cache" \
  UNITY_EDITOR="${APPROVED_UNITY_EDITOR:?set an absolute 6000.3.24f1 editor executable}" \
  bash scripts/build-webgl.sh --host
```

Set those variables to existing external paths, not paths in this
checkout; preserve custom XDG locations if the installation uses them. The wrapper
neither activates nor copies licensing material. Add `--check-toolchain` for a
version/module-only probe; it does not open the project or establish activation.
To open the project interactively, use Unity 6000.3.24f1 and
`Assets/Scenes/Boot.unity`; close it before a batch import/build.

## Docker editor: optional acquisition route

The default wrapper executes `/opt/unity/Editor/Unity` in the pinned editor image.
Acquire it explicitly before probing/building; the wrapper does not auto-pull:

```sh
docker pull --quiet --platform linux/amd64 \
  unityci/editor:ubuntu-6000.3.24f1-webgl-3.2.2@sha256:f3c6661a8c5bc975df168f114eb3882766546b2ad09765664f5b2bb186857ddb
bash scripts/build-webgl.sh --check-toolchain
```

Registry manifest pins were verified, but the recorded editor-image acquisition
was timeboxed/stopped without a cached runnable image. **A Docker-editor build is
not proven.** The successful host route does not depend on acquiring that image.

For a container build, provision exactly one external `UNITY_LICENSE_FILE`
(absolute, non-symlink `.ulf`) or `UNITY_LICENSE_DIR` (pre-provisioned directory).
The wrapper checks metadata and bind-mounts it read-only; it does not establish
whether a particular entitlement supports this route. Example:

```sh
UNITY_LICENSE_FILE="${EXTERNAL_UNITY_LICENSE_FILE:?set an external absolute .ulf path}" \
  bash scripts/build-webgl.sh
```

## Validate and serve an emitted player

The build method emits a non-development, single-threaded, uncompressed player in
`Build/WebGL`: `index.html` and one non-empty loader JS, framework JS, WASM and data
file under `Build/`.

**Generation identity, not file mtimes, proves a fresh build.** The wrapper
archives the previous exact `Build/WebGL` directory, exports into an empty
directory at that same path, and retains `Library`/Bee caches. It requires
successful BuildReport/current-run log and `build-generation.txt` identity,
artifact byte counts and hashes, then writes schema-2 `build-receipt.json`.
Cached payload/template timestamps can legitimately predate the invocation.
A zero editor exit or old payload timestamps alone are not a build verdict.
Do not manually rewrite proofs/receipts or clear caches to evade a failed gate.

Logs, builds, receipts and preserved previous generations remain ignored under
`Logs/` and `Build/`. Keep diagnostics private. Neither generated players nor
original-containing runtime images are automatically published.

```sh
WEBGL_ALLOW_TEST_STUB=0 sh scripts/verify-webgl.sh Build/WebGL
bash scripts/serve-webgl.sh
```

Serving must build the static runtime image locally from the emitted player
(`nflblitz-webgl:local`); it does not compile Unity or download a published game
image. It runs in the foreground at
`http://127.0.0.1:8080/`; Ctrl-C stops it. `WEBGL_BIND_ADDRESS` and `WEBGL_PORT`
override the binding. The script deliberately supplies an empty Compose env file
instead of loading repository `.env` files. A health response at `/healthz` proves
HTTP readiness, not gameplay.

Use `http://127.0.0.1:8080/?actors=source` to require the generated source player;
`?actors=graybox` explicitly selects the procedural baseline. Without a parameter,
an installed source prefab is preferred, otherwise graybox runs. A missing or
invalid required source prefab throws rather than silently substituting
primitives. All fourteen source actors currently reuse one model/skin/uniform.
Rebuild after import/pose changes; the browser cannot load new Editor assets from
disk into an older exported player.

The runtime is non-root/read-only, contains no editor or source tree, serves WASM
as `application/wasm`, disables compression, revalidates with ETags and returns
plain-text 404s without an index fallback. A complete real player is required;
synthetic hosting fixtures are rejected by default. `--no-build` is only for an
already prepared matching local runtime image, not a substitute for rebuilding
after a player change. The serve script selects the command-local default
builder unless `BUILDX_BUILDER` is explicitly supplied.

## Browser verification

After the same real source-player generation is served, use the bounded
perspective/source-player subset, not the older orthographic field-offset smoke:

```sh
bash tests/browser/with-posix-runtime.sh node tests/browser/smoke.mjs \
  --url 'http://127.0.0.1:8080/?actors=source' \
  --visual-controls-only --expected-source-pose qbstance-frame0 --timeout-ms 60000
```

See the [browser harness README](../tests/browser/README.md) for installed
Node/Chrome/Tesseract requirements, supported secure POSIX runtime-directory/
transport handling and optional full-match commands.
`--expected-source-pose qbstance-frame0` checks the actual distinct runtime marker;
it is not a pose override. The harness default remains the historical bind
expectation, so do not omit the flag for the standing generation or relabel a bind
run as posed acceptance. For the later identity/legend-corrected generation, add
`--final-presentation` after serving that same receipt-pinned build.

Standing generation `0000067c000032dd00007938000039fe` passed six independent
source visual-controls checks at desktop 1280×800 and portrait 360×740:
upright original-textured players, readable initial HUD, selection/snap/
pass-catch/dead-ball/next-play/restart, and **zero runtime/network errors**.
Served receipt/proof and all four payload hashes, sizes and MIME types matched.
Actual standing PNGs were visually reviewed; these are not the old bind captures.
This proves one authentic static frame, not continuous animation, universal
original pre-snap behavior, complete defensive coverage, perspective focus-release
geometry, full-match/final restart or representative hardware performance.

The subsequent minimal identity/legend host build is
`00000688000072cf00002aa300002ef9`, validated at `2026-10-16T02:44:09Z`
(October 15 PDT). It uses `NFL BLITZ 2000`, subtitle
`UNITY SOURCE PORT / IN PROGRESS`, and absolute field-axis hints rather than a
false screen-relative “right”; input mappings are unchanged. That exact generation
has now been promoted to Docker `8080`, with matching served receipt/proof and
all four payload hashes, sizes and MIME types. Only fresh final-title/legend
browser capture remains pending for this correction. The standing captures above
retain the previous label and do not qualify that newer UI generation.

## Bounded evidence recorded October 15, 2026

| Layer | Observed result | Boundary |
| --- | --- | --- |
| Native import/static pose/camera | Actual source model/rig/TGAs/runtime-atlas derivative imported; authentic `QBSTANCE` frame 0 applied/reloaded, **343 vertices** within **2.79×10⁻⁶ source units** of original matrices; native camera **3,595 assertions** passed. | One static source pose, not continuous animation, universal pre-snap behavior or original visual parity. |
| Historical bind host generations | `00005e4c0000691f00002c0100006780`, then `00002b790000553000005d2b00000d97`, both passed. The same-path/cache repeat took 19 seconds. | Retained two-generation build proof, not current standing posture. Payloads need not be byte-identical across generations. |
| Standing host WebGL / static runtime | `0000067c000032dd00007938000039fe` built and was promoted to Docker `8080/?actors=source`; served receipt/proof and all four payload hashes/MIME matched. | Authentic static frame 0, all fourteen actors sharing one variant. |
| Latest identity/legend host build and promotion | `00000688000072cf00002aa300002ef9`, validated `2026-10-16T02:44:09Z` (October 15 PDT), passed and was promoted to Docker with matching served receipt/proof/four payloads. **31 final-presentation tool tests** passed separately. | Fresh final-title browser capture pending; earlier standing captures and tool tests are not new-title render proof. |
| Docker editor | Immutable manifest pin verified; acquisition stopped without a cached runnable editor. | No successful Docker-editor build claimed. |
| Static runtime / generation tooling | **21 tooling tests** and **eight actual artifact checks** recorded; real pinned-nginx integration is separate from mocked editor/tool contracts. | Synthetic hosting cases are not gameplay proof. |
| Standing source browser | **Six checks**, **zero errors**, desktop/portrait upright PNGs and selection/snap/catch/dead/next/restart passed on the matching standing generation; **30 browser-tool tests** separately passed. | Earlier public label; no continuous animation, full-match, perspective-focus or hardware-performance qualification. |

The historical bind-generation record is local
`Build/source-player-generation-repeats.json`, with bind promotion/HTTP evidence
in `Build/source-player-hosting-readiness.json` and bind browser evidence in
`tests/browser/artifacts/game-1792116188060/evidence.json`.
Historical standing promotion/HTTP evidence is `Build/standing-player-hosting-readiness.json`;
the passed standing browser evidence and screenshot manifest are
`tests/browser/artifacts/game-1792117872816/evidence.json` and `screenshots.json`
in that same directory, including
`source-qbstance-frame0-desktop-play-select.png`. Current identity-generation
HTTP evidence is `Build/final-player-hosting-readiness.json`, verified
`2026-10-16T02:48:24Z` (October 15 PDT). These ignored records/captures are
retained locally, not prerequisites for reproducing the code path from a checkout.

## Headless and tooling checks

Python 3.9+ and .NET SDK 10.0.100 run the source-only suites without Unity or
original art. Check live metadata only while no owner is writing assets:

```sh
python3 tests/run-core.py
python3 tests/assets/run.py
python3 tests/camera/run.py
python3 tests/test-unity-metadata.py
python3 -B -m unittest discover -s tools/assets -p 'test_*.py'
python3 -B -m unittest discover -s tools/animation/tests
```

See [core/metadata](../tests/README.md), [asset](../tests/assets/README.md) and
[camera](../tests/camera/README.md) coverage. The public pure asset suite now has
**2,751 assertions**: the original **419** parser/mesh checks plus **2,332**
original synthetic `SourcePlayerPose` checks, with **eight rejected mutations**
and **seven production-file hash guards**. Clean-source execution and missing/
zero/stale-count rejection passed without original art or pose data. Native
Editor/runtime adapters and the actual 343-vertex comparison remain separate.
The camera suite retains **3,568 assertions / six rejected mutations**.

The core suite has **156 cases / six runner guards**; preparation has **35**
synthetic tests and the WAN reader **12**. Metadata has **12 guard tests** and
passed the current **73-entry** asset/meta/GUID/LF inventory; that is an observed
snapshot, not a fixed allowlist. WAN-reader tests prove decoding, not native
application by themselves.

Build/hosting tooling validation (the optional `--docker` case uses a labeled,
non-playable fixture):

```sh
PYTHONDONTWRITEBYTECODE=1 python3 scripts/test-webgl-tooling.py --docker
```

These checks cover artifact/generation rejection, failed/no-output editor runs,
external read-only activation mounts, MIME, 304/ranges, 404s and filtered runtime
context. Native checks are separate, licensed Editor operations:
`Blitz.Editor.PlayerAssetImporter.VerifySyntheticAdapter` and
`Blitz.Editor.NormalCameraChecks.Verify`. They are not silently substituted by
the headless suites.
