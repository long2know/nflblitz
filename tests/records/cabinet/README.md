# Actual source cabinet records

These checks use the production C# cabinet, Backend record store, original HUD
artwork, saved-play binder and final `game_proc` I/O barrier. They do not use
HTML game menus, JavaScript game rules or native-CMOS substitutes.

## Cabinet controls

P1 uses WASD and V/B/N for the original A/B/C buttons. The other physical
stations retain the normal source-player bindings.

1. Click the real original-game sound start button. At Joining, C enters the
   source options. Left/right chooses CREATE PLAY, ENTER NAME or SELECT TEAM.
   C/down opens explicitly portable local-storage management.
2. ENTER NAME gives every joined station its own default-NO choice. Up chooses
   YES; A confirms. The source keyboards contain six name positions and four
   PIN positions, not three initials or a text input. DEL and END are real
   keyboard choices. All-space names are valid named identities. A leading dash
   is the source guest convention. PIN entry is masked.
3. Missing records require explicit Create. Corrupt, unsupported, conflicting
   or unavailable stores are not missing records or automatic guests. A record
   already chosen at another station requires reentry or deliberate Guest.
4. A selected named station opens career pages with A. Left/right changes the
   career page. Create/edit uses the real five source formations and three
   nine-point receiver grids. A/B plants a route point; C backs up or exits.
   The action menu offers juke, spin, turbo, deletion and completion. Source
   validation, earned-slot thresholds and protected-play checks remain Backend
   rules. SAVE PLAY is an awaited durable operation.
5. Audible rows retain signed source IDs; C flips the chosen entry. Custom
   offense IDs 37–45 select the actual saved slots. Restoring an allzero row
   means the original defaults, not a generic replacement play.
6. Continue through the actual team/roster/versus controls. The last offensive
   play page renders the selected station's actual saved grid. Double-turbo
   and ORIGINAL use the same dynamic definitions, routes and flip state.
   Double-turbo uses the original 1–9 eligible **released-C** source checks
   (not down-to-down time); the slow pregame button cadence is not suitable.

Original pregame process sleeps and physical-switch debounce are intentional.
Use released presses rather than holding a button across a screen change.
Source-bound pregame controls use the cabinet buttons, not a synthesized
physical A from Enter.

## Save, reload and local files

One browser adapter and store belong to one player lifetime, across rematches.
The database is the fixed same-origin IndexedDB cabinet, independent of build
digest. It uses opaque primary/backup/recovery values and atomic compare/exchange.
Only explicit NotFound followed by Create initializes an empty cabinet.

The actual source final captures immutable period scores, physical participation
and complete source statistics. Its parent process waits for the atomic career,
world-record and receipt commit **before** final statistics. Failed writes retain
the previous Current and provide reload/retry or explicit decline/recovery.
Partial or ineligible tied matches are not padded to four periods.

Local management arms the normal browser file/download controls. Imports and
destructive replacement require a further default-NO cabinet confirmation.
Exports contain local PINs; keep them private. Recovery preserves corrupt data.
The format is portable logical records, **not native CMOS binary**.

World bests are six actual nullable global entries. Unset is not measured zero.
Career masks remain preserved source fields. Source high-score comparisons use
four-game qualification and original arithmetic; popular-team audits remain
explicitly unavailable rather than being fabricated from career totals.

## Focused checks

Use the existing SDK:

```sh
SOURCE_PLAYER_DOTNET=/path/to/existing/dotnet \
  python3 tests/records/cabinet/run.py /path/to/explicit/current/data
```

Selectors:

- `--source-menu-driver`: genuine Core 57 Hz pregame processes, actual physical
  samples, scoped original sound operands, real record UI and async IO resume;
  no PCM or GPU qualification is inferred.
- `--natural-full-match`: normal public controls through a named match,
  genuine final career/world commit and a fresh store reload. No private game,
  clock, score, actor or seed mutation.
- `--terminal-only`: clearly labelled final/IO edge fixtures. These first execute
  a real earlier EOQ/coaching/music selection and subsequent kickoff, then declare
  the terminal boundary. This is not natural four-quarter evidence.
