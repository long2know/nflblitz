# Unity source-port integration contract v1

**Owner:** Lead. **Contract checked:** October 16, 2026 (PDT).
**Status:** actual Unity 6000.3.24f1 host import/build verified for the original
player mesh/rig/textures, authentic static `QBSTANCE` frame 0 and source-derived
normal camera. Native pose apply/reload and the matching Docker-served standing
WebGL desktop/portrait visual-controls subset passed. The later minimal
`NFL BLITZ 2000` identity/field-axis legend correction passed a host build,
matching Docker promotion and six fresh browser controls checks. Current
desktop/portrait images were reviewed visually; exact tiny-font legend OCR
remains a separately recorded failed diagnostic, not an automated approval.
The kernel is headless-tested, but this is not complete original-game parity.

## Existing baseline, not the acceptance target

**Full source gameplay parity is required.** The simplified kernel and static
player preview below describe existing behavior only. The continuation boundary
and [full completion matrix](source-parity.md#full-source-completion-matrix--executable-acceptance)
supersede any earlier statement that two plays, static actors or a small prototype
suffice. Keep the working build while replacing the approximations; do not
relabel them as source-equivalent.

The repository root is the Unity project: `Assets/`, `Packages/`, and
`ProjectSettings/`; **do not create a nested `Unity/` project**.
`Assets/Scenes/Boot.unity` is the built scene. Its presentation combines a
procedural field/HUD with fourteen actors and one human-controlled Home team
versus deterministic Away AI. Source mode uses the locally generated original
player prefab; explicit graybox mode retains procedural actors. The implemented loop
covers play selection, snap, running/turbo, targeted pass and automatic catch,
tackle/out-of-bounds, down/possession resolution, scoring, and restart. The current
source browser proof covers only the subset recorded in the
[evidence ledger](source-parity.md#current-implementation-evidence).

**Visual target correction, October 15, 2026:** this procedural Yard Rush graybox
does **not** meet the requested original-game appearance. Narrowing the full-fidelity
request to this presentation was a scope error, not a visual-fidelity milestone.
The supplied NFL Blitz 2000 screenshot is the camera/player/stadium/HUD target;
the [source-data ledger](source-parity.md#visual-target-correction-and-asset-audit)
separates imported mesh/textures and recovered first-pose data from remaining
field/stadium, HUD, roster, animation, lighting and edition-equivalence gaps.

Two port-authored offensive patterns (`Run`, `QuickPass`) and two defensive choices
(`Cover`, `Blitz`) currently exist; they do not suffice for parity. They are not
archive formations. Home remains
playable on defense after possession changes. Four quarters and a final score
make a complete, deliberately simplified kernel match; a tie is a valid final
result. That is not a complete original game or a browser full-match result.
Those behavioral approximations remain explicit rather than redefining the
original-game target.

Source evidence and deviations are in [source-parity.md](source-parity.md).
That ledger distinguishes source-traced rules, pure tests, native checks,
successful Unity builds, and bounded browser observations. None is whole-game
parity certification. See [build/run instructions](build.md) and
[controls](../README.md#controls).

## Single-writer map and bounded acceptance

`.meta` ownership overrides every directory row: **Unity Systems writes all `.meta`
files**, including metadata for Lead's contracts and Unity's gameplay scripts.

| Accountable owner | Exclusive writes for this slice | Acceptance / handoff |
| --- | --- | --- |
| Lead | `docs/**`, `Assets/Scripts/Contracts/**`, root `README.md` | Maintain stable contracts, source evidence and current build/run status. |
| Unity | `Assets/Scripts/Gameplay/**`, excluding `.meta`; bounded `tools/assets/**` and `tools/animation/**` recipes/readers | Own `Blitz.Gameplay.GameSimulation`, rules, AI and interactions; offline original-data preparation/first-pose decoding. No gameplay Unity references, scene changes, generated `.csproj`, or package changes. |
| Unity Systems | `Assets/Scripts/AssetImport/**`, `Assets/Scripts/Presentation/**`, `Assets/Scripts/Runtime/**`, `Assets/Scripts/Editor/**`, all `.meta`, `Packages/**`, `ProjectSettings/**`, scene/prefab/material assets | Own source parser/native importer, rig/static-pose adapter, field/actors/ball/HUD, fixed-step bridge and camera. Single writer for shared Unity configuration and local imported assets. |
| Frontend | Dockerfiles, compose/nginx configuration, root build/serve scripts, outer browser files and `.github/workflows/**` CI only | Reproducible build/serve entry points, Unity version/toolchain evidence, correct WebGL HTTP delivery and a documented container launch. Do not write Unity configuration or scripts under `Assets/`. |
| Quality | `tests/**`, including all validation tooling and `tests/Blitz.Core.Tests/**` | Compile the exact core/contract files, test rules and replay determinism, and provide bounded browser smoke evidence when an artifact exists. No root project, gameplay, scene, or package edits. |
| Backend | None | Dormant: no persistence, API, accounts or database is needed. |

Source/documentation work can proceed in parallel only outside another owner's
write set. Import, native checks and builds require an explicit **exclusive
Editor execution slot** on the root project. The source owner releases a stable
snapshot before build/metadata/browser acceptance; no concurrent scene, prefab,
metadata or `Library/` writers. Browser evidence must identify the served
generation, not assume the latest disk source is running.

The selected editor is **Unity 6000.3.24f1**, recorded in project settings by
Unity Systems, with the **.NET Standard 2.1** API profile. The real host builds
compiled the source-player/camera/static-pose layer in that environment, with
native pose apply/reload and standing-browser evidence recorded separately.
Keep ordinary C# 7.3-compatible syntax
and profile-compatible APIs.
No records, `init`, Unity-dependent core types, NuGet core packages, DI container
or event framework.

## Executable boundary

The authoritative signatures, enum members, DTO constructors and scalar constants
are in [`GameplayContracts.cs`](../Assets/Scripts/Contracts/GameplayContracts.cs),
namespace `Blitz.Contracts`. Do not redeclare these types in Gameplay or Presentation.
The interface avoids shared snapshot implementation constructors.

Unity must provide this concrete entry point, with no additional required arguments:

```csharp
namespace Blitz.Gameplay
{
    public sealed class GameSimulation : Blitz.Contracts.IGameSimulation
    {
        public GameSimulation(int seed = 1);
        public Blitz.Contracts.IGameState State { get; }
        public System.Collections.Generic.IReadOnlyList<Blitz.Contracts.GameEvent> Events { get; }
        public void Step(Blitz.Contracts.PlayerCommand command);
        public void Restart(int seed = 1);
    }
}
```

The block is a signature sketch, not a compilable implementation. Suggested entry
file: `Assets/Scripts/Gameplay/GameSimulation.cs`. Unity owns its internal rule,
AI, and snapshot types. Unity Systems stores an `IGameSimulation`, instantiated
with `new Blitz.Gameplay.GameSimulation(1)`. No scene object enters the constructor.

Example commands (pass targets below are global player IDs):

```csharp
game.Step(new PlayerCommand(action: InputAction.SelectPlay, play: PlayId.QuickPass));
game.Step(new PlayerCommand(action: InputAction.Snap));
game.Step(new PlayerCommand(new FieldVector(1f, 0f), turbo: true));
game.Step(new PlayerCommand(action: InputAction.Pass, targetPlayerId: 4));
game.Step(new PlayerCommand(action: InputAction.Continue));
game.Restart(1);
```

### Coordinates, identity and read semantics

- `FieldVector.X` runs lengthwise in **yards**, Home goal line `0`, Away goal line
  `100`; end zones extend to `-10` and `110`. `Z` is lateral, centered at zero.
  Use `SliceRules.FieldHalfWidthYards`, the normalized archive geometry ratio,
  not an assumed regulation-NFL width. Actors and ball positions are centers.
- Home always attacks `+X`, Away `-X`; `OffenseDirection` is respectively `+1`
  or `-1`. No halftime camera/field reversal in this slice. LOS and first-down
  line are **absolute X**, not possession-relative yard numbers.
- Rendering maps `(X, Height, Z)` to Unity `(X - 50, Height, Z)`, with one Unity
  unit per yard; player ground height is zero. `Velocity` is yards/second.
  `BallView.Height` is yards above ground. No Unity Physics result changes rules.
- IDs `0..6` are Home, `7..13` Away; ID = team index * 7 + slot. Slots `0..2`
  are blockers, `3` quarterback, `4..6` eligible receivers; the same seven actors
  become defenders. `Players` contains fourteen entries in ID order.
- `ControlledPlayerId` is always Home: its carrier on offense, its quarterback
  while a pass is in flight, and an initially nearest-to-ball defender on defense.
  Catch transfers control to the Home receiver. `SwitchDefender` chooses the
  nearest **other** Home defender, with lower ID breaking ties.
- `Turbo01` is bounded `0..1`. `Ball.CarrierId` is `-1` unless `Mode == Held`;
  `TargetPlayerId` is `-1` unless a pass is in flight. Positions remain valid
  in dead-ball views. No sentinel team IDs or nullable player objects.
- `State` is a read-only, detached snapshot: a retained snapshot and its player
  collection must not change after another step/restart. `Events` is a detached
  ordered list for the **last step only**, replaced even when empty. Drain it
  after *each* simulation step, not just once per rendered frame.

## Legacy tick and command semantics

These remain the compatibility behavior of the current kernel, not the target
source rules. The full-source boundary below preserves callers while the source
scheduler, clock, playbooks and outcomes replace these approximations.

`Step` represents exactly `1 / 60` second, accepts no delta time, and increments
`State.Tick` once; `PlayNumber` starts at zero and increments once per snap.
`Restart(seed)` returns immediately to tick zero, score zero, quarter one,
`PlaySelect`, Home possession at X=20, down one and target X=50; it clears events.
The same seed and per-tick command stream must reproduce all state/events on the
same runtime. Cross-architecture floating-point bit identity is not promised.
Use stable actor iteration/tie-breaking and no wall-clock or environmental entropy.

`Move` is an absolute X/Z direction, magnitude clamped to one; non-finite components
are treated as zero. Movement and `Turbo` are held inputs. `Action` is one
edge-triggered command; repeat held movement but consume an action only once.
`Play` matters only for `SelectPlay`, `TargetPlayerId` only for `Pass`; a default
command is neutral regardless of its unused zero-initialized fields. Invalid or
wrong-phase actions are ignored without throwing or changing rules.

| Entry phase | Accepted actions and transition |
| --- | --- |
| `PlaySelect` | `SelectPlay` commits `Run`/`QuickPass` if Home has possession, otherwise `Cover`/`Blitz`. Choose Away's complementary-role play deterministically, line up actors, publish `HomePlay`/`AwayPlay`, enter `PreSnap`. No selection timeout in this slice. |
| `PreSnap` | Freeze movement. Home offense may `Snap`; otherwise auto-snap after 300 subsequent pre-snap ticks. Away offense automatically snaps after 60 subsequent ticks; Home `Snap` is ignored on defense. Show the actual countdown in `PreSnapTicksRemaining`. |
| `LiveBall` | Apply movement/turbo to the controlled actor. Accept eligible `Pass`, defensive `Tackle` and `SwitchDefender`. Run AI and geometric ball interactions. One terminal outcome enters `DeadBall`. |
| `DeadBall` | Freeze actors. `Continue` enters `PlaySelect` at the already-resolved next scrimmage; if the period expired, advance/reset the period clock first, or enter `GameOver` after quarter four. |
| `GameOver` | Freeze everything except the monotonically incrementing tick. Display final score/tie. Restart is an explicit `Restart(seed)` call, available from any phase. |

Dispatch by the phase **at entry**: selecting a play does not decrement its new
pre-snap timer; snapping does not also simulate a live tick. Only a step entered
as `LiveBall` decrements the quarter clock, clamped to zero. At zero finish the
current live play, then emit `QuarterEnded` once; do not cut off a pass or run.
Quarters 2–4 retain the next-scrimmage possession/spot/down; no halftime kickoff.
This clock policy is an approximation, not the archive's accelerated scheduler.

### Rules and interaction policy

- A series grants four downs to gain thirty yards, with the target capped at the
  opponent goal line. Do not use ten yards. At a non-scoring play end, spot whole
  yards toward midfield (ceiling below X=50, floor above), clamped to `1..99`.
  This follows source intent, not exact historical float-cast rounding.
- A gained target resets down to one and sets a new thirty-yard target.
  Otherwise advance the down; a failed fourth down gives the other team the ball
  at the same absolute spot, down one with its oppositely directed target.
  An incomplete pass consumes a down but retains the snap LOS, including on fourth.
- Resolve scores once: crossing the opponent goal while in bounds gives six;
  being tackled/out of bounds in one's own end zone gives the opponent two.
  Check sidelines/back boundary before goal scoring at an ambiguous same-tick
  corner; an in-bounds goal crossing precedes a same-tick tackle.
- After a touchdown the non-scoring team receives; after a safety the scoring
  team receives. Start the receiving team's new series at **its own 20**.
  This is a deliberate substitute for kickoffs. No PAT, two-point attempt or
  automatic seventh point.
- Resolve next possession/down/LOS/target immediately on entering `DeadBall`;
  actor/ball positions remain at the previous play end until `Continue`.
  Thus dead-ball rule fields describe the **next** scrimmage. Preserve the
  terminal `LastOutcome` through selection/pre-snap; clear it at the next snap.
- First-down/turnover outcomes take precedence over an ordinary tackled/OOB/
  incomplete `LastOutcome`; retain both cause and rule effect in ordered events.
  A terminal step emits its cause, then first-down/turnover/score effects, then
  `PossessionChanged` if needed, then `QuarterEnded` if applicable.
- Permit at most one forward pass per play, only from the quarterback holding
  the ball behind or on the snap LOS, to an own-team receiver slot `4..6`.
  Illegal targets or late/second passes are ignored. Use deterministic flight
  to a fixed lead point, automatic geometric catch on arrival, otherwise
  incomplete; show a cosmetic arc through `Ball.Height`. No interceptions,
  bobbles, fumbles, laterals or historical catch-control delay in this slice.
- Unity selects original, readable tuning for movement, finite turbo drain/
  recharge, routes, blockers and contact radii. Away uses simple deterministic
  routes/pursuit and automatic contact tackles. Home-controlled defense has
  explicit `Tackle` within a bounded reach; its other defenders may auto-tackle.
  These are playable approximations, not verified collision/AI parity.

For `GameEvent`: `Tick` equals the published state tick; `Team` is the affected/
scoring/new-possessing team, `PlayerId` the affected carrier/receiver or `-1`.
`Position` is the action/end/new-scrimmage position as relevant. `Value` is six
or two for scoring events, the ended quarter number for `QuarterEnded`, and
`+1` Home win / `-1` Away win / `0` tie for `GameEnded`; otherwise zero.
Neutral quarter/game events use `TeamId.Home` with `PlayerId == -1`.

## Full-source animation/gameplay boundary

Authoritative additive types are now in
[`SourceAnimationContracts.cs`](../Assets/Scripts/Contracts/SourceAnimationContracts.cs)
and the extended `PlayerView`, `BallView` and `GameEvent` constructors in
`GameplayContracts.cs`. All original constructor calls and `IGameSimulation`
members remain source-compatible. Default animation is **unspecified**, not a
verified pose or permission to reuse the stationary QB. No Unity types enter
these DTOs; keep C# 7.3/.NET Standard 2.1 compatibility.

| Writer / exact files | Immediate contract / deliverable |
| --- | --- |
| Lead: `Assets/Scripts/Contracts/*.cs`, existing `docs/**` | Stable DTOs/units and this matrix; `.squad/.scratch/full-parity-contracts.json` is the local machine-readable handoff, not runtime data. No `.meta` writes. |
| Unity: `tools/animation/**` | Finish the already-assigned offline `original-wan-clip/v1` producer: pinned WAN identity, sequence/flags/limb mapping, selected original indices, raw and source-scaled XYZ root, fifteen normalized XYZW rotations/frame. No second exporter or WAN investigation is needed. |
| Unity: `Assets/Scripts/Gameplay/PlayerSimulation.cs`, `SimulationState.cs`, `GameSnapshot.cs`, new `SourceAnimationState.cs`; then `GameSimulation.cs`, `GameplayRules.cs`, new `SourceClock.cs` | Publish authoritative facing/role/action and explicit script/frame/layer/transition/root inputs. Initially label approximate action selection honestly. Port source sequence semantics and then the first core groups T1/T2/R1/S1/S2; preserve the entry point and detached snapshots. |
| Unity Systems: new `Assets/Scripts/AssetImport/SourceAnimationLibrary.cs`, `Assets/Scripts/Presentation/SourceAnimationPlayer.cs`; `SourcePlayerRig.cs`, `FieldScene.cs`, `Runtime/FixedStepBridge.cs` and all `.meta` | Bounded clip loader/catalog, original pose sampler and rig application; drain snapshots/events per tick and render previous/current poses. Reuse `SourcePlayerPose`'s proven basis, pivots and reflection-aware rotation conversion. Do not put source state decisions in the view. |
| Quality: `tests/**` | Original-function/trace oracles, DTO backward/default/detachment checks, actual clip frame/matrix/root checks, then production core and served-browser cases for every matrix row. Existing suites still describe the old slice unless new assertions prove replacement behavior. |

### Source clock, not clip FPS

- `SourceTiming.DefaultTicksPerSecond == 57` selects the archive's normal 57 Hz
  profile (`main.c:graphics_init/get_tsec`). Seattle's other resolution branches
  also contain 54/72 settings; do not claim every hardware/configuration is 57.
  Record platform/build flags and source-adjustment values in each trace.
- Keep public `Step(PlayerCommand)` at **1/60 second** for existing bridge callers.
  The source port owns an integer remainder: add 57 per call, execute a source
  tick when it reaches 60, subtract 60. Sixty calls execute exactly 57 source
  ticks, never 60 animation advances. Retain pending action edges until consumed
  once by a source tick; calls without a source tick publish no repeated events.
  All actor scripts, motion, ball, contact, AI and game clock ultimately run in
  that same source domain/order, not separate floating accumulators.
- `ISourceGameState` is an optional detached `IGameState` extension with
  `SourceTick`, `SourceTicksPerSecond`, `PackedGameTime`, `ClockActive`.
  `IGameState.Tick` remains the host-step counter; event `SourceTick` identifies
  actual source execution. The packed low byte is the source clock countdown
  field, **not** 60-Hz remaining time or decimal tenths. Legacy quarter/timer
  fields may be HUD projections; never use them as the source rule clock.
- `player_proc` calls `animate_model` once unless `PF_ANITICK` says the frame
  break was already processed. `_animate_block` decrements `anicnt`; a positive
  script byte advances one frame and holds that many ticks. `ANI_RUNTOEND,1`
  therefore holds a selected frame for one tick. `_ani_streamtoend` rounds
  `ticks_per_frame * remaining_frames` to a tick count, including its special
  initial-frame rule; `qb_t_run_anim` uses **0.81**. `ANI_INTTOSEQ`,
  `ANI_INTSTREAM`, variable frame/count, callbacks and flags are behavior,
  not metadata to replace with a 30/60-FPS looping Animator.

### Pose input semantics

The renderer consumes this exact signature; its implementation belongs to Systems:

```csharp
void Apply(PlayerView previous, PlayerView current, float renderInterpolation01);
```

The input is `current.Animation` (`ActorAnimationView`); no clip parser is supplied
by Contracts. The producer owns the JSON field layout; Systems consumes that
layout rather than changing it to match guessed C# serializer fields.

- **Shared data API:** Systems implements `ISourceAnimationLibrary.GetClip(string)`
  and `ISourceAnimationClip.GetFrame(int sourceFrame)`. `SelectedSourceFrames`
  maps runtime sequence ordinals to original WAN indices; its count is the
  runtime sequence length, while `SourceFrameCount` is the authored length.
  `SourceAnimationFrame` exposes immutable fifteen-joint `Pose`, raw
  `AuthoringRoot` and pre-actor-scale `ScaledRoot` in source units. Clip identity
  includes source revision/path/blob/SHA256 and decoded SHA256; `SourceFlags`
  means WAN flags, not actor mode bits. Loader validation pins these, bounds
  frames, verifies mapping/limbs/quaternions and rejects duplicate sequence keys.
  Missing sequence/frame throws with identity diagnostics, never frame-0 fallback.
  Core and view consume the **same immutable library** without Unity references.
  Unity adds an overload `GameSimulation(ISourceAnimationLibrary animations,
  int seed = 1)` for injection; retain `GameSimulation(int seed = 1)`.
  Systems wires the source production runtime to the injected library; existing
  asset-free kernel tests are not evidence for that source path.
- **Identity:** `SourceAnimationLayer.ScriptSymbol` is the exact active script
  (e.g. `qb_stance_anim`), distinct from `Sample.SequenceName` (`QBSTANCE`).
  `InstanceId` changes at script replacement/restart, not each render or ordinary
  loop. Role/action enums are descriptive tags, **not** original mode/opcode IDs.
  `SelectionEvidence` distinguishes approximate selection from source-verified
  selection independently of content-pinned clip authenticity.
- **Samples:** `SourceFrame`/`NextSourceFrame` are original WAN frame indices
  present in the producer's selected-frame mapping, not output-array offsets.
  Map the source VM's selected-sequence `iframe` through that mapping first.
  `Interpolation01` is explicit `0..1`; a held frame uses equal indices/zero.
  Never interpolate a skipped authoring frame, infer a wrap, choose a next clip
  or advance time from rendering. Port `_stream_frame/_interpolate_frame` math
  for authoritative source interpolation; any extra render smoothing is visual
  only and must not affect contacts, catches or root movement.
- **Transitions:** `TransitionFromPose`, when non-null, contains fifteen source
  local XYZW unit rotations captured at the actual transition tick, including
  interrupted blends/turn hacks. `SourceJointPose` copies its collection.
  Blend to the explicit sample using `Transition01`; null means use the sample
  directly. Capture from source state, never the last camera-rendered pose.
- **Two-part transforms:** `Primary` is the lower/full branch; optional `Secondary`
  is the upper branch. `JointMask` marks **final matrix outputs**, not a simple
  replacement of local rotations on one skeleton. `LowerBodyJoints == 0x7e01`
  (0,9..14), `UpperBodyJoints == 0x01fe` (1..8), `AllJoints == 0x7fff`.
  In two-part mode evaluate the secondary's own hidden pelvis rotation and
  heading before upper descendants, then the primary pelvis/legs, as
  `player.c:last_player_proc` L6659–6674 and `models/skeleton.h` do.
  A layer's nonzero `FacingDirection` overrides the actor heading; zero inherits.
  This preserves source `fwd2` separately from `fwd`.
- **Position/heading:** `PlayerView.Position` is the simulation's object X/Z in
  existing yard coordinates; `Height` is object Y in yards, **excluding** pelvis
  root height. `FacingDirection` is a unit field X/Z vector; `(1,0)` points +X,
  `(0,1)` +Z; zero is unspecified. Do not replace source facing with velocity
  (stationary throws/backpedals differ). `ModelScale` is the positive roster
  `ascale` applied once to model geometry/pivots, default 1 for legacy calls.
  Core catch/contact joint positions must come from pure source pose/pivot math
  using the shared library, including offscreen actors; Unity transforms and
  camera-space coordinates must never feed back into gameplay.
- **Root:** `Animation.RootOffsetSourceUnits` is the **resolved local rig offset**
  after source anchoring/interpolation, in archive runtime units and already
  including actor scale. Normally it is `(0, ay, 0)`: add it through the proven
  coordinate conversion and divide by **7.8** once. WAN root authoring translation
  is multiplied by **0.12898** offline, not again at render time.
  The simulation consumes source X/Z root deltas, rotates them by source facing
  and publishes the resulting world `Position`; the view must **not** apply
  those deltas again. `MODE_ANCHORXZ` suppresses/clears at the next frame change,
  not for an entire clip; `MODE_ANCHORY` adjusts object Y/clamps it as source
  requires. `MODE_SECONDARY` never drives world motion. The loader's scaled
  frame root is data for the core's resolver, not an independent render mover.
- **Interpolation/failure:** render interpolation only blends compatible
  previous/current snapshots; discontinuities/restart/source-instance changes
  use the explicit transition/new state. Do not extrapolate while focus-paused.
  Source mode requires valid clip/frame/limb identity and finite normalized
  poses for every actor; missing data/unspecified animation must visibly fail
  acceptance with actor/sequence/frame diagnostics, never silently freeze the
  QB pose or substitute a generic Unity walk. `?actors=graybox` stays explicit.

`BallView.Kind` is a typed semantic kind, not the numeric `LB_*` identifier;
`Velocity`/`VerticalVelocity` are yards/second and `LastTouchPlayerId == -1`
means unknown. These additive fields default to unspecified/zero/unknown in old
calls. They do not themselves implement fumbles or kicks.
`GameEvent.ActorPlayerId` is the initiator and `TargetPlayerId` the intended
other actor; `-1` means unspecified. Preserve legacy `PlayerId` semantics:
**PassThrown.PlayerId currently names the receiver, not the thrower.** A new
release event fills `ActorPlayerId = quarterback`, `TargetPlayerId = receiver`,
keeps legacy receiver `PlayerId`, and records actual `SourceTick`; old events'
new fields remain `-1`, never an inferred QB. Array order is source execution
order. Source animation callbacks, not completion of a view's blend, release
the ball and cause gameplay events.

## Runtime and browser handoff

Unity Systems owns the fixed-step accumulator, not the kernel. Consume actions
once, step at 60 Hz, drain events per step, then render state; interpolation is
optional and cannot feed back into simulation. Cap accumulated frame time at
0.1 seconds (at most six catch-up steps); on tab/focus loss clear held/pending
input and accumulated time, so returning to a hidden tab does not fast-forward
the game. Hidden-tab wall-clock time is not match time.

Default bindings: WASD/arrows (W = +X, D = +Z), Shift turbo, 1/2 choose the
appropriate offensive/defensive play, Space snap/continue by phase, 1/2/3 pass
to Home IDs 4/5/6 during live offense, F tackle, Q switch defender, R restart.
Show contextual controls, possession/attack direction, scores, quarter/clock,
down/distance, selection/pre-snap/dead-ball/final state and controlled-player/
pass-target markers. The current field and HUD are functional approximations,
not original stadium/HUD presentation.

### Source-player import and runtime boundary

The permanent Python preparation tool consumes explicitly supplied pinned inputs
offline. `GeneratedPlayerReader` accepts a bounded generated-C initializer
grammar, not arbitrary C execution. `BindPoseMeshBuilder` preserves limb/pivot
mapping and packed material/UV state, explicitly converts axes/winding, and splits
seam vertices. `PlayerImportPlan` captures/hash-pins bytes from a trusted physical
root; missing/invalid data fails rather than becoming gray fallback material.

`PlayerAssetImporter.ImportAuthorizedBindPoseFromCommandLine` creates a new
ignored `Assets/Imported/<stable-id>/` only. It never overwrites an existing
import or deletes failed partial output. The local recipe uses stable ID
`local-plyr266`; its prefab resource key is
`BlitzSourcePlayers/local-plyr266/player`. See the
[exact import commands](build.md#prepare-and-import-a-local-source-player).

`SourcePlayerProvider` chooses the mode once at presentation initialization:
`?actors=source` requires a valid prefab and errors without one;
`?actors=graybox` deliberately selects primitives. With no parameter an installed
source prefab takes precedence, otherwise the baseline runs. All fourteen source
actors reuse one Chicago home / `PC_WHITE` normal-body variant. Source geometry
and pivots stay in archive units; only the presentation root converts by 1/7.8
to gameplay yards. Source actors are not driven by the graybox sine-leg motion.

`SourcePlayerPoseReader` validates one content-pinned decoded pose.
`ApplyAuthorizedStaticPoseFromCommandLine` is a narrow update of the matching
generated prefab: preserve the debug bind prefab, protect geometry/material/
texture provenance, apply parent-composed source rotations and anchored root
offset, then verify the saved data. `VerifyAuthorizedStaticPoseFromCommandLine`
is the corresponding read/verification entry point. Actual native apply and
independent saved reload passed against original matrices at all **343 split
vertices**, within **2.79×10⁻⁶ source units**, followed by a matching standing
WebGL/browser result. The [frame-0 apply/verify step](build.md#apply-qbstance-frame-0)
is required to reproduce that preview; the preserved identity bind is debug-only,
not acceptable game posture. This is one authentic stationary QB ball-holding
frame applied to all fourteen actors, not a continuous animation controller or
verified universal original pre-snap selection.

### Camera and simulation isolation

`PlayCamera.Render(IGameState, bool)` reads detached snapshots only.
`NormalCameraModel` ports normal tracking/projection in yard space; no camera
time, Unity physics or imported-pose transform writes gameplay state. Real-time
follow smoothing adapts the archive 57 Hz response while simulation stays 60 Hz.
QB-mode inference, current-receiver pass span, remembered dead-ball orientation,
HUD/aspect-aware fitting and focus/rewind recovery are explicit adaptations,
not identical original engine timing. See
[source camera evidence and limits](source-parity.md#source-derived-normal-camera).

Frontend owns only the outer page/host/build tooling; the in-player HUD is Unity
Systems' responsibility. Unity executes client-side WebGL; Docker builds/serves
static artifacts, not a server-side match simulation. No React or backend.
Entitlement, activation and any license injection remain external secrets, never
source/cache content or committed files.

## Verification contracts

Quality alone owns `tests/Blitz.Core.Tests/Blitz.Core.Tests.csproj`, targets the
installed .NET SDK, and links `../../Assets/Scripts/Gameplay/**/*.cs` plus
`../../Assets/Scripts/Contracts/**/*.cs` directly. Do not duplicate the kernel,
reference Unity assemblies, or ask Unity to generate/write a project file.
The inventory/hash-guarded runner is:

```sh
python3 tests/run-core.py
```

It passed all 156 required cases with zero skipped on October 15, 2026. See
[the test evidence and coverage](../tests/README.md). The conventional command is:

```sh
dotnet test tests/Blitz.Core.Tests/Blitz.Core.Tests.csproj --configuration Release
```

Required assertions: fourteen stable IDs; all phase transitions/invalid commands;
normalized diagonal input; five-second auto-snap; first down exactly at thirty
yards and goal-to-go; failed fourth-down and fourth-down incompletion both ways;
TD/safety points and receiving-team restart spots; no double score; quarter expiry
finishes a live play and reaches final/tie/restart; pass/catch/incomplete; defensive
control/tackle; finite turbo; retained snapshots; same-seed command replay compares
state **and events each tick**. Test internal pure rule helpers when necessary;
do not add public state mutation or debug-win inputs just to make tests pass.

The full integrated smoke must demonstrate selection → snap → a pass/catch or run →
dead ball → another play, a real possession change with Home defensive controls,
score/restart and final-match restart. The [browser harness](../tests/browser/README.md)
records a passed **standing-source visual-controls subset**, not this whole contract.
Current perspective focus-release geometry, complete defense/scoring coverage,
four-quarter final/restart and representative hardware performance remain
unqualified. Prior graybox observations do not certify the newer posed snapshot.

Permanent `tests/assets/run.py` now preserves **2,751 assertions**: the original
**419** parser/mesh checks plus **2,332** original synthetic pose checks. Eight
source mutations are rejected and seven actual pure-production inputs, including
`SourcePlayerPose.cs`, are hash-guarded. Clean-source and missing/zero/stale-count
gates pass without ignored helpers or original pose data. Native Editor/runtime
adapters and the actual 343-vertex source comparison remain separate evidence.
`tests/camera/run.py` retains **3,568 assertions / six rejected mutations**.
Metadata's **12 guard tests** and current **73-entry** audit require unique
32-hex GUIDs, final LF and resolving script references; run the live inventory
only on an owner-stable snapshot.

Two historical bind host generations establish repeat-build proof; the later
standing host generation passed matching Docker/browser checks. The subsequent
identity/legend-only host build, matching Docker promotion and fresh
desktop/portrait controls captures are verified. Title/legend appearance was
reviewed manually; the exact legend OCR diagnostic remains explicitly
unqualified. Freshness is current
BuildReport/run-ID/hash proof, **not payload mtime**; caches can be reused.
Browser acceptance belongs to the
exact served receipt/proof/payload hashes, not whichever generation is newest
on disk. Docker-editor acquisition remains unqualified and is distinct from
the verified host-build/static-runtime path. Exact commands and evidence
boundaries are in [build.md](build.md).
