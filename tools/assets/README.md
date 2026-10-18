# Original player/team presentation inputs

## Original popped-head SDL reconstruction

`models/head.h` is not in the pinned original tree, but **the authoring geometry
is present**: `models/phead.sdl` at `89f840d58d152a2a9f230cb8d0cdffbd554c8ded`,
Git blob `23410e01bb6db9e5fd9bacdd9440691147046798`,39,272 bytes,
SHA256 `3fb4e566b57670e397fde16c6f515d06c95379420074c2d6c7b111bca843e257`.
It is the original83-vertex/161-ST/157-triangle `phead_obj`, not a Mark/custom
head, helmeted model or generic replacement.

Prepare only from explicit verified private originals:

```sh
python3 -B tools/assets/prepare_popped_head.py \
  --original-sdl "$ORIGINAL_PHEAD_SDL" \
  --calibration-models "$PINNED_CALIBRATION_MODELS" \
  --output .squad/.scratch/popped-head-bundle
```

The calibration directory contains the exact original `plyr266.sdl/.h` and
`plyr498.sdl/.h` pairs; their content pins are in the preparer. All481 vertices /
1,443 coordinate scalars match the original generated headers after binary32
scale1.2 and six-decimal generated-C rounding. All773 expanded triangles match
original vertex topology, winding and texture identity. The291 HEDSHU skin UV
corners are checked within the original six-decimal SDL export precision;
the measured maximum source-unit delta is0.0000152587890625. Existing packed
uniform `c_hack` UV remaps are not replaced by the head conversion.

The source `models/models.mak` recipe is
`cvtmod -f 1.2 -z -l -s -t < phead.sdl > head.h`.
The source-authored output uses the original scale, square256 ST units and
original baked coordinates. It explicitly retains SDL polygon order:
**native `-z` qsort order, generated-header byte identity and native pixels remain
unqualified**. `-l`/`-t` suppress lists; they are not pivot/geometry transforms.
This is not a claim to recover a missing Composit/native tool output.

Active `player.c:6900–6909` substitutes `limb_phead_obj` at `JOINT_NECK`8 and
sets the first TRI texture code to4. Active `lib/ani3d/geom.s:309–317,569–577`
adds that code directly to the32-bit decal-pointer table: **raw4 means logical
HEDSHU slot1**, not logical SLVMSK slot4. The same stride-four interpretation is
used by the production generated-model reader. The actual actor's original
HEDSHU skin texture is retained. No extra head texture or uniform tint is made.

The private bundle contains `popped-head.json`, the byte-identical original SDL,
and a clearly named `phead.source-authored.h`. The production
`SourceTeamPoppedHeadData` reader verifies both original/derived content pins,
schema, paths, original inventory and material semantics. The shared existing
`GeneratedPlayerReader`/`BindPoseMeshBuilder` produce the real head mesh against
the existing neck bind pivot/handedness, without changing body poses or rules.
`SourceTeamPresentationModel.Select` uses the supplied validated head service
only for actual `HelmetPopped` with no station-custom-head selection. Every
original station custom head retains active-C priority. Missing required data
still stops visibly; no flag is cleared or helmet/head fallback caught.

All original SDL, generated numeric headers and mesh payloads remain ignored.
Only converter/reader code, metadata and independently authored synthetic tests
are public:

```sh
python3 -B -m unittest discover -s tools/assets -p test_prepare_popped_head.py
node --test tools/source-player/popped-head.renderer.test.mjs
```

### Explicit Unity head import (source/API-compiled; never an Editor execution claim)

First import a **new** team resource through the existing
`SourceTeamImporter` profile entrypoint. A new team asset initialized by this
source carries a pending head-extension marker; existing serialized assets do
not. Then, only under a separately authorized licensed Editor lease, use:

```text
-executeMethod Blitz.Presentation.SourceTeamPresentationAsset.ImportOriginalPoppedHeadFromCommandLine
-teamPresentationAsset Assets/Imported/<new-profile>/Resources/BlitzSourcePlayers/<new-profile>/team-presentation.asset
-teamAssetRoot <absolute verified prepared-teams root used for that fresh import>
-teamPoppedHeadRoot <absolute private popped-head-bundle>
-teamPoppedHeadSha256 <actual popped-head.json SHA256>
```