- `--observed-resume`: narrowly selected actual Host lifecycle regression with
  causal measured active-host elapsed, never audio-render batching as physics,
  bounded pre-pause debt drain, two actual paused physical observations, fresh
  consumed source epoch and unchanged C6 audio origin. It selects a fresh default
  normal play and waits for the real QB counter/mode/interruptibility gate before
  physically snapping; it does not prime the Bridge or write game state/clocks.
- `--statistics-to-career`: **sixteen of sixteen rows accepted on the qualified306 epoch**. This
  compares source-expected result counters with the actual immutable final,
  career/world commit and reload. `SOURCE_RECORD_STAT_CASE` selects one named
  case; absence selects the complete matrix. A nonzero exit is not evidence
  that any unexecuted case passed. The declared late-match score/period/result
  inputs are fixtures, not a natural four-quarter/flight/contact claim.
  `--output` chooses a new dated ignored output root. `--statistics-controls`
  additionally clean-compiles two isolated actual-source controls and forces a
  fresh complete final baseline; it rejects a single-case environment filter.

The unselected suite also checks editor-save failure/draft retention, signed
audibles, actual custom-page selection, final retry/once-only/rematch, Guest,
space names, duplicate stations, corruption and explicit recovery/import.

## Source accounting mapping and actual coverage — November 10

**Current terminal status: all sixteen named cases passed, then two real
compiled controls failed their exact predicates, then a forced-fresh unchanged
sixteen-case run passed again (38,386 assertions).** Earlier blocked/unaccepted
statements below are retained as chronological triage, not the current result.
The exact final acceptance packet and scope are recorded after the progress log.

The original `include/GAME.H:tdata_t` is per side. The sixteen static roster
records/team and seven on-field actors/side are not sixteen independent saved
careers. `lib/select/record.c:save_player_records` credits a participating named
human's career with that human's side totals. The pinned source revision remains
`89f840d58d152a2a9f230cb8d0cdffbd554c8ded`; verified blobs include
`nfl.c=15b49474fb55e8bac280b6515fcf7bf59cdf8664` and
`lib/select/record.c=b0eb18e3440967c767a4cc98874a16f34183a617`.

| Original source | Production counter/boundary | Actual Backend execution, not inferred coverage |
| --- | --- | --- |
| `player.c:qb_release_ball` predicted target >= LOS | `GameSimulation.ReleaseSourcePass` → `SourcePassAttempts` → `GameSnapshot.PlayActivityStats` → career `PassAttempts` | New qualified starter reaches real raw-B/actor-VM release and actual forward-ball flag. Numeric final/career comparison is blocked below. The old321 assertions used literal typed final values, not this producer. |
| `nfl.c:1514–1599` ordered ordinary/quick return, completed-pass, sack, rush branches | `SimulationState.AccumulateSourcePlayEndStats` / `AccumulateSourcePassCompletion` → immutable `PlayEndStats`, `CompletedPassStats`, `PassCompletions` | Source mapping inspected; **no fresh event→final case accepted yet**. Punt-TD's original no-return-credit branch, negative yardage, conversion passing without pass-TD, and sack-side accounting have explicit expectations but are not claimed green. |
| `player.c:_mode_wpn/player_down` possession credit | `SourceCarrierControl` / `CreditSourcePossession` → `SourceInterceptions`, `SourceLostFumbles` → snapshot | Opposite-side interceptions thrown are checked in the literal final writer tests. Actual turnover→final linkage remains unaccepted. |
| `detect.c` human successful-carrier-stop counter, `player.c:fumble_ball` | `StopSourceCarrier`, `SourceBallDriver.Fumble` → activity tackle/fumble totals | Literal career mapping is covered; the follow-up does not claim natural contact detection or fumble-decision coverage. |
| `nfl.c` touchdown/safety/PAT-safety/two-point result switch | `AccumulateSourcePlayEndStats` scoring groups, `AddSourcePoints` and retained quarter scores | Explicit expected fixture rows exist. They have not reached accepted actual-final results. No synthetic `extra_points`/punt counter is invented for unwritten original fields. |
| `nfl.c` free PAT and period gates | `SourceMatchFlow.SelectPat`, `ApplySourcePat`, `AdvanceSourcePeriod` | Old61 checks exercise EOQ/coaching plus a declared terminal boundary, **not all PAT/safety/OT transitions**. New free-PAT and terminal seventh-period tie cases remain unaccepted. |
| `nfl.c:game_over` → `record.c:save_player_records/check_world_records_*` | actual `game_proc` `BeginSourceCabinetFinal` → `SourceFinalRecordCommit` → `SourceCabinetRecordsRuntime` → store → `SourcePlayerRecords.CommitMatch` → atomic CAS → final continuation | **61 actual caller checks passed** for pending no-speculative-state, failure/retry, same final snapshot, once-only receipt, points/worlds, rematch/config retention and reload. They do not prove the event-producing rows above. |

