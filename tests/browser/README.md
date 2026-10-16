# Real WebGL browser smoke

The driver uses the installed Node 24 built-in WebSocket/CDP support,
`google-chrome` and `tesseract`. No Playwright/Puppeteer/npm dependency is needed
or installed. Set `CHROME_BIN` only when a different installed Chrome executable
must be selected.

## Commands

From the repository root:

```sh
# Tests of the assertion/parser/result tools, NOT a Unity gameplay run:
node --test tests/browser/lib.test.mjs
node tests/browser/probe.mjs

# Only after a real Unity build and Frontend HTTP/container host are ready:
node tests/browser/smoke.mjs --url http://127.0.0.1:8080/

# Optional long real-time four-quarter/final/restart observation:
node tests/browser/smoke.mjs --url http://127.0.0.1:8080/ --full-match
```

On this repository's `fuseblk` filesystem, use the Linux private-runtime wrapper:

```sh
bash tests/browser/with-posix-runtime.sh node --test tests/browser/lib.test.mjs tests/browser/runtime.test.mjs
bash tests/browser/with-posix-runtime.sh node tests/browser/probe.mjs
bash tests/browser/with-posix-runtime.sh node tests/browser/smoke.mjs --url http://127.0.0.1:8080/
bash tests/browser/with-posix-runtime.sh node tests/browser/smoke.mjs --url http://127.0.0.1:8080/ --full-match --match-timeout-ms 900000

# Targeted focus/input-release and portrait checks, not the full functional suite:
bash tests/browser/with-posix-runtime.sh node tests/browser/smoke.mjs --url http://127.0.0.1:8080/ --focus-layout-only

# Perspective-camera/source-player build: explicit bounded visual/controls subset:
bash tests/browser/with-posix-runtime.sh node tests/browser/smoke.mjs --url 'http://127.0.0.1:8080/?actors=source' --visual-controls-only --timeout-ms 60000

# Only AFTER coordinator promotes the QBSTANCE generation and provides readiness:
bash tests/browser/with-posix-runtime.sh node tests/browser/smoke.mjs --url 'http://127.0.0.1:8080/?actors=source' --visual-controls-only --expected-source-pose qbstance-frame0 --timeout-ms 60000

# Strict diagnostic (known small-font axis OCR limitation; see evidence below):
bash tests/browser/with-posix-runtime.sh node tests/browser/smoke.mjs --url 'http://127.0.0.1:8080/?actors=source' --visual-controls-only --expected-source-pose qbstance-frame0 --final-presentation --timeout-ms 60000

# Coordinator's separate static preview of the same real build (NOT Docker):
bash tests/browser/with-posix-runtime.sh node tests/browser/smoke.mjs --url http://127.0.0.1:18080/ --full-match --match-timeout-ms 900000
```

The wrapper requires the installed `unshare`, `mount` and `umount` tools and
enabled unprivileged user/mount namespaces; it installs nothing. It mounts
private `tmpfs` storage at one uniquely owned `tests/.artifacts/r-PID` path,
only inside the runner's namespace. The regular worktree is not remounted or
permission-modified. Screenshots and evidence remain on the regular filesystem.
Chrome runs with the exact `--no-sandbox` flag because the private user namespace
maps the launching user to UID 0. This is a local isolated-test setting, not a
recommended flag for an exposed browser service. Browser launch arguments are
recorded in the JSON evidence.

The default/full/focus commands retain the earlier orthographic field-offset
proof. They are **not perspective-camera focus-position qualification**. Use
the explicit visual/controls subset above for the current actor/camera build.
Its evidence lists perspective focus position, defense/scoring/full-match and
original standing/animation/fidelity as unqualified. Mutually exclusive subset
flags prevent accidentally claiming those checks passed.

The default artifact path is `Build/WebGL`; `--artifact-root` accepts another
repository-relative emitted player directory. `--timeout-ms 180000` controls
load readiness; `--match-timeout-ms 900000` bounds an optional full match.
No timescale, production field mutation, instant-win input, mocked canvas or
static hosting stub is used to shorten gameplay.

## Required readiness

