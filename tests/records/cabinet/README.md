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

The unselected suite also checks editor-save failure/draft retention, signed
audibles, actual custom-page selection, final retry/once-only/rematch, Guest,
space names, duplicate stations, corruption and explicit recovery/import.

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
`--records-only` stops after authentic editor/audible save and page reload;
without that selector the driver also reaches actual saved-play gameplay.
Gameplay acceptance requires moving actual kickoff actors, nonzero signal from
the original audio graph and paused GPU original-shadow/material-differential
pixels. After public resume it requires newly observed physical neutral samples,
a real A press, normal LiveBall/moving actors and another240 real source ticks
with fresh original voices and nonzero graph signal; returning from GPU pause
alone is not playable-image acceptance. `--full-match` additionally requires the
genuine final commit and career reload. The first resumed-play milestone is
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

Evidence contains safe statuses and counts, not identities, PINs, envelopes or
foreign exception messages. Original asset pixels, hosted source behavior and
portable persistence do not establish native MIPS/CMOS/DSP equivalence.

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
