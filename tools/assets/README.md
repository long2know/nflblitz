# Original player/team presentation inputs

Original sources, art, motion, prepared catalogs, imported Resources and native
proofs stay ignored/local. These tools do not download anything or grant
redistribution rights. Supply the explicitly authorized original inputs.

## Existing one-player control

`prepare_player.py` remains the immutable normal-body Chicago-home/PC_WHITE
preparation. It is a conversion control, **not** an opposing-team renderer.
Its original recipe and output are unchanged.

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