1. Unity must actually emit `Build/WebGL/index.html` and a complete non-empty
   `Build/*.loader.js`, `*.framework.js`, `*.data`, `*.wasm` set. The script runs
   Frontend's `scripts/verify-webgl.sh` with hosting-stub permission disabled.
2. Frontend must serve that **same build** over HTTP, normally
   `http://127.0.0.1:8080/`. Run `bash scripts/serve-webgl.sh` separately as the
   foreground container service; the test does not build Unity or start a second
   host. A server must return successful real WASM/data requests, with WASM MIME
   `application/wasm`.
3. Chrome runtime storage must preserve owned private `0700` directory
   permissions; screenshots do **not** have that filesystem requirement.
   `CHROME_RUNTIME_ROOT` can select an existing real, non-symlinked runtime
   directory. The wrapper supplies private project-local POSIX storage when
   the underlying worktree discards permissions. Chrome profile, cache,
   configuration and socket directories use unique children of this root;
   `HOME`, `TMPDIR` and all XDG runtime/cache/config/data variables point only
   there, not at the interactive user's profile. An unavailable secure runtime
   still fails explicitly before Chrome starts.
4. The visual/controls subset pins the schema2 local receipt and generation
   proof, independently matches served receipt/proof bytes, and checks all four
   served payload SHA-256 values, sizes and MIME types. Local index is
   proof-pinned; served HTML intentionally has hosting-only CSS/favicon changes.
   Explicit source mode additionally requires its actual Unity console marker,
   never an assumed query effect or primitive fallback. Whitelisted actor mode
   is recorded separately while arbitrary URL queries remain redacted.
   `--expected-source-pose` is a test expectation, not a game-state/pose
   override: it defaults to `bind-pose`; `qbstance-frame0` requires its distinct
   actual runtime marker and writes accurately named `source-qbstance-frame0`
   PNGs. A bind marker cannot satisfy a posed expectation, or vice versa.
   A `pre-snap` PNG suffix describes the new kernel's gameplay phase only; it
   does not certify that static QBSTANCE frame0 is the original pre-snap pose.
   `--final-presentation` additionally verifies actual HUD/document title
   `NFL BLITZ 2000`, desktop subtitle `UNITY SOURCE PORT / IN PROGRESS`, and
   positively read `W = +X field` / `D = +Z field` legend on both viewports.
   Compact layout intentionally has no desktop subtitle. Old Yard Rush or
   camera-relative `(right)`/`(far side)` hints fail; no expected text is injected.

## What is asserted

- A real WebAssembly/data load, no HTTP/network/JavaScript/console errors, a
  visible non-clipped canvas at 1280x800 and 360x740, and decoded screenshot
  pixels containing a rendered field rather than just a loader.
- **Visible HUD** phase, possession, scores, control role, pass/catch notices and
  downstream transitions, read by OCR from screenshots of Unity's actual
  `PlayHud`. A private game-state/debug bridge is not required or injected.
  Scores are read from separate numeric `PlayHud.Score` rectangle crops using
  single-word OCR (`--psm 8`, digits only); colored team labels remain visible in
  the full screenshot. Actual white score-glyph bounds are whitespace-trimmed
  with padding, without changing/replacing a digit. Narrow header OCR uses a 2x
  image-only crop. A viewport-only resize must still read
  PlaySelect/Home/0–0/quarter1/2:00, rejecting false `90` or unreadable-quarter
  readings rather than assuming state. Header reading order can separate HOME/AWAY from their
  values, so generic header text and `AT HOME 20` cannot become a score.
  Static `HOME 0` mini-field labels cannot be misreported as the current score.
- Keyboard selection → pre-snap → snap → pass/catch → running/turbo → dead ball
  → next play, followed by real downs/possession change and Home defensive
  selection, automatic Away snap, Q defender switch, F tackle input and explicit
  R restart.
- A nonzero visible scoreboard from ordinary play, with at most two additional
  minutes of normal drives if the initial plays have not already scored.
