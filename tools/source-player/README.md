# Portable C# browser preview (not a Unity player build)

## Original pregame, records and live global-mixer successor

`original-teams-complete-original303-prematch` uses963 authenticated inputs,
manifest `309ca9cf4f7f0d3b9f8b43427ab581fda4257d072ea00bfdb4ccc0003c2cba4f`,
and `SourceC2Composition.CreateCompleteOriginalPrematchSimulation`. It keeps
the final303-clip/508-cue/36-camera family and adds the six original career,
record-found/missing and custom-load plates:303 actual cabinet images total.
Earlier packages and their default routes remain separately pinned.

The actual four-slot input FIFO drives a60→57Hz pregame process service.
Unjoined menus make no original entry/bank claim. After trusted HTML startup,
an unlocked but **suspended** AudioContext, empty bank map and zero reserved
mask are intentional while Joining; require actual resumed playback only
after the physical cabinet start activates the source entry.
An accepted cabinet start
activates the authenticated permanent-bank boundary; the original pre_game,
option controller, welcome child, station cursors and CAP helpers produce
their own canonical sound/control outputs. Storage completion only queues
data for the appropriate source wake. The renderer backdrop supplies neither
source sound outputs nor the shared RNG. VS/rematch carries the same actual
random owner, full observed clock and typed current-cursor token.

The49 banks now contain1081 executed programmes. SAUDv3 adds eight finite
original2.33 implicit-global mixer programmes, including all reachable
menu20/28/80 and welcome/CAP introductions. The pinned CPU VM executes both
primary0 and rebased-primary1 contexts; their PCM is **not interchangeable**.
Original13/14 control the global0 destination matrix, not an invented timer
ID or the SFX's own gain. Live six-column envelopes retain DCS240-sample
steps, signed/clamped arithmetic, fixed1.15 transfer, final snaps,
replacement/00 resets and concurrent contributions. Native16-bit overflow
is rejected. The independent firmware/ISA reference establishes those
operators and their finite stops; it does not certify the portable allocator,
outer gain/pan, master headroom or whole-game DSP equivalence.

Immutable menu and actual selected VS PCM resources are primed before their
ready boundary. A once-only transport clock-start anchors the first real
source wake/output after context readiness; later underruns, replay,
reordering and stale plays still fail. Neither source time nor RNG is
advanced by the receiver. This profile requires its real record menu driver;
use the frozen final profile for a deliberate record-disabled AV probe.

Gameplay elapsed follows measured active host time, not render-ahead
`AudioContext.currentTime` jumps. Render time, output timestamps and latency
are separate read-only diagnostics. The active audio epoch, sample origin and
segment scheduling lead are not retimed to cover missed deadlines.
The runtime retains measured elapsed debt across normal callbacks while
keeping the existing0.1-second/six-step bridge budget per callback. While a
playing prematch-profile host retains a whole host step, presentation
is deferred until a later normal callback catches up; source steps and ordered
audio commands are still delivered on every callback. Paused/menu frames render
normally. No extra physics callback, source wake or audio pause is invented.
Complete four-slot physical changes carry observed active-host timestamps; only due
FIFO entries are delivered, so new input is not applied retroactively to
historical debt. Explicit pause/reset/VS boundaries clear transients and
exclude suspended time. An exhausted active-segment audio deadline still fails;
render deferral does not accept a late command, enlarge the scheduling lead,
reanchor an active clock or clamp stale plays.
`sourcePlayer.observe()` provides a defensive, lightweight read-only state
copy without the immutable source/resource inventories or camera history.
The full `snapshot()` contract remains available for explicitly paused proof.
Before acknowledging pause, one bounded callback drains already-observed active
time. Unprocessed fractions survive input clearing. Resume requires two genuine
complete physical observations and a zero-wake ready boundary before bounded
catch-up; no synthetic neutral frame or late-audio replay is supplied.

Original joint shadows use authored alpha, source raster winding and an
explicit normalized16-bit depth-bias proxy. Actual material/identity GPU
differentials are distinguished from native floating-WBUFFER pixels.

Focused checks for this separately prepared generation:

```sh
bash tools/source-player/dotnet.sh run --project tools/source-player/SourcePlayer.Checks.csproj -- \
  "$PREMATCH_DATA" --prematch-inputs-only
bash tools/source-player/dotnet.sh run --project tools/source-player/SourcePlayer.Checks.csproj -- \
  --native-mixer-reference-only "$ORIGINAL_ISA_MIXER_CASES"
python3 tools/source-player/build.py --profile original-teams-complete-original303-prematch \
  --data "$PREMATCH_DATA"
```

The first is explicitly a resource/source-process component fixture, not a
natural match. The second consumes independently executed firmware values,
not HLE-generated expectations. Browser acceptance uses trusted public
controls, normal sandbox/autoplay policy and actual source clocks.

### November 10: actual participating two/four-station cabinets

The explicit runtime choices below use the **same authenticated data**, not a
relabeled manifest or different simulation. Omission retains every historical
default, including a package configured with only one human.

| Runtime query | Actual participating humans |
| --- | --- |
| `?source-cabinet=two-station&source-stations=0,1` | P1/Home and P2/Away |
| `?source-cabinet=four-station&source-stations=0,1,2,3` | P1/P2 Home cooperating; P3/P4 Away cooperating |
| `?source-cabinet=four-station&source-stations=0,1` | Two Home teammates against CPU Away |

These choices require the explicit Portable pre-snap-switch services.
For these legacy runtime overrides, `source-stations` requires `source-cabinet`; duplicate/unknown IDs, malformed
options and incomplete TwoStation entries reject. Original TwoStation requires
**both** physical controllers: absent `plyr_control[0/1]` is still undefined.
Non-neutral P3/P4 input in TwoStation rejects in both browser capture APIs.
For the sealed legacy complete303/306 profiles, TwoStation admits the real **Guest** record
choices (or explicit known-new inventory); named/saved TwoStation inventories
remain guarded, not silently converted to guests. The separately authenticated
named TwoStation profile below does not broaden these legacy entries.

The actual session retains its layout through team/roster/versus choices and
restart. First/Second selects the physical teammate captain, not an actor ID.
In versus, select Second on the cooperative-captain row to exercise P2/P4
captains. For the Home-only co-op scenario, select Away on the initial
first-possession setting: this value drives the original opening **kicking**
side, so Home receives. Do not assign Home humans to a CPU quarterback.

Use the unchanged primary1114 selected-world dataset, manifest
`aeb2efdebdbe260f63f6d785c461048dc9cec337098490761c2815cd8ffc4edd`,
and its explicit `original-teams-complete-original303-selected-world-v2`
profile. The public start/Guest/team/roster/versus route is still required.
`sourcePlayer.observe().state.cabinetSession` exposes the actual participation
mask/layout; each `state.actors` entry exposes `controller` and readonly
`input` (`Held`, `Pressed`, `Released`, `StickOrdinal`, `NonTurboTicks`,
`NonATicks`, `NonBTicks`). These are actual post-player state, not setters or
an inference from four physical input slots. A suspended `player_proc` retains
its input counters even while the independent physical scanner advances.

Focused production-caller regression:

```sh
bash tools/source-player/dotnet.sh run --project tools/source-player/SourcePlayer.Checks.csproj \
  -c Release -- "$SELECTED1114_DATA" --cabinet-participation-only
node --test tools/source-player/input.test.mjs tests/interface/interop.test.mjs
```

The managed cases use only public raw capture, original menu choices and actual
host/source wakes: two opponents, all four humans, and same-side co-op.
They inspect real actor ownership, simultaneous ABC press/hold/release and
all three double-tap counters, suspended-player timing, defensive switching,
the quarterback-carrier receiver-switch guard, stick/B receiver selection,
four independent live49 diagonals, per-station turbo, observed pause/resume,
and restart. Snap intent starts the center animation; the actual release is
the later `hike_ball` callback, not an immediate possession transfer.
The Unity WebGL plugin now honors standard D-pad diagonals as the portable
browser adapter does. Focus reactivation samples still-held standard gamepads;
it cannot manufacture neutral to bypass the real release latch.

This is bounded production/Core and JavaScript-adapter evidence, **not** fresh
browser, Unity-native or full-I1 acceptance. New browser/AOT validation requires
the coordinator's complete source freeze. Preserve the accepted November 9
archives and their identities; these changes require a fresh November 10 build.
The new readonly input view remains covered by the existing whole-Core and
Browser AOT roots; serialization/JSON roots and all caller-drain limits remain.

#### Exact I1 browser bootstrap and public controls

The accepted **managed** fixture is Quality's `i1-snapshot-03`, not the old e44
application. Its independent receipt is
`/home/long2know/.cache/nflblitz-quality-20261110-1440/independent-I1-20261110-pass02.json`,
SHA256 `78f239a6b9928315157ab3ac04559a63567d85f909fb768a4b6e67afbb269e95`.
It passes21,945 assertions with15 exact owner pins and216 snapshot files.
The author's168-input manifest is
`68314c83b27f2fe8561e4009346aa771ff3152815e8f76b91a51f39cbc2ec290`;
the exact pins and case results are in the new
`participatingCabinet20261110.coherentMicrofreezeFinal` object of the existing
ignored `.squad/.scratch/frontend-delivery-20261109/delivery-receipt.json`.

Read-only prepared data:
`/home/long2know/.cache/nflblitz-quality-20261110-1440/data1114`.
The recoverable immutable authority is `drain.tgz`, SHA256
`99d5477db1aa76d4dd11ee3e82bb56d1f69b66d7eb4bf78fc4465b7d11321ec4`,
under `.squad/.scratch/frontend-delivery-20261109/`; its data prefix is
`bounded-caller-drain1114/AppBundle/data/`. Use that archive **for unchanged
data**, not as a browser binary containing the new cabinet options.
Do not modify/relabel e44, its archive, the failed33e archive, or the image.

