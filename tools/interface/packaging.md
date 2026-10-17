# Original B1 HUD and match-service resource packaging

## Explicit Contact281C2 production binding

C2 is **opt-in**, not a silent change to existing C1 or explicit graybox/
legacy flight. Runtime selection is
`?actors=source&sourceprofile=original-teams-contact281-c2`; omitting
`sourceprofile` retains Contact272. A built C2 profile and conflicting URL fail
clearly. Old profile assets/keys/data are not rewritten or aliased.

Prepare the exact canonical281/C2/ONE7538/477 artifacts with the existing-encoder
recipes in `tools/animation/README.md`. Required new identities:

| Resource | Identity |
| --- | --- |
| Team/template | `original-teams-contact281-c2` |
| Animation | `SourceData/local-plyr266-contact281-c2-e87401c8` |
| Team / impact | `BlitzSourcePlayers/original-teams-contact281-c2/{team-presentation,impact-library}` |
| Services | `BlitzSourceInterface/original-contact281-c2-match-services` |
| C2 JSON | `SourceData/source-c2-callbacks-9d2cb7ac` |
| ONE7538 SAFN | `SourceData/sound-functions-c2-hit7538-bb978454` |

The old exact1983-byte/six-tuple SAFN remains separately required at its
unchanged `SourceData/sound-functions-safn1-4a557304` key. C2 JSON and hit data
are actual `TextAsset`s, canonical-reference checked independently; matching
hashes on copied assets are not a canonical resource reference.

`SourceTeamInterfaceImporter.PlanFromContact281C2Arguments` uses all existing
explicit HUD/P1/movement/configuration/bootstrap/old-SAFN flags, plus:

```text
-interfaceC2Root <explicit authorized prepared C2 root>
-interfaceC2Data c2-callback-data.json
-interfaceC2HitExtension m-qbfite-hit-extension.safn
```

The pure `SourceInterfacePackagePlan.WithContact281C2` validates these exact
pins/schema and preserves all original HUD/uniform/roster/head/BCD/pup/movement/
DIP choices. Future licensed entrypoints (not authorization to launch Unity):

- `SourceTeamImporter.ImportContact281C2FromCommandLine` with actual281 and
  new477 impact roots/hashes, leaving every old imported profile intact.
- `SourceTeamInterfaceImporter.ImportContact281C2FromCommandLine` with a fresh
  interface stable ID. Its saved initializer is `InitializeContact281C2`,
  retaining the original ten inputs plus the two required C2 TextAssets.
- Fresh-domain `SourceTeamChecks.VerifyContact281C2FromCommandLine` and
  `SourceTeamInterfaceChecks.VerifyContact281C2BindingsFromCommandLine`.
  The separate pixel check still requires actual licensed GPU execution.
- `WebGlBuild.PrepareContact281C2Project` / `BuildContact281C2` explicitly
  preserve the281 selector on the future Boot component. Ordinary
  `PrepareProject` / `Build` retain the unpinned C1 default, permitting an
  explicit C2 URL when its resources are installed. No scene/settings/build
  has been executed by this work.

`SourceC2Composition.CreateSimulation` passes
`SourceC2Bootstrap.FromOriginalProgramBootAtSoundBanks(data)` as the appended
Core constructor input: announcer then taunt bank draws happen **before**
initial AI. The same actual281 library object supplies Core, real collision/
camera geometry and rendering. The unchanged old SAFN is bound after the
constructor and before first source wake. A post-constructor C2 bind, old272/
277 subset, copied canonical asset or changed data fails visibly.

This is an explicit original sound-bank **entry boundary**, not a claim that
pre-boundary boot/coin/load RNG was replayed, that old goldens remain equivalent,
or that a stock original boot was reproduced. Bank reloads/restarts retain
Core's actual epoch/source-order behavior.

One sound FIFO carries old six, eight speech, hit7538, deferred actorless
whistle and immediate real-actor calls. Consumers inspect
`HasOriginalAnimationCue`/`C2Context` before guarded legacy getters. Hit7538
retains the real OriginalCue even though its reused original call algorithm
leaves `call.C2Context` null; dispatch C2 kind remains authoritative.
Native whistle is never an actor0/ANI/camera substitute. Raw source words and
statuses are published before output delivery; with no real backend, nonempty
output fails visibly with **nothing played**. Playlist0 is legitimate raw
output, not mute; no DSP/speech/audio-device implementation is installed.

**Source-semantic portability repair disclosure:** this generation separately
includes Core's narrow, parent-selected early-return diagnostic when original
`bearing(4,-1)` would dereference an invalid carrier and both candidate identity
predicates are false. This is a source-semantic portability repair, **not**
bit-exact target/native behavior or user-approved original semantics. No
undefined original C was executed as an oracle. Full gameplay/all16/native/
DSP acceptance remains open; resource/API coherence is not that qualification.