Career eligibility retains original full four-to-seven-period station masks,
physical station order and original two/four-station side mapping. Eligible
teammates receive the same side's statistics; interceptions thrown use the
opposite side's interceptions made. World candidates have the separate named
station rule without the full-mask filter. The portable free-play adapter fixes
participation before wake1; it does not claim original coin-driven joining.
These writer/eligibility cases passed in the unchanged literal-final suite,
not in the new event campaign.

The qualified starter reuses the already verified public selection→source
presnap grace→A→real center ANI callback→QB catch→fresh B at actual
`player_proc` wakes. Declared LOS is supplied before original lineup, not by
changing the QB's mode/caller or teleporting a near-LOS QB afterward.
The first follow-up currently fails closed during post-result/final progression:
`SOURCE_AUDIO_PROGRAM_NOT_ADMITTED: cardinal:1`, from the real
`SourceProcessSoundCall` consumer. No audio guard is bypassed and no missing
programme or source counter is fabricated. Systems owns the exact SAUD/bank
mapping/admission dependency. The complete log and current source hashes are in
`/home/long2know/.cache/nflblitz-quality-20261110-1440/backend-evidence-20261110/accounting-qualified-boundary-20261110.tgz`.
**D1/F1 accounting-event acceptance remains open** until a qualified data/caller
handoff permits the comparisons to finish. Earlier save-format fixes and their
evidence remain frozen and separate.

### Qualified306 accounting progress

The distinct persistent `original-teams-complete-av306` root is now qualified:
`/home/long2know/nflblitz-unity-evidence-20261110/av1153-release-01/readonly-av306`,
top manifest SHA256
`51e94caffcf5875e024206eb069dfd905ee13e134b09e7f5020ff68349461e5d`.
This supersedes only the earlier audio-blocked status, not its historical
limitations or the immutable1114/e44 artifacts.

**Seven actual event/result→final/CAS→career/readback rows passed**, on one
unchanged192-source generation (terminal Bash1329, all final builds0warn/0err):

| Accepted row | Discriminating source observation | Final score/periods |
| --- | --- | --- |
| completed pass | actual raw-B/ANI release; attempts1, completions1, passing19, total20 including inherited return1 | 3–0 /4 |
| pass touchdown | passing10, passTD1, touchdowns1; actual PlayResolved TD before source PAT/begin-play cleanup | 6–0 /4 |
| sack | defense sacks1/sack yards5; not a rush | 3–0 /4 |
| two-point pass | passing10/completions1, two-point attempts1/made1, no passTD or touchdown | 8–7 /4 |
| failed two-point rush | rushing5/rushes1, attempt1/made0 | 7–6 /4 |
| safety | guilty-side safeties1, rushing−39 after source spot clamp; opponent gets2 | 3–2 /4 |
| terminal third-OT tie | genuine terminal game_proc/career commit at period7; tie is a non-win | 3–3 /7 |

These are declared late-match/result inputs and actual source producers where
stated. They do not claim natural four/seven-period progression, complete
flight/catch/contact fidelity, all nineteen original rows, or whole D1/F1
acceptance. Each accepted row checks all nineteen counters per side against
the retained snapshot, the real pending CAS/no-speculative-career boundary,
source career mapping/opposite interceptions, once-only receipt and new-store
readback. The15,576 assertions include the genuine source-wake starter checks,
not15,576 different statistic cases.

The completed-pass fixture's former expected20 was independently disproven by
compiled byte-unchanged original C: queued WORLDX60 bits`429bfffc` →
77.9999694824 → original integer spot70.2000046 → FIELDX59 → LOS40 gives19.
Only that literal delta changed; no spot, counter, tolerance or rounding changed.
Authority:
`/home/long2know/.cache/nflblitz-quality-20261110-1440/completed-pass-exact-original-C-decision-20261110.json`,
SHA256 `5d5b0c2d0f6d4f006d7a52e0175d7b42a58bee176f9c6ea92375a5847eb21836`.
The separate rush WORLDX55 bits`421bfff8` similarly has independent original-C
authority for delta14, not15; its corrected row still requires a stable rerun.