The exact data contract is `blitz-portable-source-player/v2`,
`source-complete-original-prematch-selected-world/v2`,
factory `SourceC2Composition.CreateCompleteOriginalPrematchSimulation`,
source revision `89f840d58d152a2a9f230cb8d0cdffbd554c8ded`, converter
`1792293aaaea00a919b59e8c05716978e85b4333`. Keep the manifest/profile above.
Do **not** edit the sealed settings to change station count:

- `settings-presnap-switch.json`, SHA256
  `dd067923f88c23c0d6a7c3a7c0b71d04bd6d90f9b758db147c286ae5e281f37e`:
  declared FourStation, default P1/Home only, `initial_player_id=-1`,
  source profile null, Home team0/Away team1, roster indices0..6,
  First captain, first-possession0 (opening kicking side), difficulty5,
  time adjustment2, continuation true, native gamepads enabled.
  The explicit URL creates a **new immutable runtime configuration** for
  TwoStation or four participating humans; it does not change those bytes.
- `bootstrap.json`, SHA256
  `0a51c052bf04f24dc975d19b2946c492e7e202adccb1f78992a2d033eb8cb9c4`:
  field0, source raster512×384, both turbo masks0, speed/violence powerups false,
  run-out-of-bounds mask0. World is stadium0/CAR, designated Home team0.
- Normal57/`USE_OLD_QB=false`, Portable compatibility; complete-profile
  gameflow/records/instructions/world/audio enabled. The exact I1 queries above
  leave fire disabled and kick meters Disabled. Do not add opt-ins and call
  that the identical fixture. Source clock57, bridge6 steps/100ms, audio lead
  and RNG/epoch policies are unchanged.

Keyboard bindings are physical station bindings, not generic gameplay actions:

| Station | Up / Down / Left / Right | Source A / B / C |
| --- | --- | --- |
| P1 | W / S / A / D | V / B / N |
| P2 | arrows | Delete / End / PageDown |
| P3 | I / K / J / L | U / O / P |
| P4 | T / G / F / H | Z / X / C |

A standard gamepad at the same physical index uses axes0/1 and D-pad12..15;
buttons0/1/5 are A/B/C. Unknown mappings reject. In TwoStation, P1/P2 are
**opponents**; P3/P4 do not exist. In FourStation, P1/P2 and P3/P4 are teammates.

Supported public sequence, using a fresh **isolated** browser/storage profile
without deleting any existing user's records:

1. Select the corresponding exact URL in the table above. Click the real
   **START ORIGINAL GAME WITH SOUND** control, then focus the canvas through a
   trusted pointer action. Wait for `state.cabinetSession.stage == "Joining"`;
   do not wait for global `ready` while the unadvanced menu backdrop is source0.
   The suspended AudioContext before the cabinet entry starts is intentional.
   These URLs already join the named participants: check masks3/15/3.
2. Press/release P1 **C** (`N`) to start the cabinet. At the original record
   options screen, each participating station presses/releases **Right**, then
   **A**, to choose **Guest**. Wait for `records.stations` to show Guest for
   every participating ID and `cabinetSession.stage == "Teams"`.
   Do not substitute saved/named TwoStation entries or infer missing choices.
3. Keep the approved default teams/rosters. Initial **First** team captains
   confirm **A** at Teams and again at Rosters: P1/P2 for TwoStation;
   P1/P3 for all-four FourStation; P1 for Home-only co-op (Away is CPU).
   Wait for `"Versus"` after the second confirmation.
4. For Home-only co-op, P1 presses/releases **Right** on the initial
   first-possession row, selecting **Away as opening kicker**, so Home receives.
   For all three cases, P1 then presses/releases **Down** once and **Right**
   once to change cooperative captain First→Second; P1 **A** starts the match.
   Verify `"Playing"`, `matchesStarted == 1`, the correct actual station mask,
   and a fresh source0 match. TwoStation still has one captain per side.
5. Advance instruction cards using actual joined controls after their original
   waits. When the live play-selection cursor is available
   (`instructions.introduction.Active == false`, `state.phase == "PlaySelect"`),
   the **current** captains use fresh **B** presses: P1/P2 for TwoStation,
   P2/P4 for four humans after Second, P2 for Home-only co-op. An early press
   can dismiss an instruction rather than choose a play: wait and press/release
   again. Require actual selection and `menu.timer.ForceSelection != true`;
   timer expiration must not conceal input to the wrong teammate.
6. After the original no-hike interval, the controlled quarterback's station
   presses/releases **A** (or B) to request the hike. The center still owns the
   ball until its real `hike_ball` callback; do not demand immediate LiveBall
   or quarterback possession on the input frame. In this fixed case the active
   quarterback is Two P2/actor10, Four P4/actor10, co-op P2/actor3.
   Use raw direction/ABC combinations thereafter: QB stick/B selects a receiver;
   defender B switches under source eligibility; receiver B does **not** steal
   control while a teammate quarterback has the ball. C and A/B/C double taps
   retain their source-mode/tick meanings, not invented keyboard actions.
7. Escape pauses/resumes; R invokes public restart; Enter is public continuation.
   Test real browser blur/visibility transitions, never override focus APIs.
   Held gamepads cannot manufacture neutral; physically release/center them.
   Keyboard autorepeat after a lifecycle clear is now ignored until a real fresh
   press: tracked held repeats and release/new presses remain supported.
   Require two actual complete neutral observations and normal caller servicing;
   do not inject source ticks, actors, scores, RNG, or synthetic focus.

The follow-up found one **genuine** frontend I1 omission: an untracked keyboard
autorepeat could re-arm all four stations after pause/focus/restart. It was
reproduced RED and fixed in `input.js`; all17 input/interop tests pass, including
a real-source guard-removal mutation, with34 combined input/renderer passes
and two unrelated explicit managed-packet skips. This changes only the
browser JavaScript; the accepted managed21,945 snapshot is not relabeled.
The next browser must include input.js SHA256
`bf3dea069b50116215583ee0369c819baa9c1b0517ae4fe67384e992651fcd71`.

Remaining distinctions: true Chrome/gamepad/physical-focus execution and the
positive live receiver/catch-control branches still need their own verification;
their implementation is not declared missing simply because this bounded
suite does not close them. Named/saved TwoStation inventories remain guarded
in that sealed legacy profile, not a browser-only pass or evidence for the
separate new named-record composition below.
The separately traced reachable `GC_SKILL → user_base_adjust {-3,0,3}` producer
has the scoped public binding described below; difficulty5 is not that producer.
No other frontend I1 omission was demonstrated
in this bounded follow-up. Future306/course admission requires its own exact
manifest/profile and revalidation, not relabeling these1114/aeb2 results.
Systems has separately source-authenticated/qualified, but this I1 receipt has
**not** tested, `original-teams-complete-av306`:1153 inputs/300324289 bytes,
manifest `51e94caffcf5875e024206eb069dfd905ee13e134b09e7f5020ff68349461e5d`,
feature `source-complete-original-animation-course-audiovisual/v3`, factory
`SourceC2Composition.CreateCompleteAv306Simulation`. Its typed loader/factory
handoff and new-profile I1 revalidation must precede final browser use.
Its distinct `audio/complete-av306-manifest.json`, SHA256
`c695b7c04befa93124e29b9454edb865d4fc8b1e981f2cf329485d7c0cbc01ee`,
admits49 banks/1205 programmes, including124 assigned team0..3 programmes.
All1114 ancestor records remain byte-identical;31 newly qualified team bank
files coexist rather than replace them. The authored data proof is not an
application producer→PCM/accounting or released persistent-root claim.
Do not pass that manifest as the old303 profile or claim snapshot03 tested it.

The separately released persistent read-only root is now
`/home/long2know/nflblitz-unity-evidence-20261110/av1153-release-01/readonly-av306`.
Frontend independently rehashed every1153 payload and verified files400/root500
after namespace exit. Actual new-profile loader admission passes4,807 assertions:
all1153 consumed,42 historical metadata/bank inputs verified inactive,
306 clips/31051 frames, exact same seven-entry/six301-point course object in
PlayerData and impact, unchanged303 inventory, and strict old/new cross-rejection.
The GC binding32 also passes on this exact new dataset. This does **not** close
Backend's accounting/rounding or Systems' actual producer→PCM gate.

The browser/build admission is likewise distinct: new v3/51e94 data requires
`aot-rooted-bounded-caller-drain-av306/v1`, not the old1114 caller ID.
Python gates pass4 focused cases; actual production `loadData` with the genuine
new and old manifest bytes passes strict caller-pair admission/rejection and
a real-source omitted-admission control before asset reads. Existing input/
interop plus this loader gate passes18/18; combined renderer checks35 pass,
two explicitly separate managed-packet fixtures skip.
After **all owners explicitly freeze source checksums**, the new profile's
build invocation is:

```sh
python3 -B tools/source-player/build.py --profile original-teams-complete-av306 \
  --data "$READONLY_AV1153_DATA" --aot --caller-drain --no-restore
```

No such AOT/full campaign has been run for this handoff. Cross-generation
`--reuse-aot-bundle` rejects for AV306; old e44/1114 artifacts and their caller
ID remain immutable and are not new-profile evidence.

#### New named/saved TwoStation composition: separate November 10 epoch

The explicit profile `original-teams-complete-av306-two-station-records-v1`
uses `SourceC2Composition.CreateCompleteAv306TwoStationRecordsSimulation` and
feature `source-complete-original-two-station-records-audiovisual/v1`.
It is **not** an alias for the old AV306 factory or FourStation settings:

