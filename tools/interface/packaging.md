# Original B1 HUD and match-service resource packaging

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
