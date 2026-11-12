# Original WAN poses and selected-frame clips, offline

These tools are original Python-standard-library code, require Python 3.9+, and
operate only on explicit local input with an expected upstream **Git blob** pin.
Decoded original motion and source WANs stay in ignored `.squad/.scratch/`; only
tool code, metadata-only pins and original synthetic fixtures belong in publication.

## Complete-profile original transition behavior

The complete303 runtime now retains the primary VM pose when `change_anim`
clears the torso, matching `ANIM.C`; it does not invent a composite upper-body
transition origin. Turn hacks use signed integer `heading / 2`, the admitted
original sine table and unnormalized `pipe3D.c:qxq` arithmetic. Other profiles
keep their existing transition policy.

`SourcePlayer.Checks "$COMPLETE_ORIGINAL_DATA" --original-gameplay-sounds-only`
exercises actual cabinet turbo-QB selection, **0.81 ticks/frame**, selected-frame
stream truncation, independent torso/leg masks and neutral carrier transition
through public source wakes and the fourteen-actor matrix consumer. These are
bounded A1 cases, not exhaustive selector/opcode/root or browser-pixel acceptance.

### November 10 source-VM follow-up: two actual behavior fixes

The complete303/306 instance overlay now follows original
`plyrseq.c:8297–8314`: `block_ready_anim_rnd` skips50, then jumps through
`block_ready_loop`'s NEWSEQ, displaying **BLK_RDY0**, not50. The actual
LinePreSnap role producer and jumpback continuation exercise this path through
the public Host, without directly starting an animation. The legacy base
programme is unchanged.

Complete-profile streaming also follows `ANIM.C:_stream_frame`'s exact float
order: scale **each endpoint before interpolating**, then apply the scaled
root/one-shot anchor once. Scaling the blended root afterward was not
bit-equivalent and produced693 exact root/world mismatches in the scaled
slow-stream case. Other profiles retain their previous arithmetic order.

The bounded independent comparison executes the unchanged pinned original
`_ani_inttoseq`, `_ani_intstream`, `_ani_streamtoend`, `_animate_block` and
`_next_frame`, plus original interpolation/trigonometry helpers. Twelve cases
compare **105050 bit-exact** frame/mode/hold/counter/pose/root/world predicates:

* ordinary and random-entry first/middle/last frames and complete loops;
* slow initial−1 streaming, normal QB and exact turbo-QB0.81 loops;
* INTTOSEQ first/middle/last, interruptions before/after half, and proportional targets;
* wait-true hold, one-shot XZ/Y anchors, scale1.5 and repeated-tick no-double-drift;
* independently advancing torso/legs with the original−513 heading turn.

Inputs are genuine admitted selected WAN float poses/roots. The original C
receives decoded float frame memory; an explicit SKIP adapter preserves the
source compressed-frame traversal through unchanged `_next_frame`. This does
**not** assert shipped numeric SEQ IDs, native lossy compression/quantization or
target binary ABI identity. None of those unknowns substitutes for source-defined
frame/root behavior.

An additional **8123-assertion** actual guest/Capture/Advance/SourceStep regression
observes all twelve normal/turbo QB programme names, two receiver runs,
44 genuine pre-instruction0.81 stream entries, neutral two-part collapse and the
same-wake fourteen-actor pose/matrix renderer. Role/placement/energy preconditions
are declared fixtures; animation/selector methods are not directly invoked.
Four cleanly compiled real-source controls remove the GOTO fix, restore late
root scaling, change QB0.81→0.75 or remove same-tick suppression. Each turns its
focused assertion red; forced fresh unchanged component/public baselines pass.

With the explicit new readonly dataset, installed SDK and approved POSIX scratch:

```sh
SOURCE_PLAYER_DOTNET="$DOTNET" SOURCE_PLAYER_DATA_ROOT="$DATA_ROOT" \
SOURCE_PLAYER_POSIX_SCRATCH="$SCRATCH" \
  bash tools/source-player/with-posix-build.sh bash -c '
    set -e
    bash tools/source-player/dotnet.sh build tools/source-player/SourcePlayer.Checks.csproj \
      --no-restore -v:q -p:UseSharedCompilation=false
    bash tools/source-player/dotnet.sh \
      tools/source-player/bin/SourcePlayer.Checks/Debug/net10.0/SourcePlayer.Checks.dll \
      "$SOURCE_PLAYER_DATA_ROOT" --original-animation-core-output "$OBSERVATIONS"
    python3 -B tools/animation/prepare_complete_original.py \
      --source "$ORIGINAL_SOURCE" --vm-observations "$OBSERVATIONS" --output "$NEW_ORACLE_OUTPUT"
    bash tools/source-player/dotnet.sh \
      tools/source-player/bin/SourcePlayer.Checks/Debug/net10.0/SourcePlayer.Checks.dll \
      "$SOURCE_PLAYER_DATA_ROOT" --original-animation-callers-output "$NEW_COVERAGE"
  '
```

The named source call/table closure enumerates269 programme names:17 public-wake
covered, two additional original-C component covered,247 verification-only, and
unreachable `m_hophop_anim` (`randrng(10)<35`). The remaining two
`qb_ready_anim`/`qb_ready_f_anim` programmes existed, but their real native
`tmsel_player_proc` team-screen producer was **still missing in that epoch**,
not merely an untested clip import. All29 enumerated native opcode classes have compiled
lowerings; the other operand/conditional/callback sites are verification-only,
not asserted missing logic or accepted coverage. Three additional public-wake
programme names outside this static closure are reported separately. This
bounded result does not close whole A1/V1, native team-screen animation,
natural-match, browser or pixel acceptance.

### Separate named-TwoStation team-selection producer epoch

The explicit successor
`original-teams-complete-av306-two-station-records-v1` adds an actual UI-driven
team-selection actor lifecycle, not another animation import. It retains all
1153 AV306 payloads unchanged and uses a separately pinned two-station settings
entry. Historical profiles and guest-only admission remain distinct.

After the real pregame source menu consumes physical input, the Host reads the
accepted `Session.CreateConfiguration()` and dispatches the original
`tmsel_player_proc` children on that same source clock. They are actor3/Home,
`QB_RDYF`, heading490/PF_FLIP and actor10/Away, `QB_RDY`, heading512. Both use
the selected team's static quarterback record3, not the selected custom lineup.
Birth140/first wake141 in the finite public proof demonstrates the new empty
process-host factory at a real nonzero clock without changing default0,
BeginTick/Visit/Sleep or birth guards.

Original `player.c:1252–1288` calls `change_anim` **then** `animate_model` before
its first sleep(1), so the first visible frame is1. A strictly caller-bound,
once-per-instance initial continuation represents that original call; the
normal same-tick suppression stays strict. Subsequent source wakes retain both
77-frame loops. Accepted team navigation updates record/uniform without
restarting the animation/process or changing its original initial `ascale`.
Redraws do not advance animation. Visible objects leave with the team screen;
hidden children retire at the real pregame-to-match handoff.

The actual Present packet contains both fifteen-joint full-world matrices,
native MOD498 left/right-held-ball models6/10, BCD jersey geometry and original
home H uniform pixels. `draw_player2` uses its original camera-space projection,
render scale1 and no animated `ay` offset; source `ascale` still drives animation
root motion. The renderer consumes this packet between the actual opaque HUD
backdrop/panels and the first foreground text primitive. No field-camera,
generic avatar, recolor or render-time animation substitutes for it.

The bounded public producer test is:

```sh
bash tools/source-player/dotnet.sh \
  tools/source-player/bin/SourcePlayer.Checks/Debug/net10.0/SourcePlayer.Checks.dll \
  "$NEW_TWO_STATION_DATA_ROOT" --original-team-selection-output "$NEW_PRIVATE_PACKET"
```