- Read-only data:
  `/home/long2know/nflblitz-unity-evidence-20261110/two-station-records-20261110-01/readonly-data1154`.
- Manifest SHA256:
  `745129381c326e9f237ac4e6c3e020503553aada3e1f993dfb9321c09ee8f32d`;
  1154 inputs/300324845 bytes, with all1153 AV306 ancestor pins unchanged.
- Only added payload: `settings-av306-two-station-records-v1.json`,556 bytes,
  SHA256 `6ad8eb0239d2b0432d55dcf47e2fd893a38397e036befa682ffb9b0ed89c4fc2`.
  This actually configures Original57/TwoStation, physical0/Home+1/Away,
  both actor-1/default source profiles. Old FourStation settings remain
  byte-identical and inactive;43 retained inputs are verified.
- Default URL already admits both humans and genuine record/pregame services.
  An explicit `source-cabinet=two-station&source-stations=0,1` agrees; a
  FourStation override, incomplete participation, strict compatibility or
  disabled records rejects before creating a match.

The new factory stores its typed capability at construction. Only its new
two-argument `BindSourceTwoStationSavedRecords(records, playPages)` may bind the
actual `Records.BindMatch(configuration)` result. The inventory, configuration,
selected immutable records, saved-play origins and final record barrier retain
the same objects. The old binder and old factory cannot acquire this admission.

Only this profile opts into portable record version3 and explicit logical
`customBlockState`:0 LegacyUnknown,1 NeverAllocated,2 Allocated. These values
are **not native disk-bank assignments, CRCs or a native-disk execution proof**.
Actual successful Create establishes NeverAllocated; actual CAP-slot **B exit
without editing still awaits a durable custom-block write**. Allocated-empty
survives fresh reload and keeps the original custom page, although a manual
missing-slot B chooses nothing; only the real expired selector/game_proc draws
the original first-page `randrng(9)` fallback. Saving a custom play acquires the
block; deleting the last slot does not erase its history.

An opt-in legacy record with a validated existing play has a defined positive
migration; an all-null legacy book remains Unknown regardless of GP0 or opaque
DiskBank. Loading never writes. A selected Unknown record cannot start:
`SOURCE_CUSTOM_BLOCK_HISTORY_UNAVAILABLE` requires an actual successful CAP
exit or deliberate Guest choice, never an automatic empty inventory. Ordinary
legacy profiles retain their version2 writer/Exists-only inventory unchanged.

Public sequence: **C → ENTER NAME/A**, choose YES at each physical station,
enter each six-character name/four-character PIN, and explicitly Create missing
records. From a selected station use **A record → A CAP slots**; **B** exits
CAP and must finish its actual storage-resume wake, then **B** returns to the
station choices. **C** continues; both P1/Home and P2/Away confirm Teams and
Rosters, and P1 starts from Versus. In play selection P2's original C/B controls
its own Away custom book, not a FourStation Home teammate's inventory.

The source support is explicit: pinned `select.c` blob
`26c3955a4081aff73d6e4f1f7633ae86f24e2c4a` always creates P1/P2 name cursors;
only P3/P4 are behind `four_plr_ver`. Its custom-book load has no layout
exclusion. Pinned `cap.c` blob `42605264092a30f861f5390535bbe217aac13b66`
unconditionally writes the selected record's block on CAP exit; a successfully
read unused slot counts toward the positive read result. This is source-trace
authority, not an executed native-disk receipt.

Only the new sealed profile also produces the original team-screen actors:
Home actor3 uses `qb_ready_f_anim`, Away actor10 uses `qb_ready_anim`, both
selected-team static roster3 with original498/held-ball models. Their real
pregame source-process clock advances animation once per wake, including the
first visible frame1; a zero-elapsed render does not advance either VM or the
game backdrop. Hover changes actual team/uniform data without restarting VM
phase or rescaling the native draw.

The existing Present status carries `teamSelectionPlayers` with the actual
epoch/source wake, two complete fifteen-joint world matrices, native camera and
model/appearance bindings. The production renderer draws them after the real
opaque HUD background and before its first text primitive; leaving Teams
clears them. The real C# full-Present receipt is consumed unchanged in the Node
GL-call regression, including its actual HUD arrays and source definitions.
Real draw-caller omission fails even with intact metadata. This is not a GPU
pixel/browser or native-ABI proof.

Focused public native check (no AOT/browser campaign):

```sh
dotnet run --project tools/source-player/SourcePlayer.Checks.csproj \
  -c Release -- "$READONLY_TWO_STATION_RECORDS_DATA" --two-station-records-only
```

The distinct browser caller ID is
`aot-rooted-bounded-caller-drain-av306-two-station-records/v1`.
Build/input admission currently passes7 Python and18 Node checks, including
three genuine manifest/caller generations and a production-caller omission
control. The combined input/interop/renderer checks pass38, with two separately
declared world-packet tests skipped; the actual team-screen packet case runs.
Cross-generation AOT reuse rejects. Only after global source freeze:

```sh
python3 -B tools/source-player/build.py \
  --profile original-teams-complete-av306-two-station-records-v1 \
  --data "$READONLY_TWO_STATION_RECORDS_DATA" --aot --caller-drain --no-restore
```

No new heavyweight AOT, browser campaign, native disk/ABI execution or completed
named-career match is claimed by this loader handoff. Public native feature
qualification and team-selection ready-animation producer receipts are separate
from the old Guest21,945 and old animation receipts.

#### Scoped public GC_SKILL binding

`source-gc-skill=0|1|2` is the original **user configuration index**, not
operator Difficulty. `lib/select/game_cfg.c:apply_user_config(GC_SKILL)` maps
those indices to `user_base_adjust = {-3,0,3}`; `clear_user_config` resets0.
Omission/fragment leaves index1/adjustment0. Empty, duplicate or out-of-range
values reject; no guessed index, `Difficulty-5`, or lower clamp is supplied.

The actual typed path is:

```text
Browser URL → PreviewOptions.GcSkillIndex/UserBaseAdjustment/GcSkillSpecified
→ PlayerHost existing Options (initial backdrop / CreateSessionGame / restart)
→ KernelBinding.Create(..., handicapSettings: optionalExistingSettings)
→ ResolveHandicapSettings → every actual SourceC2Composition factory
→ GameSimulation SourceHandicapSettings → UpdateSourceHandicaps, before wake1
```

Systems explicitly released the small `KernelBinding.cs` change to Frontend.
No `PlayerHost` or `GameSimulation.Source.cs` edit was needed. An explicit public
choice replaces only `UserBaseAdjustment`; `HumanWinStreak`, `RandomDrones`,
all three powerup masks, `NoAssist` and `SuperDrones` remain unchanged.
Without that public choice, an existing typed settings object is preserved.
The source `diff_settings[base-3]` undefined branch still fails when the computed
basis is below3; a valid GC index is not permission to repair arbitrary source
roster/configuration combinations.

```sh
bash tools/source-player/dotnet.sh run --project tools/source-player/SourcePlayer.Checks.csproj \
  -c Release -- "$SELECTED1114_DATA" --gc-skill-binding-only
```

The focused production regression passes32 assertions using actual imported
team0 `DroneBase=6`, both human sides and public typed match configurations.
Indices0/1/2 produce actual constructor drone handicaps3/6/9 at source0;
operator Difficulty0/10 does not become user skill, and computed base2 still
rejects. A real compiled control removing the actual prematch factory's
`handicapSettings` forwarding fails that constructor-state assertion; final
production32 passes again with0 warnings/errors and unchanged scoped sources.
This proves the public configuration binding, not complete F1 UI/accounting,
native cabinet input, or all handicap gameplay outcomes.
Independent Quality snapshot05 confirms the same32 plus six separately authored
actual public configuration→primary constructor cases, clean compile, DLL/MVID/
source-closure binding, one exact missing-forwarding source control rejected,
and unchanged final six cases. Receipt SHA256
`df81ef861d90e89f0c2e2f138772ed915ac6f2cbcb9442cc9491fb78c917b30b`
is `gc-skill-snapshot-05/.squad/.scratch/quality-own-GC-skill-20261110-01/qualified-result.json`
under Quality's dated cache root. Runtime execution of all nine factory arms,
original screen fidelity and whole F1 acceptance are not inferred.

### Offline rebuild with an existing WASM SDK and prepared private data

Freeze production and browser sources before the acceptance build. Point the
wrapper at an existing .NET 10.0.100 SDK that already has `wasm-tools`; the default
host SDK may not have that workload. This does not install or update any SDK:

```sh
export SOURCE_PLAYER_DOTNET=/absolute/path/to/existing-sdk/dotnet
export DOTNET_ROOT="$(dirname "$SOURCE_PLAYER_DOTNET")"
export NUGET_PACKAGES="$PWD/tools/source-player/.runtime/nuget"
bash tools/source-player/dotnet.sh workload list
mkdir -p tools/source-player/.runtime/empty-feed
bash tools/source-player/dotnet.sh restore tools/source-player/SourcePlayer.Browser.csproj \
  --source "$PWD/tools/source-player/.runtime/empty-feed" --disable-parallel -p:NuGetAudit=false
python3 -B tools/source-player/build.py --profile original-teams-complete-original303-prematch \
  --data "$PREMATCH_DATA" --no-restore
```

`PREMATCH_DATA` must be the already prepared, authenticated963-input bundle
described above, not a stub or a regenerated historical profile. Empty-feed
restore fails if a required installed SDK pack is missing. `--no-restore` only
disables the implicit publish restore; all private-input, source-freeze and
emitted-WASM guards remain mandatory. The resulting `AppBundle/build-info.json`
records the actual compiled source inventory/digest and immutable data manifest.
This is a portable C# WASM build, not a native Unity build or registry publication.
Republishing replaces only the generated `AppBundle/data` directory, so an older
venue's unpinned files cannot remain in a successor output. The authenticated
input must not overlap that generated directory.