## Current C1 engine: explicit Contact272 successor

The current engine generation selects `SourceTeamResourceProfile.Contact272`
and **only** `BlitzSourceInterface/original-contact272-soundvm-match-services`.
Historical B1/Movement157/SoundVM plans, catalog bytes and keys below remain
historical, not silently upgraded. Calling their services factory in the C1
engine fails clearly; it never restores the old approximate contact path.

Prepare the actual272 animation and explicitly bound C1 impact catalog with
`tools/animation/prepare_contact_profile.py` / `prepare_contact_impact.py`
(full commands and source/selection/replay provenance in the animation README).
The272 manifest is
`9cae962bbb861bffe4d379bc30919cf34c0238ed35be82e605253d3059cfede9`,
24,892 actual selected fifteen-bone frames. New resource identities are:

| Consumer | Required identity |
| --- | --- |
| Team/template stable ID | `original-teams-contact272` |
| Team | `BlitzSourcePlayers/original-teams-contact272/team-presentation` |
| Animation | `SourceData/local-plyr266-contact272-9cae962b` |
| Impact | `BlitzSourcePlayers/original-teams-contact272/impact-library` |
| Impact catalog SHA-256 | `738d712b6f80d8a9456338548e7bbce1fdf88baa46f342a7c45efffd02a8f23a` |
| Match services | `BlitzSourceInterface/original-contact272-soundvm-match-services` |
| Shared exact SAFN bytes | `SourceData/sound-functions-safn1-4a557304`,1983 bytes |
| SAFN SHA-256 | `4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280` |

`SourceTeamInterfaceImporter.PlanFromContact272Arguments` and
`SourceInterfacePackagePlan.WithContact272` require the existing complete
HUD/P1/configuration/movement/bootstrap/SoundVM inputs. All existing flags and
explicit source-DIP settings are retained. No team/roster/head/pup/jersey/HUD/
movement value is inferred or rewritten. Old six-argument movement settings
still explicitly mean normal zero C1 block/blitzer masks; this adds no new
configuration format.

**Future licensed import surfaces only; do not run while Editor entitlement
is blocked or without new authorization:**

1. `SourceTeamImporter.ImportContact272FromCommandLine`: retain original all31
   `-teamAssetRoot` / `-teamCatalogSha256`; supply the new272
   `-teamAnimationRoot`, new bound `-teamImpactRoot` and exact impact SHA above.
   Only new ignored `Assets/Imported/original-teams-contact272` is created.
2. `SourceTeamInterfaceImporter.ImportContact272FromCommandLine`: retain every
   explicit SoundVM/M1/HUD/P1/config/bootstrap flag below; choose a **fresh**
   `-interfaceStableId`. Only the new services key is created. The serialized
   `resourceProfile` tag is explicitly `original-teams-contact272`.
3. Fresh-domain `SourceTeamChecks.VerifyContact272FromCommandLine` and
   `SourceTeamInterfaceChecks.VerifyContact272BindingsFromCommandLine`
   verify new profile/paths/content/references. The corresponding
   `VerifyContact272FromCommandLine` interface check additionally requires
   actual atlas-pixel readback; reference compilation is not that evidence.

Services require the same canonical animation `ScriptableObject`, not merely
a matching hash on a copied asset, and the same canonical team template.
`SourceContactComposition.CreateSimulation` supplies the **same272 library
instance** to actual `SourceCatchGeometry` and `GameSimulation`, then binds
the exact trusted SAFN object before returning/first source wake. The renderer
receives that same instance. All fifteen reference pivots are mandatory;
actual actor-camera geometry remains enabled.

The actor root and pose consumer use current C1 hit/puppet/attack/release
snapshots, including actual program frame/root/facing, without render-only
interpolation across those seams. Ordinary M1 movement interpolation and
camera source-stage ownership are unchanged. New impact metadata covers all
360 C1 presentation sites while preserving the complete old416 catalog sites
and original camera math. This is **not** new helmet physics, speech, SPFX or
DSP playback. `BlitzRuntime.UnresolvedContactSoundCalls` exposes the actual
Core unresolved call metadata separately; it is never queued as resolved
audio. Existing resolved raw output still needs an actual output consumer;
unsupported callbacks fail visibly, not silently or with a preview substitute.

Run the dedicated asset-free and explicit actual-data preflights in
`tests/presentation/contact-runtime/README.md`. They use production sources,
meaningful compiled mutants and real Unity6000.3.24f1 assembly references
without launching Unity. No source/derived art, motion, audio or resource
metadata is publishable; all generated bundles/imports/proofs remain ignored.
Native/browser/full-match/parity16/DSP acceptance remains separate.