It exercises actual guest/menu input, normal/flipped source children, two
complete loops, hover, zero-time redraw and source handoff. Its emitted packet
and complete Present arguments are separately fed to the real browser renderer
consumer. Cleanly compiled source controls remove the actual Host producer,
choose the wrong flipped programme, or omit the original initial animate.
These are new scoped proofs, not a relabeling of the105050/8123 epoch, whole
A1/V1, native global scheduling or pixel equivalence.

### November 11 finite opcode/branch campaign

The separate `SourcePlayer.AnimationOpcodeChecks` executable covers the nineteen
classes left verification-only in the November 10 list. Its **43 finite cases /
3964 exact original-C predicates** retain the earlier twelve-case and public
selector evidence without rerunning or relabeling it.

The classes are ATTACK_ON/OFF, SETFLAG/CLRFLAG, CODE/CODE_BZ/CODE_BNZ, END,
LEAPATT/LEAPAT_TGTX, PREP_XITION, RUNTOFRAME, SETCAMBOT, SETFRICTION,
SETVELS/SETYVEL/SET_FACE_REL_VEL, SHAKER and TURNHACK. Cases exercise both integer
return branches, the C-rand and randrng threshold boundaries, independent flag
bits, frame below/equal/above target, signed headings, leap-distance caps and
actual carrier/throw-target camera-joint caches.

Four initial original-C mismatches exposed stale `MODE_ZERO` after known-int
callbacks. Complete-profile execution now records their typed return and branches
through that original zero bit. Legacy policy stays unchanged; no return value
is invented for original void/opaque callbacks. Three explicit old-policy
component checks and an invalid void-conditional negative protect those boundaries;
they do not relabel the AV306 input as a legacy product.

The public scheduled cases run real Capture/Advance/player wakes, Host CSI/shaker
consumption and the same-wake fourteen-actor matrix packet. At source tick2 the
live `plyr_have_ball` zero return must skip CSI1803. Removing the real zero-bit
writer changes the executed branch, admits an extra randrng(3), and changes the
actual camera from its preceding mode9 to mode4. A separately clean-compiled
entire-old-state control also fails; forced-fresh final Core/test DLLs equal the
baseline bytes. No canonical source is edited by these controls.

```sh
python3 -B tools/animation/tests/check_remaining_opcodes.py \
  --source "$AUTHENTICATED_ORIGINAL_SOURCE" --run "$UNCHANGED_AV306_DATA" \
  --dotnet "$EXISTING_DOTNET" --output "$NEW_IGNORED_NOV11_OUTPUT" \
  --controls "$IMMUTABLE_B9_CAPTURE_ZIP"
```

The runner captures actual compile inputs, uses the existing supported private
POSIX build wrapper, seals each variant separately, and exports/read-backs its
receipts, logs, native bodies, observations and assemblies. Original function
bodies are extracted unchanged. Clip storage is the already admitted decoded
float boundary, not shipped SEQ numeric IDs. CSI selection/copy retains authored
table scalars as opaque payload; this is not a DG2RD/native-angle comparison.
Inactive original bodies and unused opcode forms remain separately classified,
not replay substitutes. This closes these finite cases, not every reachable
programme/callback operand, whole A1, browser GPU pixels or target DSP identity.

### Repository-carried PF and camera/cache regressions

The existing animation check executable also carries the qualified handwritten
left-throw and nonidentity camera fixtures. No separate private C# project or
old snapshot is needed. Its default43 cases and optional raw6 path remain
separate; historical MODE_ZERO/PF/camera archives retain their original hashes.

```sh
# Six full-script release cases, with the current cached-joint release boundary.
python3 -B tools/animation/tests/check_remaining_opcodes.py \
  --source "$AUTHENTICATED_ORIGINAL_SOURCE" --run "$UNCHANGED_AV306_DATA" \
  --suite left-releases --source-controls --output "$NEW_LEFT_OUTPUT"

# Home/Away nonidentity camera -> previous joint cache -> current-camera release.
python3 -B tools/animation/tests/check_remaining_opcodes.py \
  --source "$AUTHENTICATED_ORIGINAL_SOURCE" --run "$UNCHANGED_AV306_DATA" \
  --suite camera-cache --source-controls --output "$NEW_CAMERA_OUTPUT"

# The separate ordinary raw snap/move/throw selector cases.
python3 -B tools/animation/tests/check_remaining_opcodes.py \
  --source "$AUTHENTICATED_ORIGINAL_SOURCE" --run "$UNCHANGED_AV306_DATA" \
  --suite raw-throw-selectors --output "$NEW_RAW_OUTPUT"
```

`--dotnet "$EXISTING_DOTNET"` selects an already installed SDK. Omit `--suite`
for the default opcode cases. Direct DLL selectors are `--left-releases`,
`--camera-cache` and `--raw-throw-selectors`, after DATA and a new observation
file. The existing project is unchanged; the additional isolated classes use
`Run`, not competing entry points. All generated native C/binaries, authentic
inputs and receipts stay in ignored/private output. No SDK installation, new
production profile, archive overwrite or data alias is performed.

The six full-script cases retain the exact original SET/release/CLEAR timing,
left/right controls, pose bits, five ball-flight frames and actual PCM transport.
Their native `set_ball_vels` now consumes the **actual preceding camera-space
joint cache and current camera**. Fresh world wrists are diagnostic fields only.
This is a new current-code boundary, not a promotion of the earlier identity-
camera/world-wrist PF proof. These fixtures still do not prove physical selector
reachability; the independent raw6 selector supplies its declared input scope.

The camera selector retains744 exact original camera-transform comparisons,
2070 native skeletal-cache coordinates and20 release values, plus969 managed
guards including fourteen strict invalid-cache cases and four LegacyNormalized/
other-library components. A small explicit test-local reference preserves the
unchanged old world/fallback arithmetic; no old production-class snapshot is
embedded or required. It does not claim construction of a whole legacy product.

Portable source controls reconstruct the **entire** original6b83 programme or
b2 geometry bytes and verify their hashes. The independent release-caller
control changes only the real cached-wrist expression back to fresh sampling.
Every control is cleanly compiled, its decisive comparison must fail, and a
forced-fresh final must pass with the same baseline Core/test DLL hashes.
Right-case invariance, callback ticks, pose bits and RNG counts remain checked.
No prior Linux proof archive is required to reconstruct those controls.

The runner seals actual compile inputs, reuses the supported POSIX wrapper, and
requires fresh-host member readback before acknowledging namespace exit.
These are finite source/consumer regressions, not another natural-match run,
full camera-tracking/DG2RD/target ABI/FPU, DSP, GPU pixel or whole-A1 claim.

## Explicit pre-snap-switch successor: one authentic clip only

`presnap_switch_sequences.json` adds only `M_HOPCLP`: original
`anim/misc2.wan` blob `5b3083a71b1a7729d508b804712f5a754afaca5b`,
chunk6, bytes `[92472,118456)`, authoring name `m_hopclp`, all35 selected
frames and fifteen joints. Selected ordinals map exactly to authoring0..34.
The unchanged selected exporter uses the original converter's `0.12898` root
scale and validates the actual matrices, quaternions and complete frame mapping.
It does not invent a decoder, pose, numeric SEQ ID or native quantization.