### Explicit complete selected-world v2 data

`original-teams-complete-original303-selected-world-v2` is a new schema/profile,
not a relabeling or mutation of the historical963-input package. Its producer
reuses that authenticated ancestor's non-world assets and replaces its world
closure with a pinned31-team runtime catalog. Initial field and designated-home
team configuration must match the catalog; later public team choices select
their authenticated group without registering replacement resources.

Two exact complete packages are admitted:

| Default selection | Inputs / payload bytes | Complete manifest SHA256 |
|---|---:|---|
| field0 / stadium0 / CAR0 | 1114 / 262649366 | `aeb2efdebdbe260f63f6d785c461048dc9cec337098490761c2815cd8ffc4edd` |
| field2 / stadium1 / EAG22 | 1110 / 262079703 | `ad66460368f7c71e61cd44146fe6fa362da44b8b1ea43958de7434b6d6df2c8f` |

```sh
python3 -B tools/source-player/prepare_complete_original.py \
  --base "$PREMATCH_DATA" --selected-world "$AUTHENTICATED_WORLD_CATALOG_ROOT" \
  --world-sha256 "$AUTHENTICATED_WORLD_CATALOG_SHA256" --output "$NEW_SELECTED_DATA"
python3 -B tools/source-player/build.py \
  --profile original-teams-complete-original303-selected-world-v2 \
  --data "$NEW_SELECTED_DATA" --no-restore
```

The primary catalog SHA256 is
`b66d491f6f1d7956de2fd9f103f1c6d227a22a48d313e29057b04defaaf6bace`;
the turf/night/Eagles catalog is
`b541301645c7afe8d0b0c1928e86b43fb6ae522ce117f6124238d00630ae5e13`.
Catalog payload pins exclude the catalog's own bytes; the complete package pins
`world/catalog.json` as well. Prematch, records, instructions, original audio and
the actual complete-prematch factory remain mandatory. Component selection and
restart checks do not prove browser pixels, natural rematch or a complete game.

### Optional rooted Mono-WASM AOT execution profile

Add `--aot` only with an existing installed AOT toolchain. The pinned SDK requires
`PublishTrimmed=true` for AOT; this explicit profile roots both entire application
assemblies and their reflection-based JSON/DataContract serialization assemblies.
JSON reflection stays enabled, including the selected-world catalog DTOs.
The default interpreted/nontrimmed build is unchanged.

`build-info.json.execution` records actual Core/Browser AOT objects, registered
module symbols, a newly linked native runtime distinct from the SDK interpreter,
and the emitted JSON-reflection runtime setting. A Release label alone is not
AOT evidence. Interpreter fallback may remain; this is not an all-methods-native
claim. Only AOT enables the bounded read-only source-step-cost diagnostics;
paused/terminal inspection must not become an active per-frame polling loop.
Neither AOT nor diagnostics changes scheduling, RNG, source clocks or audio lead.
`sourcePlayer.sourceStepCosts()` returns the exact C# JSON string; parse it only
after the existing public pause/audio-frozen barrier or an actual terminal audio
fault. The host rejects active inspection. This read neither advances a frame
nor hides a host rejection, and terminal inspection does not require player-ready.

### Separate bounded caller-drain execution profile

`--aot --caller-drain` selects **`aot-rooted-bounded-caller-drain/v1`** with the
unchanged primary1114 dataset. Without that explicit build flag/receipt, both the
interpreted and AOT-only callers remain RAF-only. `MessageChannel` tasks drain
only already-retained whole host steps through `Frame(0, sameViewport)`: each
callback retains the existing six-step/100ms bridge bound, adds no measured
elapsed time and does not poll/capture/replay physical input. `FrameObserved(0,
timestamp)` is not a drain API: it derives elapsed from the observed timestamp,
which may already have advanced through a real physical capture.

Pause, focus/visibility, administrative restart, rendered epoch changes,
failure and disposal invalidate queued task generations. No source/RNG/audio
epoch, scheduling lead, deadline or suspended-time policy is changed.

For an explicitly pinned existing rooted-AOT bundle whose C#/projects/roots
remain byte-identical, only the two caller JavaScript files may be replaced:

```sh
python3 -B tools/source-player/build.py --aot --caller-drain --no-restore \
  --profile original-teams-complete-original303-selected-world-v2 --data "$SELECTED1114_DATA" \
  --reuse-aot-bundle "$VERIFIED_AOT_APPBUNDLE" \
  --reuse-aot-build-sha256 "$VERIFIED_AOT_BUILD_INFO_SHA256"
```

This verifies and discloses reuse of the actual native/Webcil/application AOT
objects; it is not a claim that C# was compiled again. Any compiled-source
change rejects before copying. The ordinary full publish path remains available.

`sourcePlayer.callerDiagnostics()` returns a JSON string only during actual public
pause or terminal failure. Schema
`source-player-bounded-caller-drain-diagnostics/v1` retains first16, recent128
and slowest16 timestamped spans, capacity/dropped counters and task generations.
Fresh C# `Present` packet tags distinguish source/host/phase/epoch from a stale
camera or previous rendered packet. JavaScript present/draw/upload spans include
CPU and synchronous WebGL calls—not full managed `Render` or GPU completion.
Callbacks without a fresh render cannot invent authoritative live source/host
tags. These observations locate measured components; unlocalized render maxima
or inexpensive intro-wait source steps do not prove an active-football bottleneck.

### POSIX build and normal-browser isolation on non-POSIX worktrees

FUSE/NTFS storage can report free capacity while new-file creation fails or
`chmod(0700)` remains0777. Do not relax the browser's ownership/mode checks or
prune a shared Docker daemon. On Linux with existing user/subuid mappings,
`with-posix-build.sh` supplies private0700 tmpfs output directories while
keeping production sources, the existing SDK and an explicitly selected private
input root read-only. It reuses only existing dependency metadata; it installs
nothing. Outputs exist only for that namespace's lifetime: export the exact
bundle and receipts before its command exits.

```sh
export SOURCE_PLAYER_DOTNET=/absolute/path/to/existing-sdk/dotnet
export SOURCE_PLAYER_DATA_ROOT="$PREMATCH_DATA"
bash tools/source-player/with-posix-build.sh bash -c '
  set -e
  python3 -B tools/source-player/build.py \
    --profile original-teams-complete-original303-prematch --data "$SOURCE_PLAYER_DATA_ROOT" --no-restore >&2
  tar -C tools/source-player/bin/SourcePlayer.Browser/Release/net10.0/browser-wasm/AppBundle \
    -czf /proc/self/fd/3 .
' 3> "$OWNED_EXPORT_FILE"
```

`OWNED_EXPORT_FILE` must be a uniquely owned ignored output, opened before
namespace entry. The explicit descriptor exports only the bundle and survives
the read-only source view. If FUSE cannot allocate another inode, reuse only an
already-owned redundant export slot, never a fixture, original input or another
writer's evidence.

For a long campaign, `SOURCE_PLAYER_POSIX_SCRATCH` may name an **existing physical**
directory under this checkout's `.squad/.scratch/`; only its private namespace
view becomes writable tmpfs. The underlying directory and historical receipts
are not removed or changed.
Movement's original-C comparison also writes its fixed ignored artifact
directory; select `SOURCE_PLAYER_POSIX_TEST_ARTIFACTS="$PWD/tests/movement-callers/.artifacts"`
to overlay precisely that existing directory. Export both the campaign and
native comparison receipts before teardown; `export_gate.py` preserves their
unaltered bytes through descriptor3 rather than interpreting a status as acceptance.

`with-browser-runtime.sh` keeps the browser nonroot with zero effective
capabilities. Its private, byte-identical official Chrome sandbox helper retains
root ownership and4755 mode **inside the mapped namespace**, not on the host.
Sandbox/autoplay flags and strict0700 profile checks stay intact. The optional
`SOURCE_PLAYER_EVIDENCE_ROOT` binds one existing physical evidence directory to
`/e`; `SOURCE_PLAYER_BROWSER_WORKSPACE` accepts one owned empty physical project
directory when creating another FUSE inode is impossible. A caller-provided
workspace remains empty after cleanup. For a driver that creates its own absent
profile root, select `SOURCE_PLAYER_CHROME_RUNTIME_ROOT=/r/unique-run-name`.
No system temporary directory or host-root permission change is required.

Successful WASM startup or image build/load is not a full-match/audio result.
Record exact build/source/image identities alongside the actual normal-browser
campaign. Docker's data-root filesystem, AppArmor support and cgroup delegation
must also support the chosen runtime; do not disguise an API-only or
stopped-container receipt as a successfully running game container.

## Final original record/CAP/TD resource generation

The explicit successor `original-teams-complete-original303-final` freezes953
inputs with manifest
`6ad3183827cc27e5c74fd9bc140f4295266285ab9f955a190da407d825d004ce`.
Its actual factory is `SourceC2Composition.CreateCompleteOriginalFinalSimulation`.
Earlier661/692/record953 generations, approved563 and metadata-only885c remain
separate immutable inputs, not aliases.

This generation adds the exact original eleven TD camera **WORD** identities
alongside their original byte opcodes, plus fifteen closed source sound sites:
508 cues/36 explicit camera operand bindings. The original source tables,
303 clips/30777 selected frames and legacy six/fifteen SoundVM guards remain
separate and authenticated. A guessed neighboring opcode still fails.