**Sound-runtime checkout:** use the explicit **SoundVM profile** below.
Historical B1 and eight-argument M1 data/APIs are retained, not silently
reclassified as this new mandatory sound-function version.
Movement PR19 (head`70fcc`) passed CI and was normally merged; this dependent
SoundVM binding/transport remains a separate unpublished layer. Do not pull,
recopy, or reinterpret the active isolated source generation during validation.

**M1 checkout:** use the explicit **Movement157** section below. The B1 recipe
and dated qualification are retained as historical profiles, not instructions
to call157 bytes117. The shared UTF-8 visibility repair and serialized-field
initializers are already present in this checkout; current separate-runtime/
Editor API checks are green. That is not an Editor/native execution claim.

This is an explicit, local-only **B1/final117** importer, not another game inspector.
It packages the complete original HUD/selection atlases, actual converted P1
playbook, and an operator-selected source match configuration for the existing
runtime consumers. It neither downloads assets nor supplies default teams,
roster records, possession, heads, cabinet settings, or power-up values.

## Permanent implementation

- `Assets/Scripts/AssetImport/SourceTeamInterfacePackage.cs`: pure preflight and
  explicit configuration binding. This small runtime-assembly helper reuses the
  existing strict `RosterJson`/`RosterObject` parser; these internal types cannot
  be called from Unity's separate Editor assembly.
- `Assets/Scripts/Editor/SourceTeamInterfaceImporter.cs`: actual AssetDatabase
  packaging, using the existing `PlayerAssetImporter` file/folder helpers,
  `SourceHudAsset.Initialize`, and `SourceInterfaceServices.Initialize`.
- `Assets/Scripts/Editor/SourceTeamInterfaceChecks.cs`: saved resource,
  configuration, byte/reference, shader, sampler, and optional GPU-pixel checks.
- `tests/presentation/interface-packaging/`: isolated pure, real-API, and
  production-source mutation checks. No fake Unity types.

## Required prepared inputs

| Input | Required SHA-256 |
| --- | --- |
| Complete `interface.json` (wind/fire HUD included) | `258d5f4361d7da852b1446e774c5aad7d389ed268daabf3d4971c68a29fd8435` |
| Actual P1 `catalog.tsv` | `b180291759fb22c8ae7b2de7d417867e15243f6071bb1a672f11bb2fb6da4bd3` |
| Already imported final117 animation manifest | `f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274` |
| Existing `Assets/Scripts/Presentation/SourceHud.shader` | `d43a25cb309f2a6ae511aecbbc39c5272c8b784d7e782fc794d2eed33a2a0369` |

Prepare the complete four-file HUD directory using the existing
[`prepare.py`](prepare.py) recipe and authorized original inputs. All three raw
RGBA files (`interface.rgba`, `playsel8.rgba`, `psarrow1.rgba`) must match their
manifest identities. The existing original-team importer must already have
installed the actual all-team roster/mesh/texture resources and the **same**
final117/10,288-frame animation asset under `original-teams`. Neither an old
56-clip asset nor a future movement156 asset is an alias.

Movement data and movement-expanded animations are **not** loaded or required.
`SourceInterfaceServices` retains its frozen lazy M1 accessor. Audio/FX consumers
are not installed by this packager.

## Explicit selected configuration

Supply a separately SHA-pinned UTF-8 JSON file in an explicitly authorized local
root. Every member below is mandatory, including explicit `null` where source
selection/profile metadata is genuinely unknown. Duplicate/unknown members,
trailing JSON, coercions, invalid contract ranges, and cross-team station
profiles fail before Unity output.

The following is a **schema example of deliberate operator choices**, not a
claim about an original cabinet's defaults or the current match. Replace it
with the actual intended selection; do not silently infer any omitted value.
`team` is a source team index for team/profile records, but a Home/Away side
(`0`/`1`) for station/possession/presentation side fields.

```json
{
  "schema": "original-source-match-package/v1",
  "profile": 1,
  "station_layout": 4,
  "home_team": { "team": 0, "records": [0, 1, 2, 3, 4, 5, 6] },
  "away_team": { "team": 1, "records": [0, 1, 2, 3, 4, 5, 6] },
  "stations": [
    { "id": 0, "team": 0, "initial_player_id": 3, "profile": { "team": 0, "record": 3 } },
    { "id": 1, "team": 1, "initial_player_id": 10, "profile": { "team": 1, "record": 3 } }
  ],
  "first_possession": 0,
  "captain": 0,
  "time_adjustment": 2,
  "difficulty": 5,
  "continue_after_cabinet_gate": true,
  "home_play": null,
  "away_play": null,
  "presentation": {
    "designated_home_side": 0,
    "station_heads": [-1, -1, -1, -1],
    "pup_bigball": 0
  },
  "native_gamepads_enabled": false
}
```