```sh
python3 -B tools/animation/prepare_player_sequences.py \
  --source-root "$AUTHORIZED_WAN_ROOT" \
  --catalog tools/animation/presnap_switch_sequences.json \
  --destination .squad/.scratch/presnap-switch/hop
python3 -B tools/animation/prepare_presnap_switch_profile.py profile \
  --source-root "$AUTHENTICATED_C_SOURCE_ROOT" \
  --base-m1-inputs "$EXACT_OLD_288_FILE_M1_INPUTS" \
  --hop-bundle .squad/.scratch/presnap-switch/hop \
  --movement-programs Assets/Scripts/Gameplay/SourceAnimationPrograms.Movement.cs \
  --destination .squad/.scratch/presnap-switch/profile
python3 -B tools/animation/prepare_presnap_switch_profile.py impact \
  --base-catalog "$EXACT_OLD_M1_482_SITE_IMPACT" \
  --callback-data .squad/.scratch/presnap-switch/profile/c2-callback-data.json \
  --animation-manifest .squad/.scratch/presnap-switch/profile/animation282/export-inventory.json \
  --movement-programs Assets/Scripts/Gameplay/SourceAnimationPrograms.Movement.cs \
  --destination .squad/.scratch/presnap-switch/impact
```

All destinations must be new, ignored, regular local roots, disjoint from the
explicit inputs. Neither subcommand downloads anything or overwrites an old
generation. The profile producer copies all281 old clip bytes, including the
qualified R_SKIP and RUSH_C75, without re-exporting them. Its exact289-file
closure is282 clips, one inventory plus detached pin, callback JSON plus pin,
and the unchanged hit-extension SAFN/pin/source JSON. It authenticates all six
named original C/header blobs and the original positive `attack_snd` branch.

| Explicit successor artifact | SHA-256 |
| --- | --- |
| One-clip selected inventory | `ec3a334976e0dc0b13accc9b2b9cff098e61c800bd2eba1bf02f831c03f84cef` |
| Actual decoded `M_HOPCLP35` | `df5e69344d049646e07143c4cbfec0d577e442fe63152099967436fa91c4e2bb` |
|282-clip /26,265-frame inventory | `50aa432ff9b332ec9087133f5bccb43ae18e1ca7c0bf7830cbdd675d3e024046` |
|15-binding callback JSON | `e9a2ccbfffda0e83ae9ba409e4fbfd6f6c6f074ebd024aa30367d9d06c29c653` |
| Rebound482-site impact | `456d525aa75ba343942613793cc437d6732f0b16d972a981537929daece9f3d7` |
| Separate frozen pre-snap Movement programs | `2f2d888ff89a6a89d9b72d19590115a71cce8ff5c7c7d34b8c313205cf0358ef` |
| Unchanged C2 programs | `0935d54982b80f40f143033c3543837601148968175c0f33210e051f8e16a5cc` |

The resource profile is **`original-teams-contact282-c2-m1-presnap-switch`**,
tool profile `contact282-c2-m1-presnap-switch`, callback profile
`OriginalRelease57Hz-C2-M1-PreSnapSwitch-InitSoundBanks-v1`.
Animation uses
`SourceData/local-plyr266-contact282-c2-m1-presnap-switch-50aa432f`;
callbacks use `SourceData/source-c2-m1-presnap-switch-callbacks-e9a2ccbf`.
Team/impact keys use the exact stable ID under `BlitzSourcePlayers/`;
services use
`BlitzSourceInterface/original-contact282-c2-m1-presnap-switch-match-services`.
Old SAFN/hit resource keys are reused unchanged.

The first fourteen bindings, nine call templates, banks and tables are unchanged;
the sole appended tuple is `m_hopclp_anim:3409 / attack_snd(127,0,0)`.
The482-site catalog **already contains3409**: only generation metadata, schema
and qualifications change, not cue/table/camera data or the41 additional speech
lines. The successor requires both distinct program hashes. Changed/stale
programs invalidate this release; never patch a trusted constant around them.

`Contact282C2M1PreSnapSwitch`, `ReadPreSnapSwitchCallbacks`,
`WithContact282C2M1PreSnapSwitch` and
`InitializeContact282C2M1PreSnapSwitch` are explicit selectors. Old
`IsContact281C2M1`, readers, factories, data, defaults and recordings remain
distinct. `UsesM1Services` only preserves existing normal-camera, possession,
Portable/fire/kick service ancestry; clip presence never enables gameplay.
Runtime/Editor source routes to the exact **U2-owned**
`SourceC2Composition.CreatePreSnapSwitchSimulation`. Until U2 implements that
factory, full runtime/API integration is pending, not a successful old-factory
fallback. No Editor import/build, scene or renderer change is part of S1.
The unchanged `SourceCinematicPlayback.ConsumeResolvedSourceSound` is another
explicit integration dependency: its historical taunt/hit discriminator and
per-call C2-context guard reject kind5. That file is outside S1's writer list.
Its minimum narrow ownership expansion must be authorized before changing those
guards; neither dropping the typed context nor bypassing the cinematic FIFO is
an acceptable workaround. A passing selector/transport boundary is not a
passing complete presentation path.

Metadata-only tests need no original data. The opt-in real-output checks
reproduce the generation, compare every inherited clip and impact section,
and reject actual missing/changed clips, wrong maps/source hashes, stale
programs/payloads, mismatched profiles/counts/tuples, extra files and symlinks:

```sh
python3 -B -m unittest discover -s tools/animation/tests \
  -p test_prepare_presnap_switch_profile.py
# Explicit S1 release root contains baseline.json plus hop/profile/impact:
python3 -B tools/animation/tests/test_prepare_presnap_switch_profile.py \
  --systems-output "$PRIVATE_S1_SYSTEMS_OUTPUT" -v
```

All outputs remain private. Existing558-file player data and old receipts are
not edited; the separately owned U2 producer adds five files to a new563-file
bundle. Real source-host/default configuration integration, full new-profile
qualification and genuine World evidence remain U2/later Quality work.
Selected-export and API evidence is not PCM/DSP/native Unity, performance,
rights/redistribution or whole-game acceptance; all sixteen whole/native rows
remain open.

## Explicit C2 production281 generation

### Explicit M1 running-taunt successor (not the old-nine default)

`--profile contact281-c2-m1` selects a **separate** trusted generation. Without
that option both producers still reproduce the frozen old-nine C2 bytes below.
The successor keeps every281 clip, all26,230 frames, nine C2 extension programs,
old-six SAFN and ONE7538 unchanged. It adds only these source-verified VM bindings
to the existing nine, not new clips/programs or a fictitious cyclic `ANI_END`:

| Script | `plyrseq.c` operand line | `taunt_speech` arguments |
| --- | --- | --- |
| `r_histep_anim` |6373| `-4,0,0` |
| `r_skip_anim` |6401| `-4,0,0` |
| `r_hihand_anim` |6428| `-4,0,0` |
| `r_bktau2_anim` |6480| `-4,0,0` |
| `r_ezsprint_anim` |6508| `-5,0,0` |

Original `sounds.c:507–528` already drives the generic selector. `-4` consumes
`randrng(100)`, returns empty at10 or above, otherwise consumes `randrng(3)` for
the original histep table. `-5` consumes only `randrng(100)` and emits fixed11
below10. The actual initialized taunt bank is required when emitting; percent0
is unused, not mute. No new selector, DSP state or target-PRNG claim is involved.

```sh
python3 -B tools/animation/prepare_c2_profile.py \
  --source-root "$AUTHORIZED_NFL_SOURCE_ROOT" \
  --base-bundle "$AUTHORIZED_272_BUNDLE" --extra-bundle "$AUTHORIZED_EXTRA9" \
  --profile contact281-c2-m1 --destination .squad/.scratch/c2-m1/inputs
python3 -B tools/animation/prepare_c2_impact.py \
  --base-catalog "$AUTHORIZED_C1_469_CATALOG" \
  --callback-data .squad/.scratch/c2-m1/inputs/c2-callback-data.json \
  --plyrseq "$AUTHORIZED_NFL_SOURCE_ROOT/plyrseq.c" \
  --programs Assets/Scripts/Gameplay/SourceAnimationPrograms.C2.cs \
  --profile contact281-c2-m1 --destination .squad/.scratch/c2-m1/impact
```