This owned typed-asset entrypoint is head-only, not a duplicate team importer.
It verifies the same strict phead reader and original skeleton/model inputs,
reserves a new `OriginalPoppedHead` subfolder, creates one source-authored mesh
and byte-identical provenance TextAssets, and attaches them to the fresh typed
asset. Existing/prior head resources and assets without the pending marker are
rejected, not overwritten. The existing Editor importer file/default profile
entrypoints are unchanged.

At runtime `SourceTeamPresentationAsset` revalidates vertex/UV/index/material
content against the source-derived mesh and exact serialized original neck
pivot. `SourceTeamPresenter` and `SourcePlayerRig` select/cache the distinct
popped-head appearance while preserving station-custom-head priority and all
other model/material choices. Import, activation, license access and Unity
execution are **not** performed by headless/archive-API validation.

**Explicit C2-M1 successor resource registry:** `original-teams-contact281-c2-m1`
uses the same canonical e874 animation but strict new fourteen-binding callback
JSON,482-site catalog and services keys. See
`../interface/packaging.md`. Explicit URL/build admission uses Parent's
authorized single shared-selector guard addition; default/old-nine behavior
is unchanged. This is not M1/gameplay/CI/native qualification or a rendering
change.

**Explicit C2 successor:** `original-teams-contact281-c2` binds canonical281/
26,230 frames,477 original impact/callback sites and separately pinned C2 JSON/
ONE7538 sound extension. Use the C2 producer and package sections in
`../animation/README.md` and `../interface/packaging.md`. C1 remains default;
the C2 URL/build/import entrypoints are explicit and do not replace old assets.
No mesh/shader/team art, source-DIP or roster/head/jersey/pup redesign is made.

**Current C1 generation:** use the explicit `original-teams-contact272` profile,
`SourceTeamImporter.ImportContact272FromCommandLine` and
`SourceTeamInterfaceImporter.ImportContact272FromCommandLine` (future licensed
surfaces, not authorization to launch Unity). Actual272 motion is prepared by
`tools/animation/prepare_contact_profile.py`, manifest
`9cae962bbb861bffe4d379bc30919cf34c0238ed35be82e605253d3059cfede9`;
its new impact catalog is explicitly bound to that same manifest. All31 team
art/roster/skin/head/BCD-number semantics remain unchanged. Historical157
resources below are not a C1 fallback. See the current Contact272 section of
`../interface/packaging.md`; no native import/browser/DSP proof is asserted.

The dependent sound-runtime service uses a **new** services key
`BlitzSourceInterface/original-movement157-soundvm-match-services` and exact1983-byte
SAFN1 data at `SourceData/sound-functions-safn1-4a557304`. It does not rename or
replace any157 team/library/impact/HUD/movement/DIP asset. See the explicit
SoundVM section of [`../interface/packaging.md`](../interface/packaging.md).
Selection is synchronous in Core; already-resolved presentation transport is
not DSP playback, and unbound nonempty output fails visibly.

For the **new M1 checkout**, use the explicit Movement157 profile below and
[`../interface/packaging.md`](../interface/packaging.md). The117 recipe is retained
as an honest B1 profile, not repointed to new motion.

Original sources, art, motion, prepared catalogs, imported Resources and native
proofs stay ignored/local. These tools do not download anything or grant
redistribution rights. Supply the explicitly authorized original inputs.

## Existing one-player control

`prepare_player.py` remains the immutable normal-body Chicago-home/PC_WHITE
preparation. It is a conversion control, **not** an opposing-team renderer.
Its original recipe and output are unchanged.

## Completed Movement157 team/resource profile

The completed converter-state bundle has157 clips/13,490 frames, manifest SHA256
`f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e`.
`tools/animation/prepare_movement_complete.py` consumes explicit original source
and the unchanged156 base; it does not rewrite WAN flags or invent missing
poses:

```sh
python3 -B tools/animation/prepare_movement_complete.py \
  --source-root "$ORIGINAL_SOURCE" --selected-bundle "$BUNDLE156" \
  --destination "$NEW_BUNDLE157"
```

`R_SKIP` was selected/uncompressed (`0x4000`), with absent right-hand limb5.
The original `wan2seq` shared-buffer replay from selected `Huffrun2` supplies
that exact converter state. All156 preceding clip bytes remain unchanged.