Accepted receipt/log archive:
`/home/long2know/.cache/nflblitz-quality-20261110-1440/backend-evidence-20261110/accounting-av306-stable-accepted-02-20261110.tgz`,
SHA256 `2b890a7855bea9da7eef206b319ee567e13a0bf26af34b10f9557ab3ef1a0f51`.
Adjacent `accepted-accounting-rows-20261110.json` lists only accepted rows and
the exact invocation. Other cases remain open: turnover/PAT-defense fixture
queueing versus actual cambot suspension, human tackle's forbidden observer
callback, public free-PAT input cadence, and actual public punt producer setup.
None of these failures is silently promoted or fixed by relaxing a guard.

Subsequent stable-generation runs accepted six further rows: exact-original-C
rush14, public free-PAT after TD (8–7), interception, lost fumble, defensive PAT
safety (8–6), and an actual mirrored detector/body-contact→ANI `player_down`
human tackle. The tackle declares the original no-fumbles powerup boundary,
preserves the live Host clock/camera/process owners, and calls neither Stop nor
End from an observer.

The turnover failures were invalid test queue boundaries, not a HUD/stat bug:
immediately before external End, real cambot had suspended `game_proc`
(SleepWord32769) and slept40. The fixture now lets that original continuation
finish and verifies an actual resumed game-proc wake before declaring a later
result. No HUD guard, suspension duration, sound guard or production math changed.
Free-PAT input now travels through Host Capture/Advance rather than manually
stepping a Host-owned Game and invalidating its audio clock.

`accepted-accounting-rows-13-20261110.json` in the same private evidence directory
lists all thirteen accepted rows and their separate coherent source epochs;
SHA256 `deb2e5f3cc7aed559246a9292f62a240f353d09fe5f8fd18f0a8a433959b4b63`.
Three human punt rows and a final single-generation complete selector remain
open. The pending punt handoff uses public typed source selection and real
neutral/ANI launch callbacks; it will not be labelled raw page-cursor proof.

### GC_SKILL public/configuration handoff — Frontend primary

The coordinator assigned **Frontend** the public/configuration binding; request
the narrow Systems file release if needed. Backend is not editing this binding.

Verified original `include/game_cfg.h` blob
`ab1f9b03938275190313c63d462636ec18a178ab` defines `GC_SKILL=5` and
`void apply_user_config(int key)`. Original `lib/select/game_cfg.c` blob
`058293afb99f81b8a3b38991ca6fa2fe3be7440b` declares
`skill_adjust[3]={-3,0,3}` at963–966 and assigns
`user_base_adjust=skill_adjust[value]` for GC_SKILL at1031–1032.
Source values0/1/2 therefore mean −3/0/+3; `clear_user_config` explicitly resets
the adjustment to0. This is separate from the operator DIFFICULTY adjustment.

Exact existing type:
`/data/source/nflblitz/.squad/.scratch/parity-completion-20261109/Assets/Scripts/Gameplay/GameSimulation.FireHandicap.cs`

```csharp
SourceHandicapSettings(int userBaseAdjustment = 0, int humanWinStreak = 0,
    bool randomDrones = false, int offenseTeamMask = 0, int defenseTeamMask = 0,
    int dronesTeamMask = 0, bool noAssist = false, bool superDrones = false)
```

Exact missing forwarding file:
`/data/source/nflblitz/.squad/.scratch/parity-completion-20261109/tools/source-player/Runtime/KernelBinding.cs`,
`KernelBinding.Create`, factory branches around53–101. These existing
`SourceC2Composition.Create*Simulation` overloads already accept optional
`SourceHandicapSettings handicapSettings=null`, but no branch supplies it.
The minimal agreed boundary can append that optional parameter to
`KernelBinding.Create` after its existing `soundBankState` parameter, forwarding
the exact named expression **`handicapSettings: handicapSettings`** to every
current factory arm. The constructor otherwise supplies fresh zero-adjustment
settings, leaving public GC_SKILL ineffective.

