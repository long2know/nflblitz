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

The default artifact path is `Build/WebGL`; `--artifact-root` accepts another
repository-relative emitted player directory. `--timeout-ms 180000` controls
load readiness; `--match-timeout-ms 1800000` bounds an optional full match.
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
3. The worktree filesystem must preserve private `0700` directory permissions.
   Chrome's process-singleton socket cannot start safely on a filesystem that
   reports every created directory as `0777`. The harness detects this and fails
   explicitly; it never redirects caches/profiles/sockets to outside-repository
   directories as a workaround. Use an approved POSIX-permission worktree or
   execution environment for the eventual run.

## What is asserted

- A real WebAssembly/data load, no HTTP/network/JavaScript/console errors, a
  visible non-clipped canvas at 1280x800 and 360x740, and decoded screenshot
  pixels containing a rendered field rather than just a loader.
- **Visible HUD** phase, possession, scores, control role, pass/catch notices and
  downstream transitions, read by OCR from screenshots of Unity's actual
  `PlayHud`. A private game-state/debug bridge is not required or injected.
  Scores are read from a separate header crop; static `HOME 0` mini-field labels
  cannot be misreported as the current score.
- Keyboard selection → pre-snap → snap → pass/catch → running/turbo → dead ball
  → next play, followed by real downs/possession change and Home defensive
  selection, automatic Away snap, Q defender switch, F tackle input and explicit
  R restart.
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
in `finally`; only their own profiles/cache directories are removed. Errors and
unreadable HUD fields fail rather than silently skipping a required check.

## Current evidence boundary

As of October 15, 2026, browser-tool/parser unit tests ran, but no emitted
`Build/WebGL` player was available for this task. The game smoke rejected the
missing artifact with exit 1. The repository is on `fuseblk`, and an owned
directory still reported `0777` after `chmod 0700`; the transport probe now
reports this independent Chrome environment blocker before launch. There is
**no loader-only screenshot, Unity gameplay screenshot, successful container
game run or final-match browser claim** in this handoff. OCR integration remains
to be validated on the first real rendered build.