`profile: 1` explicitly means `OriginalRelease57Hz`; legacy is rejected.
Four station-head entries preserve original `-1` (explicitly absent) through
`19`, and `pup_bigball` preserves the **whole signed source int**, not a boolean.
Station profiles may explicitly be `null`. A non-null play has required fields:
`symbol`, `kind` (`0` offense/`1` defense), `slot`, `page`, `pool`, `pool_index`.
Unknown slot/page are explicit `-1`. `pool: null` requires `pool_index: -1`;
otherwise preserve the actual weighted pool and ordinal. A supplied symbol must
exist in the pinned P1 catalog. No menu selection is invented for a null play.

## Licensed isolated import and fresh-domain verification

**Do not run these commands without a legitimately authorized Editor and the
parent's isolated-candidate approval.** Never use the repository root project.
The source, frozen B1 dependencies, HUD shader, and original-team resources must
already be composed in that isolated candidate; a createProject licensing probe
does not meet this prerequisite.

Choose absolute local paths for `UNITY`, `PROJECT`, `HUD_ROOT`, `P1_ROOT`, and
`CONFIG_ROOT`. `PROJECT` must be the approved isolated B1 candidate, not an
automatically selected old inspector or hot M1 project.

```bash
CONFIG_SHA=$(sha256sum "$CONFIG_ROOT/configuration.json" | cut -d' ' -f1)
LOG_ROOT="$PROJECT/Build/interface-package"
mkdir -p "$LOG_ROOT/process-local"
INPUTS=(
  -interfaceAssetRoot "$HUD_ROOT"
  -interfaceManifest interface.json
  -interfaceManifestSha256 258d5f4361d7da852b1446e774c5aad7d389ed268daabf3d4971c68a29fd8435
  -interfacePlaybookRoot "$P1_ROOT"
  -interfacePlaybook catalog.tsv
  -interfacePlaybookSha256 b180291759fb22c8ae7b2de7d417867e15243f6071bb1a672f11bb2fb6da4bd3
  -interfaceConfigurationRoot "$CONFIG_ROOT"
  -interfaceConfiguration configuration.json
  -interfaceConfigurationSha256 "$CONFIG_SHA"
  -interfaceStableId original-interface-b1-v1
)
env TMPDIR="$LOG_ROOT/process-local" TMP="$LOG_ROOT/process-local" TEMP="$LOG_ROOT/process-local" \
  "$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod Blitz.Editor.SourceTeamInterfaceImporter.ImportFromCommandLine \
  -logFile "$LOG_ROOT/import.log" "${INPUTS[@]}"
```

The importer rejects an existing destination and any existing HUD/services
Resources asset; it never overwrites, deletes, repairs, or creates duplicate
resource keys. A failed partial import remains visible for inspection; do not
rerun over it or manually erase original assets to make a test green.

Generated resources are exactly:

```text
Assets/Imported/<stable-id>/Resources/BlitzSourceInterface/
  original-interface.asset
  original-match-services.asset
  interface.json
  Bytes/interface.bytes
  Bytes/playsel8.bytes
  Bytes/psarrow1.bytes
  playbook.bytes
  configuration.json
```

Unity imports `.bytes` as exact `TextAsset` bytes; `.tsv` is not relied on as a
Unity text importer. Runtime keys are
`BlitzSourceInterface/original-interface` and
`BlitzSourceInterface/original-match-services`. All generated assets, raw art,
converted catalogs, and **every generated metadata file** remain local/ignored
under `Assets/Imported/` (including ignored `Assets/Imported.meta`). Never add
these outputs to Git. Only the new script metadata is source-controlled.

The import's same-domain check is **not** reload or native pixel proof. After
the import process exits, run a **new** licensed process with the same `INPUTS`
and `PROJECT`:

- `-executeMethod Blitz.Editor.SourceTeamInterfaceChecks.VerifyBindingsFromCommandLine`
  with `-batchmode -nographics -quit`: reloads saved ScriptableObjects and
  Resources, compares every explicit setting, exact original bytes/paths and
  shader source/reference, native texture geometry/samplers, and the shared117
  instance. No framebuffer/pixel claim.
- `-executeMethod Blitz.Editor.SourceTeamInterfaceChecks.VerifyFromCommandLine`
  with `-batchmode -quit` and **without `-nographics`**: additionally copies and
  reads back **all** original atlas RGBA pixels on a real graphics device.
  Null graphics/copy support fails visibly. Use distinct log files.

Reports go into the candidate's `Build/source-interface-native-bindings.json`
or `Build/source-interface-native-pixels.json`. Neither report proves the
native/browser game, impact camera/effects/audio, historical scheduler/RNG,
complete source parity, or a frame-rate target. The visible browser preview's
approximately60-frame/s baseline has not been changed or remeasured here.