| Successor identity | Value |
| --- | --- |
| Resource profile | `original-teams-contact281-c2-m1` |
| Payload profile | `OriginalRelease57Hz-C2-M1-InitSoundBanks-v1` |
| Callback SHA-256 | `b0c02e5516f4f932625e1f8ccb3ff0fd53ae222657f899eb8c9d0b0c2e74460a` |
| Callback resource key | `SourceData/source-c2-m1-callbacks-b0c02e55` |
| Computed477+5 impact sites | **482**, `2fee43a7a20dad06d8846b1d558a69d8648041368091ab12677c941e19b66cd1` |
| Actual factory | `SourceC2Composition.CreateM1Simulation` |

Old `CreateSimulation` / `ReadC2Callbacks` remain old-nine only; the successor
uses `CreateM1Simulation` / `ReadC2M1Callbacks`. Mixed generations fail visibly.
Runtime/resource/import/build URL wiring is a separate Systems integration gate,
not implicit promotion from C1 or old C2. Source calls/raw RNG/FIFO qualification
is not DSP playback or full-game/native acceptance.

For the existing proper original-C producer, `tests/contact/prepare_c2.py` accepts
the same explicit `--profile contact281-c2-m1`; its default is unchanged.

`prepare_c2_profile.py` reproduces the **exact Core-qualified canonical bytes**,
using existing `read_wan_clip.encode_clip`, `prepare_functions.produce/encode`
and `prepare_functions.canonical`. It takes explicit original/base/extra roots;
it does not depend on an ignored implementation-contract file, invent another
union format, embed original tables in source, retrieve anything or decode new
motion. Original tables, bank pools and nine callback tuples are extracted from
their pinned source. All272 and nine new clip bytes are copied unchanged.

The extra-nine acquisition edge uses the tracked **metadata-only**
`c2_sequences.json`, SHA-256
`29073877f1e6586fb961b18b65d7d1173e7d77e2a8567e8f64bc2f3fbbc42210`.
It contains only names, source paths/blob pins, frame counts/bounds and converter
provenance, not WAN, frame, pose or audio payloads. The unchanged generic exporter
decodes the nine clips from `anim/misc.wan`, `anim/misc3.wan` and `anim/misc4.wan`;
all three are already included in `tests/parity/fetch_inputs.py --profile contact272`.
No ignored catalog, additional WAN download or new decoder is required.

```sh
python3 -B tools/animation/prepare_player_sequences.py \
  --source-root .squad/.scratch/SourceData/contact-kernel-original \
  --catalog tools/animation/c2_sequences.json \
  --destination .squad/.scratch/c2-presentation/extra9

python3 -B tools/animation/prepare_c2_profile.py \
  --source-root .squad/.scratch/SourceData/contact-kernel-original \
  --base-bundle .squad/.scratch/SourceData/contact-kernel272/contact-animation \
  --extra-bundle .squad/.scratch/c2-presentation/extra9 \
  --destination .squad/.scratch/c2-presentation/inputs
# Add --reuse only to freshly compare all288 existing output bytes.

python3 -B tools/animation/prepare_c2_impact.py \
  --base-catalog .squad/.scratch/contact-profile/impact272/impact-presentation.json \
  --callback-data .squad/.scratch/c2-presentation/inputs/c2-callback-data.json \
  --plyrseq .squad/.scratch/SourceData/contact-kernel-original/plyrseq.c \
  --programs Assets/Scripts/Gameplay/SourceAnimationPrograms.C2.cs \
  --destination .squad/.scratch/c2-presentation/impact
```

| Explicit C2 artifact | SHA-256 |
| --- | --- |
| Nine-clip / 1,338-selected-frame extra manifest | `3e69a29f11c15c5d318f486824b1a3bba1f9489905e20d4c9ce76742af7a8c5f` |
|281/26,230-frame animation manifest | `e87401c8cf3e348cc2d9c7255a1f18f43e41b1694ff7dff45c4bf025a5ecce4f` |
| `c2-callback-data.json` | `9d2cb7ac5d92c3c2178fc1970a68ccad778fb7d319513b1787fe3ba3fe905f99` |
| ONE7538 hit-extension SAFNv1 | `bb978454ab57b09357e422f048e4035bea01a4d5932f453dd89be39838c6e904` |
|477-site bound impact catalog | `9e82809268e18ae5d792f6e1614f0ea949a04b579bf4a93ea81b7833be4ce8ac` |
| Separate unchanged old-six SAFNv1 | `4a557304d3b041ee04dff8ed1354714fc5665abaeb1c3d458d94171adf1a0280` |

This is the verified272+9 distinct union, not an assumed277 subset. Nine real
clips add1338 selected fifteen-bone frames; transitive BREATH98/RUSH_C75 already
exist. No new decoder, frame selection, compression/quantization or shipped
numeric SEQ identity is asserted. All original data/generated JSON/SAFN/motion
remains ignored/local; no redistribution rights follow.

The new impact generation retains all469 prior sites and camera/shaker data,
adds exactly eight original speech sites, and preserves hit7538 as a new typed
`HitFunctionExtension`, not ground audio. It binds the same281 manifest,
canonical C2 JSON and frozen C2 program hash. No native whistle is converted
into an animation cue, actor0 or a fabricated camera site.

Use `tests/presentation/c2-runtime/README.md` for fresh package/runtime proofs.
Historical producers/catalogs below remain unchanged and distinct.

### Optional original-C C2 oracle inputs

The public C2 runtime fixture above needs no Midway process source. The separate
original-C oracle requires two explicit local files from `historicalsource/midwayvideo`
revision `1792293aaaea00a919b59e8c05716978e85b4333`. The NFL input fetcher does not
acquire them. If network retrieval is separately authorized, the existing pinned
Git-blob recipe is:

```sh
PROCESS_ROOT=.squad/.scratch/SourceData/c2-process-original
test ! -e "$PROCESS_ROOT" || exit 1
mkdir -p "$PROCESS_ROOT/lib/goose"
gh api repos/historicalsource/midwayvideo/git/blobs/967ca53910f24ddfa48a14485113a8fd0c989959 \
  --jq .content | base64 --decode > "$PROCESS_ROOT/lib/goose/process.c"
gh api repos/historicalsource/midwayvideo/git/blobs/7b44637c3d59e2256ad099226b7ea17f108907ef \
  --jq .content | base64 --decode > "$PROCESS_ROOT/lib/goose/mthread.c"

python3 -B tests/contact/prepare_c2.py \
  --source-root "$AUTHORIZED_NFL_SOURCE_ROOT" --process-root "$PROCESS_ROOT" \
  --base272 "$AUTHORIZED_272_BUNDLE" \
  --extra9 .squad/.scratch/c2-presentation/extra9 \
  --output .squad/.scratch/c2-original-oracle-preparation
```

Use a new ignored process root, never overwrite an existing input.
`tests/contact/c2_oracle.py` verifies both Git blob hashes before extracting any
body; there is no fallback to invented scheduler behavior. Production producers
and oracle preparation remain offline by default; these explicit `gh` commands
are the only retrieval step shown. Source bodies and generated oracle/data
outputs must stay ignored/local. Host-C evidence does not establish target
scheduler, ABI, native game or DSP equivalence.

## C1 production successor: explicit Contact272

The C1 engine requires a **new**, trusted production resource, not the old157
library with another label. `contact_sequences.json` is the metadata-only
registry for the exact disjoint115 requirements: source WAN/blob/SHA, original
chunk name/number/byte offset, complete authoring-to-selected index maps and
decoded-byte pins. `prepare_contact_profile.py` reuses the existing `full_clip`
reader, freshly reproduces all115 clip bytes and their frozen manifest
`907e2d368394e5c9db1bebdf218abae62bb3b841230293670cb3d2db48e05fcd`,
then copies the immutable true157 bytes unchanged. It does not add a decoder,
change flags/selection, infer poses, or rerun historical executables.