Exact producer callsite:
`/data/source/nflblitz/.squad/.scratch/parity-completion-20261109/tools/source-player/Runtime/PlayerHost.cs`,
`CreateSessionGame(SourceMatchConfiguration configuration)` around1321–1327,
called by `BeginSessionMatch`. Resolve the validated public per-game choice from
Frontend-owned
`/data/source/nflblitz/.squad/.scratch/parity-completion-20261109/tools/source-player/Runtime/SourceCabinetSession.cs`
before the factory/wake1, and pass the resolved settings through that boundary.
Preserve all other actual settings fields; do not derive it as `Difficulty-5`,
guess an invalid index, or silently lower-clamp the source base<3 condition.
The existing consumer combines `DroneBase + UserBaseAdjustment` and explicitly
rejects the original undefined low-base table read. No record schema/DTO change
is justified by this per-game configuration forwarding.

### Terminal sixteen-row acceptance and source freeze

Terminal **Bash1426 exit0** used the exact persistent readonly306 root and all
sixteen named rows in `AccountingCases()`, including TD/pass/rush/sack,
punt return/OOB/return TD, interception/fumble/human tackle, successful/failed
two-point/PAT/safety, free PAT after TD and terminal seventh-period tie.
All38 numeric side-counter comparisons, the real `game_proc` immutable-final
barrier, no-speculative-state while actual CAS waits, exact career counters,
all six source world categories, one receipt and full career/world reload passed.
Public typed punt selection is not raw page-cursor proof; the declared
no-fumbles tackle boundary is not neutral contact-probability qualification.
The seventh-period case starts from a declared terminal period, not naturally
played regulation/three-OT progression.

The final new runner command, inside the existing private POSIX build wrapper,
was:

```sh
python3 -B tests/records/cabinet/run.py \
  /home/long2know/nflblitz-unity-evidence-20261110/av1153-release-01/readonly-av306 \
  --statistics-to-career \
  --output "$PWD/.squad/.scratch/SourceData/backend-accounting-20261110/final16-controls/campaign" \
  --statistics-controls
```

Only genuine NETSDK1004 probes triggered restores. All final baseline and
control compilations completed with zero warnings/errors. The controls mutated
private copies of the actual production sources, never the canonical files:

* `SimulationState` adds one extra passing yard: clean compile, actual completed
  pass fails **home.passing expected19/actual20**.
* `SourcePlayerRecords` credits own rather than opposite interceptions: clean
  compile, actual turnover final fails **career.Intercepted expected1/actual0**.

After both semantic failures, the runner forced `Rebuild` from unchanged
canonical source and reran **all16 /38,386 assertions green**. All192 captured
source/test hashes matched before/after. This is actual boundary evidence, not
an exit-code-only gate or a reused isolated-control assembly.

Original-C binary32 authorities changed only three incorrect fixture deltas:
passing19 (`429bfffc`), rushing14 (`421bfff8`), and Away punt return16
(`42e9fffc`, ko_catch20). The original spots/rounding/conditions/stat code and
all guards remained unchanged; no tolerance or ±1 policy was introduced.
The original ordinary punt-return branch does not grant return yards for a TD;
that discriminating zero-credit case passed separately.

Persistent final archive:
`/home/long2know/.cache/nflblitz-quality-20261110-1440/backend-evidence-20261110/accounting-av306-final16-controls-20261110.tgz`,
SHA256 `a3b7599325fd81a43b675df2acbee98543e2c4a2dddb475c35d1cf055387207c`.
Adjacent `accounting-final16-summary-20261110.json`,
SHA256 `e658899065bf489f4cbf18c77e567689ee7e9605ae17b8a3f40c70db4bc3edbd`,
contains exact commands, controls and all192 source pins.

No remaining production statistics-math omission was confirmed in this bounded
matrix. The prior corruption/receipt fixes remain independently frozen.
This does not close whole D1/F1, all31 roster/material/caller combinations,
natural matches, dynamic paid joining, unknown target ABI/DSP or browser/AOT.
Named/saved TwoStation admission is a separate Frontend-led source-backed
workflow; older Guest-only guards/profile IDs are not promoted by this result.

### Separate named TwoStation source/provenance handoff