The original presentation package contains297 images:41 gameflow rasters,
147 CAP authoring images and109 original normal name/PIN/options images.
Actual WIFF pixels/anchors are preserved; generated native atlas packing and
native pixels are not claimed. `record-solid` is only a1x1 color carrier for
actual editor line/route geometry, never replacement artwork. The49 exact
source-selected banks contain1073 qualified executed playlist programs.
Packed PCM stays immutable/lazy; the WebAudio upload cache remains bounded.
An authenticated FFFFFFFF sparse slot has a distinct ordered **no-entry**
result, not a fabricated silent sample or an unsupported-program fallback.
Assigned DCS3 op13/14 remain unqualified in this frozen historical generation;
the separately named pregame successor above closes its eight listed programmes.

### Browser record lifetime and privacy boundary

Register `records.js` as `source-player-records` before constructing
`new SourcePlayerRecordStore(new BrowserRecordStorage())`. Await `LoadAsync`;
initialize only after an **explicit NotFound** user action. Keep the same
adapter/store across rematches and resets; dispose only when the player lifetime
ends. The database is `nflblitz-original-records`, version1, store `cabinet`.
The primary/backup/recovery triple is committed by one IndexedDB transaction,
not independent key writes. Real native/managed CAS, rollback, recovery and a
premature-ack rejection are documented in the focused persistence handoff.
Those are component tests, not by themselves cabinet-feature acceptance.

The final profile defaults records ON. `source-records=disabled` deliberately
keeps that workflow unbound; `portable` requires this genuinely admitted
record/CAP family and portable gameflow. Historical profiles never open a DB
implicitly. Browser startup loads the store once before Host construction.
Actual source game_proc owns the before-final-statistics commit barrier:
pending/failure never becomes a fake saved receipt or a host clock freeze.
Saved selection/rematch constructs a fresh immutable session before wake1.

PINs, envelopes, entered identity glyph sequences and raw record-menu inputs
are excluded from diagnostic snapshots; actual on-screen rendering is not
replaced. Local import/export requires its current explicit cabinet action.
No native EEPROM/disk, arcade DSP or whole-game equivalence follows from these
portable interfaces or original-resource identities.

## Explicit complete-original303 successor

The separately named `original-teams-complete-original303` data generation is
not the old563 or metadata-only885c package. Its661 inputs are pinned by manifest
`fef5f838759a50afdacc4d94b0749d580b1ac90406624c887fc6ea01dc6cfa42`.
`SourceC2Composition.CreateCompleteOriginalSimulation` requires the actual
303-clip/30777-selected-frame union, separate callbacks/impact pins and the
unchanged authenticated default-audible subsystem. Clip admission does **not**
mean the original TD/reaction callers and programs have been ported.

This explicit feature contract bundles portable gameflow, instructions and world
presentation. Historical profiles/default flights remain unchanged. The new
browser entry requires the visible **START ORIGINAL GAME WITH SOUND** button:
a real trusted click resumes `AudioContext` before original PCM reaches C#.
`Browser.Start` rejects this new profile rather than silently promoting it.

The original-art package contains the four32x32 PSSHOULD/PSLIMB/PSTORSO/PSHAND
materials, seven source-selected coaching photos, halftime/final backgrounds and
all31 original team-win rasters. Shadows use the existing rendered-joint C5
geometry/VBO/shader; no blob/HEDSHU/TRAIL replacement is used. Original TGA v2
developer directories are bounded and authenticated, not discarded as pixels.
Coaching selects the existing committed background ordinal without another RNG
draw. Cabinet images use the existing HUD packets. Native WMS/mip/Composit
packing and the win-program sparkle RNG are not inferred from these rasters.

SAUD v2 carries executed original single-logical-channel playlist programs,
not relabeled isolated one-shots. The preparation tool runs the pinned CPU
playlist VM with exact BNK address relocation and checks the second/third
infinite loop cycles byte-for-byte. The receiver uses supplied loop ranges,
priority/reserved tracks, gain/pan, stops, bank lifetime and reset epochs.
PCM uploads are lazy; bank deletion releases unused cached PCM while retaining
already-playing buffers. The old v1 one-shot qualification/schema remains strict.
This is not arcade DSP/native equivalence. Unknown original opcodes and
unregistered Gameplay selectors still fail explicitly, never muted success.

Focused checks, using existing SDK wrapper/environment:

```sh
bash tools/source-player/dotnet.sh build tools/source-player/SourcePlayer.Checks.csproj -c Release --no-restore
bash tools/source-player/dotnet.sh tools/source-player/bin/SourcePlayer.Checks/Release/net10.0/SourcePlayer.Checks.dll \
  "$COMPLETE_ORIGINAL_DATA" --complete-original-inputs-only
node --test tools/source-player/audio.test.mjs tools/source-player/renderer.test.mjs
python3 -B -m unittest discover -s tests/audio -p test_prepare_playlists.py
```

The actual source/consumer check exercises real original PCM and shadow uploads;
its directed music call is a component fixture, not a natural-game/browser proof.
The preparation receipts list unsupported program identities exactly. Source
bank-table overflow requires actual original load-order evidence; CURSORS2 is
never relabeled as CURSORS.

## Opt-in qualified PCM browser receiver

`Browser.WebGlSink` implements the existing `ISourceAudioSink` using binary PCM
resources and ordered JSON batches. `wwwroot/audio.js` validates resource hashes,
the v1 schemas, source clock, epochs/ordinals and bank/program/voice bindings,
then schedules real mono31250Hz buffers through gain and stereo-pan nodes.
PCM16 is divided by32768; baked playlist mix is not applied twice.

The approved563 profile still starts through the unchanged exported
`Browser.Start(manifest, url)`: recording-only, with **no AudioContext required**.
There is no audio URL flag, bank fetch, implicit admission or default promotion.
The historical approved manifest contains only `kernel/sound-functions.safn`;
it still admits no decoded banks. The October30 local integration authorization
is used only by the explicit complete-original successor above; it does not
silently change this old profile. Receiver correctness is not original hearing, DSP or
whole-game acceptance.

A separately authorized C# composition may call the non-JS-exported
`Browser.StartDecodedAudio(manifest, url, SourceAudioPlaybackAdmission)`. Before
that composition starts, a public click/pointerup/keydown handler must await
`sourcePlayerAudio.unlock(event)`. The real trusted event and current browser
user activation are required; missing/closed/unresumed contexts fail explicitly.
Context operations have a bounded five-second failure deadline.
This unlock alone does **not** admit banks or change the legacy composition.
The historical route has no end-user original banks. Calls before unlock throw;
they are never buffered for later replay.

The portable clock has100ms scheduling look-ahead per epoch. Underrun fails
explicitly rather than bursting stale one-shots. C# `active` commands suspend/
resume this receiver's owned context without restarting voices; the host frame
waits for the transition and discards elapsed wall time. `reset` cancels every
old/scheduled node while retaining immutable samples. Bank deletion retains
playing buffers; old `onended` callbacks cannot clear a replacement voice.
Output/subcall ordering is epoch-local for a genuinely new C# producer;
command and voice identities still never rewind.
Unexpected browser suspension is an explicit failure, not automatic autoplay
recovery. Focus/pause and linear gain/pan are portable policies, not DSP parity.

Run the focused synthetic/fake-node unit checks with the existing runner:

```sh
npm --prefix tools/source-player test
```

The optional real-Chrome case in `audio.test.mjs` requires
`SOURCE_AUDIO_BROWSER=1` and `SOURCE_AUDIO_BROWSER_EVIDENCE` (a new owned JSON
output), plus the existing secure short POSIX `CHROME_RUNTIME_ROOT`.
`SOURCE_AUDIO_APP_BUNDLE` optionally selects an explicitly owned freshly built
AppBundle for a real default-startup check with AudioContext unavailable.
The test starts a unique loopback server above8097 and launches its own
normally sandboxed Chrome with no autoplay/trust bypass,
uses actual CDP mouse input on a public unlock button and observes synthetic
PCM through real graph analysers. It stops its owned browser/context/server.
See the requested local C6 handoff for
the exact executed build/browser evidence. No original audio is used by tests.

## Opt-in complete-match free-play cabinet

Append **`?source-gameflow=portable`** to the normal player URL (or append it
with `&` to an existing query). Omission preserves the previous unbound route.
The value is exact, must occur once, and cannot be combined with
`source-compatibility=strict`. Fire and authored kick-meter options remain
independent; this flag does not enable them or change the authenticated profile.
New-player sessions use the actual pre-snap-switch FourStation composition.

The original-atlas cabinet screens run in C#, through the same HUD/render
packets as the match. The tick-zero fourteen-actor World is an **unadvanced
menu backdrop**. Starting constructs the playable simulation with the chosen
immutable configuration before wake1; the authenticated settings/data are not
rewritten.

Use the existing station keys or corresponding standard gamepads:

| Physical station | Side | Move | A / B / C |
|---|---|---|---|
| P1 / 0 | Home | WASD | V / B / N |
| P2 / 1 | Home | arrows | Delete / End / PageDown |
| P3 / 2 | Away | IJKL | U / O / P |
| P4 / 3 | Away | TFGH | Z / X / C |

1. **Join:** A joins that station, B leaves, C proceeds with joined humans.
   The approved initial choice is explicitly P1/Home, not a loaded saved record.
2. **Teams:** each side's captain uses left/right across all31 actual teams,
   A confirms, B returns to participation. C lets a lone side edit the CPU's
   team. Confirm both human sides; an untouched CPU side starts ready.
3. **Rosters:** up/down selects one of seven actor slots; left/right selects
   one of that team's16 original records. A confirms, B goes back; C edits an
   unmanned CPU side. Genuine roster names, numbers, models and uniform
   selectors are used. Original audible restarts deliberately rebuild the
   source's play-specific roster lineup; handpicked slots are not falsely
   promised to survive that source operation.