| Production identity | Value |
| --- | --- |
| Stable team/template profile | `original-teams-contact272` |
| Manifest SHA-256 | `9cae962bbb861bffe4d379bc30919cf34c0238ed35be82e605253d3059cfede9` |
| Animation Resources key | `SourceData/local-plyr266-contact272-9cae962b` |
| Clips / selected frames / joints | **272 / 24,892 / 15** |
| Unchanged base | true157 /13,490 frames / `f0ae9395e57ae8327b7b55493dd6c457102adb4a13ecbb581e3551d400a3800e` |
| Disjoint extension | selected115 /11,402 frames /16 source WANs |

Supply explicitly authorized local roots containing the case-sensitive WAN
paths. Repeat `--source-root` when the already-present originals are split
between roots; a conflicting original in **any** supplied root fails, rather
than being skipped. No input, Primary path, retrieval or private cache is
implicitly selected:

```sh
python3 -B tools/animation/prepare_contact_profile.py prepare \
  --base-bundle "$IMMUTABLE_BUNDLE157" \
  --source-root "$AUTHORIZED_WAN_ROOT" \
  --source-root "$AUTHORIZED_ADDITIONAL_WAN_ROOT" \
  --destination .squad/.scratch/contact-profile/bundle272

# Read-only verification: fresh decoding plus every emitted-byte comparison.
python3 -B tools/animation/prepare_contact_profile.py prepare \
  --base-bundle "$IMMUTABLE_BUNDLE157" \
  --source-root "$AUTHORIZED_WAN_ROOT" \
  --source-root "$AUTHORIZED_ADDITIONAL_WAN_ROOT" \
  --destination .squad/.scratch/contact-profile/bundle272 --reuse
```

The optional metadata-registration command takes **both** explicitly supplied
frozen C1 `--dependencies` and `--receipt` paths, verifies SHA-256
`34e6acabf68e757d6e1914fb7daeb520c511d63370dab544ca95a1d61ae606f5`
and `196ca58873341a8f0bd7f3e051b8d4a5e150bdcec1eebacd7a3067a88338041d`,
and reproduces the adjacent `contact_sequences.json`. Normal production
preparation needs only the published registry and explicit original inputs,
not that ignored handoff or its old machine paths.

The successor manifest records those pins, both parent manifest identities and
actual producer/reader hashes. The supplemental clips deliberately retain
their historical test-input provenance string **byte for byte**; the new
manifest records their explicit production adoption, not fictitious different
source frames. R_SKIP's qualified retained-limb replay and all56/117/156/157
catalogs, clip bytes and keys remain unchanged. Generated SEQ integer IDs and
shipped compression/quantization are unavailable and **not asserted**.
Changing the producer requires a new resource identity, never reusing these
pinned bytes under another old profile.

### C1 impact/camera binding is also a new explicit generation

The old complete impact catalog remains immutable. Its416 animation sites,
seven original weighted camera tables, five `detect.c` selectors and original
shaker values are retained. C1 emits25 camera contexts at the actual `.word`
CSI operand line; the new catalog explicitly records each corresponding
canonical `.byte ANI_SETCAMBOT` line as well. Twenty-eight additional original
`taunt_speech`/`tackle_spch` sites are recorded as **unimplemented callbacks**,
not speech/DSP/effects behavior. All360 C1 presentation instructions are
covered, for469 total catalog sites.

```sh
python3 -B tools/animation/prepare_contact_impact.py \
  --base-catalog "$IMMUTABLE_COMPLETE_IMPACT_CATALOG" \
  --plyrseq "$AUTHORIZED_CODE_ROOT/plyrseq.c" \
  --contact-programs Assets/Scripts/Gameplay/SourceAnimationPrograms.Contact.cs \
  --animation-manifest .squad/.scratch/contact-profile/bundle272/export-inventory.json \
  --destination .squad/.scratch/contact-profile/impact272
```

The output schema is `original-impact-contact272/v1`, SHA-256
`738d712b6f80d8a9456338548e7bbce1fdf88baa46f342a7c45efffd02a8f23a`.
It binds the exact272 manifest and C1 program source hash
`944da0dc45732eec699f96882694a65c3adc1266b1feaa6d1fe7e5296852ffde`.
The six supported synchronous SoundVM source tuples are unchanged. Unresolved
contact calls, unsupported speech/SPFX and nonempty output without an actual
DSP consumer stay explicit failures/raw metadata, never simulated audio.

For actual provider/services/import binding and SDK/Unity-reference-only
checks, see `tools/interface/packaging.md` and
`tests/presentation/contact-runtime/README.md`. Native Editor/import/browser/
device work is not part of this recipe or authorized by these commands.

## Reproduce the frozen 56-sequence library offline

`player_sequences.json` is the publishable metadata-only catalog for the verified
`edc6193` continuous-animation checkpoint: **56 sequences, 3,731 selected frames,
4,046 authoring frames across 11 WANs**. It preserves original case-sensitive
source paths, Git blob pins, runtime names, counts and selected boundaries.
It contains no animation values or decoded-file hashes tied to an old local path.
The source tree is `historicalsource/nflblitz2000` at
`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`; converter provenance remains the
`1792293aaaea00a919b59e8c05716978e85b4333` pin described below.

From the project root, set `SOURCE_ROOT` to your existing local pinned source tree
(the directory containing `anim/`). No ignored inventory, analysis helper,
network access, Git invocation, package installation or historical executable is
needed:

```sh
SOURCE_ROOT=/absolute/path/to/your/local/nflblitz2000
BUNDLE=.squad/.scratch/player-animation-edc6193

python3 tools/animation/prepare_player_sequences.py \
  --source-root "$SOURCE_ROOT" \
  --destination "$BUNDLE"

PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tools/animation/tests -q
```

The destination must be **new** and beneath ignored `.squad/.scratch/`. The batch
reads each required WAN once, caches its bytes, checks the Git blob identity and
decodes with the unchanged `full_clip` / `encode_clip` implementation. Actual
decoded names, counts and boundaries must match the catalog before any bundle
is created. Every source frame is validated by the existing reader, including
deselected frames. There is no resampling, new selection rule, inferred rate or
playback-policy change.

The bundle contains `clips/<runtime_name>.json` and `export-inventory.json`
(`original-wan-export-inventory/v1`). Manifest clip paths are relative to the
**bundle root**, with actual decoded-byte SHA-256, source blob/SHA-256, authoring
count and full selected-source-index map. Clip provenance retains both source
pins and uses the repository-relative WAN path for `local_input`; identical
inputs at different physical roots produce identical bytes and hashes. The old
single-clip CLI and its path-bearing output remain unchanged. Commit/path
linkage is caller-supplied; offline blob verification is not a check of checkout
HEAD.

An existing destination is never overwritten or repaired. Add `--reuse` only for
read-only verification of an **entire identical bundle**: the batch freshly
decodes the pinned source and compares every output byte, rejects missing,
changed or extra entries, and rejects symlinks and path escapes.

## Reproduce exactly the expanded B1/B2 117-sequence library

`ball_sequences.json` is an **explicit opt-in**, metadata-only superset of the
unchanged 56-sequence default: **117 sequences, 10,288 selected frames, 13,514
authoring frames across 17 WANs**. It is the exact sorted metadata projection of
Core's stable expanded inventory, SHA-256
`18af668aa0ff97985f91030f6c17d0552a9630fe25d40893dc5a7ac8381b7cdf`.
That inventory pin identifies the requirements, not decoded output bytes or a
final gameplay-code freeze. No ignored inventory is needed to run the recipe.