## Source/pure/API checks without launching an Editor

Provide explicit protected roots; there is no hidden Primary-M1 fallback:

For a clean, stable checkout containing the integrated source, all three roots
can explicitly be `.`. The runner guards the complete source inventory and
hashes before and after execution; a checkout directory is not automatically
classified as an actively edited Primary worktree.

```bash
python3 -B tests/presentation/interface-packaging/run.py \
  --b1-root "$B1_ROOT" --ui-root "$FROZEN_UI_ROOT" --team-root "$TEAM_DEPENDENCIES"
# Add BOTH --hud-root "$HUD_ROOT" --playbook-root "$P1_ROOT" for actual-data checks.
python3 -B tests/presentation/interface-packaging/mutations.py \
  --b1-root "$B1_ROOT" --ui-root "$FROZEN_UI_ROOT" --team-root "$TEAM_DEPENDENCIES" \
  --hud-root "$HUD_ROOT" --playbook-root "$P1_ROOT"
python3 -B tests/presentation/interface-packaging/api_compile.py \
  --unity-editor-data "$UNITY_DATA" \
  --b1-root "$B1_ROOT" --ui-root "$FROZEN_UI_ROOT" --team-root "$FROZEN_TEAM_ROOT" \
  --team-manifest "$FROZEN_TEAM_MANIFEST"
```

The API check reuses the existing stage caller's explicit protected-input
inventory, compiles a **runtime assembly first**, then real new Editor callers
with their real `PlayerAssetImporter` dependency, then the complete frozen
Team Editor baseline. It records warnings as errors and separately checks the
real WebGL API. An explicitly routed parent four-null initializer overlay can
be supplied with **both** `--dependency-overlay` and
`--dependency-overlay-sha256`; no input is silently patched or diagnostics
suppressed. A relocated installed target module must be supplied explicitly
with `--webgl-module`.

### Qualification at the October16,2026 follow-up

- The one installed6000.6.5f1 diagnostic exited198 without the required headless
  entitlement. No further Editor invocation is authorized, and no new package
  has actually been imported/saved/reloaded or GPU-checked.
- Actual6000.3.24f1 project-compatible APIs compile the new pure runtime package
  and new Editor callers. Compiling the **existing** frozen Team Editor baseline
  as a separate assembly exposes two old `AuthorizedAssetRoot.DecodeUtf8`
  internal-visibility calls in `SourceTeamImporter`/`SourceTeamChecks`. This is
  not hidden by the earlier all-in-one43-source API check; parent-routed shared
  helper/public-visibility or frozen-caller repair is required.
- The old four release CS0649 diagnostics are **resolved** by the separately
  owned parent four-null initializer overlay
  `6f5043f50a36426ac59065e68724a826b35877cbc81d162da3caa93300e93f52`.
  The protected B1 bytes stay unchanged.
- Installed6000.6.5f1 also rejects the frozen Team
  `SourceTeamPresentationAsset.Materials` call to `GetInstanceID` with CS0619.
  No frozen source, version/settings, or API was replaced to conceal it.
- Original audio decoding is not a complete source sound consumer. Synchronous
  actor-stage RNG/state, DSP3 mixing/timing, and actual impact/FX consumers remain
  separately qualified blockers; no silence/noop/fake-click consumer is bound.

## Explicit Movement157 profile — not a117/56 replacement

The completed original selected bundle has **157 clips /13,490 frames** and
manifest SHA256
`f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e`.
The exact new runtime identities are:

| Resource | New identity |
| --- | --- |
| Team/template stable ID | `original-teams-movement157` |
| Team presentation | `BlitzSourcePlayers/original-teams-movement157/team-presentation` |
| Animation `SourceAnimationLibraryAsset` | `SourceData/local-plyr266-movement157-f0ae9395` |
| Original impact catalog | `BlitzSourcePlayers/original-teams-movement157/impact-library` |
| Match services | `BlitzSourceInterface/original-movement157-match-services` |
| Identical shared HUD | `BlitzSourceInterface/original-interface` |
| Runtime movement `TextAsset` | `SourceData/movement` |

The animation asset path is
`Assets/Imported/original-teams-movement157/Resources/SourceData/local-plyr266-movement157-f0ae9395.asset`.
The old `original-teams`117 asset, old56 assets, and intermediate156 identity
remain distinct. A mismatch in stable ID, key, manifest, clip count, or frame
count fails visibly. Providers, template creation, field team selection,
cinematic lookup, and services import all use the same explicit profile.