4. **Versus:** the first joined station chooses initial possession,
   cooperative captain, source difficulty/time adjustment and free-play versus
   confirmation-required quarter continuation. A starts; B returns.
   In gameplay, the existing controls remain **B choose / A flip / C page**,
   source snap/pass/jump/turbo, stock specials, PAT and random gestures.

Only deliberate new-player games bind explicit **known-none** custom
inventories to the released source selector. Unavailable inventory is not
interpreted as empty, and a missing custom play is never replaced with a
generic/CPU play. Default audibles are available **only** when the actual
separately authenticated `source-default-audibles/v1` successor was supplied.
The approved base563 is not an alias or an enabled-audibles composition.

Committed result plates, EOQ/winner/overtime announcements, the source-selected
coaching text, halftime team statistics, continuation timedown and final
statistics are distinct screens. C1 owns all waits, the ordered coaching draws,
period/PAT rules and final confirmation. Hold A+B+C to speed result/EOQ waits
after their source minimum; press A for the other source waits. Rendering does
not poll input, advance waits, select hints or produce statistics. The existing
one-source-wake pending-result HUD hold is retained.

Final scores, seven quarter/OT columns and statistics project the actual
immutable producers. `0` is a recorded zero; `--` means unavailable or
unplayed. Actual `PlayActivityStats` supplies pass attempts, fumbles/lost fumbles
and original **human-only** tackles, independently of sacks; it does not count
all CPU stops. Old-constructor null groups remain `--`, distinct from measured
`0`. Interceptions lost use the opposite side's made count, paired with the
side's own lost fumbles, not an invented total-turnover counter. The winner
comes from scores, not the MatchEnded event value.