- A real second headless tab hides/blurs a caught, sprinting receiver while
  W/Shift are held. Key releases go to that **active other tab's CDP session**,
  not to the hidden game (injecting them into the game can incorrectly focus
  it). At least 3.5 seconds genuinely hidden/unfocused must not become match
  time on return. The visible clock has one-second resolution. The real turbo
  bar must drain/recharge, and the released player must remain fixed on both
  axes relative to an OCR-readable static field landmark, not the following
  camera. An idle QB being naturally tackled is not a focus regression.
  Missing landmarks or an unobservable comparison fail explicitly.
- Optional `--full-match` continues ordinary real-time keyboard play until a
  visible final, then proves final restart. Score observation is recorded
  separately; a zero-zero final is a legitimate tie, not a score observation.
  Without this option, the report explicitly says full-match/final restart were
  not requested/not run.

The defensive smoke observes the controlled Home defender change and sends F;
an automatic contact tackle may terminate that play. Exact F-only reach behavior
is separately asserted by the actual-source core tests, not falsely attributed
to a browser key dispatch alone.

Screenshots, OCR text and `evidence.json` stay in ignored
`tests/browser/artifacts/`. Chrome processes launched by the driver are stopped
in `finally` before deleting their own exact runtime directories. Cleanup checks
the root inode/device/owner and refuses a replaced or symlinked root; child
symlinks are unlinked, not traversed. No other profile/process is inspected or
deleted. Errors and unreadable HUD fields fail rather than silently skipping a
required check. Image names include scenario and viewport; observations include
the screenshot path, decoded pixel statistics and OCR landmark coordinates.
When an early resource error prevents the normal load check, a failed run can
spend at most 15 additional seconds collecting read-only render diagnostics and
a narrow-viewport image. The original error, exit 1 and failed status are
preserved; diagnostic screenshots are not passed gameplay assertions.
All HUD/score/clock crops come from the **same PNG**. Slow multi-frame
screenshot/OCR chains must not mix 2:00 and a later 1:59 frame or delay a pass
until after a tackle. Snap/pass and live Q/F actions are dispatched around the
atomic capture before expensive full-image OCR. A Q observation must itself
still be LiveBall/Away possession; a dead-ball reset cannot prove a switch.
Hidden Unity rendering may not produce a CDP screenshot at all, so the focus
check captures before hiding and after returning, never demands a hidden frame.

The cheap performance sample measures page `requestAnimationFrame` callback
intervals while the loaded game displays PlaySelect; it is not an instrumented
Unity FPS measurement or performance-budget verdict. Chrome JS heap values
exclude total process/GPU/WASM memory. No game telemetry/debug API is injected.

## Current evidence boundary

### Final NFL title/world-field-legend generation

`game-1792119716569/evidence.json` records **six strict source/runtime/controls
checks passed** in31.634s on actual final `8080`, zero runtime/asset errors.
Served receipt/proof/four payloads match build
`00000688000072cf00002aa300002ef9`, proof SHA-256
`d2749c8df0f21c069142f35e0d86c96af1ec32f25dcf28fe6874cb384d1a9a34`.
The exact QBSTANCE marker and actual upright source meshes remain verified.

Current clean desktop/portrait/live/dead/restart PNGs, hashes/pixel/HUD manifest
and **explicit manual presentation review** are in that directory. The viewed
current images show `NFL BLITZ 2000`, desktop
`UNITY SOURCE PORT / IN PROGRESS`, and `W = +X field / D = +Z field`;
CDP document title is `Unity Web Player | NFL Blitz 2000 - Unity Port`.
There is no old-title/candidate PNG substitution.

**Do not report the exact-text presentation OCR gate as passed.**
`game-1792119015841/evidence.json` remains **failed**: small muted `+X/+Z field`
pixels were OCR-read as `4%/+2` and `fled` despite correct visible text.
Bounded crop/contrast alternatives were retained, not substituted as invented
text. The final six-check run omits this optional diagnostic flag and uses
manual PNG review for branding/axes, recorded in
`manual-presentation-review.json`. All runtime/network/asset-error, marker,
generation, fresh-HUD and actual control gates remain unchanged and strict.