`R_SKIP` is **selected/uncompressed**, not an unselected asset: original
`A_SELECTED=0x4000`, `A_COMPRESSED=1`. Its missing right-hand limb5 is reproduced
by the authorized original `wan2seq` global-buffer replay from preceding
selected `Huffrun2`, not guessed poses, a flag rewrite, or an alias. The sole
typed reader extension and completed converter recipe are documented in
[`../animation/README.md`](../animation/README.md). All156 preceding clip bytes
are unchanged. No resource asset has yet been imported under a licensed Editor.

### Mandatory actual movement/settings inputs

The M1 importer additionally requires the actual runtime movement payload
(`9520` bytes), SHA256
`89c79007e04e9a17894c126c34dc66c199eb5a5961a4c749b1eb6b3e0e3951d6`,
not test `Responses`. It also requires a separately SHA-pinned explicit
bootstrap. Its exact nine flat keys are:

```json
{
  "schema": "original-source-movement-bootstrap/v1",
  "field_index": 6,
  "infinite_turbo_team_mask": 2,
  "speed_powerup": true,
  "fast_turbo_team_mask": 1,
  "run_out_of_bounds_team_mask": 3,
  "violence_powerup": false,
  "source_hres": 512,
  "source_vres": 256
}
```

This is a **deliberately selected example**, not historical default settings.
Supply the actual intended values. The three masks retain side bits0..3;
`speed_powerup` is global, not a team mask. Source DIP raster is **only512x384
or512x256**. Browser/window/render dimensions are not substitutes. Missing,
unknown, duplicate, coerced, or differently pinned data fails.

The match/presentation configuration from the earlier section is independently
mandatory. No home side, heads, roster selection, pup value, field, turbo mask,
or raster is inferred. `SourceInterfaceServices.Initialize` uses the actual
eight-argument overload ending in `(TextAsset bootstrap, string trustedSHA)`;
the historical six-argument call cannot silently create an M1 match.

### Future isolated licensed import

**Not authorized to execute while the installed Editor exits198.** Parent must
first approve a legitimately licensed isolated candidate composed from the
matching M1 Core/UI/Team/reader sources. No root Editor, activation workaround,
hosting change, or standalone viewer qualifies this game.

Use the same actual prepared all31 team/impact inputs, but the completed157
animation bundle and these **new** entrypoints:

```text
-executeMethod Blitz.Editor.SourceTeamImporter.ImportMovement157FromCommandLine
-teamAssetRoot <ABS_PREPARED_ALL31>
-teamCatalogSha256 c9e875052544c2d7e30d6cdab4bcf9a3b38d552adfa9deef8da5d35ab0ceb62c
-teamAnimationRoot <ABS_COMPLETED157_BUNDLE>
-teamImpactRoot <ABS_PREPARED_ORIGINAL_IMPACT>
-teamImpactSha256 794b1fa2865733677d08d5c465dad6a002966750a7d0523138f61f3a25be1fb3
```

Then use all earlier explicit HUD/P1/match configuration arguments, a **fresh**
stable ID such as `original-movement157-interface-v1`, and additionally:

```text
-executeMethod Blitz.Editor.SourceTeamInterfaceImporter.ImportMovement157FromCommandLine
-interfaceMovementRoot <ABS_RUNTIME_MOVEMENT_DIRECTORY>
-interfaceMovement <EXPLICIT_RELATIVE_RUNTIME_MOVEMENT_JSON>
-interfaceMovementSha256 89c79007e04e9a17894c126c34dc66c199eb5a5961a4c749b1eb6b3e0e3951d6
-interfaceBootstrapRoot <ABS_EXPLICIT_BOOTSTRAP_DIRECTORY>
-interfaceBootstrap <EXPLICIT_RELATIVE_BOOTSTRAP_JSON>
-interfaceBootstrapSha256 <ACTUAL_TRUSTED_BOOTSTRAP_SHA256>
```

Same-content shared HUD/movement resources are checked and reused **without
overwrite**; duplicate keys, changed pins, or matching-uniform substitutes fail.
New services/configuration/playbook/bootstrap are saved under the new interface
destination. Runtime movement is a real `.json` TextAsset in
`Resources/SourceData/movement.json`; bootstrap is a saved serialized reference
`Resources/BlitzSourceInterface/movement-bootstrap.json`. Every generated art,
catalog, resource, and metadata file remains ignored under `Assets/Imported`.

Fresh-domain checking entrypoints:

- `Blitz.Editor.SourceTeamChecks.VerifyMovement157FromCommandLine`
- `Blitz.Editor.SourceTeamInterfaceChecks.VerifyMovement157BindingsFromCommandLine`
  (same explicit arguments, binding proof only).
- `Blitz.Editor.SourceTeamInterfaceChecks.VerifyMovement157FromCommandLine`
  (real graphics required, verifies every atlas pixel; no `-nographics`).

### Current-camera producer boundary