Use the **existing** `--catalog` option with the same explicit pinned local
source root. Omitting it still prepares only `player_sequences.json` (56 clips).
No decoder, selection, timing, runtime, importer or manifest-schema change is
needed:

```sh
SOURCE_ROOT=/absolute/path/to/your/local/nflblitz2000
BUNDLE=.squad/.scratch/player-animation-ball-recipe/bundle

python3 -B tools/animation/prepare_player_sequences.py \
  --source-root "$SOURCE_ROOT" \
  --catalog tools/animation/ball_sequences.json \
  --destination "$BUNDLE"

# Read-only verification of an already complete, identical bundle:
python3 -B tools/animation/prepare_player_sequences.py \
  --source-root "$SOURCE_ROOT" \
  --catalog tools/animation/ball_sequences.json \
  --destination "$BUNDLE" --reuse
```

Names, case-sensitive source paths, source pins and selected boundaries are
preserved exactly, including runtime aliases and sparsely selected sequences.
Full authoring-to-selected maps and motion come only from the unchanged reader,
not from new public fixtures. Relocating identical source WANs does not change
any emitted byte. Selecting another catalog, even with the same clips but
different metadata, cannot reuse this bundle's manifest.

Only canonical `provenance.local_input` and the fixed explanatory
`provenance.source_linkage` may differ from older machine-path-bearing Core
exports; all other decoded data must agree. Use the **newly printed**
`manifest_sha256`, never the Core inventory pin or old decoded hashes, for
native import. This bounded preparation is not all-source-animation or
whole-gameplay completion.

## Explicit movement-complete extension: 156 unchanged clips plus R_SKIP

`movement_complete_sequences.json` is a **new composite catalog**, not a change
to any selected catalog. It pins `movement_sequences.json` and its immutable
156-clip manifest, then adds one explicitly qualified sequence: **157 clips,
13,490 selected frames, 18,174 authoring frames**. All 156 base inventory entries
and decoded clip bytes are copied unchanged after fresh, read-only verification
by the existing selected producer. The old 56/117/156 manifests, resources and
catalogs are never overwritten, repaired or aliased.

### The actual R_SKIP issue is an omitted record, not selection

The pinned original `wan2seq.c` defines `A_SELECTED=0x4000` and
`A_COMPRESSED=0x1`. The original `anim/misc99.wan` blob
`256acb7ddb838627312193ae19db66abd18ac146` contains `r_skip`, chunk 13, with raw
flags **0x4000: selected, uncompressed**. All 70 authoring frames are selected.
However, the chunk contains only fourteen matrix records: **record 5, the
right-hand rotation, is absent**. The unchanged ordinary readers correctly
reject this incomplete record set. Changing selection bits cannot fix it.

There is a deterministic **source-converter** result, distinct from a claim of
complete authored R_SKIP motion. The original converter's global
`frames[frame][limb]` buffer is not cleared between sequences
(`wan2seq.c:72,314–555`). Its immediately preceding selected chunk is
`Huffrun2`, chunk 12: a complete, uncompressed 90-frame sequence. Thus the
original converter retains its already-written limb-5 quaternions at authoring
indices 0–69 when exporting R_SKIP. This is a named, byte-addressed last-writer
dependency, **not a guessed hand pose, a whole-clip alias, identity fill or a
claim that R_SKIP contains those matrices**. Earlier compressed chunks cannot
influence these frames because the complete uncompressed writer replaces them.
Compressed retained writers/targets are deliberately not qualified; the
converter can mutate quaternion signs during compressed output.

`read_wan_replay.py` implements only this explicit policy. It validates the
actual omitted record, the last preceding selected writer, its exact name,
flags, chunk/record bounds and every required same-index frame. It validates
the complete writer using the existing reader, including frames after the last
retained frame. There is no default fallback or generic unselected export.
`prepare_movement_complete.py` additionally verifies pinned original
`plyrseq.c`, `player.c`, `anim/MAKEFILE` and `anim/seattle.mak` bytes and active
source-reference ranges before preparing anything.

The new clip uses `original-wan-converter-replay-clip/v1`. Its
`record_layout` truthfully contains **16 actual records** (flags, root and
fourteen limbs), while `converter_replay` separately identifies the retained
record outside the target chunk. All original names, flags, maps, roots and
matrix-derived rotations are retained. The inventory must carry the same
explicit qualification. The production `SourceAnimationLibrary.Read` supports
this narrowly pinned format and exposes immutable `SourceAnimationClip.ConverterReplay`
provenance; public standalone `ReadClip` still refuses an unqualified replay.
Ordinary v1 libraries remain compatible, with selected/unsupported flags checked.
No `Contracts`, sampling, actor pose, gameplay or native importer API is changed.

This is normalized binary32-WAN matrix conversion plus qualified converter-state
replay, **not verification against a shipped SEQ, its compression/quantization
or the original executable**. The catalog makes no such claim.

### Offline preparation and timing

Supply a local pinned tree containing both the case-sensitive WAN paths and the
four code/build reference files above. The selected bundle must already be the
complete immutable movement156 bundle with manifest SHA-256
`5e8fc7c5f2011303fd7b1aea87e6f957b1acdf4cbb15a43fd82f978d4b944b68`.
No source retrieval, historical makefiles/scripts or Unity process is run:

```sh
SOURCE_ROOT=/absolute/path/to/local/pinned/nflblitz2000
SELECTED_BUNDLE=.squad/.scratch/source-movement-integration/bundle156
NEW_BUNDLE=.squad/.scratch/source-rskip-export/bundle157

python3 -B tools/animation/prepare_movement_complete.py \
  --source-root "$SOURCE_ROOT" --selected-bundle "$SELECTED_BUNDLE" \
  --catalog tools/animation/movement_complete_sequences.json \
  --destination "$NEW_BUNDLE"
```

The command prints the **new actual manifest hash and resource identity**
`SourceData/local-plyr266-movement157-<manifest-first-eight>`. Do not use the old
156 identity or silently replace an existing Unity library. Native asset
creation, GUID ownership and runtime resource binding are separate handoffs.
An existing destination requires `--reuse`, which freshly verifies every output
byte without changing it. Producer source hashes participate in the new manifest;
changing a producer requires a new bundle identity, not reuse of old output.
The original selected producers/readers are unchanged.

`source_playback_evidence` records the active `r_skip_anim -> r_skip_cyc`
program, not an invented universal playback policy. At the normal 57 Hz
profile, its steady cycle advances one exported frame per tick (57 FPS, 70
frames). Entry from `R_TRB_WB`/`RUN_WB` clears the mode before the four-tick
proportional transition; the other branch enters the cycle with one-shot
`MODE_ANCHORXZ`. Runtime code applies and clears that anchor. The exporter does
not zero root axes, resample, add a closing pose or replace the existing script.
The clip's general playback fields remain explicitly runtime-bound.

### Actual-data and independent checks

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tools/animation/tests -q

python3 -B tools/animation/tests/run_movement_complete_checks.py \
  --bundle "$NEW_BUNDLE" --selected-bundle "$SELECTED_BUNDLE" \
  --source-root "$SOURCE_ROOT" \
  --converter-source .squad/.scratch/reference/midwayvideo/tools/wan2seq/wan2seq.c \
  --model .squad/.scratch/reference/nflblitz2000/models/plyr266.h \
  --skeleton .squad/.scratch/reference/nflblitz2000/models/skeleton.h \
  --baseline56 .squad/.scratch/player-animation-edc6193-recipe \
  --baseline117 .squad/.scratch/player-animation-ball-recipe/bundle \
  --output .squad/.scratch/source-rskip-export/verification