Original TwoStation names/PINs and saved custom play acquisition are supported,
not Four-only. Verified `lib/select/select.c`
blob`26c3955a4081aff73d6e4f1f7633ae86f24e2c4a` always creates P1/P2 cursors;
only P3/P4 are under `four_plr_ver` (2423–2435). A complete six-name/four-PIN
entry and successful record lookup reaches the same custom-load predicate:
`disk_bank>=0 && read_plays_hd(record,bank,custom_plays[pnum],audibles)>0`
(2908–2943), with no Four predicate. The later stats display explicitly chooses
Two versus Four layout (2986–2989).

`record.c` blob`b0eb18e3440967c767a4cc98874a16f34183a617` saves
`four_plr_ver?MAX_PLYRS:2` positions and maps Two0→Home/Two1→Away (298–300);
its dedicated two-player world writer is475–617. Existing portable saved-session
and career/world mapping already support that physical mapping. No identity,
career arithmetic or bank-address reinterpretation is required to admit it.

There is a separate real logical custom-block-history gap:

* `cap.c` blob`42605264092a30f861f5390535bbe217aac13b66`,
  `read_plays_hd`5120–5130 counts each explicit `CPLT_UNUSED` slot as valid
  after block CRC validation. It returns `valid_plays` at5190; `Exists` and
  successful block acquisition are therefore different source facts.
* `create_play_exit_proc`803–829 always saves for a valid record number, even
  with no active edited play. Available untouched slots were explicitly marked
  `CPLT_UNUSED` at1068–1074.
* `write_plays_hd`4904–5034 copies all nine slots/audibles, generates CRC,
  writes/verifies and returns its bank. It does not free an empty block.
  `update_stored_plays`5196–5220 records that bank. Return−1 is failure,
  not intentional last-play deallocation.
* Thus public CAP-open→exit-without-edit can allocate a valid empty block;
  next name/load can legitimately enable the empty custom page. Original
  route deletion is not a native whole-slot-free operation. The local whole-slot
  Delete management action does not establish a native free-bank rule.

Portable v1/v2 have nine nullable logical slots, reject nonnull `Unused` entries,
and preserve `DiskBank` only as opaque metadata. A local created record uses−1;
local custom saves do not assign a hardware bank. Its SHA envelope is not a
native cap-block CRC/ABI. Therefore neither all-null slots, GP0, a loaded logical
document nor an imported nonnegative DiskBank proves source block allocation.

Frontend owns the distinct new named-Two profile/workflow. Lead/Systems agreed
the existing sealed resource-profile singleton as readonly constructor-time
capability and a new two-argument saved-record binder, leaving old guards and
IDs unchanged. The source-required coordinator decision now authorizes an
explicit **new-profile-only** version3 logical-history policy:
`SourceRecordSavePolicy.CustomBlockHistoryV3` and
`SourceCustomBlockState.LegacyUnknown/NeverAllocated/Allocated`. Fresh creation
starts NeverAllocated; successful actual custom save or empty CAP exit acquires;
portable delete-last, audibles and career/copy rewrites retain acquisition.
Actual CAP-slot B exit awaits the store transaction and normal source-menu resume,
even without edits; failure does not close CAP or install speculative history.
No native bank, CRC, nonnullUnused DTO or layout-specific fallback is invented.

Opted-in validated v1/v2 actual Exists entries explicitly migrate to Allocated;
all-null entries remain Unknown regardless of bank or games. Reads do not write;
only successful explicit mutations upgrade with the original valid-byte backup.
The new-profile-only `FromTwoStationSavedRecords(records)` factory rejects selected
Unknown rather than substituting KnownNone/Guest, while legacy constructors and
default version2 serialization remain unchanged. Version3 requires the literal
integer history field0..2; malformed/contradictory inputs fail.
See `tests/records/README.md` for the focused storage/migration/failure selector.
Public empty-exit/reload/forced-choice caller and browser/AOT admission remain
separate Frontend/Quality acceptance. The sixteen-row statistics result above
does not close this independent named-Two F1 workflow.

## Actual browser

```sh
node tests/records/cabinet/browser.mjs /path/to/current/AppBundle \
  .squad/.scratch/original-record-ui-20261030/browser-new-run --full-match
```