Runtime constructs `PlayCamera(transform, actualPrefab)`, explicitly configures
`ConfigureSourceMovementRaster(bootstrap.HorizontalResolution, bootstrap.VerticalResolution)`,
then registers the existing raw-mode camera overload with
`simulation.SourceCameraRawGameMode`. Inside the **existing pre-player callback**:

1. `SampleAtSourceTick(state, tick)` executes once.
2. `RequireSourceCameraAtTick(tick)` returns the immutable cached
   `SourceCinematicTransform` (`Position`, all12 `Transform` floats).
3. Core receives `SetSourceMovementCameraFrame(new SourceMovementCameraFrame(
   frame.Position, frame.Transform, explicitHres, explicitVres), tick)` before
   the provider returns.

The pure frame stamp rejects initial/render-only, stale, future, suspended,
duplicate, and reset generations. Render does not advance that clock or alter
M1's original-DIP source aspect. Core retains its actual post-actor14x15 joint
cache and previous-cache/current-matrix LEAP semantics; no fresh `WorldJoint`,
Unity matrix, yard-space conversion, or fabricated heading is used by the
frame getter.

`update_cambot_xform`/`magv`/`norm`/`vxv` match **all12 binary32 floats across
15,242 unchanged original-C camera frames**. This proves matrix algebra and
cached frame transport, **not** complete original C1 normal-camera targeting:
the existing qualified normal-target model is retained, with B1/graybox
implementation unchanged. Native/browser pixels, target scheduler/libc, sound
DSP/SPFX, complete gameplay parity, and all16 full-parity rows remain open.

### Permanent current-checkout checks

No default command uses an ignored authoring snapshot or assumes ROOT is hot:

```sh
python3 -B tests/presentation/movement-stage/run.py
python3 -B tests/presentation/movement-stage/mutations.py
python3 -B tests/presentation/interface-packaging/run.py --b1-root . --ui-root . --team-root .
python3 -B tests/presentation/movement-stage/api_compile.py --unity-editor-data "$UNITY_DATA"
```

The API command compiles ALL current runtime and Editor sources as **separate**
assemblies, C#7.3/netstandard2.1, warnings as errors, against real6000.3.24
Editor and WebGL APIs. It does not execute an Editor.

For original-C, actual157, and real package-data proof, explicitly add:

```sh
python3 -B tests/presentation/movement-stage/run.py \
  --original-source-root "$ORIGINAL_SOURCE" --bundle157-root "$BUNDLE157" \
  --hud-root "$HUD_ROOT" --playbook-root "$P1_ROOT" --movement-data "$MOVEMENT_JSON"
```

The pre-existing approximate60-frame/s browser preview is untouched; no new
M1 browser performance or visual parity is claimed.

## Explicit SoundVM service/data profile

This is a dependent resource/transport version, **not DSP playback**. All
Movement157 team/prefab/library/impact/HUD/runtime-movement/bootstrap/source-DIP
identities above remain unchanged. Two new identities are explicit:

| Resource | Exact key |
| --- | --- |
| New match services | `BlitzSourceInterface/original-movement157-soundvm-match-services` |
| SAFN1 `TextAsset` | `SourceData/sound-functions-safn1-4a557304` |

Required SAFN1 is exactly **1983 bytes**, trusted SHA256
`4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280`.
The binary contains the original frozen sound-function parameters and exact
six implemented ANI_CODE tuples. It is not a PCM bank, test `Responses`, a
missing-data default, or an inferred hash. Original numerical data, its raw
input, converted output, generated resource and metadata remain ignored/local.

`SourceInterfacePackagePlan.WithSourceSoundVm` explicitly derives a **new**
package from an actual157 plan and an authorized sound root/relative path/fixed
pin. The existing B1/M1 constructors keep their original keys and do not gain
sound data. A new sound consumer rejects those old resources before import;
it does not reuse their old key as though unchanged.

### Future isolated licensed import — still blocked

No further Editor invocation is authorized while legitimate startup exits198.
When parent approves an actual licensed isolated candidate, use every required
Movement157/HUD/P1/match/bootstrap argument above, a **fresh** stable ID such as
`original-movement157-soundvm-v1`, and the new entrypoint/three additional inputs:

```text
-executeMethod Blitz.Editor.SourceTeamInterfaceImporter.ImportSoundVmFromCommandLine
-interfaceSoundVmRoot <ABS_AUTHORIZED_SAFN_DIRECTORY>
-interfaceSoundVmData <EXPLICIT_RELATIVE_SAFN_FILE>
-interfaceSoundVmSha256 4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280
```

The exact SAFN bytes become
`Assets/Imported/<fresh-id>/Resources/SourceData/sound-functions-safn1-4a557304.bytes`.
An existing identical canonical data resource is validated and reused, never
overwritten or duplicated. The new services asset is
`Resources/BlitzSourceInterface/original-movement157-soundvm-match-services.asset`.

