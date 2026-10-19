# Source fire/SPFX — implemented backend, producer integration pending

This is an **opt-in portable source-derived backend**, not a new default profile.
It contains actual cooperative burst/player-puff/ball-puff/white-flash bodies,
source activation helpers, an authenticated original-input reader, and a Unity
mesh/texture/shader consumer. No existing source factory, gameplay producer,
scene, setting, package, or default/strict profile is changed.

## Production boundaries

| File | Responsibility |
| --- | --- |
| `Contracts/SourceFireSpfxContracts.cs` | Pure C#7.3 immutable process, call-site, ordered host-slot, historical cache, sound, and render contracts |
| `AssetImport/SourceFireSpfxData.cs` | Pure authenticated eighteen-input reader, shared TGA header decoder, original RGBA pixels, source-unit SDL geometry |
| `Runtime/SourceFireSpfxRuntime.cs` | One synchronous57Hz process runtime; all randomness goes through the Core host callback |
| `Presentation/SourceFireSpfxRenderModel.cs` | Pure source camera-space triangle/UV/frame/material consumer; usable by another C# renderer |
| `Presentation/SourceFireSpfxAsset.cs` | Explicit authenticated Unity asset boundary |
| `Presentation/SourceFireSpfxRenderer.cs` / `.shader` | Real seventeen-texture, source-SDL mesh draw backend; no particles, tint fallback, render clock, or renderer RNG |
| `Editor/SourceFireSpfxImporter.cs` | Explicit non-overwriting private bundle import; not automatically executed |

All paths above are relative to `Assets/Scripts/`. The first three files have
no Unity dependency. The pure render model also has no Unity dependency.
Neither a .NET browser preview nor archive-reference compilation is a Unity
build, native execution, screenshot, or pixel qualification.

### Active source horizontal projection

The raw `draw_spfx` camera-space `cm` and historical inverse-cache equations
remain unchanged. Active NFL `lib/ani3d/geom.s` (blob
`fecf16a0511ab5d14a7f5e3a96742407e1d43aa6`, SHA256
`f6c729fa3b0a70fcdaca41df8151729a747b8cdc8a4fdcf4f06b094156bb643a`)
uses `nmadd.s` at lines169/201 to negate accumulated camera X before the
lines196/226 perspective step. The portable **unclamped** equation is
`rasterX = 256 - 664 * cameraX / cameraZ`, for both512×384 and512×256.
The real shader therefore uses a negative clip-X numerator as well. Camera X
is not secretly reflected in `CameraTranslation`, the fixed `cm`, cache
reconstruction, or mesh upload to compensate for projection.

Focused tests cover nonzero positive/negative camera X, both original rasters,
actual original-SDL consumer packets before/after co-translating a rotated
camera and its world object, and unchanged raw camera-space coordinates.
A bounded C99 reference evaluates the pinned assembly's continuous equations;
the runner also evaluates the **actual shader expression**, using its actual
`CameraVertices`/`_SourceRaster`/identity-DrawMesh consumer wiring. These are
numeric/source-contract checks, not HLSL/GPU execution or native pixels.
MIPS fused arithmetic, clamp/rounding, native W-buffer and TEXUS equivalence
remain unqualified.

## Private data preparation