This requires a **new** evidence directory and an absent authorized short Chrome
root. It creates only its own synthetic profile, uses normal Chrome sandbox and
autoplay policy, clicks the real trusted sound start, drives the original C# HUD
with physical key events, saves/reloads actual custom plays and signed audibles,
and optionally runs a real-wall-clock/RAF named match and reloads its career.
The bounded cold-start command wait accommodates actual synchronous C# loading;
it does not alter game clocks, audio guards or shared browser helpers.
Frequent polls use the public read-only `sourcePlayer.observe()`; physical
events go through the real browser input adapter and its observed capture API.
The actual public `audibles.core` is legitimately null during kickoff and until
normal play selection finishes. Numeric action/choice/caller checks apply only
to the genuine subsequent normal PreSnap/opened-audible state, never a fallback.
The exact build receipt is rechecked before and after reload and at completion;
changing a served candidate during the run cannot qualify mixed-source evidence.
`--records-only` stops after authentic editor/audible save and page reload;
without that selector the driver also reaches actual saved-play gameplay.
Gameplay acceptance requires moving actual kickoff actors, nonzero signal from
the original audio graph and paused GPU original-shadow/material-differential
pixels. After public resume it requires newly observed physical neutral samples,
a real A press, normal LiveBall/moving actors and another240 real source ticks
with fresh original voices and nonzero graph signal; returning from GPU pause
alone is not playable-image acceptance. The same browser then holds directions
and C on all four physical cabinet bindings, switches to its own other tab,
and must observe actual document focus loss, suspended audio and an exactly held
logical sample. A hidden tab's last render is not a live input oracle. The first
returned rendered sample must show neutral inputs in every slot before the
driver sends any key-up events, with the same held media sample on resume; this
is four-slot physical-input evidence, not a claim that four humans played.
`--full-match` additionally requires the
genuine final commit, at least four completed source periods in its public
final-period view, and career reload. The first resumed-play milestone is
persisted immediately in `post-resume-first-play.json`; the same browser/profile
then continues the full match without a second UI setup.
After the acknowledged final commit, the full driver uses the verified public
Final-screen B/Next-Match A route for one genuine fresh rematch, checks that the
generation/completed-match cursor did not change, then reloads the committed
career. External image runs additionally repeat the same-origin generation
restart handshake with `final-recreate-ready.json` and
`final-recreate-resume.json`; the coordinator recreates the same tested image.
Network evidence records only origins,
counts and failed status codes;
any nonlocal HTTP(S) game request or uncanceled resource failure rejects the run.
`--startup-only` is a diagnostic trusted-start/Joining check, not gameplay or
record acceptance. Failed static requests retain only sanitized basenames and
framework/data classification for diagnosis. The local test host responds204
only to the observed browser `/favicon.ico` probe; missing game files remain404.
`--first-kickoff-only` retains the same public record/custom/audible setup and
reload, then bounds observation to45 seconds after real game start. It uses
the unchanged instruction/result button policy and requires moving actual
Kickoff actors, fresh original voices, nonzero measured signal and source700,
beyond the recorded source649 underrun boundary. It is not full-match acceptance.
The full-match selector requires that same kickoff milestone first and then
continues in the same browser/profile through gameplay, focus checks, four
periods, final commit and rematch; no second kickoff campaign is needed.
Screenshots and paused material readbacks record their observer wall-time
brackets; those timestamps do not advance the game or audio clocks.

Evidence contains safe statuses and counts, not identities, PINs, envelopes or
foreign exception messages. Original asset pixels, hosted source behavior and
portable persistence do not establish native MIPS/CMOS/DSP equivalence.

For a fresh deployment or session evidence root, supply the existing owned
physical evidence parent and the deployment owner's assigned **absent** short
POSIX Chrome root explicitly:

```sh
SOURCE_RECORDS_EVIDENCE_ROOT="$SESSION_FILES" \
CHROME_RUNTIME_ROOT="$ASSIGNED_ABSENT_POSIX_CHROME_ROOT" \
SOURCE_RECORDS_EXPECTED_BUILD_SHA256="$CURRENT_BUILD_INFO_SHA256" \
node tests/records/cabinet/browser.mjs "$OWNED_LOOPBACK_CANDIDATE_URL" \
  "$SESSION_FILES/browser-current-001" --full-match
```

The evidence directory must be a new direct `browser-*` child of that parent.
Runtime ownership, real paths, private0700 permissions, short socket paths,
normal sandbox/autoplay and trusted audio start remain mandatory. System
temporary directories are not admitted. A filesystem that discards permissions
is a runtime blocker, not permission to waive the guards.