A second run `game-1792119578019` correctly exposed a parser defect, not a
gameplay reset: world `FIRST DOWN` followed by the `1 RUN` card overrode the
actual header `2nd & 20`. Down now comes from the same atomic scoreboard crop
as clock/possession. Actual-image regression evidence is retained there;
**32/32 tooling tests pass**, zero skipped. The final run observed actual
catch/out-of-bounds/down2→Continue/down2→R/down1/Q1/2:00.

This remains one-variant **static QBSTANCE frame0**, not continuous animation,
verified original pre-snap/fidelity, perspective focus-position or full-match
acceptance. Final images stay ignored/local; no publication performed.

### Current promoted static QBSTANCE frame0

`game-1792117872816/evidence.json` independently qualifies actual promoted
`8080/?actors=source` using `--expected-source-pose qbstance-frame0`:

- **Six bounded checks passed** in25.939s; zero runtime/JS/network/asset errors.
- Served receipt/proof and four payload hashes/size/MIME match build
  `0000067c000032dd00007938000039fe`, proof SHA-256
  `d616d87b6fb063be557eabe58cc2eeed6f51131981f78bd8af85bc2ce234f9c4`.
- Actual marker is the distinct QBSTANCE-frame0/static/no-continuous-animation/
  not-verified-pre-snap message. A stale bind marker cannot pass this check.
- Current `8080` desktop1280x800 and portrait360x740 PNGs were visually viewed:
  original textured helmets/jerseys, head upright and shoes below the body.
  No old bind or isolated8183 PNG is substituted.
- Portrait fresh state reads Home/0–0/Q1/2:00 and required controls. Real keys
  select→snap/live→target1 caught notice/out-of-bounds/down2→continue→R/down1.
  Caught is observed on the terminal frame, not a claimed catch animation.

Use the clean `source-qbstance-frame0-*.png` files plus `screenshots.json`
under `tests/browser/artifacts/game-1792117872816/`. The manifest binds every
alias byte-for-byte to a captured frame and records SHA/pixel/HUD statistics.
All artifacts remain ignored local preview; no publication was performed.

This is **static original QBSTANCE frame0**, applied to all fourteen actors
using one Bears variant. It is not continuous animation, verified original
pre-snap/idle/universal pose semantics, team/jersey variants or full fidelity
approval. The perspective field-offset focus proof and browser full match are
explicitly unrun/unqualified. Coordinator subsequently reports the public
source-only assets CI gap closed:2751 assertions(original419+2332pose),8mutants,
seven-file hash guard, no original pose data in fixtures. That is separate
Quality/CI evidence, not this browser run or the30 tool tests' numeric coverage.

Measured53 page rAF intervals: mean95.279ms, p95 116.700ms,
about10.495 callbacks/s under SwiftShader; Chrome JS heap13,042,818 bytes.
Software-WebGL/persistent-path deprecations and ReadPixels warnings are recorded.
These are not Unity FPS, total memory or performance-budget claims.

### Source-player perspective snapshot, identity bind pose

`game-1792116188060/evidence.json` records the **passed six-check
actor-visual-controls subset** on actual Docker `8080/?actors=source`:

- Receipt/proof and all served payload hashes match build
  `00002b790000553000005d2b00000d97`; proof SHA-256 is
  `ee310431eb6ad4085e28b44e6c5d5008f0bae3fdadecf0f258bd3edac44eab58`.
- Actual marker: `Actor mode: local source mesh/textures, bind-pose-only.`
- Genuine textured helmet/uniform meshes were visually reviewed at 1280x800
  desktop and 360x740 portrait. Portrait controls/HUD read true
  PlaySelect/Home/0–0/Q1/2:00.
- Keyboard select → pre-snap → snap/live → target1 caught notice → running and
  out-of-bounds dead ball → next play/down2 → R reset/down1/Q1/2:00.
  The catch notice was captured on the terminal frame, not a claimed live
  catch-animation frame.
- No runtime/JS/network/asset errors. Recorded warnings are software-WebGL and
  Unity persistent-path deprecations plus screenshot ReadPixels stalls.
  Error-level entries remain fatal; no unsafe-SwiftShader flag was added.
- **30/30 browser-tool unit tests passed**, zero skipped.

Clean PNGs and `screenshots.json` (exact captured-frame identity, hashes,
decoded pixel statistics and semantic HUD state) are under
`tests/browser/artifacts/game-1792116188060/`.