The additive impact-dust path uses the same runtime and draw_spfx consumer.
Explicit `prepare.py --with-dust` writes schema v2, retaining every v1
SDL/17-TRAIL input and adding ten ordered original DIRT TGAs. Its authenticated
nonpixel developer-area adapter reuses the strict decoder; native WMS remains
unqualified. V1 is still accepted for this retained component suite, while
enabled Unity/browser composition requires the full v2 dust generation.
See [impact-dust regression scope](../README.md#original-impact-dust).

Use only explicitly authorized local originals. `tools/fire-spfx/inputs.json`
pins the SDL and all seventeen TGAs; public availability does not grant reuse
or redistribution rights. The preparer reuses the existing environment
SDL/TGA parser. The source SDL is7.51; its shared `cv`/`st`/triangular-polygon
grammar is consumed through a declared version-header adapter to the existing
8.2 reader. Original bytes/hashes are preserved. No generated `spfx.h` is claimed.

```sh
python3 tools/fire-spfx/prepare.py \
  --original-root "$AUTHORIZED_ORIGINAL_ROOT" \
  --output .squad/.scratch/fire-spfx-portable-inputs \
  --authorization-reference "$EXPLICIT_LOCAL_REUSE_AUTHORIZATION"
```

The source `-f4.0` recipe is retained: the original SDL geometry is not
normalized to a substitute quad. Original32x32 BGRA8 TGAs decode to unchanged
bottom-first RGBA8, including every alpha byte. Each integer Glide aspect is
derived and checked against **decoded authoring dimensions**, not asserted to
be an observed WMS header. Portable normalized UV origin, original SDL7.51,
the grammar adapter, authorization, and false native/header-equivalence flags
are mandatory metadata.

The Unity importer entry is
`SourceFireSpfxImporter.ImportPortableBundle(absolutePreparedRoot, manifestSha256)`.
It creates a new, non-overwriting private `Assets/Imported/` package at resource
key `BlitzSourceFireSpfx/source-fire-spfx-authoring-tga-sdl-v1/fire-spfx`.
Neither original bytes nor derivative meshes/pixels belong in Git.
The Unity command-buffer stage is explicitly portable
`CameraEvent.AfterForwardAlpha`, drawing the owned puff projection in source
object-list order. This is not verification of the native global object list
or ordering against every non-FX consumer; the integrated host must qualify
that rendering boundary separately.

## Minimal Core host handoff

1. Load the actual `SourceFireSpfxBundle`, construct `SourceFireSpfxRuntime`,
   then `Reset` with an explicit `SourceFireBootstrap`. Program boot starts the
   latches and ball effect at zero/NONE. A real `ball_proc` restart separately
   submits `ResetBallProcessEffect`; lineup-latch reset does not infer this.
2. For **every source dispatch**, including menu dispatches, call
   `BeginSourceTick(SourceFireTickInput, SourceFireHostCallbacks)`. Supply the
   real logical source tick and explicitly chosen pcount epoch. This API does
   not infer an original epoch from match-relative tick zero or UI Continue.
3. Provide the complete ordered native-level-zero slice as effect visits and
   **real Core host continuations** (`game_proc`, individual C2 whistle
   continuations). No foreign process universe is manufactured here. A host
   continuation must establish its own Core random/process context. On an
   effect wake, `EnterEffectWake` must establish that effect's Core context
   from the supplied logical serial, function, and wake ordinal.
4. `Draw` must invoke the existing Core `SourceDraw` ledger with
   `SourceRandomKind.RandRange`, bound and original site. **Do not** clone a
   random provider, start a context for synchronous `MaybePlayerFire`, or
   reinterpret `WithinWakeCallOrdinal` as an RNG call index. The latter orders
   all source calls; Core continues its own random-call index. The eligible
   `randrng(4)` happens even when the subsequent pcount modulo rejects emission.
5. `AllocateEffectProcessSerial` allocates a fresh globally distinct **logical**
   process serial from the host's shared identity space. These are not target
   pointers/ABI addresses. `Processes` exposes immutable current effect nodes
   for the host to merge with its known level-zero continuation order.
6. `VerifiedDispatchWitness` accepts an **already externally verified** witness
   digest in `sha256:<64 lowercase hex>` form. A sort also requires its
   `KnownEffectListOrder` permutation. Digest syntax is not native verification.
   Explicit witnessed restart visits can revisit nodes or reach a newly
   inserted head later in the same traversal; birth never runs entry inline.
   Unknown strict order/epoch fails. `PortableDeclaredSlots`, or the clearly
   named `PortableHeadThenHostTick` convenience choice, is a declared portable
   execution policy, **not MIPS qsort/tie/global-process proof**.
7. `ReadCachedPlayer` returns the actual immutable fifteen-joint camera-space
   cache **and its historical camera**. Cache producer tick must equal paired
   camera tick. A cache older than the preceding dispatch requires explicit
   retention/suspension evidence. Never re-pose `SourceWorldJoint`, substitute
   an identity camera, or pair old joints with the new camera. `ReadBallWorld`
   supplies the live source-world ball point for fire puffs at their first wake.
   The separate `ReadBallRender` callback supplies actual XYZ/fwd/fwd2/phi/
   scale/flags at non-fire `trail_ball_proc` entry; missing orientation fails
   closed, never falling back to the fire point or a live re-pose.
8. Raw flag reads/writes must access the actual shared actor bits immediately;
   no flash reference counts or kill cleanup. Sound submission preserves each
   call (including duplicate192 calls), bank, volume, pan, priority expression,
   optional resolved priority, site, and resolved/unresolved result. Submission
   is not audible playback. An unresolved priority cannot be marked resolved.
9. After all source phases, call `CaptureFrame` with actual current/retained
   cambot, source DIP raster, and inherited cull. Pass that immutable frame to
   `SourceFireSpfxRenderer.ConsumeAfterSourceStep`. A skipped host frame holds
   the previous packets; the consumer never advances a process or draws RNG.
   Frames bind the exact authenticated asset manifest SHA.

### Exact producer points requested from Core

| Reached source point | Runtime hook |
| --- | --- |
| `_mode_wpnps` tail | `WeaponPreSnapTail`, using actual actor, receiver/completions, team fire, onside/kickoff inputs |
| `_mode_dbps` tail | `DefensivePreSnapTail`; shared global count4, no extra human/kickoff/phase guard |
| `_mode_qb` tail / reached carrier `_mode_wpn` fire site | `MaybePlayerFire` with native vx/vz, station, dragging/flip flags, lower reacting bit, actual mode/team side |
| Successful offensive `standard_catch`, after its mode/control updates | `SuccessfulOffensiveCatch`; not an `OnFire` rising-edge adapter |
| Original second-loss branch's SPFX point, after Core's actual state changes | `DefensiveSecondLossFireSite`; actual selected bank and player3-or10's known hit partner (`-1` known NULL, nullable unknown) |
| `ball_proc` free/phantom post-physics, **post-increment** flight-time point | `BallFreeOrPhantomStep` using named mapping; queue BALL_PID football ghosts for PCLOUD/BTRAIL, retain separate even-flight fire triplet; return Blink selection to the ball owner |
| Quick/kickoff or audible reset | Delete puff objects, then kill SPFX, in original order |
| `clobber_playsel` | Kill SPFX, then delete puff objects |
| Interactive selection entry after no-selection early return | Kill PFLASH, suspend `(0,0x8000)`, resume SPFX, hide existing objects |
| Interactive selection exit | Resume all, then unhide existing objects |
| `nfl.c:lineup_players(side)` actor loop | Apply `SourceFireCommand.ReinitializePlayerFlash(site, SourceFireLineupScope.FromOriginalSide(side))`; clear only raw `0x400` for side0 actors0..6, side1 actors7..13, or explicit all-player `side = -1` |
| `nfl.c:3369-3373` lineup tail | Apply the separate `ResetLineupLatches` control for the three global latches; not permission to clear every actor |

`reinit_player` (`player.c:1415-1428`) changes static data/decal/scale, not the
white-flash bit. The flash-clear command requires a specified typed lineup scope;
default scope and generic `Control(ReinitializePlayerFlash, id)` reject, so actor0,
id0 and omitted arguments cannot silently request a global clear. One-team audible
lineups retain the other team's flags and all sleeping PFLASH processes. Only
explicit `side = -1` selects all14 actors. The clear reads/writes only selected actors,
preserves every other raw bit, consumes no RNG, and does not reset global latches.

The combined revision retains the18,302 baseline assertion category, adds J1's
418 projection assertions and J2's80 scoped FLASH assertions, and retains all13
original compiled controls plus two sign controls and one global-clear control.
The runner checks measured additive categories rather than padding the count.
Side0/side1/all read/write sets and flags are compared with the pinned original
`lineup_players` loop/address/flag-mask window on declared0x440 inputs under C
bounds/undefined sanitizers. This is a safe component probe, not a match replay.

The separately typed non-fire football objects are now exposed through
`SourceFireRenderFrame.BallTrails`, not `Particles`; their original five-stage
lifetime shares this scheduler and the existing source selection/hide/suspend/
resume controls. The actual free-ball Unity/browser consumers reuse genuine
medball2/HEDSHU2 with local ALT, global draw gates and CC_ALPHA RGB/blend behavior.
See [the focused trail suite](../ball-trails/README.md). Core retains the existing
`BF_WHITE` blinking write. The runtime still does not own the main ball, players
or unrelated effects, and does not invent native pointer cleanup.

`PLAYER_PID` ball children are distinct from `SPFX_PID`; process kill is not
object deletion. Objects of equal priority retain actual arrival order. Hide
only affects existing nodes. `die(1)` has no automatic object or flash cleanup.
All active source kill sites use full masks; an arbitrary partial kill mask is
rejected until target `NODESTRUCT` metadata is supplied.

Strict deleted-node access and killed stack-backed `object_data` are explicit
undefined boundaries. The opt-in
`PortableCancelDeletedAndFreezeOrphan` policy cancels an owning continuation
when it next accesses an externally deleted object, and retains a killed
owner's last object value until actual deletion. It records these deviations;
it does not claim target stale-pointer reuse or native memory behavior.

### Existing player flash consumer handoff

`SourceFirePlayerFlashState(actualRawFlags)` identifies **only `0x400`**.
At the actual `draw_player` white branch use
`DECAL_TEXTURE_ADD_CCALPHA`, constant `0xc0000000`; restore
`TEXTURE_TIMES_ALPHA`, `0xff000000` at the source restoration point.
Preserve surrounding source branch order and other bit writers. Wiring that
branch into the existing player material consumer is still pending; a fire
tint is not a substitute.

## Proof commands

All output paths must be new ignored directories. These tools never launch
Unity, import assets in an Editor, activate a license, or access a live browser.

```sh
python3 tests/fire-spfx/run.py \
  --bundle "$PRIVATE_BUNDLE" --manifest-sha256 "$MANIFEST_SHA256" \
  --original-player "$PINNED_PLAYER_C" \
  --original-object "$PINNED_GOOSE_OBJECT_C" \
  --original-process "$PINNED_GOOSE_PROCESS_C" \
  --original-geom "$PINNED_ACTIVE_NFL_GEOM_S" \
  --output tests/fire-spfx/.artifacts/production
python3 tests/fire-spfx/test_inputs.py --original-root "$AUTHORIZED_ORIGINAL_ROOT" \
  --output tests/fire-spfx/.artifacts/inputs
python3 tests/fire-spfx/mutations.py \
  --bundle "$PRIVATE_BUNDLE" --manifest-sha256 "$MANIFEST_SHA256" \
  --original-player "$PINNED_PLAYER_C" \
  --original-object "$PINNED_GOOSE_OBJECT_C" \
  --original-process "$PINNED_GOOSE_PROCESS_C" \
  --original-geom "$PINNED_ACTIVE_NFL_GEOM_S" \
  --output tests/fire-spfx/.artifacts/mutations
python3 tests/fire-spfx/api_compile.py --unity-editor-data "$ACTUAL_UNITY_EDITOR_DATA" \
  --output tests/fire-spfx/.artifacts/api
```

The production runner compiles actual C#7.3 files and compares twelve
coroutine function cases against pinned C bodies, nine original
`maybe_plyr_fire` cases, sixteen steps of the original ball-selector/fire
triplet body, and twelve stages of extracted Goose object/process controls.
These are declared component fixtures, not a full native process universe.
Library dependencies in the C shims are explicit fixture bindings, never
asserted target enum/ABI values. Partial kill masks and stale pointers are
not tested as defined native behavior. Comparisons inspect sleep, lifetime,
positions/velocity, frame bounds, raw flags, exact RNG family/bounds/line/value/
order, child arguments/groups, source draw basis and material-state calls.

The original thirteen causal mutations, two projection-sign controls and one
wrong-global-lineup-clear control must
**compile successfully**, then fail an actual
production-field assertion or original-C comparison. They cover parent delay,
frame advance, RNG, child/process/object order, historical basis, material/
alpha state, future-hide behavior, sound duplication and preserved resume
sleep. CPU sign reversal fails the named numeric output249.36 versus262.64.
An independent reversal in the real shader must leave managed numeric checks
passing, then fail its actual consumer's shader expression against the active
assembly equation. A compiler failure is not counted as a killed mutant.
The production runner emits its small real-source project in the new ignored
output directory; no pre-existing ignored `.csproj` is required.

The API runner uses actual archive Unity C#7.3/netstandard2.1 references for all
current runtime and separate Editor files in five Editor/WebGL profiles. The
input/inventory check preserves all124 existing in-Assets metadata files plus
the Assets root metadata, verifies the exact named combined inventory of ten
added scripts/twelve new GUIDs (including the Core producer and two kick
scripts), and rejects real parser/hash/format/origin failures. The measured
current totals are123 C# sources/137 unique GUIDs; no guessed count substitutes
for the exact file-set and metadata checks.

## Qualification limits

The Unity shader applies the source fixed camera-space X/Y exchange and Z
reversal, continuous664 projection and low-resolution2/3 Y scale, authentic
ordinal texture/UV binding, texture-alpha/color combine, wrap/bilinear,
`ZTest Always`, `ZWrite On`, and both source-alpha blend pairs. Inherited
culling is explicit. The hardware W-buffer request is preserved as metadata,
but depth is **portable Unity projection depth**, not native Glide W-buffer
precision. Native MIPS FPU clamp/FMA, TEXUS ARGB8332 conversion, generated
header/ST identity, quantization/alpha association/mips, target packing,
global process sort/clock/menu timing, Unity/native pixel execution, DSP,
full-M1 integration, all16 whole-game rows, and actual player/producer consumer
proof remain **unqualified**. Do not promote old profiles or label this
backend as feature-complete while producer integration is pending.