Use `Blitz.Editor.SourceTeamImporter.ImportMovement157FromCommandLine` with the
same all31 prepared team and original impact inputs, but
`-teamAnimationRoot "$NEW_BUNDLE157"`. Actual new identities are:

- Template/stable ID: `original-teams-movement157`.
- Team: `BlitzSourcePlayers/original-teams-movement157/team-presentation`.
- Animation: `SourceData/local-plyr266-movement157-f0ae9395`.
- Impact: `BlitzSourcePlayers/original-teams-movement157/impact-library`.
- Match services: `BlitzSourceInterface/original-movement157-match-services`.

The actual prefab provider, field team selector, camera resource selector,
importer, and services consumer agree on these identities and reject117/56/
partial156 aliases. Match configuration, source movement data, actual powerup
masks, and original DIP settings are explicit package inputs; see the interface
recipe. The old B1 `ImportFromCommandLine` still requires its immutable117
profile. A current M1 services consumer fails visibly if called as old B1.

No new assets have been imported or GPU-checked while legitimate Editor access
remains blocked (exit198). All original art/motion, converted resources and
generated metadata stay ignored/local. Source/API tests do not prove a native
or browser game, full original normal-camera targeting, sound/SPFX, or any of
the16 full-parity rows.

## All31 opposing-team preparation

The consumer is the final `tools/rosters/export_rosters.py` dataset, SHA256
`f747741e95766185e0c615ded549d1ce2f5fec0c48ed2ecb29ba1b755764539b`.
Preparation reconstructs the complete pinned Git tree before checking every
consumed source blob. It uses the existing strict WMS/TGA/path helpers.

```sh
python3 -B tools/assets/prepare_teams.py \
  --source-root .squad/.scratch/source-presentation/source \
  --rosters .squad/.scratch/SourceData/rosters.json \
  --original-tools-root .squad/.scratch/source-presentation/midwayvideo \
  --destination .squad/.scratch/source-presentation/prepared-teams-all31 \
  --confirm-local-use \
  --authorization-reference owner-directed-original-team-presentation-2026-10-16
```

The source root contains the original named files and complete `tree.json` for
`historicalsource/nflblitz2000@89f840d58d152a2a9f230cb8d0cdffbd554c8ded`,
tree `a7e0352125d46075b7d98e87253ab89b9160a4c7`. The original-tools root
contains the ten pinned `tools/c_hack` inputs from
`historicalsource/midwayvideo@1792293aaaea00a919b59e8c05716978e85b4333`;
their exact blob pins are in `original_uniform_packer.py`.

For Browns/Titans, the tool follows the **active original**
`img3d/makefile -> c_hack plyr.ld -> uni.bat` recipe. It compiles unchanged
original `c_hack.c`, packing, bagger and TGA routines, with a small four-input
host driver. The four original packed authoring atlases are not Oilers aliases
or guessed layouts. Missing skin/custom-head WMS inputs use their exact-name
byte-identical authoring TGAs. Representations are explicit:

- 58 present runtime atlases: validated complete ARGB1555 WMS, level0 emitted.
- Four missing runtime atlases: exact original c_hack authoring output.
- Four skin and20 head textures: original authoring TGA bytes.

This supplies all86 selected texture identities. Missing **Texus runtime WMS
quantization** for the latter28 inputs is not claimed reproduced.

`SourceTeamGeneratedModels` reuses the strict generated-C reader and mesh builder
for all32 actual source model selectors and20 actual custom heads. The importer
compacts empty submeshes, retains original material-slot identities, binds the
actual BCD number surfaces to torso matrix1, and never multiplies team colors.
The ten-column number atlas is retained; the pinned archive does not contain the
`draw_jersey_tris` implementation, so its target rounding/depth bias remains
qualified rather than falsely certified.

The active Blitz2000 clash matrix chooses H/A by **source team IDs and designated
home side**. With no clash, both sides use HA_HOME. Physical away does not imply
HA_AWAY. Skin, body size, packed number and actor scale come from each actual
static roster record. Secret heads belong to human stations, not fabricated
static roster fields.

## Original impact presentation

```sh
python3 -B tools/assets/prepare_cinematics.py \
  --source-root .squad/.scratch/source-presentation/source \
  --destination .squad/.scratch/source-presentation/prepared-cinematics-v2
```