This is an ignored **local bind-pose preview**, not published art or accepted
fidelity. All fourteen actors use one Bears variant with identity-pose
raised/upward limbs/feet—not standing or original animation. Perspective
focus-position geometry, full match, team variants, jersey overlays and
stadium/HUD parity remain unqualified. This older snapshot does not cover the
subsequent QBSTANCE-frame0 generation independently qualified above.

Initial source run `game-1792115687211` incorrectly accepted Away `90` despite
visible `00`; its portrait semantic pass is invalidated. The real-crop
regression is retained there. `game-1792116029988` correctly failed the tighter
guard for tiny-font `QUARRER`/unreadable quarter; its 2x actual-header regression
is retained. The final run passes stricter checks with actual `00` and
`QUARTER 1`. Original evidence/PNGs were not rewritten to hide either defect.

Latest sample: 52 page rAF intervals, mean96.790ms, p95 100.100ms,
about10.332 callbacks/s under ANGLE Vulkan SwiftShader; JS heap11,357,900 bytes.
This is not Unity FPS, total process/WASM/GPU memory, representative hardware
performance or a performance-budget verdict.

### Historical early graybox/orthographic observations

On October 15, 2026, actual Chrome 155.0.8059.79 loaded the genuine Unity player
on **Docker `http://127.0.0.1:8080/`**, with no runtime/network/console errors.
Twenty-five tooling tests pass. The earlier Python `18080` favicon/narrow
failures remain historical evidence, not verdicts on the fixed container.
Frontend persistently fixed favicon delivery and portrait CSS without changing
the Unity payload or suppressing errors.

Observed ordinary gameplay on the immutable pre-HUD-rebuild player:

- Selection → snap → actual caught Home pass/control WR1 → run/dead ball/next.
- Downs transfer possession to Away; Home defensive Q changes DEF2 to DEF1
  while still live. F is dispatched live; terminal automatic contact is not
  falsely attributed solely to F.
- Away touchdown, visible 0–6 scoreboard and R reset to 0–0.
- Real 3,994ms hidden/unfocused interval: clock 1:56 before and after resume.
  Turbo bar drains from 0.734 to 0.135 while sprinting, then recharges from
  0.201 to 0.311 after active-tab key release.
- Portrait canvas now actually fills 360x740; readable-legend assertions remain
  downstream of the pending strict movement-label assertion.

The current focused run **still fails**, not green: the old clipped/wrapped
field player label is not OCR-readable for the movement-release comparison.
Systems' source fix is coordinator-reported ready; its new WebGL/Docker payload
refresh awaits Frontend's incremental-build freshness fix. No additional
ordinary gameplay defect is asserted from the earlier test-fixture timing,
incorrect background key routing, or hidden-screenshot transport failures.

Useful ignored artifacts:

- `game-1792109944467/evidence.json`: actual Docker gameplay, live Q/F and score.
- `game-1792109944467/0010-pass-catch-1280x800.png`: real caught Home pass.
- `game-1792109944467/0030-defensive-play-dead-1280x800.png`: real 0–6 score.
- `game-1792110862494/evidence.json`: explicit focus/layout subset, clock/turbo
  checks passed, movement-label assertion failed.
- `game-1792110862494/0014-viewport-360x740-360x740.png`: actual portrait game.

The loaded page measured about 8.9–9.5 rAF callbacks/s under ANGLE Vulkan
SwiftShader here. This is not instrumented Unity FPS, a hardware-browser result
or a performance-budget verdict. The latest reported JS heap was 10,893,109
bytes, excluding total process/GPU/WASM memory.

Earlier `game-1792108729937` `homeScore:20`/`scoreObserved:true` fields are invalid
OCR of **AT HOME 20**. Earlier `game-1792109456174` Q proof is also invalid where
the observation was already DeadBall; the newer live-only check fixes that.
Original failed evidence is retained, not rewritten. No completed browser
four-quarter/full-match claim exists. Screenshots document the current technical
prototype baseline, **not accepted NFL Blitz 2000 visual/behavioral fidelity**.