At final confirmation, release all buttons, then press A (or the configured
station's public Continue). The retained final offers **A rematch / B new
teams and players** after another release. A performs the existing bridge
restart/reset once. B preserves the final while returning to choices; starting
changed choices creates a fresh simulation. Inputs/neutral latch, HUD, camera,
effects, events and audio epochs are reset at that deliberate boundary, not by
redraw. Up to16 real match summaries and the chosen session settings remain in
memory; there is no persistence, fabricated original player record, credit,
coin or audit hardware. R remains administrative and is not needed for this
loop. Focus/pause and held-through transitions require a genuinely neutral
raw sample before another confirmation.
Join/leave occurs in pregame/new-match choices, not by mutating humans in a
running match. All four raw physical slots remain distinct; unjoined slots
cannot control that match. Raw A works for each configured side's final
confirmation; the existing Enter shortcut addresses P1 when P1 participates.

This is explicitly portable free play: zero-initialized wait scratch and the
continuation policies are not a native stack/ABI claim. The approved venue
remains **field0/stadium0/BEA**, even when other teams are selected. Existing
`playresult`, `sp_*`, `msg_*` and original font art present the screens; the
separate original halftime/final backgrounds, cheer photos, team-specific win
animations, disk/transit timing, native Composit packing/pixels and complete
audio remain unqualified/not admitted. No replacement asset was generated.
The bound Portable flow also reports `CarrierlessDeadBallLateHit` when original
`_mode_db` would read `player_blocks[-1]` for a new-A late hit after play-over.
Only that undefined target-dependent decision is deferred, after standard
movement; raw ABC, wait counters and independent B/C remain intact. No carrier,
replacement animation or RNG draw is invented. Strict/unbound guards remain.

Focused actual-host checks (terminal/clock/statistics fixtures are labelled
edge tests, not full-match proof):

```sh
bash tools/source-player/dotnet.sh build tools/source-player/SourcePlayer.Checks.csproj \
  -c Release --no-restore --nologo -v quiet
bash tools/source-player/dotnet.sh \
  tools/source-player/bin/SourcePlayer.Checks/Release/net10.0/SourcePlayer.Checks.dll \
  "$APPROVED563" --cabinet-session-only
```

The separate natural regression uses only public cabinet captures/Continue
through the actual host, without score, clock, seed or gameplay-state setters:

```sh
bash tools/source-player/dotnet.sh \
  tools/source-player/bin/SourcePlayer.Checks/Release/net10.0/SourcePlayer.Checks.dll \
  "$APPROVED563" --cabinet-full-match
```

## Portable source world presentation

`?source-world=portable` opts into the C# world-presentation packet. Omission
or `source-world=disabled` leaves it off. Values are exact and single-use;
StrictSource rejects the portable flight. This does not enable gameflow,
instructions, fire, kick meters, audio or another resource profile.

The actual fourteen rendered affine joint pivots and current source camera
drive `player.c`'s ten shadow links: original SUN projection onto the ground,
fixed per-end widths/extensions, PF_ONSCREEN and A/B/C acceptance,665.6 focal
factor, Single quarter-pixel snap,255 ST endpoints and descending28..10 bias.
No blob, shadow map, second pose evaluator, renderer RNG or effect clock is
introduced. The flight also places the original field2/goalpost table after
actors, inside the source fog-on/off draw interval. Legacy draw order is
unchanged when the flight is off.

**Approved563 cannot draw the player-shadow material.** Original
`player_shadow_init` needs `psshould.wms`, `pslimb.wms`, `pstorso.wms` and
`pshand.wms`; their `img3d/PSSHOULD.TGA`, `PSLIMB.TGA`, `PSTORSO.TGA` and
`PSHAND.TGA` authoring inputs are not admitted. Real articulated geometry
reaches a GPU buffer, but the explicit missing-resource packet produces
**zero shadow material draws/pixels**, never static-shadow/black-quad
substitutes. No original art is fetched or generated by this feature.

With **`?source-gameflow=portable&source-world=portable`**, the existing C3
Versus settings add **SOURCE USER FOG**: original GC_FOG values
**0 None / 1 Normal / 2 Thick / 3 Night / 4 Smog**, initially None.
Use up/down to select that row (P1 W wraps from the first row), left/right
to choose (P1 D advances), then A to start. Flag-OFF retains the original
five settings and no user-fog binding; there is no weather/debug URL.

The actual Versus confirmation commits an immutable typed choice.
`vsscreen.c:1041-1049` applies GC_FOG **then ORs saved versus power-up masks
back**. After field draw setup, `nfl.c:680-685` selects `do_user_fog`, not
`do_vs_fog`, before source wake1. C# follows that order once per fresh world
epoch, without changing authenticated settings, gameplay or RNG. Normal
uses RGB A0A0A0/end1250, Thick A0A0A0/500, Night black/700 and Smog673C00/700;
all begin at1. These need no additional texture.

Saved `pup_fog`/`pup_thickfog` and `field_setup.is_foggy` remain **unknown**.
The four active user choices nonetheless guarantee fog and the source
`is_foggy=1` write. Numeric masks stay null, except Thick's guaranteed source
override determines final `pup_fog=0`. None does **not** invent zero masks or
an `is_foggy` write: its source enable/early-return remains null, and existing
rendering stays off with that explicit unresolved status. Actual known-zero
source masks, if supplied by their owner, are distinct from unknown operands.
The menu backdrop/unbound non-cabinet route never invents a user selection.
Public rematch/restart retains the choice; changed-session confirmation makes
a new immutable choice and clears the old fog epoch.

JS only renders supplied camera-W range/color uniforms; HUD is outside fog.
`gpuProof().world.fogPixels` labels its same-geometry no-fog diagnostic
separately from the actual supplied-fog material pass and gameplay screenshot.
Continuous WebGL depth/fog,
negative polygon-offset conversion, host sqrt/trig and authoring UV origin
are not native WBUFFER, Glide fog-table, fast-trig or pixel qualification.
The source venue remains field0/stadium0/BEA for every selected team;
alternate texture/end-zone/stadium domains remain unavailable.

`ISourceWorldRenderSink.WorldPresentation(SourceWorldRenderPacket)` supplies
six `[rasterX,rasterY,reciprocalZ,GlideS,GlideT]` vertices and four
`[actor,link,textureSlot,depthBias]` selectors per accepted link, plus
`blitz-source-world/v1` metadata. Browser stages this once into the existing
Effects JSON's `world:{vertices,links,state}`; there is no new loader import.
Present's optional `world` snapshot is the identical metadata. Schema,
source identities, bounds, original resource bindings, source epochs and
same-frame delivery are validated. Pause/skipped wakes do not age effects;
rematch/fresh factory clears observations once and retains GPU resources.

Focused source math/actual Host checks (terminal reset fixtures are labelled,
not a natural match or native/GPU shadow acceptance):

```sh
bash tools/source-player/dotnet.sh build tools/source-player/SourcePlayer.Checks.csproj \
  -c Release --no-restore --nologo -v quiet
bash tools/source-player/dotnet.sh \
  tools/source-player/bin/SourcePlayer.Checks/Release/net10.0/SourcePlayer.Checks.dll \
  "$APPROVED563" --source-world-only
node --test tools/source-player/renderer.test.mjs
```

## Portable source instructions

`?source-gameflow=portable&source-instructions=portable` enables the original
kickoff children, pre-selection introductory card and four-page instruction
process in the actual PlayerHost.
Omission (or `source-instructions=disabled`) leaves this feature off. It requires
an authenticated FourStation pre-snap-switch or separately admitted
default-audibles composition and deliberate known-new cabinet session.
StrictSource still rejects this portable feature. The default-audibles
successor is its explicit pinned metadata composition, not a relabelled base
profile or permission for new assets.

The menu backdrop stays unbound. Every actual fresh match binds the existing
source dispatcher/consumer **before source wake1**. The real kickoff catch
creates the deferred parent, which writes1, runs the source children, waits,
retires their IDs and resumes football. An eligible NORMAL selection-return
creates the four original pages: Rules, How to Pass, How to Audible and General
Hints. Entry writes-1; actual waits, text/plate flashers, cleanup and the final
ten source wakes precede2. PAT/kickoff are not normal-selection aliases.
Before NORMAL or PAT selection, the real inline `play_select` introduction
raises `show_plysel_idiot_box` by10 before the existing timer's first wake.
It renders the original GENERAL INFO, OFFENSE/DEFENSE and PAGE1 prints on
the three original plates; the returned side-label strings flash every7 wakes.
Only participating offensive teammates' held ABC can dismiss the active-team
wait: minimum40 **within total627** wakes, not40+627. The source FourStation
CPU-offense branch has **no wait**, so its setup/cleanup can complete without
a visible frame. Sound, flasher/string/object cleanup and-11 occur once before
the original20-wake menu initialization. Experience3 skips this branch.
The same live gate preserves the any-button helper's conditional direct tick.

Participation, `games_played` and the instruction flag have one live
`SourceCabinetSession` owner. Experience starts at known-new0 and increments
once per participating station only when an admitted match actually reaches
its final observation. Restarts/redraws do not increment it; rematches and
changed-team factories retain these in-memory counts. Source experience3
skips both tutorials. This is not a native save, user identity or persistence
claim. Focus/pause freezes live processes/resources; real restart/new-factory
boundaries reset/dispose them without repeating texture uploads.

The adapter executes all28 page,33 child and12 introductory source print
slots, the actual ESC justification/color and PLAYER decimal operations, source masks/PIDs, button
current/old writes and player flash flags. It uses only approved563
`playresult`, `bast18`, `bast10`, `bast8t` and the already-uploaded white mask.
Glyphs travel through the same C# HUD primitives and `IRenderSink.Present`
atlas packet as every other original screen, not HTML or JS game logic.
Native-named interfaces here are managed services, not invented unmanaged
callbacks. Authored image dimensions/anchors, portable ARGB palette/radian
sprite transforms and returned-body retirement are explicit portable policies,
not native SDK numeric, Composit, fast-trig or pixel equivalence. Missing
low-resolution BAST23/BAST10LOW fonts fail explicitly; no fonts are aliased.

All four physical acquired switch samples accompany the SAME configured
cabinet FIFO frame. Helpers read only the source-consumed snapshot and input
epoch, published before process dispatch even while `game_proc` is suspended.
Unjoined press/release and joystick changes survive the default-audibles FIFO;
there is no latest-render/host sample, second dequeue or guessed zero input.
Existing old/unbound capture paths retain their original contract.

The source stamps instruction, intro, timer, speech and cabinet sound operands
with its genuine shared ordinal before callbacks. The original typed
`TakeExecuting...` callbacks retain the **same** immutable queue record.
Host merges actorless, direct and VM outputs once in common ordinal order;
event metadata is not another sound delivery. C6 receives that exact record.
Original banks remain unadmitted; metadata is **not hearing**. Explicit decoded
mode still faults an unavailable exact bank/program—no invented ordinal,
native bank address, sample or PCM fallback.

```sh
bash tools/source-player/dotnet.sh build tools/source-player/SourcePlayer.Checks.csproj \
  -c Release --no-restore --nologo -v quiet
bash tools/source-player/dotnet.sh \
  tools/source-player/bin/SourcePlayer.Checks/Release/net10.0/SourcePlayer.Checks.dll \
  "$APPROVED563" --portable-instructions-only
```

`--portable-instructions-natural` runs the public-host kickoff-catch,
CPU-offense intro branch, normal selection, all four pages and resumed normal
play without clock/score/seed/gameplay-state setters. The focused selector also
uses public Versus choices to render a human-offense intro and exercises its
real minimum/timeout. It verifies the exact `885c…` default-audibles descriptor
in memory against unchanged563 payloads and the actual suspended-source FIFO.
Its terminal/experience and failed-bind fixtures remain labelled, not natural
regulation/browser acceptance. Final current WASM/GPU acceptance is separate.

## Source skin RGB and missing-arm regression

The original `PC_BLACK` uniform binds `ARMHND2.WMS` to logical material slot0.
Its authenticated, byte-identical authoring TGA contains skin RGB but alpha0
in all4096 texels. Applying the preview's generic alpha cutoff to this slot
discarded exposed upper-arm geometry, forearms and hands. The geometry, UVs,
roster selection and joint matrices were still present.

Only **skin slot0** bypasses that cutoff. Original texture bytes/alpha are not
rewritten. Uniform, facemask, jersey-number and world alpha exclusions remain;
so do source `SN_NODRAW`, selected held-ball models, poses, depth and back-face
culling. This is a portable authoring-texture material correction, not a claim
to reconstruct missing native WMS/Texus quantization or native arcade pixels.

Focused regressions:

```sh
node --test tools/source-player/renderer.test.mjs
dotnet build tools/source-player/SourcePlayer.Checks.csproj -c Release
dotnet tools/source-player/bin/SourcePlayer.Checks/Release/net10.0/SourcePlayer.Checks.dll \
  "$EXPLICIT_PRIVATE_PLAYER563" --arm-rendering-only \
  --proof-output "$OWNED_EXISTING_OUTPUT/managed-arm-proof.json"
```

The managed check reads actual pinned data and all32 original model selectors,
checks skin triangles/UVs delivered to `IRenderSink`, and compares both arm
branches in24 standing/running pose cases against an independent row-major
implementation of the pinned original `pipe3D.c` pivot/quaternion formulas.
It advances no gameplay host. The recorded-GL tests fail the old renderer,
exercise both skins/teams and all six arm joints, and preserve other alpha
cutouts, `SN_NODRAW`, matrices, culling and the existing material consumers.

For genuine pixels, serve an **owned ignored copy** of an approved bundle on
a free loopback port above8096, with its unchanged old `renderer.js` and the
corrected production renderer copied to a separately named module. Never edit
a protected live bundle. Run:

```sh
bash tools/source-player/with-browser-runtime.sh \
  tools/source-player/arm-rendering.browser.mjs \
  --url "$OWNED_REPRO_URL" \
  --before-renderer "${OWNED_REPRO_URL%/}/renderer.js" \
  --after-renderer "${OWNED_REPRO_URL%/}/renderer-after.js" \
  --output .squad/.scratch/arm-rendering/browser
```

This uses ordinary World startup/next-possession controls and real Escape
pause, then the two actual `drawScene` implementations on an identical
C#-supplied GPU packet. Genuine canvas/page PNGs, packet hashes, source-tick
brackets, real GPU state and non-MSAA per-limb identity counts are private
local outputs. No pose/camera/gameplay setters or substituted screenshots
are used. The scoped inspection allows a180-second managed-startup bound;
neither a30-second debugger timeout nor a missing general READY marker is arm
evidence. The owned browser closes on completion; the caller owns and stops
its repro server.

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

## Explicit default-audibles source integration

`original-teams-contact282-c2-m1-default-audibles` is a separate Portable opt-in
using `SourceC2Composition.CreateDefaultAudiblesSimulation`. Its loader retains
the inherited payload inventory and requires the same authenticated HUD,
interface catalog and playbook identity as the typed default-audibles data.
Declared-hash-only admission, static audible data and alternate input roots do
not substitute for these bindings. The pre-snap-switch build default, historical
one-argument preview URL default, older factories and raw cabinet held/edge
ordering are unchanged.

The source union includes the frozen default-audibles producer, roster/pre-lineup/
lineup/defensive restart, controls rebuild, snapshot/HUD and direct sound consumers.
It does not redesign audible behavior. `--default-audibles-adapters-only` remains
an explicit data-bound check at the manifest-loader boundary; its isolated units
and constructor-boundary World frame are not active producer or changed-World
qualification. See `../../docs/build.md` for the separate preparation/build opt-in.

The positive data-bound feature checks forward `data.Catalog.Profile.StableId`
to the existing two-argument `PreviewOptions.FromUrl` overload for all four
option combinations and their feature host. The historical one-argument
overload still defaults to M1, and the kernel's profile-mismatch guard is
unchanged. Complete normal managed checks are a product smoke/contract check,
not full P1, independent original-source parity, or native acceptance.

The normal upload-inventory check retains exactly32 model selectors, nonempty
World draws, the mandatory `hud/interface-white` mask and the historical texture
count of team textures plus28. Only the authenticated default-audibles profile
requires exactly one additional `hud/interface-chroma` upload (team textures
plus29); other profiles must not upload that mask. Twenty data-free synthetic
inventory controls call this same predicate once within the full normal check,
including plausible-count missing/replaced masks and extra/count mutations.
They add no gameplay, RNG or runtime process and do not replace the actual
uploaded-inventory assertion or constitute original rendering/parity evidence.

The pinned original `player.c` audible close path leaves the raw shown latch set
while clearing the action and removing menu objects and their child processes;
the explicit reset clears the latch. A shown latch is therefore not a visible
menu. Guarded episode diagnostics distinguish raw state from actual sprite-list
membership, current process slots and the committed same-Present HUD. Retained
object/child history (including a pre-kill process view) is not active membership.
No production latch or rendering change is implied by this diagnostic contract.

### Normal callback-bootstrap selection regression

The normal managed check validates callbacks with the same
`UsesPreSnapSwitchPayload` discriminator as `PlayerData`: historical M1 requires
`SourceC2CallbackData.M1Profile` and fourteen bindings; both pre-snap-switch and
default-audibles require `SourceC2CallbackData.PreSnapSwitchProfile` and fifteen.
The validation remains before normal `PlayerHost` construction.

After a **separately authorized build and execution grant**, run this dedicated
data-free regression on the rebuilt checks DLL:

```sh
dotnet path/to/SourcePlayer.Checks.dll --source-callback-bootstrap-only
```

It calls the exact normal validation helper for all three real resource profiles,
accepts each required callback-profile/count pair, and rejects the other nine
combinations. Its JSON identifies all twelve cases; it loads no bundle and
constructs no `PlayerData`, gameplay or host. Extra arguments return exit2 with
`Usage: SourcePlayer.Checks --source-callback-bootstrap-only` before data loading.
Restoring the old pre-snap-switch-only discriminator makes the default-audibles
case fail. This check is separate from affine/observation assertions and does not
qualify normal data-bound execution, gameplay, Unity, native World or audio.

## Explicit source raw-affine CPU pipeline

`OriginalPortable` layers use the existing bound interpolation operation for
frame/transition sampling and eligible render-time pose blending. The original
two-method `ISourcePoseInterpolation` contract and its Slerp arithmetic are
unchanged. A separate `ISourcePoseAffineHeading` capability on that same opaque
operation resolves integer facing through its already bound quarter row.
Only the existing defensive-pre-snap-switch opt-in binds this operation; library,
table, selected-row, profile metadata or tick-rate presence is not authorization.

The original layer constructors remain available and default to
`LegacyNormalized`. The older source-aware constructor still supports direct
`SampleLayer` without a facing value; the additional constructor retains the
actual integer `SourceFacing`. The source-affine path requires that value and
compatible in-process capability authority. Missing, unsupported, unknown,
mixed or incompatible bindings fail explicitly, including at endpoints.
Serialized metadata does not restore the private nonserialized operation.

`SourceAnimationMatrices.Evaluate` remains the normalized legacy API.
`EvaluateSource` consumes the explicitly bound layers, sampled poses and resolved
headings. It retains raw float quaternion-to-affine and parent/pivot order across
all fifteen joints. Direct joint positions remain distinct from pivot-corrected
model-origin translations; upper/lower branches and their masks remain independent.
The basis includes `ModelScale` once, and positions include animation root but
not outer actor placement. A raw joint's quaternion `Rotation` is unavailable,
not a normalized approximation. Direct vertices and `FrameMath.Joint` validate
scale rather than applying it twice. Packets preserve reflection, column-major
layout, float placement and the existing double-transport/Float32 GPU boundary.
Geometry/camera callers consume the same direct positions, and the raw digest
includes all nine basis components plus representation/scale tags.

Facing endpoints use the bound integer quarter lookup. Interior presentation
fractions retain existing shortest-arc heading blending, not a claim about
original fractional integer-facing arithmetic. Eligibility, contact/rewind/
teleport suppression, clocks and root placement remain unchanged.

Unity's `SourcePlayerRig` consumes the basis through owned per-instance mesh
clones from immutable baseline vertices. Joint transforms are translation
containers, not raw orientations. Normals use inverse transpose; tangents,
torso numbers, bounds, submeshes, materials, UVs and triangles are retained.
Clones are reused and restored/released on legacy/static/reset/rebind/appearance
changes and destruction. Unsupported or unreadable mesh/attribute states fail
instead of silently reverting to TRS. Shared assets, scenes and settings are
unchanged. Diagnostic affine evidence reports the basis and unavailable quaternion.

### Combined public regression wiring

After a **separately authorized build and execution grant**, pure selectors are:

```sh
dotnet path/to/ParityChecks.dll --source-affine-math-only
dotnet path/to/ParityChecks.dll --source-pose-math-only
dotnet path/to/SourcePlayer.Checks.dll --source-affine-packet-only
dotnet path/to/SourcePlayer.Checks.dll --source-render-observation-only
```

Each rejects extra arguments before data loading. Parity has 84 authored sources,
including `StreamChecks.cs` and `SourceAffineChecks.cs` once. The 11-source portable
check project includes `ArmRenderingChecks.cs`, `AffinePacketChecks.cs` and
`DefaultAudiblesChecks.cs` once and references its local `SourcePlayer.Core.csproj`.
The existing data-bound arm/default dispatch is retained.

The actual recorded-GL artifact has eight tests: all seven stream-root-base
renderer groups, including both arm cases, plus the frozen full non-TRS affine
packet case. It uses the common `playerScene` helper once and the unchanged
arm-corrected production renderer, not an older renderer substitution:

```sh
env -u SOURCE_PLAYER_RENDERER node --test tools/source-player/renderer.test.mjs
python3 -B tools/source-player/test_ci.py SourcePlayerCI.test_source_union_memberships
```

The static regression reads the actual project/test bytes and rejects in-memory
removal of stream membership, arm membership and the affine test with explicit
artifact/member/inventory diagnostics. Recorded GL is not a browser or GPU run.
Authored numerical fixtures, interpolation expectations, tolerances and original
oracles are unchanged. Existing source/arithmetic reviews keep their frozen
scope; they do not replace fresh source/reference/compiled/deployed bindings for
this union or authorize a repeated source-control campaign.

The actual full C#7.3/netstandard2.1 descriptors remain
`tests/default-audibles/DefaultAudibles.Production.csproj` (95 authored sources)
and `tools/animation/tests/MovementComplete.Production.csproj` (20). The net10
Core closure has 106 authored sources. These are static authored counts, not
SDK-observed generated inputs or compilation proof. Fresh generated-definition,
compiler/reference, PE/PDB and local Core deployment bindings remain pending,
followed by one focused integration review. The source-only
`SourceAnimationChecks.VerifySourceAffineSynthetic` requires separate engine
execution admission; pure checks cannot stand in for it.

## Opt-in render-consumption observation

Existing sampler callers and `PlayerHost` constructors remain observation-off.
Request `Evaluate(..., renderInterpolation01, true, out observation)` or opt into
the existing Host path with `new PlayerHost(data, sink, observeRendering: true)`.
Opt-out allocates no observation objects, arrays or clones. No URL/profile,
status, sink, shader or browser transport is added for observation.

The sampler retains its already computed current sample, the previous sample
only when that branch ran, the actual post-blend pose/decision/amount, headings,
animation root and the same evaluated result. It does not sample, calculate
matrices or normalize again. Previous/current views and actor identity accompany
those values.

`LatestRenderObservation` retains only the latest successfully presented frame.
It uses the existing Host epoch and frame ordinal, not a new lifetime/history
authority or successful-call counter. Reset clears it. Snapshot host/source ticks,
rates, actual sampler alpha, model/appearance slots, pose scale and joint-write
world root identify the observation. Retained joint/model/appearance and actor
collections are cloned before the same sink can mutate or reuse them. Publication
occurs only after that same `Present` succeeds and the existing epoch still
matches. A failed Present leaves the prior successful receipt intact; the status
string is the same outgoing immutable string.

The pure observation selector exercises immutable views, sampler/FrameMath and
receipt construction, not a live Host/Game/VM/Present. These are observed values,
not independent original/native expectations.

## Passive live-VM and exact stance-call observation

`ISourceLiveAnimationObservationState` is separately optional; existing audible
interfaces and constructors remain valid. `LiveAnimations` is unavailable (`null`)
outside explicit default audibles. Each enabled snapshot or reinitialization
phase owns fourteen actor-indexed immutable witnesses sharing its already-created
`ActorAnimationView`. Raw primary/secondary checkpoints and exact last-advance
ticks copy stored metadata once. Missing data and negative unavailable advance
ticks stay null, never snapshot/source tick substitutes; observed zero stays zero.
A bound inactive secondary may retain stored VM metadata without a specified
presentation layer.

Capture adds no pose evaluation, sampling, animation advance, process/AI/VM
dispatch or RNG draw. Other profiles allocate no witness collections. Phase IDs
0..9 retain their meanings; 10/11 (`StanceAnimationInput`/`StanceAnimationOutput`)
immediately bracket the same existing `FullBody` call. Phase5 remains after
grounding/role writes. Observations clear once per source step, retain the
128-checkpoint cap, and have a static caller-graph maximum of 97.

This public source integration neither copies nor revises private FinalT3
artifacts or their locked-out validation. E1 original instance/input history,
E2 independent pose ancestry and E3 original World/native numerical authority
are not supplied by current witnesses or Host epochs. Fresh compiled binding,
active default-audibles, Unity/native/GPU/DSP, performance, CI and publication
acceptance remain separate. FullWorld stays STOP, all16 stay OPEN, and the
whole-game goal remains unfinished.

## Verified persistent asset cache

The loader uses explicit browser CacheStorage, keyed by normalized application
base and each manifest-listed content SHA256. Every cache hit and download is
length/hash verified before use. A corrupt entry is discarded and fetched again;
a changed hash at the same filename cannot reuse the old bytes. The data manifest
and build receipt are still fetched afresh. Binary .NET/ICU resources use the same
verified mechanism and must agree with the current boot/resource pins. Fixed-name
bootstrap JavaScript remains fresh under the existing `no-store` HTTP policy.

Progress reports cached versus downloaded assets. CacheStorage requires a secure
browser context (HTTPS or localhost); unsupported, denied or quota-failed storage
emits one explicit warning and continues only through verified network downloads.
This cache does not use a service worker, change gameplay/input scheduling, or
modify the local-records IndexedDB database. Clearing this one CacheStorage cache
only makes the next load cold; it does not erase records.

`node --test tools/source-player/verified-assets.test.mjs` exercises content
updates, corruption, base/profile safety and explicit fallback. The bounded
`verified-cache.browser.mjs` caller measures actual cold/warm network bytes with
normal Chrome sandboxing and HTTP caching disabled. The image workflow validates
the candidate before replacing `latest` and verifies anonymous latest cold/warm
behavior independently. `refresh_static_bundle.py` retains the actual original
CoreAOT/nativeWASM hashes and explicitly records JS-only source provenance; it
never claims a new C# compilation.