```

The check output directory must be new. Python synthetic tests require no
original motion. The actual-data check requires the local inputs above, GCC and
the existing .NET SDK: actual production sources compile as **C# 7.3 /
netstandard2.1**, with a separate .NET 10 check executable and no Unity types.
It reads all three old libraries and the new library using the production
loader; compares every old frame, inventory entry and clip byte; and exercises
malformed flags/payloads and explicit qualification guards on actual emitted JSON.

An independent bounded C WAN parser replays the original pinned C
`mat2quat`/`adjust_quat` functions, extracted only into ignored local outputs,
and records every frame's actual last writer and raw matrix address. All 1,050
R_SKIP quaternions and all roots are checked against that path. Separately, an
independent C# raw-matrix hierarchy evaluates every actual model vertex at all
70 frames, three headings/scales, against the production
`SourceActorPoseSampler`/`SourceAnimationMatrices` path. That hierarchy does not
decode quaternions or reuse the Python exporter. Synthetic self-roundtrips are
not described as independent original-data evidence.

Four isolated production-source mutations (qualification agreement, qualified
record identity, raw flags and pose basis), plus two well-formed, repinned actual
artifact mutations (retained pose and root), must fail their named assertions,
not compilation. Isolated source/artifact copies are removed; original inputs
and production sources are checked unchanged. These checks are not an Editor
import, browser run, public gameplay replay or whole-M1 acceptance. All decoded
motion, raw source and oracle outputs remain ignored and local-only; no asset
redistribution rights are implied.

## Existing Unity importer arguments

The printed `manifest_sha256` is the actual bundle manifest pin. With the
existing local `local-plyr266` rig imported, pass the bundle root and portable
manifest to the existing native Editor importer:

```sh
MANIFEST_SHA256="$(python3 -c 'import hashlib, pathlib, sys; print(hashlib.sha256(pathlib.Path(sys.argv[1]).read_bytes()).hexdigest())' "$BUNDLE/export-inventory.json")"
UNITY_EDITOR=/absolute/path/to/Unity/Editor/Unity

"$UNITY_EDITOR" -batchmode -nographics -quit -projectPath "$PWD" \
  -executeMethod Blitz.Editor.SourceAnimationImporter.ImportFromCommandLine \
  -animationAssetRoot "$PWD/$BUNDLE" \
  -animationManifest export-inventory.json \
  -animationManifestSha256 "$MANIFEST_SHA256" \
  -animationStableId local-plyr266
```

The importer is immutable: an existing animation library with a different
manifest pin is refused, not replaced. Run this against the intended clean
import target. Preparing the bundle alone does not launch Unity or alter any
project asset, runtime source, timing, matrix convention or source selection.

## Complete selected-frame clip

`read_wan_clip.py` exports **every selected frame of one sequence**, in the
original converter's selected export order, as compact deterministic JSON. It
does not download files, execute historical tools, synthesize keyframes, pick a
gameplay action, or guess a playback rate.

```sh
python3 tools/animation/read_wan_clip.py \
  .squad/.scratch/reference/nflblitz2000/anim/Qb.wan \
  --sequence QBSTANCE \
  --source-repository historicalsource/nflblitz2000 \
  --source-commit 89f840d58d152a2a9f230cb8d0cdffbd554c8ded \
  --source-path anim/Qb.wan \
  --expected-blob a5bed4bc6699a88281e888660e31e349d4009d3f \
  --output .squad/.scratch/original-clips/QBSTANCE.json

PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tools/animation/tests -q
```

The API is `full_clip(data, sequence, provenance)` followed by `encode_clip(clip)`.
Required provenance fields are `repository` (`owner/name`), `commit` (lowercase
40-digit SHA-1), `path` (relative repository path), and `git_blob_sha1` (lowercase
40-digit **blob**, not file, SHA-1). Commit/path linkage remains the caller's
responsibility: verify that metadata against the pinned source tree; the reader
verifies content against the blob pin offline. Optional caller metadata is
retained. CLI output must be explicitly specified beneath ignored scratch, and
cannot overwrite the input, including a symlink or hardlink alias.

### `original-wan-clip/v1`

| Field | Contract |
|---|---|
| `provenance` | Source repository/commit/path/blob, actual byte length/SHA-256, verified content pin, original converter source pin. |
| `sequence` | Actual authoring/runtime names, flags, chunk bounds/number/reference, source/selected frame counts, `selected_source_frame_indices`. |
| `joints` | Exactly 15 `{name, fbplyr_skel_index, source_limb_id, parent_index}` entries, same mapping as the first-pose reader. |
| `frames` | Every selected frame, once, in export order. Each has `exported_frame_index`, `source_frame_index`, original `source_frame_flags`, `root_translation`, and `rotations_xyzw`. |
| `frames[].root_translation` | `authoring_xyz` and `scaled_runtime_xyz`, each three finite numbers. Scale is `root_translation.runtime_scale` at clip level (`0.12898`). |
| `frames[].rotations_xyzw` | Fifteen normalized four-number arrays, indexed by `joints`, not WXYZ or world rotations. |
| `record_layout` | Each record's ID, count, stride and record/data byte offsets. A source frame's address is `data_offset + source_frame_index * stride`. |
| `validation` | All source frames, including deselected frames, are checked. Matrix orthogonality/determinant, quaternion norm, continuity and validated-frame summaries. |
| `loop_boundary` | First/last source indices, last-to-first quaternion dots and root delta; diagnostics only, not permission to loop an action. |
| `playback` | Explicitly unresolved tick rate, FPS, frame advance, loop and anchor mode. A separate **source-backed action/script binding** is required. |

Deselected authoring frames are validated and participate in quaternion sign
continuity, but are never emitted as runtime frames. Runtime script frame numbers
address **exported indices**, not authoring indices. A partially selected sequence
may therefore have different source and runtime counts. Original flags are
preserved, not repurposed as invented release/contact events.

Conversion follows the original converter's matrix convention and sign
adjustments through preceding authoring frames. This gives normalized Python
double quaternions for the original binary32 matrices. It deliberately does **not**
reproduce lossy, lookup-table-based SEQ delta compression or claim bit identity
with historical C float output. Frame zero exactly matches `first_pose` for the
same input/sequence/provenance. At an interpolated sample, especially a loop seam
or a gap in selection, use quaternion sign equivalence and shortest-arc
interpolation; do not manufacture a closing keyframe.

### Playback is not the WAN frame count

Trace the active script in `plyrseq.c`, its selection in `player.c` / `detect.c`,
and `lib/ani3d/ANIM.C`, rather than assuming 30 or 60 FPS:

- `change_anim` immediately advances to the first frame break and sets
  `PF_ANITICK`; `player_proc` avoids a second animation update in that tick, then
  sleeps one process tick (`ANIM.C:325–360`; `player.c:1001–1006,1160–1161`).
- `_animate_block` decrements the hold counter once per animation update.
  `_next_frame` holds its new exported frame for the supplied tick count.
  `ANI_RUNTOEND,1` is consequently one exported frame per process tick
  (`ANIM.C:479–570,1621–1648`).
- `ANI_INTSTREAM` and `ANI_STREAMTOEND` have explicit frame/tick ratios,
  integer rounding, special frame-minus-one entry behavior and fractional-frame
  truncation. `AA_TOGROUND` derives duration from current height/vertical velocity
  and gravity; it is not a fixed FPS (`ANIM.C:766–836,1715–1823,2076–2128`).
- The ordinary release graphics configurations explicitly use 57 Hz, while
  debug configurations include other rates (`main.c:258–338,424–440,966–972`).
  Keep the selected source configuration explicit; camera rate alone is not
  proof of animation playback.
- `MODE_ANCHORXZ` / `MODE_ANCHORY` are **one-shot next-frame anchoring bits**,
  cleared when applied. They do not mean "discard root translation forever."
  Secondary animations ignore translation. World drift, facing, model scale,
  physics and anchoring are runtime responsibilities
  (`include/ani3d.h:408–422`; `ANIM.C:578–625,961–1003`).

`QBSTANCE`, for example, has a verified looping `ANI_RUNTOEND,1` script. A throw,
catch, tackle or getup may instead skip entry frames, interpolate into a specific
exported frame, execute several differently timed segments, or stop before the
last selected frame. The exporter still preserves the **complete** selected
clip. Do not loop these actions or apply one idle/rate to all roles.

## Backward-compatible one-pose reader

`read_wan_pose.py` is an original Python-standard-library reader for **one
first-exported pose** in the observed Williams `WIFF` / `3AN ` format. Python 3.9+
is required. It is not an animation player, an archive-wide converter, or a
standing-state detector. Supply a sequence whose gameplay meaning has been traced.
There is no network retrieval and no execution of historical tools.

## Local use

Pass an explicit local file and the upstream **Git blob** SHA-1, not a plain-file
SHA-1. Commit/path metadata comes from the caller's pinned tree; the reader checks
the actual bytes against the blob pin offline. Output is confined to the existing
ignored `.squad/.scratch/` area. Do not commit or publish that output or source WANs.

```sh
python3 tools/animation/read_wan_pose.py \
  .squad/.scratch/reference/nflblitz2000/anim/Qb.wan \
  --sequence QBSTANCE \
  --source-repository historicalsource/nflblitz2000 \
  --source-commit 89f840d58d152a2a9f230cb8d0cdffbd554c8ded \
  --source-path anim/Qb.wan \
  --expected-blob a5bed4bc6699a88281e888660e31e349d4009d3f

PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tools/animation/tests -v
```

The JSON retains the source frame index, first-exported frame index, sequence
name, chunk number/bounds, record/frame byte offsets, file hashes, all 15 named
rotations, original matrices, original evidence offsets, and root translation
before/after the original `0.12898` scale. Output has no timestamps and is
deterministic for identical inputs/metadata.

## Format evidence

The schema is established by the **original tool source**, not float-pattern
guessing: `historicalsource/midwayvideo`, commit
`1792293aaaea00a919b59e8c05716978e85b4333`,
`tools/wan2seq/wan2seq.c`, Git blob
`33c7e489d9b7a40d3b0cd435236db8c357d143fe`.

- Lines 29–53, 207–277: 8-byte big-endian WIFF header; 16-byte big-endian
  chunk header. Chunk size includes number/reference but excludes tag/size.
  Chunks end on even byte boundaries.
- Lines 314–555: `3AN ` body is **little-endian**, using 32-bit longs/floats.
  Its header has a 20-byte name, flags, limb count and ten spare longs.
  Records are signed ID, frame count, then data: `-1` frame flags, `-2` XYZ root
  translations, IDs `1..15` twelve floats per limb/frame (three offsets and a
  row-major 3×3 rotation). ID `0` terminates without a count.
- Selection is `0x4000` on both sequence and frames. `0x1` requests compression
  in the *generated SEQ*, not compressed matrices in this WAN. The first selected
  pose is emitted uncompressed even by the original converter. No compression
  lookup table is needed for this task.
- Lines 585–631 establish normalized **XYZW** quaternions. Hemisphere continuity
  is preserved through preceding deselected authoring frames.

The multiframe reader reuses the unchanged pose reader's bounded container,
record and rotation helpers. It validates all root translations, limb offsets
and matrices throughout the requested sequence, including after the last
selected frame. Other sequences' payloads are not decoded. The pose reader's
CLI, schema, pretty-printed encoding and first-frame results remain unchanged.

The reader rejects malformed lengths, missing/duplicate/unknown records,
inconsistent or excessive counts, unsupported flags/references, non-finite values,
reflections, and non-orthonormal rotations. Unknown ancillary chunks are bounded
and skipped, as in the original tool. Only the selected sequence's record payload
is interpreted; this is not validation of every animation in the file.

## Synthetic regression and mutation evidence

The tests build multiframe WANs independently in memory, with shuffled record
order, nonzero ignored offsets, sparse selection, varying roots and identity /
90° / 180° / hemisphere-crossing rotations. They check first/middle/last selected
frames, every output rotation against its source bytes, exact exported ordering,
root scaling, names/parents, full provenance, legacy first-frame equality,
deterministic CLI artifacts and sign-correct interpolation inputs.

Negative coverage includes malformed container/record boundaries, duplicate
sequences/records, invalid or inconsistent counts, references, unsupported
flags/limb counts, invalid names, corrupt content pins, and non-finite/improper
rotations in **deselected frames after the last exported frame**. Output escape
and input aliases are rejected. Four isolated in-memory mutations of the actual
clip decoder (dropped middle frame, reversed frame order, broken hemisphere
adjustment, wrong root scale) must fail the same artifact assertions. Production
files and original cached inputs are never mutated by the tests.

Batch tests independently construct multiple selected sequences in one WAN plus
a second WAN and a long authoring-name/runtime-alias case. They inspect explicit
catalog selection versus the unchanged default, counts, names, sparse maps, computed content pins,
canonical provenance, one-read caching, relocated-root byte equality and
new-only/read-only-reuse behavior. Corrupt, missing, extra and symlinked emitted
artifacts must fail reuse without being repaired. Catalog symlinks/traversal and
incorrect source-path case fail visibly. Both public catalogs' entire metadata
projections are fingerprinted, not just their aggregate frame totals. Mutations
of the actual expanded metadata and generated synthetic bundle must fail the
same catalog/path/source-pin/selection/output-identity/hash assertions.

`tests/CheckPreparedLibrary.cs` is an opt-in native check entry point for a
C# 7.3 check project linked to the **actual production** `AssetImport` and
`Contracts` sources, with no Unity dependency. Compile those production sources
against .NET Standard 2.1 and reference them from the runnable check project;
do not substitute Unity API stubs or a decoder copy. Its three legacy arguments
are bundle root, baseline inventory file, and baseline inventory root; that
form still requires exactly 56 clips / 3,731 selected / 4,046 authoring frames.
An optional **fourth argument**, the explicit catalog path, checks its baseline
inventory pin, emitted manifest/catalog identity and every catalog entry, and
uses that catalog's exact totals (117 / 10,288 / 13,514 for `ball_sequences.json`).
It invokes the real `SourceAnimationLibrary.Read` and compares all clips' runtime source revisions,
original source SHA-256/blob/path identities, selected maps and every selected
frame value against the local baseline. With the fourth argument it also checks
the actual contract's selected-ordinal mapping at first/middle/last frames.
No original motion is embedded in the checker; this actual-data check is separate
from the synthetic Python suite.

## Space and gameplay meaning

In `historicalsource/nflblitz2000` at the pinned commit above:

- `anim/MAKEFILE:51–57` establishes the translation scale.
- `models/skeleton.h:1–78,180–197` establishes the 15-node `fbplyr_skel` and
  pelvis-first `model266_limbs` mapping.
- `lib/ani3d/pipe3D.c:113–200,753–767` consumes XYZW rotations, composed
  recursively about the skeleton's model-reference pivots. These are **not**
  already-world-space joint orientations or conventional parent-relative pivot
  translations. The WAN's three per-limb offsets are preserved only as evidence;
  the original converter explicitly skips them.
- `plyrseq.c:1074–1084` selects `SEQ_QBSTANCE` in `qb_stance_anim`.
  `player.c:4397–4416` uses it for a stationary, neutral-stick quarterback.
  This is a standing QB ball-holding stance, **not a verified pre-snap stance**
  and not a universal pose for all football roles.

No Unity handedness conversion, anchoring, model placement, pivot rewrite, or
renderer change occurs here. The runtime adapter must respect those original
semantics. Tests generate only original synthetic data; no source animation,
mesh, texture, or reference pivot coordinates are embedded in this tool/tests.