The actual new services initializer preserves the eight-argument M1 prefix and
requires **two more** arguments: `(TextAsset soundVmFunctions,
string trustedSoundVmSHA)`. Its serialized reference, canonical Resources
identity, fixed pin and exact bytes must agree. `CreateSimulation` constructs
the real mandatory M1 kernel, then calls
`BindSourceSoundVmData(SoundVmData)` **before returning and before the first
executing57-Hz source wake**. Restart retains that binding. The old eight-argument
initializer is not a muted/legacy fallback.

New fresh-domain checks, with the same explicit sound/M1 input arguments:

- `Blitz.Editor.SourceTeamInterfaceChecks.VerifySoundVmBindingsFromCommandLine`
  checks saved resource/configuration references and exact SAFN bytes/pin.
- `Blitz.Editor.SourceTeamInterfaceChecks.VerifySoundVmFromCommandLine` additionally
  requires real graphics for existing complete atlas-pixel checks.

Neither has run while the native license blocker remains. No saved resource,
pixel, audible output, or browser game claim follows from source compilation.

### Already-resolved sound transport

Exactly six original VM sound tuples are selected **synchronously inside Core
ANI_CODE**, using its actual state, shared RNG and process context. Detached
`SourceSoundVmDispatch` batches retain the same `OriginalCue`, result, ordered
raw calls and draw trace. Delivery never selects or consumes RNG again.

Runtime drains FIFO after each actual host `Step` (including partial failure),
pairs by `ReferenceEquals(OriginalCue)`, and routes a paired `Resolved` dispatch
to `PlayCamera.ConsumeResolvedSourceSound(dispatch, actualRawOutputConsumer)`,
**instead of** the old generic callback. `SourceCinematicPlayback` reuses its
original source-site/operand validation and consumes global cue order once.
`PlayerDown` remains the existing already-executed ordering token.

- `Resolved` with an actual completed zero-call result is legitimate source
  control flow, not fabricated silence or a request to draw RNG.
- `Unsupported`, `MissingInput`, absent results, and other unpaired
  speech/dust/SPFX callbacks remain visible unsupported/fault paths.
- A nonempty resolved result requires an explicitly supplied real typed raw
  output consumer. With no such consumer, delivery throws visibly. No isolated
  preview adapter, fake click, noop, clipping/scaling of raw uint fields, mixer,
  pan/priority interpretation or DSP firmware guess is installed here.
- Original cue reference/subcall order are preserved; duplicate delivery or
  retry through the generic callback is rejected.

Current standalone checkout commands require no ignored authoring roots:

```sh
python3 -B tests/presentation/sound-runtime/run.py
python3 -B tests/presentation/sound-runtime/mutations.py
python3 -B tests/presentation/sound-runtime/api_compile.py --unity-editor-data "$UNITY_DATA"
```

For actual positive-data, original-catalog consumer, and full package guards:

```sh
python3 -B tests/presentation/sound-runtime/run.py \
  --sound-vm-data "$SAFN_FILE" --impact-catalog "$ORIGINAL_IMPACT_JSON" \
  --hud-root "$HUD_ROOT" --playbook-root "$P1_ROOT" --movement-data "$MOVEMENT_JSON"
python3 -B tests/presentation/sound-runtime/mutations.py \
  --sound-vm-data "$SAFN_FILE" --impact-catalog "$ORIGINAL_IMPACT_JSON" \
  --hud-root "$HUD_ROOT" --playbook-root "$P1_ROOT" --movement-data "$MOVEMENT_JSON"
```

The API command reuses the permanent current-checkout compiler: ALL real runtime
sources and ALL Editor sources compiled separately, warnings as errors,
C#7.3/netstandard2.1, actual6000.3.24 APIs. No Editor is executed. DSP3
firmware/protocol/mixing, all remaining sound/SPFX functions, C1/full normal
camera/target scheduler/libc, native/browser/rendered/audible behavior, and all16
full-parity rows remain explicitly unqualified.

The playback boundary is **not missing public PM/DM images**: all eight public
images exist, and CRC deployment wrapping is resolved. Exact original playback
still requires establishing the source `TARGET_SYS`/`localset`/build identity
(Seattle ADSP2115 versus Vegas ADSP2104), the matching legitimate32768-byte
bootstrap (`sound102.u95`, SHA1 `db80aa4a645804a4574b07b9f34dec6b6b64190d`,
or `vegassio.bin`, SHA1 `f6e8405cfa604528c0224401bc374a6df9caccef`), and a full
matching DCS2 board runner. The existing pinball decoder is not that runner.
Neither target/bootstrap match nor full-board behavior is established here.
No download, alternate-target assumption, firmware implementation or DSP output
sink was added; unbound nonempty raw sound output continues to fail visibly.