This emits seven active camera tables,25 weighted entries (including duplicates
and explicit no-selection), the original64-entry shaker envelope,416 script
opcodes/callbacks, and five active `detect.c` camera sites. Raw arrays stay local.
It does not turn all tackles into camera effects or introduce slow motion.
The source's replay table is disabled in Blitz2000.

`SourceCinematicPlayback` accepts actual ordered executed source cues, selected
CSI ordinals, camera-process ticks and actor targets. It rejects duplicate camera
consumption, missing intermediate active samples and substitute RNG. Ground
timing must come from the actual VM's AA_TOGROUND/callback execution, not velocity.

The first concrete existing Core integration is `fall_04`:
`ANI_SETCAMBOT csi_rs_suplex` at `plyrseq.c:15969`, gated by actual
`plyr_have_ball`, immediately after `ANI_NEWSEQ SEQ_FALL_04`. Its shaker `(2,10)`
is at16000 **after** the actual ground branch. These cues are currently absent
from the older reduced gameplay VM; the frozen B1 producer now publishes them
through typed, ordered cues. The complete original predicate is **false during
GM_IN_PLAY**; otherwise a popped helmet passes without RNG, or the actual carrier
passes `randrng(100)<90`. Only a passing gate consumes CSI `randrng(1)`.
Rendering never changes gameplay or replaces that predicate to force the effect.

## Explicit Unity import

Run only under the coordinator's exclusive Editor lease, in the selected project:

```text
-executeMethod Blitz.Editor.SourceTeamImporter.ImportFromCommandLine
-teamAssetRoot <absolute prepared-teams-all31>
-teamCatalogSha256 <actual team-presentation.json SHA256>
-teamAnimationRoot <absolute final117 bundle>
-teamImpactRoot <absolute prepared-cinematics-v2>
-teamImpactSha256 <actual impact-presentation.json SHA256>
```

The importer reserves the new ignored `Assets/Imported/original-teams` identity.
It never overwrites the old `local-plyr266` prefab or56-clip library. It requires
the actual final117 portable manifest
`f3c34d90337c1432a0eb7a7a59fd3d583e9130946283ae8d929e008e0e1fd274`.
The old Core18af and QA2baec manifests are not aliases for those bytes.

`FieldScene.Render` consumes actual `ISourcePresentationState` directly, including
raw actor/ball flags, original player mode, explicit designated home side and
station heads. `BindSourceTeamDrawState` remains an explicit alternative.
Missing data fails visibly rather than restoring fourteen matching uniforms.

Runtime registration, before the first source wake:

```csharp
playCamera.ConfigureSourceStage(State, () => simulation.SourceCameraStageOrder,
    simulation.DrawSourceCameraRandom, simulation.SourceActorWorldPosition);
RegisterSourceCameraTickProducer(playCamera.SampleAtSourceTick);
SourcePresentationCueRaised += playCamera.ConsumeSourceCue;
AfterHostStep += playCamera.ObserveSourceHostStep;
```

The existing callback runs at verified RL_CAMBOT before AI/actors. It uses the
actual instruction ordinal, canonical source-unit roots and Core's shared camera
RNG. Post-camera actor cues affect the next camera wake, not another render tick.
Explicit PlaySelect/PatSelection suspension does not advance camera counters.
The current Core's published phase lifecycle is used without casting its tags to
raw GM IDs; the optional raw-mode overload is reserved for an actual raw producer.
Full original kickoff/gopher/other unpublished mode branches remain open.

Legacy normal camera behavior is retained. Source-stage rendering uses the full
content rect and cached source frame rather than a second per-render camera step.
A finite Unity depth
adapter covers the actual instantiated world's rendered AABB; original
`field.c:do_os_zones` performs angular/near tests, not a fixed600-yard far cutoff.
Full Glide zone/raster parity is not claimed.

## Tests

```sh
python3 -B -m unittest discover -s tools/assets -p 'test_*.py' -q
python3 -B tests/presentation/run.py \
  --actual .squad/.scratch/source-presentation/prepared-teams-all31 \
  --cinematics .squad/.scratch/source-presentation/prepared-cinematics-v2/impact-presentation.json \
  --source-root .squad/.scratch/source-presentation/source
```

See `tests/presentation/README.md` for actual-source mutation and native proof
commands, evidence boundaries and required integrated browser follow-up.