Focused harness-contract checks do not launch a browser or qualify gameplay:

```sh
node --test tests/records/cabinet/browser-contracts.test.mjs
```

The explicitly sealed diagnostic/AOT profile uses
`SOURCE_RECORDS_EXPECT_STEP_COSTS=1`. The driver reads the existing public
`Browser.SourceStepCosts()` export only inside its public paused audio fence,
or after a terminal fault. Disabled/malformed snapshots fail rather than
substituting rendered-frame timing. Actual first16/recent64 source-step elapsed
times and allocations are recorded separately from rendering; the workloads
and source tick ranges remain explicit, not an assumed AOT causal comparison.
The current catalog profile additionally sets `SOURCE_RECORDS_EXPECT_WORLD_CATALOG=1`
and its explicit sealed `SOURCE_RECORDS_DESIGNATED_HOME_SIDE=Home` (or `Away`).
Actual game-start/rematch venue hashes and home-team ownership must match that
side. A legacy venue, stale team selection or opposite-side binding fails;
component catalog counts alone do not satisfy the browser check.

The separately sealed `aot-rooted-bounded-caller-drain/v1` profile additionally
sets `SOURCE_RECORDS_EXPECT_CALLER_DRAIN=1`. After moving kickoff, the driver
pauses with public Escape and requires the exact JSON-string
`sourcePlayer.callerDiagnostics()` contract, an observed bounded zero-elapsed
drain and actual pause cancellation before continuing the same browser.
Absent retained drain evidence is inconclusive, not replaced with the profile
flag. The atomic getter read must leave source/input/clock/media state unchanged.
Raw first16/last128/slowest16 events, dropped counts and fresh Present packet
tags are saved at that fence and on a terminal failure. Nonpresent callbacks
do not establish live source/host state; synchronous JS/WebGL spans do not
measure GPU completion or full managed Render. Introduction/suspended windows
are not assumed to be active-football steady CPU. The driver never invokes a
Frame/drain export or reads these diagnostics while active.

### Same-origin container recreation

The deployment owner can target its own loopback container instead of starting
the driver's static server. Require the exact final immutable build receipt:

```sh
CHROME_RUNTIME_ROOT=/home/long2know/.bdk-4e97629f \
SOURCE_RECORDS_EXPECTED_BUILD_SHA256=<accepted-build-info-sha256> \
SOURCE_RECORDS_RECREATE_GATE=1 \
node tests/records/cabinet/browser.mjs http://127.0.0.1:18099/ \
  .squad/.scratch/original-record-ui-20261030/browser-docker-new-run
```

The assigned short profile root and evidence directory must both be absent.
The deployment driver uses only its own profile, not the named-match profile.
After actual editor and signed-audible commits, the driver pauses by public
Escape and creates `recreate-ready.json` in that evidence directory. Keep the
browser open, recreate only the owned container at the **same origin and port**,
and verify that its game and headers are responsive. Then write
`recreate-resume.json` in the same directory:

```json
{"schema":"source-record-container-recreate-resume/v1","buildReceiptSha256":"<same accepted build-info-sha256>"}
```

The bounded handshake waits at most three minutes. The driver rechecks the
unchanged build receipt, performs an ordinary page reload and trusted start,
and proves actual existing-record lookup, saved route-card rendering and signed
audibles before continuing into genuine gameplay. It neither invokes Docker
nor reads/writes IndexedDB directly. The container has no server-side record
volume: persistence belongs to the unchanged browser origin.

## Verified combined image workflow

The October30,2026 `browser-named-012` run passed on the exact corrected loaded
Docker image: original name/PIN and CAP editing, custom/signed-audible save,
generation4 same-origin container recreation, real saved-play selection,
audible/ORIGINAL, sample-fenced pause/resume and244 live source wakes with fresh
original audio. The same browser then completed four actual quarters, acknowledged
generation5 at score0:55, started one fresh rematch without recommitting, recreated
the same-origin container again and reloaded the visible one-game career.

The run used only trusted public controls and real RAF/host time: no game-clock,
score, phase, pose or RNG setters. All3462 browser HTTP requests stayed local,
with zero failed resources, exceptions or console errors. Exact evidence is in
`.squad/.scratch/original-record-ui-20261030/browser-named-012/result.json`;
earlier failed runs remain preserved and are not relabelled as accepted.
